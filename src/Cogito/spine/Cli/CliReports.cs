namespace Cogito;

using System.Linq;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── CLI REPORT HELPERS ──  the shared formatting + grammar-read + verify-suite primitives the cogito CLI
// verb bodies lean on, minted ONCE here so the kernel cluster (grammar/interp/renorm/verify…) and the rag
// cluster (bench/distill/rl…) draw from the SAME implementations instead of a god-partial's private pile.
// Pure functions over the induced grammar + text: no state, no I/O beyond LoadCorpus's read. The two homes
// they serve reference them via `using static Cogito.CliReports;`, so a body reads `Expand(r, i)` /
// `Truncate(s, 40)` exactly as it did when these lived beside it in the old Cli partial.
internal static class CliReports
{
    // ── the builtin sample + corpus loader (the argv shape the string[] bodies parse) ──

    /// LoadCorpus's contract off an argv: the file's bytes when args[1] exists, else the builtin sample.
    /// The string[] verb bodies (export/couplings/know/fix/rl/kill/verify) share this exact scrape.
    internal static byte[] LoadCorpus(string[] args)
        => args.Length > 1 && File.Exists(args[1]) ? File.ReadAllBytes(args[1]) : Encoding.UTF8.GetBytes(Builtin);

    internal const string Builtin =
        "def add(a, b): return a + b\ndef add(a, b): return a + b\ndef add(a, b): return a + b\n" +
        "for i in range(10): print(i)\nfor i in range(10): print(i)\n" +
        "the quick brown fox jumps over the lazy dog\nthe quick brown fox jumps over the lazy dog\n";

    // ── grammar reads ──

    /// References to each nonterminal N=256+i across the compressed start sequence + every rule's RHS.
    internal static int[] RuleUses(RePairResult r) => Engine.RuleUses(r);

