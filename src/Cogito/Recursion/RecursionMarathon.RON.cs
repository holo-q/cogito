namespace Cogito;

using System.Globalization;
using Ronmamon;

internal sealed class RecursionMarathonRONCodec : IRecursionMarathonRONCodec
{
    public static RecursionMarathonRONCodec Instance { get; } = new();

    private RecursionMarathonRONCodec() { }

    public byte[] EncodeManifest(RecursionMarathonManifest manifest)
    {
        RecursionRONMarathonManifest document = CreateManifestDocument(manifest);
        return RonSerializer.SerializeToUtf8(in document);
    }

    public RecursionMarathonManifest DecodeManifest(ReadOnlySpan<byte> bytes)
    {
        RecursionRONMarathonManifest document = RonSerializer.Deserialize<RecursionRONMarathonManifest>(bytes);
        RecursionMarathonManifest manifest = RestoreManifest(document);
        manifest.Validate();
        return manifest;
    }

    public byte[] EncodeReport(RecursionMarathonReport report)
    {
        RecursionRONMarathonReport document = CreateReportDocument(report);
        return RonSerializer.SerializeToUtf8(in document);
    }

    public RecursionMarathonReport DecodeReport(ReadOnlySpan<byte> bytes)
    {
        RecursionRONMarathonReport document = RonSerializer.Deserialize<RecursionRONMarathonReport>(bytes);
        RequireVersion(document.schemaVersion, "report");
        List<RecursionLaneSegmentResult> segments = new(document.segments.Count);
        for (int i = 0; i < document.segments.Count; i++) segments.Add(RestoreSegment(document.segments[i]));
        List<RecursionTerminationReceipt> terminations = new(document.terminations.Count);
        for (int i = 0; i < document.terminations.Count; i++)
            terminations.Add(RestoreTermination(document.terminations[i]));
        List<RecursionLaneClassification> classifications = new(document.classifications.Count);
        for (int i = 0; i < document.classifications.Count; i++)
            classifications.Add(RestoreClassification(document.classifications[i]));
        return new RecursionMarathonReport
        {
            SchemaVersion = document.schemaVersion,
            RunID = RequireText(document.runID, "report run ID"),
            Stage = document.stage,
            Segments = segments,
            Terminations = terminations,
            Classifications = classifications,
            ReachedBothBudgets = document.reachedBothBudgets,
            CheckpointsExact = document.checkpointsExact,
            WallAccountingExact = document.wallAccountingExact,
        };
    }

    private static RecursionRONMarathonManifest CreateManifestDocument(RecursionMarathonManifest manifest)
    {
        RecursionRONMarathonManifest document = new()
        {
            schemaVersion = manifest.SchemaVersion,
            runID = manifest.RunID,
            seed = manifest.Seed.ToString("x16", CultureInfo.InvariantCulture),
            intakeDigest = manifest.IntakeDigest,
            branchDigest = manifest.BranchDigest,
            launchUnixSeconds = manifest.LaunchUnixSeconds,
            smokeTicks = manifest.SmokeTicks,
            calibrationTicks = manifest.CalibrationTicks,
            equivalentRunTicks = manifest.EquivalentRunTicks,
            hardWallTicks = manifest.HardWallTicks,
        };
        for (int i = 0; i < manifest.Lanes.Count; i++)
        {
            RecursionLaneBudget lane = manifest.Lanes[i];
            document.lanes.Add(new RecursionRONLaneBudget
            {
                lane = lane.Lane,
                progressSelector = lane.ProgressSelector,
                conservedUnits = lane.ConservedUnits,
            });
        }
        for (int i = 0; i < manifest.ForcedResumes.Count; i++)
        {
            RecursionForcedResumePoint point = manifest.ForcedResumes[i];
            document.forcedResumes.Add(new RecursionRONForcedResume
            {
                numerator = point.Numerator,
                denominator = point.Denominator,
            });
        }
        return document;
    }

