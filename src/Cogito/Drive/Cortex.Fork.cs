namespace Cogito;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

public enum CortexForkRailRoles
{
    Unknown,
    Calibration,
    Evaluation,
    Baseline,
    Candidate,
    ForcedNull,
    ReflexFrozen,
}

/// The runtime step interval is deliberately separate from CortexRunConfig.
/// A child may execute a shorter/longer leg while preserving the parent's
/// persisted configuration bytes and digest.
public readonly record struct CortexExecutionWindow(int StartStep, int EndStep)
{
    public int Length => checked(EndStep - StartStep);

    public CortexExecutionWindow Validate()
    {
        if (StartStep < 0 || EndStep < StartStep)
            throw new ArgumentOutOfRangeException(nameof(EndStep), "execution window must be a nonnegative increasing interval");
        return this;
    }

    public string Digest
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{StartStep}:{EndStep}")));
}

public readonly struct CortexForkDigests
{
    public CortexForkDigests(string checkpointSHA256, string tapeSpanlogSHA256, string curveSHA256, string excursionsSHA256 = "")
    {
        CheckpointSHA256 = checkpointSHA256;
        TapeSpanlogSHA256 = tapeSpanlogSHA256;
        CurveSHA256 = curveSHA256;
        ExcursionsSHA256 = excursionsSHA256;
    }

    public string CheckpointSHA256 { get; }
    public string TapeSpanlogSHA256 { get; }
    public string CurveSHA256 { get; }
    public string ExcursionsSHA256 { get; }
}

public enum CortexForkSeedRelations
{
    InitialCrossArm,
    PreparedFromSharedAncestor,
    PerArmContinuation,
}

/// The closed set of arm preparation roles.  Preparation is a typed phase of a
/// fork arm, not an unstructured callback; the rail role remains the durable
/// custody owner for the resulting child.
public enum CortexForkPreparationRoles
{
    Unknown,
    Baseline,
    Candidate,
    ForcedNull,
    ReflexFrozen,
}

public readonly record struct CortexForkAdoptionHop(
    string OriginRunID,
    string ChildRunID,
    int SourceNextStep,
    string PersistedConfigDigest,
    string BasePhysicalSHA256,
    string SourceSeedDigest,
    string ParentBindingDigest);

public readonly record struct CortexForkSeedLoadReceipt(
    CortexForkDigests ExpectedDigests,
    CortexForkDigests LoadedDigests,
    long SeedIOWallMilliseconds,
    string ParentRunID = "",
    string ChildRunID = "",
    CortexForkRailRoles Role = CortexForkRailRoles.Unknown,
    string ColdSeedDigest = "",
    string PersistedConfigDigest = "",
    CortexExecutionWindow ExecutionWindow = default,
    string SourceSeedDigest = "",
    string SourceRunID = "",
    int SourceNextStep = -1,
    long SeedIORawTicks = 0,
    CheckpointRoundTripProof CheckpointProof = default,
    bool CheckpointProofReused = false,
    long ExcursionCursor = 0,
    CortexForkAdoptionHop[]? AdoptionAncestry = null,
    string AncestorSeedDigest = "",
    string PreparedSeedDigest = "",
    CortexForkPreparationRoles PreparationRole = CortexForkPreparationRoles.Unknown)
{
    public bool Exact => DigestsEqual(ExpectedDigests, LoadedDigests);
    public string ExpectedCheckpointSHA256 => ExpectedDigests.CheckpointSHA256;
    public string ExpectedTapeSpanlogSHA256 => ExpectedDigests.TapeSpanlogSHA256;
    public string ExpectedCurveSHA256 => ExpectedDigests.CurveSHA256;
    public string ExpectedExcursionsSHA256 => ExpectedDigests.ExcursionsSHA256;
    public string LoadedCheckpointSHA256 => LoadedDigests.CheckpointSHA256;
    public string LoadedTapeSpanlogSHA256 => LoadedDigests.TapeSpanlogSHA256;
    public string LoadedCurveSHA256 => LoadedDigests.CurveSHA256;
    public string LoadedExcursionsSHA256 => LoadedDigests.ExcursionsSHA256;
    public string BindingDigest => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
        ParentRunID, ChildRunID, Role, ColdSeedDigest, PersistedConfigDigest,
        SourceSeedDigest, SourceRunID, SourceNextStep, ExecutionWindow.Digest,
        AncestorSeedDigest, PreparedSeedDigest, PreparationRole,
        SeedIORawTicks,
        string.Join("|", (AdoptionAncestry ?? []).Select(static hop => string.Join("|",
            hop.OriginRunID, hop.ChildRunID, hop.SourceNextStep, hop.PersistedConfigDigest,
            hop.BasePhysicalSHA256, hop.SourceSeedDigest, hop.ParentBindingDigest))),
        ExpectedDigests.CheckpointSHA256, ExpectedDigests.TapeSpanlogSHA256, ExpectedDigests.CurveSHA256, ExpectedDigests.ExcursionsSHA256,
        LoadedDigests.CheckpointSHA256, LoadedDigests.TapeSpanlogSHA256, LoadedDigests.CurveSHA256, LoadedDigests.ExcursionsSHA256,
        CheckpointProof.BindingDigest, CheckpointProofReused, ExcursionCursor))));
    public bool Bound => !string.IsNullOrWhiteSpace(ParentRunID)
        && !string.IsNullOrWhiteSpace(ChildRunID)
        && Role != CortexForkRailRoles.Unknown
        && IsDigest(ColdSeedDigest)
        && IsDigest(PersistedConfigDigest)
        && IsDigest(SourceSeedDigest)
        && (PreparationRole == CortexForkPreparationRoles.Unknown
            || IsDigest(AncestorSeedDigest) && IsDigest(PreparedSeedDigest))
        && !string.IsNullOrWhiteSpace(SourceRunID)
        && SourceNextStep >= 0
        && ExcursionCursor >= 0
        && HasValidAdoptionAncestry(AdoptionAncestry)
        && CheckpointProof.IsBound
        && CheckpointProof.Matches(new CheckpointRoundTripProof(
            ExpectedDigests.CheckpointSHA256, CheckpointProof.EffectivePhysicalSHA256,
            CheckpointProof.BasePhysicalSHA256, CheckpointProof.PhysicalChainSHA256,
            PersistedConfigDigest, SourceNextStep, CheckpointProof.SaveLoadSaveExact))
        && ExecutionWindow.Validate().Length >= 0;

    private static bool DigestsEqual(in CortexForkDigests left, in CortexForkDigests right)
        => string.Equals(left.CheckpointSHA256, right.CheckpointSHA256, StringComparison.Ordinal)
           && string.Equals(left.TapeSpanlogSHA256, right.TapeSpanlogSHA256, StringComparison.Ordinal)
           && string.Equals(left.CurveSHA256, right.CurveSHA256, StringComparison.Ordinal)
           && string.Equals(left.ExcursionsSHA256, right.ExcursionsSHA256, StringComparison.Ordinal);

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool HasValidAdoptionAncestry(CortexForkAdoptionHop[]? ancestry)
    {
        if (ancestry is null || ancestry.Length > 64) return false;
        HashSet<string> children = new(StringComparer.Ordinal);
        HashSet<string> origins = new(StringComparer.Ordinal);
        HashSet<string> parentBindings = new(StringComparer.Ordinal);
        string previousChild = "";
        int previousHorizon = -1;
        foreach (CortexForkAdoptionHop hop in ancestry)
        {
            if (string.IsNullOrWhiteSpace(hop.OriginRunID) || string.IsNullOrWhiteSpace(hop.ChildRunID)
                || hop.OriginRunID == hop.ChildRunID
                || hop.OriginRunID.Contains(Path.DirectorySeparatorChar) || hop.OriginRunID.Contains(Path.AltDirectorySeparatorChar)
                || hop.ChildRunID.Contains(Path.DirectorySeparatorChar) || hop.ChildRunID.Contains(Path.AltDirectorySeparatorChar)
                || hop.SourceNextStep < 0 || !IsDigest(hop.PersistedConfigDigest)
                || !IsDigest(hop.BasePhysicalSHA256) || !IsDigest(hop.SourceSeedDigest)
                || !IsDigest(hop.ParentBindingDigest) || !children.Add(hop.ChildRunID)
                || !origins.Add(hop.OriginRunID) || !parentBindings.Add(hop.ParentBindingDigest)) return false;
            if (previousChild.Length > 0 && !string.Equals(hop.OriginRunID, previousChild, StringComparison.Ordinal)) return false;
            if (previousHorizon >= 0 && hop.SourceNextStep < previousHorizon) return false;
            previousChild = hop.ChildRunID;
            previousHorizon = hop.SourceNextStep;
        }
        return true;
    }
}

[RonObject]
public partial class CortexForkAdoptionHopDocument
{
    public string originRunID = "";
    public string childRunID = "";
    public int sourceNextStep;
    public string persistedConfigDigest = "";
    public string basePhysicalSHA256 = "";
    public string sourceSeedDigest = "";
    public string parentBindingDigest = "";
}

[RonObject]
public partial class CortexForkSeedLoadRailReceipt
{
    public const int CurrentSchemaVersion = 3;
    public int schemaVersion = CurrentSchemaVersion;
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
    public string expectedExcursionsSHA256 = "";
    public string loadedCheckpointSHA256 = "";
    public string loadedTapeSpanlogSHA256 = "";
    public string loadedCurveSHA256 = "";
    public string loadedExcursionsSHA256 = "";
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
    public long excursionCursor;
    public string ancestorSeedDigest = "";
    public string preparedSeedDigest = "";
    public CortexForkPreparationRoles preparationRole;
    public List<CortexForkAdoptionHopDocument> adoptionAncestry = new();

    public static CortexForkSeedLoadRailReceipt FromReceipt(in CortexForkSeedLoadReceipt receipt, long runtimeBindWallMillisecondsValue = 0)
        => new()
        {
            parentRunID = receipt.ParentRunID,
            childRunID = receipt.ChildRunID,
            role = receipt.Role,
            coldSeedDigest = receipt.ColdSeedDigest,
            persistedConfigDigest = receipt.PersistedConfigDigest,
            sourceSeedDigest = receipt.SourceSeedDigest,
            sourceRunID = receipt.SourceRunID,
            sourceNextStep = receipt.SourceNextStep,
            expectedCheckpointSHA256 = receipt.ExpectedDigests.CheckpointSHA256,
            expectedTapeSpanlogSHA256 = receipt.ExpectedDigests.TapeSpanlogSHA256,
            expectedCurveSHA256 = receipt.ExpectedDigests.CurveSHA256,
            expectedExcursionsSHA256 = receipt.ExpectedDigests.ExcursionsSHA256,
            loadedCheckpointSHA256 = receipt.LoadedDigests.CheckpointSHA256,
            loadedTapeSpanlogSHA256 = receipt.LoadedDigests.TapeSpanlogSHA256,
            loadedCurveSHA256 = receipt.LoadedDigests.CurveSHA256,
            loadedExcursionsSHA256 = receipt.LoadedDigests.ExcursionsSHA256,
            startStep = receipt.ExecutionWindow.StartStep,
            endStep = receipt.ExecutionWindow.EndStep,
            seedIOWallMilliseconds = receipt.SeedIOWallMilliseconds,
            seedIORawTicks = receipt.SeedIORawTicks,
            runtimeBindWallMilliseconds = runtimeBindWallMillisecondsValue,
            bindingDigest = receipt.BindingDigest,
            checkpointProofEffectiveImageSHA256 = receipt.CheckpointProof.EffectiveImageSHA256,
            checkpointProofEffectivePhysicalSHA256 = receipt.CheckpointProof.EffectivePhysicalSHA256,
            checkpointProofBasePhysicalSHA256 = receipt.CheckpointProof.BasePhysicalSHA256,
            checkpointProofPhysicalChainSHA256 = receipt.CheckpointProof.PhysicalChainSHA256,
            checkpointProofConfigDigest = receipt.CheckpointProof.PersistedConfigDigest,
            checkpointProofNextStep = receipt.CheckpointProof.NextStep,
            checkpointProofSaveLoadSaveExact = receipt.CheckpointProof.SaveLoadSaveExact,
            checkpointProofReused = receipt.CheckpointProofReused,
            excursionCursor = receipt.ExcursionCursor,
            ancestorSeedDigest = receipt.AncestorSeedDigest,
            preparedSeedDigest = receipt.PreparedSeedDigest,
            preparationRole = receipt.PreparationRole,
            adoptionAncestry = (receipt.AdoptionAncestry ?? []).Select(static hop => new CortexForkAdoptionHopDocument
            {
                originRunID = hop.OriginRunID,
                childRunID = hop.ChildRunID,
                sourceNextStep = hop.SourceNextStep,
                persistedConfigDigest = hop.PersistedConfigDigest,
                basePhysicalSHA256 = hop.BasePhysicalSHA256,
                sourceSeedDigest = hop.SourceSeedDigest,
                parentBindingDigest = hop.ParentBindingDigest,
            }).ToList(),
        };

    public byte[] Encode()
    {
        CortexForkSeedLoadRailReceipt document = this;
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        CortexForkSeedLoadRailReceipt restored = RonSerializer.Deserialize<CortexForkSeedLoadRailReceipt>(first);
        byte[] second = RonSerializer.SerializeToUtf8(in restored);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("fork seed-load receipt SaveLoadSave drifted");
        return first;
    }

    internal CortexForkSeedLoadReceipt ToReceipt()
        => new(
            new CortexForkDigests(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256),
            new CortexForkDigests(loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256, loadedExcursionsSHA256),
            seedIOWallMilliseconds,
            parentRunID,
            childRunID,
            role,
            coldSeedDigest,
            persistedConfigDigest,
            new CortexExecutionWindow(startStep, endStep),
            sourceSeedDigest,
            sourceRunID,
            sourceNextStep,
            seedIORawTicks,
            new CheckpointRoundTripProof(checkpointProofEffectiveImageSHA256, checkpointProofEffectivePhysicalSHA256,
                checkpointProofBasePhysicalSHA256, checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest,
                checkpointProofNextStep, checkpointProofSaveLoadSaveExact),
            checkpointProofReused, excursionCursor,
            adoptionAncestry.Select(static hop => new CortexForkAdoptionHop(
                hop.originRunID, hop.childRunID, hop.sourceNextStep, hop.persistedConfigDigest,
                hop.basePhysicalSHA256, hop.sourceSeedDigest, hop.parentBindingDigest)).ToArray(),
            ancestorSeedDigest, preparedSeedDigest, preparationRole);
}

// Schema-2 seed-load rails are read through this exact legacy shape before
// migrating in memory. Keeping the old DTO preserves its SaveLoadSave bytes;
// the staged-preparation fields are deliberately absent from this dialect.
[RonObject]
internal partial class CortexForkSeedLoadRailReceiptV2
{
    public int schemaVersion = 2;
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
    public string expectedExcursionsSHA256 = "";
    public string loadedCheckpointSHA256 = "";
    public string loadedTapeSpanlogSHA256 = "";
    public string loadedCurveSHA256 = "";
    public string loadedExcursionsSHA256 = "";
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
    public long excursionCursor;
    public List<CortexForkAdoptionHopDocument> adoptionAncestry = new();

    internal CortexForkSeedLoadRailReceipt ToCurrent()
        => CortexForkSeedLoadRailReceipt.FromReceipt(ToReceipt(), runtimeBindWallMilliseconds);

    private CortexForkSeedLoadReceipt ToReceipt()
        => new(
            new CortexForkDigests(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256),
            new CortexForkDigests(loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256, loadedExcursionsSHA256),
            seedIOWallMilliseconds, parentRunID, childRunID, role, coldSeedDigest, persistedConfigDigest,
            new CortexExecutionWindow(startStep, endStep), sourceSeedDigest, sourceRunID, sourceNextStep, seedIORawTicks,
            new CheckpointRoundTripProof(checkpointProofEffectiveImageSHA256, checkpointProofEffectivePhysicalSHA256,
                checkpointProofBasePhysicalSHA256, checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest,
                checkpointProofNextStep, checkpointProofSaveLoadSaveExact), checkpointProofReused, excursionCursor,
            adoptionAncestry.Select(static hop => new CortexForkAdoptionHop(
                hop.originRunID, hop.childRunID, hop.sourceNextStep, hop.persistedConfigDigest,
                hop.basePhysicalSHA256, hop.sourceSeedDigest, hop.parentBindingDigest)).ToArray(),
            coldSeedDigest, coldSeedDigest, LegacyPreparationRole(role));

    private static CortexForkPreparationRoles LegacyPreparationRole(CortexForkRailRoles rail)
        => rail switch
        {
            CortexForkRailRoles.Baseline => CortexForkPreparationRoles.Baseline,
            CortexForkRailRoles.Candidate => CortexForkPreparationRoles.Candidate,
            CortexForkRailRoles.ForcedNull => CortexForkPreparationRoles.ForcedNull,
            CortexForkRailRoles.ReflexFrozen => CortexForkPreparationRoles.ReflexFrozen,
            _ => CortexForkPreparationRoles.Unknown,
        };

    internal byte[] Encode()
    {
        CortexForkSeedLoadRailReceiptV2 document = this;
        return RonSerializer.SerializeToUtf8(in document);
    }

    internal static CortexForkSeedLoadRailReceiptV2 FromCurrent(in CortexForkSeedLoadReceipt current)
        => new()
        {
            parentRunID = current.ParentRunID,
            childRunID = current.ChildRunID,
            role = current.Role,
            coldSeedDigest = current.ColdSeedDigest,
            persistedConfigDigest = current.PersistedConfigDigest,
            sourceSeedDigest = current.SourceSeedDigest,
            sourceRunID = current.SourceRunID,
            sourceNextStep = current.SourceNextStep,
            expectedCheckpointSHA256 = current.ExpectedCheckpointSHA256,
            expectedTapeSpanlogSHA256 = current.ExpectedTapeSpanlogSHA256,
            expectedCurveSHA256 = current.ExpectedCurveSHA256,
            expectedExcursionsSHA256 = current.ExpectedExcursionsSHA256,
            loadedCheckpointSHA256 = current.LoadedCheckpointSHA256,
            loadedTapeSpanlogSHA256 = current.LoadedTapeSpanlogSHA256,
            loadedCurveSHA256 = current.LoadedCurveSHA256,
            loadedExcursionsSHA256 = current.LoadedExcursionsSHA256,
            startStep = current.ExecutionWindow.StartStep,
            endStep = current.ExecutionWindow.EndStep,
            seedIOWallMilliseconds = current.SeedIOWallMilliseconds,
            seedIORawTicks = current.SeedIORawTicks,
            runtimeBindWallMilliseconds = 0,
            bindingDigest = LegacyBindingDigest(current),
            checkpointProofEffectiveImageSHA256 = current.CheckpointProof.EffectiveImageSHA256,
            checkpointProofEffectivePhysicalSHA256 = current.CheckpointProof.EffectivePhysicalSHA256,
            checkpointProofBasePhysicalSHA256 = current.CheckpointProof.BasePhysicalSHA256,
            checkpointProofPhysicalChainSHA256 = current.CheckpointProof.PhysicalChainSHA256,
            checkpointProofConfigDigest = current.CheckpointProof.PersistedConfigDigest,
            checkpointProofNextStep = current.CheckpointProof.NextStep,
            checkpointProofSaveLoadSaveExact = current.CheckpointProof.SaveLoadSaveExact,
            checkpointProofReused = current.CheckpointProofReused,
            excursionCursor = current.ExcursionCursor,
            adoptionAncestry = (current.AdoptionAncestry ?? []).Select(static hop => new CortexForkAdoptionHopDocument
            {
                originRunID = hop.OriginRunID,
                childRunID = hop.ChildRunID,
                sourceNextStep = hop.SourceNextStep,
                persistedConfigDigest = hop.PersistedConfigDigest,
                basePhysicalSHA256 = hop.BasePhysicalSHA256,
                sourceSeedDigest = hop.SourceSeedDigest,
                parentBindingDigest = hop.ParentBindingDigest,
            }).ToList(),
};

    private static string LegacyBindingDigest(in CortexForkSeedLoadReceipt current)
        => (current with
        {
            AncestorSeedDigest = "",
            PreparedSeedDigest = "",
            PreparationRole = CortexForkPreparationRoles.Unknown,
        }).BindingDigest;
}

internal readonly record struct CortexForkSeedLoadRailDocument(
    CortexForkSeedLoadRailReceipt Rail,
    CortexForkSeedLoadReceipt Receipt,
    string StoredBindingDigest,
    int SourceSchemaVersion)
{
    public bool IsLegacy => SourceSchemaVersion == 2;
}

internal readonly record struct CortexForkTerminalOccurrenceCheckDocument(
    CortexForkTerminalOccurrenceCheckReceipt Receipt,
    string StoredSeedLoadBindingDigest,
    int SourceSchemaVersion)
{
    public bool IsLegacy => SourceSchemaVersion == 2;
}

[RonObject]
public partial class CortexForkTerminalOccurrenceCheckReceipt
{
    public const int CurrentSchemaVersion = 3;
    public int schemaVersion = CurrentSchemaVersion;
    public string childRunID = "";
    public string coldSeedDigest = "";
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public string finalExcursionsSHA256 = "";
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
    public string ancestorSeedDigest = "";
    public string preparedSeedDigest = "";
    public CortexForkPreparationRoles preparationRole;

    internal static CortexForkTerminalOccurrenceCheckReceipt Create(
        string childRunID,
        string coldSeedDigest,
        in CortexForkDigests finalDigests,
        long wallMilliseconds,
        bool verified,
        in CortexForkSeedLoadReceipt seedLoad,
        in CortexForkStepSpan stepSpan,
        in CortexForkRunTiming timing)
        => new()
        {
            childRunID = childRunID,
            coldSeedDigest = coldSeedDigest,
            finalCheckpointSHA256 = finalDigests.CheckpointSHA256,
            finalTapeSpanlogSHA256 = finalDigests.TapeSpanlogSHA256,
            finalCurveSHA256 = finalDigests.CurveSHA256,
            finalExcursionsSHA256 = finalDigests.ExcursionsSHA256,
            seedLoadBindingDigest = seedLoad.BindingDigest,
            startStep = stepSpan.SeedNextStep,
            plannedNextStep = stepSpan.PlannedNextStep,
            actualNextStep = stepSpan.ActualNextStep,
            executionWallMilliseconds = timing.ExecutionWallMilliseconds,
            executionRawTicks = timing.ExecutionRawTicks,
            seedIORawTicks = timing.SeedIORawTicks,
            runtimeBindWallMilliseconds = timing.RuntimeBindWallMilliseconds,
            runtimeBindRawTicks = timing.RuntimeBindRawTicks,
            wallMilliseconds = wallMilliseconds,
            verified = verified,
            checkpointProofDigest = seedLoad.CheckpointProof.BindingDigest,
            ancestorSeedDigest = seedLoad.AncestorSeedDigest,
            preparedSeedDigest = seedLoad.PreparedSeedDigest,
            preparationRole = seedLoad.PreparationRole,
        };

    internal void Validate(string expectedChildRunID, string expectedColdSeedDigest)
    {
        if (schemaVersion != CurrentSchemaVersion || childRunID != expectedChildRunID || coldSeedDigest != expectedColdSeedDigest
            || !verified || wallMilliseconds < 0 || executionWallMilliseconds < 0 || executionRawTicks <= 0
            || seedIORawTicks <= 0 || runtimeBindWallMilliseconds < 0 || runtimeBindRawTicks < 0
            || actualNextStep < startStep || plannedNextStep < startStep || actualNextStep > plannedNextStep
            || !IsCanonicalDigest(seedLoadBindingDigest)
            || !IsCanonicalDigest(finalCheckpointSHA256) || !IsCanonicalDigest(finalTapeSpanlogSHA256)
            || !IsCanonicalDigest(finalCurveSHA256) || !IsCanonicalDigest(finalExcursionsSHA256) || !IsCanonicalDigest(checkpointProofDigest))
            throw new InvalidDataException("fork terminal verifier receipt is not a committed exact check");
        if (preparationRole != CortexForkPreparationRoles.Unknown
            && (!IsCanonicalDigest(ancestorSeedDigest) || !IsCanonicalDigest(preparedSeedDigest)))
            throw new InvalidDataException("fork terminal verifier receipt preparation custody is incomplete");
    }

