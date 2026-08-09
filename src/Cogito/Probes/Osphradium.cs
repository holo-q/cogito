namespace Cogito;

using System.Text;
using System.Text.Json;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Induct;

// ── OSPHRADIUM ──  stimulus-conditioned relevance: stimulus in, density-home gradient out.
// The organ is intentionally read-only. It induces the candidate grammar, seeds from the greedy
// grammar cover of the stimulus, then flows those seed fillers through the within-family
// filler→family→density-home incidence that LatticeCensus READ 1 proved carries localization.
public static class Osphradium
{
    private const int MinTermLen = Loc.MinTermLen;
    private const int DefaultTopFamilies = 12;
    private const int DefaultMaxCorpusBytes = 500_000;
    private const long DegreeScoreScale = 4096;
    public const ulong DefaultSeed = 0x05FAD1AUL;
    private static readonly double[] Lambdas = [0.0, 0.25, 0.5, 1.0, 2.0];

    public static int Run(string dataDir, string rankingsPath, int limit, int topFamilies, int maxCorpusBytes, int maxFiles, bool preDegreeNorm, ulong seed)
    {
        if (!Directory.Exists(dataDir)) { Console.Error.WriteLine($"  osphradium: data dir not found: {dataDir}"); return 1; }
        if (rankingsPath.Length == 0) rankingsPath = FindRankings(dataDir);
        if (!File.Exists(rankingsPath))
        {
            Console.Error.WriteLine("  osphradium: rankings not found; pass --rankings <rankings.jsonl>");
            return 1;
        }

        topFamilies = topFamilies <= 0 ? DefaultTopFamilies : topFamilies;
        maxCorpusBytes = maxCorpusBytes <= 0 ? DefaultMaxCorpusBytes : maxCorpusBytes;

        var insts = LoadRankings(rankingsPath, dataDir, limit);
        if (insts.Count == 0) { Console.Error.WriteLine("  osphradium: no instances loaded"); return 1; }

        var run = Cogito.Run.New("osphradium");
        run.Write("config.txt", $"probe osphradium · data={dataDir} · rankings={rankingsPath} · limit={limit} · topFamilies={topFamilies} · maxCorpusBytes={maxCorpusBytes} · maxFiles={maxFiles} · degreeNorm={!preDegreeNorm} · seed={seed:X}\n");
        using var perInst = run.Appender("instances.tsv");
        perInst.WriteLine("instance\trepo\tcandidates\tgold_rank_base\tgold_rank_degree\tgold_rank_degree_zero\tgold_rank_pre\tgold_rank_pre_zero\tgold_rank_idf\tseed_groups\tseed_fillers\tdegree_flow_mass\tdegree_flat_mass\tdegree_flat_share\tdegree_hub_corr\tpre_flow_mass\tpre_flat_mass\tpre_flat_share\tpre_hub_corr\ttop_degree\ttop_degree_zero\ttop_pre\ttop_pre_zero\ttop_idf\tgold");

        Console.WriteLine($"osphradium · {insts.Count} instances · rankings {rankingsPath} · data {dataDir}");
        Console.WriteLine($"  organ: grammar-cover seeds → within-family density homes · topFamilies={topFamilies} maxCorpus={maxCorpusBytes:N0}B · degreeNorm={!preDegreeNorm}");

        var scored = new List<Scored>(insts.Count);
        int done = 0;
        foreach (var inst in insts)
        {
            var dir = Path.Combine(dataDir, inst.Id);
            var s = ScoreInstance(inst, dir, topFamilies, maxCorpusBytes, maxFiles, preDegreeNorm, seed);
            if (s is not null)
            {
                scored.Add(s);
                perInst.WriteLine(InstanceRow(s));
            }
            done++;
            if (done == 1 || done % 10 == 0 || done == insts.Count) Console.WriteLine($"  scored {done}/{insts.Count}");
        }

        if (scored.Count == 0) { Console.Error.WriteLine("  osphradium: all instances dropped"); return 1; }
        var report = Analyze(scored, preDegreeNorm);
        RenderReport(run, report, scored);
        Console.WriteLine(report.Console);
        Console.WriteLine($"  rendered → {run.Dir}/summary.md · instances.tsv");
        return 0;
    }

    public static string FindRankings(string dataDir)
    {
        var full = Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sweLoc = Directory.GetParent(full)?.FullName ?? "";
        var cached = Path.Combine(sweLoc, "eval", "runs", "20260702T014145Z_cache", "rankings.jsonl");
        return File.Exists(cached) ? cached : "";
    }

    private static Scored? ScoreInstance(Inst inst, string dir, int topFamilies, int maxCorpusBytes, int maxFiles, bool preDegreeNorm, ulong seed)
    {
        var sitesPath = Path.Combine(dir, "sites.jsonl");
        var queryPath = Path.Combine(dir, "query.txt");
        if (!File.Exists(sitesPath)) return null;

        var rankedSites = inst.Ranked;
        if (rankedSites.Count == 0) return null;
        var baseSites = rankedSites.Select(s => new Site(s.Path, s.Kind, s.Name, s.Start, s.End, "")).ToList();
        var (baseOrder0, baseScore0) = Loc.AggregateMaxFiles(baseSites, rankedSites.Select(s => s.Score).ToArray());
        if (baseOrder0.Count == 0) return null;
        var baseOrder = maxFiles > 0 ? baseOrder0.Take(maxFiles).ToList() : baseOrder0;
        var baseScore = new Dictionary<string, double>(StringComparer.Ordinal);
        var baseRank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < baseOrder.Count; i++)
        {
            var f = baseOrder[i];
            baseScore[f] = baseScore0[f];
            baseRank[f] = i;
        }
        var candSet = new HashSet<string>(baseOrder, StringComparer.Ordinal);

