namespace Cogito;

using System.Text;

internal enum CorroborationGrades
{
    SingleLineage,
    Corroborated,
    Replicated,
}

internal enum LawResidencies
{
    Shadow,
    Resident,
    Resorbed,
}

internal readonly record struct EmlMembraneAdmission(
    bool Admitted,
    bool AddedLineage,
    CorroborationGrades Corroboration,
    LawResidencies Residency);

internal sealed class EmlMembrane
{
    private readonly MindID _host;
    private readonly EmlEvaluatorID _evaluator;
    private readonly long _residencyHorizon;
    private readonly Dictionary<EmlLawClassID, LawState> _laws = new();

    public EmlMembrane(MindID host, EmlEvaluatorID evaluator, long residencyHorizon)
    {
        if (residencyHorizon <= 0) throw new ArgumentOutOfRangeException(nameof(residencyHorizon));
        _host = host;
        _evaluator = evaluator;
        _residencyHorizon = residencyHorizon;
    }

    public int Count => _laws.Count;

    public EmlMembraneAdmission AdmitPackage(EmlLawPackage package, long eventIndex)
    {
        if (eventIndex < 0) throw new ArgumentOutOfRangeException(nameof(eventIndex));
        if (package.Exporter == _host)
            return new EmlMembraneAdmission(false, false, CorroborationGrades.SingleLineage, LawResidencies.Shadow);
        if (!package.TryVerify(_evaluator, out EmlVerifiedLaw? verified) || verified is null)
            return new EmlMembraneAdmission(false, false, CorroborationGrades.SingleLineage, LawResidencies.Shadow);
        if (!_laws.TryGetValue(package.LawClass, out LawState? law))
        {
            law = new LawState(package.LawClass, eventIndex);
            _laws.Add(package.LawClass, law);
        }
        int before = law.CountActiveLineages(eventIndex, _residencyHorizon);
        bool admitted = law.AddPackage(package, verified, eventIndex);
        bool addedLineage = admitted && law.LineageWasFirst(package.Lineage, eventIndex, _residencyHorizon);
        int lineages = law.CountActiveLineages(eventIndex, _residencyHorizon);
        if (lineages >= 2) law.Promote(eventIndex);
        CorroborationGrades grade = ResolveCorroboration(lineages);
        return new EmlMembraneAdmission(admitted, addedLineage && lineages > before, grade, law.Residency);
    }

    public bool RecordCanonicalDelta(EmlLawClassID lawClass, long eventIndex)
    {
        if (!_laws.TryGetValue(lawClass, out LawState? law)) return false;
        law.RecordCanonicalDelta(eventIndex);
        return true;
    }

    public int ResorbExpired(long eventIndex)
    {
        int resorbed = 0;
        foreach (LawState law in _laws.Values)
        {
            if (law.Residency != LawResidencies.Shadow) continue;
            if (eventIndex - law.TrialStartedAt < _residencyHorizon) continue;
            law.Resorb(eventIndex);
            resorbed++;
        }
        return resorbed;
    }

    public void AppendResidentPackages(List<EmlLawPackage> packages)
        => AppendPackages(LawResidencies.Resident, packages);

    public void AppendShadowPackages(List<EmlLawPackage> packages)
        => AppendPackages(LawResidencies.Shadow, packages);

    private void AppendPackages(LawResidencies residency, List<EmlLawPackage> packages)
    {
        List<LawState> laws = new(_laws.Values);
        laws.Sort(static (left, right) => string.CompareOrdinal(left.LawClass.Value, right.LawClass.Value));
        for (int i = 0; i < laws.Count; i++)
        {
            LawState law = laws[i];
            if (law.Residency != residency || law.RepresentativePackage is null) continue;
            packages.Add(law.RepresentativePackage);
        }
    }

    public void AppendRows(List<EmlMembraneRow> rows, long eventIndex)
    {
        foreach (LawState law in _laws.Values)
        {
            rows.Add(new EmlMembraneRow(
                _host,
                law.LawClass,
                law.Packages.Count,
                law.CountActiveLineages(eventIndex, _residencyHorizon),
                ResolveCorroboration(law.CountActiveLineages(eventIndex, _residencyHorizon)),
                law.Residency,
                law.CanonicalDeltas,
                law.TrialStartedAt,
                law.LastChangedAt));
        }
    }

