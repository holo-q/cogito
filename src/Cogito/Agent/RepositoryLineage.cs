namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RepositoryLocus = Cogito.Tool.RepositoryLocus;

/// The source-backed receipt vocabulary for the native repository loop. These records are
/// deliberately separate from the historical report corroborationes: they are ordinary in-run
/// evidence, each carrying the exact repository authority and the predecessor event that
/// admitted it. The generic LoopLineageEdgeReceipt remains the ancestry/hash-chain owner.
public interface IRepositoryLineageReceipt
{
    string Kind { get; }
    string Canonical { get; }
    void Validate();
}

internal static class RepositoryLineageReceiptCodec
{
    internal const string Prefix = "REPOSITORY-LINEAGE";

    internal static string Digest(string kind, string canonical)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\t" + canonical)));

    /// Encode fields as length-prefixed UTF-8 tokens. Delimiter joining is not
    /// injective for paths, predictions, or tool arguments that contain tabs/newlines.
    internal static string Join(params string[] fields)
    {
        StringBuilder encoded = new();
        foreach (string field in fields)
        {
            string value = field ?? throw new InvalidDataException("repository lineage canonical field is null");
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            encoded.Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(Convert.ToBase64String(bytes));
        }
        return encoded.ToString();
    }

    internal static bool TrySplit(string canonical, out string[] fields)
    {
        fields = Array.Empty<string>();
        if (canonical is null) return false;
        List<string> parsed = new();
        int offset = 0;
        try
        {
            while (offset < canonical.Length)
            {
                int colon = canonical.IndexOf(':', offset);
                if (colon <= offset
                    || !int.TryParse(canonical.AsSpan(offset, colon - offset), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int byteLength)
                    || byteLength < 0)
                    return false;
                int encodedLength = checked(((byteLength + 2) / 3) * 4);
                int encodedStart = checked(colon + 1);
                int encodedEnd = checked(encodedStart + encodedLength);
                if (encodedEnd > canonical.Length) return false;
                byte[] bytes = Convert.FromBase64String(canonical.Substring(encodedStart, encodedLength));
                if (bytes.Length != byteLength) return false;
                parsed.Add(Encoding.UTF8.GetString(bytes));
                offset = encodedEnd;
            }
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentOutOfRangeException) { return false; }
        fields = parsed.ToArray();
        return true;
    }

    internal static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    internal static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    /// Stricter than IsSHA: every digest this rail emits is written with
    /// ToHexStringLower, so a mixed-case digest did not come from the emitter.
    internal static bool IsLowerSHA(string value)
        => value is { Length: 64 } && value.All(static digit => digit is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void RequireSHA(string value, string name)
    {
        if (!IsSHA(value)) throw new InvalidDataException($"repository lineage {name} digest is malformed");
    }

    internal static void RequireID(TapeEventID value, string name)
    {
        if (value.Value < 0) throw new InvalidDataException($"repository lineage {name} is malformed");
    }

    internal static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"repository lineage {name} is empty");
    }

    internal static void RequirePolicy(CortexPolicyID policy, PolicyCanonicalStateID state, string name)
    {
        if (!policy.Equals(RepositoryNative.Policy.ID) || !RepositoryNative.Policy.IsCanonicalState(state))
            throw new InvalidDataException($"repository lineage {name} policy/canonical state authority is malformed");
    }

    internal static RepositoryCandidate RequireCandidate(RepositoryCandidateDigest digest, string canonical, RepositoryFrontierRevision frontier, string callSHA)
    {
        if (!frontier.IsValid || string.IsNullOrWhiteSpace(canonical) || !RepositoryCandidate.TryParseCanonical(canonical, out RepositoryCandidate candidate)
            || !digest.IsValid || digest != candidate.Digest)
            throw new InvalidDataException("repository lineage candidate authority is malformed");
        RequireLowerSHA(callSHA, "candidate call");
        string expectedCall = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)));
        if (!string.Equals(expectedCall, callSHA, StringComparison.Ordinal))
            throw new InvalidDataException("repository lineage candidate tool-call authority diverges");
        return candidate;
    }

    internal static void RequireCandidateSpecies(string canonical, RepositoryCandidateSpecies species)
    {
        if (!RepositoryCandidate.TryParseCanonical(canonical, out RepositoryCandidate candidate) || candidate.Species != species)
            throw new InvalidDataException("repository lineage candidate species authority diverges");
    }

    internal static void RequireFrontierSelection(string authoritySHA256, RepositoryFrontierRevision revision, int ordinal, string name)
    {
        RequireLowerSHA(authoritySHA256, name + " frontier authority");
        if (!revision.IsValid || ordinal < 0)
            throw new InvalidDataException($"repository lineage {name} frontier selection is malformed");
    }

    internal static void RequireReadout(ulong readoutFingerprint, ulong candidateFingerprint, ulong supportDigest,
        global::Cogito.Grammar.GrammarRevisionID revision)
    {
        if (readoutFingerprint == 0 || candidateFingerprint == 0 || supportDigest == 0 || revision == global::Cogito.Grammar.GrammarRevisionID.Zero)
            throw new InvalidDataException("repository lineage readout authority is malformed");
    }

    internal static void RequireDecision(CortexPolicyDecisionID decisionID, TapeEventID decisionEventID,
        CortexPolicyQuotaDecisionID fundingDecisionID, bool requireFunding = true)
    {
        if (decisionID.Value == 0 || decisionEventID.Value <= 0 || (requireFunding && fundingDecisionID.Value == 0))
            throw new InvalidDataException("repository lineage policy decision authority is malformed");
    }

    internal static void RequirePredecessor(TapeEventID eventID, LoopClosureDigest digest, string name)
    {
        if (eventID.Value <= 0 || !digest.IsValid)
            throw new InvalidDataException($"repository lineage {name} predecessor is malformed");
    }

    internal static void RequireLinkPredecessor(LoopClosureLinkSpecies species, TapeEventID eventID, LoopClosureDigest digest)
    {
        if (species == LoopClosureLinkSpecies.PreferenceDivergence)
        {
            if (eventID.Value != 0 || digest.IsValid)
                throw new InvalidDataException("repository preference divergence unexpectedly carries a predecessor");
            return;
        }
        RequirePredecessor(eventID, digest, "divergence link");
    }

    internal static void RequireLowerSHA(string value, string name)
    {
        if (value is not { Length: 64 } || value.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException($"repository lineage {name} digest is malformed");
    }

    internal static void RequireReceiptEvent(TapeEventID eventID, LoopLineageNodeID nodeID, string payloadSHA256, string name)
    {
        if (eventID.Value <= 0 || !nodeID.IsValid) throw new InvalidDataException($"repository lineage {name} event/node identity is malformed");
        RequireLowerSHA(payloadSHA256, name + " payload");
    }

    internal static void RequirePacket(TapeEventID eventID, string payloadSHA256, string name, bool required)
    {
        bool present = eventID.Value != 0 || !string.IsNullOrEmpty(payloadSHA256);
        if (!required && !present) return;
        if (eventID.Value <= 0) throw new InvalidDataException($"repository lineage {name} packet event is malformed");
        RequireLowerSHA(payloadSHA256, name + " payload");
    }

    internal static string PacketCanonical(TapeEventID eventID, string payloadSHA256)
        => string.Join(':', eventID.Value.ToString(CultureInfo.InvariantCulture), payloadSHA256 ?? "");

    internal static string FundingDecisionCanonical(CortexPolicyTrialQuotaDecision decision)
        => Join(decision.QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture), decision.Policy.Value,
            decision.CandidateFingerprint.ToString(CultureInfo.InvariantCulture), decision.QuotaStep.ToString(CultureInfo.InvariantCulture),
            decision.RequestedHorizonSteps.ToString(CultureInfo.InvariantCulture), decision.ArmCount.ToString(CultureInfo.InvariantCulture),
            decision.PlannedArmSteps.ToString(CultureInfo.InvariantCulture), decision.HeldArmSteps.ToString(CultureInfo.InvariantCulture),
            decision.Decision.ToString(), decision.UsedSteps.ToString(CultureInfo.InvariantCulture), decision.RemainingQuota.ToString(CultureInfo.InvariantCulture),
            CanonicalState(decision.CanonicalState), decision.ReadoutFingerprint.ToString(CultureInfo.InvariantCulture), decision.CandidateState.ToString(),
            decision.DenialReason.ToString(), decision.CandidateOriginStep.ToString(CultureInfo.InvariantCulture), decision.CandidateCurrentStep.ToString(CultureInfo.InvariantCulture),
            decision.CandidateRequiredStep.ToString(CultureInfo.InvariantCulture), decision.CandidateRevision.Value.ToString(CultureInfo.InvariantCulture),
            decision.AllocationIdentity, decision.AllocationDigest, decision.AllocationArmSteps.ToString(CultureInfo.InvariantCulture), decision.SeedAuditOnlyDigest);

    internal static string ChildOutcomeCanonical(LoopClosureChildOutcomeReference child)
        => Join(child.RunID, child.RelativePath, child.AuthoritySHA256.Value ?? "", child.RailSHA256.Value ?? "",
            child.ForcedDecisionID.Value.ToString(CultureInfo.InvariantCulture), child.OutcomeEventID.Value.ToString(CultureInfo.InvariantCulture),
            child.OutcomePayloadSHA256.Value ?? "", child.BeforeSeal ? "1" : "0");

    internal static string AuthorityFrontierCanonical(string authoritySHA256, RepositoryFrontierRevision revision, int ordinal,
        RepositoryCandidateSpecies species)
        => Join(authoritySHA256, revision.Value.ToString(CultureInfo.InvariantCulture), ordinal.ToString(CultureInfo.InvariantCulture), species.ToString());

    internal static string AuthorityAccessCanonical(long sequence, string entrySHA256, long entryCount = -1)
        => entryCount < 0
            ? Join(sequence.ToString(CultureInfo.InvariantCulture), entrySHA256)
            : Join(entryCount.ToString(CultureInfo.InvariantCulture), sequence.ToString(CultureInfo.InvariantCulture), entrySHA256);

    internal static void RequireFundingDecision(in CortexPolicyTrialQuotaDecision decision,
        CortexPolicyQuotaDecisionID fundingID, CortexPolicyCandidateFingerprint candidate, CortexPolicyReadoutFingerprint readout,
        PolicyCanonicalStateID canonicalState, long planned, long reserved, long charged, long remaining)
    {
        if (!decision.Policy.Equals(RepositoryNative.Policy.ID) || !decision.QuotaDecisionID.Equals(fundingID)
            || decision.CandidateFingerprint != candidate.Value || decision.ReadoutFingerprint != readout.Value
            || decision.CanonicalState != canonicalState || !Enum.IsDefined(decision.Decision)
            || !Enum.IsDefined(decision.CandidateState) || !Enum.IsDefined(decision.DenialReason)
            || decision.QuotaStep < 0 || decision.RequestedHorizonSteps < 0 || decision.ArmCount < 0
            || decision.PlannedArmSteps != planned || decision.HeldArmSteps != reserved
            || decision.UsedSteps != charged || decision.RemainingQuota != remaining
            || decision.PlannedArmSteps < 0 || decision.HeldArmSteps < 0 || decision.UsedSteps < 0 || decision.RemainingQuota < 0
            || decision.AllocationArmSteps < 0)
            throw new InvalidDataException("repository funding decision authority is malformed");
        if (decision.CandidateCurrentStep != decision.QuotaStep || decision.CandidateOriginStep < -1 || decision.CandidateRequiredStep < -1
            || decision.CandidateState == CortexPolicyTrialCandidateStates.Active && decision.CandidateRevision == global::Cogito.Grammar.GrammarRevisionID.Zero)
            throw new InvalidDataException("repository funding candidate authority is malformed");
        if (decision.AllocationArmSteps > 0
            && (string.IsNullOrWhiteSpace(decision.AllocationIdentity)
                || decision.AllocationDigest != CortexPolicyTrialAllocation.ComputeDigest(decision.Policy, CortexPolicyAuthorities.Grammar,
                    decision.AllocationArmSteps, decision.AllocationIdentity)))
            throw new InvalidDataException("repository funding allocation authority is malformed");
        switch (decision.Decision)
        {
            case CortexPolicyQuotaDecisions.Paid:
                if (decision.CandidateState != CortexPolicyTrialCandidateStates.Active || decision.DenialReason != CortexPolicyTrialDenialReasons.None
                    || decision.PlannedArmSteps <= 0 || decision.HeldArmSteps != decision.PlannedArmSteps
                    || decision.UsedSteps != decision.PlannedArmSteps || decision.AllocationArmSteps <= 0
                    || !RepositoryLineageReceiptCodec.IsLowerSHA(decision.AllocationDigest) || !RepositoryLineageReceiptCodec.IsLowerSHA(decision.SeedAuditOnlyDigest))
                    throw new InvalidDataException("repository paid decision is not fully allocated");
                break;
            case CortexPolicyQuotaDecisions.Reused:
                if (decision.CandidateState != CortexPolicyTrialCandidateStates.Active || decision.DenialReason != CortexPolicyTrialDenialReasons.None
                    || decision.PlannedArmSteps <= 0 || decision.HeldArmSteps != decision.PlannedArmSteps || decision.UsedSteps != 0
                    || decision.AllocationArmSteps <= 0 || !RepositoryLineageReceiptCodec.IsLowerSHA(decision.AllocationDigest) || !RepositoryLineageReceiptCodec.IsLowerSHA(decision.SeedAuditOnlyDigest))
                    throw new InvalidDataException("repository reused decision is not fully allocated");
                break;
            case CortexPolicyQuotaDecisions.Denied:
                if (decision.DenialReason == CortexPolicyTrialDenialReasons.None || decision.PlannedArmSteps != 0
                    || decision.HeldArmSteps != 0 || decision.UsedSteps != 0 || decision.AllocationArmSteps != 0
                    || decision.AllocationIdentity.Length != 0 || decision.AllocationDigest.Length != 0 || decision.SeedAuditOnlyDigest.Length != 0)
                    throw new InvalidDataException("repository denied decision carries allocation custody");
                break;
            default:
                throw new InvalidDataException("repository funding decision status is unknown");
        }
    }

    internal static string CanonicalState(PolicyCanonicalStateID state)
        => string.Join(':', state.Policy.Value, (byte)state.Kind, state.Version, state.Value.ToString("X16", CultureInfo.InvariantCulture));
}

public readonly record struct RepositoryReceiptAuthority(
    TapeEventID EventID,
    LoopLineageNodeID NodeID,
    string EventPayloadSHA256,
    string DecisionPayloadSHA256,
    TapeEventID ReadoutEventID,
    string ReadoutPayloadSHA256,
    TapeEventID FundingEventID,
    string FundingPayloadSHA256,
    TapeEventID BoundaryEventID,
    string BoundaryPayloadSHA256,
    TapeEventID SettlementEventID,
    string SettlementPayloadSHA256,
    string FrontierAuthoritySHA256,
    RepositoryFrontierRevision FrontierRevision,
    int SelectionOrdinal,
    RepositoryCandidateSpecies CandidateSpecies)
{
    public long AccessSequence { get; init; } = -1;
    public string AccessEntrySHA256 { get; init; } = "";
    public long AccessEntryCount { get; init; } = -1;

    public void Validate(string name, TapeEventID decisionEventID, string candidateCanonical, bool requireFunding = true, bool requireAccess = true)
    {
        RepositoryLineageReceiptCodec.RequireReceiptEvent(EventID, NodeID, EventPayloadSHA256, name);
        RepositoryLineageReceiptCodec.RequirePacket(decisionEventID, DecisionPayloadSHA256, name + " decision", required: true);
        RepositoryLineageReceiptCodec.RequirePacket(ReadoutEventID, ReadoutPayloadSHA256, name + " readout", required: true);
        RepositoryLineageReceiptCodec.RequirePacket(FundingEventID, FundingPayloadSHA256, name + " funding", required: requireFunding);
        RepositoryLineageReceiptCodec.RequirePacket(BoundaryEventID, BoundaryPayloadSHA256, name + " boundary", required: false);
        RepositoryLineageReceiptCodec.RequirePacket(SettlementEventID, SettlementPayloadSHA256, name + " settlement", required: false);
        RepositoryLineageReceiptCodec.RequireLowerSHA(DecisionPayloadSHA256, name + " decision payload");
        RepositoryLineageReceiptCodec.RequireFrontierSelection(FrontierAuthoritySHA256, FrontierRevision, SelectionOrdinal, name);
        RepositoryLineageReceiptCodec.RequireCandidateSpecies(candidateCanonical, CandidateSpecies);
        if (AccessSequence < -1)
            throw new InvalidDataException($"repository lineage {name} access sequence is malformed");
        if (requireAccess && AccessSequence < 0)
            throw new InvalidDataException($"repository lineage {name} access sequence is malformed");
        if (AccessSequence >= 0)
            RepositoryLineageReceiptCodec.RequireLowerSHA(AccessEntrySHA256, name + " access entry");
        else if (AccessEntrySHA256.Length != 0)
            throw new InvalidDataException($"repository lineage {name} missing access entry carries a digest");
        if (!requireAccess && (AccessEntryCount < 0 || AccessSequence >= AccessEntryCount))
            throw new InvalidDataException($"repository lineage {name} access aggregate count is malformed");
    }
}

