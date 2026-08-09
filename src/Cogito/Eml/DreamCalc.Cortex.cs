namespace Cogito;

using System.Text;
using System.Numerics;
using Cogito.Grammar;
using Cogito.Induct;

public enum EmlActionSelections
{
    Off,
    RoundRobin,
    ShuffledFixed,
    Procedure,
    ProcedureShuffled,
    ProcedureGuarded,
    ProcedureGuardShuffled,
}

internal enum EmlActionSelectionCauses
{
    FixedSchedule,
    Grammar,
    Abstention,
}

public enum EmlActionArms
{
    FreshBias,
    FreshEnum,
    SolveHole,
    Counterexample,
    Compare,
}

internal enum EmlFertilityInterventions
{
    None,
    Hold,
    Actual,
    Shadow,
}

public readonly record struct EmlAccretion(
    int AppendedWeight,
    int CanonicalDeltas,
    int FirstCaptures,
    int RepresentativeImprovements,
    int TargetCaptures,
    int Counterexamples);

internal readonly record struct EmlPolicyOutcomeSnapshot(
    long EvaluatorCalls,
    long CanonicalDeltas,
    long FirstCaptures,
    bool HistoryComplete);

internal readonly record struct EmlActionSchedule(ulong Rng, int Decisions, int Cursor);

internal readonly record struct EmlBranchingReceipt(
    EmlActionSelections Variant,
    long EvaluatorCalls,
    int DistinctCertificates,
    int ExactClasses,
    int TargetsHit,
    int ProceduresStarted,
    int ProceduresCompleted,
    int GuardsPassed,
    int GuardsSkipped,
    int GuardsAbstained,
    int CanonicalDeltas);

internal static class EmlActionSelectionTokens
{
    public static string CurriculumToken(EmlActionSelections selection) => selection switch
    {
        EmlActionSelections.Off => "eml",
        EmlActionSelections.RoundRobin => "eml:round-robin",
        EmlActionSelections.ShuffledFixed => "eml:shuffled-fixed",
        EmlActionSelections.Procedure => "eml:procedure",
        EmlActionSelections.ProcedureShuffled => "eml:procedure-shuffled",
        EmlActionSelections.ProcedureGuarded => "eml:procedure-guarded",
        EmlActionSelections.ProcedureGuardShuffled => "eml:procedure-guard-shuffled",
        _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "unknown EML action selection"),
    };

    public static bool IsEmlCurriculum(string token)
        => token == "eml" || token.StartsWith("eml:", StringComparison.Ordinal);

    public static EmlActionSelections ParseCurriculumToken(string token) => token switch
    {
        "eml" => EmlActionSelections.Off,
        "eml:round-robin" => EmlActionSelections.RoundRobin,
        "eml:shuffled-fixed" => EmlActionSelections.ShuffledFixed,
        "eml:procedure" => EmlActionSelections.Procedure,
        "eml:procedure-shuffled" => EmlActionSelections.ProcedureShuffled,
        "eml:procedure-guarded" => EmlActionSelections.ProcedureGuarded,
        "eml:procedure-guard-shuffled" => EmlActionSelections.ProcedureGuardShuffled,
        _ => throw new ArgumentException($"unknown EML curriculum token '{token}'"),
    };

    public static EmlActionSelections Parse(string? token) => (token ?? "off").ToLowerInvariant() switch
    {
        "off" => EmlActionSelections.Off,
        "round-robin" or "roundrobin" => EmlActionSelections.RoundRobin,
        "shuffled-fixed" or "shuffled" => EmlActionSelections.ShuffledFixed,
        "procedure" or "routed-procedure" => EmlActionSelections.Procedure,
        "procedure-shuffled" or "shuffled-procedure" => EmlActionSelections.ProcedureShuffled,
        "procedure-guarded" or "guarded-procedure" => EmlActionSelections.ProcedureGuarded,
        "procedure-guard-shuffled" or "shuffled-guard-procedure" => EmlActionSelections.ProcedureGuardShuffled,
        string bad => throw new ArgumentException($"unknown EML action mode '{bad}' (off|round-robin|shuffled-fixed|procedure|procedure-shuffled|procedure-guarded|procedure-guard-shuffled)"),
    };

    public static string ActionToken(EmlActionArms arm) => arm switch
    {
        EmlActionArms.FreshBias => "fresh-bias",
        EmlActionArms.FreshEnum => "fresh-enum",
        EmlActionArms.SolveHole => "solve-hole",
        EmlActionArms.Counterexample => "counterexample",
        EmlActionArms.Compare => "compare",
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "unknown EML action arm"),
    };

    public static string GetSelectionCauseToken(EmlActionSelectionCauses cause) => cause switch
    {
        EmlActionSelectionCauses.FixedSchedule => "fixed-schedule",
        EmlActionSelectionCauses.Grammar => "grammar",
        EmlActionSelectionCauses.Abstention => "abstention",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "unknown EML selection cause"),
    };

    public static string GetCertificateChangeToken(EmlCertificateChanges change) => change switch
    {
        EmlCertificateChanges.ClassOpened => "class-opened",
        EmlCertificateChanges.RepresentativeImproved => "representative-improved",
        EmlCertificateChanges.TargetCaptured => "target-captured",
        EmlCertificateChanges.PredictionChallenged => "claim-challenged",
        EmlCertificateChanges.RateClassViolated => "rate-class-violated",
        EmlCertificateChanges.PredictionRegraded => "claim-regraded",
        EmlCertificateChanges.LawAdmitted => "law-admitted",
        EmlCertificateChanges.ProofAttached => "proof-attached",
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "unknown EML certificate change"),
    };
}

public sealed partial class ReplayCalc
{
    private readonly List<EmlPredictionID> _pendingRung0Closures = new();
    internal IReadOnlyList<EmlPredictionID> PendingRung0Closures => _pendingRung0Closures;
    internal enum EmlAnytimeArmModes
    {
        Deliberation,
        ReflexFrozenFuel,
    }

    internal static readonly CortexPolicyID ActionPolicyID = new("eml.action-selection");
    internal static readonly CortexPolicySchema ActionPolicySchema = new(
        ActionPolicyID, featureCount: 11, actionCount: 5, outcomeCount: 3,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);

    private enum SharedActionMetricIDs : ushort
    {
        FrontierResidual = 600,
        GlobalYield,
        DistinctCertificates,
        ExactClasses,
        TargetsHit,
        FreshBiasYield,
        FreshEnumYield,
        SolveHoleYield,
        CounterexampleYield,
        CompareYield,
        DecisionIndex,
        CanonicalDeltas = 620,
        FirstCaptures,
        EvaluatorCalls,
    }

    // The action-state suffix carries its own domain tag because the curriculum state precedes it.
    private const uint ActionTag = 0x454D4C42; // EMLB
    private const uint OrdinaryRung0Tag = 0x52305552; // R0UR
    private bool _ordinaryRung0StateLoaded;
    private const int UnchangedObligationAttemptLimit = 1;

    private static readonly EmlActionArms[] ActionArmOrder =
    [
        EmlActionArms.FreshBias,
        EmlActionArms.FreshEnum,
        EmlActionArms.SolveHole,
        EmlActionArms.Counterexample,
    ];

    private static readonly EmlActionArms[] ReportArmOrder =
    [
        EmlActionArms.FreshBias,
        EmlActionArms.FreshEnum,
        EmlActionArms.SolveHole,
        EmlActionArms.Counterexample,
        EmlActionArms.Compare,
    ];

    // DecisionIndex is a monotonic observation carried by every raw packet. It
    // identifies when the decision was observed, not which semantic state should
    // select an action, so it must not fragment the grammar readout working set.
    private static readonly MetricID[] ActionReadoutExcludedMetricIDs =
    [
        new MetricID((ushort)SharedActionMetricIDs.DecisionIndex),
    ];

    private EmlActionSelections _actionSelection;
    private OutcomeMeter<EmlActionArms>? _actionMeter;
    private EmlActionArms[] _shuffledActionOrder = Array.Empty<EmlActionArms>();
    private ulong _actionRng;
    private int _actionUnits;
    private int _actionGain;
    private int _actionDecision;
    private int _roundRobinCursor;
    private IEnumerator<string>? _actionEnum;
    private int _actionEnumTaken;
    private int _actionEnumRuler;
    private bool _actionEnumDone;
    private readonly StringBuilder _actionSampleBuilder = new();
    private readonly List<(string Toks, int Weight, int DeltaH)> _actionSamplePool = new();
    private EmlAccretion _lastAccretion;
    private EmlActionArms _currentActionArm;
    private bool _actionInFlight;
    private bool _sharedActionOutcomePending;
    private CortexPolicyDecision _sharedActionDecision;
    private bool _sharedActionInvariantClean = true;
    private readonly int[] _actionOffers = new int[ReportArmOrder.Length];
    private readonly int[] _actionFirstCaptures = new int[ReportArmOrder.Length];
    private readonly double[] _actionDeltaOutcomes = new double[ReportArmOrder.Length];
    private readonly long[] _actionEvaluatorCalls = new long[ReportArmOrder.Length];
    private readonly int[] _actionSelectionCauses = new int[Enum.GetValues<EmlActionSelectionCauses>().Length];
    private int _actionFallbacks;
    private double _actionGlobalYield = double.NaN;
    private int _actionGlobalOutcomes;
    private long _sharedCanonicalDeltas;
    private long _sharedFirstCaptures;
    private readonly List<EmlCertificateDelta> _lastDeltas = new();
    // E/A theorem classes ordered (grade-rank, cert HashKey) — the CAS is append-only under the lift-closure
    // invariant, so new classes sorted-insert at mint time; the class value is read fresh from the CAS at use.
    private readonly List<EmlCert> _stressCandidates = new();
    private readonly HashSet<EmlCert> _stressCandidateSet = new();
    private int _candidateCacheMintCount;
    private int _stressCursor;
    private int _holeCursor;
    private readonly HashSet<string> _counterexamplesSeen = new(StringComparer.Ordinal);
    private readonly List<string> _counterexampleOrder = new();          // insertion-order shadow of the append-only set — checkpoint deltas ship only order[baseline..]
    private int _checkpointCounterexampleCount;
    private string? _pendingCounterexample;
    private int _stressExactTests;
    private int _stressExactRefuted;
    private int _stressAsymptoticTests;
    private int _stressAsymptoticRefuted;
    private int _stressControlTests;
    private int _stressControlRefuted;
    private Dictionary<string, Func<Complex, Complex, Complex>>? _referenceChart;
    private CortexProcedure? _actionProcedure;
    private readonly List<EmlCertificateDelta> _procedureSolveDeltas = new();
    private int _proceduresStarted;
    private int _proceduresCompleted;
    private int _procedureBindings;
    private int _procedureShuffledBindings;
    private int _procedureObligationMatches;
    private int _procedureNewDeltas;
    private int _procedureCanonicalDeltas;
    private int _procedureGuardsPassed;
    private int _procedureGuardsSkipped;
    private int _procedureGuardsAbstained;
    private double _intrinsicFrontierResidual = 1;
    private bool _actionBatchHadCanonicalDelta;
    private int _discoveryEpoch;
    private readonly Dictionary<EmlPredictionID, EmlObligationSearchState> _obligationSearch = new();
    private int _obligationSearchAttempts;
    private int _obligationSearchSuppressions;
    private int _obligationSearchRevivals;
    private int _obligationSuppressedCalls;
    private int _executionAdmissions;
    private int _executionAffirmSkips;
    private int _hypothesisCapSkips;
    private int _firstGenerativeDecision = -1;
    private int _firstGenerativeStep = -1;
    private long _actionEvaluatorStart;
    private long _accretionEvaluatorStart;
    private EmlFertilityInterventions _fertilityIntervention;
    private long _fertilityStopAt = long.MaxValue;
    private bool _fertilityRootHandled;
    private bool _fertilityRootInFlight;
    private byte[]? _fertilityAdmissionState;
    private readonly List<EmlCert> _fertilityRootOpened = new();
    private string _fertilityRootFingerprint = "";
    private long _fertilityRootEvaluatorCalls;
    private bool _freezeAdaptiveFuel;
    private int _reflexAdaptiveOperations;
    private int _rung0Opportunities;
    private int _rung0CarrierBoundCandidates;
    private int _rung0GuardEligibleCandidates;
    private int _rung0PaidAttempts;
    private int _rung0AttemptedCandidates;
    private int _rung0Compositions;
    private int _rung0ZeroEvaluatorCompositions;
    private int _rung0Audits;
    private int _rung0AgreedAudits;
    private int _rung0DisagreedAudits;
    private int _rung0NotSelectedAudits;
    private int _relationNullExecutions;
    private int _relationNullDivergences;
    private int _relationNullAuthorityPredictions;
    private int _relationNullPairsConsidered;
    private int _relationNullPairsCreated;
    private int _relationNullRejectNoCarrier;
    private int _relationNullRejectShape;
    private int _relationNullRejectGrade;
    private ulong _rung0CompositionDigest;
    private string _rung0SourceDigest = "";
    private string _rung0ConfigDigest = "";
    private readonly List<EmlRung0FunnelReceipt> _rung0FunnelReceipts = new();
    private int _checkpointRung0FunnelReceiptCount;

    internal EmlSieve Sieve => _sieve;
    internal int SamplingChunkCount => _chunks?.Count ?? 0;
    internal EmlActionSelections ActionSelection => _actionSelection;
    internal EmlAccretion LastAccretion => _lastAccretion;
    internal IReadOnlyList<EmlCertificateDelta> LastDeltas => _lastDeltas;
    internal int CorroborationWeight => _corrobW;
    internal bool UsesActionProcedure => _actionSelection is
        EmlActionSelections.Procedure or
        EmlActionSelections.ProcedureShuffled or
        EmlActionSelections.ProcedureGuarded or
        EmlActionSelections.ProcedureGuardShuffled;
    internal long EvaluatorCalls => _sieve.EvaluatorClock.ProgramPointEvaluations;
    internal bool HasRulerLift => _lift is not null;
    internal int RulerWindowInterval => _lift?.Knobs.Window ?? 0;
    internal IReadOnlyList<EmlCert> FertilityRootOpened => _fertilityRootOpened;
    internal string FertilityRootFingerprint => _fertilityRootFingerprint;
    internal long FertilityRootEvaluatorCalls => _fertilityRootEvaluatorCalls;
    internal bool UsesReflexFrozenFuel => _freezeAdaptiveFuel;
    internal int ReflexAdaptiveOperations => _reflexAdaptiveOperations;
    internal IReadOnlyList<EmlRung0FunnelReceipt> Rung0FunnelReceipts => _rung0FunnelReceipts;

    private void RecordRung0Funnel(IReadOnlyList<EmlRung0FunnelReceipt> receipts, List<CortexObservationField> fields)
    {
        for (int i = 0; i < receipts.Count; i++)
        {
            EmlRung0FunnelReceipt receipt = receipts[i];
            _rung0FunnelReceipts.Add(receipt);
            string donor = receipt.RelationNullDonor is EmlRelationNullDonorProvenance provenance
                ? $"|donor-claim={provenance.SourcePredictionID.Value}|donor-obligation={provenance.ObligationID}|donor-supports={string.Join(',', provenance.SupportEventIDs.Select(static id => id.Value))}|donor-laws={string.Join(',', provenance.LawAdmissionIDs)}"
                : "";
            fields.Add(new CortexObservationField(
                "rung0-funnel",
                $"{receipt.Stage}|accepted={receipt.Accepted}|reason={receipt.Reason}|claim={receipt.ObligationPredictionID.Value}|obligation={receipt.ObligationID}|rule={receipt.RuleID.Value}|proof={receipt.ProofID}|audit={receipt.AuditID}|audit-selection={receipt.AuditSelection}|admission={receipt.AdmissionID}|closure={receipt.ClosureID}|eval={receipt.Evaluation.Start}:{receipt.Evaluation.End}{donor}",
                Blur.SlotSources.GrammarPrior));
        }
    }

