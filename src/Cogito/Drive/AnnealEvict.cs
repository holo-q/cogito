namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── BREACH-AND-LOWER — annealing the INDUCTION ──
//
// Scarcity selects the generalizer over a fixed memory bound; this is the dual — an oscillating
// SEARCH budget over the induction itself. The structural observation the anneal exploits: Re-Pair's pay-floor
// (`bestCount < 3`, Induct.cs — PairDelta(2,V)=0, exactly break-even) is a DEPTH CEILING, because on any
// finite corpus the count spectrum decays with scale and the deepest recurring structure necessarily lives
// at count 2 (the whole corpus occurs once). Greedy-with-floor climbs until the next stratum's count drops
// below 3, then stops — it structurally cannot spend more compute. The wave:
//
//   BREACH  (inhale) admit the count-2 stratum: mint speculative break-even merges (cmin 3→2), up to a
//           quota (the amplitude). Each mint is MDL-neutral-minus-header at mint time; its value, if any,
//           is COMPOSITIONAL — it can only be judged after the chain it caps has formed.
//   LOWER   (exhale) distill under the strict budget, losslessly:
//             COMPACT  inline every rule used ≤1× into its sole reference site (u=0 → delete). Strictly
//                      MDL-positive (each inline deletes a header + a name ref) and it FLATTENS the
//                      speculative binary chains into few n-ary template rules — the cheap primitives.
//                      This step is LOAD-BEARING: without it, per-rule MDL reads every count-2 mint as
//                      exactly −header and the breach always evaporates; after compaction a deep template
//                      used 2× genuinely PAYS on train MDL (length beats the header).
//             EVICT    per-rule n-ary MDL rent at the FINAL state — the pay-floor re-applied post hoc,
//                      where mint-time PairDelta was myopic: evict iff ((u−1)·p − u)·log2|V| ≤ H.
//                      Junk binary 2×-caps die at −H; long 2×-templates live; classic ≥3-use rules live.
//             DEMOTE   the default retirement (the drive-faithful LOWER — Memory.Gc's move): a retired rule
//                      KEEPS its expansion in the cover basis at pointer rent (RefBits); re-minting the same
//                      expansion RE-PROMOTES it. `--lower delete` is the harsher ablation (basis loses it).
//             REGISTER every rent-evicted expansion's content-hash is recorded (mirrors MemoryHierarchy.
//                      _demoted). `--tabu` makes breach SKIP registered winners — measured ANTI-RATCHET on the
//                      ladder (27/30 vs 30/30 caps): a cap evicted in unpayable binary form must be free to
//                      RE-MINT over the re-centered tape, where compaction gives it a paying n-ary shape.
//                      Default OFF; the register stands as telemetry (`rvst` = re-derivation/skip events).
//             REFOLD   one strict pass (cmin=3) to fold any ≥3-count digrams the eviction exposed.
//   RATCHET each cycle's survivors are single symbols to the next tally, so count-2 pairs OF templates
//           (super-templates) become reachable — the EML K=41-vs-11 re-centering as a training schedule.
//           Measured concretely as the re-mint path: oscillate recovers ALL 30 ladder caps where the
//           one-shot inhale keeps 27 (its unpayable-form evictions never get a second chance).
//
// Everything is lossless by construction — eviction = inlining at all sites, so ExpandAll is invariant
// (asserted per phase; the tape stays the oracle). No RNG anywhere in the anneal: the wave is a square
// wave on the accept floor, winner order is (count desc, key asc), sweeps are index-ascending — the Vow.
//
// THE KILL-LINE (this verb): fixed-budget greedy vs one-shot breach vs the oscillating anneal, same
// corpus + seed, judged on held-out ParsedSize (the depth read, never coverage), RenormStats CvZ (the
// grok read), and total bits — with a held-out CONTROL split (fresh recombinations) so junk depth
// inflation would show as a control regression, not hide inside the win.

public static class AnnealEvict
{
    private const double HeaderBitsDefault = 1.024;   // per-rule header (bits) — mirrors the engine's 1024-mbit grammar header constant

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE SHAPE — the working grammar state, self-contained int symbols (terminal <256, rule i = 256+i)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Shape
    {
        public List<int> Tape;
        public List<int[]> Pats = new();     // rule i → pattern (binary at mint, n-ary after LOWER's compaction)
        public List<byte[]> Exps = new();    // rule i → full byte expansion (inline-INVARIANT — only remapped on renumber)

        // DEMOTE-mode LOWER (the drive-faithful half — Memory.Gc keeps a demoted rule's Pattern as the
        // reconstruction fallback, so GrammarCover NEVER loses the expansion): rules removed from the working
        // set land here (keyed by expansion Fnv), stay in the COVER BASIS, and are charged pointer rent
        // (RefBits, Memory.cs's RefBitsPerSeg) instead of their symbols. A re-mint of the same expansion is a
        // RE-PROMOTION (Memory.cs's recurrence promotion): the demoted copy is dropped, the rule is resident again.
        public Dictionary<int, byte[]> Demoted = new();

        public Shape(byte[] corpus)
        {
            Tape = new List<int>(corpus.Length);
            foreach (var b in corpus) Tape.Add(b);
        }

        private Shape() { Tape = new(); }

        /// The AESTIVATION-VERB's inverse of ToResult — open the LIVE grammar (a fresh InduceOutcomeCredited output: pure binary
        /// Expansion rules, children before parents) as a working Shape. Fails LOUD on any other body-kind or a
        /// forward reference: the breach anneal is defined over Re-Pair output; a slot/TapeRef reaching it means
        /// the mount point moved without moving this contract.
        public static Shape FromResult(in RePairResult g)
        {
            if (g.AlphabetSize != 256)
                throw new InvalidOperationException($"breach opens byte grammars only (alphabet {g.AlphabetSize})");
            var s = new Shape();
            s.Tape.Capacity = g.Compressed.Length;
            foreach (var sym in g.Compressed) s.Tape.Add((int)sym.Value);
            for (int i = 0; i < g.Rules.Length; i++)
            {
                var rule = g.Rules[i];
                if (rule.Kind != RuleBodyKind.Expansion)
                    throw new InvalidOperationException($"breach expects the fresh induce's pure Expansion grammar — rule {i} is {rule.Kind}");
                var pat = new int[rule.Pattern.Length];
                int len = 0;
                for (int j = 0; j < pat.Length; j++)
                {
                    int v = (int)rule.Pattern[j].Value;
                    if (v >= 256 + i) throw new InvalidOperationException($"breach: rule {i} references rule {v - 256} forward — not Re-Pair output");
                    pat[j] = v;
                    len += v < 256 ? 1 : s.Exps[v - 256].Length;
                }
                var exp = new byte[len];
                int o = 0;
                foreach (var v in pat)
                {
                    if (v < 256) exp[o++] = (byte)v;
                    else { var ce = s.Exps[v - 256]; ce.CopyTo(exp, o); o += ce.Length; }
                }
                s.Pats.Add(pat);
                s.Exps.Add(exp);
            }
            return s;
        }

        public byte[] ExpandAll()
        {
            var outp = new List<byte>(Tape.Count * 2);
            foreach (var s in Tape)
                if (s < 256) outp.Add((byte)s);
                else outp.AddRange(Exps[s - 256]);
            return outp.ToArray();
        }
    }

