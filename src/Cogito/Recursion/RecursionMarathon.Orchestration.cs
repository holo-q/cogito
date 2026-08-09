namespace Cogito;

using System.Diagnostics;

internal readonly record struct RecursionWallPhaseTime(RecursionWallPhases Phase, long StopwatchTicks);

internal sealed class RecursionWallReport
{
    public required long TotalStopwatchTicks { get; init; }
    public required List<RecursionWallPhaseTime> Phases { get; init; }
    public required long UnaccountedStopwatchTicks { get; init; }
    public bool IsExact => TotalStopwatchTicks >= 0 && UnaccountedStopwatchTicks == 0;

    public static RecursionWallReport Create(long totalStopwatchTicks, List<RecursionWallPhaseTime> phases)
    {
        if (totalStopwatchTicks < 0) throw new ArgumentOutOfRangeException(nameof(totalStopwatchTicks));
        long accounted = 0;
        HashSet<RecursionWallPhases> seen = new();
        foreach (RecursionWallPhaseTime phase in phases)
        {
            if (phase.StopwatchTicks < 0) throw new InvalidDataException($"wall phase {phase.Phase} has negative time");
            if (!seen.Add(phase.Phase)) throw new InvalidDataException($"wall phase {phase.Phase} is duplicated");
            accounted = checked(accounted + phase.StopwatchTicks);
        }
        return new RecursionWallReport
        {
            TotalStopwatchTicks = totalStopwatchTicks,
            Phases = phases,
            UnaccountedStopwatchTicks = totalStopwatchTicks - accounted
        };
    }
}

internal sealed class RecursionWallAccount
{
    private readonly long[] _phaseTicks = new long[Enum.GetValues<RecursionWallPhases>().Length];

    public long BeginPhase() => Stopwatch.GetTimestamp();

    public void RecordPhase(RecursionWallPhases phase, long startedAt)
    {
        long elapsed = Stopwatch.GetTimestamp() - startedAt;
        if (elapsed < 0) throw new InvalidOperationException("monotonic wall clock moved backwards");
        Interlocked.Add(ref _phaseTicks[(int)phase], elapsed);
    }

    public RecursionWallReport Complete(long totalStopwatchTicks)
    {
        List<RecursionWallPhaseTime> phases = new(_phaseTicks.Length);
        for (int i = 0; i < _phaseTicks.Length; i++)
            phases.Add(new RecursionWallPhaseTime((RecursionWallPhases)i, Interlocked.Read(ref _phaseTicks[i])));
        return RecursionWallReport.Create(totalStopwatchTicks, phases);
    }
}

internal sealed class RecursionLaneSegmentRequest
{
    public required string RunID { get; init; }
    public required RecursionMarathonStages Stage { get; init; }
    public required int SegmentIndex { get; init; }
    public required long TargetUnits { get; init; }
    public required long WallLimitTicks { get; init; }
    public required bool IsResume { get; init; }
    public required string ResumeCheckpoint { get; init; }
    public required CortexStopCondition? StopCondition { get; init; }
}

internal sealed class RecursionLaneSegmentResult
{
    public required RecursionMarathonLanes Lane { get; init; }
    public required int SegmentIndex { get; init; }
    public required long CompletedUnits { get; init; }
    public required RecursionSegmentStops Stop { get; init; }
    public required string Checkpoint { get; init; }
    public required string CheckpointDigest { get; init; }
    public required string TapePrefixDigest { get; init; }
    public required string JournalPrefixDigest { get; init; }
    public required string ResumedCheckpointDigest { get; init; }
    public required string RestoredTapePrefixDigest { get; init; }
    public required string RestoredJournalPrefixDigest { get; init; }
    public required RecursionWallReport Wall { get; init; }
    public required List<RecursionMarathonWindow> Windows { get; init; }
}

internal sealed class RecursionTerminationReceipt
{
    public required RecursionMarathonLanes Lane { get; init; }
    public required int SegmentIndex { get; init; }
    public required bool ProcessWasForcedDown { get; init; }
    public required string CheckpointDigestBeforeKill { get; init; }
}

internal interface IRecursionMarathonLane
{
    RecursionMarathonLanes Lane { get; }
    string ProgressSelector { get; }

    Task<RecursionLaneSegmentResult> RunSegmentAsync(
        RecursionLaneSegmentRequest request,
        CancellationToken cancellationToken);

