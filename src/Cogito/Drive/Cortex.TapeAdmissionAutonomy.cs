namespace Cogito;

using Cogito.Grammar;
using System.Globalization;
using System.Text;

internal readonly record struct CortexTapeAdmissionTrialOutcome(
    CortexPolicyRuntimeReceipt Policy,
    HashSet<RuleID> RuleIDs,
    long TapeBytes,
    int TapeEvents,
    int RealEvents,
    int ReplayEvents,
    int BreachEvents,
    int ReflectedEvents,
    int ExecutionEvents,
    long Savings,
    int CompressedSymbols,
    long DescriptionMbits);

public static partial class CortexTapeAdmission
{
    internal static void VerifyCandidate(Cortex cortex)
    {
        if (!cortex.AllowsAutonomicSpawning) return;
        if (!cortex.TryReadPolicyReadout(PolicyID, out CortexPolicyReadoutReceipt readout)
            || readout.CandidateFingerprint == 0
            || cortex.HasPolicyOccurrenceCheck(
                policy: PolicyID,
                readoutFingerprint: readout.Fingerprint,
                candidateFingerprint: readout.CandidateFingerprint,
                revision: readout.Revision,
                passed: out _)) return;
        int comparisons = readout.Comparisons;
        if (RestoreOccurrenceCheck(cortex, readout.Fingerprint)) return;
        int[] horizons = cortex.Config.Learning.Policies.TrialHorizons;
        if (!cortex.TryFundPolicyTrial(PolicyID, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), horizons[^1], armCount: 3,
                out CortexPolicyTrialQuotaDecision fundingDecision)) return;

        CortexTapeAdmissionTrialOutcome source = ReadOutcome(cortex);
        CortexForkSeed seed = cortex.MaterializeCompletedStepForkSeed();
        CortexRunConfig config = cortex.Config.ToRunConfig(null);
        Run parentRun = cortex.CurrentRun;
        string parentRunID = Path.GetFileName(parentRun.Dir);
        string attemptID = fundingDecision.QuotaDecisionID.ToString();
        StringBuilder report = new();
        report.Append("policy\t").AppendLine(PolicyID.Value)
              .Append("fingerprint\t").AppendLine(readout.Fingerprint.ToString("X16", CultureInfo.InvariantCulture))
              .Append("seed_step\t").AppendLine(seed.NextStep.ToString(CultureInfo.InvariantCulture))
              .AppendLine("horizon\tlaunchpad_decisions\tgrammar_decisions\tlaunchpad_admit\tgrammar_admit\tlaunchpad_reject\tgrammar_reject\tlaunchpad_description_mbits\tgrammar_description_mbits\tlaunchpad_bytes\tgrammar_bytes\tlaunchpad_rules\tgrammar_rules\tlaunchpad_savings\tgrammar_savings\tgrammar_executions\tgrammar_divergent\tdirection_safe\tcheckpoint_exact\tdescription_nonregression");

