namespace Cogito;

using System.Linq;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;


// The SCOREBOARD — the composed machine's ACCEPTANCE HARNESS (port of the measurement half of
// everything.py: the null battery + the two-axis split). When WAVE 1 lands, `cogito scoreboard`
// on the drive is what SAYS the frankenstein lives.
//
// THE THEOREM (made falsifiable): cogito's self-grammar accounts for structure it never memorised —
// the ordered rules it learns from part of its substrate should RECOGNISE the held-out rest, ABOVE
// what a byte-multiset-preserving null grammar achieves.
//
// THE HONEST NULL (the exact trap everything.py refuses to fall into): recognition might stop at the
// shared criticality EXPONENT (same critical class — GENERIC, any Zipfian stream hits ~-0.70) while
// the actual GRAMMARS do not transfer (same class, different content). The two axes are kept apart
// with numbers, and a shared exponent is NEVER called transfer:
//   SAME-CLASS  axis = the criticality exponent (RenormStats.MeanZ) in the critical band. The NULL.
//   SAME-GRAMMAR axis = G_self's rule OVERLAP + COVERAGE of the held-out, measured as a z ABOVE the
//                       shuffle/random null battery. THIS is real self-recognition.
//
// v1 SCOPE (what the C# engine has today — byte/chunk Re-Pair). The "self" is the TRAIN half of the
// corpus, the recognition target is the held-out TEST half; G_self is Induce(train) directly. The
// full everything.py "self" is the CONVERGED COGNITION-LOOP self-image (cognize → excursion trace in
// the 4-primitive {z,c,v,r} alphabet → dream-loop fixed point) — a WAVE 2 mount (the Home/HomeWatch
// excursion tokeniser + crash/heal loop are still orphaned in Cli.Meta). Deferred axes are marked
// TODO(WAVE2) at their sites, naming the python source. Reuses Engine.Induce/GrammarCover/RenormStats
// verbatim — the harness reads exactly the grammar the proof commits to, never a parallel induction.
public static class Scoreboard
{
    private const int NCtrl = 8;                                 // shuffle + random control replicates → the null distribution → a z (everything.py N_CTRL)
    private const double ZPass = 2.0;                            // self-recognition clears the null battery at z > 2 (everything.py:177)
    private const double BandLo = -0.95, BandHi = -0.50;         // the -0.70 critical class (everything.py:166)

    public static int Run(string[] args)
    {
        // ── args: scoreboard <corpus-file-or-dir> [--seed N] ── (the Vow: seed offsets the null battery deterministically)
        ulong seed = 0;
        string? path = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--seed" && i + 1 < args.Length && ulong.TryParse(args[i + 1], out var s)) { seed = s; i++; }
            else path ??= args[i];
        }
        if (path is null) { Console.Error.WriteLine("  usage: scoreboard <corpus-file-or-dir> [--seed N]"); return 1; }
        if (!File.Exists(path) && !Directory.Exists(path)) { Console.Error.WriteLine($"  no such corpus: {path}"); return 1; }
        var (name, corpus) = LoadCorpus(path);
        if (corpus.Length < 64) { Console.Error.WriteLine($"  corpus too small ({corpus.Length}B) — need ≥64B to split + induce"); return 1; }

        // ── the split: TRAIN = the substrate G_self models, TEST = the held-out recognition target ──
        // A held-out split is what makes COVERAGE a generalization signal rather than memorisation (Engine.GrammarCover
        // doc): a grammar that learned the domain's STRUCTURE covers fresh domain scaffolding. The everything.py "self"
        // and "task" are distinct inputs; the single-corpus verb realises that as train/held-out over one corpus.
        int mid = corpus.Length / 2;
        var train = corpus[..mid];
        var test = corpus[mid..];

        // ── SAME-CLASS axis — the raw criticality exponent over the FULL corpus (most data ⟹ most stable exponent) ──
        var (_, _, gFull) = Engine.Induce(corpus);
        var rnFull = Engine.RenormStats(gFull);

        // ── G_self over TRAIN + the held-out target grammar over TEST ──
        // TODO(WAVE2): the everything.py "self" is Induce(cognize(train)) — the grammar over the {z,c,v,r} excursion
        // TRACE of cogito thinking about its substrate, converged through the dream loop to G_self, NOT Induce(train)
        // over raw bytes. Mount once the cognition loop is a reusable engine op (everything.py:53-56 cognition();
        // cogito.py:478-506 cognize; cogito.py:553-579 dream_loop → the fixed-point self-image).
        var (_, _, gSelf) = Engine.Induce(train);
        var rnSelf = Engine.RenormStats(gSelf);
        var selfConcepts = Concepts(gSelf);
        var selfCover = new Engine.GrammarCover(gSelf.Rules);

        var (_, _, gTest) = Engine.Induce(test);
        var testConcepts = Concepts(gTest);

