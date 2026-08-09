namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using VTR;

/// The named compute phases of one completed Cortex step. `Residual` is not a phase:
/// it is the explicit conservation bucket for clock-resolution and orchestration gaps.
internal enum CortexComputeSegmentKinds : byte
{
    Prelude,
    Induce,
    Harvest,
    Generate,
    Read,
    Model,
    Orchestration,
    Input,
    Action,
    Sleep,
    Report,
    Verifier,
    PolicyBoundary,
}

internal readonly record struct CortexComputeSegmentReading(CortexComputeSegmentKinds Kind, double WallMilliseconds)
{
    public string Name => CortexComputeAccounting.NameOf(Kind);
}

[InlineArray(13)]
internal struct CortexComputeSegmentBuffer
{
    private CortexComputeSegmentReading _element0;
}

[InlineArray(13)]
internal struct CortexComputeTickBuffer
{
    private long _element0;
}

/// One append-only compute receipt. Wall time is telemetry only; no Cortex decision reads it.
/// The fixed segment order makes a row inspectable and gives the verifier a closed vocabulary.
internal sealed class CortexComputeRecord
{
    public const double ConservationToleranceMilliseconds = 0.02;
    public const double RawTimingToleranceMilliseconds = 0.02;
    public int Step { get; }
    public long StopwatchFrequency { get; }
    public long TotalRawTicks { get; }
    public double TotalWallMilliseconds { get; }
    private CortexComputeSegmentBuffer _segments;
    public ReadOnlySpan<CortexComputeSegmentReading> Segments => _segments;
    private CortexComputeTickBuffer _segmentTicks;
    public ReadOnlySpan<long> SegmentRawTicks => _segmentTicks;
    public long ResidualRawTicks { get; }
    public double ResidualWallMilliseconds { get; }
    public string Digest { get; }
    private readonly bool _legacySchema;

    [ThreadStatic] private static StringBuilder? _tsvScratch;
    [ThreadStatic] private static StringBuilder? _canonicalScratch;
    [ThreadStatic] private static char[]? _canonicalChars;
    [ThreadStatic] private static byte[]? _canonicalBytes;
    [ThreadStatic] private static byte[]? _digestBytes;
    [ThreadStatic] private static char[]? _digestHex;

    internal CortexComputeRecord(
        int step,
        long stopwatchFrequency,
        long totalRawTicks,
        double totalWallMilliseconds,
        ReadOnlySpan<CortexComputeSegmentReading> segments,
        ReadOnlySpan<long> segmentRawTicks,
        long residualRawTicks,
        double residualWallMilliseconds,
        string digest = "")
    {
        if (segments.Length != CortexComputeAccounting.NamedSegmentCount)
            throw new ArgumentException("compute records require the fixed named segment count", nameof(segments));
        if (segmentRawTicks.Length != CortexComputeAccounting.NamedSegmentCount)
            throw new ArgumentException("compute records require the fixed named raw-tick count", nameof(segmentRawTicks));
        Step = step;
        StopwatchFrequency = stopwatchFrequency;
        TotalRawTicks = totalRawTicks;
        TotalWallMilliseconds = totalWallMilliseconds;
        _segments = default;
        for (int i = 0; i < segments.Length; i++) _segments[i] = segments[i];
        _segmentTicks = default;
        for (int i = 0; i < segmentRawTicks.Length; i++) _segmentTicks[i] = segmentRawTicks[i];
        ResidualRawTicks = residualRawTicks;
        ResidualWallMilliseconds = residualWallMilliseconds;
        Digest = digest;
        _legacySchema = false;
    }

    // Historical fixtures and the retired composite verifier still need to read the old millisecond-only shape.
    // New run rows always use the tick-bearing constructor above.
    internal CortexComputeRecord(
        int step,
        double totalWallMilliseconds,
        ReadOnlySpan<CortexComputeSegmentReading> segments,
        double residualWallMilliseconds,
        string digest = "",
        bool legacySchema = false)
    {
        if (segments.Length != CortexComputeAccounting.NamedSegmentCount)
            throw new ArgumentException("compute records require the fixed named segment count", nameof(segments));
        Step = step;
        StopwatchFrequency = 0;
        TotalRawTicks = 0;
        TotalWallMilliseconds = totalWallMilliseconds;
        _segments = default;
        for (int i = 0; i < segments.Length; i++) _segments[i] = segments[i];
        _segmentTicks = default;
        ResidualRawTicks = 0;
        ResidualWallMilliseconds = residualWallMilliseconds;
        Digest = digest;
        _legacySchema = legacySchema;
    }

    public double Segment(CortexComputeSegmentKinds kind)
    {
        for (int i = 0; i < Segments.Length; i++)
            if (Segments[i].Kind == kind) return Segments[i].WallMilliseconds;
        return double.NaN;
    }

