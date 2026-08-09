namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;


// ── COV-BEACON ──  the DOCUMENT-SIDE grammar-coverage beacon (the file@1 un-clamp; RESULT_v1_pretrained.md:85).
//
// THE CLOG it attacks: file@1 pinned at the IR floor across every nav arm (cold ≡ pretrained, Δ+0 per repo)
// because file@1 is GOVERNED BY STATIC BM25 — the beacon reads exact-token overlap and never reads the grammar's
// structural evidence about which file the issue is ABOUT. The pretrained grammar covers 93% of the issue text
// (a deep code+prose model) but that coverage only feeds the fn-level LOCAL field; the FILE ranking is blind to it.
//
// THE LSR MAPPING (Nguyen/MacAvaney/Yates ECIR'23, "A Unified Framework for Learned Sparse Retrieval", Table 5):
// the single biggest lever in learned sparse retrieval is DOCUMENT-side term WEIGHTING — BINARY→weighted doc
// encoder = +27.9 MRR (row 1b), an order above query weighting (+1.9, row 2b), and document expansion CANCELS
// query expansion (+0.0, row 4a — query-side expansion is unnecessary once the doc side is weighted). Cogito's
// GRAMMAR is the document encoder: the rules that fire on a candidate file ARE its lexical terms, and weighting
// those firings (vs a binary "the grammar covers this file") is exactly the doc-weighting move. The score shape is
// DeepImpact's (Mallia et al. SIGIR'21): score(q,d) = Σ impacts over the terms q and d SHARE. Here:
//
//   covBeacon[file] = Σ over grammar rules r that cover BOTH the ISSUE and the FILE  of  weight(r)
//
// The rules covering issue bytes are the issue's "grammar terms"; a file whose text exercises those SAME deep
// rules is structurally about the same thing — grammar-mediated soft-matching, the vocabulary bridge BM25's exact
// match misses. It is ADDITIVE into the file-level aggregate ONLY (never the site field), so fn@5 (the v1 local
// field) is untouched — the file@1 lever is isolated for a clean attribution. Off (no --cov-beacon) is a no-op:
// the beacon is never built, baseScore reaches AggregateMaxFiles unchanged, existing runs are byte-identical.
//
// THE WEIGHT — three integer, gradient-free variants (--cov-weight), no training loop:
//   (a) VEST  — the provenance-weighted corroboration count (MergeEvent.Count at mint, the wcount Mdl.PairDelta
//               normalizes; Induct.cs:28). Self-supervised WITNESSED trust: a rule that recurred across independent
//               evidence weighs more. Goodhart-proof (corroboration = evidence = weight), the purity-crux weight.
//   (b) RATIO — the discriminative count-ratio, DeepCT's term-recall analog (fires-on-gold-fix-files / fires-over-
//               all-files). *** SUPERVISED — reads gold.json. This is a DIFFERENT experiment class than the
//               zero-supervision nav (gold is otherwise print-never-steer). It is the null-kill: "is there ANY
//               per-rule reweighting that clears the floor?" On the frozen 300 it is an ORACLE UPPER BOUND (no
//               train/test split), disclosed as such — it bounds what a TRAINED per-rule head could reach, it is
//               not a deployable score. Gated behind --cov-weight ratio; NEVER blended into arms (a)/(c)'s claim.
//   (c) PROD  — vest × ratio (corroborated-trust × task-discrimination). Same supervised caveat as (b).
//
// SPARSITY + QUANT (the field's efficiency discipline, harmless-to-effectiveness):
//   • TOP-K per file (--cov-topk) — keep the k highest-weight firing rules, zero the rest (LSR Top-K pruning,
//     Table 5 row 5a ≈ FLOPs, no loss). Auto-silences ubiquitous rules — the direct cure for a uniform-weight null.
//   • QUANTIZE weights to POSITIVE 8-bit [1,255]. Positivity is
//     load-bearing — negative weights break beacon pruning (the "Wacky Weights" pitfall, Mackenzie et al.).
public sealed class CovBeacon
{
    /// The weight a firing rule contributes to a file's beacon score.
    ///   VEST  — corroborated corroboration (unsupervised); rewards rules that recurred across evidence.
    ///   IDF   — vest × RARITY across the candidate files (unsupervised); the IDF cure for the ubiquity trap:
    ///           vest-count REWARDS ubiquity (generic syntax rules recur most), which is anti-discriminative for
    ///           retrieval — a rule firing on every file localizes nothing. IDF down-weights it by 1/df, the
    ///           unsupervised analog of RATIO. Gold-free; the principled unsupervised discriminative weight.
    ///   RATIO — the discriminative count-ratio (SUPERVISED, reads gold — the oracle null-kill, reported apart).
    ///   PROD  — vest × ratio (SUPERVISED). Corroborationed-trust × gold-discrimination.
    public enum Weights { Vest, Idf, Ratio, Prod }

