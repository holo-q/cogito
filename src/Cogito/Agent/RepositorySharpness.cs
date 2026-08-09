namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

/// G6 — the sharpness organ, query-conditioned.
///
/// The crawler already corroborates: it can tell how much of the world it has corroborated. What it has
/// never had is DISCRIMINATION — given a question, which region deserves the next look. Four prior
/// costumes for this died, and they died the same death: every one of them scored a region by
/// something INTRINSIC (its mass, its rule count, its differential MDL), and intrinsic scores track
/// FILE SIZE, not relevance. The banked verdict was explicit — the missing ingredient is QUERY
/// CONDITIONING.
///
/// So this organ has no per-region score at all. It has a FLOW. The query seeds activation on the
/// grammar's own structure — the rules whose expansions carry the query's words light up — and that
/// activation reaches a region only through the rules it actually shares with the question. A region
/// nothing seeded can reach scores nothing, however large it is. Value here is a relation to a
/// stimulus, never a property of the thing.
///
/// The null is the one that killed the costumes: BM25 over query tokens, the idf class. Beating idf
/// is the whole bar; matching it means the organ is idf wearing grammar.
internal static class RepositorySharpness
{
    /// Rules shorter than this carry no locality — a 3-byte expansion is in every file. Longer than
    /// the cap they are effectively whole lines, which is memorization rather than structure.
    private const int MinRuleSpan = 8, MaxRuleSpan = 96;

    /// How many seeded rules a query may trace. Tracing a rule costs a scan of the whole world, and
    /// the short tail of seeded rules is the weakest and most numerous part of the flow.
    internal const int SeededRuleBudget = 64;

    internal readonly record struct Region(string Path, string Text);

    /// The activation a query induces over the regions: one score per region, seeded by the query's
    /// words and carried only by the rules that contain them.
    internal static double[] Activate(Region[] regions, string[] ruleExpansions, IReadOnlyList<string> queryTokens,
        Dictionary<string, int[]>? reachCache = null)
    {
        // SEEDING: a rule participates only if the question's words are inside it. This is the one
        // condition that separates this organ from every costume that died — no query, no flow.
        List<string> seeded = [];
        foreach (string expansion in ruleExpansions)
        {
            if (expansion.Length < MinRuleSpan || expansion.Length > MaxRuleSpan) continue;
            foreach (string token in queryTokens)
                if (expansion.Contains(token, StringComparison.OrdinalIgnoreCase)) { seeded.Add(expansion); break; }
        }

        // The longest seeded rules carry the most locality, and the tail is both weak and by far the
        // most expensive to trace — a rule's reach costs a scan of the whole world. The cap is a
        // stated budget, not a silent truncation: it is reported with the result.
        seeded.Sort(static (left, right) => right.Length.CompareTo(left.Length));
        double[] activation = new double[regions.Length];
        foreach (string expansion in seeded.Take(SeededRuleBudget))
        {
            int[] hits = ReachOf(regions, expansion, reachCache);
            if (hits.Length == 0) continue;
            // A rule present nearly everywhere transmits nearly nothing — idf's insight applied to
            // STRUCTURE, and only along the path the query opened.
            double transmitted = Math.Log(1 + (double)regions.Length / hits.Length);
            foreach (int index in hits) activation[index] += transmitted;
        }
        return activation;
    }

    /// Which regions a rule reaches. Memoized because reach is a property of the rule and the world,
    /// not of the question — recomputing it per query would scan the whole corpus again for nothing.
    private static int[] ReachOf(Region[] regions, string expansion, Dictionary<string, int[]>? cache)
    {
        if (cache is not null && cache.TryGetValue(expansion, out int[]? cached)) return cached;
        List<int> hits = [];
        for (int index = 0; index < regions.Length; index++)
            if (regions[index].Text.Contains(expansion, StringComparison.Ordinal)) hits.Add(index);
        int[] reach = [.. hits];
        cache?.Add(expansion, reach);
        return reach;
    }
}

