namespace Cogito;

using Cogito.Induct;

/// G4 — the compounding gate, asked the only way it can be answered: in FUEL.
///
/// "Does closure make the next discovery cheaper" is not a question about how much an organism
/// learns — a mind that already understands its world learns little more from it, and a diminishing
/// marginal curve would be read as failure when it is actually mastery. The question is about PRICE:
/// how much fuel must be spent before the NEXT thing is understood. If closure compounds, that price
/// falls cycle after cycle, because what was learned in cycle k is already paying for cycle k+1.
///
/// So each cycle names a target it must reach — bring the residual on the epoch AHEAD below a fixed
/// bar — and the measurement is the fuel it took to get there. The world is G5's: the repository's
/// own commits, delta-granular, in the order they happened.
///
/// The control is the same organism with its memory taken away: identical world, identical order,
/// identical target, grammar reset before every cycle. It has to buy the same understanding from
/// scratch each time, so the difference between the arms is exactly what carrying the past was
/// worth. A carry arm that does not beat it did not compound — it was merely fed.
internal static class RepositoryCompoundingNull
{
    private const double AffirmCut = 0.25;
    private const double Target = 0.24;          // the residual a cycle must reach on the epoch ahead
    private const int Chunk = 8192;              // fuel is spent in bites, so price has resolution
    private const int Epochs = 7;

    private readonly record struct Cycle(int Index, long Fuel, double Reached, bool Censored);

    internal static bool Verify(TextWriter output)
    {
        try
        {
            string root = FindRepositoryRoot()
                ?? throw new InvalidDataException("repository root not found — the compounding gate runs on the real history, and will not synthesize one");
            string[] paths =
            [
                "src/Cogito/Kernel/Engine.cs", "src/Cogito/Kernel/Induct.cs", "src/Cogito/Kernel/Loom.cs",
                "src/Cogito/Tape/Tape.cs", "src/Cogito/Tape/Journal.cs", "src/Cogito/Drive/Radula.cs",
            ];
            RepositoryHistoryWorld.Epoch[] history = RepositoryHistoryWorld.ReadEpochs(root, paths, Epochs);
            RepositoryHistoryWorld.Epoch[] deltas = RepositoryHistoryWorld.ReadDeltas(root, history, paths);

            Cycle[] carry = Run(deltas, carriesMemory: true);
            Cycle[] reset = Run(deltas, carriesMemory: false);

            output.WriteLine($"    world · {deltas.Length} epochs · change {string.Join('/', deltas.Skip(1).Select(static delta => delta.Bytes.Length))}B · target residual {Target:F2}");
            output.WriteLine($"    carry · fuel {Render(carry)}");
            output.WriteLine($"    reset · fuel {Render(reset)}");

            // A cycle that never reached the bar has no price — it has a FLOOR: it spent everything
            // it had and arrived nowhere. Reading a ratio off two floors would be fiction, so
            // reachability is settled first and the fuel numbers only speak where the bar was met.
            Cycle[] carryReached = [.. carry.Where(static cycle => !cycle.Censored)];
            bool carryArrives = carryReached.Length > 0 && carryReached[^1].Index >= carry.Length / 2;
            bool resetNeverArrives = reset.All(static cycle => cycle.Censored);

            long carryPrice = carryReached.Length > 0 ? carryReached[^1].Fuel : long.MaxValue;
            long resetFloor = reset[^1].Fuel;
            bool bends = carryReached.Length > 0 && carryPrice < carry[0].Fuel;

            // The ratio is a LOWER bound whenever the reset arm is censored: it spent at least that
            // much and still did not arrive, so the true price of memoryless understanding is higher
            // than the number — possibly unbounded. Stated as a bound, never as a measurement.
            bool doubles = carryPrice != long.MaxValue && carryPrice * 2 <= resetFloor;
            output.WriteLine($"    price · carry {carry[0].Fuel}B → {(carryPrice == long.MaxValue ? "never" : carryPrice + "B")}"
                           + $" · reset spent {resetFloor}B and never arrived"
                           + $" · ratio ≥ {(carryPrice is 0 or long.MaxValue ? "n/a" : ((double)resetFloor / carryPrice).ToString("F2") + "×")} (lower bound — the reset arm is censored)");

            output.WriteLine($"  repository-compounding-null · carry-arrives-late={(carryArrives ? "PASS" : "FAIL")}"
                           + $" · reset-never-arrives={(resetNeverArrives ? "PASS" : "FAIL")}"
                           + $" · price-falls={(bends ? "PASS" : "FAIL")} · 2x-bar={(doubles ? "PASS" : "FAIL")}");
            return carryArrives && resetNeverArrives && bends && doubles;
        }
        catch (Exception failure)
        {
            output.WriteLine($"  repository-compounding-null · FAIL — {failure.Message}");
            return false;
        }
    }

    /// One arm's whole life. Each cycle spends fuel on epoch k in bites until the epoch AHEAD reads
    /// under the target, and reports what that cost. The carry arm keeps everything it ate; the
    /// reset arm starts each cycle empty and must buy the same understanding again.
    private static Cycle[] Run(RepositoryHistoryWorld.Epoch[] deltas, bool carriesMemory)
    {
        List<Cycle> cycles = [];
        List<byte[]> eaten = [];
        Engine.GrammarCover? cover = null;
        for (int index = 0; index < deltas.Length - 1; index++)
        {
            if (!carriesMemory) { eaten = []; cover = null; }
            byte[] ahead = deltas[index + 1].Bytes;
            byte[] source = deltas[index].Bytes;
            long fuel = 0;
            int offset = 0;
            while (RepositorySurprise.Predict(cover, ahead) > Target && offset < source.Length)
            {
                int length = Math.Min(Chunk, source.Length - offset);
                byte[] bite = source[offset..(offset + length)];
                offset += length;

                // The intake gate is live inside the cycle: a bite the grammar already generates is
                // refused and costs NO fuel. That is what keeps the carry arm honest — it cannot buy
                // its advantage by re-swallowing what it knows, it can only skip past it.
                if (Radula.MeasureAffirmation(cover, bite, AffirmCut).Affirmed) continue;
                eaten.Add(bite);
                fuel += length;
                List<byte> corpus = [];
                foreach (byte[] span in eaten) { corpus.AddRange(span); corpus.Add((byte)'\n'); }
                (_, _, RePairResult grammar) = Engine.Induce([.. corpus]);
                cover = new Engine.GrammarCover(grammar.Rules);
            }
            double reached = RepositorySurprise.Predict(cover, ahead);
            cycles.Add(new Cycle(index, fuel, reached, reached > Target));
        }
        return [.. cycles];
    }

    private static string Render(Cycle[] cycles)
        => string.Join(" ", cycles.Select(static cycle => $"{cycle.Fuel}B→{cycle.Reached:F3}{(cycle.Censored ? "!" : "")}"));

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
}
