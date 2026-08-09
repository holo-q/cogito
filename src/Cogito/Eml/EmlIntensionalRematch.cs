namespace Cogito;

using System.Text;

internal enum EmlIntensionalRematchArms
{
    FreshEnumeration,
    ObligationHoleSolve,
    GuardedBinding,
    BindingShuffledNull,
    LawCandidateShadow,
    NoLaw,
    LawShuffledNull,
}

internal enum EmlIntensionalRematchPhases
{
    Evaluation,
    DescendantDelay,
}

internal enum EmlRematchProposalStatuses
{
    Candidate,
    Abstained,
    NoCandidate,
}

internal enum EmlRematchGraduationStatuses
{
    Graduated,
    NoAdvantage,
    NoCandidate,
    InsufficientSamples,
    InstrumentInvalid,
    InsufficientPower,
}

internal enum EmlRematchAssayStatuses
{
    Exact,
    Invalid,
}

internal enum EmlRematchPowerStatuses
{
    Powered,
    Unpowered,
    NotApplicable,
}

internal readonly record struct EmlIntensionalRematchBindingID(int Value);

internal readonly record struct EmlIntensionalRematchTrialSeed(
    EmlObligationResolution Obligation,
    EmlIntensionalRematchBindingID Binding,
    EmlLawCandidateInstantiation? LawCandidate);

internal readonly record struct EmlIntensionalRematchTrial(
    int Index,
    EmlObligationResolution Obligation,
    EmlIntensionalRematchBindingID Binding,
    EmlIntensionalRematchBindingID ShuffledBinding,
    EmlLawCandidateInstantiation? LawCandidate,
    EmlLawCandidateInstantiation? ShuffledLawCandidate,
    long RemainingEvaluatorCalls);

internal readonly record struct EmlIntensionalRematchStepResult(
    EmlRematchProposalStatuses ProposalStatus,
    IReadOnlyList<EmlCertificateDelta> CanonicalDeltas)
{
    public static EmlIntensionalRematchStepResult RecordCandidate(IReadOnlyList<EmlCertificateDelta> deltas)
        => new(EmlRematchProposalStatuses.Candidate, deltas);

    public static EmlIntensionalRematchStepResult RecordAbstention(IReadOnlyList<EmlCertificateDelta> deltas)
        => new(EmlRematchProposalStatuses.Abstained, deltas);

    public static EmlIntensionalRematchStepResult RecordNoCandidate(IReadOnlyList<EmlCertificateDelta> deltas)
        => new(EmlRematchProposalStatuses.NoCandidate, deltas);
}

/// Exact callable face of EmlHoleSolver.Solve for an obligation-directed arm.
internal delegate EmlHoleSolveResult EmlSolveObligationHole(
    IReadOnlyList<EmlMint> mintJournal,
    in EmlObligationResolution obligation,
    IReadOnlyList<EmlHoleCandidate> candidates,
    List<EmlHoleRepairProposal> output,
    EmlEvaluatorClock? clock,
    int branchRadius);

internal readonly record struct EmlSpeculativeTransactionMetrics(
    long ProbeTrials,
    long Commits,
    long Rollbacks,
    long SerializeLoads,
    long SerializeBytes,
    long Restores,
    long RestoreBytes,
    long PreviewEvaluatorCalls,
    long CommittedEvaluatorCalls,
    long PreviewWallTicks,
    long CommitWallTicks,
    long RollbackWallTicks)
{
    public static EmlSpeculativeTransactionMetrics Zero => default;
    public long TotalWallTicks => checked(PreviewWallTicks + CommitWallTicks + RollbackWallTicks);
    public EmlSpeculativeTransactionMetrics Add(in EmlSpeculativeTransactionMetrics other)
        => new(
            checked(ProbeTrials + other.ProbeTrials), checked(Commits + other.Commits),
            checked(Rollbacks + other.Rollbacks), checked(SerializeLoads + other.SerializeLoads),
            checked(SerializeBytes + other.SerializeBytes), checked(Restores + other.Restores),
            checked(RestoreBytes + other.RestoreBytes), checked(PreviewEvaluatorCalls + other.PreviewEvaluatorCalls),
            checked(CommittedEvaluatorCalls + other.CommittedEvaluatorCalls), checked(PreviewWallTicks + other.PreviewWallTicks),
            checked(CommitWallTicks + other.CommitWallTicks), checked(RollbackWallTicks + other.RollbackWallTicks));
}

internal interface IEmlIntensionalRematchArm
{
    EmlIntensionalRematchArms Kind { get; }
    string Name { get; }
    EmlEvaluatorClock EvaluatorClock { get; }
    IReadOnlyCollection<EmlCert> CaptureCertificates();
    string DescribePrediction(EmlPredictionID claimID);
    string DescribeCertificate(EmlCert certificate);
    EmlRung0RematchTelemetry CaptureRung0Telemetry();
    EmlSpeculativeTransactionMetrics CaptureSpeculativeTransactionMetrics();
    EmlIntensionalRematchStepResult ExecuteTrial(in EmlIntensionalRematchTrial trial);
    EmlIntensionalRematchStepResult AdvanceExactly(long evaluatorCalls, EmlIntensionalRematchPhases phase);
}

internal readonly record struct EmlRung0RematchTelemetry(
    int Attempts,
    int Composed,
    int EvaluatorZeroCompositions,
    int Audits,
    int UniqueAudits,
    int NoCandidates,
    int Exhausted,
    int GuardRejected,
    int ZeroWorkAttempts,
    int RelationNullExecutions,
    int RelationNullDivergences,
    int RelationNullAuthoritativeCompositions);

