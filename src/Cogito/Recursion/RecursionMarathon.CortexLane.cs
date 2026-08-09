namespace Cogito;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

internal readonly record struct RecursionCampfireStabilityBand(string Name, double Low, double High);

internal readonly record struct RecursionLaneMetricValue(string Name, double Value);

internal sealed class RecursionLaneMetrics
{
    public required long CompletedUnits { get; init; }
    public required long CanonicalDeltas { get; init; }
    public required long LawClasses { get; init; }
    public required long ProofAttachments { get; init; }
    public required long FrontierHighWater { get; init; }
    public required long ProcedureReuses { get; init; }
    public required List<RecursionLaneMetricValue> Stability { get; init; }
}

internal abstract class CortexRecursionMarathonLane : IRecursionMarathonLane
{
    private readonly long _classificationUnits;
    private readonly List<RecursionCampfireStabilityBand> _stabilityBands;
    private readonly List<RecursionMarathonWindow> _windows = new();
    private RecursionLaneMetrics _previousMetrics = EmptyMetrics();
    private long _previousUnits;
    private int _nextWindow;
    private bool _calibrationTailPrimed;
    private Cortex? _ownedRuntime;
    private string _ownedCheckpoint = "";
    private IncrementalHash? _tapePrefixHash;
    private IncrementalHash? _journalPrefixHash;
    private long _tapeAppendMark;
    private long _tapeRevision;
    private long _tapeOrderRevision;
    private int _journalLineMark;
    private string _lastCheckpointPath = "";
    private string _lastCheckpointDigest = "";

    protected CortexRecursionMarathonLane(
        long classificationUnits,
        List<RecursionCampfireStabilityBand>? stabilityBands = null)
    {
        if (classificationUnits < 0) throw new ArgumentOutOfRangeException(nameof(classificationUnits));
        _classificationUnits = classificationUnits;
        _stabilityBands = stabilityBands is null ? [] : [.. stabilityBands];
    }

    public abstract RecursionMarathonLanes Lane { get; }
    public abstract string ProgressSelector { get; }

    protected abstract Cortex CreateCortex(RecursionLaneSegmentRequest request, RecursionLaneProbe probe);

    protected abstract RecursionLaneMetrics ReadMetrics(Cortex cortex);

    public async Task<RecursionLaneSegmentResult> RunSegmentAsync(
        RecursionLaneSegmentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_ownedRuntime is not null)
            throw new InvalidOperationException($"{Lane} still owns a runtime; TerminateAsync must retire it before resume");

        if (!request.IsResume) ResetWindowJournal();
        int firstWindow = _windows.Count;
        long totalStartedAt = Stopwatch.GetTimestamp();
        RecursionLaneProbe probe = new(this, request, cancellationToken, totalStartedAt);
        Cortex cortex = CreateCortex(request, probe);
        _ownedRuntime = cortex;
        long driveStartedAt = Stopwatch.GetTimestamp();

        Func<int> drive = request.IsResume
            ? () => cortex.Resume(request.ResumeCheckpoint, int.MaxValue)
            : cortex.Run;
        int exitCode = await Task.Run(drive, CancellationToken.None).ConfigureAwait(false);

        long driveFinishedAt = Stopwatch.GetTimestamp();
        string runDirectory = probe.RunDirectory;
        if (runDirectory.Length == 0)
            throw new InvalidDataException($"{Lane} Cortex did not expose its run directory");
        string checkpointPath = Path.Combine(runDirectory, Checkpoint.FileName);
        string checkpointDigest = File.Exists(checkpointPath) ? ComputeFileDigest(checkpointPath) : "";
        _lastCheckpointPath = Path.GetFullPath(checkpointPath);
        _lastCheckpointDigest = checkpointDigest;
        long checkpointFinishedAt = Stopwatch.GetTimestamp();

        RecursionLaneMetrics finalMetrics = probe.FinalMetrics
            ?? throw new InvalidDataException($"{Lane} Cortex ended without a final metric receipt");
        List<RecursionMarathonWindow> segmentWindows = CopyWindows(firstWindow);
        long readoutFinishedAt = Stopwatch.GetTimestamp();

        RecursionSegmentStops stop = ResolveStop(exitCode, probe);
        _ownedCheckpoint = runDirectory;
        _ownedRuntime = cortex;
        long finishedAt = Stopwatch.GetTimestamp();
        RecursionWallReport wall = CreateWallReport(
            totalStartedAt,
            driveStartedAt,
            driveFinishedAt,
            checkpointFinishedAt,
            readoutFinishedAt,
            finishedAt);

