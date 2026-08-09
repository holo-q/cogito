namespace Cogito;

using System.Security.Cryptography;

internal sealed class ReplayCalcPopulationRunner : IEmlPopulationRunner
{
    private const int MaxEpochSteps = 1_000_000;
    private readonly CortexEmlCurriculum _curriculum;
    private readonly int _strideBytes;
    private readonly EmlEvaluatorID _evaluator;
    private readonly string _exchangeDirectory;
    private readonly IEmlPopulationRONCodec _codec;
    private readonly Dictionary<MindID, string> _directories;
    private readonly Dictionary<MindID, HashSet<EmlLawClassID>> _foreignClasses = new();
    private readonly Dictionary<MindID, HashSet<EmlLawClassID>> _reportedNativeClasses = new();
    private readonly List<EmlPopulationEpochRow> _rows = new();

    public ReplayCalcPopulationRunner(
        CortexEmlCurriculum curriculum,
        int strideBytes,
        EmlEvaluatorID evaluator,
        string exchangeDirectory,
        IEmlPopulationRONCodec codec,
        IReadOnlyDictionary<MindID, string> directories)
    {
        _curriculum = curriculum ?? throw new ArgumentNullException(nameof(curriculum));
        _strideBytes = strideBytes;
        _evaluator = evaluator;
        _exchangeDirectory = exchangeDirectory;
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _directories = new Dictionary<MindID, string>(directories);
        foreach (MindID mind in _directories.Keys)
        {
            _foreignClasses.Add(mind, new HashSet<EmlLawClassID>());
            _reportedNativeClasses.Add(mind, new HashSet<EmlLawClassID>());
        }
    }

    public IReadOnlyList<EmlPopulationEpochRow> Rows => _rows;

    public void RunEpochs(IReadOnlyList<EmlMindEpochRequest> requests, List<EmlMindEpochResult> results)
    {
        if (results.Count != 0) throw new ArgumentException("population result buffer must be empty", nameof(results));
        List<EmlMindEpochResult> frozenResults = new(requests.Count);
        for (int i = 0; i < requests.Count; i++)
            frozenResults.Add(RunEpoch(requests[i]));
        results.AddRange(frozenResults);
    }

    private EmlMindEpochResult RunEpoch(in EmlMindEpochRequest request)
    {
        if (!_directories.TryGetValue(request.Mind.Mind, out string? directory))
            throw new InvalidDataException("population request names an unmounted mind");
        CheckpointID startingCheckpoint = HashCheckpoint(directory);
        if (startingCheckpoint != request.Checkpoint)
            throw new InvalidDataException("population mind did not begin at its frozen checkpoint");

        List<EmlLawPackage> shadowImports = RoundTripPackages(request.ShadowImports);
        List<EmlLawPackage> residentImports = RoundTripPackages(request.ResidentImports);
        ReplayCalc dream = ReplayCalc.Mount(request.Mind.SearchSeed, _curriculum);
        CortexStopCondition stop = new("eml.evaluator.calls", request.EvaluatorCallBudget);
        EmlPopulationEpochReward epochReward = new(
            dream,
            stop,
            _evaluator,
            shadowImports,
            residentImports,
            _foreignClasses[request.Mind.Mind]);
        Cortex cortex = new(CreateConfig(request.Mind, dream, epochReward));
        int exitCode = cortex.Resume(directory, MaxEpochSteps, forkCurriculum: true);
        if (exitCode != 0)
            throw new InvalidOperationException($"population mind {request.Mind.Mind.Value} exited {exitCode}");

        CheckpointID endingCheckpoint = HashCheckpoint(directory);
        long actualCalls = checked(dream.EvaluatorCalls - epochReward.StartingEvaluatorCalls);
        if (actualCalls < request.EvaluatorCallBudget)
            throw new InvalidDataException("population mind stopped before exhausting its evaluator budget");

        List<EmlLawClassID> openedClasses = new();
        HashSet<EmlLawClassID> reported = _reportedNativeClasses[request.Mind.Mind];
        List<EmlLawClassID> nativeClasses = new();
        dream.AppendPopulationLawClasses(_foreignClasses[request.Mind.Mind], nativeClasses);
        for (int i = 0; i < nativeClasses.Count; i++)
            if (reported.Add(nativeClasses[i])) openedClasses.Add(nativeClasses[i]);

        List<EmlLawPackage> exportedPackages = new();
        dream.AppendNativeLawPackages(
            request.Mind,
            endingCheckpoint,
            _evaluator,
            "eml/round-robin/v1",
            _foreignClasses[request.Mind.Mind],
            exportedPackages);
        List<EmlLawPackage> sealedPackages = SealPackages(request.Epoch, request.Mind.Mind, exportedPackages);
        List<EmlLawClassID> importCanonicalDeltas = new(epochReward.ImportCanonicalDeltas);
        _rows.Add(new EmlPopulationEpochRow(
            request.Epoch,
            request.Mind.Mind,
            request.Mind.Lineage,
            request.Mind.Kind,
            startingCheckpoint,
            endingCheckpoint,
            request.EvaluatorCallBudget,
            actualCalls,
            shadowImports.Count,
            residentImports.Count,
            epochReward.VerifiedShadowImports,
            epochReward.AdmittedResidentImports,
            sealedPackages.Count,
            openedClasses.Count,
            importCanonicalDeltas.Count,
            directory));
        return new EmlMindEpochResult(
            request.Mind.Mind,
            startingCheckpoint,
            endingCheckpoint,
            request.EvaluatorCallBudget,
            sealedPackages,
            openedClasses,
            importCanonicalDeltas);
    }

