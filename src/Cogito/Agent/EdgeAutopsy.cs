namespace Cogito;

using System.Text;
using System.Text.Json;
using Cogito.Codec;    // Fixed.Log2 — the integer milli-bit log the whole MDL stack rides
using Cogito.Induct;   // Mdl.PairDelta — the canonical (linear) MDL delta, kept only for the linear-comparison arms

// ── EDGE AUTOPSY (H2′, Tier A) ──  the offline differential-COMPRESSION discrimination probe, REVISED. READ-ONLY science
// on DEAD DATA: it re-ranks the BANKED 300-instance navigate results, blends a differential "Edge" score, and scores the
// re-ranking against gold — testing whether CONCENTRATION (a term dense in its home file, sparse elsewhere) is the missing
// SHARPNESS axis, BEFORE any organ is built. Mints nothing, changes no engine behavior; the measure rides Fixed.Log2 /
// Mdl.PairDelta (the canonical MDL primitives).
//
// WHY H2 FAILED (the proxy, not the principle): the first pass set V=256, so Mdl.PairDelta(c,256)=(c−2)·8000 is LINEAR in
// count → the differential collapsed to a raw count diff Σ(2c−C)·8000 with ZERO rarity/concavity, and per-file counts
// SUMMED over nested module⊃class⊃method sites — together AMPLIFYING hub files (core.py/base.py). Edge anti-correlated
// with gold (+1.7%), and the idf null BEAT it (+5.7%). H2′ fixes both artifacts and asks the question idf forced.
//
// THE THREE H2′ FIXES:
//   (1) CONCAVE measure — the home saving is S(k)=log₂(1+k) (Fixed.Log2, integer mbits): DIMINISHING returns, so a term
//       concentrated in its home beats a hub term dense everywhere. edge(f)=Σ_t idf_t·[S(c)−S(d)], d=C_t−c the complement.
//       For a hub (c≈d) the concave complement saving CANCELS the home saving → ~0; for a concentrated term it stays HIGH.
//       (The linear proxy could never do this: (c−2)·8000−(d−2)·8000 rewards raw magnitude, so the biggest hub file wins.)
//   (2) MODULE-DOC-ONLY counts — the corpus scan counts each term ONCE per file at the module doc (which spans the whole
//       file), killing the module⊃class⊃method triple-count. (The engine already names this: Bm25Index._fileDf, Loc.cs.)
//   (3) DIFFERENTIAL-OVER-IDF — the `conc` arm is edge with idf STRIPPED (pure concentration): if it beats base, the
//       home/complement split discriminates BEYOND rarity → build the organ; if only idf-weighted edge helps, rarity is
//       the axis (use idf). The `lin`/`linNested` arms reproduce the H2 measure on deduped/nested counts (concave-vs-linear).
//
// PRE-REGISTERED KILL-LINE: PASS iff max_λ gap-closure ≥ 0.5 AND commit(concave-Edge) − commit(base) > +6% (BEATS idf's
// +5.7%) AND the shuffled-home differential null does NOT close the gap. Three outcomes: beats idf → BUILD; merely matches
// idf → use idf (axis is rarity); fails even fixed → differential-MDL-over-token-counts is dead, pivot to grammar-metering.
public static class EdgeAutopsy
{
    const int V = 256;                                       // the OLD byte-alphabet vocabulary — kept ONLY for the linear-comparison arms (lin/linNested reproduce the H2 measure)
    const int MinTermLen = Loc.MinTermLen;                   // len≥4 query-term filter (parity with the nav minted-term floor)
    static readonly double[] Lambdas = [0.0, 0.25, 0.5, 1.0, 2.0];   // the blend sweep

    // ── one banked instance: the reconstructed base ranking + the reached (recognition) set ──
    sealed record Inst(string Id, string Repo, List<string> Visited, List<(string Path, double Score)> Sites);

