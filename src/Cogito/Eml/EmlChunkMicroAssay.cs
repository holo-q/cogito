using System.Globalization;
using System.Text;

namespace Cogito;

internal static class EmlChunkMicroAssay
{
    private const long DefaultCalls = 100_000;
    private const int DefaultSeedK = 7;
    private const int DefaultMaxLength = 80;
    private const int DefaultUnits = 6;
    private const int DefaultGain = 4;
    private const int DefaultSignatureDigits = 9;
    private const ulong DefaultSeed = 0xE311_C0DEUL;
    private const double UniformEpsilon = 0.125;
    private const int ExpectedTargets = 36;
    private const int ExpectedNonterminalTargets = 33;

    private enum ChunkMicroAssayArms
    {
        CapturedChunkReuse,
        TerminalRotatedChunkNull,
        NoChunkReuse,
    }

    private enum ChunkMicroAssayPlanes
    {
        AllTargets,
        Holdout,
    }

    private readonly record struct AssayConfig(
        long Calls,
        int SeedK,
        int MaxLength,
        int Units,
        int Gain,
        int SignatureDigits,
        ulong Seed);

    private readonly record struct ChunkRelease(long Call, int TargetIndex, string Program);

    private readonly record struct TargetCapture(
        int Order,
        int TargetIndex,
        string Label,
        bool Terminal,
        bool Train,
        long Call,
        long Offer,
        int K,
        string Program);

    private readonly record struct CoverageRead(int Train, int Held, int Full, int Terminals);

    private sealed record ArmRun(
        ChunkMicroAssayArms Arm,
        EmlSieve Sieve,
        List<TargetCapture> Captures,
        int ChunkCount,
        long Calls,
        long Offers,
        long Overshoot,
        CoverageRead Coverage,
        CoverageRead[] Thirds,
        double TrainTime,
        double HeldTime,
        double FullTime);

    private sealed record PlaneRun(
        ChunkMicroAssayPlanes Plane,
        bool[] TrainMask,
        List<ChunkRelease> ReleaseSchedule,
        ArmRun Captured,
        ArmRun TerminalRotated,
        ArmRun NoChunkReuse,
        bool BeatsShuffled,
        bool BeatsNoChunkReuse,
        bool HasChunkReuseAdvantage);

    public static int Run(string[] args)
    {
        AssayConfig config = ParseConfig(args);
        EmlTarget[] targets = EmlScientificCalculatorBasis.CreateTargets();
        ValidateCatalog(targets);

        bool[] seedReachable = MeasureSeedReachability(config, targets);
        PlaneRun allTargets = ExecutePlane(
            ChunkMicroAssayPlanes.AllTargets, config, targets, CreateAllTargetsTrainMask(targets));
        PlaneRun holdout = ExecutePlane(
            ChunkMicroAssayPlanes.Holdout, config, targets,
            BuildTrainMask(targets, seedReachable, config.Seed));

        Run run = Cogito.Run.New("eml-chunk-micro-assay");
        const string ArtifactName = "eml_chunk_micro_assay.tsv";
        run.Write(ArtifactName, RenderArtifact(
            config, targets, seedReachable, allTargets, holdout));
        RenderConsole(run, ArtifactName, config, targets, allTargets, holdout);
        return 0;
    }

    private static AssayConfig ParseConfig(string[] args)
    {
        long calls = DefaultCalls;
        int seedK = DefaultSeedK;
        int maxLength = DefaultMaxLength;
        int units = DefaultUnits;
        int gain = DefaultGain;
        int signatureDigits = DefaultSignatureDigits;
        ulong seed = DefaultSeed;

        int firstArgument = args.Length > 0 && args[0] == "chunk-micro-assay" ? 1 : 0;
        for (int i = firstArgument; i < args.Length; i++)
        {
            string name = args[i];
            if (i + 1 >= args.Length) throw new ArgumentException($"missing value for {name}", nameof(args));
            string value = args[++i];
            switch (name)
            {
                case "--calls": calls = ParseLong(name, value); break;
                case "--seedk": seedK = ParseInt(name, value); break;
                case "--maxlen": maxLength = ParseInt(name, value); break;
                case "--units": units = ParseInt(name, value); break;
                case "--gain": gain = ParseInt(name, value); break;
                case "--sig": signatureDigits = ParseInt(name, value); break;
                case "--seed": seed = ParseULong(name, value); break;
                default: throw new ArgumentException($"unknown EML chunk micro-assay argument {name}", nameof(args));
            }
        }

        if (calls <= 0) throw new ArgumentOutOfRangeException(nameof(args), "--calls must be positive");
        if (seedK < 1 || seedK > 7) throw new ArgumentOutOfRangeException(nameof(args), "--seedk must be in [1, 7]");
        if (maxLength < seedK || maxLength > Eml.MaxProgramLen)
            throw new ArgumentOutOfRangeException(nameof(args), $"--maxlen must be in [{seedK}, {Eml.MaxProgramLen}]");
        if (units < 2) throw new ArgumentOutOfRangeException(nameof(args), "--units must be at least 2");
        if (gain < 1) throw new ArgumentOutOfRangeException(nameof(args), "--gain must be positive");
        if (signatureDigits < 1 || signatureDigits > 15)
            throw new ArgumentOutOfRangeException(nameof(args), "--sig must be in [1, 15]");
        return new AssayConfig(calls, seedK, maxLength, units, gain, signatureDigits, seed);
    }

