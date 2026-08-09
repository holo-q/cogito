namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Codec;    // Fixed.Log2 — the integer milli-bit log the concave differential rides
using Cogito.Grammar;
using Cogito.Induct;

// ── THE QUADRANT ASSAY (H2″) — BREADTH-NORMALIZED ──
// READ-ONLY science on a BANKED converged mesh checkpoint. The self-play home of the SHARPNESS axis. As first banked
// (2766b0c), sharpness was Edge = S(home)−S(complement) over per-source counts — and it FAILED orthogonality
// (corr(breadth,edge) = −0.84 global, −0.63 broad) because it is BREADTH-COUPLED BY CONSTRUCTION: with per-source
// counts c₁≥…≥c_k, complement ≥ k−1, so Edge ≤ S(home) − log₂(breadth); at breadth 1 the complement is identically 0
// and Edge = S(total) is pinned to VOLUME. A narrow rule literally CANNOT be flat → the support is a triangle, and
// corr≈0 could not distinguish real orthogonality from mechanical coupling. Edge was ¬breadth in disguise.
//
// THE FIX (monk-sharpness): the sharpness axis is a BREADTH-NORMALIZED evenness over the reflecting sources —
//   EVENNESS = 1 − H_norm, H_norm = H(shares)/log₂(k), H the Shannon entropy of the per-source SHARE distribution.
// Peakedness DEFINED IDENTICALLY at breadth 2 and 4 (concentration independent of source count), so free to be
// orthogonal to breadth. Defined for k ≥ 2 ONLY; **k = 1 rules ARE the MEMORY species by definition** (a single
// source has no distribution to be even/uneven over — membership by arithmetic, not by threshold).
//
// THE COUNT-SEMANTICS TRAP (resolved): the landed `JewelCounts` (Pearl 5562786) is the DAG-UNION count — the reverse-
// DAG pass (Pearl.cs) pours each parent's per-source distribution into its children, so a child's apparent
// concentration is contaminated by its ancestors'. Evenness therefore reads `JewelCountsDirect` (the SAME co-walk
// count WITHOUT the reverse-DAG union — a rule's OWN top-level re-derivations). BREADTH k is likewise the DIRECT
// source count |JewelCountsDirect| for the de-confounded plane; the union numbers are reported alongside as the
// contaminated comparison so the confound is legible, never silent.
//
//                     sharp (concentrated, evenness ≥ τ)   flat (spread, evenness < τ)
//   broad (k_dir ≥ 2)  GOLD  abstraction with a home        GLUE  boilerplate (true but valueless)
//   narrow (k_dir = 1) MEMORY (by definition)               — (noise structurally empty: k=1 ⇒ memory)
//
// FOUR PRE-REGISTRATIONS (each PASS/FAIL, a FAIL is a real result): (1) ORTHOGONALITY — corr(breadth, evenness) over
// k≥2 falls toward ~0 (decisive: does a real second axis exist, or does the sharpness organ move to R″/Whorl-C?).
// (2) GOLD STABILITY — the 2.2% (CancellationToken, AsyncEnumerable, <TSource) survives as broad+uneven; the SELECTION
// RECEIPT (top-N by raw Edge vs top-N by evenness) makes the −0.85 legible. (3) DEPTH, DE-CONFOUNDED — memory-at-depth
// under the normalized measure: PERSISTS ⇒ a genuinely new depth readout, FLATTENS ⇒ an echo of the depth autopsy.
// (4) GLUE/GOLD BOUNDARY — the gold cell's evenness DISTRIBUTION (the adjudicator between abstraction and idiom).
//
// Mints nothing, changes no engine behaviour; rides the byte-identity-gated 5b instrumentation.
public static class QuadrantAssay
{
    /// S(k) = log₂(1+k) in milli-bits — the concave saving primitive the home-vs-complement raw Edge rides (kept only
    /// for the selection-readout contrast; the sharpness axis is EVENNESS, below).
    static long S(long k) => Fixed.Log2((uint)(k + 1)).Value;

    enum Species { Gold, Glue, Memory, Unexercised }

    /// One rule's cell. DIRECT (de-confounded) axes are primary; UNION breadth + raw Edge are carried for the
    /// contaminated-comparison reads (the trap made legible). `Evenness` = 1−H_norm over the DIRECT counts (NaN when
    /// k_dir < 2). Species: k_dir 0 → unexercised (no direct re-derivation), k_dir 1 → MEMORY (by definition),
    /// k_dir ≥ 2 → gold (evenness ≥ τ) / glue (evenness < τ).
    readonly record struct Cell(int Idx, int Depth, long Span, long ExpLen, int BreadthDirect, int BreadthUnion,
        long TotalDirect, long HomeDirect, long RawEdgeUnion, double Evenness, string HomeSrc, bool Sharp)
    {
        public Species Kind => BreadthDirect == 0 ? Species.Unexercised
                             : BreadthDirect == 1 ? Species.Memory
                             : Sharp ? Species.Gold : Species.Glue;
        public bool Broad => BreadthDirect >= 2;
    }

