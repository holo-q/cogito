namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// One arm-neutral, step-indexed fuel authority for the registered paired gate.
/// The schedule is a prefix budget, not an opportunity quota: a step receives
/// exactly floor(T*s/H)-floor(T*(s-1)/H) on each axis, then unused work is
/// refunded.  This keeps the comparison currency independent of how many
/// opportunities an arm happens to discover.
internal readonly record struct EmlPairedFuelSchedule(
    string Identity,
    int Horizon,
    EmlDeliberationCounts Total,
    string Digest)
{
    internal const int SchemaVersion = 2;
    internal const string SidecarFile = "eml_paired_fuel_schedule.ron";

    internal static EmlPairedFuelSchedule Create(string identity, int horizon, in EmlDeliberationCounts total)
    {
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("paired fuel schedule requires an identity", nameof(identity));
        if (horizon <= 0) throw new ArgumentOutOfRangeException(nameof(horizon));
        total.ValidateNonnegative("paired fuel schedule total");
        string digest = ComputeDigest(identity, horizon, in total);
        return new(identity, horizon, total, digest);
    }

    internal EmlPairedFuelSchedule Validate()
    {
        EmlDeliberationCounts total = Total;
        if (string.IsNullOrWhiteSpace(Identity) || Horizon <= 0 || Digest.Length != 64
            || !string.Equals(Digest, ComputeDigest(Identity, Horizon, in total), StringComparison.Ordinal))
            throw new InvalidDataException("paired fuel schedule digest or bounds are invalid");
        Total.ValidateNonnegative("paired fuel schedule total");
        return this;
    }

    internal EmlDeliberationCounts Prefix(int completedSteps)
    {
        Validate();
        if (completedSteps < 0 || completedSteps > Horizon)
            throw new ArgumentOutOfRangeException(nameof(completedSteps));
        return Scale(Total, completedSteps, Horizon);
    }

    internal EmlDeliberationCounts Row(int step)
    {
        if (step < 0 || step >= Horizon) throw new ArgumentOutOfRangeException(nameof(step));
        EmlDeliberationCounts prior = Prefix(step);
        EmlDeliberationCounts next = Prefix(step + 1);
        return EmlDeliberationCounts.Subtract(in next, in prior);
    }

    internal static string ComputeDigest(string identity, int horizon, in EmlDeliberationCounts total)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', SchemaVersion, identity, horizon, total, "paired-fuel-schedule-v1"))));

    private static EmlDeliberationCounts Scale(in EmlDeliberationCounts value, long numerator, long denominator)
        => new(Scale(value.CandidateEvaluations, numerator, denominator), Scale(value.LogicalProgramPoints, numerator, denominator),
            Scale(value.ExecutedProgramPoints, numerator, denominator), Scale(value.InverseTransforms, numerator, denominator),
            Scale(value.HashProbes, numerator, denominator), Scale(value.JoinAttempts, numerator, denominator),
            Scale(value.JoinHits, numerator, denominator), Scale(value.ProcessTerms, numerator, denominator),
            Scale(value.VerifierProgramPoints, numerator, denominator), Scale(value.CandidateSupplyItems, numerator, denominator),
            Scale(value.LawRewriteApplications, numerator, denominator), Scale(value.LawRewriteTreeNodes, numerator, denominator));

    private static long Scale(long value, long numerator, long denominator)
        => checked(value * numerator / denominator);
}

internal readonly record struct EmlPairedFuelScheduleRow(
    int Step,
    EmlDeliberationCounts Planned,
    EmlDeliberationCounts Actual,
    EmlDeliberationCounts Refund,
    string PreviousDigest,
    string Digest);

internal sealed class EmlPairedFuelScheduleCursor
{
    private readonly List<EmlPairedFuelScheduleRow> _rows;

