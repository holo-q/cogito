namespace Cogito;

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public enum EmlDeliberationOutcomes
{
    Solved,
    NoCandidate,
    Exhausted,
    Suppressed,
    Interrupted,
    Rejected,
    Reused,
}

/// Search units are deliberately named instead of collapsed into a generic fuel integer. Logical ladder points
/// describe the conserved request budget; executed points describe cache misses; process terms are the proof policy's
/// per-probe work accumulated over all probes and certificate cycles.
public readonly record struct EmlDeliberationQuota(
    long CandidateEvaluations,
    long LogicalProgramPoints,
    long ExecutedProgramPoints,
    long InverseTransforms,
    long HashProbes,
    long JoinAttempts,
    long JoinHits,
    long ProcessTerms,
    long VerifierProgramPoints,
    long CandidateSupplyItems = 0,
    long LawRewriteApplications = 0,
    long LawRewriteTreeNodes = 0)
{
    public static EmlDeliberationQuota Default => new(
        CandidateEvaluations: 100_000,
        LogicalProgramPoints: 2_000_000,
        ExecutedProgramPoints: 2_000_000,
        InverseTransforms: 2_000_000,
        HashProbes: 2_000_000,
        JoinAttempts: 4_000_000,
        JoinHits: 100_000,
        ProcessTerms: 12_000_000,
        VerifierProgramPoints: 2_000_000,
        CandidateSupplyItems: 100_000,
        LawRewriteApplications: 2_000_000,
        LawRewriteTreeNodes: 4_000_000);

    /// The paired gate records this concrete profile in both arm configs. Keeping a
    /// named non-zero profile separate from the all-zero struct sentinel makes the
    /// registration visible in the checkpoint and authority RON rather than relying
    /// on runtime normalization of a missing value.
    public static EmlDeliberationQuota PairedGateNominal => Default;

    public static EmlDeliberationQuota TightAssay => new(
        CandidateEvaluations: 1,
        LogicalProgramPoints: 1,
        ExecutedProgramPoints: 1,
        InverseTransforms: 0,
        HashProbes: 0,
        JoinAttempts: 0,
        JoinHits: 0,
        ProcessTerms: 0,
        VerifierProgramPoints: 0,
        CandidateSupplyItems: 0,
        LawRewriteApplications: 0,
        LawRewriteTreeNodes: 0);

    public void Validate()
    {
        if (CandidateEvaluations < 0 || LogicalProgramPoints < 0 || ExecutedProgramPoints < 0
            || InverseTransforms < 0 || HashProbes < 0 || JoinAttempts < 0 || JoinHits < 0
            || ProcessTerms < 0 || VerifierProgramPoints < 0 || CandidateSupplyItems < 0
            || LawRewriteApplications < 0 || LawRewriteTreeNodes < 0)
            throw new ArgumentOutOfRangeException(nameof(EmlDeliberationQuota), "deliberation quota fields cannot be negative");
    }
}

public readonly record struct EmlDeliberationCounts(
    long CandidateEvaluations,
    long LogicalProgramPoints,
    long ExecutedProgramPoints,
    long InverseTransforms,
    long HashProbes,
    long JoinAttempts,
    long JoinHits,
    long ProcessTerms,
    long VerifierProgramPoints,
    long CandidateSupplyItems = 0,
    long LawRewriteApplications = 0,
    long LawRewriteTreeNodes = 0)
{
    public static EmlDeliberationCounts Zero => default;
    public static string[] AxisNames { get; } =
    [
        "candidate_evaluations", "logical_program_points", "executed_program_points", "inverse_transforms",
        "hash_probes", "join_attempts", "join_hits", "process_terms", "verifier_program_points",
        "candidate_supply_items", "law_rewrite_applications", "law_rewrite_tree_nodes",
    ];

    public static EmlDeliberationCounts Add(in EmlDeliberationCounts left, in EmlDeliberationCounts right)
        => new(checked(left.CandidateEvaluations + right.CandidateEvaluations),
            checked(left.LogicalProgramPoints + right.LogicalProgramPoints),
            checked(left.ExecutedProgramPoints + right.ExecutedProgramPoints),
            checked(left.InverseTransforms + right.InverseTransforms),
            checked(left.HashProbes + right.HashProbes),
            checked(left.JoinAttempts + right.JoinAttempts),
            checked(left.JoinHits + right.JoinHits),
            checked(left.ProcessTerms + right.ProcessTerms),
            checked(left.VerifierProgramPoints + right.VerifierProgramPoints),
            checked(left.CandidateSupplyItems + right.CandidateSupplyItems),
            checked(left.LawRewriteApplications + right.LawRewriteApplications),
            checked(left.LawRewriteTreeNodes + right.LawRewriteTreeNodes));

    public static EmlDeliberationCounts Subtract(in EmlDeliberationCounts left, in EmlDeliberationCounts right)
        => new(left.CandidateEvaluations - right.CandidateEvaluations,
            left.LogicalProgramPoints - right.LogicalProgramPoints,
            left.ExecutedProgramPoints - right.ExecutedProgramPoints,
            left.InverseTransforms - right.InverseTransforms,
            left.HashProbes - right.HashProbes,
            left.JoinAttempts - right.JoinAttempts,
            left.JoinHits - right.JoinHits,
            left.ProcessTerms - right.ProcessTerms,
            left.VerifierProgramPoints - right.VerifierProgramPoints,
            left.CandidateSupplyItems - right.CandidateSupplyItems,
            left.LawRewriteApplications - right.LawRewriteApplications,
            left.LawRewriteTreeNodes - right.LawRewriteTreeNodes);

    public void ValidateNonnegative(string label)
    {
        if (CandidateEvaluations < 0 || LogicalProgramPoints < 0 || ExecutedProgramPoints < 0
            || InverseTransforms < 0 || HashProbes < 0 || JoinAttempts < 0 || JoinHits < 0
            || ProcessTerms < 0 || VerifierProgramPoints < 0 || CandidateSupplyItems < 0
            || LawRewriteApplications < 0 || LawRewriteTreeNodes < 0)
            throw new InvalidDataException($"EML deliberation {label} has a negative journal field");
    }
}

