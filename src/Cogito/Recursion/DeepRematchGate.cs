namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cogito.Grammar;
using Ronmamon;

/// The seven pre-registered kill-lines for the dissolution rematch.  This is a
/// data contract, not an authority flag: verdicts are recomputed from typed
/// receipts and every byte that names a threshold is digest-bound.
internal static class DeepRematchGate
{
    internal const int SchemaVersion = 1;
    internal const int LineCount = 7;
    internal const int FuelAxisCount = 12;
    private const int PolicyBoundaryReceiptColumnCount = 16;
    private const int LegacyPolicyBoundaryArmEvidenceColumnCount = 41;
    private const int PolicyBoundaryArmEvidenceColumnCount = 42;
    internal const double DefaultBand = 0.25;
    internal const double DefaultResidualMultiplier = 2.0;
    internal const double BaselineResidual0098 = 0.0142;
    internal static readonly string[] VerdictNames =
    [
        "vocabulary frontier knee >63 at matched cumulative 12-axis fuel",
        "candidate at 0098 matched fuel; evaluator calls per certified capture below both historical baselines",
        "rung0 admission evaluator_zero=derived audits clean powered relation null zero authority",
        "policy readout no-worse than tree-era at matched spend with funded A3 and behaviorally executed divergent null",
        "all new checkpoint sections current dialect SaveLoadSave exact",
        "planned=actual+refund axes/funding journal and compute report PASS/no dark residual",
        "organism explicit pre-registered band vs0098",
    ];

    internal static DeepRematchGateConfig CreateDefault(
        DeepRematchArtifact baseline0071,
        DeepRematchArtifact baseline0098,
        string? gateID = null)
    {
        RequireArtifact(baseline0071, "cortex_0071");
        RequireArtifact(baseline0098, "cortex_0098");

        DeepRematchGateConfig config = new()
        {
            schemaVersion = SchemaVersion,
            gateID = string.IsNullOrWhiteSpace(gateID) ? "dissolution-deep-rematch-v1" : gateID,
            baseline0071ArtifactDigest = baseline0071.ArtifactDigest,
            baseline0098ArtifactDigest = baseline0098.ArtifactDigest,
            baseline0071RunID = baseline0071.RunID,
            baseline0098RunID = baseline0098.RunID,
            baseline0071Certificates = baseline0071.Certificates,
            baseline0071EvaluatorCalls = baseline0071.EvaluatorCalls,
            baseline0098Certificates = baseline0098.Certificates,
            baseline0098EvaluatorCalls = baseline0098.EvaluatorCalls,
            certificatesDenominator = "certificates",
            frontierKneeExclusive = 63,
            organismBand = DefaultBand,
            residualMultiplier = DefaultResidualMultiplier,
            computeDarkTolerance = 0,
            baseline0098Day = baseline0098.Day,
            baseline0098Replay = baseline0098.Replay,
            baseline0098ConsolidationPhase = baseline0098.ConsolidationPhase,
            baseline0098Residual = baseline0098.Residual,
            minimumPaidCloses = 1,
            maximumPaidCloseExecutionsRatio = 1.0,
            a3PreludeSteps = 1280,
            evaluationSteps = 500,
            matchedFuelAxes = baseline0098.FuelAxes.Count == FuelAxisCount
                ? [.. baseline0098.FuelAxes]
                : throw new InvalidDataException("deep-rematch registration requires the grounded twelve-axis cortex_0098 fuel receipt"),
        };
        config.configDigest = ComputeConfigDigest(config);
        ValidateConfig(config);
        return config;
    }

    internal static DeepRematchArtifact CreateFixtureArtifact(
        string runID,
        string registrationDigest,
        bool passing,
        bool bankedNull = false)
    {
        DeepRematchArtifact artifact = new()
        {
            schemaVersion = 2,
            runID = runID,
            registrationDigest = IsDigest(registrationDigest) ? registrationDigest : Hash(runID + "|" + registrationDigest),
            vocabularyKnee = passing ? 64 : 63,
            vocabularyFuelAxes = CreateDefaultFuelAxes(),
            evaluatorCalls = passing ? 30 : 180,
            certificates = 10,
            rung0ComposedPredictions = passing ? 1 : 0,
            rung0EvaluatorCalls = passing ? 0 : 1,
            rung0AuditFailures = passing ? 0 : 1,
            relationNullExecutions = passing ? 16 : 0,
            relationNullAuthorityPredictions = 0,
            rung0ReceiptDigest = "fixture-rung0",
            rung0AssayStatus = nameof(EmlRematchAssayStatuses.Exact),
            rung0AssayDetail = "exact",
            rung0ShadowPowerStatus = passing && !bankedNull ? nameof(EmlRematchPowerStatuses.Powered) : nameof(EmlRematchPowerStatuses.Unpowered),
            rung0ShadowPowerDetail = passing && !bankedNull ? "fixture powered" : "fixture unpowered",
            rung0NullPowerStatus = passing && !bankedNull ? nameof(EmlRematchPowerStatuses.Powered) : nameof(EmlRematchPowerStatuses.Unpowered),
            rung0NullPowerDetail = passing && !bankedNull ? "fixture powered" : "fixture unpowered",
            policyReadoutPaidCloses = passing ? 4 : 0,
            policyTreeEraPaidCloses = 4,
            policyReadoutSpend = 100,
            policyTreeEraSpend = 100,
            a3PaidArms = passing ? 4 : 3,
            a3HorizonShort = 16,
            a3HorizonMedium = 64,
            a3HorizonLong = 256,
            a3Spend = passing ? 302 : 0,
            a3ReceiptProvenanceDigest = "fixture-a3",
            checkpointReceiptDigest = "fixture-checkpoint-receipt",
            fundingReceiptDigest = "fixture-funding-receipt",
            policyReceiptDigest = "fixture-policy-receipt",
            trialPlannedSteps = 100,
            trialActualSteps = passing ? 100 : 99,
            trialRefundSteps = passing ? 0 : 1,
            readoutPlannedSteps = 100,
            readoutActualSteps = passing ? 100 : 99,
            readoutRefundSteps = passing ? 0 : 1,
            policyNullDivergentExecutions = passing ? 2 : 0,
            reflexControlAdaptations = 0,
            saveLoadSaveMismatches = passing ? 0 : 1,
            fuelAxes = CreateFuelAxes(passing),
            computeStatus = passing ? "PASS" : "FAIL",
            computeDarkResidual = passing ? 0 : 1,
            day = passing ? 254 : 500,
            dream = passing ? 176 : 500,
            aestivation = passing ? 70 : 500,
            residual = passing ? BaselineResidual0098 : 1,
            paidCloses = passing ? 4 : 0,
            executions = passing ? 8 : 0,
            bankedNull = bankedNull,
            a3ReceiptStep = 1280,
            evaluationTopology = "monolithic-handshake",
            evaluationStartStep = 1281,
            evaluationEndStep = 1780,
            checkpointDigest = Hash(runID + "|fixture-checkpoint"),
            evaluationRows = 500,
            evaluationCurveDigest = Hash(runID + "|fixture-curve"),
            evaluationComputeDigest = Hash(runID + "|fixture-compute"),
            computeReportDigest = Hash(runID + "|fixture-compute-report"),
            computeReportRecords = 1781,
            collectorProvenanceDigest = "",
            sourceDigests = [new DeepRematchSourceReceipt { path = "fixture", digest = Hash(runID + "|fixture-source") }],
        };
        artifact.collectorProvenanceDigest = ComputeCollectorProvenanceDigest(artifact);
        artifact.artifactDigest = ComputeArtifactDigest(artifact);
        return artifact;
    }

    internal static DeepRematchGateConfig DecodeConfig(ReadOnlySpan<byte> bytes)
    {
        DeepRematchGateConfig document = RonSerializer.Deserialize<DeepRematchGateConfig>(bytes);
        ValidateConfig(document);
        return document;
    }

    internal static byte[] EncodeConfig(DeepRematchGateConfig config)
    {
        ValidateConfig(config);
        byte[] first = RonSerializer.SerializeToUtf8(in config);
        DeepRematchGateConfig restored = DecodeConfig(first);
        byte[] second = RonSerializer.SerializeToUtf8(in restored);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("deep-rematch config SaveLoadSave drifted");
        return first;
    }

    internal static DeepRematchArtifact DecodeArtifact(ReadOnlySpan<byte> bytes)
    {
        DeepRematchArtifact artifact = RonSerializer.Deserialize<DeepRematchArtifact>(bytes);
        bool legacyIdentity = IsHistoricalV1Identity(artifact);
        bool serializedV2Fields = HasSerializedV2OnlyField(bytes);
        if (legacyIdentity && serializedV2Fields)
            throw new InvalidDataException("deep-rematch schema 1 historical artifact mixes v2 assay/power fields");
        if (artifact.SchemaVersion == 1 && artifact.HistoricalBaseline && !legacyIdentity)
            throw new InvalidDataException("deep-rematch schema 1 historical artifact identity is not the exact v1 shape");
        ValidateArtifact(artifact);
        return artifact;
    }

    internal static byte[] EncodeArtifact(DeepRematchArtifact artifact)
    {
        if (IsLegacyV1Artifact(artifact))
            throw new InvalidDataException("deep-rematch schema 1 historical artifacts are read-only");
        ValidateArtifact(artifact);
        byte[] first = RonSerializer.SerializeToUtf8(in artifact);
        DeepRematchArtifact restored = DecodeArtifact(first);
        byte[] second = RonSerializer.SerializeToUtf8(in restored);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("deep-rematch artifact SaveLoadSave drifted");
        return first;
    }

    internal static DeepRematchGateReport Adjudicate(
        DeepRematchGateConfig config,
        DeepRematchArtifact baseline0071,
        DeepRematchArtifact baseline0098,
        DeepRematchArtifact run)
    {
        ValidateConfig(config);
        RequireArtifact(baseline0071, config.baseline0071RunID);
        RequireArtifact(baseline0098, config.baseline0098RunID);
        RequireArtifact(run, run.runID);
        if (!string.Equals(config.baseline0071ArtifactDigest, baseline0071.ArtifactDigest, StringComparison.Ordinal)
            || !string.Equals(config.baseline0098ArtifactDigest, baseline0098.ArtifactDigest, StringComparison.Ordinal))
            throw new InvalidDataException("baseline artifact digest does not match the pre-registration");
        if (config.Baseline0071Certificates != baseline0071.Certificates
            || config.Baseline0071EvaluatorCalls != baseline0071.EvaluatorCalls
            || config.Baseline0098Certificates != baseline0098.Certificates
            || config.Baseline0098EvaluatorCalls != baseline0098.EvaluatorCalls)
            throw new InvalidDataException("baseline metric changed after pre-registration");
        if (!SameAxes(config.MatchedFuelAxes, baseline0098.FuelAxes))
            throw new InvalidDataException("matched fuel axes changed after pre-registration");
        if (!string.Equals(run.RegistrationDigest, config.ConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException("run artifact is not bound to the pre-registered config digest");
        if (run.FuelAxes.Count != FuelAxisCount || config.MatchedFuelAxes.Count != FuelAxisCount)
            throw new InvalidDataException("deep-rematch gate requires exactly twelve fuel axes");

        List<DeepRematchVerdictRecord> verdicts = new(LineCount);
        List<DeepRematchBankedNullRecord> banked = [];
        Evaluate(verdicts, banked, 1, VerdictNames[0],
            run.VocabularyKnee > config.FrontierKneeExclusive && SameAxes(run.VocabularyFuelAxes, config.MatchedFuelAxes),
            run.ArtifactDigest, "knee=" + run.VocabularyKnee.ToString(CultureInfo.InvariantCulture));

        double currentRate = Rate(run.EvaluatorCalls, run.Certificates);
        double oldRate0071 = Rate(baseline0071.EvaluatorCalls, baseline0071.Certificates);
        double oldRate0098 = Rate(baseline0098.EvaluatorCalls, baseline0098.Certificates);
        Evaluate(verdicts, banked, 2, VerdictNames[1],
            run.Certificates > 0 && SameAxes(run.FuelAxes, config.MatchedFuelAxes) && currentRate < oldRate0071 && currentRate < oldRate0098,
            run.ArtifactDigest,
            $"denominator={config.CertificatesDenominator} rate={currentRate:G17} baseline0071={oldRate0071:G17} baseline0098={oldRate0098:G17}");

        bool rung0 = run.Rung0ComposedPredictions >= 1
            && run.Rung0EvaluatorCalls == 0
            && run.Rung0AuditFailures == 0
            && run.RelationNullExecutions > 0
            && run.RelationNullAuthorityPredictions == 0
            && run.Rung0AssayStatus == nameof(EmlRematchAssayStatuses.Exact)
            && run.Rung0ShadowPowerStatus == nameof(EmlRematchPowerStatuses.Powered)
            && run.Rung0NullPowerStatus == nameof(EmlRematchPowerStatuses.Powered);
        Evaluate(verdicts, banked, 3, VerdictNames[2],
            rung0, run.ArtifactDigest,
            $"assay={run.Rung0AssayStatus}:{run.Rung0AssayDetail} shadow-power={run.Rung0ShadowPowerStatus}:{run.Rung0ShadowPowerDetail} null-power={run.Rung0NullPowerStatus}:{run.Rung0NullPowerDetail} derived={run.Rung0ComposedPredictions} evaluator={run.Rung0EvaluatorCalls} audits={run.Rung0AuditFailures} null-executions={run.RelationNullExecutions} null-authority={run.RelationNullAuthorityPredictions}");

        bool decider = run.PolicyReadoutSpend == run.PolicyTreeEraSpend
            && run.PolicyReadoutPaidCloses >= run.PolicyTreeEraPaidCloses
            && run.A3PaidArms == 4
            && run.A3HorizonShort == 16 && run.A3HorizonMedium == 64 && run.A3HorizonLong == 256
            && run.A3Spend > 0
            && !string.IsNullOrWhiteSpace(run.A3ReceiptProvenanceDigest)
            && run.PolicyNullDivergentExecutions > 0
            && run.ReflexControlAdaptations == 0;
        decider = decider && run.A3ReceiptStep <= config.A3PreludeSteps
            && ((run.EvaluationTopology == "composite-local"
                    && run.EvaluationStartStep == 1
                    && run.EvaluationEndStep == config.EvaluationSteps)
                || (run.EvaluationTopology == "monolithic-handshake"
                    && run.EvaluationStartStep == config.A3PreludeSteps + 1
                    && run.EvaluationEndStep == config.A3PreludeSteps + config.EvaluationSteps))
            && run.EvaluationRows == config.EvaluationSteps;
        Evaluate(verdicts, banked, 4, VerdictNames[3],
            decider, run.ArtifactDigest,
            $"readout={run.PolicyReadoutPaidCloses} tree={run.PolicyTreeEraPaidCloses} spend={run.PolicyReadoutSpend}/{run.PolicyTreeEraSpend} a3={run.A3PaidArms} null-divergent={run.PolicyNullDivergentExecutions} reflex-adapt={run.ReflexControlAdaptations}");

        Evaluate(verdicts, banked, 5, VerdictNames[4],
            run.SaveLoadSaveMismatches == 0, run.ArtifactDigest, $"mismatches={run.SaveLoadSaveMismatches}");

        bool accounting = run.FuelAxes.All(static axis => axis.Planned == axis.Actual + axis.Refund)
            && string.Equals(run.ComputeStatus, "PASS", StringComparison.Ordinal)
            && run.ComputeDarkResidual <= config.ComputeDarkTolerance
            && run.TrialPlannedSteps == run.TrialActualSteps + run.TrialRefundSteps
            && run.ReadoutPlannedSteps == run.ReadoutActualSteps + run.ReadoutRefundSteps;
        Evaluate(verdicts, banked, 6, VerdictNames[5],
            accounting, run.ArtifactDigest, $"axes={run.FuelAxes.Count} compute={run.ComputeStatus} dark={run.ComputeDarkResidual:G17}");

        bool organism = InBand(run.Day, config.Baseline0098Day, config.OrganismBand)
            && InBand(run.Replay, config.Baseline0098Replay, config.OrganismBand)
            && InBand(run.ConsolidationPhase, config.Baseline0098ConsolidationPhase, config.OrganismBand)
            && run.Residual <= config.Baseline0098Residual * config.ResidualMultiplier
            && run.PaidCloses >= config.MinimumPaidCloses
            && run.Executions > 0 && run.PaidCloses <= run.Executions * config.MaximumPaidCloseExecutionsRatio;
        Evaluate(verdicts, banked, 7, VerdictNames[6],
            organism, run.ArtifactDigest,
            $"day={run.Day} dream={run.Replay} aestivation={run.ConsolidationPhase} residual={run.Residual:G17} paid={run.PaidCloses}/{run.Executions}");

        DeepRematchGateReport report = new()
        {
            schemaVersion = SchemaVersion,
            gateID = config.GateID,
            configDigest = config.ConfigDigest,
            runID = run.RunID,
            runArtifactDigest = run.ArtifactDigest,
            verdicts = verdicts,
            bankedNulls = banked,
        };
        report.reportDigest = ComputeReportDigest(report);
        return report;
    }

    internal static byte[] EncodeReport(DeepRematchGateReport report)
    {
        ValidateReport(report);
        byte[] first = RonSerializer.SerializeToUtf8(in report);
        DeepRematchGateReport restored = DecodeReport(first);
        byte[] second = RonSerializer.SerializeToUtf8(in restored);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("deep-rematch report SaveLoadSave drifted");
        return first;
    }

    internal static DeepRematchGateReport DecodeReport(ReadOnlySpan<byte> bytes)
    {
        DeepRematchGateReport report = RonSerializer.Deserialize<DeepRematchGateReport>(bytes);
        ValidateReport(report);
        return report;
    }

    internal static string RenderTsv(DeepRematchGateReport report)
    {
        ValidateReport(report);
        StringBuilder text = new("line\tname\tstatus\tevidence_digest\tdetail\n");
        foreach (DeepRematchVerdictRecord verdict in report.Verdicts)
            text.Append(verdict.Line).Append('\t').Append(verdict.Name).Append('\t').Append(verdict.Status).Append('\t').Append(verdict.EvidenceDigest).Append('\t').Append(verdict.Detail).AppendLine();
        foreach (DeepRematchBankedNullRecord banked in report.BankedNulls)
            text.Append("banked-null\t").Append(banked.Name).Append("\tBANKED_NULL\t").Append(banked.ReceiptDigest).Append('\t').Append(banked.Detail).AppendLine();
        return text.ToString();
    }

    internal static void WritePrepared(string outputPath, DeepRematchGateConfig config)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outputPath, EncodeConfig(config));
    }

