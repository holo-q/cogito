namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

[RonObject]
internal partial class RepositoryTaskTransactionRON
{
    public int schemaVersion = 1;
    public string state = "prepared";
    public long actionEventID;
    public long verificationEventID;
    public long outcomeEventID;
    public string actionBase64 = "";
    public string verificationBase64 = "";
    public string outcomeBase64 = "";
    public string transactionSHA256 = "";
    public int step;
}

internal readonly record struct JournalCheckpointDelta(int Cursor, string[] Lines)
{
    internal bool IsEmpty => (Lines?.Length ?? 0) == 0;
}

public readonly record struct JournalRowBinding(
    int LineIndex,
    int Step,
    TapeEventID EventID,
    string Source,
    string SHA256);

/// An immutable journal capture for adjudication. All lines and canonical mint
/// bindings are copied while the owning Journal is live; consumers never reopen
/// journal.log or reconstruct a row from a path after this value is sealed.
public sealed class JournalSnapshot
{
    public JournalSnapshot(IReadOnlyList<string> lines, IReadOnlyList<JournalRowBinding> rows)
    {
        Lines = Array.AsReadOnly((lines ?? throw new ArgumentNullException(nameof(lines))).ToArray());
        Rows = Array.AsReadOnly((rows ?? throw new ArgumentNullException(nameof(rows))).ToArray());
        JournalSHA256 = LoopLineageVerifier.DigestJournal(Lines);
    }

    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<JournalRowBinding> Rows { get; }
    public string JournalSHA256 { get; }