    private CortexConfig CreateConfig(
        in EmlMindIdentity mind,
        ReplayCalc dream,
        EmlPopulationEpochReward epochReward)
    {
        List<CortexReward> rewards = ReplayCalc.CreateRewards();
        rewards.Add(epochReward);
        return new CortexConfig
        {
            RunName = "eml-population-" + mind.Mind.Value[..12],
            Steps = MaxEpochSteps,
            Seed = mind.SearchSeed,
            ActionsPerStep = 1,
            Stride = new CortexStrideConfig { ReinduceBytes = _strideBytes },
            Curriculum = _curriculum,
            RuntimeCurriculum = dream,
            Tools = ReplayCalc.CreateActionTools(),
            ActionPolicies = ReplayCalc.CreateActionPolicies(),
            Rewards = rewards,
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0 },
        };
    }

    private List<EmlLawPackage> RoundTripPackages(IReadOnlyList<EmlLawPackage> packages)
    {
        List<EmlLawPackage> decoded = new(packages.Count);
        for (int i = 0; i < packages.Count; i++)
        {
            byte[] bytes = _codec.EncodePackage(packages[i]);
            decoded.Add(_codec.DecodePackage(bytes));
        }
        decoded.Sort(static (left, right) => string.CompareOrdinal(left.Package.Value, right.Package.Value));
        return decoded;
    }

    private List<EmlLawPackage> SealPackages(int epoch, MindID mind, List<EmlLawPackage> packages)
    {
        string directory = Path.Combine(_exchangeDirectory, $"epoch-{epoch:D2}", mind.Value);
        Directory.CreateDirectory(directory);
        List<EmlLawPackage> sealedPackages = new(packages.Count);
        for (int i = 0; i < packages.Count; i++)
        {
            EmlLawPackage package = packages[i];
            byte[] bytes = _codec.EncodePackage(package);
            string path = Path.Combine(directory, package.Package.Value + ".ron");
            File.WriteAllBytes(path, bytes);
            EmlLawPackage decoded = _codec.DecodePackage(File.ReadAllBytes(path));
            if (decoded.Package != package.Package)
                throw new InvalidDataException("sealed population package changed identity during RON exchange");
            sealedPackages.Add(decoded);
        }
        return sealedPackages;
    }

    internal static CheckpointID HashCheckpoint(string directory)
    {
        string path = Path.Combine(directory, Checkpoint.FileName);
        if (!File.Exists(path)) throw new FileNotFoundException("population mind has no frozen checkpoint", path);
        using FileStream stream = File.OpenRead(path);
        return new CheckpointID(Convert.ToHexStringLower(SHA256.HashData(stream)));
    }
}

internal sealed class EmlPopulationEpochReward : CortexReward
{
    private readonly ReplayCalc _dream;
    private readonly CortexStopCondition _stop;
    private readonly EmlEvaluatorID _evaluator;
    private readonly List<EmlLawPackage> _shadowImports;
    private readonly List<EmlLawPackage> _residentImports;
    private readonly HashSet<EmlLawClassID> _foreignClasses;
    private readonly List<EmlLawClassID> _importCanonicalDeltas = new();