public readonly record struct RepositoryAdmissionReceipt(
    int Step,
    TapeEventID ObservationEventID,
    string WorldSHA256,
    string AccessSHA256,
    string CallSHA256,
    string SourcePath,
    int SourceLine,
    string EvidenceSHA256,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public long AccessSequence { get; init; } = -1;
    public string AccessEntrySHA256 { get; init; } = "";

    // Frozen journal row kind; identifier-side name is AdmissionPlan.
    public string Kind => "world-encounter";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), ObservationEventID.Value.ToString(CultureInfo.InvariantCulture),
        WorldSHA256, AccessSHA256, AccessSequence.ToString(CultureInfo.InvariantCulture), AccessEntrySHA256,
        CallSHA256, SourcePath, SourceLine.ToString(CultureInfo.InvariantCulture), EvidenceSHA256);

    internal static RepositoryAdmissionReceipt Create(int step, TapeEventID observationEventID,
        string worldSHA256, string accessSHA256, string callSHA256, string sourcePath, int sourceLine,
        string evidenceSHA256, long accessSequence, string accessEntrySHA256)
    {
        var value = new RepositoryAdmissionReceipt(step, observationEventID, worldSHA256, accessSHA256,
            callSHA256, sourcePath, sourceLine, evidenceSHA256, "");
        value = value with { AccessSequence = accessSequence, AccessEntrySHA256 = accessEntrySHA256 };
        value = value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
        value.Validate();
        return value;
    }

    public void Validate()
    {
        if (Step < 0 || ObservationEventID.Value < 0 || SourceLine < 0) throw new InvalidDataException("repository admission receipt is malformed");
        RepositoryLineageReceiptCodec.RequireSHA(WorldSHA256, "world"); RepositoryLineageReceiptCodec.RequireSHA(AccessSHA256, "access");
        RepositoryLineageReceiptCodec.RequireSHA(CallSHA256, "call"); RepositoryLineageReceiptCodec.RequireSHA(EvidenceSHA256, "evidence");
        if (AccessSequence < 0) throw new InvalidDataException("repository admission access sequence is missing");
        RepositoryLineageReceiptCodec.RequireLowerSHA(AccessEntrySHA256, "admission access entry");
        RepositoryLineageReceiptCodec.RequireText(SourcePath, "source path"); RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository admission receipt digest diverges");
    }
}

public readonly record struct RepositoryConfirmedPredictionReceipt(
    int Step,
    RepositoryPrediction Prediction,
    RepositoryOccurrenceCheckOutcomes Outcome,
    string WorldSHA256,
    string AccessSHA256,
    string EvidenceSHA256,
    string CallSHA256,
    long EvaluatorCalls,
    long AccessCalls,
    TapeEventID PredecessorEventID,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    // Frozen journal row kind; identifier-side name is ConfirmedPrediction.
    public string Kind => "verified-claim";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), Prediction.Canonical, Outcome.ToString(), WorldSHA256, AccessSHA256,
        EvidenceSHA256, CallSHA256, RepositoryLineageReceiptCodec.I(EvaluatorCalls), RepositoryLineageReceiptCodec.I(AccessCalls),
        PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture));

    public static RepositoryConfirmedPredictionReceipt Create(int step, RepositoryPrediction prediction, RepositoryOccurrenceCheckResult result,
        string worldSHA256, string accessSHA256, string callSHA256, TapeEventID predecessorEventID)
    {
        prediction.Validate();
        var value = new RepositoryConfirmedPredictionReceipt(step, prediction, result.Outcome, worldSHA256, accessSHA256,
            result.EvidenceSHA256, callSHA256, result.EvaluatorCost, result.AccessCost, predecessorEventID, "");
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    public void Validate()
    {
        Prediction.Validate();
        if (Step < 0 || !Enum.IsDefined(Outcome) || EvaluatorCalls < 0 || AccessCalls < 0) throw new InvalidDataException("repository confirmed prediction receipt is malformed");
        RepositoryLineageReceiptCodec.RequireSHA(WorldSHA256, "world"); RepositoryLineageReceiptCodec.RequireSHA(AccessSHA256, "access");
        RepositoryLineageReceiptCodec.RequireSHA(EvidenceSHA256, "evidence"); RepositoryLineageReceiptCodec.RequireSHA(CallSHA256, "call");
        RepositoryLineageReceiptCodec.RequireID(PredecessorEventID, "prediction predecessor");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository confirmed prediction receipt digest diverges");
    }
}

public readonly record struct RepositoryConfirmedOccurrenceReceipt(
    int Step,
    string PredictionSHA256,
    string EvidenceSHA256,
    string OccurrenceSHA256,
    TapeEventID PredecessorEventID,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    // Frozen journal row kind; identifier-side name is ConfirmedOccurrence.
    public string Kind => "verified-support";
    public string Canonical => RepositoryLineageReceiptCodec.Join(RepositoryLineageReceiptCodec.I(Step), PredictionSHA256,
        EvidenceSHA256, OccurrenceSHA256, PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture));

    public static RepositoryConfirmedOccurrenceReceipt Create(int step, string predictionSHA256, string evidenceSHA256,
        string occurrenceSHA256, TapeEventID predecessorEventID)
    {
        var value = new RepositoryConfirmedOccurrenceReceipt(step, predictionSHA256, evidenceSHA256, occurrenceSHA256, predecessorEventID, "");
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    public void Validate()
    {
        if (Step < 0) throw new InvalidDataException("repository confirmed occurrence receipt is malformed");
        RepositoryLineageReceiptCodec.RequireSHA(PredictionSHA256, "prediction"); RepositoryLineageReceiptCodec.RequireSHA(EvidenceSHA256, "evidence");
        RepositoryLineageReceiptCodec.RequireSHA(OccurrenceSHA256, "occurrence"); RepositoryLineageReceiptCodec.RequireID(PredecessorEventID, "occurrence predecessor");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository confirmed occurrence receipt digest diverges");
    }
}

public readonly record struct RepositoryComposedCandidateReceipt(
    int Step,
    RepositoryPatternRuleID RuleID,
    string OccurrenceSetSHA256,
    TapeEventID[] OccurrenceReceiptEventIDs,
    string CandidateCanonical,
    RepositoryCandidateSpecies CandidateSpecies,
    RepositoryCandidateDigest CandidateDigest,
    string PredictionSHA256,
    string SourceEvidenceSHA256,
    string ComposedAdmissionPath,
    string AlternativeAdmissionPath,
    long ComposedEvaluatorCalls,
    long AlternativeEvaluatorCalls,
    long EvaluatorDelta,
    TapeEventID CompositionEventID,
    string WorldSHA256,
    string AccessSHA256,
    TapeEventID PredecessorEventID,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    // Frozen journal row kind; identifier-side name is ComposedCandidate.
    public string Kind => "derived-candidate";
    public string Canonical => RepositoryLineageReceiptCodec.Join(RepositoryLineageReceiptCodec.I(Step), RuleID.Value, OccurrenceSetSHA256,
        string.Join(',', OccurrenceReceiptEventIDs.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture))), CandidateSpecies.ToString(), CandidateCanonical,
        CandidateDigest.ToString(), PredictionSHA256, SourceEvidenceSHA256, ComposedAdmissionPath, AlternativeAdmissionPath,
        RepositoryLineageReceiptCodec.I(ComposedEvaluatorCalls), RepositoryLineageReceiptCodec.I(AlternativeEvaluatorCalls), RepositoryLineageReceiptCodec.I(EvaluatorDelta),
        CompositionEventID.Value.ToString(CultureInfo.InvariantCulture), WorldSHA256, AccessSHA256, PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture));

    public static RepositoryComposedCandidateReceipt Create(int step, RepositoryPatternRuleID ruleID,
        RepositoryPatternOccurrenceSet occurrenceSet, RepositoryCandidate candidate, TapeEventID compositionEventID,
        string composedAdmissionPath, string alternativeAdmissionPath, long composedEvaluatorCalls,
        long alternativeEvaluatorCalls, TapeEventID predecessorEventID)
    {
        ruleID.Validate();
        occurrenceSet.Validate();
        if (candidate is null || !candidate.Digest.IsValid)
            throw new InvalidDataException("repository composed candidate requires a typed candidate");
        if (composedEvaluatorCalls != 0 || alternativeEvaluatorCalls <= 0)
            throw new InvalidDataException("repository composed candidate requires zero-vs-positive evaluator paths");
        if (compositionEventID.Value <= occurrenceSet.Occurrences[^1].OccurrenceCheckReceiptEventID.Value)
            throw new InvalidDataException("repository composition event must follow occurrence receipts");
        string worldSHA = occurrenceSet.Occurrences[0].OccurrenceCheck.WorldSHA256;
        string accessSHA = occurrenceSet.Occurrences[0].OccurrenceCheck.AccessSHA256;
        if (occurrenceSet.Occurrences.Any(s => s.OccurrenceCheck.WorldSHA256 != worldSHA || s.OccurrenceCheck.AccessSHA256 != accessSHA))
            throw new InvalidDataException("repository composed candidate occurrence authority diverges");
        TapeEventID[] occurrenceReceiptIDs = occurrenceSet.Occurrences.Select(static s => s.OccurrenceCheckReceiptEventID).ToArray();
        var value = new RepositoryComposedCandidateReceipt(step, ruleID, occurrenceSet.OccurrenceSetSHA256, occurrenceReceiptIDs,
            candidate.Canonical, candidate.Species, candidate.Digest, occurrenceSet.Occurrences[0].PredictionID.Value,
            occurrenceSet.Occurrences[0].EvidenceSHA256, composedAdmissionPath, alternativeAdmissionPath,
            composedEvaluatorCalls, alternativeEvaluatorCalls, alternativeEvaluatorCalls - composedEvaluatorCalls,
            compositionEventID, worldSHA, accessSHA, predecessorEventID, "");
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    public void Validate()
    {
        RuleID.Validate();
        if (Step < 0 || !IsSHA(OccurrenceSetSHA256) || OccurrenceReceiptEventIDs is not { Length: > 0 }
            || OccurrenceReceiptEventIDs.Any(static id => id.Value < 0)
            || OccurrenceReceiptEventIDs.Zip(OccurrenceReceiptEventIDs.Skip(1)).Any(static pair => pair.First.Value >= pair.Second.Value)
            || string.IsNullOrWhiteSpace(CandidateCanonical)
            || !Enum.IsDefined(CandidateSpecies) || !CandidateDigest.IsValid || string.IsNullOrWhiteSpace(ComposedAdmissionPath)
            || string.IsNullOrWhiteSpace(AlternativeAdmissionPath) || ComposedAdmissionPath == AlternativeAdmissionPath
            || ComposedEvaluatorCalls != 0 || AlternativeEvaluatorCalls <= 0
            || EvaluatorDelta != AlternativeEvaluatorCalls || CompositionEventID.Value <= OccurrenceReceiptEventIDs[^1].Value
            || PredecessorEventID.Value < 0 || !IsSHA(WorldSHA256) || !IsSHA(AccessSHA256))
            throw new InvalidDataException("repository composed candidate receipt is malformed");
        RepositoryLineageReceiptCodec.RequireSHA(PredictionSHA256, "candidate prediction"); RepositoryLineageReceiptCodec.RequireSHA(SourceEvidenceSHA256, "candidate evidence");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (CandidateDigest != ComputeCandidateDigest(CandidateSpecies, CandidateCanonical))
            throw new InvalidDataException("repository composed candidate payload digest diverges");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository composed candidate receipt digest diverges");
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static RepositoryCandidateDigest ComputeCandidateDigest(RepositoryCandidateSpecies species, string canonical)
    {
        RepositoryCandidate candidate = species switch
        {
            RepositoryCandidateSpecies.SearchTerm when canonical.StartsWith("search-term\t", StringComparison.Ordinal)
                => RepositoryCandidate.CreateSearchTerm(new RepositorySearchTerm(canonical[12..])),
            RepositoryCandidateSpecies.ListPrefix when canonical.StartsWith("list-prefix\t", StringComparison.Ordinal)
                => RepositoryCandidate.CreateListPrefix(new RepositoryListPrefix(canonical[12..])),
            RepositoryCandidateSpecies.OpenPath when canonical.StartsWith("open-path\t", StringComparison.Ordinal)
                => RepositoryCandidate.CreateOpenPath(new RepositoryOpenPath(canonical[10..])),
            RepositoryCandidateSpecies.ReadLocus when canonical.StartsWith("read-locus\t", StringComparison.Ordinal)
                => RepositoryCandidate.CreateReadLocus(new RepositoryReadLocus(ParseLocus(canonical[11..]))),
            RepositoryCandidateSpecies.AnswerPath when canonical.StartsWith("answer-path\t", StringComparison.Ordinal)
                => RepositoryCandidate.CreateAnswerPath(new RepositoryAnswerPath(canonical[12..])),
            // Frozen canonical prefix; identifier-side name is VerifyPrediction.
            RepositoryCandidateSpecies.VerifyPrediction when canonical.StartsWith("verify-claim\t", StringComparison.Ordinal)
                && RepositoryPrediction.TryParse(canonical[13..], out RepositoryPrediction prediction)
                => RepositoryCandidate.CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(prediction)),
            _ => throw new InvalidDataException("repository composed candidate payload is not reconstructible"),
        };
        return candidate.Digest;
    }

    private static Tool.RepositoryLocus ParseLocus(string value)
    {
        int colon = value.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(value[(colon + 1)..], out int line)) throw new InvalidDataException("repository composed candidate locus is malformed");
        return new Tool.RepositoryLocus(value[..colon], line);
    }
}

