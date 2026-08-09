namespace Cogito;

using Cogito.Grammar;
using Cogito.Induct;
using System.Security.Cryptography;


// ── THE MEMORY HIERARCHY ──  the working-set half of the night shift. Two of Seriate's
// three organs live here (defrag stays in Seriate.cs — it re-serializes the TAPE for induction; these two govern the
// GRAMMAR and RECALL): GC-DEMOTION (the grammar is the working set under a bit budget; a literal rule that stops
// paying MDL-rent is DEMOTED — its memorized surface returned to the reference tape, replaced by a TapeEventID
// reference; re-promoted when the pattern recurs) + the REVERSE INDEX (concept→event postings, the "5-clicks"
// navigability probe). The tape never forgets; the grammar delaminates from the life's size.
//
// THE HONEST FRAME (pass-2d): demotion is LOSSLESS and RETAINS the pattern — the demoted rule's expansion bytes
// already live on the append-only tape (that IS why it was demotable: its whole expansion == a tape event, a
// full-line memorization), so its Cost drops to a pointer while the Pattern stays as the reconstruction fallback
// (tape-unaware generators stay byte-correct). The BUDGET is the grammar's SURFACE bits — Σ expansion-bytes·8, the
// total literal surface it commits to reproduce independently of the tape; demotion returns that surface to reference
// bytes (pointer bits instead). So depth/coverage are PRESERVED (maximally graceful) while the working-set budget
// is met, and the tape-ref read path is proven byte-exact (Resolved == Demoted). Physically freeing the retained
// pattern (the RAM) is gated on generation threading the tape — a clean follow-up; today the delamination is
// MEASURED and RESOLUTION-PROVEN.

/// One GC-demotion pass's outcome + the demoted grammar. `Rules` is g's rule array with the evicted literals marked
/// TapeRef (patterns retained). `Resolved`/`Demoted` is the byte-exact tape-ref resolution proof (must be equal).
/// `NearDupeEvicted` counts the additional evictions the SimHash Hamming-family query unlocked (Mount 2 kill-line);
/// `Families` is the number of near-dupe clusters the sieve confirmed (a variant-breeder health read).
/// `MultiSpanEvicted` is the subset of `Evicted` that demoted to a ≥2-span chain (the multi-line mega-rule reach the
/// budget-enforcement backstop unlocked); `ResidualBits` is the over-budget overflow left after the loop — expansions
/// with NO reference cover (composed/cross-line rules whose bytes never appear contiguously), the HONEST residual.
public readonly record struct GcOutcome(
    int Evicted, int Promoted, long GrammarBits, int Resolved, int Demoted, GrammarRule[] Rules,
    int NearDupeEvicted = 0, int Families = 0, int MultiSpanEvicted = 0, long ResidualBits = 0);

/// One persistent demotion-table transition.  The chain is the durable tape
/// address; the expansion bytes are deliberately absent because the tape is
/// the source of record and already rides the same checkpoint rail.
internal readonly record struct MemoryDemotionDelta(
    int Hash, TapeEventSeg[] Chain, bool NearDupe, bool Removed);

/// A paradigm slot is an append-only learned object.  Member order is
/// semantic (the first member is the representative), so it is retained
/// exactly rather than normalized during delta capture.
internal readonly record struct MemoryParadigmSlotDelta(
    string Name, int Birth, string[] Members);

/// The mutable MemoryHierarchy receipt between keyframes.  Index high-water
/// and the two counters are scalar replacements; demotions and learned slots
/// are typed append streams.  No tape-sized index or full memory image rides
/// this record.
internal readonly record struct MemoryCheckpointDelta(
    long ParentRevision,
    long Revision,
    long IndexedEvents,
    MemoryDemotionDelta[] Demotions,
    MemoryParadigmSlotDelta[] Slots,
    int NextName,
    int NextBirth)
{
    internal bool IsEmpty => ParentRevision == Revision && (Demotions?.Length ?? 0) == 0 && (Slots?.Length ?? 0) == 0;
}

internal readonly record struct MemoryIndexFeedReceipt(
    int Added,
    long Bytes,
    long Shingles,
    long GramWindows,
    SimhashFeedReceipt Simhash);

public sealed class MemoryHierarchy
{
    private const int  DemoteFloorBytes   = 9;     // demote only where the ref SAVES bits: 9B surface = 72b > RefBitsPerSeg = 64b. (Was 16 — the sub-line Re-Pair chunks below it were structurally un-evictable and accreted the measured ~882K-over-budget residual; 9 is the economic floor, the net-savings gate below is the proof per rule)
    private const int  PromoteThreshold   = 3;     // a demoted pattern that recurs ≥ this in the recent window re-earns its seat
    private const int  PromoteWindowEvents = 160;   // the "working window" for promotion — the recent tail of the tape (by stable id)
    private const long RefBitsPerSeg      = 64;    // a demoted body costs an event REFERENCE per segment; a multi-event chain of m events = m·this bits (≪ the m-line literal surface it replaces)
    private const int  NearDupeTau        = 3;      // MOUNT 2: the Hamming ball for near-dupe family membership (Hamming ≤ 3)
    private const int  GramLen            = 8;      // containment-index shingle width — a demotion candidate (≥ DemoteFloorBytes) always carries a full first-gram
    private const int  GramPostCap        = 16;     // postings kept per gram key, lowest-(id,off) first (canonical exemplars); a miss past the cap leaves the rule resident — honest residual, never a wrong ref

    // ── THE PERSISTENT NIGHT-SHIFT INDEXES ──  maintained ACROSS consolidation passes,
    // fed ONLY with the events appended since the last pass (IndexNewEvents — the O(Δ) feed), NEVER rebuilt per
    // sleep. Both are pure functions of the id-ordered event set, so the checkpoint stores only the high-water
    // mark and Load re-derives them from the tape — Save∘Load ≡ the incremental accretion, bit-for-bit.
    //   _idx    — the SimHash LSH index (Mounts 1-3): Δ-candidate generation for the weave-defrag, the near-dupe
    //             GC family query, the bucket-graph navigability read. Slots are id-rank (feed order = id order).
    //   _grams  — the CONTAINMENT postings (GramPostings, the shared verb): 8-gram → the earliest (id, offset)
    //             sites carrying it, capped to the canonical-exemplar prefix. The sub-line demotion path: a
    //             no-'\n' rule expansion is ALWAYS a substring of some event (Concat = event‖'\n'‖…, so a
    //             boundary-crossing substring would carry the '\n'), and this finds the exemplar in O(1)
    //             instead of an O(tape) scan. Memory rides the tape's own scale, like Tape._byContent.
    //             FED ONLY WHEN A BUDGET CAN CONSUME IT (`demotable`): its sole reader is the Gc demotion loop,
    //             which arms iff budgetBits > 0 — and 0 stays 0 for a whole run (Homeostat: "unbounded is a
    //             config MODE"). An unbounded drive accreted 90MB/1.35M postings (622K keys — the KEY count is
    //             the mass, so GramPostCap cannot bound it) feeding an organ that provably never reads:
    //             trunk_0247's census read `demotions 0 segs` at step 695. Budget-armed runs still accrete
    //             O(distinct grams) — bounding THAT is the IndexNode lift (tiered, on-log).
    private readonly SimhashIndex _idx = new();
    private readonly GramPostings _grams = new(GramLen, GramPostCap);
    private readonly bool _demotable;              // the run can ever arm a surface-bit budget → maintain the containment postings
    private long _indexedEvents;                    // ids < this are indexed (the Δ feed's high-water mark; checkpointed)
    private Tape? _indexTape;
    internal MemoryIndexFeedReceipt LastIndexFeed { get; private set; }

    private long _mutationRevision;
    private long _checkpointRevision;
    private bool _legacyWire;
    private readonly List<MemoryDemotionDelta> _demotionsSinceCheckpoint = new();
    private readonly HashSet<string> _checkpointParadigmNames = new(StringComparer.Ordinal);

    /// `demotable` = this run's config can ever arm a surface-bit budget (GrammarBudgetBits > 0). False ⇒ the
    /// containment postings are never fed (their sole consumer, the Gc demotion loop, can never run) and every
    /// containment probe honestly misses — rules just stay resident, which is what an unbounded budget means.
    public MemoryHierarchy(bool demotable = true) => _demotable = demotable;

    /// The persistent SimHash index (id-ordered slots) — the trunk's consolidation reads Mounts 1-3 off THIS,
    /// never a per-pass OfTape rebuild.
    public SimhashIndex Index => _idx;

    /// MemStat census read — the containment-postings organ (its Mass() names the tape-proportional growth axis).
    internal GramPostings Grams => _grams;

