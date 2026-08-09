namespace Cogito;

using System.Text;
using System.Text.RegularExpressions;

// ── THE MEANING SUBSTRATE ──  the accumulated meaning model over a stream of text responses, and the fair
// COVERAGE read that makes a drive's residual SEMANTIC instead of SURFACE. A byte/word grammar's coverage
// saturates on a finite VOICE ("the cup was empty" ≈ "the theorem was false" once the phrasings are seen);
// this measures whether the IDEAS saturate, at three escalating levels, against everything modeled so far:
//
//   L1 lexical    — fraction of the response's CONTENT concepts (voice stripped, stemmed) already seen.
//   L2 relational — fraction of its CO-ACTIVATION relations (content-concept pairs within REL_DIST) already seen.
//   L3 class-rel  — fraction of its relations covered by a seen CLASS-relation, where classes are PARADIGM
//                   CLASSES induced from the co-activation graph by the distributional hypothesis (concepts
//                   sharing neighbourhoods are synonyms). L3 GENERALIZES: a never-seen pair (prince, scepter) is
//                   covered once (royalty-class, regalia-class) co-activated — the lift a word n-gram cannot make.
//
// The headline residual is 1 − L3 (the meaning not yet reconstructible). Meaning is read purely off CO-ACTIVATION
// STRUCTURE in the text — no embeddings, no LLM — the same black-box discipline as the surface grammar.
//
// THE VOW (determinism): every decision near a merge, argmax, or set-membership is order-invariant. Union-find
// fuses by MIN-ROOT so a class's root is its lexicographically smallest member (canonical, order-free); paradigm
// induction collects all Jaccard-passing edges then unions the SORTED set (a spanning forest — merge count and
// partition are independent of enumeration order); the stemmer is fully deterministic. Same input ⇒ same output.
//
// This is the MEANING / class-structure organ (co-activation → paradigm classes → class-relations → L1/L2/L3).
// It is NOT the PPMI couplings graph (Couplings.cs) — a separate organ over the compressed CHUNK stream.

public sealed partial class SemGrammar
{
    // co-activation edge = a content-concept pair within this many concepts of each other.
    public const int RelDist = 3;
    // skeleton overlap (LCS / |skeleton|) at which a clause counts as an instance of a known frame.
    public const double ThetaFrame = 0.5;
    // distributional-neighbour Jaccard at which two concepts fuse into one paradigm class.
    public const double SynJaccard = 0.30;
    // a concept needs at least this many co-activation neighbours to be clusterable at all.
    public const int SynMinNb = 2;

    private readonly Dictionary<string, int> _conceptCount = new();      // concept → times folded
    private readonly Dictionary<Pair, int> _relCount = new();            // raw co-activation graph (edge → count)
    private readonly Dictionary<string, HashSet<string>> _neighbours = new();  // concept → co-activation neighbours
    private Dictionary<string, string> _parent = new();                 // union-find → paradigm classes (rebuilt each fold)
    private HashSet<Pair> _classRel = new();                            // seen CLASS-level relations (the L3 model)
    private readonly List<Template> _templates = new();                 // anti-unified frame templates (frontier seeds)
    private readonly HashSet<string> _seenConcepts = new();             // the L1 lexical model

    public int TemplateCount => _templates.Count;
    public IReadOnlyList<Template> Templates => _templates;

    // ── voice-stripping: tokenize → drop stopwords → stem → keep content concepts (len ≥ 3) ─────────────────────

    /// A response's CONTENT concepts, in text order: letter-runs that survive the stopword filter, stemmed, kept
    /// when the stem is ≥ 3 chars and not itself a stopword. Duplicates are kept (co-activation counts them).
    public static List<string> Concepts(string text)
    {
        var outp = new List<string>();
        foreach (Match m in WordRegex().Matches(text))
        {
            var w = m.Value;
            if (StopWords.Contains(w.ToLowerInvariant())) continue;
            var s = Stem(w);
            if (s.Length >= 3 && !StopWords.Contains(s)) outp.Add(s);
        }
        return outp;
    }

