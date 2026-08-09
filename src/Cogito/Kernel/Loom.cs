namespace Cogito.Induct;

using System.Buffers;
using System.Diagnostics;
using Cogito.Cas;
using Cogito.Codec;
using Cogito.Grammar;

/// A mutation receipt for the standing Loom. Counts are deliberately physical touches, not wall-time guesses:
/// callers can account for appended/repriced/removed events, shed no-ops, local segment compaction, arena pages,
/// count keys, and rule mints independently in their trace boundary.
public readonly record struct LoomMutationReceipt(
    int AppendedEvents,
    int RepricedEvents,
    int RemovedEvents,
    int ShedEvents,
    int CompactedSegments,
    long TouchedSymbols,
    int TouchedCountKeys,
    int CompactedArenaSlots,
    int MintedRules,
    long HeapMutations = 0,
    int HeapChangedKeys = 0)
{
    public static LoomMutationReceipt Empty => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public LoomMutationReceipt Add(in LoomMutationReceipt other)
        => new(AppendedEvents + other.AppendedEvents, RepricedEvents + other.RepricedEvents,
            RemovedEvents + other.RemovedEvents, ShedEvents + other.ShedEvents,
            CompactedSegments + other.CompactedSegments, TouchedSymbols + other.TouchedSymbols,
            TouchedCountKeys + other.TouchedCountKeys, CompactedArenaSlots + other.CompactedArenaSlots,
            MintedRules + other.MintedRules, HeapMutations + other.HeapMutations,
            HeapChangedKeys + other.HeapChangedKeys);
    public static LoomMutationReceipt operator +(LoomMutationReceipt left, LoomMutationReceipt right) => left.Add(in right);
}

/// One append to Loom's non-derivable rank journal.  Rule entries carry their
/// content identity and cost; aliases carry only their existing target.
internal readonly record struct LoomEntryDelta(
    bool Alias, uint A, uint B, int Sym, RuleID RuleID, Mbits Cost);

/// Loom's checkpoint receipt.  Entries are append-only during the day.  A
/// rebase/seed is an explicit journal reset followed by the replacement entry
/// stream; arena/count state remains derived from that stream plus Tape.
internal readonly record struct LoomCheckpointDelta(
    long ParentRevision,
    long Revision,
    long IDMark,
    long Savings,
    LoomEntryDelta[] Entries,
    bool Reset)
{
    internal bool IsEmpty => ParentRevision == Revision && (Entries?.Length ?? 0) == 0 && !Reset;
}

// ── THE LOOM ──  persistent Re-Pair: the O(Δ) incremental induction ('s pin task — the ONE
// per-pass cost that stayed O(tape) after the night shift fell). The kernel (RePair.Induce) was ALREADY
// incremental — incremental digram counts, active occurrence postings, merge-site-local neighbor fixes, an indexed max-heap —
// but it ran with AMNESIA: every state was scratch, rebuilt from tape.Concat() each stride, so per-stride cost was
// O(tape) rebuild + O(new merge work) and the total went O(tape²). The Loom promotes that scratch to a standing
// organ, and induction inverts into SPLICE + PUMP:
//
//   SPLICE  a new span is tokenized O(Δ) and RANK-ENCODED through the standing rules (BPE-style: apply existing
//           merges in mint-rank order — a pair-heap encoder, O(Δ·log Δ), rule-count-independent), then its parsed
//           form lands in the arena and its adjacent digrams Inc into cnt/occ/heap at the span's weight. New data
//           is PARSED BY the grammar; it never re-derives it. The barrier law guarantees zero junction work.
//   PUMP    the RePair winner loop VERBATIM over the persistent state: pop winner (max count, smallest key — the
//           same total order), gate on 3·wScale + Δmdl≥tau, rewrite the postings' exact sites, fix neighbors via
//           the same Dec/Inc, mint the rule, emit the MergeEvent. O(new merge work) per call.
//
// THE STANDING INVARIANT (why a spliced span never re-derives structure): a digram equal to an existing RANK
// ENTRY's pair can never re-form. Rank-encoding leaves none (every match merges), and later merges cannot create
// one — a merge replaces a PAIR with a NEW symbol n, never deletes between symbols, and any digram containing n
// can only match entries minted after n. So every entry digram's live count is 0 forever, `_rank` never collides
// (Add throws = the tripwire), and the pump can never mint a duplicate rule. The argument covers ALIAS entries
// (self-compression's merged digrams) identically — an alias is a rank entry whose merge target is an EXISTING
// symbol instead of a fresh mint.
//
// THE ARENA IS A PURE FUNCTION: for any span, (splice through entries 0..k) + (pump rewrites k+1..m) ≡ rank-encode
// through entries 0..m — merges apply per entry in rank order, left-to-right non-overlapping, and cross-span order
// cannot matter (no digram crosses a barrier — THE ORDER-FREEDOM THEOREM, which is also why sleep's defrag reorder
// and phase-3 shedding never change the grammar). Hence the whole arena ≡ rank-encode(entries, view spans in id
// order): the checkpoint stores ONLY the entry journal + savings + the id high-water and Load re-derives
// arena/cnt/occ/heap/uses (the pattern — Save∘Load ≡ the incremental accretion; verify-loom's resume arm is
// the byte-identity gate).
//
// DETERMINISM (the Vow): winner selection reads the heap's total order (max count, smallest key) — every count
// change repairs the unique node for that key, so the pick is a function of the live cnt table alone, independent
// of insertion history. Rewrites sort their positions (arena order = span-id order;
// within a span that is left-to-right, across spans the merges commute). No dictionary iteration anywhere.
//
// BATCH ≡ LOOM: a fresh Loom spliced whole and pumped once replays the linear RePair byte-for-byte — same tally
// sequence, same heap ops, same rewrites, same harvest (barriers live outside the arena and are re-emitted in
// place; they are inert in RePair by the barrier law). Engine's batch entries run exactly that (verify-loom's
// batch-identity arm gates it against the RePair oracle, which verify-induct gates against InduceReference).
// Aliases never enter the batch path — self-compression is a live-lineage night organ.
//
// GREEDY-IN-ARRIVAL: an incrementally-grown loom is NOT byte-identical to a batch over the final tape — the batch
// sees final frequencies before choosing merge order; the loom commits winners on what has arrived. By design the
// divergence lives in the Zipf tail near the mint bar: reconstruction stays EXACT (every merge is reversible) and
// the MDL gap stays small — verify-loom's incremental arm asserts the first and bounds+reports the second.
//
// WEIGHTS: the loom rides the provenance count measure — a span splices at its evidence weight
// (wScale, or 1 unvested). Corroboration (Vest) happens ONLY inside the night's Consolidate, and the REBASE that
// immediately follows re-splices every span at its CURRENT evidence status — the rebase IS the vest-reweigh hook,
// so the live count plane always equals what a fresh Load would re-derive (resume-exactness holds at any wScale).
//
// PHASE LADDER: 1 (done) — the verified substrate behind --loom; 2 (this) — default ON, the day is pure O(Δ)
// splice+pump, the night's batch re-greed is the rare REBASE, wScale>1 rides; 3 (this) — self-compression
// (SelfCompress.Compact + rank aliases) and the rolling resident window (Tape.Evacuate — the memory win).
public sealed class Loom : IDisposable
{
    // ── the arena: the working tape at symbol resolution, flat + append-only (slots never move, never reused —
    // an arena index is a STABLE address, so occ postings survive tape defrag reorders for free). Grows by
    // doubling; a REBASE resets it wholesale (which is also what compacts dead slots — a re-spliced span lands at
    // its PARSED length, so the arena shrinks as the grammar deepens: learning reclaims working memory). Same slot
    // discipline as the batch kernel: a merge kills the RIGHT slot, the left survives — a segment's first slot
    // can never die.
    private int[]  _sym  = [];        // symbol per slot
    private int[]  _nxt  = [];        // → next live slot in the SAME segment (−1 = segment end)
    private int[]  _prv  = [];        // → prev live slot in the SAME segment (−1 = segment start)
    private bool[] _dead = [];        // slot consumed by a merge (the right half)
    private byte[] _wt   = [];        // per-slot evidence weight (span-constant — one fetch serves a merge site)
    private int _len;                 // arena high-water (slots in use)
    private long _live;               // live symbols across all segments (the pump's ≥2 guard, batch parity)
    private long _weightedLive;       // exact weighted live symbols used by the count-plane overflow guard

    /// One spliced span's parsed residence: its arena range plus the barrier run Concat writes after it. `Bars`
    /// re-emits at harvest — barrier terminals are inert in the kernel (never counted, never merged) so they live
    /// OUTSIDE the arena and cost the pump nothing.
    private readonly record struct LoomSeg(long Id, int Off, int Len, int Bars, bool Present);
    private readonly List<LoomSeg> _segs = new();           // in splice order (= id order for tape looms)
    private readonly Dictionary<long, int> _segOf = new();  // event id → _segs index (the harvest's view walk)

    // ── the count plane: cnt is the ONLY cross-span coupling; occ names each digram's live candidate sites.
    // Every arena slot owns at most one posting (the pair beginning there), so removing a rewritten site is a
    // swap-remove through _occIndex rather than a scan. The indexed winner heap carries one node per live count key;
    // updates repair that node in place, so a long-running loom cannot accumulate a history of dead priorities.
    private readonly Dictionary<long, int> _cnt = new();
    private readonly Dictionary<long, List<int>> _occ = new();
    private sealed class IndexedCountHeap
    {
        internal readonly record struct Node(long Key, int Count);
        private readonly List<Node> _nodes = new();
        private readonly Dictionary<long, int> _index = new();

        public int Count => _nodes.Count;
        public bool Contains(long key) => _index.ContainsKey(key);

        public bool Invariant(IReadOnlyDictionary<long, int> counts, int threshold)
        {
            if (_index.Count != _nodes.Count) return false;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                if (!_index.TryGetValue(node.Key, out int at) || at != i) return false;
                if (!counts.TryGetValue(node.Key, out int count) || count != node.Count || count <= 0) return false;
                int left = i * 2 + 1;
                int right = left + 1;
                if (left < _nodes.Count && IsHigher(_nodes[left], node)) return false;
                if (right < _nodes.Count && IsHigher(_nodes[right], node)) return false;
            }
            foreach (var (key, count) in counts)
                if (count >= threshold && !_index.ContainsKey(key)) return false;
            return true;
        }

        private static bool IsHigher(in Node left, in Node right)
            => left.Count > right.Count || (left.Count == right.Count && left.Key < right.Key);

        private void Swap(int a, int b)
        {
            (_nodes[a], _nodes[b]) = (_nodes[b], _nodes[a]);
            _index[_nodes[a].Key] = a;
            _index[_nodes[b].Key] = b;
        }

        private void SiftUp(int at)
        {
            while (at > 0)
            {
                int parent = (at - 1) / 2;
                if (!IsHigher(_nodes[at], _nodes[parent])) break;
                Swap(at, parent);
                at = parent;
            }
        }

        private void SiftDown(int at)
        {
            while (true)
            {
                int left = at * 2 + 1;
                if (left >= _nodes.Count) return;
                int best = left;
                int right = left + 1;
                if (right < _nodes.Count && IsHigher(_nodes[right], _nodes[left])) best = right;
                if (!IsHigher(_nodes[best], _nodes[at])) return;
                Swap(at, best);
                at = best;
            }
        }

        public void Upsert(long key, int count)
        {
            if (count <= 0) { Remove(key); return; }
            if (_index.TryGetValue(key, out int at))
            {
                int previous = _nodes[at].Count;
                if (previous == count) return;
                _nodes[at] = new Node(key, count);
                if (count > previous) SiftUp(at);
                else SiftDown(at);
                return;
            }
            _index[key] = _nodes.Count;
            _nodes.Add(new Node(key, count));
            SiftUp(_nodes.Count - 1);
        }