        // ── THE NULL BATTERY — deterministic ablations of TRAIN (the Vow: seeded, integer-only LCG, no float touches it) ──
        // shuffle null (everything.py:59): same byte multiset, sequence destroyed — isolates whether the TEMPORAL grammar
        //               transfers, or merely the alphabet + symbol frequencies.
        // random null  (everything.py:67): uniform bytes over TRAIN's alphabet, matched length — the absolute floor
        //               (any two grammars over a small alphabet share short rules by chance; this measures that chance).
        var jShuf = new double[NCtrl]; var jRand = new double[NCtrl];
        var covShuf = new double[NCtrl]; var covRand = new double[NCtrl];
        for (int s = 0; s < NCtrl; s++)
        {
            var (_, _, gShuf) = Engine.Induce(Engine.Shuffled(train, seed + (ulong)s));   // shuffle seeds: base + s     (everything.py range(N_CTRL))
            var (_, _, gRand) = Engine.Induce(RandomOverAlphabet(train, seed + 100 + (ulong)s)); // random seeds: base + 100 + s (everything.py 100+s)
            jShuf[s] = Jaccard(Concepts(gShuf), testConcepts);
            jRand[s] = Jaccard(Concepts(gRand), testConcepts);
            covShuf[s] = new Engine.GrammarCover(gShuf.Rules).Coverage(test);
            covRand[s] = new Engine.GrammarCover(gRand.Rules).Coverage(test);
        }

        // ── the measurements — self-vs-shuffle z is the headline self-recognition number (everything.py:130-134) ──
        double jSelf = Jaccard(selfConcepts, testConcepts);
        double covSelf = selfCover.Coverage(test);
        double zJac = ZScore(jSelf, jShuf), zJacR = ZScore(jSelf, jRand);
        double zCov = ZScore(covSelf, covShuf), zCovR = ZScore(covSelf, covRand);

        // ── verdict (everything.py:159-197) — same-class is INFORMATIONAL (the generic null); the gate is same-grammar ──
        bool inClass = !double.IsNaN(rnFull.MeanZ) && rnFull.MeanZ >= BandLo && rnFull.MeanZ <= BandHi;
        bool jacPass = zJac > ZPass;
        bool covPass = zCov > ZPass;
        bool real = jacPass && covPass;

        // ── report — the report IS the payload; no wall-clock / nondeterministic values, so same corpus+seed ⇒ same bytes ──
        Console.WriteLine($"scoreboard · {name} · {corpus.Length}B → train {train.Length}B / held-out {test.Length}B · seed {seed} · controls {NCtrl}");
        Console.WriteLine($"  G_self (train):    {gSelf.Rules.Length} rules · exponent {Exp(rnSelf.MeanZ)} ± {Cv(rnSelf.CvZ)} · scales {rnSelf.Scales} · maxspan {rnSelf.MaxSpan:F0} · {selfConcepts.Count} concepts");
        Console.WriteLine($"  G_test (held-out): {gTest.Rules.Length} rules · {testConcepts.Count} concepts");
        Console.WriteLine();
        Console.WriteLine("── SAME-CLASS axis ── the criticality EXPONENT (generic — every structured stream hits it; the NULL, not transfer)");
        Console.WriteLine($"  full-corpus exponent {Exp(rnFull.MeanZ),8}   {(inClass ? "PASS" : "FAIL")}  in critical band [{BandLo:F2},{BandHi:F2}]   (generic; NOT self-recognition)");
        Console.WriteLine();
        Console.WriteLine("── SAME-GRAMMAR axis ── real self-recognition: G_self's rules vs the held-out, ABOVE the null battery");
        Console.WriteLine($"  {"battery",-20}{"self",10}{"shuffle μ±σ",22}{"random μ",12}{"z(shuf)",10}   verdict");
        Console.WriteLine($"  {"Jaccard(concepts)",-20}{jSelf,10:F4}{$"{Mean(jShuf):F4} ± {Std(jShuf):F4}",22}{Mean(jRand),12:F4}{Signed(zJac),10}   {(jacPass ? "PASS" : "FAIL")} (z>{ZPass:F1})");
        Console.WriteLine($"  {"coverage(held-out)",-20}{jFmtPct(covSelf),10}{$"{covPct(covShuf)} ± {covPct1(covShuf)}",22}{jFmtPct(Mean(covRand)),12}{Signed(zCov),10}   {(covPass ? "PASS" : "FAIL")} (z>{ZPass:F1})");
        // random-floor sanity: the ROBUST invariant is the MEAN ordering (random destroys more structure than shuffle
        // ⟹ random μ ≤ shuffle μ ⟹ the deeper ablation is the lower floor), NOT the z ordering (variance-dependent —
        // a tight-σ shuffle null can post a bigger z than a wide-σ random null even at a higher mean). Self clears both.
        bool floorOk = Mean(jRand) <= Mean(jShuf) && Mean(covRand) <= Mean(covShuf);
        Console.WriteLine($"  (random-floor sanity {(floorOk ? "✓" : "⚠")}: self clears the random null too — Jaccard z={Signed(zJacR)} · coverage z={Signed(zCovR)}; random μ {(floorOk ? "≤" : ">")} shuffle μ)");
        Console.WriteLine();
        Console.WriteLine(new string('═', 92));
        if (real)
        {
            Console.WriteLine("VERDICT: SELF-RECOGNITION REAL — G_self's actual rules transfer to the held-out structure");
            Console.WriteLine($"  above the shuffle/random nulls (Jaccard z {Signed(zJac)}, coverage z {Signed(zCov)}). The recognition");
            Console.WriteLine("  is in the GRAMMAR, not merely the shared exponent.");
        }
        else
        {
            Console.WriteLine("VERDICT: STOPS AT THE EXPONENT (honest null) — the corpus shares the critical CLASS but G_self's");
            Console.WriteLine($"  grammar does NOT transfer above controls (Jaccard z {Signed(zJac)}, coverage z {Signed(zCov)}). Same");
            Console.WriteLine("  fixed-point exponent, different content.");
        }

