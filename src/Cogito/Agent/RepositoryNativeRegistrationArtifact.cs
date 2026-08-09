namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// Source identity for a native repository run. The digest binds the exact world
/// bytes and query contract; a registration cannot be moved to another intake
/// without changing this corroboration.
public sealed class RepositoryNativeSourceAuthorityCorroboration
{
    private RepositoryNativeSourceAuthorityCorroboration(
        string rootPath,
        string glob,
        string query,
        string querySHA256,
        string worldContentSHA256,
        string authoritySHA256)
    {
        RootPath = rootPath;
        Glob = glob;
        Query = query;
        QuerySHA256 = querySHA256;
        WorldContentSHA256 = worldContentSHA256;
        AuthoritySHA256 = authoritySHA256;
    }

    public string RootPath { get; }
    public string Glob { get; }
    public string Query { get; }
    public string QuerySHA256 { get; }
    public string WorldContentSHA256 { get; }
    public string AuthoritySHA256 { get; }

    public static RepositoryNativeSourceAuthorityCorroboration Create(
        string rootPath,
        string glob,
        string query,
        string querySHA256,
        RepositoryLoopClosureWorldSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Validate();
        rootPath = NormalizeRoot(rootPath);
        glob = NormalizeGlob(glob);
        query = NormalizeQuery(query);
        RequireSHA(querySHA256, "query");
        string expectedQuerySHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(query + "\n")));
        if (!string.Equals(querySHA256, expectedQuerySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("native repository query digest is not the trimmed query plus newline");
        string authority = ComputeAuthority(rootPath, glob, query, querySHA256, world.ContentSHA256);
        return new(rootPath, glob, query, querySHA256, world.ContentSHA256, authority);
    }

    public static RepositoryNativeSourceAuthorityCorroboration Create(
        string rootPath,
        string glob,
        string query,
        string querySHA256,
        Tool.RepositoryWorldSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        RepositoryLoopClosureWorldSnapshot snapshot = RepositoryNativeRegistrationArtifact.CaptureWorld(world);
        return Create(rootPath, glob, query, querySHA256, snapshot);
    }

    public void Validate(RepositoryLoopClosureWorldSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Validate();
        if (RootPath != NormalizeRoot(RootPath) || Glob != NormalizeGlob(Glob) || Query != NormalizeQuery(Query))
            throw new InvalidDataException("native repository source authority is not normalized");
        RequireText(RootPath, "source root");
        RequireText(Glob, "source glob");
        RequireText(Query, "source query");
        RequireSHA(QuerySHA256, "query");
        if (QuerySHA256 != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Query + "\n"))))
            throw new InvalidDataException("native repository query digest is not the trimmed query plus newline");
        RequireSHA(WorldContentSHA256, "source world");
        RequireSHA(AuthoritySHA256, "source authority");
        if (WorldContentSHA256 != world.ContentSHA256
            || AuthoritySHA256 != ComputeAuthority(RootPath, Glob, Query, QuerySHA256, world.ContentSHA256))
            throw new InvalidDataException("native repository source authority diverges from the exact world snapshot");
    }

    public void Validate(Tool.RepositoryWorldSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (RootPath != NormalizeRoot(world.RootPath) || Glob != NormalizeGlob(world.Glob))
            throw new InvalidDataException("native repository source authority is paired with a different crawler world");
        Validate(RepositoryNativeRegistrationArtifact.CaptureWorld(world));
    }

    private static string ComputeAuthority(string rootPath, string glob, string query, string querySHA256, string worldSHA256)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "repository-native-source-v1", rootPath, glob, query, querySHA256, worldSHA256))));

    private static void RequireText(string value, string role)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"native repository {role} is empty");
    }

    private static void RequireSHA(string value, string role)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit)
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException($"native repository {role} digest is malformed");
    }

    private static string NormalizeRoot(string value)
    {
        RequireText(value, "source root");
        string full = Path.GetFullPath(value);
        string root = Path.GetPathRoot(full) ?? full;
        return string.Equals(full, root, StringComparison.Ordinal)
            ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeGlob(string value)
    {
        RequireText(value, "source glob");
        return value.Trim();
    }

    private static string NormalizeQuery(string value)
    {
        RequireText(value, "source query");
        return value.Trim();
    }
}

