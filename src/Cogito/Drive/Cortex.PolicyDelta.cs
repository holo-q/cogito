namespace Cogito;

using Cogito.Grammar;

/// The readout-owned mutable policy surface between keyframes.  Journal rows are
/// appended by identity; the replacement is the bounded working cache and its
/// policy cursor state.  It is intentionally not a second Cortex image.
internal readonly partial record struct CortexPolicyReadoutStateReplacement(
    CortexPolicyID Policy,
    CortexPolicyModes Mode,
    CortexPolicyAuthorities Authority,
    GrammarRevisionID ObservedInstallRevision,
    GrammarRevisionID ReadoutCandidateRevision,
    ulong ReadoutCandidateFingerprint,
    ulong ReadoutCandidateSetDigest,
    PolicyCanonicalStateID ReadoutCandidateState,
    ulong ReadoutCandidateOccurrenceDigest,
    int ReadoutCandidateAction,
    bool ReadoutCandidatePending,
    int ReadoutInstalledStep,
    int ReadoutOracleComparisons,
    int ReadoutOracleAgreements,
    int ShadowComparisons,
    int ShadowAgreements,
    int EmulationMisses,
    bool LearnerEvidenceTrusted,
    CortexPolicySelectionCauses TrialExecutionCause,
    CortexPolicyTrialExecutionOutcomes TrialExecutionOutcome,
    bool SuppressTrialPackets,
    CortexPolicyPendingForcedTrialIntent PendingForcedTrialIntent,
    CortexPolicyQuotaDecisionID ActiveTrialQuotaID,
    CortexPolicyDecisionID TrialExecutionDecisionID,
    CortexPolicyDecisionReadout TrialExecutionReadout,
    int TrialExecutionStep,
    ulong TrialExecutionReadoutFingerprint,
    long TrialRequestCount,
    long TrialGuardAdmittedCount,
    CortexPolicyDecisionID TrialLastRequestDecisionID,
    CortexPolicyDecisionReadout TrialLastRequestReadout,
    int TrialLastRequestStep,
    PolicyTrialExecutionHistory HistoricalTrialExecution,
    CortexPolicyCanonicalCandidateReplacement[] CanonicalCandidates,
    PolicyVerifiedScopeEntry[] VerifiedScopes,
    PolicyReadoutCacheReplacement Cache);

// Verified succession is checkpoint authority, not a transient cache hint.
// Keep both identity domains so a resumed child cannot promote on a candidate
// fingerprint that belongs to a different install revision revision.
partial record struct CortexPolicyReadoutStateReplacement
{
    public int ActionCount { get; init; }
    public ulong AssayedReadoutFingerprint { get; init; }
    public ulong AssayedFingerprint { get; init; }
    public ulong VerifiedReadoutFingerprint { get; init; }
    public ulong VerifiedFingerprint { get; init; }
    public GrammarRevisionID VerifiedRevision { get; init; }
}

internal readonly record struct CortexPolicyCanonicalCandidateReplacement(
    PolicyCanonicalStateID State,
    int Action,
    ulong CandidateFingerprint,
    ulong OccurrenceDigest,
    GrammarRevisionID Revision,
    GrammarRevisionID OriginRevision,
    int InstalledStep,
    int Comparisons,
    int Agreements,
    int Misses);

internal readonly record struct CortexPolicyReadoutAllocationReplacement(
    bool RosterSealed,
    string RosterDigest,
    long LastQuotaStep,
    int AllocationCursor,
    long AllocationSequence,
    CortexPolicyReadoutAllocationReceipt[] AllocationStates,
    long UsedUnits,
    long HeldUnits,
    long CompletedUsedUnits);

internal readonly record struct CortexPolicyCheckpointDelta(
    int TrialQuotaCursor,
    CortexPolicyTrialQuotaDecision[] TrialQuota,
    int TrialCompletionCursor,
    CortexPolicyTrialCompletion[] TrialCompletions,
    int ReadoutQuotaCursor,
    CortexPolicyReadoutQuotaDecision[] ReadoutQuota,
    int ReadoutCompletionCursor,
    CortexPolicyTrialCompletion[] ReadoutCompletions,
    int AllocationCursor,
    CortexPolicyReadoutAllocation[] Allocations,
    CortexPolicyReadoutStateReplacement[] States,
    CortexPolicyReadoutAllocationReplacement Allocation,
    ulong NextDecisionID,
    long TrialUsedSteps,
    long TrialHeldSteps,
    long TrialCompletedUsedSteps)
{
    internal bool IsEmpty => (TrialQuota?.Length ?? 0) == 0 && (TrialCompletions?.Length ?? 0) == 0
        && (ReadoutQuota?.Length ?? 0) == 0 && (ReadoutCompletions?.Length ?? 0) == 0
        && (Allocations?.Length ?? 0) == 0;
}

public sealed partial class Cortex
{
    private const int MaxPolicyDeltaRows = 1_000_000;
    private const byte HistoricalPolicyDeltaVersion = 1;
    // Version 7 is retained as the historical verified-identity shape; scope
    // rows are appended only by version 8 so old deltas remain byte-readable.
    private const byte CurrentPolicyDeltaVersion = 17;
    private int _policyTrialQuotaCheckpointCursor;
    private int _policyTrialCompletionCheckpointCursor;
    private int _policyReadoutQuotaCheckpointCursor;
    private int _policyReadoutCompletionCheckpointCursor;
    private int _policyReadoutAllocationCheckpointCursor;

    internal CortexPolicyCheckpointDelta CapturePolicyCheckpointDelta()
    {
        ValidatePolicyCursor(_policyTrialQuotaCheckpointCursor, _policyTrialQuotaDecisions.Count, "trial quota");
        ValidatePolicyCursor(_policyTrialCompletionCheckpointCursor, _policyTrialCompletions.Count, "trial completions");
        ValidatePolicyCursor(_policyReadoutQuotaCheckpointCursor, _policyReadoutQuotaDecisions.Count, "readout quota");
        ValidatePolicyCursor(_policyReadoutCompletionCheckpointCursor, _policyReadoutCompletions.Count, "readout completions");
        ValidatePolicyCursor(_policyReadoutAllocationCheckpointCursor, _policyReadoutAllocations.Count, "readout allocations");
        List<CortexPolicyReadoutStateReplacement> states = new(_policies.Count);
        foreach (KeyValuePair<CortexPolicyID, PolicyState> pair in _policies.OrderBy(static row => row.Key))
        {
            PolicyState state = pair.Value;
            states.Add(new(pair.Key, state.Mode, state.Authority, state.ObservedInstallRevision,
                state.ReadoutCandidateRevision, state.ReadoutCandidateFingerprint, state.ReadoutCandidateSetDigest,
                state.ReadoutCandidateState, state.ReadoutCandidateOccurrenceDigest, state.ReadoutCandidateAction,
                state.ReadoutCandidatePending, state.ReadoutInstalledStep, state.ReadoutOracleComparisons,
                state.ReadoutOracleAgreements, state.ShadowComparisons, state.ShadowAgreements,
                state.EmulationMisses, true, state.TrialExecutionCause, state.TrialExecutionOutcome,
                state.SuppressTrialPackets,
                state.PendingForcedTrialIntent,
                state.ActiveTrialQuotaID,
                state.TrialExecutionCorroboration?.DecisionID ?? default,
                state.TrialExecutionCorroboration?.Readout ?? default,
                state.TrialExecutionStep,
                state.TrialExecutionReadoutFingerprint,
                state.TrialRequestCount, state.TrialGuardAdmittedCount,
                state.TrialLastRequest?.DecisionID ?? default,
                state.TrialLastRequest?.Readout ?? default,
                state.TrialLastRequestStep,
                state.HistoricalTrialExecution,
                state.CanonicalCandidates.Values.Select(static candidate => new CortexPolicyCanonicalCandidateReplacement(
                    candidate.State, candidate.Action, candidate.CandidateFingerprint, candidate.OccurrenceDigest,
                    candidate.Revision, candidate.OriginRevision, candidate.InstalledStep, candidate.Comparisons,
                    candidate.Agreements, candidate.Misses)).ToArray(),
                state.VerifiedScopes.Values.ToArray(),
                state.ReadoutCache.CaptureReplacement())
            {
                ActionCount = state.Schema.ActionCount,
                AssayedReadoutFingerprint = state.AssayedReadoutFingerprint,
                AssayedFingerprint = state.AssayedFingerprint,
                VerifiedReadoutFingerprint = state.VerifiedReadoutFingerprint,
                VerifiedFingerprint = state.VerifiedFingerprint,
                VerifiedRevision = state.VerifiedRevision,
            });
        }
        return new(
            _policyTrialQuotaCheckpointCursor,
            _policyTrialQuotaDecisions.Skip(_policyTrialQuotaCheckpointCursor).ToArray(),
            _policyTrialCompletionCheckpointCursor,
            _policyTrialCompletions.Skip(_policyTrialCompletionCheckpointCursor).ToArray(),
            _policyReadoutQuotaCheckpointCursor,
            _policyReadoutQuotaDecisions.Skip(_policyReadoutQuotaCheckpointCursor).ToArray(),
            _policyReadoutCompletionCheckpointCursor,
            _policyReadoutCompletions.Skip(_policyReadoutCompletionCheckpointCursor).ToArray(),
            _policyReadoutAllocationCheckpointCursor,
            _policyReadoutAllocations.Skip(_policyReadoutAllocationCheckpointCursor).ToArray(),
            states.ToArray(),
            CapturePolicyReadoutAllocationReplacement(),
            _nextPolicyDecisionID, _policyTrialUsedSteps, _policyTrialHeldSteps, _policyTrialCompletedUsedSteps);
    }