    /// Split a response on punctuation into clauses; each kept clause carries its raw text + its ≥2 concepts.
    /// A clause under two concepts carries no relation, so it is dropped (nothing to measure or model).
    public static List<Clause> Clauses(string text)
    {
        var outp = new List<Clause>();
        foreach (var raw0 in ClauseRegex().Split(text))
        {
            var raw = raw0.Trim();
            if (raw.Length == 0) continue;
            var cs = Concepts(raw);
            if (cs.Count >= 2) outp.Add(new Clause(raw, cs));
        }
        return outp;
    }

    /// Light deterministic stemmer — collapse inflection so compress/compresses/compressing/compression converge
    /// on ONE concept, so the SAME word cannot fragment into many and inflate novelty. Not a true lemmatizer (the
    /// distributional clustering catches the rest); the suffix table is ordered longest/most-specific first, and
    /// the fall-through order (ly then es then s) is load-bearing — the `ly` strip feeds the plural checks below it.
    public static string Stem(string word)
    {
        var w = word.ToLowerInvariant();
        if (w.Length <= 3) return w;
        foreach (var (suf, rep) in StemSuffixes)
            if (w.Length - suf.Length >= 3 && w.EndsWith(suf, Ord))
                return DeDouble(w[..^suf.Length] + rep);
        // verb / plural inflection
        if (w.EndsWith("ies", Ord) && w.Length > 4) return w[..^3] + "y";
        if (w.EndsWith("ing", Ord) && w.Length > 5) return RestoreE(DeDouble(w[..^3]));
        if (w.EndsWith("ed", Ord) && w.Length > 4) return RestoreE(DeDouble(w[..^2]));
        if (w.EndsWith("ly", Ord) && w.Length > 4) w = w[..^2];                              // fall through to the plural checks
        if (w.EndsWith("es", Ord) && w.Length > 4 && w[^3] is 's' or 'x' or 'z') return w[..^2];
        if (w.EndsWith("s", Ord) && !(w.EndsWith("ss", Ord) || w.EndsWith("us", Ord) || w.EndsWith("is", Ord)) && w.Length > 3)
            w = w[..^1];
        return RestoreE(w);
    }

    /// beginn→begin, runn→run (Porter step): drop a final doubled consonant (not l/s/z).
    private static string DeDouble(string w)
        => w.Length > 3 && w[^1] == w[^2] && w[^1] is not ('l' or 's' or 'z') && !IsVowel(w[^1])
            ? w[..^1] : w;

    /// preserv→preserve, compos→compose: a stem ending consonant·single-vowel·consonant often wants a trailing
    /// 'e' so that preserve/preserves/preserving converge on one form. Heuristic, deterministic.
    private static string RestoreE(string w)
        => w.Length >= 4 && !IsVowel(w[^1]) && IsVowel(w[^2]) && !IsVowel(w[^3]) && w[^1] is not ('w' or 'x' or 'y')
            ? w + "e" : w;

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    // ── the co-activation pairs of a clause: every content-concept pair within REL_DIST, self-pairs dropped ─────
    // Duplicates within a clause are kept (they inflate the relation count but count as ONE new relation on fold).
    private static List<Pair> Pairs(List<string> cs)
    {
        var outp = new List<Pair>();
        for (int i = 0; i < cs.Count; i++)
            for (int j = i + 1; j < Math.Min(i + 1 + RelDist, cs.Count); j++)
                if (cs[i] != cs[j]) outp.Add(Pair.Of(cs[i], cs[j]));
        return outp;
    }

    // ── union-find over concepts → paradigm classes ─────────────────────────────────────────────────────────────

    private string Find(string x)
    {
        if (!_parent.ContainsKey(x)) _parent[x] = x;                    // setdefault: an unseen concept is its own class
        var r = x;
        while (_parent[r] != r) r = _parent[r];
        while (_parent[x] != r) { var nx = _parent[x]; _parent[x] = r; x = nx; }   // path compression
        return r;
    }

