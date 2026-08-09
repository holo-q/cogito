namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// The verified objects an anytime curve is allowed to count as progress. These are cumulative, accepted
/// commitments; raw residual, rate, meanz, and wall time are diagnostics and never enter this axis.
public readonly record struct EmlAnytimeCommitments(
    long ExactClasses,
    long TheoremClasses,
    long CertificateClasses,
    long ClosedObligations,
    long HeldOutCaptures,
    long HeldOutBestK,
    long VerifiedLaws,
    long VerifiedProofs)
{
    public static EmlAnytimeCommitments Zero => default;
    public long Total => checked(ExactClasses + TheoremClasses + CertificateClasses + ClosedObligations
        + HeldOutCaptures + HeldOutBestK + VerifiedLaws + VerifiedProofs);

    public void Validate(string label)
    {
        if (ExactClasses < 0 || TheoremClasses < 0 || CertificateClasses < 0 || ClosedObligations < 0
            || HeldOutCaptures < 0 || HeldOutBestK < 0 || VerifiedLaws < 0 || VerifiedProofs < 0)
            throw new InvalidDataException($"anytime commitments {label} contain a negative field");
    }

    public bool Dominates(in EmlAnytimeCommitments other)
        => ExactClasses >= other.ExactClasses && TheoremClasses >= other.TheoremClasses
        && CertificateClasses >= other.CertificateClasses && ClosedObligations >= other.ClosedObligations
        && HeldOutCaptures >= other.HeldOutCaptures && HeldOutBestK >= other.HeldOutBestK
        && VerifiedLaws >= other.VerifiedLaws && VerifiedProofs >= other.VerifiedProofs;

    public static EmlAnytimeCommitments Subtract(in EmlAnytimeCommitments left, in EmlAnytimeCommitments right)
        => new(left.ExactClasses - right.ExactClasses, left.TheoremClasses - right.TheoremClasses,
            left.CertificateClasses - right.CertificateClasses, left.ClosedObligations - right.ClosedObligations,
            left.HeldOutCaptures - right.HeldOutCaptures, left.HeldOutBestK - right.HeldOutBestK,
            left.VerifiedLaws - right.VerifiedLaws, left.VerifiedProofs - right.VerifiedProofs);
}

public enum EmlAnytimeKillReasons : byte
{
    BudgetOverrun,
    EvidenceRegression,
    IncumbentRegression,
    DominatedAfterGrace,
    NoProgressAfterGrace,
}

/// Boundary input to the record authority. Fuel is conserved in the typed EML count vector and evaluator
/// intervals; wall/residual/rate/meanz are carried only as diagnostic observations.
public readonly record struct EmlAnytimeBoundaryReceipt(
    string RunID,
    string ConfigID,
    string ChainID,
    string ArmID,
    string ParentPointID,
    int Rung,
    int PrefixStep,
    int WindowIndex,
    string Boundary,
    EmlAnytimeCommitments Commitments,
    EmlDeliberationCounts CumulativeFuel,
    EmlDeliberationCounts WindowPlannedFuel,
    EmlDeliberationCounts WindowActualFuel,
    long CumulativeEvaluatorIntervals,
    long WindowPlannedEvaluatorIntervals,
    long WindowEvaluatorIntervals,
    bool WindowComplete,
    bool EvidenceVerified,
    bool ActiveFunding,
    bool ActiveFork,
    bool ActiveObligation,
    bool PendingResolution,
    bool WindowSettled,
    bool RunTerminal,
    int GraceUntilWindow,
    string DominatorPointID,
    string EvidenceDigest,
    double Residual,
    double Rate,
    double Meanz,
    double WallMilliseconds)
{
    public EmlDeliberationCounts WindowRefundFuel
        => SubtractCounts(WindowPlannedFuel, WindowActualFuel);

    private static EmlDeliberationCounts SubtractCounts(in EmlDeliberationCounts left, in EmlDeliberationCounts right)
        => EmlDeliberationCounts.Subtract(in left, in right);

    public bool KillEligible => WindowComplete && !ActiveFunding && !ActiveFork && !ActiveObligation
        && !PendingResolution && WindowSettled;
}

public readonly record struct EmlAnytimeKillReceipt(
    EmlAnytimeKillReasons Reason,
    string ArmID,
    int PrefixStep,
    int WindowIndex,
    int ConsecutiveNoGainWindows,
    string PointID,
    string Detail,
    string PreviousDigest,
    string Digest);

