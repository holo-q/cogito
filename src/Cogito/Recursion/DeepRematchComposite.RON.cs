namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Grammar;
using Ronmamon;
using Ronmamon.Reader;

/// Read-only durable authority for historical composite runs. These records are
/// the hand-off that a verifier can reopen after the Cortex process is gone.
internal static class DeepRematchCompositeRON
{
    internal const int SchemaVersion = 5;
    internal const int ParentRecordSchemaVersion = 4;
    internal const int ColdSeedRecordSchemaVersion = 4;
    internal const int LegacyChildCopyRecordSchemaVersion = 6;
    internal const int ChildCopyRecordSchemaVersion = 7;
    internal const int PhaseJournalSchemaVersion = 4;
    internal const int AttemptJournalSchemaVersion = 4;
    internal const int AttemptAccountingSchemaVersion = 6;
    internal const int LegacyAttemptAccountingSchemaVersion = 5;
    internal const int CompositeRecordSchemaVersion = 6;
    internal const int LegacyCompositeRecordSchemaVersion = 5;
    internal const int EvaluationRecoverySettlementSchemaVersion = 1;
    internal const string ParentRecordFile = "deep-rematch.parent.ron";
    internal const string ColdSeedRecordFile = "deep-rematch.cold-seed.ron";
    private const string ChildCopyRecordFile = "deep-rematch.child-copy.ron";
    private const string CalibrationRecordFile = "deep-rematch.calibration.ron";
    private const string EvaluationRecordFile = "deep-rematch.evaluation.ron";
    internal const string EvaluationCallbackRecordFile = "deep-rematch.evaluation-callback.ron";
    internal const string AccountingRecordFile = "deep-rematch.accounting.ron";

    internal static CortexForkPreparationRoles PreparationRoleForRail(CortexForkRailRoles role)
        => role switch
        {
            CortexForkRailRoles.Baseline => CortexForkPreparationRoles.Baseline,
            CortexForkRailRoles.Candidate => CortexForkPreparationRoles.Candidate,
            CortexForkRailRoles.ForcedNull => CortexForkPreparationRoles.ForcedNull,
            CortexForkRailRoles.ReflexFrozen => CortexForkPreparationRoles.ReflexFrozen,
            _ => CortexForkPreparationRoles.Unknown,
        };
    private const string CompositeRecordFile = "deep-rematch.composite.ron";
    internal const string PhaseJournalFile = "deep-rematch.phase-journal.ron";
    internal const string ColdCheckpointImageFile = "deep-rematch.cold-seed.checkpoint.bin";
    internal const string ColdTapeImageFile = "deep-rematch.cold-seed.tape.spanlog";
    internal const string ColdCurveImageFile = "deep-rematch.cold-seed.curve.tsv";
    internal const string ResumeAttemptFile = "deep-rematch.resume-attempt.ron";
    internal const string AttemptJournalFile = "deep-rematch.attempt-journal.ron";
    internal const string PreManifestSealIORecordFile = "deep-rematch.pre-manifest-seal-io.ron";
    internal const string AttemptDirectory = "attempts";

    internal static DeepRematchCompositeRecord ReadCompositeRecord(Run parent)
    {
        string path = parent.PathOf(CompositeRecordFile);
        if (!File.Exists(path)) throw new InvalidDataException($"missing composite record: {path}");
        int schemaVersion = RonSerializer.Deserialize<DeepRematchSchemaProbe>(File.ReadAllBytes(path)).schemaVersion;
        if (schemaVersion == LegacyCompositeRecordSchemaVersion)
            throw new InvalidDataException("legacy schema-v5 composite manifest has no permanent evaluation callback authority");
        if (schemaVersion != CompositeRecordSchemaVersion)
            throw new InvalidDataException("composite manifest schema is unsupported");
        return Read<DeepRematchCompositeRecord>(path);
    }

    internal static DeepRematchPhaseJournalRecord ReadPhaseJournal(Run parent)
        => Read<DeepRematchPhaseJournalRecord>(parent.PathOf(PhaseJournalFile));

    internal static bool TryReadOpenPhase(
        DeepRematchPhaseJournalRecord journal,
        DeepRematchCompositePhases phase,
        string attemptID,
        out DeepRematchOpenPhase openPhase)
    {
        DeepRematchPhaseRecord? open = null;
        foreach (DeepRematchPhaseRecord entry in journal.entries.OrderBy(static value => value.sequence))
        {
            if (entry.phase != phase.ToString() || entry.attemptID != attemptID)
                continue;
            if (entry.status == "started")
            {
                if (open is not null)
                    throw new InvalidDataException("deep-rematch phase journal contains multiple open matching phases");
                open = entry;
            }
            else if (entry.status == "completed")
            {
                if (open is null)
                    throw new InvalidDataException("deep-rematch phase journal completion has no matching open phase");
                open = null;
            }
        }
        if (open is null)
        {
            openPhase = default;
            return false;
        }
        openPhase = new DeepRematchOpenPhase(journal.parentRunID, phase, attemptID,
            open.sequence, open.startedAtStopwatchTicks, open.entryDigest);
        return true;
    }

    internal static DeepRematchAttemptJournalRecord ReadAttemptJournal(Run parent)
        => Read<DeepRematchAttemptJournalRecord>(parent.PathOf(AttemptJournalFile));

    internal static DeepRematchAttemptAccountingRecord ReadAttemptAccounting(string parentDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("attempt accounting path escaped the parent");
        string path = Path.Combine(parentDirectory, relativePath);
        try
        {
            DeepRematchAttemptAccountingRecord current = Read<DeepRematchAttemptAccountingRecord>(path);
            current.Validate(current.parentRunID, current.attemptID, requireExact: true, parentDirectory: parentDirectory);
            return current;
        }
        catch (Exception currentError) when (currentError is InvalidDataException or RonReadException)
        {
            try
            {
                DeepRematchAttemptAccountingRecord legacy = Read<DeepRematchLegacyAttemptAccountingRecord>(path).ToCurrent();
                legacy.Validate(legacy.parentRunID, legacy.attemptID, requireExact: true, parentDirectory: parentDirectory);
                return legacy;
            }
            catch (Exception legacyError) when (legacyError is InvalidDataException or RonReadException)
            {
                throw new InvalidDataException("attempt accounting is neither current schema-v6 nor canonical legacy schema-v5", currentError);
            }
        }
    }

