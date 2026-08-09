namespace Cogito;

using System.Text;

internal readonly struct EmlActionTrialOutcome
{
    public EmlActionTrialOutcome(
        EmlPolicyOutcomeSnapshot discovery,
        int signReversals,
        int shadowComparisons,
        int shadowAgreements,
        int emulationMisses)
    {
        Discovery = discovery;
        SignReversals = signReversals;
        ShadowComparisons = shadowComparisons;
        ShadowAgreements = shadowAgreements;
        EmulationMisses = emulationMisses;
    }

    public EmlPolicyOutcomeSnapshot Discovery { get; }
    public int SignReversals { get; }
    public int ShadowComparisons { get; }
    public int ShadowAgreements { get; }
    public int EmulationMisses { get; }
}

internal sealed class EmlActionAutonomyReward : CortexReward
{
    public override void OnStepCompleted(Cortex cortex, int step)
    {
        if (!cortex.AllowsAutonomicSpawning) return;
        if (!cortex.TryReadPolicyReadout(ReplayCalc.ActionPolicyID, out CortexPolicyReadoutReceipt readout)
            || readout.CandidateFingerprint == 0
            || cortex.HasPolicyOccurrenceCheck(
                policy: ReplayCalc.ActionPolicyID,
                readoutFingerprint: readout.Fingerprint,
                candidateFingerprint: readout.CandidateFingerprint,
                revision: readout.Revision,
                passed: out _)) return;
        int comparisons = readout.Comparisons;
        int sourceMisses = readout.Misses;
        if (RestoreVerification(cortex, readout.Fingerprint)) return;
        int[] horizons = cortex.Config.Learning.Policies.TrialHorizons;
        if (!cortex.TryFundPolicyTrial(
                ReplayCalc.ActionPolicyID,
                CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                horizons[^1],
                armCount: 2,
                out CortexPolicyTrialQuotaDecision fundingDecision)) return;

        ReplayCalc sourceDream = ResolveDream(cortex);
        EmlPolicyOutcomeSnapshot sourceDiscovery = sourceDream.ReadPolicyOutcomeSnapshot();
        int sourceReversals = cortex.ReadPolicyActionReversals(ReplayCalc.ActionPolicyID);
        CortexForkSeed seed = cortex.MaterializeCompletedStepForkSeed();
        CortexRunConfig config = cortex.Config.ToRunConfig(null);
        string parentRunID = Path.GetFileName(cortex.CurrentRun.Dir);
        string attemptID = fundingDecision.QuotaDecisionID.ToString();
        StringBuilder report = new();
        report.Append("policy\t").AppendLine(ReplayCalc.ActionPolicyID.Value)
              .Append("fingerprint\t").AppendLine(readout.Fingerprint.ToString("X16", System.Globalization.CultureInfo.InvariantCulture))
              .Append("seed_step\t").AppendLine(seed.NextStep.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .AppendLine("horizon\tlaunchpad_calls\tgrammar_calls\tlaunchpad_deltas\tgrammar_deltas\tlaunchpad_first\tgrammar_first\tlaunchpad_reversals\tgrammar_reversals\tgrammar_misses\tcheckpoint_exact\tpareto");

        bool invariantClean = true;
        bool allPareto = true;
        bool allEquivalent = true;
        bool strictGain = false;
        bool diverged = false;
        int passedHorizons = 0;
        int terminalLaunchpadReversals = 0;
        int terminalGrammarReversals = 0;
        long actualExecutedArmSteps = 0;
        long evaluatorWorkUnits = 0;
        CortexForkArm<EmlActionTrialOutcome>[] launchpadArms = new CortexForkArm<EmlActionTrialOutcome>[horizons.Length];
        CortexForkArm<EmlActionTrialOutcome>[] grammarArms = new CortexForkArm<EmlActionTrialOutcome>[horizons.Length];
        int[] absoluteHorizons = new int[horizons.Length];
        for (int i = 0; i < horizons.Length; i++)
        {
            int horizon = horizons[i];
            (Run launchpadRun, CortexForkMaterializationContract launchpadContract) =
                cortex.CurrentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.Baseline, attemptID, seed.ColdSeedDigest);
            (Run grammarRun, CortexForkMaterializationContract grammarContract) =
                cortex.CurrentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.Candidate, attemptID, seed.ColdSeedDigest);
            launchpadArms[i] = new CortexForkArm<EmlActionTrialOutcome>(
                launchpadRun.Dir,
                () => Cortex.CreateCheckpointRuntime(config),
                ReadOutcome,
                (Cortex trial) => trial.SetPolicyTrialAuthority(
                    ReplayCalc.ActionPolicyID,
                    CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                    CortexPolicyAuthorities.Shadow),
                railRole: CortexForkRailRoles.Baseline,
                parentRunID: parentRunID,
                materializationContract: launchpadContract);
            grammarArms[i] = new CortexForkArm<EmlActionTrialOutcome>(
                grammarRun.Dir,
                () => Cortex.CreateCheckpointRuntime(config),
                ReadOutcome,
                (Cortex trial) => trial.SetPolicyTrialAuthority(
                    ReplayCalc.ActionPolicyID,
                    CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                    CortexPolicyAuthorities.Grammar),
                railRole: CortexForkRailRoles.Candidate,
                parentRunID: parentRunID,
                materializationContract: grammarContract);
            absoluteHorizons[i] = checked(seed.NextStep + horizon);
        }
        List<CortexMatchedForkReceipt<EmlActionTrialOutcome>> forks = CortexForkRunner.RunMatchedForkLadder(
            cortex, seed, launchpadArms, grammarArms, absoluteHorizons);
        for (int i = 0; i < horizons.Length; i++)
        {
            int horizon = horizons[i];
            CortexMatchedForkReceipt<EmlActionTrialOutcome> fork = forks[i];
            actualExecutedArmSteps = checked(actualExecutedArmSteps + fork.Left.StepSpan.ActualSteps + fork.Right.StepSpan.ActualSteps);

            EmlActionTrialOutcome launchpadOutcome = fork.Left.Outcome;
            EmlActionTrialOutcome grammarOutcome = fork.Right.Outcome;
            long launchpadCalls = launchpadOutcome.Discovery.EvaluatorCalls - sourceDiscovery.EvaluatorCalls;
            long grammarCalls = grammarOutcome.Discovery.EvaluatorCalls - sourceDiscovery.EvaluatorCalls;
            evaluatorWorkUnits = checked(evaluatorWorkUnits + launchpadCalls + grammarCalls);
            long launchpadDeltas = launchpadOutcome.Discovery.CanonicalDeltas - sourceDiscovery.CanonicalDeltas;
            long grammarDeltas = grammarOutcome.Discovery.CanonicalDeltas - sourceDiscovery.CanonicalDeltas;
            long launchpadFirst = launchpadOutcome.Discovery.FirstCaptures - sourceDiscovery.FirstCaptures;
            long grammarFirst = grammarOutcome.Discovery.FirstCaptures - sourceDiscovery.FirstCaptures;
            int launchpadReversals = launchpadOutcome.SignReversals - sourceReversals;
            int grammarReversals = grammarOutcome.SignReversals - sourceReversals;
            int grammarMisses = grammarOutcome.EmulationMisses - sourceMisses;
            bool exact = fork.IsExact
                && fork.Left.ExitCode == 0
                && fork.Right.ExitCode == 0
                && fork.Left.TerminalCheckpointExact
                && fork.Right.TerminalCheckpointExact
                && launchpadOutcome.Discovery.HistoryComplete
                && grammarOutcome.Discovery.HistoryComplete;
            bool pareto = grammarCalls <= launchpadCalls
                && grammarDeltas >= launchpadDeltas
                && grammarFirst >= launchpadFirst;
            bool equivalent = grammarCalls == launchpadCalls
                && grammarDeltas == launchpadDeltas
                && grammarFirst == launchpadFirst
                && grammarReversals == launchpadReversals;
            invariantClean &= exact;
            allPareto &= pareto;
            allEquivalent &= equivalent;
            strictGain |= grammarCalls < launchpadCalls
                || grammarDeltas > launchpadDeltas
                || grammarFirst > launchpadFirst;
            diverged |= grammarMisses > 0;
            if (exact && pareto) passedHorizons++;
            terminalLaunchpadReversals = launchpadReversals;
            terminalGrammarReversals = grammarReversals;

            report.Append(horizon).Append('\t')
                  .Append(launchpadCalls).Append('\t').Append(grammarCalls).Append('\t')
                  .Append(launchpadDeltas).Append('\t').Append(grammarDeltas).Append('\t')
                  .Append(launchpadFirst).Append('\t').Append(grammarFirst).Append('\t')
                  .Append(launchpadReversals).Append('\t').Append(grammarReversals).Append('\t')
                  .Append(grammarMisses).Append('\t')
                  .Append(exact ? "yes" : "no").Append('\t')
                  .AppendLine(pareto ? "yes" : "no");
        }

