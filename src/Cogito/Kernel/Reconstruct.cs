namespace Cogito.Induct;

using Cogito.Grammar;

// Whole-corpus reconstruction — the native verifier and the inverse of induction. Expands a Re-Pair
// start sequence back to terminal bytes; losslessness (recon == original) is the proof the engine is exact +
// deterministic, and it is the read path every organ uses to render a rule's yield. Lives beside the Re-Pair
// engine because it IS induction run backward — the compress↔decompress pair.

public static class Reconstruct
{
    /// `rulesByEmission[i]` is nonterminal `256+i` (the Re-Pair emission-order contract — positional,
    /// so this needs no nonterminal field on the rule). Recursion depth = rule nesting.
    public static byte[] Expand(GrammarRule[] rulesByEmission, ReadOnlySpan<Symbol> start)
    {
        var outp = new List<byte>(start.Length * 2);
        foreach (var s in start) ExpandOne(s, rulesByEmission, outp);
        return outp.ToArray();
    }

    /// Expand against a standing rule list without forcing append-only consumers to
    /// materialize a fresh `GrammarRule[]` for every delta rule. The list is the same
    /// emission-ordered authority as the array overload; it merely preserves the caller's
    /// retained rule storage while a publication appends a small rule suffix.
    public static byte[] Expand(IReadOnlyList<GrammarRule> rulesByEmission, ReadOnlySpan<Symbol> start)
    {
        var outp = new List<byte>(start.Length * 2);
        foreach (var s in start) ExpandOne(s, rulesByEmission, outp);
        return outp.ToArray();
    }

    /// Expand with tape-ref RESOLUTION — a DEMOTED rule (Kind==TapeRef) resolves its bytes
    /// through its reference `tape` span CHAIN (one seg for a full-line rule, many for a multi-line mega-rule)
    /// instead of recursing its retained pattern (the demote-don't-delete read path). The tape-less overload keeps
    /// expanding the retained pattern (the fallback tape-unaware callers rely on), so both are byte-identical by
    /// construction — this overload proves the working-set delaminated to the tape resolves lossless, and is the
    /// read path once generation threads the tape.
    public static byte[] Expand(GrammarRule[] rulesByEmission, ReadOnlySpan<Symbol> start, Tape tape)
    {
        var outp = new List<byte>(start.Length * 2);
        foreach (var s in start) ExpandOne(s, rulesByEmission, outp, tape);
        return outp.ToArray();
    }

    private static void ExpandOne(Symbol s, GrammarRule[] rules, List<byte> outp)
    {
        if (s.IsTerminal) { outp.Add((byte)s.Value); return; }
        var rule = rules[(int)s.Value - (int)Symbol.FirstNonterminal];
        if (rule.IsSlot) { ExpandOne(rule.Pattern[0], rules, outp); return; }   // : a paradigm class expands to its representative member
        foreach (var p in rule.Pattern) ExpandOne(p, rules, outp);
    }

    private static void ExpandOne(Symbol s, IReadOnlyList<GrammarRule> rules, List<byte> outp)
    {
        if (s.IsTerminal) { outp.Add((byte)s.Value); return; }
        var rule = rules[(int)s.Value - (int)Symbol.FirstNonterminal];
        if (rule.IsSlot) { ExpandOne(rule.Pattern[0], rules, outp); return; }
        foreach (var p in rule.Pattern) ExpandOne(p, rules, outp);
    }

    private static void ExpandOne(Symbol s, GrammarRule[] rules, List<byte> outp, Tape tape)
    {
        if (s.IsTerminal) { outp.Add((byte)s.Value); return; }
        var rule = rules[(int)s.Value - (int)Symbol.FirstNonterminal];
        if (rule.IsDemoted && rule.Segs is { } segs && tape.TryResolveChain(segs, outp)) return;   // : resolve the demoted body (single- or multi-span chain) through the reference bytes
        if (rule.IsSlot) { ExpandOne(rule.Pattern[0], rules, outp, tape); return; }   // : paradigm class → representative member
        foreach (var p in rule.Pattern) ExpandOne(p, rules, outp, tape);
    }
}
