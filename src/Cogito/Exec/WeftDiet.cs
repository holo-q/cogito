namespace Cogito.Exec;

using Cogito.Grammar;

/// The standing executable diet for the mesh Weft channel. These are authored VM programs, but their trace bytes are
/// the only thing that enters the shared Tape; dormant authored rules have no value until execution emits them.
public static class WeftDiet
{
    private static readonly string[] Species =
    [
        "loop-accumulate",
        "loop-countdown",
        "loop-even",
        "loop-triad",
        "cross-fold",
        "cross-reordered",
    ];

    public static WeftProgram Pick(int nodeIndex, int step, int blockBudget, string source)
        => GetByName(Species[(step + nodeIndex) % Species.Length], source, blockBudget);

    public static WeftProgram GetByName(string name, string source, int blockBudget)
        => name switch
        {
            "loop-accumulate" => CreateLoop(name, "push1 add — running counter", "1 +", blockBudget),
            "loop-countdown" => CreateLoop(name, "push1 sub — monotone descent", "1 -", blockBudget),
            "loop-even" => CreateLoop(name, "push1 dup add add — period-4 body", "1 : + +", blockBudget),
            "loop-triad" => CreateLoop(name, "push0 push1 add — period-3 body", "0 1 +", blockBudget),
            "cross-fold" => CreateCross(name, source, reordered: false),
            "cross-reordered" => CreateCross(name, source, reordered: true),
            _ => throw new ArgumentException($"unknown Weft species '{name}'", nameof(name)),
        };

    internal static WeftProgram CreateFinite(string name, string note, string source, byte[] operations, int fuel)
    {
        Symbol[] start = new Symbol[operations.Length];
        for (int i = 0; i < operations.Length; i++) start[i] = Symbol.Terminal(operations[i]);
        return CreateProgram(name, note, source, start, [], Math.Max(1, fuel), []);
    }

    private static WeftProgram CreateLoop(string name, string note, string body, int blockBudget)
    {
        Symbol[] ops = Bios.Parse(body);
        Symbol[] pattern = new Symbol[ops.Length + 1];
        Array.Copy(ops, pattern, ops.Length);
        pattern[^1] = new Symbol(Symbol.FirstNonterminal);
        GrammarRule[] rules = [CreateRuleFromPattern(pattern)];
        int iterations = Math.Max(2, blockBudget / Math.Max(1, ops.Length));
        int fuel = iterations * (ops.Length + 1);
        return CreateProgram(name, note, body + " N256(self)", [new Symbol(Symbol.FirstNonterminal)], rules, fuel, ["loop-body"]);
    }

    private static WeftProgram CreateCross(string name, string source, bool reordered)
    {
        Symbol CreateNonterminal(int i) => new(Symbol.FirstNonterminal + (uint)i);
        Symbol CreateOperation(Opcodes op) => new((uint)(byte)op);

        const string sharedFold = "1 : + 1 : + 1 : + *";
        const string sharedMap = "0 1 + : + 1 + : +";
        const string privateA = "1 1 + : * 1 +";
        const string privateB = "1 : + : + 1 +";
        const string privateC = "0 1 + 1 + : *";
        const string dormant = "0 0 0 0 0 0 0 0";

        string priv = (GetSourceIndex(source) % 3) switch
        {
            0 => privateA,
            1 => privateB,
            _ => privateC,
        };
        GrammarRule[] rules = [CreateRuleFromOperations(sharedFold), CreateRuleFromOperations(sharedMap), CreateRuleFromOperations(priv), CreateRuleFromOperations(dormant)];
        Symbol[] start = reordered
            ? [CreateNonterminal(1), CreateNonterminal(0), CreateNonterminal(2), CreateOperation(Opcodes.Add)]
            : [CreateNonterminal(0), CreateNonterminal(1), CreateNonterminal(2)];
        return CreateProgram(name, "calls shared rules + one source-private body; dormant rule is authored but unfired", start, rules,
            CountDagFuel(start, rules), ["shared-fold", "shared-map", "private", "dormant"]);
    }

    private static int GetSourceIndex(string source)
    {
        if (source.StartsWith("node", StringComparison.Ordinal) && int.TryParse(source.AsSpan(4), out int i)) return i;
        return 0;
    }

    private static WeftProgram CreateProgram(string name, string note, string source, Symbol[] start, GrammarRule[] rules, int fuel, string[] ruleNames)
        => CreateProgram(name, note, start, rules, fuel, ruleNames, source);

    private static WeftProgram CreateProgram(string name, string note, Symbol[] start, GrammarRule[] rules, int fuel, string[] ruleNames)
        => CreateProgram(name, note, start, rules, fuel, ruleNames, RenderProgram(start, rules));

    private static WeftProgram CreateProgram(string name, string note, Symbol[] start, GrammarRule[] rules, int fuel, string[] ruleNames, string source)
        => new(name, note, source, start, rules, fuel, ruleNames, ReadDirectTerminalBodies(rules));

    private static GrammarRule CreateRuleFromOperations(string ops) => CreateRuleFromPattern(Bios.Parse(ops));

    private static GrammarRule CreateRuleFromPattern(params Symbol[] pattern)
        => new(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));

    private static byte[][] ReadDirectTerminalBodies(GrammarRule[] rules)
    {
        byte[][] bodies = new byte[rules.Length][];
        for (int i = 0; i < rules.Length; i++)
        {
            List<byte> body = new(rules[i].Pattern.Length);
            foreach (Symbol sym in rules[i].Pattern)
                if (sym.Value < Symbol.FirstNonterminal) body.Add((byte)sym.Value);
            bodies[i] = body.ToArray();
        }
        return bodies;
    }

    private static int CountDagFuel(ReadOnlySpan<Symbol> start, GrammarRule[] rules)
    {
        int fuel = 0;
        foreach (Symbol sym in start) fuel += CountSymbolFuel(sym, rules);
        return fuel;
    }

    private static int CountSymbolFuel(Symbol sym, GrammarRule[] rules)
    {
        if (sym.Value < Symbol.FirstNonterminal) return 1;
        int r = (int)(sym.Value - Symbol.FirstNonterminal);
        int fuel = 1;
        foreach (Symbol child in rules[r].Pattern) fuel += CountSymbolFuel(child, rules);
        return fuel;
    }

    private static string RenderProgram(ReadOnlySpan<Symbol> start, GrammarRule[] rules)
    {
        byte[] trace = ReconstructFinite(start, rules, maxSymbols: 96);
        return Bios.Render(trace);
    }

    private static byte[] ReconstructFinite(ReadOnlySpan<Symbol> start, GrammarRule[] rules, int maxSymbols)
    {
        List<byte> outp = new(maxSymbols);
        Stack<Symbol> stack = new();
        for (int i = start.Length - 1; i >= 0; i--) stack.Push(start[i]);
        while (stack.Count > 0 && outp.Count < maxSymbols)
        {
            Symbol sym = stack.Pop();
            if (sym.Value < Symbol.FirstNonterminal) { outp.Add((byte)sym.Value); continue; }
            int r = (int)(sym.Value - Symbol.FirstNonterminal);
            Symbol[] pat = rules[r].Pattern;
            for (int i = pat.Length - 1; i >= 0; i--)
            {
                if (pat[i].Value == sym.Value) continue;   // self-call is the Fuel-bounded loop edge, not finite source text
                stack.Push(pat[i]);
            }
        }
        return outp.ToArray();
    }
}

public readonly record struct WeftProgram(string Name, string Note, string Source, Symbol[] Start, GrammarRule[] Rules,
    int Fuel, string[] RuleNames, byte[][] DirectRuleBodies);