    internal static string CallbackAttemptPath(string attemptID, int sequence)
    {
        if (string.IsNullOrWhiteSpace(attemptID) || attemptID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidDataException("attempt ID is not a safe callback path atom");
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        return Path.Combine(AttemptDirectory,
            sequence == 0 ? attemptID + ".callback.ron" : $"{attemptID}.callback-{sequence:D4}.ron");
    }

    internal static string CallbackSettlementPath(string attemptID)
    {
        if (string.IsNullOrWhiteSpace(attemptID) || attemptID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidDataException("attempt ID is not a safe callback settlement path atom");
        return Path.Combine(AttemptDirectory, attemptID + ".callback-settlement.ron");
    }

    internal static string EvaluationRecoverySettlementPath(string attemptID)
    {
        if (string.IsNullOrWhiteSpace(attemptID) || attemptID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidDataException("attempt ID is not a safe recovery settlement path atom");
        return Path.Combine(AttemptDirectory, attemptID + ".evaluation-recovery.ron");
    }

    internal static (string RelativePath, DeepRematchCallbackAttemptRecord Record, int Sequence)[] ReadCallbackAttempts(
        Run parent, string attemptID, DeepRematchAttemptTransition transition)
    {
        string attemptsDirectory = parent.PathOf(AttemptDirectory);
        if (!Directory.Exists(attemptsDirectory)) return [];
        string firstName = Path.GetFileName(CallbackAttemptPath(attemptID, 0));
        string prefix = attemptID + ".callback-";
        Dictionary<int, (string RelativePath, DeepRematchCallbackAttemptRecord Record)> records = new();
        foreach (string path in Directory.GetFiles(attemptsDirectory, attemptID + ".callback*.ron")
            .Where(path => !Path.GetFileName(path).EndsWith(".callback-settlement.ron", StringComparison.Ordinal)))
        {
            string name = Path.GetFileName(path);
            int sequence;
            if (string.Equals(name, firstName, StringComparison.Ordinal))
                sequence = 0;
            else if (name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(".ron", StringComparison.Ordinal)
                && int.TryParse(name[prefix.Length..^4], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out sequence)
                && sequence > 0)
            {
                // The filename is the append-only sequence owner for retries.
            }
            else
                throw new InvalidDataException("callback attempt has a non-canonical path");
            if (!string.Equals(name, Path.GetFileName(CallbackAttemptPath(attemptID, sequence)), StringComparison.Ordinal))
                throw new InvalidDataException("callback attempt has a non-canonical sequence path");
            if (!records.TryAdd(sequence, (Path.GetRelativePath(parent.Dir, path), Read<DeepRematchCallbackAttemptRecord>(path))))
                throw new InvalidDataException("callback attempt has duplicate sequence authority");
        }
        if (records.Keys.Any(static sequence => sequence < 0)
            || records.Keys.OrderBy(static sequence => sequence).Select((sequence, index) => sequence != index).Any(static mismatch => mismatch))
            throw new InvalidDataException("callback attempt sequence has a gap or orphan");
        return records.OrderBy(static pair => pair.Key)
            .Select(pair =>
            {
                (string relativePath, DeepRematchCallbackAttemptRecord record) = pair.Value;
                record.Validate(parent.Dir, transition);
                return (relativePath, record, pair.Key);
            }).ToArray();
    }

    internal static void ValidateCallbackAttemptChain(
        string attemptID, string phase, string childRunID, string callbackAttemptPath,
        (string RelativePath, DeepRematchCallbackAttemptRecord Record, int Sequence)[] callbackAttempts,
        DeepRematchAttemptTransition[] callbackBegins, IEnumerable<DeepRematchAttemptTransition> journalEntries)
    {
        if (callbackAttempts.Length == 0)
            throw new InvalidDataException("callback settlement has no callback attempt records");
        string expectedLatestPath = CallbackAttemptPath(attemptID, callbackAttempts[^1].Sequence)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (callbackAttempts.Length != callbackBegins.Length
            || !string.Equals(callbackAttemptPath.Replace(Path.DirectorySeparatorChar, '/'), expectedLatestPath, StringComparison.Ordinal)
            || callbackAttempts[^1].Record.outcome != "completed")
            throw new InvalidDataException("callback settlement is not bound to the latest completed callback attempt");
        for (int index = 0; index < callbackAttempts.Length; index++)
        {
            DeepRematchCallbackAttemptRecord attempt = callbackAttempts[index].Record;
            DeepRematchAttemptTransition begin = callbackBegins[index];
            DeepRematchAttemptTransition? nextBegin = callbackBegins.Skip(index + 1).FirstOrDefault();
            DeepRematchAttemptTransition[] pending = journalEntries
                .Where(entry => entry.attemptID == attemptID && entry.phase == phase && entry.childRunID == childRunID
                    && entry.status == nameof(DeepRematchAttemptStatuses.CallbackPendingOrFailed)
                    && entry.sequence > begin.sequence
                    && (nextBegin is null || entry.sequence < nextBegin.sequence))
                .ToArray();
            if (attempt.outcome == "failed")
            {
                if (pending.Length != 1 || pending[0].detail != attempt.detail
                    || pending[0].terminalReceiptPath != begin.terminalReceiptPath
                    || pending[0].terminalReceiptDigest != begin.terminalReceiptDigest
                    || pending[0].landingOutcomePath != attempt.priorLandingOutcomePath
                    || pending[0].landingOutcomeDigest != attempt.priorLandingOutcomeDigest
                    || pending[0].landingOutcomeState != attempt.priorLandingOutcomeState)
                    throw new InvalidDataException("failed callback attempt is missing its matching pending transition");
            }
            else if (attempt.outcome != "completed" || index != callbackAttempts.Length - 1 || pending.Length != 0)
                throw new InvalidDataException("callback attempt outcome disagrees with its journal transitions");
        }
    }

    internal static void RequireCallbackSettlementChild(string expectedChildID, string candidateChildID)
    {
        if (!string.Equals(expectedChildID, candidateChildID, StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement has a detached authority");
    }
    internal static void ValidateAttemptAccountingBinding(string parentDirectory, DeepRematchAttemptTransition entry)
    {
        const string settlementSuffix = ".callback-settlement.ron";
        if (!entry.accountingPath.EndsWith(settlementSuffix, StringComparison.Ordinal))
            return;
        if (entry.phase != nameof(DeepRematchCompositePhases.Calibration))
            throw new InvalidDataException("callback settlement accounting is bound to a non-calibration attempt");
        if (Path.IsPathRooted(entry.accountingPath) || entry.accountingPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement accounting path escaped the parent");
        string selectedPath = Path.GetFullPath(Path.Combine(parentDirectory, entry.accountingPath));
        string parentRoot = Path.GetFullPath(parentDirectory);
        if (!selectedPath.StartsWith(parentRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(selectedPath)
            || entry.accountingDigest.Length != 64
            || DigestFile(selectedPath) != entry.accountingDigest)
            throw new InvalidDataException("callback settlement accounting binding is missing or changed");
        string selectedRelativePath = Path.GetRelativePath(parentDirectory, selectedPath).Replace(Path.DirectorySeparatorChar, '/');
        string canonicalPath = CallbackSettlementPath(entry.attemptID).Replace(Path.DirectorySeparatorChar, '/');
        if (!string.Equals(selectedRelativePath, canonicalPath, StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement accounting path is not canonical for its attempt");
        DeepRematchCallbackSettlementRecord settlement = Read<DeepRematchCallbackSettlementRecord>(selectedPath);
        if (settlement.recordDigest != settlement.ComputeDigest()
            || settlement.parentRunID != entry.parentRunID
            || settlement.attemptID != entry.attemptID
            || settlement.phase != entry.phase
            || settlement.childRunID != entry.childRunID)
            throw new InvalidDataException("callback settlement accounting tuple disagrees with its terminal transition");
    }

    private enum DeepRematchAttemptDigestFields
    {
        TrainingReceiptDigest,
        TrainingContentDigest,
        TrainingForkAuthorityDigest,
        MountReceiptDigest,
        CursorDigest,
        CheckpointDigest,
    }

    internal static long ReadCalibrationPhaseWallMilliseconds(string parentDirectory)
    {
        string parentPath = Path.GetFullPath(parentDirectory);
        DeepRematchPhaseJournalRecord journal = Read<DeepRematchPhaseJournalRecord>(Path.Combine(parentPath, PhaseJournalFile));
        journal.Validate(parentPath);
        DeepRematchPhaseRecord[] completed = journal.entries
            .Where(static entry => entry.phase == nameof(DeepRematchCompositePhases.Calibration) && entry.status == "completed")
            .ToArray();
        if (completed.Length != 1)
            throw new InvalidDataException("deep-rematch calibration phase is not uniquely completed");
        return completed[0].wallMilliseconds;
    }

    internal static DeepRematchCalibrationReceipt LoadCalibrationReceipt(Run parent, string calibrationDirectory)
    {
        string settlement = FindCallbackSettlement(parent, calibrationDirectory);
        if (settlement.Length != 0)
            return LoadCalibrationAuthority(parent, calibrationDirectory, settlement).Receipt;
        DeepRematchParentRecord parentRecord = Read<DeepRematchParentRecord>(parent.PathOf(ParentRecordFile));
        DeepRematchColdSeedRecord cold = Read<DeepRematchColdSeedRecord>(parent.PathOf(ColdSeedRecordFile));
        DeepRematchChildCopyRecord copy = Read<DeepRematchChildCopyRecord>(Path.Combine(calibrationDirectory, ChildCopyRecordFile));
        DeepRematchCalibrationRecord calibration = Read<DeepRematchCalibrationRecord>(Path.Combine(calibrationDirectory, CalibrationRecordFile));
        parentRecord.Validate(parent.Dir);
        cold.Validate(parentRecord);
        copy.Validate(parentRecord, cold, CortexForkRailRoles.Calibration);
        calibration.Validate(parentRecord, cold, copy, ReadCalibrationPhaseWallMilliseconds(parent.Dir));
        CortexForkSeedLoadRailDocument sidecarDocument = CortexForkTerminalRunReceipt.ReadSeedRailDocument(Path.Combine(calibrationDirectory, "seed-load-receipt.ron"));
        CortexForkSeedLoadReceipt seedLoad = copy.ToSeedLoadReceipt(CortexForkRailRoles.Calibration);
        CortexForkSeedLoadReceipt bindingReceipt = sidecarDocument.IsLegacy
            ? seedLoad with
            {
                AncestorSeedDigest = "",
                PreparedSeedDigest = "",
                PreparationRole = CortexForkPreparationRoles.Unknown,
            }
            : seedLoad;
        if (!seedLoad.Bound || sidecarDocument.StoredBindingDigest != bindingReceipt.BindingDigest)
            throw new InvalidDataException("calibration seed-load authority is not resume-safe");
        return new DeepRematchCalibrationReceipt(
            parentRecord.runID, calibration.childRunDirectory,
            new CortexForkStepSpan(calibration.seedStartStep, calibration.plannedNextStep, calibration.actualNextStep),
            seedLoad, new CortexForkDigests(calibration.finalCheckpointSHA256, calibration.finalTapeSpanlogSHA256, calibration.finalCurveSHA256),
            calibration.training, calibration.trainingPath, calibration.authorityWallMilliseconds,
            calibration.trainingWallMilliseconds, calibration.runtimeBindWallMilliseconds,
            calibration.executionWallMilliseconds, calibration.terminalVerifierWallMilliseconds,
            calibration.wallMilliseconds,
            calibration.terminalCheckpointExact);
    }

    internal static DeepRematchCalibrationAuthority LoadCalibrationAuthority(Run parent, string calibrationDirectory)
        => LoadCalibrationAuthority(parent, calibrationDirectory, FindCallbackSettlement(parent, calibrationDirectory));

    internal static DeepRematchCalibrationAuthority LoadEvaluationSealCalibrationAuthority(Run parent, string calibrationDirectory)
    {
        DeepRematchCalibrationAuthority authority = LoadCalibrationAuthority(parent, calibrationDirectory);
        authority.Validate();
        return authority;
    }

    internal static DeepRematchCalibrationAuthority LoadCalibrationAuthority(
        Run parent, string calibrationDirectory, string settlementPath)
        => LoadCalibrationAuthority(parent, calibrationDirectory, settlementPath, requireSelectedPath: settlementPath.Length != 0);

    private static DeepRematchCalibrationAuthority LoadCalibrationAuthority(
        Run parent, string calibrationDirectory, string settlementPath, bool requireSelectedPath)
    {
        if (settlementPath.Length == 0)
        {
            DeepRematchParentRecord parentRecord = Read<DeepRematchParentRecord>(parent.PathOf(ParentRecordFile));
            DeepRematchColdSeedRecord cold = Read<DeepRematchColdSeedRecord>(parent.PathOf(ColdSeedRecordFile));
            DeepRematchChildCopyRecord copy = Read<DeepRematchChildCopyRecord>(Path.Combine(calibrationDirectory, ChildCopyRecordFile));
            DeepRematchCalibrationRecord calibration = Read<DeepRematchCalibrationRecord>(Path.Combine(calibrationDirectory, CalibrationRecordFile));
            parentRecord.Validate(parent.Dir); cold.Validate(parentRecord);
            copy.Validate(parentRecord, cold, CortexForkRailRoles.Calibration);
            calibration.Validate(parentRecord, cold, copy, ReadCalibrationPhaseWallMilliseconds(parent.Dir));
            return DeepRematchCalibrationAuthority.FromStandard(parentRecord, cold, copy, calibration);
        }

        if (Path.IsPathRooted(settlementPath) || settlementPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement selection escaped parent");
        string selectedSettlementPath = Path.GetFullPath(parent.PathOf(settlementPath));
        string selectedRelativePath = Path.GetRelativePath(parent.Dir, selectedSettlementPath).Replace(Path.DirectorySeparatorChar, '/');
        if (requireSelectedPath && !string.Equals(selectedRelativePath, settlementPath.Replace(Path.DirectorySeparatorChar, '/'), StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement selection is not canonical");
        DeepRematchCallbackSettlementRecord settlement = Read<DeepRematchCallbackSettlementRecord>(selectedSettlementPath);
        settlement.Validate(parent.Dir);
        if (!string.Equals(selectedRelativePath, CallbackSettlementPath(settlement.attemptID).Replace(Path.DirectorySeparatorChar, '/'), StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement selection is not its attempt authority");
        if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(parent.Dir, settlement.terminalReceiptPath)))),
                Path.GetFileName(calibrationDirectory), StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement is not the selected calibration child");
        string[] siblingSettlements = Directory.Exists(parent.PathOf(AttemptDirectory))
            ? Directory.GetFiles(parent.PathOf(AttemptDirectory), "*.callback-settlement.ron")
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    string suffix = ".callback-settlement.ron";
                    if (!name.EndsWith(suffix, StringComparison.Ordinal))
                        return false;
                    string attemptID = name[..^suffix.Length];
                    CallbackSettlementPath(attemptID);
                    DeepRematchCallbackSettlementRecord candidate = Read<DeepRematchCallbackSettlementRecord>(path);
                    candidate.Validate(parent.Dir);
                    RequireCallbackSettlementChild(settlement.childRunID, candidate.childRunID);
                    return true;
                }).ToArray()
            : [];
        if (siblingSettlements.Length != 1 || !string.Equals(Path.GetFullPath(siblingSettlements[0]), selectedSettlementPath, StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement has duplicate or detached same-child authorities");
        DeepRematchLegacyTerminalAuthority legacy = DeepRematchLegacyTerminalAuthority.Read(calibrationDirectory);
        DeepRematchLegacyTerminalRunDocument terminal = legacy.TerminalRun;
        PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.Decode(
            File.ReadAllBytes(Path.Combine(calibrationDirectory, "policy-boundary.training.ron")), HomeostatPolicyBoundaryDomain.Instance);
        CheckpointRoundTripProof proof = new(settlement.currentProofEffectiveImageSHA256,
            settlement.currentProofEffectivePhysicalSHA256, settlement.currentProofBasePhysicalSHA256,
            settlement.currentProofPhysicalChainSHA256, settlement.currentProofPersistedConfigDigest,
            settlement.currentProofNextStep, settlement.currentProofSaveLoadSaveExact);
        CortexForkSeedLoadReceipt seedLoad = new(
            new CortexForkDigests(terminal.expectedCheckpointSHA256, terminal.expectedTapeSpanlogSHA256, terminal.expectedCurveSHA256),
            new CortexForkDigests(terminal.loadedCheckpointSHA256, terminal.loadedTapeSpanlogSHA256, terminal.loadedCurveSHA256),
            terminal.seedIOWallMilliseconds, terminal.parentRunID, terminal.childRunID, CortexForkRailRoles.Calibration,
            terminal.coldSeedDigest, terminal.persistedConfigDigest,
            new CortexExecutionWindow(terminal.startStep, terminal.plannedNextStep), terminal.sourceSeedDigest,
            terminal.sourceRunID, terminal.sourceNextStep, terminal.seedIORawTicks, proof, true);
        DeepRematchCalibrationReceipt receipt = new(
            terminal.parentRunID, calibrationDirectory,
            new CortexForkStepSpan(terminal.startStep, terminal.plannedNextStep, terminal.actualNextStep),
            seedLoad,
            new CortexForkDigests(terminal.finalCheckpointSHA256, terminal.finalTapeSpanlogSHA256, terminal.finalCurveSHA256),
            training, "policy-boundary.training.ron", 0, 0,
            terminal.runtimeBindWallMilliseconds, terminal.executionWallMilliseconds,
            terminal.terminalVerifierWallMilliseconds, settlement.totalWallMilliseconds,
            terminal.terminalCheckpointExact);
        return DeepRematchCalibrationAuthority.FromSettlement(parent, settlement, legacy, receipt, proof);
    }

    private static string FindCallbackSettlement(Run parent, string calibrationDirectory)
    {
        string attempts = parent.PathOf(AttemptDirectory);
        if (!Directory.Exists(attempts)) return "";
        string childID = Run.RunIDFromDirectory(calibrationDirectory);
        List<string> matches = [];
        foreach (string path in Directory.GetFiles(attempts, "*.callback-settlement.ron"))
        {
            string name = Path.GetFileName(path);
            const string suffix = ".callback-settlement.ron";
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string attemptID = name[..^suffix.Length];
            string canonicalPath = CallbackSettlementPath(attemptID);
            if (!string.Equals(name, Path.GetFileName(canonicalPath), StringComparison.Ordinal))
                throw new InvalidDataException("callback settlement has a non-canonical path");
            DeepRematchCallbackSettlementRecord record = Read<DeepRematchCallbackSettlementRecord>(path);
            record.Validate(parent.Dir);
            RequireCallbackSettlementChild(childID, record.childRunID);
            matches.Add(path);
        }
        if (matches.Count > 1)
            throw new InvalidDataException("callback settlement has duplicate same-child authorities");
        if (matches.Count == 0) return "";
        string selected = matches[0];
        DeepRematchAttemptJournalRecord journal = ReadAttemptJournal(parent);
        journal.Validate(parent.Dir);
        (string AttemptID, DeepRematchAttemptStatuses Status, DeepRematchAttemptTransition Entry)[] current = journal.Current();
        DeepRematchAttemptTransition calibration = current
            .SingleOrDefault(item => item.Entry.phase == nameof(DeepRematchCompositePhases.Calibration)
                && item.Entry.childRunID == childID).Entry;
        if (calibration is not null && !string.Equals(Path.GetFullPath(selected),
                Path.GetFullPath(parent.PathOf(CallbackSettlementPath(calibration.attemptID))), StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement is not bound to the current calibration attempt");
        return Path.GetRelativePath(parent.Dir, selected);
    }

    internal static string DigestCheckpointImage(string path)
        => Checkpoint.LogicalStateSHA256(File.ReadAllBytes(path));

    internal static bool TryReadExactTerminalReceipt(
        string parentDirectory, string childRunID, out string relativePath, out string digest)
    {
        relativePath = "";
        digest = "";
        try
        {
            string parent = Path.GetFullPath(parentDirectory);
            string child = Path.GetFullPath(Path.Combine(parent, "children", childRunID));
            string path = Path.Combine(child, CortexForkTerminalRunReceipt.FileName);
            if (!Directory.Exists(child) || !File.Exists(path)) return false;
            CortexForkTerminalRunReceipt receipt = CortexForkTerminalRunReceipt.Read(child);
            if (receipt.exitCode != 0 || !receipt.terminalCheckpointExact || !receipt.terminalOccurrenceCheckExact)
                return false;
            relativePath = Path.GetRelativePath(parent, path);
            digest = DigestFile(path);
            return digest.Length == 64;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or RonReadException)
        {
            try
            {
                string parent = Path.GetFullPath(parentDirectory);
                string child = Path.GetFullPath(Path.Combine(parent, "children", childRunID));
                string path = Path.Combine(child, CortexForkTerminalRunReceipt.FileName);
                if (!File.Exists(path)) return false;
                DeepRematchLegacyTerminalAuthority authority = DeepRematchLegacyTerminalAuthority.Read(child);
                if (!authority.TerminalRun.terminalCheckpointExact || !authority.TerminalRun.terminalOccurrenceCheckExact)
                    return false;
                relativePath = Path.GetRelativePath(parent, path);
                digest = DigestFile(path);
                return digest.Length == 64;
            }
            catch (Exception) { return false; }
        }
    }

    internal static bool TryReadLandingOutcome(
        string parentDirectory, string childRunID, out string relativePath, out string digest,
        out CortexForkLandingOutcomeStates state)
    {
        relativePath = "";
        digest = "";
        state = default;
        try
        {
            string parent = Path.GetFullPath(parentDirectory);
            string child = Path.GetFullPath(Path.Combine(parent, "children", childRunID));
            string path = Path.Combine(child, CortexForkLandingOutcomeReceipt.FileName);
            if (!File.Exists(path)) return false;
            CortexForkLandingOutcomeReceipt receipt = CortexForkLandingOutcomeReceipt.Read(child);
            relativePath = Path.GetRelativePath(parent, path);
            digest = DigestFile(path);
            state = receipt.state;
            return digest.Length == 64;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or RonReadException)
        {
            try
            {
                string parent = Path.GetFullPath(parentDirectory);
                string child = Path.GetFullPath(Path.Combine(parent, "children", childRunID));
                string path = Path.Combine(child, CortexForkLandingOutcomeReceipt.FileName);
                if (!File.Exists(path)) return false;
                state = ReadLandingOutcomeState(path);
                relativePath = Path.GetRelativePath(parent, path);
                digest = DigestFile(path);
                return digest.Length == 64;
            }
            catch (Exception) { return false; }
        }
    }

    internal static CortexForkLandingOutcomeStates ReadLandingOutcomeState(string landingPath)
    {
        string childDirectory = Path.GetDirectoryName(Path.GetFullPath(landingPath))
            ?? throw new InvalidDataException("callback landing path has no child directory");
        try
        {
            return CortexForkLandingOutcomeReceipt.Read(childDirectory).state;
        }
        catch (InvalidDataException)
        {
            DeepRematchLegacyTerminalAuthority authority = DeepRematchLegacyTerminalAuthority.Read(childDirectory);
            byte[] bytes = File.ReadAllBytes(landingPath);
            CortexForkLandingOutcomeReceipt landing = RonSerializer.Deserialize<CortexForkLandingOutcomeReceipt>(bytes);
            if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in landing))
                || landing.schemaVersion != CortexForkLandingOutcomeReceipt.CurrentSchemaVersion
                || landing.parentRunID != authority.TerminalRun.parentRunID
                || landing.childRunID != authority.TerminalRun.childRunID
                || landing.role != authority.TerminalRun.role
                || landing.terminalRunReceiptSHA256 != authority.TerminalRunReceiptSHA256
                || landing.terminalReceiptDigest != authority.TerminalRun.receiptDigest
                || !landing.authorityChainExact
                || landing.authorityBeforeSeedIntentSHA256 != landing.authorityAfterSeedIntentSHA256
                || landing.authorityBeforeSeedReceiptSHA256 != landing.authorityAfterSeedReceiptSHA256
                || landing.authorityBeforeVerifierSHA256 != landing.authorityAfterVerifierSHA256
                || landing.authorityBeforeTerminalSHA256 != landing.authorityAfterTerminalSHA256
                || landing.authorityBeforeSeedIntentSHA256 != authority.SeedLoadIntentSHA256
                || landing.authorityBeforeSeedReceiptSHA256 != authority.SeedLoadReceiptSHA256
                || landing.authorityBeforeTerminalSHA256 != authority.TerminalRunReceiptSHA256
                || landing.authorityBeforeVerifierSHA256 != (authority.TerminalOccurrenceCheckSHA256 ?? "")
                || landing.receiptDigest != ComputeLandingDigest(landing))
                throw new InvalidDataException("legacy callback landing outcome is not bound to the validated terminal authority");
            return landing.state;
        }
    }

    private static string ComputeLandingDigest(CortexForkLandingOutcomeReceipt landing)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            landing.schemaVersion, landing.parentRunID, landing.childRunID, landing.role, landing.state,
            landing.callbackAttempted, landing.callbackReturned, landing.callbackExceptionType,
            landing.callbackExceptionMessage, landing.callbackWallMilliseconds, landing.callbackRawTicks,
            landing.terminalRunReceiptSHA256, landing.terminalReceiptDigest,
            landing.authorityBeforeSeedIntentSHA256, landing.authorityBeforeSeedReceiptSHA256,
            landing.authorityBeforeVerifierSHA256, landing.authorityBeforeTerminalSHA256,
            landing.authorityAfterSeedIntentSHA256, landing.authorityAfterSeedReceiptSHA256,
            landing.authorityAfterVerifierSHA256, landing.authorityAfterTerminalSHA256, landing.authorityChainExact))));

    internal static string DigestCheckpointImage(string parentDirectory, string relativePath)
        => DigestCheckpointImage(Path.Combine(parentDirectory, relativePath));

    internal static string ComputePhaseDigest(DeepRematchPhaseRecord entry)
    {
        DeepRematchPhaseRecord copy = new()
        {
            schemaVersion = entry.schemaVersion,
            parentRunID = entry.parentRunID,
            sequence = entry.sequence,
            phase = entry.phase,
            attemptID = entry.attemptID,
            status = entry.status,
            startedAtStopwatchTicks = entry.startedAtStopwatchTicks,
            finishedAtStopwatchTicks = entry.finishedAtStopwatchTicks,
            wallMilliseconds = entry.wallMilliseconds,
            detail = entry.detail,
            previousDigest = entry.previousDigest,
            entryDigest = "",
        };
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(RonSerializer.SerializeToUtf8(in copy)));
    }

    internal static string ComputeAttemptDigest(DeepRematchAttemptTransition entry)
    {
        DeepRematchAttemptTransition copy = new()
        {
            schemaVersion = entry.schemaVersion,
            parentRunID = entry.parentRunID,
            sequence = entry.sequence,
            attemptID = entry.attemptID,
            phase = entry.phase,
            childRunID = entry.childRunID,
            status = entry.status,
            accountingPath = entry.accountingPath,
            accountingDigest = entry.accountingDigest,
            terminalReceiptPath = entry.terminalReceiptPath,
            terminalReceiptDigest = entry.terminalReceiptDigest,
            landingOutcomePath = entry.landingOutcomePath,
            landingOutcomeDigest = entry.landingOutcomeDigest,
            landingOutcomeState = entry.landingOutcomeState,
            detail = entry.detail,
            previousDigest = entry.previousDigest,
            entryDigest = "",
        };
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(RonSerializer.SerializeToUtf8(in copy)));
    }

    internal static bool VerifyPersisted(string parentDirectory, TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentNullException.ThrowIfNull(output);
        string parentPath = Path.GetFullPath(parentDirectory);
        try
        {
            Run parent = Run.Open(parentPath);
            DeepRematchCompositeRecord manifest = ReadCompositeRecord(parent);
            string calibrationDirectory = manifest.calibrationAuthorityPath.Length != 0
                ? ResolveManifestCalibrationDirectory(parent, manifest.calibrationAuthorityPath)
                : ResolveManifestChildDirectory(parent.Dir, manifest.calibrationRecordPath, "calibration record");
            string evaluationDirectory = ResolveManifestChildDirectory(parent.Dir, manifest.evaluationRecordPath, "evaluation record");
            PersistedDocuments documents = ReadPersistedDocuments(parent, calibrationDirectory, evaluationDirectory);
            ValidatePersistedDocuments(parent, documents);
            string gateConfigDigest = ReadHistoricalGateConfigDigest(parent);
            bool a3Rails = ValidateHistoricalGateReceipts(documents.CalibrationAuthority.ChildRunDirectory, gateConfigDigest,
                out int physicalRails, out int nestedRails);
            // Historical verification reads typed calibration/evaluation records and their
            // legacy terminal/checkpoint authorities. It never collects a new artifact or
            // runs the retired corruption fixtures.
            int physicalRowsExpected = documents.Evaluation.plannedNextStep - documents.Evaluation.seedStartStep;
            int scoredRowsExpected = physicalRowsExpected - 1;
            bool evaluationRows = physicalRowsExpected > 1;
            int curveRows = CountDataRows(evaluationDirectory, "curve.tsv");
            int computeRows = CountDataRows(evaluationDirectory, "compute.tsv");
            output.WriteLine($"  deep-rematch composite persisted verification · manifest=schema-v{manifest.schemaVersion} · typed-records=PASS · rails={physicalRails}/{12},nested-trials={nestedRails} · evaluation-rows=physical curve {curveRows}/{physicalRowsExpected}, compute {computeRows}/{physicalRowsExpected}; scored={scoredRowsExpected} · corruption=not-run/read-only · {(a3Rails && evaluationRows && curveRows == physicalRowsExpected && computeRows == physicalRowsExpected ? "PASS" : "FAIL")}");
            return a3Rails && evaluationRows && curveRows == physicalRowsExpected && computeRows == physicalRowsExpected;
        }
        catch (Exception ex) when (ex is InvalidDataException or RonReadException or IOException
            or FormatException or OverflowException or ArgumentException)
        {
            output.WriteLine($"  deep-rematch composite persisted verification · error={ex.Message} · FAIL");
            return false;
        }
    }

    private static int CountDataRows(string childDirectory, string file)
    {
        string path = ResolveChildArtifact(childDirectory, file, file);
        int rows = 0;
        foreach (string line in File.ReadLines(path))
            if (rows > 0 || !string.IsNullOrWhiteSpace(line)) rows++;
        return Math.Max(0, rows - 1);
    }

    private static string ResolveManifestChildDirectory(string parentDirectory, string relativeRecordPath, string label)
    {
        if (string.IsNullOrWhiteSpace(relativeRecordPath)
            || Path.IsPathRooted(relativeRecordPath)
            || relativeRecordPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} path escaped parent");
        string parent = Path.GetFullPath(parentDirectory);
        string record = Path.GetFullPath(Path.Combine(parent, relativeRecordPath));
        if (!record.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} path escaped parent");
        string child = Path.GetFullPath(Path.GetDirectoryName(record) ?? throw new InvalidDataException($"composite {label} path has no child"));
        string children = Path.Combine(parent, "children") + Path.DirectorySeparatorChar;
        if (!child.StartsWith(children, StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} path is not under children");
        return child;
    }

    private static string ResolveManifestCalibrationDirectory(Run parent, string relativeAuthorityPath)
    {
        if (Path.IsPathRooted(relativeAuthorityPath) || relativeAuthorityPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("composite calibration authority path escaped parent");
        DeepRematchCallbackSettlementRecord settlement = Read<DeepRematchCallbackSettlementRecord>(parent.PathOf(relativeAuthorityPath));
        string child = Path.Combine(parent.Dir, "children", settlement.childRunID);
        if (!Directory.Exists(child)) throw new InvalidDataException("composite calibration authority child is missing");
        return child;
    }

    private static void ValidatePersistedDocuments(Run parent, PersistedDocuments documents)
    {
        DeepRematchPhaseJournalRecord phaseJournal = Read<DeepRematchPhaseJournalRecord>(parent.PathOf(PhaseJournalFile));
        phaseJournal.Validate(parent.Dir);
        if (documents.CalibrationAuthority.IsSettlement)
        {
            documents.CalibrationAuthority.Validate();
            documents.Evaluation.Validate(documents.Parent, documents.ColdSeed, documents.EvaluationCopy, documents.CalibrationAuthority);
        }
        else
            ValidateReceiptSet(parent, documents.Parent, documents.ColdSeed, documents.CalibrationCopy,
                documents.Calibration, documents.EvaluationCopy, documents.Evaluation);
        documents.Accounting.Validate(documents.Manifest.measuredWallMilliseconds);
        documents.Manifest.ValidatePersisted(parent.Dir, documents.Parent, documents.ColdSeed,
            documents.CalibrationCopy, documents.Calibration, documents.EvaluationCopy,
            documents.Evaluation, documents.Accounting, documents.CalibrationAuthority);
    }

    private readonly record struct PersistedDocuments(
        DeepRematchParentRecord Parent,
        DeepRematchColdSeedRecord ColdSeed,
        DeepRematchChildCopyRecord CalibrationCopy,
        DeepRematchCalibrationRecord Calibration,
        DeepRematchChildCopyRecord EvaluationCopy,
        DeepRematchEvaluationRecord Evaluation,
        DeepRematchAccountingRecord Accounting,
        DeepRematchCompositeRecord Manifest,
        DeepRematchCalibrationAuthority CalibrationAuthority);

    private static PersistedDocuments ReadPersistedDocuments(Run parent, string calibrationDirectory, string evaluationDirectory)
    {
        DeepRematchCompositeRecord manifest = ReadCompositeRecord(parent);
        DeepRematchParentRecord parentRecord = Read<DeepRematchParentRecord>(parent.PathOf(ParentRecordFile));
        DeepRematchColdSeedRecord cold = Read<DeepRematchColdSeedRecord>(parent.PathOf(ColdSeedRecordFile));
        DeepRematchCalibrationAuthority authority = manifest.calibrationAuthorityPath.Length == 0
            ? LoadCalibrationAuthority(parent, calibrationDirectory)
            : LoadCalibrationAuthority(parent, calibrationDirectory, manifest.calibrationAuthorityPath);
        return new(
            parentRecord, cold, authority.Copy ?? new(), authority.Calibration ?? new(),
            Read<DeepRematchChildCopyRecord>(Path.Combine(evaluationDirectory, ChildCopyRecordFile)),
            Read<DeepRematchEvaluationRecord>(Path.Combine(evaluationDirectory, EvaluationRecordFile)),
            Read<DeepRematchAccountingRecord>(parent.PathOf(manifest.accountingRecordPath)), manifest, authority);
    }

    private static string ReadHistoricalGateConfigDigest(Run parent)
    {
        string gatePath = parent.PathOf("deep-rematch-gate.ron");
        string gateDigestPath = parent.PathOf("deep-rematch-gate.digest");
        DeepRematchGateConfig gate = DeepRematchGate.DecodeConfig(File.ReadAllBytes(gatePath));
        string persistedDigest = File.ReadAllText(gateDigestPath).Trim('\uFEFF', ' ', '\r', '\n', '\t');
        if (!string.Equals(persistedDigest, gate.ConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException("historical gate config digest sidecar disagrees with its typed config");
        return gate.ConfigDigest;
    }

    private static bool ValidateHistoricalGateReceipts(
        string calibrationDirectory, string gateConfigDigest, out int physicalRails, out int nestedRails)
    {
        physicalRails = 0;
        nestedRails = 0;
        if (!IsHistoricalCheckpointDialect(calibrationDirectory))
            throw new InvalidDataException("historical calibration custody requires an effective CORTEXO checkpoint image");
        string runID = Run.RunIDFromDirectory(calibrationDirectory);
        string checkpointDigest = DigestCheckpoint(calibrationDirectory);
        DeepRematchA3Receipt a3 = ValidateHistoricalGateReceipt<DeepRematchA3Receipt>(calibrationDirectory, "deep-rematch.a3.ron", runID, checkpointDigest, gateConfigDigest);
        DeepRematchRung0Receipt rung0 = ValidateHistoricalGateReceipt<DeepRematchRung0Receipt>(calibrationDirectory, "deep-rematch.rung0.ron", runID, checkpointDigest, gateConfigDigest);
        DeepRematchCheckpointReceipt checkpoint = ValidateHistoricalGateReceipt<DeepRematchCheckpointReceipt>(calibrationDirectory, "deep-rematch.checkpoint.ron", runID, checkpointDigest, gateConfigDigest);
        DeepRematchFundingReceipt funding = ValidateHistoricalGateReceipt<DeepRematchFundingReceipt>(calibrationDirectory, "deep-rematch.funding.ron", runID, checkpointDigest, gateConfigDigest);
        DeepRematchPolicyReceipt policy = ValidateHistoricalGateReceipt<DeepRematchPolicyReceipt>(calibrationDirectory, "deep-rematch.policy.ron", runID, checkpointDigest, gateConfigDigest);
        string controlPath = Path.Combine(calibrationDirectory, "deep-rematch.rung0-control.ron");
        DeepRematchGate.ValidateSettlementControl(controlPath, runID, checkpointDigest, gateConfigDigest);
        EmlIntensionalRematchControlReceipt control = RonSerializer.Deserialize<EmlIntensionalRematchControlReceipt>(File.ReadAllBytes(controlPath));
        PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.Decode(
            File.ReadAllBytes(Path.Combine(calibrationDirectory, "policy-boundary.training.ron")), HomeostatPolicyBoundaryDomain.Instance);
        training.Validate(HomeostatPolicyBoundaryDomain.Instance);
        if (training.ForkAuthority is null)
            throw new InvalidDataException("historical policy-boundary training receipt has no ForkAuthority");
        PolicyBoundaryForkReceipt authority = training.ForkAuthority.ToDomain();
        authority.Validate(HomeostatPolicyBoundaryDomain.Instance);
        (physicalRails, nestedRails) = CensusImmediateRails(calibrationDirectory, authority);
        bool forkRails = authority.Horizons.SequenceEqual([16, 64, 256])
            && authority.Arms.Length == 12
            && physicalRails == authority.Arms.Length
            && nestedRails == 0
            && training.CheckpointReceiptDigest == checkpointDigest;
        return forkRails
            && a3.PaidArms == 4 && a3.HorizonShort == 16 && a3.HorizonMedium == 64 && a3.HorizonLong == 256
            && a3.Spend > 0 && a3.NullDivergentExecutions > 0
            && rung0.AssayStatus == nameof(EmlRematchAssayStatuses.Exact)
            && rung0.ShadowPowerStatus == nameof(EmlRematchPowerStatuses.Unpowered)
            && rung0.NullPowerStatus == nameof(EmlRematchPowerStatuses.Unpowered)
            && rung0.ComposedPredictions >= 0 && rung0.EvaluatorCalls >= 0 && rung0.AuditFailures >= 0
            && rung0.NullExecutions >= 0 && rung0.NullAuthorityPredictions >= 0
            && rung0.ControlReceiptDigest == control.ReceiptDigest
            && rung0.NullExecutions == control.RelationNullExecutions
            && rung0.NullAuthorityPredictions == control.RelationNullAuthorityPredictions
            && checkpoint.Mismatches == 0 && checkpoint.Dialect == "CORTEXO"
            && checkpoint.SaveDigest == checkpointDigest && checkpoint.LoadSaveDigest == checkpointDigest
            && funding.Axes.Count == 12
            && policy.ReadoutPaidCloses >= 0 && policy.TreeEraPaidCloses >= 0
            && policy.ReadoutSpend >= 0 && policy.TreeEraSpend >= 0
            && policy.NullDivergentExecutions == a3.NullDivergentExecutions
            && policy.ReflexControlAdaptations >= 0;
    }

    private static (int PhysicalRails, int NestedRails) CensusImmediateRails(
        string calibrationDirectory, PolicyBoundaryForkReceipt authority)
    {
        string childrenDirectory = Path.Combine(calibrationDirectory, "children");
        if (!Directory.Exists(childrenDirectory))
            throw new InvalidDataException("historical calibration is missing its physical rail directory");
        string[] rails = Directory.GetDirectories(childrenDirectory);
        int nested = rails.Sum(static rail => Directory.GetDirectories(rail, "*", SearchOption.AllDirectories).Length);
        HashSet<string> expected = authority.Arms
            .Select(static arm => $"{arm.Arm}:{arm.Horizon}")
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> observed = new(StringComparer.Ordinal);
        (string expectedAttemptID, int expectedStep) = ReadSettledBoundaryAttempt(calibrationDirectory);
        string? expectedColdSeedDigest = null;
        int? expectedMaterializationStep = null;
        foreach (string rail in rails)
        {
            string railPath = Path.Combine(rail, "policy-boundary.rail.ron");
            if (!File.Exists(railPath))
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} is missing typed policy-boundary metadata");
            byte[] bytes = File.ReadAllBytes(railPath);
            PolicyBoundaryRailMetadataDocument document = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(bytes);
            string childID = Path.GetFileName(rail);
            string markerPath = Path.Combine(rail, CortexForkMaterializationContract.MarkerFileName);
            CortexForkMaterializationContract marker = ReadMaterializationContract(markerPath, rail);
            string terminalOccurrenceCheckPath = Path.Combine(rail, "terminal-verification.ron");
            if (!File.Exists(terminalOccurrenceCheckPath))
                throw new InvalidDataException($"historical calibration rail {childID} is missing its terminal verification receipt");
            CortexForkTerminalRunReceipt terminalRun = CortexForkTerminalRunReceipt.Read(rail);
            if (!terminalRun.terminalCheckpointExact || !terminalRun.terminalOccurrenceCheckExact || terminalRun.exitCode != 0)
                throw new InvalidDataException($"historical calibration rail {childID} terminal receipt is not exact");
            CortexForkRailRoles expectedRole = document.arm switch
            {
                PolicyBoundaryArms.Baseline => CortexForkRailRoles.Baseline,
                PolicyBoundaryArms.Candidate => CortexForkRailRoles.Candidate,
                PolicyBoundaryArms.ForcedDivergentNull => CortexForkRailRoles.ForcedNull,
                PolicyBoundaryArms.ReflexFrozenControl => CortexForkRailRoles.ReflexFrozen,
                _ => CortexForkRailRoles.Unknown,
            };
            CortexForkTerminalOccurrenceCheckReceipt terminalOccurrenceCheck = CortexForkTerminalRunReceipt.ReadTerminalOccurrenceCheckDocument(
                terminalOccurrenceCheckPath, PreparationRoleForRail(expectedRole)).Receipt;
            terminalOccurrenceCheck.Validate(childID, marker.ColdSeedDigest);
            expectedColdSeedDigest ??= marker.ColdSeedDigest;
            expectedMaterializationStep ??= document.step;
            PolicyBoundaryArmReceipt executedArm = PolicyBoundaryRailMetadata.CreateArmReceipt(
                document, document.matchedSpend, HomeostatPolicyBoundaryDomain.Instance);
            try { executedArm.ValidateExecutedDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance); }
            catch (InvalidDataException) { throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} has malformed executed decision custody"); }
            string decisionJournal = Path.Combine(rail, "policy_decisions.tsv");
            if (!File.Exists(decisionJournal))
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} is missing its policy decision custody");
            CortexPolicyDecisionReadoutOccurrenceCheck decisionOccurrenceCheck = CortexPolicyDecisionReadoutVerifier.Verify(rail, TextWriter.Null);
            if (!decisionOccurrenceCheck.Passed)
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} has unverified policy decision packet/journal custody");
            string[] decisionRows = File.ReadAllLines(decisionJournal, Encoding.UTF8);
            if (decisionRows.Length < 2 || decisionRows[0] != Cortex.PolicyDecisionReceiptHeader)
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} has malformed policy decision custody");
            string[] decisionColumns = decisionRows.Skip(1)
                .Select(row => row.Split('\t'))
                .LastOrDefault(columns => columns.Length == 14)
                ?? throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} has no terminal policy decision custody");
            if (!int.TryParse(decisionColumns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int decisionStep)
                || decisionStep != document.terminalStep
                || decisionColumns[3] != Homeostat.PolicyID.Value)
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} decision step is outside its execution window");
            CortexPolicyDecisionPacket decisionPacket = TapePacketCreator.DecodePolicyDecision(Convert.FromBase64String(decisionColumns[13]));
            if (decisionPacket.Readout.ExecutedAction != document.executedAction
                || decisionPacket.Readout.Authority != document.executedAuthority
                || decisionPacket.Readout.SelectionCause != document.executedSelectionCause
                || decisionPacket.Readout.GrammarRevision.Value != document.executedReadoutRevision
                || decisionPacket.Readout.ReadoutCandidateOccurrenceDigest != document.executedReadoutOccurrenceDigest
                || decisionPacket.Readout.ReadoutCandidateFingerprint != document.executedCandidateFingerprint
                || document.executedReadoutFingerprint != document.readoutFingerprint)
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} executed identity disagrees with its policy decision packet");
            if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in document))
                || document.schemaVersion != 2
                || !Enum.IsDefined(document.arm)
                || !Enum.IsDefined(document.railRole)
                || document.railRole != expectedRole
                || document.horizon <= 0
                || document.readoutFingerprint == 0
                || document.executedReadoutFingerprint == 0
                || document.executedReadoutRevision == 0
                || document.executedReadoutFingerprint != document.readoutFingerprint
                || document.executedReadoutRevision != authority.SourceDecisionReadoutRevision
                || authority.TeacherCorroboration is not null && document.executedReadoutRevision <= authority.TeacherCorroboration.TeacherRevision.Value
                || document.paidCloseDelta < 0
                || document.matchedSpend < 0
                || !document.continuityExact
                || document.grammarExecutionsDelta < 0
                || document.trialAdaptationTransitions < 0
                || document.step <= 0
                || document.terminalStep != document.step + document.horizon
                || terminalOccurrenceCheck.actualNextStep != document.terminalStep
                || terminalRun.actualNextStep != document.terminalStep
                || document.step != expectedStep
                || document.step != expectedMaterializationStep
                || document.materializationParentRunID != Path.GetFileName(calibrationDirectory)
                || document.materializationChildRunID != childID
                || document.materializationAttemptID != expectedAttemptID
                || document.materializationColdSeedDigest != expectedColdSeedDigest
                || marker.ParentRunID != document.materializationParentRunID
                || marker.AttemptID != document.materializationAttemptID
                || marker.ChildRunID != document.materializationChildRunID
                || marker.ColdSeedDigest != document.materializationColdSeedDigest
                || !observed.Add($"{document.arm}:{document.horizon}"))
                throw new InvalidDataException($"historical calibration rail {Path.GetFileName(rail)} has invalid typed policy-boundary metadata");
        }
        if (!expected.SetEquals(observed))
            throw new InvalidDataException("historical calibration physical rails disagree with the typed ForkAuthority arm ladder");
        return (rails.Length, nested);
    }

    internal static bool VerifyPolicyBoundaryRailProjectionFixture()
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        PolicyBoundaryRailMetadataDocument document = new()
        {
            schemaVersion = 2,
            arm = PolicyBoundaryArms.ForcedDivergentNull,
            horizon = 16,
            paidCloseDelta = 3,
            matchedSpend = 7,
            continuityExact = true,
            childProcessCompleted = true,
            grammarExecutionsDelta = 2,
            trialAdaptationTransitions = 1,
            adaptationEnabled = true,
            requestCount = 2,
            guardAdmittedCount = 1,
            lastRequestDecisionID = 41,
            lastRequestStep = 10,
            lastRequestLaunchpadAction = 0,
            lastRequestRawCandidateAction = 1,
            lastRequestSelectedCandidateAction = 1,
            lastRequestExecutedAction = 1,
            lastRequestAuthority = CortexPolicyAuthorities.Grammar,
            lastRequestRevision = 9,
            lastRequestSelectionCause = CortexPolicySelectionCauses.GrammarCandidate,
            lastRequestSupportDigest = 0x101,
            lastRequestCandidateFingerprint = 0x102,
            executedDecisionID = 42,
            executedStep = 11,
            executedLaunchpadAction = 0,
            executedRawCandidateAction = 1,
            executedSelectedCandidateAction = 2,
            executedAction = 2,
            executedAuthority = CortexPolicyAuthorities.Grammar,
            executedSelectionCause = CortexPolicySelectionCauses.TrialOverride,
            executedReadoutFingerprint = 0x103,
            executedReadoutRevision = 9,
            executedReadoutOccurrenceDigest = 0x104,
            executedCandidateFingerprint = 0x105,
            executedDecisionEventID = 43,
            forcedDivergenceSeed = 0x106,
            executedCanonicalPolicy = policy.Value,
            executedCanonicalKind = (byte)PolicyCanonicalStateKinds.Homeostat,
            executedCanonicalVersion = 1,
            executedCanonicalValue = 0x107,
        };

        PolicyBoundaryArmReceipt projected = PolicyBoundaryRailMetadata.CreateArmReceipt(
            document, document.matchedSpend, HomeostatPolicyBoundaryDomain.Instance);
        projected.ValidateRequestAccounting(HomeostatPolicyBoundaryDomain.Instance);
        projected.ValidateExecutedDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance);
        bool exact = projected.ExecutedDecisionID.Value == document.executedDecisionID
            && projected.ExecutedStep == document.executedStep
            && projected.ExecutedLaunchpadAction == document.executedLaunchpadAction
            && projected.ExecutedRawCandidateAction == document.executedRawCandidateAction
            && projected.ExecutedSelectedCandidateAction == document.executedSelectedCandidateAction
            && projected.ExecutedAction == document.executedAction
            && projected.ExecutedAuthority == document.executedAuthority
            && projected.ExecutedSelectionCause == document.executedSelectionCause
            && projected.ExecutedReadoutFingerprint == document.executedReadoutFingerprint
            && projected.ExecutedReadoutRevision == document.executedReadoutRevision
            && projected.ExecutedReadoutOccurrenceDigest == document.executedReadoutOccurrenceDigest
            && projected.ExecutedCandidateFingerprint == document.executedCandidateFingerprint
            && projected.ExecutedCanonicalState.IsValidFor(policy)
            && projected.ExecutedCanonicalState.Value == document.executedCanonicalValue
            && projected.ExecutedDecisionEventID.Value == document.executedDecisionEventID
            && projected.ForcedDivergenceSeed == document.forcedDivergenceSeed
            && projected.Diverged;

        PolicyBoundaryRailMetadataDocument omitted = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(RonSerializer.SerializeToUtf8(in document));
        omitted.executedCanonicalVersion = 0;
        omitted.executedCanonicalPolicy = "";
        omitted.executedCanonicalKind = 0;
        omitted.executedCanonicalValue = 0;
        bool omissionRejected = false;
        try { PolicyBoundaryRailMetadata.CreateArmReceipt(omitted, omitted.matchedSpend,
            HomeostatPolicyBoundaryDomain.Instance).ValidateExecutedDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance); }
        catch (InvalidDataException) { omissionRejected = true; }

        PolicyBoundaryRailMetadataDocument foreign = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(RonSerializer.SerializeToUtf8(in document));
        foreign.executedCanonicalPolicy = "foreign-policy";
        bool foreignScopeRejected = false;
        try { PolicyBoundaryRailMetadata.CreateArmReceipt(foreign, foreign.matchedSpend,
            HomeostatPolicyBoundaryDomain.Instance).ValidateExecutedDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance); }
        catch (InvalidDataException) { foreignScopeRejected = true; }
        return exact && omissionRejected && foreignScopeRejected;
    }

    private static (string AttemptID, int ActualExecutedArmSteps) ReadSettledBoundaryAttempt(string calibrationDirectory)
    {
        CortexPolicyTrialJournalOccurrenceCheck verification = CortexPolicyTrialJournalVerifier.Verify(calibrationDirectory, TextWriter.Null);
        if (!verification.Passed || verification.PaidRows != 1 || verification.CompletionRows != 1)
            throw new InvalidDataException("historical calibration policy trial journal did not close exactly one funded settlement");
        List<CortexPolicyTrialQuotaDecision> funding = CortexPolicyTrialJournalVerifier.ReadFundingDecisions(
            Path.Combine(calibrationDirectory, "policy_trial_funding.journal.tsv"));
        List<CortexPolicyTrialCompletion> settlements = CortexPolicyTrialJournalVerifier.ReadSettlements(
            Path.Combine(calibrationDirectory, "policy_trial_settlements.journal.tsv"));
        CortexPolicyTrialQuotaDecision funded = funding.Single(static row => row.Decision == CortexPolicyQuotaDecisions.Paid);
        CortexPolicyTrialCompletion settlement = settlements.Single();
        if (!funded.QuotaDecisionID.Equals(settlement.QuotaDecisionID)
            || settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed)
            throw new InvalidDataException("historical calibration policy trial settlement is not a passed funded attempt");
        return (funded.QuotaDecisionID.ToString(), checked((int)settlement.ActualExecutedArmSteps));
    }

    private static CortexForkMaterializationContract ReadMaterializationContract(string path, string childDirectory)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"historical calibration rail {Path.GetFileName(childDirectory)} is missing its materialization contract");
        byte[] bytes = File.ReadAllBytes(path);
        Dictionary<string, string> values = Encoding.UTF8.GetString(bytes)
            .Split('\n')
            .Where(static line => line.Length != 0)
            .Select(line => line.Split('=', 2))
            .Where(static parts => parts.Length == 2)
            .ToDictionary(static parts => parts[0], static parts => parts[1], StringComparer.Ordinal);
        if (!values.TryGetValue("parent", out string? parent)
            || !values.TryGetValue("attempt", out string? attempt)
            || !values.TryGetValue("child", out string? child)
            || !values.TryGetValue("cold", out string? cold))
            throw new InvalidDataException("historical calibration rail materialization contract is incomplete");
        CortexForkMaterializationContract contract = new(parent, attempt, child, cold);
        contract.Validate(childDirectory);
        if (!bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(contract.Encode())))
            throw new InvalidDataException("historical calibration rail materialization contract is not canonical");
        return contract;
    }

    private static T ValidateHistoricalGateReceipt<T>(string directory, string file, string runID,
        string checkpointDigest, string gateConfigDigest) where T : DeepRematchReceipt
    {
        T receipt = Read<T>(Path.Combine(directory, file));
        if (receipt.ReadDigest() != receipt.ComputeDigest()
            || receipt.RunID != runID
            || receipt.CheckpointDigest != checkpointDigest
            || receipt.ConfigDigest != gateConfigDigest
            || receipt.ProvenanceDigest.Length != 64)
            throw new InvalidDataException($"historical gate receipt {file} is not bound to run/checkpoint/config/provenance");
        return receipt;
    }

    private static void ValidateReceiptSet(
        Run parent,
        DeepRematchParentRecord parentDocument,
        DeepRematchColdSeedRecord coldDocument,
        DeepRematchChildCopyRecord calibrationCopy,
        DeepRematchCalibrationRecord calibration,
        DeepRematchChildCopyRecord evaluationCopy,
        DeepRematchEvaluationRecord evaluation)
    {
        parentDocument.Validate(parent.Dir);
        coldDocument.Validate(parentDocument);
        calibrationCopy.Validate(parentDocument, coldDocument, CortexForkRailRoles.Calibration);
        evaluationCopy.Validate(parentDocument, coldDocument, CortexForkRailRoles.Evaluation);
        calibration.Validate(parentDocument, coldDocument, calibrationCopy, ReadCalibrationPhaseWallMilliseconds(parent.Dir));
        evaluation.Validate(parentDocument, coldDocument, evaluationCopy, calibration);
    }

    internal static T Read<T>(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing composite record: {path}");
        byte[] bytes = File.ReadAllBytes(path);
        T document = RonSerializer.Deserialize<T>(bytes);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!bytes.AsSpan().SequenceEqual(second)) throw new InvalidDataException($"composite record drifted: {path}");
        return document;
    }

    internal static string RequireDigest(string value, string label)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{label} is not a SHA-256 digest");
        return value;
    }

    internal static string RequireText(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{label} cannot be blank");
        return value;
    }

    internal static string RequireChildPath(string parentDirectory, string childID, string childPath, string label)
    {
        RequireText(childID, label + " ID");
        RequireText(childPath, label + " path");
        string parent = Path.GetFullPath(parentDirectory);
        string child = Path.GetFullPath(childPath);
        string children = Path.Combine(parent, "children") + Path.DirectorySeparatorChar;
        if (!child.StartsWith(children, StringComparison.Ordinal) || !string.Equals(Path.GetFileName(child), childID, StringComparison.Ordinal))
            throw new InvalidDataException($"{label} path is not the declared child of the parent");
        return child;
    }

    internal static string DigestFile(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing composite artifact: {path}");
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    internal static string DigestCheckpoint(string runDirectory)
    {
        string path = Path.Combine(runDirectory, Checkpoint.FileName);
        if (!File.Exists(path)) throw new InvalidDataException($"missing composite artifact: {path}");
        return Checkpoint.LogicalStateSHA256(CheckpointDelta.ReadEffectiveSnapshot(runDirectory).EffectiveImage);
    }

    internal static bool IsHistoricalCheckpointDialect(string runDirectory)
    {
        byte[] image = CheckpointDelta.ReadEffectiveSnapshot(runDirectory).EffectiveImage;
        return IsHistoricalCheckpointDialect(image);
    }

    internal static bool IsHistoricalCheckpointDialect(ReadOnlySpan<byte> image)
    {
        byte[] historicalMagic = "CORTEXO\n"u8.ToArray();
        byte[] currentMagic = Encoding.UTF8.GetBytes(Checkpoint.CurrentDialect + "\n");
        if (image.StartsWith(historicalMagic)) return true;
        if (image.StartsWith(currentMagic)) return false;
        throw new InvalidDataException($"historical custody requires an explicit CORTEXO image or the current {Checkpoint.CurrentDialect} image");
    }

    internal static bool VerifyCheckpointDialectFixture()
    {
        bool historical = IsHistoricalCheckpointDialect("CORTEXO\nfixture"u8);
        bool current = !IsHistoricalCheckpointDialect(Encoding.UTF8.GetBytes(Checkpoint.CurrentDialect + "\nfixture"));
        bool rejected = false;
        try { _ = IsHistoricalCheckpointDialect("CORTEXE\nfixture"u8); }
        catch (InvalidDataException) { rejected = true; }
        return historical && current && rejected;
    }

    internal static string ResolveChildArtifact(string childDirectory, string relativePath, string label)
    {
        RequireText(relativePath, label);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"{label} escaped its child directory");
        string child = Path.GetFullPath(childDirectory);
        string path = Path.GetFullPath(Path.Combine(child, relativePath));
        if (!path.StartsWith(child + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"{label} escaped its child directory");
        return path;
    }
}