/// A learned readout is a pre-contact preference: it identifies the policy's
/// selected repository candidate before admission and tool execution. Contact
/// custody belongs to the later admissionPlan receipts, never this selection row.
public readonly record struct RepositoryReadoutReceipt(
    int Step,
    string PolicyID,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    CortexPolicyDecisionID DecisionID,
    TapeEventID DecisionEventID,
    ulong ReadoutFingerprint,
    ulong CandidateFingerprint,
    ulong CandidateOccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID ReadoutRevision,
    CortexPolicyAuthorities Authority,
    CortexPolicySelectionCauses SelectionCause,
    int LaunchpadAction,
    int RawCandidateAction,
    int SelectedCandidateAction,
    int ExecutedAction,
    LoopClosureCompositionEpisodeID SourceEpisodeID,
    string SourceEpisodeSHA256,
    TapeEventID CompositionEventID,
    global::Cogito.Grammar.GrammarRevisionID CompositionRevision,
    global::Cogito.Grammar.GrammarRevisionID FoldPreviousRevision,
    global::Cogito.Grammar.GrammarRevisionID FoldRevision,
    TapeEventID[] FoldConsumedEventIDs,
    string FoldConsumedEventSHA256,
    string FoldReceiptSHA256,
    TapeEventID TeacherPacketEventID,
    TapeEventID TeacherCompositionEventID,
    TapeEventID[] TeacherEvidenceEventIDs,
    string TeacherEvidenceSHA256,
    string TeacherCorroborationSHA256,
    string TeacherProvenanceSHA256,
    TapeEventID PredecessorEventID,
    string ReceiptSHA256,
    PolicyCanonicalStateID CanonicalState = default,
    ulong ContextDigest = 0,
    int ContextActionCount = 0,
    int ContextDeliberationDepth = 0,
    string FrontierAuthoritySHA256 = "",
    RepositoryFrontierRevision FrontierRevision = default,
    int SelectionOrdinal = -1) : IRepositoryLineageReceipt
{
    public string Kind => "readout";
    public bool IsPreContactPreference => true;
    public RepositoryCandidateSpecies CandidateSpecies { get; init; }
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID, CandidateDigest.ToString(), CandidateCanonical,
        DecisionID.Value.ToString(CultureInfo.InvariantCulture), DecisionEventID.Value.ToString(CultureInfo.InvariantCulture),
        ReadoutFingerprint.ToString(CultureInfo.InvariantCulture), CandidateFingerprint.ToString(CultureInfo.InvariantCulture),
        CandidateOccurrenceDigest.ToString(CultureInfo.InvariantCulture), ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture),
        ((byte)Authority).ToString(CultureInfo.InvariantCulture), ((byte)SelectionCause).ToString(CultureInfo.InvariantCulture),
        LaunchpadAction.ToString(CultureInfo.InvariantCulture), RawCandidateAction.ToString(CultureInfo.InvariantCulture),
        SelectedCandidateAction.ToString(CultureInfo.InvariantCulture), ExecutedAction.ToString(CultureInfo.InvariantCulture),
        SourceEpisodeID.Value, SourceEpisodeSHA256, CompositionEventID.Value.ToString(CultureInfo.InvariantCulture),
        CompositionRevision.Value.ToString(CultureInfo.InvariantCulture), FoldPreviousRevision.Value.ToString(CultureInfo.InvariantCulture),
        FoldRevision.Value.ToString(CultureInfo.InvariantCulture), FoldConsumedEventSHA256, FoldReceiptSHA256,
        string.Join(',', FoldConsumedEventIDs.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture))),
        TeacherPacketEventID.Value.ToString(CultureInfo.InvariantCulture), TeacherCompositionEventID.Value.ToString(CultureInfo.InvariantCulture),
        string.Join(',', TeacherEvidenceEventIDs.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture))),
        TeacherEvidenceSHA256, TeacherCorroborationSHA256, TeacherProvenanceSHA256,
        PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture),
        RepositoryLineageReceiptCodec.CanonicalState(CanonicalState),
        ContextDigest.ToString(CultureInfo.InvariantCulture),
        ContextActionCount.ToString(CultureInfo.InvariantCulture),
        ContextDeliberationDepth.ToString(CultureInfo.InvariantCulture),
        FrontierAuthoritySHA256,
        FrontierRevision.Value.ToString(CultureInfo.InvariantCulture),
        SelectionOrdinal.ToString(CultureInfo.InvariantCulture), CandidateSpecies.ToString());

    internal RepositoryReadoutReceipt BindFrontierAuthority(string authoritySHA256,
        RepositoryFrontierRevision frontierRevision, int selectionOrdinal)
    {
        RepositoryLineageReceiptCodec.RequireSHA(authoritySHA256, "frontier authority");
        if (!frontierRevision.IsValid || selectionOrdinal < 0)
            throw new InvalidDataException("repository readout frontier authority is malformed");
        RepositoryReadoutReceipt bound = this with
        {
            FrontierAuthoritySHA256 = authoritySHA256,
            FrontierRevision = frontierRevision,
            SelectionOrdinal = selectionOrdinal,
            ReceiptSHA256 = "",
        };
        return bound with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(bound.Kind, bound.Canonical) };
    }

    internal static RepositoryReadoutReceipt Create(
        int step,
        RepositoryCandidate candidate,
        in CortexPolicyDecision decision,
        TapeEventID decisionEventID,
        in LoopClosureR4Provenance provenance)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        provenance.Validate();
        if (decision.SelectionCause != CortexPolicySelectionCauses.GrammarCandidate
            || decision.Authority != CortexPolicyAuthorities.Grammar
            || decision.ReadoutCandidateFingerprint == 0
            || decision.ReadoutCandidateOccurrenceDigest == 0)
            throw new InvalidDataException("repository readout custody requires a learned grammar candidate");
        ReadoutTrainingCorroboration training = provenance.Training;
        if (training.DecisionID != decision.DecisionID || training.DecisionEventID != decisionEventID
            || !training.Policy.Equals(decision.Policy)
            || training.SelectedCandidateFingerprint != decision.ReadoutCandidateFingerprint
            || training.SelectedCandidateOccurrenceDigest != decision.ReadoutCandidateOccurrenceDigest
            || training.SelectedCandidateRevision != decision.Readout.GrammarRevision)
            throw new InvalidDataException("repository readout custody disagrees with generic policy readout");
        return Create(step, decision.Policy.Value, candidate.Digest, candidate.Canonical,
            decision.DecisionID, decisionEventID, decision.ReadoutIdentity.Value,
            decision.ReadoutCandidateFingerprint, decision.ReadoutCandidateOccurrenceDigest,
            decision.Readout.GrammarRevision, decision.Readout.Authority, decision.Readout.SelectionCause,
            decision.Readout.LaunchpadAction, decision.Readout.RawCandidateAction,
            decision.Readout.SelectedCandidateAction, decision.Readout.ExecutedAction,
            provenance.Episode.EpisodeID, provenance.Episode.EpisodeDigest.Value,
            provenance.Episode.CompositionEventID, provenance.Episode.PreFoldRevision,
            provenance.Fold.PreviousRevision, provenance.Fold.Revision,
            provenance.Fold.ConsumedEventIDs,
            provenance.Fold.ConsumedEventDigest.Value, provenance.Fold.ReceiptDigest.Value,
            training.TeacherPacketEventID, training.TeacherCompositionEventID,
            training.TeacherEvidenceEventIDs, training.TeacherEvidenceSHA256.Value,
            provenance.Teacher.CorroborationDigest.Value, provenance.Teacher.ProvenanceDigest.Value,
            provenance.Episode.CompositionEventID, decision.ReadoutContext.CanonicalState,
            decision.ReadoutContext.ContextDigest, decision.ReadoutContext.ActionCount,
            decision.ReadoutContext.DeliberationDepth);
    }

    internal static RepositoryReadoutReceipt Create(
        int step,
        string policyID,
        RepositoryCandidateDigest candidateDigest,
        string candidateCanonical,
        CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong candidateOccurrenceDigest,
        global::Cogito.Grammar.GrammarRevisionID readoutRevision,
        CortexPolicyAuthorities authority,
        CortexPolicySelectionCauses selectionCause,
        int launchpadAction,
        int rawCandidateAction,
        int selectedCandidateAction,
        int executedAction,
        LoopClosureCompositionEpisodeID sourceEpisodeID,
        string sourceEpisodeSHA256,
        TapeEventID compositionEventID,
        global::Cogito.Grammar.GrammarRevisionID compositionRevision,
        global::Cogito.Grammar.GrammarRevisionID foldPreviousRevision,
        global::Cogito.Grammar.GrammarRevisionID foldRevision,
        IReadOnlyList<TapeEventID> foldConsumedEventIDs,
        string foldConsumedEventSHA256,
        string foldReceiptSHA256,
        TapeEventID teacherPacketEventID,
        TapeEventID teacherCompositionEventID,
        IReadOnlyList<TapeEventID> teacherEvidenceEventIDs,
        string teacherEvidenceSHA256,
        string teacherCorroborationSHA256,
        string teacherProvenanceSHA256,
        TapeEventID predecessorEventID,
        PolicyCanonicalStateID canonicalState = default,
        ulong contextDigest = 0,
        int contextActionCount = 0,
        int contextDeliberationDepth = 0)
    {
        TapeEventID[] teacherEvents = teacherEvidenceEventIDs?.ToArray()
            ?? throw new ArgumentNullException(nameof(teacherEvidenceEventIDs));
        TapeEventID[] foldEvents = foldConsumedEventIDs?.ToArray()
            ?? throw new ArgumentNullException(nameof(foldConsumedEventIDs));
        var value = new RepositoryReadoutReceipt(step, policyID, candidateDigest, candidateCanonical,
            decisionID, decisionEventID, readoutFingerprint, candidateFingerprint, candidateOccurrenceDigest,
            readoutRevision, authority, selectionCause, launchpadAction, rawCandidateAction,
            selectedCandidateAction, executedAction, sourceEpisodeID, sourceEpisodeSHA256,
            compositionEventID, compositionRevision, foldPreviousRevision, foldRevision,
            foldEvents, foldConsumedEventSHA256, foldReceiptSHA256, teacherPacketEventID, teacherCompositionEventID,
            teacherEvents, teacherEvidenceSHA256, teacherCorroborationSHA256, teacherProvenanceSHA256,
            predecessorEventID, "", canonicalState, contextDigest, contextActionCount, contextDeliberationDepth);
        if (!RepositoryCandidate.TryParseCanonical(candidateCanonical, out RepositoryCandidate parsed)
            || parsed.Digest != candidateDigest)
            throw new InvalidDataException("repository readout candidate authority is malformed");
        value = value with { CandidateSpecies = parsed.Species };
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    public void Validate()
    {
        if (Step < 0 || !string.Equals(PolicyID, RepositoryNative.Policy.ID.Value, StringComparison.Ordinal)
            || !RepositoryNative.Policy.IsCanonicalState(CanonicalState)
            || ContextDigest == 0 || ContextActionCount <= 1 || ContextDeliberationDepth < 0
            || !FrontierRevision.IsValid || SelectionOrdinal < 0
            || string.IsNullOrWhiteSpace(PolicyID) || !CandidateDigest.IsValid
            || string.IsNullOrWhiteSpace(CandidateCanonical)
            || CandidateDigest != RepositoryCandidate.ComputeDigest(CandidateCanonical)
            || DecisionID.Value == 0 || DecisionEventID.Value <= 0
            || ReadoutFingerprint == 0 || CandidateFingerprint == 0 || CandidateOccurrenceDigest == 0
            || ReadoutRevision == global::Cogito.Grammar.GrammarRevisionID.Zero
            || Authority != CortexPolicyAuthorities.Grammar
            || SelectionCause != CortexPolicySelectionCauses.GrammarCandidate
            || LaunchpadAction < 0 || RawCandidateAction < 0 || SelectedCandidateAction < 0 || ExecutedAction < 0
            || SelectedCandidateAction != ExecutedAction
            || !SourceEpisodeID.IsValid || CompositionEventID.Value <= 0
            || CompositionRevision == global::Cogito.Grammar.GrammarRevisionID.Zero
            || FoldPreviousRevision == global::Cogito.Grammar.GrammarRevisionID.Zero
            || FoldRevision.CompareTo(FoldPreviousRevision) <= 0
            || CompositionRevision.CompareTo(FoldRevision) > 0
            || FoldRevision.CompareTo(ReadoutRevision) >= 0
            || FoldConsumedEventIDs is not { Length: > 0 }
            || TeacherPacketEventID.Value <= 0 || TeacherCompositionEventID.Value <= 0
            || TeacherEvidenceEventIDs is not { Length: > 0 }
            || PredecessorEventID.Value <= 0)
            throw new InvalidDataException("repository readout receipt is malformed");
        if (!RepositoryCandidate.TryParseCanonical(CandidateCanonical, out RepositoryCandidate candidate)
            || candidate.Digest != CandidateDigest || candidate.Species != CandidateSpecies)
            throw new InvalidDataException("repository readout candidate grammar or species diverges");
        RepositoryLineageReceiptCodec.RequireSHA(SourceEpisodeSHA256, "source episode");
        RepositoryLineageReceiptCodec.RequireSHA(FrontierAuthoritySHA256, "frontier authority");
        RepositoryLineageReceiptCodec.RequireSHA(FoldConsumedEventSHA256, "fold evidence");
        RepositoryLineageReceiptCodec.RequireSHA(FoldReceiptSHA256, "fold receipt");
        RepositoryLineageReceiptCodec.RequireSHA(TeacherEvidenceSHA256, "teacher evidence");
        RepositoryLineageReceiptCodec.RequireSHA(TeacherCorroborationSHA256, "teacher corroboration");
        RepositoryLineageReceiptCodec.RequireSHA(TeacherProvenanceSHA256, "teacher provenance");
        // A lambda inside a struct cannot reach `this`; the fold set is read out first.
        IReadOnlyList<TapeEventID> folded = FoldConsumedEventIDs;
        if (!FoldConsumedEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(FoldConsumedEventIDs))
            || FoldConsumedEventIDs.Any(static id => id.Value <= 0)
            || !FoldConsumedEventIDs.Contains(TeacherPacketEventID)
            || !FoldConsumedEventIDs.Contains(TeacherCompositionEventID)
            || TeacherEvidenceEventIDs.Any(id => !folded.Contains(id))
            || !TeacherEvidenceEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(TeacherEvidenceEventIDs))
            || TeacherEvidenceEventIDs.Any(static id => id.Value <= 0)
            || CompositionEventID.Value >= TeacherPacketEventID.Value
            || PredecessorEventID != CompositionEventID)
            throw new InvalidDataException("repository readout teacher custody is not canonical");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical))
            throw new InvalidDataException("repository readout receipt digest diverges");
    }
}

