namespace Cogito;

using System.Buffers.Binary;

/// Versioned, policy-owned finite state used as the learner's identity.  Raw metric samples
/// remain audit evidence; they are deliberately not part of this atom.
public readonly struct PolicyCanonicalStateID : IEquatable<PolicyCanonicalStateID>, IComparable<PolicyCanonicalStateID>
{
    public PolicyCanonicalStateID(CortexPolicyID policy, PolicyCanonicalStateKinds kind, ushort version, ulong value)
    {
        if (policy.Value.Length == 0) throw new ArgumentException("canonical state policy cannot be empty", nameof(policy));
        if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
        Policy = policy;
        Kind = kind;
        Version = version;
        Value = value;
    }

    public CortexPolicyID Policy { get; }
    public PolicyCanonicalStateKinds Kind { get; }
    public ushort Version { get; }
    public ulong Value { get; }

    public bool Equals(PolicyCanonicalStateID other)
        => Policy.Equals(other.Policy) && Kind == other.Kind && Version == other.Version && Value == other.Value;
    public override bool Equals(object? obj) => obj is PolicyCanonicalStateID other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Policy, (byte)Kind, Version, Value);
    public int CompareTo(PolicyCanonicalStateID other)
    {
        int policy = Policy.CompareTo(other.Policy);
        if (policy != 0) return policy;
        int kind = ((byte)Kind).CompareTo((byte)other.Kind);
        if (kind != 0) return kind;
        int version = Version.CompareTo(other.Version);
        return version != 0 ? version : Value.CompareTo(other.Value);
    }

    public override string ToString() => $"{Policy.Value}:{Kind}:v{Version}:{Value:X16}";
    public bool IsValidFor(CortexPolicyID policy)
        => Version != 0 && Policy.Equals(policy) && Enum.IsDefined(Kind);

    internal byte[] Encode()
    {
        int policyLength = System.Text.Encoding.UTF8.GetByteCount(Policy.Value);
        if (policyLength > ushort.MaxValue) throw new InvalidDataException("canonical state policy exceeds the u16 byte ceiling");
        byte[] encoded = new byte[4 + 2 + policyLength + 1 + 2 + 8];
        encoded[0] = (byte)'P'; encoded[1] = (byte)'C'; encoded[2] = (byte)'S'; encoded[3] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(4, 2), checked((ushort)policyLength));
        int written = System.Text.Encoding.UTF8.GetBytes(Policy.Value, encoded.AsSpan(6));
        if (written != policyLength) throw new InvalidDataException("canonical state policy encoding length changed");
        int offset = 6 + policyLength;
        encoded[offset++] = (byte)Kind;
        BinaryPrimitives.WriteUInt16BigEndian(encoded.AsSpan(offset, 2), Version); offset += 2;
        BinaryPrimitives.WriteUInt64BigEndian(encoded.AsSpan(offset, 8), Value);
        return encoded;
    }

    internal static bool TryDecode(ReadOnlySpan<byte> encoded, out PolicyCanonicalStateID state)
    {
        state = default;
        if (encoded.Length < 4 + 2 + 1 + 2 + 8
            || encoded[0] != (byte)'P' || encoded[1] != (byte)'C' || encoded[2] != (byte)'S' || encoded[3] != 1)
            return false;
        ushort policyLength = BinaryPrimitives.ReadUInt16BigEndian(encoded.Slice(4, 2));
        int offset = 6;
        if (policyLength == 0 || encoded.Length != checked(offset + policyLength + 1 + 2 + 8)) return false;
        string policy = System.Text.Encoding.UTF8.GetString(encoded.Slice(offset, policyLength));
        offset += policyLength;
        PolicyCanonicalStateKinds kind = (PolicyCanonicalStateKinds)encoded[offset++];
        if (!Enum.IsDefined(kind)) return false;
        ushort version = BinaryPrimitives.ReadUInt16BigEndian(encoded.Slice(offset, 2)); offset += 2;
        if (version == 0) return false;
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(encoded.Slice(offset, 8));
        try { state = new PolicyCanonicalStateID(new CortexPolicyID(policy), kind, version, value); return true; }
        catch (ArgumentException) { return false; }
    }

    public static bool operator ==(PolicyCanonicalStateID left, PolicyCanonicalStateID right) => left.Equals(right);
    public static bool operator !=(PolicyCanonicalStateID left, PolicyCanonicalStateID right) => !left.Equals(right);
}