[RonObject]
internal partial class DeepRematchResumeAttemptRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string attemptID = "";
    public string childRunID = "";
    public string status = "";

    internal void Validate(string expectedParentRunID)
    {
        if (schemaVersion != DeepRematchCompositeRON.SchemaVersion || parentRunID != expectedParentRunID
            || string.IsNullOrWhiteSpace(attemptID) || string.IsNullOrWhiteSpace(childRunID) || status != "admitted")
            throw new InvalidDataException("deep-rematch resume admission is not an immutable admitted attempt");
    }
}

[RonObject]
internal partial class DeepRematchCallbackAttemptRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string attemptID = "";
    public string phase = "";
    public string childRunID = "";
    public string terminalReceiptPath = "";
    public string terminalReceiptDigest = "";
    public string priorLandingOutcomePath = "";
    public string priorLandingOutcomeDigest = "";
    public string priorLandingOutcomeState = "";
    public long loadWallMilliseconds;
    public long loadRawTicks;
    public long wallMilliseconds;
    public long rawTicks;
    public string outcome = "";
    public string detail = "";
    public string recordDigest = "";

    internal void Validate(string parentDirectory, DeepRematchAttemptTransition transition)
    {
        if (schemaVersion != DeepRematchCompositeRON.SchemaVersion
            || parentRunID != Run.RunIDFromDirectory(parentDirectory)
            || attemptID != transition.attemptID || phase != transition.phase || childRunID != transition.childRunID
            || terminalReceiptPath != transition.terminalReceiptPath || terminalReceiptDigest != transition.terminalReceiptDigest
            || loadWallMilliseconds < 0 || loadRawTicks <= 0 || wallMilliseconds < 0 || rawTicks <= 0 || string.IsNullOrWhiteSpace(outcome)
            || recordDigest != ComputeDigest())
            throw new InvalidDataException("callback attempt record identity or timing is malformed");
        if (DeepRematchCompositeRON.DigestFile(Path.Combine(parentDirectory, terminalReceiptPath)) != terminalReceiptDigest)
            throw new InvalidDataException("callback attempt terminal receipt changed");
        if (priorLandingOutcomePath.Length != 0)
        {
            if (Path.IsPathRooted(priorLandingOutcomePath) || priorLandingOutcomePath.Contains("..", StringComparison.Ordinal)
                || priorLandingOutcomeDigest.Length != 64)
                throw new InvalidDataException("callback attempt prior landing binding is unsafe");
            string path = Path.GetFullPath(Path.Combine(parentDirectory, priorLandingOutcomePath));
            if (!File.Exists(path) || DeepRematchCompositeRON.DigestFile(path) != priorLandingOutcomeDigest)
                throw new InvalidDataException("callback attempt prior landing outcome changed");
            CortexForkLandingOutcomeStates landingState = DeepRematchCompositeRON.ReadLandingOutcomeState(path);
            if (landingState != CortexForkLandingOutcomeStates.CallbackFailed)
                throw new InvalidDataException("callback attempt prior landing outcome is not the failed callback boundary");
        }
    }

    internal string ComputeDigest()
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            schemaVersion, parentRunID, attemptID, phase, childRunID, terminalReceiptPath, terminalReceiptDigest,
            priorLandingOutcomePath, priorLandingOutcomeDigest, priorLandingOutcomeState, loadWallMilliseconds, loadRawTicks,
            wallMilliseconds, rawTicks, outcome, detail))));
}

internal readonly record struct DeepRematchControlBinding(
    EmlIntensionalRematchControlReceipt Control,
    string ReceiptDigest,
    string FileDigest)
{
    internal static DeepRematchControlBinding Read(string controlPath, string childRunID, string checkpointDigest, string configDigest)
    {
        if (!File.Exists(controlPath)) throw new InvalidDataException("deep-rematch control receipt is missing");
        byte[] bytes = File.ReadAllBytes(controlPath);
        EmlIntensionalRematchControlReceipt control = RonSerializer.Deserialize<EmlIntensionalRematchControlReceipt>(bytes);
        if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in control))
            || control.receiptDigest != control.ComputeDigest())
            throw new InvalidDataException("deep-rematch control receipt changed at its durability boundary");
        DeepRematchGate.ValidateSettlementControl(controlPath, childRunID, checkpointDigest, configDigest);
        return new(control, control.receiptDigest, DeepRematchCompositeRON.DigestFile(controlPath));
    }

}

