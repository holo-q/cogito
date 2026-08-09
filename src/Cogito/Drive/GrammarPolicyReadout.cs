namespace Cogito;

using Cogito.Grammar;

internal readonly record struct GrammarPolicyDecision(
    int Action,
    long LearnedWeight,
    int MatchingRecords,
    GrammarRevisionID Revision,
    GrammarContinuationQuotaCompletion Completion,
    ulong Fingerprint)
{
    /// Revision-independent action support identity.  The installRevision revision remains
    /// provenance on the decision, never part of this semantic candidate.
    public ulong OccurrenceDigest { get; init; }
}

internal readonly record struct PolicyReadoutCacheEntry(
    GrammarPolicyContextKey Context,
    GrammarPolicyDecision Decision,
    CortexPolicyQuotaDecisionID QuotaID);

internal readonly record struct PolicyReadoutCacheEntryReplacement(
    GrammarPolicyContextKey Context,
    GrammarPolicyDecision Decision,
    CortexPolicyQuotaDecisionID QuotaID,
    long LastUsed);

internal readonly record struct PolicyReadoutCacheReplacement(
    GrammarRevisionID Revision,
    long UseClock,
    PolicyReadoutCacheEntryReplacement[] Entries,
    GrammarPolicyReadoutQuotaRecord[] QuotaJournal);

internal readonly record struct GrammarPolicyReadoutQuotaRecord(
    CortexPolicyQuotaDecisionID QuotaID,
    CortexPolicyID Policy,
    GrammarRevisionID Revision,
    int QuotaStep,
    GrammarPolicyContextKey Context,
    GrammarPolicyDecision Decision);

internal enum PolicyReadoutCacheOutcomes : byte
{
    Miss,
    Hit,
    Refilled,
}

internal readonly record struct PolicyReadoutCacheReceipt(
    PolicyReadoutCacheOutcomes Outcome,
    GrammarPolicyContextKey Context,
    GrammarPolicyDecision Decision)
{
    public bool HasDecision => Outcome is PolicyReadoutCacheOutcomes.Hit or PolicyReadoutCacheOutcomes.Refilled;
}

internal readonly struct GrammarPolicyContextKey : IEquatable<GrammarPolicyContextKey>, IComparable<GrammarPolicyContextKey>
{
    private readonly byte[]? _context;
    private readonly PolicyCanonicalStateID _canonicalState;
    private readonly int _actionCount;
    private readonly int _hashCode;

    public GrammarPolicyContextKey(ReadOnlySpan<byte> context, int deliberationDepth)
        : this(context, 0, deliberationDepth) { }

    public GrammarPolicyContextKey(ReadOnlySpan<byte> context, int actionCount, int deliberationDepth)
    {
        if (context.Length > PolicyReadoutCache.MaxContextBytes)
            throw new ArgumentException($"policy readout context exceeds {PolicyReadoutCache.MaxContextBytes} bytes", nameof(context));
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        _context = context.ToArray();
        _canonicalState = PolicyCanonicalStateID.TryDecode(context, out PolicyCanonicalStateID canonical)
            ? canonical
            : default;
        _actionCount = actionCount;
        DeliberationDepth = deliberationDepth >= 0 ? deliberationDepth : throw new ArgumentOutOfRangeException(nameof(deliberationDepth));
        _hashCode = ComputeHashCode();
    }

    private GrammarPolicyContextKey(byte[] context, int actionCount, int deliberationDepth)
    {
        if (context.Length > PolicyReadoutCache.MaxContextBytes)
            throw new ArgumentException($"policy readout context exceeds {PolicyReadoutCache.MaxContextBytes} bytes", nameof(context));
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        _context = context;
        _canonicalState = PolicyCanonicalStateID.TryDecode(context, out PolicyCanonicalStateID canonical)
            ? canonical
            : default;
        _actionCount = actionCount;
        DeliberationDepth = deliberationDepth >= 0 ? deliberationDepth : throw new ArgumentOutOfRangeException(nameof(deliberationDepth));
        _hashCode = ComputeHashCode();
    }

    internal static GrammarPolicyContextKey TakeOwnership(byte[] context, int actionCount, int deliberationDepth)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new GrammarPolicyContextKey(context, actionCount, deliberationDepth);
    }

    public GrammarPolicyContextKey(in PolicyCanonicalStateID canonicalState, int actionCount, int deliberationDepth)
    {
        if (actionCount <= 1) throw new ArgumentOutOfRangeException(nameof(actionCount));
        _canonicalState = canonicalState;
        _context = null;
        _actionCount = actionCount;
        DeliberationDepth = deliberationDepth >= 0 ? deliberationDepth : throw new ArgumentOutOfRangeException(nameof(deliberationDepth));
        _hashCode = ComputeHashCode();
    }

    public ReadOnlySpan<byte> Context => _context ?? (_canonicalState.Version == 0 ? [] : _canonicalState.Encode());
    public bool IsCanonical => _canonicalState.Version != 0;
    public PolicyCanonicalStateID CanonicalState => IsCanonical
        ? _canonicalState
        : throw new InvalidOperationException("raw policy context has no canonical state");
    public int ActionCount => _actionCount;
    public int DeliberationDepth { get; }
    public ulong ContextDigest => ComputeDigest(Context, DeliberationDepth);

    internal static ulong ComputeDigest(ReadOnlySpan<byte> context, int deliberationDepth)
    {
        ulong hash = 14695981039346656037UL ^ (ulong)deliberationDepth;
        for (int index = 0; index < context.Length; index++) hash = (hash ^ context[index]) * 1099511628211UL;
        return hash == 0 ? 1 : hash;
    }

    public bool Equals(GrammarPolicyContextKey other)
        => DeliberationDepth == other.DeliberationDepth && ActionCount == other.ActionCount && IsCanonical == other.IsCanonical
            && (IsCanonical ? _canonicalState.Equals(other._canonicalState) : Context.SequenceEqual(other.Context));

    public override bool Equals(object? obj) => obj is GrammarPolicyContextKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private int ComputeHashCode()
    {
        HashCode hash = new();
        hash.Add(DeliberationDepth);
        hash.Add(ActionCount);
        hash.Add(IsCanonical);
        if (IsCanonical)
        {
            hash.Add(_canonicalState);
            return hash.ToHashCode();
        }
        ReadOnlySpan<byte> context = Context;
        for (int index = 0; index < context.Length; index++) hash.Add(context[index]);
        return hash.ToHashCode();
    }

    public int CompareTo(GrammarPolicyContextKey other)
    {
        int canonical = IsCanonical.CompareTo(other.IsCanonical);
        if (canonical != 0) return canonical;
        int actionCount = ActionCount.CompareTo(other.ActionCount);
        if (actionCount != 0) return actionCount;
        int order = Context.SequenceCompareTo(other.Context);
        return order != 0 ? order : DeliberationDepth.CompareTo(other.DeliberationDepth);
    }
}

