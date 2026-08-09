namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// Immutable R20 authority for the causal world-novelty triad.  R19 remains a
/// separate sealed protocol; this registration owns only the stationary,
/// epoch-live, and epoch-order-null world assay.
public sealed class WorldNoveltyRegistration
{
    public const int SchemaVersion = 1;
    public const string RegisteredAssayID = "r20-world-novelty-causal-triad-v1";
    public const ulong RegisteredSeed = 0xC0117011UL;
    public const int RegisteredHorizon = 500;
    public const string ReportSpecies = "WorldNoveltyReport";
    public const string TitleClause = "WorldNoveltyReport only; no ClosureCertificate";
    public const string R21Continuation = "selected-world-handoff-to-r21";
    public const string RegisteredDietSelectorID = "source-occurrence-sha256-v1";
    public const int RegisteredDietDomainCount = 4;
    public const int RegisteredDietLinesPerDomain = 96;
    public const int RegisteredDietSourceOccurrences = 384;

    private WorldNoveltyRegistration(WorldNoveltyRegistrationRON document)
    {
        SchemaVersionValue = document.schemaVersion;
        AssayID = document.assayID;
        Seed = document.seed;
        Horizon = document.horizon;
        WorldPath = document.worldPath;
        LatticeCensusPath = document.latticeCensusPath;
        LatticeCensusSHA256 = document.latticeCensusSHA256;
        WorldSHA256 = document.worldSHA256;
        PayloadMultisetSHA256 = document.payloadMultisetSHA256;
        SourceNativeAuthorityPath = document.sourceNativeAuthorityPath;
        SourceNativeAuthoritySHA256 = document.sourceNativeAuthoritySHA256;
        DietLineAuthorityPath = document.dietLineAuthorityPath;
        DietLineAuthoritySHA256 = document.dietLineAuthoritySHA256;
        DietSelectorID = document.dietSelectorID;
        DietDomainCount = document.dietDomainCount;
        DietLinesPerDomain = document.dietLinesPerDomain;
        DietSourceOccurrences = document.dietSourceOccurrences;
        Schedules = document.schedules.Select(static item => new WorldNoveltyScheduleAuthority(
            item.role, item.scheduleID, item.order, item.worldSHA256, item.payloadMultisetSHA256,
            item.scheduleSHA256, item.prefixSHA256, item.prefixAfter)).ToList();
        Configs = document.configs.Select(static item => new WorldNoveltyConfigAuthority(
            item.arm, item.configSHA256, item.schemaSHA256)).ToList();
        OfferedFuelVectorSHA256 = document.offeredFuelVectorSHA256;
        Equalization = new WorldNoveltyEqualizationContract(
            document.equalization.offeredFuelVectorSHA256,
            document.equalization.seed,
            document.equalization.horizon,
            document.equalization.requireSharedPayloadMultiset,
            document.equalization.requireActualSpendEquality,
            document.equalization.requireRefundEquality,
            document.equalization.digest);
        Contrast = new WorldNoveltyContrastContract(
            document.contrast.equalityTolerance,
            document.contrast.minimumDirectionalEffect,
            document.contrast.minimumArmDivergences,
            document.contrast.digest);
        OpportunityFloor = new WorldNoveltyOpportunityFloor(
            document.opportunityFloor.boundarySteps,
            document.opportunityFloor.boundaryPrefixes,
            document.opportunityFloor.windowK,
            document.opportunityFloor.minimumPaidComparisonsPerBoundary,
            document.opportunityFloor.digest);
        AppHost = document.appHost;
        AppHostSHA256 = document.appHostSHA256;
        Assembly = document.assembly;
        AssemblySHA256 = document.assemblySHA256;
        SchemaSHA256 = document.schemaSHA256;
        RegistrationAuthoritySHA256 = document.registrationAuthoritySHA256;
        ReportSpeciesValue = document.reportSpecies;
        TitleClauseValue = document.titleClause;
        R21ContinuationValue = document.r21Continuation;
        Digest = document.digest;
    }

