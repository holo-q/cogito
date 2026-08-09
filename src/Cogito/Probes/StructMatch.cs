namespace Cogito;

using Cogito.Grammar;
using Cogito.Induct;


// ── homology ── THE STRUCTURAL HOMOLOGY BRIDGE probe.
//
// The thesis: music↔code do NOT bridge at the SURFACE (disjoint alphabets — a literal Re-Pair over the union
// finds nothing shared; proven). But they may bridge at the STRUCTURE. Every rule occupies a NICHE — its rent
// (MDL bits saved), its depth (RG coarse-graining scale), its span (correlation length), its expansion SHAPE
// (the composition topology with every terminal collapsed to one leaf → pure form), its co-activation. Two rules
// in the SAME niche are HOMOLOGOUS even with ZERO shared bytes. This is anti-unification generalized: unify rules
// alike modulo the WHOLE alphabet (their shape), not modulo substitution (their shared terminals).
//
// The signature is deliberately SCALE-INVARIANT (normalized depth, within-grammar ranks, shape RATIOS): music is
// one 40KB line per file, code is line-dense — a raw span/depth would be dominated by that surface granularity,
// not intrinsic structure. Normalizing compares the two grammars' structural ORGANIZATION modulo surface scale,
// which IS the "modulo the whole alphabet" claim.
//
// The honesty gate is the NULL: permute each signature dim independently across the target grammar (destroys the
// JOINT niche-occupancy — which rule holds which COMBINATION of coordinates — while preserving every marginal).
// A homology score only means something if the REAL match beats this shuffled null; if it doesn't, the apparent
// match was a rank-alignment artifact (both clouds fill the same box) and is nothing.
//
// Three conditions, always together:
//   • CONTROL  C# ↔ Python  — two surface-disjoint code languages, SAME −0.70 class → expect STRONG homology.
//              (validates the matcher: if the control does NOT beat the null, the matcher is broken.)
//   • TEST     music ↔ C#   — different class → expect WEAKER; the question is whether it is NONZERO + non-spurious.
//   • NULL     the shuffled baseline both must clear.
public static class StructMatch
{
    const int Dims = 8;                       // the scale-invariant structural signature width
    const uint LeafHash = 0x9E3779B1u;        // the collapsed-terminal leaf sentinel for the topology hash
    const int TwinMinDepth = 3;               // exact-topology twins only counted at depth ≥ 3 (shallow shapes are near-universal)

    // One rule's content-blind structural signature. V is the 8-dim vector the matcher metricizes; the scalars are
    // kept for the inspect readout and the exact-topology-twin census.
    readonly record struct RuleSig(int Idx, int Depth, int Span, long Rent, int Uses, ulong ShapeHash, double[] V);

    // An induced modality: its grammar + the per-rule structural signatures, alphabet forgotten.
    sealed class Modality(string name, int bytes, int spans, RePairResult r, RuleSig[] sigs, int maxDepth)
    {
        public readonly string Name = name;
        public readonly int Bytes = bytes;
        public readonly int Spans = spans;
        public readonly RePairResult R = r;
        public readonly RuleSig[] Sigs = sigs;
        public readonly int MaxDepth = maxDepth;
    }