public enum PolicyCanonicalStateKinds : byte
{
    Generic,
    Homeostat,
    Rhythm,
    Energy,
}

/// Declares how a policy boundary proves its canonical state space. Enumerated
/// domains publish a finite catalog (Homeostat); dynamic domains have no safe
/// precomputed catalog and must validate every observed state at its authority
/// boundary (Repository); None is reserved for policy-neutral fixtures.
public enum PolicyCanonicalScopeModes : byte
{
    None,
    Enumerated,
    Dynamic,
}

/// Stable version of the policy's state encoder.  This is a schema identity, not a grammar
/// revision: changing the state partition invalidates its readout cache safely.
public readonly struct PolicyCanonicalizerVersion(ushort value) : IEquatable<PolicyCanonicalizerVersion>
{
    public ushort Value { get; } = value == 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    public bool Equals(PolicyCanonicalizerVersion other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is PolicyCanonicalizerVersion other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => $"v{Value}";
    public static implicit operator ushort(PolicyCanonicalizerVersion version) => version.Value;
}

/// Semantic identity of one learned action at one canonical state.  Grammar revisions and
/// continuation scan cost are provenance/accounting, never candidate identity.
public readonly struct PolicyCandidateIdentity : IEquatable<PolicyCandidateIdentity>, IComparable<PolicyCandidateIdentity>
{
    public PolicyCandidateIdentity(
        CortexPolicyID policy,
        in PolicyCanonicalStateID state,
        int action,
        PolicyCanonicalizerVersion canonicalizer)
    {
        if (action < 0) throw new ArgumentOutOfRangeException(nameof(action));
        if (!state.Policy.Equals(policy)) throw new ArgumentException("candidate state belongs to another policy", nameof(state));
        Policy = policy;
        State = state;
        Action = action;
        Canonicalizer = canonicalizer;
    }

    public CortexPolicyID Policy { get; }
    public PolicyCanonicalStateID State { get; }
    public int Action { get; }
    public PolicyCanonicalizerVersion Canonicalizer { get; }

    public bool Equals(PolicyCandidateIdentity other)
        => Policy.Equals(other.Policy) && State.Equals(other.State) && Action == other.Action
            && Canonicalizer.Equals(other.Canonicalizer);
    public override bool Equals(object? obj) => obj is PolicyCandidateIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Policy, State, Action, Canonicalizer);
    public int CompareTo(PolicyCandidateIdentity other)
    {
        int policy = Policy.CompareTo(other.Policy);
        if (policy != 0) return policy;
        int state = State.CompareTo(other.State);
        if (state != 0) return state;
        int action = Action.CompareTo(other.Action);
        if (action != 0) return action;
        return Canonicalizer.Value.CompareTo(other.Canonicalizer.Value);
    }
}

/// Per-state verified authority. The active program, candidate, support, and
/// publication revision are one custody tuple; any drift closes this scope.
internal readonly record struct PolicyVerifiedScopeEntry(
    PolicyCanonicalStateID State,
    ulong ReadoutFingerprint,
    ulong CandidateFingerprint,
    ulong OccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID Revision)
{
    internal bool IsValid => State.Version != 0 && ReadoutFingerprint != 0
        && CandidateFingerprint != 0 && OccurrenceDigest != 0
        && Revision != global::Cogito.Grammar.GrammarRevisionID.Zero;
}