public readonly record struct EmlDeliberationAdmission(
    string ReservationID,
    string ObligationID,
    int SourcePredictionID,
    string DiscoveryEpoch,
    string Frontier,
    string SolverRevision,
    string VerifierRevision,
    EmlDeliberationQuota Planned,
    EmlDeliberationCounts Held);

public readonly record struct EmlDeliberationPhaseReceipt(
    string ReservationID,
    string Phase,
    int Sequence,
    EmlDeliberationCounts OpeningRemaining,
    EmlDeliberationCounts Actual,
    EmlDeliberationCounts ClosingRemaining,
    long WallTicks)
{
    public double WallMilliseconds => WallTicks * 1000.0 / Stopwatch.Frequency;
}

public readonly record struct EmlDeliberationSettlement(
    string ReservationID,
    EmlDeliberationOutcomes Outcome,
    EmlDeliberationCounts Planned,
    EmlDeliberationCounts Held,
    EmlDeliberationCounts Actual,
    EmlDeliberationCounts Refund,
    long WallTicks,
    string Detail)
{
    public double WallMilliseconds => WallTicks * 1000.0 / Stopwatch.Frequency;
}

public sealed class EmlDeliberationExhaustedException : InvalidOperationException
{
    internal EmlDeliberationExhaustedException(string unit, long requested, long remaining)
        : base($"EML deliberation quota exhausted before {unit}: requested={requested}, remaining={remaining}")
    {
        Unit = unit;
        Requested = requested;
        Remaining = remaining;
    }

    public string Unit { get; }
    public long Requested { get; }
    public long Remaining { get; }
}

/// One obligation's reservation. The lease is mutable only in memory; the journal stores immutable admission and
/// terminal records. Every operation is reserved before it executes, so a failed reservation cannot overrun a field.
public sealed class EmlDeliberationLease
{
    private readonly EmlDeliberationJournal? _journal;
    private readonly EmlDeliberationQuota _planned;
    private readonly EmlDeliberationCounts _reservedTotal;
    private EmlDeliberationCounts _remaining;
    private readonly bool _reused;
    private EmlDeliberationCounts _actual;
    private long _wallStart;
    private EmlDeliberationCounts _phaseStartActual;
    private EmlDeliberationCounts _phaseStartRemaining;
    private long _phaseStartWall;
    private string? _phase;
    private int _phaseSequence;
    private bool _settled;

    internal EmlDeliberationLease(
        EmlDeliberationAdmission admission,
        EmlDeliberationJournal? journal,
        bool reused = false)
    {
        Admission = admission;
        _planned = admission.Planned;
        _reservedTotal = admission.Held;
        _remaining = admission.Held;
        _reused = reused;
        _journal = journal;
        _wallStart = Stopwatch.GetTimestamp();
    }

    public EmlDeliberationAdmission Admission { get; }
    public EmlDeliberationCounts Actual => _actual;
    public bool IsReused => _reused;

    public void BeginPhase(string phase)
    {
        if (_settled) throw new InvalidOperationException("EML deliberation lease is already settled");
        if (string.IsNullOrWhiteSpace(phase)) throw new ArgumentException("phase must be nonempty", nameof(phase));
        if (_phase is not null) CompletePhase();
        _phase = phase;
        _phaseStartActual = _actual;
        _phaseStartRemaining = _remaining;
        _phaseStartWall = Stopwatch.GetTimestamp();
    }

