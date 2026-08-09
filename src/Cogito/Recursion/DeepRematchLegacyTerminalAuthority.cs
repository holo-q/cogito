namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// Read-only adapter for the schema-1/2 terminal authority emitted by the real
/// deep-rematch _0009 children.  The old receipt bytes remain the authority;
/// this type only decodes and checks them before exposing a typed view to the
/// current deep-rematch recovery path.
public sealed class DeepRematchLegacyTerminalAuthority
{
    public const string TerminalRunFileName = "terminal-run-receipt.ron";
    public const string SeedLoadIntentFileName = "seed-load-intent.ron";
    public const string SeedLoadReceiptFileName = "seed-load-receipt.ron";
    public const string TerminalOccurrenceCheckFileName = "terminal-verification.ron";

    private DeepRematchLegacyTerminalAuthority(
        string runDirectory,
        DeepRematchLegacyTerminalRunDocument terminalRun,
        DeepRematchLegacySeedLoadDocument seedLoadIntent,
        DeepRematchLegacySeedLoadDocument seedLoadReceipt,
        DeepRematchLegacyTerminalOccurrenceCheckDocument? terminalOccurrenceCheck)
    {
        RunDirectory = runDirectory;
        TerminalRun = terminalRun;
        SeedLoadIntent = seedLoadIntent;
        SeedLoadReceipt = seedLoadReceipt;
        TerminalOccurrenceCheck = terminalOccurrenceCheck;
    }

    public string RunDirectory { get; }
    public string TerminalRunReceiptPath => Path.Combine(RunDirectory, TerminalRunFileName);
    public string TerminalRunReceiptSHA256 => DigestFile(TerminalRunReceiptPath);
    public string SeedLoadIntentPath => Path.Combine(RunDirectory, SeedLoadIntentFileName);
    public string SeedLoadIntentSHA256 => DigestFile(SeedLoadIntentPath);
    public string SeedLoadReceiptPath => Path.Combine(RunDirectory, SeedLoadReceiptFileName);
    public string SeedLoadReceiptSHA256 => DigestFile(SeedLoadReceiptPath);
    public string? TerminalOccurrenceCheckPath => TerminalOccurrenceCheck is null ? null : Path.Combine(RunDirectory, TerminalOccurrenceCheckFileName);
    public string? TerminalOccurrenceCheckSHA256 => TerminalOccurrenceCheck is null ? null : DigestFile(TerminalOccurrenceCheckPath!);
    public DeepRematchLegacyTerminalRunDocument TerminalRun { get; }
    public DeepRematchLegacySeedLoadDocument SeedLoadIntent { get; }
    public DeepRematchLegacySeedLoadDocument SeedLoadReceipt { get; }
    public DeepRematchLegacyTerminalOccurrenceCheckDocument? TerminalOccurrenceCheck { get; }

    public DeepRematchLegacySeedLoad SeedLoad => SeedLoadReceipt.ToValue();

    public static DeepRematchLegacyTerminalAuthority Read(string runDirectory)
    {
        string directory = Path.GetFullPath(runDirectory);
        DeepRematchLegacyTerminalRunDocument terminalRun = ReadDocument<DeepRematchLegacyTerminalRunDocument>(
            Path.Combine(directory, TerminalRunFileName), "legacy terminal run receipt");
        DeepRematchLegacySeedLoadDocument intent = ReadDocument<DeepRematchLegacySeedLoadDocument>(
            Path.Combine(directory, SeedLoadIntentFileName), "legacy seed-load intent");
        DeepRematchLegacySeedLoadDocument receipt = ReadDocument<DeepRematchLegacySeedLoadDocument>(
            Path.Combine(directory, SeedLoadReceiptFileName), "legacy seed-load receipt");
        DeepRematchLegacyTerminalOccurrenceCheckDocument? verifier = null;
        if (terminalRun.terminalOccurrenceCheckAttempted)
            verifier = ReadDocument<DeepRematchLegacyTerminalOccurrenceCheckDocument>(
                Path.Combine(directory, TerminalOccurrenceCheckFileName), "legacy terminal verifier receipt");

        DeepRematchLegacyTerminalAuthority authority = new(directory, terminalRun, intent, receipt, verifier);
        authority.Validate();
        return authority;
    }

    public DeepRematchLegacyCurrentSeed ToCurrentSeedLoad(in CheckpointRoundTripProof proof)
    {
        DeepRematchLegacySeedLoad seed = SeedLoad;
        if (!proof.IsBound || !proof.SaveLoadSaveExact
            || !string.Equals(proof.EffectiveImageSHA256, seed.ExpectedDigests.CheckpointSHA256, StringComparison.Ordinal)
            || !string.Equals(proof.PersistedConfigDigest, seed.PersistedConfigDigest, StringComparison.Ordinal)
            || proof.NextStep != seed.SourceNextStep)
            throw new InvalidDataException("legacy _0009 seed authority does not match the separately-proven current cold image");

        return new DeepRematchLegacyCurrentSeed(seed, proof, DeepRematchLegacySeedBinding.Create(seed, proof));
    }

