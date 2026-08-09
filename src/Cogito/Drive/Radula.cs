namespace Cogito;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Cogito.Induct;

// ── THE INTAKE CURRICULUM ──  cogito ≠ DL. DL batch-trains on a random-global sample and settles into a local
// minimum it tolerates; cogito finds NOTHING or an authentic GROK, and the grok is a MULTIRESOLUTION capability
// TREE — it cannot reach a domain's deep structure unless it groks the prerequisite scale FIRST. The lever is
// the INTAKE POLICY: WHICH span the learner ingests NEXT, and in what order. This verb proves the policy matters
// by holding the engine (Re-Pair + MDL) fixed and varying ONLY the intake:
//
//   • sequential      — corpus order (walk-forward; the fixed feed).
//   • random-global   — a uniformly random span each round (the DL way; the market's per-symbol shuffle, the
//                       mixture's random-switch — both FAILED, and this is why).
//   • frontier (RLEI) — the grammar DRIVES ITS OWN INTAKE off its OWN residual: each round it re-induces, then
//                       ingests the un-seen span it can ALREADY compress best (max coverage = the edge-of-known,
//                       min residual-to-attach). It starts at a bacterial SEED and grows LOCALLY outward —
//                       concentrating on one structural family until that family is grokked, then the highest
//                       remaining coverage is the ADJACENT family (a bridge of shared sub-structure), so the
//                       frontier crawls the similarity graph. A family sharing nothing stays at ~0 coverage:
//                       unreachable until a bridge raises it — the capability DEPENDENCY made mechanical.
//
// WHY FRONTIER MUST WIN (the mechanism, not a hope): Re-Pair mints a rule only for a digram recurring ≥3× (the
// rule-of-three, Mdl.PairDelta). A domain's DEEP patterns (its phrases, its lines) are family-specific; whether
// they clear that threshold depends on how CONCENTRATED the ingested tape is within a family. Random-global over
// K families spreads ~B/K same-family spans → deep patterns fall sub-threshold → the grammar caps SHALLOW (the
// local minimum). Frontier concentrates → the SAME budget puts every same-family span on the tape → deep patterns
// clear the gate → deep scales, the −0.70 fixed point. MDL rewards concentration; local-coherent intake PRODUCES
// concentration; random-global DESTROYS it. So RLEI-as-root is not decoration — it is the precondition under which
// the deep grok is reachable at all under a finite intake budget.

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE INTAKE CORPUS — the family-labeled pool the policy experiment (and the Farm) draws from
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// A family-labeled pool of trainable spans + a held-out generalization probe. Both the synthetic TowerCorpus
/// and the real-code FileCorpus produce this shape, so the 3-policy experiment (DrivePolicy) is source-agnostic:
/// prove the frontier win on the tower's KNOWN hierarchy, then confirm it TRANSFERS to natural code by pointing
/// the SAME engine + SAME experiment at real files. A FAMILY is a near-disjoint structural unit — the tower's
/// morpheme-window vocabulary, or a source file's own idiom/identifier vocabulary — the dependency chain frontier
/// must crawl (concentrate a family, then bridge to the adjacent one) and random-global shatters.
public interface IIntakeCorpus
{
    IReadOnlyList<(int Fam, byte[] Bytes)> Lines { get; }      // the intake pool (trainable spans)
    IReadOnlyList<(int Fam, byte[] Bytes)> Heldout { get; }    // the fixed generalization probe (never ingested)
    int Families { get; }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE TOWER CORPUS — synthetic multi-family hierarchy with a known dependency chain
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// A deterministic synthetic corpus with REFERENCE multi-scale structure, built so intake policy is the ONLY
/// free variable. Alphabet is shared; the hierarchy is bytes → morphemes (2 chars) → words (3 morphemes) →
/// phrases (3 words) → TEMPLATES (3–4 phrases) → a line is one template instance (so the deep scale RECURS). K
/// families each draw their vocabulary from a near-disjoint morpheme window (step = mWin−overlap): neighbors share
/// `overlap` morphemes (a bridge), distant families share none (the dependency chain/tree). At overlap=0 the
/// families are islands — the clean depth test. A held-out slice per family (off the intake pool) is the
/// generalization probe. No LLM — hermetic + replayable (same seed ⇒ same corpus ⇒ same curves, the Vow).
public sealed class TowerCorpus : IIntakeCorpus
{
    private readonly List<(int Fam, byte[] Bytes)> _lines = new();    // the intake pool (all trainable lines)
    private readonly List<(int Fam, byte[] Bytes)> _heldout = new();  // fixed generalization probe (never ingested)
    public IReadOnlyList<(int Fam, byte[] Bytes)> Lines => _lines;
    public IReadOnlyList<(int Fam, byte[] Bytes)> Heldout => _heldout;
    public int Families { get; }

    public TowerCorpus(int families, int nMorph, int mWin, int overlap, int wPer, int pPer, int tPer, int linesPer, int holdEvery, ulong seed, bool negControl, string poolOrder, bool flat)
    {
        Families = families;
        ulong rng = seed;
        int Next(int n) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)n); }

        // morphemes: distinct 2-char tokens (aa, ab, …) — the level-0 atoms, shared pool.
        var morph = new string[nMorph];
        for (int i = 0; i < nMorph; i++) morph[i] = $"{(char)('a' + i / 26 % 26)}{(char)('a' + i % 26)}";

        // per-family word/phrase vocabularies over a NEAR-DISJOINT morpheme window. Family f owns [start, start+mWin);
        // consecutive windows step by (mWin−overlap), so neighbors share exactly `overlap` morphemes (a weak bridge)
        // and distant families share NONE — the similarity CHAIN, so frontier can crawl adjacent but not leap distant
        // (the capability dependency). Disjoint-enough that a family's WORDS+PHRASES are its own: no cross-family
        // digram sharing above the morpheme, so deep rules can ONLY form from concentrated same-family intake.
        // negControl: random window offset per family → destroys the chain (the shuffle control proving the win is
        // structural, not a policy artifact — like the shuffle-verified foveation, cogito-mixture-curriculum).
        int step = Math.Max(1, mWin - overlap);
        // GRADED difficulty = the tree.s tiers (each scale rarer than the one below, so each needs MORE
        // concentration to clear the ≥3× MDL gate):
        //   L1 WORDS (few/family)     — recur even in a thin random slice → learnable broadly (random reaches here).
        //   L2 PHRASES (more/family)  — recur only under moderate concentration.
        //   L3 TEMPLATES (a line IS a template) — the DEEP scale: a specific phrase-sequence that recurs across
        //      lines ONLY when intake is concentrated in the family. A held-out line's bytes are covered by mere
        //      WORDS (coverage blind to depth), but its SYMBOL count collapses to ~1 only if the TEMPLATE was
        //      learned → parse-size is the depth read, and templates are what random-global starves.
        for (int f = 0; f < families; f++)
        {
            int start = negControl ? Next(Math.Max(1, nMorph - mWin)) : Math.Min(nMorph - mWin, f * step);
            string M() => morph[start + Next(mWin)];                               // a morpheme from this family's window

            var words = new string[wPer];
            for (int w = 0; w < wPer; w++) words[w] = M() + M() + M();              // L1 word = 3 morphemes (6 chars)
            var phrases = new string[pPer];
            for (int p = 0; p < pPer; p++) phrases[p] = words[Next(wPer)] + words[Next(wPer)] + words[Next(wPer)];   // L2
            var templates = new string[tPer];                                      // L3 template = 3–4 phrases (a line skeleton)
            for (int t = 0; t < tPer; t++)
            {
                int np = 3 + Next(2);
                var tb = new StringBuilder();
                for (int k = 0; k < np; k++) { if (k > 0) tb.Append(' '); tb.Append(phrases[Next(pPer)]); }
                templates[t] = tb.ToString();
            }

            for (int l = 0; l < linesPer; l++)
            {
                // NEGATIVE CONTROL (--flat): a line is 3–4 RANDOM phrases — NO template scale exists. There is no
                // deep structure to concentrate toward, so frontier's advantage must VANISH (proves the win is the
                // real hierarchy, not the policy). Otherwise a line IS a template instance (the deep scale recurs).
                string lineStr;
                if (flat) { int np = 3 + Next(2); var lb = new StringBuilder(); for (int k = 0; k < np; k++) { if (k > 0) lb.Append(' '); lb.Append(phrases[Next(pPer)]); } lineStr = lb.ToString(); }
                else lineStr = templates[Next(tPer)];
                var bytes = Encoding.UTF8.GetBytes(lineStr);
                if (l % holdEvery == holdEvery - 1) _heldout.Add((f, bytes));
                else _lines.Add((f, bytes));
            }
        }

        Radula.ReorderPool(_lines, families, poolOrder, seed);        // arrange the intake stream (blocked/roundrobin/shuffle)
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE FILE CORPUS — REAL code as the intake pool (the synthetic → natural TRANSFER test)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// Real source files as the intake corpus — does the synthetic tower win TRANSFER to natural code hierarchy?
/// Each source FILE is a family (its own identifiers/idioms = the near-disjoint-vocabulary analog of a tower
/// family), each non-blank LINE a span, every `holdEvery`-th line per family held out as the generalization
/// probe. Re-Pair runs over the concatenated tape, so multi-line idioms and function templates still form —
/// maxSpan then reads whether the residual-driven frontier reaches that DEEP scale where a globally-mixed feed
/// caps at the lexical (single-line) scale. Leading indentation is KEPT (it is nesting structure). Family order
/// is deterministic (ordinal path sort), so the corpus + curves replay bit-for-bit (the Vow).
public sealed class FileCorpus : IIntakeCorpus
{
    private readonly List<(int Fam, byte[] Bytes)> _lines = new();
    private readonly List<(int Fam, byte[] Bytes)> _heldout = new();
    private readonly List<(int Fam, byte[] Bytes)> _selected = new();
    public IReadOnlyList<(int Fam, byte[] Bytes)> Lines => _lines;
    public IReadOnlyList<(int Fam, byte[] Bytes)> Heldout => _heldout;
    /// Every non-blank selected source line in file order. Corpus curricula split this
    /// into train/held-out views; a runtime curriculum uses this complete world mouth so
    /// selected source bytes reach the tape instead of surviving only as a hash.
    internal IReadOnlyList<(int Fam, byte[] Bytes)> Selected => _selected;
    public int Families { get; }