    internal void CommitPolicyCheckpointDelta()
    {
        _policyTrialQuotaCheckpointCursor = _policyTrialQuotaDecisions.Count;
        _policyTrialCompletionCheckpointCursor = _policyTrialCompletions.Count;
        _policyReadoutQuotaCheckpointCursor = _policyReadoutQuotaDecisions.Count;
        _policyReadoutCompletionCheckpointCursor = _policyReadoutCompletions.Count;
        _policyReadoutAllocationCheckpointCursor = _policyReadoutAllocations.Count;
    }

    internal void ApplyPolicyCheckpointDelta(in CortexPolicyCheckpointDelta delta)
    {
        ValidatePolicyCursor(delta.TrialQuotaCursor, _policyTrialQuotaDecisions.Count, "trial quota");
        ValidatePolicyCursor(delta.TrialCompletionCursor, _policyTrialCompletions.Count, "trial completions");
        ValidatePolicyCursor(delta.ReadoutQuotaCursor, _policyReadoutQuotaDecisions.Count, "readout quota");
        ValidatePolicyCursor(delta.ReadoutCompletionCursor, _policyReadoutCompletions.Count, "readout completions");
        ValidatePolicyCursor(delta.AllocationCursor, _policyReadoutAllocations.Count, "readout allocations");
        ValidatePolicyRows(delta);
        AppendTrialQuota(delta.TrialQuota);
        AppendTrialCompletions(delta.TrialCompletions);
        AppendReadoutQuota(delta.ReadoutQuota);
        AppendReadoutCompletions(delta.ReadoutCompletions);
        AppendAllocations(delta.Allocations);
        ApplyPolicyStates(delta.States);
        ApplyPolicyReadoutAllocation(delta.Allocation);
        _nextPolicyDecisionID = delta.NextDecisionID;
        _policyTrialUsedSteps = delta.TrialUsedSteps;
        _policyTrialHeldSteps = delta.TrialHeldSteps;
        _policyTrialCompletedUsedSteps = delta.TrialCompletedUsedSteps;
        _policyTrialAuthorityValidationPending = _policies.Values.Any(static state =>
            state.ActiveTrialQuotaID.Value != 0 || state.PendingForcedTrialIntent.IsBound);
        ValidateDeferredPolicyTrialAuthority();
        CommitPolicyCheckpointDelta();
    }

    private void AppendTrialQuota(CortexPolicyTrialQuotaDecision[] rows)
    {
        if (rows.Length > 0) InvalidatePolicyTrialReconcileMemo();
        foreach (CortexPolicyTrialQuotaDecision row in rows)
        {
            CortexPolicyTrialQuotaDecision? existing = null;
            for (int index = 0; index < _policyTrialQuotaDecisions.Count; index++)
                if (_policyTrialQuotaDecisions[index].QuotaDecisionID.Equals(row.QuotaDecisionID))
                {
                    existing = _policyTrialQuotaDecisions[index];
                    break;
                }
            if (existing is CortexPolicyTrialQuotaDecision prior)
            {
                if (!QuotaIdentityMatches(in prior, in row)
                    || row.Decision != CortexPolicyQuotaDecisions.Reused
                    || prior.Decision == CortexPolicyQuotaDecisions.Denied)
                    throw new InvalidDataException($"conflicting trial quota decision {row.QuotaDecisionID}");
                ValidateTrialQuotaReuse(in row);
                _policyTrialQuotaDecisions.Add(row);
                continue;
            }
            _policyTrialQuotaDecisions.Add(row);
            if (row.Decision == CortexPolicyQuotaDecisions.Paid && !_policyTrialQuotaByID.TryAdd(row.QuotaDecisionID, row))
                throw new InvalidDataException($"duplicate paid trial decision {row.QuotaDecisionID}");
        }
    }

    private void AppendTrialCompletions(CortexPolicyTrialCompletion[] rows)
    {
        if (rows.Length > 0) InvalidatePolicyTrialReconcileMemo();
        foreach (CortexPolicyTrialCompletion row in rows)
        {
            if (!_policyTrialCompletionByID.TryAdd(row.QuotaDecisionID, row))
                throw new InvalidDataException($"duplicate trial completion {row.QuotaDecisionID}");
            _policyTrialCompletions.Add(row);
        }
    }

    private void AppendReadoutQuota(CortexPolicyReadoutQuotaDecision[] rows)
    {
        foreach (CortexPolicyReadoutQuotaDecision row in rows)
        {
            if (!_policyReadoutQuotaByID.TryAdd(row.QuotaDecisionID, row))
                throw new InvalidDataException($"duplicate readout quota decision {row.QuotaDecisionID}");
            if (row.Decision == CortexPolicyQuotaDecisions.Paid && !_policyReadoutPaidByID.TryAdd(row.QuotaDecisionID, row))
                throw new InvalidDataException($"duplicate paid readout decision {row.QuotaDecisionID}");
            _policyReadoutQuotaDecisions.Add(row);
        }
    }

    private void AppendReadoutCompletions(CortexPolicyTrialCompletion[] rows)
    {
        foreach (CortexPolicyTrialCompletion row in rows)
        {
            if (!_policyReadoutCompletionByID.TryAdd(row.QuotaDecisionID, row))
                throw new InvalidDataException($"duplicate readout completion {row.QuotaDecisionID}");
            _policyReadoutCompletions.Add(row);
        }
    }

    private void AppendAllocations(CortexPolicyReadoutAllocation[] rows)
    {
        foreach (CortexPolicyReadoutAllocation row in rows)
        {
            // Sequence is ABSOLUTE — contiguity is judged past the shed horizon, not the resident count.
            if (row.Sequence != AbsolutePolicyReadoutAllocationCount + 1)
                throw new InvalidDataException("policy readout allocation sequence is not contiguous");
            _policyReadoutAllocations.Add(row);
        }
    }

