namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// The sealed authority for the registered loop-closure assay. It is written once before
/// either arm starts; every later runner and adjudicator consumes the same bytes.
public sealed class LoopClosureRegistration
{
    public const int SchemaVersion = 5;
    private const int LegacySchemaVersion = 3;
    public const string WriteDialect = "loop-closure-registration-v5";
    public const ulong RegisteredSeed = 0xC0117011UL;
    public const int RegisteredHorizon = 500;
    public const string RegisteredPlanID = "t1514-loop-closure-birth-certificate-v2";
    public const string RegisteredLineageNullDomain = "loop-closure-lineage-r1";
    public const string RegisteredPlanSHA256 = "f511354db428b773395eb767d6359b61b19efa82542f2d824ce3eaa14c84a64a";
    public const string RegisteredWorldPath = "data/code";
    public const string RegisteredWorldCensusSHA256 = "f2c62dd3367f19a49106b33b465fdf68b8e1c0b7439506b02d52f417851bd616";
    public const string RegisteredWorldRecipeSHA256 = "6c30a6e063d9e86a19918cb473e3d9715667936df11b2e4b256ee95f2530115a";

    private LoopClosureRegistration(LoopClosureRegistrationRON document)
    {
        SchemaVersionValue = document.schemaVersion;
        PlanID = document.planID;
        PlanSHA256 = document.planSHA256;
        SourceTreeSHA256 = document.sourceTreeSHA256;
        RunnerAuthority = document.runnerAuthority;
        RunnerAuthoritySHA256 = document.runnerAuthoritySHA256;
        AdjudicatorAuthority = document.adjudicatorAuthority;
        AdjudicatorAuthoritySHA256 = document.adjudicatorAuthoritySHA256;
        AppHost = document.appHost;
        AppHostSHA256 = document.appHostSHA256;
        Assembly = document.assembly;
        AssemblySHA256 = document.assemblySHA256;
        AuthorityBundlePath = document.authorityBundlePath;
        AuthorityBundleAppHostPath = document.authorityBundleAppHostPath;
        AuthorityBundleAssemblyPath = document.authorityBundleAssemblyPath;
        AuthorityBundleCensusSHA256 = document.authorityBundleCensusSHA256;
        AuthorityBundleFiles = document.authorityBundleFiles.Select(static item => new LoopClosureAuthorityBundleFile(item.relativePath, item.sha256)).ToList();
        WorldPath = document.worldPath;
        WorldCensusSHA256 = document.worldCensusSHA256;
        WorldSHA256 = document.worldSHA256;
        WorldRecipeSHA256 = document.worldRecipeSHA256;
        PolicyID = document.policyID;
        PolicyBindingCanonical = document.policyBindingCanonical;
        PolicyBindingSHA256 = document.policyBindingSHA256;
        PolicySchemaSHA256 = document.policySchemaSHA256;
        WriteDialectValue = document.writeDialect;
        ArmTopologySHA256 = document.armTopologySHA256;
        ArmNeutralConfigSHA256 = document.armNeutralConfigSHA256;
        LiveConfigSHA256 = document.liveConfigSHA256;
        ControlConfigSHA256 = document.controlConfigSHA256;
        InitialStateSHA256 = document.initialStateSHA256;
        FuelScheduleSHA256 = document.fuelScheduleSHA256;
        EventPolicySHA256 = document.eventPolicySHA256;
        Seed = document.seed;
        Horizon = document.horizon;
        LineageNullDomain = document.lineageNullDomain;
        Artifacts = document.artifacts.Select(static item => new LoopClosureArtifactManifestEntry(item.relativePath, item.sha256)).ToList();
        NullIdentities = document.nullIdentities.Select(static item => new LoopClosureNullIdentity(item.name, item.domain, item.sha256)).ToList();
        Digest = document.digest;
    }

    private LoopClosureRegistration(
        string planID,
        string planSHA256,
        string sourceTreeSHA256,
        string runnerAuthority,
        string runnerAuthoritySHA256,
        string adjudicatorAuthority,
        string adjudicatorAuthoritySHA256,
        string appHost,
        string appHostSHA256,
        string assembly,
        string assemblySHA256,
        string authorityBundlePath,
        string authorityBundleAppHostPath,
        string authorityBundleAssemblyPath,
        string authorityBundleCensusSHA256,
        IReadOnlyList<LoopClosureAuthorityBundleFile> authorityBundleFiles,
        string worldPath,
        string worldCensusSHA256,
        string worldSHA256,
        string worldRecipeSHA256,
        string policyID,
        string policyBindingCanonical,
        string policyBindingSHA256,
        string policySchemaSHA256,
        string armTopologySHA256,
        string armNeutralConfigSHA256,
        string liveConfigSHA256,
        string controlConfigSHA256,
        string initialStateSHA256,
        string fuelScheduleSHA256,
        string eventPolicySHA256,
        ulong seed,
        int horizon,
        string lineageNullDomain,
        IReadOnlyList<LoopClosureArtifactManifestEntry> artifacts,
        IReadOnlyList<LoopClosureNullIdentity> nullIdentities)
    {
        SchemaVersionValue = SchemaVersion;
        PlanID = planID;
        PlanSHA256 = planSHA256;
        SourceTreeSHA256 = sourceTreeSHA256;
        RunnerAuthority = runnerAuthority;
        RunnerAuthoritySHA256 = runnerAuthoritySHA256;
        AdjudicatorAuthority = adjudicatorAuthority;
        AdjudicatorAuthoritySHA256 = adjudicatorAuthoritySHA256;
        AppHost = appHost;
        AppHostSHA256 = appHostSHA256;
        Assembly = assembly;
        AssemblySHA256 = assemblySHA256;
        AuthorityBundlePath = authorityBundlePath;
        AuthorityBundleAppHostPath = authorityBundleAppHostPath;
        AuthorityBundleAssemblyPath = authorityBundleAssemblyPath;
        AuthorityBundleCensusSHA256 = authorityBundleCensusSHA256;
        AuthorityBundleFiles = authorityBundleFiles.ToList();
        WorldPath = worldPath;
        WorldCensusSHA256 = worldCensusSHA256;
        WorldSHA256 = worldSHA256;
        WorldRecipeSHA256 = worldRecipeSHA256;
        PolicyID = policyID;
        PolicyBindingCanonical = policyBindingCanonical;
        PolicyBindingSHA256 = policyBindingSHA256;
        PolicySchemaSHA256 = policySchemaSHA256;
        WriteDialectValue = WriteDialect;
        ArmTopologySHA256 = armTopologySHA256;
        ArmNeutralConfigSHA256 = armNeutralConfigSHA256;
        LiveConfigSHA256 = liveConfigSHA256;
        ControlConfigSHA256 = controlConfigSHA256;
        InitialStateSHA256 = initialStateSHA256;
        FuelScheduleSHA256 = fuelScheduleSHA256;
        EventPolicySHA256 = eventPolicySHA256;
        Seed = seed;
        Horizon = horizon;
        LineageNullDomain = lineageNullDomain;
        Artifacts = artifacts.ToList();
        NullIdentities = nullIdentities.ToList();
        Digest = ComputeDigest(this);
    }