    private EmlPairedFuelScheduleCursor(string scheduleDigest, int horizon, int lastStep, int rowCount, string rowDigest,
        in EmlDeliberationCounts planned, in EmlDeliberationCounts actual, in EmlDeliberationCounts refund,
        string cursorDigest, List<EmlPairedFuelScheduleRow> rows)
    {
        ScheduleDigest = scheduleDigest;
        Horizon = horizon;
        LastStep = lastStep;
        RowCount = rowCount;
        RowDigest = rowDigest;
        Planned = planned;
        Actual = actual;
        Refund = refund;
        CursorDigest = cursorDigest;
        _rows = rows;
    }

    internal string ScheduleDigest { get; private set; }
    internal int Horizon { get; private set; }
    internal int LastStep { get; private set; }
    internal int RowCount { get; private set; }
    internal string RowDigest { get; private set; }
    internal EmlDeliberationCounts Planned { get; private set; }
    internal EmlDeliberationCounts Actual { get; private set; }
    internal EmlDeliberationCounts Refund { get; private set; }
    internal string CursorDigest { get; private set; }
    internal int RecordCount => _rows.Count;
    internal EmlPairedFuelScheduleRow ReadRecord(int index) => _rows[index];
    internal EmlPairedFuelScheduleRow[] ReadRows() => _rows.ToArray();

    internal static EmlPairedFuelScheduleCursor FromRows(in EmlPairedFuelSchedule schedule, IReadOnlyList<EmlPairedFuelScheduleRow> rows)
    {
        EmlPairedFuelScheduleCursor cursor = Create(in schedule);
        for (int i = 0; i < rows.Count; i++)
        {
            EmlPairedFuelScheduleRow row = rows[i];
            if (row.Step != i) throw new InvalidDataException("paired fuel checkpoint rows are not contiguous");
            EmlDeliberationCounts expected = schedule.Row(i);
            if (row.Planned != expected) throw new InvalidDataException("paired fuel checkpoint row disagrees with schedule");
            EmlDeliberationCounts planned = row.Planned, actual = row.Actual;
            cursor = cursor.Append(in schedule, row.Step, in planned, in actual);
            if (cursor.ReadRecord(i) != row) throw new InvalidDataException("paired fuel checkpoint row digest mismatch");
        }
        return cursor.Validate(in schedule);
    }

    internal static EmlPairedFuelScheduleCursor Create(in EmlPairedFuelSchedule schedule)
    {
        EmlDeliberationCounts zero = EmlDeliberationCounts.Zero;
        string rowDigest = ComputeRowDigest(schedule.Digest, -1, in zero, in zero, in zero, "genesis");
        return CreateCursor(schedule.Digest, schedule.Horizon, -1, 0, rowDigest, in zero, in zero, in zero,
            new List<EmlPairedFuelScheduleRow>(schedule.Horizon));
    }

    internal EmlPairedFuelScheduleCursor Append(in EmlPairedFuelSchedule schedule, int step, in EmlDeliberationCounts planned, in EmlDeliberationCounts actual)
    {
        schedule.Validate();
        if (ScheduleDigest != schedule.Digest || Horizon != schedule.Horizon || step != LastStep + 1 || RowCount != step
            || _rows.Count != RowCount || (RowCount > 0 && _rows[^1].Digest != RowDigest))
            throw new InvalidDataException("paired fuel schedule cursor has a gap, duplicate, or schedule mismatch");
        planned.ValidateNonnegative("paired schedule planned row");
        actual.ValidateNonnegative("paired schedule actual row");
        if (planned != schedule.Row(step)) throw new InvalidDataException("paired fuel schedule row disagrees with its prefix authority");
        if (!Within(actual, planned)) throw new InvalidDataException("paired fuel schedule actual row exceeds its planned wallet");
        EmlDeliberationCounts refund = EmlDeliberationCounts.Subtract(in planned, in actual);
        EmlDeliberationCounts existingPlanned = Planned, existingActual = Actual, existingRefund = Refund;
        EmlDeliberationCounts cumulativePlanned = EmlDeliberationCounts.Add(in existingPlanned, in planned);
        EmlDeliberationCounts cumulativeActual = EmlDeliberationCounts.Add(in existingActual, in actual);
        EmlDeliberationCounts cumulativeRefund = EmlDeliberationCounts.Add(in existingRefund, in refund);
        string rowDigest = ComputeRowDigest(ScheduleDigest, step, in planned, in actual, in refund, RowDigest);
        EmlPairedFuelScheduleRow row = new(step, planned, actual, refund, RowDigest, rowDigest);
        _rows.Add(row);
        LastStep = step;
        RowCount = checked(RowCount + 1);
        RowDigest = rowDigest;
        Planned = cumulativePlanned;
        Actual = cumulativeActual;
        Refund = cumulativeRefund;
        CursorDigest = ComputeCursorDigest(ScheduleDigest, Horizon, LastStep, RowCount, RowDigest,
            in cumulativePlanned, in cumulativeActual, in cumulativeRefund);
        return this;
    }