    public DeepRematchLegacyRecoveredRun<TOutcome> Recover<TOutcome>(
        in CheckpointRoundTripProof proof,
        TOutcome outcome,
        bool requireTerminalOccurrenceCheck = true)
    {
        if (requireTerminalOccurrenceCheck && (!TerminalRun.terminalOccurrenceCheckAttempted || !TerminalRun.terminalOccurrenceCheckExact))
            throw new InvalidDataException("legacy _0009 recovery requires an independently verified terminal receipt");
        if (TerminalRun.runtimeStopRequested)
            throw new InvalidDataException("legacy _0009 runtime-stop terminals require their completion predicate owner");
        return new DeepRematchLegacyRecoveredRun<TOutcome>(this, ToCurrentSeedLoad(in proof), outcome);
    }

    public static DeepRematchLegacyRecoveredRun<TOutcome> Recover<TOutcome>(
        string runDirectory,
        in CheckpointRoundTripProof proof,
        TOutcome outcome,
        bool requireTerminalOccurrenceCheck = true)
        => Read(runDirectory).Recover(in proof, outcome, requireTerminalOccurrenceCheck);

    private void Validate()
    {
        DeepRematchLegacyTerminalRunDocument terminal = TerminalRun;
        DeepRematchLegacySeedLoad seed = SeedLoadReceipt.ToValue();
        string directory = RunDirectory;
        ReadOnlySpan<byte> effectiveCheckpoint = CheckpointDelta.ReadEffectiveSnapshot(directory).EffectiveImage;
        if (!effectiveCheckpoint.StartsWith("CORTEXO\n"u8))
            throw new InvalidDataException("legacy _0009 terminal authority requires an effective CORTEXO checkpoint");

        if (terminal.schemaVersion is not (2 or 3) || Path.GetFileName(directory) != terminal.childRunID
            || Path.GetFileName(Path.GetDirectoryName(directory)) != "children"
            || Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(directory))) != terminal.parentRunID
            || !MatchesRoleToken(terminal.childRunID, terminal.role)
            || terminal.role == CortexForkRailRoles.Unknown || terminal.exitCode != 0 || !terminal.terminalCheckpointExact
            || terminal.terminalOccurrenceCheckAttempted && !terminal.terminalOccurrenceCheckExact
            || string.IsNullOrWhiteSpace(terminal.parentRunID) || string.IsNullOrWhiteSpace(terminal.sourceRunID)
            || terminal.sourceNextStep < 0 || terminal.startStep < 0 || terminal.plannedNextStep < terminal.startStep
            || terminal.actualNextStep < terminal.startStep
            || terminal.seedLoadIntentDigest.Length != 64 || terminal.seedLoadReceiptDigest.Length != 64
            || terminal.terminalOccurrenceCheckAttempted && terminal.terminalOccurrenceCheckReceiptDigest.Length != 64
            || !terminal.terminalOccurrenceCheckAttempted && (terminal.terminalOccurrenceCheckExact
                || terminal.terminalOccurrenceCheckReceiptDigest.Length != 0
                || terminal.terminalVerifierWallMilliseconds != 0 || terminal.terminalVerifierRawTicks != 0))
            throw new InvalidDataException("legacy _0009 terminal run identity or success contract is incomplete");

        foreach (string digest in new[]
        {
            terminal.coldSeedDigest, terminal.persistedConfigDigest, terminal.seedLoadBindingDigest,
            terminal.seedLoadIntentDigest, terminal.seedLoadReceiptDigest, terminal.sourceSeedDigest,
            terminal.expectedCheckpointSHA256, terminal.expectedTapeSpanlogSHA256, terminal.expectedCurveSHA256,
            terminal.loadedCheckpointSHA256, terminal.loadedTapeSpanlogSHA256, terminal.loadedCurveSHA256,
            terminal.finalCheckpointSHA256, terminal.finalTapeSpanlogSHA256, terminal.finalCurveSHA256,
        })
            RequireDigest(digest, "legacy _0009 terminal run receipt digest");
        if (terminal.terminalOccurrenceCheckAttempted)
            RequireDigest(terminal.terminalOccurrenceCheckReceiptDigest, "legacy _0009 terminal verifier receipt digest");
        if (terminal.anytimeCurveDigest.Length > 0)
            RequireDigest(terminal.anytimeCurveDigest, "legacy _0009 anytime curve digest");

        DeepRematchLegacyExecutionWindow window = new DeepRematchLegacyExecutionWindow(terminal.startStep, terminal.plannedNextStep).Validate();
        DeepRematchLegacyRunTiming timing = terminal.ToTiming();
        if (terminal.actualNextStep > terminal.plannedNextStep
            || terminal.totalWallMilliseconds != timing.TotalWallMilliseconds
            || terminal.totalRawTicks != timing.TotalRawTicks
            || terminal.totalWallMilliseconds < 0 || terminal.totalRawTicks <= 0
            || terminal.seedIOWallMilliseconds < 0 || terminal.executionWallMilliseconds < 0
            || terminal.runtimeBindWallMilliseconds < 0 || terminal.terminalVerifierWallMilliseconds < 0
            || terminal.seedIORawTicks <= 0 || terminal.executionRawTicks <= 0
            || terminal.runtimeBindRawTicks < 0
            || terminal.terminalOccurrenceCheckAttempted && terminal.terminalVerifierRawTicks <= 0
            || !terminal.terminalOccurrenceCheckAttempted && terminal.terminalVerifierRawTicks != 0)
            throw new InvalidDataException("legacy _0009 terminal timing or execution window is incomplete");
        if (!terminal.runtimeStopRequested && terminal.actualNextStep != terminal.plannedNextStep)
            throw new InvalidDataException("legacy _0009 exact terminal did not reach its planned horizon");

        if (!seed.Bound || !seed.Exact || seed.SeedIOWallMilliseconds < 0 || seed.SeedIORawTicks <= 0
            || seed.BindingDigest != terminal.seedLoadBindingDigest || seed.ParentRunID != terminal.parentRunID
            || seed.ChildRunID != terminal.childRunID || seed.Role != terminal.role
            || seed.ColdSeedDigest != terminal.coldSeedDigest || seed.PersistedConfigDigest != terminal.persistedConfigDigest
            || seed.SourceSeedDigest != terminal.sourceSeedDigest || seed.SourceRunID != terminal.sourceRunID
            || seed.ExecutionWindow != window || seed.SourceNextStep != terminal.sourceNextStep
            || !seed.ExpectedDigests.Equals(new DeepRematchLegacyDigests(terminal.expectedCheckpointSHA256,
                terminal.expectedTapeSpanlogSHA256, terminal.expectedCurveSHA256))
            || !seed.LoadedDigests.Equals(new DeepRematchLegacyDigests(terminal.loadedCheckpointSHA256,
                terminal.loadedTapeSpanlogSHA256, terminal.loadedCurveSHA256))
            || seed.SeedIOWallMilliseconds != terminal.seedIOWallMilliseconds
            || seed.SeedIORawTicks != terminal.seedIORawTicks
            || SeedLoadReceipt.runtimeBindWallMilliseconds != terminal.runtimeBindWallMilliseconds)
            throw new InvalidDataException("legacy _0009 terminal disagrees with its seed-load receipt");

        if (terminal.schemaVersion == 3)
        {
            foreach (string digest in new[]
            {
                terminal.checkpointProofDigest, terminal.checkpointProofEffectiveImageSHA256,
                terminal.checkpointProofEffectivePhysicalSHA256, terminal.checkpointProofBasePhysicalSHA256,
                terminal.checkpointProofPhysicalChainSHA256, terminal.checkpointProofConfigDigest,
            })
                RequireDigest(digest, "historical checkpoint proof digest");
            CheckpointRoundTripProof proof = new(terminal.checkpointProofEffectiveImageSHA256,
                terminal.checkpointProofEffectivePhysicalSHA256, terminal.checkpointProofBasePhysicalSHA256,
                terminal.checkpointProofPhysicalChainSHA256, terminal.checkpointProofConfigDigest,
                terminal.checkpointProofNextStep, terminal.checkpointProofSaveLoadSaveExact);
            if (!proof.IsBound || proof.BindingDigest != terminal.checkpointProofDigest
                || proof.NextStep != terminal.sourceNextStep
                || proof.PersistedConfigDigest != terminal.persistedConfigDigest
                || seed.CheckpointProof != proof || seed.CheckpointProofReused != terminal.checkpointProofReused)
                throw new InvalidDataException("historical terminal checkpoint proof disagrees with its seed authority");
        }

        if (!SeedLoadIntent.ToValue().Equals(seed) || SeedLoadIntent.runtimeBindWallMilliseconds != 0)
            throw new InvalidDataException("legacy _0009 seed-load intent disagrees with its committed receipt");
        if (DigestFile(Path.Combine(directory, SeedLoadIntentFileName)) != terminal.seedLoadIntentDigest
            || DigestFile(Path.Combine(directory, SeedLoadReceiptFileName)) != terminal.seedLoadReceiptDigest)
            throw new InvalidDataException("legacy _0009 seed-load authority file is missing or changed");

        DeepRematchLegacyDigests actual = ReadRunDigests(directory);
        DeepRematchLegacyDigests final = new(terminal.finalCheckpointSHA256, terminal.finalTapeSpanlogSHA256, terminal.finalCurveSHA256);
        if (!actual.Equals(final))
            throw new InvalidDataException("legacy _0009 terminal receipt disagrees with landed child artifacts");
        // The terminal receipt owns the cursor for this historical dialect. The
        // checkpoint is custody-checked by its typed digest above; parsing it with
        // the current CORTEXT reader would reinterpret the retired CORTEXO bytes.

        string parentDirectory = Path.GetFullPath(Path.Combine(directory, "..", ".."));
        string sourceDirectory = terminal.sourceRunID == terminal.parentRunID
            ? parentDirectory
            : Path.Combine(parentDirectory, "children", terminal.sourceRunID);
        DeepRematchLegacyDigests expectedSeed = new(terminal.expectedCheckpointSHA256,
            terminal.expectedTapeSpanlogSHA256, terminal.expectedCurveSHA256);
        if (terminal.sourceRunID == terminal.parentRunID)
        {
            if (terminal.sourceSeedDigest != terminal.coldSeedDigest
                || ComputeSeedIdentity(expectedSeed, terminal.persistedConfigDigest) != terminal.coldSeedDigest)
                throw new InvalidDataException("legacy _0009 cold seed authority formula disagrees");
        }
        else
        {
            if (Path.GetFileName(sourceDirectory) != terminal.sourceRunID || !Directory.Exists(sourceDirectory))
                throw new InvalidDataException("legacy _0009 continuation source path or cursor disagrees");
            DeepRematchLegacyTerminalAuthority sourceAuthority = Read(sourceDirectory);
            if (sourceAuthority.TerminalRun.actualNextStep != terminal.sourceNextStep)
                throw new InvalidDataException("legacy _0009 continuation source receipt disagrees with its cursor authority");
            DeepRematchLegacyDigests source = ReadRunDigests(sourceDirectory);
            if (!source.Equals(expectedSeed) || ComputeSeedIdentity(source, terminal.persistedConfigDigest) != terminal.sourceSeedDigest)
                throw new InvalidDataException("legacy _0009 source files disagree with seed authority");
        }

        if (terminal.terminalOccurrenceCheckAttempted)
        {
            if (TerminalOccurrenceCheck is null)
                throw new InvalidDataException("legacy _0009 terminal verifier receipt is missing");
            if (TerminalOccurrenceCheck.seedLoadBindingDigest != terminal.seedLoadBindingDigest
                || terminal.schemaVersion == 3 && TerminalOccurrenceCheck.checkpointProofDigest != terminal.checkpointProofDigest
                || TerminalOccurrenceCheck.finalCheckpointSHA256 != terminal.finalCheckpointSHA256
                || TerminalOccurrenceCheck.finalTapeSpanlogSHA256 != terminal.finalTapeSpanlogSHA256
                || TerminalOccurrenceCheck.finalCurveSHA256 != terminal.finalCurveSHA256
                || TerminalOccurrenceCheck.startStep != terminal.startStep
                || TerminalOccurrenceCheck.plannedNextStep != terminal.plannedNextStep
                || TerminalOccurrenceCheck.actualNextStep != terminal.actualNextStep
                || TerminalOccurrenceCheck.executionWallMilliseconds != terminal.executionWallMilliseconds
                || TerminalOccurrenceCheck.executionRawTicks != terminal.executionRawTicks
                || TerminalOccurrenceCheck.seedIORawTicks != terminal.seedIORawTicks
                || TerminalOccurrenceCheck.runtimeBindWallMilliseconds != terminal.runtimeBindWallMilliseconds
                || TerminalOccurrenceCheck.runtimeBindRawTicks != terminal.runtimeBindRawTicks
                || TerminalOccurrenceCheck.wallMilliseconds != terminal.terminalVerifierWallMilliseconds)
                throw new InvalidDataException("legacy _0009 terminal verifier disagrees with landed artifacts");
            if (DigestFile(Path.Combine(directory, TerminalOccurrenceCheckFileName)) != terminal.terminalOccurrenceCheckReceiptDigest)
                throw new InvalidDataException("legacy _0009 terminal verifier receipt is missing or changed");
            TerminalOccurrenceCheck.Validate(terminal.childRunID, terminal.coldSeedDigest);
        }
        else if (File.Exists(Path.Combine(directory, TerminalOccurrenceCheckFileName)))
            throw new InvalidDataException("legacy _0009 terminal run has an unexpected verifier sidecar");

        if (terminal.receiptDigest != ComputeReceiptDigest(terminal))
            throw new InvalidDataException("legacy _0009 terminal receipt digest is corrupt");
    }

    private static DeepRematchLegacyDigests ReadRunDigests(string directory)
        => new(Checkpoint.LogicalStateSHA256(CheckpointDelta.ReadEffectiveSnapshot(directory).EffectiveImage),
            DigestFile(Path.Combine(directory, "tape.spanlog")), DigestFile(Path.Combine(directory, "curve.tsv")));

    private static string ComputeReceiptDigest(DeepRematchLegacyTerminalRunDocument terminal)
        => terminal.schemaVersion == 2 ? ComputeLegacyReceiptDigest(terminal) : ComputeHistoricalReceiptDigest(terminal);

    private static string ComputeLegacyReceiptDigest(DeepRematchLegacyTerminalRunDocument terminal)
        => ComputeSHA256(string.Join('|', terminal.schemaVersion, terminal.parentRunID, terminal.childRunID,
            terminal.role, terminal.coldSeedDigest, terminal.persistedConfigDigest, terminal.seedLoadBindingDigest,
            terminal.seedLoadIntentDigest, terminal.seedLoadReceiptDigest, terminal.sourceSeedDigest, terminal.sourceRunID,
            terminal.sourceNextStep, terminal.expectedCheckpointSHA256, terminal.expectedTapeSpanlogSHA256,
            terminal.expectedCurveSHA256, terminal.loadedCheckpointSHA256, terminal.loadedTapeSpanlogSHA256,
            terminal.loadedCurveSHA256, terminal.startStep, terminal.plannedNextStep, terminal.actualNextStep,
            terminal.exitCode, terminal.terminalCheckpointExact, terminal.terminalOccurrenceCheckAttempted,
            terminal.terminalOccurrenceCheckExact, terminal.finalCheckpointSHA256, terminal.finalTapeSpanlogSHA256,
            terminal.finalCurveSHA256, terminal.seedIOWallMilliseconds, terminal.seedIORawTicks,
            terminal.runtimeBindWallMilliseconds, terminal.runtimeBindRawTicks, terminal.executionWallMilliseconds,
            terminal.executionRawTicks, terminal.terminalVerifierWallMilliseconds, terminal.terminalVerifierRawTicks,
            terminal.totalWallMilliseconds, terminal.totalRawTicks, terminal.terminalOccurrenceCheckReceiptDigest,
            terminal.anytimeCurveDigest, terminal.runtimeStopRequested));

    private static string ComputeHistoricalReceiptDigest(DeepRematchLegacyTerminalRunDocument terminal)
        => ComputeSHA256(string.Join('|', terminal.schemaVersion, terminal.parentRunID, terminal.childRunID,
            terminal.role, terminal.coldSeedDigest, terminal.persistedConfigDigest, terminal.seedLoadBindingDigest,
            terminal.seedLoadIntentDigest, terminal.seedLoadReceiptDigest, terminal.sourceSeedDigest, terminal.sourceRunID,
            terminal.sourceNextStep, terminal.expectedCheckpointSHA256, terminal.expectedTapeSpanlogSHA256,
            terminal.expectedCurveSHA256, terminal.loadedCheckpointSHA256, terminal.loadedTapeSpanlogSHA256,
            terminal.loadedCurveSHA256, terminal.startStep, terminal.plannedNextStep, terminal.actualNextStep,
            terminal.exitCode, terminal.terminalCheckpointExact, terminal.terminalOccurrenceCheckAttempted,
            terminal.terminalOccurrenceCheckExact, terminal.finalCheckpointSHA256, terminal.finalTapeSpanlogSHA256,
            terminal.finalCurveSHA256, terminal.seedIOWallMilliseconds, terminal.seedIORawTicks,
            terminal.runtimeBindWallMilliseconds, terminal.runtimeBindRawTicks, terminal.executionWallMilliseconds,
            terminal.executionRawTicks, terminal.terminalVerifierWallMilliseconds, terminal.terminalVerifierRawTicks,
            terminal.totalWallMilliseconds, terminal.totalRawTicks, terminal.terminalOccurrenceCheckReceiptDigest,
            terminal.anytimeCurveDigest, terminal.runtimeStopRequested, terminal.checkpointProofDigest,
            terminal.checkpointProofEffectiveImageSHA256, terminal.checkpointProofEffectivePhysicalSHA256,
            terminal.checkpointProofBasePhysicalSHA256, terminal.checkpointProofPhysicalChainSHA256,
            terminal.checkpointProofConfigDigest, terminal.checkpointProofNextStep,
            terminal.checkpointProofSaveLoadSaveExact, terminal.checkpointProofReused));

    internal static string ComputeFixtureReceiptDigest(DeepRematchLegacyTerminalRunDocument terminal)
        => ComputeReceiptDigest(terminal);

    private static TDocument ReadDocument<TDocument>(string path, string label) where TDocument : class
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing {label}: {path}");
        byte[] bytes = File.ReadAllBytes(path);
        TDocument document;
        try { document = RonSerializer.Deserialize<TDocument>(bytes); }
        catch (Exception error) { throw new InvalidDataException($"{label} is not readable RON", error); }
        byte[] canonical = RonSerializer.SerializeToUtf8(in document);
        if (!bytes.AsSpan().SequenceEqual(canonical) && !IsHistoricalLegacyDocument<TDocument>())
            throw new InvalidDataException($"{label} is not canonical SaveLoadSave data");
        return document;
    }

    private static bool IsHistoricalLegacyDocument<TDocument>() where TDocument : class
        => typeof(TDocument) == typeof(DeepRematchLegacyTerminalRunDocument)
            || typeof(TDocument) == typeof(DeepRematchLegacySeedLoadDocument)
            || typeof(TDocument) == typeof(DeepRematchLegacyTerminalOccurrenceCheckDocument);

    private static bool MatchesRoleToken(string childID, CortexForkRailRoles role)
    {
        if (role == CortexForkRailRoles.Unknown) return false;
        string token = role switch
        {
            CortexForkRailRoles.ForcedNull => "forced-null",
            CortexForkRailRoles.ReflexFrozen => "reflex-frozen",
            _ => role.ToString().ToLowerInvariant(),
        };
        string prefix = token + "_";
        return childID.StartsWith(prefix, StringComparison.Ordinal)
            && childID.Length > prefix.Length
            && childID[prefix.Length..].All(char.IsAsciiDigit);
    }

    private static string ComputeSeedIdentity(in DeepRematchLegacyDigests digests, string configDigest)
        => ComputeSHA256(string.Join('|', digests.CheckpointSHA256, digests.TapeSpanlogSHA256, digests.CurveSHA256, configDigest));

    private static string DigestFile(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing legacy terminal artifact: {path}");
        return ComputeSHA256(File.ReadAllBytes(path));
    }

    private static string ComputeSHA256(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string ComputeSHA256(string value)
        => ComputeSHA256(Encoding.UTF8.GetBytes(value));

    private static void RequireDigest(string value, string label)
    {
        if (value.Length != 64 || value.Any(static c => c is < '0' or > '9' and < 'a' or > 'f'))
            throw new InvalidDataException($"{label} is not canonical lowercase SHA-256");
    }
}

