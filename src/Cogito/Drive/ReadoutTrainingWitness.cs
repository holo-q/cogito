namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;

/// The immutable causal corroboration that makes one learned readout traceable to the
/// exact teacher packet and consuming grammar fold that trained it.  Funding and
/// fork execution are deliberately a later settlement: they do not exist when
/// the readout packet is published and therefore cannot be smuggled into this
/// phase's identity.
public readonly struct ReadoutTrainingCorroboration
{
    public ReadoutTrainingCorroboration(
        CortexPolicyID policy,
        TapeEventID teacherPacketEventID,
        TapeEventID teacherCompositionEventID,
        IReadOnlyList<TapeEventID> teacherEvidenceEventIDs,
        LoopClosureDigest teacherEvidenceSHA256,
        LoopClosureCompositionEpisodeID sourceEpisodeID,
        LoopClosureDigest sourceEpisodeSHA256,
        GrammarRevisionID consumingFoldPreviousRevision,
        GrammarRevisionID consumingFoldRevision,
        IReadOnlyList<TapeEventID> consumingFoldConsumedEventIDs,
        LoopClosureDigest consumingFoldConsumedEventSHA256,
        LoopClosureDigest consumingFoldReceiptSHA256,
        in PolicyCanonicalStateID canonicalState,
        ulong contextDigest,
        int contextActionCount,
        int contextDeliberationDepth,
        ulong selectedCandidateFingerprint,
        ulong selectedCandidateOccurrenceDigest,
        GrammarRevisionID selectedCandidateRevision,
        CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID,
        LoopClosureDigest corroborationSHA256)
    {
        Policy = policy;
        TeacherPacketEventID = teacherPacketEventID;
        TeacherCompositionEventID = teacherCompositionEventID;
        TeacherEvidenceEventIDs = teacherEvidenceEventIDs?.ToArray() ?? throw new ArgumentNullException(nameof(teacherEvidenceEventIDs));
        TeacherEvidenceSHA256 = teacherEvidenceSHA256;
        SourceEpisodeID = sourceEpisodeID;
        SourceEpisodeSHA256 = sourceEpisodeSHA256;
        ConsumingFoldPreviousRevision = consumingFoldPreviousRevision;
        ConsumingFoldRevision = consumingFoldRevision;
        ConsumingFoldConsumedEventIDs = consumingFoldConsumedEventIDs?.ToArray() ?? throw new ArgumentNullException(nameof(consumingFoldConsumedEventIDs));
        ConsumingFoldConsumedEventSHA256 = consumingFoldConsumedEventSHA256;
        ConsumingFoldReceiptSHA256 = consumingFoldReceiptSHA256;
        CanonicalState = canonicalState;
        ContextDigest = contextDigest;
        ContextActionCount = contextActionCount;
        ContextDeliberationDepth = contextDeliberationDepth;
        SelectedCandidateFingerprint = selectedCandidateFingerprint;
        SelectedCandidateOccurrenceDigest = selectedCandidateOccurrenceDigest;
        SelectedCandidateRevision = selectedCandidateRevision;
        DecisionID = decisionID;
        DecisionEventID = decisionEventID;
        ReadoutTrainingCorroborationSHA256 = corroborationSHA256;
        Validate();
    }

    public CortexPolicyID Policy { get; }
    public TapeEventID TeacherPacketEventID { get; }
    public TapeEventID TeacherCompositionEventID { get; }
    public TapeEventID[] TeacherEvidenceEventIDs { get; }
    public LoopClosureDigest TeacherEvidenceSHA256 { get; }
    public LoopClosureCompositionEpisodeID SourceEpisodeID { get; }
    public LoopClosureDigest SourceEpisodeSHA256 { get; }
    public GrammarRevisionID ConsumingFoldPreviousRevision { get; }
    public GrammarRevisionID ConsumingFoldRevision { get; }
    public TapeEventID[] ConsumingFoldConsumedEventIDs { get; }
    public LoopClosureDigest ConsumingFoldConsumedEventSHA256 { get; }
    public LoopClosureDigest ConsumingFoldReceiptSHA256 { get; }
    public PolicyCanonicalStateID CanonicalState { get; }
    public ulong ContextDigest { get; }
    public int ContextActionCount { get; }
    public int ContextDeliberationDepth { get; }
    public ulong SelectedCandidateFingerprint { get; }
    public ulong SelectedCandidateOccurrenceDigest { get; }
    public GrammarRevisionID SelectedCandidateRevision { get; }
    public CortexPolicyDecisionID DecisionID { get; }
    public TapeEventID DecisionEventID { get; }
    public LoopClosureDigest ReadoutTrainingCorroborationSHA256 { get; }

    internal static ReadoutTrainingCorroboration Create(
        CortexPolicyID policy,
        TapeEventID teacherPacketEventID,
        TapeEventID teacherCompositionEventID,
        IReadOnlyList<TapeEventID> teacherEvidenceEventIDs,
        LoopClosureDigest teacherEvidenceSHA256,
        LoopClosureCompositionEpisodeID sourceEpisodeID,
        LoopClosureDigest sourceEpisodeSHA256,
        GrammarRevisionID consumingFoldPreviousRevision,
        GrammarRevisionID consumingFoldRevision,
        IReadOnlyList<TapeEventID> consumingFoldConsumedEventIDs,
        LoopClosureDigest consumingFoldConsumedEventSHA256,
        LoopClosureDigest consumingFoldReceiptSHA256,
        in PolicyCanonicalStateID canonicalState,
        in GrammarPolicyContextKey context,
        ulong selectedCandidateFingerprint,
        ulong selectedCandidateOccurrenceDigest,
        GrammarRevisionID selectedCandidateRevision,
        CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID)
    {
        string canonical = Canonical(
            policy, teacherPacketEventID, teacherCompositionEventID, teacherEvidenceEventIDs,
            teacherEvidenceSHA256, sourceEpisodeID, sourceEpisodeSHA256,
            consumingFoldPreviousRevision, consumingFoldRevision, consumingFoldConsumedEventIDs,
            consumingFoldConsumedEventSHA256, consumingFoldReceiptSHA256, in canonicalState,
            context.ContextDigest, context.ActionCount, context.DeliberationDepth,
            selectedCandidateFingerprint, selectedCandidateOccurrenceDigest, selectedCandidateRevision,
            decisionID, decisionEventID);
        return new(
            policy, teacherPacketEventID, teacherCompositionEventID, teacherEvidenceEventIDs,
            teacherEvidenceSHA256, sourceEpisodeID, sourceEpisodeSHA256,
            consumingFoldPreviousRevision, consumingFoldRevision, consumingFoldConsumedEventIDs,
            consumingFoldConsumedEventSHA256, consumingFoldReceiptSHA256, in canonicalState,
            context.ContextDigest, context.ActionCount, context.DeliberationDepth,
            selectedCandidateFingerprint, selectedCandidateOccurrenceDigest, selectedCandidateRevision,
            decisionID, decisionEventID,
            new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))));
    }

    public void Validate()
    {
        if (Policy.Value.Length == 0 || TeacherPacketEventID.Value < 0 || TeacherCompositionEventID.Value < 0
            || TeacherEvidenceEventIDs.Length == 0 || TeacherEvidenceEventIDs.Any(static id => id.Value < 0))
            throw new InvalidDataException(
                $"readout training corroboration teacher identity is malformed: policy={Policy} packet={TeacherPacketEventID} derivation={TeacherCompositionEventID} evidence-count={TeacherEvidenceEventIDs.Length}");
        if (SourceEpisodeID is { IsValid: false } || !SourceEpisodeSHA256.IsValid)
            throw new InvalidDataException(
                $"readout training corroboration source identity is malformed: episode={SourceEpisodeID} digest-valid={(SourceEpisodeSHA256.IsValid ? 1 : 0)}");
        if (ConsumingFoldPreviousRevision == GrammarRevisionID.Zero
            || ConsumingFoldRevision.CompareTo(ConsumingFoldPreviousRevision) <= 0
            || ConsumingFoldConsumedEventIDs.Length == 0
            || !ConsumingFoldConsumedEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(ConsumingFoldConsumedEventIDs))
            || !ConsumingFoldConsumedEventSHA256.IsValid || !ConsumingFoldReceiptSHA256.IsValid)
            throw new InvalidDataException(
                $"readout training corroboration fold identity is malformed: previous={ConsumingFoldPreviousRevision} revision={ConsumingFoldRevision} consumed={ConsumingFoldConsumedEventIDs.Length} event-digest-valid={(ConsumingFoldConsumedEventSHA256.IsValid ? 1 : 0)} receipt-digest-valid={(ConsumingFoldReceiptSHA256.IsValid ? 1 : 0)}");
        if (CanonicalState.Version == 0 || !CanonicalState.Policy.Equals(Policy))
            throw new InvalidDataException(
                $"readout training corroboration canonical state is malformed: policy={Policy} state={CanonicalState}");
        if (ContextDigest == 0 || ContextActionCount <= 1 || ContextDeliberationDepth < 0)
            throw new InvalidDataException(
                $"readout training corroboration context schema identity is malformed: digest={ContextDigest:X16} action-count={ContextActionCount} depth={ContextDeliberationDepth}");
        if (SelectedCandidateFingerprint == 0 || SelectedCandidateOccurrenceDigest == 0
            || SelectedCandidateRevision == GrammarRevisionID.Zero)
            throw new InvalidDataException(
                $"readout training corroboration candidate identity is malformed: fingerprint={SelectedCandidateFingerprint:X16} support={SelectedCandidateOccurrenceDigest:X16} revision={SelectedCandidateRevision}");
        if (DecisionID.Value == 0 || DecisionEventID.Value < 0 || !ReadoutTrainingCorroborationSHA256.IsValid)
            throw new InvalidDataException(
                $"readout training corroboration decision identity is malformed: decision={DecisionID} event={DecisionEventID} digest-valid={(ReadoutTrainingCorroborationSHA256.IsValid ? 1 : 0)}");
        TapeEventID[] teacherEvents = LoopClosureCompositionEpisode.NormalizeEventIDs([TeacherCompositionEventID, .. TeacherEvidenceEventIDs]);
        if (!TeacherEvidenceEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(TeacherEvidenceEventIDs)))
            throw new InvalidDataException("readout training corroboration teacher evidence is not canonical");
        if (!ConsumingFoldConsumedEventIDs.ContainsAll(teacherEvents))
            throw new InvalidDataException("readout training corroboration fold does not consume its teacher event set");
        if (!ConsumingFoldConsumedEventIDs.Contains(TeacherPacketEventID))
            throw new InvalidDataException("readout training corroboration fold does not consume its teacher packet");
        if (!string.Equals(ConsumingFoldConsumedEventSHA256.Value,
                LoopClosureCompositionEpisode.ComputeEventDigest(ConsumingFoldConsumedEventIDs), StringComparison.Ordinal))
            throw new InvalidDataException("readout training corroboration fold event digest does not match");
        PolicyCanonicalStateID expectedCanonicalState = CanonicalState;
        GrammarPolicyContextKey expectedContext = new(in expectedCanonicalState, ContextActionCount, ContextDeliberationDepth);
        if (expectedContext.ContextDigest != ContextDigest)
            throw new InvalidDataException("readout training corroboration context identity does not match its canonical state");
        PolicyCanonicalStateID canonicalState = CanonicalState;
        string expected = Canonical(
            Policy, TeacherPacketEventID, TeacherCompositionEventID, TeacherEvidenceEventIDs,
            TeacherEvidenceSHA256, SourceEpisodeID, SourceEpisodeSHA256,
            ConsumingFoldPreviousRevision, ConsumingFoldRevision, ConsumingFoldConsumedEventIDs,
            ConsumingFoldConsumedEventSHA256, ConsumingFoldReceiptSHA256, in canonicalState,
            ContextDigest, ContextActionCount, ContextDeliberationDepth,
            SelectedCandidateFingerprint, SelectedCandidateOccurrenceDigest, SelectedCandidateRevision,
            DecisionID, DecisionEventID);
        if (!string.Equals(ReadoutTrainingCorroborationSHA256.Value,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), StringComparison.Ordinal))
            throw new InvalidDataException("readout training corroboration digest does not match its typed payload");
    }

    internal string Canonical()
    {
        PolicyCanonicalStateID canonicalState = CanonicalState;
        return Canonical(Policy, TeacherPacketEventID, TeacherCompositionEventID, TeacherEvidenceEventIDs,
            TeacherEvidenceSHA256, SourceEpisodeID, SourceEpisodeSHA256, ConsumingFoldPreviousRevision,
            ConsumingFoldRevision, ConsumingFoldConsumedEventIDs, ConsumingFoldConsumedEventSHA256,
            ConsumingFoldReceiptSHA256, in canonicalState, ContextDigest, ContextActionCount,
            ContextDeliberationDepth, SelectedCandidateFingerprint, SelectedCandidateOccurrenceDigest,
            SelectedCandidateRevision, DecisionID, DecisionEventID);
    }

    private static string Canonical(
        CortexPolicyID policy,
        TapeEventID teacherPacketEventID,
        TapeEventID teacherCompositionEventID,
        IReadOnlyList<TapeEventID> teacherEvidenceEventIDs,
        LoopClosureDigest teacherEvidenceSHA256,
        LoopClosureCompositionEpisodeID sourceEpisodeID,
        LoopClosureDigest sourceEpisodeSHA256,
        GrammarRevisionID consumingFoldPreviousRevision,
        GrammarRevisionID consumingFoldRevision,
        IReadOnlyList<TapeEventID> consumingFoldConsumedEventIDs,
        LoopClosureDigest consumingFoldConsumedEventSHA256,
        LoopClosureDigest consumingFoldReceiptSHA256,
        in PolicyCanonicalStateID canonicalState,
        ulong contextDigest,
        int contextActionCount,
        int contextDeliberationDepth,
        ulong selectedCandidateFingerprint,
        ulong selectedCandidateOccurrenceDigest,
        GrammarRevisionID selectedCandidateRevision,
        CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID)
    {
        StringBuilder text = new("readout-training-v1|");
        text.Append(policy.Value).Append('|').Append(teacherPacketEventID.Value).Append('|')
            .Append(teacherCompositionEventID.Value).Append('|').Append(string.Join(',', teacherEvidenceEventIDs.Select(static id => id.Value))).Append('|')
            .Append(teacherEvidenceSHA256.Value).Append('|').Append(sourceEpisodeID.Value).Append('|').Append(sourceEpisodeSHA256.Value).Append('|')
            .Append(consumingFoldPreviousRevision.Value).Append('|').Append(consumingFoldRevision.Value).Append('|')
            .Append(string.Join(',', consumingFoldConsumedEventIDs.Select(static id => id.Value))).Append('|')
            .Append(consumingFoldConsumedEventSHA256.Value).Append('|').Append(consumingFoldReceiptSHA256.Value).Append('|')
            .Append(canonicalState).Append('|').Append(contextDigest).Append('|').Append(contextActionCount).Append('|').Append(contextDeliberationDepth).Append('|')
            .Append(selectedCandidateFingerprint.ToString("X16")).Append('|').Append(selectedCandidateOccurrenceDigest.ToString("X16")).Append('|')
            .Append(selectedCandidateRevision.Value).Append('|').Append(decisionID.Value).Append('|').Append(decisionEventID.Value);
        return text.ToString();
    }
}