    public string ToTsv()
    {
        StringBuilder line = _tsvScratch ??= new StringBuilder(256);
        line.Clear();
        AppendInvariant(line, Step);
        line.Append('\t'); AppendInvariant(line, StopwatchFrequency);
        line.Append('\t'); AppendInvariant(line, TotalRawTicks);
        line.Append('\t'); AppendInvariant(line, TotalWallMilliseconds);
        for (int i = 0; i < CortexComputeAccounting.NamedSegmentCount; i++)
        {
            line.Append('\t'); AppendInvariant(line, Segments[i].WallMilliseconds);
        }
        for (int i = 0; i < CortexComputeAccounting.NamedSegmentCount; i++)
        {
            line.Append('\t'); AppendInvariant(line, SegmentRawTicks[i]);
        }
        line.Append('\t'); AppendInvariant(line, ResidualRawTicks);
        line.Append('\t'); AppendInvariant(line, ResidualWallMilliseconds);
        line.Append('\t').Append(string.IsNullOrEmpty(Digest) ? ComputeDigest() : Digest);
        return line.ToString();
    }

    public string ComputeDigest() => _legacySchema
        ? ComputeLegacyDigest(Step, TotalWallMilliseconds, Segments, ResidualWallMilliseconds)
        : ComputeDigest(Step, StopwatchFrequency, TotalRawTicks, TotalWallMilliseconds, Segments, SegmentRawTicks, ResidualRawTicks, ResidualWallMilliseconds);

    internal static string ComputeDigest(int step, long stopwatchFrequency, long totalRawTicks, double totalWallMilliseconds,
        ReadOnlySpan<CortexComputeSegmentReading> segments, ReadOnlySpan<long> segmentRawTicks,
        long residualRawTicks, double residualWallMilliseconds)
        => new(ComputeDigestHex(step, stopwatchFrequency, totalRawTicks, totalWallMilliseconds, segments, segmentRawTicks, residualRawTicks, residualWallMilliseconds));

    /// The digest core — canonical row + SHA256 + lowercase hex, all in thread-cached scratch. The
    /// returned span aliases that scratch and is valid until the next digest on the same thread; the
    /// hot per-step row (CortexComputeAccounting.CompleteRow) appends it without minting a string.
    internal static ReadOnlySpan<char> ComputeDigestHex(int step, long stopwatchFrequency, long totalRawTicks, double totalWallMilliseconds,
        ReadOnlySpan<CortexComputeSegmentReading> segments, ReadOnlySpan<long> segmentRawTicks,
        long residualRawTicks, double residualWallMilliseconds)
    {
        StringBuilder canonical = _canonicalScratch ??= new StringBuilder(256);
        canonical.Clear();
        AppendInvariant(canonical, step);
        canonical.Append("|frequency="); AppendInvariant(canonical, stopwatchFrequency);
        canonical.Append("|total_ticks="); AppendInvariant(canonical, totalRawTicks);
        canonical.Append("|total_ms="); AppendInvariant(canonical, totalWallMilliseconds);
        for (int i = 0; i < segments.Length; i++)
        {
            canonical.Append('|').Append(CortexComputeAccounting.NameOf(segments[i].Kind)).Append("_ticks=");
            AppendInvariant(canonical, segmentRawTicks[i]);
            canonical.Append('|').Append(CortexComputeAccounting.NameOf(segments[i].Kind)).Append("_ms=");
            AppendInvariant(canonical, segments[i].WallMilliseconds);
        }
        canonical.Append("|residual_ticks="); AppendInvariant(canonical, residualRawTicks);
        canonical.Append("|residual_ms="); AppendInvariant(canonical, residualWallMilliseconds);
        EnsureEncodingScratch(canonical.Length);
        canonical.CopyTo(0, _canonicalChars!, 0, canonical.Length);
        int byteCount = Encoding.UTF8.GetBytes(_canonicalChars!, 0, canonical.Length, _canonicalBytes!, 0);
        SHA256.HashData(_canonicalBytes.AsSpan(0, byteCount), _digestBytes!);
        char[] hex = _digestHex ??= new char[SHA256.HashSizeInBytes * 2];
        for (int i = 0; i < SHA256.HashSizeInBytes; i++)
        {
            hex[2 * i] = HexLower(_digestBytes![i] >> 4);
            hex[2 * i + 1] = HexLower(_digestBytes[i] & 0xF);
        }
        return hex;
    }

