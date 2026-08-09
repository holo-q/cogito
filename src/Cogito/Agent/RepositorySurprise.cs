namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Induct;

/// G2 — prediction error as the steering wheel.
///
/// The crawler's grammar is a predictor of the world it has crawled. Point it at a region it has
/// already digested and it generates the bytes almost for free; point it at a region it has never
/// touched and it can only spell them out. That gap IS the surprise, and it is the only signal the
/// organism has that says WHERE THE UNKNOWN IS before it spends a look there.
///
/// The measurement is deliberately blind to the answer. Surprise is read from what the crawler
/// already holds about a candidate — the target text it can name (a path, a term, the locus line it
/// was minted from) — never from the result the look has not yet returned. A predictor allowed to
/// peek at the file it is about to open would score perfectly and steer nothing.
public static class RepositorySurprise
{
    /// The predicted surprise of looking at `target`, in [0,1]: the standing grammar's residual over
    /// the text the crawler can already name about that region. 1 = the grammar has no purchase at
    /// all (spell it out symbol by symbol — the unknown); near 0 = the grammar generates it whole
    /// (understood territory, a look there is predicted and buys little).
    public static double Predict(Engine.GrammarCover? cover, ReadOnlySpan<byte> target)
    {
        if (cover is null || target.Length == 0) return 1;
        return Math.Clamp((double)cover.ParsedSize(target) / target.Length, 0, 1);
    }

    public static double Predict(Engine.GrammarCover? cover, string target)
        => Predict(cover, Encoding.UTF8.GetBytes(target ?? ""));
}

/// G2's kill-line — surprise &gt; random &gt; anti-surprise at matched fuel.
///
/// A steering signal only counts if steering BY it beats not steering, and if steering AGAINST it
/// does worse than both. Anything less and the ordering was decoration.
///
/// The currency is WORLD COVERAGE, not bytes eaten. Counting admitted bytes would score an arm for
/// opening big files — grammar mass tracks file size, and a fixture that pays by mass measures the
/// filesystem rather than the steering. What a crawler is actually for is knowing its world, so an
/// arm is scored by how well the grammar it ends with predicts the WHOLE pool, looked-at and
/// untouched alike. That is where redundancy is punished on its merits: re-eating territory the
/// grammar already generates leaves the rest of the world unmodeled, however many bytes it banked.
///
/// The prediction is made from the free evidence a crawler genuinely holds before it spends a look:
/// a search returns the matching LINE, and the read that returns the surrounding window is what
/// costs fuel. So surprise is measured on the line and pays off — or does not — on the window.
/// Fuel is matched exactly: same look count, same warm grammar, same world, order the only
/// difference.
///
/// The world is the repository itself, not a synthesized one: the whole paradigm is that novelty
/// comes from the world being real, and a fixture that manufactures its own contrast would be
/// measuring its own generator. If the repository is not reachable the gate FAILS loudly rather
/// than passing on an absent world — an infrastructure loss is never banked as learner-side behavior.
internal static class RepositorySurpriseNull
{
    private const double AffirmCut = 0.25;
    private const int LookBytes = 4096;              // per-look ceiling: a read returns a window, not a whole tree

    // The regime is the one a crawler actually lives in: a world it MOSTLY knows, with the frontier
    // a thin minority, and fuel far too scarce to sweep. Steering only has anything to prove there —
    // in a pool that is mostly unknown, picking blindly hits novelty anyway and every arm ties.
    private const int WarmRegions = 30;              // the territory the crawler has already digested
    private const int Fuel = 6;                      // looks each arm may spend

    /// Evidence is the line a search already handed over for free; Bytes is the window a read
    /// would cost fuel to obtain. Predicting on Evidence and paying on Bytes is the whole point —
    /// a predictor allowed to read the window would score perfectly and steer nothing.
    private readonly record struct Region(string Path, byte[] Evidence, byte[] Bytes);

    internal static bool Verify(TextWriter output)
    {
        try
        {
            Region[] pool = ReadRegions();
            byte[] warmCorpus = BuildCorpus(pool.Take(WarmRegions));
            (_, _, RePairResult warmGrammar) = Engine.Induce(warmCorpus);
            Engine.GrammarCover warmCover = new(warmGrammar.Rules);

            // Every arm sees the identical candidate pool: the digested regions AND the untouched
            // ones together. A pool of only-novel regions would let any ordering win.
            double[] surprise = [.. pool.Select(region => RepositorySurprise.Predict(warmCover, region.Evidence))];
            int[] bySurprise = Order(pool, surprise, descending: true);
            int[] byAntiSurprise = Order(pool, surprise, descending: false);
            int[] byChance = Shuffle(pool.Length, seed: 0x5EEDC0DEUL);

            double warmCoverage = MeasureWorldResidual(warmCover, pool);
            double steered = SpendFuel(pool, warmGrammar, bySurprise);
            double chance = SpendFuel(pool, warmGrammar, byChance);
            double anti = SpendFuel(pool, warmGrammar, byAntiSurprise);

            double warmSurprise = surprise.Take(WarmRegions).Average();
            double coldSurprise = surprise.Skip(WarmRegions).Average();

            output.WriteLine($"    surprise · digested regions {warmSurprise:F3} vs untouched {coldSurprise:F3}"
                           + $" (predicted from the free search line alone, {pool.Length} regions, {Fuel} looks per arm)");
            output.WriteLine($"    world residual · warm {warmCoverage:F4} → steered {steered:F4} · chance {chance:F4} · anti {anti:F4} (lower = more of the world predicted)");

            bool separates = coldSurprise > warmSurprise;
            bool beatsChance = steered < chance;
            bool chanceBeatsAnti = chance < anti;
            output.WriteLine($"  repository-surprise-null · separation={(separates ? "PASS" : "FAIL")}"
                           + $" · steered<chance={(beatsChance ? "PASS" : "FAIL")} · chance<anti={(chanceBeatsAnti ? "PASS" : "FAIL")}");
            return separates && beatsChance && chanceBeatsAnti;
        }
        catch (Exception failure)
        {
            output.WriteLine($"  repository-surprise-null · FAIL — {failure.Message}");
            return false;
        }
    }

