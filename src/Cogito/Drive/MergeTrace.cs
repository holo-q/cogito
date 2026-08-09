namespace Cogito;

using System.Numerics;
using System.Text;
using Cogito.Induct;

// THE THOUGHT STREAM — cogito's cognition, encoded into a form it can RE-INDUCE on (the strange loop).
//
// Re-Pair's pillar-1 operation IS thinking: each merge (the most-frequent pair chosen, a rule minted, positions
// rewritten) is a decision, and the time-ordered sequence of merges is cogito's literal thought stream (captured
// as MergeEvents in Induct). This module serializes that stream into token-sentences cogito can feed straight
// back into induction — so it induces the GRAMMAR OF ITS OWN THINKING. A recurring subsequence in the thought
// stream is a ROUTINE in how cogito thinks (an instinct); the meta-grammar's compression ratio is how repetitive
// its cognition is; the meta-grammar's renorm exponent asks whether its thinking is itself scale-invariant.
//
// The re-induction treats each thought-token as an ATOM (not its bytes) — a token like "LAT.c5" is one symbol,
// so Re-Pair finds recurring MERGE-subsequences, never substrings inside a descriptor. That is why the stream
// routes through ToTape → a token alphabet, apples-to-apples with cogito's own byte/token induction.
//
// Glossary: this is the THOUGHT stream (trace-loop over merge-events), NOT the Cli `dream`
// verb (a code-generation self-loop). Named for the mechanism: thought-stream, merge-events, thought-tokens.

/// How much of each merge is written into its thought-token — descriptor richness rising left to right. The
/// climb-class is the STRUCTURAL move (what kind of thought); the count-bucket is the SALIENCE tier (how
/// load-bearing); the span-bucket is the reach. Richer descriptors carry more, but fragment the stream sooner.
public enum ThoughtModes
{
    Salience,           // count-bucket only — the salience arc ("c5")
    Structure,          // climb-class only — the structural-move arc ("LAT")
    StructureSalience,  // (climb, count) — the rich thought-token ("LAT.c5"); the default
    Full,               // (climb, count, span) — richest ("LAT.c5.s3"); may fragment
}

