namespace Cogito.Induct;

using System.Buffers;
using System.Runtime.InteropServices;
using Cogito.Cas;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Observe;

// The learner — compression as consolidation. Re-Pair finds recurring structure; the MDL gate
// keeps only what pays for itself; the Consolidator runs the loop and emits the mutation packet.

/// MDL scoring in milli-bits. The acceptance gate: a rule lives iff it shortens total description length.
public static class Mdl
{
    /// Re-Pair savings for a pair seen `count` times over vocabulary `vocab`: Δ = (count − 2)·log₂|V|.
    /// The rule-of-three as arithmetic — a pair must recur 3+ times to be worth a rule.
    public static Mbits PairDelta(int count, int vocab) => (count - 2) * Fixed.Log2((uint)vocab);

    /// The PROVENANCE-WEIGHTED savings — `wcount` is the Σ of per-occurrence evidence
    /// weights (an evidence occurrence weighs wScale, an unvested Replay echo weighs 1 — Tape.WeightsFor), so
    /// Δ = (wcount − 2·wScale)·log₂|V| / wScale re-expresses the savings in INDEPENDENT-EVIDENCE units: the MDL
    /// pay-floor's hidden premise (recurrences are independent samples of reality) is restored by construction.
    /// All-integer, deterministic; the division truncates toward zero (conservative under-credit on mixed-weight
    /// counts, never a drift). EXACT degeneracies: wScale=1 reproduces the unweighted PairDelta identically, and a
    /// UNIFORM-evidence tape (every occurrence weighs wScale) makes (wcount − 2·wScale) divisible by wScale, so any
    /// wScale reproduces the unweighted arithmetic exactly — the control arm is arithmetic identity, not tolerance.
    public static Mbits PairDelta(long wcount, int vocab, int wScale) => new((wcount - 2L * wScale) * Fixed.Log2((uint)vocab).Value / wScale);

    /// L(G) = a fixed 1024-mbit grammar header plus each rule's own description length.
    public static Mbits GrammarCost(GrammarSpec g)
    {
        Mbits total = new(1024);
        for (int i = 0; i < g.Rules.Count; i++) total += g.Rules[i].Cost;
        return total;
    }

    /// L(residual) = 8-mbit framing + 8000 mbit (= 8 bits) per literal byte left unexplained.
    public static Mbits ResidualCost(ReadOnlySpan<byte> residual) => new(8000L * residual.Length + 8);
}

/// Deterministic greedy pair-merge — the fast-BPE-trainer architecture, built for cache, not for big-O.
/// The tape stays a FLAT int[]; we never pointer-chase a whole-sequence linked list (that trades bandwidth
/// for latency on a multi-million-symbol working set). Instead:
///   • each active digram owns an occurrence-position list (indices into the flat tape)
///   • a frequency bucket-queue ranks digrams; the winner is O(1) to find
///   • merging the winner touches ONLY its occurrence positions (≈ O(n) total work), locally fixing the
///     two neighbor digrams' counts + position lists — no whole-tape rescan, no sequence list to walk
/// Scatter is bounded to hot occurrences (which shrink every merge), never the whole working set; the
/// position lists are pooled, so the inner loop is zero-alloc.
/// Tie-break: max count, then smallest packed pair key ((a<<32)|b). Event boundaries are hard barriers,
/// ENFORCED by the `barrier` terminal: a digram touching it is never a merge candidate, so — by induction —
/// no rule ever contains it and no rule can straddle the boundary it marks. The barrier is ordinary tape
/// content (it survives as a lone terminal in the compressed output, reconstruction stays byte-exact); it is
/// only barred from RULES. Callers with an in-band delimiter pass it ('\n' for span/line corpora — the
/// engine's Tape.Concat newline-joins spans, so one terminal marks both boundaries); callers whose spans abut
/// with no delimiter insert the barrier terminal at each joint. Cross-boundary digrams are coincidence, not
/// structure: a straddling rule maps to no single tape span (undemotable under the memory budget) and
/// self-blocks GrammarCover's non-overlapping greedy cover (adjacent lines fight over the shared newline).
/// v0 bring-up MAY use the trivial recount+compact pass (still flat + fully sequential, just O(n·rules));
/// swap in occurrence-lists once green. Across consolidation runs, re-induce only over new/affected spans.
public sealed class RePair
{
    /// No terminal is a barrier — every digram is a candidate. uint.MaxValue can never be a real symbol
    /// (terminals < alphabetSize, nonterminals = alphabetSize + ruleIndex), so it is the safe "off" value.
    public const uint NoBarrier = uint.MaxValue;