    /// MemStat census read — persistent demotion-table mass (chains + Σ segs). Counts only.
    internal (int Chains, long Segs) DemotionMass()
    {
        long segs = 0;
        foreach (var c in _demoted.Values) segs += c.Length;
        foreach (var c in _nearDupeDemoted.Values) segs += c.Length;
        return (_demoted.Count + _nearDupeDemoted.Count, segs);
    }

    /// The persistent PARADIGM — the third night-shift organ's state. The growth loop
    /// SEEDS from this and extends it over each sleep's recency window; MintSlots rematerializes the
    /// standing slot authority onto each fresh grammar. Discovery is O(window) per pass; nothing learned is rediscovered. Unlike
    /// the indexes it is NOT derivable from the tape (it is learned structure), so the checkpoint serializes it.
    public Paradigm ConsolidationPhaseParadigm { get; } = new();

    /// The O(Δ) feed: fingerprint + bucket + gram-post ONLY the events appended since the last pass, in
    /// TapeEventID order (the probe is the ID high-water, never the resident count — shedding makes them diverge).
    /// New appends are always resident (evacuation only touches already-indexed ids), so the feed reads by id
    /// through Resolve — invariant 2: indices resolve by TapeEventID, never a positional scan.
    /// Returns the first new index slot + the Δ count — the weave's window.
    public (int FirstNewSlot, int Added) IndexNewEvents(Tape tape)
    {
        _indexTape = tape;
        _idx.ResetFeedReceipt();
        int first = _idx.Count;
        long hi = tape.NextId;
        long bytesFed = 0, shinglesFed = 0, gramWindowsFed = 0;
        for (long v = _indexedEvents; v < hi; v++)
        {
            var id = new TapeEventID(v);
            if (!tape.Resolve(id, out var bytes)) throw new InvalidOperationException($"IndexNewEvents: {id} unresolvable — a new append vanished before its first index feed");
            int pos = tape.PositionOf(id);
            _idx.Add(id, bytes, pos >= 0 ? tape.ResidentEventSources[pos] : tape.EvacSourceOf(id));
            if (_demotable) _grams.Add(id.Value, bytes);
            bytesFed += bytes.Length;
            shinglesFed += bytes.Length >= Simhash.ShingleN
                ? bytes.Length - Simhash.ShingleN + 1
                : bytes.Length == 0 ? 0 : 1;
            if (_demotable) gramWindowsFed += Math.Max(0, bytes.Length - _grams.GramLen + 1);
        }
        int added = (int)(hi - _indexedEvents);
        if (added != 0) _mutationRevision++;
        _indexedEvents = hi;
        LastIndexFeed = new MemoryIndexFeedReceipt(added, bytesFed, shinglesFed, gramWindowsFed, _idx.ConsumeFeedReceipt());
        return (first, added);
    }

    private void RecordDemotion(int hash, TapeEventSeg[] chain, bool nearDupe)
    {
        _demotionsSinceCheckpoint.Add(new MemoryDemotionDelta(hash, (TapeEventSeg[])chain.Clone(), nearDupe, Removed: false));
        _mutationRevision++;
    }

    private void RecordDemotionRemoval(int hash, bool nearDupe)
    {
        _demotionsSinceCheckpoint.Add(new MemoryDemotionDelta(hash, [], nearDupe, Removed: true));
        _mutationRevision++;
    }

    // The sub-line/offset cover: a no-'\n' expansion demotes to a 1-seg OFFSET ref inside
    // the earliest event byte-containing it. Gram postings narrow to ≤ GramPostCap candidates; byte-exact verification
    // is the lossless sieve (same law as the near-dupe boundary: postings INFORM, bytes DECIDE). Fills `chain` with
    // the single seg on success. Newline-crossers are FindCover's territory (the multi-span chain walk).
    private bool FindContainment(Tape tape, byte[] e, List<TapeEventSeg> chain)
    {
        chain.Clear();
        if (e.Length < GramLen || Array.IndexOf(e, (byte)'\n') >= 0) return false;
        if (_grams.Posts(Simhash.Fnv64(e.AsSpan(0, GramLen))) is not { } posts) return false;
        foreach (var p in posts)
            if (tape.Resolve(new TapeEventID(p.Id), out var s) && p.Off + e.Length <= s.Length
                && s.AsSpan(p.Off, e.Length).SequenceEqual(e))
            { chain.Add(new TapeEventSeg(new TapeEventID(p.Id), p.Off, e.Length)); return true; }
        return false;
    }

    // PERSISTENT across sleep passes — the demotion DECISIONS (a re-induce re-derives the full rules; this keeps the
    // evicted surfaces out of the working set until they re-earn). Keyed by expansion content-hash (Tape's hash, so
    // it survives the rule-INDEX churn a fresh induction brings), valued by the reference event CHAIN it resolves
    // through. A stored chain is tape-ORDER-invariant (TapeEventIDs are stable; resolution just concatenates their bytes),
    // so it re-applies byte-exact even after sleep's defrag Reorder scrambled adjacency.
    private readonly Dictionary<int, TapeEventSeg[]> _demoted = new();

    // MOUNT 2 (SimHash near-dupe GC) — the CONTAINMENT demotions, kept SEPARATE from the identity/multi-span demotions
    // above for reporting; each is a SINGLE offset-seg into an exemplar span (proven, like every chain, by byte-exact
    // resolution). Keyed the same way (expansion content-hash → the exemplar-containment chain).
    private readonly Dictionary<int, TapeEventSeg[]> _nearDupeDemoted = new();

    // ── RULE SURFACE ACCOUNTING ──  ExpLens is the exact recurrence for a rule's
    // expansion length, but it is a grammar-surface read, not a per-organ scratch
    // value. Loom keeps the rule array identity stable between mints; cache the
    // recurrence and its Σ surface bits at that identity so GC and the reverse index
    // share one accounting pass. A fresh/touched grammar naturally misses once; an
    // unchanged grammar never walks all rules again.
    private GrammarRule[]? _surfaceRules;
    private uint _surfaceAlphabet;
    private long[]? _surfaceLengths;
    private long _surfaceBits;

    private long SurfaceBits(RePairResult g, out long[] lengths)
    {
        if (!ReferenceEquals(_surfaceRules, g.Rules) || _surfaceAlphabet != g.AlphabetSize || _surfaceLengths is null)
        {
            lengths = Engine.ExpLens(g.Rules, g.AlphabetSize);
            long bits = 0;
            for (int i = 0; i < lengths.Length; i++)
                bits += g.Rules[i].IsDemoted ? ChainBits(g.Rules[i].Segs!) : lengths[i] * 8;
            _surfaceRules = g.Rules;
            _surfaceAlphabet = g.AlphabetSize;
            _surfaceLengths = lengths;
            _surfaceBits = bits;
        }
        else
        {
            lengths = _surfaceLengths;
        }
        return _surfaceBits;
    }

    // Reverse-index state is maintained by concept hash. Each concept keeps matching
    // stable event ids only while it occupies the deterministic active surface. The
    // grammar may expose thousands of historical concepts, but MaxConcepts is the
    // working-set contract: appended events are compared against active concepts only.
    // An inactive surface drops its postings (the identity bytes remain so reactivation
    // is exact); activation pays one resident scan, then rejoins the id delta feed.
    private sealed class ConceptSurface(byte[] bytes, int hash)
    {
        public readonly byte[] Bytes = bytes;
        public readonly int Hash = hash;
        public HashSet<long> EventIDs = new();
    }

    private readonly Dictionary<int, ConceptSurface> _conceptsByHash = new();
    private readonly Dictionary<RuleID, int> _ruleConceptHashes = new();
    private Tape? _conceptTape;
    private long _conceptIndexedEvents;
    private GrammarRule[]? _conceptRules;
    private uint _conceptAlphabet;
    private List<ConceptSurface>? _activeConcepts;
    private readonly HashSet<int> _activeConceptHashes = new();
    private long _conceptTapeRevision = -1;
    private long _conceptOrderRevision = -1;
    private (int Concepts, int Postings, string Summary)? _conceptRead;

    // Navigability is a read over the standing SimHash adjacency. Add() is the only
    // mutation, so Count is the exact cache epoch; unchanged consolidation phases return the
    // prior seeded read without walking the graph again.
    private int _navCount = -1;
    private ulong _navSeed;
    private int _navSamples;
    private double _navRead;

    public int DemotedCount => _demoted.Count + _nearDupeDemoted.Count;

    private static long ChainBits(TapeEventSeg[] segs) => (long)segs.Length * RefBitsPerSeg;
    private static Mbits ChainCost(TapeEventSeg[] segs) => new(ChainBits(segs) * 1000);