/// One immutable curve point. `Fuel` and `EvaluatorIntervals` are cumulative axes; Quality carries the accepted
/// incumbent plus diagnostics. The digest chain is the identity rail for resume/fork replay.
public readonly record struct EmlAnytimeCurvePoint(
    string PointID,
    string PreviousDigest,
    string Digest,
    string RunID,
    string ConfigID,
    string ChainID,
    string ArmID,
    string ParentPointID,
    int Rung,
    int PrefixStep,
    int WindowIndex,
    string Boundary,
    EmlAnytimeCommitments Quality,
    EmlDeliberationCounts Fuel,
    EmlDeliberationCounts WindowPlannedFuel,
    EmlDeliberationCounts WindowActualFuel,
    long EvaluatorIntervals,
    long WindowPlannedEvaluatorIntervals,
    long WindowEvaluatorIntervals,
    bool WindowComplete,
    bool EvidenceVerified,
    bool KillEligible,
    bool Dominated,
    bool ActiveFunding,
    bool ActiveFork,
    bool ActiveObligation,
    bool PendingResolution,
    bool WindowSettled,
    bool RunTerminal,
    int GraceUntilWindow,
    string DominatorPointID,
    double Residual,
    double Rate,
    double Meanz,
    double WallMilliseconds,
    string EvidenceDigest)
{
    internal bool IsHandshake
        => Boundary == "evaluation.cold.handshake" && PrefixStep == 0 && WindowIndex == 0
            && Quality == EmlAnytimeCommitments.Zero
            && Fuel == EmlDeliberationCounts.Zero && EvaluatorIntervals == 0
            && WindowPlannedFuel == EmlDeliberationCounts.Zero
            && WindowActualFuel == EmlDeliberationCounts.Zero
            && WindowEvaluatorIntervals == 0
            && !WindowComplete && !KillEligible && !WindowSettled && !RunTerminal
            && EvidenceVerified;

    /// Planned, actual, and refund are all derived from the immutable settlement records. Refund is never an
    /// independent counter: each component is planned minus actual and is therefore conserved by construction.
    public EmlDeliberationCounts WindowRefundFuel
    {
        get
        {
            EmlDeliberationCounts planned = WindowPlannedFuel;
            EmlDeliberationCounts actual = WindowActualFuel;
            return EmlDeliberationCounts.Subtract(in planned, in actual);
        }
    }

    public static EmlAnytimeCurvePoint Create(in EmlAnytimeBoundaryReceipt receipt, string previousDigest, bool dominated)
    {
        receipt.Commitments.Validate("boundary");
        receipt.CumulativeFuel.ValidateNonnegative("cumulative");
        receipt.WindowPlannedFuel.ValidateNonnegative("planned");
        receipt.WindowActualFuel.ValidateNonnegative("actual");
        if (!double.IsFinite(receipt.WallMilliseconds) || receipt.WallMilliseconds < 0)
            throw new InvalidDataException("anytime wall milliseconds must be finite and nonnegative");
        if (receipt.CumulativeEvaluatorIntervals < 0 || receipt.WindowPlannedEvaluatorIntervals < 0 || receipt.WindowEvaluatorIntervals < 0)
            throw new InvalidDataException("anytime evaluator intervals cannot be negative");
        if (string.IsNullOrWhiteSpace(receipt.RunID) || string.IsNullOrWhiteSpace(receipt.ConfigID)
            || string.IsNullOrWhiteSpace(receipt.ChainID)
            || string.IsNullOrWhiteSpace(receipt.ArmID) || string.IsNullOrWhiteSpace(receipt.Boundary)
            || string.IsNullOrWhiteSpace(receipt.EvidenceDigest))
            throw new InvalidDataException("anytime boundary identity/evidence fields are required");
        if (receipt.PrefixStep < 0 || receipt.WindowIndex < 0 || receipt.Rung < 0 || receipt.GraceUntilWindow < 0)
            throw new InvalidDataException("anytime boundary coordinates cannot be negative");
        if (receipt.ParentPointID.Length > 0 && previousDigest.Length > 0 && receipt.ParentPointID != previousDigest)
            throw new InvalidDataException("anytime boundary parent does not continue the curve digest");
        string pointID = ComputePointID(receipt, previousDigest);
        string digest = ComputeDigest(pointID, previousDigest, in receipt, dominated);
        return new(pointID, previousDigest, digest, receipt.RunID, receipt.ConfigID, receipt.ChainID, receipt.ArmID,
            receipt.ParentPointID, receipt.Rung, receipt.PrefixStep, receipt.WindowIndex, receipt.Boundary, receipt.Commitments,
            receipt.CumulativeFuel, receipt.WindowPlannedFuel, receipt.WindowActualFuel, receipt.CumulativeEvaluatorIntervals,
            receipt.WindowPlannedEvaluatorIntervals, receipt.WindowEvaluatorIntervals, receipt.WindowComplete,
            receipt.EvidenceVerified, receipt.KillEligible, dominated, receipt.ActiveFunding, receipt.ActiveFork,
            receipt.ActiveObligation, receipt.PendingResolution, receipt.WindowSettled, receipt.RunTerminal, receipt.GraceUntilWindow, receipt.DominatorPointID,
            receipt.Residual, receipt.Rate, receipt.Meanz, receipt.WallMilliseconds, receipt.EvidenceDigest);
    }

    internal bool VerifyDigest()
    {
        string pointID = ComputePointID(RunID, ConfigID, ChainID, ArmID, ParentPointID, Rung, PrefixStep, WindowIndex, Boundary, PreviousDigest, EvidenceDigest);
        return string.Equals(PointID, pointID, StringComparison.Ordinal)
            && string.Equals(Digest, ComputeDigest(PointID, PreviousDigest, this), StringComparison.Ordinal);
    }

    private static string ComputePointID(in EmlAnytimeBoundaryReceipt receipt, string previousDigest)
        => ComputePointID(receipt.RunID, receipt.ConfigID, receipt.ChainID, receipt.ArmID, receipt.ParentPointID, receipt.Rung,
            receipt.PrefixStep, receipt.WindowIndex, receipt.Boundary, previousDigest, receipt.EvidenceDigest);

    private static string ComputePointID(string runID, string configID, string chainID, string armID, string parentPointID,
        int rung, int prefixStep, int windowIndex, string boundary, string previousDigest, string evidenceDigest)
        => Hash(string.Join('|', runID, configID, chainID, armID, parentPointID, rung, prefixStep.ToString(CultureInfo.InvariantCulture),
            windowIndex.ToString(CultureInfo.InvariantCulture), boundary, previousDigest, evidenceDigest));

    private static string ComputeDigest(string pointID, string previousDigest, in EmlAnytimeBoundaryReceipt receipt, bool dominated)
        => ComputeDigest(pointID, previousDigest, receipt.Commitments, receipt.CumulativeFuel, receipt.WindowPlannedFuel,
            receipt.WindowActualFuel, receipt.CumulativeEvaluatorIntervals, receipt.WindowPlannedEvaluatorIntervals,
            receipt.WindowEvaluatorIntervals, receipt.WindowComplete, receipt.EvidenceVerified, receipt.KillEligible,
            dominated, receipt.ActiveFunding, receipt.ActiveFork, receipt.ActiveObligation, receipt.PendingResolution,
            receipt.WindowSettled, receipt.RunTerminal, receipt.GraceUntilWindow, receipt.DominatorPointID, receipt.Residual, receipt.Rate, receipt.Meanz,
            receipt.WallMilliseconds, receipt.EvidenceDigest);

    private static string ComputeDigest(string pointID, string previousDigest, in EmlAnytimeCurvePoint point)
        => ComputeDigest(pointID, previousDigest, point.Quality, point.Fuel, point.WindowPlannedFuel, point.WindowActualFuel,
            point.EvaluatorIntervals, point.WindowPlannedEvaluatorIntervals, point.WindowEvaluatorIntervals, point.WindowComplete,
            point.EvidenceVerified, point.KillEligible, point.Dominated, point.ActiveFunding, point.ActiveFork,
            point.ActiveObligation, point.PendingResolution, point.WindowSettled, point.RunTerminal, point.GraceUntilWindow, point.DominatorPointID,
            point.Residual, point.Rate, point.Meanz, point.WallMilliseconds, point.EvidenceDigest);

    private static string ComputeDigest(string pointID, string previousDigest, in EmlAnytimeCommitments commitments,
        in EmlDeliberationCounts fuel, in EmlDeliberationCounts plannedFuel, in EmlDeliberationCounts actualFuel,
        long evaluatorIntervals, long plannedEvaluatorIntervals, long windowEvaluatorIntervals,
        bool windowComplete, bool evidenceVerified, bool killEligible, bool dominated, bool activeFunding, bool activeFork,
        bool activeObligation, bool pendingResolution, bool windowSettled, bool runTerminal, int graceUntilWindow,
        string dominatorPointID, double residual, double rate, double meanz, double wallMilliseconds, string evidenceDigest)
        => Hash(string.Join('|', pointID, previousDigest, commitments.ExactClasses, commitments.TheoremClasses,
            commitments.CertificateClasses, commitments.ClosedObligations, commitments.HeldOutCaptures,
            commitments.HeldOutBestK, commitments.VerifiedLaws, commitments.VerifiedProofs,
            fuel.CandidateEvaluations, fuel.LogicalProgramPoints, fuel.ExecutedProgramPoints, fuel.InverseTransforms,
            fuel.HashProbes, fuel.JoinAttempts, fuel.JoinHits, fuel.ProcessTerms, fuel.VerifierProgramPoints,
            fuel.CandidateSupplyItems, fuel.LawRewriteApplications, fuel.LawRewriteTreeNodes, evaluatorIntervals,
            plannedFuel.CandidateEvaluations, plannedFuel.LogicalProgramPoints, plannedFuel.ExecutedProgramPoints,
            plannedFuel.InverseTransforms, plannedFuel.HashProbes, plannedFuel.JoinAttempts, plannedFuel.JoinHits,
            plannedFuel.ProcessTerms, plannedFuel.VerifierProgramPoints, plannedFuel.CandidateSupplyItems,
            plannedFuel.LawRewriteApplications, plannedFuel.LawRewriteTreeNodes,
            actualFuel.CandidateEvaluations, actualFuel.LogicalProgramPoints, actualFuel.ExecutedProgramPoints,
            actualFuel.InverseTransforms, actualFuel.HashProbes, actualFuel.JoinAttempts, actualFuel.JoinHits,
            actualFuel.ProcessTerms, actualFuel.VerifierProgramPoints, actualFuel.CandidateSupplyItems,
            actualFuel.LawRewriteApplications, actualFuel.LawRewriteTreeNodes, plannedEvaluatorIntervals,
            windowEvaluatorIntervals, windowComplete, evidenceVerified, killEligible, dominated, activeFunding, activeFork,
            activeObligation, pendingResolution, windowSettled, runTerminal, graceUntilWindow,
            dominatorPointID, residual.ToString("G17", CultureInfo.InvariantCulture), rate.ToString("G17", CultureInfo.InvariantCulture),
            meanz.ToString("G17", CultureInfo.InvariantCulture), wallMilliseconds.ToString("G17", CultureInfo.InvariantCulture), evidenceDigest));

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class EmlAnytimeCurveContract
{
    public EmlAnytimeCurveContract(int graceWindows = 2)
    {
        if (graceWindows < 1) throw new ArgumentOutOfRangeException(nameof(graceWindows));
        GraceWindows = graceWindows;
    }

    public int GraceWindows { get; }
}

internal readonly record struct EmlAnytimeRebaseScope(
    string RunID,
    string ConfigID,
    string ChainID,
    string ArmID,
    int Rung,
    string ParentPointID)
{
    public EmlAnytimeRebaseScope Validate()
    {
        if (string.IsNullOrWhiteSpace(RunID) || string.IsNullOrWhiteSpace(ConfigID)
            || string.IsNullOrWhiteSpace(ChainID) || string.IsNullOrWhiteSpace(ArmID)
            || Rung < 0 || ParentPointID.Length != 64)
            throw new InvalidDataException("anytime rebase successor scope is incomplete");
        return this;
    }
}

/// Append-only authority for anytime curves. It rejects resume conflicts and never kills on snapshot diagnostics.
public sealed class EmlAnytimeCurve
{
    private const uint Tag = 0x41544331; // ATC1
    private const int Schema = 1;
    private readonly EmlAnytimeCurveContract _contract;
    private readonly List<EmlAnytimeCurvePoint> _points = new();
    private readonly List<EmlAnytimeKillReceipt> _kills = new();
    private EmlAnytimeCommitments _incumbent;
    private EmlDeliberationCounts _fuel;
    private EmlDeliberationCounts _plannedFuel;                          // Σ WindowPlannedFuel over _points — O(1) where the readers once re-walked every point
    private long _evaluatorIntervals;
    private int _noGainWindows;
    private int _lastCompletedWindow = -1;
    private EmlAnytimeCommitments _lastCompletedQuality;
    private int _graceUntilWindow;
    private bool _rebaseScopePending;
    private string _rebaseParentPointID = "";
    private string _runID = "";
    private string _configID = "";
    private string _chainID = "";
    private string _armID = "";
    private int _rung;

    public EmlAnytimeCurve(EmlAnytimeCurveContract? contract = null) => _contract = contract ?? new();
    public IReadOnlyList<EmlAnytimeCurvePoint> Points => _points;
    public IReadOnlyList<EmlAnytimeKillReceipt> Kills => _kills;
    public EmlAnytimeCommitments Incumbent => _incumbent;
    internal EmlDeliberationCounts PlannedFuel => _plannedFuel;
    public string Digest => _points.Count == 0 ? "" : _points[^1].Digest;
    public string ScopeRunID => _runID;
    public string ScopeConfigID => _configID;
    public string ScopeChainID => _chainID;
    public string ScopeArmID => _armID;
    public int ScopeRung => _rung;
    internal string RebaseParentPointID => _rebaseParentPointID;
    internal bool HasPersistedScope => _runID.Length != 0 || _configID.Length != 0
        || _chainID.Length != 0 || _armID.Length != 0 || _rung != 0 || _rebaseParentPointID.Length != 0;

    internal void ApplyCheckpointDelta(int pointCursor, IReadOnlyList<EmlAnytimeCurvePoint> points,
        int killCursor, IReadOnlyList<EmlAnytimeKillReceipt> kills, EmlAnytimeRebaseScope? rebaseScope = null)
    {
        if (rebaseScope is EmlAnytimeRebaseScope pendingScope)
        {
            if (pointCursor != 0 || points.Count != 0 || killCursor != 0 || kills.Count != 0 || _points.Count == 0)
                throw new InvalidDataException("anytime checkpoint rebase shape is invalid");
            pendingScope.Validate();
            _points.Clear();
            _kills.Clear();
            _incumbent = EmlAnytimeCommitments.Zero;
            _fuel = EmlDeliberationCounts.Zero;
            _plannedFuel = EmlDeliberationCounts.Zero;
            _evaluatorIntervals = 0;
            _noGainWindows = 0;
            _lastCompletedWindow = -1;
            _lastCompletedQuality = EmlAnytimeCommitments.Zero;
            _graceUntilWindow = 0;
            _runID = pendingScope.RunID;
            _configID = pendingScope.ConfigID;
            _chainID = pendingScope.ChainID;
            _armID = pendingScope.ArmID;
            _rung = pendingScope.Rung;
            _rebaseParentPointID = pendingScope.ParentPointID;
            _rebaseScopePending = true;
            return;
        }
        if (pointCursor != _points.Count || killCursor != _kills.Count)
        {
            EmlAnytimeCurvePoint? prior = _points.Count == 0 ? null : _points[^1];
            EmlAnytimeCurvePoint? incoming = points.Count == 0 ? null : points[0];
            EmlAnytimeCurvePoint? incomingTail = points.Count == 0 ? null : points[^1];
            Trace.Cortex.Boundary("checkpoint.replay.anytime-transition",
                $"cursor={_points.Count}/{pointCursor} kills={_kills.Count}/{killCursor} "
                + $"scope={_runID}/{_configID}/{_chainID}/{_armID}/r{_rung} "
                + $"prior={(prior is EmlAnytimeCurvePoint p ? $"{p.PointID}:{p.PrefixStep}/{p.WindowIndex}:{p.RunID}:{p.ParentPointID}:{p.Digest}" : "<none>")} "
                + $"incoming={(incoming is EmlAnytimeCurvePoint first ? $"{first.PointID}:{first.PrefixStep}/{first.WindowIndex}:{first.RunID}:{first.ConfigID}/{first.ChainID}/{first.ArmID}:{first.ParentPointID}:{first.PreviousDigest}" : "<none>")} "
                + $"incoming_tail={(incomingTail is EmlAnytimeCurvePoint last ? $"{last.PointID}:{last.PrefixStep}/{last.WindowIndex}:{last.RunID}:{last.ConfigID}/{last.ChainID}/{last.ArmID}:{last.ParentPointID}:{last.Digest}" : "<none>")} "
                + $"point_count={points.Count} kill_count={kills.Count} kill_tail={(kills.Count == 0 ? "<none>" : kills[^1].PointID)}");
            throw new InvalidDataException("anytime checkpoint delta cursor gap");
        }
        for (int i = 0; i < points.Count; i++)
        {
            EmlAnytimeCurvePoint point = points[i];
            if (!point.VerifyDigest()) throw new InvalidDataException("anytime checkpoint delta point digest mismatch");
            AppendLoaded(point);
        }
        for (int i = 0; i < kills.Count; i++)
        {
            EmlAnytimeKillReceipt kill = kills[i];
            EmlAnytimeCurvePoint? referenced = _points.FirstOrDefault(point => point.PointID == kill.PointID);
            if (kill.PointID.Length == 0 || referenced is not EmlAnytimeCurvePoint pointRef
                || kill.PreviousDigest != (_kills.Count == 0 ? "" : _kills[^1].Digest)
                || kill.Digest != HashKill(kill.Reason, kill.ArmID, kill.PrefixStep, kill.WindowIndex,
                    kill.PointID, kill.PreviousDigest, kill.ConsecutiveNoGainWindows, kill.Detail)
                || pointRef.ArmID != kill.ArmID || pointRef.PrefixStep != kill.PrefixStep || pointRef.WindowIndex != kill.WindowIndex)
                throw new InvalidDataException("anytime checkpoint delta kill receipt mismatch");
            _kills.Add(kill);
        }
    }

    internal static void WriteCheckpointPoint(CkptWriter writer, in EmlAnytimeCurvePoint point) => WritePoint(writer, in point);
    internal static EmlAnytimeCurvePoint ReadCheckpointPoint(CkptReader reader) => ReadPoint(reader);
    internal static void WriteCheckpointKill(CkptWriter writer, in EmlAnytimeKillReceipt kill)
    { writer.U8((byte)kill.Reason); writer.Str(kill.ArmID); writer.I32(kill.PrefixStep); writer.I32(kill.WindowIndex); writer.I32(kill.ConsecutiveNoGainWindows); writer.Str(kill.PointID); writer.Str(kill.Detail); writer.Str(kill.PreviousDigest); writer.Str(kill.Digest); }
    internal static EmlAnytimeKillReceipt ReadCheckpointKill(CkptReader reader)
        => new((EmlAnytimeKillReasons)reader.U8(), reader.Str(), reader.I32(), reader.I32(), reader.I32(), reader.Str(), reader.Str(), reader.Str(), reader.Str());

    public EmlAnytimeCurvePoint Append(in EmlAnytimeBoundaryReceipt receipt, bool dominated = false)
    {
        if (!WithinBudget(receipt.WindowActualFuel, receipt.WindowPlannedFuel))
            throw new InvalidDataException("anytime boundary actual fuel exceeded planned fuel");
        if (receipt.WindowEvaluatorIntervals > receipt.WindowPlannedEvaluatorIntervals)
            throw new InvalidDataException("anytime boundary actual evaluator intervals exceeded planned intervals");
        if (!double.IsFinite(receipt.WallMilliseconds) || receipt.WallMilliseconds < 0)
            throw new InvalidDataException("anytime boundary wall milliseconds must be finite and nonnegative");
        bool declaredDominated = receipt.DominatorPointID.Length > 0;
        if (dominated && !declaredDominated) throw new InvalidDataException("dominated anytime point must name its dominator");
        dominated = declaredDominated;
        if (_points.Count > 0 && _points[^1].RunTerminal && receipt.RunTerminal
            && receipt.Boundary == "ruler.window.terminal")
        {
            EmlAnytimeCurvePoint duplicate = EmlAnytimeCurvePoint.Create(in receipt, _points[^1].PreviousDigest, dominated);
            if (_points[^1] == duplicate) return _points[^1];
            throw new InvalidDataException("conflicting terminal anytime settlement on resume");
        }
        EmlAnytimeCurvePoint point = EmlAnytimeCurvePoint.Create(in receipt, Digest, dominated);
        int existing = _points.FindIndex(candidate => candidate.PointID == point.PointID);
        if (existing >= 0)
        {
            if (_points[existing] != point) throw new InvalidDataException($"anytime point {point.PointID} conflicts on resume");
            return _points[existing];
        }
        if (_points.Count > 0 && point.PreviousDigest != Digest)
            throw new InvalidDataException("anytime curve digest chain is discontinuous");
        if (_points.Count == 0)
        {
            _runID = point.RunID; _configID = point.ConfigID; _chainID = point.ChainID; _armID = point.ArmID; _rung = point.Rung;
        }
        else if (point.ConfigID != _configID || point.ChainID != _chainID || point.ArmID != _armID)
            throw new InvalidDataException("anytime curve scope changed inside one arm chain");
        else if (point.RunID != _runID || point.Rung != _rung)
        {
            if (point.ParentPointID != Digest || point.Rung < _rung)
                throw new InvalidDataException("anytime curve run/rung changed without per-arm continuation");
            _runID = point.RunID; _rung = point.Rung;
        }
        if (point.GraceUntilWindow < _graceUntilWindow) throw new InvalidDataException("anytime grace horizon regressed");
        _graceUntilWindow = Math.Max(_graceUntilWindow, point.GraceUntilWindow);
        if (_points.Count > 0)
        {
            EmlAnytimeCurvePoint prior = _points[^1];
            bool sameCoordinate = point.PrefixStep == prior.PrefixStep && point.WindowIndex == prior.WindowIndex;
            if (sameCoordinate)
            {
                if (!point.RunTerminal || point.Boundary != "ruler.window.terminal" || prior.RunTerminal
                    || !point.WindowPlannedFuel.Equals(EmlDeliberationCounts.Zero)
                    || !point.WindowActualFuel.Equals(EmlDeliberationCounts.Zero)
                    || point.WindowEvaluatorIntervals != 0 || point.WindowPlannedEvaluatorIntervals != 0
                    || point.PendingResolution || !point.WindowSettled)
                    throw new InvalidDataException("same-horizon anytime point must be the zero-spend terminal settlement");
            }
            else if (point.PrefixStep < prior.PrefixStep || point.WindowIndex < prior.WindowIndex)
                throw new InvalidDataException("anytime curve prefix/window regressed");
            if (prior.RunTerminal) throw new InvalidDataException("anytime curve cannot append after its terminal settlement");
        }
        if (!point.Quality.Dominates(_incumbent))
        {
            EmlAnytimeCommitments pointQuality = point.Quality;
            EmlAnytimeCommitments incumbent = _incumbent;
            EmlAnytimeCommitments delta = EmlAnytimeCommitments.Subtract(in pointQuality, in incumbent);
            delta.Validate("incumbent delta");
            throw new InvalidDataException("anytime incumbent regressed");
        }
        EmlDeliberationCounts pointFuel = point.Fuel;
        EmlDeliberationCounts priorFuel = _fuel;
        EmlDeliberationCounts fuelDelta = EmlDeliberationCounts.Subtract(in pointFuel, in priorFuel);
        fuelDelta.ValidateNonnegative("fuel delta");
        EmlDeliberationCounts actualWindowFuel = point.WindowActualFuel;
        if (!point.IsHandshake && !fuelDelta.Equals(actualWindowFuel))
            throw new InvalidDataException("anytime cumulative fuel does not equal window actual fuel");
        if (!point.IsHandshake && point.EvaluatorIntervals - _evaluatorIntervals != point.WindowEvaluatorIntervals)
            throw new InvalidDataException("anytime cumulative evaluator intervals do not equal window actuals");
        if (point.Dominated && !ValidateDominator(point)) throw new InvalidDataException("anytime dominator is not a same-lineage weaker incumbent");
        if (point.EvaluatorIntervals < _evaluatorIntervals) throw new InvalidDataException("anytime evaluator intervals regressed");
        bool gained = _points.Count == 0 || point.Quality != _incumbent;
        _points.Add(point);
        _incumbent = point.Quality;
        _fuel = point.Fuel;
        _plannedFuel = EmlDeliberationCounts.Add(in _plannedFuel, point.WindowPlannedFuel);
        _evaluatorIntervals = point.EvaluatorIntervals;
        if (point.WindowComplete && point.WindowIndex > _lastCompletedWindow)
        {
            if (_lastCompletedWindow >= 0 && point.Quality == _lastCompletedQuality) _noGainWindows++;
            else _noGainWindows = 0;
            _lastCompletedWindow = point.WindowIndex;
            _lastCompletedQuality = point.Quality;
        }
        return point;
    }

    public EmlAnytimeKillReceipt? EvaluateKill(in EmlAnytimeBoundaryReceipt receipt, in EmlAnytimeCurvePoint point, bool evidenceRegression = false)
    {
        if (_points.Count == 0 || _points[^1] != point || point.RunID != receipt.RunID || point.ConfigID != receipt.ConfigID
            || point.ChainID != receipt.ChainID || point.ArmID != receipt.ArmID || point.PrefixStep != receipt.PrefixStep
            || point.WindowIndex != receipt.WindowIndex || point.Digest != Digest || point.KillEligible != receipt.KillEligible
            || point.ActiveFunding != receipt.ActiveFunding || point.ActiveFork != receipt.ActiveFork
            || point.ActiveObligation != receipt.ActiveObligation || point.PendingResolution != receipt.PendingResolution
            || point.WindowSettled != receipt.WindowSettled || point.RunTerminal != receipt.RunTerminal)
            throw new InvalidDataException("anytime kill evaluation is not bound to the current point");
        if (!WithinBudget(receipt.WindowActualFuel, receipt.WindowPlannedFuel))
            return AddKill(EmlAnytimeKillReasons.BudgetOverrun, receipt, point, "actual fuel exceeded planned fuel");
        if (receipt.WindowEvaluatorIntervals > receipt.WindowPlannedEvaluatorIntervals)
            return AddKill(EmlAnytimeKillReasons.BudgetOverrun, receipt, point, "actual evaluator intervals exceeded planned intervals");
        if (evidenceRegression || !receipt.EvidenceVerified)
            return AddKill(EmlAnytimeKillReasons.EvidenceRegression, receipt, point, "evidence verification failed");
        if (!receipt.KillEligible || receipt.WindowIndex < Math.Max(_contract.GraceWindows, receipt.GraceUntilWindow)) return null;
        if (_points.Count >= 2 && !_points[^2].Quality.Dominates(point.Quality))
            return AddKill(EmlAnytimeKillReasons.IncumbentRegression, receipt, point, "incumbent quality regressed");
        if (point.Dominated && _noGainWindows >= _contract.GraceWindows)
            return AddKill(EmlAnytimeKillReasons.DominatedAfterGrace, receipt, point, "dominated after grace windows");
        if (_noGainWindows >= _contract.GraceWindows)
            return AddKill(EmlAnytimeKillReasons.NoProgressAfterGrace, receipt, point, "no verified commitment gain after grace windows");
        return null;
    }

    private EmlAnytimeKillReceipt AddKill(EmlAnytimeKillReasons reason, in EmlAnytimeBoundaryReceipt receipt, in EmlAnytimeCurvePoint point, string detail)
    {
        for (int i = 0; i < _kills.Count; i++)
            if (_kills[i].PointID == point.PointID)
            {
                if (_kills[i].Reason == reason && _kills[i].Detail == detail) return _kills[i];
                throw new InvalidDataException("conflicting anytime kill receipts for one point");
            }
        string previous = _kills.Count == 0 ? "" : _kills[^1].Digest;
        string digest = HashKill(reason, receipt, point.PointID, previous, _noGainWindows, detail);
        EmlAnytimeKillReceipt kill = new(reason, receipt.ArmID, receipt.PrefixStep, receipt.WindowIndex, _noGainWindows, point.PointID, detail, previous, digest);
        if (_kills.Count == 0 || _kills[^1] != kill) _kills.Add(kill);
        return kill;
    }

    public void Save(CkptWriter writer)
    {
        writer.Section(Tag); writer.I32(Schema); writer.I32(_contract.GraceWindows); writer.I32(_points.Count);
        foreach (EmlAnytimeCurvePoint point in _points) WritePoint(writer, in point);
        writer.I32(_kills.Count);
        foreach (EmlAnytimeKillReceipt kill in _kills)
        { writer.U8((byte)kill.Reason); writer.Str(kill.ArmID); writer.I32(kill.PrefixStep); writer.I32(kill.WindowIndex); writer.I32(kill.ConsecutiveNoGainWindows); writer.Str(kill.PointID); writer.Str(kill.Detail); writer.Str(kill.PreviousDigest); writer.Str(kill.Digest); }
    }

    public bool TryLoad(CkptReader reader)
    {
        if (!reader.TryExpect(Tag)) return false;
        if (reader.I32() != Schema) throw new InvalidDataException("unsupported anytime curve schema");
        int grace = reader.I32(); if (grace < 1 || grace != _contract.GraceWindows) throw new InvalidDataException("anytime curve grace contract mismatch");
        _points.Clear(); _kills.Clear(); _incumbent = default; _fuel = default; _plannedFuel = default; _evaluatorIntervals = 0; _noGainWindows = 0; _lastCompletedWindow = -1; _lastCompletedQuality = default; _graceUntilWindow = 0; _runID = _configID = _chainID = _armID = _rebaseParentPointID = ""; _rung = 0; _rebaseScopePending = false;
        int count = reader.I32(); if (count < 0 || count > 10_000_000) throw new InvalidDataException("invalid anytime curve point count");
        for (int i = 0; i < count; i++)
        {
            EmlAnytimeCurvePoint point = ReadPoint(reader);
            if (!point.VerifyDigest()) throw new InvalidDataException("anytime curve point digest mismatch");
            AppendLoaded(point);
        }
        int kills = reader.I32(); if (kills < 0 || kills > count + 4) throw new InvalidDataException("invalid anytime kill count");
        for (int i = 0; i < kills; i++)
        {
            EmlAnytimeKillReasons reason = (EmlAnytimeKillReasons)reader.U8();
            if (!Enum.IsDefined(reason)) throw new InvalidDataException("invalid anytime kill reason");
            EmlAnytimeKillReceipt kill = new(reason, reader.Str(), reader.I32(), reader.I32(), reader.I32(), reader.Str(), reader.Str(), reader.Str(), reader.Str());
            EmlAnytimeCurvePoint? referenced = _points.FirstOrDefault(point => point.PointID == kill.PointID);
            if (kill.PointID.Length == 0 || referenced is not EmlAnytimeCurvePoint pointRef
                || pointRef.RunID != _runID || pointRef.ConfigID != _configID || pointRef.ChainID != _chainID
                || pointRef.ArmID != kill.ArmID || pointRef.PrefixStep != kill.PrefixStep || pointRef.WindowIndex != kill.WindowIndex
                || kill.PreviousDigest != (_kills.Count == 0 ? "" : _kills[^1].Digest)
                || kill.Digest != HashKill(kill.Reason, kill.ArmID, kill.PrefixStep, kill.WindowIndex, kill.PointID, kill.PreviousDigest, kill.ConsecutiveNoGainWindows, kill.Detail))
                throw new InvalidDataException("anytime kill receipt digest/reference mismatch");
            _kills.Add(kill);
        }
        return true;
    }

    private void AppendLoaded(in EmlAnytimeCurvePoint point)
    {
        if (!WithinBudget(point.WindowActualFuel, point.WindowPlannedFuel))
            throw new InvalidDataException("anytime checkpoint actual fuel exceeded planned fuel");
        if (point.WindowEvaluatorIntervals > point.WindowPlannedEvaluatorIntervals)
            throw new InvalidDataException("anytime checkpoint actual evaluator intervals exceeded planned intervals");
        if (!double.IsFinite(point.WallMilliseconds) || point.WallMilliseconds < 0)
            throw new InvalidDataException("anytime checkpoint wall milliseconds must be finite and nonnegative");
        if (_points.Count > 0 && point.PreviousDigest != Digest)
            throw new InvalidDataException("anytime checkpoint chain is discontinuous");
        if (_rebaseScopePending)
        {
            if (point.PreviousDigest.Length != 0 || point.RunID != _runID || point.ChainID != _chainID
                || point.ConfigID != _configID || point.ArmID != _armID || point.Rung != _rung
                || point.ParentPointID != _rebaseParentPointID)
                throw new InvalidDataException("anytime checkpoint rebase successor scope is not predecessor-bound");
            _rebaseParentPointID = "";
            _rebaseScopePending = false;
        }
        else if (_points.Count == 0)
        { _runID = point.RunID; _configID = point.ConfigID; _chainID = point.ChainID; _armID = point.ArmID; _rung = point.Rung; }
        else if (point.ConfigID != _configID || point.ChainID != _chainID || point.ArmID != _armID)
        {
            throw new InvalidDataException($"anytime checkpoint scope changed inside one arm chain current={_configID}/{_chainID}/{_armID} incoming={point.ConfigID}/{point.ChainID}/{point.ArmID}");
        }
        else if (point.RunID != _runID || point.Rung != _rung)
        {
            if (point.ParentPointID != Digest || point.Rung < _rung)
                throw new InvalidDataException("anytime checkpoint run/rung changed without per-arm continuation");
            _runID = point.RunID; _rung = point.Rung;
        }
        if (point.GraceUntilWindow < _graceUntilWindow) throw new InvalidDataException("anytime checkpoint grace horizon regressed");
        _graceUntilWindow = Math.Max(_graceUntilWindow, point.GraceUntilWindow);
        if (_points.Count > 0 && (point.PrefixStep < _points[^1].PrefixStep || point.WindowIndex < _points[^1].WindowIndex))
            throw new InvalidDataException("anytime checkpoint prefix/window regressed");
        if (!point.Quality.Dominates(_incumbent)) throw new InvalidDataException("anytime checkpoint incumbent regressed");
        EmlDeliberationCounts pointFuel = point.Fuel;
        EmlDeliberationCounts priorFuel = _fuel;
        EmlDeliberationCounts delta = EmlDeliberationCounts.Subtract(in pointFuel, in priorFuel); delta.ValidateNonnegative("checkpoint fuel delta");
        if (!point.IsHandshake && (!delta.Equals(point.WindowActualFuel) || point.EvaluatorIntervals - _evaluatorIntervals != point.WindowEvaluatorIntervals))
            throw new InvalidDataException("anytime checkpoint fuel conservation mismatch");
        if (point.Dominated && !ValidateDominator(point)) throw new InvalidDataException("anytime checkpoint dominator is invalid");
        if (point.EvaluatorIntervals < _evaluatorIntervals) throw new InvalidDataException("anytime checkpoint evaluator intervals regressed");
        _points.Add(point); _incumbent = point.Quality; _fuel = point.Fuel; _evaluatorIntervals = point.EvaluatorIntervals;
        _plannedFuel = EmlDeliberationCounts.Add(in _plannedFuel, point.WindowPlannedFuel);
        if (point.WindowComplete && point.WindowIndex > _lastCompletedWindow)
        {
            if (_lastCompletedWindow >= 0 && _lastCompletedQuality == point.Quality) _noGainWindows++; else _noGainWindows = 0;
            _lastCompletedWindow = point.WindowIndex; _lastCompletedQuality = point.Quality;
        }
    }

    private bool ValidateDominator(in EmlAnytimeCurvePoint point)
    {
        if (point.DominatorPointID.Length == 0) return false;
        string dominatorID = point.DominatorPointID;
        EmlAnytimeCurvePoint? dominator = _points.FirstOrDefault(candidate => candidate.PointID == dominatorID);
        if (dominator is not EmlAnytimeCurvePoint found) return false;
        EmlAnytimeCommitments weaker = found.Quality;
        EmlAnytimeCommitments candidate = point.Quality;
        EmlDeliberationCounts weakerFuel = found.Fuel;
        EmlDeliberationCounts candidateFuel = point.Fuel;
        EmlDeliberationCounts fuelDelta = EmlDeliberationCounts.Subtract(in candidateFuel, in weakerFuel);
        try { fuelDelta.ValidateNonnegative("dominator fuel"); } catch (InvalidDataException) { return false; }
        return found.RunID == point.RunID && found.ConfigID == point.ConfigID && found.ChainID == point.ChainID
            && weaker.Dominates(candidate);
    }

    private static bool WithinBudget(in EmlDeliberationCounts actual, in EmlDeliberationCounts planned)
        => actual.CandidateEvaluations <= planned.CandidateEvaluations && actual.LogicalProgramPoints <= planned.LogicalProgramPoints
        && actual.ExecutedProgramPoints <= planned.ExecutedProgramPoints && actual.InverseTransforms <= planned.InverseTransforms
        && actual.HashProbes <= planned.HashProbes && actual.JoinAttempts <= planned.JoinAttempts && actual.JoinHits <= planned.JoinHits
        && actual.ProcessTerms <= planned.ProcessTerms && actual.VerifierProgramPoints <= planned.VerifierProgramPoints
        && actual.CandidateSupplyItems <= planned.CandidateSupplyItems && actual.LawRewriteApplications <= planned.LawRewriteApplications
        && actual.LawRewriteTreeNodes <= planned.LawRewriteTreeNodes;

    private static string HashKill(EmlAnytimeKillReasons reason, in EmlAnytimeBoundaryReceipt receipt, string pointID,
        string previousDigest, int noGainWindows, string detail)
        => HashKill(reason, receipt.ArmID, receipt.PrefixStep, receipt.WindowIndex, pointID, previousDigest, noGainWindows, detail);

    private static string HashKill(EmlAnytimeKillReasons reason, string armID, int prefixStep, int windowIndex,
        string pointID, string previousDigest, int noGainWindows, string detail)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', reason, armID, prefixStep,
            windowIndex, pointID, previousDigest, noGainWindows, detail))));

    // Rows are append-only alongside _points, so each window close writes only the new rows; a path
    // change, an external delete, or a point-list rewind (rebase / checkpoint reload) falls back to a
    // full rewrite. Final bytes are identical to a from-zero rewrite — rows are pure functions of points.
    private string? _tsvPath;
    private int _tsvPointsWritten;

    public void WriteTSV(string path)
    {
        bool append = string.Equals(path, _tsvPath, StringComparison.Ordinal)
            && _tsvPointsWritten <= _points.Count && File.Exists(path);
        using StreamWriter writer = new(path, append, new UTF8Encoding(false));
        if (!append)
        {
            _tsvPointsWritten = 0;
            WriteTSVHeader(writer);
        }
        for (int i = _tsvPointsWritten; i < _points.Count; i++) WriteTSVRow(writer, _points[i]);
        _tsvPath = path;
        _tsvPointsWritten = _points.Count;
    }

    private static void WriteTSVHeader(StreamWriter writer)
    {
        writer.WriteLine("point_id\tprevious_digest\tdigest\trun_id\tconfig_id\tchain_id\tarm_id\tparent_point_id\trung\tprefix_step\twindow\tboundary\texact\ttheorem\tcertificates\tclosed_obligations\theldout_captures\theldout_bestk\tverified_laws\tverified_proofs\tfuel_candidate_evaluations\tfuel_logical_program_points\tfuel_executed_program_points\tfuel_inverse_transforms\tfuel_hash_probes\tfuel_join_attempts\tfuel_join_hits\tfuel_process_terms\tfuel_verifier_program_points\tfuel_candidate_supply_items\tfuel_law_rewrite_applications\tfuel_law_rewrite_tree_nodes\tplanned_candidate_evaluations\tplanned_logical_program_points\tplanned_executed_program_points\tplanned_inverse_transforms\tplanned_hash_probes\tplanned_join_attempts\tplanned_join_hits\tplanned_process_terms\tplanned_verifier_program_points\tplanned_candidate_supply_items\tplanned_law_rewrite_applications\tplanned_law_rewrite_tree_nodes\tactual_candidate_evaluations\tactual_logical_program_points\tactual_executed_program_points\tactual_inverse_transforms\tactual_hash_probes\tactual_join_attempts\tactual_join_hits\tactual_process_terms\tactual_verifier_program_points\tactual_candidate_supply_items\tactual_law_rewrite_applications\tactual_law_rewrite_tree_nodes\tevaluator_intervals\twindow_planned_evaluator_intervals\twindow_evaluator_intervals\twindow_complete\tevidence_verified\tkill_eligible\tdominated\tactive_funding\tactive_fork\tactive_obligation\tpending_resolution\twindow_settled\trun_terminal\tgrace_until_window\tdominator_point_id\tresidual\trate\tmeanz\twall_ms\tevidence_digest");
    }

    private static void WriteTSVRow(StreamWriter writer, in EmlAnytimeCurvePoint point)
        => writer.WriteLine(string.Join('\t', point.PointID, point.PreviousDigest, point.Digest, point.RunID, point.ConfigID, point.ChainID, point.ArmID, point.ParentPointID,
                point.Rung.ToString(CultureInfo.InvariantCulture), point.PrefixStep.ToString(CultureInfo.InvariantCulture), point.WindowIndex.ToString(CultureInfo.InvariantCulture), point.Boundary,
                point.Quality.ExactClasses, point.Quality.TheoremClasses, point.Quality.CertificateClasses, point.Quality.ClosedObligations,
                point.Quality.HeldOutCaptures, point.Quality.HeldOutBestK, point.Quality.VerifiedLaws, point.Quality.VerifiedProofs,
                point.Fuel.CandidateEvaluations, point.Fuel.LogicalProgramPoints, point.Fuel.ExecutedProgramPoints, point.Fuel.InverseTransforms,
                point.Fuel.HashProbes, point.Fuel.JoinAttempts, point.Fuel.JoinHits, point.Fuel.ProcessTerms, point.Fuel.VerifierProgramPoints,
                point.Fuel.CandidateSupplyItems, point.Fuel.LawRewriteApplications, point.Fuel.LawRewriteTreeNodes,
                point.WindowPlannedFuel.CandidateEvaluations, point.WindowPlannedFuel.LogicalProgramPoints, point.WindowPlannedFuel.ExecutedProgramPoints, point.WindowPlannedFuel.InverseTransforms,
                point.WindowPlannedFuel.HashProbes, point.WindowPlannedFuel.JoinAttempts, point.WindowPlannedFuel.JoinHits, point.WindowPlannedFuel.ProcessTerms, point.WindowPlannedFuel.VerifierProgramPoints,
                point.WindowPlannedFuel.CandidateSupplyItems, point.WindowPlannedFuel.LawRewriteApplications, point.WindowPlannedFuel.LawRewriteTreeNodes,
                point.WindowActualFuel.CandidateEvaluations, point.WindowActualFuel.LogicalProgramPoints, point.WindowActualFuel.ExecutedProgramPoints, point.WindowActualFuel.InverseTransforms,
                point.WindowActualFuel.HashProbes, point.WindowActualFuel.JoinAttempts, point.WindowActualFuel.JoinHits, point.WindowActualFuel.ProcessTerms, point.WindowActualFuel.VerifierProgramPoints,
                point.WindowActualFuel.CandidateSupplyItems, point.WindowActualFuel.LawRewriteApplications, point.WindowActualFuel.LawRewriteTreeNodes, point.EvaluatorIntervals,
                point.WindowPlannedEvaluatorIntervals, point.WindowEvaluatorIntervals, point.WindowComplete ? 1 : 0, point.EvidenceVerified ? 1 : 0, point.KillEligible ? 1 : 0,
                point.Dominated ? 1 : 0, point.ActiveFunding ? 1 : 0, point.ActiveFork ? 1 : 0, point.ActiveObligation ? 1 : 0, point.PendingResolution ? 1 : 0, point.WindowSettled ? 1 : 0, point.RunTerminal ? 1 : 0,
                point.GraceUntilWindow, point.DominatorPointID, point.Residual.ToString("G17", CultureInfo.InvariantCulture), point.Rate.ToString("G17", CultureInfo.InvariantCulture),
                point.Meanz.ToString("G17", CultureInfo.InvariantCulture), point.WallMilliseconds.ToString("G17", CultureInfo.InvariantCulture), point.EvidenceDigest));

    private static void WritePoint(CkptWriter w, in EmlAnytimeCurvePoint p)
    {
        w.Str(p.PointID); w.Str(p.PreviousDigest); w.Str(p.Digest); w.Str(p.RunID); w.Str(p.ConfigID); w.Str(p.ChainID); w.Str(p.ArmID); w.Str(p.ParentPointID); w.I32(p.Rung); w.I32(p.PrefixStep); w.I32(p.WindowIndex); w.Str(p.Boundary);
        WriteCommitments(w, p.Quality); WriteCounts(w, p.Fuel); WriteCounts(w, p.WindowPlannedFuel); WriteCounts(w, p.WindowActualFuel); w.I64(p.EvaluatorIntervals); w.I64(p.WindowPlannedEvaluatorIntervals); w.I64(p.WindowEvaluatorIntervals); w.Bool(p.WindowComplete); w.Bool(p.EvidenceVerified); w.Bool(p.KillEligible); w.Bool(p.Dominated); w.Bool(p.ActiveFunding); w.Bool(p.ActiveFork); w.Bool(p.ActiveObligation); w.Bool(p.PendingResolution); w.Bool(p.WindowSettled); w.Bool(p.RunTerminal); w.I32(p.GraceUntilWindow); w.Str(p.DominatorPointID); w.F64(p.Residual); w.F64(p.Rate); w.F64(p.Meanz); w.F64(p.WallMilliseconds); w.Str(p.EvidenceDigest);
    }

    private static EmlAnytimeCurvePoint ReadPoint(CkptReader r)
        => new(r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.I32(), r.I32(), r.I32(), r.Str(), ReadCommitments(r), ReadCounts(r), ReadCounts(r), ReadCounts(r), r.I64(), r.I64(), r.I64(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.I32(), r.Str(), r.F64(), r.F64(), r.F64(), r.F64(), r.Str());
    private static void WriteCommitments(CkptWriter w, in EmlAnytimeCommitments c) { w.I64(c.ExactClasses); w.I64(c.TheoremClasses); w.I64(c.CertificateClasses); w.I64(c.ClosedObligations); w.I64(c.HeldOutCaptures); w.I64(c.HeldOutBestK); w.I64(c.VerifiedLaws); w.I64(c.VerifiedProofs); }
    private static EmlAnytimeCommitments ReadCommitments(CkptReader r) => new(r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64());
    private static void WriteCounts(CkptWriter w, in EmlDeliberationCounts c) { w.I64(c.CandidateEvaluations); w.I64(c.LogicalProgramPoints); w.I64(c.ExecutedProgramPoints); w.I64(c.InverseTransforms); w.I64(c.HashProbes); w.I64(c.JoinAttempts); w.I64(c.JoinHits); w.I64(c.ProcessTerms); w.I64(c.VerifierProgramPoints); w.I64(c.CandidateSupplyItems); w.I64(c.LawRewriteApplications); w.I64(c.LawRewriteTreeNodes); }
    private static EmlDeliberationCounts ReadCounts(CkptReader r) => new(r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64());
}

