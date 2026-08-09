namespace Cogito.Cli;

using System.CommandLine;
using Cogito.Exec;

// ── EXEC / WHORL-C COMMANDS ──  the pinned v0 tape-VM verb. `probe
// tapevm` runs concatenative tape programs under Fuel, emits the flat opcode-byte execution trace, and induces
// the DOUBLING TOWER (Exp-1's pre-recursive form). `--exp2` runs the differential oracle; `--exp3` runs sourced
// execution traces through Pearl's cross-reflection law. TYPED-CALL into the Exp* bodies (no argv round-trip).
// Registered by CliRoot under the `probe` cluster, beside the other structural probes.
internal static class ExecCommands
{
    internal static Command TapeVm()
    {
        var program = new Option<string?>("--program") { Description = "a v0 op-string to run (0 1 + - * < : \\ _ ?); omitted ⇒ the Exp-1 tower corpus" };
        var loop    = new Option<bool>("--loop")        { Description = "wrap --program as a self-referential loop rule R = BODY R (Fuel-bounded)" };
        var fuel    = new Option<int?>("--fuel")        { Description = "step budget — one unit per VM step, per symbol popped (default 2000)" };
        var top     = new Option<int?>("--top")         { Description = "top rules to list for a single --program (default 12)" };
        var exp2    = new Option<bool>("--exp2")        { Description = "Exp-2: expand-as-execute vs the held-out differential oracle" };
        var exp3    = new Option<bool>("--exp3")        { Description = "Exp-3: sourced execution traces on the tape vs the same-source null" };

        var cmd = new Command("tapevm", "the pinned v0 tape-VM — Exp-1 tower, Exp-2 oracle, Exp-3 execution-grade cross-reflection")
        {
            program, loop, fuel, top, exp2, exp3
        };
        cmd.SetAction(parse =>
        {
            bool runExp2 = parse.GetValue(exp2);
            bool runExp3 = parse.GetValue(exp3);
            if (runExp2 && runExp3) { Console.Error.WriteLine("  choose one: --exp2 or --exp3"); return 1; }
            if ((runExp2 || runExp3) && parse.GetValue(program) is { Length: > 0 })
            {
                Console.Error.WriteLine("  --program belongs to the Exp-1 custom runner; --exp2/--exp3 use their pre-registered held-out corpora");
                return 1;
            }
            int t = parse.GetValue(top) ?? 12;
            if (runExp2) return Exp2.Run(t);
            if (runExp3) return Exp3.Run(t);
            return Exp1.Run(parse.GetValue(program), parse.GetValue(loop), parse.GetValue(fuel) ?? 2000, t);
        });
        return cmd;
    }
}
