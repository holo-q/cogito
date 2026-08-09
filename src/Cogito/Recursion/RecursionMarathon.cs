namespace Cogito;

internal enum RecursionMarathonLanes : byte
{
    EMLProcedure,
    Campfire
}

internal enum RecursionMarathonStages : byte
{
    Smoke,
    Calibration,
    Baseline,
    Graduated
}

internal enum RecursionSegmentStops : byte
{
    Budget,
    WallCap,
    Failed
}

internal enum RecursionWallPhases : byte
{
    StartupLoad,
    CurriculumDraw,
    ActionSelection,
    ToolExecution,
    Reward,
    Consolidation,
    ConsolidationPhase,
    Checkpoint,
    WorkspaceReadout,
    OrchestrationExchange,
    DeliberateWait,
    Shutdown
}

internal static class RecursionMarathonDefaults
{
    public const int SchemaVersion = 1;
    public const int CalibrationBins = 6;
    public const int ClassificationWindows = 6;
    public const int BootstrapDraws = 4096;
    public const long SmokeTicks = TimeSpan.TicksPerMinute * 10;
    public const long CalibrationTicks = TimeSpan.TicksPerHour * 2;
    public const long CalibrationTailTicks = TimeSpan.TicksPerMinute * 30;
    public const long EquivalentRunTicks = TimeSpan.TicksPerHour * 72;
    public const long HardWallTicks = TimeSpan.TicksPerHour * 96;
    public const string EMLSelector = "eml.evaluator.calls";
}

internal readonly record struct RecursionCalibrationBin(long CompletedUnits, long WallTicks)
{
    public double ReadUnitsPerSecond()
    {
        if (CompletedUnits < 0) throw new InvalidDataException("calibration completed units cannot be negative");
        if (WallTicks <= 0) throw new InvalidDataException("calibration wall ticks must be positive");
        return CompletedUnits / TimeSpan.FromTicks(WallTicks).TotalSeconds;
    }
}

internal sealed class RecursionLaneCalibration
{
    public required RecursionMarathonLanes Lane { get; init; }
    public required string ProgressSelector { get; init; }
    public required long SmokeCompletedUnits { get; init; }
    public required List<RecursionCalibrationBin> TailBins { get; init; }
    public required RecursionWallReport SmokeWall { get; init; }
    public required RecursionWallReport CalibrationWall { get; init; }

    public long ComputeEquivalentBudget()
    {
        if (TailBins.Count != RecursionMarathonDefaults.CalibrationBins)
            throw new InvalidDataException($"calibration requires exactly {RecursionMarathonDefaults.CalibrationBins} tail bins");

        double[] rates = new double[TailBins.Count];
        long expectedBinTicks = RecursionMarathonDefaults.CalibrationTailTicks / RecursionMarathonDefaults.CalibrationBins;
        for (int i = 0; i < TailBins.Count; i++)
        {
            if (TailBins[i].WallTicks != expectedBinTicks)
                throw new InvalidDataException($"calibration tail bin {i} must cover exactly five minutes");
            rates[i] = TailBins[i].ReadUnitsPerSecond();
        }
        Array.Sort(rates);
        int conservativeIndex = (int)Math.Floor(0.25 * (rates.Length - 1));
        double conservativeRate = rates[conservativeIndex];
        long budget = checked((long)Math.Floor(conservativeRate * TimeSpan.FromTicks(RecursionMarathonDefaults.EquivalentRunTicks).TotalSeconds));
        if (budget <= 0) throw new InvalidDataException($"{Lane} calibration produced a zero 72-hour-equivalent budget");
        return budget;
    }
}

internal readonly record struct RecursionForcedResumePoint(int Numerator, int Denominator)
{
    public long ResolveTarget(long budget)
    {
        if (Numerator <= 0 || Denominator <= 0 || Numerator >= Denominator)
            throw new InvalidDataException("forced-resume fractions must be inside (0,1)");
        return checked((budget / Denominator * Numerator) + ((budget % Denominator) * Numerator / Denominator));
    }
}

internal sealed class RecursionLaneBudget
{
    public required RecursionMarathonLanes Lane { get; init; }
    public required string ProgressSelector { get; init; }
    public required long ConservedUnits { get; init; }
}

internal sealed class RecursionMarathonManifest
{
    public int SchemaVersion { get; init; } = RecursionMarathonDefaults.SchemaVersion;
    public required string RunID { get; init; }
    public required ulong Seed { get; init; }
    public required string IntakeDigest { get; init; }
    public required string BranchDigest { get; init; }
    public required long LaunchUnixSeconds { get; init; }
    public long SmokeTicks { get; init; } = RecursionMarathonDefaults.SmokeTicks;
    public long CalibrationTicks { get; init; } = RecursionMarathonDefaults.CalibrationTicks;
    public long EquivalentRunTicks { get; init; } = RecursionMarathonDefaults.EquivalentRunTicks;
    public long HardWallTicks { get; init; } = RecursionMarathonDefaults.HardWallTicks;
    public required List<RecursionLaneBudget> Lanes { get; init; }
    public List<RecursionForcedResumePoint> ForcedResumes { get; init; } =
    [
        new RecursionForcedResumePoint(1, 12),
        new RecursionForcedResumePoint(1, 3),
        new RecursionForcedResumePoint(2, 3)
    ];

    public RecursionLaneBudget GetLane(RecursionMarathonLanes lane)
    {
        RecursionLaneBudget? found = null;
        foreach (RecursionLaneBudget candidate in Lanes)
        {
            if (candidate.Lane != lane) continue;
            if (found is not null) throw new InvalidDataException($"marathon manifest repeats lane {lane}");
            found = candidate;
        }
        return found ?? throw new InvalidDataException($"marathon manifest omits lane {lane}");
    }