/// Semantic fixture for productive, plateau, delayed-gain, dominated, overrun, evidence, resume, and corruption paths.
internal static class EmlAnytimeCurveAssay
{
    public static int Run()
    {
        bool delayedProtected = RunDelayedGain(out EmlAnytimeCurve delayed);
        bool forkSemantics = RunForkSemantics(out EmlAnytimeCurve forkLeft, out EmlAnytimeCurve forkRight);
        bool plateauKilled = RunPlateau(out EmlAnytimeCurve plateau);
        bool dominatedKilled = RunDominated(out EmlAnytimeCurve dominated);
        bool overrunKilled = RunSingleKill(EmlAnytimeKillReasons.BudgetOverrun, overrun: true, evidence: true);
        bool evidenceKilled = RunSingleKill(EmlAnytimeKillReasons.EvidenceRegression, overrun: false, evidence: false);
        bool resumeExact = VerifyResume(delayed);
        bool corruptionRejected = VerifyCorruption(delayed);
        bool budgetComparator = VerifyBudgetComparator(out string budgetComparatorReceipt);
        bool evaluationReader = VerifyEvaluationReader(out string evaluationReaderReceipt);
        bool digestCorruption = VerifyDigestCorruptionMatrix(out string digestCorruptionReceipt);
        bool standardReader = EmlStandardAnytimeCurveReaderFixture.Verify(Console.Out);
        bool cursorCheckpoint = VerifyAnytimeFuelCursorCheckpoint();
        bool branchBoundary = VerifyAnytimeBranchBoundary(out string branchBoundaryReceipt);
        bool checkpointRebase = VerifyCheckpointRebase(out string checkpointRebaseReceipt);
        bool terminalSettlement = VerifyTerminalSettlement(out string terminalSettlementReceipt);
        Run run = Cogito.Run.New("eml-anytime-curve");
        string anytimePath = run.PathOf("anytime_curve.tsv");
        delayed.WriteTSV(anytimePath);
        run.WriteCurve("anytime_curve.tsv", File.ReadAllText(anytimePath));
        string qualityDigest = EmlAnytimeCurvePlot.Write(delayed, run);
        bool qualityPlotVerified = EmlAnytimeCurvePlot.Verify(run) && qualityDigest.Length == 64;
        bool plotHandshake = VerifyPlotHandshake(run);
        Console.WriteLine($"anytime-curve-assay\tdelayed_gain_protected={(delayedProtected ? 1 : 0)}\tfork_semantics={(forkSemantics ? 1 : 0)}\tplateau_kill={(plateauKilled ? 1 : 0)}\tdominated_kill={(dominatedKilled ? 1 : 0)}\tbudget_overrun={(overrunKilled ? 1 : 0)}\tevidence_regression={(evidenceKilled ? 1 : 0)}\tresume_exact={(resumeExact ? 1 : 0)}\tcorruption_rejected={(corruptionRejected ? 1 : 0)}\tdigest_corruption_matrix={(digestCorruption ? 1 : 0)}\tbudget_comparator={(budgetComparator ? 1 : 0)}\tevaluation_reader={(evaluationReader ? 1 : 0)}\tstandard_reader={(standardReader ? 1 : 0)}\tcursor_checkpoint={(cursorCheckpoint ? 1 : 0)}\tcheckpoint_rebase={(checkpointRebase ? 1 : 0)}\tquality_plot={(qualityPlotVerified ? 1 : 0)}\tplot_handshake={(plotHandshake ? 1 : 0)}");
        Console.WriteLine($"anytime-budget-comparator-receipt\t{budgetComparatorReceipt}");
        Console.WriteLine($"anytime-evaluation-reader-receipt\t{evaluationReaderReceipt}");
        Console.WriteLine($"anytime-digest-corruption-receipt\t{digestCorruptionReceipt}");
        Console.WriteLine($"anytime-branch-boundary-receipt\t{branchBoundaryReceipt}");
        Console.WriteLine($"anytime-checkpoint-rebase-receipt\t{checkpointRebaseReceipt}");
        Console.WriteLine($"anytime-terminal-settlement-receipt\t{terminalSettlementReceipt}");
        bool passed = delayedProtected && forkSemantics && plateauKilled && dominatedKilled && overrunKilled && evidenceKilled && resumeExact && corruptionRejected && digestCorruption && budgetComparator && evaluationReader && standardReader && cursorCheckpoint && checkpointRebase && branchBoundary && terminalSettlement && qualityPlotVerified && plotHandshake;
        Console.WriteLine($"anytime-curve-assay\t{(passed ? "PASS" : "FAIL")}\t{run.Dir}");
        return passed ? 0 : 1;
    }

