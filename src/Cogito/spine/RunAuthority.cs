namespace Cogito;

using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using Ronmamon;

/// The run-owned authority consumed by read-only paired adjudicators. It is emitted only
/// after the ordinary Cortex run has landed its semantic artifacts and finalized its plot
/// writers. Renderer files are named omissions, never silently part of the closure.
internal sealed class RunAuthority
{
    internal const int SchemaVersion = 2;
    internal const string FileName = "run-authority.ron";
    internal const string DigestFileName = "run-authority.digest";

    private static readonly string[] RequiredArtifacts =
    [
        global::Cogito.Checkpoint.FileName,
        "config.txt",
        "execution-window.ron",
        "curve.tsv",
        "compute.tsv",
        "compute.report.tsv",
        "journal.log",
        "grammar.txt",
        "sample.txt",
        "selfstream.txt",
        "excursions.txt",
    ];

    internal string Schema { get; init; } = global::Cogito.Checkpoint.CurrentDialect;
    internal string RunID { get; init; } = "";
    internal string ConfigFingerprint { get; init; } = "";
    internal string PersistedConfigDigest { get; init; } = "";
    internal string WorldSHA256 { get; init; } = "";
    internal EmlDeliberationQuota DeliberationBudget { get; init; } = EmlDeliberationQuota.Default;
    internal RunAuthorityBinary Binary { get; init; } = new();
    internal RunAuthoritySwitches Switches { get; init; } = new();
    internal RunAuthorityCheckpoint Checkpoint { get; init; } = new();
    internal List<RunAuthorityArtifact> Artifacts { get; init; } = [];
    internal List<RunAuthorityOmission> Omissions { get; init; } = [];
    internal bool Complete { get; init; }
    internal string Digest { get; private set; } = "";

    internal static void WriteCompleted(Run run, CortexRunConfig config, int nextStep)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (nextStep < 0) throw new ArgumentOutOfRangeException(nameof(nextStep));
        Stopwatch authorityClock = Stopwatch.StartNew();
        string persistedDigest = Cortex.PersistedConfigDigest(config);
        string configFingerprint = Cortex.ArmNeutralPersistedConfigDigest(config);
        string configText = File.Exists(run.PathOf("config.txt")) ? File.ReadAllText(run.PathOf("config.txt")) : "";
        if (!configText.Contains($"persisted_config_digest={persistedDigest}", StringComparison.Ordinal))
            throw new InvalidDataException("run authority requires config.txt persisted_config_digest binding");