    private WorldNoveltyRegistration(
        string assayID,
        ulong seed,
        int horizon,
        string worldPath,
        string latticeCensusPath,
        string latticeCensusSHA256,
        string worldSHA256,
        string payloadMultisetSHA256,
        string sourceNativeAuthorityPath,
        string sourceNativeAuthoritySHA256,
        string dietLineAuthorityPath,
        string dietLineAuthoritySHA256,
        string dietSelectorID,
        int dietDomainCount,
        int dietLinesPerDomain,
        int dietSourceOccurrences,
        IReadOnlyList<WorldNoveltyScheduleAuthority> schedules,
        IReadOnlyList<WorldNoveltyConfigAuthority> configs,
        string offeredFuelVectorSHA256,
        WorldNoveltyEqualizationContract equalization,
        WorldNoveltyOpportunityFloor opportunityFloor,
        WorldNoveltyContrastContract contrast,
        string appHost,
        string appHostSHA256,
        string assembly,
        string assemblySHA256,
        string schemaSHA256,
        string registrationAuthoritySHA256)
    {
        SchemaVersionValue = SchemaVersion;
        AssayID = assayID;
        Seed = seed;
        Horizon = horizon;
        WorldPath = worldPath;
        LatticeCensusPath = latticeCensusPath;
        LatticeCensusSHA256 = latticeCensusSHA256;
        WorldSHA256 = worldSHA256;
        PayloadMultisetSHA256 = payloadMultisetSHA256;
        SourceNativeAuthorityPath = sourceNativeAuthorityPath;
        SourceNativeAuthoritySHA256 = sourceNativeAuthoritySHA256;
        DietLineAuthorityPath = dietLineAuthorityPath;
        DietLineAuthoritySHA256 = dietLineAuthoritySHA256;
        DietSelectorID = dietSelectorID;
        DietDomainCount = dietDomainCount;
        DietLinesPerDomain = dietLinesPerDomain;
        DietSourceOccurrences = dietSourceOccurrences;
        Schedules = schedules.ToList();
        Configs = configs.ToList();
        OfferedFuelVectorSHA256 = offeredFuelVectorSHA256;
        Equalization = equalization;
        OpportunityFloor = opportunityFloor;
        Contrast = contrast;
        AppHost = appHost;
        AppHostSHA256 = appHostSHA256;
        Assembly = assembly;
        AssemblySHA256 = assemblySHA256;
        SchemaSHA256 = schemaSHA256;
        RegistrationAuthoritySHA256 = registrationAuthoritySHA256;
        ReportSpeciesValue = ReportSpecies;
        TitleClauseValue = TitleClause;
        R21ContinuationValue = R21Continuation;
        Digest = ComputeDigest(this);
    }

    public int SchemaVersionValue { get; }
    public string AssayID { get; }
    public ulong Seed { get; }
    public int Horizon { get; }
    public string WorldPath { get; }
    public string LatticeCensusPath { get; }
    public string LatticeCensusSHA256 { get; }
    public string WorldSHA256 { get; }
    public string PayloadMultisetSHA256 { get; }
    public string SourceNativeAuthorityPath { get; }
    public string SourceNativeAuthoritySHA256 { get; }
    public string DietLineAuthorityPath { get; }
    public string DietLineAuthoritySHA256 { get; }
    public string DietSelectorID { get; }
    public int DietDomainCount { get; }
    public int DietLinesPerDomain { get; }
    public int DietSourceOccurrences { get; }
    public IReadOnlyList<WorldNoveltyScheduleAuthority> Schedules { get; }
    public IReadOnlyList<WorldNoveltyConfigAuthority> Configs { get; }
    public string OfferedFuelVectorSHA256 { get; }
    public WorldNoveltyEqualizationContract Equalization { get; }
    public WorldNoveltyOpportunityFloor OpportunityFloor { get; }
    public WorldNoveltyContrastContract Contrast { get; }
    public string AppHost { get; }
    public string AppHostSHA256 { get; }
    public string Assembly { get; }
    public string AssemblySHA256 { get; }
    public string SchemaSHA256 { get; }
    public string RegistrationAuthoritySHA256 { get; }
    public string ReportSpeciesValue { get; }
    public string TitleClauseValue { get; }
    public string R21ContinuationValue { get; }
    public string Digest { get; }
    public string RegistrationDigest => Digest;
    public string WorldCensusSHA256 => LatticeCensusSHA256;
    public string SchemaDigest => SchemaSHA256;
    public WorldNoveltyEqualizationContract EqualizationContract => Equalization;