    /// Fuse two concepts' classes; MIN-ROOT wins so a class root is its lexicographically smallest member —
    /// canonical regardless of union order (the Vow). Returns true iff the two were distinct classes.
    private bool Union(string x, string y)
    {
        var rx = Find(x);
        var ry = Find(y);
        if (rx == ry) return false;
        if (string.CompareOrdinal(rx, ry) <= 0) _parent[ry] = rx; else _parent[rx] = ry;
        return true;
    }

    /// The number of distinct paradigm classes currently modeled (roots over every concept in the union-find).
    public int ParadigmClasses()
    {
        var roots = new HashSet<string>();
        foreach (var c in _parent.Keys.ToList()) roots.Add(Find(c));
        return roots.Count;
    }

    /// The synonymy predicate the frame-alignment LCS uses: two concepts are the same skeleton slot iff identical
    /// or in the same paradigm class as of the LAST recompute (template folding runs on the pre-recompute classes).
    private bool Syn(string x, string y)
        => x == y || (_parent.ContainsKey(x) && _parent.ContainsKey(y) && Find(x) == Find(y));

    // ── distributional paradigm-class induction (co-activation neighbourhoods → synonym clusters) ────────────────

    /// Fuse concepts with SIMILAR co-activation neighbourhoods (the distributional hypothesis: words sharing
    /// contexts are synonyms). Rebuilt FROM SCRATCH each fold — union-find can't un-merge, so a fresh pass every
    /// fold lets a new edge tighten synonymy without a monotonic collapse to one class. Candidates are concept
    /// pairs sharing ≥1 neighbour, both with ≥ SynMinNb neighbours; Jaccard is on the RAW neighbourhoods (union-
    /// state-independent), so collecting all passing edges and unioning the SORTED set yields the same partition
    /// and merge count on every run. Returns the merges applied.
    public int RecomputeClasses()
    {
        _parent = new Dictionary<string, string>();

        var clusterable = _neighbours
            .Where(kv => kv.Value.Count >= SynMinNb)
            .Select(kv => kv.Key)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        // inverted index: neighbour → the clusterables that co-activate with it (any two in a bucket are candidates).
        var byNb = new Dictionary<string, List<string>>();
        foreach (var c in clusterable)
            foreach (var n in _neighbours[c])
                (byNb.TryGetValue(n, out var lst) ? lst : byNb[n] = new List<string>()).Add(c);

        var candidates = new HashSet<Pair>();
        foreach (var shared in byNb.Values)
            for (int i = 0; i < shared.Count; i++)
                for (int j = i + 1; j < shared.Count; j++)
                    candidates.Add(Pair.Of(shared[i], shared[j]));

        var passing = new List<Pair>();
        foreach (var p in candidates)
        {
            var nbA = new HashSet<string>(_neighbours[p.A]); nbA.Remove(p.B);
            var nbB = new HashSet<string>(_neighbours[p.B]); nbB.Remove(p.A);
            if (nbA.Count == 0 || nbB.Count == 0) continue;
            int inter = nbA.Count(x => nbB.Contains(x));
            if (inter == 0) continue;
            double jac = (double)inter / (nbA.Count + nbB.Count - inter);
            if (jac >= SynJaccard) passing.Add(p);
        }
        passing.Sort((x, y) => { int c = string.CompareOrdinal(x.A, y.A); return c != 0 ? c : string.CompareOrdinal(x.B, y.B); });

        int merges = 0;
        foreach (var p in passing) if (Union(p.A, p.B)) merges++;
        return merges;
    }

    /// The CLASS-relation of a concept pair — its two class roots, sorted, SELF-LOOPS ALLOWED. A within-class
    /// relation (king·throne → royalty·royalty) is the load-bearing generalization: once "royalty things
    /// co-activate" is modeled, any royalty sentence is meaning-covered even if lexically novel — so L3 reads
    /// DOMAIN-level coverage, not surface coverage.
    private Pair ClassRel(string a, string b) => Pair.Of(Find(a), Find(b));