internal sealed class PolicyReadoutCache
{
    // The readout is a bounded working-set cache, not a journal of every live F64 vector. Four
    // thousand contexts leaves ample headroom over the current policy set while making admission
    // and checkpoint allocation finite; LRU eviction is deterministic when the cap is reached.
    internal const int MaxEntries = 4096;
    internal const int MaxContextBytes = 64 * 1024;
    internal const int MaxFundingReservation = 1_000_000;

    private readonly Dictionary<GrammarPolicyContextKey, CacheValue> _entries;
    private readonly Dictionary<CortexPolicyQuotaDecisionID, GrammarPolicyReadoutQuotaRecord> _fundingJournal = new();
    private readonly LinkedList<GrammarPolicyContextKey> _lru = new();
    private long _useClock;
    private long _generation;
    private bool _needsSweep;

    private sealed class CacheValue
    {
        public CacheValue(
            GrammarPolicyDecision decision,
            CortexPolicyQuotaDecisionID quotaID,
            long lastUsed,
            long generation,
            LinkedListNode<GrammarPolicyContextKey> node)
        {
            Decision = decision;
            QuotaID = quotaID;
            LastUsed = lastUsed;
            Generation = generation;
            Node = node;
        }

        public GrammarPolicyDecision Decision;
        public CortexPolicyQuotaDecisionID QuotaID;
        public long LastUsed;
        public long Generation;
        public LinkedListNode<GrammarPolicyContextKey> Node;
    }

    public PolicyReadoutCache(IEqualityComparer<GrammarPolicyContextKey>? comparer = null)
        => _entries = new Dictionary<GrammarPolicyContextKey, CacheValue>(comparer);

    public GrammarRevisionID Revision { get; private set; }
    public int Count => _entries.Count;
    internal int FundingCount => _fundingJournal.Count;
    internal int SweepCount { get; private set; }
    internal long SweptEntries { get; private set; }
    internal long CanonicalComputations { get; private set; }

    public bool TryGet(GrammarRevisionID revision, in GrammarPolicyContextKey context, out GrammarPolicyDecision decision)
    {
        MoveToRevision(revision);
        if (!_entries.TryGetValue(context, out CacheValue? value))
        {
            decision = default;
            return false;
        }
        if (value.Decision.Revision != revision)
        {
            // Canonical entries are semantic evidence for a state/action candidate. Keep the
            // prior installRevision binding until a funded refill can revalidate that identity at
            // the new revision; raw feature contexts have no revision-independent meaning.
            if (!context.IsCanonical) RemoveEntry(in context, value);
            decision = default;
            return false;
        }
        if (value.Generation != _generation)
        {
            if (!context.IsCanonical) RemoveEntry(in context, value);
            decision = default;
            return false;
        }
        value.LastUsed = checked(++_useClock);
        _lru.Remove(value.Node);
        _lru.AddFirst(value.Node);
        decision = value.Decision;
        return true;
    }

    public void Store(CortexPolicyID policy, GrammarRevisionID revision, in GrammarPolicyContextKey context, in GrammarPolicyDecision decision)
    {
        if (revision == GrammarRevisionID.Zero)
            throw new InvalidOperationException("policy readouts require a published grammar revision");
        if (context.Context.IsEmpty)
            throw new InvalidOperationException("policy readout context cannot be empty");
        if (context.Context.Length > MaxContextBytes)
            throw new InvalidOperationException($"policy readout context exceeds {MaxContextBytes} bytes");
        if (decision.Revision != revision)
            throw new InvalidOperationException("policy readout revision differs from its cache stamp");
        MoveToRevision(revision);
        CortexPolicyQuotaDecisionID quotaID = GrammarPolicyReadout.ComputeQuotaID(policy, revision, 0, in context, in decision);
        AddFundingRecord(policy, revision, 0, in context, in decision, quotaID);
        StoreCore(revision, in context, in decision, quotaID, checked(++_useClock));
        // Entries produced by ComputeCanonicalContext are already checked against the owning
        // installRevision. A full sweep belongs to the untrusted Load boundary; marking every store
        // dirty would make a growing working set pay O(total entries) after each admission.
    }