    public WorldNoveltyScheduleAuthority StationarySchedule => GetSchedule("control-stationary");
    public WorldNoveltyScheduleAuthority EpochSchedule => GetSchedule("live-epoch");
    public WorldNoveltyScheduleAuthority EpochOrderNullSchedule => GetSchedule("null-epoch-order");
    public WorldNoveltyConfigAuthority EpochConfig => GetConfig("epoch");
    public WorldNoveltyConfigAuthority StationaryConfig => GetConfig("stationary");
    public WorldNoveltyConfigAuthority OrderNullConfig => GetConfig("order-null");

    public static WorldNoveltyRegistration Create(
        string worldPath,
        string latticeCensusPath,
        string latticeCensusSHA256,
        string worldSHA256,
        string payloadMultisetSHA256,
        string sourceNativeAuthorityPath,
        string sourceNativeAuthoritySHA256,
        string dietLineAuthorityPath,
        string dietLineAuthoritySHA256,
        string dietSelectorID,
        int dietDomainCount,
        int dietLinesPerDomain,
        int dietSourceOccurrences,
        IReadOnlyList<WorldNoveltyScheduleAuthority> schedules,
        IReadOnlyList<WorldNoveltyConfigAuthority> configs,
        string offeredFuelVectorSHA256,
        WorldNoveltyEqualizationContract equalization,
        WorldNoveltyOpportunityFloor opportunityFloor,
        string appHost,
        string appHostSHA256,
        string assembly,
        string assemblySHA256,
        string schemaSHA256,
        string registrationAuthoritySHA256,
        ulong seed = RegisteredSeed,
        int horizon = RegisteredHorizon,
        WorldNoveltyContrastContract? contrast = null)
    {
        WorldNoveltyContrastContract registeredContrast = contrast ?? WorldNoveltyContrastContract.Create();
        WorldNoveltyRegistration registration = new(RegisteredAssayID, seed, horizon, worldPath,
            latticeCensusPath, latticeCensusSHA256, worldSHA256, payloadMultisetSHA256,
            sourceNativeAuthorityPath, sourceNativeAuthoritySHA256, dietLineAuthorityPath, dietLineAuthoritySHA256,
            dietSelectorID, dietDomainCount, dietLinesPerDomain, dietSourceOccurrences, schedules, configs,
            offeredFuelVectorSHA256, equalization, opportunityFloor, registeredContrast, appHost, appHostSHA256, assembly,
            assemblySHA256, schemaSHA256, registrationAuthoritySHA256);
        registration.Validate();
        return registration;
    }