    internal static void WriteArtifact(string outputPath, DeepRematchArtifact artifact)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outputPath, EncodeArtifact(artifact));
    }

    /// Build the adjudicator input from a landed run directory.  Scalar fields
    /// are derived from the run's curve/receipt files; missing authority files
    /// fail closed instead of allowing a caller to invent a passing summary.
    internal static DeepRematchArtifact CollectRun(string runDirectory, string? expectedRunID = null, long windowStart = 1281, long windowSteps = 500, bool baselineOnly = false)
    {
        if (!Directory.Exists(runDirectory)) throw new DirectoryNotFoundException(runDirectory);
        string checkpoint = RequireRunFile(runDirectory, "checkpoint.bin");
        string curve = RequireRunFile(runDirectory, "curve.tsv");
        string compute = RequireRunFile(runDirectory, "compute.tsv");
        string rhythm = RequireRunFile(runDirectory, "rhythm.txt");
        string runID = Run.RunIDFromDirectory(runDirectory);
        if (expectedRunID is not null && !string.Equals(expectedRunID, runID, StringComparison.Ordinal))
            throw new InvalidDataException($"deep-rematch run identity mismatch: expected {expectedRunID}, got {runID}");
        DigestPass digests = new();
        string checkpointFileDigest;
        string checkpointMagic;
        using (FileStream checkpointStream = File.OpenRead(checkpoint))
        {
            Span<byte> magic = stackalloc byte[8];
            checkpointStream.ReadExactly(magic);
            checkpointMagic = Encoding.ASCII.GetString(magic);
            checkpointStream.Position = 0;
            checkpointFileDigest = Convert.ToHexStringLower(SHA256.HashData(checkpointStream));
        }
        digests.Seed(checkpoint, checkpointFileDigest);
        if (baselineOnly)
        {
            if (runID == "cortex_0071" && checkpointMagic != "CORTEX4\n"
                || runID == "cortex_0098" && checkpointMagic != "CORTEX9\n")
                throw new InvalidDataException($"historical baseline {runID} checkpoint dialect is {checkpointMagic.Trim()}, expected its registered historical dialect");
            return CollectBaseline(runDirectory, runID, checkpoint, checkpointFileDigest, Hash("historical-baseline|" + runID + "|" + checkpointFileDigest), curve, compute, rhythm);
        }
        string anytimeCurve = RequireRunFile(runDirectory, "eml_anytime_curve.tsv");
        string currentDialectMagic = Checkpoint.CurrentDialect + "\n";
        if (checkpointMagic != currentDialectMagic)
            throw new InvalidDataException(checkpointMagic == "CORTEXG\n"
                ? $"deep-rematch candidate rejects retired CORTEXG checkpoint; current gate dialect is {Checkpoint.CurrentDialect}"
                : checkpointMagic == "CORTEXH\n"
                ? $"deep-rematch candidate rejects retired CORTEXH checkpoint; current gate dialect is {Checkpoint.CurrentDialect}"
                : checkpointMagic == "CORTEXI\n"
                ? $"deep-rematch candidate rejects retired CORTEXI checkpoint; current gate dialect is {Checkpoint.CurrentDialect}"
                : $"deep-rematch candidate requires current {Checkpoint.CurrentDialect} checkpoint, got {checkpointMagic.Trim()}");
        CortexRunConfig checkpointConfig = Checkpoint.PeekConfig(runDirectory);
        string gatePath = RequireRunFile(runDirectory, "deep-rematch-gate.ron");
        string gateDigestPath = RequireRunFile(runDirectory, "deep-rematch-gate.digest");
        DeepRematchGateConfig gate = DecodeConfig(File.ReadAllBytes(gatePath));
        string gateDigest = File.ReadAllText(gateDigestPath).Trim();
        if (!IsDigest(gateDigest)
            || !string.Equals(gateDigest, gate.ConfigDigest, StringComparison.Ordinal)
            || !string.Equals(checkpointConfig.DeepRematchGateDigest, gate.ConfigDigest, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(checkpointConfig.DeepRematchGatePath))
            throw new InvalidDataException("deep-rematch run checkpoint/gate registration is not digest-bound");
        string computeReport = RequireRunFile(runDirectory, "compute.report.tsv");
        VerifiedDeepRematchReceipts authority = ReadVerifiedReceipts(runDirectory, runID, gate, digests);
        DeepRematchA3Receipt a3 = authority.A3;
        DeepRematchRung0Receipt rung0 = authority.Rung0;
        DeepRematchCheckpointReceipt save = authority.Checkpoint;
        DeepRematchFundingReceipt funding = authority.Funding;
        DeepRematchPolicyReceipt policy = authority.Policy;
        CortexPolicyTrialJournalOccurrenceCheck trial = authority.Trial;
        CortexPolicyTrialJournalOccurrenceCheck readout = authority.Readout;
        string reportText = File.ReadAllText(computeReport);
        ValidateComputeReport(reportText);
        long computeReportRecords = ParseReportLong(reportText, "records");
        long computeRows = CountStepRows(compute);
        long expectedFinalStep = checked(gate.A3PreludeSteps + gate.EvaluationSteps + 1);
        if (computeReportRecords != computeRows || computeRows != expectedFinalStep)
            throw new InvalidDataException($"deep-rematch compute report records {computeReportRecords} do not equal the complete compute row count {computeRows}");

        if (windowStart != gate.A3PreludeSteps + 1 || windowSteps != gate.EvaluationSteps) throw new InvalidDataException($"deep-rematch scored evaluation window is fixed at {gate.A3PreludeSteps + 1}..{gate.A3PreludeSteps + gate.EvaluationSteps}");
        WindowReceipt curveWindow = ReadWindow(curve, windowStart, windowSteps);
        WindowReceipt computeWindow = ReadWindow(compute, windowStart, windowSteps);
        Dictionary<string, string> final = curveWindow.Final;
        (long day, long dream, long aestivation) = ReadRhythmMetrics(rhythm);
        List<DeepRematchFuelAxis> axes = ParseFuelAxes(funding);
        VerifyFuelAxesAgainstCurve(anytimeCurve, axes);
        DeepRematchSourceReceipt[] sources = [.. authority.SourcePaths.Concat([curve, compute, rhythm, anytimeCurve])
            .Select(path => new DeepRematchSourceReceipt { path = Path.GetFileName(path), digest = digests.Digest(path) })];
        DeepRematchArtifact artifact = new()
        {
            schemaVersion = 2,
            runID = runID,
            registrationDigest = gate.ConfigDigest,
            checkpointDigest = checkpointFileDigest,
            vocabularyKnee = ParseInt(final, "eml.census.exact"),
            vocabularyFuelAxes = axes,
            evaluatorCalls = ParseLong(final, "eml.evaluator.calls"),
            certificates = ParseLong(final, "eml.census.certs"),
            rung0ComposedPredictions = rung0.ComposedPredictions,
            rung0EvaluatorCalls = rung0.EvaluatorCalls,
            rung0AuditFailures = rung0.AuditFailures,
            relationNullExecutions = rung0.NullExecutions,
            relationNullAuthorityPredictions = rung0.NullAuthorityPredictions,
            rung0ReceiptDigest = rung0.ReadDigest(),
            rung0AssayStatus = rung0.AssayStatus,
            rung0AssayDetail = rung0.AssayDetail,
            rung0ShadowPowerStatus = rung0.ShadowPowerStatus,
            rung0ShadowPowerDetail = rung0.ShadowPowerDetail,
            rung0NullPowerStatus = rung0.NullPowerStatus,
            rung0NullPowerDetail = rung0.NullPowerDetail,
            policyReadoutPaidCloses = policy.ReadoutPaidCloses,
            policyTreeEraPaidCloses = policy.TreeEraPaidCloses,
            policyReadoutSpend = policy.ReadoutSpend,
            policyTreeEraSpend = policy.TreeEraSpend,
            a3PaidArms = a3.PaidArms,
            a3HorizonShort = a3.HorizonShort,
            a3HorizonMedium = a3.HorizonMedium,
            a3HorizonLong = a3.HorizonLong,
            a3Spend = a3.Spend,
            a3ReceiptProvenanceDigest = a3.ProvenanceDigest,
            checkpointReceiptDigest = save.ReadDigest(),
            fundingReceiptDigest = funding.ReadDigest(),
            policyReceiptDigest = policy.ReadDigest(),
            trialPlannedSteps = trial.PlannedArmSteps,
            trialActualSteps = trial.ActualArmSteps,
            trialRefundSteps = trial.ReclaimedOrUnused,
            readoutPlannedSteps = readout.PlannedArmSteps,
            readoutActualSteps = readout.ActualArmSteps,
            readoutRefundSteps = readout.ReclaimedOrUnused,
            policyNullDivergentExecutions = policy.NullDivergentExecutions,
            reflexControlAdaptations = policy.ReflexControlAdaptations,
            saveLoadSaveMismatches = save.Mismatches,
            fuelAxes = axes,
            computeStatus = funding.ComputeStatus,
            computeDarkResidual = funding.ComputeDarkResidual,
            day = day,
            dream = dream,
            aestivation = aestivation,
            residual = ParseDouble(final, "eml.frontier.residual"),
            paidCloses = ParseLong(final, "cortex.homeostat.paid_takeovers"),
            executions = ParseLong(final, "cortex.homeostat.takeover_executions"),
            a3ReceiptStep = a3.ReceiptStep,
            evaluationTopology = "monolithic-handshake",
            evaluationStartStep = funding.EvaluationStartStep,
            evaluationEndStep = funding.EvaluationEndStep,
            evaluationRows = curveWindow.Rows,
            evaluationCurveDigest = curveWindow.Digest,
            evaluationComputeDigest = computeWindow.Digest,
            computeReportDigest = digests.Digest(computeReport),
            computeReportRecords = computeReportRecords,
            sourceDigests = [.. sources],
        };
        artifact.collectorProvenanceDigest = ComputeCollectorProvenanceDigest(artifact);
        artifact.artifactDigest = ComputeArtifactDigest(artifact);
        ValidateArtifact(artifact);
        return artifact;
    }

    /// Collect a split deep-rematch run. The parent is only the immutable S0
    /// setup; all cumulative metrics come from the evaluation child and all
    /// policy/A3 authority comes from calibration. The manifest is the final
    /// completion marker and every referenced byte is re-read and re-digested.
    internal static DeepRematchArtifact CollectComposite(string parentDirectory)
    {
        if (!Directory.Exists(parentDirectory)) throw new DirectoryNotFoundException(parentDirectory);
        string parent = Path.GetFullPath(parentDirectory);
        string manifestPath = Path.Combine(parent, "deep-rematch.composite.ron");
        DeepRematchCompositeRecord manifest = ReadCanonicalDocument<DeepRematchCompositeRecord>(manifestPath);
        if (manifest.schemaVersion != DeepRematchCompositeRON.CompositeRecordSchemaVersion)
            throw new InvalidDataException($"composite manifest requires schema-v{DeepRematchCompositeRON.CompositeRecordSchemaVersion}");
        if (manifest.parentRecordPath != "deep-rematch.parent.ron"
            || manifest.coldSeedRecordPath != "deep-rematch.cold-seed.ron"
            || (manifest.accountingRecordPath != "deep-rematch.accounting.ron"
                && !manifest.accountingRecordPath.StartsWith("attempts/", StringComparison.Ordinal)))
            throw new InvalidDataException("composite manifest has non-canonical parent paths");

        string parentRecordPath = ResolveCompositePath(parent, manifest.parentRecordPath, "parent record");
        string coldRecordPath = ResolveCompositePath(parent, manifest.coldSeedRecordPath, "cold seed record");
        string accountingRecordPath = ResolveCompositePath(parent, manifest.accountingRecordPath, "accounting record");
        bool settlementCalibration = !string.IsNullOrWhiteSpace(manifest.calibrationAuthorityPath);
        string calibrationAuthorityPath = settlementCalibration
            ? ResolveCompositePath(parent, manifest.calibrationAuthorityPath, "calibration authority")
            : "";
        string calibrationCopyPath = settlementCalibration ? "" : ResolveCompositePath(parent, manifest.calibrationChildCopyRecordPath, "calibration child copy");
        string calibrationRecordPath = settlementCalibration ? "" : ResolveCompositePath(parent, manifest.calibrationRecordPath, "calibration record");
        string evaluationCopyPath = ResolveCompositePath(parent, manifest.evaluationChildCopyRecordPath, "evaluation child copy");
        string evaluationRecordPath = ResolveCompositePath(parent, manifest.evaluationRecordPath, "evaluation record");
        ValidateCompositeBinding(parentRecordPath, manifest.parentRecordDigest, "parent record");
        ValidateCompositeBinding(coldRecordPath, manifest.coldSeedRecordDigest, "cold seed record");
        if (settlementCalibration)
            ValidateCompositeBinding(calibrationAuthorityPath, manifest.calibrationAuthorityDigest, "calibration authority");
        else
        {
            ValidateCompositeBinding(calibrationCopyPath, manifest.calibrationChildCopyRecordDigest, "calibration child copy");
            ValidateCompositeBinding(calibrationRecordPath, manifest.calibrationRecordDigest, "calibration record");
        }
        ValidateCompositeBinding(evaluationCopyPath, manifest.evaluationChildCopyRecordDigest, "evaluation child copy");
        ValidateCompositeBinding(evaluationRecordPath, manifest.evaluationRecordDigest, "evaluation record");
        ValidateCompositeBinding(accountingRecordPath, manifest.accountingDigest, "accounting record");

        DeepRematchParentRecord parentRecord = ReadCanonicalDocument<DeepRematchParentRecord>(parentRecordPath);
        DeepRematchColdSeedRecord coldRecord = ReadCanonicalDocument<DeepRematchColdSeedRecord>(coldRecordPath);
        DeepRematchChildCopyRecord calibrationCopy = new();
        DeepRematchCalibrationRecord calibration = new();
        DeepRematchCalibrationAuthority? calibrationAuthority = null;
        string calibrationChildID;
        string calibrationDirectory;
        if (settlementCalibration)
        {
            DeepRematchCallbackSettlementRecord settlement = ReadCanonicalDocument<DeepRematchCallbackSettlementRecord>(calibrationAuthorityPath);
            calibrationChildID = settlement.childRunID;
            calibrationDirectory = Path.Combine(parent, "children", calibrationChildID);
            calibrationAuthority = DeepRematchCompositeRON.LoadCalibrationAuthority(Run.Open(parent), calibrationDirectory, manifest.calibrationAuthorityPath);
            calibrationAuthority.Validate();
        }
        else
        {
            calibrationCopy = ReadCanonicalDocument<DeepRematchChildCopyRecord>(calibrationCopyPath);
            calibration = ReadCanonicalDocument<DeepRematchCalibrationRecord>(calibrationRecordPath);
            calibrationChildID = calibrationCopy.childRunID;
            calibrationDirectory = Path.GetFullPath(calibrationCopy.childRunDirectory);
        }
        DeepRematchChildCopyRecord evaluationCopy = ReadCanonicalDocument<DeepRematchChildCopyRecord>(evaluationCopyPath);
        DeepRematchEvaluationRecord evaluation = ReadCanonicalDocument<DeepRematchEvaluationRecord>(evaluationRecordPath);
        DeepRematchAccountingRecord accounting = ReadCanonicalDocument<DeepRematchAccountingRecord>(accountingRecordPath);
        if (!settlementCalibration)
        {
            RequireCompositeRolePath(parent, calibrationCopyPath, calibrationChildID, "deep-rematch.child-copy.ron", "calibration child copy");
            RequireCompositeRolePath(parent, calibrationRecordPath, calibrationChildID, "deep-rematch.calibration.ron", "calibration record");
        }
        RequireCompositeRolePath(parent, evaluationCopyPath, evaluationCopy.childRunID, "deep-rematch.child-copy.ron", "evaluation child copy");
        RequireCompositeRolePath(parent, evaluationRecordPath, evaluationCopy.childRunID, "deep-rematch.evaluation.ron", "evaluation record");
        if (manifest.parentRunID != parentRecord.runID || manifest.coldSeedDigest != coldRecord.coldSeedDigest
            || manifest.persistedConfigDigest != coldRecord.persistedConfigDigest)
            throw new InvalidDataException("composite manifest identity disagrees with typed parent/cold records");
        parentRecord.Validate(parent);
        coldRecord.Validate(parentRecord);
        if (!settlementCalibration)
            calibrationCopy.Validate(parentRecord, coldRecord, CortexForkRailRoles.Calibration);
        evaluationCopy.Validate(parentRecord, coldRecord, CortexForkRailRoles.Evaluation);
        if (string.Equals(calibrationChildID, evaluationCopy.childRunID, StringComparison.Ordinal)
            || string.Equals(Path.GetFullPath(calibrationDirectory), Path.GetFullPath(evaluationCopy.childRunDirectory), StringComparison.Ordinal))
            throw new InvalidDataException("composite children must be immediate and distinct");
        if (!settlementCalibration)
            calibration.Validate(parentRecord, coldRecord, calibrationCopy,
                DeepRematchCompositeRON.ReadCalibrationPhaseWallMilliseconds(parent));
        evaluation.Validate(parentRecord, coldRecord, evaluationCopy,
            calibrationAuthority ?? DeepRematchCalibrationAuthority.FromStandard(parentRecord, coldRecord, calibrationCopy, calibration));
        accounting.Validate(manifest.measuredWallMilliseconds);
        manifest.ValidatePersisted(parent, parentRecord, coldRecord, calibrationCopy, calibration, evaluationCopy, evaluation, accounting, calibrationAuthority);
        if (manifest.measuredWallMilliseconds < 0 || manifest.measuredWallMilliseconds != accounting.measuredCompositeWallMilliseconds
            || accounting.unaccountedWallMilliseconds != 0)
            throw new InvalidDataException("composite accounting is not exact");

        calibrationDirectory = Path.GetFullPath(calibrationDirectory);
        string evaluationDirectory = Path.GetFullPath(evaluationCopy.childRunDirectory);
        if (string.IsNullOrWhiteSpace(parentRecord.gatePath))
            throw new InvalidDataException("composite parent omits its registered gate authority");
        DeepRematchGateConfig gate = DecodeConfig(File.ReadAllBytes(parentRecord.gatePath));
        DigestPass digests = new();
        VerifiedDeepRematchReceipts authority = ReadVerifiedReceipts(calibrationDirectory, calibrationChildID, gate, digests);
        DeepRematchA3Receipt a3 = authority.A3;
        DeepRematchRung0Receipt rung0 = authority.Rung0;
        DeepRematchCheckpointReceipt checkpoint = authority.Checkpoint;
        DeepRematchFundingReceipt funding = authority.Funding;
        DeepRematchPolicyReceipt policy = authority.Policy;
        if (rung0.evaluatorCalls != 0 || rung0.auditFailures != 0 || rung0.nullAuthorityPredictions != 0
            || funding.ComputeStatus != "PASS" || policy.ReflexControlAdaptations != 0)
            throw new InvalidDataException("calibration child authority is incomplete");

        string evaluationCheckpoint = digests.Digest(Path.Combine(evaluationDirectory, Checkpoint.FileName));
        string evalCurve = RequireCompositeChildFile(evaluationDirectory, "curve.tsv");
        string evalCompute = RequireCompositeChildFile(evaluationDirectory, "compute.tsv");
        string evalReport = RequireCompositeChildFile(evaluationDirectory, "compute.report.tsv");
        string evalRhythm = RequireCompositeChildFile(evaluationDirectory, "rhythm.txt");
        string evalAnytime = RequireCompositeChildFile(evaluationDirectory, "eml_anytime_curve.tsv");
        string evalTape = RequireCompositeChildFile(evaluationDirectory, "tape.spanlog");
        string evalFuelCursor = RequireCompositeChildFile(evaluationDirectory, ReplayCalc.DeepRematchFuelCursorSidecarFile);
        if (evaluation.seedStartStep != 0 || evaluation.actualNextStep != evaluation.plannedNextStep
            || evaluation.plannedNextStep != evaluationCopy.endStep || evaluation.plannedNextStep < 2
            || evaluation.mount.MountStep != 0 || evaluation.mount.Relation != PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake
            || evaluation.mount.EvaluationStartStep != 1 || evaluation.mount.EvaluationEndStep != evaluation.plannedNextStep - 1
            || evaluation.calibrationRuntimeStateCopied || !evaluation.terminalCheckpointExact
            || evaluation.anytime is null)
            throw new InvalidDataException("evaluation child is not a local post-handshake mount");
        int physicalRows = checked((int)CountStepRows(evalCurve));
        if (physicalRows != evaluation.plannedNextStep || CountStepRows(evalCompute) != physicalRows)
            throw new InvalidDataException("evaluation physical curve/compute rows do not equal the physical horizon");
        WindowReceipt evaluationWindow = ReadWindow(evalCurve, 1, evaluation.plannedNextStep - 1);
        WindowReceipt evaluationCompute = ReadWindow(evalCompute, 1, evaluation.plannedNextStep - 1);
        (long day, long dream, long aestivation) = ReadRhythmMetrics(evalRhythm);
        ValidateComputeReport(File.ReadAllText(evalReport));
        string computeStatus = "";
        double darkResidual = double.NaN;
        long computeRecords = -1;
        long physicalComputeRecords = -1;
        long scoredComputeRecords = -1;
        foreach (string line in File.ReadLines(evalReport))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 2) continue;
            if (fields[0] == "status") computeStatus = fields[1];
            else if (fields[0] == "residual_ms") darkResidual = double.Parse(fields[1], CultureInfo.InvariantCulture);
            else if (fields[0] == "records") computeRecords = long.Parse(fields[1], CultureInfo.InvariantCulture);
            else if (fields[0] == "physical_records") physicalComputeRecords = long.Parse(fields[1], CultureInfo.InvariantCulture);
            else if (fields[0] == "scored_records") scoredComputeRecords = long.Parse(fields[1], CultureInfo.InvariantCulture);
        }
        if (!double.IsFinite(darkResidual) || darkResidual < 0 || computeRecords < 0
            || physicalComputeRecords < 0 || scoredComputeRecords < 0 || computeStatus != "PASS")
            throw new InvalidDataException("evaluation compute report is incomplete");
        if (!double.IsFinite(funding.ComputeDarkResidual) || funding.ComputeDarkResidual < 0)
            throw new InvalidDataException("calibration compute report has an invalid dark residual");
        (List<DeepRematchFuelAxis> evalAxes, EmlAnytimeEvaluationPrefix anytimeAuthority)
            = ParseEvaluationFuel(evaluation, evalAnytime);
        VerifyFuelAxesAgainstCurve(evalAnytime, evalAxes, anytimeAuthority.Digest);
        if (computeRecords != CountStepRows(evalCompute)
            || physicalComputeRecords != physicalRows
            || scoredComputeRecords != evaluationWindow.Rows)
            throw new InvalidDataException("evaluation compute report counters do not bind the physical/scored compute topology");
        if (anytimeAuthority.AcceptedPoint.Quality.ExactClasses <= 63)
            throw new InvalidDataException("evaluation anytime prefix never crossed the registered vocabulary knee");
        string configDigest = gate.ConfigDigest;
        List<string> sourcePaths = [manifestPath, parentRecordPath, coldRecordPath];
        if (settlementCalibration)
            sourcePaths.Add(calibrationAuthorityPath);
        else
        {
            sourcePaths.Add(calibrationCopyPath);
            sourcePaths.Add(calibrationRecordPath);
        }
        sourcePaths.AddRange([
            evaluationCopyPath,
            evaluationRecordPath,
            accountingRecordPath,
            Path.Combine(evaluationDirectory, Checkpoint.FileName), evalCurve, evalCompute, evalRhythm, evalAnytime, evalReport, evalTape, evalFuelCursor,
        ]);
        string evaluationCheckpointDelta = Path.Combine(evaluationDirectory, Checkpoint.DeltaFileName);
        if (File.Exists(evaluationCheckpointDelta)) sourcePaths.Add(evaluationCheckpointDelta);
        sourcePaths.AddRange(authority.SourcePaths);
        DeepRematchArtifact artifact = new()
        {
            schemaVersion = 2,
            runID = parentRecord.runID,
            registrationDigest = configDigest,
            checkpointDigest = evaluationCheckpoint,
            vocabularyKnee = checked((int)anytimeAuthority.AcceptedPoint.Quality.ExactClasses),
            vocabularyFuelAxes = evalAxes,
            evaluatorCalls = anytimeAuthority.EvaluatorIntervals,
            certificates = anytimeAuthority.Certificates,
            rung0ComposedPredictions = rung0.derivedPredictions,
            rung0EvaluatorCalls = rung0.evaluatorCalls,
            rung0AuditFailures = rung0.auditFailures,
            relationNullExecutions = rung0.nullExecutions,
            relationNullAuthorityPredictions = rung0.nullAuthorityPredictions,
            rung0ReceiptDigest = rung0.ReadDigest(),
            rung0AssayStatus = rung0.assayStatus,
            rung0AssayDetail = rung0.assayDetail,
            rung0ShadowPowerStatus = rung0.shadowPowerStatus,
            rung0ShadowPowerDetail = rung0.shadowPowerDetail,
            rung0NullPowerStatus = rung0.nullPowerStatus,
            rung0NullPowerDetail = rung0.nullPowerDetail,
            policyReadoutPaidCloses = policy.readoutPaidCloses,
            policyTreeEraPaidCloses = policy.treeEraPaidCloses,
            policyReadoutSpend = policy.readoutSpend,
            policyTreeEraSpend = policy.treeEraSpend,
            a3PaidArms = a3.fundedArms,
            a3HorizonShort = a3.horizonShort,
            a3HorizonMedium = a3.horizonMedium,
            a3HorizonLong = a3.horizonLong,
            a3Spend = a3.spend,
            a3ReceiptProvenanceDigest = a3.provenanceDigest,
            checkpointReceiptDigest = checkpoint.ReadDigest(),
            fundingReceiptDigest = funding.ReadDigest(),
            policyReceiptDigest = policy.ReadDigest(),
            trialPlannedSteps = funding.trialPlannedSteps,
            trialActualSteps = funding.trialActualSteps,
            trialRefundSteps = funding.trialRefundSteps,
            readoutPlannedSteps = funding.readoutPlannedSteps,
            readoutActualSteps = funding.readoutActualSteps,
            readoutRefundSteps = funding.readoutRefundSteps,
            policyNullDivergentExecutions = policy.nullDivergentExecutions,
            reflexControlAdaptations = policy.reflexControlAdaptations,
            saveLoadSaveMismatches = checkpoint.mismatches,
            fuelAxes = evalAxes,
            computeStatus = computeStatus,
            computeDarkResidual = Math.Max(funding.ComputeDarkResidual, darkResidual),
            day = day,
            dream = dream,
            aestivation = aestivation,
            residual = ParseDouble(evaluationWindow.Final, "eml.frontier.residual"),
            paidCloses = ParseLong(evaluationWindow.Final, "cortex.homeostat.paid_takeovers"),
            executions = ParseLong(evaluationWindow.Final, "cortex.homeostat.takeover_executions"),
            a3ReceiptStep = a3.receiptStep,
            evaluationTopology = "composite-local",
            evaluationStartStep = 1,
            evaluationEndStep = evaluation.plannedNextStep - 1,
            evaluationRows = evaluationWindow.Rows,
            evaluationCurveDigest = evaluationWindow.Digest,
            evaluationComputeDigest = evaluationCompute.Digest,
            computeReportDigest = digests.Digest(evalReport),
            computeReportRecords = computeRecords,
            sourceDigests = [.. sourcePaths.Select(path => new DeepRematchSourceReceipt
            {
                path = Path.GetRelativePath(parent, path).Replace(Path.DirectorySeparatorChar, '/'), digest = digests.Digest(path)
            })],
        };
        artifact.collectorProvenanceDigest = ComputeCollectorProvenanceDigest(artifact);
        artifact.artifactDigest = ComputeArtifactDigest(artifact);
        ValidateArtifact(artifact);
        return artifact;
    }

    private static VerifiedDeepRematchReceipts ReadVerifiedReceipts(
        string runDirectory,
        string expectedRunID,
        DeepRematchGateConfig gate,
        DigestPass digests)
    {
        string runID = Run.RunIDFromDirectory(runDirectory);
        if (!string.Equals(runID, expectedRunID, StringComparison.Ordinal))
            throw new InvalidDataException($"deep-rematch receipt run identity mismatch: expected {expectedRunID}, got {runID}");
        string checkpointPath = RequireRunFile(runDirectory, Checkpoint.FileName);
        string gatePath = RequireRunFile(runDirectory, "deep-rematch-gate.ron");
        string gateDigestPath = RequireRunFile(runDirectory, "deep-rematch-gate.digest");
        DeepRematchGateConfig registeredGate = DecodeConfig(File.ReadAllBytes(gatePath));
        if (registeredGate.ConfigDigest != gate.ConfigDigest
            || File.ReadAllText(gateDigestPath).Trim() != gate.ConfigDigest)
            throw new InvalidDataException("deep-rematch receipt gate files disagree with the registered gate authority");
        string tapePath = RequireRunFile(runDirectory, "tape.spanlog");
        string policyBoundaryPath = RequireRunFile(runDirectory, "policy_boundary_obligations.tsv");
        string computeReportPath = RequireRunFile(runDirectory, "compute.report.tsv");
        string rung0ControlPath = RequireRunFile(runDirectory, "deep-rematch.rung0-control.ron");
        string trialFundingPath = RequireRunFile(runDirectory, "policy_trial_funding.journal.tsv");
        string trialSettlementPath = RequireRunFile(runDirectory, "policy_trial_settlements.journal.tsv");
        string readoutFundingPath = RequireRunFile(runDirectory, "policy_readout_funding.journal.tsv");
        string readoutSettlementPath = RequireRunFile(runDirectory, "policy_readout_settlements.journal.tsv");
        string policyDecisionsPath = RequireRunFile(runDirectory, "policy_decisions.tsv");
        string policyJournalPath = RequireRunFile(runDirectory, "journal.log");
        string a3Path = RequireRunFile(runDirectory, "deep-rematch.a3.ron");
        string rung0Path = RequireRunFile(runDirectory, "deep-rematch.rung0.ron");
        string checkpointReceiptPath = RequireRunFile(runDirectory, "deep-rematch.checkpoint.ron");
        string fundingPath = RequireRunFile(runDirectory, "deep-rematch.funding.ron");
        string policyPath = RequireRunFile(runDirectory, "deep-rematch.policy.ron");

        DeepRematchA3Receipt a3 = ReadCanonicalDocument<DeepRematchA3Receipt>(a3Path);
        DeepRematchRung0Receipt rung0 = ReadCanonicalDocument<DeepRematchRung0Receipt>(rung0Path);
        DeepRematchCheckpointReceipt checkpoint = ReadCanonicalDocument<DeepRematchCheckpointReceipt>(checkpointReceiptPath);
        DeepRematchFundingReceipt funding = ReadCanonicalDocument<DeepRematchFundingReceipt>(fundingPath);
        DeepRematchPolicyReceipt policy = ReadCanonicalDocument<DeepRematchPolicyReceipt>(policyPath);
        byte[] rung0ControlBytes = File.ReadAllBytes(rung0ControlPath);
        EmlIntensionalRematchControlReceipt rung0Control = RonSerializer.Deserialize<EmlIntensionalRematchControlReceipt>(rung0ControlBytes);
        string checkpointStateDigest = DeepRematchCompositeRON.DigestCheckpoint(runDirectory);
        CortexRunConfig checkpointConfig = Checkpoint.PeekConfig(runDirectory);
        if (!string.Equals(checkpointConfig.DeepRematchGateDigest, gate.ConfigDigest, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(checkpointConfig.DeepRematchGatePath))
            throw new InvalidDataException("deep-rematch receipt checkpoint is not bound to the registered gate");
        ValidateReceipt(a3, runID, checkpointStateDigest, gate.ConfigDigest);
        ValidateReceipt(rung0, runID, checkpointStateDigest, gate.ConfigDigest);
        ValidateReceipt(checkpoint, runID, checkpointStateDigest, gate.ConfigDigest);
        ValidateReceipt(funding, runID, checkpointStateDigest, gate.ConfigDigest);
        ValidateReceipt(policy, runID, checkpointStateDigest, gate.ConfigDigest);
        ValidateRung0Control(rung0ControlBytes, rung0Control, runID, checkpointStateDigest, gate.ConfigDigest);

        string[] provenancePaths =
        [
            checkpointPath, policyBoundaryPath, computeReportPath, rung0ControlPath,
            trialFundingPath, trialSettlementPath, readoutFundingPath, readoutSettlementPath,
            policyDecisionsPath,
        ];
        string expectedFundingProvenance = DigestSources(provenancePaths, digests);
        string expectedPolicyProvenance = Hash(string.Join('|', digests.Digest(policyBoundaryPath),
            digests.Digest(policyDecisionsPath), checkpointStateDigest));
        if (!string.Equals(funding.ProvenanceDigest, expectedFundingProvenance, StringComparison.Ordinal)
            || !string.Equals(policy.ProvenanceDigest, expectedPolicyProvenance, StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch funding or policy receipt is not bound to its source records");
        if (!string.Equals(rung0.ControlReceiptDigest, rung0Control.ReceiptDigest, StringComparison.Ordinal)
            || rung0.NullExecutions != rung0Control.RelationNullExecutions
            || rung0.NullAuthorityPredictions != rung0Control.RelationNullAuthorityPredictions
            || rung0.AssayStatus != rung0Control.assayStatus
            || rung0.AssayDetail != rung0Control.assayDetail
            || rung0.ShadowPowerStatus != rung0Control.shadowPowerStatus
            || rung0.ShadowPowerDetail != rung0Control.shadowPowerDetail
            || rung0.NullPowerStatus != rung0Control.nullPowerStatus
            || rung0.NullPowerDetail != rung0Control.nullPowerDetail
            || !IsDigest(rung0.SourceCursorDigest)
            || !IsDigest(rung0.SourceStateDigest)
            || !string.Equals(rung0.SourceCursorDigest, rung0Control.SourceCursorDigest, StringComparison.Ordinal)
            || !string.Equals(rung0.ProvenanceDigest,
                Hash(string.Join('|', checkpointStateDigest, rung0.SourceCursorDigest, rung0.SourceStateDigest, rung0.ControlReceiptDigest)),
                StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch rung0 receipt is not bound to its source-derived control receipt");

        PolicyBoundaryEvidence boundary = ReadPolicyBoundaryEvidence(policyBoundaryPath, gate.A3PreludeSteps);
        PolicyBoundaryForkReceipt boundaryReceipt = boundary.Receipt;
        ValidatePolicyArms(in boundaryReceipt);
        if (a3.ReceiptStep != boundary.Step || a3.ProvenanceDigest != digests.Digest(policyBoundaryPath))
            throw new InvalidDataException("deep-rematch A3 receipt is not bound to the durable policy-boundary row");
        if (a3.PaidArms != boundaryReceipt.Arms.Select(static arm => arm.Arm).Distinct().Count()
            || a3.HorizonShort != boundaryReceipt.Horizons[0]
            || a3.HorizonMedium != boundaryReceipt.Horizons[1]
            || a3.HorizonLong != boundaryReceipt.Horizons[2]
            || a3.Spend != boundaryReceipt.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Candidate).Sum(static arm => arm.MatchedSpend)
            || a3.NullDivergentExecutions != boundaryReceipt.Arms.LongCount(static arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                && arm.BehaviorallyExecuted && arm.Diverged))
            throw new InvalidDataException("deep-rematch A3 receipt scalars disagree with policy-boundary authority");
        long treePaid = boundaryReceipt.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Baseline).Sum(static arm => arm.PaidCloseDelta);
        long candidatePaid = boundaryReceipt.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Candidate).Sum(static arm => arm.PaidCloseDelta);
        long treeSpend = boundaryReceipt.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Baseline).Sum(static arm => arm.MatchedSpend);
        long candidateSpend = boundaryReceipt.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.Candidate).Sum(static arm => arm.MatchedSpend);
        long nullDivergent = boundaryReceipt.Arms.LongCount(static arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
            && arm.BehaviorallyExecuted && arm.Diverged);
        long reflexAdaptations = boundaryReceipt.Arms.Where(static arm => arm.Arm == PolicyBoundaryArms.ReflexFrozenControl).Sum(static arm => arm.TrialAdaptationTransitions);
        if (policy.TreeEraPaidCloses != treePaid || policy.ReadoutPaidCloses != candidatePaid
            || policy.TreeEraSpend != treeSpend || policy.ReadoutSpend != candidateSpend
            || policy.NullDivergentExecutions != nullDivergent || policy.ReflexControlAdaptations != reflexAdaptations)
            throw new InvalidDataException("deep-rematch policy receipt scalars disagree with policy-boundary authority");
        CortexPolicyTrialJournalOccurrenceCheck trial;
        CortexPolicyTrialJournalOccurrenceCheck readout;
        using (CortexPolicyOccurrenceCheckBundle bundle = new(runDirectory))
        {
            VerifyPolicyBoundaryTape(bundle.Tape, in boundaryReceipt);
            VerifyCheckpointReceipt(runDirectory, checkpointStateDigest, in checkpoint);
            ValidateComputeReport(File.ReadAllText(computeReportPath));

            trial = CortexPolicyTrialJournalVerifier.Verify(bundle, TextWriter.Null);
            readout = CortexPolicyTrialJournalVerifier.VerifyReadout(bundle, TextWriter.Null);
            if (!trial.Passed || !readout.Passed)
                throw new InvalidDataException("deep-rematch policy funding/readout journal verification failed");
            if (!CortexPolicyDecisionReadoutVerifier.Verify(bundle, TextWriter.Null).Passed)
                throw new InvalidDataException("deep-rematch policy decision readout receipt failed verification");
        }
        if (funding.TrialPlannedSteps != trial.PlannedArmSteps || funding.TrialActualSteps != trial.ActualArmSteps || funding.TrialRefundSteps != trial.ReclaimedOrUnused
            || funding.ReadoutPlannedSteps != readout.PlannedArmSteps || funding.ReadoutActualSteps != readout.ActualArmSteps || funding.ReadoutRefundSteps != readout.ReclaimedOrUnused)
            throw new InvalidDataException("deep-rematch funding receipt does not bind separate trial/readout journal currencies");

        List<string> sourcePaths = [.. provenancePaths, gatePath, gateDigestPath, tapePath, policyJournalPath, a3Path, rung0Path, checkpointReceiptPath, fundingPath, policyPath];
        string checkpointDeltaPath = Path.Combine(runDirectory, Checkpoint.DeltaFileName);
        if (File.Exists(checkpointDeltaPath)) sourcePaths.Add(checkpointDeltaPath);
        string terminalRunReceiptPath = Path.Combine(runDirectory, CortexForkTerminalRunReceipt.FileName);
        if (File.Exists(terminalRunReceiptPath))
        {
            sourcePaths.Add(terminalRunReceiptPath);
            string terminalOccurrenceCheckPath = Path.Combine(runDirectory, "terminal-verification.ron");
            if (File.Exists(terminalOccurrenceCheckPath)) sourcePaths.Add(terminalOccurrenceCheckPath);
        }
        string readoutAllocationPath = Path.Combine(runDirectory, "policy_readout_allocations.journal.tsv");
        if (File.Exists(readoutAllocationPath)) sourcePaths.Add(readoutAllocationPath);
        return new(a3, rung0, checkpoint, funding, policy, trial, readout, [.. sourcePaths]);
    }

    private static T ReadCanonicalDocument<T>(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"missing composite record: {path}");
        byte[] bytes = File.ReadAllBytes(path);
        T document = RonSerializer.Deserialize<T>(bytes);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!bytes.AsSpan().SequenceEqual(second)) throw new InvalidDataException($"composite record SaveLoadSave drifted: {path}");
        return document;
    }

    private static string ResolveCompositePath(string parent, string relative, string label)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} path escaped parent");
        string path = Path.GetFullPath(Path.Combine(parent, relative));
        if (!path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} path escaped parent");
        return path;
    }

    private static void RequireCompositeRolePath(
        string parentDirectory,
        string path,
        string childID,
        string fileName,
        string label)
    {
        if (string.IsNullOrWhiteSpace(childID) || Path.GetFileName(childID) != childID)
            throw new InvalidDataException($"composite {label} child identity is not a directory basename");
        string parent = Path.GetFullPath(parentDirectory);
        string child = Path.GetFullPath(Path.Combine(parent, "children", childID));
        string expected = Path.GetFullPath(Path.Combine(child, fileName));
        if (!string.Equals(Path.GetFullPath(path), expected, StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} path is not owned by role child {childID}");
    }

    private static void ValidateCompositeBinding(string path, string digest, string label)
    {
        if (!IsDigest(digest) || !string.Equals(FileDigest(path), digest, StringComparison.Ordinal))
            throw new InvalidDataException($"composite {label} digest mismatch");
    }

    private static string RequireCompositeChildFile(string childDirectory, string file)
    {
        string path = Path.Combine(childDirectory, file);
        if (!File.Exists(path)) throw new InvalidDataException($"child omits required emission {file}");
        return path;
    }

    private static (List<DeepRematchFuelAxis> Axes, EmlAnytimeEvaluationPrefix Authority) ParseEvaluationFuel(
        DeepRematchEvaluationRecord evaluation, string anytimePath)
    {
        if (evaluation.anytime is not DeepRematchAnytimePrefixRecord prefix)
            throw new InvalidDataException("evaluation anytime evidence is absent");
        EmlAnytimeEvaluationPrefix authority = VerifyPersistedHandshakeCursor(evaluation, anytimePath);
        prefix.Validate(Path.GetFileName(evaluation.childRunDirectory), evaluation.actualNextStep - 1, in authority);
        EmlDeliberationCounts planned = authority.PlannedFuel;
        EmlDeliberationCounts actual = authority.ActualFuel;
        EmlDeliberationCounts refund = authority.RefundFuel;
        string source = FileDigest(anytimePath);
        long[] p = [planned.CandidateEvaluations, planned.LogicalProgramPoints, planned.ExecutedProgramPoints, planned.InverseTransforms, planned.HashProbes, planned.JoinAttempts, planned.JoinHits, planned.ProcessTerms, planned.VerifierProgramPoints, planned.CandidateSupplyItems, planned.LawRewriteApplications, planned.LawRewriteTreeNodes];
        long[] a = [actual.CandidateEvaluations, actual.LogicalProgramPoints, actual.ExecutedProgramPoints, actual.InverseTransforms, actual.HashProbes, actual.JoinAttempts, actual.JoinHits, actual.ProcessTerms, actual.VerifierProgramPoints, actual.CandidateSupplyItems, actual.LawRewriteApplications, actual.LawRewriteTreeNodes];
        long[] r = [refund.CandidateEvaluations, refund.LogicalProgramPoints, refund.ExecutedProgramPoints, refund.InverseTransforms, refund.HashProbes, refund.JoinAttempts, refund.JoinHits, refund.ProcessTerms, refund.VerifierProgramPoints, refund.CandidateSupplyItems, refund.LawRewriteApplications, refund.LawRewriteTreeNodes];
        List<DeepRematchFuelAxis> axes = new(EmlDeliberationCounts.AxisNames.Length);
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] != a[i] + r[i] || p[i] < 0 || a[i] < 0 || r[i] < 0)
                throw new InvalidDataException($"evaluation fuel axis {EmlDeliberationCounts.AxisNames[i]} does not close");
            axes.Add(new DeepRematchFuelAxis(EmlDeliberationCounts.AxisNames[i], p[i], a[i], r[i], "Known", "evaluation-local", source));
        }
        return (axes, authority);
    }

    private static EmlAnytimeEvaluationPrefix VerifyPersistedHandshakeCursor(DeepRematchEvaluationRecord evaluation, string anytimePath)
    {
        if (evaluation.emlHandshakeSettlementCount < 0 || evaluation.emlHandshakeEvaluatorCalls < 0
            || evaluation.emlHandshakeDigest.Length != 64 || evaluation.emlHandshakePointDigest.Length != 64
            || evaluation.emlHandshakeSettlementDigest.Length != 64 || string.IsNullOrWhiteSpace(evaluation.emlHandshakePointID)
            || evaluation.emlHandshakeCursorPath != ReplayCalc.DeepRematchFuelCursorSidecarFile
            || evaluation.emlHandshakeCursorSHA256.Length != 64)
            throw new InvalidDataException("evaluation record omits its persisted handshake cursor");
        string cursorPath = Path.Combine(Path.GetDirectoryName(anytimePath)!, evaluation.emlHandshakeCursorPath);
        if (!File.Exists(cursorPath) || FileDigest(cursorPath) != evaluation.emlHandshakeCursorSHA256)
            throw new InvalidDataException("evaluation persisted handshake cursor sidecar is missing or tampered");
        EmlDeepRematchFuelCursor cursor = RonSerializer.Deserialize<EmlDeepRematchFuelCursorDocument>(File.ReadAllBytes(cursorPath)).ToCursor();
        if (cursor.SettlementCount != evaluation.emlHandshakeSettlementCount
            || cursor.EvaluatorCalls != evaluation.emlHandshakeEvaluatorCalls
            || cursor.Digest != evaluation.emlHandshakeDigest
            || cursor.PointID != evaluation.emlHandshakePointID
            || cursor.PointDigest != evaluation.emlHandshakePointDigest
            || cursor.SettlementDigest != evaluation.emlHandshakeSettlementDigest)
            throw new InvalidDataException("evaluation persisted handshake cursor disagrees with its typed evaluation record");
        string checkpointPath = Path.Combine(Path.GetDirectoryName(anytimePath)!, Checkpoint.FileName);
        if (!File.Exists(checkpointPath))
            throw new InvalidDataException("evaluation final checkpoint is missing its AFCU cursor section");
        EmlDeepRematchFuelCursor checkpointCursor = ReplayCalc.ReadDeepRematchFuelCursorFromCheckpointImage(
            Checkpoint.LoadEffectiveImage(Path.GetDirectoryName(anytimePath)!));
        if (checkpointCursor != cursor)
            throw new InvalidDataException("evaluation typed handshake cursor disagrees with the final checkpoint AFCU cursor");
        string[] lines = File.ReadAllLines(anytimePath);
        if (lines.Length < 2) throw new InvalidDataException("evaluation anytime curve omits its handshake row");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        Dictionary<string, int> columns = header.Select((name, index) => (name, index)).ToDictionary(static item => item.name, static item => item.index, StringComparer.Ordinal);
        int pointColumn = Array.IndexOf(header, "point_id");
        int digestColumn = Array.IndexOf(header, "digest");
        int stepColumn = Array.IndexOf(header, "prefix_step");
        if (pointColumn < 0 || digestColumn < 0 || stepColumn < 0)
            throw new InvalidDataException("evaluation anytime curve omits handshake identity columns");
        string[] handshake = lines[1].Split('\t');
        if (handshake.Length != header.Length || handshake[stepColumn] != "0"
            || handshake[pointColumn] != evaluation.emlHandshakePointID
            || handshake[digestColumn] != evaluation.emlHandshakePointDigest)
            throw new InvalidDataException("evaluation anytime handshake row disagrees with its persisted cursor");
        string Text(string name) => handshake[columns[name]];
        if (!int.TryParse(Text("rung"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rung))
            throw new InvalidDataException("evaluation anytime handshake row has an invalid rung");
        EmlAnytimeEvaluationScope scope = new(
            Text("run_id"), Text("config_id"), Text("chain_id"), Text("arm_id"), rung,
            Text("parent_point_id"), FirstStep: 1, LastStep: checked((int)evaluation.plannedNextStep - 1), HandshakeCursor: cursor);
        return EmlAnytimeEvaluationReader.Read(anytimePath, in scope);
    }

    private static string RequireRunFile(string runDirectory, string file)
    {
        string path = Path.Combine(runDirectory, file);
        if (!File.Exists(path)) throw new InvalidDataException($"deep-rematch run omits required receipt {file}");
        return path;
    }

    private static string FileDigest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string DigestSources(IEnumerable<string> paths, DigestPass digests)
        => Hash(string.Join('|', paths.Select(path => Path.GetFileName(path) + ':' + digests.Digest(path))));

    /// Path→digest memo scoped to ONE verification pass. A collector digests the
    /// same authority files at several proof sites; within a pass the bytes are
    /// contractually immutable, so each file is hashed once. Never held across
    /// passes — files may change between them, so every pass builds its own.
    internal sealed class DigestPass
    {
        private readonly Dictionary<string, string> _byPath = new(StringComparer.Ordinal);

        internal string Digest(string path)
        {
            string key = Path.GetFullPath(path);
            if (!_byPath.TryGetValue(key, out string? digest))
                _byPath[key] = digest = FileDigest(path);
            return digest;
        }

        internal void Seed(string path, string digest) => _byPath[Path.GetFullPath(path)] = digest;
    }
    internal static string ComputeCollectorProvenanceDigest(DeepRematchArtifact artifact)
        => Hash(string.Join('|', artifact.RunID, artifact.RegistrationDigest, artifact.CheckpointDigest,
            string.Join(';', artifact.SourceDigests.Select(static source => source.Path + ':' + source.Digest))));
    private readonly record struct VerifiedDeepRematchReceipts(
        DeepRematchA3Receipt A3,
        DeepRematchRung0Receipt Rung0,
        DeepRematchCheckpointReceipt Checkpoint,
        DeepRematchFundingReceipt Funding,
        DeepRematchPolicyReceipt Policy,
        CortexPolicyTrialJournalOccurrenceCheck Trial,
        CortexPolicyTrialJournalOccurrenceCheck Readout,
        string[] SourcePaths);
    private readonly record struct WindowReceipt(Dictionary<string, string> Final, long Rows, string Digest);
    private readonly record struct PolicyBoundaryEvidence(long Step, PolicyBoundaryForkReceipt Receipt);

    private static PolicyBoundaryEvidence ReadPolicyBoundaryEvidence(string path, long preludeSteps)
    {
        string[] lines = File.ReadAllLines(path);
        long selectedStep = long.MaxValue;
        PolicyBoundaryForkReceipt selected = default;
        bool found = false;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] fields = lines[i].Split('\t');
            if (fields.Length != PolicyBoundaryReceiptColumnCount || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long rowStep)
                || rowStep > preludeSteps || rowStep >= selectedStep || fields[9] != "1") continue;
            if (!TryParsePolicyBoundaryRow(fields, out PolicyBoundaryForkReceipt candidate)) continue;
            candidate.Validate(HomeostatPolicyBoundaryDomain.Instance);
            selectedStep = rowStep;
            selected = candidate;
            found = true;
        }
        if (!found) throw new InvalidDataException("deep-rematch policy boundary TSV has no verified prelude receipt");
        return new PolicyBoundaryEvidence(selectedStep, selected);
    }

    private static bool TryParsePolicyBoundaryRow(string[] fields, out PolicyBoundaryForkReceipt receipt)
    {
        receipt = default;
        try
        {
            if (fields.Length != PolicyBoundaryReceiptColumnCount) return false;
            CortexPolicyID policy = new(fields[1]);
            // This gate consumes the Homeostat boundary rail; the durable row's
            // policy owner must say so explicitly rather than being inferred from
            // the arm payload.
            if (!policy.Equals(Homeostat.PolicyID)) return false;
            if (!PolicyBoundaryRational.TryParse(fields[3], out PolicyBoundaryRational candidateBoundary)
                || !PolicyBoundaryRational.TryParse(fields[4], out PolicyBoundaryRational baselineBoundary)) return false;
            string[] horizonFields = fields[5].Split(',', StringSplitOptions.RemoveEmptyEntries);
            int[] horizons = new int[horizonFields.Length];
            for (int i = 0; i < horizons.Length; i++)
                if (!int.TryParse(horizonFields[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out horizons[i])) return false;
            string[] armFields = fields[10].Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (armFields.Length != horizons.Length * 4) return false;
            PolicyBoundaryArmReceipt[] arms = new PolicyBoundaryArmReceipt[armFields.Length];
            for (int i = 0; i < arms.Length; i++)
            {
                string[] arm = armFields[i].Split(',');
                if (arm.Length is not (LegacyPolicyBoundaryArmEvidenceColumnCount or PolicyBoundaryArmEvidenceColumnCount)
                    || !byte.TryParse(arm[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
                    || !int.TryParse(arm[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int horizon)
                    || !long.TryParse(arm[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long paid)
                    || !long.TryParse(arm[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long spend)
                    || !int.TryParse(arm[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int continuity)
                    || !int.TryParse(arm[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int childProcessCompleted)
                    || !long.TryParse(arm[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long grammar)
                    || !long.TryParse(arm[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long transitions)
                    || !int.TryParse(arm[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int adaptation)
                    || adaptation is not (0 or 1)
                    || !byte.TryParse(arm[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executionOutcome)
                    || !long.TryParse(arm[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out long requestCount)
                    || !long.TryParse(arm[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out long guardAdmittedCount)
                    || !ulong.TryParse(arm[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong lastRequestDecisionID)
                    || !int.TryParse(arm[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestStep)
                    || !int.TryParse(arm[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestLaunchpad)
                    || !int.TryParse(arm[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestRaw)
                    || !int.TryParse(arm[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestSelected)
                    || !int.TryParse(arm[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestAction)
                    || !byte.TryParse(arm[18], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte lastRequestAuthority)
                    || !ulong.TryParse(arm[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong lastRequestRevision)
                    || !byte.TryParse(arm[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte lastRequestCause)
                    || !ulong.TryParse(arm[21], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong lastRequestSupport)
                    || !ulong.TryParse(arm[22], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong lastRequestCandidate)
                    || !ulong.TryParse(arm[23], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong executedDecisionID)
                    || !int.TryParse(arm[24], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedStep)
                    || !int.TryParse(arm[25], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedLaunchpad)
                    || !int.TryParse(arm[26], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedRaw)
                    || !int.TryParse(arm[27], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedSelected)
                    || !int.TryParse(arm[28], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedAction)
                    || !byte.TryParse(arm[29], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executedAuthority)
                    || !byte.TryParse(arm[30], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executedCause)
                    || !ulong.TryParse(arm[31], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedFingerprint)
                    || !ulong.TryParse(arm[32], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong executedRevision)
                    || !ulong.TryParse(arm[33], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedSupportDigest)
                    || !ulong.TryParse(arm[34], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedCandidateFingerprint)
                    || !byte.TryParse(arm[36], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executedCanonicalKind)
                    || !ushort.TryParse(arm[37], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort executedCanonicalVersion)
                    || !ulong.TryParse(arm[38], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedCanonicalValue)
                    || !long.TryParse(arm[39], NumberStyles.Integer, CultureInfo.InvariantCulture, out long executedDecisionEventID)
                    || !ulong.TryParse(arm[40], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong forcedDivergenceSeed)) return false;
                bool derivedDivergence = (PolicyBoundaryArms)kind == PolicyBoundaryArms.ForcedDivergentNull
                    && (CortexPolicyTrialExecutionOutcomes)executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                    && guardAdmittedCount > 0
                    && executedAction >= 0
                    && executedAction != executedLaunchpad
                    && executedAction != executedRaw;
                if (arm.Length == PolicyBoundaryArmEvidenceColumnCount
                    && (!TryFlag(arm[41], out bool recordedDivergence) || recordedDivergence != derivedDivergence)) return false;
                PolicyCanonicalStateID executedCanonicalState = default;
                if (executedCanonicalVersion != 0)
                {
                    if (string.IsNullOrEmpty(arm[35])) return false;
                    executedCanonicalState = new PolicyCanonicalStateID(new CortexPolicyID(arm[35]),
                        (PolicyCanonicalStateKinds)executedCanonicalKind, executedCanonicalVersion, executedCanonicalValue);
                }
                else if (!string.IsNullOrEmpty(arm[35]) || executedCanonicalValue != 0 || executedCanonicalKind != 0)
                    return false;
                arms[i] = new((PolicyBoundaryArms)kind, horizon, paid, spend, continuity == 1, childProcessCompleted == 1, grammar, transitions, adaptation == 1)
                {
                    ExecutionOutcome = (CortexPolicyTrialExecutionOutcomes)executionOutcome,
                    RequestCount = requestCount,
                    GuardAdmittedCount = guardAdmittedCount,
                    LastRequestDecisionID = new CortexPolicyDecisionID(lastRequestDecisionID),
                    LastRequestStep = lastRequestStep,
                    LastRequestReadout = new(
                        lastRequestLaunchpad, lastRequestRaw, lastRequestSelected, lastRequestAction,
                        (CortexPolicyAuthorities)lastRequestAuthority, new GrammarRevisionID(lastRequestRevision),
                        (CortexPolicySelectionCauses)lastRequestCause, lastRequestSupport, lastRequestCandidate),
                    ExecutedDecisionID = new CortexPolicyDecisionID(executedDecisionID),
                    ExecutedStep = executedStep,
                    ExecutedLaunchpadAction = executedLaunchpad,
                    ExecutedRawCandidateAction = executedRaw,
                    ExecutedSelectedCandidateAction = executedSelected,
                    ExecutedAction = executedAction,
                    ExecutedAuthority = (CortexPolicyAuthorities)executedAuthority,
                    ExecutedSelectionCause = (CortexPolicySelectionCauses)executedCause,
                    ExecutedReadoutFingerprint = executedFingerprint,
                    ExecutedReadoutRevision = executedRevision,
                    ExecutedReadoutOccurrenceDigest = executedSupportDigest,
                    ExecutedCandidateFingerprint = executedCandidateFingerprint,
                    ExecutedCanonicalState = executedCanonicalState,
                    ExecutedDecisionEventID = new TapeEventID(executedDecisionEventID),
                    ForcedDivergenceSeed = forcedDivergenceSeed,
                    Diverged = derivedDivergence,
                };
                if (executedCanonicalState.Version != 0
                    && !executedCanonicalState.Policy.Equals(policy)) return false;
            }
            if (!TryFlag(fields[6], out bool continuityExact) || !TryFlag(fields[7], out bool matchedSpend)
                || !TryFlag(fields[8], out bool forcedNullBehaviorExecuted) || !TryFlag(fields[9], out bool verified)) return false;
            if (!ulong.TryParse(fields[15], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong fundingDecisionID)
                || !ulong.TryParse(fields[12], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong sourceFingerprint)
                || !ulong.TryParse(fields[13], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong sourceCandidateFingerprint)
                || !ulong.TryParse(fields[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong sourceRevision)) return false;
            receipt = new(new PolicyBoundaryObligationID(fields[2]), baselineBoundary, candidateBoundary, horizons, arms,
                continuityExact, matchedSpend, forcedNullBehaviorExecuted, verified, sourceFingerprint, sourceRevision)
            {
                QuotaDecisionID = new CortexPolicyQuotaDecisionID(fundingDecisionID),
                SourceDecisionCandidateFingerprint = sourceCandidateFingerprint,
            };
            if (!string.Equals(fields[11], PolicyBoundaryObligation.ComputeReceiptDigest(in receipt), StringComparison.Ordinal)) return false;
            return true;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static bool TryFlag(string text, out bool value)
    {
        value = false;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || (parsed != 0 && parsed != 1)) return false;
        value = parsed == 1;
        return true;
    }

    private static void VerifyPolicyBoundaryTape(Tape tape, in PolicyBoundaryForkReceipt expected)
    {
        string expectedDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in expected);
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] packet)
                || !PolicyBoundaryTapeVerifier.TryRead(packet, HomeostatPolicyBoundaryDomain.Instance,
                    out PolicyBoundaryForkReceipt decoded, out CortexPolicyID policy)
                || !policy.Equals(Homeostat.PolicyID)) continue;
            if (string.Equals(expectedDigest, PolicyBoundaryObligation.ComputeReceiptDigest(in decoded), StringComparison.Ordinal)) return;
        }
        throw new InvalidDataException("deep-rematch policy boundary TSV receipt is absent from durable tape");
    }

    private static void VerifyCheckpointReceipt(string runDirectory, string checkpointDigest, in DeepRematchCheckpointReceipt receipt)
    {
        if (receipt.Mismatches != 0 || receipt.Dialect != Checkpoint.CurrentDialect
            || receipt.SaveDigest != checkpointDigest || receipt.LoadSaveDigest != checkpointDigest)
            throw new InvalidDataException($"deep-rematch checkpoint receipt is not a passing {Checkpoint.CurrentDialect} SaveLoadSave");
        string terminalPath = Path.Combine(runDirectory, CortexForkTerminalRunReceipt.FileName);
        if (File.Exists(terminalPath))
        {
            CortexForkTerminalRunReceipt probe;
            try
            {
                probe = RonSerializer.Deserialize<CortexForkTerminalRunReceipt>(File.ReadAllBytes(terminalPath));
            }
            catch (Exception error)
            {
                throw new InvalidDataException("deep-rematch fork terminal authority is not readable RON", error);
            }
            if (probe.schemaVersion == CortexForkTerminalRunReceipt.CurrentSchemaVersion)
            {
                CortexForkTerminalRunReceipt terminal = CortexForkTerminalRunReceipt.Read(runDirectory);
                if (terminal.exitCode != 0 || !terminal.terminalOccurrenceCheckAttempted || !terminal.terminalOccurrenceCheckExact
                    || terminal.finalCheckpointSHA256 != checkpointDigest)
                    throw new InvalidDataException("deep-rematch checkpoint receipt disagrees with its verified fork terminal authority");
            }
            else if (probe.schemaVersion == 2)
            {
                DeepRematchLegacyTerminalAuthority legacy = DeepRematchLegacyTerminalAuthority.Read(runDirectory);
                if (legacy.TerminalRun.exitCode != 0 || !legacy.TerminalRun.terminalOccurrenceCheckAttempted
                    || !legacy.TerminalRun.terminalOccurrenceCheckExact
                    || legacy.TerminalRun.finalCheckpointSHA256 != checkpointDigest)
                    throw new InvalidDataException("deep-rematch checkpoint receipt disagrees with its verified legacy fork terminal authority");
            }
            else throw new InvalidDataException($"unsupported deep-rematch terminal authority schema {probe.schemaVersion}");
            return;
        }
        if (!Cortex.VerifyCheckpointLogicalRoundTrip(runDirectory, out string diskDigest, out string encodedDigest)
            || diskDigest != checkpointDigest || encodedDigest != receipt.LoadSaveDigest)
            throw new InvalidDataException("deep-rematch checkpoint receipt does not match a fresh SaveLoadSave verification");
    }

    private static void ValidateRung0Control(
        byte[] bytes,
        EmlIntensionalRematchControlReceipt receipt,
        string runID,
        string checkpointDigest,
        string configDigest)
    {
        byte[] roundTrip = RonSerializer.SerializeToUtf8(in receipt);
        if (!bytes.AsSpan().SequenceEqual(roundTrip))
            throw new InvalidDataException("deep-rematch rung0 control SaveLoadSave drifted");
        if (receipt.dialect != "EML-INTENSIONAL-BOUND-CONTROL-V4"
            || receipt.ReceiptDigest != receipt.ComputeDigest()
            || receipt.RunID != runID
            || receipt.CheckpointDigest != checkpointDigest
            || receipt.ConfigDigest != configDigest
            || receipt.Seed != 0xE311C0DEUL
            || receipt.Replicates != 3
            || receipt.TrialsPerReplicate != 64
            || receipt.EvaluatorCalls != 1_000
            || receipt.DelayEvaluatorCalls != 100
            || !receipt.SaveLoadSave
            || !receipt.SourceAdmissionSaveLoadSave
            || !receipt.SourceLawStoreSaveLoadSave
            || receipt.AssayStatus != nameof(EmlRematchAssayStatuses.Exact)
            || receipt.DeliberationEpoch != EmlIntensionalRematchRunner.BuildControlDeliberationEpoch(
                receipt.RunID, receipt.CheckpointDigest, receipt.ConfigDigest, receipt.SourceCursorDigest,
                receipt.SourceAdmissionDigest, receipt.SourceLawStoreDigest)
            || !double.IsFinite(receipt.WallMilliseconds)
            || receipt.WallMilliseconds < 0
            || !IsDigest(receipt.SourceAdmissionDigest)
            || !IsDigest(receipt.SourceLawStoreDigest)
            || !IsDigest(receipt.SourceCursorDigest)
            || !IsDigest(receipt.ScheduleDigest)
            || !IsDigest(receipt.ReportDigest))
            throw new InvalidDataException("deep-rematch rung0 control identity, plan, source, or round-trip receipt is invalid");

        ValidateSourceRoundTripDiagnostic(
            receipt.sourceAdmissionRoundTripKind,
            receipt.sourceAdmissionOriginalLength,
            receipt.sourceAdmissionResavedLength,
            receipt.sourceAdmissionFirstDifferenceOffset,
            receipt.sourceAdmissionDifferenceSection,
            receipt.sourceAdmissionRoundTripDetail,
            "admission");
        ValidateSourceRoundTripDiagnostic(
            receipt.sourceLawStoreRoundTripKind,
            receipt.sourceLawStoreOriginalLength,
            receipt.sourceLawStoreResavedLength,
            receipt.sourceLawStoreFirstDifferenceOffset,
            receipt.sourceLawStoreDifferenceSection,
            receipt.sourceLawStoreRoundTripDetail,
            "law-store");
        if ((receipt.sourceAdmissionRoundTripKind == "exact" ? 1 : 0) != receipt.sourceAdmissionSaveLoadSave
            || (receipt.sourceLawStoreRoundTripKind == "exact" ? 1 : 0) != receipt.sourceLawStoreSaveLoadSave
            || receipt.saveLoadSave != (receipt.sourceAdmissionSaveLoadSave == 1 && receipt.sourceLawStoreSaveLoadSave == 1 ? 1 : 0))
            throw new InvalidDataException("deep-rematch rung0 control source diagnostics disagree with round-trip flags");

        int armKinds = Enum.GetValues<EmlIntensionalRematchArms>().Length;
        if (receipt.Arms.Count != receipt.Replicates * armKinds)
            throw new InvalidDataException("deep-rematch rung0 control does not carry the exact 3x7 arm matrix");
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (EmlIntensionalRematchControlArmRow row in receipt.Arms)
        {
            if (!names.Add(row.name)
                || row.scheduledTrials != receipt.TrialsPerReplicate
                || row.executedTrials != receipt.TrialsPerReplicate
                || row.assayStatus != nameof(EmlRematchAssayStatuses.Exact)
                || string.IsNullOrWhiteSpace(row.assayDetail)
                || row.powerStatus is not nameof(EmlRematchPowerStatuses.Powered)
                    and not nameof(EmlRematchPowerStatuses.Unpowered)
                    and not nameof(EmlRematchPowerStatuses.NotApplicable)
                || string.IsNullOrWhiteSpace(row.powerDetail)
                || row.rung0Attempts < 0
                || row.rung0Composed < 0
                || row.rung0EvaluatorZero < 0
                || row.rung0Audits < 0
                || row.relationNullExecutions < 0
                || row.relationNullDivergences < 0
                || row.relationNullAuthorityPredictions < 0
                || row.rung0Composed > row.rung0Attempts
                || row.rung0EvaluatorZero > row.rung0Composed
                || row.rung0Audits > row.rung0Attempts
                || row.relationNullDivergences > row.relationNullExecutions
                || row.relationNullAuthorityPredictions > row.relationNullExecutions
                || row.evaluatorCalls != receipt.EvaluatorCalls
                || row.delayEvaluatorCalls != receipt.DelayEvaluatorCalls
                || row.canonicalDeltas < 0)
                throw new InvalidDataException($"deep-rematch rung0 control arm {row.name} is malformed or unpowered");
        }
        foreach (EmlIntensionalRematchControlArmRow row in receipt.Arms)
        {
            bool causal = row.name.EndsWith(":" + EmlIntensionalRematchRunner.ReadArmName(EmlIntensionalRematchArms.LawCandidateShadow), StringComparison.Ordinal)
                || row.name.EndsWith(":" + EmlIntensionalRematchRunner.ReadArmName(EmlIntensionalRematchArms.LawShuffledNull), StringComparison.Ordinal);
            if (causal
                ? row.powerStatus is not nameof(EmlRematchPowerStatuses.Powered) and not nameof(EmlRematchPowerStatuses.Unpowered)
                : row.powerStatus != nameof(EmlRematchPowerStatuses.NotApplicable))
                throw new InvalidDataException($"deep-rematch rung0 control arm {row.name} has a power status outside its domain");
        }
        for (int replicate = 0; replicate < receipt.Replicates; replicate++)
            foreach (EmlIntensionalRematchArms arm in Enum.GetValues<EmlIntensionalRematchArms>())
            {
                string name = $"{replicate}:{EmlIntensionalRematchRunner.ReadArmName(arm)}";
                if (!names.Contains(name))
                    throw new InvalidDataException($"deep-rematch rung0 control omits {name}");
            }

        string nullSuffix = ":" + EmlIntensionalRematchRunner.ReadArmName(EmlIntensionalRematchArms.LawShuffledNull);
        string shadowSuffix = ":" + EmlIntensionalRematchRunner.ReadArmName(EmlIntensionalRematchArms.LawCandidateShadow);
        EmlIntensionalRematchControlArmRow[] nullRows = [.. receipt.Arms.Where(row => row.name.EndsWith(nullSuffix, StringComparison.Ordinal))];
        EmlIntensionalRematchControlArmRow[] shadowRows = [.. receipt.Arms.Where(row => row.name.EndsWith(shadowSuffix, StringComparison.Ordinal))];
        string expectedShadowPower = shadowRows.Length > 0
            && shadowRows.All(static row => row.powerStatus == nameof(EmlRematchPowerStatuses.Powered))
            ? nameof(EmlRematchPowerStatuses.Powered) : nameof(EmlRematchPowerStatuses.Unpowered);
        string expectedNullPower = nullRows.Length > 0
            && nullRows.All(static row => row.powerStatus == nameof(EmlRematchPowerStatuses.Powered))
            ? nameof(EmlRematchPowerStatuses.Powered) : nameof(EmlRematchPowerStatuses.Unpowered);
        if (receipt.shadowPowerStatus != expectedShadowPower || receipt.nullPowerStatus != expectedNullPower)
            throw new InvalidDataException("deep-rematch rung0 control aggregate power status disagrees with arm rows");
        if (nullRows.Sum(static row => row.relationNullExecutions) != receipt.RelationNullExecutions
            || nullRows.Sum(static row => row.relationNullDivergences) != receipt.RelationNullDivergences
            || nullRows.Sum(static row => row.relationNullAuthorityPredictions) != receipt.RelationNullAuthorityPredictions
            || shadowRows.Sum(static row => row.rung0Composed) != receipt.ShadowComposed
            || shadowRows.Sum(static row => row.rung0EvaluatorZero) != receipt.ShadowEvaluatorZero
            || shadowRows.Sum(static row => row.rung0Audits) != receipt.ShadowAudits
            || receipt.AssayStatus != nameof(EmlRematchAssayStatuses.Exact)
            || string.IsNullOrWhiteSpace(receipt.assayDetail)
            || receipt.shadowPowerStatus is not nameof(EmlRematchPowerStatuses.Powered)
                and not nameof(EmlRematchPowerStatuses.Unpowered)
            || receipt.nullPowerStatus is not nameof(EmlRematchPowerStatuses.Powered)
                and not nameof(EmlRematchPowerStatuses.Unpowered)
            || string.IsNullOrWhiteSpace(receipt.shadowPowerDetail)
            || string.IsNullOrWhiteSpace(receipt.nullPowerDetail))
            throw new InvalidDataException("deep-rematch rung0 control aggregate diagnostics disagree with arm rows");
        if (receipt.shadowPowerStatus == nameof(EmlRematchPowerStatuses.Powered)
            && (receipt.ShadowComposed <= 0 || receipt.ShadowEvaluatorZero != receipt.ShadowComposed || receipt.ShadowAudits <= 0))
            throw new InvalidDataException("deep-rematch rung0 control marks shadow powered without audited zero-cost derivation");
        if (receipt.nullPowerStatus == nameof(EmlRematchPowerStatuses.Powered)
            && (receipt.RelationNullExecutions <= 0 || receipt.RelationNullDivergences != receipt.RelationNullExecutions || receipt.RelationNullAuthorityPredictions != 0))
            throw new InvalidDataException("deep-rematch rung0 control marks relation-null powered without divergence");
    }

    internal static void ValidateSettlementControl(string controlPath, string childRunID, string checkpointDigest, string configDigest)
    {
        if (!File.Exists(controlPath)) throw new InvalidDataException("settlement control receipt is missing");
        byte[] bytes = File.ReadAllBytes(controlPath);
        EmlIntensionalRematchControlReceipt control = RonSerializer.Deserialize<EmlIntensionalRematchControlReceipt>(bytes);
        ValidateRung0Control(bytes, control, childRunID, checkpointDigest, configDigest);
    }

    internal static string ReadCanonicalGateDigest(string parentDirectory, string childDirectory)
    {
        string parentPath = Path.Combine(parentDirectory, "deep-rematch-gate.ron");
        string childPath = Path.Combine(childDirectory, "deep-rematch-gate.ron");
        if (!File.Exists(parentPath) || !File.Exists(childPath))
            throw new InvalidDataException("deep-rematch settlement gate authority is missing from its canonical parent/child paths");

        DeepRematchGateConfig parent = DecodeConfig(File.ReadAllBytes(parentPath));
        DeepRematchGateConfig child = DecodeConfig(File.ReadAllBytes(childPath));
        string parentDigest = DeepRematchCompositeRON.DigestFile(parentPath);
        string childDigest = DeepRematchCompositeRON.DigestFile(childPath);
        if (!string.Equals(parent.ConfigDigest, child.ConfigDigest, StringComparison.Ordinal)
            || !string.Equals(parentDigest, childDigest, StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch settlement gate authority changed between canonical parent/child paths");
        return parent.ConfigDigest;
    }

    private static void ValidateSourceRoundTripDiagnostic(
        string kind,
        int originalLength,
        int resavedLength,
        int firstDifferenceOffset,
        string differenceSection,
        string detail,
        string component)
    {
        if (originalLength <= 0)
            throw new InvalidDataException($"deep-rematch rung0 control {component} source image length is not positive");
        switch (kind)
        {
            case "exact":
                if (resavedLength != originalLength || firstDifferenceOffset != -1
                    || differenceSection.Length != 0 || detail.Length != 0)
                    throw new InvalidDataException($"deep-rematch rung0 control {component} exact diagnostic is malformed");
                break;
            case "digest-mismatch":
                if (resavedLength != 0 || firstDifferenceOffset != -1
                    || differenceSection != "source-digest" || detail.Length == 0)
                    throw new InvalidDataException($"deep-rematch rung0 control {component} digest diagnostic is malformed");
                break;
            case "load-failure":
                if (resavedLength != 0 || firstDifferenceOffset != -1
                    || differenceSection != "load" || detail.Length == 0)
                    throw new InvalidDataException($"deep-rematch rung0 control {component} load diagnostic is malformed");
                break;
            case "byte-drift":
                if (resavedLength < 0 || firstDifferenceOffset < 0
                    || firstDifferenceOffset >= Math.Max(originalLength, resavedLength)
                    || differenceSection != "binary" || detail.Length == 0)
                    throw new InvalidDataException($"deep-rematch rung0 control {component} byte-drift diagnostic is malformed");
                break;
            default:
                throw new InvalidDataException($"deep-rematch rung0 control {component} has unknown source diagnostic kind '{kind}'");
        }
    }

    internal static EmlIntensionalRematchControlReceipt CreateFixtureControl(
        string runID, string checkpointDigest, string configDigest, string sourceCursorDigest)
    {
        List<EmlIntensionalRematchControlArmRow> rows = [];
        for (int replicate = 0; replicate < 3; replicate++)
            foreach (EmlIntensionalRematchArms arm in Enum.GetValues<EmlIntensionalRematchArms>())
            {
                bool shadow = arm == EmlIntensionalRematchArms.LawCandidateShadow;
                bool relationNull = arm == EmlIntensionalRematchArms.LawShuffledNull;
                rows.Add(new EmlIntensionalRematchControlArmRow
                {
                    name = $"{replicate}:{EmlIntensionalRematchRunner.ReadArmName(arm)}",
                    scheduledTrials = 64,
                    executedTrials = 64,
                    rung0Attempts = shadow || relationNull ? 1 : 0,
                    rung0Composed = shadow ? 1 : 0,
                    rung0EvaluatorZero = shadow ? 1 : 0,
                    rung0Audits = shadow ? 1 : 0,
                    relationNullExecutions = relationNull ? 1 : 0,
                    relationNullDivergences = relationNull ? 1 : 0,
                    evaluatorCalls = 1_000,
                    delayEvaluatorCalls = 100,
                    assayStatus = nameof(EmlRematchAssayStatuses.Exact),
                    assayDetail = "exact",
                    powerStatus = shadow || relationNull ? nameof(EmlRematchPowerStatuses.Powered) : nameof(EmlRematchPowerStatuses.NotApplicable),
                    powerDetail = shadow ? "rung-0 derived claims audited at zero evaluator cost"
                        : relationNull ? "relation-null executions diverged without authority claims" : "arm has no causal power predicate",
                });
            }
        EmlIntensionalRematchControlReceipt receipt = new()
        {
            runID = runID,
            checkpointDigest = checkpointDigest,
            configDigest = configDigest,
            sourceAdmissionDigest = Hash("rung0-control-admission"),
            sourceLawStoreDigest = Hash("rung0-control-laws"),
            sourceCursorDigest = sourceCursorDigest,
            deliberationEpoch = EmlIntensionalRematchRunner.BuildControlDeliberationEpoch(
                runID, checkpointDigest, configDigest, sourceCursorDigest,
                Hash("rung0-control-admission"), Hash("rung0-control-laws")),
            scheduleDigest = Hash("rung0-control-schedule"),
            reportDigest = Hash("rung0-control-report"),
            seed = 0xE311C0DEUL,
            replicates = 3,
            trialsPerReplicate = 64,
            evaluatorCalls = 1_000,
            delayEvaluatorCalls = 100,
            relationNullExecutions = 3,
            relationNullDivergences = 3,
            shadowComposed = 3,
            shadowEvaluatorZero = 3,
            shadowAudits = 3,
            sourceAdmissionSaveLoadSave = 1,
            sourceLawStoreSaveLoadSave = 1,
            assayStatus = nameof(EmlRematchAssayStatuses.Exact),
            assayDetail = "exact",
            shadowPowerStatus = nameof(EmlRematchPowerStatuses.Powered),
            shadowPowerDetail = "all candidate-shadow replicates powered",
            nullPowerStatus = nameof(EmlRematchPowerStatuses.Powered),
            nullPowerDetail = "all relation-null replicates powered",
            sourceAdmissionRoundTripKind = "exact",
            sourceAdmissionOriginalLength = 1024,
            sourceAdmissionResavedLength = 1024,
            sourceAdmissionFirstDifferenceOffset = -1,
            sourceAdmissionDifferenceSection = "",
            sourceAdmissionRoundTripDetail = "",
            sourceLawStoreRoundTripKind = "exact",
            sourceLawStoreOriginalLength = 1024,
            sourceLawStoreResavedLength = 1024,
            sourceLawStoreFirstDifferenceOffset = -1,
            sourceLawStoreDifferenceSection = "",
            sourceLawStoreRoundTripDetail = "",
            saveLoadSave = 1,
            wallMilliseconds = 1,
            arms = rows,
        };
        receipt.receiptDigest = receipt.ComputeDigest();
        return receipt;
    }

    internal static bool VerifyRung0ControlFixture()
    {
        const string parentRunID = "rung0-control-parent";
        const string childRunID = "rung0-control-child";
        string checkpoint = Hash("rung0-control-checkpoint");
        string config = Hash("rung0-control-config");
        EmlIntensionalRematchControlReceipt Create()
            => CreateFixtureControl(childRunID, checkpoint, config, Hash("rung0-control-cursor"));

        bool Accepts(EmlIntensionalRematchControlReceipt receipt, string expectedRunID = childRunID)
        {
            byte[] bytes = RonSerializer.SerializeToUtf8(in receipt);
            try { ValidateRung0Control(bytes, receipt, expectedRunID, checkpoint, config); return true; }
            catch (InvalidDataException) { return false; }
        }

        EmlIntensionalRematchControlReceipt valid = Create();
        if (!Accepts(valid, childRunID) || Accepts(valid, parentRunID)) return false;
        EmlIntensionalRematchControlReceipt wrongName = Create();
        wrongName.arms[0].name = "0:FreshEnumeration";
        wrongName.receiptDigest = wrongName.ComputeDigest();
        EmlIntensionalRematchControlReceipt unpaid = Create();
        unpaid.arms[0].evaluatorCalls = 0;
        unpaid.receiptDigest = unpaid.ComputeDigest();
        EmlIntensionalRematchControlReceipt authoritativeNull = Create();
        authoritativeNull.arms[^1].relationNullAuthorityPredictions = 1;
        authoritativeNull.relationNullAuthorityPredictions = 1;
        authoritativeNull.receiptDigest = authoritativeNull.ComputeDigest();
        EmlIntensionalRematchControlReceipt wrongSeed = Create();
        wrongSeed.seed++;
        wrongSeed.receiptDigest = wrongSeed.ComputeDigest();
        EmlIntensionalRematchControlReceipt wrongReplicates = Create();
        wrongReplicates.replicates = 2;
        wrongReplicates.receiptDigest = wrongReplicates.ComputeDigest();
        EmlIntensionalRematchControlReceipt wrongTrials = Create();
        wrongTrials.trialsPerReplicate = 63;
        wrongTrials.receiptDigest = wrongTrials.ComputeDigest();
        EmlIntensionalRematchControlReceipt unpaidDelay = Create();
        unpaidDelay.arms[0].delayEvaluatorCalls = 0;
        unpaidDelay.receiptDigest = unpaidDelay.ComputeDigest();
        EmlIntensionalRematchControlReceipt admissionRoundTrip = Create();
        admissionRoundTrip.sourceAdmissionSaveLoadSave = 0;
        admissionRoundTrip.saveLoadSave = 0;
        admissionRoundTrip.receiptDigest = admissionRoundTrip.ComputeDigest();
        EmlIntensionalRematchControlReceipt lawStoreRoundTrip = Create();
        lawStoreRoundTrip.sourceLawStoreSaveLoadSave = 0;
        lawStoreRoundTrip.saveLoadSave = 0;
        lawStoreRoundTrip.receiptDigest = lawStoreRoundTrip.ComputeDigest();
        EmlIntensionalRematchControlReceipt malformedDiagnostic = Create();
        malformedDiagnostic.sourceAdmissionRoundTripKind = "load-failure";
        malformedDiagnostic.sourceAdmissionResavedLength = 0;
        malformedDiagnostic.sourceAdmissionFirstDifferenceOffset = -1;
        malformedDiagnostic.sourceAdmissionDifferenceSection = "load";
        malformedDiagnostic.sourceAdmissionRoundTripDetail = "fixture";
        malformedDiagnostic.sourceAdmissionSaveLoadSave = 0;
        malformedDiagnostic.saveLoadSave = 0;
        malformedDiagnostic.receiptDigest = malformedDiagnostic.ComputeDigest();
        bool matureSource = EmlIntensionalRematchRunner.VerifyBoundControlSourceFixture();
        return !Accepts(wrongName)
            && !Accepts(unpaid)
            && !Accepts(authoritativeNull)
            && !Accepts(wrongSeed)
            && !Accepts(wrongReplicates)
            && !Accepts(wrongTrials)
            && !Accepts(unpaidDelay)
            && !Accepts(admissionRoundTrip)
            && !Accepts(lawStoreRoundTrip)
            && !Accepts(malformedDiagnostic)
            && matureSource;
    }

    private static void VerifyFuelAxesAgainstCurve(string path, List<DeepRematchFuelAxis> axes, string? acceptedPointDigest = null)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException("deep-rematch anytime curve has no terminal row");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        Dictionary<string, int> columns = new(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++) columns[header[i]] = i;
        if (!columns.TryGetValue("digest", out int digestColumn))
            throw new InvalidDataException("deep-rematch anytime curve omits point digest authority");
        long[] plannedTotals = new long[axes.Count];
        long[] actualTotals = new long[axes.Count];
        string[]? selected = null;
        for (int rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            string[] row = lines[rowIndex].Split('\t');
            if (row.Length != header.Length)
                throw new InvalidDataException($"deep-rematch anytime curve row {rowIndex} is malformed");
            for (int axisIndex = 0; axisIndex < axes.Count; axisIndex++)
            {
                DeepRematchFuelAxis axis = axes[axisIndex];
                plannedTotals[axisIndex] = checked(plannedTotals[axisIndex] + ParseCurveLong(row, columns, "planned_" + axis.Name));
                actualTotals[axisIndex] = checked(actualTotals[axisIndex] + ParseCurveLong(row, columns, "actual_" + axis.Name));
            }
            if (acceptedPointDigest is not null && row[digestColumn] == acceptedPointDigest)
            {
                selected = row;
                break;
            }
        }
        if (acceptedPointDigest is not null && selected is null)
            throw new InvalidDataException("deep-rematch accepted anytime point is absent from its curve");
        string[] terminal = lines[^1].Split('\t');
        for (int i = 0; i < axes.Count; i++)
        {
            DeepRematchFuelAxis axis = axes[i];
            long planned = plannedTotals[i];
            long actual = actualTotals[i];
            string[] cumulativeRow = acceptedPointDigest is null ? terminal : selected!;
            long cumulative = ParseCurveLong(cumulativeRow, columns, "fuel_" + axis.Name);
            if (planned != axis.Planned || actual != axis.Actual || axis.Refund != planned - actual || cumulative != actual)
                throw new InvalidDataException($"deep-rematch fuel axis {axis.Name} disagrees with EML anytime settlement curve");
        }
    }

    private static long ParseCurveLong(string[] row, Dictionary<string, int> columns, string key)
        => columns.TryGetValue(key, out int column) && long.TryParse(row[column], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0
            ? value : throw new InvalidDataException($"deep-rematch anytime curve omits {key}");

    private static WindowReceipt ReadWindow(string path, long start, long count)
    {
        if (count <= 0 || start < 0) throw new InvalidDataException("deep-rematch evaluation window must be positive and nonnegative");
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < count + 1) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} is shorter than the complete evaluation window");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        int stepColumn = Array.IndexOf(header, "step");
        if (stepColumn < 0) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} omits step column");
        Dictionary<long, string[]> rows = new();
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex])) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} contains a blank data row");
            string[] row = lines[lineIndex].Split('\t');
            if (row.Length != header.Length || !long.TryParse(row[stepColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actual))
                throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} contains a malformed step row {lineIndex + 1}");
            if (actual < 0 || actual >= start + count)
                throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} contains step {actual} outside the registered run horizon 0..{start + count - 1}");
            if (!rows.TryAdd(actual, row))
                throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} duplicates step {actual}");
        }
        if (rows.Count != start + count) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} contains {rows.Count} rows, expected {start + count}");
        List<string> selected = new((int)count + 1) { string.Join('\t', header) };
        string[]? final = null;
        for (long expected = start; expected < start + count; expected++)
        {
            if (!rows.TryGetValue(expected, out string[]? row))
                throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} omits evaluation step {expected}");
            selected.Add(string.Join('\t', row));
            final = row;
        }
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++) values[header[i]] = final![i];
        return new WindowReceipt(values, count, Hash(string.Join('\n', selected)));
    }

    private static long CountStepRows(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} has no data rows");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        int stepColumn = Array.IndexOf(header, "step");
        if (stepColumn < 0) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} omits step column");
        HashSet<long> steps = new();
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex])) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} contains a blank data row");
            string[] row = lines[lineIndex].Split('\t');
            if (row.Length != header.Length || !long.TryParse(row[stepColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out long step) || step < 0 || !steps.Add(step))
                throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} contains a malformed or duplicate step row {lineIndex + 1}");
        }
        for (long expected = 0; expected < steps.Count; expected++)
            if (!steps.Contains(expected)) throw new InvalidDataException($"deep-rematch {Path.GetFileName(path)} omits step {expected}");
        return steps.Count;
    }

    private static bool HasExactReportField(string report, string key, string expected)
        => report.Split('\n').Count(line => line.TrimEnd('\r').Split('\t') is [var actualKey, var actualValue] && actualKey == key && actualValue == expected) == 1;

    private static void ValidateComputeReport(string report)
    {
        string[] lines = report.Split('\n');
        if (lines.Length < 8 || lines[0].TrimEnd('\r') != "Cortex compute accounting report")
            throw new InvalidDataException("deep-rematch compute report schema is not the typed Cortex report");
        HashSet<string> keys = new(StringComparer.Ordinal);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] fields = line.Split('\t');
            if (fields.Length != 2 || !keys.Add(fields[0]))
                throw new InvalidDataException("deep-rematch compute report contains duplicate or malformed fields");
        }
        string[] required = ["status", "records", "physical_records", "scored_records", "malformed_rows", "nonfinite_rows", "verification_failures", "total_wall_ms", "residual_ms"];
        if (!HasExactReportField(report, "status", "PASS")
            || required.Any(key => !keys.Contains(key))
            || !HasExactReportField(report, "malformed_rows", "0")
            || !HasExactReportField(report, "nonfinite_rows", "0")
            || !HasExactReportField(report, "verification_failures", "0"))
            throw new InvalidDataException("deep-rematch compute report omits the required status/records/timing fields");
    }

    private static DeepRematchArtifact CollectBaseline(string runDirectory, string runID, string checkpoint, string checkpointDigest, string registrationDigest, string curve, string compute, string rhythm)
    {
        Dictionary<string, string> final = ReadCurveFinalRow(curve);
        List<DeepRematchFuelAxis> axes = ReadHistoricalFuel(runDirectory, runID);
        string fuelPath = HistoricalFuelPath(runDirectory);
        string fuelEvidencePath = runID == "cortex_0098"
            ? RequireRunFile(runDirectory, "eml_anytime_curve.tsv")
            : RequireRunFile(runDirectory, "eml_actions.tsv");
        (long day, long dream, long aestivation) = ReadRhythmMetrics(rhythm);
        string curveDigest = FileDigest(curve);
        string computeDigest = FileDigest(compute);
        DeepRematchArtifact artifact = new()
        {
            schemaVersion = 1,
            runID = runID,
            historicalBaseline = true,
            registrationDigest = registrationDigest,
            checkpointDigest = checkpointDigest,
            collectorProvenanceDigest = "",
            vocabularyKnee = ParseInt(final, "eml.census.exact"),
            vocabularyFuelAxes = axes,
            evaluatorCalls = ParseLong(final, "eml.evaluator.calls"),
            certificates = ParseLong(final, "eml.census.certs"),
            fuelAxes = axes,
            computeStatus = "PASS",
            computeDarkResidual = 0,
            day = day,
            dream = dream,
            aestivation = aestivation,
            residual = ParseDouble(final, "eml.frontier.residual"),
            evaluationTopology = "historical-baseline",
            evaluationRows = 0,
            evaluationCurveDigest = curveDigest,
            evaluationComputeDigest = computeDigest,
            sourceDigests = [new DeepRematchSourceReceipt { path = Path.GetFileName(checkpoint), digest = checkpointDigest }, new DeepRematchSourceReceipt { path = Path.GetFileName(curve), digest = curveDigest }, new DeepRematchSourceReceipt { path = Path.GetFileName(compute), digest = computeDigest }, new DeepRematchSourceReceipt { path = Path.GetFileName(rhythm), digest = FileDigest(rhythm) }, new DeepRematchSourceReceipt { path = Path.GetFileName(fuelPath), digest = FileDigest(fuelPath) }, new DeepRematchSourceReceipt { path = Path.GetFileName(fuelEvidencePath), digest = FileDigest(fuelEvidencePath) }],
        };
        artifact.collectorProvenanceDigest = ComputeCollectorProvenanceDigest(artifact);
        artifact.artifactDigest = ComputeArtifactDigest(artifact);
        ValidateArtifact(artifact);
        return artifact;
    }

    private static List<DeepRematchFuelAxis> ReadHistoricalFuel(string runDirectory, string runID)
    {
        if (runID == "cortex_0098") return ReadKnownHistoricalFuel(runDirectory);
        if (runID == "cortex_0071")
        {
            string legacy = RequireRunFile(runDirectory, "eml_actions.tsv");
            string digest = FileDigest(legacy);
            return CanonicalFuelNames.Select(name => new DeepRematchFuelAxis(name, 0, 0, 0, "Unavailable",
                "cortex_0071 predates typed EmlDeliberationCounts planned/actual/refund receipts", digest)).ToList();
        }
        throw new InvalidDataException($"unsupported historical deep-rematch baseline {runID}");
    }
    private static List<DeepRematchFuelAxis> ReadKnownHistoricalFuel(string runDirectory)
    {
        string path = RequireRunFile(runDirectory, "eml_anytime_curve.tsv");
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException("historical baseline EML anytime curve has no rows");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        Dictionary<string, int> columns = header.Select((name, index) => (name, index)).ToDictionary(static x => x.name, static x => x.index, StringComparer.Ordinal);
        string[] rows = lines.Skip(1).Where(static line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (rows.Length != 10) throw new InvalidDataException($"historical baseline EML anytime curve has {rows.Length} rows, expected ten windows");
        Dictionary<string, long> planned = CanonicalFuelNames.ToDictionary(static name => name, static _ => 0L, StringComparer.Ordinal);
        Dictionary<string, long> actual = CanonicalFuelNames.ToDictionary(static name => name, static _ => 0L, StringComparer.Ordinal);
        string[]? terminal = null;
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            string line = rows[rowIndex];
            string[] fields = line.Split('\t');
            if (fields.Length != header.Length) throw new InvalidDataException("historical baseline EML anytime curve row width drifted");
            int prefixColumn = RequireColumn(columns, "prefix_step");
            if (!long.TryParse(fields[prefixColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out long prefixStep)
                || prefixStep != (rowIndex + 1) * 50L)
                throw new InvalidDataException("historical baseline EML anytime curve prefix steps are not the registered 50-step windows");
            for (int i = 0; i < CanonicalFuelNames.Length; i++)
            {
                string name = CanonicalFuelNames[i];
                long windowPlanned = ParseHistoricalLong(fields, columns, "planned_" + name);
                planned[name] = checked(planned[name] + windowPlanned);
                actual[name] = checked(actual[name] + ParseHistoricalLong(fields, columns, "actual_" + name));
                if (ParseHistoricalLong(fields, columns, "fuel_" + name) != actual[name])
                    throw new InvalidDataException($"historical baseline EML anytime curve cumulative {name} disagrees with its window actual sum");
            }
            if (prefixStep == 500) terminal = fields;
        }
        if (terminal is null) throw new InvalidDataException("historical baseline EML anytime curve omits terminal prefix step 500");
        string digest = FileDigest(path);
        return CanonicalFuelNames.Select(name =>
        {
            long refund = checked(planned[name] - actual[name]);
            if (refund < 0) throw new InvalidDataException($"historical baseline EML fuel axis {name} actual exceeds planned");
            return new DeepRematchFuelAxis(name, planned[name], actual[name], refund, "Known", "", digest);
        }).ToList();
    }
    private static string[] CanonicalFuelNames => EmlDeliberationCounts.AxisNames;
    private static int RequireColumn(Dictionary<string, int> columns, string name)
        => columns.TryGetValue(name, out int index) ? index : throw new InvalidDataException($"historical baseline EML anytime curve omits {name}");
    private static long ParseHistoricalLong(string[] fields, Dictionary<string, int> columns, string name)
        => long.TryParse(fields[RequireColumn(columns, name)], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0
            ? value : throw new InvalidDataException($"historical baseline EML anytime curve has invalid {name}");
    private static string HistoricalFuelPath(string runDirectory)
    {
        string journal = Path.Combine(runDirectory, "policy_trial_funding.journal.tsv");
        string legacy = Path.Combine(runDirectory, "policy_trial_funding.tsv");
        if (File.Exists(journal)) return journal;
        if (File.Exists(legacy)) return legacy;
        throw new InvalidDataException("historical baseline omits its fuel receipt");
    }
    private static Dictionary<string, string> ReadCurveFinalRow(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException("deep-rematch curve has no data rows");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        string[] row = lines[^1].Split('\t');
        if (header.Length != row.Length) throw new InvalidDataException("deep-rematch curve header/data width mismatch");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++) values[header[i]] = row[i];
        return values;
    }
    private static (long Day, long Replay, long ConsolidationPhase) ReadRhythmMetrics(string path)
    {
        return ParseRhythmMetrics(File.ReadAllLines(path), path);
    }

    private static (long Day, long Replay, long ConsolidationPhase) ParseRhythmMetrics(IReadOnlyList<string> lines, string source)
    {
        string[] censusLines = [.. lines.Where(static line => line.TrimStart().StartsWith("census:", StringComparison.Ordinal))];
        if (censusLines.Length != 1)
            throw new InvalidDataException($"deep-rematch rhythm must contain exactly one census line: {source}");

        MatchCollection fields = Regex.Matches(censusLines[0], @"\b(day|dream|aestivation)\s+([0-9]+)\b", RegexOptions.CultureInvariant);
        if (fields.Count != 3
            || fields[0].Groups[1].Value != "day"
            || fields[1].Groups[1].Value != "dream"
            || fields[2].Groups[1].Value != "aestivation")
            throw new InvalidDataException($"deep-rematch rhythm census has ambiguous or missing fields: {source}");

        return (
            ParseRhythmField(fields[0], source),
            ParseRhythmField(fields[1], source),
            ParseRhythmField(fields[2], source));
    }

    private static long ParseRhythmField(Match field, string source)
        => long.TryParse(field.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new InvalidDataException($"deep-rematch rhythm census has invalid numeric field: {source}");

    internal static void VerifyRhythmMetricsFixture()
    {
        const string text = "  census: day 195 (39 %) · dream 263 (52 %) · aestivation 43 (9 %) over 501 steps\n"
            + "  policy outcomes: 478 resolved (day 195 · dream 240 · aestivation 43)";
        (long day, long dream, long aestivation) = ParseRhythmMetrics(text.Split('\n'), "rhythm fixture");
        if (day != 195 || dream != 263 || aestivation != 43)
            throw new InvalidDataException("deep-rematch rhythm fixture crossed into policy outcomes");

        bool duplicateRejected = false;
        try { _ = ParseRhythmMetrics((text + "\n  census: day 1 · dream 2 · aestivation 3").Split('\n'), "rhythm duplicate fixture"); }
        catch (InvalidDataException) { duplicateRejected = true; }
        if (!duplicateRejected) throw new InvalidDataException("deep-rematch rhythm fixture accepted duplicate census lines");

        bool missingRejected = false;
        try { _ = ParseRhythmMetrics(["  census: day 195 · dream 263"], "rhythm missing fixture"); }
        catch (InvalidDataException) { missingRejected = true; }
        if (!missingRejected) throw new InvalidDataException("deep-rematch rhythm fixture accepted missing census fields");
    }
    private static Dictionary<string, string> ReadCurveWindowFinalRow(string path, long start, long steps)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException("deep-rematch curve has no data rows");
        string[] header = lines[0].TrimStart('\uFEFF').Split('\t');
        string[]? selected = null;
        long end = start + steps - 1;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] row = lines[i].Split('\t');
            if (row.Length != header.Length || !long.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long step)) continue;
            if (step >= start && step <= end) selected = row;
        }
        if (selected is null) throw new InvalidDataException($"deep-rematch curve has no complete evaluation window {start}..{end}");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++) values[header[i]] = selected[i];
        return values;
    }
    private static int ParseInt(Dictionary<string, string> values, string key) => int.Parse(ParseText(values, key), CultureInfo.InvariantCulture);
    private static long ParseLong(Dictionary<string, string> values, string key) => long.Parse(ParseText(values, key), CultureInfo.InvariantCulture);
    private static double ParseDouble(Dictionary<string, string> values, string key) => double.Parse(ParseText(values, key), CultureInfo.InvariantCulture);
    private static long ParseReportLong(string report, string key)
    {
        foreach (string line in report.Split('\n'))
        {
            string[] fields = line.Split('\t');
            if (fields.Length == 2 && fields[0] == key && long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) return value;
        }
        throw new InvalidDataException($"deep-rematch compute report omits {key}");
    }
    private static string ParseText(Dictionary<string, string> values, string key) => values.TryGetValue(key, out string? value) && value.Length > 0 && value != "nan" ? value : throw new InvalidDataException($"deep-rematch curve omits numeric receipt {key}");
    private static List<DeepRematchFuelAxis> ParseFuelAxes(DeepRematchFundingReceipt funding)
    {
        if (funding.Axes.Count != FuelAxisCount) throw new InvalidDataException("deep-rematch funding receipt must carry twelve axes");
        return [.. funding.Axes.Select(static axis => new DeepRematchFuelAxis(axis.Name, axis.Planned, axis.Actual, axis.Refund, axis.Availability, axis.Reason, axis.SourceDigest))];
    }
    private static void ValidateReceipt<T>(T receipt) where T : DeepRematchReceipt
    {
        if (string.IsNullOrWhiteSpace(receipt.ReadDigest())) throw new InvalidDataException("deep-rematch receipt digest is missing");
        if (receipt.ReadDigest() != receipt.ComputeDigest()) throw new InvalidDataException("deep-rematch receipt digest mismatch");
    }

    private static void ValidateReceipt<T>(T receipt, string expectedRunID, string checkpointDigest, string configDigest) where T : DeepRematchReceipt
    {
        ValidateReceipt(receipt);
        if (!string.Equals(receipt.RunID, expectedRunID, StringComparison.Ordinal)
            || !string.Equals(receipt.CheckpointDigest, checkpointDigest, StringComparison.Ordinal)
            || !string.Equals(receipt.ConfigDigest, configDigest, StringComparison.Ordinal)
            || !IsDigest(receipt.ProvenanceDigest))
            throw new InvalidDataException($"deep-rematch receipt {typeof(T).Name} is not bound to run/checkpoint/config/provenance");
    }

    internal static void ValidatePolicyArms(in PolicyBoundaryForkReceipt receipt)
    {
        receipt.Validate(HomeostatPolicyBoundaryDomain.Instance);
        if (receipt.Horizons.Length != 3 || receipt.Horizons[0] != 16 || receipt.Horizons[1] != 64 || receipt.Horizons[2] != 256
            || receipt.Arms.Length != 12)
            throw new InvalidDataException("deep-rematch policy receipt is not the registered four-arm 16/64/256 ladder");
        int terminal = receipt.Horizons[^1];
        foreach (PolicyBoundaryArmReceipt arm in receipt.Arms)
        {
            if (arm.Horizon != terminal) continue;
            if (!arm.ChildProcessCompleted)
                throw new InvalidDataException($"deep-rematch {arm.Arm} arm did not complete successfully");
            if (arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && !arm.BehaviorallyExecuted)
                throw new InvalidDataException("deep-rematch forced-null arm completed without behavioral execution");
            if (arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && !arm.Diverged)
                throw new InvalidDataException("deep-rematch forced-null arm completed without observed divergence");
            bool expectedAdaptation = arm.Arm != PolicyBoundaryArms.ReflexFrozenControl;
            if (arm.AdaptationEnabled != expectedAdaptation)
                throw new InvalidDataException($"deep-rematch {arm.Arm} arm adaptation state is not source-derived from its intervention rail");
            if (arm.Arm == PolicyBoundaryArms.ReflexFrozenControl)
            {
                if (arm.GrammarExecutionsDelta != 0)
                    throw new InvalidDataException("deep-rematch reflex control executed policy grammar");
                if (arm.TrialAdaptationTransitions != 0)
                    throw new InvalidDataException("deep-rematch reflex control recorded adaptation transitions");
            }
        }
    }

    internal static bool VerifyPolicyArmContractFixture()
    {
        bool valid = Accepts(CreateReceipt());
        bool forcedNullBehavior = Rejects(CreateReceipt(forcedNullBehaviorExecuted: false));
        bool reflexAdaptation = Rejects(CreateReceipt(reflexAdaptationEnabled: true));
        bool continuity = Rejects(CreateReceipt(continuityExact: false));
        bool canonicalOmission = Rejects(CreateReceipt(omitCanonicalScope: true));
        bool canonicalTamper = Rejects(CreateReceipt(foreignCanonicalScope: true));
        bool policyRow = VerifyPolicyRowContract();
        return valid && forcedNullBehavior && reflexAdaptation && continuity && canonicalOmission && canonicalTamper && policyRow;

        static bool Accepts(in PolicyBoundaryForkReceipt receipt)
        {
            try { ValidatePolicyArms(in receipt); return true; }
            catch (InvalidDataException) { return false; }
        }

        static bool Rejects(in PolicyBoundaryForkReceipt receipt) => !Accepts(in receipt);

        static bool VerifyPolicyRowContract()
        {
            PolicyBoundaryForkReceipt source = CreateReceipt();
            string[] validFields = EncodePolicyRow(Homeostat.PolicyID, in source);
            bool valid = TryParsePolicyBoundaryRow(validFields, out PolicyBoundaryForkReceipt parsed)
                && Accepts(in parsed);

            string[] missingPolicy = [.. validFields];
            missingPolicy[1] = "";
            bool missingRejected = !TryParsePolicyBoundaryRow(missingPolicy, out _);

            string[] foreignPolicy = [.. validFields];
            foreignPolicy[1] = "foreign-policy";
            bool foreignRejected = !TryParsePolicyBoundaryRow(foreignPolicy, out _);

            PolicyBoundaryArmReceipt[] mixedArms = [.. source.Arms];
            mixedArms[0] = mixedArms[0] with
            {
                ExecutedCanonicalState = new PolicyCanonicalStateID(
                    new CortexPolicyID("foreign-policy"), PolicyCanonicalStateKinds.Homeostat, 1, 0x205UL),
            };
            PolicyBoundaryForkReceipt mixed = source with { Arms = mixedArms };
            bool mixedRejected = !TryParsePolicyBoundaryRow(EncodePolicyRow(Homeostat.PolicyID, in mixed), out _);
            return valid && missingRejected && foreignRejected && mixedRejected;
        }

        static string[] EncodePolicyRow(CortexPolicyID policy, in PolicyBoundaryForkReceipt receipt)
        {
            string armEvidence = string.Join(';', receipt.Arms.Select(static arm =>
                string.Join(',', (byte)arm.Arm, arm.Horizon, arm.PaidCloseDelta, arm.MatchedSpend,
                    arm.ContinuityExact ? 1 : 0, arm.ChildProcessCompleted ? 1 : 0, arm.GrammarExecutionsDelta, arm.TrialAdaptationTransitions, arm.AdaptationEnabled ? 1 : 0,
                    (byte)arm.ExecutionOutcome, arm.RequestCount, arm.GuardAdmittedCount, arm.LastRequestDecisionID.Value, arm.LastRequestStep,
                    arm.LastRequestReadout.LaunchpadAction, arm.LastRequestReadout.RawCandidateAction, arm.LastRequestReadout.SelectedCandidateAction,
                    arm.LastRequestReadout.ExecutedAction, (byte)arm.LastRequestReadout.Authority, arm.LastRequestReadout.GrammarRevision.Value,
                    (byte)arm.LastRequestReadout.SelectionCause, arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture),
                    arm.LastRequestReadout.ReadoutCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    arm.ExecutedDecisionID.Value, arm.ExecutedStep, arm.ExecutedLaunchpadAction, arm.ExecutedRawCandidateAction, arm.ExecutedSelectedCandidateAction,
                    arm.ExecutedAction, (byte)arm.ExecutedAuthority, (byte)arm.ExecutedSelectionCause,
                    arm.ExecutedReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), arm.ExecutedReadoutRevision,
                    arm.ExecutedReadoutOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture), arm.ExecutedCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    arm.ExecutedCanonicalState.Version == 0 ? "" : arm.ExecutedCanonicalState.Policy.Value,
                    (byte)arm.ExecutedCanonicalState.Kind, arm.ExecutedCanonicalState.Version,
                    arm.ExecutedCanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture), arm.ExecutedDecisionEventID.Value,
                    arm.ForcedDivergenceSeed.ToString("X16", CultureInfo.InvariantCulture), arm.Diverged ? 1 : 0)));
            return
            [
                "1",
                policy.Value,
                receipt.Obligation.Value,
                receipt.CandidateBoundary.ToString(),
                receipt.BaselineBoundary.ToString(),
                string.Join(',', receipt.Horizons),
                receipt.ContinuityExact ? "1" : "0",
                receipt.MatchedSpend ? "1" : "0",
                receipt.ForcedNullBehaviorExecuted ? "1" : "0",
                receipt.Verified ? "1" : "0",
                armEvidence,
                PolicyBoundaryObligation.ComputeReceiptDigest(in receipt),
                receipt.SourceDecisionReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                receipt.SourceDecisionCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                receipt.SourceDecisionReadoutRevision.ToString(CultureInfo.InvariantCulture),
                receipt.QuotaDecisionID.Value.ToString("X16", CultureInfo.InvariantCulture),
            ];
        }

        static PolicyBoundaryForkReceipt CreateReceipt(
            bool forcedNullBehaviorExecuted = true,
            bool reflexAdaptationEnabled = false,
            bool continuityExact = true,
            bool omitCanonicalScope = false,
            bool foreignCanonicalScope = false)
        {
            int[] horizons = [16, 64, 256];
            PolicyBoundaryArmReceipt[] arms = new PolicyBoundaryArmReceipt[horizons.Length * 4];
            for (int i = 0; i < horizons.Length; i++)
            {
                int horizon = horizons[i];
                arms[i * 4] = CreateArm(PolicyBoundaryArms.Baseline, horizon, 1, true);
                arms[i * 4 + 1] = CreateArm(PolicyBoundaryArms.Candidate, horizon, 2, true);
                arms[i * 4 + 2] = CreateArm(PolicyBoundaryArms.ForcedDivergentNull, horizon, 3, forcedNullBehaviorExecuted);
                arms[i * 4 + 3] = CreateArm(PolicyBoundaryArms.ReflexFrozenControl, horizon, 4, true);
            }
            return new(
                new PolicyBoundaryObligationID("fixture-policy-arm-contract"),
                PolicyBoundaryRational.Zero,
                new PolicyBoundaryRational(1, 1),
                horizons,
                arms,
                continuityExact,
                MatchedSpend: true,
                ForcedNullBehaviorExecuted: forcedNullBehaviorExecuted,
                Verified: forcedNullBehaviorExecuted,
                SourceDecisionReadoutFingerprint: 1,
                SourceDecisionReadoutRevision: 1)
            {
                SourceDecisionCandidateFingerprint = 2,
            };

            PolicyBoundaryArmReceipt CreateArm(PolicyBoundaryArms arm, int horizon, int decision, bool behaviorallyExecuted)
            {
                CortexPolicyAuthorities authority = arm switch
                {
                    PolicyBoundaryArms.Baseline => CortexPolicyAuthorities.Launchpad,
                    PolicyBoundaryArms.ReflexFrozenControl => CortexPolicyAuthorities.Shadow,
                    _ => CortexPolicyAuthorities.Grammar,
                };
                CortexPolicySelectionCauses cause = arm switch
                {
                    PolicyBoundaryArms.Baseline => CortexPolicySelectionCauses.Launchpad,
                    PolicyBoundaryArms.Candidate => CortexPolicySelectionCauses.GrammarCandidate,
                    PolicyBoundaryArms.ForcedDivergentNull => CortexPolicySelectionCauses.TrialOverride,
                    _ => CortexPolicySelectionCauses.ShadowCandidate,
                };
                int rawCandidate = cause == CortexPolicySelectionCauses.Launchpad ? -1 : 1;
                int selectedCandidate = cause == CortexPolicySelectionCauses.Launchpad ? -1 : arm == PolicyBoundaryArms.ForcedDivergentNull ? 2 : 1;
                int action = cause is CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate
                    ? 0 : selectedCandidate;
                ulong decisionID = checked((ulong)(horizon * 10 + decision));
                bool adaptationEnabled = arm == PolicyBoundaryArms.ReflexFrozenControl ? reflexAdaptationEnabled : true;
                PolicyCanonicalStateID canonical = omitCanonicalScope
                    ? default
                    : new PolicyCanonicalStateID(foreignCanonicalScope ? new CortexPolicyID("foreign-policy") : Homeostat.PolicyID,
                        PolicyCanonicalStateKinds.Homeostat, 1, 0x205UL);
                CortexPolicyDecisionReadout requestReadout = new(0, rawCandidate, selectedCandidate, action,
                    authority, new GrammarRevisionID(1), cause,
                    cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 2UL,
                    cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 2UL);
                if (!behaviorallyExecuted)
                {
                    return new PolicyBoundaryArmReceipt(arm, horizon, 1, horizon, continuityExact, true, 0,
                        arm == PolicyBoundaryArms.ReflexFrozenControl ? 0 : 1, adaptationEnabled)
                    {
                        ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.GuardDenied,
                        RequestCount = 1,
                        GuardAdmittedCount = 0,
                        LastRequestDecisionID = new CortexPolicyDecisionID(decisionID),
                        LastRequestStep = 1,
                        LastRequestReadout = requestReadout,
                    };
                }
                return new PolicyBoundaryArmReceipt(arm, horizon, 1, horizon, continuityExact, true, 0,
                    arm == PolicyBoundaryArms.ReflexFrozenControl ? 0 : 1, adaptationEnabled)
                {
                    ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted,
                    RequestCount = 1,
                    GuardAdmittedCount = 1,
                    LastRequestDecisionID = new CortexPolicyDecisionID(decisionID),
                    LastRequestStep = 1,
                    LastRequestReadout = requestReadout,
                    ExecutedDecisionID = new CortexPolicyDecisionID(decisionID),
                    ExecutedStep = 1,
                    ExecutedLaunchpadAction = 0,
                    ExecutedRawCandidateAction = rawCandidate,
                    ExecutedSelectedCandidateAction = selectedCandidate,
                    ExecutedAction = action,
                    ExecutedAuthority = authority,
                    ExecutedSelectionCause = cause,
                    ExecutedReadoutFingerprint = 1,
                    ExecutedReadoutRevision = 1,
                    ExecutedReadoutOccurrenceDigest = cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 2UL,
                    ExecutedCandidateFingerprint = cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 2UL,
                    ExecutedCanonicalState = canonical,
                    ExecutedDecisionEventID = cause == CortexPolicySelectionCauses.TrialOverride ? new TapeEventID(checked((long)(decisionID + 100UL))) : default,
                    ForcedDivergenceSeed = cause == CortexPolicySelectionCauses.TrialOverride ? 0xD1E3UL : 0,
                    Diverged = cause == CortexPolicySelectionCauses.TrialOverride
                        && action != 0
                        && action != rawCandidate,
                };
            }
        }
    }

    private static bool IsDigest(string value)
        => value.Length == 64 && value.All(static ch => char.IsAsciiHexDigit(ch));

    internal static void BindRun(Run run, string registrationPath, string registrationDigest)
    {
        if (string.IsNullOrWhiteSpace(registrationDigest)) return;
        string destination = run.PathOf("deep-rematch-gate.ron");
        byte[] bytes;
        if (File.Exists(destination))
        {
            bytes = File.ReadAllBytes(destination);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(registrationPath) || !File.Exists(registrationPath))
                throw new InvalidDataException("deep-rematch gate registration is absent from a fresh run");
            bytes = File.ReadAllBytes(registrationPath);
            File.WriteAllBytes(destination, bytes);
        }
        DeepRematchGateConfig config = DecodeConfig(bytes);
        if (!string.Equals(config.ConfigDigest, registrationDigest, StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch gate registration digest changed after launch");
        File.WriteAllText(run.PathOf("deep-rematch-gate.digest"), registrationDigest + "\n", Encoding.UTF8);
    }

    internal static string ComputeArtifactDigest(DeepRematchArtifact artifact)
        => Hash(IsLegacyV1Artifact(artifact) ? ArtifactCanonicalV1(artifact) : ArtifactCanonical(artifact));

    internal static string ComputeConfigDigest(DeepRematchGateConfig config)
        => Hash(ConfigCanonical(config));

    internal static string ComputeReportDigest(DeepRematchGateReport report)
        => Hash(ReportCanonical(report));

    internal static string GetVerdictName(int line)
        => line is >= 1 and <= LineCount ? VerdictNames[line - 1] : throw new ArgumentOutOfRangeException(nameof(line));

    private static void Evaluate(List<DeepRematchVerdictRecord> verdicts, List<DeepRematchBankedNullRecord> banked,
        int line, string name, bool passed, string evidenceDigest, string detail)
    {
        string status = passed ? "PASS" : "BANKED_NULL";
        DeepRematchVerdictRecord verdict = new(line, name, status, evidenceDigest, detail,
            Hash(string.Join('|', line, name, status, evidenceDigest, detail)));
        verdicts.Add(verdict);
        if (!passed)
            banked.Add(new DeepRematchBankedNullRecord(name, evidenceDigest, detail,
                Hash(string.Join('|', "banked-null", name, evidenceDigest, detail))));
    }

    private static void ValidateConfig(DeepRematchGateConfig config)
    {
        if (config.SchemaVersion != SchemaVersion) throw new InvalidDataException($"unsupported deep-rematch config schema {config.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(config.GateID) || string.IsNullOrWhiteSpace(config.ConfigDigest)) throw new InvalidDataException("deep-rematch config identity is incomplete");
        if (config.CertificatesDenominator != "certificates") throw new InvalidDataException("deep-rematch denominator must be certificates");
        if (config.MatchedFuelAxes.Count != FuelAxisCount) throw new InvalidDataException("deep-rematch config must freeze twelve fuel axes");
        ValidateFuelAxes(config.MatchedFuelAxes, "config matched");
        if (config.MatchedFuelAxes.Any(static axis => axis.Availability != "Known")) throw new InvalidDataException("deep-rematch matched fuel requires twelve known conserved axes");
        if (config.OrganismBand <= 0 || config.ResidualMultiplier <= 0 || config.MinimumPaidCloses < 1) throw new InvalidDataException("deep-rematch organism thresholds are invalid");
        if (config.ComputeDarkTolerance < 0) throw new InvalidDataException("deep-rematch compute dark tolerance cannot be negative");
        if (config.A3PreludeSteps != 1280 || config.EvaluationSteps != 500) throw new InvalidDataException("deep-rematch gate requires prelude=1280 and evaluation=500");
        if (config.Baseline0098Day <= 0 || config.Baseline0098Replay <= 0 || config.Baseline0098ConsolidationPhase <= 0 || config.Baseline0098Residual <= 0)
            throw new InvalidDataException("deep-rematch baseline organism metrics must be derived from cortex_0098 artifact");
        if (!string.Equals(config.ConfigDigest, ComputeConfigDigest(config), StringComparison.Ordinal)) throw new InvalidDataException("deep-rematch config digest mismatch");
    }

    private static void ValidateArtifact(DeepRematchArtifact artifact)
    {
        bool legacyBaseline = IsLegacyV1Artifact(artifact);
        if ((!legacyBaseline && artifact.SchemaVersion != 2)
            || string.IsNullOrWhiteSpace(artifact.RunID) || string.IsNullOrWhiteSpace(artifact.ArtifactDigest)) throw new InvalidDataException("deep-rematch artifact identity is incomplete");
        if (!legacyBaseline && !artifact.HistoricalBaseline && (artifact.Rung0AssayStatus is not nameof(EmlRematchAssayStatuses.Exact) and not nameof(EmlRematchAssayStatuses.Invalid)
            || artifact.Rung0ShadowPowerStatus is not nameof(EmlRematchPowerStatuses.Powered) and not nameof(EmlRematchPowerStatuses.Unpowered)
            || artifact.Rung0NullPowerStatus is not nameof(EmlRematchPowerStatuses.Powered) and not nameof(EmlRematchPowerStatuses.Unpowered)
            || string.IsNullOrWhiteSpace(artifact.Rung0AssayDetail)
            || string.IsNullOrWhiteSpace(artifact.Rung0ShadowPowerDetail)
            || string.IsNullOrWhiteSpace(artifact.Rung0NullPowerDetail)))
            throw new InvalidDataException("deep-rematch artifact rung0 assay/power status is incomplete");
        if (!legacyBaseline && !artifact.HistoricalBaseline && artifact.Rung0AssayStatus != nameof(EmlRematchAssayStatuses.Exact))
            throw new InvalidDataException("deep-rematch invalid assay cannot become an adjudicable artifact");
        if (!IsDigest(artifact.RegistrationDigest) || !IsDigest(artifact.CheckpointDigest)
            || string.IsNullOrWhiteSpace(artifact.CollectorProvenanceDigest) || artifact.SourceDigests.Count == 0)
            throw new InvalidDataException("deep-rematch artifact lacks collector provenance and source digests");
        if (artifact.Certificates < 0 || artifact.EvaluatorCalls < 0 || artifact.Executions < 0 || artifact.PaidCloses < 0
            || artifact.Rung0ComposedPredictions < 0 || artifact.Rung0EvaluatorCalls < 0 || artifact.Rung0AuditFailures < 0
            || artifact.RelationNullExecutions < 0 || artifact.RelationNullAuthorityPredictions < 0
            || artifact.PolicyReadoutPaidCloses < 0 || artifact.PolicyTreeEraPaidCloses < 0
            || artifact.PolicyReadoutSpend < 0 || artifact.PolicyTreeEraSpend < 0 || artifact.A3PaidArms < 0 || artifact.A3Spend < 0
            || artifact.TrialPlannedSteps < 0 || artifact.TrialActualSteps < 0 || artifact.TrialRefundSteps < 0
            || artifact.ReadoutPlannedSteps < 0 || artifact.ReadoutActualSteps < 0 || artifact.ReadoutRefundSteps < 0
            || artifact.A3ReceiptStep < 0 || artifact.EvaluationRows < 0
            || artifact.ComputeReportRecords < 0
            || !double.IsFinite(artifact.ComputeDarkResidual) || artifact.ComputeDarkResidual < 0
            || !double.IsFinite(artifact.Residual) || artifact.Residual < 0)
            throw new InvalidDataException("deep-rematch artifact counters cannot be negative or non-finite");
        if (artifact.FuelAxes.Count != FuelAxisCount || artifact.VocabularyFuelAxes.Count != FuelAxisCount) throw new InvalidDataException("deep-rematch artifact must carry twelve fuel axes");
        ValidateFuelAxes(artifact.FuelAxes, "run");
        ValidateFuelAxes(artifact.VocabularyFuelAxes, "vocabulary");
        if (!artifact.HistoricalBaseline && (artifact.FuelAxes.Any(static axis => axis.Availability != "Known") || artifact.VocabularyFuelAxes.Any(static axis => axis.Availability != "Known")))
            throw new InvalidDataException("deep-rematch live artifact requires twelve known conserved fuel axes");
        if (artifact.TrialPlannedSteps != artifact.TrialActualSteps + artifact.TrialRefundSteps
            || artifact.ReadoutPlannedSteps != artifact.ReadoutActualSteps + artifact.ReadoutRefundSteps)
            throw new InvalidDataException("deep-rematch trial/readout currencies do not close");
        bool historicalBaseline = artifact.HistoricalBaseline
            && artifact.EvaluationTopology == "historical-baseline"
            && artifact.EvaluationStartStep == 0
            && artifact.EvaluationEndStep == 0
            && artifact.EvaluationRows == 0
            && artifact.ComputeReportRecords == 0;
        bool monolithicHandshake = artifact.EvaluationTopology == "monolithic-handshake"
            && artifact.EvaluationStartStep == 1281
            && artifact.EvaluationEndStep == 1780
            && artifact.EvaluationRows == 500
            && artifact.ComputeReportRecords == 1781;
        bool compositeLocal = artifact.EvaluationTopology == "composite-local"
            && artifact.EvaluationStartStep == 1
            && artifact.EvaluationEndStep == 500
            && artifact.EvaluationRows == 500
            && artifact.ComputeReportRecords == 501;
        if (!historicalBaseline && !monolithicHandshake && !compositeLocal)
            throw new InvalidDataException("deep-rematch artifact evaluation topology or physical/scored horizon is invalid");
        if (!artifact.HistoricalBaseline && (string.IsNullOrWhiteSpace(artifact.Rung0ReceiptDigest)
            || string.IsNullOrWhiteSpace(artifact.CheckpointReceiptDigest)
            || string.IsNullOrWhiteSpace(artifact.FundingReceiptDigest)
            || string.IsNullOrWhiteSpace(artifact.PolicyReceiptDigest)
            || string.IsNullOrWhiteSpace(artifact.A3ReceiptProvenanceDigest)))
            throw new InvalidDataException("deep-rematch live artifact omits typed section receipt identities");
        if (!artifact.HistoricalBaseline && (artifact.ComputeReportRecords <= 0 || string.IsNullOrWhiteSpace(artifact.ComputeReportDigest)))
            throw new InvalidDataException("deep-rematch live artifact omits compute report binding");
        if (!artifact.HistoricalBaseline && (artifact.EvaluationRows <= 0 || artifact.EvaluationEndStep != artifact.EvaluationStartStep + artifact.EvaluationRows - 1))
            throw new InvalidDataException("deep-rematch artifact evaluation window is not contiguous");
        foreach (DeepRematchSourceReceipt source in artifact.SourceDigests)
            if (string.IsNullOrWhiteSpace(source.Path) || !IsDigest(source.Digest))
                throw new InvalidDataException("deep-rematch artifact source digest is incomplete");
        if (artifact.SourceDigests.Select(static source => source.Path).Distinct(StringComparer.Ordinal).Count() != artifact.SourceDigests.Count)
            throw new InvalidDataException("deep-rematch artifact source digest paths are duplicated");
        if (!string.Equals(artifact.CollectorProvenanceDigest, ComputeCollectorProvenanceDigest(artifact), StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch collector provenance digest mismatch");
        if (!string.Equals(artifact.ArtifactDigest, ComputeArtifactDigest(artifact), StringComparison.Ordinal)) throw new InvalidDataException($"deep-rematch artifact {artifact.RunID} digest mismatch");
    }

    private static void ValidateReport(DeepRematchGateReport report)
    {
        if (report.SchemaVersion != SchemaVersion || report.Verdicts.Count != LineCount
            || string.IsNullOrWhiteSpace(report.GateID) || string.IsNullOrWhiteSpace(report.ConfigDigest)
            || string.IsNullOrWhiteSpace(report.RunID) || string.IsNullOrWhiteSpace(report.RunArtifactDigest))
            throw new InvalidDataException("deep-rematch report identity is incomplete");
        for (int i = 0; i < LineCount; i++)
        {
            DeepRematchVerdictRecord verdict = report.Verdicts[i];
            if (verdict.Line != i + 1 || verdict.Name != VerdictNames[i] || (verdict.Status != "PASS" && verdict.Status != "BANKED_NULL"))
                throw new InvalidDataException("deep-rematch report verdict identities are not the exact seven-line contract");
            if (!string.Equals(verdict.Digest, Hash(string.Join('|', verdict.Line, verdict.Name, verdict.Status, verdict.EvidenceDigest, verdict.Detail)), StringComparison.Ordinal))
                throw new InvalidDataException("deep-rematch verdict digest mismatch");
        }
        foreach (DeepRematchBankedNullRecord banked in report.BankedNulls)
            if (!string.Equals(banked.Digest, Hash(string.Join('|', "banked-null", banked.Name, banked.ReceiptDigest, banked.Detail)), StringComparison.Ordinal))
                throw new InvalidDataException("deep-rematch banked-null digest mismatch");
        List<DeepRematchVerdictRecord> failed = report.Verdicts.Where(static verdict => verdict.Status == "BANKED_NULL").ToList();
        HashSet<string> bankKeys = report.BankedNulls.Select(static banked => banked.Name + "|" + banked.ReceiptDigest + "|" + banked.Detail).ToHashSet(StringComparer.Ordinal);
        if (report.BankedNulls.Count != failed.Count
            || bankKeys.Count != report.BankedNulls.Count
            || report.BankedNulls.Any(banked => !failed.Any(verdict => verdict.Name == banked.Name
                && verdict.EvidenceDigest == banked.ReceiptDigest && verdict.Detail == banked.Detail)))
            throw new InvalidDataException("deep-rematch banked-null list does not exactly match verdict lines");
        if (!string.Equals(report.ReportDigest, ComputeReportDigest(report), StringComparison.Ordinal)) throw new InvalidDataException("deep-rematch report digest mismatch");
    }

    private static void RequireArtifact(DeepRematchArtifact artifact, string expectedRunID)
    {
        ValidateArtifact(artifact);
        if (!string.Equals(artifact.RunID, expectedRunID, StringComparison.Ordinal)) throw new InvalidDataException($"expected deep-rematch artifact {expectedRunID}, got {artifact.RunID}");
    }

    private static bool SameAxes(List<DeepRematchFuelAxis> left, List<DeepRematchFuelAxis> right)
        => left.Count == right.Count && left.Select(static x => string.Join(':', x.Name, x.Availability, x.Planned, x.Actual, x.Refund))
            .SequenceEqual(right.Select(static x => string.Join(':', x.Name, x.Availability, x.Planned, x.Actual, x.Refund)), StringComparer.Ordinal);

    private static void ValidateFuelAxes(List<DeepRematchFuelAxis> axes, string owner)
    {
        foreach (DeepRematchFuelAxis axis in axes)
        {
            if (string.IsNullOrWhiteSpace(axis.Name) || axis.Planned < 0 || axis.Actual < 0 || axis.Refund < 0
                || !IsDigest(axis.SourceDigest))
                throw new InvalidDataException($"deep-rematch {owner} fuel axis is negative or unnamed");
            if (axis.Availability == "Unavailable")
            {
                if (axis.Planned != 0 || axis.Actual != 0 || axis.Refund != 0 || string.IsNullOrWhiteSpace(axis.Reason))
                    throw new InvalidDataException($"deep-rematch {owner} unavailable fuel axis must carry an explicit reason and no fabricated counts");
                continue;
            }
            if (axis.Availability != "Known") throw new InvalidDataException($"deep-rematch {owner} fuel axis availability is not closed");
            if (axis.Planned != axis.Actual + axis.Refund)
                throw new InvalidDataException($"deep-rematch {owner} fuel axis {axis.Name} does not close");
        }
    }

    private static double Rate(long calls, long certificates) => certificates == 0 ? double.PositiveInfinity : (double)calls / certificates;
    private static bool InBand(long value, long baseline, double band) => value >= baseline * (1.0 - band) && value <= baseline * (1.0 + band);
    private static List<DeepRematchFuelAxis> CreateDefaultFuelAxes() => CanonicalFuelNames.Select(name => new DeepRematchFuelAxis(name, 100, 100, 0)).ToList();
    private static List<DeepRematchFuelAxis> CreateFuelAxes(bool passing) => CanonicalFuelNames.Select(name => new DeepRematchFuelAxis(name, 100, passing ? 100 : 99, passing ? 0 : 1)).ToList();
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string FuelAxisCanonical(DeepRematchFuelAxis x) => string.Join(':', x.Name, x.Availability, x.Planned, x.Actual, x.Refund, x.Reason, x.SourceDigest);
    private static bool IsHistoricalV1Identity(DeepRematchArtifact artifact)
        => artifact.SchemaVersion == 1
            && artifact.HistoricalBaseline
            && artifact.RunID is "cortex_0071" or "cortex_0098"
            && artifact.Rung0AssayStatus.Length == 0
            && artifact.Rung0AssayDetail.Length == 0
            && artifact.Rung0ShadowPowerStatus.Length == 0
            && artifact.Rung0ShadowPowerDetail.Length == 0
            && artifact.Rung0NullPowerStatus.Length == 0
            && artifact.Rung0NullPowerDetail.Length == 0;

    private static bool IsLegacyV1Artifact(DeepRematchArtifact artifact)
        => IsHistoricalV1Identity(artifact)
            && artifact.EvaluationTopology == "historical-baseline"
            && artifact.EvaluationStartStep == 0
            && artifact.EvaluationEndStep == 0
            && artifact.EvaluationRows == 0
            && artifact.ComputeReportRecords == 0;

    private static bool HasSerializedV2OnlyField(ReadOnlySpan<byte> bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        bool quoted = false;
        bool escaped = false;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"')
            {
                quoted = true;
                continue;
            }
            foreach (string v2Field in V2OnlyArtifactFields)
            {
                if (!text.AsSpan(i).StartsWith(v2Field, StringComparison.Ordinal)
                    || i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_'))
                    continue;
                int colon = i + v2Field.Length;
                while (colon < text.Length && char.IsWhiteSpace(text[colon])) colon++;
                if (colon < text.Length && text[colon] == ':')
                    return true;
            }
        }
        return false;
    }

    internal static bool IsSerializedV2OnlyFieldForFixture(string line)
    {
        string field = line.TrimStart();
        return V2OnlyArtifactFields.Any(v2Field => field.StartsWith(v2Field + ':', StringComparison.Ordinal));
    }

    private static readonly string[] V2OnlyArtifactFields =
    [
        "rung0_assay_status",
        "rung0_assay_detail",
        "rung0_shadow_power_status",
        "rung0_shadow_power_detail",
        "rung0_null_power_status",
        "rung0_null_power_detail",
    ];

    private static string ConfigCanonical(DeepRematchGateConfig c) => string.Join('|', c.SchemaVersion, c.GateID, c.Baseline0071ArtifactDigest, c.Baseline0098ArtifactDigest, c.Baseline0071RunID, c.Baseline0098RunID, c.Baseline0071Certificates, c.Baseline0071EvaluatorCalls, c.Baseline0098Certificates, c.Baseline0098EvaluatorCalls, c.CertificatesDenominator, c.FrontierKneeExclusive, c.OrganismBand.ToString("G17", CultureInfo.InvariantCulture), c.ResidualMultiplier.ToString("G17", CultureInfo.InvariantCulture), c.ComputeDarkTolerance.ToString("G17", CultureInfo.InvariantCulture), c.Baseline0098Day, c.Baseline0098Replay, c.Baseline0098ConsolidationPhase, c.Baseline0098Residual.ToString("G17", CultureInfo.InvariantCulture), c.MinimumPaidCloses, c.MaximumPaidCloseExecutionsRatio.ToString("G17", CultureInfo.InvariantCulture), c.A3PreludeSteps, c.EvaluationSteps, string.Join(',', c.MatchedFuelAxes.Select(FuelAxisCanonical)), "config-v1");
    private static string ArtifactCanonicalV1(DeepRematchArtifact a) => string.Join('|', a.SchemaVersion, a.RunID, a.RegistrationDigest, a.CheckpointDigest, a.CollectorProvenanceDigest, a.HistoricalBaseline, a.VocabularyKnee, string.Join(',', a.VocabularyFuelAxes.Select(FuelAxisCanonical)), a.EvaluatorCalls, a.Certificates, a.Rung0ComposedPredictions, a.Rung0EvaluatorCalls, a.Rung0AuditFailures, a.RelationNullExecutions, a.RelationNullAuthorityPredictions, a.Rung0ReceiptDigest, a.PolicyReadoutPaidCloses, a.PolicyTreeEraPaidCloses, a.PolicyReadoutSpend, a.PolicyTreeEraSpend, a.A3PaidArms, a.A3HorizonShort, a.A3HorizonMedium, a.A3HorizonLong, a.A3Spend, a.A3ReceiptProvenanceDigest, a.CheckpointReceiptDigest, a.FundingReceiptDigest, a.PolicyReceiptDigest, a.TrialPlannedSteps, a.TrialActualSteps, a.TrialRefundSteps, a.ReadoutPlannedSteps, a.ReadoutActualSteps, a.ReadoutRefundSteps, a.PolicyNullDivergentExecutions, a.ReflexControlAdaptations, a.SaveLoadSaveMismatches, a.A3ReceiptStep, a.EvaluationTopology, a.EvaluationStartStep, a.EvaluationEndStep, a.EvaluationRows, a.EvaluationCurveDigest, a.EvaluationComputeDigest, a.ComputeReportDigest, a.ComputeReportRecords, string.Join(',', a.FuelAxes.Select(FuelAxisCanonical)), a.ComputeStatus, a.ComputeDarkResidual.ToString("G17", CultureInfo.InvariantCulture), a.Day, a.Replay, a.ConsolidationPhase, a.Residual.ToString("G17", CultureInfo.InvariantCulture), a.PaidCloses, a.Executions, a.BankedNull, string.Join(',', a.SourceDigests.Select(static x => x.Path + ':' + x.Digest)), "artifact-v1");
    private static string ArtifactCanonical(DeepRematchArtifact a) => string.Join('|', a.SchemaVersion, a.RunID, a.RegistrationDigest, a.CheckpointDigest, a.CollectorProvenanceDigest, a.HistoricalBaseline, a.VocabularyKnee, string.Join(',', a.VocabularyFuelAxes.Select(FuelAxisCanonical)), a.EvaluatorCalls, a.Certificates, a.Rung0ComposedPredictions, a.Rung0EvaluatorCalls, a.Rung0AuditFailures, a.RelationNullExecutions, a.RelationNullAuthorityPredictions, a.Rung0ReceiptDigest, a.Rung0AssayStatus, a.Rung0AssayDetail, a.Rung0ShadowPowerStatus, a.Rung0ShadowPowerDetail, a.Rung0NullPowerStatus, a.Rung0NullPowerDetail, a.PolicyReadoutPaidCloses, a.PolicyTreeEraPaidCloses, a.PolicyReadoutSpend, a.PolicyTreeEraSpend, a.A3PaidArms, a.A3HorizonShort, a.A3HorizonMedium, a.A3HorizonLong, a.A3Spend, a.A3ReceiptProvenanceDigest, a.CheckpointReceiptDigest, a.FundingReceiptDigest, a.PolicyReceiptDigest, a.TrialPlannedSteps, a.TrialActualSteps, a.TrialRefundSteps, a.ReadoutPlannedSteps, a.ReadoutActualSteps, a.ReadoutRefundSteps, a.PolicyNullDivergentExecutions, a.ReflexControlAdaptations, a.SaveLoadSaveMismatches, a.A3ReceiptStep, a.EvaluationTopology, a.EvaluationStartStep, a.EvaluationEndStep, a.EvaluationRows, a.EvaluationCurveDigest, a.EvaluationComputeDigest, a.ComputeReportDigest, a.ComputeReportRecords, string.Join(',', a.FuelAxes.Select(FuelAxisCanonical)), a.ComputeStatus, a.ComputeDarkResidual.ToString("G17", CultureInfo.InvariantCulture), a.Day, a.Replay, a.ConsolidationPhase, a.Residual.ToString("G17", CultureInfo.InvariantCulture), a.PaidCloses, a.Executions, a.BankedNull, string.Join(',', a.SourceDigests.Select(static x => x.Path + ':' + x.Digest)), "artifact-v2");
    private static string ReportCanonical(DeepRematchGateReport r) => string.Join('|', r.SchemaVersion, r.GateID, r.ConfigDigest, r.RunID, r.RunArtifactDigest, string.Join(';', r.Verdicts.Select(static v => string.Join(':', v.Line, v.Name, v.Status, v.EvidenceDigest, v.Detail, v.Digest))), string.Join(';', r.BankedNulls.Select(static b => string.Join(':', b.Name, b.ReceiptDigest, b.Detail, b.Digest))), "report-v1");
}

internal static class DeepRematchGateFixture
{
    internal static bool Run(TextWriter output, string? outputDirectory = null)
    {
        bool valid = false, failing = false, banked = false, tamperedConfig = false, tamperedArtifact = false, legacyArtifacts = false, legacyFieldCorruption = false, legacyDigestCorruption = false, legacyMixed = false, rung0Control = false, rung0ControlFailure = false, policyArms = false, checkpointDialect = false, railProjection = false;
        try
        {
            DeepRematchGate.VerifyRhythmMetricsFixture();
            DeepRematchArtifact baseline0071 = DeepRematchGate.CreateFixtureArtifact("cortex_0071", "baseline", true);
            baseline0071.evaluatorCalls = 100;
            baseline0071.artifactDigest = DeepRematchGate.ComputeArtifactDigest(baseline0071);
            DeepRematchArtifact baseline0098 = DeepRematchGate.CreateFixtureArtifact("cortex_0098", "baseline", true);
            baseline0098.evaluatorCalls = 100;
            baseline0098.artifactDigest = DeepRematchGate.ComputeArtifactDigest(baseline0098);
            DeepRematchGateConfig config = DeepRematchGate.CreateDefault(baseline0071, baseline0098);
            DeepRematchArtifact actual = DeepRematchGate.CreateFixtureArtifact("deep-rematch-fixture", config.ConfigDigest, true);
            DeepRematchGateReport report = DeepRematchGate.Adjudicate(config, baseline0071, baseline0098, actual);
            valid = report.Verdicts.All(static v => v.Status == "PASS") && report.BankedNulls.Count == 0;
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                DeepRematchGate.WriteArtifact(Path.Combine(outputDirectory, "cortex_0071.ron"), baseline0071);
                DeepRematchGate.WriteArtifact(Path.Combine(outputDirectory, "cortex_0098.ron"), baseline0098);
                DeepRematchGate.WritePrepared(Path.Combine(outputDirectory, "deep-rematch-gate.ron"), config);
                DeepRematchGate.WriteArtifact(Path.Combine(outputDirectory, "deep-rematch-run.ron"), actual);
                File.WriteAllBytes(Path.Combine(outputDirectory, "deep-rematch-verdicts.ron"), DeepRematchGate.EncodeReport(report));
                File.WriteAllText(Path.Combine(outputDirectory, "deep-rematch-verdicts.tsv"), DeepRematchGate.RenderTsv(report));
            }

            DeepRematchArtifact failed = DeepRematchGate.CreateFixtureArtifact("deep-rematch-failing", config.ConfigDigest, false);
            DeepRematchGateReport failedReport = DeepRematchGate.Adjudicate(config, baseline0071, baseline0098, failed);
            failing = failedReport.Verdicts.Any(static v => v.Status == "BANKED_NULL") && failedReport.BankedNulls.Count > 0;

            DeepRematchArtifact nullArm = DeepRematchGate.CreateFixtureArtifact("deep-rematch-null", config.ConfigDigest, false, bankedNull: true);
            DeepRematchGateReport nullReport = DeepRematchGate.Adjudicate(config, baseline0071, baseline0098, nullArm);
            banked = nullArm.BankedNull && nullReport.BankedNulls.Count >= 1;

            byte[] configBytes = DeepRematchGate.EncodeConfig(config);
            configBytes[^1] ^= 0x01;
            try { _ = DeepRematchGate.DecodeConfig(configBytes); }
            catch (Exception) { tamperedConfig = true; }

            byte[] artifactBytes = DeepRematchGate.EncodeArtifact(actual);
            artifactBytes[^1] ^= 0x01;
            try { _ = DeepRematchGate.DecodeArtifact(artifactBytes); }
            catch (Exception) { tamperedArtifact = true; }
            (legacyArtifacts, legacyFieldCorruption, legacyDigestCorruption, legacyMixed) = VerifyLegacyArtifactAdapter();
            rung0Control = DeepRematchGate.VerifyRung0ControlFixture();
            rung0ControlFailure = DeepRematchReceiptEmission.VerifyControlFailureFixture();
            policyArms = DeepRematchGate.VerifyPolicyArmContractFixture();
            checkpointDialect = DeepRematchCompositeRON.VerifyCheckpointDialectFixture();
            railProjection = DeepRematchCompositeRON.VerifyPolicyBoundaryRailProjectionFixture();
        }
        catch (Exception error)
        {
            output.WriteLine($"  deep-rematch fixture exception · {error.Message}");
        }
        output.WriteLine($"  deep-rematch fixture · valid={(valid ? "PASS" : "FAIL")} failing={(failing ? "PASS" : "FAIL")} banked-null={(banked ? "PASS" : "FAIL")} tampered-config={(tamperedConfig ? "PASS" : "FAIL")} tampered-artifact={(tamperedArtifact ? "PASS" : "FAIL")} legacy-0071/0098={(legacyArtifacts ? "PASS" : "FAIL")} legacy-field-corruption={(legacyFieldCorruption ? "PASS" : "FAIL")} legacy-digest-corruption={(legacyDigestCorruption ? "PASS" : "FAIL")} legacy-mixed={(legacyMixed ? "PASS" : "FAIL")} rung0-control={(rung0Control ? "PASS" : "FAIL")} rung0-control-failure={(rung0ControlFailure ? "PASS" : "FAIL")} policy-arms={(policyArms ? "PASS" : "FAIL")} checkpoint-dialect={(checkpointDialect ? "PASS" : "FAIL")} rail-projection={(railProjection ? "PASS" : "FAIL")}");
        return valid && failing && banked && tamperedConfig && tamperedArtifact && legacyArtifacts && legacyFieldCorruption && legacyDigestCorruption && legacyMixed && rung0Control && rung0ControlFailure && policyArms && checkpointDialect && railProjection;
    }
    private static (bool LegacyArtifacts, bool FieldCorruption, bool DigestCorruption, bool MixedShape) VerifyLegacyArtifactAdapter()
    {
        DeepRematchArtifact baseline0071 = CreateLegacyFixtureArtifact("cortex_0071");
        DeepRematchArtifact baseline0098 = CreateLegacyFixtureArtifact("cortex_0098");
        byte[] bytes0071 = CreateLegacyFixtureBytes(baseline0071);
        byte[] bytes0098 = CreateLegacyFixtureBytes(baseline0098);
        DeepRematchArtifact decoded0071 = DeepRematchGate.DecodeArtifact(bytes0071);
        DeepRematchArtifact decoded0098 = DeepRematchGate.DecodeArtifact(bytes0098);
        bool artifacts = decoded0071.ArtifactDigest == baseline0071.ArtifactDigest
            && decoded0098.ArtifactDigest == baseline0098.ArtifactDigest
            && decoded0071.ArtifactDigest == DeepRematchGate.ComputeArtifactDigest(decoded0071)
            && decoded0098.ArtifactDigest == DeepRematchGate.ComputeArtifactDigest(decoded0098);
        string trackedDirectory = Path.Combine("src", "Cogito", "Recursion", "LegacyArtifacts");
        string tracked0071Path = Path.Combine(trackedDirectory, "cortex_0071.ron");
        string tracked0098Path = Path.Combine(trackedDirectory, "cortex_0098.ron");
        if (!File.Exists(tracked0071Path) || !File.Exists(tracked0098Path))
            throw new InvalidDataException("deep-rematch v1 regression fixtures are missing from the source tree");
        byte[] tracked0071Bytes = File.ReadAllBytes(tracked0071Path);
        byte[] tracked0098Bytes = File.ReadAllBytes(tracked0098Path);
        DeepRematchArtifact tracked0071 = DeepRematchGate.DecodeArtifact(tracked0071Bytes);
        DeepRematchArtifact tracked0098 = DeepRematchGate.DecodeArtifact(tracked0098Bytes);
        artifacts &= tracked0071.ArtifactDigest == "9aaec07e522c0075e85511e10c90f7c8ac14a08e5e431e9e1cc5d2cc8fffd7aa"
            && tracked0098.ArtifactDigest == "cafce60fb966d550f0c0c55c1076b556eedbb10255a62846887d961f48fb0744"
            && Convert.ToHexStringLower(SHA256.HashData(tracked0071Bytes)) == "f1f8d52c6de0f7e11f2791806015d6a98b1cba8ef045169d006982891f639572"
            && Convert.ToHexStringLower(SHA256.HashData(tracked0098Bytes)) == "724b83a1386cc95ee270b0d3a97376e7167cac9ad532864081062e0c15441c35";
        string historicalDirectory = Path.Combine(".tmp", "dissolution-live");
        string historical0071Path = Path.Combine(historicalDirectory, "cortex_0071.ron");
        string historical0098Path = Path.Combine(historicalDirectory, "cortex_0098.ron");
        if (File.Exists(historical0071Path) && File.Exists(historical0098Path))
        {
            DeepRematchArtifact historical0071 = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(historical0071Path));
            DeepRematchArtifact historical0098 = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(historical0098Path));
            artifacts &= historical0071.ArtifactDigest == "9aaec07e522c0075e85511e10c90f7c8ac14a08e5e431e9e1cc5d2cc8fffd7aa"
                && historical0098.ArtifactDigest == "cafce60fb966d550f0c0c55c1076b556eedbb10255a62846887d961f48fb0744";
        }

        string fieldCorruption = Encoding.UTF8.GetString(bytes0071).Replace(
            "vocabulary_knee: 64", "vocabulary_knee: 65", StringComparison.Ordinal);
        bool fieldRejected = false;
        try { _ = DeepRematchGate.DecodeArtifact(Encoding.UTF8.GetBytes(fieldCorruption)); }
        catch (InvalidDataException) { fieldRejected = true; }

        string digestCorruption = Encoding.UTF8.GetString(bytes0098).Replace(
            "artifact_digest: \"" + baseline0098.ArtifactDigest + "\"",
            "artifact_digest: \"" + new string('0', 64) + "\"",
            StringComparison.Ordinal);
        bool digestRejected = false;
        try { _ = DeepRematchGate.DecodeArtifact(Encoding.UTF8.GetBytes(digestCorruption)); }
        catch (InvalidDataException) { digestRejected = true; }

        string mixed = Encoding.UTF8.GetString(bytes0071).Replace(
            "historical_baseline: true,",
            "historical_baseline: true, rung0_assay_status: \"\",",
            StringComparison.Ordinal);
        bool mixedRejected = false;
        try { _ = DeepRematchGate.DecodeArtifact(Encoding.UTF8.GetBytes(mixed)); }
        catch (InvalidDataException) { mixedRejected = true; }
        return (artifacts, fieldRejected, digestRejected, mixedRejected);
    }

    private static DeepRematchArtifact CreateLegacyFixtureArtifact(string runID)
    {
        DeepRematchArtifact artifact = DeepRematchGate.CreateFixtureArtifact(runID, HashFixture(runID + "|registration"), true);
        artifact.schemaVersion = 1;
        artifact.historicalBaseline = true;
        artifact.rung0AssayStatus = "";
        artifact.rung0AssayDetail = "";
        artifact.rung0ShadowPowerStatus = "";
        artifact.rung0ShadowPowerDetail = "";
        artifact.rung0NullPowerStatus = "";
        artifact.rung0NullPowerDetail = "";
        artifact.evaluationTopology = "historical-baseline";
        artifact.evaluationStartStep = 0;
        artifact.evaluationEndStep = 0;
        artifact.evaluationRows = 0;
        artifact.computeReportDigest = "";
        artifact.computeReportRecords = 0;
        artifact.collectorProvenanceDigest = DeepRematchGate.ComputeCollectorProvenanceDigest(artifact);
        artifact.artifactDigest = DeepRematchGate.ComputeArtifactDigest(artifact);
        return artifact;
    }

    private static byte[] CreateLegacyFixtureBytes(DeepRematchArtifact artifact)
    {
        byte[] serialized = RonSerializer.SerializeToUtf8(in artifact);
        string[] lines = Encoding.UTF8.GetString(serialized).Split('\n');
        string text = string.Join('\n', lines.Where(line => !DeepRematchGate.IsSerializedV2OnlyFieldForFixture(line)));
        return Encoding.UTF8.GetBytes(text);
    }

    private static string HashFixture(string value)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

[RonObject]
internal partial class DeepRematchVerdictRecord
{
    public int line;
    public string name = "";
    public string status = "";
    public string evidenceDigest = "";
    public string detail = "";
    public string digest = "";
    public int Line => line;
    public string Name => name;
    public string Status => status;
    public string EvidenceDigest => evidenceDigest;
    public string Detail => detail;
    public string Digest => digest;

    internal DeepRematchVerdictRecord() { }
    internal DeepRematchVerdictRecord(int line, string name, string status, string evidenceDigest, string detail, string digest)
    {
        this.line = line; this.name = name; this.status = status; this.evidenceDigest = evidenceDigest; this.detail = detail; this.digest = digest;
    }
}

[RonObject]
internal partial class DeepRematchBankedNullRecord
{
    public string name = "";
    public string receiptDigest = "";
    public string detail = "";
    public string digest = "";
    public string Name => name;
    public string ReceiptDigest => receiptDigest;
    public string Detail => detail;
    public string Digest => digest;

    internal DeepRematchBankedNullRecord() { }
    internal DeepRematchBankedNullRecord(string name, string receiptDigest, string detail, string digest)
    {
        this.name = name; this.receiptDigest = receiptDigest; this.detail = detail; this.digest = digest;
    }
}

[RonObject]
internal partial class DeepRematchFuelAxis
{
    public string name = "";
    public long planned;
    public long actual;
    public long refund;
    public string availability = "Known";
    public string reason = "";
    public string sourceDigest = "";
    public string Name => name;
    public long Planned => planned;
    public long Actual => actual;
    public long Refund => refund;
    public string Availability => availability;
    public string Reason => reason;
    public string SourceDigest => sourceDigest;

    internal DeepRematchFuelAxis() { }
    internal DeepRematchFuelAxis(string name, long planned, long actual, long refund, string availability = "Known", string reason = "", string sourceDigest = "")
    {
        this.name = name; this.planned = planned; this.actual = actual; this.refund = refund;
        this.availability = availability; this.reason = reason;
        this.sourceDigest = string.IsNullOrWhiteSpace(sourceDigest)
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name + "|fuel-axis")))
            : sourceDigest;
    }
}