    /// The GC pass — rank the fresh grammar's rules by MDL-rent, demote the lowest-rent literals whose
    /// expansion is COVERED by reference bytes (single-span identity, a multi-line mega-rule's adjacent-span chain, OR
    /// a sub-line gram-containment offset seg) until under the surface-bit budget — each demotion gated on NET bit
    /// savings — promote demoted patterns that recur in the recent window, and return the grammar with the evicted
    /// literals marked TapeRef. Deterministic: rent-ascending with an index tie-break, FindCover's lowest-id anchor
    /// candidate, the gram postings' lowest-(id,off) exemplar, the recurrence window read off stable ids.
    ///
    /// `wuses`/`wScale` arm the PROVENANCE-WEIGHTED rent: a replay-echo rule's uses
    /// are worth 1/wScale of an exercised rule's, so echo rules rank lowest-rent and demote FIRST — and promotion
    /// reads weighted recurrence against a wScale-scaled threshold, so a demoted pattern cannot re-earn its seat
    /// by the machine echoing it to itself (the self-echo promotion hole, closed). Null wuses + wScale=1 is
    /// today's arithmetic identically.
    public GcOutcome Gc(RePairResult g, Tape tape, long budgetBits, bool nearDupe = false, SimhashIndex? simIdx = null, long[]? wuses = null, int wScale = 1)
    {
        // ── THE UNBOUNDED FAST PATH ──  budget 0 is a config MODE: no demotion,
        // regardless of the NearDupe switch or demotion history from an earlier
        // bounded era. The whole expansion/hash/rent plane is dead work. SurfaceBits
        // is the exact recurrence accounting and is shared with BuildIndex, while the
        // grammar array is returned UNCLONED (downstream identity caches stay hot).
        if (budgetBits == 0)
        {
            long bits = SurfaceBits(g, out _);
            return new GcOutcome(0, 0, bits, 0, 0, g.Rules);
        }

        int n = g.Rules.Length;
        var rules = (GrammarRule[])g.Rules.Clone();
        var exp   = new byte[n][];
        var hash  = new int[n];
        long[] rent = new long[n];
        int vocab = (int)g.AlphabetSize + n;
        var uses = wuses is null ? Engine.RuleUses(g) : null;
        for (int i = 0; i < n; i++)
        {
            exp[i]  = Reconstruct.Expand(g.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            hash[i] = Tape.ContentHash(exp[i]);
            rent[i] = (wuses is null ? Mdl.PairDelta(uses![i], vocab) : Mdl.PairDelta(wuses[i], vocab, wScale)).Value;   // the MDL savings this rule pays — lowest = evict first (weighted: echoes pay least)
        }
        var coverScratch   = new List<TapeEventSeg>();                      // reused scratch for the FindCover/FindContainment walk (one alloc per pass, not per rule)
        var resolveScratch = new List<byte>();                         // reused byte scratch for the tape-ref Vow-checks (ChainResolvesTo) — one alloc per pass, not a List+array per demoted rule

        // ── PROMOTION ──  a demoted pattern that recurs ≥ PromoteThreshold as a full-line span in the recent window
        // (the working set is being used again) re-earns its seat: drop it from the demotion set so this induction's
        // rule for it stays RESIDENT. Anti-flip-flop: demotion targets GLOBALLY-low-rent rules, promotion reads
        // RECENT recurrence — different signals, so a stable rare rule stays demoted, an active one promotes.
        // Weighted: recurrence is Σ span-weights vs PromoteThreshold·wScale — 3 real recurrences promote (as today),
        // 3 unvested replay echoes are worth 3/wScale of the bar and do NOT.
        var recent = RecentContentCounts(tape, wScale);
        int promoted = 0;
        foreach (var h in _demoted.Keys.ToList())
            if (recent.GetValueOrDefault(h) >= PromoteThreshold * wScale)
            {
                _demoted.Remove(h);
                RecordDemotionRemoval(h, nearDupe: false);
                promoted++;
            }

        // ── RE-APPLY the persistent demotions to this fresh grammar (keep evicted surfaces out of the working set) ──
        // A stored chain re-earns its TapeRef only if it still resolves BYTE-EXACT to this rule's expansion (so an
        // FNV32 collision can't re-demote a wrong rule); identity, multi-span, containment, and near-dupe all prove
        // the SAME way.
        long grammarBits = 0;
        for (int i = 0; i < n; i++)
        {
            if (exp[i].Length >= DemoteFloorBytes && !rules[i].IsSlot
                && _demoted.TryGetValue(hash[i], out var dchain)
                && tape.ChainResolvesTo(dchain, exp[i], resolveScratch))
                rules[i] = rules[i].AsTapeRef(dchain, ChainCost(dchain));
            else if (exp[i].Length >= DemoteFloorBytes && !rules[i].IsSlot
                && _nearDupeDemoted.TryGetValue(hash[i], out var nchain)
                && tape.ChainResolvesTo(nchain, exp[i], resolveScratch))
                rules[i] = rules[i].AsTapeRef(nchain, ChainCost(nchain));
            grammarBits += rules[i].IsDemoted ? ChainBits(rules[i].Segs!) : (long)exp[i].Length * 8;
        }

        // ── NEW DEMOTION under the surface-bit budget ──  evict the lowest-rent literals whose expansion is COVERED
        // by reference bytes, rent-ascending, until under budget. The cover LADDER: FindCover (whole-span identity — the
        // 1-seg case — AND multi-line MEGA-RULES as m-seg chains) → gram CONTAINMENT (an offset seg inside one span —
        // the sub-line path that made the below-16B Re-Pair chunks demotable at all). Each demotion passes the
        // NET-SAVINGS gate — the ref must cost fewer bits than the surface it evicts (a many-seg chain over short
        // lines can cost MORE than the literal; the old floor-16 masked this, the economic floor-9 makes it explicit).
        // A rule with no paying cover is left resident (the honest residual below).
        int evicted = 0, multiSpanEvicted = 0;
        if (budgetBits > 0 && grammarBits > budgetBits)
        {
            var cands = new List<int>();
            for (int i = 0; i < n; i++)
                if (!rules[i].IsDemoted && !rules[i].IsSlot && exp[i].Length >= DemoteFloorBytes) cands.Add(i);
            cands.Sort((a, b) => rent[a] != rent[b] ? rent[a].CompareTo(rent[b]) : a.CompareTo(b));   // rent-ascending, index tie-break
            foreach (int i in cands)
            {
                if (grammarBits <= budgetBits) break;
                if (!tape.FindCover(exp[i], coverScratch) && !FindContainment(tape, exp[i], coverScratch)) continue;   // no reference cover ⟹ undemotable (composed/cross-line) — stays resident
                var segs = coverScratch.ToArray();
                long save = (long)exp[i].Length * 8 - ChainBits(segs);
                if (save <= 0) continue;                                      // the ref would cost ≥ the surface — demotion must PAY
                rules[i] = rules[i].AsTapeRef(segs, ChainCost(segs));
                _demoted[hash[i]] = segs;
                RecordDemotion(hash[i], segs, nearDupe: false);
                grammarBits -= save;
                evicted++;
                if (segs.Length > 1) multiSpanEvicted++;
            }
        }

        // ── MOUNT 2 · NEAR-DUPE CONTAINMENT DEMOTION (SimHash Hamming-family query) ──  the autoregressive mint is a
        // VARIANT-BREEDER: it accretes Hamming-close spans (a family of near-dupe lines). A grammar rule whose
        // expansion is byte-CONTAINED in one of those variant spans can demote to it — coverage the identity pass
        // (whole-span match only) cannot reach. The SimHash Hamming-ball query NARROWS the containment search to the
        // near-dupe cluster; the byte-exact IndexOf is the LOSSLESS SIEVE.
        //
        // ── THE LOSSLESS BOUNDARY (state it loud) ──  near-dupes inform CLUSTERING (which spans to search), they NEVER
        // SUBSTITUTE. A rule demotes ONLY when a cluster member byte-EXACTLY CONTAINS its expansion (IndexOf ≥ 0) —
        // the exemplar reference then names a reference span that provably holds the bytes. If the variant DIFFERS
        // from every cluster member (no byte-exact containment), it is NOT a resolution target and is NOT demoted:
        // demotion stays lossless. The retained Pattern keeps tape-unaware generation byte-correct; the exemplar ref
        // is the working-set-budget accounting + recall anchor, PROVEN by the containment resolution below.
        int nearDupeEvicted = 0, families = 0;
        if (nearDupe && budgetBits > 0 && grammarBits > budgetBits)
        {
            var idx = simIdx ?? (_idx.Count > 0 ? _idx : SimhashIndex.OfTape(tape));   // the trunk hands the PERSISTENT index; bare Cli verbs fall back to a transient build
            var seenFamily = new HashSet<int>();
            var cands = new List<int>();
            for (int i = 0; i < n; i++)
                if (!rules[i].IsDemoted && !rules[i].IsSlot && exp[i].Length >= DemoteFloorBytes
                    && !tape.FindCover(exp[i], coverScratch) && !FindContainment(tape, exp[i], coverScratch)) cands.Add(i);
            cands.Sort((a, b) => rent[a] != rent[b] ? rent[a].CompareTo(rent[b]) : a.CompareTo(b));   // rent-ascending, index tie-break (deterministic)
            foreach (int i in cands)
            {
                if (grammarBits <= budgetBits) break;
                var cluster = idx.NearDupes(Simhash.OfBytes(exp[i]), NearDupeTau);   // lowest-id first — the canonical exemplar leads
                TapeEventSeg[]? segs = null;
                foreach (int slot in cluster)
                {
                    var eid = idx.IdAt(slot);
                    if (tape.Resolve(eid, out var E))
                    {
                        int off = E.AsSpan().IndexOf(exp[i]);           // THE SIEVE — byte-exact containment gives the exact offset seg
                        if (off >= 0) { segs = [new TapeEventSeg(eid, off, exp[i].Length)]; break; }
                    }
                }
                if (segs is null) continue;                            // no member byte-contains it ⟹ not a resolution target (lossless)
                long save = (long)exp[i].Length * 8 - ChainBits(segs);
                if (save <= 0) continue;                               // the net-savings gate — same law as the main loop
                rules[i] = rules[i].AsTapeRef(segs, ChainCost(segs));
                _nearDupeDemoted[hash[i]] = segs;
                RecordDemotion(hash[i], segs, nearDupe: true);
                grammarBits -= save;
                nearDupeEvicted++;
                if (seenFamily.Add((int)segs[0].Id.Value)) families++;
            }
        }

        // ── RESOLUTION PROOF ──  every demoted body reads back byte-exact from the source of record (the demote-don't-delete
        // guarantee; Resolved must equal Demoted, else the tape-ref path is broken — the crash-out localizes here).
        // ONE rule for all three cases: resolve the chain (unit-concatenation) and SequenceEqual the expansion —
        // identity (1 whole-span seg), multi-span (m segs), and near-dupe (1 offset seg) all prove the SAME lossless
        // way: the bytes provably live on the append-only tape.
        int resolved = 0, demoted = 0;
        for (int i = 0; i < n; i++)
            if (rules[i].IsDemoted)
            {
                demoted++;
                if (tape.ChainResolvesTo(rules[i].Segs!, exp[i], resolveScratch)) resolved++;
            }

        // ── RESIDUAL (honest) ──  if still over budget, the overflow is expansions with NO reference cover
        // (composed/cross-line rules whose bytes never appear contiguously on the tape — cheap by construction);
        // named, not hidden. Zero when the budget was met (or unbounded).
        long residualBits = budgetBits > 0 ? Math.Max(0, grammarBits - budgetBits) : 0;

        return new GcOutcome(evicted, promoted, grammarBits, resolved, demoted, rules,
                             nearDupeEvicted, families, multiSpanEvicted, residualBits);
    }

    // checkpoint — the demotion tables (key-sorted for the round-trip identity; chains are tape-order-invariant,
    // stable TapeEventIDs) + the night-shift high-water mark. The persistent indexes themselves are NOT serialized:
    // they are pure functions of the id-ordered span set, so Load re-derives them from the restored tape over ids
    // < _indexedEvents — the SAME feed sequence the incremental accretion performed, hence the SAME index, bit-for-
    // bit. Events appended AFTER the last sleep stay unindexed on
    // load exactly as they were live, so the next sleep's Δ window is identical to the uninterrupted run's.
    internal MemoryCheckpointDelta CaptureCheckpointDelta()
    {
        ConsolidationPhaseParadigm.ValidatePersistence();
        List<MemoryParadigmSlotDelta> slots = new();
        foreach (string name in ConsolidationPhaseParadigm.SlotMembers.Keys.Order(StringComparer.Ordinal))
        {
            if (_checkpointParadigmNames.Contains(name)) continue;
            slots.Add(new MemoryParadigmSlotDelta(
                name,
                ConsolidationPhaseParadigm.SlotBirth.GetValueOrDefault(name),
                ConsolidationPhaseParadigm.SlotMembers[name].ToArray()));
        }
        return new MemoryCheckpointDelta(
            _checkpointRevision,
            _mutationRevision,
            _indexedEvents,
            _demotionsSinceCheckpoint.ToArray(),
            slots.ToArray(),
            ConsolidationPhaseParadigm.NextName,
            ConsolidationPhaseParadigm.NextBirth);
    }

    internal void CommitCheckpointDelta()
    {
        _demotionsSinceCheckpoint.Clear();
        _checkpointParadigmNames.Clear();
        foreach (string name in ConsolidationPhaseParadigm.SlotMembers.Keys)
            _checkpointParadigmNames.Add(name);
        _checkpointRevision = _mutationRevision;
    }

    internal void ApplyCheckpointDelta(in MemoryCheckpointDelta delta)
        => ApplyCheckpointDelta(in delta, _indexTape);

    internal void ApplyCheckpointDelta(in MemoryCheckpointDelta delta, Tape? tape)
    {
        if (delta.ParentRevision != _mutationRevision)
            throw new InvalidDataException($"memory checkpoint delta parent revision {delta.ParentRevision} disagrees with {_mutationRevision}");
        if (delta.Revision < delta.ParentRevision)
            throw new InvalidDataException($"memory checkpoint delta revision {delta.Revision} regresses from {delta.ParentRevision}");
        if (delta.IndexedEvents < _indexedEvents)
            throw new InvalidDataException($"memory checkpoint indexed high-water regressed from {_indexedEvents} to {delta.IndexedEvents}");
        if (delta.NextName < ConsolidationPhaseParadigm.NextName || delta.NextBirth < ConsolidationPhaseParadigm.NextBirth)
            throw new InvalidDataException("memory checkpoint paradigm counters regressed");
        if (delta.IndexedEvents < 0)
            throw new InvalidDataException("memory checkpoint indexed high-water is invalid");

        tape ??= _indexTape;
        if (delta.IndexedEvents > _indexedEvents)
        {
            if (tape is null) throw new InvalidDataException("memory checkpoint delta advances the index without a tape binding");
            if (delta.IndexedEvents > tape.NextId) throw new InvalidDataException("memory checkpoint indexed high-water exceeds tape high-water");
            for (long v = _indexedEvents; v < delta.IndexedEvents; v++)
                if (!tape.Resolve(new TapeEventID(v), out _))
                    throw new InvalidDataException($"memory checkpoint index event {v} is unresolvable");
        }

        // Validate the complete post-delta tower before applying any side of
        // the mutation. This keeps malformed ancestry from partially replacing
        // the inverse map or advancing the scalar cursors.
        var mergedMembers = new Dictionary<string, List<string>>(ConsolidationPhaseParadigm.SlotMembers, StringComparer.Ordinal);
        var mergedBirths = new Dictionary<string, int>(ConsolidationPhaseParadigm.SlotBirth, StringComparer.Ordinal);
        foreach (MemoryParadigmSlotDelta slot in delta.Slots ?? [])
        {
            if (slot.Name is null || slot.Members is null || slot.Members.Length == 0)
                throw new InvalidDataException("memory checkpoint paradigm slot is malformed");
            if (!mergedMembers.TryAdd(slot.Name, slot.Members.ToList()) || !mergedBirths.TryAdd(slot.Name, slot.Birth))
                throw new InvalidDataException($"memory checkpoint paradigm slot '{slot.Name}' is duplicated");
        }
        Paradigm.ValidatePersistence(mergedMembers, mergedBirths, delta.NextName, delta.NextBirth);
        ConsolidationPhaseParadigm.ValidatePersistence();

        foreach (MemoryDemotionDelta change in delta.Demotions ?? [])
        {
            Dictionary<int, TapeEventSeg[]> table = change.NearDupe ? _nearDupeDemoted : _demoted;
            if (change.Removed) table.Remove(change.Hash);
            else table[change.Hash] = (TapeEventSeg[])change.Chain.Clone();
        }
        foreach (MemoryParadigmSlotDelta slot in delta.Slots ?? [])
        {
            ConsolidationPhaseParadigm.SlotMembers[slot.Name] = slot.Members.ToList();
            ConsolidationPhaseParadigm.SlotBirth[slot.Name] = slot.Birth;
            foreach (string member in slot.Members) ConsolidationPhaseParadigm.MemberToSlot[member] = slot.Name;
            _checkpointParadigmNames.Add(slot.Name);
        }
        if ((delta.Slots?.Length ?? 0) != 0) ConsolidationPhaseParadigm.RebuildMintSpine();
        ConsolidationPhaseParadigm.NextName = delta.NextName;
        ConsolidationPhaseParadigm.NextBirth = delta.NextBirth;
        if (delta.IndexedEvents > _indexedEvents)
        {
            _indexTape = tape;
            FeedIndexedEvents(tape!, delta.IndexedEvents);
        }
        _indexedEvents = delta.IndexedEvents;
        _mutationRevision = delta.Revision;
        _checkpointRevision = delta.Revision;
        _demotionsSinceCheckpoint.Clear();
        ConsolidationPhaseParadigm.ValidatePersistence();
    }

    internal static void WriteCheckpointDelta(CkptWriter w, in MemoryCheckpointDelta delta)
    {
        const int maxEntries = 1_000_000;
        w.U8(1); w.I64(delta.ParentRevision); w.I64(delta.Revision); w.I64(delta.IndexedEvents);
        if (delta.Demotions.Length > maxEntries || delta.Slots.Length > maxEntries)
            throw new InvalidDataException("memory checkpoint delta exceeds entry bound");
        w.I32(delta.Demotions.Length);
        foreach (MemoryDemotionDelta change in delta.Demotions)
        {
            w.I32(change.Hash); w.Bool(change.NearDupe); w.Bool(change.Removed); w.I32(change.Chain.Length);
            foreach (TapeEventSeg seg in change.Chain) { w.I64(seg.Id.Value); w.I32(seg.Start); w.I32(seg.Len); }
        }
        w.I32(delta.Slots.Length);
        foreach (MemoryParadigmSlotDelta slot in delta.Slots)
        {
            w.Str(slot.Name); w.I32(slot.Birth); w.I32(slot.Members.Length);
            foreach (string member in slot.Members) w.Str(member);
        }
        w.I32(delta.NextName); w.I32(delta.NextBirth);
    }

    internal static MemoryCheckpointDelta ReadCheckpointDelta(CkptReader r)
    {
        const int maxEntries = 1_000_000;
        if (r.U8() != 1) throw new InvalidDataException("unknown memory checkpoint delta version");
        long parent = r.I64(), revision = r.I64(), indexed = r.I64();
        int demotionCount = r.I32();
        if (demotionCount < 0 || demotionCount > maxEntries) throw new InvalidDataException("memory checkpoint demotion count is invalid");
        var demotions = new MemoryDemotionDelta[demotionCount];
        for (int i = 0; i < demotionCount; i++)
        {
            int hash = r.I32(); bool near = r.Bool(); bool removed = r.Bool(); int count = r.I32();
            if (count < 0 || count > maxEntries) throw new InvalidDataException("memory checkpoint chain count is invalid");
            var chain = new TapeEventSeg[count];
            for (int j = 0; j < count; j++) chain[j] = new(new TapeEventID(r.I64()), r.I32(), r.I32());
            demotions[i] = new MemoryDemotionDelta(hash, chain, near, removed);
        }
        int slotCount = r.I32();
        if (slotCount < 0 || slotCount > maxEntries) throw new InvalidDataException("memory checkpoint slot count is invalid");
        var slots = new MemoryParadigmSlotDelta[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            string name = r.Str(); int birth = r.I32(); int count = r.I32();
            if (count <= 0 || count > maxEntries) throw new InvalidDataException("memory checkpoint slot member count is invalid");
            var members = new string[count];
            for (int j = 0; j < count; j++) members[j] = r.Str();
            slots[i] = new MemoryParadigmSlotDelta(name, birth, members);
        }
        return new MemoryCheckpointDelta(parent, revision, indexed, demotions, slots, r.I32(), r.I32());
    }

    public void Save(CkptWriter w)
    {
        ConsolidationPhaseParadigm.ValidatePersistence();
        if (_legacyWire)
        {
            WriteLegacy(w);
            return;
        }
        w.U8(3);
        w.Bool(_demotable);
        w.I64(_indexedEvents);
        // Mutation revisions are the parent edge of every typed delta.  They
        // are state, not a derived index: dropping them makes a loaded
        // keyframe look like revision zero and rejects the first live delta.
        w.I64(_mutationRevision); w.I64(_checkpointRevision);
        WriteDemotions(w, _demoted);
        WriteDemotions(w, _nearDupeDemoted);
        // the paradigm — name-sorted for round-trip identity; member LIST ORDER preserved (members[0] is the
        // slot's representative in Reconstruct — order is semantics, never sort it).
        w.I32(ConsolidationPhaseParadigm.NextName); w.I32(ConsolidationPhaseParadigm.NextBirth);
        w.I32(ConsolidationPhaseParadigm.SlotMembers.Count);
        foreach (var name in ConsolidationPhaseParadigm.SlotMembers.Keys.Order(StringComparer.Ordinal))
        {
            w.Str(name);
            w.I32(ConsolidationPhaseParadigm.SlotBirth.GetValueOrDefault(name));
            var members = ConsolidationPhaseParadigm.SlotMembers[name];
            w.I32(members.Count);
            foreach (var m in members) w.Str(m);
        }
        _idx.Save(w);
        _grams.Save(w);
        w.Str(IndexDigest());
    }

    private void WriteLegacy(CkptWriter w)
    {
        w.U8(2);
        w.I64(_indexedEvents);
        w.I64(_mutationRevision); w.I64(_checkpointRevision);
        WriteDemotions(w, _demoted);
        WriteDemotions(w, _nearDupeDemoted);
        w.I32(ConsolidationPhaseParadigm.NextName); w.I32(ConsolidationPhaseParadigm.NextBirth);
        w.I32(ConsolidationPhaseParadigm.SlotMembers.Count);
        foreach (var name in ConsolidationPhaseParadigm.SlotMembers.Keys.Order(StringComparer.Ordinal))
        {
            w.Str(name);
            w.I32(ConsolidationPhaseParadigm.SlotBirth.GetValueOrDefault(name));
            var members = ConsolidationPhaseParadigm.SlotMembers[name];
            w.I32(members.Count);
            foreach (var m in members) w.Str(m);
        }
    }

    public void Load(CkptReader r, Tape tape)
    {
        byte version = r.U8();
        if (version is not (2 or 3)) throw new InvalidDataException("unsupported memory checkpoint section");
        _legacyWire = version == 2;
        if (version == 3 && r.Bool() != _demotable)
            throw new InvalidDataException("memory checkpoint demotable configuration mismatch");
        _indexTape = tape;
        _idx.Clear(); _grams.Clear();
        long storedIndexedEvents = r.I64();
        _indexedEvents = version == 2 ? 0 : storedIndexedEvents;
        _mutationRevision = r.I64(); _checkpointRevision = r.I64();
        if (_mutationRevision < 0 || _checkpointRevision < 0 || _checkpointRevision > _mutationRevision)
            throw new InvalidDataException("memory checkpoint revisions are not monotonic");
        ReadDemotions(r, _demoted);
        ReadDemotions(r, _nearDupeDemoted);
        int nextName = r.I32();
        int nextBirth = r.I32();
        int slots = r.I32();
        if (slots < 0 || slots > 1_000_000)
            throw new InvalidDataException("memory checkpoint paradigm slot count is invalid");
        var loadedMembers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var loadedBirths = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < slots; i++)
        {
            string name = r.Str();
            int birth = r.I32();
            int nm = r.I32();
            if (nm <= 0 || nm > 1_000_000)
                throw new InvalidDataException("memory checkpoint paradigm member count is invalid");
            var members = new List<string>(nm);
            for (int j = 0; j < nm; j++) members.Add(r.Str());
            if (!loadedMembers.TryAdd(name, members) || !loadedBirths.TryAdd(name, birth))
                throw new InvalidDataException($"memory checkpoint paradigm slot '{name}' is duplicated");
        }
        Paradigm.ValidatePersistence(loadedMembers, loadedBirths, nextName, nextBirth);
        ConsolidationPhaseParadigm.SlotMembers.Clear();
        ConsolidationPhaseParadigm.SlotBirth.Clear();
        ConsolidationPhaseParadigm.MemberToSlot.Clear();
        foreach (var pair in loadedMembers)
        {
            ConsolidationPhaseParadigm.SlotMembers.Add(pair.Key, pair.Value);
            ConsolidationPhaseParadigm.SlotBirth.Add(pair.Key, loadedBirths[pair.Key]);
            foreach (string member in pair.Value) ConsolidationPhaseParadigm.MemberToSlot.Add(member, pair.Key); // inverse map is derived, not stored
        }
        ConsolidationPhaseParadigm.NextName = nextName;
        ConsolidationPhaseParadigm.NextBirth = nextBirth;
        ConsolidationPhaseParadigm.RebuildMintSpine();                                      // the mint spine is derived, not stored — birth-order restored from SlotBirth
        if (storedIndexedEvents < 0 || storedIndexedEvents > tape.NextId)
            throw new InvalidDataException("memory checkpoint indexed high-water exceeds tape high-water");
        if (version == 3)
        {
            _idx.Load(r);
            _grams.Load(r, GramLen, GramPostCap, storedIndexedEvents);
            if (_idx.Count != storedIndexedEvents) throw new InvalidDataException("memory checkpoint index count disagrees with high-water");
            for (int i = 0; i < _idx.Count; i++)
                if (_idx.Ids[i].Value != i) throw new InvalidDataException("memory checkpoint index IDs are not a dense high-water prefix");
            string expectedDigest = r.Str();
            if (!string.Equals(expectedDigest, IndexDigest(), StringComparison.Ordinal))
                throw new InvalidDataException("memory checkpoint index digest mismatch");
        }
        else
        {
            FeedIndexedEvents(tape, storedIndexedEvents); // legacy v2 wire: replay bytes only when no canonical index was persisted
        }
        _demotionsSinceCheckpoint.Clear();
        _checkpointParadigmNames.Clear();
        foreach (string name in ConsolidationPhaseParadigm.SlotMembers.Keys) _checkpointParadigmNames.Add(name);

        // Concept postings are a derived working set, not checkpoint state. A
        // resume must rebuild the active surface against the supplied tape rather
        // than accidentally retaining postings from the pre-resume object.
        _conceptTape = null;
        _conceptIndexedEvents = 0;
        _conceptRules = null;
        _conceptAlphabet = 0;
        _activeConcepts = null;
        _activeConceptHashes.Clear();
        _conceptTapeRevision = -1;
        _conceptOrderRevision = -1;
        _conceptRead = null;
        _conceptsByHash.Clear();
        _ruleConceptHashes.Clear();
    }