    public void Validate()
    {
        if (SchemaVersionValue != SchemaVersion || AssayID != RegisteredAssayID)
            throw new InvalidDataException("R20 world-novelty registration schema or assay is not registered");
        if (Seed != RegisteredSeed || Horizon != RegisteredHorizon || Horizon <= 0)
            throw new InvalidDataException("R20 world-novelty registration seed or horizon differs from the registered assay");
        RequireRelativePath(WorldPath, "world path");
        RequireRelativePath(LatticeCensusPath, "lattice census path");
        RequireRelativePath(SourceNativeAuthorityPath, "source-native authority path");
        RequireRelativePath(DietLineAuthorityPath, "diet line authority path");
        RequireSidecarOutsideWorld(SourceNativeAuthorityPath, "source-native authority path");
        RequireSidecarOutsideWorld(DietLineAuthorityPath, "diet line authority path");
        if (string.Equals(SourceNativeAuthorityPath, DietLineAuthorityPath, StringComparison.Ordinal))
            throw new InvalidDataException("R20 source-native and diet authority paths must be distinct");
        if (!string.Equals(DietSelectorID, RegisteredDietSelectorID, StringComparison.Ordinal)
            || DietDomainCount != RegisteredDietDomainCount
            || DietLinesPerDomain != RegisteredDietLinesPerDomain
            || DietSourceOccurrences != RegisteredDietSourceOccurrences)
            throw new InvalidDataException("R20 diet authority selector or dimensions differ from the registered projection");
        foreach ((string value, string name) in DigestFields()) RequireDigest(value, name);
        RequireName(AppHost, "apphost");
        RequireName(Assembly, "assembly");
        if (ReportSpeciesValue != ReportSpecies || TitleClauseValue != TitleClause)
            throw new InvalidDataException("R20 registration title is not restricted to WorldNoveltyReport");
        if (R21ContinuationValue != R21Continuation)
            throw new InvalidDataException("R20 registration continuation does not name the selected-world handoff");
        OpportunityFloor.Validate(Horizon);
        Contrast.Validate();
        if (Schedules.Count != 3 || Schedules.Any(static schedule => !schedule.IsValid))
            throw new InvalidDataException("R20 registration does not carry exactly three valid schedule authorities");
        if (Schedules.Select(static schedule => schedule.Role).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("R20 registration repeats a schedule role");
        if (Schedules.Any(schedule => schedule.WorldSHA256 != WorldSHA256 || schedule.PayloadMultisetSHA256 != PayloadMultisetSHA256))
            throw new InvalidDataException("R20 schedule authority is not bound to the shared world bytes and payload multiset");
        if (StationarySchedule.ScheduleID != AdmissionCursor.ScheduleID
            || StationarySchedule.Order != "stationary-roundrobin"
            || !EpochSchedule.ScheduleID.StartsWith(WorldEpochSchedule.ScheduleID + ":", StringComparison.Ordinal)
            || !EpochOrderNullSchedule.ScheduleID.StartsWith(WorldEpochSchedule.ScheduleID + ":", StringComparison.Ordinal)
            || EpochSchedule.Order == EpochOrderNullSchedule.Order)
            throw new InvalidDataException("R20 schedule authority does not describe stationary, epoch, and deranged epoch-order arms");
        if (!StationarySchedule.PrefixAfter.SequenceEqual(OpportunityFloor.BoundaryPrefixes)
            || !EpochSchedule.PrefixAfter.SequenceEqual(OpportunityFloor.BoundaryPrefixes)
            || !EpochOrderNullSchedule.PrefixAfter.SequenceEqual(OpportunityFloor.BoundaryPrefixes))
            throw new InvalidDataException("R20 opportunity boundaries disagree with the epoch schedule prefix chain");
        if (StationarySchedule.ScheduleSHA256 == EpochSchedule.ScheduleSHA256
            || EpochSchedule.ScheduleSHA256 == EpochOrderNullSchedule.ScheduleSHA256
            || StationarySchedule.ScheduleSHA256 == EpochOrderNullSchedule.ScheduleSHA256)
            throw new InvalidDataException("R20 causal-triad schedule authorities are not distinct");
        if (Configs.Count != 3 || Configs.Any(static config => !config.IsValid))
            throw new InvalidDataException("R20 registration does not carry exactly three valid arm configs");
        if (Configs.Select(static config => config.Arm).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("R20 registration repeats an arm config");
        if (Configs.Any(config => config.SchemaSHA256 != SchemaSHA256))
            throw new InvalidDataException("R20 arm config is not bound to the registration schema authority");
        if (Configs.Select(static config => config.ConfigSHA256).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("R20 arm config digests are not equalized");
        Equalization.Validate(Seed, Horizon, PayloadMultisetSHA256, OfferedFuelVectorSHA256);
        // Equalization is about what was offered, not what happened to be spent.
        // Actual spend and refunds are run evidence and deliberately absent here.
        if (Digest.Length != 64 || !Digest.All(Uri.IsHexDigit) || ComputeDigest(this) != Digest)
            throw new InvalidDataException("R20 registration digest does not match its typed payload");
    }

    public byte[] Encode()
    {
        Validate();
        byte[] first = EncodeDocument(Digest);
        byte[] second = EncodeDocument(Digest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("R20 registration RON encoding is nondeterministic");
        return first;
    }

    public static WorldNoveltyRegistration Decode(ReadOnlySpan<byte> bytes)
    {
        WorldNoveltyRegistration registration = new(RonSerializer.Deserialize<WorldNoveltyRegistrationRON>(bytes));
        registration.Validate();
        if (!registration.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("R20 registration RON round-trip changed bytes");
        return registration;
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string output = Path.GetFullPath(path);
        byte[] bytes = Encode();
        if (File.Exists(output))
        {
            if (!File.ReadAllBytes(output).AsSpan().SequenceEqual(bytes)) throw new IOException($"R20 registration already exists with different bytes: {output}");
            return;
        }
        string? parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(output, bytes);
    }

    public static WorldNoveltyRegistration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Decode(File.ReadAllBytes(Path.GetFullPath(path)));
    }

    private WorldNoveltyScheduleAuthority GetSchedule(string role)
        => Schedules.Single(schedule => schedule.Role == role);

    private WorldNoveltyConfigAuthority GetConfig(string arm)
        => Configs.Single(config => config.Arm == arm);

    private IEnumerable<(string Value, string Name)> DigestFields()
    {
        yield return (LatticeCensusSHA256, "lattice census digest");
        yield return (WorldSHA256, "world digest");
        yield return (PayloadMultisetSHA256, "payload multiset digest");
        yield return (SourceNativeAuthoritySHA256, "source-native authority digest");
        yield return (DietLineAuthoritySHA256, "diet line authority digest");
        yield return (OfferedFuelVectorSHA256, "offered fuel vector digest");
        yield return (AppHostSHA256, "apphost digest");
        yield return (AssemblySHA256, "assembly digest");
        yield return (SchemaSHA256, "schema digest");
        yield return (RegistrationAuthoritySHA256, "registration authority digest");
    }

    private byte[] EncodeDocument(string digest)
    {
        WorldNoveltyRegistrationRON document = new()
        {
            schemaVersion = SchemaVersionValue, assayID = AssayID, seed = Seed, horizon = Horizon,
            worldPath = WorldPath, latticeCensusPath = LatticeCensusPath, latticeCensusSHA256 = LatticeCensusSHA256, worldSHA256 = WorldSHA256,
            payloadMultisetSHA256 = PayloadMultisetSHA256, offeredFuelVectorSHA256 = OfferedFuelVectorSHA256,
            sourceNativeAuthorityPath = SourceNativeAuthorityPath, sourceNativeAuthoritySHA256 = SourceNativeAuthoritySHA256,
            dietLineAuthorityPath = DietLineAuthorityPath, dietLineAuthoritySHA256 = DietLineAuthoritySHA256,
            dietSelectorID = DietSelectorID, dietDomainCount = DietDomainCount, dietLinesPerDomain = DietLinesPerDomain,
            dietSourceOccurrences = DietSourceOccurrences,
            equalization = new WorldNoveltyEqualizationContractRON
            {
                offeredFuelVectorSHA256 = Equalization.OfferedFuelVectorSHA256,
                seed = Equalization.Seed,
                horizon = Equalization.Horizon,
                requireSharedPayloadMultiset = Equalization.RequireSharedPayloadMultiset,
                requireActualSpendEquality = Equalization.RequireActualSpendEquality,
                requireRefundEquality = Equalization.RequireRefundEquality,
                digest = Equalization.Digest,
            },
            opportunityFloor = new WorldNoveltyOpportunityFloorRON
            {
                boundarySteps = OpportunityFloor.BoundarySteps.ToList(),
                boundaryPrefixes = OpportunityFloor.BoundaryPrefixes.ToList(),
                windowK = OpportunityFloor.WindowK,
                minimumPaidComparisonsPerBoundary = OpportunityFloor.MinimumPaidComparisonsPerBoundary,
                digest = OpportunityFloor.Digest,
            },
            contrast = new WorldNoveltyContrastContractRON
            {
                equalityTolerance = Contrast.EqualityTolerance,
                minimumDirectionalEffect = Contrast.MinimumDirectionalEffect,
                minimumArmDivergences = Contrast.MinimumArmDivergences,
                digest = Contrast.Digest,
            },
            appHost = AppHost, appHostSHA256 = AppHostSHA256, assembly = Assembly,
            assemblySHA256 = AssemblySHA256, schemaSHA256 = SchemaSHA256,
            registrationAuthoritySHA256 = RegistrationAuthoritySHA256,
            reportSpecies = ReportSpeciesValue, titleClause = TitleClauseValue,
            r21Continuation = R21ContinuationValue, digest = digest,
        };
        foreach (WorldNoveltyScheduleAuthority schedule in Schedules)
            document.schedules.Add(new WorldNoveltyScheduleAuthorityRON
            {
                role = schedule.Role, scheduleID = schedule.ScheduleID, order = schedule.Order,
                worldSHA256 = schedule.WorldSHA256, payloadMultisetSHA256 = schedule.PayloadMultisetSHA256,
                scheduleSHA256 = schedule.ScheduleSHA256, prefixSHA256 = schedule.PrefixSHA256,
                prefixAfter = schedule.PrefixAfter.ToList(),
            });
        foreach (WorldNoveltyConfigAuthority config in Configs)
            document.configs.Add(new WorldNoveltyConfigAuthorityRON { arm = config.Arm, configSHA256 = config.ConfigSHA256, schemaSHA256 = config.SchemaSHA256 });
        return RonSerializer.SerializeToUtf8(in document);
    }

    private static string ComputeDigest(WorldNoveltyRegistration registration)
        => Convert.ToHexStringLower(SHA256.HashData(registration.EncodeDocument("")));

    private static void RequireName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\')) throw new InvalidDataException($"R20 registration has invalid {field}");
    }

    private static void RequireDigest(string value, string field)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException($"R20 registration has invalid {field}");
    }

    private static void RequireRelativePath(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains('\\') || value.Split('/').Any(static part => part is "" or "." or ".."))
            throw new InvalidDataException($"R20 registration has invalid {field}");
    }

    private void RequireSidecarOutsideWorld(string path, string field)
    {
        string world = WorldPath.TrimEnd('/');
        if (path.StartsWith(world + "/", StringComparison.Ordinal))
            throw new InvalidDataException($"R20 {field} must remain outside the registered world directory");
    }
}

public readonly record struct WorldNoveltyScheduleAuthority(
    string Role,
    string ScheduleID,
    string Order,
    string WorldSHA256,
    string PayloadMultisetSHA256,
    string ScheduleSHA256,
    string PrefixSHA256,
    IReadOnlyList<int> PrefixAfter)
{
    public bool AuthorityEquals(in WorldNoveltyScheduleAuthority other)
        => Role == other.Role && ScheduleID == other.ScheduleID && Order == other.Order
            && WorldSHA256 == other.WorldSHA256 && PayloadMultisetSHA256 == other.PayloadMultisetSHA256
            && ScheduleSHA256 == other.ScheduleSHA256 && PrefixSHA256 == other.PrefixSHA256
            && PrefixAfter.SequenceEqual(other.PrefixAfter);

    internal static WorldNoveltyScheduleAuthority FromSchedule(string role, WorldEpochSchedule schedule, string worldSHA256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(schedule);
        StringBuilder canonical = new();
        foreach (WorldEpochBatch batch in schedule.Batches)
        {
            canonical.Append(batch.Epoch).Append(':').Append(batch.OrderIndex).Append(':')
                .Append(batch.PrefixBefore).Append(':').Append(batch.PrefixAfter).Append(':')
                .Append(batch.BatchSHA256).Append('|');
        }
        string prefix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        return new(role, schedule.ActiveScheduleID, schedule.Order, worldSHA256,
            schedule.PayloadMultisetSHA256, schedule.ScheduleSHA256, prefix,
            schedule.Batches.Select(static batch => batch.PrefixAfter).ToArray());
    }

    public bool IsValid => Role is "control-stationary" or "live-epoch" or "null-epoch-order"
        && !string.IsNullOrWhiteSpace(ScheduleID) && !string.IsNullOrWhiteSpace(Order)
        && WorldSHA256.Length == 64 && PayloadMultisetSHA256.Length == 64
        && ScheduleSHA256.Length == 64 && PrefixSHA256.Length == 64
        && PrefixAfter.Count > 0 && PrefixAfter.All(static value => value > 0)
        && PrefixAfter.Zip(PrefixAfter.Skip(1)).All(static pair => pair.First < pair.Second)
        && WorldSHA256.All(Uri.IsHexDigit) && PayloadMultisetSHA256.All(Uri.IsHexDigit)
        && ScheduleSHA256.All(Uri.IsHexDigit) && PrefixSHA256.All(Uri.IsHexDigit);
}

public readonly record struct WorldNoveltyConfigAuthority(string Arm, string ConfigSHA256, string SchemaSHA256)
{
    /// The digest covers the plan-stripped organism/policy configuration. The
    /// arm-bound AdmissionPlan is sealed separately by its schedule authority.
    public string PolicyConfigSHA256 => ConfigSHA256;

    public bool IsValid => Arm is "epoch" or "stationary" or "order-null"
        && ConfigSHA256.Length == 64 && SchemaSHA256.Length == 64
        && ConfigSHA256.All(Uri.IsHexDigit) && SchemaSHA256.All(Uri.IsHexDigit);
}

public sealed class WorldNoveltyOpportunityFloor
{
    public const int RegisteredBoundaryCount = 4;
    public const int RegisteredWindowK = 64;
    public const int RegisteredMinimumPaidComparisonsPerBoundary = 4;
    public static IReadOnlyList<int> RegisteredBoundarySteps => [96, 192, 288, 384];
    public static IReadOnlyList<int> RegisteredBoundaryPrefixes => [96, 192, 288, 384];

