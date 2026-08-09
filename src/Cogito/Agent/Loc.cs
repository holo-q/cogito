namespace Cogito;

using System.Text;
using System.Text.Json;
using Cogito.Grammar;
using Cogito.Induct;   // RePairResult (the standing-grammar snapshot + the pretrain binary-prefix seed)

// ── LOC ──  the shared substrate of the localization-bench trio (nav --mode frozen|dyn|loop). The three modes
// (Navigate/NavDyn/NavLoop) are ONE bench read three ways: a SWE-bench-Lite instance stream (query.txt + sites.jsonl
// + gold.json), ranked by gret's NG-BM25 over the sites, driven by the same beacon→descend→induce→expand→verdict loop.
// This file holds everything that is byte-identical across the three: the Site atom, the BM25 ranking substrate, the
// aggregate-max site→file law, the field-margin reads, the JSONL/gold loaders, the shared loop-policy constants, the
// checkpoint helpers, the checkpoint-dialect sniff, and the standing grammar (the cross-instance vocabulary the dyn/
// loop streams accrete). The per-mode drivers keep only what DIVERGES (their field construction, expansion policy,
// result record, emit/curve/journal formatting, and their private learners — RankHead, CorroborationMind, cov-beacon).

/// One localization site — a module/class/function/method span of a repo file (FORMAT.md: worker A's frozen plane).
/// `Kind == "module"` is the one-per-file doc the file-level aggregate reads; function/method sites carry the fn field.
public sealed record Site(string Path, string Kind, string Name, int Start, int End, string Text);

/// The descend surface referenced a file with no module site — the signature of pointing a nav mode at the wrong data
/// (the `solve` synth fixtures instead of the real SWE-bench-Lite dir). Caught at the `nav` verb boundary → exit 1 with
/// the actionable message, instead of a raw KeyNotFoundException from inside the drive loop.
public sealed class DatasetMismatchException(string path)
    : Exception($"dataset mismatch: the descend surface references '{path}' but it has no module site — is this the right --data dir? " +
                "the nav modes need the real SWE-bench-Lite localization dir (query.txt + sites.jsonl + gold.json per instance), not the solve synth fixtures");

public static class Loc
{
    // ── the pre-registered loop policy, shared by all three modes (apples-comparable inner loop) ──
    internal const int    KFilesPerLook = 2;      // files descended per look
    internal const int    MaxLooks      = 6;      // the look budget (hard cap)
    internal const int    SPersist      = 2;      // local top-1 file unchanged this many consecutive looks → LAND
    internal const double TauMargin     = 0.25;   // (top1−top2)/top1 site-field concentration required to LAND
    internal const double TauPromote    = 0.15;   // margin a visited file needs to OUTRANK beacon order (rerank-regression guard)
    internal const double K1 = 1.5, Bb = 0.3;     // gret's BM25 law (parity with the engine-of-record ranker)
    internal const double WExpand       = 0.5;    // blended field: base + WExpand·mint (the mint may re-rank, never drown the base)
    internal const double VestMarginEps = 0.01;   // a minted term vests iff it doesn't dilute the field's top-1 margin by more than this
    internal const double TauFlat       = 0.05;   // margin below this = the field is FLAT (JUMP eligible)
    internal const double CovPlateauEps = 0.01;   // Δcoverage below this = the residual plateaued (JUMP's second condition)
    internal const int    MinTermLen    = 4;      // minted/learned terms shorter than this are stopword-shaped noise
    internal const int    StandingRuleCap = 24_000;  // the standing vocabulary's stream-scale budget (dyn/loop channel 1)