    internal EmlMembraneSnapshot Capture()
    {
        List<EmlMembraneLawSnapshot> laws = new(_laws.Count);
        foreach (LawState law in _laws.Values) laws.Add(law.Capture());
        laws.Sort(static (left, right) => string.CompareOrdinal(left.LawClass.Value, right.LawClass.Value));
        return new EmlMembraneSnapshot(_host, _evaluator, _residencyHorizon, laws);
    }

    internal static EmlMembrane Restore(EmlMembraneSnapshot snapshot)
    {
        EmlMembrane membrane = new(snapshot.Host, snapshot.Evaluator, snapshot.ResidencyHorizon);
        for (int i = 0; i < snapshot.Laws.Count; i++)
        {
            LawState law = LawState.Restore(snapshot.Laws[i], snapshot.Evaluator);
            if (!membrane._laws.TryAdd(law.LawClass, law))
                throw new InvalidDataException("EML membrane snapshot repeats a law class");
        }
        return membrane;
    }

    private static CorroborationGrades ResolveCorroboration(int lineages)
        => lineages >= 3 ? CorroborationGrades.Replicated
         : lineages >= 2 ? CorroborationGrades.Corroborated
         : CorroborationGrades.SingleLineage;

    private sealed class LawState
    {
        private readonly Dictionary<ImportPackageID, PackageState> _packages = new();

        public LawState(EmlLawClassID lawClass, long trialStartedAt)
        {
            LawClass = lawClass;
            TrialStartedAt = trialStartedAt;
            LastChangedAt = trialStartedAt;
        }

        public EmlLawClassID LawClass { get; }
        public IReadOnlyDictionary<ImportPackageID, PackageState> Packages => _packages;
        public LawResidencies Residency { get; private set; } = LawResidencies.Shadow;
        public EmlVerifiedLaw? Representative { get; private set; }
        public EmlLawPackage? RepresentativePackage { get; private set; }
        public int CanonicalDeltas { get; private set; }
        public long TrialStartedAt { get; private set; }
        public long LastChangedAt { get; private set; }

        public bool AddPackage(EmlLawPackage package, EmlVerifiedLaw verified, long eventIndex)
        {
            if (_packages.ContainsKey(package.Package)) return false;
            if (Residency == LawResidencies.Resorbed)
            {
                Residency = LawResidencies.Shadow;
                TrialStartedAt = eventIndex;
                CanonicalDeltas = 0;
                Representative = null;
                RepresentativePackage = null;
            }
            _packages.Add(package.Package, new PackageState(package, eventIndex));
            if (Representative is null || CompareRepresentatives(verified, Representative) < 0)
            {
                Representative = verified;
                RepresentativePackage = package;
            }
            LastChangedAt = eventIndex;
            return true;
        }

        public bool LineageWasFirst(MindLineageID lineage, long eventIndex, long horizon)
        {
            int count = 0;
            foreach (PackageState package in _packages.Values)
                if (package.Package.Lineage == lineage && eventIndex - package.AdmittedAt <= horizon) count++;
            return count == 1;
        }

        public int CountActiveLineages(long eventIndex, long horizon)
        {
            HashSet<MindLineageID> lineages = new();
            foreach (PackageState package in _packages.Values)
                if (eventIndex - package.AdmittedAt <= horizon) lineages.Add(package.Package.Lineage);
            return lineages.Count;
        }

        public void Promote(long eventIndex)
        {
            if (Representative is null) throw new InvalidOperationException("an unverified law cannot become resident");
            Residency = LawResidencies.Resident;
            LastChangedAt = eventIndex;
        }

        public void RecordCanonicalDelta(long eventIndex)
        {
            CanonicalDeltas++;
            Promote(eventIndex);
        }

        public void Resorb(long eventIndex)
        {
            Residency = LawResidencies.Resorbed;
            Representative = null;
            RepresentativePackage = null;
            LastChangedAt = eventIndex;
        }

