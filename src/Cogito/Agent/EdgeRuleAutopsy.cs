namespace Cogito;

using System.Text;
using System.Text.Json;
using Cogito.Codec;    // Fixed.Log2 — the integer milli-bit log the whole MDL stack rides
using Cogito.Grammar;  // GrammarRule / RuleBodyKind / Symbol
using Cogito.Induct;   // RePairResult

// ── EDGE-RULE AUTOPSY (Arm R, the DECISIVE Tier-A test) ──  the offline differential-COMPRESSION discrimination probe at
// GRAMMAR-RULE grain. READ-ONLY science on DEAD DATA: it re-ranks the BANKED 300-instance navigate results, blends a
// differential "Edge" scored over per-file RULE-fire counts (not token counts), and scores the re-ranking against gold.
// Mints nothing, changes no engine behavior; the grammar is the engine's daily verb (Engine.Induce over the candidate
// corpus) and the measure rides Fixed.Log2 (the canonical integer MDL primitive).
//
// WHY TOKEN GRAIN FAILED, WHY RULE GRAIN MIGHT NOT (the whole thread, one paragraph): H2/H2′ metered CONCENTRATION over
// query-TOKEN counts. Token concentration is dominated by rarity — idf (pure global rarity) beat the concave differential
// at token grain (idf +5.7% vs edge +1.7%/+3.x%). But the token count was never the organ: cogito's actual discrimination
// engine meters grammar-RULE usage. Rules
// are SPARSE and STRUCTURE-SPECIFIC — a rule can be corpus-common yet fire densely in ONE home file, manufacturing a
// concentration signal idf cannot see (idf sees term rarity, not where composition concentrates). Rules also fire ONCE
// per maximal match (greedy longest-first cover ⇒ nesting-immune BY CONSTRUCTION — the module⊃class⊃method triple-count
// that plagued token grain cannot arise). This arm is what the whole Tier-A thread was built to answer.
//
// THE MEASURE: induce the grammar over the instance's candidate module docs; per candidate FILE, tally how
// many times each rule WINS a maximal cover position (nesting-immune fire count). For rule r with home file h (the file it
// fires in most): Edge(r) = S(count_h) − S(count_complement), S(k)=log₂(1+k) (Fixed.Log2, integer mbits), complement =
// total_fires − count_h. A rule concentrated in ONE file → high Edge; a hub rule spread across files (count_h ≈ complement)
// → the concave complement saving CANCELS → ~0. File f's EdgeScore = Σ over rules whose HOME is f of Edge(r): a file scores
// high iff it CONTAINS distinctive/concentrated rules. Rerank: newScore(f) = norm(base_BM25) + λ·norm(EdgeScore).
//
// PRE-REGISTERED KILL-LINE (three clean outcomes):
//   BEATS idf  — commit(edge)−base > +5.7% AND max_λ gap-closure ≥ 0.5 AND shuffled-home stays flat → Tier A VALIDATED,
//                BUILD the 5b organ (the Pearl-audit per-source RULE counts + value-layer wiring).
//   MATCHES idf — within ±2pt → the discrimination axis is RARITY even at rule grain; use idf, don't build the organ.
//   FAILS      — shuffled-home ALSO closes the gap, or edge < idf → differential-MDL discrimination is DEAD at every grain;
//                the sharpness axis is something else entirely (rethink).
public static class EdgeRuleAutopsy
{
    const int MinTermLen     = Loc.MinTermLen;      // len≥4 query-term filter (parity with the nav minted-term floor + the token idf null)
    const int MaxCorpusBytes = 2_000_000;           // induction diet guard — candidate module docs (≤12 files) almost never approach this
    static readonly double[] Lambdas = [0.0, 0.25, 0.5, 1.0, 2.0];   // the blend sweep (identical to H2′ — λ=0 is the identity gate)

    // ── one banked instance: the reconstructed base ranking + the reached (recognition) set ──
    sealed record Inst(string Id, string Repo, List<string> Visited, List<(string Path, double Score)> Sites);

    // per-instance scored candidate: the base rank + every arm's raw score over the candidate (reached) set
    sealed class Scored
    {
        public required string Id;
        public required string Repo;
        public required List<string> Cands;                  // candidate files in base-rank order (AggregateMaxFiles)
        public required Dictionary<string, double> Base;     // base file score (aggregate-max)
        public required Dictionary<string, int> BaseRank;    // base ordinal (0 = base top-1) — the identity tie-break
        public required Dictionary<string, long> Edge;       // RULE-grain concave differential (integer mbits) — the organ candidate (R: distributional concentration)
        public required Dictionary<string, long> Rpp;        // R″ CAUSAL Edge: Σ_{home r=f} ΔparsedSize(remove r, re-cover home) — load-bearing-ness (counterfactual contribution)
        public required Dictionary<string, long> Rprime;     // R′ query-touched Edge: Σ_{home r=f, expansion∋qterm} Edge(r) — relevance × concentration composed
        public required Dictionary<string, long> Ridf;       // rule-idf: Σ_{home r=f} rarity(r) — rule RARITY without concentration (the rule-grain rarity control)
        public required Dictionary<string, long> ShufEdge;   // shuffled-home rule-Edge (the differential NULL — same Edge magnitudes, scrambled homes)
        public required Dictionary<string, double> Idf;      // token-idf Σ ln(N/df) — the refuted +5.7% null, reproduced EXACTLY for the bar
        public required Dictionary<string, double> Rand;     // deterministic pseudo-random — the floor
        public required HashSet<string> Gold;                // scoring only, NEVER a re-rank input
        public bool Recognized;                              // gold ∩ visited ≠ ∅ (the recognition ceiling)
        // per-instance diagnostics (regime read)
        public int CorpusBytes; public int RuleCount; public int FiringRules;
    }

