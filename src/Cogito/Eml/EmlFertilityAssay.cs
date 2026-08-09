namespace Cogito;

using System.Text;

internal static class EmlFertilityAssay
{
    private const int MaxBranchSteps = 1_000_000;
    private const int MaxRootSearchActions = 4_096;

    private readonly record struct BranchResult(
        string Name,
        string Directory,
        ReplayCalc Replay,
        long Endpoint,
        byte[] Checkpoint,
        HashSet<EmlCert> Certificates,
        List<EmlCert> RootOpened,
        string RootFingerprint,
        long RootEvaluatorCalls,
        EmlActionSchedule Schedule);

    public static int Run(ulong seed, long evaluatorCalls, int actionBatch, int strideBytes,
        int signatureDigits, EmlGenerationConfig generation)
    {
        if (actionBatch != 1)
            throw new ArgumentException($"fertility assay requires ActionsPerStep==1; received {actionBatch}", nameof(actionBatch));
        if (evaluatorCalls <= 0)
            throw new ArgumentOutOfRangeException(nameof(evaluatorCalls), evaluatorCalls, "fertility evaluator budget must be positive");

        CortexEmlCurriculum curriculum = new()
        {
            SignatureDigits = signatureDigits,
            IntakeBatch = 1,
            Actions = EmlActionSelections.RoundRobin,
            Generation = generation,
        };
        ReplayCalc baseReplay = ReplayCalc.Mount(seed, curriculum);
        baseReplay.ConfigureFertility(EmlFertilityInterventions.Hold, 0);
        Cortex baseCortex = new(CreateConfig("eml-fertility", seed, 1, strideBytes, curriculum, baseReplay));
        int baseExit = baseCortex.Run();
        if (baseExit != 0) return baseExit;

        string initialDirectory = baseCortex.CurrentRun.Dir;
        byte[] initialCheckpoint = File.ReadAllBytes(Path.Combine(initialDirectory, Checkpoint.FileName));
        (string baseDirectory, byte[] baseCheckpoint, HashSet<EmlCert> baseline, long baseEndpoint, int rootSearchActions) =
            FindFertileRoot(initialDirectory, initialCheckpoint, CaptureCertificates(baseReplay.Sieve),
                baseReplay.EvaluatorCalls, seed, strideBytes, curriculum);
        File.WriteAllBytes(Path.Combine(initialDirectory, "fertility_base.bin"), baseCheckpoint);
        long requestedEndpoint = checked(baseEndpoint + evaluatorCalls);

        WritePlan(initialDirectory, seed, baseEndpoint, requestedEndpoint, rootSearchActions);
        BranchResult identityA = RunBranch(baseDirectory, Path.Combine(initialDirectory, "identity-a"), seed, requestedEndpoint, strideBytes,
            curriculum, EmlFertilityInterventions.Actual, baseCheckpoint);
        long canonicalEndpoint = identityA.Endpoint;
        BranchResult identityB = RunBranch(baseDirectory, Path.Combine(initialDirectory, "identity-b"), seed, canonicalEndpoint, strideBytes,
            curriculum, EmlFertilityInterventions.Actual, baseCheckpoint);
        BranchResult actual = RunBranch(baseDirectory, Path.Combine(initialDirectory, "actual"), seed, canonicalEndpoint, strideBytes,
            curriculum, EmlFertilityInterventions.Actual, baseCheckpoint);
        BranchResult shadow = RunBranch(baseDirectory, Path.Combine(initialDirectory, "no-op-shadow"), seed, canonicalEndpoint, strideBytes,
            curriculum, EmlFertilityInterventions.Shadow, baseCheckpoint);

        bool endpointsExact = identityB.Endpoint == canonicalEndpoint && actual.Endpoint == canonicalEndpoint && shadow.Endpoint == canonicalEndpoint;
        bool identityCheckpointExact = identityA.Checkpoint.AsSpan().SequenceEqual(identityB.Checkpoint);
        bool baseForkExact = VerifyForkImages(baseCheckpoint, identityA.Directory, identityB.Directory, actual.Directory, shadow.Directory);
        bool rootExecutionExact = actual.RootFingerprint == shadow.RootFingerprint
            && actual.RootEvaluatorCalls == shadow.RootEvaluatorCalls;
        bool fertileRootFound = actual.RootOpened.Count > 0 && shadow.RootOpened.Count > 0;
        bool scheduleExact = actual.Schedule.Decisions == shadow.Schedule.Decisions
            && actual.Schedule.Cursor == shadow.Schedule.Cursor;
        bool proposalRngExact = actual.Schedule.Rng == shadow.Schedule.Rng;

        HashSet<EmlCert> identityANew = CapturePostRoot(identityA, baseline);
        HashSet<EmlCert> identityBNew = CapturePostRoot(identityB, baseline);
        HashSet<EmlCert> actualNew = CapturePostRoot(actual, baseline);
        HashSet<EmlCert> shadowNew = CapturePostRoot(shadow, baseline);
        (HashSet<EmlCert> IdentityAExclusive, HashSet<EmlCert> IdentityBExclusive) = CancelShared(identityANew, identityBNew);
        (HashSet<EmlCert> ActualExclusive, HashSet<EmlCert> ShadowExclusive) = CancelShared(actualNew, shadowNew);

        EmlCert positiveSentinel = new('E', new EmlSig(long.MinValue, 17, long.MaxValue, -17), 0, 0);
        HashSet<EmlCert> positiveLeft = new() { positiveSentinel };
        HashSet<EmlCert> positiveRight = new();
        (HashSet<EmlCert> PositiveExclusive, HashSet<EmlCert> PositiveShadowExclusive) = CancelShared(positiveLeft, positiveRight);
        bool positiveControl = PositiveExclusive.SetEquals(positiveLeft) && PositiveShadowExclusive.Count == 0;
        HashSet<EmlCert> rootExclusion = new() { positiveSentinel };
        List<EmlCert> rootControl = new() { positiveSentinel };
        RemoveRootCertificates(rootExclusion, rootControl);
        bool rootExclusionControl = rootExclusion.Count == 0;

        WriteCertificateRows(initialDirectory, IdentityAExclusive, IdentityBExclusive, ActualExclusive, ShadowExclusive);
        WriteSummary(initialDirectory, baseCheckpoint.Length, baseEndpoint, requestedEndpoint, canonicalEndpoint,
            baseForkExact, endpointsExact, identityCheckpointExact, positiveControl,
            rootExecutionExact, scheduleExact, proposalRngExact, rootExclusionControl, fertileRootFound, rootSearchActions,
            identityA, identityB, actual, shadow,
            IdentityAExclusive, IdentityBExclusive, ActualExclusive, ShadowExclusive);

        bool passed = baseForkExact && endpointsExact && identityCheckpointExact && positiveControl
            && rootExecutionExact && scheduleExact && rootExclusionControl
            && fertileRootFound
            && IdentityAExclusive.Count == 0 && IdentityBExclusive.Count == 0;
        Console.WriteLine($"  fertility assay → {Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(initialDirectory, "fertility.tsv"))}");
        Console.WriteLine($"  base {baseCheckpoint.Length:N0}B · clock {baseEndpoint:N0} → {canonicalEndpoint:N0} · fork {(baseForkExact ? "byte-exact" : "DIVERGED")}");
        Console.WriteLine($"  fertile root found after {rootSearchActions:N0} scout actions · opened {actual.RootOpened.Count}/{shadow.RootOpened.Count}");
        Console.WriteLine($"  identity exclusive {IdentityAExclusive.Count}/{IdentityBExclusive.Count} · checkpoint {(identityCheckpointExact ? "byte-exact" : "DIVERGED")}");
        Console.WriteLine($"  root execution {(rootExecutionExact ? "exact" : "DIVERGED")} · selection schedule {(scheduleExact ? "exact" : "DIVERGED")} · proposal RNG {(proposalRngExact ? "exact" : "causally diverged")}");
        Console.WriteLine($"  actual/no-op post-root exclusive {ActualExclusive.Count}/{ShadowExclusive.Count} · exact {CountExact(ActualExclusive)}/{CountExact(ShadowExclusive)} · theorem {CountTheorems(ActualExclusive)}/{CountTheorems(ShadowExclusive)}");
        Console.WriteLine($"  positive comparator control {(positiveControl ? "PASS" : "FAIL")}");
        return passed ? 0 : 1;
    }