/// A completed trial is historical evidence once publication advances.  The
/// active trial fields are intentionally cleared so a new publication cannot
/// execute on stale authority; this tuple keeps the completed action and the
/// exact canonical scope that admitted it available for resume and terminal
/// reporting.
internal readonly record struct PolicyTrialExecutionHistory(
    CortexPolicyQuotaDecisionID QuotaDecisionID,
    CortexPolicySelectionCauses Cause,
    CortexPolicyTrialExecutionOutcomes Outcome,
    long RequestCount,
    long GuardAdmittedCount,
    CortexPolicyDecisionID LastRequestDecisionID,
    CortexPolicyDecisionReadout LastRequestReadout,
    int LastRequestStep,
    CortexPolicyDecisionID ExecutionDecisionID,
    CortexPolicyDecisionReadout ExecutionReadout,
    int ExecutionStep,
    ulong ExecutionReadoutFingerprint,
    PolicyVerifiedScopeEntry Scope)
{
    internal bool IsPresent => ExecutionDecisionID.Value != 0;

    internal void Validate(CortexPolicyID policy, int actionCount)
    {
        if (!IsPresent)
        {
            if (QuotaDecisionID.Value != 0 || Cause != CortexPolicySelectionCauses.Launchpad
                || Outcome != CortexPolicyTrialExecutionOutcomes.NotAttempted
                || RequestCount != 0 || GuardAdmittedCount != 0
                || LastRequestDecisionID.Value != 0 || LastRequestStep is not (0 or -1)
                || LastRequestReadout != default || ExecutionReadout != default
                || ExecutionReadoutFingerprint != 0 || ExecutionStep is not (0 or -1)
                || Scope != default)
                throw new InvalidDataException("policy trial execution history has partial absence");
            return;
        }
        bool launchpad = Cause == CortexPolicySelectionCauses.Launchpad;
        bool shadowCandidate = Cause == CortexPolicySelectionCauses.ShadowCandidate;
        bool configuredCause = Cause is CortexPolicySelectionCauses.Launchpad
            or CortexPolicySelectionCauses.ShadowCandidate
            or CortexPolicySelectionCauses.GrammarCandidate
            or CortexPolicySelectionCauses.TrialOverride;
        bool accountingValid = launchpad
            ? RequestCount == 0 && GuardAdmittedCount == 0
                && LastRequestDecisionID.Value == 0 && LastRequestStep is 0 or -1
                && LastRequestReadout == default
            : RequestCount > 0 && GuardAdmittedCount >= 0 && GuardAdmittedCount <= RequestCount
                && (shadowCandidate || GuardAdmittedCount > 0)
                && LastRequestDecisionID.Value != 0 && LastRequestStep >= 0
                && LastRequestReadout.SelectionCause == Cause;
        if (!configuredCause
            || QuotaDecisionID.Value == 0
            || Outcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            || !accountingValid
            || ExecutionStep < 0 || ExecutionReadoutFingerprint == 0
            || !ExecutionReadout.SelectionCause.Equals(Cause))
            throw new InvalidDataException("policy trial execution history accounting is invalid");
        if (ExecutionReadout.GrammarRevision == global::Cogito.Grammar.GrammarRevisionID.Zero
            || (launchpad
                ? ExecutionReadout.ReadoutCandidateFingerprint != 0
                    || ExecutionReadout.ReadoutCandidateOccurrenceDigest != 0
                : ExecutionReadout.ReadoutCandidateFingerprint == 0))
            throw new InvalidDataException("policy trial execution history omits execution identity");
        ExecutionReadout.Validate(actionCount);
        if (!launchpad) LastRequestReadout.Validate(actionCount);
        if (!Scope.IsValid)
            throw new InvalidDataException("policy trial execution history omits its immutable canonical scope");
        if (!Scope.State.IsValidFor(policy)
            || Scope.ReadoutFingerprint != ExecutionReadoutFingerprint
            || Scope.Revision != ExecutionReadout.GrammarRevision
            || !launchpad && (Scope.CandidateFingerprint != ExecutionReadout.ReadoutCandidateFingerprint
                || Scope.OccurrenceDigest != ExecutionReadout.ReadoutCandidateOccurrenceDigest))
            throw new InvalidDataException("policy trial execution history scope disagrees with its witness");
    }
}

public static class PolicySupportDigest
{
    /// Hashes the complete action-support vector.  The caller supplies stable weights and
    /// counts; revision, scan bytes, and funding counters are intentionally absent.
    public static ulong Compute(ReadOnlySpan<long> weights, ReadOnlySpan<int> counts, long matchingRecords)
    {
        if (weights.Length == 0 || weights.Length != counts.Length || matchingRecords < 0)
            throw new ArgumentException("action support vectors must be non-empty and have equal length");
        ulong hash = 14695981039346656037UL;
        Mix(unchecked((ulong)matchingRecords));
        Mix(unchecked((ulong)weights.Length));
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] < 0 || counts[i] < 0) throw new ArgumentOutOfRangeException(nameof(weights));
            Mix(unchecked((ulong)weights[i]));
            Mix(unchecked((ulong)counts[i]));
        }
        return hash == 0 ? 1 : hash;

        void Mix(ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
    }
}