[RonObject]
internal partial class DeepRematchGateConfig
{
    public int schemaVersion;
    public string gateID = "";
    public string configDigest = "";
    public string baseline0071ArtifactDigest = "";
    public string baseline0098ArtifactDigest = "";
    public string baseline0071RunID = "";
    public string baseline0098RunID = "";
    public long baseline0071Certificates;
    public long baseline0071EvaluatorCalls;
    public long baseline0098Certificates;
    public long baseline0098EvaluatorCalls;
    public string certificatesDenominator = "certificates";
    public int frontierKneeExclusive;
    public double organismBand;
    public double residualMultiplier;
    public double computeDarkTolerance;
    public long baseline0098Day;
    public long baseline0098Replay;
    public long baseline0098ConsolidationPhase;
    public double baseline0098Residual;
    public long minimumPaidCloses;
    public double maximumPaidCloseExecutionsRatio;
    public long a3PreludeSteps;
    public long evaluationSteps;
    public List<DeepRematchFuelAxis> matchedFuelAxes = [];

    public int SchemaVersion => schemaVersion;
    public string GateID => gateID;
    public string ConfigDigest => configDigest;
    public string Baseline0071ArtifactDigest => baseline0071ArtifactDigest;
    public string Baseline0098ArtifactDigest => baseline0098ArtifactDigest;
    public string Baseline0071RunID => baseline0071RunID;
    public string Baseline0098RunID => baseline0098RunID;
    public long Baseline0071Certificates => baseline0071Certificates;
    public long Baseline0071EvaluatorCalls => baseline0071EvaluatorCalls;
    public long Baseline0098Certificates => baseline0098Certificates;
    public long Baseline0098EvaluatorCalls => baseline0098EvaluatorCalls;
    public string CertificatesDenominator => certificatesDenominator;
    public int FrontierKneeExclusive => frontierKneeExclusive;
    public double OrganismBand => organismBand;
    public double ResidualMultiplier => residualMultiplier;
    public double ComputeDarkTolerance => computeDarkTolerance;
    public long Baseline0098Day => baseline0098Day;
    public long Baseline0098Replay => baseline0098Replay;
    public long Baseline0098ConsolidationPhase => baseline0098ConsolidationPhase;
    public double Baseline0098Residual => baseline0098Residual;
    public long MinimumPaidCloses => minimumPaidCloses;
    public double MaximumPaidCloseExecutionsRatio => maximumPaidCloseExecutionsRatio;
    public long A3PreludeSteps => a3PreludeSteps;
    public long EvaluationSteps => evaluationSteps;
    public long FinalSteps => a3PreludeSteps + evaluationSteps;
    public List<DeepRematchFuelAxis> MatchedFuelAxes => matchedFuelAxes;
}