public readonly record struct RepositoryFundingReceipt(
    int Step,
    CortexPolicyID PolicyID,
    CortexPolicyDecisionID DecisionID,
    TapeEventID DecisionEventID,
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicyReadoutFingerprint ReadoutFingerprint,
    CortexPolicyCandidateFingerprint CandidateFingerprint,
    ulong CandidateOccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID ReadoutRevision,
    PolicyCanonicalStateID CanonicalState,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    RepositoryFrontierRevision FrontierRevision,
    string WorldSHA256,
    string AccessSHA256,
    string CallSHA256,
    long PlannedArmSteps,
    long HeldArmSteps,
    long UsedSteps,
    long RemainingQuota,
    TapeEventID PredecessorEventID,
    LoopClosureDigest PredecessorDigest,
    CortexPolicyTrialQuotaDecision FundingDecision,
    RepositoryReceiptAuthority Authority,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public TapeEventID EventID => Authority.EventID;
    public LoopLineageNodeID NodeID => Authority.NodeID;
    public string EventPayloadSHA256 => Authority.EventPayloadSHA256;
    public string DecisionPayloadSHA256 => Authority.DecisionPayloadSHA256;
    public TapeEventID ReadoutEventID => Authority.ReadoutEventID;
    public string ReadoutPayloadSHA256 => Authority.ReadoutPayloadSHA256;
    public TapeEventID FundingEventID => Authority.FundingEventID;
    public string FundingPayloadSHA256 => Authority.FundingPayloadSHA256;
    public TapeEventID BoundaryEventID => Authority.BoundaryEventID;
    public string BoundaryPayloadSHA256 => Authority.BoundaryPayloadSHA256;
    public TapeEventID SettlementEventID => Authority.SettlementEventID;
    public string SettlementPayloadSHA256 => Authority.SettlementPayloadSHA256;
    public string Kind => "funding";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID.Value, DecisionID.Value.ToString(CultureInfo.InvariantCulture),
        DecisionEventID.Value.ToString(CultureInfo.InvariantCulture), QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture),
        ReadoutFingerprint.ToString(), CandidateFingerprint.ToString(), CandidateOccurrenceDigest.ToString(CultureInfo.InvariantCulture),
        ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture), RepositoryLineageReceiptCodec.CanonicalState(CanonicalState),
        CandidateDigest.ToString(), CandidateCanonical, FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), WorldSHA256,
        AccessSHA256, CallSHA256, RepositoryLineageReceiptCodec.I(PlannedArmSteps), RepositoryLineageReceiptCodec.I(HeldArmSteps),
        RepositoryLineageReceiptCodec.I(UsedSteps), RepositoryLineageReceiptCodec.I(RemainingQuota),
        PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture),
        PredecessorDigest.Value, EventID.Value.ToString(CultureInfo.InvariantCulture), NodeID.Value,
        DecisionPayloadSHA256, RepositoryLineageReceiptCodec.PacketCanonical(ReadoutEventID, ReadoutPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(FundingEventID, FundingPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(BoundaryEventID, BoundaryPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(SettlementEventID, SettlementPayloadSHA256),
        RepositoryLineageReceiptCodec.FundingDecisionCanonical(FundingDecision),
        RepositoryLineageReceiptCodec.AuthorityFrontierCanonical(Authority.FrontierAuthoritySHA256, Authority.FrontierRevision,
            Authority.SelectionOrdinal, Authority.CandidateSpecies),
        RepositoryLineageReceiptCodec.AuthorityAccessCanonical(Authority.AccessSequence, Authority.AccessEntrySHA256, Authority.AccessEntryCount));

    public static RepositoryFundingReceipt Create(int step, CortexPolicyID policyID, CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID, CortexPolicyQuotaDecisionID fundingDecisionID, CortexPolicyReadoutFingerprint readoutFingerprint,
        CortexPolicyCandidateFingerprint candidateFingerprint, ulong candidateOccurrenceDigest,
        global::Cogito.Grammar.GrammarRevisionID readoutRevision, PolicyCanonicalStateID canonicalState,
        RepositoryCandidateDigest candidateDigest, string candidateCanonical, RepositoryFrontierRevision frontierRevision,
        string worldSHA256, string accessSHA256, string callSHA256, long plannedArmSteps, long reservedArmSteps,
        long chargedSteps, long remainingBudget, TapeEventID predecessorEventID, LoopClosureDigest predecessorDigest,
        CortexPolicyTrialQuotaDecision fundingDecision, RepositoryReceiptAuthority authority)
    {
        var value = new RepositoryFundingReceipt(step, policyID, decisionID, decisionEventID, fundingDecisionID,
            readoutFingerprint, candidateFingerprint, candidateOccurrenceDigest, readoutRevision, canonicalState,
            candidateDigest, candidateCanonical, frontierRevision, worldSHA256, accessSHA256, callSHA256,
            plannedArmSteps, reservedArmSteps, chargedSteps, remainingBudget, predecessorEventID, predecessorDigest, fundingDecision, authority, "");
        value.ValidateAuthority();
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    private void ValidateAuthority()
    {
        if (Step < 0) throw new InvalidDataException("repository funding receipt step is malformed");
        RepositoryLineageReceiptCodec.RequirePolicy(PolicyID, CanonicalState, "funding");
        RepositoryLineageReceiptCodec.RequireDecision(DecisionID, DecisionEventID, QuotaDecisionID);
        RepositoryLineageReceiptCodec.RequireReadout(ReadoutFingerprint.Value, CandidateFingerprint.Value, CandidateOccurrenceDigest, ReadoutRevision);
        RepositoryCandidate parsedCandidate = RepositoryLineageReceiptCodec.RequireCandidate(CandidateDigest, CandidateCanonical, FrontierRevision, CallSHA256);
        if (Authority.FrontierRevision != FrontierRevision || Authority.CandidateSpecies != parsedCandidate.Species)
            throw new InvalidDataException("repository funding frontier selection authority diverges");
        RepositoryLineageReceiptCodec.RequireLowerSHA(WorldSHA256, "funding world");
        RepositoryLineageReceiptCodec.RequireLowerSHA(AccessSHA256, "funding access");
        RepositoryLineageReceiptCodec.RequireLowerSHA(CallSHA256, "funding call");
        if (FundingDecision.Decision != CortexPolicyQuotaDecisions.Denied && PlannedArmSteps <= 0
            || HeldArmSteps < 0 || UsedSteps < 0 || HeldArmSteps > PlannedArmSteps
            || UsedSteps > HeldArmSteps || RemainingQuota < 0) throw new InvalidDataException("repository funding economics are malformed");
        Authority.Validate("funding", DecisionEventID, CandidateCanonical,
            FundingDecision.Decision != CortexPolicyQuotaDecisions.Denied, requireAccess: false);
        RepositoryLineageReceiptCodec.RequireFundingDecision(FundingDecision, QuotaDecisionID, CandidateFingerprint,
            ReadoutFingerprint, CanonicalState, PlannedArmSteps, HeldArmSteps, UsedSteps, RemainingQuota);
        RepositoryLineageReceiptCodec.RequirePredecessor(PredecessorEventID, PredecessorDigest, "funding");
    }

    public void Validate()
    {
        ValidateAuthority();
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository funding receipt digest diverges");
    }

    internal RepositoryFundingReceipt BindEventPayloadSHA()
    {
        string encodedCanonical = Convert.ToBase64String(Encoding.UTF8.GetBytes(Canonical));
        byte[] payload = Encoding.ASCII.GetBytes(
            $"REPOSITORY-LINEAGE\tkind={Kind}\tdigest={Encoding.UTF8.GetByteCount(Canonical)}:{RepositoryLineageReceiptCodec.Digest(Kind, Canonical)}\tcanonical={encodedCanonical}");
        string payloadSHA = Convert.ToHexStringLower(SHA256.HashData(payload));
        RepositoryFundingReceipt bound = this with
        {
            Authority = Authority with { EventPayloadSHA256 = payloadSHA },
            ReceiptSHA256 = "",
        };
        return bound with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(bound.Kind, bound.Canonical) };
    }
}

public readonly record struct RepositoryAdjudicatedOutcomeReceipt(
    int Step,
    CortexPolicyID PolicyID,
    CortexPolicyDecisionID DecisionID,
    TapeEventID DecisionEventID,
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicyReadoutFingerprint ReadoutFingerprint,
    CortexPolicyCandidateFingerprint CandidateFingerprint,
    ulong CandidateOccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID ReadoutRevision,
    PolicyCanonicalStateID CanonicalState,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    RepositoryFrontierRevision FrontierRevision,
    string WorldSHA256,
    string AccessSHA256,
    string CallSHA256,
    long PlannedArmSteps,
    long ActualExecutedArmSteps,
    long ReclaimedOrUnused,
    long? EvaluatorWorkUnits,
    CortexPolicyVerifierOutcomes VerifierOutcome,
    long? WallMilliseconds,
    bool Executed,
    LoopClosureDigest ForkArmSHA256,
    LoopClosureDigest ChildExecutionReceiptSHA256,
    CortexPolicyDecisionID ExecutedDivergenceDecisionID,
    LoopClosureDigest ExecutedDivergenceOutcomeID,
    TapeEventID OutcomeEventID,
    string OutcomePayloadSHA256,
    TapeEventID PredecessorEventID,
    LoopClosureDigest PredecessorDigest,
    LoopClosureChildOutcomeReference ChildOutcome,
    RepositoryReceiptAuthority Authority,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public TapeEventID EventID => Authority.EventID;
    public LoopLineageNodeID NodeID => Authority.NodeID;
    public string EventPayloadSHA256 => Authority.EventPayloadSHA256;
    public string DecisionPayloadSHA256 => Authority.DecisionPayloadSHA256;
    public TapeEventID ReadoutEventID => Authority.ReadoutEventID;
    public string ReadoutPayloadSHA256 => Authority.ReadoutPayloadSHA256;
    public TapeEventID FundingEventID => Authority.FundingEventID;
    public string FundingPayloadSHA256 => Authority.FundingPayloadSHA256;
    public TapeEventID BoundaryEventID => Authority.BoundaryEventID;
    public string BoundaryPayloadSHA256 => Authority.BoundaryPayloadSHA256;
    public TapeEventID SettlementEventID => Authority.SettlementEventID;
    public string SettlementPayloadSHA256 => Authority.SettlementPayloadSHA256;
    public string Kind => "adjudicated-outcome";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID.Value, DecisionID.Value.ToString(CultureInfo.InvariantCulture),
        DecisionEventID.Value.ToString(CultureInfo.InvariantCulture), QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture),
        ReadoutFingerprint.ToString(), CandidateFingerprint.ToString(), CandidateOccurrenceDigest.ToString(CultureInfo.InvariantCulture),
        ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture), RepositoryLineageReceiptCodec.CanonicalState(CanonicalState),
        CandidateDigest.ToString(), CandidateCanonical, FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), WorldSHA256,
        AccessSHA256, CallSHA256, RepositoryLineageReceiptCodec.I(PlannedArmSteps), RepositoryLineageReceiptCodec.I(ActualExecutedArmSteps),
        RepositoryLineageReceiptCodec.I(ReclaimedOrUnused), EvaluatorWorkUnits?.ToString(CultureInfo.InvariantCulture) ?? "none", VerifierOutcome.ToString(),
        WallMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "none", Executed ? "1" : "0", ForkArmSHA256.Value ?? "",
        ChildExecutionReceiptSHA256.Value ?? "", ExecutedDivergenceDecisionID.Value.ToString(CultureInfo.InvariantCulture), ExecutedDivergenceOutcomeID.Value ?? "",
        OutcomeEventID.Value.ToString(CultureInfo.InvariantCulture), OutcomePayloadSHA256,
        PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture), PredecessorDigest.Value,
        EventID.Value.ToString(CultureInfo.InvariantCulture), NodeID.Value, EventPayloadSHA256, DecisionPayloadSHA256,
        RepositoryLineageReceiptCodec.PacketCanonical(ReadoutEventID, ReadoutPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(FundingEventID, FundingPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(BoundaryEventID, BoundaryPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(SettlementEventID, SettlementPayloadSHA256),
        RepositoryLineageReceiptCodec.ChildOutcomeCanonical(ChildOutcome),
        RepositoryLineageReceiptCodec.AuthorityFrontierCanonical(Authority.FrontierAuthoritySHA256, Authority.FrontierRevision,
            Authority.SelectionOrdinal, Authority.CandidateSpecies),
        RepositoryLineageReceiptCodec.AuthorityAccessCanonical(Authority.AccessSequence, Authority.AccessEntrySHA256));

    public static RepositoryAdjudicatedOutcomeReceipt Create(int step, CortexPolicyID policyID, CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID, CortexPolicyQuotaDecisionID fundingDecisionID, CortexPolicyReadoutFingerprint readoutFingerprint,
        CortexPolicyCandidateFingerprint candidateFingerprint, ulong candidateOccurrenceDigest,
        global::Cogito.Grammar.GrammarRevisionID readoutRevision, PolicyCanonicalStateID canonicalState,
        RepositoryCandidateDigest candidateDigest, string candidateCanonical, RepositoryFrontierRevision frontierRevision,
        string worldSHA256, string accessSHA256, string callSHA256, long plannedArmSteps, long actualExecutedArmSteps,
        long refundOrSlack, long? evaluatorWorkUnits, CortexPolicyVerifierOutcomes verifierOutcome, long? wallMilliseconds,
        bool executed, LoopClosureDigest forkArmSHA256, LoopClosureDigest childExecutionReceiptSHA256,
        CortexPolicyDecisionID executedDivergenceDecisionID, LoopClosureDigest executedDivergenceOutcomeID,
        TapeEventID outcomeEventID, string outcomePayloadSHA256, TapeEventID predecessorEventID, LoopClosureDigest predecessorDigest,
        LoopClosureChildOutcomeReference childOutcome, RepositoryReceiptAuthority authority)
    {
        var value = new RepositoryAdjudicatedOutcomeReceipt(step, policyID, decisionID, decisionEventID, fundingDecisionID,
            readoutFingerprint, candidateFingerprint, candidateOccurrenceDigest, readoutRevision, canonicalState,
            candidateDigest, candidateCanonical, frontierRevision, worldSHA256, accessSHA256, callSHA256, plannedArmSteps,
            actualExecutedArmSteps, refundOrSlack, evaluatorWorkUnits, verifierOutcome, wallMilliseconds, executed,
            forkArmSHA256, childExecutionReceiptSHA256, executedDivergenceDecisionID, executedDivergenceOutcomeID,
            outcomeEventID, outcomePayloadSHA256, predecessorEventID, predecessorDigest, childOutcome, authority, "");
        value.ValidateAuthority();
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    private void ValidateAuthority()
    {
        if (Step < 0) throw new InvalidDataException("repository outcome step is malformed");
        RepositoryLineageReceiptCodec.RequirePolicy(PolicyID, CanonicalState, "outcome");
        RepositoryLineageReceiptCodec.RequireDecision(DecisionID, DecisionEventID, QuotaDecisionID);
        RepositoryLineageReceiptCodec.RequireReadout(ReadoutFingerprint.Value, CandidateFingerprint.Value, CandidateOccurrenceDigest, ReadoutRevision);
        RepositoryCandidate parsedCandidate = RepositoryLineageReceiptCodec.RequireCandidate(CandidateDigest, CandidateCanonical, FrontierRevision, CallSHA256);
        if (Authority.FrontierRevision != FrontierRevision || Authority.CandidateSpecies != parsedCandidate.Species)
            throw new InvalidDataException("repository outcome frontier selection authority diverges");
        RepositoryLineageReceiptCodec.RequireLowerSHA(WorldSHA256, "outcome world"); RepositoryLineageReceiptCodec.RequireLowerSHA(AccessSHA256, "outcome access");
        RepositoryLineageReceiptCodec.RequireLowerSHA(CallSHA256, "outcome call");
        if (PlannedArmSteps <= 0 || ActualExecutedArmSteps < 0 || ReclaimedOrUnused < 0 || ActualExecutedArmSteps + ReclaimedOrUnused != PlannedArmSteps
            || EvaluatorWorkUnits is < 0 || WallMilliseconds is < 0 || !Enum.IsDefined(VerifierOutcome))
            throw new InvalidDataException("repository outcome settlement is malformed");
        if (Executed != (OutcomeEventID.Value > 0)) throw new InvalidDataException("repository outcome execution state disagrees with outcome event identity");
        ChildOutcome.Validate(Executed);
        if ((OutcomeEventID.Value == 0) != string.IsNullOrEmpty(OutcomePayloadSHA256)
            || (!string.IsNullOrEmpty(OutcomePayloadSHA256) && OutcomePayloadSHA256.Length != 64))
            throw new InvalidDataException("repository outcome payload custody is malformed");
        if (!string.IsNullOrEmpty(OutcomePayloadSHA256) && OutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("repository outcome payload digest is not canonical lowercase SHA-256");
        if (Executed)
        {
            if (!ForkArmSHA256.IsValid || !ChildExecutionReceiptSHA256.IsValid || ExecutedDivergenceDecisionID.Value == 0 || !ExecutedDivergenceOutcomeID.IsValid)
                throw new InvalidDataException("repository executed outcome omits fork or child execution corroboration");
        }
        else if (ForkArmSHA256.IsValid || ChildExecutionReceiptSHA256.IsValid || ExecutedDivergenceDecisionID.Value != 0 || ExecutedDivergenceOutcomeID.IsValid)
            throw new InvalidDataException("repository unexecuted outcome carries fabricated child execution custody");
        Authority.Validate("outcome", DecisionEventID, CandidateCanonical);
        RepositoryLineageReceiptCodec.RequirePredecessor(PredecessorEventID, PredecessorDigest, "outcome");
    }

    public void Validate()
    {
        ValidateAuthority();
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository outcome receipt digest diverges");
    }
}

public enum RepositoryOutcomePayloadSources : byte
{
    ActionExecution,
    WorldObservation,
}

/// Ordinary repository execution custody. This is deliberately separate from
/// RepositoryAdjudicatedOutcomeReceipt: the latter proves a forced child fork,
/// while a native repository action has no child identity to fabricate.
public readonly record struct RepositoryPaidOutcomeReceipt(
    int Step,
    CortexPolicyID PolicyID,
    CortexPolicyDecisionID DecisionID,
    TapeEventID DecisionEventID,
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicyReadoutFingerprint ReadoutFingerprint,
    CortexPolicyCandidateFingerprint CandidateFingerprint,
    ulong CandidateOccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID ReadoutRevision,
    PolicyCanonicalStateID CanonicalState,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    RepositoryFrontierRevision FrontierRevision,
    string WorldSHA256,
    string AccessSHA256,
    string CallSHA256,
    long PlannedArmSteps,
    long ActualExecutedArmSteps,
    long ReclaimedOrUnused,
    long? EvaluatorWorkUnits,
    CortexPolicyVerifierOutcomes VerifierOutcome,
    long? WallMilliseconds,
    TapeEventID OutcomeEventID,
    RepositoryOutcomePayloadSources OutcomePayloadSource,
    string OutcomePayloadSHA256,
    TapeEventID PredecessorEventID,
    LoopClosureDigest PredecessorDigest,
    RepositoryReceiptAuthority Authority,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public TapeEventID EventID => Authority.EventID;
    public LoopLineageNodeID NodeID => Authority.NodeID;
    public string EventPayloadSHA256 => Authority.EventPayloadSHA256;
    public string DecisionPayloadSHA256 => Authority.DecisionPayloadSHA256;
    public TapeEventID ReadoutEventID => Authority.ReadoutEventID;
    public string ReadoutPayloadSHA256 => Authority.ReadoutPayloadSHA256;
    public TapeEventID FundingEventID => Authority.FundingEventID;
    public string FundingPayloadSHA256 => Authority.FundingPayloadSHA256;
    public TapeEventID BoundaryEventID => Authority.BoundaryEventID;
    public string BoundaryPayloadSHA256 => Authority.BoundaryPayloadSHA256;
    public TapeEventID SettlementEventID => Authority.SettlementEventID;
    public string SettlementPayloadSHA256 => Authority.SettlementPayloadSHA256;
    public string Kind => "repository-outcome";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID.Value, DecisionID.Value.ToString(CultureInfo.InvariantCulture),
        DecisionEventID.Value.ToString(CultureInfo.InvariantCulture), QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture),
        ReadoutFingerprint.ToString(), CandidateFingerprint.ToString(), CandidateOccurrenceDigest.ToString(CultureInfo.InvariantCulture),
        ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture), RepositoryLineageReceiptCodec.CanonicalState(CanonicalState),
        CandidateDigest.ToString(), CandidateCanonical, FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), WorldSHA256,
        AccessSHA256, CallSHA256, RepositoryLineageReceiptCodec.I(PlannedArmSteps), RepositoryLineageReceiptCodec.I(ActualExecutedArmSteps),
        RepositoryLineageReceiptCodec.I(ReclaimedOrUnused), EvaluatorWorkUnits?.ToString(CultureInfo.InvariantCulture) ?? "none",
        VerifierOutcome.ToString(), WallMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "none",
        OutcomeEventID.Value.ToString(CultureInfo.InvariantCulture), OutcomePayloadSource.ToString(), OutcomePayloadSHA256,
        PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture), PredecessorDigest.Value,
        EventID.Value.ToString(CultureInfo.InvariantCulture), NodeID.Value, DecisionPayloadSHA256,
        RepositoryLineageReceiptCodec.PacketCanonical(ReadoutEventID, ReadoutPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(FundingEventID, FundingPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(BoundaryEventID, BoundaryPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(SettlementEventID, SettlementPayloadSHA256),
        RepositoryLineageReceiptCodec.AuthorityFrontierCanonical(Authority.FrontierAuthoritySHA256, Authority.FrontierRevision,
            Authority.SelectionOrdinal, Authority.CandidateSpecies),
        RepositoryLineageReceiptCodec.AuthorityAccessCanonical(Authority.AccessSequence, Authority.AccessEntrySHA256, Authority.AccessEntryCount));

    public static RepositoryPaidOutcomeReceipt Create(int step, CortexPolicyID policyID, CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID, CortexPolicyQuotaDecisionID fundingDecisionID, CortexPolicyReadoutFingerprint readoutFingerprint,
        CortexPolicyCandidateFingerprint candidateFingerprint, ulong candidateOccurrenceDigest,
        global::Cogito.Grammar.GrammarRevisionID readoutRevision, PolicyCanonicalStateID canonicalState,
        RepositoryCandidateDigest candidateDigest, string candidateCanonical, RepositoryFrontierRevision frontierRevision,
        string worldSHA256, string accessSHA256, string callSHA256, long plannedArmSteps, long actualExecutedArmSteps,
        long refundOrSlack, long? evaluatorWorkUnits, CortexPolicyVerifierOutcomes verifierOutcome, long? wallMilliseconds,
        TapeEventID outcomeEventID, RepositoryOutcomePayloadSources outcomePayloadSource, string outcomePayloadSHA256,
        TapeEventID predecessorEventID, LoopClosureDigest predecessorDigest,
        RepositoryReceiptAuthority authority)
    {
        var value = new RepositoryPaidOutcomeReceipt(step, policyID, decisionID, decisionEventID, fundingDecisionID,
            readoutFingerprint, candidateFingerprint, candidateOccurrenceDigest, readoutRevision, canonicalState,
            candidateDigest, candidateCanonical, frontierRevision, worldSHA256, accessSHA256, callSHA256,
            plannedArmSteps, actualExecutedArmSteps, refundOrSlack, evaluatorWorkUnits, verifierOutcome, wallMilliseconds,
            outcomeEventID, outcomePayloadSource, outcomePayloadSHA256, predecessorEventID, predecessorDigest, authority, "");
        value.ValidateAuthority();
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    private void ValidateAuthority()
    {
        if (Step < 0 || OutcomeEventID.Value <= 0)
            throw new InvalidDataException("repository ordinary outcome identity is malformed");
        RepositoryLineageReceiptCodec.RequirePolicy(PolicyID, CanonicalState, "ordinary outcome");
        RepositoryLineageReceiptCodec.RequireDecision(DecisionID, DecisionEventID, QuotaDecisionID);
        RepositoryLineageReceiptCodec.RequireReadout(ReadoutFingerprint.Value, CandidateFingerprint.Value, CandidateOccurrenceDigest, ReadoutRevision);
        RepositoryCandidate parsedCandidate = RepositoryLineageReceiptCodec.RequireCandidate(CandidateDigest, CandidateCanonical, FrontierRevision, CallSHA256);
        if (Authority.FrontierRevision != FrontierRevision || Authority.CandidateSpecies != parsedCandidate.Species)
            throw new InvalidDataException("repository ordinary outcome frontier selection authority diverges");
        RepositoryLineageReceiptCodec.RequireLowerSHA(WorldSHA256, "ordinary outcome world");
        RepositoryLineageReceiptCodec.RequireLowerSHA(AccessSHA256, "ordinary outcome access");
        RepositoryLineageReceiptCodec.RequireLowerSHA(CallSHA256, "ordinary outcome call");
        if (!Enum.IsDefined(OutcomePayloadSource))
            throw new InvalidDataException("repository ordinary outcome payload source is malformed");
        RepositoryLineageReceiptCodec.RequireLowerSHA(OutcomePayloadSHA256, "ordinary outcome payload");
        if (PlannedArmSteps <= 0 || ActualExecutedArmSteps < 0 || ReclaimedOrUnused < 0
            || ActualExecutedArmSteps + ReclaimedOrUnused != PlannedArmSteps
            || EvaluatorWorkUnits is < 0 || WallMilliseconds is < 0 || !Enum.IsDefined(VerifierOutcome))
            throw new InvalidDataException("repository ordinary outcome settlement is malformed");
        Authority.Validate("ordinary outcome", DecisionEventID, CandidateCanonical, requireAccess: false);
        if (OutcomeEventID != Authority.EventID)
            throw new InvalidDataException("repository ordinary outcome event identity diverges");
        RepositoryLineageReceiptCodec.RequirePacket(SettlementEventID, SettlementPayloadSHA256, "ordinary outcome settlement", required: true);
        RepositoryLineageReceiptCodec.RequirePredecessor(PredecessorEventID, PredecessorDigest, "ordinary outcome");
    }

    public void Validate()
    {
        ValidateAuthority();
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical))
            throw new InvalidDataException("repository ordinary outcome receipt digest diverges");
    }

    internal RepositoryPaidOutcomeReceipt BindEventPayloadSHA()
    {
        string encodedCanonical = Convert.ToBase64String(Encoding.UTF8.GetBytes(Canonical));
        byte[] payload = Encoding.ASCII.GetBytes(
            $"REPOSITORY-LINEAGE\tkind={Kind}\tdigest={Encoding.UTF8.GetByteCount(Canonical)}:{RepositoryLineageReceiptCodec.Digest(Kind, Canonical)}\tcanonical={encodedCanonical}");
        RepositoryPaidOutcomeReceipt bound = this with
        {
            Authority = Authority with { EventPayloadSHA256 = Convert.ToHexStringLower(SHA256.HashData(payload)) },
            ReceiptSHA256 = "",
        };
        return bound with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(bound.Kind, bound.Canonical) };
    }
}

