namespace Cogito;

internal sealed class EMLRecursionMarathonLane : CortexRecursionMarathonLane
{
    private readonly ulong _seed;
    private readonly CortexEmlCurriculum _curriculum;
    private readonly CortexGenerationConfig _generation;
    private readonly CortexStrideConfig _stride;
    private readonly CortexLearningConfig _learning;
    private readonly CortexDurabilityConfig _durability;

    public EMLRecursionMarathonLane(
        ulong seed,
        long classificationUnits = 0,
        CortexEmlCurriculum? curriculum = null,
        CortexGenerationConfig? generation = null,
        CortexStrideConfig? stride = null,
        CortexLearningConfig? learning = null,
        CortexDurabilityConfig? durability = null)
        : base(classificationUnits)
    {
        _seed = seed;
        _curriculum = curriculum ?? new CortexEmlCurriculum
        {
            Actions = EmlActionSelections.ProcedureGuarded,
        };
        if (_curriculum.Actions is not (EmlActionSelections.Procedure or EmlActionSelections.ProcedureGuarded))
            throw new ArgumentException("the EML marathon requires a non-shuffled provenance-bound procedure", nameof(curriculum));
        _generation = generation ?? new CortexGenerationConfig();
        _stride = stride ?? new CortexStrideConfig();
        _learning = learning ?? new CortexLearningConfig();
        _durability = durability ?? new CortexDurabilityConfig();
    }

    public override RecursionMarathonLanes Lane => RecursionMarathonLanes.EMLProcedure;
    public override string ProgressSelector => RecursionMarathonDefaults.EMLSelector;

    protected override Cortex CreateCortex(
        RecursionLaneSegmentRequest request,
        RecursionLaneProbe probe)
    {
        ReplayCalc dream = ReplayCalc.Mount(_seed, _curriculum);
        List<CortexReward> rewards = ReplayCalc.CreateRewards();
        rewards.Add(probe);
        CortexConfig config = new()
        {
            RunName = $"{request.RunID}-eml-{request.Stage.ToString().ToLowerInvariant()}",
            Steps = int.MaxValue,
            Seed = _seed,
            Generation = _generation,
            Stride = _stride,
            Curriculum = _curriculum,
            RuntimeCurriculum = dream,
            ActionsPerStep = Math.Max(1, _curriculum.IntakeBatch),
            Tools = ReplayCalc.CreateActionTools(),
            ActionPolicies = ReplayCalc.CreateActionPolicies(),
            Rewards = rewards,
            Learning = _learning,
            Durability = _durability,
            Readout = new CortexReadoutConfig
            {
                Curve =
                [
                    "eml.evaluator.calls",
                    "eml.procedure.canonical_deltas",
                    "eml.laws.classes",
                    "eml.frontier.k",
                    "eml.procedure.completed",
                ],
            },
            StopConditions = [],
        };
        return new Cortex(config);
    }

    protected override RecursionLaneMetrics ReadMetrics(Cortex cortex)
    {
        ReplayCalc dream = cortex.ActiveCurriculum as ReplayCalc
            ?? throw new InvalidOperationException("EML marathon Cortex is not running ReplayCalc");
        EmlBranchingReceipt receipt = dream.ReadBranchingReceipt();
        return new RecursionLaneMetrics
        {
            CompletedUnits = receipt.EvaluatorCalls,
            CanonicalDeltas = receipt.CanonicalDeltas,
            LawClasses = dream.LawCount,
            ProofAttachments = 0,
            FrontierHighWater = dream.Sieve.KFrontier,
            ProcedureReuses = receipt.ProceduresCompleted,
            Stability = [],
        };
    }
}
