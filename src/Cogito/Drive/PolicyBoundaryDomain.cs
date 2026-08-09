namespace Cogito;

using System.Security.Cryptography;
using System.Text;

/// Inputs which make a funded policy-boundary lease executable. The boundary
/// machinery transports these values; the policy domain owns their meaning.
public readonly record struct PolicyBoundarySeedAuthorityInputs(
    CortexPolicyAuthorities CandidateAuthority,
    CortexPolicyAuthorities ForcedNullAuthority,
    CortexPolicySelectionCauses CandidateSelectionCause,
    CortexPolicySelectionCauses ForcedNullSelectionCause)
{
    internal bool IsValid
        => Enum.IsDefined(CandidateAuthority)
            && Enum.IsDefined(ForcedNullAuthority)
            && Enum.IsDefined(CandidateSelectionCause)
            && Enum.IsDefined(ForcedNullSelectionCause);
}

public readonly record struct PolicyBoundaryArmTopology(
    CortexPolicyAuthorities LiveAuthorityCeiling,
    CortexPolicyAuthorities ControlAuthority,
    CortexPolicyAuthorities TrialAllocationAuthority,
    EmlProcessCatalogs LiveProcessCatalog,
    EmlProcessCatalogs ControlProcessCatalog,
    EmlRung0Modes LiveRung0,
    EmlRung0Modes ControlRung0,
    EmlDeliberationModes LiveDeliberation,
    EmlDeliberationModes ControlDeliberation,
    long TrialArmSteps,
    string TrialAllocationIdentity)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(LiveAuthorityCeiling) || !Enum.IsDefined(ControlAuthority)
            || !Enum.IsDefined(TrialAllocationAuthority)
            || !Enum.IsDefined(LiveProcessCatalog) || !Enum.IsDefined(ControlProcessCatalog)
            || !Enum.IsDefined(LiveRung0) || !Enum.IsDefined(ControlRung0)
            || !Enum.IsDefined(LiveDeliberation) || !Enum.IsDefined(ControlDeliberation)
            || TrialArmSteps <= 0 || string.IsNullOrWhiteSpace(TrialAllocationIdentity))
            throw new InvalidDataException("policy-boundary arm topology is incomplete");
    }
}

/// Semantic owner of one policy-boundary domain. Cortex owns funding,
/// settlement, custody, and fail-closed transport; implementations own the
/// schema, identity relations, canonical state, and R4 witness construction.
public interface IPolicyBoundaryDomain
{
    CortexPolicyID PolicyID { get; }
    CortexPolicySchema Schema { get; }
    PolicyCanonicalStateKinds CanonicalStateKind { get; }
    PolicyCanonicalScopeModes CanonicalScopeMode { get; }
    PolicyCanonicalStateID[] CanonicalStates { get; }
    ushort BoundaryFeatureID { get; }
    PolicyBoundaryArmTopology ArmTopology { get; }
    PolicyBoundarySeedAuthorityInputs SeedAuthority { get; }
    LoopClosurePolicyBinding PolicyBinding { get; }

    bool ValidateCanonicalState(in PolicyCanonicalStateID state);

    bool ValidateCandidateTransport(
        string candidateCanonical,
        ulong candidateDigest,
        ulong frontierRevision,
        string frontierAuthoritySHA256);

    bool TryResolveForcedCandidateAction(string candidateCanonical, ulong candidateDigest, out int action);

    bool ValidateActionRelation(
        CortexPolicySelectionCauses cause,
        int launchpadAction,
        int rawCandidateAction,
        int selectedCandidateAction,
        int executedAction);

    bool ValidateExecutionAuthority(
        CortexPolicyAuthorities authority,
        CortexPolicySelectionCauses cause,
        bool requireGrammar = false);

    LoopClosureDigest ComputeOutcomeIdentity(
        in PolicyBoundaryForkReceipt receipt,
        in PolicyBoundaryArmReceipt arm,
        int action);

    bool TryVerifyR4(
        Cortex cortex,
        in CortexPolicyDecision decision,
        out TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance);
}

/// Homeostat's policy-boundary implementation. Its semantic answers remain
/// here rather than in Cortex's transport, so a future RepositoryNative domain
/// can implement the same contract without adding a policy switch.
internal sealed class HomeostatPolicyBoundaryDomain : IPolicyBoundaryDomain
{
    internal static HomeostatPolicyBoundaryDomain Instance { get; } = new();

