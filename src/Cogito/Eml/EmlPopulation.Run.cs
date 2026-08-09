namespace Cogito;

using System.Text;

internal readonly record struct EmlPopulationEpochRow(
    int Epoch,
    MindID Mind,
    MindLineageID Lineage,
    EmlMindKinds Kind,
    CheckpointID StartingCheckpoint,
    CheckpointID EndingCheckpoint,
    long RequestedEvaluatorCalls,
    long ActualEvaluatorCalls,
    int ShadowImports,
    int ResidentImports,
    int VerifiedShadowImports,
    int AdmittedResidentImports,
    int ExportedPackages,
    int OpenedClasses,
    int ImportCanonicalDeltas,
    string Directory);

internal sealed class EmlReplayPopulationReport
{
    private readonly string _populationReport;
    private readonly List<EmlPopulationEpochRow> _rows;

    public EmlReplayPopulationReport(
        string directory,
        string populationReport,
        List<EmlPopulationEpochRow> rows,
        bool genesisCloneExact,
        bool matchedEvaluatorCost)
    {
        Directory = directory;
        _populationReport = populationReport;
        _rows = new List<EmlPopulationEpochRow>(rows);
        GenesisCloneExact = genesisCloneExact;
        MatchedEvaluatorCost = matchedEvaluatorCost;
        CloneTrajectoryExact = HasExactCloneTrajectory(rows);
    }

    public string Directory { get; }
    public bool GenesisCloneExact { get; }
    public bool MatchedEvaluatorCost { get; }
    public bool CloneTrajectoryExact { get; }

    public string RenderReport()
    {
        StringBuilder report = new();
        report.Append("epochs\t12\n")
            .Append("minds\t4\n")
            .Append("founders\t3\n")
            .Append("clones\t1\n")
            .Append("exchange\tsealed-ron-at-frozen-checkpoint\n")
            .Append("stop_selector\teml.evaluator.calls\n")
            .Append("genesis_clone_byte_exact\t").Append(GenesisCloneExact ? 1 : 0).AppendLine()
            .Append("clone_trajectory_checkpoint_exact\t").Append(CloneTrajectoryExact ? 1 : 0).AppendLine()
            .Append("matched_total_evaluator_cost\t").Append(MatchedEvaluatorCost ? 1 : 0).AppendLine()
            .AppendLine()
            .AppendLine("epoch\tmind\tlineage\tkind\tstart_checkpoint\tend_checkpoint\trequested_calls\tactual_calls\tovershoot\tshadow_imports\tresident_imports\tverified_shadow\tadmitted_resident\texported_packages\topened_classes\timport_canonical_deltas\trun");
        for (int i = 0; i < _rows.Count; i++)
        {
            EmlPopulationEpochRow row = _rows[i];
            report.Append(row.Epoch).Append('\t')
                .Append(row.Mind.Value).Append('\t')
                .Append(row.Lineage.Value).Append('\t')
                .Append(row.Kind).Append('\t')
                .Append(row.StartingCheckpoint.Value).Append('\t')
                .Append(row.EndingCheckpoint.Value).Append('\t')
                .Append(row.RequestedEvaluatorCalls).Append('\t')
                .Append(row.ActualEvaluatorCalls).Append('\t')
                .Append(row.ActualEvaluatorCalls - row.RequestedEvaluatorCalls).Append('\t')
                .Append(row.ShadowImports).Append('\t')
                .Append(row.ResidentImports).Append('\t')
                .Append(row.VerifiedShadowImports).Append('\t')
                .Append(row.AdmittedResidentImports).Append('\t')
                .Append(row.ExportedPackages).Append('\t')
                .Append(row.OpenedClasses).Append('\t')
                .Append(row.ImportCanonicalDeltas).Append('\t')
                .AppendLine(row.Directory);
        }
        report.AppendLine().Append(_populationReport);
        return report.ToString();
    }

    private static bool HasExactCloneTrajectory(List<EmlPopulationEpochRow> rows)
    {
        for (int epoch = 0; epoch < 12; epoch++)
        {
            EmlPopulationEpochRow? clone = null;
            for (int i = 0; i < rows.Count; i++)
            {
                EmlPopulationEpochRow row = rows[i];
                if (row.Epoch != epoch) continue;
                if (row.Kind == EmlMindKinds.Clone) clone = row;
            }
            if (clone is null) return false;
            EmlPopulationEpochRow? founder = null;
            for (int i = 0; i < rows.Count; i++)
            {
                EmlPopulationEpochRow row = rows[i];
                if (row.Epoch == epoch && row.Kind == EmlMindKinds.Founder && row.Lineage == clone.Value.Lineage)
                {
                    founder = row;
                    break;
                }
            }
            if (founder is null || founder.Value.EndingCheckpoint != clone.Value.EndingCheckpoint) return false;
        }
        return true;
    }
}