public readonly record struct RepositoryNewEvidenceReceipt(
    int Step,
    CortexPolicyID PolicyID,
    CortexPolicyDecisionID DecisionID,
    TapeEventID DecisionEventID,
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicyReadoutFingerprint ReadoutFingerprint,
    CortexPolicyCandidateFingerprint CandidateFingerprint,
    ulong CandidateOccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID ReadoutRevision,
    PolicyCanonicalStateID CanonicalState,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    RepositoryFrontierRevision FrontierRevision,
    string WorldSHA256,
    string AccessSHA256,
    string CallSHA256,
    RepositoryLocus SourceLocus,
    TapeEventID ObservationEventID,
    TapeEventID OutcomeEventID,
    string EvidenceSHA256,
    TapeEventID PredecessorEventID,
    LoopClosureDigest PredecessorDigest,
    RepositoryReceiptAuthority Authority,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public TapeEventID EventID => Authority.EventID;
    public LoopLineageNodeID NodeID => Authority.NodeID;
    public string EventPayloadSHA256 => Authority.EventPayloadSHA256;
    public string DecisionPayloadSHA256 => Authority.DecisionPayloadSHA256;
    public TapeEventID ReadoutEventID => Authority.ReadoutEventID;
    public string ReadoutPayloadSHA256 => Authority.ReadoutPayloadSHA256;
    public TapeEventID FundingEventID => Authority.FundingEventID;
    public string FundingPayloadSHA256 => Authority.FundingPayloadSHA256;
    public TapeEventID BoundaryEventID => Authority.BoundaryEventID;
    public string BoundaryPayloadSHA256 => Authority.BoundaryPayloadSHA256;
    public TapeEventID SettlementEventID => Authority.SettlementEventID;
    public string SettlementPayloadSHA256 => Authority.SettlementPayloadSHA256;
    public string Kind => "new-evidence";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID.Value, DecisionID.Value.ToString(CultureInfo.InvariantCulture),
        DecisionEventID.Value.ToString(CultureInfo.InvariantCulture), QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture),
        ReadoutFingerprint.ToString(), CandidateFingerprint.ToString(), CandidateOccurrenceDigest.ToString(CultureInfo.InvariantCulture),
        ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture), RepositoryLineageReceiptCodec.CanonicalState(CanonicalState),
        CandidateDigest.ToString(), CandidateCanonical, FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), WorldSHA256,
        AccessSHA256, CallSHA256, SourceLocus.Path.Value, SourceLocus.Line.ToString(CultureInfo.InvariantCulture),
        ObservationEventID.Value.ToString(CultureInfo.InvariantCulture), OutcomeEventID.Value.ToString(CultureInfo.InvariantCulture),
        EvidenceSHA256, PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture), PredecessorDigest.Value,
        EventID.Value.ToString(CultureInfo.InvariantCulture), NodeID.Value, DecisionPayloadSHA256,
        RepositoryLineageReceiptCodec.PacketCanonical(ReadoutEventID, ReadoutPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(FundingEventID, FundingPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(BoundaryEventID, BoundaryPayloadSHA256),
        RepositoryLineageReceiptCodec.PacketCanonical(SettlementEventID, SettlementPayloadSHA256),
        RepositoryLineageReceiptCodec.AuthorityFrontierCanonical(Authority.FrontierAuthoritySHA256, Authority.FrontierRevision,
            Authority.SelectionOrdinal, Authority.CandidateSpecies),
        RepositoryLineageReceiptCodec.AuthorityAccessCanonical(Authority.AccessSequence, Authority.AccessEntrySHA256));

    public static RepositoryNewEvidenceReceipt Create(int step, CortexPolicyID policyID, CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID, CortexPolicyQuotaDecisionID fundingDecisionID, CortexPolicyReadoutFingerprint readoutFingerprint,
        CortexPolicyCandidateFingerprint candidateFingerprint, ulong candidateOccurrenceDigest,
        global::Cogito.Grammar.GrammarRevisionID readoutRevision, PolicyCanonicalStateID canonicalState,
        RepositoryCandidateDigest candidateDigest, string candidateCanonical, RepositoryFrontierRevision frontierRevision,
        string worldSHA256, string accessSHA256, string callSHA256, RepositoryLocus sourceLocus,
        TapeEventID observationEventID, TapeEventID outcomeEventID, string evidenceSHA256,
        TapeEventID predecessorEventID, LoopClosureDigest predecessorDigest, RepositoryReceiptAuthority authority)
    {
        var value = new RepositoryNewEvidenceReceipt(step, policyID, decisionID, decisionEventID, fundingDecisionID,
            readoutFingerprint, candidateFingerprint, candidateOccurrenceDigest, readoutRevision, canonicalState,
            candidateDigest, candidateCanonical, frontierRevision, worldSHA256, accessSHA256, callSHA256,
            sourceLocus, observationEventID, outcomeEventID, evidenceSHA256, predecessorEventID, predecessorDigest, authority, "");
        value.ValidateAuthority();
        return value with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(value.Kind, value.Canonical) };
    }

    private void ValidateAuthority()
    {
        if (Step < 0 || string.IsNullOrWhiteSpace(SourceLocus.Path.Value) || SourceLocus.Line < 1 || ObservationEventID.Value <= 0)
            throw new InvalidDataException("repository new evidence source locus is malformed");
        RepositoryLineageReceiptCodec.RequirePolicy(PolicyID, CanonicalState, "new evidence");
        RepositoryLineageReceiptCodec.RequireDecision(DecisionID, DecisionEventID, QuotaDecisionID);
        RepositoryLineageReceiptCodec.RequireReadout(ReadoutFingerprint.Value, CandidateFingerprint.Value, CandidateOccurrenceDigest, ReadoutRevision);
        RepositoryCandidate parsedCandidate = RepositoryLineageReceiptCodec.RequireCandidate(CandidateDigest, CandidateCanonical, FrontierRevision, CallSHA256);
        if (Authority.FrontierRevision != FrontierRevision || Authority.CandidateSpecies != parsedCandidate.Species)
            throw new InvalidDataException("repository evidence frontier selection authority diverges");
        RepositoryLineageReceiptCodec.RequireLowerSHA(WorldSHA256, "new evidence world"); RepositoryLineageReceiptCodec.RequireLowerSHA(AccessSHA256, "new evidence access");
        RepositoryLineageReceiptCodec.RequireLowerSHA(CallSHA256, "new evidence call"); RepositoryLineageReceiptCodec.RequireLowerSHA(EvidenceSHA256, "new evidence");
        if (EventID != Authority.EventID || OutcomeEventID.Value <= 0)
            throw new InvalidDataException("repository new evidence event identity is malformed");
        Authority.Validate("new evidence", DecisionEventID, CandidateCanonical);
        RepositoryLineageReceiptCodec.RequirePacket(SettlementEventID, SettlementPayloadSHA256, "new evidence settlement", required: true);
        RepositoryLineageReceiptCodec.RequirePredecessor(PredecessorEventID, PredecessorDigest, "new evidence");
    }

    public void Validate()
    {
        ValidateAuthority();
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical)) throw new InvalidDataException("repository new evidence receipt digest diverges");
    }

    internal RepositoryNewEvidenceReceipt BindEventPayloadSHA()
    {
        string encodedCanonical = Convert.ToBase64String(Encoding.UTF8.GetBytes(Canonical));
        byte[] payload = Encoding.ASCII.GetBytes(
            $"REPOSITORY-LINEAGE\tkind={Kind}\tdigest={Encoding.UTF8.GetByteCount(Canonical)}:{RepositoryLineageReceiptCodec.Digest(Kind, Canonical)}\tcanonical={encodedCanonical}");
        RepositoryNewEvidenceReceipt bound = this with
        {
            Authority = Authority with { EventPayloadSHA256 = Convert.ToHexStringLower(SHA256.HashData(payload)) },
            ReceiptSHA256 = "",
        };
        return bound with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(bound.Kind, bound.Canonical) };
    }
}