[RonObject]
internal partial class DeepRematchArtifact
{
    public int schemaVersion;
    public string runID = "";
    public bool historicalBaseline;
    public string registrationDigest = "";
    public string artifactDigest = "";
    public string checkpointDigest = "";
    public string collectorProvenanceDigest = "";
    public int vocabularyKnee;
    public List<DeepRematchFuelAxis> vocabularyFuelAxes = [];
    public long evaluatorCalls;
    public long certificates;
    public long rung0ComposedPredictions;
    public long rung0EvaluatorCalls;
    public long rung0AuditFailures;
    public long relationNullExecutions;
    public long relationNullAuthorityPredictions;
    public string rung0ReceiptDigest = "";
    public string rung0AssayStatus = "";
    public string rung0AssayDetail = "";
    public string rung0ShadowPowerStatus = "";
    public string rung0ShadowPowerDetail = "";
    public string rung0NullPowerStatus = "";
    public string rung0NullPowerDetail = "";
    public long policyReadoutPaidCloses;
    public long policyTreeEraPaidCloses;
    public long policyReadoutSpend;
    public long policyTreeEraSpend;
    public int a3PaidArms;
    public int a3HorizonShort;
    public int a3HorizonMedium;
    public int a3HorizonLong;
    public long a3Spend;
    public string a3ReceiptProvenanceDigest = "";
    public string checkpointReceiptDigest = "";
    public string fundingReceiptDigest = "";
    public string policyReceiptDigest = "";
    public long trialPlannedSteps;
    public long trialActualSteps;
    public long trialRefundSteps;
    public long readoutPlannedSteps;
    public long readoutActualSteps;
    public long readoutRefundSteps;
    public long policyNullDivergentExecutions;
    public long reflexControlAdaptations;
    public long saveLoadSaveMismatches;
    public List<DeepRematchFuelAxis> fuelAxes = [];
    public string computeStatus = "";
    public double computeDarkResidual;
    public long day;
    public long dream;
    public long aestivation;
    public double residual;
    public long paidCloses;
    public long executions;
    public bool bankedNull;
    public long a3ReceiptStep;
    public string evaluationTopology = "";
    public long evaluationStartStep;
    public long evaluationEndStep;
    public long evaluationRows;
    public string evaluationCurveDigest = "";
    public string evaluationComputeDigest = "";
    public string computeReportDigest = "";
    public long computeReportRecords;
    public List<DeepRematchSourceReceipt> sourceDigests = [];