    public void CompletePhase()
    {
        if (_phase is null) return;
        EmlDeliberationCounts phaseActual = EmlDeliberationCounts.Subtract(in _actual, in _phaseStartActual);
        _journal?.AppendPhase(new EmlDeliberationPhaseReceipt(
            Admission.ReservationID, _phase, _phaseSequence++, _phaseStartRemaining, phaseActual, _remaining,
            Stopwatch.GetTimestamp() - _phaseStartWall));
        _phase = null;
    }

    public void ReserveCandidateEvaluation() => Reserve(nameof(EmlDeliberationQuota.CandidateEvaluations), 1, _remaining.CandidateEvaluations);
    public void ReserveLogicalProgramPoints(long count) => Reserve(nameof(EmlDeliberationQuota.LogicalProgramPoints), count, _remaining.LogicalProgramPoints);
    public void ReserveExecutedProgramPoints(long count) => Reserve(nameof(EmlDeliberationQuota.ExecutedProgramPoints), count, _remaining.ExecutedProgramPoints);
    public void ReserveInverseTransform() => Reserve(nameof(EmlDeliberationQuota.InverseTransforms), 1, _remaining.InverseTransforms);
    public void ReserveHashProbe() => Reserve(nameof(EmlDeliberationQuota.HashProbes), 1, _remaining.HashProbes);
    public void ReserveJoinAttempt() => Reserve(nameof(EmlDeliberationQuota.JoinAttempts), 1, _remaining.JoinAttempts);
    public void ReserveJoinHit() => Reserve(nameof(EmlDeliberationQuota.JoinHits), 1, _remaining.JoinHits);
    public void ReserveProcessTerms(long count) => Reserve(nameof(EmlDeliberationQuota.ProcessTerms), count, _remaining.ProcessTerms);
    public void ReserveVerifierProgramPoints(long count) => Reserve(nameof(EmlDeliberationQuota.VerifierProgramPoints), count, _remaining.VerifierProgramPoints);
    public void ReserveCandidateSupplyItem() => Reserve(nameof(EmlDeliberationQuota.CandidateSupplyItems), 1, _remaining.CandidateSupplyItems);
    public void ReserveLawRewriteApplication() => Reserve(nameof(EmlDeliberationQuota.LawRewriteApplications), 1, _remaining.LawRewriteApplications);
    public void ReserveLawRewriteTreeNodes(long count) => Reserve(nameof(EmlDeliberationQuota.LawRewriteTreeNodes), count, _remaining.LawRewriteTreeNodes);

    public EmlDeliberationSettlement Complete(EmlDeliberationOutcomes outcome, string detail = "")
    {
        if (_settled) throw new InvalidOperationException("EML deliberation lease was settled twice");
        CompletePhase();
        if (_reused)
        {
            _settled = true;
            return new EmlDeliberationSettlement(
                Admission.ReservationID, EmlDeliberationOutcomes.Reused,
                EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero,
                EmlDeliberationCounts.Zero, 0, "reservation already settled");
        }
        _settled = true;
        EmlDeliberationCounts refund = EmlDeliberationCounts.Subtract(in _reservedTotal, in _actual);
        _actual.ValidateNonnegative("actual");
        refund.ValidateNonnegative("refund");
        EmlDeliberationSettlement settlement = new(
            Admission.ReservationID, outcome, PlannedCounts(_planned), _reservedTotal, _actual, refund,
            Stopwatch.GetTimestamp() - _wallStart, detail);
        _journal?.AppendSettlement(settlement);
        return settlement;
    }

    private EmlDeliberationCounts PlannedCounts(in EmlDeliberationQuota quota)
        => new(quota.CandidateEvaluations, quota.LogicalProgramPoints, quota.ExecutedProgramPoints,
            quota.InverseTransforms, quota.HashProbes, quota.JoinAttempts, quota.JoinHits,
            quota.ProcessTerms, quota.VerifierProgramPoints, quota.CandidateSupplyItems,
            quota.LawRewriteApplications, quota.LawRewriteTreeNodes);