/// Checkpoint codec for the native receipt vocabulary.  It is intentionally a
/// byte-for-byte typed codec: no receipt is reconstructed from an event or a
/// report that may not exist yet.
internal static class RepositoryLineageReceiptCheckpoint
{
    internal const uint Section = 0x52524C43; // RRLC
    internal const uint Magic = 0x524C5237; // RLR7
    internal const ushort SchemaVersion = 7;
    internal const int MaxReceiptsPerSection = 1_000_000;
    internal const int MaxReceiptsTotal = 2_000_000;

    internal static void WriteState(CkptWriter writer, PolicyCanonicalStateID state)
    {
        writer.Bool(state.Version != 0);
        if (state.Version == 0) return;
        writer.Str(state.Policy.Value); writer.U8((byte)state.Kind); writer.U16(state.Version); writer.U64(state.Value);
    }

    internal static PolicyCanonicalStateID ReadState(CkptReader reader)
    {
        if (!reader.Bool()) return default;
        return new(new CortexPolicyID(reader.Str()), (PolicyCanonicalStateKinds)reader.U8(), reader.U16(), reader.U64());
    }

    internal static void WriteAuthority(CkptWriter writer, CortexPolicyID policy, CortexPolicyDecisionID decision,
        TapeEventID decisionEvent, CortexPolicyQuotaDecisionID funding, CortexPolicyReadoutFingerprint readout,
        CortexPolicyCandidateFingerprint candidate, ulong occurrence, global::Cogito.Grammar.GrammarRevisionID revision,
        PolicyCanonicalStateID state, RepositoryCandidateDigest digest, string canonical, RepositoryFrontierRevision frontier,
        string world, string access, string call)
    {
        writer.Str(policy.Value); writer.U64(decision.Value); writer.I64(decisionEvent.Value); writer.U64(funding.Value);
        writer.U64(readout.Value); writer.U64(candidate.Value); writer.U64(occurrence); writer.U64(revision.Value); WriteState(writer, state);
        writer.U64(digest.Value); writer.Str(canonical); writer.U64(frontier.Value); writer.Str(world); writer.Str(access); writer.Str(call);
    }

    internal static void ReadAuthority(CkptReader reader, out CortexPolicyID policy, out CortexPolicyDecisionID decision,
        out TapeEventID decisionEvent, out CortexPolicyQuotaDecisionID funding, out CortexPolicyReadoutFingerprint readout,
        out CortexPolicyCandidateFingerprint candidate, out ulong occurrence, out global::Cogito.Grammar.GrammarRevisionID revision,
        out PolicyCanonicalStateID state, out RepositoryCandidateDigest digest, out string canonical, out RepositoryFrontierRevision frontier,
        out string world, out string access, out string call)
    {
        policy = new(reader.Str()); decision = new(reader.U64()); decisionEvent = new(reader.I64()); funding = new(reader.U64());
        readout = new(reader.U64()); candidate = new(reader.U64()); occurrence = reader.U64(); revision = new(reader.U64()); state = ReadState(reader);
        digest = new(reader.U64()); canonical = reader.Str(); frontier = new(reader.U64()); world = reader.Str(); access = reader.Str(); call = reader.Str();
    }

    internal static void WriteFundingDecision(CkptWriter writer, in CortexPolicyTrialQuotaDecision decision)
    {
        writer.U64(decision.QuotaDecisionID.Value); writer.Str(decision.Policy.Value); writer.U64(decision.CandidateFingerprint);
        writer.I32(decision.QuotaStep); writer.I32(decision.RequestedHorizonSteps); writer.I32(decision.ArmCount);
        writer.I64(decision.PlannedArmSteps); writer.I64(decision.HeldArmSteps); writer.U8((byte)decision.Decision);
        writer.I64(decision.UsedSteps); writer.I64(decision.RemainingQuota); WriteState(writer, decision.CanonicalState);
        writer.U64(decision.ReadoutFingerprint); writer.U8((byte)decision.CandidateState); writer.U8((byte)decision.DenialReason);
        writer.I32(decision.CandidateOriginStep); writer.I32(decision.CandidateCurrentStep); writer.I32(decision.CandidateRequiredStep);
        writer.U64(decision.CandidateRevision.Value); writer.Str(decision.AllocationIdentity); writer.Str(decision.AllocationDigest);
        writer.I64(decision.AllocationArmSteps); writer.Str(decision.SeedAuditOnlyDigest);
    }

    internal static CortexPolicyTrialQuotaDecision ReadFundingDecision(CkptReader reader)
    {
        CortexPolicyQuotaDecisionID fundingID = new(reader.U64()); CortexPolicyID policy = new(reader.Str()); ulong candidate = reader.U64();
        int fundingStep = reader.I32(), requestedHorizon = reader.I32(), armCount = reader.I32(); long planned = reader.I64(), reserved = reader.I64();
        CortexPolicyQuotaDecisions decision = (CortexPolicyQuotaDecisions)reader.U8(); long charged = reader.I64(), remaining = reader.I64();
        PolicyCanonicalStateID state = ReadState(reader); ulong readout = reader.U64(); CortexPolicyTrialCandidateStates candidateState = (CortexPolicyTrialCandidateStates)reader.U8();
        CortexPolicyTrialDenialReasons denial = (CortexPolicyTrialDenialReasons)reader.U8(); int origin = reader.I32(), current = reader.I32(), required = reader.I32();
        global::Cogito.Grammar.GrammarRevisionID revision = new(reader.U64()); string allocationIdentity = reader.Str(); string allocationDigest = reader.Str(); long allocationArmSteps = reader.I64(); string seed = reader.Str();
        return new CortexPolicyTrialQuotaDecision(fundingID, policy, candidate, fundingStep, requestedHorizon, armCount, planned, reserved, decision, charged, remaining)
        {
            CanonicalState = state, ReadoutFingerprint = readout, CandidateState = candidateState, DenialReason = denial,
            CandidateOriginStep = origin, CandidateCurrentStep = current, CandidateRequiredStep = required, CandidateRevision = revision,
            AllocationIdentity = allocationIdentity, AllocationDigest = allocationDigest, AllocationArmSteps = allocationArmSteps, SeedAuditOnlyDigest = seed
        };
    }

    internal static void WritePredecessor(CkptWriter writer, TapeEventID eventID, LoopClosureDigest digest)
    { writer.I64(eventID.Value); writer.Str(digest.Value); }

    internal static void ReadPredecessor(CkptReader reader, out TapeEventID eventID, out LoopClosureDigest digest)
    { eventID = new(reader.I64()); digest = new(reader.Str()); }

    internal static void WriteChildOutcome(CkptWriter writer, in LoopClosureChildOutcomeReference child)
    {
        writer.Str(child.RunID); writer.Str(child.RelativePath); writer.Str(child.AuthoritySHA256.Value ?? ""); writer.Str(child.RailSHA256.Value ?? "");
        writer.U64(child.ForcedDecisionID.Value); writer.I64(child.OutcomeEventID.Value); writer.Str(child.OutcomePayloadSHA256.Value ?? ""); writer.Bool(child.BeforeSeal);
    }

    internal static LoopClosureChildOutcomeReference ReadChildOutcome(CkptReader reader)
        => new(reader.Str(), reader.Str(), new(reader.Str()), new(reader.Str()), new(reader.U64()), new(reader.I64()), new(reader.Str()), reader.Bool());

    internal static void WriteReceiptMetadata(CkptWriter writer, TapeEventID eventID, LoopLineageNodeID nodeID,
        string eventPayload, string decisionPayload, TapeEventID readoutEvent, string readoutPayload,
        TapeEventID fundingEvent, string fundingPayload, TapeEventID boundaryEvent, string boundaryPayload,
        TapeEventID settlementEvent, string settlementPayload, string frontierAuthority, RepositoryFrontierRevision frontierRevision,
        int selectionOrdinal, RepositoryCandidateSpecies candidateSpecies, long accessSequence, string accessEntrySHA256, long accessEntryCount)
    {
        writer.I64(eventID.Value); writer.Str(nodeID.Value); writer.Str(eventPayload); writer.Str(decisionPayload);
        writer.I64(readoutEvent.Value); writer.Str(readoutPayload); writer.I64(fundingEvent.Value); writer.Str(fundingPayload);
        writer.I64(boundaryEvent.Value); writer.Str(boundaryPayload); writer.I64(settlementEvent.Value); writer.Str(settlementPayload);
        writer.Str(frontierAuthority); writer.U64(frontierRevision.Value); writer.I32(selectionOrdinal); writer.U8((byte)candidateSpecies); writer.I64(accessSequence); writer.Str(accessEntrySHA256); writer.I64(accessEntryCount);
    }

    internal static void ReadReceiptMetadata(CkptReader reader, out TapeEventID eventID, out LoopLineageNodeID nodeID,
        out string eventPayload, out string decisionPayload, out TapeEventID readoutEvent, out string readoutPayload,
        out TapeEventID fundingEvent, out string fundingPayload, out TapeEventID boundaryEvent, out string boundaryPayload,
        out TapeEventID settlementEvent, out string settlementPayload, out string frontierAuthority, out RepositoryFrontierRevision frontierRevision,
        out int selectionOrdinal, out RepositoryCandidateSpecies candidateSpecies, out long accessSequence, out string accessEntrySHA256, out long accessEntryCount)
    {
        eventID = new(reader.I64()); nodeID = new(reader.Str()); eventPayload = reader.Str(); decisionPayload = reader.Str();
        readoutEvent = new(reader.I64()); readoutPayload = reader.Str(); fundingEvent = new(reader.I64()); fundingPayload = reader.Str();
        boundaryEvent = new(reader.I64()); boundaryPayload = reader.Str(); settlementEvent = new(reader.I64()); settlementPayload = reader.Str();
        frontierAuthority = reader.Str(); frontierRevision = new(reader.U64()); selectionOrdinal = reader.I32(); candidateSpecies = (RepositoryCandidateSpecies)reader.U8(); accessSequence = reader.I64(); accessEntrySHA256 = reader.Str(); accessEntryCount = reader.I64();
    }

    internal static void Write(CkptWriter writer, in RepositoryFundingReceipt receipt)
    {
        writer.I32(receipt.Step); WriteAuthority(writer, receipt.PolicyID, receipt.DecisionID, receipt.DecisionEventID, receipt.QuotaDecisionID,
            receipt.ReadoutFingerprint, receipt.CandidateFingerprint, receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.CanonicalState,
            receipt.CandidateDigest, receipt.CandidateCanonical, receipt.FrontierRevision, receipt.WorldSHA256, receipt.AccessSHA256, receipt.CallSHA256);
        writer.I64(receipt.PlannedArmSteps); writer.I64(receipt.HeldArmSteps); writer.I64(receipt.UsedSteps); writer.I64(receipt.RemainingQuota);
        WritePredecessor(writer, receipt.PredecessorEventID, receipt.PredecessorDigest); WriteFundingDecision(writer, receipt.FundingDecision);
        WriteReceiptMetadata(writer, receipt.EventID, receipt.NodeID, receipt.EventPayloadSHA256, receipt.DecisionPayloadSHA256,
            receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256, receipt.FundingEventID, receipt.FundingPayloadSHA256,
            receipt.BoundaryEventID, receipt.BoundaryPayloadSHA256, receipt.SettlementEventID, receipt.SettlementPayloadSHA256,
            receipt.Authority.FrontierAuthoritySHA256, receipt.Authority.FrontierRevision, receipt.Authority.SelectionOrdinal, receipt.Authority.CandidateSpecies,
            receipt.Authority.AccessSequence, receipt.Authority.AccessEntrySHA256, receipt.Authority.AccessEntryCount);
        writer.Str(receipt.ReceiptSHA256);
    }

    internal static RepositoryFundingReceipt ReadFunding(CkptReader reader)
    {
        int step = reader.I32(); ReadAuthority(reader, out CortexPolicyID policy, out CortexPolicyDecisionID decision, out TapeEventID decisionEvent,
            out CortexPolicyQuotaDecisionID funding, out CortexPolicyReadoutFingerprint readout, out CortexPolicyCandidateFingerprint candidate,
            out ulong occurrence, out global::Cogito.Grammar.GrammarRevisionID revision, out PolicyCanonicalStateID state, out RepositoryCandidateDigest digest,
            out string canonical, out RepositoryFrontierRevision frontier, out string world, out string access, out string call);
        long planned = reader.I64(), reserved = reader.I64(), charged = reader.I64(), remaining = reader.I64();
        ReadPredecessor(reader, out TapeEventID predecessor, out LoopClosureDigest predecessorDigest);
        CortexPolicyTrialQuotaDecision fundingDecision = ReadFundingDecision(reader);
        ReadReceiptMetadata(reader, out TapeEventID eventID, out LoopLineageNodeID nodeID, out string eventPayload, out string decisionPayload,
            out TapeEventID readoutEvent, out string readoutPayload, out TapeEventID fundingEvent, out string fundingPayload,
            out TapeEventID boundaryEvent, out string boundaryPayload, out TapeEventID settlementEvent, out string settlementPayload,
            out string frontierAuthority, out RepositoryFrontierRevision frontierRevision, out int selectionOrdinal, out RepositoryCandidateSpecies candidateSpecies,
            out long accessSequence, out string accessEntrySHA256, out long accessEntryCount);
        RepositoryFundingReceipt receipt = new(step, policy, decision, decisionEvent, funding, readout, candidate, occurrence, revision, state, digest,
            canonical, frontier, world, access, call, planned, reserved, charged, remaining, predecessor, predecessorDigest, fundingDecision,
            new RepositoryReceiptAuthority(eventID, nodeID, eventPayload, decisionPayload, readoutEvent, readoutPayload,
                fundingEvent, fundingPayload, boundaryEvent, boundaryPayload, settlementEvent, settlementPayload,
                frontierAuthority, frontierRevision, selectionOrdinal, candidateSpecies)
            {
                AccessSequence = accessSequence,
                AccessEntrySHA256 = accessEntrySHA256,
                AccessEntryCount = accessEntryCount,
            }, reader.Str());
        receipt.Validate(); return receipt;
    }

