namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;


// ── THE SLEEP PASS · COUPLINGS-GUIDED TAPE DEFRAG ──  the self-repairing-curriculum test.
//
// The intake proof (Radula.cs) showed a RANDOM-interleaved history caps the correlation length: the same
// lines, fed in a globally-mixed order, never form the cross-line TEMPLATE-pair digrams that a concentrated
// order does (maxSpan 133B vs the frontier's 228B). The proof called that cap "permanent" — but permanence is
// a property of an APPEND-ONLY tape, and cogito OWNS its tape. So it can DEFRAG: re-order the history so that
// structurally-related lines sit ADJACENT, then re-induce, recovering the relational scale a bad intake lost.
// This is memory-consolidation-during-sleep, made mechanical.
//
// THE OPEN QUESTION (precise): the intake proof's BLOCKED control proved that family-ADJACENCY (reference
// ordering) recovers the grok. The question SLEEP answers is whether LEARNED couplings — with NO family labels —
// can PRODUCE that adjacency. The lever is a label-free affinity over the grammar's OWN induced idioms:
//
//   • Each line is covered by the current grammar → a set of induced UNITS (Re-Pair rules = the idioms it holds).
//   • Two lines are AFFINE when they share DISCRIMINATIVE units (IDF-weighted: a morpheme shared by every family
//     carries no signal; a template shared only within one family carries all of it) — the couplings' MARGINAL
//     structure — plus, optionally, when their units are PPMI-COUPLED (Couplings.cs BuildScorer φ) — the couplings'
//     CO-COUNT structure. Both come from the ONE learned Couplings object; neither reads a family label.
//   • SERIATE: a greedy nearest-affinity chain re-orders the whole tape so affine lines abut — recovering both the
//     cross-family BLOCKING and the within-family TEMPLATE order (the specific adjacency maxSpan needs).
//
// THE BOOTSTRAP: defrag → re-induce → re-couple (a deeper grammar yields sharper couplings) → defrag → … If the
// sleep-CYCLE climbs 133 → toward 228, the machine RETROACTIVELY REPAIRS ITS OWN BROKEN CURRICULUM — it does not
// need a perfect intake, it consolidates its way to the relational scale.
//
// CONTROLS (honest either way): a RANDOM re-serialization (same # of moves, no coupling guidance) must NOT recover
// (the null — proves it is the couplings, not mere reshuffling); an ORACLE re-order (true families, then within-
// family affinity) is the reordering CEILING a perfect label-free clustering could reach; clustering PURITY/ARI vs
// the reference families reports whether the learned couplings actually recovered the blocking. If the clustering
// is too noisy without labels, maxSpan stays flat and purity stays low — the honest negative.

public static class Seriate
{
    /// Standing state for the incremental sleep weave.  Grammar rules are append-only on the loom between
    /// rebases, so the expensive expansion buckets and PPMI view do not belong to one call of WeaveNew.  A
    /// model survives aestivations, appends only a new rule basis when the rule stream extends, and resets when
    /// a caller presents a non-lineage grammar.  Line vectors remain per-call because IDF is defined over the
    /// touched candidate population; this is the deliberate boundary between standing model state and Δ scoring.
    internal sealed class WeaveModel
    {
        private GrammarRule[]? _rules;
        private Dictionary<int, List<int>>? _prefixSlots;
        private readonly Dictionary<int, List<(int Idx, byte[] E)>> _expandedPrefixes = new();
        private List<RulePrefix>? _rulePrefixes;
        private Couplings? _couplings;
        private Scorer? _rich;
        private Symbol[]? _compressed;
        private readonly Dictionary<int, CoverCacheEntry> _coverCache = new();
        private readonly HashSet<int> _changedPrefixes = new();

        private sealed class CoverCacheEntry(Dictionary<int, int> units, HashSet<int> prefixes)
        {
            internal Dictionary<int, int> Units { get; } = units;
            internal HashSet<int> Prefixes { get; } = prefixes;
        }

        private readonly record struct RulePrefix(byte First, byte Second, byte Count);

        internal int Resets { get; private set; }
        internal int Appends { get; private set; }
        internal int Splices { get; private set; }
        internal bool IsReady => _rules is not null;

        internal void Reset(RePairResult grammar, string reason = "explicit")
        {
            long t = Trace.NowTicks;
            _rules = grammar.Rules;
            (_prefixSlots, _rulePrefixes) = BuildPrefixSlots(grammar.Rules);
            _expandedPrefixes.Clear();
            long msPrefix = Trace.ElapsedMs(t);
            t = Trace.NowTicks;
            _couplings = Couplings.Learn(grammar);
            long msCouplings = Trace.ElapsedMs(t);
            t = Trace.NowTicks;
            _rich = _couplings.BuildScorer(minCocount: 1);
            long msScorer = Trace.ElapsedMs(t);
            _couplings.ClearDirty();
            _compressed = grammar.Compressed;
            _coverCache.Clear();
            _changedPrefixes.Clear();
            Resets++;
            Trace.Seriate.Boundary("weave.model", $"reset={reason} ms: prefix={msPrefix} couplings={msCouplings} scorer={msScorer} · rules={grammar.Rules.Length} symbols={grammar.Compressed.Length}");
        }

        internal void Ensure(RePairResult grammar)
        {
            if (_rules is null) { Reset(grammar, "initial"); return; }

            int shared = Math.Min(_rules.Length, grammar.Rules.Length);
            for (int i = 0; i < shared; i++)
                if (!_rules[i].Id.Equals(grammar.Rules[i].Id)) { Reset(grammar, $"rule-rebase@{i}"); return; }
            if (grammar.Rules.Length < _rules.Length) { Reset(grammar, "rule-shrink"); return; }
            if (grammar.Rules.Length > _rules.Length)
            {
                // The loom's rule stream is monotone. Append both the expansion basis and the raw coupling
                // evidence; PPMI is then refreshed only for units touched by this splice.
                int firstAdded = _rules.Length;
                AppendRules(grammar.Rules);
                _couplings?.AppendRules(grammar.Rules.AsSpan(firstAdded).ToArray());
                ApplySequenceDelta(grammar);
            }
            else ApplySequenceDelta(grammar);
        }