    private void ApplyPolicyStates(CortexPolicyReadoutStateReplacement[] states)
    {
        if (states is null || states.Length > _policies.Count) throw new InvalidDataException("policy state replacement count is invalid");
        foreach (CortexPolicyReadoutStateReplacement replacement in states)
        {
            if (replacement.LearnerEvidenceTrusted
                ? replacement.ShadowComparisons < 0 || replacement.ShadowAgreements < 0 || replacement.EmulationMisses < 0
                    || replacement.ShadowAgreements > replacement.ShadowComparisons
                    || replacement.EmulationMisses > replacement.ShadowComparisons
                : replacement.ShadowComparisons != 0 || replacement.ShadowAgreements != 0 || replacement.EmulationMisses != 0)
                throw new InvalidDataException("policy readout learner evidence replacement is invalid");
        }
        foreach (CortexPolicyReadoutStateReplacement replacement in states)
        {
            if (!_policies.TryGetValue(replacement.Policy, out PolicyState? state))
                throw new InvalidDataException($"policy state replacement addresses unknown policy '{replacement.Policy}'");
            state.Mode = replacement.Mode; state.Authority = replacement.Authority;
            state.ObservedInstallRevision = replacement.ObservedInstallRevision;
            state.ReadoutCandidateRevision = replacement.ReadoutCandidateRevision;
            state.ReadoutCandidateFingerprint = replacement.ReadoutCandidateFingerprint;
            state.ReadoutCandidateSetDigest = replacement.ReadoutCandidateSetDigest;
            state.ReadoutCandidateState = replacement.ReadoutCandidateState;
            state.ReadoutCandidateOccurrenceDigest = replacement.ReadoutCandidateOccurrenceDigest;
            state.ReadoutCandidateAction = replacement.ReadoutCandidateAction;
            state.ReadoutCandidatePending = replacement.ReadoutCandidatePending;
            state.ReadoutInstalledStep = replacement.ReadoutInstalledStep;
            state.ReadoutOracleComparisons = replacement.ReadoutOracleComparisons;
            state.ReadoutOracleAgreements = replacement.ReadoutOracleAgreements;
            state.AssayedReadoutFingerprint = replacement.AssayedReadoutFingerprint;
            state.AssayedFingerprint = replacement.AssayedFingerprint;
            state.VerifiedReadoutFingerprint = replacement.VerifiedReadoutFingerprint;
            state.VerifiedFingerprint = replacement.VerifiedFingerprint;
            state.VerifiedRevision = replacement.VerifiedRevision;
            if (replacement.LearnerEvidenceTrusted)
            {
                state.ShadowComparisons = replacement.ShadowComparisons;
                state.ShadowAgreements = replacement.ShadowAgreements;
                state.EmulationMisses = replacement.EmulationMisses;
                state.ReadoutLearnerEvidenceTrusted = true;
            }
            else
            {
                // Historical deltas never carried these counters. Keep the keyframe
                // values for deterministic replay, but make readiness fail closed.
                state.ReadoutLearnerEvidenceTrusted = false;
            }
            if (!Enum.IsDefined(replacement.TrialExecutionCause))
                throw new InvalidDataException("policy trial execution replacement cause is invalid");
            if (!Enum.IsDefined(replacement.TrialExecutionOutcome)
                || replacement.TrialRequestCount < 0
                || replacement.TrialGuardAdmittedCount < 0
                || replacement.TrialGuardAdmittedCount > replacement.TrialRequestCount
                || replacement.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted && replacement.TrialRequestCount != 0
                || replacement.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.GuardDenied && (replacement.TrialRequestCount == 0 || replacement.TrialGuardAdmittedCount != 0))
                throw new InvalidDataException("policy trial execution replacement allocation tracking is invalid");
            if (replacement.TrialExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted && replacement.TrialExecutionDecisionID.Value == 0)
                throw new InvalidDataException("policy trial execution replacement marks an absent corroboration observed");
            if (replacement.TrialRequestCount == 0 && (replacement.TrialLastRequestDecisionID.Value != 0 || replacement.TrialLastRequestStep != -1))
                throw new InvalidDataException("policy trial execution replacement invents a request identity");
            if (replacement.TrialRequestCount > 0)
            {
                if (replacement.TrialLastRequestDecisionID.Value == 0 || replacement.TrialLastRequestStep < 0)
                    throw new InvalidDataException("policy trial execution replacement omits its last request identity");
                replacement.TrialLastRequestReadout.Validate(state.Schema.ActionCount);
            }
            if (replacement.TrialExecutionDecisionID.Value == 0)
            {
                if (replacement.TrialExecutionReadoutFingerprint != 0)
                    throw new InvalidDataException("policy trial execution replacement invents an executed readout fingerprint");
                state.TrialExecutionCause = replacement.TrialExecutionCause;
                state.TrialExecutionOutcome = replacement.TrialExecutionOutcome;
                state.TrialExecutionCorroboration = null;
                state.TrialExecutionReadoutFingerprint = 0;
                state.TrialExecutionStep = -1;
            }
            else
            {
                replacement.TrialExecutionReadout.Validate(state.Schema.ActionCount);
                if (replacement.TrialExecutionReadout.SelectionCause != replacement.TrialExecutionCause)
                    throw new InvalidDataException("policy trial execution replacement corroboration disagrees with its configured rail");
                state.TrialExecutionCause = replacement.TrialExecutionCause;
                state.TrialExecutionOutcome = replacement.TrialExecutionOutcome;
                state.TrialExecutionCorroboration = new CortexPolicyDecision(
                    replacement.TrialExecutionDecisionID, replacement.Policy, replacement.TrialExecutionReadout);
                if (replacement.TrialExecutionReadoutFingerprint == 0)
                    throw new InvalidDataException("policy trial execution replacement omits its executed readout fingerprint");
                state.TrialExecutionReadoutFingerprint = replacement.TrialExecutionReadoutFingerprint;
                if (replacement.TrialExecutionStep < -1)
                    throw new InvalidDataException("policy trial execution replacement step is invalid");
                state.TrialExecutionStep = replacement.TrialExecutionStep;
            }
            replacement.HistoricalTrialExecution.Validate(replacement.Policy, state.Schema.ActionCount);
            state.HistoricalTrialExecution = replacement.HistoricalTrialExecution;
            state.SuppressTrialPackets = replacement.SuppressTrialPackets;
            if (replacement.ActiveTrialQuotaID.Value != 0 && !state.SuppressTrialPackets)
                throw new InvalidDataException("policy trial execution replacement carries active quota without a suppressed trial");
            CortexPolicyPendingForcedTrialIntent pendingIntent = replacement.PendingForcedTrialIntent;
            ValidatePendingForcedTrialIntent(replacement.Policy, in pendingIntent);
            if (pendingIntent.IsBound && _runtimeRun is not null
                && !TryAuthenticatePendingForcedTrialIntentAuthority(in pendingIntent))
                throw new InvalidDataException("policy pending forced trial replacement carries unauthenticated audit-only");
            state.PendingForcedTrialIntent = replacement.PendingForcedTrialIntent;
            if (replacement.ActiveTrialQuotaID.Value != 0
                && replacement.PendingForcedTrialIntent.HasSeed
                && replacement.PendingForcedTrialIntent.IsBound
                && replacement.ActiveTrialQuotaID.Value != replacement.PendingForcedTrialIntent.QuotaID)
                throw new InvalidDataException("policy trial execution replacement quota identity disagrees with forced intent");
            state.ActiveTrialQuotaID = replacement.ActiveTrialQuotaID;
            if (state.ActiveTrialQuotaID.Value != 0
                && _runtimeRun is not null
                && !TryAuthenticatePolicyTrialQuotaIdentity(state, state.ActiveTrialQuotaID))
                throw new InvalidDataException("policy trial execution replacement carries unauthenticated active quota authority");
            state.TrialRequestCount = replacement.TrialRequestCount;
            state.TrialGuardAdmittedCount = replacement.TrialGuardAdmittedCount;
            state.TrialLastRequest = replacement.TrialLastRequestDecisionID.Value == 0
                ? null
                : new CortexPolicyDecision(replacement.TrialLastRequestDecisionID, replacement.Policy, replacement.TrialLastRequestReadout);
            state.TrialLastRequestStep = replacement.TrialLastRequestStep;
            state.CanonicalCandidates.Clear();
            IPolicyBoundaryDomain? candidateDomain = _policyBoundaryDomains.TryGetValue(replacement.Policy, out IPolicyBoundaryDomain registeredCandidateDomain)
                ? registeredCandidateDomain : null;
            bool candidateRequiresScope = candidateDomain is not null
                && candidateDomain.CanonicalScopeMode != PolicyCanonicalScopeModes.None;
            if (replacement.CanonicalCandidates is null || replacement.CanonicalCandidates.Length > PolicyReadoutCache.MaxEntries)
                throw new InvalidDataException("policy canonical candidate replacement exceeds its bounded working set");
            foreach (CortexPolicyCanonicalCandidateReplacement candidate in replacement.CanonicalCandidates)
            {
                PolicyCanonicalStateID candidateState = candidate.State;
                if (!candidate.State.Policy.Equals(replacement.Policy)
                    || candidateRequiresScope && !candidateDomain!.ValidateCanonicalState(in candidateState)
                    || !state.CanonicalCandidates.TryAdd(candidate.State,
                    new PolicyState.CanonicalCandidateEvidence(candidate.State, candidate.Action, candidate.CandidateFingerprint,
                        candidate.OccurrenceDigest, candidate.Revision, candidate.InstalledStep)
                    {
                        OriginRevision = candidate.OriginRevision, Comparisons = candidate.Comparisons,
                        Agreements = candidate.Agreements, Misses = candidate.Misses,
                    }))
                    throw new InvalidDataException("policy canonical candidate replacement is malformed");
            }
            state.VerifiedScopes.Clear();
            if (replacement.VerifiedScopes is null || replacement.VerifiedScopes.Length > replacement.CanonicalCandidates.Length)
                throw new InvalidDataException("policy verified scope replacement exceeds its candidate set");
            foreach (PolicyVerifiedScopeEntry scope in replacement.VerifiedScopes)
            {
                PolicyCanonicalStateID scopeState = scope.State;
                if (!scope.IsValid || candidateRequiresScope && !candidateDomain!.ValidateCanonicalState(in scopeState)
                    || !state.CanonicalCandidates.TryGetValue(scope.State, out PolicyState.CanonicalCandidateEvidence? candidate)
                    || candidate.CandidateFingerprint != scope.CandidateFingerprint
                    || candidate.OccurrenceDigest != scope.OccurrenceDigest
                    || candidate.Revision != scope.Revision
                    || !state.VerifiedScopes.TryAdd(scope.State, scope))
                    throw new InvalidDataException("policy verified scope replacement is malformed");
            }
            state.ReadoutCache.ApplyReplacement(replacement.Cache);
        }
    }