        return new RecursionLaneSegmentResult
        {
            Lane = Lane,
            SegmentIndex = request.SegmentIndex,
            CompletedUnits = finalMetrics.CompletedUnits,
            Stop = stop,
            Checkpoint = runDirectory,
            CheckpointDigest = checkpointDigest,
            TapePrefixDigest = probe.FinalTapeDigest,
            JournalPrefixDigest = probe.FinalJournalDigest,
            ResumedCheckpointDigest = request.IsResume ? ResolveCheckpointDigest(request.ResumeCheckpoint) : "",
            RestoredTapePrefixDigest = probe.RestoredTapeDigest,
            RestoredJournalPrefixDigest = probe.RestoredJournalDigest,
            Wall = wall,
            Windows = segmentWindows,
        };
    }

    public Task<RecursionTerminationReceipt> TerminateAsync(
        RecursionLaneSegmentResult segment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_ownedRuntime is null || !string.Equals(_ownedCheckpoint, segment.Checkpoint, StringComparison.Ordinal))
            throw new InvalidOperationException($"{Lane} does not own segment {segment.SegmentIndex}");

        string digest = segment.CheckpointDigest;
        // The adapter owns no child process: forced-down means the complete Cortex object graph is discarded here.
        // The next segment can recover only through the checkpoint and persisted tape/journal artifacts.
        _ownedRuntime = null;
        _ownedCheckpoint = "";
        return Task.FromResult(new RecursionTerminationReceipt
        {
            Lane = Lane,
            SegmentIndex = segment.SegmentIndex,
            ProcessWasForcedDown = true,
            CheckpointDigestBeforeKill = digest,
        });
    }

    internal long ReadCompletedUnits(Cortex cortex) => ReadMetrics(cortex).CompletedUnits;

    internal void Observe(Cortex cortex, RecursionLaneSegmentRequest request, long elapsedTimeTicks)
    {
        RecursionLaneMetrics metrics = ReadMetrics(cortex);
        if (request.Stage == RecursionMarathonStages.Calibration)
            ObserveCalibration(metrics, request, elapsedTimeTicks);
        else if (request.Stage is RecursionMarathonStages.Baseline or RecursionMarathonStages.Graduated)
            ObserveClassification(metrics);
    }

    private void ObserveCalibration(
        RecursionLaneMetrics metrics,
        RecursionLaneSegmentRequest request,
        long elapsedTimeTicks)
    {
        long tailStart = request.WallLimitTicks - RecursionMarathonDefaults.CalibrationTailTicks;
        long binTicks = RecursionMarathonDefaults.CalibrationTailTicks / RecursionMarathonDefaults.CalibrationBins;
        if (!_calibrationTailPrimed && elapsedTimeTicks >= tailStart)
        {
            _previousUnits = metrics.CompletedUnits;
            _previousMetrics = metrics;
            _calibrationTailPrimed = true;
        }
        while (_nextWindow < RecursionMarathonDefaults.CalibrationBins
            && elapsedTimeTicks >= tailStart + binTicks * (_nextWindow + 1L))
        {
            AddWindow(metrics, binTicks);
        }
    }

    private void ObserveClassification(RecursionLaneMetrics metrics)
    {
        if (_classificationUnits <= 0) return;
        while (_nextWindow < RecursionMarathonDefaults.ClassificationWindows
            && metrics.CompletedUnits >= ResolveWindowTarget(_nextWindow + 1))
        {
            AddWindow(metrics, 1);
        }
    }

    private void AddWindow(RecursionLaneMetrics metrics, long wallTicks)
    {
        long completed = metrics.CompletedUnits - _previousUnits;
        if (completed < 0) throw new InvalidDataException($"{Lane} conserved units moved backwards");
        List<RecursionMetricObservation> stability = CreateStability(metrics.Stability);
        _windows.Add(new RecursionMarathonWindow
        {
            Index = _nextWindow,
            CompletedUnits = completed,
            WallTicks = wallTicks,
            CanonicalDeltas = ReadNonnegativeDelta(metrics.CanonicalDeltas, _previousMetrics.CanonicalDeltas, "canonical deltas"),
            LawClasses = ReadNonnegativeDelta(metrics.LawClasses, _previousMetrics.LawClasses, "law classes"),
            ProofAttachments = ReadNonnegativeDelta(metrics.ProofAttachments, _previousMetrics.ProofAttachments, "proof attachments"),
            FrontierHighWater = metrics.FrontierHighWater,
            ProcedureReuses = ReadNonnegativeDelta(metrics.ProcedureReuses, _previousMetrics.ProcedureReuses, "procedure reuses"),
            Stability = stability,
        });
        _previousUnits = metrics.CompletedUnits;
        _previousMetrics = metrics;
        _nextWindow++;
    }

    private List<RecursionMetricObservation> CreateStability(List<RecursionLaneMetricValue> values)
    {
        List<RecursionMetricObservation> observations = new(values.Count);
        foreach (RecursionLaneMetricValue value in values)
        {
            RecursionCampfireStabilityBand? found = null;
            foreach (RecursionCampfireStabilityBand band in _stabilityBands)
            {
                if (!string.Equals(band.Name, value.Name, StringComparison.Ordinal)) continue;
                found = band;
                break;
            }
            observations.Add(new RecursionMetricObservation(
                value.Name,
                value.Value,
                found?.Low ?? double.NaN,
                found?.High ?? double.NaN));
        }
        return observations;
    }

    private long ResolveWindowTarget(int oneBasedWindow)
    {
        long quotient = _classificationUnits / RecursionMarathonDefaults.ClassificationWindows;
        long remainder = _classificationUnits % RecursionMarathonDefaults.ClassificationWindows;
        return checked(quotient * oneBasedWindow
            + remainder * oneBasedWindow / RecursionMarathonDefaults.ClassificationWindows);
    }

    private void ResetWindowJournal()
    {
        _windows.Clear();
        _previousMetrics = EmptyMetrics();
        _previousUnits = 0;
        _nextWindow = 0;
        _calibrationTailPrimed = false;
        ResetPrefixDigests();
        _lastCheckpointPath = "";
        _lastCheckpointDigest = "";
    }

    private List<RecursionMarathonWindow> CopyWindows(int first)
    {
        List<RecursionMarathonWindow> result = new(_windows.Count - first);
        for (int i = first; i < _windows.Count; i++) result.Add(_windows[i]);
        return result;
    }

    private void ValidateRequest(RecursionLaneSegmentRequest request)
    {
        if (request.TargetUnits <= 0) throw new ArgumentOutOfRangeException(nameof(request.TargetUnits));
        if (request.WallLimitTicks <= 0) throw new ArgumentOutOfRangeException(nameof(request.WallLimitTicks));
        if (request.IsResume != (request.ResumeCheckpoint.Length > 0))
            throw new InvalidDataException("resume flag and checkpoint reference disagree");
        if (request.StopCondition is CortexStopCondition stop
            && (!string.Equals(stop.Selector, ProgressSelector, StringComparison.Ordinal)
                || stop.AtLeast != request.TargetUnits))
            throw new InvalidDataException($"{Lane} received a stop condition for another conserved unit");
    }

    private static RecursionSegmentStops ResolveStop(int exitCode, RecursionLaneProbe probe)
    {
        if (exitCode != 0 || !probe.RequestedStop) return RecursionSegmentStops.Failed;
        return probe.StoppedByWall ? RecursionSegmentStops.WallCap : RecursionSegmentStops.Budget;
    }

    private static long ReadNonnegativeDelta(long value, long previous, string name)
    {
        long delta = value - previous;
        if (delta < 0) throw new InvalidDataException($"{name} moved backwards");
        return delta;
    }

    private static RecursionLaneMetrics EmptyMetrics()
        => new()
        {
            CompletedUnits = 0,
            CanonicalDeltas = 0,
            LawClasses = 0,
            ProofAttachments = 0,
            FrontierHighWater = 0,
            ProcedureReuses = 0,
            Stability = [],
        };

    private static RecursionWallReport CreateWallReport(
        long startedAt,
        long driveStartedAt,
        long driveFinishedAt,
        long checkpointFinishedAt,
        long readoutFinishedAt,
        long finishedAt)
    {
        long[] phaseTicks = new long[Enum.GetValues<RecursionWallPhases>().Length];
        phaseTicks[(int)RecursionWallPhases.StartupLoad] = driveStartedAt - startedAt;
        phaseTicks[(int)RecursionWallPhases.ToolExecution] = driveFinishedAt - driveStartedAt;
        phaseTicks[(int)RecursionWallPhases.Checkpoint] = checkpointFinishedAt - driveFinishedAt;
        phaseTicks[(int)RecursionWallPhases.WorkspaceReadout] = readoutFinishedAt - checkpointFinishedAt;
        phaseTicks[(int)RecursionWallPhases.Shutdown] = finishedAt - readoutFinishedAt;
        List<RecursionWallPhaseTime> phases = new(phaseTicks.Length);
        for (int i = 0; i < phaseTicks.Length; i++)
            phases.Add(new RecursionWallPhaseTime((RecursionWallPhases)i, phaseTicks[i]));
        return RecursionWallReport.Create(finishedAt - startedAt, phases);
    }

    private string ResolveCheckpointDigest(string runDirectory)
    {
        string checkpointPath = Path.GetFullPath(Path.Combine(runDirectory, Checkpoint.FileName));
        return string.Equals(checkpointPath, _lastCheckpointPath, StringComparison.Ordinal)
            ? _lastCheckpointDigest
            : ComputeCheckpointDigest(runDirectory);
    }

    private static string ComputeCheckpointDigest(string runDirectory)
    {
        string path = Path.Combine(runDirectory, Checkpoint.FileName);
        return File.Exists(path) ? ComputeFileDigest(path) : "";
    }

    private static string ComputeFileDigest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] digest = SHA256.HashData(stream);
        return Convert.ToHexString(digest);
    }

    private void ResetPrefixDigests()
    {
        _tapePrefixHash?.Dispose();
        _journalPrefixHash?.Dispose();
        _tapePrefixHash = null;
        _journalPrefixHash = null;
        _tapeAppendMark = 0;
        _tapeRevision = 0;
        _tapeOrderRevision = 0;
        _journalLineMark = 0;
    }

    private void UpdatePrefixDigests(Tape tape, Journal journal, string? journalLogPath)
    {
        if (_tapePrefixHash is null)
        {
            _tapePrefixHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (TapeEventView view in tape.GetEventViews()) AppendTapeView(_tapePrefixHash, tape, in view);
            _tapeAppendMark = tape.NextId;
        }
        else
        {
            long appended = tape.NextId - _tapeAppendMark;
            bool loaded = tape.Revision.Value == 0 && tape.OrderRevision.Value == 0;
            bool appendOnly = tape.NextId >= _tapeAppendMark
                && (loaded || (tape.OrderRevision.Value == _tapeOrderRevision
                    && tape.Revision.Value - _tapeRevision == appended));
            if (!appendOnly)
            {
                _tapePrefixHash.Dispose();
                _tapePrefixHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (TapeEventView view in tape.GetEventViews()) AppendTapeView(_tapePrefixHash, tape, in view);
            }
            else
            {
                foreach (TapeEventView view in tape.EnumerateAppendedSince(_tapeAppendMark))
                    AppendTapeView(_tapePrefixHash, tape, in view);
            }
            _tapeAppendMark = tape.NextId;
        }
        _tapeRevision = tape.Revision.Value;
        _tapeOrderRevision = tape.OrderRevision.Value;

        if (_journalPrefixHash is not null && journal.LineCount < _journalLineMark)
        {
            _journalPrefixHash.Dispose();
            _journalPrefixHash = null;
        }
        if (_journalPrefixHash is null)
        {
            _journalPrefixHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _journalLineMark = 0;
        }
        // The journal sheds committed lines to journal.log; hash any not-yet-marked prefix from the file,
        // then the resident tail from RAM. Marks are ABSOLUTE line indices, so the digest stays the digest
        // of the whole record regardless of where the shed horizon sits.
        if (_journalLineMark < journal.ShedLineCount)
        {
            int skipped = 0;
            int hashed = _journalLineMark;
            foreach (string line in journal.EnumerateAllLines(journalLogPath))
            {
                if (skipped < _journalLineMark) { skipped++; continue; }
                if (hashed >= journal.ShedLineCount) break;
                AppendJournalLine(_journalPrefixHash, line);
                hashed++;
            }
            if (hashed != journal.ShedLineCount)
                throw new InvalidDataException($"journal.log covers {hashed} of {journal.ShedLineCount} shed lines for the prefix digest");
            _journalLineMark = journal.ShedLineCount;
        }
        for (int i = _journalLineMark; i < journal.LineCount; i++)
            AppendJournalLine(_journalPrefixHash, journal.ResidentLines[i - journal.ShedLineCount]);
        _journalLineMark = journal.LineCount;
    }

    private string ReadTapePrefixDigest()
        => Convert.ToHexString(_tapePrefixHash?.GetCurrentHash() ?? throw new InvalidOperationException("tape prefix digest is not initialized"));

    private string ReadJournalPrefixDigest()
        => Convert.ToHexString(_journalPrefixHash?.GetCurrentHash() ?? throw new InvalidOperationException("journal prefix digest is not initialized"));

    private static void AppendTapeView(IncrementalHash hash, Tape tape, in TapeEventView view)
    {
        Span<byte> number = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(number, view.Id.Value);
        hash.AppendData(number);
        BinaryPrimitives.WriteInt32LittleEndian(number, view.Len);
        hash.AppendData(number[..4]);
        number[0] = (byte)view.Provenance;
        number[1] = view.Evidence ? (byte)1 : (byte)0;
        hash.AppendData(number[..2]);
        byte[] source = Encoding.UTF8.GetBytes(view.Source);
        BinaryPrimitives.WriteInt32LittleEndian(number, source.Length);
        hash.AppendData(number[..4]);
        hash.AppendData(source);
        if (!tape.Resolve(view.Id, out byte[] bytes))
            throw new InvalidDataException($"tape view contains unresolved event {view.Id}");
        hash.AppendData(bytes);
    }

    private static void AppendJournalLine(IncrementalHash hash, string line)
    {
        Span<byte> number = stackalloc byte[4];
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        BinaryPrimitives.WriteInt32LittleEndian(number, bytes.Length);
        hash.AppendData(number);
        hash.AppendData(bytes);
    }

    internal static string ComputeTapeDigest(Tape tape)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (TapeEventView view in tape.GetEventViews())
            AppendTapeView(hash, tape, in view);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal sealed class RecursionLaneProbe : CortexReward
    {
        private readonly CortexRecursionMarathonLane _lane;
        private readonly RecursionLaneSegmentRequest _request;
        private readonly CancellationToken _cancellationToken;
        private long _startedAt;

        public RecursionLaneProbe(
            CortexRecursionMarathonLane lane,
            RecursionLaneSegmentRequest request,
            CancellationToken cancellationToken,
            long startedAt)
        {
            _lane = lane;
            _request = request;
            _cancellationToken = cancellationToken;
            _startedAt = startedAt;
        }

        public string RunDirectory { get; private set; } = "";
        public string RestoredTapeDigest { get; private set; } = "";
        public string RestoredJournalDigest { get; private set; } = "";
        public string FinalTapeDigest { get; private set; } = "";
        public string FinalJournalDigest { get; private set; } = "";
        public bool RequestedStop { get; private set; }
        public bool StoppedByWall { get; private set; }
        public RecursionLaneMetrics? FinalMetrics { get; private set; }
        private bool _finalCaptured;

        public override void OnRunStart(Cortex cortex)
        {
            RunDirectory = cortex.CurrentRun.Dir;
            _lane.UpdatePrefixDigests(cortex.Tape, cortex.Journal, Path.Combine(RunDirectory, "journal.log"));
            if (_request.IsResume)
            {
                RestoredTapeDigest = _lane.ReadTapePrefixDigest();
                RestoredJournalDigest = _lane.ReadJournalPrefixDigest();
            }
        }

        public override void OnActionBatchEnd(Cortex cortex)
        {
            long elapsed = ConvertStopwatchTicks(Stopwatch.GetTimestamp() - _startedAt);
            _lane.Observe(cortex, _request, elapsed);
            long units = _lane.ReadCompletedUnits(cortex);
            bool wall = elapsed >= _request.WallLimitTicks;
            bool budget = _request.TargetUnits != long.MaxValue && units >= _request.TargetUnits;
            bool cancelled = _cancellationToken.IsCancellationRequested;
            if (!wall && !budget && !cancelled) return;
            StoppedByWall = wall;
            RequestedStop = true;
            CaptureFinal(cortex);
            cortex.RequestStop();
        }

        public override void OnRunEnd(Cortex cortex)
        {
            long elapsed = ConvertStopwatchTicks(Stopwatch.GetTimestamp() - _startedAt);
            _lane.Observe(cortex, _request, elapsed);
            FinalMetrics = _lane.ReadMetrics(cortex);
            CaptureFinal(cortex);
        }

        private void CaptureFinal(Cortex cortex)
        {
            if (_finalCaptured) return;
            _lane.UpdatePrefixDigests(cortex.Tape, cortex.Journal, Path.Combine(cortex.CurrentRun.Dir, "journal.log"));
            FinalTapeDigest = _lane.ReadTapePrefixDigest();
            FinalJournalDigest = _lane.ReadJournalPrefixDigest();
            _finalCaptured = true;
        }

        private static long ConvertStopwatchTicks(long ticks)
            => checked((long)Math.Floor(ticks * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
    }
}
