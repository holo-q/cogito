namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

internal readonly record struct RulerLiftPolicyCausalReceipt(
    CortexPolicyRuntimeReceipt Runtime,
    CortexPolicyDecisionReadout Decision,
    RulerLiftPendingPolicyOutcomeReceipt[] Pending,
    string Digest)
{
    internal static RulerLiftPolicyCausalReceipt Create(
        in CortexPolicyRuntimeReceipt runtime,
        in CortexPolicyDecisionReadout decision,
        RulerLiftPendingPolicyOutcomeReceipt[] pending)
    {
        StringBuilder text = new();
        text.Append((byte)runtime.Authority).Append('\t')
            .Append(runtime.CachedContexts).Append('\t').Append(runtime.ShadowComparisons).Append('\t').Append(runtime.ShadowAgreements).Append('\t')
            .Append(runtime.Decisions).Append('\t').Append(runtime.Outcomes).Append('\t')
            .Append(runtime.ActionReversals).Append('\t').Append(runtime.GrammarExecutions).Append('\t').Append(runtime.GrammarOutcomes).Append('\t')
            .Append(runtime.PaidGrammarOutcomes).Append('\t').Append(runtime.DivergentGrammarExecutions).Append('\t').Append(runtime.Readmissions).Append('\t')
            .Append(runtime.RollbackDrillPending ? 1 : 0).Append('\t').Append(runtime.RollbackDrillCompleted ? 1 : 0).Append('\t')
            .Append(runtime.LastGrammarLaunchpadAction).Append('\t').Append(runtime.LastGrammarAction).Append('\t');
        foreach (ulong count in runtime.ActionExecutions) text.Append(count).Append(',');
        text.Append('\t').Append(decision.LaunchpadAction).Append('\t').Append(decision.RawCandidateAction).Append('\t')
            .Append(decision.SelectedCandidateAction).Append('\t').Append(decision.ExecutedAction).Append('\t')
            .Append((byte)decision.Authority).Append('\t').Append(decision.GrammarRevision.Value).Append('\t').Append((byte)decision.SelectionCause);
        for (int i = 0; i < pending.Length; i++)
        {
            RulerLiftPendingPolicyOutcomeReceipt outcome = pending[i];
            CortexPolicyDecisionReadout readout = outcome.Readout;
            text.Append('\t').Append(outcome.DecisionID.Value).Append('\t').Append((byte)outcome.Action)
                .Append('\t').Append(outcome.ExactBefore).Append('\t').Append(outcome.TheoremBefore).Append('\t').Append(outcome.BenchBefore)
                .Append('\t').Append(outcome.EvaluatorBefore).Append('\t').Append(outcome.WindowsRemaining).Append('\t')
                .Append(outcome.CompletionLine.ToString("G17", CultureInfo.InvariantCulture)).Append('\t')
                .Append(readout.LaunchpadAction).Append('\t').Append(readout.RawCandidateAction).Append('\t')
                .Append(readout.SelectedCandidateAction).Append('\t').Append(readout.ExecutedAction).Append('\t')
                .Append((byte)readout.Authority).Append('\t').Append(readout.GrammarRevision.Value).Append('\t').Append((byte)readout.SelectionCause);
        }
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        return new(runtime, decision, pending, digest);
    }

    public bool ChangedFrom(in RulerLiftPolicyCausalReceipt baseline) => !string.Equals(Digest, baseline.Digest, StringComparison.Ordinal);

    internal RulerLiftPolicyCausalDelta DeltaFrom(in RulerLiftPolicyCausalReceipt baseline)
    {
        CortexPolicyRuntimeReceipt before = baseline.Runtime;
        CortexPolicyRuntimeReceipt after = Runtime;
        ulong[] executionDeltas = new ulong[Math.Max(before.ActionExecutions.Length, after.ActionExecutions.Length)];
        for (int i = 0; i < executionDeltas.Length; i++)
        {
            ulong prior = i < before.ActionExecutions.Length ? before.ActionExecutions[i] : 0;
            ulong current = i < after.ActionExecutions.Length ? after.ActionExecutions[i] : 0;
            executionDeltas[i] = current >= prior ? current - prior : ulong.MaxValue;
        }
        bool readoutChanged = !Decision.Equals(baseline.Decision);
        bool pendingChanged = !Pending.SequenceEqual(baseline.Pending);
        return new(
            Runtime.Authority != baseline.Runtime.Authority,
            CheckedDelta(after.CachedContexts, before.CachedContexts),
            after.RollbackDrillPending != before.RollbackDrillPending,
            after.RollbackDrillCompleted != before.RollbackDrillCompleted,
            CheckedDelta(after.LastGrammarLaunchpadAction, before.LastGrammarLaunchpadAction),
            CheckedDelta(after.LastGrammarAction, before.LastGrammarAction),
            readoutChanged,
            pendingChanged,
            CheckedDelta(after.ShadowComparisons, before.ShadowComparisons),
            CheckedDelta(after.ShadowAgreements, before.ShadowAgreements),
            CheckedDelta(after.Decisions, before.Decisions),
            CheckedDelta(after.Outcomes, before.Outcomes),
            CheckedDelta(after.GrammarExecutions, before.GrammarExecutions),
            CheckedDelta(after.GrammarOutcomes, before.GrammarOutcomes),
            CheckedDelta(after.PaidGrammarOutcomes, before.PaidGrammarOutcomes),
            CheckedDelta(after.DivergentGrammarExecutions, before.DivergentGrammarExecutions),
            CheckedDelta(after.Readmissions, before.Readmissions),
            CheckedDelta(after.ActionReversals, before.ActionReversals),
            executionDeltas,
            Digest);
    }

    private static int CheckedDelta(int after, int before) => checked(after - before);
    private static long CheckedDelta(long after, long before) => checked(after - before);
    private static ulong CheckedDelta(ulong after, ulong before) => checked(after - before);
}