    // Reused across passes AND across Induce calls — cleared per pass, capacity retained.
    private readonly Dictionary<long, int> _counts = new();

    /// Linear-time Re-Pair — the deployable induction, BYTE-IDENTICAL to InduceReference but O(n) not O(n·rules).
    /// The reference re-tallies EVERY digram of the whole buffer each pass (O(len) hashing × R passes = the chug
    /// that made a 1MB anchor cost minutes/step). Here the sequence is a doubly-linked list; digram counts are
    /// maintained INCREMENTALLY (only the O(1) boundaries around each merge change); a lazy max-heap
    /// (max-count, smallest-key tie-break — the same total order) yields the winner without a per-pass scan; and
    /// occurrence lists let a merge touch O(occurrences) instead of rewriting the whole buffer. Same winner, same
    /// non-overlapping left-to-right rewrite, same rule order ⇒ identical grammar. (`verify-induct` is the gate.)
    ///
    /// `events`, when armed, captures the per-merge THOUGHT STREAM — one MergeEvent per rule-birth, in decision
    /// order (the strange-loop substrate; see MergeEvent). It is OFF by default and zero-cost when off: the
    /// depth/span tracking it needs is allocated and maintained only when a caller passes a buffer, so the gate's
    /// hot path pays nothing. The emit fires ONCE per mint (O(rules)), never inside the O(occurrences) rewrite,
    /// and the grammar output is byte-identical with or without it — the stream is a pure observation.
    ///
    /// `weights`/`wScale` arm the PROVENANCE-WEIGHTED count measure: weights[i] is position i's evidence
    /// weight (Tape.WeightsFor — wScale on evidence spans, 1 on unvested Replay), counts become Σweights, the mint
    /// gate scales to 3·wScale, and Mdl.PairDelta normalizes back to independent-evidence units. Empty weights =
    /// uniform wScale everywhere (today's all-evidence assumption made explicit), so wScale=1 + empty weights is
    /// BYTE-IDENTICAL to the pre-provenance inducer — the control arm. CONTRACT: weights must be constant across
    /// each barrier-delimited segment (per-span provenance guarantees it); that is what lets a merge site fetch
    /// ONE weight — the barrier law proves p,i,j,q all sit in one span, so w[i] serves all four digram updates.
    public RePairResult Induce(ReadOnlySpan<Symbol> tape, Mbits tau, uint alphabetSize = 256, List<MergeEvent>? events = null, uint barrier = NoBarrier, ReadOnlySpan<byte> weights = default, int wScale = 1)
    {
        Tape.RequireWScale(wScale);
        int len0 = tape.Length;
        if (!weights.IsEmpty && weights.Length < len0) throw new ArgumentException($"weights cover {weights.Length} of {len0} positions", nameof(weights));
        if ((long)len0 * wScale > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(wScale), $"weighted counts overflow int at {len0} symbols × wScale {wScale}");
        int bar = unchecked((int)barrier);                 // the unmergeable terminal, in the int working alphabet
        var rules = new List<GrammarRule>();
        long savings = 0;
        // The thought stream's RG scale + correlation length, tracked in lockstep with `rules` — ONLY when armed.
        // Off, these stay null and the emit block below is skipped: no lists, no per-mint work, no allocation.
        List<int>? evtDepth = events != null ? new(256) : null;
        List<int>? evtSpan  = events != null ? new(256) : null;
        if (len0 < 2)
        {
            var only = new Symbol[len0];
            for (int i = 0; i < len0; i++) only[i] = tape[i];
            return new RePairResult([], only, Mbits.Zero, alphabetSize);
        }

        int cap = len0;
        int[] sym = ArrayPool<int>.Shared.Rent(cap);      // symbol per slot
        int[] nxt = ArrayPool<int>.Shared.Rent(cap);      // → next live slot (len0 = end sentinel)
        int[] prv = ArrayPool<int>.Shared.Rent(cap);      // → prev live slot (−1 = start)
        bool[] dead = ArrayPool<bool>.Shared.Rent(cap);   // slot removed by a merge (the right half)
        byte[] wt = ArrayPool<byte>.Shared.Rent(Math.Max(cap, 1));   // per-ORIGINAL-position weight — positions never compact here, so wt[i] stays valid across merges
        try
        {
            Array.Clear(dead, 0, cap);
            for (int i = 0; i < len0; i++) { sym[i] = (int)tape[i].Value; nxt[i] = i + 1; prv[i] = i - 1; }
            if (weights.IsEmpty) wt.AsSpan(0, len0).Fill((byte)wScale); else weights[..len0].CopyTo(wt);

            var cnt = _counts; cnt.Clear();
            var occ = new Dictionary<long, List<int>>();                   // digram → candidate left-positions (lazy: validated on use)
            var heap = new PriorityQueue<long, (long NegCount, long Key)>(); // element = digram key, priority orders winner first

            static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
            void Push(long key, int c) => heap.Enqueue(key, (-(long)c, key));
            void Inc(long key, int pos, int w)
            {
                int c = cnt.GetValueOrDefault(key) + w; cnt[key] = c;
                (occ.TryGetValue(key, out var l) ? l : occ[key] = new()).Add(pos);
                Push(key, c);
            }
            void Dec(long key, int w)
            {
                if (!cnt.TryGetValue(key, out int c)) return;
                c -= w;
                if (c <= 0) cnt.Remove(key); else { cnt[key] = c; Push(key, c); }   // occ cleaned lazily on use
            }

            for (int i = 0; i + 1 < len0; i++)                                                 // initial tally (self-overlaps counted;
                if (sym[i] != bar && sym[i + 1] != bar) Inc(Pack(sym[i], sym[i + 1]), i, wt[i]); // barrier digrams never candidates)
            int liveLen = len0;

            while (liveLen >= 2)
            {
                // winner = max count, smallest key — pop stale heap entries (a count that no longer matches cnt).
                long bestKey = 0; int bestCount = 0; bool found = false;
                while (heap.TryDequeue(out var key, out var pr))
                    if (cnt.TryGetValue(key, out int cur) && cur == (int)(-pr.NegCount)) { bestKey = key; bestCount = cur; found = true; break; }
                if (!found) break;

                int vocab = (int)alphabetSize + rules.Count;
                Mbits delta = Mdl.PairDelta(bestCount, vocab, wScale);
                if (bestCount < 3 * wScale || delta < tau) break;             // rule-of-three in evidence units: 3 real exercises OR 3·wScale dream echoes

                int a = (int)(bestKey >> 32), b = (int)(bestKey & 0xFFFFFFFFL);
                uint n = alphabetSize + (uint)rules.Count;

                // rewrite: left-to-right, non-overlapping, over the winner's occurrences (validated + sorted).
                if (occ.TryGetValue(bestKey, out var positions))
                {
                    positions.Sort();
                    foreach (int i in positions)
                    {
                        if (dead[i]) continue;
                        int j = nxt[i];
                        if (j >= len0 || dead[j] || sym[i] != a || sym[j] != b) continue;   // stale / consumed by a prior overlap
                        int p = prv[i], q = nxt[j];
                        int mw = wt[i];                                                    // ONE weight fetch serves the whole merge site: the barrier law puts p,i,j,q in one span (weights are span-constant)
                        if (p >= 0 && sym[p] != bar) Dec(Pack(sym[p], a), mw);              // (left,a) → will become (left,n)
                        if (q < len0 && sym[q] != bar) Dec(Pack(b, sym[q]), mw);            // (b,right) → will become (n,right)
                        Dec(bestKey, mw);                                                  // this (a,b) consumed
                        sym[i] = (int)n; dead[j] = true; nxt[i] = q; if (q < len0) prv[q] = i;
                        liveLen--;
                        if (p >= 0 && sym[p] != bar) Inc(Pack(sym[p], (int)n), p, mw);      // barrier neighbors stay dark —
                        if (q < len0 && sym[q] != bar) Inc(Pack((int)n, sym[q]), i, mw);    // same counts a guarded re-tally sees
                    }
                    occ.Remove(bestKey);
                }

                Span<byte> ccc = stackalloc byte[16];
                var cw = new CccWriter(ccc);
                cw.U64(2); cw.U32((uint)a); cw.U32((uint)b);
                RuleID id = Hash.Rule(ccc[..cw.Written]);
                Mbits cost = new(256 + 8000L * cw.Written);
                rules.Add(new GrammarRule(id, [new Symbol((uint)a), new Symbol((uint)b)], cost));
                savings += delta.Value;

                if (events != null)
                {
                    // The new rule's RG scale = 1 + max child depth; its span = Σ child spans (terminal ⟹ depth 0,
                    // span 1). Children are earlier rules (a,b < n), so their depth/span are already recorded — the
                    // same bottom-up recurrence RenormStats runs after the merge, captured here at decision time.
                    int di = (int)alphabetSize;
                    int da = a < di ? 0 : evtDepth![a - di], db = b < di ? 0 : evtDepth![b - di];
                    int sa = a < di ? 1 : evtSpan![a - di],  sb = b < di ? 1 : evtSpan![b - di];
                    int d = 1 + Math.Max(da, db), s = sa + sb;
                    evtDepth!.Add(d); evtSpan!.Add(s);
                    events.Add(new MergeEvent(events.Count, new Symbol((uint)a), new Symbol((uint)b), new Symbol(n), bestCount, d, s, delta));
                }
            }

            var compressed = new Symbol[liveLen];                          // harvest: walk the live list from head (slot 0 is never a right-half); liveLen is EXACT, so fill the product array directly — no List→ToArray double-alloc
            int c = 0;
            for (int i = 0; i < len0; i = nxt[i]) compressed[c++] = new Symbol((uint)sym[i]);
            return new RePairResult(rules.ToArray(), compressed, new Mbits(savings), alphabetSize);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(sym);
            ArrayPool<int>.Shared.Return(nxt);
            ArrayPool<int>.Shared.Return(prv);
            ArrayPool<bool>.Shared.Return(dead);
            ArrayPool<byte>.Shared.Return(wt);
        }
    }