    private void Reserve(string unit, long count, long remaining)
    {
        if (_settled) throw new InvalidOperationException("EML deliberation lease is already settled");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count > remaining) throw new EmlDeliberationExhaustedException(unit, count, remaining);
        _remaining = unit switch
        {
            nameof(EmlDeliberationQuota.CandidateEvaluations) => _remaining with { CandidateEvaluations = remaining - count },
            nameof(EmlDeliberationQuota.LogicalProgramPoints) => _remaining with { LogicalProgramPoints = remaining - count },
            nameof(EmlDeliberationQuota.ExecutedProgramPoints) => _remaining with { ExecutedProgramPoints = remaining - count },
            nameof(EmlDeliberationQuota.InverseTransforms) => _remaining with { InverseTransforms = remaining - count },
            nameof(EmlDeliberationQuota.HashProbes) => _remaining with { HashProbes = remaining - count },
            nameof(EmlDeliberationQuota.JoinAttempts) => _remaining with { JoinAttempts = remaining - count },
            nameof(EmlDeliberationQuota.JoinHits) => _remaining with { JoinHits = remaining - count },
            nameof(EmlDeliberationQuota.ProcessTerms) => _remaining with { ProcessTerms = remaining - count },
            nameof(EmlDeliberationQuota.VerifierProgramPoints) => _remaining with { VerifierProgramPoints = remaining - count },
            nameof(EmlDeliberationQuota.CandidateSupplyItems) => _remaining with { CandidateSupplyItems = remaining - count },
            nameof(EmlDeliberationQuota.LawRewriteApplications) => _remaining with { LawRewriteApplications = remaining - count },
            nameof(EmlDeliberationQuota.LawRewriteTreeNodes) => _remaining with { LawRewriteTreeNodes = remaining - count },
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "unknown EML deliberation unit")
        };
        _actual = unit switch
        {
            nameof(EmlDeliberationQuota.CandidateEvaluations) => _actual with { CandidateEvaluations = checked(_actual.CandidateEvaluations + count) },
            nameof(EmlDeliberationQuota.LogicalProgramPoints) => _actual with { LogicalProgramPoints = checked(_actual.LogicalProgramPoints + count) },
            nameof(EmlDeliberationQuota.ExecutedProgramPoints) => _actual with { ExecutedProgramPoints = checked(_actual.ExecutedProgramPoints + count) },
            nameof(EmlDeliberationQuota.InverseTransforms) => _actual with { InverseTransforms = checked(_actual.InverseTransforms + count) },
            nameof(EmlDeliberationQuota.HashProbes) => _actual with { HashProbes = checked(_actual.HashProbes + count) },
            nameof(EmlDeliberationQuota.JoinAttempts) => _actual with { JoinAttempts = checked(_actual.JoinAttempts + count) },
            nameof(EmlDeliberationQuota.JoinHits) => _actual with { JoinHits = checked(_actual.JoinHits + count) },
            nameof(EmlDeliberationQuota.ProcessTerms) => _actual with { ProcessTerms = checked(_actual.ProcessTerms + count) },
            nameof(EmlDeliberationQuota.VerifierProgramPoints) => _actual with { VerifierProgramPoints = checked(_actual.VerifierProgramPoints + count) },
            nameof(EmlDeliberationQuota.CandidateSupplyItems) => _actual with { CandidateSupplyItems = checked(_actual.CandidateSupplyItems + count) },
            nameof(EmlDeliberationQuota.LawRewriteApplications) => _actual with { LawRewriteApplications = checked(_actual.LawRewriteApplications + count) },
            nameof(EmlDeliberationQuota.LawRewriteTreeNodes) => _actual with { LawRewriteTreeNodes = checked(_actual.LawRewriteTreeNodes + count) },
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "unknown EML deliberation unit")
        };
    }
}

public sealed class EmlDeliberationJournal
{
    private const uint Tag = 0x31304A45; // EJ01
    private const int Schema = 1;
    private readonly List<EmlDeliberationAdmission> _admissions = new();
    private readonly List<EmlDeliberationPhaseReceipt> _phases = new();
    private readonly List<EmlDeliberationSettlement> _settlements = new();

    public IReadOnlyList<EmlDeliberationAdmission> Admissions => _admissions;
    public IReadOnlyList<EmlDeliberationPhaseReceipt> Phases => _phases;
    public IReadOnlyList<EmlDeliberationSettlement> Settlements => _settlements;