internal static class EmlReplayPopulation
{
    private const int EpochCount = 12;
    private const int MaxGenesisSteps = 1_000_000;

    public static EmlReplayPopulationReport RunPopulation(
        ulong seed,
        long evaluatorCallsPerEpoch,
        long residencyHorizon,
        int strideBytes,
        int signatureDigits,
        EmlGenerationConfig generation)
    {
        if (evaluatorCallsPerEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(evaluatorCallsPerEpoch));
        if (residencyHorizon <= 0)
            throw new ArgumentOutOfRangeException(nameof(residencyHorizon));

        Run receiptRun = Run.New("eml-population");
        CortexEmlCurriculum curriculum = new()
        {
            SignatureDigits = signatureDigits,
            IntakeBatch = 1,
            Actions = EmlActionSelections.RoundRobin,
            Generation = generation,
        };
        ulong[] founderSeeds = CreateFounderSeeds(seed);
        string[] slots = ["a", "b", "c"];
        string[] founderDirectories = new string[3];
        CheckpointID[] founderCheckpoints = new CheckpointID[3];
        long[] genesisCalls = new long[3];
        for (int i = 0; i < founderSeeds.Length; i++)
        {
            (founderDirectories[i], founderCheckpoints[i], genesisCalls[i]) = RunGenesis(
                receiptRun.Dir,
                slots[i],
                founderSeeds[i],
                evaluatorCallsPerEpoch,
                strideBytes,
                curriculum);
        }
        if (genesisCalls[0] != genesisCalls[1] || genesisCalls[0] != genesisCalls[2])
            throw new InvalidDataException("founder genesis runs consumed unequal evaluator cost");

        string cloneDirectory = Path.Combine(receiptRun.Dir, "mind-a-prime");
        CopyDirectory(founderDirectories[0], cloneDirectory);
        bool genesisCloneExact = DirectoriesMatch(founderDirectories[0], cloneDirectory);
        if (!genesisCloneExact) throw new InvalidDataException("A-prime is not an exact clone of founder A");

        EmlEvaluatorID evaluator = new($"eml-grader-v1-sig-{signatureDigits}");
        string configurationDigest = CreateConfigurationDigest(signatureDigits, strideBytes, generation);
        string intakeDigest = "eml-generated-intake-v1";
        EmlCohortManifest manifest = EmlCohortManifest.Create(
            evaluator,
            configurationDigest,
            intakeDigest,
            founderSeeds,
            founderCheckpoints);
        Dictionary<MindID, string> directories = new()
        {
            [manifest.Minds[0].Mind] = founderDirectories[0],
            [manifest.Minds[1].Mind] = founderDirectories[1],
            [manifest.Minds[2].Mind] = founderDirectories[2],
            [manifest.Minds[3].Mind] = cloneDirectory,
        };
        ReplayCalcPopulationRunner runner = new(
            curriculum,
            strideBytes,
            evaluator,
            Path.Combine(receiptRun.Dir, "exchange"),
            EmlPopulationRONCodec.Instance,
            directories);
        EmlPopulation population = new(manifest, residencyHorizon);
        for (int epoch = 0; epoch < EpochCount; epoch++)
        {
            population.RunEpoch(runner, evaluatorCallsPerEpoch);
            File.WriteAllBytes(receiptRun.PathOf("cohort.ron"), population.Save(EmlPopulationRONCodec.Instance));
        }

        List<EmlPopulationEpochRow> rows = new(runner.Rows);
        bool matchedEvaluatorCost = HasMatchedEvaluatorCost(rows);
        EmlReplayPopulationReport report = new(
            receiptRun.Dir,
            population.Report(),
            rows,
            genesisCloneExact,
            matchedEvaluatorCost);
        receiptRun.Write("population.tsv", report.RenderReport());
        if (!matchedEvaluatorCost)
            throw new InvalidDataException("population minds consumed unequal actual evaluator cost");
        return report;
    }