    private static bool VerifyPlotHandshake(Run run)
    {
        const string header = "prefix_step\texact\tfuel_candidate_evaluations\tscored\n";
        string withHandshake = run.PathOf("plot_handshake.tsv");
        string scoredOnly = run.PathOf("plot_scored.tsv");
        File.WriteAllText(withHandshake, header + "0\t-100\t-100\t0\n1\t4\t10\t1\n2\t6\t20\t1\n");
        File.WriteAllText(scoredOnly, header + "1\t4\t10\t1\n2\t6\t20\t1\n");
        RunPlotReceipt handshake = RunPlotDocument.Load(run.Dir, "plot_handshake.tsv").CaptureReceipt();
        RunPlotReceipt scored = RunPlotDocument.Load(run.Dir, "plot_scored.tsv").CaptureReceipt();
        RunPlotSeriesReceipt Find(in RunPlotReceipt receipt, string name)
            => receipt.Series.First(series => string.Equals(series.Name, name, StringComparison.Ordinal));
        RunPlotSeriesReceipt handshakeExact = Find(in handshake, "exact");
        RunPlotSeriesReceipt scoredExact = Find(in scored, "exact");
        RunPlotSeriesReceipt handshakeFuel = Find(in handshake, "fuel_candidate_evaluations");
        RunPlotSeriesReceipt scoredFuel = Find(in scored, "fuel_candidate_evaluations");
        return handshake.HasScoredColumn && handshake.PhysicalRows == 3 && handshake.ScoredRows == 2
            && scored.PhysicalRows == 2 && scored.ScoredRows == 2
            && handshakeExact == scoredExact && handshakeFuel == scoredFuel
            && handshakeExact.Minimum == 4 && handshakeExact.Maximum == 6 && handshakeExact.NumericRows == 2;
    }

