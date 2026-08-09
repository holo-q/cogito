namespace Cogito.Exec;

using Cogito.Grammar;
using Cogito.Induct;

// EXP-1 — "the loop body crystallizes as a rule". The corpus threads between the two failure modes:
//   • operands-IN-the-code-plane make a tower IMPOSSIBLE (iterations differ byte-wise, no tower). Here the ONLY
//     literals are 0/1 and every real value is COMPUTED, so the growing accumulator NEVER touches the trace —
//     the operands stay on the data stack, off the code plane.
//   • a LONE BODY^N corpus makes the tower TAUTOLOGICAL. Here several DISTINCT real programs each loop under
//     Fuel, so the induced grammar carries MULTIPLE independent tower families — not one trivial repetition.
// The proof is the divergence: each loop's data stack races off (final top = f(N)) while its trace stays
// byte-identical per iteration and therefore TOWERS. Feed the trace through Engine.InduceTokens and the
// doubling tower (BODY, BODY², BODY⁴ …) appears; the straight-line control, having no repetition, does not.
public static class Exp1
{
    /// The verb body. No `programSrc` ⇒ run the Exp-1 corpus + the full tower census (the gate). A `programSrc`
    /// runs one custom op-string (looped as `R = BODY R` under `--loop`, else straight-line) and dumps its trace
    /// + induction + tower — the "run a program, emit the flat trace" surface.
    public static int Run(string? programSrc, bool loop, int fuel, int top)
    {
        Console.WriteLine("tapevm — the pinned v0 tape-VM · run under Fuel, emit the flat opcode trace, induce the doubling tower");
        Console.WriteLine($"  BIOS  0 1 + - * < : \\ _ ?  + nonterminal-CALL + Fuel   ·   trace = flat opcode-byte stream, operands off the code plane");
        Console.WriteLine();

        if (!string.IsNullOrEmpty(programSrc))
        {
            TapeProgram prog;
            try { prog = loop ? Loop("custom", "user op-string, looped R = BODY R", programSrc, fuel) : Line("custom", "user op-string, straight-line", programSrc, fuel); }
            catch (ArgumentException ex) { Console.Error.WriteLine($"  bad program: {ex.Message}"); return 1; }
            RunOne(prog, top, verbose: true);
            return 0;
        }

        // ── the Exp-1 corpus: four looping programs (varied bodies + periods) + one straight-line control ──
        var corpus = Corpus(fuel);
        var allTrace = new List<byte>();
        int towering = 0, loops = 0;
        foreach (var prog in corpus)
        {
            var res = RunOne(prog, top, verbose: false);
            if (prog.Rules.Length > 0) { loops++; if (res.Census.MaxHeight >= 1) towering++; }
            allTrace.AddRange(res.Exec.Trace);
        }

        // ── the aggregate census: induce the whole corpus trace at once — the tower families must COEXIST (the
        //    non-tautology proof: one grammar carrying every loop's tower, not a single repeated body) ──
        var (_, gAll) = Engine.InduceTokens(AsTokens(allTrace));
        var cenAll = CountSlots.Summarize(CountSlots.Scan(gAll.Rules, gAll.AlphabetSize));
        var rsAll = Engine.RenormStats(gAll);
        Console.WriteLine("── corpus census (all traces, one grammar) ─────────────────────────────────────");
        Console.WriteLine($"  induce   {gAll.Rules.Length} rules · scales {rsAll.Scales} · meanz {Fmt(rsAll.MeanZ)} · maxSpan {rsAll.MaxSpan}");
        Console.WriteLine($"  towers   {cenAll.Towers} towers · max height {cenAll.MaxHeight} (deepest covers {cenAll.DeepestSpan}B)");
        Console.WriteLine();

        bool pass = towering == loops && loops > 0;
        Console.WriteLine($"  GATE  doubling-tower proof: {(pass ? "PRESENT" : "ABSENT")} — {towering}/{loops} looping programs towered; straight-line control flat");
        return pass ? 0 : 1;
    }