    public int SchemaVersionValue { get; }
    public string PlanID { get; }
    public string PlanSHA256 { get; }
    public string SourceTreeSHA256 { get; }
    public string RunnerAuthority { get; }
    public string RunnerAuthoritySHA256 { get; }
    public string AdjudicatorAuthority { get; }
    public string AdjudicatorAuthoritySHA256 { get; }
    public string AppHost { get; }
    public string AppHostSHA256 { get; }
    public string Assembly { get; }
    public string AssemblySHA256 { get; }
    public string AuthorityBundlePath { get; }
    public string AuthorityBundleAppHostPath { get; }
    public string AuthorityBundleAssemblyPath { get; }
    public string AuthorityBundleCensusSHA256 { get; }
    public IReadOnlyList<LoopClosureAuthorityBundleFile> AuthorityBundleFiles { get; }
    public string WorldPath { get; }
    public string WorldCensusSHA256 { get; }
    public string WorldSHA256 { get; }
    public string WorldRecipeSHA256 { get; }
    public string PolicyID { get; }
    public string PolicyBindingCanonical { get; }
    public string PolicyBindingSHA256 { get; }
    public string PolicySchemaSHA256 { get; }
    public string WriteDialectValue { get; }
    public string ArmTopologySHA256 { get; }
    public string ArmNeutralConfigSHA256 { get; }
    public string LiveConfigSHA256 { get; }
    public string ControlConfigSHA256 { get; }
    public string InitialStateSHA256 { get; }
    public string FuelScheduleSHA256 { get; }
    public string EventPolicySHA256 { get; }
    public ulong Seed { get; }
    public int Horizon { get; }
    public string LineageNullDomain { get; }
    public IReadOnlyList<LoopClosureArtifactManifestEntry> Artifacts { get; }
    public IReadOnlyList<LoopClosureNullIdentity> NullIdentities { get; }
    public string Digest { get; }
    private byte[]? _legacyEncoded;
    private string? RegistrationPath { get; set; }

    internal static LoopClosureRegistration Create(
        string planSHA256,
        string sourceTreeSHA256,
        string runnerAuthority,
        string runnerAuthoritySHA256,
        string adjudicatorAuthority,
        string adjudicatorAuthoritySHA256,
        string appHost,
        string appHostSHA256,
        string assembly,
        string assemblySHA256,
        string authorityBundlePath,
        string authorityBundleAppHostPath,
        string authorityBundleAssemblyPath,
        string authorityBundleCensusSHA256,
        IReadOnlyList<LoopClosureAuthorityBundleFile> authorityBundleFiles,
        string worldPath,
        string worldCensusSHA256,
        string worldSHA256,
        string worldRecipeSHA256,
        IPolicyBoundaryDomain domain,
        string armTopologySHA256,
        string armNeutralConfigSHA256,
        string liveConfigSHA256,
        string controlConfigSHA256,
        string initialStateSHA256,
        string fuelScheduleSHA256,
        string eventPolicySHA256,
        IReadOnlyList<LoopClosureArtifactManifestEntry> artifacts,
        IReadOnlyList<LoopClosureNullIdentity> nullIdentities,
        ulong seed = RegisteredSeed,
        int horizon = RegisteredHorizon,
        string lineageNullDomain = RegisteredLineageNullDomain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ValidatePolicyDomainIdentity(domain);
        LoopClosureRegistration registration = new(RegisteredPlanID, planSHA256,
            sourceTreeSHA256, runnerAuthority, runnerAuthoritySHA256, adjudicatorAuthority,
            adjudicatorAuthoritySHA256, appHost, appHostSHA256, assembly, assemblySHA256,
            authorityBundlePath, authorityBundleAppHostPath, authorityBundleAssemblyPath,
            authorityBundleCensusSHA256, authorityBundleFiles,
            worldPath, worldCensusSHA256, worldSHA256, worldRecipeSHA256,
            domain.PolicyID.Value, domain.PolicyBinding.PolicyPacketSource,
            ComputePolicyBindingSHA256(domain.PolicyID.Value, domain.PolicyBinding.PolicyPacketSource),
            ComputePolicyDomainSHA256(domain), armTopologySHA256, armNeutralConfigSHA256,
            liveConfigSHA256, controlConfigSHA256, initialStateSHA256, fuelScheduleSHA256,
            eventPolicySHA256, seed, horizon, lineageNullDomain, artifacts, nullIdentities);
        registration.Validate();
        return registration;
    }