    internal void ApplyCheckpointDelta(
        int admissionCursor, EmlDeliberationAdmission[] admissions,
        int phaseCursor, EmlDeliberationPhaseReceipt[] phases,
        int settlementCursor, EmlDeliberationSettlement[] settlements)
    {
        if (admissionCursor != _admissions.Count || phaseCursor != _phases.Count || settlementCursor != _settlements.Count)
            throw new InvalidDataException("EML deliberation checkpoint cursor gap");
        foreach (EmlDeliberationAdmission admission in admissions)
        {
            admission.Planned.Validate(); admission.Held.ValidateNonnegative("reserved");
            if (!admission.Held.Equals(ToCounts(admission.Planned))) throw new InvalidDataException("EML deliberation admission reservation differs from planned quota");
            if (!string.Equals(admission.ReservationID, ComputeReservationID(admission.ObligationID, admission.Planned, admission.DiscoveryEpoch, admission.Frontier, admission.SolverRevision, admission.VerifierRevision), StringComparison.Ordinal))
                throw new InvalidDataException("EML deliberation admission identity mismatch");
            if (_admissions.Any(existing => existing.ReservationID == admission.ReservationID)) throw new InvalidDataException("duplicate EML deliberation admission");
            _admissions.Add(admission);
        }
        foreach (EmlDeliberationPhaseReceipt phase in phases)
        {
            if (!_admissions.Any(admission => admission.ReservationID == phase.ReservationID)) throw new InvalidDataException("EML deliberation phase has no admission");
            if (phase.Sequence < 0 || _phases.Any(existing => existing.ReservationID == phase.ReservationID && existing.Sequence == phase.Sequence)) throw new InvalidDataException("duplicate EML deliberation phase");
            phase.OpeningRemaining.ValidateNonnegative("phase opening"); phase.Actual.ValidateNonnegative("phase actual"); phase.ClosingRemaining.ValidateNonnegative("phase closing");
            EmlDeliberationCounts expectedClosing = EmlDeliberationCounts.Subtract(phase.OpeningRemaining, phase.Actual);
            if (!phase.ClosingRemaining.Equals(expectedClosing)) throw new InvalidDataException("EML deliberation phase remaining mismatch");
            int expected = _phases.Where(existing => existing.ReservationID == phase.ReservationID).Select(existing => existing.Sequence).DefaultIfEmpty(-1).Max() + 1;
            if (phase.Sequence != expected) throw new InvalidDataException("EML deliberation phase sequence is not contiguous");
            _phases.Add(phase);
        }
        foreach (EmlDeliberationSettlement settlement in settlements)
        {
            if (!Enum.IsDefined(settlement.Outcome) || settlement.WallTicks < 0) throw new InvalidDataException("invalid EML deliberation settlement");
            EmlDeliberationAdmission admission = _admissions.FirstOrDefault(item => item.ReservationID == settlement.ReservationID);
            if (string.IsNullOrEmpty(admission.ReservationID) || !settlement.Held.Equals(admission.Held) || !settlement.Planned.Equals(ToCounts(admission.Planned)))
                throw new InvalidDataException("EML deliberation settlement admission mismatch");
            settlement.Actual.ValidateNonnegative("actual"); settlement.Refund.ValidateNonnegative("refund");
            if (!settlement.Held.Equals(EmlDeliberationCounts.Add(settlement.Actual, settlement.Refund))) throw new InvalidDataException("EML deliberation settlement overdraw");
            if (_settlements.Any(existing => existing.ReservationID == settlement.ReservationID)) throw new InvalidDataException("duplicate EML deliberation settlement");
            _settlements.Add(settlement);
        }
    }

    internal static void WriteCheckpointAdmission(CkptWriter writer, in EmlDeliberationAdmission admission)
    {
        writer.Str(admission.ReservationID); writer.Str(admission.ObligationID); writer.I32(admission.SourcePredictionID); writer.Str(admission.DiscoveryEpoch); writer.Str(admission.Frontier); writer.Str(admission.SolverRevision); writer.Str(admission.VerifierRevision); WriteQuota(writer, admission.Planned); WriteCounts(writer, admission.Held);
    }

    internal static EmlDeliberationAdmission ReadCheckpointAdmission(CkptReader reader)
        => new(reader.Str(), reader.Str(), reader.I32(), reader.Str(), reader.Str(), reader.Str(), reader.Str(), ReadQuota(reader), ReadCounts(reader));

    internal static void WriteCheckpointPhase(CkptWriter writer, in EmlDeliberationPhaseReceipt phase)
    {
        writer.Str(phase.ReservationID); writer.Str(phase.Phase); writer.I32(phase.Sequence); WriteCounts(writer, phase.OpeningRemaining); WriteCounts(writer, phase.Actual); WriteCounts(writer, phase.ClosingRemaining); writer.I64(phase.WallTicks);
    }

