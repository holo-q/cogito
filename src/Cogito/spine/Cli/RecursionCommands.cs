namespace Cogito.Cli;

using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using Cogito.Exec;

internal static class RecursionCommands
{
    internal static Command DeepRematchGateCommands()
    {
        Command command = new("deep-rematch-gate", "pre-register and adjudicate the seven-line dissolution rematch gate");
        command.Subcommands.Add(PrepareDeepRematchGate());
        command.Subcommands.Add(CollectDeepRematchRun());
        command.Subcommands.Add(AdjudicateDeepRematchGate());
        command.Subcommands.Add(VerifyDeepRematchGateFixture());
        command.Subcommands.Add(VerifyPersistedDeepRematchComposite());
        command.Subcommands.Add(ResumeDeepRematchComposite());
        return command;
    }

    /// Historical-artifact custodian alias for verify-composite. This spelling is retained for
    /// banked _0002-_0009 readers; it is permanently read-only and never resumes or mints a run.
    private static Command ResumeDeepRematchComposite()
    {
        Argument<string> parentDirectory = new("parent-run-dir") { Description = "sealed historical deep-rematch composite artifact directory" };
        Command command = new("resume-composite", "historical read-only alias for verify-composite; never resumes or creates artifacts")
        {
            parentDirectory,
        };
        command.SetAction(parse => DeepRematchCompositeRON.VerifyPersisted(
            parse.GetValue(parentDirectory) ?? throw new ArgumentException("parent-run-dir is required"),
            Console.Out) ? 0 : 1);
        return command;
    }