public enum PolicyCanonicalCoverageAttributions : byte
{
    NoRequiredDomain,
    CoverageStarvation,
    CompleteCoverage,
}

/// Durable custody for one canonical program slot.  A missing slot is represented
/// explicitly rather than as a raw -1 action so a verifier can distinguish absent
/// evidence from a candidate that failed comparison.
public readonly record struct PolicyCanonicalCoverageEntry(
    PolicyCanonicalStateID State,
    bool Covered,
    int Action,
    ulong CandidateFingerprint,
    ulong OccurrenceDigest,
    global::Cogito.Grammar.GrammarRevisionID Revision,
    global::Cogito.Grammar.GrammarRevisionID OriginRevision,
    int InstalledStep,
    int Comparisons,
    int Agreements,
    int Misses)
{
    public bool HasCandidate => Covered;
}

/// Typed coverage receipt for a policy's canonical program.  `Entries` contains every
/// required state in canonical order, including missing states, and therefore survives
/// publication churn and resume without reconstructing absence from counters.
public readonly record struct PolicyCanonicalCoverageReceipt(
    int RequiredStateCount,
    int CoveredStateCount,
    int MissingStateCount,
    ulong RequiredStatesDigest,
    ulong CoveredStatesDigest,
    ulong MissingStatesDigest,
    PolicyCanonicalCoverageAttributions Attribution,
    PolicyCanonicalCoverageEntry[] Entries,
    int VerifierComparisons = 0,
    int VerifierAgreements = 0,
    int VerifierMisses = 0)
{
    public bool IsComplete => RequiredStateCount > 0 && MissingStateCount == 0;
    public bool IsStarved => Attribution == PolicyCanonicalCoverageAttributions.CoverageStarvation;

    // Coverage entries are checkpoint material, so record equality must compare
    // their values rather than the backing array identities produced by a load.
    public bool Equals(PolicyCanonicalCoverageReceipt other)
        => RequiredStateCount == other.RequiredStateCount
        && CoveredStateCount == other.CoveredStateCount
        && MissingStateCount == other.MissingStateCount
        && RequiredStatesDigest == other.RequiredStatesDigest
        && CoveredStatesDigest == other.CoveredStatesDigest
        && MissingStatesDigest == other.MissingStatesDigest
        && Attribution == other.Attribution
        && VerifierComparisons == other.VerifierComparisons
        && VerifierAgreements == other.VerifierAgreements
        && VerifierMisses == other.VerifierMisses
        && (Entries is null ? other.Entries is null : other.Entries is not null && Entries.AsSpan().SequenceEqual(other.Entries));

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(RequiredStateCount);
        hash.Add(CoveredStateCount);
        hash.Add(MissingStateCount);
        hash.Add(RequiredStatesDigest);
        hash.Add(CoveredStatesDigest);
        hash.Add(MissingStatesDigest);
        hash.Add(Attribution);
        hash.Add(VerifierComparisons);
        hash.Add(VerifierAgreements);
        hash.Add(VerifierMisses);
        if (Entries is not null)
            for (int i = 0; i < Entries.Length; i++) hash.Add(Entries[i]);
        return hash.ToHashCode();
    }

    internal static PolicyCanonicalCoverageReceipt Create(
        ReadOnlySpan<PolicyCanonicalStateID> requiredStates,
        IReadOnlyDictionary<PolicyCanonicalStateID, PolicyCanonicalCoverageEntry> candidates,
        int verifierComparisons = 0,
        int verifierAgreements = 0,
        int verifierMisses = 0)
    {
        PolicyCanonicalCoverageEntry[] entries = new PolicyCanonicalCoverageEntry[requiredStates.Length];
        int covered = 0;
        ulong requiredDigest = 14695981039346656037UL;
        ulong coveredDigest = 14695981039346656037UL;
        ulong missingDigest = 14695981039346656037UL;
        for (int i = 0; i < requiredStates.Length; i++)
        {
            PolicyCanonicalStateID state = requiredStates[i];
            MixState(ref requiredDigest, state);
            if (candidates.TryGetValue(state, out PolicyCanonicalCoverageEntry candidate) && candidate.HasCandidate)
            {
                entries[i] = candidate;
                covered++;
                MixState(ref coveredDigest, state);
                MixValue(ref coveredDigest, unchecked((ulong)candidate.Action));
                MixValue(ref coveredDigest, candidate.CandidateFingerprint);
                MixValue(ref coveredDigest, candidate.OccurrenceDigest);
                MixValue(ref coveredDigest, candidate.Revision.Value);
                MixValue(ref coveredDigest, candidate.OriginRevision.Value);
                MixValue(ref coveredDigest, unchecked((ulong)candidate.InstalledStep));
                MixValue(ref coveredDigest, unchecked((ulong)candidate.Comparisons));
                MixValue(ref coveredDigest, unchecked((ulong)candidate.Agreements));
                MixValue(ref coveredDigest, unchecked((ulong)candidate.Misses));
            }
            else
            {
                entries[i] = new(state, false, -1, 0, 0, global::Cogito.Grammar.GrammarRevisionID.Zero, global::Cogito.Grammar.GrammarRevisionID.Zero, 0, 0, 0, 0);
                MixState(ref missingDigest, state);
            }
        }
        int missing = requiredStates.Length - covered;
        PolicyCanonicalCoverageAttributions attribution = requiredStates.Length == 0
            ? PolicyCanonicalCoverageAttributions.NoRequiredDomain
            : missing == 0
                ? PolicyCanonicalCoverageAttributions.CompleteCoverage
                : PolicyCanonicalCoverageAttributions.CoverageStarvation;
        return new(
            requiredStates.Length,
            covered,
            missing,
            requiredStates.Length == 0 ? 0 : Normalize(requiredDigest),
            covered == 0 ? 0 : Normalize(coveredDigest),
            missing == 0 ? 0 : Normalize(missingDigest),
            attribution,
            entries,
            verifierComparisons,
            verifierAgreements,
            verifierMisses);

        static void MixState(ref ulong hash, PolicyCanonicalStateID state)
        {
            MixValue(ref hash, (ulong)(byte)state.Kind);
            MixValue(ref hash, (ulong)state.Version);
            MixValue(ref hash, state.Value);
        }

        static void MixValue(ref ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        static ulong Normalize(ulong hash) => hash == 0 ? 1 : hash;
    }

    internal PolicyCanonicalCoverageReceipt WithVerifier(int comparisons, int agreements, int misses)
        => this with { VerifierComparisons = comparisons, VerifierAgreements = agreements, VerifierMisses = misses };

    internal void Validate()
    {
        if (RequiredStateCount < 0 || CoveredStateCount < 0 || MissingStateCount < 0
            || CoveredStateCount + MissingStateCount != RequiredStateCount
            || Entries is null || Entries.Length != RequiredStateCount
            || VerifierComparisons < 0 || VerifierAgreements < 0 || VerifierMisses < 0
            || VerifierAgreements > VerifierComparisons || VerifierMisses > VerifierComparisons)
            throw new InvalidDataException("canonical coverage receipt counts are malformed");
        Dictionary<PolicyCanonicalStateID, PolicyCanonicalCoverageEntry> candidates = new(Entries.Length);
        PolicyCanonicalStateID[] states = new PolicyCanonicalStateID[Entries.Length];
        int covered = 0;
        for (int i = 0; i < Entries.Length; i++)
        {
            PolicyCanonicalCoverageEntry entry = Entries[i];
            if (i > 0 && entry.State.CompareTo(Entries[i - 1].State) <= 0)
                throw new InvalidDataException("canonical coverage states are not strictly ordered");
            if (!candidates.TryAdd(entry.State, entry))
                throw new InvalidDataException("canonical coverage repeats a state");
            states[i] = entry.State;
            if (entry.Covered)
            {
                covered++;
                if (entry.Action < 0 || entry.CandidateFingerprint == 0 || entry.OccurrenceDigest == 0
                    || entry.Revision == global::Cogito.Grammar.GrammarRevisionID.Zero
                    || entry.OriginRevision == global::Cogito.Grammar.GrammarRevisionID.Zero
                    || entry.InstalledStep < 0
                    || entry.Comparisons < 0 || entry.Agreements < 0 || entry.Misses < 0
                    || entry.Agreements > entry.Comparisons || entry.Misses > entry.Comparisons)
                    throw new InvalidDataException("canonical coverage candidate custody is malformed");
            }
            else if (entry.Action != -1 || entry.CandidateFingerprint != 0 || entry.OccurrenceDigest != 0
                || entry.Revision != global::Cogito.Grammar.GrammarRevisionID.Zero
                || entry.OriginRevision != global::Cogito.Grammar.GrammarRevisionID.Zero
                || entry.InstalledStep != 0
                || entry.Comparisons != 0 || entry.Agreements != 0 || entry.Misses != 0)
                throw new InvalidDataException("canonical coverage missing state carries candidate evidence");
        }
        PolicyCanonicalCoverageReceipt recomputed = Create(
            states, candidates, VerifierComparisons, VerifierAgreements, VerifierMisses);
        if (covered != CoveredStateCount
            || recomputed.RequiredStatesDigest != RequiredStatesDigest
            || recomputed.MissingStateCount != MissingStateCount
            || recomputed.CoveredStatesDigest != CoveredStatesDigest
            || recomputed.MissingStatesDigest != MissingStatesDigest
            || recomputed.Attribution != Attribution)
            throw new InvalidDataException("canonical coverage receipt digest or attribution differs from its entries");
    }
}