    private static RecursionMarathonManifest RestoreManifest(RecursionRONMarathonManifest document)
    {
        RequireVersion(document.schemaVersion, "manifest");
        List<RecursionLaneBudget> lanes = new(document.lanes.Count);
        for (int i = 0; i < document.lanes.Count; i++)
        {
            RecursionRONLaneBudget lane = document.lanes[i];
            lanes.Add(new RecursionLaneBudget
            {
                Lane = lane.lane,
                ProgressSelector = RequireText(lane.progressSelector, "lane progress selector"),
                ConservedUnits = RequirePositive(lane.conservedUnits, "lane conserved units"),
            });
        }
        List<RecursionForcedResumePoint> forcedResumes = new(document.forcedResumes.Count);
        for (int i = 0; i < document.forcedResumes.Count; i++)
        {
            RecursionRONForcedResume point = document.forcedResumes[i];
            RecursionForcedResumePoint restored = new(point.numerator, point.denominator);
            restored.ResolveTarget(12);
            forcedResumes.Add(restored);
        }
        return new RecursionMarathonManifest
        {
            SchemaVersion = document.schemaVersion,
            RunID = RequireText(document.runID, "manifest run ID"),
            Seed = ParseHex(document.seed, "marathon seed"),
            IntakeDigest = RequireText(document.intakeDigest, "manifest intake digest"),
            BranchDigest = RequireText(document.branchDigest, "manifest branch digest"),
            LaunchUnixSeconds = document.launchUnixSeconds,
            SmokeTicks = RequirePositive(document.smokeTicks, "smoke ticks"),
            CalibrationTicks = RequirePositive(document.calibrationTicks, "calibration ticks"),
            EquivalentRunTicks = RequirePositive(document.equivalentRunTicks, "equivalent-run ticks"),
            HardWallTicks = RequirePositive(document.hardWallTicks, "hard-wall ticks"),
            Lanes = lanes,
            ForcedResumes = forcedResumes,
        };
    }

    private static RecursionRONMarathonReport CreateReportDocument(RecursionMarathonReport report)
    {
        RecursionRONMarathonReport document = new()
        {
            schemaVersion = report.SchemaVersion,
            runID = report.RunID,
            stage = report.Stage,
            reachedBothBudgets = report.ReachedBothBudgets,
            checkpointsExact = report.CheckpointsExact,
            wallAccountingExact = report.WallAccountingExact,
        };
        for (int i = 0; i < report.Segments.Count; i++)
            document.segments.Add(CreateSegmentDocument(report.Segments[i]));
        for (int i = 0; i < report.Terminations.Count; i++)
            document.terminations.Add(CreateTerminationDocument(report.Terminations[i]));
        for (int i = 0; i < report.Classifications.Count; i++)
            document.classifications.Add(CreateClassificationDocument(report.Classifications[i]));
        return document;
    }

    private static RecursionRONSegment CreateSegmentDocument(RecursionLaneSegmentResult segment)
    {
        RecursionRONSegment document = new()
        {
            lane = segment.Lane,
            segmentIndex = segment.SegmentIndex,
            completedUnits = segment.CompletedUnits,
            stop = segment.Stop,
            checkpoint = segment.Checkpoint,
            checkpointDigest = segment.CheckpointDigest,
            tapePrefixDigest = segment.TapePrefixDigest,
            journalPrefixDigest = segment.JournalPrefixDigest,
            resumedCheckpointDigest = segment.ResumedCheckpointDigest,
            restoredTapePrefixDigest = segment.RestoredTapePrefixDigest,
            restoredJournalPrefixDigest = segment.RestoredJournalPrefixDigest,
            wall = CreateWallDocument(segment.Wall),
        };
        for (int i = 0; i < segment.Windows.Count; i++)
            document.windows.Add(CreateWindowDocument(segment.Windows[i]));
        return document;
    }

    private static RecursionRONWall CreateWallDocument(RecursionWallReport wall)
    {
        RecursionRONWall document = new()
        {
            totalStopwatchTicks = wall.TotalStopwatchTicks,
            unaccountedStopwatchTicks = wall.UnaccountedStopwatchTicks,
        };
        for (int i = 0; i < wall.Phases.Count; i++)
        {
            RecursionWallPhaseTime phase = wall.Phases[i];
            document.phases.Add(new RecursionRONWallPhase
            {
                phase = phase.Phase,
                stopwatchTicks = phase.StopwatchTicks,
            });
        }
        return document;
    }

    private static RecursionRONWindow CreateWindowDocument(RecursionMarathonWindow window)
    {
        RecursionRONWindow document = new()
        {
            index = window.Index,
            completedUnits = window.CompletedUnits,
            wallTicks = window.WallTicks,
            canonicalDeltas = window.CanonicalDeltas,
            lawClasses = window.LawClasses,
            proofAttachments = window.ProofAttachments,
            frontierHighWater = window.FrontierHighWater,
            procedureReuses = window.ProcedureReuses,
        };
        for (int i = 0; i < window.Stability.Count; i++)
        {
            RecursionMetricObservation metric = window.Stability[i];
            document.stability.Add(new RecursionRONMetric
            {
                name = metric.Name,
                value = metric.Value,
                baselineLow = metric.BaselineLow,
                baselineHigh = metric.BaselineHigh,
            });
        }
        return document;
    }

    private static RecursionRONTermination CreateTerminationDocument(RecursionTerminationReceipt termination)
        => new()
        {
            lane = termination.Lane,
            segmentIndex = termination.SegmentIndex,
            processWasForcedDown = termination.ProcessWasForcedDown,
            checkpointDigestBeforeKill = termination.CheckpointDigestBeforeKill,
        };