    public void Validate()
    {
        if (SchemaVersionValue is not (LegacySchemaVersion or SchemaVersion)) throw new InvalidDataException("loop-closure registration schema is unsupported");
        bool currentSchema = SchemaVersionValue == SchemaVersion;
        if (currentSchema && WriteDialectValue != WriteDialect)
            throw new InvalidDataException("loop-closure registration write dialect is unsupported");
        if (PlanID != RegisteredPlanID || PlanSHA256 != RegisteredPlanSHA256)
            throw new InvalidDataException("loop-closure registration plan identity is not registered");
        if (Seed != RegisteredSeed || Horizon != RegisteredHorizon)
            throw new InvalidDataException("loop-closure registration seed or horizon differs from the registered assay");
        if (LineageNullDomain != RegisteredLineageNullDomain) throw new InvalidDataException("loop-closure registration lineage null domain is not registered");
        if (WorldPath != RegisteredWorldPath || WorldCensusSHA256 != RegisteredWorldCensusSHA256 || WorldRecipeSHA256 != RegisteredWorldRecipeSHA256)
            throw new InvalidDataException("loop-closure registration world identity is not registered");
        RequireName(RunnerAuthority, "runner authority");
        RequireName(AdjudicatorAuthority, "adjudicator authority");
        RequireName(AppHost, "apphost");
        if (string.IsNullOrWhiteSpace(Assembly) != string.IsNullOrWhiteSpace(AssemblySHA256))
            throw new InvalidDataException("loop-closure registration assembly identity must be fully present or fully absent");
        if (!string.IsNullOrWhiteSpace(Assembly)) RequireName(Assembly, "assembly");
        RequireRelativePath(AuthorityBundlePath, "authority bundle path");
        RequireRelativePath(AuthorityBundleAppHostPath, "authority bundle apphost path");
        if (string.IsNullOrWhiteSpace(Assembly) != string.IsNullOrWhiteSpace(AuthorityBundleAssemblyPath))
            throw new InvalidDataException("loop-closure registration bundle assembly path must be fully present or fully absent");
        if (!string.IsNullOrWhiteSpace(AuthorityBundleAssemblyPath)) RequireRelativePath(AuthorityBundleAssemblyPath, "authority bundle assembly path");
        RequireDigest(AuthorityBundleCensusSHA256, "authority bundle census digest");
        if (AuthorityBundleFiles.Count == 0 || AuthorityBundleFiles.Any(static item => !item.IsValid))
            throw new InvalidDataException("loop-closure registration authority bundle manifest is empty or malformed");
        if (AuthorityBundleFiles.Select(static item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() != AuthorityBundleFiles.Count)
            throw new InvalidDataException("loop-closure registration authority bundle manifest repeats a path");
        if (!AuthorityBundleFiles.Any(item => item.RelativePath == AuthorityBundleAppHostPath)
            || !string.IsNullOrWhiteSpace(AuthorityBundleAssemblyPath) && !AuthorityBundleFiles.Any(item => item.RelativePath == AuthorityBundleAssemblyPath))
            throw new InvalidDataException("loop-closure registration authority bundle manifest omits its loaded apphost or assembly");
        if (!string.Equals(LoopClosureAuthorityBundleStore.ComputeCensus(AuthorityBundlePath, AuthorityBundleFiles), AuthorityBundleCensusSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure registration authority bundle census does not match its manifest");
        if (currentSchema)
        {
            RequireName(PolicyID, "policy ID");
            RequireText(PolicyBindingCanonical, "policy binding canonical");
            RequireDigest(PolicyBindingSHA256, "policy binding digest");
            RequireDigest(PolicySchemaSHA256, "policy schema digest");
            if (!string.Equals(PolicyBindingSHA256, ComputePolicyBindingSHA256(PolicyID, PolicyBindingCanonical), StringComparison.Ordinal))
                throw new InvalidDataException("loop-closure registration policy binding digest does not match its canonical binding");
        }
        foreach ((string value, string name) in DigestFields()) RequireDigest(value, name);
        if (Artifacts.Count == 0 || Artifacts.Any(static item => !item.IsValid))
            throw new InvalidDataException("loop-closure registration artifact manifest is empty or malformed");
        if (Artifacts.Select(static item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() != Artifacts.Count)
            throw new InvalidDataException("loop-closure registration artifact manifest repeats a path");
        string[] requiredArtifactRoles = ["plan", "source", "runner", "adjudicator", "apphost", "assembly", "world", "live", "control", "initial"];
        foreach (string role in requiredArtifactRoles)
            if (!Artifacts.Any(item => item.Role == role))
                throw new InvalidDataException($"loop-closure registration artifact manifest omits required {role} authority");
        if (NullIdentities.Count < 2 || NullIdentities.Any(static item => !item.IsValid))
            throw new InvalidDataException("loop-closure registration null identity set is empty or malformed");
        if (NullIdentities.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != NullIdentities.Count)
            throw new InvalidDataException("loop-closure registration null identity set repeats a name");
        if (!NullIdentities.Any(static item => item.Domain == RegisteredLineageNullDomain))
            throw new InvalidDataException("loop-closure registration null identity set omits the registered lineage null");
        if (NullIdentities.Select(static item => item.Domain).Distinct(StringComparer.Ordinal).Count() < 2)
            throw new InvalidDataException("loop-closure registration null identity set does not distinguish its null domains");
        if (Digest.Length != 64 || !Digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("loop-closure registration omits its digest");
        if (currentSchema && !string.Equals(ComputeDigest(this), Digest, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure registration digest does not match its typed payload");
    }

    public byte[] Encode()
    {
        Validate();
        if (SchemaVersionValue == LegacySchemaVersion && _legacyEncoded is not null) return _legacyEncoded.ToArray();
        string expected = ComputeDigest(this);
        if (!string.Equals(expected, Digest, StringComparison.Ordinal)) throw new InvalidDataException("loop-closure registration digest does not match its payload");
        byte[] first = EncodeDocument(Digest);
        byte[] second = EncodeDocument(Digest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("loop-closure registration RON encoding is nondeterministic");
        return first;
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string output = Path.GetFullPath(path);
        byte[] encoded = Encode();
        if (File.Exists(output))
        {
            if (!File.ReadAllBytes(output).AsSpan().SequenceEqual(encoded))
                throw new IOException($"loop-closure registration already exists with different bytes: {output}");
            RegistrationPath = output;
            return;
        }
        if (Directory.Exists(output)) throw new IOException($"loop-closure registration destination is a directory: {output}");
        string? parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(output, encoded);
        RegistrationPath = output;
    }

    public static LoopClosureRegistration Load(string path, IPolicyBoundaryDomain domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string input = Path.GetFullPath(path);
        if (!File.Exists(input)) throw new FileNotFoundException("loop-closure registration is missing", input);
        if (Directory.Exists(input)) throw new IOException($"loop-closure registration is a directory: {input}");
        LoopClosureRegistration registration = Decode(File.ReadAllBytes(input), domain);
        registration.RegistrationPath = input;
        return registration;
    }

    public static LoopClosureRegistration LoadLegacyHomeostat(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string input = Path.GetFullPath(path);
        if (!File.Exists(input)) throw new FileNotFoundException("loop-closure registration is missing", input);
        if (Directory.Exists(input)) throw new IOException($"loop-closure registration is a directory: {input}");
        LoopClosureRegistration registration = DecodeLegacyHomeostat(File.ReadAllBytes(input));
        registration.RegistrationPath = input;
        return registration;
    }

    /// Checks the immutable registration against the selected world and the binary that
    /// would execute it. This is deliberately a pre-arm operation: a registration that
    /// cannot be proved current must refuse before a Cortex destination is created.
    public void ValidateFrozenAuthority(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        string? root = FindRepositoryRoot();
        if (root is null) throw new InvalidDataException("loop-closure source authorities are unavailable from the current repository root");
        ValidateFrozenAuthority(Path.Combine(root, RegisteredWorldPath), domain);
    }

    public void ValidateFrozenAuthority(string corpusPath, IPolicyBoundaryDomain domain)
        => ValidateFrozenAuthority(corpusPath, RegistrationPath, domain);

    public void ValidateFrozenAuthority(string corpusPath, string? registrationPath, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        Validate();
        ValidateDomain(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusPath);
        string? root = FindRepositoryRoot();
        if (root is null) throw new InvalidDataException("loop-closure source authorities are unavailable from the current repository root");
        string registeredWorld = Path.GetFullPath(Path.Combine(root, RegisteredWorldPath));
        string selectedWorld = Path.GetFullPath(corpusPath);
        if (!string.Equals(selectedWorld, registeredWorld, StringComparison.Ordinal))
            throw new InvalidDataException($"loop-closure corpus path differs from the registered world: registered {registeredWorld}, observed {selectedWorld}");
        string census = ComputeRegisteredWorldCensusSHA256(root, WorldPath);
        if (!string.Equals(census, WorldCensusSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"loop-closure registered world census drifted: registered {WorldCensusSHA256}, observed {census}");
        string world = FileCorpus.ComputeWorldSHA256(selectedWorld, CogitoCorpus.DefaultGlob);
        if (!string.Equals(world, WorldSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"loop-closure registered world drifted: registered {WorldSHA256}, observed {world}");

        VerifyCurrentSourceAuthorities(registrationPath ?? RegistrationPath);
        LoopClosureRegistrationBuilder.ValidateCurrent(this, root, selectedWorld, domain);

        string bundleRegistrationPath = registrationPath ?? RegistrationPath
            ?? throw new InvalidDataException("loop-closure registration path is unavailable for authority bundle validation");
        LoopClosureAuthorityBundleStore.Validate(bundleRegistrationPath, AuthorityBundlePath,
            AuthorityBundleAppHostPath, AuthorityBundleAssemblyPath, AppHost, AppHostSHA256,
            Assembly, AssemblySHA256, AuthorityBundleCensusSHA256, AuthorityBundleFiles);

        RunAuthorityBinary binary = RunAuthority.CurrentBinaryIdentity();
        if (!string.Equals(binary.ProcessName, AppHost, StringComparison.Ordinal)
            || !string.Equals(binary.ProcessSHA256, AppHostSHA256, StringComparison.Ordinal)
            || !string.Equals(binary.AssemblyName, Assembly, StringComparison.Ordinal)
            || !string.Equals(binary.AssemblySHA256, AssemblySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure registration binary authority differs from the loaded apphost/assembly");
    }

    internal void ValidateDomain(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ValidatePolicyDomainIdentity(domain);
        if (SchemaVersionValue == LegacySchemaVersion)
        {
            if (domain is not HomeostatPolicyBoundaryDomain)
                throw new InvalidDataException("legacy loop-closure registration is restricted to the Homeostat policy domain");
            return;
        }
        if (!string.Equals(PolicyID, domain.PolicyID.Value, StringComparison.Ordinal)
            || !string.Equals(PolicyBindingCanonical, domain.PolicyBinding.PolicyPacketSource, StringComparison.Ordinal)
            || !string.Equals(PolicyBindingSHA256, ComputePolicyBindingSHA256(domain.PolicyID.Value, domain.PolicyBinding.PolicyPacketSource), StringComparison.Ordinal)
            || !string.Equals(PolicySchemaSHA256, ComputePolicyDomainSHA256(domain), StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure registration policy domain identity differs from the supplied domain");
    }

    private static void ValidatePolicyDomainIdentity(IPolicyBoundaryDomain domain)
    {
        domain.PolicyBinding.Validate();
        if (!domain.PolicyBinding.PolicyID.Equals(domain.PolicyID)
            || !domain.Schema.Policy.Equals(domain.PolicyID)
            || !Enum.IsDefined(domain.CanonicalStateKind)
            || !Enum.IsDefined(domain.CanonicalScopeMode)
            || !domain.SeedAuthority.IsValid
            || domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None && domain.BoundaryFeatureID == 0
            || domain.CanonicalScopeMode == PolicyCanonicalScopeModes.Enumerated && domain.CanonicalStates.Length == 0
            || domain.CanonicalScopeMode != PolicyCanonicalScopeModes.Enumerated && domain.CanonicalStates.Length != 0)
            throw new InvalidDataException("loop-closure policy domain identity is internally inconsistent");
        foreach (PolicyCanonicalStateID state in domain.CanonicalStates)
        {
            PolicyCanonicalStateID candidate = state;
            if (!domain.ValidateCanonicalState(in candidate))
                throw new InvalidDataException("loop-closure policy domain canonical-state catalog is invalid");
        }
    }

    internal static string ComputePolicyBindingSHA256(string policyID, string bindingCanonical)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', policyID, bindingCanonical, "loop-closure-policy-binding-v1"))));

    internal static string ComputePolicyDomainSHA256(IPolicyBoundaryDomain domain)
    {
        PolicyCanonicalStateID[] states = domain.CanonicalStates
            .OrderBy(static state => state)
            .ToArray();
        string catalog = string.Join(',', states.Select(static state => string.Join(':',
            state.Policy.Value, (byte)state.Kind, state.Version, state.Value.ToString("X16", System.Globalization.CultureInfo.InvariantCulture))));
        string catalogSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(catalog)));
        string canonical = string.Join('|', domain.Schema.Policy.Value, domain.Schema.FeatureCount,
            domain.Schema.ActionCount, domain.Schema.OutcomeCount, domain.Schema.ModeCeiling,
            domain.Schema.Admission, (byte)domain.CanonicalStateKind, (byte)domain.CanonicalScopeMode,
            domain.BoundaryFeatureID,
            (byte)domain.SeedAuthority.CandidateAuthority,
            (byte)domain.SeedAuthority.ForcedNullAuthority,
            (byte)domain.SeedAuthority.CandidateSelectionCause,
            (byte)domain.SeedAuthority.ForcedNullSelectionCause,
            catalogSHA256, "loop-closure-policy-domain-v3");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string ComputeArmTopologySHA256(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        domain.ArmTopology.Validate();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "arm-topology-v1", "live", domain.ArmTopology.LiveAuthorityCeiling,
            domain.ArmTopology.LiveProcessCatalog, domain.ArmTopology.LiveRung0,
            domain.ArmTopology.LiveDeliberation, domain.ArmTopology.TrialAllocationAuthority,
            domain.ArmTopology.TrialArmSteps, domain.ArmTopology.TrialAllocationIdentity,
            "control", domain.ArmTopology.ControlAuthority, domain.ArmTopology.ControlProcessCatalog,
            domain.ArmTopology.ControlRung0, domain.ArmTopology.ControlDeliberation,
            0, "", "typed-arm-topology"))));
    }

    private void VerifyCurrentSourceAuthorities(string? registrationPath)
    {
        string? root = FindRepositoryRoot();
        if (root is null) throw new InvalidDataException("loop-closure source authorities are unavailable from the current repository root");
        string observedBody = ComputeSourceTreeCensusBody(root);
        string sourceTree = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(observedBody)));
        if (!string.Equals(sourceTree, SourceTreeSHA256, StringComparison.Ordinal))
            throw new InvalidDataException(DescribeSourceTreeDrift(observedBody, sourceTree, registrationPath));
        VerifySourceArtifact(Path.Combine(root, "docs", "plans", "loop-closure-birth-certificate-v2.md"), "plan/docs/plans/loop-closure-birth-certificate-v2.md");
        VerifySourceArtifact(Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRegistration.cs"), "source/src/Cogito/Drive/LoopClosureRegistration.cs");
        VerifySourceArtifact(Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRunner.cs"), "runner/src/Cogito/Drive/LoopClosureRunner.cs");
        VerifySourceArtifact(Path.Combine(root, "src", "Cogito", "Drive", "LoopClosureRunner.cs"), "adjudicator/src/Cogito/Drive/LoopClosureRunner.cs");
    }

    private void VerifySourceArtifact(string path, string relativePath)
    {
        LoopClosureArtifactManifestEntry artifact = Artifacts.SingleOrDefault(item => item.RelativePath == relativePath);
        if (!artifact.IsValid || !File.Exists(path)
            || !string.Equals(artifact.SHA256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparison.Ordinal))
            throw new InvalidDataException($"loop-closure source authority drifted: {relativePath}");
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(Environment.CurrentDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Cogito", "Drive", "LoopClosureRunner.cs"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    /// Hashes the registered path/content census exactly as the pre-registered shell
    /// receipt: sorted relative paths, one sha256sum line per file, then a sha256 over
    /// that text. This is deliberately separate from FileCorpus's framed runtime hash.
    internal static string ComputeRegisteredWorldCensusSHA256(string repositoryRoot, string worldPath)
    {
        if (!string.Equals(worldPath, RegisteredWorldPath, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure world path differs from the registered census path");
        string root = Path.GetFullPath(repositoryRoot);
        string world = Path.GetFullPath(Path.Combine(root, worldPath));
        if (!Directory.Exists(world)) throw new DirectoryNotFoundException($"registered world census path is missing: {world}");
        List<string> files = Directory.GetFiles(world, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();
        using IncrementalHash census = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            string relative = NormalizeCensusPath(Path.GetRelativePath(root, file));
            string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            byte[] line = Encoding.UTF8.GetBytes($"{digest}  {relative}\n");
            census.AppendData(line);
        }
        return Convert.ToHexStringLower(census.GetHashAndReset());
    }

    private static string NormalizeCensusPath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    /// Hashes every source file below src by exact relative path and bytes. This is
    /// independent of git state: untracked source participates, while conventional
    /// compiler-output subtrees do not. The same census is recomputed before both arms
    /// and certification.
    internal static string ComputeSourceTreeSHA256(string repositoryRoot)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ComputeSourceTreeCensusBody(repositoryRoot))));

    /// The exact `digest␠␠path\n` census text that ComputeSourceTreeSHA256 hashes — its byte-identical
    /// preimage. Persisted at mint as a sidecar so a later drift can be diffed to the offending path
    /// instead of leaving a bare registered/observed digest pair. The sidecar's own SHA256 IS
    /// SourceTreeSHA256, so it needs no separate integrity field.
    internal static string ComputeSourceTreeCensusBody(string repositoryRoot)
    {
        string root = Path.GetFullPath(repositoryRoot);
        string source = Path.Combine(root, "src");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"loop-closure source tree is missing: {source}");
        List<string> files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutputPath(source, file))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();
        StringBuilder body = new();
        foreach (string file in files)
        {
            string relative = NormalizeCensusPath(Path.GetRelativePath(root, file));
            string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            body.Append(digest).Append("  ").Append(relative).Append('\n');
        }
        return body.ToString();
    }

    public const string SourceCensusFileName = "loop-closure-source-census.txt";

    /// Write the mint-time source census next to the registration, so a drift refusal can name the
    /// first divergent file. Idempotent: an existing sidecar with matching bytes is left alone.
    internal void WriteSourceCensusSidecar(string registrationPath, string censusBody)
    {
        string sidecar = SourceCensusSidecarPath(registrationPath);
        byte[] bytes = Encoding.UTF8.GetBytes(censusBody);
        if (File.Exists(sidecar) && File.ReadAllBytes(sidecar).AsSpan().SequenceEqual(bytes)) return;
        File.WriteAllBytes(sidecar, bytes);
    }

    private static string SourceCensusSidecarPath(string registrationPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(registrationPath));
        return dir is null ? SourceCensusFileName : Path.Combine(dir, SourceCensusFileName);
    }

    /// Name the first path whose digest was added, removed, or changed relative to the registered
    /// census, when the mint-time sidecar is present and verifies; otherwise fall back to the digest
    /// pair. This turns ":source tree drifted: registered X observed Y" into an actionable locator.
    private string DescribeSourceTreeDrift(string observedBody, string observedDigest, string? registrationPath)
    {
        string bare = $"loop-closure source tree drifted: registered {SourceTreeSHA256}, observed {observedDigest}";
        if (string.IsNullOrWhiteSpace(registrationPath)) return bare;
        string sidecar = SourceCensusSidecarPath(registrationPath);
        if (!File.Exists(sidecar)) return bare;
        byte[] bytes = File.ReadAllBytes(sidecar);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), SourceTreeSHA256, StringComparison.Ordinal))
            return bare;
        string? drift = FirstCensusDrift(Encoding.UTF8.GetString(bytes), observedBody);
        return drift is null ? bare : $"{bare}; first divergence: {drift}";
    }

    private static string? FirstCensusDrift(string registeredBody, string observedBody)
    {
        Dictionary<string, string> registered = ParseCensusBody(registeredBody);
        Dictionary<string, string> observed = ParseCensusBody(observedBody);
        foreach (string path in registered.Keys.Concat(observed.Keys).Distinct(StringComparer.Ordinal).OrderBy(static p => p, StringComparer.Ordinal))
        {
            bool inRegistered = registered.TryGetValue(path, out string? registeredDigest);
            bool inObserved = observed.TryGetValue(path, out string? observedDigest);
            if (inRegistered && !inObserved) return $"removed {path}";
            if (!inRegistered && inObserved) return $"added {path}";
            if (!string.Equals(registeredDigest, observedDigest, StringComparison.Ordinal)) return $"changed {path}";
        }
        return null;
    }

    private static Dictionary<string, string> ParseCensusBody(string body)
    {
        Dictionary<string, string> census = new(StringComparer.Ordinal);
        foreach (string line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int gap = line.IndexOf("  ", StringComparison.Ordinal);
            if (gap < 0) continue;
            census[line[(gap + 2)..]] = line[..gap];
        }
        return census;
    }

    private static bool IsBuildOutputPath(string sourceRoot, string file)
    {
        string relative = Path.GetRelativePath(sourceRoot, file);
        ReadOnlySpan<char> remaining = relative;
        while (true)
        {
            int separator = remaining.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (separator < 0) return false;
            ReadOnlySpan<char> segment = remaining[..separator];
            if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)) return true;
            remaining = remaining[(separator + 1)..];
        }
    }

    internal static bool VerifySourceTreeCensusFixture(TextWriter output)
    {
        string root = Path.GetFullPath(Path.Combine(".tmp", $"loop-closure-source-census-{Guid.NewGuid():N}"));
        string source = Path.Combine(root, "src", "Cogito");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Program.cs"), "source-v1");
            string initial = ComputeSourceTreeSHA256(root);

            string obj = Path.Combine(source, "obj", "Debug", "net10.0");
            string bin = Path.Combine(source, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(obj);
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(obj, "Cogito.dll"), "compiler-residue-v1");
            File.WriteAllText(Path.Combine(bin, "Cogito"), "apphost-residue-v1");
            bool buildResidueIgnored = ComputeSourceTreeSHA256(root) == initial;
            File.WriteAllText(Path.Combine(obj, "Cogito.dll"), "compiler-residue-v2");
            bool buildResidueMutationIgnored = ComputeSourceTreeSHA256(root) == initial;

            File.WriteAllText(Path.Combine(source, "UntrackedSource.cs"), "untracked-source");
            bool untrackedSourceBound = ComputeSourceTreeSHA256(root) != initial;
            bool pass = buildResidueIgnored && buildResidueMutationIgnored && untrackedSourceBound;
            output.WriteLine($"  loop-closure source census fixture · build-residue={(buildResidueIgnored && buildResidueMutationIgnored ? "ignored" : "BOUND")} · untracked-source={(untrackedSourceBound ? "bound" : "IGNORED")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public void WriteArmAuthority(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        byte[] bytes = Encode();
        string path = run.PathOf(AuthorityFileName);
        if (File.Exists(path) && !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            throw new IOException($"loop-closure arm registration differs from frozen authority: {path}");
        if (Directory.Exists(path)) throw new IOException($"loop-closure arm registration is a directory: {path}");
        if (!File.Exists(path)) run.Write(AuthorityFileName, bytes);
    }

    public void AssertPersistedBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string input = Path.GetFullPath(path);
        if (!File.Exists(input) || !Encode().AsSpan().SequenceEqual(File.ReadAllBytes(input)))
            throw new InvalidDataException("loop-closure registration bytes drifted after pre-arm sealing");
        RegistrationPath = input;
    }

    public const string AuthorityFileName = "loop-closure-registration.ron";

    public static LoopClosureRegistration Decode(ReadOnlySpan<byte> bytes, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        LoopClosureRegistrationRON document = RonSerializer.Deserialize<LoopClosureRegistrationRON>(bytes);
        if (document.schemaVersion == LegacySchemaVersion)
            throw new InvalidDataException("legacy loop-closure registration requires explicit Homeostat-only decoding");
        LoopClosureRegistration registration = new(document);
        registration.Validate();
        registration.ValidateDomain(domain);
        if (!string.Equals(ComputeDigest(registration), registration.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure registration digest does not match its payload");
        if (!registration.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("loop-closure registration RON round-trip changed bytes");
        return registration;
    }

    public static LoopClosureRegistration DecodeLegacyHomeostat(ReadOnlySpan<byte> bytes)
    {
        LoopClosureRegistrationRON document = RonSerializer.Deserialize<LoopClosureRegistrationRON>(bytes);
        if (document.schemaVersion != LegacySchemaVersion)
            throw new InvalidDataException("legacy loop-closure registration decoder accepts schema v3 only");
        LoopClosureRegistration registration = new(document);
        registration._legacyEncoded = bytes.ToArray();
        registration.Validate();
        registration.ValidateDomain(HomeostatPolicyBoundaryDomain.Instance);
        if (!registration.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("legacy loop-closure registration bytes changed during decode");
        return registration;
    }

    private static string ComputeDigest(LoopClosureRegistration registration)
        => Convert.ToHexStringLower(SHA256.HashData(registration.EncodeDocument("")));

    private byte[] EncodeDocument(string digest)
    {
        LoopClosureRegistrationRON document = new()
        {
            schemaVersion = SchemaVersionValue, planID = PlanID, planSHA256 = PlanSHA256,
            sourceTreeSHA256 = SourceTreeSHA256,
            runnerAuthority = RunnerAuthority, runnerAuthoritySHA256 = RunnerAuthoritySHA256,
            adjudicatorAuthority = AdjudicatorAuthority, adjudicatorAuthoritySHA256 = AdjudicatorAuthoritySHA256,
            appHost = AppHost, appHostSHA256 = AppHostSHA256, assembly = Assembly, assemblySHA256 = AssemblySHA256,
            authorityBundlePath = AuthorityBundlePath, authorityBundleAppHostPath = AuthorityBundleAppHostPath,
            authorityBundleAssemblyPath = AuthorityBundleAssemblyPath, authorityBundleCensusSHA256 = AuthorityBundleCensusSHA256,
            worldPath = WorldPath, worldCensusSHA256 = WorldCensusSHA256, worldSHA256 = WorldSHA256, worldRecipeSHA256 = WorldRecipeSHA256,
            policyID = PolicyID, policyBindingCanonical = PolicyBindingCanonical, policyBindingSHA256 = PolicyBindingSHA256,
            policySchemaSHA256 = PolicySchemaSHA256, writeDialect = WriteDialectValue,
            armTopologySHA256 = ArmTopologySHA256, armNeutralConfigSHA256 = ArmNeutralConfigSHA256,
            liveConfigSHA256 = LiveConfigSHA256, controlConfigSHA256 = ControlConfigSHA256,
            initialStateSHA256 = InitialStateSHA256, fuelScheduleSHA256 = FuelScheduleSHA256,
            eventPolicySHA256 = EventPolicySHA256, seed = Seed, horizon = Horizon,
            lineageNullDomain = LineageNullDomain, digest = digest,
        };
        foreach (LoopClosureArtifactManifestEntry artifact in Artifacts)
            document.artifacts.Add(new LoopClosureArtifactManifestEntryRON { relativePath = artifact.RelativePath, sha256 = artifact.SHA256 });
        foreach (LoopClosureNullIdentity identity in NullIdentities)
            document.nullIdentities.Add(new LoopClosureNullIdentityRON { name = identity.Name, domain = identity.Domain, sha256 = identity.SHA256 });
        foreach (LoopClosureAuthorityBundleFile file in AuthorityBundleFiles)
            document.authorityBundleFiles.Add(new LoopClosureAuthorityBundleFileRON { relativePath = file.RelativePath, sha256 = file.SHA256 });
        return RonSerializer.SerializeToUtf8(in document);
    }

    private IEnumerable<(string Value, string Name)> DigestFields()
    {
        yield return (PlanSHA256, "plan digest");
        yield return (SourceTreeSHA256, "source tree digest"); yield return (RunnerAuthoritySHA256, "runner authority digest");
        yield return (AdjudicatorAuthoritySHA256, "adjudicator authority digest"); yield return (AppHostSHA256, "apphost digest");
        if (!string.IsNullOrWhiteSpace(AssemblySHA256)) yield return (AssemblySHA256, "assembly digest");
        yield return (WorldCensusSHA256, "world census digest"); yield return (WorldSHA256, "world digest");
        yield return (WorldRecipeSHA256, "world recipe digest"); yield return (ArmTopologySHA256, "arm topology digest");
        if (SchemaVersionValue == SchemaVersion)
        {
            yield return (PolicyBindingSHA256, "policy binding digest");
            yield return (PolicySchemaSHA256, "policy schema digest");
        }
        yield return (ArmNeutralConfigSHA256, "arm-neutral config digest"); yield return (LiveConfigSHA256, "live config digest");
        yield return (ControlConfigSHA256, "control config digest"); yield return (InitialStateSHA256, "initial state digest");
        yield return (FuelScheduleSHA256, "fuel schedule digest"); yield return (EventPolicySHA256, "event policy digest");
        yield return (AuthorityBundleCensusSHA256, "authority bundle census digest");
    }

    private static void RequireText(string value, string field)
    { if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"loop-closure registration omits {field}"); }
    private static void RequireName(string value, string field)
    { RequireText(value, field); if (value.Contains('/') || value.Contains('\\')) throw new InvalidDataException($"loop-closure registration has invalid {field}"); }
    private static void RequireDigest(string value, string field)
    { if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException($"loop-closure registration has invalid {field}"); }
    private static void RequireRelativePath(string value, string field)
    {
        RequireText(value, field);
        if (Path.IsPathRooted(value) || value.Contains('\\') || value.Split('/').Any(static part => part is "" or "." or ".."))
            throw new InvalidDataException($"loop-closure registration has invalid {field}");
    }
}

public readonly record struct LoopClosureArtifactManifestEntry(string RelativePath, string SHA256)
{
    public string Role => RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    public bool IsValid => !string.IsNullOrWhiteSpace(RelativePath) && !RelativePath.Contains('\\') && !Path.IsPathRooted(RelativePath)
        && RelativePath.Split('/').All(static part => part is not "" and not "." and not "..")
        && SHA256.Length == 64 && SHA256.All(Uri.IsHexDigit);
}

public readonly record struct LoopClosureNullIdentity(string Name, string Domain, string SHA256)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Domain)
        && SHA256.Length == 64 && SHA256.All(Uri.IsHexDigit);
}

[RonObject]
internal partial class LoopClosureRegistrationRON
{
    public int schemaVersion;
    public string planID = "";
    public string planSHA256 = "";
    public string sourceTreeSHA256 = "";
    public string runnerAuthority = "";
    public string runnerAuthoritySHA256 = "";
    public string adjudicatorAuthority = "";
    public string adjudicatorAuthoritySHA256 = "";
    public string appHost = "";
    public string appHostSHA256 = "";
    public string assembly = "";
    public string assemblySHA256 = "";
    public string authorityBundlePath = "";
    public string authorityBundleAppHostPath = "";
    public string authorityBundleAssemblyPath = "";
    public string authorityBundleCensusSHA256 = "";
    public List<LoopClosureAuthorityBundleFileRON> authorityBundleFiles = new();
    public string worldPath = "";
    public string worldCensusSHA256 = "";
    public string worldSHA256 = "";
    public string worldRecipeSHA256 = "";
    public string policyID = "";
    public string policyBindingCanonical = "";
    public string policyBindingSHA256 = "";
    public string policySchemaSHA256 = "";
    public string writeDialect = "";
    public string armTopologySHA256 = "";
    public string armNeutralConfigSHA256 = "";
    public string liveConfigSHA256 = "";
    public string controlConfigSHA256 = "";
    public string initialStateSHA256 = "";
    public string fuelScheduleSHA256 = "";
    public string eventPolicySHA256 = "";
    public ulong seed;
    public int horizon;
    public string lineageNullDomain = "";
    public List<LoopClosureArtifactManifestEntryRON> artifacts = new();
    public List<LoopClosureNullIdentityRON> nullIdentities = new();
    public string digest = "";
}

[RonObject]
internal partial class LoopClosureArtifactManifestEntryRON
{
    public string relativePath = "";
    public string sha256 = "";
}

[RonObject]
internal partial class LoopClosureNullIdentityRON
{
    public string name = "";
    public string domain = "";
    public string sha256 = "";
}

[RonObject]
internal partial class LoopClosureAuthorityBundleFileRON
{
    public string relativePath = "";
    public string sha256 = "";
}