    public static int Run(string rankingsPath, string dataDir, string outDir, int limit)
    {
        if (!File.Exists(rankingsPath)) { Console.Error.WriteLine($"  rankings not found: {rankingsPath}"); return 1; }
        if (!Directory.Exists(dataDir)) { Console.Error.WriteLine($"  data dir not found: {dataDir}"); return 1; }
        Directory.CreateDirectory(outDir);

        var insts = LoadRankings(rankingsPath, limit);
        Console.WriteLine($"edge-rule-autopsy · {insts.Count} banked instances · data {dataDir} · GRAIN=grammar-rule · λ∈{{{string.Join(",", Lambdas)}}}");

        var scored = new List<Scored>(insts.Count);
        int done = 0;
        foreach (var inst in insts)
        {
            var dir = Path.Combine(dataDir, inst.Id);
            var s = ScoreInstance(inst, dir);
            if (s is not null) scored.Add(s);
            if (++done % 25 == 0) Console.WriteLine($"  scored {done}/{insts.Count}");
        }
        Console.WriteLine($"  scored {scored.Count}/{insts.Count} (dropped {insts.Count - scored.Count} — missing/empty data dir)");

        var report = Analyze(scored);
        RenderSummary(report, scored, dataDir, Path.Combine(outDir, "summary.md"));
        RenderFlips(report, scored, Path.Combine(outDir, "flips.md"));
        Console.WriteLine(report.Console);
        Console.WriteLine($"  rendered → {Path.Combine(outDir, "summary.md")} · {Path.Combine(outDir, "flips.md")}");
        return 0;
    }

