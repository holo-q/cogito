namespace Cogito;

using System.Globalization;
using System.Text;

internal readonly record struct EnergyPolicyTrialOutcome(
    CortexPolicyRuntimeReceipt Policy,
    Weights Weights,
    long TapeBytes,
    int TapeEvents,
    int RealEvents,
    int ReplayEvents,
    int BreachEvents,
    int ReflectedEvents,
    int ExecutionEvents,
    long Savings,
    long DescriptionMbits);

internal static class EnergyPolicyAutonomy
{
    internal static void VerifyCandidate(Cortex cortex)
    {
        if (!cortex.AllowsAutonomicSpawning || !Weights.IsAdaptive(cortex.Config.Generation.Energy.Token())) return;
        if (!cortex.TryReadPolicyReadout(WeightController.PolicyID, out CortexPolicyReadoutReceipt readout)
            || readout.CandidateFingerprint == 0
            || cortex.HasPolicyOccurrenceCheck(
                policy: WeightController.PolicyID,
                readoutFingerprint: readout.Fingerprint,
                candidateFingerprint: readout.CandidateFingerprint,
                revision: readout.Revision,
                passed: out _)) return;
        int comparisons = readout.Comparisons;
        int[] horizons = cortex.Config.Learning.Policies.TrialHorizons;
        if (!cortex.TryFundPolicyTrial(
                WeightController.PolicyID,
                CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                horizons[^1],
                armCount: 3,
                out CortexPolicyTrialQuotaDecision fundingDecision)) return;

        EnergyPolicyTrialOutcome source = ReadOutcome(cortex);
        CortexForkSeed seed = cortex.MaterializeCompletedStepForkSeed();
        CortexRunConfig config = cortex.Config.ToRunConfig(null);
        StringBuilder report = new();
        report.Append("policy\t").AppendLine(WeightController.PolicyID.Value)
              .Append("fingerprint\t").AppendLine(readout.Fingerprint.ToString("X16", CultureInfo.InvariantCulture))
              .Append("seed_step\t").AppendLine(seed.NextStep.ToString(CultureInfo.InvariantCulture))
              .AppendLine("horizon\tlaunchpad_description_mbits\tgrammar_description_mbits\tlaunchpad_savings\tgrammar_savings\tlaunchpad_reversals\tgrammar_reversals\tgrammar_executions\tgrammar_divergent\tcheckpoint_exact\tpareto");

        bool invariantClean = true;
        bool allPareto = true;
        bool allEquivalent = true;
        bool strictGain = false;
        bool diverged = false;
        int passedHorizons = 0;
        int terminalLaunchpadReversals = 0;
        int terminalGrammarReversals = 0;
        ulong terminalGrammarExecutions = 0;
        long actualExecutedArmSteps = 0;
        CortexForkRunReceipt<EnergyPolicyTrialOutcome>? terminalLaunchpad = null;
        CortexForkArm<EnergyPolicyTrialOutcome>[] launchpadArms = new CortexForkArm<EnergyPolicyTrialOutcome>[horizons.Length];
        CortexForkArm<EnergyPolicyTrialOutcome>[] grammarArms = new CortexForkArm<EnergyPolicyTrialOutcome>[horizons.Length];
        int[] absoluteHorizons = new int[horizons.Length];
        string parentRunID = Path.GetFileName(cortex.CurrentRun.Dir);
        string attemptID = fundingDecision.QuotaDecisionID.ToString();
        for (int i = 0; i < horizons.Length; i++)
        {
            int horizon = horizons[i];
            (Run baselineRun, CortexForkMaterializationContract baselineContract) =
                cortex.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline, attemptID, seed.ColdSeedDigest);
            (Run candidateRun, CortexForkMaterializationContract candidateContract) =
                cortex.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate, attemptID, seed.ColdSeedDigest);
            launchpadArms[i] = CreateArm(baselineRun.Dir, config,
                CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), CortexPolicyAuthorities.Shadow, 0,
                railRole: CortexForkRailRoles.Baseline, parentRunID: parentRunID, materializationContract: baselineContract);
            grammarArms[i] = CreateArm(candidateRun.Dir, config,
                CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), CortexPolicyAuthorities.Grammar, 0,
                railRole: CortexForkRailRoles.Candidate, parentRunID: parentRunID, materializationContract: candidateContract);
            absoluteHorizons[i] = checked(seed.NextStep + horizon);
        }
        List<CortexMatchedForkReceipt<EnergyPolicyTrialOutcome>> forks = CortexForkRunner.RunMatchedForkLadder(
            cortex, seed, launchpadArms, grammarArms, absoluteHorizons);
        for (int i = 0; i < horizons.Length; i++)
        {
            int horizon = horizons[i];
            CortexMatchedForkReceipt<EnergyPolicyTrialOutcome> fork = forks[i];
            actualExecutedArmSteps = checked(actualExecutedArmSteps + fork.Left.StepSpan.ActualSteps + fork.Right.StepSpan.ActualSteps);

            EnergyPolicyTrialOutcome launchpad = fork.Left.Outcome;
            EnergyPolicyTrialOutcome grammar = fork.Right.Outcome;
            long launchpadDescription = launchpad.DescriptionMbits - source.DescriptionMbits;
            long grammarDescription = grammar.DescriptionMbits - source.DescriptionMbits;
            long launchpadSavings = launchpad.Savings - source.Savings;
            long grammarSavings = grammar.Savings - source.Savings;
            int launchpadReversals = launchpad.Policy.ActionReversals - source.Policy.ActionReversals;
            int grammarReversals = grammar.Policy.ActionReversals - source.Policy.ActionReversals;
            ulong grammarExecutions = grammar.Policy.GrammarExecutions - source.Policy.GrammarExecutions;
            ulong grammarDivergent = grammar.Policy.DivergentGrammarExecutions - source.Policy.DivergentGrammarExecutions;
            bool exact = IsInvariantClean(in fork, in source, in launchpad, in grammar) && grammarExecutions <= 1;
            bool pareto = grammarDescription <= launchpadDescription && grammarReversals <= launchpadReversals;
            bool equivalent = HaveEquivalentWorld(in launchpad, in grammar);
            invariantClean &= exact;
            allPareto &= pareto;
            allEquivalent &= equivalent;
            strictGain |= grammarDescription < launchpadDescription || grammarReversals < launchpadReversals;
            diverged |= grammarDivergent > 0;
            if (exact && pareto) passedHorizons++;
            terminalLaunchpadReversals = launchpadReversals;
            terminalGrammarReversals = grammarReversals;
            terminalGrammarExecutions = grammarExecutions;
            if (i == horizons.Length - 1) terminalLaunchpad = fork.Left;

            report.Append(horizon).Append('\t')
                  .Append(launchpadDescription).Append('\t').Append(grammarDescription).Append('\t')
                  .Append(launchpadSavings).Append('\t').Append(grammarSavings).Append('\t')
                  .Append(launchpadReversals).Append('\t').Append(grammarReversals).Append('\t')
                  .Append(grammarExecutions).Append('\t').Append(grammarDivergent).Append('\t')
                  .Append(exact ? "yes" : "no").Append('\t')
                  .AppendLine(pareto ? "yes" : "no");
        }

        bool reversalDebtRepaid = terminalGrammarReversals <= terminalLaunchpadReversals;
        bool terminalExecuted = terminalGrammarExecutions == 1;
        bool candidatePassed = invariantClean && reversalDebtRepaid && terminalExecuted
            && (diverged ? allPareto && strictGain : allEquivalent);
        bool counterfactualRejected = RunCounterfactual(
            cortex, seed, config, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), source,
            terminalLaunchpad ?? throw new InvalidOperationException("terminal launchpad receipt was not retained"),
            horizons[^1], attemptID, report,
            out CortexPolicyRuntimeReceipt counterfactualPolicy,
            out bool counterfactualImproved);
        bool passed = candidatePassed && counterfactualRejected;
        report.Append("candidate_kind\t").AppendLine(diverged ? "adaptation" : "emulation")
              .Append("strict_gain\t").AppendLine(strictGain ? "yes" : "no")
              .Append("reversal_debt_repaid\t").AppendLine(reversalDebtRepaid ? "yes" : "no")
              .Append("counterfactual_rejected\t").AppendLine(counterfactualRejected ? "yes" : "no")
              .Append("counterfactual_learned\t").AppendLine(counterfactualImproved ? "yes" : "no")
              .Append("verdict\t").AppendLine(passed ? "pass" : "fail");
        string reportText = report.ToString();
        cortex.CurrentRun.Write($"energy_policy_verification_{readout.Fingerprint:X16}.tsv", reportText);
        cortex.CurrentRun.Write("energy_policy_verification.tsv", reportText);

        int failures = horizons.Length - passedHorizons;
        if (!terminalExecuted) failures++;
        if (diverged && !strictGain) failures++;
        if (!reversalDebtRepaid) failures++;
        if (!diverged && !allEquivalent) failures++;
        if (!counterfactualRejected) failures++;
        cortex.CompletePolicyTrial(
            in fundingDecision,
            actualExecutedArmSteps,
            null,
            passed ? CortexPolicyVerifierOutcomes.Passed : CortexPolicyVerifierOutcomes.Failed,
            null);
        cortex.RecordPolicyOccurrenceCheck(
            WeightController.PolicyID,
            readout.Fingerprint,
            horizons.Length + 1,
            passedHorizons + (counterfactualRejected ? 1 : 0),
            failures,
            passed);
        if (passed && !cortex.TryGrantVerifiedPolicySuccession(
                WeightController.PolicyID, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision))
            throw new InvalidOperationException("verified energy weight policy was refused by the Cortex authority gate");
        Trace.Cortex.Boundary(
            "energy.policy.verify",
            $"fp={readout.Fingerprint:X16} kind={(diverged ? "adaptation" : "emulation")} horizons={passedHorizons}/{horizons.Length} strict={(strictGain ? 1 : 0)} reversal-debt={(reversalDebtRepaid ? "repaid" : "open")} counterfactual={(counterfactualRejected ? "rejected" : "LIVE")} result={(passed ? "PASS" : "FAIL")}");
    }

    private static bool RunCounterfactual(
        Cortex cortex,
        CortexForkSeed seed,
        CortexRunConfig config,
        CortexPolicyTrialAuthorityIdentity authorityIdentity,
        in EnergyPolicyTrialOutcome source,
        CortexForkRunReceipt<EnergyPolicyTrialOutcome> launchpadReceipt,
        int horizon,
        string attemptID,
        StringBuilder report,
        out CortexPolicyRuntimeReceipt counterfactualPolicy,
        out bool improved)
    {
        ulong interventionSeed = authorityIdentity.CandidateFingerprint.Value ^ ((ulong)(uint)seed.NextStep * 0x9E3779B97F4A7C15UL);
        string parentRunID = Path.GetFileName(cortex.CurrentRun.Dir);
        (Run forcedNullRun, CortexForkMaterializationContract forcedNullContract) =
            cortex.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ForcedNull, attemptID, seed.ColdSeedDigest);
        CortexForkRunReceipt<EnergyPolicyTrialOutcome> counterfactualReceipt = CortexForkRunner.RunFork(
            cortex,
            seed,
            CreateArm(
                forcedNullRun.Dir,
                config,
                authorityIdentity,
                CortexPolicyAuthorities.Grammar,
                grammarExecutionQuota: -1,
                forcedDivergenceSeed: interventionSeed,
                railRole: CortexForkRailRoles.ForcedNull,
                parentRunID: parentRunID,
                materializationContract: forcedNullContract),
            checked(seed.NextStep + horizon));
        EnergyPolicyTrialOutcome launchpad = launchpadReceipt.Outcome;
        EnergyPolicyTrialOutcome counterfactual = counterfactualReceipt.Outcome;
        counterfactualPolicy = counterfactual.Policy;
        ulong executions = counterfactualPolicy.GrammarExecutions - source.Policy.GrammarExecutions;
        ulong divergent = counterfactualPolicy.DivergentGrammarExecutions - source.Policy.DivergentGrammarExecutions;
        long launchpadDescription = launchpad.DescriptionMbits - source.DescriptionMbits;
        long counterfactualDescription = counterfactual.DescriptionMbits - source.DescriptionMbits;
        int launchpadReversals = launchpad.Policy.ActionReversals - source.Policy.ActionReversals;
        int counterfactualReversals = counterfactual.Policy.ActionReversals - source.Policy.ActionReversals;
        double divergenceRate = executions == 0 ? 0 : (double)divergent / executions;
        bool exact = HaveEqualDigests(launchpadReceipt.SeedDigests, counterfactualReceipt.SeedDigests)
            && IsInvariantClean(launchpadReceipt, in source, in launchpad)
            && IsInvariantClean(counterfactualReceipt, in source, in counterfactual)
            && executions > 1
            && divergent == executions;
        improved = exact && counterfactualDescription < launchpadDescription
            && counterfactualReversals <= launchpadReversals;
        bool rejected = exact && !improved;
        report.Append("counterfactual\t").Append(horizon).Append('\t')
              .Append("launchpad_description_mbits=").Append(launchpadDescription).Append('\t')
              .Append("intervention_description_mbits=").Append(counterfactualDescription).Append('\t')
              .Append("launchpad_reversals=").Append(launchpadReversals).Append('\t')
              .Append("intervention_reversals=").Append(counterfactualReversals).Append('\t')
              .Append("executions=").Append(executions).Append('\t')
              .Append("divergent=").Append(divergent).Append('\t')
              .Append("divergence_rate=").Append(divergenceRate.ToString("F6", CultureInfo.InvariantCulture)).Append('\t')
              .AppendLine(rejected ? "rejected" : "survived");
        return rejected;
    }

    private static CortexForkArm<EnergyPolicyTrialOutcome> CreateArm(
        string directory,
        CortexRunConfig config,
        CortexPolicyTrialAuthorityIdentity authorityIdentity,
        CortexPolicyAuthorities authority,
        int actionOffset = 0,
        int grammarExecutionQuota = 1,
        ulong? forcedDivergenceSeed = null,
        CortexForkRailRoles railRole = CortexForkRailRoles.Unknown,
        string parentRunID = "",
        CortexForkMaterializationContract? materializationContract = null)
    {
        if (materializationContract is not CortexForkMaterializationContract contract)
            throw new ArgumentException("energy policy arms require a typed materialization contract", nameof(materializationContract));
        contract.Validate(directory);
        if (!string.Equals(contract.ParentRunID, parentRunID, StringComparison.Ordinal))
            throw new InvalidDataException("energy policy arm parent identity disagrees with its materialization contract");
        if (contract.ChildRunID != Path.GetFileName(Path.GetFullPath(directory)))
            throw new InvalidDataException("energy policy arm child identity disagrees with its materialization contract");
        return new(
            directory,
            () => Cortex.CreateCheckpointRuntime(config),
            ReadOutcome,
            (Cortex trial) => trial.SetPolicyTrialAuthority(
                WeightController.PolicyID,
                authorityIdentity,
                authority,
                authority == CortexPolicyAuthorities.Grammar ? grammarExecutionQuota : -1,
                actionOffset,
                forcedDivergenceSeed),
            railRole: railRole,
            parentRunID: parentRunID,
            materializationContract: contract);
    }

    private static EnergyPolicyTrialOutcome ReadOutcome(Cortex cortex)
    {
        Tape tape = cortex.Tape;
        Weights weights = cortex.Homeostat.Fast.Current;
        return new EnergyPolicyTrialOutcome(
            cortex.ReadPolicyRuntimeReceipt(WeightController.PolicyID),
            weights,
            tape.ByteLength,
            tape.Count,
            tape.RealCount,
            tape.ReplayCount,
            tape.BreachCount,
            tape.ReflectedCount,
            tape.ExecutionCount,
            cortex.Grammar.TotalSavings.Value,
            checked(tape.ByteLength * 8000 - cortex.Grammar.TotalSavings.Value));
    }

    private static bool HaveEquivalentWorld(in EnergyPolicyTrialOutcome left, in EnergyPolicyTrialOutcome right)
        => left.Weights == right.Weights
            && left.TapeBytes == right.TapeBytes
            && left.TapeEvents == right.TapeEvents
            && left.RealEvents == right.RealEvents
            && left.ReplayEvents == right.ReplayEvents
            && left.BreachEvents == right.BreachEvents
            && left.ReflectedEvents == right.ReflectedEvents
            && left.ExecutionEvents == right.ExecutionEvents
            && left.Savings == right.Savings;

    private static bool IsInvariantClean(
        in CortexMatchedForkReceipt<EnergyPolicyTrialOutcome> fork,
        in EnergyPolicyTrialOutcome source,
        in EnergyPolicyTrialOutcome left,
        in EnergyPolicyTrialOutcome right)
        => fork.IsExact
            && IsInvariantClean(fork.Left, in source, in left)
            && IsInvariantClean(fork.Right, in source, in right);

    private static bool IsInvariantClean(
        CortexForkRunReceipt<EnergyPolicyTrialOutcome> run,
        in EnergyPolicyTrialOutcome source,
        in EnergyPolicyTrialOutcome outcome)
        => run.ExitCode == 0
            && run.TerminalCheckpointExact
            && AreFinite(outcome.Weights)
            && outcome.TapeBytes >= source.TapeBytes
            && outcome.TapeEvents >= source.TapeEvents
            && HasCompleteDelayedOutcomes(source.Policy, outcome.Policy);

    private static bool HaveEqualDigests(CortexForkDigests left, CortexForkDigests right)
        => string.Equals(left.CheckpointSHA256, right.CheckpointSHA256, StringComparison.Ordinal)
            && string.Equals(left.TapeSpanlogSHA256, right.TapeSpanlogSHA256, StringComparison.Ordinal)
            && string.Equals(left.CurveSHA256, right.CurveSHA256, StringComparison.Ordinal);

    private static bool HasCompleteDelayedOutcomes(
        in CortexPolicyRuntimeReceipt source,
        in CortexPolicyRuntimeReceipt outcome)
    {
        ulong decisions = outcome.Decisions - source.Decisions;
        ulong resolved = outcome.Outcomes - source.Outcomes;
        return resolved == decisions || resolved + 1 == decisions;
    }

    private static bool AreFinite(in Weights weights)
        => double.IsFinite(weights.Phi) && double.IsFinite(weights.Transition)
           && double.IsFinite(weights.Novelty) && double.IsFinite(weights.Depth) && double.IsFinite(weights.Noise)
           && weights.Novelty is >= 0 and <= 3 && weights.Depth is >= 0 and <= 3
           && weights.Noise is >= 0 and <= 0.3;

    private static MetricSample[] CreateFeatureSamples(double[] features)
    {
        MetricSample[] samples = new MetricSample[features.Length];
        for (int i = 0; i < features.Length; i++)
            samples[i] = new MetricSample(new MetricID((ushort)(560 + i)), NumericValue.FromF64(features[i]));
        return samples;
    }
}