    private static bool IsCanonicalDigest(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

[RonObject]
internal partial class CortexForkTerminalOccurrenceCheckReceiptV2
{
    public int schemaVersion = 2;
    public string childRunID = "";
    public string coldSeedDigest = "";
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public string finalExcursionsSHA256 = "";
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

    internal CortexForkTerminalOccurrenceCheckReceipt ToCurrent()
        => new()
        {
            schemaVersion = CortexForkTerminalOccurrenceCheckReceipt.CurrentSchemaVersion,
            childRunID = childRunID,
            coldSeedDigest = coldSeedDigest,
            finalCheckpointSHA256 = finalCheckpointSHA256,
            finalTapeSpanlogSHA256 = finalTapeSpanlogSHA256,
            finalCurveSHA256 = finalCurveSHA256,
            finalExcursionsSHA256 = finalExcursionsSHA256,
            seedLoadBindingDigest = seedLoadBindingDigest,
            startStep = startStep,
            plannedNextStep = plannedNextStep,
            actualNextStep = actualNextStep,
            executionWallMilliseconds = executionWallMilliseconds,
            executionRawTicks = executionRawTicks,
            seedIORawTicks = seedIORawTicks,
            runtimeBindWallMilliseconds = runtimeBindWallMilliseconds,
            runtimeBindRawTicks = runtimeBindRawTicks,
            wallMilliseconds = wallMilliseconds,
            verified = verified,
            checkpointProofDigest = checkpointProofDigest,
            ancestorSeedDigest = coldSeedDigest,
            preparedSeedDigest = coldSeedDigest,
            preparationRole = CortexForkPreparationRoles.Unknown,
        };

    internal byte[] Encode()
    {
        CortexForkTerminalOccurrenceCheckReceiptV2 document = this;
        return RonSerializer.SerializeToUtf8(in document);
    }
}

/// The immutable terminal authority for one generic fork arm. It is written only after the drive and (when
/// requested) the independent verifier have completed, but before any caller-owned landing hook can fail. The
/// receipt is intentionally self-contained so a process-loss recovery can consume it without driving Cortex.
[RonObject]
public partial class CortexForkTerminalRunReceipt
{
    public const int CurrentSchemaVersion = 3;
    public const string FileName = "terminal-run-receipt.ron";

    public int schemaVersion = CurrentSchemaVersion;
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
    public string expectedExcursionsSHA256 = "";
    public string loadedCheckpointSHA256 = "";
    public string loadedTapeSpanlogSHA256 = "";
    public string loadedCurveSHA256 = "";
    public string loadedExcursionsSHA256 = "";
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
    public string finalExcursionsSHA256 = "";
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
    public long excursionCursor;
    public string ancestorSeedDigest = "";
    public string preparedSeedDigest = "";
    public CortexForkPreparationRoles preparationRole;
    public List<CortexForkAdoptionHopDocument> adoptionAncestry = new();
    public string receiptDigest = "";

    internal static CortexForkTerminalRunReceipt Create(
        in CortexForkSeedLoadReceipt seedLoad,
        in CortexForkDigests startingDigests,
        in CortexForkDigests finalDigests,
        in CortexForkStepSpan stepSpan,
        in CortexForkRunTiming timing,
        int exitCode,
        bool runtimeStopRequested,
        bool terminalCheckpointExact,
        bool terminalOccurrenceCheckAttempted,
        bool terminalOccurrenceCheckExact,
        string seedLoadIntentDigest,
        string seedLoadReceiptDigest,
        string terminalOccurrenceCheckReceiptDigest,
        string anytimeCurveDigest)
    {
        if (!DigestsEqual(startingDigests, seedLoad.ExpectedDigests))
            throw new InvalidDataException("fork terminal run receipt seed authority disagrees with the run inputs");
        CortexForkTerminalRunReceipt receipt = new()
        {
            parentRunID = seedLoad.ParentRunID,
            childRunID = seedLoad.ChildRunID,
            role = seedLoad.Role,
            coldSeedDigest = seedLoad.ColdSeedDigest,
            persistedConfigDigest = seedLoad.PersistedConfigDigest,
            seedLoadBindingDigest = seedLoad.BindingDigest,
            seedLoadIntentDigest = seedLoadIntentDigest,
            seedLoadReceiptDigest = seedLoadReceiptDigest,
            sourceSeedDigest = seedLoad.SourceSeedDigest,
            sourceRunID = seedLoad.SourceRunID,
            sourceNextStep = seedLoad.SourceNextStep,
            expectedCheckpointSHA256 = seedLoad.ExpectedCheckpointSHA256,
            expectedTapeSpanlogSHA256 = seedLoad.ExpectedTapeSpanlogSHA256,
            expectedCurveSHA256 = seedLoad.ExpectedCurveSHA256,
            expectedExcursionsSHA256 = seedLoad.ExpectedExcursionsSHA256,
            loadedCheckpointSHA256 = seedLoad.LoadedCheckpointSHA256,
            loadedTapeSpanlogSHA256 = seedLoad.LoadedTapeSpanlogSHA256,
            loadedCurveSHA256 = seedLoad.LoadedCurveSHA256,
            loadedExcursionsSHA256 = seedLoad.LoadedExcursionsSHA256,
            startStep = stepSpan.SeedNextStep,
            plannedNextStep = stepSpan.PlannedNextStep,
            actualNextStep = stepSpan.ActualNextStep,
            exitCode = exitCode,
            runtimeStopRequested = runtimeStopRequested,
            terminalCheckpointExact = terminalCheckpointExact,
            terminalOccurrenceCheckAttempted = terminalOccurrenceCheckAttempted,
            terminalOccurrenceCheckExact = terminalOccurrenceCheckExact,
            finalCheckpointSHA256 = finalDigests.CheckpointSHA256,
            finalTapeSpanlogSHA256 = finalDigests.TapeSpanlogSHA256,
            finalCurveSHA256 = finalDigests.CurveSHA256,
            finalExcursionsSHA256 = finalDigests.ExcursionsSHA256,
            seedIOWallMilliseconds = timing.SeedIOWallMilliseconds,
            seedIORawTicks = timing.SeedIORawTicks,
            runtimeBindWallMilliseconds = timing.RuntimeBindWallMilliseconds,
            runtimeBindRawTicks = timing.RuntimeBindRawTicks,
            executionWallMilliseconds = timing.ExecutionWallMilliseconds,
            executionRawTicks = timing.ExecutionRawTicks,
            terminalVerifierWallMilliseconds = timing.TerminalVerifierWallMilliseconds,
            terminalVerifierRawTicks = timing.TerminalVerifierRawTicks,
            totalWallMilliseconds = timing.TotalWallMilliseconds,
            totalRawTicks = timing.TotalRawTicks,
            terminalOccurrenceCheckReceiptDigest = terminalOccurrenceCheckReceiptDigest,
            anytimeCurveDigest = anytimeCurveDigest,
            checkpointProofDigest = seedLoad.CheckpointProof.BindingDigest,
            checkpointProofEffectiveImageSHA256 = seedLoad.CheckpointProof.EffectiveImageSHA256,
            checkpointProofEffectivePhysicalSHA256 = seedLoad.CheckpointProof.EffectivePhysicalSHA256,
            checkpointProofBasePhysicalSHA256 = seedLoad.CheckpointProof.BasePhysicalSHA256,
            checkpointProofPhysicalChainSHA256 = seedLoad.CheckpointProof.PhysicalChainSHA256,
            checkpointProofConfigDigest = seedLoad.CheckpointProof.PersistedConfigDigest,
            checkpointProofNextStep = seedLoad.CheckpointProof.NextStep,
            checkpointProofSaveLoadSaveExact = seedLoad.CheckpointProof.SaveLoadSaveExact,
            checkpointProofReused = seedLoad.CheckpointProofReused,
            excursionCursor = seedLoad.ExcursionCursor,
            ancestorSeedDigest = seedLoad.AncestorSeedDigest,
            preparedSeedDigest = seedLoad.PreparedSeedDigest,
            preparationRole = seedLoad.PreparationRole,
            adoptionAncestry = (seedLoad.AdoptionAncestry ?? []).Select(static hop => new CortexForkAdoptionHopDocument
            {
                originRunID = hop.OriginRunID,
                childRunID = hop.ChildRunID,
                sourceNextStep = hop.SourceNextStep,
                persistedConfigDigest = hop.PersistedConfigDigest,
                basePhysicalSHA256 = hop.BasePhysicalSHA256,
                sourceSeedDigest = hop.SourceSeedDigest,
                parentBindingDigest = hop.ParentBindingDigest,
            }).ToList(),
        };
        receipt.receiptDigest = receipt.ComputeReceiptDigest();
        return receipt;
    }

    public static CortexForkTerminalRunReceipt Read(string runDirectory)
    {
        string path = Path.Combine(Path.GetFullPath(runDirectory), FileName);
        if (!File.Exists(path)) throw new InvalidDataException($"missing fork terminal run receipt: {path}");
        byte[] bytes = File.ReadAllBytes(path);
        CortexForkTerminalRunReceipt receipt;
        try { receipt = RonSerializer.Deserialize<CortexForkTerminalRunReceipt>(bytes); }
        catch (Exception error) { throw new InvalidDataException("fork terminal run receipt is not readable RON", error); }
        byte[] canonical = RonSerializer.SerializeToUtf8(in receipt);
        if (!bytes.AsSpan().SequenceEqual(canonical))
            throw new InvalidDataException("fork terminal run receipt is not canonical SaveLoadSave data");
        receipt.ValidateAgainstFiles(runDirectory);
        return receipt;
    }

    public static CortexForkRunReceipt<TOutcome> Recover<TOutcome>(string runDirectory, TOutcome outcome, string anytimeCurveDigest = "", bool requireTerminalOccurrenceCheck = true)
        => RecoverCore(runDirectory, outcome, anytimeCurveDigest, requireTerminalOccurrenceCheck, rejectRuntimeStop: true);

    internal static CortexForkRunReceipt<TOutcome> RecoverForRun<TOutcome>(string runDirectory, TOutcome outcome, string anytimeCurveDigest = "", bool requireTerminalOccurrenceCheck = false)
        => RecoverCore(runDirectory, outcome, anytimeCurveDigest, requireTerminalOccurrenceCheck, rejectRuntimeStop: false);

    private static CortexForkRunReceipt<TOutcome> RecoverCore<TOutcome>(string runDirectory, TOutcome outcome,
        string anytimeCurveDigest, bool requireTerminalOccurrenceCheck, bool rejectRuntimeStop)
    {
        CortexForkTerminalRunReceipt receipt = Read(runDirectory);
        if (requireTerminalOccurrenceCheck && (!receipt.terminalOccurrenceCheckAttempted || !receipt.terminalOccurrenceCheckExact))
            throw new InvalidDataException("fork terminal recovery requires an independently verified terminal receipt");
        if (rejectRuntimeStop && receipt.runtimeStopRequested)
            throw new InvalidDataException("runtime-stop fork terminals require their completion predicate owner for recovery");
        CortexForkSeedLoadReceipt seedLoad = receipt.ToSeedLoadReceipt();
        CortexForkStepSpan span = new(receipt.startStep, receipt.plannedNextStep, receipt.actualNextStep);
        CortexForkRunTiming timing = receipt.ToTiming();
        return new CortexForkRunReceipt<TOutcome>(Path.GetFullPath(runDirectory),
            new CortexForkDigests(receipt.expectedCheckpointSHA256, receipt.expectedTapeSpanlogSHA256, receipt.expectedCurveSHA256, receipt.expectedExcursionsSHA256),
            new CortexForkDigests(receipt.finalCheckpointSHA256, receipt.finalTapeSpanlogSHA256, receipt.finalCurveSHA256, receipt.finalExcursionsSHA256),
            seedLoad, span, timing, receipt.exitCode, receipt.terminalCheckpointExact, outcome,
            string.IsNullOrWhiteSpace(anytimeCurveDigest) ? receipt.anytimeCurveDigest : anytimeCurveDigest);
    }

    public void ValidateAgainstFiles(string runDirectory)
    {
        string directory = Path.GetFullPath(runDirectory);
        if (schemaVersion != CurrentSchemaVersion || Path.GetFileName(directory) != childRunID
            || Path.GetFileName(Path.GetDirectoryName(directory)) != "children"
            || Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(directory))) != parentRunID
            || !MatchesRoleToken(childRunID, role)
            || role == CortexForkRailRoles.Unknown || exitCode != 0 || !terminalCheckpointExact
            || terminalOccurrenceCheckAttempted && !terminalOccurrenceCheckExact
            || string.IsNullOrWhiteSpace(parentRunID) || string.IsNullOrWhiteSpace(sourceRunID)
            || sourceNextStep < 0 || startStep < 0 || plannedNextStep < startStep || actualNextStep < startStep
            || excursionCursor < 0
            || seedLoadIntentDigest.Length != 64 || seedLoadReceiptDigest.Length != 64
            || terminalOccurrenceCheckAttempted && terminalOccurrenceCheckReceiptDigest.Length != 64
            || !terminalOccurrenceCheckAttempted && (terminalOccurrenceCheckExact || terminalOccurrenceCheckReceiptDigest.Length != 0
                || terminalVerifierWallMilliseconds != 0 || terminalVerifierRawTicks != 0))
            throw new InvalidDataException("fork terminal run receipt identity or success contract is incomplete");
        foreach (string digest in new[] { coldSeedDigest, persistedConfigDigest, seedLoadBindingDigest, seedLoadIntentDigest,
            seedLoadReceiptDigest, sourceSeedDigest, expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256,
            loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256, loadedExcursionsSHA256, finalCheckpointSHA256, finalTapeSpanlogSHA256,
            finalCurveSHA256, finalExcursionsSHA256, checkpointProofDigest, checkpointProofEffectiveImageSHA256,
            checkpointProofEffectivePhysicalSHA256, checkpointProofBasePhysicalSHA256,
            checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest })
            RequireDigest(digest, "fork terminal run receipt digest");
        if (preparationRole != CortexForkPreparationRoles.Unknown)
        {
            RequireDigest(ancestorSeedDigest, "fork terminal ancestor seed digest");
            RequireDigest(preparedSeedDigest, "fork terminal prepared seed digest");
        }
        if (terminalOccurrenceCheckAttempted) RequireDigest(terminalOccurrenceCheckReceiptDigest, "fork terminal verifier receipt digest");
        if (anytimeCurveDigest.Length > 0) RequireDigest(anytimeCurveDigest, "fork anytime curve digest");
        CheckpointRoundTripProof checkpointProof = new(checkpointProofEffectiveImageSHA256,
            checkpointProofEffectivePhysicalSHA256, checkpointProofBasePhysicalSHA256,
            checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest, checkpointProofNextStep,
            checkpointProofSaveLoadSaveExact);
        if (!checkpointProof.IsBound || checkpointProof.BindingDigest != checkpointProofDigest
            || checkpointProof.NextStep != sourceNextStep || checkpointProof.PersistedConfigDigest != persistedConfigDigest)
            throw new InvalidDataException("fork terminal run receipt checkpoint proof is incomplete");
        CortexExecutionWindow window = new CortexExecutionWindow(startStep, plannedNextStep).Validate();
        if (actualNextStep > plannedNextStep || totalWallMilliseconds != ToTiming().TotalWallMilliseconds
            || totalRawTicks != ToTiming().TotalRawTicks || totalWallMilliseconds < 0 || totalRawTicks <= 0
            || seedIOWallMilliseconds < 0 || executionWallMilliseconds < 0 || runtimeBindWallMilliseconds < 0
            || terminalVerifierWallMilliseconds < 0 || seedIORawTicks <= 0 || executionRawTicks <= 0
            || runtimeBindRawTicks < 0 || terminalOccurrenceCheckAttempted && terminalVerifierRawTicks <= 0
            || !terminalOccurrenceCheckAttempted && terminalVerifierRawTicks != 0)
            throw new InvalidDataException("fork terminal run receipt timing or execution window is incomplete");
        if (!runtimeStopRequested && actualNextStep != plannedNextStep)
            throw new InvalidDataException("exact fork terminal receipt did not reach its planned horizon");

        CortexForkSeedLoadRailDocument seedRailDocument = ReadSeedRailDocument(Path.Combine(directory, "seed-load-receipt.ron"));
        CortexForkSeedLoadRailReceipt seedRail = seedRailDocument.Rail;
        CortexForkSeedLoadReceipt seedLoad = seedRailDocument.Receipt;
        if (!seedLoad.Bound || !seedLoad.Exact || seedLoad.SeedIOWallMilliseconds < 0 || seedLoad.SeedIORawTicks <= 0
            || seedRailDocument.StoredBindingDigest != seedLoadBindingDigest || seedLoad.ParentRunID != parentRunID || seedLoad.ChildRunID != childRunID
            || seedLoad.Role != role || seedLoad.ColdSeedDigest != coldSeedDigest
            || seedLoad.PersistedConfigDigest != persistedConfigDigest || seedLoad.SourceSeedDigest != sourceSeedDigest
            || seedLoad.SourceRunID != sourceRunID || seedLoad.ExecutionWindow != window || seedLoad.SourceNextStep != sourceNextStep
            || !seedRailDocument.IsLegacy && (seedLoad.AncestorSeedDigest != ancestorSeedDigest
                || seedLoad.PreparedSeedDigest != preparedSeedDigest
                || seedLoad.PreparationRole != preparationRole)
            || seedLoad.CheckpointProof != checkpointProof || seedLoad.CheckpointProofReused != checkpointProofReused
            || seedLoad.ExcursionCursor != excursionCursor
            || !DigestsEqual(seedLoad.ExpectedDigests, new CortexForkDigests(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256))
            || !DigestsEqual(seedLoad.LoadedDigests, new CortexForkDigests(loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256, loadedExcursionsSHA256))
            || seedLoad.SeedIOWallMilliseconds != seedIOWallMilliseconds || seedLoad.SeedIORawTicks != seedIORawTicks
            || seedRail.runtimeBindWallMilliseconds != runtimeBindWallMilliseconds)
            throw new InvalidDataException("fork terminal run receipt disagrees with seed-load receipt");
        string intentPath = Path.Combine(directory, "seed-load-intent.ron");
        if (!File.Exists(intentPath) || ComputeFileSHA256(intentPath) != seedLoadIntentDigest)
            throw new InvalidDataException("fork terminal run receipt seed-load intent is missing or changed");
        CortexForkSeedLoadRailDocument intentDocument = ReadSeedRailDocument(intentPath);
        CortexForkSeedLoadRailReceipt intentRail = intentDocument.Rail;
        CortexForkSeedLoadReceipt intent = intentDocument.Receipt;
        if (intentRail.runtimeBindWallMilliseconds != 0
            || intentDocument.StoredBindingDigest != seedRailDocument.StoredBindingDigest)
            throw new InvalidDataException("fork terminal run receipt seed-load intent disagrees with its committed receipt");
        if (ComputeFileSHA256(Path.Combine(directory, "seed-load-receipt.ron")) != seedLoadReceiptDigest)
            throw new InvalidDataException("fork terminal run receipt seed-load receipt is missing or changed");

        CortexForkDigests actual = CortexForkRunner.ReadRunDigests(directory);
        if (!DigestsEqual(actual, new CortexForkDigests(finalCheckpointSHA256, finalTapeSpanlogSHA256, finalCurveSHA256, finalExcursionsSHA256)))
            throw new InvalidDataException("fork terminal run receipt disagrees with landed artifacts");
        if (Checkpoint.PeekNextStep(directory) != actualNextStep)
            throw new InvalidDataException("fork terminal run receipt actual step disagrees with the final checkpoint cursor");
        string parentDirectory = Path.GetFullPath(Path.Combine(directory, "..", ".."));
        string sourceDirectory = sourceRunID == parentRunID
            ? parentDirectory
            : Path.Combine(parentDirectory, "children", sourceRunID);
        CortexForkDigests expectedSeed = new(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256);
        if (sourceRunID == parentRunID)
        {
            // The parent ID denotes the immutable ancestor captured before the
            // parent drive. A prepared arm may have different loaded bytes, so
            // its expected seed is not the parent custody image.
            string expectedAncestor = string.IsNullOrWhiteSpace(ancestorSeedDigest)
                ? CortexForkRunner.ComputeSeedIdentity(expectedSeed, persistedConfigDigest)
                : ancestorSeedDigest;
            if (sourceSeedDigest != expectedAncestor || coldSeedDigest != expectedAncestor)
                throw new InvalidDataException("fork terminal run receipt ancestor seed authority formula disagrees");
        }
        else
        {
            if (Path.GetFileName(sourceDirectory) != sourceRunID || !Directory.Exists(sourceDirectory)
                || Checkpoint.PeekNextStep(sourceDirectory) != sourceNextStep)
                throw new InvalidDataException("fork terminal run receipt continuation source path or cursor disagrees");
            CortexForkDigests sourceDigests = CortexForkRunner.ReadRunDigests(sourceDirectory);
            if (!DigestsEqual(sourceDigests, expectedSeed)
                || CortexForkRunner.ComputeSeedIdentity(sourceDigests, persistedConfigDigest) != sourceSeedDigest)
                throw new InvalidDataException("fork terminal run receipt source files disagree with seed authority");
        }
        if (terminalOccurrenceCheckAttempted)
        {
            string terminalPath = Path.Combine(directory, "terminal-verification.ron");
            if (!File.Exists(terminalPath) || ComputeFileSHA256(terminalPath) != terminalOccurrenceCheckReceiptDigest)
                throw new InvalidDataException("fork terminal verifier receipt is missing or changed");
            CortexForkTerminalOccurrenceCheckDocument terminalDocument = ReadTerminalOccurrenceCheckDocument(terminalPath, preparationRole);
            CortexForkTerminalOccurrenceCheckReceipt terminal = terminalDocument.Receipt;
            terminal.Validate(childRunID, coldSeedDigest);
            if (terminal.finalCheckpointSHA256 != finalCheckpointSHA256 || terminal.finalTapeSpanlogSHA256 != finalTapeSpanlogSHA256
                || terminal.finalCurveSHA256 != finalCurveSHA256 || terminal.finalExcursionsSHA256 != finalExcursionsSHA256 || terminal.seedLoadBindingDigest != seedLoadBindingDigest
                || terminalDocument.StoredSeedLoadBindingDigest != seedRailDocument.StoredBindingDigest
                || !terminalDocument.IsLegacy && (terminal.ancestorSeedDigest != ancestorSeedDigest
                    || terminal.preparedSeedDigest != preparedSeedDigest
                    || terminal.preparationRole != preparationRole)
                || terminal.checkpointProofDigest != checkpointProofDigest
                || terminal.startStep != startStep || terminal.plannedNextStep != plannedNextStep
                || terminal.actualNextStep != actualNextStep || terminal.executionWallMilliseconds != executionWallMilliseconds
                || terminal.executionRawTicks != executionRawTicks || terminal.seedIORawTicks != seedIORawTicks
                || terminal.runtimeBindWallMilliseconds != runtimeBindWallMilliseconds
                || terminal.runtimeBindRawTicks != runtimeBindRawTicks
                || terminal.wallMilliseconds != terminalVerifierWallMilliseconds)
                throw new InvalidDataException("fork terminal verifier receipt disagrees with landed artifacts");
        }
        else if (File.Exists(Path.Combine(directory, "terminal-verification.ron")))
            throw new InvalidDataException("fork terminal run receipt has an unexpected verifier sidecar");
        if (receiptDigest != ComputeReceiptDigest()) throw new InvalidDataException("fork terminal run receipt digest is corrupt");
    }

    internal void WriteAppendSafe(string runDirectory)
    {
        CortexForkTerminalRunReceipt document = this;
        byte[] bytes = RonSerializer.SerializeToUtf8(in document);
        string path = Path.Combine(Path.GetFullPath(runDirectory), FileName);
        if (File.Exists(path))
        {
            if (!Cortex.FileContentEquals(path, bytes))
                throw new InvalidDataException("fork terminal run receipt conflicts with its prior image");
            return;
        }
        Run.Open(runDirectory).WriteAtomic(FileName, stream => stream.Write(bytes));
    }

    private CortexForkSeedLoadReceipt ToSeedLoadReceipt()
        => new(new CortexForkDigests(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256),
            new CortexForkDigests(loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256, loadedExcursionsSHA256), seedIOWallMilliseconds,
            parentRunID, childRunID, role, coldSeedDigest, persistedConfigDigest,
            new CortexExecutionWindow(startStep, plannedNextStep), sourceSeedDigest, sourceRunID, sourceNextStep, seedIORawTicks,
            new CheckpointRoundTripProof(checkpointProofEffectiveImageSHA256, checkpointProofEffectivePhysicalSHA256,
                checkpointProofBasePhysicalSHA256, checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest,
                checkpointProofNextStep, checkpointProofSaveLoadSaveExact), checkpointProofReused, excursionCursor,
            adoptionAncestry.Select(static hop => new CortexForkAdoptionHop(
                hop.originRunID, hop.childRunID, hop.sourceNextStep, hop.persistedConfigDigest,
                hop.basePhysicalSHA256, hop.sourceSeedDigest, hop.parentBindingDigest)).ToArray(),
            ancestorSeedDigest, preparedSeedDigest, preparationRole);

    private CortexForkRunTiming ToTiming()
        => new(seedIOWallMilliseconds, executionWallMilliseconds, terminalVerifierWallMilliseconds, 1,
            terminalOccurrenceCheckAttempted ? 1 : 0, runtimeBindWallMilliseconds, seedIORawTicks, executionRawTicks,
            terminalVerifierRawTicks, runtimeBindRawTicks, checkpointProofReused ? 1 : 0,
            checkpointProofReused ? 0 : 1);