public readonly record struct DeepRematchLegacyDigests(
    string CheckpointSHA256,
    string TapeSpanlogSHA256,
    string CurveSHA256);

public readonly record struct DeepRematchLegacyExecutionWindow(int StartStep, int EndStep)
{
    public int Length => checked(EndStep - StartStep);
    public DeepRematchLegacyExecutionWindow Validate()
    {
        if (StartStep < 0 || EndStep < StartStep)
            throw new ArgumentOutOfRangeException(nameof(EndStep), "legacy execution window must be a nonnegative increasing interval");
        return this;
    }
    public string Digest => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{StartStep}:{EndStep}")));
}

public readonly record struct DeepRematchLegacySeedLoad(
    DeepRematchLegacyDigests ExpectedDigests,
    DeepRematchLegacyDigests LoadedDigests,
    long SeedIOWallMilliseconds,
    string ParentRunID,
    string ChildRunID,
    CortexForkRailRoles Role,
    string ColdSeedDigest,
    string PersistedConfigDigest,
    DeepRematchLegacyExecutionWindow ExecutionWindow,
    string SourceSeedDigest,
    string SourceRunID,
    int SourceNextStep,
    long SeedIORawTicks,
    CheckpointRoundTripProof CheckpointProof = default,
    bool CheckpointProofReused = false)
{
    public bool Exact => ExpectedDigests.Equals(LoadedDigests);
    public string BindingDigest => CheckpointProof.IsBound ? ComputeBoundBindingDigest() : ComputeLegacyBindingDigest();

    private string ComputeBoundBindingDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            ParentRunID, ChildRunID, Role, ColdSeedDigest, PersistedConfigDigest, SourceSeedDigest, SourceRunID,
            SourceNextStep, ExecutionWindow.Digest, SeedIORawTicks,
            ExpectedDigests.CheckpointSHA256, ExpectedDigests.TapeSpanlogSHA256, ExpectedDigests.CurveSHA256,
            LoadedDigests.CheckpointSHA256, LoadedDigests.TapeSpanlogSHA256, LoadedDigests.CurveSHA256,
            CheckpointProof.BindingDigest, CheckpointProofReused))));

    private string ComputeLegacyBindingDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            ParentRunID, ChildRunID, Role, ColdSeedDigest, PersistedConfigDigest, SourceSeedDigest, SourceRunID,
            SourceNextStep, ExecutionWindow.Digest, SeedIORawTicks,
            ExpectedDigests.CheckpointSHA256, ExpectedDigests.TapeSpanlogSHA256, ExpectedDigests.CurveSHA256,
            LoadedDigests.CheckpointSHA256, LoadedDigests.TapeSpanlogSHA256, LoadedDigests.CurveSHA256))));
    public bool Bound => !string.IsNullOrWhiteSpace(ParentRunID) && !string.IsNullOrWhiteSpace(ChildRunID)
        && Role != CortexForkRailRoles.Unknown && IsDigest(ColdSeedDigest) && IsDigest(PersistedConfigDigest)
        && IsDigest(SourceSeedDigest) && !string.IsNullOrWhiteSpace(SourceRunID) && SourceNextStep >= 0
        && ExecutionWindow.Validate().Length >= 0;
    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