/// Encodings of cogito's cognition into re-inducible token streams, and the bridge back into induction.
public static class MergeTrace
{
    // ── E1: the merge-event stream → the flat, time-ordered THOUGHT-TOKEN sequence ──────────────────────────
    /// Each merge becomes ONE descriptor token; the temporal order IS the thought stream. Induce the whole
    /// stream as one sentence (via ToTape) and Re-Pair finds the recurring subsequences — the routines in how
    /// cogito thinks — exactly as it finds them in byte streams of code. Depth is rebuilt from the events (each
    /// carries its new rule's depth), so the climb-class of a merge reads its children's coarse-graining levels.
    public static string[] EncodeEvents(IReadOnlyList<MergeEvent> events, uint alphabetSize, ThoughtModes mode = ThoughtModes.StructureSalience)
    {
        var depth = new Dictionary<uint, int>(events.Count);
        var toks = new string[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            depth[e.NewSymbol.Value] = e.Depth;                       // record for later merges that reference this rule
            string climb = Climb(e.A, e.B, alphabetSize, depth);      // reads children's depths, set on prior merges
            toks[i] = mode switch
            {
                ThoughtModes.Salience          => CountBucket(e.Count),
                ThoughtModes.Structure         => climb,
                ThoughtModes.StructureSalience => $"{climb}.{CountBucket(e.Count)}",
                ThoughtModes.Full              => $"{climb}.{CountBucket(e.Count)}.{SpanBucket(e.Span)}",
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        }
        return toks;
    }

    // ── E2: the rule SET → a rule-SHAPE token stream (what KINDS of rules cogito built) ─────────────────────
    /// Each rule becomes one compound token `d{depth}.{climb}.c{use}` — its coarse-graining level, structural
    /// move, and load. Re-inducing this stream models "what shapes of rule I tend to build" (a different facet
    /// of self-modeling than the temporal thought stream). Single-stream to match cogito's engine; the recurring
    /// rule-shape SUBSEQUENCE is the signal (Python's cogtrace keeps 3-token records — the compound is the flat,
    /// engine-native form). Depth is the bottom-up RG scale, identical to Renorm / RenormStats.
    public static string[] EncodeRuleset(RePairResult r)
    {
        int nr = r.Rules.Length;
        if (nr == 0) return [];
        var depthByRule = new int[nr];
        var depthBySym = new Dictionary<uint, int>(nr);
        for (int i = 0; i < nr; i++)
        {
            int d = 0;
            foreach (var s in r.Rules[i].Pattern)
                if (s.Value >= r.AlphabetSize && (int)(s.Value - r.AlphabetSize) < i) d = Math.Max(d, depthByRule[(int)(s.Value - r.AlphabetSize)]);
            depthByRule[i] = d + 1;
            depthBySym[r.AlphabetSize + (uint)i] = d + 1;
        }
        var uses = Engine.RuleUses(r);
        var toks = new string[nr];
        for (int i = 0; i < nr; i++)
        {
            var p = r.Rules[i].Pattern;
            string climb = Climb(p[0], p[1], r.AlphabetSize, depthBySym);
            toks[i] = $"d{depthByRule[i]}.{climb}.{CountBucket(uses[i] + 1)}";
        }
        return toks;
    }

    // ── the bridge: thought-tokens → a re-inducible atom tape ───────────────────────────────────────────────
    /// Map each distinct thought-token to a dense terminal id (first-seen order — deterministic) and lay them
    /// out as a Symbol tape whose alphabet size = `vocab.Length`. Induce with `new RePair().Induce(tape, Zero,
    /// (uint)vocab.Length)`: each thought-token is one atom, so the grammar is over merge-subsequences, never
    /// descriptor substrings. `vocab[id]` is the inverse map — the string a terminal id decodes back to.
    public static Symbol[] ToTape(string[] toks, out string[] vocab)
    {
        var idOf = new Dictionary<string, uint>(toks.Length);
        var tape = new Symbol[toks.Length];
        for (int i = 0; i < toks.Length; i++)
        {
            if (!idOf.TryGetValue(toks[i], out var id)) { id = (uint)idOf.Count; idOf[toks[i]] = id; }
            tape[i] = new Symbol(id);
        }
        vocab = new string[idOf.Count];
        foreach (var (tok, id) in idOf) vocab[id] = tok;
        return tape;
    }

    /// Decode a meta-grammar rule back to the thought-token sequence it stands for — a recurring cognitive
    /// routine, read in cogito's own descriptor vocabulary. Token-alphabet-aware (terminals are ids &lt;
    /// `AlphabetSize`, decoded via `vocab`; nonterminals recurse), since the meta-grammar is over a token
    /// alphabet, not bytes — Reconstruct.Expand (byte-only, 256-boundary) cannot read it.
    public static string Render(RePairResult meta, int ruleIdx, string[] vocab)
    {
        var sb = new StringBuilder();
        void Emit(Symbol s)
        {
            if (s.Value < meta.AlphabetSize) { if (sb.Length > 0) sb.Append(' '); sb.Append(vocab[s.Value]); return; }
            foreach (var p in meta.Rules[(int)(s.Value - meta.AlphabetSize)].Pattern) Emit(p);
        }
        Emit(new Symbol(meta.AlphabetSize + (uint)ruleIdx));
        return sb.ToString();
    }

    // ── descriptor helpers (the thought-token vocabulary) ───────────────────────────────────────────────────

    /// The STRUCTURAL move of a merge, depth-relative — the substrate-agnostic "kind of thought". TT: both
    /// terminals · TN/NT: terminal + rule · LAT: two rules at the same depth · UP/DN: a rule joined with a
    /// deeper / shallower rule. `depth` holds the coarse-graining level of every nonterminal seen so far.
    private static string Climb(Symbol a, Symbol b, uint alphabetSize, Dictionary<uint, int> depth)
    {
        bool ta = a.Value < alphabetSize, tb = b.Value < alphabetSize;
        if (ta && tb) return "TT";
        if (ta) return "TN";
        if (tb) return "NT";
        int da = depth.GetValueOrDefault(a.Value), db = depth.GetValueOrDefault(b.Value);
        return da == db ? "LAT" : da < db ? "UP" : "DN";
    }

    /// The salience tier of a merge — floor(log2 count), an exact integer bucket (no float touches the token).
    private static string CountBucket(int count) => $"c{BitOperations.Log2((uint)Math.Max(1, count))}";

    /// The reach tier of a merge — floor(log2 span).
    private static string SpanBucket(int span) => $"s{BitOperations.Log2((uint)Math.Max(1, span))}";
}