/// The deterministic pre-run registration artifact. Its bytes are the exact
/// registration authority consumed by the runner and terminal evidence; the
/// hidden task oracle is retained here and only its prompt view is organism-facing.
public sealed class RepositoryNativeRegistrationArtifact
{
    public const string FileName = "repository-native-registration.ron";
    private const string BundleManifestFileName = "bundle-manifest.ron";
    private const string BundleCompleteMarkerFileName = "bundle.complete";
    private static readonly string[] RequiredBundlePaths =
    [
        "registration.ron", "source-authority.txt", "world-manifest.tsv", "tool-authority.bin",
        "policy-authority.bin", "candidate-authority.txt", "initial-state.bin", "fuel-authority.bin",
        "task-authority.txt", "lineage-null.txt",
    ];

    public readonly record struct Request(
        string PlanID,
        RepositoryNativeSourceAuthorityCorroboration SourceAuthority,
        Tool.RepositoryWorldSnapshot World,
        RepositoryLoopClosureTaskSpec Task,
        RepositoryLoopClosureToolAuthorityCorroboration Tool,
        RepositoryLoopClosurePolicyAuthorityCorroboration Policy,
        RepositoryLoopClosureCandidateSchemaAuthorityCorroboration Candidate,
        RepositoryLoopClosureInitialStateAuthorityCorroboration InitialState,
        RepositoryLoopClosureFuelAuthorityCorroboration Fuel,
        ulong Seed,
        int Horizon,
        long OfferedFuel,
        int OpportunityFloor,
        long DecisionThreshold,
        RepositoryLoopClosureLineageNullSpec LineageNullSpec);

    private readonly byte[] _registrationBytes;
    private readonly IReadOnlyDictionary<string, byte[]> _bundleBytes;

    private RepositoryNativeRegistrationArtifact(
        RepositoryLoopClosureRegistration registration,
        ReadOnlySpan<byte> registrationBytes,
        IReadOnlyDictionary<string, byte[]>? bundleBytes = null)
    {
        Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _registrationBytes = registrationBytes.ToArray();
        if (!Registration.Encode().AsSpan().SequenceEqual(_registrationBytes))
            throw new InvalidDataException("native repository registration bytes diverge from the typed registration");
        ValidateRegistrationCanonical();
        RegistrationDocumentSHA256 = Convert.ToHexStringLower(SHA256.HashData(_registrationBytes));
        Dictionary<string, byte[]> bundle = new(StringComparer.Ordinal);
        if (bundleBytes is not null)
            foreach ((string path, byte[] bytes) in bundleBytes)
                bundle.Add(path, bytes.ToArray());
        _bundleBytes = bundle;
        if (_bundleBytes.Count > 0)
        {
            if (!_bundleBytes.TryGetValue("registration.ron", out byte[]? bundledRegistration)
                || !bundledRegistration.AsSpan().SequenceEqual(_registrationBytes)
                || RequiredBundlePaths.Any(path => !_bundleBytes.ContainsKey(path)))
                throw new InvalidDataException("native repository registration bundle omits its exact RON");
            AuthorityBundleFiles = Array.AsReadOnly(_bundleBytes
                .Select(static pair => new LoopClosureAuthorityBundleFile(pair.Key,
                    Convert.ToHexStringLower(SHA256.HashData(pair.Value))))
                .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
                .ToArray());
            ValidateBundleSemantics();
            AuthorityBundleSHA256 = new RepositoryLoopClosureAuthoritySnapshot(Registration, AuthorityBundleFiles).AuthoritySHA256;
        }
        else
        {
            AuthorityBundleFiles = Array.Empty<LoopClosureAuthorityBundleFile>();
            AuthorityBundleSHA256 = "";
        }
        MountedAuthority = CreateMountedAuthority(Registration, _registrationBytes);
        MountedAuthority.Validate();
    }