    /// EVENNESS — the breadth-NORMALIZED sharpness: 1 − H_norm, H_norm the Shannon entropy of the per-source SHARES
    /// (present sources only) over log₂(k). Reads the PEAKEDNESS of the distribution among the sources a rule spans,
    /// DEFINED IDENTICALLY at breadth 2 and 4 — free to be orthogonal to breadth. 0 = perfectly spread, 1 = all mass
    /// in one source. k < 2 → NaN (a single source has no shape — memory by definition, not trivially concentrated).
    static double Evenness(Dictionary<string, long>? counts)
    {
        if (counts is null || counts.Count < 2) return double.NaN;
        long tot = 0; foreach (var c in counts.Values) tot += c;
        if (tot <= 0) return double.NaN;
        double h = 0;
        foreach (var c in counts.Values) { if (c <= 0) continue; double p = (double)c / tot; h -= p * Math.Log2(p); }
        double hMax = Math.Log2(counts.Count);
        return hMax > 0 ? 1.0 - h / hMax : 0.0;
    }

    /// home-vs-complement raw Edge over a count map — S(home)−S(complement), S(k)=log₂(1+k). The ORIGINAL coupled
    /// measure (2766b0c). Returned only to drive the selection readout (top-by-rawEdge should surface narrow memories).
    static (long Total, long Home, string HomeSrc, long Edge) RawEdge(Dictionary<string, long>? counts)
    {
        long total = 0, home = 0; string homeSrc = "none";
        if (counts is { Count: > 0 })
            foreach (var (src, c) in counts)
            {
                total += c;
                if (c > home || (c == home && string.CompareOrdinal(src, homeSrc) < 0)) { home = c; homeSrc = src; }
            }
        return (total, home, homeSrc, S(home) - S(total - home));
    }