        bool invariantClean = true;
        bool allDescriptionNonregression = true;
        bool allDirectionsSafe = true;
        bool allEquivalent = true;
        bool strictGain = false;
        bool diverged = false;
        long actualExecutedArmSteps = 0;
        int passedHorizons = 0;
        CortexMatchedForkReceipt<CortexTapeAdmissionTrialOutcome>? terminal = null;
        CortexForkArm<CortexTapeAdmissionTrialOutcome>[] launchpadArms = new CortexForkArm<CortexTapeAdmissionTrialOutcome>[horizons.Length];
        CortexForkArm<CortexTapeAdmissionTrialOutcome>[] grammarArms = new CortexForkArm<CortexTapeAdmissionTrialOutcome>[horizons.Length];
        int[] absoluteHorizons = new int[horizons.Length];
        for (int i = 0; i < horizons.Length; i++)
        {
            int horizon = horizons[i];
            (Run launchpadRun, CortexForkMaterializationContract launchpadContract) =
                parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline, attemptID, seed.ColdSeedDigest);
            (Run grammarRun, CortexForkMaterializationContract grammarContract) =
                parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate, attemptID, seed.ColdSeedDigest);
            launchpadArms[i] = CreateArm(
            launchpadRun, config, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                CortexPolicyAuthorities.Shadow, CortexForkRailRoles.Baseline, parentRunID, launchpadContract,
                actionOffset: 0);
            grammarArms[i] = CreateArm(
            grammarRun, config, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                CortexPolicyAuthorities.Grammar, CortexForkRailRoles.Candidate, parentRunID, grammarContract,
                actionOffset: 0);
            absoluteHorizons[i] = checked(seed.NextStep + horizon);
        }
        List<CortexMatchedForkReceipt<CortexTapeAdmissionTrialOutcome>> forks = CortexForkRunner.RunMatchedForkLadder(
            cortex, seed, launchpadArms, grammarArms, absoluteHorizons);
        for (int i = 0; i < horizons.Length; i++)
        {
            int horizon = horizons[i];
            CortexMatchedForkReceipt<CortexTapeAdmissionTrialOutcome> fork = forks[i];
            actualExecutedArmSteps = checked(actualExecutedArmSteps + fork.Left.StepSpan.ActualSteps + fork.Right.StepSpan.ActualSteps);
            terminal = fork;

            CortexTapeAdmissionTrialOutcome launchpadOutcome = fork.Left.Outcome;
            CortexTapeAdmissionTrialOutcome grammarOutcome = fork.Right.Outcome;
            CortexPolicyRuntimeReceipt launchpadPolicy = launchpadOutcome.Policy;
            CortexPolicyRuntimeReceipt grammarPolicy = grammarOutcome.Policy;
            ulong launchpadDecisions = launchpadPolicy.Decisions - source.Policy.Decisions;
            ulong grammarDecisions = grammarPolicy.Decisions - source.Policy.Decisions;
            ulong launchpadAdmits = ReadActionDelta(launchpadPolicy, source.Policy, CortexTapeAdmissionActions.Admit);
            ulong grammarAdmits = ReadActionDelta(grammarPolicy, source.Policy, CortexTapeAdmissionActions.Admit);
            ulong launchpadRejects = ReadActionDelta(launchpadPolicy, source.Policy, CortexTapeAdmissionActions.Reject);
            ulong grammarRejects = ReadActionDelta(grammarPolicy, source.Policy, CortexTapeAdmissionActions.Reject);
            ulong grammarExecutions = grammarPolicy.GrammarExecutions - source.Policy.GrammarExecutions;
            ulong grammarDivergent = grammarPolicy.DivergentGrammarExecutions - source.Policy.DivergentGrammarExecutions;
            long launchpadBytes = launchpadOutcome.TapeBytes - source.TapeBytes;
            long grammarBytes = grammarOutcome.TapeBytes - source.TapeBytes;
            int launchpadRules = CountNewRules(source.RuleIDs, launchpadOutcome.RuleIDs);
            int grammarRules = CountNewRules(source.RuleIDs, grammarOutcome.RuleIDs);
            long launchpadSavings = launchpadOutcome.Savings - source.Savings;
            long grammarSavings = grammarOutcome.Savings - source.Savings;
            long launchpadDescription = launchpadOutcome.DescriptionMbits - source.DescriptionMbits;
            long grammarDescription = grammarOutcome.DescriptionMbits - source.DescriptionMbits;
            bool exact = IsInvariantClean(in fork, in source, in launchpadOutcome, in grammarOutcome)
                && grammarExecutions <= 1;
            bool directionSafe = grammarDivergent == 0
                || grammarPolicy.LastGrammarLaunchpadAction == (int)CortexTapeAdmissionActions.Reject
                    && grammarPolicy.LastGrammarAction == (int)CortexTapeAdmissionActions.Admit;
            bool descriptionNonregression = grammarDescription <= launchpadDescription;
            bool equivalent = HasEquivalentWorld(in launchpadOutcome, in grammarOutcome);
            invariantClean &= exact;
            allDescriptionNonregression &= descriptionNonregression;
            allDirectionsSafe &= directionSafe;
            allEquivalent &= equivalent;
            strictGain |= grammarDescription < launchpadDescription;
            diverged |= grammarDivergent > 0;
            if (exact && directionSafe && descriptionNonregression) passedHorizons++;

            report.Append(horizon).Append('\t')
                  .Append(launchpadDecisions).Append('\t').Append(grammarDecisions).Append('\t')
                  .Append(launchpadAdmits).Append('\t').Append(grammarAdmits).Append('\t')
                  .Append(launchpadRejects).Append('\t').Append(grammarRejects).Append('\t')
                  .Append(launchpadDescription).Append('\t').Append(grammarDescription).Append('\t')
                  .Append(launchpadBytes).Append('\t').Append(grammarBytes).Append('\t')
                  .Append(launchpadRules).Append('\t').Append(grammarRules).Append('\t')
                  .Append(launchpadSavings).Append('\t').Append(grammarSavings).Append('\t')
                  .Append(grammarExecutions).Append('\t').Append(grammarDivergent).Append('\t')
                  .Append(directionSafe ? "yes" : "no").Append('\t')
                  .Append(exact ? "yes" : "no").Append('\t')
                  .AppendLine(descriptionNonregression ? "yes" : "no");
        }

        bool terminalExecuted = terminal is not null
            && terminal.Right.Outcome.Policy.GrammarExecutions - source.Policy.GrammarExecutions == 1;
        bool candidatePassed = invariantClean && terminalExecuted
            && (diverged
                ? allDirectionsSafe && allDescriptionNonregression && strictGain
                : allEquivalent);
        bool shuffledRejected = RunShuffledNull(
            cortex, parentRun, parentRunID, attemptID, seed, config, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), source, horizons[^1], report,
            (terminal ?? throw new InvalidOperationException("terminal tape-admission receipt was not retained")).Left,
            out CortexPolicyRuntimeReceipt shuffledPolicy,
            out bool shuffledImproved);
        bool passed = candidatePassed && shuffledRejected;
        report.Append("candidate_kind\t").AppendLine(diverged ? "adaptation" : "emulation")
              .Append("strict_gain\t").AppendLine(strictGain ? "yes" : "no")
              .Append("shuffled_rejected\t").AppendLine(shuffledRejected ? "yes" : "no")
              .Append("counterfactual_learned\t").AppendLine(shuffledImproved ? "yes" : "no")
              .Append("verdict\t").AppendLine(passed ? "pass" : "fail");
        string reportText = report.ToString();
        cortex.CurrentRun.Write(
            $"tape_admission_policy_verification_{readout.Fingerprint:X16}.tsv",
            reportText);
        cortex.CurrentRun.Write("tape_admission_policy_verification.tsv", reportText);
        int failures = horizons.Length - passedHorizons;
        if (!terminalExecuted) failures++;
        if (diverged && !strictGain) failures++;
        if (diverged && !allDirectionsSafe) failures++;
        if (diverged && !allDescriptionNonregression) failures++;
        if (!diverged && !allEquivalent) failures++;
        if (!shuffledRejected) failures++;
        cortex.CompletePolicyTrial(
            in fundingDecision,
            actualExecutedArmSteps,
            null,
            passed ? CortexPolicyVerifierOutcomes.Passed : CortexPolicyVerifierOutcomes.Failed,
            null);
        cortex.RecordPolicyOccurrenceCheck(
            PolicyID,
            readout.Fingerprint,
            horizons.Length + 1,
            passedHorizons + (shuffledRejected ? 1 : 0),
            failures,
            passed);
        if (passed && !cortex.TryGrantVerifiedPolicySuccession(
                PolicyID, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision))
            throw new InvalidOperationException("verified tape-admission policy was refused by the Cortex authority gate");
        Trace.Cortex.Boundary(
            "tape-admission.policy.verify",
            $"fp={readout.Fingerprint:X16} kind={(diverged ? "adaptation" : "emulation")} horizons={passedHorizons}/{horizons.Length} strict={(strictGain ? 1 : 0)} shuffled={(shuffledRejected ? "rejected" : "LIVE")} result={(passed ? "PASS" : "FAIL")}");
    }

    private static bool RestoreOccurrenceCheck(Cortex cortex, ulong fingerprint)
    {
        string path = cortex.CurrentRun.PathOf("tape_admission_policy_verification.tsv");
        if (!File.Exists(path)) return false;
        string[] lines = File.ReadAllLines(path);
        string expectedFingerprint = fingerprint.ToString("X16", CultureInfo.InvariantCulture);
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
                if (columns.Length >= 20
                    && string.Equals(columns[18], "yes", StringComparison.Ordinal)
                    && string.Equals(columns[19], "yes", StringComparison.Ordinal)) agreements++;
            }
        }
        if (!fingerprintMatches) return false;
        int failures = comparisons - agreements + (passed ? 0 : 1);
        cortex.RecordPolicyOccurrenceCheck(PolicyID, fingerprint, comparisons, agreements, failures, passed);
        if (passed
            && (!cortex.TryReadPolicyReadout(PolicyID, out CortexPolicyReadoutReceipt readout)
                || readout.Fingerprint != fingerprint
                || !cortex.TryGrantVerifiedPolicySuccession(
                    PolicyID, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision)))
            throw new InvalidOperationException("banked tape-admission verification was refused by the Cortex authority gate");
        Trace.Cortex.Boundary("tape-admission.policy.restore",
            $"fp={fingerprint:X16} agreement={agreements}/{comparisons} result={(passed ? "PASS" : "FAIL")}");
        return true;
    }

    private static bool RunShuffledNull(
        Cortex cortex,
        Run parentRun,
        string parentRunID,
        string attemptID,
        CortexForkSeed seed,
        CortexRunConfig config,
        CortexPolicyTrialAuthorityIdentity authorityIdentity,
        CortexTapeAdmissionTrialOutcome source,
        int horizon,
        StringBuilder report,
        CortexForkRunReceipt<CortexTapeAdmissionTrialOutcome> launchpadReceipt,
        out CortexPolicyRuntimeReceipt shuffledPolicy,
        out bool improved)
    {
        (Run shuffledRun, CortexForkMaterializationContract shuffledContract) =
            parentRun.CreateMaterializedChildRun(CortexForkRailRoles.ForcedNull, attemptID, seed.ColdSeedDigest);
        CortexForkArm<CortexTapeAdmissionTrialOutcome> shuffled = CreateArm(
            shuffledRun, config, authorityIdentity,
            CortexPolicyAuthorities.Grammar, CortexForkRailRoles.ForcedNull, parentRunID, shuffledContract,
            actionOffset: 1);
        CortexForkRunReceipt<CortexTapeAdmissionTrialOutcome> shuffledReceipt = CortexForkRunner.RunFork(
            cortex, seed, shuffled, checked(seed.NextStep + horizon));
        CortexTapeAdmissionTrialOutcome baseline = launchpadReceipt.Outcome;
        CortexTapeAdmissionTrialOutcome nullOutcome = shuffledReceipt.Outcome;
        shuffledPolicy = nullOutcome.Policy;
        ulong nullExecutions = shuffledPolicy.GrammarExecutions - source.Policy.GrammarExecutions;
        ulong nullDivergent = shuffledPolicy.DivergentGrammarExecutions - source.Policy.DivergentGrammarExecutions;
        long baselineBytes = baseline.TapeBytes - source.TapeBytes;
        long nullBytes = nullOutcome.TapeBytes - source.TapeBytes;
        int baselineRules = CountNewRules(source.RuleIDs, baseline.RuleIDs);
        int nullRules = CountNewRules(source.RuleIDs, nullOutcome.RuleIDs);
        long baselineSavings = baseline.Savings - source.Savings;
        long nullSavings = nullOutcome.Savings - source.Savings;
        long baselineDescription = baseline.DescriptionMbits - source.DescriptionMbits;
        long nullDescription = nullOutcome.DescriptionMbits - source.DescriptionMbits;
        bool exact = HaveEqualDigests(launchpadReceipt.SeedDigests, shuffledReceipt.SeedDigests)
            && launchpadReceipt.ExitCode == 0
            && shuffledReceipt.ExitCode == 0
            && launchpadReceipt.TerminalCheckpointExact
            && shuffledReceipt.TerminalCheckpointExact
            && baseline.TapeBytes >= source.TapeBytes
            && nullOutcome.TapeBytes >= source.TapeBytes
            && baseline.Policy.Outcomes - source.Policy.Outcomes
                == baseline.Policy.Decisions - source.Policy.Decisions
            && nullOutcome.Policy.Outcomes - source.Policy.Outcomes
                == nullOutcome.Policy.Decisions - source.Policy.Decisions
            && nullExecutions == 1
            && nullDivergent == 1;
        bool directionSafe = shuffledPolicy.LastGrammarLaunchpadAction == (int)CortexTapeAdmissionActions.Reject
            && shuffledPolicy.LastGrammarAction == (int)CortexTapeAdmissionActions.Admit;
        improved = exact && directionSafe && nullDescription < baselineDescription;
        bool rejected = exact && !improved;
        report.Append("shuffled\t")
              .Append(horizon).Append('\t')
              .Append("launchpad_description_mbits=").Append(baselineDescription).Append('\t')
              .Append("null_description_mbits=").Append(nullDescription).Append('\t')
              .Append("launchpad_bytes=").Append(baselineBytes).Append('\t')
              .Append("null_bytes=").Append(nullBytes).Append('\t')
              .Append("launchpad_rules=").Append(baselineRules).Append('\t')
              .Append("null_rules=").Append(nullRules).Append('\t')
              .Append("launchpad_savings=").Append(baselineSavings).Append('\t')
              .Append("null_savings=").Append(nullSavings).Append('\t')
              .Append("executions=").Append(nullExecutions).Append('\t')
              .Append("divergent=").Append(nullDivergent).Append('\t')
              .Append("direction_safe=").Append(directionSafe ? "yes" : "no").Append('\t')
              .AppendLine(rejected ? "rejected" : "survived");
        return rejected;
    }

    private static CortexForkArm<CortexTapeAdmissionTrialOutcome> CreateArm(
        Run childRun,
        CortexRunConfig config,
        CortexPolicyTrialAuthorityIdentity authorityIdentity,
        CortexPolicyAuthorities authority,
        CortexForkRailRoles role,
        string parentRunID,
        CortexForkMaterializationContract materializationContract,
        int actionOffset)
        => new(
            childRun.Dir,
            () => Cortex.CreateCheckpointRuntime(config),
            ReadOutcome,
            (Cortex trial) => trial.SetPolicyTrialAuthority(
                PolicyID,
                in authorityIdentity,
                authority,
                authority == CortexPolicyAuthorities.Grammar ? 1 : -1,
                actionOffset),
            railRole: role,
            parentRunID: parentRunID,
            materializationContract: materializationContract);

    private static CortexTapeAdmissionTrialOutcome ReadOutcome(Cortex cortex)
    {
        HashSet<RuleID> ids = new();
        GrammarRule[] rules = cortex.Grammar.Rules;
        for (int i = 0; i < rules.Length; i++) ids.Add(rules[i].Id);
        Tape tape = cortex.Tape;
        return new CortexTapeAdmissionTrialOutcome(
            cortex.ReadPolicyRuntimeReceipt(PolicyID),
            ids,
            tape.ByteLength,
            tape.Count,
            tape.RealCount,
            tape.ReplayCount,
            tape.BreachCount,
            tape.ReflectedCount,
            tape.ExecutionCount,
            cortex.Grammar.TotalSavings.Value,
            cortex.Grammar.Compressed?.Length ?? 0,
            checked(tape.ByteLength * 8000 - cortex.Grammar.TotalSavings.Value));
    }

    private static ulong ReadActionDelta(
        in CortexPolicyRuntimeReceipt after,
        in CortexPolicyRuntimeReceipt before,
        CortexTapeAdmissionActions action)
        => after.ActionExecutions[(int)action] - before.ActionExecutions[(int)action];

    private static int CountNewRules(HashSet<RuleID> source, HashSet<RuleID> after)
    {
        int count = 0;
        foreach (RuleID id in after)
            if (!source.Contains(id)) count++;
        return count;
    }

    private static bool HasEquivalentWorld(
        in CortexTapeAdmissionTrialOutcome left,
        in CortexTapeAdmissionTrialOutcome right)
        => left.TapeBytes == right.TapeBytes
            && left.TapeEvents == right.TapeEvents
            && left.RealEvents == right.RealEvents
            && left.ReplayEvents == right.ReplayEvents
            && left.BreachEvents == right.BreachEvents
            && left.ReflectedEvents == right.ReflectedEvents
            && left.ExecutionEvents == right.ExecutionEvents
            && left.Savings == right.Savings
            && left.CompressedSymbols == right.CompressedSymbols
            && left.RuleIDs.SetEquals(right.RuleIDs);

    private static bool IsInvariantClean(
        in CortexMatchedForkReceipt<CortexTapeAdmissionTrialOutcome> fork,
        in CortexTapeAdmissionTrialOutcome source,
        in CortexTapeAdmissionTrialOutcome left,
        in CortexTapeAdmissionTrialOutcome right)
        => fork.IsExact
            && fork.Left.ExitCode == 0
            && fork.Right.ExitCode == 0
            && fork.Left.TerminalCheckpointExact
            && fork.Right.TerminalCheckpointExact
            && left.TapeBytes >= source.TapeBytes
            && right.TapeBytes >= source.TapeBytes
            && left.TapeEvents >= source.TapeEvents
            && right.TapeEvents >= source.TapeEvents
            && left.Policy.Outcomes - source.Policy.Outcomes
                == left.Policy.Decisions - source.Policy.Decisions
            && right.Policy.Outcomes - source.Policy.Outcomes
                == right.Policy.Decisions - source.Policy.Decisions;

    private static bool HaveEqualDigests(in CortexForkDigests left, in CortexForkDigests right)
        => string.Equals(left.CheckpointSHA256, right.CheckpointSHA256, StringComparison.Ordinal)
            && string.Equals(left.TapeSpanlogSHA256, right.TapeSpanlogSHA256, StringComparison.Ordinal)
            && string.Equals(left.CurveSHA256, right.CurveSHA256, StringComparison.Ordinal);
}
