// SPDX-License-Identifier: MIT
using System.Text;

namespace Cogito;

internal static class EmlBasisCortexRematch
{
    private const int MaxSteps = 1_000_000;
    private const ulong ReplicateStride = 0x9E3779B97F4A7C15UL;

    private readonly record struct ArmResult(
        int Replicate,
        string Name,
        string Directory,
        int ExitCode,
        long EvaluatorCalls,
        int ExactClasses,
        int TheoremClasses,
        int DistinctCertificates,
        int Frontier,
        int SamplingChunks,
        Engine.RenormStat Criticality,
        bool[] SeedCaptured,
        long[] CaptureCalls,
        int[] BestK,
        string[] BestPrograms);

    internal static int RunMatched(
        ulong seed,
        long evaluatorCalls,
        int replicates,
        int strideBytes,
        int signatureDigits,
        EmlGenerationConfig generation)
    {
        if (evaluatorCalls <= 0)
            throw new ArgumentOutOfRangeException(nameof(evaluatorCalls), evaluatorCalls,
                "basis Cortex evaluator budget must be positive");
        if (replicates <= 0)
            throw new ArgumentOutOfRangeException(nameof(replicates), replicates,
                "basis Cortex replicate count must be positive");

        List<ArmResult> liveArms = new(replicates);
        List<ArmResult> frozenArms = new(replicates);
        for (int replicate = 0; replicate < replicates; replicate++)
        {
            ulong replicateSeed = unchecked(seed + (ulong)replicate * ReplicateStride);
            liveArms.Add(RunArm(replicate, "live_grammar", replicateSeed, evaluatorCalls, strideBytes, signatureDigits, generation, EmlGrammarSamplingModes.Live));
            frozenArms.Add(RunArm(replicate, "frozen_grammar", replicateSeed, evaluatorCalls, strideBytes, signatureDigits, generation, EmlGrammarSamplingModes.Frozen));
        }

        Run receiptRun = Run.New("eml-basis-cortex-rematch");
        StringBuilder report = new();
        report.AppendLine("instrument\tfull Cortex in every arm; no target program or target-derived chunk enters the sampler");
        report.AppendLine("causal_variable\twhether ReplayCalc.ResolveChunks follows newly induced grammar after the shared first grammar snapshot");
        report.AppendLine("primary_endpoint\tpost-seed nonterminal Calc-4 target-capture AUC at the paired common evaluator horizon");
        report.Append("base_seed\t").AppendLine(seed.ToString());
        report.Append("replicates\t").AppendLine(replicates.ToString());
        report.Append("requested_evaluator_calls\t").AppendLine(evaluatorCalls.ToString());
        report.AppendLine("replicate\tarm\texit\tevaluator_calls\tpost_seed_captures\teligible_targets\tcapture_auc\texact_classes\ttheorem_classes\tdistinct_certificates\tfrontier_k\tsampling_chunks\tmeanz\tcvz\tkz\trun");

        bool instrumentValid = true;
        int positiveReplicates = 0;
        int coverageRegressions = 0;
        double liveAucTotal = 0;
        double frozenAucTotal = 0;
        for (int replicate = 0; replicate < replicates; replicate++)
        {
            ArmResult live = liveArms[replicate];
            ArmResult frozen = frozenArms[replicate];
            long commonHorizon = Math.Min(live.EvaluatorCalls, frozen.EvaluatorCalls);
            bool baselineMatches = live.SeedCaptured.SequenceEqual(frozen.SeedCaptured);
            instrumentValid &= baselineMatches
                && live.ExitCode == 0
                && frozen.ExitCode == 0
                && commonHorizon >= evaluatorCalls;
            (int liveCaptures, int eligible, double liveAuc) = ScoreCaptures(in live, commonHorizon);
            (int frozenCaptures, int frozenEligible, double frozenAuc) = ScoreCaptures(in frozen, commonHorizon);
            instrumentValid &= eligible == frozenEligible;
            if (liveAuc > frozenAuc) positiveReplicates++;
            if (liveCaptures < frozenCaptures) coverageRegressions++;
            liveAucTotal += liveAuc;
            frozenAucTotal += frozenAuc;
            AppendArm(report, in live, commonHorizon, liveCaptures, eligible, liveAuc);
            AppendArm(report, in frozen, commonHorizon, frozenCaptures, frozenEligible, frozenAuc);
        }

        report.AppendLine();
        report.AppendLine("replicate\ttarget\tseed_captured\tlive_capture_call\tfrozen_capture_call\tlive_k\tfrozen_k\tlive_program\tfrozen_program");
        EmlTarget[] targets = EmlScientificCalculatorBasis.CreateTargets();
        for (int replicate = 0; replicate < replicates; replicate++)
        {
            ArmResult live = liveArms[replicate];
            ArmResult frozen = frozenArms[replicate];
            for (int i = 0; i < targets.Length; i++)
            {
                report.Append(replicate).Append('\t')
                      .Append(targets[i].Label).Append('\t')
                      .Append(live.SeedCaptured[i] ? 1 : 0).Append('\t')
                      .Append(live.CaptureCalls[i]).Append('\t')
                      .Append(frozen.CaptureCalls[i]).Append('\t')
                      .Append(live.BestK[i]).Append('\t')
                      .Append(frozen.BestK[i]).Append('\t')
                      .Append(live.BestPrograms[i]).Append('\t')
                      .AppendLine(frozen.BestPrograms[i]);
            }
        }

        bool grammarAdvantage = liveAucTotal > frozenAucTotal
            && positiveReplicates * 2 > replicates
            && coverageRegressions == 0;
        report.AppendLine();
        report.Append("instrument_verdict\t").AppendLine(instrumentValid ? "VALID" : "INVALID");
        report.Append("grammar_sampler_verdict\t").AppendLine(grammarAdvantage ? "OBSERVED_ADVANTAGE" : "NO_ADVANTAGE");
        report.Append("positive_auc_replicates\t").Append(positiveReplicates).Append('/').AppendLine(replicates.ToString());
        report.Append("coverage_regressions\t").AppendLine(coverageRegressions.ToString());
        report.Append("live_auc_total\t").AppendLine(liveAucTotal.ToString("G17"));
        report.Append("frozen_auc_total\t").AppendLine(frozenAucTotal.ToString("G17"));
        receiptRun.Write("eml_basis_cortex_rematch.tsv", report.ToString());
        Console.WriteLine($"  EML basis Cortex rematch -> {Path.GetRelativePath(Environment.CurrentDirectory, receiptRun.PathOf("eml_basis_cortex_rematch.tsv"))}");
        return instrumentValid ? 0 : 1;
    }

