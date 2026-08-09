namespace Cogito;

using Cogito.Grammar;
using Ronmamon;

/// Canonical RON boundary for the native repository loop report.  The sealed
/// adjudication input is supplied by the terminal authority on decode; this
/// document carries its complete identity projection so a report cannot be
/// replayed against a different tape, world, or registration.
public static class RepositoryLoopClosureReportCodec
{
    public const int SchemaVersion = 2;

    public static byte[] Encode(RepositoryLoopClosureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        report.Validate();
        RepositoryLoopClosureReportRON document = Write(report);
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second))
            throw new InvalidDataException("native repository loop report RON encoding is nondeterministic");
        return first;
    }

    public static RepositoryLoopClosureReport Decode(
        ReadOnlySpan<byte> bytes,
        RepositoryLoopClosureAdjudicationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RepositoryLoopClosureReportRON document = RonSerializer.Deserialize<RepositoryLoopClosureReportRON>(bytes);
        if (document.schemaVersion != SchemaVersion || document.reportSpecies != RepositoryLoopClosureReport.ReportSpecies)
            throw new InvalidDataException("native repository loop report schema or species is unsupported");
        input.Validate();
        VerifyInputIdentity(document.input, input);
        RepositoryLoopClosureReport report = Read(document, input);
        report.Validate();
        byte[] canonical = Encode(report);
        if (!canonical.AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("native repository loop report RON round-trip changed bytes");
        if (!string.Equals(document.title, RepositoryLoopClosureReport.ReportTitle, StringComparison.Ordinal))
            throw new InvalidDataException("native repository loop report title is not the sealed report species");
        return report;
    }

    private static RepositoryLoopClosureReportRON Write(RepositoryLoopClosureReport report)
    {
        RepositoryLoopClosureAdjudicationInput input = report.Input;
        RepositoryLoopClosureReportRON document = new()
        {
            schemaVersion = SchemaVersion,
            reportSpecies = RepositoryLoopClosureReport.ReportSpecies,
            // ClosureCertificate is a mounted adjudication presentation, not a
            // persisted wire species.  Keeping the sealed report title here
            // makes replay independent of mutable process mount state.
            title = RepositoryLoopClosureReport.ReportTitle,
            input = WriteInputIdentity(input),
            taskOutcome = report.TaskOutcome is null ? null : WriteOutcome(report.TaskOutcome),
            lineage = WriteLineage(report.Lineage),
            links = new RepositoryLoopClosureLinkContractRON
            {
                allowOrganicGap = report.Links.AllowOrganicGap,
                evidence = report.Links.Evidence.Select(WriteLink).ToList(),
                liveness = report.Links.Liveness.Select(WriteLiveness).ToList(),
            },
            verdicts = report.Verdicts.Select(WriteVerdict).ToList(),
        };
        return document;
    }

    private static RepositoryLoopClosureReport Read(
        RepositoryLoopClosureReportRON document,
        RepositoryLoopClosureAdjudicationInput input)
    {
        RepositoryLoopClosureVerdict[] verdicts = document.verdicts.Select(ReadVerdict).ToArray();
        RepositoryLoopClosureLinkContract links = new(
            document.links.evidence.Select(ReadLink).ToArray(),
            document.links.liveness.Select(ReadLiveness).ToArray(),
            document.links.allowOrganicGap);
        RepositoryLoopClosureTaskOutcome? outcome = document.taskOutcome is null ? null : ReadOutcome(document.taskOutcome);
        // A decoded report carries no adjudication capability: the wire cannot transmit
        // the authority to mint a certificate, only the evidence that a fresh adjudication
        // may re-derive it from.
        return new RepositoryLoopClosureReport(input, verdicts, links, outcome, ReadLineage(document.lineage));
    }

    private static RepositoryLoopClosureInputIdentityRON WriteInputIdentity(RepositoryLoopClosureAdjudicationInput input)
        => new()
        {
            runID = input.RunID,
            sealedIdentitySHA256 = input.SealedIdentitySHA256,
            worldContentSHA256 = input.World.ContentSHA256,
            worldSnapshotSHA256 = input.World.SnapshotSHA256,
            tapeSHA256 = input.Tape.TapeSHA256,
            preSealTapeSHA256 = input.Tape.PreSealTapeSHA256,
            journalSHA256 = input.Journal.JournalSHA256,
            journalRowAuthoritiesSHA256 = input.Journal.RowAuthoritiesSHA256,
            authoritySHA256 = input.Authority.AuthoritySHA256,
            registrationSHA256 = input.Registration.RegistrationSHA256,
            registrationDocumentSHA256 = input.Authority.RegistrationDocumentSHA256,
            accessSHA256 = input.Access.AccessSHA256,
            accessSourcesSHA256 = input.Access.SourcesSHA256,
            frontierSHA256 = input.Frontier.FrontierSHA256,
            frontierRuntimeAuthoritySHA256 = input.Frontier.RuntimeAuthoritySHA256,
            theorySHA256 = input.Pattern.PatternSHA256,
            theoryPendingAuthoritySHA256 = input.Pattern.PendingAuthoritySHA256,
            taskID = input.Task.TaskID,
            taskAuthoritySHA256 = input.Task.AuthoritySHA256,
            runtimeAuthoritySHA256 = input.RuntimeAuthority.AuthoritySHA256,
            sealedEvidenceAuthoritySHA256 = input.EvidenceAuthority.AuthoritySHA256,
        };

    private static void VerifyInputIdentity(
        RepositoryLoopClosureInputIdentityRON value,
        RepositoryLoopClosureAdjudicationInput input)
    {
        if (value.runID != input.RunID || value.sealedIdentitySHA256 != input.SealedIdentitySHA256
            || value.worldContentSHA256 != input.World.ContentSHA256 || value.worldSnapshotSHA256 != input.World.SnapshotSHA256
            || value.tapeSHA256 != input.Tape.TapeSHA256 || value.preSealTapeSHA256 != input.Tape.PreSealTapeSHA256
            || value.journalSHA256 != input.Journal.JournalSHA256 || value.journalRowAuthoritiesSHA256 != input.Journal.RowAuthoritiesSHA256
            || value.authoritySHA256 != input.Authority.AuthoritySHA256 || value.registrationSHA256 != input.Registration.RegistrationSHA256
            || value.registrationDocumentSHA256 != input.Authority.RegistrationDocumentSHA256
            || value.accessSHA256 != input.Access.AccessSHA256 || value.accessSourcesSHA256 != input.Access.SourcesSHA256
            || value.frontierSHA256 != input.Frontier.FrontierSHA256 || value.frontierRuntimeAuthoritySHA256 != input.Frontier.RuntimeAuthoritySHA256
            || value.theorySHA256 != input.Pattern.PatternSHA256 || value.theoryPendingAuthoritySHA256 != input.Pattern.PendingAuthoritySHA256
            || value.taskID != input.Task.TaskID || value.taskAuthoritySHA256 != input.Task.AuthoritySHA256
            || value.runtimeAuthoritySHA256 != input.RuntimeAuthority.AuthoritySHA256
            || value.sealedEvidenceAuthoritySHA256 != input.EvidenceAuthority.AuthoritySHA256)
            throw new InvalidDataException("native repository loop report input authority diverges");
    }

    private static RepositoryLoopClosureTaskOutcomeRON WriteOutcome(RepositoryLoopClosureTaskOutcome value)
        => new()
        {
            taskID = value.TaskID, candidateCanonical = value.Candidate.Canonical, candidateSpecies = (byte)value.Candidate.Species,
            candidateDigest = value.Candidate.Digest.ToString(), frontierRevision = value.FrontierRevision.Value,
            frontierAuthoritySHA256 = value.FrontierAuthoritySHA256, selectionRevision = value.SelectionRevision.Value,
            selectionFrontierAuthoritySHA256 = value.SelectionFrontierAuthoritySHA256, selectionOrdinal = value.SelectionOrdinal,
            selectionEventID = value.SelectionEventID.Value, selectionReceiptSHA256 = value.SelectionReceiptSHA256,
            actionEventID = value.ActionEventID.Value, verificationEventID = value.OccurrenceCheckEventID.Value,
            outcomeEventID = value.OutcomeEventID.Value, outcomePredecessorEventID = value.OutcomePredecessorEventID.Value,
            actionPayloadSHA256 = value.ActionPayloadSHA256, verificationPayloadSHA256 = value.OccurrenceCheckPayloadSHA256,
            outcomePayloadSHA256 = value.OutcomePayloadSHA256, sourcePath = value.SourcePath, sourceBytes = value.SourceBytes,
            sourceSHA256 = value.SourceSHA256, sourceLine = value.SourceLine, resultSpecies = (byte)value.ResultSpecies,
            resultSHA256 = value.ResultSHA256, resultContentBase64 = Convert.ToBase64String(value.ResultContent.Span),
            outcomeSHA256 = value.OutcomeSHA256, verification = WriteOccurrenceCheck(value.OccurrenceCheck),
        };

    private static RepositoryLoopClosureTaskOutcome ReadOutcome(RepositoryLoopClosureTaskOutcomeRON value)
    {
        if (!RepositoryCandidate.TryParseCanonical(value.candidateCanonical, out RepositoryCandidate candidate)
            || candidate.Species != (RepositoryCandidateSpecies)value.candidateSpecies
            || !string.Equals(candidate.Digest.ToString(), value.candidateDigest, StringComparison.Ordinal))
            throw new InvalidDataException("native repository loop task outcome candidate canonical diverges");
        RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck = ReadOccurrenceCheck(value.verification);
        return new(value.taskID, candidate, new(value.frontierRevision), value.frontierAuthoritySHA256,
            new(value.selectionRevision), value.selectionFrontierAuthoritySHA256, value.selectionOrdinal,
            new(value.selectionEventID), value.selectionReceiptSHA256, new(value.actionEventID), new(value.verificationEventID),
            new(value.outcomeEventID), new(value.outcomePredecessorEventID), value.actionPayloadSHA256,
            value.verificationPayloadSHA256, value.outcomePayloadSHA256, value.sourcePath, value.sourceBytes,
            value.sourceSHA256, value.sourceLine, (RepositoryLoopClosureResultSpecies)value.resultSpecies,
            Convert.FromBase64String(value.resultContentBase64), occurrenceCheck, value.outcomeSHA256);
    }

    private static RepositoryLoopClosureTaskOccurrenceCheckRON WriteOccurrenceCheck(RepositoryLoopClosureTaskOccurrenceCheck value)
        => new()
        {
            mode = (byte)value.Mode, outcome = (byte)value.Outcome, oracleSHA256 = value.OracleSHA256,
            claim = value.Prediction is { } prediction ? WritePrediction(prediction) : null,
            typedPredictionReceipt = value.TypedPredictionReceipt is { } receipt ? WriteOccurrenceCheckReceipt(receipt) : null,
            worldSHA256 = value.WorldSHA256, accessSHA256 = value.AccessSHA256, evaluatorCost = value.EvaluatorCost,
            accessCost = value.AccessCost, accessSequence = value.AccessSequence, accessEntrySHA256 = value.AccessEntrySHA256,
            accessEntryCount = value.AccessEntryCount, predecessorEventID = value.PredecessorEventID.Value,
            callSHA256 = value.CallSHA256, evidenceSHA256 = value.EvidenceSHA256, receiptSHA256 = value.ReceiptSHA256,
        };

    private static RepositoryLoopClosureTaskOccurrenceCheck ReadOccurrenceCheck(RepositoryLoopClosureTaskOccurrenceCheckRON value)
    {
        RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck = new((RepositoryLoopClosureTaskOracleModes)value.mode, (RepositoryOccurrenceCheckOutcomes)value.outcome,
            value.oracleSHA256, value.claim is null ? null : ReadPrediction(value.claim),
            value.typedPredictionReceipt is null ? null : ReadOccurrenceCheckReceipt(value.typedPredictionReceipt),
            value.worldSHA256, value.accessSHA256, value.evaluatorCost, value.accessCost, value.accessSequence,
            value.accessEntrySHA256, value.accessEntryCount, new(value.predecessorEventID), value.callSHA256,
            value.evidenceSHA256);
        if (!string.Equals(value.receiptSHA256, occurrenceCheck.ReceiptSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("native repository task occurrence check receipt digest diverges");
        return occurrenceCheck;
    }

    private static RepositoryPredictionRON WritePrediction(RepositoryPrediction value)
        => new() { species = (byte)value.Species, path = value.Path, line = value.Line, value = value.Value, otherPath = value.OtherPath };

    private static RepositoryPrediction ReadPrediction(RepositoryPredictionRON value)
        => new((RepositoryPredictionSpecies)value.species, value.path, value.line, value.value, value.otherPath);

    private static RepositoryOccurrenceCheckReceiptRON WriteOccurrenceCheckReceipt(RepositoryOccurrenceCheckReceipt value)
        => new()
        {
            step = value.Step, claim = WritePrediction(value.Prediction), outcome = (byte)value.Outcome, worldSHA256 = value.WorldSHA256,
            accessSHA256 = value.AccessSHA256, claimSHA256 = value.PredictionSHA256, evidenceSHA256 = value.EvidenceSHA256,
            evaluatorCost = value.EvaluatorCost, accessCost = value.AccessCost, predecessorEventID = value.PredecessorEventID.Value,
            callSHA256 = value.CallSHA256, receiptSHA256 = value.ReceiptSHA256, accessSequence = value.AccessSequence,
            accessEntrySHA256 = value.AccessEntrySHA256, accessEntryCount = value.AccessEntryCount,
        };

    private static RepositoryOccurrenceCheckReceipt ReadOccurrenceCheckReceipt(RepositoryOccurrenceCheckReceiptRON value)
        => new(value.step, ReadPrediction(value.claim), (RepositoryOccurrenceCheckOutcomes)value.outcome, value.worldSHA256,
            value.accessSHA256, value.claimSHA256, value.evidenceSHA256, value.evaluatorCost, value.accessCost,
            new(value.predecessorEventID), value.callSHA256, value.receiptSHA256)
        { AccessSequence = value.accessSequence, AccessEntrySHA256 = value.accessEntrySHA256, AccessEntryCount = value.accessEntryCount };

    private static RepositoryLoopClosureVerdictRON WriteVerdict(RepositoryLoopClosureVerdict value)
    {
        RepositoryLoopClosureVerdictRON result = new()
        {
            species = (byte)value.Species, assay = (byte)value.Assay, power = (byte)value.Power, status = (byte)value.Status,
            evidenceSHA256 = value.EvidenceSHA256.Value,
        };
        switch (value)
        {
            case RepositoryPatternBecameThoughtVerdict patternVerdict:
                result.derivation = WriteComposition(patternVerdict.Composition); break;
            case RepositoryThoughtOverruledInstinctVerdict thought:
                result.dissentEvidenceSHA256 = thought.DivergenceEvidenceSHA256.Value; break;
            case RepositoryObjectLoopClosedVerdict closed:
                result.outcomeEvidenceSHA256 = closed.OutcomeEvidenceSHA256.Value; break;
        }
        return result;
    }

    private static RepositoryLoopClosureVerdict ReadVerdict(RepositoryLoopClosureVerdictRON value)
    {
        LoopClosureAssayStatuses assay = (LoopClosureAssayStatuses)value.assay;
        LoopClosurePowerStatuses power = (LoopClosurePowerStatuses)value.power;
        LoopClosureVerdictStatuses status = (LoopClosureVerdictStatuses)value.status;
        LoopClosureDigest evidence = new(value.evidenceSHA256);
        return (RepositoryLoopClosureVerdictSpecies)value.species switch
        {
            RepositoryLoopClosureVerdictSpecies.PatternBecameThought when value.derivation is not null
                => new RepositoryPatternBecameThoughtVerdict(assay, power, status, evidence, ReadComposition(value.derivation)),
            RepositoryLoopClosureVerdictSpecies.ThoughtOverruledInstinct
                => new RepositoryThoughtOverruledInstinctVerdict(assay, power, status, evidence, new(value.dissentEvidenceSHA256)),
            RepositoryLoopClosureVerdictSpecies.ObjectLoopClosed
                => new RepositoryObjectLoopClosedVerdict(assay, power, status, evidence, new(value.outcomeEvidenceSHA256)),
            _ => throw new InvalidDataException("native repository loop report verdict is missing typed evidence"),
        };
    }

    private static RepositoryLoopClosureLinkEvidenceRON WriteLink(RepositoryLoopClosureLinkEvidence value)
        => new()
        {
            recordID = value.RecordID, species = (byte)value.Species, path = (byte)value.Path, state = (byte)value.State,
            eventID = value.EventID.Value, payloadSHA256 = value.PayloadSHA256, evidenceSHA256 = value.EvidenceSHA256,
            predecessorEvidenceSHA256 = value.PredecessorEvidenceSHA256, lineageSHA256 = value.LineageSHA256,
            journalSHA256 = value.JournalSHA256, nodeSpecies = (byte)value.NodeSpecies, candidateSpecies = (byte)value.CandidateSpecies,
            candidateDigest = value.CandidateDigest.ToString(), candidateCanonical = value.CandidateCanonical, sourcePath = value.SourcePath,
            sourceLine = value.SourceLine, sourceBytes = value.SourceBytes, sourceSHA256 = value.SourceSHA256,
            accessSequence = value.AccessSequence, toolVerb = (byte)value.ToolVerb, policyID = value.PolicyID.Value,
            decisionID = value.DecisionID.Value, decisionEventID = value.DecisionEventID.Value, fundingDecisionID = value.QuotaDecisionID.Value,
            readoutFingerprint = value.ReadoutFingerprint.ToString(), candidateFingerprint = value.CandidateFingerprint.ToString(),
            candidateOccurrenceDigest = value.CandidateOccurrenceDigest, readoutRevision = value.ReadoutRevision.Value,
            canonicalState = value.CanonicalState.ToString(), frontierRevision = value.FrontierRevision.Value,
            frontierAuthoritySHA256 = value.FrontierAuthoritySHA256, selectionOrdinal = value.SelectionOrdinal,
            worldSHA256 = value.WorldSHA256, accessSHA256 = value.AccessSHA256, accessEntrySHA256 = value.AccessEntrySHA256, callSHA256 = value.CallSHA256,
            forkArmSHA256 = value.ForkArmSHA256.Value ?? "", childExecutionReceiptSHA256 = value.ChildExecutionReceiptSHA256.Value ?? "",
            nodeID = value.NodeID.Value, outcomeEventID = value.OutcomeEventID.Value, outcomePayloadSHA256 = value.OutcomePayloadSHA256,
            predecessorEventID = value.PredecessorEventID.Value, predecessorDigest = value.PredecessorDigest.Value ?? "",
            decisionPayloadSHA256 = value.DecisionPayloadSHA256, readoutEventID = value.ReadoutEventID.Value,
            readoutPayloadSHA256 = value.ReadoutPayloadSHA256, fundingEventID = value.FundingEventID.Value,
            fundingPayloadSHA256 = value.FundingPayloadSHA256, boundaryEventID = value.BoundaryEventID.Value,
            boundaryPayloadSHA256 = value.BoundaryPayloadSHA256, settlementEventID = value.SettlementEventID.Value,
            settlementPayloadSHA256 = value.SettlementPayloadSHA256, childOutcome = WriteChild(value.ChildOutcome), receiptSHA256 = value.ReceiptSHA256,
            runID = value.RunID, step = value.Step, attemptEvidenceSHA256 = value.AttemptEvidenceSHA256, attemptJournalSHA256 = value.AttemptJournalSHA256,
            attemptPredecessorEventID = value.AttemptPredecessorEventID, attemptPredecessorEvidenceSHA256 = value.AttemptPredecessorEvidenceSHA256,
            denialReason = (byte)value.DenialReason, hasDenialReason = value.HasDenialReason, fundingID = value.QuotaID.Value ?? "",
            forkReceiptSHA256 = value.ForkReceiptSHA256.Value ?? "", dissentEvidenceSHA256 = value.DivergenceEvidenceSHA256.Value ?? "",
            grammarRevision = value.GrammarRevision.Value, attemptSHA256 = value.AttemptSHA256.Value ?? "",
            predecessorAttemptSHA256 = value.PredecessorAttemptSHA256.Value ?? "", attemptEvidenceRunID = value.AttemptEvidenceRunID,
            attemptEvidenceRelativePath = value.AttemptEvidenceRelativePath, attemptEvidenceAuthoritySHA256 = value.AttemptEvidenceAuthoritySHA256.Value ?? "",
            attemptEvidenceRailSHA256 = value.AttemptEvidenceRailSHA256.Value ?? "", linkEventID = value.LinkEventID.Value,
            linkPacketSHA256 = value.LinkPacketSHA256, linkJournalSHA256 = value.LinkJournalSHA256,
        };

    private static RepositoryLoopClosureLinkEvidence ReadLink(RepositoryLoopClosureLinkEvidenceRON value)
        => new()
        {
            RecordID = value.recordID, Species = (LoopClosureLinkSpecies)value.species, Path = (LoopClosureLinkPaths)value.path,
            State = (LoopClosureLinkStates)value.state, EventID = new(value.eventID), PayloadSHA256 = value.payloadSHA256,
            EvidenceSHA256 = value.evidenceSHA256, PredecessorEvidenceSHA256 = value.predecessorEvidenceSHA256,
            LineageSHA256 = value.lineageSHA256, JournalSHA256 = value.journalSHA256, NodeSpecies = (LoopLineageNodeSpecies)value.nodeSpecies,
            CandidateSpecies = (RepositoryCandidateSpecies)value.candidateSpecies, CandidateDigest = ParseCandidateDigest(value.candidateDigest),
            CandidateCanonical = value.candidateCanonical, SourcePath = value.sourcePath, SourceLine = value.sourceLine,
            SourceBytes = value.sourceBytes, SourceSHA256 = value.sourceSHA256, AccessSequence = value.accessSequence,
            ToolVerb = (Tool.ToolVerbs)value.toolVerb, PolicyID = new(value.policyID), DecisionID = new(value.decisionID),
            DecisionEventID = new(value.decisionEventID), QuotaDecisionID = new(value.fundingDecisionID),
            ReadoutFingerprint = ParseFingerprint(value.readoutFingerprint), CandidateFingerprint = ParseCandidateFingerprint(value.candidateFingerprint),
            CandidateOccurrenceDigest = value.candidateOccurrenceDigest, ReadoutRevision = new(value.readoutRevision),
            CanonicalState = ParseCanonicalState(value.canonicalState), FrontierRevision = new(value.frontierRevision),
            FrontierAuthoritySHA256 = value.frontierAuthoritySHA256, SelectionOrdinal = value.selectionOrdinal,
            WorldSHA256 = value.worldSHA256, AccessSHA256 = value.accessSHA256, AccessEntrySHA256 = value.accessEntrySHA256, CallSHA256 = value.callSHA256,
            ForkArmSHA256 = new(value.forkArmSHA256), ChildExecutionReceiptSHA256 = new(value.childExecutionReceiptSHA256),
            NodeID = new(value.nodeID), OutcomeEventID = new(value.outcomeEventID), OutcomePayloadSHA256 = value.outcomePayloadSHA256,
            PredecessorEventID = new(value.predecessorEventID), PredecessorDigest = new(value.predecessorDigest),
            DecisionPayloadSHA256 = value.decisionPayloadSHA256, ReadoutEventID = new(value.readoutEventID),
            ReadoutPayloadSHA256 = value.readoutPayloadSHA256, FundingEventID = new(value.fundingEventID),
            FundingPayloadSHA256 = value.fundingPayloadSHA256, BoundaryEventID = new(value.boundaryEventID),
            BoundaryPayloadSHA256 = value.boundaryPayloadSHA256, SettlementEventID = new(value.settlementEventID),
            SettlementPayloadSHA256 = value.settlementPayloadSHA256, ChildOutcome = ReadChild(value.childOutcome), ReceiptSHA256 = value.receiptSHA256,
            RunID = value.runID, Step = value.step, AttemptEvidenceSHA256 = value.attemptEvidenceSHA256, AttemptJournalSHA256 = value.attemptJournalSHA256,
            AttemptPredecessorEventID = value.attemptPredecessorEventID, AttemptPredecessorEvidenceSHA256 = value.attemptPredecessorEvidenceSHA256,
            DenialReason = (LoopClosureGateDenialReasons)value.denialReason, HasDenialReason = value.hasDenialReason, QuotaID = new(value.fundingID),
            ForkReceiptSHA256 = new(value.forkReceiptSHA256), DivergenceEvidenceSHA256 = new(value.dissentEvidenceSHA256), GrammarRevision = new(value.grammarRevision),
            AttemptSHA256 = new(value.attemptSHA256), PredecessorAttemptSHA256 = new(value.predecessorAttemptSHA256), AttemptEvidenceRunID = value.attemptEvidenceRunID,
            AttemptEvidenceRelativePath = value.attemptEvidenceRelativePath, AttemptEvidenceAuthoritySHA256 = new(value.attemptEvidenceAuthoritySHA256),
            AttemptEvidenceRailSHA256 = new(value.attemptEvidenceRailSHA256), LinkEventID = new(value.linkEventID),
            LinkPacketSHA256 = value.linkPacketSHA256, LinkJournalSHA256 = value.linkJournalSHA256,
        };

    private static RepositoryLoopClosureChildOutcomeReferenceRON WriteChild(LoopClosureChildOutcomeReference value)
        => new() { runID = value.RunID, relativePath = value.RelativePath, authoritySHA256 = value.AuthoritySHA256.Value ?? "", railSHA256 = value.RailSHA256.Value ?? "", forcedDecisionID = value.ForcedDecisionID.Value, outcomeEventID = value.OutcomeEventID.Value, outcomePayloadSHA256 = value.OutcomePayloadSHA256.Value ?? "", beforeSeal = value.BeforeSeal };
    private static LoopClosureChildOutcomeReference ReadChild(RepositoryLoopClosureChildOutcomeReferenceRON value)
        => new(value.runID, value.relativePath, new(value.authoritySHA256), new(value.railSHA256), new(value.forcedDecisionID), new(value.outcomeEventID), new(value.outcomePayloadSHA256), value.beforeSeal);

    private static RepositoryLoopClosureGateLivenessRON WriteLiveness(LoopClosureGateLiveness value)
        => new() { species = (byte)value.Species, reached = value.Reached, admitted = value.Admitted, denied = value.Denied, meterSHA256 = value.MeterSHA256.Value, denials = value.DenialReasons.Select(static denial => new RepositoryLoopClosureGateDenialRON { reason = (byte)denial.Reason, count = denial.Count }).ToList() };
    private static LoopClosureGateLiveness ReadLiveness(RepositoryLoopClosureGateLivenessRON value)
        => new((LoopClosureLinkSpecies)value.species, value.reached, value.admitted, value.denied, value.denials.Select(static denial => new LoopClosureGateDenial((LoopClosureGateDenialReasons)denial.reason, denial.count)).ToArray(), new(value.meterSHA256));

    private static RepositoryLoopClosureLineageRON WriteLineage(RepositoryLoopClosureLineageResult value)
        => new() { canonicalStatus = (byte)value.Canonical.Status, canonicalLineageSHA256 = value.Canonical.LineageSHA256, canonicalFirstDiscriminatingEdge = value.Canonical.FirstDiscriminatingEdge.Value, canonicalDetail = value.Canonical.Detail, shuffled = value.ShuffledPredecessorNull is LoopClosureLineageNullExecuted executed ? WriteNull(executed.Receipt) : null, shuffledMissingReason = value.ShuffledPredecessorNull is LoopClosureLineageNullMissing missing ? missing.Reason : "" };
    private static RepositoryLoopClosureLineageResult ReadLineage(RepositoryLoopClosureLineageRON value)
        => new(new((LoopLineageOccurrenceCheckStatuses)value.canonicalStatus, value.canonicalLineageSHA256, new(value.canonicalFirstDiscriminatingEdge), value.canonicalDetail), value.shuffled is null ? new LoopClosureLineageNullMissing(value.shuffledMissingReason) : new LoopClosureLineageNullExecuted(ReadNull(value.shuffled)));
    private static RepositoryLoopClosureShuffledNullRON WriteNull(LoopLineageShuffledNullReceipt value)
        => new() { sourceAuthoritySHA256 = value.SourceAuthoritySHA256, sourceTapeSHA256 = value.SourceTapeSHA256, sourceJournalSHA256 = value.SourceJournalSHA256, eventCount = value.EventCount, edgeCount = value.EdgeCount, eligibleBucketCount = value.EligibleBucketCount, permutationSeed = value.PermutationSeed, permutationSHA256 = value.PermutationSHA256, swappedEdgeCount = value.SwappedEdgeCount, derangement = value.Derangement, sameEvents = value.SameEvents, samePayloads = value.SamePayloads, originalLineageSHA256 = value.OriginalLineageSHA256, originalStatus = (byte)value.OriginalStatus, shuffledLineageSHA256 = value.ShuffledLineageSHA256, shuffledStatus = (byte)value.ShuffledStatus, firstDiscriminatingEdge = value.FirstDiscriminatingEdge.Value };
    private static LoopLineageShuffledNullReceipt ReadNull(RepositoryLoopClosureShuffledNullRON value)
        => new(value.sourceAuthoritySHA256, value.sourceTapeSHA256, value.sourceJournalSHA256, value.eventCount, value.edgeCount, value.eligibleBucketCount, value.permutationSeed, value.permutationSHA256, value.swappedEdgeCount, value.derangement, value.sameEvents, value.samePayloads, value.originalLineageSHA256, (LoopLineageOccurrenceCheckStatuses)value.originalStatus, value.shuffledLineageSHA256, (LoopLineageOccurrenceCheckStatuses)value.shuffledStatus, new(value.firstDiscriminatingEdge));

    private static RepositoryLoopClosureCompositionRON WriteComposition(RepositoryPatternComposition value)
    {
        RepositoryPatternCandidateConclusion conclusion = value.Conclusion;
        return new()
        {
            ruleID = conclusion.RuleID.Value, candidateCanonical = conclusion.Candidate.Canonical,
            candidateSpecies = (byte)conclusion.Candidate.Species, candidateDigest = conclusion.Candidate.Digest.ToString(),
            supports = conclusion.OccurrenceSet.Occurrences.Select(WriteOccurrence).ToList(), receipt = WriteComposedReceipt(value.Receipt),
        };
    }
    private static RepositoryPatternComposition ReadComposition(RepositoryLoopClosureCompositionRON value)
    {
        if (!RepositoryCandidate.TryParseCanonical(value.candidateCanonical, out RepositoryCandidate candidate)
            || candidate.Species != (RepositoryCandidateSpecies)value.candidateSpecies || candidate.Digest != ParseCandidateDigest(value.candidateDigest))
            throw new InvalidDataException("native repository composition candidate canonical diverges");
        RepositoryPatternOccurrence[] occurrences = value.supports.Select(ReadOccurrence).ToArray();
        RepositoryPatternOccurrenceSet occurrenceSet = RepositoryPatternOccurrenceSet.Create(occurrences);
        RepositoryPatternCandidateConclusion conclusion = new(new RepositoryPatternRuleID(value.ruleID), occurrenceSet, candidate.Digest, candidate);
        return new(conclusion, ReadComposedReceipt(value.receipt));
    }
    private static RepositoryPatternOccurrenceRON WriteOccurrence(RepositoryPatternOccurrence value)
        => new() { claimID = value.PredictionID.Value, claim = WritePrediction(value.Prediction), verification = WriteOccurrenceCheckReceipt(value.OccurrenceCheck), sourceEventID = value.SourceEventID.Value, verificationReceiptEventID = value.OccurrenceCheckReceiptEventID.Value };
    private static RepositoryPatternOccurrence ReadOccurrence(RepositoryPatternOccurrenceRON value)
        => new(new RepositoryPatternPredictionID(value.claimID), ReadPrediction(value.claim), ReadOccurrenceCheckReceipt(value.verification), new(value.sourceEventID), new(value.verificationReceiptEventID));
    private static RepositoryPatternComposedReceiptRON WriteComposedReceipt(RepositoryComposedCandidateReceipt value)
        => new() { step = value.Step, ruleID = value.RuleID.Value, supportSetSHA256 = value.OccurrenceSetSHA256, supportReceiptEventIDs = value.OccurrenceReceiptEventIDs.Select(static id => id.Value).ToList(), candidateCanonical = value.CandidateCanonical, candidateSpecies = (byte)value.CandidateSpecies, candidateDigest = value.CandidateDigest.ToString(), claimSHA256 = value.PredictionSHA256, sourceEvidenceSHA256 = value.SourceEvidenceSHA256, derivedAdmissionPath = value.ComposedAdmissionPath, alternativeAdmissionPath = value.AlternativeAdmissionPath, derivedEvaluatorCalls = value.ComposedEvaluatorCalls, alternativeEvaluatorCalls = value.AlternativeEvaluatorCalls, evaluatorDelta = value.EvaluatorDelta, derivationEventID = value.CompositionEventID.Value, worldSHA256 = value.WorldSHA256, accessSHA256 = value.AccessSHA256, predecessorEventID = value.PredecessorEventID.Value, receiptSHA256 = value.ReceiptSHA256 };
    private static RepositoryComposedCandidateReceipt ReadComposedReceipt(RepositoryPatternComposedReceiptRON value)
        => new(value.step, new(value.ruleID), value.supportSetSHA256, value.supportReceiptEventIDs.Select(static id => new TapeEventID(id)).ToArray(), value.candidateCanonical, (RepositoryCandidateSpecies)value.candidateSpecies, ParseCandidateDigest(value.candidateDigest), value.claimSHA256, value.sourceEvidenceSHA256, value.derivedAdmissionPath, value.alternativeAdmissionPath, value.derivedEvaluatorCalls, value.alternativeEvaluatorCalls, value.evaluatorDelta, new(value.derivationEventID), value.worldSHA256, value.accessSHA256, new(value.predecessorEventID), value.receiptSHA256);

    private static CortexPolicyReadoutFingerprint ParseFingerprint(string value)
        => ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong parsed)
            ? new(parsed) : throw new InvalidDataException("native repository link readout fingerprint is malformed");
    private static CortexPolicyCandidateFingerprint ParseCandidateFingerprint(string value)
        => ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong parsed)
            ? new(parsed) : throw new InvalidDataException("native repository link candidate fingerprint is malformed");
    private static RepositoryCandidateDigest ParseCandidateDigest(string value)
        => ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong parsed)
            ? new(parsed) : throw new InvalidDataException("native repository candidate digest is malformed");
    private static PolicyCanonicalStateID ParseCanonicalState(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length != 4 || !parts[2].StartsWith("v", StringComparison.Ordinal)
            || !ushort.TryParse(parts[2].AsSpan(1), out ushort version)
            || !ulong.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong stateValue)
            || !Enum.TryParse(parts[1], out PolicyCanonicalStateKinds kind))
            throw new InvalidDataException("native repository link canonical state is malformed");
        return new(new CortexPolicyID(parts[0]), kind, version, stateValue);
    }
}

