namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// The denominator status is part of the result. A run with no certified captures is
/// unpowered; it is not an infinite floating-point rate and it is not a passing zero.
public enum EmlAnytimeEfficiencyPower : byte
{
    Powered,
    ZeroCertifiedCaptures,
}

public readonly record struct EmlAnytimeEfficiency(
    long EvaluatorCalls,
    long CertifiedCaptures,
    EmlAnytimeEfficiencyPower Power,
    double? CallsPerCertifiedCapture)
{
    public bool IsPowered => Power == EmlAnytimeEfficiencyPower.Powered;

    public static EmlAnytimeEfficiency Create(long evaluatorCalls, long certifiedCaptures)
    {
        if (evaluatorCalls < 0) throw new InvalidDataException("EML evaluator calls cannot be negative");
        if (certifiedCaptures < 0) throw new InvalidDataException("EML certified captures cannot be negative");
        if (certifiedCaptures == 0)
            return new(evaluatorCalls, 0, EmlAnytimeEfficiencyPower.ZeroCertifiedCaptures, null);
        double rate = (double)evaluatorCalls / certifiedCaptures;
        if (!double.IsFinite(rate)) throw new InvalidDataException("EML evaluator efficiency is nonfinite");
        return new(evaluatorCalls, certifiedCaptures, EmlAnytimeEfficiencyPower.Powered, rate);
    }
}

/// A deterministic, read-only view of one ordinary (non-handshake) EML curve artifact.
public sealed class EmlStandardAnytimeCurveSummary
{
    internal EmlStandardAnytimeCurveSummary(
        IReadOnlyList<EmlAnytimeCurvePoint> points,
        EmlAnytimeCurvePoint? acceptedKnee,
        EmlAnytimeCurvePoint? exactClassKnee,
        EmlDeliberationCounts plannedFuel,
        EmlDeliberationCounts actualFuel,
        EmlDeliberationCounts refundFuel,
        EmlAnytimeEfficiency efficiency,
        string digest)
    {
        Points = points;
        AcceptedKnee = acceptedKnee;
        ExactClassKnee = exactClassKnee;
        PlannedFuel = plannedFuel;
        ActualFuel = actualFuel;
        RefundFuel = refundFuel;
        Efficiency = efficiency;
        Digest = digest;
        if (points.Count > 0)
        {
            EmlAnytimeCurvePoint terminal = points[^1];
            RunID = terminal.RunID;
            ConfigID = terminal.ConfigID;
            ChainID = terminal.ChainID;
            ArmID = terminal.ArmID;
            Rung = terminal.Rung;
            Terminal = terminal;
        }
    }

    public IReadOnlyList<EmlAnytimeCurvePoint> Points { get; }
    public EmlAnytimeCurvePoint Terminal { get; }
    public bool HasTerminal => Points.Count > 0;
    public EmlAnytimeCurvePoint? AcceptedKnee { get; }
    public EmlAnytimeCurvePoint? ExactClassKnee { get; }
    public EmlDeliberationCounts PlannedFuel { get; }
    public EmlDeliberationCounts ActualFuel { get; }
    public EmlDeliberationCounts RefundFuel { get; }
    public EmlAnytimeEfficiency Efficiency { get; }
    public string Digest { get; }
    public string RunID { get; } = "";
    public string ConfigID { get; } = "";
    public string ChainID { get; } = "";
    public string ArmID { get; } = "";
    public int Rung { get; }

    public EmlDeliberationCounts AcceptedPlannedFuel
        => AcceptedKnee is EmlAnytimeCurvePoint point
            ? SumAcceptedWindowFuel(point, static p => p.WindowPlannedFuel)
            : EmlDeliberationCounts.Zero;

    public EmlDeliberationCounts AcceptedActualFuel
        => AcceptedKnee is EmlAnytimeCurvePoint point
            ? SumAcceptedWindowFuel(point, static p => p.WindowActualFuel)
            : EmlDeliberationCounts.Zero;