        public EmlMembraneLawSnapshot Capture()
        {
            List<EmlMembranePackageSnapshot> packages = new(_packages.Count);
            foreach (PackageState package in _packages.Values)
                packages.Add(new EmlMembranePackageSnapshot(package.Package, package.AdmittedAt));
            packages.Sort(static (left, right) => string.CompareOrdinal(left.Package.Package.Value, right.Package.Package.Value));
            return new EmlMembraneLawSnapshot(
                LawClass,
                Residency,
                CanonicalDeltas,
                TrialStartedAt,
                LastChangedAt,
                RepresentativePackage?.Package,
                packages);
        }

        public static LawState Restore(EmlMembraneLawSnapshot snapshot, EmlEvaluatorID evaluator)
        {
            LawState law = new(snapshot.LawClass, snapshot.TrialStartedAt)
            {
                Residency = snapshot.Residency,
                CanonicalDeltas = snapshot.CanonicalDeltas,
                LastChangedAt = snapshot.LastChangedAt,
            };
            for (int i = 0; i < snapshot.Packages.Count; i++)
            {
                EmlMembranePackageSnapshot package = snapshot.Packages[i];
                if (!package.Package.TryVerify(evaluator, out EmlVerifiedLaw? verified) || verified is null)
                    throw new InvalidDataException("EML membrane snapshot contains an unverifiable package");
                if (!law._packages.TryAdd(package.Package.Package, new PackageState(package.Package, package.AdmittedAt)))
                    throw new InvalidDataException("EML membrane snapshot repeats an import package");
                if (snapshot.RepresentativePackage == package.Package.Package)
                {
                    law.Representative = verified;
                    law.RepresentativePackage = package.Package;
                }
            }
            if (law.Residency == LawResidencies.Resident && law.Representative is null)
                throw new InvalidDataException("resident EML membrane law has no locally verified representative");
            if (law.Residency == LawResidencies.Resorbed)
            {
                law.Representative = null;
                law.RepresentativePackage = null;
            }
            return law;
        }

        private static int CompareRepresentatives(EmlVerifiedLaw left, EmlVerifiedLaw right)
        {
            int cost = left.TemplateCostBits.CompareTo(right.TemplateCostBits);
            if (cost != 0) return cost;
            int template = string.CompareOrdinal(left.Law.Template, right.Law.Template);
            if (template != 0) return template;
            return string.CompareOrdinal(left.Proof.OccurrenceCheckPrediction, right.Proof.OccurrenceCheckPrediction);
        }
    }

    internal sealed class PackageState(EmlLawPackage package, long admittedAt)
    {
        public EmlLawPackage Package { get; } = package;
        public long AdmittedAt { get; } = admittedAt;
    }
}

internal readonly record struct EmlMembraneRow(
    MindID Host,
    EmlLawClassID LawClass,
    int Packages,
    int ActiveLineages,
    CorroborationGrades Corroboration,
    LawResidencies Residency,
    int CanonicalDeltas,
    long TrialStartedAt,
    long LastChangedAt);

internal readonly record struct EmlMindEpochRequest(
    EmlMindIdentity Mind,
    int Epoch,
    long EvaluatorCallBudget,
    CheckpointID Checkpoint,
    IReadOnlyList<EmlLawPackage> ShadowImports,
    IReadOnlyList<EmlLawPackage> ResidentImports);

internal sealed class EmlMindEpochResult
{
    public EmlMindEpochResult(
        MindID mind,
        CheckpointID startingCheckpoint,
        CheckpointID endingCheckpoint,
        long evaluatorCalls,
        List<EmlLawPackage> exportedPackages,
        List<EmlLawClassID> openedClasses,
        List<EmlLawClassID> importCanonicalDeltas)
    {
        Mind = mind;
        StartingCheckpoint = startingCheckpoint;
        EndingCheckpoint = endingCheckpoint;
        EvaluatorCalls = evaluatorCalls;
        ExportedPackages = new List<EmlLawPackage>(exportedPackages);
        OpenedClasses = new List<EmlLawClassID>(openedClasses);
        ImportCanonicalDeltas = new List<EmlLawClassID>(importCanonicalDeltas);
    }