internal sealed record EmlIntensionalRematchSchedule(
    ulong Seed,
    IReadOnlyList<EmlIntensionalRematchTrialSeed> Seeds,
    IReadOnlyList<EmlIntensionalRematchTrial> Trials,
    int BindingShuffledPositions,
    int LawShuffledPositions)
{
    public static EmlIntensionalRematchSchedule Create(
        ulong seed,
        IReadOnlyList<EmlIntensionalRematchTrialSeed> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        List<EmlIntensionalRematchTrialSeed> seedCopy = new(seeds.Count);
        Dictionary<int, List<int>> obligationRows = new();
        for (int i = 0; i < seeds.Count; i++)
        {
            EmlIntensionalRematchTrialSeed row = seeds[i];
            if (row.Binding.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(seeds), row.Binding.Value,
                    $"rematch binding at row {i} must be nonnegative");
            seedCopy.Add(row);
            if (!obligationRows.TryGetValue(row.Obligation.SourcePredictionID.Value, out List<int>? rows))
            {
                rows = new List<int>();
                obligationRows.Add(row.Obligation.SourcePredictionID.Value, rows);
            }
            rows.Add(i);
        }

        EmlIntensionalRematchBindingID[] shuffledBindings = new EmlIntensionalRematchBindingID[seedCopy.Count];
        EmlLawCandidateInstantiation?[] shuffledLaws = new EmlLawCandidateInstantiation?[seedCopy.Count];
        for (int i = 0; i < seedCopy.Count; i++)
        {
            shuffledBindings[i] = seedCopy[i].Binding;
            shuffledLaws[i] = seedCopy[i].LawCandidate;
        }

        List<int> obligationIDs = new(obligationRows.Keys);
        obligationIDs.Sort();
        for (int i = 0; i < obligationIDs.Count; i++)
        {
            int obligationID = obligationIDs[i];
            List<int> rows = obligationRows[obligationID];
            RotateBindings(seed, obligationID, rows, seedCopy, shuffledBindings);
            RotateLaws(seed, obligationID, rows, seedCopy, shuffledLaws);
        }

        int bindingShuffledPositions = 0;
        int lawShuffledPositions = 0;
        List<EmlIntensionalRematchTrial> trials = new(seedCopy.Count);
        for (int i = 0; i < seedCopy.Count; i++)
        {
            EmlIntensionalRematchTrialSeed row = seedCopy[i];
            if (row.Binding != shuffledBindings[i]) bindingShuffledPositions++;
            if (row.LawCandidate != shuffledLaws[i]) lawShuffledPositions++;
            trials.Add(new EmlIntensionalRematchTrial(
                i,
                row.Obligation,
                row.Binding,
                shuffledBindings[i],
                row.LawCandidate,
                shuffledLaws[i],
                RemainingEvaluatorCalls: 0));
        }
        return new EmlIntensionalRematchSchedule(
            seed,
            seedCopy.ToArray(),
            trials.ToArray(),
            bindingShuffledPositions,
            lawShuffledPositions);
    }

    private static void RotateBindings(
        ulong seed,
        int obligationID,
        List<int> rows,
        List<EmlIntensionalRematchTrialSeed> seeds,
        EmlIntensionalRematchBindingID[] shuffled)
    {
        int offset = PickNonzeroOffset(seed, obligationID, rows.Count, 0x42494E44494E4753UL);
        for (int i = 0; i < rows.Count; i++)
        {
            int destination = rows[i];
            int source = rows[(i + offset) % rows.Count];
            shuffled[destination] = seeds[source].Binding;
        }
    }

    private static void RotateLaws(
        ulong seed,
        int obligationID,
        List<int> rows,
        List<EmlIntensionalRematchTrialSeed> seeds,
        EmlLawCandidateInstantiation?[] shuffled)
    {
        int offset = PickNonzeroOffset(seed, obligationID, rows.Count, 0x4C41575348554646UL);
        for (int i = 0; i < rows.Count; i++)
        {
            int destination = rows[i];
            EmlLawCandidateInstantiation? relation = seeds[destination].LawCandidate;
            if (relation is null) continue;
            for (int donorOffset = 0; donorOffset < rows.Count; donorOffset++)
            {
                int source = rows[(i + offset + donorOffset) % rows.Count];
                EmlLawCandidateInstantiation? donor = seeds[source].LawCandidate;
                if (donor is null) continue;
                EmlLawRewrite relationRewrite = relation.Value.Rewrite;
                EmlLawRewrite donorRewrite = donor.Value.Rewrite;
                ulong relationNullSalt = seed
                    ^ unchecked((ulong)(uint)obligationID << 32)
                    ^ unchecked((ulong)(uint)destination << 16)
                    ^ unchecked((ulong)(uint)source)
                    ^ 0x4E554C4C52454C41UL;
                if (relationNullSalt == 0) relationNullSalt = 1;
                if (!EmlLawRewrite.TryCreateRelationNull(
                        in relationRewrite,
                        in donorRewrite,
                        relationNullSalt,
                        new EmlGrader(),
                        out EmlLawRewrite relationNull)) continue;
                shuffled[destination] = new EmlLawCandidateInstantiation(relation.Value.Obligation, relationNull);
                break;
            }
        }
    }

    private static int PickNonzeroOffset(ulong seed, int obligationID, int count, ulong salt)
    {
        if (count < 2) return 0;
        ulong mixed = seed ^ salt ^ unchecked((ulong)(uint)obligationID * 0x9E3779B97F4A7C15UL);
        mixed ^= mixed >> 30;
        mixed *= 0xBF58476D1CE4E5B9UL;
        mixed ^= mixed >> 27;
        mixed *= 0x94D049BB133111EBUL;
        mixed ^= mixed >> 31;
        return checked((int)(mixed % unchecked((ulong)(count - 1)))) + 1;
    }
}