    internal void StoreBound(
        CortexPolicyID policy,
        GrammarRevisionID revision,
        in GrammarPolicyContextKey context,
        in GrammarPolicyDecision decision,
        CortexPolicyQuotaDecisionID quotaID,
        int quotaStep = 0)
    {
        if (revision == GrammarRevisionID.Zero)
            throw new InvalidOperationException("policy readouts require a published grammar revision");
        if (context.Context.IsEmpty || context.Context.Length > MaxContextBytes)
            throw new InvalidOperationException("policy readout context is invalid");
        if (decision.Revision != revision)
            throw new InvalidOperationException("policy readout revision differs from its cache stamp");
        MoveToRevision(revision);
        AddFundingRecord(policy, revision, quotaStep, in context, in decision, quotaID);
        StoreCore(revision, in context, in decision, quotaID, checked(++_useClock));
    }

    private void AddFundingRecord(CortexPolicyID policy, GrammarRevisionID revision, int quotaStep, in GrammarPolicyContextKey context, in GrammarPolicyDecision decision, CortexPolicyQuotaDecisionID quotaID)
    {
        if (quotaID.Value == 0) throw new InvalidDataException("grammar policy readout quota identity cannot be zero");
        GrammarPolicyReadoutQuotaRecord record = new(quotaID, policy, revision, quotaStep, context, decision);
        if (_fundingJournal.TryGetValue(quotaID, out GrammarPolicyReadoutQuotaRecord prior) && prior != record)
            throw new InvalidDataException("grammar policy readout quota identity collides with a different record");
        _fundingJournal[quotaID] = record;
    }

    private void StoreCore(
        GrammarRevisionID revision,
        in GrammarPolicyContextKey context,
        in GrammarPolicyDecision decision,
        CortexPolicyQuotaDecisionID completionBinding,
        long useClock)
    {
        if (completionBinding.Value == 0) throw new InvalidOperationException("policy readout completion binding cannot be zero");
        if (_entries.TryGetValue(context, out CacheValue? prior) && !prior!.QuotaID.Equals(completionBinding))
            _fundingJournal.Remove(prior.QuotaID);
        if (prior is null && _entries.Count >= MaxEntries)
        {
            LinkedListNode<GrammarPolicyContextKey>? node = _lru.Last;
            if (node is not null && _entries.Remove(node.Value, out CacheValue? evicted) && evicted is not null)
            {
                _lru.Remove(node);
                _fundingJournal.Remove(evicted.QuotaID);
            }
        }
        if (prior is not null)
        {
            prior.Decision = decision;
            prior.QuotaID = completionBinding;
            prior.LastUsed = useClock;
            prior.Generation = _generation;
            _lru.Remove(prior.Node);
            _lru.AddFirst(prior.Node);
            return;
        }
        LinkedListNode<GrammarPolicyContextKey> newNode = _lru.AddFirst(context);
        _entries.Add(context, new CacheValue(decision, completionBinding, useClock, _generation, newNode));
    }

    public void Save(CkptWriter writer)
    {
        writer.U64(Revision.Value);
        writer.I64(_useClock);
        List<GrammarPolicyReadoutQuotaRecord> journal = [.. _fundingJournal.Values];
        journal.Sort(static (left, right) => left.QuotaID.Value.CompareTo(right.QuotaID.Value));
        writer.I32(journal.Count);
        for (int index = 0; index < journal.Count; index++)
        {
            GrammarPolicyReadoutQuotaRecord record = journal[index];
            writer.U64(record.QuotaID.Value);
            writer.Str(record.Policy.Value);
            writer.U64(record.Revision.Value);
            writer.I32(record.QuotaStep);
            writer.Bytes(record.Context.Context.ToArray());
            writer.I32(record.Context.DeliberationDepth);
            writer.I32(record.Context.ActionCount);
            GrammarPolicyDecision journalDecision = record.Decision;
            WriteDecision(writer, in journalDecision);
        }
        List<KeyValuePair<GrammarPolicyContextKey, CacheValue>> entries = new(_entries);
        entries.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        writer.I32(entries.Count);
        for (int index = 0; index < entries.Count; index++)
        {
            GrammarPolicyContextKey key = entries[index].Key;
            writer.Bytes(key.Context.ToArray());
            writer.I32(key.DeliberationDepth);
            writer.I32(key.ActionCount);
            writer.U64(entries[index].Value.QuotaID.Value);
            writer.I64(entries[index].Value.LastUsed);
        }
    }