    private static RecursionRONClassification CreateClassificationDocument(RecursionLaneClassification classification)
    {
        RecursionRONClassification document = new()
        {
            lane = classification.Lane,
            classification = classification.Classification,
            theilSenSlope = classification.TheilSenSlope,
            bootstrapLow = classification.BootstrapLow,
            bootstrapHigh = classification.BootstrapHigh,
            firstThirdRate = classification.FirstThirdRate,
            finalThirdRate = classification.FinalThirdRate,
            stableMetricWindows = classification.StableMetricWindows,
            totalMetricWindows = classification.TotalMetricWindows,
        };
        document.breaches.AddRange(classification.Breaches);
        return document;
    }

    private static RecursionLaneSegmentResult RestoreSegment(RecursionRONSegment document)
    {
        List<RecursionMarathonWindow> windows = new(document.windows.Count);
        for (int i = 0; i < document.windows.Count; i++) windows.Add(RestoreWindow(document.windows[i]));
        return new RecursionLaneSegmentResult
        {
            Lane = document.lane,
            SegmentIndex = RequireNonNegative(document.segmentIndex, "segment index"),
            CompletedUnits = RequireNonNegative(document.completedUnits, "segment completed units"),
            Stop = document.stop,
            Checkpoint = document.checkpoint ?? "",
            CheckpointDigest = document.checkpointDigest ?? "",
            TapePrefixDigest = document.tapePrefixDigest ?? "",
            JournalPrefixDigest = document.journalPrefixDigest ?? "",
            ResumedCheckpointDigest = document.resumedCheckpointDigest ?? "",
            RestoredTapePrefixDigest = document.restoredTapePrefixDigest ?? "",
            RestoredJournalPrefixDigest = document.restoredJournalPrefixDigest ?? "",
            Wall = RestoreWall(document.wall),
            Windows = windows,
        };
    }

    private static RecursionWallReport RestoreWall(RecursionRONWall document)
    {
        List<RecursionWallPhaseTime> phases = new(document.phases.Count);
        for (int i = 0; i < document.phases.Count; i++)
        {
            RecursionRONWallPhase phase = document.phases[i];
            phases.Add(new RecursionWallPhaseTime(
                phase.phase,
                RequireNonNegative(phase.stopwatchTicks, "wall phase ticks")));
        }
        RecursionWallReport wall = RecursionWallReport.Create(
            RequireNonNegative(document.totalStopwatchTicks, "total wall ticks"),
            phases);
        if (wall.UnaccountedStopwatchTicks != document.unaccountedStopwatchTicks)
            throw new InvalidDataException("marathon RON wall accounting does not balance");
        return wall;
    }

    private static RecursionMarathonWindow RestoreWindow(RecursionRONWindow document)
    {
        List<RecursionMetricObservation> stability = new(document.stability.Count);
        for (int i = 0; i < document.stability.Count; i++)
        {
            RecursionRONMetric metric = document.stability[i];
            stability.Add(new RecursionMetricObservation(
                RequireText(metric.name, "stability metric name"),
                metric.value,
                metric.baselineLow,
                metric.baselineHigh));
        }
        return new RecursionMarathonWindow
        {
            Index = RequireNonNegative(document.index, "window index"),
            CompletedUnits = RequireNonNegative(document.completedUnits, "window completed units"),
            WallTicks = RequirePositive(document.wallTicks, "window wall ticks"),
            CanonicalDeltas = RequireNonNegative(document.canonicalDeltas, "window canonical deltas"),
            LawClasses = RequireNonNegative(document.lawClasses, "window law classes"),
            ProofAttachments = RequireNonNegative(document.proofAttachments, "window proof attachments"),
            FrontierHighWater = RequireNonNegative(document.frontierHighWater, "window frontier high-water"),
            ProcedureReuses = RequireNonNegative(document.procedureReuses, "window procedure reuses"),
            Stability = stability,
        };
    }

    private static RecursionTerminationReceipt RestoreTermination(RecursionRONTermination document)
        => new()
        {
            Lane = document.lane,
            SegmentIndex = RequireNonNegative(document.segmentIndex, "termination segment index"),
            ProcessWasForcedDown = document.processWasForcedDown,
            CheckpointDigestBeforeKill = RequireText(
                document.checkpointDigestBeforeKill,
                "termination checkpoint digest"),
        };