    private string ComputeReceiptDigest()
        => ComputeSHA256(string.Join('|', schemaVersion, parentRunID, childRunID, role, coldSeedDigest, persistedConfigDigest,
            seedLoadBindingDigest, seedLoadIntentDigest, seedLoadReceiptDigest, sourceSeedDigest, sourceRunID, sourceNextStep,
            expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256, expectedExcursionsSHA256, loadedCheckpointSHA256,
            loadedTapeSpanlogSHA256, loadedCurveSHA256, loadedExcursionsSHA256, startStep, plannedNextStep, actualNextStep, exitCode,
            terminalCheckpointExact, terminalOccurrenceCheckAttempted, terminalOccurrenceCheckExact, finalCheckpointSHA256,
            finalTapeSpanlogSHA256, finalCurveSHA256, finalExcursionsSHA256, seedIOWallMilliseconds, seedIORawTicks, runtimeBindWallMilliseconds,
            runtimeBindRawTicks, executionWallMilliseconds, executionRawTicks, terminalVerifierWallMilliseconds,
            terminalVerifierRawTicks, totalWallMilliseconds, totalRawTicks, terminalOccurrenceCheckReceiptDigest,
            anytimeCurveDigest, runtimeStopRequested, checkpointProofDigest, checkpointProofEffectiveImageSHA256,
            checkpointProofEffectivePhysicalSHA256, checkpointProofBasePhysicalSHA256,
            checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest, checkpointProofNextStep,
            checkpointProofSaveLoadSaveExact, checkpointProofReused, excursionCursor,
            ancestorSeedDigest, preparedSeedDigest, preparationRole,
            string.Join("|", adoptionAncestry.Select(static hop => string.Join("|", hop.originRunID,
                hop.childRunID, hop.sourceNextStep, hop.persistedConfigDigest, hop.basePhysicalSHA256,
                hop.sourceSeedDigest, hop.parentBindingDigest)))));

    private static CortexForkSeedLoadRailDocument ReadSeedRailDocumentCore(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("fork terminal run receipt is missing seed-load receipt");
        byte[] bytes = File.ReadAllBytes(path);
        CortexForkSeedLoadRailReceipt probe;
        try { probe = RonSerializer.Deserialize<CortexForkSeedLoadRailReceipt>(bytes); }
        catch (Exception error) { throw new InvalidDataException("fork seed-load receipt is not readable RON", error); }
        if (probe.schemaVersion == CortexForkSeedLoadRailReceipt.CurrentSchemaVersion)
        {
            if (!bytes.AsSpan().SequenceEqual(probe.Encode())) throw new InvalidDataException("fork seed-load receipt is not canonical");
            CortexForkSeedLoadReceipt currentReceipt = probe.ToReceipt();
            if (probe.bindingDigest != currentReceipt.BindingDigest)
                throw new InvalidDataException("fork seed-load receipt binding digest is not canonical");
            return new CortexForkSeedLoadRailDocument(probe, currentReceipt, probe.bindingDigest, probe.schemaVersion);
        }
        if (probe.schemaVersion != 2)
            throw new InvalidDataException("fork seed-load receipt schema is retired");
        CortexForkSeedLoadRailReceiptV2 legacy;
        try { legacy = RonSerializer.Deserialize<CortexForkSeedLoadRailReceiptV2>(bytes); }
        catch (Exception error) { throw new InvalidDataException("legacy fork seed-load receipt is not readable RON", error); }
        if (!bytes.AsSpan().SequenceEqual(legacy.Encode()))
            throw new InvalidDataException("legacy fork seed-load receipt is not canonical");
        CortexForkSeedLoadRailReceipt migrated = legacy.ToCurrent();
        CortexForkSeedLoadReceipt legacyReceipt = migrated.ToReceipt();
        return new CortexForkSeedLoadRailDocument(migrated, legacyReceipt, legacy.bindingDigest, legacy.schemaVersion);
    }

    private static CortexForkSeedLoadRailReceipt ReadSeedRail(string path)
        => ReadSeedRailDocumentCore(path).Rail;

    internal static CortexForkSeedLoadRailDocument ReadSeedRailDocument(string path)
        => ReadSeedRailDocumentCore(path);

    private static CortexForkTerminalOccurrenceCheckDocument ReadTerminalOccurrenceCheckDocumentCore(
        string path, CortexForkPreparationRoles preparationRole)
    {
        byte[] bytes = File.ReadAllBytes(path);
        CortexForkTerminalOccurrenceCheckReceipt probe;
        try { probe = RonSerializer.Deserialize<CortexForkTerminalOccurrenceCheckReceipt>(bytes); }
        catch (Exception error) { throw new InvalidDataException("fork terminal verifier receipt is not readable RON", error); }
        if (probe.schemaVersion == CortexForkTerminalOccurrenceCheckReceipt.CurrentSchemaVersion)
        {
            if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in probe)))
                throw new InvalidDataException("fork terminal verifier receipt is not canonical SaveLoadSave data");
            return new CortexForkTerminalOccurrenceCheckDocument(probe, probe.seedLoadBindingDigest, probe.schemaVersion);
        }
        if (probe.schemaVersion != 2)
            throw new InvalidDataException("fork terminal verifier receipt schema is retired");
        CortexForkTerminalOccurrenceCheckReceiptV2 legacy;
        try { legacy = RonSerializer.Deserialize<CortexForkTerminalOccurrenceCheckReceiptV2>(bytes); }
        catch (Exception error) { throw new InvalidDataException("legacy fork terminal verifier receipt is not readable RON", error); }
        if (!bytes.AsSpan().SequenceEqual(legacy.Encode()))
            throw new InvalidDataException("legacy fork terminal verifier receipt is not canonical SaveLoadSave data");
        CortexForkTerminalOccurrenceCheckReceipt migrated = legacy.ToCurrent();
        migrated.preparationRole = preparationRole;
        return new CortexForkTerminalOccurrenceCheckDocument(migrated, legacy.seedLoadBindingDigest, legacy.schemaVersion);
    }

    internal static CortexForkTerminalOccurrenceCheckDocument ReadTerminalOccurrenceCheckDocument(
        string path, CortexForkPreparationRoles preparationRole)
        => ReadTerminalOccurrenceCheckDocumentCore(path, preparationRole);

    private static CortexForkTerminalOccurrenceCheckReceipt ReadTerminalOccurrenceCheckReceipt(
        string path, CortexForkPreparationRoles preparationRole)
        => ReadTerminalOccurrenceCheckDocumentCore(path, preparationRole).Receipt;

    private static void RequireDigest(string value, string label)
    {
        if (value.Length != 64 || value.Any(static c => c is < '0' or > '9' and < 'a' or > 'f'))
            throw new InvalidDataException($"{label} is not canonical lowercase SHA-256");
    }

    private static bool DigestsEqual(in CortexForkDigests left, in CortexForkDigests right)
        => left.CheckpointSHA256 == right.CheckpointSHA256 && left.TapeSpanlogSHA256 == right.TapeSpanlogSHA256 && left.CurveSHA256 == right.CurveSHA256 && left.ExcursionsSHA256 == right.ExcursionsSHA256;

    private static string ComputeFileSHA256(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing fork receipt artifact: {path}");
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string ComputeSHA256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string RoleToken(CortexForkRailRoles value)
        => value switch
        {
            CortexForkRailRoles.ForcedNull => "forced-null",
            CortexForkRailRoles.ReflexFrozen => "reflex-frozen",
            _ => value.ToString().ToLowerInvariant(),
        };

    private static bool MatchesRoleToken(string childID, CortexForkRailRoles role)
    {
        if (role == CortexForkRailRoles.Unknown) return false;
        string prefix = RoleToken(role) + "_";
        return childID.StartsWith(prefix, StringComparison.Ordinal)
            && childID.Length > prefix.Length
            && childID[prefix.Length..].All(char.IsAsciiDigit);
    }

}

public enum CortexForkLandingOutcomeStates
{
    TerminalChildExact,
    Completed,
    CallbackFailed,
}

/// The callback is caller-owned work after the child terminal has been committed. Its wall/raw span is a
/// separate accounting segment: it never inflates the child execution or verifier timings. The receipt binds
/// that outcome to the immutable terminal authority chain so recovery can distinguish an exact child from a
/// completed callback and a callback that failed after the child had already landed.
[RonObject]
public partial class CortexForkLandingOutcomeReceipt
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "landing-outcome.ron";

    public int schemaVersion = CurrentSchemaVersion;
    public string parentRunID = "";
    public string childRunID = "";
    public CortexForkRailRoles role;
    public CortexForkLandingOutcomeStates state;
    public bool callbackAttempted;
    public bool callbackReturned;
    public string callbackExceptionType = "";
    public string callbackExceptionMessage = "";
    public long callbackWallMilliseconds;
    public long callbackRawTicks;
    public string terminalRunReceiptSHA256 = "";
    public string terminalReceiptDigest = "";
    public string authorityBeforeSeedIntentSHA256 = "";
    public string authorityBeforeSeedReceiptSHA256 = "";
    public string authorityBeforeVerifierSHA256 = "";
    public string authorityBeforeTerminalSHA256 = "";
    public string authorityAfterSeedIntentSHA256 = "";
    public string authorityAfterSeedReceiptSHA256 = "";
    public string authorityAfterVerifierSHA256 = "";
    public string authorityAfterTerminalSHA256 = "";
    public bool authorityChainExact;
    public string receiptDigest = "";

    internal static CortexForkLandingOutcomeReceipt Create(
        string runDirectory,
        CortexForkLandingOutcomeStates state,
        bool callbackAttempted,
        bool callbackReturned,
        Exception? callbackError,
        long callbackWallMilliseconds,
        long callbackRawTicks,
        in CortexForkTerminalRunReceipt terminal,
        in CortexForkAuthorityChain authorityBefore,
        in CortexForkAuthorityChain authorityAfter,
        bool authorityChainExact)
    {
        string terminalPath = Path.Combine(Path.GetFullPath(runDirectory), CortexForkTerminalRunReceipt.FileName);
        CortexForkLandingOutcomeReceipt receipt = new()
        {
            parentRunID = terminal.parentRunID,
            childRunID = terminal.childRunID,
            role = terminal.role,
            state = state,
            callbackAttempted = callbackAttempted,
            callbackReturned = callbackReturned,
            callbackExceptionType = callbackError?.GetType().FullName ?? "",
            callbackExceptionMessage = callbackError?.Message ?? "",
            callbackWallMilliseconds = callbackWallMilliseconds,
            callbackRawTicks = callbackRawTicks,
            terminalRunReceiptSHA256 = ComputeFileSHA256(terminalPath),
            terminalReceiptDigest = terminal.receiptDigest,
            authorityBeforeSeedIntentSHA256 = authorityBefore.SeedIntentSHA256,
            authorityBeforeSeedReceiptSHA256 = authorityBefore.SeedReceiptSHA256,
            authorityBeforeVerifierSHA256 = authorityBefore.VerifierSHA256,
            authorityBeforeTerminalSHA256 = authorityBefore.TerminalSHA256,
            authorityAfterSeedIntentSHA256 = authorityAfter.SeedIntentSHA256,
            authorityAfterSeedReceiptSHA256 = authorityAfter.SeedReceiptSHA256,
            authorityAfterVerifierSHA256 = authorityAfter.VerifierSHA256,
            authorityAfterTerminalSHA256 = authorityAfter.TerminalSHA256,
            authorityChainExact = authorityChainExact,
        };
        receipt.receiptDigest = receipt.ComputeReceiptDigest();
        return receipt;
    }

    public static CortexForkLandingOutcomeReceipt Read(string runDirectory)
    {
        string path = Path.Combine(Path.GetFullPath(runDirectory), FileName);
        if (!File.Exists(path)) throw new InvalidDataException($"missing fork landing outcome receipt: {path}");
        byte[] bytes = File.ReadAllBytes(path);
        CortexForkLandingOutcomeReceipt receipt;
        try { receipt = RonSerializer.Deserialize<CortexForkLandingOutcomeReceipt>(bytes); }
        catch (Exception error) { throw new InvalidDataException("fork landing outcome receipt is not readable RON", error); }
        if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in receipt)))
            throw new InvalidDataException("fork landing outcome receipt is not canonical SaveLoadSave data");
        receipt.ValidateAgainstFiles(runDirectory);
        return receipt;
    }

    public void ValidateAgainstFiles(string runDirectory)
    {
        string directory = Path.GetFullPath(runDirectory);
        CortexForkTerminalRunReceipt terminal = CortexForkTerminalRunReceipt.Read(directory);
        if (schemaVersion != CurrentSchemaVersion || parentRunID != terminal.parentRunID || childRunID != terminal.childRunID
            || role != terminal.role || Path.GetFileName(directory) != childRunID
            || !IsCanonicalDigest(terminalRunReceiptSHA256) || terminalRunReceiptSHA256 != ComputeFileSHA256(Path.Combine(directory, CortexForkTerminalRunReceipt.FileName))
            || !IsCanonicalDigest(terminalReceiptDigest) || terminalReceiptDigest != terminal.receiptDigest
            || !IsCanonicalDigest(authorityBeforeSeedIntentSHA256) || !IsCanonicalDigest(authorityBeforeSeedReceiptSHA256)
            || authorityBeforeVerifierSHA256.Length != 0 && !IsCanonicalDigest(authorityBeforeVerifierSHA256)
            || !IsCanonicalDigest(authorityBeforeTerminalSHA256)
            || !IsCanonicalDigest(authorityAfterSeedIntentSHA256) || !IsCanonicalDigest(authorityAfterSeedReceiptSHA256)
            || authorityAfterVerifierSHA256.Length != 0 && !IsCanonicalDigest(authorityAfterVerifierSHA256)
            || !IsCanonicalDigest(authorityAfterTerminalSHA256)
            || !authorityChainExact
            || authorityBeforeSeedIntentSHA256 != authorityAfterSeedIntentSHA256
            || authorityBeforeSeedReceiptSHA256 != authorityAfterSeedReceiptSHA256
            || authorityBeforeVerifierSHA256 != authorityAfterVerifierSHA256
            || authorityBeforeTerminalSHA256 != authorityAfterTerminalSHA256)
            throw new InvalidDataException("fork landing outcome is not bound to an immutable terminal authority chain");

        switch (state)
        {
            case CortexForkLandingOutcomeStates.TerminalChildExact when callbackAttempted || callbackReturned
                || callbackExceptionType.Length != 0 || callbackExceptionMessage.Length != 0
                || callbackWallMilliseconds != 0 || callbackRawTicks != 0:
                throw new InvalidDataException("terminal-child landing outcome contains callback data");
            case CortexForkLandingOutcomeStates.Completed when !callbackAttempted || !callbackReturned
                || callbackExceptionType.Length != 0 || callbackExceptionMessage.Length != 0:
                throw new InvalidDataException("completed landing outcome has an incomplete callback contract");
            case CortexForkLandingOutcomeStates.CallbackFailed when !callbackAttempted || callbackReturned
                || callbackExceptionType.Length == 0:
                throw new InvalidDataException("failed landing outcome has an incomplete callback contract");
            case CortexForkLandingOutcomeStates.TerminalChildExact:
            case CortexForkLandingOutcomeStates.Completed:
            case CortexForkLandingOutcomeStates.CallbackFailed:
                break;
            default:
                throw new InvalidDataException("fork landing outcome state is unknown");
        }
        if (callbackAttempted && (callbackRawTicks <= 0 || callbackWallMilliseconds < 0)
            || !callbackAttempted && (callbackWallMilliseconds != 0 || callbackRawTicks != 0)
            || receiptDigest != ComputeReceiptDigest())
            throw new InvalidDataException("fork landing outcome timing or digest is corrupt");
    }

    internal void WriteAppendSafe(string runDirectory)
    {
        CortexForkLandingOutcomeReceipt document = this;
        byte[] bytes = RonSerializer.SerializeToUtf8(in document);
        string path = Path.Combine(Path.GetFullPath(runDirectory), FileName);
        if (File.Exists(path))
        {
            if (!Cortex.FileContentEquals(path, bytes))
                throw new InvalidDataException("fork landing outcome conflicts with its prior image");
            return;
        }
        Run.Open(runDirectory).WriteAtomic(FileName, stream => stream.Write(bytes));
    }

    private string ComputeReceiptDigest()
        => ComputeSHA256(string.Join('|', schemaVersion, parentRunID, childRunID, role, state, callbackAttempted,
            callbackReturned, callbackExceptionType, callbackExceptionMessage, callbackWallMilliseconds, callbackRawTicks,
            terminalRunReceiptSHA256, terminalReceiptDigest, authorityBeforeSeedIntentSHA256, authorityBeforeSeedReceiptSHA256,
            authorityBeforeVerifierSHA256, authorityBeforeTerminalSHA256, authorityAfterSeedIntentSHA256,
            authorityAfterSeedReceiptSHA256, authorityAfterVerifierSHA256, authorityAfterTerminalSHA256, authorityChainExact));

    private static bool IsCanonicalDigest(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeFileSHA256(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing fork landing authority file: {path}");
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string ComputeSHA256(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string ComputeSHA256(string value)
        => ComputeSHA256(Encoding.UTF8.GetBytes(value));
}

internal readonly record struct CortexForkAuthorityChain(
    string SeedIntentSHA256,
    string SeedReceiptSHA256,
    string VerifierSHA256,
    string TerminalSHA256);

public readonly record struct CortexForkStepSpan(
    int SeedNextStep,
    int PlannedNextStep,
    int ActualNextStep)
{
    public int PlannedSteps => checked(PlannedNextStep - SeedNextStep);
    public int ActualSteps => checked(ActualNextStep - SeedNextStep);
    public bool ReachedPlannedNextStep => ActualNextStep == PlannedNextStep;
}

public readonly record struct CortexForkRunTiming(
    long SeedIOWallMilliseconds,
    long ExecutionWallMilliseconds,
    long TerminalVerifierWallMilliseconds,
    int SeedLoadChecks,
    int TerminalVerifierChecks,
    long RuntimeBindWallMilliseconds = 0,
    long SeedIORawTicks = 0,
    long ExecutionRawTicks = 0,
    long TerminalVerifierRawTicks = 0,
    long RuntimeBindRawTicks = 0,
    int CheckpointProofReuses = 0,
    int CheckpointProofMisses = 0)
{
    public long TotalWallMilliseconds => checked(
        SeedIOWallMilliseconds + ExecutionWallMilliseconds + TerminalVerifierWallMilliseconds + RuntimeBindWallMilliseconds);
    public long TotalRawTicks => checked(SeedIORawTicks + ExecutionRawTicks + TerminalVerifierRawTicks + RuntimeBindRawTicks);
}

public readonly record struct CortexForkSeedRelation(
    int Rung,
    CortexForkSeedRelations Kind,
    CortexForkDigests ExpectedLeftSeed,
    CortexForkDigests ActualLeftSeed,
    CortexForkDigests ExpectedRightSeed,
    CortexForkDigests ActualRightSeed,
    bool? InitialCrossArmMatched,
    string AncestorSeedDigest = "",
    string PreparedLeftSeedDigest = "",
    string PreparedRightSeedDigest = "",
    CortexForkPreparationRoles LeftPreparationRole = CortexForkPreparationRoles.Unknown,
    CortexForkPreparationRoles RightPreparationRole = CortexForkPreparationRoles.Unknown)
{
    public bool LeftContinuityExact => DigestsEqual(ExpectedLeftSeed, ActualLeftSeed);
    public bool RightContinuityExact => DigestsEqual(ExpectedRightSeed, ActualRightSeed);
    public bool Exact => LeftContinuityExact
        && RightContinuityExact
        && (Kind != CortexForkSeedRelations.InitialCrossArm || InitialCrossArmMatched == true)
        && (Kind != CortexForkSeedRelations.PreparedFromSharedAncestor
            || IsDigest(AncestorSeedDigest)
            && IsDigest(PreparedLeftSeedDigest)
            && IsDigest(PreparedRightSeedDigest)
            && LeftPreparationRole != CortexForkPreparationRoles.Unknown
            && RightPreparationRole != CortexForkPreparationRoles.Unknown);

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool DigestsEqual(in CortexForkDigests left, in CortexForkDigests right)
        => string.Equals(left.CheckpointSHA256, right.CheckpointSHA256, StringComparison.Ordinal)
           && string.Equals(left.TapeSpanlogSHA256, right.TapeSpanlogSHA256, StringComparison.Ordinal)
           && string.Equals(left.CurveSHA256, right.CurveSHA256, StringComparison.Ordinal)
           && string.Equals(left.ExcursionsSHA256, right.ExcursionsSHA256, StringComparison.Ordinal);
}

public readonly record struct CortexForkPairTiming(
    long LeftTotalWallMilliseconds,
    long RightTotalWallMilliseconds,
    long ParallelWallMilliseconds)
{
    public long SerialWallMilliseconds => checked(LeftTotalWallMilliseconds + RightTotalWallMilliseconds);
    public bool ParallelWallReduced => ParallelWallMilliseconds < SerialWallMilliseconds;
}

public readonly record struct CortexForkRegressionReceipt(
    bool InitialCrossArmMatched,
    bool CrossArmInequalityExpected,
    bool LeftContinuityExact,
    bool RightContinuityExact,
    bool LeftTerminalCheckpointExact,
    bool RightTerminalCheckpointExact)
{
    public bool Passed => InitialCrossArmMatched
        && CrossArmInequalityExpected
        && LeftContinuityExact
        && RightContinuityExact
        && LeftTerminalCheckpointExact
        && RightTerminalCheckpointExact;
}

public readonly record struct CortexForkNSeedRelation(
    int Rung,
    CortexForkSeedRelations Kind,
    string AncestorSeedDigest,
    IReadOnlyList<string> PreparedSeedDigests,
    IReadOnlyList<CortexForkPreparationRoles> PreparationRoles,
    IReadOnlyList<string>? AncestorSeedDigests = null,
    IReadOnlyList<CortexForkPreparationRoles>? ExpectedPreparationRoles = null)
{
    public bool Exact
    {
        get
        {
            string ancestorSeedDigest = AncestorSeedDigest;
            IReadOnlyList<string>? ancestorSeedDigests = AncestorSeedDigests;
            IReadOnlyList<string> preparedSeedDigests = PreparedSeedDigests;
            IReadOnlyList<CortexForkPreparationRoles> preparationRoles = PreparationRoles;
            IReadOnlyList<CortexForkPreparationRoles>? expectedPreparationRoles = ExpectedPreparationRoles;
            return Kind is CortexForkSeedRelations.PreparedFromSharedAncestor or CortexForkSeedRelations.PerArmContinuation
                && IsDigest(ancestorSeedDigest)
                && ancestorSeedDigests is not null
                && ancestorSeedDigests.Count == preparedSeedDigests.Count
                && ancestorSeedDigests.All(digest => digest == ancestorSeedDigest)
                && preparedSeedDigests.Count >= 3
                && preparedSeedDigests.All(static digest => digest.Length == 64 && digest.All(Uri.IsHexDigit))
                && preparationRoles.Count == preparedSeedDigests.Count
                && preparationRoles.All(static role => role != CortexForkPreparationRoles.Unknown)
                && expectedPreparationRoles is not null
                && expectedPreparationRoles.Count == preparationRoles.Count
                && preparationRoles.SequenceEqual(expectedPreparationRoles);
        }
    }

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed partial class Cortex
{
    internal CortexSeedSidecarSet CopyPolicyJournals()
    {
        FlushPolicyJournalBuffer();
        return _runtimeRun is null ? CortexSeedSidecarSet.Empty : CortexSeedSidecarSet.CaptureFrom(_runtimeRun.Dir);
    }

    /// Byte-exact content check without reading the whole file into RAM — the receipt-conflict guards compare a
    /// candidate serialization against the landed image by streaming, not by materializing both sides.
    internal static bool FileContentEquals(string path, ReadOnlySpan<byte> expected)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length != expected.Length) return false;
        byte[] scratch = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            int offset = 0, read;
            while ((read = stream.Read(scratch, 0, scratch.Length)) > 0)
            {
                if (!scratch.AsSpan(0, read).SequenceEqual(expected.Slice(offset, read))) return false;
                offset += read;
            }
            return offset == expected.Length;
        }
        finally { ArrayPool<byte>.Shared.Return(scratch); }
    }

    internal void InstallForkRegressionProbe(bool emit)
    {
        CortexTool tool = new ForkRegressionTool();
        _tools.Add(tool);
        _actionPolicies.Insert(0, new ForkRegressionActionPolicy(tool, emit));
    }

    private sealed class ForkRegressionActionPolicy(CortexTool tool, bool emit) : CortexActionPolicy
    {
        public override bool TryChooseAction(Cortex cortex, List<CortexActionArgument> arguments, out CortexAction action)
        {
            action = new CortexAction(tool, "fork-regression");
            return emit;
        }
    }

    private sealed class ForkRegressionTool : CortexTool
    {
        public override string Name => "fork-regression";

        public override bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action)
        {
            action = new CortexAction(this, line);
            return string.Equals(line, Name, StringComparison.Ordinal);
        }

        public override CortexObservation Act(Cortex cortex, CortexAction action,
            List<CortexActionArgument> arguments, List<CortexObservationField> fields)
            => new("fork-regression", false);
    }
}