    // per-instance scored candidate: the base rank + every arm's raw score over the REACHED set
    sealed class Scored
    {
        public required string Id;                           // instance id (for flips + lineage)
        public required string Repo;                         // Loc.RepoOf(id) — the lineage bucket
        public required List<string> Cands;                  // the candidate files, in base-rank order (AggregateMaxFiles)
        public required Dictionary<string, double> Base;     // base file score (aggregate-max)
        public required Dictionary<string, int> BaseRank;    // base ordinal (0 = base top-1) — the identity tie-break
        public required Dictionary<string, long> Edge;       // CONCAVE idf-weighted differential — the organ candidate (integer mbits²)
        public required Dictionary<string, long> Conc;       // CONCAVE differential, idf STRIPPED — concentration-beyond-idf (the differential-over-idf arm)
        public required Dictionary<string, long> Lin;        // LINEAR differential on module-only counts — isolates the concavity fix (concave vs this)
        public required Dictionary<string, long> LinNested;  // LINEAR differential on NESTED counts — reproduces the H2 measure (the +1.7% floor)
        public required Dictionary<string, long> ShufEdge;   // shuffled-home concave Edge (the differential NULL)
        public required Dictionary<string, double> Idf;      // Σ ln(N/df) — the refuted +5.7% null
        public required Dictionary<string, double> Rand;     // deterministic pseudo-random — the floor
        public required HashSet<string> Gold;                // scoring only, NEVER a re-rank input
        public bool Recognized;                              // gold ∩ visited ≠ ∅ (the recognition ceiling)
    }