    public MindID Mind { get; }
    public CheckpointID StartingCheckpoint { get; }
    public CheckpointID EndingCheckpoint { get; }
    public long EvaluatorCalls { get; }
    public IReadOnlyList<EmlLawPackage> ExportedPackages { get; }
    public IReadOnlyList<EmlLawClassID> OpenedClasses { get; }
    public IReadOnlyList<EmlLawClassID> ImportCanonicalDeltas { get; }
}

internal interface IEmlPopulationRunner
{
    void RunEpochs(IReadOnlyList<EmlMindEpochRequest> requests, List<EmlMindEpochResult> results);
}

internal sealed class EmlPopulation
{
    private readonly EmlCohortManifest _manifest;
    private readonly long _residencyHorizon;
    private readonly Dictionary<MindID, CheckpointID> _checkpoints = new();
    private readonly Dictionary<MindID, EmlMembrane> _membranes = new();
    private readonly Dictionary<MindID, HashSet<EmlLawClassID>> _isolatedClasses = new();
    private readonly HashSet<EmlLawClassID> _chimeraClasses = new();
    private int _epoch;
    private long _eventIndex;

    public EmlPopulation(EmlCohortManifest manifest, long residencyHorizon)
    {
        if (residencyHorizon <= 0) throw new ArgumentOutOfRangeException(nameof(residencyHorizon));
        _manifest = manifest;
        _residencyHorizon = residencyHorizon;
        for (int i = 0; i < manifest.Minds.Count; i++)
        {
            EmlMindIdentity mind = manifest.Minds[i];
            _checkpoints.Add(mind.Mind, mind.InitialCheckpoint);
            _membranes.Add(mind.Mind, new EmlMembrane(mind.Mind, manifest.Evaluator, residencyHorizon));
            _isolatedClasses.Add(mind.Mind, new HashSet<EmlLawClassID>());
        }
    }

    public int Epoch => _epoch;

    public void RunEpoch(IEmlPopulationRunner runner, long evaluatorCallsPerMind)
    {
        if (evaluatorCallsPerMind <= 0) throw new ArgumentOutOfRangeException(nameof(evaluatorCallsPerMind));
        List<EmlMindEpochRequest> requests = new(_manifest.Minds.Count);
        for (int i = 0; i < _manifest.Minds.Count; i++)
        {
            EmlMindIdentity mind = _manifest.Minds[i];
            List<EmlLawPackage> shadowImports = new();
            List<EmlLawPackage> imports = new();
            _membranes[mind.Mind].AppendShadowPackages(shadowImports);
            _membranes[mind.Mind].AppendResidentPackages(imports);
            requests.Add(new EmlMindEpochRequest(
                mind,
                _epoch,
                evaluatorCallsPerMind,
                _checkpoints[mind.Mind],
                shadowImports,
                imports));
        }

        List<EmlMindEpochResult> results = new(_manifest.Minds.Count);
        runner.RunEpochs(requests, results);
        ValidateResults(requests, results, evaluatorCallsPerMind);
        _eventIndex = checked(_eventIndex + evaluatorCallsPerMind);

        Dictionary<MindID, EmlMindEpochResult> resultsByMind = new();
        for (int i = 0; i < results.Count; i++)
        {
            EmlMindEpochResult result = results[i];
            resultsByMind.Add(result.Mind, result);
            _checkpoints[result.Mind] = result.EndingCheckpoint;
            HashSet<EmlLawClassID> isolated = _isolatedClasses[result.Mind];
            for (int opened = 0; opened < result.OpenedClasses.Count; opened++)
                isolated.Add(result.OpenedClasses[opened]);
            EmlMembrane membrane = _membranes[result.Mind];
            for (int delta = 0; delta < result.ImportCanonicalDeltas.Count; delta++)
            {
                EmlLawClassID lawClass = result.ImportCanonicalDeltas[delta];
                if (!membrane.RecordCanonicalDelta(lawClass, _eventIndex))
                    throw new InvalidDataException("mind reported a canonical delta for an unimported law class");
                if (!WasOpenedByAnyFounder(lawClass)) _chimeraClasses.Add(lawClass);
            }
        }

        for (int hostIndex = 0; hostIndex < _manifest.Minds.Count; hostIndex++)
        {
            EmlMindIdentity host = _manifest.Minds[hostIndex];
            EmlMembrane membrane = _membranes[host.Mind];
            for (int sourceIndex = 0; sourceIndex < _manifest.Minds.Count; sourceIndex++)
            {
                EmlMindIdentity source = _manifest.Minds[sourceIndex];
                if (source.Mind == host.Mind) continue;
                EmlMindEpochResult result = resultsByMind[source.Mind];
                for (int packageIndex = 0; packageIndex < result.ExportedPackages.Count; packageIndex++)
                    membrane.AdmitPackage(result.ExportedPackages[packageIndex], _eventIndex);
            }
            membrane.ResorbExpired(_eventIndex);
        }
        _epoch++;
    }