    private static void WriteDecision(CkptWriter writer, in GrammarPolicyDecision decision)
    {
        writer.I32(decision.Action);
        writer.I64(decision.LearnedWeight);
        writer.I32(decision.MatchingRecords);
        writer.U64(decision.Revision.Value);
        writer.I32(decision.Completion.Held);
        writer.I32(decision.Completion.Used);
        writer.I32(decision.Completion.Reclaimed);
        writer.I64(decision.Completion.ScannedBytes);
        writer.I64(decision.Completion.ExpandedEdges);
        writer.U64(decision.Fingerprint);
        writer.U64(decision.OccurrenceDigest);
    }

    public void AppendEntries(List<PolicyReadoutCacheEntry> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        List<KeyValuePair<GrammarPolicyContextKey, CacheValue>> entries = new(_entries);
        entries.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        for (int index = 0; index < entries.Count; index++)
            destination.Add(new PolicyReadoutCacheEntry(entries[index].Key, entries[index].Value.Decision, entries[index].Value.QuotaID));
    }

    internal PolicyReadoutCacheReplacement CaptureReplacement()
    {
        List<KeyValuePair<GrammarPolicyContextKey, CacheValue>> entries = new(_entries);
        entries.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        PolicyReadoutCacheEntryReplacement[] replacement = new PolicyReadoutCacheEntryReplacement[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            CacheValue value = entries[index].Value;
            replacement[index] = new(entries[index].Key, value.Decision, value.QuotaID, value.LastUsed);
        }
        GrammarPolicyReadoutQuotaRecord[] journal = [.. _fundingJournal.Values];
        Array.Sort(journal, static (left, right) => left.QuotaID.Value.CompareTo(right.QuotaID.Value));
        return new(Revision, _useClock, replacement, journal);
    }

    internal void ApplyReplacement(in PolicyReadoutCacheReplacement replacement)
    {
        if (replacement.Entries is null || replacement.QuotaJournal is null
            || replacement.Entries.Length > MaxEntries || replacement.QuotaJournal.Length > MaxEntries
            || replacement.UseClock < 0)
            throw new InvalidDataException("policy readout cache replacement exceeds its bounded working set");
        _entries.Clear(); _fundingJournal.Clear(); _lru.Clear();
        Revision = replacement.Revision;
        _useClock = replacement.UseClock;
        _generation = 1; _needsSweep = true; SweepCount = 0; SweptEntries = 0; CanonicalComputations = 0;
        foreach (GrammarPolicyReadoutQuotaRecord record in replacement.QuotaJournal)
        {
            if (record.QuotaID.Value == 0 || !_fundingJournal.TryAdd(record.QuotaID, record))
                throw new InvalidDataException("policy readout cache replacement has duplicate quota identity");
        }
        List<PolicyReadoutCacheEntryReplacement> ordered = [.. replacement.Entries];
        ordered.Sort(static (left, right) => left.LastUsed.CompareTo(right.LastUsed));
        HashSet<GrammarPolicyContextKey> seen = new();
        foreach (PolicyReadoutCacheEntryReplacement entry in ordered)
        {
            if (entry.LastUsed < 0 || !seen.Add(entry.Context)
                || !_fundingJournal.TryGetValue(entry.QuotaID, out GrammarPolicyReadoutQuotaRecord record)
                || !record.Context.Equals(entry.Context) || record.Decision != entry.Decision)
                throw new InvalidDataException("policy readout cache replacement entry is not bound to its journal");
            LinkedListNode<GrammarPolicyContextKey> node = _lru.AddFirst(entry.Context);
            _entries.Add(entry.Context, new CacheValue(entry.Decision, entry.QuotaID, entry.LastUsed, _generation, node));
        }
    }