/// Incremental index for the finite canonical coverage domain.  Candidate custody is
/// updated at the mutation site; reading a receipt copies only the fixed required domain
/// instead of rebuilding it from every learned candidate.
internal sealed class PolicyCanonicalCoverageIndex
{
    private PolicyCanonicalStateID[] _requiredStates = Array.Empty<PolicyCanonicalStateID>();
    private Dictionary<PolicyCanonicalStateID, int> _slots = new();
    private PolicyCanonicalCoverageEntry[] _entries = Array.Empty<PolicyCanonicalCoverageEntry>();
    private int _coveredCount;
    private bool _initialized;

    internal bool Matches(ReadOnlySpan<PolicyCanonicalStateID> requiredStates)
        => _initialized && requiredStates.SequenceEqual(_requiredStates);

    internal void Ensure(
        ReadOnlySpan<PolicyCanonicalStateID> requiredStates,
        IReadOnlyDictionary<PolicyCanonicalStateID, PolicyCanonicalCoverageEntry> candidates)
    {
        if (_initialized && requiredStates.SequenceEqual(_requiredStates)) return;
        _requiredStates = requiredStates.ToArray();
        _slots = new Dictionary<PolicyCanonicalStateID, int>(_requiredStates.Length);
        _entries = new PolicyCanonicalCoverageEntry[_requiredStates.Length];
        _coveredCount = 0;
        for (int index = 0; index < _requiredStates.Length; index++)
        {
            PolicyCanonicalStateID state = _requiredStates[index];
            _slots.Add(state, index);
            if (candidates.TryGetValue(state, out PolicyCanonicalCoverageEntry candidate) && candidate.Covered)
            {
                _entries[index] = candidate;
                _coveredCount++;
            }
            else
            {
                _entries[index] = Missing(state);
            }
        }
        _initialized = true;
    }

