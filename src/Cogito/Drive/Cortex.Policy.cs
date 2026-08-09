namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;

/// Stable identity of one policy species. The value is open so mounted curricula can add policies without extending
/// a Cortex enum; equality remains exact and allocation-free after registration.
public readonly struct CortexPolicyID(string value) : IEquatable<CortexPolicyID>, IComparable<CortexPolicyID>
{
    public string Value { get; } = string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("policy identity is required", nameof(value))
        : value;

    public int CompareTo(CortexPolicyID other) => string.CompareOrdinal(Value, other.Value);
    public bool Equals(CortexPolicyID other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? other) => other is CortexPolicyID policy && Equals(policy);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(CortexPolicyID left, CortexPolicyID right) => left.Equals(right);
    public static bool operator !=(CortexPolicyID left, CortexPolicyID right) => !left.Equals(right);

    public static CortexPolicyID Parse(string value) => new(value);
}

public readonly record struct CortexPolicyDecisionID(ulong Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct CortexPolicyQuotaDecisionID(ulong Value)
{
    public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);
}

/// The install revision identity of a policy readout.  It is deliberately not
/// interchangeable with the semantic candidate identity carried beside it.
public readonly record struct CortexPolicyReadoutFingerprint(ulong Value)
{
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);
}

/// The action/state identity proven by a policy readout.  A candidate may
/// survive a install revision revision only after that candidate identity is
/// re-proven; a readout fingerprint alone is never sufficient.
public readonly record struct CortexPolicyCandidateFingerprint(ulong Value)
{
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);
}

/// The complete identity a fork arm must carry when it asks the policy to
/// execute.  These three values are deliberately inseparable: the active
/// canonical program, its raw action/state candidate, and the install revision
/// revision are distinct authorities and must all match the loaded checkpoint.
public readonly record struct CortexPolicyTrialAuthorityIdentity(
    CortexPolicyReadoutFingerprint ActiveProgramFingerprint,
    CortexPolicyCandidateFingerprint CandidateFingerprint,
    GrammarRevisionID CandidateRevision)
{
    public PolicyCanonicalStateID CanonicalState { get; init; }
    public bool HasCanonicalState => CanonicalState.Version != 0;
    public bool IsValid => ActiveProgramFingerprint.IsValid
        && CandidateFingerprint.IsValid
        && CandidateRevision != GrammarRevisionID.Zero;

    public static CortexPolicyTrialAuthorityIdentity FromReadout(in CortexPolicyReadoutReceipt readout)
        => new(new(readout.Fingerprint), new(readout.CandidateFingerprint), readout.Revision)
        {
            CanonicalState = readout.CanonicalState,
        };

    public override string ToString()
        => $"program={ActiveProgramFingerprint} candidate={CandidateFingerprint} revision={CandidateRevision.Value}";
}

public enum CortexPolicyModes : byte
{
    Off,
    Shadow,
    Autonomic,
}

public enum CortexPolicyAuthorities : byte
{
    Launchpad,
    Shadow,
    Grammar,
}

public enum CortexPolicySelectionCauses : byte
{
    Launchpad,
    ShadowCandidate,
    GrammarCandidate,
    TrialOverride,
    RollbackDrill,
}

public enum CortexPolicyPurposes : byte
{
    Emulation,
    Adaptation,
}

public enum CortexPolicyAdmissionKinds : byte
{
    ExactShadow,
    Verified,
}

public enum CortexPolicyQuotaDecisions : byte
{
    Paid,
    Reused,
    Denied,
}

public enum CortexPolicyTrialCandidateStates : byte
{
    Mismatch,
    Pending,
    Active,
}

public enum CortexPolicyTrialDenialReasons : byte
{
    None,
    CandidateMismatch,
    CandidatePending,
    MaturityWindow,
    InsufficientFuel,
    AllocationUnavailable,
    ProgramMismatch,
    CandidateRevisionMismatch,
    CanonicalScopeMissing,
}

public enum CortexPolicyTrialExecutionOutcomes : byte
{
    NotAttempted = 0,
    GuardDenied = 1,
    ConfiguredCauseExecuted = 2,
}

public enum CortexPolicyPendingForcedTrialRearmOutcomes : byte
{
    Denied,
    Allowed,
}

public enum CortexPolicyPendingForcedTrialRearmDenialSpecies : byte
{
    None,
    NoPendingIntent,
    ForcedSeedStillPresent,
    AlreadyArmed,
    IntentNotBound,
    ReadoutNotReady,
    CurrentCandidateMismatch,
    CurrentRevisionMismatch,
    CanonicalStateMissing,
    CanonicalScopeMissing,
    SuccessorCanonicalStateMismatch,
    SuccessorReadoutMismatch,
    SuccessorOccurrenceMismatch,
    BoundaryArmMismatch,
    BoundaryFeatureMismatch,
    BoundaryObligationMismatch,
    AuditOnlyDigestInvalid,
    SourceAuditOnlyMismatch,
    AuditOnlySidecarMissing,
    AuditOnlySidecarMismatch,
    RootQuotaMissing,
    RootQuotaMismatch,
    SourceCorroborationMissing,
    SourceCorroborationMismatch,
}

public readonly record struct CortexPolicyPendingForcedTrialRearmEvaluation(
    CortexPolicyPendingForcedTrialRearmOutcomes Outcome,
    CortexPolicyPendingForcedTrialRearmDenialSpecies DenialSpecies,
    CortexPolicyID Policy,
    ulong QuotaID,
    CortexPolicyQuotaDecisions SourceQuotaDecision,
    ulong SourceDecisionID,
    long SourceDecisionEventID,
    long SourceCorroborationEventID,
    ulong SourceOccurrenceDigest,
    ulong SourceCandidateFingerprint,
    ulong SourceQuotaCandidateFingerprint,
    ulong SourceReadoutFingerprint,
    GrammarRevisionID SourceCandidateRevision,
    PolicyCanonicalStateID SourceCanonicalState,
    ulong ReadoutFingerprint,
    ulong CandidateFingerprint,
    GrammarRevisionID CandidateRevision,
    ulong OccurrenceDigest,
    PolicyCanonicalStateID CanonicalState,
    string ObligationID,
    byte Arm,
    ushort FeatureID,
    string SourceRunID,
    string AuditOnlyDigest,
    bool IntentBound)
{
    public bool Allowed => Outcome == CortexPolicyPendingForcedTrialRearmOutcomes.Allowed;

    internal static CortexPolicyPendingForcedTrialRearmEvaluation Denied(
        CortexPolicyPendingForcedTrialRearmDenialSpecies species,
        in CortexPolicyPendingForcedTrialIntent pending,
        ulong candidateFingerprint,
        GrammarRevisionID candidateRevision)
        => new(CortexPolicyPendingForcedTrialRearmOutcomes.Denied, species,
            pending.Policy, pending.QuotaID, pending.SourceQuotaDecision,
            pending.SourceDecisionID, pending.SourceDecisionEventID, pending.SourceCorroborationEventID,
            pending.SourceOccurrenceDigest, pending.SourceCandidateFingerprint,
            pending.SourceQuotaCandidateFingerprint, pending.SourceReadoutFingerprint,
            pending.SourceCandidateRevision, pending.SourceCanonicalState,
            pending.ReadoutFingerprint, candidateFingerprint, candidateRevision,
            pending.SuccessorOccurrenceDigest, pending.CanonicalState, pending.ObligationID,
            pending.Arm, pending.FeatureID, pending.SourceRunID, pending.AuditOnlyDigest,
            pending.IsBound);

    internal static CortexPolicyPendingForcedTrialRearmEvaluation Allow(
        in CortexPolicyPendingForcedTrialIntent pending,
        ulong candidateFingerprint,
        GrammarRevisionID candidateRevision)
        => new(CortexPolicyPendingForcedTrialRearmOutcomes.Allowed,
            CortexPolicyPendingForcedTrialRearmDenialSpecies.None,
            pending.Policy, pending.QuotaID, pending.SourceQuotaDecision,
            pending.SourceDecisionID, pending.SourceDecisionEventID, pending.SourceCorroborationEventID,
            pending.SourceOccurrenceDigest, pending.SourceCandidateFingerprint,
            pending.SourceQuotaCandidateFingerprint, pending.SourceReadoutFingerprint,
            pending.SourceCandidateRevision, pending.SourceCanonicalState,
            pending.ReadoutFingerprint, candidateFingerprint, candidateRevision,
            pending.SuccessorOccurrenceDigest, pending.CanonicalState, pending.ObligationID,
            pending.Arm, pending.FeatureID, pending.SourceRunID, pending.AuditOnlyDigest,
            pending.IsBound);
}

internal readonly record struct CortexPolicyPendingForcedTrialIntent(
    CortexPolicyID Policy,
    ulong QuotaID,
    CortexPolicyQuotaDecisions SourceQuotaDecision,
    ulong ForcedDivergenceSeed,
    ulong SourceDecisionID,
    long SourceDecisionEventID,
    long SourceCorroborationEventID,
    ulong SourceOccurrenceDigest,
    ulong SourceCandidateFingerprint,
    ulong SourceQuotaCandidateFingerprint,
    ulong SourceReadoutFingerprint,
    global::Cogito.Grammar.GrammarRevisionID SourceCandidateRevision,
    PolicyCanonicalStateID SourceCanonicalState,
    ulong ReadoutFingerprint,
    ulong CandidateFingerprint,
    global::Cogito.Grammar.GrammarRevisionID CandidateRevision,
    ulong SuccessorOccurrenceDigest,
    PolicyCanonicalStateID CanonicalState,
    string ObligationID,
    byte Arm,
    ushort FeatureID,
    string SourceRunID,
    string AuditOnlyDigest)
{
    internal bool HasSeed => ForcedDivergenceSeed != 0;
    internal bool IsBound => QuotaID != 0 && SourceQuotaDecision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused) && SourceDecisionID != 0 && SourceDecisionEventID > 0
        && SourceCorroborationEventID > 0 && SourceOccurrenceDigest != 0 && SourceCandidateFingerprint != 0
        && SourceQuotaCandidateFingerprint != 0 && SourceReadoutFingerprint != 0
        && SourceCandidateRevision != global::Cogito.Grammar.GrammarRevisionID.Zero
        && SourceCanonicalState.Version != 0
        && ReadoutFingerprint != 0 && CandidateFingerprint != 0
        && CandidateRevision != global::Cogito.Grammar.GrammarRevisionID.Zero
        && SuccessorOccurrenceDigest != 0
        && CanonicalState.Version != 0 && !string.IsNullOrWhiteSpace(ObligationID)
        && !string.IsNullOrWhiteSpace(SourceRunID)
        && !string.IsNullOrWhiteSpace(AuditOnlyDigest);
}

internal enum CortexPolicyTrialDemotionReasons : byte
{
    None,
    ReadoutRevisionChanged,
    CandidateChanged,
    ShadowDisagreement,
    TrialQuotaExhausted,
    InvariantFailure,
    RollbackDrill,
    ConfiguredAuthority,
}

public enum CortexPolicyVerifierOutcomes : byte
{
    NotRecorded,
    Passed,
    Failed,
    ReadoutCompleted,
}

public sealed class CortexPolicyOverride
{
    public required CortexPolicyID Policy { get; init; }
    public required CortexPolicyModes Mode { get; init; }
}

public sealed class CortexPolicyLearningConfig
{
    public CortexPolicyModes DefaultMode { get; init; } = CortexPolicyModes.Autonomic;
    public CortexPolicyAuthorities AuthorityCeiling { get; init; } = CortexPolicyAuthorities.Grammar;
    public List<CortexPolicyOverride> Overrides { get; init; } = [];
    public int ShadowDecisions { get; init; } = 8;
    public int ProposalInterval { get; init; } = 32;
    public int ReadoutDeliberationQuota { get; init; }
    public int[] TrialHorizons { get; init; } = [16, 64, 256];
    public CortexPolicyTrialAllocationConfig? TrialAllocation { get; init; }

    internal CortexPolicyModes ResolveMode(CortexPolicyID policy)
    {
        CortexPolicyModes mode = DefaultMode;
        for (int i = 0; i < Overrides.Count; i++)
        {
            CortexPolicyOverride candidate = Overrides[i];
            if (candidate.Policy.Equals(policy)) mode = candidate.Mode;
        }
        return mode;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(DefaultMode)) throw new ArgumentOutOfRangeException(nameof(DefaultMode));
        if (!Enum.IsDefined(AuthorityCeiling)) throw new ArgumentOutOfRangeException(nameof(AuthorityCeiling));
        if (ShadowDecisions <= 0) throw new ArgumentOutOfRangeException(nameof(ShadowDecisions));
        if (ProposalInterval <= 0) throw new ArgumentOutOfRangeException(nameof(ProposalInterval));
        if (ReadoutDeliberationQuota < 0) throw new ArgumentOutOfRangeException(nameof(ReadoutDeliberationQuota));
        TrialAllocation?.Validate();
        if (TrialHorizons.Length == 0) throw new ArgumentException("policy trials require at least one horizon", nameof(TrialHorizons));
        int previous = 0;
        for (int i = 0; i < TrialHorizons.Length; i++)
        {
            int horizon = TrialHorizons[i];
            if (horizon <= previous) throw new ArgumentException("policy trial horizons must increase strictly", nameof(TrialHorizons));
            previous = horizon;
        }
        HashSet<CortexPolicyID> policies = new();
        for (int i = 0; i < Overrides.Count; i++)
        {
            CortexPolicyOverride candidate = Overrides[i];
            if (!Enum.IsDefined(candidate.Mode)) throw new ArgumentOutOfRangeException(nameof(Overrides));
            if (!policies.Add(candidate.Policy)) throw new ArgumentException($"policy '{candidate.Policy}' has more than one override", nameof(Overrides));
        }
    }
}

public sealed class CortexPolicyTrialAllocationConfig
{
    public long ArmSteps { get; init; }
    public CortexPolicyAuthorities Authority { get; init; } = CortexPolicyAuthorities.Grammar;
    public string Identity { get; init; } = "";

    internal void Validate()
    {
        if (ArmSteps < 0) throw new ArgumentOutOfRangeException(nameof(ArmSteps));
        if (!Enum.IsDefined(Authority)) throw new ArgumentOutOfRangeException(nameof(Authority));
        if (ArmSteps > 0 && Authority != CortexPolicyAuthorities.Grammar)
            throw new ArgumentException("policy trial allocations must authorize the Grammar LIVE rail", nameof(Authority));
        if (ArmSteps == 0 && Identity.Length != 0)
            throw new ArgumentException("an absent policy trial allocation cannot carry an identity", nameof(Identity));
        if (ArmSteps > 0 && string.IsNullOrWhiteSpace(Identity))
            throw new ArgumentException("a policy trial allocation requires an identity", nameof(Identity));
    }
}

public readonly record struct CortexPolicyTrialAllocation(
    CortexPolicyID Policy,
    CortexPolicyAuthorities Authority,
    long ArmSteps,
    string Identity,
    string Digest)
{
    public static CortexPolicyTrialAllocation Bind(CortexPolicyID policy, CortexPolicyTrialAllocationConfig config)
    {
        config.Validate();
        if (config.ArmSteps == 0) return default;
        string digest = CortexPolicyTrialAllocation.ComputeDigest(policy, config.Authority, config.ArmSteps, config.Identity);
        return new(policy, config.Authority, config.ArmSteps, config.Identity, digest);
    }

    public bool IsPresent => ArmSteps > 0 && !string.IsNullOrWhiteSpace(Identity) && Digest == ComputeDigest(Policy, Authority, ArmSteps, Identity);

    public static string ComputeDigest(CortexPolicyID policy, CortexPolicyAuthorities authority, long armSteps, string identity)
    {
        string material = string.Join('|', policy.Value, authority, armSteps, identity, "policy-trial-allocation-v1");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

public readonly struct CortexPolicySchema(
    CortexPolicyID policy,
    int featureCount,
    int actionCount,
    int outcomeCount,
    CortexPolicyModes authorityCeiling = CortexPolicyModes.Autonomic,
    CortexPolicyAdmissionKinds admission = CortexPolicyAdmissionKinds.ExactShadow)
{
    public CortexPolicyID Policy { get; } = policy;
    public int FeatureCount { get; } = featureCount > 0 ? featureCount : throw new ArgumentOutOfRangeException(nameof(featureCount));
    public int ActionCount { get; } = actionCount > 1 ? actionCount : throw new ArgumentOutOfRangeException(nameof(actionCount));
    public int OutcomeCount { get; } = outcomeCount > 0 ? outcomeCount : throw new ArgumentOutOfRangeException(nameof(outcomeCount));
    public CortexPolicyModes ModeCeiling { get; } = Enum.IsDefined(authorityCeiling)
        ? authorityCeiling
        : throw new ArgumentOutOfRangeException(nameof(authorityCeiling));
    public CortexPolicyAdmissionKinds Admission { get; } = Enum.IsDefined(admission)
        ? admission
        : throw new ArgumentOutOfRangeException(nameof(admission));
}

public readonly record struct CortexPolicyReadoutReceipt(
    GrammarRevisionID Revision,
    ulong Fingerprint,
    int CachedContexts,
    int Comparisons,
    int Agreements,
    int Misses,
    ulong ReadoutCandidateOccurrenceDigest = 0,
    ulong ReadoutCandidateFingerprint = 0,
    PolicyCanonicalCoverageReceipt CanonicalCoverage = default,
    PolicyCanonicalStateID CanonicalState = default)
{
    public ulong CandidateOccurrenceDigest => ReadoutCandidateOccurrenceDigest;
    public ulong CandidateFingerprint => ReadoutCandidateFingerprint;
    public bool IsExact => Comparisons > 0 && Comparisons == Agreements && Misses == 0;
}

public readonly record struct CortexPolicyDecisionReadout(
    int LaunchpadAction,
    int RawCandidateAction,
    int SelectedCandidateAction,
    int ExecutedAction,
    CortexPolicyAuthorities Authority,
    global::Cogito.Grammar.GrammarRevisionID GrammarRevision,
    CortexPolicySelectionCauses SelectionCause,
    ulong ReadoutCandidateOccurrenceDigest = 0,
    ulong ReadoutCandidateFingerprint = 0,
    ulong ReadoutFingerprint = 0)
{
    public ulong CandidateOccurrenceDigest => ReadoutCandidateOccurrenceDigest;
    public ulong CandidateFingerprint => ReadoutCandidateFingerprint;
    public bool RollbackDrill => SelectionCause == CortexPolicySelectionCauses.RollbackDrill;

    internal static CortexPolicyDecisionReadout NoExecution
        => new(-1, -1, -1, -1, CortexPolicyAuthorities.Launchpad,
            global::Cogito.Grammar.GrammarRevisionID.Zero, CortexPolicySelectionCauses.Launchpad);

    internal void Validate(int actionCount)
    {
        if (actionCount <= 1) throw new ArgumentOutOfRangeException(nameof(actionCount));
        if (LaunchpadAction < 0 || LaunchpadAction >= actionCount || ExecutedAction < 0 || ExecutedAction >= actionCount)
            throw new InvalidDataException("policy readout launchpad/executed action is outside the policy action count");
        if (RawCandidateAction < -1 || RawCandidateAction >= actionCount
            || SelectedCandidateAction < -1 || SelectedCandidateAction >= actionCount)
            throw new InvalidDataException("policy readout candidate action is outside the policy action count");
        if ((RawCandidateAction == -1) != (SelectedCandidateAction == -1))
            throw new InvalidDataException("policy readout candidate actions must be both absent or both present");
        bool candidatePresent = RawCandidateAction >= 0 && SelectedCandidateAction >= 0;
        if (candidatePresent && ReadoutCandidateFingerprint == 0)
            throw new InvalidDataException("policy readout candidate fingerprint is required when a candidate is present");
        if (!candidatePresent && (ReadoutCandidateFingerprint != 0 || ReadoutCandidateOccurrenceDigest != 0))
            throw new InvalidDataException("policy readout candidate identity is only valid with a candidate");
        switch (SelectionCause)
        {
            case CortexPolicySelectionCauses.Launchpad:
                if (candidatePresent || LaunchpadAction != ExecutedAction || Authority != CortexPolicyAuthorities.Launchpad)
                    throw new InvalidDataException("launchpad readout carries candidate or authority state");
                break;
            case CortexPolicySelectionCauses.ShadowCandidate:
                if (!candidatePresent || Authority != CortexPolicyAuthorities.Shadow || ExecutedAction != LaunchpadAction)
                    throw new InvalidDataException("shadow readout does not preserve candidate and launchpad selection");
                break;
            case CortexPolicySelectionCauses.GrammarCandidate:
                if (!candidatePresent || Authority != CortexPolicyAuthorities.Grammar || SelectedCandidateAction != ExecutedAction)
                    throw new InvalidDataException("grammar readout does not preserve selected candidate execution");
                break;
            case CortexPolicySelectionCauses.TrialOverride:
                if (!candidatePresent || Authority != CortexPolicyAuthorities.Grammar
                    || SelectedCandidateAction == RawCandidateAction
                    || SelectedCandidateAction != ExecutedAction)
                    throw new InvalidDataException("trial readout does not prove a forced candidate divergence");
                break;
            case CortexPolicySelectionCauses.RollbackDrill:
                if (!candidatePresent || Authority != CortexPolicyAuthorities.Grammar || SelectedCandidateAction != ExecutedAction
                    || SelectedCandidateAction == RawCandidateAction || SelectedCandidateAction == LaunchpadAction)
                    throw new InvalidDataException("rollback drill readout does not preserve candidate and drill action");
                break;
            default:
                throw new InvalidDataException("unknown policy selection cause");
        }
    }
}

public readonly struct CortexPolicyDecision
{
    public CortexPolicyDecisionID DecisionID { get; }
    public CortexPolicyID Policy { get; }
    public CortexPolicyDecisionReadout Readout { get; }
    internal GrammarPolicyContextKey ReadoutContext { get; }
    public int LaunchpadAction => Readout.LaunchpadAction;
    public int RawCandidateAction => Readout.RawCandidateAction;
    public int SelectedCandidateAction => Readout.SelectedCandidateAction;
    public int Action => Readout.ExecutedAction;
    public CortexPolicyAuthorities Authority => Readout.Authority;
    public global::Cogito.Grammar.GrammarRevisionID GrammarRevision => Readout.GrammarRevision;
    public CortexPolicyReadoutFingerprint ReadoutIdentity
        => new(Readout.ReadoutFingerprint != 0
            ? Readout.ReadoutFingerprint
            : GrammarPolicyReadout.ComputeFingerprint(Readout.GrammarRevision, Policy));
    public CortexPolicySelectionCauses SelectionCause => Readout.SelectionCause;
    public bool RollbackDrill => Readout.RollbackDrill;
    public ulong ReadoutCandidateFingerprint => Readout.CandidateFingerprint;
    public ulong ReadoutCandidateOccurrenceDigest => Readout.CandidateOccurrenceDigest;

    public CortexPolicyDecision(
        CortexPolicyDecisionID decisionID,
        CortexPolicyID policy,
        CortexPolicyDecisionReadout readout)
        : this(decisionID, policy, readout, default)
    {
    }

    internal CortexPolicyDecision(
        CortexPolicyDecisionID decisionID,
        CortexPolicyID policy,
        CortexPolicyDecisionReadout readout,
        in GrammarPolicyContextKey readoutContext)
    {
        DecisionID = decisionID;
        Policy = policy;
        Readout = readout;
        ReadoutContext = readoutContext;
    }

    public CortexPolicyDecision(
        CortexPolicyDecisionID decisionID,
        CortexPolicyID policy,
        int action,
        CortexPolicyAuthorities authority,
        global::Cogito.Grammar.GrammarRevisionID grammarRevision,
        bool rollbackDrill = false)
        : this(
            decisionID,
            policy,
            new CortexPolicyDecisionReadout(
                action,
                -1,
                -1,
                action,
                authority,
                grammarRevision,
                rollbackDrill ? CortexPolicySelectionCauses.RollbackDrill : CortexPolicySelectionCauses.Launchpad))
    {
    }
}

internal readonly record struct CortexPolicyRuntimeReceipt(
    CortexPolicyAuthorities Authority,
    int CachedContexts,
    int ShadowComparisons,
    int ShadowAgreements,
    ulong Decisions,
    ulong Outcomes,
    ulong[] ActionExecutions,
    long ConservedCost,
    int ActionReversals,
    ulong GrammarExecutions,
    ulong GrammarOutcomes,
    ulong PaidGrammarOutcomes,
    ulong DivergentGrammarExecutions,
    int Readmissions,
    bool RollbackDrillPending,
    bool RollbackDrillCompleted,
    int LastGrammarLaunchpadAction,
    int LastGrammarAction,
    double[] LastGrammarFeatures,
    ulong TrialAdaptationTransitions,
    bool TrialFrozen,
    bool AdaptationEnabled);

public readonly record struct CortexPolicyTrialQuotaDecision(
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicyID Policy,
    ulong CandidateFingerprint,
    int QuotaStep,
    int RequestedHorizonSteps,
    int ArmCount,
    long PlannedArmSteps,
    long HeldArmSteps,
    CortexPolicyQuotaDecisions Decision,
    long UsedSteps,
    long RemainingQuota)
{
    public PolicyCanonicalStateID CanonicalState { get; init; }
    public bool HasCanonicalState => CanonicalState.Version != 0;
    public ulong ReadoutFingerprint { get; init; }
    public CortexPolicyReadoutFingerprint ReadoutIdentity => new(ReadoutFingerprint);
    public CortexPolicyCandidateFingerprint CandidateIdentity => new(CandidateFingerprint);
    public CortexPolicyTrialCandidateStates CandidateState { get; init; } = CortexPolicyTrialCandidateStates.Active;
    public CortexPolicyTrialDenialReasons DenialReason { get; init; } = CortexPolicyTrialDenialReasons.None;
    public int CandidateOriginStep { get; init; } = -1;
    public int CandidateCurrentStep { get; init; } = -1;
    public int CandidateRequiredStep { get; init; } = -1;
    public GrammarRevisionID CandidateRevision { get; init; }
    public string AllocationIdentity { get; init; } = "";
    public string AllocationDigest { get; init; } = "";
    public long AllocationArmSteps { get; init; }
    public string SeedAuditOnlyDigest { get; init; } = "";
}

public readonly record struct CortexPolicyReadoutQuotaDecision(
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicyID Policy,
    ulong CandidateFingerprint,
    GrammarRevisionID GrammarRevision,
    ulong ContextDigest,
    int ContextBytes,
    int DeliberationDepth,
    int QuotaStep,
    long PlannedUnits,
    long HeldUnits,
    CortexPolicyQuotaDecisions Decision,
    long UsedUnits,
    long RemainingQuota,
    long AllocationSequence = 0,
    string RosterDigest = "",
    long AvailableBefore = 0,
    long AvailableAfter = 0);

// Codec-owned byte receipts for the policy readout corruption fixture.  The serializer records
// these offsets while emitting the typed fields; consumers must never rediscover them from bytes.
internal readonly record struct CortexPolicyReadoutQuotaCheckpointRow(
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    long RowOffset,
    long RowLength,
    long DecisionOffset);

internal readonly record struct CortexPolicyReadoutCompletionCheckpointRow(
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    long RowOffset,
    long RowLength);

internal readonly record struct CortexPolicyReadoutCheckpointLayout(
    long UsedUnitsOffset,
    long QuotaCountOffset,
    long CompletionCountOffset,
    CortexPolicyReadoutQuotaCheckpointRow[] QuotaRows,
    CortexPolicyReadoutCompletionCheckpointRow[] CompletionRows);

public readonly record struct CortexPolicyTrialCompletion(
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    long ActualExecutedArmSteps,
    long ReclaimedOrUnused,
    long? EvaluatorWorkUnits,
    CortexPolicyVerifierOutcomes VerifierOutcome,
    long? WallMilliseconds);

internal readonly record struct CortexPolicyDecisionPacket(
    CortexPolicyDecisionID DecisionID,
    CortexPolicyDecisionReadout Readout,
    int ActionCount,
    MetricSample[] Features);

/// The typed payload carried by one ordinary POLICY-OUTCOME event. The event
/// identity and payload digest are sealed separately by the policy-boundary
/// rail; this packet owns only the Homeostat sensor row itself.
internal readonly record struct CortexPolicyOutcomePacket(
    CortexPolicyDecisionID DecisionID,
    MetricSample[] Outcomes,
    bool InvariantClean,
    long ConservedCost)
{
    internal void Validate(CortexPolicySchema schema)
    {
        if (DecisionID.Value == 0 || Outcomes is null || Outcomes.Length != schema.OutcomeCount
            || ConservedCost < 0)
            throw new InvalidDataException("policy outcome packet identity or schema is malformed");
        for (int index = 1; index < Outcomes.Length; index++)
            if (Outcomes[index - 1].MetricID.CompareTo(Outcomes[index].MetricID) >= 0)
                throw new InvalidDataException("policy outcome packet metrics are not strictly ordered");
    }
}

internal readonly record struct CortexPolicyOutcomeEvidence(
    TapeEventID EventID,
    int Step,
    string PayloadSHA256,
    CortexPolicyOutcomePacket Packet)
{
    internal void Validate(CortexPolicySchema schema)
    {
        if (EventID.Value <= 0 || Step < 0 || PayloadSHA256.Length != 64
            || PayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("policy outcome evidence identity is malformed");
        Packet.Validate(schema);
    }
}

/// The in-run source receipt that joins an exact active readout to the ordinary
/// decision which caused the organism to reach this boundary.  Ordinary launchpad
/// decision packets deliberately carry no candidate identity; this corroboration carries
/// the separate active tuple without changing that packet contract.
internal readonly record struct CortexPolicyBoundarySourceCorroboration(
    CortexPolicyID Policy,
    CortexPolicyDecisionID SourceDecisionID,
    TapeEventID SourceDecisionEventID,
    CortexPolicyAuthorities SourceAuthority,
    CortexPolicySelectionCauses SourceSelectionCause,
    GrammarRevisionID ReadoutRevision,
    ulong ReadoutFingerprint,
    ulong CandidateFingerprint,
    ulong OccurrenceDigest,
    string CorroborationDigest,
    int CachedContexts = 0,
    int Comparisons = 0,
    int Agreements = 0,
    int Misses = 0)
{
    internal PolicyCanonicalStateID CanonicalState { get; init; }

    internal string ComputeDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            Policy.Value, SourceDecisionID.Value, SourceDecisionEventID.Value,
            (byte)SourceAuthority, (byte)SourceSelectionCause, ReadoutRevision.Value,
            ReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            OccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture),
            CachedContexts, Comparisons, Agreements, Misses,
            CanonicalState.Policy.Value, (byte)CanonicalState.Kind, CanonicalState.Version,
            CanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture),
            "policy-boundary-source-v3"))));

    private string ComputeLegacyDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            Policy.Value, SourceDecisionID.Value, SourceDecisionEventID.Value,
            (byte)SourceAuthority, (byte)SourceSelectionCause, ReadoutRevision.Value,
            ReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            OccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture),
            CanonicalState.Policy.Value, (byte)CanonicalState.Kind, CanonicalState.Version,
            CanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture),
            "policy-boundary-source-v2"))));

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Policy.Value) || SourceDecisionID.Value == 0
            || SourceDecisionEventID.Value <= 0 || !Enum.IsDefined(SourceAuthority)
            || !Enum.IsDefined(SourceSelectionCause) || ReadoutRevision == GrammarRevisionID.Zero
            || ReadoutFingerprint == 0 || CandidateFingerprint == 0 || OccurrenceDigest == 0
            || CanonicalState.Version == 0 || !CanonicalState.Policy.Equals(Policy)
            || CachedContexts < 0 || Comparisons < 0 || Agreements < 0 || Misses < 0
            || Comparisons == 0 && (Agreements != 0 || Misses != 0)
            || Comparisons > 0 && (Agreements > Comparisons || Misses > Comparisons)
            || CorroborationDigest.Length != 64
            || !string.Equals(CorroborationDigest, ComputeDigest(), StringComparison.Ordinal)
                && !(CachedContexts == 0 && Comparisons == 0 && Agreements == 0 && Misses == 0
                    && string.Equals(CorroborationDigest, ComputeLegacyDigest(), StringComparison.Ordinal)))
            throw new InvalidDataException("policy boundary source corroboration is incomplete or unauthenticated");
    }

    internal bool Matches(in CortexPolicyReadoutReceipt readout)
        => ReadoutRevision == readout.Revision && ReadoutFingerprint == readout.Fingerprint
            && CandidateFingerprint == readout.ReadoutCandidateFingerprint
            && OccurrenceDigest == readout.ReadoutCandidateOccurrenceDigest
            && CachedContexts == readout.CachedContexts && Comparisons == readout.Comparisons
            && Agreements == readout.Agreements && Misses == readout.Misses
            && CanonicalState == readout.CanonicalState;
}

internal static class CortexPolicyDecisionBuilder
{
internal static CortexPolicyDecisionReadout CreatePolicyDecisionReadout(
    int launchpadAction,
    int rawCandidateAction,
    int selectedCandidateAction,
    int executedAction,
    CortexPolicyAuthorities authority,
    global::Cogito.Grammar.GrammarRevisionID revision,
    ulong readoutCandidateOccurrenceDigest = 0,
    ulong readoutCandidateFingerprint = 0,
    ulong readoutFingerprint = 0,
    bool trialOverride = false,
    bool rollbackDrill = false)
{
    CortexPolicySelectionCauses cause = rollbackDrill
        ? CortexPolicySelectionCauses.RollbackDrill
        : rawCandidateAction < 0
            ? CortexPolicySelectionCauses.Launchpad
            : authority == CortexPolicyAuthorities.Grammar
                ? (trialOverride ? CortexPolicySelectionCauses.TrialOverride : CortexPolicySelectionCauses.GrammarCandidate)
                : CortexPolicySelectionCauses.ShadowCandidate;
    return new CortexPolicyDecisionReadout(
        launchpadAction, rawCandidateAction, selectedCandidateAction, executedAction, authority, revision, cause,
        readoutCandidateOccurrenceDigest, readoutCandidateFingerprint, readoutFingerprint);
}
}

internal static class CortexPolicyDecisionCheckpoint
{
    internal static void Write(CkptWriter writer, in CortexPolicyDecision decision, bool readoutIdentityPresent = true)
    {
        writer.U64(decision.DecisionID.Value);
        writer.I32(decision.LaunchpadAction);
        writer.I32(decision.RawCandidateAction);
        writer.I32(decision.SelectedCandidateAction);
        writer.I32(decision.Action);
        writer.U8((byte)decision.Authority);
        writer.U64(decision.GrammarRevision.Value);
        writer.U8((byte)decision.SelectionCause);
        writer.U64(decision.Readout.ReadoutCandidateOccurrenceDigest);
        writer.U64(decision.Readout.ReadoutCandidateFingerprint);
        if (readoutIdentityPresent) writer.U64(decision.Readout.ReadoutFingerprint);
    }

    internal static CortexPolicyDecision Read(CkptReader reader, CortexPolicyID policy, int actionCount, bool readoutIdentityPresent = true)
    {
        CortexPolicyDecisionID decisionID = new(reader.U64());
        int launchpadAction = reader.I32();
        int rawCandidateAction = reader.I32();
        int selectedCandidateAction = reader.I32();
        int executedAction = reader.I32();
        CortexPolicyAuthorities authority = (CortexPolicyAuthorities)reader.U8();
        global::Cogito.Grammar.GrammarRevisionID revision = new(reader.U64());
        CortexPolicySelectionCauses cause = (CortexPolicySelectionCauses)reader.U8();
        ulong occurrenceDigest = reader.U64();
        ulong candidateFingerprint = reader.U64();
        ulong readoutFingerprint = readoutIdentityPresent ? reader.U64() : 0;
        if (!Enum.IsDefined(authority) || !Enum.IsDefined(cause))
            throw new InvalidDataException($"invalid restored policy readout for '{policy}'");
        CortexPolicyDecision decision = new(
            decisionID,
            policy,
            new CortexPolicyDecisionReadout(
                launchpadAction,
                rawCandidateAction,
                selectedCandidateAction,
                executedAction,
                authority,
                revision,
                cause,
                occurrenceDigest,
                candidateFingerprint,
                readoutFingerprint));
        decision.Readout.Validate(actionCount);
        return decision;
    }
}

public sealed partial class Cortex
{
    /// The immutable readout tuple prepared before authority selection. It contains
    /// identities and decisions only; feature spans remain caller-owned and are
    /// revalidated when the tuple is consumed.
    internal readonly record struct CortexPolicyActionPreparation(
        CortexPolicyID Policy,
        int LaunchpadAction,
        PolicyCanonicalStateID CanonicalState,
        bool HasCanonicalState,
        int PreparedStep,
        GrammarRevisionID InstallRevision,
        ulong ActiveProgramFingerprint,
        GrammarPolicyDecision GrammarReadout,
        bool HasGrammarReadout,
        int RawCandidateAction,
        GrammarRevisionID CandidateRevision,
        ulong CandidateFingerprint,
        ulong CandidateOccurrenceDigest,
        GrammarPolicyContextKey DecisionContext,
        PolicyReadoutAttemptOutcomes ReadoutAttempt,
        CortexPolicyQuotaDecisionID? ReadoutQuotaDecisionID,
        PolicyBoundaryGateObservation BoundaryGate,
        bool BoundaryAllowsProduction,
        bool CanonicalScopeAllowsProduction)
    {
        internal bool IsValid => Policy.Value.Length != 0 && PreparedStep >= 0;
    }

    // Frozen checkpoint section tag, identifier-side name is PolicyQuotaAuditOnlyCheckpointTag.
    private const uint PolicyQuotaAuditOnlyCheckpointTag = 0x50464336; // PFC6
    private const uint PolicyTrialExecutionCheckpointTag = 0x50544531; // PTE1

    private sealed class PolicyJournalBuffer
    {
        private sealed class PendingFile(string header)
        {
            public string Header { get; } = header;
            public StringBuilder Rows { get; } = new();
        }

        private readonly Dictionary<string, PendingFile> _files = new(StringComparer.Ordinal);

        public void Append(string path, string header, string row)
        {
            if (!_files.TryGetValue(path, out PendingFile? pending))
                _files.Add(path, pending = new PendingFile(header));
            pending.Rows.Append(row).Append('\n');
        }

        public void Flush()
        {
            foreach (KeyValuePair<string, PendingFile> entry in _files)
            {
                PendingFile pending = entry.Value;
                if (pending.Rows.Length == 0) continue;
                string rows = pending.Rows.ToString();
                if (File.Exists(entry.Key))
                    File.AppendAllText(entry.Key, rows, Encoding.UTF8);
                else
                    File.WriteAllText(entry.Key, pending.Header + "\n" + rows, Encoding.UTF8);
                pending.Rows.Clear();
            }
        }

        public void AppendDurable(string path, string header, string row)
        {
            Flush();
            bool exists = File.Exists(path);
            if (exists)
            {
                using FileStream existing = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (existing.Length == 0) throw new InvalidDataException($"durable policy journal '{path}' is empty");
                string? existingHeader;
                using (StreamReader reader = new(existing, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                           bufferSize: 1024, leaveOpen: true))
                    existingHeader = reader.ReadLine();
                existing.Seek(-1, SeekOrigin.End);
                if (existing.ReadByte() != '\n')
                    throw new InvalidDataException($"durable policy journal '{path}' has a partial terminal row");
                if (existingHeader is null || (existingHeader != header
                    && !header.StartsWith(existingHeader + "\t", StringComparison.Ordinal)))
                    throw new InvalidDataException($"durable policy journal '{path}' header is not an ancestor of the append schema");
            }
            using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            if (!exists)
            {
                byte[] headerBytes = Encoding.UTF8.GetBytes(header + "\n");
                stream.Write(headerBytes);
            }
            byte[] rowBytes = Encoding.UTF8.GetBytes(row + "\n");
            stream.Write(rowBytes);
            stream.Flush(flushToDisk: true);
        }
    }

    internal enum PolicyReadoutAttemptOutcomes : byte
    {
        None,
        CachedHit,
        PaidScanMatched,
        PaidScanNoMatch,
        QuotaDenied,
        QuotaNoScan,
        SuppressedNoScan,
    }

    // Frozen wire token, identifier-side name is PolicyOccurrenceCheckReceiptFile.
    private const string PolicyOccurrenceCheckReceiptFile = "policy_verifications.tsv";
    // Frozen wire token, identifier-side name is PolicyOccurrenceCheckCoverageReceiptFile.
    private const string PolicyOccurrenceCheckCoverageReceiptFile = "policy_verification_coverage.tsv";
    private const string PolicyDecisionReceiptFile = "policy_decisions.tsv";
    // Frozen wire token, identifier-side name is PolicyTrialQuotaJournalFile.
    private const string PolicyTrialQuotaJournalFile = "policy_trial_funding.journal.tsv";
    // Frozen wire token, identifier-side name is PolicyTrialCompletionJournalFile.
    private const string PolicyTrialCompletionJournalFile = "policy_trial_settlements.journal.tsv";
    internal const string PolicyDecisionReceiptHeader = "step\tevent_id\tdecision_id\tpolicy\tlaunchpad_action\traw_candidate_action\tselected_candidate_action\texecuted_action\taction_count\tauthority\trevision\tselection_cause\tdrill\tpacket_base64";
    private const string PolicyTrialQuotaLegacy4Header = "policy\tfingerprint\tseed_step\tcost";
    // Frozen wire token, identifier-side name is PolicyTrialQuotaLegacyHeader.
    private const string PolicyTrialQuotaLegacyHeader = "funding_id\tpolicy\tcandidate_fingerprint\tfunding_step\trequested_horizon_steps\tarm_count\tplanned_arm_steps\treserved_arm_steps\tdecision\tcharged_steps\tremaining_budget";
    private const string PolicyTrialQuotaCurrentHeader = PolicyTrialQuotaLegacyHeader + "\tcandidate_state\tdenial_reason\tcandidate_origin_step\tcandidate_current_step\tcandidate_required_step\tcandidate_revision";
    private const string PolicyTrialQuotaAllocationHeader = PolicyTrialQuotaCurrentHeader + "\tallocation_identity\tallocation_digest\tallocation_arm_steps";
    // Frozen wire token, identifier-side name is PolicyTrialQuotaLegacyJournalHeader.
    private const string PolicyTrialQuotaLegacyJournalHeader = PolicyTrialQuotaAllocationHeader + "\tseed_custody_digest";
    // Frozen wire token, identifier-side name is PolicyTrialQuotaJournalHeader.
    private const string PolicyTrialQuotaJournalHeader = PolicyTrialQuotaAllocationHeader + "\tseed_custody_digest\treadout_fingerprint";
    // Frozen wire token, identifier-side name is PolicyTrialCompletionJournalHeader.
    private const string PolicyTrialCompletionJournalHeader = "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds";
    // Frozen wire token, identifier-side name is PolicyReadoutQuotaJournalFile.
    private const string PolicyReadoutQuotaJournalFile = "policy_readout_funding.journal.tsv";
    // Frozen wire token, identifier-side name is PolicyReadoutCompletionJournalFile.
    private const string PolicyReadoutCompletionJournalFile = "policy_readout_settlements.journal.tsv";
    private const string PolicyReadoutAllocationJournalFile = "policy_readout_allocations.journal.tsv";
    private const string PolicyOccurrenceCheckReceiptHeader = "policy\treadout_fingerprint\tcandidate_fingerprint\trevision\tcomparisons\tagreements\tfailures\tpassed\trequired_states\tcovered_states\tmissing_states\trequired_digest\tcovered_digest\tmissing_digest\tcoverage_attribution\tstate_policy\tstate_kind\tstate_version\tstate_value";
    // Frozen wire token, identifier-side name is PolicyOccurrenceCheckCoverageReceiptHeader.
    private const string PolicyOccurrenceCheckCoverageReceiptHeader = "policy\tfingerprint\tstep\trequired_states\tcovered_states\tmissing_states\trequired_digest\tcovered_digest\tmissing_digest\tcoverage_attribution\tverifier_comparisons\tverifier_agreements\tverifier_misses\tstate_kind\tstate_version\tstate_value\tcovered\taction\tcandidate_fingerprint\tsupport_digest\trevision\torigin_revision\tinstalled_step\tcomparisons\tagreements\tmisses";
    internal static readonly string[] PolicyJournalFileNames =
    [
        PolicyDecisionReceiptFile,
        PolicyOccurrenceCheckReceiptFile,
        PolicyOccurrenceCheckCoverageReceiptFile,
        PolicyTrialQuotaJournalFile,
        PolicyTrialCompletionJournalFile,
        PolicyReadoutQuotaJournalFile,
        PolicyReadoutCompletionJournalFile,
        PolicyReadoutAllocationJournalFile,
        PolicyBoundaryReceiptFile,
        PolicyBoundaryAdmissionCensusFile,
        PolicyBoundaryOpportunityCensusFile,
    ];
    // Frozen wire token, identifier-side name is PolicyReadoutQuotaJournalHeader.
    private const string PolicyReadoutQuotaJournalHeader = "funding_id\tpolicy\tcandidate_fingerprint\tgrammar_revision\tcontext_digest\tcontext_bytes\tdeliberation_depth\tfunding_step\tplanned_units\treserved_units\tdecision\tcharged_units\tremaining_budget\tallocation_sequence\troster_digest\taccount_balance_before\taccount_balance_after";
    // Frozen wire token, identifier-side name is PolicyReadoutAllocationJournalHeader.
    private const string PolicyReadoutAllocationJournalHeader = "sequence\tstep\troster_digest\tpolicy\tbalance_before\tcredited_units\texpired_units\tbalance_after";
    private sealed class PolicyState(
        CortexPolicySchema schema,
        CortexPolicyModes mode,
        CortexPolicyAuthorities authorityCeiling)
    {
        public sealed class CanonicalCandidateEvidence(
            PolicyCanonicalStateID state,
            int action,
            ulong candidateFingerprint,
            ulong occurrenceDigest,
            GrammarRevisionID revision,
            int installedStep)
        {
            public PolicyCanonicalStateID State { get; } = state;
            public int Action { get; set; } = action;
            public ulong CandidateFingerprint { get; set; } = candidateFingerprint;
            public ulong OccurrenceDigest { get; set; } = occurrenceDigest;
            public GrammarRevisionID Revision { get; set; } = revision;
            public int InstalledStep { get; set; } = installedStep;
            public GrammarRevisionID OriginRevision { get; set; } = revision;
            public int Comparisons { get; set; }
            public int Agreements { get; set; }
            public int Misses { get; set; }
        }

        public CortexPolicySchema Schema { get; } = schema;
        public CortexPolicyModes Mode { get; set; } = mode;
        public CortexPolicyAuthorities AuthorityCeiling { get; } = authorityCeiling;
        public CortexPolicyAuthorities Authority { get; set; } = CortexPolicyAuthorities.Launchpad;
        public PolicyReadoutCache ReadoutCache { get; } = new();
        public global::Cogito.Grammar.GrammarRevisionID ObservedInstallRevision { get; set; }
        public global::Cogito.Grammar.GrammarRevisionID ReadoutCandidateRevision { get; set; }
        public ulong ReadoutCandidateFingerprint { get; set; }
        public ulong ReadoutCandidateSetDigest { get; set; }
        public PolicyCanonicalStateID ReadoutCandidateState { get; set; }
        public ulong ReadoutCandidateOccurrenceDigest { get; set; }
        public int ReadoutCandidateAction { get; set; } = -1;
        public bool ReadoutCandidatePending { get; set; }
        public Dictionary<PolicyCanonicalStateID, CanonicalCandidateEvidence> CanonicalCandidates { get; } = new();
        public Dictionary<PolicyCanonicalStateID, PolicyVerifiedScopeEntry> VerifiedScopes { get; } = new();
        public bool CanonicalProgramDigestDirty { get; set; }
        public uint CanonicalCoverageVersion { get; set; } = 1;
        public uint CanonicalCoverageCacheVersion { get; set; }
        public PolicyCanonicalCoverageReceipt CanonicalCoverageCache { get; set; }
        public PolicyCanonicalCoverageIndex? CanonicalCoverageIndex { get; set; }
        public int ReadoutInstalledStep { get; set; } = -1;
        public int ReadoutOracleComparisons { get; set; }
        public int ReadoutOracleAgreements { get; set; }
        public ulong Decisions { get; set; }
        public ulong Outcomes { get; set; }
        public ulong CensoredOutcomes { get; set; }
        public ulong LaunchpadExecutions { get; set; }
        public ulong GrammarExecutions { get; set; }
        public ulong GrammarOutcomes { get; set; }
        public ulong PaidGrammarOutcomes { get; set; }
        public ulong[] ActionExecutions { get; } = new ulong[schema.ActionCount];
        public long ConservedCost { get; set; }
        public ulong GrammarDivergentExecutions { get; set; }
        public int LastGrammarLaunchpadAction { get; set; } = -1;
        public int LastGrammarAction { get; set; } = -1;
        public double[] LastGrammarFeatures { get; set; } = [];
        public int TrialGrammarExecutionsRemaining { get; set; } = -1;
        public int TrialActionOffset { get; set; }
        public ulong? TrialForcedDivergenceSeed { get; set; }
        public CortexPolicyPendingForcedTrialIntent PendingForcedTrialIntent { get; set; }
        // Runtime-only domain transport for the captured forced candidate. The
        // durable seed audit-only remains authoritative; this is rebound before each
        // child starts so policy selection cannot invent a replacement action.
        public string TrialForcedCandidateCanonical { get; set; } = "";
        public ulong TrialForcedCandidateDigest { get; set; }
        public ulong TrialForcedFrontierRevision { get; set; }
        public string TrialForcedFrontierAuthoritySHA256 { get; set; } = "";
        public ulong TrialForcedDivergenceExecutions { get; set; }
        // Optional quota identity for the currently suppressed trial.  This is
        // deliberately separate from PendingForcedTrialIntent: ordinary candidate
        // and shadow trials may be paid without carrying a forced-seed intent.
        public CortexPolicyQuotaDecisionID ActiveTrialQuotaID { get; set; }
        public bool SuppressTrialPackets { get; set; }
        public CortexPolicySelectionCauses TrialExecutionCause { get; set; } = CortexPolicySelectionCauses.Launchpad;
        public CortexPolicyTrialExecutionOutcomes TrialExecutionOutcome { get; set; } = CortexPolicyTrialExecutionOutcomes.NotAttempted;
        public CortexPolicyDecision? TrialExecutionCorroboration { get; set; }
        public ulong TrialExecutionReadoutFingerprint { get; set; }
        public int TrialExecutionStep { get; set; } = -1;
        public long TrialRequestCount { get; set; }
        public long TrialGuardAdmittedCount { get; set; }
        public CortexPolicyDecision? TrialLastRequest { get; set; }
        public int TrialLastRequestStep { get; set; } = -1;
        public PolicyTrialExecutionHistory HistoricalTrialExecution { get; set; }
        // Fork-local frozen-control rail: shadow comparisons may observe a candidate, but
        // no admission or re-admission transition is allowed in this arm.
        public bool TrialFrozen { get; set; }
        public ulong TrialAdaptationTransitions { get; set; }
        public int PriorAction { get; set; } = -1;
        public int LastAction { get; set; } = -1;
        public int ActionReversals { get; set; }
        public int ShadowComparisons { get; set; }
        public int ShadowAgreements { get; set; }
        public int EmulationMisses { get; set; }
        public bool ReadoutLearnerEvidenceTrusted { get; set; } = true;
        public int Readmissions { get; set; }
        public bool RollbackDrillPending { get; set; }
        public bool RollbackDrillCompleted { get; set; }
        public ulong AssayedFingerprint { get; set; }
        public ulong VerifiedFingerprint { get; set; }
        public ulong AssayedReadoutFingerprint { get; set; }
        public ulong VerifiedReadoutFingerprint { get; set; }
        public GrammarRevisionID VerifiedRevision { get; set; }
        public CortexPolicyDecisionReadout LastDecisionReadout { get; set; } = new(
            0, -1, -1, 0, CortexPolicyAuthorities.Launchpad,
            global::Cogito.Grammar.GrammarRevisionID.Zero,
            CortexPolicySelectionCauses.Launchpad);
        public CortexPolicyDecisionID LastDecisionID { get; set; }
    }

    private readonly Dictionary<CortexPolicyID, PolicyState> _policies = new();
    // The source decision event is the audit-only anchor for a paid boundary. Keep the
    // latest Homeostat identity across tape reordering; checkpoint load falls back to the tape.
    private CortexPolicyDecisionID _latestHomeostatDecisionEventDecisionID;
    private TapeEventID _latestHomeostatDecisionEventID = new(-1);
    private readonly HashSet<string> _fundedPolicyTrials = new(StringComparer.Ordinal);
    private readonly List<CortexPolicyTrialQuotaDecision> _policyTrialQuotaDecisions = new();
    private readonly Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyTrialQuotaDecision> _policyTrialQuotaByID = new();
    private readonly List<CortexPolicyTrialCompletion> _policyTrialCompletions = new();
    private readonly Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyTrialCompletion> _policyTrialCompletionByID = new();
    private ulong _nextPolicyDecisionID = 1;
    private long _policyTrialUsedSteps;
    private long _policyTrialHeldSteps;
    private long _policyTrialCompletedUsedSteps;
    private readonly List<CortexPolicyReadoutQuotaDecision> _policyReadoutQuotaDecisions = new();
    private readonly Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> _policyReadoutQuotaByID = new();
    private readonly Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> _policyReadoutPaidByID = new();
    private readonly List<CortexPolicyTrialCompletion> _policyReadoutCompletions = new();
    private readonly Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyTrialCompletion> _policyReadoutCompletionByID = new();
    private long _policyReadoutUsedUnits;
    private long _policyReadoutHeldUnits;
    private long _policyReadoutCompletedUsedUnits;
    private bool _policyReadoutJournalRewritePending;
    private bool _policyTrialAuthorityValidationPending;
    private readonly PolicyJournalBuffer _policyJournalBuffer = new();
    // Composed indexes over typed tape records.  The tape remains authoritative;
    // these caches are rebuilt once after a tape bind/load and advanced only by
    // newly appended rearm packets, keeping the hot decision path O(1).
    private Tape? _rearmReceiptIndexTape;
    private readonly Dictionary<CortexPolicyID, CortexPolicyPendingForcedTrialRearmEvaluation> _latestRearmReceipt = new();
    private readonly Dictionary<CortexPolicyID, TapeEventID> _latestRearmReceiptEvent = new();
    private readonly Dictionary<BoundarySourceCorroborationKey, List<(TapeEventID EventID, CortexPolicyBoundarySourceCorroboration Corroboration)>> _boundarySourceCorroborationIndex = new();
    private Tape? _boundarySourceCorroborationIndexTape;
    private TapeRevision _boundarySourceCorroborationIndexRevision;
    private long _boundarySourceCorroborationIndexNextID;
    private readonly ulong[] _derivedRearmDenialCounts =
        new ulong[Enum.GetValues<CortexPolicyPendingForcedTrialRearmDenialSpecies>().Length];
    private CortexPolicyActionPreparation? _preparedPolicyAction;
    private bool _capturingPolicyPreparation;
    private bool _consumingPolicyPreparation;

    private readonly record struct BoundarySourceCorroborationKey(
        ulong SourceDecisionID,
        long SourceDecisionEventID,
        GrammarRevisionID ReadoutRevision,
        ulong ReadoutFingerprint,
        ulong CandidateFingerprint,
        ulong OccurrenceDigest);

    private void FlushPolicyJournalBuffer() => _policyJournalBuffer.Flush();

    internal ulong ReadPendingForcedTrialRearmDenialCount(
        CortexPolicyPendingForcedTrialRearmDenialSpecies species)
    {
        EnsureRearmReceiptIndex();
        return (uint)species < (uint)_derivedRearmDenialCounts.Length
            ? _derivedRearmDenialCounts[(int)species] : 0;
    }

    /// Read the current forced-child rearm verdict without mutating policy state
    /// or emitting a second receipt. This is the preserved-child diagnostic seam.
    internal CortexPolicyPendingForcedTrialRearmEvaluation DiagnosePendingForcedTrialRearm(
        CortexPolicyID policy,
        PolicyCanonicalStateID canonicalState = default)
    {
        PolicyState state = GetPolicy(policy);
        CortexPolicyPendingForcedTrialIntent pending = state.PendingForcedTrialIntent;
        if (state.TrialForcedDivergenceSeed.HasValue)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed,
                in pending, state.ReadoutCandidateFingerprint, state.ReadoutCandidateRevision);
        if (!pending.HasSeed)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.NoPendingIntent,
                in pending, state.ReadoutCandidateFingerprint, state.ReadoutCandidateRevision);
        PolicyCanonicalStateID successorState = canonicalState.Version == 0
            ? state.ReadoutCandidateState : canonicalState;
        CortexPolicyPendingForcedTrialIntent successor = pending with
        {
            ReadoutFingerprint = ReadActivePolicyFingerprint(state),
            CandidateFingerprint = state.ReadoutCandidateFingerprint,
            CandidateRevision = state.ReadoutCandidateRevision,
            SuccessorOccurrenceDigest = state.ReadoutCandidateOccurrenceDigest,
            CanonicalState = successorState,
        };
        return EvaluatePendingForcedTrialRearm(policy, in successor,
            successorState.Version != 0, in successorState,
            ReadActivePolicyFingerprint(state), state.ReadoutCandidateFingerprint,
            state.ReadoutCandidateOccurrenceDigest, state.ReadoutCandidateRevision);
    }

    private void RecordPendingForcedTrialRearm(
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialRearmEvaluation evaluation)
    {
        if (evaluation.DenialSpecies == CortexPolicyPendingForcedTrialRearmDenialSpecies.NoPendingIntent) return;
        CortexPolicyPendingForcedTrialRearmEvaluation receipt = evaluation;
        if (!receipt.Policy.Equals(policy))
        {
            if (receipt.Policy.Value.Length != 0)
                throw new InvalidDataException("policy rearm evaluation policy disagrees with its owner");
            receipt = receipt with { Policy = policy };
        }
        receipt = TapePacketCreator.CanonicalizePolicyPendingForcedTrialRearm(policy, in receipt);
        if (HasRecordedPendingForcedTrialRearm(policy, in receipt)) return;
        TapeEventID receiptEvent = default;
        if (_runtimeTape is not null && _runtimeJournal is not null)
            receiptEvent = TapePacketCreator.AppendPolicyPendingForcedTrialRearm(
                _runtimeTape, _runtimeJournal, Step, policy, in receipt);
        EnsureRearmReceiptIndex();
        _latestRearmReceipt[policy] = receipt;
        if (receiptEvent.Value > 0) _latestRearmReceiptEvent[policy] = receiptEvent;
        if (!receipt.Allowed && (uint)receipt.DenialSpecies < (uint)_derivedRearmDenialCounts.Length)
            _derivedRearmDenialCounts[(int)receipt.DenialSpecies]++;
        Trace.Cortex.Boundary("policy.trial-intent-rearm-evaluation",
            $"policy={policy} quota={receipt.QuotaID:X} outcome={receipt.Outcome} species={receipt.DenialSpecies} source_candidate={receipt.SourceCandidateFingerprint:X16} candidate={receipt.CandidateFingerprint:X16} source_revision={receipt.SourceCandidateRevision.Value} revision={receipt.CandidateRevision.Value} lifetime_denials={ReadPendingForcedTrialRearmDenialCount(receipt.DenialSpecies)}");
    }

    private bool HasRecordedPendingForcedTrialRearm(
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialRearmEvaluation evaluation)
    {
        EnsureRearmReceiptIndex();
        return _latestRearmReceipt.TryGetValue(policy, out CortexPolicyPendingForcedTrialRearmEvaluation prior)
            && prior == evaluation;
    }

    private void EnsureRearmReceiptIndex()
    {
        if (ReferenceEquals(_rearmReceiptIndexTape, _runtimeTape)) return;
        _rearmReceiptIndexTape = _runtimeTape;
        _latestRearmReceipt.Clear();
        _latestRearmReceiptEvent.Clear();
        Array.Clear(_derivedRearmDenialCounts);
        if (_runtimeTape is null) return;
        foreach (TapeEventView view in _runtimeTape.GetEventViews())
        {
            if (!view.Source.StartsWith("policy-rearm:", StringComparison.Ordinal)) continue;
            if (view.Provenance != Provenances.Execution
                || !_runtimeTape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodePolicyPendingForcedTrialRearm(payload, out CortexPolicyID policy, out CortexPolicyPendingForcedTrialRearmEvaluation evaluation)
                || !string.Equals(view.Source, "policy-rearm:" + policy.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"policy rearm receipt {view.Id} is malformed or has mismatched source");
            }
            if (!evaluation.Allowed && (uint)evaluation.DenialSpecies < (uint)_derivedRearmDenialCounts.Length)
                _derivedRearmDenialCounts[(int)evaluation.DenialSpecies]++;
            if (_latestRearmReceiptEvent.TryGetValue(policy, out TapeEventID priorEvent)
                && priorEvent.Value >= view.Id.Value) continue;
            _latestRearmReceipt[policy] = evaluation;
            _latestRearmReceiptEvent[policy] = view.Id;
        }
    }

    public void RegisterPolicy(CortexPolicySchema schema)
    {
        if (_policyReadoutRosterSealed && !_policies.ContainsKey(schema.Policy))
            throw new InvalidOperationException("policy registration is closed after the readout roster seals");
        CortexPolicyLearningConfig config = _config.Learning.Policies;
        CortexPolicyModes configured = config.ResolveMode(schema.Policy);
        CortexPolicyModes mode = configured < schema.ModeCeiling ? configured : schema.ModeCeiling;
        if (_policies.TryAdd(schema.Policy, new PolicyState(schema, mode, config.AuthorityCeiling))) return;

        PolicyState state = _policies[schema.Policy];
        if (state.Schema.FeatureCount != schema.FeatureCount
            || state.Schema.ActionCount != schema.ActionCount
            || state.Schema.OutcomeCount != schema.OutcomeCount)
            throw new InvalidDataException($"restored policy '{schema.Policy}' schema does not match its runtime owner");
    }

    public CortexPolicyDecision ChoosePolicyAction(
        CortexPolicyID policy,
        int launchpadAction,
        ReadOnlySpan<MetricSample> features)
        => ChoosePreparedPolicyAction(PreparePolicyAction(policy, launchpadAction, default, false, features, ReadOnlySpan<MetricID>.Empty), features, ReadOnlySpan<MetricID>.Empty);

    // Excluded metrics remain in the raw teacher/decision packet; they are
    // omitted only from grammar identity when they describe observation time
    // rather than the semantic state that should choose an action.
    public CortexPolicyDecision ChoosePolicyAction(
        CortexPolicyID policy,
        int launchpadAction,
        ReadOnlySpan<MetricSample> features,
        ReadOnlySpan<MetricID> excludedReadoutMetricIDs)
        => ChoosePreparedPolicyAction(PreparePolicyAction(policy, launchpadAction, default, false, features, excludedReadoutMetricIDs), features, excludedReadoutMetricIDs);

    public CortexPolicyDecision ChoosePolicyAction(
        CortexPolicyID policy,
        int launchpadAction,
        in PolicyCanonicalStateID canonicalState,
        ReadOnlySpan<MetricSample> features)
        => ChoosePreparedPolicyAction(PreparePolicyAction(policy, launchpadAction, in canonicalState, true, features, ReadOnlySpan<MetricID>.Empty), features, ReadOnlySpan<MetricID>.Empty);

    internal CortexPolicyActionPreparation PreparePolicyAction(
        CortexPolicyID policy,
        int launchpadAction,
        in PolicyCanonicalStateID canonicalState,
        bool hasCanonicalState,
        ReadOnlySpan<MetricSample> features,
        ReadOnlySpan<MetricID> excludedReadoutMetricIDs)
    {
        if (_preparedPolicyAction is not null || _capturingPolicyPreparation || _consumingPolicyPreparation)
            throw new InvalidOperationException("a policy action preparation is already in flight");
        _capturingPolicyPreparation = true;
        try
        {
            _ = ChoosePolicyActionCore(policy, launchpadAction, in canonicalState, hasCanonicalState, features, excludedReadoutMetricIDs);
        }
        catch
        {
            _preparedPolicyAction = null;
            throw;
        }
        finally
        {
            _capturingPolicyPreparation = false;
        }
        if (_preparedPolicyAction is not CortexPolicyActionPreparation prepared || !prepared.IsValid)
            throw new InvalidDataException("policy readout preparation did not produce a valid tuple");
        return prepared;
    }

    internal CortexPolicyDecision ChoosePreparedPolicyAction(
        in CortexPolicyActionPreparation prepared,
        ReadOnlySpan<MetricSample> features,
        ReadOnlySpan<MetricID> excludedReadoutMetricIDs)
    {
        if (_preparedPolicyAction is not CortexPolicyActionPreparation active || active != prepared)
            throw new InvalidDataException("policy action preparation is stale or was not issued by this Cortex");
        try
        {
            if (prepared.Policy.Value.Length == 0 || prepared.PreparedStep != Step)
                throw new InvalidDataException("policy action preparation step or policy identity changed");
            PolicyState state = GetPolicy(prepared.Policy);
            if ((uint)prepared.LaunchpadAction >= (uint)state.Schema.ActionCount
                || features.Length != state.Schema.FeatureCount)
                throw new InvalidDataException("policy action preparation input shape changed");
            GrammarRevisionID installRevision = _runtimeInstallRevision?.Revision ?? GrammarRevisionID.Zero;
            if (installRevision != prepared.InstallRevision
                || ReadActivePolicyFingerprint(state) != prepared.ActiveProgramFingerprint)
                throw new InvalidDataException("policy action preparation install revision tuple changed");
            if (prepared.HasGrammarReadout
                && (state.ReadoutCandidateState != prepared.CanonicalState
                    || state.ReadoutCandidateRevision != prepared.CandidateRevision
                    || state.ReadoutCandidateFingerprint != prepared.CandidateFingerprint
                    || state.ReadoutCandidateOccurrenceDigest != prepared.CandidateOccurrenceDigest))
                throw new InvalidDataException("policy action preparation candidate tuple changed");
            _consumingPolicyPreparation = true;
            PolicyCanonicalStateID canonicalState = prepared.CanonicalState;
            return ChoosePolicyActionCore(prepared.Policy, prepared.LaunchpadAction,
                in canonicalState, prepared.HasCanonicalState, features, excludedReadoutMetricIDs);
        }
        finally
        {
            _consumingPolicyPreparation = false;
            if (_preparedPolicyAction is CortexPolicyActionPreparation activeAfterValidation && activeAfterValidation == prepared)
                _preparedPolicyAction = null;
        }
    }

    internal void DiscardPreparedPolicyAction(in CortexPolicyActionPreparation prepared)
    {
        if (_preparedPolicyAction is CortexPolicyActionPreparation active && active == prepared)
            _preparedPolicyAction = null;
    }

    private CortexPolicyDecision ChoosePolicyActionCore(
        CortexPolicyID policy,
        int launchpadAction,
        in PolicyCanonicalStateID canonicalState,
        bool hasCanonicalState,
        ReadOnlySpan<MetricSample> features,
        ReadOnlySpan<MetricID> excludedReadoutMetricIDs)
    {
        PolicyState state = GetPolicy(policy);
        if ((uint)launchpadAction >= (uint)state.Schema.ActionCount)
            throw new ArgumentOutOfRangeException(nameof(launchpadAction));
        if (features.Length != state.Schema.FeatureCount)
            throw new ArgumentException($"policy '{policy}' expected {state.Schema.FeatureCount} features, received {features.Length}", nameof(features));
        if (state.HistoricalTrialExecution.IsPresent
            && (state.SuppressTrialPackets || state.ActiveTrialQuotaID.Value != 0
                || state.PendingForcedTrialIntent.HasSeed))
            throw new InvalidDataException("completed policy trial execution retains an active trial epoch");

        global::Cogito.Grammar.InstallRevision? runtimeInstallRevision = _runtimeInstallRevision;
        global::Cogito.Grammar.GrammarRevisionID revision = runtimeInstallRevision is { } installRevision
            ? installRevision.Revision
            : global::Cogito.Grammar.GrammarRevisionID.Zero;

        int executedAction = launchpadAction;
        int rawCandidateAction = -1;
        int selectedCandidateAction = -1;
        CortexPolicyAuthorities authority = CortexPolicyAuthorities.Launchpad;
        bool trialOverrideUsed = false;
        GrammarPolicyDecision grammarReadout = default;
        bool hasGrammarReadout = false;
        GrammarPolicyContextKey decisionContext = default;
        PolicyReadoutAttemptOutcomes readoutAttempt = PolicyReadoutAttemptOutcomes.None;
        CortexPolicyQuotaDecisionID? readoutQuotaDecisionID = null;
        GrammarRevisionID candidateRevision = GrammarRevisionID.Zero;
        ulong candidateFingerprint = 0;
        PolicyBoundaryGateObservation boundaryGate = default;
        bool boundaryAllowsProduction = true;
        bool canonicalScopeAllowsProduction = true;
        bool paidTrialArmed = state.SuppressTrialPackets && state.ActiveTrialQuotaID.Value != 0;
        PolicyCanonicalStateID paidCanonicalState = state.ReadoutCandidateState;
        bool authenticatedPaidTrialEpoch = paidTrialArmed
            && TryAuthenticatePaidTrialEpoch(
                state, policy, state.ActiveTrialQuotaID, auditOnlyDigest: null,
                state.TrialExecutionCause, in paidCanonicalState,
                ReadActivePolicyFingerprint(state), state.ReadoutCandidateFingerprint,
                state.ReadoutCandidateOccurrenceDigest, state.ReadoutCandidateRevision, out _);
        bool paidTrialOpportunity = authenticatedPaidTrialEpoch
            && hasCanonicalState
            && canonicalState.Equals(paidCanonicalState);
        bool discoveringPaidTrialSuccessor = authenticatedPaidTrialEpoch
            && hasCanonicalState
            && !paidTrialOpportunity
            && state.PendingForcedTrialIntent.HasSeed;
        if (paidTrialArmed)
            Trace.Cortex.Boundary("policy.trial-opportunity",
                $"step={Step} policy={policy} quota={state.ActiveTrialQuotaID} cause={state.TrialExecutionCause} authority={state.Authority} has_canonical={(hasCanonicalState ? 1 : 0)} actual_state={canonicalState} paid_state={paidCanonicalState} state_match={(hasCanonicalState && canonicalState.Equals(paidCanonicalState) ? 1 : 0)} epoch_auth={(authenticatedPaidTrialEpoch ? 1 : 0)} opportunity={(paidTrialOpportunity ? 1 : 0)}");
        if (paidTrialOpportunity && !_consumingPolicyPreparation && runtimeInstallRevision is not null)
        {
            // A paid arm is an authenticated epoch, not a fresh learner lookup.  A
            // install revision may advance while the child is still at its first action;
            // replay the exact paid candidate/scope tuple instead of allowing the
            // new revision to demote the arm before its configured cause executes.
            grammarReadout = new GrammarPolicyDecision(
                state.ReadoutCandidateAction, 0, 0, state.ReadoutCandidateRevision,
                default, state.ReadoutCandidateFingerprint)
            {
                OccurrenceDigest = state.ReadoutCandidateOccurrenceDigest,
            };
            hasGrammarReadout = true;
            readoutAttempt = PolicyReadoutAttemptOutcomes.CachedHit;
            candidateRevision = state.ReadoutCandidateRevision;
            candidateFingerprint = state.ReadoutCandidateFingerprint;
            PolicyCanonicalStateID retainedState = state.ReadoutCandidateState;
            decisionContext = new GrammarPolicyContextKey(
                retainedState, state.Schema.ActionCount,
                _config.Learning.Policies.ReadoutDeliberationQuota);
        }
        if (_consumingPolicyPreparation)
        {
            if (_preparedPolicyAction is not CortexPolicyActionPreparation prepared
                || !prepared.Policy.Equals(policy)
                || prepared.LaunchpadAction != launchpadAction
                || prepared.HasCanonicalState != hasCanonicalState
                || prepared.CanonicalState != canonicalState)
                throw new InvalidDataException("policy action preparation tuple changed before consumption");
            revision = prepared.InstallRevision;
            hasGrammarReadout = prepared.HasGrammarReadout;
            grammarReadout = prepared.GrammarReadout;
            readoutAttempt = prepared.ReadoutAttempt;
            readoutQuotaDecisionID = prepared.ReadoutQuotaDecisionID;
            rawCandidateAction = prepared.RawCandidateAction;
            candidateRevision = prepared.CandidateRevision;
            candidateFingerprint = prepared.CandidateFingerprint;
            boundaryGate = prepared.BoundaryGate;
            boundaryAllowsProduction = prepared.BoundaryAllowsProduction;
            // The authority grant can install the verified succession scope between
            // preparation and consumption.  A cached false is therefore historical
            // evidence, not a current production decision; rejoin the live scope
            // tuple after the grant and before selecting the action.
            canonicalScopeAllowsProduction = hasCanonicalState
                && hasGrammarReadout
                && IsVerifiedPolicyScope(policy, in canonicalState,
                    ReadActivePolicyFingerprint(state), candidateFingerprint,
                    grammarReadout.OccurrenceDigest, candidateRevision);
            decisionContext = prepared.DecisionContext;
        }
        if (!_consumingPolicyPreparation
            && (!authenticatedPaidTrialEpoch || discoveringPaidTrialSuccessor)
            && state.Mode >= CortexPolicyModes.Shadow && runtimeInstallRevision is { } readoutInstallRevision)
        {
            int deliberationDepth = _config.Learning.Policies.ReadoutDeliberationQuota;
            PolicyReadoutCacheReceipt readoutReceipt = hasCanonicalState
                ? GrammarPolicyReadout.ReadCanonicalCache(
                    in readoutInstallRevision, policy, in canonicalState, state.Schema.ActionCount,
                    deliberationDepth, state.ReadoutCache)
                : GrammarPolicyReadout.ReadCache(
                    in readoutInstallRevision, policy, features, state.Schema.ActionCount,
                    deliberationDepth, state.ReadoutCache, excludedReadoutMetricIDs);
            decisionContext = readoutReceipt.Context;
            bool suppressReadoutFill = state.SuppressTrialPackets
                && state.ReadoutCandidateRevision == readoutInstallRevision.Revision;
            if (readoutReceipt.Outcome == PolicyReadoutCacheOutcomes.Miss && suppressReadoutFill)
            {
                // Suppressed trial arms cannot admit a derived readout without its packet receipt;
                // leave this context absent from the bounded cache until a packet-bearing trial.
                readoutAttempt = PolicyReadoutAttemptOutcomes.SuppressedNoScan;
            }
            else if (readoutReceipt.Outcome == PolicyReadoutCacheOutcomes.Miss)
            {
                GrammarPolicyContextKey contextKey = readoutReceipt.Context;
                CortexPolicyReadoutQuotaDecision quotaDecision = DecidePolicyReadoutQuota(
                    policy,
                    hasCanonicalState
                        ? GrammarPolicyReadout.ComputeStateFingerprint(policy, in canonicalState)
                        : GrammarPolicyReadout.ComputeFingerprint(readoutInstallRevision.Revision, policy),
                    readoutInstallRevision.Revision, deliberationDepth, in contextKey);
                readoutQuotaDecisionID = quotaDecision.QuotaDecisionID;
                if (quotaDecision.Decision == CortexPolicyQuotaDecisions.Paid)
                {
                    GrammarContinuationQuota lease = new(checked((int)quotaDecision.PlannedUnits));
                    GrammarContinuationQuotaCompletion readoutCompletion;
                    if (hasCanonicalState)
                        readoutReceipt = GrammarPolicyReadout.RefillCanonical(
                            in readoutInstallRevision, policy, in canonicalState, state.Schema.ActionCount, deliberationDepth,
                            lease, state.ReadoutCache, quotaDecision.QuotaDecisionID, Step,
                            out readoutCompletion);
                    else
                        readoutReceipt = GrammarPolicyReadout.Refill(
                            in readoutInstallRevision, policy, features, state.Schema.ActionCount, deliberationDepth,
                            lease, state.ReadoutCache, in contextKey, quotaDecision.QuotaDecisionID, Step,
                            out readoutCompletion);
                    CompletePolicyReadout(
                        in quotaDecision, readoutCompletion.Used,
                        checked(readoutCompletion.ScannedBytes + readoutCompletion.ExpandedEdges),
                        CortexPolicyVerifierOutcomes.ReadoutCompleted, null);
                    readoutAttempt = readoutReceipt.HasDecision
                        ? PolicyReadoutAttemptOutcomes.PaidScanMatched
                        : PolicyReadoutAttemptOutcomes.PaidScanNoMatch;
                }
                else if (quotaDecision.Decision == CortexPolicyQuotaDecisions.Reused)
                {
                    readoutAttempt = PolicyReadoutAttemptOutcomes.QuotaNoScan;
                }
                else
                {
                    readoutAttempt = PolicyReadoutAttemptOutcomes.QuotaDenied;
                }
            }
            else
            {
                readoutAttempt = PolicyReadoutAttemptOutcomes.CachedHit;
            }
            hasGrammarReadout = readoutReceipt.HasDecision;
            grammarReadout = readoutReceipt.Decision;
            if (discoveringPaidTrialSuccessor
                && readoutAttempt is PolicyReadoutAttemptOutcomes.PaidScanMatched
                    or PolicyReadoutAttemptOutcomes.PaidScanNoMatch
                    or PolicyReadoutAttemptOutcomes.CachedHit)
                Trace.Cortex.Boundary("policy.trial-successor-readout",
                    $"step={Step} policy={policy} quota={state.ActiveTrialQuotaID} outcome={readoutAttempt} install revision={readoutInstallRevision.Revision.Value} state={canonicalState} context_action_count={readoutReceipt.Context.ActionCount} schema_action_count={state.Schema.ActionCount} decision={(hasGrammarReadout ? 1 : 0)} candidate={(hasGrammarReadout ? grammarReadout.Fingerprint : 0):X16} occurrence={(hasGrammarReadout ? grammarReadout.OccurrenceDigest : 0):X16}");

            if (hasCanonicalState && readoutAttempt == PolicyReadoutAttemptOutcomes.PaidScanNoMatch)
            {
                // Only a completed paid scan can prove that this state no longer has a match.
                // A denied or suppressed lookup is merely missing validation and must not erase
                // the learned state -> action evidence.
                if (state.CanonicalCandidates.Remove(canonicalState, out PolicyState.CanonicalCandidateEvidence? removedEvidence))
                {
                    RemoveCanonicalEvidenceTotals(state, removedEvidence);
                    state.CanonicalProgramDigestDirty = true;
                    RemoveCanonicalCoverage(state, in canonicalState);
                    RefreshCanonicalProgramDigest(state, policy);
                    state.VerifiedScopes.Remove(canonicalState);
                }
                if (!discoveringPaidTrialSuccessor)
                {
                    state.ReadoutCandidateRevision = GrammarRevisionID.Zero;
                    state.ReadoutCandidateFingerprint = 0;
                    state.ReadoutCandidateState = default;
                    state.ReadoutCandidateOccurrenceDigest = 0;
                    state.ReadoutCandidateAction = -1;
                    state.AssayedFingerprint = 0;
                    state.VerifiedFingerprint = 0;
                    state.AssayedReadoutFingerprint = 0;
                    state.VerifiedReadoutFingerprint = 0;
                    state.VerifiedRevision = GrammarRevisionID.Zero;
                    state.ReadoutCandidatePending = false;
                }
            }
            else if (hasCanonicalState
                && readoutAttempt is PolicyReadoutAttemptOutcomes.QuotaDenied
                    or PolicyReadoutAttemptOutcomes.QuotaNoScan
                    or PolicyReadoutAttemptOutcomes.SuppressedNoScan
                    or PolicyReadoutAttemptOutcomes.None)
            {
                // No current install revision proof exists for this step, but absence of quota or
                // a suppressed packet is not counterevidence against the retained program.
                if (!discoveringPaidTrialSuccessor)
                    ClearActiveCanonicalReadout(state);
            }

            bool revisionDriftClearedOverride = false;
            GrammarRevisionID revisionDriftCandidateRevision = GrammarRevisionID.Zero;
            ulong revisionDriftCandidateFingerprint = 0;
            if (!hasCanonicalState && !hasGrammarReadout && state.ReadoutCandidateRevision != GrammarRevisionID.Zero
                && state.ReadoutCandidateRevision != readoutInstallRevision.Revision)
            {
                // A revision advance invalidates the prior candidate even when this policy cannot yet afford the
                // replacement readout. Leaving the old stamp beside the new cache dialect makes resume reject a
                // valid denied readout as if it were a corrupt checkpoint.
                revisionDriftClearedOverride = state.TrialForcedDivergenceSeed.HasValue || state.TrialActionOffset != 0;
                revisionDriftCandidateRevision = state.ReadoutCandidateRevision;
                revisionDriftCandidateFingerprint = state.ReadoutCandidateFingerprint;
                state.ReadoutCandidateRevision = GrammarRevisionID.Zero;
                state.ReadoutCandidateFingerprint = 0;
                state.ReadoutCandidateSetDigest = 0;
                state.ReadoutCandidateOccurrenceDigest = 0;
                state.AssayedFingerprint = 0;
                state.VerifiedFingerprint = 0;
                state.AssayedReadoutFingerprint = 0;
                state.VerifiedReadoutFingerprint = 0;
                state.VerifiedRevision = GrammarRevisionID.Zero;
            }

            if (!hasCanonicalState && state.ReadoutCandidateRevision != readoutInstallRevision.Revision)
            {
                if (state.Authority == CortexPolicyAuthorities.Grammar)
                {
                    SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                        CortexPolicyTrialDemotionReasons.ReadoutRevisionChanged,
                        candidateRevision: revisionDriftCandidateRevision,
                        candidateFingerprint: revisionDriftCandidateFingerprint,
                        trialOverrideClearedOnRevisionDrift: revisionDriftClearedOverride);
                    state.Readmissions++;
                }
                state.ShadowComparisons = 0;
                state.ShadowAgreements = 0;
                state.EmulationMisses = 0;
                state.TrialGrammarExecutionsRemaining = -1;
                state.TrialActionOffset = 0;
                state.TrialForcedDivergenceSeed = null;
                state.TrialForcedDivergenceExecutions = 0;
                state.SuppressTrialPackets = false;
            }
        }
        else if (!_consumingPolicyPreparation && !authenticatedPaidTrialEpoch && hasCanonicalState)
        {
            // Without a live shadow install revision there is no current-state proof to expose.
            // Retain learned evidence, but close the active receipt for this decision.
            ClearActiveCanonicalReadout(state);
        }
        // A threshold is a production guard, not a second learner. Keep the candidate in the shadow stream so its
        // fingerprint can accumulate the exact comparisons needed to fund the boundary proof; only grammar
        // execution falls back to the launchpad until the receipt (or a fork-local arm override) permits it.
        if (!_consumingPolicyPreparation)
        {
            boundaryGate = ObservePolicyBoundaryGate(policy, features);
            boundaryAllowsProduction = !hasGrammarReadout || boundaryGate.AllowsProduction;
            canonicalScopeAllowsProduction = true;
        }
        // Preparation is a read-only hand-off until the authority grant has
        // succeeded. Trial admission belongs to the consumed decision; counting
        // it here would persist NotAttempted alongside a nonzero request count
        // when the grant is denied or the prepared tuple goes stale.
        if (_consumingPolicyPreparation && state.SuppressTrialPackets && hasGrammarReadout
            && state.TrialExecutionCause != CortexPolicySelectionCauses.Launchpad)
            state.TrialRequestCount = checked(state.TrialRequestCount + 1);
        if (hasGrammarReadout && state.Mode >= CortexPolicyModes.Shadow)
        {
            if (_consumingPolicyPreparation)
                goto ConsumePreparedPolicySelection;
            rawCandidateAction = grammarReadout.Action;
            candidateRevision = grammarReadout.Revision;
            candidateFingerprint = hasCanonicalState
                ? GrammarPolicyReadout.ComputeCandidateFingerprint(policy, in canonicalState, in grammarReadout)
                : grammarReadout.Fingerprint;
            bool behavioralCandidateChanged;
            bool readoutRevisionChanged;
            if (hasCanonicalState)
            {
                if (!state.CanonicalCandidates.TryGetValue(canonicalState, out PolicyState.CanonicalCandidateEvidence? evidence))
                {
                    evidence = new PolicyState.CanonicalCandidateEvidence(
                        canonicalState, grammarReadout.Action, candidateFingerprint,
                        grammarReadout.OccurrenceDigest, grammarReadout.Revision, Step);
                    state.CanonicalCandidates.Add(canonicalState, evidence);
                    state.CanonicalProgramDigestDirty = true;
                    UpdateCanonicalCoverage(state, CreateCanonicalCoverageEntry(evidence, policy));
                    behavioralCandidateChanged = true;
                    readoutRevisionChanged = true;
                }
                else
                {
                    // Behavioral proof belongs to the action/fingerprint pair.  install revision
                    // revision is validation provenance: it invalidates the current assay,
                    // but must not erase exact shadow evidence for the same candidate.
                    behavioralCandidateChanged = evidence.Action != grammarReadout.Action
                        || evidence.CandidateFingerprint != candidateFingerprint;
                    readoutRevisionChanged = evidence.Revision != grammarReadout.Revision;
                    if (behavioralCandidateChanged)
                    {
                        RemoveCanonicalEvidenceTotals(state, evidence);
                        evidence.Action = grammarReadout.Action;
                        evidence.CandidateFingerprint = candidateFingerprint;
                        evidence.OriginRevision = grammarReadout.Revision;
                        evidence.Revision = grammarReadout.Revision;
                        evidence.OccurrenceDigest = grammarReadout.OccurrenceDigest;
                        evidence.Comparisons = 0;
                        evidence.Agreements = 0;
                        evidence.Misses = 0;
                        evidence.InstalledStep = Step;
                        state.CanonicalProgramDigestDirty = true;
                        UpdateCanonicalCoverage(state, CreateCanonicalCoverageEntry(evidence, policy));
                        state.VerifiedScopes.Remove(canonicalState);
                    }
                    else if (readoutRevisionChanged || evidence.OccurrenceDigest != grammarReadout.OccurrenceDigest)
                    {
                        // Refresh validation provenance in place.  Coverage is updated from
                        // the retained counters and installed step, so revision churn cannot
                        // manufacture a new behavioral candidate.
                        evidence.OccurrenceDigest = grammarReadout.OccurrenceDigest;
                        evidence.Revision = grammarReadout.Revision;
                        UpdateCanonicalCoverage(state, CreateCanonicalCoverageEntry(evidence, policy));
                        state.VerifiedScopes.Remove(canonicalState);
                    }
                    evidence.Revision = grammarReadout.Revision;
                    evidence.OccurrenceDigest = grammarReadout.OccurrenceDigest;
                }
                state.ReadoutCandidateRevision = grammarReadout.Revision;
                state.ReadoutCandidateFingerprint = candidateFingerprint;
                state.ReadoutCandidateState = canonicalState;
                state.ReadoutCandidateOccurrenceDigest = grammarReadout.OccurrenceDigest;
                state.ReadoutCandidateAction = grammarReadout.Action;
                state.ReadoutCandidatePending = false;
                RefreshCanonicalProgramDigest(state, policy);
                state.ReadoutInstalledStep = behavioralCandidateChanged ? Step : state.ReadoutInstalledStep;
                if (behavioralCandidateChanged || readoutRevisionChanged)
                {
                    // occurrence check is a strict tuple over the active program, candidate,
                    // and install revision revision.  A install revision refresh therefore invalidates
                    // the tuple even when behavioral evidence remains intact.
                    state.AssayedFingerprint = 0;
                    state.AssayedReadoutFingerprint = 0;
                    state.VerifiedFingerprint = 0;
                    state.VerifiedReadoutFingerprint = 0;
                    state.VerifiedRevision = GrammarRevisionID.Zero;
                    if (state.Authority == CortexPolicyAuthorities.Grammar)
                    {
                        state.Readmissions++;
                        SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                            readoutRevisionChanged
                                ? CortexPolicyTrialDemotionReasons.ReadoutRevisionChanged
                                : CortexPolicyTrialDemotionReasons.CandidateChanged,
                            candidateRevision: candidateRevision,
                            candidateFingerprint: candidateFingerprint);
                    }
                }
            }
            else
            {
                behavioralCandidateChanged = state.ReadoutCandidateAction != grammarReadout.Action;
                readoutRevisionChanged = state.ReadoutCandidateRevision != candidateRevision;
                state.ReadoutCandidateRevision = grammarReadout.Revision;
                state.ReadoutCandidateFingerprint = candidateFingerprint;
                state.ReadoutCandidateSetDigest = 0;
                state.CanonicalProgramDigestDirty = state.CanonicalCandidates.Count != 0;
                state.ReadoutCandidateState = default;
                state.ReadoutCandidateOccurrenceDigest = grammarReadout.OccurrenceDigest;
                state.ReadoutCandidateAction = grammarReadout.Action;
                state.ReadoutCandidatePending = false;
                if (behavioralCandidateChanged || readoutRevisionChanged)
                {
                    state.ReadoutInstalledStep = behavioralCandidateChanged ? Step : state.ReadoutInstalledStep;
                    if (behavioralCandidateChanged)
                    {
                        state.ShadowComparisons = 0;
                        state.ShadowAgreements = 0;
                        state.EmulationMisses = 0;
                    }
                    state.AssayedFingerprint = 0;
                    state.AssayedReadoutFingerprint = 0;
                    state.VerifiedFingerprint = 0;
                    state.VerifiedReadoutFingerprint = 0;
                    state.VerifiedRevision = GrammarRevisionID.Zero;
                    if (state.Authority == CortexPolicyAuthorities.Grammar)
                    {
                        state.Readmissions++;
                        SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                            readoutRevisionChanged
                                ? CortexPolicyTrialDemotionReasons.ReadoutRevisionChanged
                                : CortexPolicyTrialDemotionReasons.CandidateChanged,
                            candidateRevision: candidateRevision,
                            candidateFingerprint: candidateFingerprint);
                    }
                }
            }
            if (state.RollbackDrillPending && state.Authority == CortexPolicyAuthorities.Grammar)
                RecordRollbackDrill(state, policy, launchpadAction, rawCandidateAction, candidateFingerprint, features, revision);
            if (_capturingPolicyPreparation)
            {
                _preparedPolicyAction = new CortexPolicyActionPreparation(
                    policy, launchpadAction, canonicalState, hasCanonicalState, Step,
                    revision, ReadActivePolicyFingerprint(state), grammarReadout,
                    hasGrammarReadout, rawCandidateAction, candidateRevision,
                    candidateFingerprint, grammarReadout.OccurrenceDigest,
                    decisionContext,
                    readoutAttempt, readoutQuotaDecisionID, boundaryGate,
                    boundaryAllowsProduction, canonicalScopeAllowsProduction);
                return default;
            }

        ConsumePreparedPolicySelection:
            if (state.TrialForcedDivergenceSeed.HasValue
                && !state.PendingForcedTrialIntent.HasSeed
                && (state.TrialExecutionCause == CortexPolicySelectionCauses.TrialOverride
                    || ForkRailRole == CortexForkRailRoles.ForcedNull))
            {
                CortexPolicyPendingForcedTrialIntent skippedIntent = state.PendingForcedTrialIntent;
                CortexPolicyPendingForcedTrialRearmEvaluation skipped =
                    CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                        CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed,
                        in skippedIntent, candidateFingerprint, candidateRevision);
                RecordPendingForcedTrialRearm(policy, in skipped);
            }
            else if (!state.TrialForcedDivergenceSeed.HasValue
                && state.PendingForcedTrialIntent is { HasSeed: true } pendingIntent)
            {
                ulong successorReadoutFingerprint = ReadActivePolicyFingerprint(state);
                CortexPolicyPendingForcedTrialIntent successorIntent = pendingIntent with
                {
                    ReadoutFingerprint = successorReadoutFingerprint,
                    CandidateFingerprint = candidateFingerprint,
                    CandidateRevision = candidateRevision,
                    SuccessorOccurrenceDigest = grammarReadout.OccurrenceDigest,
                    CanonicalState = canonicalState,
                };
                CortexPolicyPendingForcedTrialRearmEvaluation rearm = EvaluatePendingForcedTrialRearm(
                    policy, in successorIntent, hasCanonicalState, in canonicalState,
                    successorReadoutFingerprint, candidateFingerprint,
                    grammarReadout.OccurrenceDigest, candidateRevision);
                RecordPendingForcedTrialRearm(policy, in rearm);
                if (rearm.Allowed)
                {
                    // audit-only remains anchored to the paid source tuple.  Only the
                    // independently verified current tuple becomes executable scope.
                    if (!TryBindVerifiedSuccessorTrialEpoch(policy, in successorIntent, in canonicalState,
                            successorReadoutFingerprint, candidateFingerprint, grammarReadout.OccurrenceDigest,
                            candidateRevision))
                        throw new InvalidDataException("verified forced trial successor binding was rejected atomically");
                    Trace.Cortex.Boundary("policy.trial-intent-rearmed",
                        $"policy={policy} quota={pendingIntent.QuotaID:X} source_readout={pendingIntent.SourceReadoutFingerprint:X16} source_candidate={pendingIntent.SourceCandidateFingerprint:X16} source_revision={pendingIntent.SourceCandidateRevision.Value} successor_readout={successorReadoutFingerprint:X16} successor_candidate={candidateFingerprint:X16} successor_revision={candidateRevision.Value} occurrence={grammarReadout.OccurrenceDigest:X16} state={canonicalState}");
                }
            }
            bool trialOverride = state.TrialForcedDivergenceSeed.HasValue || state.TrialActionOffset != 0;
            trialOverrideUsed = trialOverride;
            int candidateAction;
            if (state.TrialForcedDivergenceSeed is not null
                && state.TrialForcedCandidateCanonical.Length != 0)
            {
                IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
                if (!domain.TryResolveForcedCandidateAction(
                        state.TrialForcedCandidateCanonical,
                        state.TrialForcedCandidateDigest,
                        out candidateAction)
                    || candidateAction == rawCandidateAction
                    || candidateAction == launchpadAction)
                    throw new InvalidDataException("forced policy trial candidate is not the captured divergent proposal");
                state.TrialForcedDivergenceExecutions++;
            }
            else if (state.TrialForcedDivergenceSeed is not null
                && RequirePolicyBoundaryDomain(policy).CanonicalScopeMode == PolicyCanonicalScopeModes.Dynamic)
            {
                throw new InvalidDataException("dynamic forced policy trial lost its authenticated candidate transport");
            }
            else
            {
                candidateAction = state.TrialForcedDivergenceSeed is ulong interventionSeed
                    ? SelectForcedDivergenceAction(
                        rawCandidateAction,
                        launchpadAction,
                        state.Schema.ActionCount,
                        interventionSeed,
                        state.TrialForcedDivergenceExecutions++)
                    : state.TrialActionOffset == 0
                        ? rawCandidateAction
                        : (rawCandidateAction + state.TrialActionOffset) % state.Schema.ActionCount;
            }
            selectedCandidateAction = candidateAction;
            if (hasCanonicalState
                && state.CanonicalCandidates.TryGetValue(canonicalState, out PolicyState.CanonicalCandidateEvidence? currentEvidence))
            {
                currentEvidence.Comparisons++;
                state.ShadowComparisons++;
                if (candidateAction == launchpadAction)
                {
                    currentEvidence.Agreements++;
                    state.ShadowAgreements++;
                }
                else
                {
                    currentEvidence.Misses++;
                    state.EmulationMisses++;
                }
                UpdateCanonicalCoverage(state, CreateCanonicalCoverageEntry(currentEvidence, policy));
            }
            else
            {
                state.ShadowComparisons++;
                if (candidateAction == launchpadAction) state.ShadowAgreements++;
                else state.EmulationMisses++;
            }
            canonicalScopeAllowsProduction = !hasCanonicalState || !policy.Equals(Homeostat.PolicyID)
                || IsVerifiedPolicyScope(
                    policy, in canonicalState, ReadActivePolicyFingerprint(state), candidateFingerprint,
                    grammarReadout.OccurrenceDigest, candidateRevision);
            if (!state.TrialFrozen && state.Schema.Admission == CortexPolicyAdmissionKinds.ExactShadow
                && state.Mode == CortexPolicyModes.Autonomic
                && state.ShadowComparisons >= _config.Learning.Policies.ShadowDecisions
                && state.ShadowComparisons == state.ShadowAgreements)
            {
                CortexPolicyAuthorities priorAuthority = state.Authority;
                SetTrialAuthority(state, CortexPolicyAuthorities.Grammar);
                if (state.Authority == CortexPolicyAuthorities.Grammar && priorAuthority != state.Authority)
                    Trace.Cortex.Boundary("policy.takeover",
                        $"policy={policy} revision={candidateRevision} comparisons={state.ShadowComparisons} agreements={state.ShadowAgreements} fp={candidateFingerprint:X16}");
            }
            if (!state.TrialFrozen && state.Schema.Admission == CortexPolicyAdmissionKinds.ExactShadow
                && state.Authority == CortexPolicyAuthorities.Grammar
                && !trialOverride
                && candidateAction != launchpadAction)
            {
                SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                    CortexPolicyTrialDemotionReasons.ShadowDisagreement,
                    boundaryEvaluated: boundaryGate.Evaluated,
                    boundarySatisfied: boundaryGate.Satisfied,
                    hasGrammarReadout: hasGrammarReadout,
                    candidateRevision: candidateRevision,
                    candidateFingerprint: candidateFingerprint,
                    boundaryGate: boundaryGate);
                state.Readmissions++;
                Trace.Cortex.Warn("policy.repromote", $"policy={policy} reason=emulation-disagreement fp={candidateFingerprint:X16}");
            }
            if (state.Authority == CortexPolicyAuthorities.Grammar
                && boundaryAllowsProduction
                && canonicalScopeAllowsProduction)
            {
                if (_consumingPolicyPreparation && state.SuppressTrialPackets)
                    state.TrialGuardAdmittedCount = checked(state.TrialGuardAdmittedCount + 1);
                executedAction = candidateAction;
                authority = CortexPolicyAuthorities.Grammar;
                state.GrammarExecutions++;
                state.LastGrammarLaunchpadAction = launchpadAction;
                state.LastGrammarAction = candidateAction;
                state.LastGrammarFeatures = CopyNumericFeatures(features);
                if (candidateAction != launchpadAction) state.GrammarDivergentExecutions++;
                if (state.TrialGrammarExecutionsRemaining > 0
                    && --state.TrialGrammarExecutionsRemaining == 0)
                {
                    SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                        CortexPolicyTrialDemotionReasons.TrialQuotaExhausted,
                        boundaryEvaluated: boundaryGate.Evaluated,
                        boundarySatisfied: boundaryGate.Satisfied,
                        hasGrammarReadout: hasGrammarReadout,
                        candidateRevision: candidateRevision,
                        candidateFingerprint: candidateFingerprint,
                        boundaryGate: boundaryGate,
                        emitTransition: false);
                    ulong? forcedSeedBefore = state.TrialForcedDivergenceSeed;
                    int actionOffsetBefore = state.TrialActionOffset;
                    bool suppressBefore = state.SuppressTrialPackets;
                    state.TrialForcedDivergenceSeed = null;
                    TracePolicyTrialAuthorityTransition(
                        state.Schema.Policy,
                        state,
                        CortexPolicyAuthorities.Grammar,
                        CortexPolicyAuthorities.Shadow,
                        CortexPolicyTrialDemotionReasons.TrialQuotaExhausted,
                        boundaryEvaluated: boundaryGate.Evaluated,
                        boundarySatisfied: boundaryGate.Satisfied,
                        forcedSeedBefore: forcedSeedBefore,
                        forcedSeedAfter: state.TrialForcedDivergenceSeed,
                        actionOffsetBefore: actionOffsetBefore,
                        actionOffsetAfter: state.TrialActionOffset,
                        suppressBefore: suppressBefore,
                        suppressAfter: state.SuppressTrialPackets,
                        boundaryGate: boundaryGate,
                        hasGrammarReadout: hasGrammarReadout,
                        candidateRevision: candidateRevision,
                        candidateFingerprint: candidateFingerprint,
                        trialOverrideClearedOnRevisionDrift: false,
                        remainingBefore: 0,
                        remainingAfter: state.TrialGrammarExecutionsRemaining);
                }
            }
            else if (state.Authority == CortexPolicyAuthorities.Launchpad
                && state.AuthorityCeiling == CortexPolicyAuthorities.Launchpad)
            {
                // A Launchpad ceiling performs the Shadow readout and emits its teacher packet,
                // but the authority rail is reflex-only. Do not leak the observed candidate into
                // the execution receipt when the configured ceiling forbids Shadow authority.
                rawCandidateAction = -1;
                selectedCandidateAction = -1;
                authority = CortexPolicyAuthorities.Launchpad;
            }
            else
            {
                authority = CortexPolicyAuthorities.Shadow;
            }
        }

        if (_consumingPolicyPreparation
            && state.SuppressTrialPackets
            && hasGrammarReadout
            && state.TrialExecutionCause != CortexPolicySelectionCauses.Launchpad
            && state.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted)
            state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.GuardDenied;

        if (_capturingPolicyPreparation && _preparedPolicyAction is null)
        {
            _preparedPolicyAction = new CortexPolicyActionPreparation(
                policy, launchpadAction, canonicalState, hasCanonicalState, Step,
                revision, ReadActivePolicyFingerprint(state), grammarReadout,
                hasGrammarReadout, rawCandidateAction, candidateRevision,
                candidateFingerprint, grammarReadout.OccurrenceDigest,
                decisionContext, readoutAttempt, readoutQuotaDecisionID,
                boundaryGate, boundaryAllowsProduction, canonicalScopeAllowsProduction);
            return default;
        }
        CortexPolicyDecisionID decisionID = new(_nextPolicyDecisionID++);
        if (state.PriorAction == executedAction && state.LastAction >= 0 && state.LastAction != executedAction)
            state.ActionReversals++;
        state.PriorAction = state.LastAction;
        state.LastAction = executedAction;
        state.Decisions++;
        state.ActionExecutions[executedAction]++;
        if (authority != CortexPolicyAuthorities.Grammar) state.LaunchpadExecutions++;
        ulong readoutCandidateOccurrenceDigest = rawCandidateAction >= 0
            ? state.ReadoutCandidateOccurrenceDigest
            : 0;
        ulong readoutCandidateFingerprint = rawCandidateAction >= 0
            ? state.ReadoutCandidateFingerprint
            : 0;
        GrammarRevisionID executionRevision = authenticatedPaidTrialEpoch
            ? state.ReadoutCandidateRevision
            : revision;
        CortexPolicyDecision decision = new(
            decisionID,
            policy,
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(
                launchpadAction, rawCandidateAction, selectedCandidateAction, executedAction,
                authority, executionRevision,
                readoutCandidateOccurrenceDigest,
                readoutCandidateFingerprint,
                ReadActivePolicyFingerprint(state),
                trialOverride: trialOverrideUsed),
            in decisionContext);
        state.LastDecisionReadout = decision.Readout;
        state.LastDecisionID = decision.DecisionID;
        bool persistTrialDecision = state.SuppressTrialPackets
            && decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride;
        if (_consumingPolicyPreparation
            && state.SuppressTrialPackets
            && hasGrammarReadout
            && state.TrialExecutionCause != CortexPolicySelectionCauses.Launchpad
            && decision.RawCandidateAction >= 0)
        {
            state.TrialLastRequest = decision;
            state.TrialLastRequestStep = Step;
        }
        if (state.SuppressTrialPackets && decision.SelectionCause == state.TrialExecutionCause)
        {
            if (state.HistoricalTrialExecution.IsPresent)
                throw new InvalidDataException("completed policy trial execution cannot be overwritten");
            if (state.PendingForcedTrialIntent.HasSeed
                && state.PendingForcedTrialIntent.IsBound
                && (state.ActiveTrialQuotaID.Value == 0
                    || state.ActiveTrialQuotaID.Value != state.PendingForcedTrialIntent.QuotaID))
                throw new InvalidDataException("paid forced trial execution lacks its bound active quota identity");
            IPolicyBoundaryDomain? executionDomain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredExecutionDomain)
                ? registeredExecutionDomain : null;
            bool executionRequiresScope = RequiresCanonicalScope(executionDomain);
            PolicyCanonicalStateID executionState = state.ReadoutCandidateState;
            bool executionScopePresent = state.VerifiedScopes.TryGetValue(executionState, out PolicyVerifiedScopeEntry executionScope);
            if (!ValidateCanonicalState(executionDomain, policy, in executionState)
                || executionRequiresScope && !executionScopePresent)
                throw new InvalidDataException("configured trial execution lacks its canonical scope");
            // The paid Homeostat child may execute a successor revision issued after the source fork.
            // Bind that exact verified scope to the child tape before terminal audit-only freezes it.
            if (policy.Equals(Homeostat.PolicyID)
                && decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride)
            {
                PolicyCanonicalStateID executionScopeState = executionScope.State;
                AppendPolicyOccurrenceCheckScope(
                    policy, executionScope.ReadoutFingerprint, executionScope.CandidateFingerprint,
                    executionScope.OccurrenceDigest, executionScope.Revision, in executionScopeState);
            }
            state.TrialExecutionCorroboration = decision;
            state.TrialExecutionReadoutFingerprint = ReadActivePolicyFingerprint(state);
            state.TrialExecutionStep = Step;
            state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
            state.HistoricalTrialExecution = new(
                state.ActiveTrialQuotaID,
                state.TrialExecutionCause,
                state.TrialExecutionOutcome,
                state.TrialRequestCount,
                state.TrialGuardAdmittedCount,
                state.TrialLastRequest?.DecisionID ?? default,
                state.TrialLastRequest?.Readout ?? default,
                state.TrialLastRequestStep,
                decision.DecisionID,
                decision.Readout,
                state.TrialExecutionStep,
                state.TrialExecutionReadoutFingerprint,
                executionScope);
            CloseCompletedPolicyTrialEpoch(state);
        }
        // TrialOverride is a audit-only-bearing action, not an invisible control
        // operation. The completed epoch releases SuppressTrialPackets above, so
        // capture this admission before closure and persist its ordinary event.
        if (state.Mode != CortexPolicyModes.Off && (!state.SuppressTrialPackets || persistTrialDecision))
        {
            if (hasCanonicalState)
                AppendPolicyDecision(in decision, in canonicalState, features, state.Schema.ActionCount, in decisionContext, readoutAttempt, readoutQuotaDecisionID);
            else
                AppendPolicyDecision(in decision, features, state.Schema.ActionCount, excludedReadoutMetricIDs, in decisionContext, readoutAttempt, readoutQuotaDecisionID);
        }
        if (state.PendingForcedTrialIntent.HasSeed && decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride)
            state.PendingForcedTrialIntent = default;
        return decision;
    }

    private static void CloseCompletedPolicyTrialEpoch(PolicyState state)
    {
        // HistoricalTrialExecution owns the completed audit-only before these active
        // controls are released; continuation must never preserve both epochs.
        state.ActiveTrialQuotaID = default;
        state.SuppressTrialPackets = false;
        state.PendingForcedTrialIntent = default;
        state.TrialGrammarExecutionsRemaining = -1;
        state.TrialActionOffset = 0;
        state.TrialForcedDivergenceSeed = null;
        state.TrialForcedDivergenceExecutions = 0;
    }

    public void ResolvePolicyOutcome(
        in CortexPolicyDecision decision,
        ReadOnlySpan<MetricSample> outcomes,
        bool invariantClean,
        long conservedCost)
    {
        PolicyState state = GetPolicy(decision.Policy);
        if (decision.Authority > state.AuthorityCeiling)
            throw new InvalidDataException($"policy '{decision.Policy}' decision authority {decision.Authority} exceeds configured ceiling {state.AuthorityCeiling}");
        if (outcomes.Length != state.Schema.OutcomeCount)
            throw new ArgumentException($"policy '{decision.Policy}' expected {state.Schema.OutcomeCount} outcomes, received {outcomes.Length}", nameof(outcomes));
        if (conservedCost < 0) throw new ArgumentOutOfRangeException(nameof(conservedCost));
        state.Outcomes++;
        state.ConservedCost = checked(state.ConservedCost + conservedCost);
        if (decision.Authority == CortexPolicyAuthorities.Grammar)
        {
            state.GrammarOutcomes++;
            if (invariantClean && HasPositiveOutcome(outcomes))
            {
                state.PaidGrammarOutcomes++;
                if (!state.RollbackDrillCompleted) state.RollbackDrillPending = true;
            }
        }
        if (!invariantClean && decision.Authority == CortexPolicyAuthorities.Grammar)
        {
            SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                CortexPolicyTrialDemotionReasons.InvariantFailure,
                boundaryEvaluated: false,
                boundarySatisfied: false);
            state.Readmissions++;
            Trace.Cortex.Warn("policy.repromote", $"policy={decision.Policy} reason=invariant-failure");
        }
        if (state.Mode != CortexPolicyModes.Off && !state.SuppressTrialPackets)
            AppendPolicyOutcome(in decision, outcomes, invariantClean, conservedCost);
    }

    /// Closes a pending decision when its parent run ends before an outcome can exist. This is a
    /// terminal audit-only transition: it records no outcome, cost, reward, training signal, or authority change.
    /// Callers must not substitute a zero-valued ordinary outcome, which would look like evidence to the policy.
    public void ResolveCensoredPolicyOutcome(in CortexPolicyDecision decision)
    {
        PolicyState state = GetPolicy(decision.Policy);
        if (decision.Authority > state.AuthorityCeiling)
            throw new InvalidDataException($"policy '{decision.Policy}' decision authority {decision.Authority} exceeds configured ceiling {state.AuthorityCeiling}");
        state.CensoredOutcomes++;
        Trace.Cortex.Boundary("policy.outcome-censored", $"policy={decision.Policy} decision={decision.DecisionID} authority={decision.Authority}");
    }

    private PolicyState GetPolicy(CortexPolicyID policy)
        => _policies.TryGetValue(policy, out PolicyState? state)
            ? state
            : throw new KeyNotFoundException($"policy '{policy}' is not registered");

    private static bool RequiresCanonicalScope(IPolicyBoundaryDomain? domain)
        => domain is not null && domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None;

    private static bool ValidateCanonicalState(
        IPolicyBoundaryDomain? domain,
        CortexPolicyID policy,
        in PolicyCanonicalStateID state)
        => domain is not null ? domain.ValidateCanonicalState(in state) : state.IsValidFor(policy);

    internal bool TryReadPolicyReadout(CortexPolicyID policy, out CortexPolicyReadoutReceipt receipt)
    {
        PolicyState state = GetPolicy(policy);
        bool currentCanonicalStateValidated = state.ReadoutCandidateState.Version == 0
            && !state.ReadoutCandidatePending
            || (_runtimeInstallRevision is { } installRevision
                && !state.ReadoutCandidatePending
                && state.ReadoutCandidateRevision == installRevision.Revision
                && state.CanonicalCandidates.TryGetValue(state.ReadoutCandidateState, out PolicyState.CanonicalCandidateEvidence? candidate)
                && candidate.Revision == installRevision.Revision
                && candidate.Action == state.ReadoutCandidateAction
                && candidate.CandidateFingerprint == state.ReadoutCandidateFingerprint);
        receipt = new CortexPolicyReadoutReceipt(
            state.ReadoutCandidateRevision,
            ReadActivePolicyFingerprint(state),
            state.ReadoutCache.Count,
            state.ShadowComparisons,
            state.ShadowAgreements,
            state.EmulationMisses,
            state.ReadoutCandidateOccurrenceDigest,
            state.ReadoutCandidateFingerprint,
            default,
            state.ReadoutCandidateState);
        bool ready = currentCanonicalStateValidated
            && receipt.Revision != GrammarRevisionID.Zero
            && receipt.Fingerprint != 0
            && receipt.CachedContexts > 0;
        // Canonical coverage is a receipt-side expansion, not part of the cheap readout
        // validity gates. Denied/suppressed reads still return their scalar provenance,
        // while only a usable readout pays for the canonical-state walk.
        if (ready)
            receipt = receipt with { CanonicalCoverage = ReadCanonicalCoverage(state, policy) };
        return ready;
    }

    internal PolicyCanonicalCoverageReceipt ReadCanonicalCoverage(CortexPolicyID policy)
        => ReadCanonicalCoverage(GetPolicy(policy), policy);

    private PolicyCanonicalCoverageReceipt ReadCanonicalCoverage(PolicyState state, CortexPolicyID policy)
    {
        PolicyCanonicalStateID[] required;
        if (_policyBoundaryObligations.ContainsKey(policy))
            required = RequirePolicyBoundaryDomain(policy).CanonicalStates;
        else if (TryGetPolicyBoundaryDomain(policy, out IPolicyBoundaryDomain domain))
            required = domain.CanonicalStates;
        else
            required = [];
        if (required.Length == 0)
            return PolicyCanonicalCoverageReceipt.Create(
                required, new Dictionary<PolicyCanonicalStateID, PolicyCanonicalCoverageEntry>(),
                state.ShadowComparisons, state.ShadowAgreements, state.EmulationMisses);
        PolicyCanonicalCoverageIndex index = state.CanonicalCoverageIndex ??= new PolicyCanonicalCoverageIndex();
        if (!index.Matches(required))
        {
            Dictionary<PolicyCanonicalStateID, PolicyCanonicalCoverageEntry> candidates = new(state.CanonicalCandidates.Count);
            foreach (PolicyState.CanonicalCandidateEvidence evidence in state.CanonicalCandidates.Values)
                candidates[evidence.State] = CreateCanonicalCoverageEntry(evidence, policy);
            index.Ensure(required, candidates);
        }
        PolicyCanonicalCoverageReceipt coverage = index.Create(
            state.ShadowComparisons, state.ShadowAgreements, state.EmulationMisses);
        state.CanonicalCoverageCache = coverage;
        state.CanonicalCoverageCacheVersion = state.CanonicalCoverageVersion;
        return coverage;
    }

    private static PolicyCanonicalCoverageEntry CreateCanonicalCoverageEntry(
        PolicyState.CanonicalCandidateEvidence evidence,
        CortexPolicyID policy)
    {
        bool valid = evidence.State.Policy.Equals(policy)
            && evidence.CandidateFingerprint != 0
            && evidence.OccurrenceDigest != 0
            && evidence.Revision != global::Cogito.Grammar.GrammarRevisionID.Zero
            && evidence.Action >= 0
            && evidence.Comparisons >= 0
            && evidence.Agreements >= 0
            && evidence.Misses >= 0
            && evidence.Agreements <= evidence.Comparisons
            && evidence.Misses <= evidence.Comparisons;
        return new(
            evidence.State,
            valid,
            valid ? evidence.Action : -1,
            valid ? evidence.CandidateFingerprint : 0,
            valid ? evidence.OccurrenceDigest : 0,
            valid ? evidence.Revision : global::Cogito.Grammar.GrammarRevisionID.Zero,
            valid ? evidence.OriginRevision : global::Cogito.Grammar.GrammarRevisionID.Zero,
            valid ? evidence.InstalledStep : 0,
            valid ? evidence.Comparisons : 0,
            valid ? evidence.Agreements : 0,
            valid ? evidence.Misses : 0);
    }

    internal bool TryGrantPolicyAuthority(CortexPolicyID policy, ulong candidateFingerprint)
    {
        PolicyState state = GetPolicy(policy);
        // Compatibility entry point for older callers.  Candidate identity alone is
        // never sufficient: derive the complete succession tuple from the live state
        // and pass it through the same readout/candidate/revision gate as every other
        // admission path.  In particular, a restored candidate-only receipt cannot
        // regain Grammar authority without a strict verified readout corroboration.
        return TryGrantVerifiedPolicySuccession(
            policy,
            ReadActivePolicyFingerprint(state),
            candidateFingerprint,
            state.ReadoutCandidateRevision);
    }

    /// Grant Grammar only for the exact readout revision and candidate identity
    /// that the fork-trial occurrence-check proved.  install revision drift never inherits
    /// authority from a prior revision.
    internal bool TryGrantVerifiedPolicySuccession(
        CortexPolicyID policy,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        GrammarRevisionID revision)
    {
        bool boundaryCapable = _policyBoundaryObligations.ContainsKey(policy)
            || _policyBoundaryDomains.ContainsKey(policy);
        IPolicyBoundaryDomain? domain = boundaryCapable
            ? RequirePolicyBoundaryDomain(policy)
            : null;
        PolicyState state = GetPolicy(policy);
        if (state.Schema.Admission != CortexPolicyAdmissionKinds.Verified
            || state.Mode != CortexPolicyModes.Autonomic
            || state.AuthorityCeiling < CortexPolicyAuthorities.Grammar
            || readoutFingerprint == 0 || candidateFingerprint == 0 || revision == GrammarRevisionID.Zero
            || ReadActivePolicyFingerprint(state) != readoutFingerprint
            || state.ReadoutCandidateFingerprint != candidateFingerprint
            || state.ReadoutCandidateRevision != revision
            || state.VerifiedReadoutFingerprint != readoutFingerprint
            || state.VerifiedFingerprint != candidateFingerprint
            || state.VerifiedRevision != revision)
            return false;
        // A frozen rail may verify the exact successor, but it cannot cross the
        // authority boundary. This refusal must precede SetTrialAuthority: that
        // mutation is also the adaptation-transition receipt.
        if (state.TrialFrozen)
        {
            Trace.Cortex.Boundary("policy.verified-succession-refused",
                $"policy={policy} revision={revision.Value} readout={readoutFingerprint:X16} candidate={candidateFingerprint:X16} reason=trial-frozen");
            return false;
        }
        if (RequiresCanonicalScope(domain))
        {
            PolicyCanonicalStateID stateID = state.ReadoutCandidateState;
            if (!domain.ValidateCanonicalState(in stateID)
                || !state.VerifiedScopes.TryGetValue(stateID, out PolicyVerifiedScopeEntry scope)
                || !scope.IsValid
                || scope.ReadoutFingerprint != readoutFingerprint
                || scope.CandidateFingerprint != candidateFingerprint
                || scope.OccurrenceDigest != state.ReadoutCandidateOccurrenceDigest
                || scope.Revision != revision)
                return false;
        }
        SetTrialAuthority(state, CortexPolicyAuthorities.Grammar);
        Trace.Cortex.Boundary("policy.verified-succession",
            $"policy={policy} revision={revision.Value} readout={readoutFingerprint:X16} candidate={candidateFingerprint:X16}");
        return true;
    }

    internal bool IsPolicyReadoutReady(CortexPolicyID policy, ulong activeProgramFingerprint)
    {
        PolicyState state = GetPolicy(policy);
        return ReadActivePolicyFingerprint(state) == activeProgramFingerprint
            && state.ReadoutLearnerEvidenceTrusted
            && state.ShadowComparisons >= _config.Learning.Policies.ShadowDecisions
            && state.ShadowComparisons == state.ShadowAgreements
            && state.EmulationMisses == 0;
    }

    internal bool TryGrantVerifiedPolicyScope(
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong occurrenceDigest,
        GrammarRevisionID revision)
    {
        bool boundaryCapable = _policyBoundaryObligations.ContainsKey(policy)
            || _policyBoundaryDomains.ContainsKey(policy);
        IPolicyBoundaryDomain? domain = boundaryCapable
            ? RequirePolicyBoundaryDomain(policy)
            : null;
        PolicyState state = GetPolicy(policy);
        if (!(domain is not null ? domain.ValidateCanonicalState(in canonicalState) : canonicalState.IsValidFor(policy))
            || readoutFingerprint == 0 || candidateFingerprint == 0
            || occurrenceDigest == 0 || revision == GrammarRevisionID.Zero
            || ReadActivePolicyFingerprint(state) != readoutFingerprint
            || state.ReadoutCandidateState != canonicalState
            || state.ReadoutCandidateFingerprint != candidateFingerprint
            || state.ReadoutCandidateOccurrenceDigest != occurrenceDigest
            || state.ReadoutCandidateRevision != revision
            || !state.CanonicalCandidates.TryGetValue(canonicalState, out PolicyState.CanonicalCandidateEvidence? candidate)
            || candidate.Action != state.ReadoutCandidateAction
            || candidate.CandidateFingerprint != candidateFingerprint
            || candidate.OccurrenceDigest != occurrenceDigest
            || candidate.Revision != revision)
            return false;
        state.VerifiedScopes[canonicalState] = new PolicyVerifiedScopeEntry(
            canonicalState, readoutFingerprint, candidateFingerprint, occurrenceDigest, revision);
        state.VerifiedReadoutFingerprint = readoutFingerprint;
        state.VerifiedFingerprint = candidateFingerprint;
        state.VerifiedRevision = revision;
        return true;
    }

    internal void AppendPolicyOccurrenceCheckScope(
        CortexPolicyID policy,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong occurrenceDigest,
        GrammarRevisionID revision,
        in PolicyCanonicalStateID canonicalState)
    {
        if (_runtimeTape is null || _runtimeJournal is null)
            throw new InvalidOperationException("policy occurrence check scope requires a bound runtime tape and journal");
        TapePacketCreator.AppendPolicyOccurrenceCheckScope(
            _runtimeTape, _runtimeJournal, Step, policy,
            readoutFingerprint, candidateFingerprint,
            occurrenceDigest, revision,
            in canonicalState);
    }

    internal bool IsVerifiedPolicyScope(
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong occurrenceDigest,
        GrammarRevisionID revision)
    {
        PolicyState state = GetPolicy(policy);
        IPolicyBoundaryDomain? domain = _policyBoundaryObligations.ContainsKey(policy)
            || _policyBoundaryDomains.ContainsKey(policy)
            ? RequirePolicyBoundaryDomain(policy)
            : null;
        PolicyVerifiedScopeEntry scope;
        if (state.VerifiedScopes.TryGetValue(canonicalState, out PolicyVerifiedScopeEntry activeScope))
            scope = activeScope;
        else
            return false;
        return (domain is not null ? domain.ValidateCanonicalState(in canonicalState) : canonicalState.IsValidFor(policy))
            && scope.IsValid
            && scope.State == canonicalState
            && scope.ReadoutFingerprint == readoutFingerprint
            && scope.CandidateFingerprint == candidateFingerprint
            && scope.OccurrenceDigest == occurrenceDigest
            && scope.Revision == revision
            && state.CanonicalCandidates.TryGetValue(canonicalState, out PolicyState.CanonicalCandidateEvidence? candidate)
            && candidate.CandidateFingerprint == candidateFingerprint
            && candidate.OccurrenceDigest == occurrenceDigest
            && candidate.Revision == revision;
    }

    private bool TryAuthenticatePaidTrialEpoch(
        PolicyState state,
        CortexPolicyID policy,
        CortexPolicyQuotaDecisionID quotaID,
        string? auditOnlyDigest,
        CortexPolicySelectionCauses executionCause,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong occurrenceDigest,
        GrammarRevisionID candidateRevision,
        out PolicyVerifiedScopeEntry scope)
    {
        scope = default;
        IPolicyBoundaryDomain? domain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredDomain)
            ? registeredDomain : null;
        bool requiresScope = RequiresCanonicalScope(domain);
        if (!TryResolvePolicyTrialAuthority(executionCause, out CortexPolicyAuthorities requiredAuthority)
            || !state.Schema.Policy.Equals(policy)
            || !state.SuppressTrialPackets
            || !state.ActiveTrialQuotaID.Equals(quotaID)
            || state.Authority != requiredAuthority
            || requiredAuthority > state.AuthorityCeiling
            || state.ReadoutCandidatePending
            || !ValidateCanonicalState(domain, policy, in canonicalState)
            || readoutFingerprint == 0 || candidateFingerprint == 0 || occurrenceDigest == 0
            || candidateRevision == GrammarRevisionID.Zero
            || state.ReadoutCandidateSetDigest != readoutFingerprint
            || ReadActivePolicyFingerprint(state) != readoutFingerprint
            || state.ReadoutCandidateFingerprint != candidateFingerprint
            || state.ReadoutCandidateOccurrenceDigest != occurrenceDigest
            || state.ReadoutCandidateRevision != candidateRevision
            || state.ReadoutCandidateState != canonicalState
            || state.TrialExecutionCause != executionCause
            || requiresScope && (!state.VerifiedScopes.TryGetValue(canonicalState, out scope)
                || !scope.IsValid
                || scope.State != canonicalState
                || scope.ReadoutFingerprint != readoutFingerprint
                || scope.CandidateFingerprint != candidateFingerprint
                || scope.OccurrenceDigest != occurrenceDigest
                || scope.Revision != candidateRevision
                || !state.CanonicalCandidates.TryGetValue(canonicalState, out PolicyState.CanonicalCandidateEvidence? candidate)
                || candidate.CandidateFingerprint != candidateFingerprint
                || candidate.OccurrenceDigest != occurrenceDigest
                || candidate.Revision != candidateRevision)
            || !TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID, auditOnlyDigest))
        {
            scope = default;
            return false;
        }
        return true;
    }

    private static bool TryResolvePolicyTrialAuthority(
        CortexPolicySelectionCauses cause,
        out CortexPolicyAuthorities authority)
    {
        authority = cause switch
        {
            CortexPolicySelectionCauses.Launchpad => CortexPolicyAuthorities.Launchpad,
            CortexPolicySelectionCauses.ShadowCandidate => CortexPolicyAuthorities.Shadow,
            CortexPolicySelectionCauses.GrammarCandidate or CortexPolicySelectionCauses.TrialOverride
                => CortexPolicyAuthorities.Grammar,
            _ => default,
        };
        return cause is CortexPolicySelectionCauses.Launchpad
            or CortexPolicySelectionCauses.ShadowCandidate
            or CortexPolicySelectionCauses.GrammarCandidate
            or CortexPolicySelectionCauses.TrialOverride;
    }

    internal bool TryFundPolicyTrial(
        CortexPolicyID policy,
        in CortexPolicyTrialAuthorityIdentity authorityIdentity,
        int horizonSteps,
        int armCount,
        out CortexPolicyTrialQuotaDecision decision)
    {
        decision = DecidePolicyTrialQuota(policy, in authorityIdentity, horizonSteps, armCount);
        return decision.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused;
    }

    internal CortexPolicyTrialQuotaDecision DecidePolicyTrialQuota(
        CortexPolicyID policy,
        in CortexPolicyTrialAuthorityIdentity authorityIdentity,
        int horizonSteps,
        int armCount,
        Func<CortexPolicyQuotaDecisionID, string?>? preparePaidLease = null)
    {
        if (horizonSteps <= 0) throw new ArgumentOutOfRangeException(nameof(horizonSteps));
        if (armCount <= 0) throw new ArgumentOutOfRangeException(nameof(armCount));
        long planned = checked((long)horizonSteps * armCount);
        CortexPolicyTrialAllocation allocation = ReadPolicyTrialAllocation(policy);
        if (!authorityIdentity.IsValid)
            throw new ArgumentException("quota requires a non-zero active program, candidate, and revision identity", nameof(authorityIdentity));
        ulong readoutFingerprint = authorityIdentity.ActiveProgramFingerprint.Value;
        ulong candidateFingerprint = authorityIdentity.CandidateFingerprint.Value;
        GrammarRevisionID candidateRevision = authorityIdentity.CandidateRevision;
        CortexPolicyQuotaDecisionID quotaID = CreateQuotaDecisionID(policy, candidateFingerprint, Step, horizonSteps, armCount, readoutFingerprint, allocation.Digest);
        if (_policyTrialQuotaByID.TryGetValue(quotaID, out CortexPolicyTrialQuotaDecision paid))
        {
            if (!TryAuthenticatePaidAuditOnly(ref paid))
                throw new InvalidDataException($"paid policy trial {quotaID} has no authenticated seed audit-only");
            _policyTrialQuotaByID[quotaID] = paid;
            InvalidatePolicyTrialReconcileMemo();
            CortexPolicyTrialQuotaDecision reused = paid with
            {
                Decision = CortexPolicyQuotaDecisions.Reused,
                UsedSteps = 0,
                RemainingQuota = ReadPolicyTrialRemainingQuota(policy),
                AllocationIdentity = paid.AllocationIdentity,
                AllocationDigest = paid.AllocationDigest,
                AllocationArmSteps = paid.AllocationArmSteps,
            };
            _policyTrialQuotaDecisions.Add(reused);
            AppendPolicyTrialQuota(in reused);
            if (_runtimeTape is not null && _runtimeJournal is not null)
            {
                TapeEventID eventID = TapePacketCreator.AppendPolicyTrialQuota(_runtimeTape, _runtimeJournal, Step, in reused);
                EmitLoopClosurePolicyQuota(eventID, in reused);
            }
            return reused;
        }

        PolicyState state = GetPolicy(policy);
        ulong activeProgramFingerprint = ReadActivePolicyFingerprint(state);
        ulong activeCandidateFingerprint = ReadActivePolicyCandidateFingerprint(state);
        GrammarRevisionID activeCandidateRevision = state.ReadoutCandidateRevision;
        PolicyCanonicalStateID authorityState = authorityIdentity.CanonicalState;
        bool programMatches = activeProgramFingerprint == readoutFingerprint;
        bool candidateMatches = activeCandidateFingerprint == candidateFingerprint;
        bool revisionMatches = activeCandidateRevision == candidateRevision;
        bool scopeMatches = !policy.Equals(Homeostat.PolicyID)
            || authorityIdentity.HasCanonicalState
                && IsVerifiedPolicyScope(policy, in authorityState, readoutFingerprint,
                    candidateFingerprint, state.ReadoutCandidateOccurrenceDigest, candidateRevision);
        bool identityMatches = programMatches && candidateMatches && revisionMatches && scopeMatches;
        CortexPolicyTrialCandidateStates candidateState = state.ReadoutCandidatePending
            ? CortexPolicyTrialCandidateStates.Pending
            : identityMatches
                ? CortexPolicyTrialCandidateStates.Active
                : CortexPolicyTrialCandidateStates.Mismatch;
        CortexPolicyTrialDenialReasons denialReason = candidateState switch
        {
            CortexPolicyTrialCandidateStates.Pending => CortexPolicyTrialDenialReasons.CandidatePending,
            CortexPolicyTrialCandidateStates.Mismatch when !programMatches => CortexPolicyTrialDenialReasons.ProgramMismatch,
            CortexPolicyTrialCandidateStates.Mismatch when !candidateMatches => CortexPolicyTrialDenialReasons.CandidateMismatch,
            CortexPolicyTrialCandidateStates.Mismatch when !scopeMatches => CortexPolicyTrialDenialReasons.CanonicalScopeMissing,
            CortexPolicyTrialCandidateStates.Mismatch => CortexPolicyTrialDenialReasons.CandidateRevisionMismatch,
            _ => CortexPolicyTrialDenialReasons.None,
        };
        if (denialReason == CortexPolicyTrialDenialReasons.None
            && policy.Equals(Homeostat.PolicyID)
            && (!allocation.IsPresent || allocation.Authority != state.AuthorityCeiling || state.AuthorityCeiling != CortexPolicyAuthorities.Grammar))
            denialReason = CortexPolicyTrialDenialReasons.AllocationUnavailable;
        int originStep = identityMatches ? state.ReadoutInstalledStep : -1;
        int requiredStep = identityMatches && originStep >= 0 ? checked(originStep + horizonSteps) : -1;
        long remaining = ReadPolicyTrialRemainingQuota(policy);
        if (denialReason == CortexPolicyTrialDenialReasons.None && requiredStep > Step)
            denialReason = CortexPolicyTrialDenialReasons.MaturityWindow;
        else if (denialReason == CortexPolicyTrialDenialReasons.None && remaining < planned)
            denialReason = CortexPolicyTrialDenialReasons.InsufficientFuel;
        CortexPolicyQuotaDecisions result = denialReason == CortexPolicyTrialDenialReasons.None
            ? CortexPolicyQuotaDecisions.Paid : CortexPolicyQuotaDecisions.Denied;
        string auditOnlyDigest = result == CortexPolicyQuotaDecisions.Paid
            ? preparePaidLease?.Invoke(quotaID) ?? ""
            : "";
        if (result == CortexPolicyQuotaDecisions.Paid
            && preparePaidLease is not null
            && auditOnlyDigest.Length == 0)
            throw new InvalidDataException($"paid policy trial {quotaID} has no durable seed audit-only");
        long held = result == CortexPolicyQuotaDecisions.Paid ? planned : 0;
        long used = held;

        // The row records the POST-charge quota (the reserve it carries is already subtracted) even
        // though the in-memory charge lands after the durable append — the recovery-ordering law moved
        // the append, not the row's allocation tracking frame.
        CortexPolicyTrialQuotaDecision decision = new(
            quotaID, policy, candidateFingerprint, Step, horizonSteps, armCount,
            planned, held, result, used, Math.Max(0, remaining - held))
        {
            CandidateState = candidateState,
            DenialReason = denialReason,
            CandidateOriginStep = originStep,
            CandidateCurrentStep = Step,
            CandidateRequiredStep = requiredStep,
            CandidateRevision = candidateRevision,
            AllocationIdentity = allocation.Identity,
            AllocationDigest = allocation.Digest,
            AllocationArmSteps = allocation.ArmSteps,
            SeedAuditOnlyDigest = auditOnlyDigest,
            ReadoutFingerprint = readoutFingerprint,
            CanonicalState = authorityState,
        };
        // The durable row is the recovery record. Persist it before charging the
        // in-memory allocation state so a process loss cannot leave an unrecorded charge.
        if (result == CortexPolicyQuotaDecisions.Paid)
            AppendPolicyTrialQuotaDurable(in decision);
        else
            AppendPolicyTrialQuota(in decision);
        _policyTrialQuotaDecisions.Add(decision);
        InvalidatePolicyTrialReconcileMemo();
        if (result == CortexPolicyQuotaDecisions.Paid)
        {
            _policyTrialQuotaByID.Add(quotaID, decision);
            _policyTrialHeldSteps = checked(_policyTrialHeldSteps + held);
            _policyTrialUsedSteps = checked(_policyTrialCompletedUsedSteps + _policyTrialHeldSteps);
            _fundedPolicyTrials.Add(BuildQuotaKey(policy, candidateFingerprint, Step, planned));
        }
        if (_runtimeTape is not null && _runtimeJournal is not null)
        {
            TapeEventID eventID = TapePacketCreator.AppendPolicyTrialQuota(_runtimeTape, _runtimeJournal, Step, in decision);
            if (result == CortexPolicyQuotaDecisions.Paid)
                EmitLoopClosurePolicyQuota(eventID, in decision);
        }
        if (result == CortexPolicyQuotaDecisions.Paid)
            Trace.Cortex.Boundary("policy.trial-paid",
                $"id={quotaID} policy={policy} program={readoutFingerprint:X16} candidate={candidateFingerprint:X16} revision={candidateRevision.Value} expected_program={activeProgramFingerprint:X16} expected_candidate={activeCandidateFingerprint:X16} expected_revision={activeCandidateRevision.Value} horizon={horizonSteps} arms={armCount} planned={planned} held={held} remaining={decision.RemainingQuota} candidate={candidateState} denial={denialReason}");
        else
            Trace.Cortex.Boundary("policy.trial-denied",
                $"id={quotaID} policy={policy} program={readoutFingerprint:X16} candidate={candidateFingerprint:X16} revision={candidateRevision.Value} expected_program={activeProgramFingerprint:X16} expected_candidate={activeCandidateFingerprint:X16} expected_revision={activeCandidateRevision.Value} horizon={horizonSteps} arms={armCount} planned={planned} remaining={decision.RemainingQuota} candidate={candidateState} denial={denialReason}");
        return decision;
    }

    /// Reuse a durable paid lease after a process-loss recovery.  The quota identity is
    /// authoritative; recomputing it from the current step would mint a second reservation when
    /// the continuation resumed one step later.  Reuse never changes the conserved reservation.
    internal CortexPolicyTrialQuotaDecision ReusePolicyTrialQuota(
        in CortexPolicyTrialQuotaDecision paid)
    {
        if (paid.Decision != CortexPolicyQuotaDecisions.Paid
            || !_policyTrialQuotaByID.TryGetValue(paid.QuotaDecisionID, out CortexPolicyTrialQuotaDecision admitted)
            || admitted.Decision != CortexPolicyQuotaDecisions.Paid)
            throw new InvalidDataException("cannot reuse an unknown policy trial quota lease");
        if (!TryAuthenticatePaidAuditOnly(ref admitted))
            throw new InvalidDataException($"paid policy trial {admitted.QuotaDecisionID} has no authenticated seed audit-only digest");
        _policyTrialQuotaByID[admitted.QuotaDecisionID] = admitted;
        InvalidatePolicyTrialReconcileMemo();
        if (_policyTrialCompletionByID.ContainsKey(admitted.QuotaDecisionID))
            throw new InvalidOperationException("cannot reuse a completed policy trial quota lease");
        CortexPolicyTrialQuotaDecision reused = admitted with
        {
            Decision = CortexPolicyQuotaDecisions.Reused,
            UsedSteps = 0,
            RemainingQuota = ReadPolicyTrialRemainingQuota(admitted.Policy),
        };
        _policyTrialQuotaDecisions.Add(reused);
        AppendPolicyTrialQuota(in reused);
        if (_runtimeTape is not null && _runtimeJournal is not null)
        {
            TapeEventID eventID = TapePacketCreator.AppendPolicyTrialQuota(_runtimeTape, _runtimeJournal, Step, in reused);
            EmitLoopClosurePolicyQuota(eventID, in reused);
        }
        Trace.Cortex.Boundary("policy.trial-reused",
            $"id={admitted.QuotaDecisionID} policy={admitted.Policy} planned={admitted.PlannedArmSteps} held={admitted.HeldArmSteps} remaining={reused.RemainingQuota}");
        return reused;
    }

    /// Ensure a recovered terminal generation has one authenticated quota receipt on the
    /// parent tape before completion/lineage evidence is emitted.  A checkpoint may predate the
    /// durable lease while its child generations survive; in that replay window the receipt must
    /// be typed Reused (zero new use, original QuotaStep preserved) and its lineage edge must
    /// name the learned-readout predecessor.  Existing packet/edge pairs are immutable no-ops.
    internal bool EnsurePolicyTrialQuotaPredecessor(
        in CortexPolicyTrialQuotaDecision paid,
        out CortexPolicyTrialQuotaDecision tapeQuota)
    {
        tapeQuota = default;
        CortexPolicyTrialQuotaDecision authenticatedQuota = paid;
        if (_runtimeTape is null || _runtimeJournal is null
            || authenticatedQuota.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            || authenticatedQuota.QuotaDecisionID.Value == 0)
            return false;
        if (!TryAuthenticatePaidAuditOnly(ref authenticatedQuota))
            return false;

        List<(TapeEventID EventID, CortexPolicyTrialQuotaDecision Decision)> packets = new();
        foreach (TapeEventView view in _runtimeTape.GetEventViews())
        {
            if (!string.Equals(view.Source, "policy:" + authenticatedQuota.Policy.Value, StringComparison.Ordinal)
                || view.Provenance != Provenances.Execution
                || !_runtimeTape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodePolicyTrialQuota(payload, out CortexPolicyTrialQuotaDecision packet,
                    out bool hasAuditOnly, out bool hasReadoutFingerprint)
                || !packet.QuotaDecisionID.Equals(authenticatedQuota.QuotaDecisionID)) continue;
            if (!hasAuditOnly || !hasReadoutFingerprint
                || !QuotaImmutableTupleMatches(in authenticatedQuota, in packet)
                || !IsAuthenticatedAuditOnlyDigest(packet.SeedAuditOnlyDigest)
                || packet.SeedAuditOnlyDigest != authenticatedQuota.SeedAuditOnlyDigest
                || !packet.Policy.Equals(authenticatedQuota.Policy)
                || packet.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                || packet.Decision == CortexPolicyQuotaDecisions.Reused && packet.UsedSteps != 0
                || packet.Decision == CortexPolicyQuotaDecisions.Paid && packet.UsedSteps != authenticatedQuota.UsedSteps)
                throw new InvalidDataException($"policy quota predecessor {authenticatedQuota.QuotaDecisionID} conflicts with authenticated audit-only");
            packets.Add((view.Id, packet));
        }

        int reusedCount = packets.Count(static packet => packet.Decision.Decision == CortexPolicyQuotaDecisions.Reused);
        if (reusedCount > 1)
            throw new InvalidDataException($"policy quota predecessor {authenticatedQuota.QuotaDecisionID} has duplicate Reused packets");
        int paidCount = packets.Count(static packet => packet.Decision.Decision == CortexPolicyQuotaDecisions.Paid);
        if (paidCount > 1)
            throw new InvalidDataException($"policy quota predecessor {authenticatedQuota.QuotaDecisionID} has duplicate Paid packets");
        (TapeEventID EventID, CortexPolicyTrialQuotaDecision Decision)? selected = packets
            .FirstOrDefault(static packet => packet.Decision.Decision == CortexPolicyQuotaDecisions.Reused);
        if (selected is null)
            selected = packets.FirstOrDefault(static packet => packet.Decision.Decision == CortexPolicyQuotaDecisions.Paid);

        if (selected is null || selected.Value.EventID.Value == 0)
        {
            CortexPolicyTrialQuotaDecision reused = _policyTrialQuotaDecisions
                .LastOrDefault(row => row.QuotaDecisionID.Equals(authenticatedQuota.QuotaDecisionID)
                    && row.Decision == CortexPolicyQuotaDecisions.Reused);
            if (reused.QuotaDecisionID.Value == 0)
            {
                reused = authenticatedQuota with
                {
                    Decision = CortexPolicyQuotaDecisions.Reused,
                    UsedSteps = 0,
                    RemainingQuota = Math.Min(
                        authenticatedQuota.RemainingQuota,
                        ReadPolicyTrialRemainingQuota(authenticatedQuota.Policy)),
                };
                _policyTrialQuotaDecisions.Add(reused);
                AppendPolicyTrialQuota(in reused);
                FlushPolicyJournalBuffer();
            }
            if (!QuotaImmutableTupleMatches(in authenticatedQuota, in reused)
                || reused.SeedAuditOnlyDigest != authenticatedQuota.SeedAuditOnlyDigest
                || reused.UsedSteps != 0)
                throw new InvalidDataException($"policy quota replay {authenticatedQuota.QuotaDecisionID} conflicts with durable quota");
            TapeEventID eventID = TapePacketCreator.AppendPolicyTrialQuota(_runtimeTape, _runtimeJournal, Step, in reused);
            selected = (eventID, reused);
        }

        tapeQuota = selected.Value.Decision;
        return EnsurePolicyTrialQuotaLineageEdge(selected.Value.EventID, in tapeQuota);
    }

    private bool EnsurePolicyTrialQuotaLineageEdge(
        TapeEventID quotaEventID,
        in CortexPolicyTrialQuotaDecision quota)
    {
        if (!_loopLineageEnabled)
            return true;
        if (_loopLineage is null
            || !TryGetLoopClosurePolicyRail(quota.Policy, quota.ReadoutIdentity, quota.CandidateRevision, out _))
            return false;
        if (!TryGetLoopClosurePolicyReadout(quota.Policy, quota.ReadoutIdentity,
                quota.CandidateRevision, out LoopLineageNode readout))
            throw new InvalidDataException($"policy quota {quota.QuotaDecisionID} has no learned-readout predecessor");
        byte[] payload = _runtimeTape?.Resolve(quotaEventID, out byte[] resolved) == true
            ? resolved
            : throw new InvalidDataException($"policy quota {quota.QuotaDecisionID} packet is not resolvable");
        string payloadDigest = Convert.ToHexStringLower(SHA256.HashData(payload));
        LoopLineageEdgeReceipt? exact = null;
        int exactEdgeCount = 0;
        foreach (LoopLineageEdgeReceipt edge in _loopLineage.Receipts)
        {
            if (edge.Node.Species != LoopLineageNodeSpecies.Quota || edge.Node.EventID != quotaEventID) continue;
            if (edge.Node.PayloadSHA256 != payloadDigest
                || edge.Node.GrammarRevision != quota.CandidateRevision
                || edge.PredecessorIDs.Count != 1
                || edge.PredecessorIDs[0] != readout.NodeID
                || edge.Node.CausalID != readout.CausalID)
                throw new InvalidDataException($"policy quota {quota.QuotaDecisionID} has a conflicting lineage predecessor");
            exact = edge;
            exactEdgeCount++;
        }
        if (exactEdgeCount > 1)
            throw new InvalidDataException($"policy quota {quota.QuotaDecisionID} has duplicate lineage predecessors");
        if (exact is null
            && !_loopLineage.TryEmit(Step, LoopLineageNodeSpecies.Quota, quotaEventID,
                quota.CandidateRevision, [readout.NodeID], readout.CausalID))
            throw new InvalidDataException($"policy quota {quota.QuotaDecisionID} lineage predecessor could not be repaired");
        return true;
    }

    private CortexPolicyReadoutQuotaDecision DecidePolicyReadoutQuota(
        CortexPolicyID policy,
        ulong candidateFingerprint,
        GrammarRevisionID revision,
        int deliberationDepth,
        in GrammarPolicyContextKey context)
    {
        int planned = checked(1 + deliberationDepth);
        CortexPolicyQuotaDecisionID quotaID = CreatePolicyReadoutQuotaDecisionID(
            policy, candidateFingerprint, revision, Step, in context, planned);
        if (_policyReadoutQuotaByID.TryGetValue(quotaID, out CortexPolicyReadoutQuotaDecision prior))
        {
            if (prior.Decision == CortexPolicyQuotaDecisions.Paid)
            {
                PolicyReadoutAllocationState reusedAllocationState = GetPolicyReadoutAllocationState(policy);
                CortexPolicyReadoutQuotaDecision reused = prior with
                {
                    Decision = CortexPolicyQuotaDecisions.Reused,
                    UsedUnits = 0,
                    RemainingQuota = reusedAllocationState.AvailableUnits,
                };
                _policyReadoutQuotaByID[quotaID] = reused;
                return reused;
            }
            PolicyReadoutAllocationState priorAllocationState = GetPolicyReadoutAllocationState(policy);
            return prior with { RemainingQuota = priorAllocationState.AvailableUnits };
        }

        PolicyReadoutAllocationState allocationState = GetPolicyReadoutAllocationState(policy);
        long availableBefore = allocationState.AvailableUnits;
        CortexPolicyQuotaDecisions result = availableBefore >= planned
            ? CortexPolicyQuotaDecisions.Paid
            : CortexPolicyQuotaDecisions.Denied;
        long held = result == CortexPolicyQuotaDecisions.Paid ? planned : 0;
        if (result == CortexPolicyQuotaDecisions.Paid)
        {
            allocationState.AvailableUnits = checked(allocationState.AvailableUnits - held);
            allocationState.HeldUnits = checked(allocationState.HeldUnits + held);
            _policyReadoutHeldUnits = checked(_policyReadoutHeldUnits + held);
            _policyReadoutUsedUnits = checked(_policyReadoutCompletedUsedUnits + _policyReadoutHeldUnits);
        }
        CortexPolicyReadoutQuotaDecision decision = new(
            quotaID, policy, candidateFingerprint, revision, context.ContextDigest, context.Context.Length,
            deliberationDepth, Step, planned, held, result, held, allocationState.AvailableUnits,
            allocationState.LastAllocationSequence, _policyReadoutRosterDigest, availableBefore, allocationState.AvailableUnits);
        _policyReadoutQuotaDecisions.Add(decision);
        _policyReadoutQuotaByID.Add(quotaID, decision);
        if (result == CortexPolicyQuotaDecisions.Paid)
            _policyReadoutPaidByID.Add(quotaID, decision);
        AppendPolicyReadoutQuota(in decision);
        Trace.Cortex.Boundary(
            result == CortexPolicyQuotaDecisions.Paid ? "policy.readout-paid" : "policy.readout-denied",
            $"id={quotaID} policy={policy} fp={candidateFingerprint:X16} depth={deliberationDepth} planned={planned} remaining={decision.RemainingQuota}");
        return decision;
    }

    internal bool TryReadPolicyTrialQuota(
        CortexPolicyID policy,
        ulong candidateFingerprint,
        int quotaStep,
        out CortexPolicyTrialQuotaDecision decision)
    {
        for (int i = _policyTrialQuotaDecisions.Count - 1; i >= 0; i--)
        {
            CortexPolicyTrialQuotaDecision candidate = _policyTrialQuotaDecisions[i];
            if (candidate.Policy.Equals(policy)
                && candidate.CandidateFingerprint == candidateFingerprint
                && candidate.QuotaStep == quotaStep
                && candidate.Decision == CortexPolicyQuotaDecisions.Paid)
            {
                decision = candidate;
                return true;
            }
        }
        decision = default;
        return false;
    }

    internal CortexPolicyTrialCompletion CompletePolicyTrial(
        in CortexPolicyTrialQuotaDecision decision,
        long actualExecutedArmSteps,
        long? evaluatorWorkUnits,
        CortexPolicyVerifierOutcomes occurrenceCheckOutcome,
        long? wallMilliseconds)
    {
        if (decision.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
            throw new InvalidOperationException("only paid policy trials can complete");
        if (!_policyTrialQuotaByID.TryGetValue(decision.QuotaDecisionID, out CortexPolicyTrialQuotaDecision paid))
            throw new InvalidDataException($"unknown policy quota decision {decision.QuotaDecisionID}");
        if (actualExecutedArmSteps < 0 || actualExecutedArmSteps > paid.PlannedArmSteps)
            throw new ArgumentOutOfRangeException(nameof(actualExecutedArmSteps));
        if (_policyTrialCompletionByID.TryGetValue(paid.QuotaDecisionID, out CortexPolicyTrialCompletion prior))
        {
            CortexPolicyTrialCompletion expected = new(
                paid.QuotaDecisionID,
                actualExecutedArmSteps,
                checked(paid.PlannedArmSteps - actualExecutedArmSteps),
                evaluatorWorkUnits,
                occurrenceCheckOutcome,
                wallMilliseconds);
            if (!CompletionMatches(in prior, in expected))
                throw new InvalidDataException($"policy trial completion {paid.QuotaDecisionID} conflicts with its durable completion");
            return prior;
        }
        if (paid.HeldArmSteps != paid.PlannedArmSteps || _policyTrialHeldSteps < paid.HeldArmSteps)
            throw new InvalidDataException("policy trial completion reservation is not conserved");
        long reclaimed = checked(paid.PlannedArmSteps - actualExecutedArmSteps);
        CortexPolicyTrialCompletion completion = new(
            paid.QuotaDecisionID, actualExecutedArmSteps, reclaimed, evaluatorWorkUnits, occurrenceCheckOutcome, wallMilliseconds);
        _policyTrialCompletions.Add(completion);
        _policyTrialCompletionByID.Add(completion.QuotaDecisionID, completion);
        _policyTrialHeldSteps = checked(_policyTrialHeldSteps - paid.HeldArmSteps);
        _policyTrialCompletedUsedSteps = checked(_policyTrialCompletedUsedSteps + actualExecutedArmSteps);
        _policyTrialUsedSteps = checked(_policyTrialCompletedUsedSteps + _policyTrialHeldSteps);
        InvalidatePolicyTrialReconcileMemo();
        AppendPolicyTrialCompletion(in completion);
        if (_runtimeTape is not null && _runtimeJournal is not null)
            TapePacketCreator.AppendPolicyTrialCompletion(_runtimeTape, _runtimeJournal, Step, paid.Policy, in completion);
        Trace.Cortex.Boundary("policy.trial-completed",
            $"id={paid.QuotaDecisionID} actual={actualExecutedArmSteps} reclaimed={reclaimed} evaluator={(evaluatorWorkUnits?.ToString(CultureInfo.InvariantCulture) ?? "na")} occurrence-check={occurrenceCheckOutcome} remaining={ReadPolicyTrialRemainingQuota(paid.Policy)}");
        return completion;
    }

    internal CortexPolicyTrialCompletion CompletePolicyReadout(
        in CortexPolicyReadoutQuotaDecision decision,
        long actualExecutedUnits,
        long? evaluatorWorkUnits,
        CortexPolicyVerifierOutcomes occurrenceCheckOutcome,
        long? wallMilliseconds)
    {
        if (decision.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
            throw new InvalidOperationException("only paid readouts can complete");
        if (!_policyReadoutPaidByID.TryGetValue(decision.QuotaDecisionID, out CortexPolicyReadoutQuotaDecision paid))
            throw new InvalidDataException($"unknown policy readout quota decision {decision.QuotaDecisionID}");
        if (actualExecutedUnits < 0 || actualExecutedUnits > paid.PlannedUnits)
            throw new ArgumentOutOfRangeException(nameof(actualExecutedUnits));
        if (_policyReadoutCompletionByID.TryGetValue(paid.QuotaDecisionID, out CortexPolicyTrialCompletion prior))
            return prior;
        long reclaimed = checked(paid.PlannedUnits - actualExecutedUnits);
        PolicyReadoutAllocationState allocationState = GetPolicyReadoutAllocationState(paid.Policy);
        if (allocationState.HeldUnits < paid.HeldUnits)
            throw new InvalidDataException("policy readout allocation state reservation is below the completion amount");
        CortexPolicyTrialCompletion completion = new(paid.QuotaDecisionID, actualExecutedUnits, reclaimed, evaluatorWorkUnits, occurrenceCheckOutcome, wallMilliseconds);
        _policyReadoutCompletions.Add(completion);
        _policyReadoutCompletionByID.Add(completion.QuotaDecisionID, completion);
        _policyReadoutHeldUnits = checked(_policyReadoutHeldUnits - paid.HeldUnits);
        _policyReadoutCompletedUsedUnits = checked(_policyReadoutCompletedUsedUnits + actualExecutedUnits);
        _policyReadoutUsedUnits = checked(_policyReadoutCompletedUsedUnits + _policyReadoutHeldUnits);
        allocationState.HeldUnits = checked(allocationState.HeldUnits - paid.HeldUnits);
        allocationState.UsedUnits = checked(allocationState.UsedUnits + actualExecutedUnits);
        allocationState.ReclaimedUnits = checked(allocationState.ReclaimedUnits + reclaimed);
        allocationState.AvailableUnits = checked(allocationState.AvailableUnits + reclaimed);
        AppendPolicyReadoutCompletion(in completion);
        Trace.Cortex.Boundary("policy.readout-completed", $"id={paid.QuotaDecisionID} actual={actualExecutedUnits} reclaimed={reclaimed} remaining={ReadPolicyReadoutRemainingQuota()}");
        return completion;
    }

    internal bool TryReadPolicyTrialCompletion(CortexPolicyQuotaDecisionID quotaID, out CortexPolicyTrialCompletion completion)
        => _policyTrialCompletionByID.TryGetValue(quotaID, out completion);

    internal bool TryReadPolicyReadoutQuota(CortexPolicyID policy, ulong candidateFingerprint, int quotaStep, out CortexPolicyReadoutQuotaDecision decision)
    {
        for (int i = _policyReadoutQuotaDecisions.Count - 1; i >= 0; i--)
        {
            CortexPolicyReadoutQuotaDecision candidate = _policyReadoutQuotaDecisions[i];
            if (candidate.Policy.Equals(policy) && candidate.CandidateFingerprint == candidateFingerprint
                && candidate.QuotaStep == quotaStep && candidate.Decision == CortexPolicyQuotaDecisions.Paid)
            { decision = candidate; return true; }
        }
        decision = default;
        return false;
    }

    internal bool TryReadPolicyReadoutCompletion(CortexPolicyQuotaDecisionID quotaID, out CortexPolicyTrialCompletion completion)
        => _policyReadoutCompletionByID.TryGetValue(quotaID, out completion);

    internal bool TryReadPolicyReadoutQuota(CortexPolicyQuotaDecisionID quotaID, out CortexPolicyReadoutQuotaDecision decision)
        => _policyReadoutQuotaByID.TryGetValue(quotaID, out decision);

    internal static string ComputePolicyReadoutQuotaJournalRowSHA256(in CortexPolicyReadoutQuotaDecision decision)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(FormatPolicyReadoutQuotaRow(in decision))));

    internal static string ComputePolicyReadoutCompletionJournalRowSHA256(in CortexPolicyTrialCompletion completion)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(FormatPolicyTrialCompletionRow(in completion))));

    internal int ReadPolicyReadoutQuotaCount()
        => _policyReadoutQuotaDecisions.Count;

    private CortexPolicyTrialAllocation ReadPolicyTrialAllocation(CortexPolicyID policy)
        => _policyBoundaryDomains.ContainsKey(policy) && _config.Learning.Policies.TrialAllocation is { } configured
            ? CortexPolicyTrialAllocation.Bind(policy, configured)
            : default;

    private long ReadPolicyTrialRemainingQuota(CortexPolicyID policy)
    {
        CortexPolicyTrialAllocation allocation = ReadPolicyTrialAllocation(policy);
        long allocated = checked(Step + allocation.ArmSteps);
        return Math.Max(0, allocated - checked(_policyTrialCompletedUsedSteps + _policyTrialHeldSteps));
    }

    private static string BuildQuotaKey(CortexPolicyID policy, ulong fingerprint, int step, long planned)
        => policy.Value + "\t" + fingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + step.ToString(CultureInfo.InvariantCulture) + "\t" + planned.ToString(CultureInfo.InvariantCulture);

    private static bool IsAuthenticatedAuditOnlyDigest(string? digest)
        => digest is { Length: 64 } && digest.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private bool TryAuthenticatePaidAuditOnly(ref CortexPolicyTrialQuotaDecision quota)
    {
        if (!TryReadPolicyBoundarySeedAuditOnlyDigest(quota.QuotaDecisionID, out string auditOnlyDigest)
            || !IsAuthenticatedAuditOnlyDigest(auditOnlyDigest)) return false;
        if (quota.SeedAuditOnlyDigest.Length != 0 && quota.SeedAuditOnlyDigest != auditOnlyDigest) return false;
        quota = quota with { SeedAuditOnlyDigest = auditOnlyDigest };
        return TryLoadPolicyBoundarySeed(in quota, out _);
    }

    private static bool QuotaIdentityMatches(in CortexPolicyTrialQuotaDecision first, in CortexPolicyTrialQuotaDecision second)
        => first.QuotaDecisionID.Equals(second.QuotaDecisionID)
            && first.Policy.Equals(second.Policy)
            && first.ReadoutFingerprint == second.ReadoutFingerprint
            && first.CandidateFingerprint == second.CandidateFingerprint
            && first.QuotaStep == second.QuotaStep
            && first.RequestedHorizonSteps == second.RequestedHorizonSteps
            && first.ArmCount == second.ArmCount
            && first.PlannedArmSteps == second.PlannedArmSteps
            && first.HeldArmSteps == second.HeldArmSteps
            && first.CandidateState == second.CandidateState
            && first.DenialReason == second.DenialReason
            && first.CandidateOriginStep == second.CandidateOriginStep
            && first.CandidateCurrentStep == second.CandidateCurrentStep
            && first.CandidateRequiredStep == second.CandidateRequiredStep
            && first.CandidateRevision == second.CandidateRevision
            && first.AllocationIdentity == second.AllocationIdentity
            && first.AllocationDigest == second.AllocationDigest
            && first.AllocationArmSteps == second.AllocationArmSteps
            && first.HasCanonicalState == second.HasCanonicalState
            && first.CanonicalState == second.CanonicalState
            && QuotaSeedAuditOnlyMatches(in first, in second);

    private static bool QuotaImmutableTupleMatches(in CortexPolicyTrialQuotaDecision first, in CortexPolicyTrialQuotaDecision second)
        => first.QuotaDecisionID.Equals(second.QuotaDecisionID)
            && first.Policy.Equals(second.Policy)
            && first.ReadoutFingerprint == second.ReadoutFingerprint
            && first.CandidateFingerprint == second.CandidateFingerprint
            && first.QuotaStep == second.QuotaStep
            && first.RequestedHorizonSteps == second.RequestedHorizonSteps
            && first.ArmCount == second.ArmCount
            && first.PlannedArmSteps == second.PlannedArmSteps
            && first.HeldArmSteps == second.HeldArmSteps
            && first.CandidateState == second.CandidateState
            && first.DenialReason == second.DenialReason
            && first.CandidateOriginStep == second.CandidateOriginStep
            && first.CandidateCurrentStep == second.CandidateCurrentStep
            && first.CandidateRequiredStep == second.CandidateRequiredStep
            && first.CandidateRevision == second.CandidateRevision
            && first.AllocationIdentity == second.AllocationIdentity
            && first.AllocationDigest == second.AllocationDigest
            && first.AllocationArmSteps == second.AllocationArmSteps
            && first.HasCanonicalState == second.HasCanonicalState
            && first.CanonicalState == second.CanonicalState
            && QuotaSeedAuditOnlyMatches(in first, in second);

    private static bool QuotaAllocationShapeIsValid(in CortexPolicyTrialQuotaDecision row)
    {
        bool digestMatches = string.Equals(row.AllocationDigest,
            CortexPolicyTrialAllocation.ComputeDigest(row.Policy, CortexPolicyAuthorities.Grammar,
                row.AllocationArmSteps, row.AllocationIdentity), StringComparison.Ordinal);
        if (!Enum.IsDefined(row.Decision) || !Enum.IsDefined(row.CandidateState)
            || !Enum.IsDefined(row.DenialReason)
            || row.RequestedHorizonSteps <= 0 || row.ArmCount <= 0
            || row.PlannedArmSteps != checked((long)row.RequestedHorizonSteps * row.ArmCount)
            || row.HeldArmSteps != row.PlannedArmSteps
            || row.UsedSteps < 0 || row.RemainingQuota < 0
            || row.UsedSteps > row.HeldArmSteps
            || row.AllocationArmSteps < row.RequestedHorizonSteps || string.IsNullOrWhiteSpace(row.AllocationIdentity)
            || !digestMatches
            || !IsAuthenticatedAuditOnlyDigest(row.SeedAuditOnlyDigest)) return false;
        if (row.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            || row.CandidateState != CortexPolicyTrialCandidateStates.Active
            || row.DenialReason != CortexPolicyTrialDenialReasons.None
            || !((row.CandidateOriginStep == -1 && row.CandidateCurrentStep == -1 && row.CandidateRequiredStep == -1)
                || (row.CandidateOriginStep >= 0 && row.CandidateCurrentStep >= row.QuotaStep
                    && (row.CandidateRequiredStep == -1
                        || row.CandidateRequiredStep == checked(row.CandidateOriginStep + row.RequestedHorizonSteps))))) return false;
        return true;
    }

    private static bool QuotaSeedAuditOnlyMatches(in CortexPolicyTrialQuotaDecision first, in CortexPolicyTrialQuotaDecision second)
    {
        if (!first.Policy.Equals(Homeostat.PolicyID))
            return first.SeedAuditOnlyDigest == second.SeedAuditOnlyDigest;
        if (first.Decision == CortexPolicyQuotaDecisions.Denied
            && second.Decision == CortexPolicyQuotaDecisions.Denied)
            return first.SeedAuditOnlyDigest == second.SeedAuditOnlyDigest;
        return IsAuthenticatedAuditOnlyDigest(first.SeedAuditOnlyDigest)
                && first.SeedAuditOnlyDigest == second.SeedAuditOnlyDigest
            || first.SeedAuditOnlyDigest.Length == 0
                && first.Decision == CortexPolicyQuotaDecisions.Paid
                && second.Decision == CortexPolicyQuotaDecisions.Reused
                && IsAuthenticatedAuditOnlyDigest(second.SeedAuditOnlyDigest);
    }

    private static bool CompletionMatches(in CortexPolicyTrialCompletion first, in CortexPolicyTrialCompletion second)
        => first.QuotaDecisionID.Equals(second.QuotaDecisionID)
            && first.ActualExecutedArmSteps == second.ActualExecutedArmSteps
            && first.ReclaimedOrUnused == second.ReclaimedOrUnused
            && first.EvaluatorWorkUnits == second.EvaluatorWorkUnits
            && first.VerifierOutcome == second.VerifierOutcome
            && first.WallMilliseconds == second.WallMilliseconds;

    private static CortexPolicyQuotaDecisionID CreateQuotaDecisionID(
        CortexPolicyID policy, ulong fingerprint, int step, int horizonSteps, int armCount, ulong readoutFingerprint,
        string allocationDigest = "")
    {
        ulong hash = 14695981039346656037UL;
        string key = policy.Value + "\t" + fingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + step.ToString(CultureInfo.InvariantCulture) + "\t" + horizonSteps.ToString(CultureInfo.InvariantCulture)
            + "\t" + armCount.ToString(CultureInfo.InvariantCulture) + "\t" + allocationDigest
            + "\t" + readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture);
        for (int i = 0; i < key.Length; i++) { hash ^= key[i]; hash *= 1099511628211UL; }
        return new CortexPolicyQuotaDecisionID(hash == 0 ? 1 : hash);
    }

    internal static CortexPolicyQuotaDecisionID ComputePolicyReadoutQuotaDecisionID(
        CortexPolicyID policy, ulong candidateFingerprint, GrammarRevisionID revision, int step,
        ulong contextDigest, int contextBytes, int deliberationDepth, long planned)
        => CreatePolicyReadoutQuotaDecisionID(policy, candidateFingerprint, revision, step, contextDigest, contextBytes, deliberationDepth, planned);

    private static CortexPolicyQuotaDecisionID CreatePolicyReadoutQuotaDecisionID(
        CortexPolicyID policy,
        ulong candidateFingerprint,
        GrammarRevisionID revision,
        int step,
        in GrammarPolicyContextKey context,
        int planned)
        => CreatePolicyReadoutQuotaDecisionID(
            policy, candidateFingerprint, revision, step, context.ContextDigest, context.Context.Length,
            context.DeliberationDepth, planned);

    private static CortexPolicyQuotaDecisionID CreatePolicyReadoutQuotaDecisionID(
        CortexPolicyID policy,
        ulong candidateFingerprint,
        GrammarRevisionID revision,
        int step,
        ulong contextDigest,
        int contextBytes,
        int deliberationDepth,
        long planned)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(ulong value) { hash ^= value; hash *= 1099511628211UL; }
        for (int i = 0; i < policy.Value.Length; i++) Mix(policy.Value[i]);
        Mix(candidateFingerprint); Mix(revision.Value); Mix(unchecked((ulong)step));
        Mix(unchecked((ulong)deliberationDepth)); Mix(unchecked((ulong)planned));
        Mix(unchecked((ulong)contextBytes)); Mix(contextDigest);
        return new CortexPolicyQuotaDecisionID(hash == 0 ? 1 : hash);
    }

    internal static CortexPolicyQuotaDecisionID ComputePolicyTrialQuotaDecisionID(
        CortexPolicyID policy, ulong candidateFingerprint, ulong readoutFingerprint, int step, int horizonSteps, int armCount, string allocationDigest = "")
        => CreateQuotaDecisionID(policy, candidateFingerprint, step, horizonSteps, armCount, readoutFingerprint, allocationDigest);

    internal bool HasPolicyGrammarAuthority(CortexPolicyID policy, ulong activeProgramFingerprint)
    {
        PolicyState state = GetPolicy(policy);
        return state.Authority == CortexPolicyAuthorities.Grammar
            && ReadActivePolicyFingerprint(state) == activeProgramFingerprint;
    }

    private static ulong ComputeCanonicalProgramDigest(PolicyState state, CortexPolicyID policy)
    {
        List<PolicyState.CanonicalCandidateEvidence> candidates = new();
        foreach (PolicyState.CanonicalCandidateEvidence candidate in state.CanonicalCandidates.Values)
            if (candidate.OccurrenceDigest != 0 && candidate.State.Policy.Equals(policy))
                candidates.Add(candidate);
        candidates.Sort(static (left, right) => left.State.CompareTo(right.State));
        if (candidates.Count == 0) return 0;
        ulong hash = 14695981039346656037UL;
        void Mix(ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        for (int index = 0; index < candidates.Count; index++)
        {
            PolicyState.CanonicalCandidateEvidence candidate = candidates[index];
            PolicyCanonicalStateID canonicalState = candidate.State;
            Mix((byte)canonicalState.Kind);
            Mix(canonicalState.Version);
            Mix(canonicalState.Value);
            Mix(unchecked((ulong)candidate.Action));
            Mix(new PolicyCanonicalizerVersion(canonicalState.Version).Value);
        }
        Mix(unchecked((ulong)candidates.Count));
        return hash == 0 ? 1 : hash;
    }

    private static void RemoveCanonicalEvidenceTotals(
        PolicyState state,
        PolicyState.CanonicalCandidateEvidence evidence)
    {
        state.ShadowComparisons = checked(state.ShadowComparisons - evidence.Comparisons);
        state.ShadowAgreements = checked(state.ShadowAgreements - evidence.Agreements);
        state.EmulationMisses = checked(state.EmulationMisses - evidence.Misses);
    }

    private static void ClearActiveCanonicalReadout(PolicyState state)
    {
        state.ReadoutCandidateRevision = GrammarRevisionID.Zero;
        state.ReadoutCandidateFingerprint = 0;
        state.ReadoutCandidateState = default;
        state.ReadoutCandidateOccurrenceDigest = 0;
        state.ReadoutCandidateAction = -1;
        state.AssayedFingerprint = 0;
        state.VerifiedFingerprint = 0;
        state.AssayedReadoutFingerprint = 0;
        state.VerifiedReadoutFingerprint = 0;
        state.VerifiedRevision = GrammarRevisionID.Zero;
        state.ReadoutCandidatePending = true;
    }

    private static void InvalidateCanonicalCoverage(PolicyState state)
    {
        state.CanonicalCoverageVersion = checked(state.CanonicalCoverageVersion + 1);
        state.CanonicalCoverageCacheVersion = 0;
        state.CanonicalCoverageIndex = null;
    }

    private static void TouchCanonicalCoverage(PolicyState state)
    {
        state.CanonicalCoverageVersion = checked(state.CanonicalCoverageVersion + 1);
        state.CanonicalCoverageCacheVersion = 0;
    }

    private static void UpdateCanonicalCoverage(
        PolicyState state,
        PolicyCanonicalCoverageEntry entry)
    {
        state.CanonicalCoverageVersion = checked(state.CanonicalCoverageVersion + 1);
        state.CanonicalCoverageCacheVersion = 0;
        state.CanonicalCoverageIndex?.Upsert(in entry);
    }

    private static void RemoveCanonicalCoverage(
        PolicyState state,
        in PolicyCanonicalStateID canonicalState)
    {
        state.CanonicalCoverageVersion = checked(state.CanonicalCoverageVersion + 1);
        state.CanonicalCoverageCacheVersion = 0;
        state.CanonicalCoverageIndex?.Remove(in canonicalState);
    }

    private static void RefreshCanonicalProgramDigest(PolicyState state, CortexPolicyID policy)
    {
        if (!state.CanonicalProgramDigestDirty) return;
        state.ReadoutCandidateSetDigest = ComputeCanonicalProgramDigest(state, policy);
        state.CanonicalProgramDigestDirty = false;
    }

    private static ulong ReadActivePolicyFingerprint(PolicyState state)
        => state.ReadoutCandidateRevision != global::Cogito.Grammar.GrammarRevisionID.Zero
            ? state.ReadoutCandidateSetDigest != 0
                ? state.ReadoutCandidateSetDigest
                : state.ReadoutCandidateFingerprint
            : 0;

    private static ulong ReadActivePolicyCandidateFingerprint(PolicyState state)
        => state.ReadoutCandidateFingerprint;

    internal int ReadPolicyActionReversals(CortexPolicyID policy)
        => GetPolicy(policy).ActionReversals;

    private void SetTrialAuthority(
        PolicyState state,
        CortexPolicyAuthorities authority,
        CortexPolicyTrialDemotionReasons demotionReason = CortexPolicyTrialDemotionReasons.None,
        bool boundaryEvaluated = false,
        bool boundarySatisfied = false,
        bool hasGrammarReadout = false,
        GrammarRevisionID candidateRevision = default,
        ulong candidateFingerprint = 0,
        PolicyBoundaryGateObservation boundaryGate = default,
        bool trialOverrideClearedOnRevisionDrift = false,
        bool emitTransition = true)
    {
        CortexPolicyAuthorities priorAuthority = state.Authority;
        ulong? forcedSeedBefore = state.TrialForcedDivergenceSeed;
        int actionOffsetBefore = state.TrialActionOffset;
        bool suppressBefore = state.SuppressTrialPackets;
        int remainingBefore = state.TrialGrammarExecutionsRemaining;
        CortexPolicyAuthorities effective = authority <= state.AuthorityCeiling
            ? authority
            : state.AuthorityCeiling;
        if (priorAuthority != effective) state.TrialAdaptationTransitions++;
        state.Authority = effective;
        if (emitTransition && priorAuthority != effective)
            TracePolicyTrialAuthorityTransition(
                state.Schema.Policy,
                state,
                priorAuthority,
                effective,
                demotionReason,
                boundaryEvaluated,
                boundarySatisfied,
                hasGrammarReadout,
                candidateRevision,
                candidateFingerprint,
                boundaryGate,
                trialOverrideClearedOnRevisionDrift,
                forcedSeedBefore,
                state.TrialForcedDivergenceSeed,
                actionOffsetBefore,
                state.TrialActionOffset,
                suppressBefore,
                state.SuppressTrialPackets,
                remainingBefore,
                state.TrialGrammarExecutionsRemaining);
    }

    private void TracePolicyTrialAuthorityTransition(
        CortexPolicyID policy,
        PolicyState state,
        CortexPolicyAuthorities priorAuthority,
        CortexPolicyAuthorities effectiveAuthority,
        CortexPolicyTrialDemotionReasons demotionReason,
        bool boundaryEvaluated,
        bool boundarySatisfied,
        bool hasGrammarReadout,
        GrammarRevisionID candidateRevision,
        ulong candidateFingerprint,
        PolicyBoundaryGateObservation boundaryGate,
        bool trialOverrideClearedOnRevisionDrift,
        ulong? forcedSeedBefore,
        ulong? forcedSeedAfter,
        int actionOffsetBefore,
        int actionOffsetAfter,
        bool suppressBefore,
        bool suppressAfter,
        int remainingBefore = -1,
        int remainingAfter = -1)
    {
        Trace.Cortex.Boundary(
            "policy.trial-authority-transition",
            $"step={Step} policy={policy} old_authority={priorAuthority} new_authority={effectiveAuthority} "
            + $"has_grammar_readout={Bool(hasGrammarReadout)} grammar_revision={candidateRevision.Value} readout_revision={state.LastDecisionReadout.GrammarRevision.Value} "
            + $"grammar_fingerprint={candidateFingerprint:X16} readout_fingerprint={state.LastDecisionReadout.CandidateFingerprint:X16} "
            + $"configured_cause={state.TrialExecutionCause} forced_seed={FormatOptionalSeed(forcedSeedBefore)}->{FormatOptionalSeed(forcedSeedAfter)} "
            + $"action_offset={actionOffsetBefore}->{actionOffsetAfter} suppress_trial={Bool(suppressBefore)}->{Bool(suppressAfter)} "
            + $"trial_remaining={remainingBefore}->{remainingAfter} trial_override_cleared_revision_drift={Bool(trialOverrideClearedOnRevisionDrift)} "
            + $"boundary_evaluated={Bool(boundaryEvaluated)} boundary_observed={FormatObserved(boundaryGate.Observed)} "
            + $"boundary_threshold={boundaryGate.Boundary} boundary_comparison={boundaryGate.Comparison} "
            + $"boundary_satisfied={Bool(boundarySatisfied)} demotion_reason={demotionReason}");
    }

    private static string FormatOptionalSeed(ulong? seed)
        => seed.HasValue ? seed.Value.ToString("X16", CultureInfo.InvariantCulture) : "none";

    private static string FormatObserved(double observed)
        => double.IsFinite(observed) ? observed.ToString("R", CultureInfo.InvariantCulture) : "nonfinite";

    private static int Bool(bool value) => value ? 1 : 0;

    internal (ulong Grammar, ulong Divergent, int LaunchpadAction, int GrammarAction) ReadPolicyGrammarExecutions(CortexPolicyID policy)
    {
        PolicyState state = GetPolicy(policy);
        return (state.GrammarExecutions, state.GrammarDivergentExecutions,
            state.LastGrammarLaunchpadAction, state.LastGrammarAction);
    }

    internal CortexPolicyRuntimeReceipt ReadPolicyRuntimeReceipt(CortexPolicyID policy)
    {
        if (!TryReadPolicyRuntimeReceipt(policy, out CortexPolicyRuntimeReceipt receipt))
            throw new KeyNotFoundException($"policy '{policy}' is not registered");
        return receipt;
    }

    internal bool TryReadPolicyRuntimeReceipt(CortexPolicyID policy, out CortexPolicyRuntimeReceipt receipt)
    {
        if (!_policies.TryGetValue(policy, out PolicyState? state))
        {
            receipt = default;
            return false;
        }
        receipt = new CortexPolicyRuntimeReceipt(
            state.Authority,
            state.ReadoutCache.Count,
            state.ShadowComparisons,
            state.ShadowAgreements,
            state.Decisions,
            state.Outcomes,
            [.. state.ActionExecutions],
            state.ConservedCost,
            state.ActionReversals,
            state.GrammarExecutions,
            state.GrammarOutcomes,
            state.PaidGrammarOutcomes,
            state.GrammarDivergentExecutions,
            state.Readmissions,
            state.RollbackDrillPending,
            state.RollbackDrillCompleted,
            state.LastGrammarLaunchpadAction,
            state.LastGrammarAction,
            [.. state.LastGrammarFeatures],
            state.TrialAdaptationTransitions,
            state.TrialFrozen,
            !state.TrialFrozen);
        return true;
    }

    internal CortexPolicyDecisionReadout ReadPolicyDecisionReadout(CortexPolicyID policy)
        => GetPolicy(policy).LastDecisionReadout;

    /// Read the destination rail's own last executed decision.  This is a local
    /// action receipt, not the candidate-cache readout: a launchpad decision is
    /// valid evidence even when this child was never paid to populate a cache.
    internal bool TryReadExecutedPolicyDecisionIdentity(
        CortexPolicyID policy,
        out CortexPolicyDecisionReadout readout,
        out ulong fingerprint)
    {
        bool present = TryReadExecutedPolicyDecisionIdentity(policy, out readout, out CortexPolicyDecisionID _, out fingerprint);
        return present;
    }

    internal bool TryReadExecutedPolicyDecisionIdentity(
        CortexPolicyID policy,
        out CortexPolicyDecisionReadout readout,
        out CortexPolicyDecisionID decisionID,
        out ulong fingerprint)
    {
        PolicyState state = GetPolicy(policy);
        readout = state.LastDecisionReadout;
        decisionID = state.LastDecisionID;
        fingerprint = 0;
        if (state.Decisions == 0 || state.LastDecisionID.Value == 0 || readout.GrammarRevision == global::Cogito.Grammar.GrammarRevisionID.Zero)
        {
            readout = default;
            decisionID = default;
            return false;
        }
        try { readout.Validate(state.Schema.ActionCount); }
        catch (InvalidDataException)
        {
            readout = default;
            decisionID = default;
            return false;
        }
        fingerprint = readout.ReadoutFingerprint != 0
            ? readout.ReadoutFingerprint
            : GrammarPolicyReadout.ComputeFingerprint(readout.GrammarRevision, policy);
        if (fingerprint == 0)
        {
            readout = default;
            decisionID = default;
            return false;
        }
        return true;
    }

    /// Resolve one generic policy decision to its ordinary execution packet.  Domain
    /// runtimes use this identity after the policy owner has emitted the packet; they
    /// never infer a readout event from a repository-specific journal row.
    internal bool TryFindPolicyDecisionEvent(
        in CortexPolicyDecision expected,
        out TapeEventID eventID)
    {
        eventID = new TapeEventID(-1);
        if (_runtimeTape is null || expected.DecisionID.Value == 0 || expected.Policy.Value.Length == 0)
            return false;
        string source = "policy:" + expected.Policy.Value;
        int matches = 0;
        foreach (TapeEventView view in _runtimeTape.GetEventViews())
        {
            if (!string.Equals(view.Source, source, StringComparison.Ordinal)
                || view.Provenance != Provenances.Execution
                || !_runtimeTape.Resolve(view.Id, out byte[] payload))
                continue;
            CortexPolicyDecisionPacket packet;
            try { packet = TapePacketCreator.DecodePolicyDecision(payload); }
            catch (InvalidDataException) { continue; }
            if (!packet.DecisionID.Equals(expected.DecisionID)
                || packet.Readout != expected.Readout)
                continue;
            matches++;
            eventID = view.Id;
        }
        return matches == 1 && eventID.Value > 0;
    }

    /// Reads the last decision that actually crossed the configured trial rail. Later
    /// shadow traffic must not erase an earlier Grammar or forced-null execution.
    internal bool TryReadPolicyTrialExecutionIdentity(
        CortexPolicyID policy,
        out CortexPolicyDecisionReadout readout,
        out CortexPolicyDecisionID decisionID,
        out ulong fingerprint,
        out int executionStep)
    {
        PolicyState state = GetPolicy(policy);
        readout = default;
        decisionID = default;
        fingerprint = 0;
        executionStep = -1;
        if (state.HistoricalTrialExecution.IsPresent)
        {
            PolicyTrialExecutionHistory history = state.HistoricalTrialExecution;
            if (!TryReadCurrentHistoricalTrialExecution(state, policy, in history)) return false;
            readout = history.ExecutionReadout;
            decisionID = history.ExecutionDecisionID;
            fingerprint = history.ExecutionReadoutFingerprint;
            executionStep = history.ExecutionStep;
            return fingerprint != 0;
        }
        CortexPolicyDecision? corroboration = state.TrialExecutionCorroboration;
        if (corroboration is not CortexPolicyDecision executed
            || !executed.Policy.Equals(policy)
            || executed.SelectionCause != state.TrialExecutionCause
            || state.TrialExecutionStep < 0)
            return false;
        executed.Readout.Validate(state.Schema.ActionCount);
        readout = executed.Readout;
        decisionID = executed.DecisionID;
        fingerprint = state.TrialExecutionReadoutFingerprint;
        executionStep = state.TrialExecutionStep;
        return fingerprint != 0;
    }

    internal bool TryReadPolicyTrialExecutionReceipt(
        CortexPolicyID policy,
        out CortexPolicyTrialExecutionOutcomes outcome,
        out long requestCount,
        out long guardAdmittedCount,
        out CortexPolicyDecisionReadout lastRequestReadout,
        out CortexPolicyDecisionID lastRequestDecisionID,
        out int lastRequestStep,
        out CortexPolicyDecisionReadout executionReadout,
        out CortexPolicyDecisionID executionDecisionID,
        out ulong executionFingerprint,
        out int executionStep)
    {
        PolicyState state = GetPolicy(policy);
        PolicyTrialExecutionHistory historicalExecution = state.HistoricalTrialExecution;
        if (historicalExecution.IsPresent
            && !TryReadCurrentHistoricalTrialExecution(state, policy, in historicalExecution))
        {
            outcome = default;
            requestCount = 0;
            guardAdmittedCount = 0;
            lastRequestReadout = default;
            lastRequestDecisionID = default;
            lastRequestStep = -1;
            executionReadout = default;
            executionDecisionID = default;
            executionFingerprint = 0;
            executionStep = -1;
            return false;
        }
        bool historical = historicalExecution.IsPresent
            && TryReadCurrentHistoricalTrialExecution(state, policy, in historicalExecution);
        PolicyTrialExecutionHistory history = historical
            ? historicalExecution
            : new(default, state.TrialExecutionCause, state.TrialExecutionOutcome,
                state.TrialRequestCount, state.TrialGuardAdmittedCount,
                state.TrialLastRequest?.DecisionID ?? default,
                state.TrialLastRequest?.Readout ?? default,
                state.TrialLastRequestStep,
                state.TrialExecutionCorroboration?.DecisionID ?? default,
                state.TrialExecutionCorroboration?.Readout ?? default,
                state.TrialExecutionStep,
                state.TrialExecutionReadoutFingerprint,
                default);
        outcome = history.Outcome;
        requestCount = history.RequestCount;
        guardAdmittedCount = history.GuardAdmittedCount;
        lastRequestReadout = history.LastRequestReadout;
        lastRequestDecisionID = history.LastRequestDecisionID;
        lastRequestStep = history.LastRequestStep;
        executionReadout = CortexPolicyDecisionReadout.NoExecution;
        executionDecisionID = default;
        executionFingerprint = 0;
        executionStep = -1;
        if (!Enum.IsDefined(outcome) || requestCount < 0 || guardAdmittedCount < 0 || guardAdmittedCount > requestCount)
            return false;
        if (requestCount > 0)
        {
            if (lastRequestDecisionID.Value == 0 || lastRequestStep < 0)
                return false;
            try { lastRequestReadout.Validate(state.Schema.ActionCount); }
            catch (InvalidDataException) { return false; }
        }
        if (historical)
        {
            try { history.Validate(policy, state.Schema.ActionCount); }
            catch (InvalidDataException) { return false; }
            if (outcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted)
                return false;
            executionReadout = history.ExecutionReadout;
            executionDecisionID = history.ExecutionDecisionID;
            executionFingerprint = history.ExecutionReadoutFingerprint;
            executionStep = history.ExecutionStep;
            return executionFingerprint != 0;
        }
        if (state.TrialExecutionCorroboration is CortexPolicyDecision corroboration)
        {
            if (outcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                || !corroboration.Policy.Equals(policy)
                || corroboration.SelectionCause != state.TrialExecutionCause
                || state.TrialExecutionStep < 0)
                return false;
            try { corroboration.Readout.Validate(state.Schema.ActionCount); }
            catch (InvalidDataException) { return false; }
            executionReadout = corroboration.Readout;
            executionDecisionID = corroboration.DecisionID;
            executionFingerprint = state.TrialExecutionReadoutFingerprint;
            executionStep = state.TrialExecutionStep;
            return executionFingerprint != 0;
        }
        return outcome == CortexPolicyTrialExecutionOutcomes.NotAttempted && requestCount == 0
            || outcome == CortexPolicyTrialExecutionOutcomes.GuardDenied && requestCount > 0 && guardAdmittedCount == 0;
    }

    internal bool TryReadPolicyTrialExecutionScope(
        CortexPolicyID policy,
        out PolicyVerifiedScopeEntry scope)
    {
        scope = default;
        PolicyState state = GetPolicy(policy);
        IPolicyBoundaryDomain? domain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredDomain)
            ? registeredDomain : null;
        PolicyCanonicalStateID currentState = state.ReadoutCandidateState;
        bool requiresScope = RequiresCanonicalScope(domain);
        if (ValidateCanonicalState(domain, policy, in currentState)
            && (!requiresScope || state.VerifiedScopes.TryGetValue(currentState, out scope))
            && (!requiresScope || (scope.IsValid
                && scope.State == currentState
                && scope.ReadoutFingerprint == ReadActivePolicyFingerprint(state)
                && scope.CandidateFingerprint == state.ReadoutCandidateFingerprint
                && scope.OccurrenceDigest == state.ReadoutCandidateOccurrenceDigest
                && scope.Revision == state.ReadoutCandidateRevision)))
            return true;
        PolicyTrialExecutionHistory history = state.HistoricalTrialExecution;
        bool currentTuplePresent = state.ReadoutCandidateRevision != GrammarRevisionID.Zero
            || state.ReadoutCandidateFingerprint != 0
            || state.ReadoutCandidateState.Version != 0;
        if (!currentTuplePresent
            && history.IsPresent
            && TryReadCurrentHistoricalTrialExecution(state, policy, in history))
        {
            scope = history.Scope;
            return true;
        }
        scope = default;
        return false;
    }

    private bool TryReadCurrentHistoricalTrialExecution(
        PolicyState state,
        CortexPolicyID policy,
        in PolicyTrialExecutionHistory history)
    {
        try { history.Validate(policy, state.Schema.ActionCount); }
        catch (InvalidDataException) { return false; }
        IPolicyBoundaryDomain? domain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredDomain)
            ? registeredDomain : null;
        PolicyCanonicalStateID historyState = history.Scope.State;
        if (!ValidateCanonicalState(domain, policy, in historyState)) return false;

        // After install revision the active candidate tuple is deliberately empty;
        // that is the terminal window in which the completed execution remains
        // readable. Once a new candidate/readout tuple is installed, history
        // must match all identity coordinates or it is stale epoch residue.
        bool currentTuplePresent = state.ReadoutCandidateRevision != GrammarRevisionID.Zero
            || state.ReadoutCandidateFingerprint != 0
            || state.ReadoutCandidateState.Version != 0;
        if (!currentTuplePresent)
            return !state.SuppressTrialPackets
                && state.ActiveTrialQuotaID.Value == 0
                && !state.PendingForcedTrialIntent.HasSeed;
        return state.ReadoutCandidateRevision == history.Scope.Revision
            && state.ReadoutCandidateFingerprint == history.Scope.CandidateFingerprint
            && state.ReadoutCandidateState == history.Scope.State
            && state.ReadoutCandidateOccurrenceDigest == history.Scope.OccurrenceDigest
            && ReadActivePolicyFingerprint(state) == history.ExecutionReadoutFingerprint;
    }

    internal bool TryReadPolicyTrialExecutionReceiptForQuota(
        CortexPolicyID policy,
        CortexPolicyQuotaDecisionID quotaID,
        out CortexPolicyTrialExecutionOutcomes outcome,
        out long requestCount,
        out long guardAdmittedCount,
        out CortexPolicyDecisionReadout lastRequestReadout,
        out CortexPolicyDecisionID lastRequestDecisionID,
        out int lastRequestStep,
        out CortexPolicyDecisionReadout executionReadout,
        out CortexPolicyDecisionID executionDecisionID,
        out ulong executionFingerprint,
        out int executionStep)
    {
        outcome = default;
        requestCount = 0;
        guardAdmittedCount = 0;
        lastRequestReadout = default;
        lastRequestDecisionID = default;
        lastRequestStep = -1;
        executionReadout = default;
        executionDecisionID = default;
        executionFingerprint = 0;
        executionStep = -1;
        if (quotaID.Value == 0) return false;
        PolicyState state = GetPolicy(policy);
        if (state.SuppressTrialPackets)
        {
            if (!state.ActiveTrialQuotaID.Equals(quotaID)
                || !TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID))
                return false;
        }
        else if (!state.HistoricalTrialExecution.IsPresent
            || !state.HistoricalTrialExecution.QuotaDecisionID.Equals(quotaID)
            || !TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID))
            return false;
        if (!TryReadPolicyTrialExecutionReceipt(policy, out outcome, out requestCount,
                out guardAdmittedCount, out lastRequestReadout, out lastRequestDecisionID,
                out lastRequestStep, out executionReadout, out executionDecisionID,
                out executionFingerprint, out executionStep))
            return false;
        return true;
    }

    internal bool TryReadPolicyTrialExecutionScopeForQuota(
        CortexPolicyID policy,
        CortexPolicyQuotaDecisionID quotaID,
        out PolicyVerifiedScopeEntry scope)
    {
        scope = default;
        if (quotaID.Value == 0) return false;
        PolicyState state = GetPolicy(policy);
        if (state.SuppressTrialPackets)
        {
            if (!state.ActiveTrialQuotaID.Equals(quotaID)
                || !TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID)) return false;
            return TryReadPolicyTrialExecutionScope(policy, out scope);
        }
        if (!state.HistoricalTrialExecution.IsPresent
            || !state.HistoricalTrialExecution.QuotaDecisionID.Equals(quotaID)
            || !TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID)) return false;
        try { state.HistoricalTrialExecution.Validate(policy, state.Schema.ActionCount); }
        catch (InvalidDataException) { return false; }
        scope = state.HistoricalTrialExecution.Scope;
        return scope.IsValid;
    }

    private void RecordRollbackDrill(
        PolicyState state,
        CortexPolicyID policy,
        int launchpadAction,
        int candidateAction,
        ulong candidateFingerprint,
        ReadOnlySpan<MetricSample> features,
        global::Cogito.Grammar.GrammarRevisionID revision)
    {
        if (state.AuthorityCeiling < CortexPolicyAuthorities.Grammar)
            return;
        int drillAction = SelectForcedDivergenceAction(
            candidateAction, launchpadAction, state.Schema.ActionCount,
            candidateFingerprint, state.Decisions);
        CortexPolicyDecisionID decisionID = new(_nextPolicyDecisionID++);
        CortexPolicyDecision drill = new(
            decisionID,
            policy,
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(
                launchpadAction, candidateAction, drillAction, drillAction,
                CortexPolicyAuthorities.Grammar, revision, state.ReadoutCandidateOccurrenceDigest,
                state.ReadoutCandidateFingerprint,
                ReadActivePolicyFingerprint(state),
                rollbackDrill: true));
        state.LastDecisionReadout = drill.Readout;
        state.Decisions++;
        if (state.Mode != CortexPolicyModes.Off && !state.SuppressTrialPackets)
            AppendPolicyDecision(in drill, features, state.Schema.ActionCount);
        MetricSample[] outcomes = new MetricSample[state.Schema.OutcomeCount];
        for (int i = 0; i < outcomes.Length; i++)
            outcomes[i] = new MetricSample(new MetricID((ushort)i), NumericValue.FromI64(0));
        state.Outcomes++;
        if (state.Mode != CortexPolicyModes.Off && !state.SuppressTrialPackets)
            AppendPolicyOutcome(in drill, outcomes, invariantClean: false, conservedCost: 0);
        state.RollbackDrillPending = false;
        state.RollbackDrillCompleted = true;
        SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
            CortexPolicyTrialDemotionReasons.RollbackDrill);
        state.Readmissions++;
        Trace.Cortex.Boundary("policy.drill",
            $"policy={policy} decision={decisionID} candidate={candidateAction} drill={drillAction} launchpad={launchpadAction} result=repromoted");
    }

    private static bool HasPositiveOutcome(ReadOnlySpan<MetricSample> outcomes)
    {
        for (int i = 0; i < outcomes.Length; i++)
        {
            NumericValue value = outcomes[i].Value;
            if (value.Kind == NumericKinds.I64 && value.GetI64() > 0) return true;
            if (value.Kind == NumericKinds.U64 && value.GetU64() > 0) return true;
            if (value.Kind == NumericKinds.F64 && value.GetF64() > 0) return true;
        }
        return false;
    }

    internal bool HasPolicyOccurrenceCheck(
        CortexPolicyID policy,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        GrammarRevisionID revision,
        out bool passed)
    {
        PolicyState state = GetPolicy(policy);
        bool identity = readoutFingerprint != 0 && candidateFingerprint != 0 && revision != GrammarRevisionID.Zero
            && state.AssayedReadoutFingerprint == readoutFingerprint
            && state.AssayedFingerprint == candidateFingerprint
            && state.VerifiedRevision == revision;
        passed = identity
            && state.VerifiedReadoutFingerprint == readoutFingerprint
            && state.VerifiedFingerprint == candidateFingerprint;
        return identity;
    }

    internal void SetPolicyTrialAuthority(
        CortexPolicyID policy,
        in CortexPolicyTrialAuthorityIdentity authorityIdentity,
        CortexPolicyAuthorities authority,
        int grammarExecutionQuota = -1,
        int actionOffset = 0,
        ulong? forcedDivergenceSeed = null,
        bool freezeAdaptation = false)
    {
        if (AllowsAutonomicSpawning)
            throw new InvalidOperationException("policy trial authority can be changed only inside a recursion-disabled fork");
        if (authority is not (CortexPolicyAuthorities.Launchpad
            or CortexPolicyAuthorities.Shadow
            or CortexPolicyAuthorities.Grammar))
            throw new ArgumentOutOfRangeException(nameof(authority), authority, "a policy trial must execute either its launchpad or its grammar");
        if (grammarExecutionQuota == 0 || grammarExecutionQuota < -1)
            throw new ArgumentOutOfRangeException(nameof(grammarExecutionQuota));
        PolicyState state = GetPolicy(policy);
        if (authority > state.AuthorityCeiling && authority == CortexPolicyAuthorities.Grammar)
            throw new InvalidOperationException($"policy '{policy}' trial authority {authority} exceeds configured ceiling {state.AuthorityCeiling}");
        CortexPolicyAuthorities effectiveAuthority = authority <= state.AuthorityCeiling
            ? authority
            : state.AuthorityCeiling;
        if (actionOffset < 0 || actionOffset >= state.Schema.ActionCount)
            throw new ArgumentOutOfRangeException(nameof(actionOffset));
        if (actionOffset != 0 && forcedDivergenceSeed.HasValue)
            throw new ArgumentException("a policy trial cannot combine action rotation with forced divergence", nameof(forcedDivergenceSeed));
        if (effectiveAuthority != CortexPolicyAuthorities.Grammar && forcedDivergenceSeed.HasValue)
            throw new ArgumentException("only grammar authority can run a forced-divergence trial", nameof(forcedDivergenceSeed));
        if (forcedDivergenceSeed.HasValue && state.Schema.ActionCount < 3)
            throw new InvalidOperationException($"policy '{policy}' requires at least three actions for a forced-divergence trial");
        ulong expectedProgramFingerprint = ReadActivePolicyFingerprint(state);
        ulong expectedCandidateFingerprint = ReadActivePolicyCandidateFingerprint(state);
        GrammarRevisionID expectedCandidateRevision = state.ReadoutCandidateRevision;
        if (authorityIdentity.ActiveProgramFingerprint.Value != expectedProgramFingerprint
            || authorityIdentity.CandidateFingerprint.Value != expectedCandidateFingerprint
            || authorityIdentity.CandidateRevision != expectedCandidateRevision)
        {
            string field = authorityIdentity.ActiveProgramFingerprint.Value != expectedProgramFingerprint
                ? "active_program_fingerprint"
                : authorityIdentity.CandidateFingerprint.Value != expectedCandidateFingerprint
                    ? "candidate_fingerprint"
                    : "candidate_revision";
            throw new InvalidOperationException(
                $"policy '{policy}' trial authority rejected field={field} "
                + $"expected_program={expectedProgramFingerprint:X16} actual_program={authorityIdentity.ActiveProgramFingerprint.Value:X16} "
                + $"expected_candidate={expectedCandidateFingerprint:X16} actual_candidate={authorityIdentity.CandidateFingerprint.Value:X16} "
                + $"expected_revision={expectedCandidateRevision.Value} actual_revision={authorityIdentity.CandidateRevision.Value} "
                + $"candidate={authorityIdentity.CandidateFingerprint.Value:X16} revision={authorityIdentity.CandidateRevision.Value} reason=identity-mismatch");
        }
        // A newly paid trial starts a new epoch; only this explicit admission
        // boundary may retire completed evidence from the prior quota.
        state.HistoricalTrialExecution = default;
        SetTrialAuthority(state, effectiveAuthority);
        state.TrialGrammarExecutionsRemaining = effectiveAuthority == CortexPolicyAuthorities.Grammar
            ? grammarExecutionQuota
            : -1;
        state.TrialActionOffset = effectiveAuthority == CortexPolicyAuthorities.Grammar ? actionOffset : 0;
            state.TrialForcedDivergenceSeed = effectiveAuthority == CortexPolicyAuthorities.Grammar
                ? forcedDivergenceSeed
                : null;
        state.TrialForcedCandidateCanonical = "";
        state.TrialForcedCandidateDigest = 0;
        state.TrialForcedFrontierRevision = 0;
        state.TrialForcedFrontierAuthoritySHA256 = "";
        state.ActiveTrialQuotaID = default;
        state.PendingForcedTrialIntent = forcedDivergenceSeed is ulong seed
            ? new CortexPolicyPendingForcedTrialIntent(
                policy, 0, CortexPolicyQuotaDecisions.Denied, seed, 0, -1, -1, 0, expectedCandidateFingerprint,
                expectedCandidateFingerprint, expectedProgramFingerprint, expectedCandidateRevision,
                state.ReadoutCandidateState,
                expectedProgramFingerprint, expectedCandidateFingerprint, expectedCandidateRevision,
                state.ReadoutCandidateOccurrenceDigest,
                state.ReadoutCandidateState, "", 0, 0, "", "")
            : default;
        state.TrialForcedDivergenceExecutions = 0;
        state.SuppressTrialPackets = true;
        state.TrialExecutionCause = forcedDivergenceSeed.HasValue
            ? CortexPolicySelectionCauses.TrialOverride
            : effectiveAuthority switch
            {
                CortexPolicyAuthorities.Launchpad => CortexPolicySelectionCauses.Launchpad,
                CortexPolicyAuthorities.Shadow => CortexPolicySelectionCauses.ShadowCandidate,
                CortexPolicyAuthorities.Grammar => CortexPolicySelectionCauses.GrammarCandidate,
                _ => throw new InvalidDataException("policy trial authority has no execution cause"),
            };
        state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
        state.TrialExecutionCorroboration = null;
        state.TrialExecutionReadoutFingerprint = 0;
        state.TrialExecutionStep = -1;
        state.TrialRequestCount = 0;
        state.TrialGuardAdmittedCount = 0;
        state.TrialLastRequest = null;
        state.TrialLastRequestStep = -1;
        state.TrialFrozen = freezeAdaptation;
    }

    internal void BindPolicyBoundaryForcedCandidate(
        CortexPolicyID policy,
        string candidateCanonical,
        ulong candidateDigest,
        ulong frontierRevision,
        string frontierAuthoritySHA256)
    {
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
        if (!domain.ValidateCandidateTransport(candidateCanonical, candidateDigest,
                frontierRevision, frontierAuthoritySHA256))
            throw new InvalidDataException("forced policy candidate transport is not authenticated by its domain");
        PolicyState state = GetPolicy(policy);
        if (!state.PendingForcedTrialIntent.HasSeed)
            throw new InvalidDataException("forced policy candidate transport has no pending trial intent");
        state.TrialForcedCandidateCanonical = candidateCanonical;
        state.TrialForcedCandidateDigest = candidateDigest;
        state.TrialForcedFrontierRevision = frontierRevision;
        state.TrialForcedFrontierAuthoritySHA256 = frontierAuthoritySHA256;
    }

    internal bool TryReadPolicyBoundaryForcedCandidate(
        CortexPolicyID policy,
        out string candidateCanonical,
        out ulong candidateDigest,
        out ulong frontierRevision,
        out string frontierAuthoritySHA256)
    {
        PolicyState state = GetPolicy(policy);
        candidateCanonical = state.TrialForcedCandidateCanonical;
        candidateDigest = state.TrialForcedCandidateDigest;
        frontierRevision = state.TrialForcedFrontierRevision;
        frontierAuthoritySHA256 = state.TrialForcedFrontierAuthoritySHA256;
        return candidateCanonical.Length != 0 && candidateDigest != 0
            && frontierRevision != 0 && frontierAuthoritySHA256.Length == 64;
    }

    internal void BindActiveTrialQuotaIdentity(
        CortexPolicyID policy,
        CortexPolicyQuotaDecisionID quotaID,
        string auditOnlyDigest)
    {
        if (quotaID.Value == 0 || !IsAuthenticatedAuditOnlyDigest(auditOnlyDigest))
            throw new InvalidDataException("active policy trial quota identity is incomplete");
        PolicyState state = GetPolicy(policy);
        if (state.HistoricalTrialExecution.IsPresent)
            throw new InvalidDataException("completed policy trial execution cannot be rebound");
        if (!state.SuppressTrialPackets)
            throw new InvalidDataException("active policy trial quota identity requires a suppressed trial");
        if (state.PendingForcedTrialIntent.HasSeed)
        {
            if (!state.PendingForcedTrialIntent.IsBound)
                throw new InvalidDataException("active policy trial quota identity precedes forced intent audit-only binding");
            if (state.PendingForcedTrialIntent.QuotaID != quotaID.Value)
                throw new InvalidDataException("active policy trial quota identity disagrees with forced intent");
        }
        if (!TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID, auditOnlyDigest))
            throw new InvalidDataException("active policy trial quota identity has no authenticated durable authority");
        state.ActiveTrialQuotaID = quotaID;
    }

    internal void RestorePaidPolicyTrialEpoch(
        CortexPolicyID policy,
        CortexPolicyQuotaDecisionID quotaID,
        string auditOnlyDigest,
        CortexPolicySelectionCauses executionCause,
        ulong? forcedDivergenceSeed,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong occurrenceDigest,
        GrammarRevisionID candidateRevision)
    {
        if (quotaID.Value == 0 || !IsAuthenticatedAuditOnlyDigest(auditOnlyDigest))
            throw new InvalidDataException("paid policy trial epoch audit-only is incomplete");
        if (!TryResolvePolicyTrialAuthority(executionCause, out CortexPolicyAuthorities requiredAuthority))
            throw new InvalidDataException("paid policy trial epoch has an invalid execution cause");
        if (forcedDivergenceSeed.HasValue != (executionCause == CortexPolicySelectionCauses.TrialOverride)
            || (forcedDivergenceSeed is 0))
            throw new InvalidDataException("paid policy trial epoch cause and forced seed disagree");
        PolicyState state = GetPolicy(policy);
        if (state.HistoricalTrialExecution.IsPresent)
            throw new InvalidDataException("paid policy trial epoch has completed history; continuation must read history");
        bool exactPersistedEpoch = state.SuppressTrialPackets
            && state.ActiveTrialQuotaID.Equals(quotaID)
            && state.Authority == requiredAuthority
            && !state.ReadoutCandidatePending
            && state.ReadoutCandidateState == canonicalState
            && state.ReadoutCandidateSetDigest == readoutFingerprint
            && state.ReadoutCandidateFingerprint == candidateFingerprint
            && state.ReadoutCandidateOccurrenceDigest == occurrenceDigest
            && state.ReadoutCandidateRevision == candidateRevision
            && state.TrialExecutionCause == executionCause
            && state.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
            && state.TrialForcedDivergenceSeed == forcedDivergenceSeed
            && state.TrialForcedDivergenceExecutions == 0
            && state.TrialExecutionCorroboration is null
            && state.TrialExecutionReadoutFingerprint == 0
            && state.TrialExecutionStep == -1
            && state.TrialRequestCount == 0
            && state.TrialGuardAdmittedCount == 0
            && state.TrialLastRequest is null
            && state.TrialLastRequestStep == -1;
        if (!exactPersistedEpoch)
            throw new InvalidDataException("paid policy trial epoch is not the exact persisted active tuple");
        if (executionCause == CortexPolicySelectionCauses.TrialOverride)
        {
            CortexPolicyPendingForcedTrialIntent pending = state.PendingForcedTrialIntent;
            if (!pending.HasSeed || !pending.IsBound || pending.QuotaID != quotaID.Value
                || pending.ForcedDivergenceSeed != forcedDivergenceSeed.GetValueOrDefault()
                || pending.CanonicalState != canonicalState
                || pending.ReadoutFingerprint != readoutFingerprint
                || pending.CandidateFingerprint != candidateFingerprint
                || pending.CandidateRevision != candidateRevision
                || pending.SuccessorOccurrenceDigest != occurrenceDigest
                || !TryAuthenticatePendingForcedTrialIntentAuthority(in pending))
                throw new InvalidDataException("paid forced trial epoch intent is not authenticated for its successor scope");
        }
        else if (state.PendingForcedTrialIntent.HasSeed)
        {
            throw new InvalidDataException("ordinary paid trial epoch carries a forced intent");
        }
        if (!TryAuthenticatePaidTrialEpoch(state, policy, quotaID, auditOnlyDigest, executionCause,
                in canonicalState, readoutFingerprint, candidateFingerprint, occurrenceDigest, candidateRevision,
                out _))
        {
            bool hasScope = state.VerifiedScopes.TryGetValue(canonicalState, out PolicyVerifiedScopeEntry scope);
            bool hasCandidate = state.CanonicalCandidates.TryGetValue(canonicalState,
                out PolicyState.CanonicalCandidateEvidence? candidate);
            bool quotaAuthenticated = TryAuthenticatePolicyTrialQuotaIdentity(state, quotaID, auditOnlyDigest);
            throw new InvalidDataException(
                $"paid policy trial epoch is not the authenticated executable scope: quota={quotaID} cause={executionCause} authority={state.Authority} ceiling={state.AuthorityCeiling} suppress={(state.SuppressTrialPackets ? 1 : 0)} active={state.ActiveTrialQuotaID} state={canonicalState} current_state={state.ReadoutCandidateState} program={readoutFingerprint:X16}/{state.ReadoutCandidateSetDigest:X16} candidate={candidateFingerprint:X16}/{state.ReadoutCandidateFingerprint:X16} occurrence={occurrenceDigest:X16}/{state.ReadoutCandidateOccurrenceDigest:X16} revision={candidateRevision.Value}/{state.ReadoutCandidateRevision.Value} scope={(hasScope ? 1 : 0)} scope_tuple={(hasScope ? $"{scope.ReadoutFingerprint:X16}/{scope.CandidateFingerprint:X16}/{scope.OccurrenceDigest:X16}/{scope.Revision.Value}" : "none")} evidence={(hasCandidate ? 1 : 0)} evidence_tuple={(hasCandidate ? $"{candidate!.CandidateFingerprint:X16}/{candidate.OccurrenceDigest:X16}/{candidate.Revision.Value}" : "none")} quota_auth={(quotaAuthenticated ? 1 : 0)}");
        }
    }

    private bool TryBindVerifiedSuccessorTrialEpoch(
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialIntent successorIntent,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong occurrenceDigest,
        GrammarRevisionID candidateRevision)
    {
        PolicyState state = GetPolicy(policy);
        IPolicyBoundaryDomain? domain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredDomain)
            ? registeredDomain : null;
        bool requiresScope = RequiresCanonicalScope(domain);
        if (state.HistoricalTrialExecution.IsPresent
            || !successorIntent.HasSeed
            || !successorIntent.IsBound
            || !successorIntent.Policy.Equals(policy)
            || successorIntent.QuotaID == 0
            || !TryAuthenticatePendingForcedTrialIntentAuthority(in successorIntent)
            || state.AuthorityCeiling < CortexPolicyAuthorities.Grammar
            || !ValidateCanonicalState(domain, policy, in canonicalState)
            || !state.ReadoutCandidateState.Equals(canonicalState)
            || state.ReadoutCandidatePending
            || state.ReadoutCandidateSetDigest != readoutFingerprint
            || ReadActivePolicyFingerprint(state) != readoutFingerprint
            || state.ReadoutCandidateFingerprint != candidateFingerprint
            || state.ReadoutCandidateOccurrenceDigest != occurrenceDigest
            || state.ReadoutCandidateRevision != candidateRevision
            || requiresScope && (!state.VerifiedScopes.TryGetValue(canonicalState, out PolicyVerifiedScopeEntry scope)
                || !scope.IsValid
                || scope.State != canonicalState
                || scope.ReadoutFingerprint != readoutFingerprint
                || scope.CandidateFingerprint != candidateFingerprint
                || scope.OccurrenceDigest != occurrenceDigest
                || scope.Revision != candidateRevision
                || !state.CanonicalCandidates.TryGetValue(canonicalState, out PolicyState.CanonicalCandidateEvidence? candidate)
                || candidate.CandidateFingerprint != candidateFingerprint
                || candidate.OccurrenceDigest != occurrenceDigest
                || candidate.Revision != candidateRevision))
            return false;

        state.PendingForcedTrialIntent = successorIntent;
        state.Authority = CortexPolicyAuthorities.Grammar;
        state.ActiveTrialQuotaID = new CortexPolicyQuotaDecisionID(successorIntent.QuotaID);
        state.SuppressTrialPackets = true;
        state.TrialGrammarExecutionsRemaining = -1;
        state.TrialActionOffset = 0;
        state.TrialForcedDivergenceSeed = successorIntent.ForcedDivergenceSeed;
        state.TrialForcedDivergenceExecutions = 0;
        state.TrialExecutionCause = CortexPolicySelectionCauses.TrialOverride;
        state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
        state.TrialExecutionCorroboration = null;
        state.TrialExecutionReadoutFingerprint = 0;
        state.TrialExecutionStep = -1;
        state.TrialRequestCount = 0;
        state.TrialGuardAdmittedCount = 0;
        state.TrialLastRequest = null;
        state.TrialLastRequestStep = -1;
        state.TrialFrozen = false;
        return true;
    }

    internal void BindPendingForcedTrialIntent(
        CortexPolicyID policy,
        ulong quotaID,
        CortexPolicyQuotaDecisions sourceQuotaDecision,
        ulong sourceDecisionID,
        long sourceDecisionEventID,
        long sourceCorroborationEventID,
        ulong sourceOccurrenceDigest,
        ulong sourceCandidateFingerprint,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        GrammarRevisionID candidateRevision,
        in PolicyCanonicalStateID canonicalState,
        string obligationID,
        byte arm,
        ushort featureID,
        string sourceRunID,
        string auditOnlyDigest)
    {
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
        PolicyState state = GetPolicy(policy);
        if (state.HistoricalTrialExecution.IsPresent)
            throw new InvalidDataException("completed policy trial execution cannot receive a pending forced intent");
        CortexPolicyPendingForcedTrialIntent pending = state.PendingForcedTrialIntent;
        if (!pending.HasSeed || pending.ForcedDivergenceSeed == 0)
            throw new InvalidDataException($"policy '{policy}' has no forced trial intent to bind");
        if (quotaID == 0 || sourceQuotaDecision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            || sourceDecisionID == 0 || sourceDecisionEventID <= 0 || sourceCorroborationEventID <= 0
            || sourceOccurrenceDigest == 0 || sourceCandidateFingerprint == 0 || readoutFingerprint == 0
            || candidateFingerprint == 0 || candidateRevision == GrammarRevisionID.Zero
            || !domain.PolicyID.Equals(policy)
            || !domain.PolicyBinding.PolicyID.Equals(policy)
            || !domain.ValidateCanonicalState(in canonicalState)
            || string.IsNullOrWhiteSpace(obligationID) || string.IsNullOrWhiteSpace(sourceRunID)
            || !IsAuthenticatedAuditOnlyDigest(auditOnlyDigest))
            throw new InvalidDataException("forced policy trial intent audit-only is incomplete");
        if (arm != (byte)PolicyBoundaryArms.ForcedDivergentNull
            || featureID != domain.BoundaryFeatureID
            || !_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || !string.Equals(obligation.ID.Value, obligationID, StringComparison.Ordinal))
            throw new InvalidDataException("forced policy trial intent audit-only names the wrong boundary arm");
        if (pending.SourceCandidateFingerprint != 0 && pending.SourceCandidateFingerprint != sourceCandidateFingerprint)
            throw new InvalidDataException("forced policy trial intent source candidate identity changed before audit-only binding");
        if (pending.SourceReadoutFingerprint != 0 && pending.SourceReadoutFingerprint != readoutFingerprint)
            throw new InvalidDataException("forced policy trial intent source readout identity changed before audit-only binding");
        if (pending.SourceCandidateRevision != GrammarRevisionID.Zero && pending.SourceCandidateRevision != candidateRevision)
            throw new InvalidDataException("forced policy trial intent source revision changed before audit-only binding");
        if (pending.CandidateFingerprint != 0
            && pending.CandidateFingerprint != candidateFingerprint
            && pending.CandidateFingerprint != pending.SourceCandidateFingerprint)
            throw new InvalidDataException("forced policy trial intent candidate identity changed before audit-only binding");
        PolicyCanonicalStateID pendingSourceState = pending.SourceCanonicalState;
        CortexPolicyPendingForcedTrialIntent bound = pending with
        {
            QuotaID = quotaID,
            SourceQuotaDecision = sourceQuotaDecision,
            SourceDecisionID = sourceDecisionID,
            SourceDecisionEventID = sourceDecisionEventID,
            SourceCorroborationEventID = sourceCorroborationEventID,
            SourceOccurrenceDigest = sourceOccurrenceDigest,
            SourceCandidateFingerprint = sourceCandidateFingerprint,
            // Binding discovers the current successor tuple; it must not rewrite
            // the immutable source quota audit-only that authorized the intent.
            SourceQuotaCandidateFingerprint = pending.SourceQuotaCandidateFingerprint == 0
                ? sourceCandidateFingerprint : pending.SourceQuotaCandidateFingerprint,
            SourceReadoutFingerprint = pending.SourceReadoutFingerprint == 0
                ? readoutFingerprint : pending.SourceReadoutFingerprint,
            SourceCandidateRevision = pending.SourceCandidateRevision == GrammarRevisionID.Zero
                ? candidateRevision : pending.SourceCandidateRevision,
            SourceCanonicalState = ValidateCanonicalState(domain, policy, in pendingSourceState)
                ? pending.SourceCanonicalState : canonicalState,
            ReadoutFingerprint = readoutFingerprint,
            CandidateFingerprint = candidateFingerprint,
            CandidateRevision = candidateRevision,
            SuccessorOccurrenceDigest = sourceOccurrenceDigest,
            CanonicalState = canonicalState,
            ObligationID = obligationID,
            Arm = arm,
            FeatureID = featureID,
            SourceRunID = sourceRunID,
            AuditOnlyDigest = auditOnlyDigest,
        };
        if (!TryAuthenticatePendingForcedTrialIntentAuthority(in bound))
            throw new InvalidDataException("forced policy trial intent has no authenticated durable audit-only");
        state.PendingForcedTrialIntent = bound;
    }

    private static int SelectForcedDivergenceAction(
        int rawGrammarAction,
        int launchpadAction,
        int actionCount,
        ulong seed,
        ulong execution)
    {
        int allowedCount = actionCount - (rawGrammarAction == launchpadAction ? 1 : 2);
        if (allowedCount <= 0)
            throw new InvalidOperationException("a forced-divergence trial has no action outside its grammar and launchpad rails");

        ulong state;
        unchecked
        {
            state = seed + execution * 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;
        }
        int rank = (int)((state ^ (state >> 31)) % (ulong)allowedCount);
        for (int action = 0; action < actionCount; action++)
        {
            if (action == rawGrammarAction || action == launchpadAction) continue;
            if (rank-- == 0) return action;
        }

        throw new InvalidOperationException("forced-divergence action selection exhausted its valid rails");
    }

    internal void RecordPolicyOccurrenceCheck(
        CortexPolicyID policy,
        ulong readoutFingerprint,
        int comparisons,
        int agreements,
        int failures,
        bool passed,
        PolicyCanonicalCoverageReceipt coverage = default)
    {
        PolicyState state = GetPolicy(policy);
        ulong candidateFingerprint = ReadActivePolicyCandidateFingerprint(state);
        GrammarRevisionID revision = state.ReadoutCandidateRevision;
        if (readoutFingerprint == 0 || candidateFingerprint == 0 || revision == GrammarRevisionID.Zero
            || ReadActivePolicyFingerprint(state) != readoutFingerprint)
            throw new InvalidDataException($"policy '{policy}' occurrence check does not name the active readout, candidate, and revision");
        state.AssayedReadoutFingerprint = readoutFingerprint;
        state.AssayedFingerprint = candidateFingerprint;
        if (passed)
        {
            state.VerifiedReadoutFingerprint = readoutFingerprint;
            state.VerifiedFingerprint = candidateFingerprint;
            state.VerifiedRevision = revision;
        }
        else if (state.VerifiedReadoutFingerprint == readoutFingerprint)
        {
            state.VerifiedReadoutFingerprint = 0;
            state.VerifiedFingerprint = 0;
            state.VerifiedRevision = GrammarRevisionID.Zero;
        }
        AppendPolicyOccurrenceCheckReceipt(policy, readoutFingerprint, candidateFingerprint, revision,
            comparisons, agreements, failures, passed, coverage);
        if (_runtimeTape is null || _runtimeJournal is null) return;
        TapePacketCreator.AppendPolicyOccurrenceCheck(
            _runtimeTape, _runtimeJournal, Step, policy, readoutFingerprint,
            comparisons, agreements, failures, passed);
    }

    internal void RecordVerifiedPolicyReadout(
        CortexPolicyID policy,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        GrammarRevisionID revision)
    {
        if (readoutFingerprint == 0 || candidateFingerprint == 0 || revision == GrammarRevisionID.Zero)
            throw new ArgumentException("verified policy readout identities must be non-zero");
        PolicyState state = GetPolicy(policy);
        state.AssayedReadoutFingerprint = readoutFingerprint;
        state.AssayedFingerprint = candidateFingerprint;
        state.VerifiedReadoutFingerprint = readoutFingerprint;
        state.VerifiedFingerprint = candidateFingerprint;
        state.VerifiedRevision = revision;
    }

    private void AppendPolicyOccurrenceCheckReceipt(
        CortexPolicyID policy,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        GrammarRevisionID revision,
        int comparisons,
        int agreements,
        int failures,
        bool passed,
        PolicyCanonicalCoverageReceipt coverage = default)
    {
        if (_runtimeRun is null) return;
        PolicyCanonicalStateID canonicalState = GetPolicy(policy).ReadoutCandidateState;
        if (!canonicalState.Policy.Equals(policy))
            canonicalState = new PolicyCanonicalStateID(policy, (PolicyCanonicalStateKinds)0, 0, 0);
        string row = policy.Value + "\t"
            + readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + revision.Value.ToString(CultureInfo.InvariantCulture) + "\t"
            + comparisons.ToString(CultureInfo.InvariantCulture) + "\t"
            + agreements.ToString(CultureInfo.InvariantCulture) + "\t"
            + failures.ToString(CultureInfo.InvariantCulture) + "\t"
            + (passed ? "1" : "0") + "\t"
            + coverage.RequiredStateCount.ToString(CultureInfo.InvariantCulture) + "\t"
            + coverage.CoveredStateCount.ToString(CultureInfo.InvariantCulture) + "\t"
            + coverage.MissingStateCount.ToString(CultureInfo.InvariantCulture) + "\t"
            + coverage.RequiredStatesDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + coverage.CoveredStatesDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + coverage.MissingStatesDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + coverage.Attribution.ToString() + "\t"
            + canonicalState.Policy.Value + "\t"
            + ((byte)canonicalState.Kind).ToString(CultureInfo.InvariantCulture) + "\t"
            + canonicalState.Version.ToString(CultureInfo.InvariantCulture) + "\t"
            + canonicalState.Value.ToString("X16", CultureInfo.InvariantCulture);
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyOccurrenceCheckReceiptFile),
            PolicyOccurrenceCheckReceiptHeader, row);
        if (coverage.Entries is not null)
        {
            coverage.Validate();
            AppendPolicyOccurrenceCheckCoverageReceipt(policy, readoutFingerprint, in coverage);
        }
    }

    private void AppendPolicyOccurrenceCheckCoverageReceipt(
        CortexPolicyID policy,
        ulong fingerprint,
        in PolicyCanonicalCoverageReceipt coverage)
    {
        if (_runtimeRun is null || coverage.Entries is null) return;
        for (int i = 0; i < coverage.Entries.Length; i++)
        {
            PolicyCanonicalCoverageEntry entry = coverage.Entries[i];
            string row = policy.Value + "\t"
                + fingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + Step.ToString(CultureInfo.InvariantCulture) + "\t"
                + coverage.RequiredStateCount.ToString(CultureInfo.InvariantCulture) + "\t"
                + coverage.CoveredStateCount.ToString(CultureInfo.InvariantCulture) + "\t"
                + coverage.MissingStateCount.ToString(CultureInfo.InvariantCulture) + "\t"
                + coverage.RequiredStatesDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + coverage.CoveredStatesDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + coverage.MissingStatesDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + coverage.Attribution + "\t"
                + coverage.VerifierComparisons.ToString(CultureInfo.InvariantCulture) + "\t"
                + coverage.VerifierAgreements.ToString(CultureInfo.InvariantCulture) + "\t"
                + coverage.VerifierMisses.ToString(CultureInfo.InvariantCulture) + "\t"
                + ((byte)entry.State.Kind).ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.State.Version.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.State.Value.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + (entry.Covered ? "1" : "0") + "\t"
                + entry.Action.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + entry.OccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                + entry.Revision.Value.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.OriginRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.InstalledStep.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.Comparisons.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.Agreements.ToString(CultureInfo.InvariantCulture) + "\t"
                + entry.Misses.ToString(CultureInfo.InvariantCulture);
            _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyOccurrenceCheckCoverageReceiptFile), PolicyOccurrenceCheckCoverageReceiptHeader, row);
        }
    }

    internal void RestorePolicyOccurrenceCheckReceipts()
    {
        if (_runtimeRun is null || _runtimeTape is null || _runtimeJournal is null) return;
        string path = _runtimeRun.PathOf(PolicyOccurrenceCheckReceiptFile);
        if (!File.Exists(path)) return;
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (!text.EndsWith('\n')) text = text[..Math.Max(0, text.LastIndexOf('\n') + 1)];
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<CortexPolicyID, (CortexPolicyID Policy, ulong ReadoutFingerprint, ulong CandidateFingerprint, GrammarRevisionID Revision,
            int Comparisons, int Agreements, int Failures, bool Passed, int Required, int Covered, int Missing,
            ulong RequiredDigest, ulong CoveredDigest, ulong MissingDigest, PolicyCanonicalCoverageAttributions Attribution,
            PolicyCanonicalStateID CanonicalState)> latest = new();
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            // Strict succession receipts carry the complete readout/candidate/revision
            // tuple.  Legacy candidate-only rows are deliberately inadmissible: replaying
            // one would silently mint authority without a provenance-bearing readout.
            if (columns.Length != 19) throw new InvalidDataException("policy occurrence check receipt row lacks strict identity columns");
            CortexPolicyID policy = new(columns[0]);
            if (!ulong.TryParse(columns[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong readoutFingerprint)
                || !ulong.TryParse(columns[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong candidateFingerprint)
                || !ulong.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong revisionValue))
                throw new InvalidDataException("policy occurrence check receipt has malformed strict identity");
            GrammarRevisionID revision = new(revisionValue);
            if (readoutFingerprint == 0 || candidateFingerprint == 0 || revision == GrammarRevisionID.Zero)
                throw new InvalidDataException("policy occurrence check receipt has zero strict identity");
            if (!int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int comparisons)
                || !int.TryParse(columns[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int agreements)
                || !int.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int failures)
                || comparisons < 0 || agreements < 0 || failures < 0 || agreements > comparisons || failures > comparisons
                || (columns[7] is not ("0" or "1"))
                || !int.TryParse(columns[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int required)
                || !int.TryParse(columns[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int covered)
                || !int.TryParse(columns[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out int missing)
                || required < 0 || covered < 0 || missing < 0 || covered > required || missing > required
                || !ulong.TryParse(columns[11], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong requiredDigest)
                || !ulong.TryParse(columns[12], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong coveredDigest)
                || !ulong.TryParse(columns[13], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong missingDigest))
                throw new InvalidDataException("policy occurrence check receipt has malformed occurrence check evidence");
            if (!Enum.TryParse(columns[14], ignoreCase: false, out PolicyCanonicalCoverageAttributions attribution)
                || !Enum.IsDefined(attribution))
                throw new InvalidDataException("policy occurrence check receipt has an invalid canonical coverage attribution");
            if (!byte.TryParse(columns[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte stateKind)
                || !ushort.TryParse(columns[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort stateVersion)
                || !ulong.TryParse(columns[18], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong stateValue))
                throw new InvalidDataException("policy occurrence check receipt has malformed canonical state");
            PolicyCanonicalStateID canonicalState = new(new CortexPolicyID(columns[15]),
                (PolicyCanonicalStateKinds)stateKind, stateVersion, stateValue);
            // The policy column is the authoritative owner. Boundary domains own
            // state validation; dynamic domains cannot inherit a finite catalog.
            IPolicyBoundaryDomain? receiptDomain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredReceiptDomain)
                ? registeredReceiptDomain : null;
            if (!canonicalState.Policy.Equals(policy)
                || RequiresCanonicalScope(receiptDomain) && !ValidateCanonicalState(receiptDomain, policy, in canonicalState))
                throw new InvalidDataException("policy occurrence check receipt canonical state is not policy-bound");
            // History is parsed and validated in full, but only the latest row whose
            // identity exactly names this checkpoint's active tuple can affect it.  Rows
            // from prior install revisions remain audit history, never current authority.
            if (_policies.TryGetValue(policy, out PolicyState? state)
                && ReadActivePolicyFingerprint(state) == readoutFingerprint
                && state.ReadoutCandidateFingerprint == candidateFingerprint
                && state.ReadoutCandidateRevision == revision)
            {
                latest[policy] = (policy, readoutFingerprint, candidateFingerprint, revision,
                    comparisons, agreements, failures, columns[7] == "1", required, covered, missing,
                    requiredDigest, coveredDigest, missingDigest, attribution, canonicalState);
            }
        }
        foreach (KeyValuePair<CortexPolicyID, PolicyState> pair in _policies)
        {
            CortexPolicyID policy = pair.Key;
            PolicyState state = pair.Value;
            bool hasAssay = state.AssayedReadoutFingerprint != 0 || state.AssayedFingerprint != 0;
            bool boundaryVerified = IsBoundaryVerifiedPolicy(policy, state);
            if (boundaryVerified) continue;
            if (hasAssay)
            {
                bool hasLatest = latest.TryGetValue(policy, out var receipt);
                if (!hasLatest
                    || receipt.ReadoutFingerprint != state.AssayedReadoutFingerprint
                    || receipt.CandidateFingerprint != state.AssayedFingerprint
                    || receipt.Revision != state.ReadoutCandidateRevision
                    || (policy.Equals(Homeostat.PolicyID) && receipt.CanonicalState != state.ReadoutCandidateState)
                    || receipt.Passed != (state.VerifiedRevision != GrammarRevisionID.Zero
                        && state.VerifiedReadoutFingerprint == receipt.ReadoutFingerprint
                        && state.VerifiedFingerprint == receipt.CandidateFingerprint
                        && state.VerifiedRevision == receipt.Revision))
                    throw new InvalidDataException(
                        $"policy occurrence check receipt for '{policy}' is missing or conflicts with its restored assay: active={ReadActivePolicyFingerprint(state):X16}/{state.ReadoutCandidateFingerprint:X16}/{state.ReadoutCandidateRevision.Value}/{state.ReadoutCandidateState} assayed={state.AssayedReadoutFingerprint:X16}/{state.AssayedFingerprint:X16} verified={state.VerifiedReadoutFingerprint:X16}/{state.VerifiedFingerprint:X16}/{state.VerifiedRevision.Value} latest={(hasLatest ? $"{receipt.ReadoutFingerprint:X16}/{receipt.CandidateFingerprint:X16}/{receipt.Revision.Value}/{receipt.CanonicalState}/passed={(receipt.Passed ? 1 : 0)}" : "none")}");
                continue;
            }
            if (!latest.TryGetValue(policy, out var current)) continue;
            state.AssayedReadoutFingerprint = current.ReadoutFingerprint;
            state.AssayedFingerprint = current.CandidateFingerprint;
            state.VerifiedReadoutFingerprint = current.Passed ? current.ReadoutFingerprint : 0;
            state.VerifiedFingerprint = current.Passed ? current.CandidateFingerprint : 0;
            state.VerifiedRevision = current.Passed ? current.Revision : GrammarRevisionID.Zero;
            TapePacketCreator.AppendPolicyOccurrenceCheck(
                _runtimeTape, _runtimeJournal, Step, policy, current.ReadoutFingerprint,
                current.Comparisons, current.Agreements, current.Failures, current.Passed);
            Trace.Cortex.Boundary("policy.restore",
                $"policy={policy} readout={current.ReadoutFingerprint:X16} candidate={current.CandidateFingerprint:X16} revision={current.Revision.Value} agreement={current.Agreements}/{current.Comparisons} result={(current.Passed ? "PASS" : "FAIL")}");
        }
    }

    private bool IsBoundaryVerifiedPolicy(CortexPolicyID policy, PolicyState state)
    {
        if (state.VerifiedRevision == GrammarRevisionID.Zero
            || state.VerifiedReadoutFingerprint == 0
            || state.VerifiedFingerprint == 0
            || !_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || obligation.Receipt is not PolicyBoundaryForkReceipt receipt
            || !receipt.Verified)
            return false;
        return receipt.SourceDecisionReadoutFingerprint == state.VerifiedReadoutFingerprint
            && receipt.SourceDecisionCandidateFingerprint == state.VerifiedFingerprint
            && receipt.SourceDecisionReadoutRevision == state.VerifiedRevision.Value;
    }

    internal void AdvancePolicyInstallRevision(in global::Cogito.Grammar.InstallRevision installRevision)
    {
        foreach (PolicyState state in _policies.Values)
        {
            if (state.ObservedInstallRevision == installRevision.Revision) continue;
            GrammarRevisionID priorCandidateRevision = state.ReadoutCandidateRevision;
            ulong priorCandidateFingerprint = state.ReadoutCandidateFingerprint;
            bool priorTrialOverride = state.TrialForcedDivergenceSeed.HasValue || state.TrialActionOffset != 0;
            PolicyCanonicalStateID currentCandidateState = state.ReadoutCandidateState;
            bool preservePaidTrialEpoch = state.ActiveTrialQuotaID.Value != 0
                && TryAuthenticatePaidTrialEpoch(state, state.Schema.Policy,
                    state.ActiveTrialQuotaID, auditOnlyDigest: null, state.TrialExecutionCause,
                    in currentCandidateState, ReadActivePolicyFingerprint(state),
                    state.ReadoutCandidateFingerprint, state.ReadoutCandidateOccurrenceDigest,
                    state.ReadoutCandidateRevision, out _);
            if (state.PendingForcedTrialIntent is { HasSeed: true, IsBound: true } pendingIntent)
                Trace.Cortex.Boundary("policy.trial-intent-drift",
                    $"policy={state.Schema.Policy} quota={pendingIntent.QuotaID:X} source_readout={pendingIntent.SourceReadoutFingerprint:X16} source_candidate={pendingIntent.SourceCandidateFingerprint:X16} source_revision={pendingIntent.SourceCandidateRevision.Value} prior_readout={ReadActivePolicyFingerprint(state):X16} prior_candidate={priorCandidateFingerprint:X16} prior_revision={priorCandidateRevision.Value} install revision={installRevision.Revision.Value} state={pendingIntent.CanonicalState}");
            state.ObservedInstallRevision = installRevision.Revision;
            state.ReadoutCache.MoveToRevision(installRevision.Revision);
            if (preservePaidTrialEpoch)
            {
                // install revision is not a new trial epoch.  Keep the authenticated
                // candidate, scope, quota, cause, seed, and corroboration tuple intact;
                // the next arm action will consume this exact paid continuation.
                Trace.Cortex.Boundary("policy.trial-epoch-carried",
                    $"policy={state.Schema.Policy} quota={state.ActiveTrialQuotaID} revision={priorCandidateRevision.Value} candidate={priorCandidateFingerprint:X16} install revision={installRevision.Revision.Value}");
                continue;
            }
            // install revision advance changes validation provenance, not the learned program.
            // Keep every state -> action mapping and its accumulated proof; the next lookup
            // lazily refreshes only the selected state against the new install revision.
            if (state.CanonicalCandidates.Count != 0)
            {
                RefreshCanonicalProgramDigest(state, state.Schema.Policy);
                TouchCanonicalCoverage(state);
                state.ReadoutCandidateRevision = GrammarRevisionID.Zero;
                state.ReadoutCandidateFingerprint = 0;
                state.ReadoutCandidateState = default;
                state.ReadoutCandidateOccurrenceDigest = 0;
                state.ReadoutCandidateAction = -1;
                state.AssayedFingerprint = 0;
                state.VerifiedFingerprint = 0;
                state.AssayedReadoutFingerprint = 0;
                state.VerifiedReadoutFingerprint = 0;
                state.VerifiedRevision = GrammarRevisionID.Zero;
                state.ReadoutCandidatePending = true;
                state.VerifiedScopes.Clear();
            }
            else
            {
                state.ReadoutCandidateRevision = GrammarRevisionID.Zero;
                state.ReadoutCandidateFingerprint = 0;
                state.ReadoutCandidateSetDigest = 0;
                state.ReadoutCandidateState = default;
                state.ReadoutCandidateOccurrenceDigest = 0;
                state.ReadoutCandidateAction = -1;
                state.ShadowComparisons = 0;
                state.ShadowAgreements = 0;
                state.EmulationMisses = 0;
                state.AssayedFingerprint = 0;
                state.VerifiedFingerprint = 0;
                state.AssayedReadoutFingerprint = 0;
                state.VerifiedReadoutFingerprint = 0;
                state.VerifiedRevision = GrammarRevisionID.Zero;
                state.ReadoutCandidatePending = true;
            }
            state.TrialGrammarExecutionsRemaining = -1;
            state.TrialActionOffset = 0;
            state.TrialForcedDivergenceSeed = null;
            state.ActiveTrialQuotaID = default;
            state.PendingForcedTrialIntent = default;
            state.TrialForcedDivergenceExecutions = 0;
            state.SuppressTrialPackets = false;
            state.TrialExecutionCause = CortexPolicySelectionCauses.Launchpad;
            state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
            state.TrialExecutionCorroboration = null;
            state.TrialExecutionReadoutFingerprint = 0;
            state.TrialExecutionStep = -1;
            state.TrialRequestCount = 0;
            state.TrialGuardAdmittedCount = 0;
            state.TrialLastRequest = null;
            state.TrialLastRequestStep = -1;
            if (state.Authority == CortexPolicyAuthorities.Grammar)
            {
                SetTrialAuthority(state, CortexPolicyAuthorities.Shadow,
                    CortexPolicyTrialDemotionReasons.ReadoutRevisionChanged,
                    candidateRevision: priorCandidateRevision,
                    candidateFingerprint: priorCandidateFingerprint,
                    trialOverrideClearedOnRevisionDrift: priorTrialOverride);
                state.Readmissions++;
            }
        }
    }

    private static double[] CopyNumericFeatures(ReadOnlySpan<MetricSample> features)
    {
        double[] values = new double[features.Length];
        for (int i = 0; i < features.Length; i++)
        {
            NumericValue value = features[i].Value;
            values[i] = value.Kind switch
            {
                NumericKinds.I64 => value.GetI64(),
                NumericKinds.U64 => value.GetU64(),
                NumericKinds.F64 => value.GetF64(),
                _ => throw new InvalidDataException($"unknown numeric feature kind {value.Kind}"),
            };
        }
        return values;
    }

    private static void WritePolicyCheckpointPendingForcedTrialIntent(CkptWriter writer, CortexPolicyPendingForcedTrialIntent intent, int policySchema)
    {
        writer.Bool(intent.HasSeed);
        if (!intent.HasSeed) return;
        writer.U64(intent.QuotaID); if (policySchema >= 11) writer.U8((byte)intent.SourceQuotaDecision); writer.U64(intent.ForcedDivergenceSeed);
        writer.U64(intent.SourceDecisionID); writer.I64(intent.SourceDecisionEventID); writer.I64(intent.SourceCorroborationEventID);
        writer.U64(intent.SourceOccurrenceDigest); writer.U64(intent.SourceCandidateFingerprint); if (policySchema >= 11) writer.U64(intent.SourceQuotaCandidateFingerprint);
        if (policySchema >= 9)
        {
            writer.U64(intent.SourceReadoutFingerprint); writer.U64(intent.SourceCandidateRevision.Value);
        }
        if (policySchema >= 10)
            WriteCanonicalState(writer, intent.SourceCanonicalState);
        writer.U64(intent.ReadoutFingerprint);
        writer.U64(intent.CandidateFingerprint); writer.U64(intent.CandidateRevision.Value);
        if (policySchema >= 10) writer.U64(intent.SuccessorOccurrenceDigest);
        WriteCanonicalState(writer, intent.CanonicalState);
        writer.Str(intent.ObligationID); writer.U8(intent.Arm); writer.U16(intent.FeatureID);
        writer.Str(intent.SourceRunID); writer.Str(intent.AuditOnlyDigest);
    }

    private static CortexPolicyPendingForcedTrialIntent ReadPolicyCheckpointPendingForcedTrialIntent(CkptReader reader, CortexPolicyID policy, int policySchema)
    {
        if (!reader.Bool()) return default;
        ulong quotaID = reader.U64(); CortexPolicyQuotaDecisions sourceQuotaDecision = policySchema >= 11 ? (CortexPolicyQuotaDecisions)reader.U8() : CortexPolicyQuotaDecisions.Denied; ulong seed = reader.U64(); ulong sourceDecisionID = reader.U64();
        long sourceDecisionEventID = reader.I64(); long sourceCorroborationEventID = reader.I64();
        ulong sourceOccurrenceDigest = reader.U64(); ulong sourceCandidateFingerprint = reader.U64(); ulong sourceQuotaCandidateFingerprint = policySchema >= 11 ? reader.U64() : 0;
        ulong sourceReadoutFingerprint = policySchema >= 9 ? reader.U64() : 0;
        GrammarRevisionID sourceCandidateRevision = policySchema >= 9 ? new(reader.U64()) : GrammarRevisionID.Zero;
        PolicyCanonicalStateID sourceCanonicalState = policySchema >= 10 ? ReadCanonicalState(reader) : default;
        ulong readoutFingerprint = reader.U64();
        ulong candidateFingerprint = reader.U64(); GrammarRevisionID candidateRevision = new(reader.U64());
        ulong successorOccurrenceDigest = policySchema >= 10 ? reader.U64() : 0;
        PolicyCanonicalStateID canonicalState = ReadCanonicalState(reader);
        string obligationID = reader.Str(); byte arm = reader.U8(); ushort featureID = reader.U16();
        if (policySchema < 9)
        {
            sourceReadoutFingerprint = readoutFingerprint;
            sourceCandidateRevision = candidateRevision;
        }
        if (sourceCanonicalState.Version == 0) sourceCanonicalState = canonicalState;
        if (successorOccurrenceDigest == 0) successorOccurrenceDigest = sourceOccurrenceDigest;
        return new(policy, quotaID, sourceQuotaDecision, seed, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
            sourceOccurrenceDigest, sourceCandidateFingerprint, sourceQuotaCandidateFingerprint, sourceReadoutFingerprint, sourceCandidateRevision,
            sourceCanonicalState, readoutFingerprint, candidateFingerprint, candidateRevision, successorOccurrenceDigest,
            canonicalState, obligationID, arm, featureID, reader.Str(), reader.Str());
    }

    private static void WritePolicyTrialExecutionHistory(CkptWriter writer, CortexPolicyID policy, in PolicyTrialExecutionHistory history, int actionCount, bool readoutIdentityPresent)
    {
        writer.Bool(history.IsPresent);
        if (!history.IsPresent) return;
        history.Validate(policy, actionCount);
        writer.U64(history.QuotaDecisionID.Value); writer.U8((byte)history.Cause); writer.U8((byte)history.Outcome);
        writer.I64(history.RequestCount); writer.I64(history.GuardAdmittedCount);
        writer.U64(history.LastRequestDecisionID.Value);
        if (history.LastRequestDecisionID.Value != 0)
        {
            writer.I32(history.LastRequestStep);
            CortexPolicyDecisionCheckpoint.Write(writer, new CortexPolicyDecision(history.LastRequestDecisionID, policy, history.LastRequestReadout), readoutIdentityPresent);
        }
        writer.U64(history.ExecutionDecisionID.Value); writer.I32(history.ExecutionStep); writer.U64(history.ExecutionReadoutFingerprint);
        CortexPolicyDecisionCheckpoint.Write(writer, new CortexPolicyDecision(history.ExecutionDecisionID, policy, history.ExecutionReadout), readoutIdentityPresent);
        WriteCanonicalState(writer, history.Scope.State);
        writer.U64(history.Scope.ReadoutFingerprint); writer.U64(history.Scope.CandidateFingerprint);
        writer.U64(history.Scope.OccurrenceDigest); writer.U64(history.Scope.Revision.Value);
    }

    private static PolicyTrialExecutionHistory ReadPolicyTrialExecutionHistory(CkptReader reader, CortexPolicyID policy, int actionCount, bool readoutIdentityPresent)
    {
        if (!reader.Bool()) return default;
        CortexPolicyQuotaDecisionID quotaID = new(reader.U64());
        CortexPolicySelectionCauses cause = (CortexPolicySelectionCauses)reader.U8();
        CortexPolicyTrialExecutionOutcomes outcome = (CortexPolicyTrialExecutionOutcomes)reader.U8();
        long requestCount = reader.I64(); long guardAdmittedCount = reader.I64();
        CortexPolicyDecisionID lastRequestID = new(reader.U64()); CortexPolicyDecisionReadout lastRequestReadout = default; int lastRequestStep = -1;
        if (lastRequestID.Value != 0)
        {
            lastRequestStep = reader.I32();
            CortexPolicyDecision lastRequest = CortexPolicyDecisionCheckpoint.Read(reader, policy, actionCount, readoutIdentityPresent);
            if (!lastRequest.DecisionID.Equals(lastRequestID) || lastRequestStep < 0)
                throw new InvalidDataException("policy trial execution history last-request identity disagrees with its decision");
            lastRequestReadout = lastRequest.Readout;
        }
        CortexPolicyDecisionID executionID = new(reader.U64()); int executionStep = reader.I32(); ulong executionFingerprint = reader.U64();
        CortexPolicyDecision execution = CortexPolicyDecisionCheckpoint.Read(reader, policy, actionCount, readoutIdentityPresent);
        if (!execution.DecisionID.Equals(executionID) || executionID.Value == 0 || executionStep < 0)
            throw new InvalidDataException("policy trial execution history execution identity disagrees with its decision");
        CortexPolicyDecisionReadout executionReadout = execution.Readout;
        PolicyCanonicalStateID scopeState = ReadCanonicalState(reader);
        PolicyVerifiedScopeEntry scope = new(scopeState, reader.U64(), reader.U64(), reader.U64(), new GrammarRevisionID(reader.U64()));
        PolicyTrialExecutionHistory history = new(quotaID, cause, outcome, requestCount, guardAdmittedCount,
            lastRequestID, lastRequestReadout, lastRequestStep, executionID, executionReadout, executionStep,
            executionFingerprint, scope);
        history.Validate(policy, actionCount);
        return history;
    }

    private void AppendPolicyDecision(in CortexPolicyDecision decision, ReadOnlySpan<MetricSample> features, int actionCount)
    {
        GrammarPolicyContextKey context = decision.ReadoutContext;
        AppendPolicyDecision(in decision, features, actionCount, ReadOnlySpan<MetricID>.Empty, in context);
    }

    private void AppendPolicyDecision(
        in CortexPolicyDecision decision,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        ReadOnlySpan<MetricID> excludedReadoutMetricIDs,
        in GrammarPolicyContextKey context,
        PolicyReadoutAttemptOutcomes readoutAttempt = PolicyReadoutAttemptOutcomes.None,
        CortexPolicyQuotaDecisionID? readoutQuotaDecisionID = null)
    {
        if (_runtimeTape is null || _runtimeJournal is null) return;
        // The grammar teacher is the launchpad/reflex action, never the selected grammar candidate. It lands before
        // the execution receipt so the next induction sees the policy context as training data while POLICY-DECISION
        // remains a separate audit of what actually ran.
        TapeEventID teacherEventID = excludedReadoutMetricIDs.IsEmpty
            ? TapePacketCreator.AppendPolicyExample(_runtimeTape, _runtimeJournal, Step, decision.Policy,
                decision.LaunchpadAction, features, actionCount)
            : TapePacketCreator.AppendPolicySemanticExample(_runtimeTape, _runtimeJournal, Step, decision.Policy,
                decision.LaunchpadAction, features, actionCount, excludedReadoutMetricIDs).GrammarEventID;
        AppendPolicyDecisionReceipt(in decision, features, actionCount, false, teacherEventID, in context, readoutAttempt, readoutQuotaDecisionID);
    }

    private void AppendPolicyDecision(
        in CortexPolicyDecision decision,
        in PolicyCanonicalStateID canonicalState,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        in GrammarPolicyContextKey context,
        PolicyReadoutAttemptOutcomes readoutAttempt = PolicyReadoutAttemptOutcomes.None,
        CortexPolicyQuotaDecisionID? readoutQuotaDecisionID = null)
    {
        if (_runtimeTape is null || _runtimeJournal is null) return;
        bool closeLoop = TryCreateLoopClosureTeacher(decision.Readout.GrammarRevision, out LoopClosureTeacherPacketProvenance teacher);
        TapeEventID teacherEventID = closeLoop
            ? TapePacketCreator.AppendPolicyCanonicalExample(_runtimeTape, _runtimeJournal, Step, decision.Policy,
                in canonicalState, decision.LaunchpadAction, features, actionCount, in teacher).GrammarEventID
            : TapePacketCreator.AppendPolicyCanonicalExample(_runtimeTape, _runtimeJournal, Step, decision.Policy,
                in canonicalState, decision.LaunchpadAction, features, actionCount).GrammarEventID;
        if (closeLoop) RegisterLoopClosureTeacher(in teacher, teacherEventID);
        AppendPolicyDecisionReceipt(in decision, features, actionCount, closeLoop, teacherEventID, in context, readoutAttempt, readoutQuotaDecisionID);
    }

    private void AppendPolicyDecisionReceipt(
        in CortexPolicyDecision decision,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        bool closeLoop,
        TapeEventID teacherEventID,
        in GrammarPolicyContextKey context,
        PolicyReadoutAttemptOutcomes readoutAttempt = PolicyReadoutAttemptOutcomes.None,
        CortexPolicyQuotaDecisionID? readoutQuotaDecisionID = null)
    {
        if (_runtimeTape is null || _runtimeJournal is null) return;
        TapeEventID eventID = TapePacketCreator.AppendPolicyDecision(_runtimeTape, _runtimeJournal, Step, in decision, features, actionCount, out byte[] packetBytes);
        if (decision.Policy.Equals(Homeostat.PolicyID))
        {
            _latestHomeostatDecisionEventDecisionID = decision.DecisionID;
            _latestHomeostatDecisionEventID = eventID;
            AppendOrganicComparison(in decision, eventID, packetBytes, features.Length, actionCount,
                readoutAttempt, readoutQuotaDecisionID);
        }
        if (_loopLineage is not null && closeLoop)
        {
            if (!TryCreateLoopClosureTeacher(decision.Readout.GrammarRevision, out LoopClosureTeacherPacketProvenance readoutTeacher)
                || !TryGetLoopClosureReadoutBinding(in readoutTeacher, out LoopLineageNode predecessor, out LoopLineageCausalID causalID))
                throw new InvalidDataException("registered learned readout has no exact folded-derivation predecessor");
            if (!_loopLineage.TryEmit(Step, LoopLineageNodeSpecies.LearnedReadout, eventID, decision.Readout.GrammarRevision,
                    [predecessor.NodeID], causalID))
                throw new InvalidDataException("registered learned-readout lineage emission did not close");
            LoopLineageEdgeReceipt readoutEdge = _loopLineage.Receipts[^1];
            BindLoopClosurePolicyRail(decision.Policy,
                decision.ReadoutIdentity,
                decision.Readout.GrammarRevision, causalID, readoutEdge.Node.NodeID);
        }
        if (closeLoop && decision.ReadoutContext.IsCanonical)
        {
            GrammarPolicyContextKey contextKey = decision.ReadoutContext;
            PolicyCanonicalStateID canonicalState = contextKey.CanonicalState;
            LoopClosureR4Provenance provenance;
            bool verified;
            if (TryGetPolicyBoundaryDomain(decision.Policy, out IPolicyBoundaryDomain domain))
            {
                verified = domain.TryVerifyR4(this, in decision, out TapeEventID verifiedEventID,
                    out provenance) && verifiedEventID == eventID;
            }
            else
            {
                verified = TryCreateLoopClosureR4(
                    decision.Policy,
                    in canonicalState,
                    in contextKey,
                    decision.Readout.GrammarRevision,
                    decision.Readout.ReadoutCandidateFingerprint,
                    decision.Readout.ReadoutCandidateOccurrenceDigest,
                    decision.DecisionID,
                    eventID,
                    out provenance);
            }
            if (verified)
                WriteLoopClosureR4($"readout-{eventID.Value}", in provenance);
        }
        if (_runtimeRun is null) return;
        // The row mirrors the packet the appender just encoded from this same decision; the
        // decision-readout occurrence-check independently re-resolves the tape bytes against this base64
        // at load, so no in-line Resolve+Decode round-trip is owed here.
        string row = Step.ToString(CultureInfo.InvariantCulture) + "\t" + eventID + "\t"
            + decision.DecisionID.Value.ToString(CultureInfo.InvariantCulture) + "\t" + decision.Policy.Value + "\t"
            + decision.Readout.LaunchpadAction.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Readout.RawCandidateAction.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Readout.SelectedCandidateAction.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Readout.ExecutedAction.ToString(CultureInfo.InvariantCulture) + "\t" + actionCount.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Readout.Authority + "\t"
            + decision.Readout.GrammarRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Readout.SelectionCause + "\t" + (decision.Readout.RollbackDrill ? "1" : "0") + "\t"
            + Convert.ToBase64String(packetBytes);
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyDecisionReceiptFile),
            PolicyDecisionReceiptHeader,
            row);
    }

    private void AppendOrganicComparison(
        in CortexPolicyDecision decision,
        TapeEventID sourceDecisionEventID,
        byte[] sourceDecisionPayload,
        int featureCount,
        int actionCount,
        PolicyReadoutAttemptOutcomes readoutAttempt,
        CortexPolicyQuotaDecisionID? readoutQuotaDecisionID)
    {
        if (_runtimeTape is null || _runtimeJournal is null
            || decision.SelectionCause is not (CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate)
            || decision.RollbackDrill || readoutAttempt is PolicyReadoutAttemptOutcomes.None
                or PolicyReadoutAttemptOutcomes.QuotaNoScan or PolicyReadoutAttemptOutcomes.SuppressedNoScan)
            return;
        bool candidate = decision.RawCandidateAction >= 0;
        OrganicComparisonOutcomeKinds? outcome = !candidate
            ? readoutAttempt switch
            {
                PolicyReadoutAttemptOutcomes.QuotaDenied => OrganicComparisonOutcomeKinds.ReadoutQuotaDenied,
                PolicyReadoutAttemptOutcomes.PaidScanNoMatch => OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch,
                _ => null,
            }
            : decision.RawCandidateAction == decision.LaunchpadAction
                ? OrganicComparisonOutcomeKinds.CandidateAgreement
                : OrganicComparisonOutcomeKinds.CandidateDivergence;
        if (outcome is not OrganicComparisonOutcomeKinds classifiedOutcome) return;
        CortexPolicyReadoutQuotaDecision quota = default;
        bool hasQuota = readoutQuotaDecisionID is { Value: > 0 }
            && TryReadPolicyReadoutQuota(readoutQuotaDecisionID.Value, out quota);
        if (readoutQuotaDecisionID is { Value: > 0 } && !hasQuota)
            throw new InvalidDataException("organic comparison readout quota identity has no authority row");
        if (readoutAttempt is PolicyReadoutAttemptOutcomes.QuotaDenied or PolicyReadoutAttemptOutcomes.PaidScanNoMatch
            && !hasQuota)
            throw new InvalidDataException("organic comparison readout outcome lacks its quota authority");
        CortexPolicyTrialCompletion completion = default;
        bool hasCompletion = classifiedOutcome == OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch
            && readoutQuotaDecisionID is { Value: > 0 }
            && TryReadPolicyReadoutCompletion(readoutQuotaDecisionID.Value, out completion);
        if (classifiedOutcome == OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch && !hasCompletion)
            throw new InvalidDataException("completed readout no-match lacks its completion authority");
        string quotaRowDigest = hasQuota ? ComputePolicyReadoutQuotaJournalRowSHA256(in quota) : "";
        string completionRowDigest = hasCompletion ? ComputePolicyReadoutCompletionJournalRowSHA256(in completion) : "";
        OrganicComparisonReceipt receipt = new(
            Step, decision.Policy, decision.DecisionID, sourceDecisionEventID,
            Convert.ToHexStringLower(SHA256.HashData(sourceDecisionPayload)),
            Journal.ComputePolicyDecisionJournalSHA256(Step, sourceDecisionEventID,
                "policy:" + decision.Policy.Value, in decision, actionCount, featureCount, sourceDecisionPayload.Length),
            decision.GrammarRevision, decision.ReadoutIdentity.Value,
            candidate ? decision.Readout.ReadoutCandidateFingerprint : 0,
            candidate ? decision.Readout.ReadoutCandidateOccurrenceDigest : 0,
            decision.LaunchpadAction, decision.RawCandidateAction, decision.SelectedCandidateAction,
            classifiedOutcome, readoutQuotaDecisionID,
            hasQuota ? quota.Decision : null,
            quotaRowDigest, completionRowDigest, "");
        receipt = receipt with { CanonicalReceiptSHA256 = OrganicComparisonReceipt.ComputeCanonicalReceiptSHA256(in receipt) };
        TapePacketCreator.AppendOrganicComparison(_runtimeTape, _runtimeJournal, Step, in receipt);
    }

    private void AppendPolicyOutcome(in CortexPolicyDecision decision, ReadOnlySpan<MetricSample> outcomes, bool invariantClean, long conservedCost)
    {
        Tape? tape = _runtimeTape;
        Journal? journal = _runtimeJournal;
        if (tape is null || journal is null) return;
        // Ordinary POLICY-OUTCOME is the source result and may precede a paid
        // boundary. It is intentionally not an compared loop-closure node;
        // CloseLoopClosureOutcome emits the post-paid typed terminal rail.
        _ = TapePacketCreator.AppendPolicyOutcome(tape, journal, Step, in decision, outcomes, invariantClean, conservedCost);
    }

    private void AppendPolicyTrialQuota(in CortexPolicyTrialQuotaDecision decision)
    {
        if (_runtimeRun is null) return;
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyTrialQuotaJournalFile),
            PolicyTrialQuotaJournalHeader,
            FormatPolicyTrialQuotaRow(in decision));
    }

    private void AppendPolicyTrialQuotaDurable(in CortexPolicyTrialQuotaDecision decision)
    {
        if (_runtimeRun is null) return;
        _policyJournalBuffer.AppendDurable(_runtimeRun.PathOf(PolicyTrialQuotaJournalFile),
            PolicyTrialQuotaJournalHeader,
            FormatPolicyTrialQuotaRow(in decision));
    }

    internal static string FormatPolicyTrialQuotaRow(in CortexPolicyTrialQuotaDecision decision)
        => decision.QuotaDecisionID + "\t"
            + decision.Policy.Value + "\t"
            + decision.CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + decision.QuotaStep.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.RequestedHorizonSteps.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.ArmCount.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.PlannedArmSteps.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.HeldArmSteps.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Decision + "\t"
            + decision.UsedSteps.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.RemainingQuota.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.CandidateState + "\t"
            + decision.DenialReason + "\t"
            + decision.CandidateOriginStep.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.CandidateCurrentStep.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.CandidateRequiredStep.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.CandidateRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t"
            + (decision.AllocationIdentity ?? "") + "\t" + (decision.AllocationDigest ?? "") + "\t"
            + decision.AllocationArmSteps.ToString(CultureInfo.InvariantCulture) + "\t"
            + (decision.SeedAuditOnlyDigest ?? "") + "\t"
            + decision.ReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture);

    private void AppendPolicyReadoutQuota(in CortexPolicyReadoutQuotaDecision decision)
    {
        if (_runtimeRun is null) return;
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyReadoutQuotaJournalFile),
            PolicyReadoutQuotaJournalHeader,
            FormatPolicyReadoutQuotaRow(in decision));
    }

    private void AppendPolicyReadoutAllocation(in CortexPolicyReadoutAllocation allocation)
    {
        if (_runtimeRun is null) return;
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyReadoutAllocationJournalFile),
            PolicyReadoutAllocationJournalHeader,
            FormatPolicyReadoutAllocationRow(in allocation));
    }

    internal static string FormatPolicyReadoutAllocationRow(in CortexPolicyReadoutAllocation allocation)
        => allocation.Sequence.ToString(CultureInfo.InvariantCulture) + "\t"
            + allocation.Step.ToString(CultureInfo.InvariantCulture) + "\t"
            + allocation.RosterDigest + "\t" + allocation.Policy.Value + "\t"
            + allocation.AvailableBefore.ToString(CultureInfo.InvariantCulture) + "\t"
            + allocation.AllocatedUnits.ToString(CultureInfo.InvariantCulture) + "\t"
            + allocation.ExpiredUnits.ToString(CultureInfo.InvariantCulture) + "\t"
            + allocation.AvailableAfter.ToString(CultureInfo.InvariantCulture);

    internal static string FormatPolicyReadoutQuotaRow(in CortexPolicyReadoutQuotaDecision decision)
        => decision.QuotaDecisionID + "\t"
            + decision.Policy.Value + "\t"
            + decision.CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + decision.GrammarRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.ContextDigest.ToString("X16", CultureInfo.InvariantCulture) + "\t"
            + decision.ContextBytes.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.DeliberationDepth.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.QuotaStep.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.PlannedUnits.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.HeldUnits.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.Decision + "\t"
            + decision.UsedUnits.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.RemainingQuota.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.AllocationSequence.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.RosterDigest + "\t"
            + decision.AvailableBefore.ToString(CultureInfo.InvariantCulture) + "\t"
            + decision.AvailableAfter.ToString(CultureInfo.InvariantCulture);

    private void AppendPolicyTrialCompletion(in CortexPolicyTrialCompletion completion)
    {
        if (_runtimeRun is null) return;
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyTrialCompletionJournalFile),
            PolicyTrialCompletionJournalHeader,
            FormatPolicyTrialCompletionRow(in completion));
    }

    internal void EnsurePolicyTrialCompletionDurable(in CortexPolicyTrialCompletion completion)
    {
        if (_runtimeRun is null) return;
        FlushPolicyJournalBuffer();
        string path = _runtimeRun.PathOf(PolicyTrialCompletionJournalFile);
        string expected = FormatPolicyTrialCompletionRow(in completion);
        int matchingRows = 0;
        if (File.Exists(path))
        {
            foreach (string row in File.ReadAllLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row)) continue;
                if (!row.StartsWith(completion.QuotaDecisionID + "\t", StringComparison.Ordinal)) continue;
                if (!string.Equals(row, expected, StringComparison.Ordinal))
                    throw new InvalidDataException($"policy trial completion {completion.QuotaDecisionID} conflicts with its durable completion");
                matchingRows++;
            }
        }
        if (matchingRows > 1)
            throw new InvalidDataException($"policy trial completion {completion.QuotaDecisionID} is duplicated in its durable journal");
        if (matchingRows == 0)
        {
            _policyJournalBuffer.Append(path, PolicyTrialCompletionJournalHeader, expected);
            FlushPolicyJournalBuffer();
        }
    }

    internal static string FormatPolicyTrialCompletionRow(in CortexPolicyTrialCompletion completion)
        => completion.QuotaDecisionID + "\t"
            + completion.ActualExecutedArmSteps.ToString(CultureInfo.InvariantCulture) + "\t"
            + completion.ReclaimedOrUnused.ToString(CultureInfo.InvariantCulture) + "\t"
            + (completion.EvaluatorWorkUnits?.ToString(CultureInfo.InvariantCulture) ?? "") + "\t"
            + completion.VerifierOutcome + "\t"
            + (completion.WallMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "");

    private void AppendPolicyReadoutCompletion(in CortexPolicyTrialCompletion completion)
    {
        if (_runtimeRun is null) return;
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyReadoutCompletionJournalFile),
            PolicyTrialCompletionJournalHeader,
            FormatPolicyTrialCompletionRow(in completion));
    }

    internal void RestorePolicyTrialQuota()
    {
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyTrialQuotaJournalFile);
        if (!File.Exists(path)) return;
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (!text.EndsWith('\n')) throw new InvalidDataException("policy trial quota journal has a partial terminal row");
        string[] lines = text.Split('\n');
        string header = lines[0].TrimStart('\uFEFF');
        int minimumColumns = header == PolicyTrialQuotaLegacy4Header ? 4 :
            header == PolicyTrialQuotaLegacyHeader ? 11 :
            header == PolicyTrialQuotaCurrentHeader ? 17 :
            header == PolicyTrialQuotaAllocationHeader ? 20 :
            header == PolicyTrialQuotaLegacyJournalHeader ? 21 :
            header == PolicyTrialQuotaJournalHeader ? 22 : -1;
        if (minimumColumns < 0) throw new InvalidDataException("policy trial quota journal header is not a typed schema");
        int priorColumns = minimumColumns;
        for (int i = 1; i < lines.Length - 1; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length is not (4 or 11 or 17 or 20 or 21 or 22)
                || minimumColumns == 4 && columns.Length != 4
                || minimumColumns != 4 && (columns.Length < minimumColumns || columns.Length < priorColumns))
                throw new InvalidDataException("policy trial quota row regresses its typed schema");
            priorColumns = columns.Length;
            // CORTEX4 rows are accepted as an explicit migration: the old row had no stable ID or arm shape.
            if (columns.Length == 4)
            {
                throw new InvalidDataException("legacy CORTEX4 policy quota row is inadmissible without split identities");
            }
            CortexPolicyQuotaDecisionID quotaID = new(ulong.Parse(columns[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            CortexPolicyTrialQuotaDecision decision = new(
                quotaID,
                new CortexPolicyID(columns[1]),
                ulong.Parse(columns[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(columns[3], CultureInfo.InvariantCulture),
                int.Parse(columns[4], CultureInfo.InvariantCulture),
                int.Parse(columns[5], CultureInfo.InvariantCulture),
                long.Parse(columns[6], CultureInfo.InvariantCulture),
                long.Parse(columns[7], CultureInfo.InvariantCulture),
                Enum.Parse<CortexPolicyQuotaDecisions>(columns[8]),
                long.Parse(columns[9], CultureInfo.InvariantCulture),
                long.Parse(columns[10], CultureInfo.InvariantCulture))
            {
                CandidateState = columns.Length is 17 or 20 or 21 or 22
                    ? Enum.Parse<CortexPolicyTrialCandidateStates>(columns[11])
                    : CortexPolicyTrialCandidateStates.Active,
                DenialReason = columns.Length is 17 or 20 or 21 or 22
                    ? Enum.Parse<CortexPolicyTrialDenialReasons>(columns[12])
                    : CortexPolicyTrialDenialReasons.None,
                CandidateOriginStep = columns.Length is 17 or 20 or 21 or 22 ? int.Parse(columns[13], CultureInfo.InvariantCulture) : int.Parse(columns[3], CultureInfo.InvariantCulture),
                CandidateCurrentStep = columns.Length is 17 or 20 or 21 or 22 ? int.Parse(columns[14], CultureInfo.InvariantCulture) : int.Parse(columns[3], CultureInfo.InvariantCulture),
                CandidateRequiredStep = columns.Length is 17 or 20 or 21 or 22 ? int.Parse(columns[15], CultureInfo.InvariantCulture) : -1,
                CandidateRevision = columns.Length is 17 or 20 or 21 or 22 ? new GrammarRevisionID(ulong.Parse(columns[16], CultureInfo.InvariantCulture)) : GrammarRevisionID.Zero,
                AllocationIdentity = columns.Length is 20 or 21 or 22 ? columns[17] : "",
                AllocationDigest = columns.Length is 20 or 21 or 22 ? columns[18] : "",
                AllocationArmSteps = columns.Length is 20 or 21 or 22 ? long.Parse(columns[19], CultureInfo.InvariantCulture) : 0,
                SeedAuditOnlyDigest = columns.Length is 21 or 22 ? columns[20] : "",
                ReadoutFingerprint = columns.Length == 22
                    ? ulong.Parse(columns[21], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : 0,
            };
            if (columns.Length == 22 && (decision.CandidateFingerprint == 0 || decision.ReadoutFingerprint == 0))
                throw new InvalidDataException("current policy trial quota row omits its split candidate/readout identity");
            if (columns.Length != 22 && decision.Decision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
                throw new InvalidDataException("legacy policy trial quota row is inadmissible without a full readout fingerprint");
            if (columns.Length is 21 or 22 && decision.SeedAuditOnlyDigest.Length != 0
                && !IsAuthenticatedAuditOnlyDigest(decision.SeedAuditOnlyDigest))
                throw new InvalidDataException("policy trial quota row carries malformed seed audit-only");
            if (columns.Length is 21 or 22 && decision.Policy.Equals(Homeostat.PolicyID)
                && decision.Decision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                && !IsAuthenticatedAuditOnlyDigest(decision.SeedAuditOnlyDigest))
                throw new InvalidDataException("Homeostat policy trial quota row omits seed audit-only");
            RestoreQuotaDecision(decision, decision.Decision == CortexPolicyQuotaDecisions.Paid ? decision.UsedSteps : 0);
        }
        RestorePolicyTrialCompletions();
    }

    private void RestoreQuotaDecision(in CortexPolicyTrialQuotaDecision decision, long committedSteps)
    {
        CortexPolicyTrialQuotaDecision durableDecision = decision;
        if (durableDecision.Decision == CortexPolicyQuotaDecisions.Paid
            && durableDecision.SeedAuditOnlyDigest.Length == 0
            && TryReadPolicyBoundarySeedAuditOnlyDigest(durableDecision.QuotaDecisionID, out string auditOnlyDigest))
            durableDecision = durableDecision with { SeedAuditOnlyDigest = auditOnlyDigest };
        CortexPolicyTrialQuotaDecision decisionToRestore = durableDecision;
        if (decisionToRestore.AllocationArmSteps > 0
            && decisionToRestore.AllocationDigest != CortexPolicyTrialAllocation.ComputeDigest(
                decisionToRestore.Policy, _config.Learning.Policies.TrialAllocation?.Authority ?? CortexPolicyAuthorities.Grammar,
                decisionToRestore.AllocationArmSteps, decisionToRestore.AllocationIdentity))
            throw new InvalidDataException($"policy quota decision {decisionToRestore.QuotaDecisionID} carries a forged allocation digest");
        bool present = false;
        CortexPolicyTrialQuotaDecision existing = default;
        for (int i = 0; i < _policyTrialQuotaDecisions.Count; i++)
            if (_policyTrialQuotaDecisions[i].QuotaDecisionID.Equals(decisionToRestore.QuotaDecisionID))
            {
                present = true;
                existing = _policyTrialQuotaDecisions[i];
                break;
            }
        // The TSV journal predates canonical source audit-only.  Its row is still
        // authoritative for the quota scalars; inherit the keyframe's typed
        // source scope instead of erasing it during journal restoration.
        if (present && !decisionToRestore.HasCanonicalState && existing.HasCanonicalState)
            decisionToRestore = decisionToRestore with { CanonicalState = existing.CanonicalState };
        if (present && !QuotaIdentityMatches(in existing, in decisionToRestore))
            throw new InvalidDataException($"policy quota decision {decisionToRestore.QuotaDecisionID} conflicts with its durable identity");
        if (decisionToRestore.Decision == CortexPolicyQuotaDecisions.Paid)
        {
            if (_policyTrialQuotaByID.TryGetValue(decisionToRestore.QuotaDecisionID, out CortexPolicyTrialQuotaDecision admitted))
            {
                if (admitted.SeedAuditOnlyDigest.Length == 0 && decisionToRestore.SeedAuditOnlyDigest.Length != 0)
                {
                    InvalidatePolicyTrialReconcileMemo();
                    _policyTrialQuotaByID[decisionToRestore.QuotaDecisionID] = decisionToRestore;
                    for (int i = 0; i < _policyTrialQuotaDecisions.Count; i++)
                        if (_policyTrialQuotaDecisions[i].QuotaDecisionID.Equals(decisionToRestore.QuotaDecisionID)
                            && _policyTrialQuotaDecisions[i].Decision == CortexPolicyQuotaDecisions.Paid)
                        {
                            _policyTrialQuotaDecisions[i] = decisionToRestore;
                            break;
                        }
                }
                return;
            }
            InvalidatePolicyTrialReconcileMemo();
            _policyTrialQuotaDecisions.Add(decisionToRestore);
            _policyTrialQuotaByID[decisionToRestore.QuotaDecisionID] = decisionToRestore;
            _policyTrialHeldSteps = checked(_policyTrialHeldSteps + decisionToRestore.HeldArmSteps);
            _policyTrialUsedSteps = checked(_policyTrialUsedSteps + committedSteps);
            _fundedPolicyTrials.Add(BuildQuotaKey(decisionToRestore.Policy, decisionToRestore.CandidateFingerprint, decisionToRestore.QuotaStep, decisionToRestore.PlannedArmSteps));
        }
        else if (decisionToRestore.Decision == CortexPolicyQuotaDecisions.Reused)
        {
            for (int i = 0; i < _policyTrialQuotaDecisions.Count; i++)
            {
                CortexPolicyTrialQuotaDecision prior = _policyTrialQuotaDecisions[i];
                if (prior.Decision == CortexPolicyQuotaDecisions.Reused
                    && QuotaIdentityMatches(in prior, in decisionToRestore)
                    && prior.UsedSteps == decisionToRestore.UsedSteps
                    && prior.RemainingQuota == decisionToRestore.RemainingQuota)
                    return;
            }
            InvalidatePolicyTrialReconcileMemo();
            _policyTrialQuotaDecisions.Add(decisionToRestore);
        }
        else if (!present)
        {
            InvalidatePolicyTrialReconcileMemo();
            _policyTrialQuotaDecisions.Add(decisionToRestore);
        }
    }

    private void RestorePolicyTrialCompletions()
    {
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyTrialCompletionJournalFile);
        if (!File.Exists(path)) return;
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (!text.EndsWith('\n')) text = text[..Math.Max(0, text.LastIndexOf('\n') + 1)];
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 6) throw new InvalidDataException("policy trial completion row has the wrong shape");
            CortexPolicyTrialCompletion completion = new(
                new CortexPolicyQuotaDecisionID(ulong.Parse(columns[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                long.Parse(columns[1], CultureInfo.InvariantCulture),
                long.Parse(columns[2], CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(columns[3]) ? null : long.Parse(columns[3], CultureInfo.InvariantCulture),
                Enum.Parse<CortexPolicyVerifierOutcomes>(columns[4]),
                string.IsNullOrEmpty(columns[5]) ? null : long.Parse(columns[5], CultureInfo.InvariantCulture));
            if (_policyTrialCompletionByID.TryGetValue(completion.QuotaDecisionID, out CortexPolicyTrialCompletion prior))
            {
                if (!CompletionMatches(in prior, in completion))
                    throw new InvalidDataException($"policy trial completion {completion.QuotaDecisionID} conflicts with its durable completion");
                continue;
            }
            if (!_policyTrialQuotaByID.TryGetValue(completion.QuotaDecisionID, out CortexPolicyTrialQuotaDecision quota))
                throw new InvalidDataException($"completion references unknown quota decision {completion.QuotaDecisionID}");
            if (completion.ReclaimedOrUnused != quota.PlannedArmSteps - completion.ActualExecutedArmSteps)
                throw new InvalidDataException($"completion reclaimed does not close quota decision {completion.QuotaDecisionID}");
            InvalidatePolicyTrialReconcileMemo();
            _policyTrialCompletions.Add(completion);
            _policyTrialCompletionByID.Add(completion.QuotaDecisionID, completion);
            _policyTrialHeldSteps = checked(_policyTrialHeldSteps - quota.HeldArmSteps);
            _policyTrialCompletedUsedSteps = checked(_policyTrialCompletedUsedSteps + completion.ActualExecutedArmSteps);
            _policyTrialUsedSteps = checked(_policyTrialCompletedUsedSteps + _policyTrialHeldSteps);
        }
    }

    internal void SavePolicyState(CkptWriter writer, int policySchema = 13)
        => SavePolicyState(writer, policySchema, captureLayout: false, out _);

    internal void SavePolicyState(
        CkptWriter writer,
        int policySchema,
        out CortexPolicyReadoutCheckpointLayout layout)
        => SavePolicyState(writer, policySchema, captureLayout: true, out layout);

    private void SavePolicyState(
        CkptWriter writer,
        int policySchema,
        bool captureLayout,
        out CortexPolicyReadoutCheckpointLayout layout)
    {
        layout = default;
        if (policySchema is not (5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13)) throw new InvalidDataException($"retired policy checkpoint schema {policySchema}");
        List<KeyValuePair<CortexPolicyID, PolicyState>> rows = new(_policies);
        rows.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        if (policySchema < 12)
        {
            KeyValuePair<CortexPolicyID, PolicyState> executionRow = rows.Find(static row =>
                row.Value.HistoricalTrialExecution.IsPresent || row.Value.ActiveTrialQuotaID.Value != 0);
            if (executionRow.Value is not null)
                throw new InvalidDataException($"policy checkpoint schema {policySchema} cannot represent paid policy trial execution identity for {executionRow.Key}");
        }
        FlushPolicyJournalBuffer();
        writer.U64(_nextPolicyDecisionID);
        writer.I64(_policyTrialUsedSteps);
        writer.I64(_policyTrialHeldSteps);
        writer.I64(_policyTrialCompletedUsedSteps);
        writer.I32(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            KeyValuePair<CortexPolicyID, PolicyState> row = rows[i];
            PolicyState state = row.Value;
            writer.Str(row.Key.Value);
            writer.I32(state.Schema.FeatureCount);
            writer.I32(state.Schema.ActionCount);
            writer.I32(state.Schema.OutcomeCount);
            writer.U8((byte)state.Mode);
            writer.U8((byte)state.Authority);
            writer.U64(state.ObservedInstallRevision.Value);
            writer.U64(state.ReadoutCandidateRevision.Value);
            writer.U64(state.ReadoutCandidateFingerprint);
            writer.U64(state.ReadoutCandidateSetDigest);
            writer.Bool(state.ReadoutCandidateState.Version != 0);
            if (state.ReadoutCandidateState.Version != 0)
            {
                writer.Str(state.ReadoutCandidateState.Policy.Value);
                writer.U8((byte)state.ReadoutCandidateState.Kind);
                writer.U16(state.ReadoutCandidateState.Version);
                writer.U64(state.ReadoutCandidateState.Value);
            }
            writer.U64(state.ReadoutCandidateOccurrenceDigest);
            writer.I32(state.ReadoutCandidateAction);
            writer.Bool(state.ReadoutCandidatePending);
            List<PolicyState.CanonicalCandidateEvidence> canonicalCandidates = new(state.CanonicalCandidates.Values);
            canonicalCandidates.Sort(static (left, right) => left.State.CompareTo(right.State));
            writer.I32(canonicalCandidates.Count);
            for (int c = 0; c < canonicalCandidates.Count; c++)
            {
                PolicyState.CanonicalCandidateEvidence candidate = canonicalCandidates[c];
                writer.Str(candidate.State.Policy.Value);
                writer.U8((byte)candidate.State.Kind);
                writer.U16(candidate.State.Version);
                writer.U64(candidate.State.Value);
                writer.I32(candidate.Action);
                writer.U64(candidate.CandidateFingerprint);
                writer.U64(candidate.OccurrenceDigest);
                writer.U64(candidate.Revision.Value);
                writer.U64(candidate.OriginRevision.Value);
                writer.I32(candidate.InstalledStep);
                writer.I32(candidate.Comparisons);
                writer.I32(candidate.Agreements);
                writer.I32(candidate.Misses);
            }
            if (policySchema >= 7)
            {
                List<PolicyVerifiedScopeEntry> scopes = new(state.VerifiedScopes.Values);
                scopes.Sort(static (left, right) => left.State.CompareTo(right.State));
                writer.I32(scopes.Count);
                for (int s = 0; s < scopes.Count; s++)
                {
                    PolicyVerifiedScopeEntry scope = scopes[s];
                    writer.Str(scope.State.Policy.Value); writer.U8((byte)scope.State.Kind);
                    writer.U16(scope.State.Version); writer.U64(scope.State.Value);
                    writer.U64(scope.ReadoutFingerprint); writer.U64(scope.CandidateFingerprint);
                    writer.U64(scope.OccurrenceDigest); writer.U64(scope.Revision.Value);
                }
            }
            writer.I32(state.ReadoutInstalledStep);
            writer.I32(state.ReadoutOracleComparisons);
            writer.I32(state.ReadoutOracleAgreements);
            state.ReadoutCache.Save(writer);
            writer.U64(state.Decisions);
            writer.U64(state.Outcomes);
            writer.U64(state.CensoredOutcomes);
            writer.U64(state.LaunchpadExecutions);
            writer.U64(state.GrammarExecutions);
            writer.U64(state.GrammarOutcomes);
            writer.U64(state.PaidGrammarOutcomes);
            writer.I32(state.ActionExecutions.Length);
            for (int a = 0; a < state.ActionExecutions.Length; a++) writer.U64(state.ActionExecutions[a]);
            writer.I64(state.ConservedCost);
            writer.U64(state.GrammarDivergentExecutions);
            writer.I32(state.LastGrammarLaunchpadAction);
            writer.I32(state.LastGrammarAction);
            writer.I32(state.LastGrammarFeatures.Length);
            for (int f = 0; f < state.LastGrammarFeatures.Length; f++) writer.F64(state.LastGrammarFeatures[f]);
            writer.I32(state.TrialGrammarExecutionsRemaining);
            writer.I32(state.TrialActionOffset);
            writer.Bool(state.TrialForcedDivergenceSeed.HasValue);
            if (state.TrialForcedDivergenceSeed is ulong forcedDivergenceSeed) writer.U64(forcedDivergenceSeed);
            writer.U64(state.TrialForcedDivergenceExecutions);
            if (policySchema >= 8) WritePolicyCheckpointPendingForcedTrialIntent(writer, state.PendingForcedTrialIntent, policySchema);
            writer.Bool(state.SuppressTrialPackets);
            writer.Bool(state.TrialFrozen);
            writer.U64(state.TrialAdaptationTransitions);
            writer.I32(state.PriorAction);
            writer.I32(state.LastAction);
            writer.I32(state.ActionReversals);
            writer.I32(state.ShadowComparisons);
            writer.I32(state.ShadowAgreements);
            writer.I32(state.EmulationMisses);
            writer.I32(state.Readmissions);
            writer.Bool(state.RollbackDrillPending);
            writer.Bool(state.RollbackDrillCompleted);
            writer.U64(state.AssayedFingerprint);
            writer.U64(state.VerifiedFingerprint);
            if (policySchema >= 7)
            {
                writer.U64(state.AssayedReadoutFingerprint);
                writer.U64(state.VerifiedReadoutFingerprint);
                writer.U64(state.VerifiedRevision.Value);
            }
            writer.I32(state.LastDecisionReadout.LaunchpadAction);
            writer.I32(state.LastDecisionReadout.RawCandidateAction);
            writer.I32(state.LastDecisionReadout.SelectedCandidateAction);
            writer.I32(state.LastDecisionReadout.ExecutedAction);
            writer.U8((byte)state.LastDecisionReadout.Authority);
            writer.U64(state.LastDecisionReadout.GrammarRevision.Value);
            writer.U8((byte)state.LastDecisionReadout.SelectionCause);
            writer.U64(state.LastDecisionReadout.ReadoutCandidateOccurrenceDigest);
            writer.U64(state.LastDecisionReadout.ReadoutCandidateFingerprint);
            if (policySchema >= 13) writer.U64(state.LastDecisionReadout.ReadoutFingerprint);
        }
        List<KeyValuePair<CortexPolicyID, PolicyState>> trialExecutionRows = rows.FindAll(static row =>
            row.Value.TrialExecutionCause != CortexPolicySelectionCauses.Launchpad
            || row.Value.TrialExecutionCorroboration.HasValue
            || row.Value.TrialRequestCount != 0
            || row.Value.HistoricalTrialExecution.IsPresent
            || row.Value.ActiveTrialQuotaID.Value != 0);
        if (trialExecutionRows.Count != 0)
        {
            writer.Section(PolicyTrialExecutionCheckpointTag);
            writer.U32(policySchema >= 12 ? 7u : 5u);
            writer.I32(trialExecutionRows.Count);
            for (int i = 0; i < trialExecutionRows.Count; i++)
            {
                KeyValuePair<CortexPolicyID, PolicyState> row = trialExecutionRows[i];
                writer.Str(row.Key.Value);
                if (policySchema >= 12) writer.U64(row.Value.ActiveTrialQuotaID.Value);
                writer.U8((byte)row.Value.TrialExecutionCause);
                writer.U8((byte)row.Value.TrialExecutionOutcome);
                writer.I64(row.Value.TrialRequestCount);
                writer.I64(row.Value.TrialGuardAdmittedCount);
                writer.U64(row.Value.TrialLastRequest?.DecisionID.Value ?? 0);
                if (row.Value.TrialLastRequest is CortexPolicyDecision lastRequest)
                {
                    writer.I32(row.Value.TrialLastRequestStep);
                    CortexPolicyDecisionCheckpoint.Write(writer, in lastRequest, policySchema >= 13);
                }
                writer.Bool(row.Value.TrialExecutionCorroboration.HasValue);
                if (row.Value.TrialExecutionCorroboration is CortexPolicyDecision corroboration)
                {
                    writer.I32(row.Value.TrialExecutionStep);
                    writer.U64(row.Value.TrialExecutionReadoutFingerprint);
                    CortexPolicyDecisionCheckpoint.Write(writer, in corroboration, policySchema >= 13);
                }
                if (policySchema >= 12)
                {
                    PolicyTrialExecutionHistory history = row.Value.HistoricalTrialExecution;
                    WritePolicyTrialExecutionHistory(writer, row.Key, in history, row.Value.Schema.ActionCount, policySchema >= 13);
                }
            }
        }
        if (policySchema >= 6)
        {
            writer.Section(PolicyQuotaAuditOnlyCheckpointTag);
            writer.U32(policySchema >= 10 ? 8u : policySchema >= 7 ? 7u : 6u);
        }
        writer.I32(_policyTrialQuotaDecisions.Count);
        for (int i = 0; i < _policyTrialQuotaDecisions.Count; i++)
        {
            CortexPolicyTrialQuotaDecision decision = _policyTrialQuotaDecisions[i];
            writer.U64(decision.QuotaDecisionID.Value);
            writer.Str(decision.Policy.Value);
            writer.U64(decision.CandidateFingerprint);
            if (policySchema >= 7) writer.U64(decision.ReadoutFingerprint);
            writer.I32(decision.QuotaStep);
            writer.I32(decision.RequestedHorizonSteps);
            writer.I32(decision.ArmCount);
            writer.I64(decision.PlannedArmSteps);
            writer.I64(decision.HeldArmSteps);
            writer.U8((byte)decision.Decision);
            writer.I64(decision.UsedSteps);
            writer.I64(decision.RemainingQuota);
            writer.U8((byte)decision.CandidateState);
            writer.U8((byte)decision.DenialReason);
            writer.I32(decision.CandidateOriginStep);
            writer.I32(decision.CandidateCurrentStep);
            writer.I32(decision.CandidateRequiredStep);
            writer.U64(decision.CandidateRevision.Value);
            writer.Str(decision.AllocationIdentity ?? "");
            writer.Str(decision.AllocationDigest ?? "");
            writer.I64(decision.AllocationArmSteps);
            if (policySchema >= 6) writer.Str(decision.SeedAuditOnlyDigest ?? "");
            if (policySchema >= 10)
            {
                writer.Bool(decision.HasCanonicalState);
                if (decision.HasCanonicalState) WriteCanonicalState(writer, decision.CanonicalState);
            }
        }
        writer.I32(_policyTrialCompletions.Count);
        for (int i = 0; i < _policyTrialCompletions.Count; i++)
        {
            CortexPolicyTrialCompletion completion = _policyTrialCompletions[i];
            writer.U64(completion.QuotaDecisionID.Value);
            writer.I64(completion.ActualExecutedArmSteps);
            writer.I64(completion.ReclaimedOrUnused);
            writer.Bool(completion.EvaluatorWorkUnits.HasValue);
            if (completion.EvaluatorWorkUnits is long evaluatorWorkUnits) writer.I64(evaluatorWorkUnits);
            writer.U8((byte)completion.VerifierOutcome);
            writer.Bool(completion.WallMilliseconds.HasValue);
            if (completion.WallMilliseconds is long wallMilliseconds) writer.I64(wallMilliseconds);
        }
        long usedUnitsOffset = captureLayout ? writer.Position : -1;
        writer.I64(_policyReadoutUsedUnits);
        writer.I64(_policyReadoutHeldUnits);
        writer.I64(_policyReadoutCompletedUsedUnits);
        SavePolicyReadoutAllocation(writer);
        long quotaCountOffset = captureLayout ? writer.Position : -1;
        writer.I32(_policyReadoutQuotaDecisions.Count);
        List<CortexPolicyReadoutQuotaCheckpointRow>? quotaRows = captureLayout
            ? new List<CortexPolicyReadoutQuotaCheckpointRow>(_policyReadoutQuotaDecisions.Count)
            : null;
        for (int i = 0; i < _policyReadoutQuotaDecisions.Count; i++)
        {
            CortexPolicyReadoutQuotaDecision decision = _policyReadoutQuotaDecisions[i];
            long rowOffset = captureLayout ? writer.Position : -1;
            WriteReadoutQuotaDecision(writer, in decision, out long decisionOffset);
            if (captureLayout)
                quotaRows!.Add(new(decision.QuotaDecisionID, rowOffset, writer.Position - rowOffset, decisionOffset));
        }
        long completionCountOffset = captureLayout ? writer.Position : -1;
        writer.I32(_policyReadoutCompletions.Count);
        List<CortexPolicyReadoutCompletionCheckpointRow>? completionRows = captureLayout
            ? new List<CortexPolicyReadoutCompletionCheckpointRow>(_policyReadoutCompletions.Count)
            : null;
        for (int i = 0; i < _policyReadoutCompletions.Count; i++)
        {
            CortexPolicyTrialCompletion completion = _policyReadoutCompletions[i];
            long rowOffset = captureLayout ? writer.Position : -1;
            WriteCompletion(writer, in completion);
            if (captureLayout)
                completionRows!.Add(new(completion.QuotaDecisionID, rowOffset, writer.Position - rowOffset));
        }
        SavePolicyBoundaryState(writer);
        if (captureLayout)
            layout = new(usedUnitsOffset, quotaCountOffset, completionCountOffset,
                quotaRows!.ToArray(), completionRows!.ToArray());
    }

    internal void LoadPolicyState(CkptReader reader, int policySchema = 13)
    {
        if (policySchema is not (5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13)) throw new InvalidDataException($"retired policy checkpoint schema {policySchema}");
        FlushPolicyJournalBuffer();
        _nextPolicyDecisionID = reader.U64();
        _policyTrialUsedSteps = reader.I64();
        _policyTrialHeldSteps = reader.I64();
        _policyTrialCompletedUsedSteps = reader.I64();
        int count = reader.I32();
        if (count < 0) throw new InvalidDataException("negative Cortex policy count");
        if (count != _policies.Count)
            throw new InvalidDataException($"checkpoint carries {count} policies but the mounted Cortex owns {_policies.Count}");
        HashSet<CortexPolicyID> restored = new();
        for (int i = 0; i < count; i++)
        {
            CortexPolicyID policy = new(reader.Str());
            if (!_policies.TryGetValue(policy, out PolicyState? state))
                throw new InvalidDataException($"checkpoint carries unknown policy '{policy}'");
            if (!restored.Add(policy)) throw new InvalidDataException($"duplicate restored policy '{policy}'");
            CortexPolicySchema schema = state.Schema;
            IPolicyBoundaryDomain? domain = _policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain registeredDomain)
                ? registeredDomain : null;
            bool requiresScope = RequiresCanonicalScope(domain);
            int featureCount = reader.I32();
            int actionCount = reader.I32();
            int outcomeCount = reader.I32();
            if (schema.FeatureCount != featureCount || schema.ActionCount != actionCount || schema.OutcomeCount != outcomeCount)
                throw new InvalidDataException($"restored policy '{policy}' schema does not match its runtime owner");
            CortexPolicyModes mode = (CortexPolicyModes)reader.U8();
            CortexPolicyAuthorities authority = (CortexPolicyAuthorities)reader.U8();
            if (!Enum.IsDefined(mode) || !Enum.IsDefined(authority))
                throw new InvalidDataException($"invalid restored authority for policy '{policy}'");
            if (authority > state.AuthorityCeiling)
                throw new InvalidDataException($"restored authority {authority} for policy '{policy}' exceeds configured ceiling {state.AuthorityCeiling}");
            state.Mode = mode;
            state.Authority = authority;
            state.ObservedInstallRevision = new global::Cogito.Grammar.GrammarRevisionID(reader.U64());
            state.ReadoutCandidateRevision = new global::Cogito.Grammar.GrammarRevisionID(reader.U64());
            state.ReadoutCandidateFingerprint = reader.U64();
            state.ReadoutCandidateSetDigest = reader.U64();
            if (reader.Bool())
            {
                CortexPolicyID candidateStatePolicy = new(reader.Str());
                PolicyCanonicalStateKinds candidateStateKind = (PolicyCanonicalStateKinds)reader.U8();
                ushort candidateStateVersion = reader.U16();
                ulong candidateStateValue = reader.U64();
                state.ReadoutCandidateState = new PolicyCanonicalStateID(candidateStatePolicy, candidateStateKind, candidateStateVersion, candidateStateValue);
            }
            else state.ReadoutCandidateState = default;
            state.ReadoutCandidateOccurrenceDigest = reader.U64();
            state.ReadoutCandidateAction = reader.I32();
            state.ReadoutCandidatePending = reader.Bool();
            int canonicalCandidateCount = reader.I32();
            if (canonicalCandidateCount is < 0 or > PolicyReadoutCache.MaxEntries)
                throw new InvalidDataException($"restored policy '{policy}' carries an invalid canonical candidate count");
            state.CanonicalCandidates.Clear();
            for (int c = 0; c < canonicalCandidateCount; c++)
            {
                PolicyCanonicalStateID candidateState = new(
                    new CortexPolicyID(reader.Str()),
                    (PolicyCanonicalStateKinds)reader.U8(),
                    reader.U16(),
                    reader.U64());
                if (!candidateState.Policy.Equals(policy)
                    || requiresScope && !ValidateCanonicalState(domain, policy, in candidateState)
                    || state.CanonicalCandidates.ContainsKey(candidateState))
                    throw new InvalidDataException($"restored policy '{policy}' carries an invalid canonical candidate state");
                int candidateAction = reader.I32();
                ulong candidateFingerprint = reader.U64();
                ulong occurrenceDigest = reader.U64();
                GrammarRevisionID candidateRevision = new(reader.U64());
                GrammarRevisionID candidateOriginRevision = new(reader.U64());
                PolicyState.CanonicalCandidateEvidence candidate = new(
                    candidateState, candidateAction, candidateFingerprint, occurrenceDigest,
                    candidateRevision, reader.I32())
                {
                    OriginRevision = candidateOriginRevision,
                    Comparisons = reader.I32(),
                    Agreements = reader.I32(),
                    Misses = reader.I32(),
                };
                if ((uint)candidateAction >= (uint)schema.ActionCount
                    || candidateFingerprint == 0 || occurrenceDigest == 0
                    || candidate.Comparisons < 0 || candidate.Agreements < 0 || candidate.Misses < 0
                    || candidate.Agreements > candidate.Comparisons || candidate.Misses > candidate.Comparisons)
                    throw new InvalidDataException($"restored policy '{policy}' carries invalid canonical candidate evidence");
                state.CanonicalCandidates.Add(candidateState, candidate);
            }
            state.VerifiedScopes.Clear();
            if (policySchema >= 7)
            {
                int scopeCount = reader.I32();
                if (scopeCount < 0 || scopeCount > canonicalCandidateCount)
                    throw new InvalidDataException($"restored policy '{policy}' carries an invalid verified scope count");
                for (int s = 0; s < scopeCount; s++)
                {
                    PolicyCanonicalStateID scopeState = new(new CortexPolicyID(reader.Str()),
                        (PolicyCanonicalStateKinds)reader.U8(), reader.U16(), reader.U64());
                    PolicyVerifiedScopeEntry scope = new(scopeState, reader.U64(), reader.U64(), reader.U64(), new GrammarRevisionID(reader.U64()));
                    PolicyCanonicalStateID restoredScopeState = scope.State;
                    if (!scope.IsValid || !ValidateCanonicalState(domain, policy, in restoredScopeState)
                        || !state.CanonicalCandidates.TryGetValue(scope.State, out PolicyState.CanonicalCandidateEvidence? candidate)
                        || candidate.CandidateFingerprint != scope.CandidateFingerprint
                        || candidate.OccurrenceDigest != scope.OccurrenceDigest
                        || candidate.Revision != scope.Revision
                        || !state.VerifiedScopes.TryAdd(scope.State, scope))
                        throw new InvalidDataException($"restored policy '{policy}' carries an invalid verified scope");
                }
            }
            state.ReadoutInstalledStep = reader.I32();
            state.ReadoutOracleComparisons = reader.I32();
            state.ReadoutOracleAgreements = reader.I32();
            state.ReadoutCache.Load(reader, schema.ActionCount);
            if (state.ReadoutCandidatePending && state.ReadoutCandidateRevision != GrammarRevisionID.Zero)
                throw new InvalidDataException($"restored policy '{policy}' marks a current readout pending while retaining an active validation revision");
            if (state.ObservedInstallRevision < state.ReadoutCache.Revision)
                throw new InvalidDataException($"restored policy '{policy}' observed install revision revision predates its readout cache");
            if (state.ReadoutCandidateRevision != GrammarRevisionID.Zero)
            {
                if (state.ReadoutCandidateState.Version != 0)
                {
                    PolicyCanonicalStateID restoredCandidateState = state.ReadoutCandidateState;
                    if (!restoredCandidateState.Policy.Equals(policy)
                        || requiresScope && !ValidateCanonicalState(domain, policy, in restoredCandidateState)
                        || state.ReadoutCandidateFingerprint == 0
                        || state.ReadoutCandidateSetDigest == 0
                        || state.ReadoutCandidateOccurrenceDigest == 0
                        || (uint)state.ReadoutCandidateAction >= (uint)schema.ActionCount)
                        throw new InvalidDataException($"restored policy '{policy}' carries an invalid semantic candidate identity");
                    bool hasCurrentCandidate = state.CanonicalCandidates.TryGetValue(
                        state.ReadoutCandidateState, out PolicyState.CanonicalCandidateEvidence? currentCandidate);
                    if (!hasCurrentCandidate
                        || currentCandidate!.Revision != state.ReadoutCandidateRevision
                        || currentCandidate.Action != state.ReadoutCandidateAction
                        || currentCandidate.CandidateFingerprint != state.ReadoutCandidateFingerprint
                        || currentCandidate.OccurrenceDigest != state.ReadoutCandidateOccurrenceDigest)
                        throw new InvalidDataException(
                            $"restored policy '{policy}' current canonical candidate disagrees with its evidence table " +
                            $"state={state.ReadoutCandidateState} revision={state.ReadoutCandidateRevision.Value} " +
                            $"candidate={state.ReadoutCandidateFingerprint:X16} occurrence={state.ReadoutCandidateOccurrenceDigest:X16} " +
                            $"action={state.ReadoutCandidateAction} evidence_count={state.CanonicalCandidates.Count} " +
                            $"evidence_present={(hasCurrentCandidate ? 1 : 0)} " +
                            $"evidence_revision={(hasCurrentCandidate ? currentCandidate!.Revision.Value : 0)} " +
                            $"evidence_candidate={(hasCurrentCandidate ? currentCandidate!.CandidateFingerprint : 0):X16} " +
                            $"evidence_occurrence={(hasCurrentCandidate ? currentCandidate!.OccurrenceDigest : 0):X16} " +
                            $"evidence_action={(hasCurrentCandidate ? currentCandidate!.Action : -1)}");
                    ulong restoredCandidateSetDigest = ComputeCanonicalProgramDigest(state, policy);
                    if (restoredCandidateSetDigest == 0 || restoredCandidateSetDigest != state.ReadoutCandidateSetDigest)
                        throw new InvalidDataException($"restored policy '{policy}' candidate set digest differs from its canonical cache");
                }
                else if (state.ReadoutCandidateRevision != state.ReadoutCache.Revision
                    || state.ReadoutCandidateFingerprint != GrammarPolicyReadout.ComputeFingerprint(state.ReadoutCandidateRevision, policy))
                    throw new InvalidDataException($"restored policy '{policy}' readout authority differs from its revision-stamped cache");
            }
            state.Decisions = reader.U64();
            state.Outcomes = reader.U64();
            state.CensoredOutcomes = reader.U64();
            state.LaunchpadExecutions = reader.U64();
            state.GrammarExecutions = reader.U64();
            state.GrammarOutcomes = reader.U64();
            state.PaidGrammarOutcomes = reader.U64();
            int actionExecutionCount = reader.I32();
            if (actionExecutionCount != state.ActionExecutions.Length)
                throw new InvalidDataException($"restored policy '{policy}' action execution count does not match its schema");
            for (int a = 0; a < actionExecutionCount; a++) state.ActionExecutions[a] = reader.U64();
            state.ConservedCost = reader.I64();
            state.GrammarDivergentExecutions = reader.U64();
            state.LastGrammarLaunchpadAction = reader.I32();
            state.LastGrammarAction = reader.I32();
            int lastGrammarFeatureCount = reader.I32();
            if (lastGrammarFeatureCount is not 0 && lastGrammarFeatureCount != schema.FeatureCount)
                throw new InvalidDataException($"restored policy '{policy}' last grammar feature count does not match its schema");
            state.LastGrammarFeatures = new double[lastGrammarFeatureCount];
            for (int f = 0; f < lastGrammarFeatureCount; f++) state.LastGrammarFeatures[f] = reader.F64();
            state.TrialGrammarExecutionsRemaining = reader.I32();
            state.TrialActionOffset = reader.I32();
            state.TrialForcedDivergenceSeed = reader.Bool() ? reader.U64() : null;
            state.TrialForcedDivergenceExecutions = reader.U64();
            state.ActiveTrialQuotaID = default;
            state.PendingForcedTrialIntent = policySchema >= 8 ? ReadPolicyCheckpointPendingForcedTrialIntent(reader, policy, policySchema) : default;
            state.SuppressTrialPackets = reader.Bool();
            state.TrialFrozen = reader.Bool();
            state.TrialAdaptationTransitions = reader.U64();
            state.PriorAction = reader.I32();
            state.LastAction = reader.I32();
            state.ActionReversals = reader.I32();
            state.ShadowComparisons = reader.I32();
            state.ShadowAgreements = reader.I32();
            state.EmulationMisses = reader.I32();
            state.Readmissions = reader.I32();
            state.RollbackDrillPending = reader.Bool();
            state.RollbackDrillCompleted = reader.Bool();
            state.AssayedFingerprint = reader.U64();
            state.VerifiedFingerprint = reader.U64();
            if (policySchema >= 7)
            {
                state.AssayedReadoutFingerprint = reader.U64();
                state.VerifiedReadoutFingerprint = reader.U64();
                state.VerifiedRevision = new GrammarRevisionID(reader.U64());
            }
            else
            {
                state.AssayedReadoutFingerprint = 0;
                state.VerifiedReadoutFingerprint = 0;
                state.VerifiedRevision = GrammarRevisionID.Zero;
            }
            state.LastDecisionReadout = new CortexPolicyDecisionReadout(
                reader.I32(), reader.I32(), reader.I32(), reader.I32(),
                (CortexPolicyAuthorities)reader.U8(),
                new global::Cogito.Grammar.GrammarRevisionID(reader.U64()),
                (CortexPolicySelectionCauses)reader.U8(),
                reader.U64(),
                reader.U64(),
                policySchema >= 13 ? reader.U64() : 0);
            if (state.LastDecisionReadout.Authority > state.AuthorityCeiling)
                throw new InvalidDataException($"restored decision authority for policy '{policy}' exceeds configured ceiling {state.AuthorityCeiling}");
            state.LastDecisionReadout.Validate(schema.ActionCount);
            state.TrialExecutionCause = CortexPolicySelectionCauses.Launchpad;
            state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
            state.TrialExecutionCorroboration = null;
            state.TrialExecutionReadoutFingerprint = 0;
            state.TrialExecutionStep = -1;
            state.TrialRequestCount = 0;
            state.TrialGuardAdmittedCount = 0;
            state.TrialLastRequest = null;
            state.TrialLastRequestStep = -1;
            state.HistoricalTrialExecution = default;
        }
        if (reader.RemainingBytes >= sizeof(uint) * 2 && reader.TryExpect(PolicyTrialExecutionCheckpointTag))
        {
            uint trialExecutionSchema = reader.U32();
            if (trialExecutionSchema is not (1 or 2 or 3 or 4 or 5 or 6 or 7)) throw new InvalidDataException("unsupported policy trial execution checkpoint schema");
            int trialExecutionCount = reader.I32();
            if (trialExecutionCount is < 0 || trialExecutionCount > _policies.Count)
                throw new InvalidDataException("policy trial execution checkpoint count is invalid");
            HashSet<CortexPolicyID> trialExecutionPolicies = new();
            for (int i = 0; i < trialExecutionCount; i++)
            {
                CortexPolicyID policy = new(reader.Str());
                if (!_policies.TryGetValue(policy, out PolicyState? state) || !trialExecutionPolicies.Add(policy))
                    throw new InvalidDataException("policy trial execution checkpoint addresses an invalid policy");
                if (trialExecutionSchema >= 7) state.ActiveTrialQuotaID = new CortexPolicyQuotaDecisionID(reader.U64());
                if (state.ActiveTrialQuotaID.Value != 0 && !state.SuppressTrialPackets)
                    throw new InvalidDataException("policy trial execution checkpoint carries active quota without a suppressed trial");
                CortexPolicySelectionCauses cause = (CortexPolicySelectionCauses)reader.U8();
                if (cause is not (CortexPolicySelectionCauses.Launchpad
                        or CortexPolicySelectionCauses.ShadowCandidate
                        or CortexPolicySelectionCauses.GrammarCandidate
                        or CortexPolicySelectionCauses.TrialOverride)
                    || (cause == CortexPolicySelectionCauses.Launchpad && trialExecutionSchema < 6))
                    throw new InvalidDataException("policy trial execution checkpoint carries an invalid cause");
                state.TrialExecutionCause = cause;
                if (trialExecutionSchema >= 3)
                {
                    byte encodedOutcome = reader.U8();
                    CortexPolicyTrialExecutionOutcomes outcome = trialExecutionSchema >= 4
                        ? (CortexPolicyTrialExecutionOutcomes)encodedOutcome
                        : encodedOutcome switch
                        {
                            0 => CortexPolicyTrialExecutionOutcomes.GuardDenied,
                            1 => CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted,
                            _ => throw new InvalidDataException("legacy policy trial execution checkpoint carries an unknown outcome"),
                        };
                    long requestCount = reader.I64();
                    long guardAdmittedCount = reader.I64();
                    CortexPolicyDecisionID lastRequestDecisionID = new(reader.U64());
                    if (!Enum.IsDefined(outcome) || requestCount < 0 || guardAdmittedCount < 0 || guardAdmittedCount > requestCount
                        || outcome == CortexPolicyTrialExecutionOutcomes.NotAttempted && requestCount != 0
                        || outcome == CortexPolicyTrialExecutionOutcomes.GuardDenied && (requestCount == 0 || guardAdmittedCount != 0))
                        throw new InvalidDataException("policy trial execution checkpoint carries invalid request allocation tracking");
                    CortexPolicyDecision? lastRequest = null;
                    int lastRequestStep = -1;
                    if (lastRequestDecisionID.Value != 0)
                    {
                        lastRequestStep = reader.I32();
                        lastRequest = CortexPolicyDecisionCheckpoint.Read(reader, policy, state.Schema.ActionCount, policySchema >= 13);
                        if (lastRequest.Value.DecisionID.Value != lastRequestDecisionID.Value || lastRequestStep < 0)
                            throw new InvalidDataException("policy trial execution checkpoint last request identity disagrees with its decision");
                    }
                    if (requestCount == 0 && lastRequest is not null || requestCount > 0 && lastRequest is null)
                        throw new InvalidDataException("policy trial execution checkpoint last request presence disagrees with its count");
                    state.TrialExecutionOutcome = outcome;
                    state.TrialRequestCount = requestCount;
                    state.TrialGuardAdmittedCount = guardAdmittedCount;
                    state.TrialLastRequest = lastRequest;
                    state.TrialLastRequestStep = lastRequestStep;
                }
                if (reader.Bool())
                {
                    int executionStep = trialExecutionSchema >= 2 ? reader.I32() : -1;
                    ulong executionReadoutFingerprint = trialExecutionSchema >= 5 ? reader.U64() : 0;
                    CortexPolicyDecision corroboration = CortexPolicyDecisionCheckpoint.Read(reader, policy, state.Schema.ActionCount, policySchema >= 13);
                    if (corroboration.DecisionID.Value == 0 || corroboration.SelectionCause != cause || executionStep < -1)
                        throw new InvalidDataException("policy trial execution checkpoint corroboration disagrees with its configured rail");
                    state.TrialExecutionCorroboration = corroboration;
                    state.TrialExecutionReadoutFingerprint = executionReadoutFingerprint;
                    state.TrialExecutionStep = executionStep;
                    if (trialExecutionSchema < 3)
                        state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
                }
                if (state.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                    && state.TrialExecutionCorroboration is null)
                    throw new InvalidDataException("policy trial execution checkpoint marks configured execution without a corroboration");
                if (state.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                    && trialExecutionSchema >= 5 && state.TrialExecutionReadoutFingerprint == 0)
                    throw new InvalidDataException("policy trial execution checkpoint omits its executed readout fingerprint");
                if (trialExecutionSchema >= 6)
                    state.HistoricalTrialExecution = ReadPolicyTrialExecutionHistory(reader, policy, state.Schema.ActionCount, policySchema >= 13);
                if (cause == CortexPolicySelectionCauses.Launchpad
                    && !state.HistoricalTrialExecution.IsPresent
                    && state.ActiveTrialQuotaID.Value == 0)
                    throw new InvalidDataException("policy trial execution checkpoint carries a launchpad row without historical execution");
            }
        }
        _policyTrialQuotaDecisions.Clear();
        _policyTrialQuotaByID.Clear();
        _policyTrialCompletions.Clear();
        _policyTrialCompletionByID.Clear();
        bool readSeedAuditOnlyDigest = reader.RemainingBytes >= sizeof(uint) * 2
            && reader.TryExpect(PolicyQuotaAuditOnlyCheckpointTag);
        uint quotaAuditOnlySchema = readSeedAuditOnlyDigest ? reader.U32() : 0;
        if (readSeedAuditOnlyDigest && quotaAuditOnlySchema is not (6 or 7 or 8))
            throw new InvalidDataException("unsupported policy quota audit-only checkpoint schema");
        int quotaCount = reader.I32();
        if (quotaCount < 0) throw new InvalidDataException("negative policy quota count");
        for (int i = 0; i < quotaCount; i++)
        {
            CortexPolicyQuotaDecisionID quotaID = new(reader.U64());
            CortexPolicyID quotaPolicy = new(reader.Str());
            ulong candidateFingerprint = reader.U64();
            ulong readoutFingerprint = quotaAuditOnlySchema >= 7 ? reader.U64() : 0;
            CortexPolicyTrialQuotaDecision decision = new(
                quotaID,
                quotaPolicy,
                candidateFingerprint, reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64(),
                (CortexPolicyQuotaDecisions)reader.U8(), reader.I64(), reader.I64())
            {
                ReadoutFingerprint = readoutFingerprint,
                CandidateState = (CortexPolicyTrialCandidateStates)reader.U8(),
                DenialReason = (CortexPolicyTrialDenialReasons)reader.U8(),
                CandidateOriginStep = reader.I32(),
                CandidateCurrentStep = reader.I32(),
                CandidateRequiredStep = reader.I32(),
                CandidateRevision = new GrammarRevisionID(reader.U64()),
                AllocationIdentity = reader.Str(),
                AllocationDigest = reader.Str(),
                AllocationArmSteps = reader.I64(),
                SeedAuditOnlyDigest = readSeedAuditOnlyDigest ? reader.Str() : "",
            };
            if (quotaAuditOnlySchema >= 8)
            {
                if (reader.Bool()) decision = decision with { CanonicalState = ReadCanonicalState(reader) };
            }
            if (quotaAuditOnlySchema >= 7 && (decision.CandidateFingerprint == 0 || decision.ReadoutFingerprint == 0))
                throw new InvalidDataException("policy checkpoint omits the split candidate/readout identity");
            if (!Enum.IsDefined(decision.Decision) || !Enum.IsDefined(decision.CandidateState) || !Enum.IsDefined(decision.DenialReason))
                throw new InvalidDataException("invalid policy quota decision");
            if (readSeedAuditOnlyDigest && decision.SeedAuditOnlyDigest.Length != 0
                && !IsAuthenticatedAuditOnlyDigest(decision.SeedAuditOnlyDigest))
                throw new InvalidDataException("policy checkpoint carries malformed seed audit-only");
            if (readSeedAuditOnlyDigest && decision.Policy.Equals(Homeostat.PolicyID)
                && decision.Decision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                && !IsAuthenticatedAuditOnlyDigest(decision.SeedAuditOnlyDigest))
                throw new InvalidDataException("Homeostat policy checkpoint omits its seed audit-only");
            if (readSeedAuditOnlyDigest && decision.SeedAuditOnlyDigest.Length != 0 && _runtimeRun is not null
                && (!TryReadPolicyBoundarySeedAuditOnlyDigest(decision.QuotaDecisionID, out string persistedAuditOnlyDigest)
                    || persistedAuditOnlyDigest != decision.SeedAuditOnlyDigest))
                throw new InvalidDataException("policy checkpoint seed audit-only disagrees with its durable authority");
            if (!readSeedAuditOnlyDigest && decision.Decision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                && decision.SeedAuditOnlyDigest.Length == 0
                && TryReadPolicyBoundarySeedAuditOnlyDigest(decision.QuotaDecisionID, out string auditOnlyDigest))
                decision = decision with { SeedAuditOnlyDigest = auditOnlyDigest };
            if (decision.AllocationArmSteps > 0
                && decision.AllocationDigest != CortexPolicyTrialAllocation.ComputeDigest(
                    decision.Policy, _config.Learning.Policies.TrialAllocation?.Authority ?? CortexPolicyAuthorities.Grammar,
                    decision.AllocationArmSteps, decision.AllocationIdentity))
                throw new InvalidDataException("policy checkpoint carries a forged trial allocation digest");
            _policyTrialQuotaDecisions.Add(decision);
            if (decision.Decision == CortexPolicyQuotaDecisions.Paid)
            {
                _policyTrialQuotaByID.Add(decision.QuotaDecisionID, decision);
                _fundedPolicyTrials.Add(BuildQuotaKey(decision.Policy, decision.CandidateFingerprint, decision.QuotaStep, decision.PlannedArmSteps));
            }
        }
        _policyTrialAuthorityValidationPending = _policies.Values.Any(static state =>
            state.ActiveTrialQuotaID.Value != 0 || state.PendingForcedTrialIntent.IsBound);
        ValidateDeferredPolicyTrialAuthority();
        int completionCount = reader.I32();
        if (completionCount < 0) throw new InvalidDataException("negative policy completion count");
        for (int i = 0; i < completionCount; i++)
        {
            CortexPolicyTrialCompletion completion = new(
                new CortexPolicyQuotaDecisionID(reader.U64()), reader.I64(), reader.I64(),
                reader.Bool() ? reader.I64() : null,
                (CortexPolicyVerifierOutcomes)reader.U8(),
                reader.Bool() ? reader.I64() : null);
            if (!Enum.IsDefined(completion.VerifierOutcome)) throw new InvalidDataException("invalid policy occurrence-check outcome");
            _policyTrialCompletions.Add(completion);
            _policyTrialCompletionByID.Add(completion.QuotaDecisionID, completion);
        }
        InvalidatePolicyTrialReconcileMemo();
        _policyReadoutUsedUnits = reader.I64();
        _policyReadoutHeldUnits = reader.I64();
        _policyReadoutCompletedUsedUnits = reader.I64();
        if (_policyReadoutUsedUnits < 0 || _policyReadoutHeldUnits < 0 || _policyReadoutCompletedUsedUnits < 0)
            throw new InvalidDataException("negative policy readout currency total");
        LoadPolicyReadoutAllocation(reader);
        int readoutQuotaCount = reader.I32();
        if (readoutQuotaCount < 0) throw new InvalidDataException("negative policy readout quota count");
        if ((long)readoutQuotaCount > reader.RemainingBytes / 8)
            throw new InvalidDataException("policy readout quota count exceeds checkpoint payload");
        List<CortexPolicyReadoutQuotaDecision> restoredReadoutQuota = new(readoutQuotaCount);
        Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> restoredReadoutQuotaByID = new(readoutQuotaCount);
        Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> restoredReadoutPaidByID = new(readoutQuotaCount);
        HashSet<CortexPolicyQuotaDecisionID> restoredReadoutQuotaIDs = new();
        for (int i = 0; i < readoutQuotaCount; i++)
        {
            CortexPolicyReadoutQuotaDecision decision = ReadReadoutQuotaDecision(reader);
            ValidateReadoutQuotaDecision(in decision);
            if (!restoredReadoutQuotaIDs.Add(decision.QuotaDecisionID))
                throw new InvalidDataException($"duplicate policy readout quota decision {decision.QuotaDecisionID}");
            restoredReadoutQuota.Add(decision);
            restoredReadoutQuotaByID.Add(decision.QuotaDecisionID, decision);
            if (decision.Decision == CortexPolicyQuotaDecisions.Paid)
                restoredReadoutPaidByID.Add(decision.QuotaDecisionID, decision);
        }
        ValidateReadoutQuotaAllocations(restoredReadoutQuota);
        int readoutCompletionCount = reader.I32();
        if (readoutCompletionCount < 0) throw new InvalidDataException("negative policy readout completion count");
        if ((long)readoutCompletionCount > reader.RemainingBytes / 8)
            throw new InvalidDataException("policy readout completion count exceeds checkpoint payload");
        List<CortexPolicyTrialCompletion> restoredReadoutCompletions = new(readoutCompletionCount);
        Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyTrialCompletion> restoredReadoutCompletionByID = new(readoutCompletionCount);
        for (int i = 0; i < readoutCompletionCount; i++)
        {
            CortexPolicyTrialCompletion completion = ReadCompletion(reader);
            if (!restoredReadoutCompletionByID.TryAdd(completion.QuotaDecisionID, completion))
                throw new InvalidDataException($"duplicate policy readout completion {completion.QuotaDecisionID}");
            if (!restoredReadoutPaidByID.TryGetValue(completion.QuotaDecisionID, out CortexPolicyReadoutQuotaDecision quota))
                throw new InvalidDataException($"policy readout completion references unknown quota decision {completion.QuotaDecisionID}");
            ValidateReadoutCompletion(in quota, in completion);
            restoredReadoutCompletions.Add(completion);
        }
        if (restoredReadoutPaidByID.Count != restoredReadoutCompletionByID.Count)
            throw new InvalidDataException("policy readout quota and completion journals are not one-to-one");
        foreach (CortexPolicyQuotaDecisionID quotaID in restoredReadoutPaidByID.Keys)
            if (!restoredReadoutCompletionByID.ContainsKey(quotaID))
                throw new InvalidDataException($"policy readout quota decision {quotaID} has no completion");
        long expectedReadoutUsed = 0;
        long expectedReadoutHeld = 0;
        for (int i = 0; i < restoredReadoutCompletions.Count; i++)
            expectedReadoutUsed = checked(expectedReadoutUsed + restoredReadoutCompletions[i].ActualExecutedArmSteps);
        for (int i = 0; i < restoredReadoutQuota.Count; i++)
        {
            CortexPolicyReadoutQuotaDecision decision = restoredReadoutQuota[i];
            if (decision.Decision == CortexPolicyQuotaDecisions.Paid
                && !restoredReadoutCompletionByID.ContainsKey(decision.QuotaDecisionID))
                expectedReadoutHeld = checked(expectedReadoutHeld + decision.HeldUnits);
        }
        Dictionary<CortexPolicyID, long> expectedAllocationStateHeld = new();
        Dictionary<CortexPolicyID, long> expectedAllocationStateActual = new();
        Dictionary<CortexPolicyID, long> expectedAllocationStateReclaimed = new();
        foreach (CortexPolicyID policy in _policyReadoutRoster)
        {
            expectedAllocationStateHeld[policy] = 0;
            expectedAllocationStateActual[policy] = 0;
            expectedAllocationStateReclaimed[policy] = 0;
        }
        for (int i = 0; i < restoredReadoutQuota.Count; i++)
        {
            CortexPolicyReadoutQuotaDecision quota = restoredReadoutQuota[i];
            if (quota.RosterDigest != _policyReadoutRosterDigest
                || quota.AvailableAfter != quota.AvailableBefore - quota.HeldUnits)
                throw new InvalidDataException("policy readout quota row is not bound to its allocation state allocation");
            if (quota.Decision == CortexPolicyQuotaDecisions.Paid
                && !restoredReadoutCompletionByID.ContainsKey(quota.QuotaDecisionID))
                expectedAllocationStateHeld[quota.Policy] = checked(expectedAllocationStateHeld[quota.Policy] + quota.HeldUnits);
        }
        for (int i = 0; i < restoredReadoutCompletions.Count; i++)
        {
            CortexPolicyTrialCompletion completion = restoredReadoutCompletions[i];
            CortexPolicyReadoutQuotaDecision quota = restoredReadoutPaidByID[completion.QuotaDecisionID];
            expectedAllocationStateActual[quota.Policy] = checked(expectedAllocationStateActual[quota.Policy] + completion.ActualExecutedArmSteps);
            expectedAllocationStateReclaimed[quota.Policy] = checked(expectedAllocationStateReclaimed[quota.Policy] + completion.ReclaimedOrUnused);
        }
        for (int i = 0; i < _policyReadoutRoster.Count; i++)
        {
            CortexPolicyID policy = _policyReadoutRoster[i];
            PolicyReadoutAllocationState allocationState = _policyReadoutAllocationStates[policy];
            // Allocation rows are issuance events, not a running allocation state available: a later
            // row may legitimately start at zero after an earlier issuance was used.
            // Close the allocation state against cumulative issuance, then subtract live/used use.
            long allocationAvailable = _policyReadoutAllocationStates[policy].AllocatedUnits;
            long expectedAvailable = checked(allocationAvailable - expectedAllocationStateActual[policy] - expectedAllocationStateHeld[policy]);
            if (allocationState.HeldUnits != expectedAllocationStateHeld[policy] || allocationState.UsedUnits != expectedAllocationStateActual[policy]
                || allocationState.ReclaimedUnits != expectedAllocationStateReclaimed[policy] || allocationState.AvailableUnits != expectedAvailable)
                throw new InvalidDataException($"policy readout allocation state available does not close allocations and completions for {policy}: available={allocationState.AvailableUnits}/{expectedAvailable} held={allocationState.HeldUnits}/{expectedAllocationStateHeld[policy]} actual={allocationState.UsedUnits}/{expectedAllocationStateActual[policy]} reclaimed={allocationState.ReclaimedUnits}/{expectedAllocationStateReclaimed[policy]} allocation={allocationAvailable}");
        }
        if (_policyReadoutCompletedUsedUnits != expectedReadoutUsed
            || _policyReadoutHeldUnits != expectedReadoutHeld
            || _policyReadoutUsedUnits != checked(expectedReadoutUsed + expectedReadoutHeld))
            throw new InvalidDataException("policy readout currency totals do not close the quota and completion journals");
        _policyReadoutQuotaDecisions.Clear();
        _policyReadoutQuotaDecisions.AddRange(restoredReadoutQuota);
        _policyReadoutQuotaByID.Clear();
        foreach (KeyValuePair<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> row in restoredReadoutQuotaByID)
            _policyReadoutQuotaByID.Add(row.Key, row.Value);
        _policyReadoutPaidByID.Clear();
        foreach (KeyValuePair<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> row in restoredReadoutPaidByID)
            _policyReadoutPaidByID.Add(row.Key, row.Value);
        _policyReadoutCompletions.Clear();
        _policyReadoutCompletions.AddRange(restoredReadoutCompletions);
        _policyReadoutCompletionByID.Clear();
        foreach (KeyValuePair<CortexPolicyQuotaDecisionID, CortexPolicyTrialCompletion> row in restoredReadoutCompletionByID)
            _policyReadoutCompletionByID.Add(row.Key, row.Value);
        LoadPolicyBoundaryState(reader);
        TruncatePolicyTrialJournalFiles();
        RewritePolicyReadoutJournalFiles();
        TruncatePolicyDecisionReceiptFile();
        TruncatePolicyOccurrenceCheckCoverageReceiptFile();
        TruncatePolicyBoundaryOpportunityCensus();
        TruncatePolicyBoundaryAdmissionCensus();
        TruncatePolicyBoundaryReceiptFile();
    }

    private void TruncatePolicyDecisionReceiptFile()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyDecisionReceiptFile);
        if (!File.Exists(path)) return;
        string text = File.ReadAllText(path, Encoding.UTF8);
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        StringBuilder kept = new(lines[0] + "\n");
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 14 || !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                throw new InvalidDataException("policy decision receipt row has the wrong shape");
            if (step <= Step) kept.Append(lines[i]).Append('\n');
        }
        File.WriteAllText(path, kept.ToString(), Encoding.UTF8);
    }

    private void TruncatePolicyTrialJournalFiles()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null) return;
        TruncatePolicyTrialJournalFile(
            _runtimeRun.PathOf(PolicyTrialQuotaJournalFile),
            PolicyTrialQuotaJournalHeader,
            _policyTrialQuotaDecisions.Count);
        TruncatePolicyTrialJournalFile(
            _runtimeRun.PathOf(PolicyTrialCompletionJournalFile),
            PolicyTrialCompletionJournalHeader,
            _policyTrialCompletions.Count);
    }

    private void TruncatePolicyOccurrenceCheckCoverageReceiptFile()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyOccurrenceCheckCoverageReceiptFile);
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0) return;
        StringBuilder kept = new(PolicyOccurrenceCheckCoverageReceiptHeader + "\n");
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 26 || !int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                throw new InvalidDataException("policy occurrence check coverage receipt row has the wrong shape");
            if (step <= Step) kept.Append(lines[i]).Append('\n');
        }
        File.WriteAllText(path, kept.ToString(), Encoding.UTF8);
    }

    private void EnsurePolicyJournalFiles()
    {
        if (_runtimeRun is null) return;
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyOccurrenceCheckReceiptFile),
            PolicyOccurrenceCheckReceiptHeader,
            0);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyOccurrenceCheckCoverageReceiptFile),
            PolicyOccurrenceCheckCoverageReceiptHeader,
            0);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyDecisionReceiptFile),
            PolicyDecisionReceiptHeader,
            checked((int)(_nextPolicyDecisionID - 1)));
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyTrialQuotaJournalFile),
            PolicyTrialQuotaJournalHeader,
            _policyTrialQuotaDecisions.Count);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyTrialCompletionJournalFile),
            PolicyTrialCompletionJournalHeader,
            _policyTrialCompletions.Count);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyReadoutQuotaJournalFile),
            PolicyReadoutQuotaJournalHeader,
            _policyReadoutQuotaDecisions.Count);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyReadoutCompletionJournalFile),
            PolicyTrialCompletionJournalHeader,
            _policyReadoutCompletions.Count);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyReadoutAllocationJournalFile),
            PolicyReadoutAllocationJournalHeader,
            checked((int)AbsolutePolicyReadoutAllocationCount));
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyBoundaryOpportunityCensusFile),
            PolicyBoundaryOpportunityCensusHeader,
            0);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyBoundaryAdmissionCensusFile),
            PolicyBoundaryAdmissionCensusHeader,
            0);
        EnsurePolicyJournalFile(
            _runtimeRun.PathOf(PolicyBoundaryReceiptFile),
            PolicyBoundaryReceiptHeader,
            0);
    }

    private static void EnsurePolicyJournalFile(string path, string header, int rowCount)
    {
        if (File.Exists(path)) return;
        if (rowCount != 0)
            throw new InvalidDataException($"policy journal {Path.GetFileName(path)} is missing {rowCount} checkpointed row(s)");
        File.WriteAllText(path, header + "\n", Encoding.UTF8);
    }

    private void RewritePolicyReadoutJournalFiles()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null)
        {
            _policyReadoutJournalRewritePending = true;
            return;
        }
        StringBuilder quota = new(PolicyReadoutQuotaJournalHeader + "\n");
        for (int i = 0; i < _policyReadoutQuotaDecisions.Count; i++)
        {
            CortexPolicyReadoutQuotaDecision decision = _policyReadoutQuotaDecisions[i];
            quota.Append(FormatPolicyReadoutQuotaRow(in decision)).Append('\n');
        }
        File.WriteAllText(_runtimeRun.PathOf(PolicyReadoutQuotaJournalFile), quota.ToString(), Encoding.UTF8);
        StringBuilder completions = new(PolicyTrialCompletionJournalHeader + "\n");
        for (int i = 0; i < _policyReadoutCompletions.Count; i++)
        {
            CortexPolicyTrialCompletion completion = _policyReadoutCompletions[i];
            completions.Append(FormatPolicyTrialCompletionRow(in completion)).Append('\n');
        }
        File.WriteAllText(_runtimeRun.PathOf(PolicyReadoutCompletionJournalFile), completions.ToString(), Encoding.UTF8);
        RewritePolicyReadoutAllocationJournalFile();
        _policyReadoutJournalRewritePending = false;
    }

    /// Reset the allocation TSV to the checkpoint's exact row horizon. An unshed allocation rewrites the file
    /// whole from RAM; a shed allocation keeps the file's first shed rows (their only home — verified against
    /// the checkpoint's audit-only digest) and re-lands the resident tail, dropping kill-orphan rows past it.
    private void RewritePolicyReadoutAllocationJournalFile()
    {
        string path = _runtimeRun!.PathOf(PolicyReadoutAllocationJournalFile);
        if (_policyReadoutAllocationShedCount == 0)
        {
            StringBuilder allocations = new(PolicyReadoutAllocationJournalHeader + "\n");
            for (int i = 0; i < _policyReadoutAllocations.Count; i++)
            {
                CortexPolicyReadoutAllocation allocation = _policyReadoutAllocations[i];
                allocations.Append(FormatPolicyReadoutAllocationRow(in allocation)).Append('\n');
            }
            File.WriteAllText(path, allocations.ToString(), Encoding.UTF8);
            return;
        }
        if (!File.Exists(path))
            throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} is missing but the checkpoint shed {_policyReadoutAllocationShedCount} rows to it");
        ulong digest = AllocationDigestSeed;
        _runtimeRun.WriteAtomic(PolicyReadoutAllocationJournalFile, stream =>
        {
            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16_384, leaveOpen: true);
            writer.Write(PolicyReadoutAllocationJournalHeader); writer.Write('\n');
            long kept = 0;
            foreach (string row in File.ReadLines(path))
            {
                if (kept == 0 && row == PolicyReadoutAllocationJournalHeader) continue;
                digest = FoldAllocationDigest(digest, row);
                writer.Write(row); writer.Write('\n');
                if (++kept == _policyReadoutAllocationShedCount) break;
            }
            if (kept != _policyReadoutAllocationShedCount)
                throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} holds {kept} of the {_policyReadoutAllocationShedCount} shed allocation rows");
            if (digest != _policyReadoutAllocationShedDigest)
                throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} shed-prefix digest {digest:X16} disagrees with the checkpoint's audit-only digest {_policyReadoutAllocationShedDigest:X16}");
            for (int i = 0; i < _policyReadoutAllocations.Count; i++)
            {
                CortexPolicyReadoutAllocation allocation = _policyReadoutAllocations[i];
                writer.Write(FormatPolicyReadoutAllocationRow(in allocation)); writer.Write('\n');
            }
        });
    }

    private static void TruncatePolicyTrialJournalFile(string path, string header, int rowCount)
    {
        if (!File.Exists(path)) return;
        string[] rows = File.ReadAllLines(path);
        int count = Math.Min(rows.Length - 1, rowCount);
        List<string> kept = new(count + 1) { header };
        for (int i = 0; i < count; i++) kept.Add(rows[i + 1]);
        File.WriteAllLines(path, kept, Encoding.UTF8);
    }

    private static void WriteQuotaDecision(CkptWriter writer, in CortexPolicyTrialQuotaDecision decision)
    {
        writer.U64(decision.QuotaDecisionID.Value); writer.Str(decision.Policy.Value); writer.U64(decision.CandidateFingerprint);
        writer.I32(decision.QuotaStep); writer.I32(decision.RequestedHorizonSteps); writer.I32(decision.ArmCount);
        writer.I64(decision.PlannedArmSteps); writer.I64(decision.HeldArmSteps); writer.U8((byte)decision.Decision);
        writer.I64(decision.UsedSteps); writer.I64(decision.RemainingQuota);
    }

    private static CortexPolicyTrialQuotaDecision ReadQuotaDecision(CkptReader reader)
        => new(new CortexPolicyQuotaDecisionID(reader.U64()), new CortexPolicyID(reader.Str()), reader.U64(), reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64(), (CortexPolicyQuotaDecisions)reader.U8(), reader.I64(), reader.I64());

    private static void WriteReadoutQuotaDecision(CkptWriter writer, in CortexPolicyReadoutQuotaDecision decision)
    {
        WriteReadoutQuotaDecision(writer, in decision, out _);
    }

    private static void WriteReadoutQuotaDecision(
        CkptWriter writer,
        in CortexPolicyReadoutQuotaDecision decision,
        out long decisionOffset)
    {
        writer.U64(decision.QuotaDecisionID.Value); writer.Str(decision.Policy.Value); writer.U64(decision.CandidateFingerprint);
        writer.U64(decision.GrammarRevision.Value); writer.U64(decision.ContextDigest); writer.I32(decision.ContextBytes);
        writer.I32(decision.DeliberationDepth); writer.I32(decision.QuotaStep); writer.I64(decision.PlannedUnits);
        writer.I64(decision.HeldUnits);
        decisionOffset = writer.Position;
        writer.U8((byte)decision.Decision); writer.I64(decision.UsedUnits); writer.I64(decision.RemainingQuota);
        writer.I64(decision.AllocationSequence); writer.Str(decision.RosterDigest); writer.I64(decision.AvailableBefore); writer.I64(decision.AvailableAfter);
    }

    private static CortexPolicyReadoutQuotaDecision ReadReadoutQuotaDecision(CkptReader reader)
        => new(new CortexPolicyQuotaDecisionID(reader.U64()), new CortexPolicyID(reader.Str()), reader.U64(),
            new GrammarRevisionID(reader.U64()), reader.U64(), reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64(),
            (CortexPolicyQuotaDecisions)reader.U8(), reader.I64(), reader.I64(), reader.I64(), reader.Str(), reader.I64(), reader.I64());

    private static void WriteCompletion(CkptWriter writer, in CortexPolicyTrialCompletion completion)
    {
        writer.U64(completion.QuotaDecisionID.Value); writer.I64(completion.ActualExecutedArmSteps); writer.I64(completion.ReclaimedOrUnused);
        writer.Bool(completion.EvaluatorWorkUnits.HasValue); if (completion.EvaluatorWorkUnits is long evaluator) writer.I64(evaluator);
        writer.U8((byte)completion.VerifierOutcome); writer.Bool(completion.WallMilliseconds.HasValue); if (completion.WallMilliseconds is long wall) writer.I64(wall);
    }

    private static CortexPolicyTrialCompletion ReadCompletion(CkptReader reader)
        => new(new CortexPolicyQuotaDecisionID(reader.U64()), reader.I64(), reader.I64(), reader.Bool() ? reader.I64() : null, (CortexPolicyVerifierOutcomes)reader.U8(), reader.Bool() ? reader.I64() : null);

    private void ValidateReadoutQuotaDecision(in CortexPolicyReadoutQuotaDecision decision)
    {
        if (decision.QuotaDecisionID.Value == 0)
            throw new InvalidDataException("policy readout quota decision has no stable ID");
        if (!_policies.ContainsKey(decision.Policy))
            throw new InvalidDataException($"policy readout quota decision references unknown policy '{decision.Policy}'");
        if (decision.CandidateFingerprint == 0)
            throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} has no candidate fingerprint");
        if (decision.GrammarRevision == GrammarRevisionID.Zero || decision.ContextDigest == 0 || decision.ContextBytes <= 0
            || decision.DeliberationDepth < 0 || decision.QuotaStep < 0 || decision.PlannedUnits <= 0
            || decision.PlannedUnits != decision.DeliberationDepth + 1 || decision.HeldUnits < 0
            || decision.UsedUnits < 0 || decision.RemainingQuota < 0
            || string.IsNullOrWhiteSpace(decision.RosterDigest) || decision.AvailableBefore < 0 || decision.AvailableAfter < 0
            || !decision.QuotaDecisionID.Equals(CreatePolicyReadoutQuotaDecisionID(
                decision.Policy, decision.CandidateFingerprint, decision.GrammarRevision, decision.QuotaStep,
                decision.ContextDigest, decision.ContextBytes, decision.DeliberationDepth, decision.PlannedUnits)))
            throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} has invalid bounds");
        switch (decision.Decision)
        {
            case CortexPolicyQuotaDecisions.Paid:
                if (decision.HeldUnits != decision.PlannedUnits || decision.UsedUnits != decision.PlannedUnits)
            throw new InvalidDataException($"paid readout decision {decision.QuotaDecisionID} does not hold and use its plan");
                break;
            case CortexPolicyQuotaDecisions.Reused:
                if (decision.HeldUnits != decision.PlannedUnits || decision.UsedUnits != 0)
                    throw new InvalidDataException($"reused readout decision {decision.QuotaDecisionID} does not preserve its stable reservation");
                break;
            case CortexPolicyQuotaDecisions.Denied:
                if (decision.HeldUnits != 0 || decision.UsedUnits != 0)
                    throw new InvalidDataException($"denied readout decision {decision.QuotaDecisionID} carries currency");
                break;
            default:
                throw new InvalidDataException($"invalid policy readout quota decision {decision.Decision}");
        }
    }

    private void ValidateReadoutQuotaAllocations(IReadOnlyList<CortexPolicyReadoutQuotaDecision> decisions)
    {
        Dictionary<long, CortexPolicyReadoutAllocation> shedAllocations = new();
        if (_policyReadoutAllocationShedCount > 0)
        {
            Run? authorityRun = _runtimeRun ?? _checkpointAuthorityRun;
            if (authorityRun is null)
                throw new InvalidDataException("policy readout allocation shed prefix has no durable journal authority");
            string path = authorityRun.PathOf(PolicyReadoutAllocationJournalFile);
            if (!File.Exists(path))
                throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} is missing but the checkpoint shed {_policyReadoutAllocationShedCount} allocation rows to it");

            HashSet<long> needed = new();
            for (int i = 0; i < decisions.Count; i++)
            {
                long sequence = decisions[i].AllocationSequence;
                if (sequence > 0 && sequence <= _policyReadoutAllocationShedCount)
                    needed.Add(sequence);
            }

            ulong digest = AllocationDigestSeed;
            long seen = 0;
            foreach (string rendered in File.ReadLines(path))
            {
                if (seen == 0 && rendered == PolicyReadoutAllocationJournalHeader) continue;
                if (seen == _policyReadoutAllocationShedCount) break;
                digest = FoldAllocationDigest(digest, rendered);
                if (!TryParsePolicyReadoutAllocationRow(rendered, out CortexPolicyReadoutAllocation allocation)
                    || allocation.Sequence != seen + 1
                    || allocation.Step != seen + 1
                    || allocation.RosterDigest != _policyReadoutRosterDigest
                    || !_policyReadoutAllocationStates.ContainsKey(allocation.Policy)
                    || allocation.AvailableBefore < 0 || allocation.AvailableAfter < 0
                    || allocation.AllocatedUnits < 0 || allocation.ExpiredUnits < 0
                    || allocation.AllocatedUnits + allocation.ExpiredUnits != 1
                    || allocation.AvailableAfter != allocation.AvailableBefore + allocation.AllocatedUnits)
                    throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} shed prefix row {seen + 1} is malformed");
                if (needed.Contains(allocation.Sequence))
                    shedAllocations.Add(allocation.Sequence, allocation);
                seen++;
            }
            if (seen != _policyReadoutAllocationShedCount)
                throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} holds {seen} of the {_policyReadoutAllocationShedCount} shed allocation rows");
            if (digest != _policyReadoutAllocationShedDigest)
                throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} shed-prefix digest {digest:X16} disagrees with the checkpoint's audit-only digest {_policyReadoutAllocationShedDigest:X16}");
            if (shedAllocations.Count != needed.Count)
                throw new InvalidDataException($"{PolicyReadoutAllocationJournalFile} shed prefix is missing an allocation authority referenced by quota");
        }

        for (int i = 0; i < decisions.Count; i++)
        {
            CortexPolicyReadoutQuotaDecision decision = decisions[i];
            ValidateReadoutQuotaAllocation(in decision, shedAllocations);
        }
    }

    private void ValidateReadoutQuotaAllocation(
        in CortexPolicyReadoutQuotaDecision decision,
        IReadOnlyDictionary<long, CortexPolicyReadoutAllocation> shedAllocations)
    {
        if (decision.RosterDigest != _policyReadoutRosterDigest)
            throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} carries a stale roster authority");
        if (decision.AllocationSequence < 0)
            throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} has a negative allocation sequence");
        if (decision.AllocationSequence == 0)
        {
            if (decision.AvailableBefore != 0 || decision.AvailableAfter != 0)
                throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} has an unbound initial allocation state available");
            return;
        }
        if (decision.AllocationSequence > AbsolutePolicyReadoutAllocationCount)
            throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} points past the allocation journal");
        CortexPolicyReadoutAllocation allocation;
        if (decision.AllocationSequence <= _policyReadoutAllocationShedCount)
        {
            if (!shedAllocations.TryGetValue(decision.AllocationSequence, out allocation))
                throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} has no durable shed allocation authority");
        }
        else
        {
            int residentIndex = checked((int)(decision.AllocationSequence - _policyReadoutAllocationShedCount - 1));
            allocation = _policyReadoutAllocations[residentIndex];
        }
        if (allocation.Sequence != decision.AllocationSequence
            || !allocation.Policy.Equals(decision.Policy)
            || allocation.RosterDigest != decision.RosterDigest)
            throw new InvalidDataException($"policy readout quota decision {decision.QuotaDecisionID} is not bound to its allocation authority");
    }

    private static void ValidateReadoutCompletion(
        in CortexPolicyReadoutQuotaDecision quota,
        in CortexPolicyTrialCompletion completion)
    {
        if (completion.QuotaDecisionID.Value == 0
            || completion.ActualExecutedArmSteps < 0
            || completion.ActualExecutedArmSteps > quota.PlannedUnits
            || completion.ReclaimedOrUnused < 0
            || completion.ReclaimedOrUnused != quota.PlannedUnits - completion.ActualExecutedArmSteps
            || completion.EvaluatorWorkUnits is < 0
            || completion.WallMilliseconds is < 0
            || completion.VerifierOutcome != CortexPolicyVerifierOutcomes.ReadoutCompleted)
            throw new InvalidDataException($"policy readout completion {completion.QuotaDecisionID} does not close its quota decision");
    }

}