[RonObject]
public partial class DeepRematchLegacySeedLoadDocument
{
    public int schemaVersion = 1;
    public string parentRunID = "";
    public string childRunID = "";
    public CortexForkRailRoles role;
    public string coldSeedDigest = "";
    public string persistedConfigDigest = "";
    public string sourceSeedDigest = "";
    public string sourceRunID = "";
    public int sourceNextStep = -1;
    public string expectedCheckpointSHA256 = "";
    public string expectedTapeSpanlogSHA256 = "";
    public string expectedCurveSHA256 = "";
    public string loadedCheckpointSHA256 = "";
    public string loadedTapeSpanlogSHA256 = "";
    public string loadedCurveSHA256 = "";
    public int startStep;
    public int endStep;
    public long seedIOWallMilliseconds;
    public long seedIORawTicks;
    public long runtimeBindWallMilliseconds;
    public string bindingDigest = "";
    public string checkpointProofEffectiveImageSHA256 = "";
    public string checkpointProofEffectivePhysicalSHA256 = "";
    public string checkpointProofBasePhysicalSHA256 = "";
    public string checkpointProofPhysicalChainSHA256 = "";
    public string checkpointProofConfigDigest = "";
    public int checkpointProofNextStep;
    public bool checkpointProofSaveLoadSaveExact;
    public bool checkpointProofReused;