    /// Historical-artifact custodian for the immutable _0002-_0009 composite readers.
    /// OccurrenceCheck reopens sealed records without rerunning Cortex or writing a continuation.
    private static Command VerifyPersistedDeepRematchComposite()
    {
        Argument<string> parentDirectory = new("parent-run-dir") { Description = "sealed historical deep-rematch composite artifact directory" };
        Command command = new("verify-composite", "verify a sealed historical composite artifact without rerunning Cortex")
        {
            parentDirectory,
        };
        command.SetAction(parse => DeepRematchCompositeRON.VerifyPersisted(
            parse.GetValue(parentDirectory) ?? throw new ArgumentException("parent-run-dir is required"),
            Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyHomeostatDestinationHandshakeFixture()
    {
        Command command = new("destination-handshake-fixture", "run the cheap Homeostat-owned cold destination handshake gate");
        command.SetAction(_ => HomeostatDestinationHandshakeFixture.Run(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyPolicyBoundaryMaterializationFixture()
    {
        Command command = new("policy-boundary-materialization-fixture", "run the cheap A3 typed child-materialization contract fixture");
        command.SetAction(_ => Cortex.VerifyPolicyBoundaryMaterializationContractFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyPolicyBoundaryDivergenceTemporalSplitFixture()
    {
        Command command = new("policy-boundary-dissent-temporal-fixture", "prove parent-readiness and child-executed dissent are separate identities");
        command.SetAction(_ => Cortex.VerifyPolicyBoundaryDivergenceTemporalSplitFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyReadoutTrainingCorroborationFixture()
    {
        Command command = new("r4-witness-mutation-fixture", "reject mutations of every typed R4 training and funded child identity");
        command.SetAction(_ => ReadoutTrainingCorroborationFixture.Run(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyLoopClosureResumeCorroborationFixture()
    {
        Command command = new("r4-resume-witness-fixture", "prove save-load rehydrates an exact R4 teacher fold and rejects its absence");
        command.SetAction(_ => LoopClosureResumeCorroborationFixture.Run(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyRunAuthorityFixture()
    {
        Command command = new("run-authority-fixture", "run exact destination, deterministic authority, and corruption refusal checks");
        command.SetAction(_ => RunAuthority.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyLoopClosureTerminalCheckpointFixture()
    {
        Command command = new("loop-closure-terminal-checkpoint-fixture", "prove the zero-Homeostat fallback census survives effective checkpoint custody");
        command.SetAction(_ => LoopClosureTerminalCheckpointFixture.Run(Console.Out) ? 0 : 1);
        return command;
    }

    // Profile against a disposable copy so sealed historical runs remain immutable.
    internal static Command ProfileRunAuthority()
    {
        Argument<string> runDirectory = new("run-dir") { Description = "sealed run directory to copy and profile" };
        Command command = new("profile-run-authority", "copy a sealed run into .tmp and profile checkpoint proof versus closure hashing")
        {
            runDirectory,
        };
        command.SetAction(parse => RunAuthority.Profile(parse.GetValue(runDirectory)!, Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyRung0ReceiptFixture()
    {
        Command command = new("rung0-receipt-fixture", "exercise ordinary EML rung-0 audit census and digest corruption gates");
        command.SetAction(_ => EmlOrdinaryRunRung0Receipt.VerifyFixture() ? 0 : 1);
        return command;
    }


    private static Command CollectDeepRematchRun()
    {
        Argument<string> runDirectory = new("run-dir") { Description = "landed Cortex run directory" };
        Argument<string> output = new("output") { Description = "typed collected artifact RON" };
        Option<string?> runID = new("--run-id") { Description = "run identity (default: directory name)" };
        Option<long?> windowStart = new("--window-start") { Description = "scored evaluation window first step (fixed at 1281; step 1280 is the handshake)" };
        Option<long?> windowSteps = new("--window-steps") { Description = "evaluation window length (fixed at 500 for live candidates)" };
        Option<bool> baseline = new("--baseline") { Description = "collect a historical baseline using its registered legacy checkpoint dialect" };
        Command command = new("collect", "derive a digest-bound deep-rematch artifact from real run receipts"){ runDirectory, output, runID, windowStart, windowSteps, baseline };
        command.SetAction(parse =>
        {
            DeepRematchArtifact artifact = DeepRematchGate.CollectRun(parse.GetValue(runDirectory)!, parse.GetValue(runID), parse.GetValue(windowStart) ?? 1281, parse.GetValue(windowSteps) ?? 500, parse.GetValue(baseline));
            DeepRematchGate.WriteArtifact(parse.GetValue(output)!, artifact);
            Console.WriteLine($"  deep-rematch-gate collected · run={artifact.RunID} · artifact={artifact.ArtifactDigest} · {parse.GetValue(output)}");
            return 0;
        });
        return command;
    }

    private static Command PrepareDeepRematchGate()
    {
        Argument<string> output = new("output") { Description = "destination for the immutable pre-registration RON" };
        Argument<string> baseline0071 = new("baseline-0071") { Description = "typed cortex_0071 baseline artifact RON" };
        Argument<string> baseline0098 = new("baseline-0098") { Description = "typed cortex_0098 baseline artifact RON" };
        Option<string?> gateID = new("--gate-id") { Description = "stable pre-registration identity" };
        Command command = new("prepare", "freeze thresholds, baseline values, fuel axes, and baseline artifact digests before launch")
        {
            output,
            baseline0071,
            baseline0098,
            gateID,
        };
        command.SetAction(parse =>
        {
            string outputPath = parse.GetValue(output) ?? throw new ArgumentException("output is required");
            DeepRematchArtifact old0071 = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(parse.GetValue(baseline0071) ?? throw new ArgumentException("baseline-0071 is required")));
            DeepRematchArtifact old0098 = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(parse.GetValue(baseline0098) ?? throw new ArgumentException("baseline-0098 is required")));
            DeepRematchGateConfig config = DeepRematchGate.CreateDefault(old0071, old0098, parse.GetValue(gateID));
            DeepRematchGate.WritePrepared(outputPath, config);
            Console.WriteLine($"  deep-rematch-gate prepared · {config.GateID} · config={config.ConfigDigest} · {outputPath}");
            return 0;
        });
        return command;
    }

    private static Command AdjudicateDeepRematchGate()
    {
        Argument<string> config = new("config") { Description = "pre-registered gate RON" };
        Argument<string> baseline0071 = new("baseline-0071") { Description = "typed cortex_0071 baseline artifact RON" };
        Argument<string> baseline0098 = new("baseline-0098") { Description = "typed cortex_0098 baseline artifact RON" };
        Argument<string> run = new("run") { Description = "typed deep-rematch run artifact RON" };
        Option<string?> output = new("--output") { Description = "verdict report destination (.ron; TSV is written beside it)" };
        Command command = new("adjudicate", "compute seven exact verdict lines from typed receipts and bank failed lines as nulls")
        {
            config,
            baseline0071,
            baseline0098,
            run,
            output,
        };
        command.SetAction(parse =>
        {
            DeepRematchGateConfig registration = DeepRematchGate.DecodeConfig(File.ReadAllBytes(parse.GetValue(config) ?? throw new ArgumentException("config is required")));
            DeepRematchArtifact old0071 = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(parse.GetValue(baseline0071) ?? throw new ArgumentException("baseline-0071 is required")));
            DeepRematchArtifact old0098 = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(parse.GetValue(baseline0098) ?? throw new ArgumentException("baseline-0098 is required")));
            DeepRematchArtifact actual = DeepRematchGate.DecodeArtifact(File.ReadAllBytes(parse.GetValue(run) ?? throw new ArgumentException("run is required")));
            DeepRematchGateReport report = DeepRematchGate.Adjudicate(registration, old0071, old0098, actual);
            string reportPath = parse.GetValue(output) ?? Path.Combine(Path.GetDirectoryName(parse.GetValue(run)!) ?? ".", "deep-rematch-verdicts.ron");
            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(reportPath, DeepRematchGate.EncodeReport(report));
            File.WriteAllText(Path.ChangeExtension(reportPath, ".tsv"), DeepRematchGate.RenderTsv(report));
            Console.WriteLine($"  deep-rematch-gate adjudicated · {report.Verdicts.Count(v => v.Status == "PASS")}/{DeepRematchGate.LineCount} lines · config={report.ConfigDigest} · report={report.ReportDigest} · {reportPath}");
            return report.Verdicts.All(v => v.Status == "PASS") ? 0 : 1;
        });
        return command;
    }

    private static Command VerifyDeepRematchGateFixture()
    {
        Option<string?> output = new("--out") { Description = "optional directory for typed fixture artifacts and a valid verdict report" };
        Command command = new("fixture", "exercise valid, failing, banked-null, and tampered config/artifact receipts") { output };
        command.SetAction(parse => DeepRematchGateFixture.Run(Console.Out, parse.GetValue(output)) ? 0 : 1);
        return command;
    }

    internal static Command RunEmlCacheAssay()
    {
        Option<int?> signatureDigits = new("--sig") { Description = "EML certificate significant digits (default: 9)" };
        Command command = new("eml-cache-assay", "prove finite residual cache semantics, profile isolation, process separation, and resume invisibility") { signatureDigits };
        command.SetAction(parse => EmlCacheAssay.Run(parse.GetValue(signatureDigits) ?? 9).Passed ? 0 : 1);
        return command;
    }

    internal static Command VerifyPolicyTrialJournal()
    {
        Argument<string> run = new("run") { Description = "Cortex run directory carrying typed policy funding and settlement journals" };
        Command command = new("verify-trial-journal", "verify funding identity, resume reuse/deny, and settlement accounting from typed records") { run };
        command.SetAction(parse =>
        {
            string directory = parse.GetValue(run) ?? throw new ArgumentException("run is required");
            CortexPolicyTrialJournalOccurrenceCheck receipt = CortexPolicyTrialJournalVerifier.Verify(directory, Console.Out);
            return receipt.Passed ? 0 : 1;
        });
        return command;
    }

    internal static Command VerifyPolicyReadout()
    {
        Argument<string> run = new("run") { Description = "Cortex run directory carrying persisted policy decision readouts" };
        Command command = new("verify-policy-readout", "verify policy packet/journal provenance, closed causes, and resume-stable behavioral fields") { run };
        command.SetAction(parse =>
        {
            string directory = parse.GetValue(run) ?? throw new ArgumentException("run is required");
            CortexPolicyDecisionReadoutOccurrenceCheck receipt = CortexPolicyDecisionReadoutVerifier.Verify(directory, Console.Out);
            return receipt.Passed ? 0 : 1;
        });
        return command;
    }

    internal static Command VerifyPolicyReadoutFixture()
    {
        Command command = new("verify-policy-readout-fixture", "exercise every closed policy selection cause through packet and checkpoint persistence");
        command.SetAction(_ => CortexPolicyDecisionReadoutVerifier.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyOrganicComparisonFixture()
    {
        Command command = new("verify-organic-comparison-fixture", "prove ordinary Homeostat comparison custody, conservation, replay, rejection, and observer null");
        command.SetAction(_ => OrganicComparisonFixture.Run(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyPolicyCanonicalCoverageFixture()
    {
        Command command = new("verify-policy-canonical-coverage-fixture", "exercise partial and complete canonical-program coverage custody");
        command.SetAction(_ => CortexPolicyDecisionReadoutVerifier.VerifyCanonicalCoverageFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyComputeAccounting()
    {
        Command command = new("verify-compute-accounting", "exercise Cortex step-wall conservation, finite phase values, and digest-bound segment identity");
        command.SetAction(_ => CortexComputeAccountingVerifier.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command VerifyCheckpointDelta()
    {
        Command command = new("verify-checkpoint-delta", "differentially verify CORTEXP-D1 append, replay, crash, compaction, and fork-transfer semantics");
        command.SetAction(_ =>
        {
            bool checkpoint = Checkpoint.VerifyDialectFixture(Console.Out);
            bool fork = checkpoint && CortexForkRunner.VerifyDeltaTransferFixture(Console.Out);
            bool proof = fork && CortexForkRunner.VerifyCheckpointProofFixture(Console.Out);
            return checkpoint && fork && proof ? 0 : 1;
        });
        return command;
    }

    internal static Command VerifyTerminalReceipt()
    {
        Command command = new("verify-terminal-receipt", "verify generic fork terminal receipt recovery, file binding, timing, and corruption rejection");
        command.SetAction(_ => CortexForkRunner.VerifyTerminalReceiptContract(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command RunAnytimeCurveAssay()
    {
        Command command = new("anytime-curve-assay", "prove typed fuel curves, grace windows, delayed gains, and kill-line corruption gates");
        command.SetAction(_ => EmlAnytimeCurveAssay.Run());
        return command;
    }

    internal static Command RunEmlAnytimePairedKill()
    {
        Command command = new("eml-anytime-paired-kill", "run the real matched deliberation/reflex anytime kill line");
        command.SetAction(_ => EmlAnytimePairedKill.Run(Console.Out));
        return command;
    }

    internal static Command RunPopulation()
    {
        Option<ulong?> seed = new("--seed") { Description = "cohort seed" };
        Option<long?> evaluatorCalls = new("--evaluator-calls") { Description = "matched evaluator calls per mind per epoch" };
        Option<long?> residencyHorizon = new("--residency-horizon") { Description = "epochs allowed for imported laws to earn residence" };
        Option<int?> strideBytes = new("--stride-bytes") { Description = "grammar reinduction stride" };
        Option<int?> signatureDigits = new("--sig") { Description = "EML certificate significant digits" };
        Command command = new("run-population", "run the independent-lineage, exact-clone, sealed-law exchange assay")
        {
            seed,
            evaluatorCalls,
            residencyHorizon,
            strideBytes,
            signatureDigits,
        };
        command.SetAction(parse =>
        {
            EmlReplayPopulationReport report = EmlReplayPopulation.RunPopulation(
                parse.GetValue(seed) ?? 0x504F50554C415449UL,
                parse.GetValue(evaluatorCalls) ?? 1_000,
                parse.GetValue(residencyHorizon) ?? 4,
                parse.GetValue(strideBytes) ?? GrokDefaults.ReStrideBytes,
                parse.GetValue(signatureDigits) ?? ReplayCalc.MountSig,
                new EmlGenerationConfig());
            Console.WriteLine($"  population · clone genesis {(report.GenesisCloneExact ? "exact" : "BROKEN")} · trajectory {(report.CloneTrajectoryExact ? "exact" : "diverged")} · matched cost {(report.MatchedEvaluatorCost ? "yes" : "NO")} · {report.Directory}");
            return report.GenesisCloneExact && report.CloneTrajectoryExact && report.MatchedEvaluatorCost ? 0 : 1;
        });
        return command;
    }

    internal static Command CalibrateMarathon()
    {
        Argument<string> corpus = new("corpus") { Description = "perpetual campfire corpus directory" };
        Option<string?> runID = new("--run") { Description = "marathon identity (default: recursion-marathon)" };
        Option<string?> output = new("--output") { Description = "manifest destination (default: runs/<run>-manifest.ron)" };
        Option<ulong?> seed = new("--seed") { Description = "deterministic seed" };
        Command command = new("calibrate-marathon", "run the fixed 10-minute smoke and 2-hour tail calibration, then mint the conserved-budget manifest")
        {
            corpus,
            runID,
            output,
            seed,
        };
        command.SetAction(parse =>
        {
            string corpusPath = parse.GetValue(corpus) ?? throw new ArgumentException("corpus is required");
            string identity = parse.GetValue(runID) ?? "recursion-marathon";
            ulong runSeed = parse.GetValue(seed) ?? 0x4D41524154484F4EUL;
            CogitoCorpus source = new() { Path = corpusPath };
            EMLRecursionMarathonLane eml = new(runSeed);
            CampfireRecursionMarathonLane campfire = new(source, runSeed);
            (RecursionLaneCalibration EML, RecursionLaneCalibration Campfire) calibration =
                RecursionMarathon.CalibrateAsync(identity, eml, campfire, CancellationToken.None).GetAwaiter().GetResult();
            RecursionBranchAuthority branches = RecursionBranchAuthorityStore.CreateCurrent();
            RecursionMarathonManifest manifest = RecursionMarathonAuthority.CreateManifest(
                identity,
                runSeed,
                ComputeIntakeDigest(corpusPath),
                branches.Digest,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                calibration.EML,
                calibration.Campfire);
            string destination = parse.GetValue(output) ?? Path.Combine("runs", $"{identity}-manifest.ron");
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(destination, RecursionMarathonAuthority.EncodeManifest(RecursionMarathonRONCodec.Instance, manifest));
            Console.WriteLine($"  marathon calibrated · EML {manifest.GetLane(RecursionMarathonLanes.EMLProcedure).ConservedUnits} calls · campfire {manifest.GetLane(RecursionMarathonLanes.Campfire).ConservedUnits} steps · {destination}");
            return 0;
        });
        return command;
    }

    internal static Command RunMarathon()
    {
        Argument<string> manifestPath = new("manifest") { Description = "calibrated marathon manifest RON" };
        Argument<string> corpus = new("corpus") { Description = "the same perpetual campfire corpus used by calibration" };
        Option<string?> stage = new("--stage") { Description = "baseline or graduated (default: baseline)" };
        Option<string?> output = new("--output") { Description = "report destination (default: beside manifest)" };
        Command command = new("run-marathon", "run both conserved-budget lanes with forced checkpoint reconstruction and full wall accounting")
        {
            manifestPath,
            corpus,
            stage,
            output,
        };
        command.SetAction(parse =>
        {
            string manifestFile = parse.GetValue(manifestPath) ?? throw new ArgumentException("manifest is required");
            string corpusPath = parse.GetValue(corpus) ?? throw new ArgumentException("corpus is required");
            RecursionMarathonManifest manifest = RecursionMarathonRONCodec.Instance.DecodeManifest(File.ReadAllBytes(manifestFile));
            string stageToken = parse.GetValue(stage) ?? "baseline";
            RecursionMarathonStages runStage = stageToken.Equals("graduated", StringComparison.OrdinalIgnoreCase)
                ? RecursionMarathonStages.Graduated
                : stageToken.Equals("baseline", StringComparison.OrdinalIgnoreCase)
                    ? RecursionMarathonStages.Baseline
                    : throw new ArgumentException("--stage must be baseline or graduated");
            if (!string.Equals(manifest.IntakeDigest, ComputeIntakeDigest(corpusPath), StringComparison.Ordinal))
                throw new InvalidDataException("marathon corpus differs from the calibrated intake digest");
            RecursionBranchAuthority branches = RecursionBranchAuthorityStore.CreateCurrent();
            if (!string.Equals(manifest.BranchDigest, branches.Digest, StringComparison.Ordinal))
                throw new InvalidDataException("branch authority changed after calibration; mint a new manifest");
            long emlUnits = manifest.GetLane(RecursionMarathonLanes.EMLProcedure).ConservedUnits;
            long campfireUnits = manifest.GetLane(RecursionMarathonLanes.Campfire).ConservedUnits;
            EMLRecursionMarathonLane eml = new(manifest.Seed, emlUnits);
            CampfireRecursionMarathonLane campfire = new(new CogitoCorpus { Path = corpusPath }, manifest.Seed, campfireUnits);
            RecursionMarathonReport report = RecursionMarathon.RunAsync(
                manifest,
                runStage,
                eml,
                campfire,
                CancellationToken.None).GetAwaiter().GetResult();
            string destination = parse.GetValue(output) ?? Path.Combine(
                Path.GetDirectoryName(manifestFile) ?? ".",
                $"{manifest.RunID}-{runStage.ToString().ToLowerInvariant()}.ron");
            File.WriteAllBytes(destination, RecursionMarathonAuthority.EncodeReport(RecursionMarathonRONCodec.Instance, report));
            Console.WriteLine($"  marathon {runStage.ToString().ToLowerInvariant()} · budgets {(report.ReachedBothBudgets ? "reached" : "INCOMPLETE")} · checkpoints {(report.CheckpointsExact ? "exact" : "BROKEN")} · wall {(report.WallAccountingExact ? "exact" : "DARK")} · {destination}");
            return report.ReachedBothBudgets && report.CheckpointsExact && report.WallAccountingExact ? 0 : 1;
        });
        return command;
    }

    internal static Command RunThermometry()
    {
        Argument<string> data = new("data") { Description = "directory containing query.txt/sites.jsonl/gold.json instance directories" };
        Option<int?> limit = new("--limit") { Description = "maximum instances; 0 uses the complete corpus" };
        Option<ulong?> seed = new("--seed") { Description = "deterministic seed" };
        Command command = new("run-thermometry", "run the frozen answer-leak-free LOC Cortex after an anchor census")
        {
            data,
            limit,
            seed,
        };
        command.SetAction(parse =>
        {
            string? dataDirectory = parse.GetValue(data);
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                Console.Error.WriteLine("  run-thermometry requires a data directory");
                return 1;
            }
            AgentSolve.LocThermometryRequest request = new(
                dataDirectory,
                parse.GetValue(seed) ?? 0xC0117011UL,
                parse.GetValue(limit) ?? 0);
            AgentSolve.LocThermometryResult result = AgentSolve.RunThermometry(in request);
            StringBuilder report = new("anchor\ttotal\tcommits\tcorrect_commits\tsolved\tsuccess_at_commit\tactions_to_commit\tcalibration_error\treported_calibration_error\tabstention\tdeep_total\tdeep_correct\tdeep_success\n");
            AgentSolve.LocThermometryMetrics overall = result.Overall;
            AppendThermometryRow(report, in overall);
            foreach (AgentSolve.LocThermometryMetrics metrics in result.ByAnchorClass) AppendThermometryRow(report, in metrics);
            File.WriteAllText(Path.Combine(result.RunDirectory, "thermometry.tsv"), report.ToString());
            Console.WriteLine($"  thermometry · bindable {result.Census.Bindable}/{result.Census.Total} ({result.Census.BindableFraction:P1}) · solved {result.Overall.Solved}/{result.Overall.Total} · {result.RunDirectory}");
            return result.ExitCode;
        });
        return command;
    }

    internal static Command CompareBranches()
    {
        Option<ulong?> seed = new("--seed") { Description = "deterministic seed" };
        Option<long?> evaluatorCalls = new("--evaluator-calls") { Description = "matched evaluator calls per arm (default: 100000)" };
        Option<int?> strideBytes = new("--stride") { Description = "Cortex re-induction stride bytes" };
        Option<int?> signatureDigits = new("--signature-digits") { Description = "EML signature digits (default: 9)" };
        Command command = new("compare-branches", "run linear, guarded, and shuffled-guard EML procedures at matched evaluator cost")
        {
            seed,
            evaluatorCalls,
            strideBytes,
            signatureDigits,
        };
        command.SetAction(parse => EmlBranchingAssay.RunMatched(
            parse.GetValue(seed) ?? 0xC0117011UL,
            parse.GetValue(evaluatorCalls) ?? 100_000,
            parse.GetValue(strideBytes) ?? GrokDefaults.ReStrideBytes,
            parse.GetValue(signatureDigits) ?? ReplayCalc.MountSig,
            new EmlGenerationConfig()));
        return command;
    }

    internal static Command RunWeft()
    {
        Option<string?> runName = new("--run") { Description = "run name (default: recursion-weft)" };
        Option<int?> steps = new("--steps") { Description = "Cortex steps (default: 500)" };
        Option<ulong?> seed = new("--seed") { Description = "deterministic seed" };
        Option<int?> fuel = new("--fuel") { Description = "VM Fuel per candidate (default: 128)" };
        Option<int?> towerBytes = new("--tower-bytes") { Description = "tower body budget (default: 96)" };
        Option<int?> candidateLength = new("--candidate-length") { Description = "sampled Weft candidate length (default: 12)" };
        Option<string?> homeostatAutonomy = new("--homeostat-autonomy") { Description = "learned authority: off|emulation|full (default: full)" };
        Command command = new("run-weft", "mount Weft programs as a behavioral discovery world under the unchanged Cortex")
        {
            runName,
            steps,
            seed,
            fuel,
            towerBytes,
            candidateLength,
            homeostatAutonomy,
        };
        command.SetAction(parse =>
        {
            ulong runSeed = parse.GetValue(seed) ?? 0xC0117011UL;
            CortexWeftCurriculum curriculum = new()
            {
                ExecutionFuel = parse.GetValue(fuel) ?? 128,
                TowerBlockBudget = parse.GetValue(towerBytes) ?? 96,
                CandidateLength = parse.GetValue(candidateLength) ?? 12,
            };
            WeftCurriculum runtime = curriculum.Mount(runSeed);
            CortexConfig config = new()
            {
                RunName = parse.GetValue(runName) ?? "recursion-weft",
                Steps = parse.GetValue(steps) ?? 500,
                Seed = runSeed,
                Curriculum = curriculum,
                RuntimeCurriculum = runtime,
                Learning = new CortexLearningConfig
                {
                    Homeostat = new CortexHomeostatConfig
                    {
                        Autonomy = CortexConfigTokens.ParseHomeostatAutonomy(parse.GetValue(homeostatAutonomy)),
                    },
                },
                Readout = new CortexReadoutConfig
                {
                    Curve = ["weft.*"],
                },
            };
            return new Cortex(config).Run();
        });
        return command;
    }

    internal static Command RunMatchedForkProof()
    {
        Argument<string> run = new("run") { Description = "completed Cortex run directory carrying checkpoint.bin, tape.spanlog, and curve.tsv" };
        Option<int?> seedStep = new("--seed-step") { Description = "checkpoint next-step value (required; read from the run receipt)" };
        Option<int?> horizon = new("--horizon") { Description = "absolute continuation horizon (default: seed step + 1)" };
        Command command = new("matched-fork", "run two isolated Cortex arms concurrently and prove fused seed + terminal checkpoint identity")
        {
            run,
            seedStep,
            horizon,
        };
        command.SetAction(parse =>
        {
            string sourceDirectory = Path.GetFullPath(parse.GetValue(run) ?? throw new ArgumentException("run is required"));
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            string[] required = [Checkpoint.FileName, "tape.spanlog", "curve.tsv"];
            for (int i = 0; i < required.Length; i++)
                if (!File.Exists(Path.Combine(sourceDirectory, required[i]))) throw new FileNotFoundException($"matched-fork proof requires {required[i]}", required[i]);
            int nextStep = parse.GetValue(seedStep) ?? throw new ArgumentException("--seed-step is required");
            int absoluteHorizon = parse.GetValue(horizon) ?? checked(nextStep + 1);
            if (absoluteHorizon <= nextStep) throw new ArgumentOutOfRangeException(nameof(horizon), "horizon must advance beyond seed step");

            CortexRunConfig config = Checkpoint.PeekConfig(sourceDirectory);
            CortexForkSeed seed = CortexForkSeed.MaterializeRun(sourceDirectory, nextStep);
            string proofRoot = Path.Combine(sourceDirectory, "matched_fork_proof");
            int collision = 0;
            string leftDirectory;
            string rightDirectory;
            do
            {
                string stem = $"step-{nextStep:D8}-{collision:D2}";
                leftDirectory = Path.Combine(proofRoot, stem, "left");
                rightDirectory = Path.Combine(proofRoot, stem, "right");
                collision++;
            }
            while (Directory.Exists(leftDirectory) || Directory.Exists(rightDirectory));

            Cortex spawning = Cortex.CreateCheckpointRuntime(config);
            CortexMatchedForkReceipt<int> receipt = CortexForkRunner.RunMatchedFork(
                spawning,
                seed,
                new CortexForkArm<int>(leftDirectory, () => Cortex.CreateCheckpointRuntime(config), static (Cortex _) => 0),
                new CortexForkArm<int>(rightDirectory, () => Cortex.CreateCheckpointRuntime(config), static (Cortex _) => 0),
                absoluteHorizon);
            bool finalDigestsMatch = string.Equals(receipt.Left.FinalDigests.CheckpointSHA256, receipt.Right.FinalDigests.CheckpointSHA256, StringComparison.Ordinal)
                                     && string.Equals(receipt.Left.FinalDigests.TapeSpanlogSHA256, receipt.Right.FinalDigests.TapeSpanlogSHA256, StringComparison.Ordinal)
                                     && string.Equals(receipt.Left.FinalDigests.CurveSHA256, receipt.Right.FinalDigests.CurveSHA256, StringComparison.Ordinal);
            Console.WriteLine($"  matched-fork · seed={nextStep} horizon={absoluteHorizon} · seed-relation={receipt.SeedRelation.Kind} initial-cross-arm={(receipt.SeedRelation.InitialCrossArmMatched == true ? "equal" : "n/a")} · final-digests {(finalDigestsMatch ? "serial-equivalent" : "diverged")}");
            Console.WriteLine($"  wall · parallel {receipt.Timing.ParallelWallMilliseconds}ms · serial-equivalent {receipt.Timing.SerialWallMilliseconds}ms · reduced {(receipt.Timing.ParallelWallReduced ? "yes" : "no")}");
            Console.WriteLine($"  left  · exact={receipt.Left.TerminalCheckpointExact} · {receipt.Left.FinalDigests.CheckpointSHA256}");
            Console.WriteLine($"  right · exact={receipt.Right.TerminalCheckpointExact} · {receipt.Right.FinalDigests.CheckpointSHA256}");
            return receipt.IsExact && finalDigestsMatch && receipt.Timing.ParallelWallReduced ? 0 : 1;
        });
        return command;
    }

    internal static Command RunMatchedForkRegression()
    {
        Argument<string> run = new("run") { Description = "completed Cortex run directory carrying checkpoint.bin, tape.spanlog, and curve.tsv" };
        Option<int?> seedStep = new("--seed-step") { Description = "checkpoint next-step value (required; read from the run receipt)" };
        Command command = new("matched-fork-regression", "run a real two-rung divergent ladder and verify per-arm continuity")
        {
            run,
            seedStep,
        };
        command.SetAction(parse =>
        {
            string sourceDirectory = Path.GetFullPath(parse.GetValue(run) ?? throw new ArgumentException("run is required"));
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            string[] required = [Checkpoint.FileName, "tape.spanlog", "curve.tsv"];
            for (int i = 0; i < required.Length; i++)
                if (!File.Exists(Path.Combine(sourceDirectory, required[i]))) throw new FileNotFoundException($"matched-fork regression requires {required[i]}", required[i]);
            int nextStep = parse.GetValue(seedStep) ?? throw new ArgumentException("--seed-step is required");
            CortexRunConfig config = Checkpoint.PeekConfig(sourceDirectory);
            CortexForkSeed seed = CortexForkSeed.MaterializeRun(sourceDirectory, nextStep);
            Cortex spawning = Cortex.CreateCheckpointRuntime(config);
            string regressionRoot = Path.Combine(sourceDirectory, "matched_fork_regression", Guid.NewGuid().ToString("N"));
            CortexForkRegressionReceipt receipt = CortexForkRunner.RunChainedRungRegression(
                spawning, config, seed, regressionRoot);
            Console.WriteLine($"  matched-fork-regression · initial-cross-arm={(receipt.InitialCrossArmMatched ? "equal" : "NO")} · cross-arm-inequality={(receipt.CrossArmInequalityExpected ? "expected" : "unexpected")}");
            Console.WriteLine($"  continuity · left={(receipt.LeftContinuityExact ? "exact" : "FAIL")} · right={(receipt.RightContinuityExact ? "exact" : "FAIL")}");
            Console.WriteLine($"  terminal-checkpoint · left={(receipt.LeftTerminalCheckpointExact ? "exact" : "FAIL")} · right={(receipt.RightTerminalCheckpointExact ? "exact" : "FAIL")} · verdict={(receipt.Passed ? "PASS" : "FAIL")}");
            return receipt.Passed ? 0 : 1;
        });
        return command;
    }

    internal static Command RunPolicyReadoutAssay()
    {
        Option<ulong?> seed = new("--seed") { Description = "deterministic seed" };
        Option<int?> lineages = new("--lineages") { Description = "independent Weft lineages (default: 32)" };
        Option<int?> checkpoints = new("--checkpoints") { Description = "matched-fork states per lineage (default: 10)" };
        Option<int?> stride = new("--stride") { Description = "actions between matched forks (default: 4)" };
        Option<int?> horizon = new("--horizon") { Description = "counterfactual continuation actions (default: 8)" };
        Command command = new("policy-readout-assay", "compare published grammar readouts against typed, shuffled, and round-robin Weft labels")
        {
            seed,
            lineages,
            checkpoints,
            stride,
            horizon,
        };
        command.SetAction(parse => PolicyReadoutAssay.Run(
            parse.GetValue(seed) ?? 0xC0117011UL,
            parse.GetValue(lineages) ?? 32,
            parse.GetValue(checkpoints) ?? 10,
            parse.GetValue(stride) ?? 4,
            parse.GetValue(horizon) ?? 8).Passed ? 0 : 1);
        return command;
    }

    internal static Command RunPolicyBoundaryAssay()
    {
        Command command = new("policy-boundary-assay", "re-derive a policy threshold as a matched-budget obligation and verify forced-null behavior, divergence, guard, and resume identity");
        command.SetAction(_ => PolicyBoundaryAssay.Run(Console.Out).Passed ? 0 : 1);
        return command;
    }

    internal static Command VerifyPolicyBoundaryTrainingMount()
    {
        Command command = new("verify-policy-boundary-training-mount", "exercise policy-boundary training and cold-evaluation mount RON corruption gates");
        command.SetAction(_ => PolicyBoundaryTrainingMountFixture.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command ScanTowers()
    {
        Argument<string> run = new("run") { Description = "run directory or run name carrying checkpoint.bin" };
        Option<string?> output = new("--output") { Description = "destination TSV (default: <run>/recursion_towers.tsv)" };
        Command command = new("scan-towers", "stratify restored trace species and measure doubling-tower scaling over cumulative prefixes")
        {
            run,
            output,
        };
        command.SetAction(parse =>
        {
            string? runReference = parse.GetValue(run);
            if (string.IsNullOrWhiteSpace(runReference))
            {
                Console.Error.WriteLine("  scan-towers requires a run reference");
                return 1;
            }
            return Cortex.ScanRecursionTowers(runReference, parse.GetValue(output));
        });
        return command;
    }

    private static void AppendThermometryRow(StringBuilder report, in AgentSolve.LocThermometryMetrics metrics)
    {
        report.Append(metrics.Anchors).Append('\t').Append(metrics.Total).Append('\t').Append(metrics.Commits).Append('\t')
            .Append(metrics.CorrectCommits).Append('\t').Append(metrics.Solved).Append('\t').Append(metrics.SuccessAtCommit).Append('\t')
            .Append(metrics.ActionsToCommit).Append('\t').Append(metrics.CalibrationError).Append('\t')
            .Append(metrics.ReportedCalibrationError).Append('\t').Append(metrics.AbstentionRate).Append('\t')
            .Append(metrics.DeepTotal).Append('\t').Append(metrics.DeepCorrect).Append('\t').Append(metrics.DeepSuccess).AppendLine();
    }

    private static string ComputeIntakeDigest(string path)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (File.Exists(path))
        {
            AppendFileDigest(hash, path, Path.GetFileName(path));
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        List<string> files = Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToList();
        files.Sort(StringComparer.Ordinal);
        for (int i = 0; i < files.Count; i++)
            AppendFileDigest(hash, files[i], Path.GetRelativePath(path, files[i]));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendFileDigest(IncrementalHash hash, string file, string relativePath)
    {
        byte[] name = Encoding.UTF8.GetBytes(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
        hash.AppendData(name);
        hash.AppendData([0]);
        using FileStream stream = File.OpenRead(file);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            hash.AppendData(buffer, 0, read);
        hash.AppendData([0xFF]);
    }
}