internal readonly record struct EmlIntensionalRematchConfig(
    long EvaluatorCalls,
    long DescendantDelayEvaluatorCalls,
    int IndependentReplicates);

internal readonly record struct EmlIntensionalRematchProgress(
    EmlIntensionalRematchArms Arm,
    EmlIntensionalRematchPhases Phase,
    long EvaluatorCalls,
    int ExecutedTrials,
    int ScheduledTrials,
    bool Completed);

internal sealed record EmlIntensionalRematchArmReport(
    EmlIntensionalRematchArms Kind,
    string Name,
    EmlRematchAssayStatuses AssayStatus,
    string AssayDetail,
    EmlRematchPowerStatuses PowerStatus,
    string PowerDetail,
    EmlEvaluatorInterval Evaluation,
    EmlEvaluatorInterval DescendantDelay,
    int ScheduledTrials,
    int ExecutedTrials,
    int Candidates,
    int Abstentions,
    int NoCandidates,
    EmlRung0RematchTelemetry Rung0,
    EmlSpeculativeTransactionMetrics SpeculativeTransactions,
    IReadOnlyList<EmlCertificateDelta> CanonicalDeltas,
    IReadOnlyList<EmlCert> EvaluationDiscoveries,
    IReadOnlyList<EmlCert> DelayedDescendants,
    IReadOnlyList<string> DiscoveryDetails)
{
    public bool AssayExact => AssayStatus == EmlRematchAssayStatuses.Exact;

    public double CanonicalDeltasPerEvaluatorCall
        => Evaluation.Calls == 0 ? 0 : CanonicalDeltas.Count / (double)Evaluation.Calls;

    public double DelayedDescendantsPerEvaluatorCall
    {
        get
        {
            long calls = checked(Evaluation.Calls + DescendantDelay.Calls);
            return calls == 0 ? 0 : DelayedDescendants.Count / (double)calls;
        }
    }
}

internal sealed record EmlIntensionalRematchContrastReport(
    string Name,
    EmlIntensionalRematchArms Intervention,
    IReadOnlyList<EmlIntensionalRematchArms> Nulls,
    EmlRematchGraduationStatuses Graduation,
    string GraduationDetail,
    int InterventionCanonicalDeltas,
    int StrongestNullCanonicalDeltas,
    int InterventionExclusiveDelayedDescendants,
    int NullExclusiveDelayedDescendants,
    double InterventionCanonicalDeltasPerEvaluatorCall,
    double StrongestNullCanonicalDeltasPerEvaluatorCall,
    double InterventionDelayedDescendantsPerEvaluatorCall,
    double StrongestNullDelayedDescendantsPerEvaluatorCall);

