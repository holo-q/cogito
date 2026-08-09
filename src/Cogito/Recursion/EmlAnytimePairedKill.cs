namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// Runs the registered anytime kill line as a real matched Cortex fork. The two arms share the loaded image and
/// differ only at the post-load seam: the deliberation arm keeps adaptive leases, while the reflex arm freezes them.
internal static class EmlAnytimePairedKill
{
    private const ulong Seed = 0xC0117011UL;
    private const int RootStep = 1;
    private const int Window = 1;
    private const int KMax = 20;
    private const int GraceWindows = 2;
    private static readonly int[] Horizons = [2, 4, 6, 8, 10, 12, 14, 16];

    private readonly record struct Prefix(
        string Arm,
        int Rung,
        int PrefixStep,
        int Window,
        long EvaluatorSpend,
        long Planned,
        long Actual,
        long Refund,
        long Exact,
        long Theorem,
        long Certificates,
        long Closed,
        long HeldOutCaptures,
        long HeldOutBestK,
        long Laws,
        long Proofs,
        bool AccountingExact,
        bool Evidence,
        string PlannedVector,
        string ActualVector,
        string RefundVector,
        string Digest)
    {
        public bool IsStrongerThan(in Prefix other)
            => Exact >= other.Exact && Theorem >= other.Theorem && Certificates >= other.Certificates
            && Closed >= other.Closed && HeldOutCaptures >= other.HeldOutCaptures && HeldOutBestK >= other.HeldOutBestK
            && Laws >= other.Laws && Proofs >= other.Proofs;

        public bool StrictlyStrongerThan(in Prefix other)
            => IsStrongerThan(in other) && (Exact > other.Exact || Theorem > other.Theorem || Certificates > other.Certificates
                || Closed > other.Closed || HeldOutCaptures > other.HeldOutCaptures || HeldOutBestK > other.HeldOutBestK
                || Laws > other.Laws || Proofs > other.Proofs);

        public EmlAnytimeBudgetPoint ToBudgetPoint()
            => new(EvaluatorSpend, Planned, Actual,
                new(Exact, Theorem, Certificates, Closed, HeldOutCaptures, HeldOutBestK, Laws, Proofs),
                AccountingExact, Evidence, Digest);
    }