    private readonly LoopClosurePolicyBinding _policyBinding = new(global::Cogito.Homeostat.PolicyID, "policy:" + global::Cogito.Homeostat.PolicyID.Value);

    private HomeostatPolicyBoundaryDomain() { }

    public CortexPolicyID PolicyID => global::Cogito.Homeostat.PolicyID;
    public CortexPolicySchema Schema => global::Cogito.Homeostat.PolicySchema;
    public PolicyCanonicalStateKinds CanonicalStateKind => PolicyCanonicalStateKinds.Homeostat;
    public PolicyCanonicalScopeModes CanonicalScopeMode => PolicyCanonicalScopeModes.Enumerated;
    public PolicyCanonicalStateID[] CanonicalStates => PolicyCanonicalStates.HomeostatDomain(PolicyID);
    public ushort BoundaryFeatureID => checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality));
    public PolicyBoundaryArmTopology ArmTopology { get; } = new(
        CortexPolicyAuthorities.Grammar,
        CortexPolicyAuthorities.Launchpad,
        CortexPolicyAuthorities.Grammar,
        EmlProcessCatalogs.Full,
        EmlProcessCatalogs.NegativeLog,
        EmlRung0Modes.Armed,
        EmlRung0Modes.Disabled,
        EmlDeliberationModes.Adaptive,
        EmlDeliberationModes.Frozen,
        1024,
        "paired-homeostat-boundary-v1");
    public PolicyBoundarySeedAuthorityInputs SeedAuthority { get; } = new(
        CortexPolicyAuthorities.Grammar,
        CortexPolicyAuthorities.Grammar,
        CortexPolicySelectionCauses.GrammarCandidate,
        CortexPolicySelectionCauses.TrialOverride);
    public LoopClosurePolicyBinding PolicyBinding => _policyBinding;

    public bool ValidateCanonicalState(in PolicyCanonicalStateID state)
        => state.Policy.Equals(PolicyID)
            && state.Kind == CanonicalStateKind
            && state.Version == PolicyCanonicalStates.HomeostatVersion
            && (state.Value & ~0x3FFUL) == 0
            && (state.Value & 0xFF) <= 8;

    public bool ValidateCandidateTransport(
        string candidateCanonical,
        ulong candidateDigest,
        ulong frontierRevision,
        string frontierAuthoritySHA256)
        => string.IsNullOrEmpty(candidateCanonical)
            && candidateDigest == 0
            && frontierRevision == 0
            && string.IsNullOrEmpty(frontierAuthoritySHA256);

    public bool TryResolveForcedCandidateAction(string candidateCanonical, ulong candidateDigest, out int action)
    {
        action = -1;
        return false;
    }

    public bool ValidateActionRelation(
        CortexPolicySelectionCauses cause,
        int launchpadAction,
        int rawCandidateAction,
        int selectedCandidateAction,
        int executedAction)
        => ValidateActionRelationCore(cause, launchpadAction, rawCandidateAction, selectedCandidateAction, executedAction);

    public bool ValidateExecutionAuthority(
        CortexPolicyAuthorities authority,
        CortexPolicySelectionCauses cause,
        bool requireGrammar = false)
        => ValidateExecutionAuthorityCore(authority, cause, requireGrammar);

    internal static bool ValidateExecutionAuthorityCore(
        CortexPolicyAuthorities authority,
        CortexPolicySelectionCauses cause,
        bool requireGrammar = false)
    {
        if (!Enum.IsDefined(authority) || !Enum.IsDefined(cause)) return false;
        if (requireGrammar && (authority != CortexPolicyAuthorities.Grammar
            || cause is CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate)) return false;
        return cause switch
        {
            CortexPolicySelectionCauses.Launchpad => authority == CortexPolicyAuthorities.Launchpad,
            CortexPolicySelectionCauses.ShadowCandidate => authority == CortexPolicyAuthorities.Shadow,
            CortexPolicySelectionCauses.GrammarCandidate => authority == CortexPolicyAuthorities.Grammar,
            CortexPolicySelectionCauses.TrialOverride => authority == CortexPolicyAuthorities.Grammar,
            _ => false,
        };
    }

    internal static bool ValidateActionRelationCore(
        CortexPolicySelectionCauses cause,
        int launchpadAction,
        int rawCandidateAction,
        int selectedCandidateAction,
        int executedAction)
        => cause switch
        {
            CortexPolicySelectionCauses.Launchpad => executedAction == launchpadAction
                && rawCandidateAction < 0 && selectedCandidateAction < 0,
            CortexPolicySelectionCauses.ShadowCandidate => executedAction == launchpadAction
                && selectedCandidateAction >= 0,
            CortexPolicySelectionCauses.GrammarCandidate => executedAction == selectedCandidateAction
                && selectedCandidateAction >= 0,
            CortexPolicySelectionCauses.TrialOverride => executedAction == selectedCandidateAction
                && selectedCandidateAction >= 0 && selectedCandidateAction != rawCandidateAction,
            _ => false,
        };

    public LoopClosureDigest ComputeOutcomeIdentity(
        in PolicyBoundaryForkReceipt receipt,
        in PolicyBoundaryArmReceipt arm,
        int action)
        => ComputeOutcomeIdentityCore(in receipt, in arm, action);

    internal static LoopClosureDigest ComputeOutcomeIdentityCore(
        in PolicyBoundaryForkReceipt receipt,
        in PolicyBoundaryArmReceipt arm,
        int action)
    {
        string canonical = string.Join('|', receipt.Obligation, arm.Arm, arm.Horizon, action,
            arm.PaidCloseDelta, arm.MatchedSpend, arm.ContinuityExact ? 1 : 0,
            arm.ChildProcessCompleted ? 1 : 0, arm.BehaviorallyExecuted ? 1 : 0, arm.Diverged ? 1 : 0,
            arm.GrammarExecutionsDelta, arm.TrialAdaptationTransitions,
            arm.AdaptationEnabled ? 1 : 0, arm.ExecutionOutcome, arm.RequestCount, arm.GuardAdmittedCount,
            arm.LastRequestDecisionID.Value, arm.LastRequestStep, arm.LastRequestReadout.LaunchpadAction,
            arm.LastRequestReadout.RawCandidateAction, arm.LastRequestReadout.SelectedCandidateAction,
            arm.LastRequestReadout.ExecutedAction, arm.LastRequestReadout.Authority, arm.LastRequestReadout.GrammarRevision.Value,
            arm.LastRequestReadout.SelectionCause,
            arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest,
            arm.LastRequestReadout.ReadoutCandidateFingerprint, arm.ExecutedAuthority, arm.ExecutedSelectionCause,
            arm.ExecutedDecisionID.Value, arm.ExecutedLaunchpadAction, arm.ExecutedRawCandidateAction,
            arm.ExecutedStep, arm.ExecutedSelectedCandidateAction, arm.ExecutedAction,
            arm.ExecutedReadoutFingerprint.ToString("X16"), arm.ExecutedReadoutRevision,
            arm.ExecutedReadoutOccurrenceDigest.ToString("X16"), arm.ExecutedCandidateFingerprint.ToString("X16"),
            arm.ExecutedCanonicalState.Policy.Value, (byte)arm.ExecutedCanonicalState.Kind,
            arm.ExecutedCanonicalState.Version, arm.ExecutedCanonicalState.Value.ToString("X16"),
            arm.ExecutedDecisionEventID.Value, arm.ExecutedOutcomeEventID.Value, arm.ExecutedOutcomePayloadSHA256,
            arm.ForcedDivergenceSeed.ToString("X16"));
        return new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    public bool TryVerifyR4(
        Cortex cortex,
        in CortexPolicyDecision decision,
        out TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance)
    {
        decisionEventID = new TapeEventID(-1);
        provenance = default;
        if (!decision.Policy.Equals(PolicyID)
            || !decision.ReadoutContext.IsCanonical
            || decision.ReadoutContext.ActionCount != Schema.ActionCount
            || !ValidateCanonicalState(decision.ReadoutContext.CanonicalState)
            || !PolicyBinding.PolicyID.Equals(PolicyID)) return false;
        if (!cortex.TryCreatePolicyReadoutCustody(in decision, out decisionEventID, out provenance)
            || !provenance.Training.Policy.Equals(PolicyID)
            || !provenance.Training.CanonicalState.Policy.Equals(PolicyID))
        {
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            return false;
        }
        return true;
    }
}