[RonObject]
internal partial class DeepRematchCallbackSettlementRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string attemptID = "";
    public string phase = "";
    public string childRunID = "";
    public string terminalReceiptPath = "";
    public string terminalReceiptDigest = "";
    public string legacyTerminalPath = "";
    public string legacyTerminalDigest = "";
    public string trainingReceiptDigest = "";
    public string trainingContentDigest = "";
    public string trainingForkAuthorityDigest = "";
    public string callbackAttemptPath = "";
    public string callbackAttemptDigest = "";
    public string legacySeedBindingDigest = "";
    public string derivedCurrentSeedBindingDigest = "";
    public string currentProofBindingDigest = "";
    public string currentProofEffectiveImageSHA256 = "";
    public string currentProofEffectivePhysicalSHA256 = "";
    public string currentProofBasePhysicalSHA256 = "";
    public string currentProofPhysicalChainSHA256 = "";
    public string currentProofPersistedConfigDigest = "";
    public int currentProofNextStep;
    public bool currentProofSaveLoadSaveExact;
    public string priorLandingOutcomePath = "";
    public string priorLandingOutcomeDigest = "";
    public string priorLandingOutcomeState = "";
    public string sourceCursorDigest = "";
    public string controlReceiptDigest = "";
    public string controlReceiptFileDigest = "";
    public string controlAssayStatus = "";
    public string controlShadowPowerStatus = "";
    public string controlNullPowerStatus = "";
    public long loadWallMilliseconds;
    public long loadRawTicks;
    public long callbackWallMilliseconds;
    public long callbackRawTicks;
    public long totalWallMilliseconds;
    public long totalRawTicks;
    public List<DeepRematchCallbackSettlementSegment> segments = [];
    public string detail = "";
    public string recordDigest = "";

    internal DeepRematchTotalAccounting TerminalAccounting
        => DeepRematchTotalAccounting.Create(segments.Select(static segment =>
            new DeepRematchWallSegment(Enum.Parse<DeepRematchWallPhases>(segment.phase), segment.wallMilliseconds, segment.rawTicks)).ToList(),
            totalWallMilliseconds, totalRawTicks);

    internal string ComputeDigest()
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            schemaVersion, parentRunID, attemptID, phase, childRunID, terminalReceiptPath, terminalReceiptDigest,
            legacyTerminalPath, legacyTerminalDigest, trainingReceiptDigest, trainingContentDigest, trainingForkAuthorityDigest,
            callbackAttemptPath, callbackAttemptDigest, legacySeedBindingDigest, derivedCurrentSeedBindingDigest,
            currentProofBindingDigest, currentProofEffectiveImageSHA256, currentProofEffectivePhysicalSHA256,
            currentProofBasePhysicalSHA256, currentProofPhysicalChainSHA256, currentProofPersistedConfigDigest,
            currentProofNextStep, currentProofSaveLoadSaveExact, priorLandingOutcomePath, priorLandingOutcomeDigest, priorLandingOutcomeState,
            sourceCursorDigest, controlReceiptDigest, controlReceiptFileDigest,
            controlAssayStatus, controlShadowPowerStatus, controlNullPowerStatus,
            loadWallMilliseconds, loadRawTicks, callbackWallMilliseconds, callbackRawTicks, totalWallMilliseconds, totalRawTicks,
            string.Join(',', segments.Select(static segment => string.Join(':', segment.phase, segment.wallMilliseconds, segment.rawTicks))), detail))));

    internal void Validate(string parentDirectory)
    {
        if (schemaVersion != 2 || string.IsNullOrWhiteSpace(parentRunID)
            || parentRunID != Run.RunIDFromDirectory(parentDirectory)
            || string.IsNullOrWhiteSpace(attemptID) || phase != nameof(DeepRematchCompositePhases.Calibration)
            || childRunID != attemptID)
            throw new InvalidDataException("callback settlement identity is malformed");
        if (terminalReceiptDigest.Length != 64 || legacyTerminalDigest.Length != 64
            || callbackAttemptDigest.Length != 64 || legacySeedBindingDigest.Length != 64
            || derivedCurrentSeedBindingDigest.Length != 64 || currentProofBindingDigest.Length != 64
            || currentProofEffectiveImageSHA256.Length != 64 || currentProofEffectivePhysicalSHA256.Length != 64
            || currentProofBasePhysicalSHA256.Length != 64 || currentProofPhysicalChainSHA256.Length != 64
            || currentProofPersistedConfigDigest.Length != 64)
            throw new InvalidDataException("callback settlement digest shape is malformed");
        if (!currentProofSaveLoadSaveExact || currentProofNextStep < 0)
            throw new InvalidDataException("callback settlement checkpoint proof is malformed");
        if (loadWallMilliseconds < 0 || loadRawTicks <= 0
            || callbackWallMilliseconds < 0 || callbackRawTicks <= 0
            || totalWallMilliseconds < 0 || totalRawTicks <= 0 || segments.Count == 0)
            throw new InvalidDataException("callback settlement timing is malformed");
        if (recordDigest != ComputeDigest())
            throw new InvalidDataException("callback settlement record digest mismatch");
        string expectedTerminalPath = Path.Combine("children", childRunID, DeepRematchLegacyTerminalAuthority.TerminalRunFileName)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!string.Equals(terminalReceiptPath, expectedTerminalPath, StringComparison.Ordinal)
            || !string.Equals(legacyTerminalPath, expectedTerminalPath, StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement terminal authority paths are not canonical");
        ValidatePath(parentDirectory, terminalReceiptPath, terminalReceiptDigest, "terminal receipt");
        ValidatePath(parentDirectory, legacyTerminalPath, legacyTerminalDigest, "legacy terminal");
        ValidatePath(parentDirectory, callbackAttemptPath, callbackAttemptDigest, "callback attempt");
        if (priorLandingOutcomePath.Length != 0)
        {
            if (priorLandingOutcomeDigest.Length != 64) throw new InvalidDataException("callback settlement landing binding is partial");
            ValidatePath(parentDirectory, priorLandingOutcomePath, priorLandingOutcomeDigest, "prior landing outcome");
        }
        if (trainingReceiptDigest.Length != 64 || trainingContentDigest.Length != 64 || trainingForkAuthorityDigest.Length != 64
            || sourceCursorDigest.Length != 64 || controlReceiptDigest.Length != 64
            || controlReceiptFileDigest.Length != 64
            || string.IsNullOrWhiteSpace(controlAssayStatus) || string.IsNullOrWhiteSpace(controlShadowPowerStatus)
            || string.IsNullOrWhiteSpace(controlNullPowerStatus))
            throw new InvalidDataException("callback settlement training/control bindings are incomplete");
        DeepRematchLegacyTerminalAuthority authority = DeepRematchLegacyTerminalAuthority.Read(
            Path.Combine(parentDirectory, "children", childRunID));
        DeepRematchParentRecord parentRecord = DeepRematchCompositeRON.Read<DeepRematchParentRecord>(Path.Combine(parentDirectory, DeepRematchCompositeRON.ParentRecordFile));
        DeepRematchColdSeedRecord coldRecord = DeepRematchCompositeRON.Read<DeepRematchColdSeedRecord>(Path.Combine(parentDirectory, DeepRematchCompositeRON.ColdSeedRecordFile));
        parentRecord.Validate(parentDirectory);
        coldRecord.Validate(parentRecord);
        if (authority.TerminalRun.parentRunID != parentRecord.runID
            || authority.TerminalRun.coldSeedDigest != coldRecord.coldSeedDigest
            || authority.TerminalRun.persistedConfigDigest != coldRecord.persistedConfigDigest
            || authority.TerminalRun.sourceRunID != parentRecord.runID
            || authority.TerminalRun.sourceNextStep != coldRecord.nextStep)
            throw new InvalidDataException("callback settlement terminal identity is not bound to parent/cold/source");
        if (authority.SeedLoad.BindingDigest != legacySeedBindingDigest)
            throw new InvalidDataException("callback settlement legacy seed binding changed");
        DeepRematchLegacySeedLoad legacySeed = authority.SeedLoad;
        CheckpointRoundTripProof currentProof = new(currentProofEffectiveImageSHA256, currentProofEffectivePhysicalSHA256,
            currentProofBasePhysicalSHA256, currentProofPhysicalChainSHA256, currentProofPersistedConfigDigest,
            currentProofNextStep, currentProofSaveLoadSaveExact);
        DeepRematchLegacySeedBinding relation = new(legacySeedBindingDigest, derivedCurrentSeedBindingDigest, currentProofBindingDigest);
        relation.Validate(in legacySeed, in currentProof);
        if (!string.Equals(currentProof.BindingDigest, currentProofBindingDigest, StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement current seed proof changed");
        string childDirectory = Path.Combine(parentDirectory, "children", childRunID);
        string controlPath = Path.Combine(childDirectory, DeepRematchReceiptEmission.Rung0ControlFile);
        if (!File.Exists(controlPath) || DeepRematchCompositeRON.DigestFile(controlPath) != controlReceiptFileDigest)
            throw new InvalidDataException("callback settlement control receipt changed");
        string gateConfigDigest = DeepRematchGate.ReadCanonicalGateDigest(parentDirectory, childDirectory);
        DeepRematchControlBinding controlBinding = DeepRematchControlBinding.Read(controlPath, childRunID,
            authority.TerminalRun.finalCheckpointSHA256, gateConfigDigest);
        EmlIntensionalRematchControlReceipt control = controlBinding.Control;
        if (controlBinding.ReceiptDigest != controlReceiptDigest
            || controlBinding.FileDigest != controlReceiptFileDigest
            || control.sourceCursorDigest != sourceCursorDigest
            || control.assayStatus != controlAssayStatus
            || control.shadowPowerStatus != controlShadowPowerStatus
            || control.nullPowerStatus != controlNullPowerStatus)
            throw new InvalidDataException("callback settlement control authority changed");
        string trainingPath = Path.Combine(childDirectory, "policy-boundary.training.ron");
        if (!File.Exists(trainingPath)) throw new InvalidDataException("callback settlement training sidecar is missing");
        byte[] trainingBytes = File.ReadAllBytes(trainingPath);
        PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.Decode(trainingBytes, HomeostatPolicyBoundaryDomain.Instance);
        training.Validate(HomeostatPolicyBoundaryDomain.Instance);
        if (training.ReceiptDigest != trainingReceiptDigest || training.ContentDigest != trainingContentDigest
            || training.ForkAuthorityDigest != trainingForkAuthorityDigest)
            throw new InvalidDataException("callback settlement training authority changed");
        if (training.ParentRunID != parentRecord.runID || training.SourceChildID != childRunID
            || training.ColdSeedDigest != coldRecord.coldSeedDigest
            || training.ConfigReceiptDigest != coldRecord.persistedConfigDigest
            || training.CheckpointReceiptDigest != authority.TerminalRun.finalCheckpointSHA256
            || training.TrainingStartStep != authority.TerminalRun.startStep
            || training.TrainingEndStep != authority.TerminalRun.plannedNextStep - 1)
            throw new InvalidDataException("callback settlement training identity is not bound to terminal authority");
        List<DeepRematchWallSegment> named = segments.Select(static segment =>
            new DeepRematchWallSegment(Enum.Parse<DeepRematchWallPhases>(segment.phase), segment.wallMilliseconds, segment.rawTicks)).ToList();
        long wall = named.Sum(static segment => segment.WallMilliseconds);
        long raw = named.Sum(static segment => segment.RawTicks);
        if (wall != totalWallMilliseconds || raw != totalRawTicks || named.Select(static segment => segment.Phase).Distinct().Count() != named.Count
            || named.Any(static segment => segment.WallMilliseconds < 0 || segment.RawTicks <= 0))
            throw new InvalidDataException("callback settlement accounting does not close");
        Run parentRun = Run.Open(parentDirectory);
        DeepRematchAttemptJournalRecord journal = DeepRematchCompositeRON.ReadAttemptJournal(parentRun);
        journal.Validate(parentDirectory);
        DeepRematchAttemptTransition callbackTransition = new()
        {
            attemptID = attemptID, phase = phase, childRunID = childRunID,
            terminalReceiptPath = terminalReceiptPath, terminalReceiptDigest = terminalReceiptDigest,
        };
        (string RelativePath, DeepRematchCallbackAttemptRecord Record, int Sequence)[] callbackAttempts =
            DeepRematchCompositeRON.ReadCallbackAttempts(parentRun, attemptID, callbackTransition);
        DeepRematchAttemptTransition[] callbackBegins = journal.entries
            .Where(entry => entry.attemptID == attemptID && entry.phase == phase && entry.childRunID == childRunID
                && entry.status == nameof(DeepRematchAttemptStatuses.CallbackAttempted))
            .OrderBy(static entry => entry.sequence).ToArray();
        (string AttemptID, DeepRematchAttemptStatuses Status, DeepRematchAttemptTransition Entry) currentCallback = journal.Current()
            .SingleOrDefault(item => item.Entry.attemptID == attemptID && item.Entry.phase == phase && item.Entry.childRunID == childRunID);
        if (currentCallback.Entry is null
            || currentCallback.Status is not (DeepRematchAttemptStatuses.CallbackAttempted
                or DeepRematchAttemptStatuses.TerminalExact or DeepRematchAttemptStatuses.Sealed))
            throw new InvalidDataException("callback settlement is not created from an active or exact callback attempt");
        if (currentCallback.Status is DeepRematchAttemptStatuses.TerminalExact or DeepRematchAttemptStatuses.Sealed
            && (currentCallback.Entry.accountingPath != DeepRematchCompositeRON.CallbackSettlementPath(attemptID)
                || currentCallback.Entry.accountingDigest != DeepRematchCompositeRON.DigestFile(Path.Combine(parentDirectory,
                    currentCallback.Entry.accountingPath))))
            throw new InvalidDataException("callback settlement exact transition is not bound to this settlement");
        foreach ((string RelativePath, DeepRematchCallbackAttemptRecord Record, int Sequence) callbackAttempt in callbackAttempts)
            callbackAttempt.Record.Validate(parentDirectory, callbackTransition);
        DeepRematchCompositeRON.ValidateCallbackAttemptChain(attemptID, phase, childRunID,
            callbackAttemptPath, callbackAttempts, callbackBegins, journal.entries);
        if (callbackAttemptDigest != DeepRematchCompositeRON.DigestFile(Path.Combine(parentDirectory, callbackAttempts[^1].RelativePath)))
            throw new InvalidDataException("callback settlement callback digest changed");
        DeepRematchCallbackAttemptRecord latestCallback = callbackAttempts[^1].Record;
        if (latestCallback.loadWallMilliseconds != loadWallMilliseconds || latestCallback.loadRawTicks != loadRawTicks
            || latestCallback.wallMilliseconds != callbackWallMilliseconds || latestCallback.rawTicks != callbackRawTicks)
            throw new InvalidDataException("callback settlement timing disagrees with callback attempt");
        if (!string.Equals(latestCallback.outcome, "completed", StringComparison.Ordinal))
            throw new InvalidDataException("callback settlement is not bound to a completed callback attempt");
        /*
         * The callback journal is validated above before settlement can become
         * the calibration authority. The current state may still be
         * CallbackAttempted here; TerminalExact/Sealed is appended immediately
         * after this record is committed.
         */
        DeepRematchAttemptTransition[] terminalBindings = journal.entries
            .Where(entry => entry.attemptID == attemptID && entry.phase == phase && entry.childRunID == childRunID
                && entry.terminalReceiptPath.Length != 0).ToArray();
        if (terminalBindings.Length == 0 || terminalBindings.Any(entry =>
                entry.terminalReceiptPath != terminalReceiptPath || entry.terminalReceiptDigest != terminalReceiptDigest))
            throw new InvalidDataException("callback settlement terminal authority disagrees with attempt journal");
        if (priorLandingOutcomePath.Length != 0
            && priorLandingOutcomeState is not (nameof(CortexForkLandingOutcomeStates.CallbackFailed)
                or nameof(CortexForkLandingOutcomeStates.TerminalChildExact)))
            throw new InvalidDataException("callback settlement prior landing state is not retryable");
    }

    private static void ValidatePath(string parentDirectory, string relativePath, string digest, string label)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"callback settlement {label} path escaped parent");
        string path = Path.GetFullPath(Path.Combine(parentDirectory, relativePath));
        if (!path.StartsWith(Path.GetFullPath(parentDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(path) || DeepRematchCompositeRON.DigestFile(path) != digest)
            throw new InvalidDataException($"callback settlement {label} binding changed");
    }
}

[RonObject]
internal partial class DeepRematchCallbackSettlementSegment
{
    public string phase = "";
    public long wallMilliseconds;
    public long rawTicks;
}

internal enum DeepRematchCalibrationAuthorityKinds
{
    Standard,
    CallbackSettlement,
}

internal sealed class DeepRematchCalibrationAuthority
{
    private DeepRematchCalibrationAuthority(
        DeepRematchCalibrationAuthorityKinds kind,
        DeepRematchParentRecord parent,
        DeepRematchColdSeedRecord cold,
        DeepRematchChildCopyRecord? copy,
        DeepRematchCalibrationRecord? calibration,
        DeepRematchCallbackSettlementRecord? settlement,
        DeepRematchLegacyTerminalAuthority? legacy,
        DeepRematchCalibrationReceipt receipt,
        CheckpointRoundTripProof proof)
    {
        Kind = kind; Parent = parent; Cold = cold; Copy = copy; Calibration = calibration;
        Completion = settlement; Legacy = legacy; Receipt = receipt; CurrentProof = proof;
    }

    internal DeepRematchCalibrationAuthorityKinds Kind { get; }
    internal bool IsSettlement => Kind == DeepRematchCalibrationAuthorityKinds.CallbackSettlement;
    internal DeepRematchParentRecord Parent { get; }
    internal DeepRematchColdSeedRecord Cold { get; }
    internal DeepRematchChildCopyRecord? Copy { get; }
    internal DeepRematchCalibrationRecord? Calibration { get; }
    internal DeepRematchCallbackSettlementRecord? Completion { get; }
    internal DeepRematchLegacyTerminalAuthority? Legacy { get; }
    internal DeepRematchCalibrationReceipt Receipt { get; }
    internal CheckpointRoundTripProof CurrentProof { get; }
    internal PolicyBoundaryTrainingReceipt Training => Receipt.Training;
    internal string TrainingReceiptDigest => Training.ReceiptDigest;
    internal string TrainingContentDigest => Training.ContentDigest;
    internal string TrainingForkAuthorityDigest => Training.ForkAuthorityDigest;
    internal string TrainingSourceChildID => Training.SourceChildID;
    internal string ChildRunDirectory => Receipt.ChildRunDirectory;
    internal string ChildRunID => Path.GetFileName(Receipt.ChildRunDirectory);
    internal long CompletedWallMilliseconds => Receipt.WallMilliseconds;

    internal static DeepRematchCalibrationAuthority FromStandard(
        DeepRematchParentRecord parent, DeepRematchColdSeedRecord cold,
        DeepRematchChildCopyRecord copy, DeepRematchCalibrationRecord calibration)
        => new(DeepRematchCalibrationAuthorityKinds.Standard, parent, cold, copy, calibration, null, null,
            new DeepRematchCalibrationReceipt(
                parent.runID, calibration.childRunDirectory,
                new CortexForkStepSpan(calibration.seedStartStep, calibration.plannedNextStep, calibration.actualNextStep),
                copy.ToSeedLoadReceipt(CortexForkRailRoles.Calibration),
                new CortexForkDigests(calibration.finalCheckpointSHA256, calibration.finalTapeSpanlogSHA256, calibration.finalCurveSHA256),
                calibration.training, calibration.trainingPath, calibration.authorityWallMilliseconds,
                calibration.trainingWallMilliseconds, calibration.runtimeBindWallMilliseconds,
                calibration.executionWallMilliseconds, calibration.terminalVerifierWallMilliseconds,
                calibration.wallMilliseconds, calibration.terminalCheckpointExact),
            copy.CheckpointProof);

    internal static DeepRematchCalibrationAuthority FromStandardRecord(
        DeepRematchParentRecord parent, DeepRematchColdSeedRecord cold,
        DeepRematchCalibrationRecord calibration)
    {
        DeepRematchCalibrationReceipt receipt = new(
            parent.runID, calibration.childRunDirectory,
            new CortexForkStepSpan(calibration.seedStartStep, calibration.plannedNextStep, calibration.actualNextStep),
            default,
            new CortexForkDigests(calibration.finalCheckpointSHA256, calibration.finalTapeSpanlogSHA256, calibration.finalCurveSHA256),
            calibration.training, calibration.trainingPath, calibration.authorityWallMilliseconds,
            calibration.trainingWallMilliseconds, calibration.runtimeBindWallMilliseconds,
            calibration.executionWallMilliseconds, calibration.terminalVerifierWallMilliseconds,
            calibration.wallMilliseconds, calibration.terminalCheckpointExact);
        return new(DeepRematchCalibrationAuthorityKinds.Standard, parent, cold, null, calibration, null, null, receipt, default);
    }

    internal static DeepRematchCalibrationAuthority FromSettlement(
        Run parent, DeepRematchCallbackSettlementRecord settlement,
        DeepRematchLegacyTerminalAuthority legacy, DeepRematchCalibrationReceipt receipt,
        CheckpointRoundTripProof proof)
    {
        DeepRematchParentRecord parentRecord = DeepRematchCompositeRON.Read<DeepRematchParentRecord>(parent.PathOf(DeepRematchCompositeRON.ParentRecordFile));
        DeepRematchColdSeedRecord cold = DeepRematchCompositeRON.Read<DeepRematchColdSeedRecord>(parent.PathOf(DeepRematchCompositeRON.ColdSeedRecordFile));
        parentRecord.Validate(parent.Dir); cold.Validate(parentRecord);
        return new(DeepRematchCalibrationAuthorityKinds.CallbackSettlement, parentRecord, cold, null, null,
            settlement, legacy, receipt, proof);
    }

    internal void Validate(long phaseWallMilliseconds = long.MaxValue)
    {
        if (!IsSettlement)
        {
            Copy!.Validate(Parent, Cold, CortexForkRailRoles.Calibration);
            Calibration!.Validate(Parent, Cold, Copy, phaseWallMilliseconds);
            if (Receipt.SeedLoad.CheckpointProof != Copy.CheckpointProof
                || Receipt.SeedLoad.CheckpointProofReused != Copy.checkpointProofReused
                || CurrentProof != Copy.CheckpointProof)
                throw new InvalidDataException("standard calibration authority checkpoint proof drifted from its child copy");
            return;
        }
        Completion!.Validate(Path.GetDirectoryName(Parent.runDirectory) is { } ? Parent.runDirectory : Parent.runDirectory);
        if (Receipt.ParentRunID != Parent.runID || Receipt.SeedLoad.PersistedConfigDigest != Cold.persistedConfigDigest
            || Receipt.Training.ColdSeedDigest != Cold.coldSeedDigest || !Receipt.TerminalCheckpointExact)
            throw new InvalidDataException("callback settlement calibration authority is not bound to parent/cold seed");
    }
}

internal enum DeepRematchAttemptStatuses
{
    Admitted,
    Materialized,
    TerminalChildExact,
    CallbackPendingOrFailed,
    CallbackAttempted,
    CallbackCompleted,
    TerminalExact,
    Sealed,
    InterruptedDark,
}

internal enum DeepRematchAttemptAccountingBasis
{
    RunFork,
    TerminalRecovery,
}

[RonObject]
internal partial class DeepRematchAttemptTransition
{
    public int schemaVersion;
    public string parentRunID = "";
    public long sequence;
    public string attemptID = "";
    public string phase = "";
    public string childRunID = "";
    public string status = "";
    public string accountingPath = "";
    public string accountingDigest = "";
    public string terminalReceiptPath = "";
    public string terminalReceiptDigest = "";
    public string landingOutcomePath = "";
    public string landingOutcomeDigest = "";
    public string landingOutcomeState = "";
    public string detail = "";
    public string previousDigest = "";
    public string entryDigest = "";
}

[RonObject]
internal partial class DeepRematchAttemptJournalRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public List<DeepRematchAttemptTransition> entries = [];

    internal void Validate(string parentDirectory)
    {
        if (schemaVersion != DeepRematchCompositeRON.AttemptJournalSchemaVersion
            || parentRunID != Run.RunIDFromDirectory(parentDirectory))
            throw new InvalidDataException("deep-rematch attempt journal identity is invalid");
        Dictionary<string, (DeepRematchAttemptStatuses Status, string Phase, string ChildRunID)> states = new(StringComparer.Ordinal);
        string previous = "";
        long expected = 0;
        foreach (DeepRematchAttemptTransition entry in entries.OrderBy(static value => value.sequence))
        {
            if (entry.schemaVersion != schemaVersion || entry.parentRunID != parentRunID || entry.sequence != expected++
                || string.IsNullOrWhiteSpace(entry.attemptID) || string.IsNullOrWhiteSpace(entry.phase)
                || string.IsNullOrWhiteSpace(entry.childRunID)
                || !Enum.TryParse(entry.status, out DeepRematchAttemptStatuses next)
                || entry.previousDigest != previous || entry.entryDigest.Length != 64
                || entry.entryDigest != DeepRematchCompositeRON.ComputeAttemptDigest(entry))
                throw new InvalidDataException("deep-rematch attempt journal contains a malformed transition");
            ValidateAttemptChildPath(parentDirectory, entry.phase, entry.attemptID, entry.childRunID,
                next is DeepRematchAttemptStatuses.Materialized or DeepRematchAttemptStatuses.TerminalChildExact
                    or DeepRematchAttemptStatuses.CallbackPendingOrFailed or DeepRematchAttemptStatuses.CallbackAttempted or DeepRematchAttemptStatuses.TerminalExact
                    or DeepRematchAttemptStatuses.CallbackCompleted or DeepRematchAttemptStatuses.Sealed);
            if (states.TryGetValue(entry.attemptID, out (DeepRematchAttemptStatuses Status, string Phase, string ChildRunID) prior))
            {
                if (prior.Phase != entry.phase || prior.ChildRunID != entry.childRunID)
                    throw new InvalidDataException("deep-rematch attempt changed phase or child identity");
                bool allowed = prior.Status switch
                {
                    DeepRematchAttemptStatuses.Admitted => next is DeepRematchAttemptStatuses.Materialized or DeepRematchAttemptStatuses.InterruptedDark,
                    DeepRematchAttemptStatuses.Materialized => next is DeepRematchAttemptStatuses.TerminalChildExact or DeepRematchAttemptStatuses.CallbackCompleted or DeepRematchAttemptStatuses.TerminalExact or DeepRematchAttemptStatuses.InterruptedDark,
                    DeepRematchAttemptStatuses.TerminalChildExact => next is DeepRematchAttemptStatuses.CallbackPendingOrFailed or DeepRematchAttemptStatuses.CallbackAttempted or DeepRematchAttemptStatuses.CallbackCompleted or DeepRematchAttemptStatuses.TerminalExact,
                    DeepRematchAttemptStatuses.CallbackPendingOrFailed => next is DeepRematchAttemptStatuses.CallbackAttempted or DeepRematchAttemptStatuses.TerminalExact,
                    DeepRematchAttemptStatuses.CallbackAttempted => next is DeepRematchAttemptStatuses.CallbackPendingOrFailed or DeepRematchAttemptStatuses.TerminalExact,
                    DeepRematchAttemptStatuses.CallbackCompleted => next == DeepRematchAttemptStatuses.TerminalExact,
                    DeepRematchAttemptStatuses.TerminalExact => next == DeepRematchAttemptStatuses.Sealed,
                    _ => false,
                };
                if (!allowed) throw new InvalidDataException("deep-rematch attempt journal contains an illegal state transition");
            }
            else if (next != DeepRematchAttemptStatuses.Admitted)
                throw new InvalidDataException("deep-rematch attempt journal starts without admission");
            if (next is DeepRematchAttemptStatuses.TerminalExact or DeepRematchAttemptStatuses.Sealed)
            {
                if (string.IsNullOrWhiteSpace(entry.accountingPath) || entry.accountingDigest.Length != 64)
                    throw new InvalidDataException("exact attempt transition omits its immutable accounting binding");
                if (Path.IsPathRooted(entry.accountingPath) || entry.accountingPath.Contains("..", StringComparison.Ordinal))
                    throw new InvalidDataException("attempt accounting path escaped the parent");
                string accountingPath = Path.GetFullPath(Path.Combine(parentDirectory, entry.accountingPath));
                if (!accountingPath.StartsWith(Path.GetFullPath(parentDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || !File.Exists(accountingPath)
                    || DeepRematchCompositeRON.DigestFile(accountingPath) != entry.accountingDigest)
                    throw new InvalidDataException("attempt accounting binding is missing or changed");
                DeepRematchCompositeRON.ValidateAttemptAccountingBinding(parentDirectory, entry);
            }
            if (next is DeepRematchAttemptStatuses.TerminalChildExact or DeepRematchAttemptStatuses.CallbackPendingOrFailed
                or DeepRematchAttemptStatuses.CallbackAttempted or DeepRematchAttemptStatuses.CallbackCompleted)
            {
                ValidateTerminalReceiptBinding(parentDirectory, entry, next);
                if ((entry.landingOutcomePath.Length == 0) != (entry.landingOutcomeDigest.Length == 0))
                    throw new InvalidDataException("callback landing outcome binding is only partially present");
                if (entry.landingOutcomePath.Length == 0 && entry.landingOutcomeState.Length != 0)
                    throw new InvalidDataException("callback landing outcome state has no receipt binding");
                if (entry.landingOutcomePath.Length != 0)
                {
                    if (Path.IsPathRooted(entry.landingOutcomePath) || entry.landingOutcomePath.Contains("..", StringComparison.Ordinal)
                        || entry.landingOutcomeDigest.Length != 64)
                        throw new InvalidDataException("callback landing outcome binding is unsafe");
                    string expectedLanding = Path.GetRelativePath(parentDirectory, Path.Combine(parentDirectory, "children", entry.childRunID, CortexForkLandingOutcomeReceipt.FileName));
                    if (!string.Equals(entry.landingOutcomePath, expectedLanding, StringComparison.Ordinal))
                        throw new InvalidDataException("callback landing outcome binding points at the wrong child");
                    string landingPath = Path.GetFullPath(Path.Combine(parentDirectory, entry.landingOutcomePath));
                    if (!landingPath.StartsWith(Path.GetFullPath(parentDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || !File.Exists(landingPath)
                        || DeepRematchCompositeRON.DigestFile(landingPath) != entry.landingOutcomeDigest)
                        throw new InvalidDataException("callback landing outcome binding is missing or changed");
                    if (!Enum.TryParse(entry.landingOutcomeState, out CortexForkLandingOutcomeStates landingState)
                        || DeepRematchCompositeRON.ReadLandingOutcomeState(landingPath) != landingState)
                        throw new InvalidDataException("callback landing outcome state disagrees with its receipt");
                }
            }
            if (next == DeepRematchAttemptStatuses.CallbackCompleted)
                ValidateCallbackRecordBinding(parentDirectory, entry);
            if (next == DeepRematchAttemptStatuses.InterruptedDark && string.IsNullOrWhiteSpace(entry.detail))
                throw new InvalidDataException("interrupted attempt must carry a dark-wall reason");
            states[entry.attemptID] = (next, entry.phase, entry.childRunID);
            previous = entry.entryDigest;
        }
    }

    private static void ValidateTerminalReceiptBinding(
        string parentDirectory, DeepRematchAttemptTransition entry, DeepRematchAttemptStatuses status)
    {
        if (entry.terminalReceiptDigest.Length != 64 || Path.IsPathRooted(entry.terminalReceiptPath)
            || entry.terminalReceiptPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"{status} transition omits a safe terminal receipt binding");
        string expected = Path.GetRelativePath(parentDirectory, Path.Combine(parentDirectory, "children", entry.childRunID, CortexForkTerminalRunReceipt.FileName));
        if (!string.Equals(entry.terminalReceiptPath, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{status} transition is bound to the wrong terminal receipt path");
        string path = Path.GetFullPath(Path.Combine(parentDirectory, entry.terminalReceiptPath));
        if (!File.Exists(path) || DeepRematchCompositeRON.DigestFile(path) != entry.terminalReceiptDigest)
            throw new InvalidDataException($"{status} transition terminal receipt is missing or changed");
        try
        {
            CortexForkTerminalRunReceipt receipt = CortexForkTerminalRunReceipt.Read(Path.GetDirectoryName(path)!);
            if (!receipt.terminalCheckpointExact || receipt.exitCode != 0 || !receipt.terminalOccurrenceCheckExact)
                throw new InvalidDataException($"{status} transition terminal receipt is not exact");
        }
        catch (InvalidDataException currentError)
        {
            try
            {
                DeepRematchLegacyTerminalAuthority legacy = DeepRematchLegacyTerminalAuthority.Read(Path.GetDirectoryName(path)!);
                if (!legacy.TerminalRun.terminalCheckpointExact || !legacy.TerminalRun.terminalOccurrenceCheckExact
                    || legacy.TerminalRun.exitCode != 0)
                    throw new InvalidDataException($"{status} legacy terminal receipt is not exact");
            }
            catch (Exception legacyError) when (legacyError is IOException or UnauthorizedAccessException or InvalidDataException or RonReadException)
            {
                throw new InvalidDataException($"{status} transition terminal receipt cannot be read: {legacyError.Message}", currentError);
            }
        }
        catch (Exception error)
        {
            throw new InvalidDataException($"{status} transition terminal receipt cannot be read", error);
        }
    }

    private static void ValidateCallbackRecordBinding(string parentDirectory, DeepRematchAttemptTransition entry)
    {
        if (entry.phase != nameof(DeepRematchCompositePhases.Evaluation))
            throw new InvalidDataException("evaluation callback completion is bound to a non-evaluation attempt");
        string landingPath = Path.GetFullPath(Path.Combine(parentDirectory, entry.landingOutcomePath));
        if (entry.landingOutcomeState != CortexForkLandingOutcomeStates.Completed.ToString()
            || !File.Exists(landingPath))
            throw new InvalidDataException("evaluation callback completion has no completed landing authority");
        CortexForkLandingOutcomeReceipt landing = DeepRematchCompositeRON.IsHistoricalCheckpointDialect(
            Path.Combine(parentDirectory, "children", entry.childRunID))
            ? DeepRematchCompositeRON.Read<CortexForkLandingOutcomeReceipt>(landingPath)
            : CortexForkLandingOutcomeReceipt.Read(Path.GetDirectoryName(landingPath)!);
        if (DeepRematchCompositeRON.IsHistoricalCheckpointDialect(Path.Combine(parentDirectory, "children", entry.childRunID)))
            _ = DeepRematchCompositeRON.ReadLandingOutcomeState(landingPath);
        if (landing.state != CortexForkLandingOutcomeStates.Completed || !landing.callbackReturned || !landing.authorityChainExact)
            throw new InvalidDataException("evaluation callback completion landing is not exact");
        string expected = Path.Combine(parentDirectory, "children", entry.childRunID, DeepRematchCompositeRON.EvaluationCallbackRecordFile);
        string path = Path.GetFullPath(expected);
        if (!path.StartsWith(Path.GetFullPath(parentDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(path))
            throw new InvalidDataException("evaluation callback completion record is missing or changed");
        DeepRematchEvaluationCallbackRecord callback = DeepRematchCompositeRON.Read<DeepRematchEvaluationCallbackRecord>(path);
        callback.Validate(parentDirectory);
        if (callback.attemptID != entry.attemptID || callback.childRunID != entry.childRunID
            || callback.parentRunID != entry.parentRunID)
            throw new InvalidDataException("evaluation callback completion record disagrees with its journal binding");
    }

    private static void ValidateAttemptChildPath(string parentDirectory, string phase, string attemptID, string childRunID, bool requireMaterialized)
    {
        if (Path.GetFileName(attemptID) != attemptID || Path.GetFileName(childRunID) != childRunID
            || attemptID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            || childRunID.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidDataException("deep-rematch attempt child identity is not a safe basename");
        string expectedPrefix = phase switch
        {
            nameof(DeepRematchCompositePhases.Calibration) => "calibration_",
            nameof(DeepRematchCompositePhases.Evaluation) => "evaluation_",
            _ => throw new InvalidDataException("deep-rematch attempt phase is not a child rail"),
        };
        if (!childRunID.StartsWith(expectedPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch attempt child role disagrees with its phase");
        string parent = Path.GetFullPath(parentDirectory);
        string child = Path.GetFullPath(Path.Combine(parent, "children", childRunID));
        if (!child.StartsWith(Path.Combine(parent, "children") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch attempt child path escaped the parent");
        if (requireMaterialized && (!Directory.Exists(child) || !File.Exists(Path.Combine(child, "deep-rematch.child.materialized"))))
            throw new InvalidDataException("deep-rematch materialized attempt has no exact child marker");
    }

    internal (string AttemptID, DeepRematchAttemptStatuses Status, DeepRematchAttemptTransition Entry)[] Current()
        => entries.GroupBy(static entry => entry.attemptID, StringComparer.Ordinal)
            .Select(group =>
            {
                DeepRematchAttemptTransition entry = group.OrderBy(static value => value.sequence).Last();
                return (entry.attemptID, Enum.Parse<DeepRematchAttemptStatuses>(entry.status), entry);
            }).ToArray();
}

[RonObject]
internal partial class DeepRematchLegacyAttemptAccountingRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string attemptID = "";
    public string phase = "";
    public string childRunID = "";
    public string status = "";
    public long measuredWallMilliseconds;
    public long totalWallMilliseconds;
    public long unaccountedWallMilliseconds;
    public long measuredRawTicks;
    public long totalRawTicks;
    public long unaccountedRawTicks;
    public long enclosingRunForkWallMilliseconds;
    public long enclosingRunForkRawTicks;
    public string coldSeedDigest = "";
    public string childCopyRecordPath = "";
    public string childCopyRecordDigest = "";
    public string trainingReceiptDigest = "";
    public string trainingContentDigest = "";
    public string trainingForkAuthorityDigest = "";
    public string mountReceiptDigest = "";
    public string cursorDigest = "";
    public string checkpointDigest = "";
    public string terminalRunReceiptPath = "";
    public string terminalRunReceiptSHA256 = "";
    public int terminalActualNextStep;
    public long terminalExecutionWallMilliseconds;
    public long terminalExecutionRawTicks;
    public long terminalVerifierWallMilliseconds;
    public long terminalVerifierRawTicks;
    public long terminalTotalWallMilliseconds;
    public long terminalTotalRawTicks;
    public List<DeepRematchAttemptAccountingSegment> segments = [];

    internal DeepRematchAttemptAccountingRecord ToCurrent()
        => new()
        {
            schemaVersion = schemaVersion,
            basis = DeepRematchAttemptAccountingBasis.RunFork,
            parentRunID = parentRunID,
            attemptID = attemptID,
            phase = phase,
            childRunID = childRunID,
            status = status,
            measuredWallMilliseconds = measuredWallMilliseconds,
            totalWallMilliseconds = totalWallMilliseconds,
            unaccountedWallMilliseconds = unaccountedWallMilliseconds,
            measuredRawTicks = measuredRawTicks,
            totalRawTicks = totalRawTicks,
            unaccountedRawTicks = unaccountedRawTicks,
            enclosingRunForkWallMilliseconds = enclosingRunForkWallMilliseconds,
            enclosingRunForkRawTicks = enclosingRunForkRawTicks,
            coldSeedDigest = coldSeedDigest,
            childCopyRecordPath = childCopyRecordPath,
            childCopyRecordDigest = childCopyRecordDigest,
            trainingReceiptDigest = trainingReceiptDigest,
            trainingContentDigest = trainingContentDigest,
            trainingForkAuthorityDigest = trainingForkAuthorityDigest,
            mountReceiptDigest = mountReceiptDigest,
            cursorDigest = cursorDigest,
            checkpointDigest = checkpointDigest,
            terminalRunReceiptPath = terminalRunReceiptPath,
            terminalRunReceiptSHA256 = terminalRunReceiptSHA256,
            terminalActualNextStep = terminalActualNextStep,
            terminalExecutionWallMilliseconds = terminalExecutionWallMilliseconds,
            terminalExecutionRawTicks = terminalExecutionRawTicks,
            terminalVerifierWallMilliseconds = terminalVerifierWallMilliseconds,
            terminalVerifierRawTicks = terminalVerifierRawTicks,
            terminalTotalWallMilliseconds = terminalTotalWallMilliseconds,
            terminalTotalRawTicks = terminalTotalRawTicks,
            segments = segments,
        };
}

[RonObject]
internal partial class DeepRematchAttemptAccountingRecord
{
    public int schemaVersion;
    public DeepRematchAttemptAccountingBasis basis;
    public string parentRunID = "";
    public string attemptID = "";
    public string phase = "";
    public string childRunID = "";
    public string status = "";
    public long measuredWallMilliseconds;
    public long totalWallMilliseconds;
    public long unaccountedWallMilliseconds;
    public long measuredRawTicks;
    public long totalRawTicks;
    public long unaccountedRawTicks;
    public long enclosingRunForkWallMilliseconds;
    public long enclosingRunForkRawTicks;
    public string coldSeedDigest = "";
    public string childCopyRecordPath = "";
    public string childCopyRecordDigest = "";
    public string trainingReceiptDigest = "";
    public string trainingContentDigest = "";
    public string trainingForkAuthorityDigest = "";
    public string mountReceiptDigest = "";
    public string cursorDigest = "";
    public string checkpointDigest = "";
    public string terminalRunReceiptPath = "";
    public string terminalRunReceiptSHA256 = "";
    public int terminalActualNextStep;
    public long terminalExecutionWallMilliseconds;
    public long terminalExecutionRawTicks;
    public long terminalVerifierWallMilliseconds;
    public long terminalVerifierRawTicks;
    public long terminalTotalWallMilliseconds;
    public long terminalTotalRawTicks;
    public List<DeepRematchAttemptAccountingSegment> segments = [];

    internal void Validate(string expectedParentRunID, string expectedAttemptID, bool requireExact, string? parentDirectory = null)
    {
        if (schemaVersion is not (DeepRematchCompositeRON.LegacyAttemptAccountingSchemaVersion or DeepRematchCompositeRON.AttemptAccountingSchemaVersion)
            || schemaVersion == DeepRematchCompositeRON.LegacyAttemptAccountingSchemaVersion && basis != DeepRematchAttemptAccountingBasis.RunFork
            || schemaVersion == DeepRematchCompositeRON.AttemptAccountingSchemaVersion
                && basis is not (DeepRematchAttemptAccountingBasis.RunFork or DeepRematchAttemptAccountingBasis.TerminalRecovery)
            || parentRunID != expectedParentRunID
            || attemptID != expectedAttemptID || string.IsNullOrWhiteSpace(phase) || string.IsNullOrWhiteSpace(childRunID)
            || status is not (nameof(DeepRematchAttemptStatuses.TerminalExact) or nameof(DeepRematchAttemptStatuses.Sealed))
            || measuredWallMilliseconds < 0 || totalWallMilliseconds < 0 || unaccountedWallMilliseconds < 0
            || measuredRawTicks < 0 || totalRawTicks < 0 || unaccountedRawTicks < 0
            || basis == DeepRematchAttemptAccountingBasis.RunFork
                && (enclosingRunForkWallMilliseconds != measuredWallMilliseconds
                    || enclosingRunForkRawTicks != measuredRawTicks)
            || basis == DeepRematchAttemptAccountingBasis.TerminalRecovery
                && (enclosingRunForkWallMilliseconds != 0 || enclosingRunForkRawTicks != 0)
            || string.IsNullOrWhiteSpace(coldSeedDigest) || string.IsNullOrWhiteSpace(childCopyRecordPath)
            || childCopyRecordDigest.Length != 64 || trainingReceiptDigest.Length != 64
            || trainingContentDigest.Length != 64 || trainingForkAuthorityDigest.Length != 64
            || checkpointDigest.Length != 64 || terminalRunReceiptPath != CortexForkTerminalRunReceipt.FileName
            || terminalRunReceiptSHA256.Length != 64 || terminalExecutionWallMilliseconds < 0
            || terminalExecutionRawTicks <= 0 || terminalVerifierWallMilliseconds < 0 || terminalVerifierRawTicks < 0
            || terminalTotalWallMilliseconds < 0 || terminalTotalRawTicks <= 0 || segments.Count == 0)
            throw new InvalidDataException("attempt accounting identity or typed bindings are incomplete");
        if (segments.Any(static segment => segment.wallMilliseconds < 0 || segment.rawTicks < 0)
            || segments.Select(static segment => segment.phase).Distinct(StringComparer.Ordinal).Count() != segments.Count)
            throw new InvalidDataException("attempt accounting segments are negative or duplicated");
        long wallSum = checked(segments.Sum(static segment => segment.wallMilliseconds));
        long rawSum = checked(segments.Sum(static segment => segment.rawTicks));
        if (totalWallMilliseconds != wallSum || totalWallMilliseconds != measuredWallMilliseconds - unaccountedWallMilliseconds
            || measuredRawTicks != 0 && (totalRawTicks != rawSum || totalRawTicks != measuredRawTicks - unaccountedRawTicks))
            throw new InvalidDataException("attempt accounting sums do not close independently");
        if (requireExact && (unaccountedWallMilliseconds != 0 || unaccountedRawTicks != 0))
            throw new InvalidDataException("exact attempt accounting contains a dark residual");
        if (parentDirectory is not null)
        {
            if (Path.IsPathRooted(childCopyRecordPath) || childCopyRecordPath.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("attempt child-copy path escaped the parent");
            string parent = Path.GetFullPath(parentDirectory);
            string childCopyPath = Path.GetFullPath(Path.Combine(parent, childCopyRecordPath));
            if (!childCopyPath.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !File.Exists(childCopyPath)
                || DeepRematchCompositeRON.DigestFile(childCopyPath) != childCopyRecordDigest)
                throw new InvalidDataException("attempt child-copy binding is missing or changed");
            DeepRematchChildCopyRecord copy = RonSerializer.Deserialize<DeepRematchChildCopyRecord>(File.ReadAllBytes(childCopyPath));
            if (copy.terminalRunReceiptPath != terminalRunReceiptPath
                || copy.terminalRunReceiptSHA256 != terminalRunReceiptSHA256
                || copy.terminalActualNextStep != terminalActualNextStep
                || copy.terminalExecutionWallMilliseconds != terminalExecutionWallMilliseconds
                || copy.terminalExecutionRawTicks != terminalExecutionRawTicks
                || copy.terminalVerifierWallMilliseconds != terminalVerifierWallMilliseconds
                || copy.terminalVerifierRawTicks != terminalVerifierRawTicks
                || copy.terminalTotalWallMilliseconds != terminalTotalWallMilliseconds
                || copy.terminalTotalRawTicks != terminalTotalRawTicks)
                throw new InvalidDataException("attempt accounting terminal authority disagrees with child copy");
        }
    }
}

[RonObject]
internal partial class DeepRematchAttemptAccountingSegment
{
    public string phase = "";
    public long wallMilliseconds;
    public long rawTicks;
}

[RonObject]
internal partial class DeepRematchPreManifestSealIORecord
{
    public int schemaVersion;
    public long wallMilliseconds;
    public long rawTicks;

    internal void Validate()
    {
        if (schemaVersion != DeepRematchCompositeRON.SchemaVersion || wallMilliseconds < 0 || rawTicks <= 0)
            throw new InvalidDataException("pre-manifest seal IO accounting is not a direct positive stopwatch bracket");
    }
}

internal enum DeepRematchCompositePhases
{
    ColdSeed,
    ChildProvisioning,
    Calibration,
    CalibrationTerminal,
    Evaluation,
    EvaluationTerminal,
    Collection,
    Emission,
    Finalization,
}

internal readonly struct DeepRematchOpenPhase
{
    internal DeepRematchOpenPhase(string parentRunID, DeepRematchCompositePhases phase, string attemptID,
        long sequence, long startedAtStopwatchTicks, string entryDigest)
    {
        ParentRunID = parentRunID;
        Phase = phase;
        AttemptID = attemptID;
        Sequence = sequence;
        StartedAtStopwatchTicks = startedAtStopwatchTicks;
        EntryDigest = entryDigest;
    }

    internal string ParentRunID { get; }
    internal DeepRematchCompositePhases Phase { get; }
    internal string AttemptID { get; }
    internal long Sequence { get; }
    internal long StartedAtStopwatchTicks { get; }
    internal string EntryDigest { get; }
}

[RonObject]
internal partial class DeepRematchPhaseRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public long sequence;
    public string phase = "";
    public string attemptID = "";
    public string status = "";
    public long startedAtStopwatchTicks;
    public long finishedAtStopwatchTicks;
    public long wallMilliseconds;
    public string detail = "";
    public string previousDigest = "";
    public string entryDigest = "";
}

[RonObject]
internal partial class DeepRematchPhaseJournalRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public List<DeepRematchPhaseRecord> entries = [];

    internal void Validate(string parentDirectory, bool allowOpen = false)
    {
        if (schemaVersion != DeepRematchCompositeRON.PhaseJournalSchemaVersion
            || parentRunID != Run.RunIDFromDirectory(parentDirectory)
            || entries.Count == 0)
            throw new InvalidDataException("deep-rematch phase journal identity is invalid");
        long expected = 0;
        string previousDigest = "";
        Dictionary<(string Phase, string AttemptID), DeepRematchPhaseRecord> open = [];
        foreach (DeepRematchPhaseRecord entry in entries.OrderBy(static value => value.sequence))
        {
            if (entry.schemaVersion != schemaVersion || entry.parentRunID != parentRunID || entry.sequence != expected++
                || !Enum.TryParse(entry.phase, out DeepRematchCompositePhases phase)
                || string.IsNullOrWhiteSpace(entry.attemptID)
                || entry.status is not ("started" or "completed")
                || entry.wallMilliseconds < 0
                || entry.previousDigest != previousDigest
                || entry.entryDigest.Length != 64
                || entry.entryDigest != DeepRematchCompositeRON.ComputePhaseDigest(entry))
                throw new InvalidDataException("deep-rematch phase journal contains a malformed transition");
            if (entry.phase is nameof(DeepRematchCompositePhases.Calibration)
                or nameof(DeepRematchCompositePhases.CalibrationTerminal)
                or nameof(DeepRematchCompositePhases.Evaluation)
                or nameof(DeepRematchCompositePhases.EvaluationTerminal))
            {
                string childPath = Path.Combine(parentDirectory, "children", entry.attemptID);
                if (!Directory.Exists(childPath))
                    throw new InvalidDataException("deep-rematch phase attempt is not bound to a child directory");
            }
            (string Phase, string AttemptID) key = (entry.phase, entry.attemptID);
            if (entry.status == "started")
            {
                if (open.ContainsKey(key)) throw new InvalidDataException("deep-rematch phase attempt started twice");
                open[key] = entry;
            }
            else if (!open.Remove(key))
                throw new InvalidDataException("deep-rematch phase completion has no matching attempt start");
            previousDigest = entry.entryDigest;
        }
        if (!allowOpen && open.Count != 0)
            throw new InvalidDataException("deep-rematch phase journal ends with an open phase");
    }
}

[RonObject]
internal partial class DeepRematchParentRecord
{
    public int schemaVersion;
    public string runID = "";
    public string runDirectory = "";
    public int exitCode;
    public string coldSeedDigest = "";
    public int coldSeedNextStep;
    public int coldSeedBuildCount;
    public long wallMilliseconds;
    public string gatePath = "";
    public string gateDigest = "";

    internal static DeepRematchParentRecord FromReceipt(in DeepRematchParentReceipt receipt) => new()
    {
        schemaVersion = DeepRematchCompositeRON.ParentRecordSchemaVersion,
        runID = receipt.RunID,
        runDirectory = receipt.RunDirectory,
        exitCode = receipt.ExitCode,
        coldSeedDigest = receipt.ColdSeedDigest,
        coldSeedNextStep = receipt.ColdSeedNextStep,
        coldSeedBuildCount = receipt.ColdSeedBuildCount,
        wallMilliseconds = receipt.WallMilliseconds,
        gatePath = receipt.GatePath,
        gateDigest = receipt.GateDigest,
    };

    internal void Validate(string parentDirectory)
    {
        if (schemaVersion != DeepRematchCompositeRON.ParentRecordSchemaVersion) throw new InvalidDataException("unsupported composite parent record version");
        DeepRematchCompositeRON.RequireText(runID, "parent run ID");
        if (!string.Equals(Path.GetFullPath(runDirectory), Path.GetFullPath(parentDirectory), StringComparison.Ordinal))
            throw new InvalidDataException("parent record directory does not match its run");
        if (!string.Equals(runID, Run.RunIDFromDirectory(runDirectory), StringComparison.Ordinal))
            throw new InvalidDataException("parent run ID is not the run-directory basename");
        DeepRematchCompositeRON.RequireDigest(coldSeedDigest, "parent cold seed");
        if (exitCode != 0 || coldSeedNextStep != 0 || coldSeedBuildCount != 1 || wallMilliseconds < 0)
            throw new InvalidDataException("parent record is not a single cold S0 setup");
        if (!string.IsNullOrWhiteSpace(gatePath)
            && (gateDigest.Length != 64 || gateDigest != DeepRematchCompositeRON.DigestFile(gatePath)))
            throw new InvalidDataException("parent gate authority changed after cold S0 capture");
    }
}

[RonObject]
internal partial class DeepRematchColdSeedRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string coldSeedDigest = "";
    public int nextStep;
    public string checkpointSHA256 = "";
    public string tapeSpanlogSHA256 = "";
    public string curveSHA256 = "";
    public string persistedConfigDigest = "";
    public string checkpointImagePath = DeepRematchCompositeRON.ColdCheckpointImageFile;
    public string tapeImagePath = DeepRematchCompositeRON.ColdTapeImageFile;
    public string curveImagePath = DeepRematchCompositeRON.ColdCurveImageFile;
    public string checkpointImageSHA256 = "";
    public string tapeImageSHA256 = "";
    public string curveImageSHA256 = "";
    public long buildWallMilliseconds;

    internal static DeepRematchColdSeedRecord FromReceipt(in DeepRematchColdSeedReceipt receipt) => new()
    {
        schemaVersion = DeepRematchCompositeRON.ColdSeedRecordSchemaVersion,
        parentRunID = receipt.ParentRunID,
        coldSeedDigest = receipt.ColdSeedDigest,
        nextStep = receipt.NextStep,
        checkpointSHA256 = receipt.Digests.CheckpointSHA256,
        tapeSpanlogSHA256 = receipt.Digests.TapeSpanlogSHA256,
        curveSHA256 = receipt.Digests.CurveSHA256,
        persistedConfigDigest = receipt.PersistedConfigDigest,
        checkpointImageSHA256 = receipt.Digests.CheckpointSHA256,
        tapeImageSHA256 = receipt.Digests.TapeSpanlogSHA256,
        curveImageSHA256 = receipt.Digests.CurveSHA256,
        buildWallMilliseconds = receipt.BuildWallMilliseconds,
    };

    internal void Validate(DeepRematchParentRecord parent)
    {
        if (schemaVersion != DeepRematchCompositeRON.ColdSeedRecordSchemaVersion || parentRunID != parent.runID)
            throw new InvalidDataException("cold seed is not bound to the parent record");
        DeepRematchCompositeRON.RequireDigest(coldSeedDigest, "cold seed");
        DeepRematchCompositeRON.RequireDigest(checkpointSHA256, "cold checkpoint");
        DeepRematchCompositeRON.RequireDigest(tapeSpanlogSHA256, "cold tape spanlog");
        DeepRematchCompositeRON.RequireDigest(curveSHA256, "cold curve");
        DeepRematchCompositeRON.RequireDigest(persistedConfigDigest, "persisted config");
        DeepRematchCompositeRON.RequireDigest(checkpointImageSHA256, "cold checkpoint image");
        DeepRematchCompositeRON.RequireDigest(tapeImageSHA256, "cold tape image");
        DeepRematchCompositeRON.RequireDigest(curveImageSHA256, "cold curve image");
        if (nextStep != 0 || buildWallMilliseconds < 0 || coldSeedDigest != parent.coldSeedDigest)
            throw new InvalidDataException("cold seed record is not the parent's step-zero image");
        if (checkpointImagePath != DeepRematchCompositeRON.ColdCheckpointImageFile
            || tapeImagePath != DeepRematchCompositeRON.ColdTapeImageFile
            || curveImagePath != DeepRematchCompositeRON.ColdCurveImageFile)
            throw new InvalidDataException("cold seed image paths are not canonical");
        string parentDirectory = parent.runDirectory;
        if (checkpointImageSHA256 != DeepRematchCompositeRON.DigestCheckpointImage(parentDirectory, checkpointImagePath)
            || tapeImageSHA256 != DeepRematchCompositeRON.DigestFile(Path.Combine(parentDirectory, tapeImagePath))
            || curveImageSHA256 != DeepRematchCompositeRON.DigestFile(Path.Combine(parentDirectory, curveImagePath)))
            throw new InvalidDataException("cold seed image bytes disagree with the typed S0 digest");
        string recomputedColdSeedDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('|', checkpointSHA256, tapeSpanlogSHA256, curveSHA256, persistedConfigDigest))));
        if (coldSeedDigest != recomputedColdSeedDigest)
            throw new InvalidDataException("cold seed digest does not recompute from its persisted S0 image and config digest");
        // The parent is a setup-only owner: its cold seed image is persisted
        // beside this record and copied into each child. Child-copy records
        // bind these bytes back to the typed S0 record before execution.
    }
}

[RonObject]
internal partial class DeepRematchChildCopyRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string childRunID = "";
    public string childRunDirectory = "";
    public string role = "";
    public string coldSeedDigest = "";
    public string ancestorSeedDigest = "";
    public string preparedSeedDigest = "";
    public CortexForkPreparationRoles preparationRole;
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
    public string checkpointProofEffectiveImageSHA256 = "";
    public string checkpointProofEffectivePhysicalSHA256 = "";
    public string checkpointProofBasePhysicalSHA256 = "";
    public string checkpointProofPhysicalChainSHA256 = "";
    public string checkpointProofConfigDigest = "";
    public int checkpointProofNextStep;
    public bool checkpointProofSaveLoadSaveExact;
    public bool checkpointProofReused;
    public string actualCheckpointSHA256 = "";
    public string actualTapeSpanlogSHA256 = "";
    public string actualCurveSHA256 = "";
    public int startStep;
    public int endStep;
    public long seedIOWallMilliseconds;
    public long seedIORawTicks;
    public bool exact;
    public string terminalRunReceiptPath = "";
    public string terminalRunReceiptSHA256 = "";
    public int terminalStartStep;
    public int terminalPlannedNextStep;
    public int terminalActualNextStep;
    public bool terminalCheckpointExact;
    public bool terminalOccurrenceCheckAttempted;
    public bool terminalOccurrenceCheckExact;
    public long terminalSeedIOWallMilliseconds;
    public long terminalSeedIORawTicks;
    public long terminalRuntimeBindWallMilliseconds;
    public long terminalRuntimeBindRawTicks;
    public long terminalExecutionWallMilliseconds;
    public long terminalExecutionRawTicks;
    public long terminalVerifierWallMilliseconds;
    public long terminalVerifierRawTicks;
    public long terminalTotalWallMilliseconds;
    public long terminalTotalRawTicks;

    internal CheckpointRoundTripProof CheckpointProof
        => new(checkpointProofEffectiveImageSHA256, checkpointProofEffectivePhysicalSHA256,
            checkpointProofBasePhysicalSHA256, checkpointProofPhysicalChainSHA256,
            checkpointProofConfigDigest, checkpointProofNextStep, checkpointProofSaveLoadSaveExact);

    internal CortexForkSeedLoadReceipt ToSeedLoadReceipt(CortexForkRailRoles expectedRole)
        => new(
            new CortexForkDigests(expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256),
            new CortexForkDigests(loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256),
            seedIOWallMilliseconds, parentRunID, childRunID, expectedRole, coldSeedDigest,
            persistedConfigDigest, new CortexExecutionWindow(startStep, endStep), sourceSeedDigest,
            sourceRunID, sourceNextStep, seedIORawTicks, CheckpointProof, checkpointProofReused,
            0, null, ancestorSeedDigest, preparedSeedDigest, preparationRole);

    internal static DeepRematchChildCopyRecord FromReceipt(in DeepRematchChildCopyReceipt receipt, string childDirectory) => new()
    {
        schemaVersion = DeepRematchCompositeRON.ChildCopyRecordSchemaVersion,
        parentRunID = receipt.ParentRunID,
        childRunID = receipt.ChildRunID,
        childRunDirectory = childDirectory,
        role = receipt.Role.ToString(),
        coldSeedDigest = receipt.ColdSeedDigest,
        ancestorSeedDigest = receipt.SeedLoad.AncestorSeedDigest,
        preparedSeedDigest = receipt.SeedLoad.PreparedSeedDigest,
        preparationRole = receipt.SeedLoad.PreparationRole,
        persistedConfigDigest = receipt.SeedLoad.PersistedConfigDigest,
        sourceSeedDigest = receipt.SeedLoad.SourceSeedDigest,
        sourceRunID = receipt.SeedLoad.SourceRunID,
        sourceNextStep = receipt.SeedLoad.SourceNextStep,
        expectedCheckpointSHA256 = receipt.SeedLoad.ExpectedCheckpointSHA256,
        expectedTapeSpanlogSHA256 = receipt.SeedLoad.ExpectedTapeSpanlogSHA256,
        expectedCurveSHA256 = receipt.SeedLoad.ExpectedCurveSHA256,
        loadedCheckpointSHA256 = receipt.SeedLoad.LoadedCheckpointSHA256,
        loadedTapeSpanlogSHA256 = receipt.SeedLoad.LoadedTapeSpanlogSHA256,
        loadedCurveSHA256 = receipt.SeedLoad.LoadedCurveSHA256,
        checkpointProofEffectiveImageSHA256 = receipt.SeedLoad.CheckpointProof.EffectiveImageSHA256,
        checkpointProofEffectivePhysicalSHA256 = receipt.SeedLoad.CheckpointProof.EffectivePhysicalSHA256,
        checkpointProofBasePhysicalSHA256 = receipt.SeedLoad.CheckpointProof.BasePhysicalSHA256,
        checkpointProofPhysicalChainSHA256 = receipt.SeedLoad.CheckpointProof.PhysicalChainSHA256,
        checkpointProofConfigDigest = receipt.SeedLoad.CheckpointProof.PersistedConfigDigest,
        checkpointProofNextStep = receipt.SeedLoad.CheckpointProof.NextStep,
        checkpointProofSaveLoadSaveExact = receipt.SeedLoad.CheckpointProof.SaveLoadSaveExact,
        checkpointProofReused = receipt.SeedLoad.CheckpointProofReused,
        actualCheckpointSHA256 = DeepRematchCompositeRON.DigestCheckpoint(childDirectory),
        actualTapeSpanlogSHA256 = DeepRematchCompositeRON.DigestFile(Path.Combine(childDirectory, "tape.spanlog")),
        actualCurveSHA256 = DeepRematchCompositeRON.DigestFile(Path.Combine(childDirectory, "curve.tsv")),
        startStep = receipt.SeedLoad.ExecutionWindow.StartStep,
        endStep = receipt.SeedLoad.ExecutionWindow.EndStep,
        seedIOWallMilliseconds = receipt.SeedIOWallMilliseconds,
        seedIORawTicks = receipt.SeedLoad.SeedIORawTicks,
        exact = receipt.Exact,
        terminalRunReceiptPath = CortexForkTerminalRunReceipt.FileName,
        terminalRunReceiptSHA256 = DeepRematchCompositeRON.DigestFile(Path.Combine(childDirectory, CortexForkTerminalRunReceipt.FileName)),
        terminalStartStep = receipt.TerminalReceipt.startStep,
        terminalPlannedNextStep = receipt.TerminalReceipt.plannedNextStep,
        terminalActualNextStep = receipt.TerminalReceipt.actualNextStep,
        terminalCheckpointExact = receipt.TerminalReceipt.terminalCheckpointExact,
        terminalOccurrenceCheckAttempted = receipt.TerminalReceipt.terminalOccurrenceCheckAttempted,
        terminalOccurrenceCheckExact = receipt.TerminalReceipt.terminalOccurrenceCheckExact,
        terminalSeedIOWallMilliseconds = receipt.TerminalReceipt.seedIOWallMilliseconds,
        terminalSeedIORawTicks = receipt.TerminalReceipt.seedIORawTicks,
        terminalRuntimeBindWallMilliseconds = receipt.TerminalReceipt.runtimeBindWallMilliseconds,
        terminalRuntimeBindRawTicks = receipt.TerminalReceipt.runtimeBindRawTicks,
        terminalExecutionWallMilliseconds = receipt.TerminalReceipt.executionWallMilliseconds,
        terminalExecutionRawTicks = receipt.TerminalReceipt.executionRawTicks,
        terminalVerifierWallMilliseconds = receipt.TerminalReceipt.terminalVerifierWallMilliseconds,
        terminalVerifierRawTicks = receipt.TerminalReceipt.terminalVerifierRawTicks,
        terminalTotalWallMilliseconds = receipt.TerminalReceipt.totalWallMilliseconds,
        terminalTotalRawTicks = receipt.TerminalReceipt.totalRawTicks,
    };

    internal void Validate(DeepRematchParentRecord parent, DeepRematchColdSeedRecord cold, CortexForkRailRoles expectedRole)
    {
        if ((schemaVersion != DeepRematchCompositeRON.ChildCopyRecordSchemaVersion
                && schemaVersion != DeepRematchCompositeRON.LegacyChildCopyRecordSchemaVersion)
            || parentRunID != parent.runID || role != expectedRole.ToString())
            throw new InvalidDataException("child-copy role or parent binding is corrupt");
        if (schemaVersion == DeepRematchCompositeRON.LegacyChildCopyRecordSchemaVersion)
        {
            ancestorSeedDigest = coldSeedDigest;
            preparedSeedDigest = coldSeedDigest;
            preparationRole = CortexForkPreparationRoles.Unknown;
        }
        DeepRematchCompositeRON.RequireChildPath(parent.runDirectory, childRunID, childRunDirectory, role);
        DeepRematchCompositeRON.RequireDigest(coldSeedDigest, "child cold seed");
        DeepRematchCompositeRON.RequireDigest(persistedConfigDigest, "child persisted config");
        DeepRematchCompositeRON.RequireDigest(sourceSeedDigest, "child source seed");
        if (preparationRole != CortexForkPreparationRoles.Unknown)
        {
            DeepRematchCompositeRON.RequireDigest(ancestorSeedDigest, "child ancestor seed");
            DeepRematchCompositeRON.RequireDigest(preparedSeedDigest, "child prepared seed");
        }
        if (coldSeedDigest != cold.coldSeedDigest || sourceSeedDigest != cold.coldSeedDigest || sourceRunID != parent.runID
            || sourceNextStep != cold.nextStep || persistedConfigDigest != cold.persistedConfigDigest
            || startStep != 0 || endStep <= startStep || seedIOWallMilliseconds < 0 || seedIORawTicks <= 0 || !exact)
            throw new InvalidDataException("child-copy does not load the parent's exact cold S0");
        string[] digests =
        [
            expectedCheckpointSHA256, expectedTapeSpanlogSHA256, expectedCurveSHA256,
            loadedCheckpointSHA256, loadedTapeSpanlogSHA256, loadedCurveSHA256,
        ];
        foreach (string digest in digests) DeepRematchCompositeRON.RequireDigest(digest, "child seed digest");
        foreach (string digest in new[]
        {
            checkpointProofEffectiveImageSHA256, checkpointProofEffectivePhysicalSHA256,
            checkpointProofBasePhysicalSHA256, checkpointProofPhysicalChainSHA256, checkpointProofConfigDigest,
        })
            DeepRematchCompositeRON.RequireDigest(digest, "child checkpoint proof digest");
        if (!CheckpointProof.IsBound || CheckpointProof.NextStep != sourceNextStep
            || CheckpointProof.PersistedConfigDigest != persistedConfigDigest)
            throw new InvalidDataException("child-copy checkpoint proof is incomplete or detached");
        if (expectedCheckpointSHA256 != cold.checkpointSHA256 || expectedTapeSpanlogSHA256 != cold.tapeSpanlogSHA256
            || expectedCurveSHA256 != cold.curveSHA256
            || loadedCheckpointSHA256 != cold.checkpointSHA256 || loadedTapeSpanlogSHA256 != cold.tapeSpanlogSHA256
            || loadedCurveSHA256 != cold.curveSHA256
            || expectedCheckpointSHA256 != loadedCheckpointSHA256 || expectedTapeSpanlogSHA256 != loadedTapeSpanlogSHA256 || expectedCurveSHA256 != loadedCurveSHA256)
            throw new InvalidDataException("child-copy seed load changed the cold image");

        string seedLoadPath = Path.Combine(childRunDirectory, "seed-load-receipt.ron");
        if (!File.Exists(seedLoadPath)) throw new InvalidDataException("child-copy is missing its seed-load receipt");
        CortexForkSeedLoadRailDocument seedLoadDocument = CortexForkTerminalRunReceipt.ReadSeedRailDocument(seedLoadPath);
        CortexForkSeedLoadRailReceipt seedLoad = seedLoadDocument.Rail;
        CortexForkSeedLoadReceipt expectedSeedLoad = ToSeedLoadReceipt(expectedRole);
        if (seedLoadDocument.IsLegacy)
            expectedSeedLoad = expectedSeedLoad with
            {
                AncestorSeedDigest = "",
                PreparedSeedDigest = "",
                PreparationRole = CortexForkPreparationRoles.Unknown,
            };
        if (seedLoad.parentRunID != parentRunID || seedLoad.childRunID != childRunID || seedLoad.role.ToString() != role
            || seedLoad.coldSeedDigest != coldSeedDigest || seedLoad.persistedConfigDigest != persistedConfigDigest
            || seedLoad.sourceSeedDigest != sourceSeedDigest || seedLoad.sourceRunID != sourceRunID || seedLoad.sourceNextStep != sourceNextStep
            || seedLoad.startStep != startStep || seedLoad.endStep != endStep
            || seedLoad.seedIORawTicks != seedIORawTicks
            || seedLoad.expectedCheckpointSHA256 != expectedCheckpointSHA256 || seedLoad.expectedTapeSpanlogSHA256 != expectedTapeSpanlogSHA256
            || seedLoad.expectedCurveSHA256 != expectedCurveSHA256 || seedLoad.loadedCheckpointSHA256 != loadedCheckpointSHA256
            || seedLoad.loadedTapeSpanlogSHA256 != loadedTapeSpanlogSHA256 || seedLoad.loadedCurveSHA256 != loadedCurveSHA256
            || seedLoadDocument.StoredBindingDigest != expectedSeedLoad.BindingDigest
            || seedLoad.ancestorSeedDigest != ancestorSeedDigest
            || seedLoad.preparedSeedDigest != preparedSeedDigest
            || seedLoad.preparationRole != preparationRole)
            throw new InvalidDataException("child-copy seed-load sidecar disagrees with its typed record");
        string terminalPath = Path.Combine(childRunDirectory, "terminal-verification.ron");
        if (!File.Exists(terminalPath))
            throw new InvalidDataException("child-copy is missing its terminal verification receipt");
        CortexForkTerminalOccurrenceCheckDocument terminalDocument = CortexForkTerminalRunReceipt.ReadTerminalOccurrenceCheckDocument(
            terminalPath, DeepRematchCompositeRON.PreparationRoleForRail(expectedRole));
        CortexForkTerminalOccurrenceCheckReceipt terminal = terminalDocument.Receipt;
        terminal.Validate(childRunID, coldSeedDigest);
        string terminalReceiptPath = Path.Combine(childRunDirectory, terminalRunReceiptPath);
        if (!string.Equals(terminalRunReceiptPath, CortexForkTerminalRunReceipt.FileName, StringComparison.Ordinal)
            || !File.Exists(terminalReceiptPath) || terminalRunReceiptSHA256 != DeepRematchCompositeRON.DigestFile(terminalReceiptPath))
            throw new InvalidDataException("child-copy terminal run receipt path or digest is missing or changed");
        CortexForkTerminalRunReceipt durable = CortexForkTerminalRunReceipt.Read(childRunDirectory);
        if (durable.parentRunID != parentRunID || durable.childRunID != childRunID || durable.role.ToString() != role
            || durable.coldSeedDigest != coldSeedDigest || durable.persistedConfigDigest != persistedConfigDigest
            || durable.seedLoadBindingDigest != seedLoadDocument.StoredBindingDigest
            || terminalDocument.StoredSeedLoadBindingDigest != seedLoadDocument.StoredBindingDigest
            || durable.startStep != startStep
            || durable.plannedNextStep != endStep || durable.startStep != terminalStartStep
            || durable.plannedNextStep != terminalPlannedNextStep || durable.actualNextStep != terminalActualNextStep
            || durable.terminalCheckpointExact != terminalCheckpointExact
            || durable.terminalOccurrenceCheckAttempted != terminalOccurrenceCheckAttempted
            || durable.terminalOccurrenceCheckExact != terminalOccurrenceCheckExact
            || durable.seedIOWallMilliseconds != terminalSeedIOWallMilliseconds || durable.seedIORawTicks != terminalSeedIORawTicks
            || seedIORawTicks != durable.seedIORawTicks
            || durable.runtimeBindWallMilliseconds != terminalRuntimeBindWallMilliseconds || durable.runtimeBindRawTicks != terminalRuntimeBindRawTicks
            || durable.executionWallMilliseconds != terminalExecutionWallMilliseconds || durable.executionRawTicks != terminalExecutionRawTicks
            || durable.terminalVerifierWallMilliseconds != terminalVerifierWallMilliseconds || durable.terminalVerifierRawTicks != terminalVerifierRawTicks
            || durable.totalWallMilliseconds != terminalTotalWallMilliseconds || durable.totalRawTicks != terminalTotalRawTicks
            || durable.finalCheckpointSHA256 != actualCheckpointSHA256 || durable.finalTapeSpanlogSHA256 != actualTapeSpanlogSHA256
            || durable.finalCurveSHA256 != actualCurveSHA256 || !terminalCheckpointExact || !terminalOccurrenceCheckAttempted || !terminalOccurrenceCheckExact)
            throw new InvalidDataException("child-copy terminal run receipt disagrees with its typed authority");
        if (actualCheckpointSHA256 != DeepRematchCompositeRON.DigestCheckpoint(childRunDirectory)
            || actualTapeSpanlogSHA256 != DeepRematchCompositeRON.DigestFile(Path.Combine(childRunDirectory, "tape.spanlog"))
            || actualCurveSHA256 != DeepRematchCompositeRON.DigestFile(Path.Combine(childRunDirectory, "curve.tsv")))
            throw new InvalidDataException("child-copy actual artifact bytes disagree with its typed record");
        if (terminal.finalCheckpointSHA256 != actualCheckpointSHA256
            || terminal.finalTapeSpanlogSHA256 != actualTapeSpanlogSHA256
            || terminal.finalCurveSHA256 != actualCurveSHA256)
            throw new InvalidDataException("child-copy terminal verifier digest disagrees with landed artifacts");
    }
}

[RonObject]
internal partial class DeepRematchCalibrationRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string childRunDirectory = "";
    public string trainingPath = "";
    public string trainingSidecarSHA256 = "";
    public string forkAuthorityDigest = "";
    public string trainingContentDigest = "";
    public string trainingReceiptDigest = "";
    public PolicyBoundaryTrainingReceipt training = new();
    public string persistedConfigDigest = "";
    public int seedStartStep;
    public int plannedNextStep;
    public int actualNextStep;
    public long authorityWallMilliseconds;
    public long trainingWallMilliseconds;
    public long runtimeBindWallMilliseconds;
    public long executionWallMilliseconds;
    public long terminalVerifierWallMilliseconds;
    public long wallMilliseconds;
    public bool terminalCheckpointExact;
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public string terminalRunReceiptPath = "";
    public string terminalRunReceiptSHA256 = "";
    public int terminalActualNextStep;
    public long terminalExecutionWallMilliseconds;
    public long terminalExecutionRawTicks;
    public long terminalReceiptVerifierWallMilliseconds;
    public long terminalReceiptVerifierRawTicks;
    public long terminalTotalWallMilliseconds;
    public long terminalTotalRawTicks;

    internal static DeepRematchCalibrationRecord FromReceipt(in DeepRematchCalibrationReceipt receipt) => new()
    {
        schemaVersion = DeepRematchCompositeRON.SchemaVersion,
        parentRunID = receipt.ParentRunID,
        childRunDirectory = receipt.ChildRunDirectory,
        trainingPath = NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.TrainingPath),
        trainingSidecarSHA256 = DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(receipt.ChildRunDirectory, NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.TrainingPath), "training sidecar")),
        forkAuthorityDigest = receipt.Training.ForkAuthorityDigest,
        trainingContentDigest = receipt.Training.ContentDigest,
        trainingReceiptDigest = receipt.Training.ReceiptDigest,
        training = receipt.Training,
        persistedConfigDigest = receipt.SeedLoad.PersistedConfigDigest,
        seedStartStep = receipt.StepSpan.SeedNextStep,
        plannedNextStep = receipt.StepSpan.PlannedNextStep,
        actualNextStep = receipt.StepSpan.ActualNextStep,
        authorityWallMilliseconds = receipt.AuthorityWallMilliseconds,
        trainingWallMilliseconds = receipt.TrainingWallMilliseconds,
        runtimeBindWallMilliseconds = receipt.RuntimeBindWallMilliseconds,
        executionWallMilliseconds = receipt.ExecutionWallMilliseconds,
        terminalVerifierWallMilliseconds = receipt.TerminalVerifierWallMilliseconds,
        wallMilliseconds = receipt.WallMilliseconds,
        terminalCheckpointExact = receipt.TerminalCheckpointExact,
        finalCheckpointSHA256 = receipt.FinalDigests.CheckpointSHA256,
        finalTapeSpanlogSHA256 = receipt.FinalDigests.TapeSpanlogSHA256,
        finalCurveSHA256 = receipt.FinalDigests.CurveSHA256,
        terminalRunReceiptPath = CortexForkTerminalRunReceipt.FileName,
        terminalRunReceiptSHA256 = DeepRematchCompositeRON.DigestFile(Path.Combine(receipt.ChildRunDirectory, CortexForkTerminalRunReceipt.FileName)),
        terminalActualNextStep = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).actualNextStep,
        terminalExecutionWallMilliseconds = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).executionWallMilliseconds,
        terminalExecutionRawTicks = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).executionRawTicks,
        terminalReceiptVerifierWallMilliseconds = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).terminalVerifierWallMilliseconds,
        terminalReceiptVerifierRawTicks = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).terminalVerifierRawTicks,
        terminalTotalWallMilliseconds = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).totalWallMilliseconds,
        terminalTotalRawTicks = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).totalRawTicks,
    };

    internal void Validate(DeepRematchParentRecord parent, DeepRematchColdSeedRecord cold, DeepRematchChildCopyRecord copy,
        long enclosingCalibrationPhaseWallMilliseconds = long.MaxValue)
    {
        if (schemaVersion != DeepRematchCompositeRON.SchemaVersion || parentRunID != parent.runID
            || Path.GetFullPath(childRunDirectory) != Path.GetFullPath(copy.childRunDirectory))
            throw new InvalidDataException("calibration record is not bound to its child copy");
        DeepRematchCompositeRON.RequireDigest(forkAuthorityDigest, "calibration fork authority");
        DeepRematchCompositeRON.RequireDigest(trainingContentDigest, "calibration training content");
        DeepRematchCompositeRON.RequireDigest(trainingReceiptDigest, "calibration training receipt");
        DeepRematchCompositeRON.RequireDigest(trainingSidecarSHA256, "calibration training sidecar");
        training.Validate(HomeostatPolicyBoundaryDomain.Instance);
        if (string.IsNullOrWhiteSpace(trainingPath)
            || !string.Equals(trainingPath, "policy-boundary.training.ron", StringComparison.Ordinal))
            throw new InvalidDataException("calibration training path is not canonical");
        string trainingFile = DeepRematchCompositeRON.ResolveChildArtifact(childRunDirectory, trainingPath, "training sidecar");
        byte[] trainingBytes = File.ReadAllBytes(trainingFile);
        PolicyBoundaryTrainingReceipt decodedTraining = PolicyBoundaryTrainingReceipt.Decode(trainingBytes, HomeostatPolicyBoundaryDomain.Instance);
        if (training.ParentRunID != parent.runID || training.SourceChildID != copy.childRunID
            || training.ColdSeedDigest != cold.coldSeedDigest
            || training.ConfigReceiptDigest != persistedConfigDigest
            || training.CheckpointReceiptDigest != finalCheckpointSHA256
            || training.TrainingStartStep != seedStartStep
            || training.TrainingEndStep != plannedNextStep - 1
            || trainingReceiptDigest != training.ReceiptDigest || trainingContentDigest != training.ContentDigest
            || forkAuthorityDigest != training.ForkAuthorityDigest
            || trainingSidecarSHA256 != DeepRematchCompositeRON.DigestFile(trainingFile)
            || trainingSidecarSHA256 != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(trainingBytes))
            || !TrainingIdentityMatches(decodedTraining, training)
            || persistedConfigDigest != cold.persistedConfigDigest
            || seedStartStep != copy.startStep || plannedNextStep != copy.endStep || actualNextStep != plannedNextStep
            || authorityWallMilliseconds < 0 || trainingWallMilliseconds < 0 || runtimeBindWallMilliseconds < 0
            || executionWallMilliseconds < 0
            || terminalVerifierWallMilliseconds < 0 || wallMilliseconds < 0 || enclosingCalibrationPhaseWallMilliseconds < 0
            || authorityWallMilliseconds > enclosingCalibrationPhaseWallMilliseconds
            || trainingWallMilliseconds > enclosingCalibrationPhaseWallMilliseconds
            || !terminalCheckpointExact
            || terminalRunReceiptPath != CortexForkTerminalRunReceipt.FileName
            || terminalRunReceiptSHA256.Length != 64
            || terminalActualNextStep != actualNextStep
            || terminalExecutionWallMilliseconds != executionWallMilliseconds
            || terminalExecutionRawTicks <= 0 || terminalReceiptVerifierWallMilliseconds != copy.terminalVerifierWallMilliseconds
            || terminalReceiptVerifierRawTicks != copy.terminalVerifierRawTicks
            || terminalTotalWallMilliseconds != copy.terminalTotalWallMilliseconds
            || terminalTotalRawTicks != copy.terminalTotalRawTicks
            || terminalRunReceiptSHA256 != DeepRematchCompositeRON.DigestFile(Path.Combine(childRunDirectory, terminalRunReceiptPath))
            || terminalRunReceiptSHA256 != copy.terminalRunReceiptSHA256
            || !DigestFinalArtifacts(childRunDirectory, finalCheckpointSHA256, finalTapeSpanlogSHA256, finalCurveSHA256))
            throw new InvalidDataException("calibration record has a corrupt role, config, or execution window");
    }

    private static bool TrainingIdentityMatches(PolicyBoundaryTrainingReceipt left, PolicyBoundaryTrainingReceipt right)
        => left.ParentRunID == right.ParentRunID
            && left.SourceChildID == right.SourceChildID
            && left.ColdSeedDigest == right.ColdSeedDigest
            && left.TrainingStartStep == right.TrainingStartStep
            && left.TrainingEndStep == right.TrainingEndStep
            && left.ConfigReceiptDigest == right.ConfigReceiptDigest
            && left.CheckpointReceiptDigest == right.CheckpointReceiptDigest
            && left.ForkReceiptDigest == right.ForkReceiptDigest
            && left.ForkAuthorityDigest == right.ForkAuthorityDigest
            && left.ContentDigest == right.ContentDigest
            && left.ReceiptDigest == right.ReceiptDigest;

    private static bool DigestFinalArtifacts(string directory, string checkpoint, string tape, string curve)
        => DeepRematchCompositeRON.DigestCheckpoint(directory) == checkpoint
            && DeepRematchCompositeRON.DigestFile(Path.Combine(directory, "tape.spanlog")) == tape
            && DeepRematchCompositeRON.DigestFile(Path.Combine(directory, "curve.tsv")) == curve;

    private static string NormalizeArtifactPath(string directory, string path)
        => Path.IsPathRooted(path) ? Path.GetRelativePath(directory, path) : path;
}

