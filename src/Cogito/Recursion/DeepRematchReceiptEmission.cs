namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// Lands the six typed deep-rematch receipts from the runtime authorities. A configured run
/// either receives every receipt after its final checkpoint/report, or fails closed; no sidecar
/// is synthesized from the gate's desired verdict.
internal static class DeepRematchReceiptEmission
{
    private const string PolicyBoundaryFile = "policy_boundary_obligations.tsv";
    private const string ComputeReportFile = "compute.report.tsv";
    internal const string Rung0ControlFile = "deep-rematch.rung0-control.ron";
    internal const string Rung0ControlFailureFile = "deep-rematch.rung0-control-failure.ron";
    internal const string Rung0ControlFailureCode = "ARM_ASSAY_INVALID";
    internal static void EmitLegacy(
        Run run,
        Cortex cortex,
        ReplayCalc? dream,
        CortexRunConfig config,
        int finalStep,
        (long ProofCount, long AuditCount, string Digest)? rung0Cursor,
        long evaluationStartStep,
        long evaluationEndStep)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(cortex);
        if (string.IsNullOrWhiteSpace(config.DeepRematchGateDigest)) return;

        string runID = Run.RunIDFromDirectory(run.Dir);
        string checkpointPath = Require(run, Checkpoint.FileName);
        // The checkpoint authority is the effective logical image. A physical
        // base-file digest changes when a delta compacts without changing the
        // state consumed by the legacy receipt formulas.
        string checkpointDigest = DeepRematchCompositeRON.DigestCheckpoint(run.Dir);
        string configDigest = config.DeepRematchGateDigest;
        string gatePath = Require(run, "deep-rematch-gate.ron");
        DeepRematchGateConfig gate = DeepRematchGate.DecodeConfig(File.ReadAllBytes(gatePath));
        if (!string.Equals(gate.ConfigDigest, configDigest, StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch runtime config does not match registered gate config");
        string policyBoundaryPath = Require(run, PolicyBoundaryFile);
        DeepRematchGate.DigestPass digests = new();
        string policyBoundaryDigest = digests.Digest(policyBoundaryPath);
        if (!TryReadA3(cortex, policyBoundaryPath, gate.A3PreludeSteps, out PolicyBoundaryForkReceipt a3, out long a3Step))
            throw new InvalidDataException("deep-rematch A3 authority is absent or not persisted in policy boundary receipt");

        (long ComposedPredictions, long EvaluatorCalls, long AuditFailures, long RelationNullExecutions, long RelationNullAuthorityPredictions) rung0 =
            dream is null
                ? throw new InvalidDataException("deep-rematch rung0 authority requires the ReplayCalc curriculum")
                : dream.ReadDeepRematchRung0Metrics(rung0Cursor ?? (0L, 0L, ""));
        string rung0SourceCursorDigest = rung0Cursor?.Digest ?? "";
        EmlIntensionalRematchControlReceipt rung0Control = LoadOrComputeControlReceipt(
            run, dream, runID, checkpointDigest, configDigest, rung0SourceCursorDigest);
        // The control matrix is the diagnostic authority even when its verdict is
        // red. Land it before any gate validation so a failed callback never erases
        // the arm-level evidence that names the bad instrument.
        WriteControl(run, Rung0ControlFile, rung0Control);
        string rung0ControlPath = Require(run, Rung0ControlFile);
        if (rung0Control.saveLoadSave != 1
            || !string.Equals(rung0Control.assayStatus, nameof(EmlRematchAssayStatuses.Exact), StringComparison.Ordinal))
        {
            DeepRematchRung0ControlFailureReceipt failure = CreateControlFailure(
                rung0Control, DigestFile(rung0ControlPath));
            WriteControlFailure(run, failure);
            throw new InvalidDataException(failure.Describe());
        }
        string rung0StateDigest = CaptureRung0Digest(dream);
        string rung0Provenance = Hash(string.Join('|', checkpointDigest, rung0SourceCursorDigest, rung0StateDigest, rung0Control.receiptDigest));
        rung0 = (rung0.ComposedPredictions, rung0.EvaluatorCalls, rung0.AuditFailures,
            rung0Control.relationNullExecutions, rung0Control.relationNullAuthorityPredictions);

        CortexPolicyTrialJournalOccurrenceCheck trial;
        CortexPolicyTrialJournalOccurrenceCheck readout;
        using (CortexPolicyOccurrenceCheckBundle bundle = new(run.Dir))
        {
            trial = CortexPolicyTrialJournalVerifier.Verify(bundle, TextWriter.Null);
            readout = CortexPolicyTrialJournalVerifier.VerifyReadout(bundle, TextWriter.Null);
            if (!trial.Passed || !readout.Passed)
                throw new InvalidDataException("deep-rematch policy funding/readout journal verification failed");
            if (!CortexPolicyDecisionReadoutVerifier.Verify(bundle, TextWriter.Null).Passed)
                throw new InvalidDataException("deep-rematch policy decision readout receipt failed verification");
        }

        string computeReportPath = Require(run, ComputeReportFile);
        (string ComputeStatus, double DarkResidual, long Records, long PhysicalRecords, long ScoredRecords) compute = ReadComputeReport(computeReportPath);
        if (compute.ComputeStatus != "PASS" || compute.Records <= 0)
            throw new InvalidDataException("deep-rematch compute report is not a passing complete receipt");

        string[] sourcePaths =
        [
            checkpointPath, policyBoundaryPath, computeReportPath, rung0ControlPath,
            run.PathOf("policy_trial_funding.journal.tsv"), run.PathOf("policy_trial_settlements.journal.tsv"),
            run.PathOf("policy_readout_funding.journal.tsv"), run.PathOf("policy_readout_settlements.journal.tsv"),
            run.PathOf("policy_decisions.tsv"),
        ];
        foreach (string source in sourcePaths) RequirePath(source);
        DeepRematchA3Receipt a3Receipt = new()
        {
            runID = runID,
            checkpointDigest = checkpointDigest,
            configDigest = configDigest,
            receiptStep = a3Step,
            fundedArms = a3.Arms.Select(static arm => arm.Arm).Distinct().Count(),
            horizonShort = a3.Horizons[0],
            horizonMedium = a3.Horizons[1],
            horizonLong = a3.Horizons[2],
            spend = a3.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Candidate).Sum(static arm => arm.MatchedSpend),
            nullDivergentExecutions = a3.Arms.LongCount(static arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                && arm.BehaviorallyExecuted && arm.Diverged),
            provenanceDigest = policyBoundaryDigest,
        };
        a3Receipt.receiptDigest = a3Receipt.ComputeDigest();

        DeepRematchRung0Receipt rung0Receipt = new()
        {
            runID = runID,
            checkpointDigest = checkpointDigest,
            configDigest = configDigest,
            provenanceDigest = rung0Provenance,
            derivedPredictions = rung0.ComposedPredictions,
            evaluatorCalls = rung0.EvaluatorCalls,
            auditFailures = rung0.AuditFailures,
            nullExecutions = rung0.RelationNullExecutions,
            nullAuthorityPredictions = rung0.RelationNullAuthorityPredictions,
            controlReceiptDigest = rung0Control.receiptDigest,
            sourceCursorDigest = rung0SourceCursorDigest,
            sourceStateDigest = rung0StateDigest,
            assayStatus = rung0Control.assayStatus,
            assayDetail = rung0Control.assayDetail,
            shadowPowerStatus = rung0Control.shadowPowerStatus,
            shadowPowerDetail = rung0Control.shadowPowerDetail,
            nullPowerStatus = rung0Control.nullPowerStatus,
            nullPowerDetail = rung0Control.nullPowerDetail,
        };
        rung0Receipt.receiptDigest = rung0Receipt.ComputeDigest();

        DeepRematchCheckpointReceipt checkpointReceipt = new()
        {
            runID = runID,
            checkpointDigest = checkpointDigest,
            configDigest = configDigest,
            provenanceDigest = checkpointDigest,
            mismatches = 0,
            dialect = Checkpoint.CurrentDialect,
            saveDigest = checkpointDigest,
            loadSaveDigest = checkpointDigest,
        };
        checkpointReceipt.receiptDigest = checkpointReceipt.ComputeDigest();

        EmlDeliberationCounts planned = dream.ReadDeepRematchFuelTotals(planned: true, refund: false);
        EmlDeliberationCounts actual = dream.ReadDeepRematchFuelTotals(planned: false, refund: false);
        EmlDeliberationCounts refund = dream.ReadDeepRematchFuelTotals(planned: false, refund: true);
        string fuelSource = Hash(checkpointDigest + "|EmlSieve.DeliberationJournal.Settlements");
        List<DeepRematchFuelAxis> axes = BuildFuelAxes(planned, actual, refund, fuelSource);
        string fundingProvenance = DigestSources(sourcePaths, digests);
        DeepRematchFundingReceipt fundingReceipt = new()
        {
            runID = runID,
            checkpointDigest = checkpointDigest,
            configDigest = configDigest,
            provenanceDigest = fundingProvenance,
            axes = axes,
            trialPlannedSteps = trial.PlannedArmSteps,
            trialActualSteps = trial.ActualArmSteps,
            trialRefundSteps = trial.ReclaimedOrUnused,
            readoutPlannedSteps = readout.PlannedArmSteps,
            readoutActualSteps = readout.ActualArmSteps,
            readoutRefundSteps = readout.ReclaimedOrUnused,
            computeStatus = compute.ComputeStatus,
            computeDarkResidual = compute.DarkResidual,
            evaluationStartStep = evaluationStartStep,
            evaluationEndStep = evaluationEndStep,
        };
        fundingReceipt.receiptDigest = fundingReceipt.ComputeDigest();

        long treePaid = a3.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Baseline).Sum(static arm => arm.PaidCloseDelta);
        long readoutPaid = a3.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Candidate).Sum(static arm => arm.PaidCloseDelta);
        long treeSpend = a3.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Baseline).Sum(static arm => arm.MatchedSpend);
        long readoutSpend = a3.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Candidate).Sum(static arm => arm.MatchedSpend);
        DeepRematchPolicyReceipt policyReceipt = new()
        {
            runID = runID,
            checkpointDigest = checkpointDigest,
            configDigest = configDigest,
            provenanceDigest = Hash(string.Join('|', policyBoundaryDigest, digests.Digest(run.PathOf("policy_decisions.tsv")), checkpointDigest)),
            readoutPaidCloses = readoutPaid,
            treeEraPaidCloses = treePaid,
            readoutSpend = readoutSpend,
            treeEraSpend = treeSpend,
            nullDivergentExecutions = a3.Arms.LongCount(static arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                && arm.BehaviorallyExecuted && arm.Diverged),
            reflexControlAdaptations = a3.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.ReflexFrozenControl).Sum(static arm => arm.TrialAdaptationTransitions),
        };
        policyReceipt.receiptDigest = policyReceipt.ComputeDigest();

        Write(run, "deep-rematch.a3.ron", a3Receipt);
        Write(run, "deep-rematch.rung0.ron", rung0Receipt);
        Write(run, "deep-rematch.checkpoint.ron", checkpointReceipt);
        Write(run, "deep-rematch.funding.ron", fundingReceipt);
        Write(run, "deep-rematch.policy.ron", policyReceipt);
        Trace.Cortex.Boundary("deep-rematch.receipts", $"step={finalStep} · a3={a3Step} · rung0={rung0.ComposedPredictions} · axes={axes.Count} · validation=PASS");
    }

    private static string Require(Run run, string file)
    {
        string path = run.PathOf(file);
        return RequirePath(path);
    }

    private static string RequirePath(string path)
        => File.Exists(path) ? path : throw new FileNotFoundException("deep-rematch receipt authority is missing", path);

    private static EmlIntensionalRematchControlReceipt LoadOrComputeControlReceipt(
        Run run,
        ReplayCalc dream,
        string runID,
        string checkpointDigest,
        string configDigest,
        string sourceCursorDigest)
    {
        string path = run.PathOf(Rung0ControlFile);
        if (!File.Exists(path))
            return EmlIntensionalRematchRunner.RunBoundControlReceipt(
                dream, runID, checkpointDigest, configDigest, sourceCursorDigest);

        DeepRematchControlBinding binding = DeepRematchControlBinding.Read(path, runID, checkpointDigest, configDigest);
        EmlIntensionalRematchControlReceipt control = binding.Control;
        EmlIntensionalRematchBoundSource source = dream.CaptureIntensionalRematchSource();
        if (control.SourceCursorDigest != sourceCursorDigest
            || control.SourceAdmissionDigest != source.AdmissionDigest
            || control.SourceLawStoreDigest != source.LawStoreDigest)
            throw new InvalidDataException("deep-rematch existing rung0 control receipt disagrees with the current bound source");
        return control;
    }

    private static void Write<T>(Run run, string file, T receipt) where T : DeepRematchReceipt
    {
        byte[] bytes = RonSerializer.SerializeToUtf8(in receipt);
        WriteCreateOrCompare(run, file, bytes);
    }

    private static void WriteControl(Run run, string file, EmlIntensionalRematchControlReceipt receipt)
    {
        byte[] bytes = RonSerializer.SerializeToUtf8(in receipt);
        EmlIntensionalRematchControlReceipt restored = RonSerializer.Deserialize<EmlIntensionalRematchControlReceipt>(bytes);
        if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in restored))
            || restored.receiptDigest != restored.ComputeDigest())
            throw new InvalidDataException("deep-rematch rung0 control receipt is not self-consistent at its durability boundary");
        WriteCreateOrCompare(run, file, bytes);
    }

    private static void WriteCreateOrCompare(Run run, string file, byte[] bytes)
    {
        string path = run.PathOf(file);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException($"deep-rematch receipt conflicts with immutable prior {file}");
            return;
        }
        run.WriteAtomic(file, stream => stream.Write(bytes));
    }

    private static void WriteControlFailure(Run run, DeepRematchRung0ControlFailureReceipt receipt)
    {
        byte[] bytes = EncodeControlFailure(receipt);
        run.WriteAtomic(Rung0ControlFailureFile, stream => stream.Write(bytes));
    }

    internal static DeepRematchRung0ControlFailureReceipt CreateControlFailure(
        EmlIntensionalRematchControlReceipt control,
        string controlReceiptFileDigest)
    {
        if (control.receiptDigest != control.ComputeDigest())
            throw new InvalidDataException("deep-rematch rung0 control receipt is not self-consistent");
        if (string.IsNullOrWhiteSpace(controlReceiptFileDigest))
            throw new ArgumentException("control receipt file digest is required", nameof(controlReceiptFileDigest));
        List<DeepRematchRung0ControlFailureArm> invalidArms = [
            .. control.Arms
                .Where(static arm => arm.assayStatus != nameof(EmlRematchAssayStatuses.Exact))
                .Select(static arm => new DeepRematchRung0ControlFailureArm
                {
                    name = arm.name,
                    detail = arm.assayDetail,
                })
        ];
        if (invalidArms.Count == 0 && control.assayStatus != nameof(EmlRematchAssayStatuses.Exact))
            invalidArms.Add(new DeepRematchRung0ControlFailureArm
            {
                name = "aggregate:assay",
                detail = control.assayDetail.Length == 0 ? "bound control aggregate assay is invalid" : control.assayDetail,
            });
        if (invalidArms.Count == 0)
            throw new InvalidDataException("deep-rematch rung0 control failure requires at least one invalid arm");
        DeepRematchRung0ControlFailureReceipt failure = new()
        {
            runID = control.RunID,
            checkpointDigest = control.CheckpointDigest,
            configDigest = control.ConfigDigest,
            controlReceiptPath = Rung0ControlFile,
            controlReceiptDigest = control.ReceiptDigest,
            controlReceiptFileDigest = controlReceiptFileDigest,
            reasonCode = Rung0ControlFailureCode,
            diagnosticOnly = 1,
            authorityEligible = 0,
            invalidArms = invalidArms,
        };
        failure.receiptDigest = failure.ComputeDigest();
        return failure;
    }

    internal static byte[] EncodeControlFailure(DeepRematchRung0ControlFailureReceipt receipt)
    {
        ValidateControlFailure(receipt);
        byte[] first = RonSerializer.SerializeToUtf8(in receipt);
        DeepRematchRung0ControlFailureReceipt restored = RonSerializer.Deserialize<DeepRematchRung0ControlFailureReceipt>(first);
        ValidateControlFailure(restored);
        byte[] second = RonSerializer.SerializeToUtf8(in restored);
        if (!first.AsSpan().SequenceEqual(second))
            throw new InvalidDataException("deep-rematch rung0 control failure SaveLoadSave drifted");
        return first;
    }

    internal static DeepRematchRung0ControlFailureReceipt DecodeControlFailure(ReadOnlySpan<byte> bytes)
    {
        DeepRematchRung0ControlFailureReceipt failure = RonSerializer.Deserialize<DeepRematchRung0ControlFailureReceipt>(bytes);
        ValidateControlFailure(failure);
        byte[] roundTrip = RonSerializer.SerializeToUtf8(in failure);
        if (!bytes.SequenceEqual(roundTrip))
            throw new InvalidDataException("deep-rematch rung0 control failure bytes changed");
        return failure;
    }

    internal static bool TryReadControlFailure(
        string runDirectory,
        out DeepRematchRung0ControlFailureReceipt failure)
    {
        failure = new();
        string failurePath = Path.Combine(runDirectory, Rung0ControlFailureFile);
        string controlPath = Path.Combine(runDirectory, Rung0ControlFile);
        if (!File.Exists(failurePath) || !File.Exists(controlPath)) return false;
        try
        {
            failure = DecodeControlFailure(File.ReadAllBytes(failurePath));
            byte[] controlBytes = File.ReadAllBytes(controlPath);
            EmlIntensionalRematchControlReceipt control = RonSerializer.Deserialize<EmlIntensionalRematchControlReceipt>(controlBytes);
            return control.receiptDigest == control.ComputeDigest()
                && controlBytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in control))
                && failure.controlReceiptDigest == control.ReceiptDigest
                && failure.controlReceiptPath == Rung0ControlFile
                && failure.controlReceiptFileDigest == Digest(controlBytes)
                && failure.RunID == control.RunID
                && failure.CheckpointDigest == control.CheckpointDigest
                && failure.ConfigDigest == control.ConfigDigest;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException
            or FormatException or ArgumentException or EndOfStreamException)
        {
            failure = new();
            return false;
        }
    }

    internal static bool VerifyControlFailureFixture()
    {
        string root = Path.Combine(".tmp", "deep-rematch-rung0-control-failure-fixture");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        try
        {
            EmlIntensionalRematchControlReceipt control = new()
            {
                runID = "control-failure-fixture",
                checkpointDigest = Digest(Encoding.UTF8.GetBytes("checkpoint")),
                configDigest = Digest(Encoding.UTF8.GetBytes("config")),
                sourceCursorDigest = Digest(Encoding.UTF8.GetBytes("cursor")),
                arms = [new EmlIntensionalRematchControlArmRow
                {
                    name = "1:law-candidate-shadow",
                    assayStatus = nameof(EmlRematchAssayStatuses.Invalid),
                    assayDetail = "canonical delta count drifted at arm=law-candidate-shadow",
                    powerStatus = nameof(EmlRematchPowerStatuses.Unpowered),
                    powerDetail = "assay invalid",
                }],
            };
            control.receiptDigest = control.ComputeDigest();
            Run run = Run.Create(root);
            WriteControl(run, Rung0ControlFile, control);
            DeepRematchRung0ControlFailureReceipt expected = CreateControlFailure(
                control, DigestFile(run.PathOf(Rung0ControlFile)));
            WriteControlFailure(run, expected);
            bool callbackFailed = false;
            try { throw new InvalidDataException(expected.Describe()); }
            catch (InvalidDataException) { callbackFailed = true; }
            bool survived = TryReadControlFailure(run.Dir, out DeepRematchRung0ControlFailureReceipt restored);
            return callbackFailed && survived
                && restored.ReceiptDigest == expected.ReceiptDigest
                && restored.InvalidArms.Count == 1
                && restored.InvalidArms[0].Name == "1:law-candidate-shadow"
                && restored.InvalidArms[0].Detail == control.Arms[0].assayDetail
                && restored.ControlReceiptPath == Rung0ControlFile
                && !restored.AuthorityEligible
                && restored.DiagnosticOnly;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void ValidateControlFailure(DeepRematchRung0ControlFailureReceipt failure)
    {
        if (failure.dialect != "DEEP-REMATCH-RUNG0-CONTROL-FAILURE-V2"
            || failure.receiptDigest != failure.ComputeDigest()
            || string.IsNullOrWhiteSpace(failure.runID)
            || !IsDigest(failure.checkpointDigest)
            || !IsDigest(failure.configDigest)
            || failure.controlReceiptPath != Rung0ControlFile
            || !IsDigest(failure.controlReceiptDigest)
            || !IsDigest(failure.controlReceiptFileDigest)
            || failure.reasonCode != Rung0ControlFailureCode
            || failure.diagnosticOnly != 1
            || failure.authorityEligible != 0
            || failure.invalidArms.Count == 0
            || failure.invalidArms.Any(static arm => string.IsNullOrWhiteSpace(arm.name) || string.IsNullOrWhiteSpace(arm.detail)))
            throw new InvalidDataException("deep-rematch rung0 control failure diagnostic is malformed or promotable");
    }

    private static string Digest(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool IsDigest(string value)
        => value.Length == 64 && value.All(static c => Uri.IsHexDigit(c));

    private static List<DeepRematchFuelAxis> BuildFuelAxes(EmlDeliberationCounts planned, EmlDeliberationCounts actual, EmlDeliberationCounts refund, string sourceDigest)
    {
        long[] p = [planned.CandidateEvaluations, planned.LogicalProgramPoints, planned.ExecutedProgramPoints, planned.InverseTransforms, planned.HashProbes, planned.JoinAttempts, planned.JoinHits, planned.ProcessTerms, planned.VerifierProgramPoints, planned.CandidateSupplyItems, planned.LawRewriteApplications, planned.LawRewriteTreeNodes];
        long[] a = [actual.CandidateEvaluations, actual.LogicalProgramPoints, actual.ExecutedProgramPoints, actual.InverseTransforms, actual.HashProbes, actual.JoinAttempts, actual.JoinHits, actual.ProcessTerms, actual.VerifierProgramPoints, actual.CandidateSupplyItems, actual.LawRewriteApplications, actual.LawRewriteTreeNodes];
        long[] r = [refund.CandidateEvaluations, refund.LogicalProgramPoints, refund.ExecutedProgramPoints, refund.InverseTransforms, refund.HashProbes, refund.JoinAttempts, refund.JoinHits, refund.ProcessTerms, refund.VerifierProgramPoints, refund.CandidateSupplyItems, refund.LawRewriteApplications, refund.LawRewriteTreeNodes];
        IReadOnlyList<string> names = EmlDeliberationCounts.AxisNames;
        List<DeepRematchFuelAxis> axes = new(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            if (p[i] < 0 || a[i] < 0 || r[i] < 0 || p[i] != a[i] + r[i])
                throw new InvalidDataException($"deep-rematch EML fuel axis {names[i]} does not close");
            axes.Add(new DeepRematchFuelAxis(names[i], p[i], a[i], r[i], "Known", "EmlSieve.DeliberationJournal.Settlements", sourceDigest));
        }
        return axes;
    }

    private static bool TryReadA3(Cortex cortex, string path, long preludeSteps, out PolicyBoundaryForkReceipt receipt, out long step)
    {
        receipt = default;
        step = -1;
        if (!cortex.TryReadHomeostatBoundaryReceipt(out receipt)) return false;
        receipt.Validate(HomeostatPolicyBoundaryDomain.Instance);
        DeepRematchGate.ValidatePolicyArms(in receipt);
        string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        string[] lines = File.ReadAllLines(path);
        long selected = long.MaxValue;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] fields = lines[i].Split('\t');
            if (fields.Length < 12 || fields[11] != digest || fields[9] != "1") continue;
            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long candidateStep)) continue;
            if (candidateStep <= preludeSteps && candidateStep < selected) selected = candidateStep;
        }
        if (selected == long.MaxValue) return false;
        step = selected;
        return true;
    }

    private static (string ComputeStatus, double DarkResidual, long Records, long PhysicalRecords, long ScoredRecords) ReadComputeReport(string path)
    {
        string status = "";
        double residual = double.NaN;
        long records = -1;
        long physicalRecords = -1;
        long scoredRecords = -1;
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 2) continue;
            if (fields[0] == "status") status = fields[1];
            else if (fields[0] == "residual_ms") residual = double.Parse(fields[1], CultureInfo.InvariantCulture);
            else if (fields[0] == "records") records = long.Parse(fields[1], CultureInfo.InvariantCulture);
            else if (fields[0] == "physical_records") physicalRecords = long.Parse(fields[1], CultureInfo.InvariantCulture);
            else if (fields[0] == "scored_records") scoredRecords = long.Parse(fields[1], CultureInfo.InvariantCulture);
        }
        if (!double.IsFinite(residual) || residual < 0 || records < 0) throw new InvalidDataException("deep-rematch compute report is malformed");
        if (physicalRecords < 0) physicalRecords = records;
        if (scoredRecords < 0) scoredRecords = physicalRecords;
        return (status, residual, records, physicalRecords, scoredRecords);
    }

    private static string CaptureRung0Digest(ReplayCalc dream) => dream.CaptureDeepRematchRung0Cursor().Digest;
    private static string DigestFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static string DigestSources(IEnumerable<string> paths, DeepRematchGate.DigestPass digests) => Hash(string.Join('|', paths.Select(path => Path.GetFileName(path) + ':' + digests.Digest(path))));
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

