namespace Cogito;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

internal sealed record EmlIntensionalRematchBoundSource(
    int SignatureDigits,
    byte[] AdmissionImage,
    byte[] LawStoreImage,
    EmlHoleCandidate[] Bindings,
    EmlObligationResolution[] Obligations,
    string AdmissionDigest,
    string LawStoreDigest);

internal enum EmlIntensionalRematchSourceRoundTripKinds
{
    Exact,
    DigestMismatch,
    LoadFailure,
    ByteDrift,
}

internal readonly record struct EmlIntensionalRematchSourceComponentRoundTrip(
    string Component,
    EmlIntensionalRematchSourceRoundTripKinds Kind,
    int OriginalLength,
    int ResavedLength,
    int FirstDifferenceOffset,
    string DifferenceSection,
    string Detail)
{
    public bool Exact => Kind == EmlIntensionalRematchSourceRoundTripKinds.Exact;

    public string KindName => Kind switch
    {
        EmlIntensionalRematchSourceRoundTripKinds.Exact => "exact",
        EmlIntensionalRematchSourceRoundTripKinds.DigestMismatch => "digest-mismatch",
        EmlIntensionalRematchSourceRoundTripKinds.LoadFailure => "load-failure",
        EmlIntensionalRematchSourceRoundTripKinds.ByteDrift => "byte-drift",
        _ => "unknown",
    };

    internal string Canonical()
        => string.Join(',', Component, KindName, OriginalLength, ResavedLength,
            FirstDifferenceOffset, DifferenceSection, Detail);
}

internal readonly record struct EmlIntensionalRematchSourceRoundTrip(
    EmlIntensionalRematchSourceComponentRoundTrip Admission,
    EmlIntensionalRematchSourceComponentRoundTrip LawStore)
{
    public bool Exact => Admission.Exact && LawStore.Exact;
}

internal readonly record struct EmlIntensionalRematchSourceRoundTripMeasurement(
    EmlIntensionalRematchSourceRoundTrip Result,
    long WallTicks,
    long AdmissionWallTicks,
    long LawStoreWallTicks);

[RonObject]
internal partial class EmlIntensionalRematchControlArmRow
{
    public string name = "";
    public long scheduledTrials;
    public long executedTrials;
    public long rung0Attempts;
    public long rung0Composed;
    public long rung0EvaluatorZero;
    public long rung0Audits;
    public long rung0UniqueAudits;
    public long relationNullExecutions;
    public long relationNullDivergences;
    public long relationNullAuthorityPredictions;
    public long evaluatorCalls;
    public long delayEvaluatorCalls;
    public long probeTrials;
    public long probeCommits;
    public long probeRollbacks;
    public long probeSerializeLoads;
    public long probeSerializeBytes;
    public long probeRestores;
    public long probeRestoreBytes;
    public long probePreviewEvaluatorCalls;
    public long probeCommittedEvaluatorCalls;
    public long probePreviewWallTicks;
    public long probeCommitWallTicks;
    public long probeRollbackWallTicks;
    public long canonicalDeltas;
    public string assayStatus = "";
    public string assayDetail = "";
    public string powerStatus = "";
    public string powerDetail = "";
}

[RonObject]
internal partial class EmlIntensionalRematchControlReceipt
{
    public string dialect = "EML-INTENSIONAL-BOUND-CONTROL-V4";
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public string sourceAdmissionDigest = "";
    public string sourceLawStoreDigest = "";
    public string sourceCursorDigest = "";
    public string deliberationEpoch = "";
    public string scheduleDigest = "";
    public string reportDigest = "";
    public ulong seed;
    public int replicates;
    public int trialsPerReplicate;
    public long evaluatorCalls;
    public long delayEvaluatorCalls;
    public long probeTrials;
    public long probeCommits;
    public long probeRollbacks;
    public long probeSerializeLoads;
    public long probeSerializeBytes;
    public long probeRestores;
    public long probeRestoreBytes;
    public long probePreviewEvaluatorCalls;
    public long probeCommittedEvaluatorCalls;
    public long probePreviewWallTicks;
    public long probeCommitWallTicks;
    public long probeRollbackWallTicks;
    public long relationNullExecutions;
    public long relationNullDivergences;
    public long relationNullAuthorityPredictions;
    public long shadowComposed;
    public long shadowEvaluatorZero;
    public long shadowAudits;
    public int sourceAdmissionSaveLoadSave;
    public int sourceLawStoreSaveLoadSave;
    public string assayStatus = "";
    public string assayDetail = "";
    public string shadowPowerStatus = "";
    public string shadowPowerDetail = "";
    public string nullPowerStatus = "";
    public string nullPowerDetail = "";
    public string sourceAdmissionRoundTripKind = "";
    public int sourceAdmissionOriginalLength;
    public int sourceAdmissionResavedLength;
    public int sourceAdmissionFirstDifferenceOffset;
    public string sourceAdmissionDifferenceSection = "";
    public string sourceAdmissionRoundTripDetail = "";
    public string sourceLawStoreRoundTripKind = "";
    public int sourceLawStoreOriginalLength;
    public int sourceLawStoreResavedLength;
    public int sourceLawStoreFirstDifferenceOffset;
    public string sourceLawStoreDifferenceSection = "";
    public string sourceLawStoreRoundTripDetail = "";
    public long sourceRoundTripInitialWallTicks;
    public long sourceRoundTripInitialAdmissionWallTicks;
    public long sourceRoundTripInitialLawStoreWallTicks;
    public long sourceRoundTripFinalWallTicks;
    public long sourceRoundTripFinalAdmissionWallTicks;
    public long sourceRoundTripFinalLawStoreWallTicks;
    public int saveLoadSave;
    public double wallMilliseconds;
    public List<EmlIntensionalRematchControlArmRow> arms = [];