    internal static EmlDeliberationPhaseReceipt ReadCheckpointPhase(CkptReader reader)
        => new(reader.Str(), reader.Str(), reader.I32(), ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), reader.I64());

    internal static void WriteCheckpointSettlement(CkptWriter writer, in EmlDeliberationSettlement settlement)
    {
        writer.Str(settlement.ReservationID); writer.U8((byte)settlement.Outcome); WriteCounts(writer, settlement.Planned); WriteCounts(writer, settlement.Held); WriteCounts(writer, settlement.Actual); WriteCounts(writer, settlement.Refund); writer.I64(settlement.WallTicks); writer.Str(settlement.Detail);
    }

    internal static EmlDeliberationSettlement ReadCheckpointSettlement(CkptReader reader)
        => new(reader.Str(), (EmlDeliberationOutcomes)reader.U8(), ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), reader.I64(), reader.Str());

    internal void RollbackTo(int admissions, int phases, int settlements)
    {
        if (admissions < 0 || phases < 0 || settlements < 0
            || admissions > _admissions.Count || phases > _phases.Count || settlements > _settlements.Count)
            throw new InvalidDataException("EML deliberation rollback cursor is outside the journal");
        if (_admissions.Count > admissions) _admissions.RemoveRange(admissions, _admissions.Count - admissions);
        if (_phases.Count > phases) _phases.RemoveRange(phases, _phases.Count - phases);
        if (_settlements.Count > settlements) _settlements.RemoveRange(settlements, _settlements.Count - settlements);
    }

    public EmlDeliberationLease Reserve(
        in EmlObligationResolution obligation,
        EmlDeliberationQuota quota,
        string discoveryEpoch,
        string frontier,
        string solverRevision,
        string verifierRevision)
    {
        quota.Validate();
        string obligationID = ComputeObligationID(in obligation);
        string reservationID = ComputeReservationID(obligationID, quota, discoveryEpoch, frontier, solverRevision, verifierRevision);
        EmlDeliberationAdmission admission = new(
            reservationID, obligationID, obligation.SourcePredictionID.Value,
            discoveryEpoch, frontier, solverRevision, verifierRevision, quota,
            new EmlDeliberationCounts(quota.CandidateEvaluations, quota.LogicalProgramPoints, quota.ExecutedProgramPoints,
                quota.InverseTransforms, quota.HashProbes, quota.JoinAttempts, quota.JoinHits, quota.ProcessTerms, quota.VerifierProgramPoints,
                quota.CandidateSupplyItems, quota.LawRewriteApplications, quota.LawRewriteTreeNodes));
        if (_settlements.Any(s => s.ReservationID == reservationID))
            return new EmlDeliberationLease(admission, this, reused: true);
        if (_admissions.Any(a => a.ReservationID == reservationID))
            return new EmlDeliberationLease(admission, this, reused: true);
        _admissions.Add(admission);
        return new EmlDeliberationLease(admission, this);
    }

    internal void AppendSettlement(in EmlDeliberationSettlement settlement)
    {
        for (int i = 0; i < _settlements.Count; i++)
            if (_settlements[i].ReservationID == settlement.ReservationID) return;
        _settlements.Add(settlement);
    }

    internal void AppendPhase(in EmlDeliberationPhaseReceipt phase)
    {
        for (int i = 0; i < _phases.Count; i++)
            if (_phases[i].ReservationID == phase.ReservationID && _phases[i].Sequence == phase.Sequence)
                throw new InvalidDataException("duplicate EML deliberation phase receipt");
        _phases.Add(phase);
    }

    public void Save(CkptWriter writer)
    {
        writer.Section(Tag);
        writer.I32(Schema);
        writer.I32(_admissions.Count);
        foreach (EmlDeliberationAdmission admission in _admissions)
        {
            writer.Str(admission.ReservationID); writer.Str(admission.ObligationID); writer.I32(admission.SourcePredictionID);
            writer.Str(admission.DiscoveryEpoch); writer.Str(admission.Frontier); writer.Str(admission.SolverRevision); writer.Str(admission.VerifierRevision);
            WriteQuota(writer, admission.Planned); WriteCounts(writer, admission.Held);
        }
        writer.I32(_phases.Count);
        Dictionary<string, int> phaseSequences = new(StringComparer.Ordinal);
        foreach (EmlDeliberationPhaseReceipt phase in _phases)
        {
            writer.Str(phase.ReservationID); writer.Str(phase.Phase); writer.I32(phase.Sequence); WriteCounts(writer, phase.OpeningRemaining);
            WriteCounts(writer, phase.Actual); WriteCounts(writer, phase.ClosingRemaining); writer.I64(phase.WallTicks);
        }
        writer.I32(_settlements.Count);
        foreach (EmlDeliberationSettlement settlement in _settlements)
        {
            writer.Str(settlement.ReservationID); writer.U8((byte)settlement.Outcome); WriteCounts(writer, settlement.Planned);
            WriteCounts(writer, settlement.Held); WriteCounts(writer, settlement.Actual); WriteCounts(writer, settlement.Refund);
            writer.I64(settlement.WallTicks); writer.Str(settlement.Detail);
        }
    }

    public bool TryLoad(CkptReader reader)
    {
        if (!reader.TryExpect(Tag)) return false;
        if (reader.I32() != Schema) throw new InvalidDataException("unsupported EML deliberation journal schema");
        _admissions.Clear(); _phases.Clear(); _settlements.Clear();
        int admissions = reader.I32(); if (admissions < 0 || admissions > 1_000_000) throw new InvalidDataException("invalid EML deliberation admission count");
        for (int i = 0; i < admissions; i++)
        {
            string reservationID = reader.Str(); string obligationID = reader.Str(); int sourcePredictionID = reader.I32();
            string epoch = reader.Str(); string frontier = reader.Str(); string solver = reader.Str(); string verifier = reader.Str();
            EmlDeliberationQuota planned = ReadQuota(reader); EmlDeliberationCounts reserved = ReadCounts(reader);
            planned.Validate(); reserved.ValidateNonnegative("reserved");
            if (!reserved.Equals(ToCounts(planned))) throw new InvalidDataException("EML deliberation admission reservation differs from planned quota");
            string expectedReservationID = ComputeReservationID(obligationID, planned, epoch, frontier, solver, verifier);
            if (!string.Equals(reservationID, expectedReservationID, StringComparison.Ordinal))
                throw new InvalidDataException("EML deliberation reservation identity mismatch");
            _admissions.Add(new(reservationID, obligationID, sourcePredictionID, epoch, frontier, solver, verifier, planned, reserved));
        }
        int phases = reader.I32(); if (phases < 0 || phases > 4_000_000) throw new InvalidDataException("invalid EML deliberation phase count");
        for (int i = 0; i < phases; i++)
            _phases.Add(new(reader.Str(), reader.Str(), reader.I32(), ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), reader.I64()));
        Dictionary<string, int> phaseSequences = new(StringComparer.Ordinal);
        foreach (EmlDeliberationPhaseReceipt phase in _phases)
        {
            if (!_admissions.Any(a => a.ReservationID == phase.ReservationID))
                throw new InvalidDataException("EML deliberation phase has no admission");
            phase.OpeningRemaining.ValidateNonnegative("phase opening");
            phase.Actual.ValidateNonnegative("phase actual");
            phase.ClosingRemaining.ValidateNonnegative("phase closing");
            if (phase.WallTicks < 0) throw new InvalidDataException("EML deliberation phase wall time is negative");
            EmlDeliberationCounts expectedClosing = EmlDeliberationCounts.Subtract(phase.OpeningRemaining, phase.Actual);
            if (!phase.ClosingRemaining.Equals(expectedClosing)) throw new InvalidDataException("EML deliberation phase remaining mismatch");
            int expectedSequence = phaseSequences.GetValueOrDefault(phase.ReservationID);
            if (phase.Sequence != expectedSequence) throw new InvalidDataException("EML deliberation phase sequence is not contiguous");
            phaseSequences[phase.ReservationID] = expectedSequence + 1;
        }
        int settlements = reader.I32(); if (settlements < 0 || settlements > 1_000_000) throw new InvalidDataException("invalid EML deliberation settlement count");
        for (int i = 0; i < settlements; i++)
        {
            string reservationID = reader.Str();
            byte outcomeValue = reader.U8();
            if (!Enum.IsDefined(typeof(EmlDeliberationOutcomes), (int)outcomeValue))
                throw new InvalidDataException("EML deliberation settlement has an invalid outcome");
            EmlDeliberationSettlement settlement = new(reservationID, (EmlDeliberationOutcomes)outcomeValue, ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), reader.I64(), reader.Str());
            if (settlement.WallTicks < 0) throw new InvalidDataException("EML deliberation settlement wall time is negative");
            settlement.Actual.ValidateNonnegative("actual"); settlement.Refund.ValidateNonnegative("refund");
            EmlDeliberationCounts settlementActual = settlement.Actual;
            EmlDeliberationCounts settlementRefund = settlement.Refund;
            if (!settlement.Held.Equals(EmlDeliberationCounts.Add(in settlementActual, in settlementRefund))) throw new InvalidDataException("EML deliberation settlement overdraw");
            EmlDeliberationAdmission admission = _admissions.FirstOrDefault(a => a.ReservationID == settlement.ReservationID);
            if (string.IsNullOrEmpty(admission.ReservationID)) throw new InvalidDataException("EML deliberation settlement has no admission");
            if (!settlement.Held.Equals(admission.Held)) throw new InvalidDataException("EML deliberation settlement reserved quota mismatch");
            if (!settlement.Planned.Equals(ToCounts(admission.Planned))) throw new InvalidDataException("EML deliberation settlement planned quota mismatch");
            if (_settlements.Any(s => s.ReservationID == settlement.ReservationID)) throw new InvalidDataException("duplicate EML deliberation settlement");
            _settlements.Add(settlement);
        }
        for (int i = 0; i < _settlements.Count; i++)
        {
            EmlDeliberationSettlement settlement = _settlements[i];
            EmlDeliberationCounts phaseTotal = EmlDeliberationCounts.Zero;
            EmlDeliberationCounts? priorClosing = null;
            EmlDeliberationPhaseReceipt? firstPhase = null;
            EmlDeliberationPhaseReceipt? lastPhase = null;
            for (int j = 0; j < _phases.Count; j++)
            {
                if (_phases[j].ReservationID != settlement.ReservationID) continue;
                EmlDeliberationPhaseReceipt phase = _phases[j];
                if (priorClosing is EmlDeliberationCounts previous && !phase.OpeningRemaining.Equals(previous))
                    throw new InvalidDataException("EML deliberation phase chain has a remaining-budget gap");
                firstPhase ??= phase;
                lastPhase = phase;
                priorClosing = phase.ClosingRemaining;
                EmlDeliberationCounts phaseActual = _phases[j].Actual;
                phaseTotal = EmlDeliberationCounts.Add(in phaseTotal, in phaseActual);
            }
            if (!phaseTotal.Equals(settlement.Actual))
                throw new InvalidDataException("EML deliberation settlement does not equal phase actuals");
            if (firstPhase is EmlDeliberationPhaseReceipt first && !first.OpeningRemaining.Equals(settlement.Held))
                throw new InvalidDataException("EML deliberation phase chain does not start at reserved budget");
            if (lastPhase is EmlDeliberationPhaseReceipt last && !last.ClosingRemaining.Equals(settlement.Refund))
                throw new InvalidDataException("EML deliberation phase chain does not close at refund budget");
            if (firstPhase is null && !settlement.Actual.Equals(EmlDeliberationCounts.Zero))
                throw new InvalidDataException("EML deliberation settlement has unphased actual work");
        }
        return true;
    }

    private static EmlDeliberationCounts ToCounts(in EmlDeliberationQuota quota)
        => new(quota.CandidateEvaluations, quota.LogicalProgramPoints, quota.ExecutedProgramPoints,
            quota.InverseTransforms, quota.HashProbes, quota.JoinAttempts, quota.JoinHits,
            quota.ProcessTerms, quota.VerifierProgramPoints, quota.CandidateSupplyItems,
            quota.LawRewriteApplications, quota.LawRewriteTreeNodes);

    private static string ComputeObligationID(in EmlObligationResolution obligation)
        => $"claim-{obligation.SourcePredictionID.Value.ToString(CultureInfo.InvariantCulture)}-{obligation.ResidualSignature.R1:X16}";

    private static string ComputeReservationID(string obligationID, in EmlDeliberationQuota quota, string epoch, string frontier, string solver, string verifier)
    {
        string material = string.Join('|', obligationID, epoch, frontier, solver, verifier,
            quota.CandidateEvaluations, quota.LogicalProgramPoints, quota.ExecutedProgramPoints, quota.InverseTransforms,
            quota.HashProbes, quota.JoinAttempts, quota.JoinHits, quota.ProcessTerms, quota.VerifierProgramPoints,
            quota.CandidateSupplyItems, quota.LawRewriteApplications, quota.LawRewriteTreeNodes);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static void WriteQuota(CkptWriter w, in EmlDeliberationQuota q) => WriteCounts(w, new(q.CandidateEvaluations, q.LogicalProgramPoints, q.ExecutedProgramPoints, q.InverseTransforms, q.HashProbes, q.JoinAttempts, q.JoinHits, q.ProcessTerms, q.VerifierProgramPoints, q.CandidateSupplyItems, q.LawRewriteApplications, q.LawRewriteTreeNodes));
    private static EmlDeliberationQuota ReadQuota(CkptReader r) { EmlDeliberationCounts c = ReadCounts(r); return new(c.CandidateEvaluations, c.LogicalProgramPoints, c.ExecutedProgramPoints, c.InverseTransforms, c.HashProbes, c.JoinAttempts, c.JoinHits, c.ProcessTerms, c.VerifierProgramPoints, c.CandidateSupplyItems, c.LawRewriteApplications, c.LawRewriteTreeNodes); }
    private static void WriteCounts(CkptWriter w, in EmlDeliberationCounts c) { w.I64(c.CandidateEvaluations); w.I64(c.LogicalProgramPoints); w.I64(c.ExecutedProgramPoints); w.I64(c.InverseTransforms); w.I64(c.HashProbes); w.I64(c.JoinAttempts); w.I64(c.JoinHits); w.I64(c.ProcessTerms); w.I64(c.VerifierProgramPoints); w.I64(c.CandidateSupplyItems); w.I64(c.LawRewriteApplications); w.I64(c.LawRewriteTreeNodes); }
    private static EmlDeliberationCounts ReadCounts(CkptReader r) => new(r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64());
}