    public FileCorpus(string path, string glob, int holdEvery, string poolOrder, ulong seed)
    {
        var files = GatherFiles(path, glob);
        for (int f = 0; f < files.Count; f++)
        {
            int line = 0;
            foreach (var raw in File.ReadLines(files[f]))
            {
                var text = raw.TrimEnd();                             // drop trailing whitespace; keep leading indent (structure)
                if (text.Trim().Length == 0) continue;               // skip blank / whitespace-only lines
                var bytes = Encoding.UTF8.GetBytes(text);
                _selected.Add((f, bytes));
                if (line++ % holdEvery == holdEvery - 1) _heldout.Add((f, bytes));
                else _lines.Add((f, bytes));
            }
        }
        Families = files.Count;
        Radula.ReorderPool(_lines, Families, poolOrder, seed);        // same stream-arrangement as the tower (blocked/roundrobin/shuffle)
    }

    /// A single file → that one family; a directory → every file matching the (comma-separated) glob, recursively,
    /// in ordinal path order so families are numbered deterministically.
    /// Resolve the exact source selection used by FileCorpus. A file is one selected source;
    /// a directory expands each comma-separated glob recursively and deduplicates before the
    /// ordinal path sort that assigns family ordinals.
    internal static List<string> GatherFiles(string path, string glob)
    {
        if (File.Exists(path)) return [path];
        if (!Directory.Exists(path)) return [];
        var found = new List<string>();
        foreach (var pat in glob.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            found.AddRange(Directory.GetFiles(path, pat, SearchOption.AllDirectories));
        return found.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// Gather a real repository's readable source files once, without materializing an
    /// intermediate sites file.  The repository world and the intake corpus share this
    /// traversal contract so a path is selected, ordered, and hashed exactly once.
    /// Generated trees, dependency caches, hidden control directories, binary files, and
    /// symlinked subtrees stay outside the world boundary.
    public static IReadOnlyList<string> GatherRepositoryFiles(string root, string glob)
    {
        string fullRoot = Path.GetFullPath(root);
        if (File.Exists(fullRoot)) return [fullRoot];
        if (!Directory.Exists(fullRoot)) return [];

        var patterns = glob.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0) patterns = ["*"];
        var files = new List<string>();
        GatherRepositoryFiles(fullRoot, patterns, files);
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static void GatherRepositoryFiles(string directory, string[] patterns, List<string> files)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(directory).OrderBy(static p => p, StringComparer.Ordinal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"repository traversal cannot read '{directory}'", ex);
        }

        foreach (string entry in entries)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"repository traversal cannot inspect '{entry}'", ex);
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

            if ((attributes & FileAttributes.Directory) != 0)
            {
                string name = Path.GetFileName(entry);
                if (name.StartsWith(".", StringComparison.Ordinal) || RepositoryDeniedDirectories.Contains(name)) continue;
                GatherRepositoryFiles(entry, patterns, files);
                continue;
            }