/// Semantic owner of the native repository navigation policy. Repository
/// custody is mounted through this domain with repository-owned state, action,
/// and packet identities.
internal sealed class RepositoryPolicyBoundaryDomain : IPolicyBoundaryDomain
{
    internal static RepositoryPolicyBoundaryDomain Instance { get; } = new();

    private static readonly LoopClosurePolicyBinding POLICY_BINDING =
        new(RepositoryNative.Policy.ID, "policy:repository.native-navigation");

    private RepositoryPolicyBoundaryDomain() { }

    public CortexPolicyID PolicyID => RepositoryNative.Policy.ID;
    public CortexPolicySchema Schema => RepositoryNative.Policy.Schema;
    public PolicyCanonicalStateKinds CanonicalStateKind => PolicyCanonicalStateKinds.Generic;
    public PolicyCanonicalScopeModes CanonicalScopeMode => PolicyCanonicalScopeModes.Dynamic;
    public PolicyCanonicalStateID[] CanonicalStates => [];
    public ushort BoundaryFeatureID => (ushort)RepositoryNative.PolicyMetricIDs.CandidateSpecies;
    public PolicyBoundaryArmTopology ArmTopology { get; } = new(
        CortexPolicyAuthorities.Grammar,
        CortexPolicyAuthorities.Launchpad,
        CortexPolicyAuthorities.Grammar,
        EmlProcessCatalogs.Full,
        EmlProcessCatalogs.NegativeLog,
        EmlRung0Modes.Armed,
        EmlRung0Modes.Disabled,
        EmlDeliberationModes.Adaptive,
        EmlDeliberationModes.Frozen,
        1024,
        "paired-repository-native-navigation-v1");
    public PolicyBoundarySeedAuthorityInputs SeedAuthority { get; } = new(
        CortexPolicyAuthorities.Grammar,
        CortexPolicyAuthorities.Grammar,
        CortexPolicySelectionCauses.GrammarCandidate,
        CortexPolicySelectionCauses.TrialOverride);
    public LoopClosurePolicyBinding PolicyBinding => POLICY_BINDING;

