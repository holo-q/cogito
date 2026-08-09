namespace Cogito;

using System.Security.Cryptography;
using System.Text;

/// Mints the one pre-arm registration from the exact source, world, binary, and typed
/// arm configuration that the registered runner will consume.
public static class LoopClosureRegistrationBuilder
{
    public readonly record struct Request(
        string OutputPath,
        string CorpusPath,
        string RepositoryRoot = ".");

    public static LoopClosureRegistration Mint(Request request, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        string root = Path.GetFullPath(request.RepositoryRoot);
        string corpus = Path.GetFullPath(request.CorpusPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"loop-closure repository root was not found: {root}");
        if (!Directory.Exists(corpus) && !File.Exists(corpus)) throw new FileNotFoundException("loop-closure corpus was not found", corpus);

        // Frozen registration artifact path; identifier-side name is ClosureCertificate.
        string planPath = Path.Combine(root, "docs", "plans", "loop-closure-birth-certificate-v2.md");
        string runnerPath = Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRunner.cs");
        string registrationSourcePath = Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRegistration.cs");
        string worldRecipePath = Path.Combine(root, "data", "code.manifest");
        RequireFile(planPath, "registered plan");
        RequireFile(runnerPath, "loop-closure runner authority");
        RequireFile(registrationSourcePath, "loop-closure registration authority");
        RequireFile(worldRecipePath, "registered world recipe");

        string planSHA256 = HashFile(planPath);
        if (!string.Equals(planSHA256, LoopClosureRegistration.RegisteredPlanSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"registered plan bytes differ: expected {LoopClosureRegistration.RegisteredPlanSHA256}, observed {planSHA256}");
        if (!string.Equals(HashFile(worldRecipePath), LoopClosureRegistration.RegisteredWorldRecipeSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("registered world recipe bytes differ");
        string worldCensusSHA256 = LoopClosureRegistration.ComputeRegisteredWorldCensusSHA256(root, LoopClosureRegistration.RegisteredWorldPath);
        if (!string.Equals(worldCensusSHA256, LoopClosureRegistration.RegisteredWorldCensusSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"registered world path/content census differs: expected {LoopClosureRegistration.RegisteredWorldCensusSHA256}, observed {worldCensusSHA256}");
        string worldSHA256 = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
        string sourceCensusBody = LoopClosureRegistration.ComputeSourceTreeCensusBody(root);
        string sourceTreeSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceCensusBody)));
        if (!string.Equals(Path.GetFullPath(corpus), Path.GetFullPath(Path.Combine(root, LoopClosureRegistration.RegisteredWorldPath)), StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure corpus must be the registered relative world path");

        RunAuthorityBinary binary = RunAuthority.CurrentBinaryIdentity();
        string output = Path.GetFullPath(request.OutputPath);
        (string bundleAppHostPath, string bundleAssemblyPath, string bundleCensusSHA256, IReadOnlyList<LoopClosureAuthorityBundleFile> bundleFiles)
            = LoopClosureAuthorityBundleStore.Capture(output, binary);
        LoopClosureRegistration seed = LoopClosureRegistration.Create(
            LoopClosureRegistration.RegisteredPlanSHA256,
            sourceTreeSHA256,
            "LoopClosureRunner.cs",
            HashFile(runnerPath),
            "LoopClosureRunner.cs",
            HashFile(runnerPath),
            binary.ProcessName,
            binary.ProcessSHA256,
            binary.AssemblyName,
            binary.AssemblySHA256,
            LoopClosureAuthorityBundleStore.RelativePath,
            bundleAppHostPath,
            bundleAssemblyPath,
            bundleCensusSHA256,
            bundleFiles,
            LoopClosureRegistration.RegisteredWorldPath,
            worldCensusSHA256,
            worldSHA256,
            LoopClosureRegistration.RegisteredWorldRecipeSHA256,
            domain,
            DigestArmTopology(domain),
            DigestArmNeutralAndInitial(corpus, worldSHA256, domain, out string liveConfigSHA256, out string controlConfigSHA256, out string initialStateSHA256),
            liveConfigSHA256,
            controlConfigSHA256,
            initialStateSHA256,
            DigestFuelSchedule(),
            DigestEventPolicy(),
            BuildArtifacts(planPath, runnerPath, registrationSourcePath, worldCensusSHA256, worldSHA256,
                sourceTreeSHA256, initialStateSHA256, liveConfigSHA256, controlConfigSHA256),
            BuildNullIdentities());

        ValidateMinted(seed, root, corpus, planPath, runnerPath, registrationSourcePath, worldRecipePath, domain, output);
        seed.Write(output);
        seed.AssertPersistedBytes(output);
        seed.WriteSourceCensusSidecar(output, sourceCensusBody);
        return seed;
    }

    internal static void ValidateCurrent(LoopClosureRegistration registration, string root, string corpus, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        string planPath = Path.Combine(root, "docs", "plans", "loop-closure-birth-certificate-v2.md");
        string runnerPath = Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRunner.cs");
        string registrationSourcePath = Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRegistration.cs");
        string worldRecipePath = Path.Combine(root, "data", "code.manifest");
        RequireFile(planPath, "registered plan");
        RequireFile(runnerPath, "loop-closure runner authority");
        RequireFile(registrationSourcePath, "loop-closure registration authority");
        RequireFile(worldRecipePath, "registered world recipe");
        ValidateMinted(registration, root, corpus, planPath, runnerPath, registrationSourcePath, worldRecipePath, domain: domain);
    }

    private static void ValidateMinted(LoopClosureRegistration registration, string root, string corpus,
        string planPath, string runnerPath, string registrationSourcePath, string worldRecipePath,
        IPolicyBoundaryDomain domain, string? registrationPath = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        registration.Validate();
        registration.ValidateDomain(domain);
        RequireEqual(registration.PlanSHA256, HashFile(planPath), "plan");
        RequireEqual(registration.SourceTreeSHA256, LoopClosureRegistration.ComputeSourceTreeSHA256(root), "source tree");
        RequireEqual(registration.RunnerAuthoritySHA256, HashFile(runnerPath), "runner authority");
        RequireEqual(registration.AdjudicatorAuthoritySHA256, HashFile(runnerPath), "adjudicator authority");
        RequireEqual(registration.WorldCensusSHA256,
            LoopClosureRegistration.ComputeRegisteredWorldCensusSHA256(root, registration.WorldPath), "world path/content census");
        RequireEqual(registration.WorldCensusSHA256, LoopClosureRegistration.RegisteredWorldCensusSHA256, "registered world path/content census");
        RequireEqual(registration.WorldSHA256, FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob), "world runtime identity");
        RequireEqual(registration.WorldRecipeSHA256, HashFile(worldRecipePath), "world recipe");
        RequireEqual(registration.ArmTopologySHA256, DigestArmTopology(domain), "arm topology");
        string armNeutral = DigestArmNeutralAndInitial(corpus, registration.WorldSHA256, domain,
            out string live, out string control, out string initial);
        RequireEqual(registration.ArmNeutralConfigSHA256, armNeutral, "arm-neutral config");
        RequireEqual(registration.LiveConfigSHA256, live, "live config");
        RequireEqual(registration.ControlConfigSHA256, control, "control config");
        RequireEqual(registration.InitialStateSHA256, initial, "initial state");
        RequireEqual(registration.FuelScheduleSHA256, DigestFuelSchedule(), "fuel schedule");
        RequireEqual(registration.EventPolicySHA256, DigestEventPolicy(), "event policy");
        VerifyArtifact(registration, "plan/docs/plans/loop-closure-birth-certificate-v2.md", registration.PlanSHA256);
        VerifyArtifact(registration, "source/src/Cogito/Drive/LoopClosureRegistration.cs", HashFile(registrationSourcePath));
        VerifyArtifact(registration, "runner/src/Cogito/Drive/LoopClosureRunner.cs", registration.RunnerAuthoritySHA256);
        VerifyArtifact(registration, "adjudicator/src/Cogito/Drive/LoopClosureRunner.cs", registration.AdjudicatorAuthoritySHA256);
        VerifyArtifact(registration, "source-tree.sha256", registration.SourceTreeSHA256);
        VerifyArtifact(registration, "world-census/data/code", registration.WorldCensusSHA256);
        VerifyArtifact(registration, "world/data/code", registration.WorldSHA256);
        VerifyArtifact(registration, "world/recipe", registration.WorldRecipeSHA256);
        VerifyArtifact(registration, "live/config", registration.LiveConfigSHA256);
        VerifyArtifact(registration, "control/config", registration.ControlConfigSHA256);
        VerifyArtifact(registration, "initial/state", registration.InitialStateSHA256);
        LoopClosureNullIdentity[] registeredNulls = registration.NullIdentities.ToArray();
        LoopClosureNullIdentity[] expectedNulls = BuildNullIdentities().ToArray();
        if (!registeredNulls.SequenceEqual(expectedNulls))
            throw new InvalidDataException("loop-closure registered null identities drifted");
        RunAuthorityBinary binary = RunAuthority.CurrentBinaryIdentity();
        RequireEqual(registration.AppHost, binary.ProcessName, "apphost name");
        RequireEqual(registration.AppHostSHA256, binary.ProcessSHA256, "apphost digest");
        RequireEqual(registration.Assembly, binary.AssemblyName, "assembly name");
        RequireEqual(registration.AssemblySHA256, binary.AssemblySHA256, "assembly digest");
        if (!string.IsNullOrWhiteSpace(registrationPath))
            LoopClosureAuthorityBundleStore.Validate(registrationPath, registration.AuthorityBundlePath,
                registration.AuthorityBundleAppHostPath, registration.AuthorityBundleAssemblyPath, registration.AppHost, registration.AppHostSHA256,
                registration.Assembly, registration.AssemblySHA256, registration.AuthorityBundleCensusSHA256, registration.AuthorityBundleFiles);
    }

    private static void VerifyArtifact(LoopClosureRegistration registration, string path, string expected)
    {
        LoopClosureArtifactManifestEntry artifact = registration.Artifacts.SingleOrDefault(item => item.RelativePath == path);
        if (!artifact.IsValid || !string.Equals(artifact.SHA256, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"loop-closure registration artifact {path} does not carry its rehashed authority");
    }

    private static void RequireEqual(string expected, string observed, string role)
    {
        if (!string.Equals(expected, observed, StringComparison.Ordinal))
            throw new InvalidDataException($"loop-closure registration {role} drifted: expected {expected}, observed {observed}");
    }

    private static string DigestArmNeutralAndInitial(string corpus, string worldSHA256, IPolicyBoundaryDomain domain,
        out string liveConfigSHA256, out string controlConfigSHA256, out string initialStateSHA256)
    {
        ArgumentNullException.ThrowIfNull(domain);
        Cortex live = LoopClosureRunner.CreateArm(LoopClosureRegistration.RegisteredSeed, worldSHA256, LoopClosureArms.Live, corpus, domain);
        Cortex control = LoopClosureRunner.CreateArm(LoopClosureRegistration.RegisteredSeed, worldSHA256, LoopClosureArms.Control, corpus, domain);
        CortexRunConfig liveConfig = live.Config.ToRunConfig(null);
        CortexRunConfig controlConfig = control.Config.ToRunConfig(null);
        liveConfigSHA256 = Cortex.PersistedConfigDigest(liveConfig);
        controlConfigSHA256 = Cortex.PersistedConfigDigest(controlConfig);
        string armNeutral = Cortex.ArmNeutralPersistedConfigDigest(liveConfig);
        if (!string.Equals(armNeutral, Cortex.ArmNeutralPersistedConfigDigest(controlConfig), StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure arm-neutral config differs between live and control");
        initialStateSHA256 = Digest("initial-state-v1", Encoding.UTF8.GetBytes(string.Join('|',
            liveConfig.Seed, liveConfig.Steps, liveConfig.ExpectedWorldSHA256, liveConfig.Curriculum,
            liveConfig.ActionsPerStep, liveConfig.IntakeBatch, liveConfig.SeedSpans, liveConfig.EmlPairedFuelScheduleIdentity,
            armNeutral)));
        return armNeutral;
    }

    private static string DigestArmTopology(IPolicyBoundaryDomain domain)
        => LoopClosureRegistration.ComputeArmTopologySHA256(domain);

    private static string DigestFuelSchedule()
    {
        EmlDeliberationQuota quota = EmlDeliberationQuota.PairedGateNominal;
        EmlDeliberationCounts total = new(quota.CandidateEvaluations, quota.LogicalProgramPoints, quota.ExecutedProgramPoints,
            quota.InverseTransforms, quota.HashProbes, quota.JoinAttempts, quota.JoinHits, quota.ProcessTerms,
            quota.VerifierProgramPoints, quota.CandidateSupplyItems, quota.LawRewriteApplications, quota.LawRewriteTreeNodes);
        return EmlPairedFuelSchedule.Create("paired-gate-fuel-v1", LoopClosureRegistration.RegisteredHorizon, in total).Digest;
    }

    private static string DigestEventPolicy()
        => Digest("event-policy-v1", Encoding.UTF8.GetBytes(string.Join(';',
            // Frozen lineage grammar tokens; identifier-side name is AdmissionPlan.
            "WorldEncounter->", "VerifiedLaw->WorldEncounter+",
            // Frozen digest token Rung0Derivation; identifier-side name is Rung0Composition.
            "Rung0Derivation->VerifiedLaw+",
            "DisplacedEvaluation->Rung0Composition", "LearnedReadout->DisplacedEvaluation",
            // Frozen lineage grammar token FundedDissent; identifier-side name is PaidDivergence.
            "Funding->LearnedReadout", "FundedDissent->Funding,LearnedReadout",
            "AdjudicatedOutcome->FundedDissent", "NewTapeEvidence->AdjudicatedOutcome",
            LoopClosureRegistration.RegisteredLineageNullDomain, "loop-closure-dissent-r1")));

    private static IReadOnlyList<LoopClosureArtifactManifestEntry> BuildArtifacts(string planPath,
        string runnerPath, string registrationSourcePath, string worldCensusSHA256, string worldSHA256, string sourceTreeSHA256,
        string initialStateSHA256, string liveConfigSHA256, string controlConfigSHA256)
    {
        RunAuthorityBinary binary = RunAuthority.CurrentBinaryIdentity();
        List<LoopClosureArtifactManifestEntry> artifacts =
        [
            new("plan/docs/plans/loop-closure-birth-certificate-v2.md", HashFile(planPath)),
            new("source-tree.sha256", sourceTreeSHA256),
            new("source/src/Cogito/Drive/LoopClosureRegistration.cs", HashFile(registrationSourcePath)),
            new("runner/src/Cogito/Drive/LoopClosureRunner.cs", HashFile(runnerPath)),
            new("adjudicator/src/Cogito/Drive/LoopClosureRunner.cs", HashFile(runnerPath)),
            new($"apphost/{binary.ProcessName}", binary.ProcessSHA256),
            new($"assembly/{(string.IsNullOrWhiteSpace(binary.AssemblyName) ? binary.ProcessName : binary.AssemblyName)}", string.IsNullOrWhiteSpace(binary.AssemblySHA256) ? binary.ProcessSHA256 : binary.AssemblySHA256),
            new("world-census/data/code", worldCensusSHA256),
            new("world/data/code", worldSHA256),
            new("world/recipe", LoopClosureRegistration.RegisteredWorldRecipeSHA256),
            new("live/config", liveConfigSHA256),
            new("control/config", controlConfigSHA256),
            new("initial/state", initialStateSHA256),
        ];
        return artifacts;
    }

    private static IReadOnlyList<LoopClosureNullIdentity> BuildNullIdentities()
        =>
        [
            new("shuffled-lineage-predecessor", LoopClosureRegistration.RegisteredLineageNullDomain,
                Digest("null-shuffled-lineage-v1", Encoding.UTF8.GetBytes(LoopClosureRegistration.RegisteredLineageNullDomain))),
            new("funded-dissent-forced-divergent", "loop-closure-dissent-r1",
                Digest("null-funded-dissent-forced-divergent-v1", Encoding.UTF8.GetBytes("loop-closure-dissent-r1|forced-divergent"))),
        ];

    private static string HashFile(string path)
        => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Digest(string tag, ReadOnlySpan<byte> payload)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(tag + "|");
        byte[] bytes = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(bytes, 0);
        payload.CopyTo(bytes.AsSpan(prefix.Length));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void RequireFile(string path, string role)
    {
        if (!File.Exists(path) || Directory.Exists(path)) throw new FileNotFoundException($"{role} is missing", path);
    }

}
