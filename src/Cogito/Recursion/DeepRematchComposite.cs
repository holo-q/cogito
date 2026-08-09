namespace Cogito;

/// Persisted composite receipt value types and exclusive wall-accounting validation.

internal readonly record struct DeepRematchParentReceipt(
    string RunID,
    string RunDirectory,
    int ExitCode,
    string ColdSeedDigest,
    int ColdSeedNextStep,
    int ColdSeedBuildCount,
    long WallMilliseconds,
    string GatePath = "",
    string GateDigest = "");

internal readonly record struct DeepRematchColdSeedReceipt(
    string ParentRunID,
    string ColdSeedDigest,
    int NextStep,
    CortexForkDigests Digests,
    string PersistedConfigDigest,
    long BuildWallMilliseconds);

internal readonly record struct DeepRematchChildCopyReceipt(
    string ParentRunID,
    string ChildRunID,
    CortexForkRailRoles Role,
    string ColdSeedDigest,
    CortexForkSeedLoadReceipt SeedLoad,
    long SeedIOWallMilliseconds,
    bool Exact,
    CortexForkTerminalRunReceipt TerminalReceipt);

internal readonly record struct DeepRematchCalibrationReceipt(
    string ParentRunID,
    string ChildRunDirectory,
    CortexForkStepSpan StepSpan,
    CortexForkSeedLoadReceipt SeedLoad,
    CortexForkDigests FinalDigests,
    PolicyBoundaryTrainingReceipt Training,
    string TrainingPath,
    long AuthorityWallMilliseconds,
    long TrainingWallMilliseconds,
    long RuntimeBindWallMilliseconds,
    long ExecutionWallMilliseconds,
    long TerminalVerifierWallMilliseconds,
    long WallMilliseconds,
    bool TerminalCheckpointExact);

internal readonly record struct DeepRematchEvaluationReceipt(
    string ParentRunID,
    string ChildRunDirectory,
    CortexForkStepSpan StepSpan,
    CortexForkSeedLoadReceipt SeedLoad,
    CortexForkDigests FinalDigests,
    PolicyBoundaryMountReceipt Mount,
    PolicyBoundaryTrainingReceipt Training,
    string MountPath,
    bool CalibrationRuntimeStateCopied,
    EmlAnytimeEvaluationPrefix? Anytime,
    long MountWallMilliseconds,
    long RuntimeBindWallMilliseconds,
    long ExecutionWallMilliseconds,
    long TerminalVerifierWallMilliseconds,
    long WallMilliseconds,
    bool TerminalCheckpointExact,
    EmlDeepRematchFuelCursor? FuelCursor = null,
    string? FuelCursorPath = null,
    string? FuelCursorSHA256 = null,
    string? HandshakePath = null,
    string? HandshakeSHA256 = null,
    string? HandshakeReceiptDigest = null,
    ulong HandshakeDecisionID = 0,
    long HandshakeWallMilliseconds = 0,
    long HandshakeRawTicks = 0,
    long MountRawTicks = 0);

internal enum DeepRematchWallPhases
{
    ColdSeedBuild,
    ChildProvisioning,
    CalibrationSeedCopy,
    CalibrationRuntimeBind,
    CalibrationRun,
    CalibrationRecoveryLoad,
    CalibrationCallback,
    A3Authority,
    TrainingSidecar,
    CalibrationVerifier,
    CalibrationFinalization,
    EvaluationSeedCopy,
    EvaluationRuntimeBind,
    EvaluationHandshake,
    EvaluationMount,
    EvaluationRun,
    EvaluationVerifier,
    EvaluationFinalization,
    EvaluationRecovery,
    Collection,
    Emission,
    Finalization,
}

internal readonly record struct DeepRematchWallSegment(DeepRematchWallPhases Phase, long WallMilliseconds, long RawTicks = 0);

/// A parent wall interval split into named, mutually exclusive child intervals and one residual.
/// The residual is valid only when every child fits inside the parent; overlap is a rejected accounting error.

internal sealed class DeepRematchTotalAccounting
{
    private DeepRematchTotalAccounting(List<DeepRematchWallSegment> segments, long measuredCompositeWallMilliseconds, long measuredRawTicks)
    {
        Segments = segments;
        TotalWallMilliseconds = checked(segments.Sum(static segment => segment.WallMilliseconds));
        MeasuredCompositeWallMilliseconds = measuredCompositeWallMilliseconds;
        MeasuredRawTicks = measuredRawTicks;
    }

    public List<DeepRematchWallSegment> Segments { get; }
    public long TotalWallMilliseconds { get; }
    public long MeasuredCompositeWallMilliseconds { get; }
    public long MeasuredRawTicks { get; }
    public long TotalRawTicks => checked(Segments.Sum(static segment => segment.RawTicks));
    public long UnaccountedRawTicks => checked(MeasuredRawTicks - TotalRawTicks);
    public long UnaccountedWallMilliseconds => checked(MeasuredCompositeWallMilliseconds - TotalWallMilliseconds);
    public bool IsExact => Segments.Count > 0
        && UnaccountedWallMilliseconds == 0
        && (MeasuredRawTicks == 0 || UnaccountedRawTicks == 0)
        && MeasuredCompositeWallMilliseconds >= 0
        && Segments.All(static segment => segment.WallMilliseconds >= 0)
        && Segments.Select(static segment => segment.Phase).Distinct().Count() == Segments.Count;

    internal static DeepRematchTotalAccounting Create(
        List<DeepRematchWallSegment> segments,
        long measuredCompositeWallMilliseconds,
        long measuredRawTicks = 0)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0 || segments.Select(static segment => segment.Phase).Distinct().Count() != segments.Count)
            throw new InvalidDataException("composite accounting must name each exclusive phase exactly once");
        if (segments.Any(static segment => segment.WallMilliseconds < 0))
            throw new InvalidDataException("composite accounting phases cannot be negative");
        if (measuredRawTicks < 0 || (measuredRawTicks > 0 && segments.Any(static segment => segment.RawTicks <= 0)))
            throw new InvalidDataException("composite accounting raw clock is incomplete");
        DeepRematchTotalAccounting accounting = new(segments, measuredCompositeWallMilliseconds, measuredRawTicks);
        if (!accounting.IsExact)
            throw new InvalidDataException($"composite accounting has {accounting.UnaccountedWallMilliseconds}ms dark residual");
        return accounting;
    }
}