internal readonly record struct RulerLiftPolicyCausalDelta(
    bool AuthorityChanged,
    int CachedContexts,
    bool RollbackDrillPendingChanged,
    bool RollbackDrillCompletedChanged,
    int LastGrammarLaunchpadActionDelta,
    int LastGrammarActionDelta,
    bool ReadoutChanged,
    bool PendingChanged,
    int ShadowComparisons,
    int ShadowAgreements,
    ulong Decisions,
    ulong Outcomes,
    ulong GrammarExecutions,
    ulong GrammarOutcomes,
    ulong PaidGrammarOutcomes,
    ulong DivergentGrammarExecutions,
    int Readmissions,
    int ActionReversals,
    ulong[] ActionExecutionDeltas,
    string OutcomeDigest)
{
    public bool Changed => AuthorityChanged || CachedContexts != 0
        || RollbackDrillPendingChanged || RollbackDrillCompletedChanged
        || LastGrammarLaunchpadActionDelta != 0 || LastGrammarActionDelta != 0
        || ReadoutChanged || PendingChanged
        || ShadowComparisons != 0 || ShadowAgreements != 0 || Decisions != 0 || Outcomes != 0
        || GrammarExecutions != 0 || GrammarOutcomes != 0 || PaidGrammarOutcomes != 0
        || DivergentGrammarExecutions != 0 || Readmissions != 0 || ActionReversals != 0
        || ActionExecutionDeltas.Any(static delta => delta != 0);

    public bool SameAs(in RulerLiftPolicyCausalDelta other)
        => AuthorityChanged == other.AuthorityChanged && CachedContexts == other.CachedContexts
        && RollbackDrillPendingChanged == other.RollbackDrillPendingChanged
        && RollbackDrillCompletedChanged == other.RollbackDrillCompletedChanged
        && LastGrammarLaunchpadActionDelta == other.LastGrammarLaunchpadActionDelta
        && LastGrammarActionDelta == other.LastGrammarActionDelta
        && ReadoutChanged == other.ReadoutChanged
        && PendingChanged == other.PendingChanged && ShadowComparisons == other.ShadowComparisons
        && ShadowAgreements == other.ShadowAgreements && Decisions == other.Decisions && Outcomes == other.Outcomes
        && GrammarExecutions == other.GrammarExecutions && GrammarOutcomes == other.GrammarOutcomes
        && PaidGrammarOutcomes == other.PaidGrammarOutcomes && DivergentGrammarExecutions == other.DivergentGrammarExecutions
        && Readmissions == other.Readmissions && ActionReversals == other.ActionReversals
        && ActionExecutionDeltas.SequenceEqual(other.ActionExecutionDeltas);
}

internal readonly record struct RulerLiftPolicySnapshot(
    CortexPolicyAuthorities Authority,
    int CompletedWindows,
    int ExactClasses,
    int TheoremClasses,
    int BenchCaptures,
    long EvaluatorCalls,
    int Ruler,
    int Lifts,
    bool HistoryComplete,
    RulerLiftPolicyCausalReceipt Causal);

internal readonly record struct RulerLiftTrialOutcome(
    RulerLiftPolicySnapshot Discovery,
    int ActionReversals,
    ulong GrammarExecutions,
    ulong DivergentGrammarExecutions,
    int LaunchpadAction,
    int GrammarAction,
    EmlAnytimeCurvePoint[] AnytimePoints);

internal readonly record struct EmlAnytimeAlignedPrefixReceipt(
    int WindowIndex,
    int PrefixStep,
    EmlAnytimeCommitments LaunchpadQuality,
    EmlAnytimeCommitments GrammarQuality,
    long LaunchpadEvaluatorIntervals,
    long GrammarEvaluatorIntervals,
    bool AccountingExact,
    bool EvidenceExact,
    bool FrontierMonotonic,
    bool IntermediateRegression,
    string LaunchpadPointID,
    string GrammarPointID)
{
    public bool Passed => AccountingExact && EvidenceExact && FrontierMonotonic && !IntermediateRegression;
}

internal enum RulerLiftCandidateKinds : byte
{
    Emulation,
    Adaptation,
}

