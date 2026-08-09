namespace Cogito.Cli;

using System.CommandLine;
using Cogito;   // the Drive-domain verb bodies (Farm/DomainWalk/KillLine/Seriate/CritLock/GrokBell/Anchor/AnnealEvict/ClassTower/Radula)

// ── DRIVE COMMANDS ──  the Drive-domain cluster. Every verb is ADAPTER-ARGV: a typed Option<T>/Argument<T>
// set (mirroring each body's Args.* flags + defaults) whose SetAction rebuilds the minimal argv the stable-API
// body expects and forwards to its public entry. The typed CLI + generated help enumerate each verb's knobs
// (the bodies carry no usage string of their own); the body's Args parse resolves identically. Bodies untouched.
//
// DEFAULTS DISCIPLINE — the body is the source-of-record. Value-flags are Option<int?>/<double?>/<string?> with
// NO DefaultValueFactory: absent ⇒ the option isn't forwarded, so the body applies ITS default (byte-identical
// to today's path). Help text quotes the body's default for the reader; the DEFAULT ITSELF is never duplicated
// into this layer (classtower's computed `theta = Max(8, n/16)` in particular MUST stay body-side — forwarding a
// materialized theta would fork the run when --n changes). Switches are Option<bool>; INVERTED switches
// (--no-null, --no-barrier: default ON, present ⇒ off) forward the literal --no-* token when true, matching
// `!args.Contains("--no-*")`.
internal static class DriveCommands
{
    // Verbs are registered by CliRoot: the drives land under `drive`; killline/percolate/classtower are research
    // probes (registered under `probe`). The old `farm` verb is DELETED — its `--cortex` fork is now the top-level
    // `cortex` flagship (PrototypeCommands.Cortex -> Cortex); Farm.Drive itself is shed by the Mesh lane.

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  domainwalk — multi-node cross-domain coupling walk (nodes = domains). ≥2 file positionals + 4 knobs.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command CreateMesh()
    {
        Argument<string[]> corpora = new("corpora") { Arity = ArgumentArity.ZeroOrMore, Description = "corpus files; one neuron per file in the routed ring" };
        Option<string?> resume = new("--resume") { Description = "resume a routed mesh run dir from checkpoint.bin" };
        Option<bool> verify = new("--verify") { Description = "resume-only: round-trip checkpoint readout, no drive" };
        Option<int?> steps = new("--steps") { Description = "drive steps, or resume horizon extension (default 200)" };
        Option<int?> block = new("--block") { Description = "generation block length (default 700)" };
        Option<int?> maxBlock = new("--maxblock") { Description = "max generated block bytes (default 16384)" };
        Option<int?> mintSpans = new("--mintspans") { Description = "intrinsic dream spans per neuron step (default 4)" };
        Option<int?> seedSpans = new("--seedspans") { Description = "bootstrap corpus spans per neuron (default 3)" };
        Option<double?> lambda = new("--lambda") { Description = "metabolism novelty decay (default 0.3)" };
        Option<string?> gen = new("--gen") { Description = "generator preset: metabolic|markov|mcmc|coupling|nodebirth" };
        Option<double?> affFloor = new("--affloor") { Description = "nodebirth affinity floor (default 1.0)" };
        Option<int?> wScale = new("--wscale") { Description = "provenance evidence weight, power of two (default 8)" };
        Option<string?> seed = CliShared.SeedOpt("mesh LCG seed (hex, default C0117011)");
        Option<int?> checkpointEvery = new("--checkpoint-every") { Description = "checkpoint cadence in steps (default 25; 0 = off)" };
        Option<int?> mix = new("--mix") { Description = "real MIX cadence after drain (default 0)" };
        Option<int?> mixSpans = new("--mixspans") { Description = "real spans per neuron per MIX event (default 1)" };
        Option<double?> dreamRatio = new("--dreamratio") { Description = "dream stock cap vs real (default 0 = unbounded)" };
        Option<bool> dreamCapTotal = new("--dreamcap-total") { Description = "cap total dreams, not only unvested dreams" };
        Option<int?> night = new("--night") { Description = "aestivation cadence (default 32; 0 = off)" };
        Option<bool> meshHomeo = new("--mesh-homeo") { Description = "arm the mesh criticality homeostat" };
        Option<double?> meshFloor = new("--mesh-floor") { Description = "homeostat boredom floor (default 0.05)" };
        Option<double?> meshGain = new("--mesh-gain") { Description = "homeostat integral gain (default 0.30)" };

        Command cmd = new("mesh", "routed-neuron mesh: main world tape plus intrinsic ring stimuli")
        {
            corpora, resume, verify, steps, block, maxBlock, mintSpans, seedSpans, lambda, gen, affFloor, wScale, seed,
            checkpointEvery, mix, mixSpans, dreamRatio, dreamCapTotal, night, meshHomeo, meshFloor, meshGain
        };
        cmd.SetAction(parse =>
        {
            List<string> argv = new() { "mesh" };
            foreach (string corpus in parse.GetValue(corpora) ?? []) argv.Add(corpus);
            AddOpt(argv, "--resume", parse.GetValue(resume));
            if (parse.GetValue(verify)) argv.Add("--verify");
            if (parse.GetValue(dreamCapTotal)) argv.Add("--dreamcap-total");
            if (parse.GetValue(meshHomeo)) argv.Add("--mesh-homeo");
            AddInt(argv, "--steps", parse.GetValue(steps));
            AddInt(argv, "--block", parse.GetValue(block));
            AddInt(argv, "--maxblock", parse.GetValue(maxBlock));
            AddInt(argv, "--mintspans", parse.GetValue(mintSpans));
            AddInt(argv, "--seedspans", parse.GetValue(seedSpans));
            AddDbl(argv, "--lambda", parse.GetValue(lambda));
            AddOpt(argv, "--gen", parse.GetValue(gen));
            AddDbl(argv, "--affloor", parse.GetValue(affFloor));
            AddInt(argv, "--wscale", parse.GetValue(wScale));
            AddOpt(argv, "--seed", parse.GetValue(seed));
            AddInt(argv, "--checkpoint-every", parse.GetValue(checkpointEvery));
            AddInt(argv, "--mix", parse.GetValue(mix));
            AddInt(argv, "--mixspans", parse.GetValue(mixSpans));
            AddDbl(argv, "--dreamratio", parse.GetValue(dreamRatio));
            AddInt(argv, "--night", parse.GetValue(night));
            AddDbl(argv, "--mesh-floor", parse.GetValue(meshFloor));
            AddDbl(argv, "--mesh-gain", parse.GetValue(meshGain));
            return global::Cogito.Mesh.Run(argv.ToArray());
        });
        return cmd;
    }