        var queryText = inst.Query.Length > 0 ? inst.Query : File.Exists(queryPath) ? File.ReadAllText(queryPath) : "";
        var queryBytes = Encoding.UTF8.GetBytes(queryText);
        var qterms = Loc.Toks(queryText).Where(t => t.Length >= MinTermLen).Distinct(StringComparer.Ordinal).ToList();
        var querySet = new HashSet<string>(qterms, StringComparer.Ordinal);

        var moduleText = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var moduleFileText = new Dictionary<string, string>(StringComparer.Ordinal);
        var moduleFiles = new HashSet<string>(StringComparer.Ordinal);
        var dfFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var t in qterms) dfFiles[t] = new HashSet<string>(StringComparer.Ordinal);
        var fileTokCount = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var f in baseOrder) fileTokCount[f] = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var site in Loc.LoadSites(sitesPath))
        {
            if (site.Kind != "module") continue;
            moduleFiles.Add(site.Path);
            if (candSet.Contains(site.Path) && !moduleText.ContainsKey(site.Path))
            {
                var bytes = Encoding.UTF8.GetBytes(site.Text);
                moduleText[site.Path] = bytes;
                moduleFileText[site.Path] = site.Text;
            }
            Dictionary<string, int>? ftc = candSet.Contains(site.Path) ? fileTokCount[site.Path] : null;
            foreach (var t in Loc.Toks(site.Text))
            {
                if (!querySet.Contains(t)) continue;
                dfFiles[t].Add(site.Path);
                if (ftc is not null) ftc[t] = ftc.GetValueOrDefault(t) + 1;
            }
        }
        if (moduleText.Count == 0) return null;

        var graphFiles = new List<Blur.TokenFile>();
        var corpus = new List<byte>();
        foreach (var f in baseOrder)
        {
            if (!moduleText.TryGetValue(f, out var bytes)) continue;
            if (corpus.Count + bytes.Length > maxCorpusBytes) break;
            corpus.AddRange(bytes);
            corpus.Add((byte)'\n');
            graphFiles.Add(new Blur.TokenFile(f, inst.Repo, Blur.Tokenize(bytes).ToArray(), bytes.Length));
        }
        if (corpus.Count == 0 || graphFiles.Count == 0) return null;

        var rules = Engine.Induce(corpus.ToArray()).Result.Rules;
        var basis = BuildBasis(rules);
        var seeds = BuildSeedGroups(queryBytes, basis);
        var graph = BuildGraph(graphFiles, topFamilies, baseOrder);

        var degree = Flow(seeds, graph, shuffle: false, degreeNormalize: true, seed, inst.Id);
        var degreeShuffled = Flow(seeds, graph, shuffle: true, degreeNormalize: true, seed, inst.Id);
        var degreeZero = ZeroHop(seeds, graph, degreeNormalize: true);
        var pre = Flow(seeds, graph, shuffle: false, degreeNormalize: false, seed, inst.Id);
        var preShuffled = Flow(seeds, graph, shuffle: true, degreeNormalize: false, seed, inst.Id);
        var preZero = ZeroHop(seeds, graph, degreeNormalize: false);
        var idf = IdfScore(baseOrder, fileTokCount, dfFiles, Math.Max(1, moduleFiles.Count));
        var primary = preDegreeNorm ? pre : degree;
        var primaryZero = preDegreeNorm ? preZero : degreeZero;
        var primaryShuffled = preDegreeNorm ? preShuffled : degreeShuffled;

        var rand = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in baseOrder) rand[f] = Rand01(inst.Id + "|" + f);

        return new Scored
        {
            Id = inst.Id, Repo = inst.Repo, Cands = baseOrder, Base = baseScore, BaseRank = baseRank,
            Osm = primary.Score, Zero = primaryZero.Score, Shuffle = primaryShuffled.Score,
            Degree = degree.Score, DegreeZero = degreeZero.Score, DegreeShuffle = degreeShuffled.Score,
            Pre = pre.Score, PreZero = preZero.Score, PreShuffle = preShuffled.Score,
            Idf = idf, Rand = rand,
            Gold = inst.Gold.Count > 0 ? inst.Gold : Loc.LoadGold(Path.Combine(dir, "gold.json")).Files,
            SeedGroups = seeds.Count, SeedFillers = seeds.Sum(g => g.Fillers.Length), RuleCount = rules.Length,
            GraphFiles = graphFiles.Count, Families = graph.Families.Count,
            FlowMass = primary.HomeMass, FlatMass = primary.FlatMass, FlatByFiller = primary.FlatByFiller, HubCorr = Pearson(primary.Score, graph.FileBytes, baseOrder),
            DegreeFlowMass = degree.HomeMass, DegreeFlatMass = degree.FlatMass, DegreeFlatByFiller = degree.FlatByFiller, DegreeHubCorr = Pearson(degree.Score, graph.FileBytes, baseOrder),
            PreFlowMass = pre.HomeMass, PreFlatMass = pre.FlatMass, PreFlatByFiller = pre.FlatByFiller, PreHubCorr = Pearson(pre.Score, graph.FileBytes, baseOrder),
        };
    }

    private static List<Inst> LoadRankings(string rankingsPath, string dataDir, int limit)
    {
        var list = new List<Inst>();
        foreach (var line in File.ReadLines(rankingsPath))
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            string id = root.GetProperty("instance_id").GetString()!;
            string repo = root.TryGetProperty("repo", out var repoEl) ? repoEl.GetString()! : Loc.RepoOf(id);
            string query = root.TryGetProperty("query", out var qEl) ? qEl.GetString()! : "";
            var ranked = new List<RankSite>();
            var gold = new HashSet<string>(StringComparer.Ordinal);

            if (root.TryGetProperty("ranked", out var rankedEl))
            {
                foreach (var e in rankedEl.EnumerateArray())
                {
                    var a = e.EnumerateArray().ToArray();
                    ranked.Add(new RankSite(a[0].GetString()!, a[1].GetString()!, a[2].GetString()!, a[3].GetInt32(), a[4].GetInt32(), a[5].GetDouble()));
                }
                if (root.TryGetProperty("gold_files", out var gf))
                    foreach (var f in gf.EnumerateArray()) gold.Add(f.GetString()!);
            }
            else if (root.TryGetProperty("local_fn_sites", out var localEl))
            {
                foreach (var e in localEl.EnumerateArray())
                {
                    var a = e.EnumerateArray().ToArray();
                    ranked.Add(new RankSite(a[0].GetString()!, "function", a[1].GetString()!, a[2].GetInt32(), a[3].GetInt32(), a[4].GetDouble()));
                }
            }
            else
            {
                continue;
            }

            if (gold.Count == 0)
            {
                var goldPath = Path.Combine(dataDir, id, "gold.json");
                if (File.Exists(goldPath)) gold = Loc.LoadGold(goldPath).Files;
            }
            if (query.Length == 0)
            {
                var qp = Path.Combine(dataDir, id, "query.txt");
                if (File.Exists(qp)) query = File.ReadAllText(qp);
            }

            list.Add(new Inst(id, repo, query, ranked, gold));
            if (limit > 0 && list.Count >= limit) break;
        }
        return list;
    }

    private static Graph BuildGraph(IReadOnlyList<Blur.TokenFile> files, int topFamilies, List<string> baseOrder)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < baseOrder.Count; i++) rank[baseOrder[i]] = i;

        var graph = new Graph();
        foreach (var file in files) graph.FileBytes[file.File] = file.Bytes;

        foreach (var file in files)
            foreach (var sent in file.Sentences)
                foreach (var tok in sent)
                {
                    if (!IsIdentifierLike(tok)) continue;
                    var byFile = graph.Direct.TryGetValue(tok, out var d) ? d : graph.Direct[tok] = new(StringComparer.Ordinal);
                    byFile[file.File] = byFile.GetValueOrDefault(file.File) + 1;
                }

        var frames = Blur.FrameCensusByFile(files).Take(topFamilies).ToArray();
        for (int familyId = 0; familyId < frames.Length; familyId++)
        {
            var frame = frames[familyId];
            var family = new Family(familyId, frame.Left, frame.Right);
            graph.Families.Add(family);
            foreach (var filler in frame.Fillers)
            {
                if (!IsIdentifierLike(filler.Token)) continue;
                int fires = filler.Files.Sum(f => f.Count);
                int fileCount = filler.Files.Length;
                if (fileCount == 0) continue;
                var home = PickHome(filler.Files, rank);
                int homeGain = fileCount < 2 ? 0 : Math.Max(0, home.Count * fileCount - fires);
                var membership = new Membership(familyId, filler.Token, home.File, home.Count, fires, fileCount, homeGain);
                family.Memberships.Add(membership);
                var node = graph.Fillers.TryGetValue(filler.Token, out var n) ? n : graph.Fillers[filler.Token] = new FillerNode(filler.Token);
                node.Fires += fires;
                node.Memberships.Add(membership);
            }
        }
        foreach (var node in graph.Fillers.Values)
            node.FileDegree = graph.Direct.TryGetValue(node.Token, out var byFile) ? Math.Max(1, byFile.Count) : 1;
        graph.Universe = graph.Fillers.Values.OrderBy(f => f.Token, StringComparer.Ordinal).ToArray();
        return graph;
    }

    private static (string File, int Count) PickHome(Blur.FileCount[] files, Dictionary<string, int> rank)
    {
        string file = "";
        int count = -1, bestRank = int.MaxValue;
        foreach (var f in files)
        {
            int r = rank.TryGetValue(f.File, out var rr) ? rr : int.MaxValue;
            if (f.Count > count || (f.Count == count && r < bestRank) || (f.Count == count && r == bestRank && string.CompareOrdinal(f.File, file) < 0))
            {
                file = f.File;
                count = f.Count;
                bestRank = r;
            }
        }
        return (file, count);
    }

    private static FlowRead Flow(List<SeedGroup> seeds, Graph graph, bool shuffle, bool degreeNormalize, ulong seed, string instanceId)
    {
        var mass = new Dictionary<string, long>(StringComparer.Ordinal);
        var breadth = new Dictionary<string, int>(StringComparer.Ordinal);
        long homeMass = 0, flatMass = 0;
        var flatByFiller = new Dictionary<string, long>(StringComparer.Ordinal);

        for (int gi = 0; gi < seeds.Count; gi++)
        {
            var groupScores = new Dictionary<string, long>(StringComparer.Ordinal);
            int fi = 0;
            foreach (var raw in seeds[gi].Fillers)
            {
                string tok = shuffle ? DrawMatchedFiller(raw, graph, seed, instanceId, gi, fi++) : raw;
                if (!graph.Fillers.TryGetValue(tok, out var node)) continue;
                foreach (var m in node.Memberships)
                {
                    if (m.HomeGain <= 0)
                    {
                        long fm = degreeNormalize ? WeightByDegree(Math.Max(1, m.Fires), node) : Math.Max(1, m.Fires);
                        flatMass += fm;
                        flatByFiller[tok] = flatByFiller.GetValueOrDefault(tok) + fm;
                        continue;
                    }
                    long w = degreeNormalize ? WeightByDegree(m.HomeGain, node) : m.HomeGain;
                    if (w > groupScores.GetValueOrDefault(m.HomeFile)) groupScores[m.HomeFile] = w;
                }
            }
            foreach (var (file, s) in groupScores)
            {
                if (s <= 0) continue;
                mass[file] = mass.GetValueOrDefault(file) + s;
                breadth[file] = breadth.GetValueOrDefault(file) + 1;
                homeMass += s;
            }
        }

        var score = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (file, m) in mass)
        {
            int b = breadth[file];
            score[file] = b * m;
        }
        return new FlowRead(score, homeMass, flatMass, flatByFiller);
    }

    private static FlowRead ZeroHop(List<SeedGroup> seeds, Graph graph, bool degreeNormalize)
    {
        var mass = new Dictionary<string, long>(StringComparer.Ordinal);
        var breadth = new Dictionary<string, int>(StringComparer.Ordinal);
        long homeMass = 0;
        foreach (var group in seeds)
        {
            var groupScores = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var tok in group.Fillers)
            {
                if (!graph.Direct.TryGetValue(tok, out var byFile)) continue;
                foreach (var (file, c) in byFile)
                {
                    long w = degreeNormalize ? WeightByDegree(c, graph, tok) : c;
                    if (w > groupScores.GetValueOrDefault(file)) groupScores[file] = w;
                }
            }
            foreach (var (file, s) in groupScores)
            {
                if (s <= 0) continue;
                mass[file] = mass.GetValueOrDefault(file) + s;
                breadth[file] = breadth.GetValueOrDefault(file) + 1;
                homeMass += s;
            }
        }

        var score = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (file, m) in mass)
        {
            int b = breadth[file];
            score[file] = b * m;
        }
        return new FlowRead(score, homeMass, 0, new Dictionary<string, long>(StringComparer.Ordinal));
    }

    private static long WeightByDegree(long value, FillerNode node)
    {
        int familyDegree = Math.Max(1, node.Memberships.Count);
        return WeightByDegree(value, Math.Max(1, node.FileDegree), familyDegree);
    }

    private static long WeightByDegree(long value, Graph graph, string token)
    {
        int fileDegree = graph.Direct.TryGetValue(token, out var byFile) ? byFile.Count : 1;
        int familyDegree = graph.Fillers.TryGetValue(token, out var node) ? node.Memberships.Count : 1;
        return WeightByDegree(value, Math.Max(1, fileDegree), Math.Max(1, familyDegree));
    }

    private static long WeightByDegree(long value, int fileDegree, int familyDegree)
    {
        if (value <= 0) return 0;
        long denom = Math.Max(1L, (long)fileDegree * familyDegree);
        long weighted = value * DegreeScoreScale / denom;
        return weighted > 0 ? weighted : 1;
    }

    private static Dictionary<string, double> IdfScore(List<string> baseOrder, Dictionary<string, Dictionary<string, int>> fileTokCount, Dictionary<string, HashSet<string>> dfFiles, int nFiles)
    {
        var idf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in baseOrder)
        {
            double s = 0;
            if (fileTokCount.TryGetValue(f, out var counts))
                foreach (var (t, c) in counts)
                {
                    int df = dfFiles.TryGetValue(t, out var fs) ? fs.Count : 0;
                    if (df > 0) s += c * Math.Log((double)nFiles / df);
                }
            idf[f] = s;
        }
        return idf;
    }

    private static string DrawMatchedFiller(string token, Graph graph, ulong seed, string instanceId, int groupIdx, int fillerIdx)
    {
        var universe = graph.Universe;
        if (universe.Length == 0) return token;
        int targetFires = graph.Fillers.TryGetValue(token, out var node) ? Math.Max(1, node.Fires) : 1;
        int targetMemberships = graph.Fillers.TryGetValue(token, out node) ? Math.Max(1, node.Memberships.Count) : 1;
        var candidates = universe.Where(f =>
            f.Token != token &&
            f.Fires >= Math.Max(1, targetFires / 2) && f.Fires <= Math.Max(targetFires * 2, targetFires + 1) &&
            Math.Abs(f.Memberships.Count - targetMemberships) <= 1).ToArray();
        if (candidates.Length == 0) candidates = universe.Where(f => f.Token != token).ToArray();
        if (candidates.Length == 0) return token;
        ulong rng = Mix(seed ^ StableHash(instanceId), (ulong)groupIdx, (ulong)fillerIdx ^ StableHash(token));
        return candidates[(int)(rng % (ulong)candidates.Length)].Token;
    }

    private static List<SeedGroup> BuildSeedGroups(byte[] query, Basis basis)
    {
        var groups = new List<SeedGroup>();
        int i = 0, gid = 0;
        while (i < query.Length)
        {
            int bestLen = 0, bestIdx = -1;
            if (query.Length - i >= 2 && basis.ByPrefix.TryGetValue((query[i] << 8) | query[i + 1], out var cands))
                foreach (var (exp, idx) in cands)
                    if (exp.Length <= query.Length - i && query.AsSpan(i, exp.Length).SequenceEqual(exp)) { bestLen = exp.Length; bestIdx = idx; break; }
            if (bestIdx >= 0)
            {
                var toks = Blur.Tokenize(query.AsSpan(i, bestLen).ToArray())
                    .SelectMany(x => x)
                    .Where(IsIdentifierLike)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();
                if (toks.Length > 0) groups.Add(new SeedGroup(gid++, toks));
                i += bestLen;
            }
            else i++;
        }
        return groups;
    }

    private static Basis BuildBasis(GrammarRule[] rules)
    {
        var exps = new List<(byte[] Exp, int Idx)>(rules.Length);
        for (int i = 0; i < rules.Length; i++)
        {
            var e = Reconstruct.Expand(rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            if (e.Length >= 2) exps.Add((e, i));
        }
        exps.Sort((a, b) =>
        {
            if (a.Exp.Length != b.Exp.Length) return b.Exp.Length - a.Exp.Length;
            for (int k = 0; k < a.Exp.Length; k++) if (a.Exp[k] != b.Exp[k]) return a.Exp[k] - b.Exp[k];
            return a.Idx - b.Idx;
        });
        var buckets = new Dictionary<int, List<(byte[], int)>>();
        foreach (var (e, idx) in exps)
        {
            int key = (e[0] << 8) | e[1];
            (buckets.TryGetValue(key, out var l) ? l : buckets[key] = new()).Add((e, idx));
        }
        var byPrefix = new Dictionary<int, (byte[] Exp, int Idx)[]>(buckets.Count);
        foreach (var (k, l) in buckets) byPrefix[k] = l.ToArray();
        return new Basis(exps.ToArray(), byPrefix);
    }

    private static Report Analyze(List<Scored> scored, bool preDegreeNorm)
    {
        int n = scored.Count;
        double recognition = scored.Count(s => s.Gold.Overlaps(s.Cands)) / (double)n;
        var metrics = new Dictionary<string, ArmMetrics>(StringComparer.Ordinal);
        foreach (var arm in new[] { "base", "degree", "degree-zero", "degree-shuffle", "pre", "pre-zero", "pre-shuffle", "osm", "zero", "shuffle", "idf", "rand" })
            metrics[arm] = Measure(scored, arm);

        double baseCommit = metrics["base"].FileAt1;
        double gap = recognition - baseCommit;
        string[] arms = ["degree", "degree-zero", "degree-shuffle", "pre", "pre-zero", "pre-shuffle", "osm", "zero", "shuffle", "idf", "rand"];
        var blend1 = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var blend5 = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var blend10 = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var arm in arms)
        {
            blend1[arm] = new double[Lambdas.Length];
            blend5[arm] = new double[Lambdas.Length];
            blend10[arm] = new double[Lambdas.Length];
            for (int li = 0; li < Lambdas.Length; li++)
            {
                int h1 = 0, h5 = 0, h10 = 0;
                foreach (var s in scored)
                {
                    var order = BlendRank(s, arm, Lambdas[li]);
                    if (Hits(order, s.Gold, 1)) h1++;
                    if (Hits(order, s.Gold, 5)) h5++;
                    if (Hits(order, s.Gold, 10)) h10++;
                }
                blend1[arm][li] = h1 / (double)n;
                blend5[arm][li] = h5 / (double)n;
                blend10[arm][li] = h10 / (double)n;
            }
        }

        int degreeBestLi = BestLambdaIndex(blend1["degree"]);
        int preBestLi = BestLambdaIndex(blend1["pre"]);
        int primaryBestLi = BestLambdaIndex(blend1["osm"]);
        double degreeBest = blend1["degree"][degreeBestLi], degreeZeroBest = blend1["degree-zero"].Max(), idfBest = blend1["idf"].Max(), degreeShuffleBest = blend1["degree-shuffle"].Max();
        double preBest = blend1["pre"][preBestLi], preZeroBest = blend1["pre-zero"].Max();
        double degreeFlatShare = scored.Sum(s => s.DegreeFlatMass) / (double)Math.Max(1, scored.Sum(s => s.DegreeFlatMass + s.DegreeFlowMass));
        double preFlatShare = scored.Sum(s => s.PreFlatMass) / (double)Math.Max(1, scored.Sum(s => s.PreFlatMass + s.PreFlowMass));
        double degreeHubCorr = MeanCorr(scored.Select(s => s.DegreeHubCorr));
        double preHubCorr = MeanCorr(scored.Select(s => s.PreHubCorr));
        var degreeFlatTop = scored.SelectMany(s => s.DegreeFlatByFiller).GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(g => (Token: g.Key, Mass: g.Sum(x => x.Value))).OrderByDescending(x => x.Mass).ThenBy(x => x.Token, StringComparer.Ordinal).Take(12).ToArray();
        var preFlatTop = scored.SelectMany(s => s.PreFlatByFiller).GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(g => (Token: g.Key, Mass: g.Sum(x => x.Value))).OrderByDescending(x => x.Mass).ThenBy(x => x.Token, StringComparer.Ordinal).Take(12).ToArray();

        string verdict = degreeBest > idfBest && degreeBest > degreeZeroBest && degreeShuffleBest < degreeBest
            ? $"PASS-CANDIDATE — degree-normalized flow beats idf ({degreeBest:P1} vs {idfBest:P1}) and degree-zero-hop ({degreeZeroBest:P1}); seed-shuffle is lower ({degreeShuffleBest:P1})."
            : degreeBest <= degreeZeroBest
                ? $"FAIL-ZERO-HOP — degree-normalized flow adds nothing over its own zero-hop core ({degreeBest:P1} vs zero {degreeZeroBest:P1})."
            : degreeBest <= idfBest
                ? $"FAIL-IDF — degree-normalized flow does not beat the idf bar ({degreeBest:P1} vs {idfBest:P1})."
                : $"MIXED — degree-normalized flow climbs, but the seed-shuffle null also remains live ({degreeShuffleBest:P1}).";

        var console = new StringBuilder();
        console.AppendLine();
        console.AppendLine("══ OSPHRADIUM VERDICT ═════════════════════════════════════════════");
        console.AppendLine($"  N={n} · recognition ceiling={recognition:P1} · base file@1={baseCommit:P1} · gap={gap:P1} · primary={(preDegreeNorm ? "pre-degree-norm" : "degree-normalized")}");
        console.AppendLine($"  degree flow λ={Lambdas[degreeBestLi]:0.00}: file@1={degreeBest:P1} (Δ{degreeBest - baseCommit:+0.0%;-0.0%}) · r@5={blend5["degree"][degreeBestLi]:P1} r@10={blend10["degree"][degreeBestLi]:P1}");
        console.AppendLine($"  degree zero-hop best:   file@1={degreeZeroBest:P1} (Δ{degreeZeroBest - baseCommit:+0.0%;-0.0%})");
        console.AppendLine($"  idf bar best:           file@1={idfBest:P1} (Δ{idfBest - baseCommit:+0.0%;-0.0%})");
        console.AppendLine($"  degree seed-shuffle:    file@1={degreeShuffleBest:P1} (Δ{degreeShuffleBest - baseCommit:+0.0%;-0.0%})");
        console.AppendLine($"  pre-degree legacy:      flow={preBest:P1} zero={preZeroBest:P1} hub={preHubCorr:F3}");
        console.AppendLine($"  degree flat-mass:       {degreeFlatShare:P2} of activation · top {{{string.Join(" ", degreeFlatTop.Select(x => $"{x.Token}:{x.Mass}"))}}}");
        console.AppendLine($"  hub-on-file corr:       degree={degreeHubCorr:F3} · pre={preHubCorr:F3}");
        console.AppendLine($"  verdict: {verdict}");

        return new Report(n, recognition, baseCommit, gap, metrics, blend1, blend5, blend10,
            degreeBestLi, preBestLi, primaryBestLi, preDegreeNorm,
            degreeFlatShare, preFlatShare, degreeHubCorr, preHubCorr, degreeFlatTop, preFlatTop,
            verdict, console.ToString());
    }

    private static int BestLambdaIndex(double[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++) if (values[i] > values[best]) best = i;
        return best;
    }

    private static double MeanCorr(IEnumerable<double> values)
        => values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();

    private static ArmMetrics Measure(List<Scored> scored, string arm)
    {
        int h1 = 0, h5 = 0, h10 = 0;
        foreach (var s in scored)
        {
            var order = Rank(s, arm);
            if (Hits(order, s.Gold, 1)) h1++;
            if (Hits(order, s.Gold, 5)) h5++;
            if (Hits(order, s.Gold, 10)) h10++;
        }
        double n = scored.Count;
        return new ArmMetrics(h1 / n, h5 / n, h10 / n);
    }

    private static List<string> Rank(Scored s, string arm) => arm switch
    {
        "base" => s.Cands.ToList(),
        "osm" => RankLong(s.Cands, s.Osm, s.BaseRank),
        "zero" => RankLong(s.Cands, s.Zero, s.BaseRank),
        "shuffle" => RankLong(s.Cands, s.Shuffle, s.BaseRank),
        "degree" => RankLong(s.Cands, s.Degree, s.BaseRank),
        "degree-zero" => RankLong(s.Cands, s.DegreeZero, s.BaseRank),
        "degree-shuffle" => RankLong(s.Cands, s.DegreeShuffle, s.BaseRank),
        "pre" => RankLong(s.Cands, s.Pre, s.BaseRank),
        "pre-zero" => RankLong(s.Cands, s.PreZero, s.BaseRank),
        "pre-shuffle" => RankLong(s.Cands, s.PreShuffle, s.BaseRank),
        "idf" => RankDouble(s.Cands, s.Idf, s.BaseRank),
        _ => RankDouble(s.Cands, s.Rand, s.BaseRank),
    };

    private static List<string> RankLong(List<string> cands, Dictionary<string, long> score, Dictionary<string, int> baseRank)
        => cands.OrderByDescending(f => score.GetValueOrDefault(f)).ThenBy(f => baseRank[f]).ToList();

    private static List<string> RankDouble(List<string> cands, Dictionary<string, double> score, Dictionary<string, int> baseRank)
        => cands.OrderByDescending(f => score.GetValueOrDefault(f)).ThenBy(f => baseRank[f]).ToList();

    private static List<string> BlendRank(Scored s, string arm, double lambda)
    {
        double bMin = double.MaxValue, bMax = double.MinValue, aMin = double.MaxValue, aMax = double.MinValue;
        foreach (var f in s.Cands)
        {
            double b = s.Base[f], a = ArmValue(s, arm, f);
            if (b < bMin) bMin = b; if (b > bMax) bMax = b;
            if (a < aMin) aMin = a; if (a > aMax) aMax = a;
        }
        double br = bMax - bMin, ar = aMax - aMin;
        return s.Cands.OrderByDescending(f =>
        {
            double nb = br > 0 ? (s.Base[f] - bMin) / br : 0.0;
            double na = ar > 0 ? (ArmValue(s, arm, f) - aMin) / ar : 0.0;
            return nb + lambda * na;
        }).ThenBy(f => s.BaseRank[f]).ToList();
    }

    private static double ArmValue(Scored s, string arm, string file) => arm switch
    {
        "osm" => s.Osm.GetValueOrDefault(file),
        "zero" => s.Zero.GetValueOrDefault(file),
        "shuffle" => s.Shuffle.GetValueOrDefault(file),
        "degree" => s.Degree.GetValueOrDefault(file),
        "degree-zero" => s.DegreeZero.GetValueOrDefault(file),
        "degree-shuffle" => s.DegreeShuffle.GetValueOrDefault(file),
        "pre" => s.Pre.GetValueOrDefault(file),
        "pre-zero" => s.PreZero.GetValueOrDefault(file),
        "pre-shuffle" => s.PreShuffle.GetValueOrDefault(file),
        "idf" => s.Idf.GetValueOrDefault(file),
        _ => s.Rand.GetValueOrDefault(file),
    };

    private static bool Hits(List<string> order, HashSet<string> gold, int k)
        => order.Take(Math.Min(k, order.Count)).Any(gold.Contains);

    private static void RenderReport(Run run, Report r, List<Scored> scored)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Osphradium — Stimulus-Conditioned Relevance");
        sb.AppendLine();
        sb.AppendLine(r.Verdict);
        sb.AppendLine();
        sb.AppendLine($"N={r.N}; recognition ceiling={r.Recognition:P1}; base file@1={r.BaseCommit:P1}; gap={r.Gap:P1}; primary={(r.PreDegreeNorm ? "pre-degree-norm" : "degree-normalized")}.");
        sb.AppendLine();
        sb.AppendLine("| lambda | degree@1 | degree@5 | degree@10 | degree-zero@1 | pre@1 | pre-zero@1 | idf@1 | degree-shuffle@1 | rand@1 |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        for (int i = 0; i < Lambdas.Length; i++)
            sb.AppendLine($"| {Lambdas[i]:0.00} | {r.Blend1["degree"][i]:P1} | {r.Blend5["degree"][i]:P1} | {r.Blend10["degree"][i]:P1} | {r.Blend1["degree-zero"][i]:P1} | {r.Blend1["pre"][i]:P1} | {r.Blend1["pre-zero"][i]:P1} | {r.Blend1["idf"][i]:P1} | {r.Blend1["degree-shuffle"][i]:P1} | {r.Blend1["rand"][i]:P1} |");
        sb.AppendLine();
        sb.AppendLine("## Pure Ranker Diagnostic");
        sb.AppendLine();
        sb.AppendLine("| ranker | file@1 | recall@5 | recall@10 |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var arm in new[] { "base", "idf", "degree", "degree-zero", "degree-shuffle", "pre", "pre-zero", "pre-shuffle", "rand" })
            sb.AppendLine($"| {arm} | {r.Metrics[arm].FileAt1:P1} | {r.Metrics[arm].Recall5:P1} | {r.Metrics[arm].Recall10:P1} |");
        sb.AppendLine();
        sb.AppendLine("## Decisive Reads");
        sb.AppendLine();
        sb.AppendLine($"1. Degree-normalized bar: best blended file@1 {r.Blend1["degree"][r.DegreeBestLi]:P1}; idf best {r.Blend1["idf"].Max():P1}; base {r.BaseCommit:P1}; ceiling {r.Recognition:P1}.");
        sb.AppendLine($"2. Degree zero-hop gate: best zero-hop file@1 {r.Blend1["degree-zero"].Max():P1}; degree flow best {r.Blend1["degree"][r.DegreeBestLi]:P1}.");
        sb.AppendLine($"3. Pre-degree comparison: legacy flow {r.Blend1["pre"][r.PreBestLi]:P1}; legacy zero-hop {r.Blend1["pre-zero"].Max():P1}; legacy hub corr {r.PreHubCorr:F3}.");
        sb.AppendLine($"4. Hub-on-file diagnostic: degree corr(convergence,file size) {r.DegreeHubCorr:F3}; pre-degree {r.PreHubCorr:F3}.");
        sb.AppendLine($"5. Degree flat-incidence mass {r.DegreeFlatShare:P2}; top flat fillers: {string.Join(", ", r.DegreeFlatTop.Select(x => $"{x.Token}:{x.Mass}"))}.");
        sb.AppendLine($"6. Pre-degree flat-incidence mass {r.PreFlatShare:P2}; top flat fillers: {string.Join(", ", r.PreFlatTop.Select(x => $"{x.Token}:{x.Mass}"))}.");
        sb.AppendLine();
        sb.AppendLine("## Regime");
        sb.AppendLine();
        sb.AppendLine($"Mean rule count {scored.Average(s => s.RuleCount):F0}; mean graph files {scored.Average(s => s.GraphFiles):F1}; mean families {scored.Average(s => s.Families):F1}; seedless {scored.Count(s => s.SeedGroups == 0)}/{scored.Count}.");
        run.Write("summary.md", sb.ToString());
    }

    private static string InstanceRow(Scored s)
    {
        var degreeRank = Rank(s, "degree");
        var degreeZeroRank = Rank(s, "degree-zero");
        var preRank = Rank(s, "pre");
        var preZeroRank = Rank(s, "pre-zero");
        var idfRank = Rank(s, "idf");
        return string.Join('\t',
        [
            s.Id, s.Repo, s.Cands.Count.ToString(), RankOf(s.Cands, s.Gold).ToString(),
            RankOf(degreeRank, s.Gold).ToString(), RankOf(degreeZeroRank, s.Gold).ToString(), RankOf(preRank, s.Gold).ToString(), RankOf(preZeroRank, s.Gold).ToString(), RankOf(idfRank, s.Gold).ToString(),
            s.SeedGroups.ToString(), s.SeedFillers.ToString(),
            s.DegreeFlowMass.ToString(), s.DegreeFlatMass.ToString(),
            ((double)s.DegreeFlatMass / Math.Max(1, s.DegreeFlatMass + s.DegreeFlowMass)).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            s.DegreeHubCorr.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            s.PreFlowMass.ToString(), s.PreFlatMass.ToString(),
            ((double)s.PreFlatMass / Math.Max(1, s.PreFlatMass + s.PreFlowMass)).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            s.PreHubCorr.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            degreeRank.Count > 0 ? degreeRank[0] : "", degreeZeroRank.Count > 0 ? degreeZeroRank[0] : "",
            preRank.Count > 0 ? preRank[0] : "", preZeroRank.Count > 0 ? preZeroRank[0] : "",
            idfRank.Count > 0 ? idfRank[0] : "",
            string.Join(",", s.Gold),
        ]);
    }

    private static int RankOf(List<string> order, HashSet<string> gold)
    {
        for (int i = 0; i < order.Count; i++) if (gold.Contains(order[i])) return i + 1;
        return 0;
    }

    private static double Pearson(Dictionary<string, long> score, Dictionary<string, int> bytes, List<string> files)
    {
        int n = 0;
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        foreach (var f in files)
        {
            double x = score.GetValueOrDefault(f);
            double y = bytes.GetValueOrDefault(f);
            n++;
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
        }
        double dx = n * sxx - sx * sx, dy = n * syy - sy * sy;
        return dx <= 0 || dy <= 0 ? double.NaN : (n * sxy - sx * sy) / Math.Sqrt(dx * dy);
    }

    private static bool IsIdentifierLike(string token)
    {
        if (token.Length == 0 || Keywords.Contains(token)) return false;
        char c0 = token[0];
        if (!(char.IsLetter(c0) || c0 == '_')) return false;
        for (int i = 1; i < token.Length; i++)
            if (!(char.IsLetterOrDigit(token[i]) || token[i] == '_')) return false;
        return true;
    }

    private static double Rand01(string s)
    {
        ulong h = StableHash(s);
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    private static ulong StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    private static ulong Mix(ulong a, ulong b, ulong c)
    {
        ulong x = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
        x ^= c + 0xBF58476D1CE4E5B9UL + (x << 6) + (x >> 2);
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private readonly record struct Inst(string Id, string Repo, string Query, List<RankSite> Ranked, HashSet<string> Gold);
    private readonly record struct RankSite(string Path, string Kind, string Name, int Start, int End, double Score);
    private readonly record struct Basis((byte[] Exp, int Idx)[] Exps, Dictionary<int, (byte[] Exp, int Idx)[]> ByPrefix);
    private readonly record struct SeedGroup(int Id, string[] Fillers);
    private readonly record struct Membership(int FamilyId, string Token, string HomeFile, int HomeCount, int Fires, int FileCount, int HomeGain);
    private readonly record struct FlowRead(Dictionary<string, long> Score, long HomeMass, long FlatMass, Dictionary<string, long> FlatByFiller);
    private readonly record struct ArmMetrics(double FileAt1, double Recall5, double Recall10);
    private readonly record struct Report(int N, double Recognition, double BaseCommit, double Gap,
        Dictionary<string, ArmMetrics> Metrics,
        Dictionary<string, double[]> Blend1, Dictionary<string, double[]> Blend5, Dictionary<string, double[]> Blend10,
        int DegreeBestLi, int PreBestLi, int PrimaryBestLi, bool PreDegreeNorm,
        double DegreeFlatShare, double PreFlatShare, double DegreeHubCorr, double PreHubCorr,
        (string Token, long Mass)[] DegreeFlatTop, (string Token, long Mass)[] PreFlatTop,
        string Verdict, string Console);

    private sealed class Scored
    {
        public required string Id;
        public required string Repo;
        public required List<string> Cands;
        public required Dictionary<string, double> Base;
        public required Dictionary<string, int> BaseRank;
        public required Dictionary<string, long> Osm;
        public required Dictionary<string, long> Zero;
        public required Dictionary<string, long> Shuffle;
        public required Dictionary<string, long> Degree;
        public required Dictionary<string, long> DegreeZero;
        public required Dictionary<string, long> DegreeShuffle;
        public required Dictionary<string, long> Pre;
        public required Dictionary<string, long> PreZero;
        public required Dictionary<string, long> PreShuffle;
        public required Dictionary<string, double> Idf;
        public required Dictionary<string, double> Rand;
        public required HashSet<string> Gold;
        public required int SeedGroups;
        public required int SeedFillers;
        public required int RuleCount;
        public required int GraphFiles;
        public required int Families;
        public required long FlowMass;
        public required long FlatMass;
        public required Dictionary<string, long> FlatByFiller;
        public required double HubCorr;
        public required long DegreeFlowMass;
        public required long DegreeFlatMass;
        public required Dictionary<string, long> DegreeFlatByFiller;
        public required double DegreeHubCorr;
        public required long PreFlowMass;
        public required long PreFlatMass;
        public required Dictionary<string, long> PreFlatByFiller;
        public required double PreHubCorr;
    }

    private sealed class Graph
    {
        public readonly Dictionary<string, FillerNode> Fillers = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, int>> Direct = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FileBytes = new(StringComparer.Ordinal);
        public readonly List<Family> Families = new();
        public FillerNode[] Universe = [];
    }

    private sealed class Family(int id, string left, string right)
    {
        public readonly int Id = id;
        public readonly string Left = left;
        public readonly string Right = right;
        public readonly List<Membership> Memberships = new();
    }

    private sealed class FillerNode(string token)
    {
        public readonly string Token = token;
        public int Fires;
        public int FileDegree;
        public readonly List<Membership> Memberships = new();
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue",
        "def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import",
        "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while",
        "with", "yield",
        "abstract", "base", "bool", "byte", "case", "catch", "char", "checked", "const", "decimal",
        "default", "delegate", "do", "double", "enum", "event", "explicit", "extern", "false", "fixed",
        "float", "foreach", "goto", "implicit", "int", "interface", "internal", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "record", "ref", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "var", "virtual", "void", "volatile", "where", "nameof", "required", "file", "global"
    };
}