[RonObject]
internal partial class DeepRematchAnytimeCountsRecord
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

    internal static DeepRematchAnytimeCountsRecord FromCounts(in EmlDeliberationCounts value) => new()
    {
        candidateEvaluations = value.CandidateEvaluations, logicalProgramPoints = value.LogicalProgramPoints,
        executedProgramPoints = value.ExecutedProgramPoints, inverseTransforms = value.InverseTransforms,
        hashProbes = value.HashProbes, joinAttempts = value.JoinAttempts, joinHits = value.JoinHits,
        processTerms = value.ProcessTerms, verifierProgramPoints = value.VerifierProgramPoints,
        candidateSupplyItems = value.CandidateSupplyItems, lawRewriteApplications = value.LawRewriteApplications,
        lawRewriteTreeNodes = value.LawRewriteTreeNodes,
    };

    internal EmlDeliberationCounts ToCounts() => new(candidateEvaluations, logicalProgramPoints, executedProgramPoints,
        inverseTransforms, hashProbes, joinAttempts, joinHits, processTerms, verifierProgramPoints,
        candidateSupplyItems, lawRewriteApplications, lawRewriteTreeNodes);
}

[RonObject]
internal partial class DeepRematchAnytimePointRecord
{
    public string pointID = "";
    public string previousDigest = "";
    public string digest = "";
    public string runID = "";
    public string configID = "";
    public string chainID = "";
    public string armID = "";
    public string parentPointID = "";
    public int rung;
    public int prefixStep;
    public int windowIndex;
    public string boundary = "";
    public long exactClasses;
    public long theoremClasses;
    public long certificateClasses;
    public long closedObligations;
    public long heldOutCaptures;
    public long heldOutBestK;
    public long verifiedLaws;
    public long verifiedProofs;
    public DeepRematchAnytimeCountsRecord fuel = new();
    public DeepRematchAnytimeCountsRecord windowPlannedFuel = new();
    public DeepRematchAnytimeCountsRecord windowActualFuel = new();
    public long evaluatorIntervals;
    public long windowPlannedEvaluatorIntervals;
    public long windowEvaluatorIntervals;
    public bool windowComplete;
    public bool evidenceVerified;
    public bool killEligible;
    public bool dominated;
    public bool activeFunding;
    public bool activeFork;
    public bool activeObligation;
    public bool pendingResolution;
    public bool windowSettled;
    public bool runTerminal;
    public int graceUntilWindow;
    public string dominatorPointID = "";
    public double residual;
    public double rate;
    public double meanz;
    public double wallMilliseconds;
    public string evidenceDigest = "";

