namespace Cogito;

using Cogito.Grammar;
using System.Security.Cryptography;
using System.Text;

/// Provenance carried by a policy-boundary receipt when the learned readout has a
/// grammar teacher behind it.  A readout may be externally supplied, so the corroboration
/// is optional; when present every identity is checked as one immutable unit.
public sealed class PolicyBoundaryTeacherCorroboration
{
    public PolicyBoundaryTeacherCorroboration(in LoopClosureR4Provenance provenance)
        : this(provenance.Teacher.MatchedEventIDs, provenance.Teacher.EvidenceDigest.Value,
            new LoopLineageNodeID(provenance.Episode.EpisodeID.Value), provenance.Fold.Revision,
            provenance.Teacher.FoldRevision, provenance.Training.ReadoutTrainingCorroborationSHA256)
    {
        provenance.Validate();
    }

    public PolicyBoundaryTeacherCorroboration(
        IReadOnlyList<TapeEventID> teacherEventIDs,
        string evidenceSHA256,
        LoopLineageNodeID foldNodeID,
        GrammarRevisionID foldRevision,
        GrammarRevisionID teacherRevision = default,
        LoopClosureDigest readoutTrainingCorroborationSHA256 = default)
    {
        TeacherEventIDs = teacherEventIDs?.ToArray() ?? throw new ArgumentNullException(nameof(teacherEventIDs));
        EvidenceSHA256 = evidenceSHA256 ?? throw new ArgumentNullException(nameof(evidenceSHA256));
        FoldNodeID = foldNodeID;
        FoldRevision = foldRevision;
        TeacherRevision = teacherRevision == GrammarRevisionID.Zero ? foldRevision : teacherRevision;
        ReadoutTrainingCorroborationSHA256 = readoutTrainingCorroborationSHA256;
    }

    public IReadOnlyList<TapeEventID> TeacherEventIDs { get; }
    public string EvidenceSHA256 { get; }
    public LoopLineageNodeID FoldNodeID { get; }
    public GrammarRevisionID FoldRevision { get; }
    public GrammarRevisionID TeacherRevision { get; }
    public LoopClosureDigest ReadoutTrainingCorroborationSHA256 { get; }