[RonObject]
internal partial class RepositoryLoopClosureReportRON
{
    public int schemaVersion;
    public string reportSpecies = "";
    public string title = "";
    public RepositoryLoopClosureInputIdentityRON input = new();
    public List<RepositoryLoopClosureVerdictRON> verdicts = new();
    public RepositoryLoopClosureLinkContractRON links = new();
    public RepositoryLoopClosureTaskOutcomeRON? taskOutcome;
    public RepositoryLoopClosureLineageRON lineage = new();
}
// Frozen RON field names; identifier-side vocabulary is PatternStore.
[RonObject] internal partial class RepositoryLoopClosureInputIdentityRON { public string runID = ""; public string sealedIdentitySHA256 = ""; public string worldContentSHA256 = ""; public string worldSnapshotSHA256 = ""; public string tapeSHA256 = ""; public string preSealTapeSHA256 = ""; public string journalSHA256 = ""; public string journalRowAuthoritiesSHA256 = ""; public string authoritySHA256 = ""; public string registrationSHA256 = ""; public string registrationDocumentSHA256 = ""; public string accessSHA256 = ""; public string accessSourcesSHA256 = ""; public string frontierSHA256 = ""; public string frontierRuntimeAuthoritySHA256 = ""; public string theorySHA256 = ""; public string theoryPendingAuthoritySHA256 = ""; public string taskID = ""; public string taskAuthoritySHA256 = ""; public string runtimeAuthoritySHA256 = ""; public string sealedEvidenceAuthoritySHA256 = ""; }
// Frozen RON field names; identifier-side vocabulary is OccurrenceCheck.
[RonObject] internal partial class RepositoryLoopClosureTaskOutcomeRON { public string taskID = ""; public string candidateCanonical = ""; public byte candidateSpecies; public string candidateDigest = ""; public ulong frontierRevision; public string frontierAuthoritySHA256 = ""; public ulong selectionRevision; public string selectionFrontierAuthoritySHA256 = ""; public long selectionOrdinal; public long selectionEventID; public string selectionReceiptSHA256 = ""; public long actionEventID; public long verificationEventID; public long outcomeEventID; public long outcomePredecessorEventID; public string actionPayloadSHA256 = ""; public string verificationPayloadSHA256 = ""; public string outcomePayloadSHA256 = ""; public string sourcePath = ""; public long sourceBytes; public string sourceSHA256 = ""; public int sourceLine; public byte resultSpecies; public string resultSHA256 = ""; public string resultContentBase64 = ""; public string outcomeSHA256 = ""; public RepositoryLoopClosureTaskOccurrenceCheckRON verification = new(); }
// Frozen RON field name claim; identifier-side name is Prediction.
[RonObject] internal partial class RepositoryLoopClosureTaskOccurrenceCheckRON { public byte mode; public byte outcome; public string oracleSHA256 = ""; public RepositoryPredictionRON? claim; public RepositoryOccurrenceCheckReceiptRON? typedPredictionReceipt; public string worldSHA256 = ""; public string accessSHA256 = ""; public long evaluatorCost; public long accessCost; public long accessSequence; public string accessEntrySHA256 = ""; public int accessEntryCount; public long predecessorEventID; public string callSHA256 = ""; public string evidenceSHA256 = ""; public string receiptSHA256 = ""; }
[RonObject] internal partial class RepositoryPredictionRON { public byte species; public string path = ""; public int line; public string value = ""; public string otherPath = ""; }
// Frozen RON field names claim and claimSHA256; identifier-side names are Prediction and PredictionSHA256.
[RonObject] internal partial class RepositoryOccurrenceCheckReceiptRON { public int step; public RepositoryPredictionRON claim = new(); public byte outcome; public string worldSHA256 = ""; public string accessSHA256 = ""; public string claimSHA256 = ""; public string evidenceSHA256 = ""; public long evaluatorCost; public long accessCost; public long predecessorEventID; public string callSHA256 = ""; public string receiptSHA256 = ""; public long accessSequence = -1; public string accessEntrySHA256 = ""; public int accessEntryCount; }
// Frozen RON field name derivation; identifier-side name is Composition.
[RonObject] internal partial class RepositoryLoopClosureVerdictRON { public byte species; public byte assay; public byte power; public byte status; public string evidenceSHA256 = ""; public RepositoryLoopClosureCompositionRON? derivation; public string dissentEvidenceSHA256 = ""; public string outcomeEvidenceSHA256 = ""; }
// Frozen RON field names supports, supportSetSHA256, and supportReceiptEventIDs; identifier-side names are Occurrences and OccurrenceSetSHA256.
[RonObject] internal partial class RepositoryLoopClosureCompositionRON { public string ruleID = ""; public List<RepositoryPatternOccurrenceRON> supports = new(); public string candidateCanonical = ""; public byte candidateSpecies; public string candidateDigest = ""; public RepositoryPatternComposedReceiptRON receipt = new(); }
// Frozen RON field names claim* and verification*; identifier-side names are Prediction* and OccurrenceCheck*.
[RonObject] internal partial class RepositoryPatternOccurrenceRON { public string claimID = ""; public RepositoryPredictionRON claim = new(); public RepositoryOccurrenceCheckReceiptRON verification = new(); public long sourceEventID; public long verificationReceiptEventID; }
// Frozen RON field names support*, derived*, and derivationEventID; identifier-side names are Occurrence*, Composed*, and CompositionEventID.
[RonObject] internal partial class RepositoryPatternComposedReceiptRON { public int step; public string ruleID = ""; public string supportSetSHA256 = ""; public List<long> supportReceiptEventIDs = new(); public string candidateCanonical = ""; public byte candidateSpecies; public string candidateDigest = ""; public string claimSHA256 = ""; public string sourceEvidenceSHA256 = ""; public string derivedAdmissionPath = ""; public string alternativeAdmissionPath = ""; public long derivedEvaluatorCalls; public long alternativeEvaluatorCalls; public long evaluatorDelta; public long derivationEventID; public string worldSHA256 = ""; public string accessSHA256 = ""; public long predecessorEventID; public string receiptSHA256 = ""; }
[RonObject] internal partial class RepositoryLoopClosureLinkContractRON { public bool allowOrganicGap; public List<RepositoryLoopClosureLinkEvidenceRON> evidence = new(); public List<RepositoryLoopClosureGateLivenessRON> liveness = new(); }
[RonObject] internal partial class RepositoryLoopClosureLinkEvidenceRON { public string recordID = ""; public byte species; public byte path; public byte state; public long eventID; public string payloadSHA256 = ""; public string evidenceSHA256 = ""; public string predecessorEvidenceSHA256 = ""; public string lineageSHA256 = ""; public string journalSHA256 = ""; public byte nodeSpecies; public byte candidateSpecies; public string candidateDigest = ""; public string candidateCanonical = ""; public string sourcePath = ""; public int sourceLine; public long sourceBytes; public string sourceSHA256 = ""; public long accessSequence; public byte toolVerb; public string policyID = ""; public ulong decisionID; public long decisionEventID; public ulong fundingDecisionID; public string readoutFingerprint = ""; public string candidateFingerprint = ""; public ulong candidateOccurrenceDigest; public ulong readoutRevision; public string canonicalState = ""; public ulong frontierRevision; public int selectionOrdinal; public string frontierAuthoritySHA256 = ""; public string worldSHA256 = ""; public string accessSHA256 = ""; public string accessEntrySHA256 = ""; public string callSHA256 = ""; public string forkArmSHA256 = ""; public string childExecutionReceiptSHA256 = ""; public string nodeID = ""; public long outcomeEventID; public string outcomePayloadSHA256 = ""; public long predecessorEventID; public string predecessorDigest = ""; public string decisionPayloadSHA256 = ""; public long readoutEventID; public string readoutPayloadSHA256 = ""; public long fundingEventID; public string fundingPayloadSHA256 = ""; public long boundaryEventID; public string boundaryPayloadSHA256 = ""; public long settlementEventID; public string settlementPayloadSHA256 = ""; public RepositoryLoopClosureChildOutcomeReferenceRON childOutcome = new(); public string receiptSHA256 = ""; public string runID = ""; public int step; public string attemptEvidenceSHA256 = ""; public string attemptJournalSHA256 = ""; public long attemptPredecessorEventID; public string attemptPredecessorEvidenceSHA256 = ""; public byte denialReason; public bool hasDenialReason; public string fundingID = ""; public string forkReceiptSHA256 = ""; public string dissentEvidenceSHA256 = ""; public ulong grammarRevision; public string attemptSHA256 = ""; public string predecessorAttemptSHA256 = ""; public string attemptEvidenceRunID = ""; public string attemptEvidenceRelativePath = ""; public string attemptEvidenceAuthoritySHA256 = ""; public string attemptEvidenceRailSHA256 = ""; public long linkEventID; public string linkPacketSHA256 = ""; public string linkJournalSHA256 = ""; }
[RonObject] internal partial class RepositoryLoopClosureChildOutcomeReferenceRON { public string runID = ""; public string relativePath = ""; public string authoritySHA256 = ""; public string railSHA256 = ""; public ulong forcedDecisionID; public long outcomeEventID; public string outcomePayloadSHA256 = ""; public bool beforeSeal; }
[RonObject] internal partial class RepositoryLoopClosureGateLivenessRON { public byte species; public long reached; public long admitted; public long denied; public string meterSHA256 = ""; public List<RepositoryLoopClosureGateDenialRON> denials = new(); }
[RonObject] internal partial class RepositoryLoopClosureGateDenialRON { public byte reason; public long count; }
[RonObject] internal partial class RepositoryLoopClosureLineageRON { public byte canonicalStatus; public string canonicalLineageSHA256 = ""; public string canonicalFirstDiscriminatingEdge = ""; public string canonicalDetail = ""; public string shuffledMissingReason = ""; public RepositoryLoopClosureShuffledNullRON? shuffled; }
[RonObject] internal partial class RepositoryLoopClosureShuffledNullRON { public string sourceAuthoritySHA256 = ""; public string sourceTapeSHA256 = ""; public string sourceJournalSHA256 = ""; public int eventCount; public int edgeCount; public int eligibleBucketCount; public ulong permutationSeed; public string permutationSHA256 = ""; public int swappedEdgeCount; public bool derangement; public bool sameEvents; public bool samePayloads; public string originalLineageSHA256 = ""; public byte originalStatus; public string shuffledLineageSHA256 = ""; public byte shuffledStatus; public string firstDiscriminatingEdge = ""; }