    public EmlPopulationEpochReward(
        ReplayCalc dream,
        CortexStopCondition stop,
        EmlEvaluatorID evaluator,
        List<EmlLawPackage> shadowImports,
        List<EmlLawPackage> residentImports,
        HashSet<EmlLawClassID> foreignClasses)
    {
        _dream = dream;
        _stop = stop;
        _evaluator = evaluator;
        _shadowImports = shadowImports;
        _residentImports = residentImports;
        _foreignClasses = foreignClasses;
    }

    public long StartingEvaluatorCalls { get; private set; }
    public int VerifiedShadowImports { get; private set; }
    public int AdmittedResidentImports { get; private set; }
    public IReadOnlyList<EmlLawClassID> ImportCanonicalDeltas => _importCanonicalDeltas;

    public override void OnRunStart(Cortex cortex)
    {
        StartingEvaluatorCalls = _dream.EvaluatorCalls;
        VerifiedShadowImports = _dream.VerifyPopulationPackages(_shadowImports, _evaluator);
        AdmittedResidentImports = _dream.AdmitPopulationPackages(
            _residentImports,
            _evaluator,
            _foreignClasses);
    }

    public override void OnActionBatchEnd(Cortex cortex)
    {
        if (!string.Equals(_stop.Selector, "eml.evaluator.calls", StringComparison.Ordinal))
            throw new InvalidDataException($"unsupported population stop selector '{_stop.Selector}'");
        long elapsed = checked(_dream.EvaluatorCalls - StartingEvaluatorCalls);
        if (elapsed >= _stop.AtLeast) cortex.RequestStop();
    }
}

public sealed partial class ReplayCalc
{
    internal int VerifyPopulationPackages(IReadOnlyList<EmlLawPackage> packages, EmlEvaluatorID evaluator)
    {
        int verified = 0;
        for (int i = 0; i < packages.Count; i++)
            if (packages[i].TryVerify(evaluator, out EmlVerifiedLaw? law) && law is not null) verified++;
        return verified;
    }

    internal int AdmitPopulationPackages(
        IReadOnlyList<EmlLawPackage> packages,
        EmlEvaluatorID evaluator,
        HashSet<EmlLawClassID> foreignClasses)
    {
        HashSet<EmlLawClassID> existing = new();
        foreach (SemanticCASClass<EmlVerifiedLaw> lawClass in _lawStore.Classes.Values)
            existing.Add(EmlLawPackage.CreateLawClassID(lawClass.Rep.Certificate));
        int admitted = 0;
        for (int i = 0; i < packages.Count; i++)
        {
            EmlLawPackage package = packages[i];
            if (!package.TryVerify(evaluator, out EmlVerifiedLaw? law) || law is null)
                throw new InvalidDataException("resident population package failed local verification");
            if (!existing.Contains(package.LawClass)) foreignClasses.Add(package.LawClass);
            if (_lawStore.TryAdmit(law, _lawCaptureIndex++,
                    out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission))
            {
                _lawStatePresent = true;
                admitted++;
            }
        }
        return admitted;
    }

    internal void AppendPopulationLawClasses(
        HashSet<EmlLawClassID> excluded,
        List<EmlLawClassID> classes)
    {
        foreach (SemanticCASClass<EmlVerifiedLaw> lawClass in _lawStore.Classes.Values)
        {
            EmlLawClassID lawClassID = EmlLawPackage.CreateLawClassID(lawClass.Rep.Certificate);
            if (!excluded.Contains(lawClassID)) classes.Add(lawClassID);
        }
        classes.Sort(static (left, right) => string.CompareOrdinal(left.Value, right.Value));
    }

    internal void AppendNativeLawPackages(
        EmlMindIdentity exporter,
        CheckpointID checkpoint,
        EmlEvaluatorID evaluator,
        string causalProcedureDigest,
        HashSet<EmlLawClassID> excluded,
        List<EmlLawPackage> packages)
    {
        foreach (SemanticCASClass<EmlVerifiedLaw> lawClass in _lawStore.Classes.Values)
        {
            EmlLawPackage package = EmlLawPackage.Create(
                exporter,
                checkpoint,
                evaluator,
                lawClass.Rep,
                _sieve.SignatureDigits,
                causalProcedureDigest);
            if (!excluded.Contains(package.LawClass)) packages.Add(package);
        }
        packages.Sort(static (left, right) => string.CompareOrdinal(left.Package.Value, right.Package.Value));
    }
}