internal sealed record EmlIntensionalRematchReport(
    EmlIntensionalRematchConfig Config,
    EmlIntensionalRematchSchedule Schedule,
    IReadOnlyList<EmlIntensionalRematchArmReport> Arms,
    IReadOnlyList<EmlIntensionalRematchContrastReport> Contrasts)
{
    public string FormatTSV()
    {
        StringBuilder report = new();
        report.AppendLine("section\tname\tmetric\tvalue");
        report.AppendLine($"assay\trematch\tevaluator_calls\t{Config.EvaluatorCalls}");
        report.AppendLine($"assay\trematch\tdescendant_delay_evaluator_calls\t{Config.DescendantDelayEvaluatorCalls}");
        report.AppendLine($"assay\trematch\tindependent_replicates\t{Config.IndependentReplicates}");
        report.AppendLine($"schedule\trematch\ttrials\t{Schedule.Trials.Count}");
        report.AppendLine($"schedule\trematch\tbinding_shuffled_positions\t{Schedule.BindingShuffledPositions}");
        report.AppendLine($"schedule\trematch\tlaw_shuffled_positions\t{Schedule.LawShuffledPositions}");
        for (int i = 0; i < Arms.Count; i++) AppendArm(report, Arms[i]);
        for (int i = 0; i < Contrasts.Count; i++) AppendContrast(report, Contrasts[i]);
        return report.ToString();
    }

    private static void AppendArm(StringBuilder report, EmlIntensionalRematchArmReport arm)
    {
        string name = arm.Name;
        report.AppendLine($"arm\t{name}\tassay_status\t{arm.AssayStatus}");
        report.AppendLine($"arm\t{name}\tassay_detail\t{arm.AssayDetail}");
        report.AppendLine($"arm\t{name}\tpower_status\t{arm.PowerStatus}");
        report.AppendLine($"arm\t{name}\tpower_detail\t{arm.PowerDetail}");
        report.AppendLine($"arm\t{name}\tevaluator_calls\t{arm.Evaluation.Calls}");
        report.AppendLine($"arm\t{name}\tdescendant_delay_evaluator_calls\t{arm.DescendantDelay.Calls}");
        report.AppendLine($"arm\t{name}\tscheduled_trials\t{arm.ScheduledTrials}");
        report.AppendLine($"arm\t{name}\texecuted_trials\t{arm.ExecutedTrials}");
        report.AppendLine($"arm\t{name}\tcandidates\t{arm.Candidates}");
        report.AppendLine($"arm\t{name}\tabstentions\t{arm.Abstentions}");
        report.AppendLine($"arm\t{name}\tno_candidates\t{arm.NoCandidates}");
        report.AppendLine($"arm\t{name}\trung0_attempts\t{arm.Rung0.Attempts}");
        report.AppendLine($"arm\t{name}\trung0_derived\t{arm.Rung0.Composed}");
        report.AppendLine($"arm\t{name}\trung0_evaluator_zero\t{arm.Rung0.EvaluatorZeroCompositions}");
        report.AppendLine($"arm\t{name}\trung0_audits\t{arm.Rung0.Audits}");
        report.AppendLine($"arm\t{name}\trung0_unique_audits\t{arm.Rung0.UniqueAudits}");
        report.AppendLine($"arm\t{name}\trung0_no_candidates\t{arm.Rung0.NoCandidates}");
        report.AppendLine($"arm\t{name}\trung0_exhausted\t{arm.Rung0.Exhausted}");
        report.AppendLine($"arm\t{name}\trung0_guard_rejected\t{arm.Rung0.GuardRejected}");
        report.AppendLine($"arm\t{name}\trung0_zero_work_attempts\t{arm.Rung0.ZeroWorkAttempts}");
        report.AppendLine($"arm\t{name}\trelation_null_executions\t{arm.Rung0.RelationNullExecutions}");
        report.AppendLine($"arm\t{name}\trelation_null_divergences\t{arm.Rung0.RelationNullDivergences}");
        report.AppendLine($"arm\t{name}\trelation_null_authoritative_derivations\t{arm.Rung0.RelationNullAuthoritativeCompositions}");
        report.AppendLine($"arm\t{name}\tprobe_trials\t{arm.SpeculativeTransactions.ProbeTrials}");
        report.AppendLine($"arm\t{name}\tprobe_commits\t{arm.SpeculativeTransactions.Commits}");
        report.AppendLine($"arm\t{name}\tprobe_rollbacks\t{arm.SpeculativeTransactions.Rollbacks}");
        report.AppendLine($"arm\t{name}\tprobe_serialize_loads\t{arm.SpeculativeTransactions.SerializeLoads}");
        report.AppendLine($"arm\t{name}\tprobe_serialize_bytes\t{arm.SpeculativeTransactions.SerializeBytes}");
        report.AppendLine($"arm\t{name}\tprobe_restores\t{arm.SpeculativeTransactions.Restores}");
        report.AppendLine($"arm\t{name}\tprobe_restore_bytes\t{arm.SpeculativeTransactions.RestoreBytes}");
        report.AppendLine($"arm\t{name}\tprobe_preview_evaluator_calls\t{arm.SpeculativeTransactions.PreviewEvaluatorCalls}");
        report.AppendLine($"arm\t{name}\tprobe_committed_evaluator_calls\t{arm.SpeculativeTransactions.CommittedEvaluatorCalls}");
        report.AppendLine($"arm\t{name}\tprobe_preview_wall_ticks\t{arm.SpeculativeTransactions.PreviewWallTicks}");
        report.AppendLine($"arm\t{name}\tprobe_commit_wall_ticks\t{arm.SpeculativeTransactions.CommitWallTicks}");
        report.AppendLine($"arm\t{name}\tprobe_rollback_wall_ticks\t{arm.SpeculativeTransactions.RollbackWallTicks}");
        report.AppendLine($"arm\t{name}\tprobe_total_wall_ticks\t{arm.SpeculativeTransactions.TotalWallTicks}");
        report.AppendLine($"arm\t{name}\tcanonical_deltas\t{arm.CanonicalDeltas.Count}");
        report.AppendLine($"arm\t{name}\tcanonical_deltas_per_evaluator_call\t{arm.CanonicalDeltasPerEvaluatorCall:R}");
        report.AppendLine($"arm\t{name}\tevaluation_discoveries\t{arm.EvaluationDiscoveries.Count}");
        report.AppendLine($"arm\t{name}\tdelayed_descendants\t{arm.DelayedDescendants.Count}");
        report.AppendLine($"arm\t{name}\tdelayed_descendants_per_evaluator_call\t{arm.DelayedDescendantsPerEvaluatorCall:R}");
        for (int i = 0; i < arm.DiscoveryDetails.Count; i++)
            report.Append("discovery\t").Append(name).Append("\titem-").Append(i).Append('\t')
                .AppendLine(arm.DiscoveryDetails[i]);
    }

    private static void AppendContrast(StringBuilder report, EmlIntensionalRematchContrastReport contrast)
    {
        string name = contrast.Name;
        report.AppendLine($"contrast\t{name}\tgraduation\t{contrast.Graduation}");
        report.AppendLine($"contrast\t{name}\tgraduation_detail\t{contrast.GraduationDetail}");
        report.AppendLine($"contrast\t{name}\tintervention_canonical_deltas\t{contrast.InterventionCanonicalDeltas}");
        report.AppendLine($"contrast\t{name}\tstrongest_null_canonical_deltas\t{contrast.StrongestNullCanonicalDeltas}");
        report.AppendLine($"contrast\t{name}\tintervention_exclusive_delayed_descendants\t{contrast.InterventionExclusiveDelayedDescendants}");
        report.AppendLine($"contrast\t{name}\tnull_exclusive_delayed_descendants\t{contrast.NullExclusiveDelayedDescendants}");
        report.AppendLine($"contrast\t{name}\tintervention_canonical_deltas_per_evaluator_call\t{contrast.InterventionCanonicalDeltasPerEvaluatorCall:R}");
        report.AppendLine($"contrast\t{name}\tstrongest_null_canonical_deltas_per_evaluator_call\t{contrast.StrongestNullCanonicalDeltasPerEvaluatorCall:R}");
        report.AppendLine($"contrast\t{name}\tintervention_delayed_descendants_per_evaluator_call\t{contrast.InterventionDelayedDescendantsPerEvaluatorCall:R}");
        report.AppendLine($"contrast\t{name}\tstrongest_null_delayed_descendants_per_evaluator_call\t{contrast.StrongestNullDelayedDescendantsPerEvaluatorCall:R}");
    }
}