        bool reversalDebtRepaid = terminalGrammarReversals <= terminalLaunchpadReversals;
        bool passed = invariantClean && reversalDebtRepaid
            && (diverged ? allPareto && strictGain : allEquivalent);
        report.Append("candidate_kind\t").AppendLine(diverged ? "adaptation" : "emulation")
              .Append("strict_gain\t").AppendLine(strictGain ? "yes" : "no")
              .Append("reversal_debt_repaid\t").AppendLine(reversalDebtRepaid ? "yes" : "no")
              .Append("verdict\t").AppendLine(passed ? "pass" : "fail");
        cortex.CurrentRun.Write("eml_policy_verification.tsv", report.ToString());
        int failures = horizons.Length - passedHorizons;
        if (!reversalDebtRepaid) failures++;
        if (diverged && !strictGain) failures++;
        if (!diverged && !allEquivalent) failures++;
        cortex.CompletePolicyTrial(
            in fundingDecision,
            actualExecutedArmSteps,
            evaluatorWorkUnits,
            passed ? CortexPolicyVerifierOutcomes.Passed : CortexPolicyVerifierOutcomes.Failed,
            null);
        cortex.RecordPolicyOccurrenceCheck(
            ReplayCalc.ActionPolicyID,
            readout.Fingerprint,
            horizons.Length,
            passedHorizons,
            failures,
            passed);
        if (passed && !cortex.TryGrantVerifiedPolicySuccession(
                ReplayCalc.ActionPolicyID, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision))
            throw new InvalidOperationException("verified EML action policy was refused by the Cortex authority gate");
        Trace.Cortex.Boundary(
            "eml.policy.verify",
            $"fp={readout.Fingerprint:X16} kind={(diverged ? "adaptation" : "emulation")} horizons={passedHorizons}/{horizons.Length} strict={(strictGain ? 1 : 0)} reversal-debt={(reversalDebtRepaid ? "repaid" : "open")} result={(passed ? "PASS" : "FAIL")}");
    }

    // Negative-restore cache: the stale-verification window calls this every step; an unchanged (or absent)
    // banked file cannot change the verdict, so re-reading it costs one stat instead of a full parse.
    // Positive restores are never cached — they carry side effects and fire once.
    private string? _restoreMissPath;
    private ulong _restoreMissFingerprint;
    private DateTime _restoreMissWriteTimeUtc;

    private bool RestoreVerification(Cortex cortex, ulong fingerprint)
    {
        string path = cortex.CurrentRun.PathOf("eml_policy_verification.tsv");
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);           // sentinel epoch when the file is absent
        if (string.Equals(path, _restoreMissPath, StringComparison.Ordinal)
            && fingerprint == _restoreMissFingerprint && writeTimeUtc == _restoreMissWriteTimeUtc)
            return false;
        bool Miss()
        {
            _restoreMissPath = path;
            _restoreMissFingerprint = fingerprint;
            _restoreMissWriteTimeUtc = writeTimeUtc;
            return false;
        }
        if (!File.Exists(path)) return Miss();
        string[] lines = File.ReadAllLines(path);
        string expectedFingerprint = fingerprint.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        bool fingerprintMatches = false;
        bool passed = false;
        int comparisons = 0;
        int agreements = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("fingerprint\t", StringComparison.Ordinal))
                fingerprintMatches = string.Equals(line[12..], expectedFingerprint, StringComparison.Ordinal);
            else if (line.StartsWith("verdict\t", StringComparison.Ordinal))
                passed = string.Equals(line[8..], "pass", StringComparison.Ordinal);
            else if (line.Length > 0 && char.IsAsciiDigit(line[0]))
            {
                comparisons++;
                string[] columns = line.Split('\t');
                if (columns.Length >= 12
                    && string.Equals(columns[10], "yes", StringComparison.Ordinal)
                    && string.Equals(columns[11], "yes", StringComparison.Ordinal)) agreements++;
            }
        }
        if (!fingerprintMatches) return Miss();
        int failures = comparisons - agreements;
        cortex.RecordPolicyOccurrenceCheck(
            ReplayCalc.ActionPolicyID, fingerprint, comparisons, agreements, failures, passed);
        if (passed
            && (!cortex.TryReadPolicyReadout(ReplayCalc.ActionPolicyID, out CortexPolicyReadoutReceipt readout)
                || readout.Fingerprint != fingerprint
                || !cortex.TryGrantVerifiedPolicySuccession(
                    ReplayCalc.ActionPolicyID, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision)))
            throw new InvalidOperationException("banked EML action verification was refused by the Cortex authority gate");
        Trace.Cortex.Boundary("eml.policy.restore",
            $"fp={fingerprint:X16} agreement={agreements}/{comparisons} result={(passed ? "PASS" : "FAIL")}");
        return true;
    }

    private static EmlActionTrialOutcome ReadOutcome(Cortex cortex)
    {
        ReplayCalc dream = ResolveDream(cortex);
        if (!cortex.TryReadPolicyReadout(ReplayCalc.ActionPolicyID, out CortexPolicyReadoutReceipt readout))
            throw new InvalidOperationException("EML action trial finished without its published-grammar readout");
        return new EmlActionTrialOutcome(
            dream.ReadPolicyOutcomeSnapshot(),
            cortex.ReadPolicyActionReversals(ReplayCalc.ActionPolicyID),
            readout.Comparisons,
            readout.Agreements,
            readout.Misses);
    }

    private static ReplayCalc ResolveDream(Cortex cortex)
        => cortex.MountedCurriculum as ReplayCalc
        ?? throw new InvalidOperationException("EML action autonomy requires ReplayCalc");
}