    internal static void Write(CkptWriter writer, in RepositoryPaidOutcomeReceipt receipt)
    {
        writer.I32(receipt.Step); WriteAuthority(writer, receipt.PolicyID, receipt.DecisionID, receipt.DecisionEventID, receipt.QuotaDecisionID,
            receipt.ReadoutFingerprint, receipt.CandidateFingerprint, receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.CanonicalState,
            receipt.CandidateDigest, receipt.CandidateCanonical, receipt.FrontierRevision, receipt.WorldSHA256, receipt.AccessSHA256, receipt.CallSHA256);
        writer.I64(receipt.PlannedArmSteps); writer.I64(receipt.ActualExecutedArmSteps); writer.I64(receipt.ReclaimedOrUnused);
        writer.Bool(receipt.EvaluatorWorkUnits.HasValue); if (receipt.EvaluatorWorkUnits is long evaluator) writer.I64(evaluator);
        writer.U8((byte)receipt.VerifierOutcome); writer.Bool(receipt.WallMilliseconds.HasValue); if (receipt.WallMilliseconds is long wall) writer.I64(wall);
        writer.I64(receipt.OutcomeEventID.Value); writer.U8((byte)receipt.OutcomePayloadSource); writer.Str(receipt.OutcomePayloadSHA256); WritePredecessor(writer, receipt.PredecessorEventID, receipt.PredecessorDigest);
        WriteReceiptMetadata(writer, receipt.EventID, receipt.NodeID, receipt.EventPayloadSHA256, receipt.DecisionPayloadSHA256,
            receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256, receipt.FundingEventID, receipt.FundingPayloadSHA256,
            receipt.BoundaryEventID, receipt.BoundaryPayloadSHA256, receipt.SettlementEventID, receipt.SettlementPayloadSHA256,
            receipt.Authority.FrontierAuthoritySHA256, receipt.Authority.FrontierRevision, receipt.Authority.SelectionOrdinal, receipt.Authority.CandidateSpecies,
            receipt.Authority.AccessSequence, receipt.Authority.AccessEntrySHA256, receipt.Authority.AccessEntryCount);
        writer.Str(receipt.ReceiptSHA256);
    }

    internal static RepositoryPaidOutcomeReceipt ReadPaidOutcome(CkptReader reader)
    {
        int step = reader.I32(); ReadAuthority(reader, out CortexPolicyID policy, out CortexPolicyDecisionID decision, out TapeEventID decisionEvent,
            out CortexPolicyQuotaDecisionID funding, out CortexPolicyReadoutFingerprint readout, out CortexPolicyCandidateFingerprint candidate,
            out ulong occurrence, out global::Cogito.Grammar.GrammarRevisionID revision, out PolicyCanonicalStateID state, out RepositoryCandidateDigest digest,
            out string canonical, out RepositoryFrontierRevision frontier, out string world, out string access, out string call);
        long planned = reader.I64(), actual = reader.I64(), refund = reader.I64(); long? evaluator = reader.Bool() ? reader.I64() : null;
        CortexPolicyVerifierOutcomes verifier = (CortexPolicyVerifierOutcomes)reader.U8(); long? wall = reader.Bool() ? reader.I64() : null;
        TapeEventID outcomeEvent = new(reader.I64()); RepositoryOutcomePayloadSources outcomePayloadSource = (RepositoryOutcomePayloadSources)reader.U8(); string outcomePayload = reader.Str(); ReadPredecessor(reader, out TapeEventID predecessor, out LoopClosureDigest predecessorDigest);
        ReadReceiptMetadata(reader, out TapeEventID eventID, out LoopLineageNodeID nodeID, out string eventPayload, out string decisionPayload,
            out TapeEventID readoutEvent, out string readoutPayload, out TapeEventID fundingEvent, out string fundingPayload,
            out TapeEventID boundaryEvent, out string boundaryPayload, out TapeEventID settlementEvent, out string settlementPayload,
            out string frontierAuthority, out RepositoryFrontierRevision frontierRevision, out int selectionOrdinal, out RepositoryCandidateSpecies candidateSpecies,
            out long accessSequence, out string accessEntrySHA256, out long accessEntryCount);
        RepositoryPaidOutcomeReceipt receipt = new(step, policy, decision, decisionEvent, funding, readout, candidate, occurrence, revision, state,
            digest, canonical, frontier, world, access, call, planned, actual, refund, evaluator, verifier, wall, outcomeEvent, outcomePayloadSource, outcomePayload,
            predecessor, predecessorDigest,
            new RepositoryReceiptAuthority(eventID, nodeID, eventPayload, decisionPayload, readoutEvent, readoutPayload,
                fundingEvent, fundingPayload, boundaryEvent, boundaryPayload, settlementEvent, settlementPayload,
                frontierAuthority, frontierRevision, selectionOrdinal, candidateSpecies)
            {
                AccessSequence = accessSequence,
                AccessEntrySHA256 = accessEntrySHA256,
                AccessEntryCount = accessEntryCount,
            }, reader.Str());
        receipt.Validate(); return receipt;
    }

    internal static void Write(CkptWriter writer, in RepositoryLoopClosureLinkEvidence receipt)
    {
        writer.Str(receipt.RecordID); writer.U8((byte)receipt.Species); writer.U8((byte)receipt.Path); writer.U8((byte)receipt.State);
        writer.I64(receipt.EventID.Value); writer.Str(receipt.PayloadSHA256); writer.Str(receipt.EvidenceSHA256); writer.Str(receipt.PredecessorEvidenceSHA256);
        writer.Str(receipt.LineageSHA256); writer.Str(receipt.JournalSHA256); writer.U8((byte)receipt.NodeSpecies); writer.U8((byte)receipt.CandidateSpecies);
        writer.U64(receipt.CandidateDigest.Value); writer.Str(receipt.CandidateCanonical); writer.Str(receipt.SourcePath); writer.I32(receipt.SourceLine); writer.I64(receipt.SourceBytes);
        writer.Str(receipt.SourceSHA256); writer.I64(receipt.AccessSequence); writer.U8((byte)receipt.ToolVerb);
        writer.Str(receipt.PolicyID.Value); writer.U64(receipt.DecisionID.Value); writer.I64(receipt.DecisionEventID.Value); writer.U64(receipt.QuotaDecisionID.Value);
        writer.U64(receipt.ReadoutFingerprint.Value); writer.U64(receipt.CandidateFingerprint.Value); writer.U64(receipt.CandidateOccurrenceDigest);
        writer.U64(receipt.ReadoutRevision.Value); WriteState(writer, receipt.CanonicalState); writer.U64(receipt.FrontierRevision.Value);
        writer.Str(receipt.WorldSHA256); writer.Str(receipt.AccessSHA256); writer.Str(receipt.CallSHA256); writer.Str(receipt.FrontierAuthoritySHA256); writer.I32(receipt.SelectionOrdinal); writer.Str(receipt.AccessEntrySHA256); writer.Str(receipt.ForkArmSHA256.Value ?? "");
        writer.Str(receipt.ChildExecutionReceiptSHA256.Value ?? ""); WriteChildOutcome(writer, receipt.ChildOutcome); writer.Str(receipt.NodeID.Value); writer.I64(receipt.OutcomeEventID.Value);
        writer.Str(receipt.OutcomePayloadSHA256); WritePredecessor(writer, receipt.PredecessorEventID, receipt.PredecessorDigest);
        writer.Str(receipt.DecisionPayloadSHA256); writer.I64(receipt.ReadoutEventID.Value); writer.Str(receipt.ReadoutPayloadSHA256);
        writer.I64(receipt.FundingEventID.Value); writer.Str(receipt.FundingPayloadSHA256);
        writer.I64(receipt.BoundaryEventID.Value); writer.Str(receipt.BoundaryPayloadSHA256);
        writer.I64(receipt.SettlementEventID.Value); writer.Str(receipt.SettlementPayloadSHA256); writer.Str(receipt.ReceiptSHA256);
        writer.Str(receipt.RunID); writer.I32(receipt.Step); writer.Str(receipt.AttemptEvidenceSHA256); writer.Str(receipt.AttemptJournalSHA256);
        writer.I64(receipt.AttemptPredecessorEventID); writer.Str(receipt.AttemptPredecessorEvidenceSHA256);
        writer.U8((byte)receipt.DenialReason); writer.Bool(receipt.HasDenialReason); writer.Str(receipt.QuotaID.Value ?? "");
        writer.Str(receipt.ForkReceiptSHA256.Value ?? ""); writer.Str(receipt.DivergenceEvidenceSHA256.Value ?? ""); writer.U64(receipt.GrammarRevision.Value);
        writer.Str(receipt.AttemptSHA256.Value ?? ""); writer.Str(receipt.PredecessorAttemptSHA256.Value ?? ""); writer.Str(receipt.AttemptEvidenceRunID);
        writer.Str(receipt.AttemptEvidenceRelativePath); writer.Str(receipt.AttemptEvidenceAuthoritySHA256.Value ?? ""); writer.Str(receipt.AttemptEvidenceRailSHA256.Value ?? "");
        writer.I64(receipt.LinkEventID.Value); writer.Str(receipt.LinkPacketSHA256); writer.Str(receipt.LinkJournalSHA256);
    }

    internal static RepositoryLoopClosureLinkEvidence ReadDivergence(CkptReader reader)
    {
        string recordID = reader.Str(); LoopClosureLinkSpecies species = (LoopClosureLinkSpecies)reader.U8(); LoopClosureLinkPaths path = (LoopClosureLinkPaths)reader.U8(); LoopClosureLinkStates linkState = (LoopClosureLinkStates)reader.U8();
        TapeEventID eventID = new(reader.I64()); string payload = reader.Str(); string evidence = reader.Str(); string predecessorEvidence = reader.Str();
        string lineage = reader.Str(); string journal = reader.Str(); LoopLineageNodeSpecies nodeSpecies = (LoopLineageNodeSpecies)reader.U8(); RepositoryCandidateSpecies candidateSpecies = (RepositoryCandidateSpecies)reader.U8();
        RepositoryCandidateDigest digest = new(reader.U64()); string canonical = reader.Str(); string sourcePath = reader.Str(); int sourceLine = reader.I32(); long sourceBytes = reader.I64(); string sourceSHA = reader.Str(); long accessSequence = reader.I64(); Tool.ToolVerbs toolVerb = (Tool.ToolVerbs)reader.U8();
        CortexPolicyID policy = new(reader.Str()); CortexPolicyDecisionID decision = new(reader.U64()); TapeEventID decisionEvent = new(reader.I64()); CortexPolicyQuotaDecisionID funding = new(reader.U64()); CortexPolicyReadoutFingerprint readout = new(reader.U64()); CortexPolicyCandidateFingerprint candidate = new(reader.U64()); ulong occurrence = reader.U64(); global::Cogito.Grammar.GrammarRevisionID revision = new(reader.U64()); PolicyCanonicalStateID state = ReadState(reader); RepositoryFrontierRevision frontier = new(reader.U64()); string world = reader.Str(); string access = reader.Str(); string call = reader.Str(); string frontierAuthority = reader.Str(); int selectionOrdinal = reader.I32(); string accessEntrySHA256 = reader.Str(); LoopClosureDigest fork = new(reader.Str()); LoopClosureDigest child = new(reader.Str()); LoopClosureChildOutcomeReference childOutcome = ReadChildOutcome(reader); LoopLineageNodeID nodeID = new(reader.Str()); TapeEventID outcomeEvent = new(reader.I64()); string outcomePayload = reader.Str(); ReadPredecessor(reader, out TapeEventID predecessor, out LoopClosureDigest predecessorDigest);
        string decisionPayload = reader.Str(); TapeEventID readoutEvent = new(reader.I64()); string readoutPayload = reader.Str();
        TapeEventID fundingEvent = new(reader.I64()); string fundingPayload = reader.Str(); TapeEventID boundaryEvent = new(reader.I64()); string boundaryPayload = reader.Str();
        TapeEventID settlementEvent = new(reader.I64()); string settlementPayload = reader.Str();
        string receiptSHA = reader.Str(); string runID = reader.Str(); int step = reader.I32(); string attemptEvidence = reader.Str(); string attemptJournal = reader.Str(); long attemptPredecessorEvent = reader.I64(); string attemptPredecessorEvidence = reader.Str(); LoopClosureGateDenialReasons denial = (LoopClosureGateDenialReasons)reader.U8(); bool hasDenial = reader.Bool(); LoopClosureQuotaID fundingID = new(reader.Str()); LoopClosureDigest forkReceipt = new(reader.Str()); LoopClosureDigest divergenceEvidence = new(reader.Str()); global::Cogito.Grammar.GrammarRevisionID grammarRevision = new(reader.U64()); LoopClosureDigest attempt = new(reader.Str()); LoopClosureDigest predecessorAttempt = new(reader.Str()); string evidenceRunID = reader.Str(); string evidenceRelativePath = reader.Str(); LoopClosureDigest evidenceAuthority = new(reader.Str()); LoopClosureDigest evidenceRail = new(reader.Str()); TapeEventID linkEventID = new(reader.I64()); string linkPacket = reader.Str(); string linkJournal = reader.Str();
        RepositoryLoopClosureLinkEvidence receipt = new(recordID, species, path, linkState, eventID, payload, evidence, predecessorEvidence, lineage, journal, nodeSpecies, candidateSpecies, digest, canonical, sourcePath, sourceLine, sourceBytes, sourceSHA, accessSequence, toolVerb)
        {
            PolicyID = policy, DecisionID = decision, DecisionEventID = decisionEvent, QuotaDecisionID = funding, ReadoutFingerprint = readout,
            CandidateFingerprint = candidate, CandidateOccurrenceDigest = occurrence, ReadoutRevision = revision, CanonicalState = state, FrontierRevision = frontier,
            WorldSHA256 = world, AccessSHA256 = access, CallSHA256 = call, FrontierAuthoritySHA256 = frontierAuthority, SelectionOrdinal = selectionOrdinal, AccessEntrySHA256 = accessEntrySHA256, ForkArmSHA256 = fork, ChildExecutionReceiptSHA256 = child, ChildOutcome = childOutcome, NodeID = nodeID,
            OutcomeEventID = outcomeEvent, OutcomePayloadSHA256 = outcomePayload, PredecessorEventID = predecessor, PredecessorDigest = predecessorDigest,
            DecisionPayloadSHA256 = decisionPayload, ReadoutEventID = readoutEvent, ReadoutPayloadSHA256 = readoutPayload,
            FundingEventID = fundingEvent, FundingPayloadSHA256 = fundingPayload, BoundaryEventID = boundaryEvent,
            BoundaryPayloadSHA256 = boundaryPayload, SettlementEventID = settlementEvent, SettlementPayloadSHA256 = settlementPayload,
            ReceiptSHA256 = receiptSHA, RunID = runID, Step = step, AttemptEvidenceSHA256 = attemptEvidence, AttemptJournalSHA256 = attemptJournal,
            AttemptPredecessorEventID = attemptPredecessorEvent, AttemptPredecessorEvidenceSHA256 = attemptPredecessorEvidence,
            DenialReason = denial, HasDenialReason = hasDenial, QuotaID = fundingID, ForkReceiptSHA256 = forkReceipt,
            DivergenceEvidenceSHA256 = divergenceEvidence, GrammarRevision = grammarRevision, AttemptSHA256 = attempt,
            PredecessorAttemptSHA256 = predecessorAttempt, AttemptEvidenceRunID = evidenceRunID,
            AttemptEvidenceRelativePath = evidenceRelativePath, AttemptEvidenceAuthoritySHA256 = evidenceAuthority,
            AttemptEvidenceRailSHA256 = evidenceRail, LinkEventID = linkEventID, LinkPacketSHA256 = linkPacket, LinkJournalSHA256 = linkJournal,
        };
        return receipt;
    }