    private void ValidatePendingForcedTrialIntent(
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialIntent intent)
    {
        if (!intent.HasSeed) return;
        if (!_policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain domain))
            throw new InvalidDataException("policy pending forced trial intent is malformed: policy has no boundary domain");
        if (!intent.Policy.Equals(policy)) throw new InvalidDataException("policy pending forced trial intent is malformed: embedded policy disagrees");
        if (!intent.IsBound) throw new InvalidDataException($"policy pending forced trial intent is malformed: unbound quota={intent.QuotaID:X} sourceDecision={intent.SourceDecisionID} sourceEvent={intent.SourceDecisionEventID} corroboration={intent.SourceCorroborationEventID} occurrence={intent.SourceOccurrenceDigest:X} sourceCandidate={intent.SourceCandidateFingerprint:X} sourceQuotaCandidate={intent.SourceQuotaCandidateFingerprint:X} sourceReadout={intent.SourceReadoutFingerprint:X} sourceRevision={intent.SourceCandidateRevision.Value} sourceState={intent.SourceCanonicalState} successorReadout={intent.ReadoutFingerprint:X} successorCandidate={intent.CandidateFingerprint:X} successorRevision={intent.CandidateRevision.Value} successorOccurrence={intent.SuccessorOccurrenceDigest:X} state={intent.CanonicalState} obligation={intent.ObligationID} run={intent.SourceRunID} audit-only={intent.AuditOnlyDigest}");
        PolicyCanonicalStateID successorState = intent.CanonicalState;
        if (!domain.ValidateCanonicalState(in successorState)) throw new InvalidDataException("policy pending forced trial intent is malformed: successor state is invalid");
        if (intent.Arm != (byte)PolicyBoundaryArms.ForcedDivergentNull) throw new InvalidDataException("policy pending forced trial intent is malformed: arm is not forced divergent null");
        if (intent.FeatureID != domain.BoundaryFeatureID) throw new InvalidDataException("policy pending forced trial intent is malformed: feature is not owned by the boundary domain");
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || !string.Equals(obligation.ID.Value, intent.ObligationID, StringComparison.Ordinal))
            throw new InvalidDataException($"policy pending forced trial intent is malformed: obligation is not registered expected={(obligation is null ? "missing" : obligation.ID.Value)} actual={intent.ObligationID}");
        PolicyCanonicalStateID sourceState = intent.SourceCanonicalState;
        if (intent.QuotaID == 0
            || intent.SourceCandidateFingerprint == 0
            || intent.SourceQuotaCandidateFingerprint == 0
            || intent.SourceReadoutFingerprint == 0
            || intent.SourceCandidateRevision == GrammarRevisionID.Zero
            || !domain.ValidateCanonicalState(in sourceState)
            || !IsAuthenticatedAuditOnlyDigest(intent.AuditOnlyDigest))
            throw new InvalidDataException("policy pending forced trial intent quota audit-only disagrees with its source tuple");
    }

    private CortexPolicyReadoutAllocationReplacement CapturePolicyReadoutAllocationReplacement()
    {
        List<CortexPolicyReadoutAllocationReceipt> allocationStates = new();
        AppendPolicyReadoutAllocationStates(allocationStates);
        return new(_policyReadoutRosterSealed, _policyReadoutRosterDigest, _policyReadoutLastQuotaStep,
            _policyReadoutAllocationCursor, _policyReadoutAllocationSequence, allocationStates.ToArray(),
            _policyReadoutUsedUnits, _policyReadoutHeldUnits, _policyReadoutCompletedUsedUnits);
    }

    private void ApplyPolicyReadoutAllocation(in CortexPolicyReadoutAllocationReplacement replacement)
    {
        if (!replacement.RosterSealed || replacement.AllocationStates is null || replacement.AllocationStates.Length != _policyReadoutRoster.Count)
            throw new InvalidDataException("policy readout allocation replacement roster is invalid");
        if (replacement.RosterDigest != _policyReadoutRosterDigest || replacement.AllocationCursor < 0
            || replacement.AllocationCursor >= Math.Max(1, _policyReadoutRoster.Count))
            throw new InvalidDataException("policy readout allocation replacement cursor is invalid");
        foreach (CortexPolicyReadoutAllocationReceipt receipt in replacement.AllocationStates)
        {
            PolicyReadoutAllocationState allocationState = GetPolicyReadoutAllocationState(receipt.Policy);
            allocationState.AvailableUnits = receipt.AvailableUnits; allocationState.AllocatedUnits = receipt.AllocatedUnits; allocationState.HeldUnits = receipt.HeldUnits;
            allocationState.UsedUnits = receipt.UsedUnits; allocationState.ReclaimedUnits = receipt.ReclaimedUnits;
            allocationState.ExpiredUnits = receipt.ExpiredUnits; allocationState.LastAllocationSequence = receipt.LastAllocationSequence;
        }
        _policyReadoutLastQuotaStep = replacement.LastQuotaStep;
        _policyReadoutAllocationCursor = replacement.AllocationCursor;
        _policyReadoutAllocationSequence = replacement.AllocationSequence;
        _policyReadoutUsedUnits = replacement.UsedUnits; _policyReadoutHeldUnits = replacement.HeldUnits;
        _policyReadoutCompletedUsedUnits = replacement.CompletedUsedUnits;
    }

    private static void ValidatePolicyRows(in CortexPolicyCheckpointDelta delta)
    {
        if (delta.TrialQuota is null || delta.TrialCompletions is null || delta.ReadoutQuota is null
            || delta.ReadoutCompletions is null || delta.Allocations is null || delta.States is null
            || delta.TrialQuota.Length > MaxPolicyDeltaRows || delta.TrialCompletions.Length > MaxPolicyDeltaRows
            || delta.ReadoutQuota.Length > MaxPolicyDeltaRows || delta.ReadoutCompletions.Length > MaxPolicyDeltaRows
            || delta.Allocations.Length > MaxPolicyDeltaRows)
            throw new InvalidDataException("policy checkpoint delta exceeds its bounded journal size");
        for (int i = 0; i < delta.TrialQuota.Length; i++)
            if (delta.TrialQuota[i].Decision == CortexPolicyQuotaDecisions.Reused)
                ValidateTrialQuotaReuse(in delta.TrialQuota[i]);
    }

    private static void ValidateTrialQuotaReuse(in CortexPolicyTrialQuotaDecision row)
    {
        if (row.QuotaDecisionID.Value == 0
            || row.PlannedArmSteps < 0
            || row.HeldArmSteps != row.PlannedArmSteps
            || row.UsedSteps != 0
            || row.RemainingQuota < 0
            || row.DenialReason != CortexPolicyTrialDenialReasons.None
            || !Enum.IsDefined(row.CandidateState))
            throw new InvalidDataException($"policy checkpoint carries malformed reused quota decision {row.QuotaDecisionID}");
    }

    private static void ValidatePolicyCursor(int cursor, int count, string name)
    {
        if (cursor < 0 || cursor > count) throw new InvalidDataException($"policy {name} cursor {cursor} is outside {count} rows");
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in CortexPolicyCheckpointDelta delta)
    {
        writer.U8(CurrentPolicyDeltaVersion); writer.U64(delta.NextDecisionID); writer.I64(delta.TrialUsedSteps); writer.I64(delta.TrialHeldSteps); writer.I64(delta.TrialCompletedUsedSteps);
        writer.I32(delta.TrialQuotaCursor); WriteTrialQuota(writer, delta.TrialQuota, canonicalStatePresent: true);
        writer.I32(delta.TrialCompletionCursor); WriteCompletions(writer, delta.TrialCompletions);
        writer.I32(delta.ReadoutQuotaCursor); WriteReadoutQuota(writer, delta.ReadoutQuota);
        writer.I32(delta.ReadoutCompletionCursor); WriteCompletions(writer, delta.ReadoutCompletions);
        writer.I32(delta.AllocationCursor); WriteAllocations(writer, delta.Allocations);
        CortexPolicyReadoutStateReplacement[] states = delta.States;
        CortexPolicyReadoutAllocationReplacement allocation = delta.Allocation;
        WriteStates(writer, states, executionFingerprintPresent: true, historicalExecutionPresent: true, suppressionPresent: true, readoutIdentityPresent: true); WriteAllocation(writer, allocation);
    }

    internal static CortexPolicyCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte version = reader.U8();
        if (version is not (HistoricalPolicyDeltaVersion or 2 or 3 or 4 or 5 or 7 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or CurrentPolicyDeltaVersion))
            throw new InvalidDataException("unknown Cortex policy checkpoint delta version");
        ulong next = reader.U64(); long used = reader.I64(); long held = reader.I64(); long completed = reader.I64();
        int trialQuotaCursor = reader.I32(); CortexPolicyTrialQuotaDecision[] trialQuota = ReadTrialQuota(reader, version >= 3, version >= 7, version >= 8);
        int trialCompletionCursor = reader.I32(); CortexPolicyTrialCompletion[] trialCompletions = ReadCompletions(reader);
        int readoutQuotaCursor = reader.I32(); CortexPolicyReadoutQuotaDecision[] readoutQuota = ReadReadoutQuota(reader);
        int readoutCompletionCursor = reader.I32(); CortexPolicyTrialCompletion[] readoutCompletions = ReadCompletions(reader);
        int allocationCursor = reader.I32(); CortexPolicyReadoutAllocation[] allocations = ReadAllocations(reader);
        CortexPolicyReadoutStateReplacement[] states = ReadStates(reader, version >= 2, version >= 4, version >= 5, version >= 6, version >= 7, version >= 8, version >= 9, version >= 10, version >= 11, version >= 12, version >= 13, version >= 14, version >= 15, version >= 16, version >= 17); CortexPolicyReadoutAllocationReplacement allocation = ReadAllocation(reader);
        return new(trialQuotaCursor, trialQuota, trialCompletionCursor, trialCompletions, readoutQuotaCursor, readoutQuota,
            readoutCompletionCursor, readoutCompletions, allocationCursor, allocations, states, allocation, next, used, held, completed);
    }

    private static void WriteTrialQuota(CkptWriter writer, CortexPolicyTrialQuotaDecision[] rows, bool canonicalStatePresent)
    {
        WriteCount(writer, rows); foreach (CortexPolicyTrialQuotaDecision row in rows)
        {
            writer.U64(row.QuotaDecisionID.Value); writer.Str(row.Policy.Value); writer.U64(row.CandidateFingerprint); writer.U64(row.ReadoutFingerprint); writer.I32(row.QuotaStep); writer.I32(row.RequestedHorizonSteps); writer.I32(row.ArmCount); writer.I64(row.PlannedArmSteps); writer.I64(row.HeldArmSteps); writer.U8((byte)row.Decision); writer.I64(row.UsedSteps); writer.I64(row.RemainingQuota); writer.U8((byte)row.CandidateState); writer.U8((byte)row.DenialReason); writer.I32(row.CandidateOriginStep); writer.I32(row.CandidateCurrentStep); writer.I32(row.CandidateRequiredStep); writer.U64(row.CandidateRevision.Value); writer.Str(row.AllocationIdentity); writer.Str(row.AllocationDigest); writer.I64(row.AllocationArmSteps); writer.Str(row.SeedAuditOnlyDigest); if (canonicalStatePresent) { writer.Bool(row.CanonicalState.Version != 0); if (row.CanonicalState.Version != 0) WriteCanonicalState(writer, row.CanonicalState); }
        }
    }

    private static CortexPolicyTrialQuotaDecision[] ReadTrialQuota(CkptReader reader, bool readAuditOnlyDigest, bool readReadoutFingerprint, bool canonicalStatePresent)
    {
        int count = ReadCount(reader); CortexPolicyTrialQuotaDecision[] rows = new CortexPolicyTrialQuotaDecision[count];
        for (int i = 0; i < count; i++)
        {
            CortexPolicyQuotaDecisionID quotaID = new(reader.U64());
            CortexPolicyID policy = new(reader.Str());
            ulong candidateFingerprint = reader.U64();
            ulong readoutFingerprint = readReadoutFingerprint ? reader.U64() : 0;
            rows[i] = new(quotaID, policy, candidateFingerprint, reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64(), (CortexPolicyQuotaDecisions)reader.U8(), reader.I64(), reader.I64())
            {
                ReadoutFingerprint = readoutFingerprint,
                CandidateState = (CortexPolicyTrialCandidateStates)reader.U8(), DenialReason = (CortexPolicyTrialDenialReasons)reader.U8(), CandidateOriginStep = reader.I32(), CandidateCurrentStep = reader.I32(), CandidateRequiredStep = reader.I32(), CandidateRevision = new GrammarRevisionID(reader.U64()), AllocationIdentity = reader.Str(), AllocationDigest = reader.Str(), AllocationArmSteps = reader.I64(), SeedAuditOnlyDigest = readAuditOnlyDigest ? reader.Str() : ""
            };
            if (readReadoutFingerprint && (candidateFingerprint == 0 || readoutFingerprint == 0))
                throw new InvalidDataException("current policy delta omits the split candidate/readout identity");
            if (canonicalStatePresent && reader.Bool())
            {
                PolicyCanonicalStateID state = ReadCanonicalState(reader);
                rows[i] = rows[i] with { CanonicalState = state };
            }
        }
        return rows;
    }

    private static void WriteCompletions(CkptWriter writer, CortexPolicyTrialCompletion[] rows)
    {
        WriteCount(writer, rows); foreach (CortexPolicyTrialCompletion row in rows)
        {
            writer.U64(row.QuotaDecisionID.Value); writer.I64(row.ActualExecutedArmSteps); writer.I64(row.ReclaimedOrUnused); writer.Bool(row.EvaluatorWorkUnits.HasValue); if (row.EvaluatorWorkUnits is long work) writer.I64(work); writer.U8((byte)row.VerifierOutcome); writer.Bool(row.WallMilliseconds.HasValue); if (row.WallMilliseconds is long wall) writer.I64(wall);
        }
    }

    private static CortexPolicyTrialCompletion[] ReadCompletions(CkptReader reader)
    {
        int count = ReadCount(reader); CortexPolicyTrialCompletion[] rows = new CortexPolicyTrialCompletion[count];
        for (int i = 0; i < count; i++) rows[i] = new(new CortexPolicyQuotaDecisionID(reader.U64()), reader.I64(), reader.I64(), reader.Bool() ? reader.I64() : null, (CortexPolicyVerifierOutcomes)reader.U8(), reader.Bool() ? reader.I64() : null);
        return rows;
    }

    private static void WriteReadoutQuota(CkptWriter writer, CortexPolicyReadoutQuotaDecision[] rows)
    {
        WriteCount(writer, rows); foreach (CortexPolicyReadoutQuotaDecision row in rows)
        {
            writer.U64(row.QuotaDecisionID.Value); writer.Str(row.Policy.Value); writer.U64(row.CandidateFingerprint); writer.U64(row.GrammarRevision.Value); writer.U64(row.ContextDigest); writer.I32(row.ContextBytes); writer.I32(row.DeliberationDepth); writer.I32(row.QuotaStep); writer.I64(row.PlannedUnits); writer.I64(row.HeldUnits); writer.U8((byte)row.Decision); writer.I64(row.UsedUnits); writer.I64(row.RemainingQuota); writer.I64(row.AllocationSequence); writer.Str(row.RosterDigest); writer.I64(row.AvailableBefore); writer.I64(row.AvailableAfter);
        }
    }

    private static CortexPolicyReadoutQuotaDecision[] ReadReadoutQuota(CkptReader reader)
    {
        int count = ReadCount(reader); CortexPolicyReadoutQuotaDecision[] rows = new CortexPolicyReadoutQuotaDecision[count];
        for (int i = 0; i < count; i++) rows[i] = new(new CortexPolicyQuotaDecisionID(reader.U64()), new CortexPolicyID(reader.Str()), reader.U64(), new GrammarRevisionID(reader.U64()), reader.U64(), reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64(), (CortexPolicyQuotaDecisions)reader.U8(), reader.I64(), reader.I64(), reader.I64(), reader.Str(), reader.I64(), reader.I64());
        return rows;
    }

    private static void WriteAllocations(CkptWriter writer, CortexPolicyReadoutAllocation[] rows)
    {
        WriteCount(writer, rows); foreach (CortexPolicyReadoutAllocation row in rows) { writer.I64(row.Sequence); writer.I32(row.Step); writer.Str(row.RosterDigest); writer.Str(row.Policy.Value); writer.I64(row.AvailableBefore); writer.I64(row.AllocatedUnits); writer.I64(row.ExpiredUnits); writer.I64(row.AvailableAfter); }
    }

    private static CortexPolicyReadoutAllocation[] ReadAllocations(CkptReader reader)
    {
        int count = ReadCount(reader); CortexPolicyReadoutAllocation[] rows = new CortexPolicyReadoutAllocation[count];
        for (int i = 0; i < count; i++) rows[i] = new(reader.I64(), reader.I32(), reader.Str(), new CortexPolicyID(reader.Str()), reader.I64(), reader.I64(), reader.I64(), reader.I64());
        return rows;
    }

    private static void WriteStates(CkptWriter writer, CortexPolicyReadoutStateReplacement[] rows, bool executionFingerprintPresent, bool historicalExecutionPresent, bool suppressionPresent, bool readoutIdentityPresent)
    {
            WriteCount(writer, rows); foreach (CortexPolicyReadoutStateReplacement row in rows)
        {
            if (historicalExecutionPresent) writer.I32(row.ActionCount);
            writer.Str(row.Policy.Value); writer.U8((byte)row.Mode); writer.U8((byte)row.Authority); writer.U64(row.ObservedInstallRevision.Value); writer.U64(row.ReadoutCandidateRevision.Value); writer.U64(row.ReadoutCandidateFingerprint); writer.U64(row.ReadoutCandidateSetDigest); PolicyCanonicalStateID candidateState = row.ReadoutCandidateState; WriteCanonicalState(writer, candidateState); writer.U64(row.ReadoutCandidateOccurrenceDigest); writer.I32(row.ReadoutCandidateAction); writer.Bool(row.ReadoutCandidatePending); writer.I32(row.ReadoutInstalledStep); writer.I32(row.ReadoutOracleComparisons); writer.I32(row.ReadoutOracleAgreements); writer.I32(row.ShadowComparisons); writer.I32(row.ShadowAgreements); writer.I32(row.EmulationMisses); writer.U8((byte)row.TrialExecutionCause); writer.U8((byte)row.TrialExecutionOutcome); WritePendingForcedTrialIntent(writer, row.PendingForcedTrialIntent); writer.U64(row.ActiveTrialQuotaID.Value); if (suppressionPresent) writer.Bool(row.SuppressTrialPackets); writer.U64(row.TrialExecutionDecisionID.Value); if (row.TrialExecutionDecisionID.Value != 0) { writer.I32(row.TrialExecutionStep); if (executionFingerprintPresent) writer.U64(row.TrialExecutionReadoutFingerprint); writer.I32(row.TrialExecutionReadout.LaunchpadAction); writer.I32(row.TrialExecutionReadout.RawCandidateAction); writer.I32(row.TrialExecutionReadout.SelectedCandidateAction); writer.I32(row.TrialExecutionReadout.ExecutedAction); writer.U8((byte)row.TrialExecutionReadout.Authority); writer.U64(row.TrialExecutionReadout.GrammarRevision.Value); writer.U8((byte)row.TrialExecutionReadout.SelectionCause); writer.U64(row.TrialExecutionReadout.ReadoutCandidateOccurrenceDigest); writer.U64(row.TrialExecutionReadout.ReadoutCandidateFingerprint); if (readoutIdentityPresent) writer.U64(row.TrialExecutionReadout.ReadoutFingerprint); } writer.I64(row.TrialRequestCount); writer.I64(row.TrialGuardAdmittedCount); writer.U64(row.TrialLastRequestDecisionID.Value); if (row.TrialLastRequestDecisionID.Value != 0) { writer.I32(row.TrialLastRequestStep); writer.I32(row.TrialLastRequestReadout.LaunchpadAction); writer.I32(row.TrialLastRequestReadout.RawCandidateAction); writer.I32(row.TrialLastRequestReadout.SelectedCandidateAction); writer.I32(row.TrialLastRequestReadout.ExecutedAction); writer.U8((byte)row.TrialLastRequestReadout.Authority); writer.U64(row.TrialLastRequestReadout.GrammarRevision.Value); writer.U8((byte)row.TrialLastRequestReadout.SelectionCause); writer.U64(row.TrialLastRequestReadout.ReadoutCandidateOccurrenceDigest); writer.U64(row.TrialLastRequestReadout.ReadoutCandidateFingerprint); if (readoutIdentityPresent) writer.U64(row.TrialLastRequestReadout.ReadoutFingerprint); } WriteHistoricalTrialExecution(writer, row.Policy, row.ActionCount, row.HistoricalTrialExecution, historicalExecutionPresent, readoutIdentityPresent); writer.U64(row.AssayedReadoutFingerprint); writer.U64(row.AssayedFingerprint); writer.U64(row.VerifiedReadoutFingerprint); writer.U64(row.VerifiedFingerprint); writer.U64(row.VerifiedRevision.Value); CortexPolicyCanonicalCandidateReplacement[] candidates = row.CanonicalCandidates; WriteCandidates(writer, candidates); WriteVerifiedScopes(writer, row.VerifiedScopes); PolicyReadoutCacheReplacement cache = row.Cache; WriteCacheReplacement(writer, cache);
        }
    }

    private static CortexPolicyReadoutStateReplacement[] ReadStates(CkptReader reader, bool learnerEvidencePresent, bool trialExecutionPresent, bool trialExecutionStepPresent, bool trialAllocationPresent, bool verifiedIdentityPresent, bool verifiedScopesPresent, bool pendingIntentPresent, bool sourceIdentityPresent, bool sourceQuotaPresent, bool executionFingerprintPresent, bool historicalExecutionPresent, bool activeQuotaPresent, bool suppressionPresent, bool assayedFingerprintPresent, bool readoutIdentityPresent)
    {
        int count = ReadCount(reader); CortexPolicyReadoutStateReplacement[] rows = new CortexPolicyReadoutStateReplacement[count];
        for (int i = 0; i < count; i++)
        {
            int actionCount = historicalExecutionPresent ? reader.I32() : 0;
            CortexPolicyID policy = new(reader.Str()); CortexPolicyModes mode = (CortexPolicyModes)reader.U8(); CortexPolicyAuthorities authority = (CortexPolicyAuthorities)reader.U8();
            GrammarRevisionID observed = new(reader.U64()); GrammarRevisionID candidateRevision = new(reader.U64()); ulong candidateFingerprint = reader.U64(); ulong candidateSetDigest = reader.U64();
            PolicyCanonicalStateID candidateState = ReadCanonicalState(reader); ulong candidateOccurrenceDigest = reader.U64(); int candidateAction = reader.I32(); bool candidatePending = reader.Bool();
            int installedStep = reader.I32(); int oracleComparisons = reader.I32(); int oracleAgreements = reader.I32();
            int comparisons = learnerEvidencePresent ? reader.I32() : 0; int agreements = learnerEvidencePresent ? reader.I32() : 0; int misses = learnerEvidencePresent ? reader.I32() : 0;
            CortexPolicySelectionCauses trialCause = trialExecutionPresent ? (CortexPolicySelectionCauses)reader.U8() : CortexPolicySelectionCauses.Launchpad;
            CortexPolicyTrialExecutionOutcomes trialOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
            if (trialAllocationPresent)
            {
                byte encodedOutcome = reader.U8();
                trialOutcome = pendingIntentPresent
                    ? (CortexPolicyTrialExecutionOutcomes)encodedOutcome
                    : encodedOutcome switch
                    {
                        0 => CortexPolicyTrialExecutionOutcomes.GuardDenied,
                        1 => CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted,
                        _ => throw new InvalidDataException("legacy policy trial execution outcome is invalid"),
                    };
            }
            CortexPolicyPendingForcedTrialIntent pendingIntent = pendingIntentPresent ? ReadPendingForcedTrialIntent(reader, policy, sourceIdentityPresent, sourceQuotaPresent) : default;
            CortexPolicyQuotaDecisionID activeTrialQuotaID = activeQuotaPresent ? new CortexPolicyQuotaDecisionID(reader.U64()) : default;
            bool encodedSuppression = suppressionPresent && reader.Bool();
            CortexPolicyDecisionID trialDecisionID = trialExecutionPresent ? new(reader.U64()) : default;
            if (!trialAllocationPresent && trialDecisionID.Value != 0)
                trialOutcome = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
            int trialExecutionStep = trialDecisionID.Value != 0 && trialExecutionStepPresent ? reader.I32() : -1;
            ulong trialExecutionReadoutFingerprint = trialDecisionID.Value != 0 && executionFingerprintPresent ? reader.U64() : 0;
            CortexPolicyDecisionReadout trialReadout = trialDecisionID.Value == 0 ? default : new(
                reader.I32(), reader.I32(), reader.I32(), reader.I32(), (CortexPolicyAuthorities)reader.U8(),
                new GrammarRevisionID(reader.U64()), (CortexPolicySelectionCauses)reader.U8(), reader.U64(), reader.U64(), readoutIdentityPresent ? reader.U64() : 0);
            long trialRequestCount = trialAllocationPresent ? reader.I64() : 0;
            long trialGuardAdmittedCount = trialAllocationPresent ? reader.I64() : 0;
            CortexPolicyDecisionID trialLastRequestID = trialAllocationPresent ? new(reader.U64()) : default;
            int trialLastRequestStep = trialLastRequestID.Value != 0 && trialAllocationPresent ? reader.I32() : -1;
            CortexPolicyDecisionReadout trialLastRequestReadout = trialLastRequestID.Value == 0 ? default : new(
                reader.I32(), reader.I32(), reader.I32(), reader.I32(), (CortexPolicyAuthorities)reader.U8(),
                new GrammarRevisionID(reader.U64()), (CortexPolicySelectionCauses)reader.U8(), reader.U64(), reader.U64(), readoutIdentityPresent ? reader.U64() : 0);
            PolicyTrialExecutionHistory historicalTrialExecution = historicalExecutionPresent
                ? ReadHistoricalTrialExecution(reader, policy, actionCount, readoutIdentityPresent)
                : default;
            bool suppressTrialPackets = suppressionPresent
                ? encodedSuppression
                : activeTrialQuotaID.Value != 0
                    || !historicalTrialExecution.IsPresent && trialCause != CortexPolicySelectionCauses.Launchpad;
            ulong assayedReadoutFingerprint = verifiedIdentityPresent ? reader.U64() : 0;
            ulong assayedFingerprint = assayedFingerprintPresent
                ? reader.U64()
                : assayedReadoutFingerprint != 0 ? candidateFingerprint : 0;
            ulong verifiedReadoutFingerprint = verifiedIdentityPresent ? reader.U64() : 0;
            ulong verifiedFingerprint = verifiedIdentityPresent ? reader.U64() : 0;
            GrammarRevisionID verifiedRevision = verifiedIdentityPresent ? new GrammarRevisionID(reader.U64()) : GrammarRevisionID.Zero;
            rows[i] = new(policy, mode, authority, observed, candidateRevision, candidateFingerprint, candidateSetDigest, candidateState,
                candidateOccurrenceDigest, candidateAction, candidatePending, installedStep, oracleComparisons, oracleAgreements,
                comparisons, agreements, misses, learnerEvidencePresent, trialCause, trialOutcome, suppressTrialPackets, pendingIntent, activeTrialQuotaID, trialDecisionID, trialReadout, trialExecutionStep, trialExecutionReadoutFingerprint,
                trialRequestCount, trialGuardAdmittedCount, trialLastRequestID, trialLastRequestReadout, trialLastRequestStep, historicalTrialExecution,
                ReadCandidates(reader), verifiedScopesPresent ? ReadVerifiedScopes(reader) : Array.Empty<PolicyVerifiedScopeEntry>(), ReadCacheReplacement(reader))
            {
                ActionCount = actionCount,
                AssayedReadoutFingerprint = assayedReadoutFingerprint,
                AssayedFingerprint = assayedFingerprint,
                VerifiedReadoutFingerprint = verifiedReadoutFingerprint,
                VerifiedFingerprint = verifiedFingerprint,
                VerifiedRevision = verifiedRevision,
            };
        }
        return rows;
    }

    private static void WriteHistoricalTrialExecution(CkptWriter writer, CortexPolicyID policy, int actionCount, PolicyTrialExecutionHistory history, bool present, bool readoutIdentityPresent)
    {
        writer.Bool(present && history.IsPresent);
        if (!present || !history.IsPresent) return;
        history.Validate(policy, actionCount);
        writer.U64(history.QuotaDecisionID.Value); writer.U8((byte)history.Cause); writer.U8((byte)history.Outcome);
        writer.I64(history.RequestCount); writer.I64(history.GuardAdmittedCount);
        writer.U64(history.LastRequestDecisionID.Value);
        if (history.LastRequestDecisionID.Value != 0)
        {
            writer.I32(history.LastRequestStep);
            CortexPolicyDecisionCheckpoint.Write(writer,
                new CortexPolicyDecision(history.LastRequestDecisionID, policy, history.LastRequestReadout), readoutIdentityPresent);
        }
        writer.U64(history.ExecutionDecisionID.Value); writer.I32(history.ExecutionStep);
        writer.U64(history.ExecutionReadoutFingerprint);
        CortexPolicyDecisionCheckpoint.Write(writer,
            new CortexPolicyDecision(history.ExecutionDecisionID, policy, history.ExecutionReadout), readoutIdentityPresent);
        WriteCanonicalState(writer, history.Scope.State);
        writer.U64(history.Scope.ReadoutFingerprint); writer.U64(history.Scope.CandidateFingerprint);
        writer.U64(history.Scope.OccurrenceDigest); writer.U64(history.Scope.Revision.Value);
    }

    private static PolicyTrialExecutionHistory ReadHistoricalTrialExecution(CkptReader reader, CortexPolicyID policy, int actionCount, bool readoutIdentityPresent)
    {
        if (!reader.Bool()) return default;
        CortexPolicyQuotaDecisionID quotaID = new(reader.U64());
        CortexPolicySelectionCauses cause = (CortexPolicySelectionCauses)reader.U8();
        CortexPolicyTrialExecutionOutcomes outcome = (CortexPolicyTrialExecutionOutcomes)reader.U8();
        long requests = reader.I64(); long admitted = reader.I64();
        CortexPolicyDecisionID lastID = new(reader.U64()); CortexPolicyDecisionReadout lastReadout = default; int lastStep = -1;
        if (lastID.Value != 0)
        {
            lastStep = reader.I32();
            CortexPolicyDecision lastRequest = CortexPolicyDecisionCheckpoint.Read(reader, policy, actionCount, readoutIdentityPresent);
            if (!lastRequest.DecisionID.Equals(lastID) || lastStep < 0)
                throw new InvalidDataException("policy checkpoint historical trial last-request identity disagrees with its decision");
            lastReadout = lastRequest.Readout;
        }
        CortexPolicyDecisionID executionID = new(reader.U64()); int executionStep = reader.I32();
        ulong executionFingerprint = reader.U64();
        CortexPolicyDecision execution = CortexPolicyDecisionCheckpoint.Read(reader, policy, actionCount, readoutIdentityPresent);
        if (!execution.DecisionID.Equals(executionID) || executionID.Value == 0 || executionStep < 0)
            throw new InvalidDataException("policy checkpoint historical trial execution identity disagrees with its decision");
        PolicyCanonicalStateID scopeState = ReadCanonicalState(reader);
        PolicyVerifiedScopeEntry scope = new(scopeState, reader.U64(), reader.U64(), reader.U64(), new GrammarRevisionID(reader.U64()));
        PolicyTrialExecutionHistory history = new(quotaID, cause, outcome, requests, admitted, lastID, lastReadout, lastStep,
            executionID, execution.Readout, executionStep, executionFingerprint, scope);
        history.Validate(policy, actionCount);
        return history;
    }

    private static void WriteVerifiedScopes(CkptWriter writer, PolicyVerifiedScopeEntry[] scopes)
    {
        WriteCount(writer, scopes);
        for (int i = 0; i < scopes.Length; i++)
        {
            PolicyVerifiedScopeEntry scope = scopes[i];
            WriteCanonicalState(writer, scope.State);
            writer.U64(scope.ReadoutFingerprint); writer.U64(scope.CandidateFingerprint);
            writer.U64(scope.OccurrenceDigest); writer.U64(scope.Revision.Value);
        }
    }

    private static void WritePendingForcedTrialIntent(CkptWriter writer, CortexPolicyPendingForcedTrialIntent intent)
    {
        writer.Bool(intent.HasSeed);
        if (!intent.HasSeed) return;
        writer.U64(intent.QuotaID); writer.U8((byte)intent.SourceQuotaDecision); writer.U64(intent.ForcedDivergenceSeed);
        writer.U64(intent.SourceDecisionID); writer.I64(intent.SourceDecisionEventID); writer.I64(intent.SourceCorroborationEventID);
        writer.U64(intent.SourceOccurrenceDigest); writer.U64(intent.SourceCandidateFingerprint); writer.U64(intent.SourceQuotaCandidateFingerprint);
        writer.U64(intent.SourceReadoutFingerprint); writer.U64(intent.SourceCandidateRevision.Value);
        WriteCanonicalState(writer, intent.SourceCanonicalState);
        writer.U64(intent.ReadoutFingerprint);
        writer.U64(intent.CandidateFingerprint); writer.U64(intent.CandidateRevision.Value); writer.U64(intent.SuccessorOccurrenceDigest); WriteCanonicalState(writer, intent.CanonicalState);
        writer.Str(intent.ObligationID); writer.U8(intent.Arm); writer.U16(intent.FeatureID);
        writer.Str(intent.SourceRunID); writer.Str(intent.AuditOnlyDigest);
    }

    private static CortexPolicyPendingForcedTrialIntent ReadPendingForcedTrialIntent(CkptReader reader, CortexPolicyID policy, bool sourceIdentityPresent, bool sourceQuotaPresent)
    {
        if (!reader.Bool()) return default;
        ulong quotaID = reader.U64(); CortexPolicyQuotaDecisions sourceQuotaDecision = sourceQuotaPresent ? (CortexPolicyQuotaDecisions)reader.U8() : CortexPolicyQuotaDecisions.Denied; ulong seed = reader.U64(); ulong sourceDecisionID = reader.U64();
        long sourceDecisionEventID = reader.I64(); long sourceCorroborationEventID = reader.I64();
        ulong sourceOccurrenceDigest = reader.U64(); ulong sourceCandidateFingerprint = reader.U64(); ulong sourceQuotaCandidateFingerprint = sourceQuotaPresent ? reader.U64() : 0;
        ulong sourceReadoutFingerprint = sourceIdentityPresent ? reader.U64() : 0;
        GrammarRevisionID sourceCandidateRevision = sourceIdentityPresent ? new(reader.U64()) : GrammarRevisionID.Zero;
        PolicyCanonicalStateID sourceCanonicalState = sourceIdentityPresent ? ReadCanonicalState(reader) : default;
        ulong readoutFingerprint = reader.U64();
        ulong candidateFingerprint = reader.U64(); GrammarRevisionID candidateRevision = new(reader.U64());
        ulong successorOccurrenceDigest = sourceIdentityPresent ? reader.U64() : 0;
        PolicyCanonicalStateID canonicalState = ReadCanonicalState(reader);
        string obligationID = reader.Str(); byte arm = reader.U8(); ushort featureID = reader.U16();
        string sourceRunID = reader.Str(); string auditOnlyDigest = reader.Str();
        if (!sourceIdentityPresent)
        {
            sourceReadoutFingerprint = readoutFingerprint;
            sourceCandidateRevision = candidateRevision;
            sourceCanonicalState = canonicalState;
            successorOccurrenceDigest = sourceOccurrenceDigest;
        }
        return new(policy, quotaID, sourceQuotaDecision, seed, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
            sourceOccurrenceDigest, sourceCandidateFingerprint, sourceQuotaCandidateFingerprint, sourceReadoutFingerprint, sourceCandidateRevision, sourceCanonicalState,
            readoutFingerprint, candidateFingerprint, candidateRevision, successorOccurrenceDigest,
            canonicalState, obligationID, arm, featureID, sourceRunID, auditOnlyDigest);
    }

    private static PolicyVerifiedScopeEntry[] ReadVerifiedScopes(CkptReader reader)
    {
        int count = ReadCount(reader);
        PolicyVerifiedScopeEntry[] scopes = new PolicyVerifiedScopeEntry[count];
        for (int i = 0; i < count; i++)
            scopes[i] = new(ReadCanonicalState(reader), reader.U64(), reader.U64(), reader.U64(), new GrammarRevisionID(reader.U64()));
        return scopes;
    }

    /// Encodes the pre-cae1ad8 policy row shape for an on-disk dialect fixture.
    /// This is intentionally explicit: historical rows omitted learner counters.
    internal static void WriteHistoricalCheckpointDeltaFixture(CkptWriter writer, in CortexPolicyCheckpointDelta delta)
    {
        writer.U8(HistoricalPolicyDeltaVersion); writer.U64(delta.NextDecisionID); writer.I64(delta.TrialUsedSteps); writer.I64(delta.TrialHeldSteps); writer.I64(delta.TrialCompletedUsedSteps);
        writer.I32(delta.TrialQuotaCursor); WriteHistoricalTrialQuota(writer, delta.TrialQuota);
        writer.I32(delta.TrialCompletionCursor); WriteCompletions(writer, delta.TrialCompletions);
        writer.I32(delta.ReadoutQuotaCursor); WriteReadoutQuota(writer, delta.ReadoutQuota);
        writer.I32(delta.ReadoutCompletionCursor); WriteCompletions(writer, delta.ReadoutCompletions);
        writer.I32(delta.AllocationCursor); WriteAllocations(writer, delta.Allocations);
        WriteHistoricalStates(writer, delta.States); WriteAllocation(writer, delta.Allocation);
    }

    private static void WriteHistoricalTrialQuota(CkptWriter writer, CortexPolicyTrialQuotaDecision[] rows)
    {
        WriteCount(writer, rows); foreach (CortexPolicyTrialQuotaDecision row in rows)
        {
            writer.U64(row.QuotaDecisionID.Value); writer.Str(row.Policy.Value); writer.U64(row.CandidateFingerprint); writer.I32(row.QuotaStep); writer.I32(row.RequestedHorizonSteps); writer.I32(row.ArmCount); writer.I64(row.PlannedArmSteps); writer.I64(row.HeldArmSteps); writer.U8((byte)row.Decision); writer.I64(row.UsedSteps); writer.I64(row.RemainingQuota); writer.U8((byte)row.CandidateState); writer.U8((byte)row.DenialReason); writer.I32(row.CandidateOriginStep); writer.I32(row.CandidateCurrentStep); writer.I32(row.CandidateRequiredStep); writer.U64(row.CandidateRevision.Value); writer.Str(row.AllocationIdentity); writer.Str(row.AllocationDigest); writer.I64(row.AllocationArmSteps);
        }
    }

    private static void WriteHistoricalStates(CkptWriter writer, CortexPolicyReadoutStateReplacement[] rows)
    {
        WriteCount(writer, rows); foreach (CortexPolicyReadoutStateReplacement row in rows)
        {
            writer.Str(row.Policy.Value); writer.U8((byte)row.Mode); writer.U8((byte)row.Authority); writer.U64(row.ObservedInstallRevision.Value); writer.U64(row.ReadoutCandidateRevision.Value); writer.U64(row.ReadoutCandidateFingerprint); writer.U64(row.ReadoutCandidateSetDigest); WriteCanonicalState(writer, row.ReadoutCandidateState); writer.U64(row.ReadoutCandidateOccurrenceDigest); writer.I32(row.ReadoutCandidateAction); writer.Bool(row.ReadoutCandidatePending); writer.I32(row.ReadoutInstalledStep); writer.I32(row.ReadoutOracleComparisons); writer.I32(row.ReadoutOracleAgreements); WriteCandidates(writer, row.CanonicalCandidates); WriteCacheReplacement(writer, row.Cache);
        }
    }

    private static void WriteCandidates(CkptWriter writer, CortexPolicyCanonicalCandidateReplacement[] rows)
    {
        WriteCount(writer, rows); foreach (CortexPolicyCanonicalCandidateReplacement row in rows) { WriteCanonicalState(writer, row.State); writer.I32(row.Action); writer.U64(row.CandidateFingerprint); writer.U64(row.OccurrenceDigest); writer.U64(row.Revision.Value); writer.U64(row.OriginRevision.Value); writer.I32(row.InstalledStep); writer.I32(row.Comparisons); writer.I32(row.Agreements); writer.I32(row.Misses); }
    }

    private static CortexPolicyCanonicalCandidateReplacement[] ReadCandidates(CkptReader reader)
    {
        int count = ReadCount(reader); CortexPolicyCanonicalCandidateReplacement[] rows = new CortexPolicyCanonicalCandidateReplacement[count];
        for (int i = 0; i < count; i++) { PolicyCanonicalStateID state = ReadCanonicalState(reader); rows[i] = new(state, reader.I32(), reader.U64(), reader.U64(), new GrammarRevisionID(reader.U64()), new GrammarRevisionID(reader.U64()), reader.I32(), reader.I32(), reader.I32(), reader.I32()); }
        return rows;
    }

    private static void WriteCanonicalState(CkptWriter writer, PolicyCanonicalStateID state) { writer.Bool(state.Version != 0); if (state.Version != 0) { writer.Str(state.Policy.Value); writer.U8((byte)state.Kind); writer.U16(state.Version); writer.U64(state.Value); } }
    private static PolicyCanonicalStateID ReadCanonicalState(CkptReader reader) => reader.Bool() ? new(new CortexPolicyID(reader.Str()), (PolicyCanonicalStateKinds)reader.U8(), reader.U16(), reader.U64()) : default;

    private static void WriteCacheReplacement(CkptWriter writer, PolicyReadoutCacheReplacement replacement)
    {
        writer.U64(replacement.Revision.Value); writer.I64(replacement.UseClock); PolicyReadoutCacheEntryReplacement[] entries = replacement.Entries; WriteCount(writer, entries); foreach (PolicyReadoutCacheEntryReplacement row in entries) { GrammarPolicyContextKey context = row.Context; GrammarPolicyDecision decision = row.Decision; WriteContext(writer, context); WriteDecision(writer, decision); writer.U64(row.QuotaID.Value); writer.I64(row.LastUsed); } GrammarPolicyReadoutQuotaRecord[] quotaJournal = replacement.QuotaJournal; WriteCount(writer, quotaJournal); foreach (GrammarPolicyReadoutQuotaRecord row in quotaJournal) { writer.U64(row.QuotaID.Value); writer.Str(row.Policy.Value); writer.U64(row.Revision.Value); writer.I32(row.QuotaStep); GrammarPolicyContextKey context = row.Context; GrammarPolicyDecision decision = row.Decision; WriteContext(writer, context); WriteDecision(writer, decision); }
    }

    private static PolicyReadoutCacheReplacement ReadCacheReplacement(CkptReader reader)
    {
        GrammarRevisionID revision = new(reader.U64()); long clock = reader.I64(); int count = ReadCount(reader); PolicyReadoutCacheEntryReplacement[] entries = new PolicyReadoutCacheEntryReplacement[count]; for (int i = 0; i < count; i++) entries[i] = new(ReadContext(reader), ReadDecision(reader), new CortexPolicyQuotaDecisionID(reader.U64()), reader.I64()); int journalCount = ReadCount(reader); GrammarPolicyReadoutQuotaRecord[] quotaJournal = new GrammarPolicyReadoutQuotaRecord[journalCount]; for (int i = 0; i < journalCount; i++) quotaJournal[i] = new(new CortexPolicyQuotaDecisionID(reader.U64()), new CortexPolicyID(reader.Str()), new GrammarRevisionID(reader.U64()), reader.I32(), ReadContext(reader), ReadDecision(reader)); return new(revision, clock, entries, quotaJournal);
    }

    private static void WriteContext(CkptWriter writer, GrammarPolicyContextKey key) { writer.Bool(key.IsCanonical); writer.Bytes(key.Context.ToArray()); writer.I32(key.ActionCount); writer.I32(key.DeliberationDepth); }
    private static GrammarPolicyContextKey ReadContext(CkptReader reader) { bool canonical = reader.Bool(); byte[] bytes = reader.Bytes(PolicyReadoutCache.MaxContextBytes); int actions = reader.I32(); int depth = reader.I32(); if (canonical) { if (!PolicyCanonicalStateID.TryDecode(bytes, out PolicyCanonicalStateID state)) throw new InvalidDataException("invalid canonical policy context"); return new(state, actions, depth); } return new(bytes, actions, depth); }
    private static void WriteDecision(CkptWriter writer, GrammarPolicyDecision decision) { writer.I32(decision.Action); writer.I64(decision.LearnedWeight); writer.I32(decision.MatchingRecords); writer.U64(decision.Revision.Value); writer.I32(decision.Completion.Held); writer.I32(decision.Completion.Used); writer.I32(decision.Completion.Reclaimed); writer.I64(decision.Completion.ScannedBytes); writer.I64(decision.Completion.ExpandedEdges); writer.U64(decision.Fingerprint); writer.U64(decision.OccurrenceDigest); }
    private static GrammarPolicyDecision ReadDecision(CkptReader reader) => new(reader.I32(), reader.I64(), reader.I32(), new GrammarRevisionID(reader.U64()), new GrammarContinuationQuotaCompletion(reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64()), reader.U64()) { OccurrenceDigest = reader.U64() };

    private static void WriteAllocation(CkptWriter writer, CortexPolicyReadoutAllocationReplacement allocation) { writer.Bool(allocation.RosterSealed); writer.Str(allocation.RosterDigest); writer.I64(allocation.LastQuotaStep); writer.I32(allocation.AllocationCursor); writer.I64(allocation.AllocationSequence); WriteCount(writer, allocation.AllocationStates); foreach (CortexPolicyReadoutAllocationReceipt row in allocation.AllocationStates) { writer.Str(row.Policy.Value); writer.I64(row.AvailableUnits); writer.I64(row.AllocatedUnits); writer.I64(row.HeldUnits); writer.I64(row.UsedUnits); writer.I64(row.ReclaimedUnits); writer.I64(row.ExpiredUnits); writer.I64(row.LastAllocationSequence); } writer.I64(allocation.UsedUnits); writer.I64(allocation.HeldUnits); writer.I64(allocation.CompletedUsedUnits); }
    private static CortexPolicyReadoutAllocationReplacement ReadAllocation(CkptReader reader) { bool sealedRoster = reader.Bool(); string digest = reader.Str(); long last = reader.I64(); int cursor = reader.I32(); long seq = reader.I64(); int count = ReadCount(reader); CortexPolicyReadoutAllocationReceipt[] allocationStates = new CortexPolicyReadoutAllocationReceipt[count]; for (int i = 0; i < count; i++) allocationStates[i] = new(new CortexPolicyID(reader.Str()), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64()); return new(sealedRoster, digest, last, cursor, seq, allocationStates, reader.I64(), reader.I64(), reader.I64()); }

    private static void WriteCount(CkptWriter writer, Array? rows) { int count = rows?.Length ?? 0; if (count > MaxPolicyDeltaRows) throw new InvalidDataException("policy checkpoint delta row count exceeds bound"); writer.I32(count); }
    private static int ReadCount(CkptReader reader) { int count = reader.I32(); if (count < 0 || count > MaxPolicyDeltaRows) throw new InvalidDataException("policy checkpoint delta row count exceeds bound"); return count; }
}
