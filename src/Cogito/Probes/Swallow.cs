namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE SWALLOW PROBE (metacircularity check) ──  READ-ONLY science on DEAD DATA, testing the 4th
// homoiconic closure: today the grammar lives in C# heap structs
// (GrammarRule[]); the tape can't graze it. The swallow SERIALIZES the rule bodies AS SPANS and asks whether the
// grammar-of-grammar is ITSELF critical (self-similar all the way down — the log-spiral literalized). If critical,
// the store is a BODY and the bootstrap's "shell read as its own growth-program" clause survives first contact; if
// flat, the store is a heap and the clause revises honestly.
//
// THE VOW (canonical serialization — deterministic, byte-reproducible):
//   • RULE ORDER — mint order (rule index i; the nonterminal id AlphabetSize+i IS the rule's stable token). A rule
//     references only EARLIER nonterminals, so mint order is a topological order of the reference DAG.
//   • SYMBOL ENCODING — a Pattern symbol is emitted as its raw Value: a TERMINAL keeps its byte (0..AlphabetSize−1),
//     a NONTERMINAL keeps its stable token AlphabetSize+childIdx. "nonterminals as stable tokens, terminals as bytes."
//   • BARRIER — a sentinel token (AlphabetSize+nr, one past the max nonterminal, never a real symbol) between rule
//     bodies. A meta-rule may not straddle it, so a meta-motif is a sub-pattern WITHIN a body that recurs ACROSS
//     bodies (the honest "shared structural motif" — the byte-inducer's '\n' law, applied one level up).
//   • DENSIFY — the raw-token stream remaps to a dense 0..K−1 alphabet in first-appearance order (InduceTokens'
//     contract), then Re-Pair induces the META-grammar with the densified barrier as its unmergeable terminal.
//
// THE BINARY DEGENERACY (why consolidation is mandatory, and itself a finding): a fresh Engine.Induce grammar is
// PURE binary Re-Pair — every body is length 2, and every within-body pair is a UNIQUE rule (content-addressed), so
// no within-body motif can recur and the barrier-delimited stream has no graze-able structure by construction. The
// swallow is BORN AT CONSOLIDATION: AnnealEvict.Breach welds speculative chains into n-ary templates whose internal
// sub-patterns CAN recur across bodies. So the probe consolidates the binary base to n-ary before serializing (the
// checkpoint path skips this — a banked converged grammar already carries breach templates + slots).
//
// THE NULL (pre-registered) — rule-SHUFFLED: a deterministic global Fisher-Yates permutation of the body-symbol
// multiset (Engine.Shuffled, seed fixed) with body lengths + barrier positions HELD, so the null keeps the exact
// token frequencies and destroys only the arrangement. PASS = real critical (meanz→≈−0.70, low CvZ, deep KZ) while
// the null is flat; FAIL = real ≈ null (the meta-structure is Zipf-NULL — explained by frequencies alone).
public static class Swallow
{
    public const ulong DefaultSeed = 0x5EEDF00Dul;