    private static bool VerifyAnytimeFuelCursorCheckpoint()
    {
        Run run = Cogito.Run.New("eml-anytime-cursor-checkpoint");
        ReplayCalc source = ReplayCalc.Mount(0xC0FFEEUL);
        source.BindAnytimeRun(run, "cursor-config", "cursor-chain", "cursor-arm");
        _ = source.CaptureDeepRematchEvaluationHandshake();
        EmlDeepRematchFuelCursor cursor = source.DeepRematchFuelCursor;
        byte[] saved = SaveReplayState(source);
        ReplayCalc restored = ReplayCalc.Mount(0xC0FFEEUL);
        try
        {
            LoadReplayState(restored, saved);
            byte[] roundTrip = SaveReplayState(restored);
            bool exact = saved.AsSpan().SequenceEqual(roundTrip)
                && restored.DeepRematchFuelCursor == cursor
                && restored.ReadDeepRematchFuelTotalsSinceHandshake(planned: true, refund: false) == EmlDeliberationCounts.Zero;
            bool checkpointSectionBound = ReplayCalc.ReadDeepRematchFuelCursorFromCheckpointImage(saved) == cursor;
            EmlDeepRematchFuelCursor highWater = cursor with { EvaluatorCalls = cursor.EvaluatorCalls + 1 };
            EmlDeliberationCounts highWaterPlanned = highWater.Planned;
            EmlDeliberationCounts highWaterActual = highWater.Actual;
            EmlDeliberationCounts highWaterRefund = highWater.Refund;
            highWater = highWater with { Digest = EmlDeepRematchFuelCursor.ComputeDigest(highWater.SettlementCount, highWater.EvaluatorCalls,
                in highWaterPlanned, in highWaterActual, in highWaterRefund, highWater.PointID, highWater.PointDigest, highWater.SettlementDigest) };
            bool highWaterRejected = RejectCursor(restored, highWater, highWater: true);
            EmlDeepRematchFuelCursor reordered = cursor with { SettlementDigest = new string('0', 64) };
            EmlDeliberationCounts reorderedPlanned = reordered.Planned;
            EmlDeliberationCounts reorderedActual = reordered.Actual;
            EmlDeliberationCounts reorderedRefund = reordered.Refund;
            reordered = reordered with { Digest = EmlDeepRematchFuelCursor.ComputeDigest(reordered.SettlementCount, reordered.EvaluatorCalls,
                in reorderedPlanned, in reorderedActual, in reorderedRefund, reordered.PointID, reordered.PointDigest, reordered.SettlementDigest) };
            bool prefixRejected = RejectCursor(restored, reordered, highWater: false);
            byte[] corrupted = saved.ToArray();
            int cursorTag = FindTag(corrupted, 0x41464355);
            int digestOffset = checked(cursorTag + 4 + 4 + 4 + 8 + (12 * 8 * 3));
            corrupted[digestOffset + 1] ^= 0x01;
            bool corruptionRejected = false;
            try { LoadReplayState(ReplayCalc.Mount(0xC0FFEEUL), corrupted); }
            catch (InvalidDataException) { corruptionRejected = true; }
            return exact && checkpointSectionBound && highWaterRejected && prefixRejected && corruptionRejected;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool VerifyAnytimeBranchBoundary(out string receipt)
    {
        ReplayCalc source = ReplayCalc.Mount(0xBADC0DEUL);
        Run parent = Cogito.Run.New("eml-anytime-branch-parent");
        source.BindAnytimeRun(parent, "branch-config", "branch-chain", "parent-arm");
        _ = source.CaptureDeepRematchEvaluationHandshake();
        for (int i = 1; i <= 4; i++) AppendBranchPoint(source, i);
        ReplayCalcCheckpointDelta parentDelta = source.CaptureCheckpointDelta();
        source.CommitCheckpointDelta(in parentDelta);
        byte[] parentState = SaveReplayState(source);
        string parentDigest = source.AnytimeCurve.Digest;

        bool fourFresh = true;
        bool fourFirstCaptures = true;
        bool fourLineages = true;
        bool fourRoundTrips = true;
        bool cursorOnlyRejected = false;
        bool emptyRebind = true;
        for (int childIndex = 0; childIndex < 4; childIndex++)
        {
            ReplayCalc childSource = ReplayCalc.Mount(0xBADC0DEUL);
            LoadReplayState(childSource, parentState);
            Run child = Cogito.Run.New($"eml-anytime-branch-child-{childIndex}");
            if (childIndex == 0)
            {
                // Replay can stage a typed successor scope before the runtime binds its
                // child run. The curve is empty, but LoadState's inherited parent cursor
                // (and AFCU) are still live; BindAnytimeRun must recognize that boundary.
                EmlAnytimeRebaseScope stagedScope = new(
                    "staged-successor", "branch-config", "branch-chain", "staged-arm", 1, parentDigest);
                childSource.AnytimeCurve.ApplyCheckpointDelta(0, [], 0, [], stagedScope);
                emptyRebind &= childSource.AnytimeCurve.Points.Count == 0
                    && childSource.AnytimeCurve.ScopeArmID == "staged-arm";
            }
            childSource.BindAnytimeRun(child, "branch-config", "branch-chain", $"child-arm-{childIndex}", childIndex, parentDigest);
            fourFresh &= childSource.AnytimeCurve.Points.Count == 0 && childSource.AnytimeCurve.Kills.Count == 0
                && childSource.PersistedDeepRematchFuelCursor is null;
            try
            {
                ReplayCalcCheckpointDelta empty = childSource.CaptureCheckpointDelta();
                fourFirstCaptures &= empty.AnytimePointCursor == 0 && empty.AnytimeKillCursor == 0;
                childSource.CommitCheckpointDelta(in empty);
            }
            catch (InvalidDataException)
            {
                fourFirstCaptures = false;
            }

            EmlAnytimeCurvePoint handshake = childSource.CaptureDeepRematchEvaluationHandshake();
            fourLineages &= handshake.ParentPointID == parentDigest;
            ReplayCalcCheckpointDelta handshakeDelta = childSource.CaptureCheckpointDelta();
            fourFirstCaptures &= handshakeDelta.AnytimePointCursor == 0 && handshakeDelta.AnytimePoints.Length == 1;
            childSource.CommitCheckpointDelta(in handshakeDelta);
            EmlAnytimeCurvePoint next = AppendBranchPoint(childSource, childIndex + 1);
            fourLineages &= next.ParentPointID == handshake.Digest;
            ReplayCalcCheckpointDelta mutation = childSource.CaptureCheckpointDelta();
            fourFirstCaptures &= mutation.AnytimePointCursor == 1 && mutation.AnytimePoints.Length == 1;
            childSource.CommitCheckpointDelta(in mutation);
            byte[] saved = SaveReplayState(childSource);
            ReplayCalc restored = ReplayCalc.Mount(0xBADC0DEUL);
            LoadReplayState(restored, saved);
            fourRoundTrips &= saved.AsSpan().SequenceEqual(SaveReplayState(restored));
        }

        EmlAnytimeCurve nonfresh = new();
        AppendEvaluationHandshake(nonfresh);
        try
        {
            nonfresh.ApplyCheckpointDelta(0, [], 0, []);
        }
        catch (InvalidDataException exception)
        {
            cursorOnlyRejected = exception.Message.Contains("cursor gap", StringComparison.Ordinal);
        }
        receipt = string.Join(';', $"children={(fourFresh ? 4 : 0)}", $"first_mutation={(fourFirstCaptures ? 1 : 0)}",
            $"lineage={(fourLineages ? 1 : 0)}", $"save_load_save={(fourRoundTrips ? 1 : 0)}",
            $"cursor_only_rejected={(cursorOnlyRejected ? 1 : 0)}", $"empty_rebind={(emptyRebind ? 1 : 0)}",
            $"verdict={(fourFresh && fourFirstCaptures && fourLineages && fourRoundTrips && cursorOnlyRejected && emptyRebind ? "pass" : "fail")}");
        return fourFresh && fourFirstCaptures && fourLineages && fourRoundTrips && cursorOnlyRejected && emptyRebind;
    }

    private static bool VerifyCheckpointRebase(out string receipt)
    {
        EmlAnytimeCurve curve = new();
        AppendEvaluationHandshake(curve);
        AppendEvaluationScored(curve, 1, new(2, 1, 1, 0, 0, 0, 0, 0));
        string scopeBefore = string.Join('/', curve.ScopeRunID, curve.ScopeConfigID, curve.ScopeChainID, curve.ScopeArmID, curve.ScopeRung);
        string predecessorPointID = curve.Digest;
        bool historicalShape = true;
        bool mutationRejected = false;
        try
        {
            EmlAnytimeCurvePoint point = curve.Points[^1];
            EmlAnytimeRebaseScope malformedScope = new(
                curve.ScopeRunID, curve.ScopeConfigID, curve.ScopeChainID, curve.ScopeArmID, curve.ScopeRung, curve.Digest);
            curve.ApplyCheckpointDelta(0, [point], 0, [], malformedScope);
        }
        catch (InvalidDataException) { historicalShape = false; }
        mutationRejected = !historicalShape;
        historicalShape = true;
        try
        {
            EmlAnytimeRebaseScope successorScope = new(
                "fixture-successor-run", "fixture-successor-config", curve.ScopeChainID,
                "fixture-successor-arm", curve.ScopeRung + 1, predecessorPointID);
            curve.ApplyCheckpointDelta(0, [], 0, [], successorScope);
        }
        catch (InvalidDataException) { historicalShape = false; }
        bool scopeRebound = string.Equals("fixture-successor-run/fixture-successor-config/fixture-chain/fixture-successor-arm/1",
                string.Join('/', curve.ScopeRunID, curve.ScopeConfigID, curve.ScopeChainID, curve.ScopeArmID, curve.ScopeRung), StringComparison.Ordinal)
            && !string.Equals(scopeBefore, string.Join('/', curve.ScopeRunID, curve.ScopeConfigID, curve.ScopeChainID, curve.ScopeArmID, curve.ScopeRung), StringComparison.Ordinal)
            && curve.Points.Count == 0 && curve.Kills.Count == 0 && curve.Digest.Length == 0;
        EmlAnytimeBoundaryReceipt successorReceipt = new(
            "fixture-successor-run", "fixture-successor-config", "fixture-chain", "fixture-successor-arm",
            predecessorPointID, 1, 1, 1, "ruler.window.commit", EmlAnytimeCommitments.Zero,
            EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero,
            0, 0, 0, true, true, false, false, false, false, true, false, 0, "",
            "successor-evidence", double.NaN, 0, 0, 0);
        EmlAnytimeCurvePoint successorPoint = EmlAnytimeCurvePoint.Create(in successorReceipt, "", dominated: false);
        bool wrongSuccessorRejected;
        EmlAnytimeBoundaryReceipt wrongReceipt = successorReceipt with { RunID = "wrong-successor-run" };
        EmlAnytimeCurvePoint wrongPoint = EmlAnytimeCurvePoint.Create(in wrongReceipt, "", dominated: false);
        try { curve.ApplyCheckpointDelta(0, [wrongPoint], 0, []); wrongSuccessorRejected = false; }
        catch (InvalidDataException exception)
        { wrongSuccessorRejected = exception.Message.Contains("successor scope", StringComparison.Ordinal); }
        curve.ApplyCheckpointDelta(0, [successorPoint], 0, []);
        bool ordinaryGapRejected = false;
        try { curve.ApplyCheckpointDelta(0, [], 0, []); }
        catch (InvalidDataException exception) { ordinaryGapRejected = exception.Message.Contains("cursor gap", StringComparison.Ordinal); }
        receipt = string.Join(';', $"historical_shape={(historicalShape ? 1 : 0)}", $"scope_rebound={(scopeRebound ? 1 : 0)}",
            $"mutation_rejected={(mutationRejected ? 1 : 0)}", $"wrong_successor_rejected={(wrongSuccessorRejected ? 1 : 0)}",
            $"ordinary_gap_rejected={(ordinaryGapRejected ? 1 : 0)}",
            $"verdict={(historicalShape && scopeRebound && mutationRejected && wrongSuccessorRejected && ordinaryGapRejected ? "pass" : "fail")}");
        return historicalShape && scopeRebound && mutationRejected && wrongSuccessorRejected && ordinaryGapRejected;
    }

    private static EmlAnytimeCurvePoint AppendBranchPoint(ReplayCalc dream, int step)
    {
        EmlAnytimeCurve curve = dream.AnytimeCurve;
        EmlAnytimeCurvePoint prior = curve.Points[^1];
        EmlAnytimeCommitments quality = prior.Quality with { ExactClasses = prior.Quality.ExactClasses + 1 };
        string evidence = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', curve.ScopeRunID, step, prior.Digest))));
        EmlAnytimeBoundaryReceipt boundary = new(
            curve.ScopeRunID, curve.ScopeConfigID, curve.ScopeChainID, curve.ScopeArmID, prior.Digest, curve.ScopeRung,
            step, prior.WindowIndex + 1, "ruler.window.commit", quality,
            EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero,
            0, 0, 0, true, true, false, false, false, false, true, false, 0, "", evidence,
            double.NaN, 0, 0, 0);
        return curve.Append(in boundary);
    }

    private static bool VerifyTerminalSettlement(out string receipt)
    {
        EmlAnytimeCurve curve = new();
        AppendEvaluationHandshake(curve);
        for (int step = 1; step <= 500; step++)
            AppendEvaluationScored(curve, step, new(step == 500 ? 64 : 32, step == 500 ? 1 : 0, step == 500 ? 1 : 0, 0, 0, 0, 0, 0));
        EmlAnytimeCurvePoint prior = curve.Points[^1];
        EmlAnytimeBoundaryReceipt terminal = new(
            "fixture-run", "fixture-config", "fixture-chain", "evaluation", prior.Digest, 0,
            prior.PrefixStep, prior.WindowIndex, "ruler.window.terminal", prior.Quality, prior.Fuel,
            EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, prior.EvaluatorIntervals, 0, 0,
            true, true, false, false, false, false, true, true, prior.GraceUntilWindow, "",
            "terminal-evidence", prior.Residual, prior.Rate, prior.Meanz, 0);
        EmlAnytimeCurvePoint point = curve.Append(in terminal);
        bool duplicateIdempotent = curve.Append(in terminal).Equals(point);
        bool terminalShape = point.RunTerminal && point.Boundary == "ruler.window.terminal"
            && point.PendingResolution == false && point.WindowSettled && point.WindowComplete
            && point.WindowActualFuel == EmlDeliberationCounts.Zero && point.WindowEvaluatorIntervals == 0;
        bool saveLoadSaveExact = VerifyResume(curve);
        Run run = Cogito.Run.New("eml-anytime-terminal-settlement");
        string path = run.PathOf("eml_anytime_curve.tsv");
        curve.WriteTSV(path);
        EmlAnytimeEvaluationPrefix parsed = EmlAnytimeEvaluationReader.Read(path,
            new("fixture-run", "fixture-config", "fixture-chain", "evaluation", 0, FirstStep: 1, LastStep: 500));
        bool readerAccepted = curve.Points[^1].RunTerminal && parsed.AcceptedStep == 500 && parsed.Passed;
        bool appendAfterRejected;
        try
        {
            EmlAnytimeBoundaryReceipt after = terminal with { ParentPointID = point.PointID, PrefixStep = 501, WindowIndex = 501, RunTerminal = false, Boundary = "ruler.window.commit" };
            _ = curve.Append(in after);
            appendAfterRejected = false;
        }
        catch (InvalidDataException) { appendAfterRejected = true; }
        bool passed = terminalShape && duplicateIdempotent && saveLoadSaveExact && readerAccepted && appendAfterRejected;
        bool partialSettlement = VerifyPartialTerminalSettlement(out string partialReceipt);
        passed &= partialSettlement;
        receipt = string.Join(';', $"aligned_terminal={(terminalShape ? 1 : 0)}", $"duplicate_idempotent={(duplicateIdempotent ? 1 : 0)}",
            $"aligned_save_load_save={(saveLoadSaveExact ? 1 : 0)}", $"aligned_reader={(readerAccepted ? 1 : 0)}",
            $"append_after_rejected={(appendAfterRejected ? 1 : 0)}", partialReceipt, $"verdict={(passed ? "pass" : "fail")}");
        return passed;
    }

    private static bool VerifyPartialTerminalSettlement(out string receipt)
    {
        EmlAnytimeCurve curve = new();
        AppendEvaluationHandshake(curve);
        for (int step = 1; step <= 3; step++)
            AppendEvaluationScored(curve, step, new(64, 1, 1, 0, 0, 0, 0, 0));

        EmlDeliberationCounts one = new(1, 2, 1, 0, 0, 0, 0, 1, 0);
        EmlDeliberationCounts cumulative = Scale(one, 4);
        EmlAnytimeCurvePoint prior = curve.Points[^1];
        EmlAnytimeBoundaryReceipt partial = new(
            "fixture-run", "fixture-config", "fixture-chain", "evaluation", prior.Digest, 0,
            5, prior.WindowIndex + 1, "ruler.window.commit", prior.Quality, cumulative, one, one,
            4, 1, 1, true, true, false, false, false, false, true, false, 0, "", "partial-evidence",
            .9, 9, -.2, 7);
        EmlAnytimeCurvePoint partialPoint = curve.Append(in partial);
        _ = curve.EvaluateKill(in partial, in partialPoint);
        EmlAnytimeBoundaryReceipt terminal = new(
            "fixture-run", "fixture-config", "fixture-chain", "evaluation", partialPoint.Digest, 0,
            5, prior.WindowIndex + 1, "ruler.window.terminal", prior.Quality, cumulative,
            EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, 4, 0, 0,
            true, true, false, false, false, false, true, true, 0, "", "partial-terminal-evidence",
            .9, 9, -.2, 0);
        EmlAnytimeCurvePoint terminalPoint = curve.Append(in terminal);
        _ = curve.EvaluateKill(in terminal, in terminalPoint);
        bool shape = partialPoint.Boundary == "ruler.window.commit" && !partialPoint.RunTerminal
            && partialPoint.PrefixStep == 5 && partialPoint.WindowIndex == prior.WindowIndex + 1
            && partialPoint.WindowActualFuel == one && partialPoint.WindowEvaluatorIntervals == 1
            && terminalPoint.RunTerminal && terminalPoint.WindowActualFuel == EmlDeliberationCounts.Zero
            && terminalPoint.WindowEvaluatorIntervals == 0 && terminalPoint.PrefixStep == partialPoint.PrefixStep
            && terminalPoint.WindowIndex == partialPoint.WindowIndex;
        bool saveLoadSave = VerifyResume(curve);
        Run run = Cogito.Run.New("eml-anytime-partial-terminal");
        string path = run.PathOf("eml_anytime_curve.tsv");
        curve.WriteTSV(path);
        EmlAnytimeEvaluationPrefix parsed = EmlAnytimeEvaluationReader.Read(path,
            new("fixture-run", "fixture-config", "fixture-chain", "evaluation", 0, FirstStep: 1, LastStep: 5));
        bool reader = parsed.Passed && parsed.AcceptedStep == 1 && parsed.EvaluatorIntervals == 1;
        bool passed = shape && saveLoadSave && reader;
        receipt = string.Join(';', $"partial_shape={(shape ? 1 : 0)}", $"partial_save_load_save={(saveLoadSave ? 1 : 0)}",
            $"partial_reader={(reader ? 1 : 0)}", $"partial_terminal_step={terminalPoint.PrefixStep}",
            $"partial_window={terminalPoint.WindowIndex}");
        return passed;
    }

    private static bool RejectCursor(ReplayCalc dream, EmlDeepRematchFuelCursor cursor, bool highWater)
    {
        try
        {
            _ = dream.ReadDeepRematchFuelTotals(in cursor, planned: false, refund: false);
            return false;
        }
        catch (InvalidDataException exception)
        {
            return highWater
                ? exception.Message.Contains("high-water", StringComparison.Ordinal)
                : exception.Message.Contains("truncated or reordered", StringComparison.Ordinal);
        }
    }

    private static byte[] SaveReplayState(ReplayCalc dream)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) dream.SaveState(writer);
        return stream.ToArray();
    }