    public RepositoryLoopClosureRegistration Registration { get; }
    public ReadOnlyMemory<byte> RegistrationBytes => _registrationBytes;
    public string RegistrationDocumentSHA256 { get; }
    public string RegistrationSHA256 => Registration.RegistrationSHA256;
    public string MountedAuthoritySHA256 => MountedAuthority.authoritySHA256;
    public RepositoryLoopClosureTaskPromptView PromptView => Registration.Task.PromptView;
    public IReadOnlyList<LoopClosureAuthorityBundleFile> AuthorityBundleFiles { get; }
    public string AuthorityBundleSHA256 { get; }

    public RepositoryLoopClosureAuthoritySnapshot CreateAuthoritySnapshot()
    {
        if (AuthorityBundleFiles.Count == 0)
            throw new InvalidOperationException("native repository authority bundle has not been materialized");
        RepositoryLoopClosureAuthoritySnapshot snapshot = new(Registration, AuthorityBundleFiles);
        snapshot.Validate();
        return snapshot;
    }

    // Terminal evidence consumes this exact typed block; no digest is re-derived
    // by the caller, and the oracle remains behind RepositoryLoopClosureTaskSpec.
    internal RepositoryNativeRegisteredAuthorityRON MountedAuthority { get; }

    public static RepositoryNativeRegistrationArtifact Mint(in Request request)
    {
        ArgumentNullException.ThrowIfNull(request.SourceAuthority);
        ArgumentNullException.ThrowIfNull(request.World);
        ArgumentNullException.ThrowIfNull(request.Task);
        ArgumentNullException.ThrowIfNull(request.Tool);
        ArgumentNullException.ThrowIfNull(request.Policy);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentNullException.ThrowIfNull(request.InitialState);
        ArgumentNullException.ThrowIfNull(request.Fuel);
        ArgumentNullException.ThrowIfNull(request.LineageNullSpec);

        RepositoryLoopClosureWorldSnapshot world = CaptureWorld(request.World);
        request.SourceAuthority.Validate(request.World);
        request.Task.Validate();
        ValidateTaskOracle(world, request.Task);
        request.Tool.Validate();
        request.Policy.Validate();
        request.Candidate.Validate();
        request.InitialState.Validate();
        request.Fuel.Validate();
        request.LineageNullSpec.Validate();

        if (string.IsNullOrWhiteSpace(request.PlanID) || request.Horizon <= 0
            || request.OfferedFuel < 0 || request.OpportunityFloor < 0 || request.DecisionThreshold < 0
            || request.OpportunityFloor != 0 || request.DecisionThreshold != 0)
            throw new InvalidDataException("native repository registration request is malformed");
        if (request.InitialState.Digest != RepositoryLoopClosureInitialStateAuthorityCorroboration.Create(request.Seed, request.Horizon).Digest)
            throw new InvalidDataException("native repository initial-state witness does not match seed and horizon");
        if (request.Fuel.Digest != RepositoryLoopClosureFuelAuthorityCorroboration.Create(request.OfferedFuel).Digest)
            throw new InvalidDataException("native repository fuel witness does not match offered fuel");

        RepositoryLoopClosureRegistration registration = new(
            request.PlanID,
            request.SourceAuthority.AuthoritySHA256,
            world.ContentSHA256,
            world.SnapshotSHA256,
            request.Tool.Digest,
            request.Policy.Digest,
            request.Candidate.Digest,
            request.InitialState.Digest,
            request.Seed,
            request.Horizon,
            request.OfferedFuel,
            request.Fuel.Digest,
            request.OpportunityFloor,
            request.DecisionThreshold,
            request.Task,
            request.LineageNullSpec);
        byte[] bytes = registration.Encode();
        return new(registration, bytes, CreateBundleBytes(request, world, bytes));
    }

