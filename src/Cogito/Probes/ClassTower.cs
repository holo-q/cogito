namespace Cogito;

using System.Text;

// ── DEEPEN — context-conditional slot discovery + the recursive tower ──
//
// Port 2 (AntiUnify) crystallizes FLAT paradigm classes and rests there — pure compression never asks for a
// class-of-classes (the coact_growth verdict (c): the tower is never the MDL optimum under a flat two-part code).
// DEEPEN is the machinery that DOES build the tower, ported from coact/deepen.py + deepen_multi.py. Two moves:
//
//   DISCOVERY — the immediate slot a token wears = the LCA (lowest common ancestor) of the FILLER-SET of its
//   local CONTEXT, in the candidate class hierarchy. A context that interchanges multiple sub-classes activates
//   the COARSE class; a context that only ever admits one sub-class keeps it FINE. Purely distributional — no gold:
//       context `the ___ was [PERCEIVE]` is filled by {mammals, birds} → LCA = [NP]      (coarse)
//       context `the ___ crept into`     is filled by {mammals}        → LCA = [MAMMAL]   (fine)
//   The MDL is then CONTEXT-SENSITIVE: data on the discovered slot-stream + residual charged at the IMMEDIATE
//   slot the context selected. Under THAT code the gold tower [NP]={[MAMMAL],[BIRD]} is the global optimum
//   (probe3's result), and the discovery recovers it WITHOUT labels — oracle-to-the-bit.
//
//   THE TOWER — freshly-minted slots become the units for the NEXT round: anti-unify at the SLOT level (which
//   contexts interchange which slots), gate each candidate meta by context-sensitive ΔMDL, mint, repeat. Level k's
//   slots reveal level k+1. Over-build-safe: a corpus with no super-structure mints NO tower (the gate rejects it).
//
// Builds directly on Port 2 (reuses WordRePair for the induced-stream entropy + AntiUnify.MintCandidates for the
// slot-level anti-unification). Validated on the synthetic NESTED testbed where the reference structure is known: does the
// discovery recover the gold tower, and NOT over-build the no-superclass control?
//
// GLOSSARY: this `tower` is the DEEPEN discovered-class hierarchy, NOT the `tower` test-corpus elsewhere.
// Ported by mechanism.

public static class ClassTower
{
    private const char Sep = '\u001f';
    private const string F = "[F]", Bos = "[BOS]", Eos = "[EOS]";

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE DISCOVERY — immediate-slot from context filler-set LCA
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// One-level substitution: each word → its flat slot (or itself if unslotted). NOT resolve-to-top.
    private static List<string[]> FlatSlotted(IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s)
    {
        var outp = new List<string[]>(corpus.Count);
        foreach (var s in corpus)
        {
            var row = new string[s.Length];
            for (int i = 0; i < s.Length; i++) row[i] = flatM2s.GetValueOrDefault(s[i], s[i]);
            outp.Add(row);
        }
        return outp;
    }