    public void Validate()
    {
        if (JournalSHA256 is not { Length: 64 } || !JournalSHA256.All(Uri.IsHexDigit)
            || JournalSHA256 != LoopLineageVerifier.DigestJournal(Lines))
            throw new InvalidDataException("journal snapshot digest diverges");

        long previousLine = -1;
        HashSet<long> eventIDs = new();
        HashSet<int> boundLines = new();
        foreach (JournalRowBinding binding in Rows)
        {
            if (binding.LineIndex < 0 || binding.LineIndex >= Lines.Count
                || binding.LineIndex <= previousLine || binding.Step < 0 || binding.EventID.Value < 0
                || string.IsNullOrWhiteSpace(binding.Source) || !IsSHA(binding.SHA256)
                || !eventIDs.Add(binding.EventID.Value))
                throw new InvalidDataException("journal snapshot row binding is malformed");
            string line = Lines[binding.LineIndex];
            if (!TryParseCanonicalMintRow(line, out int step, out TapeEventID eventID, out string source)
                || step != binding.Step || eventID != binding.EventID || source != binding.Source
                || Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line))) != binding.SHA256)
                throw new InvalidDataException("journal snapshot row binding diverges from its line");
            if (!boundLines.Add(binding.LineIndex))
                throw new InvalidDataException("journal snapshot row binding is duplicated");
            previousLine = binding.LineIndex;
        }

        int canonicalMintRows = 0;
        for (int lineIndex = 0; lineIndex < Lines.Count; lineIndex++)
        {
            string[] fields = Lines[lineIndex].Split('\t');
            if (fields.Length <= 1 || fields[1] != "mint") continue;
            canonicalMintRows++;
            if (!boundLines.Contains(lineIndex))
                throw new InvalidDataException("journal snapshot omits a canonical mint row binding");
            if (!TryParseCanonicalMintRow(Lines[lineIndex], out _, out _, out _))
                throw new InvalidDataException("journal snapshot contains a malformed mint row");
        }
        if (Rows.Count != canonicalMintRows)
            throw new InvalidDataException("journal snapshot canonical mint row count diverges");
    }

    internal static bool TryParseCanonicalMintRow(
        string line,
        out int step,
        out TapeEventID eventID,
        out string source)
    {
        step = 0;
        eventID = default;
        source = string.Empty;
        string[] fields = line.Split('\t');
        if (fields.Length != 5 || fields[1] != "mint"
            || fields[2].Length < 2 || fields[2][0] != 's'
            || (fields[2].Length > 2 && fields[2][1] == '0')
            || fields[3].Length == 0 || fields[3].Contains('=')
            || fields[4].Length < 2 || fields[4][^1] != 'B'
            || !int.TryParse(fields[4].AsSpan(0, fields[4].Length - 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out int byteCount)
            || byteCount < 0
            || !Journal.TryParseBindingRow(line, out step, out eventID, out source)
            || fields[0] != step.ToString(CultureInfo.InvariantCulture)
            || fields[4][..^1] != byteCount.ToString(CultureInfo.InvariantCulture)
            || fields[2] != $"s{eventID.Value}")
            return false;
        return true;
    }

    private static bool IsSHA(string value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

// ── THE JOURNAL (durable) ──  the run's append-only line record. It is a file-backed trace of the
// tape's transitions, not a second memory substrate: the live learner reads the Tape, VTR carries
// phase telemetry, and this file sink keeps the human/replay journal durable across kills.

/// The durable write-through record. Every drive event lands as one deterministic text line
/// (`journal.log` when mounted). The loop never reads it as working state; checkpoint load only
/// restores the line journal so resume can truncate/rewrite the on-disk sink back to the snapshot.
public sealed class Journal : IDisposable
{
    public const string LogHeader = "step\tevent\tdetail";
    private const int MaxCheckpointLines = 1_000_000;
    // Keyframe dialect sentinel: the legacy keyframe form leads with a nonnegative line count, so a
    // negative marker unambiguously selects the shed-horizon form (and makes an old reader fail loud).
    private const int ShedHorizonSentinel = -2;

    private readonly List<string> _lines = new();        // the RESIDENT tail of the record — lines at absolute index [_shedLineCount, LineCount)
    private readonly Dictionary<string, TapeEventID> _repositoryTaskPredecessors = new(StringComparer.Ordinal);
    private bool _repositoryTaskIndexInitialized;
    private TextWriter? _sink;                           // the LIVE disk sink (journal.log appender) — every line lands as it happens, so a killed run keeps its record
    private string? _durablePath;                        // mounted journal.log authority for shed-row binding checks
    private FileStream? _durableReader;                  // held owner-file identity; callers must not replace or write journal.log outside this Journal
    private int _shedLineCount;                          // lines dropped from RAM after a durable checkpoint commit; journal.log is their only home
    private int _checkpointLineCount;                    // ABSOLUTE committed-line cursor (never resident-relative)
    private readonly Dictionary<int, string> _lineCache = new();
    private readonly Dictionary<int, (long Offset, int Length)> _shedLineOffsets = new();
    private long _indexedDurableBytes;
    private int _indexedDurableLineCount;
    private bool _indexedDurableHeader;
    private bool _disposed;
    private const string RepositoryTaskTransactionFileName = "repository-task-transaction.ron";

    public long Count => LineCount;                       // journal lines recorded (absolute, shed included)
    public int LineCount => _shedLineCount + _lines.Count;
    public int ShedLineCount => _shedLineCount;
    /// The lines still resident in RAM — absolute indices [ShedLineCount, LineCount). Full-record walks
    /// must go through EnumerateAllLines so a shed journal fails loud instead of silently truncating.
    public IReadOnlyList<string> ResidentLines => _lines;
    internal string? DurablePath => _durablePath;

    /// The whole line record, splicing the shed prefix back from journal.log. Passing a null path asserts
    /// the journal never shed (harness/Mesh journals); a shed journal without its file is an error, not
    /// an empty prefix — the record would be silently truncated.
    public IEnumerable<string> EnumerateAllLines(string? journalLogPath)
    {
        if (_shedLineCount > 0)
        {
            if (journalLogPath is null || !File.Exists(journalLogPath))
                throw new InvalidDataException($"journal shed {_shedLineCount} lines to disk but its journal.log is not available");
            int taken = 0;
            foreach (string line in File.ReadLines(journalLogPath))
            {
                if (taken == 0 && line == LogHeader) continue;
                yield return line;
                if (++taken == _shedLineCount) break;
            }
            if (taken != _shedLineCount)
                throw new InvalidDataException($"journal.log holds {taken} lines but the checkpoint shed {_shedLineCount}");
        }
        foreach (string line in _lines) yield return line;
    }

    /// Seal the complete canonical row view while this Journal still owns its
    /// resident tail and any shed-line reader. A shed prefix may be read here;
    /// the returned snapshot is self-contained and performs no later I/O.
    public JournalSnapshot CaptureSnapshot()
    {
        string[] lines = EnumerateAllLines(_durablePath).ToArray();
        List<JournalRowBinding> rows = new();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!JournalSnapshot.TryParseCanonicalMintRow(lines[lineIndex], out int step, out TapeEventID eventID, out string source))
                continue;
            rows.Add(new JournalRowBinding(lineIndex, step, eventID, source,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines[lineIndex])))));
        }
        JournalSnapshot snapshot = new(lines, rows);
        snapshot.Validate();
        return snapshot;
    }

    /// Advance the committed cursor, then drop the committed prefix from RAM when a live sink guarantees
    /// its durability (journal.log is line-flushed, so every committed line is already on disk). Sink-less
    /// journals (harnesses, Mesh, agent loops) stay fully resident and keep the legacy keyframe form.
    internal void CommitCheckpointLines()
    {
        _checkpointLineCount = LineCount;
        if (_sink is null || _lines.Count == 0) return;
        _shedLineCount = _checkpointLineCount;
        _lines.Clear();
        if (_durablePath is null) return;
        BuildShedLineOffsets();
        foreach (int lineIndex in _lineCache.Keys.Where(static index => index >= 0).ToArray())
            if (lineIndex < _shedLineCount) _lineCache.Remove(lineIndex);
    }

    internal JournalCheckpointDelta CaptureCheckpointDelta()
    {
        ValidateCursor(_checkpointLineCount);
        int residentCursor = _checkpointLineCount - _shedLineCount;
        return new(_checkpointLineCount, _lines.Count == residentCursor
            ? Array.Empty<string>()
            : _lines.GetRange(residentCursor, _lines.Count - residentCursor).ToArray());
    }

    internal void ApplyCheckpointDelta(in JournalCheckpointDelta delta)
    {
        ValidateCursor(delta.Cursor);
        if (delta.Lines is null)
            throw new InvalidDataException("journal checkpoint delta has no lines");
        if (delta.Lines.Length > MaxCheckpointLines)
            throw new InvalidDataException($"journal checkpoint delta exceeds {MaxCheckpointLines} lines");
        if (delta.Cursor != LineCount)
            throw new InvalidDataException($"journal checkpoint cursor gap: expected {LineCount}, got {delta.Cursor}");
        if (delta.Lines.Any(static line => line is null))
            throw new InvalidDataException("journal checkpoint delta contains a null line");
        _lines.AddRange(delta.Lines);
        _checkpointLineCount = LineCount;
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in JournalCheckpointDelta delta)
    {
        if (delta.Cursor < 0) throw new InvalidDataException("journal checkpoint delta cursor cannot be negative");
        if (delta.Lines is null || delta.Lines.Length > MaxCheckpointLines)
            throw new InvalidDataException($"journal checkpoint delta exceeds {MaxCheckpointLines} lines");
        writer.U8(1);
        writer.I32(delta.Cursor);
        writer.I32(delta.Lines.Length);
        foreach (string line in delta.Lines)
        {
            if (line is null) throw new InvalidDataException("journal checkpoint delta contains a null line");
            writer.Str(line);
        }
    }

    internal static JournalCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown journal checkpoint delta version");
        int cursor = reader.I32();
        int count = reader.I32();
        if (cursor < 0 || count < 0 || count > MaxCheckpointLines)
            throw new InvalidDataException("journal checkpoint delta cursor or line count is malformed");
        string[] lines = new string[count];
        for (int i = 0; i < count; i++) lines[i] = reader.Str();
        return new(cursor, lines);
    }

    /// Mount the live disk sink — from here every journal line is APPENDED + FLUSHED to journal.log the moment it
    /// is recorded (the safe-to-kill law: the durable record must not wait for LAND). Sink-less journals (the
    /// kill-line harnesses) keep working in-memory.
    public void Mount(TextWriter sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sink is not null) throw new InvalidOperationException("journal is already mounted");
        _sink = sink;
    }

    internal void Mount(TextWriter sink, string durablePath)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (string.IsNullOrWhiteSpace(durablePath)) throw new ArgumentException("journal durable path is required", nameof(durablePath));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sink is not null || _durableReader is not null) throw new InvalidOperationException("journal is already mounted");
        // The mounted sink and held reader are the sole journal owner. External replacement or writes to this path
        // while mounted violate the accepted durable append owner invariant; Journal does not and cannot enforce
        // mutation detection at the file-handle boundary.
        _sink = sink;
        _durablePath = durablePath;
        // UNBUFFERED, deliberately (bufferSize: 0 — .NET's "no read-ahead"): this reader is the
        // audit-only authority for shed journal rows, and a buffered stream answers a re-read from the
        // copy it took BEFORE the file changed. That turns tamper detection into a coin flip on
        // whether the row happened to fall inside the last buffer fill — the mutated-row null read
        // ACCEPTED for exactly that reason. Shed rows are read rarely and one at a time, so the
        // read-ahead bought nothing and cost the whole guarantee.
        _durableReader = new FileStream(durablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0);
    }

    /// Release the reader held for durable shed-row verification. The sink remains caller-owned: its writer is
    /// flushed and disposed by the mount caller, so Journal never closes an externally-owned sink a second time.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FileStream? durableReader = _durableReader;
        _durableReader = null;
        _durablePath = null;
        durableReader?.Dispose();
        GC.SuppressFinalize(this);
    }

    // one line: the in-memory record + the live sink (Run.Appender line-flushes each completed WriteLine —
    // a kill loses at most the in-flight line; no second Flush here).
    private void Emit(string line)
    {
        _lines.Add(line);
        _sink?.WriteLine(line);
    }

    /// A tape event entered cogito's world — corpus intake or the MIX re-ingest. `eventID` is
    /// the Tape's stable id; `source` is the provenance tag ("corpus").
    public void Ingest(int step, TapeEventID eventID, string source, byte[] eventBytes)
    {
        Emit($"{step}\tingest\t{eventID}\t{source}\t{eventBytes.Length}B");
    }

    /// The autoregressive loopback minted an event back onto the tape (the node's own utterance).
    public void Mint(int step, TapeEventID eventID, string node, byte[] eventBytes)
    {
        Emit(RenderMint(step, eventID, node, eventBytes));
    }

    internal JournalRowBinding MintWithBinding(int step, TapeEventID eventID, string node, byte[] eventBytes)
    {
        string line = RenderMint(step, eventID, node, eventBytes);
        Emit(line);
        _lineCache[LineCount - 1] = line;
        return new(LineCount - 1, step, eventID, node,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line))));
    }

    internal bool VerifyBinding(in JournalRowBinding binding)
    {
        if (binding.LineIndex >= _shedLineCount && _lineCache.TryGetValue(binding.LineIndex, out string? cachedLine))
            return VerifyBindingLine(in binding, cachedLine);
        if (binding.LineIndex < _shedLineCount)
            return VerifyShedBinding(in binding);
        int residentIndex = binding.LineIndex - _shedLineCount;
        if ((uint)residentIndex >= (uint)_lines.Count) return false;
        string line = _lines[residentIndex];
        return VerifyBindingLine(in binding, line);
    }

    /// Parse the stable prefix shared by every event-bearing journal row. The
    /// binding authority uses this instead of trusting caller-supplied row metadata.
    internal static bool TryParseBindingRow(
        string line,
        out int step,
        out TapeEventID eventID,
        out string source)
    {
        step = 0;
        eventID = default;
        source = string.Empty;
        string[] fields = line.Split('\t');
        if (fields.Length < 4
            || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out step)
            || step < 0
            || string.IsNullOrWhiteSpace(fields[1])
            || fields[2].Length < 2 || fields[2][0] != 's'
            || !long.TryParse(fields[2].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            || value < 0 || string.IsNullOrWhiteSpace(fields[3]))
        {
            step = 0;
            eventID = default;
            source = string.Empty;
            return false;
        }
        eventID = new TapeEventID(value);
        source = fields[3];
        return true;
    }

    internal Dictionary<string, int> BuildMintRowIndex()
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        _lineCache.Clear();
        BuildShedLineOffsets();
        int lineIndex = 0;
        foreach (string line in EnumerateAllLines(_durablePath))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 2 || fields[1] != "mint") { lineIndex++; continue; }
            if (!TryParseBindingRow(line, out _, out TapeEventID eventID, out string source))
                throw new InvalidDataException("journal mint row is malformed");
            if (lineIndex >= _shedLineCount) _lineCache[lineIndex] = line;
            lineIndex++;
            string key = $"{eventID.Value}\u0000{source}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    private bool VerifyShedBinding(in JournalRowBinding binding)
    {
        if (_durablePath is null || !File.Exists(_durablePath)) return false;
        if (!_shedLineOffsets.TryGetValue(binding.LineIndex, out (long Offset, int Length) span)) return false;
        FileStream stream = _durableReader ?? throw new InvalidDataException("journal durable reader is not mounted");
        stream.Position = span.Offset;
        byte[] bytes = new byte[span.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int chunk = stream.Read(bytes, read, bytes.Length - read);
            if (chunk == 0) return false;
            read += chunk;
        }
        // The cached length describes what was indexed, not what the file now holds. Reading only
        // that many bytes lets an APPEND to a shed line hide behind its own prefix — the tampered
        // line reads back byte-identical and its digest still matches. The line must therefore end
        // where the index says it ends: the next byte is a terminator, or the file has moved under
        // us and this binding is not verifiable.
        int terminator = stream.ReadByte();
        if (terminator is not (-1 or '\n' or '\r')) return false;
        return VerifyBindingLine(in binding, Encoding.UTF8.GetString(bytes));
    }

    private void BuildShedLineOffsets()
    {
        if (_shedLineCount == 0 || _durablePath is null || !File.Exists(_durablePath)) return;
        FileInfo durable = new(_durablePath);
        if (durable.Length < _indexedDurableBytes)
        {
            _shedLineOffsets.Clear();
            _indexedDurableBytes = 0;
            _indexedDurableLineCount = 0;
            _indexedDurableHeader = false;
        }
        FileStream stream = _durableReader ?? throw new InvalidDataException("journal durable reader is not mounted");
        stream.Position = _indexedDurableBytes;
        long offset = _indexedDurableBytes;
        while (true)
        {
            long lineOffset = stream.Position;
            List<byte> lineBytes = new();
            int value;
            bool terminated = false;
            while ((value = stream.ReadByte()) >= 0)
            {
                if (value == '\n') { terminated = true; break; }
                lineBytes.Add((byte)value);
            }
            if (!terminated)
            {
                stream.Position = lineOffset;
                break;
            }
            int encodedLength = lineBytes.Count > 0 && lineBytes[^1] == (byte)'\r' ? lineBytes.Count - 1 : lineBytes.Count;
            string line = Encoding.UTF8.GetString(lineBytes.ToArray(), 0, encodedLength);
            if (!_indexedDurableHeader && offset == 0 && line == LogHeader)
                _indexedDurableHeader = true;
            else
            {
                int logicalLine = _indexedDurableLineCount++;
                if (line.Split('\t') is { Length: > 1 } fields && fields[1] == "mint"
                    && TryParseBindingRow(line, out _, out TapeEventID eventID, out string source))
                {
                    if (logicalLine < _shedLineCount)
                        _shedLineOffsets[logicalLine] = (offset, encodedLength);
                    _ = eventID; _ = source;
                }
                else if (line.Split('\t') is { Length: > 1 } nonMintFields && nonMintFields[1] == "mint")
                    throw new InvalidDataException("journal mint row is malformed");
            }
            offset = stream.Position;
        }
        _indexedDurableBytes = offset;
    }

    private static bool VerifyBindingLine(in JournalRowBinding binding, string line)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line))) == binding.SHA256
            && TryParseBindingRow(line, out int step, out TapeEventID eventID, out string source)
            && line.Split('\t')[1] == "mint"
            && step == binding.Step && eventID == binding.EventID && source == binding.Source;
    }

    private static string RenderMint(int step, TapeEventID eventID, string node, byte[] eventBytes)
        => $"{step}\tmint\t{eventID}\t{node}\t{eventBytes.Length}B";

    public void RecordAction(int step, string episodeID, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields)
    {
        System.Text.StringBuilder sb = new();
        sb.Append(step).Append("\taction\t").Append(RenderField(episodeID)).Append('\t').Append(action.Tool.Name);
        foreach (CortexActionArgument argument in arguments)
        {
            sb.Append("\targ:").Append(RenderField(argument.Slot)).Append('=')
              .Append(RenderField(argument.Value)).Append('@').Append(Blur.SourceToken(argument.Source));
        }
        foreach (CortexObservationField field in fields)
        {
            sb.Append("\tobs:").Append(RenderField(field.Slot)).Append('=')
              .Append(RenderField(field.Value)).Append('@').Append(Blur.SourceToken(field.Source));
        }
        sb.Append("\tterminal=").Append(observation.Terminal ? '1' : '0');
        Emit(sb.ToString());
    }

    internal void RecordActionAdmission(int step, TapeEventID eventID,
        in CortexActionAdmissionReceipt receipt, int packetLength)
    {
        Emit($"{step}\taction-admission\t{eventID}\tphase={receipt.Phase}\ttool={receipt.Tool}\tsource={receipt.Source}\taction-request={receipt.ActionRequestSHA256}\texecution={receipt.ExecutionSHA256}\tdecision={receipt.Decision}\treason={receipt.Reason}\treceipt={receipt.ReceiptSHA256}\t{packetLength}B");
    }

    /// A compact source-typed action execution entered the grammar diet. The exhaustive action/observation record
    /// remains the adjacent `action` row; this row names the exact neutral packet admitted to the tape.
    public void RecordExecution(int step, TapeEventID eventID, string source, byte[] eventBytes)
    {
        Emit($"{step}\texecution\t{eventID}\t{source}\t{eventBytes.Length}B");
    }

    /// Durable mirror for a typed R1 lineage edge. The edge receipt itself remains in the
    /// ordinary tape packet; this line is only its journal address and compact identity.
    internal void RecordLoopLineageEdge(int step, TapeEventID eventID, LoopLineageEdgeReceipt receipt, byte[] eventBytes)
    {
        Emit($"{step}\tloop-lineage\t{eventID}\tedge={receipt.EdgeID.Value}\tnode={receipt.Node.NodeID.Value}\tspecies={receipt.Node.Species}\tcausal-event={receipt.Node.EventID}\tpredecessors={string.Join(',', receipt.PredecessorIDs.Select(static id => id.Value))}\tlineage={receipt.CanonicalLineageSHA256}\t{eventBytes.Length}B");
    }

    internal void RecordEmlOrdinaryRunRung0Receipt(
        int step,
        TapeEventID eventID,
        string source,
        in EmlOrdinaryRunRung0Receipt receipt,
        byte[] eventBytes)
    {
        Emit($"{step}\teml-rung0\t{eventID}\t{source}\trung0={receipt.Rung0}\tassay={receipt.Assay}\tpower={receipt.Power}\topportunities={receipt.Opportunities}\tcarrier-bound={receipt.CarrierBoundCandidates}\tguard-eligible={receipt.GuardEligibleCandidates}\tfunded-attempts={receipt.PaidAttempts}\tattempted-candidates={receipt.AttemptedCandidates}\tderivations={receipt.Compositions}\tzero-evaluator={receipt.ZeroEvaluatorCompositions}\taudits={receipt.Audits}\tagreed-audits={receipt.AgreedAudits}\tdisagreed-audits={receipt.DisagreedAudits}\tnot-selected-audits={receipt.NotSelectedAudits}\tschema={receipt.SchemaVersion}\tnull-executions={receipt.RelationNullExecutions}\tnull-divergences={receipt.RelationNullDivergences}\tnull-authority={receipt.RelationNullAuthorityPredictions}\tnull-pairs-considered={receipt.RelationNullPairsConsidered}\tnull-pairs-created={receipt.RelationNullPairsCreated}\tnull-reject-no-carrier={receipt.RelationNullRejectNoCarrier}\tnull-reject-shape={receipt.RelationNullRejectShape}\tnull-reject-grade={receipt.RelationNullRejectGrade}\tderivation={receipt.CompositionDigest}\tsource={receipt.SourceDigest}\tconfig={receipt.ConfigDigest}\tdigest={receipt.Digest}\t{eventBytes.Length}B");
    }

    /// A Weft VM execution trace joined the mesh diet. The event bytes are the sourced execution corroboration; the
    /// program name lets the standing Weft channel reconstruct its Fuel journal on resume without extending the
    /// checkpoint image.
    public void Weft(int step, TapeEventID eventID, string source, string program, byte[] trace, Cogito.Exec.ExecResult exec)
    {
        Emit($"{step}\tweft\t{eventID}\t{source}\t{program}\t{trace.Length}B\tfuelLeft={exec.FuelLeft}\tbody={exec.FuelJournal.TotalBodyFuel}\tcalls={exec.FuelJournal.TotalCalls}\tleaf={exec.FuelJournal.TotalLeafFuel}");
    }

    /// An intrinsic self-stream packet joined the same tape as world observations and generated events. `source`
    /// names the channel (`self:excursion`, `self:thought`) so the grammar can learn routing without a side bus.
    public void Self(int step, TapeEventID eventID, string source, string channel, string token, byte[] eventBytes)
    {
        Emit($"{step}\tself\t{eventID}\t{source}\t{channel}\t{eventBytes.Length}B\t{token}");
    }

    /// Diagnostic mirror for an exact typed metric frame. The learner reads the packet from Tape; the journal
    /// records only its identity and extent so it cannot become a second numeric stream.
    public void RecordMetricFrame(int step, TapeEventID eventID, string source, int sampleCount, byte[] eventBytes)
    {
        Emit($"{step}\tmetric\t{eventID}\t{source}\t{sampleCount} samples\t{eventBytes.Length}B");
    }

    public void RecordPolicyDecision(
        int step,
        TapeEventID eventID,
        string source,
        in CortexPolicyDecision decision,
        int actionCount,
        int featureCount,
        byte[] eventBytes)
    {
        Emit(FormatPolicyDecisionRow(step, eventID, source, in decision, actionCount, featureCount, eventBytes.Length));
    }

    internal static string FormatPolicyDecisionRow(
        int step,
        TapeEventID eventID,
        string source,
        in CortexPolicyDecision decision,
        int actionCount,
        int featureCount,
        int payloadLength)
        => $"{step}\tpolicy-decision\t{eventID}\t{source}\tdecision={decision.DecisionID.Value}\tauthority={decision.Authority}\trevision={decision.GrammarRevision.Value}\tlaunchpad={decision.LaunchpadAction}\traw={decision.RawCandidateAction}\tselected={decision.SelectedCandidateAction}\taction={decision.Action}/{actionCount}\tcause={decision.SelectionCause}\tdrill={(decision.RollbackDrill ? 1 : 0)}\tfeatures={featureCount}\t{payloadLength}B";

    internal static string ComputePolicyDecisionJournalSHA256(
        int step,
        TapeEventID eventID,
        string source,
        in CortexPolicyDecision decision,
        int actionCount,
        int featureCount,
        int payloadLength)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormatPolicyDecisionRow(step, eventID, source, in decision, actionCount, featureCount, payloadLength))));

    internal void RecordPolicyBoundarySourceCorroboration(
        int step,
        TapeEventID eventID,
        string source,
        in CortexPolicyBoundarySourceCorroboration corroboration,
        byte[] eventBytes)
    {
        corroboration.Validate();
        Emit($"{step}\tpolicy-boundary-source\t{eventID}\t{source}\tdecision={corroboration.SourceDecisionID.Value}\tsource-event={corroboration.SourceDecisionEventID.Value}\tauthority={corroboration.SourceAuthority}\tcause={corroboration.SourceSelectionCause}\treadout={corroboration.ReadoutFingerprint:X16}\tcandidate={corroboration.CandidateFingerprint:X16}\tsupport={corroboration.OccurrenceDigest:X16}\trevision={corroboration.ReadoutRevision.Value}\tcached={corroboration.CachedContexts}\tcomparisons={corroboration.Comparisons}\tagreements={corroboration.Agreements}\tmisses={corroboration.Misses}\tdigest={corroboration.CorroborationDigest}\t{eventBytes.Length}B");
    }

    internal void RecordLoopClosureOrganicOpportunity(
        int step,
        TapeEventID eventID,
        string source,
        CortexPolicyID policy,
        int opportunities,
        byte[] eventBytes)
    {
        Emit($"{step}\tloop-closure-organic-opportunity\t{eventID}\t{source}\tpolicy={policy.Value}\topportunities={opportunities}\t{eventBytes.Length}B");
    }

    internal void RecordOrganicComparison(
        int step,
        TapeEventID eventID,
        string source,
        in OrganicComparisonReceipt receipt,
        byte[] eventBytes)
    {
        receipt.Validate();
        Emit($"{step}\torganic-comparison\t{eventID}\t{source}\tpolicy={receipt.Policy.Value}\tdecision={receipt.DecisionID.Value}\tsource-event={receipt.SourceDecisionEventID.Value}\toutcome={receipt.Outcome}\treadout-revision={receipt.ReadoutRevision.Value}\treadout={receipt.ReadoutFingerprint:X16}\tcandidate={receipt.CandidateFingerprint:X16}\tsupport={receipt.CandidateOccurrenceDigest:X16}\tlaunchpad={receipt.LaunchpadAction}\traw={receipt.RawCandidateAction}\tselected={receipt.SelectedCandidateAction}\tfunding={receipt.QuotaDecisionID?.Value.ToString() ?? ""}\tfunding-row-sha256={receipt.FundingJournalRowSHA256}\tsettlement-row-sha256={receipt.SettlementJournalRowSHA256}\tsource-payload-sha256={receipt.SourceDecisionPayloadSHA256}\tsource-journal-sha256={receipt.SourceDecisionJournalSHA256}\treceipt-sha256={receipt.CanonicalReceiptSHA256}\t{eventBytes.Length}B");
    }

    internal void RecordLoopClosureLinkEvent(
        int step,
        TapeEventID eventID,
        string kind,
        string source,
        byte[] eventBytes)
    {
        Emit($"{step}\t{kind}\t{eventID}\t{source}\t{eventBytes.Length}B");
    }

    public void RecordPolicyOutcome(
        int step,
        TapeEventID eventID,
        string source,
        CortexPolicyDecisionID decisionID,
        int outcomeCount,
        bool invariantClean,
        long conservedCost,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-outcome\t{eventID}\t{source}\tdecision={decisionID.Value}\toutcomes={outcomeCount}\tinvariant={(invariantClean ? 1 : 0)}\tconserved-cost={conservedCost}\tpayload-sha256={Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(eventBytes))}\t{eventBytes.Length}B");
    }

    public void RecordPolicyOccurrenceCheck(
        int step,
        TapeEventID eventID,
        string source,
        ulong fingerprint,
        int comparisons,
        int agreements,
        int failures,
        bool passed,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-verification\t{eventID}\t{source}\tfingerprint={fingerprint:X16}\tagreement={agreements}/{comparisons}\tfailures={failures}\tresult={(passed ? "pass" : "reject")}\t{eventBytes.Length}B");
    }

    internal void RecordRepositoryOccurrenceCheck(
        int step, TapeEventID eventID, in RepositoryOccurrenceCheckReceipt receipt, byte[] eventBytes)
    {
        receipt.Validate();
        Emit($"{step}\trepository-verification\t{eventID}\tspecies={receipt.Prediction.Species}\toutcome={receipt.Outcome}\tclaim={receipt.PredictionSHA256}\tevidence={receipt.EvidenceSHA256}\tworld={receipt.WorldSHA256}\taccess={receipt.AccessSHA256}\tevaluator-cost={receipt.EvaluatorCost}\taccess-cost={receipt.AccessCost}\tpredecessor={receipt.PredecessorEventID.Value}\tcall={receipt.CallSHA256}\treceipt={receipt.ReceiptSHA256}\t{eventBytes.Length}B");
    }

    internal void RecordRepositoryLoopTaskReceipt(
        int step,
        TapeEventID eventID,
        string source,
        string prefix,
        TapeEventID predecessorEventID,
        string receiptSHA256,
        byte[] eventBytes)
    {
        if (source is not ("repository-action" or "repository-verification" or "repository-outcome"))
            throw new InvalidDataException("repository task journal source is not an ordinary task species");
        string expectedPrefix = source switch
        {
            "repository-action" => RepositoryLoopTaskReceiptCodec.ActionPrefix,
            "repository-verification" => RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix,
            _ => RepositoryLoopTaskReceiptCodec.OutcomePrefix,
        };
        if (prefix != expectedPrefix)
            throw new InvalidDataException("repository task journal source/prefix mapping is malformed");
        if (!RepositoryLoopTaskReceiptCodec.IsSHA(receiptSHA256))
            throw new InvalidDataException("repository task journal receipt digest is malformed");
        (TapeEventID DecodedPredecessor, string DecodedReceipt) decoded = source switch
        {
            "repository-action" when RepositoryLoopTaskActionReceipt.TryDecode(eventBytes, out RepositoryLoopTaskActionReceipt action)
                => (action.SelectionEventID, action.ReceiptSHA256),
            "repository-verification" when RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(eventBytes, out RepositoryLoopTaskOccurrenceCheckReceipt verification)
                => (verification.ActionEventID, verification.ReceiptSHA256),
            "repository-outcome" when RepositoryLoopTaskOutcomeReceipt.TryDecode(eventBytes, out RepositoryLoopTaskOutcomeReceipt outcome)
                => (outcome.OccurrenceCheckEventID, outcome.ReceiptSHA256),
            _ => throw new InvalidDataException("repository task journal payload prefix is malformed"),
        };
        if (decoded.DecodedReceipt != receiptSHA256 || decoded.DecodedPredecessor != predecessorEventID)
            throw new InvalidDataException("repository task journal receipt authority diverges from tape payload");
        if (predecessorEventID.Value <= 0 || !_repositoryTaskPredecessors.TryAdd($"{source}\0{predecessorEventID.Value}", eventID))
            throw new InvalidDataException("repository task predecessor already has an ordinary receipt");
        // The journal's canonical authority is the mint row. Prefix and predecessor
        // remain typed in the tape receipt; duplicating them into an ad-hoc row would
        // make JournalSnapshot reject the row as non-canonical.
        Mint(step, eventID, source, eventBytes);
    }

    /// Record the three ordinary task links as one journal mutation. Every
    /// payload/predecessor/index join is checked before the resident record or
    /// sink receives any row, so a checkpoint cannot capture a journal prefix.
    internal void RecordRepositoryLoopTaskTransaction(
        int step,
        TapeEventID actionEventID,
        RepositoryLoopTaskActionReceipt action,
        byte[] actionBytes,
        TapeEventID verificationEventID,
        RepositoryLoopTaskOccurrenceCheckReceipt verification,
        byte[] verificationBytes,
        TapeEventID outcomeEventID,
        RepositoryLoopTaskOutcomeReceipt outcome,
        byte[] outcomeBytes,
        bool commit = true)
    {
        (TapeEventID Predecessor, string Receipt, string Source, string Prefix, TapeEventID EventID, byte[] Bytes)[] rows =
        [
            (action.SelectionEventID, action.ReceiptSHA256, "repository-action", RepositoryLoopTaskReceiptCodec.ActionPrefix, actionEventID, actionBytes),
            (verification.ActionEventID, verification.ReceiptSHA256, "repository-verification", RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix, verificationEventID, verificationBytes),
            (outcome.OccurrenceCheckEventID, outcome.ReceiptSHA256, "repository-outcome", RepositoryLoopTaskReceiptCodec.OutcomePrefix, outcomeEventID, outcomeBytes),
        ];
        HashSet<string> pendingKeys = new(StringComparer.Ordinal);
        string[] lines = new string[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            (TapeEventID predecessor, string receipt, string source, string prefix, TapeEventID eventID, byte[] bytes) = rows[index];
            if (bytes is null || predecessor.Value <= 0 || !RepositoryLoopTaskReceiptCodec.IsSHA(receipt)
                || !bytes.AsSpan().StartsWith(Encoding.ASCII.GetBytes(prefix)))
                throw new InvalidDataException("repository task journal transaction row is malformed");
            (TapeEventID decodedPredecessor, string decodedReceipt) = source switch
            {
                "repository-action" when RepositoryLoopTaskActionReceipt.TryDecode(bytes, out RepositoryLoopTaskActionReceipt decodedAction)
                    => (decodedAction.SelectionEventID, decodedAction.ReceiptSHA256),
                "repository-verification" when RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(bytes, out RepositoryLoopTaskOccurrenceCheckReceipt decodedOccurrenceCheck)
                    => (decodedOccurrenceCheck.ActionEventID, decodedOccurrenceCheck.ReceiptSHA256),
                "repository-outcome" when RepositoryLoopTaskOutcomeReceipt.TryDecode(bytes, out RepositoryLoopTaskOutcomeReceipt decodedOutcome)
                    => (decodedOutcome.OccurrenceCheckEventID, decodedOutcome.ReceiptSHA256),
                _ => throw new InvalidDataException("repository task journal transaction payload is malformed"),
            };
            string key = $"{source}\0{predecessor.Value}";
            if (decodedPredecessor != predecessor || decodedReceipt != receipt
                || _repositoryTaskPredecessors.ContainsKey(key) || !pendingKeys.Add(key))
                throw new InvalidDataException("repository task journal transaction predecessor repeats");
            lines[index] = RenderMint(step, eventID, source, bytes);
        }
        if (!commit) return;
        string batch = string.Join('\n', lines) + '\n';
        StreamWriter? sink = _sink as StreamWriter;
        long sinkPosition = -1;
        if (sink is not null)
        {
            sink.Flush();
            if (sink.BaseStream.CanSeek) sinkPosition = sink.BaseStream.Position;
        }
        try
        {
            _sink?.Write(batch);
            _sink?.Flush();
            _lines.AddRange(lines);
            for (int index = 0; index < rows.Length; index++)
                _repositoryTaskPredecessors[$"{rows[index].Source}\0{rows[index].Predecessor.Value}"] = rows[index].EventID;
            ClearRepositoryLoopTaskTransaction();
        }
        catch
        {
            if (sink is not null && sinkPosition >= 0)
            {
                try
                {
                    sink.BaseStream.SetLength(sinkPosition);
                    sink.BaseStream.Position = sinkPosition;
                    sink.BaseStream.Flush();
                }
                catch { /* the run is already aborting; the sink is quarantined below */ }
            }
            _sink = null;
            throw;
        }
    }

    internal void PrepareRepositoryLoopTaskTransaction(
        int step,
        TapeEventID actionEventID,
        byte[] actionBytes,
        TapeEventID verificationEventID,
        byte[] verificationBytes,
        TapeEventID outcomeEventID,
        byte[] outcomeBytes)
    {
        if (_durablePath is null) return;
        if (actionEventID.Value <= 0 || verificationEventID.Value != actionEventID.Value + 1
            || outcomeEventID.Value != verificationEventID.Value + 1)
            throw new InvalidDataException("repository task transaction ids are not contiguous");
        RepositoryTaskTransactionRON document = new()
        {
            step = step,
            actionEventID = actionEventID.Value,
            verificationEventID = verificationEventID.Value,
            outcomeEventID = outcomeEventID.Value,
            actionBase64 = Convert.ToBase64String(actionBytes),
            verificationBase64 = Convert.ToBase64String(verificationBytes),
            outcomeBase64 = Convert.ToBase64String(outcomeBytes),
        };
        document.transactionSHA256 = ComputeRepositoryTaskTransactionSHA(document);
        byte[] encoded = RonSerializer.SerializeToUtf8(in document);
        string path = Path.Combine(Path.GetDirectoryName(_durablePath)!, RepositoryTaskTransactionFileName);
        string temporary = path + ".next";
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(encoded);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private void ClearRepositoryLoopTaskTransaction()
    {
        if (_durablePath is null) return;
        string path = Path.Combine(Path.GetDirectoryName(_durablePath)!, RepositoryTaskTransactionFileName);
        if (File.Exists(path)) File.Delete(path);
    }

    internal void RecoverRepositoryLoopTaskTransaction(Tape tape)
    {
        if (_durablePath is null) return;
        string path = Path.Combine(Path.GetDirectoryName(_durablePath)!, RepositoryTaskTransactionFileName);
        if (!File.Exists(path)) return;
        RepositoryTaskTransactionRON document = RonSerializer.Deserialize<RepositoryTaskTransactionRON>(File.ReadAllBytes(path));
        if (document.schemaVersion != 1 || document.state != "prepared"
            || document.actionEventID <= 0 || document.verificationEventID != document.actionEventID + 1
            || document.outcomeEventID != document.verificationEventID + 1
            || document.transactionSHA256 != ComputeRepositoryTaskTransactionSHA(document))
            throw new InvalidDataException("repository task transaction marker is malformed");
        byte[] actionBytes = Convert.FromBase64String(document.actionBase64);
        byte[] verificationBytes = Convert.FromBase64String(document.verificationBase64);
        byte[] outcomeBytes = Convert.FromBase64String(document.outcomeBase64);
        TapeEventID actionID = new(document.actionEventID);
        TapeEventID verificationID = new(document.verificationEventID);
        TapeEventID outcomeID = new(document.outcomeEventID);
        RepositoryLoopTaskActionReceipt action = RepositoryLoopTaskActionReceipt.Decode(actionBytes);
        RepositoryLoopTaskOccurrenceCheckReceipt verification = RepositoryLoopTaskOccurrenceCheckReceipt.Decode(verificationBytes);
        RepositoryLoopTaskOutcomeReceipt outcome = RepositoryLoopTaskOutcomeReceipt.Decode(outcomeBytes);
        bool[] tapeRows =
        [
            tape.Resolve(actionID, out byte[] actionPayload) && actionPayload.AsSpan().SequenceEqual(actionBytes),
            tape.Resolve(verificationID, out byte[] verificationPayload) && verificationPayload.AsSpan().SequenceEqual(verificationBytes),
            tape.Resolve(outcomeID, out byte[] outcomePayload) && outcomePayload.AsSpan().SequenceEqual(outcomeBytes),
        ];
        bool[] journalRows =
        [
            _repositoryTaskPredecessors.ContainsKey($"repository-action\0{action.SelectionEventID.Value}"),
            _repositoryTaskPredecessors.ContainsKey($"repository-verification\0{verification.ActionEventID.Value}"),
            _repositoryTaskPredecessors.ContainsKey($"repository-outcome\0{outcome.OccurrenceCheckEventID.Value}"),
        ];
        int tapeCount = tapeRows.Count(static present => present);
        int journalCount = journalRows.Count(static present => present);
        if (tapeRows.Any(static present => present) && tapeCount != 3
            || journalRows.Any(static present => present) && journalCount != 3)
            throw new InvalidDataException("repository task transaction marker found a partial chain");
        if (tapeCount == 0)
        {
            if (journalCount == 3)
                tape.AppendTransaction([
                    new(actionBytes, "repository-action", Provenances.Execution, TapeEventRoles.AuditOnly),
                    new(verificationBytes, "repository-verification", Provenances.Execution, TapeEventRoles.AuditOnly),
                    new(outcomeBytes, "repository-outcome", Provenances.Execution, TapeEventRoles.AuditOnly)]);
            else if (journalCount == 0)
            {
                tape.AppendTransaction([
                    new(actionBytes, "repository-action", Provenances.Execution, TapeEventRoles.AuditOnly),
                    new(verificationBytes, "repository-verification", Provenances.Execution, TapeEventRoles.AuditOnly),
                    new(outcomeBytes, "repository-outcome", Provenances.Execution, TapeEventRoles.AuditOnly)],
                    _ => RecordRepositoryLoopTaskTransaction(document.step, actionID, action, actionBytes, verificationID, verification,
                        verificationBytes, outcomeID, outcome, outcomeBytes));
            }
        }
        else if (tapeCount == 3 && journalCount == 0)
        {
            RecordRepositoryLoopTaskTransaction(document.step, actionID, action, actionBytes, verificationID, verification,
                verificationBytes, outcomeID, outcome, outcomeBytes);
        }
        ClearRepositoryLoopTaskTransaction();
    }

    private static string ComputeRepositoryTaskTransactionSHA(RepositoryTaskTransactionRON document)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            document.schemaVersion, document.state, document.actionEventID, document.verificationEventID,
            document.outcomeEventID, document.actionBase64, document.verificationBase64, document.outcomeBase64,
            document.step))));

    internal void ValidateRepositoryLoopTaskTransaction(
        int step,
        TapeEventID actionEventID,
        RepositoryLoopTaskActionReceipt action,
        byte[] actionBytes,
        TapeEventID verificationEventID,
        RepositoryLoopTaskOccurrenceCheckReceipt verification,
        byte[] verificationBytes,
        TapeEventID outcomeEventID,
        RepositoryLoopTaskOutcomeReceipt outcome,
        byte[] outcomeBytes)
        => RecordRepositoryLoopTaskTransaction(step, actionEventID, action, actionBytes, verificationEventID, verification,
            verificationBytes, outcomeEventID, outcome, outcomeBytes, commit: false);

    internal void EnsureRepositoryTaskIndex(Tape tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        if (_repositoryTaskIndexInitialized) return;
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (view.Source is not ("repository-action" or "repository-verification" or "repository-outcome")
                || !tape.Resolve(view.Id, out byte[] payload)) continue;
            string taskPrefix = view.Source switch
            {
                "repository-action" => RepositoryLoopTaskReceiptCodec.ActionPrefix,
                "repository-verification" => RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix,
                _ => RepositoryLoopTaskReceiptCodec.OutcomePrefix,
            };
            if (!payload.AsSpan().StartsWith(Encoding.ASCII.GetBytes(taskPrefix)))
                continue; // shared source may carry a legacy REPOSITORY-LINEAGE receipt
            TapeEventID predecessor = view.Source switch
            {
                "repository-action" when RepositoryLoopTaskActionReceipt.TryDecode(payload, out RepositoryLoopTaskActionReceipt action) => action.SelectionEventID,
                "repository-verification" when RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(payload, out RepositoryLoopTaskOccurrenceCheckReceipt verification) => verification.ActionEventID,
                "repository-outcome" when RepositoryLoopTaskOutcomeReceipt.TryDecode(payload, out RepositoryLoopTaskOutcomeReceipt outcome) => outcome.OccurrenceCheckEventID,
                _ => throw new InvalidDataException("repository task tape payload is malformed during journal indexing"),
            };
            if (!_repositoryTaskPredecessors.TryAdd($"{view.Source}\0{predecessor.Value}", view.Id))
                throw new InvalidDataException("repository task predecessor repeats during journal indexing");
        }
        _repositoryTaskIndexInitialized = true;
    }

    internal void RecordRepositoryLoopSeal(int step, TapeEventID eventID, byte[] eventBytes)
        => Mint(step, eventID, "repository-seal", eventBytes);

    public void RecordPolicyTrialQuota(int step, TapeEventID eventID, string source, in CortexPolicyTrialQuotaDecision decision, byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-trial-funding\t{eventID}\t{source}\tid={decision.QuotaDecisionID}\tdecision={decision.Decision}\tplanned={decision.PlannedArmSteps}\treserved={decision.HeldArmSteps}\tcharged={decision.UsedSteps}\tremaining={decision.RemainingQuota}\tcandidate={decision.CandidateState}\tdenial={decision.DenialReason}\torigin={decision.CandidateOriginStep}\tcurrent={decision.CandidateCurrentStep}\trequired={decision.CandidateRequiredStep}\trevision={decision.CandidateRevision.Value}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyPendingForcedTrialRearm(
        int step,
        TapeEventID eventID,
        string source,
        in CortexPolicyPendingForcedTrialRearmEvaluation evaluation,
        byte[] eventBytes)
        => Emit($"{step}\tpolicy-trial-rearm\t{eventID}\t{source}\tpolicy={evaluation.Policy.Value}\tfunding={evaluation.QuotaID:X16}\toutcome={evaluation.Outcome}\tspecies={evaluation.DenialSpecies}\tsource-funding={evaluation.SourceQuotaDecision}\tsource-decision={evaluation.SourceDecisionID:X16}\tsource-event={evaluation.SourceDecisionEventID}\tsource-witness={evaluation.SourceCorroborationEventID}\tsource-support={evaluation.SourceOccurrenceDigest:X16}\tsource-candidate={evaluation.SourceCandidateFingerprint:X16}\tsource-funded-candidate={evaluation.SourceQuotaCandidateFingerprint:X16}\tsource-readout={evaluation.SourceReadoutFingerprint:X16}\tsource-revision={evaluation.SourceCandidateRevision.Value}\tsource-state={evaluation.SourceCanonicalState}\treadout={evaluation.ReadoutFingerprint:X16}\tcandidate={evaluation.CandidateFingerprint:X16}\trevision={evaluation.CandidateRevision.Value}\tsupport={evaluation.OccurrenceDigest:X16}\tstate={evaluation.CanonicalState}\tarm={evaluation.Arm}\tfeature={evaluation.FeatureID}\tobligation={evaluation.ObligationID}\tbound={(evaluation.IntentBound ? 1 : 0)}\tsource-run={evaluation.SourceRunID}\tcustody={evaluation.AuditOnlyDigest}\t{eventBytes.Length}B");

    public void RecordPolicyTrialCompletion(int step, TapeEventID eventID, string source, in CortexPolicyTrialCompletion settlement, byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-trial-settlement\t{eventID}\t{source}\tid={settlement.QuotaDecisionID}\tactual={settlement.ActualExecutedArmSteps}\trefund={settlement.ReclaimedOrUnused}\tevaluator={(settlement.EvaluatorWorkUnits?.ToString() ?? "na")}\tverifier={settlement.VerifierOutcome}\twall={(settlement.WallMilliseconds?.ToString() ?? "na")}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyBoundary(int step, TapeEventID eventID, string source, IPolicyBoundaryDomain domain, in PolicyBoundaryForkReceipt receipt, string receiptDigest, byte[] eventBytes)
    {
        ArgumentNullException.ThrowIfNull(domain);
        receipt.Validate(domain);
        string arms = string.Join(';', receipt.Arms.Select(static arm =>
            string.Join(',', (byte)arm.Arm, arm.Horizon, arm.PaidCloseDelta, arm.MatchedSpend,
                arm.ContinuityExact ? 1 : 0, arm.ChildProcessCompleted ? 1 : 0, arm.GrammarExecutionsDelta, arm.TrialAdaptationTransitions, arm.AdaptationEnabled ? 1 : 0,
                (byte)arm.ExecutionOutcome, arm.RequestCount, arm.GuardAdmittedCount, arm.LastRequestDecisionID.Value, arm.LastRequestStep,
                arm.LastRequestReadout.LaunchpadAction, arm.LastRequestReadout.RawCandidateAction, arm.LastRequestReadout.SelectedCandidateAction,
                arm.LastRequestReadout.ExecutedAction, (byte)arm.LastRequestReadout.Authority, arm.LastRequestReadout.GrammarRevision.Value,
                (byte)arm.LastRequestReadout.SelectionCause, arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture),
                arm.LastRequestReadout.ReadoutCandidateFingerprint.ToString("X16", System.Globalization.CultureInfo.InvariantCulture),
                arm.ExecutedDecisionID.Value, arm.ExecutedLaunchpadAction, arm.ExecutedRawCandidateAction, arm.ExecutedSelectedCandidateAction,
                arm.ExecutedAction, (byte)arm.ExecutedAuthority, (byte)arm.ExecutedSelectionCause,
                arm.ExecutedReadoutFingerprint.ToString("X16", System.Globalization.CultureInfo.InvariantCulture), arm.ExecutedReadoutRevision,
                arm.ExecutedReadoutOccurrenceDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture), arm.ExecutedCandidateFingerprint.ToString("X16", System.Globalization.CultureInfo.InvariantCulture))));
        Emit($"{step}\tpolicy-boundary\t{eventID}\t{source}\tobligation={receipt.Obligation}\thorizons={string.Join(',', receipt.Horizons)}\tarms={arms}\tchild-process-completed={(receipt.AllChildrenCompleted ? 1 : 0)}\tforced-null-behavior={(receipt.ForcedNullBehaviorExecuted ? 1 : 0)}\tforced-null-diverged={(receipt.ForcedNullDiverged ? 1 : 0)}\tcontinuity={(receipt.ContinuityExact ? 1 : 0)}\tmatched-spend={(receipt.MatchedSpend ? 1 : 0)}\tverified={(receipt.Verified ? 1 : 0)}\tfunding-id={receipt.QuotaDecisionID}\tsource-fingerprint={receipt.SourceDecisionReadoutFingerprint:X16}\tsource-candidate-fingerprint={receipt.SourceDecisionCandidateFingerprint:X16}\tsource-revision={receipt.SourceDecisionReadoutRevision}\texecution-witness={(receipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "")}\texecution-training={(receipt.ExecutionCorroboration?.ReadoutTrainingCorroborationSHA256.Value ?? "")}\texecution-readout={(receipt.ExecutionCorroboration?.QuotaReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture) ?? "")}\texecution-candidate={(receipt.ExecutionCorroboration?.QuotaCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) ?? "")}\treceipt={receiptDigest}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyBoundaryMount(int step, TapeEventID eventID, string source,
        in PolicyBoundaryMountReceipt receipt, byte[] eventBytes)
        => Emit($"{step}\tpolicy-boundary-mount\t{eventID}\t{source}\tparent={receipt.ParentRunID}\tsource={receipt.SourceChildID}\tdestination={receipt.DestinationChildID}\tcold={receipt.ColdSeedDigest}\ttraining={receipt.TrainingReceiptDigest}\tcontent={receipt.SourceContentDigest}\trelation={receipt.Relation}\tevaluation={receipt.EvaluationStartStep}..{receipt.EvaluationEndStep}\tmount={receipt.MountStep}\tdestination-fingerprint={receipt.DestinationDecisionReadoutFingerprint:X16}\tdestination-revision={receipt.DestinationDecisionReadoutRevision}\tdestination-handshake-digest={receipt.DestinationHandshakeReceiptDigest}\tdestination-handshake-decision-id={receipt.DestinationHandshakeDecisionID}\tverified={(receipt.VerifiedReceipt && receipt.VerifiedContent ? 1 : 0)}\treceipt={receipt.ReceiptDigest}\t{eventBytes.Length}B");

    public void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        string source,
        int action,
        int actionCount,
        int featureCount,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tfeatures={featureCount}\t{eventBytes.Length}B");
    }

    // Frozen journal field: custody= remains the wire token; auditOnlyEventID is the identifier-side name.
    internal void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        string source,
        int action,
        int actionCount,
        int semanticFeatureCount,
        int rawFeatureCount,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tsemantic-features={semanticFeatureCount}\traw-features={rawFeatureCount}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        TapeEventID auditOnlyEventID,
        string source,
        int action,
        int actionCount,
        int semanticFeatureCount,
        int rawFeatureCount,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tsemantic-features={semanticFeatureCount}\traw-features={rawFeatureCount}\tcustody={auditOnlyEventID}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        string source,
        int action,
        int actionCount,
        in PolicyCanonicalStateID canonicalState,
        int rawFeatureCount,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tstate={canonicalState}\traw-features={rawFeatureCount}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        TapeEventID auditOnlyEventID,
        string source,
        int action,
        int actionCount,
        in PolicyCanonicalStateID canonicalState,
        int rawFeatureCount,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tstate={canonicalState}\traw-features={rawFeatureCount}\tcustody={auditOnlyEventID}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        string source,
        int action,
        int actionCount,
        in PolicyCanonicalStateID canonicalState,
        int rawFeatureCount,
        in LoopClosureTeacherPacketProvenance provenance,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tstate={canonicalState}\traw-features={rawFeatureCount}\tfold-revision={provenance.FoldRevision.Value}\tteacher-events={string.Join(',', provenance.MatchedEventIDs.Select(static id => id.Value))}\tteacher-evidence={provenance.EvidenceDigest.Value}\tteacher-witness={provenance.CorroborationDigest.Value}\tteacher-provenance={provenance.ProvenanceDigest.Value}\t{eventBytes.Length}B");
    }

    internal void RecordPolicyExample(
        int step,
        TapeEventID eventID,
        TapeEventID auditOnlyEventID,
        string source,
        int action,
        int actionCount,
        in PolicyCanonicalStateID canonicalState,
        int rawFeatureCount,
        in LoopClosureTeacherPacketProvenance provenance,
        byte[] eventBytes)
    {
        Emit($"{step}\tpolicy-example\t{eventID}\t{source}\taction={action}/{actionCount}\tstate={canonicalState}\traw-features={rawFeatureCount}\tcustody={auditOnlyEventID}\tfold-revision={provenance.FoldRevision.Value}\tteacher-events={string.Join(',', provenance.MatchedEventIDs.Select(static id => id.Value))}\tteacher-evidence={provenance.EvidenceDigest.Value}\tteacher-witness={provenance.CorroborationDigest.Value}\tteacher-provenance={provenance.ProvenanceDigest.Value}\t{eventBytes.Length}B");
    }

    /// The self-signal — this step's HomeWatch excursion token (which probes left their comfort zone). "" = quiet.
    public void Excursion(int step, string token)
    {
        if (token.Length == 0) return;                   // no excursion this step — nothing durable to record
        Emit($"{step}\texcursion\t{token}");
    }

    /// A consolidation (sleep) pass ran — defrag / GC-demotion / index rebuild, the night shift.
    public void Consolidation(int step, string note) => Emit($"{step}\tconsolidation\t{note}");

    private static string RenderField(string value)
    {
        string rendered = value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return rendered.Length <= 160 ? rendered : rendered[..160];
    }

    /// A replay event REFLECTED — corroborated into evidence by a real event exercising a rule it supports (THE
    /// REFLECTION , Pearl.Corroborate). `why` names the corroborated rule ("r14"). One line per TRANSITION
    /// only — Tape.Reflect's monotonic idempotence means a re-corroboration is silent, so the journal is the exact
    /// reflection count. The `vest` line tag is the durable journal wire-format (unchanged for artifact continuity).
    public void Reflect(int step, TapeEventID eventID, string why) => Emit($"{step}\tvest\t{eventID}\t{why}");

    /// The reverse index the sleep pass rebuilt — concept→event postings (the self-indexing tape: the machine
    /// EMITS its own index maps as tape events, so the working set delaminates from the life's size). Compact summary.
    public void Index(int step, string note) => Emit($"{step}\tindex\t{note}");

    /// CHECKPOINT — the keyframe records the durable horizon plus the resident tail, never the shed body
    /// (journal.log owns that history; the keyframe stopped duplicating it so a campaign's keyframe cost
    /// is O(tail), not O(life)). An unshed journal keeps the legacy full-body form so harness/Mesh images
    /// and pre-shed checkpoints stay byte-identical. The sink is remounted by the resume path.
    public void Save(CkptWriter w)
    {
        _sink?.Flush();
        if (_shedLineCount == 0)
        {
            if (_lines.Count > MaxCheckpointLines)
                throw new InvalidDataException($"sink-less journal contains more than {MaxCheckpointLines} lines");
            w.I32(_lines.Count);
            foreach (string line in _lines) w.Str(line);
            return;
        }
        if (_lines.Count > MaxCheckpointLines)
            throw new InvalidDataException($"journal resident tail exceeds {MaxCheckpointLines} lines");
        w.I32(ShedHorizonSentinel);
        w.I32(_shedLineCount);
        w.I32(_lines.Count);
        foreach (string line in _lines) w.Str(line);
    }

    public void Load(CkptReader r, Tape tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        Load(r);
        EnsureRepositoryTaskIndex(tape);
    }

    /// Restore the record: either the legacy full body, or the shed horizon + resident tail. The tape is
    /// the source of record for bytes; Journal resume only restores the durable line journal.
    public void Load(CkptReader r)
    {
        if (_lines.Count != 0 || _shedLineCount != 0) throw new InvalidOperationException("Journal.Load requires a fresh journal");
        int n = r.I32();
        if (n == ShedHorizonSentinel)
        {
            int shed = r.I32();
            int tail = r.I32();
            if (shed <= 0 || tail < 0 || tail > MaxCheckpointLines)
                throw new InvalidDataException($"journal checkpoint shed horizon {shed} / tail {tail} is malformed");
            _shedLineCount = shed;
            for (int i = 0; i < tail; i++) _lines.Add(r.Str());
        }
        else
        {
            if (n < 0 || n > MaxCheckpointLines)
                throw new InvalidDataException($"journal checkpoint contains {n} lines; maximum is {MaxCheckpointLines}");
            for (int i = 0; i < n; i++) _lines.Add(r.Str());
        }
        _checkpointLineCount = LineCount;
    }

    /// Reset journal.log to the checkpoint's exact line horizon — the resume path's file reset: rows a
    /// kill appended past the checkpoint horizon are shed here, then the live sink re-mounts and appends
    /// onward. An unshed journal rewrites the file whole from RAM; a shed journal TRUNCATES the existing
    /// file at the shed horizon (its only copy of that prefix) and re-lands the resident tail.
    public void Rewrite(Run run, bool header = true)
    {
        if (_shedLineCount == 0)
        {
            System.Text.StringBuilder sb = new(LogHeader.Length + 1 + _lines.Count * 48);
            if (header) sb.Append(LogHeader).Append('\n');
            foreach (string line in _lines) sb.Append(line).Append('\n');
            run.Write("journal.log", sb.ToString());
            return;
        }
        string path = run.PathOf("journal.log");
        if (!File.Exists(path))
            throw new InvalidDataException($"journal shed {_shedLineCount} lines to disk but {path} is missing — the durable record cannot be reconstructed");
        long horizon = FindLineHorizonOffset(path, (header ? 1 : 0) + _shedLineCount);
        run.Truncate("journal.log", horizon);
        using StreamWriter tailW = new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (string line in _lines) { tailW.Write(line); tailW.Write('\n'); }
    }

    /// Byte offset just past the Nth '\n' — the truncation horizon for a line-granular incremental artifact.
    private static long FindLineHorizonOffset(string path, int lineCount)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] buffer = new byte[64 * 1024];
        long offset = 0;
        int remaining = lineCount;
        int read;
        while (remaining > 0 && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n' && --remaining == 0) { offset += i + 1; return offset; }
                if (i == read - 1) offset += read;
            }
        throw new InvalidDataException($"{path} holds fewer than {lineCount} lines — the journal's shed horizon is not durable");
    }

    /// MemStat census read — the RESIDENT line record's chars (shed lines cost no RAM). Counts only.
    internal long LineChars()
    {
        long chars = 0;
        foreach (string line in _lines) chars += line.Length;
        return chars;
    }

    private void ValidateCursor(int cursor)
    {
        if (cursor < _shedLineCount || cursor > LineCount)
            throw new InvalidDataException($"journal checkpoint cursor {cursor} is outside [{_shedLineCount}, {LineCount}] lines");
    }
}