    public void Validate()
    {
        if (SchemaVersion != RecursionMarathonDefaults.SchemaVersion)
            throw new InvalidDataException($"unsupported marathon manifest version {SchemaVersion}");
        if (string.IsNullOrWhiteSpace(RunID)) throw new InvalidDataException("marathon run ID cannot be blank");
        if (string.IsNullOrWhiteSpace(IntakeDigest)) throw new InvalidDataException("marathon intake digest cannot be blank");
        if (string.IsNullOrWhiteSpace(BranchDigest)) throw new InvalidDataException("marathon branch digest cannot be blank");
        if (SmokeTicks <= 0 || CalibrationTicks <= 0 || EquivalentRunTicks <= 0 || HardWallTicks <= 0)
            throw new InvalidDataException("marathon durations must be positive");
        if (HardWallTicks < EquivalentRunTicks) throw new InvalidDataException("hard wall cannot precede the equivalent-run horizon");
        RecursionLaneBudget eml = GetLane(RecursionMarathonLanes.EMLProcedure);
        RecursionLaneBudget campfire = GetLane(RecursionMarathonLanes.Campfire);
        if (!string.Equals(eml.ProgressSelector, RecursionMarathonDefaults.EMLSelector, StringComparison.Ordinal))
            throw new InvalidDataException($"EML marathon selector must be '{RecursionMarathonDefaults.EMLSelector}'");
        if (string.IsNullOrWhiteSpace(campfire.ProgressSelector)) throw new InvalidDataException("Campfire progress selector cannot be blank");
        if (eml.ConservedUnits <= 0 || campfire.ConservedUnits <= 0) throw new InvalidDataException("marathon budgets must be positive");

        long previousEML = 0;
        long previousCampfire = 0;
        foreach (RecursionForcedResumePoint point in ForcedResumes)
        {
            long emlTarget = point.ResolveTarget(eml.ConservedUnits);
            long campfireTarget = point.ResolveTarget(campfire.ConservedUnits);
            if (emlTarget <= previousEML || campfireTarget <= previousCampfire)
                throw new InvalidDataException("forced-resume targets must be strictly increasing in both lanes");
            previousEML = emlTarget;
            previousCampfire = campfireTarget;
        }
    }
}

internal static class RecursionMarathonAuthority
{
    public static RecursionMarathonManifest CreateManifest(
        string runID,
        ulong seed,
        string intakeDigest,
        string branchDigest,
        long launchUnixSeconds,
        RecursionLaneCalibration eml,
        RecursionLaneCalibration campfire)
    {
        if (eml.Lane != RecursionMarathonLanes.EMLProcedure) throw new ArgumentException("EML calibration has the wrong lane", nameof(eml));
        if (campfire.Lane != RecursionMarathonLanes.Campfire) throw new ArgumentException("Campfire calibration has the wrong lane", nameof(campfire));
        if (!eml.SmokeWall.IsExact || !campfire.SmokeWall.IsExact || !eml.CalibrationWall.IsExact || !campfire.CalibrationWall.IsExact)
            throw new InvalidDataException("a calibration with dark wall time cannot mint a marathon manifest");

        RecursionMarathonManifest manifest = new()
        {
            RunID = runID,
            Seed = seed,
            IntakeDigest = intakeDigest,
            BranchDigest = branchDigest,
            LaunchUnixSeconds = launchUnixSeconds,
            Lanes =
            [
                new RecursionLaneBudget
                {
                    Lane = RecursionMarathonLanes.EMLProcedure,
                    ProgressSelector = eml.ProgressSelector,
                    ConservedUnits = eml.ComputeEquivalentBudget()
                },
                new RecursionLaneBudget
                {
                    Lane = RecursionMarathonLanes.Campfire,
                    ProgressSelector = campfire.ProgressSelector,
                    ConservedUnits = campfire.ComputeEquivalentBudget()
                }
            ]
        };
        manifest.Validate();
        return manifest;
    }

    public static byte[] EncodeManifest(IRecursionMarathonRONCodec codec, RecursionMarathonManifest manifest)
    {
        manifest.Validate();
        byte[] first = codec.EncodeManifest(manifest);
        byte[] second = codec.EncodeManifest(manifest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("marathon manifest codec is nondeterministic");
        RecursionMarathonManifest restored = codec.DecodeManifest(first);
        restored.Validate();
        byte[] roundTrip = codec.EncodeManifest(restored);
        if (!first.AsSpan().SequenceEqual(roundTrip)) throw new InvalidDataException("marathon manifest RON round-trip changed bytes");
        return first;
    }

    public static byte[] EncodeReport(IRecursionMarathonRONCodec codec, RecursionMarathonReport report)
    {
        if (report.SchemaVersion != RecursionMarathonDefaults.SchemaVersion)
            throw new InvalidDataException($"unsupported marathon report version {report.SchemaVersion}");
        byte[] first = codec.EncodeReport(report);
        byte[] second = codec.EncodeReport(report);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("marathon report codec is nondeterministic");
        RecursionMarathonReport restored = codec.DecodeReport(first);
        byte[] roundTrip = codec.EncodeReport(restored);
        if (!first.AsSpan().SequenceEqual(roundTrip)) throw new InvalidDataException("marathon report RON round-trip changed bytes");
        return first;
    }
}

internal interface IRecursionMarathonRONCodec
{
    byte[] EncodeManifest(RecursionMarathonManifest manifest);
    RecursionMarathonManifest DecodeManifest(ReadOnlySpan<byte> bytes);
    byte[] EncodeReport(RecursionMarathonReport report);
    RecursionMarathonReport DecodeReport(ReadOnlySpan<byte> bytes);
}
