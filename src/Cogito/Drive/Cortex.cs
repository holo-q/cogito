namespace Cogito;

/// The embeddable Cogito organism: one configuration object in, one life-loop run out.
public sealed partial class Cortex
{
    private readonly CortexConfig _config;
    private readonly ICurriculum? _mountedCurriculum;
    private readonly List<CortexTool> _tools;
    private readonly List<CortexActionPolicy> _actionPolicies;
    private readonly List<CortexReward> _rewards;

    public Cortex(CortexConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Learning.Policies.Validate();
        _mountedCurriculum = _config.RuntimeCurriculum ?? (_config.Curriculum switch
        {
            CortexEmlCurriculum eml => ReplayCalc.Mount(_config.Seed, eml),
            CortexWeftCurriculum weft => weft.Mount(_config.Seed),
            _ => null,
        });
        if (_mountedCurriculum is null && _config.Curriculum is CortexCorpusCurriculumConfig corpus && string.IsNullOrWhiteSpace(corpus.Corpus.Path))
            throw new ArgumentException($"{_config.Curriculum.GetType().Name}.Corpus.Path is required.", nameof(config));

        bool mountsEml = _config.Curriculum is CortexEmlCurriculum;
        bool mountsEmlActions = _config.Curriculum is CortexEmlCurriculum { Actions: not EmlActionSelections.Off };
        _tools = _config.Tools ?? (mountsEmlActions ? ReplayCalc.CreateActionTools() : []);
        _actionPolicies = _config.ActionPolicies ?? (mountsEmlActions ? ReplayCalc.CreateActionPolicies() : []);
        _rewards = _config.Rewards ?? (mountsEml ? ReplayCalc.CreateRewards(mountsEmlActions) : []);
    }

    public CortexConfig Config => _config;
    internal ICurriculum? MountedCurriculum => _mountedCurriculum;
    internal AdmissionPlan? AdmissionPlan => _config.AdmissionPlan;

    public int Run() => Run((Action<Cortex, CortexExecutionWindow>?)null);

    /// Drive a fresh run into an already-created exact destination. The destination is an
    /// operational ownership seam, not semantic run configuration, so it never enters the
    /// checkpoint dialect or arm fingerprint. Plain Run() retains its lineage allocation.
    public int Run(Run destination) => Run(destination, null);

    /// Drive a fresh run into an exact destination and observe its runtime bind.
    public int Run(Run destination, Action<Cortex, CortexExecutionWindow>? afterRuntimeBind)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Drive(this, _config.ToRunConfig(_mountedCurriculum), destination: destination,
            checkpointRunEnd: true, afterRuntimeBind: afterRuntimeBind);
    }

    public int Run(Action<Cortex, CortexExecutionWindow>? afterRuntimeBind)
        => Drive(this, _config.ToRunConfig(_mountedCurriculum), checkpointRunEnd: true,
            afterRuntimeBind: afterRuntimeBind);

    internal int RunForkFixture(Action<Cortex, int> afterCompletedStep)
    {
        ArgumentNullException.ThrowIfNull(afterCompletedStep);
        return Drive(this, _config.ToRunConfig(_mountedCurriculum), checkpointRunEnd: true,
            afterCompletedStep: afterCompletedStep);
    }

    internal int CaptureColdForkSeedSetup(Action<Cortex, CortexExecutionWindow> afterRuntimeBind)
    {
        ArgumentNullException.ThrowIfNull(afterRuntimeBind);
        return Drive(this, _config.ToRunConfig(_mountedCurriculum), checkpointRunEnd: false,
            executionWindow: new CortexExecutionWindow(0, 0), afterRuntimeBind: afterRuntimeBind);
    }

    internal int CaptureColdForkSeedSetup(Run destination, Action<Cortex, CortexExecutionWindow> afterRuntimeBind)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(afterRuntimeBind);
        return Drive(this, _config.ToRunConfig(_mountedCurriculum), destination: destination, checkpointRunEnd: false,
            executionWindow: new CortexExecutionWindow(0, 0), afterRuntimeBind: afterRuntimeBind);
    }
}

public sealed class CortexConfig
{
    public string RunName { get; init; } = "cortex";
    /// Explicit paired-fuel schedule identity. Null selects the fresh-run default
    /// for the gate-paired run name; an empty value is an intentional no-schedule
    /// choice and survives checkpoint recovery unchanged.
    public string? EmlPairedFuelScheduleIdentity { get; init; }
    /// Optional immutable dissolution gate registration copied into the run before step zero.
    public string DeepRematchGatePath { get; init; } = "";
    public string DeepRematchGateDigest { get; init; } = "";
    public int Steps { get; init; } = 200;
    public ulong Seed { get; init; } = 0xC0117011UL;