    public static int Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        CortexConfig config = CreateConfig();
        int before = Directory.Exists("runs")
            ? Directory.GetDirectories("runs", "eml-anytime-paired_*").Length
            : 0;
        int rootExit = new Cortex(config).Run();
        if (rootExit != 0) return rootExit;
        string[] roots = Directory.Exists("runs")
            ? Directory.GetDirectories("runs", "eml-anytime-paired_*").OrderBy(static p => p, StringComparer.Ordinal).ToArray()
            : [];
        if (roots.Length <= before) throw new InvalidDataException("paired anytime root did not materialize a run directory");
        string root = roots[^1];
        string checkpoint = Path.Combine(root, Checkpoint.FileName);
        if (!File.Exists(checkpoint)) throw new FileNotFoundException("paired anytime root checkpoint missing", checkpoint);
        CortexRunConfig runConfig = Checkpoint.PeekConfig(root);
        CortexForkSeed seed = CortexForkSeed.MaterializeRun(root, RootStep);
        global::Cogito.Run parentRun = global::Cogito.Run.Open(root);
        string parentRunID = Path.GetFileName(parentRun.Dir);
        Cortex spawning = Cortex.CreateCheckpointRuntime(runConfig);
        spawning.AttachForkParentRun(parentRun);
        CortexForkArm<EmlAnytimeCurve>[] deliberation = new CortexForkArm<EmlAnytimeCurve>[Horizons.Length];
        CortexForkArm<EmlAnytimeCurve>[] reflex = new CortexForkArm<EmlAnytimeCurve>[Horizons.Length];
        for (int i = 0; i < Horizons.Length; i++)
        {
            int rung = i;
            (global::Cogito.Run deliberationRun, CortexForkMaterializationContract deliberationContract) =
                parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate,
                    $"eml-anytime-paired-{rung:D2}", seed.ColdSeedDigest);
            (global::Cogito.Run reflexRun, CortexForkMaterializationContract reflexContract) =
                parentRun.CreateMaterializedChildRun(CortexForkRailRoles.ReflexFrozen,
                    $"eml-anytime-paired-{rung:D2}", seed.ColdSeedDigest);
            deliberation[i] = CreateArm(deliberationRun.Dir, runConfig, ReplayCalc.EmlAnytimeArmModes.Deliberation,
                rung, CortexForkRailRoles.Candidate, parentRunID, deliberationContract);
            reflex[i] = CreateArm(reflexRun.Dir, runConfig, ReplayCalc.EmlAnytimeArmModes.ReflexFrozenFuel,
                rung, CortexForkRailRoles.ReflexFrozen, parentRunID, reflexContract);
        }
        List<CortexMatchedForkReceipt<EmlAnytimeCurve>> receipts = CortexForkRunner.RunMatchedForkLadder(
            spawning, seed, deliberation, reflex, Horizons);

        List<Prefix> left = ReadPrefixes(receipts, left: true);
        List<Prefix> right = ReadPrefixes(receipts, left: false);
        string outputRoot = Path.Combine(root, "paired_anytime");
        Directory.CreateDirectory(outputRoot);
        bool continuity = receipts.Count == Horizons.Length && receipts[0].SeedRelation.Exact;
        bool inequality = true;
        bool terminal = true;
        for (int i = 0; i < receipts.Count; i++)
        {
            continuity &= receipts[i].SeedRelation.Exact;
            if (i > 0) inequality &= receipts[i].SeedRelation.InitialCrossArmMatched is null;
            if (i == receipts.Count - 1) terminal &= receipts[i].Left.TerminalCheckpointExact && receipts[i].Right.TerminalCheckpointExact;
        }
        bool reflexZeroAdaptive = right.All(static p => p.Actual == 0 && p.Planned == 0 && p.Refund == 0 && p.AccountingExact);
        bool reflexAdaptiveOperations = VerifyReflexAdaptiveOperations(reflex);
        bool accounting = left.Concat(right).All(static p => p.AccountingExact);
        bool evidence = left.Concat(right).All(static p => p.Evidence && p.Digest.Length == 64)
            && VerifyRungArtifacts(runConfig, deliberation, reflex);
        bool trajectory = left.Any(static p => p.Exact + p.Theorem + p.Certificates + p.Closed + p.Laws + p.Proofs > 0)
            && right.Any(static p => p.Exact + p.Theorem + p.Certificates + p.Closed + p.Laws + p.Proofs > 0);
        // The generic comparator treats its right arm as the candidate.  Reverse the paired
        // kill arms here so deliberation is adjudicated against reflex at common spend caps.
        EmlAnytimeBudgetComparison budgetComparison = EmlAnytimeBudgetComparator.Compare(
            right.Select(static point => point.ToBudgetPoint()).ToArray(),
            left.Select(static point => point.ToBudgetPoint()).ToArray());
        bool matchedBudget = budgetComparison.Comparable;
        bool qualityDominates = budgetComparison.RightNoWorse;
        bool laterGain = budgetComparison.StrictLaterGain;
        bool stepAlignment = budgetComparison.StepFunction;
        EmlAnytimeBudgetAlignment[] alignments = budgetComparison.Alignments;
        string verdict = !continuity || !inequality || !terminal || !accounting || !evidence || !trajectory || !reflexZeroAdaptive || !reflexAdaptiveOperations
            ? "fail"
            : !matchedBudget ? "inconclusive" : qualityDominates && laterGain ? "pass" : "fail";

        WritePrefixTSV(Path.Combine(outputRoot, "eml_anytime_paired_prefix.tsv"), left, right);
        WriteBudgetTSV(Path.Combine(outputRoot, "eml_anytime_paired_budget.tsv"), alignments);
        StringBuilder report = new();
        report.AppendLine("gate\tresult");
        report.AppendLine($"seed_world_corpus_exact\t{(receipts[0].SeedRelation.Exact ? "pass" : "fail")}");
        report.AppendLine($"per_arm_continuity\t{(continuity ? "pass" : "fail")}");
        report.AppendLine($"cross_arm_inequality_expected\t{(inequality ? "pass" : "fail")}");
        report.AppendLine($"terminal_checkpoint_history_evidence_exact\t{(terminal ? "pass" : "fail")}");
        report.AppendLine($"journal_planned_actual_refund\t{(accounting ? "pass" : "fail")}");
        report.AppendLine($"reflex_adaptive_fuel_zero\t{(reflexZeroAdaptive ? "pass" : "fail")}");
        report.AppendLine($"reflex_adaptive_operations\t{(reflexAdaptiveOperations ? "pass" : "fail")}");
        report.AppendLine($"matched_evaluator_budget\t{(matchedBudget ? "pass" : "inconclusive")}");
        report.AppendLine($"evaluator_alignment\t{(matchedBudget ? (stepAlignment ? "step-function" : "exact") : "none")}");
        report.AppendLine($"deliberation_quality_no_worse\t{(qualityDominates ? "pass" : "fail")}");
        report.AppendLine($"strict_later_verified_commitment\t{(laterGain ? "pass" : "fail")}");
        report.AppendLine($"evidence_digests\t{(evidence ? "pass" : "fail")}");
        report.AppendLine($"nonzero_paired_trajectory\t{(trajectory ? "pass" : "fail")}");
        report.AppendLine("paired_receipt_digest\tpass");
        report.AppendLine($"verdict\t{verdict}");
        report.AppendLine($"seed\t0x{Seed:X8}");
        report.AppendLine($"root_step\t{RootStep}");
        report.AppendLine($"windows\t{Window}");
        report.AppendLine($"kmax\t{KMax}");
        report.AppendLine($"grace\t{GraceWindows}");
        string verdictText = report.ToString();
        string verdictPath = Path.Combine(outputRoot, "eml_anytime_paired_verdict.tsv");
        string prefixText = File.ReadAllText(Path.Combine(outputRoot, "eml_anytime_paired_prefix.tsv"));
        string budgetText = File.ReadAllText(Path.Combine(outputRoot, "eml_anytime_paired_budget.tsv"));
        File.WriteAllText(verdictPath, verdictText);
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(verdictText + prefixText + budgetText)));
        File.WriteAllText(Path.Combine(outputRoot, "eml_anytime_paired.digest"), digest + "\n");
        if (!VerifyPairedDigest(outputRoot)) throw new InvalidDataException("paired anytime receipt digest failed immediately after write");
        output.WriteLine($"  eml-anytime-paired-kill · root={Path.GetFileName(root)} · arms=deliberation/reflex-frozen-fuel · continuity={(continuity ? "exact" : "FAIL")} · terminal={(terminal ? "exact" : "FAIL")}");
        output.WriteLine($"  accounting={(accounting ? "exact" : "FAIL")} · matched-budget={(matchedBudget ? (stepAlignment ? "step-function" : "exact") : "inconclusive")} · reflex-fuel={(reflexZeroAdaptive ? "zero" : "NONZERO")} · reflex-ops={(reflexAdaptiveOperations ? "zero" : "NONZERO")} · verdict={verdict}");
        output.WriteLine($"  artifacts · {outputRoot}");
        return verdict == "pass" ? 0 : verdict == "inconclusive" ? 2 : 1;
    }

    private static bool VerifyPairedDigest(string outputRoot)
    {
        string verdictPath = Path.Combine(outputRoot, "eml_anytime_paired_verdict.tsv");
        string prefixPath = Path.Combine(outputRoot, "eml_anytime_paired_prefix.tsv");
        string budgetPath = Path.Combine(outputRoot, "eml_anytime_paired_budget.tsv");
        string digestPath = Path.Combine(outputRoot, "eml_anytime_paired.digest");
        if (!File.Exists(verdictPath) || !File.Exists(prefixPath) || !File.Exists(budgetPath) || !File.Exists(digestPath)) return false;
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            File.ReadAllText(verdictPath) + File.ReadAllText(prefixPath) + File.ReadAllText(budgetPath))));
        return string.Equals(expected, File.ReadAllText(digestPath).Trim(), StringComparison.Ordinal);
    }

    private static CortexForkArm<EmlAnytimeCurve> CreateArm(
        string path,
        CortexRunConfig config,
        ReplayCalc.EmlAnytimeArmModes mode,
        int rung,
        CortexForkRailRoles role,
        string parentRunID,
        CortexForkMaterializationContract materializationContract)
        => new(path, () => Cortex.CreateCheckpointRuntime(config), cortex =>
            (cortex.MountedCurriculum as ReplayCalc)?.AnytimeCurve ?? throw new InvalidDataException("paired EML arm did not expose a typed anytime curve"),
            anytimeIdentity: new CortexForkAnytimeIdentity("eml-anytime-paired", Path.GetFileName(path), rung, ""),
            railRole: role,
            afterRuntimeBind: (cortex, _) => (cortex.MountedCurriculum as ReplayCalc)?.ConfigureAnytimeArm(mode),
            parentRunID: parentRunID,
            materializationContract: materializationContract);

    private static CortexConfig CreateConfig()
        => new()
        {
            RunName = "eml-anytime-paired",
            Steps = RootStep,
            Seed = Seed,
            Curriculum = new CortexEmlCurriculum
            {
                Actions = EmlActionSelections.ProcedureGuarded,
                Generation = new EmlGenerationConfig(),
                Lift = new EmlLiftGateConfig { MaxRuler = KMax, Window = Window, Sustain = 1, CensusOnly = true },
            },
            Learning = new CortexLearningConfig { Rhythm = false },
            Durability = new CortexDurabilityConfig { CheckpointEvery = 1, CurveEvery = 1 },
        };

    private static List<Prefix> ReadPrefixes(List<CortexMatchedForkReceipt<EmlAnytimeCurve>> receipts, bool left)
    {
        List<Prefix> result = new();
        long evaluatorOffset = 0;
        for (int i = 0; i < receipts.Count; i++)
        {
            EmlAnytimeCurve curve = left ? receipts[i].Left.Outcome : receipts[i].Right.Outcome;
            string arm = left ? "deliberation" : "reflex-frozen-fuel";
            List<Prefix> rung = new(curve.Points.Count);
            foreach (EmlAnytimeCurvePoint point in curve.Points)
            {
                EmlDeliberationCounts planned = point.WindowPlannedFuel;
                EmlDeliberationCounts actual = point.WindowActualFuel;
                EmlDeliberationCounts refund = point.WindowRefundFuel;
                long plannedTotal = SumCounts(in planned), actualTotal = SumCounts(in actual), refundTotal = SumCounts(in refund);
                rung.Add(new Prefix(arm, point.Rung, point.PrefixStep, point.WindowIndex, point.EvaluatorIntervals,
                    plannedTotal, actualTotal, refundTotal, point.Quality.ExactClasses, point.Quality.TheoremClasses,
                    point.Quality.CertificateClasses, point.Quality.ClosedObligations, point.Quality.HeldOutCaptures,
                    point.Quality.HeldOutBestK, point.Quality.VerifiedLaws, point.Quality.VerifiedProofs,
                    refundTotal == plannedTotal - actualTotal && CountsConserve(in planned, in actual, in refund),
                    point.EvidenceVerified, Vector(in planned), Vector(in actual), Vector(in refund), point.Digest));
            }
            for (int j = 0; j < rung.Count; j++)
            {
                Prefix point = rung[j] with { EvaluatorSpend = checked(rung[j].EvaluatorSpend + evaluatorOffset) };
                result.Add(point);
            }
            if (rung.Count > 0) evaluatorOffset = checked(evaluatorOffset + rung.Max(static p => p.EvaluatorSpend));
        }
        return result.OrderBy(static p => p.EvaluatorSpend).ThenBy(static p => p.Rung).ThenBy(static p => p.Window).ToList();
    }

    private static bool VerifyRungArtifacts(CortexRunConfig config, CortexForkArm<EmlAnytimeCurve>[] left, CortexForkArm<EmlAnytimeCurve>[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (!VerifyRun(config, left[i].RunDirectory) || !VerifyRun(config, right[i].RunDirectory)) return false;
        }
        return true;
    }

    private static bool VerifyReflexAdaptiveOperations(CortexForkArm<EmlAnytimeCurve>[] reflex)
    {
        for (int i = 0; i < reflex.Length; i++)
        {
            string reportPath = Path.Combine(reflex[i].RunDirectory, "eml_actions.tsv");
            if (!File.Exists(reportPath)) return false;
            string? line = File.ReadLines(reportPath).FirstOrDefault(static line => line.StartsWith("adaptive_operations\t", StringComparison.Ordinal));
            if (line is null || !line.StartsWith("adaptive_operations\treflex=", StringComparison.Ordinal)) return false;
            if (!int.TryParse(line["adaptive_operations\treflex=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int operations)
                || operations != 0) return false;
        }
        return true;
    }

    private static bool VerifyRun(CortexRunConfig config, string directory)
    {
        string[] required = [Checkpoint.FileName, "tape.spanlog", "curve.tsv", "eml_anytime_curve.tsv"];
        for (int i = 0; i < required.Length; i++)
            if (!File.Exists(Path.Combine(directory, required[i]))) return false;
        Cortex verifier = Cortex.CreateCheckpointRuntime(config);
        return verifier.VerifyMaterializedFork(directory) == 0;
    }

    private static long SumCounts(in EmlDeliberationCounts c)
        => checked(c.CandidateEvaluations + c.LogicalProgramPoints + c.ExecutedProgramPoints + c.InverseTransforms + c.HashProbes
            + c.JoinAttempts + c.JoinHits + c.ProcessTerms + c.VerifierProgramPoints + c.CandidateSupplyItems
            + c.LawRewriteApplications + c.LawRewriteTreeNodes);

    private static bool CountsConserve(in EmlDeliberationCounts planned, in EmlDeliberationCounts actual, in EmlDeliberationCounts refund)
        => planned.CandidateEvaluations == actual.CandidateEvaluations + refund.CandidateEvaluations
        && planned.LogicalProgramPoints == actual.LogicalProgramPoints + refund.LogicalProgramPoints
        && planned.ExecutedProgramPoints == actual.ExecutedProgramPoints + refund.ExecutedProgramPoints
        && planned.InverseTransforms == actual.InverseTransforms + refund.InverseTransforms
        && planned.HashProbes == actual.HashProbes + refund.HashProbes
        && planned.JoinAttempts == actual.JoinAttempts + refund.JoinAttempts
        && planned.JoinHits == actual.JoinHits + refund.JoinHits
        && planned.ProcessTerms == actual.ProcessTerms + refund.ProcessTerms
        && planned.VerifierProgramPoints == actual.VerifierProgramPoints + refund.VerifierProgramPoints
        && planned.CandidateSupplyItems == actual.CandidateSupplyItems + refund.CandidateSupplyItems
        && planned.LawRewriteApplications == actual.LawRewriteApplications + refund.LawRewriteApplications
        && planned.LawRewriteTreeNodes == actual.LawRewriteTreeNodes + refund.LawRewriteTreeNodes;

    private static string Vector(in EmlDeliberationCounts c)
        => string.Join(',', c.CandidateEvaluations, c.LogicalProgramPoints, c.ExecutedProgramPoints, c.InverseTransforms,
            c.HashProbes, c.JoinAttempts, c.JoinHits, c.ProcessTerms, c.VerifierProgramPoints, c.CandidateSupplyItems,
            c.LawRewriteApplications, c.LawRewriteTreeNodes);

    private static void WriteBudgetTSV(string path, IReadOnlyList<EmlAnytimeBudgetAlignment> alignments)
    {
        using StreamWriter writer = new(path, false, new UTF8Encoding(false));
        writer.WriteLine("budget\tdeliberation_spend\tdeliberation_slack\treflex_spend\treflex_slack");
        foreach (EmlAnytimeBudgetAlignment alignment in alignments)
            writer.WriteLine($"{alignment.Budget}\t{alignment.RightSpend}\t{alignment.RightSlack}\t{alignment.LeftSpend}\t{alignment.LeftSlack}");
    }

    private static void WritePrefixTSV(string path, List<Prefix> left, List<Prefix> right)
    {
        using StreamWriter writer = new(path, false, new UTF8Encoding(false));
        writer.WriteLine("arm\trung\tprefix_step\twindow\tevaluator_spend\tplanned\tactual\trefund\tplanned_vector\tactual_vector\trefund_vector\texact\ttheorem\tcertificates\tclosed\theldout_captures\theldout_bestk\tlaws\tproofs\taccounting\tevidence\tdigest");
        foreach (Prefix point in left.Concat(right))
            writer.WriteLine(string.Join('\t', point.Arm, point.Rung, point.PrefixStep, point.Window, point.EvaluatorSpend, point.Planned, point.Actual, point.Refund,
                point.PlannedVector, point.ActualVector, point.RefundVector, point.Exact, point.Theorem, point.Certificates, point.Closed, point.HeldOutCaptures, point.HeldOutBestK, point.Laws, point.Proofs,
                point.AccountingExact ? 1 : 0, point.Evidence ? 1 : 0, point.Digest));
    }
}