    private readonly GrammarRule[] _rules;        // the FULL frozen mesh grammar (emission order — the document encoder's vocabulary)
    private readonly byte[][] _exps;              // per-rule expansion bytes (len≥2), index-aligned with _rules; len<2 ⇒ empty (never fires)
    private readonly int[] _vest;                 // per-rule VEST weight (MergeEvent.Count at mint), quantized [1,255], index-aligned
    private readonly Weights _mode;
    private readonly int _topK;                   // 0 = keep all firing rules per file
    private readonly double _scale;               // blend coefficient into the file aggregate (like WExpand for the mint field)
    private readonly int _minLen;                 // only rules whose expansion is ≥ this many bytes are "grammar-terms" — the DEEP-TEMPLATE restriction

    private CovBeacon(GrammarRule[] rules, byte[][] exps, int[] vest, Weights mode, int topK, double scale, int minLen)
    { _rules = rules; _exps = exps; _vest = vest; _mode = mode; _topK = topK; _scale = scale; _minLen = minLen; }

    public double Scale => _scale;
    public Weights Mode => _mode;
    public int RuleCount => _rules.Length;
    public int MinLen => _minLen;
    public bool Supervised => _mode is Weights.Ratio or Weights.Prod;

    /// The count of DEEP rules in the grammar (expansion ≥ _minLen bytes) — the discriminative-scale vocabulary the
    /// transfer read cares about (the C#-grammar had thousands of rules but the ≥20B deep ones fired ZERO times on the
    /// Python issue text; a Python grammar's deep count that FIRES is the whole hypothesis). O(rules), diagnostic-only.
    public int DeepRuleCount { get { int c = 0; foreach (var e in _exps) if (e.Length >= 2 && e.Length >= _minLen) c++; return c; } }

    /// How many of the issue's deep grammar-terms ALSO fire on `fileText` — the issue↔file shared-deep-rule count (the
    /// transfer's discriminative substance: a shared DEEP rule is a long identifier / API phrase / idiom template both
    /// the issue and the file exercise). Used by --cov-diag to measure "deep rules firing on BOTH issue and gold file".
    public int CountFiring(IssueTerms terms, byte[] fileText)
    {
        if (fileText.Length == 0) return 0;
        Span<byte> file = fileText; int n = 0;
        for (int t = 0; t < terms.Idx.Length; t++) if (file.IndexOf(_exps[terms.Idx[t]]) >= 0) n++;
        return n;
    }

    /// The literal expansions of the issue-terms that ALSO fire on `fileText` — the CONCRETE shared structure, for the
    /// --cov-dump readout (are these real Python identifiers/phrases or generic ASCII runs?). Newline-escaped, capped.
    public List<string> SharedExpansions(IssueTerms terms, byte[] fileText, int cap)
    {
        var outp = new List<string>();
        if (fileText.Length == 0) return outp;
        Span<byte> file = fileText;
        for (int t = 0; t < terms.Idx.Length && outp.Count < cap; t++)
        {
            var e = _exps[terms.Idx[t]];
            if (file.IndexOf(e) < 0) continue;
            outp.Add(Encoding.UTF8.GetString(e).Replace("\n", "\\n").Replace("\t", "\\t"));
        }
        return outp;
    }