    public CortexGenerationConfig Generation { get; init; } = new();
    public CortexStrideConfig Stride { get; init; } = new();
    public CortexCurriculumConfig Curriculum { get; init; } = new CortexEmlCurriculum();
    public ICurriculum? RuntimeCurriculum { get; init; }
    /// Zero uses the curriculum's intake batch, keeping action cadence and world intake on one budget.
    public int ActionsPerStep { get; init; }
    /// Null mounts the curriculum's native faculties; an explicit list replaces that faculty class.
    public List<CortexTool>? Tools { get; init; }
    public List<CortexActionPolicy>? ActionPolicies { get; init; }
    public List<CortexReward>? Rewards { get; init; }
    public CortexLearningConfig Learning { get; init; } = new();
    public CortexDurabilityConfig Durability { get; init; } = new();
    public CortexReadoutConfig Readout { get; init; } = new();
    public List<CortexStopCondition> StopConditions { get; init; } = new();
    /// Focused world assays may supply an immutable admission order. It is consumed
    /// only by the world cursor and is deliberately absent from policy configuration.
    internal AdmissionPlan? AdmissionPlan { get; init; }

    internal CortexRunConfig ToRunConfig(ICurriculum? runtimeCurriculum)
    {
        CogitoCorpus? corpus = Curriculum.CorpusSource;
        bool includesEml = Curriculum is CortexEmlCurriculum;
        bool includesEmlActions = Curriculum is CortexEmlCurriculum { Actions: not EmlActionSelections.Off };
        string[] curveReadout = Readout.Curve is null
            ? CortexReadoutConfig.CreateDefaultCurve(includesEml, includesEmlActions)
            : Readout.Curve;
        string deepRematchGatePath = DeepRematchGatePath ?? "";
        string deepRematchGateDigest = DeepRematchGateDigest ?? "";
        if (!string.IsNullOrWhiteSpace(deepRematchGatePath))
        {
            deepRematchGatePath = Path.GetFullPath(deepRematchGatePath);
            DeepRematchGateConfig registration = DeepRematchGate.DecodeConfig(File.ReadAllBytes(deepRematchGatePath));
            deepRematchGateDigest = registration.ConfigDigest;
        }
        return new CortexRunConfig(
        corpus?.Path ?? "",
        corpus?.ExpectedWorldSHA256 ?? "",
        Math.Max(1, Steps),
        BlockLen: Generation.BlockLength,
        MaxBlockBytes: Generation.MaxBlockBytes,
        Window: Generation.Window,
        Lambda: Generation.NoveltyDecay,
        Seed: Seed,
        ReStrideBytes: Stride.ReinduceBytes,
        DomStrideSpans: Stride.DomainSpans,
        FrontierCapExps: Stride.FrontierExpansionCap,
        IntakeBatch: Curriculum.IntakeBatch,
        SeedSpans: Curriculum.SeedSpans,
        MixEvery: Curriculum.MixEvery,
        AffirmGate: Curriculum.AffirmGate,
        Curriculum: Curriculum.Token,
        Glob: corpus?.Glob ?? CogitoCorpus.DefaultGlob,
        GrokCv: Curriculum.GrokCv,
        LockRounds: Curriculum.LockRounds,
        Energy: Generation.Energy.Token(),
        AffFloor: Generation.AffinityFloor,
        IntervalConsolidationPhase: Learning.IntervalConsolidationPhase,
        GrammarBudgetBits: Learning.GrammarBudgetBits,
        WScale: Learning.EvidenceWeightScale,
        CrossReflect: Learning.CrossReflect,
        ReplayRatio: Learning.ReplayRatio,
        ConsolidationPhaseControl: Learning.ConsolidationPhaseControl,
        SenseMask: Learning.SenseMask,
        Breach: Learning.Breach,
        Simhash: Learning.Simhash.Token(),
        NearDupe: Learning.NearDupe,
        Antiunify: Learning.Antiunify,
        WallTol: Learning.WallTolerance,
        CheckpointEvery: Durability.CheckpointEvery,
        CurveEvery: Math.Max(1, Durability.CurveEvery),
        Loom: Learning.Loom,
        Shed: Learning.Shed,
        Rhythm: Learning.Rhythm,
        HomeoPolicy: Learning.Homeostat.Policy,
        HomeoAutonomy: Learning.Homeostat.Autonomy,
        PolicyDefaultMode: Learning.Policies.DefaultMode,
        PolicyAuthorityCeiling: Learning.Policies.AuthorityCeiling,
        PolicyOverrides: Learning.Policies.Overrides.Count == 0 ? [] : [.. Learning.Policies.Overrides],
        PolicyShadowDecisions: Learning.Policies.ShadowDecisions,
        PolicyProposalInterval: Learning.Policies.ProposalInterval,
        ReadoutDeliberationQuota: Learning.Policies.ReadoutDeliberationQuota,
        PolicyTrialHorizons: [.. Learning.Policies.TrialHorizons],
        PolicyTrialAllocationArmSteps: Learning.Policies.TrialAllocation?.ArmSteps ?? 0,
        PolicyTrialAllocationIdentity: Learning.Policies.TrialAllocation?.Identity ?? "",
        PolicyTrialAllocationAuthority: Learning.Policies.TrialAllocation?.Authority ?? CortexPolicyAuthorities.Grammar,
        EmlSignatureDigits: Curriculum is CortexEmlCurriculum emlCurriculum ? emlCurriculum.SignatureDigits : ReplayCalc.MountSig,
        Eml: Curriculum is CortexEmlCurriculum eml ? eml.ToKnobs() : EmlKnobs.Mount,
        EmlHoldoutFraction: Curriculum is CortexEmlCurriculum emlHoldout ? emlHoldout.HoldoutFraction : 0,
        EmlHoldoutSeed: Curriculum is CortexEmlCurriculum emlHoldoutSeed ? emlHoldoutSeed.HoldoutSeed : 0,
        EmlTargetCatalog: Curriculum is CortexEmlCurriculum emlTargets ? emlTargets.TargetCatalog : EmlTargetCatalogs.LeafCount,
        EmlGrammarSampling: Curriculum is CortexEmlCurriculum emlSampling ? emlSampling.GrammarSampling : EmlGrammarSamplingModes.Live,
        EmlProcessCatalog: Curriculum is CortexEmlCurriculum emlProcess ? emlProcess.ProcessCatalog : EmlProcessCatalogs.Full,
        EmlRung0: Curriculum is CortexEmlCurriculum emlRung0 ? emlRung0.Rung0 : EmlRung0Modes.Armed,
        EmlDeliberation: Curriculum is CortexEmlCurriculum emlDeliberation ? emlDeliberation.Deliberation : EmlDeliberationModes.Adaptive,
        EmlDeliberationBudget: Curriculum is CortexEmlCurriculum emlBudget ? emlBudget.DeliberationBudget : EmlDeliberationQuota.Default,
        CurveReadout: string.Join(",", curveReadout),
        ActionsPerStep: Math.Max(1, ActionsPerStep > 0 ? ActionsPerStep : Curriculum.IntakeBatch),
        StopConditions: StopConditions.Count == 0 ? [] : [.. StopConditions],
        RuntimeCurriculum: runtimeCurriculum,
        RunName: RunName,
        DeepRematchGatePath: deepRematchGatePath,
        DeepRematchGateDigest: deepRematchGateDigest,
        EmlPairedFuelScheduleIdentity: EmlPairedFuelScheduleIdentity is null
            ? (string.Equals(RunName, "gate-paired", StringComparison.Ordinal) ? "paired-gate-fuel-v1" : "")
            : EmlPairedFuelScheduleIdentity,
        AdmissionPlan: AdmissionPlan);
    }
}