    private static long ParseLong(string name, string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            throw new ArgumentException($"invalid {name} value {value}");
        return parsed;
    }

    private static int ParseInt(string name, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new ArgumentException($"invalid {name} value {value}");
        return parsed;
    }

    private static ulong ParseULong(string name, string value)
    {
        ReadOnlySpan<char> digits = value.AsSpan();
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) digits = digits[2..];
        if (!ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
            throw new ArgumentException($"invalid {name} value {value}");
        return parsed;
    }

    private static void ValidateCatalog(EmlTarget[] targets)
    {
        if (targets.Length != ExpectedTargets)
            throw new InvalidDataException($"Calc-4 catalog must contain {ExpectedTargets} targets, observed {targets.Length}");
        int terminals = 0;
        bool containsI = false;
        HashSet<string> labels = new(StringComparer.Ordinal);
        for (int i = 0; i < targets.Length; i++)
        {
            if (!labels.Add(targets[i].Label))
                throw new InvalidDataException($"duplicate Calc-4 target label {targets[i].Label}");
            if (IsTerminalTarget(targets[i])) terminals++;
            if (targets[i].Label == "i") containsI = true;
        }
        if (terminals != 3 || targets.Length - terminals != ExpectedNonterminalTargets)
            throw new InvalidDataException($"Calc-4 catalog must expose 3 terminals and {ExpectedNonterminalTargets} nonterminal targets");
        if (!containsI) throw new InvalidDataException("Calc-4 catalog must retain i as an independently captured nonterminal target");
    }

    private static bool[] MeasureSeedReachability(AssayConfig config, EmlTarget[] targets)
    {
        EmlSieve sieve = new(config.SignatureDigits, targets, null);
        foreach (string program in EmlGen.Enumerate(1, config.SeedK)) sieve.Offer(program);
        bool[] reachable = new bool[targets.Length];
        for (int i = 0; i < targets.Length; i++) reachable[i] = sieve.BestK(i) >= 0;
        return reachable;
    }

    private static bool[] BuildTrainMask(EmlTarget[] targets, bool[] seedReachable, ulong seed)
    {
        bool[] trainMask = new bool[targets.Length];
        List<(ulong Rank, int Index)> advanced = new();
        for (int i = 0; i < targets.Length; i++)
        {
            if (IsTerminalTarget(targets[i]) || seedReachable[i]) trainMask[i] = true;
            else advanced.Add((RankTarget(targets[i].Label, seed), i));
        }
        advanced.Sort(static (left, right) =>
            left.Rank != right.Rank ? left.Rank.CompareTo(right.Rank) : left.Index.CompareTo(right.Index));
        int held = advanced.Count / 2;
        for (int i = held; i < advanced.Count; i++) trainMask[advanced[i].Index] = true;
        return trainMask;
    }

    private static bool[] CreateAllTargetsTrainMask(EmlTarget[] targets)
    {
        bool[] trainMask = new bool[targets.Length];
        Array.Fill(trainMask, true);
        return trainMask;
    }