    public int SchemaVersion => schemaVersion;
    public string RunID => runID;
    public bool HistoricalBaseline => historicalBaseline;
    public string RegistrationDigest => registrationDigest;
    public string ArtifactDigest => artifactDigest;
    public string CheckpointDigest => checkpointDigest;
    public string CollectorProvenanceDigest => collectorProvenanceDigest;
    public int VocabularyKnee => vocabularyKnee;
    public List<DeepRematchFuelAxis> VocabularyFuelAxes => vocabularyFuelAxes;
    public long EvaluatorCalls => evaluatorCalls;
    public long Certificates => certificates;
    public long Rung0ComposedPredictions => rung0ComposedPredictions;
    public long Rung0EvaluatorCalls => rung0EvaluatorCalls;
    public long Rung0AuditFailures => rung0AuditFailures;
    public long RelationNullExecutions => relationNullExecutions;
    public long RelationNullAuthorityPredictions => relationNullAuthorityPredictions;
    public string Rung0ReceiptDigest => rung0ReceiptDigest;
    public string Rung0AssayStatus => rung0AssayStatus;
    public string Rung0AssayDetail => rung0AssayDetail;
    public string Rung0ShadowPowerStatus => rung0ShadowPowerStatus;
    public string Rung0ShadowPowerDetail => rung0ShadowPowerDetail;
    public string Rung0NullPowerStatus => rung0NullPowerStatus;
    public string Rung0NullPowerDetail => rung0NullPowerDetail;
    public long PolicyReadoutPaidCloses => policyReadoutPaidCloses;
    public long PolicyTreeEraPaidCloses => policyTreeEraPaidCloses;
    public long PolicyReadoutSpend => policyReadoutSpend;
    public long PolicyTreeEraSpend => policyTreeEraSpend;
    public int A3PaidArms => a3PaidArms;
    public int A3HorizonShort => a3HorizonShort;
    public int A3HorizonMedium => a3HorizonMedium;
    public int A3HorizonLong => a3HorizonLong;
    public long A3Spend => a3Spend;
    public string A3ReceiptProvenanceDigest => a3ReceiptProvenanceDigest;
    public string CheckpointReceiptDigest => checkpointReceiptDigest;
    public string FundingReceiptDigest => fundingReceiptDigest;
    public string PolicyReceiptDigest => policyReceiptDigest;
    public long TrialPlannedSteps => trialPlannedSteps;
    public long TrialActualSteps => trialActualSteps;
    public long TrialRefundSteps => trialRefundSteps;
    public long ReadoutPlannedSteps => readoutPlannedSteps;
    public long ReadoutActualSteps => readoutActualSteps;
    public long ReadoutRefundSteps => readoutRefundSteps;
    public long PolicyNullDivergentExecutions => policyNullDivergentExecutions;
    public long ReflexControlAdaptations => reflexControlAdaptations;
    public long SaveLoadSaveMismatches => saveLoadSaveMismatches;
    public List<DeepRematchFuelAxis> FuelAxes => fuelAxes;
    public string ComputeStatus => computeStatus;
    public double ComputeDarkResidual => computeDarkResidual;
    public long Day => day;
    public long Replay => dream;
    public long ConsolidationPhase => aestivation;
    public double Residual => residual;
    public long PaidCloses => paidCloses;
    public long Executions => executions;
    public bool BankedNull => bankedNull;
    public long A3ReceiptStep => a3ReceiptStep;
    public string EvaluationTopology => evaluationTopology;
    public long EvaluationStartStep => evaluationStartStep;
    public long EvaluationEndStep => evaluationEndStep;
    public long EvaluationRows => evaluationRows;
    public string EvaluationCurveDigest => evaluationCurveDigest;
    public string EvaluationComputeDigest => evaluationComputeDigest;
    public string ComputeReportDigest => computeReportDigest;
    public long ComputeReportRecords => computeReportRecords;
    public List<DeepRematchSourceReceipt> SourceDigests => sourceDigests;
}