internal static class EmlIntensionalRematch
{
    public const int MinimumTrialsForGraduation = 64;
    public const int MinimumCandidatesForGraduation = 16;
    public const int MinimumIndependentReplicatesForGraduation = 3;

    public static EmlSolveObligationHole CreateHoleSolver() => SolveHole;

    private static EmlHoleSolveResult SolveHole(
        IReadOnlyList<EmlMint> mintJournal,
        in EmlObligationResolution obligation,
        IReadOnlyList<EmlHoleCandidate> candidates,
        List<EmlHoleRepairProposal> output,
        EmlEvaluatorClock? clock,
        int branchRadius)
        => EmlHoleSolver.Solve(mintJournal, in obligation, candidates, output, clock, branchRadius);

    public static EmlIntensionalRematchReport Run(
        in EmlIntensionalRematchConfig config,
        EmlIntensionalRematchSchedule schedule,
        IReadOnlyList<IEmlIntensionalRematchArm> arms,
        Action<EmlIntensionalRematchProgress>? progress = null)
    {
        ValidateConfig(in config);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(arms);
        Dictionary<EmlIntensionalRematchArms, IEmlIntensionalRematchArm> indexedArms = IndexArms(arms);
        List<EmlIntensionalRematchArmReport> reports = new(indexedArms.Count);
        foreach (EmlIntensionalRematchArms kind in Enum.GetValues<EmlIntensionalRematchArms>())
            reports.Add(RunArm(in config, schedule, indexedArms[kind], progress));

        Dictionary<EmlIntensionalRematchArms, EmlIntensionalRematchArmReport> indexedReports = new();
        for (int i = 0; i < reports.Count; i++) indexedReports.Add(reports[i].Kind, reports[i]);
        List<EmlIntensionalRematchContrastReport> contrasts = new(3)
        {
            Compare("obligation-hole-solve", EmlIntensionalRematchArms.ObligationHoleSolve,
                [EmlIntensionalRematchArms.FreshEnumeration], in config, schedule, indexedReports),
            Compare("guarded-binding", EmlIntensionalRematchArms.GuardedBinding,
                [EmlIntensionalRematchArms.BindingShuffledNull], in config, schedule, indexedReports),
            Compare("law-candidate-shadow", EmlIntensionalRematchArms.LawCandidateShadow,
                [EmlIntensionalRematchArms.NoLaw, EmlIntensionalRematchArms.LawShuffledNull],
                in config, schedule, indexedReports),
        };
        return new EmlIntensionalRematchReport(config, schedule, reports.ToArray(), contrasts.ToArray());
    }