    private static ulong RankTarget(string label, ulong seed)
    {
        ulong hash = 14695981039346656037UL ^ seed;
        for (int i = 0; i < label.Length; i++)
        {
            hash ^= label[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static PlaneRun ExecutePlane(
        ChunkMicroAssayPlanes plane,
        AssayConfig config,
        EmlTarget[] targets,
        bool[] trainMask)
    {
        List<ChunkRelease> releaseSchedule = new();
        ArmRun captured = ExecuteArm(
            ChunkMicroAssayArms.CapturedChunkReuse, config, targets, trainMask, releaseSchedule);
        ArmRun terminalRotated = ExecuteArm(
            ChunkMicroAssayArms.TerminalRotatedChunkNull, config, targets, trainMask, releaseSchedule);
        ArmRun noReuse = ExecuteArm(
            ChunkMicroAssayArms.NoChunkReuse, config, targets, trainMask, releaseSchedule);
        bool beatsShuffled = plane == ChunkMicroAssayPlanes.AllTargets
            ? BeatsAllTargetsArm(captured, terminalRotated)
            : BeatsHoldoutArm(captured, terminalRotated);
        bool beatsNoChunkReuse = plane == ChunkMicroAssayPlanes.AllTargets
            ? BeatsAllTargetsArm(captured, noReuse)
            : BeatsHoldoutArm(captured, noReuse);
        return new PlaneRun(
            plane, trainMask, releaseSchedule, captured, terminalRotated, noReuse,
            beatsShuffled, beatsNoChunkReuse, beatsShuffled && beatsNoChunkReuse);
    }

    private static ArmRun ExecuteArm(
        ChunkMicroAssayArms arm,
        AssayConfig config,
        EmlTarget[] targets,
        bool[] trainMask,
        List<ChunkRelease> releaseSchedule)
    {
        EmlSieve sieve = new(config.SignatureDigits, targets, trainMask);
        List<EmlGen.Chunk> chunks = new();
        HashSet<string> chunkPrograms = new(StringComparer.Ordinal);
        List<TargetCapture> captures = new();
        bool[] captured = new bool[targets.Length];
        StringBuilder sampleBuilder = new();
        List<(string Toks, int Weight, int DeltaH)> samplePool = new();
        ulong rng = config.Seed == 0 ? DefaultSeed : config.Seed;
        int scheduledCursor = 0;

        foreach (string program in EmlGen.Enumerate(1, config.SeedK))
        {
            sieve.Offer(program);
            CaptureTargets(sieve, targets, trainMask, captured, captures, program);
        }

        if (sieve.EvaluatorClock.ProgramPointEvaluations >= config.Calls)
            throw new ArgumentOutOfRangeException(nameof(config),
                $"--calls {config.Calls} does not exceed the exhaustive K<={config.SeedK} seed cost {sieve.EvaluatorClock.ProgramPointEvaluations}");

        if (arm == ChunkMicroAssayArms.CapturedChunkReuse)
        {
            for (int i = 0; i < captures.Count; i++)
                TryReleaseVerifiedChunk(captures[i], sieve, targets, chunks, chunkPrograms, releaseSchedule);
        }
        else if (arm == ChunkMicroAssayArms.TerminalRotatedChunkNull)
        {
            scheduledCursor = ApplyScheduledChunks(
                releaseSchedule, scheduledCursor, sieve.EvaluatorClock.ProgramPointEvaluations,
                terminalRotated: true, chunks, chunkPrograms);
        }

        while (sieve.EvaluatorClock.ProgramPointEvaluations < config.Calls)
        {
            if (arm == ChunkMicroAssayArms.TerminalRotatedChunkNull)
                scheduledCursor = ApplyScheduledChunks(
                    releaseSchedule, scheduledCursor, sieve.EvaluatorClock.ProgramPointEvaluations,
                    terminalRotated: true, chunks, chunkPrograms);

            string program = EmlGen.Sample(
                chunks, config.Units, config.MaxLength, config.Gain, UniformEpsilon,
                ref rng, sampleBuilder, samplePool);
            int captureStart = captures.Count;
            sieve.Offer(program);
            CaptureTargets(sieve, targets, trainMask, captured, captures, program);
            if (arm == ChunkMicroAssayArms.CapturedChunkReuse)
                for (int i = captureStart; i < captures.Count; i++)
                    TryReleaseVerifiedChunk(captures[i], sieve, targets, chunks, chunkPrograms, releaseSchedule);
        }

        long calls = sieve.EvaluatorClock.ProgramPointEvaluations;
        CoverageRead coverage = MeasureCoverage(captures, trainMask, calls);
        CoverageRead[] thirds = MeasureThirds(captures, trainMask, config.Calls, calls);
        return new ArmRun(
            arm,
            sieve,
            captures,
            chunks.Count,
            calls,
            sieve.EvaluatorClock.OfferRequests,
            calls - config.Calls,
            coverage,
            thirds,
            MeasureNormalizedCaptureTime(captures, targets, trainMask, config.Calls, CaptureSets.Train),
            MeasureNormalizedCaptureTime(captures, targets, trainMask, config.Calls, CaptureSets.Held),
            MeasureNormalizedCaptureTime(captures, targets, trainMask, config.Calls, CaptureSets.Full));
    }

    private static void CaptureTargets(
        EmlSieve sieve,
        EmlTarget[] targets,
        bool[] trainMask,
        bool[] captured,
        List<TargetCapture> captures,
        string offeredProgram)
    {
        IReadOnlyCollection<int> heldCaptured = sieve.HeldCaptured;
        for (int i = 0; i < targets.Length; i++)
        {
            bool nowCaptured = trainMask[i] ? sieve.BestK(i) >= 0 : heldCaptured.Contains(i);
            if (!nowCaptured || captured[i]) continue;
            captured[i] = true;
            string program = trainMask[i] ? sieve.BestProg(i) ?? offeredProgram : offeredProgram;
            captures.Add(new TargetCapture(
                captures.Count + 1,
                i,
                targets[i].Label,
                IsTerminalTarget(targets[i]),
                trainMask[i],
                sieve.EvaluatorClock.ProgramPointEvaluations,
                sieve.EvaluatorClock.OfferRequests,
                program.Length,
                program));
        }
    }

    private static void TryReleaseVerifiedChunk(
        TargetCapture capture,
        EmlSieve sieve,
        EmlTarget[] targets,
        List<EmlGen.Chunk> chunks,
        HashSet<string> chunkPrograms,
        List<ChunkRelease> releaseSchedule)
    {
        if (capture.Terminal || !capture.Train) return;
        string? bestProgram = sieve.BestProg(capture.TargetIndex);
        if (bestProgram is null) return;
        if (!TryAddClosedChunk(bestProgram, chunks, chunkPrograms))
            throw new InvalidDataException($"exact target {targets[capture.TargetIndex].Label} produced a non-closed RPN program");
        releaseSchedule.Add(new ChunkRelease(capture.Call, capture.TargetIndex, bestProgram));
    }

    private static int ApplyScheduledChunks(
        List<ChunkRelease> schedule,
        int cursor,
        long call,
        bool terminalRotated,
        List<EmlGen.Chunk> chunks,
        HashSet<string> chunkPrograms)
    {
        while (cursor < schedule.Count && schedule[cursor].Call <= call)
        {
            string program = terminalRotated ? RotateTerminals(schedule[cursor].Program) : schedule[cursor].Program;
            if (!TryAddClosedChunk(program, chunks, chunkPrograms))
                throw new InvalidDataException("scheduled captured chunk is not a complete closed RPN program");
            cursor++;
        }
        return cursor;
    }

    private static bool TryAddClosedChunk(
        string program,
        List<EmlGen.Chunk> chunks,
        HashSet<string> chunkPrograms)
    {
        (int deltaH, int minReq) = EmlGen.StackProfile(program);
        if (deltaH != 1 || minReq != 0 || EmlGen.ClosedSpans(program).Count == 0) return false;
        if (chunkPrograms.Add(program)) chunks.Add(new EmlGen.Chunk(program, 1, deltaH, minReq));
        return true;
    }

    private static string RotateTerminals(string program)
    {
        char[] rotated = program.ToCharArray();
        for (int i = 0; i < rotated.Length; i++)
            rotated[i] = rotated[i] switch
            {
                Eml.One => Eml.VarX,
                Eml.VarX => Eml.VarY,
                Eml.VarY => Eml.One,
                _ => rotated[i],
            };
        return new string(rotated);
    }

    private static bool IsTerminalTarget(EmlTarget target)
        => target.Label is "1" or "x" or "y";

    private enum CaptureSets
    {
        Train,
        Held,
        Full,
    }

    private static CoverageRead MeasureCoverage(
        List<TargetCapture> captures,
        bool[] trainMask,
        long throughCall)
    {
        int train = 0;
        int held = 0;
        int terminals = 0;
        for (int i = 0; i < captures.Count; i++)
        {
            TargetCapture capture = captures[i];
            if (capture.Call > throughCall) continue;
            if (capture.Terminal) terminals++;
            else if (trainMask[capture.TargetIndex]) train++;
            else held++;
        }
        return new CoverageRead(train, held, train + held, terminals);
    }

    private static CoverageRead[] MeasureThirds(
        List<TargetCapture> captures,
        bool[] trainMask,
        long budget,
        long actualCalls)
        =>
        [
            MeasureCoverage(captures, trainMask, budget / 3),
            MeasureCoverage(captures, trainMask, budget * 2 / 3),
            MeasureCoverage(captures, trainMask, actualCalls),
        ];

    private static double MeasureNormalizedCaptureTime(
        List<TargetCapture> captures,
        EmlTarget[] targets,
        bool[] trainMask,
        long budget,
        CaptureSets set)
    {
        long total = 0;
        int count = 0;
        Dictionary<int, long> callsByTarget = new();
        for (int i = 0; i < captures.Count; i++) callsByTarget[captures[i].TargetIndex] = captures[i].Call;
        for (int i = 0; i < targets.Length; i++)
        {
            if (IsTerminalTarget(targets[i])) continue;
            bool included = set == CaptureSets.Full
                || set == CaptureSets.Train && trainMask[i]
                || set == CaptureSets.Held && !trainMask[i];
            if (!included) continue;
            total += Math.Min(budget, callsByTarget.GetValueOrDefault(i, budget));
            count++;
        }
        return count == 0 ? 1 : (double)total / count / budget;
    }

    private static bool BeatsAllTargetsArm(ArmRun captured, ArmRun nullArm)
        => captured.Coverage.Full > nullArm.Coverage.Full
            || captured.FullTime < nullArm.FullTime;

    private static bool BeatsHoldoutArm(ArmRun captured, ArmRun nullArm)
        => captured.Coverage.Held > nullArm.Coverage.Held
            || captured.Coverage.Full > nullArm.Coverage.Full
            || captured.HeldTime < nullArm.HeldTime
            || captured.FullTime < nullArm.FullTime;

    private static string RenderArtifact(
        AssayConfig config,
        EmlTarget[] targets,
        bool[] seedReachable,
        PlaneRun allTargets,
        PlaneRun holdout)
    {
        StringBuilder report = new("kind\tplane\tarm\tname\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\tvalue7\n");
        report.Append("config\tall\tall\tcalls\t").Append(config.Calls)
            .Append("\tseedk\t").Append(config.SeedK)
            .Append("\tmaxlen\t").Append(config.MaxLength)
            .Append("\tunits\t").Append(config.Units).AppendLine();
        report.Append("config\tall\tall\tgain\t").Append(config.Gain)
            .Append("\tsig\t").Append(config.SignatureDigits)
            .Append("\tseed\t").Append(config.Seed).AppendLine();
        AppendPlaneRows(report, targets, seedReachable, allTargets);
        AppendPlaneRows(report, targets, seedReachable, holdout);
        report.Append("overall\tdual_plane\tall\tseparate_verdicts")
            .Append("\tall_targets\t").Append(allTargets.HasChunkReuseAdvantage ? "ADVANTAGE" : "HOLD")
            .Append("\tholdout\t").Append(holdout.HasChunkReuseAdvantage ? "ADVANTAGE" : "HOLD")
            .Append("\tmicro_assay_does_not_test_cortex_rematch").AppendLine();
        return report.ToString();
    }

    private static void AppendPlaneRows(
        StringBuilder report,
        EmlTarget[] targets,
        bool[] seedReachable,
        PlaneRun plane)
    {
        string planeName = FormatPlane(plane.Plane);
        for (int i = 0; i < targets.Length; i++)
            report.Append("target\t").Append(planeName).Append("\tall\t").Append(targets[i].Label)
                .Append('\t').Append(IsTerminalTarget(targets[i]) ? "terminal" : "nonterminal target")
                .Append('\t').Append(seedReachable[i] ? "seed-reachable" : "advanced")
                .Append('\t').Append(plane.TrainMask[i] ? "train" : "held").AppendLine();
        AppendArmRows(report, planeName, plane.Captured, targets, plane.TrainMask);
        AppendArmRows(report, planeName, plane.TerminalRotated, targets, plane.TrainMask);
        AppendArmRows(report, planeName, plane.NoChunkReuse, targets, plane.TrainMask);
        for (int i = 0; i < plane.ReleaseSchedule.Count; i++)
            report.Append("release\t").Append(planeName).Append("\tcaptured_chunk_reuse\t").Append(i + 1)
                .Append('\t').Append(plane.ReleaseSchedule[i].Call)
                .Append('\t').Append(targets[plane.ReleaseSchedule[i].TargetIndex].Label)
                .Append('\t').Append(plane.ReleaseSchedule[i].Program)
                .Append('\t').Append(RotateTerminals(plane.ReleaseSchedule[i].Program)).AppendLine();
        string criterion = plane.Plane == ChunkMicroAssayPlanes.AllTargets
            ? "all_target_coverage_or_all_target_ttc"
            : "held_coverage_or_all_target_coverage_or_held_ttc_or_all_target_ttc";
        report.Append("verdict\t").Append(planeName).Append("\tcaptured_chunk_reuse\t").Append(criterion)
            .Append("\tterminal_rotated_chunk_null\t").Append(plane.BeatsShuffled ? "beat" : "hold")
            .Append("\tno_chunk_reuse\t").Append(plane.BeatsNoChunkReuse ? "beat" : "hold")
            .Append("\tfinal\t").Append(plane.HasChunkReuseAdvantage ? "ADVANTAGE" : "HOLD").AppendLine();
    }

    private static void AppendArmRows(
        StringBuilder report,
        string plane,
        ArmRun arm,
        EmlTarget[] targets,
        bool[] trainMask)
    {
        string name = FormatArm(arm.Arm);
        int trainTotal = CountNonterminalTargets(targets, trainMask, train: true);
        int heldTotal = CountNonterminalTargets(targets, trainMask, train: false);
        report.Append("arm\t").Append(plane).Append('\t').Append(name).Append("\tsummary\t")
            .Append(arm.Calls).Append('\t').Append(arm.Offers).Append('\t')
            .Append(arm.Coverage.Train).Append('/').Append(trainTotal).Append('\t')
            .Append(arm.Coverage.Held).Append('/').Append(heldTotal).Append('\t')
            .Append(arm.Coverage.Full).Append('/').Append(ExpectedNonterminalTargets).Append('\t')
            .Append(arm.Coverage.Terminals).Append("/3\t").Append(arm.ChunkCount).AppendLine();
        report.Append("arm\t").Append(plane).Append('\t').Append(name).Append("\tfrontier\t")
            .Append(arm.Sieve.KFrontier).Append('\t').Append(arm.Sieve.ExactClasses).Append('\t')
            .Append(arm.Sieve.TheoremClasses).Append('\t').Append(arm.Overshoot).Append('\t')
            .Append(arm.TrainTime.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
            .Append(arm.HeldTime.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
            .Append(arm.FullTime.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
            .Append(KeepsGrowingByThirds(arm) ? "growing_by_thirds" : "not_growing_by_thirds").AppendLine();
        for (int i = 0; i < arm.Thirds.Length; i++)
            report.Append("third\t").Append(plane).Append('\t').Append(name).Append('\t').Append(i + 1)
                .Append('\t').Append(arm.Thirds[i].Train)
                .Append('\t').Append(arm.Thirds[i].Held)
                .Append('\t').Append(arm.Thirds[i].Full)
                .Append('\t').Append(arm.Thirds[i].Terminals).AppendLine();
        for (int i = 0; i < arm.Captures.Count; i++)
        {
            TargetCapture capture = arm.Captures[i];
            report.Append("capture\t").Append(plane).Append('\t').Append(name).Append('\t').Append(capture.Order)
                .Append('\t').Append(capture.Call).Append('\t').Append(capture.Offer)
                .Append('\t').Append(capture.Label)
                .Append('\t').Append(capture.Terminal ? "terminal" : capture.Train ? "train" : "held")
                .Append('\t').Append(capture.K).Append('\t').Append(capture.Program).AppendLine();
        }
    }

    private static void RenderConsole(
        Run run,
        string artifactName,
        AssayConfig config,
        EmlTarget[] targets,
        PlaneRun allTargets,
        PlaneRun holdout)
    {
        Console.WriteLine($"  EML chunk micro-assay -> {Path.GetRelativePath(Environment.CurrentDirectory, run.PathOf(artifactName))}");
        Console.WriteLine($"  budget {config.Calls:N0} evaluator calls per arm · exhaustive K<={config.SeedK} · 33 nonterminal targets · 3 given terminals");
        RenderPlaneConsole(allTargets, targets);
        RenderPlaneConsole(holdout, targets);
        Console.WriteLine($"  direct-sampler verdicts: all-targets {(allTargets.HasChunkReuseAdvantage ? "ADVANTAGE" : "HOLD")} · holdout {(holdout.HasChunkReuseAdvantage ? "ADVANTAGE" : "HOLD")} · this micro-assay does not test Cortex");
    }

    private static void RenderPlaneConsole(PlaneRun plane, EmlTarget[] targets)
    {
        int trainTotal = CountNonterminalTargets(targets, plane.TrainMask, train: true);
        int heldTotal = CountNonterminalTargets(targets, plane.TrainMask, train: false);
        string planeName = FormatPlane(plane.Plane);
        Console.WriteLine($"  [{planeName}] nonterminal targets train {trainTotal} / held {heldTotal}");
        RenderArmConsole(plane.Captured, trainTotal, heldTotal);
        RenderArmConsole(plane.TerminalRotated, trainTotal, heldTotal);
        RenderArmConsole(plane.NoChunkReuse, trainTotal, heldTotal);
        Console.WriteLine($"    verdict captured chunks vs terminal-rotated null {(plane.BeatsShuffled ? "BEAT" : "HOLD")} · vs no-chunk null {(plane.BeatsNoChunkReuse ? "BEAT" : "HOLD")} -> {(plane.HasChunkReuseAdvantage ? "ADVANTAGE" : "HOLD")}");
    }

    private static void RenderArmConsole(ArmRun arm, int trainTotal, int heldTotal)
    {
        string name = FormatArm(arm.Arm);
        Console.WriteLine(
            $"  {name,-22} calls {arm.Calls,8:N0} (+{arm.Overshoot}) · offers {arm.Offers,7:N0} · coverage train {arm.Coverage.Train}/{trainTotal} held {arm.Coverage.Held}/{heldTotal} full {arm.Coverage.Full}/{ExpectedNonterminalTargets} · terminals {arm.Coverage.Terminals}/3 · chunks {arm.ChunkCount} · K {arm.Sieve.KFrontier} · exact/theorem {arm.Sieve.ExactClasses}/{arm.Sieve.TheoremClasses}");
        Console.WriteLine(
            $"    thirds train {arm.Thirds[0].Train}/{arm.Thirds[1].Train}/{arm.Thirds[2].Train} · held {arm.Thirds[0].Held}/{arm.Thirds[1].Held}/{arm.Thirds[2].Held} · full {arm.Thirds[0].Full}/{arm.Thirds[1].Full}/{arm.Thirds[2].Full} · named {(KeepsGrowingByThirds(arm) ? "GROWING" : "NOT GROWING")} · normalized TTC train/held/full {arm.TrainTime:F4}/{arm.HeldTime:F4}/{arm.FullTime:F4}");
        StringBuilder captures = new("    captures ");
        for (int i = 0; i < arm.Captures.Count; i++)
        {
            if (i > 0) captures.Append(" -> ");
            captures.Append(arm.Captures[i].Order).Append(':').Append(arm.Captures[i].Label)
                .Append('@').Append(arm.Captures[i].Call);
        }
        Console.WriteLine(captures.ToString());
    }

    private static bool KeepsGrowingByThirds(ArmRun arm)
        => arm.Thirds[1].Full > arm.Thirds[0].Full
            && arm.Thirds[2].Full > arm.Thirds[1].Full;

    private static int CountNonterminalTargets(EmlTarget[] targets, bool[] trainMask, bool train)
    {
        int count = 0;
        for (int i = 0; i < targets.Length; i++)
            if (!IsTerminalTarget(targets[i]) && trainMask[i] == train) count++;
        return count;
    }

    private static string FormatArm(ChunkMicroAssayArms arm)
        => arm switch
        {
            ChunkMicroAssayArms.CapturedChunkReuse => "captured_chunk_reuse",
            ChunkMicroAssayArms.TerminalRotatedChunkNull => "terminal_rotated_chunk_null",
            ChunkMicroAssayArms.NoChunkReuse => "no_chunk_reuse",
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };

    private static string FormatPlane(ChunkMicroAssayPlanes plane)
        => plane switch
        {
            ChunkMicroAssayPlanes.AllTargets => "all_targets",
            ChunkMicroAssayPlanes.Holdout => "holdout",
            _ => throw new ArgumentOutOfRangeException(nameof(plane)),
        };
}