    Task<RecursionTerminationReceipt> TerminateAsync(
        RecursionLaneSegmentResult segment,
        CancellationToken cancellationToken);
}

internal sealed class RecursionMarathonReport
{
    public int SchemaVersion { get; init; } = RecursionMarathonDefaults.SchemaVersion;
    public required string RunID { get; init; }
    public required RecursionMarathonStages Stage { get; init; }
    public required List<RecursionLaneSegmentResult> Segments { get; init; }
    public required List<RecursionTerminationReceipt> Terminations { get; init; }
    public required List<RecursionLaneClassification> Classifications { get; init; }
    public required bool ReachedBothBudgets { get; init; }
    public required bool CheckpointsExact { get; init; }
    public required bool WallAccountingExact { get; init; }
}

internal static class RecursionMarathon
{
    public static async Task<(RecursionLaneCalibration EML, RecursionLaneCalibration Campfire)> CalibrateAsync(
        string runID,
        IRecursionMarathonLane eml,
        IRecursionMarathonLane campfire,
        CancellationToken cancellationToken)
    {
        ValidateLanePair(eml, campfire);
        RecursionLaneSegmentResult[] smoke = await RunPairAsync(
            eml,
            campfire,
            CreateTimedRequest(runID, RecursionMarathonStages.Smoke, RecursionMarathonDefaults.SmokeTicks),
            CreateTimedRequest(runID, RecursionMarathonStages.Smoke, RecursionMarathonDefaults.SmokeTicks),
            RecursionMarathonDefaults.SmokeTicks,
            cancellationToken).ConfigureAwait(false);
        ValidateTimedPair(smoke, RecursionMarathonStages.Smoke);

        RecursionLaneSegmentResult[] calibration = await RunPairAsync(
            eml,
            campfire,
            CreateTimedRequest(runID, RecursionMarathonStages.Calibration, RecursionMarathonDefaults.CalibrationTicks),
            CreateTimedRequest(runID, RecursionMarathonStages.Calibration, RecursionMarathonDefaults.CalibrationTicks),
            RecursionMarathonDefaults.CalibrationTicks,
            cancellationToken).ConfigureAwait(false);
        ValidateTimedPair(calibration, RecursionMarathonStages.Calibration);

        RecursionLaneCalibration emlCalibration = CreateCalibration(eml, smoke[0], calibration[0]);
        RecursionLaneCalibration campfireCalibration = CreateCalibration(campfire, smoke[1], calibration[1]);
        return (emlCalibration, campfireCalibration);
    }