    private static char HexLower(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    private static string ComputeLegacyDigest(int step, double totalWallMilliseconds,
        ReadOnlySpan<CortexComputeSegmentReading> segments, double residualWallMilliseconds)
    {
        StringBuilder canonical = _canonicalScratch ??= new StringBuilder(256);
        canonical.Clear();
        AppendInvariant(canonical, step);
        canonical.Append('|'); AppendInvariant(canonical, totalWallMilliseconds);
        for (int i = 0; i < segments.Length; i++)
        {
            canonical.Append('|').Append(CortexComputeAccounting.NameOf(segments[i].Kind)).Append('=');
            AppendInvariant(canonical, segments[i].WallMilliseconds);
        }
        canonical.Append("|residual="); AppendInvariant(canonical, residualWallMilliseconds);
        EnsureEncodingScratch(canonical.Length);
        canonical.CopyTo(0, _canonicalChars!, 0, canonical.Length);
        int byteCount = Encoding.UTF8.GetBytes(_canonicalChars!, 0, canonical.Length, _canonicalBytes!, 0);
        SHA256.HashData(_canonicalBytes.AsSpan(0, byteCount), _digestBytes!);
        return Convert.ToHexStringLower(_digestBytes!);
    }

    internal static void AppendInvariant(StringBuilder target, int value)
    {
        Span<char> chars = stackalloc char[16];
        value.TryFormat(chars, out int written, provider: CultureInfo.InvariantCulture);
        target.Append(chars[..written]);
    }

    internal static void AppendInvariant(StringBuilder target, long value)
    {
        Span<char> chars = stackalloc char[32];
        value.TryFormat(chars, out int written, provider: CultureInfo.InvariantCulture);
        target.Append(chars[..written]);
    }

    internal static void AppendInvariant(StringBuilder target, double value)
    {
        Span<char> chars = stackalloc char[32];
        value.TryFormat(chars, out int written, "F6", CultureInfo.InvariantCulture);
        target.Append(chars[..written]);
    }

    private static void EnsureEncodingScratch(int chars)
    {
        if (_canonicalChars is null || _canonicalChars.Length < chars) _canonicalChars = new char[Math.Max(chars, 256)];
        int bytes = Encoding.UTF8.GetMaxByteCount(chars);
        if (_canonicalBytes is null || _canonicalBytes.Length < bytes) _canonicalBytes = new byte[Math.Max(bytes, 512)];
        _digestBytes ??= new byte[SHA256.HashSizeInBytes];
    }
}

internal sealed class CortexComputeAccounting
{
    public const int NamedSegmentCount = 13;
    public const string LegacyHeader = "step\tstep_wall_ms\tprelude_ms\tinduce_ms\tharvest_ms\tgenerate_ms\tread_ms\tmodel_ms\torchestration_ms\tinput_ms\taction_ms\tsleep_ms\treport_ms\tverifier_ms\tresidual_ms\tdigest";
    public const string Header = "step\tstopwatch_frequency\ttotal_raw_ticks\tstep_wall_ms\tprelude_ms\tinduce_ms\tharvest_ms\tgenerate_ms\tread_ms\tmodel_ms\torchestration_ms\tinput_ms\taction_ms\tsleep_ms\treport_ms\tverifier_ms\tpolicy_boundary_ms\tprelude_raw_ticks\tinduce_raw_ticks\tharvest_raw_ticks\tgenerate_raw_ticks\tread_raw_ticks\tmodel_raw_ticks\torchestration_raw_ticks\tinput_raw_ticks\taction_raw_ticks\tsleep_raw_ticks\treport_raw_ticks\tverifier_raw_ticks\tpolicy_boundary_raw_ticks\tresidual_raw_ticks\tresidual_ms\tdigest";

    private CortexComputeTickBuffer _segmentTicks;
    private readonly long _totalStarted;
    private long _segmentStarted;
    private CortexComputeSegmentKinds _active;
    private bool _started;
    public bool Completed { get; private set; }

    public CortexComputeAccounting(long startTicks)
    {
        _totalStarted = startTicks;
        _segmentStarted = startTicks;
        _active = CortexComputeSegmentKinds.Prelude;
        _started = true;
    }

    public void Advance(CortexComputeSegmentKinds next, long nowTicks)
    {
        if (!_started) throw new InvalidOperationException("compute accounting has not started");
        if (nowTicks < _segmentStarted) throw new InvalidOperationException("compute clock moved backwards");
        _segmentTicks[(int)_active] += nowTicks - _segmentStarted;
        _segmentStarted = nowTicks;
        _active = next;
    }

    public CortexComputeRecord Complete(int step, long endTicks)
    {
        CortexComputeSegmentBuffer segments = default;
        (long totalTicks, double totalMilliseconds, long residualTicks, double residualMilliseconds) = Close(endTicks, ref segments);
        ReadOnlySpan<long> segmentRawTicks = _segmentTicks;
        string digest = CortexComputeRecord.ComputeDigest(step, TraceClock.Frequency, totalTicks, totalMilliseconds, segments, segmentRawTicks, residualTicks, residualMilliseconds);
        CortexComputeRecord completed = new(step, TraceClock.Frequency, totalTicks, totalMilliseconds, segments, segmentRawTicks, residualTicks, residualMilliseconds, digest);
        Completed = true;
        return completed;
    }

    /// The hot-path completion — the row Complete().ToTsv() would produce, byte-identical (same fields,
    /// same digest), with zero per-step allocation: the record object, the raw-tick copy, the digest
    /// string, and the TSV string all collapse into caller/thread-owned scratch. Returns the step's
    /// total wall for the slow-step tripwire.
    public double CompleteRow(int step, long endTicks, StringBuilder row)
    {
        CortexComputeSegmentBuffer segments = default;
        (long totalTicks, double totalMilliseconds, long residualTicks, double residualMilliseconds) = Close(endTicks, ref segments);
        ReadOnlySpan<long> segmentRawTicks = _segmentTicks;
        row.Clear();
        CortexComputeRecord.AppendInvariant(row, step);
        row.Append('\t'); CortexComputeRecord.AppendInvariant(row, TraceClock.Frequency);
        row.Append('\t'); CortexComputeRecord.AppendInvariant(row, totalTicks);
        row.Append('\t'); CortexComputeRecord.AppendInvariant(row, totalMilliseconds);
        for (int i = 0; i < NamedSegmentCount; i++) { row.Append('\t'); CortexComputeRecord.AppendInvariant(row, segments[i].WallMilliseconds); }
        for (int i = 0; i < NamedSegmentCount; i++) { row.Append('\t'); CortexComputeRecord.AppendInvariant(row, segmentRawTicks[i]); }
        row.Append('\t'); CortexComputeRecord.AppendInvariant(row, residualTicks);
        row.Append('\t'); CortexComputeRecord.AppendInvariant(row, residualMilliseconds);
        row.Append('\t').Append(CortexComputeRecord.ComputeDigestHex(step, TraceClock.Frequency, totalTicks, totalMilliseconds, segments, segmentRawTicks, residualTicks, residualMilliseconds));
        Completed = true;
        return totalMilliseconds;
    }