[RonObject]
internal partial class DeepRematchSourceReceipt
{
    public string path = "";
    public string digest = "";
    public string Path => path;
    public string Digest => digest;
}

internal abstract class DeepRematchReceipt
{
    public abstract string RunID { get; }
    public abstract string CheckpointDigest { get; }
    public abstract string ConfigDigest { get; }
    public abstract string ProvenanceDigest { get; }
    public abstract string ReadDigest();
    public abstract string ComputeDigest();
}

[RonObject]
internal partial class DeepRematchA3Receipt : DeepRematchReceipt
{
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public long receiptStep;
    public int fundedArms;
    public int horizonShort;
    public int horizonMedium;
    public int horizonLong;
    public long spend;
    public long nullDivergentExecutions;
    public string provenanceDigest = "";
    public int PaidArms => fundedArms;
    public int HorizonShort => horizonShort;
    public int HorizonMedium => horizonMedium;
    public int HorizonLong => horizonLong;
    public long ReceiptStep => receiptStep;
    public long Spend => spend;
    public long NullDivergentExecutions => nullDivergentExecutions;
    public override string ProvenanceDigest => provenanceDigest;
    public override string RunID => runID;
    public override string CheckpointDigest => checkpointDigest;
    public override string ConfigDigest => configDigest;
    public override string ReadDigest() => receiptDigest;
    public override string ComputeDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', runID, checkpointDigest, configDigest, receiptStep, fundedArms, horizonShort, horizonMedium, horizonLong, spend, nullDivergentExecutions, provenanceDigest))));
}