/// One fork-seed sidecar carried as a REFERENCE, not materialized bytes. A reference form (SourcePath) is streamed
/// file→file at write time — the multi-gigabyte grammar-revision bins never enter RAM; a splice form (Bytes) holds
/// synthetic content a fixture builds in memory (a verification receipt). Names are unique within a set.
internal readonly struct CortexSeedSidecar
{
    private CortexSeedSidecar(string name, string? sourcePath, byte[]? bytes)
    {
        Name = name;
        SourcePath = sourcePath;
        Bytes = bytes;
    }

    public string Name { get; }
    public string? SourcePath { get; }
    public byte[]? Bytes { get; }

    public static CortexSeedSidecar Reference(string name, string sourcePath) => new(name, sourcePath, null);
    public static CortexSeedSidecar Splice(string name, byte[] bytes) => new(name, null, bytes);
}

/// A fork seed's sidecar payload as references. Capturing a run directory is O(1) — it records paths and reads
/// NOTHING (the 2.44 GB of grammar bins stay on disk until WriteInto streams them). This is the fix for the 13.5 GB
/// fork spike: the old model read every journal + journal.log + every grammar-revision bin into a
/// Dictionary<string,byte[]> and deep-copied it per child consumer (12 children × concurrent copies). The set is
/// immutable — With/WithReference return a new set — so a per-child copy costs a small reference array, not a
/// gigabyte clone. Custody is unchanged: WriteInto streams each source once (the same bytes on disk), so digests
/// and receipts stay byte-identical; the bytes are verified during the stream instead of held in memory.
internal sealed class CortexSeedSidecarSet
{
    private readonly List<CortexSeedSidecar> _entries;

    private CortexSeedSidecarSet(List<CortexSeedSidecar> entries) => _entries = entries;

    public static CortexSeedSidecarSet Empty { get; } = new([]);

    /// Reference every sidecar living in a run directory, in the historical dictionary-fill order (policy journals,
    /// paired schedule, journal.log, then grammar revisions). journal.log carries the journal's shed prefix — a seed
    /// without it cannot splice on resume — so it always rides when present.
    public static CortexSeedSidecarSet CaptureFrom(string runDirectory)
    {
        string directory = Path.GetFullPath(runDirectory);
        List<CortexSeedSidecar> entries = [];
        foreach (string name in Cortex.PolicyJournalFileNames)
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) entries.Add(CortexSeedSidecar.Reference(name, path));
        }
        string schedule = Path.Combine(directory, EmlPairedFuelSchedule.SidecarFile);
        if (File.Exists(schedule)) entries.Add(CortexSeedSidecar.Reference(EmlPairedFuelSchedule.SidecarFile, schedule));
        string journalLog = Path.Combine(directory, "journal.log");
        if (File.Exists(journalLog)) entries.Add(CortexSeedSidecar.Reference("journal.log", journalLog));
        foreach (string path in Directory.EnumerateFiles(directory, "grammar-revision-*.bin"))
            entries.Add(CortexSeedSidecar.Reference(Path.GetFileName(path), path));
        return new(entries);
    }

    private CortexSeedSidecarSet Replace(string name, CortexSeedSidecar entry)
    {
        List<CortexSeedSidecar> next = new(_entries.Count + 1);
        bool replaced = false;
        foreach (CortexSeedSidecar existing in _entries)
        {
            if (string.Equals(existing.Name, name, StringComparison.Ordinal)) { next.Add(entry); replaced = true; }
            else next.Add(existing);
        }
        if (!replaced) next.Add(entry);
        return new(next);
    }

    public CortexSeedSidecarSet With(string name, byte[] bytes) => Replace(name, CortexSeedSidecar.Splice(name, bytes));

    public CortexSeedSidecarSet WithReference(string name, string sourcePath) => Replace(name, CortexSeedSidecar.Reference(name, sourcePath));

    /// Land every sidecar into the destination run through its atomic writer — splice bytes written directly, file
    /// references streamed source→destination through a bounded buffer (Stream.CopyTo), so the payload never fully
    /// materializes in RAM. Same durable flush + rename per file as the byte-snapshot path it replaces.
    public void WriteInto(Run destination)
    {
        foreach (CortexSeedSidecar entry in _entries)
        {
            if (entry.Bytes is byte[] bytes)
                destination.WriteAtomic(entry.Name, stream => stream.Write(bytes));
            else
            {
                string sourcePath = entry.SourcePath!;
                destination.WriteAtomic(entry.Name, stream =>
                {
                    using FileStream source = File.OpenRead(sourcePath);
                    source.CopyTo(stream);
                });
            }
        }
    }
}

public sealed class CortexForkSeed
{
    private readonly byte[] _checkpoint;
    private readonly byte[] _tapeSpanlog;
    private readonly byte[] _curve;
    private readonly byte[] _excursions;
    private readonly CortexSeedSidecarSet _policyJournals;

    private CortexForkSeed(int nextStep, byte[] checkpoint, byte[] tapeSpanlog, byte[] curve,
        byte[] excursions, long excursionCursor, CortexSeedSidecarSet policyJournals, string persistedConfigDigest)
    {
        NextStep = nextStep;
        _checkpoint = checkpoint;
        _tapeSpanlog = tapeSpanlog;
        _curve = curve;
        _excursions = excursions;
        ExcursionCursor = excursionCursor;
        _policyJournals = policyJournals;
        Digests = new CortexForkDigests(
            Checkpoint.LogicalStateSHA256(checkpoint),
            ComputeSHA256(tapeSpanlog),
            ComputeSHA256(curve),
            excursions.Length == 0 ? "" : ComputeSHA256(excursions));
        PersistedConfigDigest = persistedConfigDigest;
        CheckpointProof = string.IsNullOrWhiteSpace(persistedConfigDigest)
            ? default
            : Checkpoint.CreateImageProof(checkpoint, persistedConfigDigest, nextStep, saveLoadSaveExact: false);
        ColdSeedDigest = CortexForkRunner.ComputeSeedIdentity(Digests, persistedConfigDigest);
    }

    public int NextStep { get; }
    public int CheckpointLength => _checkpoint.Length;
    public int TapeSpanlogLength => _tapeSpanlog.Length;
    public int CurveLength => _curve.Length;
    public int ExcursionsLength => _excursions.Length;
    public long ExcursionCursor { get; }
    public CortexForkDigests Digests { get; }
    public string PersistedConfigDigest { get; }
    public string ColdSeedDigest { get; }
    public CheckpointRoundTripProof CheckpointProof { get; }

    public byte[] CopyCheckpoint() => [.. _checkpoint];
    public byte[] CopyTapeSpanlog() => [.. _tapeSpanlog];
    public byte[] CopyCurve() => [.. _curve];
    public byte[] CopyExcursions() => [.. _excursions];
    // The sidecar set is immutable, so a "copy" is just the shared reference — the per-child gigabyte dictionary
    // clone that drove the fork memory spike is gone.
    internal CortexSeedSidecarSet CopyPolicyJournals() => _policyJournals;

    /// Prove the seed's own in-memory image: decode-validates config digest and
    /// next-step against the image bytes the seed will write, byte-identical to
    /// reading the proof back from a freshly written (rail-less) run dir.
    internal CheckpointRoundTripProof ProveCheckpointImage()
        => Checkpoint.ReadImageProof(_checkpoint, Checkpoint.PhysicalSHA256(_checkpoint),
            CheckpointDelta.ChainSHA256ForImage(_checkpoint), PersistedConfigDigest, NextStep, saveLoadSaveExact: false);

    internal static CortexForkSeed Materialize(int nextStep, byte[] checkpoint, byte[] tapeSpanlog, byte[] curve,
        string persistedConfigDigest = "", CortexSeedSidecarSet? policyJournals = null,
        byte[]? excursions = null, long excursionCursor = 0)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(tapeSpanlog);
        ArgumentNullException.ThrowIfNull(curve);
        excursions ??= Array.Empty<byte>();
        if (excursionCursor < 0) throw new ArgumentOutOfRangeException(nameof(excursionCursor));
        // Materialize consumes the three freshly-produced buffers. CopyCheckpoint/CopyTapeSpanlog/CopyCurve remain
        // the explicit defensive boundary for readers that need an independent image; the seed itself owns these
        // arrays until its run directories have been written. The sidecar set is immutable references — shared,
        // never cloned.
        return new(nextStep, checkpoint, tapeSpanlog, curve, excursions, excursionCursor,
            policyJournals ?? CortexSeedSidecarSet.Empty, persistedConfigDigest);
    }

    internal static CortexForkSeed MaterializeRun(string runDirectory, int expectedNextStep)
    {
        string directory = Path.GetFullPath(runDirectory);
        byte[] checkpoint = Checkpoint.LoadEffectiveImage(directory);
        int nextStep = Checkpoint.PeekNextStep(checkpoint);
        if (nextStep != expectedNextStep)
            throw new InvalidDataException($"fork seed step mismatch: expected {expectedNextStep}, got {nextStep}");
        string configDigest = Cortex.PersistedConfigDigest(Checkpoint.PeekConfig(checkpoint));
        (byte[] excursions, long excursionCursor) = ReadExcursions(directory);
        return Materialize(nextStep, checkpoint,
            File.ReadAllBytes(Path.Combine(directory, "tape.spanlog")),
            File.ReadAllBytes(Path.Combine(directory, "curve.tsv")),
            configDigest, CortexSeedSidecarSet.CaptureFrom(directory), excursions, excursionCursor);
    }

    public void WriteRunDirectory(string runDirectory, CortexForkMaterializationContract? materializationContract = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        string fullDirectory = Path.GetFullPath(runDirectory);
        if (Directory.Exists(fullDirectory))
        {
            string[] entries = Directory.EnumerateFileSystemEntries(fullDirectory).ToArray();
            if (entries.Length != 1 || materializationContract is null
                || !string.Equals(Path.GetFileName(entries[0]), CortexForkMaterializationContract.MarkerFileName, StringComparison.Ordinal))
                throw new IOException($"fork run dir already exists: {fullDirectory} — refusing to resume or clobber an existing arc");
            materializationContract.Value.Validate(fullDirectory);
            if (!string.Equals(File.ReadAllText(entries[0]), materializationContract.Value.Encode(), StringComparison.Ordinal))
                throw new InvalidDataException("fork materialization marker disagrees with its typed contract");
        }
        else
            Directory.CreateDirectory(fullDirectory);
        Run destination = Run.Open(fullDirectory);
        destination.WriteAtomic(Checkpoint.FileName, stream => stream.Write(_checkpoint));
        destination.WriteAtomic("tape.spanlog", stream => stream.Write(_tapeSpanlog));
        destination.WriteAtomic("curve.tsv", stream => stream.Write(_curve));
        if (_excursions.Length > 0)
            destination.WriteAtomic("excursions.txt", stream => stream.Write(_excursions));
        _policyJournals.WriteInto(destination);
    }

    private static string ComputeSHA256(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static (byte[] Bytes, long Cursor) ReadExcursions(string directory)
    {
        string path = Path.Combine(directory, "excursions.txt");
        if (!File.Exists(path))
            return (Array.Empty<byte>(), 0);
        byte[] bytes = File.ReadAllBytes(path);
        long cursor = CountRows(bytes);
        return (bytes, cursor);
    }

    internal static long CountRows(byte[] bytes)
    {
        int newline = Array.IndexOf(bytes, (byte)'\n');
        if (newline < 0 || !Encoding.UTF8.GetString(bytes, 0, newline).TrimEnd('\r').Equals("step\ttoken", StringComparison.Ordinal))
            throw new InvalidDataException("excursions.txt header is malformed");
        long rows = 0;
        for (int i = newline + 1; i < bytes.Length; i++)
            if (bytes[i] == (byte)'\n') rows++;
        return rows;
    }
}

public enum CortexForkCompletionModes
{
    ExactAbsoluteStep,
    RuntimeStop,
}

public readonly record struct CortexForkAnytimeIdentity(
    string ChainID,
    string ArmID,
    int Rung,
    string ParentPointID)
{
    public CortexForkAnytimeIdentity Validate()
    {
        if (string.IsNullOrWhiteSpace(ChainID) || string.IsNullOrWhiteSpace(ArmID) || Rung < 0)
            throw new InvalidDataException("fork anytime identity requires chain, arm, and nonnegative rung");
        return this;
    }
}

internal readonly struct CortexForkExecutionReceipt
{
    public CortexForkExecutionReceipt(
        int exitCode,
        bool runtimeStopRequested,
        long runtimeBindWallMilliseconds = 0,
        long executionWallMilliseconds = 0,
        long runtimeBindRawTicks = 0,
        long executionRawTicks = 0)
    {
        ExitCode = exitCode;
        RuntimeStopRequested = runtimeStopRequested;
        RuntimeBindWallMilliseconds = runtimeBindWallMilliseconds;
        ExecutionWallMilliseconds = executionWallMilliseconds;
        RuntimeBindRawTicks = runtimeBindRawTicks;
        ExecutionRawTicks = executionRawTicks;
    }

    public int ExitCode { get; }
    public bool RuntimeStopRequested { get; }
    public long RuntimeBindWallMilliseconds { get; }
    public long ExecutionWallMilliseconds { get; }
    public long RuntimeBindRawTicks { get; }
    public long ExecutionRawTicks { get; }
}

public readonly record struct CortexForkMaterializationContract(
    string ParentRunID,
    string AttemptID,
    string ChildRunID,
    string ColdSeedDigest)
{
    public const string MarkerFileName = "deep-rematch.child.materialized";

    public string Encode() => $"parent={ParentRunID}\nattempt={AttemptID}\nchild={ChildRunID}\ncold={ColdSeedDigest}\n";

    public void Validate(string runDirectory)
    {
        if (string.IsNullOrWhiteSpace(ParentRunID) || string.IsNullOrWhiteSpace(AttemptID)
            || string.IsNullOrWhiteSpace(ChildRunID) || ColdSeedDigest is null || ColdSeedDigest.Length != 64
            || !ColdSeedDigest.All(Uri.IsHexDigit)
            || ParentRunID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            || AttemptID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            || ChildRunID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            || Path.GetFileName(ChildRunID) != ChildRunID
            || Path.GetFileName(Path.GetFullPath(runDirectory)) != ChildRunID)
            throw new InvalidDataException("fork materialization contract is not bound to its exact child");
    }
}

public sealed class CortexForkArm<TOutcome>
{
    public CortexForkArm(
        string runDirectory,
        Func<Cortex> createCortex,
        Func<Cortex, TOutcome> readCompletion,
        Action<Cortex>? interveneAfterLoad = null,
        CortexForkCompletionModes completionMode = CortexForkCompletionModes.ExactAbsoluteStep,
        Func<TOutcome, bool>? isCompletionSatisfied = null,
        CortexForkAnytimeIdentity? anytimeIdentity = null,
        CortexForkRailRoles railRole = CortexForkRailRoles.Unknown,
        Action<Cortex, CortexExecutionWindow>? afterRuntimeBind = null,
        string parentRunID = "",
        Action<Cortex, int>? afterCompletedStep = null,
        Action<Cortex, int>? afterCompletedStepEveryStep = null,
        Action<Cortex, CortexForkSeedLoadReceipt, CortexForkDigests>? afterRunLanded = null,
        Action<Cortex, int>? beforeCompletedStep = null,
        CortexForkMaterializationContract? materializationContract = null,
        Action<Cortex, CortexForkSeedLoadReceipt, CortexForkDigests, TOutcome>? persistCompletionBeforeLanding = null,
        CortexForkPreparationRoles preparationRole = CortexForkPreparationRoles.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(createCortex);
        ArgumentNullException.ThrowIfNull(readCompletion);
        if (completionMode == CortexForkCompletionModes.RuntimeStop && isCompletionSatisfied is null)
            throw new ArgumentNullException(nameof(isCompletionSatisfied), "runtime-stop fork arms require a completion predicate");
        if (completionMode == CortexForkCompletionModes.ExactAbsoluteStep && isCompletionSatisfied is not null)
            throw new ArgumentException("exact-step fork arms do not accept a completion predicate", nameof(isCompletionSatisfied));
        CortexForkPreparationRoles railPreparationRole = ResolvePreparationRole(railRole);
        if (preparationRole != CortexForkPreparationRoles.Unknown
            && railPreparationRole != CortexForkPreparationRoles.Unknown
            && preparationRole != railPreparationRole)
            throw new ArgumentException("fork preparation role must match its rail role", nameof(preparationRole));

        RunDirectory = runDirectory;
        CreateCortex = createCortex;
        ReadCompletion = readCompletion;
        InterveneAfterLoad = interveneAfterLoad;
        CompletionMode = completionMode;
        IsCompletionSatisfied = isCompletionSatisfied;
        AnytimeIdentity = anytimeIdentity;
        RailRole = railRole;
        AfterRuntimeBind = afterRuntimeBind;
        AfterCompletedStep = afterCompletedStep;
        AfterCompletedStepEveryStep = afterCompletedStepEveryStep;
        AfterRunLanded = afterRunLanded;
        BeforeCompletedStep = beforeCompletedStep;
        ParentRunID = parentRunID;
        MaterializationContract = materializationContract;
        PersistCompletionBeforeLanding = persistCompletionBeforeLanding;
        PreparationRole = preparationRole == CortexForkPreparationRoles.Unknown
            ? railPreparationRole : preparationRole;
    }

    public string RunDirectory { get; }
    public Func<Cortex> CreateCortex { get; }
    public Func<Cortex, TOutcome> ReadCompletion { get; }
    public Action<Cortex>? InterveneAfterLoad { get; }
    public CortexForkCompletionModes CompletionMode { get; }
    public Func<TOutcome, bool>? IsCompletionSatisfied { get; }
    public CortexForkAnytimeIdentity? AnytimeIdentity { get; }
    public CortexForkRailRoles RailRole { get; }
    public Action<Cortex, CortexExecutionWindow>? AfterRuntimeBind { get; }
    /// A one-shot lifecycle hook invoked after the first completed step in this
    /// child, before the next step begins.
    public Action<Cortex, int>? AfterCompletedStep { get; }
    public Action<Cortex, int>? AfterCompletedStepEveryStep { get; }
    /// Invoked after terminal verification, ReadCompletion, and the durable terminal receipt. The
    /// PersistCompletionBeforeLanding hook runs first; this caller-owned hook runs immediately before the
    /// generic landing-outcome receipt is written.
    public Action<Cortex, CortexForkSeedLoadReceipt, CortexForkDigests>? AfterRunLanded { get; }
    /// Invoked after a step's decision/verifier work, before the runtime settles and checkpoints it.
    public Action<Cortex, int>? BeforeCompletedStep { get; }
    public string ParentRunID { get; }
    public CortexForkMaterializationContract? MaterializationContract { get; }
    /// Invoked after ReadCompletion has produced the final typed outcome, before the caller landing hook and
    /// generic landing receipt. The hook owns durable post-completion artifacts; its failure is persisted as
    /// CallbackFailed while the terminal authority chain remains immutable, then rethrown.
    public Action<Cortex, CortexForkSeedLoadReceipt, CortexForkDigests, TOutcome>? PersistCompletionBeforeLanding { get; }
    public CortexForkPreparationRoles PreparationRole { get; }

    internal CortexForkAnytimeIdentity ResolveAnytimeIdentity(int rung, string parentPointID = "")
    {
        CortexForkAnytimeIdentity identity = AnytimeIdentity ?? CreateDefaultAnytimeIdentity(RunDirectory, rung);
        return identity with { Rung = rung, ParentPointID = string.IsNullOrWhiteSpace(identity.ParentPointID) ? parentPointID : identity.ParentPointID };
    }

    private static CortexForkAnytimeIdentity CreateDefaultAnytimeIdentity(string runDirectory, int rung)
    {
        string full = Path.GetFullPath(runDirectory);
        string? parent = Path.GetDirectoryName(full);
        string? rungRoot = parent is null ? null : Path.GetDirectoryName(parent);
        string chainSource = rungRoot ?? parent ?? full;
        string chain = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(chainSource)));
        return new(chain, Path.GetFileName(full), rung, "");
    }

    private static CortexForkPreparationRoles ResolvePreparationRole(CortexForkRailRoles role)
        => role switch
        {
            CortexForkRailRoles.Baseline => CortexForkPreparationRoles.Baseline,
            CortexForkRailRoles.Candidate => CortexForkPreparationRoles.Candidate,
            CortexForkRailRoles.ForcedNull => CortexForkPreparationRoles.ForcedNull,
            CortexForkRailRoles.ReflexFrozen => CortexForkPreparationRoles.ReflexFrozen,
            _ => CortexForkPreparationRoles.Unknown,
        };
}

public sealed class CortexForkRunReceipt<TOutcome>
{
    public CortexForkRunReceipt(string runDirectory, CortexForkDigests seedDigests, CortexForkDigests finalDigests,
        CortexForkSeedLoadReceipt seedLoad, CortexForkStepSpan stepSpan, CortexForkRunTiming timing,
        int exitCode, bool terminalCheckpointExact, TOutcome outcome, string anytimeCurveDigest = "")
    {
        RunDirectory = runDirectory;
        SeedDigests = seedDigests;
        FinalDigests = finalDigests;
        SeedLoad = seedLoad;
        StepSpan = stepSpan;
        Timing = timing;
        ExitCode = exitCode;
        TerminalCheckpointExact = terminalCheckpointExact;
        Outcome = outcome;
        AnytimeCurveDigest = anytimeCurveDigest;
    }

    public string RunDirectory { get; }
    public CortexForkDigests SeedDigests { get; }
    public CortexForkDigests FinalDigests { get; }
    public CortexForkSeedLoadReceipt SeedLoad { get; }
    public CortexForkStepSpan StepSpan { get; }
    public CortexForkRunTiming Timing { get; }
    public int ExitCode { get; }
    public bool TerminalCheckpointExact { get; }
    public long WallMilliseconds => Timing.TotalWallMilliseconds;
    public TOutcome Outcome { get; }
    public string AnytimeCurveDigest { get; }
}

public sealed class CortexMatchedForkReceipt<TOutcome>
{
    public CortexMatchedForkReceipt(
        CortexForkRunReceipt<TOutcome> left,
        CortexForkRunReceipt<TOutcome> right,
        CortexForkSeedRelation seedRelation,
        CortexForkPairTiming timing)
    {
        Left = left;
        Right = right;
        SeedRelation = seedRelation;
        Timing = timing;
    }

    public CortexForkRunReceipt<TOutcome> Left { get; }
    public CortexForkRunReceipt<TOutcome> Right { get; }
    public CortexForkSeedRelation SeedRelation { get; }
    public CortexForkPairTiming Timing { get; }
    public bool IsExact => SeedRelation.Exact && Left.TerminalCheckpointExact && Right.TerminalCheckpointExact;
}

internal sealed class CortexMatchedForkNReceipt<TOutcome>
{
    internal CortexMatchedForkNReceipt(
        IReadOnlyList<CortexForkRunReceipt<TOutcome>> arms,
        CortexForkNSeedRelation seedRelation = default)
    {
        Arms = arms;
        SeedRelation = seedRelation;
        bool relationAbsent = seedRelation.Kind == default
            && string.IsNullOrWhiteSpace(seedRelation.AncestorSeedDigest)
            && seedRelation.PreparedSeedDigests is null;
        IsExact = arms.All(static arm => arm.SeedLoad.Exact && arm.TerminalCheckpointExact && arm.ExitCode == 0)
            && (relationAbsent || seedRelation.Exact);
    }

    internal IReadOnlyList<CortexForkRunReceipt<TOutcome>> Arms { get; }
    internal CortexForkNSeedRelation SeedRelation { get; }
    internal bool IsExact { get; }
}

public static class CortexForkRunner
{
    internal static void InstallRegressionProbe(Cortex cortex, bool emit)
        => cortex.InstallForkRegressionProbe(emit);