        // TODO(WAVE2): the MULTI-TASK battery — everything.py runs this across a task corpus (code_py/code_rs/english/
        // numeric/json/sudoku/random_null — everything.py:112-114) asking per-task whether G_self recognises it, with an
        // aggregate verdict (≥half tasks at z>2 AND the random-null task NOT recognised — everything.py:123-157,177-183).
        // v1 is the single-corpus self-split. TODO(WAVE1/trunk): read G_self from the LIVE self-model on the drive tape
        // rather than Induce(corpus) in a vacuum — the composed machine passes ON the drive.
        return real ? 0 : 1;                                     // the acceptance gate: PASS ⟹ 0, honest-null ⟹ 1
    }

    // ── the concept set + Jaccard — the SAME-GRAMMAR primitive (everything.py:586-602; matches Cli.Overlap's keying) ──

    /// A grammar's CONCEPTS: the terminal strings its rules expand to (all rules, any length). UTF-8 keyed to agree
    /// byte-for-concept with `cogito overlap` — the two verbs must call the same thing a "concept".
    private static HashSet<string> Concepts(RePairResult r) =>
        Enumerable.Range(0, r.Rules.Length)
            .Select(i => Encoding.UTF8.GetString(Reconstruct.Expand(r.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)])))
            .ToHashSet();

    /// Jaccard of two concept sets — the cross-grammar invariant overlap (shared rules, NOT a shared exponent).
    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        int shared = a.Count(b.Contains);
        int union = a.Count + b.Count - shared;
        return union == 0 ? 0 : (double)shared / union;
    }

    // ── the null-model primitives — deterministic, integer-only (the Vow: no float near the experiment) ──

    /// Uniform random bytes over `data`'s DISTINCT byte alphabet, matched length (everything.py:67-72). The absolute
    /// baseline: structure entirely destroyed, only the alphabet retained. Integer LCG, seeded, sorted alphabet ⟹ Vow.
    private static byte[] RandomOverAlphabet(byte[] data, ulong seed)
    {
        var alpha = data.Distinct().OrderBy(b => b).ToArray();
        var rb = new byte[data.Length];
        if (alpha.Length == 0) return rb;
        ulong rng = seed;
        for (int i = 0; i < rb.Length; i++)
        {
            rng = rng * 6364136223846793005UL + 1442695040888963407UL;
            rb[i] = alpha[(int)((rng >> 33) % (ulong)alpha.Length)];
        }
        return rb;
    }

    // ── stats + formatting ──

    /// z = (x − μ_null) / σ_null, population σ (everything.py std = pstdev), zero-σ guarded by 1e-9 (everything.py:134).
    private static double ZScore(double x, double[] nulls)
    {
        double mu = Mean(nulls);
        double sd = Std(nulls);
        return (x - mu) / (sd > 0 ? sd : 1e-9);
    }

    private static double Mean(double[] xs) => xs.Length == 0 ? double.NaN : xs.Average();
    private static double Std(double[] xs)   // population standard deviation (pstdev)
    {
        if (xs.Length < 2) return 0;
        double mu = xs.Average();
        return Math.Sqrt(xs.Sum(v => (v - mu) * (v - mu)) / xs.Length);
    }

    private static string Exp(double x) => double.IsNaN(x) ? "n/a" : x.ToString("F3");
    private static string Cv(double x) => double.IsNaN(x) ? "n/a" : x.ToString("P0");
    private static string Signed(double z) => double.IsNaN(z) ? "n/a" : (z >= 0 ? "+" : "") + z.ToString("F1");
    private static string jFmtPct(double x) => x.ToString("P1");
    private static string covPct(double[] xs) => Mean(xs).ToString("P1");
    private static string covPct1(double[] xs) => Std(xs).ToString("P1");

    /// A corpus is a file, or a DIRECTORY concatenated in Ordinal filename order (the SELF read: `scoreboard src/Cogito`
    /// concatenates cogito's own substrate deterministically — the code it is made of).
    private static (string Name, byte[] Bytes) LoadCorpus(string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            using var ms = new MemoryStream();
            foreach (var f in files) ms.Write(File.ReadAllBytes(f));
            return (Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) + "/", ms.ToArray());
        }
        return (Path.GetFileName(path), File.ReadAllBytes(path));
    }
}