/// G6's kill-line — the organ against the idf class that killed its four predecessors, on held-out
/// localization over the real repository.
///
/// A query is a symbol's NAME split into words, and the gold is the file that declares it. The
/// symbol's own spelling is withheld from the query on purpose: with it, localization degenerates
/// into grep and every ranker scores perfectly. Split into its words the question becomes what a
/// crawler is actually asked — "where does the repository tool mediation live" — and answering it
/// requires knowing which region those words BELONG to, not which region contains their string.
///
/// Two numbers, because they are different faculties and the gap between them is the finding that
/// has stood since the corroboration engine was named: FIND is gold anywhere in the top-K (the
/// question was corroborated at all), COMMIT is gold at rank 1 (the organism would have acted on it).
/// A wide gap is a mind that recognizes but cannot decide.
internal static class RepositorySharpnessNull
{
    internal const int TopK = 5;

    /// Queries are sampled evenly across the mined set rather than taking a prefix — an alphabetical
    /// prefix of type names is a prefix of the codebase, not a sample of it.
    internal const int QueryBudget = 40;

    internal static bool Verify(TextWriter output)
    {
        try
        {
            string root = FindRepositoryRoot()
                ?? throw new InvalidDataException("repository root not found — the sharpness gate localizes in the real repository, and will not synthesize one");
            RepositorySharpness.Region[] regions = ReadRegions(root);
            (string[] Tokens, int Gold, string Symbol)[] queries = BuildQueries(regions);
            if (queries.Length < 8)
                throw new InvalidDataException($"only {queries.Length} uniquely-declared symbols found — too few to hold anything out");

            // Held out by construction: the grammar is induced over the corpus, never over the
            // queries, and no query's gold is used to build either ranker.
            List<byte> corpus = [];
            foreach (RepositorySharpness.Region region in regions) { corpus.AddRange(Encoding.UTF8.GetBytes(region.Text)); corpus.Add((byte)'\n'); }
            (_, _, RePairResult grammar) = Engine.Induce([.. corpus]);
            string[] expansions = ExpandRules(grammar);

            Bm25Index index = new([.. regions.Select(static region => region.Text)]);

            int organFind = 0, organCommit = 0, nullFind = 0, nullCommit = 0;
            Dictionary<string, int[]> reach = new(StringComparer.Ordinal);
            queries = [.. queries.Where((_, index) => index % Math.Max(1, queries.Length / QueryBudget) == 0).Take(QueryBudget)];
            int goldReached = 0, litRegions = 0, deadQueries = 0;
            foreach ((string[] tokens, int gold, _) in queries)
            {
                double[] organ = RepositorySharpness.Activate(regions, expansions, tokens, reach);
                double[] idf = index.Score([.. tokens]);
                // Did the flow ever ARRIVE? A verdict against an organ that was never reached would
                // be a verdict against the harness wearing the organ's name.
                if (organ[gold] > 0) goldReached++;
                int lit = organ.Count(static value => value > 0);
                litRegions += lit;
                if (lit == 0) deadQueries++;
                (int organRank, int nullRank) = (RankOf(organ, gold), RankOf(idf, gold));
                if (organRank < TopK) organFind++;
                if (organRank == 0) organCommit++;
                if (nullRank < TopK) nullFind++;
                if (nullRank == 0) nullCommit++;
            }

            double organFindRate = (double)organFind / queries.Length, organCommitRate = (double)organCommit / queries.Length;
            double nullFindRate = (double)nullFind / queries.Length, nullCommitRate = (double)nullCommit / queries.Length;

            output.WriteLine($"    world · {regions.Length} regions · {queries.Length} held-out queries · {grammar.Rules.Length} rules"
                           + $" · ≤{RepositorySharpness.SeededRuleBudget} seeded rules traced per query");
            output.WriteLine($"    flow  · gold reached {goldReached}/{queries.Length} queries · {(double)litRegions / queries.Length:F1} regions lit per query"
                           + $" · {deadQueries} queries lit nothing at all");
            output.WriteLine($"    organ · find@{TopK} {organFindRate:P1} · commit@1 {organCommitRate:P1} · gap {organFindRate - organCommitRate:P1}");
            output.WriteLine($"    idf   · find@{TopK} {nullFindRate:P1} · commit@1 {nullCommitRate:P1} · gap {nullFindRate - nullCommitRate:P1}");

            bool beatsCommit = organCommitRate > nullCommitRate;
            bool beatsFind = organFindRate >= nullFindRate;
            bool gapCloses = organFindRate - organCommitRate < nullFindRate - nullCommitRate;
            output.WriteLine($"  repository-sharpness-null · beats-idf-commit={(beatsCommit ? "PASS" : "FAIL")}"
                           + $" · holds-idf-find={(beatsFind ? "PASS" : "FAIL")} · gap-closes={(gapCloses ? "PASS" : "FAIL")}");
            return beatsCommit && beatsFind && gapCloses;
        }
        catch (Exception failure)
        {
            output.WriteLine($"  repository-sharpness-null · FAIL — {failure.Message}");
            return false;
        }
    }

