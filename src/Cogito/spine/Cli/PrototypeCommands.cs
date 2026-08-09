namespace Cogito.Cli;

using System.CommandLine;
using System.Linq;
using Cogito;   // AgentSolve · Cortex · EdgeAutopsy · Swallow · Gret — the drives these commands front
using static Cogito.CliReports;   // ZipfSlope · Health — the shared grammar-health reads the stats body uses

// ── FLAGSHIP + PROTOTYPE COMMANDS ──  the top-level drives (solve · cortex) plus the probe
// prototypes (stats · gret). Each verb's Build() returns a Command whose typed Option<T>/Argument<T> own
// tokenize + validation + generated help; the SetAction reads ParseResult.GetValue and invokes the body.
//
// THREE HANDLER STYLES (AOT-safe — the EXPLICIT api, never SetHandler reflection binding):
//   TYPED-CALL   — the body takes typed params (byte[] corpus, …); the handler reads the values and calls it.
//   RUNTIME      — the handler builds a config object and invokes an embeddable runtime (Cortex).
//   ADAPTER-ARGV — the body is a stable-API `X.Run(string[])` (AgentSolve) that owns its own
//                  Args.* parse; the handler rebuilds the minimal argv it expects. The typed CLI owns the
//                  front; the body's parse resolves identically (the determinism Vow: same flags ⇒ same run).
internal static class PrototypeCommands
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  1. SIMPLE — `stats` : one corpus positional, no flags. The floor of the pattern.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Stats()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("stats", "compression + grammar health (Zipf slope: language vs hoard)")
        {
            corpus
        };
        // TYPED-CALL: resolve the corpus positional, hand the bytes straight to the DumpStats body below.
        cmd.SetAction(parse => DumpStats(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2. THE MESSY ONE — `gret` : 2 required file positionals + a fistful of value/switch flags, INCLUDING
    //     the --topk dual-default quirk. In the old body --topk is read TWICE via Array.IndexOf with two
    //     different defaults (1000 for the --rank path, 100 for the --dump/--rankdump path). Modeled cleanly
    //     as ONE Option<int?> with NO default; each code path applies ITS default when the option is absent.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Gret()
    {
        var sites   = new Argument<string>("sites-file")   { Description = "site blocks (JSON array / =====SITE===== / \\n\\n)" };
        var intents = new Argument<string>("intents-file") { Description = "one intent per line, line i targets site i" };
        var raw       = new Option<bool>("--raw")     { Description = "add case-sensitive grammar rows (expose the CamelCase confound)" };
        var delim     = new Option<string?>("--delim")     { Description = "force the site delimiter" };
        var index     = new Option<string?>("--index")     { Description = "induce the grammar over this corpus instead of the sites" };
        var dump      = new Option<string?>("--dump")      { Description = "dump concept features to <prefix>.{concepts,sites,intents}.tsv" };
        var rankdump  = new Option<string?>("--rankdump")  { Description = "emit per-intent NG-BM25 rankings for external scoring" };
        var rank      = new Option<string?>("--rank")      { Description = "NG-BM25 rank dump for qrels-based (BEIR) scoring" };
        // THE DUAL-DEFAULT: no.DefaultValueFactory — absent ⇒ null, and each path picks 1000 (--rank) or 100 (--dump).
        var topk      = new Option<int?>("--topk")         { Description = "top-K to emit (default 1000 for --rank, 100 for --rankdump)" };

        var cmd = new Command("gret", "grammar-native retrieval vs the n-gram floor (top-1/r@10/MRR)")
        {
            sites, intents, raw, delim, index, dump, rankdump, rank, topk
        };
        // ADAPTER-ARGV: gret's body owns a dense internal parse (LoadSites shapes, the rank/rankdump
        // short-circuits, the dual-topk) that we keep intact this pass. Rebuild the argv it expects — the
        // typed CLI now owns validation + help, the body's Array.IndexOf scrapes still resolve identically.
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "gret", parse.GetValue(sites)!, parse.GetValue(intents)! };
            if (parse.GetValue(raw)) argv.Add("--raw");
            AddOpt(argv, "--delim",    parse.GetValue(delim));
            AddOpt(argv, "--index",    parse.GetValue(index));
            AddOpt(argv, "--dump",     parse.GetValue(dump));
            AddOpt(argv, "--rankdump", parse.GetValue(rankdump));
            AddOpt(argv, "--rank",     parse.GetValue(rank));
            if (parse.GetValue(topk) is int k) { argv.Add("--topk"); argv.Add(k.ToString()); }
            return global::Cogito.Gret.Run(argv.ToArray());   // global:: — the `Gret()` command-builder shadows the engine class
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2b. THE AUTOPSY — `edge-rerank` : parked H2 differential-MDL probe. READ-ONLY historical science on the
    //     banked navigate results; closure-wave rewards live on the self-play execution corroboration, not this probe.
    //     TYPED-CALL straight into EdgeAutopsy.Run; mints nothing, changes no engine behavior.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command EdgeRerank()
    {
        var rankings = new Argument<string>("rankings") { Description = "banked navigate_rankings.jsonl (per-instance local_fn_sites + visited_files)" };
        var data     = new Option<string>("--data") { Required = true, Description = "swe_loc data dir — <inst>/{query.txt,sites.jsonl,gold.json} per instance" };
        var outDir   = new Option<string?>("--out")  { Description = "render dir for the tables (default tmp/edge_autopsy)" };
        var limit    = new Option<int?>("--limit")   { Description = "cap the instance stream (0 = all; for a smoke)" };

        var cmd = new Command("edge-rerank", "H2 Edge autopsy — offline differential-MDL re-rank of banked navigate results vs gold (read-only)")
        {
            rankings, data, outDir, limit
        };
        cmd.SetAction(parse => EdgeAutopsy.Run(
            parse.GetValue(rankings)!, parse.GetValue(data)!,
            parse.GetValue(outDir) ?? "tmp/edge_autopsy", parse.GetValue(limit) ?? 0));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2c. THE DEPTH AUTOPSY — `depth-autopsy` : the P1/P3 read of the DEPTH thesis on a banked corroborated-mesh
    //     checkpoint. READ-ONLY science on DEAD DATA — it loads the converged (grammar, tape), runs Pearl.Audit,
    //     and bins reflection-rate by rule DEPTH, testing whether deep rules reflect ~exponentially rarely (the
    //     "maxSpan = memorization" clause) BEFORE Whorl B (the blur) is built. TYPED-CALL into DepthAutopsy.Run.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command DepthAutopsy()
    {
        var runDir = new Argument<string>("run-dir") { Description = "a banked witnessed-mesh run dir (triangle/mesh checkpoint.bin + tape.spanlog)" };
        var outDir = new Option<string?>("--out")   { Description = "render dir for the tables + charts (default tmp/depth_autopsy)" };
        var diet   = new Option<string[]>("--diet") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true, Description = "P3 diet-depth: corpus files to induce the raw-world ladder from (default = the run's own CorpusPaths)" };
        var trace  = new Option<bool>("--trace-rules") { Description = "also dump every rule's (depth, span, explen, direct/dag source breadth) to rules_by_depth.tsv" };
        var slots  = new Option<bool>("--slots-on") { Description = "arm Whorl B Tier 1.5 slot pooling in the re-attributed co-walk (exit-gate read; default off = byte-identical baseline)" };

        var cmd = new Command("depth-autopsy", "the DEPTH thesis on dead data — reflection-rate vs rule depth (P1) + collapse-depth vs diet-depth (P3), read-only")
        {
            runDir, outDir, diet, trace, slots
        };
        cmd.SetAction(parse => global::Cogito.DepthAutopsy.Run(
            parse.GetValue(runDir)!, parse.GetValue(outDir) ?? "tmp/depth_autopsy",
            parse.GetValue(diet), parse.GetValue(trace), parse.GetValue(slots)));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2c′. THE QUADRANT ASSAY — `quadrant` : H2″, the self-play home of the sharpness axis. READ-ONLY science on a
    //     banked converged mesh checkpoint — it loads the (grammar, tape), runs Pearl.Audit, and factors every rule
    //     into breadth × edge → gold/glue/memory/noise, reporting orthogonality corr(breadth,edge), boilerplate→glue,
    //     and the depth wall re-expressed (deep → memory). TYPED-CALL into QuadrantAssay.Run. Sibling of depth-autopsy.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Quadrant()
    {
        var runDir = new Argument<string>("run-dir") { Description = "a banked converged mesh run dir (mesh checkpoint.bin, CrossReflect on)" };
        var outDir = new Option<string?>("--out")   { Description = "render dir for the 2×2 + tables + charts (default tmp/quadrant)" };

        var cmd = new Command("quadrant", "H2″ quadrant assay — breadth × edge per rule over a converged mesh → gold/glue/memory/noise (read-only)")
        {
            runDir, outDir
        };
        cmd.SetAction(parse => global::Cogito.QuadrantAssay.Run(
            parse.GetValue(runDir)!, parse.GetValue(outDir) ?? "tmp/quadrant"));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2c″. THE BLUR — `blur` : the v0 blur at TOKEN grain. Runs the three
    //     detectors over a corpus — count-slots (the doubling-tower/knot census), literal-alternation (anti-unify,
    //     MDL-gated) + the frame census (the def/call emergence read), transform-slots (the offset op) — plus the
    //     marginal-filler anti-Goodhart null. READ-ONLY + ADDITIVE (Tier 1, byte-identical): mints nothing into the
    //     reconstruction path. ADAPTER-ARGV into Blur.Run (owns its own Args.* parse).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Blur()
    {
        var source   = new Argument<string?>("source") { Arity = ArgumentArity.ZeroOrOne, Description = "corpus FILE or DIR (files concatenated); omitted ⇒ builtin sample" };
        var outDir   = new Option<string?>("--out")       { Description = "render dir for the report (default tmp/blur)" };
        var maxBytes = new Option<int?>("--max-bytes")    { Description = "corpus byte cap (default 300000)" };
        var iter     = new Option<int?>("--iter")         { Description = "anti-unify growth iterations (default 6)" };
        var top      = new Option<int?>("--top")          { Description = "rows per section (default 20)" };
        var reflect  = new Option<bool>("--reflect")      { Description = "TIER 1.5: run the slot-aware reflection depth-cure demo (two-source, slots OFF vs ON + shuffled-mates null)" };
        var seed     = CliShared.SeedOpt("blur marginal-filler-null seed (hex, default B1000B1)");

        var cmd = new Command("blur", "the v0 blur at token grain — count-slots (towers) · literal-alternation · transform-slots (offset) + the anti-Goodhart null (read-only)")
        {
            source, outDir, maxBytes, iter, top, reflect, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "blur" };
            if (parse.GetValue(source) is { Length: > 0 } s) argv.Add(s);
            AddOpt(argv, "--out",       parse.GetValue(outDir));
            AddInt(argv, "--max-bytes", parse.GetValue(maxBytes));
            AddInt(argv, "--iter",      parse.GetValue(iter));
            AddInt(argv, "--top",       parse.GetValue(top));
            if (parse.GetValue(reflect)) argv.Add("--reflect");
            AddOpt(argv, "--seed",      parse.GetValue(seed));
            return global::Cogito.Blur.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2c‴. THE LATTICE — `lattice` : the co-instantiation census between blur paradigms. The blur proves
    //     paradigms exist; this measures whether the SAME identifier fillers knit those paradigms across files.
    //     READ-ONLY + ADDITIVE: re-tokenizes per file, preserves the byte-induction path, and verifies against a
    //     per-file degree-preserving relabel null.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Lattice()
    {
        var source = new Argument<string>("dir") { Description = "source directory to tokenize per file" };
        var top    = new Option<int?>("--top")   { Description = "top frame families by diversity (default 12)" };
        var all    = new Option<bool>("--all-families") { Description = "census every identifier-bearing frame family and enforce the R20 opportunity gate" };
        var seed   = CliShared.SeedOpt("lattice relabel-null seed (hex, default 1A771CE)");

        var cmd = new Command("lattice", "co-instantiation lattice census — same identifier fillers bridging blur paradigms across files, with relabel null")
        {
            source, top, all, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "lattice", parse.GetValue(source)! };
            AddInt(argv, "--top", parse.GetValue(top));
            if (parse.GetValue(all)) argv.Add("--all-families");
            AddOpt(argv, "--seed", parse.GetValue(seed));
            return global::Cogito.LatticeCensus.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2c⁗. THE OSPHRADIUM — stimulus-conditioned relevance over the LatticeCensus READ-1 incidence homes.
    //     Seeds come from the grammar cover of the stimulus; flow crosses filler→family memberships and lands in
    //     family×filler density homes. The localization bench is only the readout: no task words, no parser.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Osphradium()
    {
        var dataDir  = new Argument<string>("data-dir") { Description = "swe_loc data dir — <inst>/{query.txt,sites.jsonl,gold.json}" };
        var rankings = new Option<string?>("--rankings") { Description = "rankings.jsonl; omitted discovers swe_loc/eval/runs/20260702T014145Z_cache/rankings.jsonl" };
        var limit    = new Option<int?>("--limit") { Description = "cap the instance stream (0 = all; for a smoke)" };
        var top      = new Option<int?>("--top-families") { Description = "top frame families by diversity (default 12)" };
        var maxBytes = new Option<int?>("--max-corpus-bytes") { Description = "candidate module bytes induced per instance (default 500000)" };
        var maxFiles = new Option<int?>("--max-files") { Description = "cap ranked candidate files before scoring (0 = all)" };
        var preDegree = new Option<bool>("--pre-degree-norm") { Description = "legacy Osphradium activation: do not degree-normalize the primary flow arm" };
        var seed     = CliShared.SeedOpt("osphradium seed-shuffle null seed (hex, default 5FAD1A)");

        var cmd = new Command("osphradium", "stimulus-conditioned relevance organ — grammar-cover seeds flowing through within-family density homes")
        {
            dataDir, rankings, limit, top, maxBytes, maxFiles, preDegree, seed
        };
        cmd.SetAction(parse => global::Cogito.Osphradium.Run(
            parse.GetValue(dataDir)!,
            parse.GetValue(rankings) ?? "",
            parse.GetValue(limit) ?? 0,
            parse.GetValue(top) ?? 12,
            parse.GetValue(maxBytes) ?? 0,
            parse.GetValue(maxFiles) ?? 0,
            parse.GetValue(preDegree),
            CliShared.ParseSeed(parse.GetValue(seed), global::Cogito.Osphradium.DefaultSeed)));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2d. THE DECISIVE AUTOPSY — `edge-rule-rerank` : Arm R, the differential-MDL probe at GRAMMAR-RULE grain.
    //     Token concentration lost to idf (H2/H2′); this induces the grammar per instance and meters CONCENTRATION
    //     over per-file RULE-fire counts — the organ cogito actually specced. Same
    //     READ-ONLY dead-data re-rank, same +5.7% idf bar, plus the shuffled-home differential null. Decides Tier A.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command EdgeRuleRerank()
    {
        var rankings = new Argument<string>("rankings") { Description = "banked navigate_rankings.jsonl (per-instance local_fn_sites + visited_files)" };
        var data     = new Option<string>("--data") { Required = true, Description = "swe_loc data dir — <inst>/{query.txt,sites.jsonl,gold.json} per instance" };
        var outDir   = new Option<string?>("--out")  { Description = "render dir for the tables (default tmp/edge_rule_autopsy)" };
        var limit    = new Option<int?>("--limit")   { Description = "cap the instance stream (0 = all; for a smoke)" };

        var cmd = new Command("edge-rule-rerank", "Arm R — rule-grain Edge autopsy: offline differential-MDL re-rank over grammar-RULE fire counts vs gold (read-only)")
        {
            rankings, data, outDir, limit
        };
        cmd.SetAction(parse => EdgeRuleAutopsy.Run(
            parse.GetValue(rankings)!, parse.GetValue(data)!,
            parse.GetValue(outDir) ?? "tmp/edge_rule_autopsy", parse.GetValue(limit) ?? 0));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  2e. THE SWALLOW — `swallow` : metacircularity check.
    //     Serializes a converged grammar's rule bodies AS SPANS, induces the META-grammar (grammar-of-grammar),
    //     and reads RenormStats vs a rule-shuffled null: is the store self-similar all the way down (a BODY) or a
    //     heap? READ-ONLY dead-data probe over the engine (Induce/Breach/RenormStats); TYPED-CALL into Swallow.Run.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Swallow()
    {
        var source     = new Argument<string>("grammar-source") { Description = "a corpus FILE (induce+consolidate), a corpus DIR (concat+induce), or a checkpoint run-dir (peek the converged grammar)" };
        var outDir     = new Option<string?>("--out")           { Description = "render dir for the tables (default tmp/swallow)" };
        var seed       = CliShared.SeedOpt("null-model shuffle seed (hex, default 5EEDF00D)");
        var top        = new Option<int?>("--top")              { Description = "how many top meta-motifs to dump (default 25)" };
        var noBarrier  = new Option<bool>("--no-barrier")       { Description = "concatenate rule bodies WITHOUT a boundary sentinel (lets meta-motifs straddle bodies)" };
        var noConsol   = new Option<bool>("--no-consolidate")   { Description = "skip binary→n-ary consolidation (Breach) — serialize the pure binary base (degenerate; documents the binary floor)" };
        var breachQ    = new Option<int?>("--breach-quota")     { Description = "speculative count-2 mint quota for Breach consolidation (0 = auto: base rule count)" };
        var composeOnly = new Option<bool>("--compose-only")    { Description = "serialize ONLY the nonterminal children (drop terminal bytes) — the pure reference DAG, isolating composition from re-derived byte morphology" };

        var cmd = new Command("swallow", "the metacircular stage on dead data — is the grammar-of-grammar critical (a BODY) or flat (a heap)? read-only")
        {
            source, outDir, seed, top, noBarrier, noConsol, breachQ, composeOnly
        };
        cmd.SetAction(parse => global::Cogito.Swallow.Run(
            parse.GetValue(source)!, parse.GetValue(outDir) ?? "tmp/swallow",
            CliShared.ParseSeed(parse.GetValue(seed), global::Cogito.Swallow.DefaultSeed),
            parse.GetValue(top) ?? 25, !parse.GetValue(noBarrier), !parse.GetValue(noConsol),
            parse.GetValue(breachQ) ?? 0, parse.GetValue(composeOnly)));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  3. THE LOC VERB — `solve` : an optional dir positional + ~9 knobs + 2 mode-switch flags
    //     (--verify-durability / --probe-index route to entirely different routines). The body already
    //     funnels its knobs through SolveOpts.Parse(args), the clean adapter seam — so this is ADAPTER-ARGV
    //     against that seam, with the mode-switches modeled as bool options.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Solve()
    {
        var dataDir      = new Argument<string?>("data-dir") { Arity = ArgumentArity.ZeroOrOne, Description = "instance dir; default = synth fixtures" };
        var looks        = new Option<int?>("--looks")        { Description = "tool-call budget per instance (default 8)" };
        var looksCap     = new Option<int?>("--looks-cap")    { Description = "max action budget for an abstaining instance (default = --looks; the commit boundary is calibration-homeostat owned)" };
        var len          = new Option<int?>("--len")          { Description = "generation length per look (default 80)" };
        var sweeps       = new Option<int?>("--sweeps")       { Description = "MCMC sweeps per emission (default 2)" };
        var seed         = CliShared.SeedOpt("solve LCG seed (hex, default 50175E00)");
        var limit        = new Option<int?>("--limit")        { Description = "cap the instance stream (0 = all)" };
        var pretrain     = new Option<int?>("--pretrain")     { Description = "1 = pretrain on synth tool-traces (default 1)" };
        var meshHomeo    = new Option<bool>("--mesh-homeo")   { Description = "arm the mesh-criticality basin brake" };
        var siteBudget   = new Option<int?>("--site-budget")  { Description = "world-contact site spans per instance (default 48)" };
        var confidenceTrace = new Option<bool>("--confidence-trace") { Description = "dump each commit candidate's margin/coherence/confidence and the homeostat floor" };
        var explainRank  = new Option<bool>("--explain-rank") { Description = "RANKING AUTOPSY: dump the full PathVotes tally (per-candidate vote, pushes, grep fan-out, terms) + the idf counterfactual at each commit" };
        var verifyDura   = new Option<bool>("--verify-durability") { Description = "run ONLY the vest=permanence contract check" };
        var probeIndex   = new Option<bool>("--probe-index")  { Description = "run ONLY the hippocampus searchability probe" };
        // ── THE COMBUSTION WIRING (FEED + HOLD + CHURN) ──
        var feed         = new Option<string[]>("--feed") { Arity = ArgumentArity.ZeroOrMore, Description = "FEED: extra data root(s) streamed alongside data-dir — the perpetual-novel-real newspaper (repeatable; point at different codebases)" };
        var interleave   = new Option<bool>("--interleave") { Description = "FEED: round-robin instances ACROSS repos (varied feed — consecutive instances are different codebases) instead of id-sorted clustering" };
        var passes       = new Option<int?>("--passes") { Description = "FEED: re-stream the pool N times, Cortex LOC runtime NOT reset (the LOOPED world — does the re-pass renormalize flat, or does the wiring keep it combusting; default 1)" };
        var meshFloor    = new Option<double?>("--mesh-floor") { Description = "HOLD: the homeostat boredom floor — minimal dream-accretion fraction retained at rest (default 0.05)" };
        var meshGain     = new Option<double?>("--mesh-gain")  { Description = "HOLD: the homeostat integral gain — how fast the throttle chases its target (default 0.30)" };
        var mixSpans     = new Option<int?>("--mix-spans") { Description = "CHURN: prior-real spans the night's MIX rail re-mounts (fresh structure for the loopback to vest; 0 = inert-loopback baseline)" };
        var ckptEvery    = new Option<int?>("--checkpoint-every") { Description = "checkpoint cadence in completed instances (default 25; 0 = off)" };
        var answerLeakFree = new Option<bool>("--answer-leak-free") { Description = "CONTROL: redact literal answer paths from the grammar/tape plane" };
        Option<bool> shuffleBindings = new("--shuffle-bindings") { Description = "NULL: scramble which provenance source feeds each procedure slot" };
        Option<int?> heldout = new("--heldout") { Description = "held-out world count; enables the two-pass transfer experiment" };
        Option<int?> revisited = new("--revisited") { Description = "training-world count before each held-out fork" };
        var cmd = new Command("solve", "the Cortex-backed LOC runtime — solves + compounds, no reset")
        {
            dataDir, looks, looksCap, len, sweeps, seed, limit, pretrain, meshHomeo, siteBudget, confidenceTrace, explainRank, verifyDura, probeIndex,
            feed, interleave, passes, meshFloor, meshGain, mixSpans, ckptEvery, answerLeakFree,
            shuffleBindings, heldout, revisited
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "solve" };
            if (parse.GetValue(dataDir) is { Length: > 0 } dir) argv.Add(dir);
            // mode-switches first — the body checks them before SolveOpts.From and short-circuits.
            if (parse.GetValue(verifyDura)) argv.Add("--verify-durability");
            if (parse.GetValue(probeIndex)) argv.Add("--probe-index");
            if (parse.GetValue(meshHomeo))  argv.Add("--mesh-homeo");
            if (parse.GetValue(confidenceTrace)) argv.Add("--confidence-trace");
            if (parse.GetValue(explainRank)) argv.Add("--explain-rank");
            if (parse.GetValue(interleave)) argv.Add("--interleave");
            if (parse.GetValue(answerLeakFree)) argv.Add("--answer-leak-free");
            if (parse.GetValue(shuffleBindings)) argv.Add("--shuffle-bindings");
            foreach (var f in parse.GetValue(feed) ?? []) { argv.Add("--feed"); argv.Add(f); }
            AddInt(argv, "--looks",       parse.GetValue(looks));
            AddInt(argv, "--looks-cap",   parse.GetValue(looksCap));
            AddInt(argv, "--len",         parse.GetValue(len));
            AddInt(argv, "--sweeps",      parse.GetValue(sweeps));
            AddOpt(argv, "--seed",        parse.GetValue(seed));
            AddInt(argv, "--limit",       parse.GetValue(limit));
            AddInt(argv, "--pretrain",    parse.GetValue(pretrain));
            AddInt(argv, "--site-budget", parse.GetValue(siteBudget));
            AddInt(argv, "--passes",      parse.GetValue(passes));
            AddDbl(argv, "--mesh-floor",  parse.GetValue(meshFloor));
            AddDbl(argv, "--mesh-gain",   parse.GetValue(meshGain));
            AddInt(argv, "--mix-spans",   parse.GetValue(mixSpans));
            AddInt(argv, "--checkpoint-every", parse.GetValue(ckptEvery));
            AddInt(argv, "--heldout", parse.GetValue(heldout));
            AddInt(argv, "--revisited", parse.GetValue(revisited));
            return AgentSolve.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  3b. THE JOURNAL READ — `ignition-journal-read` : Lane 2's dead-data read over the banked ignition wave.
    //      No loop coupling, no fresh solve; it renders walk-shape and class-attribution tables from the
    //      existing curve/journal/value journals.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command IgnitionJournalRead()
    {
        var stamp = new Argument<string?>("stamp") { Arity = ArgumentArity.ZeroOrOne, Description = "run stamp (default ignition_20260708T002231634Z)" };
        var outPath = new Option<string?>("--out") { Description = "markdown report path (default runs/<stamp>_report/lane2_dead_data.md)" };

        var cmd = new Command("ignition-journal-read", "Lane 2 dead-data read over the ignition journal: walk shape + class attribution")
        {
            stamp, outPath
        };
        cmd.SetAction(parse => global::Cogito.IgnitionJournalRead.Run(parse.GetValue(stamp), parse.GetValue(outPath)));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE FLAGSHIP RUNTIME — `cortex`: one ctor-configured machine over curriculum + tape + grammar + self-stream.
    //  CortexRunConfig remains the checkpoint contract under the public API, not the user-facing ontology.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Cortex()
    {
        var corpus       = new Argument<string?>("corpus") { Arity = ArgumentArity.ZeroOrOne, Description = "domain corpus (FILE = flat pool · DIR = each file a domain · hf://owner/dataset/config/split?text=col&maxRows=N = Hugging Face rows stream)" };
        var steps        = new Argument<int?>("steps")     { Arity = ArgumentArity.ZeroOrOne, Description = "drive steps (default 200; --steps also accepted)" };
        var stepsOpt     = new Option<int?>("--steps")     { Description = "drive steps (wins over the positional; default 200)" };
        var block        = new Option<int?>("--block")     { Description = "mint block size (default 700)" };
        var maxBlock     = new Option<int?>("--maxblock")  { Description = "max block (default 16384)" };
        var window       = new Option<int?>("--window")    { Description = "context window, 0 = whole tape (default 0)" };
        var lambda       = new Option<double?>("--lambda") { Description = "curiosity weight (default 0.3)" };
        var seed         = CliShared.SeedOpt("cortex LCG seed (hex, default C0117011)");
        var stride       = new Option<int?>("--stride")    { Description = "re-induce stride bytes (default 5000)" };
        var domStride    = new Option<int?>("--domstride") { Description = "domain-stride spans (default 6)" };
        var frontierCap  = new Option<int?>("--frontiercap") { Description = "frontier cap exponents (default 400)" };
        var intake       = new Option<int?>("--intake")    { Description = "frontier-intake drain rate, spans/step (default 4)" };
        var seedSpans    = new Option<int?>("--seedspans") { Description = "bootstrap anchor size, spans/domain (default 3)" };
        var mix          = new Option<int?>("--mix")       { Description = "MIX rail cadence, 0 = seal the loop (default 8)" };
        var affirmGate   = new Option<double?>("--affirm-gate") { Description = "intake-affirm veto per-line residual cut (0 = on; <0 = disarmed control arm)" };
        var curriculum   = new Option<string?>("--curriculum") { Description = "flatpool|grokbell|eml|campfire (default flatpool)" };
        var glob         = new Option<string?>("--glob")   { Description = "with a DIR corpus: file globs (default *.cs,*.py,*.md,*.txt)" };
        var cv           = new Option<double?>("--cv")     { Description = "lock-line CV floor (default 0.15)" };
        var lockL        = new Option<int?>("--lock")      { Description = "hysteresis depth, rounds (default 3)" };
        var energy       = new Option<string?>("--energy") { Description = "metabolic|markov|mcmc|coupling|nodebirth|energy (default metabolic)" };
        var gen          = new Option<string?>("--gen")    { Description = "alias of --energy" };
        var affFloor     = new Option<double?>("--affloor"){ Description = "affinity floor (default 1.0)" };
        Option<int?> intervalConsolidationPhase = new("--interval-aestivation") { Description = "replace homeostatic aestivation with a fixed cadence; 0 disables aestivation" };
        var budget       = new Option<long?>("--budget")   { Description = "grammar bit budget, 0 = unbounded (default 0)" };
        Option<int?> wScale = new("--wscale") { Description = "provenance count measure / evidence weight scale (default 8)" };
        Option<bool> noCrossReflect = new("--no-cross-reflection") { Description = "disable source-independent corroboration" };
        Option<double?> dreamRatio = new("--dreamratio") { Description = "unvested-dream cap = ratio x born-evidence spans, 0 = unbounded (default 1.0)" };
        var senseMask    = new Option<string?>("--sense-mask") { Description = "attribution ablation: sense-planes pinned dark (self-stream,cost,collapse,provenance)" };
        var noBreach     = new Option<bool>("--no-breach") { Description = "the grants-expire-unspent control arm (breach ON by default)" };
        var simhash      = new Option<bool>("--simhash")   { Description = "force the SimHash candidate-gen ON (default auto)" };
        var noSimhash    = new Option<bool>("--no-simhash"){ Description = "force the SimHash candidate-gen OFF (default auto)" };
        Option<bool> noNearDupe = new("--no-neardupe") { Description = "disable near-duplicate containment demotion" };
        Option<bool> noAntiunify = new("--no-antiunify") { Description = "disable sleep-pass paradigm induction" };
        var wallTol      = new Option<double?>("--walltol"){ Description = "momentum WALL dead-band (default 0.003)" };
        var ckptEvery    = new Option<int?>("--checkpoint-every") { Description = "checkpoint cadence, 0 = auto (default 0)" };
        var curveEvery   = new Option<int?>("--curve-every") { Description = "curve.tsv row cadence (default 1 = every step)" };
        var readout      = new Option<string[]>("--readout") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true, Description = "workspace key/prefix selectors appended to curve.tsv (comma or repeat; e.g. eml.targets.*,eml.census.*)" };
        var noLoom       = new Option<bool>("--no-loom")   { Description = "the stride-gated batch oracle (the O(Δ) loom is the default)" };
        var noShed       = new Option<bool>("--no-shed")   { Description = "everything-resident (night tape-shed is the default)" };
        Option<bool> noRhythm = new("--no-rhythm") { Description = "disable the autonomic day/dream/aestivation scheduler" };
        Option<string?> homeostatPolicy = new("--homeostat-policy") { Description = "homeostat sense/prediction tier: reflex|wired|predict (default predict)" };
        Option<string?> homeostatAutonomy = new("--homeostat-autonomy") { Description = "learned authority: off|emulation|full (default full)" };
        Option<string?> deepRematchGate = new("--deep-rematch-gate") { Description = "pre-registered dissolution deep-rematch gate RON copied into the run before step zero" };
        Option<int?> policyShadow = new("--policy-shadow") { Description = "shared Cortex policy observations before verification (default 8)" };
        Option<int?> policyShort = new("--policy-short") { Description = "shared policy short matched-fork horizon in decisions (default 16)" };
        Option<int?> policyMedium = new("--policy-medium") { Description = "shared policy medium matched-fork horizon in decisions (default 64)" };
        Option<int?> policyLong = new("--policy-long") { Description = "shared policy long matched-fork horizon in decisions (default 256)" };
        var emlSig       = new Option<int?>("--sig")       { Description = "EML dual-point sig figures (default 9)" };
        var seedk        = new Option<int?>("--seedk")     { Description = "EML seed shells enumerated at bootstrap (default 7)" };
        var maxlen       = new Option<int?>("--maxlen")    { Description = "EML sampled-program length cap (default 40)" };
        var maxenum      = new Option<int?>("--maxenum")   { Description = "EML enumeration cap (default 13)" };
        var units        = new Option<int?>("--units")     { Description = "EML sampled units per program (default 6)" };
        var gain         = new Option<int?>("--gain")      { Description = "EML chunk-frequency bias vs flat token weight (default 4)" };
        var polmix       = new Option<double?>("--polmix") { Description = "EML uniform epsilon (default 0.125)" };
        var polenum      = new Option<double?>("--polenum"){ Description = "EML enum-rail epsilon (default 0.4)" };
        var corrob       = new Option<int?>("--corrob")    { Description = "EML corroboration weight (default 16)" };
        var certw        = new Option<int?>("--certw")     { Description = "EML certificate-gate weight (default 4)" };
        var holdout      = new Option<double?>("--holdout") { Description = "EML held-out fraction of named targets for workspace readout (default 0)" };
        var holdoutSeed  = new Option<string?>("--holdout-seed") { Description = "EML holdout split seed (hex; default = run seed)" };
        Option<int?> kmax = new("--kmax") { Description = "EML live ruler max K; 0 = lift off (default 200)" };
        var annealFactor = new Option<double?>("--anneal-factor") { Description = "EML ruler growth factor (default 1.4)" };
        var annealWin    = new Option<int?>("--anneal-win")       { Description = "EML lift census window (default 50)" };
        var annealSustain = new Option<int?>("--anneal-sustain")  { Description = "EML lift plateau sustain windows (default 3)" };
        var annealFrac   = new Option<double?>("--anneal-frac")   { Description = "EML lift plateau fraction (default 0.25)" };
        var meanzBand    = new Option<double?>("--meanzband")     { Description = "EML exact-tier meanz tolerance (default 0.35)" };
        var censusOnly   = new Option<bool>("--census-only")      { Description = "EML lift gate ignores exact-tier RG lock" };
        var lockMeanz    = new Option<bool>("--lock-meanz")       { Description = "EML lift gates on meanz only; cvz telegraphed" };
        Option<string?> emlActions = new("--eml-actions")         { Description = "EML autonomous action selection: off|round-robin|shuffled-fixed|procedure|procedure-shuffled|procedure-guarded|procedure-guard-shuffled (default procedure-guarded)" };

        var cmd = new Command("cortex", "the Cogito cortex loop — curriculum → tape → grammar → self-stream → aestivation")
        {
            corpus, steps, stepsOpt, block, maxBlock, window, lambda, seed, stride, domStride, frontierCap, intake,
            seedSpans, mix, affirmGate, curriculum, glob, cv, lockL, energy, gen, affFloor, intervalConsolidationPhase, budget, wScale,
            noCrossReflect, dreamRatio, senseMask, noBreach, simhash, noSimhash, noNearDupe, noAntiunify,
            wallTol, ckptEvery, curveEvery, readout, noLoom, noShed, noRhythm,
            homeostatPolicy, homeostatAutonomy,
            deepRematchGate,
            policyShadow, policyShort, policyMedium, policyLong,
            emlSig, seedk, maxlen, maxenum, units, gain, polmix, polenum, corrob, certw, holdout, holdoutSeed,
            kmax, annealFactor, annealWin, annealSustain, annealFrac, meanzBand, censusOnly, lockMeanz, emlActions
        };
        cmd.SetAction(parse =>
        {
            string? curriculumToken = parse.GetValue(curriculum);
            bool usesEml = string.Equals(curriculumToken, "eml", StringComparison.OrdinalIgnoreCase);
            string? actionToken = parse.GetValue(emlActions);
            EmlActionSelections actionSelection = EmlActionSelectionTokens.Parse(actionToken ?? (usesEml ? "procedure-guarded" : "off"));
            string? corpusPath = parse.GetValue(corpus);
            bool needsCorpus = !string.Equals(curriculumToken ?? "flatpool", "eml", StringComparison.OrdinalIgnoreCase);
            if (needsCorpus && string.IsNullOrWhiteSpace(corpusPath))
            {
                Console.Error.WriteLine("usage: cortex <corpus-file|corpus-dir|hf://owner/dataset/config/split?text=col&maxRows=N> [steps|--steps N] [--curriculum flatpool|grokbell|campfire]");
                Console.Error.WriteLine("       cortex --curriculum eml [--steps N]");
                return 1;
            }
            int runSteps = Math.Max(1, parse.GetValue(stepsOpt) ?? parse.GetValue(steps) ?? 200);
            string? energyToken = parse.GetValue(energy) ?? parse.GetValue(gen);
            CogitoCorpus? corpusSource = string.IsNullOrWhiteSpace(corpusPath)
                ? null
                : new CogitoCorpus
                {
                    Path = corpusPath,
                    Glob = parse.GetValue(glob) ?? CogitoCorpus.DefaultGlob,
                };
            var curriculumConfig = CortexConfigTokens.ParseCurriculum(curriculumToken, corpusSource);
            if (actionSelection != EmlActionSelections.Off && curriculumConfig is not CortexEmlCurriculum)
            {
                Console.Error.WriteLine("--eml-actions requires --curriculum eml");
                return 1;
            }
            if (curriculumConfig is CortexEmlCurriculum emlCurriculum)
            {
                curriculumConfig = emlCurriculum with
                {
                    SignatureDigits = parse.GetValue(emlSig) ?? ReplayCalc.MountSig,
                    HoldoutFraction = parse.GetValue(holdout) ?? 0,
                    HoldoutSeed = CliShared.ParseSeed(parse.GetValue(holdoutSeed), 0),
                    Actions = actionSelection,
                    Generation = new EmlGenerationConfig
                    {
                        SeedShells = parse.GetValue(seedk) ?? ReplayCalc.MountSeedK,
                        MaxLength = parse.GetValue(maxlen) ?? ReplayCalc.MountMaxLen,
                        MaxEnumerationLength = parse.GetValue(maxenum) ?? ReplayCalc.MountMaxEnum,
                        SampleUnits = parse.GetValue(units) ?? ReplayCalc.MountUnits,
                        ChunkGain = parse.GetValue(gain) ?? ReplayCalc.MountGain,
                        UniformEpsilon = parse.GetValue(polmix) ?? ReplayCalc.MountEps,
                        EnumerationEpsilon = parse.GetValue(polenum) ?? ReplayCalc.MountEpsEnum,
                        CorroborationWeight = parse.GetValue(corrob) ?? ReplayCalc.MountCorrobW,
                        CertificateWeight = parse.GetValue(certw) ?? ReplayCalc.MountCertW,
                    },
                    Lift = new EmlLiftGateConfig
                    {
                        MaxRuler = parse.GetValue(kmax) ?? 200,
                        Factor = parse.GetValue(annealFactor) ?? 1.4,
                        Window = parse.GetValue(annealWin) ?? 50,
                        Sustain = parse.GetValue(annealSustain) ?? 3,
                        Fraction = parse.GetValue(annealFrac) ?? 0.25,
                        MeanzBand = parse.GetValue(meanzBand) ?? 0.35,
                        CensusOnly = parse.GetValue(censusOnly),
                        LockMeanz = parse.GetValue(lockMeanz),
                    },
                };
            }
            int configuredIntake = parse.GetValue(intake) ?? 4;
            int? intervalCadence = parse.GetValue(intervalConsolidationPhase);
            bool usesHomeostat = !intervalCadence.HasValue;
            ulong runSeed = CliShared.ParseSeed(parse.GetValue(seed), 0xC0117011UL);
            CortexCurriculumConfig runCurriculum = curriculumConfig with
            {
                IntakeBatch = configuredIntake,
                SeedSpans = parse.GetValue(seedSpans) ?? 3,
                MixEvery = parse.GetValue(mix) ?? 8,
                AffirmGate = parse.GetValue(affirmGate) ?? 0.0,
                GrokCv = parse.GetValue(cv) ?? GrokDefaults.Cv,
                LockRounds = parse.GetValue(lockL) ?? GrokDefaults.LockRounds,
            };
            CortexConfig config = new()
            {
                DeepRematchGatePath = parse.GetValue(deepRematchGate) ?? "",
                Steps = runSteps,
                Seed = runSeed,
                Generation = new CortexGenerationConfig
                {
                    BlockLength = parse.GetValue(block) ?? 700,
                    MaxBlockBytes = parse.GetValue(maxBlock) ?? 16384,
                    Window = parse.GetValue(window) ?? 0,
                    NoveltyDecay = parse.GetValue(lambda) ?? 0.3,
                    Energy = CortexConfigTokens.ParseEnergy(energyToken),
                    AffinityFloor = parse.GetValue(affFloor) ?? 1.0,
                },
                Stride = new CortexStrideConfig
                {
                    ReinduceBytes = parse.GetValue(stride) ?? GrokDefaults.ReStrideBytes,
                    DomainSpans = parse.GetValue(domStride) ?? GrokDefaults.DomStrideSpans,
                    FrontierExpansionCap = parse.GetValue(frontierCap) ?? GrokDefaults.FrontierCapExps,
                },
                ActionsPerStep = actionSelection == EmlActionSelections.Off ? 1 : configuredIntake,
                Curriculum = runCurriculum,
                Learning = new CortexLearningConfig
                {
                    ConsolidationPhaseControl = usesHomeostat ? CortexConsolidationPhaseControl.Homeostat : CortexConsolidationPhaseControl.Interval,
                    IntervalConsolidationPhase = intervalCadence ?? 0,
                    GrammarBudgetBits = parse.GetValue(budget) ?? 0,
                    EvidenceWeightScale = parse.GetValue(wScale) ?? 8,
                    CrossReflect = !parse.GetValue(noCrossReflect),
                    ReplayRatio = parse.GetValue(dreamRatio) ?? 1.0,
                    SenseMask = parse.GetValue(senseMask) ?? "",
                    Breach = !parse.GetValue(noBreach),
                    Simhash = CortexConfigTokens.ParseSimhash(parse.GetValue(simhash), parse.GetValue(noSimhash)),
                    NearDupe = !parse.GetValue(noNearDupe),
                    Antiunify = !parse.GetValue(noAntiunify),
                    WallTolerance = parse.GetValue(wallTol) ?? 0.003,
                    Loom = !parse.GetValue(noLoom),
                    Shed = !parse.GetValue(noShed),
                    Rhythm = usesHomeostat && !parse.GetValue(noRhythm),
                    Homeostat = new CortexHomeostatConfig
                    {
                        Policy = usesHomeostat
                            ? CortexConfigTokens.ParseHomeostatPolicy(parse.GetValue(homeostatPolicy))
                            : HomeoPolicies.Reflex,
                        Autonomy = usesHomeostat
                            ? CortexConfigTokens.ParseHomeostatAutonomy(parse.GetValue(homeostatAutonomy))
                            : HomeostatAutonomyModes.Off,
                    },
                    Policies = new CortexPolicyLearningConfig
                    {
                        ShadowDecisions = parse.GetValue(policyShadow) ?? 8,
                        TrialHorizons =
                        [
                            parse.GetValue(policyShort) ?? 16,
                            parse.GetValue(policyMedium) ?? 64,
                            parse.GetValue(policyLong) ?? 256,
                        ],
                    },
                },
                Durability = new CortexDurabilityConfig
                {
                    CheckpointEvery = parse.GetValue(ckptEvery) ?? 0,
                    CurveEvery = parse.GetValue(curveEvery) ?? 1,
                },
                Readout = new CortexReadoutConfig
                {
                    Curve = SplitReadout(parse.GetValue(readout)),
                },
            };
            return new Cortex(config).Run();
        });
        return cmd;
    }

    // ── argv-rebuild helpers (the ADAPTER-ARGV bridge) — append `key value` only when the option was set. ──
    private static void AddOpt(List<string> argv, string key, string? val) { if (!string.IsNullOrEmpty(val)) { argv.Add(key); argv.Add(val); } }
    private static void AddInt(List<string> argv, string key, int? val)    { if (val is int v) { argv.Add(key); argv.Add(v.ToString()); } }
    private static void AddLng(List<string> argv, string key, long? val)   { if (val is long v) { argv.Add(key); argv.Add(v.ToString()); } }
    private static void AddDbl(List<string> argv, string key, double? val) { if (val is double v) { argv.Add(key); argv.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); } }

    private static string[] SplitReadout(string[]? values) =>
        values is null
            ? []
            : values.SelectMany(v => v.Split(',')).Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();

    // ── stats ──  health: is the grammar a language (Zipf ≈ −1) or a hoard (flat)? + the compression numbers.
    // The `stats` verb body, homed beside its Stats() builder since the Cli god-partial dissolved.
    private static int DumpStats(byte[] corpus)
    {
        var (_, n, r) = Engine.Induce(corpus);
        int comp = r.Compressed.Length, rules = r.Rules.Length;
        double ratio = n == 0 ? 0 : (double)comp / n;
        double zipf = ZipfSlope(r);
        double conc = Engine.ConcentrationOf(r);
        double avgExp = rules == 0 ? 0 : r.Rules.Average(rule => Cogito.Induct.Reconstruct.Expand(r.Rules, rule.Pattern).Length);

        Console.WriteLine($"stats · {n} bytes");
        Console.WriteLine($"  compressed : {comp} symbols  ({ratio:P1} of byte count)");
        Console.WriteLine($"  rules      : {rules}  (avg expansion {avgExp:F1} bytes/rule)");
        Console.WriteLine($"  mdl saved  : {r.TotalSavings}");
        Console.WriteLine($"  zipf slope : {zipf:F3}   ({Health(zipf)})");
        Console.WriteLine($"  concentr.  : {conc:F3}   (Gini of chunk usage; high = healthy Zipfian structure, a drop = collapse)");
        return 0;
    }
}