    private void FeedIndexedEvents(Tape tape, long highWater)
    {
        if (highWater < _indexedEvents) throw new InvalidDataException("memory index high-water regressed");
        for (long v = _indexedEvents; v < highWater; v++)
        {
            var id = new TapeEventID(v);
            if (!tape.Resolve(id, out var bytes)) throw new InvalidDataException($"MemoryHierarchy index event {id} is unresolvable");
            int pos = tape.PositionOf(id);
            _idx.Add(id, bytes, pos >= 0 ? tape.ResidentEventSources[pos] : tape.EvacSourceOf(id));
            if (_demotable) _grams.Add(id.Value, bytes);
        }
        _indexedEvents = highWater;
    }

    // The digest streams through a Null-sunk HashTeeStream — the serialization is never materialized (the old
    // MemoryStream + ToArray held two full images). The byte sequence is unchanged, so persisted digests match.
    private string IndexDigest()
    {
        using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (CkptWriter w = new(new HashTeeStream(Stream.Null, sha))) { _idx.Save(w); _grams.Save(w); }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    // Differential receipt for the durable index rail.  The straight-through
    // subject feeds the append delta live; the resumed subject loads the
    // keyframe and applies the typed mutation against the same tape.  Every
    // observable index organ is compared, then a digest flip must fail closed.
    internal static int VerifyCheckpointIndex()
    {
        static byte[] Bytes(long i) => System.Text.Encoding.UTF8.GetBytes($"near-dupe-family-{i % 4:D2} :: resonant_cursor_delta :: source_{i:D3}");
        static Tape NewTape() { var t = new Tape(); t.MountLog(new MemoryStream()); return t; }
        static byte[] Save(MemoryHierarchy m) { using MemoryStream ms = new(); using (var w = new CkptWriter(ms)) m.Save(w); return ms.ToArray(); }
        static MemoryHierarchy Load(byte[] image, Tape tape) { var m = new MemoryHierarchy(); using var ms = new MemoryStream(image, writable: false); using var r = new CkptReader(ms); m.Load(r, tape); return m; }
        int fails = 0;
        var tape = NewTape();
        var live = new MemoryHierarchy();
        for (long i = 0; i < 24; i++) tape.Append(Bytes(i), "node0", Provenances.Replay);
        live.IndexNewEvents(tape);
        byte[] keyframe = Save(live);
        live.CommitCheckpointDelta();
        for (long i = 24; i < 40; i++) tape.Append(Bytes(i), "node0", Provenances.Replay);
        live.IndexNewEvents(tape);
        MemoryCheckpointDelta mutation = live.CaptureCheckpointDelta();
        var resumed = Load(keyframe, tape);
        resumed.ApplyCheckpointDelta(in mutation, tape);

        bool same = live.Index.Count == resumed.Index.Count && live.Grams.Mass() == resumed.Grams.Mass();
        for (int i = 0; same && i < live.Index.Count; i++)
        {
            same &= live.Index.Ids[i] == resumed.Index.Ids[i] && live.Index.Signatures[i] == resumed.Index.Signatures[i]
                && live.Index.SourceAt(i) == resumed.Index.SourceAt(i)
                && live.Index.AdjOf(i).SequenceEqual(resumed.Index.AdjOf(i));
        }
        for (int i = 0; same && i < live.Index.Count; i++)
        {
            var query = live.Index.SigAt(i);
            same &= live.Index.NearDupes(query, 3).SequenceEqual(resumed.Index.NearDupes(query, 3));
            var a = new List<int>(); var b = new List<int>();
            live.Index.TopPriorCandidates(i, 8, a); resumed.Index.TopPriorCandidates(i, 8, b);
            same &= a.SequenceEqual(b);
        }
        if (!same) fails++;
        byte[] corrupt = (byte[])keyframe.Clone(); corrupt[^1] ^= 1;
        bool rejected = false;
        try { _ = Load(corrupt, tape); } catch (InvalidDataException) { rejected = true; }
        var legacySource = new MemoryHierarchy(); legacySource.IndexNewEvents(tape); legacySource._legacyWire = true;
        byte[] legacy = Save(legacySource);
        bool legacyExact = Save(Load(legacy, tape)).AsSpan().SequenceEqual(legacy);
        if (!rejected) fails++;
        if (!legacyExact) fails++;
        Console.WriteLine($"  {(same ? "✓" : "✗ FAIL")}  memory-keyframe-delta  IDs/signatures/adjacency/gram-mass/NearDupes/TopPriorCandidates");
        Console.WriteLine($"  {(rejected ? "✓" : "✗ FAIL")}  memory-index-corruption  digest tamper rejected");
        Console.WriteLine($"  {(legacyExact ? "✓" : "✗ FAIL")}  memory-v2-wire           legacy Save∘Load∘Save exact");
        return fails;
    }

    // Differential receipt for the learned paradigm rail.  The nested tower
    // is resumed from a keyframe, then a typed mutation appends one sibling;
    // the effective graph and top-resolution must match the uninterrupted
    // subject. Corrupt ancestry is rejected before the live maps move.
    internal static int VerifyCheckpointParadigm()
    {
        static Tape NewTape() { var t = new Tape(); t.MountLog(new MemoryStream()); return t; }
        static byte[] Save(MemoryHierarchy m)
        {
            using MemoryStream ms = new();
            using var w = new CkptWriter(ms);
            m.Save(w);
            return ms.ToArray();
        }
        static MemoryHierarchy Load(byte[] image, Tape tape)
        {
            var m = new MemoryHierarchy();
            using var ms = new MemoryStream(image, writable: false);
            using var r = new CkptReader(ms);
            m.Load(r, tape);
            return m;
        }
        static bool Reject(MemoryHierarchy m, in MemoryCheckpointDelta delta, Tape tape)
        {
            try { m.ApplyCheckpointDelta(in delta, tape); return false; }
            catch (InvalidDataException) { return true; }
        }

        int fails = 0;
        var tape = NewTape();
        tape.Append(System.Text.Encoding.UTF8.GetBytes("cat dog fox bird fish\n"), "node0", Provenances.Replay);
        var live = new MemoryHierarchy();
        live.ConsolidationPhaseParadigm.BirthSlot("[S0]", ["cat", "dog"]);
        live.ConsolidationPhaseParadigm.BirthSlot("[[S1]]", ["[S0]", "fox"]);
        byte[] keyframe = Save(live);
        live.CommitCheckpointDelta();
        live.ConsolidationPhaseParadigm.BirthSlot("[S2]", ["bird", "fish"]);
        MemoryCheckpointDelta mutation = live.CaptureCheckpointDelta();
        var resumed = Load(keyframe, tape);
        resumed.ApplyCheckpointDelta(in mutation, tape);

        bool same = live.ConsolidationPhaseParadigm.SlotMembers.Count == resumed.ConsolidationPhaseParadigm.SlotMembers.Count
            && live.ConsolidationPhaseParadigm.NextBirth == resumed.ConsolidationPhaseParadigm.NextBirth
            && live.ConsolidationPhaseParadigm.ResolveTop("cat") == resumed.ConsolidationPhaseParadigm.ResolveTop("cat")
            && live.ConsolidationPhaseParadigm.ResolveTop("fox") == resumed.ConsolidationPhaseParadigm.ResolveTop("fox")
            && live.ConsolidationPhaseParadigm.ResolveTop("bird") == resumed.ConsolidationPhaseParadigm.ResolveTop("bird");
        foreach (var pair in live.ConsolidationPhaseParadigm.SlotMembers)
            same &= resumed.ConsolidationPhaseParadigm.SlotMembers.TryGetValue(pair.Key, out var members)
                && pair.Value.SequenceEqual(members)
                && resumed.ConsolidationPhaseParadigm.SlotBirth[pair.Key] == live.ConsolidationPhaseParadigm.SlotBirth[pair.Key];

        var cycle = new MemoryCheckpointDelta(0, 0, 0, [],
            [new MemoryParadigmSlotDelta("[S9]", 3, ["[S9]"])], 0, 4);
        var rebind = new MemoryCheckpointDelta(0, 0, 0, [],
            [new MemoryParadigmSlotDelta("[S9]", 3, ["cat"])], 0, 4);
        var childAfterParent = new MemoryCheckpointDelta(0, 0, 0, [],
            [new MemoryParadigmSlotDelta("[S9]", 3, ["[S10]"]), new MemoryParadigmSlotDelta("[S10]", 4, ["new"])], 0, 5);
        bool cycleRejected = Reject(resumed, in cycle, tape);
        bool rebindRejected = Reject(resumed, in rebind, tape);
        bool childRejected = Reject(resumed, in childAfterParent, tape);
        if (!same) fails++;
        if (!cycleRejected || !rebindRejected || !childRejected) fails++;
        Console.WriteLine($"  {(same ? "✓" : "✗ FAIL")}  memory-paradigm-resume  nested tower + sibling delta preserves births, members, and top resolution");
        Console.WriteLine($"  {(cycleRejected && rebindRejected && childRejected ? "✓" : "✗ FAIL")}  memory-paradigm-corruption  cycle/rebind/child-after-parent rejected before mutation");
        return fails;
    }

    private static void WriteDemotions(CkptWriter w, Dictionary<int, TapeEventSeg[]> d)
    {
        w.I32(d.Count);
        foreach (var k in d.Keys.Order())
        {
            w.I32(k);
            var segs = d[k];
            w.I32(segs.Length);
            foreach (var s in segs) { w.I64(s.Id.Value); w.I32(s.Start); w.I32(s.Len); }
        }
    }

    private static void ReadDemotions(CkptReader r, Dictionary<int, TapeEventSeg[]> d)
    {
        d.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++)
        {
            int k = r.I32();
            var segs = new TapeEventSeg[r.I32()];
            for (int j = 0; j < segs.Length; j++) segs[j] = new TapeEventSeg(new TapeEventID(r.I64()), r.I32(), r.I32());
            d[k] = segs;
        }
    }