    internal void Upsert(in PolicyCanonicalCoverageEntry entry)
    {
        if (!_initialized || !_slots.TryGetValue(entry.State, out int index)) return;
        if (!_entries[index].Covered && entry.Covered) _coveredCount++;
        else if (_entries[index].Covered && !entry.Covered) _coveredCount--;
        _entries[index] = entry.Covered ? entry : Missing(entry.State);
    }

    internal void Remove(in PolicyCanonicalStateID state)
    {
        if (!_initialized || !_slots.TryGetValue(state, out int index)) return;
        if (_entries[index].Covered) _coveredCount--;
        _entries[index] = Missing(state);
    }

    internal PolicyCanonicalCoverageReceipt Create(int verifierComparisons, int verifierAgreements, int verifierMisses)
    {
        if (!_initialized) throw new InvalidOperationException("canonical coverage index is not initialized");
        Dictionary<PolicyCanonicalStateID, PolicyCanonicalCoverageEntry> candidates = new(_coveredCount);
        for (int index = 0; index < _entries.Length; index++)
            if (_entries[index].Covered) candidates.Add(_entries[index].State, _entries[index]);
        return PolicyCanonicalCoverageReceipt.Create(
            _requiredStates, candidates, verifierComparisons, verifierAgreements, verifierMisses);
    }