    private static EmlIntensionalRematchArmReport RunArm(
        in EmlIntensionalRematchConfig config,
        EmlIntensionalRematchSchedule schedule,
        IEmlIntensionalRematchArm arm,
        Action<EmlIntensionalRematchProgress>? progress)
    {
        ValidateArmName(arm.Name);
        EmlEvaluatorClock clock = arm.EvaluatorClock
            ?? throw new InvalidOperationException($"rematch arm {arm.Kind} has no evaluator clock");
        long evaluationStart = clock.ProgramPointEvaluations;
        long evaluationEnd = checked(evaluationStart + config.EvaluatorCalls);
        HashSet<EmlCert> baseline = CaptureCertificates(arm);
        List<EmlCertificateDelta> deltas = new();
        int candidates = 0;
        int abstentions = 0;
        int noCandidates = 0;
        int executedTrials = 0;
        EmlRematchAssayStatuses assayStatus = EmlRematchAssayStatuses.Exact;
        string assayDetail = "exact";
        progress?.Invoke(new EmlIntensionalRematchProgress(
            arm.Kind,
            EmlIntensionalRematchPhases.Evaluation,
            0,
            0,
            schedule.Trials.Count,
            Completed: false));

        for (int i = 0; i < schedule.Trials.Count; i++)
        {
            long remaining = evaluationEnd - clock.ProgramPointEvaluations;
            if (remaining <= 0)
            {
                assayStatus = EmlRematchAssayStatuses.Invalid;
                assayDetail = "evaluation budget exhausted before the finite schedule completed";
                break;
            }
            EmlIntensionalRematchTrial scheduled = schedule.Trials[i];
            EmlIntensionalRematchTrial trial = scheduled with { RemainingEvaluatorCalls = remaining };
            EmlIntensionalRematchStepResult result = arm.ExecuteTrial(in trial);
            executedTrials++;
            CountProposal(result.ProposalStatus, ref candidates, ref abstentions, ref noCandidates);
            AppendDeltas(deltas, result.CanonicalDeltas, evaluationStart, clock.ProgramPointEvaluations, ref assayStatus, ref assayDetail);
            if (clock.ProgramPointEvaluations > evaluationEnd)
            {
                assayStatus = EmlRematchAssayStatuses.Invalid;
                assayDetail = "trial overran the matched evaluator endpoint";
                break;
            }
        }

        long evaluationRemainder = evaluationEnd - clock.ProgramPointEvaluations;
        if (evaluationRemainder > 0)
        {
            EmlIntensionalRematchStepResult fill = arm.AdvanceExactly(
                evaluationRemainder, EmlIntensionalRematchPhases.Evaluation);
            AppendDeltas(deltas, fill.CanonicalDeltas, evaluationStart, clock.ProgramPointEvaluations, ref assayStatus, ref assayDetail);
        }
        if (clock.ProgramPointEvaluations != evaluationEnd)
        {
            assayStatus = EmlRematchAssayStatuses.Invalid;
            assayDetail = $"evaluation endpoint mismatch: expected {evaluationEnd}, observed {clock.ProgramPointEvaluations}";
        }
        progress?.Invoke(new EmlIntensionalRematchProgress(
            arm.Kind,
            EmlIntensionalRematchPhases.Evaluation,
            checked(clock.ProgramPointEvaluations - evaluationStart),
            executedTrials,
            schedule.Trials.Count,
            Completed: true));
        EmlEvaluatorInterval evaluation = new(evaluationStart, clock.ProgramPointEvaluations);
        HashSet<EmlCert> atEvaluation = CaptureCertificates(arm);
        HashSet<EmlCert> evaluationDiscoveries = new(atEvaluation);
        evaluationDiscoveries.ExceptWith(baseline);

        long delayStart = clock.ProgramPointEvaluations;
        long delayEnd = checked(delayStart + config.DescendantDelayEvaluatorCalls);
        progress?.Invoke(new EmlIntensionalRematchProgress(
            arm.Kind,
            EmlIntensionalRematchPhases.DescendantDelay,
            0,
            0,
            schedule.Trials.Count,
            Completed: false));
        if (config.DescendantDelayEvaluatorCalls > 0)
            arm.AdvanceExactly(config.DescendantDelayEvaluatorCalls, EmlIntensionalRematchPhases.DescendantDelay);
        if (clock.ProgramPointEvaluations != delayEnd)
        {
            assayStatus = EmlRematchAssayStatuses.Invalid;
            assayDetail = $"descendant-delay endpoint mismatch: expected {delayEnd}, observed {clock.ProgramPointEvaluations}";
        }
        progress?.Invoke(new EmlIntensionalRematchProgress(
            arm.Kind,
            EmlIntensionalRematchPhases.DescendantDelay,
            checked(clock.ProgramPointEvaluations - delayStart),
            executedTrials,
            schedule.Trials.Count,
            Completed: true));
        EmlEvaluatorInterval delay = new(delayStart, clock.ProgramPointEvaluations);
        HashSet<EmlCert> delayedDescendants = CaptureCertificates(arm);
        delayedDescendants.ExceptWith(atEvaluation);

        if (executedTrials != schedule.Trials.Count)
        {
            assayStatus = EmlRematchAssayStatuses.Invalid;
            if (assayDetail == "exact") assayDetail = "not every scheduled trial executed";
        }
        if (candidates + abstentions + noCandidates != executedTrials)
        {
            assayStatus = EmlRematchAssayStatuses.Invalid;
            assayDetail = "proposal census does not equal executed trials";
        }
        EmlRung0RematchTelemetry rung0 = arm.CaptureRung0Telemetry();
        EmlSpeculativeTransactionMetrics speculativeTransactions = arm.CaptureSpeculativeTransactionMetrics();
        (EmlRematchPowerStatuses powerStatus, string powerDetail) = arm.Kind switch
        {
            EmlIntensionalRematchArms.LawCandidateShadow
                when rung0.Attempts > 0 && rung0.Composed > 0
                    && rung0.EvaluatorZeroCompositions == rung0.Composed && rung0.Audits > 0
                => (EmlRematchPowerStatuses.Powered, "rung-0 derived claims audited at zero evaluator cost"),
            EmlIntensionalRematchArms.LawCandidateShadow
                => (EmlRematchPowerStatuses.Unpowered, "insufficient rung-0 derived/audit coverage"),
            EmlIntensionalRematchArms.LawShuffledNull
                when rung0.RelationNullExecutions > 0
                    && rung0.RelationNullDivergences == rung0.RelationNullExecutions
                    && rung0.RelationNullAuthoritativeCompositions == 0
                => (EmlRematchPowerStatuses.Powered, "relation-null executions diverged without authority claims"),
            EmlIntensionalRematchArms.LawShuffledNull
                => (EmlRematchPowerStatuses.Unpowered, "insufficient relation-null divergence/authority coverage"),
            _ => (EmlRematchPowerStatuses.NotApplicable, "arm has no causal power predicate"),
        };
        EmlCert[] sortedEvaluation = SortCertificates(evaluationDiscoveries);
        EmlCert[] sortedDescendants = SortCertificates(delayedDescendants);
        List<string> discoveryDetails = new();
        for (int i = 0; i < deltas.Count; i++)
        {
            EmlCertificateDelta delta = deltas[i];
            discoveryDetails.Add(
                $"delta|change={delta.Change}|claim={delta.PredictionID.Value}|before={FormatCertificate(delta.Before)}|after={FormatCertificate(delta.After)}|line={EscapeDetail(arm.DescribePrediction(delta.PredictionID))}");
        }
        for (int i = 0; i < sortedEvaluation.Length; i++)
        {
            EmlCert certificate = sortedEvaluation[i];
            discoveryDetails.Add($"evaluation|certificate={certificate.Hex()}|grade={certificate.Grade}|representative={EscapeDetail(arm.DescribeCertificate(certificate))}");
        }
        for (int i = 0; i < sortedDescendants.Length; i++)
        {
            EmlCert certificate = sortedDescendants[i];
            discoveryDetails.Add($"descendant|certificate={certificate.Hex()}|grade={certificate.Grade}|representative={EscapeDetail(arm.DescribeCertificate(certificate))}");
        }
        return new EmlIntensionalRematchArmReport(
            arm.Kind,
            arm.Name,
            assayStatus,
            assayDetail,
            powerStatus,
            powerDetail,
            evaluation,
            delay,
            schedule.Trials.Count,
            executedTrials,
            candidates,
            abstentions,
            noCandidates,
            rung0,
            speculativeTransactions,
            deltas.ToArray(),
            sortedEvaluation,
            sortedDescendants,
            discoveryDetails.ToArray());
    }