    public WorldNoveltyOpportunityFloor(
        IReadOnlyList<int> boundarySteps,
        IReadOnlyList<int> boundaryPrefixes,
        int windowK,
        int minimumPaidComparisonsPerBoundary,
        string digest)
    {
        BoundarySteps = boundarySteps.ToList();
        BoundaryPrefixes = boundaryPrefixes.ToList();
        WindowK = windowK;
        MinimumPaidComparisonsPerBoundary = minimumPaidComparisonsPerBoundary;
        Digest = digest;
    }

    public IReadOnlyList<int> BoundarySteps { get; }
    public IReadOnlyList<int> BoundaryPrefixes { get; }
    public int WindowK { get; }
    public int MinimumPaidComparisonsPerBoundary { get; }
    public string Digest { get; }
    public static WorldNoveltyOpportunityFloor Create(IReadOnlyList<int> boundarySteps, IReadOnlyList<int> boundaryPrefixes)
    {
        int windowK = RegisteredWindowK;
        int minimum = RegisteredMinimumPaidComparisonsPerBoundary;
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"r20-opportunity-floor-v2|{string.Join(',', boundarySteps)}|{string.Join(',', boundaryPrefixes)}|{windowK}|{minimum}")));
        return new(boundarySteps, boundaryPrefixes, windowK, minimum, digest);
    }

    public void Validate(int horizon)
    {
        if (BoundarySteps.Count != RegisteredBoundaryCount || BoundaryPrefixes.Count != BoundarySteps.Count
            || WindowK != RegisteredWindowK || MinimumPaidComparisonsPerBoundary != RegisteredMinimumPaidComparisonsPerBoundary
            || Digest.Length != 64 || !Digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("R20 opportunity-liveness floor is malformed");
        if (!BoundarySteps.SequenceEqual(RegisteredBoundarySteps) || !BoundaryPrefixes.SequenceEqual(RegisteredBoundaryPrefixes))
            throw new InvalidDataException("R20 opportunity-liveness boundaries differ from the registered four-epoch plan");
        for (int i = 0; i < BoundarySteps.Count; i++)
        {
            if (BoundarySteps[i] < 0 || BoundarySteps[i] >= horizon || BoundarySteps[i] + WindowK > horizon
                || BoundaryPrefixes[i] <= 0 || (i > 0 && (BoundarySteps[i] <= BoundarySteps[i - 1] || BoundaryPrefixes[i] <= BoundaryPrefixes[i - 1])))
                throw new InvalidDataException("R20 opportunity-liveness boundaries are not ordered or fit the horizon");
        }
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"r20-opportunity-floor-v2|{string.Join(',', BoundarySteps)}|{string.Join(',', BoundaryPrefixes)}|{WindowK}|{MinimumPaidComparisonsPerBoundary}")));
        if (Digest != expected) throw new InvalidDataException("R20 opportunity-liveness floor digest does not match its contract");
    }
}