            string fileName = Path.GetFileName(entry);
            if (RepositoryDeniedSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) continue;
            if (!patterns.Any(pattern => MatchesRepositoryPattern(fileName, pattern))) continue;
            EnsureRepositoryTextFile(entry);
            files.Add(entry);
        }
    }

    private static void EnsureRepositoryTextFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4096];
            int read = stream.Read(head);
            if (head[..read].Contains((byte)0))
                throw new InvalidDataException($"repository source is binary: '{path}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"repository traversal cannot read '{path}'", ex);
        }
    }

    private static bool MatchesRepositoryPattern(string fileName, string pattern)
    {
        if (pattern is "*" or "*.*") return true;
        int star = pattern.IndexOf('*');
        if (star < 0) return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        string prefix = pattern[..star];
        string suffix = pattern[(star + 1)..];
        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && fileName.Length >= prefix.Length + suffix.Length;
    }

    private static readonly HashSet<string> RepositoryDeniedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "target", "node_modules", "dist", "build", "vendor", "third_party", "__pycache__", "venv",
        "runs", "scratchpad", "out", ".git", ".hg", ".svn", ".idea", ".vs"
    };

    private static readonly string[] RepositoryDeniedSuffixes =
        [".g.cs", ".generated.cs", ".Designer.cs", ".min.js", ".min.css", ".lock", "-lock.json"];

    /// Hash the repository source selection using the same length-framed path/content
    /// authority as the runtime FileCorpus.  The caller may pass the already selected
    /// files to avoid a second filesystem walk.
    public static string ComputeRepositoryWorldSHA256(string root, IReadOnlyList<string> files)
    {
        string fullRoot = Path.GetFullPath(root);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (string file in files.OrderBy(static p => p, StringComparer.Ordinal))
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(fullRoot, file));
            byte[] relativeBytes = Encoding.UTF8.GetBytes(relative);
            BinaryPrimitives.WriteInt64LittleEndian(length, relativeBytes.LongLength);
            hash.AppendData(length);
            hash.AppendData(relativeBytes);
            byte[] content = File.ReadAllBytes(file);
            BinaryPrimitives.WriteInt64LittleEndian(length, content.LongLength);
            hash.AppendData(length);
            hash.AppendData(content);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// Hash the selected source world, including names and bytes rather than only aggregate
    /// corpus counts. The framing makes equal-length edits and path substitutions distinct.
    /// This is deliberately the same file selection seam as FileCorpus, not a second glob walk.
    internal static string ComputeWorldSHA256(string path, string glob)
    {
        List<string> files = GatherFiles(path, glob);
        string root = File.Exists(path)
            ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory()
            : Path.GetFullPath(path);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (string file in files)
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(root, file));
            byte[] relativeBytes = Encoding.UTF8.GetBytes(relative);
            BinaryPrimitives.WriteInt64LittleEndian(length, relativeBytes.LongLength);
            hash.AppendData(length);
            hash.AppendData(relativeBytes);
            byte[] content = File.ReadAllBytes(file);
            BinaryPrimitives.WriteInt64LittleEndian(length, content.LongLength);
            hash.AppendData(length);
            hash.AppendData(content);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void ValidateExpectedWorldSHA256(string expected)
    {
        if (expected.Length != 0
            && (expected.Length != 64 || expected.Any(static c => !Uri.IsHexDigit(c))))
            throw new InvalidDataException("expected world SHA-256 must be empty or 64 hexadecimal characters");
    }

    internal static bool VerifyWorldIdentityFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.Combine(".tmp", $"file-corpus-world-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            string cs = Path.Combine(root, "a.cs");
            string md = Path.Combine(nested, "b.md");
            string txt = Path.Combine(root, "ignored.txt");
            File.WriteAllText(cs, "alpha\n");
            File.WriteAllText(md, "beta\n");
            File.WriteAllText(txt, "gamma\n");
            string exact = ComputeWorldSHA256(root, "*.cs,*.md");
            string same = ComputeWorldSHA256(root, "*.cs,*.md");
            var snapshot = new Tool.RepositoryWorldSnapshot(root, "*.cs,*.md");
            var grep = snapshot.Act(Tool.ToolCall.Parse("grep alpha"));
            var read = snapshot.Act(Tool.ToolCall.Parse("read a.cs:1"));
            var listing = snapshot.Act(Tool.ToolCall.Parse("ls ."));
            var traversal = snapshot.Act(Tool.ToolCall.Parse("read ../a.cs:1"));
            (string WorldSHA256, string[] Inputs) live = CaptureRuntimeWorld(root, exact, Path.Combine(root, "live"), authority: CortexPolicyAuthorities.Grammar);
            (string WorldSHA256, string[] Inputs) control = CaptureRuntimeWorld(root, exact, Path.Combine(root, "control"), authority: CortexPolicyAuthorities.Launchpad);
            (string WorldSHA256, string[] Inputs) unset = CaptureRuntimeWorld(root, "", Path.Combine(root, "unset"), authority: CortexPolicyAuthorities.Grammar);
            File.WriteAllText(cs, "omega\n"); // equal length, different bytes
            string edited = ComputeWorldSHA256(root, "*.cs,*.md");
            string selected = ComputeWorldSHA256(root, "*.cs");
            bool selectedGlob = GatherFiles(root, "*.cs,*.md").Count == 2
                && GatherFiles(root, "*.cs").Count == 1
                && !string.Equals(selected, edited, StringComparison.Ordinal);
            bool staleRegistrationRejected = false;
            try
            {
                _ = CaptureRuntimeWorld(root, exact, Path.Combine(root, "stale"), authority: CortexPolicyAuthorities.Grammar);
            }
            catch (InvalidDataException)
            {
                staleRegistrationRejected = true;
            }
            bool sameWorldInput = live.WorldSHA256 == exact
                && control.WorldSHA256 == exact
                && live.WorldSHA256 == control.WorldSHA256
                && live.Inputs.SequenceEqual(control.Inputs, StringComparer.Ordinal)
                && live.Inputs.Any(static input => input.Contains("alpha", StringComparison.Ordinal));
            bool unsetPreserved = unset.WorldSHA256.Length == 0 && unset.Inputs.Length == 0;
            bool snapshotActions = snapshot.WorldSHA256 == exact
                && grep.HitPaths.Contains((Tool.RepositoryPath)"a.cs")
                && read.Locus is { Path.Value: "a.cs", Line: 1 }
                && listing.HitPaths.Count == 2
                && traversal.HitPaths.Count == 0;
            bool pass = exact == same
                && !string.Equals(exact, edited, StringComparison.Ordinal)
                && staleRegistrationRejected
                && sameWorldInput
                && unsetPreserved
                && snapshotActions
                && selectedGlob;
            output.WriteLine($"  file-corpus world identity · exact={(exact == same ? "stable" : "DRIFT")} · organism-input={(sameWorldInput ? "same-world" : "MISSING")} · unset={(unsetPreserved ? "unchanged" : "CHANGED")} · snapshot={(snapshotActions ? "indexed-actions" : "BROKEN")} · equal-length-edit={(!string.Equals(exact, edited, StringComparison.Ordinal) && staleRegistrationRejected ? "rejected" : "ACCEPTED")} · selected-glob={(selectedGlob ? "exact" : "BROKEN")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static (string WorldSHA256, string[] Inputs) CaptureRuntimeWorld(
        string corpus,
        string expectedWorldSHA256,
        string runDirectory,
        CortexPolicyAuthorities authority)
    {
        CortexEmlCurriculum curriculum = new()
        {
            Corpus = new CogitoCorpus { Path = corpus, Glob = "*.cs,*.md", ExpectedWorldSHA256 = expectedWorldSHA256 },
            Actions = EmlActionSelections.Off,
        };
        Cortex cortex = new(new CortexConfig
        {
            RunName = "world-fixture",
            Seed = 0xC0117011UL,
            Steps = 1,
            Curriculum = curriculum,
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig { AuthorityCeiling = authority },
            },
        });
        string[] inputs = [];
        Cortex? bound = null;
        int exit = cortex.Run((runtime, _) => bound = runtime);
        if (exit != 0) throw new InvalidDataException($"runtime world fixture setup failed with exit {exit}");
        List<string> observed = new();
        foreach (TapeEventView view in bound!.Tape.GetEventViews())
        {
            if (!string.Equals(view.Source, "corpus", StringComparison.Ordinal)
                || !bound.Tape.Resolve(view.Id, out byte[] bytes)) continue;
            observed.Add(Encoding.UTF8.GetString(bytes));
        }
        inputs = [.. observed];
        string boundWorldSHA256 = (cortex.Config.Curriculum as CortexEmlCurriculum)?.Corpus?.ExpectedWorldSHA256 ?? "";
        return (boundWorldSHA256, inputs);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE EXPERIMENT
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

public static class Radula
{
    /// A held-out line is DEEP-grokked when it compresses below this many symbols per byte. A line is ~70B; the
    /// word scale parses to ~10 symbols (≈0.14 sym/B), the template scale to ~1–2 (≈0.03 sym/B). 0.07 sits between
    /// — cleared only once the grammar reached ABOVE the word scale into phrase/template structure.
    private const double DeepThresholdSymPerByte = 0.07;

    /// One checkpoint on a policy's grok curve — read off the grammar induced over what it has ingested so far.
    /// DeepFams (# families whose held-out COMPRESSES past the word scale = deep structure learned) is the
    /// headline: frontier deep-groks a GROWING SET (bacterial), random covers ALL shallowly and deep-groks NONE
    /// (breadth = the DL local minimum). BestFamSym = the deepest family's held-out symbols/byte (lower = deeper).
    private readonly record struct Curve(
        string Policy, int Round, int Spans, int Bytes, int Rules, int Scales,
        double Exponent, double CvZ, double MaxSpan, double HeldCov, int DeepFams, double BestFamSym, double Comp)
    {
        public const string Header = "policy\tround\tspans\tbytes\trules\tscales\texponent\tcvz\tmaxspan\theldcov\tdeepfams\tbestfamsym\tcomp";
        public string Row() => $"{Policy}\t{Round}\t{Spans}\t{Bytes}\t{Rules}\t{Scales}\t{Exponent:F3}\t{CvZ:F3}\t{MaxSpan:F0}\t{HeldCov:F4}\t{DeepFams}\t{BestFamSym:F4}\t{Comp:F4}";
    }

    /// usage: intake [--fam K] [--morph N] [--win W] [--words N] [--phrases N] [--lines N] [--batch M] [--seed HEX] [--negctrl]
    ///        intake --corpus <file|dir> [--glob "*.cs"] [--batch M] [--pool roundrobin|blocked|shuffle]   ← the real-code TRANSFER test
    public static int Run(string[] args)
    {
        int fam      = Args.Int(args, "--fam", 8);
        int nMorph   = Args.Int(args, "--morph", 90);
        int mWin     = Args.Int(args, "--win", 12);
        int overlap  = Args.Int(args, "--overlap", 0);       // 0 = disjoint islands (the clean depth proof); >0 adds a bridge for the adjacency crawl
        int wPer     = Args.Int(args, "--words", 12);
        int pPer     = Args.Int(args, "--phrases", 16);
        int tPer     = Args.Int(args, "--templates", 12);    // L3 deep scale: templates/family — rarer than phrases, so only concentration groks them
        int linesPer = Args.Int(args, "--lines", 60);
        int batch    = Args.Int(args, "--batch", 3);
        int seedLines= Args.Int(args, "--seedlines", 1);     // minimal bootstrap so frontier's coverage can discriminate — NOT enough to grok a family (no depth contamination)
        bool neg     = args.Contains("--negctrl");
        bool flat    = args.Contains("--flat");           // negative control: no deep template scale → frontier's advantage must vanish
        string pool  = Args.Str(args, "--pool", "roundrobin");   // roundrobin (mixed feed, adversarial) | blocked (pre-concentrated) | shuffle
        string real  = Args.Str(args, "--corpus", "");       // real-code TRANSFER test: a file or dir of source (families = files) instead of the synthetic tower
        string glob  = Args.Str(args, "--glob", "*.cs");     // which files a --corpus DIR pulls (comma-separated globs)
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);

        IIntakeCorpus corpus; string label;
        if (real.Length > 0) { corpus = new FileCorpus(real, glob, holdEvery: 8, pool, seed); label = $"REAL CODE — {corpus.Families} files ({real}) · the synthetic→natural TRANSFER test"; }
        else { corpus = new TowerCorpus(fam, nMorph, mWin, overlap, wPer, pPer, tPer, linesPer, holdEvery: 8, seed, neg, pool, flat); label = flat ? "FLAT-CONTROL (no deep scale)" : neg ? "NEG-CONTROL (shuffled families)" : "tower hierarchy"; }
        int totalBytes = corpus.Lines.Sum(l => l.Bytes.Length);

        var run = Cogito.Run.New(real.Length > 0 ? "intake-real" : neg ? "intake-negctrl" : "intake");   // fully-qualified: `Run` alone binds to Radula.Run (this class)
        Trace.Note($"intake · {corpus.Families} families · {corpus.Lines.Count} trainable lines ({totalBytes}B) + {corpus.Heldout.Count} held-out · batch {batch} · pool={pool} · {label}");
        Trace.Note($"  the SAME engine (Re-Pair+MDL), three intake policies — WHICH span next is the only variable");

        // Identical minimal SEED for every policy (the first `seedLines` family-0 lines): same starting grammar,
        // different growth — so the divergence is the SELECTION, not the seed. Kept tiny so it never groks a family.
        var seedIdx = corpus.Lines.Select((l, i) => (l, i)).Where(x => x.l.Fam == 0).Take(seedLines).Select(x => x.i).ToList();

        var all = new List<Curve>();
        foreach (var policy in new[] { "sequential", "random", "frontier" })
            all.AddRange(DrivePolicy(policy, corpus, seedIdx, batch, seed));

        // ── the curve + the matched-budget verdict ──
        run.WriteCurve("curve.tsv", Curve.Header + "\n" + string.Join("\n", all.Select(c => c.Row())) + "\n");
        run.Write("corpus.txt", string.Join("\n", corpus.Lines.Select(l => Encoding.UTF8.GetString(l.Bytes))) + "\n");
        Report(all, totalBytes, corpus.Families);
        return 0;
    }

    /// Drive one intake policy from the shared seed to pool-exhaustion, reading the grok curve each round.
    private static List<Curve> DrivePolicy(
        string policy, IIntakeCorpus corpus, List<int> seedIdx, int batch, ulong seed)
    {
        int n = corpus.Lines.Count;
        var poolBytes = new byte[n][];                                            // the span-bytes view frontier scores over
        for (int i = 0; i < n; i++) poolBytes[i] = corpus.Lines[i].Bytes;
        var frontier = policy == "frontier" ? new FrontierIndex(poolBytes) : null;   // pool postings, built once (face 3c)
        var ingested = new bool[n];
        var tape = new List<byte>();
        void Take(int i) { if (!ingested[i]) { ingested[i] = true; tape.AddRange(corpus.Lines[i].Bytes); tape.Add((byte)'\n'); } }
        foreach (var i in seedIdx) Take(i);

        ulong rng = seed ^ 0x5EED;
        int NextRand(int m) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)m); }

        var curve = new List<Curve>();
        int seqCursor = 0, round = 0;
        while (true)
        {
            var g = Engine.Induce(tape.ToArray()).Result;
            var (scales, meanZ, cvZ, maxSpan, _) = Engine.RenormStats(g);
            // per-family reads. COVERAGE (byte-cover) is the SHALLOW read — words already cover a line's bytes, so
            // it saturates at the word scale and is blind to depth. PARSE-SIZE (symbols/line) is the DEEP read — it
            // collapses toward 1 only when the family's TEMPLATE (the deep scale) was learned. A family is
            // DEEP-GROKKED when its held-out compresses below θ_deep. Frontier concentrates → the deep scale clears
            // MDL for the families it works → DEEP-groks a GROWING set; random spreads → words everywhere, templates
            // NOWHERE → covers all (breadth) but deep-groks none (the DL local minimum, master of none).
            var famSym = new double[corpus.Families]; var famCov = new double[corpus.Families]; var famCnt = new int[corpus.Families];
            var cover = new Engine.GrammarCover(g.Rules);                     // hoisted out of the held-out loop: the cover is a pure function of g, so ParsedSize+CoverageOf were rebuilding it 2× PER held-out span
            foreach (var (fm, hb) in corpus.Heldout)
            {
                famSym[fm] += hb.Length == 0 ? 0 : (double)cover.ParsedSize(hb) / hb.Length;   // symbols per byte (lower = deeper)
                famCov[fm] += cover.Coverage(hb);
                famCnt[fm]++;
            }
            for (int fmi = 0; fmi < corpus.Families; fmi++) { famSym[fmi] /= Math.Max(1, famCnt[fmi]); famCov[fmi] /= Math.Max(1, famCnt[fmi]); }
            double heldCov = corpus.Heldout.Count == 0 ? 0 : corpus.Heldout.Average(h => famCov[h.Fam]);      // breadth (coverage)
            int deepFams = famSym.Count(s => s > 0 && s < DeepThresholdSymPerByte);                          // DEEP-grok count (compression)
            double bestFamSym = famSym.Where(s => s > 0).DefaultIfEmpty(1).Min();                            // deepest family's sym/byte
            int spans = 0, bytes = tape.Count; foreach (var b in ingested) if (b) spans++;
            double comp = g.Compressed.Length == 0 ? 0 : 1.0 - (double)g.Compressed.Length / Math.Max(1, CountSymbols(tape));
            curve.Add(new Curve(policy, round++, spans, bytes, g.Rules.Length, scales, meanZ, cvZ, maxSpan, heldCov, deepFams, bestFamSym, comp));

            var remaining = Enumerable.Range(0, n).Where(i => !ingested[i]).ToList();
            if (remaining.Count == 0) break;

            // ── the intake step — the ONLY thing that differs between policies ──
            int m = Math.Min(batch, remaining.Count);
            switch (policy)
            {
                case "sequential":                                                 // corpus order (walk-forward)
                    for (int k = 0; k < m; k++) { while (ingested[seqCursor]) seqCursor = (seqCursor + 1) % n; Take(seqCursor); }
                    break;
                case "random":                                                     // random-global (the DL way)
                    for (int k = 0; k < m; k++) { var r = remaining.Where(i => !ingested[i]).ToList(); Take(r[NextRand(r.Count)]); }
                    break;
                case "frontier":                                                   // RLEI — the shared developmental-curriculum root (Farm.Drive calls the same)
                    foreach (var i in FrontierPick(cover, poolBytes, ingested, m, frontier!)) Take(i);
                    break;
            }
        }
        return curve;
    }

    private static int CountSymbols(List<byte> tape) => tape.Count;   // byte tokenizer: 1 symbol/byte (comp% denominator)

    // ── the read ──  per-policy grok curve, then the matched-budget verdict. DEPTH = grok: maxSpan (deepest rule's
    // byte-extent = correlation length) + seedfam-cov (how deeply the concentrated family is grokked) are the
    // headline; held-cov (ALL families) is the breadth read — random wins it by spreading (jack of all, master of
    // none = the DL local minimum), frontier trades it for depth (the grok). The tension IS the finding.
    private static void Report(List<Curve> all, int totalBytes, int families)
    {
        Curve At(string p, int budget)
        {
            var c = all.Where(x => x.Policy == p && x.Bytes <= budget).OrderBy(x => x.Bytes).LastOrDefault();
            return c.Policy is null ? all.Where(x => x.Policy == p).OrderBy(x => x.Bytes).First() : c;
        }
        int[] pct = [10, 20, 35, 50, 75, 100];
        Trace.Note("");
        Trace.Note($"  ── GROK CURVES (same engine; intake policy is the only variable) ──");
        Trace.Note($"     deepFams=# families whose HELD-OUT compresses past the word scale (DEEP grok) · bestSym=deepest family sym/byte (↓deeper) · allCov=breadth");
        foreach (var p in new[] { "sequential", "random", "frontier" })
        {
            Trace.Note($"    {p}");
            foreach (int pc in pct)
            {
                int budget = totalBytes * pc / 100; var c = At(p, budget);
                Trace.Note($"      {pc,4}% ({c.Bytes,6}B, {c.Spans,3} spans)  deepFams {c.DeepFams}/{families}  bestSym {c.BestFamSym,5:F3}  maxSpan {c.MaxSpan,4:F0}B  allCov {c.HeldCov,4:P0}  scales {c.Scales,2}");
            }
        }

        // Verdict at an EARLY budget (the concentration-starved regime — where the deep scale is barely affordable,
        // so only a policy that CONCENTRATES the budget can reach it). Lead with maxSpan (the robust, continuous
        // correlation-length read) over the noisy hard-threshold deepFams count.
        var mid = totalBytes * 20 / 100;
        var f = At("frontier", mid); var r = At("random", mid); var s = At("sequential", mid);
        double Ceiling(string p) => all.Where(x => x.Policy == p).Max(x => x.MaxSpan);   // deepest rule reached at ANY budget
        Trace.Note("");
        Trace.Note($"  ── VERDICT @ 20% budget ({mid}B) — the concentration-starved regime ──");
        Trace.Note($"    frontier    maxSpan {f.MaxSpan,3:F0}B  deepFams {f.DeepFams}/{families}  bestSym {f.BestFamSym:F3}   (concentrated → reached the deep TEMPLATE scale)");
        Trace.Note($"    random      maxSpan {r.MaxSpan,3:F0}B  deepFams {r.DeepFams}/{families}  bestSym {r.BestFamSym:F3}   allCov {r.HeldCov:P0} broad — but shallow (the DL local minimum, master of none)");
        Trace.Note($"    sequential  maxSpan {s.MaxSpan,3:F0}B  deepFams {s.DeepFams}/{families}  bestSym {s.BestFamSym:F3}   (fixed walk-forward on a MIXED feed = starved like random)");
        Trace.Note($"    run-ceiling correlation length (deepest rule at ANY budget):  frontier {Ceiling("frontier"):F0}B · random {Ceiling("random"):F0}B · sequential {Ceiling("sequential"):F0}B");
        bool deeper = f.MaxSpan > Math.Max(r.MaxSpan, s.MaxSpan) * 1.3 || f.DeepFams > Math.Max(r.DeepFams, s.DeepFams);
        Trace.Note($"    → {(deeper ? $"FRONTIER GROKS DEEPER — at {mid}B its correlation length is {f.MaxSpan:F0}B vs {r.MaxSpan:F0}/{s.MaxSpan:F0}B (random/sequential): only the residual-driven policy concentrates the budget enough to clear the MDL gate on the deep scale a globally-mixed feed starves" : "no separation at this regime — concentration threshold not crossed (sweep --fam up / --batch down / --templates up)")}");
    }

    // ── the RLEI primitive — shared by this proof's frontier arm AND Farm.Drive (the 24h farmer's intake root) ──

    /// The frontier pick — the developmental-curriculum ROOT (cogito ≠ DL). From the un-ingested pool, the spans
    /// the CURRENT grammar compresses BEST (max CoverageOf = the edge-of-known, minimal residual-to-attach):
    /// concentrate the budget into one family until its deep scale clears the ≥3× MDL gate, then the highest
    /// remaining coverage is the ADJACENT family (a shared-substructure bridge) → the frontier crawls the
    /// similarity graph outward = bacterial growth. This REPLACES the DL batch-dump (dump the whole corpus and the
    /// grammar caps SHALLOW; residual-driven local intake groks the deep scale a globally-mixed feed starves).
    /// Deterministic: max coverage, span-index tie-break.
    public static List<int> FrontierPick(RePairResult g, IReadOnlyList<byte[]> pool, bool[] ingested, int count)
        => FrontierPick(new Engine.GrammarCover(g.Rules), pool, ingested, count);   // full cover (byte-exact intake path)

    /// The frontier pick over a PREBUILT cover basis — the hot-scale entry (CritLock passes a capped cover so
    /// scoring a large pool every round stays O(pool·span·capExps) instead of O(pool·span·rules)). Same residual
    /// (max coverage, span-index tie-break); the caller owns the cover's fidelity.
    public static List<int> FrontierPick(Engine.GrammarCover cover, IReadOnlyList<byte[]> pool, bool[] ingested, int count)
    {
        var scored = new List<(int I, double Cov)>();
        for (int i = 0; i < pool.Count; i++)
            if (!ingested[i]) scored.Add((i, cover.Coverage(pool[i])));
        scored.Sort((a, b) => a.Cov != b.Cov ? b.Cov.CompareTo(a.Cov) : a.I.CompareTo(b.I));   // max cov, i tie-break
        var picks = new List<int>(Math.Min(count, scored.Count));
        for (int k = 0; k < count && k < scored.Count; k++) picks.Add(scored[k].I);
        return picks;
    }

    /// The HIERARCHICAL frontier pick (face 3c) — the O(Δ)-scale entry: score ONLY the index-gathered candidate
    /// spans (bounded by the grammar's reach — the deep rules' gram postings), never the whole pool. Every span
    /// outside the candidate set has coverage EXACTLY 0 (FrontierIndex's superset law), and the full scan orders
    /// all zero-coverage spans purely by index — so positives-then-index-ordered-zero-fill reproduces the full
    /// scan's picks BYTE-IDENTICALLY at O(candidates) instead of O(pool) per Draw (the Vow holds by construction,
    /// not by tolerance).
    public static List<int> FrontierPick(Engine.GrammarCover cover, IReadOnlyList<byte[]> pool, bool[] ingested, int count, FrontierIndex index)
        => index.Pick(cover, pool, ingested, count);

    /// RePairResult convenience for the indexed pick (Farm.Drive, the intake experiment): builds the full
    /// byte-exact cover for THIS grammar (per-round, as the un-indexed overload always did), then picks
    /// hierarchically. Same picks, O(candidates) scoring.
    public static List<int> FrontierPick(RePairResult g, IReadOnlyList<byte[]> pool, bool[] ingested, int count, FrontierIndex index)
        => FrontierPick(new Engine.GrammarCover(g.Rules), pool, ingested, count, index);

    // ── THE INTAKE-AFFIRM GATE — the frontier's DUAL, the self-maintaining-memory source-fix ──
    //
    // FrontierPick maximizes coverage — the edge-of-known, the span with the SMALLEST residual still worth attaching.
    // Affirm is its reflection at the ceiling: a span the grammar generates WHOLE has residual ZERO — nothing left to
    // attach — so re-appending it banks a byte the grammar already produces (mints no rule: the Loom's standing
    // invariant, Loom.cs:23-29) while it costs tape. Under a finite budget that re-append is exactly zero MDL savings
    // at a positive byte cost, so a description-length minimizer is ALREADY penalized for it; this gate makes that
    // penalty a hard veto. It is the SAME "already affirmed" predicate the aestivation SHED reads post-hoc over resident
    // spans (loom.ParsedLenOf ≤ 1, Cortex.cs:1425), moved one step earlier — pre-hoc, at the door — so the byte is
    // PREVENTED, not reclaimed after it has already inflated the resident view and been re-observed. And it is the
    // world-mouth twin of ReplayCalc.AccreteWeight (ReplayCalc.cs:224) — one law, two mouths: don't re-bank what you
    // already generate; the EML mouth keys on the certificate, the world mouth on the grammar residual.
    //
    // THE KEY is transient, not the standing loom: a MIX candidate is NOT yet spliced (its splice is the next INDUCE,
    // which is why Rhythm.FoldSpliced reads it one step LATER), so ParsedLenOf would return −1. GrammarCover.ParsedSize
    // (Engine.cs:261) is the transient affirmation — the greedy longest-match symbol count of the span against the
    // CURRENT grammar, no tape write. It feeds the SAME residual scalar the whole vision rests on: parsed/rawByteLen
    // (Rhythm.FoldSpliced, Rhythm.cs:204 — "a learned span parses toward ONE symbol, novelty ~0; a fresh one stays
    // near byte-per-symbol, novelty ~1"), a true fraction in [0,1] directly comparable to the rhythm's own residual
    // EMA. TWO tiers, because the shed criterion (ParsedLenOf ≤ 1, absolute) and the rhythm's residual (fractional)
    // are DIFFERENT tests and both matter (MEASURED — a grokked 27-byte code line parses to ~7 symbols, NEVER exactly
    // 1, so an absolute ≤1 gate is correct-but-inert on real corpora; the fractional cut is what actually fires):
    //   • ABSOLUTE floor — parsed ≤ 1 ⟺ the whole span is ONE symbol ⟺ the exact shed mirror (Cortex.cs:1425). Always
    //     skips a literally-affirmed span regardless of θ (a lone symbol carries no residual).
    //   • FRACTIONAL cut — parsed/rawByteLen ≤ θ ⟺ the residual FRACTION is below the affirm ceiling ⟺ the grammar
    //     generates (nearly) all of it. θ IS the budget's shadow: 0 disarms this stage (pure shed mirror, the
    //     safe/leaky end — only perfectly-affirmed spans skip); ~0.3 catches the grokked-but-multi-symbol code line;
    //     rising toward 1 skips ever-shallower re-admissionPlans (the tighter-budget, more-conscious pole).
    // The residual is computed WHOLE-SPAN (byte-normalized), which is inherently window-aware: a multi-line
    // window with one novel line keeps a higher parsed count → higher residual → not skipped, exactly as wanted.

    /// Does the CURRENT grammar already AFFIRM this span — generate it whole, residual ~0, nothing novel to bank?
    /// `affirmCut` is θ_affirm: the residual-FRACTION ceiling (parsed/rawByteLen, the Rhythm.cs:204 scalar) below which
    /// the span is a re-observation, not the world's edge; the threshold is the budget's one-sided shadow
    /// toward more skipping as the budget tightens. A literal one-symbol span (parsed ≤ 1) always affirms (the shed
    /// mirror). θ = 0 leaves ONLY that absolute floor armed (the safe/leaky end); θ &lt; 0 DISARMS the gate entirely
    /// (never affirms → every candidate appends → byte-identical to the pre-gate machine, the kill-line control arm).
    public const double ExactAffirmCut = 0.0;

    public readonly record struct Affirmation(int ParsedSymbols, int SemanticBytes, double Residual, bool Affirmed);

    public static Affirmation MeasureAffirmation(Engine.GrammarCover? cover, ReadOnlySpan<byte> span, double affirmCut)
    {
        if (affirmCut < 0 || cover is null) return new Affirmation(span.Length, span.Length, 1, Affirmed: false);
        if (span.Length == 0) return new Affirmation(0, 0, 0, Affirmed: true);
        int parsed = cover.ParsedSize(span);                   // greedy longest-match symbol count against the standing grammar (no splice, no copy)
        double residual = (double)parsed / span.Length;
        return new Affirmation(parsed, span.Length, residual,
            Affirmed: parsed <= 1 || residual <= affirmCut);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════
    //  THE FRONTIER-BENCH KILL-LINE (face 3c) — per-Draw wall FLAT vs pool size, picks byte-identical
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// usage: frontierbench [--pools 2000,50000,1000000] [--draws 10] [--batch 8] [--cap 400] [--warm 240]
    ///                      [--fidmax 60000] [--seed HEX]
    /// The pre-registered face-3c readout: sweep the pool by ADDING FAMILIES (the world grows in breadth — the
    /// scale story), warm the grammar on the first families, then per Draw run the FULL SCAN and the HIERARCHICAL
    /// pick on identical state. Kill-line: (a) hierarchical per-Draw wall must NOT climb with pool size (the
    /// candidate set tracks grammar reach, not pool); (b) picks BYTE-IDENTICAL to the full scan wherever the full
    /// scan is affordable (--fidmax caps its arm) — plus an early-grammar arm (3-span warm, uncapped cover) that
    /// exercises the short-expansion 2-gram fallback and the zero-coverage fill.
    public static int FrontierBench(string[] args)
    {
        int[] pools  = Args.Str(args, "--pools", "2000,50000,1000000").Split(',').Select(int.Parse).ToArray();
        int draws    = Args.Int(args, "--draws", 10);
        int batch    = Args.Int(args, "--batch", 8);
        int cap      = Args.Int(args, "--cap", 400);           // the drive's FrontierCapExps discipline
        int warm     = Args.Int(args, "--warm", 1000);         // spans the grammar is induced over — enough that the top-`cap` basis is ALL deep (a tiny grammar's short tail rides dense 2-gram postings and candidacy is genuinely ~pool-dense; the early-grammar arm below covers that regime's exactness)
        int fidMax   = Args.Int(args, "--fidmax", 60000);      // full-scan arm cap (it is the O(pool) side being retired)
        ulong seed   = Args.Seed(args, "--seed", 0xF07711E4UL);

        var run = Cogito.Run.New("frontierbench");
        Trace.Note($"frontierbench · pools [{string.Join(", ", pools)}] · {draws} draws · batch {batch} · cover cap {cap} · warm {warm} spans · full-scan fidelity ≤ {fidMax} spans");
        var tsv = new StringBuilder("pool\tspans\tbuild_ms\tfull_ms\thier_ms\tcands\tbasis_deep\tbasis_short\tpost_mass\tspeedup\tfidelity\n");

        foreach (int target in pools)
        {
            var pool = BenchPool(target, seed);
            int n = pool.Count;

            // the grammar = the machine's current reach: induced over EVERY OTHER span of the first families
            // (a strided half-eaten prefix — mid-drain frontier life: the reached families still hold un-ingested
            // spans the deep rules score HIGH; warming whole families instead leaves the grammar's reach exactly
            // ≡ the eaten set and candidates are rightly zero — a degenerate all-zero-fill bench).
            var warmBytes = new List<byte>();
            int w0 = Math.Min(warm, n / 2);
            for (int i = 0; i < 2 * w0; i += 2) { warmBytes.AddRange(pool[i]); warmBytes.Add((byte)'\n'); }
            var g = Engine.Induce(warmBytes.ToArray()).Result;
            var cover = new Engine.GrammarCover(g.Rules, cap);

            var sw = Stopwatch.StartNew();
            var index = new FrontierIndex(pool);
            long buildMs = sw.ElapsedMilliseconds;

            var ingested = new bool[n];
            for (int i = 0; i < 2 * w0; i += 2) ingested[i] = true;

            bool fidelity = true; int fidChecked = 0;
            double fullMs = 0, hierMs = 0, cands = 0; int fullRuns = 0;
            for (int d = 0; d < draws; d++)
            {
                List<int>? full = null;
                if (n <= fidMax)
                {
                    sw.Restart(); full = FrontierPick(cover, pool, ingested, batch); sw.Stop();
                    fullMs += sw.Elapsed.TotalMilliseconds; fullRuns++;
                }
                sw.Restart(); var hier = FrontierPick(cover, pool, ingested, batch, index); sw.Stop();
                hierMs += sw.Elapsed.TotalMilliseconds;
                cands += index.LastCandidateCount;
                if (full is not null) { fidelity &= full.SequenceEqual(hier); fidChecked++; }
                foreach (int i in hier) ingested[i] = true;                       // advance the state — each Draw sees a moved frontier
            }
            fullMs /= Math.Max(1, fullRuns); hierMs /= draws; cands /= draws;
            var (bd, bs, bm) = index.BasisStats;
            string fid = fidChecked > 0 ? (fidelity ? $"IDENTICAL×{fidChecked}" : "DIVERGED") : "(skipped)";
            string speed = fullRuns > 0 ? $"{fullMs / Math.Max(1e-4, hierMs):F0}×" : "—";
            Trace.Note($"  pool {n,8} · index build {buildMs,5}ms · full {(fullRuns > 0 ? $"{fullMs,9:F2}ms" : "  (skipped)")} · hier {hierMs,7:F3}ms · cands {cands,7:F0} · basis {bd}d+{bs}s mass {bm} · {speed,6} · {fid}");
            tsv.AppendLine($"{target}\t{n}\t{buildMs}\t{(fullRuns > 0 ? fullMs.ToString("F3") : "")}\t{hierMs:F4}\t{cands:F0}\t{bd}\t{bs}\t{bm}\t{(fullRuns > 0 ? (fullMs / Math.Max(1e-4, hierMs)).ToString("F1") : "")}\t{fid}");
            if (fidChecked > 0 && !fidelity) { Trace.Note("  ✗ FIDELITY DIVERGED — the hierarchical pick is NOT byte-identical; kill-line FAILED"); run.Write("bench.tsv", tsv.ToString()); return 1; }
        }

        // ── the early-grammar arm ──  3-span warm ⇒ the cover is mostly SHORT rules (the 2-gram fallback) and
        // whole rounds score zero (the index-ordered fill) — the exactness paths a deep basis never exercises.
        {
            var pool = BenchPool(Math.Min(20000, fidMax), seed ^ 0xEA71);
            var warmBytes = new List<byte>();
            for (int i = 0; i < 3; i++) { warmBytes.AddRange(pool[i]); warmBytes.Add((byte)'\n'); }
            var g = Engine.Induce(warmBytes.ToArray()).Result;
            var cover = new Engine.GrammarCover(g.Rules);                         // UNCAPPED — short rules stay in the basis
            var index = new FrontierIndex(pool);
            var ingested = new bool[pool.Count];
            for (int i = 0; i < 3; i++) ingested[i] = true;
            bool ok = true;
            for (int d = 0; d < 12; d++)
            {
                var full = FrontierPick(cover, pool, ingested, batch);
                var hier = FrontierPick(cover, pool, ingested, batch, index);
                ok &= full.SequenceEqual(hier);
                foreach (int i in hier) ingested[i] = true;
            }
            Trace.Note($"  early-grammar arm (3-span warm, uncapped cover, {pool.Count} spans) · 12 draws · {(ok ? "IDENTICAL" : "DIVERGED")}");
            tsv.AppendLine($"early\t{pool.Count}\t\t\t\t\t\t{(ok ? "IDENTICAL×12" : "DIVERGED")}");
            if (!ok) { run.Write("bench.tsv", tsv.ToString()); return 1; }
        }

        run.Write("bench.tsv", tsv.ToString());
        Trace.Note("  ⇒ kill-line: hier wall flat vs pool (cands track grammar reach) + picks byte-identical wherever the full scan ran");
        return 0;
    }

    /// The bench pool — deterministic multi-family world that grows by ADDING families (each ~250 spans of
    /// family-private 5-char words composed into recurring 6-9-word templates), so grammar reach stays bounded
    /// while the pool sweeps 10³→10⁶. TowerCorpus caps at ~56 families (the 2-char morpheme space), hence local.
    /// Template count is deliberately HIGH (24/family): few templates make consecutive same-family lines recur
    /// as PAIRS, Re-Pair mints cross-line mega-rules above the line scale, and a longest-first cap then holds
    /// mostly '\n'-carriers — expansions that can never match a line-span, a degenerate basis (both arms
    /// zero-fill identically, but nothing is being scored). Many long templates keep the top-of-cap at the
    /// LINE scale — the regime the frontier actually schools in.
    private static List<byte[]> BenchPool(int spans, ulong seed)
    {
        const int perFam = 250, vocab = 40, templates = 24;
        var pool = new List<byte[]>(spans);
        ulong rng = seed;
        int Next(int m) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)m); }
        var sb = new StringBuilder(96);
        for (int f = 0; pool.Count < spans; f++)
        {
            var words = new string[vocab];
            for (int v = 0; v < vocab; v++)
            {
                // splitmix64-mixed base-26 encoding — words are globally (near-)unique and WELL-MIXED, so families
                // share no 5-grams except by 26⁵-space chance. A weak generator here leaks cross-family grams and
                // the candidate set silently grows with the pool — measuring the corpus, not the index.
                sb.Clear();
                ulong h = (ulong)(f * vocab + v) + 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL; h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL; h ^= h >> 31;
                for (int c = 0; c < 5; c++) { sb.Append((char)('a' + (int)(h % 26))); h /= 26; }
                words[v] = sb.ToString();
            }
            var tmpl = new string[templates];
            for (int t = 0; t < templates; t++)
            {
                sb.Clear();
                int nw = 6 + Next(4);
                for (int k = 0; k < nw; k++) { if (k > 0) sb.Append(' '); sb.Append(words[Next(vocab)]); }
                tmpl[t] = sb.ToString();
            }
            for (int l = 0; l < perFam && pool.Count < spans; l++)
                pool.Add(Encoding.UTF8.GetBytes(tmpl[Next(templates)]));
        }
        return pool;
    }

    /// Arrange the intake stream (shared by both corpora). "blocked" = family-by-family, as-built (pre-concentrated,
    /// locally coherent — where even fixed walk-forward groks); "roundrobin" = families cycle (the globally-MIXED
    /// feed, the adversarial case where only the residual-driven frontier recovers concentration); "shuffle" =
    /// seeded random. The RLEI point: recover the local curriculum from ANY ordering, so it never needs a sorted feed.
    internal static void ReorderPool(List<(int Fam, byte[] Bytes)> lines, int families, string poolOrder, ulong seed)
    {
        if (poolOrder == "blocked") return;
        var byFam = Enumerable.Range(0, families).Select(_ => new List<byte[]>()).ToArray();
        foreach (var (fm, b) in lines) byFam[fm].Add(b);
        var reordered = new List<(int, byte[])>();
        if (poolOrder == "shuffle")
        {
            var shuf = lines.ToList(); ulong r = seed ^ 0x5117;
            for (int i = shuf.Count - 1; i > 0; i--) { r = r * 6364136223846793005UL + 1442695040888963407UL; int j = (int)((r >> 33) % (ulong)(i + 1)); (shuf[i], shuf[j]) = (shuf[j], shuf[i]); }
            reordered = shuf;
        }
        else for (int i = 0; ; i++)                                            // roundrobin: one line from each family per cycle
        {
            bool any = false;
            for (int f = 0; f < families; f++) if (i < byFam[f].Count) { reordered.Add((f, byFam[f][i])); any = true; }
            if (!any) break;
        }
        lines.Clear(); lines.AddRange(reordered);
    }

}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE FRONTIER INDEX (face 3c) — gram postings over the FIXED pool; candidates from the grammar's own reach
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// THE PACKED RAIL — the frontier's posting profile (per-span, offset-free, unbounded, fed once over the FIXED
/// pool) frozen into three flat arrays: sorted gram keys, CSR prefix bounds, span ids. The mutable dict-of-lists
/// shape (GramPostings) spends ~116B/key (Dictionary entry + List header + backing-array header) + 16B/posting
/// (a GramPost carries an Off the frontier never reads) on a pool-proportional CONSTANT resident block — the
/// memstat census's headline elephant (~29M postings to index a ~20MB pool, ~694MB across the per-domain
/// frontiers). This layout pays 12B/key + 4B/posting for the SAME postings in the SAME order (span-ascending
/// within a key = feed order), so the candidate gather is bit-identical while the block shrinks ~5×. Lookups are
/// binary search — resolved once per PrepareBasis (per stride), never per Draw, so the swap is off the hot path.
public sealed class PackedSpanPostings
{
    private readonly ulong[] _keys;      // sorted distinct gram keys
    private readonly int[]   _starts;    // K+1 CSR bounds into _spans (segment k = [_starts[k], _starts[k+1]))
    private readonly int[]   _spans;     // span ids, ascending within each segment (the determinism contract)