    /// Build the beacon ONCE from the mesh checkpoint (reused read-only across all 300 instances — the grammar is
    /// ~thousands of rules; expanding + quantizing per instance would be the rebuild-O(total) trap). The vest weight
    /// is the per-rule MergeEvent.Count (weighted count at mint) recovered by re-inducing the corroborated tape TRACED —
    /// the ONE place the provenance-weighted corroboration survives as a per-rule scalar (the frozen GrammarRule
    /// drops it; the tape's provenance bytes are consumed during induce). Returns null when the checkpoint yields no
    /// grammar. `traced` is the (grammar, events) pair from Engine.InduceTraced over the mesh tape — events[i]
    /// corresponds to grammar.Rules[i] by emission order (the same bottom-up order RenormStats walks).
    public static CovBeacon? Build(RePairResult grammar, List<MergeEvent> events, Weights mode, int topK, double scale, int minLen)
    {
        var rules = grammar.Rules;
        if (rules is not { Length: > 0 }) return null;
        var exps = new byte[rules.Length][];
        for (int i = 0; i < rules.Length; i++)
        {
            var e = Reconstruct.Expand(rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            exps[i] = e.Length >= 2 ? e : [];   // a 1-byte expansion is a lone terminal — it covers a byte of every file, no discrimination
        }
        // VEST = the merge count at mint, quantized to positive 8-bit. events aligns with rules by emission order;
        // guard the length in case a caller passes a mismatched pair (fall back to a uniform 1 — the binary null).
        var raw = new long[rules.Length];
        long max = 1;
        for (int i = 0; i < rules.Length; i++)
        {
            long c = i < events.Count ? events[i].Count : 1;
            raw[i] = c < 1 ? 1 : c;
            if (raw[i] > max) max = raw[i];
        }
        var vest = new int[rules.Length];
        for (int i = 0; i < rules.Length; i++) vest[i] = Quantize(raw[i], max);
        return new CovBeacon(rules, exps, vest, mode, topK, scale, minLen);
    }

    /// Linear quantization to [1,255] (DeepImpact's 8-bit impact store — no precision loss, positivity preserved so
    /// Top-K pruning stays correct). max maps to 255, everything ≥1 maps to ≥1 (never zeroed — a firing rule always
    /// carries weight; Top-K, not quantization, is what drops rules).
    private static int Quantize(long w, long max) => max <= 1 ? 1 : 1 + (int)((w - 1) * 254L / (max - 1));

    /// The issue's covering rule-set: the indices into _rules whose expansion appears in the issue bytes, each with
    /// its weight. This is the "query grammar-terms" — computed ONCE per instance (the issue is fixed), then every
    /// file is scored against THIS small set. A rule earns membership by CONTAINMENT (its expansion is a substring of
    /// the issue) — the same firing test GrammarCover uses, without the non-overlap bookkeeping (the beacon sums
    /// weights of DISTINCT rules that fire, not a byte-cover, so overlap between two rules is fine — each is one term).
    public IssueTerms IssueGrammarTerms(byte[] issue)
    {
        var idx = new List<int>();
        var wt = new List<int>();
        for (int i = 0; i < _rules.Length; i++)
        {
            var e = _exps[i];
            if (e.Length < 2 || e.Length < _minLen) continue;   // DEEP-TEMPLATE restriction: short rules are BM25-visible noise; the grammar's discriminative signal is the long phrase/template rules (the fn-template scale)
            if (issue.AsSpan().IndexOf(e) >= 0) { idx.Add(i); wt.Add(_vest[i]); }
        }
        return new IssueTerms(idx.ToArray(), wt.ToArray());
    }

    /// The per-file beacon bonus: Σ weight(r) over the issue's grammar-terms that ALSO fire on this file, Top-K
    /// pruned. `termFactor` is null for the pure VEST arm; for IDF/RATIO/PROD it is the per-issue-term discriminative
    /// factor (0..255 — rarity for IDF, gold-recall for RATIO) aligned with `terms.Idx`, folded per the mode. The
    /// returned bonus is RAW (unscaled) — the caller normalizes to BM25's scale when folding into the file aggregate.
    public double FileBonus(IssueTerms terms, byte[] fileText, int[]? termFactor)
    {
        if (terms.Idx.Length == 0 || fileText.Length == 0) return 0;
        Span<byte> file = fileText;
        // gather the weight of each issue-term that fires on THIS file. VEST = the raw corroborated weight; IDF/RATIO/
        // PROD fold in the per-term discriminative FACTOR (rarity for IDF, gold-recall for RATIO) computed once per
        // instance — IDF/RATIO are vest × factor, RATIO alone is the factor (DeepCT's pure term-recall).
        Span<long> hits = terms.Idx.Length <= 512 ? stackalloc long[terms.Idx.Length] : new long[terms.Idx.Length];
        int nHit = 0;
        for (int t = 0; t < terms.Idx.Length; t++)
        {
            var e = _exps[terms.Idx[t]];
            if (file.IndexOf(e) < 0) continue;
            long f = termFactor is null ? 1 : termFactor[t];
            long w = _mode switch
            {
                Weights.Vest  => terms.Weight[t],
                Weights.Idf   => (long)terms.Weight[t] * f,   // corroborated-trust × rarity (unsupervised discriminative)
                Weights.Ratio => f,                            // pure gold-recall (DeepCT term-recall, supervised)
                Weights.Prod  => (long)terms.Weight[t] * f,   // corroborated-trust × gold-discrimination (supervised)
                _             => terms.Weight[t],
            };
            hits[nHit++] = w;
        }
        if (nHit == 0) return 0;
        // TOP-K: keep the k heaviest firing terms (auto-silence ubiquitous rules). 0 = keep all.
        var slice = hits[..nHit];
        if (_topK > 0 && nHit > _topK)
        {
            // partial: sort descending, sum the top k. nHit is small (issue-term-set), a full sort is cheap.
            slice.Sort();               // ascending
            long s = 0; for (int i = nHit - _topK; i < nHit; i++) s += slice[i];
            return s;
        }
        long sum = 0; for (int i = 0; i < nHit; i++) sum += slice[i];
        return sum;
    }

    /// The issue's grammar-term set (rule indices + weights) — built once, scored against every file. Value type so
    /// the per-instance build allocates two small arrays, not an object graph.
    public readonly struct IssueTerms(int[] idx, int[] weight)
    {
        public readonly int[] Idx = idx;       // indices into the beacon's _rules
        public readonly int[] Weight = weight; // quantized vest weight per term, aligned with Idx
    }

    /// SUPERVISED (variant b/c only): the discriminative count-ratio per issue-term, DeepCT's term-recall analog.
    /// For each issue grammar-term, ratio = (# gold-fix files it fires on) / (# candidate files it fires on),
    /// quantized [1,255]. Reads gold — the ORACLE null-kill, disclosed. Returns null-safe uniform when goldFiles is
    /// empty (degrades RATIO to the binary null, PROD to VEST). O(terms · files) — computed once per instance.
    public int[] RatioWeights(IssueTerms terms, List<(string Path, byte[] Text)> files, HashSet<string> goldFiles)
    {
        var ratio = new int[terms.Idx.Length];
        for (int t = 0; t < terms.Idx.Length; t++)
        {
            var e = _exps[terms.Idx[t]];
            int firesAll = 0, firesGold = 0;
            foreach (var (path, text) in files)
            {
                if (text.AsSpan().IndexOf(e) < 0) continue;
                firesAll++;
                if (goldFiles.Contains(path)) firesGold++;
            }
            // recall-like: fraction of this term's firings that land on a gold file, scaled to [1,255].
            ratio[t] = firesAll == 0 ? 1 : 1 + (int)(254L * firesGold / firesAll);
        }
        return ratio;
    }

    /// UNSUPERVISED (Idf mode): the per-issue-term IDF factor — rarity across the candidate files, quantized [1,255].
    /// idf = log(1 + N/df) mapped to [1,255] by its max over the term set — a rule firing on FEW files scores high,
    /// a ubiquitous rule (fires on most files) scores ~1. The gold-free discriminative weight: it cures vest-count's
    /// ubiquity bias (generic-syntax rules recur most across evidence → high vest, but fire everywhere → useless for
    /// localization) exactly as BM25's IDF does for tokens. O(terms · files), once per instance — no gold, no train.
    public int[] IdfWeights(IssueTerms terms, List<(string Path, byte[] Text)> files)
    {
        int nf = files.Count;
        var idf = new double[terms.Idx.Length]; double idfMax = 0;
        for (int t = 0; t < terms.Idx.Length; t++)
        {
            var e = _exps[terms.Idx[t]];
            int df = 0;
            foreach (var (_, text) in files) if (text.AsSpan().IndexOf(e) >= 0) df++;
            idf[t] = Math.Log(1.0 + (double)nf / Math.Max(1, df));
            if (idf[t] > idfMax) idfMax = idf[t];
        }
        var q = new int[terms.Idx.Length];
        for (int t = 0; t < terms.Idx.Length; t++) q[t] = idfMax <= 0 ? 1 : 1 + (int)(254.0 * idf[t] / idfMax);
        return q;
    }
}