    // ── gret's NgramTokens law (lowercased a–z runs ≥3 + adjacent pairs), hand-scanned — BYTE-IDENTICAL tokens to the
    // `[a-z]{3,}` regex over ToLowerInvariant() but without a regex + a lowercased string COPY per site (over 18k–46k
    // site texts the regex machinery was the per-instance chug). Same tokens ⇒ same BM25 ⇒ same rank. ──
    [ThreadStatic] private static char[]? _tokScratch;   // run buffer reused across calls — Toks runs millions of times per instance; a StringBuilder per token was the alloc chug
    public static List<string> Toks(string text)
    {
        var w = new List<string>();
        var scratch = _tokScratch ??= new char[256];
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = char.ToLowerInvariant(text[i]);
            if (c < 'a' || c > 'z') { i++; continue; }
            int len = 0;
            while (i < n)
            {
                char lc = char.ToLowerInvariant(text[i]);
                if (lc < 'a' || lc > 'z') break;
                if (len == scratch.Length) { Array.Resize(ref scratch, scratch.Length * 2); _tokScratch = scratch; }
                scratch[len++] = lc; i++;
            }
            if (len >= 3) w.Add(new string(scratch, 0, len));
        }
        int uni = w.Count;                                            // bigrams appended in place — unigrams-then-bigrams order preserved
        for (int j = 0; j + 1 < uni; j++) w.Add(w[j] + " " + w[j + 1]);
        return w;
    }

    /// A TEST file by path shape: a `tests/` segment, `test_*.py`, `*_test.py`, `conftest.py`. Deliberately narrow —
    /// `django/test/testcases.py` (framework SOURCE) must NOT match, so the bare `test` segment doesn't.
    public static bool IsTest(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("test_", StringComparison.Ordinal) || name.EndsWith("_test.py", StringComparison.Ordinal) || name == "conftest.py") return true;
        foreach (var seg in path.Split('/')) if (seg == "tests") return true;
        return false;
    }

    // ── aggregate-max site→file ranking (aggregate.py's law: max site score per file, tie-break the file's best site
    // rank). This IS static's file ranking when fed the base scores; the nav loop descends it (re-fed the blended
    // scores as mints vest). Deterministic (stable score sort, path order). ──
    public static (List<string> Order, Dictionary<string, double> Score) AggregateMaxFiles(List<Site> sites, double[] siteScore)
    {
        var fileScore = new Dictionary<string, double>();
        var fileBestRank = new Dictionary<string, int>();
        var rankOrder = Enumerable.Range(0, sites.Count).OrderByDescending(i => siteScore[i]).ToList();   // stable → site-order tie-break
        for (int r = 0; r < rankOrder.Count; r++)
        {
            int i = rankOrder[r]; var p = sites[i].Path;
            if (!fileScore.TryGetValue(p, out var s) || siteScore[i] > s) fileScore[p] = siteScore[i];
            if (!fileBestRank.ContainsKey(p)) fileBestRank[p] = r;
        }
        var order = fileScore.Keys.OrderByDescending(p => fileScore[p]).ThenBy(p => fileBestRank[p]).ToList();
        return (order, fileScore);
    }

    // file-level aggregate-max of a score field, restricted to the visited set — the corroboration read's input.
    public static List<double> FileMax(List<Site> sites, double[] score, HashSet<string> visitedSet)
    {
        var best = new Dictionary<string, double>();
        for (int i = 0; i < sites.Count; i++)
        {
            if (!visitedSet.Contains(sites[i].Path)) continue;
            if (!best.TryGetValue(sites[i].Path, out var s) || score[i] > s) best[sites[i].Path] = score[i];
        }
        return best.Values.OrderByDescending(x => x).ToList();
    }

    /// The visited file field's top-1 MARGIN ((top1−top2)/top1) — the concentration currency the outcomeCredit gate reads
    /// (a corroborating term SHARPENS the leader; a diluting one flattens it). The loop's own LAND signal, reused.
    public static double FieldMargin(List<Site> sites, double[] score, HashSet<string> visitedSet)
    {
        var desc = FileMax(sites, score, visitedSet);
        return desc.Count > 1 && desc[0] > 0 ? (desc[0] - desc[1]) / desc[0] : 1.0;
    }

    // the loop's HEAD rank of the gold (nav's own visited/beacon ordering) — 0 = not in the head. Print-only.
    public static int HeadRank(List<string> order, HashSet<string> gold)
    { for (int i = 0; i < order.Count; i++) if (gold.Contains(order[i])) return i + 1; return 0; }

    // instance_id = "<repo>__<repo>-<num>" (FORMAT.md) — the repo key is everything before the first "__".
    public static string RepoOf(string instanceId)
    { int p = instanceId.IndexOf("__", StringComparison.Ordinal); return p < 0 ? instanceId : instanceId[..p]; }

    // JSON has no NaN/Infinity literal — a single non-finite score would corrupt the sweep line + the scorer's parse.
    // Emit null; the Python scorer reads a missing score as 0.
    public static string R(double x) => double.IsFinite(x) ? x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "null";
    public static string JsonStr(string s) => JsonSerializer.Serialize(s);

    /// The descend read: the module doc for a picked file path. A pick comes from the beacon order (over ALL sites),
    /// so on the real SWE-bench plane every picked path has a module doc (FORMAT.md guarantees one-per-file). The synth
    /// `solve` fixtures don't — their gold references files with no module site — so a bare `fileByPath[p]` throws a
    /// raw KeyNotFoundException deep in the loop. Turn that into the actionable dataset-mismatch verdict at the seam.
    public static Site DescendDoc(Dictionary<string, Site> fileByPath, string path)
        => fileByPath.TryGetValue(path, out var d) ? d
         : throw new DatasetMismatchException(path);

    // ── worker A's plane (world-boundary JSON — doctrine-legal) ──
    public static List<Site> LoadSites(string path)
    {
        var list = new List<Site>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            list.Add(new Site(r.GetProperty("path").GetString()!, r.GetProperty("kind").GetString()!,
                              r.GetProperty("name").GetString()!, r.GetProperty("start_line").GetInt32(),
                              r.GetProperty("end_line").GetInt32(), r.GetProperty("text").GetString()!));
        }
        return list;
    }

    public static (HashSet<string> Files, HashSet<(string, string)> Fns) LoadGold(string path)
    {
        var files = new HashSet<string>(); var fns = new HashSet<(string, string)>();
        if (!File.Exists(path)) return (files, fns);
        using var d = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var f in d.RootElement.GetProperty("files").EnumerateArray()) files.Add(f.GetString()!);
        if (d.RootElement.TryGetProperty("functions", out var fnArr))
            foreach (var f in fnArr.EnumerateArray())
                fns.Add((f.GetProperty("path").GetString()!, f.GetProperty("name").GetString()!));
        return (files, fns);
    }

    // ── the checkpoint-dialect sniff — a routed mesh writes CGRING (MeshCheckpoint), Cortex writes CGCTX1
    // (Checkpoint). The pretrain-seed loaders route on this: the mesh grammar is the deep corroborated vocabulary. ──
    public static bool IsMeshCheckpoint(string runDir)
    {
        using FileStream fs = File.OpenRead(Path.Combine(runDir, "checkpoint.bin"));
        byte[] m = new byte[7];
        return fs.Read(m, 0, 7) == 7 && m.AsSpan().SequenceEqual("CGRING\n"u8);
    }

    /// The pretrained grammar's PURE-BINARY PREFIX — the Loom.Seed contract. A trained grammar (Cortex CGCTX1 or mesh
    /// CGRING) carries campfire/breach/GC consolidation (n-ary templates, demoted TapeRef bodies) over the pure Re-Pair
    /// core; emission order guarantees a binary rule references only EARLIER symbols, so the first non-binary rule and
    /// everything after it is a suffix the seed can't rank-encode (re-derivable from the repo splices). Returns the raw
    /// grammar (for the caller's log) + the seed-able binary prefix.
    public static (RePairResult Raw, List<GrammarRule> BinPrefix) LoadBinaryPrefix(string runDir)
    {
        RePairResult raw = IsMeshCheckpoint(runDir) ? MeshCheckpoint.PeekGrammar(runDir) : Checkpoint.PeekGrammar(runDir);
        List<GrammarRule> bin = new();
        foreach (GrammarRule r in raw.Rules) { if (r.Kind == RuleBodyKind.Expansion && r.Pattern.Length == 2) bin.Add(r); else break; }
        return (raw, bin);
    }

    // ── the agent-stream checkpoint helpers (byte-identical across the modes' SaveCheckpoint/Resume) ──
    public static long Len(Run run, string file) => new FileInfo(run.PathOf(file)).Length;
    public static void WriteStrings(CkptWriter w, IEnumerable<string> xs) { var a = xs.ToArray(); w.I32(a.Length); foreach (var x in a) w.Str(x); }
    public static List<string> ReadStrings(CkptReader r) { int n = r.I32(); var xs = new List<string>(n); for (int i = 0; i < n; i++) xs.Add(r.Str()); return xs; }
    public static void WriteIntMap(CkptWriter w, Dictionary<string, int> m) { w.I32(m.Count); foreach (var (k, v) in m.OrderBy(x => x.Key, StringComparer.Ordinal)) { w.Str(k); w.I32(v); } }
    public static Dictionary<string, int> ReadIntMap(CkptReader r) { int n = r.I32(); var m = new Dictionary<string, int>(StringComparer.Ordinal); for (int i = 0; i < n; i++) m[r.Str()] = r.I32(); return m; }
    public static void WriteStringDoubleMap(CkptWriter w, Dictionary<string, double> m) { w.I32(m.Count); foreach (var (k, v) in m.OrderBy(x => x.Key, StringComparer.Ordinal)) { w.Str(k); w.F64(v); } }
    public static Dictionary<string, double> ReadStringDoubleMap(CkptReader r) { int n = r.I32(); var m = new Dictionary<string, double>(StringComparer.Ordinal); for (int i = 0; i < n; i++) m[r.Str()] = r.F64(); return m; }
    public static void WriteRules(CkptWriter w, List<GrammarRule> rules) => Checkpoint.WriteGrammar(w, new RePairResult(rules.ToArray(), [], new Mbits(0), 256));
    public static List<GrammarRule> ReadRules(CkptReader r) => Checkpoint.ReadGrammar(r).Rules.ToList();
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  BM25 INDEX — gret's NG-BM25 law (k1=1.5 b=0.3, lowercased a–z runs ≥3 + adjacent pairs, Distinct query) over
//  postings built ONCE per instance. The nav loop scores the growing minted query per look, so the postings are
//  hoisted — same tokens ⇒ same BM25 ⇒ same rank.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class Bm25Index
{
    private readonly List<Dictionary<string, int>> _tf;
    private readonly Dictionary<string, List<(int Doc, int Tf)>> _post = new(StringComparer.Ordinal);   // inverted postings, doc-ascending per term — Score walks Σdf instead of terms × ALL docs
    private readonly Dictionary<string, int> _fileDf = new(StringComparer.Ordinal);   // df over MODULE docs only — the minted-term rarity read (site-level df triple-counts a file's class/method nesting)
    private readonly double[] _dl;
    private readonly double _avgdl;
    private readonly int _n;

    public Bm25Index(List<string> docs)
    {
        _n = docs.Count;
        _tf = new List<Dictionary<string, int>>(_n);
        _dl = new double[_n];
        foreach (var d in docs)
        {
            var c = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in Loc.Toks(d)) c[t] = c.GetValueOrDefault(t) + 1;
            _tf.Add(c);
        }
        for (int j = 0; j < _n; j++)
        {
            foreach (var (t, c) in _tf[j]) (_post.TryGetValue(t, out var p) ? p : _post[t] = new()).Add((j, c));
            _dl[j] = _tf[j].Values.Sum();
        }
        _avgdl = _n == 0 ? 1 : Math.Max(1, _dl.Average());
    }

    /// The doc's term counts (the hoisted tokenization) — RankHead.Learn reads these instead of re-tokenizing every site.
    public Dictionary<string, int> Tf(int doc) => _tf[doc];

    /// Register which doc indices are per-file module docs — FileDf then reads file-level rarity.
    public void IndexModules(IEnumerable<int> moduleDocIdx)
    {
        foreach (var j in moduleDocIdx)
            foreach (var t in _tf[j].Keys) _fileDf[t] = _fileDf.GetValueOrDefault(t) + 1;
    }

    public int FileDf(string term) => _fileDf.GetValueOrDefault(term);

    /// Score every doc for a term list (already distinct) — gret's BM25 verbatim over the inverted postings. The
    /// per-term walk is its posting list (doc-ascending, the same accumulation order as the dense scan — bit-identical).
    public double[] Score(List<string> terms)
    {
        var scores = new double[_n];
        foreach (var t in terms)
        {
            if (!_post.TryGetValue(t, out var post)) continue;
            int df = post.Count;
            double idf = Math.Log(1 + (_n - df + 0.5) / (df + 0.5));
            foreach (var (j, c) in post)
                scores[j] += idf * (c * (Loc.K1 + 1)) / (c + Loc.K1 * (1 - Loc.Bb + Loc.Bb * _dl[j] / _avgdl));
        }
        return scores;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  STANDING GRAMMAR — CHANNEL 1 of the dyn/loop streams: the persistent vocabulary that accretes across the stream.
//  Holds a growing set of PURE BINARY Re-Pair rules (Loom.Seed's contract), deduped by pattern. Each solved instance
//  contributes its novel rules; the next instance's grok seeds from the current snapshot. This is the mind that KNOWS
//  the structure it has already seen — the frozen mode's per-instance dispose throws exactly this away.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class StandingGrammar
{
    private readonly List<GrammarRule> _rules;                        // emission-ordered pure-binary rules (Seed contract) — id = 256 + index
    private readonly int _cap;
    private readonly List<GrammarRule> _seed;                         // the pretrain seed — the frozen snapshot returned when carry is off
    private readonly Dictionary<long, int> _localOf = new();           // LOCAL-id digram → standing rule id — the cross-instance structural dedup

    public StandingGrammar(List<GrammarRule> seed, int cap)
    {
        _cap = cap;
        _seed = seed;
        _rules = new List<GrammarRule>(seed);
        // prime the structural dedup with the SEED's own digrams (in local-id space — the seed rules already carry
        // correct emission-ordered ids), so a harvested idiom matching a seed idiom dedups instead of re-minting.
        for (int i = 0; i < seed.Count; i++)
        {
            var p = seed[i].Pattern;
            if (p.Length == 2) _localOf[((long)p[0].Value << 32) | p[1].Value] = 256 + i;
        }
    }

    public int Count => _rules.Count;

    // A snapshot for one instance's induction seed. carry=true → the CURRENT accreted grammar (the lifelong
    // vocabulary); carry=false → the FROZEN seed (the fixed pretrain — the channel-1 ablation).
    public RePairResult Snapshot(bool carry)
    {
        var rules = carry ? _rules : _seed;
        return new RePairResult(rules.ToArray(), [], new Mbits(0), 256);
    }

    // ABSORB an instance's harvested binary rules — a full DAG IMPORT with id remapping. `harvested` is emission-
    // ordered: foreign rule at index j has implicit id (256+j) and references only EARLIER foreign ids, so a rule's
    // children resolve recursively WITHIN the array. Each foreign rule is keyed by its LOCAL-id digram (children
    // remapped to the standing grammar's ids) — a structurally-identical idiom that recurred in an earlier instance
    // DEDUPS (the same span-shape lands the same standing id no matter which instance grokked it), while a genuinely-
    // novel idiom mints at the tail in emission order (children-before-parent, the Seed contract). This is what makes
    // channel 1 real: the DEEP repo idioms accrete, not just shallow bigrams. Past the cap the vocabulary crystallizes.
    public void Absorb(GrammarRule[] harvested)
    {
        if (harvested.Length == 0 || _rules.Count >= _cap) return;
        var foreignToLocal = new int[harvested.Length];               // foreign index j → local id (256+k), or -1 unresolved
        Array.Fill(foreignToLocal, -1);
        for (int j = 0; j < harvested.Length; j++)
        {
            if (_rules.Count >= _cap) break;
            var r = harvested[j];
            if (r.Kind != RuleBodyKind.Expansion || r.Pattern.Length != 2) break;   // binary prefix only (HarvestBinary guarantees it, but stop at the first non-binary)
            // resolve both children to LOCAL ids. A terminal maps to itself; a foreign nonterminal (256+c) maps to the
            // local id we assigned when we imported foreign rule c earlier in THIS pass (guaranteed earlier by emission
            // order). If a child is unresolved (its rule was dropped at the cap), this rule is unimportable.
            int la = ResolveChild((int)r.Pattern[0].Value, foreignToLocal);
            int lb = ResolveChild((int)r.Pattern[1].Value, foreignToLocal);
            if (la < 0 || lb < 0) continue;
            long key = ((long)la << 32) | (uint)lb;                    // the LOCAL-id digram — the cross-instance dedup key
            if (_localOf.TryGetValue(key, out int existing)) { foreignToLocal[j] = existing; continue; }
            var pat = new Symbol[] { new((uint)la), new((uint)lb) };
            int localId = 256 + _rules.Count;
            _rules.Add(new GrammarRule(GrammarRule.ComputeId(pat), pat, new Mbits(256 + 8000L * 16)));
            _localOf[key] = localId;
            foreignToLocal[j] = localId;
        }
    }

    // resolve a harvested rule's child id to a LOCAL id: a terminal (<256) is itself; a foreign nonterminal (256+c) is
    // the local id assigned to foreign rule c (or -1 if that rule wasn't imported — dropped at the cap).
    private int ResolveChild(int foreignId, int[] foreignToLocal)
    {
        if (foreignId < 256) return foreignId;                        // terminal — build-invariant across grammars
        int c = foreignId - 256;
        return c < foreignToLocal.Length ? foreignToLocal[c] : -1;
    }

    public void Save(CkptWriter w)
    {
        Loc.WriteRules(w, _seed);
        Loc.WriteRules(w, _rules);
    }

    public void Load(CkptReader r)
    {
        _seed.Clear(); _seed.AddRange(Loc.ReadRules(r));
        _rules.Clear(); _rules.AddRange(Loc.ReadRules(r));
        _localOf.Clear();
        for (int i = 0; i < _rules.Count; i++)
        {
            var p = _rules[i].Pattern;
            if (p.Length == 2) _localOf[((long)p[0].Value << 32) | p[1].Value] = 256 + i;
        }
    }
}