    public int GramLen { get; }

    /// Two passes over the pool: count per key (per-span dedup — identical semantics to GramPostings' historical
    /// perSpan tail check: the feed is span-ascending, so "last posted id == this id" ⟺ this key already saw this
    /// span), then fill the packed segments in the same feed order. ONE transient dictionary carries both passes
    /// (value (X, Last): X = count in pass 1, segment index in pass 2) and dies at ctor exit — peak transient
    /// ~36B/key vs the old shape's PERMANENT ~116B/key.
    public PackedSpanPostings(int gramLen, IReadOnlyList<byte[]> pool)
    {
        GramLen = gramLen;
        var acc = new Dictionary<ulong, (int X, int Last)>();
        for (int i = 0; i < pool.Count; i++)
        {
            var span = pool[i];
            for (int off = 0; off + gramLen <= span.Length; off++)
            {
                ref var e = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                    acc, Simhash.Fnv64(span.AsSpan(off, gramLen)), out bool existed);
                if (!existed) e = (1, i);
                else if (e.Last != i) e = (e.X + 1, i);
            }
        }
        _keys = new ulong[acc.Count];
        acc.Keys.CopyTo(_keys, 0);
        Array.Sort(_keys);
        _starts = new int[_keys.Length + 1];
        for (int k = 0; k < _keys.Length; k++) _starts[k + 1] = _starts[k] + acc[_keys[k]].X;
        _spans = new int[_starts[^1]];
        var cursor = (int[])_starts.Clone();
        for (int k = 0; k < _keys.Length; k++) acc[_keys[k]] = (k, -1);
        for (int i = 0; i < pool.Count; i++)
        {
            var span = pool[i];
            for (int off = 0; off + gramLen <= span.Length; off++)
            {
                ref var e = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(
                    acc, Simhash.Fnv64(span.AsSpan(off, gramLen)));
                if (e.Last == i) continue;
                e.Last = i;
                _spans[cursor[e.X]++] = i;
            }
        }
    }

    /// The segment holding `key`'s postings, or −1 — absence PROVES the gram occurs in no indexed span (the
    /// frontier's whole-rule-drop direction).
    public int SegOf(ulong key) { int k = Array.BinarySearch(_keys, key); return k < 0 ? -1 : k; }
    public int CountOf(int seg) => _starts[seg + 1] - _starts[seg];
    /// The segment's span ids, ascending (feed order) — the Candidates walk.
    public ReadOnlySpan<int> SpansOf(int seg) => _spans.AsSpan(_starts[seg], _starts[seg + 1] - _starts[seg]);
    /// MemStat census read — distinct gram keys + Σ postings (identical counts to the mutable shape). Counts only.
    public (long Keys, long Posts) Mass() => (_keys.Length, _spans.Length);
}

