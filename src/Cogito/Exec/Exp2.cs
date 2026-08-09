namespace Cogito.Exec;

using Cogito.Grammar;

// EXP-2 - the expand-as-execute differential oracle. These cases are deliberately held out from
// Exp-1's tower corpus: no self-recursive loop rules, several DAG calls, and both arms of `?`.
// The oracle is not "flatten then run": `?` consumes the next continuation SYMBOL, so skipping a
// nonterminal skips the call before expansion. The reference runner below uses a queue and inserts
// rule bodies into that queue; TapeVm uses a stack. Same semantics, different mechanism.
public static class Exp2
{
    public static int Run(int top)
    {
        Console.WriteLine("tapevm Exp-2 - expand-as-execute differential oracle");
        Console.WriteLine("  held-out DAG programs · every executed opcode must satisfy MinReq before the oracle compares bytes");
        Console.WriteLine();

        int pass = 0;
        Case[] cases = CreateCases();
        foreach (Case c in cases)
        {
            ExecResult vm = new TapeVm(c.Rules).Run(c.Start, c.Fuel);
            OracleResult oracle = Oracle.Run(c.Start, c.Rules, c.Fuel);
            bool same = oracle.Fed
                     && vm.Halted == oracle.Halted
                     && vm.FuelLeft == oracle.FuelLeft
                     && vm.Trace.AsSpan().SequenceEqual(oracle.Trace)
                     && vm.Data.AsSpan().SequenceEqual(oracle.Data);
            if (same) pass++;

            Console.WriteLine($"● {c.Name,-14} {c.Note}");
            Console.WriteLine($"    program  {RenderStart(c.Start)}  rules={c.Rules.Length}  fuel={c.Fuel}");
            Console.WriteLine($"    vm       trace {vm.Trace.Length}B {Bios.Render(vm.Trace, 64)} · top {vm.DataTop} · halted={(vm.Halted ? "yes" : "no")} · fuelLeft {vm.FuelLeft}");
            Console.WriteLine($"    oracle   trace {oracle.Trace.Length}B {Bios.Render(oracle.Trace, 64)} · top {ReadTop(oracle.Data)} · halted={(oracle.Halted ? "yes" : "no")} · fuelLeft {oracle.FuelLeft} · fed={(oracle.Fed ? "yes" : "NO")}");
            PrintJournal(vm, top);
            Console.WriteLine($"    verdict  {(same ? "✓ BYTE-IDENTICAL" : "✗ DIVERGENCE")}");
            Console.WriteLine();
        }

        bool ok = pass == cases.Length;
        Console.WriteLine($"  GATE  Exp-2 differential oracle: {(ok ? "PASS" : "FAIL")} - {pass}/{cases.Length} held-out programs reproduced byte-for-byte");
        return ok ? 0 : 1;
    }

    private static Case[] CreateCases()
    {
        Symbol CreateNonterminal(int index) => new(Symbol.FirstNonterminal + (uint)index);
        GrammarRule CreateRuleFromOperations(string operations) => CreateRuleFromPattern(Bios.Parse(operations));
        GrammarRule CreateRuleFromPattern(params Symbol[] pattern)
            => new(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));