    internal static DeepRematchAnytimePointRecord FromPoint(in EmlAnytimeCurvePoint point) => new()
    {
        pointID = point.PointID, previousDigest = point.PreviousDigest, digest = point.Digest,
        runID = point.RunID, configID = point.ConfigID, chainID = point.ChainID, armID = point.ArmID,
        parentPointID = point.ParentPointID, rung = point.Rung, prefixStep = point.PrefixStep,
        windowIndex = point.WindowIndex, boundary = point.Boundary,
        exactClasses = point.Quality.ExactClasses, theoremClasses = point.Quality.TheoremClasses,
        certificateClasses = point.Quality.CertificateClasses, closedObligations = point.Quality.ClosedObligations,
        heldOutCaptures = point.Quality.HeldOutCaptures, heldOutBestK = point.Quality.HeldOutBestK,
        verifiedLaws = point.Quality.VerifiedLaws, verifiedProofs = point.Quality.VerifiedProofs,
        fuel = Counts(point.Fuel),
        windowPlannedFuel = Counts(point.WindowPlannedFuel),
        windowActualFuel = Counts(point.WindowActualFuel),
        evaluatorIntervals = point.EvaluatorIntervals, windowPlannedEvaluatorIntervals = point.WindowPlannedEvaluatorIntervals,
        windowEvaluatorIntervals = point.WindowEvaluatorIntervals, windowComplete = point.WindowComplete,
        evidenceVerified = point.EvidenceVerified, killEligible = point.KillEligible, dominated = point.Dominated,
        activeFunding = point.ActiveFunding, activeFork = point.ActiveFork, activeObligation = point.ActiveObligation,
        pendingResolution = point.PendingResolution, windowSettled = point.WindowSettled, runTerminal = point.RunTerminal,
        graceUntilWindow = point.GraceUntilWindow, dominatorPointID = point.DominatorPointID, residual = point.Residual,
        rate = point.Rate, meanz = point.Meanz, wallMilliseconds = point.WallMilliseconds, evidenceDigest = point.EvidenceDigest,
    };

    internal EmlAnytimeCurvePoint ToPoint() => new(pointID, previousDigest, digest, runID, configID, chainID, armID,
        parentPointID, rung, prefixStep, windowIndex, boundary,
        new EmlAnytimeCommitments(exactClasses, theoremClasses, certificateClasses, closedObligations,
            heldOutCaptures, heldOutBestK, verifiedLaws, verifiedProofs), fuel.ToCounts(), windowPlannedFuel.ToCounts(),
        windowActualFuel.ToCounts(), evaluatorIntervals, windowPlannedEvaluatorIntervals, windowEvaluatorIntervals,
        windowComplete, evidenceVerified, killEligible, dominated, activeFunding, activeFork, activeObligation,
        pendingResolution, windowSettled, runTerminal, graceUntilWindow, dominatorPointID, residual, rate, meanz,
        wallMilliseconds, evidenceDigest);

    private static DeepRematchAnytimeCountsRecord Counts(EmlDeliberationCounts value)
        => DeepRematchAnytimeCountsRecord.FromCounts(in value);
}

[RonObject]
internal partial class DeepRematchAnytimePrefixRecord
{
    public bool passed;
    public bool banked;
    public string bankReason = "";
    public DeepRematchAnytimePointRecord acceptedPoint = new();
    public DeepRematchAnytimeCountsRecord plannedFuel = new();
    public DeepRematchAnytimeCountsRecord actualFuel = new();
    public DeepRematchAnytimeCountsRecord refundFuel = new();
    public long evaluatorIntervals;
    public long certificates;
    public double? evaluatorPerCertificate;
    public string acceptedPointDigest = "";

    internal static DeepRematchAnytimePrefixRecord FromPrefix(in EmlAnytimeEvaluationPrefix prefix)
    {
        EmlAnytimeCurvePoint point = prefix.AcceptedPoint;
        EmlDeliberationCounts planned = prefix.PlannedFuel;
        EmlDeliberationCounts actual = prefix.ActualFuel;
        EmlDeliberationCounts refund = prefix.RefundFuel;
        return new()
        {
            passed = prefix.Passed, banked = prefix.Banked, bankReason = prefix.BankReason,
            acceptedPoint = DeepRematchAnytimePointRecord.FromPoint(in point),
            plannedFuel = Counts(planned), actualFuel = Counts(actual), refundFuel = Counts(refund),
            evaluatorIntervals = prefix.EvaluatorIntervals, certificates = prefix.Certificates,
            evaluatorPerCertificate = prefix.EvaluatorPerCertificate, acceptedPointDigest = prefix.Digest,
        };
    }

    private static DeepRematchAnytimeCountsRecord Counts(EmlDeliberationCounts value)
        => DeepRematchAnytimeCountsRecord.FromCounts(in value);

    internal void Validate(string childRunID, int maxStep)
    {
        EmlAnytimeCurvePoint point = acceptedPoint.ToPoint();
        if (!point.VerifyDigest() || point.Digest != acceptedPointDigest || point.RunID != childRunID
            || point.PrefixStep <= 0 || point.PrefixStep > maxStep
            || point.WindowIndex <= 0 || !point.EvidenceVerified || !point.WindowComplete || !point.KillEligible
            || point.Dominated || point.ActiveFunding || point.ActiveFork || point.ActiveObligation
            || point.PendingResolution || !point.WindowSettled || point.Quality.ExactClasses <= 63
            || point.RunID.Length == 0 || point.ConfigID.Length == 0
            || point.ChainID.Length == 0 || point.ArmID.Length == 0 || evaluatorIntervals < 0 || certificates < 0
            || !string.Equals(point.Digest, acceptedPoint.digest, StringComparison.Ordinal))
            throw new InvalidDataException("evaluation anytime prefix evidence is corrupt");
        EmlDeliberationCounts planned = plannedFuel.ToCounts();
        EmlDeliberationCounts actual = actualFuel.ToCounts();
        EmlDeliberationCounts refund = refundFuel.ToCounts();
        planned.ValidateNonnegative("anytime planned fuel"); actual.ValidateNonnegative("anytime actual fuel"); refund.ValidateNonnegative("anytime refund fuel");
        if (refund != EmlDeliberationCounts.Subtract(in planned, in actual)
            || point.EvaluatorIntervals != evaluatorIntervals || point.Quality.CertificateClasses < certificates
            || (evaluatorPerCertificate is double rate && (!double.IsFinite(rate) || rate < 0)))
            throw new InvalidDataException("evaluation anytime prefix accounting is corrupt");
    }