/// Packed span postings over the intake pool, built ONCE at setup (O(pool bytes) — already paid by reading the
/// corpus), so each Draw gathers the spans worth scoring from the grammar's OWN rules instead of scanning the
/// pool: FrontierPick was O(pool·span·exps) EVERY Draw — the ~100× wall between the proofs (10³ spans) and the
/// skookum eval (10⁶).
///
/// THE SUPERSET LAW (what makes the fast pick EXACT, not approximate): a span scores > 0 iff some basis
/// expansion occurs in it as a whole substring, and e ⊆ s implies EVERY w-gram of e ⊆ s — so the postings of
/// any ONE gram of e name a superset of the spans carrying e, and the RAREST gram names the tightest such
/// superset. Union over the basis ⊇ {span : coverage > 0}; everything outside scores exactly 0 and re-enters
/// only through the pick's index-ordered zero-fill (the same order the full scan gives zeros). Expansions
/// shorter than the deep width ride their rarest 2-GRAM postings instead (a 2-byte expansion IS its own
/// 2-gram — exact; a 3..4-byte one a superset): short rules are the early-grammar regime, their true reach is
/// dense, and exactness outranks selectivity there. A gram absent from the pool proves NO span contains the
/// expansion — the whole rule drops from the gather (dream-minted rules mostly do).
public sealed class FrontierIndex
{
    public const int DeepGramLen = 5;                    // w — family-specific reach at code/word scale (ShingleN consonance)
    private readonly PackedSpanPostings _deep;           // w-grams, one posting per span — the selective rail
    private readonly PackedSpanPostings _bi;             // 2-grams, one posting per span — the exactness floor for short expansions
    private readonly int[] _stamp;                       // per-span visit epoch — alloc-free dedup across Draws
    private int _epoch;
    private readonly List<int> _candidateScratch = new();
    private readonly List<(int I, double Cov)> _topScratch = new();
    private int _zeroCursor;