    public static int Run(string runDir, string outDir)
    {
        if (!File.Exists(Path.Combine(runDir, MeshCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"quadrant: no mesh checkpoint at {runDir}/{MeshCheckpoint.FileName} — need a banked converged mesh (mesh run with CheckpointEvery > 0, CrossReflect on)");
            return 1;
        }

        Console.WriteLine("quadrant assay (H2″) — BREADTH-NORMALIZED evenness — read-only, dead data");
        Console.WriteLine($"  checkpoint  {runDir}");

        var peek = MeshCheckpoint.PeekGrammarAndTape(runDir);
        using var tape = peek.Tape;
        var g = peek.Grammar;
        int wScale = peek.WScale;
        int n = g.Rules.Length;
        Console.WriteLine($"  grammar     {n} rules · alphabet {g.AlphabetSize} · wScale {wScale} · cross-reflect {peek.CrossReflect}");

        if (!peek.CrossReflect)
            Console.WriteLine("  ⚠ this run had cross-reflect OFF — JewelSources/JewelCounts would be null; forcing crossReflect ON for the audit (the source census is the assay's substrate)");

        // cross-reflect ON — the source-independence gate the assay reads breadth+edge off, regardless of the run's own gate
        var audit = Pearl.Audit(tape, g, wScale, crossReflect: true);
        var (depth, span) = Engine.RuleDepthSpan(g);
        var expLen = audit.ExpLen;

        // ── the source census (the guard: how COARSE is the breadth axis?) ──
        var allSources = new SortedSet<string>(StringComparer.Ordinal);
        if (audit.JewelSources is { } js) foreach (var s in js) if (s is not null) foreach (var src in s) allSources.Add(src);
        Console.WriteLine($"  sources     {allSources.Count} distinct: {string.Join(", ", allSources)}  (breadth ∈ [0,{allSources.Count}])");
        Console.WriteLine();
        Console.WriteLine("  ── COUNT SEMANTICS (the trap, resolved) ──");
        Console.WriteLine("  JewelCounts (5562786) is DAG-UNION — the reverse-DAG pass pours each parent's per-source distribution into its children (Pearl.cs), so a child's");
        Console.WriteLine("  apparent concentration borrows its ancestors'. Evenness reads JewelCountsDirect (co-walk only, a rule's OWN top-level re-derivations); breadth k");
        Console.WriteLine("  is the DIRECT source count |JewelCountsDirect|. The UNION numbers are reported alongside as the contaminated comparison.");

        // ── per-rule cells over the ELIGIBLE population (expLen ≥ ReflectFloorBytes — the reflection-capable rules) ──
        int nElig = 0, nDirectUnexercised = 0;
        var raw = new List<(int Idx, int Depth, long Span, long ExpLen, int BrDir, int BrUnion, long TotDir, long HomeDir, long RawEdgeU, double EvenDir, double EvenUnion, string HomeSrc)>();
        for (int r = 0; r < n; r++)
        {
            if (expLen[r] < Pearl.ReflectFloorBytes) continue;
            nElig++;
            var direct = audit.JewelCountsDirect?[r];
            var union = audit.JewelCounts?[r];
            int brDir = direct?.Count ?? 0;
            int brUnion = audit.JewelSources?[r]?.Count ?? 0;
            if (brDir == 0) nDirectUnexercised++;
            var (totDir, homeDir, homeSrc, _) = RawEdge(direct);
            var (_, _, _, rawEdgeU) = RawEdge(union);
            raw.Add((r, depth[r], span[r], expLen[r], brDir, brUnion, totDir, homeDir, rawEdgeU,
                Evenness(direct), Evenness(union), homeSrc));
        }

        if (raw.Count == 0) { Console.Error.WriteLine("  no eligible rules (expLen ≥ 8B) — grammar too shallow for the assay"); return 1; }

        // ── τ_even = MEDIAN evenness over the BROAD (k_dir ≥ 2) population — a balanced, reproducible, τ-transparent
        //    split; the orthogonality corr below is τ-FREE, so the verdict does not hinge on this threshold. ──
        var broadEven = raw.Where(x => x.BrDir >= 2 && !double.IsNaN(x.EvenDir)).Select(x => x.EvenDir).OrderBy(v => v).ToArray();
        double tauEven = broadEven.Length > 0 ? broadEven[broadEven.Length / 2] : 0.5;

        var cells = new List<Cell>(raw.Count);
        foreach (var x in raw)
            cells.Add(new Cell(x.Idx, x.Depth, x.Span, x.ExpLen, x.BrDir, x.BrUnion, x.TotDir, x.HomeDir, x.RawEdgeU, x.EvenDir, x.HomeSrc,
                Sharp: !double.IsNaN(x.EvenDir) && x.EvenDir >= tauEven));

        // ── (1) ORTHOGONALITY — the decisive number. DIRECT corr(k_dir, evenness) over k_dir ≥ 2. The evenness is
        //    breadth-normalized (÷ log₂ k), so a residual correlation is a REAL coupling, not the mechanical triangle.
        //    Reported beside: the contaminated-union corr (reproduces ce5e942's −0.339) and the raw home-vs-complement
        //    edge coupling (the −0.84/−0.63 this de-confounds). ──
        var broadCells = cells.Where(c => c.Broad).ToList();
        double corrDirect = broadCells.Count >= 3 ? Pearson(broadCells.Select(c => (double)c.BreadthDirect), broadCells.Select(c => c.Evenness)) : double.NaN;
        // union comparison: corr over UNION-breadth ≥ 2 population, evenness computed over UNION counts (the ce5e942 read)
        var unionBroad = raw.Where(x => x.BrUnion >= 2 && !double.IsNaN(x.EvenUnion)).ToList();
        double corrUnion = unionBroad.Count >= 3 ? Pearson(unionBroad.Select(x => (double)x.BrUnion), unionBroad.Select(x => x.EvenUnion)) : double.NaN;
        // the raw home-vs-complement edge coupling — global + broad (the triangle the de-confounding removes)
        double corrRawGlobal = Pearson(cells.Select(c => (double)c.BreadthUnion), cells.Select(c => (double)c.RawEdgeUnion));
        double corrRawBroad = cells.Count(c => c.BreadthUnion >= 2) >= 3
            ? Pearson(cells.Where(c => c.BreadthUnion >= 2).Select(c => (double)c.BreadthUnion), cells.Where(c => c.BreadthUnion >= 2).Select(c => (double)c.RawEdgeUnion))
            : double.NaN;
        // mean evenness by direct breadth — the coupling made legible (the analogue of mean-edge-by-breadth)
        var evenByK = new List<(int K, int N, double MeanEven)>();
        for (int k = 2; k <= allSources.Count; k++)
        {
            var kc = broadCells.Where(c => c.BreadthDirect == k).ToList();
            if (kc.Count > 0) evenByK.Add((k, kc.Count, kc.Average(c => c.Evenness)));
        }

        // ── the 2×2 populations (direct plane) ──
        int gold = cells.Count(c => c.Kind == Species.Gold);
        int glue = cells.Count(c => c.Kind == Species.Glue);
        int mem  = cells.Count(c => c.Kind == Species.Memory);
        int unexer = cells.Count(c => c.Kind == Species.Unexercised);
        int total2 = cells.Count;

        // ── (2) GOLD STABILITY + SELECTION RECEIPT — top-N by raw Edge (should be narrow memories) vs top-N by
        //    evenness among broad (should be genuine abstractions). The named gold survivors are searched by expansion. ──
        var topByRawEdge = cells.OrderByDescending(c => c.RawEdgeUnion).ThenByDescending(c => c.ExpLen).Take(20).ToList();
        var topByEvenness = broadCells.OrderByDescending(c => c.Evenness).ThenByDescending(c => c.BreadthDirect).ThenByDescending(c => c.ExpLen).Take(20).ToList();
        // the banked gold vocabulary — do they survive as broad (k_dir ≥ 2) + uneven (gold)?
        string[] goldMarkers = { "CancellationToken", "AsyncEnumerable", "TSource", "inputField", "IEnumerable", "await ", "public " };
        var goldSurvival = new List<(string Marker, int Idx, int KDir, int KUnion, double Even, Species Kind)>();
        foreach (var m in goldMarkers)
        {
            var hit = cells.Where(c => Latin1(Expand(g, c.Idx)).Contains(m, StringComparison.Ordinal))
                           .OrderByDescending(c => c.BreadthDirect).ThenByDescending(c => c.Evenness).FirstOrDefault();
            if (hit.ExpLen > 0) goldSurvival.Add((m, hit.Idx, hit.BreadthDirect, hit.BreadthUnion, hit.Evenness, hit.Kind));
        }

        // ── (3) DEPTH, DE-CONFOUNDED — species mix by depth on the DIRECT plane; memory share shallow vs deep. ──
        int maxDepth = cells.Max(c => c.Depth);
        var byDepth = new List<(int D, int N, int G, int Gl, int M, int Un, double MeanKDir, double MeanKUnion)>();
        for (int d = 1; d <= maxDepth; d++)
        {
            var dc = cells.Where(c => c.Depth == d).ToList();
            if (dc.Count == 0) continue;
            byDepth.Add((d, dc.Count, dc.Count(c => c.Kind == Species.Gold), dc.Count(c => c.Kind == Species.Glue),
                dc.Count(c => c.Kind == Species.Memory), dc.Count(c => c.Kind == Species.Unexercised),
                dc.Average(c => (double)c.BreadthDirect), dc.Average(c => (double)c.BreadthUnion)));
        }
        int deepFloor = Math.Max(4, (int)Math.Ceiling(maxDepth * 2.0 / 3));
        (int m, int u, int n) shallow = (0, 0, 0), deep = (0, 0, 0);   // m=memory u=unexercised — the two competing depth classes
        foreach (var c in cells)
        {
            if (c.Depth <= 3) { shallow.n++; if (c.Kind == Species.Memory) shallow.m++; if (c.Kind == Species.Unexercised) shallow.u++; }
            if (c.Depth >= deepFloor) { deep.n++; if (c.Kind == Species.Memory) deep.m++; if (c.Kind == Species.Unexercised) deep.u++; }
        }
        double shallowMem = shallow.n > 0 ? 100.0 * shallow.m / shallow.n : 0;
        double deepMem = deep.n > 0 ? 100.0 * deep.m / deep.n : 0;
        double shallowUnexer = shallow.n > 0 ? 100.0 * shallow.u / shallow.n : 0;
        double deepUnexer = deep.n > 0 ? 100.0 * deep.u / deep.n : 0;

        // ── (4) GLUE/GOLD BOUNDARY — the gold vs glue evenness DISTRIBUTIONS (the adjudicator). ──
        var goldEven = cells.Where(c => c.Kind == Species.Gold).Select(c => c.Evenness).OrderBy(v => v).ToArray();
        var glueEven = cells.Where(c => c.Kind == Species.Glue).Select(c => c.Evenness).OrderBy(v => v).ToArray();

        // ── verdicts ──
        bool orthoPass = !double.IsNaN(corrDirect) && Math.Abs(corrDirect) < 0.30;
        bool goldSurvives = goldSurvival.Count(x => x.Kind == Species.Gold) >= Math.Max(1, goldSurvival.Count / 2);
        // the DEPTH read decomposes: memory-SHARE rises (a real signal) vs memory-DOMINANCE (memory the largest deep
        // class). If unexercised dominates deep, the depth wall is primarily the UNEXERCISED cliff — the depth-autopsy's
        // direct-reflection cliff, an ECHO — with the memory rise a secondary quadrant-only signal.
        bool depthMemRises = deepMem > shallowMem + 8;
        bool depthMemDominates = deepMem > deepUnexer;

        // ── render ──
        Directory.CreateDirectory(outDir);
        WriteTsv(Path.Combine(outDir, "quadrant_rules.tsv"), cells, g);
        WriteMd(Path.Combine(outDir, "quadrant.md"), runDir, n, nElig, nDirectUnexercised, allSources, tauEven,
            gold, glue, mem, unexer, total2, corrDirect, corrUnion, corrRawGlobal, corrRawBroad, orthoPass, evenByK,
            topByRawEdge, topByEvenness, goldSurvival, goldSurvives, g,
            byDepth, deepFloor, shallowMem, deepMem, shallowUnexer, deepUnexer, depthMemRises, depthMemDominates, goldEven, glueEven);
        WriteHtml(Path.Combine(outDir, "quadrant.html"), runDir, cells, gold, glue, mem, unexer, corrDirect, byDepth, tauEven);

        // ── console — the routing payload ──
        Console.WriteLine();
        Console.WriteLine($"  eligible rules (expLen ≥ {Pearl.ReflectFloorBytes}B): {nElig}  ·  direct-unexercised (k_dir=0): {nDirectUnexercised}  ·  τ_even (median evenness over k_dir≥2) = {tauEven:F3}");
        Console.WriteLine();
        Console.WriteLine("  THE 2×2 (broad = ≥2 DIRECT sources · sharp = evenness ≥ median · k_dir=1 ⇒ MEMORY by definition)");
        Console.WriteLine($"                    sharp (concentrated)   flat (spread)");
        Console.WriteLine($"    broad    GOLD  {gold,6} ({Pct(gold, total2)})   GLUE  {glue,6} ({Pct(glue, total2)})");
        Console.WriteLine($"    narrow   MEMORY{mem,6} ({Pct(mem, total2)})   (k_dir=0 unexercised: {unexer})");
        Console.WriteLine();
        Console.WriteLine($"  (1) ORTHOGONALITY (the decisive number)");
        Console.WriteLine($"      DIRECT  corr(k_dir, evenness) over k_dir≥2 (n{broadCells.Count}) = {corrDirect:F3}  → {(orthoPass ? "PASS — evenness DECOUPLES from breadth: a genuine second axis EXISTS (the sharpness organ's measure)" : "FAIL — evenness still couples with breadth: no real second axis in reflection data → the sharpness organ moves to R″/Whorl-C")}");
        Console.WriteLine($"      mean evenness by k_dir: {string.Join("  ", evenByK.Select(e => $"k{e.K}={e.MeanEven:F3}(n{e.N})"))}");
        Console.WriteLine($"      UNION   corr(union-breadth, evenness-UNION) over ub≥2 = {corrUnion:F3}  (the contaminated ce5e942 read — DAG-union counts)");
        Console.WriteLine($"      RAW     home-vs-complement edge coupling: global corr(ub,edge) = {corrRawGlobal:F3} · broad = {corrRawBroad:F3}  (the −0.84/−0.63 triangle the evenness de-confounds)");
        Console.WriteLine($"  (2) GOLD STABILITY + SELECTION RECEIPT");
        foreach (var gs in goldSurvival)
            Console.WriteLine($"      {gs.Marker,-18} N{256 + gs.Idx}  k_dir={gs.KDir} k_union={gs.KUnion} evenness={(double.IsNaN(gs.Even) ? "  —  " : gs.Even.ToString("F3"))}  → {gs.Kind}");
        Console.WriteLine($"      → {(goldSurvives ? "PASS — the named abstractions survive as broad+uneven (gold)" : "FAIL — the named gold collapses out of the gold cell under the direct measure")}  (selection-readout tables in quadrant.md)");
        string depthRead = !depthMemRises ? "FLATTENS — no memory rise; the quadrant depth read was an ECHO of the depth autopsy's breadth-vs-depth cliff"
            : depthMemDominates ? "PERSISTS + DOMINATES — memory is the largest deep class: a genuinely NEW quadrant depth readout"
            : "ECHO + secondary rise — the UNEXERCISED cliff dominates deep (deep rules never directly re-derived = the depth-autopsy's direct-reflection cliff); the memory-share rise is a real but SECONDARY quadrant-only signal, NOT memory-dominance";
        Console.WriteLine($"  (3) DEPTH, DE-CONFOUNDED   memory share: shallow(d≤3) {shallowMem:F1}% → deep(d≥{deepFloor}) {deepMem:F1}%  ·  unexercised: shallow {shallowUnexer:F1}% → deep {deepUnexer:F1}%");
        Console.WriteLine($"                             → {depthRead}");
        Console.WriteLine($"  (4) GLUE/GOLD BOUNDARY     gold evenness [{Fmt(Pctl(goldEven, 0))}, {Fmt(Pctl(goldEven,.5))}, {Fmt(Pctl(goldEven, 1))}] (min/med/max, n{goldEven.Length})  ·  glue evenness [{Fmt(Pctl(glueEven, 0))}, {Fmt(Pctl(glueEven,.5))}, {Fmt(Pctl(glueEven, 1))}] (n{glueEven.Length})");
        Console.WriteLine();
        Console.WriteLine($"  rendered → {outDir}/  (quadrant.md · quadrant_rules.tsv · quadrant.html)");
        return 0;
    }

    static byte[] Expand(in RePairResult g, int idx) => Reconstruct.Expand(g.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)idx)]);
    static string Latin1(byte[] exp) => System.Text.Encoding.Latin1.GetString(exp);
    static string Fmt(double v) => double.IsNaN(v) ? "—" : v.ToString("F3", CultureInfo.InvariantCulture);