        RunAuthorityCheckpoint checkpoint = ReadCheckpoint(run, persistedDigest, nextStep);
        long checkpointMs = authorityClock.ElapsedMilliseconds;
        (List<RunAuthorityArtifact> artifacts, List<RunAuthorityOmission> omissions) = CollectClosure(run);
        long closureMs = authorityClock.ElapsedMilliseconds - checkpointMs;
        RequireArtifacts(run, artifacts);
        RunAuthority authority = new()
        {
            Schema = global::Cogito.Checkpoint.CurrentDialect,
            RunID = Path.GetFileName(run.Dir),
            ConfigFingerprint = configFingerprint,
            PersistedConfigDigest = persistedDigest,
            WorldSHA256 = config.ExpectedWorldSHA256,
            DeliberationBudget = config.EmlDeliberationBudget,
            Binary = CaptureBinary(),
            Switches = new RunAuthoritySwitches
            {
                PolicyAuthorityCeiling = config.PolicyAuthorityCeiling.ToString(),
                PolicyTrialAllocationArmSteps = config.PolicyTrialAllocationArmSteps,
                PolicyTrialAllocationIdentity = config.PolicyTrialAllocationIdentity,
                PolicyTrialAllocationDigest = config.PolicyTrialAllocationArmSteps > 0
                    ? CortexPolicyTrialAllocation.ComputeDigest(Homeostat.PolicyID, config.PolicyTrialAllocationAuthority,
                        config.PolicyTrialAllocationArmSteps, config.PolicyTrialAllocationIdentity)
                    : "",
                ProcessCatalog = config.EmlProcessCatalog.ToString(),
                Rung0 = config.EmlRung0.ToString(),
                Deliberation = config.EmlDeliberation.ToString(),
            },
            Checkpoint = checkpoint,
            Artifacts = artifacts,
            Omissions = omissions,
            Complete = true,
            Digest = "",
        };
        authority.Digest = ComputeDigest(authority);
        byte[] encoded = Encode(authority);
        run.WriteAtomic(FileName, stream => stream.Write(encoded));
        byte[] sidecar = Encoding.UTF8.GetBytes(authority.Digest + "\n");
        run.WriteAtomic(DigestFileName, stream => stream.Write(sidecar));
        Trace.Cortex.Boundary("authority.seal", $"checkpoint={checkpointMs}ms closure={closureMs}ms artifacts={artifacts.Count} omissions={omissions.Count} bytes={encoded.Length} total={authorityClock.ElapsedMilliseconds}ms");
    }

    internal static RunAuthority Load(string runDirectory)
    {
        return Load(runDirectory, null, null);
    }

    /// Load an authority against an already captured checkpoint/Vow snapshot.
    /// Paired adjudication uses this path so closure validation does not replay
    /// the checkpoint verifier or read the effective image a second time.
    internal static RunAuthority Load(string runDirectory, byte[]? effectiveImage, CheckpointVowReceipt? prevalidatedVow)
    {
        string? resolved = Run.Resolve(runDirectory);
        if (resolved is null) throw new DirectoryNotFoundException($"run dir not found: {runDirectory}");
        RunAuthority authority = LoadIdentity(resolved);
        authority.ValidateOnDisk(resolved, effectiveImage, prevalidatedVow);
        return authority;
    }

    /// Recheck the complete semantic closure after a read-only consumer has
    /// decoded its evidence. The authority remains the expected path+digest
    /// list; this pass catches a late mutation without replaying checkpoint or
    /// grammar state.
    internal bool ClosureMatches(string runDirectory, out string error)
    {
        try
        {
            (List<RunAuthorityArtifact> actual, List<RunAuthorityOmission> omitted) = CollectClosure(Run.Open(runDirectory));
            RequireArtifacts(Run.Open(runDirectory), actual);
            if (!SequenceEqual(Artifacts, actual) || !SequenceEqual(Omissions, omitted))
            {
                error = "completed authority closure changed after arm evidence was read";
                return false;
            }
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            error = $"completed authority closure recheck failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// Read only the immutable completion identity. The full loader replays the
    /// current verifier against every artifact; rerun admission must first tell
    /// a finished historical arm from an interrupted directory without letting a
    /// newer verifier reinterpret that finished evidence. Adjudication still
    /// uses Load and therefore remains fail-closed on any artifact drift.
    internal static RunAuthority LoadIdentity(string runDirectory)
    {
        string? resolved = Run.Resolve(runDirectory);
        if (resolved is null) throw new DirectoryNotFoundException($"run dir not found: {runDirectory}");
        string authorityPath = Path.Combine(resolved, FileName);
        if (!File.Exists(authorityPath)) throw new FileNotFoundException("run authority is missing", authorityPath);
        byte[] bytes = File.ReadAllBytes(authorityPath);
        RunAuthority authority = Decode(bytes);
        if (!Encode(authority).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("run authority Save∘Load∘Save changed bytes");
        string sidecar = File.Exists(Path.Combine(resolved, DigestFileName))
            ? File.ReadAllText(Path.Combine(resolved, DigestFileName)).Trim()
            : throw new InvalidDataException("run authority digest sidecar is missing");
        if (!string.Equals(sidecar, authority.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("run authority digest sidecar disagrees with its RON");
        if (!authority.Complete) throw new InvalidDataException("run authority is not complete");
        if (!string.Equals(authority.RunID, Run.RunIDFromDirectory(resolved), StringComparison.Ordinal))
            throw new InvalidDataException("run authority run ID disagrees with its directory");
        return authority;
    }

    internal static RunAuthorityBinary CurrentBinaryIdentity() => CaptureBinary();

    internal static bool VerifyFixture(TextWriter output)
    {
        string root = Run.HomePath($".run-authority-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string corpus = Path.Combine(root, "corpus.txt");
            File.WriteAllText(corpus, "alpha beta gamma\n");
            Run run = Run.Create(Path.Combine(root, "ordinary"));
            CortexConfig config = new()
            {
                Steps = 1,
                Seed = 0xA11CEUL,
                Curriculum = new CortexFlatPoolCurriculum
                {
                    Corpus = new CogitoCorpus { Path = corpus },
                    IntakeBatch = 1,
                    SeedSpans = 1,
                },
            };
            bool drove = new Cortex(config).Run(run) == 0;
            bool collisionRejected;
            try { _ = Run.Create(Path.Combine(root, "ordinary")); collisionRejected = false; }
            catch (IOException) { collisionRejected = true; }
            bool noSuffix = Directory.GetDirectories(root, "ordinary*").Length == 1;
            byte[] first = File.ReadAllBytes(run.PathOf(FileName));
            RunAuthority loaded = Load(run.Dir);
            bool saveLoadSaveExact = first.AsSpan().SequenceEqual(Encode(loaded));
            string originalConfig = File.ReadAllText(run.PathOf("config.txt"));
            File.AppendAllText(run.PathOf("config.txt"), "corrupt-source\n");
            bool sourceCorruptionRejected;
            try { _ = Load(run.Dir); sourceCorruptionRejected = false; }
            catch (InvalidDataException) { sourceCorruptionRejected = true; }
            File.WriteAllText(run.PathOf("config.txt"), originalConfig);
            bool switchExact = loaded.Switches.Deliberation == EmlDeliberationModes.Adaptive.ToString();
            bool pass = drove && collisionRejected && noSuffix && saveLoadSaveExact && switchExact && sourceCorruptionRejected;
            output.WriteLine($"  run authority · destination={(collisionRejected ? "exact-refusal" : "SUFFIXED")} · serialization={(saveLoadSaveExact ? "byte-exact" : "DRIFT")} · switches={(switchExact ? "exact" : "DRIFT")} · source-corruption={(sourceCorruptionRejected ? "rejected" : "ACCEPTED")} · dirs={(noSuffix ? "no-suffix" : "SUFFIXED")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static bool Profile(string runDirectory, TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(output);
        string? source = Run.Resolve(runDirectory);
        if (source is null) throw new DirectoryNotFoundException($"run dir not found: {runDirectory}");

        string runsRoot = Path.GetDirectoryName(Run.HomePath("profile-placeholder"))!;
        string profileRoot = Path.GetFullPath(Path.Combine(runsRoot, "..", ".tmp"));
        Directory.CreateDirectory(profileRoot);
        string destination = Path.Combine(profileRoot, $"run-authority-profile_{Guid.NewGuid():N}");
        CopyDirectory(source, destination);
        File.Delete(Path.Combine(destination, FileName));
        File.Delete(Path.Combine(destination, DigestFileName));

        Run copy = Run.Open(destination);
        CortexRunConfig config = global::Cogito.Checkpoint.PeekConfig(destination);
        int nextStep = global::Cogito.Checkpoint.NextStep(destination);
        WriteCompleted(copy, config, nextStep);
        byte[] promoted = Cortex.PromoteReadOnlyCheckpointV3(destination);
        copy.WriteAtomic(global::Cogito.Checkpoint.FileName, stream => stream.Write(promoted));
        File.Delete(Path.Combine(destination, global::Cogito.Checkpoint.DeltaFileName));
        File.Delete(Path.Combine(destination, global::Cogito.Checkpoint.DeltaTailFileName));
        File.Delete(Path.Combine(destination, FileName));
        File.Delete(Path.Combine(destination, DigestFileName));
        Trace.Cortex.Boundary("authority.profile.promote", $"wire=v3 image={promoted.LongLength}");
        WriteCompleted(copy, config, nextStep);
        output.WriteLine($"  authority profile · source={source} · copy={destination} · next_step={nextStep}");
        return true;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: false);
        }
    }

    private static RunAuthorityCheckpoint ReadCheckpoint(Run run, string persistedDigest, int nextStep,
        byte[]? effectiveImage = null, CheckpointVowReceipt? prevalidatedVow = null)
    {
        Stopwatch checkpointClock = Stopwatch.StartNew();
        CheckpointDeltaAuthority rail = CheckpointDelta.ReadAuthority(run.Dir);
        if (nextStep != rail.LastToStep)
            throw new InvalidDataException($"run authority terminal horizon disagrees with mutation rail: expected {nextStep}, got {rail.LastToStep}");
        (byte[] Image, string BasePhysicalSHA256, string ChainSHA256)? snapshot = null;
        if (effectiveImage is null)
            snapshot = CheckpointDelta.ReadEffectiveSnapshot(run.Dir);
        long snapshotMs = checkpointClock.ElapsedMilliseconds;
        CheckpointVowReceipt vow = prevalidatedVow ?? (snapshot is { } captured
            ? Cortex.VerifyReadOnlyCheckpointVow(run.Dir, captured.Image, captured.BasePhysicalSHA256, captured.ChainSHA256)
            : Cortex.VerifyReadOnlyCheckpointVow(run.Dir));
        long vowMs = checkpointClock.ElapsedMilliseconds - snapshotMs;
        if (!vow.Passed)
            throw new InvalidDataException($"run authority checkpoint Vow failed: {vow.Summary}");
        byte[] baseImage = File.ReadAllBytes(run.PathOf(global::Cogito.Checkpoint.FileName));
        int baseStep = global::Cogito.Checkpoint.PeekNextStep(baseImage);
        if (baseStep != rail.BaseStep)
            throw new InvalidDataException($"run authority base horizon disagrees with mutation rail: image={baseStep}, rail={rail.BaseStep}");
        if (!string.Equals(vow.BasePhysicalSHA256, rail.BasePhysicalSHA256, StringComparison.Ordinal)
            || !string.Equals(vow.ChainSHA256, rail.ChainSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("run authority checkpoint Vow disagrees with mutation rail authority");
        _ = global::Cogito.Checkpoint.ReadImageProof(baseImage, vow.BasePhysicalSHA256, vow.ChainSHA256, persistedDigest, baseStep, saveLoadSaveExact: vow.Passed);
        ReadOnlySpan<byte> effective = effectiveImage ?? snapshot!.Value.Image;
        CheckpointRoundTripProof proof = global::Cogito.Checkpoint.ReadImageProof(
            effective, vow.BasePhysicalSHA256, vow.ChainSHA256, persistedDigest, rail.LastToStep, saveLoadSaveExact: vow.Passed);
        long proofMs = checkpointClock.ElapsedMilliseconds - snapshotMs - vowMs;
        long imageBytes = effectiveImage?.LongLength ?? snapshot?.Image.LongLength ?? 0;
        Trace.Cortex.Boundary("authority.checkpoint", $"snapshot_ms={snapshotMs} vow_ms={vowMs} proof_ms={proofMs} image={imageBytes} base_step={baseStep} terminal_step={rail.LastToStep} records={rail.RecordCount}");
        if (proof.EffectivePhysicalSHA256 != vow.EffectivePhysicalSHA256
            || proof.BasePhysicalSHA256 != vow.BasePhysicalSHA256
            || proof.PhysicalChainSHA256 != vow.ChainSHA256)
            throw new InvalidDataException("run authority checkpoint proof disagrees with the measured Vow identities");
        return new RunAuthorityCheckpoint
        {
            LogicalSHA256 = proof.EffectiveImageSHA256,
            PhysicalSHA256 = proof.EffectivePhysicalSHA256,
            BasePhysicalSHA256 = proof.BasePhysicalSHA256,
            PhysicalChainSHA256 = proof.PhysicalChainSHA256,
            BaseStep = baseStep,
            NextStep = rail.LastToStep,
            SaveLoadSaveExact = proof.SaveLoadSaveExact,
        };
    }

    private static (List<RunAuthorityArtifact>, List<RunAuthorityOmission>) CollectClosure(Run run)
    {
        List<RunAuthorityArtifact> artifacts = [];
        List<RunAuthorityOmission> omissions = [];
        foreach (string path in Directory.EnumerateFiles(run.Dir, "*", SearchOption.AllDirectories))
        {
            string relative = Normalize(Path.GetRelativePath(run.Dir, path));
            if (relative.Equals(FileName, StringComparison.Ordinal) || relative.Equals(DigestFileName, StringComparison.Ordinal))
                continue;
            if (relative.EndsWith(".tmp", StringComparison.Ordinal))
                throw new InvalidDataException($"run authority found an unfinished temporary artifact: {relative}");
            if (IsRendererArtifact(relative))
            {
                omissions.Add(new RunAuthorityOmission { RelativePath = relative, Reason = "renderer output is nondeterministic and outside semantic adjudication" });
                continue;
            }
            artifacts.Add(new RunAuthorityArtifact
            {
                RelativePath = relative,
                SHA256 = HashArtifact(path),
            });
        }
        artifacts.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
        omissions.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
        return (artifacts, omissions);
    }

    private static string HashArtifact(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, options: FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void RequireArtifacts(Run run, List<RunAuthorityArtifact> artifacts)
    {
        foreach (string required in RequiredArtifacts)
            if (!artifacts.Any(item => item.RelativePath.Equals(required, StringComparison.Ordinal)))
                throw new InvalidDataException($"run authority is incomplete: required semantic artifact is missing: {required}");
    }

    private void ValidateOnDisk(string runDirectory, byte[]? effectiveImage = null, CheckpointVowReceipt? prevalidatedVow = null)
    {
        if (!Complete) throw new InvalidDataException("run authority is not complete");
        if (!string.Equals(RunID, Run.RunIDFromDirectory(runDirectory), StringComparison.Ordinal))
            throw new InvalidDataException("run authority run ID disagrees with its directory");
        (List<RunAuthorityArtifact> actual, List<RunAuthorityOmission> omitted) = CollectClosure(Run.Open(runDirectory));
        if (!SequenceEqual(Artifacts, actual) || !SequenceEqual(Omissions, omitted))
            throw new InvalidDataException("run authority artifact closure changed");
        RequireArtifacts(Run.Open(runDirectory), actual);
        string configText = File.ReadAllText(Path.Combine(runDirectory, "config.txt"));
        if (!configText.Contains($"persisted_config_digest={PersistedConfigDigest}", StringComparison.Ordinal))
            throw new InvalidDataException("run authority is not bound to config.txt persisted_config_digest");
        if (!configText.Contains($"world_sha256={WorldSHA256}", StringComparison.Ordinal))
            throw new InvalidDataException("run authority is not bound to config.txt world_sha256");
        CortexRunConfig config = effectiveImage is null
            ? global::Cogito.Checkpoint.PeekConfig(runDirectory)
            : global::Cogito.Checkpoint.PeekConfig(effectiveImage);
        if (!string.Equals(Cortex.PersistedConfigDigest(config), PersistedConfigDigest, StringComparison.Ordinal)
            || !string.Equals(Cortex.ArmNeutralPersistedConfigDigest(config), ConfigFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("run authority config fingerprint disagrees with checkpoint config");
        if (!string.Equals(config.ExpectedWorldSHA256, WorldSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("run authority world SHA-256 disagrees with checkpoint config");
        if (DeliberationBudget != config.EmlDeliberationBudget)
            throw new InvalidDataException("run authority deliberation budget disagrees with checkpoint config");
        int nextStep = CheckpointDelta.ReadAuthority(runDirectory).LastToStep;
        RunAuthorityCheckpoint checkpoint = ReadCheckpoint(Run.Open(runDirectory), PersistedConfigDigest, nextStep, effectiveImage, prevalidatedVow);
        if (!checkpoint.Equals(Checkpoint)) throw new InvalidDataException("run authority checkpoint identity changed");
    }

    private static bool SequenceEqual(List<RunAuthorityArtifact> left, List<RunAuthorityArtifact> right)
        => left.Count == right.Count && left.Zip(right).All(pair => pair.First.RelativePath == pair.Second.RelativePath && pair.First.SHA256 == pair.Second.SHA256);

    private static bool SequenceEqual(List<RunAuthorityOmission> left, List<RunAuthorityOmission> right)
        => left.Count == right.Count && left.Zip(right).All(pair => pair.First.RelativePath == pair.Second.RelativePath && pair.First.Reason == pair.Second.Reason);

    private static bool IsRendererArtifact(string relative)
        => relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ComputeDigest(RunAuthority authority)
        => Convert.ToHexStringLower(SHA256.HashData(EncodeDocument(authority, "")));

    private static byte[] Encode(RunAuthority authority)
    {
        if (!authority.Complete) throw new InvalidDataException("run authority must be complete before encoding");
        string expected = ComputeDigest(authority);
        if (!string.Equals(expected, authority.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("run authority digest does not match its payload");
        byte[] first = EncodeDocument(authority, authority.Digest);
        byte[] second = EncodeDocument(authority, authority.Digest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("run authority RON encoding is nondeterministic");
        RunAuthority restored = DecodeDocument(first);
        if (!first.AsSpan().SequenceEqual(EncodeDocument(restored, restored.Digest)))
            throw new InvalidDataException("run authority RON round-trip changed bytes");
        return first;
    }

    private static RunAuthority Decode(ReadOnlySpan<byte> bytes)
    {
        RunAuthority authority = DecodeDocument(bytes);
        string expected = ComputeDigest(authority);
        if (!string.Equals(expected, authority.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("decoded run authority digest does not match its payload");
        return authority;
    }

    private static RunAuthority DecodeDocument(ReadOnlySpan<byte> bytes)
    {
        RunAuthorityRON document = RonSerializer.Deserialize<RunAuthorityRON>(bytes);
        if (document.schemaVersion != SchemaVersion) throw new InvalidDataException($"unsupported run authority schema {document.schemaVersion}");
        RunAuthority authority = new()
        {
            Schema = Require(document.schema, "schema"), RunID = Require(document.runID, "run ID"),
            ConfigFingerprint = Require(document.configFingerprint, "config fingerprint"),
            PersistedConfigDigest = Require(document.persistedConfigDigest, "persisted config digest"),
            WorldSHA256 = RequireWorldSHA256(document.worldSHA256),
            DeliberationBudget = ReadQuota(document.deliberationBudget),
            Binary = ReadBinary(document.binary),
            Switches = new RunAuthoritySwitches
            {
                PolicyAuthorityCeiling = Require(document.switches.policyAuthorityCeiling, "policy authority ceiling"),
                PolicyTrialAllocationArmSteps = document.switches.policyTrialAllocationArmSteps,
                PolicyTrialAllocationIdentity = document.switches.policyTrialAllocationIdentity,
                PolicyTrialAllocationDigest = document.switches.policyTrialAllocationDigest,
                ProcessCatalog = Require(document.switches.processCatalog, "process catalog"),
                Rung0 = Require(document.switches.rung0, "rung-0 mode"),
                Deliberation = Require(document.switches.deliberation, "deliberation mode"),
            },
            Checkpoint = new RunAuthorityCheckpoint
            {
                LogicalSHA256 = Require(document.checkpoint.logicalSHA256, "checkpoint logical identity"),
                PhysicalSHA256 = Require(document.checkpoint.physicalSHA256, "checkpoint physical identity"),
            BasePhysicalSHA256 = Require(document.checkpoint.basePhysicalSHA256, "checkpoint base identity"),
            PhysicalChainSHA256 = Require(document.checkpoint.physicalChainSHA256, "checkpoint chain identity"),
                BaseStep = document.checkpoint.baseStep, NextStep = document.checkpoint.nextStep, SaveLoadSaveExact = document.checkpoint.saveLoadSaveExact,
            },
            Artifacts = document.artifacts.Select(static item => new RunAuthorityArtifact { RelativePath = item.relativePath, SHA256 = item.sha256 }).ToList(),
            Omissions = document.omissions.Select(static item => new RunAuthorityOmission { RelativePath = item.relativePath, Reason = item.reason }).ToList(),
            Complete = document.complete,
            Digest = Require(document.digest, "authority digest"),
        };
        if (!authority.Checkpoint.IsValid || authority.Artifacts.Any(item => !IsDigest(item.SHA256)))
            throw new InvalidDataException("run authority carries malformed identity or artifact digest");
        return authority;
    }

    private static byte[] EncodeDocument(RunAuthority authority, string digest)
    {
        RunAuthorityRON document = new()
        {
            schemaVersion = SchemaVersion, schema = authority.Schema, runID = authority.RunID,
            configFingerprint = authority.ConfigFingerprint, persistedConfigDigest = authority.PersistedConfigDigest,
            worldSHA256 = authority.WorldSHA256,
            deliberationBudget = WriteQuota(authority.DeliberationBudget),
            binary = WriteBinary(authority.Binary),
            switches = new RunAuthorityRONSwitches
            {
                policyAuthorityCeiling = authority.Switches.PolicyAuthorityCeiling,
                policyTrialAllocationArmSteps = authority.Switches.PolicyTrialAllocationArmSteps,
                policyTrialAllocationIdentity = authority.Switches.PolicyTrialAllocationIdentity,
                policyTrialAllocationDigest = authority.Switches.PolicyTrialAllocationDigest,
                processCatalog = authority.Switches.ProcessCatalog, rung0 = authority.Switches.Rung0,
                deliberation = authority.Switches.Deliberation,
            },
            checkpoint = new RunAuthorityRONCheckpoint
            {
                logicalSHA256 = authority.Checkpoint.LogicalSHA256, physicalSHA256 = authority.Checkpoint.PhysicalSHA256,
                basePhysicalSHA256 = authority.Checkpoint.BasePhysicalSHA256,
                physicalChainSHA256 = authority.Checkpoint.PhysicalChainSHA256,
                baseStep = authority.Checkpoint.BaseStep, nextStep = authority.Checkpoint.NextStep, saveLoadSaveExact = authority.Checkpoint.SaveLoadSaveExact,
            },
            complete = authority.Complete, digest = digest,
        };
        foreach (RunAuthorityArtifact item in authority.Artifacts)
            document.artifacts.Add(new RunAuthorityRONArtifact { relativePath = item.RelativePath, sha256 = item.SHA256 });
        foreach (RunAuthorityOmission item in authority.Omissions)
            document.omissions.Add(new RunAuthorityRONOmission { relativePath = item.RelativePath, reason = item.Reason });
        return RonSerializer.SerializeToUtf8(in document);
    }

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"run authority omits {field}") : value;

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string RequireWorldSHA256(string value)
    {
        FileCorpus.ValidateExpectedWorldSHA256(value);
        return value;
    }

    private static RunAuthorityBinary CaptureBinary()
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidDataException("process image path is unavailable");
        string assemblyPath = typeof(Cortex).Assembly.Location;
        if (!File.Exists(processPath)) throw new FileNotFoundException("process image is missing", processPath);
        string processDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(processPath)));
        bool assemblyIsProcess = !string.IsNullOrWhiteSpace(assemblyPath)
            && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(assemblyPath), StringComparison.Ordinal);
        string assemblyDigest = assemblyIsProcess
            ? ""
            : !string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath)
                ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)))
                : "";
        return new() { ProcessName = Path.GetFileName(processPath), ProcessSHA256 = processDigest, AssemblyName = assemblyIsProcess ? "" : Path.GetFileName(assemblyPath), AssemblySHA256 = assemblyDigest };
    }

    private static RunAuthorityBinary ReadBinary(RunAuthorityRONBinary binary)
    {
        string assemblyName = string.IsNullOrWhiteSpace(binary.assemblyName) ? "" : RequireName(binary.assemblyName, "assembly name");
        string assemblyDigest = string.IsNullOrWhiteSpace(binary.assemblySHA256) ? "" : RequireDigest(binary.assemblySHA256, "assembly digest");
        if ((assemblyName.Length == 0) != (assemblyDigest.Length == 0))
            throw new InvalidDataException("run authority assembly identity must be fully present or fully absent");
        return new()
        {
            ProcessName = RequireName(binary.processName, "process image name"),
            ProcessSHA256 = RequireDigest(binary.processSHA256, "process image digest"),
            AssemblyName = assemblyName,
            AssemblySHA256 = assemblyDigest,
        };
    }

    private static RunAuthorityRONBinary WriteBinary(RunAuthorityBinary binary)
        => new() { processName = binary.ProcessName, processSHA256 = binary.ProcessSHA256, assemblyName = binary.AssemblyName, assemblySHA256 = binary.AssemblySHA256 };

    private static string RequireName(string value, string field)
        => string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\') ? throw new InvalidDataException($"run authority omits valid {field}") : value;

    private static string RequireDigest(string value, string field)
        => IsDigest(value) ? value : throw new InvalidDataException($"run authority omits valid {field}");

    private static EmlDeliberationQuota ReadQuota(RunAuthorityRONQuota quota)
        => new(quota.candidateEvaluations, quota.logicalProgramPoints, quota.executedProgramPoints, quota.inverseTransforms,
            quota.hashProbes, quota.joinAttempts, quota.joinHits, quota.processTerms, quota.verifierProgramPoints,
            quota.candidateSupplyItems, quota.lawRewriteApplications, quota.lawRewriteTreeNodes);

    private static RunAuthorityRONQuota WriteQuota(in EmlDeliberationQuota quota)
        => new()
        {
            candidateEvaluations = quota.CandidateEvaluations, logicalProgramPoints = quota.LogicalProgramPoints,
            executedProgramPoints = quota.ExecutedProgramPoints, inverseTransforms = quota.InverseTransforms,
            hashProbes = quota.HashProbes, joinAttempts = quota.JoinAttempts, joinHits = quota.JoinHits,
            processTerms = quota.ProcessTerms, verifierProgramPoints = quota.VerifierProgramPoints,
            candidateSupplyItems = quota.CandidateSupplyItems, lawRewriteApplications = quota.LawRewriteApplications,
            lawRewriteTreeNodes = quota.LawRewriteTreeNodes,
        };
}

internal sealed class RunAuthoritySwitches
{
    internal string PolicyAuthorityCeiling { get; init; } = "";
    internal long PolicyTrialAllocationArmSteps { get; init; }
    internal string PolicyTrialAllocationIdentity { get; init; } = "";
    internal string PolicyTrialAllocationDigest { get; init; } = "";
    internal string ProcessCatalog { get; init; } = "";
    internal string Rung0 { get; init; } = "";
    internal string Deliberation { get; init; } = "";
}

internal sealed record class RunAuthorityBinary
{
    internal string ProcessName { get; init; } = "";
    internal string ProcessSHA256 { get; init; } = "";
    internal string AssemblyName { get; init; } = "";
    internal string AssemblySHA256 { get; init; } = "";
}

internal sealed record class RunAuthorityCheckpoint
{
    internal string LogicalSHA256 { get; init; } = "";
    internal string PhysicalSHA256 { get; init; } = "";
    internal string BasePhysicalSHA256 { get; init; } = "";
    internal string PhysicalChainSHA256 { get; init; } = "";
    internal int BaseStep { get; init; }
    internal int NextStep { get; init; }
    internal bool SaveLoadSaveExact { get; init; }
    internal bool IsValid => IsDigest(LogicalSHA256) && IsDigest(PhysicalSHA256) && IsDigest(BasePhysicalSHA256) && IsDigest(PhysicalChainSHA256) && BaseStep >= 0 && NextStep >= BaseStep;
    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal sealed class RunAuthorityArtifact
{
    internal string RelativePath { get; init; } = "";
    internal string SHA256 { get; init; } = "";
}

internal sealed class RunAuthorityOmission
{
    internal string RelativePath { get; init; } = "";
    internal string Reason { get; init; } = "";
}

[RonObject]
internal partial class RunAuthorityRON
{
    public int schemaVersion;
    public string schema = "";
    public string runID = "";
    public string configFingerprint = "";
    public string persistedConfigDigest = "";
    public string worldSHA256 = "";
    public RunAuthorityRONQuota deliberationBudget = new();
    public RunAuthorityRONBinary binary = new();
    public RunAuthorityRONSwitches switches = new();
    public RunAuthorityRONCheckpoint checkpoint = new();
    public List<RunAuthorityRONArtifact> artifacts = new();
    public List<RunAuthorityRONOmission> omissions = new();
    public bool complete;
    public string digest = "";
}

[RonObject]
internal partial class RunAuthorityRONBinary
{
    public string processName = "";
    public string processSHA256 = "";
    public string assemblyName = "";
    public string assemblySHA256 = "";
}

[RonObject]
internal partial class RunAuthorityRONQuota
{
    public long candidateEvaluations;
    public long logicalProgramPoints;
    public long executedProgramPoints;
    public long inverseTransforms;
    public long hashProbes;
    public long joinAttempts;
    public long joinHits;
    public long processTerms;
    public long verifierProgramPoints;
    public long candidateSupplyItems;
    public long lawRewriteApplications;
    public long lawRewriteTreeNodes;
}

[RonObject]
internal partial class RunAuthorityRONSwitches
{
    public string policyAuthorityCeiling = "";
    public long policyTrialAllocationArmSteps;
    public string policyTrialAllocationIdentity = "";
    public string policyTrialAllocationDigest = "";
    public string processCatalog = "";
    public string rung0 = "";
    public string deliberation = "";
}

[RonObject]
internal partial class RunAuthorityRONCheckpoint
{
    public string logicalSHA256 = "";
    public string physicalSHA256 = "";
    public string basePhysicalSHA256 = "";
    public string physicalChainSHA256 = "";
    public int baseStep;
    public int nextStep;
    public bool saveLoadSaveExact;
}

[RonObject]
internal partial class RunAuthorityRONArtifact
{
    public string relativePath = "";
    public string sha256 = "";
}

[RonObject]
internal partial class RunAuthorityRONOmission
{
    public string relativePath = "";
    public string reason = "";
}