    private static string FormatCertificate(EmlCert? certificate)
        => certificate.HasValue ? certificate.Value.Hex() : "none";

    private static string EscapeDetail(string value)
        => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static EmlIntensionalRematchContrastReport Compare(
        string name,
        EmlIntensionalRematchArms interventionKind,
        EmlIntensionalRematchArms[] nullKinds,
        in EmlIntensionalRematchConfig config,
        EmlIntensionalRematchSchedule schedule,
        Dictionary<EmlIntensionalRematchArms, EmlIntensionalRematchArmReport> reports)
    {
        EmlIntensionalRematchArmReport intervention = reports[interventionKind];
        HashSet<EmlCert> nullDescendants = new();
        int strongestNullDeltas = 0;
        double strongestNullDeltaRate = 0;
        double strongestNullDescendantRate = 0;
        bool assayExact = intervention.AssayExact;
        for (int i = 0; i < nullKinds.Length; i++)
        {
            EmlIntensionalRematchArmReport nullReport = reports[nullKinds[i]];
            assayExact &= nullReport.AssayExact;
            if (nullReport.CanonicalDeltas.Count > strongestNullDeltas)
                strongestNullDeltas = nullReport.CanonicalDeltas.Count;
            if (nullReport.CanonicalDeltasPerEvaluatorCall > strongestNullDeltaRate)
                strongestNullDeltaRate = nullReport.CanonicalDeltasPerEvaluatorCall;
            if (nullReport.DelayedDescendantsPerEvaluatorCall > strongestNullDescendantRate)
                strongestNullDescendantRate = nullReport.DelayedDescendantsPerEvaluatorCall;
            for (int descendant = 0; descendant < nullReport.DelayedDescendants.Count; descendant++)
                nullDescendants.Add(nullReport.DelayedDescendants[descendant]);
        }

        HashSet<EmlCert> interventionDescendants = new(intervention.DelayedDescendants);
        HashSet<EmlCert> interventionExclusive = new(interventionDescendants);
        interventionExclusive.ExceptWith(nullDescendants);
        HashSet<EmlCert> nullExclusive = new(nullDescendants);
        nullExclusive.ExceptWith(interventionDescendants);
        (EmlRematchGraduationStatuses graduation, string detail) = DecideGraduation(
            interventionKind,
            intervention,
            strongestNullDeltas,
            strongestNullDeltaRate,
            strongestNullDescendantRate,
            interventionExclusive.Count,
            assayExact,
            nullKinds.Select(kind => reports[kind]).ToArray(),
            in config,
            schedule);
        return new EmlIntensionalRematchContrastReport(
            name,
            interventionKind,
            nullKinds,
            graduation,
            detail,
            intervention.CanonicalDeltas.Count,
            strongestNullDeltas,
            interventionExclusive.Count,
            nullExclusive.Count,
            intervention.CanonicalDeltasPerEvaluatorCall,
            strongestNullDeltaRate,
            intervention.DelayedDescendantsPerEvaluatorCall,
            strongestNullDescendantRate);
    }