    internal bool TryGetAcceptedFuel(int horizon, out EmlAnytimeCurvePoint point,
        out EmlDeliberationCounts planned, out EmlDeliberationCounts actual)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            EmlAnytimeCurvePoint candidate = Points[i];
            if (candidate.PrefixStep != horizon || !IsAccepted(candidate)) continue;
            point = candidate;
            planned = SumAcceptedWindowFuel(candidate, static p => p.WindowPlannedFuel);
            actual = SumAcceptedWindowFuel(candidate, static p => p.WindowActualFuel);
            return true;
        }
        point = default;
        planned = EmlDeliberationCounts.Zero;
        actual = EmlDeliberationCounts.Zero;
        return false;
    }

    private EmlDeliberationCounts SumAcceptedWindowFuel(
        EmlAnytimeCurvePoint accepted,
        Func<EmlAnytimeCurvePoint, EmlDeliberationCounts> select)
    {
        EmlDeliberationCounts total = EmlDeliberationCounts.Zero;
        for (int i = 0; i < Points.Count; i++)
        {
            EmlAnytimeCurvePoint point = Points[i];
            EmlDeliberationCounts selected = select(point);
            total = EmlDeliberationCounts.Add(in total, in selected);
            if (point == accepted) break;
        }
        return total;
    }

    private static bool IsAccepted(in EmlAnytimeCurvePoint point)
        => point.EvidenceVerified && point.WindowComplete && point.KillEligible && !point.Dominated
        && !point.ActiveFunding && !point.ActiveFork && !point.ActiveObligation && !point.PendingResolution && point.WindowSettled;

}

public readonly record struct EmlAnytimePairedComparison(
    bool Comparable,
    bool PlannedFuelMatched,
    EmlDeliberationCounts LivePlannedFuel,
    EmlDeliberationCounts ControlPlannedFuel,
    EmlAnytimeCurvePoint? LiveKnee,
    EmlAnytimeCurvePoint? ControlKnee,
    bool LiveExactExceedsControl,
    bool LiveCrossesExactClassWall,
    bool EfficiencyPowered,
    bool EfficiencyNoWorse,
    int CommonHorizon,
    string Failure)
{
    public bool VocabularyPass => Comparable && LiveExactExceedsControl && LiveCrossesExactClassWall;
    public bool EfficiencyPass => Comparable && EfficiencyPowered && EfficiencyNoWorse;
}

/// Reads ordinary standard-run curves. It consumes bytes only; all writes belong to the run or fixture.
public static class EmlStandardAnytimeCurveReader
{
    private static readonly string[] RequiredColumns =
    [
        "point_id", "previous_digest", "digest", "run_id", "config_id", "chain_id", "arm_id", "parent_point_id",
        "rung", "prefix_step", "window", "boundary", "exact", "theorem", "certificates", "closed_obligations",
        "heldout_captures", "heldout_bestk", "verified_laws", "verified_proofs",
        "fuel_candidate_evaluations", "fuel_logical_program_points", "fuel_executed_program_points", "fuel_inverse_transforms",
        "fuel_hash_probes", "fuel_join_attempts", "fuel_join_hits", "fuel_process_terms", "fuel_verifier_program_points",
        "fuel_candidate_supply_items", "fuel_law_rewrite_applications", "fuel_law_rewrite_tree_nodes",
        "planned_candidate_evaluations", "planned_logical_program_points", "planned_executed_program_points", "planned_inverse_transforms",
        "planned_hash_probes", "planned_join_attempts", "planned_join_hits", "planned_process_terms", "planned_verifier_program_points",
        "planned_candidate_supply_items", "planned_law_rewrite_applications", "planned_law_rewrite_tree_nodes",
        "actual_candidate_evaluations", "actual_logical_program_points", "actual_executed_program_points", "actual_inverse_transforms",
        "actual_hash_probes", "actual_join_attempts", "actual_join_hits", "actual_process_terms", "actual_verifier_program_points",
        "actual_candidate_supply_items", "actual_law_rewrite_applications", "actual_law_rewrite_tree_nodes",
        "evaluator_intervals", "window_planned_evaluator_intervals", "window_evaluator_intervals", "window_complete", "evidence_verified",
        "kill_eligible", "dominated", "active_funding", "active_fork", "active_obligation", "pending_resolution", "window_settled",
        "run_terminal", "grace_until_window", "dominator_point_id", "residual", "rate", "meanz", "wall_ms", "evidence_digest",
    ];

