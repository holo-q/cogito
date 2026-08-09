namespace Cogito;

public readonly record struct EmlEvaluatorInterval(long Start, long End)
{
    public long Calls => End - Start;

    public static EmlEvaluatorInterval EmptyAt(long position) => new(position, position);
}

public readonly record struct EmlEvaluatorClockSnapshot(
    long OfferRequests,
    long OfferProgramPointEvaluations,
    long LadderRequests,
    long LadderCacheHits,
    long LadderCacheMisses,
    long LadderProgramPointEvaluations,
    long ExecutedLadderProgramPointEvaluations,
    long OutOfDistributionProbeCalls,
    long InverseTransforms,
    long HashProbes,
    long OfferedJoinHits,
    bool WritesCheckpoint,
    int LoadedCheckpointVersion,
    bool HistoryComplete)
{
    public long ProgramPointEvaluations
        => OfferProgramPointEvaluations + LadderProgramPointEvaluations + OutOfDistributionProbeCalls;
}

/// Conserved EML work clock. A tick is one RPN program evaluated at one complex point, independent of caches.
/// The request/cache/join counters explain why those ticks occurred without participating in the conserved total.
public sealed class EmlEvaluatorClock
{
    private const uint CheckpointTag = 0x45434C4B; // ECLK
    private const int CheckpointVersion = 2;
    private bool _writesCheckpoint = true;
    private int _loadedCheckpointVersion = CheckpointVersion;

    public bool HistoryComplete { get; private set; } = true;

    public long OfferRequests { get; private set; }
    public long OfferProgramPointEvaluations { get; private set; }
    public long LadderRequests { get; private set; }
    public long LadderCacheHits { get; private set; }
    public long LadderCacheMisses { get; private set; }
    public long LadderProgramPointEvaluations { get; private set; }
    public long ExecutedLadderProgramPointEvaluations { get; private set; }
    public long OutOfDistributionProbeCalls { get; private set; }
    public long InverseTransforms { get; private set; }
    public long HashProbes { get; private set; }
    public long OfferedJoinHits { get; private set; }

    public long ProgramPointEvaluations
        => OfferProgramPointEvaluations + LadderProgramPointEvaluations + OutOfDistributionProbeCalls;

    public void RecordOfferRequest() { ArmCheckpoint(); OfferRequests++; }
    public void RecordOfferProgramPointEvaluation() { ArmCheckpoint(); OfferProgramPointEvaluations++; }

    public void RecordLadderRequest(bool cacheHit)
    {
        ArmCheckpoint();
        LadderRequests++;
        LadderProgramPointEvaluations += 3;
        if (cacheHit) LadderCacheHits++;
        else LadderCacheMisses++;
    }

    public void RecordExecutedLadderProgramPointEvaluation() { ArmCheckpoint(); ExecutedLadderProgramPointEvaluations++; }
    public void RecordOutOfDistributionProbeCall() { ArmCheckpoint(); OutOfDistributionProbeCalls++; }
    public void RecordInverseTransform() { ArmCheckpoint(); InverseTransforms++; }
    public void RecordHashProbe() { ArmCheckpoint(); HashProbes++; }
    public void RecordOfferedJoinHit() { ArmCheckpoint(); OfferedJoinHits++; }

    public EmlEvaluatorInterval MeasureFrom(long start)
        => new(start, ProgramPointEvaluations);

    public EmlEvaluatorClockSnapshot Capture()
        => new(OfferRequests, OfferProgramPointEvaluations,
            LadderRequests, LadderCacheHits, LadderCacheMisses, LadderProgramPointEvaluations,
            ExecutedLadderProgramPointEvaluations,
            OutOfDistributionProbeCalls, InverseTransforms, HashProbes, OfferedJoinHits,
            _writesCheckpoint, _loadedCheckpointVersion, HistoryComplete);