    public string ReceiptDigest => receiptDigest;
    public string RunID => runID;
    public string CheckpointDigest => checkpointDigest;
    public string ConfigDigest => configDigest;
    public string SourceAdmissionDigest => sourceAdmissionDigest;
    public string SourceLawStoreDigest => sourceLawStoreDigest;
    public string SourceCursorDigest => sourceCursorDigest;
    public string DeliberationEpoch => deliberationEpoch;
    public string ScheduleDigest => scheduleDigest;
    public string ReportDigest => reportDigest;
    public ulong Seed => seed;
    public int Replicates => replicates;
    public int TrialsPerReplicate => trialsPerReplicate;
    public long EvaluatorCalls => evaluatorCalls;
    public long DelayEvaluatorCalls => delayEvaluatorCalls;
    public long ProbeTrials => probeTrials;
    public long ProbeCommits => probeCommits;
    public long ProbeRollbacks => probeRollbacks;
    public long ProbeSerializeLoads => probeSerializeLoads;
    public long ProbeSerializeBytes => probeSerializeBytes;
    public long ProbeRestores => probeRestores;
    public long ProbeRestoreBytes => probeRestoreBytes;
    public long ProbePreviewEvaluatorCalls => probePreviewEvaluatorCalls;
    public long ProbeCommittedEvaluatorCalls => probeCommittedEvaluatorCalls;
    public long ProbePreviewWallTicks => probePreviewWallTicks;
    public long ProbeCommitWallTicks => probeCommitWallTicks;
    public long ProbeRollbackWallTicks => probeRollbackWallTicks;
    public long RelationNullExecutions => relationNullExecutions;
    public long RelationNullDivergences => relationNullDivergences;
    public long RelationNullAuthorityPredictions => relationNullAuthorityPredictions;
    public long ShadowComposed => shadowComposed;
    public long ShadowEvaluatorZero => shadowEvaluatorZero;
    public long ShadowAudits => shadowAudits;
    public bool SourceAdmissionSaveLoadSave => sourceAdmissionSaveLoadSave == 1;
    public bool SourceLawStoreSaveLoadSave => sourceLawStoreSaveLoadSave == 1;
    public string AssayStatus => assayStatus;
    public string AssayDetail => assayDetail;
    public string ShadowPowerStatus => shadowPowerStatus;
    public string NullPowerStatus => nullPowerStatus;
    public bool SaveLoadSave => saveLoadSave == 1;
    public double WallMilliseconds => wallMilliseconds;
    public List<EmlIntensionalRematchControlArmRow> Arms => arms;

    internal string ComputeDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical())));

    internal string Canonical()
        => string.Join('|', dialect, runID, checkpointDigest, configDigest, sourceAdmissionDigest,
            sourceLawStoreDigest, sourceCursorDigest, deliberationEpoch, scheduleDigest, reportDigest, seed, replicates,
            trialsPerReplicate, evaluatorCalls, delayEvaluatorCalls, relationNullExecutions,
            probeTrials, probeCommits, probeRollbacks, probeSerializeLoads, probeSerializeBytes, probeRestores,
            probeRestoreBytes, probePreviewEvaluatorCalls, probeCommittedEvaluatorCalls, probePreviewWallTicks,
            probeCommitWallTicks, probeRollbackWallTicks,
            relationNullDivergences, relationNullAuthorityPredictions, shadowComposed, shadowEvaluatorZero,
            shadowAudits, assayStatus, assayDetail, shadowPowerStatus, shadowPowerDetail, nullPowerStatus, nullPowerDetail,
            sourceAdmissionRoundTripKind, sourceAdmissionOriginalLength, sourceAdmissionResavedLength,
            sourceAdmissionFirstDifferenceOffset, sourceAdmissionDifferenceSection, sourceAdmissionRoundTripDetail,
            sourceLawStoreRoundTripKind, sourceLawStoreOriginalLength, sourceLawStoreResavedLength,
            sourceLawStoreFirstDifferenceOffset, sourceLawStoreDifferenceSection, sourceLawStoreRoundTripDetail,
            sourceRoundTripInitialWallTicks, sourceRoundTripInitialAdmissionWallTicks,
            sourceRoundTripInitialLawStoreWallTicks, sourceRoundTripFinalWallTicks,
            sourceRoundTripFinalAdmissionWallTicks, sourceRoundTripFinalLawStoreWallTicks,
            saveLoadSave, wallMilliseconds.ToString("G17", CultureInfo.InvariantCulture),
            string.Join(';', arms.Select(static row => string.Join(',', row.name, row.scheduledTrials,
                row.executedTrials, row.rung0Attempts, row.rung0Composed, row.rung0EvaluatorZero,
                row.rung0Audits, row.relationNullExecutions, row.relationNullDivergences,
                row.relationNullAuthorityPredictions, row.evaluatorCalls, row.delayEvaluatorCalls,
                row.probeTrials, row.probeCommits, row.probeRollbacks, row.probeSerializeLoads,
                row.probeSerializeBytes, row.probeRestores, row.probeRestoreBytes,
                row.probePreviewEvaluatorCalls, row.probeCommittedEvaluatorCalls,
                row.probePreviewWallTicks, row.probeCommitWallTicks, row.probeRollbackWallTicks,
                row.canonicalDeltas, row.assayStatus, row.assayDetail, row.powerStatus, row.powerDetail))));
}

