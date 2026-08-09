namespace Cogito;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cogito.Exec;
using Cogito.Grammar;
using Cogito.Induct;

public readonly record struct PolicyReadoutAssayResult(
    string RunDirectory,
    int ReferenceRows,
    int ProbeRows,
    double TypedRegret,
    double ShuffledRegret,
    double RoundRobinRegret,
    double TypedAccuracy,
    int TypedActions,
    int ReadoutMisses,
    int CacheMismatches,
    int ColdMisses,
    int Refills,
    int WarmHits,
    int InvariantRows,
    int InvariantFailures,
    bool NullSeparated,
    bool OverlapRejected,
    bool Passed);

/// A matched-fork battery for the production grammar readout. Weft supplies deterministic actions and measured
/// consequences; typed, shuffled, and round-robin labels become ordinary published grammar records, and every
/// selection crosses GrammarPolicyReadout plus its revision-stamped cache.
public static class PolicyReadoutAssay
{
    private const int ActionCount = 4;
    private const int FeatureCount = 14;
    private static readonly CortexPolicyID PolicyID = new("weft.discovery.action");

    private enum Treatments : byte { Typed, Shuffled, RoundRobin }

    private readonly record struct AssayRow(
        int Lineage,
        int Checkpoint,
        double[] Features,
        long[] Utilities);

    private readonly record struct PolicyScore(
        double Regret,
        double Accuracy,
        int Actions,
        int ReadoutMisses,
        int CacheMismatches,
        int ColdMisses,
        int Refills,
        int WarmHits,
        int InvariantRows,
        int InvariantFailures,
        int[] Decisions);

    private readonly record struct NullSeparationReceipt(
        int RequiredDivergences,
        int TypedShuffledDivergences,
        int TypedRoundRobinDivergences,
        bool RegretSeparated,
        bool AccuracySeparated,
        bool Passed);

    private readonly record struct CacheWorkReceipt(
        int Rows,
        int Sweeps,
        long SweptEntries,
        long CanonicalComputations,
        long WallMilliseconds);

    // Static comparison data only. The retired tree learner is never reconstructed or executed.
    private readonly record struct TreeEraBaseline(
        double TypedRegret,
        double ShuffledRegret,
        double LiveTypedUtility,
        double LiveRoundRobinUtility);

    private static readonly TreeEraBaseline HistoricalTree = new(
        73_450_962.5,
        843_775_045.3,
        13_203_067_953.1,
        11_437_435_265.6);