    public static async Task<RecursionMarathonReport> RunAsync(
        RecursionMarathonManifest manifest,
        RecursionMarathonStages stage,
        IRecursionMarathonLane eml,
        IRecursionMarathonLane campfire,
        CancellationToken cancellationToken)
    {
        if (stage is not (RecursionMarathonStages.Baseline or RecursionMarathonStages.Graduated))
            throw new ArgumentException("a marathon run must be baseline or graduated", nameof(stage));
        manifest.Validate();
        ValidateLanePair(eml, campfire);

        RecursionLaneBudget emlBudget = manifest.GetLane(RecursionMarathonLanes.EMLProcedure);
        RecursionLaneBudget campfireBudget = manifest.GetLane(RecursionMarathonLanes.Campfire);
        ValidateLaneBudget(eml, emlBudget);
        ValidateLaneBudget(campfire, campfireBudget);

        List<long> emlTargets = CreateTargets(emlBudget.ConservedUnits, manifest.ForcedResumes);
        List<long> campfireTargets = CreateTargets(campfireBudget.ConservedUnits, manifest.ForcedResumes);
        List<RecursionLaneSegmentResult> segments = new();
        List<RecursionTerminationReceipt> terminations = new();
        string emlCheckpoint = "";
        string campfireCheckpoint = "";
        long marathonStartedAt = Stopwatch.GetTimestamp();

        for (int i = 0; i < emlTargets.Count; i++)
        {
            long elapsedTicks = ConvertStopwatchTicks(Stopwatch.GetTimestamp() - marathonStartedAt);
            long remainingWallTicks = manifest.HardWallTicks - elapsedTicks;
            if (remainingWallTicks <= 0) break;

            RecursionLaneSegmentRequest emlRequest = CreateBudgetRequest(
                manifest.RunID, stage, i, emlTargets[i], remainingWallTicks, emlCheckpoint, emlBudget);
            RecursionLaneSegmentRequest campfireRequest = CreateBudgetRequest(
                manifest.RunID, stage, i, campfireTargets[i], remainingWallTicks, campfireCheckpoint, campfireBudget);
            RecursionLaneSegmentResult[] pair = await RunPairAsync(
                eml, campfire, emlRequest, campfireRequest, remainingWallTicks, cancellationToken).ConfigureAwait(false);
            ValidateBudgetSegment(pair[0], emlRequest, RecursionMarathonLanes.EMLProcedure);
            ValidateBudgetSegment(pair[1], campfireRequest, RecursionMarathonLanes.Campfire);
            segments.Add(pair[0]);
            segments.Add(pair[1]);
            emlCheckpoint = pair[0].Checkpoint;
            campfireCheckpoint = pair[1].Checkpoint;

            bool forcedResume = i < emlTargets.Count - 1;
            if (!forcedResume) continue;
            RecursionTerminationReceipt[] killed = await TerminatePairAsync(
                eml, campfire, pair[0], pair[1], cancellationToken).ConfigureAwait(false);
            ValidateTermination(killed[0], pair[0]);
            ValidateTermination(killed[1], pair[1]);
            terminations.Add(killed[0]);
            terminations.Add(killed[1]);
        }

        RecursionLaneSegmentResult? finalEML = FindLastSegment(segments, RecursionMarathonLanes.EMLProcedure);
        RecursionLaneSegmentResult? finalCampfire = FindLastSegment(segments, RecursionMarathonLanes.Campfire);
        bool reached = finalEML is not null && finalCampfire is not null
            && finalEML.CompletedUnits >= emlBudget.ConservedUnits
            && finalCampfire.CompletedUnits >= campfireBudget.ConservedUnits;
        bool wallsExact = true;
        foreach (RecursionLaneSegmentResult segment in segments) wallsExact &= segment.Wall.IsExact;
        bool checkpointsExact = ValidateCheckpointChain(segments, terminations);
        List<RecursionLaneClassification> classifications = new();
        if (finalEML is not null) classifications.Add(RecursionMarathonClassifier.ClassifyEML(GatherWindows(segments, RecursionMarathonLanes.EMLProcedure), manifest.Seed));
        if (finalCampfire is not null) classifications.Add(RecursionMarathonClassifier.ClassifyCampfire(GatherWindows(segments, RecursionMarathonLanes.Campfire)));

        return new RecursionMarathonReport
        {
            RunID = manifest.RunID,
            Stage = stage,
            Segments = segments,
            Terminations = terminations,
            Classifications = classifications,
            ReachedBothBudgets = reached,
            CheckpointsExact = checkpointsExact,
            WallAccountingExact = wallsExact
        };
    }

    private static RecursionLaneSegmentRequest CreateTimedRequest(string runID, RecursionMarathonStages stage, long wallTicks)
        => new()
        {
            RunID = runID,
            Stage = stage,
            SegmentIndex = 0,
            TargetUnits = long.MaxValue,
            WallLimitTicks = wallTicks,
            IsResume = false,
            ResumeCheckpoint = "",
            StopCondition = null
        };

    private static RecursionLaneSegmentRequest CreateBudgetRequest(
        string runID,
        RecursionMarathonStages stage,
        int segmentIndex,
        long target,
        long wallTicks,
        string checkpoint,
        RecursionLaneBudget budget)
        => new()
        {
            RunID = runID,
            Stage = stage,
            SegmentIndex = segmentIndex,
            TargetUnits = target,
            WallLimitTicks = wallTicks,
            IsResume = checkpoint.Length > 0,
            ResumeCheckpoint = checkpoint,
            StopCondition = new CortexStopCondition(budget.ProgressSelector, target)
        };

    private static async Task<RecursionLaneSegmentResult[]> RunPairAsync(
        IRecursionMarathonLane eml,
        IRecursionMarathonLane campfire,
        RecursionLaneSegmentRequest emlRequest,
        RecursionLaneSegmentRequest campfireRequest,
        CancellationToken cancellationToken)
        => await RunPairAsync(eml, campfire, emlRequest, campfireRequest, 0, cancellationToken).ConfigureAwait(false);

