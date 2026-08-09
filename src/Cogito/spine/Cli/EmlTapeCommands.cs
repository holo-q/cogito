using System.CommandLine;
using Cogito;   // the EML/tape verb bodies (all namespace Cogito)

namespace Cogito.Cli
{

// ── EML + TAPE COMMANDS ──  the EML dream-calc verbs + tape resume. AOT-safe: the EXPLICIT api
// (typed Option<T>/Argument<T>, values pulled via ParseResult.GetValue — never SetHandler reflection binding).
// Registration by CliRoot: dreamcalc rides top-level (flagship); emlbench/sheffer/antiunify/mintbench under
// `eml`; semgrammar under `probe`; resume under `tape`.
//
//   EML  flagship (dreamcalc / dreamlift) is TYPED-CALL: the CLI builds a CortexConfig with CortexEmlCurriculum;
//        the observatory drives that curriculum directly. Bench/probe siblings keep ADAPTER-ARGV where their bodies still own conditional
//        defaults, clamps, comma-lists, and probe-specific parse law.
//
//   TAPE (resume) — TYPED-CALL, not argv: a required <run-dir> Argument (System.CommandLine enforces
//        presence — replacing the old args.Length<2 usage guard) plus the flags, read straight into the
//        RunResume verb body below. resume sniffs the checkpoint magic and dispatches to the owning
    //        engine's Resume (cortex/agent); legacy trunk/mesh dialects fail loud.
internal static class EmlTapeCommands
{
    // Verbs are registered by CliRoot: dreamcalc rides top-level (flagship); emlbench/sheffer/antiunify/mintbench
    // land under the `eml` cluster; semgrammar under `probe`; resume under `tape`.

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    //  EML CLUSTER — typed curriculum config for the flagship, adapter-argv for probe siblings.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    // ── dreamcalc ──  the EML dream-calculator. Regrade/semantic are corpus audits over an existing run;
    //    the typed config is ordinary CortexConfig + CortexEmlCurriculum, without argv round-tripping.
    internal static Command ReplayCalc()
    {
        Option<string?> lawProbe = new("--law-probe")          { Description = "read-only mature-data anti-unify funnel over banked mints + regrade journals" };
        Option<bool> fertilityAssay = new("--fertility-assay")  { Description = "matched-checkpoint delayed-fertility assay: actual root vs admission-suppressed shadow" };
        Option<long?> fertilityCalls = new("--fertility-calls") { Description = "logical evaluator calls beyond the base checkpoint (default 1000)" };
        Option<bool> intensionalRematch = new("--intensional-rematch") { Description = "seven-arm finite EML intensional rematch instrument" };
        Option<bool> processResidualRematch = new("--process-residual-rematch") { Description = "matched real/null process residual rematch assay" };
        Option<bool> obligationClosureAssay = new("--obligation-closure-assay") { Description = "typed EML obligation closure policy/fuel/persistence assay" };
        Option<bool> fuelledDeliberationAssay = new("--fuelled-deliberation-assay") { Description = "typed per-obligation search reservation/lease assay" };
        Option<bool> formFarmAssay = new("--form-farm-assay") { Description = "causal reward-dark exact-form farm assay" };
        Option<bool> guardedRewriteAssay = new("--guarded-rewrite-assay") { Description = "C1 typed domain guard and branch witness assay" };
        Option<bool> rung0Assay = new("--rung0-assay") { Description = "C2 bounded guarded derivation, audit, quarantine, and powered-null assay" };
        Option<long?> rematchEvaluatorCalls = new("--rematch-evaluator-calls") { Description = "logical evaluator calls per rematch arm (default 100000)" };
        Option<long?> rematchDelayCalls = new("--rematch-delay-calls") { Description = "delayed-continuation evaluator calls per rematch arm (default 10000)" };
        Option<int?> rematchReplicates = new("--rematch-replicates") { Description = "independent deterministic rematch replicates (default 3)" };
        Option<long?> rematchProcessFuel = new("--rematch-process-fuel") { Description = "negative-log series fuel per probe (default 32)" };
        Option<bool> mitmProbe = new("--mitm-probe")             { Description = "read-only semantic meet-in-the-middle search against matched fresh nulls" };
        Option<long?> mitmCalls = new("--mitm-calls")            { Description = "total logical evaluator calls per MITM/null arm (default 100000)" };
        Option<int?> mitmK = new("--mitm-k")                      { Description = "forward-index maximum odd K, capped at 11 (default 11)" };
        var regrade  = new Option<string?>("--regrade")          { Description = "retro witness ladder over an existing mint journal (mode-switch → EmlRegrade)" };
        var semComp  = new Option<string?>("--semantic-compress"){ Description = "semantic-compress drive over an existing run (mode-switch → EmlSemantic)" };
        var lift     = new Option<bool>("--anneal-len")             { Description = "the anneal-len length scheduler — plateau-gated ruler cascade (mode-switch → EML lift observatory)" };
        var steps    = new Option<int?>("--steps")    { Description = "drive steps (default 200)" };
        var batch    = new Option<int?>("--batch")    { Description = "candidates per arm per step (default 32)" };
        var sig      = new Option<int?>("--sig")      { Description = "dual-point sig figures (default 9)" };
        var stride   = new Option<int?>("--stride")   { Description = "re-induce stride (default 800)" };
        var top      = new Option<int?>("--top")      { Description = "top discoveries to report (default 12)" };
        var seed     = CliShared.SeedOpt("dreamcalc LCG seed (hex, default E311C0DE)");
        var seedk    = new Option<int?>("--seedk")    { Description = "seed shells enumerated at bootstrap (default 7)" };
        var maxlen   = new Option<int?>("--maxlen")   { Description = "sampled-program length cap (default 40)" };
        var maxenum  = new Option<int?>("--maxenum")  { Description = "OFF-arm enumeration cap (default 13)" };
        var units    = new Option<int?>("--units")    { Description = "sampled units per program (default 6)" };
        var gain     = new Option<int?>("--gain")     { Description = "chunk-frequency bias vs flat token weight (default 4)" };
        var polmix   = new Option<double?>("--polmix"){ Description = "uniform ε — the support floor's mass (default 0.125)" };
        var polenum  = new Option<double?>("--polenum"){ Description = "enum-rail ε (default 0.4)" };
        var corrob   = new Option<int?>("--corrob")   { Description = "corroboration weight (default 16)" };
        var certw    = new Option<int?>("--certw")    { Description = "certificate-gate weight (default 4)" };
        var p3x      = new Option<double?>("--p3x")    { Description = "retro ladder regime probe x (regrade / semantic-compress)" };
        var p3y      = new Option<double?>("--p3y")    { Description = "retro ladder regime probe y (regrade / semantic-compress)" };
        var file     = new Option<string?>("--file")   { Description = "mint corpus file within run (regrade / semantic-compress)" };
        var kmax     = new Option<int?>("--kmax")      { Description = "anneal-len max ruler K (default 200)" };
        var annealFactor  = new Option<double?>("--anneal-factor") { Description = "anneal-len ruler growth factor (default 1.4)" };
        var annealWin     = new Option<int?>("--anneal-win")       { Description = "anneal-len census window (default 50)" };
        var annealSustain = new Option<int?>("--anneal-sustain")   { Description = "anneal-len plateau sustain windows (default 3)" };
        var annealFrac    = new Option<double?>("--anneal-frac")   { Description = "anneal-len plateau fraction (default 0.25)" };
        var meanzBand     = new Option<double?>("--meanzband")     { Description = "anneal-len exact-tier meanz tolerance (default 0.35)" };
        var strideFrac    = new Option<double?>("--stridefrac")    { Description = "anneal-len re-induce stride fraction (default 0.05)" };
        var censusOnly    = new Option<bool>("--census-only")      { Description = "anneal-len lift gate ignores exact-tier RG lock" };
        var lockMeanz     = new Option<bool>("--lock-meanz")       { Description = "anneal-len gates on meanz only; cvz telegraphed" };
        Option<string?> emlActions = new("--eml-actions")          { Description = "EML autonomous action selection: off|round-robin|shuffled-fixed|procedure|procedure-shuffled (default off)" };
        Option<int?> policyShadow = new("--policy-shadow")         { Description = "shared Cortex policy observations before verification (default 8)" };
        Option<int?> policyShort = new("--policy-short")           { Description = "shared policy short matched-fork horizon in decisions (default 16)" };
        Option<int?> policyMedium = new("--policy-medium")         { Description = "shared policy medium matched-fork horizon in decisions (default 64)" };
        Option<int?> policyLong = new("--policy-long")             { Description = "shared policy long matched-fork horizon in decisions (default 256)" };

        var cmd = new Command("dreamcalc", "the EML dream-calculator env — dream identities out of eml(x,y)=exp(x)−ln(y), the dual-point Schanuel sieve")
        {
            lawProbe, fertilityAssay, fertilityCalls, intensionalRematch, processResidualRematch, obligationClosureAssay, fuelledDeliberationAssay, formFarmAssay, guardedRewriteAssay, rung0Assay,
            rematchEvaluatorCalls, rematchDelayCalls, rematchReplicates, rematchProcessFuel,
            mitmProbe, mitmCalls, mitmK, regrade, semComp, lift, steps, batch, sig, stride, top, seed,
            seedk, maxlen, maxenum, units, gain, polmix, polenum, corrob, certw,
            p3x, p3y, file, kmax, annealFactor, annealWin, annealSustain, annealFrac,
            meanzBand, strideFrac, censusOnly, lockMeanz, emlActions,
            policyShadow, policyShort, policyMedium, policyLong
        };
        cmd.SetAction(parse =>
        {
            EmlGenerationConfig generationConfig = new()
            {
                SeedShells = parse.GetValue(seedk) ?? global::Cogito.ReplayCalc.MountSeedK,
                MaxLength = parse.GetValue(maxlen) ?? global::Cogito.ReplayCalc.MountMaxLen,
                MaxEnumerationLength = parse.GetValue(maxenum) ?? global::Cogito.ReplayCalc.MountMaxEnum,
                SampleUnits = parse.GetValue(units) ?? global::Cogito.ReplayCalc.MountUnits,
                ChunkGain = parse.GetValue(gain) ?? global::Cogito.ReplayCalc.MountGain,
                UniformEpsilon = parse.GetValue(polmix) ?? global::Cogito.ReplayCalc.MountEps,
                EnumerationEpsilon = parse.GetValue(polenum) ?? global::Cogito.ReplayCalc.MountEpsEnum,
                CorroborationWeight = parse.GetValue(corrob) ?? global::Cogito.ReplayCalc.MountCorrobW,
                CertificateWeight = parse.GetValue(certw) ?? global::Cogito.ReplayCalc.MountCertW,
            };
            EmlActionSelections actionSelection = EmlActionSelectionTokens.Parse(parse.GetValue(emlActions));

            if (parse.GetValue(obligationClosureAssay))
                return EmlObligationClosureAssay.Run(parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig);

            if (parse.GetValue(fuelledDeliberationAssay))
                return EmlFuelledDeliberationAssay.Run(parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig);

            if (parse.GetValue(formFarmAssay))
                return EmlFormFarmAssay.Run(
                    CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                    parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig);

            if (parse.GetValue(guardedRewriteAssay))
                return EmlGuardedRewriteAssay.Run();

            if (parse.GetValue(rung0Assay))
                return EmlRung0Assay.Run();

            if (parse.GetValue(processResidualRematch))
                return EmlProcessResidualRematchAssay.Run(
                    CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                    parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                    parse.GetValue(rematchReplicates) ?? 3,
                    parse.GetValue(rematchProcessFuel) ?? 32);

            if (parse.GetValue(intensionalRematch))
                return EmlIntensionalRematchRunner.RunMatched(
                    CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                    parse.GetValue(rematchEvaluatorCalls) ?? 100_000,
                    parse.GetValue(rematchDelayCalls) ?? 10_000,
                    parse.GetValue(rematchReplicates) ?? 3,
                    parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig);

            if (parse.GetValue(mitmProbe))
                return EmlMeetInMiddle.Run(
                    CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                    parse.GetValue(mitmCalls) ?? 100_000,
                    parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                    parse.GetValue(mitmK) ?? 11);

            string? lawProbeRun = parse.GetValue(lawProbe);
            if (!string.IsNullOrWhiteSpace(lawProbeRun))
                return EmlLawProbe.Run(lawProbeRun, parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig);

            if (parse.GetValue(fertilityAssay))
            {
                int fertilityBatch = parse.GetValue(batch) ?? 1;
                if (fertilityBatch != 1)
                {
                    Console.Error.WriteLine($"--fertility-assay requires --batch 1 (ActionsPerStep==1); received {fertilityBatch}");
                    return 1;
                }
                return EmlFertilityAssay.Run(
                    CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                    parse.GetValue(fertilityCalls) ?? 1000,
                    fertilityBatch,
                    parse.GetValue(stride) ?? 800,
                    parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                    generationConfig);
            }

            var regradeRun = parse.GetValue(regrade);
            if (!string.IsNullOrWhiteSpace(regradeRun))
            {
                var argv = new List<string> { "dreamcalc", "--regrade", regradeRun };
                AddDbl(argv, "--p3x", parse.GetValue(p3x));
                AddDbl(argv, "--p3y", parse.GetValue(p3y));
                AddOpt(argv, "--file", parse.GetValue(file));
                return EmlRegrade.Run(argv.ToArray());
            }

            var semanticRun = parse.GetValue(semComp);
            if (!string.IsNullOrWhiteSpace(semanticRun))
            {
                var argv = new List<string> { "dreamcalc", "--semantic-compress", semanticRun };
                AddInt(argv, "--sig", parse.GetValue(sig));
                AddDbl(argv, "--p3x", parse.GetValue(p3x));
                AddDbl(argv, "--p3y", parse.GetValue(p3y));
                AddOpt(argv, "--file", parse.GetValue(file));
                return EmlSemantic.Run(argv.ToArray());
            }

            if (parse.GetValue(lift))
            {
                if (actionSelection != EmlActionSelections.Off)
                {
                    int actionBatch = parse.GetValue(batch) ?? 32;
                    CortexEmlCurriculum actionCurriculum = new()
                    {
                        SignatureDigits = parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                        IntakeBatch = actionBatch,
                        Actions = actionSelection,
                        Generation = generationConfig,
                        Lift = new EmlLiftGateConfig
                        {
                            MaxRuler = parse.GetValue(kmax) ?? 200,
                            Factor = parse.GetValue(annealFactor) ?? 1.4,
                            Window = parse.GetValue(annealWin) ?? 50,
                            Sustain = parse.GetValue(annealSustain) ?? 3,
                            Fraction = parse.GetValue(annealFrac) ?? 0.25,
                            MeanzBand = parse.GetValue(meanzBand) ?? 0.35,
                            StrideFraction = parse.GetValue(strideFrac) ?? 0.05,
                            CensusOnly = parse.GetValue(censusOnly),
                            LockMeanz = parse.GetValue(lockMeanz),
                        },
                    };
                    ulong actionSeed = CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL);
                    CortexConfig actionConfig = new()
                    {
                        RunName = "dreamcalc-actions",
                        Steps = parse.GetValue(steps) ?? 4000,
                        Seed = actionSeed,
                        ActionsPerStep = actionBatch,
                        Curriculum = actionCurriculum,
                        Tools = global::Cogito.ReplayCalc.CreateActionTools(),
                        ActionPolicies = global::Cogito.ReplayCalc.CreateActionPolicies(),
                        Rewards = global::Cogito.ReplayCalc.CreateRewards(),
                        Learning = CreatePolicyLearning(parse, policyShadow, policyShort, policyMedium, policyLong),
                    };
                    return new Cortex(actionConfig).Run();
                }
                return global::Cogito.ReplayCalc.RunLiftObservatory(new CortexConfig
                {
                    Steps = parse.GetValue(steps) ?? 4000,
                    Seed = CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                    Curriculum = new CortexEmlCurriculum
                    {
                        Actions = EmlActionSelections.Off,
                        SignatureDigits = parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                        IntakeBatch = parse.GetValue(batch) ?? 32,
                        Generation = generationConfig,
                        Lift = new EmlLiftGateConfig
                        {
                            MaxRuler = parse.GetValue(kmax) ?? 200,
                            Factor = parse.GetValue(annealFactor) ?? 1.4,
                            Window = parse.GetValue(annealWin) ?? 50,
                            Sustain = parse.GetValue(annealSustain) ?? 3,
                            Fraction = parse.GetValue(annealFrac) ?? 0.25,
                            MeanzBand = parse.GetValue(meanzBand) ?? 0.35,
                            StrideFraction = parse.GetValue(strideFrac) ?? 0.05,
                            CensusOnly = parse.GetValue(censusOnly),
                            LockMeanz = parse.GetValue(lockMeanz),
                        },
                    },
                });
            }

            if (actionSelection != EmlActionSelections.Off)
            {
                int actionBatch = parse.GetValue(batch) ?? 32;
                ulong actionSeed = CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL);
                CortexEmlCurriculum actionCurriculum = new()
                {
                    SignatureDigits = parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                    IntakeBatch = actionBatch,
                    Actions = actionSelection,
                    Generation = generationConfig,
                };
                CortexConfig actionConfig = new()
                {
                    RunName = "dreamcalc-actions",
                    Steps = parse.GetValue(steps) ?? 200,
                    Seed = actionSeed,
                    ActionsPerStep = actionBatch,
                    Stride = new CortexStrideConfig
                    {
                        ReinduceBytes = parse.GetValue(stride) ?? 800,
                    },
                    Curriculum = actionCurriculum,
                    Tools = global::Cogito.ReplayCalc.CreateActionTools(),
                    ActionPolicies = global::Cogito.ReplayCalc.CreateActionPolicies(),
                    Rewards = global::Cogito.ReplayCalc.CreateRewards(),
                    Learning = CreatePolicyLearning(parse, policyShadow, policyShort, policyMedium, policyLong),
                };
                return new Cortex(actionConfig).Run();
            }

            return global::Cogito.ReplayCalc.RunObservatory(new CortexConfig
            {
                Steps = parse.GetValue(steps) ?? 200,
                Seed = CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
                Stride = new CortexStrideConfig
                {
                    ReinduceBytes = parse.GetValue(stride) ?? 800,
                },
                Curriculum = new CortexEmlCurriculum
                {
                    Actions = EmlActionSelections.Off,
                    SignatureDigits = parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
                    IntakeBatch = parse.GetValue(batch) ?? 32,
                    Generation = generationConfig,
                },
            }, parse.GetValue(top) ?? 12);
        });
        return cmd;
    }

    private static CortexLearningConfig CreatePolicyLearning(
        ParseResult parse,
        Option<int?> shadow,
        Option<int?> shortHorizon,
        Option<int?> mediumHorizon,
        Option<int?> longHorizon)
        => new()
        {
            Policies = new CortexPolicyLearningConfig
            {
                ShadowDecisions = parse.GetValue(shadow) ?? 8,
                TrialHorizons =
                [
                    parse.GetValue(shortHorizon) ?? 16,
                    parse.GetValue(mediumHorizon) ?? 64,
                    parse.GetValue(longHorizon) ?? 256,
                ],
            },
        };

    // ── emlbench ──  PASS (grammar pinned) vs LOOP (re-induce spiral). The CONDITIONAL-DEFAULT verb: the body
    //    reads --polenum ONLY when --with-enum is present (else the enum-rail ε is forced 0.0). We pass both
    //    flags through raw; the body owns the gate — so passing --polenum without --with-enum is inert exactly
    //    as it is in the current verb. --sig's default is ReplayCalc.MountSig (=9), applied by the body.
    internal static Command EmlBench()
    {
        var steps    = new Option<int?>("--steps")     { Description = "drive steps (default 300)" };
        var batch    = new Option<int?>("--batch")     { Description = "candidates per arm per step (default 32)" };
        var stride   = new Option<int?>("--stride")    { Description = "re-induce stride (default 800)" };
        var sig      = new Option<int?>("--sig")       { Description = "dual-point sig figures (default 9)" };
        var seed     = CliShared.SeedOpt("emlbench LCG seed (hex, default E311C0DE)");
        var holdout  = new Option<double?>("--holdout"){ Description = "held-out fraction of named targets (default 0.5)" };
        var withEnum = new Option<bool>("--with-enum") { Description = "arm the enum-rail (gates --polenum — off ⇒ ε=0)" };
        var polenum  = new Option<double?>("--polenum"){ Description = "enum-rail ε — READ ONLY under --with-enum (default 0.4)" };
        var seedk    = new Option<int?>("--seedk")     { Description = "seed shells (default 7)" };
        var maxlen   = new Option<int?>("--maxlen")    { Description = "sampled-program length cap (default 40)" };
        var maxenum  = new Option<int?>("--maxenum")   { Description = "enumeration cap (default 13)" };
        var units    = new Option<int?>("--units")     { Description = "sampled units per program (default 6)" };
        var gain     = new Option<int?>("--gain")      { Description = "chunk-frequency bias (default 4)" };
        var polmix   = new Option<double?>("--polmix") { Description = "uniform ε (default 0.125)" };
        var corrob   = new Option<int?>("--corrob")    { Description = "corroboration weight (default 16)" };
        var certw    = new Option<int?>("--certw")     { Description = "certificate-gate weight (default 4)" };

        var cmd = new Command("emlbench", "PASS (grammar pinned) vs LOOP (re-induce spiral) — same seed/budget/verifier, generalization via a held-out split")
        {
            steps, batch, stride, sig, seed, holdout, withEnum, polenum,
            seedk, maxlen, maxenum, units, gain, polmix, corrob, certw
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "emlbench" };
            if (parse.GetValue(withEnum)) argv.Add("--with-enum");
            AddInt(argv, "--steps",   parse.GetValue(steps));
            AddInt(argv, "--batch",   parse.GetValue(batch));
            AddInt(argv, "--stride",  parse.GetValue(stride));
            AddInt(argv, "--sig",     parse.GetValue(sig));
            AddOpt(argv, "--seed",    parse.GetValue(seed));
            AddDbl(argv, "--holdout", parse.GetValue(holdout));
            AddDbl(argv, "--polenum", parse.GetValue(polenum));
            AddInt(argv, "--seedk",   parse.GetValue(seedk));
            AddInt(argv, "--maxlen",  parse.GetValue(maxlen));
            AddInt(argv, "--maxenum", parse.GetValue(maxenum));
            AddInt(argv, "--units",   parse.GetValue(units));
            AddInt(argv, "--gain",    parse.GetValue(gain));
            AddDbl(argv, "--polmix",  parse.GetValue(polmix));
            AddInt(argv, "--corrob",  parse.GetValue(corrob));
            AddInt(argv, "--certw",   parse.GetValue(certw));
            return global::Cogito.EmlBench.Run(argv.ToArray());   // fully-qualified: the method is also named EmlBench()
        });
        return cmd;
    }

    // ── chunk-micro-assay ──  direct proposal-kernel micro-assay. This deliberately does not instantiate Cortex;
    //    it isolates whether verified target chunks compose better than the two fixed proposal nulls.
    internal static Command ChunkMicroAssay()
    {
        Option<long?> calls = new("--calls") { Description = "logical evaluator calls per arm (default 100000)" };
        Option<int?> seedK = new("--seedk") { Description = "shared exhaustive seed shell (default 7)" };
        Option<int?> maxLen = new("--maxlen") { Description = "maximum generated EML program length (default 80)" };
        Option<int?> units = new("--units") { Description = "assembly units per generated program (default 6)" };
        Option<int?> gain = new("--gain") { Description = "captured exact-program chunk weight (default 4)" };
        Option<int?> sig = new("--sig") { Description = "dual-point signature figures (default 9)" };
        Option<string?> seed = CliShared.SeedOpt("chunk micro-assay LCG seed (hex, default E311C0DE)");

        Command command = new("chunk-micro-assay", "direct EmlGen.Sample micro-assay: captured chunks vs terminal-rotated chunks vs no chunk reuse")
        {
            calls, seedK, maxLen, units, gain, sig, seed
        };
        command.SetAction(parse =>
        {
            List<string> argv = ["chunk-micro-assay"];
            AddLng(argv, "--calls", parse.GetValue(calls));
            AddInt(argv, "--seedk", parse.GetValue(seedK));
            AddInt(argv, "--maxlen", parse.GetValue(maxLen));
            AddInt(argv, "--units", parse.GetValue(units));
            AddInt(argv, "--gain", parse.GetValue(gain));
            AddInt(argv, "--sig", parse.GetValue(sig));
            AddOpt(argv, "--seed", parse.GetValue(seed));
            return EmlChunkMicroAssay.Run(argv.ToArray());
        });
        return command;
    }

    // ── basis-cortex-rematch ──  both arms are complete Cortex organisms. The causal variable is whether the
    //    grammar-derived sampler vocabulary keeps following induction or freezes after its first snapshot.
    internal static Command BasisCortexRematch()
    {
        Option<long?> calls = new("--calls") { Description = "evaluator calls per full-Cortex arm (default 100000)" };
        Option<int?> replicates = new("--replicates") { Description = "paired deterministic seed replicates (default 3)" };
        Option<int?> stride = new("--stride") { Description = "Cortex re-induction byte stride (default 800)" };
        Option<int?> sig = new("--sig") { Description = "dual-point signature figures (default 9)" };
        Option<string?> seed = CliShared.SeedOpt("basis Cortex-rematch seed (hex, default E311C0DE)");
        Command command = new("basis-cortex-rematch", "Calc-4 through the full Cortex: live induced grammar vs frozen grammar sampler")
        {
            calls, replicates, stride, sig, seed
        };
        command.SetAction(parse => EmlBasisCortexRematch.RunMatched(
            CliShared.ParseSeed(parse.GetValue(seed), 0xE311C0DEUL),
            parse.GetValue(calls) ?? 100_000,
            parse.GetValue(replicates) ?? 3,
            parse.GetValue(stride) ?? 800,
            parse.GetValue(sig) ?? global::Cogito.ReplayCalc.MountSig,
            new EmlGenerationConfig()));
        return command;
    }

    // ── sheffer ──  the constant-free program sweep. The CLAMP/SNAP verb: the body clamps --kmax to [1,21] and
    //    snaps it down to the nearest odd (shells are odd lengths), lowercases --rung and rejects anything but
    //    "a", and floors --park at 1. All three transforms live in the body; we pass the raw values through.
    internal static Command Sheffer()
    {
        var rung = new Option<string?>("--rung") { Description = "tier: only \"a\" (exact, 4-point gate) is built (default a)" };
        var kmax = new Option<int?>("--kmax")    { Description = "max program length — clamped [1,21], snapped down to odd (default 17)" };
        var park = new Option<int?>("--park")    { Description = "park cap, floored at 1 (default 64)" };

        var cmd = new Command("sheffer", "the Sheffer-stroke constant-free program sweep — E/A/U verdicts across cousins × 4 probe points")
        {
            rung, kmax, park
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "sheffer" };
            AddOpt(argv, "--rung", parse.GetValue(rung));
            AddInt(argv, "--kmax", parse.GetValue(kmax));
            AddInt(argv, "--park", parse.GetValue(park));
            return ShefferSweep.Run(argv.ToArray());
        });
        return cmd;
    }

    // ── antiunify ──  the CHAIN-corpus abstraction proof (induce → anti-unify → mint slot IFF ΔMDL pays). Plain
    //    value flags; --budget is a Long in the body and >0 gates an extra report (the body owns that branch).
    internal static Command AntiUnify()
    {
        var n      = new Option<int?>("--n")     { Description = "train sentences (default 500)" };
        var held   = new Option<int?>("--held")  { Description = "held-out sentences (default 200)" };
        var iter   = new Option<int?>("--iter")  { Description = "growth iterations (default 6)" };
        var cand   = new Option<int?>("--cand")  { Description = "candidate yields per iter (default 8)" };
        var seed   = CliShared.SeedOpt("antiunify LCG seed (hex, default C0117011)");
        var budget = new Option<long?>("--budget"){ Description = "extra-report budget — >0 gates the extra report (default 0)" };

        var cmd = new Command("antiunify", "CHAIN-corpus abstraction proof — induce Re-Pair → anti-unify rule yields → mint slot IFF ΔMDL pays, no LLM")
        {
            n, held, iter, cand, seed, budget
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "antiunify" };
            AddInt(argv, "--n",    parse.GetValue(n));
            AddInt(argv, "--held", parse.GetValue(held));
            AddInt(argv, "--iter", parse.GetValue(iter));
            AddInt(argv, "--cand", parse.GetValue(cand));
            AddOpt(argv, "--seed", parse.GetValue(seed));
            AddLng(argv, "--budget", parse.GetValue(budget));
            return global::Cogito.AntiUnify.Run(argv.ToArray());   // fully-qualified: the method is also the file-local AntiUnify()
        });
        return cmd;
    }

    // ── mintbench ──  the paradigm-mint scaling sweep. The COMMA-LIST verb: --slots is a comma-separated int
    //    list ("50,200,800,3200") the body Split(',')-parses; we pass the raw string through untouched so the
    //    body's own int.Parse per element resolves identically.
    internal static Command MintBench()
    {
        var slots   = new Option<string?>("--slots") { Description = "comma-separated paradigm sizes (default 50,200,800,3200)" };
        var members = new Option<int?>("--members")  { Description = "members per slot (default 8)" };
        var passes  = new Option<int?>("--passes")   { Description = "passes (default 7)" };
        var lines   = new Option<int?>("--lines")    { Description = "fresh-grammar corpus lines (default 2000)" };
        var seed    = CliShared.SeedOpt("mintbench LCG seed (hex, default 317BE7C4)");

        var cmd = new Command("mintbench", "the paradigm-mint scaling sweep — legacy stringify vs the cortex's Δ-mint across paradigm sizes")
        {
            slots, members, passes, lines, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "mintbench" };
            AddOpt(argv, "--slots",   parse.GetValue(slots));    // comma-list — raw passthrough; the body Split(',')-parses
            AddInt(argv, "--members", parse.GetValue(members));
            AddInt(argv, "--passes",  parse.GetValue(passes));
            AddInt(argv, "--lines",   parse.GetValue(lines));
            AddOpt(argv, "--seed",    parse.GetValue(seed));
            return global::Cogito.AntiUnify.MintBench(argv.ToArray());   // mintbench lives on AntiUnify, not its own class
        });
        return cmd;
    }

    // ── semgrammar ──  the semantic-coverage grammar (L1/L2/L3 residual). --file default "" ⇒ the builtin
    //    self-test corpus; a non-empty path MUST exist (the body errors + exits 1 otherwise — kept in the body).
    internal static Command SemGrammar()
    {
        var file = new Option<string?>("--file") { Description = "responses file (\\n\\n-separated); empty ⇒ builtin self-test corpus" };

        var cmd = new Command("semgrammar", "the semantic-coverage grammar — L1 lexical · L2 relational · L3 class-relational, residual = 1−L3")
        {
            file
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "semgrammar" };
            AddOpt(argv, "--file", parse.GetValue(file));
            return global::Cogito.SemGrammar.Run(argv.ToArray());
        });
        return cmd;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    //  TAPE CLUSTER — TYPED-CALL through RunResume below, which routes the exact current checkpoint schema to
    //  its owning runtime.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    // ── resume ──  continue a killed cortex run from its checkpoint (byte-identical), or --verify the round-trip
    //    only. The run-dir positional is REQUIRED (System.CommandLine enforces presence — the old args.Length<2
    //    usage guard). --steps extends a landed run's horizon; --night-probe = one instrumented Consolidate.
    internal static Command Resume()
    {
        var runDir      = new Argument<string>("run-dir") { Description = "runs/cortex_NNNN (or bare cortex_NNNN) — carries the run's config" };
        var verify      = new Option<bool>("--verify")      { Description = "round-trip check only (Save∘Load∘Save), no drive" };
        var memstat     = new Option<bool>("--memstat")     { Description = "report the loaded state's memory footprint" };
        var steps       = new Option<int?>("--steps")       { Description = "extend a landed run's horizon (default 0 = continue as configured)" };
        var nightProbe  = new Option<bool>("--night-probe") { Description = "one instrumented Consolidate over a COPY of the loaded state" };

        var cmd = new Command("resume", "continue a killed cortex run from its checkpoint.bin — byte-identical to the unkilled run")
        {
            runDir, verify, memstat, steps, nightProbe
        };
        cmd.SetAction(parse => RunResume(
            parse.GetValue(runDir)!, parse.GetValue(verify), parse.GetValue(memstat),
            parse.GetValue(steps) ?? 0, parse.GetValue(nightProbe)));
        return cmd;
    }

    // ── argv-rebuild helpers (the ADAPTER-ARGV bridge) — append `key value` only when the option was set. ──
    private static void AddOpt(List<string> argv, string key, string? val) { if (!string.IsNullOrEmpty(val)) { argv.Add(key); argv.Add(val); } }
    private static void AddInt(List<string> argv, string key, int? val)    { if (val is int v)    { argv.Add(key); argv.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); } }
    private static void AddLng(List<string> argv, string key, long? val)   { if (val is long v)   { argv.Add(key); argv.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); } }
    private static void AddDbl(List<string> argv, string key, double? val) { if (val is double v) { argv.Add(key); argv.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); } }

    // ── the tape verb bodies ──  resume dispatches on the checkpoint magic (current Cortex dialect / CGAGENT
    // agent) to the owning engine's Resume; legacy CGCKPT/CGTRIA and routed CGRING images fail loud. Homed here
    // beside the commands that own them since the Cli god-partial dissolved (were internal Cli bridges).
    private static int RunResume(string runDir, bool verify, bool memstat, int steps, bool nightProbe)
    {
        string? dir = Run.Resolve(runDir);
        if (dir is null)
        {
            Console.Error.WriteLine($"  run dir not found: {runDir}");
            return 1;
        }
        string ck = Path.Combine(dir, Checkpoint.FileName);
        if (!File.Exists(ck))
        {
            Console.Error.WriteLine(File.Exists(Path.Combine(dir, "manifest"))
                ? $"  run '{runDir}' has an agent manifest/config but no checkpoint.bin; cannot resume, only inspect or re-run"
                : $"  no checkpoint.bin under '{runDir}' — nothing to resume (the run predates checkpointing, or never reached its first checkpoint)");
            return 1;
        }
        Span<byte> magic = stackalloc byte[8];
        using (FileStream fs = File.OpenRead(ck)) fs.ReadExactly(magic);
        if (Checkpoint.MatchesCurrentSchema(magic)) return Cortex.Resume(dir, verify, memstat, steps, nightProbe);
        if (TryRejectRetiredCortexO(magic, Console.Error)) return 1;
        if (magic.SequenceEqual("CORTEXN\n"u8))
        {
            Console.Error.WriteLine($"  {Checkpoint.RetiredVNMessage}");
            return 1;
        }
        if (TryRejectRetiredCortexJ(magic, Console.Error)) return 1;
        if (TryRejectRetiredCortexD(magic, Console.Error)) return 1;
        if (TryRejectRetiredCortexF(magic, Console.Error)) return 1;
        if (TryRejectRetiredCortexH(magic, Console.Error)) return 1;
        if (TryRejectRetiredCortexI(magic, Console.Error)) return 1;
        if (magic.SequenceEqual("CORTEXE\n"u8))
        {
            Console.Error.WriteLine($"  {Checkpoint.RetiredVEMessage}");
            return 1;
        }
        if (magic.SequenceEqual("CORTEXB\n"u8))
        {
            Console.Error.WriteLine($"  {Checkpoint.RetiredVBMessage}");
            return 1;
        }
        if (magic.SequenceEqual("CORTEXC\n"u8))
        {
            Console.Error.WriteLine($"  {Checkpoint.RetiredVCMessage}");
            return 1;
        }
        if (magic.SequenceEqual("CORTEXA\n"u8) || magic.SequenceEqual("CORTEX9\n"u8))
        {
            Console.Error.WriteLine("  checkpoint carries retired tree-policy authority; keep the old run as data and start a new cortex run.");
            return 1;
        }
        if (magic.SequenceEqual("CORTEX4\n"u8))
        {
            Console.Error.WriteLine("  CORTEX4 checkpoint predates typed policy decision readouts; keep the old run as data and start a new cortex run.");
            return 1;
        }
        if (magic[..6].SequenceEqual("CGCKPT"u8))
        {
            Console.Error.WriteLine($"  legacy trunk checkpoint dialect (CGCKPT): Cortex uses the current {Checkpoint.CurrentDialect} schema. Keep the old run as data and start a new cortex run.");
            return 1;
        }
        if (magic[..7].SequenceEqual("CGTRIA\n"u8))
        {
            Console.Error.WriteLine("  legacy mesh checkpoint dialect (CGTRIA): mesh is no longer a public tape-resume target. Keep the old run as data.");
            return 1;
        }
        if (magic[..7].SequenceEqual("CGRING\n"u8))
        {
            Console.Error.WriteLine("  routed mesh checkpoint dialect (CGRING): use drive mesh --resume for this run.");
            return 1;
        }
        if (magic.SequenceEqual("CGAGENT\n"u8))
        {
            if (memstat || nightProbe) { Console.Error.WriteLine("  agent resume does not accept --memstat or --night-probe"); return 1; }
            return AgentResume.Resume(dir, verify, steps);
        }
        string got = System.Text.Encoding.ASCII.GetString(magic).TrimEnd('\n', '\0');
        Console.Error.WriteLine(got.StartsWith("CG", StringComparison.Ordinal)
            ? $"  checkpoint format skew: unrecognized magic '{got}'"
            : "  not a cogito checkpoint (bad magic)");
        return 1;
    }

    internal static bool TryRejectRetiredCortexD(ReadOnlySpan<byte> magic, TextWriter error)
    {
        if (!magic.SequenceEqual("CORTEXD\n"u8)) return false;
        error.WriteLine($"  {Checkpoint.RetiredVDMessage}");
        return true;
    }

    internal static bool TryRejectRetiredCortexO(ReadOnlySpan<byte> magic, TextWriter error)
    {
        if (!magic.SequenceEqual("CORTEXO\n"u8)) return false;
        error.WriteLine($"  {Checkpoint.RetiredVOMessage}");
        return true;
    }

    internal static bool TryRejectRetiredCortexJ(ReadOnlySpan<byte> magic, TextWriter error)
    {
        if (!magic.SequenceEqual("CORTEXJ\n"u8)) return false;
        error.WriteLine($"  {Checkpoint.RetiredVJMessage}");
        return true;
    }

    internal static bool TryRejectRetiredCortexF(ReadOnlySpan<byte> magic, TextWriter error)
    {
        if (!magic.SequenceEqual("CORTEXF\n"u8)) return false;
        error.WriteLine($"  {Checkpoint.RetiredVFMessage}");
        return true;
    }

    internal static bool TryRejectRetiredCortexH(ReadOnlySpan<byte> magic, TextWriter error)
    {
        if (!magic.SequenceEqual("CORTEXH\n"u8)) return false;
        error.WriteLine($"  {Checkpoint.RetiredVHMessage}");
        return true;
    }

    internal static bool TryRejectRetiredCortexI(ReadOnlySpan<byte> magic, TextWriter error)
    {
        if (!magic.SequenceEqual("CORTEXI\n"u8)) return false;
        error.WriteLine($"  {Checkpoint.RetiredVIMessage}");
        return true;
    }

    }
}