    internal ulong ComputeCanonicalCandidateSetDigest(CortexPolicyID policy, int actionCount, GrammarRevisionID revision)
    {
        List<PolicyReadoutCacheEntry> entries = new();
        AppendEntries(entries);
        ulong hash = 14695981039346656037UL;
        int count = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            PolicyReadoutCacheEntry entry = entries[index];
            if (!entry.Context.IsCanonical || entry.Context.ActionCount != actionCount
                || entry.Decision.Revision != revision
                || !entry.Context.CanonicalState.Policy.Equals(policy)) continue;
            count++;
            PolicyCanonicalStateID state = entry.Context.CanonicalState;
            Mix(ref hash, (byte)state.Kind);
            Mix(ref hash, state.Version);
            Mix(ref hash, state.Value);
            Mix(ref hash, unchecked((ulong)entry.Decision.Action));
            Mix(ref hash, new PolicyCanonicalizerVersion(state.Version).Value);
        }
        if (count == 0) return 0;
        Mix(ref hash, unchecked((ulong)count));
        return hash == 0 ? 1 : hash;
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= 1099511628211UL;
    }

    public void Load(CkptReader reader, int actionCount = 0)
    {
        Revision = new GrammarRevisionID(reader.U64());
        _entries.Clear();
        _lru.Clear();
        _fundingJournal.Clear();
        _useClock = reader.I64();
        if (_useClock < 0) throw new InvalidDataException("invalid policy readout cache use clock");
        _generation = 1;
        _needsSweep = true;
        SweepCount = 0;
        SweptEntries = 0;
        int journalCount = reader.I32();
        if (journalCount is < 0 or > MaxEntries) throw new InvalidDataException("invalid policy readout quota journal count");
        for (int index = 0; index < journalCount; index++)
        {
            CortexPolicyQuotaDecisionID quotaID = new(reader.U64());
            CortexPolicyID policy = new(reader.Str());
            GrammarRevisionID revision = new(reader.U64());
            int quotaStep = reader.I32();
            byte[] journalContextBytes = reader.Bytes(MaxContextBytes);
            int journalDepth = reader.I32();
            int journalActionCount = reader.I32();
            GrammarPolicyReadoutQuotaRecord record = new(
                quotaID,
                policy,
                revision,
                quotaStep,
                new GrammarPolicyContextKey(journalContextBytes, journalActionCount, journalDepth),
                ReadDecision(reader));
            GrammarPolicyDecision journalDecision = record.Decision;
            GrammarPolicyContextKey journalContext = record.Context;
            bool retainedCanonicalEvidence = journalContext.IsCanonical
                && record.Revision == journalDecision.Revision
                && record.Revision.Value <= Revision.Value;
            if ((!retainedCanonicalEvidence && (record.Revision != Revision || journalDecision.Revision != Revision))
                || record.QuotaID.Value == 0
                || !record.QuotaID.Equals(GrammarPolicyReadout.ComputeQuotaID(record.Policy, record.Revision, record.QuotaStep, in journalContext, in journalDecision))
                || !_fundingJournal.TryAdd(record.QuotaID, record))
                throw new InvalidDataException("grammar policy readout quota journal is not append-only and canonical");
        }
        int count = reader.I32();
        if (count is < 0 or > MaxEntries) throw new InvalidDataException("invalid policy readout cache count");
        if (count > 0 && Revision == GrammarRevisionID.Zero)
            throw new InvalidDataException("policy readout cache entries require a published grammar revision");
        HashSet<long> useClocks = new();
        List<(GrammarPolicyContextKey Key, long LastUsed)> loadedEntries = new(count);
        for (int index = 0; index < count; index++)
        {
            byte[] context = reader.Bytes(MaxContextBytes);
            GrammarPolicyContextKey key;
            int depth = reader.I32();
            int actionCountForKey = reader.I32();
            try { key = new GrammarPolicyContextKey(context, actionCountForKey, depth); }
            catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("grammar policy cache entry has an invalid deliberation depth", error); }
            CortexPolicyQuotaDecisionID quotaID = new(reader.U64());
            long lastUsed = reader.I64();
            if (!_fundingJournal.TryGetValue(quotaID, out GrammarPolicyReadoutQuotaRecord record)
                || !record.Context.Equals(key)
                || (actionCount > 0 && (uint)record.Decision.Action >= (uint)actionCount)
                || lastUsed <= 0 || lastUsed > _useClock || !useClocks.Add(lastUsed)
                || !_entries.TryAdd(key, new CacheValue(record.Decision, quotaID, lastUsed, _generation, null!)))
                throw new InvalidDataException("grammar policy readout cache repeats a canonical context");
            loadedEntries.Add((key, lastUsed));
        }
        if (_fundingJournal.Count != _entries.Count)
            throw new InvalidDataException("grammar policy readout quota journal contains an orphan active record");
        loadedEntries.Sort(static (left, right) => left.LastUsed.CompareTo(right.LastUsed));
        for (int index = 0; index < loadedEntries.Count; index++)
        {
            GrammarPolicyContextKey key = loadedEntries[index].Key;
            CacheValue value = _entries[key];
            value.Node = _lru.AddFirst(key);
        }
    }

    private static GrammarPolicyDecision ReadDecision(CkptReader reader)
        => new(reader.I32(), reader.I64(), reader.I32(), new GrammarRevisionID(reader.U64()),
            new GrammarContinuationQuotaCompletion(reader.I32(), reader.I32(), reader.I32(), reader.I64(), reader.I64()), reader.U64())
        {
            OccurrenceDigest = reader.U64(),
        };

    internal bool NeedsSweep(GrammarRevisionID revision)
        => _needsSweep && Revision == revision;

    internal void MarkSwept(GrammarRevisionID revision)
    {
        if (Revision != revision) throw new InvalidOperationException("cannot sweep a different policy revision");
        _needsSweep = false;
    }

    internal void RecordSweep(int entryCount)
    {
        SweepCount = checked(SweepCount + 1);
        SweptEntries = checked(SweptEntries + entryCount);
    }

    internal void RecordCanonicalComputation()
        => CanonicalComputations = checked(CanonicalComputations + 1);

    internal void RequestSweep()
        => _needsSweep = true;

    internal bool TryGetFundingRecord(CortexPolicyQuotaDecisionID quotaID, out GrammarPolicyReadoutQuotaRecord record)
        => _fundingJournal.TryGetValue(quotaID, out record);

    public void MoveToRevision(GrammarRevisionID revision)
    {
        if (revision == Revision) return;
        Revision = revision;
        _generation = checked(_generation + 1);
        _needsSweep = true;
    }

    internal void Remove(in GrammarPolicyContextKey key)
    {
        if (_entries.TryGetValue(key, out CacheValue? value) && value is not null) RemoveEntry(in key, value);
    }

    private void RemoveEntry(in GrammarPolicyContextKey key, CacheValue value)
    {
        if (_entries.Remove(key))
        {
            _lru.Remove(value.Node);
            _fundingJournal.Remove(value.QuotaID);
        }
    }
}

