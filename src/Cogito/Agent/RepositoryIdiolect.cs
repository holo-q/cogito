namespace Cogito;

using System.Text;

/// The carrier G6's death pointed at: structure selected for UNIQUENESS instead of recurrence.
///
/// Induction selects for what recurs, so a grammar's rules are exactly the parts of the world that
/// are not distinctive, and a discrimination organ riding them cannot prefer the region that owns an
/// idiom over every region that merely shares it. That is not a defect in the flow — it is the
/// selection pressure. Reverse the pressure and the carrier changes: keep the phrases that appear in
/// ONE region and nowhere else, and drop precisely the ones a grammar would have kept.
///
/// A region's IDIOLECT is what it says that no one else says. In a codebase that is not an
/// abstraction — it is the literal mechanism of ownership: the file that declares a thing spells its
/// name in ways no other file does, in its declaration, its fields, its internal helpers. Every
/// other file merely mentions it.
///
/// The tokenizer is why this can work where the idf class plateaus. `RepositoryToolMediation` is ONE
/// token to a tokenizer that splits on non-letters, so a question asked in words ("repository tool
/// mediation") cannot touch it and idf can only score the prose around it. Split identifiers into
/// their words and the same identifier becomes a PHRASE — and a phrase that occurs in one region is
/// the rarest evidence the world contains.
internal static class RepositoryIdiolect
{
    /// A phrase held by more regions than this is common property, not a signature. Two is deliberate
    /// rather than one: a declaration and its single canonical caller are still ownership, and a
    /// cut at one would discard the most-used half of every real idiolect.
    private const int MaxHolders = 2;
    private const int MinPhrase = 2, MaxPhrase = 3;

    internal sealed class Signature
    {
        /// phrase → the regions holding it, and how often each says it. Rare by construction: a
        /// phrase past MaxHolders never enters, so the index cannot grow into a term dictionary.
        private readonly Dictionary<string, List<(int Region, int Count)>> _phrases = new(StringComparer.Ordinal);
        private readonly int _regions;

        internal Signature(RepositorySharpness.Region[] regions)
        {
            _regions = regions.Length;
            Dictionary<string, Dictionary<int, int>> counts = new(StringComparer.Ordinal);
            for (int index = 0; index < regions.Length; index++)
                foreach (string phrase in Phrases(SplitIdentifierWords(regions[index].Text)))
                {
                    Dictionary<int, int> holders = counts.TryGetValue(phrase, out Dictionary<int, int>? existing)
                        ? existing : counts[phrase] = [];
                    holders[index] = holders.GetValueOrDefault(index) + 1;
                }
            foreach ((string phrase, Dictionary<int, int> holders) in counts)
                if (holders.Count <= MaxHolders)
                    _phrases[phrase] = [.. holders.Select(static holder => (holder.Key, holder.Value))];
        }

        internal int Count => _phrases.Count;

        /// Where a question's phrases live. A query contributes its own word n-grams; each one that
        /// survived as a signature names the region that owns it, weighted by how rare its holding is
        /// and how insistently that region repeats it.
        internal double[] Locate(IReadOnlyList<string> queryTokens)
        {
            double[] score = new double[_regions];
            foreach (string phrase in Phrases(queryTokens))
            {
                if (!_phrases.TryGetValue(phrase, out List<(int Region, int Count)>? holders)) continue;
                double rarity = Math.Log(1 + (double)_regions / holders.Count);
                foreach ((int region, int count) in holders) score[region] += rarity * Math.Log(1 + count);
            }
            return score;
        }
    }

    /// Contiguous word n-grams. Order is kept: "tool mediation" and "mediation tool" are different
    /// claims about the world, and collapsing them would hand back the bag-of-words the idf class
    /// already owns.
    private static IEnumerable<string> Phrases(IReadOnlyList<string> words)
    {
        for (int width = MinPhrase; width <= MaxPhrase; width++)
            for (int start = 0; start + width <= words.Count; start++)
                yield return string.Join(' ', words.Skip(start).Take(width));
    }

    /// Every identifier decomposed into the words it was built from, plus the words of ordinary
    /// prose. CamelCase, snake_case and punctuation all fall to the same rule, so a name and the
    /// sentence describing it become the same kind of evidence.
    internal static List<string> SplitIdentifierWords(string text)
    {
        List<string> words = [];
        StringBuilder word = new();
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (!char.IsLetter(character))
            {
                if (word.Length > 0) { words.Add(word.ToString().ToLowerInvariant()); word.Clear(); }
                continue;
            }
            // A capital opens a new word only after a lowercase run, so an ACRONYM stays whole —
            // splitting `RGBA` into four letters would bury the identifiers this org marks with them.
            if (char.IsUpper(character) && word.Length > 0 && char.IsLower(word[^1]))
            {
                words.Add(word.ToString().ToLowerInvariant());
                word.Clear();
            }
            word.Append(character);
        }
        if (word.Length > 0) words.Add(word.ToString().ToLowerInvariant());
        return words;
    }
}