    // ── one program: run → emit trace → induce → census, printing the proof line(s) ──
    private static ProgResult RunOne(in TapeProgram prog, int top, bool verbose)
    {
        var exec = new TapeVm(prog.Rules).Run(prog.Start, prog.Fuel);
        var (_, g) = Engine.InduceTokens(AsTokens(exec.Trace));
        var cen = CountSlots.Summarize(CountSlots.Scan(g.Rules, g.AlphabetSize));   // Lane 1's shared [X,X] doubling-tower scanner
        var rs = Engine.RenormStats(g);

        Console.WriteLine($"● {prog.Name,-13} {prog.Note}");
        Console.WriteLine($"    program  {prog.Source}");
        Console.WriteLine($"    fuel     {prog.Fuel} → trace {exec.Trace.Length}B · halted={(exec.Halted ? "yes" : "no")} · data-top {exec.DataTop} · depth {exec.Data.Length} (the operands — on the data stack, never the trace)");
        PrintJournal(exec, prog.Rules, take: verbose ? Math.Min(top, 6) : 2);
        Console.WriteLine($"    trace    {Bios.Render(exec.Trace, 56)}");
        Console.WriteLine($"    induce   {g.Rules.Length} rules · scales {rs.Scales} · meanz {Fmt(rs.MeanZ)} · maxSpan {rs.MaxSpan}");
        Console.WriteLine(cen.MaxHeight >= 1
            ? $"    tower    ✓ height {cen.MaxHeight} · BODY {cen.DeepestSpan >> cen.MaxHeight}B · {cen.Towers} tower(s) · covers {cen.DeepestSpan}B (BODY,BODY²,…,BODY^{1 << cen.MaxHeight})"
            : $"    tower    · none ({cen.Towers} towers) — the straight-line form the knot thesis predicts SHOULD stay flat");
        if (verbose && top > 0) DumpTopRules(g, DenseInverse(exec.Trace), top);
        Console.WriteLine();
        return new ProgResult(exec, cen);
    }

