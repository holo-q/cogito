namespace Cogito;

using System.Globalization;

/// The identity and lineage that an evaluation child is allowed to emit.
internal readonly record struct EmlAnytimeEvaluationScope(
    string RunID,
    string ConfigID,
    string ChainID,
    string ArmID,
    int Rung,
    string ParentDigest = "",
    int FirstStep = 1,
    int LastStep = 500,
    EmlDeepRematchFuelCursor? HandshakeCursor = null)
{
    public EmlAnytimeEvaluationScope Validate()
    {
        if (string.IsNullOrWhiteSpace(RunID) || string.IsNullOrWhiteSpace(ConfigID)
            || string.IsNullOrWhiteSpace(ChainID) || string.IsNullOrWhiteSpace(ArmID)
            || Rung < 0 || FirstStep <= 0 || LastStep < FirstStep)
            throw new InvalidDataException("evaluation anytime scope requires run, config, chain, arm, rung, and an ordered step window");
        HandshakeCursor?.Validate();
        return this;
    }
}

/// A local evaluation prefix.  Fuel and evaluator work are deliberately derived by summing the
/// evaluation records, never by reading a terminal global Cortex/Replay row.
internal readonly record struct EmlAnytimeEvaluationPrefix(
    bool Passed,
    bool Banked,
    string BankReason,
    EmlAnytimeCurvePoint AcceptedPoint,
    EmlDeliberationCounts PlannedFuel,
    EmlDeliberationCounts ActualFuel,
    EmlDeliberationCounts RefundFuel,
    long EvaluatorIntervals,
    long Certificates,
    double? EvaluatorPerCertificate)
{
    public int AcceptedStep => AcceptedPoint.PrefixStep;
    public string Digest => AcceptedPoint.Digest;
    public bool HasCertificateRate => EvaluatorPerCertificate.HasValue;
}

/// Reads and verifies an evaluation child's typed anytime TSV authority.
internal static class EmlAnytimeEvaluationReader
{
    private static readonly string[] RequiredColumns =
    [
        "point_id", "previous_digest", "digest", "run_id", "config_id", "chain_id", "arm_id", "parent_point_id",
        "rung", "prefix_step", "window", "boundary", "exact", "theorem", "certificates", "closed_obligations",
        "heldout_captures", "heldout_bestk", "verified_laws", "verified_proofs",
        "evaluator_intervals", "window_planned_evaluator_intervals", "window_evaluator_intervals",
        "window_complete", "evidence_verified", "kill_eligible", "dominated", "active_funding", "active_fork",
        "active_obligation", "pending_resolution", "window_settled", "run_terminal", "grace_until_window",
        "dominator_point_id", "residual", "rate", "meanz", "wall_ms", "evidence_digest",
    ];

    private static readonly string[] FuelPrefixes = ["fuel_", "planned_", "actual_"];