    private void RebuildClassRel()
    {
        _classRel = new HashSet<Pair>();
        foreach (var e in _relCount.Keys) _classRel.Add(ClassRel(e.A, e.B));
    }

    // ── the measurement: multi-level coverage of a response vs the PRE-FOLD model ────────────────────────────────

    /// Coverage of `response` at lexical / relational / class-relational levels against everything modeled so far
    /// (call BEFORE Fold). Carries the headline residual (1 − L3) plus per-clause residuals for frontier seeding.
    public Measurement Measure(string response)
    {
        var per = new List<ClauseResidual>();
        int cTot = 0, cSeen = 0, rTot = 0, rSeen = 0, crSeen = 0;
        foreach (var clause in Clauses(response))
        {
            var cs = clause.Concepts;
            foreach (var c in cs) { cTot++; if (_seenConcepts.Contains(c)) cSeen++; }

            var ps = Pairs(cs);
            int clTot = ps.Count, clCov = 0;
            foreach (var e in ps)
            {
                rTot++;
                if (_relCount.ContainsKey(e)) rSeen++;
                if (_classRel.Contains(ClassRel(e.A, e.B))) { crSeen++; clCov++; }
            }
            double res = 1.0 - (clTot > 0 ? (double)clCov / clTot : 1.0);
            var cov = clCov == 0 ? ClauseCoverages.Novel : clCov == clTot ? ClauseCoverages.Covered : ClauseCoverages.Mixed;
            per.Add(new ClauseResidual(clause.Raw, res, cov));
        }
        double l1 = cTot > 0 ? (double)cSeen / cTot : 0.0;
        double l2 = rTot > 0 ? (double)rSeen / rTot : 0.0;
        double l3 = rTot > 0 ? (double)crSeen / rTot : 0.0;
        return new Measurement(1.0 - l3, l1, l2, l3, rTot, per);
    }

    /// The headline residual + per-clause residuals only (the meaning-frontier read the drive selects on).
    public (double Residual, List<ClauseResidual> PerClause) ResponseResidual(string response)
    {
        var m = Measure(response);
        return (m.Residual, m.PerClause);
    }

    // ── fold a response into the model ───────────────────────────────────────────────────────────────────────────

    /// Fold a response into the meaning model: accrete its concepts, co-activation edges, and frame template, then
    /// re-induce the paradigm classes from the grown graph and refresh the L3 class-relation set. Returns the
    /// step's events (new concepts / relations / frames / slots, and the merges the re-induction applied).
    public FoldEvents Fold(string response)
    {
        var ev = new FoldEvents();
        foreach (var clause in Clauses(response))
        {
            ev.Clauses++;
            var cs = clause.Concepts;
            foreach (var c in cs)
            {
                if (_seenConcepts.Add(c)) ev.NewConcepts++;
                _conceptCount[c] = _conceptCount.GetValueOrDefault(c) + 1;
            }
            foreach (var e in Pairs(cs))
            {
                if (!_relCount.ContainsKey(e)) ev.NewRelations++;
                _relCount[e] = _relCount.GetValueOrDefault(e) + 1;
                Neighbour(e.A).Add(e.B);
                Neighbour(e.B).Add(e.A);
            }
            FoldTemplate(clause.Raw, cs, ref ev);
        }
        ev.Merges = RecomputeClasses();       // induce paradigm classes from the grown co-activation graph
        RebuildClassRel();                     // refresh the L3 class-relation model
        return ev;
    }

    private HashSet<string> Neighbour(string x)
        => _neighbours.TryGetValue(x, out var s) ? s : _neighbours[x] = new HashSet<string>();