    internal static void Write(CkptWriter writer, in RepositoryAdjudicatedOutcomeReceipt receipt)
    {
        writer.I32(receipt.Step); WriteAuthority(writer, receipt.PolicyID, receipt.DecisionID, receipt.DecisionEventID, receipt.QuotaDecisionID,
            receipt.ReadoutFingerprint, receipt.CandidateFingerprint, receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.CanonicalState,
            receipt.CandidateDigest, receipt.CandidateCanonical, receipt.FrontierRevision, receipt.WorldSHA256, receipt.AccessSHA256, receipt.CallSHA256);
        writer.I64(receipt.PlannedArmSteps); writer.I64(receipt.ActualExecutedArmSteps); writer.I64(receipt.ReclaimedOrUnused);
        writer.Bool(receipt.EvaluatorWorkUnits.HasValue); if (receipt.EvaluatorWorkUnits is long evaluator) writer.I64(evaluator);
        writer.U8((byte)receipt.VerifierOutcome); writer.Bool(receipt.WallMilliseconds.HasValue); if (receipt.WallMilliseconds is long wall) writer.I64(wall); writer.Bool(receipt.Executed);
        writer.Str(receipt.ForkArmSHA256.Value ?? ""); writer.Str(receipt.ChildExecutionReceiptSHA256.Value ?? ""); writer.U64(receipt.ExecutedDivergenceDecisionID.Value);
        writer.Str(receipt.ExecutedDivergenceOutcomeID.Value ?? ""); writer.I64(receipt.OutcomeEventID.Value); writer.Str(receipt.OutcomePayloadSHA256);
        WritePredecessor(writer, receipt.PredecessorEventID, receipt.PredecessorDigest); WriteChildOutcome(writer, receipt.ChildOutcome);
        WriteReceiptMetadata(writer, receipt.EventID, receipt.NodeID, receipt.EventPayloadSHA256, receipt.DecisionPayloadSHA256,
            receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256, receipt.FundingEventID, receipt.FundingPayloadSHA256,
            receipt.BoundaryEventID, receipt.BoundaryPayloadSHA256, receipt.SettlementEventID, receipt.SettlementPayloadSHA256,
            receipt.Authority.FrontierAuthoritySHA256, receipt.Authority.FrontierRevision, receipt.Authority.SelectionOrdinal, receipt.Authority.CandidateSpecies,
            receipt.Authority.AccessSequence, receipt.Authority.AccessEntrySHA256, receipt.Authority.AccessEntryCount);
        writer.Str(receipt.ReceiptSHA256);
    }

    internal static RepositoryAdjudicatedOutcomeReceipt ReadOutcome(CkptReader reader)
    {
        int step = reader.I32(); ReadAuthority(reader, out CortexPolicyID policy, out CortexPolicyDecisionID decision, out TapeEventID decisionEvent,
            out CortexPolicyQuotaDecisionID funding, out CortexPolicyReadoutFingerprint readout, out CortexPolicyCandidateFingerprint candidate,
            out ulong occurrence, out global::Cogito.Grammar.GrammarRevisionID revision, out PolicyCanonicalStateID state, out RepositoryCandidateDigest digest,
            out string canonical, out RepositoryFrontierRevision frontier, out string world, out string access, out string call);
        long planned = reader.I64(), actual = reader.I64(), refund = reader.I64(); long? evaluator = reader.Bool() ? reader.I64() : null;
        CortexPolicyVerifierOutcomes verifier = (CortexPolicyVerifierOutcomes)reader.U8(); long? wall = reader.Bool() ? reader.I64() : null; bool executed = reader.Bool();
        LoopClosureDigest fork = new(reader.Str()), child = new(reader.Str()); CortexPolicyDecisionID executedDecision = new(reader.U64()); LoopClosureDigest outcome = new(reader.Str());
        TapeEventID outcomeEvent = new(reader.I64()); string outcomePayload = reader.Str(); ReadPredecessor(reader, out TapeEventID predecessor, out LoopClosureDigest predecessorDigest);
        LoopClosureChildOutcomeReference childOutcome = ReadChildOutcome(reader);
        ReadReceiptMetadata(reader, out TapeEventID eventID, out LoopLineageNodeID nodeID, out string eventPayload, out string decisionPayload,
            out TapeEventID readoutEvent, out string readoutPayload, out TapeEventID fundingEvent, out string fundingPayload,
            out TapeEventID boundaryEvent, out string boundaryPayload, out TapeEventID settlementEvent, out string settlementPayload,
            out string frontierAuthority, out RepositoryFrontierRevision frontierRevision, out int selectionOrdinal, out RepositoryCandidateSpecies candidateSpecies,
            out long accessSequence, out string accessEntrySHA256, out long accessEntryCount);
        RepositoryAdjudicatedOutcomeReceipt receipt = new(step, policy, decision, decisionEvent, funding, readout, candidate, occurrence, revision, state, digest,
            canonical, frontier, world, access, call, planned, actual, refund, evaluator, verifier, wall, executed, fork, child, executedDecision,
            outcome, outcomeEvent, outcomePayload, predecessor, predecessorDigest, childOutcome,
            new RepositoryReceiptAuthority(eventID, nodeID, eventPayload, decisionPayload, readoutEvent, readoutPayload,
                fundingEvent, fundingPayload, boundaryEvent, boundaryPayload, settlementEvent, settlementPayload,
                frontierAuthority, frontierRevision, selectionOrdinal, candidateSpecies)
            {
                AccessSequence = accessSequence,
                AccessEntrySHA256 = accessEntrySHA256,
                AccessEntryCount = accessEntryCount,
            }, reader.Str());
        receipt.Validate(); return receipt;
    }

    internal static void Write(CkptWriter writer, in RepositoryNewEvidenceReceipt receipt)
    {
        writer.I32(receipt.Step); WriteAuthority(writer, receipt.PolicyID, receipt.DecisionID, receipt.DecisionEventID, receipt.QuotaDecisionID,
            receipt.ReadoutFingerprint, receipt.CandidateFingerprint, receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.CanonicalState,
            receipt.CandidateDigest, receipt.CandidateCanonical, receipt.FrontierRevision, receipt.WorldSHA256, receipt.AccessSHA256, receipt.CallSHA256);
        writer.Str(receipt.SourceLocus.Path.Value); writer.I32(receipt.SourceLocus.Line); writer.I64(receipt.ObservationEventID.Value); writer.I64(receipt.OutcomeEventID.Value);
        writer.Str(receipt.EvidenceSHA256); WritePredecessor(writer, receipt.PredecessorEventID, receipt.PredecessorDigest);
        WriteReceiptMetadata(writer, receipt.EventID, receipt.NodeID, receipt.EventPayloadSHA256, receipt.DecisionPayloadSHA256,
            receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256, receipt.FundingEventID, receipt.FundingPayloadSHA256,
            receipt.BoundaryEventID, receipt.BoundaryPayloadSHA256, receipt.SettlementEventID, receipt.SettlementPayloadSHA256,
            receipt.Authority.FrontierAuthoritySHA256, receipt.Authority.FrontierRevision, receipt.Authority.SelectionOrdinal, receipt.Authority.CandidateSpecies,
            receipt.Authority.AccessSequence, receipt.Authority.AccessEntrySHA256, receipt.Authority.AccessEntryCount);
        writer.Str(receipt.ReceiptSHA256);
    }

    internal static RepositoryNewEvidenceReceipt ReadEvidence(CkptReader reader)
    {
        int step = reader.I32(); ReadAuthority(reader, out CortexPolicyID policy, out CortexPolicyDecisionID decision, out TapeEventID decisionEvent,
            out CortexPolicyQuotaDecisionID funding, out CortexPolicyReadoutFingerprint readout, out CortexPolicyCandidateFingerprint candidate,
            out ulong occurrence, out global::Cogito.Grammar.GrammarRevisionID revision, out PolicyCanonicalStateID state, out RepositoryCandidateDigest digest,
            out string canonical, out RepositoryFrontierRevision frontier, out string world, out string access, out string call);
        RepositoryLocus locus = new(reader.Str(), reader.I32()); TapeEventID observation = new(reader.I64()), outcome = new(reader.I64()); string evidence = reader.Str();
        ReadPredecessor(reader, out TapeEventID predecessor, out LoopClosureDigest predecessorDigest);
        ReadReceiptMetadata(reader, out TapeEventID eventID, out LoopLineageNodeID nodeID, out string eventPayload, out string decisionPayload,
            out TapeEventID readoutEvent, out string readoutPayload, out TapeEventID fundingEvent, out string fundingPayload,
            out TapeEventID boundaryEvent, out string boundaryPayload, out TapeEventID settlementEvent, out string settlementPayload,
            out string frontierAuthority, out RepositoryFrontierRevision frontierRevision, out int selectionOrdinal, out RepositoryCandidateSpecies candidateSpecies,
            out long accessSequence, out string accessEntrySHA256, out long accessEntryCount);
        RepositoryNewEvidenceReceipt receipt = new(step, policy, decision, decisionEvent, funding, readout, candidate, occurrence, revision, state, digest,
            canonical, frontier, world, access, call, locus, observation, outcome, evidence, predecessor, predecessorDigest,
            new RepositoryReceiptAuthority(eventID, nodeID, eventPayload, decisionPayload, readoutEvent, readoutPayload,
                fundingEvent, fundingPayload, boundaryEvent, boundaryPayload, settlementEvent, settlementPayload,
                frontierAuthority, frontierRevision, selectionOrdinal, candidateSpecies)
            {
                AccessSequence = accessSequence,
                AccessEntrySHA256 = accessEntrySHA256,
                AccessEntryCount = accessEntryCount,
            }, reader.Str());
        receipt.Validate(); return receipt;
    }

    internal static void WriteBundle(CkptWriter writer,
        IReadOnlyList<RepositoryFundingReceipt> funding,
        IReadOnlyList<RepositoryLoopClosureLinkEvidence> divergence,
        IReadOnlyList<RepositoryAdjudicatedOutcomeReceipt> outcomes,
        IReadOnlyList<RepositoryNewEvidenceReceipt> evidence)
    {
        ArgumentNullException.ThrowIfNull(funding); ArgumentNullException.ThrowIfNull(divergence);
        ArgumentNullException.ThrowIfNull(outcomes); ArgumentNullException.ThrowIfNull(evidence);
        if (funding.Count > MaxReceiptsPerSection || divergence.Count > MaxReceiptsPerSection
            || outcomes.Count > MaxReceiptsPerSection || evidence.Count > MaxReceiptsPerSection
            || (long)funding.Count + divergence.Count + outcomes.Count + evidence.Count > MaxReceiptsTotal)
            throw new InvalidDataException("repository lineage receipt bundle exceeds checkpoint bounds");
        ValidateDivergenceChain(divergence);
        writer.Section(Section);
        writer.U32(Magic); writer.U16(SchemaVersion);
        writer.I32(funding.Count); foreach (RepositoryFundingReceipt receipt in funding) { receipt.Validate(); Write(writer, in receipt); }
        writer.I32(divergence.Count); foreach (RepositoryLoopClosureLinkEvidence receipt in divergence) Write(writer, in receipt);
        writer.I32(outcomes.Count); foreach (RepositoryAdjudicatedOutcomeReceipt receipt in outcomes) { receipt.Validate(); Write(writer, in receipt); }
        writer.I32(evidence.Count); foreach (RepositoryNewEvidenceReceipt receipt in evidence) { receipt.Validate(); Write(writer, in receipt); }
    }

    internal static (RepositoryFundingReceipt[] Funding, RepositoryLoopClosureLinkEvidence[] Divergence,
        RepositoryAdjudicatedOutcomeReceipt[] Outcomes, RepositoryNewEvidenceReceipt[] Evidence) ReadBundle(CkptReader reader)
    {
        reader.Expect(Section);
        reader.Expect(Magic);
        ushort version = reader.U16();
        if (version != SchemaVersion)
            throw new InvalidDataException($"repository lineage checkpoint schema {version} is unsupported");
        static int ReadCount(CkptReader reader, string name)
        {
            int count = reader.I32();
            if (count < 0 || count > MaxReceiptsPerSection) throw new InvalidDataException($"repository {name} receipt count is malformed");
            return count;
        }
        int fundingCount = ReadCount(reader, "funding");
        // Frozen journal row kind; identifier-side name is Divergence.
        int divergenceCount = ReadCount(reader, "dissent");
        int outcomeCount = ReadCount(reader, "outcome");
        int evidenceCount = ReadCount(reader, "new-evidence");
        if ((long)fundingCount + divergenceCount + outcomeCount + evidenceCount > MaxReceiptsTotal)
            throw new InvalidDataException("repository lineage receipt bundle exceeds checkpoint bounds");
        RepositoryFundingReceipt[] funding = new RepositoryFundingReceipt[fundingCount];
        for (int i = 0; i < funding.Length; i++) funding[i] = ReadFunding(reader);
        RepositoryLoopClosureLinkEvidence[] divergence = new RepositoryLoopClosureLinkEvidence[divergenceCount];
        for (int i = 0; i < divergence.Length; i++) divergence[i] = ReadDivergence(reader);
        RepositoryAdjudicatedOutcomeReceipt[] outcomes = new RepositoryAdjudicatedOutcomeReceipt[outcomeCount];
        for (int i = 0; i < outcomes.Length; i++) outcomes[i] = ReadOutcome(reader);
        RepositoryNewEvidenceReceipt[] evidence = new RepositoryNewEvidenceReceipt[evidenceCount];
        for (int i = 0; i < evidence.Length; i++) evidence[i] = ReadEvidence(reader);
        ValidateDivergenceChain(divergence);
        return (funding, divergence, outcomes, evidence);
    }

    internal static void ValidateDivergenceChain(IReadOnlyList<RepositoryLoopClosureLinkEvidence> divergence)
    {
        ArgumentNullException.ThrowIfNull(divergence);
        if (divergence.Count > LoopClosureLinkContract.OrderedSpecies.Count)
            throw new InvalidDataException("repository divergence receipt chain exceeds the canonical five-link species");
        for (int index = 0; index < divergence.Count; index++)
        {
            RepositoryLoopClosureLinkEvidence receipt = divergence[index];
            LoopClosureLinkSpecies expected = LoopClosureLinkContract.OrderedSpecies[index];
            string? predecessorEvidence = index == 0 ? null : divergence[index - 1].EvidenceSHA256;
            receipt.Validate(expected, predecessorEvidence);
            if (index == 0)
            {
                if (receipt.PredecessorEventID.Value != 0 || receipt.PredecessorDigest.IsValid)
                    throw new InvalidDataException("repository divergence chain first link carries a predecessor");
            }
            else
            {
                RepositoryLoopClosureLinkEvidence prior = divergence[index - 1];
                if (receipt.PredecessorEventID.Value <= 0 || !receipt.PredecessorDigest.IsValid)
                    throw new InvalidDataException("repository divergence chain predecessor event authority is malformed");
                if (!string.Equals(receipt.PredecessorDigest.Value, prior.EvidenceSHA256, StringComparison.Ordinal))
                    throw new InvalidDataException("repository divergence chain predecessor digest does not name the preceding typed link");
            }
        }
    }
}

/// Source-root predicate shared by the generic lineage turnstile and the native repository
/// turnstile. The generic corpus rule remains the default; this predicate only recognizes
/// the repository packet pair emitted by TapePacketCreator.
internal static class RepositoryLineageWorldRoot
{
    internal static bool IsRepositoryAdmissionPlan(Tape tape, TapeEventID eventID)
    {
        if (eventID.Value <= 0 || !tape.Resolve(eventID, out byte[] sourceBytes)
            || tape.ProvenanceOf(eventID) != Provenances.Real
            || !tape.HasRole(eventID, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
            || !string.Equals(tape.SourceOf(eventID), "repository:world", StringComparison.Ordinal)) return false;
        TapeEventID receiptID = new(eventID.Value - 1);
        return tape.Resolve(receiptID, out byte[] payload)
            // Frozen tape source token repository:encounter; identifier-side name is AdmissionPlan.
            && string.Equals(tape.SourceOf(receiptID), "repository:encounter", StringComparison.Ordinal)
            && tape.ProvenanceOf(receiptID) == Provenances.Execution
            && tape.HasRole(receiptID, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
            && TapePacketCreator.TryReadRepositoryWorldEncounter(payload, out RepositoryAdmissionReceipt receipt)
            && receipt.ObservationEventID == eventID
            && receipt.EvidenceSHA256 == Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
    }
}