    public static int Run(string source, string outDir, ulong seed, int topN, bool barrier, bool consolidate, int breachQuota, bool composeOnly)
    {
        Console.WriteLine("swallow — grammar-on-tape · the metacircular stage on dead data");

        // ── load the base grammar (file → induce+consolidate; checkpoint dir → peek the converged grammar) ──
        RePairResult baseG;
        string regime;
        if (Directory.Exists(source) && File.Exists(Path.Combine(source, MeshCheckpoint.FileName)))
        {
            baseG = Loc.LoadBinaryPrefix(source).Raw;
            regime = $"checkpoint {source}";
            Console.WriteLine($"  source      {regime} · {baseG.Rules.Length} rules · alphabet {baseG.AlphabetSize}");
        }
        else
        {
            byte[] corpus = ResolveCorpus(source, out string corpusLabel);
            if (corpus.Length == 0) { Console.Error.WriteLine($"swallow: empty/absent corpus source '{source}'"); return 1; }
            var (_, _, g0) = Engine.Induce(corpus);
            var s0 = Engine.RenormStats(g0);
            Console.WriteLine($"  source      corpus {corpusLabel} · {corpus.Length}B");
            Console.WriteLine($"  base(binary) {g0.Rules.Length} rules · " + StatLine(s0) + $"  (bodies all length-2 — degenerate for the swallow)");
            if (consolidate)
            {
                int quota = breachQuota > 0 ? breachQuota : g0.Rules.Length;
                var br = AnnealEvict.Breach(g0, corpus, quota, seed);
                baseG = br.Grammar;
                regime = $"consolidated (breach quota {quota}: {br.Line})";
                Console.WriteLine($"  consolidate {regime}");
            }
            else { baseG = g0; regime = "binary (no consolidation)"; }
        }

        int nr = baseG.Rules.Length;
        if (nr < 4) { Console.Error.WriteLine($"swallow: base grammar too small ({nr} rules) — need a deep converged grammar"); return 1; }

        // ── the base grammar's own shape: RenormStats (the criticality it carries) + arity histogram ──
        var baseStat = Engine.RenormStats(baseG);
        var arity = ArityHistogram(baseG);
        int naryCount = 0; long bodySyms = 0; int maxArity = 0;
        foreach (var r in baseG.Rules) { int L = r.Pattern.Length; bodySyms += L; if (L > 2) naryCount++; if (L > maxArity) maxArity = L; }
        Console.WriteLine($"  base        {nr} rules · " + StatLine(baseStat));
        Console.WriteLine($"  bodies      {bodySyms} symbols · {naryCount} n-ary (arity>2) · max arity {maxArity} · barrier {(barrier ? "ON (bodies separated)" : "OFF (concatenated)")}"
            + (composeOnly ? " · COMPOSE-ONLY (terminals dropped — the pure reference DAG)" : ""));
        if (naryCount == 0)
            Console.WriteLine("  ⚠ NO n-ary bodies — every body is a unique pair; the meta-grammar can only capture cross-body coincidence (expect FLAT/HEAP).");

        // ── induce the META-grammar over the serialized bodies (real), and over the rule-shuffled null ──
        var (metaReal, sReal) = MetaInduce(baseG, barrier, composeOnly, null);
        var (metaNull, _)     = MetaInduce(baseG, barrier, composeOnly, seed);
        var statReal = Engine.RenormStats(metaReal);
        var statNull = Engine.RenormStats(metaNull);

        Console.WriteLine();
        Console.WriteLine("META-GRAMMAR — the grammar-of-grammar (induced over the serialized rule bodies)");
        Console.WriteLine($"  stream      {sReal.StreamLen} tokens · meta-alphabet {sReal.K} distinct");
        Console.WriteLine($"  REAL        {metaReal.Rules.Length} meta-rules · " + StatLine(statReal));
        Console.WriteLine($"  NULL        {metaNull.Rules.Length} meta-rules · " + StatLine(statNull) + "  (rule-shuffled; frequencies held, arrangement destroyed)");

        // ── the verdict (pre-registered) ──
        var verdict = Verdict(statReal, statNull);
        Console.WriteLine();
        Console.WriteLine($"  VERDICT     {verdict.Tag}");
        Console.WriteLine($"              {verdict.Line}");

        // ── the corollary — the meta-grammar's most frequent motifs (do transform-slot families reappear?) ──
        var motifs = TopMotifs(metaReal, sReal, topN);
        Console.WriteLine();
        Console.WriteLine($"COROLLARY — top {motifs.Count} meta-motifs (the proto-types: shared sub-patterns across rule bodies)");
        Console.WriteLine("  rank  uses  depth  len  class      motif (decoded: Nk=base-nonterminal · 'c'=byte · ·=space · ¦=barrier)");
        for (int i = 0; i < motifs.Count; i++)
        {
            var m = motifs[i];
            Console.WriteLine($"  {i + 1,4}  {m.Uses,4}  {m.Depth,5}  {m.LeafLen,3}  {m.Cls,-9}  {Trunc(m.Decoded, 96)}");
        }
        var families = SlotFamilies(motifs);
        Console.WriteLine();
        if (families.Count > 0)
        {
            Console.WriteLine($"  TRANSFORM-SLOT FAMILIES — {families.Count} shared-skeleton set(s) (a fixed frame + a varying slot = a proto-type):");
            foreach (var f in families) Console.WriteLine($"    {f}");
        }
        else
            Console.WriteLine("  no shared-skeleton slot families in the top band (motifs are distinct sub-patterns, not a frame+slot paradigm)");
        int compose = motifs.Count(m => m.Cls == "compose"), literal = motifs.Count(m => m.Cls == "literal"), mixed = motifs.Count(m => m.Cls == "mixed");
        Console.WriteLine($"  top-band composition: {compose} compose (all-nonterminal proto-types) · {mixed} mixed · {literal} literal (byte fragments)");
        bool byteDriven = verdict.Tag.StartsWith("BODY") && !composeOnly && compose == 0 && literal > 0;
        if (byteDriven)
            Console.WriteLine("  ⚠ BYTE-DRIVEN BODY — the top motifs are ALL literal (re-derived corpus morphology from the demoted terminal-pattern bodies), NOT compositional proto-types. This BODY reflects the CORPUS's criticality through the literal bodies, not the grammar's own composition structure. Re-run with --compose-only to test the pure reference DAG in isolation (it collapses to HEAP — the composition is near-unique, un-grazeable).");

        // ── render ──
        Directory.CreateDirectory(outDir);
        WriteTsv(Path.Combine(outDir, "swallow_stats.tsv"), regime, nr, naryCount, maxArity, barrier, sReal, baseStat, statReal, statNull, verdict);
        WriteMotifsTsv(Path.Combine(outDir, "swallow_motifs.tsv"), motifs);
        WriteHtml(Path.Combine(outDir, "swallow.html"), regime, nr, naryCount, maxArity, arity, barrier, sReal, baseStat, statReal, statNull, verdict, motifs, families);
        Console.WriteLine();
        Console.WriteLine($"  rendered → {outDir}/  (swallow.html · swallow_stats.tsv · swallow_motifs.tsv)");
        return 0;
    }