    // Align this clause to its best-overlapping frame (LCS under synonymy); a good-enough match records the
    // unmatched concepts as new slot-fillers (the frontier's interest), else the clause mints a fresh frame.
    private void FoldTemplate(string raw, List<string> cs, ref FoldEvents ev)
    {
        Template? best = null;
        double bcov = 0.0;
        foreach (var t in _templates)
        {
            double ov = t.Skeleton.Count > 0 ? (double)LCS(t.Skeleton, cs, Syn).Count / t.Skeleton.Count : 0.0;
            if (ov > bcov) { best = t; bcov = ov; }
        }
        if (best is null || bcov < ThetaFrame)
        {
            _templates.Add(new Template(new List<string>(cs), raw));
            ev.NewFrame++;
            return;
        }
        var align = LCS(best.Skeleton, cs, Syn);
        var matched = new HashSet<int>(align.Select(p => p.J));
        for (int j = 0; j < cs.Count; j++)
            if (!matched.Contains(j) && best.SlotFillers.Add(cs[j])) ev.NewSlot++;
        best.Instances++;
    }

    /// The frontier exemplar: the least-instantiated frame (longest skeleton breaks ties) — the meaning-frontier
    /// seed a drive re-presents to widen coverage. Null when nothing has been folded yet.
    public string? FrontierExemplar()
    {
        if (_templates.Count == 0) return null;
        var best = _templates[0];
        foreach (var t in _templates)
            if (t.Instances < best.Instances || (t.Instances == best.Instances && t.Skeleton.Count > best.Skeleton.Count))
                best = t;
        return best.Exemplar;
    }

    // ── anti-unification alignment: longest common subsequence under a synonymy predicate ────────────────────────

    /// LCS of two concept sequences under `syn` (identity-or-same-class), returned as aligned (i, j) index pairs.
    /// Backward DP fills the length table; the forward greedy walk (prefer advancing `a` on ties) reconstructs a
    /// deterministic alignment — the anti-unification skeleton match two clauses share.
    public static List<(int I, int J)> LCS(List<string> a, List<string> b, Func<string, string, bool> syn)
    {
        int na = a.Count, nb = b.Count;
        var dp = new int[na + 1, nb + 1];
        for (int i = na - 1; i >= 0; i--)
            for (int j = nb - 1; j >= 0; j--)
                dp[i, j] = syn(a[i], b[j]) ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var outp = new List<(int, int)>();
        int ii = 0, jj = 0;
        while (ii < na && jj < nb)
        {
            if (syn(a[ii], b[jj])) { outp.Add((ii, jj)); ii++; jj++; }
            else if (dp[ii + 1, jj] >= dp[ii, jj + 1]) ii++;
            else jj++;
        }
        return outp;
    }

    // ── the substrate self-test (zero LLM calls) — verb `semgrammar` ─────────────────────────────────────────────

    /// usage: semgrammar [--file <path>]
    ///   Fold a response stream into the meaning model, printing the per-step L1/L2/L3 coverage curve. With no
    ///   --file, runs the built-in royalty/regalia corpus: L1/L2 rise as words/pairs recur, and L3 should cover
    ///   the lexically-novel "prince" sentence via an induced royalty class while the "cup" sentence stays novel.
    ///   --file measures a real text (blank-line-separated paragraphs = the response stream).
    public static int Run(string[] args)
    {
        var file = Args.Str(args, "--file", "");
        List<string> responses;
        if (file.Length > 0)
        {
            if (!File.Exists(file)) { Console.Error.WriteLine($"  no such file: {file}"); return 1; }
            responses = File.ReadAllText(file)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Length > 0).ToList();
        }
        else responses = SelfTestCorpus.ToList();

        var g = new SemGrammar();
        var run = Cogito.Run.New("semgrammar");
        Trace.Note($"semgrammar · {(file.Length > 0 ? Path.GetFileName(file) : "self-test (royalty/regalia)")} · {responses.Count} responses · residual = 1−L3 (semantic, pre-fold)");
        Trace.Note("  L1 lexical (concepts seen) · L2 relational (co-activation pairs seen) · L3 class-relational (covered by a seen paradigm-class relation)");
        Trace.Note("");
        Trace.Note("    step    res     L1    L2    L3    classes  merges   frames");

