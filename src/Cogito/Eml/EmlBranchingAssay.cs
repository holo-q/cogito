namespace Cogito;

using System.Text;

internal static class EmlBranchingAssay
{
    private const int MaxSteps = 1_000_000;

    private readonly record struct ArmResult(
        string Name,
        string Directory,
        int ExitCode,
        EmlBranchingReceipt Receipt);

    public static int RunMatched(ulong seed, long evaluatorCalls, int strideBytes,
        int signatureDigits, EmlGenerationConfig generation)
    {
        if (evaluatorCalls <= 0)
            throw new ArgumentOutOfRangeException(nameof(evaluatorCalls), evaluatorCalls,
                "branching evaluator budget must be positive");

        Run receiptRun = Run.New("eml-branching-assay");
        EmlActionSelections[] variants =
        [
            EmlActionSelections.Procedure,
            EmlActionSelections.ProcedureGuarded,
            EmlActionSelections.ProcedureGuardShuffled,
        ];
        List<ArmResult> arms = new(variants.Length);
        for (int i = 0; i < variants.Length; i++)
            arms.Add(RunArm(variants[i], seed, evaluatorCalls, strideBytes, signatureDigits, generation));

        StringBuilder report = new();
        report.AppendLine($"seed\t{seed}");
        report.AppendLine($"requested_evaluator_calls\t{evaluatorCalls}");
        report.AppendLine("arm\texit\tevaluator_calls\tdistinct_certificates\texact_classes\ttargets_hit\tprocedures_started\tprocedures_completed\tguards_passed\tguards_skipped\tguards_abstained\tcanonical_deltas\trun");
        for (int i = 0; i < arms.Count; i++)
        {
            ArmResult arm = arms[i];
            AppendArm(report, in arm);
        }
        receiptRun.Write("eml_branching.tsv", report.ToString());

        bool instrumentValid = true;
        for (int i = 0; i < arms.Count; i++)
        {
            ArmResult arm = arms[i];
            instrumentValid &= arm.ExitCode == 0;
            instrumentValid &= arm.Receipt.EvaluatorCalls >= evaluatorCalls;
        }
        instrumentValid &= arms[0].Receipt.GuardsPassed == 0;
        instrumentValid &= arms[0].Receipt.GuardsSkipped == 0;
        instrumentValid &= arms[1].Receipt.GuardsPassed + arms[1].Receipt.GuardsSkipped > 0;
        instrumentValid &= arms[2].Receipt.GuardsPassed + arms[2].Receipt.GuardsSkipped > 0;

        Console.WriteLine($"  EML branching assay → {Path.GetRelativePath(Environment.CurrentDirectory, receiptRun.PathOf("eml_branching.tsv"))}");
        return instrumentValid ? 0 : 1;
    }

    private static ArmResult RunArm(EmlActionSelections variant, ulong seed, long evaluatorCalls,
        int strideBytes, int signatureDigits, EmlGenerationConfig generation)
    {
        CortexEmlCurriculum curriculum = new()
        {
            SignatureDigits = signatureDigits,
            IntakeBatch = 1,
            Actions = variant,
            Generation = generation,
        };
        ReplayCalc dream = ReplayCalc.Mount(seed, curriculum);
        CortexConfig config = new()
        {
            RunName = "eml-r1-" + EmlActionSelectionTokens.CurriculumToken(variant).Replace(':', '-'),
            Steps = MaxSteps,
            Seed = seed,
            ActionsPerStep = 1,
            Stride = new CortexStrideConfig { ReinduceBytes = strideBytes },
            Curriculum = curriculum,
            RuntimeCurriculum = dream,
            Tools = ReplayCalc.CreateActionTools(),
            ActionPolicies = ReplayCalc.CreateActionPolicies(),
            Rewards = ReplayCalc.CreateRewards(),
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0 },
            StopConditions = new List<CortexStopCondition>
            {
                new("eml.evaluator.calls", evaluatorCalls),
            },
        };
        Cortex cortex = new(config);
        int exitCode = cortex.Run();
        return new ArmResult(
            EmlActionSelectionTokens.CurriculumToken(variant),
            cortex.CurrentRun.Dir,
            exitCode,
            dream.ReadBranchingReceipt());
    }

    private static void AppendArm(StringBuilder report, in ArmResult arm)
    {
        EmlBranchingReceipt receipt = arm.Receipt;
        report.Append(arm.Name).Append('\t')
              .Append(arm.ExitCode).Append('\t')
              .Append(receipt.EvaluatorCalls).Append('\t')
              .Append(receipt.DistinctCertificates).Append('\t')
              .Append(receipt.ExactClasses).Append('\t')
              .Append(receipt.TargetsHit).Append('\t')
              .Append(receipt.ProceduresStarted).Append('\t')
              .Append(receipt.ProceduresCompleted).Append('\t')
              .Append(receipt.GuardsPassed).Append('\t')
              .Append(receipt.GuardsSkipped).Append('\t')
              .Append(receipt.GuardsAbstained).Append('\t')
              .Append(receipt.CanonicalDeltas).Append('\t')
              .AppendLine(arm.Directory);
    }
}