    private static void PrintJournal(in ExecResult exec, GrammarRule[] rules, int take)
    {
        if (rules.Length == 0)
        {
            Console.WriteLine("    journal   no authored rules (all Fuel is top-level)");
            return;
        }
        var rows = exec.FuelJournal.TopRows(take);
        if (rows.Length == 0)
        {
            Console.WriteLine("    journal   no rule body spent Fuel");
            return;
        }
        Console.Write("    journal   ");
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) Console.Write(" · ");
            var row = rows[i];
            Console.Write($"N{Symbol.FirstNonterminal + (uint)row.Rule}: calls {row.Calls}, body {row.BodyFuel}, leaf {row.LeafFuel}, {row.BodyFuelPerCall:0.0}/call");
        }
        long unowned = exec.Trace.Length + exec.FuelJournal.TotalCalls - exec.FuelJournal.TotalBodyFuel;
        Console.WriteLine($" · totals body {exec.FuelJournal.TotalBodyFuel}, calls {exec.FuelJournal.TotalCalls}, unowned {unowned}");
    }

    // Render the top induced rules as op-strings — the doubling tower made LEGIBLE. InduceTokens DENSIFIES the
    // trace alphabet (opcode bytes → dense ids in first-appearance order), so an expanded rule yields dense-id
    // bytes; `denseToOp` inverts the densify to recover the opcode mnemonics, so the reader sees "1+", "1+1+1+1+",
    // … (BODY, BODY², BODY⁴) rather than raw dense bytes.
    private static void DumpTopRules(in RePairResult g, ReadOnlySpan<byte> denseToOp, int top)
    {
        int n = Math.Min(top, g.Rules.Length);
        Console.WriteLine($"    rules    (first {n} by mint order — each is one squaring of the tower)");
        for (int i = 0; i < n; i++)
        {
            var dense = ExpandDense(g, i, 40);
            for (int k = 0; k < dense.Length; k++) dense[k] = denseToOp[dense[k]];
            Console.WriteLine($"      N{g.AlphabetSize + (uint)i}  = {Bios.Render(dense, 40)}");
        }
    }

    // Expand a dense-alphabet rule to its terminal DENSE-ID bytes, boundary = g.AlphabetSize (NOT the 256-byte
    // Symbol.FirstNonterminal). Reconstruct.Expand hardcodes 256, so it silently mis-reads every InduceTokens
    // (densified) grammar as all-terminal — a latent trap flagged for the kernel, sidestepped here. `maxBytes`
    // caps the yield so a deep tower node doesn't materialize its full 2^depth extent for one report line.
    private static byte[] ExpandDense(in RePairResult g, int ruleIdx, int maxBytes)
    {
        var outp = new List<byte>();
        var stack = new Stack<uint>();
        stack.Push(g.AlphabetSize + (uint)ruleIdx);
        while (stack.Count > 0 && outp.Count < maxBytes)
        {
            uint v = stack.Pop();
            if (v < g.AlphabetSize) { outp.Add((byte)v); continue; }
            var pat = g.Rules[(int)(v - g.AlphabetSize)].Pattern;
            for (int k = pat.Length - 1; k >= 0; k--) stack.Push(pat[k].Value);
        }
        return outp.ToArray();
    }

    // ── the corpus ──  four loops with varied bodies + trace periods (2, 2, 4, 3 — the odd period proves towers
    //    are not a power-of-2 artifact), plus one straight-line control that MUST stay flat.
    private static TapeProgram[] Corpus(int fuel) =>
    [
        Loop("accumulate", "push1 add — a running counter; data 1,2,3,…",           "1 +",     fuel),
        Loop("countdown",  "push1 sub — monotone descent; data −1,−2,−3,…",          "1 -",     fuel),
        Loop("even",       "push1 dup add add — data 2,4,6,… (period-4 body)",       "1 : + +", fuel),
        Loop("triad",      "push0 push1 add — period-3 body (odd → non-power-of-2)", "0 1 +",   fuel),
        Line("line",       "dup mul push1 add drop — NO loop ⇒ NO tower (control)",  ": * 1 + _", 64),
    ];

    // `R = BODY R` — a self-referential loop rule (rule index 0); start = the single call into it. Legal to
    // AUTHOR + EXECUTE (Fuel bounds the unroll) though Re-Pair could never MINT it (DAG-only).
    private static TapeProgram Loop(string name, string note, string body, int fuel)
    {
        var ops = Bios.Parse(body);
        var pattern = new Symbol[ops.Length + 1];
        Array.Copy(ops, pattern, ops.Length);
        pattern[^1] = new Symbol(Symbol.FirstNonterminal);                       // self-call: rule index 0
        var rule = new GrammarRule(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));
        // Snap Fuel DOWN to a whole number of iterations (one step per body op + the self-call): the trace is then
        // a clean BODY^N with no partial tail — the tower squares evenly, and data-top lands on the true diverged
        // accumulator instead of a dangling mid-body push.
        int perIter = ops.Length + 1;
        int snapped = Math.Max(perIter, fuel - fuel % perIter);
        return new TapeProgram(name, note, body + " N256(self)", [new Symbol(Symbol.FirstNonterminal)], [rule], snapped);
    }

    // A straight-line program — no rules, the start sequence IS the whole program. The negative control: a trace
    // with no repetition cannot tower.
    private static TapeProgram Line(string name, string note, string ops, int fuel) =>
        new(name, note, ops, Bios.Parse(ops), [], fuel);

    private static uint[] AsTokens(IReadOnlyList<byte> trace)
    {
        var t = new uint[trace.Count];
        for (int i = 0; i < t.Length; i++) t[i] = trace[i];
        return t;
    }

    /// The inverse of InduceTokens' densify: dense id (first-appearance order over the trace) → original opcode
    /// byte. Rebuilt from the trace with the identical first-appearance walk, so `denseToOp[denseId]` recovers the
    /// mnemonic an expanded induced rule flattens to.
    private static byte[] DenseInverse(ReadOnlySpan<byte> trace)
    {
        var seen = new Dictionary<byte, byte>();
        var inv = new List<byte>();
        foreach (byte b in trace)
            if (seen.TryAdd(b, (byte)inv.Count)) inv.Add(b);
        return inv.ToArray();
    }

    private static string Fmt(double d) => double.IsNaN(d) ? "  n/a" : d.ToString("+0.000;-0.000", System.Globalization.CultureInfo.InvariantCulture);

    // An authored v0 program: the display `Source`, the `Start` sequence the VM enters at, its (possibly
    // self-referential) `Rules` indexed in emission order, and the Fuel budget it runs under.
    private readonly record struct TapeProgram(string Name, string Note, string Source, Symbol[] Start, GrammarRule[] Rules, int Fuel);

    private readonly record struct ProgResult(ExecResult Exec, CountSlots.Census Census);
}