    private static readonly byte[][] SingleByte = Enumerable.Range(0, 256).Select(b => new[] { (byte)b }).ToArray();
    private static byte[] ExpOf(Shape s, int sym) => sym < 256 ? SingleByte[sym] : s.Exps[sym - 256];

    // streaming FNV-1a over one or two byte runs — the register key; both the mint-time candidate (two child
    // expansions) and the evict-time rule (one full expansion) hash the SAME byte sequence, so they agree.
    private static int Fnv(byte[] a, byte[]? b = null)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var x in a) { h ^= x; h *= 16777619; }
            if (b != null) foreach (var x in b) { h ^= x; h *= 16777619; }
            return (int)h;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  MINT — one strict/breach pass (the engine's greedy loop, reference style, with the floor as a knob)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// Repeatedly tally adjacent digrams over the working tape, mint the winner (max count, smallest packed
    /// key — the engine's total order) as a binary rule, rewrite left-to-right non-overlapping. `cmin`=3 is
    /// the engine's strict pay-floor; 2 is the BREACH floor. `quota`&lt;0 = to fixpoint. `tabu` skips winners
    /// whose expansion was rent-evicted by a previous LOWER (`revisits` counts them — with tabu null they are
    /// minted anyway and counted, the re-derivation waste measurement). Returns mints performed.
    ///
    /// `deep` re-orders EQUAL-COUNT winners by combined child span (desc) before the key tie-break — the AESTIVATION
    /// verb's inhale order. On the machine's own life tape the count-2 stratum is dominated by dream-junction
    /// terminal noise, and key-ascending spent the whole amplitude below the compositional frontier (quota
    /// 128-256 aestivations minted in full and LOWER evicted everything — trunk_0140/0142); span-descending mints the
    /// deepest reachable caps first ('s "propose deep abstractions greedy Re-Pair structurally can't reach",
    /// made the inhale's total order). The standalone kill-line arms keep deep=false — their committed reports
    /// ride the engine's order, and their ladder corpus is pure enough not to need the counter.
    private static int Mint(Shape s, int cmin, int quota, HashSet<int>? tabu, HashSet<int>? register,
                            bool barrier, ref long touches, ref int revisits, bool deep = false)
    {
        // Winner-order-IDENTICAL incremental rewrite of the reference loop (perf.md rebuild-O(total)-where-O(Δ):
        // the reference body re-tallied EVERY digram and rewrote the WHOLE tape per mint — O(quota·tape), the
        // measured 497s of a 608s breach aestivation at quota 2048 over a ~500k-symbol tape). Same planes as the Loom's
        // pump (incremental counts, occurrence postings validated at rewrite, a lazy max-heap), same total order
        // per arm — non-tabu: (count desc, span desc when `deep`, key asc); tabu: (count desc, key asc) with
        // tabu'd winners parked, counted once per mint (`revisits`), and re-armed after the pick — so every arm's
        // mint sequence, register interplay, and re-promotion behavior is unchanged. O(tape + merge work) per CALL
        // instead of O(tape) per mint.
        int mints = 0;
        var t = s.Tape;
        int n0 = t.Count;
        if (quota == 0 || n0 < 2) return 0;

        // SPAN BARRIER — the engine's law ("event boundaries are hard barriers", Induct.cs), enforced engine-wide
        //. Without it,
        // whole-line caps grow \n-straddling expansions ("\nline\n") that SELF-BLOCK in GrammarCover's
        // non-overlapping greedy cover (adjacent held-out lines fight over the shared newline — the measured
        // 60→115 regression). '\n' never merges ⇒ by induction no rule ever contains it. `--no-barrier` remains
        // as the straddle-trap ablation arm.
        bool Bar(int x, int y) => barrier && (x == '\n' || y == '\n');

        var sym = new int[n0]; var nxt = new int[n0]; var prv = new int[n0]; var dead = new bool[n0];
        for (int i = 0; i < n0; i++) { sym[i] = t[i]; nxt[i] = i + 1 < n0 ? i + 1 : -1; prv[i] = i - 1; }
        var counts = new Dictionary<long, int>();
        var occ = new Dictionary<long, List<int>>();
        var heap = new PriorityQueue<long, (long NegC, long NegSpan, long Key)>();
        int SpanOf(int sy) => sy < 256 ? 1 : s.Exps[sy - 256].Length;
        void Push(long key, int c)
            => heap.Enqueue(key, (-(long)c, deep ? -(long)(SpanOf((int)(key >> 32)) + SpanOf((int)(key & 0xFFFFFFFFL))) : 0L, key));
        void Inc(long key, int pos)
        {
            int c = counts.GetValueOrDefault(key) + 1; counts[key] = c;
            (occ.TryGetValue(key, out var l) ? l : occ[key] = new()).Add(pos);
            Push(key, c);
        }
        void Dec(long key)
        {
            if (!counts.TryGetValue(key, out int c)) return;
            c--;
            if (c <= 0) counts.Remove(key); else { counts[key] = c; Push(key, c); }   // occ cleaned lazily at rewrite
        }
        for (int i = 0; i + 1 < n0; i++)                               // the ONE tally — counts + postings, heap seeded once per distinct key below
        {
            if (Bar(sym[i], sym[i + 1])) continue;
            long key = ((long)sym[i] << 32) | (uint)sym[i + 1];
            counts[key] = counts.GetValueOrDefault(key) + 1;
            (occ.TryGetValue(key, out var l) ? l : occ[key] = new()).Add(i);
        }
        foreach (var (key, c) in counts) Push(key, c);                 // distinct keys ⇒ distinct priorities — heap order independent of dictionary order
        touches += n0;

        var parked = new List<(long Key, int C)>();                    // tabu'd winners this mint — re-armed after the pick
        var seenTabu = new HashSet<long>();                            // count each tabu key once per mint (the reference's distinct-candidate walk)
        while (quota != 0)
        {
            long bestKey = 0; int bestCount = 0; bool found = false;
            parked.Clear(); seenTabu.Clear();
            while (heap.TryDequeue(out long key, out var pr))
            {
                if (!counts.TryGetValue(key, out int cur) || cur != (int)(-pr.NegC)) continue;   // stale entry
                if (cur < cmin) { Push(key, cur); break; }             // heap top is the global max ⟹ nothing eligible remains
                if (tabu is { Count: > 0 } && tabu.Contains(Fnv(ExpOf(s, (int)(key >> 32)), ExpOf(s, (int)(key & 0xFFFFFFFFL)))))
                {
                    if (seenTabu.Add(key)) revisits++;                 // evicted stays evicted — spend the quota on NEW frontier
                    parked.Add((key, cur));
                    continue;
                }
                bestKey = key; bestCount = cur; found = true; break;
            }
            foreach (var (k, c) in parked) Push(k, c);                 // tabu'd candidates stay live for future mints
            if (!found) break;

            int a = (int)(bestKey >> 32), b = (int)(bestKey & 0xFFFFFFFFL);
            var expA = ExpOf(s, a); var expB = ExpOf(s, b);
            if (tabu is null && register is not null && register.Contains(Fnv(expA, expB))) revisits++;   // tabu OFF: measure the re-derivation waste

            int nt = 256 + s.Pats.Count;
            s.Pats.Add(new[] { a, b });
            var exp = new byte[expA.Length + expB.Length];
            expA.CopyTo(exp, 0); expB.CopyTo(exp, expA.Length);
            s.Exps.Add(exp);
            s.Demoted.Remove(Fnv(exp));                                   // re-admission: the pattern recurred and re-earned residency

            if (occ.TryGetValue(bestKey, out var positions))
            {
                positions.Sort();                                      // left-to-right — the reference's sequential non-overlapping scan
                foreach (int i in positions)
                {
                    if (dead[i] || sym[i] != a) continue;
                    int j = nxt[i];
                    if (j < 0 || dead[j] || sym[j] != b) continue;     // stale / consumed by a prior overlap
                    int p = prv[i], q = nxt[j];
                    if (p >= 0 && !Bar(sym[p], a)) Dec(((long)sym[p] << 32) | (uint)a);
                    if (q >= 0 && !Bar(b, sym[q])) Dec(((long)b << 32) | (uint)sym[q]);
                    Dec(bestKey);
                    sym[i] = nt; dead[j] = true; nxt[i] = q; if (q >= 0) prv[q] = i;
                    if (p >= 0 && !Bar(sym[p], nt)) Inc(((long)sym[p] << 32) | (uint)nt, p);
                    if (q >= 0 && !Bar(nt, sym[q])) Inc(((long)nt << 32) | (uint)sym[q], i);
                    touches++;
                }
                occ.Remove(bestKey);
            }
            mints++;
            if (quota > 0) quota--;
        }

        var outp = new List<int>();                                    // land the linked list back as the working tape
        for (int i = 0; i >= 0; i = nxt[i]) if (!dead[i]) outp.Add(sym[i]);
        s.Tape = outp;
        return mints;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  LOWER — compact (u≤1 inline) + rent-evict (the post-hoc pay-floor) + register, to fixpoint
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static int[] Uses(Shape s)
    {
        var u = new int[s.Pats.Count];
        foreach (var sym in s.Tape) if (sym >= 256) u[sym - 256]++;
        foreach (var p in s.Pats) foreach (var sym in p) if (sym >= 256) u[sym - 256]++;
        return u;
    }

    /// Remove `dead` rules by inlining their (recursively resolved) patterns at every reference site, then
    /// renumber the survivors. Children precede parents in emission order, so an ascending resolve pass fully
    /// flattens dead-into-dead chains. ExpandAll is invariant — this is the lossless eviction primitive.
    private static void Inline(Shape s, bool[] dead, ref long touches)
    {
        int n = s.Pats.Count;
        var resolved = new int[n][];
        for (int i = 0; i < n; i++)
        {
            if (!dead[i]) continue;
            var flat = new List<int>();
            foreach (var sym in s.Pats[i])
                if (sym >= 256 && dead[sym - 256]) flat.AddRange(resolved[sym - 256]);
                else flat.Add(sym);
            resolved[i] = flat.ToArray();
        }
        var map = new int[n]; int live = 0;
        for (int i = 0; i < n; i++) map[i] = dead[i] ? -1 : live++;

        List<int> Rw(IReadOnlyList<int> src)
        {
            var outp = new List<int>(src.Count);
            foreach (var sym in src)
            {
                if (sym < 256) { outp.Add(sym); continue; }
                int j = sym - 256;
                if (!dead[j]) { outp.Add(256 + map[j]); continue; }
                foreach (var r in resolved[j]) outp.Add(r < 256 ? r : 256 + map[r - 256]);   // resolved holds only live/terminal symbols
            }
            return outp;
        }

        touches += s.Tape.Count;
        s.Tape = Rw(s.Tape);
        var pats = new List<int[]>(live); var exps = new List<byte[]>(live);
        for (int i = 0; i < n; i++)
        {
            if (dead[i]) continue;
            pats.Add(Rw(s.Pats[i]).ToArray());
            exps.Add(s.Exps[i]);
        }
        s.Pats = pats; s.Exps = exps;
    }

    /// THE LOWER. Alternates COMPACT-fixpoint and one EVICT sweep until neither moves, then REFOLDs strict.
    /// Rent-evicted expansions go to `register` (the tracked unpaid speculation); compacted scaffold does NOT —
    /// its content paid, inside a surviving parent. Deterministic (index-ascending sweeps, batch inlines).
    private static (int Compacted, int Evicted, int Refolded) Lower(Shape s, double headerBits, HashSet<int> register,
                                                                    bool barrier, bool demote, ref long touches, ref int revisits)
    {
        // demote mode (default, the drive-faithful LOWER — Memory.Gc's move): a rule leaving the working set
        // KEEPS its expansion in the cover basis at pointer rent. Delete mode is the ablation arm that showed
        // why it matters: deletion starves the mid-basis (the held-firing 2×-idiom compacted INTO its outer
        // context vanishes from the cover) — real-code heldR went −2% under delete where the breach transient
        // had been +10%.
        void Retire(bool[] dead)
        {
            if (!demote) return;
            for (int i = 0; i < dead.Length; i++) if (dead[i]) s.Demoted[Fnv(s.Exps[i])] = s.Exps[i];
        }
        int compacted = 0, evicted = 0;
        while (true)
        {
            // COMPACT to fixpoint — deleting a u=0 rule drops its children's uses, so loop.
            while (true)
            {
                var u = Uses(s);
                var dead = new bool[s.Pats.Count]; int k = 0;
                for (int i = 0; i < s.Pats.Count; i++) if (u[i] <= 1) { dead[i] = true; k++; }
                if (k == 0) break;
                Retire(dead);
                Inline(s, dead, ref touches);
                compacted += k;
            }
            // EVICT one sweep — rent at the FINAL state: Δbits(evict) = ((u−1)·p − u)·log2|V| − H ≤ 0 ⇒ evict.
            {
                var u = Uses(s);
                double log2V = Math.Log2(256 + s.Pats.Count);
                var dead = new bool[s.Pats.Count]; int k = 0;
                for (int i = 0; i < s.Pats.Count; i++)
                {
                    int p = s.Pats[i].Length;
                    if (((double)(u[i] - 1) * p - u[i]) * log2V - headerBits <= 0)
                    { dead[i] = true; register.Add(Fnv(s.Exps[i])); k++; }
                }
                if (k == 0) break;
                Retire(dead);
                Inline(s, dead, ref touches);
                evicted += k;
            }
        }
        int refolded = Mint(s, cmin: 3, quota: -1, tabu: null, register: null, barrier, ref touches, ref revisits);
        return (compacted, evicted, refolded);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  READS — Engine-shared metrics per phase boundary (ParsedSize IS Engine.ParsedSize, the depth read)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private readonly record struct Row(
        string Arm, string Phase, int Cycle, long MintsCum, long TouchesCum, int Rules, int DemotedN, double Bits, long SurfaceBytes,
        int PsTrain, int PsHeldR, int PsHeldF, int Caps, int Scales, double MeanZ, double CvZ, double MaxSpan,
        int Compacted, int Evicted, int Revisits);

    /// The Engine-readable view. Demoted expansions ride along as terminal-pattern rules appended after the
    /// live set — exactly the drive's read (a Memory.Gc-demoted rule keeps its Pattern, so GrammarCover keeps
    /// its expansion in the basis); they are never referenced, so RuleUses reads them 0 and the Zipf levels
    /// filter them out. Live rules carry the ENGINE's rule tally (256 + 8000·ccc-bytes — RePair's own mint
    /// cost, n-ary via ccc(pattern) = LE64(len)‖U32·len); demoted rules carry pointer rent (RefBits). `savings`
    /// rides through because the drive's momentum verdict window (Reads.Step) enqueues TotalSavings — a zero
    /// here would read as a savings crash and thrash the WALL band.
    private static RePairResult ToResult(Shape s, Mbits savings)
    {
        var rules = new GrammarRule[s.Pats.Count + s.Demoted.Count];
        for (int i = 0; i < s.Pats.Count; i++)
        {
            var pat = new Symbol[s.Pats[i].Length];
            for (int j = 0; j < pat.Length; j++) pat[j] = new Symbol((uint)s.Pats[i][j]);
            rules[i] = new GrammarRule(GrammarRule.ComputeId(pat), pat, new Mbits(256 + 8000L * (8 + 4L * pat.Length)));
        }
        int k2 = s.Pats.Count;
        foreach (var h in s.Demoted.Keys.OrderBy(x => x))              // sorted — the deterministic append order
        {
            var e = s.Demoted[h];
            var pat = new Symbol[e.Length];
            for (int j = 0; j < e.Length; j++) pat[j] = new Symbol(e[j]);
            rules[k2++] = new GrammarRule(GrammarRule.ComputeId(pat), pat, new Mbits(RefBits * 1000));
        }
        var comp = new Symbol[s.Tape.Count];
        for (int i = 0; i < comp.Length; i++) comp[i] = new Symbol((uint)s.Tape[i]);
        return new RePairResult(rules, comp, savings, 256);
    }

    private const long RefBits = 64;   // pointer rent per demoted expansion — Memory.cs's RefBitsPerSeg (one oracle span ref)

    /// Symbolic two-part bits: every RESIDENT symbol (tape + patterns) at log2|V| + a per-rule header, plus
    /// pointer rent per demoted expansion (the accounting: the surface returned to the oracle, a ref
    /// retained). The engine's Mbits Cost is a serialization-size tally; this is the information accounting.
    private static double Bits(Shape s, double headerBits)
    {
        double log2V = Math.Log2(256 + Math.Max(1, s.Pats.Count));
        long syms = s.Tape.Count;
        foreach (var p in s.Pats) syms += p.Length;
        return syms * log2V + s.Pats.Count * headerBits + s.Demoted.Count * RefBits;
    }

    private static Row Read(string arm, string phase, int cycle, Shape s, byte[] train, byte[] heldR, byte[] heldF,
                            int[] probeHashes, long mints, long touches, double headerBits, int compacted, int evicted, int revisits)
    {
        var r = ToResult(s, Mbits.Zero);
        var cover = new Engine.GrammarCover(r.Rules);
        var (scales, meanZ, cvZ, maxSpan, _) = Engine.RenormStats(r);
        long surface = 0; foreach (var e in s.Exps) surface += e.Length;
        // caps = how many held-REPEAT lines exist as a whole-line expansion in the BASIS (live or demoted) —
        // the direct mechanism check (breach forms the count-2 line caps; LOWER must not lose them).
        var live = new HashSet<int>(); foreach (var e in s.Exps) live.Add(Fnv(e));
        foreach (var h2 in s.Demoted.Keys) live.Add(h2);
        int caps = 0; foreach (var h in probeHashes) if (live.Contains(h)) caps++;
        return new Row(arm, phase, cycle, mints, touches, s.Pats.Count, s.Demoted.Count, Bits(s, headerBits), surface,
            cover.ParsedSize(train), cover.ParsedSize(heldR), heldF.Length > 0 ? cover.ParsedSize(heldF) : 0,
            caps, scales, meanZ, cvZ, maxSpan, compacted, evicted, revisits);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE AESTIVATION VERB — the anneal mounted on the LIVE grammar (Cortex.Consolidate calls this when the
    //  homeostat's Stalled condition granted a BreachQuota)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// One aestivation's inhale/exhale over the live grammar. `Fired` = the grammar changed (the C2 cvz mask must
    /// arm iff true); `Demoted` is the aestivation's demote-retired basis (rides the returned grammar as appended
    /// terminal-pattern rules — Memory.Gc can further demote them to TapeRefs under budget, the composed path).
    public readonly record struct BreachConsolidationPhase(RePairResult Grammar, int Mints, int Compacted, int Evicted, int Demoted, int Refolded)
    {
        public bool Fired => Mints + Compacted + Evicted + Refolded > 0;
        public string Line => $"mint {Mints} compact {Compacted} evict {Evicted} demote {Demoted} refold {Refolded}";
    }

    /// BREACH speculatively mints count-2 candidates past the greedy pay-floor (quota-bounded), LOWER distills
    /// under the strict budget (compact u≤1 → rent-evict → demote-don't-delete → strict refold). Lossless by
    /// construction — `tape` is the oracle (the Tape's Concat) and reconstruction is asserted against it
    /// around the anneal. Deterministic square wave, no RNG (`seed` is the NOISING seam — stochastic breach
    /// depth, the NCA-entropy variant — reserved, unread today so the Vow holds bitwise). Unlike the standalone
    /// kill-line's Shape (which persists across cycles), the drive's ratchet rides the TAPE: the deepened grammar
    /// generates deeper dream spans until the next InduceOutcomeCredited internalizes what recurs — the demoted basis and
    /// the count-2 templates live in the returned grammar only until that re-induce (the C2 mask's exact horizon).
    public static BreachConsolidationPhase Breach(in RePairResult g, byte[] tape, int quota, ulong seed)
    {
        _ = seed;                                                     // the noising seam — reserved
        var s = Shape.FromResult(g);
        Guard(s, tape, "open");                                       // the input grammar must BE this tape's grammar
        long touches = 0; int revisits = 0;
        var register = new HashSet<int>();                            // tabu stays OFF (the measured anti-ratchet); the register is Lower's eviction record
        int mints = Mint(s, cmin: 2, quota: quota, tabu: null, register: null, barrier: true, ref touches, ref revisits, deep: true);
        var (compacted, evicted, refolded) = Lower(s, HeaderBitsDefault, register, barrier: true, demote: true, ref touches, ref revisits);
        Guard(s, tape, "lower");
        return new BreachConsolidationPhase(ToResult(s, g.TotalSavings), mints, compacted, evicted, s.Demoted.Count, refolded);
    }

    /// The drive's arm of the aestivation verb: the lossless oracle walks THE VIEW in place — residents
    /// straight from tape RAM, the shed tail read from the event byte log ONCE per breach — instead of
    /// materializing the whole view via Concat() per aestivation. Anneal-identical to the byte[] arm.
    public static BreachConsolidationPhase Breach(in RePairResult g, Tape tape, int quota, ulong seed)
    {
        _ = seed;                                                     // the noising seam — reserved
        var s = Shape.FromResult(g);
        byte[] shedTail = MaterializeShedTail(tape);
        Guard(s, tape, shedTail, "open");                             // the input grammar must BE this tape's grammar
        long touches = 0; int revisits = 0;
        var register = new HashSet<int>();                            // tabu stays OFF (the measured anti-ratchet); the register is Lower's eviction record
        int mints = Mint(s, cmin: 2, quota: quota, tabu: null, register: null, barrier: true, ref touches, ref revisits, deep: true);
        var (compacted, evicted, refolded) = Lower(s, HeaderBitsDefault, register, barrier: true, demote: true, ref touches, ref revisits);
        Guard(s, tape, shedTail, "lower");
        return new BreachConsolidationPhase(ToResult(s, g.TotalSavings), mints, compacted, evicted, s.Demoted.Count, refolded);
    }

    // the view's shed half (id-ascending, '\n'-separated, Concat's exact tail) — read from the log once
    // per breach so both guards compare against RAM
    private static byte[] MaterializeShedTail(Tape tape)
    {
        if (tape.ShedEventIDs.Count == 0) return [];
        long tailLength = tape.ByteLength - tape.ResidentBytes;
        if (tailLength > int.MaxValue) throw new InvalidOperationException($"breach: shed tail is {tailLength}B — past the int-indexed ceiling");
        var tail = new byte[tailLength];
        int at = 0;
        foreach (long v in tape.ShedEventIDs)
        {
            if (!tape.Resolve(new TapeEventID(v), out byte[] bytes))
                throw new InvalidOperationException($"breach: shed event {v} did not resolve from the event byte log");
            bytes.CopyTo(tail, at); at += bytes.Length; tail[at++] = (byte)'\n';
        }
        if (at != tail.Length) throw new InvalidOperationException($"breach: shed tail materialized {at}B of {tail.Length}B");
        return tail;
    }

    private static void Guard(Shape s, Tape tape, byte[] shedTail, string phase)
    {
        byte[] expanded = s.ExpandAll();
        bool intact = expanded.LongLength == tape.ByteLength;
        int at = 0;
        if (intact)
            foreach (byte[] resident in tape.ResidentEventBytes)
            {
                if (at + resident.Length + 1 > expanded.Length
                    || !expanded.AsSpan(at, resident.Length).SequenceEqual(resident)
                    || expanded[at + resident.Length] != (byte)'\n') { intact = false; break; }
                at += resident.Length + 1;
            }
        if (intact) intact = expanded.AsSpan(at).SequenceEqual(shedTail);
        if (!intact)
            throw new InvalidOperationException($"breach/{phase}: reconstruction broke — the anneal must be lossless");
    }

    private static void Guard(Shape s, byte[] reference, string phase)
    {
        if (!s.ExpandAll().AsSpan().SequenceEqual(reference))
            throw new InvalidOperationException($"breach/{phase}: reconstruction broke — the anneal must be lossless");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE ARMS
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ArmResult(List<Row> Rows, Shape Final);

    /// fixed: strict fixpoint — the engine's greedy, which then SATURATES (cannot convert more compute).
    /// oneshot: strict → breach-to-fixpoint → one LOWER (a single giant inhale/exhale).
    /// oscillate: strict → cycles of (breach quota Qk, LOWER), Qk doubling — the anneal with the ratchet.
    private static ArmResult RunArm(string arm, byte[] train, byte[] heldR, byte[] heldF, int[] probeHashes,
                                    int cycles, int quota0, bool tabuOn, bool barrier, bool demote, double headerBits)
    {
        var s = new Shape(train);
        var register = new HashSet<int>();
        long touches = 0, mints = 0; int revisits = 0;
        var rows = new List<Row>();

        void Guard(string phase)
        {
            if (!s.ExpandAll().AsSpan().SequenceEqual(train))
                throw new InvalidOperationException($"{arm}/{phase}: reconstruction broke — the anneal must be lossless");
        }
        Row Land(string phase, int cycle, int comp, int evic)
            => Read(arm, phase, cycle, s, train, heldR, heldF, probeHashes, mints, touches, headerBits, comp, evic, revisits);

        mints += Mint(s, cmin: 3, quota: -1, tabu: null, register: null, barrier, ref touches, ref revisits);
        Guard("strict"); rows.Add(Land("strict", 0, 0, 0));
        if (arm == "fixed") return new ArmResult(rows, s);

        if (arm == "oneshot")
        {
            mints += Mint(s, cmin: 2, quota: -1, tabu: null, register: null, barrier, ref touches, ref revisits);
            Guard("breach"); rows.Add(Land("breach", 1, 0, 0));
            var (c, e, rf) = Lower(s, headerBits, register, barrier, demote, ref touches, ref revisits);
            mints += rf;
            Guard("lower"); rows.Add(Land("lower", 1, c, e));
            return new ArmResult(rows, s);
        }

        int quota = quota0;
        for (int cyc = 1; cyc <= cycles; cyc++)
        {
            int minted = Mint(s, cmin: 2, quota: quota, tabu: tabuOn ? register : null, register: tabuOn ? null : register, barrier, ref touches, ref revisits);
            mints += minted;
            Guard($"breach{cyc}"); rows.Add(Land("breach", cyc, 0, 0));
            var (c, e, rf) = Lower(s, headerBits, register, barrier, demote, ref touches, ref revisits);
            mints += rf;
            Guard($"lower{cyc}"); rows.Add(Land("lower", cyc, c, e));
            if (minted == 0) break;                                     // frontier exhausted — the momentum stop
            quota *= 2;                                                 // the amplitude schedule (deeper inhale each cycle)
        }
        return new ArmResult(rows, s);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE LADDER CORPUS — known 3-level structure with the DEEPEST level pinned at count 2
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    // L1 phrases recur widely (strict-reachable) · L2 sentences recur ≥4× (strict-reachable) · L3 paragraphs
    // occur EXACTLY 2× in train (strict-UNREACHABLE by the pay-floor, by construction) + 1× in held-REPEAT.
    // held-FRESH is novel recombinations of the SAME L2s — the control: no arm should out-parse another there,
    // so junk depth-inflation from breach would surface as a control divergence. Noise lines feed the breach
    // junk it must learn to evict. Deterministic LCG (the Vow — ChainCorpus's generator shape).
    private static (byte[] Train, byte[] HeldRepeat, byte[] HeldFresh) LadderCorpus(ulong seed)
    {
        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
        int Rand(int lo, int hi) => lo + (int)(U() * (hi - lo + 1));

        var words = new string[60];
        for (int i = 0; i < words.Length; i++)
        {
            int len = Rand(4, 7);
            var sb = new StringBuilder(len);
            for (int j = 0; j < len; j++) sb.Append((char)('a' + Rand(0, 25)));
            words[i] = sb.ToString();
        }
        string[] glue = { "the", "of", "and", "in", "to" };

        var l1 = new string[24];
        for (int i = 0; i < l1.Length; i++)
        {
            int k = Rand(3, 4);
            var parts = new string[k];
            for (int j = 0; j < k; j++) parts[j] = words[Rand(0, words.Length - 1)];
            l1[i] = string.Join(' ', parts);
        }
        var l2 = new string[36];
        for (int i = 0; i < l2.Length; i++)
        {
            int k = Rand(2, 3);
            var sb = new StringBuilder();
            for (int j = 0; j < k; j++)
            {
                if (j > 0) sb.Append(' ').Append(glue[Rand(0, glue.Length - 1)]).Append(' ');
                sb.Append(l1[Rand(0, l1.Length - 1)]);
            }
            l2[i] = sb.ToString();
        }
        var l3 = new string[30];
        var l3Set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < l3.Length; i++)
        {
            int a = Rand(0, l2.Length - 1), b = Rand(0, l2.Length - 1), c = Rand(0, l2.Length - 1);
            l3[i] = $"{l2[a]} ; {l2[b]} ; {l2[c]}";
            l3Set.Add($"{a},{b},{c}");
        }

        var train = new List<string>();
        foreach (var p in l3) { train.Add(p); train.Add(p); }             // the count-2 stratum — the breach target
        foreach (var sent in l2) { train.Add(sent); train.Add(sent); }    // keeps L2 comfortably ≥3 with the L3 embeddings
        for (int i = 0; i < 30; i++)                                      // unique noise — the junk breach must evict
        {
            int k = Rand(5, 9);
            var parts = new string[k];
            for (int j = 0; j < k; j++) parts[j] = words[Rand(0, words.Length - 1)];
            train.Add(string.Join(' ', parts));
        }
        for (int i = train.Count - 1; i > 0; i--)                         // Fisher–Yates, LCG (deterministic shuffle)
        {
            int j = Rand(0, i);
            (train[i], train[j]) = (train[j], train[i]);
        }

        var fresh = new List<string>();
        while (fresh.Count < 15)
        {
            int a = Rand(0, l2.Length - 1), b = Rand(0, l2.Length - 1), c = Rand(0, l2.Length - 1);
            if (l3Set.Add($"{a},{b},{c}")) fresh.Add($"{l2[a]} ; {l2[b]} ; {l2[c]}");
        }

        static byte[] Join(IEnumerable<string> lines) => Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        return (Join(train), Join(l3), Join(fresh));
    }

    // real corpus: a file, or a directory's *.cs (sorted) — capped; every 8th line held out (FileCorpus's holdEvery).
    private static (byte[] Train, byte[] Held) RealCorpus(string path, int cap)
    {
        var raw = new List<byte>(cap);
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.cs");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                var bytes = File.ReadAllBytes(f);
                raw.AddRange(bytes);
                if (raw.Count >= cap) break;
            }
        }
        else raw.AddRange(File.ReadAllBytes(path));
        if (raw.Count > cap) raw.RemoveRange(cap, raw.Count - cap);

        var train = new List<byte>(raw.Count); var held = new List<byte>();
        int line = 0;
        foreach (var mem in Engine.SplitLines(raw.ToArray()))
        {
            var dst = line++ % 8 == 7 ? held : train;
            foreach (var b in mem.Span) dst.Add(b);
            dst.Add((byte)'\n');
        }
        return (train.ToArray(), held.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE VERB
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// usage: breachlower [--corpus PATH] [--cap BYTES] [--cycles N] [--quota N] [--tabu] [--no-barrier]
    ///                    [--header BITS] [--seed HEX] [--out DIR] [--emit-corpus PATH]
    /// Default corpus = the synthetic LADDER (known count-2 deep structure + a fresh-recombination control);
    /// --corpus runs a real file/directory (*.cs) with an every-8th-line held-out instead. --emit-corpus writes
    /// the ladder TRAIN to PATH and exits — the stall-prone drive world (greedy saturates on it by construction,
    /// so the homeostat's Stalled condition fires and the mounted breach gets a live target).
    public static int Run(string[] args)
    {
        string emit = Args.Str(args, "--emit-corpus", "");
        if (emit.Length > 0)
        {
            var (tr, _, _) = LadderCorpus(Args.Seed(args, "--seed", 0xC0117011UL));
            var parent = Path.GetDirectoryName(Path.GetFullPath(emit));
            if (parent is { Length: > 0 }) Directory.CreateDirectory(parent);
            File.WriteAllBytes(emit, tr);
            Trace.Note($"ladder train ({tr.Length}B) → {emit}");
            return 0;
        }
        string corpusPath = Args.Str(args, "--corpus", "");
        int cap = Args.Int(args, "--cap", 49152);
        int cycles = Args.Int(args, "--cycles", 5);
        int quota0 = Args.Int(args, "--quota", 128);
        // tabu default OFF — the ladder ablation measured the evicted-content register as ANTI-RATCHET: a cap
        // evicted in unpayable binary form must be allowed to RE-MINT over the re-centered tape, where it
        // compacts into a paying n-ary rule (tabu-on 27/30 caps · heldR 64 vs tabu-off 30/30 · 60).
        bool tabu = Args.Has(args, "--tabu");
        bool barrier = !Args.Has(args, "--no-barrier");
        bool demote = Args.Str(args, "--lower", "demote") != "delete";   // demote = the drive-faithful LOWER (Memory.Gc); delete = the harsher ablation
        double header = Args.Double(args, "--header", HeaderBitsDefault);
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);
        string outDir = Args.Str(args, "--out", "scratchpad/breach_lower");

        byte[] train, heldR, heldF; string corpusName;
        if (corpusPath.Length > 0)
        {
            (train, heldR) = RealCorpus(corpusPath, cap);
            heldF = Array.Empty<byte>();
            corpusName = Path.GetFileName(corpusPath.TrimEnd('/'));
        }
        else
        {
            (train, heldR, heldF) = LadderCorpus(seed);
            corpusName = "ladder";
        }

        // held-REPEAT whole-line hashes — the `caps` mechanism check (did the count-2 line caps form + survive?)
        var probeHashes = new List<int>();
        foreach (var mem in Engine.SplitLines(heldR)) probeHashes.Add(Fnv(mem.ToArray()));
        var probes = probeHashes.ToArray();

        Trace.Note($"breachlower ·  anneal kill-line · corpus {corpusName} · train {train.Length}B · held repeat {heldR.Length}B / fresh {heldF.Length}B · quota {quota0}×2ᶜ · {cycles} cycles · tabu {(tabu ? "on" : "off")} · barrier {(barrier ? "on" : "off")} · lower {(demote ? "demote" : "delete")} · header {header:F3}b · seed {seed:X}");
        Trace.Note("  BREACH admit the count-2 stratum (cmin 3→2, quota) · LOWER compact u≤1 → rent-evict ((u−1)p−u)·log2V ≤ H → register → strict refold");
        Trace.Note("");

        var arms = new List<ArmResult>
        {
            RunArm("fixed",     train, heldR, heldF, probes, cycles, quota0, tabu, barrier, demote, header),
            RunArm("oneshot",   train, heldR, heldF, probes, cycles, quota0, tabu, barrier, demote, header),
            RunArm("oscillate", train, heldR, heldF, probes, cycles, quota0, tabu, barrier, demote, header),
        };

        // ── the Vow — re-run the anneal arm, fingerprint both finals ──
        var again = RunArm("oscillate", train, heldR, heldF, probes, cycles, quota0, tabu, barrier, demote, header);
        static int Fingerprint(Shape s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var sym in s.Tape) { h ^= (uint)sym; h *= 16777619; }
                foreach (var e in s.Exps) h = (uint)(h * 31 + (uint)Fnv(e));
                foreach (var d in s.Demoted.Keys.OrderBy(x => x)) h = (uint)(h * 31 + (uint)d);
                return (int)h;
            }
        }
        bool vow = Fingerprint(arms[2].Final) == Fingerprint(again.Final);

        // ── strict parity — the ENGINE now arms the '\n' barrier (Engine.Induce → RePair barrier:'\n'), so the
        // barrier-ON fixed arm must reproduce it exactly (same greedy, same total order, same barrier predicate);
        // with `--no-barrier` this arm deliberately diverges (the straddle-trap ablation the engine fix killed).
        var (_, _, engine) = Engine.Induce(train);
        bool parity = barrier ? engine.Rules.Length == arms[0].Final.Pats.Count : true;
        string parityNote = !barrier
            ? $"n/a (--no-barrier ablation — engine {engine.Rules.Length} rules within-line, arm {arms[0].Final.Pats.Count} incl. cross-line)"
            : parity ? $"OK ({engine.Rules.Length} rules)" : $"DIVERGES ({engine.Rules.Length} vs {arms[0].Final.Pats.Count})";

        // ── the table ──
        Trace.Note($"  {"arm",-9} {"phase",-7} {"cyc",3} {"mints∑",8} {"touch∑",10} {"rules",6} {"dem",5} {"bits",10} {"ps_train",8} {"ps_heldR",8} {"ps_heldF",8} {"caps",4} {"scal",4} {"cvZ",6} {"maxSpan",8} {"cmp",5} {"evc",5} {"rvst",5}");
        Trace.Note("  " + new string('─', 130));
        var tsv = new StringBuilder("arm\tphase\tcycle\tmints_cum\ttouches_cum\trules\tdemoted\tbits\tsurface_bytes\tps_train\tps_heldR\tps_heldF\tcaps\tscales\tmeanZ\tcvZ\tmaxSpan\tcompacted\tevicted\trevisits\n");
        foreach (var arm in arms)
            foreach (var r in arm.Rows)
            {
                Trace.Note($"  {r.Arm,-9} {r.Phase,-7} {r.Cycle,3} {r.MintsCum,8} {r.TouchesCum,10} {r.Rules,6} {r.DemotedN,5} {r.Bits,10:F0} {r.PsTrain,8} {r.PsHeldR,8} {r.PsHeldF,8} {r.Caps,4} {r.Scales,4} {r.CvZ,6:F2} {r.MaxSpan,8:F0} {r.Compacted,5} {r.Evicted,5} {r.Revisits,5}");
                tsv.AppendLine($"{r.Arm}\t{r.Phase}\t{r.Cycle}\t{r.MintsCum}\t{r.TouchesCum}\t{r.Rules}\t{r.DemotedN}\t{r.Bits:F1}\t{r.SurfaceBytes}\t{r.PsTrain}\t{r.PsHeldR}\t{r.PsHeldF}\t{r.Caps}\t{r.Scales}\t{r.MeanZ:F3}\t{r.CvZ:F3}\t{r.MaxSpan:F0}\t{r.Compacted}\t{r.Evicted}\t{r.Revisits}");
            }
        Trace.Note("");

        // ── the verdict ──
        Row fin(int i) => arms[i].Rows[^1];
        var f = fin(0); var o = fin(1); var c = fin(2);
        double DepthWin(Row x) => f.PsHeldR == 0 ? 0 : 100.0 * (f.PsHeldR - x.PsHeldR) / f.PsHeldR;
        // grok-point: first row per arm with CvZ < 0.20 at ≥3 scales (GrokCv's pre-registered threshold)
        string Grok(ArmResult a)
        {
            foreach (var r in a.Rows) if (!double.IsNaN(r.CvZ) && r.CvZ < 0.20 && r.Scales >= 3) return $"{r.Phase}#{r.Cycle} (mints {r.MintsCum})";
            return "—";
        }
        Trace.Note("  ── VERDICT ──");
        Trace.Note($"    held-REPEAT depth (ParsedSize, lower=deeper): fixed {f.PsHeldR} → oneshot {o.PsHeldR} ({DepthWin(o):+0.0;-0.0}%) → oscillate {c.PsHeldR} ({DepthWin(c):+0.0;-0.0}%)");
        Trace.Note($"    count-2 line caps live (of {probes.Length} held lines): fixed {f.Caps} · oneshot {o.Caps} · oscillate {c.Caps}");
        if (heldF.Length > 0)
            Trace.Note($"    held-FRESH control (must ≈ tie):              fixed {f.PsHeldF} · oneshot {o.PsHeldF} · oscillate {c.PsHeldF}");
        Trace.Note($"    bits: fixed {f.Bits:F0} · oneshot {o.Bits:F0} · oscillate {c.Bits:F0} · rules {f.Rules}/{o.Rules}/{c.Rules}");
        Trace.Note($"    compute (symbol touches): fixed {f.TouchesCum} (SATURATED — greedy cannot spend more) · oneshot {o.TouchesCum} · oscillate {c.TouchesCum}");
        Trace.Note($"    grok-point (CvZ<0.20, ≥3 scales): fixed {Grok(arms[0])} · oneshot {Grok(arms[1])} · oscillate {Grok(arms[2])}");
        Trace.Note($"    VOW same-seed byte-identical re-run: {(vow ? "OK" : "BROKEN")} · strict parity with engine RePair: {parityNote}");

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, $"killline_{corpusName}.tsv"), tsv.ToString());
        File.WriteAllText(Path.Combine(outDir, $"verdict_{corpusName}.txt"),
            $"corpus={corpusName} train={train.Length}B heldR={heldR.Length}B heldF={heldF.Length}B quota={quota0} cycles={cycles} tabu={tabu} header={header} seed={seed:X}\n" +
            $"ps_heldR fixed={f.PsHeldR} oneshot={o.PsHeldR} oscillate={c.PsHeldR}\n" +
            $"ps_heldF fixed={f.PsHeldF} oneshot={o.PsHeldF} oscillate={c.PsHeldF}\n" +
            $"bits fixed={f.Bits:F0} oneshot={o.Bits:F0} oscillate={c.Bits:F0}\n" +
            $"rules fixed={f.Rules} oneshot={o.Rules} oscillate={c.Rules}\n" +
            $"vow={(vow ? "ok" : "BROKEN")} parity={(parity ? "ok" : "DIVERGES")}\n");
        Trace.Note($"    → {outDir}/killline_{corpusName}.tsv landed");
        return vow && parity ? 0 : 1;
    }
}