    internal EmlOrdinaryRunRung0Receipt ReadOrdinaryRunRung0Receipt()
        => EmlOrdinaryRunRung0Receipt.Create(
            _rung0Mode,
            _rung0Mode == EmlRung0Modes.Disabled ? EmlRematchAssayStatuses.Invalid : EmlRematchAssayStatuses.Exact,
            _rung0Mode == EmlRung0Modes.Armed && _relationNullExecutions > 0 && _relationNullDivergences == _relationNullExecutions
                && _relationNullAuthorityPredictions == 0 ? EmlRematchPowerStatuses.Powered : EmlRematchPowerStatuses.Unpowered,
            _rung0Opportunities, _rung0CarrierBoundCandidates, _rung0GuardEligibleCandidates,
            _rung0PaidAttempts, _rung0AttemptedCandidates,
            _rung0Compositions, _rung0ZeroEvaluatorCompositions, _rung0Audits,
            _rung0AgreedAudits, _rung0DisagreedAudits, _rung0NotSelectedAudits,
            _relationNullExecutions, _relationNullDivergences, _relationNullAuthorityPredictions,
            _relationNullPairsConsidered, _relationNullPairsCreated,
            _relationNullRejectNoCarrier, _relationNullRejectShape, _relationNullRejectGrade,
            _rung0CompositionDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture),
            _rung0SourceDigest, _rung0ConfigDigest);

    internal void ConfigureOrdinaryRunRung0Digests(string sourceDigest)
    {
        _rung0SourceDigest = sourceDigest ?? "";
        _rung0ConfigDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('|', _seed, _processCatalog, _rung0Mode, _deliberationMode, _sampler.MaxLen, _sampler.RailReads.Shell))));
    }

    /// The paired anytime null keeps the exact loaded action/config/world image but suppresses adaptive
    /// obligation leases after the fork boundary. This is an intervention, not a second curriculum/config.
    internal void ConfigureAnytimeArm(EmlAnytimeArmModes mode)
    {
        _freezeAdaptiveFuel = mode == EmlAnytimeArmModes.ReflexFrozenFuel;
        if (_freezeAdaptiveFuel)
        {
            // A checkpoint may carry the deliberation procedure's cursor. Reflex starts at the same world state,
            // but must not inherit a pending solve-hole transition across the intervention boundary.
            _actionProcedure = null;
            _reflexAdaptiveOperations = 0;
        }
    }
    internal EmlActionSchedule FertilitySchedule => new(_actionRng, _actionDecision, _roundRobinCursor);
    public double IntrinsicFrontierResidual => _actionSelection == EmlActionSelections.Off
        ? double.NaN
        : _intrinsicFrontierResidual;

    private struct EmlObligationSearchState
    {
        public int Epoch;
        public int Attempts;
    }

    internal EmlBranchingReceipt ReadBranchingReceipt() => new(
        _actionSelection,
        EvaluatorCalls,
        _sieve.DistinctCerts,
        _sieve.ExactClasses,
        _sieve.TargetsHit(),
        _proceduresStarted,
        _proceduresCompleted,
        _procedureGuardsPassed,
        _procedureGuardsSkipped,
        _procedureGuardsAbstained,
        _procedureCanonicalDeltas);

    internal EmlPolicyOutcomeSnapshot ReadPolicyOutcomeSnapshot() => new(
        _sieve.EvaluatorClock.ProgramPointEvaluations,
        _sharedCanonicalDeltas,
        _sharedFirstCaptures,
        _sieve.EvaluatorClock.HistoryComplete);

    internal RulerLiftPolicySnapshot ReadRulerLiftPolicySnapshot(Cortex cortex)
    {
        if (_lift is null) throw new InvalidOperationException("RulerLift is not mounted");
        CortexPolicyRuntimeReceipt runtime = cortex.ReadPolicyRuntimeReceipt(RulerLift.PolicyID);
        CortexPolicyDecisionReadout decision = cortex.ReadPolicyDecisionReadout(RulerLift.PolicyID);
        RulerLiftPolicyCausalReceipt causal = RulerLiftPolicyCausalReceipt.Create(
            in runtime,
            in decision,
            _lift.ReadPendingPolicyOutcomes());
        return new RulerLiftPolicySnapshot(
            runtime.Authority,
            _lift.CompletedWindows,
            _sieve.ExactClasses,
            _sieve.TheoremClasses,
            _sieve.TargetsHit(),
            _sieve.EvaluatorClock.ProgramPointEvaluations,
            _lift.Ruler,
            _lift.Lifts.Count,
            _sieve.EvaluatorClock.HistoryComplete,
            causal);
    }

    internal void ConfigureFertility(EmlFertilityInterventions intervention, long stopAt)
    {
        _fertilityIntervention = intervention;
        _fertilityStopAt = stopAt;
        _fertilityRootHandled = false;
        _fertilityRootInFlight = false;
        _fertilityAdmissionState = null;
        _fertilityRootOpened.Clear();
        _fertilityRootFingerprint = "";
        _fertilityRootEvaluatorCalls = 0;
    }

    internal bool HoldsFertilityRoot => _fertilityIntervention == EmlFertilityInterventions.Hold;

    internal static List<CortexTool> CreateActionTools() => new()
    {
        new FreshBiasTool(),
        new FreshEnumTool(),
        new SolveHoleTool(),
        new CounterexampleTool(),
        new CompareTool(),
    };

    internal static List<CortexActionPolicy> CreateActionPolicies()
        => new() { new EmlActionPolicy() };

    internal static List<CortexReward> CreateRewards(bool includesActions = true)
    {
        List<CortexReward> rewards = new() { new RulerLiftAutonomyReward() };
        if (!includesActions) return rewards;
        rewards.Add(new EmlDiscoveryReward());
        rewards.Add(new EmlActionAutonomyReward());
        return rewards;
    }

    private void InitializeActionState(EmlActionSelections selection, ulong seed, in EmlKnobs knobs)
    {
        _actionSelection = selection;
        _actionUnits = knobs.Units;
        _actionGain = knobs.Gain;
        _actionRng = (seed == 0 ? 0x9E3779B97F4A7C15UL : seed) ^ 0xD1B54A32D192ED03UL;
        if (selection == EmlActionSelections.Off) return;
        _actionMeter = new OutcomeMeter<EmlActionArms>(ActionArmOrder);
        _shuffledActionOrder = BuildShuffledOrder(seed);
        RebuildActionEnumeration(_sampler.MaxLen, taken: 0, done: false);
    }

    internal void ResolveChunks(RePairResult grammar)
    {
        if (_freezeSamplingGrammar && _chunks is not null) return;
        if (ReferenceEquals(grammar.Rules, _chunkRules)) return;
        _chunks = EmlGen.PureChunks(grammar);
        _chunkRules = grammar.Rules;
    }

    private void SaveSamplingGrammar(CkptWriter writer)
    {
        writer.Section(SamplingGrammarTag);
        writer.Bool(_chunks is not null);
        List<EmlGen.Chunk> chunks = _chunks ?? [];
        writer.I32(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
        {
            EmlGen.Chunk chunk = chunks[i];
            writer.Str(chunk.Toks);
            writer.I32(chunk.Freq);
            writer.I32(chunk.DeltaH);
            writer.I32(chunk.MinReq);
        }
    }

    private void LoadSamplingGrammar(CkptReader reader)
    {
        reader.Expect(SamplingGrammarTag);
        bool initialized = reader.Bool();
        int count = reader.I32();
        if (count < 0 || count > 1_000_000)
            throw new InvalidDataException($"invalid frozen EML grammar chunk count {count}");
        List<EmlGen.Chunk> chunks = new(count);
        for (int i = 0; i < count; i++)
            chunks.Add(new EmlGen.Chunk(reader.Str(), reader.I32(), reader.I32(), reader.I32()));
        _chunks = initialized ? chunks : null;
        _chunkRules = null;
    }

    internal bool TryProposeActionArm(out EmlActionArms arm, out EmlActionSelectionCauses cause)
    {
        if (_actionMeter is null) throw new InvalidOperationException("EML actions are not armed");
        switch (_actionSelection)
        {
            case EmlActionSelections.RoundRobin:
                arm = ActionArmOrder[_roundRobinCursor % ActionArmOrder.Length];
                cause = EmlActionSelectionCauses.FixedSchedule;
                break;
            case EmlActionSelections.ShuffledFixed:
                arm = _shuffledActionOrder[_roundRobinCursor % _shuffledActionOrder.Length];
                cause = EmlActionSelectionCauses.FixedSchedule;
                break;
            case EmlActionSelections.Procedure:
            case EmlActionSelections.ProcedureShuffled:
            case EmlActionSelections.ProcedureGuarded:
            case EmlActionSelections.ProcedureGuardShuffled:
                throw new InvalidOperationException("procedure actions must be selected through their bound program");
            default:
                throw new InvalidOperationException("EML action policy ran while disabled");
        }
        return true;
    }

    internal void StageActionArm(EmlActionArms arm, EmlActionSelectionCauses cause)
    {
        if (_actionMeter is null) throw new InvalidOperationException("EML actions are not armed");
        if (UsesActionProcedure) throw new InvalidOperationException("procedure actions stage through their bound program");
        if (_actionInFlight || _actionMeter.PendingIndex >= 0)
            throw new InvalidOperationException("an EML action is already staged");
        if (_actionSelection is EmlActionSelections.RoundRobin or EmlActionSelections.ShuffledFixed)
            _roundRobinCursor++;
        _actionMeter.RecordFire(arm);
        _actionMeter.Pend(arm);
        _actionSelectionCauses[(int)cause]++;
        _actionDecision++;
        _currentActionArm = arm;
        _actionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        _actionInFlight = true;
        BeginFertilityRoot();
    }

    internal void RecordActionAbstention()
    {
        _actionSelectionCauses[(int)EmlActionSelectionCauses.Abstention]++;
        _actionDecision++;
    }

    internal bool TryProposeProcedureAction(List<CortexActionArgument> arguments,
        out EmlActionArms arm, out EmlActionSelectionCauses cause, out CortexProcedureProposal proposal)
    {
        if (!UsesActionProcedure) throw new InvalidOperationException("EML procedure is not armed");
        while (true)
        {
            if (_actionProcedure is null || _actionProcedure.Complete)
            {
                _actionProcedure = CreateActionProcedure(_actionSelection);
                _procedureSolveDeltas.Clear();
                _proceduresStarted++;
            }

            proposal = _actionProcedure.ProposeNext(arguments);
            CortexProcedureTransitions transition = proposal.Transition;
            if (transition == CortexProcedureTransitions.Skip)
            {
                _procedureGuardsSkipped++;
                _actionProcedure.Commit(in proposal);
                if (_actionProcedure.Complete) RecordCompletedProcedure();
                continue;
            }
            if (transition == CortexProcedureTransitions.Abstain)
            {
                _procedureGuardsAbstained++;
                _actionProcedure.Commit(in proposal);
                RecordCompletedProcedure();
                arm = default;
                cause = EmlActionSelectionCauses.Abstention;
                _actionSelectionCauses[(int)cause]++;
                _actionDecision++;
                return false;
            }
            if (transition is CortexProcedureTransitions.Blocked or CortexProcedureTransitions.Complete)
            {
                proposal = default;
                arm = default;
                cause = EmlActionSelectionCauses.Abstention;
                _actionSelectionCauses[(int)cause]++;
                _actionDecision++;
                return false;
            }
            if (ContainsGuardArgument(arguments)) _procedureGuardsPassed++;
            arm = ParseProcedureArm(proposal.Tool);
            cause = EmlActionSelectionCauses.FixedSchedule;
            return true;
        }
    }

    internal void StageProcedureAction(in CortexProcedureProposal proposal, EmlActionArms arm,
        EmlActionSelectionCauses cause)
    {
        if (_actionProcedure is null || proposal.Transition != CortexProcedureTransitions.Execute
            || ParseProcedureArm(proposal.Tool) != arm)
            throw new InvalidOperationException("EML procedure proposal does not match the staged action");
        if (_actionInFlight) throw new InvalidOperationException("an EML action is already staged");
        _actionProcedure.Commit(in proposal);
        _actionSelectionCauses[(int)cause]++;
        _actionDecision++;
        _currentActionArm = arm;
        _actionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        _actionInFlight = true;
        if (arm != EmlActionArms.SolveHole) return;
        if (_actionSelection is EmlActionSelections.ProcedureShuffled or EmlActionSelections.ProcedureGuardShuffled)
            _procedureShuffledBindings++;
        else _procedureBindings++;
    }

    internal EmlActionArms ChooseSharedAction(
        Cortex cortex,
        EmlActionArms launchpad,
        out EmlActionSelectionCauses cause)
    {
        Span<MetricSample> features = stackalloc MetricSample[11]
        {
            new(new MetricID((ushort)SharedActionMetricIDs.FrontierResidual), NumericValue.FromF64(_intrinsicFrontierResidual)),
            new(new MetricID((ushort)SharedActionMetricIDs.GlobalYield), NumericValue.FromF64(_actionGlobalYield)),
            new(new MetricID((ushort)SharedActionMetricIDs.DistinctCertificates), NumericValue.FromI64(_sieve.DistinctCerts)),
            new(new MetricID((ushort)SharedActionMetricIDs.ExactClasses), NumericValue.FromI64(_sieve.ExactClasses)),
            new(new MetricID((ushort)SharedActionMetricIDs.TargetsHit), NumericValue.FromI64(_sieve.TargetsHit())),
            new(new MetricID((ushort)SharedActionMetricIDs.FreshBiasYield), NumericValue.FromF64(ReadEmpiricalArmYield(EmlActionArms.FreshBias))),
            new(new MetricID((ushort)SharedActionMetricIDs.FreshEnumYield), NumericValue.FromF64(ReadEmpiricalArmYield(EmlActionArms.FreshEnum))),
            new(new MetricID((ushort)SharedActionMetricIDs.SolveHoleYield), NumericValue.FromF64(ReadEmpiricalArmYield(EmlActionArms.SolveHole))),
            new(new MetricID((ushort)SharedActionMetricIDs.CounterexampleYield), NumericValue.FromF64(ReadEmpiricalArmYield(EmlActionArms.Counterexample))),
            new(new MetricID((ushort)SharedActionMetricIDs.CompareYield), NumericValue.FromF64(ReadEmpiricalArmYield(EmlActionArms.Compare))),
            new(new MetricID((ushort)SharedActionMetricIDs.DecisionIndex), NumericValue.FromI64(_actionDecision)),
        };
        _sharedActionDecision = cortex.ChoosePolicyAction(
            ActionPolicyID,
            (int)launchpad,
            features,
            ActionReadoutExcludedMetricIDs);
        _sharedActionOutcomePending = true;
        _sharedActionInvariantClean = true;
        cause = _sharedActionDecision.Authority == CortexPolicyAuthorities.Grammar
            ? EmlActionSelectionCauses.Grammar
            : EmlActionSelectionCauses.FixedSchedule;
        return (EmlActionArms)_sharedActionDecision.Action;
    }

    internal void RejectSharedAction() => _sharedActionInvariantClean = false;

    private double ReadEmpiricalArmYield(EmlActionArms arm)
    {
        int offers = _actionOffers[(int)arm];
        return offers == 0 ? double.NaN : _actionDeltaOutcomes[(int)arm] / offers;
    }

    internal void MeterAction(Cortex cortex, EmlActionArms arm)
    {
        EmlEvaluatorInterval evaluation = _sieve.EvaluatorClock.MeasureFrom(_actionEvaluatorStart);
        _actionEvaluatorCalls[(int)arm] += evaluation.Calls;
        if (UsesActionProcedure)
        {
            double procedureYield = arm == EmlActionArms.Compare && _procedureSolveDeltas.Count > 0 ? 1.0 : 0.0;
            _actionGlobalYield = double.IsNaN(_actionGlobalYield)
                ? procedureYield
                : _actionGlobalYield + (1.0 / 8) * (procedureYield - _actionGlobalYield);
            _actionGlobalOutcomes++;
            if (procedureYield > 0) _actionDeltaOutcomes[(int)arm]++;
            ResolveSharedActionOutcome(cortex, in evaluation);
            _actionInFlight = false;
            CompleteFertilityRoot();
            return;
        }
        if (_actionMeter is null) return;
        if (_actionMeter.PendingIndex < 0 || _actionMeter.ArmAt(_actionMeter.PendingIndex) != arm)
            throw new InvalidOperationException($"EML action outcome skew: selected {arm}, pending {(_actionMeter.PendingIndex < 0 ? "none" : _actionMeter.ArmAt(_actionMeter.PendingIndex))}");
        double yield = _lastAccretion.CanonicalDeltas > 0 ? 1.0 : 0.0;
        _actionGlobalYield = double.IsNaN(_actionGlobalYield)
            ? yield
            : _actionGlobalYield + (1.0 / 8) * (yield - _actionGlobalYield);
        _actionGlobalOutcomes++;
        _actionMeter.Meter(yield);
        ResolveSharedActionOutcome(cortex, in evaluation);
        _actionInFlight = false;
        CompleteFertilityRoot();
    }

    private void ResolveSharedActionOutcome(Cortex cortex, in EmlEvaluatorInterval evaluation)
    {
        if (!_sharedActionOutcomePending) return;
        Span<MetricSample> outcomes = stackalloc MetricSample[3]
        {
            new(new MetricID((ushort)SharedActionMetricIDs.CanonicalDeltas), NumericValue.FromI64(_lastAccretion.CanonicalDeltas)),
            new(new MetricID((ushort)SharedActionMetricIDs.FirstCaptures), NumericValue.FromI64(_lastAccretion.FirstCaptures)),
            new(new MetricID((ushort)SharedActionMetricIDs.EvaluatorCalls), NumericValue.FromI64(evaluation.Calls)),
        };
        cortex.ResolvePolicyOutcome(in _sharedActionDecision, outcomes, _sharedActionInvariantClean, conservedCost: evaluation.Calls);
        _sharedCanonicalDeltas += _lastAccretion.CanonicalDeltas;
        _sharedFirstCaptures += _lastAccretion.FirstCaptures;
        _sharedActionOutcomePending = false;
    }

    internal bool ShouldAdmitFertilityAction(Cortex cortex, EmlActionArms arm, List<CortexObservationField> fields)
    {
        CaptureFertilityRootObservation(arm, fields);
        if (!_fertilityRootInFlight || _fertilityIntervention != EmlFertilityInterventions.Shadow) return true;
        if (_fertilityAdmissionState is null) throw new InvalidOperationException("shadow root has no admission snapshot");
        EmlDeltaSummary deltas = CollectPendingDeltas();
        RecordFertilityRootOpened();
        _lastAccretion = new EmlAccretion(0, _lastDeltas.Count, deltas.FirstCaptures,
            deltas.RepresentativeImprovements, deltas.TargetCaptures, 0);
        _actionFirstCaptures[(int)arm] += deltas.FirstCaptures;
        if (_lastDeltas.Count > 0) _actionDeltaOutcomes[(int)arm]++;
        EmlEvaluatorClockSnapshot consumedClock = _sieve.EvaluatorClock.Capture();
        _sieve.RestoreAdmissionState(_fertilityAdmissionState, in consumedClock);
        _fertilityAdmissionState = null;
        _accretionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        MeterAction(cortex, arm);
        return false;
    }

    internal void RequestFertilityStop(Cortex cortex)
    {
        if (_fertilityStopAt == long.MaxValue) return;
        if (_sieve.EvaluatorClock.ProgramPointEvaluations >= _fertilityStopAt) cortex.RequestStop();
    }

    private void BeginFertilityRoot()
    {
        if (_fertilityRootHandled || _fertilityIntervention is EmlFertilityInterventions.None or EmlFertilityInterventions.Hold) return;
        _fertilityRootInFlight = true;
        if (_fertilityIntervention == EmlFertilityInterventions.Shadow)
            _fertilityAdmissionState = _sieve.CaptureAdmissionState();
    }

    private void CompleteFertilityRoot()
    {
        if (!_fertilityRootInFlight) return;
        RecordFertilityRootOpened();
        _fertilityRootInFlight = false;
        _fertilityRootHandled = true;
    }

    private void RecordFertilityRootOpened()
    {
        for (int i = 0; i < _lastDeltas.Count; i++)
        {
            EmlCertificateDelta delta = _lastDeltas[i];
            if (delta.Change != EmlCertificateChanges.ClassOpened || delta.After is not EmlCert certificate) continue;
            if (!_fertilityRootOpened.Contains(certificate)) _fertilityRootOpened.Add(certificate);
        }
    }

    private void CaptureFertilityRootObservation(EmlActionArms arm, List<CortexObservationField> fields)
    {
        if (!_fertilityRootInFlight || _fertilityRootFingerprint.Length > 0) return;
        StringBuilder fingerprint = new();
        fingerprint.Append(EmlActionSelectionTokens.ActionToken(arm));
        for (int i = 0; i < fields.Count; i++)
        {
            CortexObservationField field = fields[i];
            fingerprint.Append('|').Append(field.Slot).Append('=').Append(field.Value).Append('@').Append((int)field.Source);
        }
        _fertilityRootFingerprint = fingerprint.ToString();
        _fertilityRootEvaluatorCalls = _sieve.EvaluatorClock.ProgramPointEvaluations - _actionEvaluatorStart;
    }

    internal void BindProcedureObservation(EmlActionArms arm, List<CortexObservationField> fields)
    {
        if (!UsesActionProcedure || _actionProcedure is null) return;
        for (int i = 0; i < fields.Count; i++)
        {
            CortexObservationField field = fields[i];
            _actionProcedure.AddInput(new CortexProcedureInput(field.Slot, field.Source, field.Value));
        }
        if (arm == EmlActionArms.SolveHole)
        {
            _procedureSolveDeltas.Clear();
            for (int i = 0; i < _lastDeltas.Count; i++) _procedureSolveDeltas.Add(_lastDeltas[i]);
        }
        if (arm == EmlActionArms.Compare)
        {
            if (ReadField(fields, "obligation-match") == "yes") _procedureObligationMatches++;
            if (_procedureSolveDeltas.Count > 0)
            {
                _procedureNewDeltas++;
                _procedureCanonicalDeltas += _procedureSolveDeltas.Count;
            }
        }
        if (_actionProcedure.Complete) RecordCompletedProcedure();
    }

    internal string OfferFreshBias(RePairResult grammar)
    {
        RecordActionOffer(EmlActionArms.FreshBias);
        if (_firstGenerativeDecision < 0) _firstGenerativeDecision = _actionDecision;
        ResolveChunks(grammar);
        string program = EmlGen.Sample(_chunks!, _actionUnits, _sampler.MaxLen, _actionGain, 0,
            ref _actionRng, _actionSampleBuilder, _actionSamplePool);
        OfferWithCurrentOpportunity(program);
        return program;
    }

    internal string OfferFreshEnumeration()
    {
        RecordActionOffer(EmlActionArms.FreshEnum);
        if (_firstGenerativeDecision < 0) _firstGenerativeDecision = _actionDecision;
        EnsureActionEnumeration();
        if (_actionEnumDone || _actionEnum is null) return "";
        if (!_actionEnum.MoveNext())
        {
            _actionEnumDone = true;
            return "";
        }
        _actionEnumTaken++;
        string program = _actionEnum.Current;
        OfferWithCurrentOpportunity(program);
        return program;
    }

    internal void RecordActionStep(EmlActionArms arm, int step)
    {
        if (_firstGenerativeStep >= 0 || arm is not (EmlActionArms.FreshBias or EmlActionArms.FreshEnum)) return;
        _firstGenerativeStep = step;
    }

    internal string OfferHoleSolution(List<CortexActionArgument> arguments, List<CortexObservationField> fields)
    {
        RecordActionOffer(EmlActionArms.SolveHole);
        if (_freezeAdaptiveFuel)
        {
            _reflexAdaptiveOperations++;
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "reflex-adaptive-operation-rejected", Blur.SlotSources.PriorObservation));
            return "";
        }
        if (!TryResolveBoundTarget(arguments, out EmlObligationTarget target, out EmlObligationResolution obligation, out EmlExactCompositionObligation exactTarget))
        {
            fields.Add(new CortexObservationField("obligation-claim-id", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "no-obligation", Blur.SlotSources.PriorObservation));
            return "";
        }

        EmlPredictionID targetPredictionID = target.SourcePredictionID;
        string targetIdentity = target.Species == EmlObligationTargetSpecies.ExactComposition
            ? _sieve.ExactCompositionObligationIdentity(targetPredictionID)
            : _sieve.ObligationIdentity(targetPredictionID);
        fields.Add(new CortexObservationField("obligation-claim-id", targetPredictionID.Value.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("obligation-species", target.Species.ToString(), Blur.SlotSources.PriorObservation));

        if (!TryStartObligationSearch(in obligation))
        {
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "frontier-unchanged", Blur.SlotSources.PriorObservation));
            _obligationSuppressedCalls++;
            return "";
        }

        EmlDeliberationQuota deliberationQuota = _deliberationQuota;
        if (_pairedFuelSchedule is not null)
        {
            if (_pairedFuelStepAllocated || IsZero(_pairedFuelWallet))
            {
                fields.Add(new CortexObservationField("solve-status", "schedule-wallet-exhausted", Blur.SlotSources.PriorObservation));
                fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
                return "";
            }
            deliberationQuota = PairedFuelWalletQuota();
        }
        EmlDeliberationLease deliberation = _sieve.ReserveDeliberation(in obligation, deliberationQuota);
        if (!deliberation.IsReused)
            MarkPairedFuelLeaseAllocated();
        fields.Add(new CortexObservationField("deliberation-mode", _deliberationMode.ToString().ToLowerInvariant(), Blur.SlotSources.PriorObservation));
        if (deliberation.IsReused)
        {
            deliberation.Complete(EmlDeliberationOutcomes.Reused, "reservation already settled");
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "reused", Blur.SlotSources.PriorObservation));
            return "";
        }
        if (_freezeAdaptiveFuel || _deliberationMode == EmlDeliberationModes.Frozen)
        {
            EmlDeliberationSettlement settlement = deliberation.Complete(EmlDeliberationOutcomes.Suppressed, "deliberation frozen");
            fields.Add(new CortexObservationField("deliberation-fuel",
                $"planned={settlement.Planned};actual={settlement.Actual};refund={settlement.Refund}",
                Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "deliberation-frozen", Blur.SlotSources.PriorObservation));
            return "";
        }
        _sieve.Grader.BindDeliberation(deliberation);
        EmlDeliberationOutcomes settlementOutcome = EmlDeliberationOutcomes.Interrupted;
        string settlementDetail = "cortex hole search interrupted";
        try
        {
        deliberation?.BeginPhase("candidate-supply");

        List<EmlHoleCandidate> candidates = new();
        long processFuel = GetResidualProcessFuel();
        if (target.Species == EmlObligationTargetSpecies.Residual)
        {
            AppendHoleCandidates(candidates, 512, deliberation);
            EmlProcessFunction negativeLogX = EmlProcessFunctions.CreateNegativeLog(EmlProcessInputSlots.X, processFuel);
            EmlProcessFunction negativeLogY = EmlProcessFunctions.CreateNegativeLog(EmlProcessInputSlots.Y, processFuel);
            deliberation?.ReserveCandidateSupplyItem();
            candidates.Add(new EmlHoleCandidate(EmlResidualExpression.CreateProcessFunction(in negativeLogX), "process:negative-log:x", 1));
            deliberation?.ReserveCandidateSupplyItem();
            candidates.Add(new EmlHoleCandidate(EmlResidualExpression.CreateProcessFunction(in negativeLogY), "process:negative-log:y", 1));
            if (_processCatalog == EmlProcessCatalogs.Full)
                AppendStructuralProcessCandidates(in obligation, processFuel, candidates, deliberation);
        }
        List<EmlLawCandidateInstantiation> lawCandidates = new();
        if (target.Species == EmlObligationTargetSpecies.ExactComposition)
            _lawStore.AppendExactPredictionBoundCandidateRewrites(in exactTarget, _sieve, lawCandidates, deliberation);
        else
            _lawStore.AppendPredictionBoundCandidateRewrites(in obligation, _sieve, lawCandidates, deliberation);
        int carrierBound = 0;
        int guardEligible = 0;
        for (int candidateIndex = 0; candidateIndex < lawCandidates.Count; candidateIndex++)
        {
            EmlLawCandidateInstantiation candidate = lawCandidates[candidateIndex];
            if (candidate.PredictionCarrier is EmlRewritePredictionCarrier)
            {
                carrierBound++;
                if (candidate.Rewrite.IsRung0Eligible) guardEligible++;
            }
        }
        _rung0CarrierBoundCandidates = checked(_rung0CarrierBoundCandidates + carrierBound);
        _rung0GuardEligibleCandidates = checked(_rung0GuardEligibleCandidates + guardEligible);
        if (carrierBound > 0)
            fields.Add(new CortexObservationField("rung0-carrier-bound", carrierBound.ToString(), Blur.SlotSources.GrammarPrior));
        if (guardEligible > 0)
            fields.Add(new CortexObservationField("rung0-guard-eligible", guardEligible.ToString(), Blur.SlotSources.GrammarPrior));
        deliberation?.BeginPhase("rung0-derivation");
        EmlRung0AdmissionResult rung0 = default;
        bool attemptedRung0 = false;
        int fundedBefore = _rung0PaidAttempts;
        int attemptedBefore = _rung0AttemptedCandidates;
        HashSet<EmlRuleID> numericFallbackProhibited = new();
        bool isExactTarget = target.Species == EmlObligationTargetSpecies.ExactComposition;
        if (_rung0Mode == EmlRung0Modes.Armed && isExactTarget)
        {
            // The opportunity census is the exact theorem-use funnel, not generic
            // residual repair.  A claim-bound exact solve-hole action remains an
            // opportunity even when exact-form supply is empty; generic residual
            // solve-hole selections must not make the theory arm look powered.
            _rung0Opportunities++;
            if (lawCandidates.Count > 0
                && deliberation is not null && _deliberationMode != EmlDeliberationModes.Frozen && !_freezeAdaptiveFuel)
                _rung0PaidAttempts = checked(_rung0PaidAttempts + lawCandidates.Count);
            if (lawCandidates.Count > 0)
                ExecuteOrdinaryRelationNullAssay(in obligation, targetIdentity, lawCandidates, deliberation, fields);
            else
            {
                EmlRung0FunnelReceipt exactOpportunity = new(
                    EmlRung0FunnelStages.Opportunity,
                    targetPredictionID,
                    targetIdentity,
                    default,
                    Accepted: true,
                    "exact-target-no-law-candidate",
                    "", "", "", "",
                    EmlEvaluatorInterval.EmptyAt(_sieve.EvaluatorClock.ProgramPointEvaluations));
                EmlRung0FunnelReceipt exactEligibility = exactOpportunity with
                {
                    Stage = EmlRung0FunnelStages.Eligibility,
                    Accepted = false,
                    Reason = "exact-form-supply-empty",
                };
                RecordRung0Funnel(new[] { exactOpportunity, exactEligibility }, fields);
            }
            for (int i = 0; i < lawCandidates.Count; i++)
            {
                attemptedRung0 = true;
                _rung0AttemptedCandidates++;
                EmlLawCandidateInstantiation lawCandidate = lawCandidates[i];
                rung0 = EmlRung0Admission.TryAdmit(
                    _sieve,
                    _lawStore,
                    in lawCandidate,
                    deliberation);
                RecordRung0Funnel(rung0.FunnelReceipts, fields);
                RecordOrdinaryRung0(in rung0);
                if (rung0.NumericFallbackProhibited)
                    numericFallbackProhibited.Add(lawCandidate.Rewrite.RuleID);
                if (rung0.Admitted || rung0.Composition.Status == EmlRung0Statuses.Exhausted) break;
            }
        }
        else
        {
            EmlRung0FunnelReceipt noOpportunity = new(
                EmlRung0FunnelStages.Opportunity,
                targetPredictionID,
                targetIdentity,
                default,
                false,
                _rung0Mode == EmlRung0Modes.Disabled ? "rung0-disabled" : isExactTarget ? "no-law-candidate" : "non-exact-target",
                "", "", "", "",
                EmlEvaluatorInterval.EmptyAt(_sieve.EvaluatorClock.ProgramPointEvaluations));
            RecordRung0Funnel(new[] { noOpportunity }, fields);
        }
        if (_rung0PaidAttempts > fundedBefore)
            fields.Add(new CortexObservationField("rung0-funded-attempts", (_rung0PaidAttempts - fundedBefore).ToString(), Blur.SlotSources.GrammarPrior));
        if (_rung0AttemptedCandidates > attemptedBefore)
            fields.Add(new CortexObservationField("rung0-attempted-candidates", (_rung0AttemptedCandidates - attemptedBefore).ToString(), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField(
            "rung0-status",
            _rung0Mode == EmlRung0Modes.Disabled ? "Disabled" : attemptedRung0 ? rung0.Composition.Status.ToString() : EmlRung0Statuses.NoCandidate.ToString(),
            Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("rung0-expanded-states", rung0.Composition.Work.ExpandedStates.ToString(), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("rung0-visited-states", rung0.Composition.Work.VisitedStates.ToString(), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("rung0-applications", rung0.Composition.Work.Applications.ToString(), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("rung0-guard-rejections", rung0.Composition.Work.GuardRejections.ToString(), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("rung0-main-evaluator-delta", rung0.MainEvaluatorDelta.ToString(), Blur.SlotSources.GrammarPrior));
        if (rung0.Composition.Proof is EmlRung0Proof derivedProof)
        {
            fields.Add(new CortexObservationField("rung0-proof-digest", derivedProof.Digest.ToString("X16"), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("rung0-search-revision", derivedProof.SearchRevision.ToString(), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("rung0-search-depth", derivedProof.Budget.MaxDepth.ToString(), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("rung0-search-states", derivedProof.Budget.MaxStates.ToString(), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("rung0-search-applications", derivedProof.Budget.MaxApplications.ToString(), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("rung0-proof-steps", derivedProof.Steps.Count.ToString(), Blur.SlotSources.GrammarPrior));
        }
        if (rung0.Audit is EmlRung0Audit audit)
            fields.Add(new CortexObservationField("rung0-audit", audit.Status.ToString(), Blur.SlotSources.GrammarPrior));
        if (rung0.Admitted)
        {
            if (rung0.MainEvaluatorDelta != 0)
                throw new InvalidOperationException("admitted rung-0 derivation touched the main evaluator");
            if (rung0.ClosureProof is not EmlRung0ComposedFormProof closureProof || !closureProof.IsExactZeroAdmission)
                throw new InvalidOperationException("admitted rung-0 derivation did not emit its structural closure witness");
            _pendingRung0Closures.Add(targetPredictionID);
            string derivedProgram = rung0.Composition.Proof!.Value.ConsequentRPN;
            CompleteObligationSearch(targetPredictionID, changed: true);
            settlementOutcome = EmlDeliberationOutcomes.Solved;
            settlementDetail = "rung0-derived";
            fields.Add(new CortexObservationField("obligation-label", obligation.Label, Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("candidate-program", derivedProgram, Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "rung0-derived", Blur.SlotSources.PriorObservation));
            return derivedProgram;
        }
        if (target.Species == EmlObligationTargetSpecies.ExactComposition)
        {
            settlementOutcome = EmlDeliberationOutcomes.NoCandidate;
            settlementDetail = "exact target has no admitted guarded derivation";
            CompleteObligationSearch(targetPredictionID, changed: false);
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "no-candidate", Blur.SlotSources.PriorObservation));
            return "";
        }
        for (int i = 0; i < lawCandidates.Count; i++)
        {
            EmlLawRewrite rewrite = lawCandidates[i].Rewrite;
            if (numericFallbackProhibited.Contains(rewrite.RuleID)) continue;
            string provenance = "law:" + rewrite.LawProof.OccurrenceDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + rewrite.Orientation + ":" + rewrite.MatchedPath;
            deliberation?.ReserveCandidateSupplyItem();
            candidates.Add(new EmlHoleCandidate(rewrite.ConsequentRpn, provenance, rewrite.ConsequentSize));
        }
        List<EmlHoleRepairProposal> repairs = new();
        long solveStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        EmlHoleSolveResult result = EmlHoleSolver.Solve(
            _sieve.MintLog,
            in obligation,
            candidates,
            repairs,
            _sieve.EvaluatorClock,
            branchRadius: 2,
            grader: _sieve.Grader,
            deliberationLease: deliberation);
        fields.Add(new CortexObservationField("obligation-label", obligation.Label, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-candidates", result.CandidatePrograms.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-law-candidates", lawCandidates.Count.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-holes", result.HoleCount.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-join-attempts", result.JoinAttempts.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-verified", result.VerifiedRepairs.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-process-fuel", result.Work.ProcessFuel.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-finite-expressions", result.Telemetry.FiniteExpressions.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-composed-expressions", result.Telemetry.ComposedExpressions.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-ladder-requests", result.Telemetry.LadderRequests.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-ladder-cache-hits", result.Telemetry.LadderCacheHits.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-ladder-cache-misses", result.Telemetry.LadderCacheMisses.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-executed-probe-points", result.Telemetry.ExecutedProbePoints.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-unique-finite-keys", result.Telemetry.UniqueFiniteKeys.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-structural-nodes", result.Telemetry.StructuralNodes.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-wall-ms", result.Telemetry.WallMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Blur.SlotSources.PriorObservation));
        if (repairs.Count == 0)
        {
            settlementOutcome = result.Outcome;
            settlementDetail = "no candidate";
            CompleteObligationSearch(targetPredictionID, changed: false);
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "no-candidate", Blur.SlotSources.PriorObservation));
            return "";
        }

        EmlHoleRepairProposal repair = default;
        EmlCertificateDelta proofDelta = default;
        bool admitted = false;
        deliberation?.BeginPhase("proof-admission");
        for (int i = 0; i < repairs.Count; i++)
        {
            repair = repairs[_holeCursor++ % repairs.Count];
            if (repair.Expression.TryRenderRPN(out string finiteRPN))
            {
                if (!_sieve.TryAdmitResidualProof(targetPredictionID, finiteRPN, solveStart, out proofDelta, deliberation)) continue;
            }
            else if (repair.Expression.TryGetProcessFunction(out EmlProcessFunction processFunction))
            {
                if (!_sieve.TryAdmitProcessResidualProof(
                        targetPredictionID,
                        in processFunction,
                        repair.Composition,
                        solveStart,
                        out proofDelta,
                        out long admittedProcessFuel,
                        deliberation)) continue;
                fields.Add(new CortexObservationField("proof-process-fuel", admittedProcessFuel.ToString(), Blur.SlotSources.PriorObservation));
                if (repair.Composition is EmlResidualComposition derivation)
                    fields.Add(new CortexObservationField("proof-derivation", derivation.Receipt, Blur.SlotSources.GrammarPrior));
            }
            else continue;
            admitted = true;
            break;
        }
        if (!admitted)
        {
            settlementOutcome = EmlDeliberationOutcomes.Rejected;
            settlementDetail = "known proofs";
            CompleteObligationSearch(targetPredictionID, changed: false);
            fields.Add(new CortexObservationField("candidate-program", repair.Program, Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "known-proofs", Blur.SlotSources.PriorObservation));
            return "";
        }
        CompleteObligationSearch(targetPredictionID, changed: true);
        settlementOutcome = EmlDeliberationOutcomes.Solved;
        settlementDetail = "accepted";
        string program = repair.Program;
        if (repair.Expression.TryRenderRPN(out string offeredRPN)) OfferWithCurrentOpportunity(offeredRPN);
        fields.Add(new CortexObservationField("solve-status", "proof-attached", Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("candidate-program", program, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("candidate-left", repair.Provenance.LeftProgram, Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("candidate-right", repair.Provenance.RightProgram, Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("candidate-join", repair.Provenance.Orientation.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("proof-change", EmlActionSelectionTokens.GetCertificateChangeToken(proofDelta.Change), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("candidate-cost", repair.Cost.Total.ToString(), Blur.SlotSources.PriorObservation));
        return program;
        }
        catch (EmlDeliberationExhaustedException exhausted)
        {
            settlementOutcome = EmlDeliberationOutcomes.Exhausted;
            settlementDetail = exhausted.Message;
            fields.Add(new CortexObservationField("candidate-program", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("solve-status", "fuel-exhausted", Blur.SlotSources.PriorObservation));
            return "";
        }
        finally
        {
            deliberation?.Complete(settlementOutcome, settlementDetail);
            _sieve.Grader.BindDeliberation(null);
        }
    }

    private static bool IsZero(in EmlDeliberationCounts value)
        => value == EmlDeliberationCounts.Zero;

    private void AppendStructuralProcessCandidates(
        in EmlObligationResolution obligation,
        long processFuel,
        List<EmlHoleCandidate> candidates,
        EmlDeliberationLease? deliberationLease = null)
    {
        EmlMint sourceMint = _sieve.MintLog[obligation.SourcePredictionID.Value];
        if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction claim)) return;
        if (EmlResidualDeriver.TryDeriveSharedExponentialArgument(
                obligation.SourcePredictionID, in claim, processFuel, out EmlResidualComposition logRatio, deliberationLease))
            AppendProcessCandidate(in logRatio, candidates, deliberationLease);
        if (EmlResidualDeriver.TryDeriveExponentialTail(
                obligation.SourcePredictionID, in claim, processFuel, out EmlResidualComposition exponentialTail, deliberationLease))
            AppendProcessCandidate(in exponentialTail, candidates, deliberationLease);
    }

    private static void AppendProcessCandidate(
        in EmlResidualComposition derivation,
        List<EmlHoleCandidate> candidates,
        EmlDeliberationLease? deliberationLease)
    {
        EmlProcessFunction process = derivation.Process;
        deliberationLease?.ReserveCandidateSupplyItem();
        candidates.Add(new EmlHoleCandidate(
            EmlResidualExpression.CreateProcessFunction(in process),
            "process:" + derivation.Receipt + ":claim=" + derivation.SourcePredictionID.Value,
            checked(1 + derivation.NumeratorRPN.Length + derivation.DenominatorRPN.Length),
            derivation));
    }

    private void RecordOrdinaryRung0(in EmlRung0AdmissionResult rung0)
    {
        if (!rung0.Composition.Composed) return;
        _rung0Compositions++;
        if (rung0.MainEvaluatorDelta == 0) _rung0ZeroEvaluatorCompositions++;
        if (rung0.Audit is EmlRung0Audit audit)
        {
            _rung0Audits++;
            switch (audit.Status)
            {
                case EmlRung0AuditStatuses.Agreed:
                    _rung0AgreedAudits++;
                    break;
                case EmlRung0AuditStatuses.Disagreed:
                    _rung0DisagreedAudits++;
                    _rung0CompositionDigest ^= audit.ProofDigest;
                    break;
                case EmlRung0AuditStatuses.NotSelected:
                    _rung0NotSelectedAudits++;
                    break;
                default:
                    throw new InvalidDataException($"unknown ordinary rung-0 audit status {audit.Status}");
            }
        }
        if (rung0.Composition.Proof is EmlRung0Proof proof)
            _rung0CompositionDigest ^= proof.Digest;
    }

    private void ExecuteOrdinaryRelationNullAssay(
        in EmlObligationResolution obligation,
        string targetIdentity,
        List<EmlLawCandidateInstantiation> lawCandidates,
        EmlDeliberationLease? deliberationLease,
        List<CortexObservationField> fields)
    {
        if (lawCandidates.Count == 0) return;
        int consideredBefore = _relationNullPairsConsidered;
        int createdBefore = _relationNullPairsCreated;
        int rejectNoCarrierBefore = _relationNullRejectNoCarrier;
        int rejectShapeBefore = _relationNullRejectShape;
        int rejectGradeBefore = _relationNullRejectGrade;
        EmlObligationResolution obligationSnapshot = obligation;
        bool TryExecute(EmlLawCandidateInstantiation sourceCandidate, EmlLawRewrite donor, int sourceIndex, int donorIndex, EmlRelationNullDonorProvenance? donorProvenance = null)
        {
            EmlLawRewrite source = sourceCandidate.Rewrite;
            if (source.IsRelationNull || donor.IsRelationNull) return false;
            _relationNullPairsConsidered++;
            ulong salt = _seed ^ unchecked((ulong)(uint)obligationSnapshot.SourcePredictionID.Value << 32)
                ^ unchecked((ulong)(uint)sourceIndex << 16) ^ unchecked((ulong)(uint)donorIndex)
                ^ 0x4E554C4C52454C41UL;
            if (salt == 0 || !EmlLawRewrite.TryCreateRelationNull(in source, in donor, salt, new EmlGrader(), out EmlLawRewrite relationNull))
            {
                fields.Add(new CortexObservationField(
                    "relation-null-pair-rejected-detail",
                    DescribeRelationNullReject(in source, in donor, salt),
                    Blur.SlotSources.GrammarPrior));
                RecordRelationNullReject(in source, in donor, salt);
                return false;
            }
            _relationNullPairsCreated++;
            if (sourceCandidate.PredictionCarrier is not EmlRewritePredictionCarrier carrier)
            {
                _relationNullRejectNoCarrier++;
                EmlRung0FunnelReceipt noCarrierReceipt = new(
                    EmlRung0FunnelStages.RelationNull,
                    obligationSnapshot.SourcePredictionID,
                    targetIdentity,
                    relationNull.RuleID,
                    false,
                    "no-carrier",
                    "", "", "", "",
                    EmlEvaluatorInterval.EmptyAt(_sieve.EvaluatorClock.ProgramPointEvaluations),
                    donorProvenance ?? CreateRelationNullDonorProvenance(sourceCandidate, donor));
                RecordRung0Funnel(new[] { noCarrierReceipt }, fields);
                return false;
            }
            long evaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
            EmlRung0NullExecution execution = _lawStore.DeriveRung0Null(
                in carrier, sourceCandidate.Instantiation.LeftRpn, in relationNull,
                EmlRung0Budget.Default, deliberationLease);
            EmlRung0FunnelReceipt relationNullReceipt = new(
                EmlRung0FunnelStages.RelationNull,
                obligationSnapshot.SourcePredictionID,
                targetIdentity,
                execution.RuleID,
                execution.Powered,
                execution.Powered ? "powered" : "unpowered",
                "", "", "", "",
                _sieve.EvaluatorClock.MeasureFrom(evaluatorStart),
                donorProvenance ?? CreateRelationNullDonorProvenance(sourceCandidate, donor));
            RecordRung0Funnel(new[] { relationNullReceipt }, fields);
            _relationNullExecutions++;
            if (execution.Powered) _relationNullDivergences++;
            _relationNullAuthorityPredictions = checked(_relationNullAuthorityPredictions + execution.AuthoritativeCompositions);
            return true;
        }

        for (int sourceIndex = 0; sourceIndex < lawCandidates.Count; sourceIndex++)
        {
            for (int donorIndex = sourceIndex + 1; donorIndex < lawCandidates.Count; donorIndex++)
            {
                EmlLawCandidateInstantiation sourceCandidate = lawCandidates[sourceIndex];
                EmlLawCandidateInstantiation donorCandidate = lawCandidates[donorIndex];
                if (TryExecute(
                        sourceCandidate,
                        donorCandidate.Rewrite,
                        sourceIndex,
                        donorIndex,
                        CreateRelationNullDonorProvenance(donorCandidate, donorCandidate.Rewrite)))
                    goto RelationNullComplete;
            }
        }

        List<EmlRelationNullDonor> donorFrontier = new();
        _lawStore.AppendRelationNullDonorRewrites(_sieve, donorFrontier, deliberationLease);
        for (int sourceIndex = 0; sourceIndex < lawCandidates.Count; sourceIndex++)
        for (int donorIndex = 0; donorIndex < donorFrontier.Count; donorIndex++)
        {
            EmlLawCandidateInstantiation sourceCandidate = lawCandidates[sourceIndex];
            EmlRelationNullDonor donor = donorFrontier[donorIndex];
            if (TryExecute(sourceCandidate, donor.Rewrite, sourceIndex, donorIndex, donor.Provenance))
                goto RelationNullComplete;
        }

    RelationNullComplete:
        if (_relationNullPairsConsidered > consideredBefore)
            fields.Add(new CortexObservationField("relation-null-pair-considered", (_relationNullPairsConsidered - consideredBefore).ToString(), Blur.SlotSources.GrammarPrior));
        if (_relationNullPairsCreated > createdBefore)
            fields.Add(new CortexObservationField("relation-null-pair-created", (_relationNullPairsCreated - createdBefore).ToString(), Blur.SlotSources.GrammarPrior));
        int rejected = (_relationNullRejectNoCarrier - rejectNoCarrierBefore)
            + (_relationNullRejectShape - rejectShapeBefore)
            + (_relationNullRejectGrade - rejectGradeBefore);
        if (rejected > 0)
            fields.Add(new CortexObservationField("relation-null-pair-rejected", $"shape={_relationNullRejectShape - rejectShapeBefore};grade={_relationNullRejectGrade - rejectGradeBefore};no-carrier={_relationNullRejectNoCarrier - rejectNoCarrierBefore}", Blur.SlotSources.GrammarPrior));
    }

    private EmlRelationNullDonorProvenance CreateRelationNullDonorProvenance(
        EmlLawCandidateInstantiation sourceCandidate,
        EmlLawRewrite donor)
    {
        EmlObligationTarget address = sourceCandidate.Address;
        if (address.Species != EmlObligationTargetSpecies.ExactComposition
            || !_sieve.TryReadExactCompositionObligation(address.SourcePredictionID, out EmlExactCompositionObligation target))
            return new(address.SourcePredictionID, "", Array.Empty<TapeEventID>(), Array.Empty<string>());
        EmlPredictionID sourcePredictionID = address.SourcePredictionID;
        string admissionID = donor.RulePattern + "\u0001"
            + donor.LawProof.OccurrenceDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)
            + "\u0001" + donor.LawProof.OccurrenceCheckPrediction;
        return new(sourcePredictionID, _sieve.ExactCompositionObligationIdentity(sourcePredictionID), target.Supports, [admissionID]);
    }

    private void RecordRelationNullReject(in EmlLawRewrite source, in EmlLawRewrite donor, ulong salt)
    {
        if (salt == 0
            || source.RuleID.IsEmpty || donor.RuleID.IsEmpty
            || EmlRuleID.CreateRewriteInstance(in source) == EmlRuleID.CreateRewriteInstance(in donor)
            || !EmlRewriteSystem.ReducesRank(source.AntecedentRpn, source.ConsequentRpn))
        {
            _relationNullRejectShape++;
            return;
        }
        if (source.AntecedentSize != donor.AntecedentSize
            || source.ConsequentSize != donor.ConsequentSize
            || string.Equals(source.ConsequentRpn, donor.ConsequentRpn, StringComparison.Ordinal))
        {
            _relationNullRejectShape++;
            return;
        }
        _relationNullRejectGrade++;
    }

    private static string DescribeRelationNullReject(in EmlLawRewrite source, in EmlLawRewrite donor, ulong salt)
    {
        if (salt == 0) return "salt";
        if (source.RuleID.IsEmpty || donor.RuleID.IsEmpty) return "rule-id-empty";
        if (EmlRuleID.CreateRewriteInstance(in source) == EmlRuleID.CreateRewriteInstance(in donor)) return "same-rewrite-instance";
        if (!EmlRewriteSystem.ReducesRank(source.AntecedentRpn, source.ConsequentRpn)) return "source-rank";
        if (source.AntecedentSize != donor.AntecedentSize) return "antecedent-size";
        if (source.ConsequentSize != donor.ConsequentSize) return "consequent-size";
        if (string.Equals(source.ConsequentRpn, donor.ConsequentRpn, StringComparison.Ordinal)) return "same-consequent";
        EmlGrader grader = new();
        if (grader.GradeRpn(source.ConsequentRpn, donor.ConsequentRpn).Grade == 'E') return "consequent-grade";
        if (grader.GradeRpn(source.AntecedentRpn, donor.ConsequentRpn).Grade == 'E') return "cross-grade";
        return "unknown";
    }

    private bool TryResolveBoundTarget(
        List<CortexActionArgument> arguments,
        out EmlObligationTarget target,
        out EmlObligationResolution resolution,
        out EmlExactCompositionObligation exact)
    {
        target = default;
        resolution = default;
        exact = default;
        EmlObligationTargetSpecies species = EmlObligationTargetSpecies.Residual;
        bool speciesSpecified = false;
        if (TryReadArgument(arguments, "obligation-species", out string speciesText)
            || TryReadArgument(arguments, "target-species", out speciesText))
        {
            if (!Enum.TryParse(speciesText, ignoreCase: true, out species) || !Enum.IsDefined(species)) return false;
            speciesSpecified = true;
        }
        bool priorTargetBinding = arguments.Any(static argument =>
            argument.Source == Blur.SlotSources.PriorObservation
            && (argument.Slot is "obligation-claim-id" or "obligation-species" or "target-species"));
        if (!priorTargetBinding && TryReadArgument(arguments, "obligation-claim-id", out string claimIDText))
        {
            if (!int.TryParse(claimIDText, out int claimID)) return false;
            target = new(species, new EmlPredictionID(claimID));
            if (!speciesSpecified)
            {
                bool hasResidual = _sieve.TryResolveObligation(target.SourcePredictionID, out resolution);
                bool hasExact = _sieve.TryResolveExactCompositionObligation(target.SourcePredictionID, out exact);
                if (hasResidual == hasExact) return false;
                if (hasExact) target = EmlObligationTarget.ExactComposition(target.SourcePredictionID);
                if (hasResidual) return true;
                resolution = new EmlObligationResolution(
                    exact.SourcePredictionID, default, "exact-derivation", default, default, 0, exact.Supports, exact.MintEventID);
                return true;
            }
            if (species == EmlObligationTargetSpecies.Residual)
                return _sieve.TryResolveObligation(target.SourcePredictionID, out resolution);
            if (!_sieve.TryResolveExactCompositionObligation(target.SourcePredictionID, out exact)) return false;
            resolution = new EmlObligationResolution(
                exact.SourcePredictionID,
                default,
                "exact-derivation",
                default,
                default,
                0,
                exact.Supports,
                exact.MintEventID);
            return true;
        }
        if (_sieve.Obligations.Count == 0 && _sieve.ExactCompositionObligations.Count == 0)
        {
            return false;
        }
        // Without an explicit claim ID, select from the deterministic typed union. The
        // procedure's prior species slot is an observation, not a target lock; honoring
        // its stale Residual default would starve exact-derivation targets forever.
        {
            int targetCount = _sieve.Obligations.Count + _sieve.ExactCompositionObligations.Count;
            int selected = _holeCursor++ % targetCount;
            if (selected >= _sieve.Obligations.Count)
            {
                exact = _sieve.ExactCompositionObligations[selected - _sieve.Obligations.Count];
                target = EmlObligationTarget.ExactComposition(exact.SourcePredictionID);
                resolution = new EmlObligationResolution(exact.SourcePredictionID, default, "exact-derivation", default, default, 0, exact.Supports, exact.MintEventID);
                return true;
            }
            EmlObligation selectedObligation = _sieve.Obligations[selected];
            target = EmlObligationTarget.Residual(selectedObligation.SourcePredictionID);
            resolution = _sieve.ResolveObligation(selectedObligation.SourcePredictionID);
            return true;
        }
    }

    private void AppendHoleCandidates(List<EmlHoleCandidate> candidates, int maximum, EmlDeliberationLease? deliberationLease = null)
    {
        List<string> programs = new();
        _sieve.AppendCanonicalPrograms(programs, maximum);
        List<EmlHoleCandidate> ordered = new(programs.Count);
        for (int i = 0; i < programs.Count; i++)
            ordered.Add(new EmlHoleCandidate(programs[i], "canon", programs[i].Length));
        ordered.Sort(static (left, right) =>
        {
            int byCost = left.Cost.CompareTo(right.Cost);
            return byCost != 0 ? byCost : string.CompareOrdinal(left.Program, right.Program);
        });
        for (int i = 0; i < ordered.Count; i++)
        {
            deliberationLease?.ReserveCandidateSupplyItem();
            candidates.Add(ordered[i]);
        }
    }

    internal string TestCounterexample(List<CortexObservationField> fields)
    {
        RecordActionOffer(EmlActionArms.Counterexample);
        RefreshActionCandidates();
        if (!TryPickStressCandidate(UsesActionProcedure, out KeyValuePair<EmlCert, SemanticCASClass<string>> candidate,
                out EmlPredictionID obligationPredictionID))
        {
            fields.Add(new CortexObservationField("obligation-claim-id", "", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("shuffled-obligation-claim-id", "", Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("solve-hole-eligibility", "ineligible", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("probe-verdict", "held", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("futility-status", "all-cold", Blur.SlotSources.PriorObservation));
            _obligationSuppressedCalls++;
            return "stress skipped all-cold";
        }

        int candidateCount = UsesActionProcedure
            ? Math.Max(1, _sieve.Obligations.Count + _sieve.ExactCompositionObligations.Count)
            : Math.Max(1, _stressCandidates.Count);
        int probeIndex = (_stressCursor / candidateCount) & 1;
        _stressCursor++;
        int mintIndex = obligationPredictionID.Value >= 0 ? obligationPredictionID.Value : candidate.Value.FirstCapture;
        if (mintIndex < 0 || mintIndex >= _sieve.MintLog.Count) return "";
        EmlMint mint = _sieve.MintLog[mintIndex];
        fields.Add(new CortexObservationField("certificate", candidate.Key.Hex(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("claim-id", mintIndex.ToString(), Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("claim", mint.Line, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("grade", candidate.Key.Grade.ToString(), Blur.SlotSources.PriorObservation));
        EmlObligationTargetSpecies selectedSpecies = EmlObligationTargetSpecies.Residual;
        bool hasSelectedSpecies = obligationPredictionID.Value >= 0
            && _sieve.TryReadTargetIdentity(obligationPredictionID, out _, out selectedSpecies);
        if (hasSelectedSpecies)
        {
            fields.Add(new CortexObservationField("obligation-claim-id", obligationPredictionID.Value.ToString(), Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("obligation-species", selectedSpecies.ToString(), Blur.SlotSources.PriorObservation));
            EmlPredictionID shuffledObligation = GetShuffledObligationPredictionID(obligationPredictionID);
            fields.Add(new CortexObservationField("shuffled-obligation-claim-id", shuffledObligation.Value.ToString(), Blur.SlotSources.GrammarPrior));
        }
        EmlCert shuffled = _stressCandidates[_stressCursor % _stressCandidates.Count];
        int shuffledMintIndex = _sieve.Cas[shuffled].FirstCapture;
        if (shuffledMintIndex >= 0 && shuffledMintIndex < _sieve.MintLog.Count)
        {
            fields.Add(new CortexObservationField("shuffled-certificate", shuffled.Hex(), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("shuffled-claim-id", shuffledMintIndex.ToString(), Blur.SlotSources.GrammarPrior));
            fields.Add(new CortexObservationField("shuffled-claim", _sieve.MintLog[shuffledMintIndex].Line, Blur.SlotSources.GrammarPrior));
        }
        if (!EmlPrediction.TryParse(mint.Line, out EmlPrediction claim)) return "";

        (Complex X, Complex Y, string Label) probe = probeIndex == 0
            ? (new Complex(1.0 / 2.6854520010653064, 0), new Complex(0.5671432904097838, 0), "P4")
            : (new Complex(2 * Math.PI, 0), new Complex(Math.E * Math.E, 0), "P5");
        EmlProbeRead read = ProbePrediction(in claim, probe.X, probe.Y);
        fields.Add(new CortexObservationField("probe", probe.Label, Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("probe-x", probe.X.Real.ToString("G17"), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("probe-y", probe.Y.Real.ToString("G17"), Blur.SlotSources.GrammarPrior));
        fields.Add(new CortexObservationField("probe-valid", read.Valid ? "yes" : "no", Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("probe-verdict",
            !read.Valid ? "invalid" : read.Refuted ? "refuted" : "held", Blur.SlotSources.PriorObservation));
        bool solveHoleEligible = read.Valid
            && hasSelectedSpecies
            && (selectedSpecies == EmlObligationTargetSpecies.ExactComposition ? !read.Refuted : read.Refuted);
        fields.Add(new CortexObservationField("solve-hole-eligibility",
            solveHoleEligible ? "eligible" : "ineligible", Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("relative-residual", read.RelativeResidual.ToString("G17"), Blur.SlotSources.PriorObservation));
        if (candidate.Key.Grade == 'E')
        {
            _stressExactTests++;
            if (read.Refuted) _stressExactRefuted++;
        }
        else
        {
            _stressAsymptoticTests++;
            if (read.Refuted) _stressAsymptoticRefuted++;
            // The diagonal preserves the candidate and probe scale while removing x/y independence.
            EmlProbeRead control = ProbePrediction(in claim, probe.X, probe.X);
            _stressControlTests++;
            if (control.Refuted) _stressControlRefuted++;
            fields.Add(new CortexObservationField("control-verdict", control.Refuted ? "refuted" : "held", Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("control-residual", control.RelativeResidual.ToString("G17"), Blur.SlotSources.PriorObservation));
        }
        if (!read.Refuted) return $"stress {probe.Label} pass {candidate.Key.Hex()}";

        string key = candidate.Key.Hex() + ":" + probe.Label;
        if (_counterexamplesSeen.Add(key))
        {
            _counterexampleOrder.Add(key);
            _pendingCounterexample = $"COUNTEREXAMPLE cert={candidate.Key.Hex()} grade={candidate.Key.Grade} probe={probe.Label} rel={read.RelativeResidual:G17} claim={claim.Line}";
        }
        return $"stress {probe.Label} refuted {candidate.Key.Hex()}";
    }

    private bool TryPickStressCandidate(bool requireObligation,
        out KeyValuePair<EmlCert, SemanticCASClass<string>> candidate,
        out EmlPredictionID obligationPredictionID)
    {
        if (requireObligation)
        {
            int targetCount = _sieve.Obligations.Count + _sieve.ExactCompositionObligations.Count;
            if (targetCount == 0)
            {
                candidate = default;
                obligationPredictionID = new EmlPredictionID(-1);
                return false;
            }
            int start = _stressCursor % targetCount;
            for (int offset = 0; offset < targetCount; offset++)
            {
                int selected = (start + offset) % targetCount;
                EmlPredictionID sourcePredictionID;
                if (selected < _sieve.Obligations.Count)
                {
                    EmlObligation obligation = _sieve.Obligations[selected];
                    if (!CanSearchObligation(in obligation)) continue;
                    sourcePredictionID = obligation.SourcePredictionID;
                }
                else
                {
                    EmlExactCompositionObligation target = _sieve.ExactCompositionObligations[selected - _sieve.Obligations.Count];
                    if (!CanSearchTarget(target.SourcePredictionID)) continue;
                    sourcePredictionID = target.SourcePredictionID;
                }
                obligationPredictionID = sourcePredictionID;
                EmlCert certificate = _sieve.GetPredictionCertificate(obligationPredictionID);
                candidate = new KeyValuePair<EmlCert, SemanticCASClass<string>>(certificate, _sieve.Cas[certificate]);
                return true;
            }
            candidate = default;
            obligationPredictionID = new EmlPredictionID(-1);
            return false;
        }

        if (_stressCandidates.Count == 0)
        {
            candidate = default;
            obligationPredictionID = new EmlPredictionID(-1);
            return false;
        }
        EmlCert stressCert = _stressCandidates[_stressCursor % _stressCandidates.Count];
        candidate = new KeyValuePair<EmlCert, SemanticCASClass<string>>(stressCert, _sieve.Cas[stressCert]);
        EmlPredictionID stressSourcePredictionID = new(candidate.Value.FirstCapture);
        obligationPredictionID = _sieve.TryResolveObligation(stressSourcePredictionID, out EmlObligationResolution ignored)
            ? stressSourcePredictionID
            : new EmlPredictionID(-1);
        return true;
    }

    private EmlPredictionID GetShuffledObligationPredictionID(EmlPredictionID source)
    {
        IReadOnlyList<EmlObligation> obligations = _sieve.Obligations;
        if (obligations.Count < 2) return source;
        for (int i = 0; i < obligations.Count; i++)
        {
            if (obligations[i].SourcePredictionID != source) continue;
            return obligations[(i + 1) % obligations.Count].SourcePredictionID;
        }
        return source;
    }

    private bool CanSearchObligation(in EmlObligation obligation)
    {
        if (_sieve.IsObligationClosed(obligation.SourcePredictionID))
            return false;
        if (!_obligationSearch.TryGetValue(obligation.SourcePredictionID, out EmlObligationSearchState state)) return true;
        if (state.Epoch == _discoveryEpoch) return state.Attempts < UnchangedObligationAttemptLimit;
        if (state.Attempts >= UnchangedObligationAttemptLimit) _obligationSearchRevivals++;
        state.Epoch = _discoveryEpoch;
        state.Attempts = 0;
        _obligationSearch[obligation.SourcePredictionID] = state;
        return true;
    }

    private bool CanSearchTarget(EmlPredictionID sourcePredictionID)
    {
        if (_sieve.IsObligationClosed(sourcePredictionID)) return false;
        if (!_obligationSearch.TryGetValue(sourcePredictionID, out EmlObligationSearchState state)) return true;
        if (state.Epoch == _discoveryEpoch) return state.Attempts < UnchangedObligationAttemptLimit;
        if (state.Attempts >= UnchangedObligationAttemptLimit) _obligationSearchRevivals++;
        state.Epoch = _discoveryEpoch;
        state.Attempts = 0;
        _obligationSearch[sourcePredictionID] = state;
        return true;
    }

    private bool TryStartObligationSearch(in EmlObligationResolution obligation)
    {
        if (!_sieve.TryReadTargetIdentity(obligation.SourcePredictionID, out string identity, out _)) return false;
        EmlObligation source = new(obligation.SourcePredictionID, identity);
        if (!CanSearchObligation(in source)) return false;
        _obligationSearch.TryGetValue(obligation.SourcePredictionID, out EmlObligationSearchState state);
        state.Epoch = _discoveryEpoch;
        state.Attempts++;
        _obligationSearch[obligation.SourcePredictionID] = state;
        _obligationSearchAttempts++;
        return true;
    }

    private void CompleteObligationSearch(EmlPredictionID claimID, bool changed)
    {
        if (!changed && _obligationSearch.TryGetValue(claimID, out EmlObligationSearchState state)
            && state.Epoch == _discoveryEpoch && state.Attempts >= UnchangedObligationAttemptLimit)
            _obligationSearchSuppressions++;
    }

    internal string CompareHoleSolution(List<CortexActionArgument> arguments, List<CortexObservationField> fields)
    {
        RecordActionOffer(EmlActionArms.Compare);
        string obligationPredictionID = ReadArgument(arguments, "obligation-claim-id");
        string candidateProgram = ReadArgument(arguments, "candidate-program");
        string solveStatus = ReadArgument(arguments, "solve-status");
        string obligationSpecies = ReadArgument(arguments, "obligation-species");
        bool obligationMatch = int.TryParse(obligationPredictionID, out int claimID)
            && (string.Equals(obligationSpecies, EmlObligationTargetSpecies.ExactComposition.ToString(), StringComparison.OrdinalIgnoreCase)
                ? _sieve.TryResolveExactCompositionObligation(new EmlPredictionID(claimID), out _)
                : _sieve.TryResolveObligation(new EmlPredictionID(claimID), out _));
        fields.Add(new CortexObservationField("obligation-claim-id", obligationPredictionID, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("candidate-program", candidateProgram, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("solve-status", solveStatus, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("obligation-species", obligationSpecies, Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("obligation-match", obligationMatch ? "yes" : "no", Blur.SlotSources.PriorObservation));
        fields.Add(new CortexObservationField("new-delta", _procedureSolveDeltas.Count > 0 ? "yes" : "no", Blur.SlotSources.PriorObservation));
        string comparison = ResolveComparisonResult(_procedureSolveDeltas);
        fields.Add(new CortexObservationField("comparison", comparison, Blur.SlotSources.PriorObservation));
        for (int i = 0; i < _procedureSolveDeltas.Count; i++)
        {
            EmlCertificateDelta delta = _procedureSolveDeltas[i];
            fields.Add(new CortexObservationField("comparison-change",
                EmlActionSelectionTokens.GetCertificateChangeToken(delta.Change), Blur.SlotSources.PriorObservation));
            if (delta.Before.HasValue)
                fields.Add(new CortexObservationField("comparison-before", delta.Before.Value.Hex(), Blur.SlotSources.PriorObservation));
            if (delta.After.HasValue)
                fields.Add(new CortexObservationField("comparison-after", delta.After.Value.Hex(), Blur.SlotSources.PriorObservation));
        }
        return $"compare {comparison} obligation={obligationPredictionID} {candidateProgram}";
    }

    internal void AppendPendingDeltaFields(List<CortexObservationField> fields)
    {
        CollectPendingDeltas();
        for (int i = 0; i < _lastDeltas.Count; i++)
        {
            EmlCertificateDelta delta = _lastDeltas[i];
            fields.Add(new CortexObservationField("delta-change",
                EmlActionSelectionTokens.GetCertificateChangeToken(delta.Change), Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("delta-claim", delta.PredictionID.Value.ToString(), Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("delta-claim-line",
                _sieve.MintLog[delta.PredictionID.Value].Line, Blur.SlotSources.PriorObservation));
            if (delta.Before.HasValue)
                fields.Add(new CortexObservationField("delta-before-certificate", delta.Before.Value.Hex(), Blur.SlotSources.PriorObservation));
            if (delta.After.HasValue)
                fields.Add(new CortexObservationField("delta-after-certificate", delta.After.Value.Hex(), Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("delta-evaluator-calls", delta.EvaluatorCalls.ToString(), Blur.SlotSources.PriorObservation));
            fields.Add(new CortexObservationField("delta-description-bits", delta.DescriptionBits.ToString(), Blur.SlotSources.PriorObservation));
        }
        fields.Add(new CortexObservationField("canonical-deltas", _lastDeltas.Count.ToString(), Blur.SlotSources.PriorObservation));
    }

    private CortexProcedure CreateActionProcedure(EmlActionSelections selection)
    {
        if (_freezeAdaptiveFuel)
        {
            // Reflex is the fixed procedure arm: contact the world, then use the launchpad's fresh proposal.
            // Solve-hole and compare are adaptive deliberation operations and must not execute without a lease.
            return new CortexProcedure(
            [
                new CortexProcedureStep("counterexample", Array.Empty<CortexProcedureArgument>()),
                new CortexProcedureStep("fresh-bias", Array.Empty<CortexProcedureArgument>(),
                    new CortexProcedureGuard("probe-verdict", Blur.SlotSources.PriorObservation,
                        CortexProcedureComparisons.NotEqual, "refuted")),
            ]);
        }
        bool shuffledBindings = selection is EmlActionSelections.ProcedureShuffled or EmlActionSelections.ProcedureGuardShuffled;
        bool guarded = selection is EmlActionSelections.ProcedureGuarded or EmlActionSelections.ProcedureGuardShuffled;
        Blur.SlotSources bindingSource = shuffledBindings
            ? Blur.SlotSources.GrammarPrior
            : Blur.SlotSources.PriorObservation;
        string obligationChannel = shuffledBindings ? "shuffled-obligation-claim-id" : "obligation-claim-id";
        CortexProcedureArgument[] solveArguments =
        [
            new CortexProcedureArgument("obligation-claim-id", obligationChannel, bindingSource),
            new CortexProcedureArgument("obligation-species", "obligation-species", bindingSource),
        ];
        CortexProcedureArgument[] comparisonArguments =
        [
            new CortexProcedureArgument("obligation-claim-id", "obligation-claim-id", Blur.SlotSources.PriorObservation),
            new CortexProcedureArgument("obligation-species", "obligation-species", Blur.SlotSources.PriorObservation),
            new CortexProcedureArgument("candidate-program", "candidate-program", Blur.SlotSources.PriorObservation),
            new CortexProcedureArgument("solve-status", "solve-status", Blur.SlotSources.PriorObservation),
        ];
        if (!guarded)
        {
            return new CortexProcedure(
            [
                new CortexProcedureStep("counterexample", Array.Empty<CortexProcedureArgument>()),
                new CortexProcedureStep("solve-hole", solveArguments),
                new CortexProcedureStep("compare", comparisonArguments),
            ]);
        }

        CortexProcedureGuard solveHoleEligibilityGuard = new("solve-hole-eligibility", Blur.SlotSources.PriorObservation,
            CortexProcedureComparisons.Equal, "eligible", ConsumeInput: false);
        CortexProcedureGuard frontierGuard = new("probe-verdict", Blur.SlotSources.PriorObservation,
            CortexProcedureComparisons.Equal, "held");
        CortexProcedureStep[] steps =
        [
            new CortexProcedureStep("counterexample", Array.Empty<CortexProcedureArgument>()),
            new CortexProcedureStep("solve-hole", solveArguments, solveHoleEligibilityGuard),
            new CortexProcedureStep("compare", comparisonArguments, solveHoleEligibilityGuard),
            new CortexProcedureStep("fresh-bias", Array.Empty<CortexProcedureArgument>(), frontierGuard),
        ];
        return new CortexProcedure(steps);
    }

    private static EmlActionArms ParseProcedureArm(string tool) => tool switch
    {
        "fresh-bias" => EmlActionArms.FreshBias,
        "counterexample" => EmlActionArms.Counterexample,
        "solve-hole" => EmlActionArms.SolveHole,
        "compare" => EmlActionArms.Compare,
        _ => throw new InvalidDataException($"unknown EML procedure tool '{tool}'"),
    };

    private static bool ContainsGuardArgument(List<CortexActionArgument> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
            if (arguments[i].Slot.StartsWith("guard:", StringComparison.Ordinal)) return true;
        return false;
    }

    private void RecordCompletedProcedure()
    {
        _proceduresCompleted++;
        _actionProcedure = null;
    }

    private static string ReadArgument(List<CortexActionArgument> arguments, string slot)
    {
        for (int i = 0; i < arguments.Count; i++)
            if (arguments[i].Slot == slot) return arguments[i].Value;
        return "";
    }

    private static bool TryReadArgument(List<CortexActionArgument> arguments, string slot, out string value)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Slot != slot) continue;
            value = arguments[i].Value;
            return true;
        }
        value = "";
        return false;
    }

    private static string ReadField(List<CortexObservationField> fields, string slot)
    {
        for (int i = 0; i < fields.Count; i++)
            if (fields[i].Slot == slot) return fields[i].Value;
        return "";
    }

    private static string ResolveComparisonResult(List<EmlCertificateDelta> deltas)
    {
        for (int i = 0; i < deltas.Count; i++)
            if (deltas[i].Change == EmlCertificateChanges.ClassOpened) return "opened";
        for (int i = 0; i < deltas.Count; i++)
            if (deltas[i].Change == EmlCertificateChanges.RepresentativeImproved) return "improved";
        return deltas.Count > 0 ? "changed" : "unchanged";
    }

    internal void CloseActionBatch()
    {
        double batchYield = _actionBatchHadCanonicalDelta ? 1 : 0;
        _intrinsicFrontierResidual += (1.0 / 8) * (batchYield - _intrinsicFrontierResidual);
        if (_actionBatchHadCanonicalDelta) _discoveryEpoch++;
        _actionBatchHadCanonicalDelta = false;
    }

    internal void RecordExecutionAdmission(bool admitted)
    {
        if (admitted) _executionAdmissions++;
        else _executionAffirmSkips++;
    }

    internal OutcomeArmState ReadActionArm(EmlActionArms arm)
        => UsesActionProcedure || arm == EmlActionArms.Compare
            ? new OutcomeArmState(double.NaN, _actionOffers[(int)arm], _actionOffers[(int)arm], _actionOffers[(int)arm])
            : _actionMeter?.Read(arm) ?? new OutcomeArmState(double.NaN, 0, 0, 0);

    internal string ActionReport()
    {
        StringBuilder report = new();
        string globalYield = double.IsNaN(_actionGlobalYield) ? "nan" : _actionGlobalYield.ToString("F6");
        report.AppendLine($"EML actions · {_actionSelection} · decisions {_actionDecision} · legacy_fallbacks {_actionFallbacks} · global_yield {globalYield}");
        report.Append("selection_causes")
              .Append("\tfixed_schedule=").Append(_actionSelectionCauses[(int)EmlActionSelectionCauses.FixedSchedule])
              .Append("\tgrammar=").Append(_actionSelectionCauses[(int)EmlActionSelectionCauses.Grammar])
              .Append("\tabstention=").Append(_actionSelectionCauses[(int)EmlActionSelectionCauses.Abstention]).AppendLine();
        report.Append("adaptive_operations\treflex=").Append(_reflexAdaptiveOperations).AppendLine();
        EmlEvaluatorClock clock = _sieve.EvaluatorClock;
        report.Append("evaluator\thistory_complete=").Append(clock.HistoryComplete ? "yes" : "no")
              .Append("\ttotal=").Append(clock.ProgramPointEvaluations)
              .Append("\toffer_requests=").Append(clock.OfferRequests)
              .Append("\toffer_calls=").Append(clock.OfferProgramPointEvaluations)
              .Append("\tladder_requests=").Append(clock.LadderRequests)
              .Append("\tcache_hits=").Append(clock.LadderCacheHits)
              .Append("\tcache_misses=").Append(clock.LadderCacheMisses)
              .Append("\tladder_calls=").Append(clock.LadderProgramPointEvaluations)
              .Append("\tladder_executed_calls=").Append(clock.ExecutedLadderProgramPointEvaluations)
              .Append("\tood_probe_calls=").Append(clock.OutOfDistributionProbeCalls)
              .Append("\tinverse_transforms=").Append(clock.InverseTransforms)
              .Append("\thash_probes=").Append(clock.HashProbes)
              .Append("\toffered_join_hits=").Append(clock.OfferedJoinHits).AppendLine();
        report.AppendLine("arm\tselections\teligible\toutcomes\tyield_ema\toffers\tevaluator_calls\tfirst_captures\tdelta_outcomes");
        for (int i = 0; i < ReportArmOrder.Length; i++)
        {
            EmlActionArms arm = ReportArmOrder[i];
            OutcomeArmState state = ReadActionArm(arm);
            report.Append(EmlActionSelectionTokens.ActionToken(arm)).Append('\t')
                  .Append(state.Decisive).Append('\t').Append(state.Fires).Append('\t').Append(state.Outcomes).Append('\t')
                  .Append(double.IsNaN(state.YieldEma) ? "nan" : state.YieldEma.ToString("F6")).Append('\t')
                  .Append(_actionOffers[(int)arm]).Append('\t').Append(_actionEvaluatorCalls[(int)arm]).Append('\t')
                  .Append(_actionFirstCaptures[(int)arm]).Append('\t')
                  .Append(_actionDeltaOutcomes[(int)arm].ToString("F1")).AppendLine();
        }
        report.AppendLine($"stress\texact_refuted={_stressExactRefuted}/{_stressExactTests}\tasymptotic_refuted={_stressAsymptoticRefuted}/{_stressAsymptoticTests}\tshuffled_point_refuted={_stressControlRefuted}/{_stressControlTests}\tunique={_counterexamplesSeen.Count}");
        report.AppendLine($"procedure\tvariant={EmlActionSelectionTokens.CurriculumToken(_actionSelection)}\tstarted={_proceduresStarted}\tcompleted={_proceduresCompleted}\tbound={_procedureBindings}\tshuffled={_procedureShuffledBindings}\tguards_passed={_procedureGuardsPassed}\tguards_skipped={_procedureGuardsSkipped}\tguards_abstained={_procedureGuardsAbstained}\tobligation_match={_procedureObligationMatches}\tnew_delta={_procedureNewDeltas}\tcanonical_deltas={_procedureCanonicalDeltas}");
        report.AppendLine($"frontier\tresidual={_intrinsicFrontierResidual:F6}\tepoch={_discoveryEpoch}\tfirst_generative_step={_firstGenerativeStep}\tfirst_generative_decision={_firstGenerativeDecision}");
        report.AppendLine($"futility\tlimit={UnchangedObligationAttemptLimit}\tattempts={_obligationSearchAttempts}\tsuppressions={_obligationSearchSuppressions}\trevivals={_obligationSearchRevivals}\tresolved={CountResolvedObligations()}\tsuppressed_calls={_obligationSuppressedCalls}\tcold={CountColdObligations()}");
        report.AppendLine($"execution\tadmitted={_executionAdmissions}\taffirm_skips={_executionAffirmSkips}");
        report.AppendLine($"hypothesis_admission\tcap_skips={_hypothesisCapSkips}");
        long processProofFuel = 0;
        int structuralProcessProofs = 0;
        for (int i = 0; i < _sieve.ProcessResidualProofs.Count; i++)
        {
            processProofFuel = checked(processProofFuel + _sieve.ProcessResidualProofs[i].ProcessFuel);
            if (_sieve.ProcessResidualProofs[i].CompositionLaw is not null) structuralProcessProofs++;
        }
        report.AppendLine($"process_residuals\tproofs={_sieve.ProcessResidualProofs.Count}\tstructural={structuralProcessProofs}\tfuel={processProofFuel}");
        return report.ToString();
    }

    internal string ObligationReport()
    {
        StringBuilder report = new("species\tclaim_id\tclosures\tlabel\tp1_re\tp1_im\tp2_re\tp2_im\tp3_re\tp3_im\tclaim\tmath\n");
        for (int i = 0; i < _sieve.Obligations.Count; i++)
        {
            EmlObligation obligation = _sieve.Obligations[i];
            EmlObligationResolution resolution = _sieve.ResolveObligation(obligation.SourcePredictionID);
            EmlResidualWitness witness = resolution.Corroboration;
            string claim = _sieve.MintLog[obligation.SourcePredictionID.Value].Line;
            string math = EmlPrediction.TryParse(claim, out EmlPrediction parsed)
                ? EmlRender.Render(parsed.Lhs) + (parsed.Tilde ? " ~ " : " = ")
                    + (parsed.RhsRpn ? EmlRender.Render(parsed.Rhs) : parsed.Rhs)
                : "";
            report.Append(EmlObligationTargetSpecies.Residual).Append('\t')
                .Append(obligation.SourcePredictionID.Value).Append('\t')
                .Append(resolution.ClosureCount).Append('\t')
                .Append(resolution.Label).Append('\t')
                .Append(witness.P1.Value.Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(witness.P1.Value.Imaginary.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(witness.P2.Value.Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(witness.P2.Value.Imaginary.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(witness.P3.Value.Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(witness.P3.Value.Imaginary.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(claim).Append('\t').Append(math).AppendLine();
        }
        for (int i = 0; i < _sieve.ExactCompositionObligations.Count; i++)
        {
            EmlExactCompositionObligation target = _sieve.ExactCompositionObligations[i];
            string claim = _sieve.MintLog[target.SourcePredictionID.Value].Line;
            report.Append(EmlObligationTargetSpecies.ExactComposition).Append('\t')
                .Append(target.SourcePredictionID.Value).Append('\t')
                .Append(_sieve.ClosureCount(target.SourcePredictionID)).Append('\t')
                .Append(target.CarrierRPN).Append("\t0\t0\t0\t0\t0\t0\t0\t0\t")
                .Append(claim).Append("\t").Append(claim).AppendLine();
        }
        return report.ToString();
    }

    private void PostActionWorkspace(CogitoWorkspace workspace)
    {
        workspace.Post("eml.actions.decisions", _actionDecision);
        workspace.Post("eml.actions.fallbacks", _actionFallbacks);
        workspace.Post("eml.actions.global_yield", _actionGlobalYield);
        workspace.Post("eml.actions.global_outcomes", _actionGlobalOutcomes);
        workspace.Post("eml.actions.causes.fixed_schedule", _actionSelectionCauses[(int)EmlActionSelectionCauses.FixedSchedule]);
        workspace.Post("eml.actions.causes.grammar", _actionSelectionCauses[(int)EmlActionSelectionCauses.Grammar]);
        workspace.Post("eml.actions.causes.abstention", _actionSelectionCauses[(int)EmlActionSelectionCauses.Abstention]);
        PostArm(EmlActionArms.FreshBias, "fresh_bias");
        PostArm(EmlActionArms.FreshEnum, "fresh_enum");
        PostArm(EmlActionArms.SolveHole, "solve_hole");
        PostArm(EmlActionArms.Counterexample, "counterexample");
        PostArm(EmlActionArms.Compare, "compare");
        workspace.Post("eml.procedure.started", _proceduresStarted);
        workspace.Post("eml.procedure.completed", _proceduresCompleted);
        workspace.Post("eml.procedure.bound", _procedureBindings);
        workspace.Post("eml.procedure.shuffled", _procedureShuffledBindings);
        workspace.Post("eml.procedure.obligation_match", _procedureObligationMatches);
        workspace.Post("eml.procedure.new_delta", _procedureNewDeltas);
        workspace.Post("eml.procedure.canonical_deltas", _procedureCanonicalDeltas);
        workspace.Post("eml.procedure.variant", EmlActionSelectionTokens.CurriculumToken(_actionSelection));
        workspace.Post("eml.procedure.guards_passed", _procedureGuardsPassed);
        workspace.Post("eml.procedure.guards_skipped", _procedureGuardsSkipped);
        workspace.Post("eml.procedure.guards_abstained", _procedureGuardsAbstained);
        workspace.Post("eml.frontier.residual", _intrinsicFrontierResidual);
        workspace.Post("eml.frontier.epoch", _discoveryEpoch);
        workspace.Post("eml.frontier.first_generative_step", _firstGenerativeStep);
        workspace.Post("eml.frontier.first_generative_decision", _firstGenerativeDecision);
        workspace.Post("eml.futility.attempts", _obligationSearchAttempts);
        workspace.Post("eml.futility.suppressions", _obligationSearchSuppressions);
        workspace.Post("eml.futility.revivals", _obligationSearchRevivals);
        workspace.Post("eml.futility.resolved", CountResolvedObligations());
        workspace.Post("eml.futility.suppressed_calls", _obligationSuppressedCalls);
        workspace.Post("eml.futility.cold", CountColdObligations());
        workspace.Post("eml.execution.admitted", _executionAdmissions);
        workspace.Post("eml.execution.affirm_skips", _executionAffirmSkips);
        workspace.Post("eml.hypothesis.cap_skips", _hypothesisCapSkips);

        void PostArm(EmlActionArms arm, string key)
        {
            OutcomeArmState state = ReadActionArm(arm);
            workspace.Post($"eml.actions.{key}.selections", state.Decisive);
            workspace.Post($"eml.actions.{key}.yield", state.YieldEma);
            workspace.Post($"eml.actions.{key}.evaluator_calls", _actionEvaluatorCalls[(int)arm]);
        }
    }

    private int CountColdObligations()
    {
        int cold = 0;
        IReadOnlyList<EmlObligation> obligations = _sieve.Obligations;
        for (int i = 0; i < obligations.Count; i++)
        {
            EmlObligation obligation = obligations[i];
            if (_sieve.IsObligationClosed(obligation.SourcePredictionID)) continue;
            if (_obligationSearch.TryGetValue(obligation.SourcePredictionID, out EmlObligationSearchState state)
                && state.Epoch == _discoveryEpoch && state.Attempts >= UnchangedObligationAttemptLimit)
                cold++;
        }
        IReadOnlyList<EmlExactCompositionObligation> exactTargets = _sieve.ExactCompositionObligations;
        for (int i = 0; i < exactTargets.Count; i++)
        {
            EmlPredictionID claimID = exactTargets[i].SourcePredictionID;
            if (_sieve.IsObligationClosed(claimID)) continue;
            if (_obligationSearch.TryGetValue(claimID, out EmlObligationSearchState state)
                && state.Epoch == _discoveryEpoch && state.Attempts >= UnchangedObligationAttemptLimit)
                cold++;
        }
        return cold;
    }

    private int CountResolvedObligations()
    {
        int resolved = 0;
        IReadOnlyList<EmlObligation> obligations = _sieve.Obligations;
        for (int i = 0; i < obligations.Count; i++)
            if (_sieve.IsObligationClosed(obligations[i].SourcePredictionID)) resolved++;
        IReadOnlyList<EmlExactCompositionObligation> exactTargets = _sieve.ExactCompositionObligations;
        for (int i = 0; i < exactTargets.Count; i++)
            if (_sieve.IsObligationClosed(exactTargets[i].SourcePredictionID)) resolved++;
        return resolved;
    }

    internal void SaveActionState(CkptWriter writer)
    {
        if (_actionSelection == EmlActionSelections.Off) return;
        if (_actionMeter is null) throw new InvalidOperationException("armed EML action state has no outcome meter");
        if (_actionInFlight || _actionMeter.PendingIndex >= 0 || _sharedActionOutcomePending)
            throw new InvalidOperationException("EML action checkpoints are valid only between completed action slots");
        writer.Section(ActionTag);
        writer.U8((byte)_actionSelection);
        writer.U64(_actionRng);
        writer.I32(_actionDecision);
        writer.I32(_roundRobinCursor);
        writer.I32(_actionEnumTaken);
        writer.I32(_actionEnumRuler);
        writer.Bool(_actionEnumDone);
        _actionMeter.SaveArmState(writer);
        writer.U8((byte)_currentActionArm);
        for (int i = 0; i < ReportArmOrder.Length; i++)
        {
            writer.I32(_actionOffers[i]);
            writer.I64(_actionEvaluatorCalls[i]);
            writer.I32(_actionFirstCaptures[i]);
            writer.F64(_actionDeltaOutcomes[i]);
        }
        writer.I32(_stressCursor);
        writer.I32(_stressExactTests); writer.I32(_stressExactRefuted);
        writer.I32(_stressAsymptoticTests); writer.I32(_stressAsymptoticRefuted);
        writer.I32(_stressControlTests); writer.I32(_stressControlRefuted);
        List<string> counterexamples = _counterexamplesSeen.OrderBy(static value => value, StringComparer.Ordinal).ToList();
        writer.I32(counterexamples.Count);
        for (int i = 0; i < counterexamples.Count; i++) writer.Str(counterexamples[i]);
        writer.Bool(_pendingCounterexample is not null);
        if (_pendingCounterexample is not null) writer.Str(_pendingCounterexample);
        for (int i = 0; i < _actionSelectionCauses.Length; i++) writer.I32(_actionSelectionCauses[i]);
        writer.I32(_actionFallbacks);
        writer.F64(_actionGlobalYield);
        writer.I32(_actionGlobalOutcomes);
        writer.I64(_sharedCanonicalDeltas);
        writer.I64(_sharedFirstCaptures);
        writer.Bool(_actionProcedure is not null);
        _actionProcedure?.Save(writer);
        writer.I32(_procedureSolveDeltas.Count);
        for (int i = 0; i < _procedureSolveDeltas.Count; i++)
            SaveCertificateDelta(writer, _procedureSolveDeltas[i]);
        writer.I32(_proceduresStarted);
        writer.I32(_proceduresCompleted);
        writer.I32(_procedureBindings);
        writer.I32(_procedureShuffledBindings);
        writer.I32(_procedureObligationMatches);
        writer.I32(_procedureNewDeltas);
        writer.I32(_procedureGuardsPassed);
        writer.I32(_procedureGuardsSkipped);
        writer.I32(_procedureGuardsAbstained);
        writer.I32(_procedureCanonicalDeltas);
        writer.I32(_holeCursor);
        writer.F64(_intrinsicFrontierResidual);
        writer.Bool(_actionBatchHadCanonicalDelta);
        writer.I32(_discoveryEpoch);
        writer.I32(_obligationSearchAttempts);
        writer.I32(_obligationSearchSuppressions);
        writer.I32(_obligationSearchRevivals);
        writer.I32(_obligationSuppressedCalls);
        writer.I32(_executionAdmissions);
        writer.I32(_executionAffirmSkips);
        writer.I32(_hypothesisCapSkips);
        writer.I32(_firstGenerativeDecision);
        writer.I32(_firstGenerativeStep);
        List<KeyValuePair<EmlPredictionID, EmlObligationSearchState>> searches =
            _obligationSearch.OrderBy(static pair => pair.Key.Value).ToList();
        writer.I32(searches.Count);
        for (int i = 0; i < searches.Count; i++)
        {
            writer.I32(searches[i].Key.Value);
            writer.I32(searches[i].Value.Epoch);
            writer.I32(searches[i].Value.Attempts);
        }
    }

    internal void LoadActionState(CkptReader reader)
    {
        if (_actionSelection == EmlActionSelections.Off) return;
        if (_actionMeter is null) throw new InvalidOperationException("armed EML action state has no outcome meter");
        _actionInFlight = false;
        reader.Expect(ActionTag);
        EmlActionSelections savedSelection = (EmlActionSelections)reader.U8();
        if (savedSelection != _actionSelection)
            throw new InvalidDataException($"EML action checkpoint mode drifted ({savedSelection} != {_actionSelection})");
        _actionRng = reader.U64();
        _actionDecision = reader.I32();
        _roundRobinCursor = reader.I32();
        int taken = reader.I32();
        int ruler = reader.I32();
        bool done = reader.Bool();
        _actionMeter.LoadArmState(reader);
        _currentActionArm = (EmlActionArms)reader.U8();
        for (int i = 0; i < ReportArmOrder.Length; i++)
        {
            _actionOffers[i] = reader.I32();
            _actionEvaluatorCalls[i] = reader.I64();
            _actionFirstCaptures[i] = reader.I32();
            _actionDeltaOutcomes[i] = reader.F64();
        }
        _stressCursor = reader.I32();
        _stressExactTests = reader.I32(); _stressExactRefuted = reader.I32();
        _stressAsymptoticTests = reader.I32(); _stressAsymptoticRefuted = reader.I32();
        _stressControlTests = reader.I32(); _stressControlRefuted = reader.I32();
        _counterexamplesSeen.Clear();
        _counterexampleOrder.Clear();
        int counterexampleCount = reader.I32();
        for (int i = 0; i < counterexampleCount; i++)
        {
            string counterexample = reader.Str();
            if (_counterexamplesSeen.Add(counterexample)) _counterexampleOrder.Add(counterexample);
        }
        _checkpointCounterexampleCount = 0;
        _pendingCounterexample = reader.Bool() ? reader.Str() : null;
        for (int i = 0; i < _actionSelectionCauses.Length; i++) _actionSelectionCauses[i] = reader.I32();
        _actionFallbacks = reader.I32();
        _actionGlobalYield = reader.F64();
        _actionGlobalOutcomes = reader.I32();
        _sharedCanonicalDeltas = reader.I64();
        _sharedFirstCaptures = reader.I64();
        _actionProcedure = reader.Bool() ? CortexProcedure.Load(reader) : null;
        _procedureSolveDeltas.Clear();
        int solveDeltaCount = reader.I32();
        for (int i = 0; i < solveDeltaCount; i++)
            _procedureSolveDeltas.Add(LoadCertificateDelta(reader));
        _checkpointProcedureSolveDeltaCount = _procedureSolveDeltas.Count;
        _proceduresStarted = reader.I32();
        _proceduresCompleted = reader.I32();
        _procedureBindings = reader.I32();
        _procedureShuffledBindings = reader.I32();
        _procedureObligationMatches = reader.I32();
        _procedureNewDeltas = reader.I32();
        _procedureGuardsPassed = reader.I32();
        _procedureGuardsSkipped = reader.I32();
        _procedureGuardsAbstained = reader.I32();
        _procedureCanonicalDeltas = reader.I32();
        _holeCursor = reader.I32();
        _intrinsicFrontierResidual = reader.F64();
        _actionBatchHadCanonicalDelta = reader.Bool();
        _discoveryEpoch = reader.I32();
        _obligationSearchAttempts = reader.I32();
        _obligationSearchSuppressions = reader.I32();
        _obligationSearchRevivals = reader.I32();
        _obligationSuppressedCalls = reader.I32();
        _executionAdmissions = reader.I32();
        _executionAffirmSkips = reader.I32();
        _hypothesisCapSkips = reader.I32();
        _firstGenerativeDecision = reader.I32();
        _firstGenerativeStep = reader.I32();
        _obligationSearch.Clear();
        int searchCount = reader.I32();
        if (searchCount < 0 || searchCount > 1_000_000)
            throw new InvalidDataException($"invalid EML obligation-search count {searchCount}");
        for (int i = 0; i < searchCount; i++)
        {
            EmlPredictionID claimID = new(reader.I32());
            EmlObligationSearchState state = new() { Epoch = reader.I32(), Attempts = reader.I32() };
            if (!_obligationSearch.TryAdd(claimID, state))
                throw new InvalidDataException($"duplicate EML obligation-search claim {claimID.Value}");
        }
        RebuildActionEnumeration(ruler, taken, done);
        _actionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        _accretionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
    }

    private static void SaveCertificateDelta(CkptWriter writer, in EmlCertificateDelta delta)
    {
        writer.U8((byte)delta.Change);
        writer.I32(delta.PredictionID.Value);
        SaveOptionalCertificate(writer, delta.Before);
        SaveOptionalCertificate(writer, delta.After);
        writer.I64(delta.Evaluation.Start);
        writer.I64(delta.Evaluation.End);
        writer.I32(delta.DescriptionBits);
    }

    private static EmlCertificateDelta LoadCertificateDelta(CkptReader reader)
    {
        EmlCertificateChanges change = (EmlCertificateChanges)reader.U8();
        EmlPredictionID claimID = new(reader.I32());
        EmlCert? before = LoadOptionalCertificate(reader);
        EmlCert? after = LoadOptionalCertificate(reader);
        EmlEvaluatorInterval evaluation = new(reader.I64(), reader.I64());
        return new EmlCertificateDelta(change, claimID, before, after, evaluation, reader.I32());
    }

    private static void SaveOptionalCertificate(CkptWriter writer, EmlCert? certificate)
    {
        writer.Bool(certificate.HasValue);
        if (!certificate.HasValue) return;
        EmlCert value = certificate.Value;
        writer.I32(value.Grade);
        writer.I64(value.Limit.R1); writer.I64(value.Limit.I1);
        writer.I64(value.Limit.R2); writer.I64(value.Limit.I2);
        writer.I64(value.RateRe); writer.I64(value.RateIm);
    }

    private static EmlCert? LoadOptionalCertificate(CkptReader reader)
    {
        if (!reader.Bool()) return null;
        char grade = (char)reader.I32();
        EmlSig limit = new(reader.I64(), reader.I64(), reader.I64(), reader.I64());
        return new EmlCert(grade, limit, reader.I64(), reader.I64());
    }

    private void EnsureActionEnumeration()
    {
        if (_actionEnumRuler == _sampler.MaxLen) return;
        RebuildActionEnumeration(_sampler.MaxLen, _actionEnumTaken, done: false);
    }

    private void RebuildActionEnumeration(int ruler, int taken, bool done)
    {
        _actionEnumRuler = Math.Max(ruler, _seedK + 2);
        _actionEnumTaken = taken;
        _actionEnumDone = done;
        _actionEnum = EmlGen.Enumerate(_seedK + 2, _actionEnumRuler).GetEnumerator();
        for (int i = 0; i < taken; i++)
            if (!_actionEnum.MoveNext()) throw new InvalidDataException($"EML action enumeration cursor {taken} exceeds ruler {_actionEnumRuler}");
    }

    private static EmlActionArms[] BuildShuffledOrder(ulong seed)
    {
        EmlActionArms[] order = (EmlActionArms[])ActionArmOrder.Clone();
        ulong rng = seed ^ 0xA0761D6478BD642FUL;
        for (int i = order.Length - 1; i > 0; i--)
        {
            rng = EmlGen.Lcg(rng);
            int j = (int)((rng >> 33) % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    private void RecordActionOffer(EmlActionArms arm) => _actionOffers[(int)arm]++;

    private void RefreshActionCandidates()
    {
        int mintCount = _sieve.MintLog.Count;
        if (_candidateCacheMintCount == mintCount) return;
        if (_candidateCacheMintCount > mintCount)
        {
            // The mint journal rewound (speculative rollback / checkpoint restore) — replay from zero.
            _candidateCacheMintCount = 0;
            _stressCandidates.Clear();
            _stressCandidateSet.Clear();
        }
        for (int i = _candidateCacheMintCount; i < mintCount; i++)
        {
            EmlCert certificate = _sieve.MintCert(i);
            if (certificate.Grade is not ('E' or 'A') || !_stressCandidateSet.Add(certificate)) continue;
            int index = _stressCandidates.BinarySearch(certificate, StressCandidateOrder.Instance);
            _stressCandidates.Insert(index < 0 ? ~index : index, certificate);
        }
        _candidateCacheMintCount = mintCount;
    }

    private sealed class StressCandidateOrder : IComparer<EmlCert>
    {
        public static readonly StressCandidateOrder Instance = new();

        public int Compare(EmlCert left, EmlCert right)
        {
            int byRank = Rank(left).CompareTo(Rank(right));
            return byRank != 0 ? byRank : left.HashKey().CompareTo(right.HashKey());
        }

        private static int Rank(EmlCert certificate)
            => certificate.Grade == 'A' && certificate.RateRe == EmlCert.SubResolution ? 0 : certificate.Grade == 'A' ? 1 : 2;
    }

    private EmlProbeRead ProbePrediction(in EmlPrediction claim, Complex x, Complex y)
    {
        _sieve.EvaluatorClock.RecordOutOfDistributionProbeCall();
        EmlLadder left = Eml.EvalLadder(claim.Lhs, x, y);
        EmlLadder right;
        if (claim.RhsRpn)
        {
            _sieve.EvaluatorClock.RecordOutOfDistributionProbeCall();
            right = Eml.EvalLadder(claim.Rhs, x, y);
        }
        else if (EmlGrader.TryParseAnomalyLabel(claim.Rhs, out Complex anomaly)) right = EmlGrader.ReferenceLadder(anomaly);
        else
        {
            _referenceChart ??= EmlSieve.LabelChart();
            if (!_referenceChart.TryGetValue(claim.Rhs, out Func<Complex, Complex, Complex>? reference))
                return EmlProbeRead.Invalid;
            right = EmlGrader.ReferenceLadder(reference(x, y));
        }
        if (!left.Plain.Finite || !right.Plain.Finite) return EmlProbeRead.Invalid;
        EmlGrader.Encl enclosure = EmlGrader.EnclAt(left, right);
        bool q9 = Eml.AgreeSig(left.Plain.Value, right.Plain.Value, 9);
        bool q12 = Eml.AgreeSig(left.Plain.Value, right.Plain.Value, 12);
        bool refuted = enclosure == EmlGrader.Encl.Excludes || (enclosure == EmlGrader.Encl.Undecided && !q9 && !q12);
        return new EmlProbeRead(true, refuted, EmlGrader.RelResid(left, right));
    }

    private readonly record struct EmlProbeRead(bool Valid, bool Refuted, double RelativeResidual)
    {
        public static readonly EmlProbeRead Invalid = new(false, false, double.NaN);
    }
}

internal abstract class EmlTool(EmlActionArms arm, Blur.SlotSources source) : CortexTool
{
    public EmlActionArms Arm => arm;
    public Blur.SlotSources Source => source;
    public override string Name => EmlActionSelectionTokens.ActionToken(arm);

    public override bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action)
    {
        arguments.Clear();
        action = CortexAction.None;
        return false;
    }

    protected static ReplayCalc ResolveReplay(Cortex cortex)
        => cortex.ActiveCurriculum as ReplayCalc
        ?? throw new InvalidOperationException($"EML tool mounted over {cortex.ActiveCurriculum.GetType().Name}, expected ReplayCalc");
}

internal sealed class FreshBiasTool : EmlTool
{
    public FreshBiasTool() : base(EmlActionArms.FreshBias, Blur.SlotSources.GrammarPrior) { }
    public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        List<CortexObservationField> fields)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        string program = dream.OfferFreshBias(cortex.Grammar);
        fields.Add(new CortexObservationField("candidate-program", program, Blur.SlotSources.GrammarPrior));
        dream.AppendPendingDeltaFields(fields);
        return new CortexObservation(program, false);
    }
}

internal sealed class FreshEnumTool : EmlTool
{
    public FreshEnumTool() : base(EmlActionArms.FreshEnum, Blur.SlotSources.Unknown) { }
    public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        List<CortexObservationField> fields)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        string program = dream.OfferFreshEnumeration();
        fields.Add(new CortexObservationField("candidate-program", program, Blur.SlotSources.Unknown));
        dream.AppendPendingDeltaFields(fields);
        return new CortexObservation(program, false);
    }
}

internal sealed class SolveHoleTool : EmlTool
{
    public SolveHoleTool() : base(EmlActionArms.SolveHole, Blur.SlotSources.PriorObservation) { }
    public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        List<CortexObservationField> fields)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        string program = dream.OfferHoleSolution(arguments, fields);
        dream.AppendPendingDeltaFields(fields);
        return new CortexObservation(program, false);
    }
}

internal sealed class CounterexampleTool : EmlTool
{
    public CounterexampleTool() : base(EmlActionArms.Counterexample, Blur.SlotSources.PriorObservation) { }
    public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        List<CortexObservationField> fields)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        string result = dream.TestCounterexample(fields);
        dream.AppendPendingDeltaFields(fields);
        return new CortexObservation(result, false);
    }
}

internal sealed class CompareTool : EmlTool
{
    public CompareTool() : base(EmlActionArms.Compare, Blur.SlotSources.PriorObservation) { }
    public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        List<CortexObservationField> fields)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        string result = dream.CompareHoleSolution(arguments, fields);
        return new CortexObservation(result, false);
    }
}

internal sealed class EmlActionPolicy : CortexActionPolicy
{
    public override bool HarvestsAfterBatch => true;

    public override bool TryChooseAction(Cortex cortex, List<CortexActionArgument> arguments, out CortexAction action)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        if (dream.HoldsFertilityRoot)
        {
            action = CortexAction.None;
            return false;
        }
        EmlActionArms arm;
        EmlActionSelectionCauses cause;
        CortexProcedureProposal procedureProposal = default;
        bool selected = dream.UsesActionProcedure
            ? dream.TryProposeProcedureAction(arguments, out arm, out cause, out procedureProposal)
            : dream.TryProposeActionArm(out arm, out cause);
        if (!selected)
        {
            if (!dream.UsesActionProcedure) dream.RecordActionAbstention();
            action = CortexAction.None;
            return false;
        }
        EmlActionArms launchpadArm = arm;
        EmlActionSelectionCauses sharedCause;
        if (dream.UsesReflexFrozenFuel)
        {
            // The reflex arm is deliberately launchpad-only: policy grammar choice is adaptive work.
            arm = launchpadArm;
            sharedCause = cause;
        }
        else arm = dream.ChooseSharedAction(cortex, launchpadArm, out sharedCause);
        if (dream.UsesActionProcedure)
        {
            if (arm != launchpadArm)
            {
                dream.RejectSharedAction();
                arm = launchpadArm;
                sharedCause = cause;
            }
            dream.StageProcedureAction(in procedureProposal, arm, sharedCause);
        }
        else dream.StageActionArm(arm, sharedCause);
        EmlTool tool = ResolveTool(cortex, arm);
        dream.RecordActionStep(arm, cortex.Step);
        string token = EmlActionSelectionTokens.ActionToken(arm);
        arguments.Add(new CortexActionArgument("arm", token, tool.Source));
        arguments.Add(new CortexActionArgument("selection-cause",
            EmlActionSelectionTokens.GetSelectionCauseToken(sharedCause), ResolveSelectionSource(sharedCause)));
        action = new CortexAction(tool, "");
        return true;
    }

    public override void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields, byte[] executionBytes,
        List<TapeEventID> eventIDs)
    {
        if (action.Tool is not EmlTool tool) return;
        ResolveReplay(cortex).BindProcedureObservation(tool.Arm, fields);
    }

    public override bool ShouldRouteActionArgument(Cortex cortex, CortexAction action, CortexActionArgument argument)
        => argument.Slot is "obligation-claim-id" or "obligation-species" or "candidate-program" or "solve-status"
            || argument.Slot.StartsWith("guard:", StringComparison.Ordinal);

    public override bool ShouldRouteObservationField(Cortex cortex, CortexAction action, CortexObservationField field)
    {
        if (action.Tool is not EmlTool tool) return false;
        return tool.Arm switch
        {
            EmlActionArms.Counterexample => field.Slot is "probe-verdict" or "obligation-claim-id"
                or "shuffled-obligation-claim-id" or "solve-hole-eligibility",
            EmlActionArms.SolveHole => field.Slot is "obligation-claim-id" or "obligation-species" or "candidate-program" or "solve-status",
            EmlActionArms.Compare => field.Slot is "obligation-claim-id" or "obligation-species" or "candidate-program" or "solve-status",
            _ => false,
        };
    }

    public override void AppendDomainEvents(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs)
        => ResolveReplay(cortex).Accrete(cortex.Tape, cortex.Journal, cortex.Step, eventIDs, cortex);

    public override void OnActionExecutionAdmission(Cortex cortex, CortexAction action,
        in CortexActionAdmissionDecision decision)
        => ResolveReplay(cortex).RecordExecutionAdmission(decision.Admitted);

    public override CortexActionAdmissionDecision EvaluateActionExecutionAdmission(Cortex cortex, CortexAction action,
        List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields)
    {
        if (action.Tool is not EmlTool tool || ResolveReplay(cortex).ShouldAdmitFertilityAction(cortex, tool.Arm, fields))
            return CortexActionAdmissionDecision.Admit("fertility-admission");
        return CortexActionAdmissionDecision.Deny("fertility-admission");
    }

    public override void OnActionBatchEnd(Cortex cortex)
    {
        ReplayCalc dream = ResolveReplay(cortex);
        dream.Accrete(cortex.Tape, cortex.Journal, cortex.Step, cortex: cortex);
        dream.CloseActionBatch();
        dream.AdmitNewLaws(cortex.Grammar, cortex.Tape, cortex.Journal, cortex.Step, cortex.LoopLineage,
            cortex.InstallRevision?.Revision ?? GrammarRevisionID.Zero, cortex.Config.Learning.EvidenceWeightScale);
        dream.RequestFertilityStop(cortex);
    }

    private static ReplayCalc ResolveReplay(Cortex cortex)
        => cortex.ActiveCurriculum as ReplayCalc
        ?? throw new InvalidOperationException($"EML policy mounted over {cortex.ActiveCurriculum.GetType().Name}, expected ReplayCalc");

    private static Blur.SlotSources ResolveSelectionSource(EmlActionSelectionCauses cause)
        => cause == EmlActionSelectionCauses.Grammar
            ? Blur.SlotSources.GrammarPrior
            : Blur.SlotSources.Unknown;

    private static EmlTool ResolveTool(Cortex cortex, EmlActionArms arm) => arm switch
    {
        EmlActionArms.FreshBias => cortex.FindTool<FreshBiasTool>() ?? throw new InvalidOperationException("fresh-bias tool is not mounted"),
        EmlActionArms.FreshEnum => cortex.FindTool<FreshEnumTool>() ?? throw new InvalidOperationException("fresh-enum tool is not mounted"),
        EmlActionArms.SolveHole => cortex.FindTool<SolveHoleTool>() ?? throw new InvalidOperationException("solve-hole tool is not mounted"),
        EmlActionArms.Counterexample => cortex.FindTool<CounterexampleTool>() ?? throw new InvalidOperationException("counterexample tool is not mounted"),
        EmlActionArms.Compare => cortex.FindTool<CompareTool>() ?? throw new InvalidOperationException("compare tool is not mounted"),
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "unknown EML action arm"),
    };
}

internal sealed class EmlDiscoveryReward : CortexReward
{
    public override void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs)
    {
        if (action.Tool is not EmlTool tool) return;
        ReplayCalc dream = cortex.ActiveCurriculum as ReplayCalc
            ?? throw new InvalidOperationException("EML reward requires ReplayCalc");
        dream.MeterAction(cortex, tool.Arm);
    }

    public override void OnRunEnd(Cortex cortex)
    {
        ReplayCalc dream = cortex.ActiveCurriculum as ReplayCalc
            ?? throw new InvalidOperationException("EML reward requires ReplayCalc");
        string report = dream.ActionReport();
        cortex.CurrentRun.Write("eml_actions.tsv", report);
        cortex.CurrentRun.Write("eml_laws.tsv", dream.ReportLaws());
        cortex.CurrentRun.Write("eml_proof_queue.tsv", dream.ReportLawProofQueue());
        cortex.CurrentRun.Write("eml_law_funnel.tsv", dream.ReportLawFunnel(cortex.Grammar));
        cortex.CurrentRun.Write("eml_rewrites.tsv", dream.ReportRewriteSystem());
        cortex.CurrentRun.Write("eml_obligations.tsv", dream.ObligationReport());
        Trace.Note(report);
        Trace.Note($"EML laws · {dream.LawCount} behavior-certified equation class(es) · generated {dream.LawGeneratedMints}/{dream.LawGeneratedOffers} semantic mints/offers · direct probe-witness matches {dream.LawDirectWitnessMatches} (report-only)");
    }
}