    private static (string Directory, byte[] Checkpoint, HashSet<EmlCert> Certificates, long Endpoint, int Actions)
        FindFertileRoot(string initialDirectory, byte[] initialCheckpoint, HashSet<EmlCert> initialCertificates,
            long initialEndpoint, ulong seed, int strideBytes, CortexEmlCurriculum curriculum)
    {
        string baseDirectory = initialDirectory;
        byte[] baseCheckpoint = initialCheckpoint;
        HashSet<EmlCert> baseline = initialCertificates;
        long baseEndpoint = initialEndpoint;
        for (int action = 1; action <= MaxRootSearchActions; action++)
        {
            BranchResult scout = RunBranch(baseDirectory, Path.Combine(initialDirectory, $"root-scout-{action:D4}"), seed,
                checked(baseEndpoint + 1), strideBytes, curriculum, EmlFertilityInterventions.Actual, baseCheckpoint);
            if (scout.RootOpened.Count > 0)
                return (baseDirectory, baseCheckpoint, baseline, baseEndpoint, action);
            baseDirectory = scout.Directory;
            baseCheckpoint = scout.Checkpoint;
            baseline = scout.Certificates;
            baseEndpoint = scout.Endpoint;
        }
        throw new InvalidOperationException($"no theorem-store-changing root appeared within {MaxRootSearchActions:N0} actions");
    }