internal static class GrammarPolicyReadout
{
    public static PolicyReadoutCacheReceipt ReadCache(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        int deliberationDepth,
        PolicyReadoutCache cache)
        => ReadCache(in installRevision, policy, features, actionCount, deliberationDepth, cache, ReadOnlySpan<MetricID>.Empty);

    internal static GrammarPolicyContextKey CreateContext(
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        int deliberationDepth,
        ReadOnlySpan<MetricID> excludedMetricIDs,
        out int semanticFeatureCount)
    {
        Span<MetricSample> semanticFeatures = stackalloc MetricSample[features.Length];
        semanticFeatureCount = 0;
        for (int index = 0; index < features.Length; index++)
        {
            MetricSample sample = features[index];
            bool excluded = false;
            for (int excludedIndex = 0; excludedIndex < excludedMetricIDs.Length; excludedIndex++)
            {
                if (sample.MetricID.Equals(excludedMetricIDs[excludedIndex]))
                {
                    excluded = true;
                    break;
                }
            }
            if (!excluded) semanticFeatures[semanticFeatureCount++] = sample;
        }
        if (semanticFeatureCount == 0)
            throw new ArgumentException("semantic policy context cannot exclude every metric", nameof(excludedMetricIDs));
        byte[] context = TapePacketCreator.EncodePolicyGrammarContext(
            policy, semanticFeatures[..semanticFeatureCount], actionCount);
        return GrammarPolicyContextKey.TakeOwnership(context, 0, deliberationDepth);
    }

    internal static GrammarPolicyContextKey CreateContext(
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        int deliberationDepth,
        ReadOnlySpan<MetricID> excludedMetricIDs)
        => CreateContext(policy, features, actionCount, deliberationDepth, excludedMetricIDs,
            out _);

    public static PolicyReadoutCacheReceipt ReadCache(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        int deliberationDepth,
        PolicyReadoutCache cache,
        ReadOnlySpan<MetricID> excludedMetricIDs)
    {
        GrammarPolicyContextKey contextKey = CreateContext(
            policy, features, actionCount, deliberationDepth, excludedMetricIDs,
            out int semanticFeatureCount);
        return ReadCacheCore(in installRevision, policy, actionCount, semanticFeatureCount, cache, in contextKey);
    }

    public static PolicyReadoutCacheReceipt ReadCanonicalCache(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        int actionCount,
        int deliberationDepth,
        PolicyReadoutCache cache)
    {
        GrammarPolicyContextKey contextKey = new(in canonicalState, actionCount, deliberationDepth);
        return ReadCacheCore(in installRevision, policy, actionCount, 0, cache, in contextKey);
    }

    private static PolicyReadoutCacheReceipt ReadCacheCore(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        int actionCount,
        int featureCount,
        PolicyReadoutCache cache,
        in GrammarPolicyContextKey contextKey)
    {
        bool found = cache.TryGet(installRevision.Revision, in contextKey, out GrammarPolicyDecision decision);
        if (cache.NeedsSweep(installRevision.Revision))
        {
            List<PolicyReadoutCacheEntry> entries = new();
            cache.AppendEntries(entries);
            cache.RecordSweep(entries.Count);
            for (int index = 0; index < entries.Count; index++)
            {
                PolicyReadoutCacheEntry entry = entries[index];
                GrammarPolicyContextKey entryContext = entry.Context;
                GrammarPolicyDecision entryDecision = entry.Decision;
                if (!cache.TryGetFundingRecord(entry.QuotaID, out GrammarPolicyReadoutQuotaRecord quotaRecord))
                    throw new InvalidDataException("grammar policy readout cache references a missing quota record");
                if (entryDecision.Revision != installRevision.Revision)
                {
                    // A canonical entry is retained as shadow evidence and will be
                    // revalidated/refilled on the next request.  Raw entries were already
                    // removed at the revision boundary and cannot be grandfathered.
                    if (entryContext.IsCanonical) continue;
                    cache.Remove(in entryContext);
                    continue;
                }
                ValidateCachedDecision(in installRevision, policy, actionCount, featureCount, cache, in quotaRecord, in entryContext, in entryDecision);
            }
            cache.MarkSwept(installRevision.Revision);
        }
        // A hit admitted by Refill is already canonical; persisted entries were exhaustively
        // checked by the sweep above. Recomputing the installRevision on every warm hit would turn
        // the cache into a second full readout engine instead of an O(1) working-set lookup.
        return new PolicyReadoutCacheReceipt(
            found ? PolicyReadoutCacheOutcomes.Hit : PolicyReadoutCacheOutcomes.Miss,
            contextKey,
            decision);
    }

    public static PolicyReadoutCacheReceipt Refill(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        int deliberationDepth,
        GrammarContinuationQuota quota,
        PolicyReadoutCache cache,
        in GrammarPolicyContextKey contextKey,
        CortexPolicyQuotaDecisionID quotaID,
        int quotaStep,
        out GrammarContinuationQuotaCompletion completion)
    {
        bool found = TryChooseCanonicalContext(
            in installRevision, policy, actionCount, deliberationDepth, quota, cache,
            in contextKey, quotaID, quotaStep, out GrammarPolicyDecision decision, out completion);
        return new PolicyReadoutCacheReceipt(
            found ? PolicyReadoutCacheOutcomes.Refilled : PolicyReadoutCacheOutcomes.Miss,
            contextKey,
            decision);
    }