    public void Validate()
    {
        if (TeacherEventIDs.Count == 0 || TeacherEventIDs.Any(static id => id.Value < 0)
            || TeacherEventIDs.Distinct().Count() != TeacherEventIDs.Count)
            throw new InvalidDataException("policy boundary teacher corroboration has no distinct teacher events");
        if (EvidenceSHA256.Length != 64 || EvidenceSHA256.Any(static c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("policy boundary teacher corroboration has an invalid evidence digest");
        if (!FoldNodeID.IsValid || FoldRevision.Value == 0 || TeacherRevision.Value == 0 || TeacherRevision > FoldRevision)
            throw new InvalidDataException("policy boundary teacher corroboration omits its source-to-consuming fold ordering");
    }

    internal string Canonical()
    {
        Validate();
        return string.Join(',', TeacherEventIDs.Select(static id => id.Value))
            + '|' + EvidenceSHA256 + '|' + FoldNodeID.Value + '|' + FoldRevision.Value + '|' + TeacherRevision.Value;
    }
}

internal enum PolicyBoundaryDivergenceArmKinds : byte
{
    LiveCandidate,
    ForcedNull,
}

/// The typed action/result produced by one divergence rail.  Process completion and
/// policy execution are separate receipts; neither may be inferred from the other.
/// The forced-null arm is a separate divergent arm, never the live candidate with
/// its label changed afterward.
internal readonly record struct PolicyBoundaryDivergenceArmOutcome(
    PolicyBoundaryDivergenceArmKinds Arm,
    int Action,
    CortexPolicyAuthorities Authority,
    CortexPolicySelectionCauses SelectionCause,
    bool ChildProcessCompleted,
    bool BehaviorallyExecuted,
    bool Diverged,
    int Horizon,
    long MatchedSpend,
    LoopClosureDigest OutcomeID)
{
    public CortexPolicyDecisionID DecisionID { get; init; }
    public int LaunchpadAction { get; init; } = -1;
    public int RawCandidateAction { get; init; } = -1;
    public int SelectedCandidateAction { get; init; } = -1;
    public ulong ReadoutFingerprint { get; init; }
    public ulong ReadoutRevision { get; init; }
    public ulong ReadoutOccurrenceDigest { get; init; }
    public ulong CandidateFingerprint { get; init; }
    public TapeEventID ExecutedOutcomeEventID { get; init; }
    public string ExecutedOutcomePayloadSHA256 { get; init; } = "";

    public void Validate()
    {
        if (!Enum.IsDefined(Arm) || !Enum.IsDefined(Authority) || !Enum.IsDefined(SelectionCause)
            || DecisionID.Value == 0 || LaunchpadAction < 0 || RawCandidateAction < -1 || SelectedCandidateAction < -1
            || (RawCandidateAction == -1) != (SelectedCandidateAction == -1)
            || Action < 0 || Horizon <= 0 || MatchedSpend <= 0 || !OutcomeID.IsValid
            || ReadoutFingerprint == 0 || ReadoutRevision == 0)
            throw new InvalidDataException("policy boundary divergence outcome is malformed");
        if (BehaviorallyExecuted && !ChildProcessCompleted)
            throw new InvalidDataException("policy boundary divergence marks behavior without a completed child process");
        if (ReadoutOccurrenceDigest == 0 || CandidateFingerprint == 0)
            throw new InvalidDataException("policy boundary divergence outcome omits its readout support identity");
        if ((ExecutedOutcomeEventID.Value == 0) != (ExecutedOutcomePayloadSHA256.Length == 0)
            || ExecutedOutcomeEventID.Value < 0
            || ExecutedOutcomePayloadSHA256.Length != 0
                && (ExecutedOutcomePayloadSHA256.Length != 64
                    || ExecutedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("policy boundary divergence outcome ordinary outcome identity is malformed");
        if (Arm == PolicyBoundaryDivergenceArmKinds.LiveCandidate)
        {
            if (Authority != CortexPolicyAuthorities.Grammar || !ChildProcessCompleted || !BehaviorallyExecuted
                || SelectionCause != CortexPolicySelectionCauses.GrammarCandidate
                || Action != SelectedCandidateAction
                || SelectionCause is CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate)
                throw new InvalidDataException("policy boundary live candidate outcome does not prove learned execution");
        }
        else if (!ChildProcessCompleted || !BehaviorallyExecuted || !Diverged || Authority != CortexPolicyAuthorities.Grammar
            || SelectionCause != CortexPolicySelectionCauses.TrialOverride
            || Action != SelectedCandidateAction || Action == LaunchpadAction || Action == RawCandidateAction)
            throw new InvalidDataException("policy boundary forced-null outcome was relabeled as a live action");
    }
}

/// Terminal status for the live candidate rail. A candidate may be absent or
/// guard-denied; only configured execution carries an executed child outcome.
internal readonly record struct PolicyBoundaryDivergenceCandidateTerminal(
    CortexPolicyTrialExecutionOutcomes Outcome,
    long RequestCount,
    long GuardAdmittedCount,
    int Horizon,
    long MatchedSpend,
    PolicyBoundaryDivergenceArmOutcome? ExecutedOutcome)
{
    public bool HasExecutedOutcome => ExecutedOutcome is not null;

    public void Validate()
    {
        if (!Enum.IsDefined(Outcome) || RequestCount < 0 || GuardAdmittedCount < 0
            || GuardAdmittedCount > RequestCount || Horizon <= 0 || MatchedSpend < 0)
            throw new InvalidDataException("policy boundary candidate terminal status is malformed");

        switch (Outcome)
        {
            case CortexPolicyTrialExecutionOutcomes.NotAttempted:
                if (RequestCount != 0 || GuardAdmittedCount != 0 || ExecutedOutcome is not null)
                    throw new InvalidDataException("policy boundary candidate terminal marks not-attempted execution with custody");
                break;
            case CortexPolicyTrialExecutionOutcomes.GuardDenied:
                if (RequestCount <= 0 || GuardAdmittedCount != 0 || ExecutedOutcome is not null)
                    throw new InvalidDataException("policy boundary candidate terminal carries invalid guard-denied accounting");
                break;
            case CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted:
                if (RequestCount <= 0 || GuardAdmittedCount <= 0 || ExecutedOutcome is not PolicyBoundaryDivergenceArmOutcome executed)
                    throw new InvalidDataException("policy boundary candidate terminal marks configured execution without an executed outcome");
                executed.Validate();
                if (executed.Arm != PolicyBoundaryDivergenceArmKinds.LiveCandidate
                    || !executed.BehaviorallyExecuted
                    || executed.Horizon != Horizon
                    || executed.MatchedSpend != MatchedSpend)
                    throw new InvalidDataException("policy boundary candidate terminal executed outcome does not bind its terminal rail");
                break;
            default:
                throw new InvalidDataException("policy boundary candidate terminal carries an unknown execution outcome");
        }
    }
}

/// The custody object for line 4.  It binds the readout, funding, matched four-rail
/// receipt, and typed outcomes before adjudication can call the divergence real.
internal readonly record struct PolicyBoundaryDivergenceProof(
    CortexPolicyDecisionID DecisionID,
    CortexPolicyID Policy,
    int LaunchpadAction,
    int RawCandidateAction,
    ulong ReadoutFingerprint,
    ulong ReadoutOccurrenceDigest,
    ulong ReadoutCandidateFingerprint,
    GrammarRevisionID ReadoutRevision,
    int ReadoutComparisons,
    int ReadoutAgreements,
    int ReadoutMisses,
    CortexPolicyTrialQuotaDecision Funding,
    CortexPolicyTrialCompletion Completion,
    PolicyBoundaryForkReceipt ForkReceipt,
    PolicyBoundaryDivergenceCandidateTerminal Candidate,
    PolicyBoundaryDivergenceArmOutcome ForcedNull,
    PolicyBoundaryTeacherCorroboration? Teacher,
    LoopClosureR4Provenance? Provenance = null)
{
    public void Validate(IPolicyBoundaryDomain domain)
    {
        ForkReceipt.Validate(domain);
        if (!domain.PolicyID.Equals(Policy)
            || !domain.Schema.Policy.Equals(Policy)
            || !domain.PolicyBinding.PolicyID.Equals(Policy)
            || !Funding.Policy.Equals(Policy))
            throw new InvalidDataException("policy boundary divergence policy authority is not joined");
        for (int index = 0; index < ForkReceipt.Arms.Length; index++)
        {
            PolicyBoundaryArmReceipt arm = ForkReceipt.Arms[index];
            PolicyCanonicalStateID state = arm.ExecutedCanonicalState;
            bool executionIdentityRequired = arm.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                || arm.GuardAdmittedCount > 0;
            if (executionIdentityRequired && (state.Version == 0 || !state.Policy.Equals(Policy)))
                throw new InvalidDataException("policy boundary divergence arm execution has no joined policy scope");
            if (state.Version != 0 && (!state.Policy.Equals(Policy) || !domain.ValidateCanonicalState(in state)))
                throw new InvalidDataException("policy boundary divergence arm carries a foreign policy scope");
        }
        if (!ForkReceipt.Verified || !ForkReceipt.MatchedSpend || !ForkReceipt.ForcedNullBehaviorExecuted
            || !ForkReceipt.ForcedNullDiverged || !ForkReceipt.AllChildrenCompleted)
            throw new InvalidDataException("policy boundary divergence lacks a verified four-rail receipt");
        if (!Funding.Policy.Equals(Policy)
            || Funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            || !Funding.QuotaDecisionID.Equals(Completion.QuotaDecisionID)
            || !Funding.QuotaDecisionID.Equals(ForkReceipt.QuotaDecisionID)
            || Funding.ReadoutFingerprint != ReadoutFingerprint
            || Funding.CandidateFingerprint != ReadoutCandidateFingerprint
            || Funding.CandidateRevision != ReadoutRevision)
            throw new InvalidDataException("policy boundary divergence funding does not bind its readout");
        if (Funding.QuotaDecisionID.Value == 0 || ForkReceipt.QuotaDecisionID.Value == 0
            || string.IsNullOrWhiteSpace(Funding.AllocationIdentity)
            || Funding.SeedAuditOnlyDigest.Length != 64
            || Funding.SeedAuditOnlyDigest.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || Funding.AllocationArmSteps <= 0
            || Funding.AllocationDigest != CortexPolicyTrialAllocation.ComputeDigest(
                Funding.Policy, CortexPolicyAuthorities.Grammar, Funding.AllocationArmSteps, Funding.AllocationIdentity)
            || Funding.HeldArmSteps != Funding.PlannedArmSteps
            || Funding.Decision == CortexPolicyQuotaDecisions.Paid && Funding.UsedSteps != Funding.PlannedArmSteps
            || Funding.Decision == CortexPolicyQuotaDecisions.Reused && Funding.UsedSteps != 0)
            throw new InvalidDataException("policy boundary divergence funding allocation/accounting authority is malformed");
        if (ReadoutCandidateFingerprint == 0 || ReadoutComparisons <= 0 || ReadoutAgreements != ReadoutComparisons || ReadoutMisses != 0)
            throw new InvalidDataException("policy boundary divergence readout support is not exact");
        if (Funding.ArmCount != 4 || Funding.RequestedHorizonSteps != ForkReceipt.Horizons[^1]
            || Funding.PlannedArmSteps != checked((long)Funding.ArmCount * Funding.RequestedHorizonSteps)
            || Completion.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed
            || Completion.ActualExecutedArmSteps <= 0
            || Completion.ReclaimedOrUnused < 0
            || Funding.PlannedArmSteps != Completion.ActualExecutedArmSteps + Completion.ReclaimedOrUnused)
            throw new InvalidDataException("policy boundary divergence settlement is not a passed paid outcome");
        long armSpend = ForkReceipt.ComputeTerminalMatchedSpend();
        if (armSpend != Completion.ActualExecutedArmSteps)
            throw new InvalidDataException("policy boundary divergence settlement does not equal the four-rail arm spend");
        if (ForkReceipt.SourceDecisionReadoutFingerprint != ReadoutFingerprint
            || ForkReceipt.SourceDecisionReadoutRevision != ReadoutRevision.Value)
            throw new InvalidDataException("policy boundary divergence receipt readout identity drifted");
        Candidate.Validate();
        ForcedNull.Validate();
        if (Candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateOutcome
            && !domain.ValidateActionRelation(candidateOutcome.SelectionCause, candidateOutcome.LaunchpadAction,
                candidateOutcome.RawCandidateAction, candidateOutcome.SelectedCandidateAction, candidateOutcome.Action))
            throw new InvalidDataException("policy boundary candidate outcome action relation is not domain-owned");
        if (!domain.ValidateActionRelation(ForcedNull.SelectionCause, ForcedNull.LaunchpadAction,
                ForcedNull.RawCandidateAction, ForcedNull.SelectedCandidateAction, ForcedNull.Action))
            throw new InvalidDataException("policy boundary forced-null outcome action relation is not domain-owned");
        int terminalHorizon = ForkReceipt.Horizons[^1];
        if (Candidate.Horizon != terminalHorizon || ForcedNull.Horizon != terminalHorizon)
            throw new InvalidDataException("policy boundary divergence candidate and forced-null horizons must be receipt-terminal");
        PolicyBoundaryArmReceipt candidateArm = default;
        PolicyBoundaryArmReceipt nullArm = default;
        for (int index = 0; index < ForkReceipt.Arms.Length; index++)
        {
            PolicyBoundaryArmReceipt arm = ForkReceipt.Arms[index];
            if (arm.Arm == PolicyBoundaryArms.Candidate && arm.Horizon == terminalHorizon) candidateArm = arm;
            if (arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == terminalHorizon) nullArm = arm;
        }
        if (candidateArm.Horizon != Candidate.Horizon || nullArm.Horizon != ForcedNull.Horizon
            || candidateArm.MatchedSpend != Candidate.MatchedSpend || nullArm.MatchedSpend != ForcedNull.MatchedSpend
            || candidateArm.ExecutionOutcome != Candidate.Outcome
            || candidateArm.RequestCount != Candidate.RequestCount
            || candidateArm.GuardAdmittedCount != Candidate.GuardAdmittedCount
            || Candidate.Horizon != ForcedNull.Horizon || Candidate.MatchedSpend != ForcedNull.MatchedSpend)
            throw new InvalidDataException("policy boundary divergence outcomes do not bind the same matched horizon/spend");
        candidateArm.ValidateRequestAccounting(domain);
        if (Candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateExecution)
        {
            candidateArm.ValidateExecutedDecisionIdentity(domain, requireGrammar: true);
            if (candidateArm.ExecutedAction != candidateExecution.Action
                || candidateArm.ExecutedAuthority != candidateExecution.Authority
                || candidateArm.ExecutedSelectionCause != candidateExecution.SelectionCause
                || candidateArm.ChildProcessCompleted != candidateExecution.ChildProcessCompleted
                || candidateArm.BehaviorallyExecuted != candidateExecution.BehaviorallyExecuted
                || candidateArm.Diverged != candidateExecution.Diverged
                || candidateExecution.Diverged
                || !candidateArm.ExecutedDecisionID.Equals(candidateExecution.DecisionID)
                || candidateArm.ExecutedLaunchpadAction != candidateExecution.LaunchpadAction
                || candidateArm.ExecutedRawCandidateAction != candidateExecution.RawCandidateAction
                || candidateArm.ExecutedSelectedCandidateAction != candidateExecution.SelectedCandidateAction
                || candidateArm.ExecutedReadoutFingerprint != candidateExecution.ReadoutFingerprint
                || candidateArm.ExecutedReadoutRevision != candidateExecution.ReadoutRevision
                || candidateArm.ExecutedReadoutOccurrenceDigest != candidateExecution.ReadoutOccurrenceDigest
                || candidateArm.ExecutedCandidateFingerprint != candidateExecution.CandidateFingerprint
                || candidateArm.ExecutedOutcomeEventID != candidateExecution.ExecutedOutcomeEventID
                || candidateArm.ExecutedOutcomePayloadSHA256 != candidateExecution.ExecutedOutcomePayloadSHA256)
                throw new InvalidDataException("policy boundary candidate terminal execution drifted from its candidate rail");
        }
        else if (candidateArm.HasAnyExecutedDecisionIdentityData)
            throw new InvalidDataException("policy boundary candidate terminal omits execution but its rail carries execution custody");
        nullArm.ValidateExecutedDecisionIdentity(domain);
        if (Candidate.ExecutedOutcome is not null)
            candidateArm.ValidateExecutedReadoutAncestry(Policy, ReadoutRevision.Value, domain);
        nullArm.ValidateExecutedReadoutAncestry(Policy, ReadoutRevision.Value, domain);
        // The source funding tuple is immutable, but a paid child may consume a
        // verified successor publication before it executes.  The terminal rail
        // verifier has already authenticated that successor scope (canonical state,
        // carried program digest, support, and revision) from the child tape and its
        // verification receipts.  Keep the distinction explicit here: same-tuple
        // execution is the ordinary path; a changed candidate is admissible only as
        // a strictly later Grammar-owned successor on the same readout fingerprint.
        if (Candidate.ExecutedOutcome is not null
            && !AcceptExecutedReadoutRelation(in candidateArm, ReadoutFingerprint,
                ReadoutCandidateFingerprint, ReadoutOccurrenceDigest, ReadoutRevision.Value, domain))
            throw new InvalidDataException("policy boundary candidate execution is neither source-paid nor a verified successor");
        if (!AcceptExecutedReadoutRelation(in nullArm, ReadoutFingerprint,
            ReadoutCandidateFingerprint, ReadoutOccurrenceDigest, ReadoutRevision.Value, domain))
            throw new InvalidDataException("policy boundary forced-null execution is neither source-paid nor a verified successor");
        if (nullArm.ExecutedAction != ForcedNull.Action
            || nullArm.ExecutedAuthority != ForcedNull.Authority
            || nullArm.ExecutedSelectionCause != ForcedNull.SelectionCause
            || !nullArm.ExecutedDecisionID.Equals(ForcedNull.DecisionID)
            || nullArm.ExecutedLaunchpadAction != ForcedNull.LaunchpadAction
            || nullArm.ExecutedRawCandidateAction != ForcedNull.RawCandidateAction
            || nullArm.ExecutedSelectedCandidateAction != ForcedNull.SelectedCandidateAction
            || nullArm.ExecutedOutcomeEventID != ForcedNull.ExecutedOutcomeEventID
            || nullArm.ExecutedOutcomePayloadSHA256 != ForcedNull.ExecutedOutcomePayloadSHA256
            || DecisionID.Equals(ForcedNull.DecisionID))
            throw new InvalidDataException("policy boundary divergence outcome was not derived from its executed child decision");
        if (Candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateExecutionReadout
            && (candidateArm.ExecutedReadoutOccurrenceDigest != candidateExecutionReadout.ReadoutOccurrenceDigest
                || candidateArm.ExecutedCandidateFingerprint != candidateExecutionReadout.CandidateFingerprint))
            throw new InvalidDataException("policy boundary candidate terminal execution readout drifted from its candidate rail");
        if (nullArm.ExecutedReadoutOccurrenceDigest != ForcedNull.ReadoutOccurrenceDigest
            || nullArm.ExecutedCandidateFingerprint != ForcedNull.CandidateFingerprint)
            throw new InvalidDataException("policy boundary divergence outcomes do not bind their child execution readouts");
        if (ForkReceipt.ExecutionCorroboration is PaidDivergenceExecutionCorroboration executionCorroboration
            && (!executionCorroboration.ExecutedDivergenceDecisionID.Equals(ForcedNull.DecisionID)
                || executionCorroboration.ExecutedDivergenceOutcomeID != ForcedNull.OutcomeID
                || executionCorroboration.ExecutedDivergenceOutcomeEventID != ForcedNull.ExecutedOutcomeEventID
                || executionCorroboration.ExecutedDivergenceOutcomePayloadSHA256 != ForcedNull.ExecutedOutcomePayloadSHA256
                || executionCorroboration.ChildExecutionReceiptSHA256 != Cortex.DigestExecutedDivergenceChildExecution(
                    ForcedNull.DecisionID, ForcedNull.OutcomeID)))
            throw new InvalidDataException("policy boundary divergence execution corroboration is not bound to the terminal forced-null child");
        PolicyCanonicalStateID candidateState = candidateArm.ExecutedCanonicalState;
        PolicyCanonicalStateID nullState = nullArm.ExecutedCanonicalState;
        if ((Candidate.ExecutedOutcome is not null && (!candidateState.Policy.Equals(Policy)
                || !domain.ValidateCanonicalState(in candidateState)))
            || !nullState.Policy.Equals(Policy)
            || !domain.ValidateCanonicalState(in nullState))
            throw new InvalidDataException("policy boundary divergence outcomes omit an immutable execution scope");
        if (Candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateExecutionIdentity
            && candidateExecutionIdentity.OutcomeID == ForcedNull.OutcomeID)
            throw new InvalidDataException("policy boundary divergence candidate and forced-null outcomes share an identity");
        PolicyBoundaryForkReceipt receiptForIdentity = ForkReceipt;
        if (Candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateExecutionOutcome
            && candidateExecutionOutcome.OutcomeID != ComputeOutcomeID(in receiptForIdentity, in candidateArm, candidateExecutionOutcome.Action, domain))
            throw new InvalidDataException("policy boundary candidate terminal identity was not recomputed from its arm receipt");
        if (ForcedNull.OutcomeID != ComputeOutcomeID(in receiptForIdentity, in nullArm, ForcedNull.Action, domain))
            throw new InvalidDataException("policy boundary divergence outcome identity was not recomputed from its arm receipt");
        if (Teacher is not null)
        {
            Teacher.Validate();
            if (Teacher.TeacherRevision.Value >= ReadoutRevision.Value)
                throw new InvalidDataException("policy boundary divergence revision ordering is not fold-to-teacher-to-readout");
        }
        if (ForkReceipt.TeacherCorroboration is PolicyBoundaryTeacherCorroboration receiptTeacher
            && (Teacher is null || !string.Equals(receiptTeacher.Canonical(), Teacher.Canonical(), StringComparison.Ordinal)))
            throw new InvalidDataException("policy boundary divergence teacher corroboration disagrees with its fork receipt");
        if (Provenance is LoopClosureR4Provenance provenance)
        {
            provenance.Validate();
            if (ForkReceipt.ExecutionCorroboration is not PaidDivergenceExecutionCorroboration execution)
                throw new InvalidDataException("policy boundary R4 divergence lacks its paid execution corroboration");
            if (execution.ReadoutTrainingCorroborationSHA256 != provenance.Training.ReadoutTrainingCorroborationSHA256)
                throw new InvalidDataException("policy boundary paid execution corroboration disagrees with R4 training corroboration");
            if (Teacher is null)
                throw new InvalidDataException("policy boundary R4 divergence requires its typed teacher corroboration");
            PolicyBoundaryTeacherCorroboration expectedTeacher = new(in provenance);
            if (!string.Equals(Teacher.Canonical(), expectedTeacher.Canonical(), StringComparison.Ordinal))
                throw new InvalidDataException("policy boundary R4 teacher corroboration disagrees with provenance");
            if (ReadoutOccurrenceDigest == 0
                || provenance.ReadoutOccurrenceDigest != ReadoutOccurrenceDigest
                || provenance.LearnedReadoutRevision != ReadoutRevision)
                throw new InvalidDataException("policy boundary divergence R4 provenance does not bind the readout");
            if (Teacher.FoldRevision != provenance.Fold.Revision
                || Teacher.EvidenceSHA256 != provenance.Teacher.EvidenceDigest.Value
                || Teacher.TeacherRevision.Value >= Teacher.FoldRevision.Value
                || Teacher.FoldRevision.Value >= ReadoutRevision.Value)
                throw new InvalidDataException("policy boundary divergence teacher corroboration disagrees with R4 provenance");
        }
    }

    public LoopClosureDigest EvidenceDigest(IPolicyBoundaryDomain domain)
    {
        Validate(domain);
        int terminalHorizon = ForkReceipt.Horizons[^1];
        PolicyBoundaryArmReceipt candidateArm = ForkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.Candidate && arm.Horizon == terminalHorizon);
        PolicyBoundaryArmReceipt nullArm = ForkReceipt.Arms.Single(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == terminalHorizon);
        string candidateExecution = Candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome executed
            ? string.Join(',', executed.DecisionID.Value, executed.Action, executed.Authority, executed.SelectionCause,
                executed.ChildProcessCompleted ? 1 : 0, executed.BehaviorallyExecuted ? 1 : 0, executed.Diverged ? 1 : 0,
                executed.LaunchpadAction, executed.RawCandidateAction, executed.SelectedCandidateAction,
                executed.ReadoutFingerprint, executed.ReadoutRevision, executed.ReadoutOccurrenceDigest,
                executed.CandidateFingerprint, executed.ExecutedOutcomeEventID.Value,
                executed.ExecutedOutcomePayloadSHA256, executed.OutcomeID.Value)
            : "none";
        string candidateScope = Candidate.ExecutedOutcome is not null
            ? string.Join(',', candidateArm.ExecutedCanonicalState.Policy.Value, (byte)candidateArm.ExecutedCanonicalState.Kind,
                candidateArm.ExecutedCanonicalState.Version, candidateArm.ExecutedCanonicalState.Value.ToString("X16"))
            : "none";
        string canonical = string.Join('|', DecisionID.Value, Policy.Value, LaunchpadAction,
            ReadoutFingerprint.ToString("X16"), ReadoutOccurrenceDigest.ToString("X16"), ReadoutRevision.Value, Funding.QuotaDecisionID,
            RawCandidateAction, ReadoutCandidateFingerprint, ReadoutComparisons, ReadoutAgreements, ReadoutMisses,
            Completion.ActualExecutedArmSteps, Completion.ReclaimedOrUnused, ForkReceipt.Obligation,
            Candidate.Outcome, Candidate.RequestCount, Candidate.GuardAdmittedCount, Candidate.Horizon, Candidate.MatchedSpend,
            candidateExecution, candidateScope,
            ForcedNull.DecisionID.Value, ForcedNull.LaunchpadAction, ForcedNull.RawCandidateAction, ForcedNull.SelectedCandidateAction,
            ForcedNull.ReadoutFingerprint, ForcedNull.ReadoutRevision, ForcedNull.ReadoutOccurrenceDigest, ForcedNull.CandidateFingerprint,
            nullArm.ExecutedCanonicalState.Policy.Value, (byte)nullArm.ExecutedCanonicalState.Kind,
            nullArm.ExecutedCanonicalState.Version, nullArm.ExecutedCanonicalState.Value.ToString("X16"),
            ForcedNull.Arm, ForcedNull.Action, ForcedNull.Authority, ForcedNull.SelectionCause,
            ForcedNull.ChildProcessCompleted ? 1 : 0, ForcedNull.BehaviorallyExecuted ? 1 : 0, ForcedNull.Diverged ? 1 : 0,
            ForcedNull.ExecutedOutcomeEventID.Value, ForcedNull.ExecutedOutcomePayloadSHA256, ForcedNull.OutcomeID.Value,
            ForcedNull.Horizon, ForcedNull.MatchedSpend,
            Teacher?.Canonical() ?? "none", Provenance?.Fold.ReceiptDigest.Value ?? "none",
            Provenance?.Teacher.ProvenanceDigest.Value ?? "none", Provenance?.Episode.EpisodeDigest.Value ?? "none",
            Provenance?.Training.ReadoutTrainingCorroborationSHA256.Value ?? "none",
            ForkReceipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "none");
        return new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    internal static bool AcceptExecutedReadoutRelation(
        in PolicyBoundaryArmReceipt arm,
        ulong sourceReadoutFingerprint,
        ulong sourceCandidateFingerprint,
        ulong sourceSupportDigest,
        ulong sourceRevision,
        IPolicyBoundaryDomain domain)
    {
        if (!arm.HasExecutedDecisionIdentity || arm.ExecutedReadoutFingerprint != sourceReadoutFingerprint)
            return false;
        bool sourceTuple = arm.ExecutedReadoutRevision == sourceRevision
            && arm.ExecutedCandidateFingerprint == sourceCandidateFingerprint
            && arm.ExecutedReadoutOccurrenceDigest == sourceSupportDigest;
        if (sourceTuple) return true;
        // A successor is not a forgiveness path: it must be a later publication,
        // retain the same readout/program identity, and carry a complete Grammar
        // candidate scope.  Per-child terminal verification has authenticated the
        // durable scope/coverage receipts before this parent receipt is assembled.
        return arm.ExecutedReadoutRevision > sourceRevision
            && arm.ExecutedAuthority == CortexPolicyAuthorities.Grammar
            && (arm.ExecutedSelectionCause is CortexPolicySelectionCauses.GrammarCandidate
                or CortexPolicySelectionCauses.TrialOverride)
            && arm.ExecutedReadoutOccurrenceDigest != 0
            && arm.ExecutedCandidateFingerprint != 0
            && arm.ExecutedCanonicalState.Policy.Equals(domain.PolicyID)
            && domain.ValidateCanonicalState(arm.ExecutedCanonicalState);
    }

    internal static LoopClosureDigest ComputeOutcomeID(
        in PolicyBoundaryForkReceipt receipt,
        in PolicyBoundaryArmReceipt arm,
        int action,
        IPolicyBoundaryDomain domain)
        => domain.ComputeOutcomeIdentity(in receipt, in arm, action);

}

internal readonly record struct PolicyBoundaryDivergenceAdjudication(
    PolicyBoundaryDivergenceProof Proof,
    bool Accepted,
    LoopClosureDigest EvidenceSHA256,
    string Reason)
{
    public void Validate(IPolicyBoundaryDomain domain)
    {
        Proof.Validate(domain);
        if (!Accepted || !EvidenceSHA256.IsValid || string.IsNullOrWhiteSpace(Reason))
            throw new InvalidDataException("policy boundary divergence adjudication is not accepted");
        if (EvidenceSHA256.Value != Proof.EvidenceDigest(domain).Value)
            throw new InvalidDataException("policy boundary divergence adjudication digest does not match its proof");
    }
}

internal static class PolicyBoundaryDivergenceAdjudicator
{
    internal static PolicyBoundaryDivergenceAdjudication Adjudicate(
        in PolicyBoundaryDivergenceProof proof,
        IPolicyBoundaryDomain domain)
    {
        proof.Validate(domain);
        PolicyBoundaryDivergenceAdjudication result = new(proof, true, proof.EvidenceDigest(domain), "candidate terminal status bound to its paid rail; forced-null behavior executed and diverged");
        result.Validate(domain);
        return result;
    }
}

public sealed partial class Cortex
{
    /// Build and adjudicate line 4 from the real decision/readout/funding/settlement
    /// owners. Callers cannot promote an action by assembling an untyped report row.
    internal bool TryAdjudicatePaidDivergence(
        in CortexPolicyDecision decision,
        in CortexPolicyReadoutReceipt readout,
        in CortexPolicyTrialQuotaDecision funding,
        in CortexPolicyTrialCompletion settlement,
        in PolicyBoundaryForkReceipt forkReceipt,
        in PolicyBoundaryDivergenceCandidateTerminal candidate,
        in PolicyBoundaryDivergenceArmOutcome forcedNull,
        PolicyBoundaryTeacherCorroboration? teacher,
        out PolicyBoundaryDivergenceAdjudication adjudication,
        LoopClosureR4Provenance? provenance = null)
    {
        adjudication = default;
        IPolicyBoundaryDomain domain;
        try { domain = RequirePolicyBoundaryDomain(decision.Policy); }
        catch (InvalidDataException) { return false; }
        if (!decision.Policy.Equals(funding.Policy)
            || decision.DecisionID.Value == 0
            || decision.GrammarRevision != readout.Revision
            || decision.Readout.ReadoutCandidateOccurrenceDigest != readout.ReadoutCandidateOccurrenceDigest
            || decision.Readout.ReadoutCandidateFingerprint != readout.ReadoutCandidateFingerprint
            || funding.ReadoutFingerprint != readout.Fingerprint
            || funding.CandidateFingerprint != readout.ReadoutCandidateFingerprint)
            return false;
        if (!readout.IsExact || readout.Fingerprint == 0 || readout.ReadoutCandidateFingerprint == 0 || readout.Revision.Value == 0
            || provenance is not null && readout.ReadoutCandidateOccurrenceDigest == 0)
            return false;
        forkReceipt.ValidateDivergenceCorroboration(readout.Fingerprint, readout.Revision, domain);
        if (forkReceipt.SourceDecisionCandidateFingerprint != readout.ReadoutCandidateFingerprint)
            return false;
        if (candidate.Horizon <= 0
            || candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateExecution
                && (candidateExecution.ReadoutFingerprint != readout.Fingerprint
                    || candidateExecution.ReadoutRevision < readout.Revision.Value
                    || candidateExecution.ReadoutRevision == readout.Revision.Value
                        && (candidateExecution.ReadoutOccurrenceDigest != readout.ReadoutCandidateOccurrenceDigest
                            || candidateExecution.CandidateFingerprint != readout.ReadoutCandidateFingerprint)
                    || candidateExecution.DecisionID.Equals(decision.DecisionID))
            || forcedNull.ReadoutFingerprint != readout.Fingerprint
            || forcedNull.ReadoutRevision < readout.Revision.Value
            || forcedNull.ReadoutRevision == readout.Revision.Value
                && (forcedNull.ReadoutOccurrenceDigest != readout.ReadoutCandidateOccurrenceDigest
                    || forcedNull.CandidateFingerprint != readout.ReadoutCandidateFingerprint)
            || forcedNull.DecisionID.Equals(decision.DecisionID))
            return false;
        PolicyBoundaryDivergenceProof proof = new(
            decision.DecisionID, decision.Policy, decision.LaunchpadAction, decision.RawCandidateAction,
            readout.Fingerprint, readout.ReadoutCandidateOccurrenceDigest, readout.ReadoutCandidateFingerprint, readout.Revision,
            readout.Comparisons, readout.Agreements, readout.Misses,
            funding, settlement, forkReceipt, candidate, forcedNull, teacher, provenance);
        try
        {
            if (!domain.PolicyBinding.PolicyID.Equals(decision.Policy)
                || !_policyBoundaryObligations.TryGetValue(decision.Policy, out PolicyBoundaryObligation? obligation)
                || !obligation.Identity.Policy.Equals(domain.PolicyID)
                || !forkReceipt.Obligation.Equals(obligation.ID))
                return false;
            for (int index = 0; index < forkReceipt.Arms.Length; index++)
            {
                PolicyCanonicalStateID state = forkReceipt.Arms[index].ExecutedCanonicalState;
                if (state.Version != 0 && !state.Policy.Equals(domain.PolicyID)) return false;
            }
            adjudication = PolicyBoundaryDivergenceAdjudicator.Adjudicate(in proof, domain);
        }
        catch (InvalidDataException) { return false; }
        return true;
    }
}