/// The uniqueness carrier against the same idf class, on the same held-out queries G6's flow lost to.
///
/// Nothing about the trial changes — same regions, same mined questions, same rank ruler, same
/// withheld spelling — so the two organs are directly comparable and the only difference is what
/// carries the evidence: structure the world REPEATS, or structure exactly one region owns.
internal static class RepositoryIdiolectNull
{
    internal static bool Verify(TextWriter output)
    {
        try
        {
            string root = RepositorySharpnessNull.FindRepositoryRoot()
                ?? throw new InvalidDataException("repository root not found — the idiolect gate localizes in the real repository, and will not synthesize one");
            RepositorySharpness.Region[] regions = RepositorySharpnessNull.ReadRegions(root);
            (string[] Tokens, int Gold, string Symbol)[] mined = RepositorySharpnessNull.BuildQueries(regions);
            (string[] Tokens, int Gold, string Symbol)[] queries =
                [.. mined.Where((_, index) => index % Math.Max(1, mined.Length / RepositorySharpnessNull.QueryBudget) == 0)
                    .Take(RepositorySharpnessNull.QueryBudget)];

            RepositoryIdiolect.Signature signature = new(regions);
            Bm25Index index = new([.. regions.Select(static region => region.Text)]);

            int organFind = 0, organCommit = 0, nullFind = 0, nullCommit = 0, goldReached = 0;
            int fired = 0, firedCorrect = 0, pairCommit = 0;
            foreach ((string[] tokens, int gold, _) in queries)
            {
                double[] organ = signature.Locate(tokens);
                double[] idf = index.Score([.. tokens]);
                if (organ[gold] > 0) goldReached++;

                // WHETHER THE ORGAN SPOKE AT ALL is a separate question from whether it was right,
                // and conflating them is what makes a sharp-but-narrow organ read as a bad one. A
                // discrimination engine is allowed to abstain; what it is not allowed to do is be
                // wrong when it commits.
                bool speaks = organ.Any(static value => value > 0);
                if (speaks)
                {
                    fired++;
                    if (RepositorySharpnessNull.RankOf(organ, gold) == 0) firedCorrect++;
                }
                // The pair: the sharp organ decides where it has evidence, the broad one everywhere
                // else. Neither is asked to do the other's work.
                if (RepositorySharpnessNull.RankOf(speaks ? organ : idf, gold) == 0) pairCommit++;
                int organRank = RepositorySharpnessNull.RankOf(organ, gold), nullRank = RepositorySharpnessNull.RankOf(idf, gold);
                if (organRank < RepositorySharpnessNull.TopK) organFind++;
                if (organRank == 0) organCommit++;
                if (nullRank < RepositorySharpnessNull.TopK) nullFind++;
                if (nullRank == 0) nullCommit++;
            }

            double organFindRate = (double)organFind / queries.Length, organCommitRate = (double)organCommit / queries.Length;
            double nullFindRate = (double)nullFind / queries.Length, nullCommitRate = (double)nullCommit / queries.Length;

            output.WriteLine($"    world · {regions.Length} regions · {queries.Length} held-out queries · {signature.Count} signature phrases (≤2 holders)");
            output.WriteLine($"    flow  · gold reached {goldReached}/{queries.Length} queries");
            output.WriteLine($"    organ · find@{RepositorySharpnessNull.TopK} {organFindRate:P1} · commit@1 {organCommitRate:P1} · gap {organFindRate - organCommitRate:P1}");
            output.WriteLine($"    idf   · find@{RepositorySharpnessNull.TopK} {nullFindRate:P1} · commit@1 {nullCommitRate:P1} · gap {nullFindRate - nullCommitRate:P1}");
            output.WriteLine($"    sharp · spoke on {fired}/{queries.Length} · right when it spoke {(fired == 0 ? 0 : (double)firedCorrect / fired):P1}"
                           + $" · idf right when it commits {(nullCommitRate / Math.Max(nullFindRate, 1e-9)):P1}");
            output.WriteLine($"    pair  · commit@1 {(double)pairCommit / queries.Length:P1} (sharp decides where it has evidence, idf elsewhere)");

            // The bar is not "replace idf" — that was the costume error, asking one organ to be both
            // faculties. The bar is that a sharp organ is SHARPER where it speaks, that its gap
            // closes, and that pairing it with the broad one beats the broad one alone.
            double sharpPrecision = fired == 0 ? 0 : (double)firedCorrect / fired;
            double idfPrecision = nullFindRate <= 0 ? 0 : nullCommitRate / nullFindRate;
            bool sharperWhereItSpeaks = sharpPrecision > idfPrecision;
            bool gapCloses = organFindRate - organCommitRate < nullFindRate - nullCommitRate;
            bool pairWins = (double)pairCommit / queries.Length > nullCommitRate;
            output.WriteLine($"  repository-idiolect-null · sharper-where-it-speaks={(sharperWhereItSpeaks ? "PASS" : "FAIL")}"
                           + $" · gap-closes={(gapCloses ? "PASS" : "FAIL")} · pair-beats-idf-alone={(pairWins ? "PASS" : "FAIL")}");
            return sharperWhereItSpeaks && gapCloses && pairWins;
        }
        catch (Exception failure)
        {
            output.WriteLine($"  repository-idiolect-null · FAIL — {failure.Message}");
            return false;
        }
    }
}