public readonly record struct CortexStopCondition(string Selector, double AtLeast);

public sealed record CogitoCorpus
{
    public const string DefaultGlob = "*.cs,*.py,*.md,*.txt";
    public required string Path { get; init; }
    public string Glob { get; init; } = DefaultGlob;
    /// Optional immutable registration of the selected file world. When present the runtime
    /// verifies it before step zero; authority reloads retain the recorded value but do not
    /// re-read external corpus files.
    public string ExpectedWorldSHA256 { get; init; } = "";
}

public sealed class CortexGenerationConfig
{
    public int BlockLength { get; init; } = 700;
    public int MaxBlockBytes { get; init; } = 16384;
    public int Window { get; init; }
    public double NoveltyDecay { get; init; } = 0.3;
    public CortexEnergy Energy { get; init; } = CortexEnergy.Metabolic;
    public double AffinityFloor { get; init; } = 1.0;
}

public sealed class CortexStrideConfig
{
    public int ReinduceBytes { get; init; } = GrokDefaults.ReStrideBytes;
    public int DomainSpans { get; init; } = GrokDefaults.DomStrideSpans;
    public int FrontierExpansionCap { get; init; } = GrokDefaults.FrontierCapExps;
}

public abstract record CortexCurriculumConfig
{
    internal abstract string Token { get; }
    internal virtual CogitoCorpus? CorpusSource => null;
    public int IntakeBatch { get; init; } = 4;
    public int SeedSpans { get; init; } = 3;
    public int MixEvery { get; init; } = 8;
    public double AffirmGate { get; init; }
    public double GrokCv { get; init; } = GrokDefaults.Cv;
    public int LockRounds { get; init; } = GrokDefaults.LockRounds;
}