    // the prepared basis — rarest-gram posting segment per expansion, keyed on the cover's expansion-array identity
    // (a cover is built once per stride; Draw runs per step — preparing per Draw would re-hash the basis ~6×).
    private byte[][]? _basisKey;
    private readonly List<(PackedSpanPostings Rail, int Seg)> _basisPosts = new();

    /// Spans gathered by the last Candidates call — the bench's candidate-set-size read.
    public int LastCandidateCount { get; private set; }

    /// The prepared basis decomposed (total accounting): how many expansions ride the deep rail vs the 2-gram
    /// fallback, and the posting mass a Draw walks. A big Short count with a huge PostMass names the ONE dense
    /// regime — short common rules in the basis make candidacy genuinely dense (every span containing "e "
    /// scores > 0), so the flat-wall win belongs to the DEEP-basis discipline (a capped cover whose top-N are
    /// long), not to this index alone.
    public (int Deep, int Short, long PostMass) BasisStats { get; private set; }

    public FrontierIndex(IReadOnlyList<byte[]> pool)
    {
        _deep = new PackedSpanPostings(DeepGramLen, pool);
        _bi   = new PackedSpanPostings(2, pool);
        _stamp = new int[pool.Count];
    }

    /// MemStat census read — both rails' key/posting masses (the pool-proportional resident cost). Counts only.
    public (long DeepKeys, long DeepPosts, long BiKeys, long BiPosts) Mass()
    {
        var (dk, dp) = _deep.Mass();
        var (bk, bp) = _bi.Mass();
        return (dk, dp, bk, bp);
    }