    public static RepositoryNativeRegistrationArtifact Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        RepositoryLoopClosureRegistration registration = RepositoryLoopClosureRegistration.Decode(bytes);
        return new(registration, bytes);
    }

    public static RepositoryNativeRegistrationArtifact Read(string registrationPath, string bundleDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        byte[] registrationBytes = File.ReadAllBytes(registrationPath);
        RepositoryLoopClosureRegistration registration = RepositoryLoopClosureRegistration.Decode(registrationBytes);
        Dictionary<string, byte[]> bundle = ReadBundle(bundleDirectory);
        return new(registration, registrationBytes, bundle);
    }

    public static RepositoryNativeRegistrationArtifact Read(
        string registrationPath,
        string bundleDirectory,
        Tool.RepositoryWorldSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        RepositoryNativeRegistrationArtifact artifact = Read(registrationPath, bundleDirectory);
        RepositoryLoopClosureWorldSnapshot snapshot = CaptureWorld(world);
        if (artifact.Registration.WorldContentSHA256 != snapshot.ContentSHA256
            || artifact.Registration.WorldSnapshotSHA256 != snapshot.SnapshotSHA256)
            throw new InvalidDataException("native repository registration world authority diverges on readback");
        ValidateTaskOracle(snapshot, artifact.Registration.Task);
        return artifact;
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        WriteAtomicFile(fullPath, _registrationBytes);
        byte[] persisted = File.ReadAllBytes(fullPath);
        if (!persisted.AsSpan().SequenceEqual(_registrationBytes))
            throw new InvalidDataException("native repository registration persisted bytes changed");
        _ = Read(fullPath);
    }

    public void Write(Run run, string file = FileName)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        run.WriteAtomic(file, stream => stream.Write(_registrationBytes));
        byte[] persisted = File.ReadAllBytes(run.PathOf(file));
        if (!persisted.AsSpan().SequenceEqual(_registrationBytes))
            throw new InvalidDataException("native repository registration persisted bytes changed");
        _ = RepositoryLoopClosureRegistration.Decode(persisted);
    }

    public void Write(string registrationPath, string bundleDirectory)
    {
        Write(registrationPath);
        WriteBundle(bundleDirectory);
    }

    public void WriteBundle(string directory)
    {
        if (_bundleBytes.Count == 0)
            throw new InvalidOperationException("native repository authority bundle is not available");
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        foreach ((string relativePath, byte[] bytes) in _bundleBytes)
        {
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            WriteAtomicFile(path, bytes);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException($"native authority bundle bytes changed: {relativePath}");
        }
        RepositoryNativeRegistrationBundleManifestRON manifest = new();
        foreach (LoopClosureAuthorityBundleFile file in AuthorityBundleFiles)
            manifest.files.Add(new RepositoryNativeRegistrationBundleFileRON
            {
                relativePath = file.RelativePath,
                sha256 = file.SHA256,
            });
        byte[] manifestBytes = RonSerializer.SerializeToUtf8(in manifest);
        string manifestPath = Path.Combine(root, BundleManifestFileName);
        WriteAtomicFile(manifestPath, manifestBytes);
        if (!File.ReadAllBytes(manifestPath).AsSpan().SequenceEqual(manifestBytes))
            throw new InvalidDataException("native authority bundle manifest bytes changed");
        byte[] markerBytes = Encoding.UTF8.GetBytes(Convert.ToHexStringLower(SHA256.HashData(manifestBytes)) + "\n");
        string markerPath = Path.Combine(root, BundleCompleteMarkerFileName);
        WriteAtomicFile(markerPath, markerBytes);
        if (!File.ReadAllBytes(markerPath).AsSpan().SequenceEqual(markerBytes))
            throw new InvalidDataException("native authority bundle completion marker bytes changed");
    }

    internal RepositoryNativeRegisteredAuthorityRON CreateTerminalAuthority()
        => MountedAuthority;

    internal static RepositoryLoopClosureWorldSnapshot CaptureWorld(Tool.RepositoryWorldSnapshot world)
    {
        ArgumentNullException.ThrowIfNull(world);
        RepositoryLoopClosureWorldSnapshot snapshot = new(world.CaptureFiles()
            .Select(static file => new RepositoryLoopClosureWorldFile(file.Path, file.Content)).ToArray());
        snapshot.Validate();
        if (snapshot.Files.Any(static file => !new LoopClosureAuthorityBundleFile(
            file.Path.Value, file.SHA256).IsValid))
            throw new InvalidDataException("native repository world contains an unsafe relative path");
        if (!string.Equals(snapshot.ContentSHA256, world.WorldSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("native repository world snapshot digest diverges from crawler authority");
        return snapshot;
    }

    private static void ValidateTaskOracle(RepositoryLoopClosureWorldSnapshot world, RepositoryLoopClosureTaskSpec task)
    {
        RepositoryLoopClosureTaskOracle oracle = task.Oracle;
        RequireLowerSHA(oracle.ExpectedSource.SHA256, "task oracle source");
        RequireLowerSHA(oracle.ExpectedResult.SHA256, "task oracle result");
        RepositoryLoopClosureWorldFile file = world.Files.SingleOrDefault(item => item.Path.Value == oracle.ExpectedSource.Path)
            ?? throw new InvalidDataException("native repository task oracle source is absent from the exact world snapshot");
        if (file.Bytes != oracle.ExpectedSource.Bytes || file.SHA256 != oracle.ExpectedSource.SHA256)
            throw new InvalidDataException("native repository task oracle source diverges from the exact world snapshot");
        RepositoryLoopClosureExpectedResult derived = oracle.DeriveExpectedResult(task.Species, world);
        if (derived.Species != oracle.ExpectedResult.Species
            || derived.SHA256 != oracle.ExpectedResult.SHA256
            || !derived.Content.Span.SequenceEqual(oracle.ExpectedResult.Content.Span))
            throw new InvalidDataException("native repository task oracle result is not derived from the registered world");
    }

    private static void RequireLowerSHA(string value, string role)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit)
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException($"native repository {role} digest is malformed");
    }

    private static Dictionary<string, byte[]> CreateBundleBytes(
        in Request request,
        RepositoryLoopClosureWorldSnapshot world,
        ReadOnlySpan<byte> registrationBytes)
    {
        RepositoryLoopClosureTaskOracle oracle = request.Task.Oracle;
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            ["registration.ron"] = registrationBytes.ToArray(),
            ["source-authority.txt"] = Encoding.UTF8.GetBytes(string.Join('|',
                "repository-native-source-v1", request.SourceAuthority.RootPath, request.SourceAuthority.Glob,
                request.SourceAuthority.Query, request.SourceAuthority.QuerySHA256, world.ContentSHA256)),
            ["world-manifest.tsv"] = Encoding.UTF8.GetBytes(string.Join('\n', world.Files
                .OrderBy(static file => file.Path.Value, StringComparer.Ordinal)
                .Select(static file => $"{file.Path.Value}\t{file.Bytes}\t{file.SHA256}")) + '\n'),
            ["tool-authority.bin"] = request.Tool.CanonicalBytes.ToArray(),
            ["policy-authority.bin"] = request.Policy.CanonicalBytes.ToArray(),
            ["candidate-authority.txt"] = Encoding.UTF8.GetBytes(request.Candidate.Canonical),
            ["initial-state.bin"] = request.InitialState.CanonicalBytes.ToArray(),
            ["fuel-authority.bin"] = request.Fuel.CanonicalBytes.ToArray(),
            ["task-authority.txt"] = Encoding.UTF8.GetBytes(string.Join('|',
                "repository-loop-task-authority-v3", request.Task.TaskID, request.Task.Species,
                request.Task.Prompt, oracle.AuthoritySHA256)),
            ["lineage-null.txt"] = Encoding.UTF8.GetBytes(string.Join('|',
                "repository-loop-lineage-null-spec-v1", request.LineageNullSpec.Domain,
                request.LineageNullSpec.Algorithm)),
        };
        if (files.Keys.Any(static path => !new LoopClosureAuthorityBundleFile(path, new string('0', 64)).IsValid))
            throw new InvalidDataException("native repository registration bundle path is malformed");
        return files;
    }

    private static Dictionary<string, byte[]> ReadBundle(string directory)
    {
        string root = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(root, BundleManifestFileName);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        string marker = File.ReadAllText(Path.Combine(root, BundleCompleteMarkerFileName));
        if (!string.Equals(marker, Convert.ToHexStringLower(SHA256.HashData(manifestBytes)) + "\n", StringComparison.Ordinal))
            throw new InvalidDataException("native repository authority bundle completion marker diverges");
        RepositoryNativeRegistrationBundleManifestRON manifest = RonSerializer.Deserialize<RepositoryNativeRegistrationBundleManifestRON>(manifestBytes);
        if (manifest.files.Count == 0)
            throw new InvalidDataException("native repository authority bundle manifest is empty");
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (RepositoryNativeRegistrationBundleFileRON entry in manifest.files)
        {
            if (!new LoopClosureAuthorityBundleFile(entry.relativePath, entry.sha256).IsValid
                || files.ContainsKey(entry.relativePath))
                throw new InvalidDataException("native repository authority bundle manifest is malformed");
            string path = Path.Combine(root, entry.relativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = File.ReadAllBytes(path);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != entry.sha256)
                throw new InvalidDataException($"native repository authority bundle digest diverges: {entry.relativePath}");
            files[entry.relativePath] = bytes;
        }
        return files;
    }

    private static void WriteAtomicFile(string path, ReadOnlySpan<byte> bytes)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp";
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, fullPath, overwrite: true);
    }

    private void ValidateBundleSemantics()
    {
        RequireBundleDigest("registration.ron", RegistrationDocumentSHA256);
        RequireBundleDigest("source-authority.txt", Registration.SourceAuthoritySHA256);
        RequireBundleDigest("world-manifest.tsv", Registration.WorldSnapshotSHA256);
        RequireBundleDigest("tool-authority.bin", Registration.ToolAuthoritySHA256);
        RequireBundleDigest("policy-authority.bin", Registration.PolicyAuthoritySHA256);
        RequireBundleDigest("candidate-authority.txt", Registration.CandidateAuthoritySHA256);
        RequireBundleDigest("initial-state.bin", Registration.InitialStateSHA256);
        RequireBundleDigest("fuel-authority.bin", Registration.OfferedFuelSHA256);
        RequireBundleDigest("task-authority.txt", Registration.TaskAuthoritySHA256);
        RequireBundleDigest("lineage-null.txt", Registration.LineageNullSpec.Digest);
    }

    private void ValidateRegistrationCanonical()
    {
        string[] digests =
        [
            Registration.RegistrationSHA256, Registration.SourceAuthoritySHA256, Registration.WorldContentSHA256,
            Registration.WorldSnapshotSHA256, Registration.ToolAuthoritySHA256, Registration.PolicyAuthoritySHA256,
            Registration.CandidateAuthoritySHA256, Registration.InitialStateSHA256, Registration.OfferedFuelSHA256,
            Registration.TaskAuthoritySHA256, Registration.Task.Oracle.AuthoritySHA256, Registration.LineageNullSpec.Digest,
        ];
        if (digests.Any(static digest => digest is not { Length: 64 }
            || !digest.All(Uri.IsHexDigit)
            || !string.Equals(digest, digest.ToLowerInvariant(), StringComparison.Ordinal)))
            throw new InvalidDataException("native repository registration contains a non-canonical digest");
    }

    private void RequireBundleDigest(string relativePath, string expectedSHA256)
    {
        LoopClosureAuthorityBundleFile file = AuthorityBundleFiles.Single(item => item.RelativePath == relativePath);
        if (!string.Equals(file.SHA256, expectedSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"native repository authority bundle semantic digest diverges: {relativePath}");
    }

    private static RepositoryNativeRegisteredAuthorityRON CreateMountedAuthority(
        RepositoryLoopClosureRegistration registration,
        ReadOnlySpan<byte> registrationBytes)
    {
        return RepositoryNativeRegisteredAuthorityRON.Create(
            registration.RegistrationSHA256,
            Convert.ToHexStringLower(SHA256.HashData(registrationBytes)),
            registration.TaskAuthoritySHA256,
            registration.ToolAuthoritySHA256,
            registration.PolicyAuthoritySHA256,
            registration.CandidateAuthoritySHA256,
            registration.InitialStateSHA256,
            registration.OfferedFuelSHA256);
    }
}

[RonObject]
internal partial class RepositoryNativeRegistrationBundleManifestRON
{
    public List<RepositoryNativeRegistrationBundleFileRON> files = new();
}

[RonObject]
internal partial class RepositoryNativeRegistrationBundleFileRON
{
    public string relativePath = "";
    public string sha256 = "";
}