internal static partial class EmlIntensionalRematchRunner
{
    internal static byte[] SaveLawStoreImage(EmlLawStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) store.Save(writer);
        return stream.ToArray();
    }

    internal static EmlIntensionalRematchControlReceipt RunBoundControlReceipt(
        ReplayCalc dream,
        string runID,
        string checkpointDigest,
        string configDigest,
        string sourceCursorDigest,
        ulong seed = 0xE311C0DEUL,
        long evaluatorCalls = 1_000,
        long delayEvaluatorCalls = 100,
        int independentReplicates = 3)
    {
        ArgumentNullException.ThrowIfNull(dream);
        if (string.IsNullOrWhiteSpace(runID) || string.IsNullOrWhiteSpace(checkpointDigest)
            || string.IsNullOrWhiteSpace(configDigest))
            throw new ArgumentException("bound rematch receipt requires run/checkpoint/config identity");
        if (independentReplicates <= 0) throw new ArgumentOutOfRangeException(nameof(independentReplicates));
        EmlIntensionalRematchBoundSource source = dream.CaptureIntensionalRematchSource();
        Stopwatch wall = Stopwatch.StartNew();
        List<EmlIntensionalRematchControlArmRow> rows = [];
        StringBuilder reportMaterial = new();
        StringBuilder scheduleMaterial = new();
        long nullExecutions = 0;
        long nullDivergences = 0;
        long nullAuthority = 0;
        long shadowComposed = 0;
        long shadowEvaluatorZero = 0;
        long shadowAudits = 0;
        EmlIntensionalRematchSourceRoundTripMeasurement sourceMeasurement = MeasureSourceRoundTrip(source);
        EmlIntensionalRematchSourceRoundTrip sourceRoundTrip = sourceMeasurement.Result;
        if (!sourceRoundTrip.Admission.Exact || !sourceRoundTrip.LawStore.Exact)
        {
            EmlIntensionalRematchControlReceipt invalid = new()
            {
                runID = runID, checkpointDigest = checkpointDigest, configDigest = configDigest,
                sourceAdmissionDigest = source.AdmissionDigest, sourceLawStoreDigest = source.LawStoreDigest,
                sourceCursorDigest = sourceCursorDigest ?? "", seed = seed, replicates = independentReplicates,
                trialsPerReplicate = TrialsPerReplicate, evaluatorCalls = evaluatorCalls, delayEvaluatorCalls = delayEvaluatorCalls,
                assayStatus = nameof(EmlRematchAssayStatuses.Invalid),
                assayDetail = $"source SaveLoadSave failed: admission={sourceRoundTrip.Admission.Canonical()} law-store={sourceRoundTrip.LawStore.Canonical()}",
                shadowPowerStatus = nameof(EmlRematchPowerStatuses.Unpowered), shadowPowerDetail = "assay invalid",
                nullPowerStatus = nameof(EmlRematchPowerStatuses.Unpowered), nullPowerDetail = "assay invalid",
                sourceAdmissionSaveLoadSave = sourceRoundTrip.Admission.Exact ? 1 : 0,
                sourceLawStoreSaveLoadSave = sourceRoundTrip.LawStore.Exact ? 1 : 0,
                sourceAdmissionRoundTripKind = sourceRoundTrip.Admission.KindName,
                sourceAdmissionOriginalLength = sourceRoundTrip.Admission.OriginalLength,
                sourceAdmissionResavedLength = sourceRoundTrip.Admission.ResavedLength,
                sourceAdmissionFirstDifferenceOffset = sourceRoundTrip.Admission.FirstDifferenceOffset,
                sourceAdmissionDifferenceSection = sourceRoundTrip.Admission.DifferenceSection,
                sourceAdmissionRoundTripDetail = sourceRoundTrip.Admission.Detail,
                sourceLawStoreRoundTripKind = sourceRoundTrip.LawStore.KindName,
                sourceLawStoreOriginalLength = sourceRoundTrip.LawStore.OriginalLength,
                sourceLawStoreResavedLength = sourceRoundTrip.LawStore.ResavedLength,
                sourceLawStoreFirstDifferenceOffset = sourceRoundTrip.LawStore.FirstDifferenceOffset,
                sourceLawStoreDifferenceSection = sourceRoundTrip.LawStore.DifferenceSection,
                sourceLawStoreRoundTripDetail = sourceRoundTrip.LawStore.Detail,
                sourceRoundTripInitialWallTicks = sourceMeasurement.WallTicks,
                sourceRoundTripInitialAdmissionWallTicks = sourceMeasurement.AdmissionWallTicks,
                sourceRoundTripInitialLawStoreWallTicks = sourceMeasurement.LawStoreWallTicks,
                saveLoadSave = 0,
            };
            invalid.receiptDigest = invalid.ComputeDigest();
            return invalid;
        }
        bool assayExact = true;
        string deliberationEpoch = BuildControlDeliberationEpoch(
            runID, checkpointDigest, configDigest, sourceCursorDigest, source.AdmissionDigest, source.LawStoreDigest);
        for (int replicate = 0; replicate < independentReplicates; replicate++)
        {
            ulong replicateSeed = MixReplicateSeed(seed, replicate);
            EmlSieve baseSieve = EmlRematchFixture.CloneSieve(source.SignatureDigits, source.AdmissionImage);
            EmlLawStore lawStore = LoadLawStoreImage(source.LawStoreImage);
            List<EmlIntensionalRematchTrialSeed> seeds = BuildScheduleSeeds(
                replicateSeed, baseSieve, [.. source.Obligations], [.. source.Bindings], lawStore);
            string supply = ValidateSupply([.. source.Obligations], [.. source.Bindings], lawStore, seeds);
            if (supply.Length != 0)
            {
                EmlIntensionalRematchControlReceipt invalid = new()
                {
                    runID = runID, checkpointDigest = checkpointDigest, configDigest = configDigest,
                    sourceAdmissionDigest = source.AdmissionDigest, sourceLawStoreDigest = source.LawStoreDigest,
                    sourceCursorDigest = sourceCursorDigest ?? "", seed = seed, replicates = independentReplicates,
                    trialsPerReplicate = TrialsPerReplicate, evaluatorCalls = evaluatorCalls, delayEvaluatorCalls = delayEvaluatorCalls,
                    assayStatus = nameof(EmlRematchAssayStatuses.Invalid), assayDetail = "source supply invalid: " + supply,
                    shadowPowerStatus = nameof(EmlRematchPowerStatuses.Unpowered), shadowPowerDetail = "assay invalid",
                    nullPowerStatus = nameof(EmlRematchPowerStatuses.Unpowered), nullPowerDetail = "assay invalid",
                    sourceAdmissionSaveLoadSave = 1, sourceLawStoreSaveLoadSave = 1, saveLoadSave = 1,
                };
                invalid.receiptDigest = invalid.ComputeDigest();
                return invalid;
            }
            EmlIntensionalRematchSchedule schedule = EmlIntensionalRematchSchedule.Create(replicateSeed, seeds);
            EmlIntensionalRematchConfig config = new(evaluatorCalls, delayEvaluatorCalls, 1);
            IEmlIntensionalRematchArm[] arms = CreateArms(
                source.SignatureDigits, source.AdmissionImage, [.. source.Bindings], source.LawStoreImage,
                schedule.Trials.Count, deliberationEpoch);
            EmlIntensionalRematchReport report = EmlIntensionalRematch.Run(in config, schedule, arms);
            scheduleMaterial.Append(replicate).Append(':').Append(replicateSeed.ToString("X16", CultureInfo.InvariantCulture))
                .Append(':').Append(ScheduleCanonical(schedule)).Append('|');
            reportMaterial.Append(replicate).Append(':').Append(report.FormatTSV()).Append('|');
            foreach (EmlIntensionalRematchArmReport arm in report.Arms)
            {
                EmlRung0RematchTelemetry rung = arm.Rung0;
                rows.Add(new EmlIntensionalRematchControlArmRow
                {
                    name = $"{replicate}:{arm.Name}", scheduledTrials = arm.ScheduledTrials,
                    executedTrials = arm.ExecutedTrials, rung0Attempts = rung.Attempts,
                    rung0Composed = rung.Composed, rung0EvaluatorZero = rung.EvaluatorZeroCompositions,
                    rung0Audits = rung.Audits, relationNullExecutions = rung.RelationNullExecutions,
                    rung0UniqueAudits = rung.UniqueAudits,
                    relationNullDivergences = rung.RelationNullDivergences,
                    relationNullAuthorityPredictions = rung.RelationNullAuthoritativeCompositions,
                    evaluatorCalls = arm.Evaluation.Calls, delayEvaluatorCalls = arm.DescendantDelay.Calls,
                    probeTrials = arm.SpeculativeTransactions.ProbeTrials,
                    probeCommits = arm.SpeculativeTransactions.Commits,
                    probeRollbacks = arm.SpeculativeTransactions.Rollbacks,
                    probeSerializeLoads = arm.SpeculativeTransactions.SerializeLoads,
                    probeSerializeBytes = arm.SpeculativeTransactions.SerializeBytes,
                    probeRestores = arm.SpeculativeTransactions.Restores,
                    probeRestoreBytes = arm.SpeculativeTransactions.RestoreBytes,
                    probePreviewEvaluatorCalls = arm.SpeculativeTransactions.PreviewEvaluatorCalls,
                    probeCommittedEvaluatorCalls = arm.SpeculativeTransactions.CommittedEvaluatorCalls,
                    probePreviewWallTicks = arm.SpeculativeTransactions.PreviewWallTicks,
                    probeCommitWallTicks = arm.SpeculativeTransactions.CommitWallTicks,
                    probeRollbackWallTicks = arm.SpeculativeTransactions.RollbackWallTicks,
                    canonicalDeltas = arm.CanonicalDeltas.Count,
                    assayStatus = arm.AssayStatus.ToString(), assayDetail = arm.AssayDetail,
                    powerStatus = arm.PowerStatus.ToString(), powerDetail = arm.PowerDetail,
                });
                assayExact &= arm.AssayExact;
                if (arm.Kind == EmlIntensionalRematchArms.LawShuffledNull)
                {
                    nullExecutions += rung.RelationNullExecutions;
                    nullDivergences += rung.RelationNullDivergences;
                    nullAuthority += rung.RelationNullAuthoritativeCompositions;
                }
                else if (arm.Kind == EmlIntensionalRematchArms.LawCandidateShadow)
                {
                    shadowComposed += rung.Composed;
                    shadowEvaluatorZero += rung.EvaluatorZeroCompositions;
                    shadowAudits += rung.Audits;
                }
            }
        }
        wall.Stop();
        int expectedArms = checked(independentReplicates * Enum.GetValues<EmlIntensionalRematchArms>().Length);
        if (rows.Count != expectedArms)
            throw new InvalidDataException($"bound rematch emitted {rows.Count} arm rows; expected {expectedArms}");
        HashSet<string> armNames = new(StringComparer.Ordinal);
        foreach (EmlIntensionalRematchControlArmRow row in rows)
        {
            if (!armNames.Add(row.name) || row.scheduledTrials != TrialsPerReplicate || row.executedTrials != TrialsPerReplicate)
                throw new InvalidDataException($"bound rematch arm row {row.name} is not an exact {TrialsPerReplicate}-trial receipt");
        }
        EmlIntensionalRematchControlArmRow? assayFailure = rows.FirstOrDefault(static row => row.assayStatus != nameof(EmlRematchAssayStatuses.Exact));
        EmlRematchAssayStatuses assayStatus = assayExact ? EmlRematchAssayStatuses.Exact : EmlRematchAssayStatuses.Invalid;
        string assayDetail = assayFailure?.assayDetail ?? "exact";
        string nullSuffix = ":" + EmlIntensionalRematchRunner.ReadArmName(EmlIntensionalRematchArms.LawShuffledNull);
        string shadowSuffix = ":" + EmlIntensionalRematchRunner.ReadArmName(EmlIntensionalRematchArms.LawCandidateShadow);
        EmlIntensionalRematchControlArmRow[] nullRows = [.. rows.Where(row => row.name.EndsWith(nullSuffix, StringComparison.Ordinal))];
        EmlIntensionalRematchControlArmRow[] shadowRows = [.. rows.Where(row => row.name.EndsWith(shadowSuffix, StringComparison.Ordinal))];
        EmlRematchPowerStatuses nullPowerStatus = nullRows.Length > 0 && nullRows.All(static row => row.powerStatus == nameof(EmlRematchPowerStatuses.Powered))
            ? EmlRematchPowerStatuses.Powered : EmlRematchPowerStatuses.Unpowered;
        EmlRematchPowerStatuses shadowPowerStatus = shadowRows.Length > 0 && shadowRows.All(static row => row.powerStatus == nameof(EmlRematchPowerStatuses.Powered))
            ? EmlRematchPowerStatuses.Powered : EmlRematchPowerStatuses.Unpowered;
        string nullPowerDetail = nullRows.FirstOrDefault(static row => row.powerStatus != nameof(EmlRematchPowerStatuses.Powered))?.powerDetail
            ?? "all relation-null replicates powered";
        string shadowPowerDetail = shadowRows.FirstOrDefault(static row => row.powerStatus != nameof(EmlRematchPowerStatuses.Powered))?.powerDetail
            ?? "all candidate-shadow replicates powered";
        string scheduleDigest = Digest(Encoding.UTF8.GetBytes(scheduleMaterial.ToString()));
        string reportDigest = Digest(Encoding.UTF8.GetBytes(reportMaterial.ToString()));
        EmlIntensionalRematchSourceRoundTripMeasurement finalSourceMeasurement = MeasureSourceRoundTrip(source);
        EmlIntensionalRematchSourceRoundTrip finalSourceRoundTrip = finalSourceMeasurement.Result;
        if (!finalSourceRoundTrip.Exact)
        {
            assayExact = false;
            assayStatus = EmlRematchAssayStatuses.Invalid;
            assayDetail = $"final source SaveLoadSave failed: admission={finalSourceRoundTrip.Admission.Canonical()} law-store={finalSourceRoundTrip.LawStore.Canonical()}";
        }
        EmlIntensionalRematchControlReceipt receipt = new()
        {
            runID = runID, checkpointDigest = checkpointDigest, configDigest = configDigest,
            sourceAdmissionDigest = source.AdmissionDigest, sourceLawStoreDigest = source.LawStoreDigest,
            sourceCursorDigest = sourceCursorDigest ?? "", deliberationEpoch = deliberationEpoch,
            scheduleDigest = scheduleDigest, reportDigest = reportDigest,
            seed = seed,
            replicates = independentReplicates, trialsPerReplicate = TrialsPerReplicate,
            evaluatorCalls = evaluatorCalls, delayEvaluatorCalls = delayEvaluatorCalls, relationNullExecutions = nullExecutions,
            probeTrials = rows.Sum(static row => row.probeTrials),
            probeCommits = rows.Sum(static row => row.probeCommits),
            probeRollbacks = rows.Sum(static row => row.probeRollbacks),
            probeSerializeLoads = rows.Sum(static row => row.probeSerializeLoads),
            probeSerializeBytes = rows.Sum(static row => row.probeSerializeBytes),
            probeRestores = rows.Sum(static row => row.probeRestores),
            probeRestoreBytes = rows.Sum(static row => row.probeRestoreBytes),
            probePreviewEvaluatorCalls = rows.Sum(static row => row.probePreviewEvaluatorCalls),
            probeCommittedEvaluatorCalls = rows.Sum(static row => row.probeCommittedEvaluatorCalls),
            probePreviewWallTicks = rows.Sum(static row => row.probePreviewWallTicks),
            probeCommitWallTicks = rows.Sum(static row => row.probeCommitWallTicks),
            probeRollbackWallTicks = rows.Sum(static row => row.probeRollbackWallTicks),
            relationNullDivergences = nullDivergences, relationNullAuthorityPredictions = nullAuthority,
            shadowComposed = shadowComposed, shadowEvaluatorZero = shadowEvaluatorZero, shadowAudits = shadowAudits,
            sourceAdmissionSaveLoadSave = sourceRoundTrip.Admission.Exact && finalSourceRoundTrip.Admission.Exact ? 1 : 0,
            sourceLawStoreSaveLoadSave = sourceRoundTrip.LawStore.Exact && finalSourceRoundTrip.LawStore.Exact ? 1 : 0,
            assayStatus = assayStatus.ToString(), assayDetail = assayDetail,
            shadowPowerStatus = shadowPowerStatus.ToString(), shadowPowerDetail = shadowPowerDetail,
            nullPowerStatus = nullPowerStatus.ToString(), nullPowerDetail = nullPowerDetail,
            sourceAdmissionRoundTripKind = sourceRoundTrip.Admission.KindName,
            sourceAdmissionOriginalLength = sourceRoundTrip.Admission.OriginalLength,
            sourceAdmissionResavedLength = sourceRoundTrip.Admission.ResavedLength,
            sourceAdmissionFirstDifferenceOffset = sourceRoundTrip.Admission.FirstDifferenceOffset,
            sourceAdmissionDifferenceSection = sourceRoundTrip.Admission.DifferenceSection,
            sourceAdmissionRoundTripDetail = sourceRoundTrip.Admission.Detail,
            sourceLawStoreRoundTripKind = sourceRoundTrip.LawStore.KindName,
            sourceLawStoreOriginalLength = sourceRoundTrip.LawStore.OriginalLength,
            sourceLawStoreResavedLength = sourceRoundTrip.LawStore.ResavedLength,
            sourceLawStoreFirstDifferenceOffset = sourceRoundTrip.LawStore.FirstDifferenceOffset,
            sourceLawStoreDifferenceSection = sourceRoundTrip.LawStore.DifferenceSection,
            sourceLawStoreRoundTripDetail = sourceRoundTrip.LawStore.Detail,
            sourceRoundTripInitialWallTicks = sourceMeasurement.WallTicks,
            sourceRoundTripInitialAdmissionWallTicks = sourceMeasurement.AdmissionWallTicks,
            sourceRoundTripInitialLawStoreWallTicks = sourceMeasurement.LawStoreWallTicks,
            sourceRoundTripFinalWallTicks = finalSourceMeasurement.WallTicks,
            sourceRoundTripFinalAdmissionWallTicks = finalSourceMeasurement.AdmissionWallTicks,
            sourceRoundTripFinalLawStoreWallTicks = finalSourceMeasurement.LawStoreWallTicks,
            saveLoadSave = sourceRoundTrip.Exact && finalSourceRoundTrip.Exact ? 1 : 0,
            wallMilliseconds = wall.Elapsed.TotalMilliseconds, arms = rows,
        };
        receipt.receiptDigest = receipt.ComputeDigest();
        return receipt;
    }

    private static EmlLawStore LoadLawStoreImage(byte[] image)
    {
        EmlLawStore store = new();
        using MemoryStream stream = new(image, writable: false);
        using CkptReader reader = new(stream);
        store.Load(reader);
        return store;
    }

    private static EmlIntensionalRematchSourceRoundTrip VerifySourceRoundTrip(EmlIntensionalRematchBoundSource source)
        => MeasureSourceRoundTrip(source).Result;

    private static EmlIntensionalRematchSourceRoundTripMeasurement MeasureSourceRoundTrip(
        EmlIntensionalRematchBoundSource source)
    {
        long started = Stopwatch.GetTimestamp();
        long admissionStarted = Stopwatch.GetTimestamp();
        EmlIntensionalRematchSourceComponentRoundTrip admission = VerifyAdmissionRoundTrip(source);
        long admissionWallTicks = Stopwatch.GetTimestamp() - admissionStarted;
        long lawStoreStarted = Stopwatch.GetTimestamp();
        EmlIntensionalRematchSourceComponentRoundTrip lawStore = VerifyLawStoreRoundTrip(source);
        long lawStoreWallTicks = Stopwatch.GetTimestamp() - lawStoreStarted;
        return new(new(admission, lawStore), Stopwatch.GetTimestamp() - started,
            admissionWallTicks, lawStoreWallTicks);
    }

    private static EmlIntensionalRematchSourceComponentRoundTrip VerifyAdmissionRoundTrip(
        EmlIntensionalRematchBoundSource source)
    {
        const string component = "admission";
        int originalLength = source.AdmissionImage.Length;
        if (Digest(source.AdmissionImage) != source.AdmissionDigest)
            return new(component, EmlIntensionalRematchSourceRoundTripKinds.DigestMismatch,
                originalLength, 0, -1, "source-digest", "admission source digest does not match image");
        try
        {
            EmlSieve sieve = EmlRematchFixture.CloneSieve(source.SignatureDigits, source.AdmissionImage);
            byte[] resaved = sieve.CaptureAdmissionState();
            if (resaved.AsSpan().SequenceEqual(source.AdmissionImage))
                return new(component, EmlIntensionalRematchSourceRoundTripKinds.Exact,
                    originalLength, resaved.Length, -1, "", "");
            int firstDifference = FindFirstDifference(source.AdmissionImage, resaved);
            return new(component, EmlIntensionalRematchSourceRoundTripKinds.ByteDrift,
                originalLength, resaved.Length, firstDifference, "binary", "admission image changed after SaveLoadSave");
        }
        catch (Exception ex) when (IsSourceLoadFailure(ex))
        {
            return new(component, EmlIntensionalRematchSourceRoundTripKinds.LoadFailure,
                originalLength, 0, -1, "load", DescribeLoadFailure(ex));
        }
    }

    private static EmlIntensionalRematchSourceComponentRoundTrip VerifyLawStoreRoundTrip(
        EmlIntensionalRematchBoundSource source)
    {
        const string component = "law-store";
        int originalLength = source.LawStoreImage.Length;
        if (Digest(source.LawStoreImage) != source.LawStoreDigest)
            return new(component, EmlIntensionalRematchSourceRoundTripKinds.DigestMismatch,
                originalLength, 0, -1, "source-digest", "law-store source digest does not match image");
        try
        {
            EmlLawStore store = LoadLawStoreImage(source.LawStoreImage);
            byte[] resaved = SaveLawStoreImage(store);
            if (resaved.AsSpan().SequenceEqual(source.LawStoreImage))
                return new(component, EmlIntensionalRematchSourceRoundTripKinds.Exact,
                    originalLength, resaved.Length, -1, "", "");
            int firstDifference = FindFirstDifference(source.LawStoreImage, resaved);
            return new(component, EmlIntensionalRematchSourceRoundTripKinds.ByteDrift,
                originalLength, resaved.Length, firstDifference, "binary", "law-store image changed after SaveLoadSave");
        }
        catch (Exception ex) when (IsSourceLoadFailure(ex))
        {
            return new(component, EmlIntensionalRematchSourceRoundTripKinds.LoadFailure,
                originalLength, 0, -1, "load", DescribeLoadFailure(ex));
        }
    }

    private static int FindFirstDifference(ReadOnlySpan<byte> original, ReadOnlySpan<byte> resaved)
    {
        int common = Math.Min(original.Length, resaved.Length);
        for (int i = 0; i < common; i++)
            if (original[i] != resaved[i]) return i;
        return common < original.Length || common < resaved.Length ? common : -1;
    }

    private static string DescribeLoadFailure(Exception exception)
        => exception.GetType().Name + ": " + exception.Message;

    private static bool IsSourceLoadFailure(Exception exception)
        => exception is InvalidDataException or EndOfStreamException or ArgumentException or FormatException
            or OverflowException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException;

    internal static bool VerifyBoundControlSourceFixture()
    {
        static bool RequireFixture(bool condition, string detail)
        {
            if (!condition) Trace.Note($"deep-rematch source fixture failed · {detail}");
            return condition;
        }

        EmlRematchFixture fixture = EmlRematchFixture.Create(5);
        EmlSieve mature = fixture.Sieve;
        EmlObligationResolution obligation = fixture.Obligations[0];
        EmlDeliberationLease settled = mature.ReserveDeliberation(in obligation, EmlDeliberationQuota.Default);
        settled.Complete(EmlDeliberationOutcomes.NoCandidate, "mature-source-fixture");
        EmlRematchFixture bound = EmlRematchFixture.CaptureBound(mature);
        byte[] lawImage = SaveLawStoreImage(new EmlLawStore());
        EmlIntensionalRematchBoundSource source = new(
            mature.SignatureDigits,
            bound.AdmissionImage,
            lawImage,
            bound.Bindings.ToArray(),
            bound.Obligations.ToArray(),
            Digest(bound.AdmissionImage),
            Digest(lawImage));
        EmlIntensionalRematchSourceRoundTrip roundTrip = VerifySourceRoundTrip(source);
        if (!RequireFixture(roundTrip.Exact, $"mature={roundTrip.Admission.Canonical()}|{roundTrip.LawStore.Canonical()}")) return false;

        EmlSieve clone = EmlRematchFixture.CloneSieve(source.SignatureDigits, source.AdmissionImage);
        EmlDeliberationLease reused = clone.ReserveDeliberation(in obligation, EmlDeliberationQuota.Default);
        EmlDeliberationLease control = clone.ReserveDeliberation(
            in obligation, EmlDeliberationQuota.Default, "deep-rematch-control-fixture");
        if (!RequireFixture(reused.IsReused && !control.IsReused, "reservation epoch")) return false;

        byte[] admissionTamper = [.. source.AdmissionImage];
        admissionTamper[0] ^= 0x01;
        EmlIntensionalRematchSourceRoundTrip admissionFailure = VerifySourceRoundTrip(source with { AdmissionImage = admissionTamper });
        if (!RequireFixture(admissionFailure.Admission.Kind == EmlIntensionalRematchSourceRoundTripKinds.DigestMismatch
            && admissionFailure.LawStore.Exact, $"admission tamper={admissionFailure.Admission.Canonical()}|{admissionFailure.LawStore.Canonical()}"))
            return false;
        byte[] admissionLoadTamper = [.. source.AdmissionImage];
        BinaryPrimitives.WriteInt32LittleEndian(admissionLoadTamper.AsSpan(24), -1);
        EmlIntensionalRematchSourceRoundTrip admissionLoadFailure = VerifySourceRoundTrip(source with
        {
            AdmissionImage = admissionLoadTamper,
            AdmissionDigest = Digest(admissionLoadTamper),
        });
        if (!RequireFixture(admissionLoadFailure.Admission.Kind == EmlIntensionalRematchSourceRoundTripKinds.LoadFailure
            && admissionLoadFailure.LawStore.Exact, $"admission load={admissionLoadFailure.Admission.Canonical()}|{admissionLoadFailure.LawStore.Canonical()}"))
            return false;
        byte[] lawTamper = [.. source.LawStoreImage];
        lawTamper[0] ^= 0x01;
        EmlIntensionalRematchSourceRoundTrip lawFailure = VerifySourceRoundTrip(source with { LawStoreImage = lawTamper });
        if (!RequireFixture(lawFailure.LawStore.Kind == EmlIntensionalRematchSourceRoundTripKinds.DigestMismatch
            && lawFailure.Admission.Exact, $"law tamper={lawFailure.Admission.Canonical()}|{lawFailure.LawStore.Canonical()}"))
            return false;
        EmlIntensionalRematchSourceRoundTrip lawLoadFailure = VerifySourceRoundTrip(source with
        {
            LawStoreImage = lawTamper,
            LawStoreDigest = Digest(lawTamper),
        });
        if (!RequireFixture(lawLoadFailure.LawStore.Kind == EmlIntensionalRematchSourceRoundTripKinds.LoadFailure
            && lawLoadFailure.Admission.Exact, $"law load={lawLoadFailure.Admission.Canonical()}|{lawLoadFailure.LawStore.Canonical()}"))
            return false;

        byte[] lawSchemaDrift = [.. source.LawStoreImage];
        BinaryPrimitives.WriteInt32LittleEndian(lawSchemaDrift, 7);
        EmlIntensionalRematchSourceRoundTrip schemaDrift = VerifySourceRoundTrip(source with
        {
            LawStoreImage = lawSchemaDrift,
            LawStoreDigest = Digest(lawSchemaDrift),
        });
        bool schemaFixture = RequireFixture(schemaDrift.LawStore.Kind == EmlIntensionalRematchSourceRoundTripKinds.ByteDrift
            && schemaDrift.LawStore.FirstDifferenceOffset == 0
            && schemaDrift.LawStore.ResavedLength > 0
            && schemaDrift.Admission.Exact, $"schema drift={schemaDrift.Admission.Canonical()}|{schemaDrift.LawStore.Canonical()}");
        bool transactionFixture = VerifySpeculativeTransactionEquivalence(
            mature.SignatureDigits,
            [fixture.AdmissionImage, bound.AdmissionImage]);
        bool queueFixture = VerifySpeculativeQueueRollback(mature, bound.AdmissionImage, obligation);
        bool cacheFixture = VerifySpeculativeCacheOverlay(mature.SignatureDigits, bound.AdmissionImage);
        if (schemaFixture && transactionFixture && queueFixture && cacheFixture)
            Trace.Note("deep-rematch speculative transaction fixture · fresh=PASS mature=PASS queues=PASS cache=PASS");
        return schemaFixture && transactionFixture && queueFixture && cacheFixture;
    }

    private static bool VerifySpeculativeTransactionEquivalence(int signatureDigits, byte[][] admissionImages)
    {
        static bool Require(bool condition, string detail)
        {
            if (!condition) Trace.Note($"deep-rematch speculative transaction fixture failed · {detail}");
            return condition;
        }

        string[] programs = ["1", "11?E1EE1E"];
        bool valid = true;
        for (int state = 0; state < 2; state++)
        {
            EmlSieve source = EmlRematchFixture.CloneSieve(signatureDigits, admissionImages[state]);
            if (state == 1)
            {
                int initialMints = source.NewMints.Count;
                source.Offer(programs[0]);
                if (source.NewMints.Count == initialMints) source.Offer(programs[1]);
                if (source.NewMints.Count == initialMints)
                {
                    foreach (string candidate in EmlGen.Enumerate(8, 9))
                    {
                        source.Offer(candidate);
                        if (source.NewMints.Count > initialMints) break;
                    }
                }
                valid &= Require(source.NewMints.Count > initialMints, "mature setup did not retain a non-empty mint queue");
            }
            byte[] before = source.CaptureAdmissionState();
            EmlEvaluatorClockSnapshot beforeClock = source.EvaluatorClock.Capture();
            string program = programs[state];

            EmlSieve reference = EmlRematchFixture.CloneSieve(source.SignatureDigits, before);
            reference.Offer(program);
            byte[] expectedCommit = reference.CaptureAdmissionState();
            EmlEvaluatorClockSnapshot expectedCommitClock = reference.EvaluatorClock.Capture();

            EmlSieve committed = EmlRematchFixture.CloneSieve(source.SignatureDigits, before);
            using (EmlSieve.SpeculativeTransaction transaction = committed.BeginSpeculativeTransaction())
            {
                committed.Offer(program);
                transaction.RecordPreview(committed.EvaluatorClock.ProgramPointEvaluations - beforeClock.ProgramPointEvaluations);
                transaction.RecordCommitted(committed.EvaluatorClock.ProgramPointEvaluations - beforeClock.ProgramPointEvaluations);
                transaction.Commit();
                valid &= Require(committed.CaptureAdmissionState().AsSpan().SequenceEqual(expectedCommit), $"commit image drift state={state}");
                valid &= Require(committed.EvaluatorClock.Capture().Equals(expectedCommitClock), $"commit evaluator drift state={state}");
            }

            EmlSieve rolledBack = EmlRematchFixture.CloneSieve(source.SignatureDigits, before);
            using (EmlSieve.SpeculativeTransaction transaction = rolledBack.BeginSpeculativeTransaction())
            {
                rolledBack.Offer(program);
                transaction.RecordPreview(rolledBack.EvaluatorClock.ProgramPointEvaluations - beforeClock.ProgramPointEvaluations);
                transaction.Rollback();
                valid &= Require(rolledBack.CaptureAdmissionState().AsSpan().SequenceEqual(before), $"rollback image drift state={state}");
                valid &= Require(rolledBack.EvaluatorClock.Capture().Equals(beforeClock), $"rollback evaluator drift state={state}");
            }
        }
        return valid;
    }

    private static bool VerifySpeculativeQueueRollback(
        EmlSieve mature,
        byte[] admissionImage,
        EmlObligationResolution obligation)
    {
        static bool Require(bool condition, string detail)
        {
            if (!condition) Trace.Note($"deep-rematch speculative queue fixture failed · {detail}");
            return condition;
        }

        static bool SameQueue<T>(IReadOnlyList<T> actual, T[] expected)
            where T : IEquatable<T>
        {
            if (actual.Count != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
                if (!actual[i].Equals(expected[i])) return false;
            return true;
        }

        EmlSieve queued = EmlRematchFixture.CloneSieve(mature.SignatureDigits, admissionImage);
        int initialMints = queued.NewMints.Count;
        queued.Offer("1");
        if (queued.NewMints.Count == initialMints) queued.Offer("11?E1EE1E");
        if (queued.NewMints.Count == initialMints)
        {
            foreach (string candidate in EmlGen.Enumerate(8, 9))
            {
                queued.Offer(candidate);
                if (queued.NewMints.Count > initialMints) break;
            }
        }
        if (!Require(queued.NewMints.Count > initialMints, "queue setup did not retain a non-empty mint queue")) return false;

        EmlMint source = queued.MintLog[obligation.SourcePredictionID.Value];
        if (!Require(EmlPrediction.TryParse(source.Line, out EmlPrediction claim), "queue source claim is malformed")) return false;
        if (!Require(EmlResidualDeriver.TryDeriveSharedExponentialArgument(
                obligation.SourcePredictionID, in claim, 32, out EmlResidualComposition derivation),
                "queue source has no process residual")) return false;
        const string finiteNegativeLogXProgram = "111E1EE1111EE1EE111111EE1EE11EEE1EE11xE1EE1EE1EE1EE";
        const string finiteNegativeLogYProgram = "111E1EE1111EE1EE111111EE1EE11EEE1EE11yE1EE1EE1EE1EE";
        string finiteProgram = derivation.Process.DenominatorRPN == "x"
            ? finiteNegativeLogXProgram
            : finiteNegativeLogYProgram;
        EmlObligationClosureResult closure = queued.AdmitResidualProof(
            obligation.SourcePredictionID,
            finiteProgram,
            queued.EvaluatorClock.ProgramPointEvaluations);
        if (!Require(closure.Accepted && queued.NewSemanticDeltas.Count > 0,
                $"queue setup did not retain a semantic delta: {closure.Closure.Status}")) return false;

        byte[] before = queued.CaptureAdmissionState();
        EmlEvaluatorClockSnapshot beforeClock = queued.EvaluatorClock.Capture();
        EmlMint[] beforeMints = [.. queued.NewMints];
        EmlCertificateDelta[] beforeDeltas = [.. queued.NewSemanticDeltas];
        using (EmlSieve.SpeculativeTransaction transaction = queued.BeginSpeculativeTransaction())
        {
            long start = queued.EvaluatorClock.ProgramPointEvaluations;
            queued.Offer("11?E1EE1E");
            transaction.RecordPreview(queued.EvaluatorClock.ProgramPointEvaluations - start);
            transaction.Rollback();
        }

        bool queuesExact = Require(queued.CaptureAdmissionState().AsSpan().SequenceEqual(before), "overbudget rollback image drift")
            && Require(queued.EvaluatorClock.Capture().Equals(beforeClock), "overbudget rollback evaluator drift")
            && Require(SameQueue(queued.NewMints, beforeMints), "overbudget rollback mint queue drift")
            && Require(SameQueue(queued.NewSemanticDeltas, beforeDeltas), "overbudget rollback semantic queue drift");
        EmlSieve legacy = EmlRematchFixture.CloneSieve(mature.SignatureDigits, admissionImage);
        legacy.EvaluatorClock.MarkLegacyCheckpoint();
        byte[] legacyBefore = legacy.CaptureAdmissionState();
        using (EmlSieve.SpeculativeTransaction transaction = legacy.BeginSpeculativeTransaction())
        {
            long start = legacy.EvaluatorClock.ProgramPointEvaluations;
            legacy.Offer("11?E1EE1E");
            transaction.RecordPreview(legacy.EvaluatorClock.ProgramPointEvaluations - start);
            transaction.Rollback();
        }
        return queuesExact
            && Require(legacy.CaptureAdmissionState().AsSpan().SequenceEqual(legacyBefore), "legacy-clock rollback image drift")
            && Require(!legacy.EvaluatorClock.Capture().WritesCheckpoint, "legacy-clock rollback restored checkpoint writer")
            && Require(legacy.EvaluatorClock.Capture().LoadedCheckpointVersion == 0, "legacy-clock rollback restored checkpoint version")
            && Require(!legacy.EvaluatorClock.HistoryComplete, "legacy-clock rollback restored history completeness");
    }

    private static bool VerifySpeculativeCacheOverlay(int signatureDigits, byte[] admissionImage)
    {
        static bool Require(bool condition, string detail)
        {
            if (!condition) Trace.Note($"deep-rematch speculative cache fixture failed · {detail}");
            return condition;
        }

        EmlSieve sieve = EmlRematchFixture.CloneSieve(signatureDigits, admissionImage);
        EmlGrader grader = sieve.Grader;
        grader.EvaluateFinite("1");
        (long baseKeys, long _) = grader.CacheMass();
        using (EmlSieve.SpeculativeTransaction warm = sieve.BeginSpeculativeTransaction())
        {
            grader.EvaluateFinite("1");
            (long scratchKeys, long _) = grader.SpeculativeCacheMass();
            bool readThrough = Require(scratchKeys == 0, "warm-base read-through populated speculative cache");
            warm.Rollback();
            if (!readThrough) return false;
        }
        (long afterWarmKeys, long _) = grader.CacheMass();
        bool valid = Require(afterWarmKeys == baseKeys, "warm-base rollback changed base cache");

        string firstFresh = "";
        string secondFresh = "";
        foreach (string candidate in EmlGen.Enumerate(19, 19))
        {
            if (firstFresh.Length == 0) firstFresh = candidate;
            else
            {
                secondFresh = candidate;
                break;
            }
        }
        if (!Require(firstFresh.Length != 0 && secondFresh.Length != 0, "speculative cache fixture has no fresh keys")) return false;

        using (EmlSieve.SpeculativeTransaction committed = sieve.BeginSpeculativeTransaction())
        {
            grader.EvaluateFinite(firstFresh);
            (long scratchKeys, long _) = grader.SpeculativeCacheMass();
            valid &= Require(scratchKeys == 1, $"speculative cache write was not isolated: scratch={scratchKeys}");
            committed.Commit();
        }
        (long afterCommitKeys, long _) = grader.CacheMass();
        valid &= Require(afterCommitKeys == baseKeys + 1, $"speculative cache commit did not merge: before={baseKeys} after={afterCommitKeys}");

        using (EmlSieve.SpeculativeTransaction rolledBack = sieve.BeginSpeculativeTransaction())
        {
            grader.EvaluateFinite(secondFresh);
            (long scratchKeys, long _) = grader.SpeculativeCacheMass();
            valid &= Require(scratchKeys == 1, $"rollback cache write was not isolated: scratch={scratchKeys}");
            rolledBack.Rollback();
        }
        (long afterRollbackKeys, long _) = grader.CacheMass();
        valid &= Require(afterRollbackKeys == afterCommitKeys, "speculative cache rollback changed base cache");

        int filled = (int)afterRollbackKeys;
        if (filled < 4096)
        {
            foreach (string candidate in EmlGen.Enumerate(1, 17))
            {
                grader.EvaluateFinite(candidate);
                filled = (int)grader.CacheMass().Keys;
                if (filled >= 4096) break;
            }
        }
        valid &= Require(grader.CacheMass().Keys == 4096, "cache boundary fixture did not reach CacheCap");
        string boundaryProgram = "";
        foreach (string candidate in EmlGen.Enumerate(21, 21))
        {
            boundaryProgram = candidate;
            break;
        }
        valid &= Require(boundaryProgram.Length != 0, "cache boundary fixture has no fresh key");
        using (EmlSieve.SpeculativeTransaction boundary = sieve.BeginSpeculativeTransaction())
        {
            grader.EvaluateFinite(boundaryProgram);
            boundary.Commit();
        }
        valid &= Require(grader.CacheMass().Keys <= 4096, "cache commit exceeded CacheCap");
        return valid;
    }

    internal static string BuildControlDeliberationEpoch(
        string runID,
        string checkpointDigest,
        string configDigest,
        string sourceCursorDigest,
        string sourceAdmissionDigest,
        string sourceLawStoreDigest)
    {
        string material = string.Join('|', runID, checkpointDigest, configDigest, sourceCursorDigest ?? "",
            sourceAdmissionDigest, sourceLawStoreDigest);
        return "deep-rematch-control-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string ScheduleCanonical(EmlIntensionalRematchSchedule schedule)
    {
        StringBuilder text = new();
        for (int i = 0; i < schedule.Trials.Count; i++)
        {
            EmlIntensionalRematchTrial trial = schedule.Trials[i];
            text.Append(trial.Index).Append(':').Append(trial.Obligation.SourcePredictionID.Value).Append(':')
                .Append(trial.Binding.Value).Append(':').Append(trial.ShuffledBinding.Value).Append(':')
                .Append(trial.LawCandidate?.Rewrite.ToString() ?? "none").Append(':')
                .Append(trial.ShuffledLawCandidate?.Rewrite.ToString() ?? "none").Append('|');
        }
        return text.ToString();
    }

    private static string Digest(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