    internal static CortexForkRegressionReceipt RunChainedRungRegression(
        Cortex spawningCortex,
        CortexRunConfig config,
        CortexForkSeed seed,
        string regressionRoot)
    {
        ArgumentNullException.ThrowIfNull(spawningCortex);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(regressionRoot);
        if (!VerifyCompositeRailReceiptContract())
            throw new InvalidDataException("composite rail receipt fixture violated parent/child seed contract");
        string root = Path.GetFullPath(regressionRoot);
        Directory.CreateDirectory(root);
        Run parentRun = Run.Open(root);
        spawningCortex.AttachForkParentRun(parentRun);
        string parentRunID = Path.GetFileName(parentRun.Dir);
        const string regressionAttemptID = "chained-rung-regression";
        CortexForkArm<int> CreateArm(
            Run childRun,
            CortexForkRailRoles role,
            CortexForkMaterializationContract materializationContract,
            bool emit,
            int rung)
            => new(childRun.Dir, () =>
            {
                Cortex cortex = Cortex.CreateCheckpointRuntime(config);
                cortex.InstallForkRegressionProbe(emit);
                return cortex;
            }, static _ => 0,
            anytimeIdentity: new CortexForkAnytimeIdentity(
                "chained-rung-regression", Path.GetFileName(childRun.Dir), rung, ""),
            railRole: role,
            parentRunID: parentRunID,
            materializationContract: materializationContract);

        (Run rung0LeftRun, CortexForkMaterializationContract rung0LeftContract) =
            parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Calibration, regressionAttemptID, seed.ColdSeedDigest);
        (Run rung1LeftRun, CortexForkMaterializationContract rung1LeftContract) =
            parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Calibration, regressionAttemptID, seed.ColdSeedDigest);
        (Run rung0RightRun, CortexForkMaterializationContract rung0RightContract) =
            parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Evaluation, regressionAttemptID, seed.ColdSeedDigest);
        (Run rung1RightRun, CortexForkMaterializationContract rung1RightContract) =
            parentRun.CreateMaterializedChildRun(CortexForkRailRoles.Evaluation, regressionAttemptID, seed.ColdSeedDigest);
        CortexForkArm<int>[] left =
        [
            CreateArm(rung0LeftRun, CortexForkRailRoles.Calibration, rung0LeftContract, emit: false, rung: 0),
            CreateArm(rung1LeftRun, CortexForkRailRoles.Calibration, rung1LeftContract, emit: false, rung: 1),
        ];
        CortexForkArm<int>[] right =
        [
            CreateArm(rung0RightRun, CortexForkRailRoles.Evaluation, rung0RightContract, emit: true, rung: 0),
            CreateArm(rung1RightRun, CortexForkRailRoles.Evaluation, rung1RightContract, emit: true, rung: 1),
        ];
        List<CortexMatchedForkReceipt<int>> rungs = RunMatchedForkLadder(
            spawningCortex, seed, left, right, [checked(seed.NextStep + 1), checked(seed.NextStep + 2)]);
        CortexMatchedForkReceipt<int> rung0 = rungs[0];
        CortexMatchedForkReceipt<int> rung1 = rungs[1];
        bool crossArmInequalityExpected = !DigestsEqual(rung0.Left.FinalDigests, rung0.Right.FinalDigests);
        CortexForkRegressionReceipt receipt = new(
            rung0.SeedRelation.InitialCrossArmMatched == true,
            crossArmInequalityExpected,
            rung1.SeedRelation.LeftContinuityExact,
            rung1.SeedRelation.RightContinuityExact,
            rung1.Left.TerminalCheckpointExact,
            rung1.Right.TerminalCheckpointExact);
        if (!receipt.Passed)
            throw new InvalidDataException("chained fork ladder regression rejected a valid per-arm continuation");
        return receipt;
    }

    /// Contract regression for the false-red class: rung 0 starts from one common seed but deliberately ends with
    /// distinct per-arm checkpoint provenance; rung 1 loads each arm's own final checkpoint. The receipt must accept
    /// both continuities while leaving cross-arm inequality visible as an expected property of a chained ladder.
    public static CortexForkRegressionReceipt VerifyChainedRungReceiptContract()
    {
        CortexForkDigests common = CreateRegressionDigests("common");
        CortexForkDigests leftFinal = CreateRegressionDigests("left-final");
        CortexForkDigests rightFinal = CreateRegressionDigests("right-final");
        CortexForkSeedLoadReceipt commonLeftLoad = new(common, common, 0);
        CortexForkSeedLoadReceipt commonRightLoad = new(common, common, 0);
        CortexForkSeedLoadReceipt leftContinuationLoad = new(leftFinal, leftFinal, 0);
        CortexForkSeedLoadReceipt rightContinuationLoad = new(rightFinal, rightFinal, 0);
        CortexForkRunReceipt<int> leftRung0 = CreateRegressionRun(common, leftFinal, commonLeftLoad, 0, 1);
        CortexForkRunReceipt<int> rightRung0 = CreateRegressionRun(common, rightFinal, commonRightLoad, 0, 1);
        CortexForkRunReceipt<int> leftRung1 = CreateRegressionRun(leftFinal, CreateRegressionDigests("left-terminal"), leftContinuationLoad, 1, 2);
        CortexForkRunReceipt<int> rightRung1 = CreateRegressionRun(rightFinal, CreateRegressionDigests("right-terminal"), rightContinuationLoad, 1, 2);
        CortexForkSeedRelation rung0 = CreateSeedRelation(0, CortexForkSeedRelations.InitialCrossArm,
            common, leftRung0.SeedLoad.LoadedDigests, common, rightRung0.SeedLoad.LoadedDigests);
        CortexForkSeedRelation rung1 = CreateSeedRelation(1, CortexForkSeedRelations.PerArmContinuation,
            leftRung0.FinalDigests, leftRung1.SeedLoad.LoadedDigests,
            rightRung0.FinalDigests, rightRung1.SeedLoad.LoadedDigests);
        if (rung0.InitialCrossArmMatched != true || !rung0.Exact
            || DigestsEqual(leftRung0.FinalDigests, rightRung0.FinalDigests)
            || rung1.InitialCrossArmMatched.HasValue || !rung1.Exact
            || !leftRung1.TerminalCheckpointExact || !rightRung1.TerminalCheckpointExact)
            throw new InvalidDataException("chained fork receipt regression violated per-arm continuity contract");
        return new CortexForkRegressionReceipt(
            rung0.InitialCrossArmMatched == true,
            !DigestsEqual(leftRung0.FinalDigests, rightRung0.FinalDigests),
            rung1.LeftContinuityExact,
            rung1.RightContinuityExact,
            leftRung1.TerminalCheckpointExact,
            rightRung1.TerminalCheckpointExact);
    }

    internal static bool VerifyDeltaTransferFixture(TextWriter output)
    {
        string root = Path.Combine(Run.HomePath(".fork-delta-transfer"), Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string destination = Path.Combine(root, "destination");
        try
        {
            Directory.CreateDirectory(source);
            byte[] baseImage = [.. Checkpoint.CurrentMagic, 0x01, 0x01];
            byte[] effectiveImage = [.. Checkpoint.CurrentMagic, 0x01, 0x03];
            File.WriteAllBytes(Path.Combine(source, Checkpoint.FileName), baseImage);
            File.WriteAllBytes(Path.Combine(source, Checkpoint.DeltaFileName),
                CheckpointDelta.EncodeFixtureDeltaForFork(baseImage, [.. Checkpoint.CurrentMagic, 0x01, 0x02], effectiveImage));
            File.WriteAllBytes(Path.Combine(source, "tape.spanlog"), "tape"u8.ToArray());
            File.WriteAllBytes(Path.Combine(source, "curve.tsv"), "curve"u8.ToArray());

            CortexForkDigests expected = ReadRunDigests(source);
            CortexForkSeedLoadReceipt receipt = CopyRunState(source, destination, expected, validateCheckpointProof: false);
            bool logicalReplay = Checkpoint.LoadEffectiveImage(destination).AsSpan().SequenceEqual(effectiveImage);
            bool deltaCopied = File.Exists(Path.Combine(destination, Checkpoint.DeltaFileName));

            byte[] corrupt = File.ReadAllBytes(Path.Combine(destination, Checkpoint.DeltaFileName));
            corrupt[^1] ^= 0x01;
            File.WriteAllBytes(Path.Combine(destination, Checkpoint.DeltaFileName), corrupt);
            bool corruptionRejected;
            try
            {
                _ = ReadRunDigests(destination);
                corruptionRejected = false;
            }
            catch (InvalidDataException)
            {
                corruptionRejected = true;
            }

            bool passed = receipt.Exact && logicalReplay && deltaCopied && corruptionRejected;
            output.WriteLine($"  fork delta transfer · logical={(logicalReplay ? "effective" : "STALE")} delta={(deltaCopied ? "copied" : "LOST")} corruption={(corruptionRejected ? "rejected" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static bool VerifyCheckpointProofFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        const string image = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string physical = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string basePhysical = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        const string chain = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        const string config = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        CheckpointRoundTripProof proof = new(image, physical, basePhysical, chain, config, 1024, true);
        bool exact = proof.Matches(proof);
        bool boolRejected = !proof.Matches(proof with { SaveLoadSaveExact = false });
        bool imageRejected = !proof.Matches(proof with { EffectiveImageSHA256 = new string('f', 64) });
        bool baseRejected = !proof.Matches(proof with { BasePhysicalSHA256 = new string('f', 64) });
        bool chainRejected = !proof.Matches(proof with { PhysicalChainSHA256 = new string('f', 64) });
        bool configRejected = !proof.Matches(proof with { PersistedConfigDigest = new string('f', 64) });
        bool stepRejected = !proof.Matches(proof with { NextStep = 1025 });
        CortexForkSeedLoadReceipt receipt = new(
            new CortexForkDigests(image, physical, chain),
            new CortexForkDigests(image, physical, chain), 0,
            ParentRunID: "parent", ChildRunID: "calibration_0000", Role: CortexForkRailRoles.Calibration,
            ColdSeedDigest: new string('1', 64), PersistedConfigDigest: config,
            ExecutionWindow: new CortexExecutionWindow(1024, 1025), SourceSeedDigest: new string('2', 64),
            SourceRunID: "parent", SourceNextStep: 1024, SeedIORawTicks: 1,
            CheckpointProof: proof);
        bool reusedBindingChanges = receipt.BindingDigest != (receipt with { CheckpointProofReused = true }).BindingDigest;
        CortexForkSeedLoadRailReceipt rail = CortexForkSeedLoadRailReceipt.FromReceipt(receipt);
        bool schemaBound = rail.schemaVersion == CortexForkSeedLoadRailReceipt.CurrentSchemaVersion;
        CortexForkSeedLoadReceipt stagedReceipt = receipt with
        {
            AncestorSeedDigest = new string('6', 64),
            PreparedSeedDigest = new string('7', 64),
            PreparationRole = CortexForkPreparationRoles.Baseline,
        };
        CortexForkSeedLoadRailReceiptV2 legacy = CortexForkSeedLoadRailReceiptV2.FromCurrent(stagedReceipt);
        CortexForkSeedLoadRailReceipt migratedLegacy = legacy.ToCurrent();
        bool legacyBindingPreserved = legacy.bindingDigest == (stagedReceipt with
        {
            AncestorSeedDigest = "",
            PreparedSeedDigest = "",
            PreparationRole = CortexForkPreparationRoles.Unknown,
        }).BindingDigest
            && migratedLegacy.ancestorSeedDigest == migratedLegacy.coldSeedDigest
            && migratedLegacy.preparedSeedDigest == migratedLegacy.coldSeedDigest;
        bool accounting = 1 + 3 == 4;
        bool passed = exact && boolRejected && imageRejected && baseRejected && chainRejected
            && configRejected && stepRejected && reusedBindingChanges && schemaBound && legacyBindingPreserved && accounting;
        output.WriteLine($"  checkpoint proof · exact={(exact ? "yes" : "NO")} flips={(boolRejected && imageRejected && baseRejected && chainRejected && configRejected && stepRejected ? "rejected" : "ACCEPTED")} schema={(schemaBound ? "bound" : "BROKEN")} legacy={(legacyBindingPreserved ? "stored-binding" : "BROKEN")} fanout=1miss+3reuse={(accounting ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    /// Focused composite-topology fixture: a real parent drive materializes one
    /// pre-step S0, then calibration and evaluation child runs load that exact
    /// image under the parent's child namespace. This is intentionally an
    /// end-to-end fixture rather than a receipt-only synthetic assertion.
    public static bool VerifyCompositeRailReceiptContract()
    {
        string token = Guid.NewGuid().ToString("N");
        string corpusPath = Path.GetFullPath(Path.Combine(".tmp", $"composite-rail-fixture-{token}.txt"));
        string? parentDirectory = null;
        string? unboundDirectory = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(corpusPath)!);
            File.WriteAllText(corpusPath, "alpha beta gamma\nalpha beta delta\ncalibration evaluation\n");

            CortexConfig config = new()
            {
                RunName = $"composite-rail-fixture-{token}",
                Steps = 1,
                Seed = 0xC0117011UL,
                Curriculum = new CortexFlatPoolCurriculum
                {
                    Corpus = new CogitoCorpus { Path = corpusPath, Glob = "*.txt" },
                    IntakeBatch = 1,
                    SeedSpans = 1,
                    MixEvery = 1,
                },
            };

            CortexForkSeed? coldSeed = null;
            CortexExecutionWindow parentWindow = default;
            int parentSeedLoads = 0;
            Cortex parent = new(config);
            int parentExit = parent.Run((runtime, window) =>
            {
                if (Interlocked.Increment(ref parentSeedLoads) != 1)
                    throw new InvalidOperationException("composite fixture materialized cold S0 more than once");
                coldSeed = runtime.MaterializeColdForkSeed();
                parentWindow = window.Validate();
                parentDirectory = runtime.CurrentRun.Dir;
            });
            if (parentExit != 0 || coldSeed is null || parentSeedLoads != 1 || parentWindow != new CortexExecutionWindow(0, 1))
                return false;

            Run parentRun = Run.Open(parentDirectory!);
            Run preMaterializedRun = parentRun.CreateChildRun(CortexForkRailRoles.Calibration);
            string parentID = Path.GetFileName(parentDirectory!);
            string preMaterializedID = Path.GetFileName(preMaterializedRun.Dir);
            CortexForkMaterializationContract preMaterializedContract = new(parentID, "fixture-attempt", preMaterializedID, coldSeed.ColdSeedDigest);
            preMaterializedRun.WriteAtomic(CortexForkMaterializationContract.MarkerFileName,
                stream => stream.Write(Encoding.UTF8.GetBytes(preMaterializedContract.Encode())));
            CortexForkArm<int> preMaterializedArm = new(
                preMaterializedRun.Dir, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Calibration, parentRunID: parentID,
                materializationContract: preMaterializedContract);
            bool preMaterializedAccepted = false;
            try { _ = CortexForkRunner.RunFork(parent, coldSeed, preMaterializedArm, parentWindow.EndStep); preMaterializedAccepted = true; }
            catch (Exception) { }
            bool preMaterializedReuseRejected = false;
            try { _ = CortexForkRunner.RunFork(parent, coldSeed, preMaterializedArm, parentWindow.EndStep); }
            catch (IOException) { preMaterializedReuseRejected = true; }
            Run wrongColdRun = parentRun.CreateChildRun(CortexForkRailRoles.Calibration);
            CortexForkMaterializationContract wrongColdContract = new(parentID, "fixture-wrong-cold", Path.GetFileName(wrongColdRun.Dir), new string('0', 64));
            wrongColdRun.WriteAtomic(CortexForkMaterializationContract.MarkerFileName,
                stream => stream.Write(Encoding.UTF8.GetBytes(wrongColdContract.Encode())));
            CortexForkArm<int> wrongColdArm = new(wrongColdRun.Dir, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Calibration, parentRunID: parentID, materializationContract: wrongColdContract);
            bool wrongColdRejected = false;
            try { _ = CortexForkRunner.RunFork(parent, coldSeed, wrongColdArm, parentWindow.EndStep); }
            catch (InvalidDataException) { wrongColdRejected = true; }
            Run wrongParentRun = parentRun.CreateChildRun(CortexForkRailRoles.Evaluation);
            CortexForkMaterializationContract wrongParentContract = new("wrong-parent", "fixture-wrong-parent", Path.GetFileName(wrongParentRun.Dir), coldSeed.ColdSeedDigest);
            wrongParentRun.WriteAtomic(CortexForkMaterializationContract.MarkerFileName,
                stream => stream.Write(Encoding.UTF8.GetBytes(wrongParentContract.Encode())));
            CortexForkArm<int> wrongParentContractArm = new(wrongParentRun.Dir, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Evaluation, parentRunID: parentID, materializationContract: wrongParentContract);
            bool wrongParentRejected = false;
            try { _ = CortexForkRunner.RunFork(parent, coldSeed, wrongParentContractArm, parentWindow.EndStep); }
            catch (InvalidDataException) { wrongParentRejected = true; }
            Run arbitraryExistingRun = parentRun.CreateChildRun(CortexForkRailRoles.Evaluation);
            arbitraryExistingRun.WriteAtomic("arbitrary", stream => stream.Write("x"u8));
            CortexForkArm<int> arbitraryExistingArm = new(
                arbitraryExistingRun.Dir, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Evaluation, parentRunID: parentID);
            bool arbitraryExistingRejected = false;
            try { _ = CortexForkRunner.RunFork(parent, coldSeed, arbitraryExistingArm, parentWindow.EndStep); }
            catch (IOException) { arbitraryExistingRejected = true; }
            if (!preMaterializedAccepted || !preMaterializedReuseRejected || !wrongColdRejected || !wrongParentRejected || !arbitraryExistingRejected)
                return false;
            string calibrationDirectory = Path.Combine(parentRun.Dir, "children", parentRun.NextChildRunID(CortexForkRailRoles.Calibration));
            string evaluationDirectory = Path.Combine(parentRun.Dir, "children", parentRun.NextChildRunID(CortexForkRailRoles.Evaluation));
            int calibrationBinds = 0;
            int evaluationBinds = 0;

            CortexForkArm<int> calibration = new(
                calibrationDirectory,
                () => new Cortex(config),
                runtime => runtime.Step,
                railRole: CortexForkRailRoles.Calibration,
                afterRuntimeBind: (_, window) =>
                {
                    if (window != parentWindow) throw new InvalidDataException("calibration runtime window diverged");
                    Interlocked.Increment(ref calibrationBinds);
                });
            CortexForkArm<int> evaluation = new(
                evaluationDirectory,
                () => new Cortex(config),
                runtime => runtime.Step,
                railRole: CortexForkRailRoles.Evaluation,
                afterRuntimeBind: (_, window) =>
                {
                    if (window != parentWindow) throw new InvalidDataException("evaluation runtime window diverged");
                    Interlocked.Increment(ref evaluationBinds);
                });

            CortexMatchedForkReceipt<int> matched = CortexForkRunner.RunMatchedFork(
                parent, coldSeed, calibration, evaluation, parentWindow.EndStep);
            CortexForkRunReceipt<int> left = matched.Left;
            CortexForkRunReceipt<int> right = matched.Right;
            if (!matched.IsExact || !matched.SeedRelation.Exact
                || calibrationBinds != 1 || evaluationBinds != 1
                || !left.SeedLoad.Bound || !right.SeedLoad.Bound
                || left.SeedLoad.ParentRunID != Path.GetFileName(parentDirectory)
                || right.SeedLoad.ParentRunID != Path.GetFileName(parentDirectory)
                || left.SeedLoad.Role != CortexForkRailRoles.Calibration
                || right.SeedLoad.Role != CortexForkRailRoles.Evaluation
                || left.SeedLoad.ExecutionWindow != parentWindow
                || right.SeedLoad.ExecutionWindow != parentWindow
                || left.SeedLoad.ColdSeedDigest != coldSeed.ColdSeedDigest
                || right.SeedLoad.ColdSeedDigest != coldSeed.ColdSeedDigest
                || left.SeedLoad.PersistedConfigDigest != coldSeed.PersistedConfigDigest
                || right.SeedLoad.PersistedConfigDigest != coldSeed.PersistedConfigDigest
                || left.SeedLoad.SourceSeedDigest != coldSeed.ColdSeedDigest
                || right.SeedLoad.SourceSeedDigest != coldSeed.ColdSeedDigest
                || left.SeedLoad.SourceRunID != Path.GetFileName(parentDirectory)
                    || right.SeedLoad.SourceRunID != Path.GetFileName(parentDirectory)
                    || left.SeedLoad.SourceNextStep != coldSeed.NextStep
                    || right.SeedLoad.SourceNextStep != coldSeed.NextStep
                    || left.SeedLoad.AncestorSeedDigest != right.SeedLoad.AncestorSeedDigest
                    || left.SeedLoad.PreparedSeedDigest != right.SeedLoad.PreparedSeedDigest
                    || left.SeedLoad.PreparationRole != right.SeedLoad.PreparationRole
                || left.Timing.TotalWallMilliseconds != left.Timing.SeedIOWallMilliseconds
                    + left.Timing.ExecutionWallMilliseconds
                    + left.Timing.TerminalVerifierWallMilliseconds
                    + left.Timing.RuntimeBindWallMilliseconds
                || right.Timing.TotalWallMilliseconds != right.Timing.SeedIOWallMilliseconds
                    + right.Timing.ExecutionWallMilliseconds
                    + right.Timing.TerminalVerifierWallMilliseconds
                    + right.Timing.RuntimeBindWallMilliseconds)
                return false;

            foreach (CortexForkRunReceipt<int> receipt in new[] { left, right })
            {
                string railPath = Path.Combine(receipt.RunDirectory, "seed-load-receipt.ron");
                if (!File.Exists(railPath)) return false;
                byte[] bytes = File.ReadAllBytes(railPath);
                CortexForkSeedLoadRailDocument railDocument = CortexForkTerminalRunReceipt.ReadSeedRailDocument(railPath);
                CortexForkSeedLoadRailReceipt document = railDocument.Rail;
                if (!bytes.AsSpan().SequenceEqual(document.Encode())
                    || document.parentRunID != receipt.SeedLoad.ParentRunID
                    || document.childRunID != receipt.SeedLoad.ChildRunID
                    || document.role != receipt.SeedLoad.Role
                    || document.sourceSeedDigest != receipt.SeedLoad.SourceSeedDigest
                    || document.sourceRunID != receipt.SeedLoad.SourceRunID
                    || document.sourceNextStep != receipt.SeedLoad.SourceNextStep
                    || document.expectedCheckpointSHA256 != receipt.SeedLoad.ExpectedCheckpointSHA256
                    || document.expectedTapeSpanlogSHA256 != receipt.SeedLoad.ExpectedTapeSpanlogSHA256
                    || document.expectedCurveSHA256 != receipt.SeedLoad.ExpectedCurveSHA256
                    || document.expectedExcursionsSHA256 != receipt.SeedLoad.ExpectedExcursionsSHA256
                    || document.loadedCheckpointSHA256 != receipt.SeedLoad.LoadedCheckpointSHA256
                    || document.loadedTapeSpanlogSHA256 != receipt.SeedLoad.LoadedTapeSpanlogSHA256
                    || document.loadedCurveSHA256 != receipt.SeedLoad.LoadedCurveSHA256
                    || document.loadedExcursionsSHA256 != receipt.SeedLoad.LoadedExcursionsSHA256
                    || document.startStep != parentWindow.StartStep
                    || document.endStep != parentWindow.EndStep
                    || document.ancestorSeedDigest != receipt.SeedLoad.AncestorSeedDigest
                    || document.preparedSeedDigest != receipt.SeedLoad.PreparedSeedDigest
                    || document.preparationRole != receipt.SeedLoad.PreparationRole
                    || railDocument.StoredBindingDigest != receipt.SeedLoad.BindingDigest)
                    return false;
                // Composite rails are topology-only in this fixture (no
                // registered gate): the legacy one-rail authority sink must
                // stay suppressed while seed-load/gate identity remains.
                string[] legacyReceipts =
                [
                    "deep-rematch.a3.ron", "deep-rematch.rung0.ron",
                    "deep-rematch.rung0-control.ron", "deep-rematch.checkpoint.ron",
                    "deep-rematch.funding.ron", "deep-rematch.policy.ron",
                ];
                if (legacyReceipts.Any(file => File.Exists(Path.Combine(receipt.RunDirectory, file))))
                    return false;

                CortexForkTerminalRunReceipt durable = CortexForkTerminalRunReceipt.Read(receipt.RunDirectory);
                CortexForkRunReceipt<int> recovered = CortexForkTerminalRunReceipt.Recover(receipt.RunDirectory, 0);
                CortexForkLandingOutcomeReceipt landing = CortexForkLandingOutcomeReceipt.Read(receipt.RunDirectory);
                if (durable.actualNextStep != receipt.StepSpan.ActualNextStep
                    || durable.finalCheckpointSHA256 != receipt.FinalDigests.CheckpointSHA256
                    || durable.finalTapeSpanlogSHA256 != receipt.FinalDigests.TapeSpanlogSHA256
                    || durable.finalCurveSHA256 != receipt.FinalDigests.CurveSHA256
                    || durable.finalExcursionsSHA256 != receipt.FinalDigests.ExcursionsSHA256
                    || landing.state != CortexForkLandingOutcomeStates.TerminalChildExact
                    || landing.callbackAttempted || landing.callbackReturned
                    || recovered.StepSpan != receipt.StepSpan
                    || recovered.FinalDigests.CheckpointSHA256 != receipt.FinalDigests.CheckpointSHA256
                    || recovered.Timing.TotalRawTicks <= 0)
                    return false;

                string terminalRunPath = Path.Combine(receipt.RunDirectory, CortexForkTerminalRunReceipt.FileName);
                byte[] terminalRunBytes = File.ReadAllBytes(terminalRunPath);
                terminalRunBytes[^1] ^= 0x01;
                File.WriteAllBytes(terminalRunPath, terminalRunBytes);
                bool receiptCorruptionRejected = false;
                try { _ = CortexForkTerminalRunReceipt.Read(receipt.RunDirectory); }
                catch (Exception) { receiptCorruptionRejected = true; }
                CortexForkTerminalRunReceipt restoredDurable = durable;
                File.WriteAllBytes(terminalRunPath, RonSerializer.SerializeToUtf8(in restoredDurable));
                if (!receiptCorruptionRejected) return false;

                string curvePath = Path.Combine(receipt.RunDirectory, "curve.tsv");
                byte[] curveBytes = File.ReadAllBytes(curvePath);
                File.WriteAllBytes(curvePath, [.. curveBytes, (byte)'x']);
                bool artifactCorruptionRejected = false;
                try { _ = CortexForkTerminalRunReceipt.Read(receipt.RunDirectory); }
                catch (InvalidDataException) { artifactCorruptionRejected = true; }
                File.WriteAllBytes(curvePath, curveBytes);
                if (!artifactCorruptionRejected) return false;

                bool authorityCorruptionRejected = true;
                foreach (string authorityFile in new[] { "seed-load-intent.ron", "seed-load-receipt.ron", "terminal-verification.ron" })
                {
                    string authorityPath = Path.Combine(receipt.RunDirectory, authorityFile);
                    byte[] authorityBytes = File.ReadAllBytes(authorityPath);
                    authorityBytes[^1] ^= 0x01;
                    File.WriteAllBytes(authorityPath, authorityBytes);
                    bool rejected = false;
                    try { _ = CortexForkTerminalRunReceipt.Read(receipt.RunDirectory); }
                    catch (InvalidDataException) { rejected = true; }
                    byte[] restoredAuthorityBytes = authorityBytes;
                    restoredAuthorityBytes[^1] ^= 0x01;
                    File.WriteAllBytes(authorityPath, restoredAuthorityBytes);
                    authorityCorruptionRejected &= rejected;
                }
                if (!authorityCorruptionRejected) return false;
            }

            string completedLandingDirectory = Path.Combine(parentRun.Dir, "children", parentRun.NextChildRunID(CortexForkRailRoles.Baseline));
            int completedLandingCalls = 0;
            CortexForkArm<int> completedLandingArm = new(
                completedLandingDirectory, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Baseline,
                afterRunLanded: (_, _, _) => Interlocked.Increment(ref completedLandingCalls));
            CortexForkRunReceipt<int> completedLanding = RunFork(parent, coldSeed, completedLandingArm, parentWindow.EndStep);
            CortexForkLandingOutcomeReceipt completedOutcome = CortexForkLandingOutcomeReceipt.Read(completedLanding.RunDirectory);
            if (completedLandingCalls != 1 || completedOutcome.state != CortexForkLandingOutcomeStates.Completed
                || !completedOutcome.callbackAttempted || !completedOutcome.callbackReturned
                || completedOutcome.callbackRawTicks <= 0 || completedOutcome.terminalReceiptDigest.Length != 64)
                return false;

            string persistedCompletionDirectory = Path.Combine(parentRun.Dir, "children", parentRun.NextChildRunID(CortexForkRailRoles.ForcedNull));
            string persistedCompletionPath = Path.Combine(persistedCompletionDirectory, "completion-output.ron");
            List<string> persistedCompletionOrder = [];
            CortexForkArm<int> persistedCompletionArm = new(
                persistedCompletionDirectory, () => new Cortex(config),
                runtime =>
                {
                    persistedCompletionOrder.Add("read");
                    return runtime.Step;
                },
                railRole: CortexForkRailRoles.ForcedNull,
                persistCompletionBeforeLanding: (runtime, seedLoad, finalDigests, outcome) =>
                {
                    if (outcome != runtime.Step || !seedLoad.Bound || finalDigests.CheckpointSHA256.Length != 64)
                        throw new InvalidDataException("completion persistence hook did not receive the final typed outcome");
                    File.WriteAllText(persistedCompletionPath, $"outcome={outcome}\n");
                    persistedCompletionOrder.Add("persist");
                },
                afterRunLanded: (_, _, _) =>
                {
                    if (!File.Exists(persistedCompletionPath))
                        throw new InvalidDataException("completion persistence hook did not land its output before the caller hook");
                    persistedCompletionOrder.Add("landing");
                });
            CortexForkRunReceipt<int> persistedCompletion = RunFork(parent, coldSeed, persistedCompletionArm, parentWindow.EndStep);
            CortexForkLandingOutcomeReceipt persistedCompletionOutcome = CortexForkLandingOutcomeReceipt.Read(persistedCompletion.RunDirectory);
            if (!persistedCompletionOrder.SequenceEqual(["read", "persist", "landing"])
                || persistedCompletionOutcome.state != CortexForkLandingOutcomeStates.Completed
                || !persistedCompletionOutcome.callbackAttempted || !persistedCompletionOutcome.callbackReturned
                || !File.ReadAllText(persistedCompletionPath).Equals($"outcome={persistedCompletion.Outcome}\n", StringComparison.Ordinal))
                return false;

            string failedPersistenceDirectory = Path.Combine(parentRun.Dir, "children", parentRun.NextChildRunID(CortexForkRailRoles.ReflexFrozen));
            int failedPersistenceLandingCalls = 0;
            CortexForkArm<int> failedPersistenceArm = new(
                failedPersistenceDirectory, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.ReflexFrozen,
                persistCompletionBeforeLanding: (_, _, _, _) => throw new InvalidOperationException("fixture completion persistence failed"),
                afterRunLanded: (_, _, _) => Interlocked.Increment(ref failedPersistenceLandingCalls));
            bool persistenceFailureRethrown = false;
            try { _ = RunFork(parent, coldSeed, failedPersistenceArm, parentWindow.EndStep); }
            catch (InvalidOperationException error) when (error.Message == "fixture completion persistence failed")
            {
                persistenceFailureRethrown = true;
            }
            CortexForkLandingOutcomeReceipt failedPersistenceOutcome = CortexForkLandingOutcomeReceipt.Read(failedPersistenceDirectory);
            CortexForkTerminalRunReceipt failedPersistenceTerminal = CortexForkTerminalRunReceipt.Read(failedPersistenceDirectory);
            if (!persistenceFailureRethrown || failedPersistenceLandingCalls != 0
                || failedPersistenceOutcome.state != CortexForkLandingOutcomeStates.CallbackFailed
                || !failedPersistenceOutcome.callbackAttempted || failedPersistenceOutcome.callbackReturned
                || failedPersistenceOutcome.callbackExceptionType != typeof(InvalidOperationException).FullName
                || failedPersistenceOutcome.callbackExceptionMessage != "fixture completion persistence failed"
                || !failedPersistenceOutcome.authorityChainExact
                || failedPersistenceOutcome.authorityBeforeTerminalSHA256 != failedPersistenceOutcome.authorityAfterTerminalSHA256
                || failedPersistenceOutcome.terminalReceiptDigest != failedPersistenceTerminal.receiptDigest)
                return false;

            string failedLandingDirectory = Path.Combine(parentRun.Dir, "children", parentRun.NextChildRunID(CortexForkRailRoles.Candidate));
            CortexForkArm<int> failedLandingArm = new(
                failedLandingDirectory, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Candidate,
                afterRunLanded: (_, _, _) => throw new InvalidOperationException("fixture landing callback failed"));
            bool callbackFailureRethrown = false;
            try { _ = RunFork(parent, coldSeed, failedLandingArm, parentWindow.EndStep); }
            catch (InvalidOperationException error) when (error.Message == "fixture landing callback failed")
            {
                callbackFailureRethrown = true;
            }
            CortexForkLandingOutcomeReceipt failedOutcome = CortexForkLandingOutcomeReceipt.Read(failedLandingDirectory);
            if (!callbackFailureRethrown || failedOutcome.state != CortexForkLandingOutcomeStates.CallbackFailed
                || !failedOutcome.callbackAttempted || failedOutcome.callbackReturned
                || failedOutcome.callbackExceptionType != typeof(InvalidOperationException).FullName
                || failedOutcome.callbackExceptionMessage != "fixture landing callback failed"
                || failedOutcome.callbackRawTicks <= 0)
                return false;
            string landingOutcomePath = Path.Combine(completedLanding.RunDirectory, CortexForkLandingOutcomeReceipt.FileName);
            byte[] landingOutcomeBytes = File.ReadAllBytes(landingOutcomePath);
            landingOutcomeBytes[^1] ^= 0x01;
            File.WriteAllBytes(landingOutcomePath, landingOutcomeBytes);
            bool landingCorruptionRejected = false;
            try { _ = CortexForkLandingOutcomeReceipt.Read(completedLanding.RunDirectory); }
            catch (InvalidDataException) { landingCorruptionRejected = true; }
            File.WriteAllBytes(landingOutcomePath, RonSerializer.SerializeToUtf8(in completedOutcome));
            if (!landingCorruptionRejected) return false;

            // Known rails fail closed when the spawning Cortex has no active
            // parent, when the declared parent id is forged, or when the child
            // basename does not carry the declared role.
            unboundDirectory = Path.Combine(".tmp", $"composite-rail-unbound-{token}");
            CortexForkArm<int> unboundArm = new(unboundDirectory, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Calibration);
            bool rejectedUnbound = false;
            try { _ = RunFork(new Cortex(config), coldSeed, unboundArm, parentWindow.EndStep); }
            catch (InvalidOperationException) { rejectedUnbound = true; }
            if (!rejectedUnbound) return false;

            string forgedParentDirectory = Path.Combine(parentRun.Dir, "children", "calibration_9998");
            CortexForkArm<int> forgedParentArm = new(forgedParentDirectory, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Calibration, parentRunID: "forged-parent");
            bool rejectedParent = false;
            try { _ = RunFork(parent, coldSeed, forgedParentArm, parentWindow.EndStep); }
            catch (InvalidDataException) { rejectedParent = true; }
            if (!rejectedParent) return false;

            string wrongRoleDirectory = Path.Combine(parentRun.Dir, "children", "evaluation_9998");
            CortexForkArm<int> wrongRoleArm = new(wrongRoleDirectory, () => new Cortex(config), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Calibration);
            bool rejectedRole = false;
            try { _ = RunFork(parent, coldSeed, wrongRoleArm, parentWindow.EndStep); }
            catch (InvalidDataException) { rejectedRole = true; }
            if (!rejectedRole) return false;

            string mismatchedConfigDirectory = Path.Combine(parentRun.Dir, "children", "evaluation_9997");
            CortexConfig mismatchedConfig = new()
            {
                RunName = config.RunName,
                Steps = config.Steps,
                Seed = config.Seed + 1,
                Curriculum = config.Curriculum,
            };
            CortexForkArm<int> mismatchedConfigArm = new(mismatchedConfigDirectory, () => new Cortex(mismatchedConfig), static runtime => runtime.Step,
                railRole: CortexForkRailRoles.Evaluation);
            bool rejectedFactoryConfig = false;
            try { _ = RunFork(parent, coldSeed, mismatchedConfigArm, parentWindow.EndStep); }
            catch (InvalidDataException) { rejectedFactoryConfig = true; }
            if (!rejectedFactoryConfig) return false;

            // Recovery creates a fresh checkpoint runtime, so the rail role must
            // be rebound before the load callback observes it. A changed role is
            // rejected by the same immutable runtime seam used by RunArm.
            CortexRunConfig recoveredConfig = Checkpoint.PeekConfig(left.RunDirectory);
            Cortex recoveredRuntime = Cortex.CreateCheckpointRuntime(recoveredConfig, left.RunDirectory);
            recoveredRuntime.BindForkRailRole(CortexForkRailRoles.Calibration);
            recoveredRuntime.DisableAutonomicSpawning();
            bool recoveredRoleBound = false;
            int recoveredLoad = Cortex.LoadCheckpointRuntime(recoveredRuntime, recoveredConfig, left.RunDirectory,
                afterLoad: (loaded, _, _) => recoveredRoleBound = loaded.ForkRailRole == CortexForkRailRoles.Calibration
                    && !loaded.AllowsAutonomicSpawning,
                checkpointOccurrenceCheck: _ => { });
            bool changedRoleRejected = false;
            try { recoveredRuntime.BindForkRailRole(CortexForkRailRoles.Evaluation); }
            catch (InvalidOperationException) { changedRoleRejected = true; }
            if (recoveredLoad != 0 || !recoveredRoleBound || !changedRoleRejected) return false;
            return true;
        }
        finally
        {
            if (parentDirectory is not null && Directory.Exists(parentDirectory))
                Directory.Delete(parentDirectory, recursive: true);
            if (unboundDirectory is not null && Directory.Exists(unboundDirectory))
                Directory.Delete(unboundDirectory, recursive: true);
            if (File.Exists(corpusPath)) File.Delete(corpusPath);
        }
    }

    /// Focused durability proof for the generic terminal/run authority. The one-step materialization keeps this
    /// verifier independent of the long composite fixture while exercising real files, recovery, and corruption.
    public static bool VerifyTerminalReceiptContract(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        bool passed = VerifyCompositeRailReceiptContract();
        output.WriteLine($"  fork terminal receipt · typed-recovery={(passed ? "exact" : "FAIL")} · corruption={(passed ? "rejected" : "UNKNOWN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    public static CortexForkRunReceipt<TOutcome> RunFork<TOutcome>(
        Cortex spawningCortex,
        CortexForkSeed seed,
        CortexForkArm<TOutcome> arm,
        int absoluteHorizon)
    {
        ArgumentNullException.ThrowIfNull(spawningCortex);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(arm);
        if (!spawningCortex.AllowsAutonomicSpawning)
            throw new InvalidOperationException("autonomic fork spawning is disabled for this Cortex runtime");
        if (absoluteHorizon <= seed.NextStep)
            throw new ArgumentOutOfRangeException(nameof(absoluteHorizon), absoluteHorizon,
                $"absolute fork horizon must advance beyond seed step {seed.NextStep}");

        string runDirectory = Path.GetFullPath(arm.RunDirectory);
        ValidateMaterializationContract(spawningCortex, seed, arm, runDirectory);
        CortexForkSeedLoadReceipt seedLoad = WriteSeedDirectory(seed, runDirectory, arm.MaterializationContract);
        CortexExecutionWindow window = new CortexExecutionWindow(seed.NextStep, absoluteHorizon).Validate();
        seedLoad = BindSeedLoadReceipt(seedLoad, spawningCortex, arm, seed, window, runDirectory);
        Cortex cortex = arm.CreateCortex() ?? throw new InvalidOperationException("fork factory returned null");
        CortexForkRunReceipt<TOutcome> receipt = RunArm(
            seed.Digests, seedLoad, seed.NextStep, runDirectory, arm, cortex, absoluteHorizon, verifyTerminal: true,
            applyInitialIntervention: true,
            arm.ResolveAnytimeIdentity(0, ReadSpawningAnytimeDigest(spawningCortex)));
        Trace.Cortex.Boundary("fork.arm",
            $"arm={Path.GetFileName(runDirectory)} wall={receipt.WallMilliseconds}ms horizon={absoluteHorizon} final={receipt.FinalDigests.CheckpointSHA256}");
        return receipt;
    }

    internal static void ValidateMaterializationContract<TOutcome>(
        Cortex spawningCortex,
        CortexForkSeed seed,
        CortexForkArm<TOutcome> arm,
        string runDirectory,
        bool requireContract = false)
    {
        if (arm.MaterializationContract is not CortexForkMaterializationContract contract)
        {
            if (requireContract)
                throw new InvalidDataException("fork seed materialization requires a typed contract");
            return;
        }
        contract.Validate(runDirectory);
        if (!string.Equals(contract.ColdSeedDigest, seed.ColdSeedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("fork materialization contract cold seed disagrees with the actual seed");
        string parentDirectory;
        try { parentDirectory = Path.GetFullPath(spawningCortex.CurrentRun.Dir); }
        catch (InvalidOperationException error) { throw new InvalidDataException("fork materialization contract requires an active spawning parent", error); }
        string parentID = Path.GetFileName(parentDirectory);
        if (!string.Equals(contract.ParentRunID, parentID, StringComparison.Ordinal)
            || !string.Equals(arm.ParentRunID, parentID, StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(runDirectory), Path.Combine(parentDirectory, "children"), StringComparison.Ordinal)
            || !string.Equals(contract.ChildRunID, Path.GetFileName(runDirectory), StringComparison.Ordinal))
            throw new InvalidDataException("fork materialization contract is detached from the spawning parent or child path");
    }

    public static CortexMatchedForkReceipt<TOutcome> RunMatchedFork<TOutcome>(
        Cortex spawningCortex,
        CortexForkSeed seed,
        CortexForkArm<TOutcome> left,
        CortexForkArm<TOutcome> right,
        int absoluteHorizon)
    {
        ArgumentNullException.ThrowIfNull(spawningCortex);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!spawningCortex.AllowsAutonomicSpawning)
            throw new InvalidOperationException("autonomic matched-fork spawning is disabled for this Cortex runtime");
        if (absoluteHorizon <= seed.NextStep)
            throw new ArgumentOutOfRangeException(nameof(absoluteHorizon), absoluteHorizon,
                $"absolute fork horizon must advance beyond seed step {seed.NextStep}");

        string leftDirectory = Path.GetFullPath(left.RunDirectory);
        string rightDirectory = Path.GetFullPath(right.RunDirectory);
        if (string.Equals(leftDirectory, rightDirectory, StringComparison.Ordinal))
            throw new ArgumentException("matched fork arms require distinct run directories");

        long forkStarted = Stopwatch.GetTimestamp();
        CortexForkSeedLoadReceipt leftSeedLoad = WriteSeedDirectory(seed, leftDirectory);
        CortexForkSeedLoadReceipt rightSeedLoad = CopyRunState(leftDirectory, rightDirectory, seed.Digests);
        HashSet<Cortex> reservedWorlds = new(ReferenceEqualityComparer.Instance);
        CheckpointRoundTripProof sharedProof = VerifySharedCheckpointProof(seed, left, leftDirectory, reservedWorlds);
        leftSeedLoad = leftSeedLoad with { CheckpointProof = sharedProof, CheckpointProofReused = false };
        rightSeedLoad = rightSeedLoad with { CheckpointProof = sharedProof, CheckpointProofReused = true };
        CortexExecutionWindow window = new CortexExecutionWindow(seed.NextStep, absoluteHorizon).Validate();
        leftSeedLoad = BindSeedLoadReceipt(leftSeedLoad, spawningCortex, left, seed, window, leftDirectory);
        rightSeedLoad = BindSeedLoadReceipt(rightSeedLoad, spawningCortex, right, seed, window, rightDirectory);
        (CortexForkRunReceipt<TOutcome> leftReceipt, CortexForkRunReceipt<TOutcome> rightReceipt) = RunArmsIndependently(
            seed.NextStep, seed.Digests, leftSeedLoad, leftDirectory, left,
            seed.NextStep, seed.Digests, rightSeedLoad, rightDirectory, right,
            absoluteHorizon, verifyTerminal: true, rung: 0,
            leftParentPointID: ReadSpawningAnytimeDigest(spawningCortex),
            rightParentPointID: ReadSpawningAnytimeDigest(spawningCortex), reservedWorlds);
        long forkWallMilliseconds = Stopwatch.GetElapsedTime(forkStarted).Ticks / TimeSpan.TicksPerMillisecond;
        CortexForkSeedRelation relation = CreateSeedRelation(
            0, CortexForkSeedRelations.InitialCrossArm,
            seed.Digests, leftReceipt.SeedLoad.LoadedDigests,
            seed.Digests, rightReceipt.SeedLoad.LoadedDigests);
        CortexForkPairTiming timing = new(
            leftReceipt.Timing.TotalWallMilliseconds,
            rightReceipt.Timing.TotalWallMilliseconds,
            forkWallMilliseconds);
        Trace.Cortex.Boundary("fork.wall",
            $"left={timing.LeftTotalWallMilliseconds}ms right={timing.RightTotalWallMilliseconds}ms serial={timing.SerialWallMilliseconds}ms total={timing.ParallelWallMilliseconds}ms reduced={(timing.ParallelWallReduced ? "yes" : "no")} horizon={absoluteHorizon} seed-relation={relation.Exact} left.final={leftReceipt.FinalDigests.CheckpointSHA256} right.final={rightReceipt.FinalDigests.CheckpointSHA256}");
        return new CortexMatchedForkReceipt<TOutcome>(leftReceipt, rightReceipt, relation, timing);
    }

    public static List<CortexMatchedForkReceipt<TOutcome>> RunMatchedForkLadder<TOutcome>(
        Cortex spawningCortex,
        CortexForkSeed seed,
        CortexForkArm<TOutcome>[] leftArms,
        CortexForkArm<TOutcome>[] rightArms,
        int[] absoluteHorizons)
    {
        ArgumentNullException.ThrowIfNull(spawningCortex);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(leftArms);
        ArgumentNullException.ThrowIfNull(rightArms);
        ArgumentNullException.ThrowIfNull(absoluteHorizons);
        if (!spawningCortex.AllowsAutonomicSpawning)
            throw new InvalidOperationException("autonomic matched-fork spawning is disabled for this Cortex runtime");
        if (leftArms.Length == 0 || leftArms.Length != rightArms.Length || leftArms.Length != absoluteHorizons.Length)
            throw new ArgumentException("matched fork ladder requires equally sized non-empty arm and horizon arrays");
        int priorHorizon = seed.NextStep;
        for (int i = 0; i < absoluteHorizons.Length; i++)
        {
            if (absoluteHorizons[i] <= priorHorizon)
                throw new ArgumentException("matched fork ladder horizons must increase strictly", nameof(absoluteHorizons));
            priorHorizon = absoluteHorizons[i];
        }

        List<CortexMatchedForkReceipt<TOutcome>> receipts = new(absoluteHorizons.Length);
        CortexForkDigests leftDigests = seed.Digests;
        CortexForkDigests rightDigests = seed.Digests;
        int leftSeedStep = seed.NextStep;
        int rightSeedStep = seed.NextStep;
        string leftAnytimeDigest = ReadSpawningAnytimeDigest(spawningCortex);
        string rightAnytimeDigest = leftAnytimeDigest;
        for (int i = 0; i < absoluteHorizons.Length; i++)
        {
            string leftDirectory = Path.GetFullPath(leftArms[i].RunDirectory);
            string rightDirectory = Path.GetFullPath(rightArms[i].RunDirectory);
            if (string.Equals(leftDirectory, rightDirectory, StringComparison.Ordinal))
                throw new ArgumentException("matched fork arms require distinct run directories");
            long started = Stopwatch.GetTimestamp();
            CortexForkSeedLoadReceipt leftSeedLoad;
            CortexForkSeedLoadReceipt rightSeedLoad;
            HashSet<Cortex>? reservedWorlds = null;
            if (i == 0)
            {
                leftSeedLoad = WriteSeedDirectory(seed, leftDirectory, leftArms[i].MaterializationContract);
                rightSeedLoad = rightArms[i].MaterializationContract is CortexForkMaterializationContract rightContract
                    ? WriteSeedDirectory(seed, rightDirectory, rightContract)
                    : CopyRunState(leftDirectory, rightDirectory, seed.Digests);
                CortexExecutionWindow window = new CortexExecutionWindow(seed.NextStep, absoluteHorizons[i]).Validate();
                leftSeedLoad = BindSeedLoadReceipt(leftSeedLoad, spawningCortex, leftArms[i], seed, window, leftDirectory);
                rightSeedLoad = BindSeedLoadReceipt(rightSeedLoad, spawningCortex, rightArms[i], seed, window, rightDirectory);
                reservedWorlds = new HashSet<Cortex>(ReferenceEqualityComparer.Instance);
                CheckpointRoundTripProof sharedProof = VerifySharedCheckpointProof(seed, leftArms[i], leftDirectory, reservedWorlds);
                leftSeedLoad = leftSeedLoad with { CheckpointProof = sharedProof, CheckpointProofReused = false };
                rightSeedLoad = rightSeedLoad with { CheckpointProof = sharedProof, CheckpointProofReused = true };
            }
            else
            {
                leftSeedLoad = CopyRunState(leftArms[i - 1].RunDirectory, leftDirectory, leftDigests);
                rightSeedLoad = CopyRunState(rightArms[i - 1].RunDirectory, rightDirectory, rightDigests);
                CortexExecutionWindow window = new CortexExecutionWindow(leftSeedStep, absoluteHorizons[i]).Validate();
                leftSeedLoad = BindSeedLoadReceipt(leftSeedLoad, spawningCortex, leftArms[i], seed, window, leftDirectory,
                    leftArms[i - 1].RunDirectory, leftDigests, leftSeedStep);
                rightSeedLoad = BindSeedLoadReceipt(rightSeedLoad, spawningCortex, rightArms[i], seed, window, rightDirectory,
                    rightArms[i - 1].RunDirectory, rightDigests, rightSeedStep);
                ValidateContinuationReceipt(leftSeedLoad, leftArms[i - 1].RunDirectory, leftSeedStep, leftDigests);
                ValidateContinuationReceipt(rightSeedLoad, rightArms[i - 1].RunDirectory, rightSeedStep, rightDigests);
            }

            (CortexForkRunReceipt<TOutcome> leftReceipt, CortexForkRunReceipt<TOutcome> rightReceipt) = RunArmsIndependently(
                leftSeedStep, leftDigests, leftSeedLoad, leftDirectory, leftArms[i],
                rightSeedStep, rightDigests, rightSeedLoad, rightDirectory, rightArms[i],
                absoluteHorizons[i], verifyTerminal: i == absoluteHorizons.Length - 1, rung: i,
                leftParentPointID: leftAnytimeDigest,
                rightParentPointID: rightAnytimeDigest,
                reservedWorlds: reservedWorlds);
            long wallMilliseconds = Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond;
            CortexForkSeedRelation relation = CreateSeedRelation(
                i,
                i == 0 ? CortexForkSeedRelations.InitialCrossArm : CortexForkSeedRelations.PerArmContinuation,
                leftDigests, leftReceipt.SeedLoad.LoadedDigests,
                rightDigests, rightReceipt.SeedLoad.LoadedDigests);
            CortexForkPairTiming timing = new(
                leftReceipt.Timing.TotalWallMilliseconds,
                rightReceipt.Timing.TotalWallMilliseconds,
                wallMilliseconds);
            receipts.Add(new CortexMatchedForkReceipt<TOutcome>(leftReceipt, rightReceipt, relation, timing));
            leftDigests = leftReceipt.FinalDigests;
            rightDigests = rightReceipt.FinalDigests;
            leftSeedStep = leftReceipt.StepSpan.ActualNextStep;
            rightSeedStep = rightReceipt.StepSpan.ActualNextStep;
            leftAnytimeDigest = leftReceipt.AnytimeCurveDigest;
            rightAnytimeDigest = rightReceipt.AnytimeCurveDigest;
            Trace.Cortex.Boundary("fork.ladder",
                $"rung={i} left={timing.LeftTotalWallMilliseconds}ms right={timing.RightTotalWallMilliseconds}ms serial={timing.SerialWallMilliseconds}ms total={wallMilliseconds}ms reduced={(timing.ParallelWallReduced ? "yes" : "no")} horizon={absoluteHorizons[i]} relation={relation.Kind} left-continuity={relation.LeftContinuityExact} right-continuity={relation.RightContinuityExact} left.final={leftReceipt.FinalDigests.CheckpointSHA256} right.final={rightReceipt.FinalDigests.CheckpointSHA256}");
        }
        return receipts;
    }

    internal static List<CortexMatchedForkNReceipt<TOutcome>> RunMatchedForkNLadder<TOutcome>(
        Cortex spawningCortex,
        CortexForkSeed seed,
        CortexForkArm<TOutcome>[][] arms,
        int[] absoluteHorizons,
        bool verifyEveryTerminal = false,
        Action<IReadOnlyList<CortexMatchedForkNReceipt<TOutcome>>>? inspectAfterRung = null)
    {
        ArgumentNullException.ThrowIfNull(spawningCortex);
        ArgumentNullException.ThrowIfNull(seed);
        if (arms is null || absoluteHorizons is null || arms.Length == 0 || arms.Any(static rung => rung is null || rung.Length < 3)
            || arms.Length != absoluteHorizons.Length)
            throw new ArgumentException("matched N-arm ladder requires at least three arms at every horizon");
        int priorHorizon = seed.NextStep;
        for (int i = 0; i < absoluteHorizons.Length; i++)
        {
            if (absoluteHorizons[i] <= priorHorizon) throw new ArgumentException("N-arm horizons must increase strictly", nameof(absoluteHorizons));
            priorHorizon = absoluteHorizons[i];
        }
        List<CortexMatchedForkNReceipt<TOutcome>> result = new(absoluteHorizons.Length);
        CortexForkDigests[] digests = Enumerable.Repeat(seed.Digests, arms[0].Length).ToArray();
        int[] steps = Enumerable.Repeat(seed.NextStep, arms[0].Length).ToArray();
        string[] anytimeDigests = Enumerable.Repeat(ReadSpawningAnytimeDigest(spawningCortex), arms[0].Length).ToArray();
        for (int rung = 0; rung < arms.Length; rung++)
        {
            CortexForkArm<TOutcome>[] rungArms = arms[rung];
            CortexForkSeedLoadReceipt[] loads = new CortexForkSeedLoadReceipt[rungArms.Length];
            HashSet<Cortex> reservedWorlds = new(ReferenceEqualityComparer.Instance);
            CortexForkNSeedRelation relation = default;
            for (int arm = 0; arm < rungArms.Length; arm++)
            {
                string childDirectory = Path.GetFullPath(rungArms[arm].RunDirectory);
                if (rung == 0)
                    ValidateMaterializationContract(spawningCortex, seed, rungArms[arm], childDirectory, requireContract: true);
                CortexForkSeed? preparedSeed = null;
                if (rung == 0)
                    (preparedSeed, loads[arm]) = PrepareArmSeed(seed, rungArms[arm], childDirectory, reservedWorlds);
                else
                    loads[arm] = CopyRunState(arms[rung - 1][arm].RunDirectory, childDirectory, digests[arm]);
                loads[arm] = BindSeedLoadReceipt(loads[arm], spawningCortex, rungArms[arm], seed,
                    new CortexExecutionWindow(steps[arm], absoluteHorizons[rung]).Validate(), childDirectory,
                    rung == 0 ? "" : arms[rung - 1][arm].RunDirectory,
                    rung == 0 ? null : digests[arm], steps[arm], preparedSeed);
                if (rung > 0)
                    ValidateContinuationReceipt(loads[arm], arms[rung - 1][arm].RunDirectory, steps[arm], digests[arm]);
            }
            if (rung == 0)
            {
                string[] preparedDigests = new string[loads.Length];
                CortexForkPreparationRoles[] preparationRoles = new CortexForkPreparationRoles[loads.Length];
                for (int arm = 0; arm < loads.Length; arm++)
                {
                    CortexForkSeed preparedSeed = CortexForkSeed.MaterializeRun(
                        rungArms[arm].RunDirectory, seed.NextStep);
                    CheckpointRoundTripProof preparedProof = VerifySharedCheckpointProof(
                        preparedSeed, rungArms[arm], Path.GetFullPath(rungArms[arm].RunDirectory), reservedWorlds);
                    loads[arm] = loads[arm] with { CheckpointProof = preparedProof, CheckpointProofReused = false };
                    preparedDigests[arm] = loads[arm].PreparedSeedDigest;
                    preparationRoles[arm] = loads[arm].PreparationRole;
                }
                relation = new CortexForkNSeedRelation(
                    rung, CortexForkSeedRelations.PreparedFromSharedAncestor,
                    seed.ColdSeedDigest, preparedDigests, preparationRoles,
                    Enumerable.Repeat(seed.ColdSeedDigest, loads.Length).ToArray(),
                    rungArms.Select(static arm => arm.PreparationRole).ToArray());
                for (int arm = 0; arm < loads.Length; arm++)
                    digests[arm] = loads[arm].LoadedDigests;
            }
            Task<CortexForkRunReceipt<TOutcome>>[] tasks = new Task<CortexForkRunReceipt<TOutcome>>[rungArms.Length];
            for (int arm = 0; arm < rungArms.Length; arm++)
            {
                int index = arm;
                CortexForkArm<TOutcome> forkArm = rungArms[index];
                Cortex fork = forkArm.CreateCortex() ?? throw new InvalidOperationException("N-arm factory returned null");
                if (!reservedWorlds.Add(fork))
                    throw new InvalidOperationException("N-arm fork factories returned an aliased Cortex object graph");
                tasks[index] = Task.Run(() => RunArm(
                    digests[index], loads[index], steps[index], Path.GetFullPath(forkArm.RunDirectory), forkArm, fork,
                    absoluteHorizons[rung], verifyEveryTerminal || rung == arms.Length - 1, applyInitialIntervention: false,
                    forkArm.ResolveAnytimeIdentity(rung, anytimeDigests[index])));
            }
            Task.WaitAll(tasks);
            CortexForkRunReceipt<TOutcome>[] receipts = tasks.Select(static task => task.GetAwaiter().GetResult()).ToArray();
            if (rung > 0)
                relation = new CortexForkNSeedRelation(
                    rung, CortexForkSeedRelations.PerArmContinuation,
                    seed.ColdSeedDigest,
                    receipts.Select(static receipt => receipt.SeedLoad.PreparedSeedDigest).ToArray(),
                    receipts.Select(static receipt => receipt.SeedLoad.PreparationRole).ToArray(),
                    receipts.Select(static receipt => receipt.SeedLoad.AncestorSeedDigest).ToArray(),
                    rungArms.Select(static arm => arm.PreparationRole).ToArray());
            result.Add(new CortexMatchedForkNReceipt<TOutcome>(receipts, relation));
            // The caller may judge each landed rung and throw to stop the ladder early — a doomed
            // trial that can no longer change its terminal verdict need not run the remaining rungs.
            inspectAfterRung?.Invoke(result);
            for (int arm = 0; arm < receipts.Length; arm++)
            {
                digests[arm] = receipts[arm].FinalDigests;
                steps[arm] = receipts[arm].StepSpan.ActualNextStep;
                anytimeDigests[arm] = receipts[arm].AnytimeCurveDigest;
            }
        }
        return result;
    }

    private static (CortexForkSeed Prepared, CortexForkSeedLoadReceipt Load) PrepareArmSeed<TOutcome>(
        CortexForkSeed ancestorSeed,
        CortexForkArm<TOutcome> arm,
        string childDirectory,
        HashSet<Cortex> reservedWorlds)
    {
        string? childrenDirectory = Path.GetDirectoryName(childDirectory);
        if (childrenDirectory is null)
            throw new InvalidDataException("arm seed preparation requires a child directory");
        string stageDirectory = Path.Combine(childrenDirectory,
            $".seed-preparation-{Path.GetFileName(childDirectory)}-{Guid.NewGuid():N}");
        try
        {
            // The stage is an immediate sibling of the marker-only child.  This
            // keeps preparation custody separate from the caller's materialized
            // directory, which is deliberately not overwritten.
            WriteSeedDirectory(ancestorSeed, stageDirectory);
            Cortex preparer = arm.CreateCortex() ?? throw new InvalidOperationException("arm seed preparer factory returned null");
            if (!reservedWorlds.Add(preparer))
                throw new InvalidOperationException("arm seed preparation factory returned an aliased Cortex object graph");
            preparer.BindForkRailRole(arm.RailRole);
            preparer.DisableAutonomicSpawning();
            CortexForkExecutionReceipt preparation = preparer.RunMaterializedFork(
                stageDirectory,
                ancestorSeed.NextStep,
                arm.InterveneAfterLoad,
                CortexForkCompletionModes.ExactAbsoluteStep,
                executionWindow: new CortexExecutionWindow(ancestorSeed.NextStep, ancestorSeed.NextStep),
                expectedPersistedConfigDigest: ancestorSeed.PersistedConfigDigest,
                prepareOnly: true);
            if (preparation.ExitCode != 0)
                throw new InvalidDataException($"arm seed preparation failed for {Path.GetFileName(childDirectory)}");
            CortexForkSeed prepared = CortexForkSeed.MaterializeRun(stageDirectory, ancestorSeed.NextStep);
            if (string.IsNullOrWhiteSpace(prepared.PersistedConfigDigest)
                || !string.Equals(prepared.PersistedConfigDigest, ancestorSeed.PersistedConfigDigest, StringComparison.Ordinal))
                throw new InvalidDataException("arm seed preparation changed the persisted config epoch");
            // The prepared seed now REFERENCES the stage's grammar bins rather than holding them in RAM, so the
            // child write must happen here — before the finally tears the stage down. Later uses of `prepared`
            // (receipt binding, shared-checkpoint proof) read the stable child dir, never these references again.
            CortexForkSeedLoadReceipt load = WriteSeedDirectory(prepared, childDirectory, arm.MaterializationContract);
            return (prepared, load);
        }
        finally
        {
            if (Directory.Exists(stageDirectory))
                Directory.Delete(stageDirectory, recursive: true);
        }
    }

    private static (CortexForkRunReceipt<TOutcome> Left, CortexForkRunReceipt<TOutcome> Right) RunArmsIndependently<TOutcome>(
        int leftSeedStep,
        CortexForkDigests leftStartingDigests,
        CortexForkSeedLoadReceipt leftSeedLoad,
        string leftDirectory,
        CortexForkArm<TOutcome> left,
        int rightSeedStep,
        CortexForkDigests rightStartingDigests,
        CortexForkSeedLoadReceipt rightSeedLoad,
        string rightDirectory,
        CortexForkArm<TOutcome> right,
        int absoluteHorizon,
        bool verifyTerminal,
        int rung,
        string leftParentPointID,
        string rightParentPointID,
        HashSet<Cortex>? reservedWorlds = null)
    {
        Cortex leftCortex = left.CreateCortex() ?? throw new InvalidOperationException("left fork factory returned null");
        Cortex rightCortex = right.CreateCortex() ?? throw new InvalidOperationException("right fork factory returned null");
        if (ReferenceEquals(leftCortex, rightCortex)
            || reservedWorlds is not null && (!reservedWorlds.Add(leftCortex) || !reservedWorlds.Add(rightCortex)))
            throw new InvalidOperationException("matched fork arms require independent Cortex object graphs");
        // Each arm owns a fresh Cortex graph and run directory. Execute both worlds concurrently, then read them
        // in left/right order so completion scheduling cannot perturb the receipt order.
        Task<CortexForkRunReceipt<TOutcome>> leftTask = Task.Run(() => RunArm(
            leftStartingDigests, leftSeedLoad, leftSeedStep, leftDirectory, left, leftCortex, absoluteHorizon, verifyTerminal,
            applyInitialIntervention: rung == 0,
            left.ResolveAnytimeIdentity(rung, leftParentPointID)));
        Task<CortexForkRunReceipt<TOutcome>> rightTask = Task.Run(() => RunArm(
            rightStartingDigests, rightSeedLoad, rightSeedStep, rightDirectory, right, rightCortex, absoluteHorizon, verifyTerminal,
            applyInitialIntervention: rung == 0,
            right.ResolveAnytimeIdentity(rung, rightParentPointID)));
        Task.WaitAll(leftTask, rightTask);
        return (leftTask.GetAwaiter().GetResult(), rightTask.GetAwaiter().GetResult());
    }

    private static CheckpointRoundTripProof VerifySharedCheckpointProof<TOutcome>(
        CortexForkSeed seed, CortexForkArm<TOutcome> arm, string runDirectory,
        HashSet<Cortex>? reservedWorlds = null)
    {
        Cortex verifier = arm.CreateCortex() ?? throw new InvalidOperationException("shared checkpoint verifier factory returned null");
        if (reservedWorlds is not null && !reservedWorlds.Add(verifier))
            throw new InvalidOperationException("shared checkpoint verifier factory returned an aliased Cortex object graph");
        verifier.BindForkRailRole(arm.RailRole);
        verifier.DisableAutonomicSpawning();
        if (verifier.VerifyMaterializedFork(runDirectory) != 0)
            throw new InvalidDataException($"shared cold-seed checkpoint SaveLoadSave failed: {runDirectory}");
        CheckpointRoundTripProof proof = Checkpoint.ReadImageProof(
            runDirectory, seed.PersistedConfigDigest, seed.NextStep, saveLoadSaveExact: true);
        if (!proof.IsBound || !string.Equals(proof.EffectiveImageSHA256, seed.Digests.CheckpointSHA256, StringComparison.Ordinal)
            || !string.Equals(proof.PersistedConfigDigest, seed.PersistedConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException("shared checkpoint proof disagrees with the cold seed authority");
        Trace.Cortex.Boundary("fork.checkpoint-proof",
            $"mode=shared exact=yes image={proof.EffectiveImageSHA256} base={proof.BasePhysicalSHA256} chain={proof.PhysicalChainSHA256} step={proof.NextStep}");
        return proof;
    }

    private static CortexForkRunReceipt<TOutcome> RunArm<TOutcome>(
        CortexForkDigests startingDigests,
        CortexForkSeedLoadReceipt seedLoad,
        int seedStep,
        string runDirectory,
        CortexForkArm<TOutcome> arm,
        Cortex cortex,
        int absoluteHorizon,
        bool verifyTerminal,
        bool applyInitialIntervention,
        CortexForkAnytimeIdentity anytimeIdentity)
    {
        // One runtime Cortex graph drives the arm. RunMaterializedFork verifies the loaded seed inside that same
        // World; only the terminal rung allocates an independent reload verifier for final checkpoint exactness.
        string anytimeCurveDigest = "";
        TOutcome outcome = default!;
        bool completionCaptured = false;
        void CaptureCompletion(Cortex runtime)
        {
            outcome = arm.ReadCompletion(runtime);
            completionCaptured = true;
        }
        cortex.BindForkRailRole(arm.RailRole);
        cortex.DisableAutonomicSpawning();
        // Land the seed-load authority before the long-running child drive.
        // A kill during execution or terminal verification must leave enough
        // binding evidence for a resume decision; the final write below only
        // fills in the measured runtime-bind wall.
        WriteSeedLoadReceipt(runDirectory, "seed-load-intent.ron", in seedLoad, runtimeBindWallMilliseconds: 0);
        string seedLoadIntentDigest = ComputeFileSHA256(Path.Combine(runDirectory, "seed-load-intent.ron"));
        // A ladder continuation loads the prior rung's terminal state. Replaying the fork-point
        // intervention here would reset the experiment and break per-arm causal continuity.
        CortexForkExecutionReceipt execution = cortex.RunMaterializedFork(
            runDirectory, absoluteHorizon, applyInitialIntervention ? arm.InterveneAfterLoad : null, arm.CompletionMode, anytimeIdentity,
            arm.AfterRuntimeBind, new CortexExecutionWindow(seedStep, absoluteHorizon),
            seedLoad.PersistedConfigDigest,
            checkpointProof: seedLoad.CheckpointProof.IsBound ? seedLoad.CheckpointProof : null,
            afterCompletedStep: CaptureAnytimeAfter(arm.AfterCompletedStep),
            afterCompletedStepEveryStep: CaptureAnytimeAfter(arm.AfterCompletedStepEveryStep),
            beforeCompletedStep: arm.BeforeCompletedStep,
            captureCompletionBeforeWorldDispose: CaptureCompletion);
        int exitCode = execution.ExitCode;
        CortexForkDigests finalDigests = ReadRunDigests(runDirectory);
        if (exitCode == 0 && arm.CompletionMode == CortexForkCompletionModes.ExactAbsoluteStep
            && cortex.Step + 1 != absoluteHorizon)
            throw new InvalidOperationException($"fork arm {Path.GetFileName(runDirectory)} stopped at step {cortex.Step + 1}, before absolute horizon {absoluteHorizon}");
        bool checkpointExact = exitCode == 0;
        long terminalVerifierWallMilliseconds = 0;
        long terminalVerifierRawTicks = 0;
        string terminalOccurrenceCheckReceiptDigest = "";
        if (verifyTerminal)
        {
            long verifierStarted = Stopwatch.GetTimestamp();
            Cortex finalVerifier = arm.CreateCortex() ?? throw new InvalidOperationException("final fork verifier factory returned null");
            if (ReferenceEquals(cortex, finalVerifier))
                throw new InvalidOperationException("terminal fork verification requires an independent Cortex object graph");
            finalVerifier.BindForkRailRole(arm.RailRole);
            finalVerifier.DisableAutonomicSpawning();
            checkpointExact &= finalVerifier.VerifyMaterializedFork(runDirectory) == 0;
            terminalVerifierRawTicks = Math.Max(1, Stopwatch.GetTimestamp() - verifierStarted);
            terminalVerifierWallMilliseconds = Math.Max(0, Stopwatch.GetElapsedTime(verifierStarted).Ticks / TimeSpan.TicksPerMillisecond);
            finalDigests = ReadRunDigests(runDirectory);
        }
        if (!completionCaptured)
            throw new InvalidOperationException($"fork arm {Path.GetFileName(runDirectory)} did not capture its completion before runtime disposal");
        if (exitCode == 0 && arm.CompletionMode == CortexForkCompletionModes.RuntimeStop)
        {
            if (!execution.RuntimeStopRequested)
                throw new InvalidOperationException($"fork arm {Path.GetFileName(runDirectory)} reached safety horizon {absoluteHorizon} without requesting completion");
            if (!arm.IsCompletionSatisfied!(outcome))
                throw new InvalidOperationException($"fork arm {Path.GetFileName(runDirectory)} requested completion at step {cortex.Step + 1} without satisfying its completion predicate");
        }
        int actualFinalStep = cortex.Step + 1;
        if (anytimeCurveDigest.Length == 0)
            anytimeCurveDigest = ReadSpawningAnytimeDigest(cortex);
        CortexForkStepSpan stepSpan = new(seedStep, absoluteHorizon, actualFinalStep);
        CortexForkRunTiming timing = new(
            seedLoad.SeedIOWallMilliseconds,
            execution.ExecutionWallMilliseconds,
            terminalVerifierWallMilliseconds,
            SeedLoadChecks: 1,
            TerminalVerifierChecks: verifyTerminal ? 1 : 0,
            RuntimeBindWallMilliseconds: execution.RuntimeBindWallMilliseconds,
            SeedIORawTicks: seedLoad.SeedIORawTicks,
            ExecutionRawTicks: execution.ExecutionRawTicks,
            TerminalVerifierRawTicks: terminalVerifierRawTicks,
            RuntimeBindRawTicks: execution.RuntimeBindRawTicks,
            CheckpointProofReuses: seedLoad.CheckpointProofReused ? 1 : 0,
            CheckpointProofMisses: seedLoad.CheckpointProofReused ? 0 : 1);
        if (verifyTerminal)
        {
            CortexForkTerminalOccurrenceCheckReceipt terminal = CortexForkTerminalOccurrenceCheckReceipt.Create(
                Path.GetFileName(runDirectory), seedLoad.ColdSeedDigest, in finalDigests,
                terminalVerifierWallMilliseconds, checkpointExact, in seedLoad, in stepSpan, in timing);
            Run.Open(runDirectory).WriteAtomic("terminal-verification.ron", stream =>
            {
                byte[] bytes = RonSerializer.SerializeToUtf8(in terminal);
                stream.Write(bytes);
            });
            terminalOccurrenceCheckReceiptDigest = ComputeFileSHA256(Path.Combine(runDirectory, "terminal-verification.ron"));
        }
        WriteSeedLoadReceipt(runDirectory, "seed-load-receipt.ron", in seedLoad, execution.RuntimeBindWallMilliseconds);
        string seedLoadReceiptDigest = ComputeFileSHA256(Path.Combine(runDirectory, "seed-load-receipt.ron"));
        CortexForkTerminalRunReceipt durable = CortexForkTerminalRunReceipt.Create(
            in seedLoad, in startingDigests, in finalDigests, in stepSpan, in timing, exitCode, execution.RuntimeStopRequested, checkpointExact,
            verifyTerminal, verifyTerminal && checkpointExact, seedLoadIntentDigest, seedLoadReceiptDigest,
            terminalOccurrenceCheckReceiptDigest, anytimeCurveDigest);
        durable.WriteAppendSafe(runDirectory);
        CortexForkRunReceipt<TOutcome> recovered = CortexForkTerminalRunReceipt.RecoverForRun(runDirectory, outcome, anytimeCurveDigest);
        if (recovered.ExitCode != exitCode || recovered.TerminalCheckpointExact != checkpointExact
            || recovered.StepSpan != stepSpan || recovered.FinalDigests.CheckpointSHA256 != finalDigests.CheckpointSHA256
            || recovered.FinalDigests.TapeSpanlogSHA256 != finalDigests.TapeSpanlogSHA256
            || recovered.FinalDigests.CurveSHA256 != finalDigests.CurveSHA256
            || recovered.Timing != timing)
            throw new InvalidDataException($"fork arm {Path.GetFileName(runDirectory)} in-memory receipt disagrees with durable terminal receipt");
        // RecoverForRun above already ran the full ValidateAgainstFiles pass over
        // the durable receipts this same call just wrote. Authority capture here
        // only fingerprints those files for the landing-hook mutation check, so
        // it takes the four-SHA256 path; cold recovery (Recover / cross-process
        // Read) keeps the full re-validation.
        CortexForkAuthorityChain authorityBefore = CaptureAuthorityChainFiles(runDirectory);
        if (exitCode == 0 && checkpointExact)
        {
            bool callbackAttempted = arm.PersistCompletionBeforeLanding is not null || arm.AfterRunLanded is not null;
            Exception? callbackError = null;
            long callbackStarted = Stopwatch.GetTimestamp();
            try
            {
                arm.PersistCompletionBeforeLanding?.Invoke(cortex, seedLoad, finalDigests, outcome);
                arm.AfterRunLanded?.Invoke(cortex, seedLoad, finalDigests);
            }
            catch (Exception error) { callbackError = error; }
            long callbackRawTicks = callbackAttempted
                ? Math.Max(1, Stopwatch.GetTimestamp() - callbackStarted)
                : 0;
            long callbackWallMilliseconds = callbackAttempted
                ? Math.Max(0, Stopwatch.GetElapsedTime(callbackStarted).Ticks / TimeSpan.TicksPerMillisecond)
                : 0;
            CortexForkAuthorityChain authorityAfter = CaptureAuthorityChainFiles(runDirectory);
            bool authorityChainExact = authorityAfter == authorityBefore;
            CortexForkLandingOutcomeStates state = !callbackAttempted
                ? CortexForkLandingOutcomeStates.TerminalChildExact
                : callbackError is null
                    ? CortexForkLandingOutcomeStates.Completed
                    : CortexForkLandingOutcomeStates.CallbackFailed;
            CortexForkLandingOutcomeReceipt landing = CortexForkLandingOutcomeReceipt.Create(
                runDirectory, state, callbackAttempted, callbackAttempted && callbackError is null, callbackError,
                callbackWallMilliseconds, callbackRawTicks, in durable, in authorityBefore, in authorityAfter,
                authorityChainExact);
            landing.WriteAppendSafe(runDirectory);
            if (!authorityChainExact)
                throw new InvalidDataException($"fork arm {Path.GetFileName(runDirectory)} landing hook mutated durable authority chain");
            landing.ValidateAgainstFiles(runDirectory);
            if (callbackError is not null)
                ExceptionDispatchInfo.Capture(callbackError).Throw();
        }
        return recovered;

        static CortexForkAuthorityChain CaptureAuthorityChainFiles(string directory)
            => new(
                ComputeFileSHA256(Path.Combine(directory, "seed-load-intent.ron")),
                ComputeFileSHA256(Path.Combine(directory, "seed-load-receipt.ron")),
                File.Exists(Path.Combine(directory, "terminal-verification.ron"))
                    ? ComputeFileSHA256(Path.Combine(directory, "terminal-verification.ron"))
                    : "",
                ComputeFileSHA256(Path.Combine(directory, CortexForkTerminalRunReceipt.FileName)));

        static void WriteSeedLoadReceipt(string directory, string file, in CortexForkSeedLoadReceipt receipt, long runtimeBindWallMilliseconds)
        {
            CortexForkSeedLoadRailReceipt document = CortexForkSeedLoadRailReceipt.FromReceipt(receipt, runtimeBindWallMilliseconds);
            Run.Open(directory).WriteAtomic(file, stream =>
            {
                byte[] bytes = document.Encode();
                stream.Write(bytes);
            });
        }

        Action<Cortex, int>? CaptureAnytimeAfter(Action<Cortex, int>? callback)
            => callback is null
                ? CaptureAnytime
                : (runtime, completedStep) =>
                {
                    callback(runtime, completedStep);
                    CaptureAnytime(runtime, completedStep);
                };

        void CaptureAnytime(Cortex runtime, int _)
            => anytimeCurveDigest = ReadSpawningAnytimeDigest(runtime);
    }

    private static string ReadSpawningAnytimeDigest(Cortex cortex)
        => cortex.MountedCurriculum is ReplayCalc dream ? dream.AnytimeCurve.Digest : "";

    private static CortexForkSeedLoadReceipt CopyRunState(string sourceDirectory, string destinationDirectory, in CortexForkDigests expected,
        bool validateCheckpointProof = true)
    {
        long started = Stopwatch.GetTimestamp();
        string sourceFull = Path.GetFullPath(sourceDirectory);
        string destinationFull = Path.GetFullPath(destinationDirectory);
        lock (Run.CheckpointWriteGate(sourceFull))
        {
            CortexForkDigests source = ReadRunDigests(sourceFull);
            if (!DigestsEqual(source, expected))
                throw new InvalidDataException($"fork milestone changed before copying {sourceFull}");

            Directory.CreateDirectory(destinationFull);
            File.Copy(Path.Combine(sourceFull, Checkpoint.FileName), Path.Combine(destinationFull, Checkpoint.FileName), overwrite: true);
            string sourceDelta = Path.Combine(sourceFull, Checkpoint.DeltaFileName);
            string destinationDelta = Path.Combine(destinationFull, Checkpoint.DeltaFileName);
            if (File.Exists(sourceDelta))
                File.Copy(sourceDelta, destinationDelta, overwrite: true);
            else if (File.Exists(destinationDelta))
                File.Delete(destinationDelta);
            string sourceDeltaTail = Path.Combine(sourceFull, Checkpoint.DeltaTailFileName);
            string destinationDeltaTail = Path.Combine(destinationFull, Checkpoint.DeltaTailFileName);
            if (File.Exists(sourceDeltaTail))
                File.Copy(sourceDeltaTail, destinationDeltaTail, overwrite: true);
            else if (File.Exists(destinationDeltaTail))
                File.Delete(destinationDeltaTail);
            string copiedTapeSHA256 = CopyFileHashed(Path.Combine(sourceFull, "tape.spanlog"), Path.Combine(destinationFull, "tape.spanlog"));
            string copiedCurveSHA256 = CopyFileHashed(Path.Combine(sourceFull, "curve.tsv"), Path.Combine(destinationFull, "curve.tsv"));
            string sourceExcursions = Path.Combine(sourceFull, "excursions.txt");
            string destinationExcursions = Path.Combine(destinationFull, "excursions.txt");
            string copiedExcursionsSHA256 = "";
            if (File.Exists(sourceExcursions))
                copiedExcursionsSHA256 = CopyFileHashed(sourceExcursions, destinationExcursions);
            else if (File.Exists(destinationExcursions))
                File.Delete(destinationExcursions);
            // journal.log carries the journal's SHED prefix — the keyframe records only its horizon, so a
            // child without the file cannot splice its journal on resume.
            string sourceJournalLog = Path.Combine(sourceFull, "journal.log");
            string destinationJournalLog = Path.Combine(destinationFull, "journal.log");
            if (File.Exists(sourceJournalLog))
                CopyFileHashed(sourceJournalLog, destinationJournalLog);
            else if (File.Exists(destinationJournalLog))
                File.Delete(destinationJournalLog);
            foreach (string name in Cortex.PolicyJournalFileNames)
            {
                string sourceJournal = Path.Combine(sourceFull, name);
                string destinationJournal = Path.Combine(destinationFull, name);
                if (File.Exists(sourceJournal))
                    CopyFileHashed(sourceJournal, destinationJournal);
                else if (File.Exists(destinationJournal))
                    File.Delete(destinationJournal);
            }
            string sourceSchedule = Path.Combine(sourceFull, EmlPairedFuelSchedule.SidecarFile);
            string destinationSchedule = Path.Combine(destinationFull, EmlPairedFuelSchedule.SidecarFile);
            if (File.Exists(sourceSchedule))
                CopyFileHashed(sourceSchedule, destinationSchedule);
            else if (File.Exists(destinationSchedule))
                File.Delete(destinationSchedule);

            // The checkpoint stores the grammar revision identity, while the revision bytes
            // live beside it.  A continuation/retry must carry both; copying only the keyframe
            // leaves a child that cannot materialize its effective image on the next generation.
            HashSet<string> sourceGrammarArtifacts = new(StringComparer.Ordinal);
            foreach (string sourceGrammar in Directory.EnumerateFiles(sourceFull, "grammar-revision-*.bin"))
            {
                string name = Path.GetFileName(sourceGrammar);
                sourceGrammarArtifacts.Add(name);
                CopyFileHashed(sourceGrammar, Path.Combine(destinationFull, name));
            }
            foreach (string destinationGrammar in Directory.EnumerateFiles(destinationFull, "grammar-revision-*.bin"))
                if (!sourceGrammarArtifacts.Contains(Path.GetFileName(destinationGrammar)))
                    File.Delete(destinationGrammar);

            // The destination now owns the copied mutation rail, but its
            // adoption corroboration is written by BindSeedLoadReceipt immediately
            // after this byte-copy. Reading the effective image here would
            // replay an inherited prefix before that corroboration exists. The
            // source digest was checked under its write gate above; the copy
            // itself MEASURED each stream artifact's digest from the bytes
            // that flowed, so compare those and carry the exact digest
            // forward to the corroboration binder.
            CortexForkDigests copied = expected;
            if (copiedTapeSHA256 != expected.TapeSpanlogSHA256
                || copiedCurveSHA256 != expected.CurveSHA256
                || (expected.ExcursionsSHA256.Length > 0 && copiedExcursionsSHA256 != expected.ExcursionsSHA256))
                throw new InvalidDataException($"fork milestone changed while copying {destinationFull}");
            CheckpointRoundTripProof destinationProof = default;
            if (validateCheckpointProof)
            {
                CheckpointRoundTripProof sourceProof = Checkpoint.ReadImageProof(
                    sourceFull, Cortex.PersistedConfigDigest(Checkpoint.PeekConfig(sourceFull)),
                    Checkpoint.PeekNextStep(sourceFull), saveLoadSaveExact: false);
                // The destination cannot replay its inherited rail until the
                // binder persists its adoption corroboration. The source proof is
                // byte-bound to the copied checkpoint image, so carry it
                // across the locked byte-copy and let RunMaterializedFork
                // revalidate it after the corroboration lands.
                destinationProof = sourceProof;
            }
            CortexForkSeedLoadReceipt receipt = new(
                expected,
                copied,
                Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond,
                SeedIORawTicks: Math.Max(1, Stopwatch.GetTimestamp() - started),
                CheckpointProof: destinationProof);
            if (!receipt.Exact)
                throw new InvalidDataException($"fork milestone changed while copying {destinationFull}");
            return receipt;
        }
    }

    /// Copy while hashing the stream — the digest is MEASURED from the bytes that actually flowed
    /// (source-read side), never asserted from the caller's expectation; one pass, no re-read.
    private static string CopyFileHashed(string sourcePath, string destinationPath)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using FileStream source = File.OpenRead(sourcePath);
        using FileStream destination = File.Create(destinationPath);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                destination.Write(buffer, 0, read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static CortexForkDigests ReadRunDigests(string runDirectory)
    {
        string directory = Path.GetFullPath(runDirectory);
        string excursions = Path.Combine(directory, "excursions.txt");
        return new(
            Checkpoint.LogicalStateSHA256(Checkpoint.LoadEffectiveImage(directory)),
            ComputeFileSHA256(Path.Combine(directory, "tape.spanlog")),
            ComputeFileSHA256(Path.Combine(directory, "curve.tsv")),
            File.Exists(excursions) ? ComputeFileSHA256(excursions) : "");
    }

    private static CortexForkSeedLoadReceipt BindSeedLoadReceipt<TOutcome>(
        CortexForkSeedLoadReceipt receipt,
        Cortex spawningCortex,
        CortexForkArm<TOutcome> arm,
        CortexForkSeed seed,
        CortexExecutionWindow window,
        string childDirectory,
        string sourceRunDirectory = "",
        CortexForkDigests? sourceDigests = null,
        int sourceNextStep = -1,
        CortexForkSeed? preparedSeed = null)
    {
        bool hasExplicitSource = !string.IsNullOrWhiteSpace(sourceRunDirectory);
        string parentID = arm.ParentRunID;
        string parentDirectory = "";
        try { parentDirectory = Path.GetFullPath(spawningCortex.CurrentRun.Dir); }
        catch (InvalidOperationException)
        {
            if (arm.RailRole != CortexForkRailRoles.Unknown)
                throw new InvalidOperationException("a bound rail requires a spawning Cortex with an active parent run");
        }
        string actualParentID = parentDirectory.Length == 0 ? "" : Path.GetFileName(parentDirectory);
        if (actualParentID.Length > 0)
        {
            if (!string.IsNullOrWhiteSpace(parentID) && !string.Equals(parentID, actualParentID, StringComparison.Ordinal))
                throw new InvalidDataException($"fork rail parent id {parentID} does not match active parent {actualParentID}");
            parentID = actualParentID;
        }
        if (arm.RailRole != CortexForkRailRoles.Unknown)
        {
            string expectedRoot = Path.Combine(parentDirectory, "children");
            string childFull = Path.GetFullPath(childDirectory);
            if (!string.Equals(Path.GetDirectoryName(childFull), expectedRoot, StringComparison.Ordinal))
                throw new InvalidDataException($"fork child {childFull} must be an immediate child of {expectedRoot}");
            string rolePrefix = RailRoleToken(arm.RailRole) + "_";
            string childName = Path.GetFileName(childDirectory);
            if (!childName.StartsWith(rolePrefix, StringComparison.Ordinal)
                || childName.Length == rolePrefix.Length
                || !childName[rolePrefix.Length..].All(char.IsAsciiDigit))
                throw new InvalidDataException($"fork child {Path.GetFileName(childDirectory)} does not match rail role {arm.RailRole}");
        }
        string configDigest = seed.PersistedConfigDigest;
        if (string.IsNullOrWhiteSpace(configDigest))
            configDigest = Cortex.PersistedConfigDigest(Checkpoint.PeekConfig(childDirectory));
        string coldSeedDigest = seed.ColdSeedDigest;
        if (string.IsNullOrWhiteSpace(seed.PersistedConfigDigest))
            coldSeedDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
                seed.Digests.CheckpointSHA256, seed.Digests.TapeSpanlogSHA256, seed.Digests.CurveSHA256, seed.Digests.ExcursionsSHA256, configDigest))));
        CortexForkDigests source = sourceDigests ?? seed.Digests;
        if (preparedSeed is not null && !DigestsEqual(receipt.ExpectedDigests, preparedSeed.Digests))
            throw new InvalidDataException("fork prepared seed digest disagrees with its materialized child");
        string ancestorSeedDigest = seed.ColdSeedDigest;
        string preparedSeedDigest = preparedSeed?.ColdSeedDigest ?? seed.ColdSeedDigest;
        CortexForkPreparationRoles preparationRole = arm.PreparationRole;
        CortexForkPreparationRoles railPreparationRole = arm.RailRole switch
        {
            CortexForkRailRoles.Baseline => CortexForkPreparationRoles.Baseline,
            CortexForkRailRoles.Candidate => CortexForkPreparationRoles.Candidate,
            CortexForkRailRoles.ForcedNull => CortexForkPreparationRoles.ForcedNull,
            CortexForkRailRoles.ReflexFrozen => CortexForkPreparationRoles.ReflexFrozen,
            _ => CortexForkPreparationRoles.Unknown,
        };
        if (railPreparationRole != CortexForkPreparationRoles.Unknown && preparationRole != railPreparationRole)
            throw new InvalidDataException("fork preparation role does not match its rail role");
        if (hasExplicitSource)
        {
            string sourceFull = Path.GetFullPath(sourceRunDirectory);
            CortexForkDigests actual = ReadRunDigests(sourceFull);
            if (!DigestsEqual(actual, source))
                throw new InvalidDataException($"fork continuation source changed before copy: {sourceFull}");
            // A continuation carries the source rail's immutable config epoch,
            // not the original cold seed's epoch. Child-written records bind
            // their own later epoch; copied records must retain this source
            // authority so mixed epochs remain verifiable after adoption.
            configDigest = Cortex.PersistedConfigDigest(Checkpoint.PeekConfig(sourceFull));
        }
        string sourceID = hasExplicitSource
            ? Path.GetFileName(Path.GetFullPath(sourceRunDirectory))
            : parentID;
        if (string.IsNullOrWhiteSpace(sourceID) && arm.RailRole != CortexForkRailRoles.Unknown)
            throw new InvalidOperationException("fork seed receipt requires a source run identity");
        sourceNextStep = sourceNextStep < 0 ? seed.NextStep : sourceNextStep;
        if (sourceNextStep < 0)
            throw new InvalidDataException("fork seed receipt source step cannot be negative");
        string sourceSeedDigest = sourceID.Length == 0 ? "" : ComputeSeedIdentity(source, configDigest);
        long excursionCursor = hasExplicitSource
            ? ReadExcursionCursor(sourceRunDirectory)
            : preparedSeed?.ExcursionCursor ?? seed.ExcursionCursor;
        CortexForkAdoptionHop[] adoptionAncestry = receipt.AdoptionAncestry ?? [];
        if (hasExplicitSource)
        {
            CortexForkSeedLoadReceipt sourceReceipt = ReadSeedLoadReceiptForAdoption(sourceRunDirectory);
            ancestorSeedDigest = string.IsNullOrWhiteSpace(sourceReceipt.AncestorSeedDigest)
                ? ancestorSeedDigest : sourceReceipt.AncestorSeedDigest;
            preparedSeedDigest = string.IsNullOrWhiteSpace(sourceReceipt.PreparedSeedDigest)
                ? preparedSeedDigest : sourceReceipt.PreparedSeedDigest;
            preparationRole = sourceReceipt.PreparationRole == CortexForkPreparationRoles.Unknown
                ? preparationRole : sourceReceipt.PreparationRole;
            CortexForkAdoptionHop hop = new(
                sourceID,
                Path.GetFileName(childDirectory),
                sourceNextStep,
                configDigest,
                receipt.CheckpointProof.BasePhysicalSHA256,
                sourceSeedDigest,
                sourceReceipt.BindingDigest);
            adoptionAncestry = [.. sourceReceipt.AdoptionAncestry ?? [], hop];
        }
        if (preparedSeed is not null)
        {
            string expectedPreparedSeedDigest = ComputeSeedIdentity(receipt.ExpectedDigests, configDigest);
            if (!string.Equals(preparedSeed.ColdSeedDigest, expectedPreparedSeedDigest, StringComparison.Ordinal))
                throw new InvalidDataException("fork prepared seed identity disagrees with its loaded digests");
        }
        else if (!hasExplicitSource && railPreparationRole != CortexForkPreparationRoles.Unknown)
        {
            string expectedPreparedSeedDigest = ComputeSeedIdentity(receipt.ExpectedDigests, configDigest);
            if (!string.Equals(preparedSeedDigest, expectedPreparedSeedDigest, StringComparison.Ordinal))
                throw new InvalidDataException("fork seed receipt prepared identity disagrees with its loaded digests");
        }
        if (railPreparationRole != CortexForkPreparationRoles.Unknown && preparationRole != railPreparationRole)
            throw new InvalidDataException("fork continuation preparation role does not match its rail role");
        if (preparationRole != CortexForkPreparationRoles.Unknown
            && (!IsForkDigest(ancestorSeedDigest) || !IsForkDigest(preparedSeedDigest)))
            throw new InvalidDataException("fork seed receipt preparation custody is incomplete");
        if (sourceID.Length > 0 && adoptionAncestry.Length == 0)
        {
            adoptionAncestry = [new CortexForkAdoptionHop(
                sourceID,
                Path.GetFileName(childDirectory),
                sourceNextStep,
                configDigest,
                receipt.CheckpointProof.BasePhysicalSHA256,
                sourceSeedDigest,
                coldSeedDigest)];
        }
        long loadedExcursionCursor = ReadExcursionCursor(childDirectory);
        if (loadedExcursionCursor != excursionCursor)
            throw new InvalidDataException($"fork seed receipt excursion cursor disagrees with loaded artifact: {childDirectory}");
        return receipt with
        {
            ParentRunID = parentID,
            ChildRunID = Path.GetFileName(childDirectory),
            Role = arm.RailRole,
            ColdSeedDigest = coldSeedDigest,
            PersistedConfigDigest = configDigest,
            ExecutionWindow = window,
            SourceSeedDigest = sourceSeedDigest,
            SourceRunID = sourceID,
            SourceNextStep = sourceNextStep,
            ExcursionCursor = excursionCursor,
            AdoptionAncestry = adoptionAncestry,
            AncestorSeedDigest = ancestorSeedDigest,
            PreparedSeedDigest = preparedSeedDigest,
            PreparationRole = preparationRole,
        };
    }

    private static CortexForkSeedLoadReceipt ReadSeedLoadReceiptForAdoption(string sourceRunDirectory)
    {
        string source = Path.GetFullPath(sourceRunDirectory);
        string path = Path.Combine(source, "seed-load-receipt.ron");
        if (!File.Exists(path)) path = Path.Combine(source, "seed-load-intent.ron");
        if (!File.Exists(path))
            throw new InvalidDataException($"fork continuation source has no adoption receipt: {source}");
        CortexForkSeedLoadRailDocument document = CortexForkTerminalRunReceipt.ReadSeedRailDocument(path);
        CortexForkSeedLoadReceipt receipt = document.Receipt;
        if (!IsForkDigest(document.StoredBindingDigest) || !receipt.Bound || !receipt.Exact)
            throw new InvalidDataException($"fork continuation source adoption receipt is not bound: {source}");
        return receipt;
    }

    private static long ReadExcursionCursor(string runDirectory)
    {
        string path = Path.Combine(Path.GetFullPath(runDirectory), "excursions.txt");
        if (!File.Exists(path)) return 0;
        return CortexForkSeed.CountRows(File.ReadAllBytes(path));
    }

    internal static string ComputeSeedIdentity(in CortexForkDigests digests, string configDigest)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            digests.CheckpointSHA256, digests.TapeSpanlogSHA256, digests.CurveSHA256, digests.ExcursionsSHA256, configDigest))));

    private static bool IsForkDigest(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void ValidateContinuationReceipt(
        in CortexForkSeedLoadReceipt receipt,
        string sourceRunDirectory,
        int sourceNextStep,
        in CortexForkDigests sourceDigests)
    {
        if (!string.Equals(receipt.SourceRunID, Path.GetFileName(Path.GetFullPath(sourceRunDirectory)), StringComparison.Ordinal)
            || receipt.SourceNextStep != sourceNextStep
            || !string.Equals(receipt.SourceSeedDigest,
                ComputeSeedIdentity(sourceDigests, receipt.PersistedConfigDigest), StringComparison.Ordinal))
            throw new InvalidDataException($"fork continuation receipt does not bind prior final state: {sourceRunDirectory}");
        CheckpointRoundTripProof sourceProof = Checkpoint.ReadImageProof(
            sourceRunDirectory, receipt.PersistedConfigDigest, sourceNextStep, receipt.CheckpointProof.SaveLoadSaveExact);
        if (!sourceProof.Matches(receipt.CheckpointProof))
            throw new InvalidDataException($"fork continuation checkpoint proof does not bind prior final state: {sourceRunDirectory}");
    }

    private static string RailRoleToken(CortexForkRailRoles role)
        => role switch
        {
            CortexForkRailRoles.ForcedNull => "forced-null",
            CortexForkRailRoles.ReflexFrozen => "reflex-frozen",
            _ => role.ToString().ToLowerInvariant(),
        };

    private static CortexForkSeedLoadReceipt WriteSeedDirectory(CortexForkSeed seed, string runDirectory, CortexForkMaterializationContract? materializationContract = null)
    {
        long started = Stopwatch.GetTimestamp();
        seed.WriteRunDirectory(runDirectory, materializationContract);
        // WriteRunDirectory lands the seed's own buffers through WriteAtomic
        // (durable flush + rename, or throw), so the seed's ctor-time digests
        // ARE the digests of the bytes on disk — re-reading the directory here
        // re-hashed our own write. The proof still decode-validates the image
        // (config digest + next-step), and the child World independently
        // re-verifies the loaded seed from disk in RunMaterializedFork.
        CheckpointRoundTripProof proof = seed.ProveCheckpointImage();
        if (!seed.CheckpointProof.Equals(default) && !seed.CheckpointProof.Matches(proof))
            throw new InvalidDataException($"fork seed checkpoint proof changed while writing {runDirectory}");
        CortexForkSeedLoadReceipt receipt = new(
            seed.Digests,
            seed.Digests,
            Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond,
            SeedIORawTicks: Math.Max(1, Stopwatch.GetTimestamp() - started),
            CheckpointProof: proof);
        if (!receipt.Exact)
            throw new InvalidDataException($"fork seed changed while writing {runDirectory}");
        return receipt;
    }

    private static CortexForkSeedRelation CreateSeedRelation(
        int rung,
        CortexForkSeedRelations kind,
        in CortexForkDigests expectedLeft,
        in CortexForkDigests actualLeft,
        in CortexForkDigests expectedRight,
        in CortexForkDigests actualRight)
    {
        bool initialMatch = kind == CortexForkSeedRelations.InitialCrossArm
            ? DigestsEqual(expectedLeft, expectedRight)
            : false;
        return new CortexForkSeedRelation(
            rung,
            kind,
            expectedLeft,
            actualLeft,
            expectedRight,
            actualRight,
            kind == CortexForkSeedRelations.InitialCrossArm ? initialMatch : null);
    }

    private static bool DigestsEqual(in CortexForkDigests left, in CortexForkDigests right)
        => string.Equals(left.CheckpointSHA256, right.CheckpointSHA256, StringComparison.Ordinal)
           && string.Equals(left.TapeSpanlogSHA256, right.TapeSpanlogSHA256, StringComparison.Ordinal)
           && string.Equals(left.CurveSHA256, right.CurveSHA256, StringComparison.Ordinal)
           && string.Equals(left.ExcursionsSHA256, right.ExcursionsSHA256, StringComparison.Ordinal);

    private static CortexForkRunReceipt<int> CreateRegressionRun(
        in CortexForkDigests seed,
        in CortexForkDigests final,
        in CortexForkSeedLoadReceipt seedLoad,
        int seedNextStep,
        int plannedNextStep)
        => new(
            "<receipt-regression>",
            seed,
            final,
            seedLoad,
            new CortexForkStepSpan(seedNextStep, plannedNextStep, plannedNextStep),
            new CortexForkRunTiming(seedLoad.SeedIOWallMilliseconds, 0, 0, 1, 1),
            0,
            terminalCheckpointExact: true,
            outcome: 0);

    private static CortexForkDigests CreateRegressionDigests(string label)
        => new($"{label}:checkpoint", $"{label}:tape", $"{label}:curve");


    private static string ComputeFileSHA256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
