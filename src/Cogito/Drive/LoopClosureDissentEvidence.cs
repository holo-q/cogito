namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Grammar;
using Ronmamon;

/// Durable, typed custody for one accepted policy-boundary divergence.  The outer
/// loop-closure record stores this payload as base64 so the per-funding file is
/// still one deterministic RON object while the policy receipt remains opaque
/// to report-level corroborationes.
internal static class LoopClosureDivergenceEvidence
{
    internal static byte[] Encode(in PolicyBoundaryDivergenceAdjudication adjudication, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        adjudication.Validate(domain);
        PolicyBoundaryDivergenceProof proof = adjudication.Proof;
        if (proof.ForcedNull.ExecutedOutcomeEventID.Value <= 0
            || proof.ForcedNull.ExecutedOutcomePayloadSHA256.Length != 64
            || proof.ForcedNull.ExecutedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("loop-closure divergence evidence requires the forced ordinary outcome custody");
        PolicyBoundaryForkReceipt forkReceipt = proof.ForkReceipt;
        PolicyBoundaryDivergenceArmOutcome candidateExecution = proof.Candidate.ExecutedOutcome ?? default;
        bool candidateHasExecution = proof.Candidate.ExecutedOutcome is not null;
        PolicyBoundaryArmReceipt candidateReceipt = forkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.Candidate && arm.Horizon == forkReceipt.Horizons[^1]);
        PolicyBoundaryDivergenceEvidenceRON document = new()
        {
            schemaVersion = 10,
            evidenceSHA256 = adjudication.EvidenceSHA256.Value,
            reason = adjudication.Reason,
            decisionID = proof.DecisionID.Value,
            policy = proof.Policy.Value,
            launchpadAction = proof.LaunchpadAction,
            rawCandidateAction = proof.RawCandidateAction,
            readoutFingerprint = proof.ReadoutFingerprint,
            readoutSupportDigest = proof.ReadoutOccurrenceDigest,
            readoutCandidateFingerprint = proof.ReadoutCandidateFingerprint,
            readoutRevision = proof.ReadoutRevision.Value,
            readoutComparisons = proof.ReadoutComparisons,
            readoutAgreements = proof.ReadoutAgreements,
            readoutMisses = proof.ReadoutMisses,
            fundingDecisionID = proof.Funding.QuotaDecisionID.Value,
            fundingPolicy = proof.Funding.Policy.Value,
            fundingReadoutFingerprint = proof.Funding.ReadoutFingerprint,
            fundingCandidateFingerprint = proof.Funding.CandidateFingerprint,
            fundingStep = proof.Funding.QuotaStep,
            fundingRequestedHorizonSteps = proof.Funding.RequestedHorizonSteps,
            fundingArmCount = proof.Funding.ArmCount,
            fundingPlannedArmSteps = proof.Funding.PlannedArmSteps,
            fundingHeldArmSteps = proof.Funding.HeldArmSteps,
            fundingDecision = (byte)proof.Funding.Decision,
            fundingUsedSteps = proof.Funding.UsedSteps,
            fundingRemainingQuota = proof.Funding.RemainingQuota,
            fundingCandidateState = (byte)proof.Funding.CandidateState,
            fundingDenialReason = (byte)proof.Funding.DenialReason,
            fundingCandidateOriginStep = proof.Funding.CandidateOriginStep,
            fundingCandidateCurrentStep = proof.Funding.CandidateCurrentStep,
            fundingCandidateRequiredStep = proof.Funding.CandidateRequiredStep,
            fundingCandidateRevision = proof.Funding.CandidateRevision.Value,
            fundingAllocationIdentity = proof.Funding.AllocationIdentity,
            fundingAllocationDigest = proof.Funding.AllocationDigest,
            fundingAllocationArmSteps = proof.Funding.AllocationArmSteps,
            fundingSeedAuditOnlyDigest = proof.Funding.SeedAuditOnlyDigest,
            settlementQuotaDecisionID = proof.Completion.QuotaDecisionID.Value,
            settlementActualExecutedArmSteps = proof.Completion.ActualExecutedArmSteps,
            settlementReclaimedOrUnused = proof.Completion.ReclaimedOrUnused,
            settlementEvaluatorWorkUnits = proof.Completion.EvaluatorWorkUnits ?? long.MinValue,
            settlementVerifierOutcome = (byte)proof.Completion.VerifierOutcome,
            settlementWallMilliseconds = proof.Completion.WallMilliseconds ?? long.MinValue,
            forkReceiptBase64 = Convert.ToBase64String(TapePacketCreator.EncodePolicyBoundaryReceipt(
                proof.Policy, domain, in forkReceipt)),
            executionWitnessSHA256 = forkReceipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "",
            executionTrainingWitnessSHA256 = forkReceipt.ExecutionCorroboration?.ReadoutTrainingCorroborationSHA256.Value ?? "",
            executionQuotaDecisionID = forkReceipt.ExecutionCorroboration?.QuotaDecisionID.Value ?? 0,
            executionQuotaReadoutFingerprint = forkReceipt.ExecutionCorroboration?.QuotaReadoutFingerprint ?? 0,
            executionQuotaCandidateFingerprint = forkReceipt.ExecutionCorroboration?.QuotaCandidateFingerprint ?? 0,
            executionFundingCandidateRevision = forkReceipt.ExecutionCorroboration?.FundingCandidateRevision.Value ?? 0,
            executionForkArmSHA256 = forkReceipt.ExecutionCorroboration?.ForkArmSHA256.Value ?? "",
            executionChildExecutionReceiptSHA256 = forkReceipt.ExecutionCorroboration?.ChildExecutionReceiptSHA256.Value ?? "",
            executionDissentDecisionID = forkReceipt.ExecutionCorroboration?.ExecutedDivergenceDecisionID.Value ?? 0,
            executionDissentOutcomeID = forkReceipt.ExecutionCorroboration?.ExecutedDivergenceOutcomeID.Value ?? "",
            executionDissentOutcomeEventID = forkReceipt.ExecutionCorroboration?.ExecutedDivergenceOutcomeEventID.Value ?? 0,
            executionDissentOutcomePayloadSHA256 = forkReceipt.ExecutionCorroboration?.ExecutedDivergenceOutcomePayloadSHA256 ?? "",
            candidateExecutionOutcome = (byte)proof.Candidate.Outcome,
            candidateRequestCount = proof.Candidate.RequestCount,
            candidateGuardAdmittedCount = proof.Candidate.GuardAdmittedCount,
            candidateArm = candidateHasExecution ? (byte)candidateExecution.Arm : (byte)0,
            candidateAction = candidateHasExecution ? candidateExecution.Action : 0,
            candidateAuthority = candidateHasExecution ? (byte)candidateExecution.Authority : (byte)0,
            candidateSelectionCause = candidateHasExecution ? (byte)candidateExecution.SelectionCause : (byte)0,
            candidateChildProcessCompleted = candidateHasExecution && candidateExecution.ChildProcessCompleted,
            candidateBehaviorallyExecuted = candidateHasExecution && candidateExecution.BehaviorallyExecuted,
            candidateDiverged = candidateHasExecution && candidateExecution.Diverged,
            candidateHorizon = proof.Candidate.Horizon,
            candidateMatchedSpend = proof.Candidate.MatchedSpend,
            candidateOutcomeID = candidateHasExecution ? candidateExecution.OutcomeID.Value : "",
            candidateDecisionID = candidateHasExecution ? candidateExecution.DecisionID.Value : 0,
            candidateLaunchpadAction = candidateHasExecution ? candidateExecution.LaunchpadAction : -1,
            candidateRawCandidateAction = candidateHasExecution ? candidateExecution.RawCandidateAction : -1,
            candidateSelectedCandidateAction = candidateHasExecution ? candidateExecution.SelectedCandidateAction : -1,
            candidateReadoutFingerprint = candidateHasExecution ? candidateExecution.ReadoutFingerprint : 0,
            candidateReadoutRevision = candidateHasExecution ? candidateExecution.ReadoutRevision : 0,
            candidateReadoutOccurrenceDigest = candidateHasExecution ? candidateExecution.ReadoutOccurrenceDigest : 0,
            candidateCandidateFingerprint = candidateHasExecution ? candidateExecution.CandidateFingerprint : 0,
            candidateOutcomeEventID = candidateHasExecution ? candidateExecution.ExecutedOutcomeEventID.Value : 0,
            candidateOutcomePayloadSHA256 = candidateHasExecution ? candidateExecution.ExecutedOutcomePayloadSHA256 : "",
            candidateCanonicalPolicy = candidateHasExecution ? candidateReceipt.ExecutedCanonicalState.Policy.Value : "",
            candidateCanonicalKind = candidateHasExecution ? (byte)candidateReceipt.ExecutedCanonicalState.Kind : (byte)0,
            candidateCanonicalVersion = candidateHasExecution ? candidateReceipt.ExecutedCanonicalState.Version : (ushort)0,
            candidateCanonicalValue = candidateHasExecution ? candidateReceipt.ExecutedCanonicalState.Value : 0,
            forcedNullArm = (byte)proof.ForcedNull.Arm,
            forcedNullAction = proof.ForcedNull.Action,
            forcedNullAuthority = (byte)proof.ForcedNull.Authority,
            forcedNullSelectionCause = (byte)proof.ForcedNull.SelectionCause,
            forcedNullChildProcessCompleted = proof.ForcedNull.ChildProcessCompleted,
            forcedNullBehaviorallyExecuted = proof.ForcedNull.BehaviorallyExecuted,
            forcedNullDiverged = proof.ForcedNull.Diverged,
            forcedNullHorizon = proof.ForcedNull.Horizon,
            forcedNullMatchedSpend = proof.ForcedNull.MatchedSpend,
            forcedNullOutcomeID = proof.ForcedNull.OutcomeID.Value,
            forcedNullDecisionID = proof.ForcedNull.DecisionID.Value,
            forcedNullLaunchpadAction = proof.ForcedNull.LaunchpadAction,
            forcedNullRawCandidateAction = proof.ForcedNull.RawCandidateAction,
            forcedNullSelectedCandidateAction = proof.ForcedNull.SelectedCandidateAction,
            forcedNullReadoutFingerprint = proof.ForcedNull.ReadoutFingerprint,
            forcedNullReadoutRevision = proof.ForcedNull.ReadoutRevision,
            forcedNullReadoutOccurrenceDigest = proof.ForcedNull.ReadoutOccurrenceDigest,
            forcedNullCandidateFingerprint = proof.ForcedNull.CandidateFingerprint,
            forcedNullOutcomeEventID = proof.ForcedNull.ExecutedOutcomeEventID.Value,
            forcedNullOutcomePayloadSHA256 = proof.ForcedNull.ExecutedOutcomePayloadSHA256,
            forcedNullCanonicalPolicy = proof.ForkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == proof.ForkReceipt.Horizons[^1]).ExecutedCanonicalState.Policy.Value,
            forcedNullCanonicalKind = (byte)proof.ForkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == proof.ForkReceipt.Horizons[^1]).ExecutedCanonicalState.Kind,
            forcedNullCanonicalVersion = proof.ForkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == proof.ForkReceipt.Horizons[^1]).ExecutedCanonicalState.Version,
            forcedNullCanonicalValue = proof.ForkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == proof.ForkReceipt.Horizons[^1]).ExecutedCanonicalState.Value,
            teacherEvidenceSHA256 = proof.Teacher?.EvidenceSHA256 ?? "",
            teacherFoldNodeID = proof.Teacher?.FoldNodeID.Value ?? "",
            teacherFoldRevision = proof.Teacher?.FoldRevision.Value ?? 0,
            teacherRevision = proof.Teacher?.TeacherRevision.Value ?? 0,
            teacherTrainingCorroborationSHA256 = proof.Teacher?.ReadoutTrainingCorroborationSHA256.Value ?? "",
            r4ProvenanceBase64 = proof.Provenance is LoopClosureR4Provenance provenance
                ? Convert.ToBase64String(provenance.Encode())
                : "",
        };
        if (proof.Teacher is not null)
            foreach (TapeEventID eventID in proof.Teacher.TeacherEventIDs) document.teacherEventIDs.Add(eventID.Value);
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("policy-boundary divergence RON encoding is nondeterministic");
        return first;
    }

    internal static PolicyBoundaryDivergenceAdjudication Decode(
        ReadOnlySpan<byte> bytes,
        Func<CortexPolicyID, IPolicyBoundaryDomain> resolveDomain)
    {
        ArgumentNullException.ThrowIfNull(resolveDomain);
        PolicyBoundaryDivergenceEvidenceRON document = RonSerializer.Deserialize<PolicyBoundaryDivergenceEvidenceRON>(bytes);
        if (document.schemaVersion != 10) throw new InvalidDataException("policy-boundary divergence schema is unsupported");
        IPolicyBoundaryDomain domain = resolveDomain(new CortexPolicyID(document.policy))
            ?? throw new InvalidDataException($"no policy-boundary domain is registered for {document.policy}");
        PolicyBoundaryForkReceipt receipt = DecodeReceipt(document.forkReceiptBase64, document.policy,
            document.readoutFingerprint, document.readoutRevision, domain);
        VerifyExecutionScopeFields(document, receipt);
        VerifyExecutionCorroborationFields(document, receipt);
        CortexPolicyTrialQuotaDecision funding = new(
            new CortexPolicyQuotaDecisionID(document.fundingDecisionID), new CortexPolicyID(document.fundingPolicy),
            document.fundingCandidateFingerprint, document.fundingStep, document.fundingRequestedHorizonSteps,
            document.fundingArmCount, document.fundingPlannedArmSteps, document.fundingHeldArmSteps,
            (CortexPolicyQuotaDecisions)document.fundingDecision, document.fundingUsedSteps, document.fundingRemainingQuota)
        {
            ReadoutFingerprint = document.fundingReadoutFingerprint,
            CandidateState = (CortexPolicyTrialCandidateStates)document.fundingCandidateState,
            DenialReason = (CortexPolicyTrialDenialReasons)document.fundingDenialReason,
            CandidateOriginStep = document.fundingCandidateOriginStep,
            CandidateCurrentStep = document.fundingCandidateCurrentStep,
            CandidateRequiredStep = document.fundingCandidateRequiredStep,
            CandidateRevision = new GrammarRevisionID(document.fundingCandidateRevision),
            AllocationIdentity = document.fundingAllocationIdentity,
            AllocationDigest = document.fundingAllocationDigest,
            AllocationArmSteps = document.fundingAllocationArmSteps,
            SeedAuditOnlyDigest = document.fundingSeedAuditOnlyDigest,
        };
        CortexPolicyTrialCompletion settlement = new(
            new CortexPolicyQuotaDecisionID(document.settlementQuotaDecisionID),
            document.settlementActualExecutedArmSteps, document.settlementReclaimedOrUnused,
            document.settlementEvaluatorWorkUnits == long.MinValue ? null : document.settlementEvaluatorWorkUnits,
            (CortexPolicyVerifierOutcomes)document.settlementVerifierOutcome,
            document.settlementWallMilliseconds == long.MinValue ? null : document.settlementWallMilliseconds);
        bool teacherFieldsPresent = document.teacherEventIDs.Count != 0 || !string.IsNullOrEmpty(document.teacherEvidenceSHA256)
            || !string.IsNullOrEmpty(document.teacherFoldNodeID) || document.teacherFoldRevision != 0 || document.teacherRevision != 0
            || !string.IsNullOrEmpty(document.teacherTrainingCorroborationSHA256);
        PolicyBoundaryTeacherCorroboration? teacher = null;
        if (teacherFieldsPresent)
        {
            teacher = new PolicyBoundaryTeacherCorroboration(document.teacherEventIDs.Select(static id => new TapeEventID(id)).ToArray(),
                document.teacherEvidenceSHA256, new LoopLineageNodeID(document.teacherFoldNodeID),
                new GrammarRevisionID(document.teacherFoldRevision), new GrammarRevisionID(document.teacherRevision),
                new LoopClosureDigest(document.teacherTrainingCorroborationSHA256));
        }
        else if (receipt.TeacherCorroboration is not null)
            throw new InvalidDataException("policy-boundary divergence evidence omits its required teacher corroboration");
        if (receipt.TeacherCorroboration is null && teacherFieldsPresent)
            throw new InvalidDataException("policy-boundary divergence evidence carries stray teacher fields");
        LoopClosureR4Provenance? provenance = string.IsNullOrEmpty(document.r4ProvenanceBase64)
            ? null
            : LoopClosureR4Provenance.Decode(Convert.FromBase64String(document.r4ProvenanceBase64));
        bool candidateExecuted = document.candidateExecutionOutcome == (byte)CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
        if (!candidateExecuted && HasStrayCandidateExecutionFields(document))
            throw new InvalidDataException("policy-boundary divergence evidence carries stray candidate execution fields");
        PolicyBoundaryDivergenceArmOutcome? candidateExecution = candidateExecuted
            ? DecodeArm(document.candidateArm, document.candidateAction, document.candidateAuthority,
                document.candidateSelectionCause, document.candidateChildProcessCompleted, document.candidateBehaviorallyExecuted,
                document.candidateDiverged, document.candidateHorizon, document.candidateMatchedSpend, document.candidateOutcomeID,
                document.candidateDecisionID, document.candidateLaunchpadAction, document.candidateRawCandidateAction,
                document.candidateSelectedCandidateAction, document.candidateReadoutFingerprint, document.candidateReadoutRevision,
                document.candidateReadoutOccurrenceDigest, document.candidateCandidateFingerprint,
                document.candidateOutcomeEventID, document.candidateOutcomePayloadSHA256)
            : null;
        PolicyBoundaryDivergenceCandidateTerminal candidate = new(
            (CortexPolicyTrialExecutionOutcomes)document.candidateExecutionOutcome,
            document.candidateRequestCount, document.candidateGuardAdmittedCount,
            document.candidateHorizon, document.candidateMatchedSpend, candidateExecution);
        PolicyBoundaryDivergenceProof proof = new(
            new CortexPolicyDecisionID(document.decisionID), new CortexPolicyID(document.policy), document.launchpadAction, document.rawCandidateAction,
            document.readoutFingerprint, document.readoutSupportDigest, document.readoutCandidateFingerprint, new GrammarRevisionID(document.readoutRevision), document.readoutComparisons,
            document.readoutAgreements, document.readoutMisses, funding, settlement, receipt,
            candidate,
            DecodeArm(document.forcedNullArm, document.forcedNullAction, document.forcedNullAuthority,
                document.forcedNullSelectionCause, document.forcedNullChildProcessCompleted, document.forcedNullBehaviorallyExecuted,
                document.forcedNullDiverged, document.forcedNullHorizon, document.forcedNullMatchedSpend, document.forcedNullOutcomeID,
                document.forcedNullDecisionID, document.forcedNullLaunchpadAction, document.forcedNullRawCandidateAction,
                document.forcedNullSelectedCandidateAction, document.forcedNullReadoutFingerprint, document.forcedNullReadoutRevision,
                document.forcedNullReadoutOccurrenceDigest, document.forcedNullCandidateFingerprint,
                document.forcedNullOutcomeEventID, document.forcedNullOutcomePayloadSHA256),
            teacher, provenance);
        PolicyBoundaryDivergenceAdjudication adjudication = new(proof, true, new LoopClosureDigest(document.evidenceSHA256), document.reason);
        adjudication.Validate(domain);
        byte[] canonical = Encode(in adjudication, domain);
        if (!canonical.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("policy-boundary divergence RON round-trip changed bytes");
        return adjudication;
    }

    private static PolicyBoundaryDivergenceArmOutcome DecodeArm(
        byte arm, int action, byte authority, byte cause, bool childProcessCompleted, bool behaviorallyExecuted, bool diverged,
        int horizon, long matchedSpend, string outcomeID, ulong decisionID, int launchpadAction,
        int rawCandidateAction, int selectedCandidateAction, ulong readoutFingerprint,
        ulong readoutRevision, ulong readoutSupportDigest, ulong candidateFingerprint,
        long outcomeEventID, string outcomePayloadSHA256)
        => new((PolicyBoundaryDivergenceArmKinds)arm, action, (CortexPolicyAuthorities)authority,
            (CortexPolicySelectionCauses)cause, childProcessCompleted, behaviorallyExecuted, diverged, horizon, matchedSpend,
            new LoopClosureDigest(outcomeID))
        {
            DecisionID = new CortexPolicyDecisionID(decisionID),
            LaunchpadAction = launchpadAction,
            RawCandidateAction = rawCandidateAction,
            SelectedCandidateAction = selectedCandidateAction,
            ReadoutFingerprint = readoutFingerprint,
            ReadoutRevision = readoutRevision,
            ReadoutOccurrenceDigest = readoutSupportDigest,
            CandidateFingerprint = candidateFingerprint,
            ExecutedOutcomeEventID = new TapeEventID(outcomeEventID),
            ExecutedOutcomePayloadSHA256 = outcomePayloadSHA256,
        };

    private static PolicyBoundaryForkReceipt DecodeReceipt(string encoded, string policy, ulong fingerprint, ulong revision, IPolicyBoundaryDomain domain)
    {
        byte[] packet = Convert.FromBase64String(encoded);
        string text = Encoding.ASCII.GetString(packet);
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string field in text.Split('\t', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = field.IndexOf('=');
            if (equals > 0 && !fields.TryAdd(field[..equals], field[(equals + 1)..]))
                throw new InvalidDataException("policy-boundary divergence receipt contains duplicate fields");
        }
        if (!fields.TryGetValue("policy", out string? packetPolicy) || packetPolicy != policy)
            throw new InvalidDataException("policy-boundary divergence receipt policy identity drifted");
        if (!PolicyBoundaryTapeVerifier.TryRead(packet, domain,
                out PolicyBoundaryForkReceipt receipt, out CortexPolicyID decodedPolicy)
            || !string.Equals(decodedPolicy.Value, policy, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary divergence receipt packet is malformed");
        receipt.ValidateDivergenceCorroboration(fingerprint, new GrammarRevisionID(revision), domain);
        return receipt;
    }

    private static void VerifyExecutionCorroborationFields(
        PolicyBoundaryDivergenceEvidenceRON document,
        in PolicyBoundaryForkReceipt receipt)
    {
        if (receipt.ExecutionCorroboration is not PaidDivergenceExecutionCorroboration execution)
        {
            if (!string.IsNullOrEmpty(document.executionWitnessSHA256)
                || !string.IsNullOrEmpty(document.executionTrainingWitnessSHA256)
                || document.executionQuotaDecisionID != 0
                || document.executionQuotaCandidateFingerprint != 0
                || document.executionQuotaReadoutFingerprint != 0
                || document.executionFundingCandidateRevision != 0
                || !string.IsNullOrEmpty(document.executionForkArmSHA256)
                || !string.IsNullOrEmpty(document.executionChildExecutionReceiptSHA256)
                || document.executionDissentDecisionID != 0
                || !string.IsNullOrEmpty(document.executionDissentOutcomeID)
                || document.executionDissentOutcomeEventID != 0
                || !string.IsNullOrEmpty(document.executionDissentOutcomePayloadSHA256))
                throw new InvalidDataException("policy-boundary divergence evidence invents a paid execution corroboration");
            return;
        }
        if (!string.Equals(document.executionWitnessSHA256, execution.PaidDivergenceExecutionCorroborationSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(document.executionTrainingWitnessSHA256, execution.ReadoutTrainingCorroborationSHA256.Value, StringComparison.Ordinal)
            || document.executionQuotaDecisionID != execution.QuotaDecisionID.Value
            || document.executionQuotaReadoutFingerprint != execution.QuotaReadoutFingerprint
            || document.executionQuotaCandidateFingerprint != execution.QuotaCandidateFingerprint
            || document.executionFundingCandidateRevision != execution.FundingCandidateRevision.Value
            || !string.Equals(document.executionForkArmSHA256, execution.ForkArmSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(document.executionChildExecutionReceiptSHA256, execution.ChildExecutionReceiptSHA256.Value, StringComparison.Ordinal)
            || document.executionDissentDecisionID != execution.ExecutedDivergenceDecisionID.Value
            || !string.Equals(document.executionDissentOutcomeID, execution.ExecutedDivergenceOutcomeID.Value, StringComparison.Ordinal)
            || document.executionDissentOutcomeEventID != execution.ExecutedDivergenceOutcomeEventID.Value
            || !string.Equals(document.executionDissentOutcomePayloadSHA256, execution.ExecutedDivergenceOutcomePayloadSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary divergence evidence execution corroboration disagrees with its fork receipt");
    }

    private static bool HasStrayCandidateExecutionFields(PolicyBoundaryDivergenceEvidenceRON document)
        => document.candidateArm != 0 || document.candidateAction != 0 || document.candidateAuthority != 0
            || document.candidateSelectionCause != 0 || document.candidateChildProcessCompleted || document.candidateBehaviorallyExecuted
            || document.candidateDiverged || !string.IsNullOrEmpty(document.candidateOutcomeID) || document.candidateDecisionID != 0
            || document.candidateLaunchpadAction != -1 || document.candidateRawCandidateAction != -1
            || document.candidateSelectedCandidateAction != -1 || document.candidateReadoutFingerprint != 0
            || document.candidateReadoutRevision != 0 || document.candidateReadoutOccurrenceDigest != 0
            || document.candidateCandidateFingerprint != 0 || document.candidateOutcomeEventID != 0
            || !string.IsNullOrEmpty(document.candidateOutcomePayloadSHA256);

    private static void VerifyExecutionScopeFields(
        PolicyBoundaryDivergenceEvidenceRON document,
        in PolicyBoundaryForkReceipt receipt)
    {
        int terminal = receipt.Horizons[^1];
        PolicyBoundaryArmReceipt candidate = receipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.Candidate && arm.Horizon == terminal);
        PolicyBoundaryArmReceipt forcedNull = receipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == terminal);
        bool candidateExecuted = document.candidateExecutionOutcome == (byte)CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
        if ((candidateExecuted && (!candidate.ExecutedCanonicalState.IsValidFor(new CortexPolicyID(document.policy))
                || document.candidateCanonicalPolicy != candidate.ExecutedCanonicalState.Policy.Value
                || document.candidateCanonicalKind != (byte)candidate.ExecutedCanonicalState.Kind
                || document.candidateCanonicalVersion != candidate.ExecutedCanonicalState.Version
                || document.candidateCanonicalValue != candidate.ExecutedCanonicalState.Value))
            || (!candidateExecuted && (document.candidateCanonicalPolicy.Length != 0 || document.candidateCanonicalKind != 0
                || document.candidateCanonicalVersion != 0 || document.candidateCanonicalValue != 0))
            || !forcedNull.ExecutedCanonicalState.IsValidFor(new CortexPolicyID(document.policy))
            || document.forcedNullCanonicalPolicy != forcedNull.ExecutedCanonicalState.Policy.Value
            || document.forcedNullCanonicalKind != (byte)forcedNull.ExecutedCanonicalState.Kind
            || document.forcedNullCanonicalVersion != forcedNull.ExecutedCanonicalState.Version
            || document.forcedNullCanonicalValue != forcedNull.ExecutedCanonicalState.Value
            || document.forcedNullOutcomeEventID != forcedNull.ExecutedOutcomeEventID.Value
            || !string.Equals(document.forcedNullOutcomePayloadSHA256, forcedNull.ExecutedOutcomePayloadSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary divergence evidence execution scope disagrees with its fork receipt");
    }
}

[RonObject]
// Frozen RON field names retain witness/dissent vocabulary; identifier-side names use Corroboration/Divergence.
internal partial class PolicyBoundaryDivergenceEvidenceRON
{
    public int schemaVersion;
    public string evidenceSHA256 = "";
    public string reason = "";
    public ulong decisionID;
    public string policy = "";
    public int launchpadAction;
    public int rawCandidateAction;
    public ulong readoutFingerprint;
    public ulong readoutSupportDigest;
    public ulong readoutCandidateFingerprint;
    public ulong readoutRevision;
    public int readoutComparisons;
    public int readoutAgreements;
    public int readoutMisses;
    public ulong fundingDecisionID;
    public string fundingPolicy = "";
    public ulong fundingReadoutFingerprint;
    public ulong fundingCandidateFingerprint;
    public int fundingStep;
    public int fundingRequestedHorizonSteps;
    public int fundingArmCount;
    public long fundingPlannedArmSteps;
    public long fundingHeldArmSteps;
    public byte fundingDecision;
    public long fundingUsedSteps;
    public long fundingRemainingQuota;
    public byte fundingCandidateState;
    public byte fundingDenialReason;
    public int fundingCandidateOriginStep;
    public int fundingCandidateCurrentStep;
    public int fundingCandidateRequiredStep;
    public ulong fundingCandidateRevision;
    public string fundingAllocationIdentity = "";
    public string fundingAllocationDigest = "";
    public long fundingAllocationArmSteps;
    public string fundingSeedAuditOnlyDigest = "";
    public ulong settlementQuotaDecisionID;
    public long settlementActualExecutedArmSteps;
    public long settlementReclaimedOrUnused;
    public long settlementEvaluatorWorkUnits = long.MinValue;
    public byte settlementVerifierOutcome;
    public long settlementWallMilliseconds = long.MinValue;
    public string forkReceiptBase64 = "";
    public string executionWitnessSHA256 = "";
    public string executionTrainingWitnessSHA256 = "";
    public ulong executionQuotaDecisionID;
    public ulong executionQuotaReadoutFingerprint;
    public ulong executionQuotaCandidateFingerprint;
    public ulong executionFundingCandidateRevision;
    public string executionForkArmSHA256 = "";
    public string executionChildExecutionReceiptSHA256 = "";
    public ulong executionDissentDecisionID;
    public string executionDissentOutcomeID = "";
    public long executionDissentOutcomeEventID;
    public string executionDissentOutcomePayloadSHA256 = "";
    public byte candidateExecutionOutcome;
    public long candidateRequestCount;
    public long candidateGuardAdmittedCount;
    public byte candidateArm;
    public int candidateAction;
    public byte candidateAuthority;
    public byte candidateSelectionCause;
    public bool candidateChildProcessCompleted;
    public bool candidateBehaviorallyExecuted;
    public bool candidateDiverged;
    public int candidateHorizon;
    public long candidateMatchedSpend;
    public string candidateOutcomeID = "";
    public ulong candidateDecisionID;
    public int candidateLaunchpadAction = -1;
    public int candidateRawCandidateAction = -1;
    public int candidateSelectedCandidateAction = -1;
    public ulong candidateReadoutFingerprint;
    public ulong candidateReadoutRevision;
    public ulong candidateReadoutOccurrenceDigest;
    public ulong candidateCandidateFingerprint;
    public long candidateOutcomeEventID;
    public string candidateOutcomePayloadSHA256 = "";
    public string candidateCanonicalPolicy = "";
    public byte candidateCanonicalKind;
    public ushort candidateCanonicalVersion;
    public ulong candidateCanonicalValue;
    public byte forcedNullArm;
    public int forcedNullAction;
    public byte forcedNullAuthority;
    public byte forcedNullSelectionCause;
    public bool forcedNullChildProcessCompleted;
    public bool forcedNullBehaviorallyExecuted;
    public bool forcedNullDiverged;
    public int forcedNullHorizon;
    public long forcedNullMatchedSpend;
    public string forcedNullOutcomeID = "";
    public ulong forcedNullDecisionID;
    public int forcedNullLaunchpadAction = -1;
    public int forcedNullRawCandidateAction = -1;
    public int forcedNullSelectedCandidateAction = -1;
    public ulong forcedNullReadoutFingerprint;
    public ulong forcedNullReadoutRevision;
    public ulong forcedNullReadoutOccurrenceDigest;
    public ulong forcedNullCandidateFingerprint;
    public long forcedNullOutcomeEventID;
    public string forcedNullOutcomePayloadSHA256 = "";
    public string forcedNullCanonicalPolicy = "";
    public byte forcedNullCanonicalKind;
    public ushort forcedNullCanonicalVersion;
    public ulong forcedNullCanonicalValue;
    public List<long> teacherEventIDs = new();
    public string teacherEvidenceSHA256 = "";
    public string teacherFoldNodeID = "";
    public ulong teacherFoldRevision;
    public ulong teacherRevision;
    public string teacherTrainingCorroborationSHA256 = "";
    public string r4ProvenanceBase64 = "";
}