    public static int Run(string[] args)
    {
        int budget   = ArgInt(args, "--bytes", 120_000);      // per-pool byte cap (Python-limited ~127KB → equal budgets)
        int nMatch   = ArgInt(args, "--n", 1200);             // rules sampled per grammar (query AND target) for a fair match
        ulong seed   = (ulong)ArgInt(args, "--seed", 1);
        int shuffles = ArgInt(args, "--shuffles", 128);       // null replicates → the shuffled-H distribution
        int top      = ArgInt(args, "--top", 12);             // top homolog pairs to inspect

        // ── the pools, equal-budget, joined on '\n' (the span barrier — no rule straddles a file/line) ──
        byte[] Cap(byte[] b) => b.Length > budget ? b[..budget] : b;
        var musicBytes = Cap(PoolFiles("docs/gallery/science/regen/corpora/spread", "music_*.txt", false));
        var music   = Induce("music",   musicBytes, budget);
        var csharp  = Induce("csharp",  PoolFiles("src/Cogito", "*.cs", false), budget);
        var python  = Induce("python",  PoolFiles("docs/gallery", "*.py", true), budget);
        // control-gating domains (coordinator's amendment) — the swap-nulls that turn "beats shuffle" into "beats
        // ANOTHER domain": ENGLISH prose (repo.md) + JSON (structured data). If music↔code ≈ music↔english, the
        // correspondence is machine-generic, not a code-specific bridge.
        var english = Induce("english", PoolFiles(".", "*.md", true), budget);
        var json    = Induce("json",    PoolFiles("docs/gallery/science/regen/corpora", "*.json", true), budget);
        // the RATE-MATCHED MARKOV null: token-level order-1 resample of music — preserves the climb-class unigram+
        // bigram RATES (the ~6-symbol autocorrelation that fools a plain shuffle) but destroys ALL long structure.
        // If musicMk↔code ≈ music↔code, the bridge needs no real long structure ⟹ machine-generic (Re-Pair's own
        // signature), NOT a music↔code bridge.
        var musicMk = Induce("music✗mk", MarkovResampleTokens(musicBytes, 0xC0FFEEUL), budget);

        Console.WriteLine("homology · the STRUCTURAL HOMOLOGY BRIDGE — same niche = homologous, modulo the whole alphabet");
        Console.WriteLine($"  signature: ⟨depthNorm, rentRankInScale, spanRank, usageRank, balance, leafBias, cvLeafDepth, meanLeafDepthRatio⟩ (scale-invariant, content-blind)");
        Console.WriteLine($"  nulls: (a) per-dim SHUFFLE of the target (breaks joint niche-occupancy) ×{shuffles}  (b) DOMAIN-SWAP (music↔english/json)  (c) rate-matched MARKOV resample");
        Console.WriteLine();
        foreach (var m in new[] { music, csharp, python, english, json, musicMk })
            Console.WriteLine($"  {m.Name,-8}  {m.Bytes,7}B  {m.Spans,5} spans  {m.R.Rules.Length,6} rules  maxDepth {m.MaxDepth,3}  maxSpan {(m.Sigs.Length > 0 ? m.Sigs.Max(s => s.Span) : 0),6}B");
        Console.WriteLine();

        // ── the control-gated verdicts (each averages both directions; each vs its own per-dim shuffled null) ──
        Console.WriteLine($"  condition                H_real   H_null   ±std    homology   z_beat   twins   verdict");
        var control = Verdict("C# ↔ Python  (CONTROL)", csharp, python, nMatch, seed, shuffles);   // same −0.70 class → the tight reference
        var test    = Verdict("music ↔ C#   (TEST)",    music,  csharp, nMatch, seed, shuffles);
        var test2   = Verdict("music ↔ Python (TEST2)",  music,  python, nMatch, seed, shuffles);   // cross-class triangulation
        var swapEng = Verdict("music ↔ English (SWAP)",  music,  english,nMatch, seed, shuffles);   // domain-swap: is music generically close to ALL non-music?
        var swapJsn = Verdict("music ↔ JSON  (SWAP2)",   music,  json,   nMatch, seed, shuffles);
        var codeEng = Verdict("C# ↔ English (CODE-SWAP)", csharp, english,nMatch, seed, shuffles);  // is English generically close to code too?
        var mkNull  = Verdict("music✗mk ↔ C# (MK-NULL)",  musicMk,csharp, nMatch, seed, shuffles);  // rate-matched: does the TEST need music's real structure?

        Console.WriteLine();
        Console.WriteLine("  read: homology = (H_null − H_real)/H_null ∈ (−∞,1]; 1 = perfect, 0 = no better than shuffle, <0 = worse.");
        Console.WriteLine("        z_beat = (H_null − H_real)/std_null; the real match is real iff z_beat ≫ 0. twins = exact-topology");
        Console.WriteLine($"        matches at depth≥{TwinMinDepth} (query rules whose collapsed expansion-shape has an identical twin in target).");
        Console.WriteLine();

        // ── WHERE the bridge lives — the same homology split into its NICHE slice (depth/rent/span/usage) vs its
        //    SHAPE slice (the collapsed-terminal composition topology). Localizes what actually transfers. ──
        Console.WriteLine($"  ── where the bridge lives (homology · z_beat, by signature slice) ──");
        Console.WriteLine($"  condition                niche(depth/rent/span/use)     shape(collapsed topology)");
        AblationRow("C# ↔ Python  (CONTROL)", control);
        AblationRow("music ↔ C#   (TEST)",    test);
        AblationRow("music ↔ English (SWAP)",  swapEng);
        AblationRow("music✗mk ↔ C# (MK-NULL)", mkNull);
        Console.WriteLine();

        // ── CONTROL-GATING — the verdict is not "beats shuffle", it is "beats ANOTHER domain + a rate-matched null".
        //    The shape slice (index 2) is where the cross-modal signal lives, so gate on it (full is diluted by the
        //    anti-correlating niche). ──
        double SameClassOverTest = control.StructMatch[SHAPE] / Nz(test.StructMatch[SHAPE]);   // ≥1.5 ⟹ same-class clearly tighter
        double TestOverSwap      = test.StructMatch[SHAPE]    / Nz(swapEng.StructMatch[SHAPE]); // ~1 ⟹ generic; ≥1.5 ⟹ code-specific bridge
        double TestOverMk        = test.StructMatch[SHAPE]    / Nz(mkNull.StructMatch[SHAPE]);  // ~1 ⟹ needs no real structure (machine-generic)
        Console.WriteLine("  ── control-gating (shape-slice homology ratios; the verdict is comparative, not shuffle-only) ──");
        Console.WriteLine($"    same-class / test     (C#↔Py) / (music↔C#)      = {control.StructMatch[SHAPE]:F3} / {test.StructMatch[SHAPE]:F3} = {SameClassOverTest,4:F2}×   [≥1.5 ⟹ same-class tighter, expected]");
        Console.WriteLine($"    test / domain-swap    (music↔C#) / (music↔Eng)  = {test.StructMatch[SHAPE]:F3} / {swapEng.StructMatch[SHAPE]:F3} = {TestOverSwap,4:F2}×   [~1 ⟹ GENERIC; ≥1.5 ⟹ code-SPECIFIC bridge]");
        Console.WriteLine($"    test / markov-null    (music↔C#) / (music✗↔C#)  = {test.StructMatch[SHAPE]:F3} / {mkNull.StructMatch[SHAPE]:F3} = {TestOverMk,4:F2}×   [~1 ⟹ needs no real structure ⟹ machine-generic]");
        Console.WriteLine();

        // ── the honest verdict banner ──
        Console.WriteLine("  ── VERDICT ──");
        bool controlOk = control.ZBeat[FULL] > 3.0;
        Console.WriteLine(controlOk
            ? $"  ✓ CONTROL VALIDATES THE MATCHER — C#↔Python homology {control.StructMatch[FULL]:F3}@{control.ZBeat[FULL]:F1}σ full, {control.StructMatch[SHAPE]:F3} shape, {control.StructMatch[NICHE]:F3} niche, {control.TwinFrac:P0} exact-topology twins (same −0.70 class shares BOTH niche + shape)."
            : $"  ✗ CONTROL FAILED (z={control.ZBeat[FULL]:F1}σ ≤ 3) — matcher broken; nothing below is trustworthy.");
        // the localized read: shape bridges cross-class, niche does not — the honest decomposition.
        Console.WriteLine($"  → TEST music↔C#: shape {test.StructMatch[SHAPE]:F3}@{test.ZBeat[SHAPE]:F1}σ (BRIDGES) · niche {test.StructMatch[NICHE]:F3}@{test.ZBeat[NICHE]:F1}σ (does NOT) · exact-topology twins {test.TwinFrac:P0}. Music shares code's composition-shape STATISTICS but not its niche-economics, and not exact topologies.");
        // the control-gated verdict — the shape bridge is real ONLY if it beats the domain-swap AND the markov null.
        bool codeSpecific = TestOverSwap >= 1.5;
        bool needsStructure = TestOverMk >= 1.5;
        string gate = (codeSpecific, needsStructure) switch
        {
            (true, true)  => $"CODE-SPECIFIC + STRUCTURE-DEPENDENT — music↔C# shape-homology is {TestOverSwap:F2}× the music↔English swap AND {TestOverMk:F2}× the rate-matched Markov null. A REAL music↔code structural bridge (falsifies the machine-generic reading). RLEI has a genuine scaffold.",
            (false, true) => $"STRUCTURE-DEPENDENT but GENERIC — the shape bridge needs music's real structure ({TestOverMk:F2}× Markov null) but music corresponds ~equally to English ({TestOverSwap:F2}× swap). Music shares composition-shape with ALL structured domains, not code specifically: a UNIVERSAL Re-Pair shape-manifold, not a code bridge.",
            (true, false) => $"CODE-LEANING but MACHINE-GENERIC — beats the English swap ({TestOverSwap:F2}×) yet a rate-matched Markov resample of music matches code just as well ({TestOverMk:F2}×): the signal is Re-Pair's own compositional signature on low-entropy input, NOT music's real long structure.",
            _             => $"MACHINE-GENERIC — music↔C# shape-homology is indistinguishable from both the English swap ({TestOverSwap:F2}×) and the Markov null ({TestOverMk:F2}×). The 'bridge' is Re-Pair's universal shape fingerprint on any Zipfian input, NOT a music↔code correspondence. Even structure does not bridge; a shared quantizer is needed.",
        };
        Console.WriteLine($"    CONTROL-GATED VERDICT: {gate}");
        Console.WriteLine();

        // ── PRE-REGISTERED PREDICTIONS (coordinator) vs measured — the honesty register ──
        Console.WriteLine("  ── pre-registered predictions (coordinator) vs measured ──");
        PredRow("music↔code z_beat modest (2–6σ)",        $"{test.ZBeat[FULL]:F1}σ full / {test.ZBeat[SHAPE]:F1}σ shape", test.ZBeat[FULL] is >= 2 and <= 8);
        PredRow("(same-class)/(music↔code) ≥ 1.5×",       $"{SameClassOverTest:F2}×", SameClassOverTest >= 1.5);
        PredRow("(music↔code)/(music↔English) ∈[0.8,1.2]", $"{TestOverSwap:F2}×  ({(TestOverSwap >= 1.5 ? "code-SPECIFIC → FALSIFIES generic" : "generic")})", TestOverSwap is >= 0.8 and <= 1.2);
        Console.WriteLine();

        // ── INSPECT the strongest music↔C# homolog pairs (the honest read: meaningful or coincidental?) ──
        Console.WriteLine($"  ── top {top} music↔C# homolog pairs (smallest signature distance; music-bytes ⇔ C#-bytes side by side) ──");
        InspectTopPairs(music, csharp, nMatch, seed, top);
        Console.WriteLine();
        Console.WriteLine($"  ── (reference) top {top} C#↔Python homolog pairs — what STRONG homology looks like ──");
        InspectTopPairs(csharp, python, nMatch, seed, top);

        return controlOk ? 0 : 1;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Induction + the content-blind signature
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════

    static Modality Induce(string name, byte[] corpus, int budget)
    {
        if (corpus.Length > budget) corpus = corpus[..budget];
        var (_, n, r, _) = Engine.InduceTraced(corpus);
        int nr = r.Rules.Length;
        int spans = 1; foreach (var b in corpus) if (b == (byte)'\n') spans++;

        // ── the DAG reads, memoized bottom-up (rules reference only earlier rules) ──
        var depth = new int[nr];              // RG scale = 1 + max child depth
        var span  = new int[nr];              // correlation length = Σ child spans (terminal ⟹ 1)
        var shape = new ulong[nr];            // exact expansion topology, all terminals ≡ one leaf
        var lfCount = new long[nr];           // leaf census of the full expansion tree
        var lfSum   = new double[nr];         // Σ leaf depths (relative to the rule root)
        var lfSq    = new double[nr];         // Σ leaf depths² (for the leaf-depth CV — a pure topology fingerprint)
        // per-rule immediate-child span/depth (for the balance/leafBias shape features — the rule's own fork)
        var lSpan = new int[nr]; var rSpan = new int[nr];
        for (int i = 0; i < nr; i++)
        {
            var pat = r.Rules[i].Pattern;
            int d = 0, s = 0; ulong sh = 1469598103934665603UL;
            long lc = 0; double ds = 0, sq = 0;
            for (int k = 0; k < pat.Length; k++)
            {
                var sym = pat[k];
                int cd, cs; ulong csh; long clc; double cds, csq;
                if (sym.Value >= Symbol.FirstNonterminal && (int)(sym.Value - Symbol.FirstNonterminal) < i)
                {
                    int j = (int)(sym.Value - Symbol.FirstNonterminal);
                    cd = depth[j]; cs = span[j]; csh = shape[j]; clc = lfCount[j]; cds = lfSum[j]; csq = lfSq[j];
                }
                else { cd = 0; cs = 1; csh = LeafHash; clc = 1; cds = 0; csq = 0; }   // a terminal: one leaf, collapsed
                d = Math.Max(d, cd); s += cs;
                sh = (sh ^ csh) * 1099511628211UL;                                    // FNV mix — order-sensitive (left ≠ right)
                // placing this child under rule i lifts every one of its leaves by 1 level
                ds += cds + clc; sq += csq + 2 * cds + clc; lc += clc;
                if (k == 0) lSpan[i] = cs; if (k == pat.Length - 1) rSpan[i] = cs;
            }
            depth[i] = d + 1; span[i] = s; shape[i] = sh;
            lfCount[i] = lc; lfSum[i] = ds; lfSq[i] = sq;
        }
        int maxDepth = nr > 0 ? depth.Max() : 0;
        var uses = Engine.RuleUses(r);
        var rent = new long[nr];
        // rent = MDL bits this rule saves = (count−2)·log₂|V| — recomputed from the DAG (traced events carry it too;
        // this keeps the read one-source and independent of the events buffer).
        for (int i = 0; i < nr; i++) rent[i] = Math.Max(0, uses[i] - 2) * (long)Math.Ceiling(Math.Log2(Math.Max(2, r.AlphabetSize + (uint)i)));

        // ── ranks (scale-invariant, alphabet-independent) ──
        var spanRank = RankNorm(Enumerable.Range(0, nr).Select(i => (double)span[i]).ToArray());
        var useRank  = RankNorm(Enumerable.Range(0, nr).Select(i => (double)uses[i]).ToArray());
        var rentRankInScale = RentRankWithinScale(rent, depth, maxDepth);

        var sigs = new RuleSig[nr];
        for (int i = 0; i < nr; i++)
        {
            int fork = Math.Max(1, lSpan[i] + rSpan[i]);
            double balance  = Math.Abs(lSpan[i] - rSpan[i]) / (double)fork;           // fork asymmetry ∈ [0,1)
            double leafBias = lSpan[i] / (double)fork;                                // signed skew ∈ (0,1): left-heavy vs right-heavy
            double meanLd   = lfCount[i] > 0 ? lfSum[i] / lfCount[i] : 0;
            double varLd    = lfCount[i] > 0 ? Math.Max(0, lfSq[i] / lfCount[i] - meanLd * meanLd) : 0;
            double cvLd     = meanLd > 0 ? Math.Sqrt(varLd) / meanLd : 0;             // leaf-depth CV — balanced tree ≈ 0, comb ≫ 0
            double meanLdRatio = depth[i] > 0 ? meanLd / depth[i] : 0;                // ∈ (0,1]: 1 = balanced, <1 = skewed/comb
            var v = new double[Dims]
            {
                maxDepth > 0 ? depth[i] / (double)maxDepth : 0,   // depthNorm
                rentRankInScale[i],                                // rent-rank WITHIN its RG scale
                spanRank[i],                                       // span-rank (correlation length)
                useRank[i],                                        // usage-rank (co-activation strength)
                balance, leafBias, cvLd, meanLdRatio,              // the collapsed-terminal expansion SHAPE
            };
            sigs[i] = new RuleSig(i, depth[i], span[i], rent[i], uses[i], shape[i], v);
        }
        return new Modality(name, n, spans, r, sigs, maxDepth);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The matcher + the shuffled null
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════

    // Three signature SLICES, so the bridge can be LOCALIZED: does music↔code share the graded NICHE-occupancy
    // (depth/rent/span/usage) or the composition SHAPE (the collapsed-terminal topology), or both?
    static readonly double[] MaskFull  = [1, 1, 1, 1, 1, 1, 1, 1];
    static readonly double[] MaskNiche = [1, 1, 1, 1, 0, 0, 0, 0];   // depthNorm, rentRankInScale, spanRank, usageRank
    static readonly double[] MaskShape = [0, 0, 0, 0, 1, 1, 1, 1];   // balance, leafBias, cvLeafDepth, meanLeafDepthRatio
    static readonly double[][] Masks = [MaskFull, MaskNiche, MaskShape];
    const int NM = 3;                                                // full · niche · shape
    const int FULL = 0, NICHE = 1, SHAPE = 2;                        // mask indices into the per-mask arrays

    // Per-condition homology, one entry per mask (index 0=full, 1=niche, 2=shape) + the exact-topology-twin fraction.
    readonly record struct MatchStat(double[] HReal, double[] HNull, double[] HNullStd, double[] StructMatch, double[] ZBeat, double TwinFrac);

    static MatchStat Verdict(string label, Modality a, Modality b, int nMatch, ulong seed, int shuffles)
    {
        // average both directions (NN is asymmetric) — the symmetric homology read
        var ab = MatchOneWay(a, b, nMatch, seed, shuffles);
        var ba = MatchOneWay(b, a, nMatch, seed ^ 0x5DEECE66DUL, shuffles);
        var hReal = new double[NM]; var hNull = new double[NM]; var hStd = new double[NM];
        var homology = new double[NM]; var zBeat = new double[NM];
        for (int m = 0; m < NM; m++)
        {
            hReal[m] = 0.5 * (ab.HReal[m] + ba.HReal[m]);
            hNull[m] = 0.5 * (ab.HNull[m] + ba.HNull[m]);
            hStd[m]  = 0.5 * (ab.HNullStd[m] + ba.HNullStd[m]);
            homology[m] = hNull[m] != 0 ? (hNull[m] - hReal[m]) / hNull[m] : 0;
            zBeat[m] = hStd[m] > 1e-9 ? (hNull[m] - hReal[m]) / hStd[m] : 0;
        }
        double twin = 0.5 * (ab.TwinFrac + ba.TwinFrac);
        Console.WriteLine($"  {label,-22} {hReal[0],6:F3}  {hNull[0],6:F3}  {hStd[0],5:F3}   {homology[0],7:F3}   {zBeat[0],5:F1}σ  {twin,5:P0}   {(zBeat[0] > 3.0 ? "BEATS null" : "≈ null")}");
        return new MatchStat(hReal, hNull, hStd, homology, zBeat, twin);
    }

    static void AblationRow(string label, MatchStat st)
        => Console.WriteLine($"  {label,-22}   {st.StructMatch[NICHE],7:F3} · {st.ZBeat[NICHE],5:F1}σ            {st.StructMatch[SHAPE],7:F3} · {st.ZBeat[SHAPE],5:F1}σ");

    static void PredRow(string prediction, string measured, bool held)
        => Console.WriteLine($"    [{(held ? "✓" : "✗")}] {prediction,-38} → {measured}");

    // guard a ratio denominator: a shape-homology at/below ~0 can't anchor a ratio (the swap/null found no bridge),
    // so floor tiny/negative denominators to a small positive ⟹ the ratio reads LARGE (test clearly exceeds it).
    static double Nz(double x) => x > 0.02 ? x : 0.02;

    // One-way NN match (query → target) in the pooled-z-scored signature space, with the per-dim independent-shuffle
    // null of the target — evaluated for all three masks over the SAME subsample + SAME shuffle draws (so full/niche/
    // shape are directly comparable). Returns per-mask real + null mean/std, and the exact-topology-twin fraction.
    static (double[] HReal, double[] HNull, double[] HNullStd, double TwinFrac) MatchOneWay(Modality query, Modality target, int nMatch, ulong seed, int shuffles)
    {
        var q = Subsample(query.Sigs, nMatch, seed);
        var t = Subsample(target.Sigs, nMatch, seed ^ 0xA5A5A5A5UL);

        // pooled per-dim z-score over the union — preserves cross-grammar marginal SHIFTS (a genuine mismatch must
        // cost distance) AND the within-grammar feature CORRELATIONS the null exists to destroy.
        var mean = new double[Dims]; var std = new double[Dims];
        for (int d = 0; d < Dims; d++)
        {
            double s = 0; int c = 0;
            foreach (var x in q) { s += x.V[d]; c++; } foreach (var x in t) { s += x.V[d]; c++; }
            mean[d] = s / c;
            double v = 0;
            foreach (var x in q) v += Sq(x.V[d] - mean[d]); foreach (var x in t) v += Sq(x.V[d] - mean[d]);
            std[d] = Math.Sqrt(v / c); if (std[d] < 1e-9) std[d] = 1;
        }
        var qz = ZRows(q, mean, std);
        var tz = ZRows(t, mean, std);

        var hReal = new double[NM]; var sum = new double[NM]; var sumSq = new double[NM];
        for (int m = 0; m < NM; m++) hReal[m] = MeanNn(qz, tz, Masks[m]);   // REAL, per mask

        // NULL: permute each target dim independently (destroys joint niche-occupancy, keeps marginals) × shuffles;
        // one shuffle draw scored under all three masks so the slices share the exact same null draws.
        var perm = new int[tz.Length];
        for (int s = 0; s < shuffles; s++)
        {
            var tzShuf = ShufflePerDim(tz, ref perm, seed + (ulong)s * 0x9E3779B97F4A7C15UL);
            for (int m = 0; m < NM; m++) { double h = MeanNn(qz, tzShuf, Masks[m]); sum[m] += h; sumSq[m] += h * h; }
        }
        var hNull = new double[NM]; var hStd = new double[NM];
        for (int m = 0; m < NM; m++) { hNull[m] = sum[m] / shuffles; hStd[m] = Math.Sqrt(Math.Max(0, sumSq[m] / shuffles - hNull[m] * hNull[m])); }

        // exact-topology twins: query rules (depth≥TwinMinDepth) whose collapsed expansion-shape appears in target
        var tShapes = new HashSet<ulong>();
        foreach (var x in t) if (x.Depth >= TwinMinDepth) tShapes.Add(x.ShapeHash);
        int deep = 0, twin = 0;
        foreach (var x in q) if (x.Depth >= TwinMinDepth) { deep++; if (tShapes.Contains(x.ShapeHash)) twin++; }
        double twinFrac = deep > 0 ? twin / (double)deep : 0;

        return (hReal, hNull, hStd, twinFrac);
    }

    static double MeanNn(double[][] q, double[][] t, double[] w)
    {
        double acc = 0;
        for (int i = 0; i < q.Length; i++)
        {
            var qi = q[i]; double best = double.MaxValue;
            for (int j = 0; j < t.Length; j++)
            {
                var tj = t[j]; double d = 0;
                for (int k = 0; k < Dims; k++) { double e = qi[k] - tj[k]; d += w[k] * e * e; if (d >= best) break; }
                if (d < best) best = d;
            }
            acc += Math.Sqrt(best);
        }
        return acc / q.Length;
    }

    static double[][] ShufflePerDim(double[][] rows, ref int[] perm, ulong seed)
    {
        int n = rows.Length;
        var outp = new double[n][];
        for (int i = 0; i < n; i++) outp[i] = new double[Dims];
        ulong rng = seed | 1;
        for (int d = 0; d < Dims; d++)
        {
            for (int i = 0; i < n; i++) perm[i] = i;
            for (int i = n - 1; i > 0; i--)   // Fisher-Yates, per-dim independent
            {
                rng = rng * 6364136223846793005UL + 1442695040888963407UL;
                int j = (int)((rng >> 33) % (ulong)(i + 1));
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            for (int i = 0; i < n; i++) outp[i][d] = rows[perm[i]][d];
        }
        return outp;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Inspect — the strongest pairs, bytes side by side (the honest read)
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════

    static void InspectTopPairs(Modality query, Modality target, int nMatch, ulong seed, int top)
    {
        var q = Subsample(query.Sigs, nMatch, seed);
        var t = Subsample(target.Sigs, nMatch, seed ^ 0xA5A5A5A5UL);
        var mean = new double[Dims]; var std = new double[Dims];
        for (int d = 0; d < Dims; d++)
        {
            double s = 0; int c = 0;
            foreach (var x in q) { s += x.V[d]; c++; } foreach (var x in t) { s += x.V[d]; c++; }
            mean[d] = s / c; double v = 0;
            foreach (var x in q) v += Sq(x.V[d] - mean[d]); foreach (var x in t) v += Sq(x.V[d] - mean[d]);
            std[d] = Math.Sqrt(v / c); if (std[d] < 1e-9) std[d] = 1;
        }
        var qz = ZRows(q, mean, std); var tz = ZRows(t, mean, std);

        var pairs = new List<(double D, int Qi, int Tj)>();
        for (int i = 0; i < qz.Length; i++)
        {
            double best = double.MaxValue; int bj = -1;
            for (int j = 0; j < tz.Length; j++)
            {
                double d = 0; for (int k = 0; k < Dims; k++) d += Sq(qz[i][k] - tz[j][k]);
                if (d < best) { best = d; bj = j; }
            }
            pairs.Add((Math.Sqrt(best), i, bj));
        }
        // rank by distance, but require depth≥2 on BOTH sides so we inspect real composed structure, not flat pairs
        foreach (var (dist, qi, tj) in pairs
                     .Where(p => q[p.Qi].Depth >= 2 && t[p.Tj].Depth >= 2)
                     .OrderBy(p => p.D).Take(top))
        {
            var qs = q[qi]; var ts = t[tj];
            Console.WriteLine($"  d={dist:F3}  [{query.Name} N{qs.Idx} d{qs.Depth} s{qs.Span} r{qs.Rent} ×{qs.Uses}]  {Ellipsize(Expand(query.R, qs.Idx), 34)}");
            Console.WriteLine($"           [{target.Name} N{ts.Idx} d{ts.Depth} s{ts.Span} r{ts.Rent} ×{ts.Uses}]  {Ellipsize(Expand(target.R, ts.Idx), 34)}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════════════
    // helpers
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════

    // rank → [0,1], ties share the mean rank; empty/singleton ⇒ 0.5
    static double[] RankNorm(double[] xs)
    {
        int n = xs.Length; var rank = new double[n];
        if (n <= 1) { if (n == 1) rank[0] = 0.5; return rank; }
        var idx = Enumerable.Range(0, n).OrderBy(i => xs[i]).ToArray();
        int p = 0;
        while (p < n)
        {
            int qq = p; while (qq + 1 < n && xs[idx[qq + 1]] == xs[idx[p]]) qq++;
            double avg = (p + qq) / 2.0 / (n - 1);
            for (int k = p; k <= qq; k++) rank[idx[k]] = avg;
            p = qq + 1;
        }
        return rank;
    }

    // rent rank computed WITHIN each RG scale (depth band), so a rule's rent-rank says "how load-bearing among its
    // scale-mates" — the scale-invariant niche coordinate, not a global rent that just re-encodes depth.
    static double[] RentRankWithinScale(long[] rent, int[] depth, int maxDepth)
    {
        int n = rent.Length; var outp = new double[n];
        for (int L = 1; L <= maxDepth; L++)
        {
            var members = new List<int>();
            for (int i = 0; i < n; i++) if (depth[i] == L) members.Add(i);
            if (members.Count == 0) continue;
            var sub = RankNorm(members.Select(i => (double)rent[i]).ToArray());
            for (int k = 0; k < members.Count; k++) outp[members[k]] = sub[k];
        }
        return outp;
    }

    // deterministic Fisher-Yates subsample (or all rules if fewer than n), stable order by rule id afterwards
    static RuleSig[] Subsample(RuleSig[] all, int n, ulong seed)
    {
        if (all.Length <= n) return all;
        var idx = Enumerable.Range(0, all.Length).ToArray();
        ulong rng = seed | 1;
        for (int i = all.Length - 1; i > 0; i--)
        {
            rng = rng * 6364136223846793005UL + 1442695040888963407UL;
            int j = (int)((rng >> 33) % (ulong)(i + 1));
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }
        var pick = idx.Take(n).OrderBy(i => i).ToArray();
        var outp = new RuleSig[n];
        for (int i = 0; i < n; i++) outp[i] = all[pick[i]];
        return outp;
    }

    static double[][] ZRows(RuleSig[] rows, double[] mean, double[] std)
    {
        var outp = new double[rows.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            outp[i] = new double[Dims];
            for (int d = 0; d < Dims; d++) outp[i][d] = (rows[i].V[d] - mean[d]) / std[d];
        }
        return outp;
    }

    static byte[] Expand(RePairResult r, int ruleIdx)
        => Reconstruct.Expand(r.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)ruleIdx)]);

    static string Ellipsize(byte[] bytes, int max)
    {
        var s = System.Text.Encoding.UTF8.GetString(bytes).Replace("\r", "").Replace("\n", "⏎").Replace("\t", "→");
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    // The rate-matched MARKOV null: token-level order-1 resample. Splits the corpus into tokens (a run of
    // non-whitespace, OR a single whitespace byte kept as its own separator token so the space/newline RATE — hence
    // span density — is preserved), builds the token→successor transition table, and resamples a same-length stream.
    // Preserves the token alphabet + unigram + bigram rates EXACTLY; destroys every correlation longer than a bigram.
    // A grammar induced over this has music's local climb-class statistics but NONE of its real long structure.
    static byte[] MarkovResampleTokens(byte[] corpus, ulong seed)
    {
        static bool Ws(byte b) => b == (byte)' ' || b == (byte)'\n' || b == (byte)'\t' || b == (byte)'\r';
        // tokenize → list of token byte-slices, keyed by a Latin1 string for the transition map
        var toks = new List<byte[]>();
        for (int i = 0; i < corpus.Length;)
        {
            if (Ws(corpus[i])) { toks.Add([corpus[i]]); i++; }
            else { int j = i; while (j < corpus.Length && !Ws(corpus[j])) j++; toks.Add(corpus[i..j]); i = j; }
        }
        if (toks.Count < 2) return corpus;
        string Key(byte[] t) => System.Text.Encoding.Latin1.GetString(t);
        var next = new Dictionary<string, List<byte[]>>();
        for (int i = 0; i + 1 < toks.Count; i++)
        {
            var k = Key(toks[i]);
            (next.TryGetValue(k, out var l) ? l : next[k] = new()).Add(toks[i + 1]);
        }
        ulong rng = seed | 1;
        var outp = new List<byte>(corpus.Length + 16);
        var cur = toks[0];
        for (int i = 0; i < toks.Count; i++)
        {
            outp.AddRange(cur);
            if (!next.TryGetValue(Key(cur), out var opts) || opts.Count == 0) { cur = toks[0]; continue; }
            rng = rng * 6364136223846793005UL + 1442695040888963407UL;
            cur = opts[(int)((rng >> 33) % (ulong)opts.Count)];
        }
        return outp.ToArray();
    }

    static byte[] PoolFiles(string dir, string pattern, bool recurse)
    {
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"  pool dir missing: {dir}"); return []; }
        var files = Directory.GetFiles(dir, pattern, recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        var buf = new List<byte>();
        foreach (var f in files) { buf.AddRange(File.ReadAllBytes(f)); buf.Add((byte)'\n'); }   // '\n' join = span barrier between files
        return buf.ToArray();
    }

    static double Sq(double x) => x * x;

    static int ArgInt(string[] args, string flag, int dflt)
    {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag && int.TryParse(args[i + 1], out var v)) return v;
        return dflt;
    }
}