    public static PolicyReadoutCacheReceipt RefillCanonical(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        int actionCount,
        int deliberationDepth,
        GrammarContinuationQuota quota,
        PolicyReadoutCache cache,
        CortexPolicyQuotaDecisionID quotaID,
        int quotaStep,
        out GrammarContinuationQuotaCompletion completion)
    {
        GrammarPolicyContextKey contextKey = new(in canonicalState, actionCount, deliberationDepth);
        return Refill(
            in installRevision,
            policy,
            ReadOnlySpan<MetricSample>.Empty,
            actionCount,
            deliberationDepth,
            quota,
            cache,
            in contextKey,
            quotaID,
            quotaStep,
            out completion);
    }

    public static bool TryChooseCanonicalContext(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        int actionCount,
        int deliberationDepth,
        GrammarContinuationQuota quota,
        PolicyReadoutCache cache,
        in GrammarPolicyContextKey contextKey,
        CortexPolicyQuotaDecisionID quotaID,
        int quotaStep,
        out GrammarPolicyDecision decision,
        out GrammarContinuationQuotaCompletion completion)
    {
        if (contextKey.DeliberationDepth != deliberationDepth)
            throw new InvalidOperationException("canonical policy context depth differs from its readout request");
        cache.RecordCanonicalComputation();
        bool found = ComputeCanonicalContext(
            in installRevision, policy, actionCount, deliberationDepth, quota,
            contextKey.Context, out decision, out completion);
        if (found)
        {
            if (quotaID.Value == 0) cache.Store(policy, installRevision.Revision, in contextKey, in decision);
            else cache.StoreBound(policy, installRevision.Revision, in contextKey, in decision, quotaID, quotaStep);
        }
        return found;
    }

    private static bool ComputeCanonicalContext(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        int actionCount,
        int deliberationDepth,
        GrammarContinuationQuota quota,
        ReadOnlySpan<byte> context,
        out GrammarPolicyDecision decision,
        out GrammarContinuationQuotaCompletion completion)
    {
        byte[][] continuations = new byte[actionCount][];
        for (int action = 0; action < actionCount; action++)
            continuations[action] = TapePacketCreator.EncodePolicyGrammarContinuation(action);
        bool found = installRevision.TryChooseContinuation(
            context,
            continuations,
            quota,
            deliberationDepth,
            out GrammarContinuationDecision continuation,
            out GrammarContinuationReadoutReceipt readoutReceipt);
        if (readoutReceipt.Revision != installRevision.Revision
            || readoutReceipt.CorpusBytes != continuation.ScannedBytes
            || readoutReceipt.MatchingRecords != continuation.MatchingRecords
            || readoutReceipt.ExpandedEdges != continuation.ExpandedEdges)
            throw new InvalidDataException("policy readout index receipt diverges from its continuation decision");
        completion = quota.Complete();
        if (!found)
        {
            decision = default;
            return false;
        }
        decision = new GrammarPolicyDecision(
            continuation.Continuation,
            continuation.LearnedWeight,
            continuation.MatchingRecords,
            installRevision.Revision,
            completion,
            PolicyCanonicalStateID.TryDecode(context, out PolicyCanonicalStateID canonicalState)
                ? ComputeStateFingerprint(policy, in canonicalState)
                : ComputeFingerprint(installRevision.Revision, policy))
        {
            OccurrenceDigest = PolicySupportDigest.Compute(
                continuation.CandidateScores,
                continuation.CandidateCounts,
                continuation.MatchingRecords),
        };
        return true;
    }