[RonObject]
internal partial class DeepRematchRung0Receipt : DeepRematchReceipt
{
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public string provenanceDigest = "";
    public long derivedPredictions;
    public long evaluatorCalls;
    public long auditFailures;
    public long nullExecutions;
    public long nullAuthorityPredictions;
    public string controlReceiptDigest = "";
    public string sourceCursorDigest = "";
    public string sourceStateDigest = "";
    public string assayStatus = "";
    public string assayDetail = "";
    public string shadowPowerStatus = "";
    public string shadowPowerDetail = "";
    public string nullPowerStatus = "";
    public string nullPowerDetail = "";
    public long ComposedPredictions => derivedPredictions;
    public long EvaluatorCalls => evaluatorCalls;
    public long AuditFailures => auditFailures;
    public long NullExecutions => nullExecutions;
    public long NullAuthorityPredictions => nullAuthorityPredictions;
    public string ControlReceiptDigest => controlReceiptDigest;
    public string SourceCursorDigest => sourceCursorDigest;
    public string SourceStateDigest => sourceStateDigest;
    public string AssayStatus => assayStatus;
    public string AssayDetail => assayDetail;
    public string ShadowPowerStatus => shadowPowerStatus;
    public string ShadowPowerDetail => shadowPowerDetail;
    public string NullPowerStatus => nullPowerStatus;
    public string NullPowerDetail => nullPowerDetail;
    public override string RunID => runID;
    public override string CheckpointDigest => checkpointDigest;
    public override string ConfigDigest => configDigest;
    public override string ProvenanceDigest => provenanceDigest;
    public override string ReadDigest() => receiptDigest;
    public override string ComputeDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', runID, checkpointDigest, configDigest, provenanceDigest, derivedPredictions, evaluatorCalls, auditFailures, nullExecutions, nullAuthorityPredictions, controlReceiptDigest, sourceCursorDigest, sourceStateDigest, assayStatus, assayDetail, shadowPowerStatus, shadowPowerDetail, nullPowerStatus, nullPowerDetail, "rung0-v2"))));
}