    /// The candidate spans for this cover's basis: per expansion the postings of its rarest gram, unioned,
    /// deduped (stamp epoch), un-ingested only. Size tracks the grammar's reach, never the pool.
    public List<int> Candidates(Engine.GrammarCover cover, bool[] ingested)
    {
        if (!ReferenceEquals(cover.Expansions, _basisKey)) PrepareBasis(cover.Expansions);
        _epoch++;
        var cands = _candidateScratch;
        cands.Clear();
        foreach (var (rail, seg) in _basisPosts)
            foreach (int i in rail.SpansOf(seg))
            {
                if (_stamp[i] == _epoch || ingested[i]) continue;
                _stamp[i] = _epoch;
                cands.Add(i);
            }
        LastCandidateCount = cands.Count;
        return cands;
    }

    /// Select the exact top batch without sorting the entire candidate set. The full comparator is preserved by
    /// maintaining a sorted top-k list, while the zero-coverage tail advances through one monotonic cursor because
    /// intake only changes false→true in `ingested`; no later call can make an earlier skipped span eligible again.
    public List<int> Pick(Engine.GrammarCover cover, IReadOnlyList<byte[]> pool, bool[] ingested, int count)
    {
        var picks = new List<int>(Math.Min(count, pool.Count));
        if (count <= 0) return picks;

        var top = _topScratch;
        top.Clear();
        foreach (int i in Candidates(cover, ingested))
        {
            double cov = cover.Coverage(pool[i]);
            if (cov <= 0) continue;
            var candidate = (i, cov);
            int insert = 0;
            while (insert < top.Count && Compare(top[insert], candidate) <= 0) insert++;
            if (insert >= count) continue;
            top.Insert(insert, candidate);
            if (top.Count > count) top.RemoveAt(count);
        }
        for (int i = 0; i < top.Count; i++) picks.Add(top[i].I);

        if (picks.Count < count)
        {
            // If there are fewer positives than requested, every positive was retained above. The remaining
            // full-scan winners are therefore the first un-ingested pool indices with zero coverage.
            for (int i = _zeroCursor; i < pool.Count && picks.Count < count; i++)
            {
                _zeroCursor = i + 1;
                if (!ingested[i]) picks.Add(i);
            }
        }
        return picks;
    }