public abstract record CortexCorpusCurriculumConfig : CortexCurriculumConfig
{
    public required CogitoCorpus Corpus { get; init; }
    internal override CogitoCorpus? CorpusSource => Corpus;
}

public sealed record CortexFlatPoolCurriculum : CortexCorpusCurriculumConfig
{
    internal override string Token => "flatpool";
}

public sealed record CortexGrokBellCurriculum : CortexCorpusCurriculumConfig
{
    internal override string Token => "grokbell";
}

public sealed record CortexEmlCurriculum : CortexCurriculumConfig
{
    internal override string Token => EmlActionSelectionTokens.CurriculumToken(Actions);
    public CogitoCorpus? Corpus { get; init; }
    internal override CogitoCorpus? CorpusSource => Corpus;
    public int SignatureDigits { get; init; } = ReplayCalc.MountSig;
    public double HoldoutFraction { get; init; }
    public ulong HoldoutSeed { get; init; }
    public EmlTargetCatalogs TargetCatalog { get; init; } = EmlTargetCatalogs.LeafCount;
    public EmlGrammarSamplingModes GrammarSampling { get; init; } = EmlGrammarSamplingModes.Live;
    public EmlProcessCatalogs ProcessCatalog { get; init; } = EmlProcessCatalogs.Full;
    public EmlRung0Modes Rung0 { get; init; } = EmlRung0Modes.Armed;
    public EmlDeliberationModes Deliberation { get; init; } = EmlDeliberationModes.Adaptive;
    public EmlDeliberationQuota DeliberationBudget { get; init; } = EmlDeliberationQuota.Default;
    public EmlActionSelections Actions { get; init; } = EmlActionSelections.ProcedureGuarded;
    public EmlGenerationConfig Generation { get; init; } = new();
    public EmlLiftGateConfig Lift { get; init; } = new();

    internal EmlKnobs ToKnobs() => Generation.ToKnobs(Lift.ToKnobs());
}

public enum EmlTargetCatalogs
{
    LeafCount,
    ScientificCalculator,
}

public enum EmlGrammarSamplingModes
{
    Live,
    Frozen,
}

public enum EmlProcessCatalogs
{
    NegativeLog,
    Full,
}

public enum EmlRung0Modes
{
    Disabled,
    Armed,
}

public enum EmlDeliberationModes
{
    Frozen,
    Adaptive,
}

public sealed record CortexCampfireCurriculum : CortexCorpusCurriculumConfig
{
    internal override string Token => "campfire";
}

public sealed class CortexLearningConfig
{
    public CortexConsolidationPhaseControl ConsolidationPhaseControl { get; init; } = CortexConsolidationPhaseControl.Homeostat;
    public int IntervalConsolidationPhase { get; init; }
    public long GrammarBudgetBits { get; init; }
    public int EvidenceWeightScale { get; init; } = 8;
    public bool CrossReflect { get; init; } = true;
    public double ReplayRatio { get; init; } = 1.0;
    public string SenseMask { get; init; } = "";
    public bool Breach { get; init; } = true;
    public CortexSimhash Simhash { get; init; } = CortexSimhash.Auto;
    public bool NearDupe { get; init; } = true;
    public bool Antiunify { get; init; } = true;
    public double WallTolerance { get; init; } = 0.003;
    public bool Loom { get; init; } = true;
    public bool Shed { get; init; } = true;
    public bool Rhythm { get; init; } = true;
    public CortexHomeostatConfig Homeostat { get; init; } = new();
    public CortexPolicyLearningConfig Policies { get; init; } = new();
}

public sealed class CortexHomeostatConfig
{
    public HomeoPolicies Policy { get; init; } = HomeoPolicies.Predict;
    public HomeostatAutonomyModes Autonomy { get; init; } = HomeostatAutonomyModes.Full;
}

public enum HomeostatAutonomyModes
{
    Off,
    Emulation,
    Full,
}