    public void Restore(in EmlEvaluatorClockSnapshot snapshot, bool writesCheckpoint)
    {
        OfferRequests = snapshot.OfferRequests;
        OfferProgramPointEvaluations = snapshot.OfferProgramPointEvaluations;
        LadderRequests = snapshot.LadderRequests;
        LadderCacheHits = snapshot.LadderCacheHits;
        LadderCacheMisses = snapshot.LadderCacheMisses;
        LadderProgramPointEvaluations = snapshot.LadderProgramPointEvaluations;
        ExecutedLadderProgramPointEvaluations = snapshot.ExecutedLadderProgramPointEvaluations;
        OutOfDistributionProbeCalls = snapshot.OutOfDistributionProbeCalls;
        InverseTransforms = snapshot.InverseTransforms;
        HashProbes = snapshot.HashProbes;
        OfferedJoinHits = snapshot.OfferedJoinHits;
        _writesCheckpoint = writesCheckpoint;
        Validate();
    }

    internal void Restore(in EmlEvaluatorClockSnapshot snapshot)
    {
        Restore(in snapshot, snapshot.WritesCheckpoint);
        _loadedCheckpointVersion = snapshot.LoadedCheckpointVersion;
        HistoryComplete = snapshot.HistoryComplete;
    }

    internal void MarkLegacyCheckpoint()
    {
        _writesCheckpoint = false;
        _loadedCheckpointVersion = 0;
        HistoryComplete = false;
    }

    public void Save(CkptWriter writer)
    {
        if (!_writesCheckpoint) return;
        Validate();
        writer.Section(CheckpointTag);
        writer.I32(_loadedCheckpointVersion);
        if (_loadedCheckpointVersion >= 2) writer.Bool(HistoryComplete);
        writer.I64(OfferRequests);
        writer.I64(OfferProgramPointEvaluations);
        writer.I64(LadderRequests);
        writer.I64(LadderCacheHits);
        writer.I64(LadderCacheMisses);
        writer.I64(LadderProgramPointEvaluations);
        writer.I64(ExecutedLadderProgramPointEvaluations);
        writer.I64(OutOfDistributionProbeCalls);
        writer.I64(InverseTransforms);
        writer.I64(HashProbes);
        writer.I64(OfferedJoinHits);
    }

    public bool TryLoad(CkptReader reader)
    {
        if (!reader.TryExpect(CheckpointTag))
        {
            Restore(default, writesCheckpoint: false);
            HistoryComplete = false;
            _loadedCheckpointVersion = 0;
            return false;
        }
        int version = reader.I32();
        if (version is < 1 or > CheckpointVersion)
            throw new InvalidDataException($"unsupported EML evaluator clock checkpoint v{version}");
        HistoryComplete = version < 2 || reader.Bool();
        _loadedCheckpointVersion = version;
        EmlEvaluatorClockSnapshot snapshot = new(
            reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(),
            reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(),
            true, version, HistoryComplete);
        Restore(in snapshot, writesCheckpoint: true);
        return true;
    }

    private void Validate()
    {
        if (OfferRequests < 0 || OfferProgramPointEvaluations < 0 ||
            LadderRequests < 0 || LadderCacheHits < 0 || LadderCacheMisses < 0 ||
            LadderProgramPointEvaluations < 0 || ExecutedLadderProgramPointEvaluations < 0 ||
            OutOfDistributionProbeCalls < 0 ||
            InverseTransforms < 0 || HashProbes < 0 || OfferedJoinHits < 0)
            throw new InvalidDataException("EML evaluator clock contains a negative counter");
        if (LadderRequests != LadderCacheHits + LadderCacheMisses)
            throw new InvalidDataException("EML evaluator clock ladder requests do not equal cache hits plus misses");
        if (OfferProgramPointEvaluations != OfferRequests * 2)
            throw new InvalidDataException("EML evaluator clock offers did not consume exactly two program-point evaluations each");
        if (LadderProgramPointEvaluations != LadderRequests * 3)
            throw new InvalidDataException("EML evaluator clock ladder requests did not reserve exactly three logical program-point evaluations each");
        if (ExecutedLadderProgramPointEvaluations != LadderCacheMisses * 3)
            throw new InvalidDataException("EML evaluator clock ladder misses did not execute exactly three program-point evaluations each");
    }

    private void ArmCheckpoint()
    {
        _writesCheckpoint = true;
        if (_loadedCheckpointVersion == 0) _loadedCheckpointVersion = CheckpointVersion;
    }
}