    internal EmlPairedFuelScheduleCursor Validate(in EmlPairedFuelSchedule schedule)
    {
        schedule.Validate();
        EmlDeliberationCounts actual = Actual, refund = Refund, planned = Planned;
        EmlDeliberationCounts closed = EmlDeliberationCounts.Add(in actual, in refund);
        if (ScheduleDigest != schedule.Digest || Horizon != schedule.Horizon || RowCount < 0 || RowCount > Horizon
            || _rows.Count != RowCount || LastStep != RowCount - 1
            || RowDigest.Length != 64 || planned != closed || CursorDigest != ComputeCursorDigest(ScheduleDigest, Horizon, LastStep, RowCount, RowDigest, in planned, in actual, in refund))
            throw new InvalidDataException("paired fuel schedule cursor does not close");
        Planned.ValidateNonnegative("paired schedule cumulative planned");
        Actual.ValidateNonnegative("paired schedule cumulative actual");
        Refund.ValidateNonnegative("paired schedule cumulative refund");
        EmlDeliberationCounts expected = schedule.Prefix(RowCount);
        if (expected != Planned) throw new InvalidDataException("paired fuel schedule cumulative prefix drifted");
        EmlDeliberationCounts zero = EmlDeliberationCounts.Zero;
        string genesis = ComputeRowDigest(schedule.Digest, -1, in zero, in zero, in zero, "genesis");
        if ((RowCount == 0 && RowDigest != genesis)
            || (RowCount > 0 && (_rows[^1].Step != LastStep || _rows[^1].Digest != RowDigest)))
            throw new InvalidDataException("paired fuel schedule terminal row does not bind the cursor");
        return this;
    }

    internal EmlPairedFuelScheduleCursor ValidateClosed(in EmlPairedFuelSchedule schedule)
    {
        Validate(in schedule);
        if (RowCount != schedule.Horizon || LastStep != schedule.Horizon - 1 || Planned != schedule.Total)
            throw new InvalidDataException("paired fuel schedule cursor is not closed at its terminal prefix");
        return this;
    }

    /// Resume authority is the checkpoint cursor. A sidecar may lag when a process dies after the
    /// checkpoint record is durable but before the sidecar replacement lands; it must never advance
    /// the resumed prefix or silently disagree at the same horizon.
    internal static EmlPairedFuelScheduleCursor ReconcileResumeCursor(in EmlPairedFuelSchedule schedule,
        in EmlPairedFuelScheduleCursor checkpoint, in EmlPairedFuelScheduleCursor sidecar)
    {
        EmlPairedFuelScheduleCursor checkpointCursor = checkpoint.Validate(in schedule);
        EmlPairedFuelScheduleCursor sidecarCursor = sidecar.Validate(in schedule);
        if (sidecarCursor.RowCount > checkpointCursor.RowCount)
            throw new InvalidDataException("paired fuel sidecar cursor is ahead of checkpoint authority");
        if (sidecarCursor.RowCount == checkpointCursor.RowCount && sidecarCursor.CursorDigest != checkpointCursor.CursorDigest)
            throw new InvalidDataException("paired fuel sidecar cursor disagrees with checkpoint authority");
        return checkpointCursor;
    }