    public byte[] Save(IEmlPopulationRONCodec codec) => codec.EncodeCohort(Capture());

    public static EmlPopulation Load(ReadOnlySpan<byte> bytes, IEmlPopulationRONCodec codec)
    {
        EmlCohortSnapshot snapshot = codec.DecodeCohort(bytes);
        EmlPopulation population = new(snapshot.Manifest, snapshot.ResidencyHorizon)
        {
            _epoch = snapshot.Epoch,
            _eventIndex = snapshot.EventIndex,
        };
        population._checkpoints.Clear();
        population._membranes.Clear();
        population._isolatedClasses.Clear();
        for (int i = 0; i < snapshot.Minds.Count; i++)
        {
            EmlPopulationMindSnapshot mind = snapshot.Minds[i];
            if (!population._checkpoints.TryAdd(mind.Mind, mind.Checkpoint)
                || !population._membranes.TryAdd(mind.Mind, EmlMembrane.Restore(mind.Membrane))
                || !population._isolatedClasses.TryAdd(mind.Mind, new HashSet<EmlLawClassID>(mind.IsolatedClasses)))
                throw new InvalidDataException("EML cohort snapshot repeats a mind");
        }
        for (int i = 0; i < snapshot.ChimeraClasses.Count; i++)
            population._chimeraClasses.Add(snapshot.ChimeraClasses[i]);
        population.ValidatePopulationShape();
        return population;
    }

    public string Report()
    {
        StringBuilder report = new();
        report.Append("cohort\t").Append(_manifest.Cohort.Value).AppendLine();
        report.Append("epoch\t").Append(_epoch).AppendLine();
        report.Append("event_index\t").Append(_eventIndex).AppendLine();
        report.Append("chimera_classes\t").Append(_chimeraClasses.Count).AppendLine();
        AppendCloneNullReport(report);
        report.AppendLine();
        report.AppendLine("mind\tlineage\tkind\tcheckpoint\tisolated_classes");
        for (int i = 0; i < _manifest.Minds.Count; i++)
        {
            EmlMindIdentity mind = _manifest.Minds[i];
            report.Append(mind.Mind.Value).Append('\t')
                .Append(mind.Lineage.Value).Append('\t')
                .Append(mind.Kind).Append('\t')
                .Append(_checkpoints[mind.Mind].Value).Append('\t')
                .Append(_isolatedClasses[mind.Mind].Count).AppendLine();
        }
        report.AppendLine();
        report.AppendLine("host\tlaw_class\tpackages\tactive_lineages\tcorroboration\tresidency\tcanonical_deltas\ttrial_started\tlast_changed\tchimera");
        List<EmlMembraneRow> rows = new();
        foreach (EmlMembrane membrane in _membranes.Values) membrane.AppendRows(rows, _eventIndex);
        rows.Sort(static (left, right) =>
        {
            int host = string.CompareOrdinal(left.Host.Value, right.Host.Value);
            return host != 0 ? host : string.CompareOrdinal(left.LawClass.Value, right.LawClass.Value);
        });
        for (int i = 0; i < rows.Count; i++)
        {
            EmlMembraneRow row = rows[i];
            report.Append(row.Host.Value).Append('\t')
                .Append(row.LawClass.Value).Append('\t')
                .Append(row.Packages).Append('\t')
                .Append(row.ActiveLineages).Append('\t')
                .Append(row.Corroboration).Append('\t')
                .Append(row.Residency).Append('\t')
                .Append(row.CanonicalDeltas).Append('\t')
                .Append(row.TrialStartedAt).Append('\t')
                .Append(row.LastChangedAt).Append('\t')
                .Append(_chimeraClasses.Contains(row.LawClass) ? 1 : 0).AppendLine();
        }
        return report.ToString();
    }

