namespace Cogito;

using System.Security.Cryptography;
using System.Text;

// ── CO-INSTANTIATION LATTICE CENSUS ──
// The blur already proves that high-diversity paradigms emerge (`left ___ right`). The localization-bearing middle
// structure is the same identifier occupying multiple paradigms across files: e.g. a name in a call-frame in one
// file and a definition-frame in another. This probe measures that lattice before any walk consumes it, and checks
// it against a file-degree-preserving relabel null.
public static class LatticeCensus
{
    public const ulong DefaultSeed = 0x1A771CEUL;
    private const int NullDraws = 8;

    /// usage: lattice <dir> [--top K] [--all-families] [--seed HEX]
    public static int Run(string[] args)
    {
        var pos = Args.Positionals(args, 1);
        string source = pos.Count > 0 ? pos[0] : "";
        if (source.Length == 0) { Console.Error.WriteLine("lattice: missing <dir>"); return 1; }

        int top = Args.Int(args, "--top", 12);
        ulong seed = Args.Seed(args, "--seed", DefaultSeed);
        bool allFamilies = args.Contains("--all-families", StringComparer.Ordinal);

        var files = Blur.TokenizeSourceFiles(source);
        if (files.Count == 0) { Console.Error.WriteLine($"lattice: no tokenizable files under '{source}'"); return 1; }

        var frames = Blur.FrameCensusByFile(files);
        IReadOnlyList<Blur.FileFrame> selectedFrames = allFamilies
            ? frames
            : frames.Take(Math.Max(1, top)).ToArray();
        var families = BuildFamilies(selectedFrames);
        if (families.Count == 0) { Console.Error.WriteLine("lattice: top frames contain no identifier-like fillers"); return 1; }

        var fileRepo = files.ToDictionary(f => f.File, f => f.Repo, StringComparer.Ordinal);
        var lexicon = BuildFileLexicon(files);
        var real = Analyze(families, fileRepo);
        var roles = CollectRoleData(files, families);
        var callDef = AnalyzeCallDef(roles);
        var withinReal = AnalyzeWithinFamilies(roles);
        var defReal = AnalyzeDefInDiet(roles);

        var nulls = new List<Metrics>(NullDraws);
        var withinNulls = new List<WithinFamilyRead>(NullDraws);
        var defNulls = new List<DefInDietRead>(NullDraws);
        for (int i = 0; i < NullDraws; i++)
        {
            nulls.Add(Analyze(ShuffleFamilies(families, lexicon, seed ^ (0x9E3779B97F4A7C15UL * (ulong)(i + 1))), fileRepo));
            var shuffledRoles = ShuffleRoleData(roles, seed ^ (0xD1B54A32D192ED03UL * (ulong)(i + 1)));
            withinNulls.Add(AnalyzeWithinFamilies(shuffledRoles));
            defNulls.Add(AnalyzeDefInDiet(shuffledRoles));
        }
        var nullBand = NullBand.From(nulls);
        var withinNull = WithinFamilyNullBand.From(withinNulls);
        var defNull = DefInDietNullBand.From(defNulls);

        var run = Cogito.Run.New("lattice");
        run.Write("lattice_summary.tsv", SummaryTsv(source, files, families, real, nullBand, seed, allFamilies ? 0 : top));
        run.Write("lattice_edges.tsv", EdgesTsv(real));
        run.Write("lattice_components.tsv", ComponentsTsv(real));
        run.Write("lattice_families.tsv", FamiliesTsv(families));
        run.Write("lattice_call_def.tsv", CallDefTsv(callDef));
        run.Write("lattice_within_family.tsv", WithinFamilyTsv(withinReal, withinNull));
        run.Write("lattice_def_in_diet.tsv", DefInDietTsv(defReal, defNull));
        string authority = AuthorityTsv(source, files, families, defReal, defNull, seed, allFamilies, top);
        run.Write("lattice_authority.tsv", authority);
        string authoritySHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(authority)));
        run.Write("lattice_authority.sha256", authoritySHA256 + "\n");

        Console.WriteLine("lattice census — co-instantiation between blur paradigms, file-aware, read-only");
        Console.WriteLine($"  source      {source}");
        Console.WriteLine($"  corpus      {files.Count} files · {files.Sum(f => f.Sentences.Length)} token sentences · {files.Sum(f => f.Bytes)}B");
        Console.WriteLine($"  frames      {(allFamilies ? "ALL identifier-bearing" : $"top {top} by diversity")} → {families.Count} identifier-bearing families · seed {seed:X}");
        Console.WriteLine();

        PrintMetrics("REAL", real);
        Console.WriteLine();
        Console.WriteLine($"  NULL        {NullDraws} per-file degree-preserving relabel draws");
        Console.WriteLine($"              family_edges       real {real.FamilyEdges.Count,6}  null mean {nullBand.FamilyEdges.Mean,8:F1}  max {nullBand.FamilyEdges.Max,6}");
        Console.WriteLine($"              bridge_filler_pairs real {real.BridgeFillerPairs,6}  null mean {nullBand.BridgeFillerPairs.Mean,8:F1}  max {nullBand.BridgeFillerPairs.Max,6}");
        Console.WriteLine($"              distinct_fillers    real {real.BridgeFillers,6}  null mean {nullBand.BridgeFillers.Mean,8:F1}  max {nullBand.BridgeFillers.Max,6}");
        Console.WriteLine($"              file_edges          real {real.FileEdges.Count,6}  null mean {nullBand.FileEdges.Mean,8:F1}  max {nullBand.FileEdges.Max,6}");
        Console.WriteLine($"              largest_component   real {real.LargestComponentFiles,6}  null mean {nullBand.LargestComponentFiles.Mean,8:F1}  max {nullBand.LargestComponentFiles.Max,6}");
        Console.WriteLine();

        bool beatsBridge = real.BridgeFillerPairs > nullBand.BridgeFillerPairs.Max;
        bool beatsComponent = real.LargestComponentFiles > nullBand.LargestComponentFiles.Max || real.FileEdges.Count > nullBand.FileEdges.Max;
        string verdict = beatsBridge && beatsComponent ? "PASS — knitting beats the relabel null"
            : beatsBridge ? "MIXED — bridge count beats null, component geometry does not"
            : "FAIL — bridge count does not beat the relabel null";
        Console.WriteLine($"  CROSS-FAMILY VERDICT  {verdict}");
        Console.WriteLine();

        Console.WriteLine("  READ 4      call/def family collapse");
        Console.WriteLine($"              collapsed {callDef.CollapsedFamilies} · call-only {callDef.CallOnlyFamilies} · def-only {callDef.DefOnlyFamilies} · neither {callDef.NeitherFamilies}");
        foreach (var row in callDef.Rows)
            Console.WriteLine($"              F{row.FamilyId,-2} {row.Shape,-34} {row.Distinguishability,-13} call {row.CallOccurrences,4} def {row.DefOccurrences,4} amb {row.AmbiguousOccurrences,4}");
        Console.WriteLine();

        int withinBeatingFamilies = withinReal.Rows.Count(r => r.DensityHomeScore > withinNull.ForFamily(r.FamilyId).DensityHomeScore.Max);
        bool withinBeats = withinReal.DensityHomeScore > withinNull.DensityHomeScore.Max;
        Console.WriteLine("  READ 1      within-family cross-file density homes");
        Console.WriteLine($"              density_home_score real {withinReal.DensityHomeScore,8:F3}  null mean {withinNull.DensityHomeScore.Mean,8:F3}  max {withinNull.DensityHomeScore.Max,8:F3}");
        Console.WriteLine($"              beating families    {withinBeatingFamilies}/{withinReal.Rows.Length}  (per-family max null)");
        foreach (var row in withinReal.Rows.OrderByDescending(r => r.DensityHomeScore).Take(6))
        {
            var nb = withinNull.ForFamily(row.FamilyId).DensityHomeScore;
            Console.WriteLine($"              F{row.FamilyId,-2} score {row.DensityHomeScore,8:F3} null_max {nb.Max,8:F3} · multi {row.MultiFileFillers,4} · maxfiles {row.MaxFillerFiles,3} · {row.TopHomes}");
        }
        Console.WriteLine();

        bool defBeats = defReal.ConditionalRate > defNull.ConditionalRate.Max;
        Console.WriteLine("  READ 2      def-in-diet conditional knitting");
        Console.WriteLine($"              conditional fillers real {defReal.ConditionalFillers,6}  null mean {defNull.ConditionalFillers.Mean,8:F1}  max {defNull.ConditionalFillers.Max,6}");
        Console.WriteLine($"              knitting fillers    real {defReal.KnittingFillers,6}  null mean {defNull.KnittingFillers.Mean,8:F1}  max {defNull.KnittingFillers.Max,6}");
        Console.WriteLine($"              call_def filepairs  real {defReal.CrossFileCallDefPairs,6}  null mean {defNull.CrossFileCallDefPairs.Mean,8:F1}  max {defNull.CrossFileCallDefPairs.Max,6}");
        Console.WriteLine($"              conditional rate    real {defReal.ConditionalRate,8:P2}  null mean {defNull.ConditionalRate.Mean,8:P2}  max {defNull.ConditionalRate.Max,8:P2}");
        foreach (var row in defReal.TopFillers.Take(8))
            Console.WriteLine($"              {row}");
        Console.WriteLine();

        string rereadVerdict = withinBeats ? "KNITTING EXISTS, instrument missed it"
            : defBeats ? "KNITTING EXISTS only def-in-diet"
            : "ROLE-SEGREGATION IS TOTAL, the lattice route dies honestly";
        Console.WriteLine($"  REREAD VERDICT  {rereadVerdict}");
        bool opportunityGate = defReal.ConditionalFillers >= 2
            && defReal.CrossFileCallDefPairs >= 2
            && defBeats;
        if (allFamilies)
            Console.WriteLine($"  OPPORTUNITY GATE  {(opportunityGate ? "PASS" : "FAIL")} · conditional≥2={defReal.ConditionalFillers >= 2} · cross-file-pairs≥2={defReal.CrossFileCallDefPairs >= 2} · rate>all-null={defBeats}");
        Console.WriteLine($"  authority        {run.PathOf("lattice_authority.tsv")} · sha256={authoritySHA256}");
        Console.WriteLine($"  rendered → {run.Dir}/  (lattice_summary.tsv · lattice_edges.tsv · lattice_components.tsv · lattice_families.tsv · lattice_call_def.tsv · lattice_within_family.tsv · lattice_def_in_diet.tsv · lattice_authority.tsv)");
        return allFamilies && !opportunityGate ? 1 : 0;
    }

    private static void PrintMetrics(string label, Metrics m)
    {
        Console.WriteLine($"  {label,-4}        family nodes {m.Families.Count} · filler nodes {m.FillerNodes} · family edges {m.FamilyEdges.Count}");
        Console.WriteLine($"              bridge filler-pairs {m.BridgeFillerPairs} · distinct bridge fillers {m.BridgeFillers}");
        Console.WriteLine($"              file nodes {m.FileNodes.Count} · file edges {m.FileEdges.Count} · components {m.Components.Count} · largest {m.LargestComponentFiles} files");
        Console.WriteLine($"              filler-degree histogram: {FormatHist(m.FillerDegreeHistogram)}");
        foreach (var c in m.Components.Take(5))
            Console.WriteLine($"              component {c.Rank}: {c.Files.Length} files · {c.Edges} edges · repos {{{string.Join(" ", c.Repos)}}} · {{{string.Join(" ", c.Files.Take(6))}{(c.Files.Length > 6 ? " …" : "")}}}");
        Console.WriteLine("              top family bridges:");
        foreach (var e in m.FamilyEdges.Values.OrderByDescending(e => e.Fillers.Count).ThenByDescending(e => e.FilePairs.Count).ThenBy(e => e.A).ThenBy(e => e.B).Take(8))
            Console.WriteLine($"                F{e.A}<->F{e.B}  fillers {e.Fillers.Count,4}  file-pairs {e.FilePairs.Count,4}  {{{string.Join(" ", e.Fillers.Take(8))}{(e.Fillers.Count > 8 ? " …" : "")}}}");
    }

    private static List<Family> BuildFamilies(IReadOnlyList<Blur.FileFrame> frames)
    {
        var families = new List<Family>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            var fam = new Family(families.Count, f.Left, f.Right);
            foreach (var filler in f.Fillers)
            {
                if (!IsIdentifierLike(filler.Token)) continue;
                foreach (var fc in filler.Files) fam.Add(filler.Token, fc.File, fc.Count);
            }
            if (fam.Fillers.Count > 0) families.Add(fam);
        }
        return families;
    }

    private static Metrics Analyze(IReadOnlyList<Family> families, Dictionary<string, string> fileRepo)
    {
        var m = new Metrics(families);
        var byFiller = new SortedDictionary<string, List<(Family Family, FillerUse Use)>>(StringComparer.Ordinal);
        foreach (var family in families)
        {
            m.FillerNodes += family.Fillers.Count;
            foreach (var (filler, use) in family.Fillers)
                (byFiller.TryGetValue(filler, out var l) ? l : byFiller[filler] = new()).Add((family, use));
        }

        foreach (var (filler, uses) in byFiller)
        {
            if (uses.Count < 2) continue;
            var bridgeFamilies = new SortedSet<int>();
            for (int i = 0; i < uses.Count; i++)
                for (int j = i + 1; j < uses.Count; j++)
                {
                    var pairs = CrossFilePairs(uses[i].Use, uses[j].Use);
                    if (pairs.Count == 0) continue;
                    int a = Math.Min(uses[i].Family.Id, uses[j].Family.Id);
                    int b = Math.Max(uses[i].Family.Id, uses[j].Family.Id);
                    if (!m.FamilyEdges.TryGetValue((a, b), out var edge)) m.FamilyEdges[(a, b)] = edge = new FamilyEdge(a, b);
                    edge.Fillers.Add(filler);
                    foreach (var p in pairs)
                    {
                        edge.FilePairs.Add(p);
                        m.FileEdges.Add(p);
                        AddAdj(m.FileAdj, p.A, p.B);
                        AddAdj(m.FileAdj, p.B, p.A);
                    }
                    m.BridgeFillerPairs++;
                    bridgeFamilies.Add(a);
                    bridgeFamilies.Add(b);
                }
            if (bridgeFamilies.Count >= 2)
            {
                m.BridgeFillers++;
                m.FillerDegreeHistogram[bridgeFamilies.Count] = m.FillerDegreeHistogram.GetValueOrDefault(bridgeFamilies.Count) + 1;
            }
        }

        foreach (var f in m.FileAdj.Keys) m.FileNodes.Add(f);
        m.Components.AddRange(BuildComponents(m.FileAdj, m.FileEdges, fileRepo));
        m.LargestComponentFiles = m.Components.Count > 0 ? m.Components[0].Files.Length : 0;
        return m;
    }

    private static List<FamilyRoleData> CollectRoleData(IReadOnlyList<Blur.TokenFile> files, IReadOnlyList<Family> families)
    {
        var byKey = new Dictionary<(string Left, string Right), FamilyRoleData>();
        foreach (var family in families) byKey[(family.Left, family.Right)] = new FamilyRoleData(family);

        foreach (var file in files)
            foreach (var s in file.Sentences)
                for (int p = 1; p + 1 < s.Length; p++)
                {
                    if (!byKey.TryGetValue((s[p - 1], s[p + 1]), out var data)) continue;
                    if (!IsIdentifierLike(s[p])) continue;
                    data.Occurrences.Add(new RoleOccurrence(s[p], file.File, ClassifyRole(s, p)));
                }

        return families.Select(f => byKey[(f.Left, f.Right)]).ToList();
    }

    private static CallDefRead AnalyzeCallDef(IReadOnlyList<FamilyRoleData> families)
    {
        var rows = new List<CallDefFamilyRow>(families.Count);
        foreach (var data in families)
        {
            var callFillers = new SortedSet<string>(StringComparer.Ordinal);
            var defFillers = new SortedSet<string>(StringComparer.Ordinal);
            int calls = 0, defs = 0, ambiguous = 0, other = 0;
            foreach (var o in data.Occurrences)
            {
                switch (o.Role)
                {
                    case RoleKinds.Call: calls++; callFillers.Add(o.Token); break;
                    case RoleKinds.Def: defs++; defFillers.Add(o.Token); break;
                    case RoleKinds.Ambiguous: ambiguous++; break;
                    default: other++; break;
                }
            }

            int overlap = callFillers.Count(f => defFillers.Contains(f));
            string distinguishability = calls > 0 && defs > 0 ? "COLLAPSED"
                : calls > 0 ? "call-only"
                : defs > 0 ? "def-only"
                : ambiguous > 0 ? "ambiguous"
                : "not-call-def";
            rows.Add(new CallDefFamilyRow(data.Family.Id, ShowFrame(data.Family), data.Occurrences.Count,
                calls, defs, ambiguous, other, callFillers.Count, defFillers.Count, overlap, distinguishability));
        }
        return new CallDefRead(rows.ToArray());
    }

    private static WithinFamilyRead AnalyzeWithinFamilies(IReadOnlyList<FamilyRoleData> families)
    {
        var rows = new List<WithinFamilyRow>(families.Count);
        double totalScore = 0;
        foreach (var data in families)
        {
            var uses = BuildUses(data.Occurrences);
            int cells = 0, multi = 0, maxFiles = 0;
            double score = 0;
            var homes = new List<HomeHit>();
            foreach (var use in uses.Values)
            {
                cells += use.Files.Count;
                maxFiles = Math.Max(maxFiles, use.Files.Count);
                if (use.Files.Count < 2) continue;
                multi++;
                var (homeFile, homeCount) = HomeFile(use);
                double excess = homeCount - ((double)use.Fires / use.Files.Count);
                score += excess;
                homes.Add(new HomeHit(use.Token, homeFile, homeCount, use.Fires, use.Files.Count, excess));
            }

            totalScore += score;
            string topHomes = string.Join(" ", homes.OrderByDescending(h => h.Excess).ThenByDescending(h => h.Fires).ThenBy(h => h.Token, StringComparer.Ordinal)
                .Take(8)
                .Select(h => $"{h.Token}@{h.File}:{h.Home}/{h.Fires}/{h.Files}"));
            if (topHomes.Length == 0) topHomes = "(none)";
            rows.Add(new WithinFamilyRow(data.Family.Id, ShowFrame(data.Family), uses.Count, data.Occurrences.Count,
                data.Family.FileFires.Count, cells, multi, maxFiles, score, topHomes));
        }
        return new WithinFamilyRead(rows.ToArray(), totalScore);
    }

    private static DefInDietRead AnalyzeDefInDiet(IReadOnlyList<FamilyRoleData> families)
    {
        var byFiller = new SortedDictionary<string, ConditionalUse>(StringComparer.Ordinal);
        foreach (var data in families)
            foreach (var o in data.Occurrences)
            {
                if (o.Role is not (RoleKinds.Call or RoleKinds.Def)) continue;
                if (!byFiller.TryGetValue(o.Token, out var use)) byFiller[o.Token] = use = new ConditionalUse(o.Token);
                use.Add(o.Role, o.File);
            }

        int conditional = 0, knitting = 0, pairsTotal = 0;
        var top = new List<string>();
        foreach (var use in byFiller.Values)
        {
            if (use.CallOccurrences == 0 || use.DefOccurrences == 0) continue;
            conditional++;
            int pairs = CrossRoleFilePairs(use.CallFiles, use.DefFiles);
            pairsTotal += pairs;
            if (pairs > 0) knitting++;
            top.Add($"{use.Token}:pairs={pairs}:call={use.CallOccurrences}/{use.CallFiles.Count}:def={use.DefOccurrences}/{use.DefFiles.Count}");
        }

        top.Sort((a, b) => string.CompareOrdinal(a, b));
        top = top.OrderByDescending(ParsePairCount).ThenBy(x => x, StringComparer.Ordinal).Take(32).ToList();
        double rate = conditional == 0 ? 0 : (double)knitting / conditional;
        return new DefInDietRead(conditional, knitting, pairsTotal, rate, top.ToArray());
    }

    private static List<FamilyRoleData> ShuffleRoleData(IReadOnlyList<FamilyRoleData> families, ulong seed)
    {
        var shuffled = new List<FamilyRoleData>(families.Count);
        foreach (var data in families)
        {
            var nd = new FamilyRoleData(data.Family);
            var tokens = data.Occurrences.Select(o => o.Token).ToArray();
            ulong rng = Mix(seed, (ulong)data.Family.Id, (ulong)tokens.Length);
            for (int i = tokens.Length - 1; i > 0; i--)
            {
                int j = NextIndex(ref rng, i + 1);
                (tokens[i], tokens[j]) = (tokens[j], tokens[i]);
            }

            for (int i = 0; i < data.Occurrences.Count; i++)
            {
                var slot = data.Occurrences[i];
                nd.Occurrences.Add(new RoleOccurrence(tokens[i], slot.File, slot.Role));
            }
            shuffled.Add(nd);
        }
        return shuffled;
    }

    private static List<Family> ShuffleFamilies(IReadOnlyList<Family> families, Dictionary<string, FileLexicon> lexicon, ulong seed)
    {
        var shuffled = new List<Family>(families.Count);
        foreach (var family in families)
        {
            var nf = new Family(family.Id, family.Left, family.Right);
            foreach (var (file, count) in family.FileFires)
            {
                if (!lexicon.TryGetValue(file, out var lx) || lx.Total == 0) continue;
                ulong rng = Mix(seed, (ulong)family.Id, StableHash(file));
                for (int i = 0; i < count; i++) nf.Add(Draw(lx, ref rng), file, 1);
            }
            shuffled.Add(nf);
        }
        return shuffled;
    }

    private static List<FilePair> CrossFilePairs(FillerUse a, FillerUse b)
    {
        var seen = new HashSet<FilePair>();
        foreach (var fa in a.Files.Keys)
            foreach (var fb in b.Files.Keys)
                if (!string.Equals(fa, fb, StringComparison.Ordinal))
                    seen.Add(FilePair.Make(fa, fb));
        var pairs = seen.ToList();
        pairs.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
        return pairs;
    }

    private static int CrossRoleFilePairs(SortedSet<string> calls, SortedSet<string> defs)
    {
        var seen = new HashSet<FilePair>();
        foreach (var call in calls)
            foreach (var def in defs)
                if (!string.Equals(call, def, StringComparison.Ordinal))
                    seen.Add(FilePair.Make(call, def));
        return seen.Count;
    }

    private static Dictionary<string, FillerUse> BuildUses(IEnumerable<RoleOccurrence> occurrences)
    {
        var uses = new Dictionary<string, FillerUse>(StringComparer.Ordinal);
        foreach (var o in occurrences)
        {
            if (!uses.TryGetValue(o.Token, out var use)) uses[o.Token] = use = new FillerUse(o.Token);
            use.Add(o.File, 1);
        }
        return uses;
    }

    private static (string File, int Count) HomeFile(FillerUse use)
    {
        string file = "";
        int count = -1;
        foreach (var (f, c) in use.Files)
        {
            if (c < count) continue;
            if (c == count && string.CompareOrdinal(f, file) >= 0) continue;
            file = f;
            count = c;
        }
        return (file, count);
    }

    private static RoleKinds ClassifyRole(string[] s, int p)
    {
        if (!string.Equals(s[p + 1], "(", StringComparison.Ordinal)) return RoleKinds.Other;
        string left = s[p - 1];
        if (CallLeftTokens.Contains(left)) return RoleKinds.Call;
        if (LooksLikeDefinition(s, p)) return RoleKinds.Def;
        return RoleKinds.Ambiguous;
    }

    private static bool LooksLikeDefinition(string[] s, int p)
    {
        string left = s[p - 1];
        if (left == "." || left == "new") return false;
        if (DefinitionLeftTokens.Contains(left)) return true;
        if (left == ">" || left == "]") return true;
        if (left.Length > 0 && char.IsUpper(left[0])) return true;
        int lo = Math.Max(0, p - 5);
        for (int i = lo; i < p; i++)
            if (DeclarationCueTokens.Contains(s[i])) return true;
        return false;
    }

    private static int ParsePairCount(string s)
    {
        const string marker = ":pairs=";
        int start = s.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return 0;
        start += marker.Length;
        int end = s.IndexOf(':', start);
        if (end < 0) end = s.Length;
        return int.TryParse(s[start..end], out int n) ? n : 0;
    }

    private static List<Component> BuildComponents(Dictionary<string, SortedSet<string>> adj, HashSet<FilePair> edges, Dictionary<string, string> fileRepo)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var comps = new List<Component>();
        foreach (var root in adj.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!seen.Add(root)) continue;
            var stack = new Stack<string>(); stack.Push(root);
            var files = new SortedSet<string>(StringComparer.Ordinal) { root };
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                if (!adj.TryGetValue(f, out var ns)) continue;
                foreach (var n in ns) if (seen.Add(n)) { files.Add(n); stack.Push(n); }
            }
            int edgeCount = edges.Count(e => files.Contains(e.A) && files.Contains(e.B));
            var repos = files.Select(f => fileRepo.TryGetValue(f, out var r) ? r : "?").Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToArray();
            comps.Add(new Component(0, files.ToArray(), repos, edgeCount));
        }
        comps.Sort((a, b) => a.Files.Length != b.Files.Length ? b.Files.Length.CompareTo(a.Files.Length)
                          : a.Edges != b.Edges ? b.Edges.CompareTo(a.Edges)
                          : string.CompareOrdinal(a.Files[0], b.Files[0]));
        for (int i = 0; i < comps.Count; i++) comps[i] = comps[i] with { Rank = i + 1 };
        return comps;
    }

    private static Dictionary<string, FileLexicon> BuildFileLexicon(IReadOnlyList<Blur.TokenFile> files)
    {
        var outp = new Dictionary<string, FileLexicon>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in file.Sentences)
                foreach (var t in s)
                    if (IsIdentifierLike(t)) counts[t] = counts.GetValueOrDefault(t) + 1;
            var toks = counts.Keys.OrderBy(t => t, StringComparer.Ordinal).ToArray();
            var cum = new long[toks.Length];
            long acc = 0;
            for (int i = 0; i < toks.Length; i++) { acc += counts[toks[i]]; cum[i] = acc; }
            outp[file.File] = new FileLexicon(toks, cum, acc);
        }
        return outp;
    }

    private static string Draw(in FileLexicon lx, ref ulong rng)
    {
        rng = rng * 6364136223846793005UL + 1442695040888963407UL;
        long r = (long)((rng >> 11) % (ulong)lx.Total);
        int lo = 0, hi = lx.Tokens.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (lx.Cumulative[mid] <= r) lo = mid + 1;
            else hi = mid;
        }
        return lx.Tokens[lo];
    }

    private static void AddAdj(Dictionary<string, SortedSet<string>> adj, string a, string b)
        => (adj.TryGetValue(a, out var ns) ? ns : adj[a] = new(StringComparer.Ordinal)).Add(b);

    private static int NextIndex(ref ulong rng, int count)
    {
        rng = rng * 6364136223846793005UL + 1442695040888963407UL;
        return (int)((rng >> 11) % (ulong)count);
    }

    private static ulong Mix(ulong a, ulong b, ulong c)
    {
        ulong x = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
        x ^= c + 0xBF58476D1CE4E5B9UL + (x << 6) + (x >> 2);
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private static ulong StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
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

    private static string FormatHist(Dictionary<int, int> hist)
        => hist.Count == 0 ? "(empty)" : string.Join(" ", hist.OrderBy(kv => kv.Key).Select(kv => $"d{kv.Key}:{kv.Value}"));

    private static string ShowFrame(Family f) => $"«{Trunc(f.Left, 24)}»___«{Trunc(f.Right, 24)}»";

    private static string Trunc(string s, int cap)
    {
        var t = s.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);
        return t.Length > cap ? t[..cap] + "..." : t;
    }

    /// The compact authority consumed by a registration builder. It binds the census to the
    /// exact source world and records the positive opportunity gate without naming a policy,
    /// event, candidate, or desired outcome.
    private static string AuthorityTsv(string source, IReadOnlyList<Blur.TokenFile> files,
        IReadOnlyList<Family> families, DefInDietRead real, DefInDietNullBand nullBand,
        ulong seed, bool allFamilies, int top)
    {
        string worldSHA256 = FileCorpus.ComputeWorldSHA256(source, CogitoCorpus.DefaultGlob);
        var sb = new StringBuilder("schema\t1\n");
        sb.Append("source\t").Append(source).Append('\n');
        sb.Append("world_sha256\t").Append(worldSHA256).Append('\n');
        sb.Append("frame_mode\t").Append(allFamilies ? "all-identifier-families" : "top-diversity").Append('\n');
        sb.Append("top\t").Append(top).Append('\n');
        sb.Append("seed\t").Append(seed.ToString("X")).Append('\n');
        sb.Append("files\t").Append(files.Count).Append('\n');
        sb.Append("families\t").Append(families.Count).Append('\n');
        sb.Append("conditional_fillers\t").Append(real.ConditionalFillers).Append('\n');
        sb.Append("cross_file_call_def_pairs\t").Append(real.CrossFileCallDefPairs).Append('\n');
        sb.Append("conditional_rate\t").Append(real.ConditionalRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("null_max_conditional_rate\t").Append(nullBand.ConditionalRate.Max.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("opportunity_gate\t").Append(real.ConditionalFillers >= 2 && real.CrossFileCallDefPairs >= 2
            && real.ConditionalRate > nullBand.ConditionalRate.Max ? "pass" : "fail").Append('\n');
        return sb.ToString();
    }

    private static string SummaryTsv(string source, IReadOnlyList<Blur.TokenFile> files, IReadOnlyList<Family> families, Metrics real, NullBand nullBand, ulong seed, int top)
    {
        var sb = new StringBuilder();
        sb.AppendLine("metric\treal\tnull_mean\tnull_max");
        sb.AppendLine($"source\t{source}\t\t");
        sb.AppendLine($"seed\t{seed:X}\t\t");
        sb.AppendLine($"top\t{top}\t\t");
        sb.AppendLine($"files\t{files.Count}\t\t");
        sb.AppendLine($"sentences\t{files.Sum(f => f.Sentences.Length)}\t\t");
        sb.AppendLine($"bytes\t{files.Sum(f => f.Bytes)}\t\t");
        sb.AppendLine($"family_nodes\t{families.Count}\t\t");
        sb.AppendLine($"filler_nodes\t{real.FillerNodes}\t\t");
        sb.AppendLine($"family_edges\t{real.FamilyEdges.Count}\t{nullBand.FamilyEdges.Mean:F3}\t{nullBand.FamilyEdges.Max}");
        sb.AppendLine($"bridge_filler_pairs\t{real.BridgeFillerPairs}\t{nullBand.BridgeFillerPairs.Mean:F3}\t{nullBand.BridgeFillerPairs.Max}");
        sb.AppendLine($"bridge_fillers\t{real.BridgeFillers}\t{nullBand.BridgeFillers.Mean:F3}\t{nullBand.BridgeFillers.Max}");
        sb.AppendLine($"file_edges\t{real.FileEdges.Count}\t{nullBand.FileEdges.Mean:F3}\t{nullBand.FileEdges.Max}");
        sb.AppendLine($"components\t{real.Components.Count}\t{nullBand.Components.Mean:F3}\t{nullBand.Components.Max}");
        sb.AppendLine($"largest_component_files\t{real.LargestComponentFiles}\t{nullBand.LargestComponentFiles.Mean:F3}\t{nullBand.LargestComponentFiles.Max}");
        sb.AppendLine($"filler_degree_hist\t{FormatHist(real.FillerDegreeHistogram)}\t\t");
        return sb.ToString();
    }

    private static string EdgesTsv(Metrics m)
    {
        var sb = new StringBuilder("family_a\tframe_a\tfamily_b\tframe_b\tbridge_fillers\tfile_pairs\tfillers\n");
        foreach (var e in m.FamilyEdges.Values.OrderByDescending(e => e.Fillers.Count).ThenByDescending(e => e.FilePairs.Count).ThenBy(e => e.A).ThenBy(e => e.B))
            sb.AppendLine($"F{e.A}\t{ShowFrame(m.Families[e.A])}\tF{e.B}\t{ShowFrame(m.Families[e.B])}\t{e.Fillers.Count}\t{e.FilePairs.Count}\t{string.Join(" ", e.Fillers)}");
        return sb.ToString();
    }

    private static string ComponentsTsv(Metrics m)
    {
        var sb = new StringBuilder("rank\tfiles\tedges\trepos\tfile_labels\n");
        foreach (var c in m.Components)
            sb.AppendLine($"{c.Rank}\t{c.Files.Length}\t{c.Edges}\t{string.Join(" ", c.Repos)}\t{string.Join(" ", c.Files)}");
        return sb.ToString();
    }

    private static string FamiliesTsv(IReadOnlyList<Family> families)
    {
        var sb = new StringBuilder("family\tframe\tidentifier_fillers\tidentifier_fires\tfiles\ttop_fillers\n");
        foreach (var f in families)
        {
            var top = f.Fillers.Values.OrderByDescending(u => u.Fires).ThenBy(u => u.Token, StringComparer.Ordinal).Take(16).Select(u => $"{u.Token}:{u.Fires}");
            sb.AppendLine($"F{f.Id}\t{ShowFrame(f)}\t{f.Fillers.Count}\t{f.FileFires.Values.Sum()}\t{f.FileFires.Count}\t{string.Join(" ", top)}");
        }
        return sb.ToString();
    }

    private static string CallDefTsv(CallDefRead read)
    {
        var sb = new StringBuilder("family\tframe\toccurrences\tcall_occurrences\tdef_occurrences\tambiguous_occurrences\tother_occurrences\tcall_fillers\tdef_fillers\tcall_def_fillers\tdistinguishability\n");
        foreach (var r in read.Rows)
            sb.AppendLine($"F{r.FamilyId}\t{r.Shape}\t{r.Occurrences}\t{r.CallOccurrences}\t{r.DefOccurrences}\t{r.AmbiguousOccurrences}\t{r.OtherOccurrences}\t{r.CallFillers}\t{r.DefFillers}\t{r.CallDefFillers}\t{r.Distinguishability}");
        return sb.ToString();
    }

    private static string WithinFamilyTsv(WithinFamilyRead real, WithinFamilyNullBand nullBand)
    {
        var sb = new StringBuilder("family\tframe\tfillers\tfires\tfiles\tincidence_cells\tmulti_file_fillers\tmax_filler_files\tdensity_home_score\tnull_mean\tnull_max\tbeats_null\ttop_density_homes\n");
        foreach (var r in real.Rows)
        {
            var nb = nullBand.ForFamily(r.FamilyId).DensityHomeScore;
            sb.AppendLine($"F{r.FamilyId}\t{r.Shape}\t{r.Fillers}\t{r.Fires}\t{r.Files}\t{r.IncidenceCells}\t{r.MultiFileFillers}\t{r.MaxFillerFiles}\t{r.DensityHomeScore:F3}\t{nb.Mean:F3}\t{nb.Max:F3}\t{r.DensityHomeScore > nb.Max}\t{r.TopHomes}");
        }
        return sb.ToString();
    }

    private static string DefInDietTsv(DefInDietRead real, DefInDietNullBand nullBand)
    {
        var sb = new StringBuilder("metric\treal\tnull_mean\tnull_max\n");
        sb.AppendLine($"conditional_fillers\t{real.ConditionalFillers}\t{nullBand.ConditionalFillers.Mean:F3}\t{nullBand.ConditionalFillers.Max}");
        sb.AppendLine($"knitting_fillers\t{real.KnittingFillers}\t{nullBand.KnittingFillers.Mean:F3}\t{nullBand.KnittingFillers.Max}");
        sb.AppendLine($"cross_file_call_def_pairs\t{real.CrossFileCallDefPairs}\t{nullBand.CrossFileCallDefPairs.Mean:F3}\t{nullBand.CrossFileCallDefPairs.Max}");
        sb.AppendLine($"conditional_rate\t{real.ConditionalRate:F6}\t{nullBand.ConditionalRate.Mean:F6}\t{nullBand.ConditionalRate.Max:F6}");
        sb.AppendLine();
        sb.AppendLine("filler\tpairs\tcall_occurrences\tcall_files\tdef_occurrences\tdef_files");
        foreach (var row in real.TopFillers)
        {
            var cols = row.Split(':');
            if (cols.Length < 4) { sb.AppendLine(row); continue; }
            string filler = cols[0];
            string pairs = cols[1].Replace("pairs=", "", StringComparison.Ordinal);
            var call = cols[2].Replace("call=", "", StringComparison.Ordinal).Split('/');
            var def = cols[3].Replace("def=", "", StringComparison.Ordinal).Split('/');
            sb.AppendLine($"{filler}\t{pairs}\t{call[0]}\t{call[1]}\t{def[0]}\t{def[1]}");
        }
        return sb.ToString();
    }

    private sealed class Family
    {
        public readonly int Id;
        public readonly string Left;
        public readonly string Right;
        public readonly Dictionary<string, FillerUse> Fillers = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FileFires = new(StringComparer.Ordinal);

        public Family(int id, string left, string right) { Id = id; Left = left; Right = right; }

        public void Add(string filler, string file, int count)
        {
            if (!Fillers.TryGetValue(filler, out var use)) Fillers[filler] = use = new FillerUse(filler);
            use.Add(file, count);
            FileFires[file] = FileFires.GetValueOrDefault(file) + count;
        }
    }

    private sealed class FillerUse
    {
        public readonly string Token;
        public readonly Dictionary<string, int> Files = new(StringComparer.Ordinal);
        public int Fires;

        public FillerUse(string token) => Token = token;

        public void Add(string file, int count)
        {
            Files[file] = Files.GetValueOrDefault(file) + count;
            Fires += count;
        }
    }

    private sealed class FamilyEdge
    {
        public readonly int A;
        public readonly int B;
        public readonly SortedSet<string> Fillers = new(StringComparer.Ordinal);
        public readonly HashSet<FilePair> FilePairs = new();
        public FamilyEdge(int a, int b) { A = a; B = b; }
    }

    private enum RoleKinds { Other, Call, Def, Ambiguous }

    private sealed class FamilyRoleData
    {
        public readonly Family Family;
        public readonly List<RoleOccurrence> Occurrences = new();
        public FamilyRoleData(Family family) => Family = family;
    }

    private sealed class ConditionalUse
    {
        public readonly string Token;
        public readonly SortedSet<string> CallFiles = new(StringComparer.Ordinal);
        public readonly SortedSet<string> DefFiles = new(StringComparer.Ordinal);
        public int CallOccurrences;
        public int DefOccurrences;

        public ConditionalUse(string token) => Token = token;

        public void Add(RoleKinds role, string file)
        {
            if (role == RoleKinds.Call)
            {
                CallOccurrences++;
                CallFiles.Add(file);
            }
            else if (role == RoleKinds.Def)
            {
                DefOccurrences++;
                DefFiles.Add(file);
            }
        }
    }

    private readonly record struct RoleOccurrence(string Token, string File, RoleKinds Role);

    private readonly record struct CallDefFamilyRow(int FamilyId, string Shape, int Occurrences,
        int CallOccurrences, int DefOccurrences, int AmbiguousOccurrences, int OtherOccurrences,
        int CallFillers, int DefFillers, int CallDefFillers, string Distinguishability);

    private sealed class CallDefRead
    {
        public readonly CallDefFamilyRow[] Rows;
        public readonly int CollapsedFamilies;
        public readonly int CallOnlyFamilies;
        public readonly int DefOnlyFamilies;
        public readonly int NeitherFamilies;

        public CallDefRead(CallDefFamilyRow[] rows)
        {
            Rows = rows;
            CollapsedFamilies = rows.Count(r => r.CallOccurrences > 0 && r.DefOccurrences > 0);
            CallOnlyFamilies = rows.Count(r => r.CallOccurrences > 0 && r.DefOccurrences == 0);
            DefOnlyFamilies = rows.Count(r => r.CallOccurrences == 0 && r.DefOccurrences > 0);
            NeitherFamilies = rows.Length - CollapsedFamilies - CallOnlyFamilies - DefOnlyFamilies;
        }
    }

    private readonly record struct HomeHit(string Token, string File, int Home, int Fires, int Files, double Excess);

    private readonly record struct WithinFamilyRow(int FamilyId, string Shape, int Fillers, int Fires, int Files,
        int IncidenceCells, int MultiFileFillers, int MaxFillerFiles, double DensityHomeScore, string TopHomes);

    private readonly record struct WithinFamilyRead(WithinFamilyRow[] Rows, double DensityHomeScore);

    private readonly record struct DefInDietRead(int ConditionalFillers, int KnittingFillers,
        int CrossFileCallDefPairs, double ConditionalRate, string[] TopFillers);

    private sealed class Metrics
    {
        public readonly IReadOnlyList<Family> Families;
        public readonly Dictionary<(int A, int B), FamilyEdge> FamilyEdges = new();
        public readonly HashSet<FilePair> FileEdges = new();
        public readonly Dictionary<string, SortedSet<string>> FileAdj = new(StringComparer.Ordinal);
        public readonly SortedSet<string> FileNodes = new(StringComparer.Ordinal);
        public readonly Dictionary<int, int> FillerDegreeHistogram = new();
        public readonly List<Component> Components = new();
        public int FillerNodes;
        public int BridgeFillerPairs;
        public int BridgeFillers;
        public int LargestComponentFiles;

        public Metrics(IReadOnlyList<Family> families) => Families = families;
    }

    private readonly record struct FilePair(string A, string B)
    {
        public string Key => A + "\u001f" + B;
        public static FilePair Make(string a, string b)
            => string.CompareOrdinal(a, b) <= 0 ? new FilePair(a, b) : new FilePair(b, a);
    }

    private readonly record struct Component(int Rank, string[] Files, string[] Repos, int Edges);

    private readonly record struct FileLexicon(string[] Tokens, long[] Cumulative, long Total);

    private readonly record struct NullScalar(double Mean, int Max)
    {
        public static NullScalar From(IEnumerable<int> xs)
        {
            var a = xs.ToArray();
            return a.Length == 0 ? new NullScalar(0, 0) : new NullScalar(a.Average(), a.Max());
        }
    }

    private readonly record struct NullBand(NullScalar FamilyEdges, NullScalar BridgeFillerPairs, NullScalar BridgeFillers,
        NullScalar FileEdges, NullScalar Components, NullScalar LargestComponentFiles)
    {
        public static NullBand From(IReadOnlyList<Metrics> ms) => new(
            NullScalar.From(ms.Select(m => m.FamilyEdges.Count)),
            NullScalar.From(ms.Select(m => m.BridgeFillerPairs)),
            NullScalar.From(ms.Select(m => m.BridgeFillers)),
            NullScalar.From(ms.Select(m => m.FileEdges.Count)),
            NullScalar.From(ms.Select(m => m.Components.Count)),
            NullScalar.From(ms.Select(m => m.LargestComponentFiles)));
    }

    private readonly record struct NullDoubleScalar(double Mean, double Max)
    {
        public static NullDoubleScalar From(IEnumerable<double> xs)
        {
            var a = xs.ToArray();
            return a.Length == 0 ? new NullDoubleScalar(0, 0) : new NullDoubleScalar(a.Average(), a.Max());
        }
    }

    private readonly record struct WithinFamilyNullRow(int FamilyId, NullDoubleScalar DensityHomeScore);

    private sealed class WithinFamilyNullBand
    {
        public readonly NullDoubleScalar DensityHomeScore;
        private readonly Dictionary<int, WithinFamilyNullRow> _families;

        private WithinFamilyNullBand(NullDoubleScalar densityHomeScore, Dictionary<int, WithinFamilyNullRow> families)
        {
            DensityHomeScore = densityHomeScore;
            _families = families;
        }

        public WithinFamilyNullRow ForFamily(int familyId)
            => _families.TryGetValue(familyId, out var row) ? row : new WithinFamilyNullRow(familyId, new NullDoubleScalar(0, 0));

        public static WithinFamilyNullBand From(IReadOnlyList<WithinFamilyRead> reads)
        {
            var ids = reads.SelectMany(r => r.Rows.Select(row => row.FamilyId)).Distinct().OrderBy(x => x).ToArray();
            var rows = new Dictionary<int, WithinFamilyNullRow>();
            foreach (int id in ids)
                rows[id] = new WithinFamilyNullRow(id, NullDoubleScalar.From(reads.Select(r => r.Rows.First(row => row.FamilyId == id).DensityHomeScore)));
            return new WithinFamilyNullBand(NullDoubleScalar.From(reads.Select(r => r.DensityHomeScore)), rows);
        }
    }

    private readonly record struct DefInDietNullBand(NullScalar ConditionalFillers, NullScalar KnittingFillers,
        NullScalar CrossFileCallDefPairs, NullDoubleScalar ConditionalRate)
    {
        public static DefInDietNullBand From(IReadOnlyList<DefInDietRead> reads) => new(
            NullScalar.From(reads.Select(r => r.ConditionalFillers)),
            NullScalar.From(reads.Select(r => r.KnittingFillers)),
            NullScalar.From(reads.Select(r => r.CrossFileCallDefPairs)),
            NullDoubleScalar.From(reads.Select(r => r.ConditionalRate)));
    }

    private static readonly HashSet<string> CallLeftTokens = new(StringComparer.Ordinal)
    {
        ".", "new", "return", "throw", "await", "yield", "=", "=>", "(", "[", ",", ":", "?", "!", "&&", "||",
        "if", "while", "for", "foreach", "switch", "using", "lock", "catch", "when", "typeof", "sizeof", "nameof",
        "default", "checked", "unchecked"
    };

    private static readonly HashSet<string> DefinitionLeftTokens = new(StringComparer.Ordinal)
    {
        "void", "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double",
        "decimal", "char", "string", "object", "nint", "nuint", "Task", "ValueTask", "IEnumerable",
        "IReadOnlyList", "IReadOnlyDictionary", "List", "Dictionary", "SortedDictionary", "HashSet", "SortedSet",
        "Span", "ReadOnlySpan", "Memory", "ReadOnlyMemory", "StringBuilder"
    };

    private static readonly HashSet<string> DeclarationCueTokens = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed",
        "extern", "async", "unsafe", "partial", "readonly", "required"
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile",
        "while", "where", "yield", "async", "await", "nameof", "required", "file", "global"
    };
}