    internal static byte[] Expand(RePairResult r, int ruleIndex)
        => Reconstruct.Expand(r.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)ruleIndex)]);

    /// Least-squares slope of log(freq) vs log(rank) over every symbol reference. ≈ −1 ⟹ a language;
    /// flat (→ 0) ⟹ a lookup hoard. Floats are fine here — introspection never touches consensus state.
    internal static double ZipfSlope(RePairResult r)
    {
        var freq = new Dictionary<uint, int>();
        void T(Symbol s) => freq[s.Value] = freq.GetValueOrDefault(s.Value) + 1;
        foreach (var s in r.Compressed) T(s);
        foreach (var rule in r.Rules) foreach (var s in rule.Pattern) T(s);

        var f = freq.Values.OrderByDescending(x => x).ToList();
        int k = f.Count;
        if (k < 2) return double.NaN;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < k; i++)
        {
            double x = Math.Log(i + 1), y = Math.Log(f[i]);
            sx += x; sy += y; sxx += x * x; sxy += x * y;
        }
        return (k * sxy - sx * sy) / (k * sxx - sx * sx);
    }

    /// Zipf slope of an explicit frequency list (log freq vs log rank) — per-scale criticality for renorm.
    internal static double ZipfOf(IEnumerable<int> freqs) => Engine.ZipfOf(freqs);

    internal static string Health(double zipf) =>
        double.IsNaN(zipf)              ? "n/a"
        : Math.Abs(zipf + 1) < 0.3      ? "language-like (≈ -1)"        // slope ∈ [-1.3, -0.7]
        : zipf > -0.7                   ? "flat — small/repetitive corpus or hoarding"
        :                                 "steep — over-merged?";        // slope < -1.3

    /// Renorm summary for the grok curve — the shared engine read (Engine.RenormStats).
    internal static (int scales, double meanZ, double cvZ, double maxSpan) RenormStats(RePairResult r)
    {
        var s = Engine.RenormStats(r);
        return (s.Scales, s.MeanZ, s.CvZ, s.MaxSpan);
    }

    /// The criticality exponent ± its scale-CV, formatted for a headline line (n/a when a grammar is too shallow
    /// to populate ≥1 scale with an exponent). Shared by the grok/renorm/thoughtstream reads.
    internal static string ExpFmt(double meanZ, double cvZ)
        => double.IsNaN(meanZ) ? "n/a" : $"{meanZ:F2}±{(double.IsNaN(cvZ) ? "n/a" : cvZ.ToString("P0"))}";

    /// Least-squares slope over an (x,y) point list — the Heaps β fit + any log-log regression.
    internal static double Slope(List<(double X, double Y)> pts)
    {
        int k = pts.Count;
        if (k < 2) return double.NaN;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var (x, y) in pts) { sx += x; sy += y; sxx += x * x; sxy += x * y; }
        return (k * sxy - sx * sy) / (k * sxx - sx * sx);
    }

    // ── text rendering ──

    /// A chunk's expansion → a compact one-line graph label (real newlines/tabs shown inline as \n/\t, capped ~46
    /// like the Python `surface`): the code fragment the node IS, legible on a hub without breaking the layout.
    internal static string Label(byte[] expansion)
    {
        var s = Encoding.UTF8.GetString(expansion).Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
        return s.Length <= 46 ? s : s[..44] + "…";
    }

    /// minimal JSON string escaper (world boundary; hand-rolled like Export's numeric emit): quotes, backslashes,
    /// and control chars — enough to make arbitrary code-chunk text a valid JSON string value.
    internal static string JsonStr(string s)
    {
        var b = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
            switch (ch)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default: if (ch < 0x20) b.Append("\\u").Append(((int)ch).ToString("x4")); else b.Append(ch); break;
            }
        return b.ToString();
    }

    internal static string ShowRaw(ReadOnlySpan<byte> b) => Encoding.UTF8.GetString(b).ReplaceLineEndings("\\n");

    internal static string Show(ReadOnlySpan<byte> bytes, int max = 48)
    {
        var sb = new StringBuilder("\"");
        int lim = Math.Min(bytes.Length, max);
        for (int i = 0; i < lim; i++)
        {
            byte b = bytes[i];
            sb.Append(b switch
            {
                (byte)'\n' => "\\n",
                (byte)'\t' => "\\t",
                (byte)'\r' => "\\r",
                < 32 or 127 => $"\\x{b:x2}",
                _ => ((char)b).ToString(),
            });
        }
        sb.Append('"');
        if (bytes.Length > max) sb.Append($"…+{bytes.Length - max}B");
        return sb.ToString();
    }

    internal static string Truncate(string s, int max)
    {
        s = s.ReplaceLineEndings("\\n");
        return s.Length <= max ? s : s[..max] + $"…+{s.Length - max}";
    }

    internal static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        var slug = sb.ToString().Trim('-');
        return slug.Length <= 40 ? slug : slug[..40];
    }

    /// Readable join of selected rule expansions — for embedding cogito's grammar state into a prompt.
    internal static string Join(RePairResult r, int[] uses, IEnumerable<int> idx)
        => string.Join(", ", idx.Select(i => $"\"{ShowRaw(Expand(r, i))}\""));

    /// cogito's mutation, rendered as observable bytes — the top learned rule expansions. Re-entering
    /// these into the corpus is the autoregressive wire: cogito observes its own structure (the genesis idea).
    internal static byte[] SelfText(RePairResult g)
    {
        var u = RuleUses(g);
        var sb = new StringBuilder();
        foreach (var i in Enumerable.Range(0, g.Rules.Length).OrderByDescending(i => u[i]).Take(20))
            sb.Append(Encoding.UTF8.GetString(Expand(g, i))).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── string / byte distances ──

    internal static int Lcp(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    internal static int Lev(string a, string b)                              // Levenshtein edit distance (for fix's nearest-concept)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    // ── retrieval units + siblings (the rag cluster's shared matching primitives) ──

    /// The 1+2-grams of a text (lowercased words ≥3 chars + adjacent pairs) — the retrieval matching unit.
    internal static List<string> NgramTokens(string text)
    {
        var w = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), "[a-z]{3,}").Select(m => m.Value).ToList();
        var g = new List<string>(w);
        for (int i = 0; i + 1 < w.Count; i++) g.Add(w[i] + " " + w[i + 1]);
        return g;
    }
    internal static HashSet<string> Ngrams(string text) => new(NgramTokens(text));

    // Per-passage nearest SIBLINGS by IDF-weighted n-gram cosine — the confusable set the lexical retriever sees
    // (rare shared n-grams dominate, so siblings are the methods a query can't tell apart). Drives contrastive distill.
    internal static int[][] ComputeSiblings(List<string> passages, int k)
    {
        int n = passages.Count;
        var tf = passages.Select(p => { var c = new Dictionary<string, int>(); foreach (var g in NgramTokens(p)) c[g] = c.GetValueOrDefault(g) + 1; return c; }).ToList();
        var df = new Dictionary<string, int>();
        foreach (var c in tf) foreach (var g in c.Keys) df[g] = df.GetValueOrDefault(g) + 1;
        double Idf(string g) => Math.Log(1 + (n - df[g] + 0.5) / (df[g] + 0.5));
        var vec = tf.Select(c => c.ToDictionary(kv => kv.Key, kv => kv.Value * Idf(kv.Key))).ToList();
        var norm = vec.Select(v => Math.Sqrt(v.Values.Sum(x => x * x)) + 1e-9).ToArray();
        var res = new int[n][];
        for (int i = 0; i < n; i++)
        {
            var scores = new List<(int J, double S)>(n);
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                var (a, b) = vec[i].Count <= vec[j].Count ? (vec[i], vec[j]) : (vec[j], vec[i]);
                double dot = 0; foreach (var kv in a) if (b.TryGetValue(kv.Key, out var w)) dot += kv.Value * w;
                scores.Add((j, dot / (norm[i] * norm[j])));
            }
            res[i] = scores.OrderByDescending(x => x.S).Take(k).Select(x => x.J).ToArray();
        }
        return res;
    }

    /// Batch-score candidate generations with codex (one call), parsing the leading integers in order.
    internal static double[] ScoreBatch(CodexLlm codex, string criterion, List<byte[]> cands)
    {
        var sb = new StringBuilder();
        sb.Append($"Score each of the {cands.Count} samples below from 0 to 10 on this criterion: {criterion}.\n");
        sb.Append($"Output ONLY {cands.Count} integers separated by spaces, in order — nothing else.\n\n");
        for (int i = 0; i < cands.Count; i++)
        {
            var t = Encoding.UTF8.GetString(cands[i]);
            sb.Append($"[sample {i + 1}]\n{(t.Length > 500 ? t[..500] : t)}\n\n");
        }
        var resp = codex.Complete("You are a strict grader. Output only the integer scores, in order.", sb.ToString());
        var nums = System.Text.RegularExpressions.Regex.Matches(resp, @"\d+");
        var scores = new double[cands.Count];
        for (int i = 0; i < cands.Count; i++) scores[i] = i < nums.Count ? Math.Clamp(int.Parse(nums[i].Value), 0, 10) : 0;
        return scores;
    }

    // ── the verify-suite primitives (kernel cluster: verify-induct / verify-loom) ──

    /// The induction danger-zone suite — self-overlap runs (where incremental counts drift), random small-alphabet
    /// corpora (dense merges), random LINE corpora + barrier-run edges (the '\n' law's danger zone), plus the real
    /// corpus when one is passed. Shared by verify-induct and verify-loom so both gates walk the same minefield.
    internal static List<(string Name, byte[] Data)> InductCases(string[] args)
    {
        var cases = new List<(string Name, byte[] Data)>
        {
            ("empty", []), ("a", "a"u8.ToArray()), ("aa", "aa"u8.ToArray()), ("aaa", "aaa"u8.ToArray()),
            ("aaaa", "aaaa"u8.ToArray()), ("aaaaa", "aaaaa"u8.ToArray()), ("aaaaaa", "aaaaaa"u8.ToArray()),
            ("abab", "abababab"u8.ToArray()), ("runs", "aaabbbaaabbbaaabbb"u8.ToArray()),
            ("mixed", "aXaXaYaXaXaY"u8.ToArray()), ("nested", "abcabcabcabc"u8.ToArray()),
            ("tail", "baaaaab"u8.ToArray()), ("front", "aaaaabc"u8.ToArray()),
        };
        var rng = new Random(7);
        for (int k = 0; k < 6; k++)                                       // random small-alphabet → dense merges + overlaps
        {
            var d = new byte[400 + rng.Next(3000)];
            for (int i = 0; i < d.Length; i++) d[i] = (byte)('a' + rng.Next(k < 3 ? 3 : 6));
            cases.Add(($"rand{k}", d));
        }
        for (int k = 0; k < 3; k++)                                       // random LINE corpora — the barrier's danger zone
        {
            var d = new byte[400 + rng.Next(3000)];
            for (int i = 0; i < d.Length; i++) d[i] = rng.Next(9) == 0 ? (byte)'\n' : (byte)('a' + rng.Next(4));
            cases.Add(($"lines{k}", d));
        }
        cases.Add(("edges", "\n\nab\nab\nab\n\n"u8.ToArray()));           // barrier runs + boundary-adjacent repeats
        if (args.Length > 1 && File.Exists(args[1])) cases.Add(("corpus", LoadCorpus(args)));
        return cases;
    }

    // byte-identical iff same rule sequence (id bytes + pattern), same compressed tape, same savings.
    internal static bool SameGrammar(Cogito.Induct.RePairResult x, Cogito.Induct.RePairResult y)
    {
        if (x.Rules.Length != y.Rules.Length || x.Compressed.Length != y.Compressed.Length || x.TotalSavings.Value != y.TotalSavings.Value) return false;
        for (int i = 0; i < x.Rules.Length; i++)
        {
            if (!x.Rules[i].Id.Equals(y.Rules[i].Id)) return false;
            if (x.Rules[i].Pattern.Length != y.Rules[i].Pattern.Length) return false;
            for (int j = 0; j < x.Rules[i].Pattern.Length; j++)
                if (!x.Rules[i].Pattern[j].Equals(y.Rules[i].Pattern[j])) return false;
        }
        for (int i = 0; i < x.Compressed.Length; i++) if (!x.Compressed[i].Equals(y.Compressed[i])) return false;
        return true;
    }

    /// Total description length in mbits — L(G) (header + Σ rule costs) + the compressed tape priced at
    /// log₂|V| per symbol. The one comparable number for the incremental-vs-batch MDL gap: both grammars
    /// explain the SAME reference, so the cheaper description wins and the gap is the greedy-in-arrival tax.
    internal static long DescriptionLength(in Cogito.Induct.RePairResult r)
    {
        long dl = 1024;
        foreach (var ru in r.Rules) dl += ru.Cost.Value;
        dl += (long)r.Compressed.Length * Cogito.Codec.Fixed.Log2(r.AlphabetSize + (uint)r.Rules.Length).Value;
        return dl;
    }
}