    internal EmlCohortSnapshot Capture()
    {
        List<EmlPopulationMindSnapshot> minds = new(_manifest.Minds.Count);
        for (int i = 0; i < _manifest.Minds.Count; i++)
        {
            EmlMindIdentity mind = _manifest.Minds[i];
            List<EmlLawClassID> isolated = new(_isolatedClasses[mind.Mind]);
            isolated.Sort(static (left, right) => string.CompareOrdinal(left.Value, right.Value));
            minds.Add(new EmlPopulationMindSnapshot(
                mind.Mind,
                _checkpoints[mind.Mind],
                isolated,
                _membranes[mind.Mind].Capture()));
        }
        List<EmlLawClassID> chimera = new(_chimeraClasses);
        chimera.Sort(static (left, right) => string.CompareOrdinal(left.Value, right.Value));
        return new EmlCohortSnapshot(_manifest, _residencyHorizon, _epoch, _eventIndex, minds, chimera);
    }

    private void ValidateResults(
        IReadOnlyList<EmlMindEpochRequest> requests,
        IReadOnlyList<EmlMindEpochResult> results,
        long evaluatorCallsPerMind)
    {
        if (results.Count != requests.Count)
            throw new InvalidDataException("population runner did not return one isolated result per mind");
        Dictionary<MindID, EmlMindEpochRequest> requestsByMind = new();
        for (int i = 0; i < requests.Count; i++) requestsByMind.Add(requests[i].Mind.Mind, requests[i]);
        HashSet<MindID> seen = new();
        for (int i = 0; i < results.Count; i++)
        {
            EmlMindEpochResult result = results[i];
            if (!seen.Add(result.Mind)
                || !requestsByMind.TryGetValue(result.Mind, out EmlMindEpochRequest request)
                || result.StartingCheckpoint != request.Checkpoint
                || result.EvaluatorCalls != evaluatorCallsPerMind
                || string.IsNullOrWhiteSpace(result.EndingCheckpoint.Value))
                throw new InvalidDataException("population runner violated the matched isolated epoch contract");
            for (int packageIndex = 0; packageIndex < result.ExportedPackages.Count; packageIndex++)
            {
                EmlLawPackage package = result.ExportedPackages[packageIndex];
                if (package.Exporter != result.Mind
                    || package.Lineage != request.Mind.Lineage
                    || package.Checkpoint != result.EndingCheckpoint
                    || package.Evaluator != _manifest.Evaluator
                    || !package.HasValidIdentity())
                    throw new InvalidDataException("mind exported a law package with foreign or mutable identity");
            }
        }
    }

    private bool WasOpenedByAnyFounder(EmlLawClassID lawClass)
    {
        for (int i = 0; i < _manifest.Minds.Count; i++)
        {
            EmlMindIdentity mind = _manifest.Minds[i];
            if (mind.Kind == EmlMindKinds.Founder && _isolatedClasses[mind.Mind].Contains(lawClass)) return true;
        }
        return false;
    }