        var tsv = new StringBuilder("step\tres\tl1\tl2\tl3\tclasses\tmerges\tframes\trelations\n");
        for (int i = 0; i < responses.Count; i++)
        {
            var m = g.Measure(responses[i]);          // coverage vs the pre-fold model
            var ev = g.Fold(responses[i]);            // then fold the response in
            Trace.Note($"    t{i,-3} {m.Residual,7:F3} {m.L1ConceptCov,5:F2} {m.L2RelationCov,5:F2} {m.L3ClassRelCov,5:F2}   {g.ParadigmClasses(),5}   {ev.Merges,5}   {g.TemplateCount,5}(+{ev.NewFrame})");
            tsv.Append($"{i}\t{m.Residual:F4}\t{m.L1ConceptCov:F4}\t{m.L2RelationCov:F4}\t{m.L3ClassRelCov:F4}\t{g.ParadigmClasses()}\t{ev.Merges}\t{g.TemplateCount}\t{m.RelationCount}\n");
        }

        Trace.Note("");
        Trace.Note("  paradigm classes (>1 member) — synonyms induced from shared co-activation neighbourhoods:");
        var classes = g.MultiMemberClasses();
        if (classes.Count == 0) Trace.Note("    (none — not enough shared-context evidence to fuse concepts yet)");
        foreach (var (_, members) in classes) Trace.Note("    { " + string.Join(", ", members) + " }");