    /// The O(n·rules) reference induction — re-tally, scan for the winner, compact-rewrite, per pass. Retained as
    /// the correctness ORACLE the linear Induce must match byte-for-byte (`cogito verify-induct`); never the hot
    /// path. `weights`/`wScale` mirror Induce's provenance-weighted count measure (the weight buffer compacts in
    /// lockstep with the symbol buffer — a merged pair keeps its LEFT weight, the same weight by the span-constant
    /// contract), so the weighted linear path has an independent oracle too.
    public RePairResult InduceReference(ReadOnlySpan<Symbol> tape, Mbits tau, uint alphabetSize = 256, uint barrier = NoBarrier, ReadOnlySpan<byte> weights = default, int wScale = 1)
    {
        Tape.RequireWScale(wScale);
        var rules = new List<GrammarRule>();
        long savings = 0;
        int len = tape.Length;
        if (!weights.IsEmpty && weights.Length < len) throw new ArgumentException($"weights cover {weights.Length} of {len} positions", nameof(weights));
        if ((long)len * wScale > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(wScale), $"weighted counts overflow int at {len} symbols × wScale {wScale}");
        int bar = unchecked((int)barrier);
        int[] buf = ArrayPool<int>.Shared.Rent(Math.Max(len, 1));
        byte[] wbuf = ArrayPool<byte>.Shared.Rent(Math.Max(len, 1));
        try
        {
            // ── Seed: flatten the symbol tape into the int working buffer (+ the parallel weight buffer) ──
            for (int i = 0; i < len; i++) buf[i] = (int)tape[i].Value;
            if (weights.IsEmpty) wbuf.AsSpan(0, len).Fill((byte)wScale); else weights[..len].CopyTo(wbuf);

            // ── Greedy merge loop: each pass mints at most one nonterminal ──
            while (len >= 2)
            {
                // Tally every adjacent digram (self-overlaps ARE counted — v0 accepts the overcount;
                // the left-to-right rewrite below replaces only the non-overlapping subset).
                _counts.Clear();
                for (int i = 0; i + 1 < len; i++)
                {
                    if (buf[i] == bar || buf[i + 1] == bar) continue;      // barrier digrams never candidates
                    long key = ((long)buf[i] << 32) | (uint)buf[i + 1];
                    ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_counts, key, out _);
                    slot += wbuf[i];
                }

                // Winner = max count, tie-break SMALLEST packed key. That total order makes the pick
                // independent of dictionary iteration order — the first half of the determinism guarantee.
                long bestKey = 0; int bestCount = 0; bool found = false;
                foreach (var (key, count) in _counts)
                    if (!found || count > bestCount || (count == bestCount && key < bestKey))
                    { bestCount = count; bestKey = key; found = true; }
                if (!found) break;

                // MDL gate. vocabSize = alphabetSize terminals + nonterminals minted so far: the working
                // alphabet's size, monotone and scan-free (a distinct-symbol census would alloc + cost
                // O(n) each pass for zero determinism gain). log₂|V| is the per-reference symbol price.
                // alphabetSize = 256 for byte corpora; a token alphabet passes its own (denser) terminal count.
                int vocab = (int)alphabetSize + rules.Count;
                Mbits delta = Mdl.PairDelta(bestCount, vocab, wScale);
                if (bestCount < 3 * wScale || delta < tau) break;

                int a = (int)(bestKey >> 32);
                int b = (int)(bestKey & 0xFFFFFFFFL);
                uint n = alphabetSize + (uint)rules.Count;

                // Compacting rewrite in place: greedy, non-overlapping, left-to-right. Output ≤ input, so
                // write cursor w never overtakes read cursor r — safe to mutate buf as we go. This + the
                // tie-break above is what makes induction bit-identical across runs. The weight buffer
                // compacts in lockstep (a merge keeps the LEFT weight — same weight, span-constant contract).
                // TODO(perf): maintain per-digram occurrence-position lists so a merge touches
                // O(occurrences) instead of rescanning + rewriting the whole buffer.
                int w = 0;
                for (int r = 0; r < len;)
                {
                    if (r + 1 < len && buf[r] == a && buf[r + 1] == b) { wbuf[w] = wbuf[r]; buf[w++] = (int)n; r += 2; }
                    else { wbuf[w] = wbuf[r]; buf[w++] = buf[r]; r += 1; }
                }
                len = w;

                // Mint the content-addressed rule: id over ccc(pattern) = LE64(2) ‖ U32(a) ‖ U32(b).
                Span<byte> ccc = stackalloc byte[16];
                var cw = new CccWriter(ccc);
                cw.U64(2);
                cw.U32((uint)a);
                cw.U32((uint)b);
                RuleID id = Hash.Rule(ccc[..cw.Written]);
                Mbits cost = new(256 + 8000L * cw.Written);
                rules.Add(new GrammarRule(id, [new Symbol((uint)a), new Symbol((uint)b)], cost));

                savings += delta.Value;
            }

            // ── Harvest: the shrunken buffer is the compressed tape ──
            var compressed = new Symbol[len];
            for (int i = 0; i < len; i++) compressed[i] = new Symbol((uint)buf[i]);
            return new RePairResult(rules.ToArray(), compressed, new Mbits(savings), alphabetSize);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buf);
            ArrayPool<byte>.Shared.Return(wbuf);
        }
    }
}

