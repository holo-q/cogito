namespace Cogito.Cli;

using System.CommandLine;
using Cogito;   // the external verb bodies: Navigate · NavDyn · NavLoop · AgentTrace · Corpus (all public static Run)

// ── AGENT-NAV COMMANDS ──  the localization / lifelong-eval loops + the trace/corpus substrate. Each verb is
// ADAPTER-ARGV: a typed Option<T>/Argument<T> surface over its flags, and a SetAction that rebuilds the minimal
// argv the stable-API body (X.Run(string[])) expects. AOT-safe: the EXPLICIT api, every value pulled by hand.
//
// Registration is split by CliRoot: navigate/navdyn/navloop ride TOP-LEVEL, DEFERRED until the unified `nav`
// one `nav --mode`; traces/corpus land under the `tape` cluster.
//
// THE CLUSTER — five verbs sharing a <dir> positional + a fistful of --no-* ablation switches:
//   navigate  the localization loop (v1) — the widest: the loop knobs + the cov-beacon sub-flags + the RunAll sub-flags,
//             with --all a MODE-SWITCH routing to the batch driver.
//   navdyn    the dynamic/lifelong eval — two learning channels, each --no-* ablatable.
//   navloop   the closed agentic loop with the value layer — the value/outcome-vest/mode switches.
//   traces    the agentic-trace substrate — MODE-SWITCHES (--selftest/--probe/--episode) route to different routines,
//             each with its own sub-flags; the default (no mode) materializes the corpus.
//   corpus    the world-pool gather — `gather` is a LITERAL sub-verb (first positional), then the gather knobs.
//
// The --no-* flags are booleans (present ⇒ true; the un-negated behavior is the default). Value-flag DEFAULTS are NOT
// set on the Option (absent ⇒ null) — the untouched body applies ITS default when the token is absent, so the argv the
// handler emits is byte-identical to a hand-typed invocation (the determinism Vow: same flags in ⇒ same run).
internal static class AgentNavCommands
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  nav — THE UNIFIED localization / lifelong / closed-loop eval (--mode frozen|dyn|loop). ONE loop read three
    //  ways over the SWE-bench-Lite plane; the mode routes to the frozen (Navigate), dynamic (NavDyn), or closed
    //  value-loop (NavLoop) driver. Natively typed onto the SCL tree — every value pulled by hand and passed straight
    //  to the mode entry (no argv rebuild). Options are grouped by mode ([frozen]/[dyn]/[loop] in the descriptions);
    //  a flag from the wrong mode is simply ignored by that mode's entry. Value-flag DEFAULTS resolve HERE from each
    //  mode's own default const (Loc.MaxLooks/KFilesPerLook, Navigate.DefaultPromoteEps, NavDyn.WGold, NavLoop.WVest)
    //  so the flag-absent behavior can't drift from the historical single-verb defaults.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    internal static Command Nav()
    {
        var dir       = new Argument<string?>("dir")           { Arity = ArgumentArity.ZeroOrOne, Description = "instance/data dir for legacy benchmark modes (omit with --repo)" };
        var repo      = new Option<string?>("--repo")          { Description = "native repository root; source enters cognition only through executed tool observations" };
        var query     = new Option<string?>("--query")         { Description = "native repository user query/instruction (the only initial intake)" };
        var repoSteps = new Option<int?>("--repo-steps")       { Description = "native repository action horizon (default 32)" };
        var repoGlob  = new Option<string?>("--repo-glob")     { Description = "native repository source glob (default Cogito source set)" };
        var repoArm   = new Option<string?>("--arm")           { Description = "[repo] G3 tool-mediation arm: tools-live (default) | tools-blocked | tools-shuffled" };
        var mode      = new Option<string>("--mode")           { Description = "frozen (navigate) | dyn (navdyn, lifelong) | loop (navloop, closed value loop)", DefaultValueFactory = _ => "frozen" };
        // shared across all modes
        var looks     = new Option<int?>("--looks")            { Description = "tool-call budget per instance (default 6)" };
        var k         = new Option<int?>("--k")                { Description = "files descended per look (default 2)" };
        var pretrain  = new Option<string?>("--pretrain")      { Description = "seed the induction from a trunk/mesh run's trained grammar (default cold)" };
        var limit     = new Option<int?>("--limit")            { Description = "cap the instance stream (default all)" };
        var ckptEvery = new Option<int?>("--checkpoint-every") { Description = "checkpoint cadence in completed instances (default 25; 0 = off)" };
        var noExpand  = new Option<bool>("--no-expand")        { Description = "ablate residual-driven expansion (all modes)" };
        // [frozen]
        var all         = new Option<bool>("--all")            { Description = "[frozen] batch-drive every instance in <dir> → runs/navigate_NNNN/" };
        var noTestPrior = new Option<bool>("--no-testprior")   { Description = "[frozen] drop the edit-site KIND prior (test-file demote)" };
        var promote     = new Option<bool>("--promote")        { Description = "[frozen] open the vesting gate's promotion channel (a surging mint may re-rank a new top-1)" };
        var promoteEps  = new Option<double?>("--promote-eps") { Description = "[frozen] margin surge a mint must produce to promote a new leader (default 0.10)" };
        var covBeacon   = new Option<bool>("--cov-beacon")     { Description = "[frozen] arm the grammar-coverage document beacon (needs --pretrain)" };
        var covWeight   = new Option<string?>("--cov-weight")  { Description = "[frozen] cov weight: vest|idf|ratio|prod (default vest; ratio/prod SUPERVISED)" };
        var covTopk     = new Option<int?>("--cov-topk")       { Description = "[frozen] cov firing-rule sparsity cap, 0 = keep all (default 0)" };
        var covScale    = new Option<double?>("--cov-scale")   { Description = "[frozen] cov bonus scale relative to BM25 top (default 1.0)" };
        var covMinlen   = new Option<int?>("--cov-minlen")     { Description = "[frozen] restrict cov to rules with ≥ N-byte expansion, 0 = all (default 0)" };
        var covDiag     = new Option<bool>("--cov-diag")       { Description = "[frozen] run the deep-rule transfer diagnostic (needs --cov-beacon)" };
        var covDump     = new Option<int?>("--cov-dump")       { Description = "[frozen] dump the first N shared issue∩gold rule expansions (default 0)" };
        // [dyn]
        var noGrammarCarry = new Option<bool>("--no-grammar-carry") { Description = "[dyn/loop] freeze channel 1 (re-seed the same base every instance)" };
        var noRankLearn    = new Option<bool>("--no-rank-learn")    { Description = "[dyn] freeze channel 2 (base BM25 only, no learned gold field)" };
        var wGold          = new Option<double?>("--wgold")         { Description = "[dyn] the learned gold-field's blend weight (default 0.5)" };
        // [loop]
        var valueLayer    = new Option<bool>("--value-layer")      { Description = "[loop] arm value-discovery descent (off by default)" };
        var outcomeVest   = new Option<bool>("--outcome-vest")     { Description = "[loop] arm gold→vest calibration (off by default)" };
        var noValue       = new Option<bool>("--no-value")         { Description = "[loop] legacy/off alias for leaving value-discovery descent frozen" };
        var noOutcomeVest = new Option<bool>("--no-outcome-vest")  { Description = "[loop] legacy/off alias for leaving gold→vest calibration frozen" };
        var supervised    = new Option<bool>("--supervised")       { Description = "[loop] MODE-1 control: gold ALWAYS evidence, no pathHit correctness gate" };
        var wVest         = new Option<double?>("--wvest")         { Description = "[loop] the outcome-vested field's blend weight (default 0.6)" };
        var passes        = new Option<int?>("--passes")           { Description = "[loop] re-drive the stream N times over one never-reset mind (default 1)" };
        var dreamBetween  = new Option<bool>("--dream-between")     { Description = "[loop] interleave an intrinsic consolidation night between passes" };
        var dreamNights   = new Option<int?>("--dream-nights")     { Description = "[loop] consolidation nights per between-pass loopback (default 4)" };

        var cmd = new Command("nav", "the localization / lifelong / closed-loop eval — one loop read three ways (--mode frozen|dyn|loop)")
        {
            dir, repo, query, repoSteps, repoGlob, repoArm, mode, looks, k, pretrain, limit, ckptEvery, noExpand,
            all, noTestPrior, promote, promoteEps, covBeacon, covWeight, covTopk, covScale, covMinlen, covDiag, covDump,
            noGrammarCarry, noRankLearn, wGold,
            valueLayer, outcomeVest, noValue, noOutcomeVest, supervised, wVest, passes, dreamBetween, dreamNights
        };
        cmd.SetAction(parse =>
        {
            string? d = parse.GetValue(dir);
            string? repoRoot = parse.GetValue(repo);
            if (!string.IsNullOrWhiteSpace(repoRoot))
                return global::Cogito.RepositoryNative.Run(repoRoot, parse.GetValue(query) ?? "", parse.GetValue(repoSteps) ?? 32,
                    parse.GetValue(repoGlob), registration: null, arm: RepositoryToolArmNames.Parse(parse.GetValue(repoArm)));
            if (string.IsNullOrWhiteSpace(d))
            {
                Console.Error.WriteLine("usage: nav <data-dir> [legacy mode flags] | nav --repo <root> --query <instruction>");
                return 1;
            }
            int    lk = parse.GetValue(looks) ?? global::Cogito.Loc.MaxLooks;
            int    kk = parse.GetValue(k) ?? global::Cogito.Loc.KFilesPerLook;
            string pt = parse.GetValue(pretrain) ?? "";
            int    lim = parse.GetValue(limit) ?? int.MaxValue;
            int    ce = parse.GetValue(ckptEvery) ?? 25;
            bool   expand = !parse.GetValue(noExpand);
            try
            {
                return parse.GetValue(mode) switch
                {
                    "dyn" or "d" => global::Cogito.NavDyn.Run(d, lk, kk, !parse.GetValue(noGrammarCarry), !parse.GetValue(noRankLearn), expand,
                                        parse.GetValue(wGold) ?? global::Cogito.NavDyn.WGold, pt, lim, ce),
                    "loop" or "l" => global::Cogito.NavLoop.Run(d, lk, kk, parse.GetValue(valueLayer) && !parse.GetValue(noValue), parse.GetValue(outcomeVest) && !parse.GetValue(noOutcomeVest), !parse.GetValue(noGrammarCarry), expand,
                                        parse.GetValue(supervised), parse.GetValue(wVest) ?? global::Cogito.NavLoop.WVest,
                                        parse.GetValue(passes) ?? 1, parse.GetValue(dreamBetween), parse.GetValue(dreamNights) ?? 4, pt, lim, ce),
                    _ /* frozen */ => global::Cogito.Navigate.Run(d, parse.GetValue(all), lk, kk, !parse.GetValue(noTestPrior), expand,
                                        parse.GetValue(promote), parse.GetValue(promoteEps) ?? global::Cogito.Navigate.DefaultPromoteEps, pt, lim, ce,
                                        new global::Cogito.Navigate.CovOpts(parse.GetValue(covBeacon), parse.GetValue(covWeight) ?? "vest",
                                            parse.GetValue(covTopk) ?? 0, parse.GetValue(covScale) ?? 1.0, parse.GetValue(covMinlen) ?? 0,
                                            parse.GetValue(covDiag), parse.GetValue(covDump) ?? 0)),
                };
            }
            catch (global::Cogito.DatasetMismatchException e)
            {
                Console.Error.WriteLine($"  {e.Message}");
                return 1;
            }
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  traces — the agentic-trace SUBSTRATE (AgentTrace.Run). An OPTIONAL <data-dir> positional (default = the synth
    //  fixtures dir) + MODE-SWITCHES that route to different routines: --selftest (round-trip), --probe (in-dist),
    //  --episode (ACT→OBSERVE loop). Default (no mode) materializes the corpus; --show dumps one transcript. The
    //  probe/episode sub-flags SHARE --len/--seed with DIFFERENT per-mode defaults — one Option each, absent ⇒ the
    //  body's own per-mode default (the argv round-trip preserves that).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    internal static Command Traces()
    {
        var dir      = new Argument<string?>("data-dir") { Arity = ArgumentArity.ZeroOrOne, Description = "data dir; omitted ⇒ the synth fixtures dir" };
        var selftest = new Option<bool>("--selftest")    { Description = "MODE: the tool-world round-trip self-test (grep→open→read→answer correct + deterministic)" };
        var probe    = new Option<bool>("--probe")       { Description = "MODE: the in-distribution probe (does inducing on the corpus put tool-use in-dist)" };
        var episode  = new Option<bool>("--episode")     { Description = "MODE: the reference ACT→OBSERVE→APPEND→GENERATE loop on one instance" };
        var show     = new Option<string?>("--show")     { Description = "dump one transcript by instanceId or index" };
        var @out     = new Option<string?>("--out")      { Description = "corpus output path (default: agentic_traces.txt beside the data dir)" };
        // probe sub-flags
        var len      = new Option<int?>("--len")         { Description = "generation length (probe default 1200 · episode default 60)" };
        var sweeps   = new Option<int?>("--sweeps")      { Description = "MCMC sweeps per generation (probe default 3)" };
        var samples  = new Option<int?>("--samples")     { Description = "generation samples (probe default 4)" };
        var seed     = CliShared.SeedOpt("generation seed (hex; probe default C0617010 · episode default E9150DE)");
        // episode sub-flags
        var inst     = new Option<string?>("--inst")     { Description = "the episode instance (default: the first instance)" };
        var looks    = new Option<int?>("--looks")       { Description = "the episode look budget (default 6)" };

        var cmd = new Command("traces", "the agentic-trace corpus substrate — materialize / self-test / probe / episode")
        {
            dir, selftest, probe, episode, show, @out, len, sweeps, samples, seed, inst, looks
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "traces" };
            if (parse.GetValue(dir) is { Length: > 0 } d) argv.Add(d);
            // mode-switches — the body checks them first (SelfTest/Probe/Episode short-circuit before --show/corpus).
            if (parse.GetValue(selftest)) argv.Add("--selftest");
            if (parse.GetValue(probe))    argv.Add("--probe");
            if (parse.GetValue(episode))  argv.Add("--episode");
            AddOpt(argv, "--show",    parse.GetValue(show));
            AddOpt(argv, "--out",     parse.GetValue(@out));
            AddInt(argv, "--len",     parse.GetValue(len));
            AddInt(argv, "--sweeps",  parse.GetValue(sweeps));
            AddInt(argv, "--samples", parse.GetValue(samples));
            AddOpt(argv, "--seed",    parse.GetValue(seed));
            AddOpt(argv, "--inst",    parse.GetValue(inst));
            AddInt(argv, "--looks",   parse.GetValue(looks));
            return global::Cogito.AgentTrace.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  corpus — the WORLD-POOL gather (Corpus.Run). `gather` is a LITERAL sub-verb (the body reads args[1]; the only
    //  sub-verb today), then the gather knobs. data/code is DESTRUCTIVELY recreated (the world dir is a build artifact).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    internal static Command Corpus()
    {
        // the sub-verb positional — `gather` keeps the historical line-pool path; `materialize`
        // copies ordinary source files byte-for-byte while preserving one file per source boundary.
        var sub      = new Argument<string>("subverb") { Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => "gather", Description = "gather, source-native materialize, fixed source-occurrence diet, or code-block-diet" };
        var manifest = new Option<string?>("--manifest") { Description = "the world recipe (default data/code.manifest)" };
        var @out     = new Option<string?>("--out")      { Description = "the source-native output dir (required for materialize; use a fresh data/code-native path)" };
        var source   = new Option<string?>("--source")   { Description = "frozen source-native world directory (required for diet)" };
        var authority = new Option<string?>("--authority") { Description = "source-native authority TSV (default <source>.source-native.tsv)" };
        var replace  = new Option<bool>("--replace")     { Description = "allow materialize to replace a non-empty destination" };
        var scale    = new Option<double?>("--scale")    { Description = "multiply every domain budget, 0.1 = a 1/10th smoke world (default 1.0)" };

        var cmd = new Command("corpus", "assemble the durable many-domain world pool (the bonfire's fuel)")
        {
            sub, manifest, @out, source, authority, replace, scale
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "corpus", parse.GetValue(sub)! };
            AddOpt(argv, "--manifest", parse.GetValue(manifest));
            AddOpt(argv, "--out",      parse.GetValue(@out));
            AddOpt(argv, "--source",   parse.GetValue(source));
            AddOpt(argv, "--authority", parse.GetValue(authority));
            if (parse.GetValue(replace)) argv.Add("--replace");
            AddDbl(argv, "--scale",    parse.GetValue(scale));
            return global::Cogito.Corpus.Run(argv.ToArray());
        });
        return cmd;
    }

    // ── argv-rebuild helpers (the ADAPTER-ARGV bridge) — append `key value` only when the option was set. ──
    private static void AddOpt(List<string> argv, string key, string? val) { if (!string.IsNullOrEmpty(val)) { argv.Add(key); argv.Add(val); } }
    private static void AddInt(List<string> argv, string key, int? val)    { if (val is int v) { argv.Add(key); argv.Add(v.ToString()); } }
    private static void AddDbl(List<string> argv, string key, double? val) { if (val is double v) { argv.Add(key); argv.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); } }
}