internal sealed class RulerLiftAutonomyReward : CortexReward
{
    public override void OnStepCompleted(Cortex cortex, int step)
    {
        if (!cortex.AllowsAutonomicSpawning) return;
        ReplayCalc sourceReplay = ResolveReplay(cortex);
        if (!sourceReplay.HasRulerLift) return;
        if (!cortex.TryReadPolicyReadout(RulerLift.PolicyID, out CortexPolicyReadoutReceipt readout)
            || readout.CandidateFingerprint == 0
            || !readout.IsExact
            || cortex.HasPolicyOccurrenceCheck(
                policy: RulerLift.PolicyID,
                readoutFingerprint: readout.Fingerprint,
                candidateFingerprint: readout.CandidateFingerprint,
                revision: readout.Revision,
                passed: out _)) return;
        int comparisons = readout.Comparisons;

        RulerLiftPolicySnapshot source = sourceReplay.ReadRulerLiftPolicySnapshot(cortex);
        int sourceReversals = cortex.ReadPolicyActionReversals(RulerLift.PolicyID);
        (ulong sourceGrammar, ulong sourceDivergent, _, _) = cortex.ReadPolicyGrammarExecutions(RulerLift.PolicyID);
        bool causalFixture = VerifyCausalClassificationFixture();
        int interval = Math.Max(1, sourceReplay.RulerWindowInterval);
        int fundedHorizon = checked(interval * 3 + 1);
        if (!cortex.TryFundPolicyTrial(
                RulerLift.PolicyID,
                CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                fundedHorizon,
                armCount: 2,
                out CortexPolicyTrialQuotaDecision fundingDecision)) return;
        CortexForkSeed seed = cortex.MaterializeCompletedStepForkSeed();
        CortexRunConfig config = cortex.Config.ToRunConfig(null);
        Run parentRun = cortex.CurrentRun;
        string parentRunID = Path.GetFileName(Path.GetFullPath(parentRun.Dir));
        string attemptID = fundingDecision.QuotaDecisionID.ToString();
        int nextCloseStep = checked(((seed.NextStep + interval - 1) / interval) * interval);
        int[] closeCounts = [1, 3];
        StringBuilder report = new();
        StringBuilder prefixReport = new("close\twindow\tprefix_step\tlaunchpad_point_id\tgrammar_point_id\tlaunchpad_evaluator_intervals\tgrammar_evaluator_intervals\tlaunchpad_frontier\tgrammar_frontier\taccounting_exact\tevidence_exact\tfrontier_monotonic\tintermediate_regression\tverdict\n");
        StringBuilder causalReport = new("close\tlaunchpad_delta_digest\tgrammar_delta_digest\tcausal_delta_equal\tlaunchpad_changed\tgrammar_changed\tlaunchpad_authority_changed\tgrammar_authority_changed\tlaunchpad_cached_contexts\tgrammar_cached_contexts\tlaunchpad_rollback_pending\tgrammar_rollback_pending\tlaunchpad_rollback_completed\tgrammar_rollback_completed\tlaunchpad_last_grammar_launchpad_action\tgrammar_last_grammar_launchpad_action\tlaunchpad_last_grammar_action\tgrammar_last_grammar_action\tlaunchpad_readout_changed\tgrammar_readout_changed\tlaunchpad_pending_changed\tgrammar_pending_changed\n");
        StringBuilder budgetReport = new("close\tbudget\tlaunchpad_spend\tlaunchpad_slack\tgrammar_spend\tgrammar_slack\tlaunchpad_frontier\tgrammar_frontier\tcomparable\tstep_function\tright_no_worse\tstrict_later_gain\tpoint_ids\n");
        report.Append("policy\t").AppendLine(RulerLift.PolicyID.Value)
              .Append("fingerprint\t").AppendLine(readout.Fingerprint.ToString("X16", CultureInfo.InvariantCulture))
              .Append("seed_step\t").AppendLine(seed.NextStep.ToString(CultureInfo.InvariantCulture))
              .AppendLine("close\tlaunchpad_windows\tgrammar_windows\tlaunchpad_calls\tgrammar_calls\tlaunchpad_exact\tgrammar_exact\tlaunchpad_theorems\tgrammar_theorems\tlaunchpad_bench\tgrammar_bench\tlaunchpad_ruler\tgrammar_ruler\tlaunchpad_lifts\tgrammar_lifts\tlaunchpad_authority\tgrammar_authority\texecuted_launchpad_action\texecuted_grammar_action\tgrammar_executions\tgrammar_divergent\tseed_relation\tinitial_cross_arm_matched\tcross_arm_inequality_expected\tleft_continuity_exact\tright_continuity_exact\tleft_checkpoint_exact\tright_checkpoint_exact\tleft_horizon_exact\tright_horizon_exact\thistory_exact\tlaunchpad_planned_steps\tgrammar_planned_steps\tlaunchpad_actual_steps\tgrammar_actual_steps\tlaunchpad_seed_wall_ms\tgrammar_seed_wall_ms\tlaunchpad_run_wall_ms\tgrammar_run_wall_ms\tlaunchpad_verifier_wall_ms\tgrammar_verifier_wall_ms\tpair_wall_ms\tpareto\tcausal_delta_equal\tbudget_digest\tbudget_comparable\tbudget_step_function\tbudget_strict_later_gain");

        bool invariantClean = true;
        bool allPareto = true;
        bool allEquivalent = true;
        bool strictGain = false;
        bool diverged = false;
        RulerLiftCandidateKinds candidateKind = RulerLiftCandidateKinds.Emulation;
        int passedCloses = 0;
        int terminalLaunchpadReversals = 0;
        int terminalGrammarReversals = 0;
        long actualExecutedArmSteps = 0;
        long evaluatorWorkUnits = 0;
        long forkWallMilliseconds = 0;
        bool anytimePrefixClean = true;
        string anytimePrefixKillReason = "";
        CortexForkArm<RulerLiftTrialOutcome>[] launchpadArms = new CortexForkArm<RulerLiftTrialOutcome>[closeCounts.Length];
        CortexForkArm<RulerLiftTrialOutcome>[] grammarArms = new CortexForkArm<RulerLiftTrialOutcome>[closeCounts.Length];
        int[] absoluteHorizons = new int[closeCounts.Length];
        for (int i = 0; i < closeCounts.Length; i++)
        {
            int closeCount = closeCounts[i];
            absoluteHorizons[i] = checked(nextCloseStep + (closeCount - 1) * interval + 1);
            (Run launchpadRun, CortexForkMaterializationContract launchpadContract) =
                parentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.Baseline, attemptID, seed.ColdSeedDigest);
            (Run grammarRun, CortexForkMaterializationContract grammarContract) =
                parentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.Candidate, attemptID, seed.ColdSeedDigest);
            launchpadArms[i] = new CortexForkArm<RulerLiftTrialOutcome>(
                launchpadRun.Dir,
                () => Cortex.CreateCheckpointRuntime(config),
                ReadOutcome,
                (Cortex trial) => trial.SetPolicyTrialAuthority(
                    RulerLift.PolicyID,
                    CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                    CortexPolicyAuthorities.Shadow),
                railRole: CortexForkRailRoles.Baseline,
                parentRunID: parentRunID,
                materializationContract: launchpadContract);
            grammarArms[i] = new CortexForkArm<RulerLiftTrialOutcome>(
                grammarRun.Dir,
                () => Cortex.CreateCheckpointRuntime(config),
                ReadOutcome,
                (Cortex trial) => trial.SetPolicyTrialAuthority(
                    RulerLift.PolicyID,
                    CortexPolicyTrialAuthorityIdentity.FromReadout(in readout),
                    CortexPolicyAuthorities.Grammar,
                    grammarExecutionQuota: 1),
                railRole: CortexForkRailRoles.Candidate,
                parentRunID: parentRunID,
                materializationContract: grammarContract);
        }
        List<CortexMatchedForkReceipt<RulerLiftTrialOutcome>> forks = CortexForkRunner.RunMatchedForkLadder(
            cortex, seed, launchpadArms, grammarArms, absoluteHorizons);
        for (int i = 0; i < closeCounts.Length; i++)
        {
            int closeCount = closeCounts[i];
            CortexMatchedForkReceipt<RulerLiftTrialOutcome> fork = forks[i];

            RulerLiftTrialOutcome launchpadOutcome = fork.Left.Outcome;
            RulerLiftTrialOutcome grammarOutcome = fork.Right.Outcome;
            RulerLiftPolicySnapshot launchpadDiscovery = launchpadOutcome.Discovery;
            RulerLiftPolicySnapshot grammarDiscovery = grammarOutcome.Discovery;
            List<EmlAnytimeAlignedPrefixReceipt> alignedPrefixes = AlignAnytimePrefixes(
                launchpadOutcome.AnytimePoints, grammarOutcome.AnytimePoints, closeCount, prefixReport, closeCount);
            if (alignedPrefixes.Count != closeCount)
            {
                anytimePrefixClean = false;
                anytimePrefixKillReason = "missing-aligned-prefix";
            }
            foreach (EmlAnytimeAlignedPrefixReceipt prefix in alignedPrefixes)
                if (!prefix.Passed)
                {
                    anytimePrefixClean = false;
                    anytimePrefixKillReason = prefix.IntermediateRegression ? "intermediate-regression"
                        : !prefix.EvidenceExact ? "evidence-regression"
                        : !prefix.AccountingExact ? "budget-overrun"
                        : "frontier-regression";
                    break;
                }
            int launchpadWindows = launchpadDiscovery.CompletedWindows - source.CompletedWindows;
            int grammarWindows = grammarDiscovery.CompletedWindows - source.CompletedWindows;
            long launchpadCalls = launchpadDiscovery.EvaluatorCalls - source.EvaluatorCalls;
            long grammarCalls = grammarDiscovery.EvaluatorCalls - source.EvaluatorCalls;
            int launchpadExact = launchpadDiscovery.ExactClasses - source.ExactClasses;
            int grammarExact = grammarDiscovery.ExactClasses - source.ExactClasses;
            int launchpadTheorems = launchpadDiscovery.TheoremClasses - source.TheoremClasses;
            int grammarTheorems = grammarDiscovery.TheoremClasses - source.TheoremClasses;
            int launchpadBench = launchpadDiscovery.BenchCaptures - source.BenchCaptures;
            int grammarBench = grammarDiscovery.BenchCaptures - source.BenchCaptures;
            ulong grammarExecutions = grammarOutcome.GrammarExecutions - sourceGrammar;
            ulong grammarDivergent = grammarOutcome.DivergentGrammarExecutions - sourceDivergent;
            int launchpadReversals = launchpadOutcome.ActionReversals - sourceReversals;
            int grammarReversals = grammarOutcome.ActionReversals - sourceReversals;
            RulerLiftPolicyCausalDelta launchpadCausal = launchpadDiscovery.Causal.DeltaFrom(source.Causal);
            RulerLiftPolicyCausalDelta grammarCausal = grammarDiscovery.Causal.DeltaFrom(source.Causal);
            EmlAnytimeBudgetComparison budgetComparison = EmlAnytimeBudgetComparator.Compare(
                launchpadOutcome.AnytimePoints.Select(ToBudgetPoint).ToArray(),
                grammarOutcome.AnytimePoints.Select(ToBudgetPoint).ToArray());
            bool exact = fork.IsExact
                && fork.Left.ExitCode == 0
                && fork.Right.ExitCode == 0
                && launchpadDiscovery.HistoryComplete
                && grammarDiscovery.HistoryComplete
                && launchpadWindows == closeCount
                && grammarWindows == closeCount
                && launchpadCalls >= 0
                && grammarCalls >= 0
                && launchpadExact >= 0
                && grammarExact >= 0
                && launchpadTheorems >= 0
                && grammarTheorems >= 0
                && launchpadBench >= 0
                && grammarBench >= 0
                && grammarExecutions <= 1
                && anytimePrefixClean;
            bool causalStateChanged = !launchpadCausal.SameAs(grammarCausal);
            if (causalStateChanged) candidateKind = RulerLiftCandidateKinds.Adaptation;
            bool pareto = budgetComparison.Comparable && budgetComparison.RightNoWorse;
            bool equivalent = budgetComparison.Comparable
                && budgetComparison.Alignments.All(static alignment => alignment.Passed
                    && alignment.LeftQuality == alignment.RightQuality);
            invariantClean &= exact;
            allPareto &= pareto;
            allEquivalent &= equivalent;
            strictGain |= budgetComparison.StrictLaterGain;
            diverged |= grammarDivergent > 0 || causalStateChanged;
            if (exact && pareto) passedCloses++;
            terminalLaunchpadReversals = launchpadReversals;
            terminalGrammarReversals = grammarReversals;
            actualExecutedArmSteps = checked(actualExecutedArmSteps
                + fork.Left.StepSpan.ActualSteps
                + fork.Right.StepSpan.ActualSteps);
            evaluatorWorkUnits = checked(evaluatorWorkUnits + launchpadCalls + grammarCalls);
            forkWallMilliseconds = checked(forkWallMilliseconds + fork.Timing.ParallelWallMilliseconds);

            report.Append(closeCount).Append('\t')
                  .Append(launchpadWindows).Append('\t').Append(grammarWindows).Append('\t')
                  .Append(launchpadCalls).Append('\t').Append(grammarCalls).Append('\t')
                  .Append(launchpadExact).Append('\t').Append(grammarExact).Append('\t')
                  .Append(launchpadTheorems).Append('\t').Append(grammarTheorems).Append('\t')
                  .Append(launchpadBench).Append('\t').Append(grammarBench).Append('\t')
                  .Append(launchpadDiscovery.Ruler).Append('\t').Append(grammarDiscovery.Ruler).Append('\t')
                  .Append(launchpadDiscovery.Lifts).Append('\t').Append(grammarDiscovery.Lifts).Append('\t')
                  .Append(launchpadDiscovery.Authority).Append('\t').Append(grammarDiscovery.Authority).Append('\t')
                  .Append(grammarOutcome.LaunchpadAction).Append('\t').Append(grammarOutcome.GrammarAction).Append('\t')
                  .Append(grammarExecutions).Append('\t').Append(grammarDivergent).Append('\t')
                  .Append(fork.SeedRelation.Kind).Append('\t')
                  .Append(fork.SeedRelation.InitialCrossArmMatched?.ToString().ToLowerInvariant() ?? "na").Append('\t')
                  .Append(fork.SeedRelation.Kind == CortexForkSeedRelations.PerArmContinuation ? "yes" : "no").Append('\t')
                  .Append(fork.SeedRelation.LeftContinuityExact ? "yes" : "no").Append('\t')
                  .Append(fork.SeedRelation.RightContinuityExact ? "yes" : "no").Append('\t')
                  .Append(fork.Left.TerminalCheckpointExact ? "yes" : "no").Append('\t')
                  .Append(fork.Right.TerminalCheckpointExact ? "yes" : "no").Append('\t')
                  .Append(fork.Left.StepSpan.ReachedPlannedNextStep ? "yes" : "no").Append('\t')
                  .Append(fork.Right.StepSpan.ReachedPlannedNextStep ? "yes" : "no").Append('\t')
                  .Append((launchpadDiscovery.HistoryComplete && grammarDiscovery.HistoryComplete) ? "yes" : "no").Append('\t')
                  .Append(fork.Left.StepSpan.PlannedSteps).Append('\t')
                  .Append(fork.Right.StepSpan.PlannedSteps).Append('\t')
                  .Append(fork.Left.StepSpan.ActualSteps).Append('\t')
                  .Append(fork.Right.StepSpan.ActualSteps).Append('\t')
                  .Append(fork.Left.Timing.SeedIOWallMilliseconds).Append('\t')
                  .Append(fork.Right.Timing.SeedIOWallMilliseconds).Append('\t')
                  .Append(fork.Left.Timing.ExecutionWallMilliseconds).Append('\t')
                  .Append(fork.Right.Timing.ExecutionWallMilliseconds).Append('\t')
                  .Append(fork.Left.Timing.TerminalVerifierWallMilliseconds).Append('\t')
                  .Append(fork.Right.Timing.TerminalVerifierWallMilliseconds).Append('\t')
                  .Append(fork.Timing.ParallelWallMilliseconds).Append('\t')
                  .Append(pareto ? "yes" : "no").Append('\t')
                  .Append(!causalStateChanged ? "yes" : "no").Append('\t')
                  .Append(budgetComparison.Digest).Append('\t')
                  .Append(budgetComparison.Comparable ? "yes" : "no").Append('\t')
                  .Append(budgetComparison.StepFunction ? "yes" : "no").Append('\t')
                  .AppendLine(budgetComparison.StrictLaterGain ? "yes" : "no");
            causalReport.Append(closeCount).Append('\t').Append(launchpadCausal.OutcomeDigest).Append('\t').Append(grammarCausal.OutcomeDigest).Append('\t')
                .Append(launchpadCausal.SameAs(grammarCausal) ? "yes" : "no").Append('\t')
                .Append(launchpadCausal.Changed ? "yes" : "no").Append('\t').Append(grammarCausal.Changed ? "yes" : "no").Append('\t')
                .Append(launchpadCausal.AuthorityChanged ? "yes" : "no").Append('\t').Append(grammarCausal.AuthorityChanged ? "yes" : "no").Append('\t')
                .Append(launchpadCausal.CachedContexts).Append('\t').Append(grammarCausal.CachedContexts).Append('\t')
                .Append(launchpadCausal.RollbackDrillPendingChanged ? "yes" : "no").Append('\t').Append(grammarCausal.RollbackDrillPendingChanged ? "yes" : "no").Append('\t')
                .Append(launchpadCausal.RollbackDrillCompletedChanged ? "yes" : "no").Append('\t').Append(grammarCausal.RollbackDrillCompletedChanged ? "yes" : "no").Append('\t')
                .Append(launchpadCausal.LastGrammarLaunchpadActionDelta).Append('\t').Append(grammarCausal.LastGrammarLaunchpadActionDelta).Append('\t')
                .Append(launchpadCausal.LastGrammarActionDelta).Append('\t').Append(grammarCausal.LastGrammarActionDelta).Append('\t')
                .Append(launchpadCausal.ReadoutChanged ? "yes" : "no").Append('\t').Append(grammarCausal.ReadoutChanged ? "yes" : "no").Append('\t')
                .Append(launchpadCausal.PendingChanged ? "yes" : "no").Append('\t').AppendLine(grammarCausal.PendingChanged ? "yes" : "no");
            foreach (EmlAnytimeBudgetAlignment alignment in budgetComparison.Alignments)
                budgetReport.Append(closeCount).Append('\t').Append(alignment.Budget).Append('\t').Append(alignment.LeftSpend).Append('\t').Append(alignment.LeftSlack).Append('\t')
                    .Append(alignment.RightSpend).Append('\t').Append(alignment.RightSlack).Append('\t').Append(alignment.LeftQuality.Total).Append('\t').Append(alignment.RightQuality.Total).Append('\t')
                    .Append(budgetComparison.Comparable ? "yes" : "no").Append('\t').Append(budgetComparison.StepFunction ? "yes" : "no").Append('\t')
                    .Append(alignment.RightNoWorse ? "yes" : "no").Append('\t').Append(alignment.StrictLaterGain ? "yes" : "no").Append('\t')
                    .Append(alignment.LeftPointID).Append('|').AppendLine(alignment.RightPointID);
        }

        bool reversalDebtRepaid = terminalGrammarReversals <= terminalLaunchpadReversals;
        bool passed = causalFixture && invariantClean && reversalDebtRepaid
            && (diverged ? allPareto && strictGain : allEquivalent);
        CortexPolicyTrialCompletion settlement = cortex.CompletePolicyTrial(
            in fundingDecision,
            actualExecutedArmSteps,
            evaluatorWorkUnits,
            passed ? CortexPolicyVerifierOutcomes.Passed : CortexPolicyVerifierOutcomes.Failed,
            forkWallMilliseconds);
        report.Append("funding_decision_id\t").AppendLine(fundingDecision.QuotaDecisionID.ToString())
              .Append("funding_planned_arm_steps\t").AppendLine(fundingDecision.PlannedArmSteps.ToString(CultureInfo.InvariantCulture))
              .Append("funding_reserved_arm_steps\t").AppendLine(fundingDecision.HeldArmSteps.ToString(CultureInfo.InvariantCulture))
              .Append("funding_charged_steps\t").AppendLine(fundingDecision.UsedSteps.ToString(CultureInfo.InvariantCulture))
              .Append("settlement_actual_arm_steps\t").AppendLine(settlement.ActualExecutedArmSteps.ToString(CultureInfo.InvariantCulture))
              .Append("settlement_refund_or_slack\t").AppendLine(settlement.ReclaimedOrUnused.ToString(CultureInfo.InvariantCulture))
              .Append("settlement_evaluator_work_units\t").AppendLine(settlement.EvaluatorWorkUnits?.ToString(CultureInfo.InvariantCulture) ?? "na")
              .Append("settlement_verifier_outcome\t").AppendLine(settlement.VerifierOutcome.ToString())
              .Append("settlement_wall_ms\t").AppendLine(settlement.WallMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "na");
        report.Append("candidate_kind\t").AppendLine(candidateKind.ToString().ToLowerInvariant())
              .Append("strict_gain\t").AppendLine(strictGain ? "yes" : "no")
              .Append("causal_fixture\t").AppendLine(causalFixture ? "pass" : "fail")
              .Append("anytime_prefix_clean\t").AppendLine(anytimePrefixClean ? "yes" : "no")
              .Append("anytime_prefix_kill_reason\t").AppendLine(string.IsNullOrWhiteSpace(anytimePrefixKillReason) ? "none" : anytimePrefixKillReason)
              .Append("reversal_debt_repaid\t").AppendLine(reversalDebtRepaid ? "yes" : "no")
              .Append("verdict\t").AppendLine(passed ? "pass" : "fail");
        cortex.CurrentRun.Write("ruler_policy_verification.tsv", report.ToString());
        cortex.CurrentRun.Write("ruler_policy_anytime_prefix.tsv", prefixReport.ToString());
        cortex.CurrentRun.Write("ruler_policy_causal.tsv", causalReport.ToString());
        cortex.CurrentRun.Write("ruler_policy_budget.tsv", budgetReport.ToString());
        int failures = closeCounts.Length - passedCloses;
        if (!causalFixture) failures++;
        if (!reversalDebtRepaid) failures++;
        if (diverged && !strictGain) failures++;
        if (!diverged && !allEquivalent) failures++;
        cortex.RecordPolicyOccurrenceCheck(
            RulerLift.PolicyID,
            readout.Fingerprint,
            closeCounts.Length,
            passedCloses,
            failures,
            passed);
        if (passed && !cortex.TryGrantVerifiedPolicySuccession(
                RulerLift.PolicyID, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision))
            throw new InvalidOperationException("verified RulerLift policy was refused by the Cortex authority gate");
        Trace.Cortex.Boundary(
            "ruler.policy.verify",
            $"fp={readout.Fingerprint:X16} kind={candidateKind.ToString().ToLowerInvariant()} closes={passedCloses}/{closeCounts.Length} strict={(strictGain ? 1 : 0)} reversal-debt={(reversalDebtRepaid ? "repaid" : "open")} result={(passed ? "PASS" : "FAIL")}");
    }

    private static RulerLiftTrialOutcome ReadOutcome(Cortex cortex)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        (ulong grammar, ulong divergent, int launchpadAction, int grammarAction) =
            cortex.ReadPolicyGrammarExecutions(RulerLift.PolicyID);
        return new RulerLiftTrialOutcome(
            dream.ReadRulerLiftPolicySnapshot(cortex),
            cortex.ReadPolicyActionReversals(RulerLift.PolicyID),
            grammar,
            divergent,
            launchpadAction,
            grammarAction,
            dream.AnytimeCurve.Points.ToArray());
    }

    private static List<EmlAnytimeAlignedPrefixReceipt> AlignAnytimePrefixes(
        EmlAnytimeCurvePoint[] launchpad,
        EmlAnytimeCurvePoint[] grammar,
        int expectedCount,
        StringBuilder report,
        int close)
    {
        List<EmlAnytimeAlignedPrefixReceipt> aligned = new(Math.Min(launchpad.Length, grammar.Length));
        if (launchpad.Length != expectedCount || grammar.Length != expectedCount) return aligned;
        EmlAnytimeCurvePoint? priorLaunchpad = null;
        EmlAnytimeCurvePoint? priorGrammar = null;
        for (int i = 0; i < expectedCount; i++)
        {
            EmlAnytimeCurvePoint left = launchpad[i];
            EmlAnytimeCurvePoint right = grammar[i];
            bool coordinates = left.WindowIndex == right.WindowIndex && left.PrefixStep == right.PrefixStep;
            bool accounting = coordinates && CountsWithinBudget(left.WindowActualFuel, left.WindowPlannedFuel)
                && CountsWithinBudget(right.WindowActualFuel, right.WindowPlannedFuel)
                && left.WindowEvaluatorIntervals <= left.WindowPlannedEvaluatorIntervals
                && right.WindowEvaluatorIntervals <= right.WindowPlannedEvaluatorIntervals
                && left.EvaluatorIntervals >= (priorLaunchpad?.EvaluatorIntervals ?? 0)
                && right.EvaluatorIntervals >= (priorGrammar?.EvaluatorIntervals ?? 0);
            bool evidence = coordinates && left.EvidenceVerified && right.EvidenceVerified
                && left.VerifyDigest() && right.VerifyDigest();
            bool frontier = coordinates
                && (priorLaunchpad is null || left.Quality.Dominates(priorLaunchpad.Value.Quality))
                && (priorGrammar is null || right.Quality.Dominates(priorGrammar.Value.Quality));
            bool regression = coordinates && !right.Quality.Dominates(left.Quality);
            EmlAnytimeAlignedPrefixReceipt receipt = new(left.WindowIndex, left.PrefixStep, left.Quality, right.Quality,
                left.EvaluatorIntervals, right.EvaluatorIntervals, accounting, evidence, frontier, regression,
                left.PointID, right.PointID);
            aligned.Add(receipt);
            report.Append(close).Append('\t').Append(receipt.WindowIndex).Append('\t').Append(receipt.PrefixStep).Append('\t')
                .Append(receipt.LaunchpadPointID).Append('\t').Append(receipt.GrammarPointID).Append('\t')
                .Append(receipt.LaunchpadEvaluatorIntervals).Append('\t').Append(receipt.GrammarEvaluatorIntervals).Append('\t')
                .Append(receipt.LaunchpadQuality.Total).Append('\t').Append(receipt.GrammarQuality.Total).Append('\t')
                .Append(receipt.AccountingExact ? "yes" : "no").Append('\t').Append(receipt.EvidenceExact ? "yes" : "no").Append('\t')
                .Append(receipt.FrontierMonotonic ? "yes" : "no").Append('\t').Append(receipt.IntermediateRegression ? "yes" : "no")
                .Append('\t').Append(receipt.Passed ? "pass" : "fail").Append('\n');
            priorLaunchpad = left;
            priorGrammar = right;
        }
        return aligned;
    }

    private static bool CountsWithinBudget(in EmlDeliberationCounts actual, in EmlDeliberationCounts planned)
    {
        try
        {
            EmlDeliberationCounts delta = EmlDeliberationCounts.Subtract(in planned, in actual);
            delta.ValidateNonnegative("aligned anytime slack");
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static EmlAnytimeBudgetPoint ToBudgetPoint(EmlAnytimeCurvePoint point)
    {
        long priorActual = checked(point.EvaluatorIntervals - point.WindowEvaluatorIntervals);
        long planned = checked(priorActual + point.WindowPlannedEvaluatorIntervals);
        return new(
            point.EvaluatorIntervals,
            planned,
            point.EvaluatorIntervals,
            point.Quality,
            CountsWithinBudget(point.WindowActualFuel, point.WindowPlannedFuel)
                && point.WindowEvaluatorIntervals <= point.WindowPlannedEvaluatorIntervals,
            point.EvidenceVerified && point.VerifyDigest(),
            point.PointID);
    }

    internal static bool VerifyCausalClassificationFixture(TextWriter? output = null)
    {
        CortexPolicyRuntimeReceipt baselineRuntime = MakeCausalFixtureRuntime();
        RulerLiftPolicyCausalReceipt baseline = RulerLiftPolicyCausalReceipt.Create(
            in baselineRuntime, default, []);
        RulerLiftPolicyCausalDelta unchanged = baseline.DeltaFrom(baseline);
        if (unchanged.Changed || !unchanged.SameAs(unchanged)) return false;

        bool adapted = true;
        adapted &= VerifyCausalFixtureVariant(baseline, baselineRuntime with { CachedContexts = 1 });
        adapted &= VerifyCausalFixtureVariant(baseline, baselineRuntime with { RollbackDrillPending = true });
        adapted &= VerifyCausalFixtureVariant(baseline, baselineRuntime with { RollbackDrillCompleted = true });
        adapted &= VerifyCausalFixtureVariant(baseline, baselineRuntime with { LastGrammarLaunchpadAction = 1 });
        adapted &= VerifyCausalFixtureVariant(baseline, baselineRuntime with { LastGrammarAction = 1 });

        CortexPolicyRuntimeReceipt costOnlyRuntime = baselineRuntime with { ConservedCost = 1 };
        RulerLiftPolicyCausalReceipt costOnly = RulerLiftPolicyCausalReceipt.Create(
            in costOnlyRuntime, default, []);
        RulerLiftPolicyCausalDelta costDelta = costOnly.DeltaFrom(baseline);
        bool accountingOnly = !costOnly.ChangedFrom(baseline)
            && !costDelta.Changed
            && costDelta.SameAs(unchanged);

        CortexPolicyRuntimeReceipt featuresOnlyRuntime = baselineRuntime with { LastGrammarFeatures = [42.0] };
        RulerLiftPolicyCausalReceipt featuresOnly = RulerLiftPolicyCausalReceipt.Create(
            in featuresOnlyRuntime, default, []);
        RulerLiftPolicyCausalDelta featuresDelta = featuresOnly.DeltaFrom(baseline);
        bool telemetryOnly = !featuresOnly.ChangedFrom(baseline)
            && !featuresDelta.Changed
            && featuresDelta.SameAs(unchanged);
        bool passed = adapted && accountingOnly && telemetryOnly;
        output?.WriteLine($"  ruler causal fixture · adapted={(adapted ? "yes" : "no")} accounting_only={(accountingOnly ? "yes" : "no")} telemetry_only={(telemetryOnly ? "yes" : "no")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyCausalFixtureVariant(
        in RulerLiftPolicyCausalReceipt baseline,
        in CortexPolicyRuntimeReceipt runtime)
    {
        RulerLiftPolicyCausalReceipt variant = RulerLiftPolicyCausalReceipt.Create(
            in runtime, default, []);
        RulerLiftPolicyCausalDelta delta = variant.DeltaFrom(baseline);
        RulerLiftPolicyCausalDelta unchanged = baseline.DeltaFrom(baseline);
        return variant.ChangedFrom(baseline) && delta.Changed && !delta.SameAs(unchanged);
    }

    private static CortexPolicyRuntimeReceipt MakeCausalFixtureRuntime()
        => new(
            CortexPolicyAuthorities.Launchpad,
            0,
            0,
            0,
            0,
            0,
            [0, 0],
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            -1,
            -1,
            [],
            0,
            false,
            true);

    private static ReplayCalc ResolveReplay(Cortex cortex)
        => cortex.MountedCurriculum as ReplayCalc
        ?? throw new InvalidOperationException("RulerLift autonomy requires ReplayCalc");
}