    public static EmlAnytimeEvaluationPrefix Read(string path, in EmlAnytimeEvaluationScope expected)
    {
        expected.Validate();
        if (!File.Exists(path)) throw new FileNotFoundException("evaluation anytime artifact is missing", path);
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException("evaluation anytime artifact has no point rows");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        Dictionary<string, int> columns = BuildColumns(header);
        for (int i = 0; i < RequiredColumns.Length; i++)
            if (!columns.ContainsKey(RequiredColumns[i]))
                throw new InvalidDataException($"evaluation anytime artifact omits {RequiredColumns[i]}");
        for (int prefixIndex = 0; prefixIndex < FuelPrefixes.Length; prefixIndex++)
            for (int axisIndex = 0; axisIndex < EmlDeliberationCounts.AxisNames.Length; axisIndex++)
            {
                string name = FuelPrefixes[prefixIndex] + EmlDeliberationCounts.AxisNames[axisIndex];
                if (!columns.ContainsKey(name)) throw new InvalidDataException($"evaluation anytime artifact omits {name}");
            }
        string previousDigest = "";
        EmlAnytimeCurvePoint previous = default;
        EmlAnytimeCommitments previousQuality = EmlAnytimeCommitments.Zero;
        EmlAnytimeCommitments handshakeQuality = EmlAnytimeCommitments.Zero;
        EmlDeliberationCounts planned = EmlDeliberationCounts.Zero;
        EmlDeliberationCounts actual = EmlDeliberationCounts.Zero;
        long evaluatorIntervals = 0;
        long certificates = 0;
        int acceptedIndex = -1;
        EmlAnytimeCurvePoint accepted = default;
        for (int rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[rowIndex]))
                throw new InvalidDataException($"evaluation anytime artifact contains a blank row {rowIndex + 1}");
            string[] row = lines[rowIndex].Split('\t');
            if (row.Length != header.Length)
                throw new InvalidDataException($"evaluation anytime artifact row {rowIndex + 1} has the wrong width");
            EmlAnytimeCurvePoint point = ParsePoint(row, columns, rowIndex + 1);
            VerifyPoint(in point, in expected, rowIndex, rowIndex == lines.Length - 1, previousDigest, in previous, in previousQuality);
            if (rowIndex == 1 && expected.HandshakeCursor is EmlDeepRematchFuelCursor cursor
                && (point.PointID != cursor.PointID || point.Digest != cursor.PointDigest))
                throw new InvalidDataException("evaluation anytime handshake point does not match the persisted fuel cursor");
            EmlDeliberationCounts rowPlanned = point.WindowPlannedFuel;
            EmlDeliberationCounts rowActual = point.WindowActualFuel;
            planned = EmlDeliberationCounts.Add(in planned, in rowPlanned);
            actual = EmlDeliberationCounts.Add(in actual, in rowActual);
            evaluatorIntervals = checked(evaluatorIntervals + point.WindowEvaluatorIntervals);
            long certificateDelta = checked(point.Quality.CertificateClasses - (rowIndex == 1 ? 0 : previousQuality.CertificateClasses));
            if (certificateDelta < 0)
                throw new InvalidDataException($"evaluation anytime artifact certificate prefix regressed at row {rowIndex + 1}");
            certificates = checked(certificates + certificateDelta);
            if (rowIndex == 1) handshakeQuality = point.Quality;
            long scoredExact = checked(point.Quality.ExactClasses - handshakeQuality.ExactClasses);
            if (rowIndex > 1 && acceptedIndex < 0 && IsAccepted(in point) && scoredExact > 63)
            {
                acceptedIndex = rowIndex;
                accepted = point;
            }
            previous = point;
            previousDigest = point.Digest;
            previousQuality = point.Quality;
        }
        if (previous.PrefixStep != expected.LastStep)
            throw new InvalidDataException($"evaluation anytime artifact ends at prefix step {previous.PrefixStep}, expected {expected.LastStep}");
        if (acceptedIndex < 0)
            throw new InvalidDataException("evaluation anytime artifact has no accepted verified prefix crossing exact classes >63");

        // Re-sum only through the accepted prefix.  Rows after the crossing are still validated above,
        // but they cannot influence the evaluation-local certificate rate or fuel receipt.
        planned = EmlDeliberationCounts.Zero;
        actual = EmlDeliberationCounts.Zero;
        evaluatorIntervals = 0;
        certificates = 0;
        previousQuality = handshakeQuality;
        for (int rowIndex = 2; rowIndex <= acceptedIndex; rowIndex++)
        {
            EmlAnytimeCurvePoint point = ParsePoint(lines[rowIndex].Split('\t'), columns, rowIndex + 1);
            EmlDeliberationCounts rowPlanned = point.WindowPlannedFuel;
            EmlDeliberationCounts rowActual = point.WindowActualFuel;
            planned = EmlDeliberationCounts.Add(in planned, in rowPlanned);
            actual = EmlDeliberationCounts.Add(in actual, in rowActual);
            evaluatorIntervals = checked(evaluatorIntervals + point.WindowEvaluatorIntervals);
            certificates = checked(certificates + point.Quality.CertificateClasses - previousQuality.CertificateClasses);
            previousQuality = point.Quality;
        }
        EmlDeliberationCounts refund = EmlDeliberationCounts.Subtract(in planned, in actual);
        refund.ValidateNonnegative("evaluation refund");
        if (certificates == 0)
            return new(false, true, "evaluation prefix has zero certificates", accepted, planned, actual, refund,
                evaluatorIntervals, certificates, null);
        double rate = (double)evaluatorIntervals / certificates;
        if (!double.IsFinite(rate)) throw new InvalidDataException("evaluation evaluator-per-certificate rate is nonfinite");
        return new(true, false, "", accepted, planned, actual, refund, evaluatorIntervals, certificates, rate);
    }

    private static void VerifyPoint(in EmlAnytimeCurvePoint point, in EmlAnytimeEvaluationScope expected,
        int rowIndex, bool isLastRow, string previousDigest, in EmlAnytimeCurvePoint previous, in EmlAnytimeCommitments previousQuality)
    {
        if (string.IsNullOrWhiteSpace(point.PointID) || string.IsNullOrWhiteSpace(point.Digest)
            || string.IsNullOrWhiteSpace(point.RunID) || string.IsNullOrWhiteSpace(point.ConfigID)
            || string.IsNullOrWhiteSpace(point.ChainID) || string.IsNullOrWhiteSpace(point.ArmID)
            || string.IsNullOrWhiteSpace(point.Boundary) || string.IsNullOrWhiteSpace(point.EvidenceDigest))
            throw new InvalidDataException($"evaluation anytime point identity/evidence is blank at row {rowIndex + 1}");
        if (!point.VerifyDigest()) throw new InvalidDataException($"evaluation anytime point digest mismatch at row {rowIndex + 1}");
        if (point.RunID != expected.RunID || point.ConfigID != expected.ConfigID || point.ChainID != expected.ChainID
            || point.ArmID != expected.ArmID || point.Rung != expected.Rung)
            throw new InvalidDataException($"evaluation anytime point scope mismatch at row {rowIndex + 1}");
        int handshakeStep = expected.FirstStep - 1;
        if (point.PrefixStep < handshakeStep || point.PrefixStep > expected.LastStep)
            throw new InvalidDataException($"evaluation anytime prefix step {point.PrefixStep} is outside physical window {handshakeStep}..{expected.LastStep}");
        if (rowIndex > 1)
        {
            if (rowIndex == 2 && point.PrefixStep < expected.FirstStep)
                throw new InvalidDataException($"evaluation anytime scored sequence must begin at or after step {expected.FirstStep}");
            if (point.PreviousDigest != previousDigest)
                throw new InvalidDataException($"evaluation anytime previous-digest chain broke at row {rowIndex + 1}");
            bool terminal = point.RunTerminal;
            if (terminal)
            {
                if (!isLastRow || point.Boundary != "ruler.window.terminal"
                    || previous.RunTerminal || point.PrefixStep != previous.PrefixStep
                    || point.WindowIndex != previous.WindowIndex || point.WindowPlannedFuel != EmlDeliberationCounts.Zero
                    || point.WindowActualFuel != EmlDeliberationCounts.Zero || point.WindowEvaluatorIntervals != 0
                    || point.WindowPlannedEvaluatorIntervals != 0 || point.PendingResolution || !point.WindowSettled
                    || !point.WindowComplete || !point.KillEligible)
                    throw new InvalidDataException("evaluation anytime terminal point is not a zero-spend final settlement");
            }
            else if (point.PrefixStep <= previous.PrefixStep || point.WindowIndex != previous.WindowIndex + 1)
                throw new InvalidDataException($"evaluation anytime local prefix/window regressed at row {rowIndex + 1}");
            EmlDeliberationCounts pointFuel = point.Fuel;
            EmlDeliberationCounts previousFuel = previous.Fuel;
            EmlDeliberationCounts delta = EmlDeliberationCounts.Subtract(in pointFuel, in previousFuel);
            delta.ValidateNonnegative("evaluation cumulative fuel delta");
            if (delta != point.WindowActualFuel)
                throw new InvalidDataException($"evaluation anytime cumulative fuel disagrees with row {rowIndex + 1} actual fuel");
            if (!point.Quality.Dominates(previousQuality))
                throw new InvalidDataException($"evaluation anytime commitments regressed at row {rowIndex + 1}");
            if (point.EvaluatorIntervals - previous.EvaluatorIntervals != point.WindowEvaluatorIntervals)
                throw new InvalidDataException($"evaluation anytime evaluator delta disagrees at row {rowIndex + 1}");
        }
        else
        {
            if (!point.IsHandshake || point.PrefixStep != expected.FirstStep - 1 || point.WindowIndex != 0)
                throw new InvalidDataException("evaluation anytime first point must be the explicit unscored cold handshake at step zero");
            if (expected.ParentDigest.Length > 0 && point.ParentPointID != expected.ParentDigest)
                throw new InvalidDataException("evaluation anytime first point does not bind the parent digest");
            if (point.GraceUntilWindow < 0 || point.Fuel != EmlDeliberationCounts.Zero
                || point.EvaluatorIntervals != 0 || point.WindowComplete || point.KillEligible
                || point.WindowSettled || point.RunTerminal || point.WindowPlannedFuel != EmlDeliberationCounts.Zero
                || point.WindowActualFuel != EmlDeliberationCounts.Zero || point.WindowEvaluatorIntervals != 0
                || point.WindowPlannedEvaluatorIntervals != 0)
                throw new InvalidDataException("evaluation anytime first point must be an unscored zero-work handshake");
        }
        point.Quality.Validate($"evaluation row {rowIndex + 1}");
        point.Fuel.ValidateNonnegative("evaluation cumulative fuel");
        point.WindowPlannedFuel.ValidateNonnegative("evaluation planned fuel");
        point.WindowActualFuel.ValidateNonnegative("evaluation actual fuel");
        EmlDeliberationCounts actualFuel = point.WindowActualFuel;
        EmlDeliberationCounts plannedFuel = point.WindowPlannedFuel;
        if (!WithinBudget(in actualFuel, in plannedFuel)
            || point.WindowEvaluatorIntervals < 0
            || point.WindowEvaluatorIntervals > point.WindowPlannedEvaluatorIntervals)
            throw new InvalidDataException($"evaluation anytime accounting flags fail at row {rowIndex + 1}");
        if (!point.EvidenceVerified)
            throw new InvalidDataException($"evaluation anytime evidence verification failed at row {rowIndex + 1}");
    }

    private static bool IsAccepted(in EmlAnytimeCurvePoint point)
        => point.EvidenceVerified && point.WindowComplete && point.KillEligible && !point.Dominated
        && !point.ActiveFunding && !point.ActiveFork && !point.ActiveObligation && !point.PendingResolution && point.WindowSettled;

    private static Dictionary<string, int> BuildColumns(string[] header)
    {
        Dictionary<string, int> columns = new(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
            if (!columns.TryAdd(header[i], i)) throw new InvalidDataException($"evaluation anytime artifact duplicates column {header[i]}");
        return columns;
    }

    private static EmlAnytimeCurvePoint ParsePoint(string[] row, Dictionary<string, int> columns, int line)
    {
        string Text(string name) => row[columns[name]];
        long Long(string name)
            => long.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0
                ? value : throw new InvalidDataException($"evaluation anytime row {line} has invalid {name}");
        int Int(string name)
            => int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value : throw new InvalidDataException($"evaluation anytime row {line} has invalid {name}");
        bool Bool(string name)
            => Text(name) switch { "0" => false, "1" => true, _ => throw new InvalidDataException($"evaluation anytime row {line} has invalid {name}") };
        double Diagnostic(string name)
            => double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && !double.IsInfinity(value)
                ? value : throw new InvalidDataException($"evaluation anytime row {line} has invalid {name}");
        double Wall(string name)
            => double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && value >= 0
                ? value : throw new InvalidDataException($"evaluation anytime row {line} has invalid {name}");
        EmlAnytimeCommitments quality = new(Long("exact"), Long("theorem"), Long("certificates"), Long("closed_obligations"),
            Long("heldout_captures"), Long("heldout_bestk"), Long("verified_laws"), Long("verified_proofs"));
        EmlDeliberationCounts Fuel(string prefix) => new(Long(prefix + "candidate_evaluations"), Long(prefix + "logical_program_points"), Long(prefix + "executed_program_points"), Long(prefix + "inverse_transforms"), Long(prefix + "hash_probes"), Long(prefix + "join_attempts"), Long(prefix + "join_hits"), Long(prefix + "process_terms"), Long(prefix + "verifier_program_points"), Long(prefix + "candidate_supply_items"), Long(prefix + "law_rewrite_applications"), Long(prefix + "law_rewrite_tree_nodes"));
        return new(Text("point_id"), Text("previous_digest"), Text("digest"), Text("run_id"), Text("config_id"), Text("chain_id"), Text("arm_id"), Text("parent_point_id"), Int("rung"), Int("prefix_step"), Int("window"), Text("boundary"), quality, Fuel("fuel_"), Fuel("planned_"), Fuel("actual_"), Long("evaluator_intervals"), Long("window_planned_evaluator_intervals"), Long("window_evaluator_intervals"), Bool("window_complete"), Bool("evidence_verified"), Bool("kill_eligible"), Bool("dominated"), Bool("active_funding"), Bool("active_fork"), Bool("active_obligation"), Bool("pending_resolution"), Bool("window_settled"), Bool("run_terminal"), Int("grace_until_window"), Text("dominator_point_id"), Diagnostic("residual"), Diagnostic("rate"), Diagnostic("meanz"), Wall("wall_ms"), Text("evidence_digest"));
    }


    private static bool WithinBudget(in EmlDeliberationCounts actual, in EmlDeliberationCounts planned)
        => actual.CandidateEvaluations <= planned.CandidateEvaluations && actual.LogicalProgramPoints <= planned.LogicalProgramPoints && actual.ExecutedProgramPoints <= planned.ExecutedProgramPoints && actual.InverseTransforms <= planned.InverseTransforms && actual.HashProbes <= planned.HashProbes && actual.JoinAttempts <= planned.JoinAttempts && actual.JoinHits <= planned.JoinHits && actual.ProcessTerms <= planned.ProcessTerms && actual.VerifierProgramPoints <= planned.VerifierProgramPoints && actual.CandidateSupplyItems <= planned.CandidateSupplyItems && actual.LawRewriteApplications <= planned.LawRewriteApplications && actual.LawRewriteTreeNodes <= planned.LawRewriteTreeNodes;
}