    // ── the serialized-stream carrier: the alphabet split + the dense→raw inverse the motif decoder reads ──
    readonly record struct MetaStream(uint Alpha, int NrBase, uint BarVal, uint K, uint[] Inverse, int StreamLen);

    // ── serialize the base grammar's rule bodies → canonical dense token stream → induce the meta-grammar. When
    // `shuffleSeed` is set, the body-symbol multiset is globally permuted (body lengths + barrier positions held) —
    // the rule-shuffled null: identical frequencies, arrangement destroyed. ──
    static (RePairResult Meta, MetaStream Stream) MetaInduce(RePairResult g, bool barrier, bool composeOnly, ulong? shuffleSeed)
    {
        uint alpha = g.AlphabetSize;
        int nr = g.Rules.Length;
        uint barVal = alpha + (uint)nr;                             // sentinel: one past the max nonterminal, never a real symbol

        var body = new List<uint>();
        var lens = new int[nr];
        for (int i = 0; i < nr; i++)                                // composeOnly ⇒ drop terminals, keep only the nonterminal reference structure (the pure DAG)
        {
            int L = 0;
            foreach (var sym in g.Rules[i].Pattern) { if (composeOnly && sym.Value < alpha) continue; body.Add(sym.Value); L++; }
            lens[i] = L;
        }
        var bodyArr = body.ToArray();
        if (shuffleSeed is ulong sd) bodyArr = Engine.Shuffled(bodyArr, sd);   // destroy arrangement, keep frequencies

        var raw = new List<uint>(bodyArr.Length + nr);
        int p = 0;
        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < lens[i]; j++) raw.Add(bodyArr[p++]);
            if (barrier && i < nr - 1) raw.Add(barVal);
        }

        var map = new Dictionary<uint, uint>();                      // raw token → dense id (first-appearance order — InduceTokens' contract)
        var tape = new Symbol[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            if (!map.TryGetValue(raw[i], out var id)) { id = (uint)map.Count; map[raw[i]] = id; }
            tape[i] = new Symbol(id);
        }
        uint K = (uint)map.Count;
        uint denseBar = barrier && map.TryGetValue(barVal, out var db) ? db : RePair.NoBarrier;
        var inverse = new uint[K];
        foreach (var kv in map) inverse[kv.Value] = kv.Key;

        var meta = new RePair().Induce(tape, Mbits.Zero, K, null, denseBar);
        return (meta, new MetaStream(alpha, nr, barVal, K, inverse, raw.Count));
    }

    // ── one meta-motif: a meta-rule expanded to its meta-terminal leaves, decoded back to base symbols ──
    readonly record struct Motif(int MetaId, int Uses, int Depth, int LeafLen, string Cls, string Decoded, uint[] Leaves);

    static List<Motif> TopMotifs(RePairResult meta, in MetaStream s, int topN)
    {
        int m = meta.Rules.Length;
        if (m == 0) return new List<Motif>();
        var uses = Engine.RuleUses(meta);
        var depth = MetaDepth(meta);
        var order = new int[m]; for (int i = 0; i < m; i++) order[i] = i;
        Array.Sort(order, (a, b) => uses[b] != uses[a] ? uses[b].CompareTo(uses[a]) : a.CompareTo(b));  // most-used first, tie by mint order

        var outp = new List<Motif>(Math.Min(topN, m));
        var leaves = new List<uint>(16);
        for (int k = 0; k < m && outp.Count < topN; k++)
        {
            int i = order[k];
            if (uses[i] <= 0) break;                                 // an unreferenced meta-rule is not a motif
            leaves.Clear();
            ExpandMeta((uint)(s.K + (uint)i), s.K, meta.Rules, leaves);
            int nNon = 0, nTerm = 0;
            var sb = new StringBuilder();
            foreach (var lf in leaves)
            {
                uint rawv = lf < s.K ? s.Inverse[lf] : 0;
                if (lf < s.K && rawv >= s.Alpha && rawv != s.BarVal) nNon++; else if (lf < s.K) nTerm++;
                sb.Append(RenderLeaf(lf, s)).Append(' ');
            }
            string cls = nNon > 0 && nTerm == 0 ? "compose" : nTerm > 0 && nNon == 0 ? "literal" : "mixed";
            outp.Add(new Motif((int)s.K + i, uses[i], depth[i], leaves.Count, cls, sb.ToString().TrimEnd(), leaves.ToArray()));
        }
        return outp;
    }

    static void ExpandMeta(uint sym, uint K, GrammarRule[] rules, List<uint> outLeaves)
    {
        if (sym < K) { outLeaves.Add(sym); return; }
        int idx = (int)(sym - K);
        if (idx < 0 || idx >= rules.Length) { outLeaves.Add(sym); return; }   // defensive — emission order forbids a forward ref
        foreach (var s in rules[idx].Pattern) ExpandMeta(s.Value, K, rules, outLeaves);
    }

    // per-meta-rule RG depth (1 + max child depth) — the meta-tower's scale, same recurrence as RenormStats' depth[]
    static int[] MetaDepth(RePairResult meta)
    {
        int m = meta.Rules.Length;
        var d = new int[m];
        uint K = meta.AlphabetSize;
        for (int i = 0; i < m; i++)
        {
            int best = 0;
            foreach (var sym in meta.Rules[i].Pattern)
                if (sym.Value >= K && (int)(sym.Value - K) < i) best = Math.Max(best, d[(int)(sym.Value - K)]);
            d[i] = best + 1;
        }
        return d;
    }

    static string RenderLeaf(uint leaf, in MetaStream s)
    {
        if (leaf >= s.K) return $"M{leaf}";                          // a meta-nonterminal that slipped the expand guard
        uint raw = s.Inverse[leaf];
        if (raw == s.BarVal) return "¦";
        if (raw < s.Alpha) { byte b = (byte)raw; return b == 32 ? "·" : b >= 33 && b < 127 ? ((char)b).ToString() : $"\\x{b:X2}"; }
        return $"N{raw}";                                            // a base nonterminal — its stable token (= AlphabetSize+childIdx, DepthAutopsy's Nk labeling)
    }

    // ── transform-slot families — a fixed frame + a varying slot across the top motifs (the Whorl-B blur, re-derived
    // from the swallow's angle). Length-2 motifs sharing a leaf on one side with ≥2 distinct partners = a slot. ──
    static List<string> SlotFamilies(List<Motif> motifs)
    {
        var byFirst = new Dictionary<string, List<string>>();
        var byLast = new Dictionary<string, List<string>>();
        foreach (var m in motifs)
        {
            var toks = m.Decoded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length != 2) continue;                          // the cleanest slot signature is a 2-leaf frame
            (byFirst.TryGetValue(toks[0], out var lf) ? lf : byFirst[toks[0]] = new()).Add(toks[1]);
            (byLast.TryGetValue(toks[1], out var lg) ? lg : byLast[toks[1]] = new()).Add(toks[0]);
        }
        var outp = new List<string>();
        foreach (var (k, v) in byFirst) if (v.Count >= 2) outp.Add($"[{k} · ▢]  slot → {{{string.Join(", ", v)}}}");
        foreach (var (k, v) in byLast) if (v.Count >= 2) outp.Add($"[▢ · {k}]  slot → {{{string.Join(", ", v)}}}");
        return outp;
    }

    // ── the verdict logic (pre-registered) ──
    readonly record struct Vd(string Tag, string Line);
    static bool NearCrit(double z) => !double.IsNaN(z) && Math.Abs(z + 0.70) <= 0.35;   // z ∈ [−1.05, −0.35] around the −0.70 universality
    static Vd Verdict(Engine.RenormStat real, Engine.RenormStat nul)
    {
        bool rCrit = NearCrit(real.MeanZ) && real.KZ >= 3 && !double.IsNaN(real.CvZ) && real.CvZ <= 0.60;
        bool nCrit = NearCrit(nul.MeanZ) && nul.KZ >= 3 && !double.IsNaN(nul.CvZ) && nul.CvZ <= 0.60;
        bool deeper = real.KZ > nul.KZ + 1 || (!double.IsNaN(real.CvZ) && !double.IsNaN(nul.CvZ) && real.CvZ < nul.CvZ * 0.7);
        if (rCrit && !nCrit)
            return new Vd("BODY — the store is a BODY (self-similar all the way down)",
                "the grammar-of-grammar is CRITICAL while the rule-shuffled null is FLAT: the arrangement carries structure beyond its frequencies. The \"recursive self-maintaining universe\" clause SURVIVES first contact.");
        if (rCrit && nCrit && deeper)
            return new Vd("BODY (weak) — self-similar, but frequencies carry much of it",
                "both real and null read critical, yet real is deeper/tighter (KZ/CvZ separation): there is arrangement-structure above the Zipf-NULL floor, but the token-frequency law explains a large share. Graze-able, with caveats.");
        if (!rCrit && !nCrit)
            return new Vd("HEAP — no graze-able meta-structure at this serialization",
                "neither the real nor the shuffled meta-grammar is critical: the rule bodies do not compose into a self-similar store at this angle. The \"shell reads itself\" clause REVISES honestly (or the serialization needs a deeper n-ary base).");
        return new Vd("HEAP — real ≈ null (Zipf-NULL)",
            "the real and shuffled meta-grammars are indistinguishable: whatever meta-structure exists is explained by token frequencies alone, not by the arrangement. No self-similarity beyond the frequency law; the clause REVISES.");
    }

    // ── helpers ──
    static byte[] ResolveCorpus(string source, out string label)
    {
        if (File.Exists(source)) { label = Path.GetFileName(source); return File.ReadAllBytes(source); }
        if (Directory.Exists(source))
        {
            var files = Directory.GetFiles(source).Where(f => !f.EndsWith(".bin") && !f.EndsWith(".sh")).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            var ms = new MemoryStream();
            foreach (var f in files) { ms.Write(File.ReadAllBytes(f)); ms.WriteByte((byte)'\n'); }
            label = $"{Path.GetFileName(source.TrimEnd('/'))}/ ({files.Length} files)";
            return ms.ToArray();
        }
        label = source; return Array.Empty<byte>();
    }

    static int[] ArityHistogram(RePairResult g)
    {
        int max = 0; foreach (var r in g.Rules) if (r.Pattern.Length > max) max = r.Pattern.Length;
        var h = new int[max + 1];
        foreach (var r in g.Rules) h[r.Pattern.Length]++;
        return h;
    }

    static string StatLine(Engine.RenormStat s) =>
        $"scales {s.Scales} · meanz {F(s.MeanZ)} · CvZ {F(s.CvZ)} · KZ {s.KZ} · maxSpan {s.MaxSpan:F0}";
    static string F(double v) => double.IsNaN(v) ? "nan" : v.ToString("F3", CultureInfo.InvariantCulture);
    static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";

    // ── writers ──
    static void WriteTsv(string path, string regime, int nr, int nary, int maxArity, bool barrier, in MetaStream s,
        Engine.RenormStat baseStat, Engine.RenormStat real, Engine.RenormStat nul, Vd v)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# swallow — grammar-on-tape · {regime}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# base {nr} rules · {nary} n-ary · maxArity {maxArity} · barrier {barrier} · stream {s.StreamLen} tokens · meta-alphabet {s.K}\n");
        sb.Append("layer\tscales\tmeanz\tcvz\tkz\tmaxspan\n");
        sb.Append(CultureInfo.InvariantCulture, $"base\t{baseStat.Scales}\t{F(baseStat.MeanZ)}\t{F(baseStat.CvZ)}\t{baseStat.KZ}\t{baseStat.MaxSpan:F0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"meta_real\t{real.Scales}\t{F(real.MeanZ)}\t{F(real.CvZ)}\t{real.KZ}\t{real.MaxSpan:F0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"meta_null\t{nul.Scales}\t{F(nul.MeanZ)}\t{F(nul.CvZ)}\t{nul.KZ}\t{nul.MaxSpan:F0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# verdict: {v.Tag}\n# {v.Line}\n");
        File.WriteAllText(path, sb.ToString());
    }

    static void WriteMotifsTsv(string path, List<Motif> motifs)
    {
        var sb = new StringBuilder("rank\tmeta_id\tuses\tdepth\tleaf_len\tclass\tmotif\n");
        for (int i = 0; i < motifs.Count; i++)
        {
            var m = motifs[i];
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1}\t{m.MetaId}\t{m.Uses}\t{m.Depth}\t{m.LeafLen}\t{m.Cls}\t{m.Decoded}\n");
        }
        File.WriteAllText(path, sb.ToString());
    }

    static void WriteHtml(string path, string regime, int nr, int nary, int maxArity, int[] arity, bool barrier, in MetaStream s,
        Engine.RenormStat baseStat, Engine.RenormStat real, Engine.RenormStat nul, Vd v, List<Motif> motifs, List<string> families)
    {
        bool body = v.Tag.StartsWith("BODY");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><meta charset=utf-8><title>the swallow — grammar-on-tape</title>");
        sb.Append("<style>body{font:14px/1.5 ui-monospace,monospace;background:#0d0f12;color:#d6dae0;max-width:960px;margin:2rem auto;padding:0 1rem}h1{font-size:19px}h2{font-size:15px;color:#8fd0ff;margin-top:1.8rem}");
        sb.Append(".v{padding:.7rem 1rem;border-radius:6px;background:#161a20;border-left:4px solid " + (body ? "#3d5" : "#f0a") + ";margin:1rem 0}table{border-collapse:collapse;font-size:12.5px;margin:.4rem 0}td,th{padding:3px 10px;text-align:right}th{color:#7a8290}td.l,th.l{text-align:left}.mono{color:#c8b}.hl{color:" + (body ? "#5e6" : "#f6a") + ";font-weight:bold}.dim{color:#7a8290}</style>");
        sb.Append(CultureInfo.InvariantCulture, $"<h1>the swallow — grammar-on-tape</h1><p class=dim>the metacircular stage on dead data · {regime}</p>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=v><b class=hl>{v.Tag}</b><br>{v.Line}</div>");
        if (body && motifs.Count > 0 && motifs.All(m => m.Cls == "literal"))
            sb.Append("<div class=v style='border-left-color:#fa0'><b style='color:#fa0'>⚠ BYTE-DRIVEN BODY</b><br>the top motifs are ALL literal (re-derived corpus morphology from the demoted terminal-pattern bodies), not compositional proto-types — this BODY reflects the CORPUS's criticality through the literal bodies, not the grammar's own composition structure. Re-run with <span class=mono>--compose-only</span> to isolate the pure reference DAG (it collapses to HEAP).</div>");

        sb.Append("<h2>the three layers — is the grammar-of-grammar critical?</h2>");
        sb.Append("<table><tr><th class=l>layer<th>scales<th>meanz<th>CvZ<th>KZ<th>maxSpan<th class=l>read</tr>");
        Row(sb, "base grammar", baseStat, "the store's own criticality (the −0.70 it carries)");
        Row(sb, "meta · REAL", real, "grammar-of-grammar, bodies in mint order");
        Row(sb, "meta · NULL", nul, "rule-shuffled — frequencies held, arrangement destroyed");
        sb.Append("</table>");
        sb.Append(CultureInfo.InvariantCulture, $"<p class=dim>base {nr} rules · {nary} n-ary (arity>2, max {maxArity}) · barrier {(barrier ? "ON" : "OFF")} · stream {s.StreamLen} tokens · meta-alphabet {s.K}</p>");

        sb.Append("<h2>base arity histogram (where the n-ary bodies are — the graze-able material)</h2><table><tr><th>arity<th>rules</tr>");
        for (int a = 2; a < arity.Length; a++) if (arity[a] > 0) sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{a}<td>{arity[a]}</tr>");
        sb.Append("</table>");

        sb.Append(CultureInfo.InvariantCulture, $"<h2>corollary — top {motifs.Count} meta-motifs (the proto-types)</h2>");
        if (families.Count > 0)
        {
            sb.Append("<p><b>transform-slot families</b> (a fixed frame + a varying slot — the Whorl-B blur re-derived):</p><ul>");
            foreach (var f in families) sb.Append(CultureInfo.InvariantCulture, $"<li class=mono>{Esc(f)}</li>");
            sb.Append("</ul>");
        }
        else sb.Append("<p class=dim>no shared-skeleton slot families in the top band.</p>");
        sb.Append("<table><tr><th>rank<th>uses<th>depth<th>len<th class=l>class<th class=l>motif <span class=dim>(Nk=base-nonterminal · 'c'=byte · ·=space · ¦=barrier)</span></tr>");
        for (int i = 0; i < motifs.Count; i++)
        {
            var m = motifs[i];
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{i + 1}<td>{m.Uses}<td>{m.Depth}<td>{m.LeafLen}<td class=l>{m.Cls}<td class=\"l mono\">{Esc(Trunc(m.Decoded, 120))}</tr>");
        }
        sb.Append("</table>");
        File.WriteAllText(path, sb.ToString());
    }

    static void Row(StringBuilder sb, string label, Engine.RenormStat s, string read) =>
        sb.Append(CultureInfo.InvariantCulture, $"<tr><td class=l>{label}<td>{s.Scales}<td>{F(s.MeanZ)}<td>{F(s.CvZ)}<td>{s.KZ}<td>{s.MaxSpan:F0}<td class=\"l dim\">{read}</tr>");
    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