/// Registered directional contrast thresholds consumed by adjudication.
public readonly record struct WorldNoveltyContrastContract(
    double EqualityTolerance,
    double MinimumDirectionalEffect,
    int MinimumArmDivergences,
    string Digest)
{
    public static WorldNoveltyContrastContract Create()
    {
        const double equalityTolerance = 0.01;
        const double minimumDirectionalEffect = 0.01;
        const int minimumArmDivergences = 1;
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"r20-contrast-v3|{equalityTolerance:R}|{minimumDirectionalEffect:R}|{minimumArmDivergences}")));
        return new(equalityTolerance, minimumDirectionalEffect, minimumArmDivergences, digest);
    }

    public void Validate()
    {
        WorldNoveltyContrastContract registered = Create();
        if (EqualityTolerance != registered.EqualityTolerance
            || MinimumDirectionalEffect != registered.MinimumDirectionalEffect
            || MinimumArmDivergences != registered.MinimumArmDivergences
            || Digest != registered.Digest)
            throw new InvalidDataException("R20 contrast contract differs from the registered thresholds");
    }
}

/// Equalization is a preregistered offer contract.  It intentionally has no
/// actual-spend or refund fields: those are observations, not arm identity.
public readonly record struct WorldNoveltyEqualizationContract(
    string OfferedFuelVectorSHA256,
    ulong Seed,
    int Horizon,
    bool RequireSharedPayloadMultiset,
    bool RequireActualSpendEquality,
    bool RequireRefundEquality,
    string Digest)
{
    public static WorldNoveltyEqualizationContract Create(string offeredFuelVectorSHA256, ulong seed, int horizon)
    {
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"r20-equalization-v1|{offeredFuelVectorSHA256}|{seed}|{horizon}|shared-payload|actual-spend-evidence|refund-evidence")));
        return new(offeredFuelVectorSHA256, seed, horizon, true, false, false, digest);
    }

    public void Validate(ulong expectedSeed, int expectedHorizon, string payloadMultisetSHA256, string offeredFuelVectorSHA256)
    {
        if (Seed != expectedSeed || Horizon != expectedHorizon || OfferedFuelVectorSHA256 != offeredFuelVectorSHA256
            || !RequireSharedPayloadMultiset || RequireActualSpendEquality || RequireRefundEquality
            || OfferedFuelVectorSHA256.Length != 64 || !OfferedFuelVectorSHA256.All(Uri.IsHexDigit)
            || Digest.Length != 64 || !Digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("R20 equalization contract is malformed");
        _ = payloadMultisetSHA256;
        if (Digest != Create(OfferedFuelVectorSHA256, Seed, Horizon).Digest)
            throw new InvalidDataException("R20 equalization contract digest does not match its offer");
    }
}