[RonObject]
internal partial class DeepRematchCheckpointReceipt : DeepRematchReceipt
{
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public string provenanceDigest = "";
    public long mismatches;
    public string dialect = "";
    public string saveDigest = "";
    public string loadSaveDigest = "";
    public long Mismatches => mismatches;
    public string Dialect => dialect;
    public string SaveDigest => saveDigest;
    public string LoadSaveDigest => loadSaveDigest;
    public override string RunID => runID;
    public override string CheckpointDigest => checkpointDigest;
    public override string ConfigDigest => configDigest;
    public override string ProvenanceDigest => provenanceDigest;
    public override string ReadDigest() => receiptDigest;
    public override string ComputeDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', runID, checkpointDigest, configDigest, provenanceDigest, mismatches, dialect, saveDigest, loadSaveDigest))));
}

[RonObject]
internal partial class DeepRematchFundingReceipt : DeepRematchReceipt
{
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public string provenanceDigest = "";
    public List<DeepRematchFuelAxis> axes = [];
    public long trialPlannedSteps;
    public long trialActualSteps;
    public long trialRefundSteps;
    public long readoutPlannedSteps;
    public long readoutActualSteps;
    public long readoutRefundSteps;
    public string computeStatus = "";
    public double computeDarkResidual;
    public long evaluationStartStep;
    public long evaluationEndStep;
    public List<DeepRematchFuelAxis> Axes => axes;
    public long TrialPlannedSteps => trialPlannedSteps;
    public long TrialActualSteps => trialActualSteps;
    public long TrialRefundSteps => trialRefundSteps;
    public long ReadoutPlannedSteps => readoutPlannedSteps;
    public long ReadoutActualSteps => readoutActualSteps;
    public long ReadoutRefundSteps => readoutRefundSteps;
    public string ComputeStatus => computeStatus;
    public double ComputeDarkResidual => computeDarkResidual;
    public long EvaluationStartStep => evaluationStartStep;
    public long EvaluationEndStep => evaluationEndStep;
    public override string RunID => runID;
    public override string CheckpointDigest => checkpointDigest;
    public override string ConfigDigest => configDigest;
    public override string ProvenanceDigest => provenanceDigest;
    public override string ReadDigest() => receiptDigest;
    public override string ComputeDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', runID, checkpointDigest, configDigest, provenanceDigest, evaluationStartStep, evaluationEndStep, computeStatus, computeDarkResidual.ToString("G17", CultureInfo.InvariantCulture), trialPlannedSteps, trialActualSteps, trialRefundSteps, readoutPlannedSteps, readoutActualSteps, readoutRefundSteps, string.Join(',', axes.Select(static x => string.Join(':', x.Name, x.Availability, x.Planned, x.Actual, x.Refund, x.Reason, x.SourceDigest)))))));
}

[RonObject]
internal partial class DeepRematchPolicyReceipt : DeepRematchReceipt
{
    public string receiptDigest = "";
    public string runID = "";
    public string checkpointDigest = "";
    public string configDigest = "";
    public string provenanceDigest = "";
    public long readoutPaidCloses;
    public long treeEraPaidCloses;
    public long readoutSpend;
    public long treeEraSpend;
    public long nullDivergentExecutions;
    public long reflexControlAdaptations;
    public long ReadoutPaidCloses => readoutPaidCloses;
    public long TreeEraPaidCloses => treeEraPaidCloses;
    public long ReadoutSpend => readoutSpend;
    public long TreeEraSpend => treeEraSpend;
    public long NullDivergentExecutions => nullDivergentExecutions;
    public long ReflexControlAdaptations => reflexControlAdaptations;
    public override string RunID => runID;
    public override string CheckpointDigest => checkpointDigest;
    public override string ConfigDigest => configDigest;
    public override string ProvenanceDigest => provenanceDigest;
    public override string ReadDigest() => receiptDigest;
    public override string ComputeDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', runID, checkpointDigest, configDigest, provenanceDigest, readoutPaidCloses, treeEraPaidCloses, readoutSpend, treeEraSpend, nullDivergentExecutions, reflexControlAdaptations))));
}

[RonObject]
internal partial class DeepRematchGateReport
{
    public int schemaVersion;
    public string gateID = "";
    public string configDigest = "";
    public string runID = "";
    public string runArtifactDigest = "";
    public string reportDigest = "";
    public List<DeepRematchVerdictRecord> verdicts = [];
    public List<DeepRematchBankedNullRecord> bankedNulls = [];

    public int SchemaVersion => schemaVersion;
    public string GateID => gateID;
    public string ConfigDigest => configDigest;
    public string RunID => runID;
    public string RunArtifactDigest => runArtifactDigest;
    public string ReportDigest => reportDigest;
    public List<DeepRematchVerdictRecord> Verdicts => verdicts;
    public List<DeepRematchBankedNullRecord> BankedNulls => bankedNulls;
}