    private static string[] ExpandRules(in RePairResult grammar)
    {
        string[] expansions = new string[grammar.Rules.Length];
        for (int index = 0; index < grammar.Rules.Length; index++)
            expansions[index] = Encoding.UTF8.GetString(
                Reconstruct.Expand(grammar.Rules, [new Symbol(grammar.AlphabetSize + (uint)index)]));
        return expansions;
    }

    /// Rank of the gold region, ties broken against the ranker so a flat field cannot score by luck.
    internal static int RankOf(double[] scores, int gold)
    {
        int rank = 0;
        for (int index = 0; index < scores.Length; index++)
            if (scores[index] > scores[gold] || (scores[index] == scores[gold] && index != gold)) rank++;
        return rank;
    }

    /// Queries mined from the corpus itself: a type declared in exactly ONE region, asked for by the
    /// words of its name with the name itself withheld.
    internal static (string[] Tokens, int Gold, string Symbol)[] BuildQueries(RepositorySharpness.Region[] regions)
    {
        Dictionary<string, List<int>> declared = new(StringComparer.Ordinal);
        for (int index = 0; index < regions.Length; index++)
            foreach (string symbol in DeclaredTypes(regions[index].Text))
                (declared.TryGetValue(symbol, out List<int>? homes) ? homes : declared[symbol] = []).Add(index);

        List<(string[] Tokens, int Gold, string Symbol)> queries = [];
        foreach ((string symbol, List<int> homes) in declared.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (homes.Count != 1) continue;
            string[] tokens = [.. SplitWords(symbol).Where(static word => word.Length >= 4)];
            if (tokens.Length < 2) continue;
            queries.Add((tokens, homes[0], symbol));
        }
        return [.. queries];
    }

    private static IEnumerable<string> DeclaredTypes(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimStart();
            foreach (string keyword in (string[])["public sealed class ", "public static class ", "internal static class ",
                "internal sealed class ", "public sealed record ", "public readonly record struct "])
            {
                if (!trimmed.StartsWith(keyword, StringComparison.Ordinal)) continue;
                string tail = trimmed[keyword.Length..];
                int end = 0;
                while (end < tail.Length && (char.IsLetterOrDigit(tail[end]) || tail[end] == '_')) end++;
                if (end > 3) yield return tail[..end];
            }
        }
    }

    /// CamelCase into its words — the question a person would ask, minus the spelling that would
    /// turn the question into a lookup.
    private static IEnumerable<string> SplitWords(string symbol)
    {
        StringBuilder word = new();
        foreach (char character in symbol)
        {
            if (char.IsUpper(character) && word.Length > 0) { yield return word.ToString().ToLowerInvariant(); word.Clear(); }
            word.Append(character);
        }
        if (word.Length > 0) yield return word.ToString().ToLowerInvariant();
    }

    internal static RepositorySharpness.Region[] ReadRegions(string root)
        => [.. new[] { "Kernel", "Tape", "Drive", "Agent" }
            .SelectMany(area => Directory.EnumerateFiles(Path.Combine(root, "src", "Cogito", area), "*.cs", SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal)
            .Select(path => new RepositorySharpness.Region(Path.GetRelativePath(root, path), File.ReadAllText(path)))];

    internal static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(Environment.CurrentDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "cogito.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}