        internal void AppendRules(GrammarRule[] complete)
        {
            if (_rules is null || complete.Length <= _rules.Length) return;
            if (_prefixSlots is null || _rules is null || _rulePrefixes is null) throw new InvalidOperationException("weave model is not initialized");
            int first = _rules.Length;
            int addedCount = complete.Length - first;
            for (int i = first; i < complete.Length; i++)
            {
                RulePrefix prefix = ReadPrefix(complete[i], _rulePrefixes);
                _rulePrefixes.Add(prefix);
                AddPrefixSlot(_prefixSlots, i, prefix, _changedPrefixes);
            }
            _rules = complete;
            foreach (int key in _changedPrefixes) _expandedPrefixes.Remove(key);
            Appends += addedCount;
        }

        internal Dictionary<int, int> CoverUnits(int slot, byte[] line)
        {
            if (_coverCache.TryGetValue(slot, out var cached) && !cached.Prefixes.Overlaps(_changedPrefixes))
                return cached.Units;

            var units = Seriate.CoverUnits(this, line);
            var prefixes = new HashSet<int>();
            for (int i = 0; i + 1 < line.Length; i++) prefixes.Add((line[i] << 8) | line[i + 1]);
            _coverCache[slot] = new CoverCacheEntry(units, prefixes);
            return units;
        }

        internal void ClearChangedPrefixes() => _changedPrefixes.Clear();

        private void ApplySequenceDelta(RePairResult grammar)
        {
            if (_couplings is null || _compressed is null) return;
            if (_compressed.AsSpan().SequenceEqual(grammar.Compressed)) return;
            int prefix = 0;
            while (prefix < _compressed.Length && prefix < grammar.Compressed.Length && _compressed[prefix].Equals(grammar.Compressed[prefix])) prefix++;
            int suffix = 0;
            while (suffix < _compressed.Length - prefix && suffix < grammar.Compressed.Length - prefix
                && _compressed[_compressed.Length - 1 - suffix].Equals(grammar.Compressed[grammar.Compressed.Length - 1 - suffix])) suffix++;
            int removed = _compressed.Length - prefix - suffix;
            int insertedLength = grammar.Compressed.Length - prefix - suffix;
            var inserted = new Symbol[insertedLength];
            Array.Copy(grammar.Compressed, prefix, inserted, 0, insertedLength);
            _couplings.ApplySequenceSplice(prefix, removed, inserted);
            _couplings.RefreshScorer(_rich!);
            _compressed = grammar.Compressed;
            Splices++;
        }

        /// A splice is the explicit invalidation seam for callers that can prove a sequence edit.  The weave
        /// keeps the prior coupling view stable when only line candidates changed; consumers with a full grammar
        /// splice may call Reset on the next Ensure rather than silently claiming the old PPMI view is current.
        internal void RecordSplice() => Splices++;

        internal Scorer Rich => _rich ?? throw new InvalidOperationException("weave model is not initialized");

        internal List<(int Idx, byte[] E)> Candidates(int key)
        {
            if (_expandedPrefixes.TryGetValue(key, out var expanded)) return expanded;
            if (_prefixSlots is null || _rules is null || !_prefixSlots.TryGetValue(key, out var slots))
                return [];
            expanded = new List<(int Idx, byte[] E)>(slots.Count);
            foreach (int index in slots)
            {
                var e = Reconstruct.Expand(_rules, [new Symbol(Symbol.FirstNonterminal + (uint)index)]);
                if (e.Length >= 2) expanded.Add((index, e));
            }
            expanded.Sort((a, b) => b.E.Length - a.E.Length);
            _expandedPrefixes[key] = expanded;
            return expanded;
        }

        internal static Dictionary<int, List<(int Idx, byte[] E)>> BuildPrefixIndex(GrammarRule[] rules)
        {
            var byPrefix = new Dictionary<int, List<(int Idx, byte[] E)>>();
            for (int i = 0; i < rules.Length; i++) AddPrefix(byPrefix, i, rules);
            foreach (var list in byPrefix.Values) list.Sort((a, b) => b.E.Length - a.E.Length);
            return byPrefix;
        }

        private static (Dictionary<int, List<int>>, List<RulePrefix>) BuildPrefixSlots(GrammarRule[] rules)
        {
            var slots = new Dictionary<int, List<int>>();
            var prefixes = new List<RulePrefix>(rules.Length);
            for (int i = 0; i < rules.Length; i++)
            {
                RulePrefix prefix = ReadPrefix(rules[i], prefixes);
                prefixes.Add(prefix);
                AddPrefixSlot(slots, i, prefix);
            }
            return (slots, prefixes);
        }

        private static void AddPrefixSlot(Dictionary<int, List<int>> byPrefix, int index, RulePrefix prefix, HashSet<int>? changed = null)
        {
            if (prefix.Count < 2) return;
            int key = (prefix.First << 8) | prefix.Second;
            (byPrefix.TryGetValue(key, out var l) ? l : byPrefix[key] = new()).Add(index);
            changed?.Add(key);
        }

        private static RulePrefix ReadPrefix(GrammarRule rule, List<RulePrefix> prefixes)
        {
            byte first = 0, second = 0;
            int count = 0;
            var pattern = rule.IsSlot
                ? (rule.Pattern.Length == 0 ? ReadOnlySpan<Symbol>.Empty : rule.Pattern.AsSpan(0, 1))
                : rule.Pattern.AsSpan();
            foreach (var symbol in pattern)
            {
                if (count == 2) break;
                if (symbol.IsTerminal)
                {
                    if (count++ == 0) first = (byte)symbol.Value;
                    else second = (byte)symbol.Value;
                }
                else
                {
                    RulePrefix child = prefixes[(int)symbol.Value - (int)Symbol.FirstNonterminal];
                    if (child.Count > 0)
                    {
                        if (count++ == 0) first = child.First;
                        else second = child.First;
                    }
                    if (count < 2 && child.Count > 1)
                    {
                        if (count++ == 0) first = child.Second;
                        else second = child.Second;
                    }
                }
            }
            return new RulePrefix(first, second, (byte)count);
        }