[RonObject]
internal partial class WorldNoveltyRegistrationRON
{
    public int schemaVersion;
    public string assayID = "";
    public ulong seed;
    public int horizon;
    public string worldPath = "";
    public string latticeCensusPath = "";
    public string latticeCensusSHA256 = "";
    public string worldSHA256 = "";
    public string payloadMultisetSHA256 = "";
    public string sourceNativeAuthorityPath = "";
    public string sourceNativeAuthoritySHA256 = "";
    public string dietLineAuthorityPath = "";
    public string dietLineAuthoritySHA256 = "";
    public string dietSelectorID = "";
    public int dietDomainCount;
    public int dietLinesPerDomain;
    public int dietSourceOccurrences;
    public List<WorldNoveltyScheduleAuthorityRON> schedules = new();
    public List<WorldNoveltyConfigAuthorityRON> configs = new();
    public string offeredFuelVectorSHA256 = "";
    public WorldNoveltyOpportunityFloorRON opportunityFloor = new();
    public WorldNoveltyContrastContractRON contrast = new();
    public WorldNoveltyEqualizationContractRON equalization = new();
    public string appHost = "";
    public string appHostSHA256 = "";
    public string assembly = "";
    public string assemblySHA256 = "";
    public string schemaSHA256 = "";
    public string registrationAuthoritySHA256 = "";
    public string reportSpecies = "";
    public string titleClause = "";
    public string r21Continuation = "";
    public string digest = "";
}