    private (long TotalTicks, double TotalMs, long ResidualTicks, double ResidualMs) Close(long endTicks, ref CortexComputeSegmentBuffer segments)
    {
        if (!_started) throw new InvalidOperationException("compute accounting has not started");
        if (endTicks < _segmentStarted) throw new InvalidOperationException("compute clock moved backwards");
        _segmentTicks[(int)_active] += endTicks - _segmentStarted;
        long namedTicks = 0;
        for (int i = 0; i < NamedSegmentCount; i++)
        {
            long segmentTicks = _segmentTicks[i];
            namedTicks += segmentTicks;
            segments[i] = new CortexComputeSegmentReading((CortexComputeSegmentKinds)i, Trace.ElapsedMsPrecise(0, segmentTicks));
        }
        // Independent whole-step measurement. Named phases are accumulated separately; any
        // interval not covered by them survives as the explicit residual below.
        double totalMilliseconds = Trace.ElapsedMsPrecise(_totalStarted, endTicks);
        long totalTicks = endTicks - _totalStarted;
        long residualTicks = totalTicks - namedTicks;
        return (totalTicks, totalMilliseconds, residualTicks, Trace.ElapsedMsPrecise(0, residualTicks));
    }

    public static string NameOf(CortexComputeSegmentKinds kind) => kind switch
    {
        CortexComputeSegmentKinds.Prelude => "prelude",
        CortexComputeSegmentKinds.Induce => "induce",
        CortexComputeSegmentKinds.Harvest => "harvest",
        CortexComputeSegmentKinds.Generate => "generate",
        CortexComputeSegmentKinds.Read => "read",
        CortexComputeSegmentKinds.Model => "model",
        CortexComputeSegmentKinds.Orchestration => "orchestration",
        CortexComputeSegmentKinds.Input => "input",
        CortexComputeSegmentKinds.Action => "action",
        CortexComputeSegmentKinds.Sleep => "sleep",
        CortexComputeSegmentKinds.Report => "report",
        CortexComputeSegmentKinds.Verifier => "verifier",
        CortexComputeSegmentKinds.PolicyBoundary => "policy_boundary",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static void EnsureHeader(string path)
    {
        if (!File.Exists(path)) { File.WriteAllText(path, Header + Environment.NewLine); return; }
        string first = File.ReadLines(path).FirstOrDefault() ?? "";
        if (string.Equals(first.TrimStart('\uFEFF'), Header, StringComparison.Ordinal)) return;
        throw new InvalidDataException("compute.tsv is not the current raw-tick schema; historical rows belong to the legacy verifier and cannot be appended to a standard run");
    }

    internal static bool TryParse(string line, string[] header, out CortexComputeRecord? record)
    {
        record = null;
        string[] fields = line.Split('\t');
        if (header.Length < 2 || fields.Length != header.Length) return false;
        int stepColumn = Array.IndexOf(header, "step");
        int totalColumn = Array.IndexOf(header, "step_wall_ms");
        int residualColumn = Array.IndexOf(header, "residual_ms");
        if (stepColumn < 0 || totalColumn < 0 || residualColumn < 0
            || !int.TryParse(fields[stepColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
            || !double.TryParse(fields[totalColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
            || !double.TryParse(fields[residualColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out double residual)) return false;
        bool legacy = Array.IndexOf(header, "stopwatch_frequency") < 0;
        if (!legacy)
        {
            int frequencyColumn = Array.IndexOf(header, "stopwatch_frequency");
            int totalTicksColumn = Array.IndexOf(header, "total_raw_ticks");
            int residualTicksColumn = Array.IndexOf(header, "residual_raw_ticks");
            if (frequencyColumn < 0 || totalTicksColumn < 0 || residualTicksColumn < 0
                || !long.TryParse(fields[frequencyColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out long frequency)
                || !long.TryParse(fields[totalTicksColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out long totalTicks)
                || !long.TryParse(fields[residualTicksColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out long residualTicks)) return false;
            CortexComputeSegmentReading[] tickSegments = new CortexComputeSegmentReading[NamedSegmentCount];
            long[] ticks = new long[NamedSegmentCount];
            for (int i = 0; i < NamedSegmentCount; i++)
            {
                int column = Array.IndexOf(header, NameOf((CortexComputeSegmentKinds)i) + "_ms");
                int tickColumn = Array.IndexOf(header, NameOf((CortexComputeSegmentKinds)i) + "_raw_ticks");
                if (column < 0 || tickColumn < 0
                    || !double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    || !long.TryParse(fields[tickColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks[i])) return false;
                tickSegments[i] = new CortexComputeSegmentReading((CortexComputeSegmentKinds)i, value);
            }
            string currentDigest = fields[Array.IndexOf(header, "digest")];
            record = new CortexComputeRecord(step, frequency, totalTicks, total, tickSegments, ticks, residualTicks, residual, currentDigest);
            return true;
        }
        CortexComputeSegmentReading[] segments = new CortexComputeSegmentReading[NamedSegmentCount];
        legacy = true;
        for (int i = 0; i < NamedSegmentCount; i++)
        {
            int column = Array.IndexOf(header, NameOf((CortexComputeSegmentKinds)i) + "_ms");
            if (column < 0)
            {
                if ((CortexComputeSegmentKinds)i != CortexComputeSegmentKinds.PolicyBoundary) return false;
                segments[i] = new CortexComputeSegmentReading((CortexComputeSegmentKinds)i, 0);
                continue;
            }
            if (!double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
            segments[i] = new CortexComputeSegmentReading((CortexComputeSegmentKinds)i, value);
        }
        int digestColumn = Array.IndexOf(header, "digest");
        string digest = digestColumn >= 0 ? fields[digestColumn] : "";
        record = new CortexComputeRecord(step, total, segments, residual, digest, legacy);
        return true;
    }
}

internal readonly record struct CortexComputeOccurrenceCheck(bool Passed, string[] Failures)
{
    public string Summary => Passed ? "PASS" : "FAIL: " + string.Join("; ", Failures);
}

internal static class CortexComputeAccountingVerifier
{
    public static CortexComputeOccurrenceCheck Verify(CortexComputeRecord record, bool requireZeroDark = false)
    {
        List<string> failures = new();
        if (record.Step < 0) failures.Add("negative step");
        if (!double.IsFinite(record.TotalWallMilliseconds) || record.TotalWallMilliseconds < 0)
            failures.Add("total wall is negative or non-finite");
        if (record.Segments.Length != CortexComputeAccounting.NamedSegmentCount)
            failures.Add($"segment count {record.Segments.Length} != {CortexComputeAccounting.NamedSegmentCount}");
        bool legacy = record.StopwatchFrequency <= 0;
        if (!legacy && record.StopwatchFrequency <= 0) failures.Add("stopwatch frequency is not positive");
        if (!legacy && record.TotalRawTicks < 0) failures.Add("total raw ticks are negative");
        long namedTicks = 0;
        double named = 0;
        for (int i = 0; i < record.Segments.Length; i++)
        {
            CortexComputeSegmentReading segment = record.Segments[i];
            if ((int)segment.Kind != i) failures.Add($"segment {i} is {segment.Kind}, expected {(CortexComputeSegmentKinds)i}");
            if (!double.IsFinite(segment.WallMilliseconds) || segment.WallMilliseconds < 0)
                failures.Add($"segment {segment.Kind} is negative or non-finite");
            else named += segment.WallMilliseconds;
            if (!legacy)
            {
                long ticks = record.SegmentRawTicks[i];
                if (ticks < 0) failures.Add($"segment {segment.Kind} raw ticks are negative");
                else namedTicks = TryAdd(namedTicks, ticks, out long sum) ? sum : long.MaxValue;
                if (record.StopwatchFrequency > 0 && Math.Abs(segment.WallMilliseconds - ticks * 1000d / record.StopwatchFrequency) > CortexComputeRecord.RawTimingToleranceMilliseconds)
                    failures.Add($"segment {segment.Kind} milliseconds disagree with raw ticks");
            }
        }
        if (!double.IsFinite(record.ResidualWallMilliseconds) || record.ResidualWallMilliseconds < -CortexComputeRecord.ConservationToleranceMilliseconds)
            failures.Add("residual is negative or non-finite");
        double residual = double.IsFinite(record.ResidualWallMilliseconds) ? record.ResidualWallMilliseconds : 0;
        if (Math.Abs(record.TotalWallMilliseconds - (named + residual)) > CortexComputeRecord.ConservationToleranceMilliseconds)
            failures.Add("total wall does not equal named segments plus residual");
        if (!legacy)
        {
            if (record.ResidualRawTicks < 0) failures.Add("residual raw ticks are negative");
            if (!TryAdd(namedTicks, record.ResidualRawTicks, out long conservedTicks) || conservedTicks != record.TotalRawTicks)
                failures.Add("total raw ticks do not equal named segments plus residual");
            if (record.StopwatchFrequency > 0 && Math.Abs(record.TotalWallMilliseconds - record.TotalRawTicks * 1000d / record.StopwatchFrequency) > CortexComputeRecord.RawTimingToleranceMilliseconds)
                failures.Add("total milliseconds disagree with raw ticks");
            if (requireZeroDark && record.ResidualRawTicks != 0)
                failures.Add("zero-dark residual raw ticks are nonzero");
        }
        if (!string.Equals(record.ComputeDigest(), record.Digest, StringComparison.Ordinal))
            failures.Add("digest mismatch");
        return new CortexComputeOccurrenceCheck(failures.Count == 0, failures.ToArray());
    }

    private static bool TryAdd(long left, long right, out long sum)
    {
        if ((right > 0 && left > long.MaxValue - right) || (right < 0 && left < long.MinValue - right))
        {
            sum = 0;
            return false;
        }
        sum = left + right;
        return true;
    }

    public static bool VerifyFixture(TextWriter output)
    {
        CortexComputeSegmentReading[] segments = new CortexComputeSegmentReading[CortexComputeAccounting.NamedSegmentCount];
        for (int i = 0; i < segments.Length; i++)
            segments[i] = new CortexComputeSegmentReading((CortexComputeSegmentKinds)i, i == 0 ? 2 : 0);
        CortexComputeRecord validBase = new(7, 10, segments, 8);
        CortexComputeRecord valid = new(7, 10, segments, 8, validBase.ComputeDigest());
        List<(string Name, bool Passed)> cases = new()
        {
            ("conserved", Verify(valid).Passed),
            ("omitted-time", !Verify(Corrupt(7, 10, segments, 0)).Passed),
            ("negative", !Verify(Corrupt(7, 10, Replace(segments, 1, -1), 9)).Passed),
            ("non-finite", !Verify(Corrupt(7, 10, Replace(segments, 1, double.NaN), 8)).Passed),
            ("segment-swap", !Verify(new CortexComputeRecord(7, 10, Replace(segments, 0, 0, 1, 2), 8, valid.Digest)).Passed),
            ("total-mismatch", !Verify(Corrupt(7, 11, segments, 8)).Passed),
            ("terminal-finalization", Verify(CreateTerminalFallbackRecord(11)).Passed),
            ("exception-finalization", Verify(CreateExceptionFallbackRecord(12)).Passed),
            ("raw-tick-zero-dark", Verify(CreateTerminalFallbackRecord(13), requireZeroDark: true).Passed),
            ("raw-tick-nonzero-dark", !Verify(CreateRawResidualRecord(14), requireZeroDark: true).Passed),
            ("raw-tick-roundtrip", VerifyRawTickRoundTrip()),
            ("precise-multi-segment-no-dark", VerifyPreciseMultiSegmentFixture()),
            ("funded-policy-boundary", Verify(CreatePolicyBoundaryRecord(15)).Passed
                && CreatePolicyBoundaryRecord(15).Segment(CortexComputeSegmentKinds.PolicyBoundary) > 0
                && CreatePolicyBoundaryRecord(15).Segment(CortexComputeSegmentKinds.Verifier) == 0),
            ("no-policy-boundary", Verify(CreateNoPolicyBoundaryRecord(16)).Passed
                && CreateNoPolicyBoundaryRecord(16).Segment(CortexComputeSegmentKinds.PolicyBoundary) == 0),
            ("legacy-schema", VerifyLegacySchemaFixture()),
        };
        bool passed = true;
        for (int i = 0; i < cases.Count; i++)
        {
            passed &= cases[i].Passed;
            output.WriteLine($"  {(cases[i].Passed ? "✓" : "✗")} compute-accounting {cases[i].Name}");
        }
        output.WriteLine($"compute-accounting-fixture · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static CortexComputeSegmentReading[] Replace(CortexComputeSegmentReading[] source, int index, double value)
    {
        CortexComputeSegmentReading[] copy = (CortexComputeSegmentReading[])source.Clone();
        copy[index] = copy[index] with { WallMilliseconds = value };
        return copy;
    }

    private static CortexComputeRecord Corrupt(int step, double total, CortexComputeSegmentReading[] segments, double residual)
        => new(step, total, segments, residual, "corrupt");

    private static CortexComputeRecord CreateTerminalFallbackRecord(int step)
    {
        CortexComputeAccounting accounting = new(100);
        accounting.Advance(CortexComputeSegmentKinds.Verifier, 110);
        return accounting.Complete(step, 120);
    }

    private static CortexComputeRecord CreateExceptionFallbackRecord(int step)
    {
        CortexComputeAccounting accounting = new(100);
        try { throw new InvalidOperationException("fixture fault"); }
        catch { accounting.Advance(CortexComputeSegmentKinds.Verifier, 110); }
        return accounting.Complete(step, 120);
    }

    private static bool VerifyPreciseMultiSegmentFixture()
    {
        long firstTicks = TraceClock.Frequency / 3;
        long secondTicks = TraceClock.Frequency / 5;
        long thirdTicks = TraceClock.Frequency / 7;
        long secondBoundary = firstTicks + secondTicks;
        long endTicks = secondBoundary + thirdTicks;
        CortexComputeAccounting accounting = new(0);
        accounting.Advance(CortexComputeSegmentKinds.Induce, firstTicks);
        accounting.Advance(CortexComputeSegmentKinds.Harvest, secondBoundary);
        CortexComputeRecord record = accounting.Complete(15, endTicks);
        double firstMilliseconds = Trace.ElapsedMsPrecise(0, firstTicks);
        double secondMilliseconds = Trace.ElapsedMsPrecise(0, secondTicks);
        double thirdMilliseconds = Trace.ElapsedMsPrecise(0, thirdTicks);
        bool passed = Verify(record).Passed
            && record.Segment(CortexComputeSegmentKinds.Prelude) == firstMilliseconds
            && record.Segment(CortexComputeSegmentKinds.Induce) == secondMilliseconds
            && record.Segment(CortexComputeSegmentKinds.Harvest) == thirdMilliseconds
            && record.ResidualWallMilliseconds == 0
            && firstMilliseconds > Trace.MsOf(firstTicks)
            && thirdMilliseconds > Trace.MsOf(thirdTicks);
        return passed;
    }

    private static bool VerifyRawTickRoundTrip()
    {
        CortexComputeRecord original = CreateTerminalFallbackRecord(17);
        bool parsed = CortexComputeAccounting.TryParse(original.ToTsv(), CortexComputeAccounting.Header.Split('\t'), out CortexComputeRecord? roundTrip)
            && roundTrip is not null && Verify(roundTrip).Passed;
        long[] changedTicks = new long[CortexComputeAccounting.NamedSegmentCount];
        if (roundTrip is not null)
            for (int i = 0; i < changedTicks.Length; i++) changedTicks[i] = roundTrip.SegmentRawTicks[i];
        if (changedTicks.Length == 0) return false;
        changedTicks[0]++;
        CortexComputeRecord forged = new(roundTrip!.Step, roundTrip.StopwatchFrequency, roundTrip.TotalRawTicks,
            roundTrip.TotalWallMilliseconds, roundTrip.Segments, changedTicks, roundTrip.ResidualRawTicks,
            roundTrip.ResidualWallMilliseconds, roundTrip.Digest);
        return parsed && !Verify(forged).Passed;
    }

    private static CortexComputeRecord CreatePolicyBoundaryRecord(int step)
    {
        long second = TraceClock.Frequency;
        CortexComputeAccounting accounting = new(0);
        accounting.Advance(CortexComputeSegmentKinds.PolicyBoundary, second);
        accounting.Advance(CortexComputeSegmentKinds.Verifier, second * 3);
        return accounting.Complete(step, second * 3);
    }

    private static CortexComputeRecord CreateNoPolicyBoundaryRecord(int step)
    {
        long second = TraceClock.Frequency;
        CortexComputeAccounting accounting = new(0);
        accounting.Advance(CortexComputeSegmentKinds.Verifier, second);
        return accounting.Complete(step, second * 4);
    }

    private static CortexComputeRecord CreateRawResidualRecord(int step)
    {
        long frequency = TraceClock.Frequency;
        CortexComputeSegmentReading[] segments = new CortexComputeSegmentReading[CortexComputeAccounting.NamedSegmentCount];
        long[] ticks = new long[CortexComputeAccounting.NamedSegmentCount];
        ticks[(int)CortexComputeSegmentKinds.Prelude] = frequency;
        segments[(int)CortexComputeSegmentKinds.Prelude] = new(CortexComputeSegmentKinds.Prelude, 1000);
        for (int i = 1; i < segments.Length; i++) segments[i] = new((CortexComputeSegmentKinds)i, 0);
        long totalTicks = frequency * 2;
        double totalMilliseconds = 2000;
        double residualMilliseconds = 1000;
        string digest = CortexComputeRecord.ComputeDigest(step, frequency, totalTicks, totalMilliseconds,
            segments, ticks, frequency, residualMilliseconds);
        return new(step, frequency, totalTicks, totalMilliseconds, segments, ticks, frequency, residualMilliseconds, digest);
    }

    private static bool VerifyLegacySchemaFixture()
    {
        CortexComputeSegmentReading[] segments = new CortexComputeSegmentReading[CortexComputeAccounting.NamedSegmentCount];
        for (int i = 0; i < segments.Length; i++)
            segments[i] = new CortexComputeSegmentReading((CortexComputeSegmentKinds)i, i == 0 ? 1 : 0);
        CortexComputeRecord legacy = new(8, 2, segments, 1, legacySchema: true);
        List<string> fields = [legacy.Step.ToString(CultureInfo.InvariantCulture), legacy.TotalWallMilliseconds.ToString("F6", CultureInfo.InvariantCulture)];
        for (int i = 0; i < segments.Length; i++)
            if ((CortexComputeSegmentKinds)i != CortexComputeSegmentKinds.PolicyBoundary)
                fields.Add(segments[i].WallMilliseconds.ToString("F6", CultureInfo.InvariantCulture));
        fields.Add(legacy.ResidualWallMilliseconds.ToString("F6", CultureInfo.InvariantCulture));
        fields.Add(legacy.ComputeDigest());
        string line = string.Join('\t', fields);
        return CortexComputeAccounting.TryParse(line, CortexComputeAccounting.LegacyHeader.Split('\t'), out CortexComputeRecord? parsed)
            && parsed is not null && CortexComputeAccountingVerifier.Verify(parsed).Passed
            && parsed.Segment(CortexComputeSegmentKinds.PolicyBoundary) == 0;
    }

    private static CortexComputeSegmentReading[] Replace(CortexComputeSegmentReading[] source, int left, double leftValue, int right, double rightValue)
    {
        CortexComputeSegmentReading[] copy = (CortexComputeSegmentReading[])source.Clone();
        copy[left] = copy[left] with { WallMilliseconds = leftValue };
        copy[right] = copy[right] with { WallMilliseconds = rightValue };
        return copy;
    }
}

internal static class CortexComputeAccountingReport
{
    public static void Write(string computePath, string reportPath, bool requireZeroDark = false)
    {
        if (!File.Exists(computePath)) return;
        string[] lines = File.ReadAllLines(computePath);
        string[] header = lines.Length == 0 ? [] : lines[0].TrimStart('\uFEFF').Split('\t');
        long[] totals = new long[CortexComputeAccounting.NamedSegmentCount];
        long[] rawTotals = new long[CortexComputeAccounting.NamedSegmentCount];
        long stopwatchFrequency = 0;
        long totalRawTicks = 0;
        long residualRawTicks = 0;
        long totalWall = 0;
        long residual = 0;
        int rows = 0;
        int scoredRows = 0;
        int failures = 0;
        int malformed = 0;
        int nonFinite = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (!CortexComputeAccounting.TryParse(lines[i], header, out CortexComputeRecord? record) || record is null)
            {
                malformed++;
                continue;
            }
            CortexComputeOccurrenceCheck verification = CortexComputeAccountingVerifier.Verify(record, requireZeroDark);
            if (!verification.Passed) failures++;
            if (record.StopwatchFrequency > 0)
            {
                if (stopwatchFrequency == 0) stopwatchFrequency = record.StopwatchFrequency;
                else if (stopwatchFrequency != record.StopwatchFrequency) failures++;
                if (!TryAdd(totalRawTicks, record.TotalRawTicks, out totalRawTicks)
                    || !TryAdd(residualRawTicks, record.ResidualRawTicks, out residualRawTicks))
                {
                    malformed++;
                    continue;
                }
            }
            if (!IsFinite(record)) nonFinite++;
            if (!TryToMicros(record.TotalWallMilliseconds, out long totalMicros)
                || !TryToMicros(record.ResidualWallMilliseconds, out long residualMicros))
            {
                malformed++;
                continue;
            }
            if (!TryAdd(totalWall, totalMicros, out totalWall) || !TryAdd(residual, residualMicros, out residual))
            {
                malformed++;
                continue;
            }
            bool scaled = true;
            for (int segment = 0; segment < record.Segments.Length; segment++)
            {
                if (!TryToMicros(record.Segments[segment].WallMilliseconds, out long segmentMicros))
                {
                    scaled = false;
                    break;
                }
                if (!TryAdd(totals[segment], segmentMicros, out totals[segment]))
                {
                    scaled = false;
                    break;
                }
                if (record.StopwatchFrequency > 0 && !TryAdd(rawTotals[segment], record.SegmentRawTicks[segment], out rawTotals[segment]))
                {
                    scaled = false;
                    break;
                }
            }
            if (!scaled) malformed++;
            else
            {
                rows++;
                if (record.Step >= 1) scoredRows++;
            }
        }

        StringBuilder output = new();
        output.AppendLine("Cortex compute accounting report");
        output.AppendLine($"status\t{(malformed == 0 && failures == 0 ? "PASS" : "FAIL")}");
        output.AppendLine($"records\t{rows.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"physical_records\t{rows.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"scored_records\t{scoredRows.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"malformed_rows\t{malformed.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"nonfinite_rows\t{nonFinite.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"verification_failures\t{failures.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"total_wall_ms\t{FromMicros(totalWall)}");
        output.AppendLine($"stopwatch_frequency\t{stopwatchFrequency.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"total_raw_ticks\t{totalRawTicks.ToString(CultureInfo.InvariantCulture)}");
        for (int i = 0; i < totals.Length; i++)
        {
            output.Append(CortexComputeAccounting.NameOf((CortexComputeSegmentKinds)i)).Append("_ms\t").AppendLine(FromMicros(totals[i]));
            output.Append(CortexComputeAccounting.NameOf((CortexComputeSegmentKinds)i)).Append("_raw_ticks\t").AppendLine(rawTotals[i].ToString(CultureInfo.InvariantCulture));
        }
        output.AppendLine($"residual_raw_ticks\t{residualRawTicks.ToString(CultureInfo.InvariantCulture)}");
        output.AppendLine($"residual_ms\t{FromMicros(residual)}");
        output.AppendLine($"zero_dark\t{(requireZeroDark ? (failures == 0 && residualRawTicks == 0 ? "PASS" : "FAIL") : "not-required")}");
        File.WriteAllText(reportPath, output.ToString());
    }

    private static bool IsFinite(CortexComputeRecord record)
    {
        if (!double.IsFinite(record.TotalWallMilliseconds) || !double.IsFinite(record.ResidualWallMilliseconds)) return false;
        for (int i = 0; i < record.Segments.Length; i++)
            if (!double.IsFinite(record.Segments[i].WallMilliseconds)) return false;
        return true;
    }

    private static bool TryToMicros(double milliseconds, out long micros)
    {
        micros = 0;
        if (!double.IsFinite(milliseconds) || Math.Abs(milliseconds) > long.MaxValue / 1_000_000d) return false;
        micros = (long)Math.Round(milliseconds * 1_000_000, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryAdd(long left, long right, out long sum)
    {
        if ((right > 0 && left > long.MaxValue - right) || (right < 0 && left < long.MinValue - right))
        {
            sum = 0;
            return false;
        }
        sum = left + right;
        return true;
    }

    private static string FromMicros(long micros) => (micros / 1_000_000d).ToString("F6", CultureInfo.InvariantCulture);
}