    internal static PolicyCanonicalCoverageEntry Missing(in PolicyCanonicalStateID state)
        => new(state, false, -1, 0, 0,
            global::Cogito.Grammar.GrammarRevisionID.Zero,
            global::Cogito.Grammar.GrammarRevisionID.Zero, 0, 0, 0, 0);
}

public static class PolicyCanonicalStates
{
    public const ushort HomeostatVersion = 1;
    public const ushort RhythmVersion = 1;
    public const ushort EnergyVersion = 1;

    /// The finite Homeostat state partition exercised by the shared-policy verifier.
    /// The verifier varies the auxiliary actuation dimensions independently, but those
    /// dimensions are deliberately not part of the canonical learner identity.  Keeping
    /// this domain here makes that distinction explicit and gives coverage a checkable
    /// required set instead of inferring it from whichever state happened to be observed.
    public static PolicyCanonicalStateID[] HomeostatDomain(CortexPolicyID policy)
    {
        PolicyCanonicalStateID[] states = new PolicyCanonicalStateID[9 * 2 * 2];
        int index = 0;
        for (int condition = 0; condition < 9; condition++)
            for (int wasted = 0; wasted < 2; wasted++)
                for (int growth = 0; growth < 2; growth++)
                {
                    HomeostatPolicyContext context = new(
                        (HomeostatPolicyConditions)condition,
                        wasted != 0,
                        growth != 0);
                    states[index++] = Homeostat(policy, in context);
                }
        Array.Sort(states);
        return states;
    }

    public static PolicyCanonicalStateID Homeostat(CortexPolicyID policy, in HomeostatPolicyContext context)
    {
        ulong value = (ulong)context.Condition
            | (context.PreviousConsolidationPhaseWasted ? 1UL << 8 : 0)
            | (context.GrowthAboveMintParity ? 1UL << 9 : 0);
        return new(policy, PolicyCanonicalStateKinds.Homeostat, HomeostatVersion, value);
    }

    public static PolicyCanonicalStateID Rhythm(
        CortexPolicyID policy,
        bool aestivationDue,
        bool headroomBound,
        bool worthAtLeastReplayLine,
        bool epsilonEligible,
        bool freshFrontier,
        bool epsilonFired)
    {
        ulong value = (aestivationDue ? 1UL : 0)
            | (headroomBound ? 1UL << 1 : 0)
            | (worthAtLeastReplayLine ? 1UL << 2 : 0)
            | (epsilonEligible ? 1UL << 3 : 0)
            | (freshFrontier ? 1UL << 4 : 0)
            | (epsilonFired ? 1UL << 5 : 0);
        return new(policy, PolicyCanonicalStateKinds.Rhythm, RhythmVersion, value);
    }

    public static PolicyCanonicalStateID Energy(
        CortexPolicyID policy,
        bool collapse,
        bool plateau,
        bool grokked,
        bool momentumClimbing,
        bool adaptive,
        bool increaseNovelty,
        bool increaseDepth,
        bool coolNoise)
    {
        ulong value = (collapse ? 1UL : 0)
            | (plateau ? 1UL << 1 : 0)
            | (grokked ? 1UL << 2 : 0)
            | (momentumClimbing ? 1UL << 3 : 0)
            | (adaptive ? 1UL << 4 : 0)
            | (increaseNovelty ? 1UL << 5 : 0)
            | (increaseDepth ? 1UL << 6 : 0)
            | (coolNoise ? 1UL << 7 : 0);
        return new(policy, PolicyCanonicalStateKinds.Energy, EnergyVersion, value);
    }
}