[RonObject]
internal partial class WorldNoveltyContrastContractRON
{
    public double equalityTolerance;
    public double minimumDirectionalEffect;
    public int minimumArmDivergences;
    public string digest = "";
}

[RonObject]
internal partial class WorldNoveltyEqualizationContractRON
{
    public string offeredFuelVectorSHA256 = "";
    public ulong seed;
    public int horizon;
    public bool requireSharedPayloadMultiset;
    public bool requireActualSpendEquality;
    public bool requireRefundEquality;
    public string digest = "";
}

[RonObject]
internal partial class WorldNoveltyScheduleAuthorityRON
{
    public string role = "";
    public string scheduleID = "";
    public string order = "";
    public string worldSHA256 = "";
    public string payloadMultisetSHA256 = "";
    public string scheduleSHA256 = "";
    public string prefixSHA256 = "";
    public List<int> prefixAfter = new();
}

[RonObject]
internal partial class WorldNoveltyConfigAuthorityRON
{
    public string arm = "";
    public string configSHA256 = "";
    public string schemaSHA256 = "";
}

[RonObject]
internal partial class WorldNoveltyOpportunityFloorRON
{
    public List<int> boundarySteps = new();
    public List<int> boundaryPrefixes = new();
    public int windowK;
    public int minimumPaidComparisonsPerBoundary;
    public string digest = "";
}