    private static void LoadReplayState(ReplayCalc dream, byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using CkptReader reader = new(stream);
        dream.LoadState(reader);
    }

    private static int FindTag(byte[] bytes, uint tag)
    {
        byte[] needle = BitConverter.GetBytes(tag);
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidDataException("cursor checkpoint fixture could not locate AFCU section");
    }

    private static bool RunDelayedGain(out EmlAnytimeCurve curve)
    {
        curve = new();
        Append(curve, 400, 0, new(1, 0, 0, 0, 0, 0, 0, 0), false, "", 4);
        Append(curve, 460, 1, new(1, 0, 0, 0, 0, 0, 0, 0), false, "", 4, activeFork: true);
        bool noKillBeforeGain = curve.Kills.Count == 0;
        Append(curve, 465, 2, new(2, 0, 1, 0, 0, 0, 0, 0), false, "", 4);
        Append(curve, 473, 3, new(2, 1, 1, 0, 0, 0, 0, 0), false, "", 4);
        Append(curve, 480, 4, new(2, 1, 1, 0, 1, 0, 0, 0), false, "", 4);
        return noKillBeforeGain && (curve.Kills.Count == 0 || curve.Kills[0].PrefixStep >= 480);
    }

    private static bool RunPlateau(out EmlAnytimeCurve curve)
    {
        curve = new(); Append(curve, 1, 0, new(1, 0, 0, 0, 0, 0, 0, 0)); Append(curve, 2, 1, new(1, 0, 0, 0, 0, 0, 0, 0)); Append(curve, 3, 2, new(1, 0, 0, 0, 0, 0, 0, 0));
        return curve.Kills.Count == 1 && curve.Kills[0].Reason == EmlAnytimeKillReasons.NoProgressAfterGrace;
    }