    // content-hash → PROVENANCE-WEIGHTED occurrence count over the recent tail of the tape (spans with the highest
    // stable ids — the "working window" the promotion recurrence reads). An evidence span counts wScale, an unvested
    // replay counts 1 — so at wScale=1 this is exactly the old +1-per-span census, and armed it denies the
    // machine's own echoes the power to re-promote what real usage demoted. Ids are monotonic, so the window IS
    // the id range off the HIGH-WATER (never the resident count — they diverge under shedding); the shed recency
    // guard keeps the window resident, and a non-resident id (evacuated) is simply not working-set recurrence.
    private static Dictionary<int, int> RecentContentCounts(Tape tape, int wScale)
    {
        var counts = new Dictionary<int, int>();
        for (long v = Math.Max(0, tape.NextId - PromoteWindowEvents); v < tape.NextId; v++)
        {
            int pos = tape.PositionOf(new TapeEventID(v));
            if (pos < 0) continue;                          // evacuated/dropped — out of the working window by definition
            int h = Tape.ContentHash(tape.ResidentEventBytes[pos]);
            counts[h] = counts.GetValueOrDefault(h) + (tape.IsEvidenceAt(pos) ? wScale : 1);
        }
        return counts;
    }

    // ── THE REVERSE INDEX ──  concept → the spans that CONTAIN it. The gret inverted-postings idea, a small
    // honest subset: the grammar's distinct SPAN-SCALE rule-expansions are the concepts; each maps to the tape spans
    // it occurs in (substring). The length band is deliberate — a concept must be SPAN-SCALE to have postings: below
    // MinConceptLen it is a noise morpheme, above MaxConceptLen it is a multi-line mega-rule that crosses newline
    // boundaries and so is NEVER a substring of any single span (the memorization the GC evicts, not an index
    // concept). Compact by construction (longest-in-band first, capped postings) — the self-indexing tape, an Index
    // event in the journal. Returns the counts + a single-line summary for the journal.
    public (int Concepts, int Postings, string Summary) BuildIndex(RePairResult g, Tape tape)
    {
        const int MinConceptLen = 8, MaxConceptLen = 96, MaxConcepts = 48, MaxPostings = 12, TopInLine = 8;
        const int MaxScanEvents = 4096;   // report prefix; matching ids are retained so Δ updates and drops stay exact
        if (!ReferenceEquals(_conceptTape, tape))
        {
            _conceptTape = tape;
            _conceptIndexedEvents = 0;
            _conceptRead = null;
            foreach (var concept in _conceptsByHash.Values) concept.EventIDs = new();
            _activeConcepts = null;
            _activeConceptHashes.Clear();
        }
        long tapeRevision = tape.Revision.Value;
        long orderRevision = tape.OrderRevision.Value;
        if (ReferenceEquals(_conceptTape, tape)
            && ReferenceEquals(_conceptRules, g.Rules)
            && _conceptAlphabet == g.AlphabetSize
            && _conceptTapeRevision == tapeRevision
            && _conceptOrderRevision == orderRevision
            && _conceptRead is { } cached)
            return cached;

        _ = SurfaceBits(g, out var lens);
        bool grammarChanged = !ReferenceEquals(_conceptRules, g.Rules) || _conceptAlphabet != g.AlphabetSize || _activeConcepts is null;
        List<ConceptSurface> concepts;
        long activatedResidentScans = 0;
        long activationChecks = 0;
        int retired = 0;
        if (!grammarChanged)
        {
            concepts = _activeConcepts!;
        }
        else
        {
            var candidates = BuildConcepts(g, lens);
            candidates.Sort((a, b) => a.Bytes.Length != b.Bytes.Length
                ? b.Bytes.Length - a.Bytes.Length
                : a.Hash.CompareTo(b.Hash));
            if (candidates.Count > MaxConcepts) candidates.RemoveRange(MaxConcepts, candidates.Count - MaxConcepts);

            var nextActive = new HashSet<int>(candidates.Count);
            foreach (var concept in candidates) nextActive.Add(concept.Hash);

            foreach (int hash in _activeConceptHashes)
                if (!nextActive.Contains(hash) && _conceptsByHash.TryGetValue(hash, out var retiredConcept))
                {
                    // Clear the backing buckets, not just Count: long-lived runs
                    // must return the inactive surface's postings to the GC.
                    retiredConcept.EventIDs = new();
                    retired++;
                }

            long existingHighWater = _conceptIndexedEvents;
            foreach (var concept in candidates)
                if (!_activeConceptHashes.Contains(concept.Hash))
                {
                    AddResidentMatches(concept, tape, existingHighWater, ref activationChecks);
                    activatedResidentScans++;
                }

            _activeConceptHashes.Clear();
            foreach (int hash in nextActive) _activeConceptHashes.Add(hash);
            concepts = candidates;
            _activeConcepts = concepts;
        }

        List<ConceptSurface> BuildConcepts(RePairResult grammar, long[] lengths)
        {
            var activeHashes = new HashSet<int>();
            var result = new List<ConceptSurface>();
            for (int i = 0; i < grammar.Rules.Length; i++)
            {
                if (lengths[i] < MinConceptLen || lengths[i] > MaxConceptLen) continue;
                RuleID id = grammar.Rules[i].Id;
                if (!_ruleConceptHashes.TryGetValue(id, out int hash))
                {
                    var e = Reconstruct.Expand(grammar.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
                    hash = Tape.ContentHash(e);
                    _ruleConceptHashes[id] = hash;
                    if (!_conceptsByHash.ContainsKey(hash))
                        _conceptsByHash.Add(hash, new ConceptSurface(e, hash));
                }
                if (activeHashes.Add(hash)) result.Add(_conceptsByHash[hash]);
            }
            return result;
        }

        long hi = tape.NextId;
        long deltaEvents = Math.Max(0, hi - _conceptIndexedEvents);
        long deltaChecks = 0;
        if (concepts.Count > 0)
        {
            for (long v = _conceptIndexedEvents; v < hi; v++)
            {
                var id = new TapeEventID(v);
                if (!tape.Resolve(id, out var bytes)) continue;
                foreach (var concept in concepts)
                {
                    deltaChecks++;
                    if (bytes.AsSpan().IndexOf(concept.Bytes.AsSpan()) >= 0) concept.EventIDs.Add(v);
                }
            }
        }
        _conceptIndexedEvents = hi;

        int scanLimit = Math.Min(tape.ResidentEventBytes.Count, MaxScanEvents);
        int totalPostings = 0;
        var perConcept = new List<(string Preview, int Count)>(concepts.Count);
        foreach (var concept in concepts)
        {
            int count = 0;
            foreach (long idValue in concept.EventIDs)
            {
                int pos = tape.PositionOf(new TapeEventID(idValue));
                if (pos >= 0 && pos < scanLimit && ++count >= MaxPostings) break;
            }
            totalPostings += count;
            var preview = System.Text.Encoding.UTF8.GetString(concept.Bytes, 0, Math.Min(12, concept.Bytes.Length)).Replace('\t', ' ').Replace('\n', ' ');
            perConcept.Add((preview, count));
        }
        // the journal preview shows the POSTING-RICH concepts (the recurring idioms the reverse index actually recalls)
        var line = string.Join(" · ", perConcept.Where(p => p.Count > 0).OrderByDescending(p => p.Count).Take(TopInLine).Select(p => $"{p.Preview}→{p.Count}"));
        _conceptRules = g.Rules;
        _conceptAlphabet = g.AlphabetSize;
        _conceptTapeRevision = tapeRevision;
        _conceptOrderRevision = orderRevision;
        _conceptRead = (concepts.Count, totalPostings,
            $"concepts={concepts.Count} postings={totalPostings} active={concepts.Count} historical={_conceptsByHash.Count} "
            + $"delta_events={deltaEvents} active_checks={deltaChecks} activated={activatedResidentScans} "
            + $"activation_checks={activationChecks} retired={retired} · {line}");
        return _conceptRead.Value;

        static void AddResidentMatches(ConceptSurface concept, Tape tape, long existingHighWater, ref long checks)
        {
            var spans = tape.ResidentEventBytes;
            var ids = tape.ResidentEventIDs;
            for (int i = 0; i < spans.Count; i++)
            {
                if (ids[i].Value >= existingHighWater) continue;
                checks++;
                if (spans[i].AsSpan().IndexOf(concept.Bytes.AsSpan()) >= 0) concept.EventIDs.Add(ids[i].Value);
            }
        }
    }

    // ── THE NAVIGABILITY PROBE ──  mean shortest-path length over the tape's
    // affinity graph: each span links to its top-k most-affine spans (the couplings' k-NN), BFS a seeded sample of
    // span pairs. A small mean over a large tape = a navigable small-world memory (a few hops between any concepts);
    // a large / disconnected mean = an unbrowsable heap. Reads the SAME affinity Seriate.LineAffinity built for the
    // defrag, so it costs one extra k-NN + a handful of BFS walks. Returns the mean over reachable pairs (∞ dropped).
    public static double Navigability(double[][] aff, int n, ulong seed, int k = 6, int samples = 64)
        => NavigabilityOver(KnnGraph(aff, n, k), n, seed, samples);

    /// The affinity-kNN edge source — each span links to its top-k most-affine spans (strongest affinity first, id
    /// tie-break). The φ-graph the small-world read walked before Mount 3 added the SimHash bucket graph as a second.
    private static int[][] KnnGraph(double[][] aff, int n, int k)
    {
        var adj = new int[n][];
        var nbr = new List<(double A, int J)>(n);
        for (int i = 0; i < n; i++)
        {
            nbr.Clear();
            for (int j = 0; j < n; j++) if (j != i) nbr.Add((aff[i][j], j));
            nbr.Sort((x, y) => x.A != y.A ? y.A.CompareTo(x.A) : x.J.CompareTo(y.J));   // strongest affinity first, id tie-break
            int take = Math.Min(k, nbr.Count);
            adj[i] = new int[take];
            for (int t = 0; t < take; t++) adj[i][t] = nbr[t].J;
        }
        return adj;
    }

    /// MOUNT 3 — the BFS mean-path core over ANY edge source. The affinity-kNN graph
    /// (KnnGraph) and the SimHash BUCKET graph (SimhashIndex.BucketGraph — spans linked to their LSH bucket
    /// co-members, the hub structure) both feed it, so the trunk can report the mean path length over bucket-hops
    /// vs the affinity-kNN graph (which memory is more navigable — the "5 clicks" small-world read). Seeded pair
    /// sampling, BFS, mean over reachable pairs (∞ dropped). Deterministic (the Vow: same seed ⇒ same mean).
    public static double NavigabilityOver(int[][] adj, int n, ulong seed, int samples = 64)
    {
        if (n < 3) return 0;
        ulong rng = seed;
        int Next(int m) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)m); }
        long sum = 0; int reached = 0;
        var dist = new int[n];
        var queue = new Queue<int>(n);
        for (int s = 0; s < samples; s++)
        {
            int src = Next(n), dst = Next(n);
            if (src == dst) continue;
            Array.Fill(dist, -1);
            dist[src] = 0; queue.Clear(); queue.Enqueue(src);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                if (u == dst) break;
                foreach (int v in adj[u]) if (dist[v] < 0) { dist[v] = dist[u] + 1; queue.Enqueue(v); }
            }
            if (dist[dst] >= 0) { sum += dist[dst]; reached++; }
        }
        return reached == 0 ? 0 : (double)sum / reached;
    }

    /// The night-shift's bucket-graph navigability, off the index's STANDING adjacency (AdjOf — maintained O(1)
    /// per feed, never rebuilt): the same seeded-pair BFS as NavigabilityOver(int[][]) reading the same rows
    /// BucketGraph(12) would materialize, minus the nightly O(slots × hub-prefix) rebuild that owned the night
    /// wall. Same seed ⇒ same mean as the eager walk (row-identity is SimhashIndex.AdjOf's contract).
    public static double NavigabilityOver(SimhashIndex idx, ulong seed, int samples = 64)
    {
        int n = idx.Count;
        if (n < 3) return 0;
        ulong rng = seed;
        int Next(int m) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)m); }
        long sum = 0; int reached = 0;
        var dist = new int[n];
        var queue = new Queue<int>(n);
        for (int s = 0; s < samples; s++)
        {
            int src = Next(n), dst = Next(n);
            if (src == dst) continue;
            Array.Fill(dist, -1);
            dist[src] = 0; queue.Clear(); queue.Enqueue(src);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                if (u == dst) break;
                foreach (int v in idx.AdjOf(u)) if (dist[v] < 0) { dist[v] = dist[u] + 1; queue.Enqueue(v); }
            }
            if (dist[dst] >= 0) { sum += dist[dst]; reached++; }
        }
        return reached == 0 ? 0 : (double)sum / reached;
    }

    /// Owner-local bucket navigability read. The standing index mutates only by
    /// appending slots, so Count is the exact epoch for this seeded sample; repeated
    /// light/full consolidation phases with no index delta return without another BFS walk.
    public double NavigabilityOver(ulong seed, int samples = 64)
    {
        if (_navCount == _idx.Count && _navSeed == seed && _navSamples == samples)
            return _navRead;
        _navCount = _idx.Count;
        _navSeed = seed;
        _navSamples = samples;
        _navRead = NavigabilityOver(_idx, seed, samples);
        return _navRead;
    }
}