        run.Write("measure.tsv", tsv.ToString());
        Trace.Note("");
        Trace.Note("  EXPECT: L1/L2 rise as words/pairs recur; L3 covers the lexically-novel 'prince' sentence via a");
        Trace.Note("  royalty/regalia class if clustering fires; the 'cup' sentence stays novel (a fresh frame).");
        return 0;
    }

    /// The paradigm classes with more than one member — the induced synonym groups, members sorted ordinal and
    /// groups ordered by root (deterministic). The self-test prints these; a drive can read the class structure.
    public List<(string Root, List<string> Members)> MultiMemberClasses()
    {
        var byRoot = new Dictionary<string, List<string>>();
        foreach (var c in _parent.Keys.ToList())
        {
            var root = Find(c);
            (byRoot.TryGetValue(root, out var lst) ? lst : byRoot[root] = new List<string>()).Add(c);
        }
        var outp = new List<(string, List<string>)>();
        foreach (var root in byRoot.Keys.OrderBy(r => r, StringComparer.Ordinal))
        {
            var members = byRoot[root];
            if (members.Count <= 1) continue;
            members.Sort(StringComparer.Ordinal);
            outp.Add((root, members));
        }
        return outp;
    }

    // The royalty/regalia self-test corpus: king/throne/crown recur (L1/L2 climb); the compression frame recurs
    // with synonyms (grammar↔model, data↔evidence); "prince" is lexically novel but royalty-class covered (L3);
    // "cup on the table" is a genuinely new frame that stays novel.
    private static readonly string[] SelfTestCorpus =
    {
        "The king sat on the throne wearing his crown.",
        "Compression preserves information; the grammar explains the data.",
        "The queen approached the throne and lifted the crown.",
        "Compression preserves structure; the model explains the evidence.",
        "A prince will inherit the throne and its golden crown.",
        "The cup on the table was empty.",
    };

    private const StringComparison Ord = StringComparison.Ordinal;

    // suffix → replacement, longest / most-specific first — the FIRST endswith-match wins, so order is load-bearing
    // (izations before ization before ion). A match fires only when the remaining stem is still ≥3 chars.
    private static readonly (string Suffix, string Replacement)[] StemSuffixes =
    {
        ("izations", "ize"), ("ization", "ize"), ("isation", "ize"),
        ("ational", "ate"), ("ations", "ate"), ("ation", "ate"),
        ("iveness", "ive"), ("ousness", "ous"), ("fulness", "ful"),
        ("ements", "e"), ("ement", "e"), ("ingly", ""), ("edly", ""),
        ("ness", ""), ("ments", "e"), ("ment", "e"), ("ions", ""), ("ion", ""),
    };

    // content words: runs of ASCII letters.
    [GeneratedRegex(@"[A-Za-z]+")]
    private static partial Regex WordRegex();

    // clause boundaries: punctuation runs, plus spaced hyphen / em-dash (—) / en-dash (–).
    [GeneratedRegex(@"[.!?;:,—–()\[\]""\n]+|\s-\s|\s—\s")]
    private static partial Regex ClauseRegex();

    // the finite stationary VOICE — function words dropped before any concept is coined (a content-word filter).
    private static readonly HashSet<string> StopWords = new(
        (@"a an the this that these those there here it its they them their then thus so such
           and or but nor for yet because since while as if than when where which who whom whose what
           why how all any both each few more most other some no not only own same too very can will just
           of in on at by to up out off over under again further once into onto from with about against
           between through during before after above below down upon within without across behind beyond
           i me my we us our you your he him his she her hers do does did doing done have has had having
           be been being am is are was were would should could might must may shall let
           one two three four five also however moreover therefore hence thereby whereby indeed instead
           rather perhaps often always never sometimes usually generally essentially simply merely actually
           really quite much many lot lots kind sort way ways thing things something anything nothing everything
           get got gets getting make makes made making use used uses using like likes liked
           become becomes came come comes goes went being able toward upon per via amid among")
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

// ── the domain vocabulary ────────────────────────────────────────────────────────────────────────────────────

/// An unordered concept pair, canonicalized so A ≤ B (ordinal) — the value-equal key for co-activation edges and
/// class-relations. `Of` sorts; a self-loop (x, x) is well-formed (class-relations allow it, co-activation drops it).
public readonly record struct Pair(string A, string B)
{
    public static Pair Of(string x, string y) => string.CompareOrdinal(x, y) <= 0 ? new Pair(x, y) : new Pair(y, x);
}

/// One clause: its raw text (the frontier exemplar source) + its ≥2 content concepts (in text order).
public readonly record struct Clause(string Raw, List<string> Concepts);

/// The per-clause coverage verdict at the class-relational level — none of its relations covered, all of them, or a mix.
public enum ClauseCoverages { Novel, Covered, Mixed }

/// One clause's class-relational residual within a measured response: its raw text, residual (1 − covered
/// fraction), and coverage verdict — the granularity a drive picks its next meaning-frontier target from.
public readonly record struct ClauseResidual(string Raw, double Residual, ClauseCoverages Coverage);

/// A response's multi-level coverage against the pre-fold meaning model. Residual (1 − L3) is the headline
/// semantic reward; L1/L2/L3 are the lexical / relational / class-relational coverage curves; RelationCount is
/// the number of co-activation relations measured; PerClause carries the per-clause residuals.
public readonly record struct Measurement(
    double Residual, double L1ConceptCov, double L2RelationCov, double L3ClassRelCov,
    int RelationCount, List<ClauseResidual> PerClause);

/// What one Fold changed in the model — new frames/slots, the class merges the re-induction applied, new concepts
/// and relations, and how many clauses were folded. A drive reads these as the step's structural growth signal.
public struct FoldEvents
{
    public int NewFrame, NewSlot, Merges, NewConcepts, NewRelations, Clauses;
}

/// An anti-unified frame template: the concept SKELETON shared across its instances, an exemplar clause (the
/// frontier seed), the instance count, and the slot-fillers seen in the varying positions. Mutated in place as
/// clauses fold into it, so it is a class (reference identity across folds), not a struct.
public sealed class Template
{
    public List<string> Skeleton;
    public string Exemplar;
    public int Instances;
    public HashSet<string> SlotFillers;

    public Template(List<string> skeleton, string exemplar)
    {
        Skeleton = skeleton;
        Exemplar = exemplar;
        Instances = 1;
        SlotFillers = new HashSet<string>();
    }
}