    private static bool RunDominated(out EmlAnytimeCurve curve)
    {
        curve = new(); Append(curve, 1, 0, new(1, 0, 0, 0, 0, 0, 0, 0));
        string dominator = curve.Points[0].PointID;
        Append(curve, 2, 1, new(1, 0, 0, 0, 0, 0, 0, 0), false, dominator);
        Append(curve, 3, 2, new(1, 0, 0, 0, 0, 0, 0, 0), false, dominator);
        return curve.Kills.Count == 1 && curve.Kills[0].Reason == EmlAnytimeKillReasons.DominatedAfterGrace;
    }

    private static bool RunForkSemantics(out EmlAnytimeCurve left, out EmlAnytimeCurve right)
    {
        left = new(); right = new();
        AppendArm(left, "left", 1, 0, new(1, 0, 0, 0, 0, 0, 0, 0), "parent-seed", rung: 0);
        AppendArm(right, "right", 1, 0, new(1, 0, 0, 0, 0, 0, 0, 0), "parent-seed", rung: 0);
        AppendArm(left, "left", 2, 1, new(2, 0, 0, 0, 0, 0, 0, 0), rung: 1);
        AppendArm(right, "right", 2, 1, new(1, 0, 0, 0, 0, 0, 0, 0), rung: 1);
        AppendArm(left, "left", 3, 2, new(3, 0, 0, 0, 0, 0, 0, 0), rung: 1);
        AppendArm(right, "right", 3, 2, new(3, 0, 0, 0, 0, 0, 0, 0), rung: 1);
        bool identities = left.Points.All(point => point.ArmID == "left") && right.Points.All(point => point.ArmID == "right")
            && left.Points[0].ParentPointID == "parent-seed" && right.Points[0].ParentPointID == "parent-seed"
            && left.Points[0].ChainID == right.Points[0].ChainID;
        bool aligned = left.Points.Select(point => point.WindowIndex).SequenceEqual(right.Points.Select(point => point.WindowIndex));
        bool intermediateRegression = !right.Points[1].Quality.Dominates(left.Points[1].Quality)
            && right.Points[^1].Quality.Dominates(left.Points[^1].Quality);
        bool resumeExact = VerifyResume(left) && VerifyResume(right);
        return identities && aligned && intermediateRegression && resumeExact;
    }

    private static bool RunSingleKill(EmlAnytimeKillReasons reason, bool overrun, bool evidence)
    {
        EmlAnytimeCurve curve = new(); EmlAnytimeBoundaryReceipt receipt = Make("single", 1, 0, EmlAnytimeCommitments.Zero, overrun, evidence);
        if (overrun)
        {
            try { _ = curve.Append(in receipt); return false; }
            catch (InvalidDataException) { return reason == EmlAnytimeKillReasons.BudgetOverrun; }
        }
        EmlAnytimeCurvePoint point = curve.Append(in receipt);
        EmlAnytimeKillReceipt? kill = curve.EvaluateKill(in receipt, in point, evidenceRegression: !evidence);
        return kill is { Reason: var actual } && actual == reason;
    }

    private static bool VerifyBudgetComparator(out string receipt)
    {
        EmlAnytimeCommitments early = new(6, 0, 0, 0, 0, 0, 0, 0);
        EmlAnytimeCommitments later = new(8, 0, 0, 0, 0, 0, 0, 0);
        EmlAnytimeBudgetPoint launchpadEarly = new(100, 100, 100, early, true, true, "launchpad-early");
        EmlAnytimeBudgetPoint launchpadLate = new(200, 200, 200, early, true, true, "launchpad-late");
        EmlAnytimeBudgetPoint grammarEarly = new(100, 100, 100, early, true, true, "grammar-early");
        EmlAnytimeBudgetPoint grammarLate = new(210, 210, 210, later, true, true, "grammar-late");
        EmlAnytimeBudgetComparison adaptation = EmlAnytimeBudgetComparator.Compare(
            [launchpadEarly, launchpadLate], [grammarEarly, grammarLate]);
        EmlAnytimeBudgetComparison emulation = EmlAnytimeBudgetComparator.Compare(
            [launchpadEarly, launchpadLate],
            [grammarEarly, new(210, 210, 210, early, true, true, "grammar-equal-late")]);
        EmlAnytimeBudgetComparison swappedAxis = EmlAnytimeBudgetComparator.Compare(
            [launchpadEarly, launchpadLate],
            [new(100, 100, 100, new(5, 1, 0, 0, 0, 0, 0, 0), true, true, "grammar-swapped-early"),
             new(210, 210, 210, early, true, true, "grammar-swapped-late")]);
        bool corruptionRejected;
        try
        {
            _ = EmlAnytimeBudgetComparator.Compare(
                [launchpadEarly with { PointID = "" }], [grammarEarly]);
            corruptionRejected = false;
        }
        catch (InvalidDataException)
        {
            corruptionRejected = true;
        }
        bool passed = adaptation.Passed && adaptation.StepFunction && adaptation.StrictLaterGain
            && emulation.Comparable && emulation.RightNoWorse && !emulation.StrictLaterGain
            && !swappedAxis.RightNoWorse && swappedAxis.Digest != emulation.Digest
            && corruptionRejected;
        receipt = string.Join(';',
            $"adaptation_passed={(adaptation.Passed ? 1 : 0)}",
            $"adaptation_comparable={(adaptation.Comparable ? 1 : 0)}",
            $"adaptation_step_function={(adaptation.StepFunction ? 1 : 0)}",
            $"adaptation_no_worse={(adaptation.RightNoWorse ? 1 : 0)}",
            $"adaptation_strict={(adaptation.StrictLaterGain ? 1 : 0)}",
            $"emulation_comparable={(emulation.Comparable ? 1 : 0)}",
            $"emulation_no_worse={(emulation.RightNoWorse ? 1 : 0)}",
            $"emulation_strict={(emulation.StrictLaterGain ? 1 : 0)}",
            $"axis_swap_rejected={(!swappedAxis.RightNoWorse ? 1 : 0)}",
            $"axis_swap_digest_distinct={(swappedAxis.Digest != emulation.Digest ? 1 : 0)}",
            $"corruption_rejected={(corruptionRejected ? 1 : 0)}",
            $"verdict={(passed ? "pass" : "fail")}");
        return passed;
    }

    private static bool VerifyEvaluationReader(out string receipt)
    {
        EmlAnytimeCurve curve = new();
        AppendEvaluationHandshake(curve);
        for (int step = 1; step <= 500; step++)
        {
            long exact = step >= 250 ? 64 : 32;
            AppendEvaluationScored(curve, step, new(exact, step >= 250 ? 1 : 0, step >= 250 ? 1 : 0, 0, 0, 0, 0, 0));
        }
        Run run = Cogito.Run.New("eml-anytime-evaluation-reader");
        string path = run.PathOf("eml_anytime_curve.tsv");
        curve.WriteTSV(path);
        EmlAnytimeEvaluationPrefix result = EmlAnytimeEvaluationReader.Read(path,
            new("fixture-run", "fixture-config", "fixture-chain", "evaluation", 0));
        EmlDeliberationCounts expectedPlanned = new(250, 500, 250, 0, 0, 0, 0, 250, 0);
        EmlDeliberationCounts expectedRefund = EmlDeliberationCounts.Zero;
        bool accepted = result.Passed && !result.Banked && result.AcceptedStep == 250
            && result.AcceptedPoint.Quality.ExactClasses == 64 && result.Certificates == 1
            && result.PlannedFuel == expectedPlanned && result.ActualFuel == expectedPlanned
            && result.RefundFuel == expectedRefund && result.EvaluatorIntervals == 250
            && result.EvaluatorPerCertificate is 250.0;

        EmlAnytimeCurve zero = new();
        AppendEvaluationHandshake(zero);
        for (int step = 1; step <= 500; step++)
            AppendEvaluationScored(zero, step, new(64, 0, 0, 0, 0, 0, 0, 0));
        string zeroPath = run.PathOf("eml_anytime_curve.zero.tsv");
        zero.WriteTSV(zeroPath);
        EmlAnytimeEvaluationPrefix banked = EmlAnytimeEvaluationReader.Read(zeroPath,
            new("fixture-run", "fixture-config", "fixture-chain", "evaluation", 0));
        bool zeroBanked = banked.Banked && !banked.Passed && banked.EvaluatorPerCertificate is null
            && banked.BankReason.Contains("zero certificates", StringComparison.Ordinal);
        EmlAnytimeCurve lone = new();
        AppendEvaluationHandshake(lone);
        AppendEvaluationScored(lone, 499, new(64, 0, 1, 0, 0, 0, 0, 0));
        string lonePath = run.PathOf("eml_anytime_curve.lone.tsv");
        lone.WriteTSV(lonePath);
        bool originRejected;
        try
        {
            _ = EmlAnytimeEvaluationReader.Read(lonePath,
                new("fixture-run", "fixture-config", "fixture-chain", "evaluation", 0));
            originRejected = false;
        }
        catch (InvalidDataException)
        {
            originRejected = true;
        }
        receipt = string.Join(';', $"accepted={(accepted ? 1 : 0)}", $"zero_certificates_banked={(zeroBanked ? 1 : 0)}",
            $"origin_step_rejected={(originRejected ? 1 : 0)}", $"step={result.AcceptedStep}", $"certificates={result.Certificates}", $"rate={(result.EvaluatorPerCertificate ?? -1).ToString("G17", CultureInfo.InvariantCulture)}",
            $"planned_candidate_evaluations={result.PlannedFuel.CandidateEvaluations}", $"refund_candidate_evaluations={result.RefundFuel.CandidateEvaluations}",
            $"verdict={(accepted && zeroBanked && originRejected ? "pass" : "fail")}");
        return accepted && zeroBanked && originRejected;
    }

    private static bool VerifyResume(EmlAnytimeCurve source)
    {
        using MemoryStream ms = new(); using (CkptWriter writer = new(ms)) source.Save(writer); ms.Position = 0; EmlAnytimeCurve restored = new(); using (CkptReader reader = new(ms)) restored.TryLoad(reader); return restored.Points.SequenceEqual(source.Points) && restored.Digest == source.Digest;
    }

    private static bool VerifyCorruption(EmlAnytimeCurve source)
    {
        using MemoryStream ms = new(); using (CkptWriter writer = new(ms)) source.Save(writer); byte[] bytes = ms.ToArray(); bytes[^1] ^= 1; try { using MemoryStream broken = new(bytes); using CkptReader reader = new(broken); new EmlAnytimeCurve().TryLoad(reader); return false; } catch (InvalidDataException) { return true; }
    }