    internal static string ComputeRowDigest(string scheduleDigest, int step, in EmlDeliberationCounts planned,
        in EmlDeliberationCounts actual, in EmlDeliberationCounts refund, string previousDigest)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', scheduleDigest, step, planned, actual, refund, previousDigest, "paired-fuel-row-v1"))));

    internal static string ComputeCursorDigest(string scheduleDigest, int horizon, int lastStep, int rowCount, string rowDigest,
        in EmlDeliberationCounts planned, in EmlDeliberationCounts actual, in EmlDeliberationCounts refund)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', scheduleDigest, horizon, lastStep, rowCount, rowDigest, planned, actual, refund, "paired-fuel-cursor-v1"))));

    private static EmlPairedFuelScheduleCursor CreateCursor(string scheduleDigest, int horizon, int lastStep, int rowCount, string rowDigest,
        in EmlDeliberationCounts planned, in EmlDeliberationCounts actual, in EmlDeliberationCounts refund,
        List<EmlPairedFuelScheduleRow> rows)
        => new(scheduleDigest, horizon, lastStep, rowCount, rowDigest, in planned, in actual, in refund,
            ComputeCursorDigest(scheduleDigest, horizon, lastStep, rowCount, rowDigest, in planned, in actual, in refund), rows);


    private static bool Within(in EmlDeliberationCounts actual, in EmlDeliberationCounts planned)
        => actual.CandidateEvaluations <= planned.CandidateEvaluations && actual.LogicalProgramPoints <= planned.LogicalProgramPoints
        && actual.ExecutedProgramPoints <= planned.ExecutedProgramPoints && actual.InverseTransforms <= planned.InverseTransforms
        && actual.HashProbes <= planned.HashProbes && actual.JoinAttempts <= planned.JoinAttempts && actual.JoinHits <= planned.JoinHits
        && actual.ProcessTerms <= planned.ProcessTerms && actual.VerifierProgramPoints <= planned.VerifierProgramPoints
        && actual.CandidateSupplyItems <= planned.CandidateSupplyItems && actual.LawRewriteApplications <= planned.LawRewriteApplications
        && actual.LawRewriteTreeNodes <= planned.LawRewriteTreeNodes;
}

[RonObject]
internal partial class EmlPairedFuelScheduleRowDocument
{
    public int step;
    public long[] planned = new long[12];
    public long[] actual = new long[12];
    public long[] refund = new long[12];
    public string previousDigest = "";
    public string digest = "";
}

[RonObject]
internal partial class EmlPairedFuelScheduleDocument
{
    public int schemaVersion = EmlPairedFuelSchedule.SchemaVersion;
    public string identity = "";
    public int horizon;
    public string scheduleDigest = "";
    public int lastStep;
    public int rowCount;
    public string rowDigest = "";
    public string cursorDigest = "";
    public long[] total = new long[12];
    public long[] planned = new long[12];
    public long[] actual = new long[12];
    public long[] refund = new long[12];
    public List<EmlPairedFuelScheduleRowDocument> rows = new();
}

internal static class EmlPairedFuelScheduleJournal
{
    internal static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        EmlDeliberationCounts total = new(17, 29, 43, 5, 31, 47, 11, 53, 19, 37, 23, 41);
        EmlPairedFuelSchedule schedule = EmlPairedFuelSchedule.Create("paired-fuel-fixture-v1", 7, in total);
        EmlPairedFuelScheduleCursor left = EmlPairedFuelScheduleCursor.Create(in schedule);
        EmlPairedFuelScheduleCursor right = EmlPairedFuelScheduleCursor.Create(in schedule);
        EmlDeliberationCounts zero = EmlDeliberationCounts.Zero;
        bool duplicateRejected = false, gapRejected = false, mismatchRejected = false, openTailRejected = false;
        try { _ = left.Append(in schedule, 1, in total, in zero); }
        catch (InvalidDataException) { gapRejected = true; }
        EmlPairedFuelSchedule mismatched = EmlPairedFuelSchedule.Create("paired-fuel-other-v1", schedule.Horizon, in total);
        EmlDeliberationCounts firstRow = schedule.Row(0);
        try { _ = left.Append(in mismatched, 0, in firstRow, in zero); }
        catch (InvalidDataException) { mismatchRejected = true; }