    private static void ValidateCachedDecision(
        in InstallRevision installRevision,
        CortexPolicyID policy,
        int actionCount,
        int featureCount,
        PolicyReadoutCache cache,
        in GrammarPolicyReadoutQuotaRecord quotaRecord,
        in GrammarPolicyContextKey context,
        in GrammarPolicyDecision cached)
    {
        if (actionCount < 2) throw new ArgumentOutOfRangeException(nameof(actionCount));
        if (cached.Revision != installRevision.Revision)
            throw new InvalidDataException("grammar policy cache entry revision differs from its owning installRevision");
        if ((uint)cached.Action >= (uint)actionCount)
            throw new InvalidDataException("grammar policy cache entry action exceeds its owning policy schema");
        PolicyCanonicalStateID canonicalState = context.IsCanonical ? context.CanonicalState : default;
        ulong expectedFundingFingerprint = context.IsCanonical
            ? ComputeStateFingerprint(policy, in canonicalState)
            : ComputeFingerprint(installRevision.Revision, policy);
        if (cached.MatchingRecords < 0 || cached.Fingerprint != expectedFundingFingerprint
            || cached.OccurrenceDigest == 0)
            throw new InvalidDataException("grammar policy cache entry is not a canonical readout");
        if (context.IsCanonical)
        {
            if (!context.CanonicalState.Policy.Equals(policy) || context.ActionCount != actionCount)
                throw new InvalidDataException("grammar policy canonical state is owned by another policy");
        }
        else if (!TapePacketCreator.ValidatePolicyGrammarContext(context.Context, policy, actionCount, featureCount))
            throw new InvalidDataException("grammar policy cache context is not owned by its policy schema");
        if (!quotaRecord.QuotaID.Equals(ComputeQuotaID(quotaRecord.Policy, quotaRecord.Revision, quotaRecord.QuotaStep, in context, in cached))
            || !quotaRecord.Context.Equals(context)
            || quotaRecord.Revision != installRevision.Revision
            || !quotaRecord.Policy.Equals(policy))
            throw new InvalidDataException("grammar policy cache quota journal record does not match its cache entry");
        if (cached.Completion.Held <= 0)
            throw new InvalidDataException("grammar policy cache entry has no bounded quota reservation");
        if (cached.Completion.Held > PolicyReadoutCache.MaxFundingReservation)
            throw new InvalidDataException("grammar policy cache entry exceeds its bounded quota reservation");
        try
        {
            int expectedSpent = checked(1 + context.DeliberationDepth);
            if (cached.Completion.Used != expectedSpent
                || cached.Completion.Reclaimed != checked(cached.Completion.Held - expectedSpent))
                throw new InvalidDataException("grammar policy cache completion does not carry canonical work and refund");
            GrammarContinuationQuota quota = new(cached.Completion.Held);
            cache.RecordCanonicalComputation();
            bool found = ComputeCanonicalContext(
                in installRevision, policy, actionCount, context.DeliberationDepth, quota,
                context.Context, out GrammarPolicyDecision recomputed, out _);
            if (!found)
                throw new InvalidDataException(
                    $"grammar policy cache entry vanished from its owning installRevision: policy={policy} revision={installRevision.Revision} " +
                    $"rules={installRevision.Snapshot.Rules.Length} symbols={installRevision.Snapshot.Compressed.Length} " +
                    $"context={context.ContextDigest:X16} quota={quotaRecord.QuotaID.Value:X16} cached=[{DescribeDecision(in cached)}]");
            if (recomputed != cached)
                throw new InvalidDataException(
                    $"grammar policy cache entry diverges from its owning installRevision: policy={policy} revision={installRevision.Revision} " +
                    $"rules={installRevision.Snapshot.Rules.Length} symbols={installRevision.Snapshot.Compressed.Length} " +
                    $"context={context.ContextDigest:X16} quota={quotaRecord.QuotaID.Value:X16} " +
                    $"cached=[{DescribeDecision(in cached)}] recomputed=[{DescribeDecision(in recomputed)}]");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("grammar policy cache entry cannot fund its owning installRevision", error);
        }
    }

    private static string DescribeDecision(in GrammarPolicyDecision decision)
        => $"action={decision.Action} weight={decision.LearnedWeight} matches={decision.MatchingRecords} " +
           $"reserved={decision.Completion.Held} spent={decision.Completion.Used} refund={decision.Completion.Reclaimed} " +
           $"scan={decision.Completion.ScannedBytes} edges={decision.Completion.ExpandedEdges} " +
           $"fingerprint={decision.Fingerprint:X16} support={decision.OccurrenceDigest:X16}";

    internal static ulong ComputeFingerprint(GrammarRevisionID revision, CortexPolicyID policy)
    {
        ulong hash = 14695981039346656037UL ^ revision.Value;
        for (int index = 0; index < policy.Value.Length; index++) hash = (hash ^ policy.Value[index]) * 1099511628211UL;
        return hash;
    }

    internal static ulong ComputeCandidateFingerprint(
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        in GrammarPolicyDecision decision)
    {
        PolicyCandidateIdentity identity = new(
            policy,
            in canonicalState,
            decision.Action,
            new PolicyCanonicalizerVersion(canonicalState.Version));
        ulong hash = 14695981039346656037UL;
        void Mix(ulong value) { hash ^= value; hash *= 1099511628211UL; }
        for (int index = 0; index < identity.Policy.Value.Length; index++) Mix(identity.Policy.Value[index]);
        Mix((byte)identity.State.Kind); Mix(identity.State.Version); Mix(identity.State.Value);
        Mix(unchecked((ulong)identity.Action)); Mix(identity.Canonicalizer.Value);
        return hash == 0 ? 1 : hash;
    }

    internal static ulong ComputeStateFingerprint(CortexPolicyID policy, in PolicyCanonicalStateID state)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(ulong value) { hash ^= value; hash *= 1099511628211UL; }
        for (int index = 0; index < policy.Value.Length; index++) Mix(policy.Value[index]);
        Mix((byte)state.Kind); Mix(state.Version); Mix(state.Value);
        return hash == 0 ? 1 : hash;
    }

    internal static CortexPolicyQuotaDecisionID ComputeQuotaID(CortexPolicyID policy, GrammarRevisionID revision, int quotaStep, in GrammarPolicyContextKey context, in GrammarPolicyDecision decision)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(ulong value) { hash ^= value; hash *= 1099511628211UL; }
        for (int index = 0; index < policy.Value.Length; index++) Mix(policy.Value[index]);
        Mix(decision.Fingerprint); Mix(revision.Value); Mix(unchecked((ulong)quotaStep));
        Mix(unchecked((ulong)context.DeliberationDepth)); Mix(unchecked((ulong)(1 + context.DeliberationDepth)));
        Mix(unchecked((ulong)context.Context.Length)); Mix(context.ContextDigest);
        return new CortexPolicyQuotaDecisionID(hash == 0 ? 1 : hash);
    }
}