    /// How much of the world the grammar fails to predict: the mean per-region residual over the
    /// WHOLE pool. Every region weighs the same, so a big file cannot buy the score.
    private static double MeasureWorldResidual(Engine.GrammarCover cover, Region[] pool)
        => pool.Select(region => RepositorySurprise.Predict(cover, region.Bytes)).Average();

    /// Spend the arm's fuel in the given order, re-inducing after every admitted look — the crawler
    /// learns DURING the arm, which is why a re-look earns nothing and a well-steered arm can pull
    /// ahead. Returns the world residual the arm ends on.
    private static double SpendFuel(Region[] pool, in RePairResult warmGrammar, int[] order)
    {
        using Tape tape = new();
        Journal journal = new();
        Engine.GrammarCover cover = new(warmGrammar.Rules);
        List<byte[]> eaten = [.. pool.Take(WarmRegions).Select(region => region.Bytes)];
        for (int spent = 0; spent < Fuel && spent < order.Length; spent++)
        {
            Region region = pool[order[spent]];
            Radula.Affirmation measurement = Radula.MeasureAffirmation(cover, region.Bytes, AffirmCut);
            bool admit = !measurement.Affirmed;
            AppendLook(tape, journal, spent, region, admit);
            if (!admit) continue;
            eaten.Add(region.Bytes);
            (_, _, RePairResult grammar) = Engine.Induce(BuildCorpus(eaten));
            cover = new Engine.GrammarCover(grammar.Rules);
        }
        return MeasureWorldResidual(cover, pool);
    }

    private static void AppendLook(Tape tape, Journal journal, int step, in Region region, bool admit)
    {
        RepositoryAdmissionReceipt receipt = RepositoryAdmissionReceipt.Create(
            step, new TapeEventID(tape.NextId + 1), Digest("world"), Digest("access"), Digest("call"),
            region.Path, 1, Convert.ToHexStringLower(SHA256.HashData(region.Bytes)), step, Digest($"entry-{step}"));
        TapePacketCreator.AppendRepositoryWorldEncounter(tape, journal, step, receipt, region.Bytes, admitToGrammar: admit);
    }

    /// The regions: real source files, ordered by path so the digested/untouched split is fixed and
    /// the run is reproducible. Each look returns a window, the way a read does.
    private static Region[] ReadRegions()
    {
        string root = FindRepositoryRoot()
            ?? throw new InvalidDataException("repository root not found — the surprise gate needs the real world, and will not synthesize one");
        string[] paths = [.. new[] { "Kernel", "Tape", "Eml" }
            .SelectMany(area => Directory.EnumerateFiles(Path.Combine(root, "src", "Cogito", area), "*.cs", SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal)];
        if (paths.Length < WarmRegions + Fuel)
            throw new InvalidDataException($"the world holds {paths.Length} regions, fewer than the {WarmRegions + Fuel} the arms need");
        return [.. paths.Select(path =>
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] window = bytes.Length <= LookBytes ? bytes : bytes[..LookBytes];
            return new Region(Path.GetRelativePath(root, path), ExtractSearchLine(window), window);
        })];
    }

    /// The free evidence: the line a search would have returned. Taken as the first substantive
    /// line of the window so the choice is deterministic and carries the region's own vocabulary
    /// rather than a boilerplate header.
    private static byte[] ExtractSearchLine(byte[] window)
    {
        foreach (string line in Encoding.UTF8.GetString(window).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length >= 24 && !trimmed.StartsWith("//", StringComparison.Ordinal)) return Encoding.UTF8.GetBytes(trimmed);
        }
        return window;
    }

    private static byte[] BuildCorpus(IEnumerable<Region> regions)
        => BuildCorpus(regions.Select(region => region.Bytes));

    private static byte[] BuildCorpus(IEnumerable<byte[]> spans)
    {
        List<byte> corpus = [];
        foreach (byte[] span in spans) { corpus.AddRange(span); corpus.Add((byte)'\n'); }
        return [.. corpus];
    }

    private static int[] Order(Region[] pool, double[] surprise, bool descending)
    {
        int[] order = [.. Enumerable.Range(0, pool.Length)];
        Array.Sort(order, (left, right) =>
        {
            int compared = surprise[left].CompareTo(surprise[right]);
            if (compared != 0) return descending ? -compared : compared;
            return string.CompareOrdinal(pool[left].Path, pool[right].Path);   // ties resolve by name, never by hash order
        });
        return order;
    }

    /// The chance arm, deterministically: the Vow holds even for the control.
    private static int[] Shuffle(int count, ulong seed)
    {
        int[] order = [.. Enumerable.Range(0, count)];
        ulong state = seed;
        for (int index = count - 1; index > 0; index--)
        {
            state ^= state << 13; state ^= state >> 7; state ^= state << 17;
            int pick = (int)(state % (ulong)(index + 1));
            (order[index], order[pick]) = (order[pick], order[index]);
        }
        return order;
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(Environment.CurrentDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "cogito.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