    public static int Run(string rankingsPath, string dataDir, string outDir, int limit)
    {
        if (!File.Exists(rankingsPath)) { Console.Error.WriteLine($"  rankings not found: {rankingsPath}"); return 1; }
        if (!Directory.Exists(dataDir)) { Console.Error.WriteLine($"  data dir not found: {dataDir}"); return 1; }
        Directory.CreateDirectory(outDir);

        var insts = LoadRankings(rankingsPath, limit);
        Console.WriteLine($"edge-autopsy · {insts.Count} banked instances · data {dataDir} · V={V} · λ∈{{{string.Join(",", Lambdas)}}}");

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

    // ── INPUT 1: navigate_rankings.jsonl → the base ranking + reached set per instance ──
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
                // [path, name, start, end, score] — reconstruct the base file ranking via aggregate-max (Loc law)
                var a = e.EnumerateArray().ToArray();
                sites.Add((a[0].GetString()!, a[4].GetDouble()));
            }
            list.Add(new Inst(id, Loc.RepoOf(id), visited, sites));
            if (limit > 0 && list.Count >= limit) break;
        }
        return list;
    }

    // ── the per-instance Edge approximation ──
    static Scored? ScoreInstance(Inst inst, string dir)
    {
        var sitesPath = Path.Combine(dir, "sites.jsonl");
        var queryPath = Path.Combine(dir, "query.txt");
        if (!File.Exists(sitesPath) || !File.Exists(queryPath)) return null;

        // base file ranking over the REACHED set (aggregate-max of local_fn_sites — exactly Loc/NavLoop's law)
        var baseSites = inst.Sites.Select(s => new Site(s.Path, "", "", 0, 0, "")).ToList();
        var (baseOrder, baseScore) = Loc.AggregateMaxFiles(baseSites, inst.Sites.Select(s => s.Score).ToArray());
        if (baseOrder.Count == 0) return null;
        var baseRank = new Dictionary<string, int>();
        for (int i = 0; i < baseOrder.Count; i++) baseRank[baseOrder[i]] = i;
        var candSet = new HashSet<string>(baseOrder, StringComparer.Ordinal);

        // query terms T: Loc.Toks(query), distinct, len≥4 (df-cap applied after the corpus scan). Bigrams ride along —
        // Loc.Toks emits adjacent pairs, and the corpus counts use the same Toks, so the multiset matches (field-parity).
        var qterms = Loc.Toks(File.ReadAllText(queryPath)).Where(t => t.Length >= MinTermLen).Distinct().ToList();
        var querySet = new HashSet<string>(qterms, StringComparer.Ordinal);

        // ONE corpus scan over sites.jsonl, restricted to query terms. TWO count-sets, so one pass yields the full ablation
        // ladder (the nesting fix and the concavity fix, isolated):
        //   MODULE-ONLY (kind=="module" — one doc per file, spanning the WHOLE file: FIX 2) → the honest per-file term
        //     frequency, no module⊃class⊃method triple-count. Feeds edge/conc/lin/idf + the df read.
        //   NESTED (every site — the H2 behaviour) → reproduces the linNested comparison arm (the +1.7% floor).
        //   corpusCount[t]=Σ_file moduleCount · dfFiles[t]=distinct module docs carrying t · fileCount[f][t]=home density.
        var corpusCount  = new Dictionary<string, long>(StringComparer.Ordinal);            // module-only term mass
        var dfFiles      = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); // distinct files (= module docs) — the df read
        var corpusCountN = new Dictionary<string, long>(StringComparer.Ordinal);            // nested term mass (H2 repro)
        foreach (var t in qterms) { corpusCount[t] = 0; corpusCountN[t] = 0; dfFiles[t] = new HashSet<string>(StringComparer.Ordinal); }
        var fileCount  = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);   // module-only home density
        var fileCountN = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);   // nested home density (H2 repro)
        foreach (var f in baseOrder) { fileCount[f] = new Dictionary<string, int>(StringComparer.Ordinal); fileCountN[f] = new Dictionary<string, int>(StringComparer.Ordinal); }
        var moduleFiles = new HashSet<string>(StringComparer.Ordinal);   // file universe = one module doc per file → N

        foreach (var site in Loc.LoadSites(sitesPath))
        {
            bool isModule = site.Kind == "module";
            if (isModule) moduleFiles.Add(site.Path);
            bool isCand = candSet.Contains(site.Path);
            var fc  = isCand && isModule ? fileCount[site.Path]  : null;   // module-only home density
            var fcN = isCand             ? fileCountN[site.Path] : null;   // nested home density
            foreach (var t in Loc.Toks(site.Text))
            {
                if (!querySet.Contains(t)) continue;
                corpusCountN[t]++;                                          // NESTED: counts every containing site
                if (fcN is not null) fcN[t] = fcN.GetValueOrDefault(t) + 1;
                if (!isModule) continue;                                    // MODULE-ONLY from here — one count per file
                corpusCount[t]++;
                dfFiles[t].Add(site.Path);
                if (fc is not null) fc[t] = fc.GetValueOrDefault(t) + 1;
            }
        }
        int n = moduleFiles.Count;                                         // distinct files (module docs) — the honest N
        int dfCap = n / 4;   // DfCapFrac = 0.25 — the nav stoplist (Gret's "drop concepts in >25% of sites"), integer

        // T = query terms surviving the df cap (df now over MODULE docs — the honest file-level rarity, no nesting).
        // df=0 terms survive harmlessly (absent from every candidate's fileCount → contribute nothing to any Edge sum).
        var T = qterms.Where(t => dfFiles[t].Count <= dfCap).ToList();

        var edge = new Dictionary<string, long>(StringComparer.Ordinal);   // concave, idf-weighted (the organ candidate)
        var conc = new Dictionary<string, long>(StringComparer.Ordinal);   // concave, idf-stripped (concentration alone)
        var lin  = new Dictionary<string, long>(StringComparer.Ordinal);   // linear, module-only (isolates concavity)
        var linN = new Dictionary<string, long>(StringComparer.Ordinal);   // linear, nested (H2 repro)
        var idf  = new Dictionary<string, double>(StringComparer.Ordinal);
        var rand = new Dictionary<string, double>(StringComparer.Ordinal);
        long log2N = Fixed.Log2((uint)Math.Max(1, n)).Value;               // for the integer idf weight = log2(N) − log2(df)
        foreach (var f in baseOrder)
        {
            var fc = fileCount[f]; var fcN = fileCountN[f];
            long e = 0, cc = 0, li = 0, liN = 0; double id = 0;
            foreach (var t in T)
            {
                int c = fc.GetValueOrDefault(t);
                if (c >= 1)                                                  // module-only home carries the term
                {
                    long d = corpusCount[t] - c;                            // the complement mass (elsewhere, module-only)
                    // CONCAVE differential: S(k)=log₂(1+k) — diminishing returns, so a hub (c≈d) cancels to ~0 while a
                    // concentrated term (d≪c) stays HIGH. This is the concavity the linear V=256 proxy destroyed.
                    long sHome  = Fixed.Log2((uint)(c + 1)).Value;
                    long sCompl = Fixed.Log2((uint)(d + 1)).Value;
                    long idfMbits = Math.Max(0, log2N - Fixed.Log2((uint)Math.Max(1, dfFiles[t].Count)).Value);   // self-information
                    cc += sHome - sCompl;                                    // pure concentration (idf-stripped)
                    e  += idfMbits * (sHome - sCompl);                       // rarity-weighted concentration (the organ candidate)
                    li += Mdl.PairDelta(c, V).Value - Mdl.PairDelta((int)d, V).Value;   // LINEAR, module-only
                    id += Math.Log((double)n / dfFiles[t].Count);            // the idf null: Σ ln(N/df) over present terms
                }
                int cN = fcN.GetValueOrDefault(t);
                if (cN >= 1)                                                 // NESTED home count → the H2 reproduction arm
                {
                    long dN = corpusCountN[t] - cN;
                    liN += Mdl.PairDelta(cN, V).Value - Mdl.PairDelta((int)dN, V).Value;
                }
            }
            edge[f] = e; conc[f] = cc; lin[f] = li; linN[f] = liN;
            idf[f] = id; rand[f] = Rand01(inst.Id + "|" + f);
        }

        // ARM — shuffled-home Edge: each candidate is assigned ANOTHER file's concave Edge (deterministic derangement
        // seeded from the instance id). Same magnitude distribution, wrong home → if this ALSO closes the gap, the signal
        // is magnitude not the differential. THE make-or-break null.
        var shuf = ShuffledHome(inst.Id, baseOrder, edge);

        var gold = Loc.LoadGold(Path.Combine(dir, "gold.json")).Files;
        bool recognized = inst.Visited.Any(gold.Contains);

        return new Scored
        {
            Id = inst.Id, Repo = inst.Repo,
            Cands = baseOrder, Base = baseScore, BaseRank = baseRank,
            Edge = edge, Conc = conc, Lin = lin, LinNested = linN, ShufEdge = shuf,
            Idf = idf, Rand = rand, Gold = gold, Recognized = recognized,
        };
    }

    // deterministic derangement of the Edge values across candidates (seed = FNV(instance id)); no candidate keeps its own.
    static Dictionary<string, long> ShuffledHome(string id, List<string> cands, Dictionary<string, long> edge)
    {
        int nc = cands.Count;
        var perm = Enumerable.Range(0, nc).ToArray();
        ulong seed = Fnv(id);
        for (int i = nc - 1; i >= 1; i--)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;       // LCG step
            int j = (int)((seed >> 33) % (ulong)(i + 1));
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }
        for (int i = 0; i < nc; i++) if (perm[i] == i) { int k = (i + 1) % nc; (perm[i], perm[k]) = (perm[k], perm[i]); }  // repair fixed points → derangement
        var shuf = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int i = 0; i < nc; i++) shuf[cands[i]] = edge[cands[perm[i]]];
        return shuf;
    }

    // ── the re-rank: newScore(f) = norm(base) + λ·norm(arm), min-max per instance; tie-break by base ordinal (so λ=0 is
    // byte-identical to the base order — the identity gate). Returns the committed top-1 file. ──
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

    // ── the analysis: commit / recognition / gap-closure per arm × λ, gated arm, flips, lineage ──
    sealed class Report
    {
        public required int N;
        public required double Recognition, BaseCommit, Gap;
        public required Dictionary<string, double[]> Commit;   // arm → per-λ commit rate
        public required double Gated;                          // pure-Edge hard-gate commit (argmax Edge)
        public required int BestLi;                            // the λ index maximizing Edge commit
        public required List<(string Id, string Repo, string BaseTop, string EdgeTop, string Gold)> Gained, Lost;
        public required int BaseRepos, EdgeRepos;              // distinct repos in the correct-commit set
        public required Dictionary<string, int> GainRepos, LossRepos;
        public required string Verdict, Console;
    }

    static Report Analyze(List<Scored> scored)
    {
        int n = scored.Count;
        double recognition = scored.Count(s => s.Recognized) / (double)n;
        double baseCommit = scored.Count(s => s.Gold.Contains(s.Cands[0])) / (double)n;
        double gap = recognition - baseCommit;

        string[] arms = ["edge", "conc", "lin", "linNested", "shuf", "idf", "rand"];
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
                        "edge" => x => s.Edge[x], "conc" => x => s.Conc[x],
                        "lin" => x => s.Lin[x], "linNested" => x => s.LinNested[x],
                        "shuf" => x => s.ShufEdge[x], "idf" => x => s.Idf[x],
                        _ => x => s.Rand[x],
                    };
                    if (s.Gold.Contains(CommitTop(s, f, Lambdas[li]))) hit++;
                }
                row[li] = hit / (double)n;
            }
            commit[arm] = row;
        }

        // gated — the pure-Edge hard gate: commit argmax(Edge) over the reached set (base ignored). graded>gated = Avida.
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

        // verdict — the concave-Edge (idf-weighted) is the organ candidate; the decision rests on it beating idf AND the
        // differential null holding AND concentration (idf-stripped `conc`) independently clearing base.
        double edgeBest = commit["edge"][bestLi];
        double edgeGapClosure = gap > 0 ? (edgeBest - baseCommit) / gap : 0.0;
        double edgeDelta = edgeBest - baseCommit;
        double concBest = commit["conc"].Max();  double concDelta = concBest - baseCommit;    // differential-over-idf
        double idfBest  = commit["idf"].Max();   double idfDelta  = idfBest  - baseCommit;    // the null to beat
        double linBest  = commit["lin"].Max();   double linDelta  = linBest  - baseCommit;    // linear, module-only
        double linNBest = commit["linNested"].Max(); double linNDelta = linNBest - baseCommit; // the H2 measure (repro)
        double shufBest = commit["shuf"].Max();
        double shufGapClosure = gap > 0 ? (shufBest - baseCommit) / gap : 0.0;

        bool passClosure   = edgeGapClosure >= 0.5;
        bool passBeatsIdf  = edgeDelta > 0.06 && edgeDelta > idfDelta;
        bool differentialHolds = shufGapClosure < 0.5 && shufGapClosure < edgeGapClosure - 1e-9;
        bool concBeatsBase = concDelta > 0.0;   // concentration, stripped of rarity, discriminates on its own
        string verdict =
            !differentialHolds && passClosure && passBeatsIdf
                ? "FAIL-magnitude — shuffled-home ALSO closes the gap: the signal is MAGNITUDE, not the differential. The concave measure did not isolate concentration."
          : passClosure && passBeatsIdf && differentialHolds
                ? $"PASS — BUILD the grammar-metered organ. Concave-Edge closes ≥half the gap, BEATS the idf null (Δ{edgeDelta:+0.0%} vs {idfDelta:+0.0%}), and the differential null holds. Concentration-beyond-idf ({(concBeatsBase ? "conc" : "idf-only")}) {(concBeatsBase ? "confirmed" : "WEAK — win rides on rarity, note it")}."
          : (Math.Abs(edgeDelta - idfDelta) <= 0.02 && edgeDelta > 0.0)
                ? $"USE-IDF — concave-Edge merely MATCHES idf (Δ{edgeDelta:+0.0%} vs {idfDelta:+0.0%}, within ±2pt): the discrimination axis is RARITY, not concentration. Ship idf (cheap, known); the differential adds nothing."
                : "FAIL-pivot — concave-Edge clears neither the kill-line nor idf. Differential-MDL-over-token-counts is dead; pivot to grammar-RULE metering (the token proxy was never the organ).";

        bool lineageFlag = edgeBest > baseCommit && edgeCorrectRepos.Count < baseCorrectRepos.Count;

        var sb = new StringBuilder();
        sb.Append($"\n══ EDGE AUTOPSY VERDICT (H2′) ════════════════════════════════════\n");
        sb.Append($"  N={n} · recognition={recognition:P1} · base file@1={baseCommit:P1} · gap={gap:P1}\n");
        sb.Append($"  CONCAVE-EDGE (idf-wt) λ={Lambdas[bestLi]}: file@1={edgeBest:P1} (Δ{edgeDelta:+0.0%;-0.0%}) · gap-closure={edgeGapClosure:P0}   ← the organ candidate\n");
        sb.Append($"  conc (idf-STRIPPED) best : file@1={concBest:P1} (Δ{concDelta:+0.0%;-0.0%})   [concentration beyond idf — the differential-over-idf arm]\n");
        sb.Append($"  idf  null           best : file@1={idfBest:P1} (Δ{idfDelta:+0.0%;-0.0%})   [the refuted +5.7% null — MUST beat this]\n");
        sb.Append($"  lin  (concave→LINEAR, module-only): file@1={linBest:P1} (Δ{linDelta:+0.0%;-0.0%})   [isolates the concavity fix]\n");
        sb.Append($"  linNested (the H2 measure, repro) : file@1={linNBest:P1} (Δ{linNDelta:+0.0%;-0.0%})   [the +1.7% floor]\n");
        sb.Append($"  shuf (differential NULL)  best : file@1={shufBest:P1} · gap-closure={shufGapClosure:P0}   [MUST stay LOW]\n");
        sb.Append($"  random              best : file@1={commit["rand"].Max():P1}   [the floor]\n");
        sb.Append($"  gated (argmax concave-Edge): file@1={gated:P1}   ·  graded(best)={edgeBest:P1} → {(edgeBest > gated ? "graded>gated ✓ (Avida readout)" : "gated≥graded ✗")}\n");
        sb.Append($"  lineage: base correct spans {baseCorrectRepos.Count} repos · edge {edgeCorrectRepos.Count} repos{(lineageFlag ? "  ⚠ SHRINKS — Goodhart-via-pruning shadow" : "")}\n");
        sb.Append($"  → {verdict}\n");
        sb.Append($"══════════════════════════════════════════════════════════════════\n");

        return new Report
        {
            N = n, Recognition = recognition, BaseCommit = baseCommit, Gap = gap,
            Commit = commit, Gated = gated, BestLi = bestLi,
            Gained = gained, Lost = lost, BaseRepos = baseCorrectRepos.Count, EdgeRepos = edgeCorrectRepos.Count,
            GainRepos = gainRepos, LossRepos = lossRepos, Verdict = verdict, Console = sb.ToString(),
        };
    }

    // ── deterministic hashing (FNV-1a 64) + a [0,1) draw ──
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
        sb.Append("# Edge autopsy — H2′ concave differential-compression discrimination (Tier A)\n\n");
        sb.Append($"Read-only re-rank of the banked navigate results · `{dataDir}` · N={r.N} instances · df-cap=0.25·N.\n");
        sb.Append("Fixes vs H2: **(1) concave** home saving S(k)=log₂(1+k) (the linear V=256 proxy was the bug); ");
        sb.Append("**(2) module-doc-only** counts (kill the module⊃class⊃method triple-count); ");
        sb.Append("**(3) differential-over-idf** isolation (the `conc` arm strips idf).\n\n");
        sb.Append($"- **recognition** (gold ∩ visited ≠ ∅): **{r.Recognition:P1}** — the ceiling a re-rank can reach\n");
        sb.Append($"- **base file@1** (commit): **{r.BaseCommit:P1}**\n");
        sb.Append($"- **gap** (recognition − base): **{r.Gap:P1}** — the headroom Edge must close\n\n");

        sb.Append("## Commit rate by arm × λ\n\n");
        sb.Append("Arms: **edge** = concave, idf-weighted (organ candidate) · **conc** = concave, idf-STRIPPED (concentration alone) · ");
        sb.Append("**lin** = linear, module-only (isolates concavity) · **linNested** = the H2 measure reproduced · ");
        sb.Append("**idf** = Σln(N/df) (null to beat) · **shuf** = shuffled-home (differential null) · **rand** = floor.\n\n");
        sb.Append("| λ | edge | edge gap-cl | conc | lin | linNested | idf (null) | shuf (diff-null) | random |\n");
        sb.Append("|---|------|-------------|------|-----|-----------|------------|------------------|--------|\n");
        for (int li = 0; li < Lambdas.Length; li++)
        {
            double e = r.Commit["edge"][li];
            double gc = r.Gap > 0 ? (e - r.BaseCommit) / r.Gap : 0.0;
            sb.Append($"| {Lambdas[li]:0.00} | {e:P1} | {gc:P0} | {r.Commit["conc"][li]:P1} | {r.Commit["lin"][li]:P1} | {r.Commit["linNested"][li]:P1} | {r.Commit["idf"][li]:P1} | {r.Commit["shuf"][li]:P1} | {r.Commit["rand"][li]:P1} |\n");
        }
        sb.Append($"\n_λ=0 is the identity gate: every arm must equal base file@1 ({r.BaseCommit:P1}) — a nonzero Δ is a bug._\n\n");

        // ── the two H2′ questions, answered head-on ──
        double edgeBest = r.Commit["edge"][r.BestLi];
        double linBest = r.Commit["lin"].Max(), linNBest = r.Commit["linNested"].Max();
        double concBest = r.Commit["conc"].Max(), idfBest = r.Commit["idf"].Max();
        sb.Append("## Concave vs linear (does the concavity fix help?)\n\n");
        sb.Append($"- **linNested** (H2 measure, nested counts): **{linNBest:P1}** (Δ{linNBest - r.BaseCommit:+0.0%;-0.0%}) — the +1.7% floor reproduced\n");
        sb.Append($"- **lin** (same LINEAR measure, module-only counts): **{linBest:P1}** (Δ{linBest - r.BaseCommit:+0.0%;-0.0%}) — isolates the nesting fix\n");
        sb.Append($"- **edge** (CONCAVE, module-only, idf-weighted): **{edgeBest:P1}** (Δ{edgeBest - r.BaseCommit:+0.0%;-0.0%}) — isolates the concavity fix\n");
        sb.Append($"- verdict: concavity {(edgeBest > linBest ? $"**HELPS** (+{(edgeBest - linBest) * 100:0.0}pt over linear-module-only)" : "does **NOT** help over the linear measure on the same counts")}.\n\n");

        sb.Append("## Differential over idf (does concentration add beyond rarity?)\n\n");
        sb.Append($"- **idf** (pure rarity, no concentration): **{idfBest:P1}** (Δ{idfBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- **conc** (pure concentration, idf STRIPPED): **{concBest:P1}** (Δ{concBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- **edge** (rarity × concentration): **{edgeBest:P1}** (Δ{edgeBest - r.BaseCommit:+0.0%;-0.0%})\n");
        sb.Append($"- verdict: {(edgeBest > idfBest + 1e-9 ? $"concave-Edge **BEATS** idf (+{(edgeBest - idfBest) * 100:0.0}pt) → concentration discriminates" : Math.Abs(edgeBest - idfBest) <= 0.02 ? "concave-Edge **MATCHES** idf → the axis is rarity, use idf" : "concave-Edge **loses** to idf → differential dead")}{(concBest > r.BaseCommit ? "; concentration alone clears base (real signal)" : "; concentration alone does NOT clear base (rides on rarity)")}.\n\n");

        sb.Append("## Graded vs gated (the offline Avida readout)\n\n");
        sb.Append($"- **gated** (hard gate — commit argmax concave-Edge, base ignored): **{r.Gated:P1}**\n");
        sb.Append($"- **graded** (best-λ blend): **{r.Commit["edge"][r.BestLi]:P1}** (λ={Lambdas[r.BestLi]})\n");
        sb.Append($"- verdict: {(r.Commit["edge"][r.BestLi] > r.Gated ? "**graded > gated** — the graded blend wins, as Avida predicts (a hard gate over-commits)." : "**gated ≥ graded** — the hard gate is not worse here; note it.")}\n\n");

        sb.Append("## Lineage diversity (stepping-stone shadow)\n\n");
        sb.Append($"- distinct repos in the **base** correct-commit set: **{r.BaseRepos}**\n");
        sb.Append($"- distinct repos in the **best-λ Edge** correct-commit set: **{r.EdgeRepos}**\n");
        sb.Append($"- gains by repo: {FmtRepos(r.GainRepos)}\n");
        sb.Append($"- losses by repo: {FmtRepos(r.LossRepos)}\n");
        if (r.EdgeRepos < r.BaseRepos && r.Commit["edge"][r.BestLi] > r.BaseCommit)
            sb.Append("- ⚠ **Edge raises file@1 while SHRINKING repo coverage** — the offline shadow of the stepping-stone kill-line. Flagged for the Reservoir primitive.\n\n");
        else
            sb.Append("- repo coverage holds — no stepping-stone shrink flagged.\n\n");

        sb.Append("## Verdict\n\n");
        sb.Append($"**{r.Verdict}**\n\n");
        sb.Append("Kill-line (pre-registered): PASS iff max_λ gap-closure ≥ 0.5 AND commit(concave-Edge)−commit(base) > +6% (BEATS idf's +5.7%) AND the shuffled-home arm does NOT close the gap. ");
        sb.Append("Three outcomes: beats idf → BUILD the grammar-metered organ; matches idf → use idf (axis is rarity); fails even fixed → pivot to grammar-rule metering.\n");
        File.WriteAllText(path, sb.ToString());
    }

    static string FmtRepos(Dictionary<string, int> m)
        => m.Count == 0 ? "(none)" : string.Join(", ", m.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}×{kv.Value}"));

    static void RenderFlips(Report r, List<Scored> scored, string path)
    {
        var sb = new StringBuilder();
        sb.Append($"# Edge autopsy — per-instance flips at best-λ Edge (λ={Lambdas[r.BestLi]})\n\n");
        sb.Append($"Base file@1 → Edge file@1 on the same reached set. Gained = base miss recovered to a hit@1; Lost = base hit@1 dropped.\n\n");
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
