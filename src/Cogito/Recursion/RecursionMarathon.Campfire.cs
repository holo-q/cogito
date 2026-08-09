namespace Cogito;

using Cogito.Induct;

internal sealed class CampfireRecursionMarathonLane : CortexRecursionMarathonLane
{
    public const string StepSelector = "cortex.steps";

    private readonly ulong _seed;
    private readonly CortexCampfireCurriculum _curriculum;
    private readonly CortexGenerationConfig _generation;
    private readonly CortexStrideConfig _stride;
    private readonly CortexLearningConfig _learning;
    private readonly CortexDurabilityConfig _durability;

    public CampfireRecursionMarathonLane(
        CogitoCorpus corpus,
        ulong seed,
        long classificationUnits = 0,
        List<RecursionCampfireStabilityBand>? stabilityBands = null,
        CortexGenerationConfig? generation = null,
        CortexStrideConfig? stride = null,
        CortexLearningConfig? learning = null,
        CortexDurabilityConfig? durability = null)
        : base(classificationUnits, stabilityBands)
    {
        if (corpus is null) throw new ArgumentNullException(nameof(corpus));
        _seed = seed;
        _curriculum = new CortexCampfireCurriculum { Corpus = corpus };
        _generation = generation ?? new CortexGenerationConfig();
        _stride = stride ?? new CortexStrideConfig();
        _learning = learning ?? new CortexLearningConfig();
        _durability = durability ?? new CortexDurabilityConfig();
    }

    public override RecursionMarathonLanes Lane => RecursionMarathonLanes.Campfire;
    public override string ProgressSelector => StepSelector;

    protected override Cortex CreateCortex(
        RecursionLaneSegmentRequest request,
        RecursionLaneProbe probe)
    {
        CortexConfig config = new()
        {
            RunName = $"{request.RunID}-campfire-{request.Stage.ToString().ToLowerInvariant()}",
            Steps = int.MaxValue,
            Seed = _seed,
            Generation = _generation,
            Stride = _stride,
            Curriculum = _curriculum,
            ActionsPerStep = 1,
            Tools = [],
            ActionPolicies = [],
            Rewards = [probe],
            Learning = _learning,
            Durability = _durability,
            Readout = new CortexReadoutConfig(),
            StopConditions = [],
        };
        return new Cortex(config);
    }

    protected override RecursionLaneMetrics ReadMetrics(Cortex cortex)
    {
        Campfire campfire = cortex.ActiveCurriculum as Campfire
            ?? throw new InvalidOperationException("Campfire marathon Cortex is not running Campfire");
        Engine.RenormStat criticality = Engine.RenormStats(cortex.Grammar);
        double vestRate = cortex.Tape.ReplayCount > 0
            ? (double)cortex.Tape.ReflectedReplayCount / cortex.Tape.ReplayCount
            : 0;
        double residentFraction = cortex.Tape.ByteLength > 0
            ? (double)cortex.Tape.ResidentBytes / cortex.Tape.ByteLength
            : 1;
        List<RecursionLaneMetricValue> stability =
        [
            new RecursionLaneMetricValue("meanz", criticality.MeanZ),
            new RecursionLaneMetricValue("cvz", criticality.CvZ),
            new RecursionLaneMetricValue("kz", criticality.KZ),
            new RecursionLaneMetricValue("maxspan", criticality.MaxSpan),
            new RecursionLaneMetricValue("vest_rate", vestRate),
            new RecursionLaneMetricValue("resident_fraction", residentFraction),
        ];
        return new RecursionLaneMetrics
        {
            CompletedUnits = cortex.Step + 1L,
            CanonicalDeltas = 0,
            LawClasses = campfire.SieveOrgan.DistinctCerts,
            ProofAttachments = 0,
            FrontierHighWater = campfire.SieveOrgan.KFrontier,
            ProcedureReuses = 0,
            Stability = stability,
        };
    }
}