    public static EmlStandardAnytimeCurveSummary Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("standard EML anytime curve is missing", path);
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 1) throw new InvalidDataException("standard EML anytime curve is empty");
        Dictionary<string, int> columns = BuildColumns(lines[0].TrimStart('\uFEFF').Split('\t'));
        if (columns.Count != RequiredColumns.Length)
            throw new InvalidDataException("standard EML anytime curve schema has unexpected columns");
        for (int i = 0; i < RequiredColumns.Length; i++)
            if (!columns.ContainsKey(RequiredColumns[i])) throw new InvalidDataException($"standard EML anytime curve omits {RequiredColumns[i]}");

        if (lines.Length == 1)
        {
            string emptyDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines[0] + "\n")));
            return new EmlStandardAnytimeCurveSummary(
                Array.Empty<EmlAnytimeCurvePoint>(), null, null,
                EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero,
                EmlAnytimeEfficiency.Create(0, 0), emptyDigest);
        }

        List<EmlAnytimeCurvePoint> points = new(lines.Length - 1);
        EmlAnytimeCurvePoint previous = default;
        EmlAnytimeCommitments previousQuality = EmlAnytimeCommitments.Zero;
        EmlDeliberationCounts cumulativePlanned = EmlDeliberationCounts.Zero;
        EmlDeliberationCounts cumulativeActual = EmlDeliberationCounts.Zero;
        long cumulativeEvaluatorCalls = 0;
        string runID = "", configID = "", chainID = "", armID = "";
        int rung = -1;
        EmlAnytimeCurvePoint? acceptedKnee = null;
        EmlAnytimeCurvePoint? exactClassKnee = null;

        for (int rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[rowIndex])) throw new InvalidDataException($"standard EML anytime curve has blank row {rowIndex + 1}");
            string[] row = lines[rowIndex].Split('\t');
            if (row.Length != columns.Count) throw new InvalidDataException($"standard EML anytime curve row {rowIndex + 1} has the wrong width");
            EmlAnytimeCurvePoint point = ParsePoint(row, columns, rowIndex + 1);
            ValidatePoint(in point, rowIndex, rowIndex == lines.Length - 1, points.Count == 0 ? null : previous, in previousQuality, in cumulativeActual, cumulativeEvaluatorCalls);
            if (points.Count == 0)
            {
                runID = point.RunID; configID = point.ConfigID; chainID = point.ChainID; armID = point.ArmID; rung = point.Rung;
            }
            else if (point.RunID != runID || point.ConfigID != configID || point.ChainID != chainID || point.ArmID != armID || point.Rung != rung)
                throw new InvalidDataException($"standard EML anytime curve scope changed at row {rowIndex + 1}");

            EmlDeliberationCounts plannedWindow = point.WindowPlannedFuel;
            EmlDeliberationCounts actualWindow = point.WindowActualFuel;
            cumulativePlanned = EmlDeliberationCounts.Add(in cumulativePlanned, in plannedWindow);
            cumulativeActual = EmlDeliberationCounts.Add(in cumulativeActual, in actualWindow);
            cumulativeEvaluatorCalls = checked(cumulativeEvaluatorCalls + point.WindowEvaluatorIntervals);
            points.Add(point);
            if (acceptedKnee is null && IsAccepted(in point)) acceptedKnee = point;
            if (exactClassKnee is null && IsAccepted(in point) && point.Quality.ExactClasses > 63) exactClassKnee = point;
            previous = point;
            previousQuality = point.Quality;
        }

        EmlDeliberationCounts refund = EmlDeliberationCounts.Subtract(in cumulativePlanned, in cumulativeActual);
        refund.ValidateNonnegative("standard anytime refund");
        EmlAnytimeEfficiency efficiency = acceptedKnee is EmlAnytimeCurvePoint knee
            ? EmlAnytimeEfficiency.Create(knee.EvaluatorIntervals, knee.Quality.CertificateClasses)
            : EmlAnytimeEfficiency.Create(0, 0);
        string digest = points.Count == 0
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines[0] + "\n")))
            : points[^1].Digest;
        return new EmlStandardAnytimeCurveSummary(points.AsReadOnly(), acceptedKnee, exactClassKnee,
            cumulativePlanned, cumulativeActual, refund, efficiency, digest);
    }

    public static EmlAnytimePairedComparison Compare(
        EmlStandardAnytimeCurveSummary live,
        EmlStandardAnytimeCurveSummary control,
        in EmlDeliberationQuota liveBudget,
        in EmlDeliberationQuota controlBudget,
        int commonHorizon)
    {
        liveBudget.Validate();
        controlBudget.Validate();
        EmlDeliberationCounts liveFuel = EmlDeliberationCounts.Zero;
        EmlDeliberationCounts controlFuel = EmlDeliberationCounts.Zero;
        if (!live.HasTerminal || !control.HasTerminal)
            return new(false, false, liveFuel, controlFuel,
                live.HasTerminal ? live.Terminal : null, control.HasTerminal ? control.Terminal : null,
                false, false, false, false, commonHorizon, "no scored EML opportunity");
        if (commonHorizon <= 0 || !live.TryGetAcceptedFuel(commonHorizon, out EmlAnytimeCurvePoint liveKnee, out liveFuel, out _)
            || !control.TryGetAcceptedFuel(commonHorizon, out EmlAnytimeCurvePoint controlKnee, out controlFuel, out _))
            return new(false, false, liveFuel, controlFuel, null, null,
                false, false, false, false, commonHorizon, "matched planned horizon did not close an accepted settled point");
        bool fuelMatched = liveBudget == controlBudget && liveFuel == controlFuel;
        if (!fuelMatched)
            return new(false, false, liveFuel, controlFuel, liveKnee, controlKnee, false, false, false, false, commonHorizon,
                liveBudget == controlBudget ? "paired curves consumed different cumulative planned fuel at the common horizon" : "arm-neutral deliberation budget profiles differ");
        bool exactExceeds = liveKnee.Quality.ExactClasses > controlKnee.Quality.ExactClasses;
        bool crossesWall = liveKnee.Quality.ExactClasses > 63;
        EmlAnytimeEfficiency liveEfficiency = EmlAnytimeEfficiency.Create(liveKnee.EvaluatorIntervals, liveKnee.Quality.CertificateClasses);
        EmlAnytimeEfficiency controlEfficiency = EmlAnytimeEfficiency.Create(controlKnee.EvaluatorIntervals, controlKnee.Quality.CertificateClasses);
        bool efficiencyPowered = liveEfficiency.IsPowered && controlEfficiency.IsPowered;
        bool efficiencyNoWorse = efficiencyPowered
            && liveEfficiency.CallsPerCertifiedCapture <= controlEfficiency.CallsPerCertifiedCapture;
        return new(true, true, liveFuel, controlFuel, liveKnee, controlKnee, exactExceeds, crossesWall, efficiencyPowered, efficiencyNoWorse, commonHorizon, "");
    }

    /// Compares a registered paired curve in the arm-neutral schedule currency.
    /// The ordinary reader deliberately retains its historical lease-total compare
    /// for standalone assays; paired gates must bind every accepted prefix to the
    /// persisted schedule, so opportunity count can never change the denominator.
    internal static EmlAnytimePairedComparison ComparePairedSchedule(
        EmlStandardAnytimeCurveSummary live,
        EmlStandardAnytimeCurveSummary control,
        in EmlPairedFuelSchedule liveSchedule,
        in EmlPairedFuelSchedule controlSchedule,
        in EmlPairedFuelScheduleCursor liveCursor,
        in EmlPairedFuelScheduleCursor controlCursor)
    {
        EmlDeliberationCounts zero = EmlDeliberationCounts.Zero;
        if (liveSchedule != controlSchedule)
            return new(false, false, zero, zero, null, null, false, false, false, false, 0, "paired schedule identity, digest, horizon, or totals differ");
        try
        {
            liveSchedule.Validate();
            controlSchedule.Validate();
            liveCursor.Validate(in liveSchedule);
            controlCursor.Validate(in controlSchedule);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        {
            return new(false, false, zero, zero, null, null, false, false, false, false, 0, $"paired schedule cursor is invalid: {error.Message}");
        }

        if (liveCursor.Horizon != liveSchedule.Horizon || controlCursor.Horizon != controlSchedule.Horizon
            || liveCursor.RowCount != liveSchedule.Horizon || controlCursor.RowCount != controlSchedule.Horizon
            || liveCursor.RowCount != controlCursor.RowCount)
            return new(false, false, zero, zero, null, null, false, false, false, false, 0,
                "paired schedule horizon is not terminal and equal across both arms");

        int commonHorizon = liveSchedule.Horizon;
        // Structural custody has already closed above: both decoded journals reach the
        // same terminal checkpoint horizon. No accepted curve point here is therefore
        // an observed absence of certified capture, not malformed schedule evidence;
        // the paired gate deliberately banks that scientific null.
        if (commonHorizon <= 0
            || !live.TryGetAcceptedFuel(commonHorizon, out EmlAnytimeCurvePoint liveKnee, out EmlDeliberationCounts liveLeasePlanned, out _)
            || !control.TryGetAcceptedFuel(commonHorizon, out EmlAnytimeCurvePoint controlKnee, out EmlDeliberationCounts controlLeasePlanned, out _))
            return new(false, false, zero, zero, null, null, false, false, false, false, commonHorizon,
                "matched schedule prefix did not close an accepted settled point");

        EmlDeliberationCounts scheduledPrefix = liveSchedule.Prefix(commonHorizon);
        if (liveLeasePlanned != scheduledPrefix || controlLeasePlanned != scheduledPrefix
            || liveCursor.Planned != liveSchedule.Prefix(liveCursor.RowCount)
            || controlCursor.Planned != controlSchedule.Prefix(controlCursor.RowCount))
            return new(false, false, liveLeasePlanned, controlLeasePlanned, liveKnee, controlKnee, false, false, false, false,
                commonHorizon, "accepted curve prefix does not equal the persisted paired schedule");

        bool exactExceeds = liveKnee.Quality.ExactClasses > controlKnee.Quality.ExactClasses;
        bool crossesWall = liveKnee.Quality.ExactClasses > 63;
        EmlAnytimeEfficiency liveEfficiency = EmlAnytimeEfficiency.Create(liveKnee.EvaluatorIntervals, liveKnee.Quality.CertificateClasses);
        EmlAnytimeEfficiency controlEfficiency = EmlAnytimeEfficiency.Create(controlKnee.EvaluatorIntervals, controlKnee.Quality.CertificateClasses);
        bool efficiencyPowered = liveEfficiency.IsPowered && controlEfficiency.IsPowered;
        bool efficiencyNoWorse = efficiencyPowered
            && liveEfficiency.CallsPerCertifiedCapture <= controlEfficiency.CallsPerCertifiedCapture;
        return new(true, true, scheduledPrefix, scheduledPrefix, liveKnee, controlKnee, exactExceeds, crossesWall,
            efficiencyPowered, efficiencyNoWorse, commonHorizon, "");
    }

    private static bool IsAccepted(in EmlAnytimeCurvePoint point)
        => point.EvidenceVerified && point.WindowComplete && point.KillEligible && !point.Dominated
        && !point.ActiveFunding && !point.ActiveFork && !point.ActiveObligation && !point.PendingResolution && point.WindowSettled;

    private static Dictionary<string, int> BuildColumns(string[] header)
    {
        Dictionary<string, int> columns = new(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
            if (!columns.TryAdd(header[i], i)) throw new InvalidDataException($"standard EML anytime curve duplicates column {header[i]}");
        return columns;
    }

    private static void ValidatePoint(in EmlAnytimeCurvePoint point, int rowIndex, bool isLastRow, EmlAnytimeCurvePoint? prior,
        in EmlAnytimeCommitments priorQuality, in EmlDeliberationCounts cumulativeActual, long cumulativeEvaluatorCalls)
    {
        if (string.IsNullOrWhiteSpace(point.RunID) || string.IsNullOrWhiteSpace(point.ConfigID) || string.IsNullOrWhiteSpace(point.ChainID)
            || string.IsNullOrWhiteSpace(point.ArmID) || string.IsNullOrWhiteSpace(point.Boundary)
            || string.IsNullOrWhiteSpace(point.PointID) || string.IsNullOrWhiteSpace(point.Digest)
            || string.IsNullOrWhiteSpace(point.EvidenceDigest) || point.EvidenceDigest.Contains('\t') || point.EvidenceDigest.Contains('\n'))
            throw new InvalidDataException($"standard EML anytime point identity/evidence is blank at row {rowIndex + 1}");
        if (!point.VerifyDigest()) throw new InvalidDataException($"standard EML anytime point digest mismatch at row {rowIndex + 1}");
        if (point.Rung < 0 || point.PrefixStep < 0 || point.WindowIndex < 0 || point.GraceUntilWindow < 0)
            throw new InvalidDataException($"standard EML anytime point coordinates are negative at row {rowIndex + 1}");
        if (point.IsHandshake) throw new InvalidDataException("standard EML anytime reader rejects evaluation handshake rows");
        point.Quality.Validate($"standard anytime row {rowIndex + 1}");
        point.Fuel.ValidateNonnegative("standard anytime cumulative fuel");
        point.WindowPlannedFuel.ValidateNonnegative("standard anytime planned fuel");
        point.WindowActualFuel.ValidateNonnegative("standard anytime actual fuel");
        EmlDeliberationCounts plannedWindow = point.WindowPlannedFuel;
        EmlDeliberationCounts actualWindow = point.WindowActualFuel;
        EmlDeliberationCounts refund = EmlDeliberationCounts.Subtract(in plannedWindow, in actualWindow);
        refund.ValidateNonnegative($"standard anytime window refund row {rowIndex + 1}");
        if (point.EvaluatorIntervals < 0 || point.WindowPlannedEvaluatorIntervals < 0 || point.WindowEvaluatorIntervals < 0
            || point.WindowEvaluatorIntervals > point.WindowPlannedEvaluatorIntervals)
            throw new InvalidDataException($"standard EML anytime evaluator accounting failed at row {rowIndex + 1}");
        if (!double.IsFinite(point.Residual) && !double.IsNaN(point.Residual)
            || !double.IsFinite(point.Rate) && !double.IsNaN(point.Rate)
            || !double.IsFinite(point.Meanz) && !double.IsNaN(point.Meanz)
            || !double.IsFinite(point.WallMilliseconds) || point.WallMilliseconds < 0)
            throw new InvalidDataException($"standard EML anytime diagnostics are invalid at row {rowIndex + 1}");
        bool killEligible = point.WindowComplete && !point.ActiveFunding && !point.ActiveFork && !point.ActiveObligation
            && !point.PendingResolution && point.WindowSettled;
        if (point.KillEligible != killEligible)
            throw new InvalidDataException($"standard EML anytime kill eligibility disagrees at row {rowIndex + 1}");
        if (prior is EmlAnytimeCurvePoint previous)
        {
            if (point.PreviousDigest != previous.Digest || point.ParentPointID != previous.Digest)
                throw new InvalidDataException($"standard EML anytime digest chain broke at row {rowIndex + 1}");
            bool terminal = point.RunTerminal;
            if (terminal)
            {
                if (!isLastRow || point.Boundary != "ruler.window.terminal"
                    || previous.RunTerminal || point.PrefixStep != previous.PrefixStep
                    || point.WindowIndex != previous.WindowIndex || point.WindowPlannedFuel != EmlDeliberationCounts.Zero
                    || point.WindowActualFuel != EmlDeliberationCounts.Zero || point.WindowEvaluatorIntervals != 0
                    || point.WindowPlannedEvaluatorIntervals != 0 || point.PendingResolution || !point.WindowSettled
                    || !point.WindowComplete || !point.KillEligible)
                    throw new InvalidDataException("standard EML anytime terminal point is not a zero-spend final settlement");
            }
            else if (point.PrefixStep <= previous.PrefixStep || point.WindowIndex <= previous.WindowIndex)
                throw new InvalidDataException($"standard EML anytime prefix/window regressed at row {rowIndex + 1}");
            if (!point.Quality.Dominates(priorQuality))
                throw new InvalidDataException($"standard EML anytime commitments regressed at row {rowIndex + 1}");
            EmlDeliberationCounts pointFuel = point.Fuel;
            EmlDeliberationCounts delta = EmlDeliberationCounts.Subtract(in pointFuel, in cumulativeActual);
            delta.ValidateNonnegative($"standard anytime cumulative fuel delta row {rowIndex + 1}");
            if (delta != point.WindowActualFuel)
                throw new InvalidDataException($"standard EML anytime cumulative fuel disagrees at row {rowIndex + 1}");
            if (point.EvaluatorIntervals - cumulativeEvaluatorCalls != point.WindowEvaluatorIntervals)
                throw new InvalidDataException($"standard EML anytime evaluator cumulative disagrees at row {rowIndex + 1}");
        }
        else
        {
            if (point.PreviousDigest.Length != 0 || point.ParentPointID.Length != 0)
                throw new InvalidDataException("standard EML anytime first point cannot bind a prior curve");
            if (point.Fuel != point.WindowActualFuel || point.EvaluatorIntervals != point.WindowEvaluatorIntervals)
                throw new InvalidDataException("standard EML anytime first point cumulative accounting is not its window accounting");
        }
        if (!point.EvidenceVerified) throw new InvalidDataException($"standard EML anytime evidence is not verified at row {rowIndex + 1}");
    }

    private static EmlAnytimeCurvePoint ParsePoint(string[] row, Dictionary<string, int> columns, int line)
    {
        string Text(string name) => row[columns[name]];
        long Long(string name) => long.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0
            ? value : throw new InvalidDataException($"standard EML anytime row {line} has invalid {name}");
        int Int(string name) => int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException($"standard EML anytime row {line} has invalid {name}");
        bool Bool(string name) => Text(name) switch { "0" => false, "1" => true, _ => throw new InvalidDataException($"standard EML anytime row {line} has invalid {name}") };
        double Diagnostic(string name) => double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && !double.IsInfinity(value) ? value : throw new InvalidDataException($"standard EML anytime row {line} has invalid {name}");
        double Wall(string name) => double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) && value >= 0 ? value : throw new InvalidDataException($"standard EML anytime row {line} has invalid {name}");
        EmlAnytimeCommitments quality = new(Long("exact"), Long("theorem"), Long("certificates"), Long("closed_obligations"), Long("heldout_captures"), Long("heldout_bestk"), Long("verified_laws"), Long("verified_proofs"));
        EmlDeliberationCounts Fuel(string prefix) => new(Long(prefix + "candidate_evaluations"), Long(prefix + "logical_program_points"), Long(prefix + "executed_program_points"), Long(prefix + "inverse_transforms"), Long(prefix + "hash_probes"), Long(prefix + "join_attempts"), Long(prefix + "join_hits"), Long(prefix + "process_terms"), Long(prefix + "verifier_program_points"), Long(prefix + "candidate_supply_items"), Long(prefix + "law_rewrite_applications"), Long(prefix + "law_rewrite_tree_nodes"));
        return new(Text("point_id"), Text("previous_digest"), Text("digest"), Text("run_id"), Text("config_id"), Text("chain_id"), Text("arm_id"), Text("parent_point_id"), Int("rung"), Int("prefix_step"), Int("window"), Text("boundary"), quality, Fuel("fuel_"), Fuel("planned_"), Fuel("actual_"), Long("evaluator_intervals"), Long("window_planned_evaluator_intervals"), Long("window_evaluator_intervals"), Bool("window_complete"), Bool("evidence_verified"), Bool("kill_eligible"), Bool("dominated"), Bool("active_funding"), Bool("active_fork"), Bool("active_obligation"), Bool("pending_resolution"), Bool("window_settled"), Bool("run_terminal"), Int("grace_until_window"), Text("dominator_point_id"), Diagnostic("residual"), Diagnostic("rate"), Diagnostic("meanz"), Wall("wall_ms"), Text("evidence_digest"));
    }
}