        public void Remove(long key)
        {
            if (!_index.TryGetValue(key, out int at)) return;
            int last = _nodes.Count - 1;
            _index.Remove(key);
            if (at == last) { _nodes.RemoveAt(last); return; }
            Node replacement = _nodes[last];
            _nodes[at] = replacement;
            _nodes.RemoveAt(last);
            _index[replacement.Key] = at;
            if (at > 0 && IsHigher(_nodes[at], _nodes[(at - 1) / 2])) SiftUp(at);
            else SiftDown(at);
        }

        public bool TryPop(out Node node)
        {
            if (_nodes.Count == 0) { node = default; return false; }
            node = _nodes[0];
            Remove(node.Key);
            return true;
        }

        public void Clear()
        {
            _nodes.Clear();
            _index.Clear();
        }
    }

    internal static bool ValidateIndexedHeapFixture()
    {
        var heap = new IndexedCountHeap();
        var counts = new Dictionary<long, int>();
        bool valid = true;
        void Set(long key, int count)
        {
            counts[key] = count;
            heap.Upsert(key, count);
            valid &= heap.Invariant(counts, 1);
        }
        void Remove(long key)
        {
            counts.Remove(key);
            heap.Remove(key);
            valid &= heap.Invariant(counts, 1);
        }

        Set(30, 3); Set(10, 3); Set(20, 3); // root/middle/leaf ties
        Set(20, 8);                           // leaf rises to root
        Set(10, 1);                           // root falls through the middle
        Remove(20);                           // remove a root with a populated remainder
        Set(40, 4); Set(30, 4);               // equal-count key order
        if (!heap.TryPop(out var first) || first.Key != 30) valid = false;
        counts.Remove(30);
        valid &= heap.Invariant(counts, 1);
        if (!heap.TryPop(out var second) || second.Key != 40) valid = false;
        counts.Remove(40);
        valid &= heap.Invariant(counts, 1);
        return valid;
    }

    private readonly IndexedCountHeap _heap = new();
    private int[] _occIndex = [];                         // arena slot → index in its key's active posting bucket, -1 otherwise
    private readonly HashSet<long> _deferredHeapKeys = new();
    private bool _deferHeap;
    private long _heapMutationCount;
    private int _heapChangedKeyCount;

    // ── the rank plane: the ENTRY JOURNAL — the encoder's whole program, one entry per rank, in rank order.
    // A RULE entry mints nonterminal (alphabet + Sym) at its rank; an ALIAS entry merges
    // its digram into an EXISTING symbol — the mechanism that stops a compacted-away duplicate from re-minting
    // (without the alias, the dead digram's count re-accumulates at the next splice and the pump re-mints it,
    // undoing the compaction). The journal IS the checkpoint payload: Load replays it entry by entry.
    private readonly record struct RankEntry(int Rank, int Sym);
    private readonly record struct LoomEntry(bool Alias, uint A, uint B, int Sym);
    private readonly Dictionary<long, RankEntry> _rank = new();   // digram key → (rank, merge-target rule index)
    private readonly List<LoomEntry> _entries = new();            // rank order — the serialized program
    private int _rankSeq;                                         // next rank (== _entries.Count)
    private int _activeSegs;

    // ── the grammar plane: append-only rules + per-rule RG metadata (depth/span feed MergeEvents) + the
    // incrementally-held usage counters (≡ Engine.RuleUses of the harvest; the GC's rent sense).
    private readonly List<GrammarRule> _rules = new();
    private readonly List<int> _depth = new(), _span = new();
    private readonly List<long> _uses = new();
    private GrammarRule[]? _rulesArr;                        // identity-stable between mints — downstream cover
                                                             // caches key on the array reference, so an unchanged
                                                             // grammar HITS them across steps
    private long _savings;                                   // Σ minted Δmdl (adopted whole on Rebase)
    private long _idMark;                                    // tape ids < this are spliced (the Δ probe — an ID high-water, not a span count: shedding makes residents ≠ ids)
    private readonly List<Symbol> _harvest = new();          // harvest scratch — Result runs every stride; clear+refill retains the backing array, ToArray still mints the fresh product the caller retains
    private long _mutationRevision;
    private long _checkpointRevision;
    private int _checkpointEntryCursor;
    private bool _checkpointReset;
    private bool _legacyWireFormat;
    private RePairResult? _cachedTapeResult;
    private Tape? _cachedTape;
    private long _cachedTapeRevision = -1;
    private long _cachedTapeOrderRevision = -1;
    private long _cachedTapeMutationRevision = -1;
    private RePairResult? _cachedSpliceResult;
    private long _cachedSpliceMutationRevision = -1;

    internal long SpliceIDMark => _idMark;
    internal long MutationRevision => _mutationRevision;

    internal void UpgradeCheckpointWire() => _legacyWireFormat = false;

    private readonly uint _alphabet;
    private readonly int _bar;                               // the unmergeable terminal (−1 = NoBarrier)
    private readonly int _wScale;

    // rank-encoder scratch (reused across splices; the pair-heap orders (rank, pos) = lowest rank first, leftmost
    // on ties — provably equivalent to applying entries sequentially in rank order, the classic fast-BPE argument)
    private readonly PriorityQueue<int, (int Rank, int Pos)> _encq = new();

    public Loom(uint alphabetSize = 256, uint barrier = (uint)'\n', int wScale = 1)
    {
        Tape.RequireWScale(wScale);
        _alphabet = alphabetSize;
        _bar = unchecked((int)barrier);
        _wScale = wScale;
    }

    public int RuleCount => _rules.Count;
    public int SplicedEvents => _activeSegs;
    public long LiveSymbols => _live;
    public long Savings => _savings;
    public int AliasCount => _entries.Count - _rules.Count;
    public IReadOnlyList<long> Uses => _uses;                // verify-loom asserts ≡ Engine.RuleUses(Result())

    /// MemStat census read — the persistent induction organ's mass: arena slots (total incl. dead) + live symbols,
    /// segments, count-plane keys + occurrence postings, heap entries, rank entries. Counts only.
    internal (int ArenaSlots, long LiveSyms, int Segs, int CntKeys, long OccPosts, int HeapCount, int RankEntries) Mass()
    {
        long posts = 0;
        foreach (var l in _occ.Values) posts += l.Count;
        return (_len, _live, _activeSegs, _cnt.Count, posts, _heap.Count, _entries.Count);
    }

    /// Differential-gate witness for the active planes. Every live non-barrier digram has exactly one posting,
    /// its bucket's weighted sum equals cnt, and the indexed heap never has more nodes than live count keys.
    internal bool ValidateActivePlanes()
    {
        long posts = 0;
        long weightedLive = 0;
        foreach (var (key, positions) in _occ)
        {
            if (positions.Count == 0 || !_cnt.TryGetValue(key, out int weighted)) return false;
            int actualWeight = 0;
            for (int at = 0; at < positions.Count; at++)
            {
                int pos = positions[at];
                if ((uint)pos >= (uint)_len || _dead[pos] || _occIndex[pos] != at) return false;
                int next = _nxt[pos];
                if (next < 0 || _dead[next] || _sym[pos] == _bar || _sym[next] == _bar) return false;
                if (Pack(_sym[pos], _sym[next]) != key) return false;
                actualWeight += _wt[pos];
                posts++;
            }
            if (weighted != actualWeight) return false;
        }
        long expected = 0;
        int threshold = checked(3 * _wScale);
        foreach (var (key, count) in _cnt)
            if (count <= 0 || !_occ.ContainsKey(key)) return false;
        if (!_heap.Invariant(_cnt, threshold)) return false;
        foreach (LoomSeg seg in _segs)
        {
            if (!seg.Present) continue;
            for (int pos = seg.Off; pos >= 0 && pos < _len && !_dead[pos]; pos = _nxt[pos])
            {
                weightedLive += _wt[pos];
                int next = _nxt[pos];
                if (next >= 0 && !_dead[next] && _sym[pos] != _bar && _sym[next] != _bar)
                {
                    expected++;
                    if (_occIndex[pos] < 0) return false;
                }
            }
        }
        return posts == expected && weightedLive == _weightedLive && _heap.Count <= _cnt.Count;
    }

    /// A span's parsed length in the arena (live symbols), −1 if never spliced. THE SHED CRITERION reads this:
    /// parsed ≤ 1 ⟺ the whole span is ONE symbol ⟺ the grammar generates it whole — its raw bytes carry no
    /// structure the grammar lacks, so they may leave RAM (Tape.Evacuate) without touching a single count.
    public int ParsedLenOf(long spanId) => _segOf.TryGetValue(spanId, out int si) && _segs[si].Present ? _segs[si].Len : -1;

    private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
    // A delta or winner rewrite mutates many occurrences of the same digram before the next consumer observes the
    // state. Keep exact counts and occurrence postings live on every touch, but arm the winner heap once at the
    // transaction boundary; every intermediate priority is dead before a consumer can read it.
    private void Push(long key, int c)
    {
        if (c <= 0)
        {
            RemoveHeap(key);
            return;
        }
        _heap.Upsert(key, c);
        _heapMutationCount++;
    }

    private void RemoveHeap(long key)
    {
        if (!_heap.Contains(key)) return;
        _heap.Remove(key);
        _heapMutationCount++;
    }

    private bool TryPopHeap(out IndexedCountHeap.Node node)
    {
        bool popped = _heap.TryPop(out node);
        if (popped) _heapMutationCount++;
        return popped;
    }

    private void ClearHeap()
    {
        _heapMutationCount += _heap.Count;
        _heap.Clear();
    }

    private void ArmDeferredHeap()
    {
        if (_deferredHeapKeys.Count == 0) return;
        int changed = _deferredHeapKeys.Count;
        int threshold = checked(3 * _wScale);
        foreach (long key in _deferredHeapKeys)
            if (_cnt.TryGetValue(key, out int count))
            {
                if (count >= threshold) Push(key, count); else RemoveHeap(key);
            }
            else
                RemoveHeap(key);
        _deferredHeapKeys.Clear();
        _heapChangedKeyCount += changed;
    }

    private void Inc(long key, int pos, int w)
    {
        if ((uint)pos >= (uint)_occIndex.Length) throw new InvalidOperationException($"loom: occurrence position {pos} outside arena {_occIndex.Length}");
        if (_occIndex[pos] >= 0) throw new InvalidOperationException($"loom: occurrence slot {pos} already posted");
        List<int> l = _occ.TryGetValue(key, out var existing) ? existing : (_occ[key] = new());
        _occIndex[pos] = l.Count;
        l.Add(pos);
        int c = _cnt.GetValueOrDefault(key) + w; _cnt[key] = c;
        if (_deferHeap) _deferredHeapKeys.Add(key); else Push(key, c);
    }
    private void Dec(long key, int pos, int w)
    {
        if (!_cnt.TryGetValue(key, out int c))
            throw new InvalidOperationException($"loom: missing count for active occurrence key {key} at slot {pos}");
        if ((uint)pos >= (uint)_occIndex.Length || _occIndex[pos] < 0)
            throw new InvalidOperationException($"loom: missing occurrence posting for key {key} at slot {pos}");
        if (!_occ.TryGetValue(key, out List<int>? positions))
            throw new InvalidOperationException($"loom: missing occurrence bucket for key {key} at slot {pos}");
        int at = _occIndex[pos];
        if ((uint)at >= (uint)positions.Count || positions[at] != pos)
            throw new InvalidOperationException($"loom: occurrence index mismatch for key {key} at slot {pos}");
        int last = positions[^1];
        positions[at] = last;
        _occIndex[last] = at;
        positions.RemoveAt(positions.Count - 1);
        _occIndex[pos] = -1;
        if (positions.Count == 0) _occ.Remove(key);
        c -= w;
        if (c <= 0)
        {
            _cnt.Remove(key);
            if (_deferHeap) _deferredHeapKeys.Add(key); else RemoveHeap(key);
        }
        else { _cnt[key] = c; if (_deferHeap) _deferredHeapKeys.Add(key); else Push(key, c); }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  SPLICE — rank-encode an event through the standing entries, land it in the arena, tally its digrams
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Splice one event: rank-encode `bytes` through the standing entries, append the parsed form to the arena,
    /// Inc its adjacent digrams at `weight`, and record `trailingBarriers` barrier terminals for the harvest.
    /// O(Δ·log Δ) — independent of tape size and of rule count. Returns the parsed length.
    public int SpliceEvent(ReadOnlySpan<byte> bytes, long eventID, byte weight, int trailingBarriers = 1)
    {
        if (_segOf.ContainsKey(eventID)) throw new ArgumentException($"loom: event {eventID} already spliced", nameof(eventID));
        if (trailingBarriers > 0 && _bar < 0) throw new ArgumentException("loom: barrier run on a NoBarrier loom", nameof(trailingBarriers));

        int head = 0;
        try
        {
            if (weight == 0 || weight > _wScale) throw new ArgumentOutOfRangeException(nameof(weight), "loom: span weight must be in [1, wScale]");
            int parsed = bytes.Length == 0 ? 0 : Encode(bytes, out head);
            if (_weightedLive + (long)parsed * weight > int.MaxValue)
                throw new InvalidOperationException($"loom: weighted counts overflow int at {_weightedLive + (long)parsed * weight} live symbols");

            // land the encoded form: contiguous arena slots, segment-local links, event-constant weight
            EnsureArena(_len + parsed);
            int off = _len;
            if (parsed > 0)
            {
                int w2 = off;
                for (int k = head; k >= 0; k = _encNxt[k], w2++)
                {
                    _sym[w2] = _encSym[k];
                    _wt[w2] = weight;
                    _dead[w2] = false;
                    _nxt[w2] = w2 + 1 < off + parsed ? w2 + 1 : -1;
                    _prv[w2] = w2 > off ? w2 - 1 : -1;
                    if (_encSym[k] >= (int)_alphabet) _uses[_encSym[k] - (int)_alphabet]++;   // tape reference
                }
                _len += parsed;
                _live += parsed;
                _weightedLive += (long)parsed * weight;
                for (int t = off; t + 1 < off + parsed; t++)                                  // the Δ tally — the only
                    if (_sym[t] != _bar && _sym[t + 1] != _bar)                               // counts a new event moves
                        Inc(Pack(_sym[t], _sym[t + 1]), t, weight);
            }
            _segOf[eventID] = _segs.Count;
            _segs.Add(new LoomSeg(eventID, off, parsed, trailingBarriers, true));
            _activeSegs++;
            return parsed;
        }
        finally
        {
            ReleaseEncodeScratch();
        }
    }

    /// Splice every tape event appended since the last call, in event-ID order. The probe is the ID high-water
    /// (ids are monotonic; NEW appends are always resident and dense in [_idMark, NextId) — evacuation only ever
    /// touches ids the loom already spliced, so the day path never sees a hole).
    public int SpliceNew(Tape tape)
    {
        long hi = tape.NextId;
        var ids = new List<TapeEventID>();
        for (long v = _idMark; v < hi; v++)
        {
            // The high-water is an ID cursor, not a grammar-span count.  A
            // measurement/custody packet may be appended (or dropped) between
            // grammar events; advance past it without minting an empty segment.
            if (tape.TryGetEventView(new TapeEventID(v), out _)) ids.Add(new TapeEventID(v));
        }
        LoomMutationReceipt receipt = SpliceAppended(tape, ids.ToArray());
        _idMark = hi;
        return receipt.AppendedEvents;
    }

    /// Splice exactly the appended ids named by a TapeDelta. This is the append verb for the live path:
    /// it never scans the tape or infers a high-water range, and therefore remains correct when dropped ids
    /// create holes in the monotonic id space. The ids are sorted before landing so a reordered receipt cannot
    /// perturb the rank/arena order that determines later winner ties.
    public LoomMutationReceipt SpliceAppended(Tape tape, ReadOnlySpan<TapeEventID> appended)
    {
        if (appended.Length == 0) return LoomMutationReceipt.Empty;
        InvalidateResults();
        var ids = appended.ToArray();
        Array.Sort(ids, static (x, y) => x.Value.CompareTo(y.Value));
        long touched = 0;
        foreach (TapeEventID id in ids)
        {
            if (_segOf.ContainsKey(id.Value)) throw new InvalidOperationException($"loom: append event {id} is already spliced");
            if (!tape.TryGetEventView(id, out TapeEventView view))
            {
                _idMark = Math.Max(_idMark, id.Value + 1);
                continue;
            }
            if (!view.HasRole(TapeEventRoles.GrammarInput))
            {
                _idMark = Math.Max(_idMark, id.Value + 1);
                continue;
            }
            if (tape.PositionOf(id) < 0)
                throw new InvalidOperationException($"loom: appended grammar event {id} is not resident — staged replay must splice before evacuation");
            if (!tape.Resolve(id, out var bytes))
                throw new InvalidOperationException($"loom: appended event {id} is not a resident tape event");
            int parsed = SpliceEvent(bytes, id.Value, tape.IsEvidence(id) ? (byte)_wScale : (byte)1);
            touched += parsed;
            _idMark = Math.Max(_idMark, id.Value + 1);
        }
        return new LoomMutationReceipt(ids.Length, 0, 0, 0, 0, touched, 0, 0, 0);
    }

    /// Reprice exactly the reflected ids named by a TapeDelta. Reflection changes only the evidence weight;
    /// the parsed symbols and standing rank program stay put. Each segment removes its old weighted digrams and
    /// re-adds the same local digrams at wScale, so no unrelated segment or rule count is rebuilt.
    public LoomMutationReceipt RepriceReflected(Tape tape, ReadOnlySpan<TapeEventID> reflected)
    {
        if (reflected.Length == 0) return LoomMutationReceipt.Empty;
        InvalidateResults();
        long touched = 0; int keys = 0; int events = 0;
        var seen = new HashSet<long>();
        foreach (TapeEventID id in reflected)
        {
            if (!_segOf.TryGetValue(id.Value, out int si) || !_segs[si].Present)
                throw new InvalidOperationException($"loom: reflected event {id} is not spliced");
            if (!tape.IsEvidence(id)) throw new InvalidOperationException($"loom: reflected event {id} is not evidence on the tape");
            LoomSeg seg = _segs[si];
            int oldWeight = seg.Len == 0 ? _wScale : _wt[seg.Off];
            if (oldWeight == _wScale) continue;
            events++;
            int liveSlots = 0;
            for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i]) liveSlots++;
            long nextWeighted = _weightedLive + (long)(_wScale - oldWeight) * liveSlots;
            if (nextWeighted > int.MaxValue) throw new InvalidOperationException($"loom: reflected weight exceeds count range at {nextWeighted}");
            RemoveSegmentCounts(seg, seen, ref touched);
            for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i]) _wt[i] = (byte)_wScale;
            _weightedLive = nextWeighted;
            AddSegmentCounts(seg, seen, ref touched);
        }
        keys = seen.Count;
        return new LoomMutationReceipt(0, events, 0, 0, 0, touched, keys, 0, 0);
    }

    /// Remove exactly the dropped ids named by a TapeDelta. The event leaves the grammar view, so its segment's
    /// live digrams and tape references are subtracted in place; no rule or rank entry is revoked. The arena slots
    /// become tombstones until the explicit CompactArena verb is requested.
    public LoomMutationReceipt RemoveDropped(Tape tape, ReadOnlySpan<TapeEventID> dropped)
    {
        if (dropped.Length == 0) return LoomMutationReceipt.Empty;
        InvalidateResults();
        long touched = 0; int keys = 0; int events = 0; int slots = 0;
        var seen = new HashSet<long>();
        foreach (TapeEventID id in dropped)
        {
            if (!_segOf.TryGetValue(id.Value, out int si) || !_segs[si].Present)
                throw new InvalidOperationException($"loom: dropped event {id} is not spliced");
            LoomSeg seg = _segs[si];
            RemoveSegmentCounts(seg, seen, ref touched);
            for (int i = seg.Off; i >= 0 && i < _len && !_dead[i];)
            {
                int next = _nxt[i];
                if (_sym[i] >= (int)_alphabet) _uses[_sym[i] - (int)_alphabet]--;
                _dead[i] = true; _nxt[i] = -1; _prv[i] = -1;
                _live--; _weightedLive -= _wt[i]; slots++;
                i = next;
            }
            _segs[si] = seg with { Present = false, Len = 0, Bars = 0 };
            _segOf.Remove(id.Value);
            _activeSegs--; events++;
        }
        keys = seen.Count;
        return new LoomMutationReceipt(0, 0, events, 0, 0, touched, keys, slots, 0);
    }

    /// Compact inactive segment metadata without moving arena addresses. Active occurrence postings are already
    /// removed at each count transition, so this local verb only retires dead segment records; arena reclamation is
    /// deliberately separate in CompactArena, so a global copy can never hide inside an ordinary delta application.
    public LoomMutationReceipt CompactSegments()
    {
        int removed = 0;
        for (int i = _segs.Count - 1; i >= 0; i--)
            if (!_segs[i].Present) { _segs.RemoveAt(i); removed++; }
        if (removed > 0)
        {
            InvalidateResults();
            _segOf.Clear();
            for (int i = 0; i < _segs.Count; i++) _segOf[_segs[i].Id] = i;
        }
        return new LoomMutationReceipt(0, 0, 0, 0, removed, 0, 0, 0, 0);
    }

    /// Explicitly reclaim dead arena slots after one or more local mutations. This is a global O(arena) copy and
    /// count-plane rebuild by design; callers can account for it as a night/global maintenance operation rather than
    /// accidentally paying it in append, reflect, or drop verbs.
    public LoomMutationReceipt CompactArena()
    {
        if (_len == _live) return LoomMutationReceipt.Empty;
        InvalidateResults();
        int oldLen = _len;
        int cap = Math.Max(1024, (int)Math.Min(int.MaxValue, Math.Max(1, _live)));
        int[] sym = ArrayPool<int>.Shared.Rent(cap), nxt = ArrayPool<int>.Shared.Rent(cap), prv = ArrayPool<int>.Shared.Rent(cap);
        bool[] dead = ArrayPool<bool>.Shared.Rent(cap); byte[] wt = ArrayPool<byte>.Shared.Rent(cap);
        int at = 0;
        for (int si = 0; si < _segs.Count; si++)
        {
            LoomSeg seg = _segs[si];
            int off = at;
            int count = 0;
            for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i])
            {
                sym[at] = _sym[i]; wt[at] = _wt[i]; dead[at] = false; at++; count++;
            }
            for (int i = off; i < at; i++) { nxt[i] = i + 1 < at ? i + 1 : -1; prv[i] = i > off ? i - 1 : -1; }
            _segs[si] = seg with { Off = off, Len = count };
        }
        ReturnArena();
        _sym = sym; _nxt = nxt; _prv = prv; _dead = dead; _wt = wt; _len = at;
        _occIndex = new int[_sym.Length];
        Array.Fill(_occIndex, -1);
        _cnt.Clear(); _occ.Clear(); ClearHeap(); _deferredHeapKeys.Clear();
        _live = 0; _weightedLive = 0;
        for (int si = 0; si < _segs.Count; si++)
        {
            LoomSeg seg = _segs[si];
            _live += seg.Len;
            for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i])
            {
                _weightedLive += _wt[i];
                if (_nxt[i] >= 0 && _sym[_nxt[i]] != _bar && _sym[i] != _bar)
                    Inc(Pack(_sym[i], _sym[_nxt[i]]), i, _wt[i]);
            }
        }
        return new LoomMutationReceipt(0, 0, 0, 0, _segs.Count, _live, _cnt.Count, oldLen - _len, 0);
    }

    /// Compose the exact TapeDelta verbs. Reordering and shedding are grammar no-ops under the barrier/order-free
    /// law; they are reported but never trigger a reparse. Pump remains a separate verb so Cortex can attach its
    /// MergeEvent stream and threshold policy explicitly.
    public LoomMutationReceipt ApplyTapeDelta(Tape tape, in TapeDelta delta)
    {
        long mutationsBefore = _heapMutationCount;
        int changedBefore = _heapChangedKeyCount;
        bool wasDeferred = _deferHeap;
        _deferHeap = true;
        try
        {
            LoomMutationReceipt receipt = SpliceAppended(tape, delta.Appended);
            receipt += RepriceReflected(tape, delta.Reflected);
            receipt += RemoveDropped(tape, delta.Dropped);
            receipt += new LoomMutationReceipt(0, 0, 0, delta.Shed.Length, 0, 0, 0, 0, 0);
            if (delta.Dropped.Length > 0) receipt += CompactSegments();
            if (!wasDeferred) ArmDeferredHeap();
            return receipt + new LoomMutationReceipt(
                0, 0, 0, 0, 0, 0, 0, 0, 0,
                _heapMutationCount - mutationsBefore,
                _heapChangedKeyCount - changedBefore);
        }
        catch
        {
            if (!wasDeferred) ArmDeferredHeap();
            throw;
        }
        finally
        {
            _deferHeap = wasDeferred;
        }
    }

    private void RemoveSegmentCounts(LoomSeg seg, HashSet<long> touched, ref long symbols)
    {
        for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i])
        {
            symbols++;
            int j = _nxt[i];
            if (j >= 0 && !_dead[j] && _sym[i] != _bar && _sym[j] != _bar)
            {
                long key = Pack(_sym[i], _sym[j]); touched.Add(key); Dec(key, i, _wt[i]);
            }
        }
    }

    private void AddSegmentCounts(LoomSeg seg, HashSet<long> touched, ref long symbols)
    {
        for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i])
        {
            symbols++;
            int j = _nxt[i];
            if (j >= 0 && !_dead[j] && _sym[i] != _bar && _sym[j] != _bar)
            {
                long key = Pack(_sym[i], _sym[j]); touched.Add(key); Inc(key, i, _wt[i]);
            }
        }
    }

    private void ReturnArena()
    {
        if (_sym.Length > 0) ArrayPool<int>.Shared.Return(_sym);
        if (_nxt.Length > 0) ArrayPool<int>.Shared.Return(_nxt);
        if (_prv.Length > 0) ArrayPool<int>.Shared.Return(_prv);
        if (_dead.Length > 0) ArrayPool<bool>.Shared.Return(_dead);
        if (_wt.Length > 0) ArrayPool<byte>.Shared.Return(_wt);
    }

    // Splice the WHOLE VIEW (residents + shed) in id-ascending order, resolving bytes through the tape's log-backed
    // read path — the rebase/load body. Dropped ids are simply absent from the view: their counts vanish here,
    // which IS how a dream-drop takes effect on the grammar (deterministically, at the sleep cadence).
    private void SpliceView(Tape tape, long idBelow)
    {
        Stopwatch clock = Stopwatch.StartNew();
        var ids = new List<long>(tape.Count + tape.ShedEventIDs.Count);
        foreach (var u in tape.GetGrammarEventViews()) if (u.Id.Value < idBelow) ids.Add(u.Id.Value);
        long collectMs = clock.ElapsedMilliseconds;
        ids.Sort();
        long sortMs = clock.ElapsedMilliseconds - collectMs;
        foreach (long v in ids)
        {
            if (!tape.Resolve(new TapeEventID(v), out var bytes)) throw new InvalidOperationException($"loom: view event {v} did not resolve — tape/log skew");
            SpliceEvent(bytes, v, tape.IsEvidence(new TapeEventID(v)) ? (byte)_wScale : (byte)1);
        }
        long spliceMs = clock.ElapsedMilliseconds - collectMs - sortMs;
        if (spliceMs >= 5 || ids.Count >= 1_000)
            global::Cogito.Trace.Cortex.Boundary("loom.load.view", $"events={ids.Count} collect_ms={collectMs} sort_ms={sortMs} splice_ms={spliceMs}");
    }

    // encoder scratch — rented per splice, released after the arena copy (fields so Encode can hand the
    // linked result to SpliceEvent without an extra materialization)
    private int[] _encSym = [], _encNxt = [], _encPrv = [];
    private bool[] _encDead = [];
    private int Encode(ReadOnlySpan<byte> bytes, out int head)
    {
        int n = bytes.Length;
        _encSym = ArrayPool<int>.Shared.Rent(n);
        _encNxt = ArrayPool<int>.Shared.Rent(n);
        _encPrv = ArrayPool<int>.Shared.Rent(n);
        _encDead = ArrayPool<bool>.Shared.Rent(n);
        for (int i = 0; i < n; i++) { _encSym[i] = bytes[i]; _encNxt[i] = i + 1 < n ? i + 1 : -1; _encPrv[i] = i - 1; _encDead[i] = false; }

        _encq.Clear();
        for (int i = 0; i + 1 < n; i++)
            if (_rank.TryGetValue(Pack(bytes[i], bytes[i + 1]), out var r)) _encq.Enqueue(i, (r.Rank, i));

        int live = n;
        while (_encq.TryDequeue(out int i, out var pr))
        {
            if (_encDead[i]) continue;
            int j = _encNxt[i];
            if (j < 0 || _encDead[j]) continue;
            if (!_rank.TryGetValue(Pack(_encSym[i], _encSym[j]), out var r) || r.Rank != pr.Rank) continue;   // pair changed since push — its current form rides its own entry
            _encSym[i] = (int)_alphabet + r.Sym;
            _encDead[j] = true;
            int q = _encNxt[j]; _encNxt[i] = q; if (q >= 0) _encPrv[q] = i;
            live--;
            int p = _encPrv[i];
            if (p >= 0 && _rank.TryGetValue(Pack(_encSym[p], _encSym[i]), out var rp)) _encq.Enqueue(p, (rp.Rank, p));
            if (q >= 0 && _rank.TryGetValue(Pack(_encSym[i], _encSym[q]), out var rq)) _encq.Enqueue(i, (rq.Rank, i));
        }
        head = 0;                                            // slot 0 never dies (a merge kills the RIGHT half)
        return live;
    }
    private void ReleaseEncodeScratch()
    {
        if (_encSym.Length == 0) return;
        ArrayPool<int>.Shared.Return(_encSym); _encSym = [];
        ArrayPool<int>.Shared.Return(_encNxt); _encNxt = [];
        ArrayPool<int>.Shared.Return(_encPrv); _encPrv = [];
        ArrayPool<bool>.Shared.Return(_encDead); _encDead = [];
    }

    private void EnsureArena(int need)
    {
        if (need > _sym.Length)
        {
            int cap = Math.Max(Math.Max(_sym.Length * 2, need), 1024);
            Grow(ref _sym, cap); Grow(ref _nxt, cap); Grow(ref _prv, cap);
            var dead = ArrayPool<bool>.Shared.Rent(cap);
            Array.Copy(_dead, dead, _len);
            if (_dead.Length > 0) ArrayPool<bool>.Shared.Return(_dead);
            _dead = dead;
            var wt = ArrayPool<byte>.Shared.Rent(cap);
            Array.Copy(_wt, wt, _len);
            if (_wt.Length > 0) ArrayPool<byte>.Shared.Return(_wt);
            _wt = wt;
        }
        if (need > _occIndex.Length)
        {
            int cap = Math.Max(Math.Max(_occIndex.Length * 2, need), 1024);
            var occIndex = new int[cap];
            Array.Copy(_occIndex, occIndex, _len);
            Array.Fill(occIndex, -1, _len, cap - _len);
            _occIndex = occIndex;
        }
    }
    private void Grow(ref int[] arr, int cap)
    {
        var next = ArrayPool<int>.Shared.Rent(cap);
        Array.Copy(arr, next, _len);
        if (arr.Length > 0) ArrayPool<int>.Shared.Return(arr);
        arr = next;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  PUMP — the RePair winner loop verbatim over the persistent state
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Drain every winner past the mint bar: pop (max count, smallest key), gate on 3·wScale and Δmdl ≥ tau, mint,
    /// rewrite the occurrence sites, fix neighbor counts. `events`, when armed, receives one MergeEvent per mint in
    /// decision order (the thought stream — only the mints THIS pump performs, not a from-scratch replay). Returns
    /// the physical heap operations and distinct rearmed keys for this pump, so callers can account for winner work.
    public LoomMutationReceipt Pump(List<MergeEvent>? events = null, Mbits tau = default)
    {
        int rulesBefore = _rules.Count;
        long mutationsBefore = _heapMutationCount;
        int changedBefore = _heapChangedKeyCount;
        long touchedSymbols = 0;
        while (_live >= 2)
        {
            if (!TryPopHeap(out var winner)) break;
            long bestKey = winner.Key;
            int bestCount = winner.Count;
            if (!_cnt.TryGetValue(bestKey, out int currentCount) || currentCount != bestCount)
                throw new InvalidOperationException($"loom: indexed heap/count plane diverged for key {bestKey}: heap {bestCount}, count {currentCount}");

            int vocab = (int)_alphabet + _rules.Count;
            Mbits delta = Mdl.PairDelta(bestCount, vocab, _wScale);
            if (bestCount < 3 * _wScale || delta < tau) { Push(bestKey, bestCount); break; }   // re-arm the popped winner — the heap persists across pumps

            int a = (int)(bestKey >> 32), b = (int)(bestKey & 0xFFFFFFFFL);
            int ruleIdx = _rules.Count;
            uint n = _alphabet + (uint)ruleIdx;
            if (!_occ.TryGetValue(bestKey, out var positions) || positions.Count == 0)
                throw new InvalidOperationException($"loom: winner {bestKey} has count {bestCount} but no active occurrence postings");
            _uses.Add(0);                                          // the mint's tape references accrete during the rewrite below

            // A winner rewrite can touch the same neighbor digram once per occurrence. Keep the count plane exact
            // at every site, but defer heap movement until this winner transaction has reached its final priorities.
            // The heap therefore pays once per touched key, not once per occurrence of a high-frequency winner.
            bool wasDeferred = _deferHeap;
            _deferHeap = true;
            try
            {
                int candidateCount = positions.Count;
                int[] candidates = ArrayPool<int>.Shared.Rent(candidateCount);
                try
                {
                    positions.CopyTo(candidates, 0);
                    Array.Sort(candidates, 0, candidateCount);          // arena order: within a span left-to-right; across spans the merges commute
                    for (int candidate = 0; candidate < candidateCount; candidate++)
                    {
                        int i = candidates[candidate];
                        if (_occIndex[i] < 0) continue;                 // an overlapping earlier rewrite consumed this site
                        if (_dead[i]) throw new InvalidOperationException($"loom: dead active occurrence at slot {i} for key {bestKey}");
                        int j = _nxt[i];
                        if (j < 0 || _dead[j] || _sym[i] != a || _sym[j] != b)
                            throw new InvalidOperationException($"loom: active occurrence at slot {i} no longer matches key {bestKey}");
                        int p = _prv[i], q = _nxt[j];
                        int mw = _wt[i];                                                   // ONE weight fetch serves the whole site (span-constant weights, the barrier law)
                        if (p >= 0 && _sym[p] != _bar) Dec(Pack(_sym[p], a), p, mw);
                        if (q >= 0 && _sym[q] != _bar) Dec(Pack(b, _sym[q]), j, mw);
                        Dec(bestKey, i, mw);
                        _sym[i] = (int)n; _dead[j] = true; _nxt[i] = q; if (q >= 0) _prv[q] = i;
                        _live--; _weightedLive -= mw;
                        if (p >= 0 && _sym[p] != _bar) Inc(Pack(_sym[p], (int)n), p, mw);
                        if (q >= 0 && _sym[q] != _bar) Inc(Pack((int)n, _sym[q]), i, mw);
                        if (a >= (int)_alphabet) _uses[a - (int)_alphabet]--;              // consumed tape references…
                        if (b >= (int)_alphabet) _uses[b - (int)_alphabet]--;
                        _uses[ruleIdx]++;                                                  // …become one reference to the mint
                        touchedSymbols++;
                    }
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(candidates);
                }
                if (_occ.ContainsKey(bestKey)) throw new InvalidOperationException($"loom: rewrite left active postings for key {bestKey}");
            }
            finally
            {
                _deferHeap = wasDeferred;
                if (!wasDeferred) ArmDeferredHeap();
            }

            Span<byte> ccc = stackalloc byte[16];
            var cw = new CccWriter(ccc);
            cw.U64(2); cw.U32((uint)a); cw.U32((uint)b);
            RuleID id = Hash.Rule(ccc[..cw.Written]);
            Mbits cost = new(256 + 8000L * cw.Written);
            _rules.Add(new GrammarRule(id, [new Symbol((uint)a), new Symbol((uint)b)], cost));
            _rulesArr = null;
            _rank.Add(bestKey, new RankEntry(_rankSeq++, ruleIdx));   // Add throws on a duplicate — the standing-invariant tripwire (an entry digram can never re-form)
            _entries.Add(new LoomEntry(Alias: false, (uint)a, (uint)b, ruleIdx));
            if (a >= (int)_alphabet) _uses[a - (int)_alphabet]++;  // the pattern's own references
            if (b >= (int)_alphabet) _uses[b - (int)_alphabet]++;
            _savings += delta.Value;

            int di = (int)_alphabet;
            int da = a < di ? 0 : _depth[a - di], db = b < di ? 0 : _depth[b - di];
            int sa = a < di ? 1 : _span[a - di],  sb = b < di ? 1 : _span[b - di];
            _depth.Add(1 + Math.Max(da, db)); _span.Add(sa + sb);
            events?.Add(new MergeEvent(events.Count, new Symbol((uint)a), new Symbol((uint)b), new Symbol(n), bestCount, _depth[^1], _span[^1], delta));
        }
        if (_rules.Count != rulesBefore) InvalidateResults();
        return new LoomMutationReceipt(
            0, 0, 0, 0, 0, touchedSymbols, _heapChangedKeyCount - changedBefore, 0, _rules.Count - rulesBefore,
            _heapMutationCount - mutationsBefore, _heapChangedKeyCount - changedBefore);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  HARVEST — the loom's grammar as a RePairResult (rules identity-stable between mints; compressed fresh)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Harvest grammar input in VIEW ORDER (residents in current tape order, shed spans in id order).
    /// Every grammar-role view span must already be spliced (SpliceNew before Result). Non-grammar spans remain
    /// custody-only and are excluded. Shed grammar spans STAY in the harvest: their
    /// parsed forms (one symbol each, the shed criterion) keep the compressed sequence, the rule-use census, and
    /// the criticality reads (meanz/cvz ride RuleUses) IDENTICAL under shedding — only the raw bytes moved out.
    public RePairResult Result(Tape tape)
    {
        long tapeRevision = tape.Revision.Value;
        long tapeOrderRevision = tape.OrderRevision.Value;
        if (_cachedTapeResult is RePairResult cached
            && ReferenceEquals(_cachedTape, tape)
            && _cachedTapeRevision == tapeRevision
            && _cachedTapeOrderRevision == tapeOrderRevision
            && _cachedTapeMutationRevision == _mutationRevision)
            return cached;

        var compressed = _harvest;                                          // reused across steps — clear+refill keeps the backing array (Result runs every stride)
        compressed.Clear();
        compressed.EnsureCapacity((int)Math.Min(int.MaxValue, _live + tape.GrammarByteLength));
        foreach (TapeEventView view in tape.GetGrammarEventViews())
            EmitSegOf(view.Id.Value, compressed);
        RePairResult result = Compose(compressed);
        _cachedTape = tape;
        _cachedTapeRevision = tapeRevision;
        _cachedTapeOrderRevision = tapeOrderRevision;
        _cachedTapeMutationRevision = _mutationRevision;
        _cachedTapeResult = result;
        return result;
    }

    /// Harvest in SPLICE ORDER — the batch path (Engine's one-shot entries), where splice order IS corpus order.
    public RePairResult Result()
    {
        if (_cachedSpliceResult is RePairResult cached && _cachedSpliceMutationRevision == _mutationRevision)
            return cached;
        var compressed = _harvest;
        compressed.Clear();
        compressed.EnsureCapacity((int)Math.Min(int.MaxValue, _live + _segs.Count));
        for (int si = 0; si < _segs.Count; si++) EmitSeg(si, compressed);
        RePairResult result = Compose(compressed);
        _cachedSpliceMutationRevision = _mutationRevision;
        _cachedSpliceResult = result;
        return result;
    }

    private void InvalidateResults()
    {
        _mutationRevision++;
        _cachedTapeResult = null;
        _cachedSpliceResult = null;
    }

    private void EmitSegOf(long id, List<Symbol> outp)
    {
        if (!_segOf.TryGetValue(id, out int si))
            throw new InvalidOperationException($"loom: view span s{id} not spliced — SpliceNew must run before Result");
        EmitSeg(si, outp);
    }

    private void EmitSeg(int si, List<Symbol> outp)
    {
        var seg = _segs[si];
        if (!seg.Present) return;
        if (seg.Len > 0)
            for (int k = seg.Off; k >= 0; k = _nxt[k]) outp.Add(new Symbol((uint)_sym[k]));   // seg.Off never dies
        for (int b2 = 0; b2 < seg.Bars; b2++) outp.Add(new Symbol((uint)_bar));
    }

    private RePairResult Compose(List<Symbol> compressed)
    {
        _rulesArr ??= _rules.ToArray();
        return new RePairResult(_rulesArr, compressed.ToArray(), new Mbits(_savings), _alphabet);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  REBASE — adopt an externally-induced PURE grammar (+ compaction aliases) and re-parse the world through it
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Adopt `pure` (fresh batch Re-Pair output over THIS tape's VIEW — the sleep's global re-greed, optionally
    /// SelfCompress.Compact'ed) as the standing grammar: reset every plane, install the rules, install the
    /// compaction `aliases` (rank-ordered after the rules — any deterministic rank order yields an exact parse;
    /// see the arena-is-a-pure-function law), rank-encode every view span through them (id order), retally.
    /// O(view) — paid at the sleep cadence, where the night already pays its own O(view) re-greed. This is what
    /// bounds the greedy-in-arrival MDL drift AND what re-prices vest transitions (the vest-reweigh hook) AND what
    /// compacts the arena's dead slots. The night's GC/breach/slot layers ride the RETURNED grammar only — the
    /// loom is the pure Re-Pair layer, exactly what a day re-induce re-derives on the batch arm.
    public void Rebase(Tape tape, in RePairResult pure, IReadOnlyList<RankAlias>? aliases = null)
    {
        _checkpointReset = true;
        InvalidateResults();
        _cnt.Clear(); _occ.Clear(); ClearHeap(); _deferredHeapKeys.Clear(); _rank.Clear(); _entries.Clear(); _rankSeq = 0;
        _rules.Clear(); _depth.Clear(); _span.Clear(); _uses.Clear();
        _segs.Clear(); _segOf.Clear();
        _activeSegs = 0; _len = 0; _live = 0; _weightedLive = 0; _savings = 0; _idMark = 0; _rulesArr = null;
        if (_occIndex.Length > 0) Array.Fill(_occIndex, -1);

        var rules = pure.Rules ?? [];
        for (int i = 0; i < rules.Length; i++)
        {
            var pat = rules[i].Pattern;
            if (pat.Length != 2)
                throw new ArgumentException($"loom.Rebase adopts PURE Re-Pair output only — rule {i} has a {pat.Length}-ary pattern (slot/breach layers cannot seed the rank-encoder)");
            _rules.Add(rules[i]);
            PushRuleMeta(i, (int)pat[0].Value, (int)pat[1].Value);
        }
        if (aliases is not null)
            foreach (var al in aliases)
            {
                if (al.Rule < 0 || al.Rule >= _rules.Count)
                    throw new ArgumentException($"loom.Rebase alias ({al.A},{al.B}) targets rule {al.Rule} of {_rules.Count}");
                _rank.Add(Pack((int)al.A, (int)al.B), new RankEntry(_rankSeq++, al.Rule));   // Add throws on collision — an alias may never shadow a rule digram
                _entries.Add(new LoomEntry(Alias: true, al.A, al.B, al.Rule));
            }
        _savings = pure.TotalSavings.Value;
        SpliceView(tape, long.MaxValue);
        _idMark = tape.NextId;
    }

    /// RE-SPLICE the whole view through the STANDING grammar — the O(view·log) vest-reweigh + drop-retire hook,
    /// the night's replacement for the O(view²) batch re-greed. Where Rebase ADOPTS a fresh (externally-induced)
    /// rule table, Resplice keeps the loom's OWN rules + aliases and only re-parses the world through them at
    /// CURRENT evidence status — so a vested event re-enters at wScale, a dropped event vanishes (absent from
    /// GetEventViews), and the arena's dead slots compact. It is EXACTLY the state a fresh Load re-derives (Load's body
    /// is rule-replay + this same SpliceView), so resume stays byte-identical without ever paying pump-from-zero.
    /// The standing alias plane survives untouched — the compaction never oscillates. Only the count/arena/segment
    /// planes reset; _uses re-derives its PATTERN references from the kept rules, then SpliceView re-adds the tape
    /// references (the same two-source tally PushRuleMeta+SpliceEvent build on Rebase).
    public void Resplice(Tape tape)
    {
        InvalidateResults();
        _cnt.Clear(); _occ.Clear(); ClearHeap(); _deferredHeapKeys.Clear();
        _segs.Clear(); _segOf.Clear();
        _activeSegs = 0; _len = 0; _live = 0; _weightedLive = 0; _idMark = 0; _rulesArr = null;
        if (_occIndex.Length > 0) Array.Fill(_occIndex, -1);

        int di = (int)_alphabet;
        for (int i = 0; i < _uses.Count; i++) _uses[i] = 0;              // reset the two-source tally…
        for (int i = 0; i < _rules.Count; i++)                           // …re-derive PATTERN references from the kept rules
        {
            var pat = _rules[i].Pattern;
            int a = (int)pat[0].Value, b = (int)pat[1].Value;
            if (a >= di) _uses[a - di]++;
            if (b >= di) _uses[b - di]++;
        }
        SpliceView(tape, long.MaxValue);                                // …SpliceView re-adds the TAPE references (post-vest weight, post-drop view)
        _idMark = tape.NextId;
    }

    /// SEED a PRETRAINED grammar as the standing rule table WITHOUT a tape re-parse — the transfer-learning entry
    /// (nav-v1's pretrained RepoGrok): install `base`'s rules into every plane so subsequent SpliceEvent calls
    /// rank-encode new material THROUGH the trained vocabulary (the repo inducts against a mind that already knows
    /// code+prose), then Pump mints only the repo-specific deltas ON TOP. Unlike Rebase there is no view to
    /// re-splice — the loom starts empty of events, the base only preloads the rank-encoder's program. Requires a
    /// FRESH loom and PURE emission-ordered binary Re-Pair rules (the same contract as Rebase; a base carrying
    /// slot/breach/demoted bodies must be Compact'ed or re-induced to binary first — reported loud so the caller
    /// sees the exact rule). Aliases are the compaction entries (rank-ordered after the rules).
    public void Seed(in RePairResult @base, IReadOnlyList<RankAlias>? aliases = null)
    {
        if (_rules.Count != 0 || _len != 0 || _segs.Count != 0) throw new InvalidOperationException("Loom.Seed requires a fresh loom");
        _checkpointReset = true;
        InvalidateResults();
        if (@base.AlphabetSize != _alphabet) throw new ArgumentException($"loom.Seed alphabet skew: base {@base.AlphabetSize} vs loom {_alphabet}");
        var rules = @base.Rules ?? [];
        for (int i = 0; i < rules.Length; i++)
        {
            var pat = rules[i].Pattern;
            if (rules[i].Kind != RuleBodyKind.Expansion || pat.Length != 2)
                throw new ArgumentException($"loom.Seed adopts PURE binary Re-Pair rules only — rule {i} is {rules[i].Kind} arity {pat.Length} (a consolidated base must be flattened to binary first)");
            _rules.Add(rules[i]);
            PushRuleMeta(i, (int)pat[0].Value, (int)pat[1].Value);
        }
        if (aliases is not null)
            foreach (var al in aliases)
            {
                if (al.Rule < 0 || al.Rule >= _rules.Count) throw new ArgumentException($"loom.Seed alias ({al.A},{al.B}) targets rule {al.Rule} of {_rules.Count}");
                _rank.Add(Pack((int)al.A, (int)al.B), new RankEntry(_rankSeq++, al.Rule));
                _entries.Add(new LoomEntry(Alias: true, al.A, al.B, al.Rule));
            }
        _savings = @base.TotalSavings.Value;
    }

    /// Rank map + RG metadata + usage slots for rule `i` = (a,b) — shared by the mint-free adoption paths
    /// (Rebase/Load/Seed); the pump inlines the same recurrence at mint time.
    private void PushRuleMeta(int i, int a, int b)
    {
        int di = (int)_alphabet;
        if ((a >= di && a - di >= i) || (b >= di && b - di >= i))
            throw new ArgumentException($"loom: rule {i} references a later nonterminal — not emission-ordered Re-Pair output");
        _rank.Add(Pack(a, b), new RankEntry(_rankSeq++, i));
        _entries.Add(new LoomEntry(Alias: false, (uint)a, (uint)b, i));
        int da = a < di ? 0 : _depth[a - di], db = b < di ? 0 : _depth[b - di];
        int sa = a < di ? 1 : _span[a - di],  sb = b < di ? 1 : _span[b - di];
        _depth.Add(1 + Math.Max(da, db)); _span.Add(sa + sb);
        _uses.Add(0);
        if (a >= di) _uses[a - di]++;
        if (b >= di) _uses[b - di]++;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  CHECKPOINT — the pattern: store only what cannot be re-derived
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    internal LoomCheckpointDelta CaptureCheckpointDelta()
    {
        int first = _checkpointReset ? 0 : _checkpointEntryCursor;
        LoomEntryDelta[] entries = new LoomEntryDelta[_entries.Count - first];
        for (int i = 0; i < entries.Length; i++)
        {
            LoomEntry entry = _entries[first + i];
            GrammarRule rule = entry.Alias ? default : _rules[entry.Sym];
            entries[i] = new LoomEntryDelta(entry.Alias, entry.A, entry.B, entry.Sym, rule.Id, rule.Cost);
        }
        return new LoomCheckpointDelta(
            _checkpointRevision,
            _mutationRevision,
            _idMark,
            _savings,
            entries,
            _checkpointReset);
    }

    internal void CommitCheckpointDelta()
    {
        _checkpointEntryCursor = _entries.Count;
        _checkpointRevision = _mutationRevision;
        _checkpointReset = false;
        _legacyWireFormat = false;
    }

    /// Apply a tape transition while replaying a typed checkpoint record. The
    /// live verb advances the runtime mutation revision on every physical
    /// touch; the checkpoint rail owns the authoritative revision and sets it
    /// when the standing entry delta is committed, so preserve the caller's
    /// parent revision across this arena-only phase.
    internal void ApplyTapeDeltaForCheckpoint(Tape tape, in TapeDelta delta)
    {
        long revision = _mutationRevision;
        ApplyTapeDelta(tape, in delta);
        _mutationRevision = revision;
        _cachedTapeResult = null;
        _cachedSpliceResult = null;
    }

    internal void ApplyCheckpointDelta(in LoomCheckpointDelta delta, bool applyArenaEntries = false)
    {
        if (delta.ParentRevision != _mutationRevision)
            throw new InvalidDataException($"loom checkpoint delta parent revision {delta.ParentRevision} disagrees with {_mutationRevision}");
        if (delta.Reset)
        {
            _cnt.Clear(); _occ.Clear(); ClearHeap(); _deferredHeapKeys.Clear(); _rank.Clear(); _entries.Clear(); _rankSeq = 0;
            _rules.Clear(); _depth.Clear(); _span.Clear(); _uses.Clear(); _segs.Clear(); _segOf.Clear();
            _activeSegs = 0; _len = 0; _live = 0; _weightedLive = 0; _rulesArr = null;
            if (_occIndex.Length > 0) Array.Fill(_occIndex, -1);
        }
        foreach (LoomEntryDelta entry in delta.Entries)
        {
            long key = Pack((int)entry.A, (int)entry.B);
            if (entry.Alias)
            {
                if (entry.Sym < 0 || entry.Sym >= _rules.Count)
                    throw new InvalidDataException($"loom checkpoint alias targets rule {entry.Sym} of {_rules.Count}");
                _rank.Add(key, new RankEntry(_rankSeq++, entry.Sym));
                _entries.Add(new LoomEntry(true, entry.A, entry.B, entry.Sym));
            }
            else
            {
                if (entry.Sym != _rules.Count)
                    throw new InvalidDataException($"loom checkpoint rule index {entry.Sym} disagrees with {_rules.Count}");
                Symbol[] pattern = [new Symbol(entry.A), new Symbol(entry.B)];
                if (applyArenaEntries)
                    ApplyCheckpointRule(entry);
                _rules.Add(new GrammarRule(entry.RuleID, pattern, entry.Cost));
                if (!applyArenaEntries)
                    PushRuleMeta(_rules.Count - 1, (int)entry.A, (int)entry.B);
                else
                {
                    int ruleIdx = _rules.Count - 1;
                    int di = (int)_alphabet;
                    int a = (int)entry.A, b = (int)entry.B;
                    _rank.Add(Pack(a, b), new RankEntry(_rankSeq++, ruleIdx));
                    _entries.Add(new LoomEntry(false, entry.A, entry.B, ruleIdx));
                    if (a >= di) _uses[a - di]++;
                    if (b >= di) _uses[b - di]++;
                    int da = a < di ? 0 : _depth[a - di], db = b < di ? 0 : _depth[b - di];
                    int sa = a < di ? 1 : _span[a - di], sb = b < di ? 1 : _span[b - di];
                    _depth.Add(1 + Math.Max(da, db)); _span.Add(sa + sb);
                }
            }
        }
        _idMark = delta.IDMark;
        _savings = delta.Savings;
        _rulesArr = null;
        _cachedTapeResult = null;
        _cachedSpliceResult = null;
        _mutationRevision = delta.Revision;
        _checkpointRevision = delta.Revision;
        _checkpointEntryCursor = _entries.Count;
        _checkpointReset = false;
    }

    /// Replay one emitted non-alias rule against the standing arena. The
    /// checkpoint entry contains the same winner pair that Pump consumed; its
    /// occurrence postings therefore identify the exact rewrite sites without
    /// a tape-wide reparse.
    private void ApplyCheckpointRule(in LoomEntryDelta entry)
    {
        int a = checked((int)entry.A), b = checked((int)entry.B);
        long key = Pack(a, b);
        if (!_occ.TryGetValue(key, out List<int>? positions) || positions.Count == 0)
            throw new InvalidDataException($"loom checkpoint rule ({a},{b}) has no live arena occurrences");

        int ruleIdx = _rules.Count;
        uint symbol = _alphabet + (uint)ruleIdx;
        if (symbol > int.MaxValue) throw new InvalidDataException("loom checkpoint rule symbol exceeds arena range");
        _uses.Add(0);
        bool wasDeferred = _deferHeap;
        _deferHeap = true;
        try
        {
            int count = positions.Count;
            int[] candidates = ArrayPool<int>.Shared.Rent(count);
            try
            {
                positions.CopyTo(candidates, 0);
                Array.Sort(candidates, 0, count);
                for (int n = 0; n < count; n++)
                {
                    int i = candidates[n];
                    if (_occIndex[i] < 0) continue;
                    if (_dead[i]) throw new InvalidDataException($"loom checkpoint rule touches dead arena slot {i}");
                    int j = _nxt[i];
                    if (j < 0 || _dead[j] || _sym[i] != a || _sym[j] != b)
                        throw new InvalidDataException($"loom checkpoint rule ({a},{b}) no longer matches arena slot {i}");
                    int p = _prv[i], q = _nxt[j], weight = _wt[i];
                    if (p >= 0 && _sym[p] != _bar) Dec(Pack(_sym[p], a), p, weight);
                    if (q >= 0 && _sym[q] != _bar) Dec(Pack(b, _sym[q]), j, weight);
                    Dec(key, i, weight);
                    _sym[i] = (int)symbol; _dead[j] = true; _nxt[i] = q; if (q >= 0) _prv[q] = i;
                    _live--; _weightedLive -= weight;
                    if (p >= 0 && _sym[p] != _bar) Inc(Pack(_sym[p], (int)symbol), p, weight);
                    if (q >= 0 && _sym[q] != _bar) Inc(Pack((int)symbol, _sym[q]), i, weight);
                    if (a >= (int)_alphabet) _uses[a - (int)_alphabet]--;
                    if (b >= (int)_alphabet) _uses[b - (int)_alphabet]--;
                    _uses[ruleIdx]++;
                }
            }
            finally { ArrayPool<int>.Shared.Return(candidates); }
            if (_occ.ContainsKey(key)) throw new InvalidDataException($"loom checkpoint rule ({a},{b}) left active postings");
        }
        finally
        {
            _deferHeap = wasDeferred;
            if (!wasDeferred) ArmDeferredHeap();
        }
    }

    internal static void WriteCheckpointDelta(CkptWriter w, in LoomCheckpointDelta delta)
    {
        const int maxEntries = 1_000_000;
        w.U8(1); w.I64(delta.ParentRevision); w.I64(delta.Revision); w.I64(delta.IDMark); w.I64(delta.Savings); w.Bool(delta.Reset);
        if (delta.Entries.Length > maxEntries) throw new InvalidDataException("loom checkpoint delta exceeds entry bound");
        w.I32(delta.Entries.Length);
        foreach (LoomEntryDelta entry in delta.Entries)
        {
            w.Bool(entry.Alias); w.U32(entry.A); w.U32(entry.B); w.I32(entry.Sym);
            w.Raw(entry.RuleID.Hash.AsSpan()); w.I64(entry.Cost.Value);
        }
    }

    internal static LoomCheckpointDelta ReadCheckpointDelta(CkptReader r)
    {
        const int maxEntries = 1_000_000;
        if (r.U8() != 1) throw new InvalidDataException("unknown loom checkpoint delta version");
        long parent = r.I64(), revision = r.I64(), idMark = r.I64(), savings = r.I64(); bool reset = r.Bool();
        int count = r.I32();
        if (count < 0 || count > maxEntries) throw new InvalidDataException("loom checkpoint entry count is invalid");
        var entries = new LoomEntryDelta[count];
        for (int i = 0; i < count; i++)
            entries[i] = new LoomEntryDelta(r.Bool(), r.U32(), r.U32(), r.I32(), new RuleID(Hash256.From(r.Raw(32))), new Mbits(r.I64()));
        return new LoomCheckpointDelta(parent, revision, idMark, savings, entries, reset);
    }

    private const int MaxCheckpointArenaSlots = 100_000_000;
    private const int MaxCheckpointSegments = 1_000_000;

    /// Serialize the entry journal and the standing parsed arena. The count/occurrence/heap planes remain derived;
    /// retaining the arena removes the load-time rank-encoding wall while keeping one typed authority for the
    /// continuation. Legacy v2 images are retained byte-for-byte until a checkpoint commit upgrades their wire.
    public void Save(CkptWriter w)
    {
        byte version = _legacyWireFormat ? (byte)2 : (byte)3;
        w.U8(version);
        w.I64(_idMark);
        w.I64(_savings);
        w.I64(_mutationRevision); w.I64(_checkpointRevision);
        w.I32(_entries.Count);
        foreach (var e in _entries)
        {
            w.U8(e.Alias ? (byte)1 : (byte)0);
            w.U32(e.A); w.U32(e.B);
            if (e.Alias) w.I32(e.Sym);                       // a rule entry's Sym is its mint position — implicit
            else if (version == 3)
            {
                GrammarRule rule = _rules[e.Sym];
                w.Raw(rule.Id.Hash.AsSpan()); w.I64(rule.Cost.Value);
            }
        }
        if (version == 2) return;

        if (_len < 0 || _len > MaxCheckpointArenaSlots) throw new InvalidDataException("loom checkpoint arena exceeds bound");
        w.I32(_len); w.I64(_live); w.I64(_weightedLive);
        for (int i = 0; i < _len; i++)
        {
            if (_nxt[i] < -1 || _nxt[i] >= _len) throw new InvalidDataException($"loom checkpoint next link {i} is outside arena");
            w.U32((uint)_sym[i]); w.I32(_nxt[i]); w.U8(_wt[i]);
        }
        if (_segs.Count > MaxCheckpointSegments) throw new InvalidDataException("loom checkpoint segment count exceeds bound");
        w.I32(_segs.Count);
        foreach (LoomSeg seg in _segs)
        {
            int liveLen = 0;
            if (seg.Present)
            {
                for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i])
                {
                    if (++liveLen > _len) throw new InvalidDataException($"loom checkpoint segment {seg.Id} chain is cyclic");
                }
            }
            w.I64(seg.Id); w.I32(seg.Off); w.I32(seg.Len); w.I32(liveLen); w.I32(seg.Bars); w.Bool(seg.Present);
        }
    }

    /// Restore into a fresh loom. v3 adopts the typed arena and rebuilds only derived planes; v2 falls back to the
    /// historical rank-encoding path and remains byte-identical for the legacy Vow.
    public void Load(CkptReader r, Tape tape)
    {
        Stopwatch clock = Stopwatch.StartNew();
        if (_rules.Count != 0 || _len != 0 || _segs.Count != 0) throw new InvalidOperationException("Loom.Load requires a fresh loom");
        InvalidateResults();
        byte version = r.U8();
        if (version is not (2 or 3)) throw new InvalidDataException("unsupported loom checkpoint section");
        _legacyWireFormat = version == 2;
        long idMark = r.I64();
        _savings = r.I64();
        _mutationRevision = r.I64();
        long checkpointRevision = r.I64();
        _checkpointRevision = checkpointRevision;
        if (_mutationRevision < 0 || _checkpointRevision < 0 || _checkpointRevision > _mutationRevision)
            throw new InvalidDataException("loom checkpoint revisions are not monotonic");
        HashSet<Hash256>? ruleIDs = version == 3 ? new HashSet<Hash256>() : null;
        int n = r.I32();
        for (int i = 0; i < n; i++)
        {
            bool alias = r.U8() != 0;
            int a = (int)r.U32(), b = (int)r.U32();
            if (alias)
            {
                int sym = r.I32();
                if (sym < 0 || sym >= _rules.Count) throw new InvalidDataException($"loom checkpoint alias entry {i} targets rule {sym} of {_rules.Count}");
                _rank.Add(Pack(a, b), new RankEntry(_rankSeq++, sym));
                _entries.Add(new LoomEntry(Alias: true, (uint)a, (uint)b, sym));
            }
            else
            {
                var pattern = new Symbol[] { new((uint)a), new((uint)b) };
                GrammarRule rule = version == 3
                    ? new GrammarRule(new RuleID(Hash256.From(r.RawExact(32))), pattern, new Mbits(r.I64()))
                    : new GrammarRule(GrammarRule.ComputeId(pattern), pattern, new Mbits(256 + 8000L * 16));
                if (version == 3 && !ruleIDs!.Add(rule.Id.Hash)) throw new InvalidDataException($"loom checkpoint rule {i} has duplicate identity");
                _rules.Add(rule);
                PushRuleMeta(_rules.Count - 1, a, b);
            }
        }
        long entryMs = clock.ElapsedMilliseconds;
        if (entryMs >= 5 || n >= 1_000)
            global::Cogito.Trace.Cortex.Boundary("loom.load.entries", $"entries={n} rules={_rules.Count} aliases={_entries.Count - _rules.Count} ms={entryMs}");
        if (idMark < 0 || idMark > tape.NextId) throw new InvalidDataException($"loom checkpoint splice mark {idMark} exceeds the tape's id high-water {tape.NextId} — tape/loom skew");
        if (version == 3)
        {
            LoadArena(r, tape, idMark, clock, checkpointRevision);
            return;
        }

        long save = _savings;                                  // SpliceView mints nothing (no Pump) but keep the tally explicit
        SpliceView(tape, idMark);
        long rebuildMs = clock.ElapsedMilliseconds - entryMs;
        if (rebuildMs >= 5 || _len >= 10_000)
            global::Cogito.Trace.Cortex.Boundary("loom.load.rebuild", $"id_mark={idMark} arena={_len} live={_live} count_keys={_cnt.Count} occurrence_posts={_occ.Values.Sum(static p => p.Count)} ms={rebuildMs}");
        _idMark = idMark;
        _savings = save;
        _checkpointEntryCursor = _entries.Count;
        // SpliceView rebuilds only derived arena/count planes. It must not
        // advance the persisted mutation checkpoint boundary; retaining the
        // serialized revision is what keeps Save∘Load∘Save byte-identical.
        _checkpointRevision = checkpointRevision;
        _checkpointReset = false;
    }

    private void LoadArena(CkptReader r, Tape tape, long idMark, Stopwatch clock, long checkpointRevision)
    {
        int len = r.I32();
        long persistedLive = r.I64(), persistedWeightedLive = r.I64();
        if (len < 0 || len > MaxCheckpointArenaSlots || persistedLive < 0 || persistedWeightedLive < 0)
            throw new InvalidDataException("loom checkpoint arena header is malformed");
        EnsureArena(len);
        _len = len;
        for (int i = 0; i < len; i++)
        {
            uint symbol = r.U32(); int next = r.I32(); byte weight = r.U8();
            if (symbol >= _alphabet + (uint)_rules.Count || next < -1 || next >= len || weight == 0 || weight > _wScale)
                throw new InvalidDataException($"loom checkpoint arena slot {i} is malformed");
            _sym[i] = (int)symbol; _nxt[i] = next; _wt[i] = weight; _dead[i] = true; _prv[i] = -1;
        }
        int segmentCount = r.I32();
        if (segmentCount < 0 || segmentCount > MaxCheckpointSegments) throw new InvalidDataException("loom checkpoint segment count is malformed");
        _segs.Clear(); _segOf.Clear(); _activeSegs = 0;
        long previousID = -1;
        bool[] reached = len == 0 ? [] : new bool[len];
        long live = 0, weighted = 0;
        for (int s = 0; s < segmentCount; s++)
        {
            long id = r.I64(); int off = r.I32(); int segmentLen = r.I32(); int reachableLen = r.I32(); int bars = r.I32(); bool present = r.Bool();
            if (id < 0 || id <= previousID || segmentLen < 0 || reachableLen < 0 || segmentLen < reachableLen || bars < 0 || off < 0 || off > len)
                throw new InvalidDataException($"loom checkpoint segment {s} metadata is malformed");
            previousID = id;
            if (present)
            {
                if (id >= idMark || !tape.Resolve(new TapeEventID(id), out _))
                    throw new InvalidDataException($"loom checkpoint segment {id} has no tape source custody");
                byte expectedWeight = tape.IsEvidence(new TapeEventID(id)) ? (byte)_wScale : (byte)1;
                if (segmentLen > 0 && _wt[off] != expectedWeight)
                    throw new InvalidDataException($"loom checkpoint segment {id} weight {_wt[off]} disagrees with tape evidence {expectedWeight}");
                if (_bar >= 0 && bars < 0) throw new InvalidDataException($"loom checkpoint segment {id} has malformed barriers");
                if (reachableLen > len || (segmentLen == 0 && off != 0 && off != len)) throw new InvalidDataException($"loom checkpoint empty segment {id} offset is malformed");
                int count = 0;
                for (int i = off; i >= 0 && i < len && count < reachableLen; i = _nxt[i])
                {
                    if (reached[i] || !_dead[i]) throw new InvalidDataException($"loom checkpoint segment {id} overlaps arena reachability");
                    if (_wt[i] != expectedWeight) throw new InvalidDataException($"loom checkpoint segment {id} has mixed weights");
                    reached[i] = true; _dead[i] = false; count++; live++; weighted += _wt[i];
                    if (_nxt[i] >= 0) _prv[_nxt[i]] = i;
                }
                if (count != reachableLen) throw new InvalidDataException($"loom checkpoint segment {id} chain length {count} != {reachableLen}");
                _segs.Add(new LoomSeg(id, off, segmentLen, bars, true)); _segOf.Add(id, _segs.Count - 1); _activeSegs++;
            }
            else
            {
                if (reachableLen != 0 || bars != 0) throw new InvalidDataException($"loom checkpoint inactive segment {id} carries arena state");
                _segs.Add(new LoomSeg(id, off, 0, 0, false));
            }
        }
        HashSet<long> expectedIDs = new();
        foreach (var view in tape.GetGrammarEventViews())
            if (view.Id.Value < idMark) expectedIDs.Add(view.Id.Value);
        if (expectedIDs.Count != _activeSegs || expectedIDs.Any(id => !_segOf.ContainsKey(id)))
            throw new InvalidDataException($"loom checkpoint segment census disagrees with tape view: segments={_activeSegs}, tape={expectedIDs.Count}");
        if (live != persistedLive || weighted != persistedWeightedLive)
            throw new InvalidDataException($"loom checkpoint live totals disagree: stored {persistedLive}/{persistedWeightedLive}, rebuilt {live}/{weighted}");
        _live = live; _weightedLive = weighted;
        RebuildComposedPlanes();
        long rebuildMs = clock.ElapsedMilliseconds;
        if (rebuildMs >= 5 || _len >= 10_000)
            global::Cogito.Trace.Cortex.Boundary("loom.load.rebuild", $"id_mark={idMark} arena={_len} live={_live} count_keys={_cnt.Count} occurrence_posts={_occ.Values.Sum(static p => p.Count)} ms={rebuildMs}");
        _idMark = idMark; _checkpointEntryCursor = _entries.Count; _checkpointRevision = checkpointRevision; _checkpointReset = false;
    }

    private void RebuildComposedPlanes()
    {
        _cnt.Clear(); _occ.Clear(); ClearHeap(); _deferredHeapKeys.Clear();
        if (_occIndex.Length < _len) _occIndex = new int[_len];
        Array.Fill(_occIndex, -1, 0, _len);
        _live = 0; _weightedLive = 0;
        for (int i = 0; i < _uses.Count; i++) _uses[i] = 0;
        for (int i = 0; i < _rules.Count; i++)
        {
            Symbol[] pattern = _rules[i].Pattern;
            if (pattern.Length != 2) throw new InvalidDataException($"loom checkpoint rule {i} is not binary");
            if (pattern[0].Value >= _alphabet + (uint)_rules.Count || pattern[1].Value >= _alphabet + (uint)_rules.Count)
                throw new InvalidDataException($"loom checkpoint rule {i} symbol is outside rank plane");
            if (pattern[0].Value >= _alphabet) _uses[(int)pattern[0].Value - (int)_alphabet]++;
            if (pattern[1].Value >= _alphabet) _uses[(int)pattern[1].Value - (int)_alphabet]++;
        }
        bool wasDeferred = _deferHeap; _deferHeap = true;
        try
        {
            foreach (LoomSeg seg in _segs)
            {
                if (!seg.Present) continue;
                for (int i = seg.Off; i >= 0 && i < _len && !_dead[i]; i = _nxt[i])
                {
                    _live++; _weightedLive += _wt[i];
                    if (_sym[i] >= (int)_alphabet) _uses[_sym[i] - (int)_alphabet]++;
                    int next = _nxt[i];
                    if (next >= 0 && _sym[i] != _bar && _sym[next] != _bar)
                        Inc(Pack(_sym[i], _sym[next]), i, _wt[i]);
                }
            }
        }
        finally { _deferHeap = wasDeferred; }
        if (!wasDeferred) ArmDeferredHeap();
        if (_live < 0 || _weightedLive < 0 || !ValidateActivePlanes()) throw new InvalidDataException("loom checkpoint derived planes failed validation");
    }

    /// Rebuild the parsed arena after an explicit reset/alias checkpoint delta.
    /// Ordinary emitted rules patch their occurrence postings directly; only a
    /// rank-program replacement lacks arena rewrite sites and therefore pays
    /// this one exact reparse against the final tape/rank program.
    internal void RebuildFromTape(Tape tape, long idMark)
    {
        ArgumentNullException.ThrowIfNull(tape);
        if (idMark < 0 || idMark > tape.NextId)
            throw new InvalidDataException($"loom replay splice mark {idMark} exceeds tape high-water {tape.NextId}");
        ReturnArena();
        _sym = []; _nxt = []; _prv = []; _dead = []; _wt = [];
        _occIndex = [];
        _len = 0; _live = 0; _weightedLive = 0; _activeSegs = 0;
        _segs.Clear(); _segOf.Clear(); _cnt.Clear(); _occ.Clear(); ClearHeap(); _deferredHeapKeys.Clear();
        _idMark = 0;
        _rulesArr = null; _cachedTapeResult = null; _cachedSpliceResult = null;
        for (int i = 0; i < _uses.Count; i++) _uses[i] = 0;
        for (int i = 0; i < _rules.Count; i++)
        {
            Symbol[] pattern = _rules[i].Pattern;
            if (pattern.Length != 2) throw new InvalidDataException($"loom replay rule {i} is not binary");
            if (pattern[0].Value >= _alphabet) _uses[(int)pattern[0].Value - (int)_alphabet]++;
            if (pattern[1].Value >= _alphabet) _uses[(int)pattern[1].Value - (int)_alphabet]++;
        }
        SpliceView(tape, idMark);
        _idMark = idMark;
    }

    public void Dispose()
    {
        if (_sym.Length > 0)
        {
            ArrayPool<int>.Shared.Return(_sym); _sym = [];
            ArrayPool<int>.Shared.Return(_nxt); _nxt = [];
            ArrayPool<int>.Shared.Return(_prv); _prv = [];
            ArrayPool<bool>.Shared.Return(_dead); _dead = [];
            ArrayPool<byte>.Shared.Return(_wt); _wt = [];
        }
        ReleaseEncodeScratch();
        _occIndex = [];
        _len = 0; _live = 0; _weightedLive = 0;
    }
}

/// A compaction alias — a digram whose merge target is an EXISTING rule's symbol (the canonical of its
/// expansion-identity class). Installed into the loom's rank plane at Rebase so the merged-away duplicate's
/// digram keeps merging into the canonical instead of re-accumulating counts and re-minting.
public readonly record struct RankAlias(uint A, uint B, int Rule);

/// GRAMMAR SELF-COMPRESSION — Re-Pair's output is MDL-minimal per-rule (the
/// rule-of-three floor) but NOT canonical ACROSS rules: greedy overlap resolution can mint two rules with
/// DIFFERENT patterns and IDENTICAL expansions (the same surface parsed two ways in different neighborhoods).
/// `Compact` is the night's redundancy eliminator over the pure re-greed output, three moves, all MDL-gated:
///
///   MERGE     expansion-identical rules collapse to ONE canonical (hash-consing the grammar DAG by surface).
///             The canonical is the LOWEST-INDEX member — emission-ordering FORCES this choice: every referrer
///             of a class member has a HIGHER index than that member, so only the earliest member is provably
///             before all referrers ("deepest form" would need a topological reorder; earliest is the only
///             order-safe canonical). Each merge strictly lowers |grammar| by one rule cost and leaves the
///             parse EXACTLY as expressive (the dead digram becomes a RankAlias into the canonical, so every
///             site that merged before still merges — no coverage is lost, no count re-accumulates).
///   SWEEP     0-use rules delete, to fixpoint. Fresh batch output has none (a minted rule's occurrences either
///             survive in the compressed sequence or were consumed by a deeper rule that references it), but a
///             MERGE can orphan the dead rule's children: R3=(a,R1) dying leaves R1 referenced only by R3's
///             discarded pattern. Liveness = compressed occurrence ∨ live-pattern reference ∨ alias reference
///             (an alias digram names the rule as a symbol the encoder can still produce; its target is the
///             canonical). The genuinely-unreferenced delete and the cascade re-runs (a deletion can orphan
///             deeper children) — "subsumed → inline" is deliberately NOT run: inlining makes a referrer's
///             pattern 3-ary, and binariness is the loom's rank-encode substrate (a rule costs ~10 bytes;
///             breaking the invariant to save it is a bad trade).
///   REFACTOR  (Re-Pair ON the grammar — a sub-pattern recurring across ≥3 rules' RHS becomes a shared rule.)
///             PROVABLY inert on this substrate and therefore NOT run: every pattern is binary, so the only
///             sub-pattern of length ≥2 is the whole digram, and the rank plane already guarantees digram
///             uniqueness across rules. The move exists for n-ary layers (breach templates, slots) — those ride
///             the RETURNED grammar, never the loom, so the loom-side compactor carries only the binary moves.
///
/// Deterministic throughout: grouping is (length, FNV, byte-verify), canonical choice is index order, alias
/// emission is digram-key order. The compacted grammar remains pure binary emission-ordered Re-Pair shape —
/// exactly what Loom.Rebase adopts.
public static class SelfCompress
{
    public readonly record struct Compacted(RePairResult Grammar, List<RankAlias> Aliases, int Merged, int Swept);

    public static Compacted Compact(in RePairResult pure)
    {
        var rules = pure.Rules ?? [];
        int n = rules.Length;
        int alpha = (int)pure.AlphabetSize;
        if (n == 0) return new Compacted(pure, new List<RankAlias>(), 0, 0);

        // ── expansions (Gc-style materialization — the night already pays this class of cost per pass) ──
        var exp = new byte[n][];
        for (int i = 0; i < n; i++)
            exp[i] = Reconstruct.Expand(rules, [new Symbol((uint)(alpha + i))]);

        // ── MERGE: group by (len, hash, byte-exact) → canonical = lowest index ──
        var canon = new int[n];                              // rule → its class canonical (identity for singletons)
        var byKey = new Dictionary<(int Len, int Hash), List<int>>();
        int merged = 0;
        for (int i = 0; i < n; i++)
        {
            canon[i] = i;
            var key = (exp[i].Length, Tape.ContentHash(exp[i]));
            if (byKey.TryGetValue(key, out var members))
            {
                foreach (int m in members)                   // lowest-index first (insertion order is index order)
                    if (exp[m].AsSpan().SequenceEqual(exp[i])) { canon[i] = m; merged++; break; }
                members.Add(i);
            }
            else byKey[key] = new List<int> { i };
        }
        if (merged == 0) return new Compacted(pure, new List<RankAlias>(), 0, 0);

        // survivors renumber in emission order; dead rules' references remap through their canonical
        var newIdx = new int[n];
        int w = 0;
        for (int i = 0; i < n; i++) newIdx[i] = canon[i] == i ? w++ : -1;
        int Remap(uint sym) => sym < (uint)alpha ? (int)sym : alpha + newIdx[canon[(int)sym - alpha]];

        var outRules = new GrammarRule[w];
        for (int i = 0; i < n; i++)
        {
            if (canon[i] != i) continue;
            var pattern = new Symbol[] { new((uint)Remap(rules[i].Pattern[0].Value)), new((uint)Remap(rules[i].Pattern[1].Value)) };
            outRules[newIdx[i]] = new GrammarRule(GrammarRule.ComputeId(pattern), pattern, rules[i].Cost);
        }
        var comp = pure.Compressed ?? [];
        var outComp = new Symbol[comp.Length];
        for (int i = 0; i < comp.Length; i++) outComp[i] = new Symbol((uint)Remap(comp[i].Value));

        // dead digrams → aliases into their canonical's NEW symbol (skip a digram a survivor already owns — that
        // dead rule was pattern-identical post-remap, its digram merges into the survivor natively)
        var seen = new HashSet<long>();
        foreach (var r in outRules) seen.Add(((long)r.Pattern[0].Value << 32) | r.Pattern[1].Value);
        var aliases = new List<RankAlias>();
        for (int i = 0; i < n; i++)
        {
            if (canon[i] == i) continue;
            uint a = (uint)Remap(rules[i].Pattern[0].Value), b = (uint)Remap(rules[i].Pattern[1].Value);
            long key = ((long)a << 32) | b;
            if (seen.Add(key)) aliases.Add(new RankAlias(a, b, newIdx[canon[i]]));
        }
        aliases.Sort((x, y) => (((long)x.A << 32) | x.B).CompareTo(((long)y.A << 32) | y.B));

        // ── SWEEP to fixpoint: delete the genuinely-unreferenced (see the class doc — a merge can orphan) ──
        int swept = 0;
        while (true)
        {
            int m = outRules.Length;
            var live = new bool[m];
            foreach (var s in outComp) if (s.Value >= (uint)alpha) live[(int)s.Value - alpha] = true;
            foreach (var r in outRules) foreach (var p in r.Pattern) if (p.Value >= (uint)alpha) live[(int)p.Value - alpha] = true;
            foreach (var al in aliases)
            {
                if (al.A >= (uint)alpha) live[(int)al.A - alpha] = true;   // the encoder can still produce this symbol → its rule must exist
                if (al.B >= (uint)alpha) live[(int)al.B - alpha] = true;
                live[al.Rule] = true;                                      // the merge target
            }
            int dead = 0;
            foreach (var l in live) if (!l) dead++;
            if (dead == 0) break;
            swept += dead;
            var shift = new int[m];
            int w2 = 0;
            for (int i = 0; i < m; i++) shift[i] = live[i] ? w2++ : -1;
            uint Shift(uint sym) => sym < (uint)alpha ? sym : (uint)(alpha + shift[(int)sym - alpha]);
            var nextRules = new GrammarRule[w2];
            for (int i = 0; i < m; i++)
            {
                if (!live[i]) continue;
                var pattern = new Symbol[] { new(Shift(outRules[i].Pattern[0].Value)), new(Shift(outRules[i].Pattern[1].Value)) };
                nextRules[shift[i]] = new GrammarRule(GrammarRule.ComputeId(pattern), pattern, outRules[i].Cost);
            }
            outRules = nextRules;
            for (int i = 0; i < outComp.Length; i++) outComp[i] = new Symbol(Shift(outComp[i].Value));
            for (int i = 0; i < aliases.Count; i++)
                aliases[i] = new RankAlias(Shift(aliases[i].A), Shift(aliases[i].B), shift[aliases[i].Rule]);
        }

        return new Compacted(new RePairResult(outRules, outComp, pure.TotalSavings, pure.AlphabetSize), aliases, merged, swept);
    }
}