public readonly struct RePairResult(GrammarRule[] rules, Symbol[] compressed, Mbits totalSavings, uint alphabetSize = 256)
{
    public readonly GrammarRule[] Rules = rules;
    public readonly Symbol[] Compressed = compressed;
    public readonly Mbits TotalSavings = totalSavings;
    public readonly uint AlphabetSize = alphabetSize;   // terminal/nonterminal boundary: 256 for bytes, K for a token alphabet
}

/// One merge in the induction's THOUGHT STREAM — the time-ordered record of a single rule-birth. Re-Pair's
/// pillar-1 operation IS cognition: each step greedily coarse-grains the most-frequent adjacent pair into a new
/// symbol, and the SEQUENCE of those decisions is cogito's literal thinking. Emitting one MergeEvent per mint
/// turns induction into an observable stream that can itself be tokenized and RE-INDUCED (the grammar of its own
/// thinking — the strange-loop substrate the self-model cluster feeds on: dream-of-cognition, the tower).
///   `Count`     pair frequency AT merge time — the greedy salience, the intrinsic importance at decision time
///               (under an armed provenance-weight measure this IS the weighted count — salience in evidence units).
///   `Depth`     the rule's RG scale (1 + max child depth); terminal children are depth 0. The coarse-graining level.
///   `Span`      the rule's correlation length (Σ child spans, byte extent); terminal children are span 1.
///   `MdlDelta`  the description-length the merge earned — (Count−2)·log₂|V|, the MDL gate's verdict at mint.
/// `A`/`B` are the merged symbols (terminal if &lt; alphabet size, else an earlier nonterminal); `NewSymbol` is
/// the minted nonterminal (= alphabetSize + Step, hence derivable — kept explicit so a reader needn't recompute).
public readonly struct MergeEvent(int step, Symbol a, Symbol b, Symbol newSymbol, int count, int depth, int span, Mbits mdlDelta)
{
    public readonly int Step = step;
    public readonly Symbol A = a;
    public readonly Symbol B = b;
    public readonly Symbol NewSymbol = newSymbol;
    public readonly int Count = count;
    public readonly int Depth = depth;
    public readonly int Span = span;
    public readonly Mbits MdlDelta = mdlDelta;
}