    /// Symbols reliable enough to anchor a context: frequency ≥ theta, PLUS all slot symbols. Rare/filler symbols
    /// collapse to [F] when used as context (UNK-ing) so the context space doesn't fragment on hapax neighbours.
    private static HashSet<string> KeepSet(List<string[]> flat, HashSet<string> slotSet, int theta)
    {
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in flat) foreach (var c in s) freq[c] = freq.GetValueOrDefault(c) + 1;
        var keep = new HashSet<string>(slotSet, StringComparer.Ordinal);
        foreach (var (c, n) in freq) if (n >= theta) keep.Add(c);
        return keep;
    }

    /// The 'weak' context symbols a token looks PAST to find its discriminating context: the determiner(s) (top
    /// nDet most-frequent non-slot kept words — 'the' here) plus the filler/boundary tokens. A weak right neighbour
    /// gives an unreliable context (every noun follows 'the'), so the discoverer backs off to the nearest content.
    private static HashSet<string> WeakSet(List<string[]> flat, HashSet<string> slotSet, HashSet<string> keep, int nDet = 1)
    {
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in flat) foreach (var c in s) if (keep.Contains(c) && !slotSet.Contains(c)) freq[c] = freq.GetValueOrDefault(c) + 1;
        var dets = freq.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(nDet).Select(kv => kv.Key);
        var weak = new HashSet<string>(dets, StringComparer.Ordinal) { F, Bos, Eos };
        return weak;
    }

    private static string[] Collapse(string[] s, HashSet<string> keep)
    {
        var cc = new string[s.Length];
        for (int i = 0; i < s.Length; i++) cc[i] = keep.Contains(s[i]) ? s[i] : F;
        return cc;
    }

    /// The DISCRIMINATING local context of the class token at position i in collapsed stream cc. Primary = the
    /// right neighbour (the generative frame's verb). If it is WEAK (determiner/filler/boundary — uninformative),
    /// back off to the nearest CONTENT symbol on the LEFT (past determiners) — where a clause-final slot's frame
    /// identity lives. A slot symbol IS content here (`[PERCEIVE] the ___` identifies a clause-final NP2).
    private static string TokenContext(string[] cc, int i, HashSet<string> weak)
    {
        int n = cc.Length;
        string right = i + 1 < n ? cc[i + 1] : Eos;
        if (!weak.Contains(right)) return "R" + Sep + right;
        int j = i - 1;
        while (j >= 0 && weak.Contains(cc[j])) j--;
        string left = j >= 0 ? cc[j] : Bos;
        return "L" + Sep + left;
    }

    /// For every discriminating context, the SET of slot symbols filling its centre. A context filled by multiple
    /// sub-classes interchanges them (→ coarse); a single-class context keeps it fine.
    private static Dictionary<string, HashSet<string>> ContextFillerClasses(List<string[]> flat, HashSet<string> slotSet, HashSet<string> keep, HashSet<string> weak)
    {
        var ctx = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var s in flat)
        {
            var cc = Collapse(s, keep);
            for (int i = 0; i < s.Length; i++)
                if (slotSet.Contains(s[i]))
                    (ctx.TryGetValue(TokenContext(cc, i, weak), out var set) ? set : ctx[TokenContext(cc, i, weak)] = new(StringComparer.Ordinal)).Add(s[i]);
        }
        return ctx;
    }

    /// Lowest common ancestor of a set of slot symbols in the parent-chain hierarchy: their shared ancestor
    /// (coarse) if any, the slot itself (fine) if they ARE one, else null. The FINEST common ancestor = the first
    /// node in a representative chain that is in the common set.
    private static string? Lca(IReadOnlyCollection<string> slots, Dictionary<string, string> parent)
    {
        if (slots.Count == 0) return null;
        List<string> Chain(string s) { var o = new List<string> { s }; while (parent.TryGetValue(s, out var p)) { s = p; o.Add(s); } return o; }
        HashSet<string>? common = null;
        foreach (var s in slots.OrderBy(x => x, StringComparer.Ordinal))
        {
            var ch = new HashSet<string>(Chain(s), StringComparer.Ordinal);
            if (common is null) common = ch; else common.IntersectWith(ch);
        }
        if (common is null || common.Count == 0) return null;
        var rep = Chain(slots.OrderBy(x => x, StringComparer.Ordinal).First());
        foreach (var c in rep) if (common.Contains(c)) return c;
        return null;
    }

    private readonly record struct DiscoveryContext(List<string[]> Flat, HashSet<string> SlotSet, HashSet<string> Keep, HashSet<string> Weak, Dictionary<string, HashSet<string>> Ctx);

    private static DiscoveryContext BuildContext(IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s, int theta)
    {
        var flat = FlatSlotted(corpus, flatM2s);
        var slotSet = new HashSet<string>(flatM2s.Values, StringComparer.Ordinal);
        var keep = KeepSet(flat, slotSet, theta);
        var weak = WeakSet(flat, slotSet, keep);
        var ctx = ContextFillerClasses(flat, slotSet, keep, weak);
        return new DiscoveryContext(flat, slotSet, keep, weak, ctx);
    }

    /// THE DISCOVERY ENCODER. Given flat classes + a candidate hierarchy (parent: child-slot → parent-slot), assign
    /// each class token the immediate slot its CONTEXT selects (LCA of the context's filler-classes), NO gold labels.
    /// Returns the discovered slotted corpus + the per-IMMEDIATE-slot filler distributions (the context-sensitive
    /// member-choice residual is charged there).
    private static (List<string[]> Slotted, Dictionary<string, Dictionary<string, int>> Fillers) DiscoverSlotted(
        IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s, Dictionary<string, string> parent, int theta)
    {
        var dc = BuildContext(corpus, flatM2s, theta);
        var slotted = new List<string[]>(corpus.Count);
        var fillers = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        for (int r = 0; r < corpus.Count; r++)
        {
            var orig = corpus[r]; var fs = dc.Flat[r];
            var cc = Collapse(fs, dc.Keep);
            var row = new string[orig.Length];
            for (int i = 0; i < orig.Length; i++)
            {
                if (!dc.SlotSet.Contains(fs[i])) { row[i] = fs[i]; continue; }
                var fillerClasses = dc.Ctx.GetValueOrDefault(TokenContext(cc, i, dc.Weak)) ?? new HashSet<string>(StringComparer.Ordinal);
                string immediate = Lca(fillerClasses, parent) ?? fs[i];
                row[i] = immediate;
                (fillers.TryGetValue(immediate, out var f) ? f : fillers[immediate] = new(StringComparer.Ordinal))[orig[i]] =
                    (fillers[immediate].TryGetValue(orig[i], out var c) ? c : 0) + 1;
            }
            slotted.Add(row);
        }
        return (slotted, fillers);
    }

    /// The flat-sub reference state: every class token wears its FINE slot, no tower.
    private static (List<string[]> Slotted, Dictionary<string, Dictionary<string, int>> Fillers) FlatState(
        IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s)
    {
        var slotted = new List<string[]>(corpus.Count);
        var fillers = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var s in corpus)
        {
            var row = new string[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                string sym = flatM2s.GetValueOrDefault(s[i], s[i]);
                row[i] = sym;
                if (sym != s[i])
                    (fillers.TryGetValue(sym, out var f) ? f : fillers[sym] = new(StringComparer.Ordinal))[s[i]] =
                        (fillers[sym].TryGetValue(s[i], out var c) ? c : 0) + 1;
            }
            slotted.Add(row);
        }
        return (slotted, fillers);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE CONTEXT-SENSITIVE MDL — data on the discovered slot-stream + residual at the IMMEDIATE slot
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// cs_mdl (probe3) on the fast Re-Pair: data = entropy of the compressed discovered slot-stream; grammar = the
    /// rules; slotdef = the member lists (a count); residual = Σ entropy of each IMMEDIATE slot's filler
    /// distribution (the context-sensitive member-choice code). Total = the description length of the tower state.
    private static double CsMdl(List<string[]> slotted, Dictionary<string, Dictionary<string, int>> fillers, int nSlotdef)
    {
        var snap = WordRePair.Induce(slotted);
        double lg = Math.Log2(snap.NAlpha);
        var counts = new Dictionary<int, int>(); long tot = 0;
        foreach (var st in snap.Compressed) foreach (var x in st) { counts[x] = counts.GetValueOrDefault(x) + 1; tot++; }
        double data = 0;
        if (tot > 0) foreach (var sym in counts.Keys.OrderBy(x => x)) data += counts[sym] * -Math.Log2((double)counts[sym] / tot);
        double grammar = snap.Rules.Count * 2 * lg;
        double slotdef = nSlotdef * lg;
        double residual = 0;
        foreach (var slot in fillers.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var fc = fillers[slot]; long ftot = 0; foreach (var v in fc.Values) ftot += v;
            foreach (var t in fc.Keys.OrderBy(x => x, StringComparer.Ordinal)) residual += fc[t] * -Math.Log2((double)fc[t] / ftot);
        }
        return data + grammar + slotdef + residual;
    }

    private static double DiscoveredTotal(IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s, Dictionary<string, string> parent, int theta)
    {
        var (slotted, fillers) = parent.Count > 0 ? DiscoverSlotted(corpus, flatM2s, parent, theta) : FlatState(corpus, flatM2s);
        return CsMdl(slotted, fillers, flatM2s.Count + parent.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE TOWER — anti-unify at the slot level, gate by context-sensitive ΔMDL, mint, iterate
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// Anti-unify at the SLOT level: the weight of a (top-slot, top-slot) pair = how often a single context
    /// interchanges them. The level-2 context binds all leaves but WEAKLY per-pair (does not blob them); a level-1
    /// context binds its sibling pair TIGHTLY. Offers both the strong pairs AND the mutual-kNN groups; the gate +
    /// greedy pick the tightest level first, so depth is built one level at a time.
    private static List<List<string>> ProposeMetas(IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s, Dictionary<string, string> parent, int theta, int k = 2, int minWeight = 5)
    {
        var dc = BuildContext(corpus, flatM2s, theta);
        string ToTop(string sym) { while (parent.TryGetValue(sym, out var p)) sym = p; return sym; }

        var ctxTops = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var s in dc.Flat)
        {
            var cc = Collapse(s, dc.Keep);
            for (int i = 0; i < s.Length; i++)
                if (dc.SlotSet.Contains(s[i]))
                {
                    string key = TokenContext(cc, i, dc.Weak); string top = ToTop(s[i]);
                    (ctxTops.TryGetValue(key, out var t) ? t : ctxTops[key] = new(StringComparer.Ordinal))[top] =
                        (ctxTops[key].TryGetValue(top, out var c) ? c : 0) + 1;
                }
        }
        var edge = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tops in ctxTops.Values)
        {
            var present = tops.Where(kv => kv.Value >= 3).Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < present.Length; i++)
                for (int j = i + 1; j < present.Length; j++)
                {
                    string key = present[i] + Sep + present[j];
                    edge[key] = edge.GetValueOrDefault(key) + Math.Min(tops[present[i]], tops[present[j]]);
                }
        }
        var cands = new List<List<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, w) in edge)
            if (w >= minWeight)
            {
                int si = key.IndexOf(Sep);
                var pair = new List<string> { key[..si], key[(si + 1)..] };
                string k2 = string.Join(Sep, pair);
                if (seen.Add(k2)) cands.Add(pair);
            }
        foreach (var grp in AntiUnify.MintCandidates(edge, k, minWeight))
        {
            string k2 = string.Join(Sep, grp);
            if (seen.Add(k2)) cands.Add(grp);
        }
        // strongest pairs first — the gate sees the tightest mates before looser groupings
        cands.Sort((a, b) => MinPair(b, edge).CompareTo(MinPair(a, edge)));
        return cands;

        static int MinPair(List<string> c, Dictionary<string, int> e)
        {
            int mn = int.MaxValue;
            var ws = c.ToArray(); Array.Sort(ws, StringComparer.Ordinal);
            for (int i = 0; i < ws.Length; i++) for (int j = i + 1; j < ws.Length; j++) mn = Math.Min(mn, e.GetValueOrDefault(ws[i] + Sep + ws[j]));
            return mn == int.MaxValue ? 0 : mn;
        }
    }

    /// One tower level's telemetry — what was proposed, gated, and minted (the discovered-hierarchy build log).
    public readonly record struct Level(int Depth, string[] Proposed, string[] Minted);

    /// Iterate: propose metas over current top-slots, gate each by context-sensitive ΔMDL, mint accepted (non-
    /// overlapping, highest-gain first), repeat until no proposal pays. Returns the parent map (the discovered
    /// hierarchy) + the per-level log. Deterministic.
    public static (Dictionary<string, string> Parent, List<Level> Log) GrowHierarchy(
        IReadOnlyList<string[]> corpus, Dictionary<string, string> flatM2s, int theta, int maxLevels = 5)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var log = new List<Level>();
        for (int level = 0; level < maxLevels; level++)
        {
            double cur = DiscoveredTotal(corpus, flatM2s, parent, theta);
            var cands = ProposeMetas(corpus, flatM2s, parent, theta);
            var proposedNames = cands.Select(c => string.Join("+", c.Select(Strip))).ToArray();
            var minted = new List<(string Name, List<string> Members, double Gain)>();
            foreach (var members in cands)
            {
                if (members.Any(m => parent.ContainsKey(m))) continue;   // already grouped
                string name = "[[" + string.Join("+", members.Select(Strip)) + "]]";
                var testParent = new Dictionary<string, string>(parent, StringComparer.Ordinal);
                foreach (var m in members) testParent[m] = name;
                double gain = cur - DiscoveredTotal(corpus, flatM2s, testParent, theta);
                if (gain > 1) minted.Add((name, members, gain));
            }
            if (minted.Count == 0) { log.Add(new Level(level, proposedNames, [])); break; }

            var taken = new HashSet<string>(StringComparer.Ordinal);
            var mintedNames = new List<string>();
            foreach (var (name, members, _) in minted.OrderByDescending(x => x.Gain))
            {
                if (members.Any(m => taken.Contains(m))) continue;
                foreach (var m in members) { parent[m] = name; taken.Add(m); }
                mintedNames.Add(name);
            }
            log.Add(new Level(level, proposedNames, mintedNames.ToArray()));
        }
        return (parent, log);
    }

    private static string Strip(string slot) => slot.Trim('[', ']');

    // the leaf-slots under an internal node (used to compare discovered structure to gold, member-set-wise)
    private static HashSet<string> Leaves(string node, Dictionary<string, string> parent)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (c, p) in parent) (children.TryGetValue(p, out var l) ? l : children[p] = new()).Add(c);
        var outp = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(); stack.Push(node);
        while (stack.Count > 0)
        {
            var u = stack.Pop();
            if (children.TryGetValue(u, out var kids)) foreach (var k in kids) stack.Push(k);
            else outp.Add(u);
        }
        return outp;
    }

    // the set of leaf-partitions per internal node — the structure signature (order-free comparison to gold)
    private static HashSet<string> StructureSignature(Dictionary<string, string> parent)
    {
        var internals = new HashSet<string>(parent.Values, StringComparer.Ordinal);
        var sig = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in internals)
            sig.Add(string.Join(",", Leaves(node, parent).OrderBy(x => x, StringComparer.Ordinal)));
        return sig;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE NESTED TESTBEDS — 2-level + 3-level corpora engineered so each level is individually compressible
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] Mammal = "dog cat wolf fox deer hare".Split(' ');
    private static readonly string[] Bird = "owl hawk crow robin finch lark".Split(' ');
    private static readonly string[] MPlace = "den burrow lair cave".Split(' ');
    private static readonly string[] BPlace = "nest branch cliff sky".Split(' ');
    private static readonly string[] Perceive = "watching gazing observing eyeing".Split(' ');

    private static Dictionary<string, string> NestedGoldFlat()
    {
        var g = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in Mammal) g[w] = "[MAMMAL]";
        foreach (var w in Bird) g[w] = "[BIRD]";
        foreach (var w in MPlace) g[w] = "[MPLACE]";
        foreach (var w in BPlace) g[w] = "[BPLACE]";
        foreach (var w in Perceive) g[w] = "[PERCEIVE]";
        return g;
    }

    /// The 2-level corpus: MAMMAL & BIRD share a LONG frame `the {NP} was {PERCEIVE} the {NP}` (rewards a superclass
    /// [NP]) but each owns a distinct frame (rewards keeping the sub-classes). Both levels individually compressible —
    /// the cleanest chance for a genuine class-of-classes tower to be MDL-optimal and form.
    private static List<string[]> Nested(int n, ulong seed, double sharedFrac = 0.55, int nFiller = 80)
    {
        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
        int Rand(int lo, int hi) => lo + (int)(U() * (hi - lo + 1));
        string Pick(string[] a) => a[(int)(U() * a.Length)];
        var np = Mammal.Concat(Bird).ToArray();
        void Fillers(List<string> into) { int c = Rand(0, 2); for (int i = 0; i < c; i++) into.Add("f" + (int)(U() * nFiller)); }
        List<string> Clause()
        {
            var c = new List<string>();
            double r = U();
            if (r < sharedFrac) { c.AddRange(["the", Pick(np), "was", Pick(Perceive), "the", Pick(np)]); }
            else if (r < sharedFrac + (1 - sharedFrac) / 2) { c.AddRange(["the", Pick(Mammal), "crept", "into", "the", Pick(MPlace)]); }
            else { c.AddRange(["the", Pick(Bird), "flew", "toward", "the", Pick(BPlace)]); }
            return c;
        }
        var corpus = new List<string[]>(n);
        for (int s = 0; s < n; s++)
        {
            var sent = new List<string>();
            int clauses = Rand(1, 2);
            for (int cl = 0; cl < clauses; cl++) { Fillers(sent); sent.AddRange(Clause()); }
            Fillers(sent);
            corpus.Add(sent.ToArray());
        }
        return corpus;
    }

    private static readonly string[] M3 = "dog cat wolf fox".Split(' ');
    private static readonly string[] B3 = "owl hawk crow finch".Split(' ');
    private static readonly string[] A3 = "ant termite aphid beetle".Split(' ');
    private static readonly string[] E3 = "bee wasp moth gnat".Split(' ');

    private static Dictionary<string, string> Nested3GoldFlat()
    {
        var g = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in M3) g[w] = "[MAMMAL]";
        foreach (var w in B3) g[w] = "[BIRD]";
        foreach (var w in A3) g[w] = "[ANT]";
        foreach (var w in E3) g[w] = "[BEE]";
        return g;
    }

    // gold 3-level structure: leaf-partitions {MAMMAL,BIRD} (VERT), {ANT,BEE} (INSECT), {all four} (CREATURE)
    private static HashSet<string> Nested3GoldStructure()
    {
        var vert = M3.Concat(B3).Select(w => "[" + (Array.IndexOf(M3, w) >= 0 ? "MAMMAL" : "BIRD") + "]");
        // compare by leaf WORDS (flat-slot-agnostic): VERT = MAMMAL∪BIRD words, INSECT = ANT∪BEE, CREATURE = all
        var sig = new HashSet<string>(StringComparer.Ordinal)
        {
            string.Join(",", M3.Concat(B3).OrderBy(x => x, StringComparer.Ordinal)),
            string.Join(",", A3.Concat(E3).OrderBy(x => x, StringComparer.Ordinal)),
            string.Join(",", M3.Concat(B3).Concat(A3).Concat(E3).OrderBy(x => x, StringComparer.Ordinal)),
        };
        return sig;
    }

    /// The 3-level corpus (deepen_multi.nested3): leaf-distinct frames, level-1 shared (within VERT / within
    /// INSECT), level-2 shared (any creature). Each level gets a frequent long shared frame so all three are
    /// individually compressible — depth is bought by shared-frame mass.
    private static List<string[]> Nested3(int n, ulong seed, double fLeaf = 0.34, double fL1 = 0.33, int nFiller = 60)
    {
        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
        int Rand(int lo, int hi) => lo + (int)(U() * (hi - lo + 1));
        string Pick(string[] a) => a[(int)(U() * a.Length)];
        var vert = M3.Concat(B3).ToArray(); var insect = A3.Concat(E3).ToArray(); var all = vert.Concat(insect).ToArray();
        void Fillers(List<string> into) { int c = Rand(0, 2); for (int i = 0; i < c; i++) into.Add("f" + (int)(U() * nFiller)); }
        List<string> Clause()
        {
            var c = new List<string>(); double r = U();
            if (r < fLeaf)
            {
                switch (Rand(0, 3))
                {
                    case 0: c.AddRange(["the", Pick(M3), "crept", "into", "the", "den"]); break;
                    case 1: c.AddRange(["the", Pick(B3), "flew", "toward", "the", "nest"]); break;
                    case 2: c.AddRange(["the", Pick(A3), "tunneled", "under", "the", "soil"]); break;
                    default: c.AddRange(["the", Pick(E3), "buzzed", "around", "the", "hive"]); break;
                }
            }
            else if (r < fLeaf + fL1)
            {
                if (U() < 0.5) c.AddRange(["the", Pick(vert), "watched", "the", Pick(vert), "closely"]);
                else c.AddRange(["the", Pick(insect), "swarmed", "over", "the", Pick(insect), "quietly"]);
            }
            else c.AddRange(["the", Pick(all), "was", "alive", "and", "so", "the", Pick(all), "slowly", "fed", "nearby"]);
            return c;
        }
        var corpus = new List<string[]>(n);
        for (int s = 0; s < n; s++)
        {
            var sent = new List<string>();
            int clauses = Rand(1, 2);
            for (int cl = 0; cl < clauses; cl++) { Fillers(sent); sent.AddRange(Clause()); }
            Fillers(sent);
            corpus.Add(sent.ToArray());
        }
        return corpus;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE KILL-LINE VERB — recovery (oracle-to-the-bit) + the multi-level tower + over-build safety
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// usage: deepen [--n N] [--theta T] [--seed HEX]
    ///   (1) 2-level: does the discovery mint [NP]={MAMMAL,BIRD} without labels? tower gain > 0 = MINT.
    ///   (2) 3-level: does it iterate to a CREATURE→{VERT,INSECT}→leaves tower? structure match vs gold.
    ///   (3) over-build: a flat corpus (leaf-distinct frames only) must mint NO tower.
    public static int Run(string[] args)
    {
        int n = Args.Int(args, "--n", 2500);
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);
        int theta = Args.Int(args, "--theta", Math.Max(8, n / 16));

        var run = Cogito.Run.New("deepen");
        Trace.Note($"deepen · context-conditional tower discovery · n={n} · theta={theta} · seed {seed:X} · no gold labels, no LLM");
        Trace.Note("  the immediate slot a token wears = the LCA of its local context's filler-classes; the tower is gated by context-sensitive ΔMDL.");
        Trace.Note("");

        // ── (1) 2-LEVEL RECOVERY ──  does the discovery mint the gold tower [NP]={MAMMAL,BIRD}?
        Trace.Note("  ── (1) 2-LEVEL RECOVERY — nested corpus (MAMMAL/BIRD share `the __ was PERCEIVE the __`) ──");
        var flat2 = NestedGoldFlat();
        var tr2 = Nested(n, seed, sharedFrac: 0.55);
        double flatTotal = DiscoveredTotal(tr2, flat2, new Dictionary<string, string>(StringComparer.Ordinal), theta);
        var towerParent = new Dictionary<string, string>(StringComparer.Ordinal) { ["[MAMMAL]"] = "[NP]", ["[BIRD]"] = "[NP]" };
        double towerTotal = DiscoveredTotal(tr2, flat2, towerParent, theta);
        double gain2 = flatTotal - towerTotal;
        Trace.Note($"    flat-sub MDL {flatTotal:F0} bits · discovered-[NP]-tower MDL {towerTotal:F0} bits · tower gain {gain2:+0} bits → {(gain2 > 1 ? "MINT" : "reject")}");

        var (parent2, log2) = GrowHierarchy(tr2, flat2, theta);
        Trace.Note($"    grow_hierarchy discovered: {HierarchyStr(parent2)}");
        bool got2 = parent2.GetValueOrDefault("[MAMMAL]") == parent2.GetValueOrDefault("[BIRD]") && parent2.ContainsKey("[MAMMAL]");
        Trace.Note($"    recovers [NP]={{MAMMAL,BIRD}} without labels : {(got2 ? "YES — oracle tower, no gold" : "no")}");
        Trace.Note("");

        // ── (2) 3-LEVEL TOWER ──  does the encoder iterate to classes-of-classes-of-classes?
        Trace.Note("  ── (2) MULTI-LEVEL TOWER — nested3 (leaf / VERT·INSECT / CREATURE, each with a shared frame) ──");
        var flat3 = Nested3GoldFlat();
        int n3 = Math.Max(n, 5000);            // the 3-level tower needs the level-2 shared-frame MASS (deepen_multi used 5000); below this the meta-merge doesn't pay
        var tr3 = Nested3(n3, seed);
        int theta3 = Math.Max(8, n3 / 16);
        var (parent3, log3) = GrowHierarchy(tr3, flat3, theta3);
        Trace.Note($"    discovered hierarchy:");
        foreach (var line in HierarchyLines(parent3)) Trace.Note("      " + line);
        var goldSig = Nested3GoldStructure();
        var discSig = StructureSignatureByWords(parent3, flat3);
        bool match3 = goldSig.SetEquals(discSig);
        Trace.Note($"    gold leaf-partitions : {string.Join(" | ", goldSig.OrderBy(x => x))}");
        Trace.Note($"    disc leaf-partitions : {string.Join(" | ", discSig.OrderBy(x => x))}");
        Trace.Note($"    STRUCTURE MATCH (same node leaf-partitions) : {(match3 ? "YES — the full tower, oracle-to-the-bit" : "partial")}");
        Trace.Note("");

        // ── (3) OVER-BUILD CONTROL ──  a flat corpus (leaf-distinct frames only) must mint NO tower
        Trace.Note("  ── (3) OVER-BUILD CONTROL — flat corpus (leaf-distinct frames ONLY, no shared super-structure) ──");
        var flatCorp = Nested3(n3, seed, fLeaf: 1.0, fL1: 0.0);
        var (parentFlat, _) = GrowHierarchy(flatCorp, flat3, theta3);
        int internalNodes = new HashSet<string>(parentFlat.Values).Count;
        Trace.Note($"    discovered internal nodes: {internalNodes} (correct = 0; the data has no super-structure)");
        Trace.Note($"    over-build-safe : {(internalNodes == 0 ? "YES — the gate rejected every spurious tower" : "NO — over-built " + internalNodes + " node(s)")}");
        Trace.Note("");

        Trace.Note("  ── VERDICT ──");
        Trace.Note($"    (1) 2-level recovery WITHOUT labels : {(got2 && gain2 > 1 ? "YES" : "partial")} (tower gain {gain2:F0} bits)");
        Trace.Note($"    (2) multi-level tower structure     : {(match3 ? "YES (oracle-to-the-bit)" : "partial")}");
        Trace.Note($"    (3) over-build-safe (no false tower): {(internalNodes == 0 ? "YES" : "no")}");
        Trace.Note(got2 && match3 && internalNodes == 0
            ? "    ⇒ DEEPEN CONFIRMED — context-conditional slot discovery recovers the true tower with no labels AND refuses to build one the data doesn't support."
            : "    ⇒ PARTIAL — the discovery recovers some structure; sweep --theta / --n (context anchoring is theta-sensitive at this corpus size).");

        run.Write("verdict.txt", $"gain2={gain2:F0} got2={got2} match3={match3} overbuild={internalNodes}\n");
        return 0;
    }

    // structure signature by LEAF WORDS (so it compares to the word-defined gold, independent of flat-slot names)
    private static HashSet<string> StructureSignatureByWords(Dictionary<string, string> parent, Dictionary<string, string> flatM2s)
    {
        // invert flat slot → member words
        var slotWords = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (w, s) in flatM2s) (slotWords.TryGetValue(s, out var l) ? l : slotWords[s] = new()).Add(w);
        var internals = new HashSet<string>(parent.Values, StringComparer.Ordinal);
        var sig = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in internals)
        {
            var words = new List<string>();
            foreach (var leafSlot in Leaves(node, parent)) if (slotWords.TryGetValue(leafSlot, out var ws)) words.AddRange(ws);
            sig.Add(string.Join(",", words.OrderBy(x => x, StringComparer.Ordinal)));
        }
        return sig;
    }

    private static string HierarchyStr(Dictionary<string, string> parent)
    {
        if (parent.Count == 0) return "(no tower)";
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (c, p) in parent) (children.TryGetValue(p, out var l) ? l : children[p] = new()).Add(c);
        return string.Join(" · ", children.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={{{string.Join(",", kv.Value.Select(Strip).OrderBy(x => x))}}}"));
    }

    private static IEnumerable<string> HierarchyLines(Dictionary<string, string> parent)
    {
        if (parent.Count == 0) { yield return "(no tower)"; yield break; }
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (c, p) in parent) (children.TryGetValue(p, out var l) ? l : children[p] = new()).Add(c);
        var roots = children.Keys.Where(p => !parent.ContainsKey(p)).OrderBy(x => x, StringComparer.Ordinal);
        foreach (var root in roots)
            foreach (var line in RenderNode(root, children, 0)) yield return line;
    }

    private static IEnumerable<string> RenderNode(string node, Dictionary<string, List<string>> children, int depth)
    {
        var kids = children.GetValueOrDefault(node);
        string line = new string(' ', depth * 2) + node + (kids is { Count: > 0 } ? " → {" + string.Join(", ", kids.Select(Strip).OrderBy(x => x)) + "}" : "");
        yield return line;
        if (kids is not null) foreach (var k in kids.OrderBy(x => x, StringComparer.Ordinal)) if (children.ContainsKey(k)) foreach (var l in RenderNode(k, children, depth + 1)) yield return l;
    }
}