    public bool ValidateCanonicalState(in PolicyCanonicalStateID state)
        => RepositoryNative.Policy.IsCanonicalState(state);

    public bool ValidateCandidateTransport(
        string candidateCanonical,
        ulong candidateDigest,
        ulong frontierRevision,
        string frontierAuthoritySHA256)
        => !string.IsNullOrWhiteSpace(candidateCanonical)
            && candidateDigest != 0
            && frontierRevision != 0
            && RepositoryLineageReceiptCodec.IsSHA(frontierAuthoritySHA256)
            && RepositoryCandidate.TryParseCanonical(candidateCanonical, out RepositoryCandidate candidate)
            && candidate.Digest.Value == candidateDigest;

    public bool TryResolveForcedCandidateAction(string candidateCanonical, ulong candidateDigest, out int action)
    {
        action = -1;
        if (!RepositoryCandidate.TryParseCanonical(candidateCanonical, out RepositoryCandidate candidate)
            || candidate.Digest.Value != candidateDigest
            || !RepositoryNative.Policy.TrySpecies((int)candidate.Species, out _))
            return false;
        action = RepositoryNative.Policy.Action(candidate.Species);
        return true;
    }

    public bool ValidateActionRelation(
        CortexPolicySelectionCauses cause,
        int launchpadAction,
        int rawCandidateAction,
        int selectedCandidateAction,
        int executedAction)
    {
        bool launchpadValid = RepositoryNative.Policy.TrySpecies(launchpadAction, out _);
        bool rawValid = rawCandidateAction < 0 || RepositoryNative.Policy.TrySpecies(rawCandidateAction, out _);
        bool selectedValid = selectedCandidateAction < 0 || RepositoryNative.Policy.TrySpecies(selectedCandidateAction, out _);
        bool executedValid = RepositoryNative.Policy.TrySpecies(executedAction, out _);
        return cause switch
        {
            CortexPolicySelectionCauses.Launchpad => launchpadValid && executedValid
                && executedAction == launchpadAction && rawCandidateAction < 0 && selectedCandidateAction < 0,
            CortexPolicySelectionCauses.ShadowCandidate => launchpadValid && selectedValid && executedValid
                && selectedCandidateAction >= 0 && executedAction == launchpadAction,
            CortexPolicySelectionCauses.GrammarCandidate => selectedValid && executedValid
                && selectedCandidateAction >= 0 && executedAction == selectedCandidateAction,
            CortexPolicySelectionCauses.TrialOverride => rawValid && selectedValid && executedValid
                && selectedCandidateAction >= 0
                && executedAction == selectedCandidateAction && selectedCandidateAction != rawCandidateAction,
            _ => false,
        };
    }