    internal void Validate(string childRunID, int maxStep, in EmlAnytimeEvaluationPrefix authority)
    {
        Validate(childRunID, maxStep);
        EmlAnytimeCurvePoint point = acceptedPoint.ToPoint();
        if (!authority.AcceptedPoint.Equals(point)
            || authority.Digest != acceptedPointDigest
            || authority.PlannedFuel != plannedFuel.ToCounts()
            || authority.ActualFuel != actualFuel.ToCounts()
            || authority.RefundFuel != refundFuel.ToCounts()
            || authority.EvaluatorIntervals != evaluatorIntervals
            || authority.Certificates != certificates
            || authority.Passed != passed
            || authority.Banked != banked
            || authority.BankReason != bankReason
            || authority.EvaluatorPerCertificate != evaluatorPerCertificate)
            throw new InvalidDataException("evaluation anytime prefix disagrees with the freshly accepted settled curve point");
    }
}

[RonObject]
internal partial class DeepRematchEvaluationRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string childRunDirectory = "";
    public string mountPath = "";
    public string mountSidecarSHA256 = "";
    public string destinationHandshakePath = "";
    public string destinationHandshakeSidecarSHA256 = "";
    public string destinationHandshakeReceiptDigest = "";
    public ulong destinationHandshakeDecisionID;
    public string forkAuthorityDigest = "";
    public string trainingContentDigest = "";
    public string trainingReceiptDigest = "";
    public PolicyBoundaryTrainingReceipt training = new();
    public PolicyBoundaryMountReceipt mount = new();
    public ulong destinationDecisionReadoutFingerprint;
    public ulong destinationDecisionReadoutRevision;
    public DeepRematchAnytimePrefixRecord? anytime;
    public string persistedConfigDigest = "";
    public int seedStartStep;
    public int plannedNextStep;
    public int actualNextStep;
    public long mountWallMilliseconds;
    public long handshakeWallMilliseconds;
    public long handshakeRawTicks;
    public long mountRawTicks;
    public long runtimeBindWallMilliseconds;
    public long executionWallMilliseconds;
    public long terminalVerifierWallMilliseconds;
    public long wallMilliseconds;
    public bool calibrationRuntimeStateCopied;
    public bool terminalCheckpointExact;
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public string terminalRunReceiptPath = "";
    public string terminalRunReceiptSHA256 = "";
    public int terminalActualNextStep;
    public long terminalExecutionWallMilliseconds;
    public long terminalExecutionRawTicks;
    public long terminalReceiptVerifierWallMilliseconds;
    public long terminalReceiptVerifierRawTicks;
    public long terminalTotalWallMilliseconds;
    public long terminalTotalRawTicks;
    public int emlHandshakeSettlementCount;
    public long emlHandshakeEvaluatorCalls;
    public string emlHandshakeDigest = "";
    public string emlHandshakePointID = "";
    public string emlHandshakePointDigest = "";
    public string emlHandshakeSettlementDigest = "";
    public string emlHandshakeCursorPath = "";
    public string emlHandshakeCursorSHA256 = "";

    internal static DeepRematchEvaluationRecord FromReceipt(in DeepRematchEvaluationReceipt receipt) => new()
    {
        schemaVersion = DeepRematchCompositeRON.SchemaVersion,
        parentRunID = receipt.ParentRunID,
        childRunDirectory = receipt.ChildRunDirectory,
        mountPath = NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.MountPath),
        mountSidecarSHA256 = DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(receipt.ChildRunDirectory, NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.MountPath), "mount sidecar")),
        destinationHandshakePath = NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.HandshakePath ?? ""),
        destinationHandshakeSidecarSHA256 = string.IsNullOrWhiteSpace(receipt.HandshakePath) ? "" : DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(receipt.ChildRunDirectory, NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.HandshakePath), "destination handshake sidecar")),
        destinationHandshakeReceiptDigest = receipt.HandshakeReceiptDigest ?? "",
        destinationHandshakeDecisionID = receipt.HandshakeDecisionID,
        forkAuthorityDigest = receipt.Training.ForkAuthorityDigest,
        trainingContentDigest = receipt.Training.ContentDigest,
        trainingReceiptDigest = receipt.Training.ReceiptDigest,
        training = receipt.Training,
        mount = receipt.Mount,
        destinationDecisionReadoutFingerprint = receipt.Mount.DestinationDecisionReadoutFingerprint,
        destinationDecisionReadoutRevision = receipt.Mount.DestinationDecisionReadoutRevision,
        anytime = receipt.Anytime is EmlAnytimeEvaluationPrefix prefix ? DeepRematchAnytimePrefixRecord.FromPrefix(in prefix) : null,
        persistedConfigDigest = receipt.SeedLoad.PersistedConfigDigest,
        seedStartStep = receipt.StepSpan.SeedNextStep,
        plannedNextStep = receipt.StepSpan.PlannedNextStep,
        actualNextStep = receipt.StepSpan.ActualNextStep,
        mountWallMilliseconds = receipt.MountWallMilliseconds,
        handshakeWallMilliseconds = receipt.HandshakeWallMilliseconds,
        handshakeRawTicks = receipt.HandshakeRawTicks,
        mountRawTicks = receipt.MountRawTicks,
        runtimeBindWallMilliseconds = receipt.RuntimeBindWallMilliseconds,
        executionWallMilliseconds = receipt.ExecutionWallMilliseconds,
        terminalVerifierWallMilliseconds = receipt.TerminalVerifierWallMilliseconds,
        wallMilliseconds = receipt.WallMilliseconds,
        calibrationRuntimeStateCopied = receipt.CalibrationRuntimeStateCopied,
        terminalCheckpointExact = receipt.TerminalCheckpointExact,
        finalCheckpointSHA256 = receipt.FinalDigests.CheckpointSHA256,
        finalTapeSpanlogSHA256 = receipt.FinalDigests.TapeSpanlogSHA256,
        finalCurveSHA256 = receipt.FinalDigests.CurveSHA256,
        terminalRunReceiptPath = CortexForkTerminalRunReceipt.FileName,
        terminalRunReceiptSHA256 = DeepRematchCompositeRON.DigestFile(Path.Combine(receipt.ChildRunDirectory, CortexForkTerminalRunReceipt.FileName)),
        terminalActualNextStep = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).actualNextStep,
        terminalExecutionWallMilliseconds = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).executionWallMilliseconds,
        terminalExecutionRawTicks = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).executionRawTicks,
        terminalReceiptVerifierWallMilliseconds = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).terminalVerifierWallMilliseconds,
        terminalReceiptVerifierRawTicks = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).terminalVerifierRawTicks,
        terminalTotalWallMilliseconds = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).totalWallMilliseconds,
        terminalTotalRawTicks = CortexForkTerminalRunReceipt.Read(receipt.ChildRunDirectory).totalRawTicks,
        emlHandshakeSettlementCount = receipt.FuelCursor?.SettlementCount ?? -1,
        emlHandshakeEvaluatorCalls = receipt.FuelCursor?.EvaluatorCalls ?? -1,
        emlHandshakeDigest = receipt.FuelCursor?.Digest ?? "",
        emlHandshakePointID = receipt.FuelCursor?.PointID ?? "",
        emlHandshakePointDigest = receipt.FuelCursor?.PointDigest ?? "",
        emlHandshakeSettlementDigest = receipt.FuelCursor?.SettlementDigest ?? "",
        emlHandshakeCursorPath = receipt.FuelCursorPath is null ? "" : NormalizeArtifactPath(receipt.ChildRunDirectory, receipt.FuelCursorPath),
        emlHandshakeCursorSHA256 = receipt.FuelCursorSHA256 ?? "",
    };

    internal void Validate(DeepRematchParentRecord parent, DeepRematchColdSeedRecord cold,
        DeepRematchChildCopyRecord copy, DeepRematchCalibrationRecord calibration)
        => Validate(parent, cold, copy, DeepRematchCalibrationAuthority.FromStandardRecord(parent, cold, calibration));

    internal void Validate(DeepRematchParentRecord parent, DeepRematchColdSeedRecord cold,
        DeepRematchChildCopyRecord copy, DeepRematchCalibrationAuthority calibration)
    {
        if (schemaVersion != DeepRematchCompositeRON.SchemaVersion || parentRunID != parent.runID
            || Path.GetFullPath(childRunDirectory) != Path.GetFullPath(copy.childRunDirectory))
            throw new InvalidDataException("evaluation record is not bound to its child copy");
        training.Validate(HomeostatPolicyBoundaryDomain.Instance);
        mount.Validate(in training, parent.runID, copy.childRunID, cold.coldSeedDigest, HomeostatPolicyBoundaryDomain.Instance);
        if (anytime is not null && (emlHandshakeSettlementCount < 0 || emlHandshakeEvaluatorCalls < 0
            || string.IsNullOrWhiteSpace(emlHandshakeDigest) || string.IsNullOrWhiteSpace(emlHandshakePointID)
            || emlHandshakePointDigest.Length != 64 || emlHandshakeSettlementDigest.Length != 64
            || emlHandshakeCursorPath != ReplayCalc.DeepRematchFuelCursorSidecarFile
            || emlHandshakeCursorSHA256.Length != 64))
            throw new InvalidDataException("evaluation record omits the post-handshake EML cursor");
        DeepRematchCompositeRON.RequireDigest(forkAuthorityDigest, "evaluation fork authority");
        DeepRematchCompositeRON.RequireDigest(trainingContentDigest, "evaluation training content");
        DeepRematchCompositeRON.RequireDigest(trainingReceiptDigest, "evaluation training receipt");
        DeepRematchCompositeRON.RequireDigest(mountSidecarSHA256, "evaluation mount sidecar");
        bool handshakeMount = mount.Relation == PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake;
        if (handshakeMount)
            DeepRematchCompositeRON.RequireDigest(destinationHandshakeSidecarSHA256, "destination handshake sidecar");
        if (forkAuthorityDigest != training.ForkAuthorityDigest
            || trainingContentDigest != training.ContentDigest
            || trainingReceiptDigest != training.ReceiptDigest
            || training.ReceiptDigest != calibration.TrainingReceiptDigest
            || training.ForkAuthorityDigest != calibration.TrainingForkAuthorityDigest
            || training.ContentDigest != calibration.TrainingContentDigest
            || training.SourceChildID != calibration.TrainingSourceChildID
            || mount.SourceChildID != calibration.TrainingSourceChildID
            || mount.DestinationChildID != copy.childRunID
            || destinationDecisionReadoutFingerprint == 0
            || destinationDecisionReadoutRevision == 0
            || destinationDecisionReadoutFingerprint != mount.DestinationDecisionReadoutFingerprint
            || destinationDecisionReadoutRevision != mount.DestinationDecisionReadoutRevision
            || mount.DestinationDecisionReadoutFingerprint == 0
            || mount.DestinationDecisionReadoutRevision == 0
            || (handshakeMount && (mount.SchemaVersion < 2
                || destinationHandshakePath.Length == 0
                || destinationHandshakeDecisionID == 0
                || destinationHandshakeReceiptDigest != mount.DestinationHandshakeReceiptDigest
                || destinationHandshakeReceiptDigest.Length != 64
                || destinationHandshakePath != "homeostat.destination-handshake.ron"
                || handshakeWallMilliseconds < 0
                || handshakeRawTicks <= 0
                || mountRawTicks <= 0))
            || emlHandshakeCursorSHA256 != DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(childRunDirectory, emlHandshakeCursorPath, "EML handshake cursor"))
            || mount.MountStep != 0
            || !handshakeMount || copy.endStep < 2
            || mount.EvaluationStartStep != 1
            || mount.EvaluationEndStep != copy.endStep - 1
            || mountSidecarSHA256 != DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(childRunDirectory, mountPath, "mount sidecar"))
            || persistedConfigDigest != cold.persistedConfigDigest || string.IsNullOrWhiteSpace(mountPath)
            || seedStartStep != copy.startStep || plannedNextStep != copy.endStep || actualNextStep != plannedNextStep
            || mountWallMilliseconds < 0 || handshakeWallMilliseconds < 0 || runtimeBindWallMilliseconds < 0 || executionWallMilliseconds < 0 || terminalVerifierWallMilliseconds < 0
            || wallMilliseconds < 0 || calibrationRuntimeStateCopied || !terminalCheckpointExact
            || terminalRunReceiptPath != CortexForkTerminalRunReceipt.FileName
            || terminalRunReceiptSHA256.Length != 64
            || terminalActualNextStep != actualNextStep
            || terminalExecutionWallMilliseconds != executionWallMilliseconds
            || terminalExecutionRawTicks <= 0
            || terminalReceiptVerifierWallMilliseconds != copy.terminalVerifierWallMilliseconds
            || terminalReceiptVerifierRawTicks != copy.terminalVerifierRawTicks
            || terminalTotalWallMilliseconds != copy.terminalTotalWallMilliseconds
            || terminalTotalRawTicks != copy.terminalTotalRawTicks
            || terminalRunReceiptSHA256 != copy.terminalRunReceiptSHA256
            || terminalRunReceiptSHA256 != DeepRematchCompositeRON.DigestFile(Path.Combine(childRunDirectory, terminalRunReceiptPath))
            || checked(mountWallMilliseconds + handshakeWallMilliseconds) > executionWallMilliseconds
            || checked(copy.seedIOWallMilliseconds + runtimeBindWallMilliseconds + executionWallMilliseconds + terminalVerifierWallMilliseconds) > wallMilliseconds
            || mountWallMilliseconds > executionWallMilliseconds
            || !string.Equals(mountPath, "policy-boundary.mount.ron", StringComparison.Ordinal)
            || !DigestFinalArtifacts(childRunDirectory, finalCheckpointSHA256, finalTapeSpanlogSHA256, finalCurveSHA256))
            throw new InvalidDataException("evaluation record has a corrupt role, config, or execution window");
        string mountFile = DeepRematchCompositeRON.ResolveChildArtifact(childRunDirectory, mountPath, "mount sidecar");
        byte[] mountBytes = File.ReadAllBytes(mountFile);
        PolicyBoundaryMountReceipt decodedMount = PolicyBoundaryMountReceipt.Decode(
            mountBytes, in training, parent.runID, copy.childRunID, cold.coldSeedDigest, HomeostatPolicyBoundaryDomain.Instance);
        if (mountSidecarSHA256 != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(mountBytes))
            || !MountIdentityMatches(decodedMount, mount))
            throw new InvalidDataException("evaluation mount sidecar identities disagree with its typed record");
        if (handshakeMount)
        {
            string handshakeFile = DeepRematchCompositeRON.ResolveChildArtifact(childRunDirectory, destinationHandshakePath, "destination handshake sidecar");
            byte[] handshakeBytes = File.ReadAllBytes(handshakeFile);
            HomeostatDestinationHandshakeReceipt decodedHandshake = HomeostatDestinationHandshakeReceipt.Decode(handshakeBytes);
            if (destinationHandshakeSidecarSHA256 != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(handshakeBytes))
                || decodedHandshake.ReceiptDigest != destinationHandshakeReceiptDigest
                || decodedHandshake.DecisionID != destinationHandshakeDecisionID
                || decodedHandshake.ReceiptDigest != mount.DestinationHandshakeReceiptDigest)
                throw new InvalidDataException("evaluation destination handshake sidecar identities disagree with its typed record");
        }
        if (string.IsNullOrWhiteSpace(emlHandshakeCursorPath) || emlHandshakeCursorPath != ReplayCalc.DeepRematchFuelCursorSidecarFile
            || emlHandshakeCursorSHA256.Length != 64 || emlHandshakeDigest.Length != 64
            || emlHandshakePointDigest.Length != 64 || emlHandshakeSettlementDigest.Length != 64)
            throw new InvalidDataException("evaluation record omits its persisted AFCU cursor binding");
        string cursorPath = DeepRematchCompositeRON.ResolveChildArtifact(childRunDirectory, emlHandshakeCursorPath, "EML handshake cursor");
        if (DeepRematchCompositeRON.DigestFile(cursorPath) != emlHandshakeCursorSHA256)
            throw new InvalidDataException("evaluation AFCU cursor sidecar digest changed");
        EmlDeepRematchFuelCursor cursor = RonSerializer.Deserialize<EmlDeepRematchFuelCursorDocument>(File.ReadAllBytes(cursorPath)).ToCursor();
        if (cursor.SettlementCount != emlHandshakeSettlementCount || cursor.EvaluatorCalls != emlHandshakeEvaluatorCalls
            || cursor.Digest != emlHandshakeDigest || cursor.PointID != emlHandshakePointID
            || cursor.PointDigest != emlHandshakePointDigest || cursor.SettlementDigest != emlHandshakeSettlementDigest)
            throw new InvalidDataException("evaluation AFCU cursor sidecar disagrees with its typed record");
        if (DeepRematchCompositeRON.IsHistoricalCheckpointDialect(childRunDirectory))
            ValidateHistoricalCursorAuthority(cursor);
        else
        {
            EmlDeepRematchFuelCursor checkpointCursor = ReplayCalc.ReadDeepRematchFuelCursorFromCheckpointImage(
                CheckpointDelta.ReadEffectiveSnapshot(childRunDirectory).EffectiveImage);
            if (checkpointCursor != cursor)
                throw new InvalidDataException("evaluation AFCU cursor disagrees with the final checkpoint");
        }
        anytime?.Validate(copy.childRunID, copy.endStep - 1);
    }

    private void ValidateHistoricalCursorAuthority(EmlDeepRematchFuelCursor cursor)
    {
        if (!terminalCheckpointExact || terminalActualNextStep != actualNextStep
            || finalCheckpointSHA256 != DeepRematchCompositeRON.DigestCheckpoint(childRunDirectory)
            || cursor.Digest != emlHandshakeDigest)
            throw new InvalidDataException("historical evaluation cursor is not bound to its terminal authority");
    }

    private static bool MountIdentityMatches(PolicyBoundaryMountReceipt left, PolicyBoundaryMountReceipt right)
        => left.ParentRunID == right.ParentRunID
            && left.SourceChildID == right.SourceChildID
            && left.DestinationChildID == right.DestinationChildID
            && left.ColdSeedDigest == right.ColdSeedDigest
            && left.TrainingReceiptDigest == right.TrainingReceiptDigest
            && left.SourceContentDigest == right.SourceContentDigest
            && left.Relation == right.Relation
            && left.EvaluationStartStep == right.EvaluationStartStep
            && left.EvaluationEndStep == right.EvaluationEndStep
            && left.MountStep == right.MountStep
            && left.DestinationDecisionReadoutFingerprint == right.DestinationDecisionReadoutFingerprint
            && left.DestinationDecisionReadoutRevision == right.DestinationDecisionReadoutRevision
            && left.DestinationHandshakeReceiptDigest == right.DestinationHandshakeReceiptDigest
            && left.DestinationHandshakeDecisionID == right.DestinationHandshakeDecisionID
            && left.ReceiptDigest == right.ReceiptDigest;

    private static string NormalizeArtifactPath(string directory, string path)
        => Path.IsPathRooted(path) ? Path.GetRelativePath(directory, path) : path;

    private static bool DigestFinalArtifacts(string directory, string checkpoint, string tape, string curve)
        => DeepRematchCompositeRON.DigestCheckpoint(directory) == checkpoint
            && DeepRematchCompositeRON.DigestFile(Path.Combine(directory, "tape.spanlog")) == tape
            && DeepRematchCompositeRON.DigestFile(Path.Combine(directory, "curve.tsv")) == curve;
}

[RonObject]
internal partial class DeepRematchEvaluationCallbackRecord
{
    internal const int CurrentSchemaVersion = 1;
    public int schemaVersion;
    public string parentRunID = "";
    public string attemptID = "";
    public string childRunID = "";
    public string role = "";
    public string terminalReceiptPath = "";
    public string terminalReceiptDigest = "";
    public string seedCheckpointSHA256 = "";
    public string seedTapeSpanlogSHA256 = "";
    public string seedCurveSHA256 = "";
    public string seedLoadBindingDigest = "";
    public string finalCheckpointSHA256 = "";
    public string finalTapeSpanlogSHA256 = "";
    public string finalCurveSHA256 = "";
    public bool calibrationRuntimeStateCopied;
    public DeepRematchEvaluationRecord evaluation = new();
    public string recordDigest = "";

    internal static DeepRematchEvaluationCallbackRecord FromReceipt(
        string parentRunID, string attemptID, string childRunID,
        in CortexForkSeedLoadReceipt seedLoad, in CortexForkDigests finalDigests,
        CortexForkTerminalRunReceipt terminal, in DeepRematchEvaluationRecord evaluation)
    {
        DeepRematchEvaluationCallbackRecord record = new()
        {
            schemaVersion = CurrentSchemaVersion,
            parentRunID = parentRunID,
            attemptID = attemptID,
            childRunID = childRunID,
            role = CortexForkRailRoles.Evaluation.ToString(),
            terminalReceiptPath = CortexForkTerminalRunReceipt.FileName,
            terminalReceiptDigest = DeepRematchCompositeRON.DigestFile(Path.Combine(evaluation.childRunDirectory, CortexForkTerminalRunReceipt.FileName)),
            seedCheckpointSHA256 = seedLoad.ExpectedDigests.CheckpointSHA256,
            seedTapeSpanlogSHA256 = seedLoad.ExpectedDigests.TapeSpanlogSHA256,
            seedCurveSHA256 = seedLoad.ExpectedDigests.CurveSHA256,
            seedLoadBindingDigest = seedLoad.BindingDigest,
            finalCheckpointSHA256 = finalDigests.CheckpointSHA256,
            finalTapeSpanlogSHA256 = finalDigests.TapeSpanlogSHA256,
            finalCurveSHA256 = finalDigests.CurveSHA256,
            calibrationRuntimeStateCopied = evaluation.calibrationRuntimeStateCopied,
            evaluation = evaluation,
        };
        record.recordDigest = record.ComputeDigest();
        return record;
    }

    internal string ComputeDigest()
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            RonSerializer.SerializeToUtf8(new DeepRematchEvaluationCallbackRecord
            {
                schemaVersion = schemaVersion, parentRunID = parentRunID, attemptID = attemptID,
                childRunID = childRunID, role = role, terminalReceiptPath = terminalReceiptPath,
                terminalReceiptDigest = terminalReceiptDigest, seedCheckpointSHA256 = seedCheckpointSHA256,
                seedTapeSpanlogSHA256 = seedTapeSpanlogSHA256, seedCurveSHA256 = seedCurveSHA256,
                seedLoadBindingDigest = seedLoadBindingDigest,
                finalCheckpointSHA256 = finalCheckpointSHA256, finalTapeSpanlogSHA256 = finalTapeSpanlogSHA256,
                finalCurveSHA256 = finalCurveSHA256, calibrationRuntimeStateCopied = calibrationRuntimeStateCopied,
                evaluation = evaluation,
            })));

    internal void Validate(string parentDirectory)
    {
        if (schemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(parentRunID)
            || string.IsNullOrWhiteSpace(attemptID) || Path.GetFileName(attemptID) != attemptID
            || role != CortexForkRailRoles.Evaluation.ToString() || childRunID != Path.GetFileName(evaluation.childRunDirectory)
            || terminalReceiptPath != CortexForkTerminalRunReceipt.FileName || terminalReceiptDigest.Length != 64
            || recordDigest != ComputeDigest())
            throw new InvalidDataException("evaluation callback record identity or digest is malformed");
        string childDirectory = Path.GetFullPath(evaluation.childRunDirectory);
        string expectedChildRoot = Path.GetFullPath(Path.Combine(parentDirectory, "children", childRunID));
        if (childDirectory != expectedChildRoot)
            throw new InvalidDataException("evaluation callback record child path is detached");
        string terminalPath = Path.Combine(childDirectory, terminalReceiptPath);
        if (!File.Exists(terminalPath) || DeepRematchCompositeRON.DigestFile(terminalPath) != terminalReceiptDigest)
            throw new InvalidDataException("evaluation callback terminal receipt is missing or changed");
        string terminalSeedLoadBindingDigest;
        if (DeepRematchCompositeRON.IsHistoricalCheckpointDialect(childDirectory))
        {
            DeepRematchLegacyTerminalAuthority terminal = DeepRematchLegacyTerminalAuthority.Read(childDirectory);
            terminalSeedLoadBindingDigest = terminal.TerminalRun.seedLoadBindingDigest;
            if (terminal.TerminalRun.parentRunID != parentRunID || terminal.TerminalRun.childRunID != childRunID
                || terminal.TerminalRun.role != CortexForkRailRoles.Evaluation || !terminal.TerminalRun.terminalCheckpointExact
                || !terminal.TerminalRun.terminalOccurrenceCheckExact || terminal.TerminalRun.exitCode != 0
                || terminal.TerminalRun.finalCheckpointSHA256 != finalCheckpointSHA256
                || terminal.TerminalRun.finalTapeSpanlogSHA256 != finalTapeSpanlogSHA256
                || terminal.TerminalRun.finalCurveSHA256 != finalCurveSHA256)
                throw new InvalidDataException("evaluation callback historical terminal verifier disagrees with its typed output");
        }
        else
        {
            CortexForkTerminalRunReceipt terminal = CortexForkTerminalRunReceipt.Read(childDirectory);
            terminalSeedLoadBindingDigest = terminal.seedLoadBindingDigest;
            if (terminal.parentRunID != parentRunID || terminal.childRunID != childRunID
                || terminal.role != CortexForkRailRoles.Evaluation || !terminal.terminalCheckpointExact
                || !terminal.terminalOccurrenceCheckExact || terminal.exitCode != 0
                || terminal.finalCheckpointSHA256 != finalCheckpointSHA256
                || terminal.finalTapeSpanlogSHA256 != finalTapeSpanlogSHA256
                || terminal.finalCurveSHA256 != finalCurveSHA256)
                throw new InvalidDataException("evaluation callback terminal verifier disagrees with its typed output");
        }
        DeepRematchCompositeRON.RequireDigest(seedCheckpointSHA256, "evaluation callback seed checkpoint");
        DeepRematchCompositeRON.RequireDigest(seedTapeSpanlogSHA256, "evaluation callback seed tape");
        DeepRematchCompositeRON.RequireDigest(seedCurveSHA256, "evaluation callback seed curve");
        if (seedLoadBindingDigest.Length != 64)
            throw new InvalidDataException("evaluation callback seed-load binding is missing");
        CortexForkSeedLoadRailDocument seedRailDocument = CortexForkTerminalRunReceipt.ReadSeedRailDocument(
            Path.Combine(childDirectory, "seed-load-receipt.ron"));
        if (seedRailDocument.StoredBindingDigest != seedLoadBindingDigest || terminalSeedLoadBindingDigest != seedLoadBindingDigest)
            throw new InvalidDataException("evaluation callback seed-load binding changed");
        DeepRematchCompositeRON.RequireDigest(finalCheckpointSHA256, "evaluation callback final checkpoint");
        DeepRematchCompositeRON.RequireDigest(finalTapeSpanlogSHA256, "evaluation callback final tape");
        DeepRematchCompositeRON.RequireDigest(finalCurveSHA256, "evaluation callback final curve");
        if (evaluation.parentRunID != parentRunID || evaluation.childRunDirectory != childDirectory
            || evaluation.finalCheckpointSHA256 != finalCheckpointSHA256
            || evaluation.finalTapeSpanlogSHA256 != finalTapeSpanlogSHA256
            || evaluation.finalCurveSHA256 != finalCurveSHA256
            || evaluation.calibrationRuntimeStateCopied != calibrationRuntimeStateCopied
            || evaluation.handshakeRawTicks <= 0 || evaluation.mountRawTicks <= 0
            || evaluation.handshakeWallMilliseconds < 0 || evaluation.mountWallMilliseconds < 0
            || !evaluation.terminalCheckpointExact)
            throw new InvalidDataException("evaluation callback output is incomplete or detached");
        if (evaluation.training is null || !evaluation.training.IsVerified(HomeostatPolicyBoundaryDomain.Instance))
            throw new InvalidDataException("evaluation callback omitted verified training authority");
        evaluation.training.Validate(HomeostatPolicyBoundaryDomain.Instance);
        evaluation.mount.Validate(in evaluation.training, parentRunID, childRunID,
            DeepRematchCompositeRON.Read<DeepRematchColdSeedRecord>(Path.Combine(parentDirectory, DeepRematchCompositeRON.ColdSeedRecordFile)).coldSeedDigest,
            HomeostatPolicyBoundaryDomain.Instance);
        if (evaluation.anytime is not null)
            evaluation.anytime.Validate(childRunID, evaluation.actualNextStep - 1);
        string mountPath = DeepRematchCompositeRON.ResolveChildArtifact(childDirectory, evaluation.mountPath, "evaluation mount sidecar");
        if (DeepRematchCompositeRON.DigestFile(mountPath) != evaluation.mountSidecarSHA256)
            throw new InvalidDataException("evaluation callback mount sidecar digest changed");
        if (string.IsNullOrWhiteSpace(evaluation.emlHandshakeCursorPath)
            || evaluation.emlHandshakeCursorSHA256.Length != 64
            || DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(childDirectory, evaluation.emlHandshakeCursorPath, "evaluation fuel cursor")) != evaluation.emlHandshakeCursorSHA256)
            throw new InvalidDataException("evaluation callback fuel cursor sidecar is missing or changed");
        if (evaluation.destinationHandshakePath.Length == 0 || evaluation.destinationHandshakeSidecarSHA256.Length != 64
            || DeepRematchCompositeRON.DigestFile(DeepRematchCompositeRON.ResolveChildArtifact(childDirectory, evaluation.destinationHandshakePath, "evaluation handshake sidecar")) != evaluation.destinationHandshakeSidecarSHA256)
            throw new InvalidDataException("evaluation callback handshake sidecar is missing or changed");
    }
}