/// The consolidation loop. Tokenizes the observation corpus, runs Re-Pair, MDL-gates, and — if the grammar
/// improved by ≥ tau — produces the next GrammarVersionEvent. Returns null at homeostasis (no win left).
public sealed class Consolidator(ContentStore store, ITokenizer tokenizer)
{
    public GrammarVersionEvent? Consolidate(EventLog log, GrammarSpec current, Mbits tau)
    {
        // ── Gather the observation tape: every ObsTextEvent's text, tokenized, '\n'-joined ──
        // (the plane separation — only observations feed the learner; grammar/theorem events are skipped).
        // The joint terminal marks the event boundary the barrier law needs: observations are separate
        // spans, so no digram — and by induction no rule — may straddle two of them. Same convention as
        // Tape.Concat's newline-join; assumes the byte alphabet (ByteTokenizer, the only tokenizer today —
        // a sub-word tokenizer must supply its own boundary token here).
        var tape = new List<Symbol>();
        for (var id = EventID.Zero; id.Value < (ulong)log.Count; id = id.Next)
        {
            var e = log[id];
            if (e.SchemaId.Value != ObsTextEvent.Schema) continue;
            var obs = ObsTextEvent.Decode(e);
            var utf8 = new CccReader(store.Get(obs.TextRef).Payload.Span).Bytes();   // TextBlob payload = CCC Bytes(utf8)
            var buf = new Symbol[tokenizer.MaxSymbols(utf8.Length)];
            int k = tokenizer.Tokenize(utf8, buf);
            for (int i = 0; i < k; i++) tape.Add(buf[i]);
            tape.Add(Symbol.Terminal((byte)'\n'));
        }
        if (tape.Count == 0) return null;

        // ── Re-Pair + MDL gate (the span barrier armed — no rule may cross an observation boundary) ──
        var result = new RePair().Induce(CollectionsMarshal.AsSpan(tape), tau, barrier: '\n');
        if (result.Rules.Length == 0) return null;                                   // homeostasis: nothing paid

        // ── Mint + store the new grammar version (content-addressed artifact) ──
        var merged = new GrammarRule[current.Rules.Count + result.Rules.Length];
        for (int i = 0; i < current.Rules.Count; i++) merged[i] = current.Rules[i];
        for (int i = 0; i < result.Rules.Length; i++) merged[current.Rules.Count + i] = result.Rules[i];
        var spec = GrammarSpec.WithRules(current.Version + 1, merged);
        var specRef = store.Put(spec.ToEnvelope());

        var rulesAdded = new List<RuleID>(result.Rules.Length);
        foreach (var r in result.Rules) rulesAdded.Add(r.Id);

        return new GrammarVersionEvent
        {
            Version = current.Version + 1,
            ParentVersion = current.Version,
            SpecRef = specRef,
            MdlDelta = result.TotalSavings,
            RulesAdded = rulesAdded,
            Window = (EventID.Zero, log.Last),
        };
    }
}