    public bool ValidateExecutionAuthority(
        CortexPolicyAuthorities authority,
        CortexPolicySelectionCauses cause,
        bool requireGrammar = false)
    {
        if (!Enum.IsDefined(authority) || !Enum.IsDefined(cause)) return false;
        if (requireGrammar && (authority != CortexPolicyAuthorities.Grammar
            || cause is CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate)) return false;
        return cause switch
        {
            CortexPolicySelectionCauses.Launchpad => authority == CortexPolicyAuthorities.Launchpad,
            CortexPolicySelectionCauses.ShadowCandidate => authority == CortexPolicyAuthorities.Shadow,
            CortexPolicySelectionCauses.GrammarCandidate => authority == CortexPolicyAuthorities.Grammar,
            CortexPolicySelectionCauses.TrialOverride => authority == CortexPolicyAuthorities.Grammar,
            _ => false,
        };
    }

    public LoopClosureDigest ComputeOutcomeIdentity(
        in PolicyBoundaryForkReceipt receipt,
        in PolicyBoundaryArmReceipt arm,
        int action)
    {
        string canonical = string.Join('|', receipt.Obligation, arm.Arm, arm.Horizon, action,
            arm.PaidCloseDelta, arm.MatchedSpend, arm.ContinuityExact ? 1 : 0,
            arm.ChildProcessCompleted ? 1 : 0, arm.BehaviorallyExecuted ? 1 : 0, arm.Diverged ? 1 : 0,
            arm.GrammarExecutionsDelta, arm.TrialAdaptationTransitions,
            arm.AdaptationEnabled ? 1 : 0, arm.ExecutionOutcome, arm.RequestCount, arm.GuardAdmittedCount,
            arm.LastRequestDecisionID.Value, arm.LastRequestStep, arm.LastRequestReadout.LaunchpadAction,
            arm.LastRequestReadout.RawCandidateAction, arm.LastRequestReadout.SelectedCandidateAction,
            arm.LastRequestReadout.ExecutedAction, arm.LastRequestReadout.Authority, arm.LastRequestReadout.GrammarRevision.Value,
            arm.LastRequestReadout.SelectionCause,
            arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest,
            arm.LastRequestReadout.ReadoutCandidateFingerprint, arm.ExecutedAuthority, arm.ExecutedSelectionCause,
            arm.ExecutedDecisionID.Value, arm.ExecutedLaunchpadAction, arm.ExecutedRawCandidateAction,
            arm.ExecutedStep, arm.ExecutedSelectedCandidateAction, arm.ExecutedAction,
            arm.ExecutedReadoutFingerprint.ToString("X16"), arm.ExecutedReadoutRevision,
            arm.ExecutedReadoutOccurrenceDigest.ToString("X16"), arm.ExecutedCandidateFingerprint.ToString("X16"),
            arm.ExecutedCanonicalState.Policy.Value, (byte)arm.ExecutedCanonicalState.Kind,
            arm.ExecutedCanonicalState.Version, arm.ExecutedCanonicalState.Value.ToString("X16"),
            arm.ExecutedDecisionEventID.Value, arm.ExecutedOutcomeEventID.Value, arm.ExecutedOutcomePayloadSHA256,
            arm.ForcedDivergenceSeed.ToString("X16"));
        return new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    public bool TryVerifyR4(
        Cortex cortex,
        in CortexPolicyDecision decision,
        out TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance)
    {
        decisionEventID = new TapeEventID(-1);
        provenance = default;
        if (!decision.Policy.Equals(PolicyID)
            || !decision.ReadoutContext.IsCanonical
            || decision.ReadoutContext.ActionCount != Schema.ActionCount
            || !ValidateCanonicalState(decision.ReadoutContext.CanonicalState)
            || !PolicyBinding.PolicyID.Equals(PolicyID)) return false;
        if (!RepositoryNative.TryVerifyReadout(cortex, in decision, out decisionEventID, out provenance)
            || !provenance.Training.Policy.Equals(PolicyID)
            || !ValidateCanonicalState(provenance.Training.CanonicalState))
        {
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            return false;
        }
        return true;
    }
}