[RonObject]
internal partial class DeepRematchRung0ControlFailureArm
{
    public string name = "";
    public string detail = "";

    public string Name => name;
    public string Detail => detail;
}

[RonObject]
internal partial class DeepRematchRung0ControlFailureReceipt
{
    public string dialect = "DEEP-REMATCH-RUNG0-CONTROL-FAILURE-V2";
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public string controlReceiptPath = "";
    public string controlReceiptDigest = "";
    public string controlReceiptFileDigest = "";
    public string reasonCode = "";
    public int diagnosticOnly;
    public int authorityEligible;
    public List<DeepRematchRung0ControlFailureArm> invalidArms = [];

    public string ReceiptDigest => receiptDigest;
    public string RunID => runID;
    public string CheckpointDigest => checkpointDigest;
    public string ConfigDigest => configDigest;
    public string ControlReceiptPath => controlReceiptPath;
    public string ControlReceiptDigest => controlReceiptDigest;
    public string ControlReceiptFileDigest => controlReceiptFileDigest;
    public string ReasonCode => reasonCode;
    public bool DiagnosticOnly => diagnosticOnly == 1;
    public bool AuthorityEligible => authorityEligible == 1;
    public List<DeepRematchRung0ControlFailureArm> InvalidArms => invalidArms;

    internal string ComputeDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical())));

    internal string Canonical()
        => string.Join('|', dialect, runID, checkpointDigest, configDigest,
            controlReceiptPath, controlReceiptDigest, controlReceiptFileDigest, reasonCode, diagnosticOnly,
            authorityEligible, string.Join(';', invalidArms.Select(static arm => arm.name + ',' + arm.detail)),
            "rung0-control-failure-v2");

    internal string Describe()
        => $"deep-rematch rung0 control failure · code={reasonCode} · diagnostic-only=1 · "
            + $"control={controlReceiptPath} · control-digest={controlReceiptDigest} · "
            + string.Join("; ", invalidArms.Select(static arm => $"{arm.name}: {arm.detail}"));
}