    public static PolicyReadoutAssayResult Run(
        ulong seed,
        int lineages = 32,
        int checkpoints = 10,
        int stride = 4,
        int horizon = 8)
    {
        if (lineages < 8) throw new ArgumentOutOfRangeException(nameof(lineages));
        if (checkpoints <= 0) throw new ArgumentOutOfRangeException(nameof(checkpoints));
        if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));
        if (horizon <= 0) throw new ArgumentOutOfRangeException(nameof(horizon));

        Stopwatch assayClock = Stopwatch.StartNew();
        Console.WriteLine($"  policy readout assay · start lineages={lineages} checkpoints={checkpoints} horizon={horizon} rows={lineages * checkpoints}");
        Cogito.Run run = Cogito.Run.New("policy-readout");
        List<AssayRow> rows = new(lineages * checkpoints);
        RePairResult emptyGrammar = new([], [], Mbits.Zero, 256);
        for (int lineage = 0; lineage < lineages; lineage++)
            AppendLineageRows(seed, lineage, checkpoints, stride, horizon, in emptyGrammar, rows);

        int referenceLineages = lineages * 3 / 4;
        InstallRevision typed = Publish(rows, Treatments.Typed, revision: 1);
        InstallRevision shuffled = Publish(rows, Treatments.Shuffled, revision: 2);
        InstallRevision roundRobin = Publish(rows, Treatments.RoundRobin, revision: 3);
        Console.WriteLine($"  policy readout assay · published rows={rows.Count} wall={assayClock.ElapsedMilliseconds}ms");
        PolicyScore typedScore = ScoreInstallRevision(rows, referenceLineages, in typed, verifyCanonicalFormatting: true);
        PolicyScore shuffledScore = ScoreInstallRevision(rows, referenceLineages, in shuffled, verifyCanonicalFormatting: false);
        PolicyScore roundRobinScore = ScoreInstallRevision(rows, referenceLineages, in roundRobin, verifyCanonicalFormatting: false);
        int readoutMisses = typedScore.ReadoutMisses + shuffledScore.ReadoutMisses + roundRobinScore.ReadoutMisses;
        int cacheMismatches = typedScore.CacheMismatches + shuffledScore.CacheMismatches + roundRobinScore.CacheMismatches;
        int coldMisses = typedScore.ColdMisses + shuffledScore.ColdMisses + roundRobinScore.ColdMisses;
        int refills = typedScore.Refills + shuffledScore.Refills + roundRobinScore.Refills;
        int warmHits = typedScore.WarmHits + shuffledScore.WarmHits + roundRobinScore.WarmHits;
        NullSeparationReceipt separation = MeasureNullSeparation(in typedScore, in shuffledScore, in roundRobinScore);
        NullSeparationReceipt overlap = MeasureNullSeparation(in typedScore, in typedScore, in typedScore);
        bool overlapRejected = !overlap.Passed;
        bool passed = readoutMisses == 0 && cacheMismatches == 0 && typedScore.InvariantFailures == 0
            && separation.Passed && overlapRejected;

        CacheWorkReceipt work = WriteRows(run, rows, referenceLineages, in typed, in shuffled, in roundRobin);
        WriteSummary(run, referenceLineages * checkpoints, rows.Count - referenceLineages * checkpoints,
            in typed, in shuffled, in roundRobin, in typedScore, in shuffledScore, in roundRobinScore,
            in separation, overlapRejected, passed, in work);
        WriteHistoricalBaseline(run);

        Console.WriteLine($"  policy readout battery · {(passed ? "PASS" : "FAIL")} · typed regret {typedScore.Regret:F1} · shuffled {shuffledScore.Regret:F1} · round-robin {roundRobinScore.Regret:F1} · nulls {(separation.Passed ? "separated" : "INSUFFICIENT")} · overlap fixture {(overlapRejected ? "rejected" : "ACCEPTED")}");
        Console.WriteLine($"  cache turnstile · cold-miss {coldMisses} · refill {refills} · warm-hit {warmHits} · unresolved {readoutMisses} · violations {cacheMismatches} · invariant {typedScore.InvariantFailures}/{typedScore.InvariantRows}");
        Console.WriteLine($"  cache work · rows={work.Rows} sweeps={work.Sweeps} swept-entries={work.SweptEntries} canonical-computations={work.CanonicalComputations} wall={work.WallMilliseconds}ms");
        Console.WriteLine($"  policy readout assay · complete wall={assayClock.ElapsedMilliseconds}ms");
        Console.WriteLine($"  run → {run.Dir}");
        return new PolicyReadoutAssayResult(
            run.Dir, referenceLineages * checkpoints, rows.Count - referenceLineages * checkpoints,
            typedScore.Regret, shuffledScore.Regret, roundRobinScore.Regret,
            typedScore.Accuracy, typedScore.Actions, readoutMisses, cacheMismatches,
            coldMisses, refills, warmHits,
            typedScore.InvariantRows, typedScore.InvariantFailures,
            separation.Passed, overlapRejected, passed);
    }

    private static InstallRevision Publish(
        List<AssayRow> rows,
        Treatments treatment,
        ulong revision)
    {
        using Tape tape = new();
        Journal journal = new();
        int step = 0;
        for (int index = 0; index < rows.Count; index++)
        {
            AssayRow row = rows[index];
            MetricSample[] features = ReadMetricSamples(row.Features);
            int action = ReadTreatmentAction(in row, treatment);
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, PolicyID, action, features, ActionCount);
        }
        RePairResult result = Engine.Induce(tape, 1).Result;
        return InstallRevision.FromRePair(new GrammarRevisionID(revision), GrammarRevisionID.Zero, in result);
    }

    private static int ReadTreatmentAction(in AssayRow row, Treatments treatment)
    {
        int oracle = FindBestAction(row.Utilities);
        return treatment switch
        {
            Treatments.Typed => oracle,
            Treatments.Shuffled => (oracle + 1 + (row.Lineage * 17 + row.Checkpoint * 31) % (ActionCount - 1)) % ActionCount,
            Treatments.RoundRobin => (row.Lineage + row.Checkpoint) % ActionCount,
            _ => throw new ArgumentOutOfRangeException(nameof(treatment)),
        };
    }

    private static PolicyScore ScoreInstallRevision(
        List<AssayRow> rows,
        int referenceLineages,
        in InstallRevision publication,
        bool verifyCanonicalFormatting)
    {
        long regret = 0;
        int correct = 0;
        int count = 0;
        int misses = 0;
        int cacheMismatches = 0;
        int coldMisses = 0;
        int refills = 0;
        int warmHits = 0;
        int invariantRows = 0;
        int invariantFailures = 0;
        bool[] actions = new bool[ActionCount];
        List<int> decisions = new();
        Stopwatch phaseClock = Stopwatch.StartNew();
        int processed = 0;
        Console.WriteLine($"  policy readout assay · score revision={publication.Revision.Value} source-rows={rows.Count} reference-lineages={referenceLineages}");
        for (int index = 0; index < rows.Count; index++)
        {
            AssayRow row = rows[index];
            if (row.Lineage < referenceLineages) continue;
            MetricSample[] features = ReadMetricSamples(row.Features);
            PolicyReadoutCache cache = new();
            PolicyReadoutCacheReceipt cold = GrammarPolicyReadout.ReadCache(
                in publication, PolicyID, features, ActionCount, 0, cache);
            if (cold.Outcome == PolicyReadoutCacheOutcomes.Miss) coldMisses++;
            else cacheMismatches++;
            GrammarPolicyContextKey context = cold.Context;
            PolicyReadoutCacheReceipt refill = GrammarPolicyReadout.Refill(
                in publication, PolicyID, features, ActionCount, 0,
                new GrammarContinuationQuota(8), cache, in context, default, 0, out _);
            if (refill.Outcome == PolicyReadoutCacheOutcomes.Refilled) refills++;
            else
            {
                misses++;
                cacheMismatches++;
            }
            PolicyReadoutCacheReceipt warm = GrammarPolicyReadout.ReadCache(
                in publication, PolicyID, features, ActionCount, 0, cache);
            if (warm.Outcome == PolicyReadoutCacheOutcomes.Hit && warm.Decision == refill.Decision) warmHits++;
            else cacheMismatches++;
            int action = refill.HasDecision ? refill.Decision.Action : 0;
            int oracle = FindBestAction(row.Utilities);
            regret = checked(regret + row.Utilities[oracle] - row.Utilities[action]);
            if (action == oracle) correct++;
            actions[action] = true;
            decisions.Add(action);
            count++;
            processed++;
            if (processed % 16 == 0)
                Console.WriteLine($"  policy readout assay · score revision={publication.Revision.Value} processed={processed} wall={phaseClock.ElapsedMilliseconds}ms");

            if (!verifyCanonicalFormatting) continue;
            MetricSample[] formatted = ReadMetricSamples(ReadFormattedFeatures(row.Features));
            PolicyReadoutCacheReceipt formattedHit = GrammarPolicyReadout.ReadCache(
                in publication, PolicyID, formatted, ActionCount, 0, cache);
            invariantRows++;
            if (formattedHit.Outcome != PolicyReadoutCacheOutcomes.Hit || formattedHit.Decision.Action != action)
                invariantFailures++;
        }
        int actionCount = 0;
        for (int action = 0; action < actions.Length; action++) if (actions[action]) actionCount++;
        return new PolicyScore(
            count == 0 ? double.NaN : (double)regret / count,
            count == 0 ? double.NaN : (double)correct / count,
            actionCount, misses, cacheMismatches, coldMisses, refills, warmHits,
            invariantRows, invariantFailures, [.. decisions]);
    }

    private static PolicyReadoutCacheReceipt ReadPublishedAction(
        in InstallRevision publication,
        ReadOnlySpan<MetricSample> features,
        PolicyReadoutCache cache)
    {
        PolicyReadoutCacheReceipt receipt = GrammarPolicyReadout.ReadCache(
            in publication, PolicyID, features, ActionCount, 0, cache);
        if (receipt.HasDecision) return receipt;
        GrammarPolicyContextKey context = receipt.Context;
        return GrammarPolicyReadout.Refill(
            in publication, PolicyID, features, ActionCount, 0,
            new GrammarContinuationQuota(8), cache, in context, default, 0, out _);
    }

    private static NullSeparationReceipt MeasureNullSeparation(
        in PolicyScore typed,
        in PolicyScore shuffled,
        in PolicyScore roundRobin)
    {
        if (typed.Decisions.Length != shuffled.Decisions.Length || typed.Decisions.Length != roundRobin.Decisions.Length)
            throw new InvalidOperationException("policy null arms must score the same canonical probe rows");
        int typedShuffled = 0;
        int typedRoundRobin = 0;
        for (int index = 0; index < typed.Decisions.Length; index++)
        {
            if (typed.Decisions[index] != shuffled.Decisions[index]) typedShuffled++;
            if (typed.Decisions[index] != roundRobin.Decisions[index]) typedRoundRobin++;
        }
        int required = Math.Max(1, (typed.Decisions.Length + 3) / 4);
        bool regretSeparated = typed.Regret < shuffled.Regret && typed.Regret < roundRobin.Regret;
        bool accuracySeparated = typed.Accuracy > shuffled.Accuracy && typed.Accuracy > roundRobin.Accuracy;
        bool passed = regretSeparated && accuracySeparated
            && typedShuffled >= required && typedRoundRobin >= required;
        return new NullSeparationReceipt(required, typedShuffled, typedRoundRobin, regretSeparated, accuracySeparated, passed);
    }

    private static void AppendLineageRows(
        ulong seed,
        int lineage,
        int checkpoints,
        int stride,
        int horizon,
        in RePairResult grammar,
        List<AssayRow> rows)
    {
        CortexWeftCurriculum config = CreateConfig(lineage);
        using WeftCurriculum world = config.Mount(seed + (ulong)lineage * 0x9E3779B97F4A7C15UL);
        using Tape tape = new();
        Journal journal = new();
        world.Seed(tape, journal);
        WeftDiscoveryState previous = world.CaptureDiscoveryState();
        for (int checkpoint = 0; checkpoint < checkpoints; checkpoint++)
        {
            for (int advance = 0; advance < stride; advance++)
            {
                int step = checkpoint * stride + advance;
                WeftDiscoveryActions action = SelectExplorationAction(lineage, step);
                world.ExecuteDiscoveryAction(action, in grammar, tape, journal, step, slot: lineage & 7);
                world.InduceDiscoveryKnots();
            }
            WeftDiscoveryState state = world.CaptureDiscoveryState();
            byte[] checkpointState = world.CaptureCheckpointState();
            long[] utilities = new long[ActionCount];
            for (int action = 0; action < ActionCount; action++)
                utilities[action] = MeasureFork(world, checkpointState, (WeftDiscoveryActions)action,
                    checkpoint * stride + stride, lineage, horizon, in grammar);
            rows.Add(new AssayRow(lineage, checkpoint, ReadFeatures(in state, in previous), utilities));
            previous = state;
        }
    }

    private static long MeasureFork(
        WeftCurriculum source,
        byte[] checkpointState,
        WeftDiscoveryActions firstAction,
        int step,
        int lineage,
        int horizon,
        in RePairResult grammar)
    {
        using WeftCurriculum fork = source.CreateCheckpointFork(checkpointState);
        using Tape tape = new();
        Journal journal = new();
        WeftDiscoveryState before = fork.CaptureDiscoveryState();
        fork.ExecuteDiscoveryAction(firstAction, in grammar, tape, journal, step, slot: lineage & 7);
        fork.InduceDiscoveryKnots();
        for (int offset = 1; offset < horizon; offset++)
        {
            WeftDiscoveryActions action = (WeftDiscoveryActions)((step + offset + lineage) % ActionCount);
            fork.ExecuteDiscoveryAction(action, in grammar, tape, journal, step + offset, slot: (lineage + offset) & 7);
            fork.InduceDiscoveryKnots();
        }
        WeftDiscoveryState after = fork.CaptureDiscoveryState();
        return ScoreDelta(new WeftDiscoveryDelta(in before, in after));
    }

    private static double[] ReadFeatures(in WeftDiscoveryState state, in WeftDiscoveryState previous)
    {
        double members = Math.Max(1, state.BehaviorMembers);
        double classes = Math.Max(1, state.BehaviorClasses);
        return
        [
            state.BehaviorClasses,
            state.BehaviorMembers,
            state.BehaviorClasses / members,
            state.BehaviorMembers / classes,
            state.AdmittedKnots,
            state.ActiveKnots,
            state.PendingKnots,
            state.RejectedKnots,
            state.MdlSavingsMbits,
            state.Executions,
            state.ExecutionFuel,
            state.CandidateLength,
            state.BehaviorClasses - previous.BehaviorClasses,
            state.MdlSavingsMbits - previous.MdlSavingsMbits,
        ];
    }

    private static MetricSample[] ReadMetricSamples(double[] features)
    {
        MetricSample[] samples = new MetricSample[FeatureCount];
        for (int index = 0; index < samples.Length; index++)
            samples[index] = new MetricSample(new MetricID((ushort)(640 + index)), NumericValue.FromF64(features[index]));
        return samples;
    }

    private static double[] ReadFormattedFeatures(double[] features)
    {
        double[] result = new double[features.Length];
        for (int index = 0; index < features.Length; index++)
            result[index] = double.Parse(features[index].ToString("E17", CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture);
        return result;
    }

    private static CortexWeftCurriculum CreateConfig(int lineage) => new()
    {
        ExecutionFuel = 32 << (lineage % 4),
        CandidateLength = 6 + 4 * ((lineage / 2) % 4),
        TowerBlockBudget = 48 + 16 * ((lineage / 3) % 4),
    };

    private static WeftDiscoveryActions SelectExplorationAction(int lineage, int step)
    {
        int phase = step & 3;
        int action = (lineage & 3) switch
        {
            0 => (step + lineage) % ActionCount,
            1 => phase & 1,
            2 => phase switch { 0 or 1 => 0, 2 => 1, _ => 2 },
            _ => phase switch { 0 or 1 => 1, 2 => 0, _ => 3 },
        };
        return (WeftDiscoveryActions)action;
    }

    private static long ScoreDelta(in WeftDiscoveryDelta delta)
        => checked(delta.BehaviorClasses * 1_000_000_000L
                   + delta.AdmittedKnots * 1_000_000_000L
                   + delta.MdlSavingsMbits
                   - delta.RejectedKnots * 10_000L
                   - Math.Max(0, delta.Executions) * 100L);

    private static int FindBestAction(long[] utilities)
    {
        int best = 0;
        for (int action = 1; action < utilities.Length; action++)
            if (utilities[action] > utilities[best]) best = action;
        return best;
    }

    private static CacheWorkReceipt WriteRows(
        Run run,
        List<AssayRow> rows,
        int referenceLineages,
        in InstallRevision typed,
        in InstallRevision shuffled,
        in InstallRevision roundRobin)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        PolicyReadoutCache typedCache = new();
        PolicyReadoutCache shuffledCache = new();
        PolicyReadoutCache roundRobinCache = new();
        StringBuilder builder = new();
        Stopwatch phaseClock = Stopwatch.StartNew();
        Console.WriteLine($"  policy readout assay · write rows start total={rows.Count}");
        builder.AppendLine("lineage\tcheckpoint\tsplit\toracle\ttyped_readout\tshuffled_readout\tround_robin_readout\tu_sample\tu_mutate\tu_stress\tu_compare\tfeatures");
        for (int index = 0; index < rows.Count; index++)
        {
            AssayRow row = rows[index];
            MetricSample[] features = ReadMetricSamples(row.Features);
            PolicyReadoutCacheReceipt typedReceipt = ReadPublishedAction(in typed, features, typedCache);
            PolicyReadoutCacheReceipt shuffledReceipt = ReadPublishedAction(in shuffled, features, shuffledCache);
            PolicyReadoutCacheReceipt roundRobinReceipt = ReadPublishedAction(in roundRobin, features, roundRobinCache);
            builder.Append(row.Lineage).Append('\t').Append(row.Checkpoint).Append('\t')
                .Append(row.Lineage < referenceLineages ? "reference" : "probe").Append('\t')
                .Append(FindBestAction(row.Utilities)).Append('\t').Append(typedReceipt.Decision.Action).Append('\t')
                .Append(shuffledReceipt.Decision.Action).Append('\t').Append(roundRobinReceipt.Decision.Action);
            for (int action = 0; action < ActionCount; action++) builder.Append('\t').Append(row.Utilities[action]);
            builder.Append('\t');
            for (int feature = 0; feature < row.Features.Length; feature++)
            {
                if (feature > 0) builder.Append(',');
                builder.Append(row.Features[feature].ToString("R", CultureInfo.InvariantCulture));
            }
            builder.AppendLine();
            if ((index + 1) % 64 == 0)
                Console.WriteLine($"  policy readout assay · write rows processed={index + 1}/{rows.Count} wall={phaseClock.ElapsedMilliseconds}ms");
        }
        MetricSample[] terminalFeatures = ReadMetricSamples(rows[^1].Features);
        typedCache.RequestSweep();
        shuffledCache.RequestSweep();
        roundRobinCache.RequestSweep();
        // Terminal verifier work exhaustively revalidates every admitted context once per
        // publication. Runtime hits stay O(1); this is the explicit kill-line for cache integrity.
        _ = GrammarPolicyReadout.ReadCache(in typed, PolicyID, terminalFeatures, ActionCount, 0, typedCache);
        _ = GrammarPolicyReadout.ReadCache(in shuffled, PolicyID, terminalFeatures, ActionCount, 0, shuffledCache);
        _ = GrammarPolicyReadout.ReadCache(in roundRobin, PolicyID, terminalFeatures, ActionCount, 0, roundRobinCache);
        run.Write("readouts.tsv", builder.ToString());
        return new CacheWorkReceipt(
            rows.Count,
            typedCache.SweepCount + shuffledCache.SweepCount + roundRobinCache.SweepCount,
            typedCache.SweptEntries + shuffledCache.SweptEntries + roundRobinCache.SweptEntries,
            typedCache.CanonicalComputations + shuffledCache.CanonicalComputations + roundRobinCache.CanonicalComputations,
            stopwatch.ElapsedMilliseconds);
    }

    private static void WriteSummary(
        Run run,
        int referenceRows,
        int probeRows,
        in InstallRevision typedInstallRevision,
        in InstallRevision shuffledInstallRevision,
        in InstallRevision roundRobinInstallRevision,
        in PolicyScore typed,
        in PolicyScore shuffled,
        in PolicyScore roundRobin,
        in NullSeparationReceipt separation,
        bool overlapRejected,
        bool passed,
        in CacheWorkReceipt work)
    {
        StringBuilder builder = new();
        builder.AppendLine("verdict\tnull_separated\toverlap_rejected\trequired_divergences\ttyped_shuffled_divergences\ttyped_round_robin_divergences\treference_rows\tprobe_rows\ttyped_revision\tshuffled_revision\tround_robin_revision\ttyped_rules\tshuffled_rules\tround_robin_rules\ttyped_regret\tshuffled_regret\tround_robin_regret\ttyped_accuracy\ttyped_actions\tcold_misses\trefills\twarm_hits\treadout_misses\tcache_mismatches\tinvariant_rows\tinvariant_failures\tcache_rows\tcache_sweeps\tswept_entries\tcanonical_computations\tcache_work_ms");
        builder.Append(passed ? "PASS" : "FAIL").Append('\t').Append(separation.Passed ? "yes" : "no").Append('\t')
            .Append(overlapRejected ? "yes" : "no").Append('\t')
            .Append(separation.RequiredDivergences).Append('\t')
            .Append(separation.TypedShuffledDivergences).Append('\t')
            .Append(separation.TypedRoundRobinDivergences).Append('\t')
            .Append(referenceRows).Append('\t').Append(probeRows).Append('\t')
            .Append(typedInstallRevision.Revision.Value).Append('\t').Append(shuffledInstallRevision.Revision.Value).Append('\t').Append(roundRobinInstallRevision.Revision.Value).Append('\t')
            .Append(typedInstallRevision.Snapshot.Rules.Length).Append('\t').Append(shuffledInstallRevision.Snapshot.Rules.Length).Append('\t').Append(roundRobinInstallRevision.Snapshot.Rules.Length).Append('\t')
            .Append(typed.Regret.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
            .Append(shuffled.Regret.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
            .Append(roundRobin.Regret.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
            .Append(typed.Accuracy.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(typed.Actions).Append('\t')
            .Append(typed.ColdMisses + shuffled.ColdMisses + roundRobin.ColdMisses).Append('\t')
            .Append(typed.Refills + shuffled.Refills + roundRobin.Refills).Append('\t')
            .Append(typed.WarmHits + shuffled.WarmHits + roundRobin.WarmHits).Append('\t')
            .Append(typed.ReadoutMisses + shuffled.ReadoutMisses + roundRobin.ReadoutMisses).Append('\t')
            .Append(typed.CacheMismatches + shuffled.CacheMismatches + roundRobin.CacheMismatches).Append('\t')
            .Append(typed.InvariantRows).Append('\t').Append(typed.InvariantFailures).Append('\t')
            .Append(work.Rows).Append('\t').Append(work.Sweeps).Append('\t').Append(work.SweptEntries).Append('\t')
            .Append(work.CanonicalComputations).Append('\t')
            .Append(work.WallMilliseconds).AppendLine();
        run.Write("summary.tsv", builder.ToString());
    }

    private static void WriteHistoricalBaseline(Run run)
    {
        run.Write("historical_tree_baseline.tsv",
            "status\ttyped_regret\tshuffled_regret\tlive_typed_utility\tlive_round_robin_utility\n"
            + "HOLD_STATIC_ONLY\t"
            + HistoricalTree.TypedRegret.ToString("R", CultureInfo.InvariantCulture) + "\t"
            + HistoricalTree.ShuffledRegret.ToString("R", CultureInfo.InvariantCulture) + "\t"
            + HistoricalTree.LiveTypedUtility.ToString("R", CultureInfo.InvariantCulture) + "\t"
            + HistoricalTree.LiveRoundRobinUtility.ToString("R", CultureInfo.InvariantCulture) + "\n");
    }
}