    private static CortexConfig CreateConfig(string runName, ulong seed, int steps, int strideBytes,
        CortexEmlCurriculum curriculum, ReplayCalc dream)
        => new()
        {
            RunName = runName,
            Steps = steps,
            Seed = seed,
            ActionsPerStep = 1,
            Stride = new CortexStrideConfig { ReinduceBytes = strideBytes },
            Curriculum = curriculum,
            RuntimeCurriculum = dream,
            Tools = ReplayCalc.CreateActionTools(),
            ActionPolicies = ReplayCalc.CreateActionPolicies(),
            Rewards = ReplayCalc.CreateRewards(),
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0 },
        };

    private static BranchResult RunBranch(string baseDirectory, string branchDirectory, ulong seed, long stopAt, int strideBytes,
        CortexEmlCurriculum curriculum, EmlFertilityInterventions intervention, byte[] baseCheckpoint)
    {
        CopyBaseArtifacts(baseDirectory, branchDirectory);
        byte[] copiedCheckpoint = File.ReadAllBytes(Path.Combine(branchDirectory, Checkpoint.FileName));
        if (!copiedCheckpoint.AsSpan().SequenceEqual(baseCheckpoint))
            throw new InvalidDataException($"fertility branch {Path.GetFileName(branchDirectory)} did not begin from the byte-exact base checkpoint");
        File.WriteAllBytes(Path.Combine(branchDirectory, "fork_base.bin"), copiedCheckpoint);

        ReplayCalc dream = ReplayCalc.Mount(seed, curriculum);
        dream.ConfigureFertility(intervention, stopAt);
        Cortex cortex = new(CreateConfig("eml-fertility", seed, MaxBranchSteps, strideBytes, curriculum, dream));
        int exit = cortex.Resume(branchDirectory, MaxBranchSteps, forkCurriculum: true);
        if (exit != 0) throw new InvalidOperationException($"fertility branch {Path.GetFileName(branchDirectory)} exited {exit}");
        byte[] checkpoint = File.ReadAllBytes(Path.Combine(branchDirectory, Checkpoint.FileName));
        List<EmlCert> rootOpened = new(dream.FertilityRootOpened.Count);
        for (int i = 0; i < dream.FertilityRootOpened.Count; i++) rootOpened.Add(dream.FertilityRootOpened[i]);
        return new BranchResult(Path.GetFileName(branchDirectory), branchDirectory, dream, dream.EvaluatorCalls, checkpoint,
            CaptureCertificates(dream.Sieve), rootOpened, dream.FertilityRootFingerprint,
            dream.FertilityRootEvaluatorCalls, dream.FertilitySchedule);
    }

    private static void CopyBaseArtifacts(string baseDirectory, string branchDirectory)
    {
        Directory.CreateDirectory(branchDirectory);
        string[] files = Directory.GetFiles(baseDirectory);
        Array.Sort(files, StringComparer.Ordinal);
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (name.StartsWith("fertility_", StringComparison.Ordinal)) continue;
            File.Copy(files[i], Path.Combine(branchDirectory, name));
        }
    }

    private static bool VerifyForkImages(byte[] baseCheckpoint, params string[] branchDirectories)
    {
        for (int i = 0; i < branchDirectories.Length; i++)
        {
            string forkPath = Path.Combine(branchDirectories[i], "fork_base.bin");
            if (!File.ReadAllBytes(forkPath).AsSpan().SequenceEqual(baseCheckpoint)) return false;
        }
        return true;
    }

    private static HashSet<EmlCert> CaptureCertificates(EmlSieve sieve)
    {
        HashSet<EmlCert> certificates = new();
        foreach (EmlCert certificate in sieve.Cas.Keys) certificates.Add(certificate);
        return certificates;
    }

    private static HashSet<EmlCert> CapturePostRoot(in BranchResult branch, HashSet<EmlCert> baseline)
    {
        HashSet<EmlCert> result = new(branch.Certificates);
        result.ExceptWith(baseline);
        RemoveRootCertificates(result, branch.RootOpened);
        return result;
    }

    private static void RemoveRootCertificates(HashSet<EmlCert> certificates, List<EmlCert> rootOpened)
    {
        for (int i = 0; i < rootOpened.Count; i++) certificates.Remove(rootOpened[i]);
    }

    private static (HashSet<EmlCert> Left, HashSet<EmlCert> Right) CancelShared(HashSet<EmlCert> left, HashSet<EmlCert> right)
    {
        HashSet<EmlCert> leftExclusive = new(left);
        HashSet<EmlCert> rightExclusive = new(right);
        leftExclusive.ExceptWith(right);
        rightExclusive.ExceptWith(left);
        return (leftExclusive, rightExclusive);
    }

    private static int CountExact(HashSet<EmlCert> certificates)
    {
        int count = 0;
        foreach (EmlCert certificate in certificates) if (certificate.Grade == 'E') count++;
        return count;
    }

    private static int CountTheorems(HashSet<EmlCert> certificates)
    {
        int count = 0;
        foreach (EmlCert certificate in certificates) if (certificate.Grade is 'E' or 'A') count++;
        return count;
    }

    private static void WritePlan(string baseDirectory, ulong seed, long baseEndpoint, long requestedEndpoint,
        int rootSearchActions)
    {
        StringBuilder report = new();
        report.AppendLine("branch\troot\tcontinuation\tstop_clock");
        report.AppendLine($"identity-a\tactual\tdeterministic\t>={requestedEndpoint}");
        report.AppendLine("identity-b\tactual\tdeterministic\tidentity-a endpoint");
        report.AppendLine("actual\tactual\tdeterministic\tidentity-a endpoint");
        report.AppendLine("no-op-shadow\texecute+rollback CAS/tape\tdeterministic\tidentity-a endpoint");
        report.AppendLine($"# seed\t{seed:X16}");
        report.AppendLine($"# base_clock\t{baseEndpoint}");
        report.AppendLine($"# root_search_actions\t{rootSearchActions}");
        File.WriteAllText(Path.Combine(baseDirectory, "fertility_plan.tsv"), report.ToString());
    }

    private static void WriteCertificateRows(string baseDirectory,
        HashSet<EmlCert> identityA, HashSet<EmlCert> identityB,
        HashSet<EmlCert> actual, HashSet<EmlCert> shadow)
    {
        List<(string Comparison, string Side, EmlCert Certificate)> rows = new();
        AddCertificateRows(rows, "identity", "a", identityA);
        AddCertificateRows(rows, "identity", "b", identityB);
        AddCertificateRows(rows, "fertility", "actual", actual);
        AddCertificateRows(rows, "fertility", "no-op-shadow", shadow);
        rows.Sort((left, right) =>
        {
            int comparison = string.CompareOrdinal(left.Comparison, right.Comparison);
            if (comparison != 0) return comparison;
            int side = string.CompareOrdinal(left.Side, right.Side);
            return side != 0 ? side : string.CompareOrdinal(left.Certificate.Hex(), right.Certificate.Hex());
        });
        StringBuilder report = new();
        report.AppendLine("comparison\tside\ttier\tgrade\tcertificate");
        for (int i = 0; i < rows.Count; i++)
        {
            EmlCert certificate = rows[i].Certificate;
            string tier = certificate.Grade == 'E' ? "exact" : certificate.Grade == 'A' ? "theorem" : "non-theorem";
            report.Append(rows[i].Comparison).Append('\t').Append(rows[i].Side).Append('\t')
                .Append(tier).Append('\t').Append(certificate.Grade).Append('\t').AppendLine(certificate.Hex());
        }
        File.WriteAllText(Path.Combine(baseDirectory, "fertility_certificates.tsv"), report.ToString());
    }

    private static void AddCertificateRows(List<(string Comparison, string Side, EmlCert Certificate)> rows,
        string comparison, string side, HashSet<EmlCert> certificates)
    {
        foreach (EmlCert certificate in certificates) rows.Add((comparison, side, certificate));
    }

    private static void WriteSummary(string baseDirectory, int baseBytes, long baseEndpoint, long requestedEndpoint,
        long canonicalEndpoint, bool baseForkExact, bool endpointsExact, bool identityCheckpointExact, bool positiveControl,
        bool rootExecutionExact, bool scheduleExact, bool proposalRngExact, bool rootExclusionControl, bool fertileRootFound,
        int rootSearchActions,
        in BranchResult identityA, in BranchResult identityB, in BranchResult actual, in BranchResult shadow,
        HashSet<EmlCert> identityAExclusive, HashSet<EmlCert> identityBExclusive,
        HashSet<EmlCert> actualExclusive, HashSet<EmlCert> shadowExclusive)
    {
        StringBuilder report = new();
        report.AppendLine("measure\tvalue");
        report.AppendLine($"base_checkpoint_bytes\t{baseBytes}");
        report.AppendLine($"base_evaluator_clock\t{baseEndpoint}");
        report.AppendLine($"requested_evaluator_endpoint\t{requestedEndpoint}");
        report.AppendLine($"canonical_evaluator_endpoint\t{canonicalEndpoint}");
        report.AppendLine($"base_fork_byte_exact\t{(baseForkExact ? 1 : 0)}");
        report.AppendLine($"all_endpoints_exact\t{(endpointsExact ? 1 : 0)}");
        report.AppendLine($"identity_checkpoint_byte_exact\t{(identityCheckpointExact ? 1 : 0)}");
        report.AppendLine($"positive_comparator_control\t{(positiveControl ? 1 : 0)}");
        report.AppendLine($"positive_root_exclusion_control\t{(rootExclusionControl ? 1 : 0)}");
        report.AppendLine($"fertile_root_found\t{(fertileRootFound ? 1 : 0)}");
        report.AppendLine($"root_search_actions\t{rootSearchActions}");
        report.AppendLine($"root_execution_exact\t{(rootExecutionExact ? 1 : 0)}");
        report.AppendLine($"selection_schedule_exact\t{(scheduleExact ? 1 : 0)}");
        report.AppendLine($"proposal_rng_exact\t{(proposalRngExact ? 1 : 0)}");
        AppendBranchRows(report, in identityA, identityAExclusive);
        AppendBranchRows(report, in identityB, identityBExclusive);
        AppendBranchRows(report, in actual, actualExclusive);
        AppendBranchRows(report, in shadow, shadowExclusive);
        string text = report.ToString();
        File.WriteAllText(Path.Combine(baseDirectory, "fertility.tsv"), text);
        File.WriteAllText(Path.Combine(baseDirectory, "fertility_report.txt"), text.Replace('\t', ' '));
    }

    private static void AppendBranchRows(StringBuilder report, in BranchResult branch, HashSet<EmlCert> exclusive)
    {
        report.AppendLine($"{branch.Name}.endpoint\t{branch.Endpoint}");
        report.AppendLine($"{branch.Name}.root_opened\t{branch.RootOpened.Count}");
        report.AppendLine($"{branch.Name}.root_evaluator_calls\t{branch.RootEvaluatorCalls}");
        report.AppendLine($"{branch.Name}.root_fingerprint\t{branch.RootFingerprint}");
        report.AppendLine($"{branch.Name}.schedule_rng\t{branch.Schedule.Rng:X16}");
        report.AppendLine($"{branch.Name}.schedule_decisions\t{branch.Schedule.Decisions}");
        report.AppendLine($"{branch.Name}.schedule_cursor\t{branch.Schedule.Cursor}");
        report.AppendLine($"{branch.Name}.post_root_exclusive\t{exclusive.Count}");
        report.AppendLine($"{branch.Name}.post_root_exact_exclusive\t{CountExact(exclusive)}");
        report.AppendLine($"{branch.Name}.post_root_theorem_exclusive\t{CountTheorems(exclusive)}");
    }
}