    private static bool VerifyDigestCorruptionMatrix(out string receipt)
    {
        EmlAnytimeCurve curve = new();
        AppendEvaluation(curve, 0, 0, new(32, 1, 1, 1, 1, 1, 1, 1));
        AppendEvaluation(curve, 250, 1, new(64, 2, 2, 2, 2, 2, 2, 2));
        EmlAnytimeCurvePoint point = curve.Points[1];
        List<(string Name, Func<EmlAnytimeCurvePoint, EmlAnytimeCurvePoint> Mutate)> cases = [];
        cases.Add(("point", static p => p with { PointID = "corrupt-point" }));
        cases.Add(("previous", static p => p with { PreviousDigest = "corrupt-previous" }));
        cases.Add(("digest", static p => p with { Digest = "corrupt-digest" }));
        cases.Add(("parent", static p => p with { ParentPointID = "corrupt-parent" }));
        cases.Add(("run", static p => p with { RunID = "corrupt-run" }));
        cases.Add(("config", static p => p with { ConfigID = "corrupt-config" }));
        cases.Add(("chain", static p => p with { ChainID = "corrupt-chain" }));
        cases.Add(("arm", static p => p with { ArmID = "corrupt-arm" }));
        cases.Add(("rung", static p => p with { Rung = p.Rung + 1 }));
        cases.Add(("prefix-step", static p => p with { PrefixStep = p.PrefixStep + 1 }));
        cases.Add(("window", static p => p with { WindowIndex = p.WindowIndex + 1 }));
        cases.Add(("boundary", static p => p with { Boundary = "corrupt-boundary" }));
        cases.Add(("evidence-digest", static p => p with { EvidenceDigest = "corrupt-evidence" }));
        cases.Add(("evidence", static p => p with { EvidenceVerified = !p.EvidenceVerified }));
        cases.Add(("window-complete", static p => p with { WindowComplete = !p.WindowComplete }));
        cases.Add(("kill-eligible", static p => p with { KillEligible = !p.KillEligible }));
        cases.Add(("dominated", static p => p with { Dominated = !p.Dominated }));
        cases.Add(("active-funding", static p => p with { ActiveFunding = !p.ActiveFunding }));
        cases.Add(("active-fork", static p => p with { ActiveFork = !p.ActiveFork }));
        cases.Add(("active-obligation", static p => p with { ActiveObligation = !p.ActiveObligation }));
        cases.Add(("pending-resolution", static p => p with { PendingResolution = !p.PendingResolution }));
        cases.Add(("window-settled", static p => p with { WindowSettled = !p.WindowSettled }));
        cases.Add(("run-terminal", static p => p with { RunTerminal = !p.RunTerminal }));
        cases.Add(("grace", static p => p with { GraceUntilWindow = p.GraceUntilWindow + 1 }));
        cases.Add(("dominator", static p => p with { DominatorPointID = "corrupt-dominator" }));
        cases.Add(("residual", static p => p with { Residual = p.Residual + 1 }));
        cases.Add(("rate", static p => p with { Rate = p.Rate + 1 }));
        cases.Add(("meanz", static p => p with { Meanz = p.Meanz + 1 }));
        cases.Add(("wall", static p => p with { WallMilliseconds = p.WallMilliseconds + 1 }));
        cases.Add(("exact-knee", static p => p with { Quality = p.Quality with { ExactClasses = p.Quality.ExactClasses + 1 } }));
        cases.Add(("theorem", static p => p with { Quality = p.Quality with { TheoremClasses = p.Quality.TheoremClasses + 1 } }));
        cases.Add(("certificates", static p => p with { Quality = p.Quality with { CertificateClasses = p.Quality.CertificateClasses + 1 } }));
        cases.Add(("closed-obligations", static p => p with { Quality = p.Quality with { ClosedObligations = p.Quality.ClosedObligations + 1 } }));
        cases.Add(("heldout-captures", static p => p with { Quality = p.Quality with { HeldOutCaptures = p.Quality.HeldOutCaptures + 1 } }));
        cases.Add(("heldout-bestk", static p => p with { Quality = p.Quality with { HeldOutBestK = p.Quality.HeldOutBestK + 1 } }));
        cases.Add(("verified-laws", static p => p with { Quality = p.Quality with { VerifiedLaws = p.Quality.VerifiedLaws + 1 } }));
        cases.Add(("verified-proofs", static p => p with { Quality = p.Quality with { VerifiedProofs = p.Quality.VerifiedProofs + 1 } }));

        (string Name, Func<EmlDeliberationCounts, EmlDeliberationCounts> Mutate)[] axes =
        [
            ("candidate-evaluations", static c => c with { CandidateEvaluations = c.CandidateEvaluations + 1 }),
            ("logical-program-points", static c => c with { LogicalProgramPoints = c.LogicalProgramPoints + 1 }),
            ("executed-program-points", static c => c with { ExecutedProgramPoints = c.ExecutedProgramPoints + 1 }),
            ("inverse-transforms", static c => c with { InverseTransforms = c.InverseTransforms + 1 }),
            ("hash-probes", static c => c with { HashProbes = c.HashProbes + 1 }),
            ("join-attempts", static c => c with { JoinAttempts = c.JoinAttempts + 1 }),
            ("join-hits", static c => c with { JoinHits = c.JoinHits + 1 }),
            ("process-terms", static c => c with { ProcessTerms = c.ProcessTerms + 1 }),
            ("verifier-program-points", static c => c with { VerifierProgramPoints = c.VerifierProgramPoints + 1 }),
            ("candidate-supply-items", static c => c with { CandidateSupplyItems = c.CandidateSupplyItems + 1 }),
            ("law-rewrite-applications", static c => c with { LawRewriteApplications = c.LawRewriteApplications + 1 }),
            ("law-rewrite-tree-nodes", static c => c with { LawRewriteTreeNodes = c.LawRewriteTreeNodes + 1 }),
        ];
        void AddFuel(string label, Func<EmlAnytimeCurvePoint, EmlDeliberationCounts> select,
            Func<EmlAnytimeCurvePoint, EmlDeliberationCounts, EmlAnytimeCurvePoint> replace,
            Func<EmlDeliberationCounts, EmlDeliberationCounts> mutate)
            => cases.Add((label, pointValue => replace(pointValue, mutate(select(pointValue)))));
        foreach ((string name, Func<EmlDeliberationCounts, EmlDeliberationCounts> mutate) in axes)
        {
            AddFuel($"fuel.{name}", static p => p.Fuel, static (p, value) => p with { Fuel = value }, mutate);
            AddFuel($"planned.{name}", static p => p.WindowPlannedFuel, static (p, value) => p with { WindowPlannedFuel = value }, mutate);
            AddFuel($"actual.{name}", static p => p.WindowActualFuel, static (p, value) => p with { WindowActualFuel = value }, mutate);
        }
        cases.Add(("refund-relationship", static p => p with { WindowActualFuel = p.WindowActualFuel with { CandidateEvaluations = p.WindowActualFuel.CandidateEvaluations + 2 } }));
        cases.Add(("evaluator-intervals", static p => p with { EvaluatorIntervals = p.EvaluatorIntervals + 1 }));
        cases.Add(("planned-evaluator-intervals", static p => p with { WindowPlannedEvaluatorIntervals = p.WindowPlannedEvaluatorIntervals + 1 }));
        cases.Add(("window-evaluator-intervals", static p => p with { WindowEvaluatorIntervals = p.WindowEvaluatorIntervals + 1 }));

        int rejected = 0;
        foreach ((_, Func<EmlAnytimeCurvePoint, EmlAnytimeCurvePoint> mutate) in cases)
            if (!mutate(point).VerifyDigest()) rejected++;
        bool passed = rejected == cases.Count;
        receipt = $"cases={cases.Count};rejected={rejected};verdict={(passed ? "pass" : "fail")}";
        return passed;
    }

    private static void Append(EmlAnytimeCurve curve, int step, int window, EmlAnytimeCommitments commitments, bool dominated = false, string dominator = "", int graceUntil = 0, bool activeFork = false)
    {
        EmlAnytimeBoundaryReceipt receipt = Make("fixture", step, window, commitments, false, true, dominator, curve.Digest, graceUntil, activeFork);
        EmlAnytimeCurvePoint point = curve.Append(in receipt, dominated);
        _ = curve.EvaluateKill(in receipt, in point);
    }

    private static void AppendEvaluation(EmlAnytimeCurve curve, int step, int window, EmlAnytimeCommitments commitments,
        bool activeFork = false, bool dominated = false)
    {
        string dominator = dominated && curve.Points.Count > 0 ? curve.Points[^1].PointID : "";
        EmlAnytimeBoundaryReceipt receipt = Make("evaluation", step, window, commitments, false, true, dominator,
            curve.Digest, activeFork: activeFork);
        EmlAnytimeCurvePoint point = curve.Append(in receipt, dominated);
        _ = curve.EvaluateKill(in receipt, in point);
    }

    private static void AppendEvaluationHandshake(EmlAnytimeCurve curve)
    {
        EmlAnytimeBoundaryReceipt receipt = new(
            "fixture-run", "fixture-config", "fixture-chain", "evaluation", "", 0, 0, 0,
            "evaluation.cold.handshake", EmlAnytimeCommitments.Zero, EmlDeliberationCounts.Zero,
            EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, 0, 0, 0, false, true,
            false, false, false, false, false, false, 0, "", "handshake-evidence",
            double.NaN, double.NaN, double.NaN, 0);
        _ = curve.Append(in receipt);
    }

    private static void AppendEvaluationScored(EmlAnytimeCurve curve, int step, EmlAnytimeCommitments commitments)
    {
        EmlDeliberationCounts planned = new(1, 2, 1, 0, 0, 0, 0, 1, 0);
        int priorStep = curve.Points.Count == 0 ? 0 : curve.Points[^1].PrefixStep;
        int windowSpan = step - priorStep;
        if (windowSpan <= 0) throw new InvalidDataException("evaluation fixture prefix must advance");
        EmlDeliberationCounts windowFuel = Scale(planned, windowSpan);
        EmlAnytimeBoundaryReceipt receipt = new(
            "fixture-run", "fixture-config", "fixture-chain", "evaluation", curve.Digest, 0, step, step,
            "ruler.window.commit", commitments, Scale(planned, step), windowFuel, windowFuel, step, windowSpan, windowSpan, true, true,
            false, false, false, false, true, false, 0, "", "evidence-" + step,
            .9, 9, -.2, step * 3);
        _ = curve.Append(in receipt);
    }

    private static void AppendArm(EmlAnytimeCurve curve, string arm, int step, int window, EmlAnytimeCommitments commitments, string parent = "", int rung = 0)
    {
        EmlAnytimeBoundaryReceipt receipt = Make(arm, step, window, commitments, false, true, "", parent.Length > 0 ? parent : curve.Digest, rung: rung);
        EmlAnytimeCurvePoint point = curve.Append(in receipt);
        _ = curve.EvaluateKill(in receipt, in point);
    }

    private static EmlAnytimeBoundaryReceipt Make(string arm, int step, int window, EmlAnytimeCommitments commitments, bool overrun, bool evidence, string dominator = "", string parent = "", int graceUntil = 0, bool activeFork = false, int rung = 0)
    {
        EmlDeliberationCounts planned = new(1, 2, 1, 0, 0, 0, 0, 1, 0);
        EmlDeliberationCounts actual = overrun ? planned with { CandidateEvaluations = 2 } : planned;
        EmlDeliberationCounts cumulative = overrun ? actual : Scale(actual, window + 1);
        long evaluator = overrun ? 1 : window + 1;
        return new("fixture-run", "fixture-config", "fixture-chain", arm, parent, rung, step, window, "ruler.window.commit", commitments, cumulative, planned, actual,
            evaluator, 1, 1, true, evidence, false, activeFork, false, false, true, false, graceUntil, dominator, "evidence-" + step,
            step % 2 == 0 ? .9 : .1, step % 2 == 0 ? 9 : -3, step % 2 == 0 ? -.2 : -.9, step * 3);
    }

    private static EmlDeliberationCounts Scale(EmlDeliberationCounts c, long n)
        => new(c.CandidateEvaluations * n, c.LogicalProgramPoints * n, c.ExecutedProgramPoints * n,
            c.InverseTransforms * n, c.HashProbes * n, c.JoinAttempts * n, c.JoinHits * n, c.ProcessTerms * n,
            c.VerifierProgramPoints * n, c.CandidateSupplyItems * n, c.LawRewriteApplications * n, c.LawRewriteTreeNodes * n);
}

/// Materializes the doctrine view from curve records. The source authority remains the typed point chain; this
/// artifact deliberately names commitment, evaluator, typed-fuel, and wall panels separately so wall cannot pose as
/// a budget axis. Its digest is the plot/checkpoint seam receipt.
internal static class EmlAnytimeCurvePlot
{
    internal static string Write(EmlAnytimeCurve curve, Run run)
    {
        StringBuilder tsv = new("prefix_step\twindow\tevaluator_intervals\tfuel_candidate_evaluations\tfuel_logical_program_points\tfuel_executed_program_points\tfrontier_total\texact\ttheorem\tcertificates\tclosed_obligations\theldout_captures\theldout_bestk\tverified_laws\tverified_proofs\tdominator_marker\tdominated\tgrace_until_window\tkill_marker\twall_ms\tscored\n");
        foreach (EmlAnytimeCurvePoint point in curve.Points)
        {
            bool kill = curve.Kills.Any(k => k.PointID == point.PointID);
            tsv.Append(point.PrefixStep).Append('\t').Append(point.WindowIndex).Append('\t').Append(point.EvaluatorIntervals).Append('\t')
                .Append(point.Fuel.CandidateEvaluations).Append('\t').Append(point.Fuel.LogicalProgramPoints).Append('\t').Append(point.Fuel.ExecutedProgramPoints).Append('\t').Append(point.Quality.Total).Append('\t')
                .Append(point.Quality.ExactClasses).Append('\t').Append(point.Quality.TheoremClasses).Append('\t').Append(point.Quality.CertificateClasses).Append('\t')
                .Append(point.Quality.ClosedObligations).Append('\t').Append(point.Quality.HeldOutCaptures).Append('\t').Append(point.Quality.HeldOutBestK).Append('\t')
                .Append(point.Quality.VerifiedLaws).Append('\t').Append(point.Quality.VerifiedProofs).Append('\t').Append(point.DominatorPointID.Length > 0 ? 1 : 0).Append('\t').Append(point.Dominated ? 1 : 0).Append('\t')
                .Append(point.GraceUntilWindow).Append('\t').Append(kill ? 1 : 0).Append('\t').Append(point.WallMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
                .Append('\t').Append(point.IsHandshake ? 0 : 1).Append('\n');
        }
        string rendered = tsv.ToString();
        run.WriteCurve("anytime_quality.tsv", rendered);
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rendered)));
        run.Write("anytime_quality.digest", digest + "\n");
        return digest;
    }

    internal static bool Verify(Run run)
    {
        string path = run.PathOf("anytime_quality.tsv");
        string digestPath = run.PathOf("anytime_quality.digest");
        if (!File.Exists(path) || !File.Exists(digestPath)) return false;
        string text = File.ReadAllText(path);
        string expected = File.ReadAllText(digestPath).Trim();
        return string.Equals(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), StringComparison.Ordinal);
    }
}
