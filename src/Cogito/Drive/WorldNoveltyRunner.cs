namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// The registered R20 execution seam.  It owns only arm construction and
/// custody; adjudication remains the read-only WorldNoveltyAdjudicator.
public readonly record struct WorldNoveltyRunRequest(
    WorldNoveltyRegistration Registration,
    string CorpusPath,
    string OutputDirectory,
    string RegistrationPath = "")
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Registration);
        Registration.Validate();
        if (string.IsNullOrWhiteSpace(CorpusPath) || string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("R20 world-novelty run requires corpus and output paths");
        if (!Directory.Exists(CorpusPath) && !File.Exists(CorpusPath))
            throw new DirectoryNotFoundException($"R20 world-novelty corpus was not found: {CorpusPath}");
        if (!string.IsNullOrWhiteSpace(RegistrationPath)
            && !File.ReadAllBytes(RegistrationPath).AsSpan().SequenceEqual(Registration.Encode()))
            throw new InvalidDataException("R20 world-novelty registration bytes drifted");
    }
}

public readonly record struct WorldNoveltyRunResult(
    WorldNoveltyArmKinds Arm,
    string RunDirectory,
    WorldNoveltyArmResult Evidence,
    WorldNoveltyAdmissionEconomicsEvidence AdmissionEconomics);

/// Complete, arm-scoped custody for the durable theory-to-grammar opportunity
/// set. Receipt identities may repeat across the three equalized arms; they
/// must be unique only within this run and remain attached to its run ID.
public readonly record struct WorldNoveltyAdmissionEconomicsEvidence(
    WorldNoveltyArmKinds Arm,
    string RunID,
    IReadOnlyList<EmlPatternGrammarAdmissionEconomicsReceipt> Receipts)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Arm) || string.IsNullOrWhiteSpace(RunID) || Receipts is null || Receipts.Count == 0)
            throw new InvalidDataException("R20 arm promotion economics evidence is empty or unbound");
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (EmlPatternGrammarAdmissionEconomicsReceipt receipt in Receipts)
        {
            receipt.Validate();
            if (!identities.Add(receipt.IdentityKey))
                throw new InvalidDataException($"R20 arm promotion economics repeats opportunity {receipt.IdentityKey}");
        }
    }
}

public sealed class WorldNoveltyRunner
{
    // R20 deliberately reuses the established paired-gate wallet identity. The
    // registration pins the resulting offered vector digest, while actual and
    // refunded rows remain arm-local observations.
    internal const string FuelScheduleIdentity = "paired-gate-fuel-v1";

    public IReadOnlyList<WorldNoveltyRunResult> RunTriad(WorldNoveltyRunRequest request)
    {
        request.Validate();
        ValidateSharedConfigAuthority(request.Registration);
        string root = Path.GetFullPath(request.OutputDirectory);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException($"R20 world-novelty triad destination already exists: {root}");
        WorldNoveltyRunResult epochLive = RunArm(request with { OutputDirectory = Path.Combine(root, "epoch-live") }, WorldNoveltyArmKinds.EpochLive);
        WorldNoveltyRunResult stationary = RunArm(request with { OutputDirectory = Path.Combine(root, "stationary-control") }, WorldNoveltyArmKinds.StationaryControl);
        WorldNoveltyRunResult orderNull = RunArm(request with { OutputDirectory = Path.Combine(root, "epoch-order-null") }, WorldNoveltyArmKinds.EpochOrderNull);
        if (epochLive.Evidence.WorldSHA256 != stationary.Evidence.WorldSHA256
            || epochLive.Evidence.WorldSHA256 != orderNull.Evidence.WorldSHA256
            || epochLive.Evidence.OfferedFuel != stationary.Evidence.OfferedFuel
            || epochLive.Evidence.OfferedFuel != orderNull.Evidence.OfferedFuel)
            throw new InvalidDataException("R20 causal triad shared world or offered fuel vectors drifted");
        epochLive.AdmissionEconomics.Validate();
        stationary.AdmissionEconomics.Validate();
        orderNull.AdmissionEconomics.Validate();
        return [epochLive, stationary, orderNull];
    }