    private static (string Directory, CheckpointID Checkpoint, long EvaluatorCalls) RunGenesis(
        string receiptDirectory,
        string slot,
        ulong seed,
        long evaluatorCalls,
        int strideBytes,
        CortexEmlCurriculum curriculum)
    {
        ReplayCalc dream = ReplayCalc.Mount(seed, curriculum);
        CortexStopCondition stop = new("eml.evaluator.calls", evaluatorCalls);
        EmlPopulationGenesisReward reward = new(dream, stop);
        List<CortexReward> rewards = ReplayCalc.CreateRewards();
        rewards.Add(reward);
        Cortex cortex = new(new CortexConfig
        {
            RunName = "eml-population-genesis-" + slot,
            Steps = MaxGenesisSteps,
            Seed = seed,
            ActionsPerStep = 1,
            Stride = new CortexStrideConfig { ReinduceBytes = strideBytes },
            Curriculum = curriculum,
            RuntimeCurriculum = dream,
            Tools = ReplayCalc.CreateActionTools(),
            ActionPolicies = ReplayCalc.CreateActionPolicies(),
            Rewards = rewards,
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0 },
        });
        int exitCode = cortex.Run();
        if (exitCode != 0) throw new InvalidOperationException($"population founder {slot} exited {exitCode}");
        string destination = Path.Combine(receiptDirectory, "mind-" + slot);
        Directory.Move(cortex.CurrentRun.Dir, destination);
        return (destination, ReplayCalcPopulationRunner.HashCheckpoint(destination), dream.EvaluatorCalls);
    }

    private static ulong[] CreateFounderSeeds(ulong seed)
    {
        ulong[] seeds = new ulong[3];
        ulong state = seed;
        for (int i = 0; i < seeds.Length; i++)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong mixed = state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            seeds[i] = mixed ^ (mixed >> 31);
        }
        return seeds;
    }

    private static string CreateConfigurationDigest(
        int signatureDigits,
        int strideBytes,
        EmlGenerationConfig generation)
    {
        EmlPopulationHash hash = new("cogito/eml/population-config/v1");
        hash.Append(signatureDigits);
        hash.Append(strideBytes);
        hash.Append(generation.SeedShells);
        hash.Append(generation.MaxLength);
        hash.Append(generation.MaxEnumerationLength);
        hash.Append(generation.SampleUnits);
        hash.Append(generation.ChunkGain);
        hash.Append(BitConverter.DoubleToInt64Bits(generation.UniformEpsilon));
        hash.Append(BitConverter.DoubleToInt64Bits(generation.EnumerationEpsilon));
        hash.Append(generation.CorroborationWeight);
        hash.Append(generation.CertificateWeight);
        return hash.Finish();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        string[] files = Directory.GetFiles(source);
        Array.Sort(files, StringComparer.Ordinal);
        for (int i = 0; i < files.Length; i++)
            File.Copy(files[i], Path.Combine(destination, Path.GetFileName(files[i])));
    }

    private static bool DirectoriesMatch(string left, string right)
    {
        string[] leftFiles = Directory.GetFiles(left);
        string[] rightFiles = Directory.GetFiles(right);
        Array.Sort(leftFiles, StringComparer.Ordinal);
        Array.Sort(rightFiles, StringComparer.Ordinal);
        if (leftFiles.Length != rightFiles.Length) return false;
        for (int i = 0; i < leftFiles.Length; i++)
        {
            if (!string.Equals(Path.GetFileName(leftFiles[i]), Path.GetFileName(rightFiles[i]), StringComparison.Ordinal)
                || !File.ReadAllBytes(leftFiles[i]).AsSpan().SequenceEqual(File.ReadAllBytes(rightFiles[i])))
                return false;
        }
        return true;
    }

    private static bool HasMatchedEvaluatorCost(List<EmlPopulationEpochRow> rows)
    {
        for (int epoch = 0; epoch < EpochCount; epoch++)
        {
            long? expected = null;
            int minds = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Epoch != epoch) continue;
                expected ??= rows[i].ActualEvaluatorCalls;
                if (rows[i].ActualEvaluatorCalls != expected.Value) return false;
                minds++;
            }
            if (minds != 4) return false;
        }
        return true;
    }
}

internal sealed class EmlPopulationGenesisReward : CortexReward
{
    private readonly ReplayCalc _dream;
    private readonly CortexStopCondition _stop;

    public EmlPopulationGenesisReward(ReplayCalc dream, CortexStopCondition stop)
    {
        _dream = dream;
        _stop = stop;
    }

    public override void OnActionBatchEnd(Cortex cortex)
    {
        if (!string.Equals(_stop.Selector, "eml.evaluator.calls", StringComparison.Ordinal))
            throw new InvalidDataException($"unsupported population stop selector '{_stop.Selector}'");
        if (_dream.EvaluatorCalls >= _stop.AtLeast) cortex.RequestStop();
    }
}