    private void AppendCloneNullReport(StringBuilder report)
    {
        EmlMindIdentity founder = _manifest.Minds[0];
        EmlMindIdentity clone = _manifest.Minds[3];
        int clonePairClasses = 0;
        int clonePairVestViolations = 0;
        foreach (EmlMembrane membrane in _membranes.Values)
        {
            EmlMembraneSnapshot snapshot = membrane.Capture();
            for (int i = 0; i < snapshot.Laws.Count; i++)
            {
                EmlMembraneLawSnapshot law = snapshot.Laws[i];
                bool hasFounder = false;
                bool hasClone = false;
                HashSet<MindLineageID> lineages = new();
                for (int packageIndex = 0; packageIndex < law.Packages.Count; packageIndex++)
                {
                    EmlLawPackage package = law.Packages[packageIndex].Package;
                    if (package.Exporter == founder.Mind) hasFounder = true;
                    if (package.Exporter == clone.Mind) hasClone = true;
                    lineages.Add(package.Lineage);
                }
                if (!hasFounder || !hasClone || lineages.Count != 1) continue;
                clonePairClasses++;
                if (law.Residency == LawResidencies.Resident && law.CanonicalDeltas == 0)
                    clonePairVestViolations++;
            }
        }
        report.Append("clone_pair_classes\t").Append(clonePairClasses).AppendLine();
        report.Append("clone_pair_vest_violations\t").Append(clonePairVestViolations).AppendLine();
    }

    private void ValidatePopulationShape()
    {
        if (_checkpoints.Count != _manifest.Minds.Count
            || _membranes.Count != _manifest.Minds.Count
            || _isolatedClasses.Count != _manifest.Minds.Count)
            throw new InvalidDataException("EML cohort snapshot does not cover every mind");
        for (int i = 0; i < _manifest.Minds.Count; i++)
        {
            MindID mind = _manifest.Minds[i].Mind;
            if (!_checkpoints.ContainsKey(mind) || !_membranes.ContainsKey(mind) || !_isolatedClasses.ContainsKey(mind))
                throw new InvalidDataException("EML cohort snapshot contains a foreign mind set");
        }
    }
}

internal sealed class EmlCohortSnapshot(
    EmlCohortManifest manifest,
    long residencyHorizon,
    int epoch,
    long eventIndex,
    List<EmlPopulationMindSnapshot> minds,
    List<EmlLawClassID> chimeraClasses)
{
    public EmlCohortManifest Manifest { get; } = manifest;
    public long ResidencyHorizon { get; } = residencyHorizon;
    public int Epoch { get; } = epoch;
    public long EventIndex { get; } = eventIndex;
    public IReadOnlyList<EmlPopulationMindSnapshot> Minds { get; } = minds;
    public IReadOnlyList<EmlLawClassID> ChimeraClasses { get; } = chimeraClasses;
}

internal sealed class EmlPopulationMindSnapshot(
    MindID mind,
    CheckpointID checkpoint,
    List<EmlLawClassID> isolatedClasses,
    EmlMembraneSnapshot membrane)
{
    public MindID Mind { get; } = mind;
    public CheckpointID Checkpoint { get; } = checkpoint;
    public IReadOnlyList<EmlLawClassID> IsolatedClasses { get; } = isolatedClasses;
    public EmlMembraneSnapshot Membrane { get; } = membrane;
}

internal sealed class EmlMembraneSnapshot(
    MindID host,
    EmlEvaluatorID evaluator,
    long residencyHorizon,
    List<EmlMembraneLawSnapshot> laws)
{
    public MindID Host { get; } = host;
    public EmlEvaluatorID Evaluator { get; } = evaluator;
    public long ResidencyHorizon { get; } = residencyHorizon;
    public IReadOnlyList<EmlMembraneLawSnapshot> Laws { get; } = laws;
}

internal sealed class EmlMembraneLawSnapshot(
    EmlLawClassID lawClass,
    LawResidencies residency,
    int canonicalDeltas,
    long trialStartedAt,
    long lastChangedAt,
    ImportPackageID? representativePackage,
    List<EmlMembranePackageSnapshot> packages)
{
    public EmlLawClassID LawClass { get; } = lawClass;
    public LawResidencies Residency { get; } = residency;
    public int CanonicalDeltas { get; } = canonicalDeltas;
    public long TrialStartedAt { get; } = trialStartedAt;
    public long LastChangedAt { get; } = lastChangedAt;
    public ImportPackageID? RepresentativePackage { get; } = representativePackage;
    public IReadOnlyList<EmlMembranePackageSnapshot> Packages { get; } = packages;
}

internal readonly record struct EmlMembranePackageSnapshot(EmlLawPackage Package, long AdmittedAt);