    public DeepRematchLegacySeedLoad ToValue()
        => new(new(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256),
            new(loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256), seedIOWallMilliseconds,
            parentRunID, childRunID, role, coldSeedDigest, persistedConfigDigest,
            new(startStep, endStep), sourceSeedDigest, sourceRunID, sourceNextStep, seedIORawTicks,
            new(checkpointProofEffectiveImageSHA256, checkpointProofEffectivePhysicalSHA256,
                checkpointProofBasePhysicalSHA256, checkpointProofPhysicalChainSHA256,
                checkpointProofConfigDigest, checkpointProofNextStep, checkpointProofSaveLoadSaveExact),
            checkpointProofReused);
}

[RonObject]
public partial class DeepRematchLegacyTerminalOccurrenceCheckDocument
{
    public int schemaVersion = 1;
    public string childRunID = "";
    public string coldSeedDigest = "";
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public string seedLoadBindingDigest = "";
    public int startStep;
    public int plannedNextStep;
    public int actualNextStep;
    public long executionWallMilliseconds;
    public long executionRawTicks;
    public long seedIORawTicks;
    public long runtimeBindWallMilliseconds;
    public long runtimeBindRawTicks;
    public long wallMilliseconds;
    public bool verified;
    public string checkpointProofDigest = "";