    private static (EmlRematchGraduationStatuses Status, string Detail) DecideGraduation(
        EmlIntensionalRematchArms interventionKind,
        EmlIntensionalRematchArmReport intervention,
        int strongestNullDeltas,
        double strongestNullDeltaRate,
        double strongestNullDescendantRate,
        int exclusiveDescendants,
        bool assayExact,
        IReadOnlyList<EmlIntensionalRematchArmReport> nullReports,
        in EmlIntensionalRematchConfig config,
        EmlIntensionalRematchSchedule schedule)
    {
        if (!assayExact)
            return (EmlRematchGraduationStatuses.InstrumentInvalid, "one or more matched arms failed exact evaluator accounting");
        if (interventionKind == EmlIntensionalRematchArms.LawCandidateShadow
            && (intervention.PowerStatus != EmlRematchPowerStatuses.Powered
                || nullReports.Any(static report => report.Kind == EmlIntensionalRematchArms.LawShuffledNull
                    && report.PowerStatus != EmlRematchPowerStatuses.Powered)))
        {
            // The law contrast is structurally exact even when its causal arms
            // have no observable effect. That is a durable null, not bad assay.
            EmlIntensionalRematchArmReport? unpoweredNull = nullReports
                .FirstOrDefault(report => report.Kind == EmlIntensionalRematchArms.LawShuffledNull
                    && report.PowerStatus != EmlRematchPowerStatuses.Powered);
            string detail = intervention.PowerStatus != EmlRematchPowerStatuses.Powered
                ? intervention.PowerDetail
                : unpoweredNull?.PowerDetail ?? "law relation-null is unpowered";
            return (EmlRematchGraduationStatuses.InsufficientPower, "exact assay but insufficient causal power: " + detail);
        }
        if (intervention.Candidates == 0 && intervention.NoCandidates > 0)
            return (EmlRematchGraduationStatuses.NoCandidate, "the intervention produced no candidate");
        if (interventionKind == EmlIntensionalRematchArms.GuardedBinding
            && schedule.BindingShuffledPositions == 0)
            return (EmlRematchGraduationStatuses.InstrumentInvalid, "binding null did not change any within-obligation assignment");
        if (interventionKind == EmlIntensionalRematchArms.LawCandidateShadow
            && schedule.Seeds.Count > 0
            && schedule.LawShuffledPositions == 0)
            return (EmlRematchGraduationStatuses.InstrumentInvalid, "law null did not change any within-obligation assignment");
        if (schedule.Trials.Count < MinimumTrialsForGraduation
            || intervention.Candidates < MinimumCandidatesForGraduation
            || config.IndependentReplicates < MinimumIndependentReplicatesForGraduation)
            return (EmlRematchGraduationStatuses.InsufficientSamples,
                $"graduation requires >= {MinimumTrialsForGraduation} trials, >= {MinimumCandidatesForGraduation} candidates, and >= {MinimumIndependentReplicatesForGraduation} independent replicates");
        bool canonicalAdvantage = intervention.CanonicalDeltas.Count > strongestNullDeltas
            && intervention.CanonicalDeltasPerEvaluatorCall > strongestNullDeltaRate;
        bool descendantAdvantage = exclusiveDescendants > 0
            && intervention.DelayedDescendantsPerEvaluatorCall > strongestNullDescendantRate;
        if (canonicalAdvantage && descendantAdvantage)
            return (EmlRematchGraduationStatuses.Graduated,
                "canonical deltas and delayed exclusive descendants both exceeded the matched nulls per evaluator call");
        return (EmlRematchGraduationStatuses.NoAdvantage,
            "matched nulls were not exceeded on both canonical deltas and delayed exclusive descendants per evaluator call");
    }

    private static Dictionary<EmlIntensionalRematchArms, IEmlIntensionalRematchArm> IndexArms(
        IReadOnlyList<IEmlIntensionalRematchArm> arms)
    {
        Dictionary<EmlIntensionalRematchArms, IEmlIntensionalRematchArm> indexed = new();
        for (int i = 0; i < arms.Count; i++)
        {
            IEmlIntensionalRematchArm arm = arms[i]
                ?? throw new ArgumentException($"rematch arm {i} is null", nameof(arms));
            if (!indexed.TryAdd(arm.Kind, arm))
                throw new ArgumentException($"rematch arm {arm.Kind} was supplied more than once", nameof(arms));
        }
        foreach (EmlIntensionalRematchArms kind in Enum.GetValues<EmlIntensionalRematchArms>())
            if (!indexed.ContainsKey(kind))
                throw new ArgumentException($"rematch arm {kind} is missing", nameof(arms));
        return indexed;
    }

    private static HashSet<EmlCert> CaptureCertificates(IEmlIntensionalRematchArm arm)
    {
        IReadOnlyCollection<EmlCert> captured = arm.CaptureCertificates()
            ?? throw new InvalidOperationException($"rematch arm {arm.Kind} returned a null certificate census");
        return new HashSet<EmlCert>(captured);
    }

    private static EmlCert[] SortCertificates(HashSet<EmlCert> certificates)
    {
        List<EmlCert> sorted = new(certificates);
        sorted.Sort(static (left, right) => string.CompareOrdinal(left.Hex(), right.Hex()));
        return sorted.ToArray();
    }

    private static void CountProposal(
        EmlRematchProposalStatuses status,
        ref int candidates,
        ref int abstentions,
        ref int noCandidates)
    {
        switch (status)
        {
            case EmlRematchProposalStatuses.Candidate:
                candidates++;
                break;
            case EmlRematchProposalStatuses.Abstained:
                abstentions++;
                break;
            case EmlRematchProposalStatuses.NoCandidate:
                noCandidates++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "unknown rematch proposal status");
        }
    }

    private static void AppendDeltas(
        List<EmlCertificateDelta> into,
        IReadOnlyList<EmlCertificateDelta> deltas,
        long evaluationStart,
        long observedEnd,
        ref EmlRematchAssayStatuses assayStatus,
        ref string assayDetail)
    {
        if (deltas is null)
            throw new InvalidOperationException("rematch arm returned a null canonical-delta list");
        for (int i = 0; i < deltas.Count; i++)
        {
            EmlCertificateDelta delta = deltas[i];
            if (delta.Evaluation.Start < evaluationStart
                || delta.Evaluation.End < delta.Evaluation.Start
                || delta.Evaluation.End > observedEnd)
            {
                assayStatus = EmlRematchAssayStatuses.Invalid;
                assayDetail = "canonical delta carried an evaluator interval outside the matched evaluation window";
            }
            into.Add(delta);
        }
    }

    private static void ValidateConfig(in EmlIntensionalRematchConfig config)
    {
        if (config.EvaluatorCalls <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), config.EvaluatorCalls,
                "rematch evaluator calls must be positive");
        if (config.DescendantDelayEvaluatorCalls <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), config.DescendantDelayEvaluatorCalls,
                "rematch descendant delay must be positive");
        if (config.IndependentReplicates <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), config.IndependentReplicates,
                "rematch independent replicate count must be positive");
    }

    private static void ValidateArmName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.IndexOfAny(['\t', '\r', '\n']) >= 0)
            throw new ArgumentException("rematch arm name must be nonempty TSV-safe text", nameof(name));
    }
}