    // ── INPUT 1: navigate_rankings.jsonl → the base ranking + reached set per instance (byte-identical to H2′) ──
    static List<Inst> LoadRankings(string path, int limit)
    {
        var list = new List<Inst>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            string id = r.GetProperty("instance_id").GetString()!;
            var visited = new List<string>();
            foreach (var v in r.GetProperty("visited_files").EnumerateArray()) visited.Add(v.GetString()!);
            var sites = new List<(string, double)>();
            foreach (var e in r.GetProperty("local_fn_sites").EnumerateArray())
            {
                var a = e.EnumerateArray().ToArray();   // [path, name, start, end, score]
                sites.Add((a[0].GetString()!, a[4].GetDouble()));
            }
            list.Add(new Inst(id, Loc.RepoOf(id), visited, sites));
            if (limit > 0 && list.Count >= limit) break;
        }
        return list;
    }

    // ── the per-instance rule-Edge scoring ──
    static Scored? ScoreInstance(Inst inst, string dir)
    {
        var sitesPath = Path.Combine(dir, "sites.jsonl");
        var queryPath = Path.Combine(dir, "query.txt");
        if (!File.Exists(sitesPath) || !File.Exists(queryPath)) return null;

        // base file ranking over the reached set (aggregate-max of local_fn_sites — exactly Loc/NavLoop's law)
        var baseSites = inst.Sites.Select(s => new Site(s.Path, "", "", 0, 0, "")).ToList();
        var (baseOrder, baseScore) = Loc.AggregateMaxFiles(baseSites, inst.Sites.Select(s => s.Score).ToArray());
        if (baseOrder.Count == 0) return null;
        var baseRank = new Dictionary<string, int>();
        for (int i = 0; i < baseOrder.Count; i++) baseRank[baseOrder[i]] = i;
        var candSet = new HashSet<string>(baseOrder, StringComparer.Ordinal);

        // query terms (Loc.Toks, distinct, len≥4) — for the token-idf null ONLY (the rule arm needs no query terms)
        var qterms = Loc.Toks(File.ReadAllText(queryPath)).Where(t => t.Length >= MinTermLen).Distinct().ToList();
        var querySet = new HashSet<string>(qterms, StringComparer.Ordinal);

        // ── ONE scan over the MODULE docs (kind=="module" — one doc per FILE, spanning the whole file: nesting-immune) ──
        //   candidate module texts → the induction corpus + the per-file rule-fire metering surface;
        //   token corpusCount / dfFiles / fileTokCount over ALL module docs → the token-idf null, reproduced byte-for-byte
        //   from H2′ (N = distinct module files, df = distinct module docs carrying t) so the +5.7% bar is apples-comparable.
        var candModText = new Dictionary<string, byte[]>(StringComparer.Ordinal);   // candidate file → its module-doc bytes
        var corpusCount = new Dictionary<string, long>(StringComparer.Ordinal);     // module-only term mass (all files)
        var dfFiles     = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);  // distinct module docs carrying t
        foreach (var t in qterms) { corpusCount[t] = 0; dfFiles[t] = new HashSet<string>(StringComparer.Ordinal); }
        var fileTokCount = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);   // candidate home token density
        foreach (var f in baseOrder) fileTokCount[f] = new Dictionary<string, int>(StringComparer.Ordinal);
        var moduleFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var site in Loc.LoadSites(sitesPath))
        {
            if (site.Kind != "module") continue;              // module docs only — one per file, no class/method nesting
            moduleFiles.Add(site.Path);
            bool isCand = candSet.Contains(site.Path);
            if (isCand && !candModText.ContainsKey(site.Path)) candModText[site.Path] = Encoding.UTF8.GetBytes(site.Text);
            var ftc = isCand ? fileTokCount[site.Path] : null;
            foreach (var t in Loc.Toks(site.Text))
            {
                if (!querySet.Contains(t)) continue;
                corpusCount[t]++;
                dfFiles[t].Add(site.Path);
                if (ftc is not null) ftc[t] = ftc.GetValueOrDefault(t) + 1;
            }
        }
        int n = moduleFiles.Count;                            // distinct files (module docs) — the honest N for the idf null
        if (candModText.Count == 0) return null;              // no module docs for any candidate → not the SWE-bench plane
        int dfCap = n / 4;                                    // DfCapFrac 0.25 (Gret's stoplist), integer
        var T = qterms.Where(t => dfFiles[t].Count <= dfCap).ToList();

        // ── INDUCE the grammar over the candidate module docs (the engine's daily verb, batch Re-Pair via the loom —
        //    verify-loom proves loom-batch ≡ RePair byte-for-byte). '\n'-joined so rules never straddle a line barrier
        //    (Engine.Induce's law) — the induced rules are within-line identifier/phrase structure, exactly the grain. ──
        var corpusBuf = new List<byte>();
        foreach (var f in baseOrder)   // base-rank order → the top candidates always seed the grammar under the diet cap
        {
            if (!candModText.TryGetValue(f, out var txt)) continue;
            if (corpusBuf.Count + txt.Length > MaxCorpusBytes) break;
            corpusBuf.AddRange(txt); corpusBuf.Add((byte)'\n');
        }
        var rules = Engine.Induce(corpusBuf.ToArray()).Result.Rules;

        // ── per candidate FILE: RULE-fire counts via the greedy longest-first NESTING-IMMUNE cover (each byte belongs to
        //    exactly one maximal rule ⇒ a rule fires once per maximal match, never summed over containment). ──
        var basis = BuildBasis(rules);
        var fileFire = new Dictionary<string, Dictionary<int, long>>(StringComparer.Ordinal);
        foreach (var f in baseOrder)
            fileFire[f] = candModText.TryGetValue(f, out var txt) ? RuleFires(txt, basis) : new Dictionary<int, long>();

        // ── per RULE: total fires, home file (argmax fire, tie-break base-rank asc then path), concave differential Edge,
        //    and rule-df (candidate files it fires in — for the rule-rarity control). Dead rules (0 fires) are skipped. ──
        long log2Ncand = Fixed.Log2((uint)Math.Max(1, baseOrder.Count)).Value;
        var ruleEdge  = new Dictionary<int, long>();          // rule idx → Edge (mbits)
        var ruleHome  = new Dictionary<int, string>();        // rule idx → home file
        var ruleRarity = new Dictionary<int, long>();         // rule idx → idf-mbits over the candidate set
        for (int i = 0; i < rules.Length; i++)
        {
            long total = 0; string? home = null; long homeCount = 0; int homeRank = int.MaxValue; int ruleDf = 0;
            foreach (var f in baseOrder)
            {
                if (!fileFire[f].TryGetValue(i, out var c) || c == 0) continue;
                total += c; ruleDf++;
                int rk = baseRank[f];
                if (c > homeCount || (c == homeCount && rk < homeRank)) { homeCount = c; home = f; homeRank = rk; }
            }
            if (home is null || total == 0) continue;         // a rule that never won a maximal match — dead weight
            long compl = total - homeCount;
            long sHome  = Fixed.Log2((uint)(homeCount + 1)).Value;
            long sCompl = Fixed.Log2((uint)(compl + 1)).Value;
            ruleEdge[i] = sHome - sCompl;                     // CONCAVE differential: concentrated → high, hub (home≈compl) → ~0
            ruleHome[i] = home;
            ruleRarity[i] = Math.Max(0, log2Ncand - Fixed.Log2((uint)Math.Max(1, ruleDf)).Value);
        }

        // ── file EdgeScore = Σ over rules whose HOME is f of Edge(r); rule-idf score = Σ over home-f rules of rarity(r) ──
        var edge = new Dictionary<string, long>(StringComparer.Ordinal);
        var ridf = new Dictionary<string, long>(StringComparer.Ordinal);
        var rand = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in baseOrder) { edge[f] = 0; ridf[f] = 0; rand[f] = Rand01(inst.Id + "|" + f); }
        foreach (var (i, e) in ruleEdge) { edge[ruleHome[i]] += e; ridf[ruleHome[i]] += ruleRarity[i]; }

        // ── R″ (CAUSAL Edge) + R′ (query-touched Edge) — the ablation arms. R measured
        //    distributional concentration; R″ measures LOAD-BEARING-NESS: per firing rule, the extra cover-symbols its
        //    home file suffers when the rule is REMOVED and its OWN maximal-match territory must be re-covered by the
        //    rest of the basis. A rule concentrated yet REDUNDANT (a sibling covers its window on removal) → Δ≈0; spread
        //    yet IRREPLACEABLE (its window shatters into lone bytes) → Δ large. The execution-flavored Edge available
        //    BEFORE Whorl C's native meters. (LOCAL by construction: re-covering each fire's window in isolation is the
        //    per-rule MARGINAL contribution — and O(home bytes) total, vs an O(home-rules × file) full-file re-parse the
        //    grammar's rule count makes intractable at 300 instances.) R′ composes relevance × concentration: Edge(r),
        //    but ONLY for rules whose expansion touches a query term. ──
        var expByIdx = new Dictionary<int, byte[]>(basis.Exps.Length);
        foreach (var (exp, idx) in basis.Exps) expByIdx[idx] = exp;
        var trace = new Dictionary<string, Dictionary<int, List<(int Pos, int Len)>>>(StringComparer.Ordinal);   // per file: winning-rule → its maximal-match windows
        foreach (var f in baseOrder) trace[f] = candModText.TryGetValue(f, out var txt) ? CoverTrace(txt, basis) : new();
        var rpp = new Dictionary<string, long>(StringComparer.Ordinal);
        var rprime = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var f in baseOrder) { rpp[f] = 0; rprime[f] = 0; }
        foreach (var (i, home) in ruleHome)
        {
            if (candModText.TryGetValue(home, out var htext) && trace[home].TryGetValue(i, out var wins))
                foreach (var (pos, len) in wins) rpp[home] += LocalReDelta(htext, pos, len, basis, i);   // ≥ 0: the extra symbols to re-cover this window without rule i
            if (expByIdx.TryGetValue(i, out var exp))
            {
                string el = Encoding.UTF8.GetString(exp).ToLowerInvariant();                      // Loc.Toks lowercases; match the qterm casing
                foreach (var t in qterms) if (el.Contains(t, StringComparison.Ordinal)) { rprime[home] += ruleEdge[i]; break; }
            }
        }

        // ── shuffled-home NULL: keep every rule's Edge magnitude, PERMUTE which file gets credited (the home-label vector
        //    permuted across rules — each file stays home to the same NUMBER of rules, a random subset). If this ALSO
        //    closes the gap, the signal is MAGNITUDE (rules-per-file mass), not the differential (right rule → right file). ──
        var shufEdge = ShuffledHome(inst.Id, baseOrder, ruleEdge, ruleHome);

        // ── token-idf null (the refuted +5.7% bar), reproduced EXACTLY from H2′ ──
        var idf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in baseOrder)
        {
            var ftc = fileTokCount[f]; double id = 0;
            foreach (var t in T) if (ftc.GetValueOrDefault(t) >= 1) id += Math.Log((double)n / dfFiles[t].Count);
            idf[f] = id;
        }

        var gold = Loc.LoadGold(Path.Combine(dir, "gold.json")).Files;
        bool recognized = inst.Visited.Any(gold.Contains);

        return new Scored
        {
            Id = inst.Id, Repo = inst.Repo,
            Cands = baseOrder, Base = baseScore, BaseRank = baseRank,
            Edge = edge, Rpp = rpp, Rprime = rprime, Ridf = ridf, ShufEdge = shufEdge, Idf = idf, Rand = rand,
            Gold = gold, Recognized = recognized,
            CorpusBytes = corpusBuf.Count, RuleCount = rules.Length, FiringRules = ruleEdge.Count,
        };
    }

    // ── the id-carrying cover BASIS: (expansion, ruleIdx) for every rule (len≥2), sorted (len desc, bytes asc, idx asc) —
    //    the greedy longest-first maximal-cover order, WITH rule identity retained (GrammarCover discards it), plus the
    //    2-byte-prefix buckets so the per-position probe scans one bucket. The idx tie-break makes byte-identical
    //    expansions deterministic (lowest rule wins the fire). ──
    sealed class Basis
    {
        public required (byte[] Exp, int Idx)[] Exps;
        public required Dictionary<int, (byte[] Exp, int Idx)[]> ByPrefix;
    }

    static Basis BuildBasis(GrammarRule[] rules)
    {
        var exps = new List<(byte[] Exp, int Idx)>(rules.Length);
        for (int i = 0; i < rules.Length; i++)
        {
            if (rules[i].Kind != RuleBodyKind.Expansion && rules[i].Kind != RuleBodyKind.SlotClass && rules[i].Kind != RuleBodyKind.TapeRef) continue;
            var e = Reconstruct.Expand(rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            if (e.Length >= 2) exps.Add((e, i));
        }
        exps.Sort((a, b) =>
        {
            if (a.Exp.Length != b.Exp.Length) return b.Exp.Length - a.Exp.Length;   // longest first (greedy maximal cover)
            for (int k = 0; k < a.Exp.Length; k++) if (a.Exp[k] != b.Exp[k]) return a.Exp[k] - b.Exp[k];   // bytes asc (build-invariant)
            return a.Idx - b.Idx;                                                    // idx asc — deterministic on byte-identical expansions
        });
        var buckets = new Dictionary<int, List<(byte[], int)>>();
        foreach (var (e, idx) in exps)
        {
            int key = (e[0] << 8) | e[1];
            (buckets.TryGetValue(key, out var l) ? l : buckets[key] = new()).Add((e, idx));
        }
        var byPrefix = new Dictionary<int, (byte[] Exp, int Idx)[]>(buckets.Count);
        foreach (var (k, l) in buckets) byPrefix[k] = l.ToArray();
        return new Basis { Exps = exps.ToArray(), ByPrefix = byPrefix };
    }

    // greedy longest-first NON-OVERLAPPING cover — mirrors Engine.GrammarCover.ParsedSize's scan exactly, but records the
    // WINNING rule per maximal match (the bucket is length-desc, so the first match is the longest — the greedy winner).
    // Uncovered bytes are lone symbols (no rule fired), never tallied. Returns rule idx → fire count for this text.
    static Dictionary<int, long> RuleFires(byte[] text, Basis basis)
    {
        var counts = new Dictionary<int, long>();
        int i = 0;
        while (i < text.Length)
        {
            int bestLen = 0, bestIdx = -1;
            if (text.Length - i >= 2 && basis.ByPrefix.TryGetValue((text[i] << 8) | text[i + 1], out var cands))
                foreach (var (exp, idx) in cands)
                    if (exp.Length <= text.Length - i && text.AsSpan(i, exp.Length).SequenceEqual(exp)) { bestLen = exp.Length; bestIdx = idx; break; }
            if (bestIdx >= 0) { counts[bestIdx] = counts.GetValueOrDefault(bestIdx) + 1; i += bestLen; }
            else i += 1;                                       // uncovered byte — one lone symbol, no rule fired
        }
        return counts;
    }

    // the greedy longest-first cover, KEEPING each winning rule's positions (RuleFires records only counts). Returns
    // rule idx → the list of (pos, len) windows it won — the R″ re-cover targets. Same scan/winner as RuleFires and
    // Engine.GrammarCover.ParsedSize; uncovered lone bytes are dropped (no rule fired there).
    static Dictionary<int, List<(int Pos, int Len)>> CoverTrace(byte[] text, Basis basis)
    {
        var trace = new Dictionary<int, List<(int, int)>>();
        int i = 0;
        while (i < text.Length)
        {
            int bestLen = 0, bestIdx = -1;
            if (text.Length - i >= 2 && basis.ByPrefix.TryGetValue((text[i] << 8) | text[i + 1], out var cands))
                foreach (var (exp, idx) in cands)
                    if (exp.Length <= text.Length - i && text.AsSpan(i, exp.Length).SequenceEqual(exp)) { bestLen = exp.Length; bestIdx = idx; break; }
            if (bestIdx >= 0) { (trace.TryGetValue(bestIdx, out var l) ? l : trace[bestIdx] = new()).Add((i, bestLen)); i += bestLen; }
            else i += 1;
        }
        return trace;
    }

    // the LOCAL counterfactual: re-cover the single window [pos, pos+len) — which rule `excludeIdx` covered as ONE
    // symbol — using the basis MINUS that rule (matches must fit inside the window). Returns Δ = (re-cover symbols) − 1:
    // 0 if a sibling covers the window in one symbol (the rule is REDUNDANT), up to len−1 if the window shatters into
    // lone bytes (the rule is IRREPLACEABLE). Summed over a rule's windows in its home file, this is its causal Edge.
    static int LocalReDelta(byte[] text, int pos, int len, Basis basis, int excludeIdx)
    {
        int end = pos + len, i = pos, syms = 0;
        while (i < end)
        {
            int bestLen = 0;
            if (end - i >= 2 && basis.ByPrefix.TryGetValue((text[i] << 8) | text[i + 1], out var cands))
                foreach (var (exp, idx) in cands)
                    if (idx != excludeIdx && exp.Length <= end - i && text.AsSpan(i, exp.Length).SequenceEqual(exp)) { bestLen = exp.Length; break; }
            syms++;
            i += bestLen > 0 ? bestLen : 1;
        }
        return syms - 1;
    }

    // permute the home-label assignment across rules (Fisher-Yates via Engine.Shuffle, seed = FNV(instance id)) — the Edge
    // magnitudes are unchanged, only WHICH file is credited is scrambled. Each file stays home to the same rule COUNT.
    static Dictionary<string, long> ShuffledHome(string id, List<string> cands, Dictionary<int, long> ruleEdge, Dictionary<int, string> ruleHome)
    {
        var idxs = ruleEdge.Keys.OrderBy(x => x).ToArray();                 // deterministic rule order
        var homes = idxs.Select(i => ruleHome[i]).ToArray();               // the home-label vector
        Engine.Shuffle(homes, Fnv(id));                                    // permute the labels across rules
        var shuf = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var f in cands) shuf[f] = 0;
        for (int k = 0; k < idxs.Length; k++) shuf[homes[k]] += ruleEdge[idxs[k]];
        return shuf;
    }

    // ── the re-rank: newScore(f) = norm(base) + λ·norm(arm), min-max per instance; tie-break by base ordinal (so λ=0 is
    // byte-identical to the base order — the identity gate). Returns the committed top-1 file. (Verbatim from H2′.) ──
    static string CommitTop(Scored s, Func<string, double> arm, double lambda)
    {
        double bMin = double.MaxValue, bMax = double.MinValue, aMin = double.MaxValue, aMax = double.MinValue;
        foreach (var f in s.Cands)
        {
            double b = s.Base[f], a = arm(f);
            if (b < bMin) bMin = b; if (b > bMax) bMax = b;
            if (a < aMin) aMin = a; if (a > aMax) aMax = a;
        }
        double bRange = bMax - bMin, aRange = aMax - aMin;
        string best = s.Cands[0]; double bestKey = double.MinValue; int bestRank = int.MaxValue;
        foreach (var f in s.Cands)
        {
            double nb = bRange > 0 ? (s.Base[f] - bMin) / bRange : 0.0;
            double na = aRange > 0 ? (arm(f) - aMin) / aRange : 0.0;
            double key = nb + lambda * na;
            int rk = s.BaseRank[f];
            if (key > bestKey || (key == bestKey && rk < bestRank)) { bestKey = key; bestRank = rk; best = f; }
        }
        return best;
    }

    // ── the analysis: commit / recognition / gap-closure per arm × λ, gated arm, flips, lineage, verdict ──
    sealed class Report
    {
        public required int N;
        public required double Recognition, BaseCommit, Gap;
        public required Dictionary<string, double[]> Commit;   // arm → per-λ commit rate
        public required double Gated;                          // pure-Edge hard-gate commit (argmax rule-Edge)
        public required int BestLi;                            // the λ index maximizing rule-Edge commit
        public required List<(string Id, string Repo, string BaseTop, string EdgeTop, string Gold)> Gained, Lost;
        public required int BaseRepos, EdgeRepos;
        public required Dictionary<string, int> GainRepos, LossRepos;
        public required double MeanCorpusKB, MeanRules, MeanFiring;
        public required string Verdict, Console;
        public required string RppVerdict;                     // the R vs R″ pre-registered read (causal vs distributional Edge)
    }

    static Report Analyze(List<Scored> scored)
    {
        int n = scored.Count;
        double recognition = scored.Count(s => s.Recognized) / (double)n;
        double baseCommit = scored.Count(s => s.Gold.Contains(s.Cands[0])) / (double)n;
        double gap = recognition - baseCommit;

        string[] arms = ["edge", "rpp", "rprime", "ridf", "shuf", "idf", "rand"];
        var commit = new Dictionary<string, double[]>();
        foreach (var arm in arms)
        {
            var row = new double[Lambdas.Length];
            for (int li = 0; li < Lambdas.Length; li++)
            {
                int hit = 0;
                foreach (var s in scored)
                {
                    Func<string, double> f = arm switch
                    {
                        "edge" => x => s.Edge[x], "rpp" => x => s.Rpp[x], "rprime" => x => s.Rprime[x],
                        "ridf" => x => s.Ridf[x], "shuf" => x => s.ShufEdge[x], "idf" => x => s.Idf[x],
                        _ => x => s.Rand[x],
                    };
                    if (s.Gold.Contains(CommitTop(s, f, Lambdas[li]))) hit++;
                }
                row[li] = hit / (double)n;
            }
            commit[arm] = row;
        }

        // gated — the pure-Edge hard gate: commit argmax(rule-Edge) over the reached set (base ignored). graded>gated = Avida.
        int gatedHit = 0;
        foreach (var s in scored)
        {
            string top = s.Cands[0]; long bestE = long.MinValue; int bestRank = int.MaxValue;
            foreach (var f in s.Cands)
            {
                long e = s.Edge[f]; int rk = s.BaseRank[f];
                if (e > bestE || (e == bestE && rk < bestRank)) { bestE = e; bestRank = rk; top = f; }
            }
            if (s.Gold.Contains(top)) gatedHit++;
        }
        double gated = gatedHit / (double)n;

        // best-λ Edge (smallest λ achieving the max commit — the most conservative blend)
        int bestLi = 0;
        for (int li = 1; li < Lambdas.Length; li++) if (commit["edge"][li] > commit["edge"][bestLi]) bestLi = li;

        // flips + lineage at best-λ Edge
        var gained = new List<(string, string, string, string, string)>();
        var lost = new List<(string, string, string, string, string)>();
        var gainRepos = new Dictionary<string, int>();
        var lossRepos = new Dictionary<string, int>();
        var baseCorrectRepos = new HashSet<string>();
        var edgeCorrectRepos = new HashSet<string>();
        foreach (var s in scored)
        {
            string bTop = s.Cands[0];
            string eTop = CommitTop(s, x => s.Edge[x], Lambdas[bestLi]);
            bool bHit = s.Gold.Contains(bTop), eHit = s.Gold.Contains(eTop);
            string repo = s.Repo;
            if (bHit) baseCorrectRepos.Add(repo);
            if (eHit) edgeCorrectRepos.Add(repo);
            string goldStr = string.Join(",", s.Gold);
            if (!bHit && eHit) { gained.Add((s.Id, repo, bTop, eTop, goldStr)); gainRepos[repo] = gainRepos.GetValueOrDefault(repo) + 1; }
            if (bHit && !eHit) { lost.Add((s.Id, repo, bTop, eTop, goldStr)); lossRepos[repo] = lossRepos.GetValueOrDefault(repo) + 1; }
        }

        double edgeBest = commit["edge"][bestLi];
        double edgeGapClosure = gap > 0 ? (edgeBest - baseCommit) / gap : 0.0;
        double edgeDelta = edgeBest - baseCommit;
        double ridfBest = commit["ridf"].Max(); double ridfDelta = ridfBest - baseCommit;   // rule-rarity control
        double idfBest  = commit["idf"].Max();  double idfDelta  = idfBest  - baseCommit;    // the +5.7% bar to beat
        double shufBest = commit["shuf"].Max();
        double shufGapClosure = gap > 0 ? (shufBest - baseCommit) / gap : 0.0;
        double rppBest    = commit["rpp"].Max();    double rppDelta    = rppBest    - baseCommit;   // R″ CAUSAL Edge (load-bearing-ness)
        double rprimeBest = commit["rprime"].Max(); double rprimeDelta = rprimeBest - baseCommit;   // R′ query-touched Edge (relevance × concentration)

        // ── the R vs R″ pre-registered read: R″ beats R → sharpness is CAUSAL (the A-organ
        //    waits for Whorl C's native counterfactual meters); R beats R″ → the cheap distributional estimator earns
        //    its keep (the biological shape — pain/hunger are cheap proxies for expensive fitness ground-truth). ──
        string rppVerdict = rppDelta > edgeDelta + 1e-9
            ? $"R″ > R  (Δ{rppDelta:+0.0%;-0.0%} vs {edgeDelta:+0.0%;-0.0%}) — sharpness is CAUSAL: load-bearing-ness out-discriminates distributional concentration; the A-organ should wait for Whorl C's native counterfactual meters."
          : edgeDelta > rppDelta + 1e-9
            ? $"R > R″  (Δ{edgeDelta:+0.0%;-0.0%} vs {rppDelta:+0.0%;-0.0%}) — the CHEAP distributional estimator earns its keep: concentration prices the rule as well as the expensive counterfactual re-cover, the biological shape (pain/hunger are cheap proxies for costly fitness)."
            : $"R ≈ R″  (Δ{edgeDelta:+0.0%;-0.0%} ≈ {rppDelta:+0.0%;-0.0%}) — causal and distributional Edge are indistinguishable on the nav plane; neither the expensive counterfactual nor the cheap concentration separates from base.";

        // ── the pre-registered kill-line (three clean outcomes) ──
        bool passClosure  = edgeGapClosure >= 0.5;
        bool beatsIdf     = edgeDelta > 0.057 && edgeDelta > idfDelta;
        bool diffHolds    = shufGapClosure < 0.5 && shufGapClosure < edgeGapClosure - 1e-9;
        bool matchesIdf   = Math.Abs(edgeDelta - idfDelta) <= 0.02 && edgeDelta > 0.0;
        string verdict =
            beatsIdf && passClosure && !diffHolds
                ? $"FAIL-magnitude — rule-Edge beats idf (Δ{edgeDelta:+0.0%} vs {idfDelta:+0.0%}) BUT the shuffled-home null ALSO closes the gap (gap-cl {shufGapClosure:P0}): the signal is rules-per-file MAGNITUDE, not the differential. NOT a clean Tier-A pass."
          : beatsIdf && passClosure && diffHolds
                ? $"PASS — RUNG A VALIDATED, BUILD the organ. Rule-grain Edge closes {edgeGapClosure:P0} of the gap, BEATS the idf null (Δ{edgeDelta:+0.0%} vs {idfDelta:+0.0%}), and the shuffled-home differential null stays flat ({shufGapClosure:P0}). The concentration structure — right rule → right file — is what discriminates."
          : matchesIdf
                ? $"USE-IDF — rule-Edge merely MATCHES idf (Δ{edgeDelta:+0.0%} vs {idfDelta:+0.0%}, within ±2pt): the discrimination axis is RARITY even at rule grain; the differential adds nothing over rarity. Ship idf (cheap, known); do NOT build the organ."
                : $"FAIL-pivot — rule-grain Edge clears neither the kill-line nor idf (Δ{edgeDelta:+0.0%} vs idf {idfDelta:+0.0%}, gap-cl {edgeGapClosure:P0}). Differential-MDL discrimination is DEAD at every grain; the sharpness axis is something else entirely — rethink Whorl A.";

        var sb = new StringBuilder();
        sb.Append($"\n══ EDGE-RULE AUTOPSY VERDICT (Arm R — the decisive Tier-A test) ══════════\n");
        sb.Append($"  N={n} · recognition={recognition:P1} · base file@1={baseCommit:P1} · gap={gap:P1}\n");
        sb.Append($"  grammar regime: mean corpus={scored.Average(s => s.CorpusBytes) / 1024.0:0.0}KB · mean rules={scored.Average(s => s.RuleCount):0} · mean firing-rules={scored.Average(s => s.FiringRules):0}\n");
        sb.Append($"  RULE-EDGE (concave differential) λ={Lambdas[bestLi]}: file@1={edgeBest:P1} (Δ{edgeDelta:+0.0%;-0.0%}) · gap-closure={edgeGapClosure:P0}   ← R (distributional concentration)\n");
        sb.Append($"  R″  (CAUSAL Edge, ΔparsedSize on rule-removal re-cover) best : file@1={rppBest:P1} (Δ{rppDelta:+0.0%;-0.0%})   ← the ablation arm\n");
        sb.Append($"  R′  (query-touched Edge, relevance × concentration)     best : file@1={rprimeBest:P1} (Δ{rprimeDelta:+0.0%;-0.0%})\n");
        sb.Append($"  → R vs R″: {rppVerdict}\n");
        sb.Append($"  idf  null (token, +5.7% bar) best : file@1={idfBest:P1} (Δ{idfDelta:+0.0%;-0.0%})   [MUST beat this]\n");
        sb.Append($"  ridf (rule-rarity control)  best : file@1={ridfBest:P1} (Δ{ridfDelta:+0.0%;-0.0%})   [rarity, no concentration — is rule grain just rarity?]\n");
        sb.Append($"  shuf (differential NULL)    best : file@1={shufBest:P1} · gap-closure={shufGapClosure:P0}   [MUST stay LOW]\n");
        sb.Append($"  random                      best : file@1={commit["rand"].Max():P1}   [the floor]\n");
        sb.Append($"  gated (argmax rule-Edge): file@1={gated:P1}  ·  graded(best)={edgeBest:P1} → {(edgeBest > gated ? "graded>gated ✓ (Avida readout)" : "gated≥graded ✗")}\n");
        sb.Append($"  lineage: base correct spans {baseCorrectRepos.Count} repos · edge {edgeCorrectRepos.Count} repos{(edgeCorrectRepos.Count < baseCorrectRepos.Count && edgeBest > baseCommit ? "  ⚠ SHRINKS — Goodhart-via-pruning shadow" : "")}\n");
        sb.Append($"  → {verdict}\n");
        sb.Append($"══════════════════════════════════════════════════════════════════════════\n");

        return new Report
        {
            N = n, Recognition = recognition, BaseCommit = baseCommit, Gap = gap,
            Commit = commit, Gated = gated, BestLi = bestLi,
            Gained = gained, Lost = lost, BaseRepos = baseCorrectRepos.Count, EdgeRepos = edgeCorrectRepos.Count,
            GainRepos = gainRepos, LossRepos = lossRepos,
            MeanCorpusKB = scored.Average(s => s.CorpusBytes) / 1024.0, MeanRules = scored.Average(s => s.RuleCount), MeanFiring = scored.Average(s => s.FiringRules),
            Verdict = verdict, Console = sb.ToString(), RppVerdict = rppVerdict,
        };
    }

    // ── deterministic hashing (FNV-1a 64) + a [0,1) draw (verbatim from H2′) ──
    static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 1099511628211UL; }
        return h;
    }
    static double Rand01(string s) => (Fnv(s) >> 11) / (double)(1UL << 53);

    // ── RENDER (local artifacts — the deliverable) ──
    static void RenderSummary(Report r, List<Scored> scored, string dataDir, string path)
    {
        var sb = new StringBuilder();
        sb.Append("# Edge-rule autopsy — Arm R: differential-compression at GRAMMAR-RULE grain (the decisive Tier-A test)\n\n");
        sb.Append($"Read-only re-rank of the banked navigate results · `{dataDir}` · N={r.N} instances.\n");
        sb.Append("The grammar is induced per instance over the candidate files' module docs (Engine.Induce, the engine's daily verb); ");
        sb.Append("each candidate file's per-rule FIRE counts come from the greedy longest-first NESTING-IMMUNE cover (a rule fires once per maximal match). ");
        sb.Append("Edge(r) = S(fires_home) − S(fires_complement), S(k)=log₂(1+k); EdgeScore(f) = Σ over rules whose HOME is f. ");
        sb.Append("The token-idf null is reproduced byte-for-byte from H2′ — the +5.7% bar.\n\n");
        sb.Append($"- **recognition** (gold ∩ visited ≠ ∅): **{r.Recognition:P1}** — the ceiling a re-rank can reach\n");
        sb.Append($"- **base file@1** (commit): **{r.BaseCommit:P1}**\n");
        sb.Append($"- **gap** (recognition − base): **{r.Gap:P1}** — the headroom Edge must close\n");
        sb.Append($"- **grammar regime**: mean corpus {r.MeanCorpusKB:0.0}KB · mean {r.MeanRules:0} rules/instance · mean {r.MeanFiring:0} FIRING rules (won ≥1 maximal match)\n\n");

        sb.Append("## Commit rate by arm × λ\n\n");
        sb.Append("Arms: **edge** = rule-grain concave differential (organ candidate) · **ridf** = rule-rarity (Σ home-rule idf, no concentration) · ");
        sb.Append("**idf** = token Σln(N/df) (the +5.7% bar) · **shuf** = shuffled-home (differential null) · **rand** = floor.\n\n");
        sb.Append("| λ | edge | edge gap-cl | ridf | idf (bar) | shuf (diff-null) | random |\n");
        sb.Append("|---|------|-------------|------|-----------|------------------|--------|\n");
        for (int li = 0; li < Lambdas.Length; li++)
        {
            double e = r.Commit["edge"][li];
            double gc = r.Gap > 0 ? (e - r.BaseCommit) / r.Gap : 0.0;
            sb.Append($"| {Lambdas[li]:0.00} | {e:P1} | {gc:P0} | {r.Commit["ridf"][li]:P1} | {r.Commit["idf"][li]:P1} | {r.Commit["shuf"][li]:P1} | {r.Commit["rand"][li]:P1} |\n");
        }
        sb.Append($"\n_λ=0 is the identity gate: every arm must equal base file@1 ({r.BaseCommit:P1}) — a nonzero Δ is a bug._\n\n");

        double edgeBest = r.Commit["edge"][r.BestLi];
        double idfBest = r.Commit["idf"].Max(), ridfBest = r.Commit["ridf"].Max(), shufBest = r.Commit["shuf"].Max();
        double shufGc = r.Gap > 0 ? (shufBest - r.BaseCommit) / r.Gap : 0.0;

        sb.Append("## Rule grain vs token grain vs idf (the whole Tier-A thread, one table)\n\n");
        sb.Append("| grain / measure | file@1 | Δ vs base | note |\n|---|---|---|---|\n");
        sb.Append($"| base (BM25, λ=0) | {r.BaseCommit:P1} | — | the floor to beat |\n");
        sb.Append($"| token concave-Edge (H2′, committed) | — | **+1.7% / +3.x%** | the prior result — token concentration lost to idf |\n");
        sb.Append($"| token idf (the bar) | {idfBest:P1} | {idfBest - r.BaseCommit:+0.0%;-0.0%} | pure rarity — beat H2′'s edge |\n");
        sb.Append($"| **rule concave-Edge (Arm R)** | **{edgeBest:P1}** | **{edgeBest - r.BaseCommit:+0.0%;-0.0%}** | **the decisive arm — concentration at rule grain** |\n");
        sb.Append($"| rule-rarity (ridf) | {ridfBest:P1} | {ridfBest - r.BaseCommit:+0.0%;-0.0%} | rule rarity without concentration |\n");
        sb.Append($"| shuffled-home (differential null) | {shufBest:P1} | {shufBest - r.BaseCommit:+0.0%;-0.0%} | gap-closure {shufGc:P0} — MUST stay flat |\n\n");

        sb.Append("## R vs R″ vs R′ — distributional, causal, and query-touched Edge (the ablation arms)\n\n");
        double rppBest = r.Commit["rpp"].Max(), rprimeBest = r.Commit["rprime"].Max();
        sb.Append("| arm | file@1 | Δ vs base | what it prices |\n|---|---|---|---|\n");
        sb.Append($"| **R** (rule concentration) | {edgeBest:P1} | {edgeBest - r.BaseCommit:+0.0%;-0.0%} | distributional: home-vs-complement fire concentration |\n");
        sb.Append($"| **R″** (causal Edge) | {rppBest:P1} | {rppBest - r.BaseCommit:+0.0%;-0.0%} | load-bearing-ness: ΔparsedSize when the rule is removed + its home file re-covered |\n");
        sb.Append($"| **R′** (query-touched Edge) | {rprimeBest:P1} | {rprimeBest - r.BaseCommit:+0.0%;-0.0%} | relevance × concentration: Edge, only for rules whose expansion touches a query term |\n\n");
        sb.Append($"- **R vs R″ verdict**: {r.RppVerdict}\n");
        sb.Append("- Pre-registered read (SPIRE): R″ beats R → sharpness is CAUSAL (the A-organ waits for Whorl C's native counterfactual meters); R beats R″ → the cheap distributional estimator earns its keep (the biological shape — pain/hunger are cheap proxies for expensive fitness ground-truth).\n\n");

        sb.Append("## The differential null (magnitude or concentration?)\n\n");
        sb.Append($"- **rule-Edge** (right rule → right file): **{edgeBest:P1}** (Δ{edgeBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- **shuffled-home** (same Edge magnitudes, scrambled homes): **{shufBest:P1}** (gap-closure {shufGc:P0})\n");
        sb.Append($"- verdict: {(shufGc < 0.5 && shufBest < edgeBest - 1e-9 ? "the shuffled null stays flat → the signal IS the differential (concentration structure), not rules-per-file magnitude." : "the shuffled null ALSO moves → the signal is MAGNITUDE (rules-per-file mass), not the differential.")}\n\n");

        sb.Append("## Rarity or concentration (does rule grain just recover idf)?\n\n");
        sb.Append($"- **idf** (token rarity): **{idfBest:P1}** (Δ{idfBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- **ridf** (rule rarity, no concentration): **{ridfBest:P1}** (Δ{ridfBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- **edge** (rule concentration): **{edgeBest:P1}** (Δ{edgeBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- verdict: {(edgeBest > idfBest + 1e-9 && edgeBest > ridfBest + 1e-9 ? "rule concentration BEATS both rarities → the differential sees what rarity cannot." : edgeBest <= ridfBest + 1e-9 && ridfBest > r.BaseCommit ? "rule-rarity matches/beats concentration → the axis is rarity even at rule grain." : "concentration does not clearly separate from rarity.")}\n\n");

        sb.Append("## Graded vs gated (the offline Avida readout)\n\n");
        sb.Append($"- **gated** (hard gate — commit argmax rule-Edge, base ignored): **{r.Gated:P1}**\n");
        sb.Append($"- **graded** (best-λ blend): **{edgeBest:P1}** (λ={Lambdas[r.BestLi]})\n");
        sb.Append($"- verdict: {(edgeBest > r.Gated ? "**graded > gated** — the blend wins, as Avida predicts (a hard gate over-commits)." : "**gated ≥ graded** — the hard gate is not worse here; note it.")}\n\n");

        sb.Append("## Lineage diversity (stepping-stone shadow)\n\n");
        sb.Append($"- distinct repos in the **base** correct-commit set: **{r.BaseRepos}**\n");
        sb.Append($"- distinct repos in the **best-λ Edge** correct-commit set: **{r.EdgeRepos}**\n");
        sb.Append($"- gains by repo: {FmtRepos(r.GainRepos)}\n");
        sb.Append($"- losses by repo: {FmtRepos(r.LossRepos)}\n");
        if (r.EdgeRepos < r.BaseRepos && edgeBest > r.BaseCommit)
            sb.Append("- ⚠ **Edge raises file@1 while SHRINKING repo coverage** — the offline shadow of the stepping-stone kill-line. Flagged for the Reservoir primitive.\n\n");
        else
            sb.Append("- repo coverage holds — no stepping-stone shrink flagged.\n\n");

        sb.Append("## Verdict\n\n");
        sb.Append($"**{r.Verdict}**\n\n");
        sb.Append("Kill-line (pre-registered, three clean outcomes): **BEATS idf** (Δ > +5.7% AND max_λ gap-closure ≥ 0.5 AND shuffled-home flat) → BUILD the organ; ");
        sb.Append("**MATCHES idf** (within ±2pt) → use idf (axis is rarity); **FAILS** (shuffled-home also closes, or edge < idf) → differential-MDL dead at every grain, rethink.\n");
        File.WriteAllText(path, sb.ToString());
    }

    static string FmtRepos(Dictionary<string, int> m)
        => m.Count == 0 ? "(none)" : string.Join(", ", m.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}×{kv.Value}"));

    static void RenderFlips(Report r, List<Scored> scored, string path)
    {
        var sb = new StringBuilder();
        sb.Append($"# Edge-rule autopsy — per-instance flips at best-λ Edge (λ={Lambdas[r.BestLi]})\n\n");
        sb.Append("Base file@1 → rule-Edge file@1 on the same reached set. Gained = base miss recovered to a hit@1; Lost = base hit@1 dropped.\n\n");
        sb.Append($"## Recovered ({r.Gained.Count}) — base miss → Edge hit@1\n\n");
        sb.Append("| instance | repo | base top-1 | edge top-1 (=gold) | gold |\n|---|---|---|---|---|\n");
        foreach (var (id, repo, bt, et, gold) in r.Gained.OrderBy(x => x.Item2, StringComparer.Ordinal).ThenBy(x => x.Item1, StringComparer.Ordinal))
            sb.Append($"| {id} | {repo} | `{bt}` | `{et}` | `{gold}` |\n");
        sb.Append($"\n## Lost ({r.Lost.Count}) — base hit@1 → Edge miss\n\n");
        sb.Append("| instance | repo | base top-1 (=gold) | edge top-1 | gold |\n|---|---|---|---|---|\n");
        foreach (var (id, repo, bt, et, gold) in r.Lost.OrderBy(x => x.Item2, StringComparer.Ordinal).ThenBy(x => x.Item1, StringComparer.Ordinal))
            sb.Append($"| {id} | {repo} | `{bt}` | `{et}` | `{gold}` |\n");
        sb.Append($"\n**Net: {r.Gained.Count - r.Lost.Count:+0;-0} instances** ({r.Gained.Count} recovered − {r.Lost.Count} lost).\n");
        File.WriteAllText(path, sb.ToString());
    }
}