    private static ArmResult RunArm(
        int replicate,
        string name,
        ulong seed,
        long evaluatorCalls,
        int strideBytes,
        int signatureDigits,
        EmlGenerationConfig generation,
        EmlGrammarSamplingModes grammarSampling)
    {
        CortexEmlCurriculum curriculum = new()
        {
            SignatureDigits = signatureDigits,
            IntakeBatch = 32,
            TargetCatalog = EmlTargetCatalogs.ScientificCalculator,
            GrammarSampling = grammarSampling,
            Generation = generation,
        };
        ReplayCalc dream = ReplayCalc.Mount(seed, curriculum);
        EmlBasisCaptureProbe probe = new(dream);
        CortexConfig config = new()
        {
            RunName = $"eml-basis-cortex-r{replicate}-{name.Replace('_', '-')}",
            Steps = MaxSteps,
            Seed = seed,
            Stride = new CortexStrideConfig { ReinduceBytes = strideBytes },
            Curriculum = curriculum,
            RuntimeCurriculum = dream,
            Rewards = new List<CortexReward> { probe },
            Learning = new CortexLearningConfig
            {
                ConsolidationPhaseControl = CortexConsolidationPhaseControl.Homeostat,
                EvidenceWeightScale = 8,
                CrossReflect = true,
                NearDupe = true,
                Antiunify = true,
                Loom = true,
                Shed = true,
                Rhythm = true,
            },
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0 },
            Readout = new CortexReadoutConfig
            {
                Curve =
                [
                    "eml.evaluator.calls",
                    "eml.targets.train_hit",
                    "eml.census.exact",
                    "eml.census.theorem",
                    "eml.census.certs",
                    "eml.frontier.k",
                ],
            },
            StopConditions = new List<CortexStopCondition>
            {
                new("eml.evaluator.calls", evaluatorCalls),
            },
        };
        Cortex cortex = new(config);
        int exitCode = cortex.Run();
        EmlSieve sieve = dream.Sieve;
        int[] bestK = new int[sieve.Targets.Count];
        string[] programs = new string[sieve.Targets.Count];
        for (int i = 0; i < sieve.Targets.Count; i++)
        {
            bestK[i] = sieve.BestK(i);
            programs[i] = sieve.BestProg(i) ?? "";
        }
        return new ArmResult(
            replicate,
            name,
            cortex.CurrentRun.Dir,
            exitCode,
            dream.EvaluatorCalls,
            sieve.ExactClasses,
            sieve.TheoremClasses,
            sieve.DistinctCerts,
            sieve.KFrontier,
            dream.SamplingChunkCount,
            Engine.RenormStats(cortex.Grammar),
            probe.SeedCaptured,
            probe.CaptureCalls,
            bestK,
            programs);
    }

    private static (int Captures, int Eligible, double Auc) ScoreCaptures(in ArmResult arm, long horizon)
    {
        EmlTarget[] targets = EmlScientificCalculatorBasis.CreateTargets();
        int captures = 0;
        int eligible = 0;
        double area = 0;
        for (int i = 0; i < targets.Length; i++)
        {
            if (EmlScientificCalculatorBasis.IsGivenTerminal(targets[i]) || arm.SeedCaptured[i]) continue;
            eligible++;
            long capture = arm.CaptureCalls[i];
            if (capture < 0 || capture > horizon) continue;
            captures++;
            area += (double)(horizon - capture) / horizon;
        }
        return (captures, eligible, eligible == 0 ? 0 : area / eligible);
    }

    private static void AppendArm(
        StringBuilder report,
        in ArmResult arm,
        long commonHorizon,
        int captures,
        int eligible,
        double auc)
    {
        report.Append(arm.Replicate).Append('\t')
              .Append(arm.Name).Append('\t')
              .Append(arm.ExitCode).Append('\t')
              .Append(arm.EvaluatorCalls).Append('\t')
              .Append(captures).Append('\t')
              .Append(eligible).Append('\t')
              .Append(auc.ToString("G17")).Append('\t')
              .Append(arm.ExactClasses).Append('\t')
              .Append(arm.TheoremClasses).Append('\t')
              .Append(arm.DistinctCertificates).Append('\t')
              .Append(arm.Frontier).Append('\t')
              .Append(arm.SamplingChunks).Append('\t')
              .Append(arm.Criticality.MeanZ.ToString("G17")).Append('\t')
              .Append(arm.Criticality.CvZ.ToString("G17")).Append('\t')
              .Append(arm.Criticality.KZ).Append('\t')
              .AppendLine(arm.Directory);
    }

    private sealed class EmlBasisCaptureProbe : CortexReward
    {
        private readonly ReplayCalc _dream;

        internal EmlBasisCaptureProbe(ReplayCalc dream)
        {
            _dream = dream;
            SeedCaptured = new bool[dream.Sieve.Targets.Count];
            CaptureCalls = new long[dream.Sieve.Targets.Count];
            Array.Fill(CaptureCalls, -1);
        }

        internal bool[] SeedCaptured { get; }
        internal long[] CaptureCalls { get; }

        public override void OnRunStart(Cortex cortex)
        {
            for (int i = 0; i < SeedCaptured.Length; i++)
            {
                SeedCaptured[i] = _dream.Sieve.BestK(i) >= 0;
                if (SeedCaptured[i]) CaptureCalls[i] = _dream.EvaluatorCalls;
            }
        }

        public override void OnStepStart(Cortex cortex, int step) => RecordCaptures();

        public override void OnRunEnd(Cortex cortex) => RecordCaptures();

        private void RecordCaptures()
        {
            for (int i = 0; i < CaptureCalls.Length; i++)
                if (CaptureCalls[i] < 0 && _dream.Sieve.BestK(i) >= 0)
                    CaptureCalls[i] = _dream.EvaluatorCalls;
        }
    }
}