    internal void Validate(string expectedChildRunID, string expectedColdSeedDigest)
    {
        if (schemaVersion is not (1 or 2) || childRunID != expectedChildRunID || coldSeedDigest != expectedColdSeedDigest
            || !verified || wallMilliseconds < 0 || executionWallMilliseconds < 0 || executionRawTicks <= 0
            || seedIORawTicks <= 0 || runtimeBindWallMilliseconds < 0 || runtimeBindRawTicks < 0
            || actualNextStep < startStep || plannedNextStep < startStep || actualNextStep > plannedNextStep
            || !IsDigest(seedLoadBindingDigest) || !IsDigest(finalCheckpointSHA256)
            || !IsDigest(finalTapeSpanlogSHA256) || !IsDigest(finalCurveSHA256))
            throw new InvalidDataException("legacy _0009 terminal verifier receipt is not exact");
    }

    private static bool IsDigest(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

[RonObject]
public partial class DeepRematchLegacyTerminalRunDocument
{
    public int schemaVersion = 2;
    public string parentRunID = "";
    public string childRunID = "";
    public CortexForkRailRoles role;
    public string coldSeedDigest = "";
    public string persistedConfigDigest = "";
    public string seedLoadBindingDigest = "";
    public string seedLoadIntentDigest = "";
    public string seedLoadReceiptDigest = "";
    public string sourceSeedDigest = "";
    public string sourceRunID = "";
    public int sourceNextStep = -1;
    public string expectedCheckpointSHA256 = "";
    public string expectedTapeSpanlogSHA256 = "";
    public string expectedCurveSHA256 = "";
    public string loadedCheckpointSHA256 = "";
    public string loadedTapeSpanlogSHA256 = "";
    public string loadedCurveSHA256 = "";
    public int startStep;
    public int plannedNextStep;
    public int actualNextStep;
    public int exitCode;
    public bool runtimeStopRequested;
    public bool terminalCheckpointExact;
    public bool terminalOccurrenceCheckAttempted;
    public bool terminalOccurrenceCheckExact;
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public long seedIOWallMilliseconds;
    public long seedIORawTicks;
    public long runtimeBindWallMilliseconds;
    public long runtimeBindRawTicks;
    public long executionWallMilliseconds;
    public long executionRawTicks;
    public long terminalVerifierWallMilliseconds;
    public long terminalVerifierRawTicks;
    public long totalWallMilliseconds;
    public long totalRawTicks;
    public string terminalOccurrenceCheckReceiptDigest = "";
    public string anytimeCurveDigest = "";
    public string checkpointProofDigest = "";
    public string checkpointProofEffectiveImageSHA256 = "";
    public string checkpointProofEffectivePhysicalSHA256 = "";
    public string checkpointProofBasePhysicalSHA256 = "";
    public string checkpointProofPhysicalChainSHA256 = "";
    public string checkpointProofConfigDigest = "";
    public int checkpointProofNextStep;
    public bool checkpointProofSaveLoadSaveExact;
    public bool checkpointProofReused;
    public string receiptDigest = "";

    internal DeepRematchLegacyRunTiming ToTiming()
        => new(seedIOWallMilliseconds, executionWallMilliseconds, terminalVerifierWallMilliseconds, runtimeBindWallMilliseconds,
            seedIORawTicks, executionRawTicks, terminalVerifierRawTicks, runtimeBindRawTicks);
}

public readonly record struct DeepRematchLegacyRunTiming(
    long SeedIOWallMilliseconds,
    long ExecutionWallMilliseconds,
    long TerminalVerifierWallMilliseconds,
    long RuntimeBindWallMilliseconds,
    long SeedIORawTicks,
    long ExecutionRawTicks,
    long TerminalVerifierRawTicks,
    long RuntimeBindRawTicks)
{
    public long TotalWallMilliseconds => checked(SeedIOWallMilliseconds + ExecutionWallMilliseconds
        + TerminalVerifierWallMilliseconds + RuntimeBindWallMilliseconds);
    public long TotalRawTicks => checked(SeedIORawTicks + ExecutionRawTicks + TerminalVerifierRawTicks + RuntimeBindRawTicks);
}

public readonly record struct DeepRematchLegacyCurrentSeed(
    DeepRematchLegacySeedLoad LegacySeedLoad,
    CheckpointRoundTripProof CurrentCheckpointProof,
    DeepRematchLegacySeedBinding Binding)
{
    public string ColdSeedDigest => LegacySeedLoad.ColdSeedDigest;
    public string PersistedConfigDigest => LegacySeedLoad.PersistedConfigDigest;
    public int NextStep => LegacySeedLoad.SourceNextStep;
}

/// The old terminal receipt and the current checkpoint proof have different
/// binding formulas. Keep both digests named so a recovery caller cannot use
/// the current derived binding as a replacement for the committed legacy one.
public readonly record struct DeepRematchLegacySeedBinding(
    string LegacyBindingDigest,
    string CurrentBindingDigest,
    string CheckpointProofBindingDigest)
{
    internal static DeepRematchLegacySeedBinding Create(
        in DeepRematchLegacySeedLoad seed,
        in CheckpointRoundTripProof proof)
    {
        string current = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            seed.ParentRunID, seed.ChildRunID, seed.Role, seed.ColdSeedDigest, seed.PersistedConfigDigest,
            seed.SourceSeedDigest, seed.SourceRunID, seed.SourceNextStep, seed.ExecutionWindow.Digest,
            seed.SeedIORawTicks, seed.ExpectedDigests.CheckpointSHA256, seed.ExpectedDigests.TapeSpanlogSHA256,
            seed.ExpectedDigests.CurveSHA256, seed.LoadedDigests.CheckpointSHA256, seed.LoadedDigests.TapeSpanlogSHA256,
            seed.LoadedDigests.CurveSHA256, proof.BindingDigest, true))));
        return new(seed.BindingDigest, current, proof.BindingDigest);
    }

    public void Validate(in DeepRematchLegacySeedLoad seed, in CheckpointRoundTripProof proof)
    {
        DeepRematchLegacySeedBinding expected = Create(seed, proof);
        if (LegacyBindingDigest != expected.LegacyBindingDigest
            || CurrentBindingDigest != expected.CurrentBindingDigest
            || CheckpointProofBindingDigest != expected.CheckpointProofBindingDigest)
            throw new InvalidDataException("legacy/current seed binding relation changed");
    }
}

public sealed class DeepRematchLegacyRecoveredRun<TOutcome>
{
    internal DeepRematchLegacyRecoveredRun(
        DeepRematchLegacyTerminalAuthority authority,
        DeepRematchLegacyCurrentSeed currentSeed,
        TOutcome outcome)
    {
        Authority = authority;
        CurrentSeed = currentSeed;
        Outcome = outcome;
    }

    public DeepRematchLegacyTerminalAuthority Authority { get; }
    public DeepRematchLegacyCurrentSeed CurrentSeed { get; }
    public TOutcome Outcome { get; }
    public DeepRematchLegacyTerminalRunDocument TerminalRun => Authority.TerminalRun;
    public DeepRematchLegacySeedLoad SeedLoad => Authority.SeedLoad;
}