        EmlDeliberationCounts halfActual = EmlDeliberationCounts.Zero;
        for (int step = 0; step < schedule.Horizon; step++)
        {
            EmlDeliberationCounts row = schedule.Row(step);
            EmlDeliberationCounts actual = Half(row);
            left = left.Append(in schedule, step, in row, in zero);
            right = right.Append(in schedule, step, in row, in actual);
            halfActual = EmlDeliberationCounts.Add(in halfActual, in actual);
        }
        EmlDeliberationCounts lastRow = schedule.Row(schedule.Horizon - 1);
        try { _ = right.Append(in schedule, schedule.Horizon - 1, in lastRow, in zero); }
        catch (InvalidDataException) { duplicateRejected = true; }
        left.ValidateClosed(in schedule); right.ValidateClosed(in schedule);
        EmlPairedFuelScheduleCursor open = EmlPairedFuelScheduleCursor.Create(in schedule);
        try { _ = open.ValidateClosed(in schedule); }
        catch (InvalidDataException) { openTailRejected = true; }
        EmlDeliberationCounts rightActual = right.Actual, rightRefund = right.Refund, leftActual = left.Actual, leftRefund = left.Refund;
        EmlDeliberationCounts leftPlanned = left.Planned, rightPlanned = right.Planned;
        bool prefixes = left.Planned == total && right.Planned == total
            && left.Actual == zero && right.Actual == halfActual
            && left.Refund == total && right.Planned == EmlDeliberationCounts.Add(in rightActual, in rightRefund);
        byte[] encoded = Encode(in schedule, right);
        (EmlPairedFuelSchedule loadedSchedule, EmlPairedFuelScheduleCursor loadedCursor) = Decode(encoded);
        bool saveLoadSave = encoded.AsSpan().SequenceEqual(Encode(in loadedSchedule, loadedCursor));
        string forgedRow = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("forged-row")));
        string forgedCursor = EmlPairedFuelScheduleCursor.ComputeCursorDigest(right.ScheduleDigest, right.Horizon, right.LastStep,
            right.RowCount, forgedRow, in rightPlanned, in rightActual, in rightRefund);
        bool forgedRejected = false;
        try { _ = Decode(encoded.AsSpan().ToArray().ReplaceUtf8(right.RowDigest, forgedRow).ReplaceUtf8(right.CursorDigest, forgedCursor)); }
        catch (InvalidDataException) { forgedRejected = true; }
        bool floor = EmlDeliberationCounts.Subtract(in total, in leftPlanned) == zero;
        bool conservation = leftPlanned == EmlDeliberationCounts.Add(in leftActual, in leftRefund) && rightPlanned == EmlDeliberationCounts.Add(in rightActual, in rightRefund);
        string root = Path.GetFullPath(Path.Combine(".tmp", $"paired-fuel-journal-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(root);
        bool atomicOpenExact = false, tornRejected = false, laggingCheckpointWins = false, aheadRejected = false;
        EmlPairedFuelScheduleRow[] expectedRows = right.ReadRows();
        EmlPairedFuelScheduleCursor checkpointCursor = EmlPairedFuelScheduleCursor.FromRows(in schedule, expectedRows.AsSpan(0, 3).ToArray());
        EmlPairedFuelScheduleCursor laggingSidecar = EmlPairedFuelScheduleCursor.FromRows(in schedule, expectedRows.AsSpan(0, 2).ToArray());
        EmlPairedFuelScheduleCursor aheadSidecar = EmlPairedFuelScheduleCursor.FromRows(in schedule, expectedRows.AsSpan(0, 4).ToArray());
        laggingCheckpointWins = EmlPairedFuelScheduleCursor.ReconcileResumeCursor(in schedule, in checkpointCursor, in laggingSidecar).CursorDigest == checkpointCursor.CursorDigest;
        try { _ = EmlPairedFuelScheduleCursor.ReconcileResumeCursor(in schedule, in checkpointCursor, in aheadSidecar); }
        catch (InvalidDataException) { aheadRejected = true; }
        try
        {
            Run run = Run.Create(Path.Combine(root, "run"));
            WriteAtomic(run, in schedule, right);
            byte[] persisted = File.ReadAllBytes(run.PathOf(EmlPairedFuelSchedule.SidecarFile));
            (EmlPairedFuelSchedule openedSchedule, EmlPairedFuelScheduleCursor openedCursor) = Decode(persisted);
            atomicOpenExact = openedSchedule == schedule && openedCursor.ReadRows().AsSpan().SequenceEqual(expectedRows);
            File.WriteAllBytes(run.PathOf(EmlPairedFuelSchedule.SidecarFile), persisted[..^1]);
            try { _ = Decode(File.ReadAllBytes(run.PathOf(EmlPairedFuelSchedule.SidecarFile))); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { tornRejected = true; }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        bool pass = floor && prefixes && conservation && saveLoadSave && forgedRejected && gapRejected && duplicateRejected && mismatchRejected && openTailRejected
            && atomicOpenExact && tornRejected && laggingCheckpointWins && aheadRejected;
        output.WriteLine($"  paired fuel schedule fixture · floor={(floor ? "exact" : "BROKEN")} · unequal-opportunities={(right.Actual != left.Actual && right.Planned == left.Planned ? "same-prefix" : "DRIFT")} · refund={(conservation ? "closed" : "BROKEN")} · save-load-save={(saveLoadSave ? "exact" : "DRIFT")} · atomic-open={(atomicOpenExact ? "exact" : "DRIFT")} · torn={(tornRejected ? "rejected" : "ACCEPTED")} · crash-window={(laggingCheckpointWins ? "checkpoint-wins" : "SIDECAR-OVERRIDE")} · ahead={(aheadRejected ? "rejected" : "ACCEPTED")} · forged={(forgedRejected ? "rejected" : "ACCEPTED")} · sequence={(gapRejected && duplicateRejected ? "rejected" : "ACCEPTED")} · mismatch={(mismatchRejected ? "rejected" : "ACCEPTED")} · terminal={(openTailRejected ? "closed" : "OPEN")} · {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    internal static byte[] Encode(in EmlPairedFuelSchedule schedule, EmlPairedFuelScheduleCursor cursor)
    {
        cursor.Validate(in schedule);
        EmlPairedFuelScheduleDocument document = ToDocument(in schedule, cursor);
        return RonSerializer.SerializeToUtf8(in document);
    }

    internal static (EmlPairedFuelSchedule Schedule, EmlPairedFuelScheduleCursor Cursor) Decode(ReadOnlySpan<byte> bytes)
    {
        EmlPairedFuelScheduleDocument document = RonSerializer.Deserialize<EmlPairedFuelScheduleDocument>(bytes);
        if (document.schemaVersion != EmlPairedFuelSchedule.SchemaVersion) throw new InvalidDataException("unsupported paired fuel schedule schema");
        EmlDeliberationCounts total = Counts(document.total), planned = Counts(document.planned), actual = Counts(document.actual), refund = Counts(document.refund);
        EmlPairedFuelSchedule schedule = new EmlPairedFuelSchedule(document.identity, document.horizon, total, document.scheduleDigest).Validate();
        if (document.rows is null || document.rows.Count != document.rowCount)
            throw new InvalidDataException("paired fuel schedule row census disagrees with its cursor");
        EmlPairedFuelScheduleCursor cursor = EmlPairedFuelScheduleCursor.Create(in schedule);
        foreach (EmlPairedFuelScheduleRowDocument rowDocument in document.rows)
        {
            EmlDeliberationCounts rowPlanned = Counts(rowDocument.planned), rowActual = Counts(rowDocument.actual), rowRefund = Counts(rowDocument.refund);
            cursor.Append(in schedule, rowDocument.step, in rowPlanned, in rowActual);
            EmlPairedFuelScheduleRow row = cursor.ReadRecord(cursor.RecordCount - 1);
            if (row.Refund != rowRefund || row.PreviousDigest != rowDocument.previousDigest || row.Digest != rowDocument.digest)
                throw new InvalidDataException($"paired fuel schedule row {rowDocument.step} does not close its recorded chain");
        }
        if (cursor.LastStep != document.lastStep || cursor.RowCount != document.rowCount || cursor.RowDigest != document.rowDigest
            || cursor.CursorDigest != document.cursorDigest || cursor.Planned != planned || cursor.Actual != actual || cursor.Refund != refund)
            throw new InvalidDataException("paired fuel schedule document summary disagrees with its row records");
        cursor.Validate(in schedule);
        return (schedule, cursor);
    }

    internal static void WriteAtomic(Run run, in EmlPairedFuelSchedule schedule, EmlPairedFuelScheduleCursor cursor)
    {
        byte[] bytes = Encode(in schedule, cursor);
        run.WriteAtomic(EmlPairedFuelSchedule.SidecarFile, stream => stream.Write(bytes));
    }

    private static EmlPairedFuelScheduleDocument ToDocument(in EmlPairedFuelSchedule schedule, EmlPairedFuelScheduleCursor cursor)
    {
        EmlPairedFuelScheduleDocument document = new()
        {
            identity = schedule.Identity, horizon = schedule.Horizon, scheduleDigest = schedule.Digest,
            lastStep = cursor.LastStep, rowCount = cursor.RowCount, rowDigest = cursor.RowDigest, cursorDigest = cursor.CursorDigest,
            total = Values(schedule.Total), planned = Values(cursor.Planned), actual = Values(cursor.Actual), refund = Values(cursor.Refund),
        };
        for (int i = 0; i < cursor.RecordCount; i++)
        {
            EmlPairedFuelScheduleRow row = cursor.ReadRecord(i);
            document.rows.Add(new EmlPairedFuelScheduleRowDocument
            {
                step = row.Step, planned = Values(row.Planned), actual = Values(row.Actual), refund = Values(row.Refund),
                previousDigest = row.PreviousDigest, digest = row.Digest,
            });
        }
        return document;
    }

    private static long[] Values(in EmlDeliberationCounts value)
        => [value.CandidateEvaluations, value.LogicalProgramPoints, value.ExecutedProgramPoints, value.InverseTransforms, value.HashProbes,
            value.JoinAttempts, value.JoinHits, value.ProcessTerms, value.VerifierProgramPoints, value.CandidateSupplyItems,
            value.LawRewriteApplications, value.LawRewriteTreeNodes];

    private static EmlDeliberationCounts Counts(long[] values)
    {
        if (values is null || values.Length != 12) throw new InvalidDataException("paired fuel schedule sidecar has the wrong axis count");
        return new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11]);
    }

    private static EmlDeliberationCounts Half(in EmlDeliberationCounts value)
        => new(value.CandidateEvaluations / 2, value.LogicalProgramPoints / 2, value.ExecutedProgramPoints / 2,
            value.InverseTransforms / 2, value.HashProbes / 2, value.JoinAttempts / 2, value.JoinHits / 2,
            value.ProcessTerms / 2, value.VerifierProgramPoints / 2, value.CandidateSupplyItems / 2,
            value.LawRewriteApplications / 2, value.LawRewriteTreeNodes / 2);

}

file static class EmlPairedFuelScheduleFixtureBytes
{
    internal static byte[] ReplaceUtf8(this byte[] bytes, string oldValue, string newValue)
    {
        string text = Encoding.UTF8.GetString(bytes);
        if (!text.Contains(oldValue, StringComparison.Ordinal)) throw new InvalidDataException("paired fuel fixture could not locate cursor digest");
        return Encoding.UTF8.GetBytes(text.Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