    public WorldNoveltyRunResult RunArm(WorldNoveltyRunRequest request, WorldNoveltyArmKinds arm)
    {
        request.Validate();
        ValidateSharedConfigAuthority(request.Registration);
        string root = Path.GetFullPath(request.OutputDirectory);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException($"R20 world-novelty destination already exists: {root}");

        string corpus = Path.GetFullPath(request.CorpusPath);
        string runtimeWorld = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
        if (!string.Equals(runtimeWorld, request.Registration.WorldSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("R20 world-novelty runtime world differs from registration");
        VerifyWorldDietCustody(request, corpus, runtimeWorld);
        VerifyRegisteredLatticeAuthority(request, runtimeWorld);

        IReadOnlyList<(int Domain, byte[] Bytes)> source = ReadPayloads(corpus, CogitoCorpus.DefaultGlob);
        string payloadMultiset = WorldEpochSchedule.ComputePayloadMultisetSHA256(source);
        if (!string.Equals(payloadMultiset, request.Registration.PayloadMultisetSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("R20 world-novelty payload multiset differs from registration");

        AdmissionPlan plan = BuildPlan(request.Registration, arm, source, runtimeWorld);
        if (!WorldEpochNoveltyProbe.VerifyResumedSuffix(corpus, CogitoCorpus.DefaultGlob, plan))
            throw new InvalidDataException($"R20 {arm} world encounter suffix did not resume byte-exactly");
        Cortex cortex = CreateArm(request.Registration, arm, corpus, runtimeWorld, plan);
        CortexRunConfig expected = cortex.Config.ToRunConfig(null);
        WorldNoveltyConfigAuthority configAuthority = ConfigAuthority(request.Registration, arm);
        CortexRunConfig policyConfig = expected with { AdmissionPlan = null };
        string configDigest = Cortex.PersistedConfigDigest(policyConfig);
        if (!string.Equals(configDigest, configAuthority.ConfigSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"R20 {arm} config differs from registered authority");

        Run run = Run.Create(root);
        int exit = cortex.Run(run);
        if (exit != 0) throw new InvalidDataException($"R20 {arm} Cortex exited with {exit}");

        RunAuthority authority = RunAuthority.Load(run.Dir);
        VerifyAuthority(request.Registration, arm, authority, expected, runtimeWorld, run.Dir);
        string configText = File.ReadAllText(Path.Combine(run.Dir, "config.txt"));
        if (configText.Contains(WorldEpochSchedule.ScheduleID, StringComparison.Ordinal)
            || configText.Contains(plan.ScheduleID, StringComparison.Ordinal))
            throw new InvalidDataException($"R20 {arm} schedule metadata leaked into the policy config surface");
        IReadOnlyList<EmlPatternGrammarAdmissionEconomicsReceipt> admissionEconomics = VerifyAdmissionEconomics(run.Dir);
        WorldNoveltyArmResult evidence = ReadEvidence(request.Registration, arm, run.Dir, authority, plan, runtimeWorld);
        WorldNoveltyAdmissionEconomicsEvidence economicsEvidence = new(arm, authority.RunID, admissionEconomics);
        economicsEvidence.Validate();
        return new(arm, run.Dir, evidence, economicsEvidence);
    }

    internal static Cortex CreateArm(
        WorldNoveltyRegistration registration,
        WorldNoveltyArmKinds arm,
        string corpus,
        string worldSHA256,
        AdmissionPlan plan)
    {
        CortexEmlCurriculum curriculum = new()
        {
            Corpus = new CogitoCorpus { Path = Path.GetFullPath(corpus), ExpectedWorldSHA256 = worldSHA256 },
            ProcessCatalog = EmlProcessCatalogs.Full,
            Rung0 = EmlRung0Modes.Armed,
            Deliberation = EmlDeliberationModes.Adaptive,
            DeliberationBudget = EmlDeliberationQuota.PairedGateNominal,
            Actions = EmlActionSelections.ProcedureGuarded,
        };
        return new Cortex(new CortexConfig
        {
            RunName = "r20-world-novelty",
            Seed = registration.Seed,
            Steps = registration.Horizon,
            EmlPairedFuelScheduleIdentity = FuelScheduleIdentity,
            AdmissionPlan = plan,
            Curriculum = curriculum,
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0, CurveEvery = 1 },
        });
    }

    private static WorldNoveltyConfigAuthority ConfigAuthority(WorldNoveltyRegistration registration, WorldNoveltyArmKinds arm)
        => arm switch
        {
            WorldNoveltyArmKinds.EpochLive => registration.EpochConfig,
            WorldNoveltyArmKinds.StationaryControl => registration.StationaryConfig,
            WorldNoveltyArmKinds.EpochOrderNull => registration.OrderNullConfig,
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };

    private static void ValidateSharedConfigAuthority(WorldNoveltyRegistration registration)
    {
        string config = registration.EpochConfig.ConfigSHA256;
        if (registration.StationaryConfig.ConfigSHA256 != config || registration.OrderNullConfig.ConfigSHA256 != config)
            throw new InvalidDataException("R20 triad arm config authorities are not equalized");
    }

    private static AdmissionPlan BuildPlan(
        WorldNoveltyRegistration registration,
        WorldNoveltyArmKinds arm,
        IReadOnlyList<(int Domain, byte[] Bytes)> source,
        string worldSHA256)
    {
        WorldNoveltyScheduleAuthority authority = arm switch
        {
            WorldNoveltyArmKinds.EpochLive => registration.EpochSchedule,
            WorldNoveltyArmKinds.StationaryControl => registration.StationarySchedule,
            WorldNoveltyArmKinds.EpochOrderNull => registration.EpochOrderNullSchedule,
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };
        int domainCount = source.Select(static row => row.Domain).Distinct().Count();
        WorldEpochSchedule schedule;
        if (arm == WorldNoveltyArmKinds.StationaryControl)
        {
            schedule = WorldEpochSchedule.CreateStationary(source, registration.OpportunityFloor.BoundaryPrefixes);
        }
        else
        {
            int[] order = ParseOrder(authority.Order, domainCount);
            schedule = WorldEpochSchedule.Create(source, order.Length, order);
        }
        WorldNoveltyScheduleAuthority expectedAuthority = WorldNoveltyScheduleAuthority.FromSchedule(authority.Role, schedule, worldSHA256);
        if (!expectedAuthority.AuthorityEquals(in authority))
            throw new InvalidDataException($"R20 {arm} schedule authority differs from the registered schedule");
        AdmissionPlan unboundPlan = schedule.AdmissionPlan
            ?? new AdmissionPlan(authority.ScheduleID,
                arm == WorldNoveltyArmKinds.StationaryControl
                    ? BuildRoundRobinSequence(source, domainCount)
                    : BuildEpochSequence(source, ParseOrder(authority.Order, domainCount)));
        AdmissionPlan plan = unboundPlan.BindWorld(worldSHA256);
        plan.Validate(domainCount);
        plan.ValidateWorld(worldSHA256);
        plan.ValidateCounts(CountDomains(source, domainCount));
        return plan;
    }

    private static void VerifyRegisteredLatticeAuthority(WorldNoveltyRunRequest request, string runtimeWorld)
    {
        string path = ResolveLatticeAuthorityPath(request);
        byte[] bytes = File.ReadAllBytes(path);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(digest, request.Registration.LatticeCensusSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"R20 lattice authority digest differs from registration: {path}");

        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;
            string[] pair = trimmed.Split('\t');
            if (pair.Length != 2 || !fields.TryAdd(pair[0], pair[1]))
                throw new InvalidDataException("R20 lattice authority has malformed or duplicate fields");
        }
        RequireField(fields, "schema", "1");
        RequireField(fields, "world_sha256", runtimeWorld);
        RequireField(fields, "frame_mode", "all-identifier-families");
        RequireField(fields, "opportunity_gate", "pass");
        int conditional = ParseFieldInt(fields, "conditional_fillers");
        int callDef = ParseFieldInt(fields, "cross_file_call_def_pairs");
        double rate = ParseFieldDouble(fields, "conditional_rate");
        double nullRate = ParseFieldDouble(fields, "null_max_conditional_rate");
        if (conditional < 2 || callDef < 2 || double.IsNaN(rate) || double.IsNaN(nullRate)
            || double.IsInfinity(rate) || double.IsInfinity(nullRate) || rate <= 0d || nullRate < 0d || rate <= nullRate)
            throw new InvalidDataException("R20 lattice authority does not clear its preregistered opportunity metrics");
    }

    private static void VerifyWorldDietCustody(WorldNoveltyRunRequest request, string corpus, string runtimeWorld)
    {
        WorldNoveltyRegistration registration = request.Registration;
        string sourceAuthorityPath = ResolveRegisteredAuthorityPath(registration.SourceNativeAuthorityPath, "source-native");
        byte[] sourceAuthorityBytes = File.ReadAllBytes(sourceAuthorityPath);
        RequireAuthorityDigest(sourceAuthorityBytes, registration.SourceNativeAuthoritySHA256, "source-native");
        string sourceNativeRoot = ResolveSourceNativeRoot(sourceAuthorityPath);
        Dictionary<string, SourceNativeAuthorityRow> sourceRows = ParseSourceNativeAuthority(sourceAuthorityBytes, sourceNativeRoot);

        string dietAuthorityPath = ResolveRegisteredAuthorityPath(registration.DietLineAuthorityPath, "diet line");
        byte[] dietAuthorityBytes = File.ReadAllBytes(dietAuthorityPath);
        RequireAuthorityDigest(dietAuthorityBytes, registration.DietLineAuthoritySHA256, "diet line");
        VerifyDietLineAuthority(dietAuthorityBytes, registration, runtimeWorld, corpus, sourceRows, sourceNativeRoot);
    }

    private static Dictionary<string, SourceNativeAuthorityRow> ParseSourceNativeAuthority(byte[] bytes, string sourceNativeRoot)
    {
        string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');
        if (lines.Length < 5 || lines[0].TrimEnd('\r') != "schema\t1"
            || lines[3].TrimEnd('\r') != "domain\ttag\tordinal\tsource_path\tmaterialized_path\tbytes\tsha256")
            throw new InvalidDataException("R20 source-native authority has an invalid header");

        Dictionary<string, SourceNativeAuthorityRow> rows = new(StringComparer.Ordinal);
        for (int index = 4; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] fields = line.Split('\t');
            if (fields.Length != 7 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int domain)
                || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ordinal)
                || !long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytesLength)
                || !WorldNoveltyArmResult.IsDigest(fields[6])
                || fields[4].Length == 0 || Path.IsPathRooted(fields[4]) || fields[4].Contains('\\')
                || fields[4].Split('/').Any(static part => part is "" or "." or ".."))
                throw new InvalidDataException("R20 source-native authority has a malformed row");

            string materializedPath = fields[4];
            if (!rows.TryAdd(materializedPath, new(domain, fields[3], materializedPath, bytesLength, fields[6])))
                throw new InvalidDataException($"R20 source-native authority repeats materialized path {materializedPath}");
            string fullPath = Path.GetFullPath(Path.Combine(sourceNativeRoot, materializedPath));
            if (!IsWithinDirectory(fullPath, sourceNativeRoot) || !File.Exists(fullPath))
                throw new InvalidDataException($"R20 source-native authority references a missing materialized file {materializedPath}");
            byte[] materialized = File.ReadAllBytes(fullPath);
            if (materialized.LongLength != bytesLength
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(materialized)), fields[6], StringComparison.Ordinal))
                throw new InvalidDataException($"R20 source-native materialized file drifted: {materializedPath}");
            _ = domain;
            _ = ordinal;
        }
        if (rows.Count == 0) throw new InvalidDataException("R20 source-native authority contains no source files");
        return rows;
    }

    private static void VerifyDietLineAuthority(
        byte[] bytes,
        WorldNoveltyRegistration registration,
        string runtimeWorld,
        string corpus,
        IReadOnlyDictionary<string, SourceNativeAuthorityRow> sourceRows,
        string sourceNativeRoot)
    {
        string[] authorityLines = Encoding.UTF8.GetString(bytes).Split('\n');
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        int headerIndex = -1;
        for (int index = 0; index < authorityLines.Length; index++)
        {
            string line = authorityLines[index].TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line == "epoch\tordinal\tsource_path\tmaterialized_path\tsource_line_ordinal\traw_sha256\traw_bytes\tpayload_sha256\tpayload_bytes\tselector_id\tworld_sha256")
            {
                headerIndex = index;
                break;
            }
            string[] pair = line.Split('\t');
            if (pair.Length != 2 || !fields.TryAdd(pair[0], pair[1]))
                throw new InvalidDataException("R20 diet line authority has malformed or duplicate fields");
        }
        if (headerIndex < 0 || fields.Count != 7)
            throw new InvalidDataException("R20 diet line authority is missing its complete header");
        RequireRegisteredField(fields, "schema", "1");
        RequireRegisteredField(fields, "selector_id", registration.DietSelectorID);
        RequireRegisteredField(fields, "source_native_authority_sha256", registration.SourceNativeAuthoritySHA256);
        RequireRegisteredField(fields, "world_sha256", runtimeWorld);
        RequireRegisteredField(fields, "domain_count", registration.DietDomainCount.ToString(CultureInfo.InvariantCulture));
        RequireRegisteredField(fields, "lines_per_domain", registration.DietLinesPerDomain.ToString(CultureInfo.InvariantCulture));
        RequireRegisteredField(fields, "source_occurrences", registration.DietSourceOccurrences.ToString(CultureInfo.InvariantCulture));

        string[] worldFiles = FileCorpus.GatherFiles(corpus, CogitoCorpus.DefaultGlob).ToArray();
        if (worldFiles.Length != registration.DietDomainCount)
            throw new InvalidDataException($"R20 diet world has {worldFiles.Length} domains, expected {registration.DietDomainCount}");
        string[][] worldLines = new string[worldFiles.Length][];
        for (int domain = 0; domain < worldFiles.Length; domain++)
        {
            worldLines[domain] = File.ReadLines(worldFiles[domain])
                .Select(static line => line.TrimEnd())
                .Where(static line => line.Trim().Length != 0)
                .ToArray();
            if (worldLines[domain].Length != registration.DietLinesPerDomain)
                throw new InvalidDataException($"R20 diet domain {domain} has {worldLines[domain].Length} payload lines");
        }

        Dictionary<string, string[]> sourceLineCache = new(StringComparer.Ordinal);
        HashSet<string> sourceOccurrences = new(StringComparer.Ordinal);
        int rowCount = 0;
        for (int index = headerIndex + 1; index < authorityLines.Length; index++)
        {
            string line = authorityLines[index].TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] fieldsRow = line.Split('\t');
            if (fieldsRow.Length != 11
                || !int.TryParse(fieldsRow[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dietDomain)
                || !int.TryParse(fieldsRow[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dietLine)
                || !int.TryParse(fieldsRow[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceLine)
                || !long.TryParse(fieldsRow[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long rawByteCount)
                || !long.TryParse(fieldsRow[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out long payloadByteCount)
                || !WorldNoveltyArmResult.IsDigest(fieldsRow[5])
                || !WorldNoveltyArmResult.IsDigest(fieldsRow[7])
                || dietDomain != rowCount / registration.DietLinesPerDomain
                || dietLine != rowCount % registration.DietLinesPerDomain
                || !sourceRows.ContainsKey(fieldsRow[3]) || sourceLine < 0
                || !string.Equals(fieldsRow[9], registration.DietSelectorID, StringComparison.Ordinal)
                || !string.Equals(fieldsRow[10], runtimeWorld, StringComparison.Ordinal))
                throw new InvalidDataException($"R20 diet line authority row {rowCount} is malformed or out of order");

            SourceNativeAuthorityRow source = sourceRows[fieldsRow[3]];
            if (!string.Equals(fieldsRow[2], source.SourcePath, StringComparison.Ordinal))
                throw new InvalidDataException($"R20 diet line authority row {rowCount} disagrees with source-native provenance");
            string sourcePath = Path.GetFullPath(Path.Combine(sourceNativeRoot, source.MaterializedPath));
            if (!sourceLineCache.TryGetValue(source.MaterializedPath, out string[]? sourceLines))
                sourceLineCache[source.MaterializedPath] = sourceLines = File.ReadLines(sourcePath).ToArray();
            if ((uint)sourceLine >= (uint)sourceLines.Length)
                throw new InvalidDataException($"R20 diet line authority row {rowCount} references a missing source line");
            string rawSourceText = sourceLines[sourceLine];
            string sourceText = rawSourceText.TrimEnd();
            if (sourceText.Trim().Length == 0 || !sourceOccurrences.Add($"{source.MaterializedPath}\u001f{sourceLine}"))
                throw new InvalidDataException($"R20 diet line authority row {rowCount} repeats or selects a blank source occurrence");
            byte[] rawSourceBytes = Encoding.UTF8.GetBytes(rawSourceText);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(sourceText);
            string rawDigest = Convert.ToHexStringLower(SHA256.HashData(rawSourceBytes));
            string payloadDigest = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
            if (rawSourceBytes.LongLength != rawByteCount || !string.Equals(rawDigest, fieldsRow[5], StringComparison.Ordinal)
                || payloadBytes.LongLength != payloadByteCount || !string.Equals(payloadDigest, fieldsRow[7], StringComparison.Ordinal)
                || !string.Equals(worldLines[dietDomain][dietLine], sourceText, StringComparison.Ordinal))
                throw new InvalidDataException($"R20 diet line authority row {rowCount} does not close to source and final world bytes");
            rowCount++;
        }
        if (rowCount != registration.DietSourceOccurrences || sourceOccurrences.Count != registration.DietSourceOccurrences)
            throw new InvalidDataException($"R20 diet line authority closes {rowCount} source occurrences, expected {registration.DietSourceOccurrences}");
    }

    private static string ResolveRegisteredAuthorityPath(string relativePath, string label)
    {
        string path = Path.GetFullPath(relativePath, Directory.GetCurrentDirectory());
        if (!File.Exists(path)) throw new FileNotFoundException($"R20 registered {label} authority artifact is missing", path);
        return path;
    }

    private static string ResolveSourceNativeRoot(string authorityPath)
    {
        const string suffix = ".source-native.tsv";
        if (!authorityPath.EndsWith(suffix, StringComparison.Ordinal))
            throw new InvalidDataException("R20 source-native authority path must end in .source-native.tsv");
        string root = authorityPath[..^suffix.Length];
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"R20 source-native world directory is missing: {root}");
        return root;
    }

    private static void RequireAuthorityDigest(byte[] bytes, string expected, string label)
    {
        string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"R20 {label} authority digest differs from registration");
    }

    private static void RequireRegisteredField(IReadOnlyDictionary<string, string> fields, string name, string expected)
    {
        if (!fields.TryGetValue(name, out string? actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"R20 diet line authority field '{name}' differs from registration");
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        string relative = Path.GetRelativePath(directory, path);
        return relative is not (".." or ".") && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private readonly record struct SourceNativeAuthorityRow(int Domain, string SourcePath, string MaterializedPath, long Bytes, string SHA256);

    private static string ResolveLatticeAuthorityPath(WorldNoveltyRunRequest request)
    {
        string path = Path.GetFullPath(request.Registration.LatticeCensusPath, Directory.GetCurrentDirectory());
        if (!File.Exists(path))
            throw new FileNotFoundException("R20 registered lattice authority artifact is missing", path);
        return path;
    }

    private static void RequireField(IReadOnlyDictionary<string, string> fields, string key, string expected)
    {
        if (!fields.TryGetValue(key, out string? actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"R20 lattice authority field '{key}' is not registered");
    }

    private static int ParseFieldInt(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out string? value)
            || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new InvalidDataException($"R20 lattice authority field '{key}' is malformed");
        return parsed;
    }

    private static double ParseFieldDouble(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out string? value)
            || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            throw new InvalidDataException($"R20 lattice authority field '{key}' is malformed");
        return parsed;
    }

    private static void VerifyAuthority(
        WorldNoveltyRegistration registration,
        WorldNoveltyArmKinds arm,
        RunAuthority authority,
        CortexRunConfig expected,
        string worldSHA256,
        string directory)
    {
        if (authority.Binary.ProcessName != registration.AppHost
            || authority.Binary.ProcessSHA256 != registration.AppHostSHA256
            || authority.Binary.AssemblyName != registration.Assembly
            || authority.Binary.AssemblySHA256 != registration.AssemblySHA256)
            throw new InvalidDataException($"R20 {arm} binary authority differs from registration");
        if (authority.Checkpoint.NextStep != registration.Horizon || !authority.Checkpoint.SaveLoadSaveExact)
            throw new InvalidDataException($"R20 {arm} checkpoint did not seal the registered horizon exactly");
        if (authority.WorldSHA256 != worldSHA256)
            throw new InvalidDataException($"R20 {arm} authority carries a different world");
        if (authority.PersistedConfigDigest != Cortex.PersistedConfigDigest(expected))
            throw new InvalidDataException($"R20 {arm} authority config digest drifted");
        if (!File.Exists(Path.Combine(directory, Checkpoint.FileName)))
            throw new InvalidDataException($"R20 {arm} checkpoint authority is missing its image");
        CortexRunConfig restored = Checkpoint.PeekConfig(directory);
        if (restored.AdmissionPlan is not { } restoredPlan
            || restoredPlan.AuthorityDigest != expected.AdmissionPlan?.AuthorityDigest
            || restoredPlan.WorldSHA256 != worldSHA256)
            throw new InvalidDataException($"R20 {arm} checkpoint lost the bound world encounter plan");
    }

    private static WorldNoveltyArmResult ReadEvidence(
        WorldNoveltyRegistration registration,
        WorldNoveltyArmKinds arm,
        string directory,
        RunAuthority authority,
        AdmissionPlan plan,
        string worldSHA256)
    {
        string fuelPath = Path.Combine(directory, EmlPairedFuelSchedule.SidecarFile);
        if (!File.Exists(fuelPath)) throw new FileNotFoundException("R20 paired-fuel sidecar is missing", fuelPath);
        (EmlPairedFuelSchedule schedule, EmlPairedFuelScheduleCursor cursor) =
            EmlPairedFuelScheduleJournal.Decode(File.ReadAllBytes(fuelPath));
        cursor.ValidateClosed(in schedule);
        if (!string.Equals(schedule.Identity, FuelScheduleIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("R20 paired-fuel identity drifted");
        EmlDeliberationCounts offered = cursor.Planned, actual = cursor.Actual, refund = cursor.Refund;
        EmlDeliberationCounts closed = EmlDeliberationCounts.Add(in actual, in refund);
        if (closed != offered) throw new InvalidDataException("R20 paired-fuel actual/refund rows do not close the offered vector");
        string offeredDigest = schedule.Digest;
        if (offeredDigest != registration.OfferedFuelVectorSHA256)
            throw new InvalidDataException("R20 paired-fuel offered vector differs from registration");

        List<int> transitionPrefixes = ReadTransitionPrefixes(directory, plan);
        foreach (int boundaryPrefix in registration.OpportunityFloor.BoundaryPrefixes)
            if (!transitionPrefixes.Contains(boundaryPrefix))
                throw new InvalidDataException($"R20 {arm} did not admit registered world boundary prefix {boundaryPrefix}");
        List<OrganicRow> comparisons = ReadOrganicComparisons(directory, registration.Horizon);
        IReadOnlyList<int> boundaries = registration.OpportunityFloor.BoundarySteps;
        int window = registration.OpportunityFloor.WindowK;
        int candidatePresent = 0;
        int[] fundedByBoundary = new int[boundaries.Count];
        int[] divergencesByBoundary = new int[boundaries.Count];
        foreach (OrganicRow row in comparisons)
        {
            if (!row.Paid || row.RawCandidate < 0) continue;
            for (int boundaryIndex = 0; boundaryIndex < boundaries.Count; boundaryIndex++)
            {
                int boundary = boundaries[boundaryIndex];
                if (row.Step < boundary || row.Step >= boundary + window) continue;
                candidatePresent++;
                fundedByBoundary[boundaryIndex]++;
                if (row.Diverged) divergencesByBoundary[boundaryIndex]++;
            }
        }
        if (fundedByBoundary.Any(count => count < registration.OpportunityFloor.MinimumPaidComparisonsPerBoundary))
            throw new InvalidDataException($"R20 {arm} failed one or more preregistered paid-comparison floors");
        int readRows = File.Exists(Path.Combine(directory, "curve.tsv"))
            ? Math.Max(0, File.ReadAllLines(Path.Combine(directory, "curve.tsv")).Length - 1) : 0;
        int canonical = CountCanonicalStates(directory);
        WorldNoveltyScheduleAuthority scheduleAuthority = arm switch
        {
            WorldNoveltyArmKinds.EpochLive => registration.EpochSchedule,
            WorldNoveltyArmKinds.StationaryControl => registration.StationarySchedule,
            WorldNoveltyArmKinds.EpochOrderNull => registration.EpochOrderNullSchedule,
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };
        WorldNoveltyConfigAuthority configAuthority = arm switch
        {
            WorldNoveltyArmKinds.EpochLive => registration.EpochConfig,
            WorldNoveltyArmKinds.StationaryControl => registration.StationaryConfig,
            WorldNoveltyArmKinds.EpochOrderNull => registration.OrderNullConfig,
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };
        WorldNoveltyArmResult result = new(arm, authority.RunID, registration.Seed, worldSHA256, scheduleAuthority.ScheduleSHA256,
            configAuthority.ConfigSHA256, configAuthority.SchemaSHA256, offeredDigest, registration.Horizon, boundaries, fundedByBoundary, divergencesByBoundary, offered, actual, refund,
            transitionPrefixes, readRows, canonical, candidatePresent,
            registration.OpportunityFloor.BoundaryPrefixes);
        result.Validate(WorldNoveltyAdjudicationConfig.FromRegistration(registration));
        return result;
    }

    private static List<int> ReadTransitionPrefixes(string directory, AdmissionPlan plan)
    {
        string path = Path.Combine(directory, "world-admission.tsv");
        if (!File.Exists(path)) throw new FileNotFoundException("R20 world admission custody is missing", path);
        List<int> prefixes = new();
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 13) continue;
            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int after)
                || !string.Equals(fields[10], plan.ScheduleID, StringComparison.Ordinal)
                || !string.Equals(fields[11], plan.AuthorityDigest, StringComparison.Ordinal)) continue;
            if (after > 0 && !prefixes.Contains(after)) prefixes.Add(after);
        }
        return prefixes;
    }

    private static List<OrganicRow> ReadOrganicComparisons(string directory, int horizon)
    {
        using Tape tape = Checkpoint.LoadTape(directory);
        string path = Path.Combine(directory, "journal.log");
        if (!File.Exists(path)) throw new FileNotFoundException("R20 journal custody is missing", path);
        string[] journalLines = File.ReadAllLines(path);
        const string comparisonSource = "policy:homeostat:organic-comparison";
        string policySource = "policy:" + Homeostat.PolicyID.Value;
        TapeEventView[] views = tape.GetEventViews().Where(view => view.Source == comparisonSource).OrderBy(view => view.Id.Value).ToArray();
        if (views.Length == 0) throw new InvalidDataException("R20 run omits typed organic comparison receipts");
        int journalComparisonRows = journalLines.Count(line => line.Contains("\torganic-comparison\t", StringComparison.Ordinal));
        if (journalComparisonRows != views.Length)
            throw new InvalidDataException($"R20 organic comparison journal/tape count drifted: journal={journalComparisonRows} tape={views.Length}");
        List<OrganicRow> rows = new(views.Length);
        HashSet<long> eventIDs = new();
        HashSet<long> sourceEventIDs = new();
        HashSet<ulong> decisionIDs = new();
        HashSet<ulong> fundingIDs = new();
        foreach (TapeEventView view in views)
        {
            if (!view.HasRole(TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !tape.Resolve(view.Id, out byte[] encoded)
                || !TapePacketCreator.TryDecodeOrganicComparison(encoded, out OrganicComparisonReceipt receipt))
                throw new InvalidDataException($"R20 organic comparison event {view.Id} is not a typed measurement/custody receipt");
            receipt.Validate();
            if (receipt.Step < 0 || receipt.Step >= horizon || !receipt.Policy.Equals(Homeostat.PolicyID)
                || receipt.SourceDecisionEventID.Value >= view.Id.Value
                || !eventIDs.Add(view.Id.Value) || !sourceEventIDs.Add(receipt.SourceDecisionEventID.Value)
                || !decisionIDs.Add(receipt.DecisionID.Value))
                throw new InvalidDataException($"R20 organic comparison event {view.Id} is outside the registered run identity");
            TapeEventView? sourceView = tape.GetEventViews().FirstOrDefault(candidate => candidate.Id == receipt.SourceDecisionEventID);
            if (sourceView is null || sourceView.Value.Source != policySource
                || !sourceView.Value.HasRole(TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !tape.Resolve(receipt.SourceDecisionEventID, out byte[] sourcePayload))
                throw new InvalidDataException($"R20 organic comparison event {view.Id} has no policy-decision source");
            CortexPolicyDecisionPacket packet;
            try { packet = TapePacketCreator.DecodePolicyDecision(sourcePayload); }
            catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
            { throw new InvalidDataException($"R20 organic comparison event {view.Id} policy source is malformed", error); }
            if (!packet.DecisionID.Equals(receipt.DecisionID)
                || packet.Readout.LaunchpadAction != receipt.LaunchpadAction
                || packet.Readout.RawCandidateAction != receipt.RawCandidateAction
                || packet.Readout.SelectedCandidateAction != receipt.SelectedCandidateAction
                || packet.Readout.GrammarRevision != receipt.ReadoutRevision
                || packet.Readout.ReadoutFingerprint != receipt.ReadoutFingerprint
                || packet.Readout.ReadoutCandidateFingerprint != receipt.CandidateFingerprint
                || packet.Readout.ReadoutCandidateOccurrenceDigest != receipt.CandidateOccurrenceDigest)
                throw new InvalidDataException($"R20 organic comparison event {view.Id} diverges from its policy source");
            string sourcePayloadDigest = Convert.ToHexStringLower(SHA256.HashData(sourcePayload));
            CortexPolicyDecision sourceDecision = new(receipt.DecisionID, receipt.Policy, packet.Readout);
            string sourceJournalDigest = Journal.ComputePolicyDecisionJournalSHA256(receipt.Step,
                receipt.SourceDecisionEventID, policySource, in sourceDecision, packet.ActionCount,
                packet.Features.Length, sourcePayload.Length);
            if (sourcePayloadDigest != receipt.SourceDecisionPayloadSHA256 || sourceJournalDigest != receipt.SourceDecisionJournalSHA256)
                throw new InvalidDataException($"R20 organic comparison event {view.Id} source custody changed");
            RequireJournalRow(journalLines, receipt.Step, view.Id.Value, "organic-comparison", receipt.CanonicalReceiptSHA256);
            RequireJournalRow(journalLines, receipt.Step, receipt.SourceDecisionEventID.Value, "policy-decision", "");
            VerifyFundingRows(directory, receipt, fundingIDs);
            bool paid = receipt.HasFundingDecision && receipt.FundingDecision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused;
            if (receipt.Outcome is OrganicComparisonOutcomeKinds.CandidateAgreement or OrganicComparisonOutcomeKinds.CandidateDivergence)
            {
                if (!paid) throw new InvalidDataException($"R20 candidate comparison {view.Id} is not paid");
                rows.Add(new(receipt.Step, receipt.RawCandidateAction, receipt.LaunchpadAction, true,
                    receipt.Outcome == OrganicComparisonOutcomeKinds.CandidateDivergence));
            }
        }
        return rows;
    }

    private static void RequireJournalRow(IReadOnlyList<string> lines, int step, long eventID, string kind, string digest)
    {
        string marker = $"{step}\t{kind}\t{new TapeEventID(eventID)}\t";
        int matches = lines.Count(line => line.StartsWith(marker, StringComparison.Ordinal)
            && (digest.Length == 0 || line.Contains(digest, StringComparison.Ordinal)));
        if (matches != 1) throw new InvalidDataException($"R20 journal custody has {matches} {kind} rows for {eventID}");
    }

    private static void VerifyFundingRows(string directory, OrganicComparisonReceipt receipt, HashSet<ulong> fundingIDs)
    {
        if (receipt.QuotaDecisionID is not { Value: > 0 } fundingID)
        {
            if (receipt.FundingJournalRowSHA256.Length != 0 || receipt.SettlementJournalRowSHA256.Length != 0)
                throw new InvalidDataException("R20 organic comparison carries funding digests without funding identity");
            return;
        }
        if (!fundingIDs.Add(fundingID.Value))
            throw new InvalidDataException($"R20 organic comparison repeats funding identity {fundingID}");
        string fundingPath = Path.Combine(directory, "policy_readout_funding.journal.tsv");
        if (!File.Exists(fundingPath)) throw new InvalidDataException($"R20 funding row {fundingID} is missing");
        string fundingToken = fundingID.ToString();
        string[] fundingRows = File.ReadAllLines(fundingPath).Where(line => line.StartsWith(fundingToken + "\t", StringComparison.Ordinal)).ToArray();
        if (fundingRows.Length != 1 || Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fundingRows[0]))) != receipt.FundingJournalRowSHA256)
            throw new InvalidDataException($"R20 funding row {fundingID} is missing, duplicated, or mutated");
        if (receipt.SettlementJournalRowSHA256.Length == 0) return;
        string settlementPath = Path.Combine(directory, "policy_readout_settlements.journal.tsv");
        if (!File.Exists(settlementPath)) throw new InvalidDataException($"R20 settlement row {fundingID} is missing");
        string[] settlementRows = File.ReadAllLines(settlementPath).Where(line => line.StartsWith(fundingToken + "\t", StringComparison.Ordinal)).ToArray();
        if (settlementRows.Length != 1 || Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(settlementRows[0]))) != receipt.SettlementJournalRowSHA256)
            throw new InvalidDataException($"R20 settlement row {fundingID} is missing, duplicated, or mutated");
    }

    private static IReadOnlyList<EmlPatternGrammarAdmissionEconomicsReceipt> VerifyAdmissionEconomics(string directory)
    {
        using Tape tape = Checkpoint.LoadTape(directory);
        string journalPath = Path.Combine(directory, "journal.log");
        if (!File.Exists(journalPath)) throw new FileNotFoundException("R20 promotion economics journal is missing", journalPath);
        string[] journalLines = File.ReadAllLines(journalPath);
        TapeEventView[] economicsViews = tape.GetEventViews()
            .Where(view => view.Source == "eml:theory-grammar-economics")
            .OrderBy(view => view.Id.Value).ToArray();
        TapeEventView[] promotionViews = tape.GetEventViews()
            .Where(view => view.Source == "eml:theory-grammar").ToArray();
        if (economicsViews.Length == 0)
            throw new InvalidDataException("R20 promotion economics has no durable opportunity receipts");
        int journalEconomicsRows = journalLines.Count(line => line.Contains("\tmint\t", StringComparison.Ordinal)
            && line.Contains("\teml:theory-grammar-economics\t", StringComparison.Ordinal));
        if (journalEconomicsRows != economicsViews.Length)
            throw new InvalidDataException($"R20 promotion economics journal/tape count drifted: journal={journalEconomicsRows} tape={economicsViews.Length}");
        HashSet<string> admittedCandidates = new(StringComparer.Ordinal);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (TapeEventView view in economicsViews)
        {
            if (!view.HasRole(TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !tape.Resolve(view.Id, out byte[] encoded)
                || !TapePacketCreator.TryDecodeEmlPatternGrammarAdmissionEconomics(encoded,
                    out EmlPatternGrammarAdmissionEconomicsReceipt receipt))
                throw new InvalidDataException($"R20 promotion economics event {view.Id} is not a typed measurement/custody receipt");
            receipt.Validate();
            if (!identities.Add(receipt.IdentityKey))
                throw new InvalidDataException($"R20 promotion economics repeats opportunity {receipt.IdentityKey}");
            int journalIndex = FindEconomicsJournalRow(journalLines, view.Id, out int step, out string row);
            JournalRowBinding binding = new(journalIndex, step, view.Id, "eml:theory-grammar-economics",
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row))));
            EmlPatternGrammarAdmissionEconomicsRecord record = EmlPatternGrammarAdmissionEconomicsRecord.Create(receipt, view.Id, encoded, in binding);
            record.Validate(encoded);
            if (receipt.Decision == EmlPatternGrammarAdmissionEconomicsDecisionKinds.Admitted)
            {
                if (!receipt.Price.IsPositive) throw new InvalidDataException("R20 promotion admitted non-positive marginal savings");
                if (!admittedCandidates.Add(receipt.CandidateSHA256))
                    throw new InvalidDataException($"R20 promotion economics repeats admitted candidate {receipt.CandidateSHA256}");
            }
            else if (!receipt.IsRefusal || receipt.Price.IsPositive)
                throw new InvalidDataException("R20 promotion refusal does not prove non-positive marginal savings");
        }

        HashSet<string> observedPromotions = new(StringComparer.Ordinal);
        foreach (TapeEventView view in promotionViews)
        {
            if (!view.HasRole(TapeEventRoles.GrammarInput) || !tape.Resolve(view.Id, out byte[] payload))
                throw new InvalidDataException($"R20 grammar promotion event {view.Id} has an invalid role or payload");
            string candidate = Convert.ToHexStringLower(SHA256.HashData(payload));
            if (!observedPromotions.Add(candidate) || !admittedCandidates.Contains(candidate))
                throw new InvalidDataException($"R20 grammar promotion event {view.Id} lacks a positive economics receipt");
        }
        if (observedPromotions.Count != admittedCandidates.Count)
            throw new InvalidDataException("R20 promotion economics does not cover every admitted grammar promotion");
        return economicsViews.Select(view =>
        {
            if (!tape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodeEmlPatternGrammarAdmissionEconomics(payload, out EmlPatternGrammarAdmissionEconomicsReceipt receipt))
                throw new InvalidDataException($"R20 promotion economics event {view.Id} could not be re-read");
            return receipt;
        }).ToArray();
    }

    private static int FindEconomicsJournalRow(IReadOnlyList<string> lines, TapeEventID eventID, out int step, out string row)
    {
        step = -1;
        row = "";
        int found = -1;
        int eventIndex = 0;
        foreach (string line in lines)
        {
            if (eventIndex == 0 && line == Journal.LogHeader) continue;
            string[] fields = line.Split('\t');
            if (fields.Length >= 4 && fields[1] == "mint" && fields[2] == eventID.ToString()
                && fields[3] == "eml:theory-grammar-economics")
            {
                if (found >= 0) throw new InvalidDataException($"R20 promotion economics event {eventID} has duplicate journal rows");
                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out step))
                    throw new InvalidDataException($"R20 promotion economics event {eventID} has malformed journal step");
                found = eventIndex;
                row = line;
            }
            eventIndex++;
        }
        if (found < 0) throw new InvalidDataException($"R20 promotion economics event {eventID} lacks its durable journal row");
        return found;
    }

    private static int CountCanonicalStates(string directory)
        => File.Exists(Path.Combine(directory, "journal.log"))
            ? File.ReadLines(Path.Combine(directory, "journal.log"))
                .Count(static line => line.Contains("\tpolicy-trial-rearm\t", StringComparison.Ordinal)
                    && line.Contains("\tstate=", StringComparison.Ordinal))
            : 0;

    private static IReadOnlyList<(int Domain, byte[] Bytes)> ReadPayloads(string path, string glob)
    {
        List<(int Domain, byte[] Bytes)> source = new();
        string[] files = FileCorpus.GatherFiles(path, glob).ToArray();
        for (int domain = 0; domain < files.Length; domain++)
            foreach (string raw in File.ReadLines(files[domain]))
            {
                string text = raw.TrimEnd();
                if (text.Trim().Length != 0) source.Add((domain, Encoding.UTF8.GetBytes(text)));
            }
        return source;
    }

    private static int[] ParseOrder(string value, int domainCount)
    {
        string[] tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != domainCount) throw new InvalidDataException("R20 schedule order does not cover the runtime world domains");
        int[] order = tokens.Select(token => int.Parse(token, CultureInfo.InvariantCulture)).ToArray();
        if (order.Distinct().Count() != domainCount || order.Any(item => item < 0 || item >= domainCount))
            throw new InvalidDataException("R20 schedule order is not a domain permutation");
        return order;
    }

    private static int[] BuildRoundRobinSequence(IReadOnlyList<(int Domain, byte[] Bytes)> source, int domainCount)
    {
        int[] counts = CountDomains(source, domainCount);
        List<int> sequence = new(source.Count);
        for (int row = 0; row < counts.Max(); row++)
            for (int domain = 0; domain < counts.Length; domain++)
                if (row < counts[domain]) sequence.Add(domain);
        return sequence.ToArray();
    }

    private static int[] BuildEpochSequence(IReadOnlyList<(int Domain, byte[] Bytes)> source, IReadOnlyList<int> order)
    {
        int[] counts = CountDomains(source, order.Count);
        List<int> sequence = new(source.Count);
        foreach (int domain in order)
            for (int i = 0; i < counts[domain]; i++) sequence.Add(domain);
        return sequence.ToArray();
    }

    private static int[] CountDomains(IReadOnlyList<(int Domain, byte[] Bytes)> source, int domainCount)
    {
        int[] counts = new int[domainCount];
        foreach ((int domain, _) in source)
        {
            if ((uint)domain >= (uint)domainCount) throw new InvalidDataException("R20 payload names an unknown world domain");
            counts[domain]++;
        }
        if (counts.Any(static count => count == 0)) throw new InvalidDataException("R20 world domain is empty");
        return counts;
    }

    private readonly record struct OrganicRow(int Step, int RawCandidate, int Launchpad, bool Paid, bool Diverged);
}