/// Cheap fixture battery for the standard reader. It writes only its private temporary inputs and
/// removes them before returning; the reader itself has no write path.
internal static class EmlStandardAnytimeCurveReaderFixture
{
    internal static bool Verify(TextWriter output)
    {
        string root = Path.Combine(".tmp", "eml-standard-anytime-curve-reader-fixture");
        Directory.CreateDirectory(root);
        try
        {
            string valid = Path.Combine(root, "valid.tsv");
            string corrupt = Path.Combine(root, "corrupt.tsv");
            string noKnee = Path.Combine(root, "no-knee.tsv");
            string zeroCertificate = Path.Combine(root, "zero-certificate.tsv");
            string frozen = Path.Combine(root, "frozen.tsv");
            string empty = Path.Combine(root, "empty.tsv");
            WriteFixture(valid, exact: 64, certificates: 1);
            WriteFixture(noKnee, exact: 63, certificates: 1);
            WriteFixture(zeroCertificate, exact: 64, certificates: 0);
            WriteFixture(frozen, exact: 63, certificates: 1, frozen: true);
            File.WriteAllText(empty, File.ReadLines(valid).First() + "\n");
            File.Copy(valid, corrupt, true);
            string before = File.ReadAllText(valid);
            byte[] bytes = File.ReadAllBytes(corrupt); bytes[^1] = (byte)(bytes[^1] == (byte)'\n' ? 'x' : bytes[^1] ^ 1); File.WriteAllBytes(corrupt, bytes);
            EmlStandardAnytimeCurveSummary parsed = EmlStandardAnytimeCurveReader.Read(valid);
            EmlStandardAnytimeCurveSummary noKneeSummary = EmlStandardAnytimeCurveReader.Read(noKnee);
            EmlStandardAnytimeCurveSummary zeroSummary = EmlStandardAnytimeCurveReader.Read(zeroCertificate);
            EmlStandardAnytimeCurveSummary frozenSummary = EmlStandardAnytimeCurveReader.Read(frozen);
            EmlStandardAnytimeCurveSummary emptySummary = EmlStandardAnytimeCurveReader.Read(empty);
            EmlDeliberationQuota budget = EmlDeliberationQuota.Default;
            EmlAnytimePairedComparison matched = EmlStandardAnytimeCurveReader.Compare(parsed, frozenSummary, in budget, in budget, commonHorizon: 1);
            EmlDeliberationQuota mismatchBudget = EmlDeliberationQuota.TightAssay;
            EmlAnytimePairedComparison mismatch = EmlStandardAnytimeCurveReader.Compare(parsed, frozenSummary, in budget, in mismatchBudget, commonHorizon: 1);
            EmlDeliberationCounts frozenPlanned = frozenSummary.PlannedFuel;
            bool refundExact = frozenSummary.RefundFuel == frozenPlanned && frozenSummary.ActualFuel == EmlDeliberationCounts.Zero;
            bool corruptRejected = Rejects(corrupt);
            bool noKneeTyped = noKneeSummary.ExactClassKnee is null && noKneeSummary.AcceptedKnee is not null;
            bool zeroTyped = zeroSummary.Efficiency.Power == EmlAnytimeEfficiencyPower.ZeroCertifiedCaptures
                && zeroSummary.Efficiency.CallsPerCertifiedCapture is null;
            bool noWrite = before == File.ReadAllText(valid);
            bool emptyTyped = !emptySummary.HasTerminal && emptySummary.Efficiency.Power == EmlAnytimeEfficiencyPower.ZeroCertifiedCaptures;
            bool pass = parsed.AcceptedKnee is not null && parsed.ExactClassKnee is not null && parsed.Efficiency.IsPowered
                && corruptRejected && noKneeTyped && zeroTyped && noWrite && emptyTyped
                && matched.Comparable && !mismatch.Comparable && refundExact;
            output.WriteLine($"  standard EML anytime reader fixture · valid={(parsed.AcceptedKnee is not null ? "accepted" : "BROKEN")} · corrupt={(corruptRejected ? "rejected" : "ACCEPTED")} · no-knee={(noKneeTyped ? "typed-null" : "BROKEN")} · zero-certificate={(zeroTyped ? "typed-null" : "BROKEN")} · empty={(emptyTyped ? "typed-unpowered" : "BROKEN")} · matched-plan={(matched.Comparable ? "comparable" : "REJECTED")} · mismatch={(mismatch.Comparable ? "ACCEPTED" : "rejected")} · frozen-refund={(refundExact ? "exact" : "BROKEN")} · no-write={(noWrite ? "exact" : "MUTATED")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void WriteFixture(string path, long exact, long certificates, bool frozen = false)
    {
        EmlAnytimeCurve curve = new();
        EmlDeliberationCounts fuel = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        EmlDeliberationCounts actual = frozen ? EmlDeliberationCounts.Zero : fuel;
        EmlAnytimeBoundaryReceipt receipt = new("fixture-run", "fixture-config", "fixture-chain", "fixture-arm", "", 0, 1, 1, "ruler.window.commit", new(exact, 0, certificates, 0, 0, 0, 0, 0), actual, fuel, actual, frozen ? 0 : 1, 1, frozen ? 0 : 1, true, true, false, false, false, false, true, false, 0, "", "fixture-evidence", double.NaN, double.NaN, double.NaN, 1);
        curve.Append(in receipt);
        curve.WriteTSV(path);
    }

    private static bool Rejects(string path)
    {
        try { _ = EmlStandardAnytimeCurveReader.Read(path); return false; }
        catch (InvalidDataException) { return true; }
    }
}