        private static void AddPrefix(Dictionary<int, List<(int Idx, byte[] E)>> byPrefix, int index, GrammarRule[] rules, HashSet<int>? changed = null)
        {
            var e = Reconstruct.Expand(rules, [new Symbol(Symbol.FirstNonterminal + (uint)index)]);
            if (e.Length < 2) return;
            int key = (e[0] << 8) | e[1];
            (byPrefix.TryGetValue(key, out var l) ? l : byPrefix[key] = new()).Add((index, e));
            changed?.Add(key);
        }
    }

    /// A held-out line is DEEP-grokked when it compresses below this many symbols/byte (same gate as Radula).
    private const double DeepThresholdSymPerByte = 0.07;

    /// One measured tape state — an arm at a sleep-cycle. maxSpan is the headline (the relational scale random
    /// history caps at 133 and the defrag must lift toward 228); heldSym/deepFams are the held-out depth read;
    /// purity/ari say whether the label-free clustering recovered the reference families that produced the order.
    private readonly record struct Cycle(
        string Arm, int T, int MaxSpan, int Rules, int Scales, double HeldSym, int DeepFams, double Contig, double Purity, double Ari)
    {
        public const string Header = "arm\tcycle\tmaxspan\trules\tscales\theldsym\tdeepfams\tcontig\tpurity\tari";
        public string Row() => $"{Arm}\t{T}\t{MaxSpan}\t{Rules}\t{Scales}\t{HeldSym:F4}\t{DeepFams}\t{Contig:F3}\t{Purity:F3}\t{Ari:F3}";
    }

    /// usage: sleep [--fam K] [--morph N] [--win W] [--overlap O] [--words N] [--phrases N] [--templates N]
    ///              [--lines N] [--pool roundrobin|shuffle] [--cycles N] [--phi F] [--seed HEX] [--no-null]
    ///   The pool is the RANDOM-history ordering to repair (roundrobin = the mixed feed that caps at 133).
    ///   --phi weights the PPMI co-count bridge in the affinity (0 = shared-idiom/IDF only; 1 = + couplings).
    public static int Run(string[] args)
    {
        int fam      = Args.Int(args, "--fam", 8);
        int nMorph   = Args.Int(args, "--morph", 90);
        int mWin     = Args.Int(args, "--win", 12);
        int overlap  = Args.Int(args, "--overlap", 0);
        int wPer     = Args.Int(args, "--words", 12);
        int pPer     = Args.Int(args, "--phrases", 16);
        int tPer     = Args.Int(args, "--templates", 12);
        int linesPer = Args.Int(args, "--lines", 60);
        int cycles   = Args.Int(args, "--cycles", 4);
        double wPhi  = Args.Double(args, "--phi", 1.0);        // PPMI co-count bridge weight (0 = shared-idiom IDF only)
        string pool  = Args.Str(args, "--pool", "roundrobin");   // the mixed history to repair
        bool doNull  = !args.Contains("--no-null");
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);

        // ── the RANDOM-history tape: the full line-set in a globally-mixed order (the 133B-capped case) ──
        var corpus = new TowerCorpus(fam, nMorph, mWin, overlap, wPer, pPer, tPer, linesPer, holdEvery: 8, seed, negControl: false, pool, flat: false);
        int n = corpus.Lines.Count;
        var lineBytes = new byte[n][];
        var fams = new int[n];
        for (int i = 0; i < n; i++) { lineBytes[i] = corpus.Lines[i].Bytes; fams[i] = corpus.Lines[i].Fam; }
        int totalBytes = lineBytes.Sum(b => b.Length);

        var run = Cogito.Run.New("sleep");
        Trace.Note($"sleep · {corpus.Families} families · {n} lines ({totalBytes}B) + {corpus.Heldout.Count} held-out · pool={pool} · φ-bridge weight {wPhi:F1}");
        Trace.Note($"  DEFRAG the random-history tape via LEARNED label-free couplings → re-induce → does maxSpan climb 133 → 228?");
        Trace.Note("");

        // ── reference points: the random-history baseline (order = pool), the family-oracle ceiling ──
        var order0 = Enumerable.Range(0, n).ToArray();
        var baseline = Measure("baseline", 0, Induce(lineBytes, order0), corpus, Contiguity(order0, fams, corpus.Families), 0, 0);
        var oracleOrder = OracleReorder(lineBytes, fams, corpus.Families, seed);
        var oracle = Measure("oracle", 0, Induce(lineBytes, oracleOrder), corpus, Contiguity(oracleOrder, fams, corpus.Families), 1.0, 1.0);

        var log = new List<Cycle> { baseline, oracle };

        // ── the DEFRAG bootstrap: induce → couple → seriate → re-induce, iterated ──
        var order = order0;
        for (int t = 1; t <= cycles; t++)
        {
            var g = Induce(lineBytes, order);                          // grammar over the CURRENT tape
            var aff = LineAffinity(g, lineBytes, wPhi);                // label-free affinity over induced idioms
            var next = Chain(aff, n);                                // greedy nearest-affinity chain = the defrag
            var clusters = CutIntoK(next, aff, corpus.Families);       // segment the chain → clusters (for validation only)
            var (purity, ari) = ScoreClustering(clusters, fams, corpus.Families);
            order = next;
            log.Add(Measure("defrag", t, Induce(lineBytes, order), corpus, Contiguity(order, fams, corpus.Families), purity, ari));   // re-induce over the defragged tape
        }

        // ── the NULL: same cycle count, RANDOM re-serialization (no coupling guidance) ──
        if (doNull)
        {
            var norder = order0;
            ulong nrng = seed ^ 0xDEFA6;
            for (int t = 1; t <= cycles; t++)
            {
                norder = Shuffle(norder, ref nrng);
                log.Add(Measure("null-random", t, Induce(lineBytes, norder), corpus, Contiguity(norder, fams, corpus.Families), 0, 0));
            }
        }