    private static int Compare((int I, double Cov) a, (int I, double Cov) b)
        => a.Cov != b.Cov ? b.Cov.CompareTo(a.Cov) : a.I.CompareTo(b.I);

    private void PrepareBasis(byte[][] exps)
    {
        _basisKey = exps;
        _basisPosts.Clear();
        int deep = 0, shrt = 0; long mass = 0;
        var seen = new HashSet<long>();                                  // (rail bit << 32 | seg) — expansions sharing a rarest gram share the segment: walk it once. The bit keeps deep seg #k distinct from bi seg #k (the old reference-identity dedup could never cross rails).
        foreach (var e in exps)
        {
            bool isDeep = e.Length >= DeepGramLen;
            if (isDeep) deep++; else shrt++;
            var rail = isDeep ? _deep : _bi;
            int seg = RarestSeg(rail, e);
            if (seg >= 0 && seen.Add(((isDeep ? 0L : 1L) << 32) | (uint)seg))
            {
                _basisPosts.Add((rail, seg));
                mass += rail.CountOf(seg);
            }
        }
        BasisStats = (deep, shrt, mass);
    }

    // the tightest necessary-condition posting segment for `e` — its rarest gram's (strictly-smaller wins, first
    // gram wins ties — the full-scan comparator's mirror). −1 ⟺ some gram of e occurs in NO pool span ⟹ no span
    // contains e ⟹ the expansion contributes nothing.
    private static int RarestSeg(PackedSpanPostings rail, byte[] e)
    {
        int w = rail.GramLen, best = -1, bestCount = int.MaxValue;
        for (int off = 0; off + w <= e.Length; off++)
        {
            int seg = rail.SegOf(Simhash.Fnv64(e.AsSpan(off, w)));
            if (seg < 0) return -1;
            int c = rail.CountOf(seg);
            if (c < bestCount) { best = seg; bestCount = c; }
        }
        return best;
    }
}