        return
        [
            new Case("straight", "arithmetic with duplication, no authored rules",
                Bios.Parse("1 1 + : * 1 +"), [], 64),

            new Case("dag-square", "two nonterminal calls into a fed arithmetic tail",
                [CreateNonterminal(1), new Symbol((byte)Opcodes.One), new Symbol((byte)Opcodes.Add)],
                [CreateRuleFromOperations("1 1 +"), CreateRuleFromPattern(CreateNonterminal(0), new Symbol((byte)Opcodes.Dup), new Symbol((byte)Opcodes.Mul))], 96),

            new Case("cond-take", "`?` keeps the next terminal continuation item",
                Bios.Parse("1 1 ? : 1 +"), [], 64),

            new Case("cond-skip", "`?` discards the next terminal continuation item",
                Bios.Parse("1 0 ? _ 1 +"), [], 64),

            new Case("nested", "a parent calls the same child twice before a reducer",
                [CreateNonterminal(1), new Symbol((byte)Opcodes.Dup), new Symbol((byte)Opcodes.Add)],
                [CreateRuleFromOperations("1 : +"), CreateRuleFromPattern(CreateNonterminal(0), CreateNonterminal(0), new Symbol((byte)Opcodes.Mul))], 128),

            new Case("cond-rule", "conditional inside a called rule, then called twice",
                [CreateNonterminal(1)],
                [CreateRuleFromOperations("1 0 ? _ 1 +"), CreateRuleFromPattern(CreateNonterminal(0), CreateNonterminal(0), new Symbol((byte)Opcodes.Add))], 128),

            new Case("float-add", "explicit conversion and float arithmetic",
                Bios.Parse("1 F 1 F A"), [], 64),

            new Case("float-invalid", "integer arithmetic rejects a float operand without coercion",
                Bios.Parse("1 F 1 +"), [], 64),

            new Case("float-nan", "IEEE NaN is canonicalized on the typed data plane",
                Bios.Parse("0 F 0 F /"), [], 64),
        ];
    }

    private static void PrintJournal(in ExecResult vm, int top)
    {
        FuelJournalRow[] rows = vm.FuelJournal.TopRows(Math.Max(1, Math.Min(top, 4)));
        if (rows.Length == 0)
        {
            Console.WriteLine("    journal   no authored rule spent Fuel");
            return;
        }
        Console.Write("    journal   ");
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) Console.Write(" · ");
            FuelJournalRow r = rows[i];
            Console.Write($"N{Symbol.FirstNonterminal + (uint)r.Rule}: calls {r.Calls}, body {r.BodyFuel}, leaf {r.LeafFuel}, {r.BodyFuelPerCall:0.0}/call");
        }
        Console.WriteLine();
    }

    private static string RenderStart(Symbol[] start)
    {
        string[] parts = new string[start.Length];
        for (int i = 0; i < start.Length; i++) parts[i] = start[i].IsTerminal ? ((char)start[i].Value).ToString() : $"N{start[i].Value}";
        return string.Join(' ', parts);
    }

    private static WeftNumber ReadTop(WeftNumber[] data) => data.Length == 0 ? WeftNumber.Zero : data[^1];

    private readonly record struct Case(string Name, string Note, Symbol[] Start, GrammarRule[] Rules, int Fuel);

    private readonly record struct OracleResult(byte[] Trace, WeftNumber[] Data, int FuelLeft, bool Halted, bool Fed);

    private static class Oracle
    {
        public static OracleResult Run(Symbol[] start, GrammarRule[] rules, int fuelBudget)
        {
            List<Symbol> cont = new(start);
            List<byte> trace = new();
            DataStack ds = new();
            Fuel fuel = new(fuelBudget);
            bool fed = true;
            int ip = 0;

            while (ip < cont.Count)
            {
                if (!fuel.TrySpend()) break;
                Symbol s = cont[ip++];
                if (s.IsNonterminal)
                {
                    int r = (int)(s.Value - Symbol.FirstNonterminal);
                    if ((uint)r >= (uint)rules.Length) throw new InvalidOperationException($"oracle: N{s.Value} names rule {r} of {rules.Length}");
                    Symbol[] body = rules[r].Pattern;
                    cont.InsertRange(ip, body);
                    continue;
                }

                Opcodes op = (Opcodes)(byte)s.Value;
                StackEffect effect = Bios.Effect(op);
                if (ds.Count < effect.MinReq) fed = false;
                if (op == Opcodes.Cond)
                {
                    WeftNumber predicate = ds.Pop();
                    if (!predicate.ReadsTrue() && ip < cont.Count) ip++;
                }
                else Exec(op, ds);
                trace.Add((byte)s.Value);
            }

            return new OracleResult(trace.ToArray(), ds.Snapshot(), fuel.Remaining, ip >= cont.Count, fed);
        }

        private static void Exec(Opcodes op, DataStack ds)
        {
            switch (op)
            {
                case Opcodes.Zero: ds.Push(WeftNumber.Zero); break;
                case Opcodes.One: ds.Push(WeftNumber.One); break;
                case Opcodes.Add: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.AddIntegers(a, b)); break; }
                case Opcodes.Sub: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.SubtractIntegers(a, b)); break; }
                case Opcodes.Mul: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.MultiplyIntegers(a, b)); break; }
                case Opcodes.Lt: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareIntegersLessThan(a, b)); break; }
                case Opcodes.ToFloat: ds.Push(ds.Pop().ConvertToFloat64()); break;
                case Opcodes.FloatAdd: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.AddFloats(a, b)); break; }
                case Opcodes.FloatSub: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.SubtractFloats(a, b)); break; }
                case Opcodes.FloatMul: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.MultiplyFloats(a, b)); break; }
                case Opcodes.FloatDiv: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.DivideFloats(a, b)); break; }
                case Opcodes.FloatLt: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsLessThan(a, b)); break; }
                case Opcodes.FloatLe: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsLessThanOrEqual(a, b)); break; }
                case Opcodes.FloatGt: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsGreaterThan(a, b)); break; }
                case Opcodes.FloatGe: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsGreaterThanOrEqual(a, b)); break; }
                case Opcodes.FloatEq: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsEqual(a, b)); break; }
                case Opcodes.Dup:  ds.Push(ds.Peek()); break;
                case Opcodes.Swap: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(b); ds.Push(a); break; }
                case Opcodes.Drop: ds.Pop(); break;
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        private sealed class DataStack
        {
            private WeftNumber[] _a = new WeftNumber[32];
            private int _n;

            public int Count => _n;

            public void Push(WeftNumber value)
            {
                if (_n == _a.Length) Array.Resize(ref _a, _a.Length * 2);
                _a[_n++] = value;
            }

            public WeftNumber Pop() => _n > 0 ? _a[--_n] : WeftNumber.Zero;
            public WeftNumber Peek() => _n > 0 ? _a[_n - 1] : WeftNumber.Zero;
            public WeftNumber[] Snapshot() => _a[.._n];
        }
    }
}