        run.Write("cycles.tsv", Cycle.Header + "\n" + string.Join("\n", log.Select(c => c.Row())) + "\n");
        Report(log, cycles, corpus.Families);
        return 0;
    }

    // ── induce a grammar over the line-set in a given ORDER (the only thing that varies) ──────────────────
    private static RePairResult Induce(byte[][] lineBytes, int[] order)
    {
        var tape = new List<byte>();
        foreach (var i in order) { tape.AddRange(lineBytes[i]); tape.Add((byte)'\n'); }
        var (_, _, r) = Engine.Induce(tape.ToArray());
        return r;
    }

    private static Cycle Measure(string arm, int t, RePairResult g, IIntakeCorpus corpus, double contig, double purity, double ari)
    {
        var (scales, _, _, maxSpan, _) = Engine.RenormStats(g);
        var famSym = new double[corpus.Families]; var famCnt = new int[corpus.Families];
        foreach (var (fm, hb) in corpus.Heldout)
        {
            famSym[fm] += hb.Length == 0 ? 0 : (double)Engine.ParsedSize(g.Rules, hb) / hb.Length;
            famCnt[fm]++;
        }
        for (int f = 0; f < corpus.Families; f++) famSym[f] /= Math.Max(1, famCnt[f]);
        int deepFams = famSym.Count(s => s > 0 && s < DeepThresholdSymPerByte);
        double heldSym = corpus.Heldout.Count == 0 ? 0 : corpus.Heldout.Average(h => famSym[h.Fam]);
        return new Cycle(arm, t, (int)maxSpan, g.Rules.Length, scales, heldSym, deepFams, contig, purity, ari);
    }

    /// The DIRECT blocking read (independent of the gap-cut clustering): how ADJACENT are same-family lines in
    /// the produced order. Perfect blocking = only F−1 family-transitions (score 1); a fully-mixed order = ~n−1
    /// transitions (score 0). This — not the gap-cut purity — is the honest "did the reorder recover the blocking".
    private static double Contiguity(int[] order, int[] fam, int nFam)
    {
        int trans = 0;
        for (int t = 0; t < order.Length - 1; t++) if (fam[order[t]] != fam[order[t + 1]]) trans++;
        int ideal = nFam - 1, worst = order.Length - 1;
        return worst <= ideal ? 1.0 : 1.0 - (double)(trans - ideal) / (worst - ideal);
    }

    // ── the LABEL-FREE line affinity: how structurally related two lines are, read off the LEARNED grammar ───
    //
    // Each line is greedily covered by the grammar's rule-expansions → its set of induced UNITS (idioms). Two
    // signals, both from the learned model, neither from a family label:
    //   diagonal (always) — SHARED units, IDF-weighted: idf(u)=log(n/(1+df(u))) down-weights ubiquitous morphemes
    //                       to ~0 and up-weights family-specific templates. Cosine of the tf·idf unit vectors.
    //   φ-bridge (--phi>0) — the couplings' PPMI CO-COUNT: line i's units couple-forward (Couplings BuildScorer,
    //                        rich min_cocount=1) into line j's units. Catches related-but-not-identical idioms.
    /// `internal` (was private) — the drive's night-shift Consolidate (Cortex.cs) drives the SAME proven affinity to
    /// defrag THE TAPE (this is the mechanism the sleep verb proved: 133→304B label-free recovery, null holds).
    internal static double[][] LineAffinity(RePairResult g, byte[][] lineBytes, double wPhi)
    {
        var basis = AffinityBasis.Build(g, lineBytes, wPhi);
        int n = lineBytes.Length;
        var A = NewMatrix(n);
        for (int i = 0; i < n; i++)                                     // the O(n²) wall — every pair scored (the exact, proven path)
            for (int j = i + 1; j < n; j++)
            { double a = basis.Score(i, j); A[i][j] = a; A[j][i] = a; }
        return A;
    }

    /// MOUNT 1 — the O(spans²) → O(candidate-pairs) collapse. The precise φ-affinity is
    /// UNCHANGED (same tf·idf-diagonal + PPMI-bridge basis); the only difference from the exact path is WHICH pairs
    /// get scored: instead of all n², only pairs the SimHash index puts in a shared LSH bucket (near ⟹ shared
    /// bucket, Theorem 5.2). Two-tier retrieval: SimHash/Hamming FILTERS candidates, φ DECIDES their affinity.
    /// Non-candidate cells stay 0 — which the DENSE path already produces for cross-family pairs (near-disjoint
    /// vocab ⟹ ~0 cosine), so Chain's greedy chain is preserved where it matters (the within-family high-affinity
    /// edges) at a fraction of the scoring cost. `pairsScored` is the kill-line readout (candidate pairs vs n(n−1)/2).
    internal static double[][] LineAffinitySimhash(RePairResult g, byte[][] lineBytes, double wPhi, bool bandFlip, out int pairsScored)
    {
        var basis = AffinityBasis.Build(g, lineBytes, wPhi);
        int n = lineBytes.Length;
        var A = NewMatrix(n);
        var idx = SimhashIndex.OfEvents(lineBytes);                      // one fingerprint per span → LSH bands
        var pairs = idx.CandidatePairs(bandFlip);                       // only bucket-co-members are scored
        foreach (var (i, j) in pairs) { double a = basis.Score(i, j); A[i][j] = a; A[j][i] = a; }
        pairsScored = pairs.Count;
        return A;
    }

    private static double[][] NewMatrix(int n) { var A = new double[n][]; for (int i = 0; i < n; i++) A[i] = new double[n]; return A; }

    // ── THE AFFINITY BASIS ──  the O(n) per-line precompute the pairwise Score reads (cover units → tf·idf diagonal
    // + the couplings' PPMI φ-bridge). Extracted so the EXACT (all-pairs) and SIMHASH-GATED (candidate-pairs) entries
    // share ONE definition of "how affine are lines i and j" — the mount changes the pair SET, never the affinity.
    private readonly struct AffinityBasis(
        Dictionary<int, Dictionary<int, int>> units, Dictionary<int, Dictionary<int, double>> vec, Dictionary<int, double> norm,
        Dictionary<int, Dictionary<int, double>> bridgeSrc, Dictionary<int, double> bnorm, double wPhi)
    {
        private readonly Dictionary<int, Dictionary<int, int>> _units = units;
        private readonly Dictionary<int, Dictionary<int, double>> _vec = vec;
        private readonly Dictionary<int, double> _norm = norm;
        private readonly Dictionary<int, Dictionary<int, double>> _bridgeSrc = bridgeSrc;
        private readonly Dictionary<int, double> _bnorm = bnorm;
        private readonly double _wPhi = wPhi;

        /// `touched` null ⟹ every line is covered/vectorized (the exact arm — identical math to the original).
        /// Non-null (ascending) ⟹ ONLY those line indices are (the weave: basis cost O(touched), never O(tape));
        /// Score may then be called on touched pairs exclusively, and the idf population is the scored subset.
        public static AffinityBasis Build(RePairResult g, byte[][] lineBytes, double wPhi, int[]? touched = null, WeaveModel? standing = null,
            IReadOnlyDictionary<int, byte[]>? sparseLines = null, int totalLines = -1)
        {
            long tPhase = Trace.NowTicks;
            long msModel = 0, msCover = 0, msIdf = 0, msVector = 0, msBridge = 0;
            int n = sparseLines is null ? lineBytes.Length : totalLines;
            int[] scored = touched ?? Enumerable.Range(0, n).ToArray();

            Dictionary<int, List<(int Idx, byte[] E)>> byPrefix;
            Scorer? standingRich = null;
            if (standing is not null)
            {
                tPhase = Trace.NowTicks;
                standing.Ensure(g);
                msModel = Trace.ElapsedMs(tPhase);
                byPrefix = null!;
                standingRich = standing.Rich;
            }
            else
            {
                byPrefix = WeaveModel.BuildPrefixIndex(g.Rules);
            }
            // cover each scored line → its unit multiset (rule index → count).
            var units = new Dictionary<int, Dictionary<int, int>>(scored.Length);
            // Only units that actually cover a touched line can contribute to IDF.  The prior delta path
            // allocated/scanned one counter per grammar rule (35k at the step-56 spike) even when nearly all
            // rules were absent from the Δ population; keep the population sparse and preserve the same formula.
            var df = new Dictionary<int, int>();
            tPhase = Trace.NowTicks;
            foreach (int i in scored)
            {
                byte[] line = sparseLines is null ? lineBytes[i] : sparseLines[i];
                var u = standing is null ? CoverUnits(byPrefix, line) : standing.CoverUnits(i, line);
                units[i] = u;
                foreach (var k in u.Keys) df[k] = df.GetValueOrDefault(k) + 1;
            }
            msCover = Trace.ElapsedMs(tPhase);
            standing?.ClearChangedPrefixes();
            tPhase = Trace.NowTicks;
            var idf = new Dictionary<int, double>(df.Count);
            foreach (var (u, count) in df) idf[u] = Math.Log((double)scored.Length / (1 + count));   // rarer unit ⇒ higher weight
            msIdf = Trace.ElapsedMs(tPhase);

            // tf·idf vectors + norms (the diagonal signal).
            var vec = new Dictionary<int, Dictionary<int, double>>(scored.Length);
            var norm = new Dictionary<int, double>(scored.Length);
            tPhase = Trace.NowTicks;
            foreach (int i in scored)
            {
                var v = new Dictionary<int, double>(units[i].Count);
                double sq = 0;
                foreach (var (u, c) in units[i]) { double w = c * idf[u]; v[u] = w; sq += w * w; }
                vec[i] = v; norm[i] = Math.Sqrt(sq) + 1e-9;
            }
            msVector = Trace.ElapsedMs(tPhase);

            // the couplings' PPMI co-count bridge: per line, accumulate forward-φ mass onto neighbour units.
            var bridgeSrc = new Dictionary<int, Dictionary<int, double>>(scored.Length);   // line → (neighbour unit → Σφ from this line's units)
            var bnorm = new Dictionary<int, double>(scored.Length);
            if (wPhi > 0)
            {
                var rich = standingRich ?? Couplings.Learn(g).BuildScorer(minCocount: 1);
                tPhase = Trace.NowTicks;
                foreach (int i in scored)
                {
                    var acc = new Dictionary<int, double>();
                    foreach (var a in units[i].Keys)
                        foreach (var (b, phi) in rich.Fwd(Symbol.FirstNonterminal + (uint)a))
                            if (b >= Symbol.FirstNonterminal)
                            {
                                int bu = (int)(b - Symbol.FirstNonterminal);
                                acc[bu] = acc.GetValueOrDefault(bu) + phi;
                            }
                    bridgeSrc[i] = acc;
                    double sq = 0; foreach (var w in acc.Values) sq += w * w; bnorm[i] = Math.Sqrt(sq) + 1e-9;
                }
                msBridge = Trace.ElapsedMs(tPhase);
            }
            if (standing is not null)
                Trace.Seriate.Boundary("weave.basis", $"ms: model={msModel} cover={msCover} idf={msIdf} vector={msVector} bridge={msBridge} · touched={scored.Length} units={df.Count}");
            return new AffinityBasis(units, vec, norm, bridgeSrc, bnorm, wPhi);
        }

        /// The affinity of lines i and j — cosine of the tf·idf unit vectors + (when φ>0) the symmetric PPMI-bridge.
        /// IDENTICAL arithmetic to the pre-refactor inline loop, so exact-mode Chain is byte-for-byte unchanged.
        public double Score(int i, int j)
        {
            double dot = 0;
            var (small, big) = _vec[i].Count <= _vec[j].Count ? (_vec[i], _vec[j]) : (_vec[j], _vec[i]);
            foreach (var (u, w) in small) if (big.TryGetValue(u, out var w2)) dot += w * w2;
            double a = dot / (_norm[i] * _norm[j]);                       // cosine diagonal
            if (_wPhi > 0)
            {
                double br = 0;
                foreach (var (u, w) in _units[j]) if (_bridgeSrc[i].TryGetValue(u, out var m)) br += m;   // i→j
                foreach (var (u, w) in _units[i]) if (_bridgeSrc[j].TryGetValue(u, out var m)) br += m;   // j→i
                a += _wPhi * br / (_bnorm[i] * _bnorm[j]);
            }
            return a;
        }
    }

    /// Greedy longest-first cover of a line → the multiset of induced units (rule indices) it decomposes to.
    /// Uncovered bytes contribute no unit — only the grammar's own idioms count toward affinity. `byPrefix` holds
    /// the length-sorted expansions bucketed by their first two bytes (an expansion is ≥ 2 bytes by construction),
    /// so each position probes one bucket instead of the whole rule set — same winner, a fraction of the compares.
    private static Dictionary<int, int> CoverUnits(Dictionary<int, List<(int Idx, byte[] E)>> byPrefix, byte[] text)
    {
        var u = new Dictionary<int, int>();
        int i = 0;
        while (i < text.Length)
        {
            int best = 0, bestIdx = -1;
            if (text.Length - i >= 2 && byPrefix.TryGetValue((text[i] << 8) | text[i + 1], out var cands))
                foreach (var (idx, e) in cands)
                    if (e.Length <= text.Length - i && text.AsSpan(i, e.Length).SequenceEqual(e)) { best = e.Length; bestIdx = idx; break; }
            if (bestIdx >= 0) { u[bestIdx] = u.GetValueOrDefault(bestIdx) + 1; i += best; }
            else i++;
        }
        return u;
    }

    private static Dictionary<int, int> CoverUnits(WeaveModel standing, byte[] text)
    {
        var u = new Dictionary<int, int>();
        int i = 0;
        while (i < text.Length)
        {
            int best = 0, bestIdx = -1;
            if (text.Length - i >= 2)
            {
                int key = (text[i] << 8) | text[i + 1];
                foreach (var (idx, e) in standing.Candidates(key))
                    if (e.Length <= text.Length - i && text.AsSpan(i, e.Length).SequenceEqual(e)) { best = e.Length; bestIdx = idx; break; }
            }
            if (bestIdx >= 0) { u[bestIdx] = u.GetValueOrDefault(bestIdx) + 1; i += best; }
            else i++;
        }
        return u;
    }

    // ── the DEFRAG: a greedy nearest-affinity chain re-orders the tape so affine lines abut ─────────────────
    // Deterministic: start at the highest total-affinity line, always append the unplaced line most affine to the
    // current tail (id tie-break). With near-disjoint family vocab, intra-family affinity ≫ cross, so the chain
    // consumes a family (chaining its template-sharing lines adjacent — the maxSpan lever) before jumping to the
    // next — recovering BOTH the blocking and the within-family order, from couplings alone.
    internal static int[] Chain(double[][] A, int n)   // `internal` — the drive's Consolidate re-orders THE TAPE with this exact greedy chain
    {
        var placed = new bool[n];
        int start = 0; double bestSum = double.NegativeInfinity;
        for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += A[i][j]; if (s > bestSum) { bestSum = s; start = i; } }
        var order = new int[n];
        order[0] = start; placed[start] = true;
        int cur = start;
        for (int k = 1; k < n; k++)
        {
            int best = -1; double bestA = double.NegativeInfinity;
            for (int j = 0; j < n; j++) if (!placed[j] && (A[cur][j] > bestA || (A[cur][j] == bestA && (best < 0 || j < best)))) { bestA = A[cur][j]; best = j; }
            order[k] = best; placed[best] = true; cur = best;
        }
        return order;
    }

    private const int WeavePartners = 12;   // Δ-weave candidate partners per new span (Hamming top-k off the persistent index) — bounds the scored pairs at Δ·k

    /// THE O(Δ) WEAVE — Chain's incremental sibling, the night shift's standing defrag.
    /// The full re-seriation above costs O(spans²) in affinity cells and chain scans — cost that grows with the
    /// TAPE, the pin's definition of a bug. The weave's contract: spans already woven stay PUT (the standing order
    /// IS the accumulated product of every previous sleep); only the Δ spans appended since the last pass are
    /// candidate-generated (bucket co-members off the persistent index — new×existing and new×earlier-new, never an
    /// all-pairs sweep), φ-scored (the SAME AffinityBasis, built over the touched subset only), and SPLICED directly
    /// after their most-affine earlier partner. Id-ascending processing, so an earlier new span is already home when
    /// a later one picks it as partner. No paying partner (affinity ≤ 0, or no bucket co-member) ⟹ the span keeps
    /// its append position. Deterministic throughout: candidate order (Hamming asc, slot asc), argmax tie → lowest
    /// slot. Returns the Reorder permutation, or null when Δ = 0 (a grok-lock sleep can fire between appends).
    internal static int[]? WeaveNew(RePairResult g, Tape tape, SimhashIndex idx, int firstNewSlot, double wPhi,
        out int pairsScored, out int placed, out double meanAffinity)
        => WeaveNew(g, tape, idx, firstNewSlot, wPhi, null, out pairsScored, out placed, out meanAffinity);

    internal static int[]? WeaveNew(RePairResult g, Tape tape, SimhashIndex idx, int firstNewSlot, double wPhi,
        WeaveModel? standing, out int pairsScored, out int placed, out double meanAffinity)
    {
        pairsScored = 0; placed = 0; meanAffinity = 0;
        int nSlots = idx.Count;                                        // id-rank feed slots — ALL ids ever indexed (shed/dropped included)
        int nPos = tape.Count;                                         // resident positions — the only thing an order can permute
        if (nSlots == 0 || firstNewSlot >= nSlots || nPos == 0) return null;

        // Δ-candidates off the persistent index (slots are id-rank; a slot's bytes resolve via its TapeEventID).
        // Evacuated partners are FILTERED here: a shed span has no position to splice after — the weave orders
        // the resident window only (the view's shed tail is id-fixed; defrag and shedding never fight).
        long tW = Trace.NowTicks;
        int dn = nSlots - firstNewSlot;
        var candsOf = new List<int>[dn];
        var touched = new HashSet<int>();
        var scratch = new List<int>();
        for (int s = firstNewSlot; s < nSlots; s++)
        {
            idx.TopPriorCandidates(s, WeavePartners, scratch);
            var kept = new List<int>(scratch.Count);
            foreach (int c in scratch) if (tape.PositionOf(idx.IdAt(c)) >= 0) kept.Add(c);
            candsOf[s - firstNewSlot] = kept;
            touched.Add(s);
            foreach (int c in kept) touched.Add(c);
        }
        var tarr = touched.ToArray();
        Array.Sort(tarr);                                              // ascending — the basis' deterministic cover order
        long msCands = Trace.ElapsedMs(tW);

        // the SAME proven affinity, restricted to the touched subset — O(touched) basis, O(Δ·k) scores.
        tW = Trace.NowTicks;
        var bySlot = new Dictionary<int, byte[]>(tarr.Length);
        foreach (int t in tarr) { tape.Resolve(idx.IdAt(t), out var b); bySlot[t] = b; }
        var basis = AffinityBasis.Build(g, [], wPhi, tarr, standing, bySlot, nSlots);
        long msBasis = Trace.ElapsedMs(tW);
        tW = Trace.NowTicks;

        // best earlier partner per new span (affinity argmax; tie → lowest slot; must strictly pay > 0).
        var target = new int[dn];
        Array.Fill(target, -1);
        double affSum = 0;
        for (int s = firstNewSlot; s < nSlots; s++)
        {
            double bestA = 0; int best = -1;
            foreach (int c in candsOf[s - firstNewSlot])
            {
                double a = basis.Score(s, c);
                pairsScored++;
                if (a <= 0) continue;
                if (best < 0 || a > bestA || (a == bestA && c < best)) { bestA = a; best = c; }
            }
            if (best >= 0) { target[s - firstNewSlot] = best; placed++; affSum += bestA; }
        }
        meanAffinity = placed == 0 ? 0 : affSum / placed;
        Trace.Seriate.Boundary("weave.sub", $"ms: cands={msCands} basis={msBasis} score={Trace.ElapsedMs(tW)} · Δ={dn} touched={tarr.Length} pairs={pairsScored} · model={(standing is null ? "ephemeral" : $"standing reset={standing.Resets} append={standing.Appends} splice={standing.Splices}")}");
        if (placed == 0) return null;                                  // nothing paid — keep the standing order untouched
        standing?.RecordSplice();

        // SPLICE — a doubly-linked list over current tape positions (node identity = position, sentinel nPos).
        // Each placed new span unlinks from its append position and re-links directly after its partner's node —
        // wherever that node now sits, including a partner that was itself woven earlier this pass.
        var next = new int[nPos + 1]; var prev = new int[nPos + 1];
        for (int i = 0; i < nPos; i++) { next[i] = i + 1 == nPos ? nPos : i + 1; prev[i] = i == 0 ? nPos : i - 1; }
        next[nPos] = 0; prev[nPos] = nPos - 1;
        for (int s = firstNewSlot; s < nSlots; s++)
        {
            int t = target[s - firstNewSlot];
            if (t < 0) continue;
            int node = tape.PositionOf(idx.IdAt(s));                   // new spans are always resident
            int after = tape.PositionOf(idx.IdAt(t));                  // partners were resident-filtered above
            if (node == after) continue;                               // cannot happen (distinct ids) — belt against a future id-aliasing bug
            next[prev[node]] = next[node]; prev[next[node]] = prev[node];   // unlink
            next[node] = next[after]; prev[node] = after;                   // relink after the partner
            prev[next[after]] = node; next[after] = node;
        }
        var order = new int[nPos];
        for (int k = 0, at = next[nPos]; k < nPos; k++, at = next[at]) order[k] = at;
        return order;
    }

    /// Segment the seriated chain into K clusters by cutting at the K−1 largest affinity DROPS between adjacent
    /// placed lines (validation only — never touches the tape order). Recovers the clusters the label-free chain
    /// found, to score against the reference families.
    private static int[] CutIntoK(int[] order, double[][] A, int k)
    {
        int n = order.Length;
        var gaps = new List<(double A, int Pos)>(n - 1);
        for (int t = 0; t < n - 1; t++) gaps.Add((A[order[t]][order[t + 1]], t));
        gaps.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : x.Pos.CompareTo(y.Pos));   // smallest affinity = cut
        var cut = new HashSet<int>();
        for (int c = 0; c < k - 1 && c < gaps.Count; c++) cut.Add(gaps[c].Pos);
        var cl = new int[n];
        int id = 0;
        for (int t = 0; t < n; t++) { cl[order[t]] = id; if (cut.Contains(t)) id++; }
        return cl;
    }

    // purity = Σ_c max_f |cluster c ∩ family f| / n ; ARI = adjusted Rand index of clustering vs families.
    private static (double Purity, double Ari) ScoreClustering(int[] cl, int[] fam, int nFam)
    {
        int n = cl.Length;
        int nCl = cl.Max() + 1;
        var cont = new long[nCl, nFam];
        foreach (var _ in cl) { }
        for (int i = 0; i < n; i++) cont[cl[i], fam[i]]++;
        long pure = 0;
        for (int c = 0; c < nCl; c++) { long mx = 0; for (int f = 0; f < nFam; f++) mx = Math.Max(mx, cont[c, f]); pure += mx; }
        double purity = (double)pure / n;

        // ARI
        double sumC2 = 0, aRow = 0, bCol = 0;
        var rowSum = new long[nCl]; var colSum = new long[nFam];
        for (int c = 0; c < nCl; c++) for (int f = 0; f < nFam; f++) { long v = cont[c, f]; sumC2 += Choose2(v); rowSum[c] += v; colSum[f] += v; }
        foreach (var r in rowSum) aRow += Choose2(r);
        foreach (var cc in colSum) bCol += Choose2(cc);
        double tot = Choose2(n);
        double expected = tot == 0 ? 0 : aRow * bCol / tot;
        double maxIndex = 0.5 * (aRow + bCol);
        double ari = Math.Abs(maxIndex - expected) < 1e-12 ? 0 : (sumC2 - expected) / (maxIndex - expected);
        return (purity, ari);
    }

    private static double Choose2(long x) => x < 2 ? 0 : (double)x * (x - 1) / 2.0;

    // ── the ORACLE reorder: true family-major, then within-family greedy affinity — the reordering CEILING a
    // perfect label-free clustering could reach (it recovers blocking AND within-family template adjacency). ──
    private static int[] OracleReorder(byte[][] lineBytes, int[] fam, int nFam, ulong seed)
    {
        var g = Induce(lineBytes, Enumerable.Range(0, lineBytes.Length).ToArray());
        var aff = LineAffinity(g, lineBytes, 0.0);   // diagonal affinity suffices for within-family ordering
        var order = new List<int>(lineBytes.Length);
        for (int f = 0; f < nFam; f++)
        {
            var members = Enumerable.Range(0, lineBytes.Length).Where(i => fam[i] == f).ToList();
            order.AddRange(ChainSubset(aff, members));   // affinity-chain WITHIN the true family
        }
        return order.ToArray();
    }

    private static IEnumerable<int> ChainSubset(double[][] A, List<int> members)
    {
        if (members.Count == 0) yield break;
        var placed = new HashSet<int>();
        int cur = members[0]; placed.Add(cur); yield return cur;
        for (int k = 1; k < members.Count; k++)
        {
            int best = -1; double bestA = double.NegativeInfinity;
            foreach (var j in members) if (!placed.Contains(j) && (A[cur][j] > bestA || (A[cur][j] == bestA && (best < 0 || j < best)))) { bestA = A[cur][j]; best = j; }
            placed.Add(best); cur = best; yield return best;
        }
    }

    private static int[] Shuffle(int[] src, ref ulong rng)
    {
        var a = (int[])src.Clone();
        for (int i = a.Length - 1; i > 0; i--) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; int j = (int)((rng >> 33) % (ulong)(i + 1)); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }

    // ── the read ──
    private static void Report(List<Cycle> log, int cycles, int families)
    {
        var baseline = log.First(c => c.Arm == "baseline");
        var oracle = log.First(c => c.Arm == "oracle");
        Trace.Note($"  ── SLEEP-CYCLE CURVE (maxSpan = the relational scale; random history caps it, the defrag lifts it) ──");
        Trace.Note($"     baseline (random history) maxSpan {baseline.MaxSpan}B contig {baseline.Contig:F2}  ·  oracle (true-family reorder) maxSpan {oracle.MaxSpan}B contig {oracle.Contig:F2}");
        Trace.Note($"     contig = family-adjacency of the produced order (1=perfectly blocked, 0=fully mixed) — the DIRECT blocking read");
        Trace.Note("");
        Trace.Note($"     arm            cycle  maxSpan   rules  scales   heldSym  deepFams   contig   purity    ARI");
        foreach (var c in log.Where(c => c.Arm == "defrag"))
            Trace.Note($"     defrag           {c.T,2}    {c.MaxSpan,4}B    {c.Rules,4}    {c.Scales,3}    {c.HeldSym,6:F4}    {c.DeepFams}/{families}    {c.Contig,5:F3}  {c.Purity,5:F3}  {c.Ari,5:F3}");
        foreach (var c in log.Where(c => c.Arm == "null-random"))
            Trace.Note($"     null-random      {c.T,2}    {c.MaxSpan,4}B    {c.Rules,4}    {c.Scales,3}    {c.HeldSym,6:F4}    {c.DeepFams}/{families}    {c.Contig,5:F3}    —      —");
        Trace.Note("");

        var defrags = log.Where(c => c.Arm == "defrag").OrderBy(c => c.T).ToList();
        var nulls = log.Where(c => c.Arm == "null-random").OrderBy(c => c.T).ToList();
        int defBest = defrags.Count > 0 ? defrags.Max(c => c.MaxSpan) : baseline.MaxSpan;
        int nullBest = nulls.Count > 0 ? nulls.Max(c => c.MaxSpan) : baseline.MaxSpan;
        double bestContig = defrags.Count > 0 ? defrags.Max(c => c.Contig) : baseline.Contig;
        double bestPurity = defrags.Count > 0 ? defrags.Max(c => c.Purity) : 0;
        bool spanRecovers = defBest > baseline.MaxSpan * 1.2 && defBest > nullBest * 1.2;
        bool blockRecovers = bestContig > 0.7 && bestContig > baseline.Contig + 0.4;
        bool bootstraps = defrags.Count >= 2 && defrags.Last().MaxSpan > defrags.First().MaxSpan;
        double reach = oracle.MaxSpan > baseline.MaxSpan ? (double)(defBest - baseline.MaxSpan) / (oracle.MaxSpan - baseline.MaxSpan) : 0;

        Trace.Note($"  ── VERDICT ──");
        Trace.Note($"    couplings-guided defrag RECOVERS the relational scale : {(spanRecovers ? "YES" : "no")} (maxSpan {baseline.MaxSpan}→{defBest}B vs null {nullBest}B; oracle ceiling {oracle.MaxSpan}B, {reach:P0} of the gap closed)");
        Trace.Note($"    label-free couplings PRODUCE the family blocking       : {(blockRecovers ? "YES" : "no")} (contiguity {baseline.Contig:F2}→{bestContig:F2}; oracle 1.00) — the direct label-free coupling test");
        Trace.Note($"    the sleep-CYCLE bootstraps (each cycle deeper)         : {(bootstraps ? "YES" : cycle1AtCeiling(defrags, oracle) ? "moot — cycle 1 already at the ceiling" : "no")} (defrag maxSpan {string.Join("→", defrags.Select(c => c.MaxSpan))}B)");
        Trace.Note($"    the random-reserialization null does NOT recover        : {(nullBest <= baseline.MaxSpan * 1.2 ? "confirmed (null flat)" : "NULL ALSO MOVES — recovery is not coupling-specific")} (null best {nullBest}B, contig {(nulls.Count > 0 ? nulls.Max(c => c.Contig) : 0):F2})");
        Trace.Note($"    (gap-cut clustering purity {bestPurity:P0} / ARI {(defrags.Count > 0 ? defrags.Max(c => c.Ari) : 0):F3} — a crude cluster extractor; contiguity is the load-bearing blocking read)");
        Trace.Note("");
        Trace.Note(spanRecovers && blockRecovers
            ? $"    ⇒ SELF-REPAIR CONFIRMED: label-free couplings PRODUCE the blocking a bad intake destroyed (contig {baseline.Contig:F2}→{bestContig:F2}) AND recover the scale ({baseline.MaxSpan}→{defBest}B) — the machine consolidates its way to the relational scale during sleep."
            : spanRecovers
                ? $"    ⇒ PARTIAL: the defrag lifts maxSpan {baseline.MaxSpan}→{defBest}B but does NOT cleanly block the families (contig {bestContig:F2}) — it chains enough same-template lines adjacent to mint the deep rule without a global family sort. Scale recovers; clean blocking does not."
                : $"    ⇒ learned couplings do NOT recover the scale without labels (maxSpan {defBest}B, contig {bestContig:F2}) — the affinity is too noisy to defrag; the intake cap holds (honest negative).");
    }

    // bootstrap is MOOT (not a failure) when cycle 1 already reached the reordering ceiling — there is no
    // headroom left to climb, so a flat 304→304 curve is saturation, not a stalled bootstrap.
    private static bool cycle1AtCeiling(List<Cycle> defrags, Cycle oracle) =>
        defrags.Count > 0 && defrags[0].MaxSpan >= oracle.MaxSpan * 0.95;

}