public sealed class CortexDurabilityConfig
{
    public int CheckpointEvery { get; init; }
    public int CurveEvery { get; init; } = 1;
}

public enum CortexEnergy
{
    Metabolic,
    Markov,
    Coupling,
    NodeBirth,
    Energy,
}

public enum CortexSimhash
{
    Auto,
    On,
    Off,
}

public enum CortexConsolidationPhaseControl
{
    Interval,
    Homeostat,
}

internal static class CortexConfigTokens
{
    public static int ResolveActionsPerStep(CortexRunConfig config)
    {
        if (config.ActionsPerStep > 0) return config.ActionsPerStep;
        if (!EmlActionSelectionTokens.IsEmlCurriculum(config.Curriculum)) return 1;
        return EmlActionSelectionTokens.ParseCurriculumToken(config.Curriculum) == EmlActionSelections.Off
            ? 1
            : Math.Max(1, config.IntakeBatch);
    }

    public static string Token(this CortexEnergy value) => value switch
    {
        CortexEnergy.Metabolic => "metabolic",
        CortexEnergy.Markov => "markov",
        CortexEnergy.Coupling => "coupling",
        CortexEnergy.NodeBirth => "nodebirth",
        CortexEnergy.Energy => "energy",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unknown Cogito energy policy"),
    };

    public static string Token(this CortexSimhash value) => value switch
    {
        CortexSimhash.Auto => "auto",
        CortexSimhash.On => "on",
        CortexSimhash.Off => "off",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unknown Cogito SimHash mode"),
    };

    public static CortexCurriculumConfig ParseCurriculum(string? value, CogitoCorpus? corpus) => (value ?? "flatpool").ToLowerInvariant() switch
    {
        "flatpool" => new CortexFlatPoolCurriculum { Corpus = RequireCorpus(corpus, "flatpool") },
        "grokbell" => new CortexGrokBellCurriculum { Corpus = RequireCorpus(corpus, "grokbell") },
        "eml" => new CortexEmlCurriculum { Corpus = corpus },
        "campfire" => new CortexCampfireCurriculum { Corpus = RequireCorpus(corpus, "campfire") },
        string bad => throw new ArgumentException($"unknown curriculum '{bad}' (flatpool|grokbell|eml|campfire)"),
    };

    private static CogitoCorpus RequireCorpus(CogitoCorpus? corpus, string curriculum)
        => corpus ?? throw new ArgumentException($"{curriculum} curriculum requires a corpus path.");

    public static CortexEnergy ParseEnergy(string? value) => (value ?? "metabolic").ToLowerInvariant() switch
    {
        "metabolic" => CortexEnergy.Metabolic,
        "markov" => CortexEnergy.Markov,
        "mcmc" => CortexEnergy.Markov,
        "coupling" => CortexEnergy.Coupling,
        "nodebirth" => CortexEnergy.NodeBirth,
        "energy" => CortexEnergy.Energy,
        string bad => throw new ArgumentException($"unknown energy policy '{bad}' (metabolic|markov|mcmc|coupling|nodebirth|energy)"),
    };

    public static CortexSimhash ParseSimhash(bool forceOn, bool forceOff)
    {
        if (forceOn && forceOff) throw new ArgumentException("--simhash and --no-simhash cannot both be set");
        return forceOff ? CortexSimhash.Off : forceOn ? CortexSimhash.On : CortexSimhash.Auto;
    }

    public static CortexSimhash ParseSimhash(string? value) => (value ?? "auto").ToLowerInvariant() switch
    {
        "auto" => CortexSimhash.Auto,
        "on" => CortexSimhash.On,
        "off" => CortexSimhash.Off,
        string bad => throw new ArgumentException($"unknown simhash policy '{bad}' (auto|on|off)"),
    };

    public static HomeoPolicies ParseHomeostatPolicy(string? value) => (value ?? "predict").ToLowerInvariant() switch
    {
        "reflex" => HomeoPolicies.Reflex,
        "wired" => HomeoPolicies.Wired,
        "predict" => HomeoPolicies.Predict,
        string bad => throw new ArgumentException($"unknown Homeostat policy '{bad}' (reflex|wired|predict)"),
    };

    public static HomeostatAutonomyModes ParseHomeostatAutonomy(string? value) => (value ?? "full").ToLowerInvariant() switch
    {
        "off" => HomeostatAutonomyModes.Off,
        "emulation" => HomeostatAutonomyModes.Emulation,
        "full" => HomeostatAutonomyModes.Full,
        string bad => throw new ArgumentException($"unknown Homeostat autonomy mode '{bad}' (off|emulation|full)"),
    };
}