    private static RecursionLaneClassification RestoreClassification(RecursionRONClassification document)
        => new()
        {
            Lane = document.lane,
            Classification = document.classification,
            TheilSenSlope = document.theilSenSlope,
            BootstrapLow = document.bootstrapLow,
            BootstrapHigh = document.bootstrapHigh,
            FirstThirdRate = document.firstThirdRate,
            FinalThirdRate = document.finalThirdRate,
            StableMetricWindows = RequireNonNegative(document.stableMetricWindows, "stable metric windows"),
            TotalMetricWindows = RequireNonNegative(document.totalMetricWindows, "total metric windows"),
            Breaches = new List<string>(document.breaches),
        };

    private static void RequireVersion(int actual, string artifact)
    {
        if (actual != RecursionMarathonDefaults.SchemaVersion)
            throw new InvalidDataException(
                $"unsupported marathon {artifact} RON schema {actual}; expected {RecursionMarathonDefaults.SchemaVersion}");
    }

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"marathon RON omits {field}");
        return value;
    }

    private static int RequireNonNegative(int value, string field)
    {
        if (value < 0) throw new InvalidDataException($"marathon RON {field} is negative");
        return value;
    }

    private static long RequireNonNegative(long value, string field)
    {
        if (value < 0) throw new InvalidDataException($"marathon RON {field} is negative");
        return value;
    }

    private static long RequirePositive(long value, string field)
    {
        if (value <= 0) throw new InvalidDataException($"marathon RON {field} is not positive");
        return value;
    }

    private static ulong ParseHex(string? value, string field)
    {
        if (value is null || value.Length != 16
            || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsed))
            throw new InvalidDataException($"marathon RON {field} is not a canonical 16-digit hexadecimal value");
        return parsed;
    }
}

[RonObject]
internal partial class RecursionRONMarathonManifest
{
    public int schemaVersion;
    public string runID = "";
    public string seed = "";
    public string intakeDigest = "";
    public string branchDigest = "";
    public long launchUnixSeconds;
    public long smokeTicks;
    public long calibrationTicks;
    public long equivalentRunTicks;
    public long hardWallTicks;
    public List<RecursionRONLaneBudget> lanes = new();
    public List<RecursionRONForcedResume> forcedResumes = new();
}

[RonObject]
internal partial class RecursionRONLaneBudget
{
    public RecursionMarathonLanes lane;
    public string progressSelector = "";
    public long conservedUnits;
}

[RonObject]
internal partial class RecursionRONForcedResume
{
    public int numerator;
    public int denominator;
}

[RonObject]
internal partial class RecursionRONMarathonReport
{
    public int schemaVersion;
    public string runID = "";
    public RecursionMarathonStages stage;
    public List<RecursionRONSegment> segments = new();
    public List<RecursionRONTermination> terminations = new();
    public List<RecursionRONClassification> classifications = new();
    public bool reachedBothBudgets;
    public bool checkpointsExact;
    public bool wallAccountingExact;
}

[RonObject]
internal partial class RecursionRONSegment
{
    public RecursionMarathonLanes lane;
    public int segmentIndex;
    public long completedUnits;
    public RecursionSegmentStops stop;
    public string checkpoint = "";
    public string checkpointDigest = "";
    public string tapePrefixDigest = "";
    public string journalPrefixDigest = "";
    public string resumedCheckpointDigest = "";
    public string restoredTapePrefixDigest = "";
    public string restoredJournalPrefixDigest = "";
    public RecursionRONWall wall = new();
    public List<RecursionRONWindow> windows = new();
}

[RonObject]
internal partial class RecursionRONWall
{
    public long totalStopwatchTicks;
    public List<RecursionRONWallPhase> phases = new();
    public long unaccountedStopwatchTicks;
}

[RonObject]
internal partial class RecursionRONWallPhase
{
    public RecursionWallPhases phase;
    public long stopwatchTicks;
}

[RonObject]
internal partial class RecursionRONWindow
{
    public int index;
    public long completedUnits;
    public long wallTicks;
    public long canonicalDeltas;
    public long lawClasses;
    public long proofAttachments;
    public long frontierHighWater;
    public long procedureReuses;
    public List<RecursionRONMetric> stability = new();
}

[RonObject]
internal partial class RecursionRONMetric
{
    public string name = "";
    public double value;
    public double baselineLow;
    public double baselineHigh;
}

[RonObject]
internal partial class RecursionRONTermination
{
    public RecursionMarathonLanes lane;
    public int segmentIndex;
    public bool processWasForcedDown;
    public string checkpointDigestBeforeKill = "";
}

[RonObject]
internal partial class RecursionRONClassification
{
    public RecursionMarathonLanes lane;
    public RecursionMarathonClassifications classification;
    public double theilSenSlope;
    public double bootstrapLow;
    public double bootstrapHigh;
    public double firstThirdRate;
    public double finalThirdRate;
    public int stableMetricWindows;
    public int totalMetricWindows;
    public List<string> breaches = new();
}
