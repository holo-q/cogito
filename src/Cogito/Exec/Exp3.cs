namespace Cogito.Exec;

using Cogito.Grammar;

// EXP-3 - execution-grade cross exercise. Static pooling died because co-membership is not use.
// This experiment appends only EXECUTED opcode traces to the tape. If a peer's trace calls the
// same body, Re-Pair rediscovers that byte span and Pearl's existing cross-reflection law can vest
// it. The same-source null keeps the exact trace multiset but collapses every source label.
public static class Exp3
{
    private const int WScale = 8;

    public static int Run(int top)
    {
        Console.WriteLine("tapevm Exp-3 - sourced execution traces -> Pearl cross-reflection");
        Console.WriteLine("  event standard: only fired VM traces land on the tape; dormant authored rules never count");
        Console.WriteLine();

        var real = RunArm("cross-source", sameSource: false);
        var nul = RunArm("same-source null", sameSource: true);

        PrintArm(real, top);
        PrintArm(nul, top);

        double gap = real.ReflectionRate - nul.ReflectionRate;
        bool pass = real.Reflected > 0 && nul.Reflected == 0 && real.DirectCrossRules > 0 && gap >= 0.20;
        Console.WriteLine("── kill-line ───────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  reflection rate  real {Pct(real.ReflectionRate),6}  vs  same-source null {Pct(nul.ReflectionRate),6}  gap {Pct(gap),6}");
        Console.WriteLine($"  GATE  execution-grade cross-exercise: {(pass ? "PASS" : "FAIL")} - {(pass ? "beats" : "does not beat")} the same-source null");
        return pass ? 0 : 1;
    }

    private static ArmResult RunArm(string name, bool sameSource)
    {
        var tape = new Tape();
        var traces = new List<TraceRow>();
        foreach (var p in Programs())
        {
            var exec = new TapeVm(p.Rules).Run(p.Start, p.Fuel);
            string source = sameSource ? "node0" : p.Source;
            tape.Append(exec.Trace, source, Provenances.Replay);
            traces.Add(new TraceRow(p.Name, source, p.Note, exec, p.RuleNames));
        }

        var (_, _, g) = Engine.Induce(tape);
        var audit = Pearl.Audit(tape, g, WScale, crossReflect: true);
        int eligible = 0, unionCross = 0, directCross = 0;
        for (int r = 0; r < g.Rules.Length; r++)
        {
            if (audit.ExpLen[r] < Pearl.ReflectFloorBytes) continue;
            eligible++;
            if (audit.JewelSources?[r] is { Count: >= 2 }) unionCross++;
            if (audit.JewelCountsDirect?[r] is { Count: >= 2 }) directCross++;
        }

        var journal = new Journal();
        int reflected = Pearl.Corroborate(audit, tape, journal, step: 3);
        return new ArmResult(name, traces.ToArray(), g.Rules.Length, eligible, unionCross, directCross, reflected, tape.ReplayCount);
    }

    private static TraceProgram[] Programs()
    {
        Symbol N(int i) => new(Symbol.FirstNonterminal + (uint)i);
        Symbol Op(Opcodes op) => new((uint)(byte)op);
        GrammarRule RuleOps(string ops) => RulePat(Bios.Parse(ops));
        GrammarRule RulePat(params Symbol[] pattern)
            => new(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));

        const string sharedFold = "1 : + 1 : + 1 : + *";
        const string sharedMap = "0 1 + : + 1 + : +";
        const string privateA = "1 1 + : * 1 +";
        const string privateB = "1 : + : + 1 +";
        const string privateC = "0 1 + 1 + : *";
        const string dormant = "0 0 0 0 0 0 0 0";

        TraceProgram Cross(string name, string source, string priv, Symbol[] start)
        {
            var rules = new[]
            {
                RuleOps(sharedFold),
                RuleOps(sharedMap),
                RuleOps(priv),
                RuleOps(dormant),
            };
            return new TraceProgram(name, source, "calls shared rules + one private body; dormant boilerplate rule is authored but never fired", start, rules, 256,
                ["shared-fold", "shared-map", "private", "dormant"]);
        }

        TraceProgram Private(string name, string source, string priv)
        {
            var rules = new[]
            {
                RuleOps(sharedFold),
                RuleOps(sharedMap),
                RuleOps(priv),
                RuleOps(dormant),
            };
            return new TraceProgram(name, source, "private-only control; shared and dormant rules are present but not fired", [N(2), N(2), Op(Opcodes.Add)], rules, 128,
                ["shared-fold", "shared-map", "private", "dormant"]);
        }

        return
        [
            Cross("node0-cross-a", "node0", privateA, [N(0), N(1), N(2)]),
            Cross("node1-cross-a", "node1", privateB, [N(0), N(1), N(2)]),
            Cross("node2-cross-a", "node2", privateC, [N(0), N(2), N(1)]),
            Cross("node1-cross-b", "node1", privateB, [N(1), N(0), N(2)]),
            Private("node0-private", "node0", privateA),
            Private("node2-private", "node2", privateC),
        ];
    }

    private static void PrintArm(in ArmResult arm, int top)
    {
        Console.WriteLine($"── {arm.Name} ───────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  tape      {arm.Traces.Length} dream trace spans · reflected {arm.Reflected}/{arm.Replays} ({Pct(arm.ReflectionRate)})");
        Console.WriteLine($"  grammar   {arm.Rules} rules · eligible {arm.EligibleRules} · cross rules direct {arm.DirectCrossRules} / union {arm.UnionCrossRules}");
        Console.WriteLine("  traces");
        foreach (var t in arm.Traces)
        {
            Console.WriteLine($"    {t.Source,-5} {t.Name,-15} trace {t.Exec.Trace.Length,2}B · top {t.Exec.DataTop,3} · {Bios.Render(t.Exec.Trace, 48)}");
            var rows = t.Exec.FuelJournal.TopRows(Math.Max(1, Math.Min(top, 4)));
            Console.Write("          journal ");
            if (rows.Length == 0) Console.Write("no fired authored rules");
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) Console.Write(" · ");
                var r = rows[i];
                Console.Write($"{t.RuleNames[r.Rule]}: calls {r.Calls}, body {r.BodyFuel}, leaf {r.LeafFuel}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    private static string Pct(double x) => x.ToString("P1", System.Globalization.CultureInfo.InvariantCulture);

    private readonly record struct TraceProgram(string Name, string Source, string Note, Symbol[] Start, GrammarRule[] Rules, int Fuel, string[] RuleNames);
    private readonly record struct TraceRow(string Name, string Source, string Note, ExecResult Exec, string[] RuleNames);
    private readonly record struct ArmResult(string Name, TraceRow[] Traces, int Rules, int EligibleRules, int UnionCrossRules, int DirectCrossRules, int Reflected, int Replays)
    {
        public double ReflectionRate => Replays == 0 ? 0 : (double)Reflected / Replays;
    }
}
