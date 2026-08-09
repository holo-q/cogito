namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using static Cogito.CliReports;

// ── gret ──  GRAMMAR-NATIVE RETRIEVAL, head-to-head against the n-gram floor (Worker A).
// The thesis under test: cogito's edge is the GRAMMAR (Re-Pair learned variable-length chunks / concepts),
// not the raw 1+2-grams the `bench` verb actually ranks on. So deploy the grammar as the retrieval engine and
// measure it on the SAME 98-site benchmark with the SAME fair metrics (top-1 / r@10 / r@20 / MRR / gold-rank
// median). Every relevance function below shares ONE BM25/IDF ranker and ONE metric harness — only the TERM
// UNIT changes (n-grams vs grammar chunks vs grammar concepts), so the comparison isolates the grammar's worth.
//
// Variants:
//   NG-BM25        — 1+2-gram BM25 (the floor; reproduces `bench` to validate the harness)
//   GCHUNK-BM25    — grammar as a learned TOKENIZER: greedy longest-first cover → chunk-id terms, BM25 over them
//   GCHUNK+UNI     — grammar chunks PLUS unigrams from the uncovered residue (does the concept layer ADD signal?)
//   GCONCEPT-IDF   — grammar as a CONCEPT vocabulary: every rule-expansion that occurs as a substring, IDF-summed
//   GCONCEPT-DEPTH — concept overlap weighted by rule composition DEPTH (deeper = more abstract/specific)
//   GCONCEPT-IDF2  — IDF² weighting: punish generic (high-DF) concepts harder, chasing discrimination
//   GCONCEPT-RARE  — hard stoplist: drop concepts present in >25% of sites (only discriminating concepts vote)
//   GCONCEPT-COUNT — naive unweighted concept count (reproduces the prior "24%, too coarse" number)
// Grammar is induced LOWERCASED by default (the n-gram baseline lowercases too — removes the CamelCase↔nl-words
// confound); `--raw` adds case-sensitive rows so the confound's size is visible.
// The `gret` verb's engine — a standalone home since the Cli god-partial dissolved (was `partial class Cli`).
// PrototypeCommands.Gret() rebuilds the argv this Run scrapes; the shared report helpers ride CliReports.
public static class Gret
{
    public static int Run(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1]) || !File.Exists(args[2]))
        { Console.Error.WriteLine("  usage: gret <sites-file> <intents-file> [--raw] [--delim <sep>] [--index <grammar-corpus>]"); return 1; }
        bool raw = args.Contains("--raw");
        int ixArg = Array.IndexOf(args, "--index");
        string? grammarCorpusFile = ixArg >= 0 && ixArg + 1 < args.Length ? args[ixArg + 1] : null;
        int dArg = Array.IndexOf(args, "--delim");
        string? delim = dArg >= 0 && dArg + 1 < args.Length ? args[dArg + 1] : null;
        int dumpArg = Array.IndexOf(args, "--dump");
        string? dumpPrefix = dumpArg >= 0 && dumpArg + 1 < args.Length ? args[dumpArg + 1] : null;
        // --rankdump <out>: emit per-intent NG-BM25 full rankings (top-K) for EXTERNAL localization
        // scoring (SWE-bench-Lite: one issue-intent over a whole repo's sites, aggregate site→file
        // downstream). This is the engine-of-record ranker: pure NG-BM25, NO grammar induction (BM25
        // never needed it — skipping it is what lets this scale to django-size 37k-site indexes).
        int rankDumpArg = Array.IndexOf(args, "--rankdump");
        string? rankDumpPath = rankDumpArg >= 0 && rankDumpArg + 1 < args.Length ? args[rankDumpArg + 1] : null;
        // --topk <K>: shared dump depth for BOTH external-scoring paths (--rank BEIR, --rankdump SWE-loc).
        // K=1000 so downstream recall@k up to 1000 is reconstructible (a top-100 dump cannot yield recall@1000);
        // TryParse (not Parse) so a malformed value falls back to the default rather than throwing.
        int topKArg = Array.IndexOf(args, "--topk");
        int topK = topKArg >= 0 && topKArg + 1 < args.Length && int.TryParse(args[topKArg + 1], out var tk) ? tk : 1000;

        int rankArg = Array.IndexOf(args, "--rank");
        string? rankPrefix = rankArg >= 0 && rankArg + 1 < args.Length ? args[rankArg + 1] : null;

        // ── --rank: NG-BM25 rank dump for EXTERNAL (qrels-based) scoring — the BEIR path. Non-diagonal (no
        //    intent↔site assumption), keepAll (no length filter, so site/intent index == the caller's fixed
        //    doc/query order), and it SKIPS all grammar induction (Re-Pair over 171k docs would OOM/crawl). ──
        if (rankPrefix != null)
        {
            var rsites = LoadSites(args[1], delim, keepAll: true);
            // Intents as a JSON array when the file is .json — multi-line queries (whole-file code
            // intents, e.g. CoIR codetrans/apps) survive intact; line-per-intent files still work.
            var rintents = args[2].EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? LoadJsonArray(args[2])
                : File.ReadAllLines(args[2]).Select(l => l.Trim()).ToList();
            Console.WriteLine($"gret · NG-BM25 rank dump · {rsites.Count} sites · {rintents.Count} intents · top{topK}");
            RankBm25Dump(rankPrefix, rsites, rintents, topK);
            return 0;
        }

        // Sites: block i == site i. Loader auto-handles JSON arrays (.json) and the `=====SITE=====` sentinel
        // (methods contain blank lines, so \n\n-splitting SHATTERS them — the clean-field delimiter discipline).
        var sites = LoadSites(args[1], delim);
        // Intents: one per line, line i targets site i (the held-out test set — the floor's measurement).
        var intents = File.ReadAllLines(args[2]).Select(l => l.Trim()).Where(l => l.Length > 5).ToList();
        int N = Math.Min(sites.Count, intents.Count);
        Console.WriteLine($"gret · grammar-native retrieval · {sites.Count} sites · {intents.Count} intents · eval over first {N}");

        // ── rank-dump short-circuit (before the expensive grammar build) — pure NG-BM25. ──
        if (rankDumpPath != null) { RankDumpBm25(rankDumpPath, sites, intents, topK); return 0; }

        // The grammar is induced over the index content (the sites' text) unless an explicit corpus is given.
        string grammarSrc = grammarCorpusFile != null && File.Exists(grammarCorpusFile) ? string.Join("\n", LoadSites(grammarCorpusFile, delim)) : string.Join("\n", sites);
        var gLower = BuildGrammar(grammarSrc.ToLowerInvariant());
        Console.WriteLine($"  grammar(lower) · {gLower.Rules.Length} rules over {grammarSrc.Length} B  ·  chunk vocab (len≥2): {gLower.Order.Length}  ·  concepts (len≥3, lettered): {gLower.ConceptIds.Count}");

        // ── concept-feature DUMP (for the learned-reranker test — the definitive grammar-as-discriminator probe).
        //    Emits the concept vocabulary (df/depth/len) + per-site + per-intent present-concept sets so an offline
        //    learner can build query↔site concept-match features and try to CROSS BM25. No hand-agg ceiling. ──
        if (dumpPrefix != null) { DumpConcepts(dumpPrefix, sites, intents, N, gLower); return 0; }

        Console.WriteLine();
        Console.WriteLine("  method                    top-1    r@10    r@20     MRR   gold-rank-med");
        Console.WriteLine("  ─────────────────────────────────────────────────────────────────────");

        // ── BM25 family (the term unit is the only variable) ──
        EvalBm25("NG-BM25 (1+2gram)",   sites, intents, N, NgramTokens);
        EvalBm25("GCHUNK-BM25 (lower)",  sites, intents, N, t => ChunkTerms(gLower, t.ToLowerInvariant(), withUnigrams: false));
        EvalBm25("GCHUNK+UNI (lower)",   sites, intents, N, t => ChunkTerms(gLower, t.ToLowerInvariant(), withUnigrams: true));

        // ── concept-overlap family (all-substring presence sets; fragmentation-free) ──
        EvalConcept("GCONCEPT-IDF (lower)",   sites, intents, N, gLower, WeightKinds.Idf);
        EvalConcept("GCONCEPT-DEPTH (lower)", sites, intents, N, gLower, WeightKinds.IdfDepth);
        EvalConcept("GCONCEPT-IDF2 (lower)",  sites, intents, N, gLower, WeightKinds.Idf2);
        EvalConcept("GCONCEPT-RARE (lower)",  sites, intents, N, gLower, WeightKinds.RareOnly);
        EvalConcept("GCONCEPT-COUNT (lower)", sites, intents, N, gLower, WeightKinds.Count);

        // ── FOVEATION family: the reranker LAW (rarest concept discriminates; SUM of idf-mass is a false signal)
        //    turned into a GRAMMAR mechanism — MAX/TOP3 over shared concepts instead of SUM. The frontier-#2 test. ──
        EvalConcept("GCONCEPT-MAXIDF (fov)",   sites, intents, N, gLower, WeightKinds.Idf,      AggKinds.Max);
        EvalConcept("GCONCEPT-TOP3IDF (fov)",  sites, intents, N, gLower, WeightKinds.Idf,      AggKinds.Top3);
        EvalConcept("GCONCEPT-MAXDEPTH (fov)", sites, intents, N, gLower, WeightKinds.IdfDepth, AggKinds.Max);
        EvalConcept("GCONCEPT-MAXLEN (fov)",   sites, intents, N, gLower, WeightKinds.IdfLen,   AggKinds.Max);
        // ── CO-ACTIVATION: PPMI concept-cluster coherence (the meaning organ, retrieval-side). ──
        EvalCoact("GCONCEPT-PPMI (coact)",     sites, intents, N, gLower);

        if (raw)
        {
            var gRaw = BuildGrammar(grammarSrc);
            Console.WriteLine($"  ── raw (case-sensitive) grammar: {gRaw.Rules.Length} rules ──");
            EvalBm25("GCHUNK-BM25 (raw)",  sites, intents, N, t => ChunkTerms(gRaw, t, withUnigrams: false));
            EvalConcept("GCONCEPT-IDF (raw)", sites, intents, N, gRaw, WeightKinds.Idf);
        }

        // ── fragmentation diagnosis: how often does an intent's gold-site share ANY grammar chunk/concept? ──
        Diagnose(sites, intents, N, gLower);
        return 0;
    }

    /// Unfiltered, order-preserving JSON-array load: `["doc0","doc1",...]` -> list, index==position.
    /// Distinct from LoadSites (which drops short blocks + trims): external benchmarks need EVERY
    /// entry at a STABLE index so the caller's doc/query-id map stays aligned. Multi-line entries
    /// (whole-file code queries) are preserved verbatim — the JSON string is the whole intent.
    private static List<string> LoadJsonArray(string path)
        => System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new List<string>();

    // The induced grammar plus the retrieval-side derived tables (expansions, cover order, concept set).
    private sealed class Gram
    {
        public required RePairResult Result;
        public GrammarRule[] Rules => Result.Rules;
        public required byte[][] Exps;        // Exps[i] = full terminal expansion of nonterminal 256+i
        public required int[] Order;          // rule indices sorted by expansion length DESC (greedy-cover order), len≥2
        public required int[] Depth;          // composition depth per rule (1 = terminals only)
        public required List<int> ConceptIds; // rules that read as a "concept": len in [3,40], majority letters
        public required string[] ConceptStr;  // decoded expansion per rule (lowercased-as-induced bytes)
    }

    /// Load the site blocks (block i == site i). Three input shapes, auto-detected (or forced via `delim`):
    ///   *.json        → a JSON array of strings (one code/site per element)
    ///   =====SITE===== → sentinel-delimited (the clean field; methods carry blank lines so \n\n shatters them)
    ///   otherwise      → \n\n-delimited (the legacy big_* benchmark)
    private static List<string> LoadSites(string path, string? delim, bool keepAll = false)
    {
        var raw = File.ReadAllText(path);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw)!.Where(p => keepAll || p.Length > 20).ToList();
        string d = delim ?? (raw.Contains("=====SITE=====") ? "=====SITE=====" : "\n\n");
        var parts = raw.Split(d, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return keepAll ? parts.ToList() : parts.Where(p => p.Length > 20).ToList();
    }

    // ── NG-BM25 rank dump (the trophy floor ranker: 1+2-word-gram BM25, k1=1.5 b=0.3) → per-intent top-K
    //    (site_index:score) TSV, one line per intent, for external qrels-based scoring (BEIR EvaluateRetrieval).
    //    An inverted-free O(intents·sites·qterms) sweep — fine here since grammar induction is skipped. ──
    private static void RankBm25Dump(string prefix, List<string> sites, List<string> intents, int topk)
    {
        const double k1 = 1.5, b = 0.3;
        var tf = sites.Select(p => { var c = new Dictionary<string, int>(); foreach (var t in NgramTokens(p)) c[t] = c.GetValueOrDefault(t) + 1; return c; }).ToList();
        var dl = tf.Select(c => (double)c.Values.Sum()).ToArray();
        // Inverted index: df + postings, so scoring visits only docs sharing a query term — O(matched),
        // not O(sites). This is what makes ~1M-doc corpora (CoIR codesearchnet) tractable; the old O(nS)
        // inner scan extrapolated to ~hours/query-set at that scale. Scores are identical (a non-matching
        // doc contributes exactly 0), and a 0-score doc can never enter top-K.
        var df = new Dictionary<string, int>();
        var postings = new Dictionary<string, List<int>>();
        for (int j = 0; j < tf.Count; j++)
            foreach (var t in tf[j].Keys)
            {
                df[t] = df.GetValueOrDefault(t) + 1;
                if (!postings.TryGetValue(t, out var lst)) { lst = new List<int>(); postings[t] = lst; }
                lst.Add(j);
            }
        int nS = sites.Count;
        double avgdl = nS == 0 ? 1 : Math.Max(1, dl.Average());
        var idf = new Dictionary<string, double>(df.Count);
        foreach (var kv in df) idf[kv.Key] = Math.Log(1 + (nS - kv.Value + 0.5) / (kv.Value + 0.5));

        using var w = new StreamWriter(prefix + ".rank.tsv");
        var sb = new StringBuilder();
        var scores = new Dictionary<int, double>();
        for (int i = 0; i < intents.Count; i++)
        {
            scores.Clear();
            // outer loop over the query's distinct terms → each site accumulates its terms in q-order,
            // parity-identical float sum to the prior per-doc loop; tie-break is ascending site index.
            foreach (var t in NgramTokens(intents[i]).Distinct())
            {
                if (!postings.TryGetValue(t, out var plist)) continue;
                double it = idf[t];
                foreach (int j in plist)
                { int ct = tf[j][t]; scores[j] = scores.GetValueOrDefault(j) + it * (ct * (k1 + 1)) / (ct + k1 * (1 - b + b * dl[j] / avgdl)); }
            }
            sb.Clear(); sb.Append(i);
            foreach (var kv in scores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(topk))
            { sb.Append('\t'); sb.Append(kv.Key); sb.Append(':'); sb.Append(kv.Value.ToString("R")); }
            w.WriteLine(sb.ToString());
            if ((i & 2047) == 0) Trace.Gret.Event("rank", $"{i}/{intents.Count}");   // periodic progress → the elevator, not the report
        }
        Console.WriteLine($"  ranked {intents.Count} intents × {nS} sites (NG-BM25 k1={k1} b={b}, postings) → {prefix}.rank.tsv (top{topk})");
    }

    private static Gram BuildGrammar(string corpus)
    {
        var (_, _, r) = Engine.Induce(Encoding.UTF8.GetBytes(corpus));
        int nr = r.Rules.Length;
        var exps = new byte[nr][];
        var conceptStr = new string[nr];
        for (int i = 0; i < nr; i++) { exps[i] = Expand(r, i); conceptStr[i] = Encoding.UTF8.GetString(exps[i]); }
        var depth = new int[nr];
        for (int i = 0; i < nr; i++)
        {
            int d = 0;
            foreach (var sym in r.Rules[i].Pattern)
                if (sym.Value >= Symbol.FirstNonterminal) { int j = (int)(sym.Value - Symbol.FirstNonterminal); if (j < i) d = Math.Max(d, depth[j]); }
            depth[i] = d + 1;
        }
        var order = Enumerable.Range(0, nr).Where(i => exps[i].Length >= 2)
            .OrderByDescending(i => exps[i].Length).ThenBy(i => i).ToArray();
        var conceptIds = Enumerable.Range(0, nr)
            .Where(i => { var s = conceptStr[i]; return s.Length is >= 3 and <= 40 && s.Count(char.IsLetter) >= s.Length / 2; })
            .ToList();
        return new Gram { Result = r, Exps = exps, Order = order, Depth = depth, ConceptIds = conceptIds, ConceptStr = conceptStr };
    }

    /// Greedy longest-first grammar COVER of `text` → the list of chunk terms (one "R{ruleId}" per placed chunk).
    /// This is cogito's CoverMask segmentation, instrumented to emit which rule covered each region (the parse).
    /// withUnigrams: the uncovered residue is split into whitespace words and emitted as plain terms too.
    private static List<string> ChunkTerms(Gram g, string text, bool withUnigrams)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var covered = new bool[bytes.Length];
        var terms = new List<string>();
        foreach (int ri in g.Order)
        {
            var exp = g.Exps[ri];
            int L = exp.Length;
            for (int i = 0; i + L <= bytes.Length;)
            {
                if (!covered[i] && RegionFree(covered, i, L) && bytes.AsSpan(i, L).SequenceEqual(exp))
                { for (int k = 0; k < L; k++) covered[i + k] = true; terms.Add("R" + ri); i += L; }
                else i++;
            }
        }
        if (withUnigrams)
        {
            int s = -1;
            for (int i = 0; i <= bytes.Length; i++)
            {
                bool boundary = i == bytes.Length || covered[i] || bytes[i] == (byte)' ' || bytes[i] == (byte)'\n' || bytes[i] == (byte)'\t';
                if (!boundary) { if (s < 0) s = i; }
                else { if (s >= 0 && i - s >= 3) terms.Add("u:" + Encoding.UTF8.GetString(bytes, s, i - s)); s = -1; }
            }
        }
        return terms;
    }

    private static bool RegionFree(bool[] covered, int start, int len)
    {
        for (int k = 0; k < len; k++) if (covered[start + k]) return false;
        return true;
    }

    private enum WeightKinds { Count, Idf, IdfDepth, Idf2, RareOnly, IdfLen }

    // How shared-concept weights AGGREGATE into a site score. SUM is the incumbent — and the reranker-law's
    // known-FALSE signal (generic concepts pile up, drowning the one discriminating hit under avg-28 shared).
    // MAX foveates on the single rarest/most-specific matched concept (the law's TRUE signal: "the rarest matched
    // n-gram discriminates"); TOP3 is the soft middle (the k-rarest cluster). Frontier #2's crux, as a knob.
    private enum AggKinds { Sum, Max, Top3 }

    // ── rank-dump: per-intent NG-BM25 full ranking (top-K), for external localization scoring.
    //    Byte-identical BM25 to EvalBm25 (k1=1.5, b=0.3, NgramTokens, .Distinct() first-occurrence
    //    accumulation, stable OrderByDescending → ties break ascending site index) so the dumped
    //    rankings match the parity-gated python engine to the decimal. Emits `intent<TAB>site<TAB>score`
    //    (round-trippable double) for the top-K matched sites per intent. Only sites with score>0 are
    //    emitted (zero-score sites carry no signal and never enter a top-K worth localizing over).
    private static void RankDumpBm25(string outPath, List<string> sites, List<string> intents, int topK)
    {
        const double k1 = 1.5, b = 0.3;
        var tf = sites.Select(p => { var c = new Dictionary<string, int>(); foreach (var t in NgramTokens(p)) c[t] = c.GetValueOrDefault(t) + 1; return c; }).ToList();
        var df = new Dictionary<string, int>();
        foreach (var c in tf) foreach (var t in c.Keys) df[t] = df.GetValueOrDefault(t) + 1;
        double avgdl = tf.Count == 0 ? 1 : Math.Max(1, tf.Average(c => (double)c.Values.Sum()));
        using var w = new StreamWriter(outPath);
        for (int i = 0; i < intents.Count; i++)
        {
            var q = NgramTokens(intents[i]).Distinct().ToList();
            var ranked = Enumerable.Range(0, sites.Count).Select(j =>
            {
                var c = tf[j]; double dl = c.Values.Sum();
                double rel = q.Where(c.ContainsKey).Sum(t => Math.Log(1 + (sites.Count - df[t] + 0.5) / (df[t] + 0.5)) * (c[t] * (k1 + 1)) / (c[t] + k1 * (1 - b + b * dl / avgdl)));
                return (J: j, Rel: rel);
            }).Where(x => x.Rel > 0).OrderByDescending(x => x.Rel).Take(topK).ToList();
            foreach (var x in ranked) w.WriteLine($"{i}\t{x.J}\t{x.Rel:R}");
        }
        Console.WriteLine($"  rankdump · {intents.Count} intents × top-{topK} over {sites.Count} sites → {outPath}");
    }

    // ── BM25 evaluator: build a TF index from `tokenizer`, rank each intent, score the gold rank ──
    private static void EvalBm25(string name, List<string> sites, List<string> intents, int N, Func<string, List<string>> tokenizer)
    {
        const double k1 = 1.5, b = 0.3;   // matches `bench` (equal-ish doc2query blocks → low length-norm)
        var tf = sites.Select(p => { var c = new Dictionary<string, int>(); foreach (var t in tokenizer(p)) c[t] = c.GetValueOrDefault(t) + 1; return c; }).ToList();
        var df = new Dictionary<string, int>();
        foreach (var c in tf) foreach (var t in c.Keys) df[t] = df.GetValueOrDefault(t) + 1;
        double avgdl = tf.Count == 0 ? 1 : Math.Max(1, tf.Average(c => (double)c.Values.Sum()));

        var goldRanks = new List<int>();
        for (int i = 0; i < N; i++)
        {
            var q = tokenizer(intents[i]).Distinct().ToList();
            var ranked = Enumerable.Range(0, sites.Count).Select(j =>
            {
                var c = tf[j]; double dl = c.Values.Sum();
                double rel = q.Where(c.ContainsKey).Sum(t => Math.Log(1 + (sites.Count - df[t] + 0.5) / (df[t] + 0.5)) * (c[t] * (k1 + 1)) / (c[t] + k1 * (1 - b + b * dl / avgdl)));
                return (J: j, Rel: rel);
            }).OrderByDescending(x => x.Rel).ToList();
            goldRanks.Add(ranked.FindIndex(x => x.J == i) + 1);
        }
        PrintRow(name, goldRanks, N);
    }

    // ── concept-overlap evaluator: presence SET of grammar concepts per text (all-substring, overlap-allowed) ──
    private static void EvalConcept(string name, List<string> sites, List<string> intents, int N, Gram g, WeightKinds wk, AggKinds agg = AggKinds.Sum)
    {
        // Per-concept document frequency, for IDF. A concept is "present" iff its expansion is a substring.
        var sitePresent = sites.Select(p => PresentConcepts(g, p.ToLowerInvariant())).ToList();
        var df = new int[g.Rules.Length];
        foreach (var set in sitePresent) foreach (int ci in set) df[ci]++;

        double Idf(int ci) => Math.Log(1 + (sites.Count - df[ci] + 0.5) / (df[ci] + 0.5));
        double Weight(int ci) => wk switch
        {
            WeightKinds.Count => 1.0,
            WeightKinds.Idf => Idf(ci),
            WeightKinds.IdfDepth => Idf(ci) * g.Depth[ci],
            WeightKinds.Idf2 => Idf(ci) * Idf(ci),                              // square IDF → punish generic concepts harder
            WeightKinds.RareOnly => df[ci] <= sites.Count / 4 ? Idf(ci) : 0.0,  // hard stoplist: drop concepts in >25% of sites
            WeightKinds.IdfLen => Idf(ci) * g.ConceptStr[ci].Length,            // longer concept = more specific match ("light years" > "light")
            _ => 1.0,
        };

        var goldRanks = new List<int>();
        var buf = new List<double>();
        for (int i = 0; i < N; i++)
        {
            var q = PresentConcepts(g, intents[i].ToLowerInvariant());
            var ranked = Enumerable.Range(0, sites.Count).Select(j =>
            {
                double rel;
                if (agg == AggKinds.Sum) { rel = 0; foreach (int ci in q) if (sitePresent[j].Contains(ci)) rel += Weight(ci); }
                else
                {
                    buf.Clear(); foreach (int ci in q) if (sitePresent[j].Contains(ci)) buf.Add(Weight(ci));
                    if (buf.Count == 0) rel = 0;
                    else if (agg == AggKinds.Max) { rel = buf[0]; for (int t = 1; t < buf.Count; t++) if (buf[t] > rel) rel = buf[t]; }
                    else { buf.Sort((x, y) => y.CompareTo(x)); rel = 0; for (int t = 0; t < Math.Min(3, buf.Count); t++) rel += buf[t]; }  // Top3
                }
                return (J: j, Rel: rel);
            }).OrderByDescending(x => x.Rel).ToList();
            goldRanks.Add(ranked.FindIndex(x => x.J == i) + 1);
        }
        PrintRow(name, goldRanks, N);
    }

    // ── co-activation evaluator: concepts that co-occur across sites (PPMI) form COHERENT clusters. A query's
    // shared concept votes weighted by IDF AND by how strongly it co-activates with the query's OTHER shared
    // concepts — a tight concept-cluster (galaxy·star·orbit) outscores scattered generic hits. The retrieval
    // analogue of the couplings "meaning organ" (Couplings.cs), over site co-occurrence not symbol adjacency.
    private static void EvalCoact(string name, List<string> sites, List<string> intents, int N, Gram g)
    {
        int nS = sites.Count;
        var sitePresent = sites.Select(p => PresentConcepts(g, p.ToLowerInvariant())).ToList();
        var df = new int[g.Rules.Length];
        foreach (var set in sitePresent) foreach (int ci in set) df[ci]++;
        double Idf(int ci) => Math.Log(1 + (nS - df[ci] + 0.5) / (df[ci] + 0.5));

        // Pairwise co-document-count over the site collection → PPMI(a,b)=max(0, log( co·N / (df[a]·df[b]) )).
        var pairCo = new Dictionary<long, int>();
        foreach (var set in sitePresent)
        {
            var arr = set.ToArray(); Array.Sort(arr);
            for (int a = 0; a < arr.Length; a++) for (int b = a + 1; b < arr.Length; b++)
            { long key = ((long)arr[a] << 32) | (uint)arr[b]; pairCo[key] = pairCo.GetValueOrDefault(key) + 1; }
        }
        double Ppmi(int a, int b)
        {
            if (a == b) return 0;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            int co = pairCo.GetValueOrDefault(((long)lo << 32) | (uint)hi);
            if (co == 0) return 0;
            double v = Math.Log((double)co * nS / ((double)df[a] * df[b]));
            return v > 0 ? v : 0;
        }

        var goldRanks = new List<int>();
        for (int i = 0; i < N; i++)
        {
            var q = PresentConcepts(g, intents[i].ToLowerInvariant()).ToArray();
            var ranked = Enumerable.Range(0, nS).Select(j =>
            {
                double rel = 0;
                foreach (int ci in q) if (sitePresent[j].Contains(ci))
                {
                    double coh = 0; int m = 0;
                    foreach (int cj in q) if (cj != ci && sitePresent[j].Contains(cj)) { coh += Ppmi(ci, cj); m++; }
                    rel += Idf(ci) * (1.0 + (m > 0 ? coh / m : 0.0));
                }
                return (J: j, Rel: rel);
            }).OrderByDescending(x => x.Rel).ToList();
            goldRanks.Add(ranked.FindIndex(x => x.J == i) + 1);
        }
        PrintRow(name, goldRanks, N);
    }

    /// The set of concept rule-ids whose expansion occurs (as a substring, overlaps allowed) in `text`.
    private static HashSet<int> PresentConcepts(Gram g, string text)
    {
        var set = new HashSet<int>();
        foreach (int ci in g.ConceptIds) if (text.Contains(g.ConceptStr[ci], StringComparison.Ordinal)) set.Add(ci);
        return set;
    }

    /// Dump the concept vocabulary + per-site/per-intent presence sets → three TSVs an offline learner reads to
    /// build query↔site concept-match feature vectors (the learned-reranker test: can LEARNING over concept
    /// features cross BM25, or is the concept unit intrinsically sub-discriminative regardless of aggregation?).
    private static void DumpConcepts(string prefix, List<string> sites, List<string> intents, int N, Gram g)
    {
        var present = sites.Select(p => PresentConcepts(g, p.ToLowerInvariant())).ToList();
        var df = new int[g.Rules.Length];
        foreach (var set in present) foreach (int ci in set) df[ci]++;
        using (var w = new StreamWriter(prefix + ".concepts.tsv"))
            foreach (int ci in g.ConceptIds)
                w.WriteLine($"{ci}\t{df[ci]}\t{g.Depth[ci]}\t{g.ConceptStr[ci].Length}\t{g.ConceptStr[ci].Replace('\t', ' ').Replace('\n', ' ')}");
        using (var w = new StreamWriter(prefix + ".sites.tsv"))
            for (int j = 0; j < sites.Count; j++)
                w.WriteLine($"{j}\t{string.Join(' ', present[j])}");
        using (var w = new StreamWriter(prefix + ".intents.tsv"))
            for (int i = 0; i < N; i++)
                w.WriteLine($"{i}\t{string.Join(' ', PresentConcepts(g, intents[i].ToLowerInvariant()))}");
        Console.WriteLine($"  dumped {g.ConceptIds.Count} concepts · {sites.Count} sites · {N} intents → {prefix}.{{concepts,sites,intents}}.tsv");
    }

    private static void PrintRow(string name, List<int> goldRanks, int N)
    {
        int top1 = goldRanks.Count(r => r == 1), r10 = goldRanks.Count(r => r is >= 1 and <= 10), r20 = goldRanks.Count(r => r is >= 1 and <= 20);
        double mrr = goldRanks.Where(r => r >= 1).Sum(r => 1.0 / r) / Math.Max(1, N);
        var sorted = goldRanks.Where(r => r >= 1).OrderBy(x => x).ToList();
        double med = sorted.Count == 0 ? double.NaN : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
        Console.WriteLine($"  {name,-24}{100.0 * top1 / N,5:F0}%  {100.0 * r10 / N,5:F0}%  {100.0 * r20 / N,5:F0}%  {mrr,6:F3}   {med,7:F0}");
    }

    /// Fragmentation/coverage diagnosis: the ceiling for grammar retrieval is "does the gold site even SHARE a
    /// grammar term with the intent?" — if not, no ranker can recover it. Reports the recall ceiling per term unit.
    private static void Diagnose(List<string> sites, List<string> intents, int N, Gram g)
    {
        int chunkShare = 0, conceptShare = 0, ngramShare = 0;
        double avgQChunks = 0, avgQConcepts = 0;
        for (int i = 0; i < N; i++)
        {
            var qChunks = new HashSet<string>(ChunkTerms(g, intents[i].ToLowerInvariant(), withUnigrams: false));
            var sChunks = new HashSet<string>(ChunkTerms(g, sites[i].ToLowerInvariant(), withUnigrams: false));
            if (qChunks.Overlaps(sChunks)) chunkShare++;
            avgQChunks += qChunks.Count;

            var qCon = PresentConcepts(g, intents[i].ToLowerInvariant());
            var sCon = PresentConcepts(g, sites[i].ToLowerInvariant());
            if (qCon.Overlaps(sCon)) conceptShare++;
            avgQConcepts += qCon.Count;

            var qNg = new HashSet<string>(NgramTokens(intents[i]));
            var sNg = new HashSet<string>(NgramTokens(sites[i]));
            if (qNg.Overlaps(sNg)) ngramShare++;
        }
        Console.WriteLine();
        Console.WriteLine("  ── share ceiling (does the gold site share ≥1 term with its intent? no share ⟹ unrecoverable) ──");
        Console.WriteLine($"    n-gram (1+2)   gold-site shares a term: {ngramShare}/{N} = {100.0 * ngramShare / N:F0}%");
        Console.WriteLine($"    grammar-chunk  gold-site shares a chunk: {chunkShare}/{N} = {100.0 * chunkShare / N:F0}%  (avg {avgQChunks / N:F1} chunks/intent)");
        Console.WriteLine($"    grammar-concept gold-site shares a concept: {conceptShare}/{N} = {100.0 * conceptShare / N:F0}%  (avg {avgQConcepts / N:F1} concepts/intent)");

        // ── discrimination diagnosis: is concept-presence near-UNIVERSAL? (the "covers 100% but can't rank" disease).
        //    A concept in >25% of sites carries ~no discriminating signal; if the avg matched concept is generic,
        //    SUM-scoring drowns the rare one. Reports the DF spread + the generic-fraction of a typical match set. ──
        var present = sites.Select(p => PresentConcepts(g, p.ToLowerInvariant())).ToList();
        var df = new int[g.Rules.Length];
        foreach (var set in present) foreach (int ci in set) df[ci]++;
        int gt50 = g.ConceptIds.Count(ci => df[ci] > N / 2), gt25 = g.ConceptIds.Count(ci => df[ci] > N / 4),
            gt10 = g.ConceptIds.Count(ci => df[ci] > N / 10), df1 = g.ConceptIds.Count(ci => df[ci] == 1);
        // Generic fraction of a typical intent∩site match set: of the concepts the intent shares with its gold site,
        // how many are generic (df>25%)? High ⟹ the discriminating concept is a needle summed under generic hay.
        double genFrac = 0; int cnt = 0;
        for (int i = 0; i < N; i++)
        {
            var q = PresentConcepts(g, intents[i].ToLowerInvariant());
            int shared = 0, gen = 0;
            foreach (int ci in q) if (present[i].Contains(ci)) { shared++; if (df[ci] > N / 4) gen++; }
            if (shared > 0) { genFrac += (double)gen / shared; cnt++; }
        }
        Console.WriteLine($"  ── concept-DF spread over {g.ConceptIds.Count} concepts (discrimination signal): "
            + $"df>50%: {gt50} · df>25%: {gt25} · df>10%: {gt10} · df==1 (unique): {df1}");
        Console.WriteLine($"     typical intent↔gold match set is {100.0 * genFrac / Math.Max(1, cnt):F0}% GENERIC (df>25%) concepts — the needle-in-hay that SUM-scoring drowns");
    }
}