    private static async Task<RecursionLaneSegmentResult[]> RunPairAsync(
        IRecursionMarathonLane eml,
        IRecursionMarathonLane campfire,
        RecursionLaneSegmentRequest emlRequest,
        RecursionLaneSegmentRequest campfireRequest,
        long hardWallTicks,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource wall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (hardWallTicks > 0) wall.CancelAfter(TimeSpan.FromTicks(hardWallTicks));
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RecursionLaneSegmentResult> emlTask = RunAfterStartAsync(eml, emlRequest, start.Task, wall.Token);
        Task<RecursionLaneSegmentResult> campfireTask = RunAfterStartAsync(campfire, campfireRequest, start.Task, wall.Token);
        start.SetResult();
        return await Task.WhenAll(emlTask, campfireTask).ConfigureAwait(false);
    }

    private static async Task<RecursionLaneSegmentResult> RunAfterStartAsync(
        IRecursionMarathonLane lane,
        RecursionLaneSegmentRequest request,
        Task start,
        CancellationToken cancellationToken)
    {
        await start.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await lane.RunSegmentAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RecursionTerminationReceipt[]> TerminatePairAsync(
        IRecursionMarathonLane eml,
        IRecursionMarathonLane campfire,
        RecursionLaneSegmentResult emlSegment,
        RecursionLaneSegmentResult campfireSegment,
        CancellationToken cancellationToken)
    {
        Task<RecursionTerminationReceipt> emlTask = eml.TerminateAsync(emlSegment, cancellationToken);
        Task<RecursionTerminationReceipt> campfireTask = campfire.TerminateAsync(campfireSegment, cancellationToken);
        return await Task.WhenAll(emlTask, campfireTask).ConfigureAwait(false);
    }

    private static RecursionLaneCalibration CreateCalibration(
        IRecursionMarathonLane lane,
        RecursionLaneSegmentResult smoke,
        RecursionLaneSegmentResult calibration)
    {
        if (smoke.Lane != lane.Lane || calibration.Lane != lane.Lane)
            throw new InvalidDataException($"{lane.Lane} calibration adapter returned another lane's result");
        if (calibration.Windows.Count != RecursionMarathonDefaults.CalibrationBins)
            throw new InvalidDataException("calibration lane must return six final-tail windows");
        List<RecursionCalibrationBin> bins = new(calibration.Windows.Count);
        foreach (RecursionMarathonWindow window in calibration.Windows)
            bins.Add(new RecursionCalibrationBin(window.CompletedUnits, window.WallTicks));
        return new RecursionLaneCalibration
        {
            Lane = lane.Lane,
            ProgressSelector = lane.ProgressSelector,
            SmokeCompletedUnits = smoke.CompletedUnits,
            TailBins = bins,
            SmokeWall = smoke.Wall,
            CalibrationWall = calibration.Wall
        };
    }

    private static List<long> CreateTargets(long budget, List<RecursionForcedResumePoint> forcedResumes)
    {
        List<long> targets = new(forcedResumes.Count + 1);
        foreach (RecursionForcedResumePoint point in forcedResumes) targets.Add(point.ResolveTarget(budget));
        targets.Add(budget);
        return targets;
    }

    private static void ValidateLanePair(IRecursionMarathonLane eml, IRecursionMarathonLane campfire)
    {
        if (eml.Lane != RecursionMarathonLanes.EMLProcedure) throw new ArgumentException("first marathon lane must be EML Procedure", nameof(eml));
        if (campfire.Lane != RecursionMarathonLanes.Campfire) throw new ArgumentException("second marathon lane must be Campfire", nameof(campfire));
        if (string.Equals(eml.ProgressSelector, campfire.ProgressSelector, StringComparison.Ordinal))
            throw new InvalidDataException("marathon lanes cannot share a progress selector");
    }

    private static void ValidateLaneBudget(IRecursionMarathonLane lane, RecursionLaneBudget budget)
    {
        if (lane.Lane != budget.Lane || !string.Equals(lane.ProgressSelector, budget.ProgressSelector, StringComparison.Ordinal))
            throw new InvalidDataException($"{lane.Lane} adapter does not match its frozen manifest budget");
    }

    private static void ValidateTimedPair(RecursionLaneSegmentResult[] pair, RecursionMarathonStages stage)
    {
        if (pair.Length != 2) throw new InvalidDataException($"{stage} did not return two lanes");
        foreach (RecursionLaneSegmentResult result in pair)
        {
            if (result.Stop == RecursionSegmentStops.Failed) throw new InvalidDataException($"{result.Lane} failed during {stage}");
            if (!result.Wall.IsExact) throw new InvalidDataException($"{result.Lane} has dark wall time during {stage}");
        }
    }

    private static void ValidateBudgetSegment(
        RecursionLaneSegmentResult result,
        RecursionLaneSegmentRequest request,
        RecursionMarathonLanes expectedLane)
    {
        if (result.Lane != expectedLane) throw new InvalidDataException($"expected {expectedLane} but adapter returned {result.Lane}");
        if (result.SegmentIndex != request.SegmentIndex) throw new InvalidDataException("lane returned the wrong segment index");
        if (result.Stop == RecursionSegmentStops.Failed) throw new InvalidDataException($"{result.Lane} segment failed");
        if (result.Stop == RecursionSegmentStops.Budget && result.CompletedUnits < request.TargetUnits)
            throw new InvalidDataException($"{result.Lane} stopped before its conserved-unit target");
        if (result.Checkpoint.Length == 0 || result.CheckpointDigest.Length == 0)
            throw new InvalidDataException($"{result.Lane} did not produce a checkpoint receipt");
    }

    private static void ValidateTermination(RecursionTerminationReceipt receipt, RecursionLaneSegmentResult segment)
    {
        if (receipt.Lane != segment.Lane || receipt.SegmentIndex != segment.SegmentIndex)
            throw new InvalidDataException("forced-termination receipt addresses the wrong segment");
        if (!receipt.ProcessWasForcedDown) throw new InvalidDataException($"{segment.Lane} performed a graceful stop instead of the forced-kill drill");
        if (!string.Equals(receipt.CheckpointDigestBeforeKill, segment.CheckpointDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"{segment.Lane} checkpoint changed before forced termination");
    }

    private static RecursionLaneSegmentResult? FindLastSegment(List<RecursionLaneSegmentResult> segments, RecursionMarathonLanes lane)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
            if (segments[i].Lane == lane) return segments[i];
        return null;
    }

    private static bool ValidateCheckpointChain(
        List<RecursionLaneSegmentResult> segments,
        List<RecursionTerminationReceipt> terminations)
    {
        foreach (RecursionTerminationReceipt termination in terminations)
        {
            RecursionLaneSegmentResult? segment = null;
            foreach (RecursionLaneSegmentResult candidate in segments)
                if (candidate.Lane == termination.Lane && candidate.SegmentIndex == termination.SegmentIndex) segment = candidate;
            if (segment is null || !termination.ProcessWasForcedDown ||
                !string.Equals(segment.CheckpointDigest, termination.CheckpointDigestBeforeKill, StringComparison.Ordinal)) return false;
        }
        foreach (RecursionMarathonLanes lane in Enum.GetValues<RecursionMarathonLanes>())
        {
            RecursionLaneSegmentResult? previous = null;
            foreach (RecursionLaneSegmentResult segment in segments)
            {
                if (segment.Lane != lane) continue;
                if (previous is not null)
                {
                    if (!string.Equals(segment.ResumedCheckpointDigest, previous.CheckpointDigest, StringComparison.Ordinal)) return false;
                    if (!string.Equals(segment.RestoredTapePrefixDigest, previous.TapePrefixDigest, StringComparison.Ordinal)) return false;
                    if (!string.Equals(segment.RestoredJournalPrefixDigest, previous.JournalPrefixDigest, StringComparison.Ordinal)) return false;
                }
                previous = segment;
            }
        }
        return true;
    }

    private static List<RecursionMarathonWindow> GatherWindows(
        List<RecursionLaneSegmentResult> segments,
        RecursionMarathonLanes lane)
    {
        List<RecursionMarathonWindow> windows = new();
        foreach (RecursionLaneSegmentResult segment in segments)
            if (segment.Lane == lane) windows.AddRange(segment.Windows);
        windows.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        return windows;
    }

    private static long ConvertStopwatchTicks(long ticks)
        => checked((long)Math.Floor(ticks * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
}