[RonObject]
internal partial class DeepRematchEvaluationRecoverySettlementRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string attemptID = "";
    public string childRunID = "";
    public string phase = "";
    public string terminalReceiptPath = "";
    public string terminalReceiptDigest = "";
    public string landingOutcomePath = "";
    public string landingOutcomeDigest = "";
    public string landingOutcomeState = "";
    public string callbackRecordPath = "";
    public string callbackRecordDigest = "";
    public long recoveryWallMilliseconds;
    public long recoveryRawTicks;
    public string recordDigest = "";

    internal string ComputeDigest()
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            schemaVersion, parentRunID, attemptID, childRunID, phase,
            terminalReceiptPath, terminalReceiptDigest, landingOutcomePath, landingOutcomeDigest, landingOutcomeState,
            callbackRecordPath, callbackRecordDigest, recoveryWallMilliseconds, recoveryRawTicks))));

    internal void Validate(string parentDirectory)
    {
        if (schemaVersion != DeepRematchCompositeRON.EvaluationRecoverySettlementSchemaVersion
            || parentRunID != Run.RunIDFromDirectory(parentDirectory)
            || string.IsNullOrWhiteSpace(attemptID) || string.IsNullOrWhiteSpace(childRunID)
            || phase != nameof(DeepRematchCompositePhases.Evaluation)
            || recoveryWallMilliseconds < 0 || recoveryRawTicks <= 0
            || terminalReceiptDigest.Length != 64 || landingOutcomeDigest.Length != 64
            || callbackRecordDigest.Length != 64 || recordDigest != ComputeDigest())
            throw new InvalidDataException("evaluation recovery settlement identity or timing is malformed");
        string expectedTerminal = Path.Combine("children", childRunID, CortexForkTerminalRunReceipt.FileName).Replace(Path.DirectorySeparatorChar, '/');
        string expectedLanding = Path.Combine("children", childRunID, CortexForkLandingOutcomeReceipt.FileName).Replace(Path.DirectorySeparatorChar, '/');
        string expectedCallback = Path.Combine("children", childRunID, DeepRematchCompositeRON.EvaluationCallbackRecordFile).Replace(Path.DirectorySeparatorChar, '/');
        if (terminalReceiptPath != expectedTerminal || landingOutcomePath != expectedLanding
            || callbackRecordPath != expectedCallback
            || landingOutcomeState != CortexForkLandingOutcomeStates.Completed.ToString())
            throw new InvalidDataException("evaluation recovery settlement paths are not canonical");
        ValidatePath(parentDirectory, terminalReceiptPath, terminalReceiptDigest, "terminal receipt");
        ValidatePath(parentDirectory, landingOutcomePath, landingOutcomeDigest, "landing outcome");
        ValidatePath(parentDirectory, callbackRecordPath, callbackRecordDigest, "evaluation callback");
        CortexForkLandingOutcomeReceipt landing = CortexForkLandingOutcomeReceipt.Read(
            Path.GetDirectoryName(Path.Combine(parentDirectory, landingOutcomePath))!);
        if (landing.state != CortexForkLandingOutcomeStates.Completed || !landing.callbackReturned || !landing.authorityChainExact)
            throw new InvalidDataException("evaluation recovery settlement landing is not completed and exact");
        DeepRematchEvaluationCallbackRecord callback = DeepRematchCompositeRON.Read<DeepRematchEvaluationCallbackRecord>(
            Path.Combine(parentDirectory, callbackRecordPath));
        callback.Validate(parentDirectory);
        if (callback.parentRunID != parentRunID || callback.attemptID != attemptID || callback.childRunID != childRunID)
            throw new InvalidDataException("evaluation recovery settlement callback identity is detached");
    }

    private static void ValidatePath(string parentDirectory, string relativePath, string digest, string label)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"evaluation recovery settlement {label} path escaped parent");
        string path = Path.GetFullPath(Path.Combine(parentDirectory, relativePath));
        if (!path.StartsWith(Path.GetFullPath(parentDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(path) || DeepRematchCompositeRON.DigestFile(path) != digest)
            throw new InvalidDataException($"evaluation recovery settlement {label} binding changed");
    }
}

[RonObject]
internal partial class DeepRematchWallSegmentRecord
{
    public string phase = "";
    public long wallMilliseconds;
    public long rawTicks;
}

[RonObject]
internal partial class DeepRematchAccountingRecord
{
    public int schemaVersion;
    public long measuredCompositeWallMilliseconds;
    public long totalWallMilliseconds;
    public long unaccountedWallMilliseconds;
    public long measuredRawTicks;
    public long totalRawTicks;
    public long unaccountedRawTicks;
    public List<DeepRematchWallSegmentRecord> phases = [];

    internal static DeepRematchAccountingRecord FromAccounting(DeepRematchTotalAccounting accounting)
    {
        DeepRematchAccountingRecord record = new()
        {
            schemaVersion = DeepRematchCompositeRON.SchemaVersion,
            measuredCompositeWallMilliseconds = accounting.MeasuredCompositeWallMilliseconds,
            totalWallMilliseconds = accounting.TotalWallMilliseconds,
            unaccountedWallMilliseconds = accounting.UnaccountedWallMilliseconds,
            measuredRawTicks = accounting.MeasuredRawTicks,
            totalRawTicks = accounting.TotalRawTicks,
            unaccountedRawTicks = accounting.UnaccountedRawTicks,
        };
        foreach (DeepRematchWallSegment segment in accounting.Segments)
            record.phases.Add(new DeepRematchWallSegmentRecord { phase = segment.Phase.ToString(), wallMilliseconds = segment.WallMilliseconds, rawTicks = segment.RawTicks });
        return record;
    }

    internal void Validate(long expectedMeasuredWallMilliseconds)
    {
        if (schemaVersion != DeepRematchCompositeRON.SchemaVersion || measuredCompositeWallMilliseconds != expectedMeasuredWallMilliseconds)
            throw new InvalidDataException("accounting measured wall does not match the composite run");
        if (phases.Count == 0 || phases.Any(static phase => phase.wallMilliseconds < 0)
            || phases.Select(static phase => phase.phase).Distinct(StringComparer.Ordinal).Count() != phases.Count)
            throw new InvalidDataException("accounting phases are missing, duplicated, or negative");
        long sum = checked(phases.Sum(static phase => phase.wallMilliseconds));
        if (totalWallMilliseconds != sum || unaccountedWallMilliseconds != measuredCompositeWallMilliseconds - sum || unaccountedWallMilliseconds != 0)
            throw new InvalidDataException("accounting contains a dark wall residual");
        long rawSum = checked(phases.Sum(static phase => phase.rawTicks));
        if (measuredRawTicks > 0 && (phases.Any(static phase => phase.rawTicks <= 0)
            || totalRawTicks != rawSum || unaccountedRawTicks != measuredRawTicks - rawSum || unaccountedRawTicks != 0))
            throw new InvalidDataException("accounting contains a dark raw-clock residual");
    }
}

[RonObject]
internal partial class DeepRematchSchemaProbe
{
    public int schemaVersion;
}

[RonObject]
internal partial class DeepRematchCompositeRecord
{
    public int schemaVersion;
    public string parentRunID = "";
    public string parentRecordPath = "";
    public string coldSeedRecordPath = "";
    public string calibrationChildCopyRecordPath = "";
    public string calibrationTerminalRunReceiptPath = "";
    public string calibrationRecordPath = "";
    public string calibrationAuthorityPath = "";
    public string evaluationChildCopyRecordPath = "";
    public string evaluationTerminalRunReceiptPath = "";
    public string evaluationRecordPath = "";
    public string evaluationCallbackRecordPath = "";
    public string evaluationRecoverySettlementPath = "";
    public string accountingRecordPath = "";
    public string parentRecordDigest = "";
    public string coldSeedRecordDigest = "";
    public string calibrationChildCopyRecordDigest = "";
    public string calibrationTerminalRunReceiptDigest = "";
    public string calibrationRecordDigest = "";
    public string calibrationAuthorityDigest = "";
    public string evaluationChildCopyRecordDigest = "";
    public string evaluationTerminalRunReceiptDigest = "";
    public string evaluationRecordDigest = "";
    public string evaluationCallbackRecordDigest = "";
    public string evaluationRecoverySettlementDigest = "";
    public string coldSeedDigest = "";
    public string persistedConfigDigest = "";
    public long measuredWallMilliseconds;
    public string accountingDigest = "";
    public string selectedAttemptID = "";
    public string selectedAttemptAccountingPath = "";
    public string selectedAttemptAccountingDigest = "";
    public List<DeepRematchAttemptBinding> attemptAccounts = [];
    public long sealBoundaryRawTicks;
    public long sealBoundaryWallMilliseconds;
    public string phaseJournalPath = "";
    public string phaseJournalDigest = "";
    public string attemptJournalDigest = "";
    public string preManifestSealIOPath = "";
    public string preManifestSealIODigest = "";
    public bool externalOwnerTailRequired;


    internal void ValidatePersisted(
        string parentDirectory,
        DeepRematchParentRecord parent,
        DeepRematchColdSeedRecord cold,
        DeepRematchChildCopyRecord calibrationCopy,
        DeepRematchCalibrationRecord calibration,
        DeepRematchChildCopyRecord evaluationCopy,
        DeepRematchEvaluationRecord evaluation,
        DeepRematchAccountingRecord accounting,
        DeepRematchCalibrationAuthority? calibrationAuthority = null)
    {
        string parentPath = Path.GetFullPath(parentDirectory);
        if (schemaVersion != DeepRematchCompositeRON.CompositeRecordSchemaVersion
            || parentRunID != parent.runID
            || measuredWallMilliseconds != accounting.measuredCompositeWallMilliseconds
            || sealBoundaryRawTicks != accounting.measuredRawTicks
            || sealBoundaryWallMilliseconds != accounting.measuredCompositeWallMilliseconds
            || coldSeedDigest != cold.coldSeedDigest
            || persistedConfigDigest != cold.persistedConfigDigest)
            throw new InvalidDataException("persisted composite manifest is not bound to its typed records");
        if (!string.Equals(parentRecordPath, "deep-rematch.parent.ron", StringComparison.Ordinal)
            || !string.Equals(coldSeedRecordPath, "deep-rematch.cold-seed.ron", StringComparison.Ordinal)
            || !IsAccountingPath(accountingRecordPath))
            throw new InvalidDataException("persisted composite manifest has non-canonical parent paths");

        bool settlement = calibrationAuthority?.IsSettlement == true || calibrationAuthorityPath.Length != 0;
        string calibrationDirectory = settlement && calibrationAuthority is not null ? calibrationAuthority.ChildRunDirectory : calibrationCopy.childRunDirectory;
        string expectedCalibrationChildCopy = Path.GetRelativePath(parentPath, Path.Combine(calibrationDirectory, "deep-rematch.child-copy.ron"));
        string expectedCalibrationTerminal = Path.GetRelativePath(parentPath, Path.Combine(calibrationDirectory, CortexForkTerminalRunReceipt.FileName));
        string expectedCalibration = Path.GetRelativePath(parentPath, Path.Combine(calibrationDirectory, "deep-rematch.calibration.ron"));
        string expectedEvaluationChildCopy = Path.GetRelativePath(parentPath, Path.Combine(evaluationCopy.childRunDirectory, "deep-rematch.child-copy.ron"));
        string expectedEvaluationTerminal = Path.GetRelativePath(parentPath, Path.Combine(evaluationCopy.childRunDirectory, CortexForkTerminalRunReceipt.FileName));
        string expectedEvaluation = Path.GetRelativePath(parentPath, Path.Combine(evaluation.childRunDirectory, "deep-rematch.evaluation.ron"));
        string expectedEvaluationCallback = Path.GetRelativePath(parentPath, Path.Combine(evaluationCopy.childRunDirectory, DeepRematchCompositeRON.EvaluationCallbackRecordFile));
        if ((!settlement && !string.Equals(calibrationChildCopyRecordPath, expectedCalibrationChildCopy, StringComparison.Ordinal))
            || !string.Equals(calibrationTerminalRunReceiptPath, expectedCalibrationTerminal, StringComparison.Ordinal)
            || (!settlement && !string.Equals(calibrationRecordPath, expectedCalibration, StringComparison.Ordinal))
            || !string.Equals(evaluationChildCopyRecordPath, expectedEvaluationChildCopy, StringComparison.Ordinal)
            || !string.Equals(evaluationTerminalRunReceiptPath, expectedEvaluationTerminal, StringComparison.Ordinal)
            || !string.Equals(evaluationRecordPath, expectedEvaluation, StringComparison.Ordinal)
            || !string.Equals(evaluationCallbackRecordPath, expectedEvaluationCallback, StringComparison.Ordinal))
            throw new InvalidDataException("persisted composite manifest paths are not role-qualified child records");

        ValidateBinding(parentPath, parentRecordPath, parentRecordDigest, "parent");
        ValidateBinding(parentPath, coldSeedRecordPath, coldSeedRecordDigest, "cold seed");
        if (!settlement)
            ValidateBinding(parentPath, calibrationChildCopyRecordPath, calibrationChildCopyRecordDigest, "calibration child copy");
        ValidateBinding(parentPath, calibrationTerminalRunReceiptPath, calibrationTerminalRunReceiptDigest, "calibration terminal run receipt");
        if (!settlement)
            ValidateBinding(parentPath, calibrationRecordPath, calibrationRecordDigest, "calibration");
        else
        {
            if (string.IsNullOrWhiteSpace(calibrationAuthorityPath) || calibrationAuthorityDigest.Length != 64)
                throw new InvalidDataException("persisted composite manifest omitted callback calibration authority");
            ValidateBinding(parentPath, calibrationAuthorityPath, calibrationAuthorityDigest, "calibration authority");
            if (calibrationAuthority is null || !calibrationAuthority.IsSettlement || calibrationAuthority.Completion is null)
                throw new InvalidDataException("persisted composite manifest callback authority is not the settlement union arm");
            DeepRematchCallbackSettlementRecord selectedSettlement = DeepRematchCompositeRON.Read<DeepRematchCallbackSettlementRecord>(Path.Combine(parentPath, calibrationAuthorityPath));
            selectedSettlement.Validate(parentPath);
            string expectedSettlementPath = Path.Combine(DeepRematchCompositeRON.AttemptDirectory, selectedSettlement.attemptID + ".callback-settlement.ron")
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!string.Equals(calibrationAuthorityPath.Replace(Path.DirectorySeparatorChar, '/'), expectedSettlementPath, StringComparison.Ordinal))
                throw new InvalidDataException("persisted composite manifest selected a non-canonical callback settlement");
            if (selectedSettlement.recordDigest != calibrationAuthority.Completion.recordDigest
                || selectedSettlement.parentRunID != parent.runID
                || selectedSettlement.childRunID != calibrationAuthority.ChildRunID)
                throw new InvalidDataException("persisted composite manifest settlement authority disagrees with its selected typed authority");
            calibrationAuthority.Validate();
        }
        ValidateBinding(parentPath, evaluationChildCopyRecordPath, evaluationChildCopyRecordDigest, "evaluation child copy");
        ValidateBinding(parentPath, evaluationTerminalRunReceiptPath, evaluationTerminalRunReceiptDigest, "evaluation terminal run receipt");
        ValidateBinding(parentPath, evaluationRecordPath, evaluationRecordDigest, "evaluation");
        ValidateBinding(parentPath, evaluationCallbackRecordPath, evaluationCallbackRecordDigest, "evaluation callback");
        ValidateBinding(parentPath, accountingRecordPath, accountingDigest, "accounting");
        DeepRematchAttemptBinding selectedEvaluation = attemptAccounts.Single(account => account.phase == nameof(DeepRematchCompositePhases.Evaluation));
        DeepRematchAttemptBinding selectedCalibration = attemptAccounts.Single(account => account.phase == nameof(DeepRematchCompositePhases.Calibration));
        string selectedCalibrationChildID = settlement && calibrationAuthority is not null
            ? calibrationAuthority.ChildRunID : Path.GetFileName(calibration.childRunDirectory);
        if (selectedAttemptID != selectedEvaluation.attemptID
            || selectedEvaluation.childRunID != Path.GetFileName(evaluation.childRunDirectory)
            || selectedCalibration.childRunID != selectedCalibrationChildID
            || !selectedCalibration.childRunID.StartsWith("calibration_", StringComparison.Ordinal)
            || !selectedEvaluation.childRunID.StartsWith("evaluation_", StringComparison.Ordinal))
            throw new InvalidDataException("persisted selected attempt authority is swapped or not bound to its role child");
        DeepRematchEvaluationCallbackRecord callback = DeepRematchCompositeRON.Read<DeepRematchEvaluationCallbackRecord>(Path.Combine(parentPath, evaluationCallbackRecordPath));
        callback.Validate(parentPath);
        if (callback.parentRunID != parent.runID || callback.attemptID != selectedEvaluation.attemptID
            || callback.childRunID != selectedEvaluation.childRunID
            || callback.role != CortexForkRailRoles.Evaluation.ToString())
            throw new InvalidDataException("persisted evaluation callback authority is detached from its selected attempt");
        ValidateRecoverySettlement(parentPath, evaluationRecoverySettlementPath, evaluationRecoverySettlementDigest,
            selectedEvaluation.attemptID, required: false);
        if (!string.Equals(phaseJournalPath, DeepRematchCompositeRON.PhaseJournalFile, StringComparison.Ordinal))
            throw new InvalidDataException("persisted composite manifest phase journal path is not canonical");
        ValidateSelectedAttempt(parentDirectory);
        ValidateBinding(parentPath, phaseJournalPath, phaseJournalDigest, "phase journal");
        ValidateBinding(parentPath, DeepRematchCompositeRON.AttemptJournalFile, attemptJournalDigest, "attempt journal");
        if (!externalOwnerTailRequired)
            throw new InvalidDataException("persisted composite manifest omitted the external owner tail required after pre-manifest seal IO");
        if (!string.Equals(preManifestSealIOPath, DeepRematchCompositeRON.PreManifestSealIORecordFile, StringComparison.Ordinal))
            throw new InvalidDataException("persisted composite manifest pre-manifest seal IO path is not canonical");
        ValidateBinding(parentPath, preManifestSealIOPath, preManifestSealIODigest, "pre-manifest seal IO");
        ValidatePreManifestSealIO(parentPath, preManifestSealIOPath);
    }

    private static void ValidateRecoverySettlement(string parentDirectory, string relativePath, string digest,
        string expectedAttemptID, bool required)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (required || !string.IsNullOrWhiteSpace(digest))
                throw new InvalidDataException("evaluation recovery settlement binding is partial");
            return;
        }
        string expected = DeepRematchCompositeRON.EvaluationRecoverySettlementPath(expectedAttemptID)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!string.Equals(relativePath.Replace(Path.DirectorySeparatorChar, '/'), expected, StringComparison.Ordinal)
            || digest.Length != 64)
            throw new InvalidDataException("evaluation recovery settlement path is non-canonical");
        ValidateBinding(parentDirectory, relativePath, digest, "evaluation recovery settlement");
        DeepRematchEvaluationRecoverySettlementRecord settlement = DeepRematchCompositeRON.Read<DeepRematchEvaluationRecoverySettlementRecord>(Path.Combine(parentDirectory, relativePath));
        settlement.Validate(parentDirectory);
        if (settlement.attemptID != expectedAttemptID)
            throw new InvalidDataException("evaluation recovery settlement attempt binding changed");
    }

    private void ValidateSelectedAttempt(string parentDirectory)
    {
        DeepRematchAttemptJournalRecord journal = DeepRematchCompositeRON.ReadAttemptJournal(Run.Open(parentDirectory));
        (string AttemptID, DeepRematchAttemptStatuses Status, DeepRematchAttemptTransition Entry)[] current = journal.Current();
        if (current.Length != 2 || current.Any(static item => item.Status != DeepRematchAttemptStatuses.Sealed)
            || current.Count(static item => item.Entry.phase == nameof(DeepRematchCompositePhases.Calibration)) != 1
            || current.Count(static item => item.Entry.phase == nameof(DeepRematchCompositePhases.Evaluation)) != 1)
            throw new InvalidDataException("composite manifest has orphan or non-sealed current attempt authorities");
        if (string.IsNullOrWhiteSpace(selectedAttemptID) || string.IsNullOrWhiteSpace(selectedAttemptAccountingPath)
            || selectedAttemptAccountingDigest.Length != 64 || attemptAccounts.Count != 2
            || attemptAccounts.Count(account => account.phase == nameof(DeepRematchCompositePhases.Calibration)) != 1
            || attemptAccounts.Count(account => account.phase == nameof(DeepRematchCompositePhases.Evaluation)) != 1
            || !attemptAccounts.Any(account => account.attemptID == selectedAttemptID
                && account.accountingPath == selectedAttemptAccountingPath
                && account.accountingDigest == selectedAttemptAccountingDigest
                && account.status == nameof(DeepRematchAttemptStatuses.Sealed)))
            throw new InvalidDataException("composite manifest omits its selected sealed attempt account");
        ValidateBinding(parentDirectory, selectedAttemptAccountingPath, selectedAttemptAccountingDigest, "selected attempt accounting");
        foreach (DeepRematchAttemptBinding account in attemptAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.attemptID) || string.IsNullOrWhiteSpace(account.phase)
                || string.IsNullOrWhiteSpace(account.childRunID) || account.accountingDigest.Length != 64
                || account.status != nameof(DeepRematchAttemptStatuses.Sealed))
                throw new InvalidDataException("composite attempt account binding is incomplete");
            if (account.phase is not (nameof(DeepRematchCompositePhases.Calibration) or nameof(DeepRematchCompositePhases.Evaluation))
                || Path.GetFileName(account.childRunID) != account.childRunID
                || !account.childRunID.StartsWith(account.phase == nameof(DeepRematchCompositePhases.Calibration) ? "calibration_" : "evaluation_", StringComparison.Ordinal))
                throw new InvalidDataException("composite attempt account role or child identity is not canonical");
            ValidateBinding(parentDirectory, account.accountingPath, account.accountingDigest, "attempt account");
            (string AttemptID, DeepRematchAttemptStatuses Status, DeepRematchAttemptTransition Entry) journalAccount = current.Single(item => item.AttemptID == account.attemptID);
            if (journalAccount.Entry.phase != account.phase
                || journalAccount.Entry.childRunID != account.childRunID
                || journalAccount.Entry.accountingPath != account.accountingPath
                || journalAccount.Entry.accountingDigest != account.accountingDigest)
                throw new InvalidDataException("manifest attempt account is detached from its current journal authority");
            if (account.phase == nameof(DeepRematchCompositePhases.Calibration)
                && account.accountingPath.EndsWith(".callback-settlement.ron", StringComparison.Ordinal))
            {
                DeepRematchCallbackSettlementRecord settlement = DeepRematchCompositeRON.Read<DeepRematchCallbackSettlementRecord>(Path.Combine(parentDirectory, account.accountingPath));
                settlement.Validate(parentDirectory);
                if (settlement.attemptID != account.attemptID || settlement.childRunID != account.childRunID)
                    throw new InvalidDataException("composite callback settlement identity disagrees with its attempt account");
            }
            else
            {
                DeepRematchAttemptAccountingRecord typed = ReadAttemptAccounting(parentDirectory, account.accountingPath);
                if (typed.attemptID != account.attemptID || typed.phase != account.phase || typed.childRunID != account.childRunID)
                    throw new InvalidDataException("composite attempt account binding identity disagrees with its typed account");
            }
        }
    }

    private DeepRematchAttemptAccountingRecord ReadAttemptAccounting(string parentDirectory, string relativePath)
        => DeepRematchCompositeRON.ReadAttemptAccounting(parentDirectory, relativePath);

    private static void ValidateBinding(string parentDirectory, string relativePath, string digest, string label)
    {
        if (Path.IsPathRooted(relativePath) || string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} record path escaped the parent");
        string parent = Path.GetFullPath(parentDirectory);
        string path = Path.GetFullPath(Path.Combine(parent, relativePath));
        if (!path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal) && path != parent)
            throw new InvalidDataException($"composite {label} record path escaped the parent");
        DeepRematchCompositeRON.RequireDigest(digest, $"{label} record");
        if (DeepRematchCompositeRON.DigestFile(path) != digest)
            throw new InvalidDataException($"composite {label} record digest mismatch");
    }

    private static void ValidatePreManifestSealIO(string parentDirectory, string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(parentDirectory, relativePath));
        DeepRematchPreManifestSealIORecord record = RonSerializer.Deserialize<DeepRematchPreManifestSealIORecord>(File.ReadAllBytes(path));
        record.Validate();
    }

    private static bool IsAccountingPath(string path)
        => string.Equals(path, DeepRematchCompositeRON.AccountingRecordFile, StringComparison.Ordinal)
            || (path.StartsWith(DeepRematchCompositeRON.AttemptDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && path.EndsWith(".accounting.ron", StringComparison.Ordinal));
}

[RonObject]
internal partial class DeepRematchAttemptBinding
{
    public string attemptID = "";
    public string phase = "";
    public string childRunID = "";
    public string accountingPath = "";
    public string accountingDigest = "";
    public string status = "";
}