    public static Command DomainWalk()
    {
        var files    = new Argument<string[]>("files") { Arity = ArgumentArity.OneOrMore, Description = "≥2 domain corpora (one node per file); non-files ignored" };
        var steps    = new Option<int?>("--steps")     { Description = "walk steps (default 60)" };
        var cross    = new Option<double?>("--cross")  { Description = "cross-domain coupling weight (default 3.0)" };
        var perDom   = new Option<int?>("--perdomain") { Description = "bytes/domain cap (default 180000)" };
        var render   = new Option<int?>("--render")    { Description = "render width (default 44)" };

        var cmd = new Command("domainwalk", "multi-node cross-domain coupling walk (nodes = domains)")
        {
            files, steps, cross, perDom, render
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "domainwalk" };
            foreach (var f in parse.GetValue(files) ?? []) argv.Add(f);
            AddInt(argv, "--steps",     parse.GetValue(steps));
            AddDbl(argv, "--cross",     parse.GetValue(cross));
            AddInt(argv, "--perdomain", parse.GetValue(perDom));
            AddInt(argv, "--render",    parse.GetValue(render));
            return global::Cogito.DomainWalk.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  killline — the pre-registered falsifier gate. DUAL --check: a bare SWITCH (grade landed run dirs from
    //  the positionals) OR a value (also grades — the body accepts either via `check.Length>0 || Has(--check)`).
    //  Modeled as `--check` bool + variadic run-dir positionals; manifest mode uses the --corpus/--c1/2/3 knobs.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command KillLine()
    {
        var runDirs = new Argument<string[]>("run-dirs") { Arity = ArgumentArity.ZeroOrMore, Description = "with --check: landed run dir(s) to grade (curve.tsv)" };
        var check   = new Option<bool>("--check")        { Description = "grade the pre-registered falsifiers against the run-dir positional(s)" };
        var corpus  = new Option<string?>("--corpus")    { Description = "manifest mode: corpus dir (default <corpus-dir>)" };
        var c1      = new Option<string?>("--c1")        { Description = "manifest mode: domain 1 (default <domain1>)" };
        var c2      = new Option<string?>("--c2")        { Description = "manifest mode: domain 2 (default <domain2>)" };
        var c3      = new Option<string?>("--c3")        { Description = "manifest mode: domain 3 (default <domain3>)" };

        var cmd = new Command("killline", "the pre-registered falsifier gate (launch manifest OR --check landed curves)")
        {
            runDirs, check, corpus, c1, c2, c3
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "killline" };
            // --check FIRST — the body branches on it before touching the manifest knobs, and consumes the
            // run-dir positionals as the curves to grade (its Positionals(args,1,"--check") drops the flag token).
            if (parse.GetValue(check)) argv.Add("--check");
            foreach (var d in parse.GetValue(runDirs) ?? []) argv.Add(d);
            AddOpt(argv, "--corpus", parse.GetValue(corpus));
            AddOpt(argv, "--c1",     parse.GetValue(c1));
            AddOpt(argv, "--c2",     parse.GetValue(c2));
            AddOpt(argv, "--c3",     parse.GetValue(c3));
            return global::Cogito.KillLine.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  seriate — affinity seriation defrag (couplings-guided tape re-order). No positional; the --no-null INVERTED
    //  switch (null control ON by default; --no-null seals it off) is the quirk.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Seriate()
    {
        var fam       = new Option<int?>("--fam")       { Description = "families (default 8)" };
        var morph     = new Option<int?>("--morph")     { Description = "morphemes/family (default 90)" };
        var win       = new Option<int?>("--win")       { Description = "morph window (default 12)" };
        var overlap   = new Option<int?>("--overlap")   { Description = "adjacent-family morpheme overlap (default 0)" };
        var words     = new Option<int?>("--words")     { Description = "words/family (default 12)" };
        var phrases   = new Option<int?>("--phrases")   { Description = "phrases/family (default 16)" };
        var templates = new Option<int?>("--templates") { Description = "templates/family (default 12)" };
        var lines     = new Option<int?>("--lines")     { Description = "lines/family (default 60)" };
        var cycles    = new Option<int?>("--cycles")    { Description = "repair cycles (default 4)" };
        var phi       = new Option<double?>("--phi")    { Description = "PPMI co-count bridge weight, 0 = IDF only (default 1.0)" };
        var pool      = new Option<string?>("--pool")   { Description = "mixed history to repair: roundrobin|shuffle|blocked (default roundrobin)" };
        var seed      = CliShared.SeedOpt("seriate LCG seed (hex, default C0117011)");
        var noNull    = new Option<bool>("--no-null")   { Description = "seal off the null control (default: null control ON)" };

        var cmd = new Command("seriate", "affinity seriation defrag — couplings-guided tape re-order")
        {
            fam, morph, win, overlap, words, phrases, templates, lines, cycles, phi, pool, seed, noNull
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "seriate" };
            if (parse.GetValue(noNull)) argv.Add("--no-null");   // INVERTED: forward only when the reader opted OUT
            AddInt(argv, "--fam",       parse.GetValue(fam));
            AddInt(argv, "--morph",     parse.GetValue(morph));
            AddInt(argv, "--win",       parse.GetValue(win));
            AddInt(argv, "--overlap",   parse.GetValue(overlap));
            AddInt(argv, "--words",     parse.GetValue(words));
            AddInt(argv, "--phrases",   parse.GetValue(phrases));
            AddInt(argv, "--templates", parse.GetValue(templates));
            AddInt(argv, "--lines",     parse.GetValue(lines));
            AddInt(argv, "--cycles",    parse.GetValue(cycles));
            AddDbl(argv, "--phi",       parse.GetValue(phi));
            AddOpt(argv, "--pool",      parse.GetValue(pool));
            AddOpt(argv, "--seed",      parse.GetValue(seed));
            return global::Cogito.Seriate.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  critlock — the critical-point-shift kill-line (frontier vs random-global). No positional; --corpus
    //  is the mode-switch (empty ⇒ synthetic families; a dir ⇒ real code, each glob-matched file a domain).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command CritLock()
    {
        var fam       = new Option<int?>("--fam")       { Description = "families (default 6)" };
        var morph     = new Option<int?>("--morph")     { Description = "morphemes/family (default 96)" };
        var win       = new Option<int?>("--win")       { Description = "morph window (default 12)" };
        var overlap   = new Option<int?>("--overlap")   { Description = ">0: adjacent families share morphemes → a walkable bridge chain (default 4)" };
        var words     = new Option<int?>("--words")     { Description = "words/family (default 12)" };
        var phrases   = new Option<int?>("--phrases")   { Description = "phrases/family (default 16)" };
        var templates = new Option<int?>("--templates") { Description = "templates/family (default 12)" };
        var lines     = new Option<int?>("--lines")     { Description = "lines/family (default 60)" };
        var batch     = new Option<int?>("--batch")     { Description = "spans/step (default 3)" };
        var cv        = new Option<double?>("--cv")     { Description = "lock-line CV floor (default 0.15)" };
        var lockL     = new Option<int?>("--lock")      { Description = "hysteresis depth, rounds (default 3)" };
        var corpus    = new Option<string?>("--corpus") { Description = "mode-switch: real-code dir (each file a domain); empty ⇒ synthetic (default \"\")" };
        var glob      = new Option<string?>("--glob")   { Description = "with --corpus dir: file globs (default *.cs)" };
        var seed      = CliShared.SeedOpt("critlock LCG seed (hex, default C0117011)");

        var cmd = new Command("critlock", "the critical-point-shift kill-line — frontier vs random-global intake")
        {
            fam, morph, win, overlap, words, phrases, templates, lines, batch, cv, lockL, corpus, glob, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "critlock" };
            AddInt(argv, "--fam",       parse.GetValue(fam));
            AddInt(argv, "--morph",     parse.GetValue(morph));
            AddInt(argv, "--win",       parse.GetValue(win));
            AddInt(argv, "--overlap",   parse.GetValue(overlap));
            AddInt(argv, "--words",     parse.GetValue(words));
            AddInt(argv, "--phrases",   parse.GetValue(phrases));
            AddInt(argv, "--templates", parse.GetValue(templates));
            AddInt(argv, "--lines",     parse.GetValue(lines));
            AddInt(argv, "--batch",     parse.GetValue(batch));
            AddDbl(argv, "--cv",        parse.GetValue(cv));
            AddInt(argv, "--lock",      parse.GetValue(lockL));
            AddOpt(argv, "--corpus",    parse.GetValue(corpus));
            AddOpt(argv, "--glob",      parse.GetValue(glob));
            AddOpt(argv, "--seed",      parse.GetValue(seed));
            return global::Cogito.CritLock.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  grokbell — the bytes-to-grok kill-line over real multi-domain corpora (frontier self-schedule). ≥2
    //  domain positionals (a FILE = a domain; a DIR contributes each glob-matched file) + the belled knobs.
    //  Entry is GrokBell.KillLine (NOT a .Run) — fully-qualified so `using Cogito;` doesn't bind bare KillLine.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command GrokBell()
    {
        var domains     = new Argument<string[]>("domains") { Arity = ArgumentArity.OneOrMore, Description = "≥2 domain corpora (FILE = a domain; DIR = each glob-matched file)" };
        var steps       = new Option<int?>("--steps")       { Description = "drive steps (default 120)" };
        var batch       = new Option<int?>("--batch")       { Description = "spans/step (default 4)" };
        var cv          = new Option<double?>("--cv")       { Description = "lock-line CV floor (default 0.15)" };
        var band        = new Option<double?>("--band")     { Description = "k-band width (default 1.5)" };
        var lockL       = new Option<int?>("--lock")        { Description = "hysteresis depth, rounds (default 3)" };
        var glob        = new Option<string?>("--glob")     { Description = "globs a DIR arg pulls (default *.cs,*.py,*.md,*.txt)" };
        var mix         = new Option<int?>("--mix")         { Description = "MIX rail cadence, 0 = drain-only (default 0)" };
        var domStride   = new Option<int?>("--domstride")   { Description = "domain-stride spans (default 6)" };
        var frontierCap = new Option<int?>("--frontiercap") { Description = "frontier cap exponents (default 400)" };
        var seedSpans   = new Option<int?>("--seedspans")   { Description = "seed spans/domain (default 3)" };
        var stride      = new Option<int?>("--stride")      { Description = "re-induce stride bytes (default 5000)" };
        var perDom      = new Option<int?>("--perdomain")   { Description = "cap trainable spans/domain (default 120)" };
        var seed        = CliShared.SeedOpt("grokbell LCG seed (hex, default C0117011)");

        var cmd = new Command("grokbell", "the bytes-to-grok kill-line over real multi-domain corpora (frontier self-schedule)")
        {
            domains, steps, batch, cv, band, lockL, glob, mix, domStride, frontierCap, seedSpans, stride, perDom, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "grokbell" };
            foreach (var d in parse.GetValue(domains) ?? []) argv.Add(d);
            AddInt(argv, "--steps",       parse.GetValue(steps));
            AddInt(argv, "--batch",       parse.GetValue(batch));
            AddDbl(argv, "--cv",          parse.GetValue(cv));
            AddDbl(argv, "--band",        parse.GetValue(band));
            AddInt(argv, "--lock",        parse.GetValue(lockL));
            AddOpt(argv, "--glob",        parse.GetValue(glob));
            AddInt(argv, "--mix",         parse.GetValue(mix));
            AddInt(argv, "--domstride",   parse.GetValue(domStride));
            AddInt(argv, "--frontiercap", parse.GetValue(frontierCap));
            AddInt(argv, "--seedspans",   parse.GetValue(seedSpans));
            AddInt(argv, "--stride",      parse.GetValue(stride));
            AddInt(argv, "--perdomain",   parse.GetValue(perDom));
            AddOpt(argv, "--seed",        parse.GetValue(seed));
            return global::Cogito.GrokBell.KillLine(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  percolate — the anchor-percolation held-out sweep. No positional; --ks is a COMMA-LIST (the body
    //  splits it), --verbose a switch.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Percolate()
    {
        var n       = new Option<int?>("--n")       { Description = "corpus size (default 800)" };
        var seed    = CliShared.SeedOpt("percolate LCG seed (hex, default A2C407)");
        var iter    = new Option<int?>("--iter")    { Description = "percolation iterations (default 8)" };
        var cand    = new Option<int?>("--cand")    { Description = "candidates/iter (default 8)" };
        var knn     = new Option<int?>("--knn")     { Description = "k nearest for the graph (default 3)" };
        var minw    = new Option<int?>("--minw")    { Description = "min edge weight (default 2)" };
        var floor   = new Option<double?>("--floor"){ Description = "affinity floor (default 1.0)" };
        var heldK   = new Option<int?>("--heldk")   { Description = "held-out K (default 16)" };
        var ablateK = new Option<int?>("--ablatek") { Description = "ablation K (default 12)" };
        var ks      = new Option<string?>("--ks")   { Description = "comma-list of K sweep points (default 0,2,4,8,12,16,24)" };
        var verbose = new Option<bool>("--verbose") { Description = "per-iter detail" };

        var cmd = new Command("percolate", "the anchor-percolation held-out sweep")
        {
            n, seed, iter, cand, knn, minw, floor, heldK, ablateK, ks, verbose
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "percolate" };
            if (parse.GetValue(verbose)) argv.Add("--verbose");
            AddInt(argv, "--n",       parse.GetValue(n));
            AddOpt(argv, "--seed",    parse.GetValue(seed));
            AddInt(argv, "--iter",    parse.GetValue(iter));
            AddInt(argv, "--cand",    parse.GetValue(cand));
            AddInt(argv, "--knn",     parse.GetValue(knn));
            AddInt(argv, "--minw",    parse.GetValue(minw));
            AddDbl(argv, "--floor",   parse.GetValue(floor));
            AddInt(argv, "--heldk",   parse.GetValue(heldK));
            AddInt(argv, "--ablatek", parse.GetValue(ablateK));
            AddOpt(argv, "--ks",      parse.GetValue(ks));   // comma-list forwarded verbatim; the body splits it
            return global::Cogito.Anchor.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  annealevict — the cap-eviction journal ablation (LOWER = demote vs delete). Two mode-switches:
    //  --emit-corpus (write the ladder train set and exit) and --corpus (run a real file/dir). INVERTED
    //  --no-barrier (barrier ON by default), --tabu switch, --lower is a demote|delete string.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command AnnealEvict()
    {
        var emitCorpus = new Option<string?>("--emit-corpus") { Description = "mode-switch: write the ladder train set to PATH and exit" };
        var corpus     = new Option<string?>("--corpus")      { Description = "mode-switch: run a real file/dir (*.cs), every-8th-line held out (default \"\")" };
        var cap        = new Option<int?>("--cap")            { Description = "tape byte cap (default 49152)" };
        var cycles     = new Option<int?>("--cycles")         { Description = "breach/lower cycles (default 5)" };
        var quota      = new Option<int?>("--quota")          { Description = "mint quota/cycle (default 128)" };
        var header     = new Option<double?>("--header")      { Description = "per-rule header bits (default 1.024)" };
        var seed       = CliShared.SeedOpt("annealevict LCG seed (hex, default C0117011)");
        var outDir     = new Option<string?>("--out")         { Description = "output dir (default scratchpad/breach_lower)" };
        var lower      = new Option<string?>("--lower")       { Description = "LOWER mode: demote (cortex-faithful) | delete (harsher) (default demote)" };
        var tabu       = new Option<bool>("--tabu")           { Description = "tabu the evicted content (anti-ratchet; default OFF)" };
        var noBarrier  = new Option<bool>("--no-barrier")     { Description = "disable the '\\n' merge barrier (straddle-trap ablation; default: barrier ON)" };

        var cmd = new Command("annealevict", "the cap-eviction journal ablation (LOWER = demote vs delete)")
        {
            emitCorpus, corpus, cap, cycles, quota, header, seed, outDir, lower, tabu, noBarrier
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "annealevict" };
            if (parse.GetValue(tabu))      argv.Add("--tabu");
            if (parse.GetValue(noBarrier)) argv.Add("--no-barrier");   // INVERTED
            AddOpt(argv, "--emit-corpus", parse.GetValue(emitCorpus));
            AddOpt(argv, "--corpus",      parse.GetValue(corpus));
            AddInt(argv, "--cap",         parse.GetValue(cap));
            AddInt(argv, "--cycles",      parse.GetValue(cycles));
            AddInt(argv, "--quota",       parse.GetValue(quota));
            AddDbl(argv, "--header",      parse.GetValue(header));
            AddOpt(argv, "--seed",        parse.GetValue(seed));
            AddOpt(argv, "--out",         parse.GetValue(outDir));
            AddOpt(argv, "--lower",       parse.GetValue(lower));
            return global::Cogito.AnnealEvict.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  classtower — context-conditional class tower via LCA. --theta's default is COMPUTED body-side from --n
    //  (Max(8, n/16)); it is NOT materialized here — absent ⇒ the body derives it, so a swept --n stays
    //  self-consistent. Only forwarded when the reader overrides it explicitly.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command ClassTower()
    {
        var n     = new Option<int?>("--n")     { Description = "corpus size (default 2500)" };
        var seed  = CliShared.SeedOpt("classtower LCG seed (hex, default C0117011)");
        var theta = new Option<int?>("--theta") { Description = "keep-frequency threshold (default computed: Max(8, n/16))" };

        var cmd = new Command("classtower", "context-conditional class tower via LCA (no gold labels, no LLM)")
        {
            n, seed, theta
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "classtower" };
            AddInt(argv, "--n",     parse.GetValue(n));
            AddOpt(argv, "--seed",  parse.GetValue(seed));
            AddInt(argv, "--theta", parse.GetValue(theta));   // absent ⇒ body computes Max(8, n/16) — do NOT default here
            return global::Cogito.ClassTower.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  intake — residual-driven local intake at depth (frontier vs random-global). No positional; --corpus
    //  is the mode-switch (real code = the synthetic→natural transfer test). Two extra control switches
    //  (--negctrl shuffled families, --flat no deep scale) the body reads via args.Contains.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command Intake()
    {
        var fam       = new Option<int?>("--fam")       { Description = "families (default 8)" };
        var morph     = new Option<int?>("--morph")     { Description = "morphemes/family (default 90)" };
        var win       = new Option<int?>("--win")       { Description = "morph window (default 12)" };
        var overlap   = new Option<int?>("--overlap")   { Description = "0 = disjoint islands; >0 adds a bridge (default 0)" };
        var words     = new Option<int?>("--words")     { Description = "words/family (default 12)" };
        var phrases   = new Option<int?>("--phrases")   { Description = "phrases/family (default 16)" };
        var templates = new Option<int?>("--templates") { Description = "templates/family — the L3 deep scale (default 12)" };
        var lines     = new Option<int?>("--lines")     { Description = "lines/family (default 60)" };
        var batch     = new Option<int?>("--batch")     { Description = "spans/step (default 3)" };
        var seedLines = new Option<int?>("--seedlines") { Description = "bootstrap seed lines/family (default 1)" };
        var pool      = new Option<string?>("--pool")   { Description = "feed order: roundrobin|blocked|shuffle (default roundrobin)" };
        var corpus    = new Option<string?>("--corpus") { Description = "mode-switch: real-code file/dir (families = files) — the transfer test (default \"\")" };
        var glob      = new Option<string?>("--glob")   { Description = "with --corpus dir: comma-separated globs (default *.cs)" };
        var seed      = CliShared.SeedOpt("intake LCG seed (hex, default C0117011)");
        var negctrl   = new Option<bool>("--negctrl")   { Description = "negative control: shuffled families" };
        var flat      = new Option<bool>("--flat")      { Description = "negative control: no deep template scale" };

        var cmd = new Command("intake", "residual-driven local intake at depth (frontier vs random-global)")
        {
            fam, morph, win, overlap, words, phrases, templates, lines, batch, seedLines, pool, corpus, glob, seed, negctrl, flat
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "intake" };
            if (parse.GetValue(negctrl)) argv.Add("--negctrl");
            if (parse.GetValue(flat))    argv.Add("--flat");
            AddInt(argv, "--fam",       parse.GetValue(fam));
            AddInt(argv, "--morph",     parse.GetValue(morph));
            AddInt(argv, "--win",       parse.GetValue(win));
            AddInt(argv, "--overlap",   parse.GetValue(overlap));
            AddInt(argv, "--words",     parse.GetValue(words));
            AddInt(argv, "--phrases",   parse.GetValue(phrases));
            AddInt(argv, "--templates", parse.GetValue(templates));
            AddInt(argv, "--lines",     parse.GetValue(lines));
            AddInt(argv, "--batch",     parse.GetValue(batch));
            AddInt(argv, "--seedlines", parse.GetValue(seedLines));
            AddOpt(argv, "--pool",      parse.GetValue(pool));
            AddOpt(argv, "--corpus",    parse.GetValue(corpus));
            AddOpt(argv, "--glob",      parse.GetValue(glob));
            AddOpt(argv, "--seed",      parse.GetValue(seed));
            return global::Cogito.Radula.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  frontierbench — the O(pool) full-scan-vs-frontier candidacy bench. --pools is a COMMA-LIST (the body
    //  splits it). Entry is Radula.FrontierBench.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    public static Command FrontierBench()
    {
        var pools  = new Option<string?>("--pools") { Description = "comma-list of pool sizes to sweep (default 2000,50000,1000000)" };
        var draws  = new Option<int?>("--draws")    { Description = "draws/pool (default 10)" };
        var batch  = new Option<int?>("--batch")    { Description = "spans/draw (default 8)" };
        var cap    = new Option<int?>("--cap")      { Description = "frontier cap exponents (default 400)" };
        var warm   = new Option<int?>("--warm")     { Description = "warm-up spans the grammar is induced over (default 1000)" };
        var fidMax = new Option<int?>("--fidmax")   { Description = "full-scan arm cap — the O(pool) side being retired (default 60000)" };
        var seed   = CliShared.SeedOpt("frontierbench LCG seed (hex, default F07711E4)");

        var cmd = new Command("frontierbench", "the O(pool) full-scan-vs-frontier candidacy bench")
        {
            pools, draws, batch, cap, warm, fidMax, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "frontierbench" };
            AddOpt(argv, "--pools",  parse.GetValue(pools));   // comma-list forwarded verbatim; the body splits it
            AddInt(argv, "--draws",  parse.GetValue(draws));
            AddInt(argv, "--batch",  parse.GetValue(batch));
            AddInt(argv, "--cap",    parse.GetValue(cap));
            AddInt(argv, "--warm",   parse.GetValue(warm));
            AddInt(argv, "--fidmax", parse.GetValue(fidMax));
            AddOpt(argv, "--seed",   parse.GetValue(seed));
            return global::Cogito.Radula.FrontierBench(argv.ToArray());
        });
        return cmd;
    }

    // ── argv-rebuild helpers (the ADAPTER-ARGV bridge) — append `key value` only when the option was set. ──
    private static void AddOpt(List<string> argv, string key, string? val) { if (!string.IsNullOrEmpty(val)) { argv.Add(key); argv.Add(val); } }
    private static void AddInt(List<string> argv, string key, int? val)    { if (val is int v) { argv.Add(key); argv.Add(v.ToString()); } }
    private static void AddDbl(List<string> argv, string key, double? val) { if (val is double v) { argv.Add(key); argv.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); } }
}
