namespace Cogito;

using System.Globalization;
using Cogito.Grammar;

/// Reconstructs a boundary receipt from its durable tape packet. Top-level flags are checked against the arm rows;
/// they are readout conveniences, never authority.
internal static class PolicyBoundaryTapeVerifier
{
    internal static bool TryRead(ReadOnlySpan<byte> packet, IPolicyBoundaryDomain domain,
        out PolicyBoundaryForkReceipt receipt, out CortexPolicyID policy)
    {
        receipt = default;
        policy = default;
        string text = System.Text.Encoding.ASCII.GetString(packet);
        if (!text.StartsWith(TapePacketCreator.PolicyBoundaryPrefix + "\t", StringComparison.Ordinal)) return false;
        string[] fields = text.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < fields.Length; i++)
        {
            int equals = fields[i].IndexOf('=');
            if (equals <= 0 || !values.TryAdd(fields[i][..equals], fields[i][(equals + 1)..])) return false;
        }
        // Execution identity includes the canonical program digest and immutable
        // state scope. Schema 7 is the only admitted dialect after divergence
        // candidate/child custody hardening; older packets must not be reinterpreted.
        bool currentExecutionSchema = values.TryGetValue("execution-schema", out string? executionSchemaText)
            && executionSchemaText == "7";
        if (!currentExecutionSchema) return false;
        if (!values.TryGetValue("policy", out string? policyText)
            || !values.TryGetValue("id", out string? id)
            || !values.TryGetValue("baseline", out string? baselineText)
            || !values.TryGetValue("boundary", out string? candidateText)
            || !values.TryGetValue("horizons", out string? horizonsText)
            || !values.TryGetValue("arms", out string? armsText)) return false;
        try { policy = new CortexPolicyID(policyText); }
        catch (ArgumentException) { return false; }
        if (!domain.PolicyID.Equals(policy) || !domain.PolicyBinding.PolicyID.Equals(policy)) return false;
        if (!PolicyBoundaryRational.TryParse(baselineText, out PolicyBoundaryRational baseline)
            || !PolicyBoundaryRational.TryParse(candidateText, out PolicyBoundaryRational candidate)) return false;
        string[] horizonColumns = horizonsText.Split(',', StringSplitOptions.RemoveEmptyEntries);
        int[] horizons = new int[horizonColumns.Length];
        for (int i = 0; i < horizons.Length; i++)
            if (!int.TryParse(horizonColumns[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out horizons[i])) return false;
        string[] armColumns = armsText.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (armColumns.Length != horizons.Length * 4) return false;
        PolicyBoundaryArmReceipt[] arms = new PolicyBoundaryArmReceipt[armColumns.Length];
        CortexPolicyID? reconstructedReceiptPolicy = null;
        for (int i = 0; i < arms.Length; i++)
        {
            string[] columns = armColumns[i].Split(',');
            if (columns.Length != 44
                || !byte.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte arm)
                || !int.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int horizon)
                || !long.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long paid)
                || !long.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long spend)
                || !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int continuity)
                || !int.TryParse(columns[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int childProcessCompleted)
                || !long.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long grammarExecutions)
                || !long.TryParse(columns[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long transitions)
                || !int.TryParse(columns[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int adaptationEnabled)
                || adaptationEnabled is not (0 or 1)
                || !byte.TryParse(columns[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executionOutcome)
                || !long.TryParse(columns[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out long requestCount)
                || !long.TryParse(columns[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out long guardAdmittedCount)
                || !ulong.TryParse(columns[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong lastRequestDecisionID)
                || !int.TryParse(columns[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestStep)
                || !int.TryParse(columns[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestLaunchpad)
                || !int.TryParse(columns[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestRaw)
                || !int.TryParse(columns[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestSelected)
                || !int.TryParse(columns[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastRequestAction)
                || !byte.TryParse(columns[18], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte lastRequestAuthority)
                || !ulong.TryParse(columns[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong lastRequestRevision)
                || !byte.TryParse(columns[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte lastRequestCause)
                || !ulong.TryParse(columns[21], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong lastRequestSupport)
                || !ulong.TryParse(columns[22], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong lastRequestCandidate)
                || !ulong.TryParse(columns[23], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong executedDecisionID)
                || !int.TryParse(columns[24], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedStep)
                || !int.TryParse(columns[25], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedLaunchpad)
                || !int.TryParse(columns[26], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedRaw)
                || !int.TryParse(columns[27], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedSelected)
                || !int.TryParse(columns[28], NumberStyles.Integer, CultureInfo.InvariantCulture, out int executedAction)
                || !byte.TryParse(columns[29], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executedAuthority)
                || !byte.TryParse(columns[30], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executedCause)
                || !ulong.TryParse(columns[31], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedFingerprint)
                || !ulong.TryParse(columns[32], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong executedRevision)
                || !ulong.TryParse(columns[33], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedSupportDigest)
                || !ulong.TryParse(columns[34], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedCandidateFingerprint)
                || !byte.TryParse(columns[36], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte executedCanonicalKind)
                || !ushort.TryParse(columns[37], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort executedCanonicalVersion)
                || !ulong.TryParse(columns[38], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executedCanonicalValue)
                || !long.TryParse(columns[39], NumberStyles.Integer, CultureInfo.InvariantCulture, out long executedDecisionEventID)
                || !long.TryParse(columns[40], NumberStyles.Integer, CultureInfo.InvariantCulture, out long executedOutcomeEventID)
                || !ulong.TryParse(columns[42], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong forcedDivergenceSeed)
                || !int.TryParse(columns[43], NumberStyles.Integer, CultureInfo.InvariantCulture, out int diverged)
                || diverged is not (0 or 1)) return false;
            string executedOutcomePayloadSHA256 = columns[41];
            if (childProcessCompleted is not (0 or 1) || diverged is not (0 or 1)) return false;
            PolicyCanonicalStateID executedCanonicalState = default;
            if (executedCanonicalVersion != 0)
            {
                if (string.IsNullOrEmpty(columns[35])) return false;
                try
                {
                    executedCanonicalState = new PolicyCanonicalStateID(
                        new CortexPolicyID(columns[35]), (PolicyCanonicalStateKinds)executedCanonicalKind,
                        executedCanonicalVersion, executedCanonicalValue);
                }
                catch (ArgumentException) { return false; }
                if (!executedCanonicalState.Policy.Equals(policy)) return false;
                if (reconstructedReceiptPolicy is CortexPolicyID priorPolicy
                    && !priorPolicy.Equals(executedCanonicalState.Policy)) return false;
                reconstructedReceiptPolicy = executedCanonicalState.Policy;
            }
            else if (!string.IsNullOrEmpty(columns[35]) || executedCanonicalValue != 0 || executedCanonicalKind != 0)
                return false;
            CortexPolicyTrialExecutionOutcomes mappedExecutionOutcome = (CortexPolicyTrialExecutionOutcomes)executionOutcome;
            arms[i] = new((PolicyBoundaryArms)arm, horizon, paid, spend, continuity == 1, childProcessCompleted == 1, grammarExecutions, transitions, adaptationEnabled == 1)
            {
                ExecutionOutcome = mappedExecutionOutcome,
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
                ExecutedOutcomeEventID = new TapeEventID(executedOutcomeEventID),
                ExecutedOutcomePayloadSHA256 = executedOutcomePayloadSHA256,
                ForcedDivergenceSeed = forcedDivergenceSeed,
                Diverged = diverged == 1,
            };
            try { arms[i].Validate(domain); arms[i].ValidateRequestAccounting(domain); arms[i].ValidateExecutedDecisionIdentity(domain); }
            catch (InvalidDataException) { return false; }
        }
        if (reconstructedReceiptPolicy is not CortexPolicyID receiptPolicy
            || !receiptPolicy.Equals(policy)) return false;
        PolicyBoundaryVerdict verdict = PolicyBoundaryVerdict.Compute(arms, horizons);
        if (!TryFlag(values, "continuity", verdict.ContinuityExact)
            || !TryFlag(values, "matched-spend", verdict.MatchedSpend)
            || !TryFlag(values, "child-process-completed", verdict.AllChildrenCompleted)
            || !TryFlag(values, "forced-null-behavior", verdict.ForcedNullBehaviorExecuted)
            || !TryFlag(values, "forced-null-diverged", verdict.ForcedNullDiverged)
            || !values.TryGetValue("verified", out string? verifiedText)
            || !int.TryParse(verifiedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int verified)
            || verified != (verdict.Verified ? 1 : 0)) return false;
        if (!values.TryGetValue("source-fingerprint", out string? sourceFingerprintText)
            || !values.TryGetValue("source-candidate-fingerprint", out string? sourceCandidateFingerprintText)
            || !values.TryGetValue("source-revision", out string? sourceRevisionText)
            || !values.TryGetValue("funding-id", out string? fundingIDText)
            || !ulong.TryParse(fundingIDText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong fundingID)
            || !ulong.TryParse(sourceFingerprintText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong sourceFingerprint)
            || !ulong.TryParse(sourceCandidateFingerprintText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong sourceCandidateFingerprint)
            || !ulong.TryParse(sourceRevisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong sourceRevision)) return false;
        PolicyBoundaryTeacherCorroboration? teacher = null;
        bool anyTeacherField = values.TryGetValue("teacher-events", out string? teacherEventsProbe)
            && !string.IsNullOrWhiteSpace(teacherEventsProbe);
        bool anyTrainingCorroborationField = values.TryGetValue("training-witness", out string? trainingCorroborationProbe)
            && !string.IsNullOrWhiteSpace(trainingCorroborationProbe);
        bool strayTeacherField = values.TryGetValue("teacher-evidence", out string? teacherEvidenceProbe)
                && !string.IsNullOrWhiteSpace(teacherEvidenceProbe)
            || values.TryGetValue("fold-node", out string? foldNodeProbe)
                && !string.IsNullOrWhiteSpace(foldNodeProbe)
            || values.TryGetValue("fold-revision", out string? foldRevisionProbe)
                && foldRevisionProbe != "0"
            || values.TryGetValue("teacher-revision", out string? teacherRevisionProbe)
                && teacherRevisionProbe != "0";
        if (anyTrainingCorroborationField && !anyTeacherField) return false;
        if (strayTeacherField && !anyTeacherField) return false;
        if (anyTeacherField)
        {
            if (!values.TryGetValue("teacher-events", out string? teacherEventsText)
                || string.IsNullOrWhiteSpace(teacherEventsText)
                || !values.TryGetValue("teacher-evidence", out string? teacherEvidence)
                || !values.TryGetValue("fold-node", out string? foldNode)
                || !values.TryGetValue("fold-revision", out string? foldRevisionText)
                || !values.TryGetValue("teacher-revision", out string? teacherRevisionText)
                || !ulong.TryParse(foldRevisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong foldRevision)
                || !ulong.TryParse(teacherRevisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong teacherRevision)) return false;
            string[] teacherEvents = teacherEventsText.Split(',', StringSplitOptions.RemoveEmptyEntries);
            TapeEventID[] teacherIDs = new TapeEventID[teacherEvents.Length];
            for (int i = 0; i < teacherIDs.Length; i++)
                if (!long.TryParse(teacherEvents[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long eventID)) return false;
                else teacherIDs[i] = new TapeEventID(eventID);
            LoopClosureDigest trainingCorroboration = anyTrainingCorroborationField ? new LoopClosureDigest(trainingCorroborationProbe!) : default;
            try { teacher = new PolicyBoundaryTeacherCorroboration(teacherIDs, teacherEvidence, new LoopLineageNodeID(foldNode), new GrammarRevisionID(foldRevision), new GrammarRevisionID(teacherRevision), trainingCorroboration); teacher.Validate(); }
            catch (InvalidDataException) { return false; }
        }
        PaidDivergenceExecutionCorroboration? execution = null;
        bool anyExecutionField = values.TryGetValue("execution-witness", out string? executionProbe)
            && !string.IsNullOrWhiteSpace(executionProbe);
        if (!anyExecutionField && values.Any(static pair => pair.Key.StartsWith("execution-", StringComparison.Ordinal)
            && !string.Equals(pair.Key, "execution-schema", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(pair.Value))) return false;
        if (anyExecutionField)
        {
            if (!values.TryGetValue("execution-training", out string? trainingDigest)
                || !values.TryGetValue("execution-funding", out string? executionFundingText)
                || !values.TryGetValue("execution-readout", out string? executionReadoutText)
                || !values.TryGetValue("execution-fingerprint", out string? executionFingerprintText)
                || !values.TryGetValue("execution-revision", out string? executionRevisionText)
                || !values.TryGetValue("execution-fork", out string? executionFork)
                || !values.TryGetValue("execution-child", out string? executionChild)
                || !values.TryGetValue("execution-dissent-decision", out string? executedDivergenceDecisionText)
                || !values.TryGetValue("execution-dissent-outcome", out string? executedDivergenceOutcome)
                || !values.TryGetValue("execution-dissent-outcome-event", out string? executedDivergenceOutcomeEventText)
                || !values.TryGetValue("execution-dissent-outcome-payload", out string? executedDivergenceOutcomePayload)
                || !ulong.TryParse(executionFundingText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executionFunding)
                || !ulong.TryParse(executionReadoutText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executionReadout)
                || !ulong.TryParse(executionFingerprintText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong executionFingerprint)
                || !ulong.TryParse(executionRevisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong executionRevision)
                || !ulong.TryParse(executedDivergenceDecisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong executedDivergenceDecision)
                || !long.TryParse(executedDivergenceOutcomeEventText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long executedDivergenceOutcomeEventID)) return false;
            try
            {
                execution = new PaidDivergenceExecutionCorroboration(
                    new LoopClosureDigest(trainingDigest), new CortexPolicyQuotaDecisionID(executionFunding), executionReadout, executionFingerprint,
                    new GrammarRevisionID(executionRevision), new LoopClosureDigest(executionFork), new LoopClosureDigest(executionChild),
                    new CortexPolicyDecisionID(executedDivergenceDecision), new LoopClosureDigest(executedDivergenceOutcome),
                    new LoopClosureDigest(executionProbe!), new TapeEventID(executedDivergenceOutcomeEventID), executedDivergenceOutcomePayload);
                execution.Value.Validate();
            }
            catch (InvalidDataException) { return false; }
        }
        receipt = new(new PolicyBoundaryObligationID(id), baseline, candidate, horizons, arms,
            verdict.ContinuityExact, verdict.MatchedSpend, verdict.ForcedNullBehaviorExecuted, verdict.Verified,
            sourceFingerprint, sourceRevision, teacher, execution)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(fundingID),
            SourceDecisionCandidateFingerprint = sourceCandidateFingerprint,
        };
        if (execution is PaidDivergenceExecutionCorroboration executionCorroboration
            && teacher is PolicyBoundaryTeacherCorroboration teacherCorroboration
            && teacherCorroboration.ReadoutTrainingCorroborationSHA256.IsValid
            && teacherCorroboration.ReadoutTrainingCorroborationSHA256 != executionCorroboration.ReadoutTrainingCorroborationSHA256)
            return false;
        receipt.Validate(domain);
        if (!values.TryGetValue("digest", out string? digest)
            || !string.Equals(digest, PolicyBoundaryObligation.ComputeReceiptDigest(in receipt), StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool TryFlag(Dictionary<string, string> values, string key, bool expected)
        => values.TryGetValue(key, out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value == (expected ? 1 : 0);
}