/// Completion-side custody joins a previously published training corroboration to the
/// paid fork and the one required forced executed-divergence child outcome.
public readonly struct PaidDivergenceExecutionCorroboration
{
    private readonly ulong _fundingReadoutFingerprint;

    public PaidDivergenceExecutionCorroboration(
        LoopClosureDigest readoutTrainingCorroborationSHA256,
        CortexPolicyQuotaDecisionID fundingDecisionID,
        ulong fundingCandidateFingerprint,
        GrammarRevisionID fundingCandidateRevision,
        LoopClosureDigest forkArmSHA256,
        LoopClosureDigest childExecutionReceiptSHA256,
        CortexPolicyDecisionID executedDivergenceDecisionID,
        LoopClosureDigest executedDivergenceOutcomeID,
        LoopClosureDigest settlementSHA256,
        TapeEventID executedDivergenceOutcomeEventID = default,
        string executedDivergenceOutcomePayloadSHA256 = "")
    {
        ReadoutTrainingCorroborationSHA256 = readoutTrainingCorroborationSHA256;
        QuotaDecisionID = fundingDecisionID;
        QuotaCandidateFingerprint = fundingCandidateFingerprint;
        FundingCandidateRevision = fundingCandidateRevision;
        ForkArmSHA256 = forkArmSHA256;
        ChildExecutionReceiptSHA256 = childExecutionReceiptSHA256;
        ExecutedDivergenceDecisionID = executedDivergenceDecisionID;
        ExecutedDivergenceOutcomeID = executedDivergenceOutcomeID;
        ExecutedDivergenceOutcomeEventID = executedDivergenceOutcomeEventID;
        ExecutedDivergenceOutcomePayloadSHA256 = executedDivergenceOutcomePayloadSHA256;
        PaidDivergenceExecutionCorroborationSHA256 = settlementSHA256;
        _fundingReadoutFingerprint = 0;
        Validate();
    }

    internal PaidDivergenceExecutionCorroboration(
        LoopClosureDigest readoutTrainingCorroborationSHA256,
        CortexPolicyQuotaDecisionID fundingDecisionID,
        ulong fundingReadoutFingerprint,
        ulong fundingCandidateFingerprint,
        GrammarRevisionID fundingCandidateRevision,
        LoopClosureDigest forkArmSHA256,
        LoopClosureDigest childExecutionReceiptSHA256,
        CortexPolicyDecisionID executedDivergenceDecisionID,
        LoopClosureDigest executedDivergenceOutcomeID,
        LoopClosureDigest settlementSHA256,
        TapeEventID executedDivergenceOutcomeEventID = default,
        string executedDivergenceOutcomePayloadSHA256 = "")
    {
        ReadoutTrainingCorroborationSHA256 = readoutTrainingCorroborationSHA256;
        QuotaDecisionID = fundingDecisionID;
        QuotaCandidateFingerprint = fundingCandidateFingerprint;
        FundingCandidateRevision = fundingCandidateRevision;
        ForkArmSHA256 = forkArmSHA256;
        ChildExecutionReceiptSHA256 = childExecutionReceiptSHA256;
        ExecutedDivergenceDecisionID = executedDivergenceDecisionID;
        ExecutedDivergenceOutcomeID = executedDivergenceOutcomeID;
        ExecutedDivergenceOutcomeEventID = executedDivergenceOutcomeEventID;
        ExecutedDivergenceOutcomePayloadSHA256 = executedDivergenceOutcomePayloadSHA256;
        PaidDivergenceExecutionCorroborationSHA256 = settlementSHA256;
        _fundingReadoutFingerprint = fundingReadoutFingerprint;
        Validate();
    }

    public LoopClosureDigest ReadoutTrainingCorroborationSHA256 { get; }
    public CortexPolicyQuotaDecisionID QuotaDecisionID { get; }
    public ulong QuotaReadoutFingerprint => _fundingReadoutFingerprint;
    public ulong QuotaCandidateFingerprint { get; }
    public GrammarRevisionID FundingCandidateRevision { get; }
    public LoopClosureDigest ForkArmSHA256 { get; }
    public LoopClosureDigest ChildExecutionReceiptSHA256 { get; }
    public CortexPolicyDecisionID ExecutedDivergenceDecisionID { get; }
    public LoopClosureDigest ExecutedDivergenceOutcomeID { get; }
    public TapeEventID ExecutedDivergenceOutcomeEventID { get; }
    public string ExecutedDivergenceOutcomePayloadSHA256 { get; }
    public LoopClosureDigest PaidDivergenceExecutionCorroborationSHA256 { get; }

    internal static PaidDivergenceExecutionCorroboration Create(
        LoopClosureDigest readoutTrainingCorroborationSHA256,
        CortexPolicyQuotaDecisionID fundingDecisionID,
        ulong fundingReadoutFingerprint,
        ulong fundingCandidateFingerprint,
        GrammarRevisionID fundingCandidateRevision,
        LoopClosureDigest forkArmSHA256,
        LoopClosureDigest childExecutionReceiptSHA256,
        CortexPolicyDecisionID executedDivergenceDecisionID,
        LoopClosureDigest executedDivergenceOutcomeID,
        TapeEventID executedDivergenceOutcomeEventID = default,
        string executedDivergenceOutcomePayloadSHA256 = "")
    {
        string canonical = Canonical(fundingReadoutFingerprint, readoutTrainingCorroborationSHA256, fundingDecisionID,
            fundingCandidateFingerprint, fundingCandidateRevision, forkArmSHA256,
            childExecutionReceiptSHA256, executedDivergenceDecisionID, executedDivergenceOutcomeID,
            executedDivergenceOutcomeEventID, executedDivergenceOutcomePayloadSHA256);
        return new(readoutTrainingCorroborationSHA256, fundingDecisionID, fundingReadoutFingerprint,
            fundingCandidateFingerprint, fundingCandidateRevision, forkArmSHA256, childExecutionReceiptSHA256,
            executedDivergenceDecisionID, executedDivergenceOutcomeID,
            new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))),
            executedDivergenceOutcomeEventID, executedDivergenceOutcomePayloadSHA256);
    }

    public void Validate()
    {
        if (!ReadoutTrainingCorroborationSHA256.IsValid || QuotaDecisionID.Value == 0
            || _fundingReadoutFingerprint == 0 || QuotaCandidateFingerprint == 0 || FundingCandidateRevision == GrammarRevisionID.Zero
            || !ForkArmSHA256.IsValid || !ChildExecutionReceiptSHA256.IsValid
            || ExecutedDivergenceDecisionID.Value == 0 || !ExecutedDivergenceOutcomeID.IsValid
            || (ExecutedDivergenceOutcomeEventID.Value == 0) != (ExecutedDivergenceOutcomePayloadSHA256.Length == 0)
            || ExecutedDivergenceOutcomeEventID.Value < 0
            || ExecutedDivergenceOutcomePayloadSHA256.Length != 0
                && (ExecutedDivergenceOutcomePayloadSHA256.Length != 64
                    || ExecutedDivergenceOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            || !PaidDivergenceExecutionCorroborationSHA256.IsValid)
            throw new InvalidDataException($"readout training corroboration settlement identity is malformed: funding={QuotaDecisionID} readout={_fundingReadoutFingerprint:X16} executed-divergence-decision={ExecutedDivergenceDecisionID} executed-divergence-outcome-valid={(ExecutedDivergenceOutcomeID.IsValid ? 1 : 0)}");
        string canonical = Canonical(_fundingReadoutFingerprint, ReadoutTrainingCorroborationSHA256, QuotaDecisionID,
            QuotaCandidateFingerprint, FundingCandidateRevision, ForkArmSHA256,
            ChildExecutionReceiptSHA256, ExecutedDivergenceDecisionID, ExecutedDivergenceOutcomeID,
            ExecutedDivergenceOutcomeEventID, ExecutedDivergenceOutcomePayloadSHA256);
        if (!string.Equals(PaidDivergenceExecutionCorroborationSHA256.Value,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), StringComparison.Ordinal))
            throw new InvalidDataException("paid divergence execution corroboration digest does not match its typed payload");
    }

    private static string Canonical(
        ulong readoutFingerprint,
        LoopClosureDigest training,
        CortexPolicyQuotaDecisionID funding,
        ulong fingerprint,
        GrammarRevisionID revision,
        LoopClosureDigest forkArm,
        LoopClosureDigest child,
        CortexPolicyDecisionID executedDivergenceDecision,
        LoopClosureDigest executedDivergenceOutcome,
        TapeEventID executedDivergenceOutcomeEventID = default,
        string executedDivergenceOutcomePayloadSHA256 = "")
        // Frozen digest token funded-dissent-execution-v4; identifier-side names are Paid/Divergence.
        => string.Join('|', "funded-dissent-execution-v4", training.Value, funding.Value,
            readoutFingerprint.ToString("X16"), fingerprint.ToString("X16"), revision.Value,
            forkArm.Value, child.Value, executedDivergenceDecision.Value, executedDivergenceOutcome.Value,
            executedDivergenceOutcomeEventID.Value, executedDivergenceOutcomePayloadSHA256);
}