    /// linear-interpolated percentile over a PRE-SORTED ascending array (q ∈ [0,1]); NaN on empty.
    static double Pctl(double[] sorted, double q)
    {
        if (sorted.Length == 0) return double.NaN;
        if (sorted.Length == 1) return sorted[0];
        double pos = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos); int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
    }

    // ── Pearson correlation over two aligned sequences (population form) ──
    static double Pearson(IEnumerable<double> xs, IEnumerable<double> ys)
    {
        var x = xs.ToArray(); var y = ys.ToArray();
        int k = x.Length; if (k == 0) return double.NaN;
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < k; i++) { double dx = x[i] - mx, dy = y[i] - my; sxy += dx * dy; sxx += dx * dx; syy += dy * dy; }
        return (sxx > 0 && syy > 0) ? sxy / Math.Sqrt(sxx * syy) : 0.0;
    }

    static string Pct(int a, int b) => b > 0 ? $"{100.0 * a / b,4:F1}%" : "  — ";

    // ── writers ──
    static void WriteTsv(string path, List<Cell> cells, in RePairResult g)
    {
        var sb = new StringBuilder("rule\tdepth\tspan\texplen\tk_dir\tk_union\ttotal_dir\thome_dir\trawedge_union\tevenness\thome_src\tspecies\texpansion\n");
        foreach (var c in cells.OrderBy(c => c.Idx))
        {
            string ev = double.IsNaN(c.Evenness) ? "nan" : c.Evenness.ToString("F4", CultureInfo.InvariantCulture);
            sb.Append(CultureInfo.InvariantCulture,
                $"N{256 + c.Idx}\t{c.Depth}\t{c.Span}\t{c.ExpLen}\t{c.BreadthDirect}\t{c.BreadthUnion}\t{c.TotalDirect}\t{c.HomeDirect}\t{c.RawEdgeUnion}\t{ev}\t{c.HomeSrc}\t{c.Kind}\t{Vis(Expand(g, c.Idx), 40)}\n");
        }
        File.WriteAllText(path, sb.ToString());
    }

    static void WriteMd(string path, string runDir, int nRules, int nElig, int nDirectUnexercised, SortedSet<string> sources, double tauEven,
        int gold, int glue, int mem, int unexer, int total, double corrDirect, double corrUnion, double corrRawGlobal, double corrRawBroad, bool orthoPass,
        List<(int K, int N, double MeanEven)> evenByK, List<Cell> topByRawEdge, List<Cell> topByEvenness,
        List<(string Marker, int Idx, int KDir, int KUnion, double Even, Species Kind)> goldSurvival, bool goldSurvives, RePairResult g,
        List<(int D, int N, int G, int Gl, int M, int Un, double MeanKDir, double MeanKUnion)> byDepth, int deepFloor,
        double shallowMem, double deepMem, double shallowUnexer, double deepUnexer, bool depthMemRises, bool depthMemDominates, double[] goldEven, double[] glueEven)
    {
        var sb = new StringBuilder();
        sb.Append("# The quadrant assay (H2″) — breadth-normalized evenness (monk-sharpness)\n\n");
        sb.Append($"Read-only, dead data. Converged mesh grammar `{runDir}` — {nRules} rules, {nElig} eligible (expLen ≥ {Pearl.ReflectFloorBytes}B), {nDirectUnexercised} direct-unexercised (k_dir = 0). ");
        sb.Append($"Sources ({sources.Count}): {string.Join(", ", sources)}.\n\n");
        sb.Append("**The count-semantics trap (resolved).** The landed `JewelCounts` is the **DAG-UNION** count — Pearl's reverse-DAG pass pours each parent's per-source distribution into its children, so a child's apparent concentration is borrowed from its ancestors. Evenness therefore reads **`JewelCountsDirect`** (the co-walk count WITHOUT the reverse-DAG — a rule's OWN top-level re-derivations); breadth **k** is the direct source count `|JewelCountsDirect|`. The union numbers appear below only as the contaminated comparison.\n\n");
        sb.Append("**sharpness = evenness = 1 − H_norm** (H_norm = Shannon entropy of the per-source SHARE distribution ÷ log₂ k) — peakedness defined identically at breadth 2 and 4, so free to be orthogonal to breadth. Defined for **k ≥ 2** only; **k = 1 ⇒ MEMORY by definition**. ");
        sb.Append($"sharp = evenness ≥ median (τ = {tauEven:F3}); broad = k_dir ≥ 2.\n\n");

        sb.Append("## The 2×2 (direct plane)\n\n");
        sb.Append("| | sharp (concentrated) | flat (spread) |\n|---|---|---|\n");
        sb.Append($"| **broad** (k_dir ≥ 2) | **GOLD** {gold} ({Pct(gold, total)}) — abstraction with a home | **GLUE** {glue} ({Pct(glue, total)}) — boilerplate |\n");
        sb.Append($"| **narrow** (k_dir = 1) | **MEMORY** {mem} ({Pct(mem, total)}) — by definition | — (k_dir=0 unexercised: {unexer}) |\n\n");

        sb.Append("## (1) Orthogonality — the decisive number\n\n");
        sb.Append($"**DIRECT** corr(k_dir, evenness) over the k_dir ≥ 2 population = **{corrDirect:F3}** → **{(orthoPass ? "PASS" : "FAIL")}**. ");
        sb.Append(orthoPass
            ? "Evenness DECOUPLES from breadth — a genuinely orthogonal sharpness axis EXISTS in the reflection data. The breadth-normalization did its job: peakedness-among-sources is independent of how-many-sources, so the sharpness organ has a real second axis to price on.\n\n"
            : "Evenness STILL couples with breadth even under the de-confounded direct counts — there is no real second axis in the reflection data. Sharpness-as-source-concentration is inherently ~one axis with breadth; a genuine sharpness organ must come from a DIFFERENT substrate (causal load-bearing-ness R″, or Whorl-C execution), NOT the source distribution.\n\n");
        sb.Append("Mean evenness by direct breadth (the coupling made legible — the analogue of mean-edge-by-breadth):\n\n| k_dir | n | mean evenness |\n|---|---|---|\n");
        foreach (var (k, nn, me) in evenByK) sb.Append(CultureInfo.InvariantCulture, $"| {k} | {nn} | {me:F3} |\n");
        sb.Append("\n**Contaminated comparisons** (the confound, made visible):\n\n");
        sb.Append($"- UNION corr(union-breadth, evenness over DAG-union counts) = **{corrUnion:F3}** — the ce5e942 read; the union pours ancestor distributions into children, weakening but not removing the coupling.\n");
        sb.Append($"- RAW home-vs-complement edge: global corr(union-breadth, edge) = **{corrRawGlobal:F3}**, broad-subpop = **{corrRawBroad:F3}** — the triangle (a narrow rule cannot be flat) the evenness de-confounds.\n\n");

        sb.Append("## (2) Gold stability + the selection readout\n\n");
        sb.Append($"Do the banked gold markers survive as broad (k_dir ≥ 2) + uneven (gold)? → **{(goldSurvives ? "PASS" : "FAIL")}**.\n\n");
        sb.Append("| marker | rule | k_dir | k_union | evenness | species |\n|---|---|---|---|---|---|\n");
        foreach (var gs in goldSurvival)
            sb.Append(CultureInfo.InvariantCulture, $"| `{gs.Marker}` | N{256 + gs.Idx} | {gs.KDir} | {gs.KUnion} | {(double.IsNaN(gs.Even) ? "—" : gs.Even.ToString("F3"))} | {gs.Kind} |\n");
        sb.Append("\n**The selection readout** — top-20 by RAW home-vs-complement Edge (the coupled measure) vs top-20 by EVENNESS (the de-confounded measure). The raw list should be dominated by narrow, high-volume MEMORIES (the −0.85 made legible); the evenness list by the semantically real abstractions.\n\n");
        sb.Append("### Top 20 by raw Edge (the OLD measure — expect narrow memories)\n\n");
        sb.Append("| rule | k_dir | k_union | rawEdge (mb) | evenness | species | expansion |\n|---|---|---|---|---|---|---|\n");
        foreach (var c in topByRawEdge)
            sb.Append(CultureInfo.InvariantCulture, $"| N{256 + c.Idx} | {c.BreadthDirect} | {c.BreadthUnion} | {c.RawEdgeUnion} | {(double.IsNaN(c.Evenness) ? "—" : c.Evenness.ToString("F3"))} | {c.Kind} | `{Vis(Expand(g, c.Idx), 40)}` |\n");
        sb.Append("\n### Top 20 by evenness among broad (the NEW measure — expect real abstractions)\n\n");
        sb.Append("| rule | k_dir | k_union | evenness | rawEdge (mb) | species | expansion |\n|---|---|---|---|---|---|---|\n");
        foreach (var c in topByEvenness)
            sb.Append(CultureInfo.InvariantCulture, $"| N{256 + c.Idx} | {c.BreadthDirect} | {c.BreadthUnion} | {c.Evenness:F3} | {c.RawEdgeUnion} | {c.Kind} | `{Vis(Expand(g, c.Idx), 40)}` |\n");

        sb.Append("\n## (3) Depth, de-confounded\n\n");
        sb.Append($"DIRECT-plane MEMORY share: shallow (d ≤ 3) **{shallowMem:F1}%** → deep (d ≥ {deepFloor}) **{deepMem:F1}%**. UNEXERCISED (k_dir = 0) share: shallow **{shallowUnexer:F1}%** → deep **{deepUnexer:F1}%**. ");
        sb.Append(!depthMemRises
            ? "**FLATTENS** — no memory rise; the quadrant depth read was an ECHO of the depth autopsy's breadth-vs-depth cliff.\n\n"
            : depthMemDominates
                ? "**PERSISTS + DOMINATES** — memory is the largest deep class: a genuinely NEW quadrant depth readout, beyond the depth-autopsy echo.\n\n"
                : "**ECHO + secondary rise.** The dominant deep class is UNEXERCISED (a deep rule never appears at top-level, only nested — the depth autopsy's direct-reflection cliff, re-derived here). The memory-share rise is REAL but SECONDARY, and it is NOT memory-dominance: of the shrinking directly-exercised minority at depth, more land k_dir = 1. The primary depth signal is the unexercised cliff (echo); the memory rise is the quadrant-only addition.\n\n");
        sb.Append("| depth | n | gold | glue | memory | unexer | mean k_dir | mean k_union |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var (d, nn, gg, gl, mm, un, mkd, mku) in byDepth)
            sb.Append(CultureInfo.InvariantCulture, $"| {d} | {nn} | {gg} | {gl} | {mm} | {un} | {mkd:F2} | {mku:F2} |\n");

        sb.Append("\n## (4) Glue/gold boundary — the evenness distributions\n\n");
        sb.Append("The adjudicator between genuine abstraction and frequent framework idiom is evenness. If gold and glue occupy cleanly separated evenness bands, the τ split is a real boundary; if they form one continuum, the cut is arbitrary and the gold/glue names are a median artifact.\n\n");
        sb.Append("| cell | n | min | p25 | median | p75 | max |\n|---|---|---|---|---|---|---|\n");
        sb.Append(CultureInfo.InvariantCulture, $"| GOLD | {goldEven.Length} | {Fmt(Pctl(goldEven, 0))} | {Fmt(Pctl(goldEven, .25))} | {Fmt(Pctl(goldEven, .5))} | {Fmt(Pctl(goldEven, .75))} | {Fmt(Pctl(goldEven, 1))} |\n");
        sb.Append(CultureInfo.InvariantCulture, $"| GLUE | {glueEven.Length} | {Fmt(Pctl(glueEven, 0))} | {Fmt(Pctl(glueEven, .25))} | {Fmt(Pctl(glueEven, .5))} | {Fmt(Pctl(glueEven, .75))} | {Fmt(Pctl(glueEven, 1))} |\n");

        sb.Append("\n## The value formula (reads straight off the plane)\n\n");
        sb.Append("Promote **gold**, tolerate **glue**, price **memory** as memory. The organ question — build the sharpness meter, or route to R″/Whorl-C — is decided by claim (1): whether evenness is a distinct axis from breadth in the reflection data.\n");
        File.WriteAllText(path, sb.ToString());
    }

    // ── a compact self-contained HTML: the 2×2 heat grid + a breadth×evenness scatter + the depth-species stack ──
    static void WriteHtml(string path, string runDir, List<Cell> cells, int gold, int glue, int mem, int unexer, double corr,
        List<(int D, int N, int G, int Gl, int M, int Un, double MeanKDir, double MeanKUnion)> byDepth, double tau)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><meta charset=utf-8><title>quadrant assay (breadth-normalized)</title>");
        sb.Append("<style>body{font:14px/1.5 ui-monospace,monospace;background:#0d0f12;color:#d6dae0;max-width:920px;margin:2rem auto;padding:0 1rem}h1{font-size:18px}h2{font-size:15px;color:#8fd0ff;margin-top:2rem}.q{display:grid;grid-template-columns:80px 1fr 1fr;gap:4px;max-width:560px}.c{padding:14px;border-radius:6px;text-align:center}.gold{background:#3a2f0a;border:1px solid #d4a000}.glue{background:#1a2a1a;border:1px solid #3a6a3a}.mem{background:#2a1a2a;border:1px solid #a04fa0}.noise{background:#1a1a20;border:1px solid #444}.lbl{color:#7a8290;padding:14px;text-align:right}svg{background:#12151a;border-radius:6px}</style>");
        int total = cells.Count;
        sb.Append(CultureInfo.InvariantCulture, $"<h1>quadrant assay (H2″) — breadth-normalized evenness</h1><p>{runDir} · {total} eligible rules · corr(k_dir,evenness)={corr:F3} · τ_even={tau:F3}</p>");
        sb.Append("<h2>the 2×2 (direct plane)</h2><div class=q>");
        sb.Append("<div></div><div class=lbl>sharp (concentrated)</div><div class=lbl>flat (spread)</div>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=lbl>broad (k≥2)</div><div class='c gold'>GOLD<br>{gold}<br>{Pct(gold, total)}</div><div class='c glue'>GLUE<br>{glue}<br>{Pct(glue, total)}</div>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=lbl>narrow (k=1)</div><div class='c mem'>MEMORY<br>{mem}<br>{Pct(mem, total)}</div><div class='c noise'>unexer<br>{unexer}</div>");
        sb.Append("</div>");
        sb.Append("<h2>breadth × evenness (the orthogonality scatter)</h2>");
        sb.Append(Scatter(cells));
        sb.Append("<h2>species by depth (the de-confounded depth wall)</h2>");
        sb.Append(DepthStack(byDepth));
        File.WriteAllText(path, sb.ToString());
    }

    static string Scatter(List<Cell> cells)
    {
        const int W = 860, H = 300, ml = 44, mb = 30, mt = 12, mr = 12;
        int maxBreadth = Math.Max(1, cells.Max(c => c.BreadthDirect));
        double px(int b) => ml + (double)b / maxBreadth * (W - ml - mr);
        double py(double e) => mt + (1 - e) * (H - mt - mb);   // evenness ∈ [0,1]
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg width={W} height={H} viewBox='0 0 {W} {H}'>");
        for (int gb = 0; gb <= maxBreadth; gb++) { double xx = px(gb); sb.Append(CultureInfo.InvariantCulture, $"<line x1={xx:F1} y1={mt} x2={xx:F1} y2={H - mb} stroke='#1c2029' /><text x={xx:F1} y={H - mb + 14} fill='#7a8290' font-size=10 text-anchor=middle>{gb}</text>"); }
        int step = Math.Max(1, cells.Count / 4000);
        for (int i = 0; i < cells.Count; i += step)
        {
            var c = cells[i];
            if (double.IsNaN(c.Evenness)) continue;   // memory (k=1) has no evenness — off-plane
            string col = c.Kind switch { Species.Gold => "#f5c400", Species.Glue => "#5bd06a", Species.Memory => "#c86fd0", _ => "#556" };
            double jx = px(c.BreadthDirect) + ((c.Idx * 2654435761u) % 17) - 8;
            sb.Append(CultureInfo.InvariantCulture, $"<circle cx={jx:F1} cy={py(c.Evenness):F1} r=1.6 fill='{col}' fill-opacity=0.55 />");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<text x={W / 2} y={H - 4} fill='#7a8290' font-size=11 text-anchor=middle>direct breadth (k_dir) →  ·  ↑ evenness (1−H_norm)</text></svg>");
        return sb.ToString();
    }

    static string DepthStack(List<(int D, int N, int G, int Gl, int M, int Un, double MeanKDir, double MeanKUnion)> byDepth)
    {
        if (byDepth.Count == 0) return "<p>(no depths)</p>";
        const int W = 860, H = 260, ml = 30, mb = 28, mt = 12, mr = 12;
        int maxD = byDepth.Max(r => r.D);
        int maxN = byDepth.Max(r => r.N);
        double bw = (W - ml - mr) / (double)Math.Max(1, maxD);
        double hy(int v) => (double)v / maxN * (H - mt - mb);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg width={W} height={H} viewBox='0 0 {W} {H}'>");
        foreach (var (d, nn, gg, gl, mm, un, _, _) in byDepth)
        {
            double x = ml + (d - 1) * bw;
            double y = H - mb;
            foreach (var (v, col) in new[] { (gg, "#f5c400"), (gl, "#5bd06a"), (mm, "#c86fd0"), (un, "#556") })
            {
                double h = hy(v); y -= h;
                sb.Append(CultureInfo.InvariantCulture, $"<rect x={x + 1:F1} y={y:F1} width={bw - 2:F1} height={h:F1} fill='{col}' />");
            }
            if (d % 2 == 1) sb.Append(CultureInfo.InvariantCulture, $"<text x={x + bw / 2:F1} y={H - mb + 14} fill='#7a8290' font-size=9 text-anchor=middle>{d}</text>");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<text x={W / 2} y={H - 2} fill='#7a8290' font-size=11 text-anchor=middle>depth →  (■gold ■glue ■memory ■unexer)</text></svg>");
        return sb.ToString();
    }

    // printable-collapse of an expansion for tables (control bytes → ·, newline → ⏎, cap length)
    static string Vis(byte[] exp, int cap)
    {
        var sb = new StringBuilder();
        int lim = Math.Min(exp.Length, cap);
        for (int i = 0; i < lim; i++)
        {
            byte b = exp[i];
            sb.Append(b == (byte)'\n' ? '⏎' : b == (byte)'\t' ? '→' : (b >= 32 && b < 127) ? (char)b : '·');
        }
        if (exp.Length > cap) sb.Append('…');
        return sb.ToString();
    }
}
