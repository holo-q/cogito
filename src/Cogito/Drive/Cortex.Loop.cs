namespace Cogito;

using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Induct;
using Datasets;

internal enum CortexMetricIDs : ushort
{
    LoomRules = 1,
    LoomSymbols,
    LoomMdlSaved,
    LoomInstallRevisionLagBytes,
    TapeResident,
    TapeShed,
    TapeExecution,
    TapeBornEvidence,
    TapeUnreflectedReplays,
    HomeostatAuthority,
    HomeostatPolicyCachedContexts,
    HomeostatShadowAgreement,
    HomeostatTakeoverExecutions,
    HomeostatPaidTakeovers,
    HomeostatReadmissions,
}


// ── THE RUNTIME LOOP ──  The developmental drain loop. The proven organs mount behind clean seams — ICurriculum schedules
// the intake (which span next · when the focus is grokked · which domain to cross to), EnergyPolicy generates, Reads
// reads the sparkline suite, SelfStream models cogito's own next token, Seriate consolidates — and the substrate
// ruling holds: the hot Tape is what it induces over, the durable Journal records what happened off the critical
// path. The load-bearing discipline is STRIDE-gated re-induction: re-inducing the WHOLE tape every step is the O(n²)
// wall (300-800ms/step — the wave-0 reaper's number), so the drive re-induces only after the tape grows ≥
// ReStrideBytes, decoupling induction count from step count (the intake proof rides a ≤stride-stale grammar losslessly).
//
//   loop step:  INDUCE  stride-gated re-induction over THE TAPE (the mandatory O(n²) fix)
//               GENERATE EnergyPolicy sample → block (read every step; minted only once drained)
//               READ    the sparkline suite → LossReading
//               MODEL   SelfStream predicts its OWN next token (excursion + thought channels via meta-grammars) →
//                       mints the RESIDUAL → mint-rate = the explore self-signal the WeightController reads (guardrail-safe)
//               FORK    THE SELF-REGULATION LAW — !drained → INTAKE (schooled residual accretion) · drained &&
//                       !exhausted → MOP-UP (residual drain of what grok→move-on abandoned; the replay-fork stays
//                       shut while real spans remain) · exhausted → MINT (loopback, replay-fraction-throttled) + MIX + STOP
//               SLEEP   every N steps / on grok-lock: couplings-guided defrag → force re-induce
//               STOP    on the MOMENTUM WALL read on the STRIDE clock (savings is stair-stepped — only a flat
//                       stride-over-stride slope is a genuine plateau), never step-count alone

/// The flattened run dialect — everything that makes a run deterministic (same corpus + seed ⇒ same curve, the Vow). One
/// record for every seam of the loop: the STRIDE discipline (the O(n²) fix), the scheduler choice (ICurriculum), the
/// energy preset (EnergyPolicy Weights), the consolidation cadence (Seriate), and the memory-hierarchy bit budget.
/// The grok / stride defaults come from GrokDefaults (the shared authority the curriculum organs read too).
internal sealed record CortexRunConfig(
    string CorpusPath,
    string ExpectedWorldSHA256 = "",
    int Steps = 200,
    int BlockLen = 700,               // chunks generated per step
    int MaxBlockBytes = 16384,        // cap on a block's EXPANSION (a high-level chunk balloons to up-to-corpus-length)
    int Window = 0,                   // node attention: 0 = WHOLE accreted tape (the deep-grok default); N>0 = anchor + last N (pass-2)
    double Lambda = 0.3,              // curiosity-metabolism decay — the proven sweet spot
    ulong Seed = 0xC0117011UL,
    // ── STRIDE discipline (CritLock's ReStrideBytes/DomStrideSpans promoted — the mandatory O(n²) fix) ──
    int ReStrideBytes = GrokDefaults.ReStrideBytes,     // re-induce the accreted tape only after it grows this many bytes (else reuse the ≤stride-stale grammar — lossless for the schedule)
    int DomStrideSpans = GrokDefaults.DomStrideSpans,   // per-domain isolated re-induce stride (GrokBell — pass-2b)
    int FrontierCapExps = GrokDefaults.FrontierCapExps, // frontier cover-basis cap (score a large pool against the top-N longest expansions, not all rules)
    // ── intake (RLEI-root frontier + the MIX rail) ──
    int IntakeBatch = 4,              // frontier-intake DRAIN RATE — spans accreted per step
    int SeedSpans = 3,                // bootstrap anchor so the residual can discriminate
    int MixEvery = 8,                 // MIX rail cadence (post-drain real re-ingest; 0 = off = the sealed-loop control arm)
    double AffirmGate = 0.0,          // THE INTAKE-AFFIRM GATE — the self-maintaining-memory source-fix: a MIX re-ingest whose per-line residual ≤ this (the grammar already GENERATES the event whole — CortexTapeAdmission, measured by GrammarCover.ParsedSize) does NOT re-append → re-observing learned data is a no-op → the tape stops growing from repetition. 0 = on at perfect-affirm (parsed ≤ 1/line — the shed mirror, the safe end); higher skips near-affirmed events harder; <0 = DISARMED (world mouth re-ingests unconditionally — byte-identical to the pre-gate machine, the kill-line control arm)
    // ── the scheduler (ICurriculum) ──
    string Curriculum = "flatpool",   // flatpool (whole-pool residual frontier, no domains — the day-one scheduler) | grokbell (per-domain CV-lock + bridge-order) | eml (the replay-calculator standalone) | campfire (the 3-way: grok-belled code+NL + vest-gated EML)
    string Glob = "*.cs,*.py,*.md,*.txt",   // when CorpusPath is a DIRECTORY: which files become domains (each matched file = one domain; comma-separated)
    double GrokCv = GrokDefaults.Cv,  // the grok lock-line FLOOR (per-domain criticality-CV lock — GrokBell adds the k-aware sampling band on top; the old flat 0.20 was the k≈12 special case, RESULTS "correction")
    int LockRounds = GrokDefaults.LockRounds,   // CV-lock hysteresis depth (GrokBell anti-chatter)
    // ── generation (EnergyPolicy Weights preset) ──
    string Energy = "metabolic",      // preset → Weights: metabolic (default) | markov | coupling | nodebirth
    double AffFloor = 1.0,            // node-birth affinity floor — the proven monotonic DEPTH lever
    // ── consolidationPhase control ──
    int IntervalConsolidationPhase = 0,      // interval control: 0 = off; every N steps / on grok-lock run the couplings-guided defrag (pass-2d)
    // ── memory hierarchy ──
    long GrammarBudgetBits = 0,       // the grammar's bit budget (working set under MDL-rent eviction); 0 = UNBOUNDED (honest about today — pass-2d mounts the GC)
    // ── provenance outcomeCredit ──
    int WScale = 8,                   // evidence weight per count (power of two, 1..128): an unvested Replay recurrence pays 1/WScale of a real one; 1 disables provenance weighting
    bool CrossReflect = true,         // THE SEALED-LOOP FIX: a Replay span (source=nodeX) reflects only when a DIFFERENT source exercises its ≥8B rule (a peer node's span OR the Real corpus); same-source self/clones are rejected. False restricts corroboration to Real corpus exercise and requires WScale>1 to matter
    double ReplayRatio = 1.0,          // cap UNVESTED replay spans at ratio x born-evidence spans, so hypotheses cannot outpace evidence available to corroborate them; a vested replay frees its slot. 0 removes the cap
    // ── the interoceptive homeostat (face 1) ──
    CortexConsolidationPhaseControl ConsolidationPhaseControl = CortexConsolidationPhaseControl.Homeostat, // interval = fixed cadence; homeostat = slow plane senses cost/self/criticality/collapse/provenance each step and actuates sleep-stride/MIX/intake/budget at sleep boundaries
    string SenseMask = "",            // the attribution ablation: comma-list of sense-planes pinned DARK at population — self-stream (ExcMint/ExcHit/ThtMint) | cost (InduceOpb/GenOpb/BitsPerSpan) | collapse (CollFrac/DfThird/Js) | provenance (UnvestedFrac/VestRate); "" = all senses live
    bool Breach = true,               // the breach-and-lower organ under the homeostat's BreachQuota (Stalled grants → the next consolidationPhase breaches past the greedy floor); --no-breach is the grants-expire-unspent ablation
    // ── SimHash locality organ (canon paper 05, Mounts 1-3) ──
    string Simhash = "auto",          // sleep defrag arm: auto/on = the O(Δ) incremental WEAVE off the persistent consolidationPhase-shift index | off = the proven exact O(spans²) re-seriation (the kill-line control arm, small-scale A/B)
    bool NearDupe = true,             // GC near-dupe containment demotion (Mount 2); false retains exact-only demotion
    // ── generalization ──
    bool Antiunify = true,            // sleep-pass: MDL-gated anti-unification over the working grammar → mint paradigm slots
    // ── stop ──
    double WallTol = 0.003,           // momentum dead-band — |savings-slope / level| below this reads as a genuine WALL
    // ── durability (the safe-to-kill law — a trace-driven machine must survive its own kill) ──
    int CheckpointEvery = 0,          // checkpoint cadence in steps; >0 = every N steps AND every sleep pass; 0 = AUTO (every sleep pass; every 25 steps when sleep is off)
    int CurveEvery = 1,               // curve.tsv row cadence — 1 = every step (the standing default; rows now land INCREMENTALLY, not at LAND); N>1 thins very long diagnosis runs
    // ── THE LOOM (O(Δ) incremental induction — phase 2: DEFAULT ON) ──
    bool Loom = true,                 // persistent Re-Pair state: induction = SPLICE (rank-encode new spans through the standing entries) + PUMP (the winner loop over the held counts) — O(Δ) per step, grammar fresh EVERY step the tape moves; the full consolidationPhase is the rare RESPLICE (re-parse the view through the standing rules at post-vest weights — re-prices vest transitions + retires drops, so wScale>1 rides — O(view·log), NOT a batch re-greed). --no-loom = the stride-gated batch arm byte-identical to phase 1 (the differential oracle)
    // ── TAPE-SHED (phase 3 — the memory win: the resident tape becomes a rolling window) ──
    bool Shed = true,                 // consolidationPhase evacuation (loom arm only): a learned evidence span (parses to ONE symbol) sheds its raw bytes to <run>/tape.spanlog and stays in the view; a stale unvested replay DROPS (leaves the view — hypothesis turnover frees mint headroom). --no-shed = the everything-resident control arm (isolates plateau-vs-quality in a diagnosis)
    // ── THE RHYTHM (emergent metabolism — Rhythm.cs) ──
    bool Rhythm = true,               // the per-step day/replay/consolidationPhase scheduler replaces the hard school→replay fork: the machine chooses each step's INPUT SOURCE (world / self / none) off its own senses (frontier-residual, grok-bell, coverage, surprise), ε-bootstrapped until replay outcomes teach the worth. Requires homeostatic consolidationPhase because sleep rides its surprise clock
    // ── THE POLICY PLANE ──
    HomeoPolicies HomeoPolicy = HomeoPolicies.Predict,
    HomeostatAutonomyModes HomeoAutonomy = HomeostatAutonomyModes.Full,
    CortexPolicyModes PolicyDefaultMode = CortexPolicyModes.Autonomic,
    CortexPolicyAuthorities PolicyAuthorityCeiling = CortexPolicyAuthorities.Grammar,
    CortexPolicyOverride[]? PolicyOverrides = null,
    int PolicyShadowDecisions = 8,
    int PolicyProposalInterval = 16,
    int ReadoutDeliberationQuota = 0,
    int[]? PolicyTrialHorizons = null,
    int EmlSignatureDigits = ReplayCalc.MountSig,
    EmlKnobs Eml = default,           // lowered EML curriculum knobs; ignored by non-EML curricula. Older checkpoint dialects and default mounts read as EmlKnobs.Mount.
    double EmlHoldoutFraction = 0,
    ulong EmlHoldoutSeed = 0,
    EmlTargetCatalogs EmlTargetCatalog = EmlTargetCatalogs.LeafCount,
    EmlGrammarSamplingModes EmlGrammarSampling = EmlGrammarSamplingModes.Live,
    EmlProcessCatalogs EmlProcessCatalog = EmlProcessCatalogs.Full,
    EmlRung0Modes EmlRung0 = EmlRung0Modes.Armed,
    EmlDeliberationModes EmlDeliberation = EmlDeliberationModes.Adaptive,
    EmlDeliberationQuota EmlDeliberationBudget = default,
    string? CurveReadout = null,      // null = fresh default curve; empty = explicitly no extra selectors.
    int ActionsPerStep = 1,
    CortexStopCondition[]? StopConditions = null,
    ICurriculum? RuntimeCurriculum = null,
    string RunName = "cortex",
    string DeepRematchGatePath = "",
    string DeepRematchGateDigest = "",
    long PolicyTrialAllocationArmSteps = 0,
    string PolicyTrialAllocationIdentity = "",
    CortexPolicyAuthorities PolicyTrialAllocationAuthority = CortexPolicyAuthorities.Grammar,
    string? EmlPairedFuelScheduleIdentity = null,
    AdmissionPlan? AdmissionPlan = null);

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE RUNTIME
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

public sealed partial class Cortex
{
    internal static string PersistedConfigDigest(CortexRunConfig config)
    {
        CortexRunConfig persisted = config with { RuntimeCurriculum = null };
        return Convert.ToHexStringLower(SHA256.HashData(Checkpoint.EncodeConfig(persisted)));
    }

    internal static string ArmNeutralPersistedConfigDigest(CortexRunConfig config)
        => PersistedConfigDigest(config with
        {
            PolicyAuthorityCeiling = CortexPolicyAuthorities.Grammar,
            EmlProcessCatalog = EmlProcessCatalogs.Full,
            EmlRung0 = EmlRung0Modes.Armed,
            EmlDeliberation = EmlDeliberationModes.Adaptive,
            PolicyTrialAllocationArmSteps = 0,
            PolicyTrialAllocationIdentity = "",
            PolicyTrialAllocationAuthority = CortexPolicyAuthorities.Grammar,
        });

    public int Resume(string runDir, int steps = 0, bool forkCurriculum = false)
    {
        string? dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, Checkpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {Checkpoint.FileName} under '{runDir}' — nothing to resume");
            return 1;
        }

        CortexRunConfig cfg = Checkpoint.PeekConfig(dir);
        if (steps > 0) cfg = cfg with { Steps = steps };
        CortexRunConfig runtimeConfig = _config.ToRunConfig(_mountedCurriculum);
        cfg = cfg with
        {
            Curriculum = runtimeConfig.Curriculum,
            IntakeBatch = runtimeConfig.IntakeBatch,
            ActionsPerStep = runtimeConfig.ActionsPerStep,
            RuntimeCurriculum = _mountedCurriculum,
        };
        return Drive(this, cfg, dir, allowRuntimeWorldFork: forkCurriculum, checkpointRunEnd: true);
    }

    internal CortexForkExecutionReceipt RunMaterializedFork(
        string runDirectory,
        int absoluteHorizon,
        Action<Cortex>? interveneAfterLoad,
        CortexForkCompletionModes completionMode,
        CortexForkAnytimeIdentity? anytimeIdentity = null,
        Action<Cortex, CortexExecutionWindow>? afterRuntimeBind = null,
        CortexExecutionWindow? executionWindow = null,
        string expectedPersistedConfigDigest = "",
        CheckpointRoundTripProof? checkpointProof = null,
        Action<Cortex, int>? afterCompletedStep = null,
        Action<Cortex, int>? afterCompletedStepEveryStep = null,
        Action<Cortex, int>? beforeCompletedStep = null,
        Action<Cortex>? captureCompletionBeforeWorldDispose = null,
        bool prepareOnly = false)
    {
        CortexRunConfig source = Checkpoint.PeekConfig(runDirectory);
        string sourceConfigDigest = PersistedConfigDigest(source);
        string factoryConfigDigest = ComputeFactoryPersistedConfigDigest();
        if (!string.Equals(sourceConfigDigest, factoryConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"fork factory config diverged from child checkpoint: {runDirectory} source-digest={sourceConfigDigest} factory-digest={factoryConfigDigest} delta={DescribePersistedConfigDelta(source, _config.ToRunConfig(_mountedCurriculum))}");
        if (!string.IsNullOrWhiteSpace(expectedPersistedConfigDigest)
            && !string.Equals(factoryConfigDigest, expectedPersistedConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"fork child config digest diverged from cold seed: {runDirectory} source-digest={sourceConfigDigest} factory-digest={factoryConfigDigest} expected-digest={expectedPersistedConfigDigest} delta={DescribePersistedConfigDelta(source, _config.ToRunConfig(_mountedCurriculum))}");
        CortexRunConfig config = source with { RuntimeCurriculum = _mountedCurriculum };
        bool runtimeStopRequested = false;
        long runtimeBindWallMilliseconds = 0;
        long runtimeBindRawTicks = 0;
        long executionStarted = 0;
        bool completedStepCallbackInvoked = false;
        Action<Cortex, int>? oneShotAfterCompletedStep = afterCompletedStep is null
            ? null
            : (runtime, completedStep) =>
            {
                if (completedStepCallbackInvoked) return;
                completedStepCallbackInvoked = true;
                afterCompletedStep(runtime, completedStep);
            };
        Action<Cortex, CortexExecutionWindow>? measuredAfterRuntimeBind = afterRuntimeBind is null
            ? null
            : (runtime, window) =>
            {
                long started = Stopwatch.GetTimestamp();
                afterRuntimeBind(runtime, window);
                runtimeBindRawTicks = Math.Max(1, Stopwatch.GetTimestamp() - started);
                runtimeBindWallMilliseconds = Math.Max(0, Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond);
                executionStarted = Stopwatch.GetTimestamp();
            };
        if (measuredAfterRuntimeBind is null)
            executionStarted = Stopwatch.GetTimestamp();
        if (checkpointProof is { } proof)
        {
            CheckpointRoundTripProof destinationProof = Checkpoint.ReadImageProof(
                runDirectory, expectedPersistedConfigDigest, executionWindow?.StartStep ?? Checkpoint.PeekNextStep(runDirectory),
                proof.SaveLoadSaveExact);
            if (!proof.Matches(destinationProof))
                throw new InvalidDataException($"fork checkpoint proof does not match child image: {runDirectory}");
        }
        int exitCode = Drive(this, config, runDirectory, interveneAfterLoad: interveneAfterLoad,
            runToAbsoluteHorizon: completionMode == CortexForkCompletionModes.ExactAbsoluteStep,
            verifyLoadedCheckpoint: checkpointProof is null || !checkpointProof.Value.SaveLoadSaveExact, checkpointRunEnd: true,
            observeRuntimeStop: () => runtimeStopRequested = true,
            anytimeIdentity: anytimeIdentity,
            afterRuntimeBind: measuredAfterRuntimeBind,
            executionWindow: executionWindow ?? new CortexExecutionWindow(0, absoluteHorizon),
            afterCompletedStep: oneShotAfterCompletedStep,
            afterCompletedStepEveryStep: afterCompletedStepEveryStep,
            beforeCompletedStep: beforeCompletedStep,
            captureCompletionBeforeWorldDispose: captureCompletionBeforeWorldDispose,
            prepareOnly: prepareOnly);
        long executionRawTicks = Math.Max(1, Stopwatch.GetTimestamp() - executionStarted);
        long executionWallMilliseconds = Math.Max(0, Stopwatch.GetElapsedTime(executionStarted).Ticks / TimeSpan.TicksPerMillisecond);
        return new CortexForkExecutionReceipt(
            exitCode, runtimeStopRequested, runtimeBindWallMilliseconds, executionWallMilliseconds,
            runtimeBindRawTicks, executionRawTicks);
    }

    private string ComputeFactoryPersistedConfigDigest()
        => PersistedConfigDigest(_config.ToRunConfig(_mountedCurriculum));

    internal static string DescribePersistedConfigDelta(CortexRunConfig source, CortexRunConfig factory)
    {
        List<string> delta = [];
        AddConfigDelta(delta, nameof(CortexRunConfig.CorpusPath), source.CorpusPath, factory.CorpusPath);
        AddConfigDelta(delta, nameof(CortexRunConfig.ExpectedWorldSHA256), source.ExpectedWorldSHA256, factory.ExpectedWorldSHA256);
        AddConfigDelta(delta, nameof(CortexRunConfig.Steps), source.Steps, factory.Steps);
        AddConfigDelta(delta, nameof(CortexRunConfig.BlockLen), source.BlockLen, factory.BlockLen);
        AddConfigDelta(delta, nameof(CortexRunConfig.MaxBlockBytes), source.MaxBlockBytes, factory.MaxBlockBytes);
        AddConfigDelta(delta, nameof(CortexRunConfig.Window), source.Window, factory.Window);
        AddConfigDelta(delta, nameof(CortexRunConfig.Lambda), source.Lambda, factory.Lambda);
        AddConfigDelta(delta, nameof(CortexRunConfig.Seed), source.Seed, factory.Seed);
        AddConfigDelta(delta, nameof(CortexRunConfig.ReStrideBytes), source.ReStrideBytes, factory.ReStrideBytes);
        AddConfigDelta(delta, nameof(CortexRunConfig.DomStrideSpans), source.DomStrideSpans, factory.DomStrideSpans);
        AddConfigDelta(delta, nameof(CortexRunConfig.FrontierCapExps), source.FrontierCapExps, factory.FrontierCapExps);
        AddConfigDelta(delta, nameof(CortexRunConfig.IntakeBatch), source.IntakeBatch, factory.IntakeBatch);
        AddConfigDelta(delta, nameof(CortexRunConfig.SeedSpans), source.SeedSpans, factory.SeedSpans);
        AddConfigDelta(delta, nameof(CortexRunConfig.MixEvery), source.MixEvery, factory.MixEvery);
        AddConfigDelta(delta, nameof(CortexRunConfig.Curriculum), source.Curriculum, factory.Curriculum);
        AddConfigDelta(delta, nameof(CortexRunConfig.Glob), source.Glob, factory.Glob);
        AddConfigDelta(delta, nameof(CortexRunConfig.GrokCv), source.GrokCv, factory.GrokCv);
        AddConfigDelta(delta, nameof(CortexRunConfig.LockRounds), source.LockRounds, factory.LockRounds);
        AddConfigDelta(delta, nameof(CortexRunConfig.Energy), source.Energy, factory.Energy);
        AddConfigDelta(delta, nameof(CortexRunConfig.AffFloor), source.AffFloor, factory.AffFloor);
        AddConfigDelta(delta, nameof(CortexRunConfig.IntervalConsolidationPhase), source.IntervalConsolidationPhase, factory.IntervalConsolidationPhase);
        AddConfigDelta(delta, nameof(CortexRunConfig.GrammarBudgetBits), source.GrammarBudgetBits, factory.GrammarBudgetBits);
        AddConfigDelta(delta, nameof(CortexRunConfig.Simhash), source.Simhash, factory.Simhash);
        AddConfigDelta(delta, nameof(CortexRunConfig.NearDupe), source.NearDupe, factory.NearDupe);
        AddConfigDelta(delta, nameof(CortexRunConfig.Antiunify), source.Antiunify, factory.Antiunify);
        AddConfigDelta(delta, nameof(CortexRunConfig.WallTol), source.WallTol, factory.WallTol);
        AddConfigDelta(delta, nameof(CortexRunConfig.CheckpointEvery), source.CheckpointEvery, factory.CheckpointEvery);
        AddConfigDelta(delta, nameof(CortexRunConfig.CurveEvery), source.CurveEvery, factory.CurveEvery);
        AddConfigDelta(delta, nameof(CortexRunConfig.WScale), source.WScale, factory.WScale);
        AddConfigDelta(delta, nameof(CortexRunConfig.ConsolidationPhaseControl), source.ConsolidationPhaseControl, factory.ConsolidationPhaseControl);
        AddConfigDelta(delta, nameof(CortexRunConfig.SenseMask), source.SenseMask, factory.SenseMask);
        AddConfigDelta(delta, nameof(CortexRunConfig.Breach), source.Breach, factory.Breach);
        AddConfigDelta(delta, nameof(CortexRunConfig.ReplayRatio), source.ReplayRatio, factory.ReplayRatio);
        AddConfigDelta(delta, nameof(CortexRunConfig.Loom), source.Loom, factory.Loom);
        AddConfigDelta(delta, nameof(CortexRunConfig.Shed), source.Shed, factory.Shed);
        AddConfigDelta(delta, nameof(CortexRunConfig.Rhythm), source.Rhythm, factory.Rhythm);
        AddConfigDelta(delta, nameof(CortexRunConfig.HomeoPolicy), source.HomeoPolicy, factory.HomeoPolicy);
        AddConfigDelta(delta, nameof(CortexRunConfig.HomeoAutonomy), source.HomeoAutonomy, factory.HomeoAutonomy);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyDefaultMode), source.PolicyDefaultMode, factory.PolicyDefaultMode);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyAuthorityCeiling), source.PolicyAuthorityCeiling, factory.PolicyAuthorityCeiling);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyShadowDecisions), source.PolicyShadowDecisions, factory.PolicyShadowDecisions);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyProposalInterval), source.PolicyProposalInterval, factory.PolicyProposalInterval);
        AddConfigDelta(delta, nameof(CortexRunConfig.ReadoutDeliberationQuota), source.ReadoutDeliberationQuota, factory.ReadoutDeliberationQuota);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlSignatureDigits), source.EmlSignatureDigits, factory.EmlSignatureDigits);
        AddConfigDelta(delta, nameof(CortexRunConfig.Eml), source.Eml, factory.Eml);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlHoldoutFraction), source.EmlHoldoutFraction, factory.EmlHoldoutFraction);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlHoldoutSeed), source.EmlHoldoutSeed, factory.EmlHoldoutSeed);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlTargetCatalog), source.EmlTargetCatalog, factory.EmlTargetCatalog);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlGrammarSampling), source.EmlGrammarSampling, factory.EmlGrammarSampling);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlProcessCatalog), source.EmlProcessCatalog, factory.EmlProcessCatalog);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlRung0), source.EmlRung0, factory.EmlRung0);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlDeliberation), source.EmlDeliberation, factory.EmlDeliberation);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlDeliberationBudget), NormalizeQuota(source.EmlDeliberationBudget), NormalizeQuota(factory.EmlDeliberationBudget));
        AddConfigDelta(delta, nameof(CortexRunConfig.CurveReadout), source.CurveReadout, factory.CurveReadout);
        AddConfigDelta(delta, nameof(CortexRunConfig.ActionsPerStep), source.ActionsPerStep, factory.ActionsPerStep);
        AddPolicyOverridesDelta(delta, source.PolicyOverrides, factory.PolicyOverrides);
        AddSequenceDelta(delta, nameof(CortexRunConfig.PolicyTrialHorizons), source.PolicyTrialHorizons ?? [16, 64, 256], factory.PolicyTrialHorizons ?? [16, 64, 256]);
        AddSequenceDelta(delta, nameof(CortexRunConfig.StopConditions), source.StopConditions ?? [], factory.StopConditions ?? []);
        AddConfigDelta(delta, nameof(CortexRunConfig.RunName), source.RunName, factory.RunName);
        AddConfigDelta(delta, nameof(CortexRunConfig.DeepRematchGatePath), source.DeepRematchGatePath, factory.DeepRematchGatePath);
        AddConfigDelta(delta, nameof(CortexRunConfig.DeepRematchGateDigest), source.DeepRematchGateDigest, factory.DeepRematchGateDigest);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyTrialAllocationArmSteps), source.PolicyTrialAllocationArmSteps, factory.PolicyTrialAllocationArmSteps);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyTrialAllocationIdentity), source.PolicyTrialAllocationIdentity, factory.PolicyTrialAllocationIdentity);
        AddConfigDelta(delta, nameof(CortexRunConfig.PolicyTrialAllocationAuthority), source.PolicyTrialAllocationAuthority, factory.PolicyTrialAllocationAuthority);
        AddConfigDelta(delta, nameof(CortexRunConfig.EmlPairedFuelScheduleIdentity), source.EmlPairedFuelScheduleIdentity, factory.EmlPairedFuelScheduleIdentity);
        return delta.Count == 0 ? "unattributed" : string.Join(',', delta);
    }

    private static void AddConfigDelta<T>(List<string> delta, string name, T source, T factory)
    {
        if (!EqualityComparer<T>.Default.Equals(source, factory)) delta.Add($"{name}:{source}->{factory}");
    }

    private static EmlDeliberationQuota NormalizeQuota(EmlDeliberationQuota quota)
        => quota.Equals(default) ? EmlDeliberationQuota.Default : quota;

    private static void AddPolicyOverridesDelta(List<string> delta, CortexPolicyOverride[]? source, CortexPolicyOverride[]? factory)
    {
        CortexPolicyOverride[] sourceOverrides = source ?? [];
        CortexPolicyOverride[] factoryOverrides = factory ?? [];
        if (sourceOverrides.Length == factoryOverrides.Length)
        {
            bool equal = true;
            for (int i = 0; i < sourceOverrides.Length; i++)
            {
                CortexPolicyOverride left = sourceOverrides[i];
                CortexPolicyOverride right = factoryOverrides[i];
                if (!left.Policy.Equals(right.Policy) || left.Mode != right.Mode)
                {
                    equal = false;
                    break;
                }
            }

            if (equal) return;
        }

        delta.Add($"{nameof(CortexRunConfig.PolicyOverrides)}:{FormatPolicyOverrides(sourceOverrides)}->{FormatPolicyOverrides(factoryOverrides)}");
    }

    private static void AddSequenceDelta<T>(List<string> delta, string name, IReadOnlyList<T> source, IReadOnlyList<T> factory)
    {
        if (source.Count == factory.Count)
        {
            bool equal = true;
            for (int i = 0; i < source.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(source[i], factory[i]))
                {
                    equal = false;
                    break;
                }
            }

            if (equal) return;
        }

        delta.Add($"{name}:{FormatSequence(source)}->{FormatSequence(factory)}");
    }

    private static string FormatPolicyOverrides(IReadOnlyList<CortexPolicyOverride> overrides)
        => $"[{string.Join(';', overrides.Select(static item => $"{item.Policy.Value}={item.Mode}"))}]";

    private static string FormatSequence<T>(IReadOnlyList<T> values)
        => $"[{string.Join(';', values)}]";

    internal int VerifyMaterializedFork(string runDirectory)
    {
        CortexRunConfig source = Checkpoint.PeekConfig(runDirectory);
        CortexRunConfig config = source with { RuntimeCurriculum = _mountedCurriculum };
        return Drive(this, config, runDirectory, verifyOnly: true);
    }

    /// RESUME a killed run from its run dir's checkpoint.bin — the config rides INSIDE the checkpoint, so the run
    /// dir is the only argument; PHASE 0 rebuilds the corpus world deterministically from it, then the full state
    /// load restores every organ and the drive continues to cfg.Steps. `verify` short-circuits before the drive:
    /// load → re-encode → byte-compare against the file (the Save∘Load∘Save identity check).
    /// `steps` > 0 EXTENDS the horizon (a landed run resumes past its original cap — checkpoints cut during the
    /// extended leg carry the new horizon, so resume-of-resume continues it); never valid under `verify` (the
    /// round-trip must compare the config the image actually carries). `consolidationPhaseProbe` = load, run ONE Consolidate
    /// with the sleep.sub sub-phase walls, and exit — the next consolidationPhase's per-organ cost, without driving a step.
    public static int Resume(string runDir, bool verify = false, bool memstat = false, int steps = 0, bool consolidationPhaseProbe = false)
    {
        var dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, Checkpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {Checkpoint.FileName} under '{runDir}' — nothing to resume (the run predates checkpointing, or never reached its first checkpoint)");
            return 1;
        }
        var cfg = Checkpoint.PeekConfig(dir);
        if (steps > 0)
        {
            if (verify) { Console.Error.WriteLine("  --steps cannot ride --verify: the round-trip check compares the config the checkpoint carries, not an override"); return 1; }
            cfg = cfg with { Steps = steps };
        }
        Cortex runtime = CreateCheckpointRuntime(cfg, dir);
        if (runtime.MountedCurriculum is not null)
            cfg = cfg with { RuntimeCurriculum = runtime.MountedCurriculum };
        return Drive(runtime, cfg, dir, verify, memstat, consolidationPhaseProbe,
            checkpointRunEnd: !verify && !memstat && !consolidationPhaseProbe);
    }

    internal static bool VerifyCheckpointRoundTrip(string runDir, out string diskDigest, out string encodedDigest, Action<Cortex>? inspectLoaded = null)
    {
        string? dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, Checkpoint.FileName)))
            throw new FileNotFoundException("checkpoint is missing", runDir);
        // Existing receipt consumers intentionally hash the physical base image. Materialize
        // an active delta before that legacy verification boundary; resume itself still replays
        // deltas without forcing a compaction.
        Checkpoint.Compact(dir);
        CortexRunConfig cfg = Checkpoint.PeekConfig(dir);
        Cortex runtime = CreateCheckpointRuntime(cfg, dir);
        if (runtime.MountedCurriculum is not null) cfg = cfg with { RuntimeCurriculum = runtime.MountedCurriculum };
        string encoded = "";
        int result = LoadCheckpointRuntime(runtime, cfg, dir,
            inspectLoaded is null ? null : (loaded, _, _) => inspectLoaded(loaded),
            checkpointOccurrenceCheck: bytes => encoded = Convert.ToHexStringLower(SHA256.HashData(bytes)));
        diskDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, Checkpoint.FileName))));
        encodedDigest = encoded;
        return result == 0 && string.Equals(diskDigest, encodedDigest, StringComparison.Ordinal);
    }

    internal static bool VerifyCheckpointLogicalRoundTrip(string runDir, out string diskDigest, out string encodedDigest)
    {
        string? dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, Checkpoint.FileName)))
            throw new FileNotFoundException("checkpoint is missing", runDir);
        byte[] effective = Checkpoint.LoadEffectiveImage(dir);
        diskDigest = Checkpoint.LogicalStateSHA256(effective);
        CortexRunConfig cfg = Checkpoint.PeekConfig(dir);
        Cortex runtime = CreateCheckpointRuntime(cfg, dir);
        if (runtime.MountedCurriculum is not null) cfg = cfg with { RuntimeCurriculum = runtime.MountedCurriculum };
        int result = LoadCheckpointRuntime(runtime, cfg, dir);
        encodedDigest = result == 0 ? diskDigest : "";
        return result == 0;
    }

    /// Verify one checkpoint without opening the drive or touching durable state. The effective image is read once
    /// through the delta authority, loaded into a fresh in-memory World, and re-encoded into a MemoryStream. The
    /// recursive run manifest (paths, lengths, and mtimes) brackets the whole operation so a verifier that mutates
    /// an artifact cannot mint a Vow receipt.
    internal static CheckpointVowReceipt VerifyReadOnlyCheckpointVow(string runDir)
    {
        string? directory = Cogito.Run.Resolve(runDir);
        if (directory is null) throw new DirectoryNotFoundException(runDir);
        (byte[] effective, string basePhysical, string chain) = CheckpointDelta.ReadEffectiveSnapshot(directory);
        return VerifyReadOnlyCheckpointVow(directory, effective, basePhysical, chain);
    }

    /// Verify an already captured effective checkpoint image. The caller owns
    /// the immutable arm snapshot; this overload deliberately performs no
    /// checkpoint read, allowing tape decoding and Vow verification to share
    /// the same bytes.
    internal static CheckpointVowReceipt VerifyReadOnlyCheckpointVow(string runDir, byte[] effective, string basePhysical, string chain)
    {
        string? directory = Cogito.Run.Resolve(runDir);
        if (directory is null) throw new DirectoryNotFoundException(runDir);
        Dictionary<string, (long Length, long LastWriteUtcTicks)> before = CaptureCheckpointManifest(directory);
        Stopwatch vowClock = Stopwatch.StartNew();
        try
        {
            CortexRunConfig config = Checkpoint.PeekConfig(effective);
            Cortex runtime = CreateCheckpointRuntime(config, directory);
            if (runtime.MountedCurriculum is not null)
                config = config with { RuntimeCurriculum = runtime.MountedCurriculum };
            // A checkpoint Vow is an artifact-only verification. It must not require the
            // external corpus to remain mounted after the run landed; the live drive path
            // performs the registered-world check before step zero instead.
            using World world = new(config, verifyExpectedWorld: false);
            runtime.BindCheckpointAuthority(Cogito.Run.Open(directory));
            RegisterRuntimePolicies(runtime, world, config);
            string tapeLogPath = Path.Combine(directory, "tape.spanlog");
            if (File.Exists(tapeLogPath))
                world.Tape.MountLog(File.Open(tapeLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            (CortexSnap snap, RePairResult grammar, InstallRevision installRevision) = Checkpoint.LoadImage(
                effective, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
                world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
                world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);
            runtime.UnbindCheckpointRuntime();
            long loadMs = vowClock.ElapsedMilliseconds;
            byte[] reencoded = Checkpoint.Encode(config, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
                snap, grammar, installRevision, world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
                world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);
            long saveMs = vowClock.ElapsedMilliseconds - loadMs;
            bool exact = effective.AsSpan().SequenceEqual(reencoded);
            bool manifestUnchanged = ManifestEquals(before, CaptureCheckpointManifest(directory));
            long manifestMs = vowClock.ElapsedMilliseconds - loadMs - saveMs;
            string effectivePhysical = Checkpoint.PhysicalSHA256(effective);
            string reencodedPhysical = Checkpoint.PhysicalSHA256(reencoded);
            long digestMs = vowClock.ElapsedMilliseconds - loadMs - saveMs - manifestMs;
            List<string> failures = new();
            if (!exact) failures.Add("effective checkpoint sections diverged after in-memory Save∘Load∘Save");
            if (!manifestUnchanged) failures.Add("recursive checkpoint manifest or mtimes changed during verification");
            Trace.Cortex.Boundary("checkpoint.vow", $"load_ms={loadMs} save_ms={saveMs} manifest_ms={manifestMs} digest_ms={digestMs} image={effective.LongLength} exact={exact} manifest={manifestUnchanged}");
            return new CheckpointVowReceipt(exact && manifestUnchanged, Checkpoint.CurrentSectionCount,
                effective.LongLength, reencoded.LongLength, effectivePhysical,
                reencodedPhysical, basePhysical, chain, manifestUnchanged, failures.ToArray());
        }
        catch (Exception error)
        {
            bool manifestUnchanged = ManifestEquals(before, CaptureCheckpointManifest(directory));
            string failure = $"{error.GetType().Name}: {error.Message}";
            if (!manifestUnchanged) failure += "; recursive checkpoint manifest or mtimes changed during verification";
            return new CheckpointVowReceipt(false, 0, 0, 0, "", "", "", "", manifestUnchanged, [failure]);
        }
    }

    /// Process-wide effective-image memo. Fork adjudication reads the same
    /// (keyframe, rail) pair 15-20× per arm; each miss pays a full World
    /// construction + all-organ restore + rail replay + re-Encode. Hits are
    /// authenticated in two tiers, both under the held write gate: the epoch
    /// fast path returns the cached image when the keyframe/rail file stamps
    /// (length + mtime ticks) are byte-for-byte the ones ReadAuthority proved
    /// on the hit that populated them — nothing can mutate the files under the
    /// gate, so an unchanged stamp is an unchanged authenticated state. ANY
    /// doubt (no entry, stamp mismatch, missing file) falls back to the full
    /// ReadAuthority path, which re-hashes the keyframe and re-scans the
    /// chained rail — a keyframe swap or rail append changes the key and
    /// forces a rebuild. Bounded LRU; entries are whole images.
    private const int EffectiveImageMemoCapacity = 8;
    private static readonly Dictionary<string, (string BasePhysicalSHA256, string ChainSHA256, CheckpointFileStamp Stamp, byte[] Image, long Touched)> _effectiveImageMemo = new(StringComparer.Ordinal);
    private static long _effectiveImageMemoClock;

    /// Filesystem identity of the (keyframe, rail) pair at an authenticated
    /// read. Delta fields are -1 when no rail file exists.
    private readonly record struct CheckpointFileStamp(long BaseLength, long BaseMtimeTicks, long DeltaLength, long DeltaMtimeTicks)
    {
        internal bool Valid => BaseLength >= 0;
    }

    private static CheckpointFileStamp CaptureCheckpointFileStamp(string directory)
    {
        FileInfo baseInfo = new(Path.Combine(directory, Checkpoint.FileName));
        if (!baseInfo.Exists) return new(-1, -1, -1, -1);
        FileInfo deltaInfo = new(Path.Combine(directory, Checkpoint.DeltaFileName));
        return new(baseInfo.Length, baseInfo.LastWriteTimeUtc.Ticks,
            deltaInfo.Exists ? deltaInfo.Length : -1, deltaInfo.Exists ? deltaInfo.LastWriteTimeUtc.Ticks : -1);
    }

    /// Materialize the typed mutation rail into an in-memory checkpoint image.
    /// The canonical keyframe remains untouched; this is the read-only image
    /// handed to adjudicators so their tape, lineage, and step horizon all
    /// describe the terminal rail state rather than its base prefix.
    /// Callers must treat the returned image as read-only: cache hits alias
    /// one shared array.
    internal static byte[] MaterializeReadOnlyCheckpoint(string runDir)
    {
        string? directory = Cogito.Run.Resolve(runDir);
        if (directory is null) throw new DirectoryNotFoundException(runDir);
        // Hold the run's write gate across identity capture + materialization
        // so an appending writer cannot advance the rail between the authority
        // scan and the image build (which would bind a newer image to an older
        // key). The gate is reentrant for callers already holding it.
        lock (Cogito.Run.CheckpointWriteGate(directory))
        {
            CheckpointFileStamp stamp = CaptureCheckpointFileStamp(directory);
            lock (_effectiveImageMemo)
            {
                if (stamp.Valid && _effectiveImageMemo.TryGetValue(directory, out var fast) && fast.Stamp == stamp)
                {
                    _effectiveImageMemo[directory] = fast with { Touched = ++_effectiveImageMemoClock };
                    return fast.Image;
                }
            }
            CheckpointDeltaAuthority authority = CheckpointDelta.ReadAuthority(directory);
            lock (_effectiveImageMemo)
            {
                if (_effectiveImageMemo.TryGetValue(directory, out var cached)
                    && string.Equals(cached.BasePhysicalSHA256, authority.BasePhysicalSHA256, StringComparison.Ordinal)
                    && string.Equals(cached.ChainSHA256, authority.ChainSHA256, StringComparison.Ordinal))
                {
                    _effectiveImageMemo[directory] = cached with { Stamp = stamp, Touched = ++_effectiveImageMemoClock };
                    return cached.Image;
                }
            }
            byte[] image = MaterializeEffectiveCheckpoint(directory);
            lock (_effectiveImageMemo)
            {
                if (_effectiveImageMemo.Count >= EffectiveImageMemoCapacity && !_effectiveImageMemo.ContainsKey(directory))
                {
                    string? coldest = null;
                    long coldestTouch = long.MaxValue;
                    foreach ((string key, var entry) in _effectiveImageMemo)
                        if (entry.Touched < coldestTouch) { coldest = key; coldestTouch = entry.Touched; }
                    if (coldest is not null) _effectiveImageMemo.Remove(coldest);
                }
                _effectiveImageMemo[directory] = (authority.BasePhysicalSHA256, authority.ChainSHA256, stamp, image, ++_effectiveImageMemoClock);
            }
            return image;
        }
    }

    private static byte[] MaterializeEffectiveCheckpoint(string directory)
    {
        byte[] baseImage = File.ReadAllBytes(Path.Combine(directory, Checkpoint.FileName));
        if (!File.Exists(Path.Combine(directory, Checkpoint.DeltaFileName))) return baseImage;

        CortexRunConfig config = Checkpoint.PeekConfig(baseImage);
        Cortex runtime = CreateCheckpointRuntime(config, directory);
        if (runtime.MountedCurriculum is not null)
            config = config with { RuntimeCurriculum = runtime.MountedCurriculum };
        using World world = new(config, verifyExpectedWorld: false);
        // Effective-image verification decodes policy state before Drive can bind its runtime. Preserve
        // the durable run identity here so shed allocation prefixes remain authenticated during LoadImage.
        runtime.BindCheckpointAuthority(Cogito.Run.Open(directory));
        RegisterRuntimePolicies(runtime, world, config);
        string tapeLogPath = Path.Combine(directory, "tape.spanlog");
        if (File.Exists(tapeLogPath))
            world.Tape.MountLog(File.Open(tapeLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        (CortexSnap snap, RePairResult grammar, InstallRevision installRevision) = Checkpoint.LoadImage(
            baseImage, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
            world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
            world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);
        // LoadImage restores the persisted enable bit after decoding the policy
        // section. Rebind the now-armed turnstile to the loaded tape before the
        // mutation rail is replayed; replay must never weaken its lineage guard.
        runtime.BindLoopLineage(world.Tape, world.Journal, world.Curriculum.LineageWorldRootPredicate);
        CheckpointDeltaReplayReceipt replay = CheckpointDelta.ReplayInto(
            directory, world.Tape, world.Journal, world.Reads, world.Curriculum, world.SelfStream,
            world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Rhythm, runtime, world.Loom);
        if (!replay.LatestGrammarArtifact.IsEmpty)
        {
            GrammarArtifactDelta artifact = replay.LatestGrammarArtifact;
            installRevision = Checkpoint.LoadGrammarArtifact(directory, artifact.FileName, artifact.SHA256, artifact.Revision);
            grammar = installRevision.Snapshot.ToRePairResult();
            snap = replay.LatestSnap is CortexSnap latest
                ? latest with { GrammarRevision = artifact.Revision }
                : snap with { NextStep = replay.LastToStep, GrammarRevision = artifact.Revision };
        }
        if (replay.RecordCount != 0 && replay.LatestSnap is null)
            throw new InvalidDataException("typed mutation rail predates the complete CortexSnap replacement and cannot materialize an effective image");
        if (replay.LatestGrammarArtifact.IsEmpty)
            snap = replay.LatestSnap ?? (snap with
        {
            NextStep = replay.RecordCount == 0 ? snap.NextStep : replay.LastToStep,
        });
        runtime.UnbindCheckpointRuntime();
        return Checkpoint.Encode(config, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
            snap, grammar, installRevision, world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
            world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);
    }

    /// Re-encode a legacy checkpoint into the current typed wire after its read-only Vow has passed.
    /// This is profile-only migration support: the caller owns an isolated copy and atomically replaces its
    /// keyframe, so ordinary runtime resume never silently rewrites a user's source run.
    internal static byte[] PromoteReadOnlyCheckpointV3(string runDir)
    {
        string? directory = Cogito.Run.Resolve(runDir);
        if (directory is null) throw new DirectoryNotFoundException(runDir);
        (byte[] effective, _, _) = CheckpointDelta.ReadEffectiveSnapshot(directory);
        CortexRunConfig config = Checkpoint.PeekConfig(effective);
        Cortex runtime = CreateCheckpointRuntime(config, directory);
        if (runtime.MountedCurriculum is not null)
            config = config with { RuntimeCurriculum = runtime.MountedCurriculum };
        using World world = new(config, verifyExpectedWorld: false);
        runtime.BindCheckpointAuthority(Cogito.Run.Open(directory));
        RegisterRuntimePolicies(runtime, world, config);
        string tapeLogPath = Path.Combine(directory, "tape.spanlog");
        if (File.Exists(tapeLogPath))
            world.Tape.MountLog(File.Open(tapeLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        (CortexSnap snap, RePairResult grammar, InstallRevision installRevision) = Checkpoint.LoadImage(
            effective, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
            world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
            world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);
        runtime.UnbindCheckpointRuntime();
        world.Loom?.UpgradeCheckpointWire();
        return Checkpoint.Encode(config, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
            snap, grammar, installRevision, world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
            world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);
    }

    private static Dictionary<string, (long Length, long LastWriteUtcTicks)> CaptureCheckpointManifest(string directory)
    {
        Dictionary<string, (long Length, long LastWriteUtcTicks)> manifest = new(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(static p => p, StringComparer.Ordinal))
        {
            FileInfo info = new(path);
            manifest[Path.GetRelativePath(directory, path)] = (info.Length, info.LastWriteTimeUtc.Ticks);
        }
        return manifest;
    }

    private static bool ManifestEquals(
        Dictionary<string, (long Length, long LastWriteUtcTicks)> expected,
        Dictionary<string, (long Length, long LastWriteUtcTicks)> actual)
    {
        if (expected.Count != actual.Count) return false;
        foreach ((string path, (long length, long mtime)) in expected)
            if (!actual.TryGetValue(path, out (long Length, long LastWriteUtcTicks) observed)
                || observed.Length != length || observed.LastWriteUtcTicks != mtime) return false;
        return true;
    }

    /// Load one committed checkpoint into a fresh Cortex graph, prove its
    /// Save∘Load∘Save identity, and hand the loaded runtime to a post-load
    /// consumer without executing a drive step. This is the recovery seam for
    /// durable terminal callbacks; callers must keep their work side-effect
    /// bounded to derived receipts and may not mutate checkpoint authority.
    internal static int LoadCheckpointRuntime(
        Cortex runtime,
        CortexRunConfig config,
        string runDirectory,
        Action<Cortex, long, long>? afterLoad = null,
        Action<byte[]>? checkpointOccurrenceCheck = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        return Drive(runtime, config, runDirectory, checkpointRuntimeOnly: true,
            afterRuntimeBindTimed: afterLoad is null ? null : (loaded, _, wall, raw) => afterLoad(loaded, wall, raw),
            checkpointOccurrenceCheck: checkpointOccurrenceCheck, verifyLoadedCheckpoint: true);
    }

    internal static Cortex CreateCheckpointRuntime(CortexRunConfig config, string? runDir = null)
    {
        if (string.Equals(config.RunName, "nav-repo", StringComparison.Ordinal))
            return RepositoryNative.CreateCheckpointRuntime(config, runDir);
        if (CortexLocCurriculum.TryParseWorkloadCount(config.Curriculum, out int workloadCount))
            return AgentSolve.CreateCheckpointRuntime(config, workloadCount, runDir);
        CortexCurriculumConfig curriculum = CreateCheckpointCurriculum(config);
        bool mountsEml = curriculum is CortexEmlCurriculum;
        bool mountsEmlActions = curriculum is CortexEmlCurriculum { Actions: not EmlActionSelections.Off };
        CortexConfig runtimeConfig = new()
        {
            RunName = config.RunName,
            EmlPairedFuelScheduleIdentity = config.EmlPairedFuelScheduleIdentity,
            DeepRematchGatePath = config.DeepRematchGatePath,
            DeepRematchGateDigest = config.DeepRematchGateDigest,
            Steps = config.Steps,
            Seed = config.Seed,
            Generation = new CortexGenerationConfig
            {
                BlockLength = config.BlockLen,
                MaxBlockBytes = config.MaxBlockBytes,
                Window = config.Window,
                NoveltyDecay = config.Lambda,
                Energy = CortexConfigTokens.ParseEnergy(config.Energy),
                AffinityFloor = config.AffFloor,
            },
            Stride = new CortexStrideConfig
            {
                ReinduceBytes = config.ReStrideBytes,
                DomainSpans = config.DomStrideSpans,
                FrontierExpansionCap = config.FrontierCapExps,
            },
            ActionsPerStep = CortexConfigTokens.ResolveActionsPerStep(config),
            Curriculum = curriculum,
            Tools = mountsEmlActions ? ReplayCalc.CreateActionTools() : null,
            ActionPolicies = mountsEmlActions ? ReplayCalc.CreateActionPolicies() : null,
            Rewards = mountsEml ? ReplayCalc.CreateRewards(mountsEmlActions) : null,
            Learning = new CortexLearningConfig
            {
                ConsolidationPhaseControl = config.ConsolidationPhaseControl,
                IntervalConsolidationPhase = config.IntervalConsolidationPhase,
                GrammarBudgetBits = config.GrammarBudgetBits,
                EvidenceWeightScale = config.WScale,
                CrossReflect = config.CrossReflect,
                ReplayRatio = config.ReplayRatio,
                SenseMask = config.SenseMask,
                Breach = config.Breach,
                Simhash = CortexConfigTokens.ParseSimhash(config.Simhash),
                NearDupe = config.NearDupe,
                Antiunify = config.Antiunify,
                WallTolerance = config.WallTol,
                Loom = config.Loom,
                Shed = config.Shed,
                Rhythm = config.Rhythm,
                Homeostat = new CortexHomeostatConfig
                {
                    Policy = config.HomeoPolicy,
                    Autonomy = config.HomeoAutonomy,
                },
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = config.PolicyDefaultMode,
                    AuthorityCeiling = config.PolicyAuthorityCeiling,
                    Overrides = config.PolicyOverrides is null ? [] : [.. config.PolicyOverrides],
                    ShadowDecisions = config.PolicyShadowDecisions,
                    ProposalInterval = config.PolicyProposalInterval,
                    ReadoutDeliberationQuota = config.ReadoutDeliberationQuota,
                    TrialHorizons = config.PolicyTrialHorizons is null ? [16, 64, 256] : [.. config.PolicyTrialHorizons],
                    TrialAllocation = config.PolicyTrialAllocationArmSteps > 0
                        ? new CortexPolicyTrialAllocationConfig
                        {
                            ArmSteps = config.PolicyTrialAllocationArmSteps,
                            Identity = config.PolicyTrialAllocationIdentity,
                            Authority = config.PolicyTrialAllocationAuthority,
                        }
                        : null,
                },
            },
            Durability = new CortexDurabilityConfig
            {
                CheckpointEvery = config.CheckpointEvery,
                CurveEvery = config.CurveEvery,
            },
            Readout = new CortexReadoutConfig
            {
                Curve = config.CurveReadout is null
                    ? null
                    : config.CurveReadout.Length == 0
                        ? []
                        : config.CurveReadout.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            },
            StopConditions = config.StopConditions is null ? [] : [.. config.StopConditions],
            AdmissionPlan = config.AdmissionPlan,
        };
        return new Cortex(runtimeConfig);
    }

    private static void RegisterRuntimePolicies(Cortex cortex, World world, CortexRunConfig config)
    {
        if (world.RhythmOn) cortex.RegisterPolicy(Rhythm.PolicySchema);
        if (Weights.IsAdaptive(config.Energy)) cortex.RegisterPolicy(WeightController.PolicySchema);
        if (world.HomeoOn)
        {
            cortex.RegisterPolicy(Homeostat.PolicySchema);
            cortex.RegisterPolicyBoundaryDomain(HomeostatPolicyBoundaryDomain.Instance);
            cortex.RegisterPolicy(Homeostat.ForecastLeadPolicySchema);
            cortex.RegisterPolicyBoundaryObligation(CreateRegisteredHomeostatBoundaryObligation(world, config));
        }
        cortex.RegisterPolicy(CortexTapeAdmission.PolicySchema);
        if (world.HomeoOn && config.HomeoAutonomy == HomeostatAutonomyModes.Full)
            cortex.RegisterPolicy(CortexPolicyGraduation.PolicySchema);
        world.Curriculum.RegisterPolicies(cortex);
    }

    private static CortexCurriculumConfig CreateCheckpointCurriculum(CortexRunConfig config)
    {
        if (CortexWeftCurriculum.TryParseToken(config.Curriculum, out CortexWeftCurriculum weft))
        {
            return weft with
            {
                IntakeBatch = config.IntakeBatch,
                SeedSpans = config.SeedSpans,
                MixEvery = config.MixEvery,
                AffirmGate = config.AffirmGate,
                GrokCv = config.GrokCv,
                LockRounds = config.LockRounds,
            };
        }
        if (EmlActionSelectionTokens.IsEmlCurriculum(config.Curriculum))
        {
            EmlActionSelections selection = EmlActionSelectionTokens.ParseCurriculumToken(config.Curriculum);
            EmlKnobs eml = config.Eml.Equals(default) ? EmlKnobs.Mount : config.Eml;
            return new CortexEmlCurriculum
            {
                Actions = selection,
                Corpus = string.IsNullOrWhiteSpace(config.CorpusPath)
                    ? null
                    : new CogitoCorpus { Path = config.CorpusPath, Glob = config.Glob, ExpectedWorldSHA256 = config.ExpectedWorldSHA256 },
                IntakeBatch = config.IntakeBatch,
                SeedSpans = config.SeedSpans,
                MixEvery = config.MixEvery,
                AffirmGate = config.AffirmGate,
                GrokCv = config.GrokCv,
                LockRounds = config.LockRounds,
                SignatureDigits = config.EmlSignatureDigits,
                HoldoutFraction = config.EmlHoldoutFraction,
                HoldoutSeed = config.EmlHoldoutSeed,
                TargetCatalog = config.EmlTargetCatalog,
                GrammarSampling = config.EmlGrammarSampling,
                ProcessCatalog = config.EmlProcessCatalog,
                Rung0 = config.EmlRung0,
                Deliberation = config.EmlDeliberation,
                DeliberationBudget = config.EmlDeliberationBudget == default ? EmlDeliberationQuota.Default : config.EmlDeliberationBudget,
                Generation = new EmlGenerationConfig
                {
                    SeedShells = eml.SeedK,
                    MaxLength = eml.MaxLen,
                    MaxEnumerationLength = eml.MaxEnum,
                    SampleUnits = eml.Units,
                    ChunkGain = eml.Gain,
                    UniformEpsilon = eml.Eps,
                    EnumerationEpsilon = eml.EpsEnum,
                    CorroborationWeight = eml.CorrobW,
                    CertificateWeight = eml.CertW,
                },
                Lift = new EmlLiftGateConfig
                {
                    MaxRuler = eml.Lift.KMax,
                    Factor = eml.Lift.Factor,
                    Window = eml.Lift.Window,
                    Sustain = eml.Lift.Sustain,
                    Fraction = eml.Lift.Frac,
                    MeanzBand = eml.Lift.MeanzBand,
                    CensusOnly = eml.Lift.CensusOnly,
                    LockMeanz = eml.Lift.LockMeanz,
                },
            };
        }

        CogitoCorpus corpus = new() { Path = config.CorpusPath, Glob = config.Glob, ExpectedWorldSHA256 = config.ExpectedWorldSHA256 };
        return config.Curriculum switch
        {
            "flatpool" => new CortexFlatPoolCurriculum
            {
                Corpus = corpus,
                IntakeBatch = config.IntakeBatch,
                SeedSpans = config.SeedSpans,
                MixEvery = config.MixEvery,
                AffirmGate = config.AffirmGate,
                GrokCv = config.GrokCv,
                LockRounds = config.LockRounds,
            },
            "grokbell" => new CortexGrokBellCurriculum
            {
                Corpus = corpus,
                IntakeBatch = config.IntakeBatch,
                SeedSpans = config.SeedSpans,
                MixEvery = config.MixEvery,
                AffirmGate = config.AffirmGate,
                GrokCv = config.GrokCv,
                LockRounds = config.LockRounds,
            },
            "campfire" => new CortexCampfireCurriculum
            {
                Corpus = corpus,
                IntakeBatch = config.IntakeBatch,
                SeedSpans = config.SeedSpans,
                MixEvery = config.MixEvery,
                AffirmGate = config.AffirmGate,
                GrokCv = config.GrokCv,
                LockRounds = config.LockRounds,
            },
            _ => throw new InvalidOperationException($"curriculum '{config.Curriculum}' cannot materialize a Cortex fork"),
        };
    }

    /// PHASE 0 as ONE deterministic construction — the corpus world + every state-bearing organ, identical on the
    /// fresh and resume paths. The checkpoint's corpus guard (GUAR) is only meaningful because both paths build
    /// THIS world in THIS order — a second hand-kept copy of the construction would drift exactly
    /// where byte-identity dies. Owns the Tape (Dispose closes the mounted event byte log) and the Loom arena.
    private sealed class World : IDisposable
    {
        // ── the corpus world ──  the intake POOL + held-out probe. CorpusPath may be a DIRECTORY: then the
        // domain-structured FileCorpus (each file = a domain, the GrokBell family unit; every 8th line held out)
        // is built ONCE and its lines flatten to the pool — and that SAME FileCorpus is handed to GrokBell, so
        // `farm <dir> --cortex --curriculum grokbell` reads the tree exactly once (2b's double corpus read killed).
        // A single FILE keeps the flat every-10th-line SplitPool (the day-one path); grokbell also needs the
        // family structure, so it builds a FileCorpus (one file → one domain) too.
        public readonly FileCorpus? Fc;
        public readonly AdmissionCursor? ExternalWorld;
        public readonly int ExternalWorldFamilies;
        public readonly List<byte[]> Pool;
        public readonly byte[] Heldout;
        public readonly byte[] Probe;                                  // the READ's generalization probe (held-out, else the pool) — resolved once, drive-constant
        public readonly long CorpusBytes;
        // ── the organs ──
        public readonly Tape Tape;                                     // owns the event byte log stream once mounted
        public readonly Journal Journal;
        public readonly ICurriculum Curriculum;
        public readonly EnergyPolicy Energy;
        public readonly Metabolism Metabolism;
        public readonly Reads Reads;
        public readonly SelfStream SelfStream;
        public readonly WeightController Controller;
        public readonly Homeostat Homeo;
        public readonly bool HomeoOn;
        public readonly SenseMask Mask;
        public readonly Rhythm Rhythm;
        public readonly bool RhythmOn;
        public readonly MemoryHierarchy Memory;
        public readonly Seriate.WeaveModel Weave;
        public readonly Loom? Loom;
        public readonly CogitoWorkspace Workspace;
        public readonly long Mem0, MemWorld, MemCurr;                  // memstat phase brackets — 0 when dark (never a forced GC)

        public World(CortexRunConfig cfg, bool memstat = false, bool verifyExpectedWorld = true, AdmissionPlan? worldAdmissionPlan = null)
        {
            Tape.RequireWScale(cfg.WScale);                            // the power-of-two law — ONE guard for every entry (CLI, resume, programmatic)
            Mem0 = memstat ? MemStat.Managed() : 0;
            bool hasCorpus = !string.IsNullOrWhiteSpace(cfg.CorpusPath);
            FileCorpus.ValidateExpectedWorldSHA256(cfg.ExpectedWorldSHA256);
            if (verifyExpectedWorld && cfg.ExpectedWorldSHA256.Length > 0)
            {
                if (!hasCorpus) throw new InvalidDataException("expected world SHA-256 is registered without a corpus path");
                string actualWorld = FileCorpus.ComputeWorldSHA256(cfg.CorpusPath, cfg.Glob);
                if (!string.Equals(actualWorld, cfg.ExpectedWorldSHA256, StringComparison.Ordinal))
                    throw new InvalidDataException($"corpus world SHA-256 drifted: expected {cfg.ExpectedWorldSHA256}, observed {actualWorld}");
            }
            bool hasRuntimeCurriculum = cfg.RuntimeCurriculum is not null;
            bool isEmlCurriculum = EmlActionSelectionTokens.IsEmlCurriculum(cfg.Curriculum);
            if (!hasRuntimeCurriculum && isEmlCurriculum)
                throw new InvalidOperationException("EML requires the ReplayCalc runtime mounted by Cortex");
            if (!hasRuntimeCurriculum && !hasCorpus && !isEmlCurriculum)
                throw new ArgumentException($"{cfg.Curriculum} curriculum requires a corpus path");

            bool useHf = !hasRuntimeCurriculum && hasCorpus && IsHfDatasetPath(cfg.CorpusPath);
            if (useHf && cfg.Curriculum is not "flatpool")
                throw new ArgumentException("hf:// corpus sources currently feed the flatpool Radula intake; grokbell/campfire need file-domain structure.");

            bool useFc = !hasRuntimeCurriculum && hasCorpus && !useHf && (Directory.Exists(cfg.CorpusPath) || cfg.Curriculum is "grokbell" or "campfire");   // the belled curricula need the family-structured corpus; a directory can't be File.ReadAllBytes'd
            Fc = useFc ? new FileCorpus(cfg.CorpusPath, cfg.Glob, holdEvery: 8, poolOrder: "blocked", cfg.Seed) : null;
            ExternalWorld = hasRuntimeCurriculum && hasCorpus && !useHf && cfg.ExpectedWorldSHA256.Length > 0
                ? new AdmissionCursor(cfg.CorpusPath, cfg.Glob, worldAdmissionPlan)
                : null;
            ExternalWorldFamilies = ExternalWorld?.Families ?? 0;
            if (Fc is not null)
            {
                Pool = new List<byte[]>(Fc.Lines.Count);
                foreach (var (_, b) in Fc.Lines) Pool.Add(b);
                Heldout = ConcatSpans(Fc.Heldout);
                CorpusBytes = 0; foreach (var b in Pool) CorpusBytes += b.Length; CorpusBytes += Heldout.Length;
            }
            else if (useHf)
            {
                var remote = LoadHfDatasetCorpus(cfg.CorpusPath);
                Pool = new List<byte[]>(remote.Lines);
                Heldout = ConcatPool(remote.Heldout);
                CorpusBytes = remote.TextBytes;
            }
            else if (hasRuntimeCurriculum)
            {
                int workloadCount = Math.Max(1, cfg.RuntimeCurriculum!.WorkloadCount);
                Pool = new List<byte[]>(workloadCount);
                cfg.RuntimeCurriculum.AppendProbeSamples(Pool);
                if (Pool.Count == 0)
                    throw new InvalidDataException($"runtime curriculum {cfg.RuntimeCurriculum.GetType().Name} supplied no probe samples");
                Heldout = Array.Empty<byte>();
                long runtimeBytes = 0;
                foreach (byte[] sample in Pool) runtimeBytes += sample.Length;
                CorpusBytes = runtimeBytes;
            }
            else
            {
                var corpus = File.ReadAllBytes(cfg.CorpusPath);
                (Pool, Heldout) = SplitPool(corpus);
                CorpusBytes = corpus.Length;
            }
            Probe = Heldout.Length > 0 ? Heldout : ConcatPool(Pool);
            MemWorld = memstat ? MemStat.Managed() : 0;                // bracket: corpus → pool + probe
            Tape = new Tape();
            Journal = new Journal();
            Curriculum = cfg.RuntimeCurriculum ?? cfg.Curriculum switch
            {
                "grokbell" => new GrokBell(Fc!, cfg.GrokCv, cfg.LockRounds, cfg.Seed, cfg.MixEvery, cfg.DomStrideSpans, cfg.FrontierCapExps),   // the SHARED FileCorpus (no re-read) — CritLock's CV-lock + bridge-order, promoted (CortexRunConfig owns the stride/cap knobs)
                "campfire" => new Campfire(Fc!, cfg),                         // the 3-way: GrokBell over code+NL (extrinsic) + the EML replay-calculator (intrinsic), vest-gated
                "flatpool" => new FlatPool(Pool, cfg.SeedSpans, cfg.MixEvery),
                _          => throw new ArgumentException($"unknown --curriculum '{cfg.Curriculum}' (flatpool|grokbell|eml|campfire) — a typo'd scheduler must never silently flatpool"),
            };
            if (!string.IsNullOrEmpty(cfg.EmlPairedFuelScheduleIdentity))
            {
                if (Curriculum is not ReplayCalc pairedFuelReplay)
                    throw new InvalidDataException("paired fuel schedule identity requires the EML curriculum");
                EmlDeliberationQuota pairedFuelQuota = cfg.EmlDeliberationBudget == default
                    ? EmlDeliberationQuota.PairedGateNominal
                    : cfg.EmlDeliberationBudget;
                pairedFuelReplay.ConfigurePairedFuelSchedule(cfg.Steps, in pairedFuelQuota, cfg.EmlPairedFuelScheduleIdentity);
            }
            Workspace = new CogitoWorkspace();
            Workspace.Define(
                "cortex.loom.rules",
                "cortex.loom.symbols",
                "cortex.loom.mdl_saved",
                "cortex.loom.publish_lag_bytes",
                "cortex.tape.resident",
                "cortex.tape.shed",
                "cortex.tape.execution",
                "cortex.tape.born_evidence",
                "cortex.tape.unreflected_dreams",
                "cortex.homeostat.authority",
                "cortex.homeostat.cached_contexts",
                "cortex.homeostat.shadow_agreement",
                "cortex.homeostat.takeover_executions",
                "cortex.homeostat.paid_takeovers",
                "cortex.homeostat.repromotions");
            Curriculum.DefineWorkspace(Workspace);
            foreach (CortexStopCondition condition in cfg.StopConditions ?? [])
            {
                if (string.IsNullOrWhiteSpace(condition.Selector)) throw new ArgumentException("stop selector cannot be blank");
                if (double.IsNaN(condition.AtLeast)) throw new ArgumentException($"stop selector '{condition.Selector}' has a NaN threshold");
                Workspace.RequireKey(condition.Selector);
            }
            MemCurr = memstat ? MemStat.Managed() : 0;                 // bracket: curriculum build (frontier postings + sieve targets)
            Energy = new EnergyPolicy(cfg.Energy, cfg.AffFloor, new LineModel(Pool));   // FREEZE the line-length model to the REAL pool — the generator reproduces reference line structure, immune to per-stride fragmentation drift
            Metabolism = new Metabolism(cfg.Lambda);
            Reads = new Reads();
            SelfStream = new SelfStream();
            // the controller's post-grok cooling gate keeps its own flat 0.20 on the WHOLE-grammar cvZ — it is NOT the
            // per-domain bell (now floor + k-band, cfg.GrokCv = the floor). The homeostat OWNS the k-aware whole-grammar
            // lock (its QUIET condition) and the C2 cvz phase-mask; the fast plane below keeps its flat gate but reads
            // the MASKED cvz when the homeostat arms, so a breach-heated reading can never cool it.
            Controller = new WeightController(Energy.Weights, adaptive: Weights.IsAdaptive(cfg.Energy));

            // ── THE HOMEOSTAT (face 1) ──  the slow interoceptive plane composed OVER the fast plane (the
            // WeightController IS Homeostat.Fast — one generation limb, two control cadences). Constructed ALWAYS so
            // its state rides the checkpoint uniformly; CONSULTED under homeostatic consolidationPhase.
            Homeo = new Homeostat(Controller, new HomeoActuation(
                SleepFrac: RestSleepFrac, MixEvery: cfg.MixEvery, IntakeBatch: cfg.IntakeBatch,
                BudgetBits: cfg.GrammarBudgetBits, BreachQuota: 0, ForceGeneralize: false),
                // Heavy's growth-anomaly band = the machine's own append ceiling: the mint cadence + the intake
                // actuator's 4× clamp max (the homeostat's own UpI bound). Growth the drive's laws can produce is
                // cadence, not anomaly — the literal-4 band sat AT MintSpansPerStep and Heavy shadowed Stalled forever.
                mintParity: MintSpansPerStep + 4 * cfg.IntakeBatch,
                // the policy tier (SELFMODEL wave): Reflex = the banked table byte-identically; the knobs climb it.
                policy: cfg.HomeoPolicy,
                seed: cfg.Seed,
                autonomy: cfg.HomeoAutonomy);
            HomeoOn = cfg.ConsolidationPhaseControl == CortexConsolidationPhaseControl.Homeostat;
            Mask = SenseMask.Parse(cfg.SenseMask);                     // fail-loud at entry — a typo'd ablation arm must never run silently unmasked
            if (Mask.Any && !HomeoOn)
                throw new ArgumentException("SenseMask requires homeostatic consolidationPhase; the interval controller has no slow-plane senses to mask");
            if ((cfg.HomeoPolicy != HomeoPolicies.Reflex || cfg.HomeoAutonomy != HomeostatAutonomyModes.Off) && !HomeoOn)
                throw new ArgumentException("Homeostat policy and autonomy require homeostatic consolidationPhase");

            // ── THE RHYTHM (emergent metabolism — Rhythm.cs) ──  the per-step day/replay/consolidationPhase scheduler. Constructed
            // ALWAYS so its state rides the checkpoint uniformly (the homeostat pattern). ConsolidationPhase rides the
            // homeostat's byte-stride surprise clock, so Rhythm requires the slow plane.
            RhythmOn = cfg.Rhythm;
            if (RhythmOn && !HomeoOn)
                throw new ArgumentException("Rhythm requires homeostatic consolidationPhase because its cadence and senses ride the slow plane");
            Rhythm = new Rhythm(ConsolidationPhase.DropUnvestedAfterEvents);         // the cohort verdict horizon IS the drop horizon: by then a replay either vested or left the view
            if (RhythmOn && cfg.WScale == 1)
                Trace.Cortex.Warn("rhythm.dark", "wScale=1: vests never fire, so the replay-outcome plane is DARK — ε anneals on its step ceiling alone and the worth's outcome gain stays neutral (arm --wscale ≥2 for the full self-referential loop)");

            Memory = new MemoryHierarchy(demotable: cfg.GrammarBudgetBits > 0);   // the working-set organ — GC-demotion + reverse index, persistent across the run's sleep passes; an unbounded budget (0 stays 0 — a config MODE) never demotes, so the containment postings stay unfed
            Weave = new Seriate.WeaveModel();                         // standing sleep basis/coupling model; first sleep seeds it, later weaves score only touched spans
            // THE LOOM; null = the stride-gated batch arm (--no-loom, the
            // differential oracle). wScale>1 rides: a span splices at its CURRENT evidence weight, corroboration fires
            // only inside the consolidationPhase's Consolidate, and the delta application that immediately follows re-prices post-vest
            // status — the live count plane always equals what a fresh Load re-derives (resume-exact at any wScale).
            Loom = cfg.Loom ? new Loom(256, '\n', cfg.WScale) : null;
        }

        public void Dispose()
        {
            try { ExternalWorld?.Dispose(); }
            finally
            {
                try { Tape.Dispose(); }
                finally
                {
                    try { Loom?.Dispose(); }
                    finally { Journal.Dispose(); }
                }
            }
        }
    }

    private static bool IsHfDatasetPath(string path) =>
        path.StartsWith("hf://", StringComparison.OrdinalIgnoreCase);

    private static DatasetTextCorpus LoadHfDatasetCorpus(string source)
    {
        var options = HfDatasetRowsOptions.FromUri(new Uri(source, UriKind.Absolute));
        if (options.MaxRows is null)
            throw new ArgumentException("hf:// corpus sources require maxRows, rows, or limit because Radula builds a finite frontier pool.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Cogito/Datasets.cs");
        var stream = new HfDatasetRowsStream(http, options);
        return DatasetTextCorpus.MaterializeAsync(stream, holdEvery: 10).GetAwaiter().GetResult();
    }

    /// The one drain loop, config in hand. Public so the multi-node study (and tests) can call it without the CLI.
    /// `resumeDir` non-null = continue a checkpointed run: PHASE 0 rebuilds the world deterministically from the
    /// config, then the checkpoint restores every organ and the loop continues where the kill cut it — the
    /// continuation is byte-identical to the straight-through run (the resume contract, Checkpoint.cs). `verifyOnly`
    /// loads, re-encodes, byte-compares, and reports — the round-trip check without driving a step.
    private static int Drive(Cortex cortex, CortexRunConfig cfg, string? resumeDir = null, bool verifyOnly = false, bool memstatOnly = false,
        bool consolidationPhaseProbe = false, bool allowRuntimeWorldFork = false, Action<Cortex>? interveneAfterLoad = null,
        bool runToAbsoluteHorizon = false, bool verifyLoadedCheckpoint = false, bool checkpointRunEnd = false,
        bool checkpointRuntimeOnly = false,
        Action? observeRuntimeStop = null, CortexForkAnytimeIdentity? anytimeIdentity = null,
        Action<byte[]>? checkpointOccurrenceCheck = null,
        Action<Cortex, CortexExecutionWindow>? afterRuntimeBind = null,
        Action<Cortex, CortexExecutionWindow, long, long>? afterRuntimeBindTimed = null,
        CortexExecutionWindow? executionWindow = null,
        Action<Cortex, int>? afterCompletedStep = null,
        Action<Cortex, int>? afterCompletedStepEveryStep = null,
        Action<Cortex, int>? beforeCompletedStep = null,
        Action<Cortex>? captureCompletionBeforeWorldDispose = null, Run? destination = null,
        bool prepareOnly = false)
    {
        // ── PHASE 0 · SETUP ──  the shared deterministic construction (World above); locals alias its organs so
        // the loop reads as it always did.
        if (cortex.AdmissionPlan is not null && cfg.AdmissionPlan is not null
            && cortex.AdmissionPlan.AuthorityDigest != cfg.AdmissionPlan.AuthorityDigest)
            throw new InvalidDataException("cortex world encounter plan disagrees with checkpoint config");
        using var world = new World(cfg, memstatOnly, worldAdmissionPlan: cfg.AdmissionPlan);
        var fc = world.Fc; var pool = world.Pool; var heldout = world.Heldout; var probe = world.Probe;
        long corpusBytes = world.CorpusBytes;
        var tape = world.Tape; var journal = world.Journal; var curriculum = world.Curriculum;
        cortex.BindLoopLineage(tape, journal, curriculum.LineageWorldRootPredicate);
        var energy = world.Energy; var metabolism = world.Metabolism; var reads = world.Reads;
        var selfModel = world.SelfStream; var controller = world.Controller;
        var homeo = world.Homeo; bool homeoOn = world.HomeoOn; var senseMask = world.Mask;
        var rhythm = world.Rhythm; bool rhythmOn = world.RhythmOn;
        var memory = world.Memory; var loom = world.Loom;
        PearlAuditCache auditCache = new();
        var workspace = world.Workspace;
        CogitoReadout curveReadout = cfg.CurveReadout is null
            ? workspace.Select(string.Join(',', CortexReadoutConfig.CreateDefaultCurve(
                world.Curriculum is ReplayCalc,
                world.Curriculum is ReplayCalc { ActionSelection: not EmlActionSelections.Off })))
            : cfg.CurveReadout.Length == 0
                ? workspace.Select(Array.Empty<string>())
                : workspace.Select(cfg.CurveReadout);
        long mem0 = world.Mem0, memWorld = world.MemWorld, memCurr = world.MemCurr;
        bool resuming = resumeDir is not null;
        AdmissionReceipt lastWorldAdmission = default;
        StreamWriter? worldAdmissionW = null;
        void AdmitWorldAtStep(int admissionStep)
        {
            if (world.ExternalWorld is not { TotalItems: > 0 } admittedWorld) return;
            AdmissionReceipt admission = admittedWorld.Admit(tape, journal, admissionStep, cfg.IntakeBatch);
            admission.Validate();
            lastWorldAdmission = admission;
            worldAdmissionW?.WriteLine(admission.ToTsv());
            curriculum.BindWorldOpportunityEvents(admittedWorld.EventIDs);
        }
        RegisterRuntimePolicies(cortex, world, cfg);
        if (!resuming)
        {
            int runtimeWorldCount = world.ExternalWorld?.TotalItems ?? 0;
            TapePacketCreator.AppendWorldManifest(tape, journal, curriculum.WorkloadCount + runtimeWorldCount);
            AdmitWorldAtStep(0);
            curriculum.Seed(tape, journal);                            // bootstrap the anchor so the residual can discriminate (a resumed tape already carries it)
        }
        if (resuming && destination is not null)
            throw new InvalidOperationException("a resumed Cortex cannot also provide a fresh run destination");
        var run = resuming ? Cogito.Run.Open(resumeDir!) : destination ?? Cogito.Run.New(cfg.RunName);
        if (!checkpointRuntimeOnly)
            DeepRematchGate.BindRun(run, cfg.DeepRematchGatePath, cfg.DeepRematchGateDigest);
        if (loom is not null && cfg.Shed)                              // the event byte log — shed bytes' durable home (mounted BEFORE the resume load: shed entries resolve through it)
            tape.MountLog(new FileStream(run.PathOf("tape.spanlog"), checkpointRuntimeOnly ? FileMode.Open : FileMode.OpenOrCreate,
                checkpointRuntimeOnly ? FileAccess.Read : FileAccess.ReadWrite));

        // ── PHASE 1 · THE DRIVE ──  the ONE loop. INDUCE is stride-gated (the O(n²) fix); the intake→autoregressive
        // fork is the developmental curriculum (drain the scaffold, then replay + MIX-anchor to extrinsic reality).
        // FRESH: induce the seeded tape once. RESUME: the checkpoint restores every organ + the loop's own locals —
        // including the live grammar, which is NOT re-derivable by induction (between sleeps it carries the
        // consolidation's TapeRef demotions + AntiUnify slots layered over the Re-Pair output).
        RePairResult g;
        long lastInduceBytes;                                          // the grammar-refresh anchor — batch arm: re-induce stride; loom arm: HARVEST stride
        long lastSpliceBytes = 0;                                      // loom fold accounting anchor (opb denominator only — control rides TapeDelta's revision/high-water; set to the live tape below, both paths)
        int wallStreak = 0;                                            // consecutive flat momentum readings on the STRIDE clock (both arms — the grammar refresh IS the stride)
        int lastConsolidationPhaseRules = -1;                                       // rule count at the last sleep close (−1 = no consolidationPhase yet) — the Δnodes_dream anchor
        int lastDNodes = 0;                                            // Δ(rules) across the last replay/sleep cycle — the convergence-invariant curve column
        int totalEvicted = 0, totalPromoted = 0;                       // cumulative memory-hierarchy activity → the reserved LossReading columns
        int totalDemoted = 0;                                          // cumulative GC demotions (literals → tape-refs) — the REFACTOR numerator the loop computed then discarded (the motion suite's demoted column); telemetry-grade (re-accumulates post-resume, like the converge trace's mem=)
        int lastBirths = 0, lastChurn = 0;                             // the LAST consolidationPhase's motion — rule births (Δrules up) + total structural churn (evict+promote+demote+shed+drop) — the refactor-verdict inputs, held between consolidationPhases, reset on the first post-resume consolidationPhase (stale-between-consolidationPhases display, never determinism-load-bearing)
        int lastSlotted = 0; long lastBitsSaved = 0;                   // anti-unification gauge — the last sleep pass's slot-rule count + bits-saved-by-generalization (the sparkline)
        long lastSleepBytes;                                           // geometric sleep-stride anchor (the homeostat's SleepDue; maintained both arms — passive when off)
        double lastInduceOpb = 0;                                      // merges/Δbyte at the last induce, held between strides (the Hot cost sense)
        double lastBitsPerSpan = 0;                                    // grammar surface bits / REAL span at the last sleep (the Heavy sense, honest denominator)
        long prevTapeCount;                                            // last step's span count (the GrowthRate sense)
        int breachConsolidationPhases = 0;                                          // consolidationPhases whose breach fired
        int breachWindowResets = 0;                                    // DomainMeter streak-resets landing INSIDE a cvz-mask window (C2 kill-line: must stay 0)
        int lastStreakResets = 0;                                      // the window-attribution edge detector (re-derived from the curriculum at resume — its meters checkpoint the total)
        int forkStep = -1;                                             // the step the replay-fork OPENED (intake-exhaustion crossed); −1 = still eating the world
        double forkVolumeFrac = double.NaN;                            // pool coverage frozen at the fork — the self-regulation readout (curve: fork_vol_frac)
        bool mintThrottleAnnounced = false;                            // one-time replay-fraction-cap telegraph (trace-only — not checkpointed; a resume re-announces once)
        int step0 = 0;                                                 // the first step this process executes (resume continues mid-arc)
        int checkpointHorizon = 0;                                     // previous durable keyframe; typed mutation records span this horizon
        bool mutationRailReady = false;
        bool tracedFirstLiveLoomDrain = false;
        GrammarArtifactDelta checkpointGrammarArtifact = default;
        ulong loadedGrammarRevision = 0;
        InstallRevision? loadedInstallRevision = null;
        bool checkpointProofExact = false;
        long checkpointLoadStarted = 0;
        long checkpointLoadWallMilliseconds = 0;
        long checkpointLoadRawTicks = 0;
        if (resuming)
        {
            // Policy keyframes may shed their allocation prefix from RAM. Bind the run before decoding
            // the image so LoadPolicyState can authenticate funding decisions against that durable TSV prefix.
            cortex.BindCheckpointAuthority(run);
            if (checkpointRuntimeOnly) checkpointLoadStarted = Stopwatch.GetTimestamp();
            var (snap, gg, restoredInstallRevision) = Checkpoint.Load(run.Dir, corpusBytes, pool.Count, fc?.Families ?? 0,
                tape, journal, curriculum, reads, selfModel, controller, metabolism, memory, homeo, loom, rhythm,
                cortex,
                allowRuntimeWorldFork, readOnlyEffectiveImage: checkpointRuntimeOnly);
            if (world.ExternalWorld is { TotalItems: > 0 } restoredWorld)
            {
                IReadOnlyList<TapeEventID> restoredEvents = TapePacketCreator.ReadWorldEncounterEventIDs(tape);
                restoredWorld.Restore(restoredEvents.Count, restoredEvents);
                curriculum.BindWorldOpportunityEvents(restoredEvents);
                lastWorldAdmission = new AdmissionReceipt(
                    step0, restoredWorld.Cursor, restoredWorld.Cursor, 0, 0, 0,
                    restoredWorld.Remaining, restoredWorld.TotalItems, restoredWorld.IsTerminal,
                    restoredWorld.ComputeDigest(), restoredWorld.ActiveScheduleID,
                    restoredWorld.ActiveScheduleDigest, 0,
                    restoredWorld.EmptyActiveDomainDigest());
            }
            loadedGrammarRevision = snap.GrammarRevision;
            loadedInstallRevision = restoredInstallRevision;
            checkpointHorizon = snap.NextStep;
            mutationRailReady = File.Exists(run.PathOf(Checkpoint.DeltaFileName));
            Trace.Cortex.Boundary("loom.resume.bound",
                $"step={snap.NextStep} tape={tape.MutationCursor} loom_mark={(loom is null ? -1 : loom.SpliceIDMark)} loom_revision={(loom is null ? -1 : loom.MutationRevision)} loom_arena={(loom is null ? -1 : loom.LiveSymbols)}");
            if (verifyLoadedCheckpoint)
            {
                // A matched fork verifies its seed in the same World that will drive it. The checkpoint carries the
                // previous horizon; encoding with the extended runtime horizon would manufacture a false mismatch.
                CortexRunConfig checkpointConfig = Checkpoint.PeekConfig(run.Dir);
                byte[] image = Checkpoint.Encode(checkpointConfig, corpusBytes, pool.Count, fc?.Families ?? 0, snap,
                    g: gg, restoredInstallRevision, tape, journal, curriculum, reads, selfModel, controller, metabolism, memory, homeo, loom, rhythm, cortex);
                byte[] disk = checkpointRuntimeOnly ? CheckpointDelta.ReadEffectiveSnapshot(run.Dir).EffectiveImage : Checkpoint.LoadEffectiveImage(run.Dir);
                if (!image.AsSpan().SequenceEqual(disk))
                {
                    Trace.Cortex.Warn("fork.verify", $"checkpoint round-trip diverged before drive · {run.Dir}");
                    return 1;
                }
                checkpointOccurrenceCheck?.Invoke(image);
                checkpointProofExact = true;
                Trace.Cortex.Boundary("fork.verify", $"checkpoint round-trip byte-exact before drive · {image.Length}B · step {snap.NextStep}");
            }
            if (verifyOnly)
            {
                // the round-trip check: loaded state re-encodes to the EXACT file bytes, or the
                // serialization is lossy and every resume built on it is worthless. No file is touched.
                var image = Checkpoint.Encode(cfg, corpusBytes, pool.Count, fc?.Families ?? 0, snap,
                    g: gg, restoredInstallRevision, tape, journal, curriculum, reads, selfModel, controller, metabolism, memory, homeo, loom, rhythm, cortex);
                var disk = Checkpoint.LoadEffectiveImage(run.Dir);
                checkpointOccurrenceCheck?.Invoke(image);
                bool ok = image.AsSpan().SequenceEqual(disk);
                int firstDifference = -1;
                if (!ok)
                {
                    int commonLength = Math.Min(image.Length, disk.Length);
                    for (int i = 0; i < commonLength; i++)
                        if (image[i] != disk[i]) { firstDifference = i; break; }
                    if (firstDifference < 0) firstDifference = commonLength;
                }
                Console.WriteLine(ok
                    ? $"  ✓ checkpoint round-trip byte-exact — {image.Length}B · step {snap.NextStep} · tape {tape.Count} spans · journal {journal.LineCount} lines (Save∘Load∘Save = identity)"
                    : $"  ✗ checkpoint round-trip DIVERGED — re-encoded {image.Length}B vs disk {disk.Length}B · first byte {firstDifference} (lossy state serialization; the Vow is broken)");
                return ok ? 0 : 1;
            }
            if (memstatOnly)
            {
                // ── THE MEMORY CENSUS (diagnostic — same no-file-touched contract as --verify) ──  the loaded
                // machine IS the live drive's resident state (PHASE 0 world + every organ + the derived indexes
                // Load rebuilds); one Generate arms the per-stride energy caches (couplings · scorers · CSR ·
                // transitions · generator) exactly as a live stride holds them. Brackets are exact per phase;
                // the structure walk decomposes them (MemStat.Render's Total-Accounting footer).
                long memLoad = MemStat.Managed();                                      // bracket: checkpoint load (organs + derived index rebuild)
                _ = energy.Generate(gg, cfg.BlockLen, cfg.Seed + (ulong)snap.NextStep, metabolism, controller.Current);
                long memEnergy = MemStat.Managed();                                    // bracket: energy stride caches armed
                var rows = MemStat.Census(tape, memory, gg, journal, curriculum, loom, energy.TransEvidence);
                Console.WriteLine($"  memstat · {run.Dir} · step {snap.NextStep} · tape {tape.Count} spans ({tape.ByteLength}B) · rules {gg.Rules.Length} · homeostat reversals {homeo.SignReversals}");
                Console.Write(MemStat.Render(rows,
                [
                    ("world (pool+probe)", memWorld - mem0),
                    ("curriculum build", memCurr - memWorld),
                    ("checkpoint load", memLoad - memCurr),
                    ("energy stride caches", memEnergy - memLoad),
                ], memEnergy));
                return 0;
            }
            g = gg;
            step0           = snap.NextStep;
            lastInduceBytes = snap.LastInduceBytes;
            wallStreak      = snap.WallStreak;
            lastConsolidationPhaseRules  = snap.LastConsolidationPhaseRules; lastDNodes = snap.LastDNodes;
            totalEvicted    = snap.TotalEvicted;  totalPromoted = snap.TotalPromoted;
            lastSlotted     = snap.LastSlotted;   lastBitsSaved = snap.LastBitsSaved;
            lastSleepBytes  = snap.LastSleepBytes;
            lastInduceOpb   = snap.LastInduceOpb; lastBitsPerSpan = snap.LastBitsPerSpan;
            prevTapeCount   = snap.PrevTapeCount;
            breachConsolidationPhases    = snap.BreachConsolidationPhases;  breachWindowResets = snap.BreachWindowResets;
            forkStep        = snap.ForkStep;      forkVolumeFrac = snap.ForkVolumeFrac;
            lastStreakResets = curriculum.StreakResets;                // the meters carry the total; the edge detector re-anchors on it
            if (!consolidationPhaseProbe && !checkpointRuntimeOnly)
            {
                run.TruncateCurve("curve.tsv", snap.CurveLen); // shed rows a kill appended past the snapshot horizon (probe: leave the run dir untouched)
                run.TruncateCurveByLeadingStep("compute.tsv", snap.NextStep); // telemetry has no serialized byte horizon; its logical step is the resume authority
            }
            Trace.Note($"cortex ⇄ RESUME at step {step0}/{cfg.Steps} · tape {tape.Count} spans ({tape.ByteLength}B) · ingested {curriculum.IngestedCount}/{pool.Count} · journal {journal.LineCount} lines · grammar {g.Rules.Length} rules");

            // ── THE CONSOLIDATION_PHASE PROBE (diagnostic) ──  run ONE Consolidate over the loaded state — the per-organ walls
            // of the next consolidationPhase this checkpoint would pay (the sleep.sub boundary) — then exit without driving a
            // step, mounting the journal, or cutting a checkpoint. NOT no-touch: Evacuate appends shed bytes to
            // the mounted tape.spanlog — probe a COPY of the run dir, never the original.
            if (consolidationPhaseProbe)
            {
                long probeBudget = homeoOn ? homeo.BudgetBits : cfg.GrammarBudgetBits;
                int probeQuota = homeoOn && cfg.Breach ? homeo.BreachQuota : 0;
                var probePass = Consolidate(tape, g, memory, journal, step0, cfg, probeBudget, probeQuota, cfg.Seed + (ulong)step0, loom, weaveModel: world.Weave, auditCache: auditCache);
                Console.WriteLine($"  consolidationPhase-probe · step {step0} · view {tape.ByteLength}B ({tape.Count} residents + {tape.ShedEventIDs.Count} shed) · rules {g.Rules.Length} → {probePass.Grammar.Rules.Length} · per-organ walls on the sleep.sub trace boundary above");
                return 0;
            }
        }
        else
        {
            if (loom is not null)                                             // THE LOOM seed: splice the seeded tape + pump — same traced bootstrap, the persistent lineage's first food
            {
                var seedEvents = new List<MergeEvent>();
                FoldLoomDelta(tape, loom, seedEvents);
                g = loom.Result(tape);
                selfModel.ObserveThought(seedEvents, 256);
            }
            else
            {
                var (g1, seedEvents, _) = InduceOutcomeCredited(tape, journal, step: 0, cfg.WScale, cfg.CrossReflect, auditCache);   // induce the seeded tape once (traced — bootstrap the thought channel; grammar byte-identical to Induce)
                g = g1;
                selfModel.ObserveThought(seedEvents, 256);                    // the seed cognition is the thought channel's first food
            }
            lastInduceBytes = tape.GrammarByteLength;
            lastSleepBytes  = tape.GrammarByteLength;
            prevTapeCount   = tape.Count;
            run.Write("curve.tsv", LossReading.Header + SelfStream.HeaderCols + (rhythmOn ? Rhythm.HeaderCols : "") + curveReadout.HeaderSuffix + "\n");   // header lands NOW — rows append incrementally (a killed run keeps its curve); the rhythm columns ride ARMED ONLY (the off arm's curve stays byte-identical)
        }
        if (checkpointRuntimeOnly)
        {
            CortexExecutionWindow recoveredWindow;
            if (executionWindow is { } requestedRuntimeWindow)
            {
                requestedRuntimeWindow.Validate();
                if (requestedRuntimeWindow.StartStep != 0 && requestedRuntimeWindow.StartStep != step0)
                    throw new InvalidDataException($"execution window starts at {requestedRuntimeWindow.StartStep}, but checkpoint resumes at {step0}");
                recoveredWindow = requestedRuntimeWindow with { StartStep = step0 };
            }
            else recoveredWindow = new CortexExecutionWindow(step0, cfg.Steps);
            recoveredWindow.Validate();
            cortex.BindCheckpointRuntime(run, tape, journal, homeo, cfg.ReplayRatio);
            cortex.BindRuntimeCurriculum(curriculum);
            cortex.BindRuntimeExecutionWindow(recoveredWindow);
            cortex.BindRuntimeStep(step0, g);
            InstallRevision restoredInstallRevision = loadedInstallRevision
                ?? throw new InvalidDataException("checkpoint runtime recovery has no install revision tuple");
            cortex.SwapGrammar(restoredInstallRevision, advancePolicies: false);
            if (cortex.GrammarShape is { } restoredShape) energy.BindGrammarShape(restoredShape);
            checkpointLoadRawTicks = Math.Max(1, Stopwatch.GetTimestamp() - checkpointLoadStarted);
            checkpointLoadWallMilliseconds = Math.Max(0, Stopwatch.GetElapsedTime(checkpointLoadStarted).Ticks / TimeSpan.TicksPerMillisecond);
            afterRuntimeBindTimed?.Invoke(cortex, recoveredWindow, checkpointLoadWallMilliseconds, checkpointLoadRawTicks);
            afterRuntimeBind?.Invoke(cortex, recoveredWindow);
            return checkpointProofExact ? 0 : throw new InvalidDataException("checkpoint runtime recovery has no exact image proof");
        }
        (long ProofCount, long AuditCount, string Digest)? rung0Cursor = curriculum is ReplayCalc rung0Replay
            ? rung0Replay.CaptureDeepRematchRung0Cursor()
            : null;
        if (curriculum is ReplayCalc anytimeReplay)
        {
            // Fork rungs deliberately change only the absolute horizon.  Keep that
            // mutable stop boundary out of the anytime scope so a copied checkpoint
            // remains the same per-arm chain when the next rung extends it.
            CortexRunConfig anytimeScopeConfig = cfg with { Steps = 0 };
            string configID = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(anytimeScopeConfig.ToString())));
            if (anytimeIdentity is { } forkIdentity)
            {
                forkIdentity.Validate();
                anytimeReplay.BindAnytimeRun(run, configID, forkIdentity.ChainID, forkIdentity.ArmID, forkIdentity.Rung, forkIdentity.ParentPointID);
            }
            else
            {
                string armID = Path.GetFileName(run.Dir);
                anytimeReplay.BindAnytimeRun(run, configID, armID, armID, rung: 0);
            }
        }
        lastSpliceBytes = tape.GrammarByteLength;                      // both paths: the loom (if armed) has folded every grammar-intake event (seed splice / checkpoint idMark)
        GrammarRevisionID revisionID = resuming ? new GrammarRevisionID(loadedGrammarRevision) : GrammarRevisionID.Zero;
        InstallRevision? liveInstallRevision = null;
        GrammarOverlay? liveOverlay = null;
        void InstallRevision(in RePairResult result, string phase, GrammarOverlay? overlay = null, bool emitLineagePacket = true)
        {
            GrammarRevisionID next = revisionID.Next();
            InstallRevision nextInstallRevision;
            if (cortex.TryCreateLoopClosureFold(revisionID, next, out GrammarFoldProvenanceReceipt fold))
            {
                nextInstallRevision = global::Cogito.Grammar.InstallRevision.FromRePair(next, revisionID, in result, liveInstallRevision?.Snapshot, in fold).WithOverlay(overlay);
                if (emitLineagePacket && cortex.LoopLineage is not null)
                    _ = TapePacketCreator.AppendGrammarFoldInstallRevision(tape, journal, cortex.Step, in fold);
            }
            else
                nextInstallRevision = global::Cogito.Grammar.InstallRevision.FromRePair(next, revisionID, in result, liveInstallRevision?.Snapshot).WithOverlay(overlay);
            revisionID = next;
            liveInstallRevision = nextInstallRevision;
            liveOverlay = overlay;
            cortex.SwapGrammar(nextInstallRevision);
            if (cortex.GrammarShape is { } shape)
                energy.BindGrammarShape(shape);
            Trace.Cortex.Boundary("grammar.install-revision", $"phase={phase} revision={next} parent={nextInstallRevision.ParentRevision} reset={nextInstallRevision.Reset} rules={result.Rules.Length} symbols={result.Compressed.Length}");
        }
        if (resuming)
        {
            InstallRevision restoredInstallRevision = loadedInstallRevision ?? throw new InvalidDataException("checkpoint did not restore the installed grammar tuple");
            liveOverlay = GrammarOverlay.TryFromComposed(restoredInstallRevision.Snapshot, in g);
            restoredInstallRevision = restoredInstallRevision.WithOverlay(liveOverlay);
            liveInstallRevision = restoredInstallRevision;
            cortex.SwapGrammar(restoredInstallRevision, advancePolicies: false);
            if (cortex.GrammarShape is { } restoredShape) energy.BindGrammarShape(restoredShape);
            Trace.Cortex.Boundary("grammar.install-revision", $"phase=resume revision={revisionID} installed_rules={restoredInstallRevision.Snapshot.Rules.Length} installed_symbols={restoredInstallRevision.Snapshot.Compressed.Length} working_rules={g.Rules.Length} working_symbols={g.Compressed.Length}");
        }
        else InstallRevision(g, "seed");
        run.Write("config.txt", $"{cfg}\nworld_sha256={cfg.ExpectedWorldSHA256}\npersisted_config_digest={PersistedConfigDigest(cfg)}\n");
        journal.Rewrite(run);                                          // fresh: header + the seed's lines; resume: the checkpoint's exact horizon (kill-orphans shed)
        using var journalW = run.Appender("journal.log");
        journal.Mount(journalW, run.PathOf("journal.log"));           // from here every line lands on disk; shed-row custody re-reads this authority
        journal.CommitCheckpointLines();                               // the whole record is durably in journal.log now — shed it from RAM so this arc's base keyframe carries the horizon form, never the O(life) body
        if (resuming)
        {
            curriculum.VerifyLoadedState(tape, journal);
            if (curriculum is RepositoryNative.ICortexLoadedStateVerifier loadedStateVerifier)
                loadedStateVerifier.VerifyLoadedState(cortex, tape, journal);
        }
        reads.PrepareExcursionLog(run, fresh: !resuming);
        using var excursionsW = new StreamWriter(
            new FileStream(run.PathOf("excursions.txt"), FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16_384);
        reads.MountExcursionSink(excursionsW);                          // the persistent stream owns history; checkpoint memory keeps only its uncommitted tail
        if (world.ExternalWorld is { TotalItems: > 0 } externalWorld)
        {
            const string admissionHeader = "step\tcursor_before\tcursor_after\tplanned_items\tadmitted_items\tadmitted_bytes\tremaining_items\ttotal_items\tterminal\tcursor_digest\tschedule\tschedule_digest\tadmitted_domains\tdomain_digest";
            string admissionPath = run.PathOf("world-admission.tsv");
            if (!resuming)
                File.WriteAllText(admissionPath, admissionHeader + Environment.NewLine);
            else
            {
                // A killed process may have appended a row after its last
                // checkpoint. Keep only the prefix represented by the restored
                // tape cursor before reopening the append stream.
                string[] rows = File.Exists(admissionPath) ? File.ReadAllLines(admissionPath) : [];
                using StreamWriter repaired = new(admissionPath, append: false);
                repaired.WriteLine(admissionHeader);
                for (int i = 1; i < rows.Length; i++)
                {
                    string[] fields = rows[i].Split('\t');
                    if (fields.Length < 3 || !int.TryParse(fields[2], out int cursorAfter) || cursorAfter > externalWorld.Cursor) break;
                    repaired.WriteLine(rows[i]);
                }
            }
            worldAdmissionW = run.Appender("world-admission.tsv");
            if (!resuming && lastWorldAdmission.AdmittedItems > 0)
                worldAdmissionW.WriteLine(lastWorldAdmission.ToTsv());
        }
        CortexComputeAccounting.EnsureHeader(run.PathOf("compute.tsv"));
        using StreamWriter curveW = run.CurveAppender("curve.tsv");
        using StreamWriter computeW = run.CurveAppender("compute.tsv");
        string corpusLabel = cfg.RuntimeCurriculum is not null ? cfg.RuntimeCurriculum.GetType().Name : string.IsNullOrWhiteSpace(cfg.CorpusPath) ? cfg.Curriculum : Path.GetFileName(cfg.CorpusPath.TrimEnd('/'));
        if (world.ExternalWorld is { TotalItems: > 0 } labelWorld)
            corpusLabel += $" + world:{labelWorld.TotalItems} spans/{world.ExternalWorldFamilies} domains";
        string curriculumLabel = cfg.RuntimeCurriculum is not null ? "runtime:" + cfg.RuntimeCurriculum.GetType().Name : cfg.Curriculum;
        Trace.Note($"cortex · {corpusLabel}{(fc is not null ? $" · {fc.Families} domains" : "")} · {corpusBytes}B → pool {pool.Count} spans ({heldout.Length}B held out) · curriculum={curriculumLabel} energy={cfg.Energy} λ={cfg.Lambda} · {(cfg.Loom ? "LOOM O(Δ)" : $"stride≥{cfg.ReStrideBytes}B/K{StrideK}")} · {cfg.Steps} steps · budget {(cfg.GrammarBudgetBits == 0 ? "unbounded" : cfg.GrammarBudgetBits + "b")}");
        Trace.Note($"  {(LossReading.Header + SelfStream.HeaderCols + (rhythmOn ? Rhythm.HeaderCols : "") + curveReadout.HeaderSuffix).Replace('\t', ' ')}");
        int step = step0;
        CortexExecutionWindow runtimeWindow;
        if (executionWindow is { } requestedWindow)
        {
            requestedWindow.Validate();
            if (requestedWindow.StartStep != 0 && requestedWindow.StartStep != step0)
                throw new InvalidDataException($"execution window starts at {requestedWindow.StartStep}, but checkpoint resumes at {step0}");
            runtimeWindow = requestedWindow with { StartStep = step0 };
        }
        else
            runtimeWindow = new CortexExecutionWindow(step0, cfg.Steps);
        runtimeWindow.Validate();
        bool setupOnly = !resuming && runtimeWindow.StartStep == 0 && runtimeWindow.EndStep == 0;
        string executionWindowDocument = $"(start_step:{runtimeWindow.StartStep},end_step:{runtimeWindow.EndStep})\n";
        string executionWindowPath = run.PathOf("execution-window.ron");
        if (File.Exists(executionWindowPath) && executionWindow is not null)
        {
            string persistedWindow = File.ReadAllText(executionWindowPath);
            if (!string.Equals(persistedWindow, executionWindowDocument, StringComparison.Ordinal))
                throw new InvalidDataException($"execution window changed for {run.Dir}: persisted {persistedWindow.Trim()} requested {executionWindowDocument.Trim()}");
        }
        else
            run.Write("execution-window.ron", executionWindowDocument);
        bool loomInstallRevisionLagging = false;
        StringBuilder computeRow = new(320);                       // per-run scratch — CompleteRow keeps the per-step accounting emit alloc-free
        void RecordStepCompute(CortexComputeAccounting accounting, int completedStep)
        {
            double totalMs = accounting.CompleteRow(completedStep, Trace.NowTicks, computeRow);
            computeW.Write(computeRow);
            computeW.WriteLine();
            if (totalMs > Trace.StepSlowMs)
                Trace.Cortex.Warn("step.slow", $"step={completedStep} ms={totalMs:F3} tape={tape.Count} drained={curriculum.IngestedCount}");
        }

        void CompleteStep(CortexComputeAccounting accounting, int completedStep)
        {
            accounting.Advance(CortexComputeSegmentKinds.Verifier, Trace.NowTicks);
            beforeCompletedStep?.Invoke(cortex, completedStep);
            // The economy is settled by completed outer steps, not by the
            // next step's preflight bind. Persisting this before the callback
            // fork boundary makes terminal checkpoints/forks carry the final
            // mint exactly once; the next bind sees an equal cursor.
            cortex.CompleteRuntimeStep(completedStep);
            cortex.SetCompletedStepForkBoundary(true);
            try
            {
                curriculum.OnStepCompleted(cortex, completedStep);
                foreach (CortexActionPolicy policy in cortex.ActionPolicies) policy.OnStepCompleted(cortex, completedStep);
                foreach (CortexReward reward in cortex.Rewards) reward.OnStepCompleted(cortex, completedStep);
                CortexTapeAdmission.VerifyCandidate(cortex);
                EnergyPolicyAutonomy.VerifyCandidate(cortex);
                // The funded fork assay is not verifier work. Keep its nested child ladder on its own
                // conservation segment so step averages expose the policy boundary's actual wall.
                accounting.Advance(CortexComputeSegmentKinds.PolicyBoundary, Trace.NowTicks);
                cortex.TryRunHomeostatBoundaryAtStep(cfg);
                accounting.Advance(CortexComputeSegmentKinds.Verifier, Trace.NowTicks);
            }
            finally
            {
                cortex.SetCompletedStepForkBoundary(false);
            }
            RecordStepCompute(accounting, completedStep);
            afterCompletedStep?.Invoke(cortex, completedStep);
            afterCompletedStepEveryStep?.Invoke(cortex, completedStep);
        }
        CortexSnap BuildSnapshot(int nextStep)
        {
            curveW.Flush();
            return new CortexSnap(nextStep, revisionID.Value, lastInduceBytes, wallStreak,
                totalEvicted, totalPromoted, lastSlotted, lastBitsSaved, new FileInfo(run.PathOf("curve.tsv")).Length,
                lastSleepBytes, lastInduceOpb, lastBitsPerSpan, prevTapeCount, breachConsolidationPhases, breachWindowResets,
                forkStep, forkVolumeFrac, lastConsolidationPhaseRules, lastDNodes);
        }
        List<TapeEventID> foldedAdmissionEvents = new();            // exact promotion IDs folded since the last grammar install revision
        if (resuming && loom is not null && curriculum is ReplayCalc resumedAdmissionReplay)
        {
            foreach (EmlPatternGrammarAdmissionReceipt promotion in resumedAdmissionReplay.PatternGrammarAdmissions)
            {
                if (promotion.Consumed || promotion.ReflectedTapeEventID is not TapeEventID reflected)
                    continue;
                if (tape.TryGetEventView(reflected, out TapeEventView view)
                    && view.Source == "eml:theory-grammar"
                    && view.Provenance == Provenances.Reflected
                    && loom.ParsedLenOf(reflected.Value) >= 0)
                    foldedAdmissionEvents.Add(reflected);
            }
        }
        if (resuming && loom is not null)
        {
            foreach (TapeEventView view in tape.GetGrammarEventViews())
            {
                if (view.Source != "repository:theory" || view.Provenance != Provenances.Reflected
                    || loom.ParsedLenOf(view.Id.Value) < 0 || foldedAdmissionEvents.Contains(view.Id)) continue;
                foldedAdmissionEvents.Add(view.Id);
            }
        }
        void FlushLoomDeltaBeforeSnapshot(int nextStep)
        {
            if (loom is null) return;

            // A checkpoint is a resume boundary, not a copy of a half-consumed
            // mutation receipt. Fold and pump the exact pending TapeDelta before
            // serializing Loom so the restored id mark covers every grammar-role view span;
            // the thought stream advances here exactly where the next live step
            // would have consumed the same merge events.
            List<MergeEvent> events = new();
            TapeDelta delta = tape.DrainDelta();
            if (delta.IsEmpty) return;
            loom.ApplyTapeDelta(tape, in delta);
            loom.Pump(events);
            RecordFoldedPatternGrammarAdmissions(tape, in delta, foldedAdmissionEvents);
            if (events.Count > 0) selfModel.ObserveThought(events, 256);
            lastSpliceBytes = tape.GrammarByteLength;
            Trace.Cortex.Boundary("loom.checkpoint.flush",
                $"step={nextStep} appended={delta.Appended.Length} reflected={delta.Reflected.Length} shed={delta.Shed.Length} dropped={delta.Dropped.Length} events={events.Count}");
        }
        byte[] CaptureSnapshot(int nextStep)
        {
            FlushLoomDeltaBeforeSnapshot(nextStep);
            CortexSnap snapshot = BuildSnapshot(nextStep);
            InstallRevision installRevision = liveInstallRevision ?? throw new InvalidOperationException("checkpoint capture has no install revision authority");
            return Checkpoint.Encode(cfg, corpusBytes, pool.Count, fc?.Families ?? 0, snapshot,
                g, installRevision, tape, journal, curriculum, reads, selfModel, controller, metabolism, memory, homeo, loom, rhythm, cortex);
        }
        void CommitMutationState()
        {
            // The keyframe already contains these append-only streams.  Advance
            // their cursors before the first mutation so replay starts at the
            // keyframe horizon instead of attempting to append the whole tape.
            tape.CommitCheckpointDelta();
            journal.CommitCheckpointLines();
            reads.CommitCheckpointDelta();
            selfModel.CommitCheckpointDelta();
            controller.CommitCheckpointDelta();
            metabolism.CommitCheckpointDelta();
            memory.CommitCheckpointDelta();
            homeo.CommitCheckpointDelta();
            rhythm.CommitCheckpointDelta();
            cortex.CommitPolicyCheckpointDelta();
            cortex.ShedCommittedPolicyReadoutAllocations();
            cortex.LoopLineage?.CommitCheckpointDelta();
            cortex.CommitLoopClosureLinkCheckpointDelta();
            loom?.CommitCheckpointDelta();
            if (curriculum is ICurriculumCheckpointDeltaOwner owner)
            {
                ICurriculumCheckpointDelta captured = owner.CaptureCheckpointDelta()
                    ?? throw new InvalidDataException($"runtime curriculum {curriculum.GetType().Name} returned no checkpoint mutation");
                owner.CommitCheckpointDelta(captured);
            }
            if (curriculum is ReplayCalc committedReplay)
                committedReplay.PersistPairedFuelScheduleSidecar();
        }
        void CommitCapturedMutationState(in CheckpointMutationState state)
        {
            // SaveMutation commits tape/journal/reads after its durable append;
            // repeating the idempotent cursor advance here keeps the keyframe
            // and typed-organ commits symmetric.
            tape.CommitCheckpointDelta();
            journal.CommitCheckpointLines();
            reads.CommitCheckpointDelta();
            selfModel.CommitCheckpointDelta();
            controller.CommitCheckpointDelta();
            metabolism.CommitCheckpointDelta();
            memory.CommitCheckpointDelta();
            homeo.CommitCheckpointDelta();
            rhythm.CommitCheckpointDelta();
            cortex.CommitPolicyCheckpointDelta();
            cortex.ShedCommittedPolicyReadoutAllocations();
            cortex.LoopLineage?.CommitCheckpointDelta();
            cortex.CommitLoopClosureLinkCheckpointDelta();
            loom?.CommitCheckpointDelta();
            if (curriculum is ICurriculumCheckpointDeltaOwner owner)
                owner.CommitCheckpointDelta(state.Curriculum
                    ?? throw new InvalidDataException($"runtime curriculum {curriculum.GetType().Name} mutation omitted its captured checkpoint delta"));
            if (curriculum is ReplayCalc committedReplay)
                committedReplay.PersistPairedFuelScheduleSidecar();
        }
        ICurriculumCheckpointDelta CaptureCurriculumMutation()
            => curriculum is ICurriculumCheckpointDeltaOwner owner
                ? owner.CaptureCheckpointDelta()
                    ?? throw new InvalidDataException($"runtime curriculum {curriculum.GetType().Name} returned no checkpoint mutation")
                : throw new InvalidDataException($"runtime curriculum {curriculum.GetType().Name} does not own checkpoint mutations");
        CheckpointWriteReceipt SaveSnapshot(int nextStep)
        {
            if (!mutationRailReady)
            {
                reads.FlushCheckpointOutput();
                byte[] image = CaptureSnapshot(nextStep);
                CheckpointWriteReceipt keyframe = Checkpoint.Save(run, stream => stream.Write(image));
                CommitMutationState();
                Checkpoint.InitializeMutationRail(run, nextStep);
                mutationRailReady = true;
                checkpointHorizon = nextStep;
                return keyframe;
            }
            if (checkpointGrammarArtifact.Revision != revisionID.Value)
            {
                InstallRevision installRevision = liveInstallRevision ?? throw new InvalidOperationException("grammar artifact has no install revision authority");
                (string fileName, string digest) = Checkpoint.SaveGrammarArtifact(run, in installRevision);
                checkpointGrammarArtifact = new(revisionID.Value, fileName, digest);
            }
            CortexSnapCheckpointDelta snapDelta = CortexSnapCheckpointDelta.Capture(BuildSnapshot(nextStep));
            ICurriculumCheckpointDelta curriculumDelta = CaptureCurriculumMutation();
            CheckpointReplayKinds replayKind = curriculumDelta is ReplayCalcCheckpointDelta dreamDeltaValue
                && dreamDeltaValue.AnytimeRebase
                ? CheckpointReplayKinds.AnytimeRebase
                : CheckpointReplayKinds.None;
            ReplayCalcCheckpointDelta dreamDelta = curriculumDelta is ReplayCalcCheckpointDelta typedReplay
                ? typedReplay
                : default;
            string predecessorPointID = replayKind == CheckpointReplayKinds.AnytimeRebase
                ? dreamDelta.AnytimeRebasePredecessorPointID : "";
            CheckpointReplayContext replayContext = new(
                runtimeWindow.StartStep,
                runtimeWindow.EndStep,
                replayKind,
                ConfigDigest: PersistedConfigDigest(cfg),
                RailRunID: Path.GetFileName(run.Dir),
                PredecessorCurveDigest: predecessorPointID,
                PredecessorParentPointID: predecessorPointID,
                SuccessorRunID: dreamDelta.AnytimeRebaseSuccessorRunID ?? "",
                SuccessorConfigID: dreamDelta.AnytimeRebaseSuccessorConfigID ?? "",
                SuccessorChainID: dreamDelta.AnytimeRebaseSuccessorChainID ?? "",
                SuccessorArmID: dreamDelta.AnytimeRebaseSuccessorArmID ?? "",
                ScheduleDigest: dreamDelta.PairedFuelConfigured ? dreamDelta.PairedFuelSchedule.Digest : "",
                Bound: true);
            CheckpointMutationState mutationState = new(
                selfModel.CaptureCheckpointDelta(),
                controller.CaptureCheckpointDelta(),
                metabolism.CaptureCheckpointDelta(),
                memory.CaptureCheckpointDelta(),
                homeo.CaptureCheckpointDelta(),
                rhythm.CaptureCheckpointDelta(),
                cortex.CapturePolicyCheckpointDelta(),
                cortex.LoopLineage?.CaptureCheckpointDelta() ?? default,
                loom?.CaptureCheckpointDelta() ?? default,
                loom is not null,
                curriculumDelta,
                snapDelta,
                replayContext,
                cortex.CaptureLoopClosureLinkCheckpointDelta());
            CheckpointWriteReceipt mutation = Checkpoint.SaveMutation(run, checkpointHorizon, nextStep, tape, journal, reads, in checkpointGrammarArtifact, in mutationState);
            CommitCapturedMutationState(in mutationState);
            checkpointHorizon = nextStep;
            // The paired cursor is part of ReplayCalc's typed mutation payload.
            // Keep the detached RON sidecar at the coarse keyframe/open boundary;
            // writing and rereading it here would put an fsync on every step and
            // could overwrite a newer checkpoint cursor during resume.
            return mutation;
        }

        CortexForkSeed MaterializeCompletedStepForkSeed()
        {
            reads.FlushCheckpointOutput();
            byte[] checkpoint = CaptureSnapshot(step + 1);
            using MemoryStream tapeLog = new();
            tape.CopyLogTo(tapeLog);
            byte[] curve = File.ReadAllBytes(run.PathOf("curve.tsv"));
            byte[] excursions = File.ReadAllBytes(run.PathOf("excursions.txt"));
            return CortexForkSeed.Materialize(step + 1, checkpoint, tapeLog.ToArray(), curve,
                PersistedConfigDigest(cfg), cortex.CopyPolicyJournals(), excursions, reads.ExcursionCount);
        }

        CortexForkSeed MaterializeColdForkSeed()
        {
            reads.FlushCheckpointOutput();
            byte[] checkpoint = CaptureSnapshot(0);
            using MemoryStream tapeLog = new();
            tape.CopyLogTo(tapeLog);
            byte[] curve = File.ReadAllBytes(run.PathOf("curve.tsv"));
            byte[] excursions = File.ReadAllBytes(run.PathOf("excursions.txt"));
            return CortexForkSeed.Materialize(0, checkpoint, tapeLog.ToArray(), curve,
                PersistedConfigDigest(cfg), cortex.CopyPolicyJournals(), excursions, reads.ExcursionCount);
        }

        CortexForkSeed? coldForkSeed = null;

        cortex.BindRuntime(run, tape, journal, homeo, cfg.ReplayRatio, reads.FlushCheckpointOutput);
        cortex.BindRuntimeCurriculum(curriculum);
        cortex.BindRuntimeExecutionWindow(runtimeWindow);
        cortex.BindRuntimeStep(step0, g);
        cortex.BindRuntimeSnapshot(() => CaptureSnapshot(step + 1));
        cortex.BindCompletedStepForkSeed(MaterializeCompletedStepForkSeed);
        if (!resuming) coldForkSeed = MaterializeColdForkSeed();
        cortex.BindColdForkSeed(coldForkSeed);
        // Fork preparation can authenticate source custody through CurrentRun. Keep it after
        // runtime binding but before the observer callback and first child step.
        if (resuming && interveneAfterLoad is not null)
            interveneAfterLoad(cortex);
        afterRuntimeBind?.Invoke(cortex, runtimeWindow);
        if (setupOnly)
        {
            cortex.BindRuntimeSnapshot(null);
            cortex.BindCompletedStepForkSeed(null);
            cortex.BindRuntimeCurriculum(null);
            return 0;
        }
        if (prepareOnly)
        {
            // Preparation deliberately stops at the post-load intervention
            // boundary.  Do not run terminal landing (grammar induction,
            // policy settlement, or a second callback); persist exactly the
            // loaded-plus-prepared runtime image for the child seed.
            reads.FlushCheckpointOutput();
            cortex.FlushPolicyJournalBuffer();
            curveW.Flush();
            computeW.Flush();
            CheckpointWriteReceipt prepared = SaveSnapshot(step);
            Trace.Cortex.Boundary("fork.seed.prepare", $"step={step} bytes={prepared.Bytes}");
            cortex.BindRuntimeSnapshot(null);
            cortex.BindCompletedStepForkSeed(null);
            cortex.BindRuntimeCurriculum(null);
            curveW.Dispose();
            computeW.Dispose();
            return 0;
        }
        foreach (CortexActionPolicy policy in cortex.ActionPolicies) policy.OnRunStart(cortex);
        foreach (CortexReward reward in cortex.Rewards) reward.OnRunStart(cortex);
        cortex.SetColdForkBoundary(false);
        List<TapeEventID> actionEventIDs = new();
        List<MergeEvent> actionThoughts = new();
        List<MergeEvent> loomEvents = new();                       // per-step Δ-mint scratch (loom arm) — cleared each drain, aliased by reinduceEvents within the step only
        try
        {
        for (; step < runtimeWindow.EndStep; step++)
        {
            long stepT0 = Trace.NowTicks;
            CortexComputeAccounting accounting = new(stepT0);
            try
            {
            Trace.Cortex.Boundary("step", $"#{step}");
            cortex.BindRuntimeStep(step, g);
            if (curriculum is ReplayCalc stepFuelReplay)
                stepFuelReplay.BeginPairedFuelStep(cortex, step);
            foreach (CortexActionPolicy policy in cortex.ActionPolicies) policy.OnStepStart(cortex, step);
            foreach (CortexReward reward in cortex.Rewards) reward.OnStepStart(cortex, step);
            List<MergeEvent>? reinduceEvents = null;                     // non-null only when INDUCE re-ran → the thought channel's food this step
            accounting.Advance(CortexComputeSegmentKinds.Induce, Trace.NowTicks);

            // ── INDUCE ──  stride-gated re-induction over THE TAPE (the mandatory O(n²) fix — reuse the
            // ≤stride-stale grammar between strides; the frontier's residual ranking is stable within a stride).
            // THE STRIDE LAW: the stride SCALES with the tape — max(ReStrideBytes floor, tape/StrideK) — so the
            // O(tape) induce amortizes as a CONSTANT fraction of growth (O(StrideK) per byte → LINEAR total, geometric
            // re-induce spacing). A fixed floor stride would be OVERSHOT every step by a full post-drain block (→
            // MaxBlockBytes), so the tape grew ARITHMETICALLY and induce cost went O(n²) — the wall 2a/2c measured at
            // 16-20s/step by the deep regime (blocks balloon to MaxBlockBytes as rules deepen). Paired with the MINT
            // cap below (per-step growth kept < stride) so growth stays geometric and the amortization holds.
            long stride = Math.Max(cfg.ReStrideBytes, tape.GrammarByteLength / StrideK);
            if (loom is not null)
            {
                // ── THE LOOM ARM ──  the FOLD is per-step, the HARVEST is per-stride.
                // SPLICE + PUMP run every step the tape moved (O(Δ) — counts, savings, and the thought stream stay
                // continuous), but `g` — the grammar IDENTITY every stride cache downstream keys on (Reads' cover,
                // Energy's couplings/scorers/transitions, the curricula's frontier covers) — refreshes only on the
                // harvest stride, exactly the cadence those organs were designed for. Per-step harvest made the grammar
                // volatile EVERY step and silently turned each of those O(rules) stride rebuilds into a per-step
                // cost (measured 14s/step at 27k rules: read 11.5s + energy 3.4s, against a 3-7ms fold — the O(Δ)
                // induce defeated by its own freshness). The stride trades bounded staleness for amortization at
                // the MEASUREMENT layer only — the loom underneath never re-derives, so the harvest step pays
                // Result + cache rebuilds, not O(tape) induction.
                loomEvents.Clear();
                TapeDelta loomDelta = tape.DrainDelta();
                if (!tracedFirstLiveLoomDrain)
                {
                    tracedFirstLiveLoomDrain = true;
                    string appendRange = loomDelta.Appended.Length == 0
                        ? "none"
                        : $"{loomDelta.Appended[0].Value}..{loomDelta.Appended[^1].Value}";
                    Trace.Cortex.Boundary("loom.resume.first-live-drain",
                        $"step={step} delta={loomDelta.Appended.Length}/{loomDelta.Reflected.Length}/{loomDelta.Shed.Length}/{loomDelta.Dropped.Length} append_ids={appendRange} tape={tape.MutationCursor} loom_mark={loom.SpliceIDMark} loom_revision={loom.MutationRevision} loom_arena={loom.LiveSymbols}");
                }
                if (!loomDelta.IsEmpty)
                {
                    var phInduce = Trace.CortexPhase("induce");
                    long dSpliceBytes = tape.GrammarByteLength - lastSpliceBytes;
                    loom.ApplyTapeDelta(tape, in loomDelta);
                    loom.Pump(loomEvents);
                    RecordFoldedPatternGrammarAdmissions(tape, in loomDelta, foldedAdmissionEvents);
                    if (loomEvents.Count > 0)
                        reinduceEvents = loomEvents;                    // only the Δ mints — the thought channel hears new cognition, not a from-scratch replay
                    if (dSpliceBytes > 0) lastInduceOpb = (double)loomEvents.Count / dSpliceBytes;
                    lastSpliceBytes = tape.GrammarByteLength;
                    phInduce.Dispose();
                }
                if (tape.GrammarByteLength - lastInduceBytes >= stride)
                {
                    accounting.Advance(CortexComputeSegmentKinds.Harvest, Trace.NowTicks);
                    var phHarvest = Trace.CortexPhase("harvest");
                    g = loom.Result(tape);                             // the ONE pure-binary grammar identity change per stride — downstream caches rebuild HERE
                    InstallRevision(g, "stride", liveOverlay);
                    InstallRevision strideInstallRevision = liveInstallRevision
                        ?? throw new InvalidDataException("stride grammar installRevision was not installed");
                    curriculum.SettleInstallRevision(
                        in strideInstallRevision, foldedAdmissionEvents,
                        id => loom.ParsedLenOf(id.Value) >= 0,
                        cortex.LoopLineage, tape, journal, step);
                    foldedAdmissionEvents.Clear();
                    lastInduceBytes = tape.GrammarByteLength;
                    if (homeoOn && homeo.CvzMasked)                    // C2: the harvest IS the grammar-identity change consumers see
                    { homeo.MaskCvz(false); Trace.Cortex.Boundary("breach.c2", $"step={step} cvz mask cleared — post-LOWER re-induce (loom harvest)"); }
                    phHarvest.Dispose();
                }
            }
            else if (tape.GrammarByteLength - lastInduceBytes >= stride)
            {
                var phInduce = Trace.CortexPhase("induce");
                long dInduceBytes = tape.GrammarByteLength - lastInduceBytes;              // Δ since the last induce — grammar intake is the Hot sense's denominator
                (g, reinduceEvents, _) = InduceOutcomeCredited(tape, journal, step, cfg.WScale, cfg.CrossReflect, auditCache);   // traced — the merge stream is the thought channel's food (grammar byte-identical); armed (wScale>1) it also corroborates: real exercise (cross-reflection: a peer source's) reflects the replay spans supporting it
                if (dInduceBytes > 0) lastInduceOpb = (double)reinduceEvents.Count / dInduceBytes;   // merges per new byte — falls as structure repeats (held between strides)
                lastInduceBytes = tape.GrammarByteLength;
                if (homeoOn && homeo.CvzMasked)                        // C2: the first post-LOWER InduceOutcomeCredited is the grammar-identity change — the bell may read again
                { homeo.MaskCvz(false); Trace.Cortex.Boundary("breach.c2", $"step={step} cvz mask cleared — post-LOWER re-induce (day stride)"); }
                phInduce.Dispose();
            }
            if (loom is not null)
            {
                long installRevisionLagBytes = Math.Max(0, tape.GrammarByteLength - lastInduceBytes);
                bool installRevisionLagging = installRevisionLagBytes > 0;
                if (installRevisionLagging != loomInstallRevisionLagging)
                {
                    Trace.Cortex.Boundary("loom.install-revision",
                        $"step={step} {(installRevisionLagging ? "lagging" : "caught-up")} · installed rules={g.Rules.Length} mdl={g.TotalSavings.Value} · folded rules={loom.RuleCount} mdl={loom.Savings} symbols={loom.LiveSymbols} · pending={installRevisionLagBytes}/{stride}B");
                    loomInstallRevisionLagging = installRevisionLagging;
                }
            }
            if ((g.Compressed?.Length ?? 0) == 0) { Trace.Note("  drive halted: empty grammar (nothing on the tape)"); break; }

            // ── GENERATE ──  sample the current replay from the grammar (EnergyPolicy). Read every step; minted
            // only once the scaffold is drained (the autoregressive loopback).
            accounting.Advance(CortexComputeSegmentKinds.Generate, Trace.NowTicks);
            var phGen = Trace.CortexPhase("generate");
            ReadOnlyMemory<byte> block = liveInstallRevision is { } installRevision
                ? energy.GenerateMemory(installRevision, cfg.BlockLen, cfg.Seed + (ulong)step, metabolism, controller.Current)
                : energy.GenerateMemory(g, cfg.BlockLen, cfg.Seed + (ulong)step, metabolism, controller.Current);   // the controller's LIVE weights (nudged from the previous step's reads — the machine becomes the walk its reads demand)
            if (block.Length > cfg.MaxBlockBytes) block = block[..cfg.MaxBlockBytes];   // bound the expansion without copying the generator-owned output
            phGen.Dispose();

            // ── the FORK EDGE ──  the replay-fork OPENS the first step the curriculum reports intake-exhaustion
            // (the pool volume-consumed — THE SELF-REGULATION LAW: the machine eats its world before it dreams,
            // never forking on a schedule's say-so). Detected before READ so the fork row itself carries the
            // frozen coverage readout (fork_vol_frac — the sparkline that red-flags a premature fork).
            if (forkStep < 0 && curriculum.Exhausted)
            {
                forkStep = step;
                forkVolumeFrac = pool.Count > 0 ? (double)curriculum.IngestedCount / pool.Count : 1.0;
                Trace.Cortex.Boundary("fork", $"step={step} replay-fork OPENS — pool volume-exhausted: ingested {curriculum.IngestedCount}/{pool.Count} (fork_vol_frac {forkVolumeFrac:F2}) · tape {tape.Count} spans (real {tape.RealCount} · replay {tape.ReplayCount})");
            }

            // ── READ ──  the sparkline suite → LossReading, over the held-out probe (resolved in PHASE 0).
            // The MOTION snapshot rides in: the tape's vest-by-source census (node0 vs peer — the decisive multi-node
            // read, updated live at every Corroborate) + the last consolidationPhase's structural deltas (demoted/births/churn).
            accounting.Advance(CortexComputeSegmentKinds.Read, Trace.NowTicks);
            var phRead = Trace.CortexPhase("read");
            var motion = new Reads.Motion(
                VestN0: tape.ReflectedNode0, VestPeer: tape.ReflectedPeer, OutcomeCreditedTotal: tape.ReflectedReplayCount, ReplayTotal: tape.ReplayCount,
                ReplaysN0: tape.ReplaysNode0, ReplaysPeer: tape.ReplaysPeer, Demoted: totalDemoted, Births: lastBirths, Churn: lastChurn);
            LossReading read = reads.Step(step, (int)tape.GrammarByteLength, tape.Count, curriculum.IngestedCount, g, block, probe, cfg.WallTol, totalEvicted, totalPromoted, lastSlotted, lastBitsSaved, forkVolumeFrac, curriculum.IngestDiversity, tape.ShedCount, tape.DroppedCount, lastDNodes, motion, cortex.GrammarShape);
            phRead.Dispose();

            // ── MODEL ──  the self-model predicts its OWN next token — the excursion stream (HomeWatch dynamics) and
            // the merge-event thought stream (cognition, when INDUCE re-ran) — and MINTS ONLY THE RESIDUAL (pred≠actual,
            // a genuine surprise; predicted excursions go free). The rolling mint-rate is the self-signal the loop
            // reads. The WeightController rides the field on the COLLAPSE-ROBUST reads
            // ONLY — Distinct/NovelChain/maxSpan-plateau/coll_frac/df_third/CvZ, NEVER the energy terms (the longdrive
            // Goodhart guardrail). The row/line compose HERE, not in READ: the reading is not complete until the
            // self-model has spoken.
            accounting.Advance(CortexComputeSegmentKinds.Model, Trace.NowTicks);
            var phModel = Trace.CortexPhase("model");
            if (reinduceEvents is { } thoughts)
            {
                int thoughtMints = selfModel.ObserveThought(thoughts, 256);   // cognition re-ran this step → the second channel's food
                if (thoughtMints > 0)
                    TapePacketCreator.AppendSelfSignal(tape, journal, step, "thought", $"mint={thoughtMints};events={thoughts.Count}");
            }
            double surprise = selfModel.Observe(read.Excursion);
            if (surprise > 0) TapePacketCreator.AppendSelfSignal(tape, journal, step, "excursion", read.Excursion);   // mint ONLY the residual — the surprise
            // ── the INTEROCEPT fold (face 1) ──  armed, the homeostat senses the whole machine each step — cost
            // OPB, the self-model's mint/hit, the k-aware criticality pair, the collapse suite, comprehension, and
            // the PROVENANCE plane (unvested fraction / vest rate off the vested tape — the face-1↔face-2 coupling)
            // — and returns the MASKED cvz the fast plane rides (NaN while a breach heats the bell, the C2 law).
            // Off, the fast plane reads the raw cvz exactly as today.
            double cvzFast = read.CvZ;
            if (homeoOn)
            {
                curriculum.MixEvery = homeo.MixEvery;                  // re-apply the perception actuation every step (idempotent — and resume-exact for free)
                // the standing forecast (Predict tier only — the pure read still allocs its context keys, so the
                // un-armed tiers never pay it); a masked self-model plane forecasts NOTHING (the mask law).
                string excPred = "·"; char excArm = 'x';
                if (cfg.HomeoPolicy == HomeoPolicies.Predict && !senseMask.SelfStream && selfModel.ExcForecast(out char fArm) is { Length: > 0 } f) { excPred = f; excArm = fArm; }
                cvzFast = homeo.SenseStep(senseMask.Apply(new Interocept(   // masked planes pinned DARK here, before the EMA — the --sense-mask attribution ablation (None = identity)
                    InduceOpb: lastInduceOpb,
                    GenOpb: block.Length > 0 ? (double)cfg.BlockLen / block.Length : 0,   // relax chunks per generated byte — falls as rules deepen
                    GrowthRate: tape.Count - prevTapeCount,
                    BitsPerSpan: lastBitsPerSpan,
                    ExcMint: selfModel.MintRate, ExcHit: selfModel.ExcHitRate, ThtMint: selfModel.ThoughtMintRate,
                    Cvz: read.CvZ, Kz: read.KZ,
                    Distinct: read.Distinct, NovelChain: read.NovelChain, CollFrac: read.CollFrac, DfThird: read.DfThird,
                    Js: read.Js, LoopConverged: read.LoopVerdict == "CONVERGE",
                    Depth: read.Depth, MaxSpan: read.MaxSpan, MomentumStalled: read.MomentumVerdict == "WALL",
                    UnvestedFrac: tape.Count > 0 ? (double)(tape.ReplayCount - tape.ReflectedReplayCount) / tape.Count : 0,
                    VestRate: tape.ReplayCount > 0 ? (double)tape.ReflectedReplayCount / tape.ReplayCount : 0,
                    ReplayEra: curriculum.Exhausted, CvzMasked: false)), excPred, excArm);   // replay era = post-fork (volume-exhausted) — the mop-up still counts as intake, not dreaming
            }
            prevTapeCount = tape.Count;                                // maintained both arms (checkpointed loop-local — the GrowthRate sense's anchor)
            controller.Nudge(cortex, read.Distinct, read.NovelChain, read.MaxSpan, read.CollFrac, read.DfThird, cvzFast, read.MomentumVerdict);   // ride the field on the COLLAPSE-ROBUST reads (never the energy terms — the Goodhart guardrail); Current feeds next step's GENERATE
            if (step % 10 == 0 || step == runtimeWindow.EndStep - 1) Trace.Note(read.Line() + selfModel.Line());
            phModel.Dispose();

            // ── FORK ──  THE SELF-REGULATION LAW: the machine consumes its WORLD, not its schedule. Three eras:
            //   SCHOOL (!Drained)              — the scheduler drives residual intake (grok → move-on → grok).
            //   MOP-UP (Drained && !Exhausted) — the schedule crossed every bridge, but grok→move-on abandoned real
            //     spans (the max-push forked here at ~20% coverage — 84% un-ingested — and drifted off criticality).
            //     The PREVENTIVE guard, promoted from the reactive momentum-meadow WALL-nudge: keep draining the
            //     leftovers by residual frontier at the intake rate — the replay-fork stays SHUT while reality
            //     remains un-eaten. (The O(Δ) frontier makes eating past schedule-done affordable.)
            //   REPLAY (Exhausted)              — the pool is volume-consumed: the autoregressive loopback mints
            //     under the replay-fraction throttle, MIX re-anchors, and the STOP gate below reads
            //     the momentum wall on the STRIDE clock (a flat stride-over-stride slope — the true plateau).
            bool grokLock = false;                                        // set when the scheduler crosses a bridge (a domain grokked) — a SLEEP trigger
            bool rhythmConsolidationPhase = false;                                     // the rhythm CHOSE an input-free consolidationPhase this step (armed only) — the sleep gate consumes it
            RhythmChoice rhythmChoice = default;
            bool exhaustedAtFork = curriculum.Exhausted;                  // the era AT the fork — the STOP wall reads only post-drain steps (the boundary step that just exhausted the pool mopped up; it didn't replay)
            int intakeBatch = homeoOn ? homeo.IntakeBatch : cfg.IntakeBatch;   // the drain rate — a homeostat perception actuator (floored at rest: anti-dark-room)

            // ── the REPLAY step's one body (both arms — the legacy replay era and the rhythm's Replay phase) ──
            // MINT cap: accrete at most stride/MintStrideDiv bytes so per-step tape growth stays BELOW the
            // re-induce stride — this is what keeps growth GEOMETRIC (tape ×(1+1/StrideK) per stride) so the scaling
            // stride's induce amortization holds; an uncapped block (→ MaxBlockBytes) would overshoot the stride in
            // one step (arithmetic growth ⇒ re-induce every step ⇒ O(n²)). No-op in the deep regime (block already
            // < stride once the tape is large); it only bites the early band where a full block would overshoot.
            // THE REPLAY-FRACTION THROTTLE: unvested dreams are HYPOTHESES; born evidence must corroborate them.
            // Cap the unvested-replay stock at ReplayRatio x born evidence, so once the cap binds the mint rate follows
            // new evidence admitted to the tape. Un-throttled, mint accreted at a constant MintSpansPerStep while
            // post-fork real intake decayed to the MIX trickle: the replay outpaced reality, the real anchor diluted,
            // and meanz drifted −0.78→−0.66 (measured). A VESTED replay has earned evidence status and frees its
            // slot — corroborate more, replay more. `cohort` non-null collects the minted ids (the rhythm's outcome
            // register — a replay-decision is judged by whether its spans eventually vest).
            void ReplayStep(List<TapeEventID>? cohort)
            {
                int mintCap = (int)Math.Min(block.Length, Math.Max(1, stride / MintStrideDiv));
                long dreamHeadroom = cfg.ReplayRatio <= 0 ? MintSpansPerStep : tape.ComputeUnreflectedHeadroom(cfg.ReplayRatio);
                int mintSpans = (int)Math.Clamp(dreamHeadroom, 0, MintSpansPerStep);
                if (mintSpans < MintSpansPerStep && !mintThrottleAnnounced)
                {
                    mintThrottleAnnounced = true;
                    Trace.Cortex.Boundary("mint.throttle", $"step={step} replay-fraction cap binds — unvested replay {tape.ReplayCount - tape.ReflectedReplayCount} vs {cfg.ReplayRatio:F2}×born-evidence {tape.BornEvidenceCount} (real {tape.RealCount} + breach {tape.BreachCount} + witnessed {tape.ReflectedCount}); mint rides the evidence rail from here");
                }
                if (mintSpans > 0) Mint(block[..mintCap], tape, journal, step, mintSpans, cohort);
                metabolism.Leak();                                           // between-block novelty decay
                curriculum.Replay(g, tape, journal, step, intakeBatch);       // the intrinsic side-channel (Campfire's verified EML pump — same era, same outcomeCredit headroom; default no-op)
            }

            accounting.Advance(CortexComputeSegmentKinds.Orchestration, Trace.NowTicks);
            if (rhythmOn)
            {
                // ── THE RHYTHM FORK (emergent metabolism — Rhythm.cs) ──  the machine chooses THIS step's input
                // source off its own senses: DAY = eat the world (school → mop-up → MIX re-admissionPlan once drained —
                // the world-channel never closes), REPLAY = eat itself (the same fold, self-generated spans,
                // outcomeCredit-throttled), CONSOLIDATION_PHASE = eat nothing (the due byte-stride became a chosen, input-free step —
                // the sleep gate below consolidates). The hard school→replay fork dissolves into this decision;
                // replay-fraction rises emergently as the frontier-residual falls and grok locks accumulate.
                rhythm.ResolveCohorts(cortex, tape, step);                // outcomes first — this step's ε/worth read the freshest yield
                double intrinsicFrontierResidual = curriculum.IntrinsicFrontierResidual;
                rhythm.FoldResidual(intrinsicFrontierResidual);           // intrinsic discoveries do not masquerade as Real corpus packets for Loom
                if (loom is not null) rhythm.FoldSpliced(tape, loom, cortex); // every prior step's world appends are spliced by now — fold their parse residual
                long headroom = cfg.ReplayRatio <= 0 ? MintSpansPerStep : tape.ComputeUnreflectedHeadroom(cfg.ReplayRatio);
                rhythmChoice = rhythm.Choose(step, new RhythmSenses(
                    Cvz: read.CvZ, Kz: read.KZ,
                    Coverage: double.IsNaN(intrinsicFrontierResidual) ? read.Coverage : 1 - Math.Clamp(intrinsicFrontierResidual, 0, 1),
                    ExcMint: selfModel.MintRate,
                    Exhausted: exhaustedAtFork, ReplayHeadroom: headroom,
                    ConsolidationPhaseDue: homeo.SleepDue(tape.GrammarByteLength, lastSleepBytes)), cfg.Seed, cortex);
                MetabolicPhases phase = rhythmChoice.Phase;
                rhythmConsolidationPhase = phase == MetabolicPhases.ConsolidationPhase;
                long worldIdLo = tape.NextId;
                accounting.Advance(CortexComputeSegmentKinds.Input, Trace.NowTicks);
                AdmitWorldAtStep(step);
                switch (phase)
                {
                    case MetabolicPhases.Day when !curriculum.Drained:
                    {
                        var phIntake = Trace.CortexPhase("intake", boundary: true);
                        var drew = curriculum.Draw(g, tape, journal, step, intakeBatch);
                        grokLock = drew.Advanced;
                        if (loom is null)
                        {
                            rhythm.FoldDraw(curriculum.LastPickCoverage);
                            double productivity = double.IsNaN(curriculum.LastPickCoverage) ? 0 : 1 - Math.Clamp(curriculum.LastPickCoverage, 0, 1);
                            rhythm.ResolveDay(cortex, in rhythmChoice, productivity, tape.NextId - worldIdLo);
                        }
                        else if (tape.NextId > worldIdLo) rhythm.MarkWorldAppends(worldIdLo, tape.NextId, in rhythmChoice, resolveDay: true);
                        else rhythm.ResolveDay(cortex, in rhythmChoice, 0, 0);
                        if (drew.Advanced && curriculum.Drained)
                            Trace.Cortex.Boundary("school.done", $"step={step} schedule crossed every bridge at {curriculum.IngestedCount}/{pool.Count} spans — mop-up era opens");
                        phIntake.Dispose();
                        break;
                    }
                    case MetabolicPhases.Day when !curriculum.Exhausted:
                    {
                        var phMopup = Trace.CortexPhase("mopup", boundary: true);
                        curriculum.Advance(g, tape, journal, step, intakeBatch);
                        if (loom is null)
                        {
                            rhythm.FoldDraw(curriculum.LastPickCoverage);
                            double productivity = double.IsNaN(curriculum.LastPickCoverage) ? 0 : 1 - Math.Clamp(curriculum.LastPickCoverage, 0, 1);
                            rhythm.ResolveDay(cortex, in rhythmChoice, productivity, tape.NextId - worldIdLo);
                        }
                        else if (tape.NextId > worldIdLo) rhythm.MarkWorldAppends(worldIdLo, tape.NextId, in rhythmChoice, resolveDay: true);
                        else rhythm.ResolveDay(cortex, in rhythmChoice, 0, 0);
                        phMopup.Dispose();
                        break;
                    }
                    case MetabolicPhases.Day:
                    {
                        var phRemix = Trace.CortexPhase("remix", boundary: true);   // day after world-drain = re-ENCOUNTER reality now, cadence-free (the never-pure invariant's day face)
                        curriculum.MixOne(cortex, g, tape, journal, step, cfg.AffirmGate);  // the intake-affirm gate rides the re-admissionPlan: a span the grammar already generates is a no-op
                        long mixed = tape.NextId - worldIdLo;
                        rhythm.ResolveDay(cortex, in rhythmChoice, mixed > 0 ? 1 : 0, mixed);
                        if (loom is not null && mixed > 0) rhythm.MarkWorldAppends(worldIdLo, tape.NextId, in rhythmChoice, resolveDay: false);
                        phRemix.Dispose();
                        break;                                                     // no residual fold — a re-admissionPlan is a re-anchor, not the frontier's edge (and Choose pins residual 0 post-exhaustion anyway)
                    }
                    case MetabolicPhases.Replay:
                    {
                        var phMint = Trace.CortexPhase("mint", boundary: true);
                        var cohort = new List<TapeEventID>(MintSpansPerStep);
                        ReplayStep(cohort);
                        rhythm.OpenCohort(cortex, step, in rhythmChoice, cohort);
                        phMint.Dispose();
                        curriculum.Mix(cortex, g, tape, journal, step, cfg.AffirmGate);   // the cadenced MIX rail rides replay steps — reality stays mounted, but an affirmed re-ingest is skipped
                        if (loom is not null && tape.NextId > worldIdLo) rhythm.MarkWorldAppends(worldIdLo, tape.NextId, in rhythmChoice, resolveDay: false);
                        break;
                    }
                    // ConsolidationPhase: no input — the step is pure reorganization; the sleep gate below consolidates.
                }
            }
            else if (!curriculum.Drained)
            {
                accounting.Advance(CortexComputeSegmentKinds.Input, Trace.NowTicks);
                AdmitWorldAtStep(step);
                var phIntake = Trace.CortexPhase("intake", boundary: true);   // the step's terminal phase
                var drew = curriculum.Draw(g, tape, journal, step, intakeBatch);
                grokLock = drew.Advanced;
                if (drew.Advanced && curriculum.Drained)
                    Trace.Cortex.Boundary("school.done", $"step={step} schedule crossed every bridge at {curriculum.IngestedCount}/{pool.Count} spans — mop-up era opens (the replay-fork stays shut until the pool is consumed)");
                phIntake.Dispose();
            }
            else if (!curriculum.Exhausted)
            {
                accounting.Advance(CortexComputeSegmentKinds.Input, Trace.NowTicks);
                AdmitWorldAtStep(step);
                var phMopup = Trace.CortexPhase("mopup", boundary: true);     // the step's terminal phase — the preventive replay-fork guard
                curriculum.Advance(g, tape, journal, step, intakeBatch);     // residual-frontier drain of what grok→move-on abandoned (schedule-order, RLEI-root)
                phMopup.Dispose();
            }
            else
            {
                accounting.Advance(CortexComputeSegmentKinds.Input, Trace.NowTicks);
                AdmitWorldAtStep(step);
                var phMint = Trace.CortexPhase("mint", boundary: true);       // autoregressive loopback + metabolism
                ReplayStep(cohort: null);
                phMint.Dispose();
                curriculum.Mix(cortex, g, tape, journal, step, cfg.AffirmGate);       // the MIX rail — reality mounted permanently, an affirmed re-ingest skipped
            }

            accounting.Advance(CortexComputeSegmentKinds.Action, Trace.NowTicks);
            if (ActBatch(cortex, tape, journal, loom, selfModel, cfg, step, actionEventIDs, actionThoughts, ref g, ref lastInduceBytes, ref lastSpliceBytes, auditCache))
            {
                // Action batches fold their tape delta and feed merge events to
                // the self-model; grammar materialization remains stride/sleep-
                // gated so downstream consumers do not rebuild per action.
                cortex.BindRuntimeGrammar(g);
            }
            cortex.SetRuntimeForkBoundary(true);
            try
            {
                foreach (CortexReward reward in cortex.Rewards) reward.OnActionBatchEnd(cortex);
            }
            finally
            {
                cortex.SetRuntimeForkBoundary(false);
            }
            bool requestedConsolidationPhase = cortex.ConsumeConsolidationPhaseRequest();
            bool requestedStop = cortex.ConsumeStopRequest();
            accounting.Advance(CortexComputeSegmentKinds.Report, Trace.NowTicks);
            curriculum.PostWorkspace(workspace);
            // ONE derivation of the step's 15 vitals, feeding BOTH consumers identically: the live
            // workspace posts and the tape's metric frame (telemetry-as-food — the frame itself is a
            // pending design ruling; the fold only removes the duplicated arithmetic).
            CortexPolicyRuntimeReceipt absorption = cortex.TryReadPolicyRuntimeReceipt(Homeostat.PolicyID, out CortexPolicyRuntimeReceipt homeostatReceipt)
                ? homeostatReceipt : default;
            long loomRules = loom?.RuleCount ?? g.Rules.Length;
            long loomSymbols = loom?.LiveSymbols ?? g.Compressed?.LongLength ?? 0;
            long loomMdlSaved = loom?.Savings ?? g.TotalSavings.Value;
            long loomInstallRevisionLagBytes = Math.Max(0, tape.GrammarByteLength - lastInduceBytes);
            long tapeResident = tape.Count;
            long tapeShed = tape.ShedEventIDs.Count;
            long tapeExecution = tape.ExecutionCount;
            long tapeBornEvidence = tape.BornEvidenceCount;
            long tapeUnreflectedReplays = tape.ReplayCount - tape.ReflectedReplayCount;
            double homeostatShadowAgreement = absorption.ShadowComparisons == 0
                ? double.NaN
                : (double)absorption.ShadowAgreements / absorption.ShadowComparisons;
            workspace.Post("cortex.loom.rules", loomRules);
            workspace.Post("cortex.loom.symbols", loomSymbols);
            workspace.Post("cortex.loom.mdl_saved", loomMdlSaved);
            workspace.Post("cortex.loom.publish_lag_bytes", loomInstallRevisionLagBytes);
            workspace.Post("cortex.tape.resident", tapeResident);
            workspace.Post("cortex.tape.shed", tapeShed);
            workspace.Post("cortex.tape.execution", tapeExecution);
            workspace.Post("cortex.tape.born_evidence", tapeBornEvidence);
            workspace.Post("cortex.tape.unreflected_dreams", tapeUnreflectedReplays);
            workspace.Post("cortex.homeostat.authority", (int)absorption.Authority);
            workspace.Post("cortex.homeostat.cached_contexts", absorption.CachedContexts);
            workspace.Post("cortex.homeostat.shadow_agreement", homeostatShadowAgreement);
            workspace.Post("cortex.homeostat.takeover_executions", absorption.GrammarExecutions);
            workspace.Post("cortex.homeostat.paid_takeovers", absorption.PaidGrammarOutcomes);
            workspace.Post("cortex.homeostat.repromotions", absorption.Readmissions);
            Span<MetricSample> metricFrame = stackalloc MetricSample[15]
            {
                new(new MetricID((ushort)CortexMetricIDs.LoomRules), NumericValue.FromI64(loomRules)),
                new(new MetricID((ushort)CortexMetricIDs.LoomSymbols), NumericValue.FromI64(loomSymbols)),
                new(new MetricID((ushort)CortexMetricIDs.LoomMdlSaved), NumericValue.FromI64(loomMdlSaved)),
                new(new MetricID((ushort)CortexMetricIDs.LoomInstallRevisionLagBytes), NumericValue.FromI64(loomInstallRevisionLagBytes)),
                new(new MetricID((ushort)CortexMetricIDs.TapeResident), NumericValue.FromI64(tapeResident)),
                new(new MetricID((ushort)CortexMetricIDs.TapeShed), NumericValue.FromI64(tapeShed)),
                new(new MetricID((ushort)CortexMetricIDs.TapeExecution), NumericValue.FromI64(tapeExecution)),
                new(new MetricID((ushort)CortexMetricIDs.TapeBornEvidence), NumericValue.FromI64(tapeBornEvidence)),
                new(new MetricID((ushort)CortexMetricIDs.TapeUnreflectedReplays), NumericValue.FromI64(tapeUnreflectedReplays)),
                new(new MetricID((ushort)CortexMetricIDs.HomeostatAuthority), NumericValue.FromI64((int)absorption.Authority)),
                new(new MetricID((ushort)CortexMetricIDs.HomeostatPolicyCachedContexts), NumericValue.FromI64(absorption.CachedContexts)),
                new(new MetricID((ushort)CortexMetricIDs.HomeostatShadowAgreement), NumericValue.FromF64(homeostatShadowAgreement)),
                new(new MetricID((ushort)CortexMetricIDs.HomeostatTakeoverExecutions), NumericValue.FromU64(absorption.GrammarExecutions)),
                new(new MetricID((ushort)CortexMetricIDs.HomeostatPaidTakeovers), NumericValue.FromU64(absorption.PaidGrammarOutcomes)),
                new(new MetricID((ushort)CortexMetricIDs.HomeostatReadmissions), NumericValue.FromI64(absorption.Readmissions)),
            };
            TapePacketCreator.AppendMetricFrame(tape, journal, step, metricFrame);
            if (step % cfg.CurveEvery == 0)
            {
                // sequential writes — no intermediate concat strings on the curve cadence
                curveW.Write(read.Row());
                curveW.Write(selfModel.RowCols());
                if (rhythmOn) curveW.Write(rhythm.RowCols());
                curveW.WriteLine(curveReadout.RowSuffix());
            }
            accounting.Advance(CortexComputeSegmentKinds.Orchestration, Trace.NowTicks);
            if (TryMatchStopCondition(workspace, cfg.StopConditions, out CortexStopCondition matchedStop))
            {
                requestedStop = true;
                Trace.Cortex.Boundary("stop.selector", $"step={step} {matchedStop.Selector}>={matchedStop.AtLeast:G17}");
            }

            if (requestedStop && !runToAbsoluteHorizon)
            {
                accounting.Advance(CortexComputeSegmentKinds.Sleep, Trace.NowTicks);
                RecordStepCompute(accounting, step); // an explicit stop observes this partial terminal step without running completion policies that were previously skipped
                observeRuntimeStop?.Invoke();
                CheckpointWriteReceipt receipt = SaveSnapshot(step + 1);
                long bytes = receipt.Bytes;
                run.Write("grammar.txt", DumpGrammar(g));
                Trace.Cortex.Boundary("stop", $"step {step + 1} · {bytes}B · alloc={receipt.AllocatedBytes}B · explicit runtime stop → {Checkpoint.FileName}");
                step++;
                break;
            }

            // ── STOP ──  the MOMENTUM law, ONE clock: the STRIDE. Both arms' mdl_saved is STAIR-STEPPED
            // — it moves only where the grammar the drive SEES refreshes (batch: the re-induce stride; loom:
            // the harvest stride — same cadence), and between refreshes the per-step slope reads exact-zero
            // REGARDLESS of the underlying trend (the STILLBORN-REPLAY bug: trunk_0193 forked at step 211 and
            // walled at 216 with meanz frozen from the step-203 stride). So the wall advances only when a fresh
            // stride reading lands: WallLock consecutive FLAT stride readings ⇒ the tape grew ~+40% while
            // savings stood still — learning-exhausted, the REAL plateau. --steps stays the hard cap. (The loom
            // phase-2 per-step clock died with per-step harvest — its continuous-savings premise was the very
            // grammar volatility that defeated every downstream stride cache.) Reads POST-DRAIN steps only
            // (exhaustedAtFork — a WALL verdict is a true plateau only once the world is consumed): the legacy
            // arm's replay era, or ANY rhythm step after the pool drained (the eras interleave there).
            if (exhaustedAtFork)
            {
                if (reads.StrideReadingLanded)
                {
                    wallStreak = reads.StrideVerdict == "WALL" ? wallStreak + 1 : 0;
                    Trace.Cortex.Boundary("wall.stride", $"step={step} stride-reading Δmdl={read.MdlSaved} verdict={reads.StrideVerdict} streak={wallStreak}/{Reads.WallLock}");
                }
                // A curriculum mounting a learner slower than the byte grammar may defer the wall while
                // that plane is still maturing — otherwise the drive stops on the wrong learner's clock
                // and the slow plane never gets a life. --steps stays the hard cap.
                bool slowPlaneStillMaturing = curriculum is ICurriculumMomentumHaltVeto haltVeto && haltVeto.VetoesMomentumHalt;
                if (!runToAbsoluteHorizon && !requestedConsolidationPhase && !slowPlaneStillMaturing
                    && reads.StrideWindowFull && wallStreak >= Reads.WallLock)
                { Trace.Note($"  drive halted: MOMENTUM WALL — savings-slope flat across {Reads.WallLock} consecutive stride readings at step {step} (a real stride-over-stride plateau: the grammar stopped improving while the tape grew)"); break; }
            }

            // ── C2 window attribution ──  a DomainMeter streak-reset landing while the cvz mask is up would be
            // the bell thrashing on breach heat — the coupled-anneal kill-line holds this at 0. Sampled here,
            // after the FORK's Draw folded the bells and before SLEEP can move the mask edge.
            if (homeoOn)
            {
                int sr = curriculum.StreakResets;
                if (homeo.CvzMasked && sr > lastStreakResets) breachWindowResets += sr - lastStreakResets;
                lastStreakResets = sr;
            }

            // ── SLEEP ──  the consolidationPhase shift: couplings-guided defrag + GC-demotion + reverse-index rebuild.
            // OFF-ARM: fires on the step cadence OR on a grok-lock (a domain grokked → the scheduler crossed a
            // bridge — ); off entirely at --interval-consolidationPhase 0 (the kill-line control arm). HOMEOSTAT-ARM: the cadence is
            // the GEOMETRIC BYTE-STRIDE (sleep when the tape grew SleepFrac× since the last — O(Δ) spacing, face 3's
            // #20 subsumed into the rest-point) with the conditions tightening/relaxing the fraction at each close;
            // grok-lock still triggers. Consolidate re-inducts the defragged+demoted tape and RETURNS the grammar,
            // so `g` carries the consolidation into the next GENERATE (exercising the tape-ref hierarchy) — no
            // forced re-induce, the stride is reset to the just-inducted tape.
            bool strideDue = homeoOn
                ? homeo.SleepDue(tape.GrammarByteLength, lastSleepBytes)
                : step > 0 && cfg.IntervalConsolidationPhase > 0 && step % cfg.IntervalConsolidationPhase == 0;
            if (rhythmOn) strideDue = rhythmConsolidationPhase;                     // the rhythm CONSUMED the byte-stride read at step top: a due consolidationPhase became this step's CHOSEN, input-free phase (shifts the consolidationPhase ≤1 step vs the gate-time read — the geometric law holds; grok-lock naps below stay same-step light consolidationPhases)
            bool slept = requestedConsolidationPhase || (homeoOn ? grokLock || strideDue
                                 : cfg.IntervalConsolidationPhase > 0 && (grokLock || strideDue));
            bool ckptConsolidationPhase = false;                                    // slept AND worth a snapshot (full consolidationPhases; light lock-storm consolidationPhases replay from the periodic cut)
            accounting.Advance(CortexComputeSegmentKinds.Sleep, Trace.NowTicks);
            if (slept)
            {
                var phSleep = Trace.CortexPhase("sleep");
                // the sleep PHASE's total-accounting frame data — Consolidate's sleep.sub covers its own organs,
                // but the phase carries more: publish/bind/thought/hooks/antiunify/close were DARK (the measured
                // 3.9s gap between sleep.sub's Σ and sleep_ms). Every stage bracketed; sleep.phase emits Σ vs total.
                long tSleep0 = Trace.NowTicks;
                long msInstallRevision = 0, msBind = 0, msThought = 0, msHooks = 0, msAntiunify = 0, msClose = 0;
                long budgetBits = homeoOn ? homeo.BudgetBits : cfg.GrammarBudgetBits;   // the working-set budget — a homeostat recurrence actuator (Hot/Heavy tighten it)
                int breachQuota = homeoOn && cfg.Breach ? homeo.BreachQuota : 0;         // the LAST close's grant (Stalled raises · Hot zeroes · relax clears); --no-breach = the control arm where grants expire unspent
                long dSleepBytes = tape.GrammarByteLength - lastInduceBytes;              // Δ since the last induce — grammar intake is the Hot sense's denominator (the sleep induce is the dominant one under grok-lock / the byte-stride)
                // THE CONSOLIDATION_PHASE LADDER: full (O(Δ) delta fold + vest + drops) on the geometric byte-stride
                // beat or under a breach grant; LIGHT (O(Δ) — the standing loom's parse) on grok-lock storms.
                // Composed from persisted state only (strideDue ← lastSleepBytes; quota ← HOME) — kill→resume takes
                // the same fork. The batch arm (--no-loom) has no standing loom and stays always-full (the oracle).
                bool fullConsolidationPhase = requestedConsolidationPhase || loom is null || strideDue || breachQuota > 0;
                // the weave scores against the PURE install revision snapshot, never the composed `g` — the
                // composed image rebases the weave model every consolidationPhase (see Consolidate's weaveBase doc)
                RePairResult? weaveBase = liveInstallRevision is { } weaveInstallRevision
                    ? weaveInstallRevision.Snapshot.ToRePairResult(cloneArrays: false)
                    : null;
                var pass = Consolidate(tape, g, memory, journal, step, cfg, budgetBits, breachQuota, cfg.Seed + (ulong)step, loom, fullConsolidationPhase, world.Weave, auditCache, weaveBase, foldedAdmissionEvents);
                long msConsolidate = Trace.ElapsedMs(tSleep0);
                g = pass.Grammar;
                long tStage = Trace.NowTicks;
                InstallRevision(g, fullConsolidationPhase ? "sleep.full" : "sleep.light", liveOverlay);
                InstallRevision sleepInstallRevision = liveInstallRevision
                    ?? throw new InvalidDataException("sleep grammar installRevision was not installed");
                curriculum.SettleInstallRevision(
                    in sleepInstallRevision, foldedAdmissionEvents,
                    id => loom is not null && loom.ParsedLenOf(id.Value) >= 0,
                    cortex.LoopLineage, tape, journal, step);
                foldedAdmissionEvents.Clear();
                msInstallRevision = Trace.ElapsedMs(tStage);
                tStage = Trace.NowTicks;
                cortex.BindRuntimeGrammar(g);
                msBind = Trace.ElapsedMs(tStage);
                tStage = Trace.NowTicks;
                int sleepThoughtMints = selfModel.ObserveThought(pass.Events, 256);   // the sleep re-induce's merge stream IS cognition — the composed path's dominant induce, so this is the thought channel's food (else it goes dark, as measured)
                if (fullConsolidationPhase && dSleepBytes > 0) lastInduceOpb = (double)pass.Events.Count / dSleepBytes;   // light consolidationPhases re-derive nothing — the Hot sense holds its last full reading
                lastInduceBytes = tape.GrammarByteLength;
                lastSpliceBytes = tape.GrammarByteLength;               // the full delta fold re-anchored grammar accounting (drops may have SHRUNK the intake view)
                lastSleepBytes  = tape.GrammarByteLength;
                lastBitsPerSpan = (double)pass.GrammarBits / Math.Max(1, tape.RealCount);   // the Heavy sense — surface bits per REAL span (honest denominator: dreams don't dilute it)
                if (sleepThoughtMints > 0)
                    TapePacketCreator.AppendSelfSignal(tape, journal, step, "thought", $"sleep-mint={sleepThoughtMints};events={pass.Events.Count}");
                msThought = Trace.ElapsedMs(tStage);
                tStage = Trace.NowTicks;
                totalEvicted += pass.Evicted; totalPromoted += pass.Promoted; totalDemoted += pass.Demoted;

                // ── THE CONVERGENCE INVARIANT (phase 3 kill-line) ──  Δnodes_dream = rule-count delta consolidationPhase-over-
                // consolidationPhase (≤ 0 on re-admissionPlan of learned material — the re-greed + self-compression net-reduce);
                // nodes/byte must fall toward 0 (sublinear grammar growth in cumulative experience); residents
                // plateau while the view climbs (the rolling window). `mem` is telemetry (non-deterministic —
                // trace plane only, never a journal/curve artifact).
                lastDNodes = lastConsolidationPhaseRules < 0 ? 0 : pass.Grammar.Rules.Length - lastConsolidationPhaseRules;
                lastConsolidationPhaseRules = pass.Grammar.Rules.Length;
                // the motion suite's consolidationPhase-scoped inputs: births = rules ADDED (Δrules up), churn = total structural
                // turnover this consolidationPhase (evict+promote+demote+shed+drop) — the refactor-verdict reads them off the curve.
                lastBirths = Math.Max(0, lastDNodes);
                lastChurn = pass.Evicted + pass.Promoted + pass.Demoted + pass.ShedN + pass.DropN;
                Trace.Cortex.Boundary("converge", $"step={step} nodes={pass.Grammar.Rules.Length} Δnodes/grammar_byte={(tape.GrammarByteLength > 0 ? (double)pass.Grammar.Rules.Length / tape.GrammarByteLength : 0):F6}"
                    + $" residents={tape.Count} shed={tape.ShedCount}(+{pass.ShedN}) dropped={tape.DroppedCount}(+{pass.DropN}) merged={pass.Merged} view={tape.ByteLength}B mem={GC.GetTotalMemory(false) >> 20}MB");
                long beforeConsolidationPhaseHooks = tape.GrammarByteLength;
                foreach (CortexReward reward in cortex.Rewards) reward.OnConsolidationPhase(cortex, step);
                if (tape.GrammarByteLength != beforeConsolidationPhaseHooks)
                {
                    HarvestTape(cortex, tape, journal, loom, cfg, step, ref g, ref lastInduceBytes, ref lastSpliceBytes, auditCache);
                    lastSleepBytes = tape.GrammarByteLength;
                }
                msHooks = Trace.ElapsedMs(tStage);
                tStage = Trace.NowTicks;

                // GENERALIZE — after defrag+GC, grow the PERSISTENT paradigm over the
                // recency window and mint its slots into the working grammar (a slot [S]={cat,dog,…} + skeleton
                // replaces N literal lines). Slotted rules REPLACE clusters of literals → bits drop → the budget
                // breathes. The escape route the memory budget selects for. CADENCE sleeps only — a grok-lock
                // stretch fires sleep EVERY step, and full generalization per step was the measured 70-107s/step
                // stall; defrag+GC still ride every lock, the paradigm grows on the periodic beat (the
                // byte-stride beat when the homeostat governs — same law, O(Δ) cadence — or on its ForceGeneralize
                // actuation, the Heavy escape). The window + seeded growth keep the pass O(window) FLAT. Deterministic.
                bool auDue = requestedConsolidationPhase || (homeoOn ? strideDue || homeo.ForceGeneralize : cfg.IntervalConsolidationPhase > 0 && step % cfg.IntervalConsolidationPhase == 0);
                int consolidationPhaseSlotted = 0; long consolidationPhaseBitsSaved = 0;             // THIS consolidationPhase's generalization yield (lastSlotted holds the sparkline's last-pass gauge — stale for the wasted-sleep read)
                if (cfg.Antiunify && auDue)
                {
                    // `g` may still carry yesterday's side layer; the install revision
                    // snapshot is the pure binary authority used to bind this mint.
                    RePairResult binaryBase = liveInstallRevision is { } installed
                        ? installed.Snapshot.ToRePairResult(cloneArrays: false)
                        : g;
                    var au = AntiUnify.Consolidate(tape, g, memory.ConsolidationPhaseParadigm, AuWindowSpans, AuMaxIter, AuMaxCand);
                    g = au.Grammar;
                    lastSlotted = au.SlottedRules; lastBitsSaved = au.BitsSaved;
                    consolidationPhaseSlotted = au.SlottedRules; consolidationPhaseBitsSaved = au.BitsSaved;
                    Trace.Cortex.Boundary("antiunify.timing", $"step={step} tokenize={au.Timing.TokenizeMs:F2}ms growth={au.Timing.GrowthMs:F2}ms flat_mdl={au.Timing.FlatMdlMs:F2}ms slotted_mdl={au.Timing.SlottedMdlMs:F2}ms mint={au.Timing.MintMs:F2}ms base_visit={au.Timing.BaseVisitMs:F2}ms base_copy={au.Timing.BaseCopyMs:F2}ms expand={au.Timing.RuleExpansionMs:F2}ms overlay_visit={au.Timing.OverlayVisitMs:F2}ms image_copied={au.Operations.ImageRulesCopied}");
                    journal.Consolidation(step, "antiunify · " + au.FormatJournalNote());
                    GrammarOverlay? overlay = GrammarOverlay.TryFromComposed(binaryBase, in g, liveOverlay);
                    bool overlayChanged = overlay is not null && (liveOverlay is null || !overlay.ContentEquals(liveOverlay));
                    if (overlayChanged)
                    {
                    // AntiUnify is a typed side layer. Install the pure Loom base as
                        // the authority and carry the overlay separately; inserting its
                        // rules into the flat base would make the next binary mint look
                        // like a prefix rewrite and force a false full rebuild.
                        InstallRevision(binaryBase, "antiunify", overlay);
                        Trace.Cortex.Boundary("antiunify", $"+{au.SlottedRules} slot-rules · {au.BitsSaved} bits saved by generalization");
                    }
                    cortex.BindRuntimeGrammar(g);
                }
                msAntiunify = Trace.ElapsedMs(tStage);
                tStage = Trace.NowTicks;

                // ── the homeostat's boundary close (face 1) ──  fold the consolidationPhase's yield, classify the EMA'd senses,
                // actuate ONE notch (or relax to rest). BREACH is MOUNTED:
                // the quota granted at the PREVIOUS close ran AnnealEvict.Breach inside this consolidationPhase's Consolidate,
                // and the C2 mask opens here iff it fired — held until the first post-LOWER InduceOutcomeCredited (the
                // grammar-identity change: the next day-stride induce or the next consolidationPhase's internal one, whichever
                // lands first), so the bell reads post-LOWER only and the fast plane cannot cool on breach-heated
                // cvz (bell-vs-breach dissolved — the coupled anneals never fight).
                ConsolidationPhaseYield completedConsolidationPhase = new(
                    pass.Evicted,
                    pass.Promoted,
                    pass.Demoted,
                    consolidationPhaseSlotted,
                    consolidationPhaseBitsSaved,
                    Breached: pass.Breach.Mints + pass.Breach.Compacted + pass.Breach.Evicted + pass.Breach.Refolded);
                if (homeoOn)
                {
                    bool wasMasked = homeo.CvzMasked;
                    homeo.MaskCvz(pass.Breach.Fired);                  // one assignment covers both edges: an un-fired consolidationPhase's internal induce IS the post-LOWER re-induce (clear); a fired consolidationPhase (re-)arms
                    if (pass.Breach.Fired)
                    {
                        homeo.SpendBreach();                           // the consolidationPhase spent its grant — Stalled re-grants at the close below (the oscillate cadence); a stale grant must never ride into a non-stalled era
                        breachConsolidationPhases++;
                    }
                    else if (wasMasked) Trace.Cortex.Boundary("breach.c2", $"step={step} cvz mask cleared — post-LOWER re-induce (consolidationPhase)");
                    homeo.CloseSleep(cortex, in completedConsolidationPhase);
                    if (homeo.BreachQuota > 0)
                    {
                        if (cfg.Breach) Trace.Cortex.Boundary("breach.grant", $"step={step} quota={homeo.BreachQuota} granted (Stalled) — breach fires next consolidationPhase unless Hot zeroes it");
                        else Trace.Cortex.Warn("breach.quota", $"step={step} quota={homeo.BreachQuota} granted (Stalled) — --no-breach control arm, expires unspent");
                    }
                    Trace.Cortex.Boundary("homeostat", homeo.Line());
                }
                if (rhythmOn && rhythmConsolidationPhase)
                    rhythm.ResolveConsolidationPhase(cortex, in rhythmChoice, in completedConsolidationPhase);
                ckptConsolidationPhase = fullConsolidationPhase;                                 // light consolidationPhases don't earn a 26MB snapshot each — a kill replays them deterministically from the last full-consolidationPhase/periodic cut
                msClose = Trace.ElapsedMs(tStage);
                long msSleepWall = Trace.ElapsedMs(tSleep0);
                long msSleepSum = msConsolidate + msInstallRevision + msBind + msThought + msHooks + msAntiunify + msClose;
                Trace.Cortex.Boundary("sleep.phase", $"step={step} {(fullConsolidationPhase ? "FULL" : "light")} ms: consolidate={msConsolidate} install={msInstallRevision} bind={msBind} thought={msThought} hooks={msHooks} antiunify={msAntiunify} close={msClose} Σ={msSleepSum}/{msSleepWall}");
                phSleep.Dispose();
            }

            CompleteStep(accounting, step); // compute telemetry ends at the completed-step callback, before durability IO

            // ── CHECKPOINT ──  the safe-to-kill law: snapshot the WHOLE machine (grammar · tape · journal ·
            // curriculum · reads · self-model · controller · metabolism · memory + the loop's own locals) to
            // checkpoint.bin, atomically, and drop a readable grammar.txt beside it (the diagnostician's mid-run
            // window; LAND overwrites it with the final). Cadence: --checkpoint-every N (AND every sleep), default
            // every sleep pass — the post-consolidation state is the natural snapshot; sleep-off runs fall back to
            // every 25 steps so a long flatpool diagnosis is never uncheckpointed. Outside the step reaper: the
            // snapshot is durability's cost, not the machine's, so it never trips the step.slow alarm.
            if (ShouldCheckpoint(cfg, step, ckptConsolidationPhase, homeoOn))
            {
                var phCkpt = Trace.CortexPhase("ckpt");
                CheckpointWriteReceipt receipt = SaveSnapshot(step + 1);
                long bytes = receipt.Bytes;
                run.Write("grammar.txt", DumpGrammar(g));
                Trace.Cortex.Boundary("ckpt", $"step {step + 1} · {bytes}B · alloc={receipt.AllocatedBytes}B · tape {tape.Count} spans · journal {journal.LineCount} lines → {Checkpoint.FileName}");
                phCkpt.Dispose();
            }
            }
            finally
            {
                if (!accounting.Completed)
                {
                    try
                    {
                        accounting.Advance(CortexComputeSegmentKinds.Verifier, Trace.NowTicks);
                        RecordStepCompute(accounting, step);
                    }
                    catch (Exception accountingException)
                    {
                        Trace.Cortex.Warn("compute.accounting", $"step={step} fallback emission failed: {accountingException.GetType().Name}: {accountingException.Message}");
                    }
                }
            }
        }

        if (curriculum is ReplayCalc terminalAnytimeReplay)
        {
            EmlAnytimeCurvePoint? terminalPoint = terminalAnytimeReplay.SettleAnytimeRunTerminal(cortex, step);
            if (terminalPoint is EmlAnytimeCurvePoint settled)
                Trace.Cortex.Boundary("anytime.terminal", $"step={step} point={settled.PointID} window={settled.WindowIndex} pending=0 zero-spend=1 digest={settled.Digest}");
        }
        // A resumed run can reach its persisted end without another live step. Reconcile only
        // an already-funded Homeostat lease from its immutable funding-bound seed custody; this
        // never admits world input, advances the parent horizon, or opens a new funding decision.
        cortex.SetCompletedStepForkBoundary(true);
        try { cortex.TryRunHomeostatBoundaryAtStep(cfg, terminalRecoveryOnly: true); }
        finally { cortex.SetCompletedStepForkBoundary(false); }
        }
        finally
        {
            try
            {
                computeW.Flush();
                CortexComputeAccountingReport.Write(run.PathOf("compute.tsv"), run.PathOf("compute.report.tsv"), requireZeroDark: true);
            }
            catch (Exception reportException)
            {
                Trace.Cortex.Warn("compute.report", $"compute report emission failed: {reportException.GetType().Name}: {reportException.Message}");
            }
        }
        // Plot renderers finalize from their writer Dispose boundary. Close both streams before
        // hashing the run closure so the authority covers the final curve/compute bytes and the
        // renderer's completed side effects are no longer in flight.
        // ── PHASE 2 · LAND THE RUN ──  curve.tsv + journal.log are ALREADY on disk (incremental, the safe-to-kill
        // law); land the self-signal stream + the self-stream's decay/vocabulary report + the final grammar + sample.
        run.Write("selfstream.txt", selfModel.Report());                // the mint-rate decay curve + the machine's dynamics vocabulary
        Trace.Note(selfModel.Report());                                // telegraph the kill-line report at drive end
        if (homeoOn)
        {
            // the C2 check (bell-vs-breach, the coupled-anneal no-thrash proof): breach activity beside the
            // three thrash reads that must stay 0 while it fires — bell streak-resets inside mask windows,
            // condition-driven actuator sign-reversals within the hysteresis horizon, and the wasted-sleep rate.
            string c2 = $"breach consolidationPhases {breachConsolidationPhases} · streak-resets inside cvz-mask windows {breachWindowResets} (run total {curriculum.StreakResets})"
                      + $" · actuator sign-reversals {homeo.SignReversals} (horizon {Homeostat.ReversalHorizon} closes) · wasted sleeps {homeo.WastedSleeps}/{homeo.SleepsClosed}";
            run.Write("c2.txt", c2 + "\n");
            Trace.Note("  C2 · " + c2);
            if (homeo.PolicyArmed)
            {
                CortexPolicyRuntimeReceipt policyReceipt = cortex.ReadPolicyRuntimeReceipt(Homeostat.PolicyID);
                string homeostatReport = homeo.Report(in policyReceipt);
                run.Write("homeostat.txt", homeostatReport);
                Trace.Note(homeostatReport);
            }
        }
        if (rhythmOn)
        {
            run.Write("rhythm.txt", rhythm.Report());                  // the emergence report — phase census, ε trail, cohort yield, the rhythm channel's self-prediction
            Trace.Note(rhythm.Report());
        }
        RePairResult finalG;
        if (loom is not null)
        {
            // LAND uses the same standing authority as the loop: fold any terminal
            // tape delta, pump its winners, then emit the canonical view once.
            FoldLoomDelta(tape, loom, foldedAdmissions: foldedAdmissionEvents);
            finalG = loom.Result(tape);
        }
        else
        {
            finalG = Engine.Induce(tape, cfg.WScale).Result;          // the batch arm remains the differential oracle
        }
        InstallRevision(finalG, "land", liveOverlay);
        InstallRevision landInstallRevision = liveInstallRevision
            ?? throw new InvalidDataException("land grammar installRevision was not installed");
        curriculum.SettleInstallRevision(
            in landInstallRevision, foldedAdmissionEvents,
            id => loom is not null && loom.ParsedLenOf(id.Value) >= 0,
            cortex.LoopLineage, tape, journal, step);
        foldedAdmissionEvents.Clear();
        if (loom is not null && cortex.LoopLineage is not null)
        {
            // The first land install revision emits its custody packet. Fold that
            // packet before the terminal image is written; otherwise the tape
            // carries a post-install revision delta that the persisted Loom never
            // consumed. Re-publish the now-authoritative result without minting
            // a second lineage packet, closing the terminal rail exactly once.
            FoldLoomDelta(tape, loom);
            finalG = loom.Result(tape);
            InstallRevision(finalG, "land.final", liveOverlay, emitLineagePacket: false);
            TapeDelta terminalLineageDelta = tape.DrainDelta();
            if (!terminalLineageDelta.IsEmpty)
                throw new InvalidDataException("terminal land left an unfurled tape delta after the bounded lineage fold");
        }
        run.Write("grammar.txt", DumpGrammar(finalG));
        run.Write("grammar.rules.txt", DumpGrammarRules(finalG));
        byte[] finalSample = liveInstallRevision is { } finalInstallRevision
            ? energy.Generate(finalInstallRevision, cfg.BlockLen, cfg.Seed, metabolism, controller.Current)
            : energy.Generate(finalG, cfg.BlockLen, cfg.Seed, metabolism, controller.Current);
        run.Write("sample.txt", Encoding.UTF8.GetString(finalSample));   // the landed sample speaks at the drive's FINAL adapted weights
        cortex.BindRuntimeGrammar(finalG);
        cortex.BindRuntimeSnapshot(null);
        cortex.BindCompletedStepForkSeed(null);
        foreach (CortexActionPolicy policy in cortex.ActionPolicies) policy.OnRunEnd(cortex);
        foreach (CortexReward reward in cortex.Rewards) reward.OnRunEnd(cortex);
        cortex.BindRuntimeCurriculum(null);
        if (curriculum is ReplayCalc finalAnytimeReplay)
        {
            if (!File.Exists(run.PathOf("eml_anytime_curve.tsv")))
                finalAnytimeReplay.AnytimeCurve.WriteTSV(run.PathOf("eml_anytime_curve.tsv"));
            EmlAnytimeCurvePlot.Write(finalAnytimeReplay.AnytimeCurve, run);
        }
        if (world.ExternalWorld is { TotalItems: > 0 } finalWorld)
        {
            AdmissionReceipt terminalAdmission = lastWorldAdmission with
            {
                Step = Math.Max(0, step - 1),
                CursorBefore = finalWorld.Cursor,
                CursorAfter = finalWorld.Cursor,
                PlannedItems = 0,
                AdmittedItems = 0,
                AdmittedBytes = 0,
                RemainingItems = finalWorld.Remaining,
                TotalItems = finalWorld.TotalItems,
                Terminal = finalWorld.IsTerminal,
                CursorDigest = finalWorld.ComputeDigest(),
                Schedule = finalWorld.ActiveScheduleID,
                ScheduleDigest = finalWorld.ActiveScheduleDigest,
                AdmittedDomains = 0,
                DomainDigest = finalWorld.EmptyActiveDomainDigest(),
            };
            terminalAdmission.Validate();
            run.Write("world-admission.ron", terminalAdmission.ToRon());
            worldAdmissionW?.Dispose();
        }
        if (cortex.ForkRailRole == CortexForkRailRoles.Unknown && !string.IsNullOrWhiteSpace(cfg.DeepRematchGateDigest))
        {
            DeepRematchGateConfig gate = DeepRematchGate.DecodeConfig(File.ReadAllBytes(run.PathOf("deep-rematch-gate.ron")));
            DeepRematchReceiptEmission.EmitLegacy(run, cortex, curriculum as ReplayCalc, cfg, step, rung0Cursor,
                checked(gate.A3PreludeSteps + 1), checked(gate.A3PreludeSteps + gate.EvaluationSteps));
        }
        reads.FlushCheckpointOutput();
        // Legacy Homeostat receipts retain their historical terminal projection;
        // repository-native links remain transition-owned and are never synthesized.
        cortex.EmitPolicyBoundaryLoopClosureLinkAttemptsBeforeAuthoritySeal(run, tape, journal);
        // Completion readers resolve evacuated events while the world remains mounted;
        // this callback must precede the native seal so it cannot append after terminal custody.
        captureCompletionBeforeWorldDispose?.Invoke(cortex);
        if (checkpointRunEnd)
        {
            // LAND may append lineage, grammar, policy, and terminal evidence;
            // seal only after every state-bearing transition has landed.
            bool terminalMutationRail = mutationRailReady && File.Exists(run.PathOf(Checkpoint.DeltaFileName));
            string terminalBasePhysical = terminalMutationRail
                ? Checkpoint.PhysicalSHA256(File.ReadAllBytes(run.PathOf(Checkpoint.FileName))) : "";
            long terminalBaseLength = terminalMutationRail ? new FileInfo(run.PathOf(Checkpoint.FileName)).Length : 0;
            cortex.FlushLoopClosureObjects();
            if (curriculum is ICurriculumTerminalTransition terminalTransition)
                terminalTransition.CaptureTerminalTransition(cortex, run, tape, journal);
            CheckpointWriteReceipt receipt = SaveSnapshot(step);
            bool terminalBaseStable = !terminalMutationRail ||
                (new FileInfo(run.PathOf(Checkpoint.FileName)).Length == terminalBaseLength
                 && string.Equals(terminalBasePhysical,
                     Checkpoint.PhysicalSHA256(File.ReadAllBytes(run.PathOf(Checkpoint.FileName))), StringComparison.Ordinal));
            if (terminalMutationRail && !terminalBaseStable)
                throw new InvalidDataException("terminal mutation checkpoint rewrote its canonical keyframe");
            (string terminalBase, string terminalChain) = CheckpointDelta.ReadPhysicalAuthority(run.Dir);
            long terminalRailBytes = File.Exists(run.PathOf(Checkpoint.DeltaFileName))
                ? new FileInfo(run.PathOf(Checkpoint.DeltaFileName)).Length : 0;
            string runID = Cogito.Run.RunIDFromDirectory(run.Dir);
            int terminalObjects = LoopClosureEvidenceStore.ReadObject(run.Dir, runID).Count;
            Trace.Cortex.Boundary("run.ckpt", $"step {step} · mode={(terminalMutationRail ? "mutation" : "keyframe")} · {receipt.Bytes}B · alloc={receipt.AllocatedBytes}B · base_stable={(terminalBaseStable ? "yes" : "NO")} · rail={terminalRailBytes}B · objects={terminalObjects} · base={terminalBase} · chain={terminalChain}");
        }
        curveW.Dispose();
        computeW.Dispose();
        RunAuthority.WriteCompleted(run, cfg, step);
        int finalViewCount = tape.Count + tape.ShedEventIDs.Count;
        Trace.Note($"  → best Δmdl step {reads.BestStep} = {reads.BestMdl} · drained {curriculum.IngestedCount}/{pool.Count} spans · fork {(forkStep < 0 ? "NEVER (world un-consumed at the step cap)" : $"step {forkStep} @ {forkVolumeFrac:F2} coverage")} · final tape {finalViewCount} view spans ({tape.Count} resident + {tape.ShedEventIDs.Count} shed; {tape.RealCount} real · {tape.BreachCount} breach · {tape.ReplayCount} replay/{tape.ReflectedReplayCount} vested · {tape.ReflectedCount} witnessed · {tape.ExecutionCount} execution) · journal {journal.LineCount} lines · curve.tsv landed");
        if (cfg.AffirmGate >= 0)   // the self-maintenance check: MIX re-admissionPlans the grammar already generated → skipped, no tape byte banked
            Trace.Note($"  → intake-affirm gate (θ={cfg.AffirmGate:F2}) · {curriculum.MixAffirmSkips} MIX re-ingests SKIPPED (the grammar already generates them — the tape did not grow from re-observation)");
        return 0;
    }

    private static bool TryMatchStopCondition(CogitoWorkspace workspace, CortexStopCondition[]? conditions, out CortexStopCondition matched)
    {
        foreach (CortexStopCondition condition in conditions ?? [])
        {
            if (workspace.TryReadDouble(condition.Selector, out double value) && value >= condition.AtLeast)
            {
                matched = condition;
                return true;
            }
        }
        matched = default;
        return false;
    }

    private static bool ActBatch(Cortex cortex, Tape tape, Journal journal, Loom? loom, SelfStream selfModel, CortexRunConfig cfg, int step,
        List<TapeEventID> eventIDs, List<MergeEvent> actionThoughts, ref RePairResult grammar, ref long lastInduceBytes, ref long lastSpliceBytes, PearlAuditCache auditCache)
    {
        bool installed = false;
        bool deferredHarvest = false;
        bool deferredInstallRevision = false;
        actionThoughts.Clear();
        List<CortexActionArgument> arguments = cortex.GetActionArguments();
        List<CortexObservationField> fields = cortex.GetObservationFields();
        GrammarRule[]? actionCoverRules = null;
        Engine.GrammarCover? actionAffirmCover = null;
        int actions = CortexConfigTokens.ResolveActionsPerStep(cfg);
        for (int slot = 0; slot < actions; slot++)
        {
            arguments.Clear();
            fields.Clear();
            CortexActionPolicy? owner = null;
            CortexAction action = CortexAction.None;
            foreach (CortexActionPolicy policy in cortex.ActionPolicies)
            {
                arguments.Clear();
                if (!policy.TryChooseAction(cortex, arguments, out action)) continue;
                owner = policy;
                break;
            }
            if (owner is null || action.Tool == CortexTool.None) break;

            string source = owner.GetSource(cortex, action);
            byte[] actionArgumentPacket = TapePacketCreator.EncodeActionRequest(action, arguments);
            string actionArgumentDigest = TapePacketCreator.ComputeSHA256(actionArgumentPacket);
            CortexActionAdmissionDecision requestDecision = owner.EvaluateActionRequestAdmission(cortex, action, arguments);
            requestDecision.Validate();
            TapePacketCreator.AppendActionAdmission(
                tape,
                journal,
                step,
                CortexActionAdmissionPhases.Request,
                action.Tool.Name,
                source,
                actionArgumentDigest,
                "",
                requestDecision.Species,
                requestDecision.Reason);
            if (!requestDecision.Admitted) continue;

            CortexObservation observation = action.Tool.Act(cortex, action, arguments, fields);
            journal.RecordAction(step, cortex.EpisodeID, action, arguments, observation, fields);
            eventIDs.Clear();
            if (cfg.AffirmGate >= 0 && !ReferenceEquals(actionCoverRules, grammar.Rules))
            {
                actionCoverRules = grammar.Rules;
                actionAffirmCover = actionCoverRules.Length > 0
                    ? cortex.GrammarCover ?? new Engine.GrammarCover(actionCoverRules)
                    : null;
            }
            byte[] executionBytes = TapePacketCreator.EncodeActionExecution(cortex, owner, action, arguments, fields);
            CortexActionAdmissionDecision executionDecision = owner.EvaluateActionExecutionAdmission(
                cortex, action, arguments, observation, fields);
            executionDecision.Validate();
            string executionPacketDigest = TapePacketCreator.ComputeSHA256(executionBytes);
            if (!executionDecision.Admitted)
            {
                owner.OnActionExecutionAdmission(cortex, action, in executionDecision);
                TapePacketCreator.AppendActionAdmission(
                    tape,
                    journal,
                    step,
                    CortexActionAdmissionPhases.Execution,
                    action.Tool.Name,
                    source,
                    actionArgumentDigest,
                    executionPacketDigest,
                    executionDecision.Species,
                    executionDecision.Reason);
                continue;
            }

            bool admittedActionExecution = TapePacketCreator.TryAppendActionExecution(cortex, owner, action,
                executionBytes, actionAffirmCover, cfg.AffirmGate, out TapeEventID actionEventID);
            if (!admittedActionExecution)
            {
                CortexActionAdmissionDecision packetDecision = CortexActionAdmissionDecision.Deny("tape-packet-admission");
                owner.OnActionExecutionAdmission(cortex, action, in packetDecision);
                TapePacketCreator.AppendActionAdmission(
                    tape,
                    journal,
                    step,
                    CortexActionAdmissionPhases.Execution,
                    action.Tool.Name,
                    source,
                    actionArgumentDigest,
                    executionPacketDigest,
                    packetDecision.Species,
                    packetDecision.Reason);
                continue;
            }
            CortexActionAdmissionDecision admittedExecution = CortexActionAdmissionDecision.Admit("tape-packet-admission");
            owner.OnActionExecutionAdmission(cortex, action, in admittedExecution);
            eventIDs.Add(actionEventID);
            foreach (CortexReward reward in cortex.Rewards) reward.OnAction(cortex, action, arguments);
            owner.AppendDomainEvents(cortex, action, arguments, observation, fields, eventIDs);
            owner.OnObservation(cortex, action, arguments, observation, fields, executionBytes, eventIDs);
            foreach (CortexReward reward in cortex.Rewards)
                reward.OnObservation(cortex, action, arguments, observation, fields, eventIDs);

            if (eventIDs.Count > 0)
            {
                installed = true;
                if (owner.HarvestsAfterBatch || owner.InstallsRevisionAfterBatch)
                {
                    deferredHarvest = true;
                    deferredInstallRevision |= owner.InstallsRevisionAfterBatch;
                }
                else HarvestTape(cortex, tape, journal, loom, cfg, step, ref grammar, ref lastInduceBytes, ref lastSpliceBytes, auditCache, actionThoughts, materialize: false);
            }
            foreach (CortexReward reward in cortex.Rewards)
                reward.OnActionHarvest(cortex, action, arguments, observation, fields);
        }
        if (deferredHarvest)
            HarvestTape(cortex, tape, journal, loom, cfg, step, ref grammar, ref lastInduceBytes, ref lastSpliceBytes, auditCache, actionThoughts, materialize: deferredInstallRevision);
        if (actionThoughts.Count > 0)
        {
            int thoughtMints = selfModel.ObserveThought(actionThoughts, 256);
            if (thoughtMints > 0)
                TapePacketCreator.AppendSelfSignal(tape, journal, step, "thought", $"action-mint={thoughtMints};events={actionThoughts.Count}");
        }
        foreach (CortexActionPolicy policy in cortex.ActionPolicies) policy.OnActionBatchEnd(cortex);
        return installed;
    }

    private static void HarvestTape(Cortex cortex, Tape tape, Journal journal, Loom? loom, CortexRunConfig cfg, int step,
        ref RePairResult grammar, ref long lastInduceBytes, ref long lastSpliceBytes, PearlAuditCache auditCache,
        List<MergeEvent>? thoughts = null, bool materialize = true)
    {
        if (loom is not null)
        {
            FoldLoomDelta(tape, loom, thoughts);
            if (materialize) grammar = loom.Result(tape);
            lastSpliceBytes = tape.GrammarByteLength;
        }
        else
        {
            (RePairResult induced, List<MergeEvent> events, _) = InduceOutcomeCredited(tape, journal, step, cfg.WScale, cfg.CrossReflect, auditCache);
            grammar = induced;
            thoughts?.AddRange(events);
        }
        if (materialize)
        {
            lastInduceBytes = tape.GrammarByteLength;
            cortex.BindRuntimeGrammar(grammar);
        }
    }

    /// Apply every tape transition since the previous fold without scanning the
    /// resident view. The caller chooses when to harvest Result(tape); action
    /// batches that do not expose a new grammar only pay this O(Δ) mutation path.
    private static LoomMutationReceipt FoldLoomDelta(Tape tape, Loom loom, List<MergeEvent>? events = null, List<TapeEventID>? foldedAdmissions = null)
    {
        TapeDelta delta = tape.DrainDelta();
        long started = Trace.NowTicks;
        LoomMutationReceipt appliedReceipt = loom.ApplyTapeDelta(tape, in delta);
        long applied = Trace.NowTicks;
        LoomMutationReceipt pumpReceipt = loom.Pump(events);
        long pumped = Trace.NowTicks;
        RecordFoldedPatternGrammarAdmissions(tape, in delta, foldedAdmissions);
        LoomMutationReceipt receipt = appliedReceipt + pumpReceipt;
        if (!delta.IsEmpty)
            Trace.Cortex.Boundary("loom.delta",
                $"appended={delta.Appended.Length} reflected={delta.Reflected.Length} shed={delta.Shed.Length} dropped={delta.Dropped.Length} " +
                $"apply_ms={Trace.ElapsedMsPrecise(started, applied):F3} pump_ms={Trace.ElapsedMsPrecise(applied, pumped):F3} " +
                $"apply_touched={appliedReceipt.TouchedSymbols} apply_keys={appliedReceipt.TouchedCountKeys} " +
                $"pump_touched={pumpReceipt.TouchedSymbols} pump_keys={pumpReceipt.TouchedCountKeys} pump_rules={pumpReceipt.MintedRules} " +
                $"pump_heap_mutations={pumpReceipt.HeapMutations} pump_heap_keys={pumpReceipt.HeapChangedKeys} " +
                $"touched={receipt.TouchedSymbols} keys={receipt.TouchedCountKeys} rules={receipt.MintedRules} " +
                $"heap_mutations={receipt.HeapMutations} heap_keys={receipt.HeapChangedKeys}");
        return receipt;
    }

    private static void RecordFoldedPatternGrammarAdmissions(Tape tape, in TapeDelta delta, List<TapeEventID>? foldedAdmissions)
    {
        if (foldedAdmissions is null) return;
        for (int i = 0; i < delta.Appended.Length; i++)
        {
            TapeEventID eventID = delta.Appended[i];
            if (tape.TryGetEventView(eventID, out TapeEventView view)
                && (view.Source == "eml:theory-grammar" || view.Source == "repository:theory")
                && view.Provenance == Provenances.Reflected
                && !foldedAdmissions.Contains(eventID))
                foldedAdmissions.Add(eventID);
        }
    }

    // ── INDUCE (vested) ──  ONE induce under the provenance count measure: weighted Re-Pair over the tape's
    // epistemics, then — ARMED ONLY (wScale>1) — the corroboration pass: Audit co-walks the fresh grammar against
    // the tape's span provenance, Corroborate vests the replay spans a real corroboration exercised (one journal `vest`
    // line per transition), and the audit rides out so Gc can price rent in weighted uses. wScale=1 is today's
    // machine byte-identically: the unweighted induce path, no audit, no vest lines (the degenerate control arm —
    // Replay spans exist on the tape but nothing weighs them until the scale arms).
    private static (RePairResult G, List<MergeEvent> Events, PearlAudit? Audit) InduceOutcomeCredited(Tape tape, Journal journal, int step, int wScale, bool crossReflect = false, PearlAuditCache? auditCache = null)
    {
        var (_, _, g, events) = Engine.InduceTraced(tape, wScale);
        PearlAudit? audit = null;
        if (wScale > 1)
        {
            var a = auditCache?.Get(tape, in g, wScale, crossReflect) ?? Pearl.Audit(tape, in g, wScale, crossReflect);
            List<TapeEventID>? vestedIDs = auditCache is null ? null : new List<TapeEventID>();
            int vested = Pearl.Corroborate(a, tape, journal, step, vestedIDs);
            if (vested > 0) Trace.Cortex.Boundary("vest", $"step={step} vested={vested} · tape real={tape.RealCount} replay={tape.ReplayCount} vested={tape.ReflectedReplayCount}");
            // sync the cache across the reflection so the NEXT stride's audit extends append-only
            // instead of full-rebuilding off the NonAppendRevision bump
            if (vestedIDs is { Count: > 0 }) auditCache!.RepriceReflected(tape, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vestedIDs));
            audit = a;
        }
        return (g, events, audit);
    }

    // ── MINT ──  split the generated block into lines, decode-with-replacement to valid UTF-8 (generated bytes can
    // break multi-byte sequences — the lossy repair is sound; the node's utterance, not consensus corpus), append
    // the first `maxSpans` onto the hot tape as node0's spans, and record the mint to the durable journal.
    // THE SPAN-RATE BOUND: the byte cap alone let a 16KB block mint ~300 line-spans PER STEP once the tape was
    // large (line-aware generation emits many bounded lines) — a mint firehose that grew the life ~16KB/step toward
    // a ~28MB/700k-span tape by step 2000, defeating every bounded organ downstream with unbounded INPUT. The replay
    // accretes at reality's cadence instead: at most IntakeBatch-sized span batches per step, the same rate the
    // scaffold drained at. The rest of the block still fed READ/MODEL this step — the utterance was heard in full;
    // only its accretion onto the permanent tape is rate-bounded.
    private static void Mint(ReadOnlyMemory<byte> block, Tape tape, Journal journal, int step, int maxSpans = int.MaxValue, List<TapeEventID>? into = null)
    {
        int minted = 0;
        foreach (var line in Engine.SplitLines(block))
        {
            if (minted >= maxSpans) break;
            if (line.Length == 0) continue;
            var sid = TapePacketCreator.AppendGeneratedUtterance(tape, journal, step, "node0", line);   // the machine's own utterance looped back — hypothesis at ε-weight until a real span corroborates it
            into?.Add(sid);                                            // the rhythm's cohort register — a replay-decision is judged by whether its spans vest
            minted++;
        }
    }

    private const double SleepPhi = 1.0;   // defrag φ-bridge weight (IDF diagonal + PPMI co-count) — the proven Seriate default

    private const double RestSleepFrac = 1.0 / 8;   // the homeostat's sleep-stride REST point — one sleep per +12.5% of tape, aligned with the induce stride (tape/StrideK): at rest, the consolidationPhase rides the same geometric beat the induce amortization proved; the conditions tighten toward 1/32, Quiet drifts toward 1/4

    private const int AutoCkptSteps = 25;  // sleep-off fallback cadence — a drive with no consolidationPhase shift still checkpoints (the safe-to-kill law has no off switch)

    // the checkpoint cadence (CortexRunConfig.CheckpointEvery): explicit N ⇒ every N steps AND every FULL consolidationPhase;
    // 0 (auto) ⇒ ride the full-consolidationPhase cadence (the post-consolidation state is the natural snapshot) with the
    // step fallback as the staleness bound — `slept` here is the caller's ckptConsolidationPhase (full consolidationPhases only; a light
    // lock-storm consolidationPhase doesn't earn a 26MB snapshot each — a kill replays it deterministically), so the
    // periodic arm is what keeps the safe-to-kill law's staleness bounded through a storm.
    private static bool ShouldCheckpoint(CortexRunConfig cfg, int step, bool slept, bool homeoOn) =>
        cfg.CheckpointEvery > 0     ? slept || (step + 1) % cfg.CheckpointEvery == 0
      : cfg.IntervalConsolidationPhase > 0 || homeoOn ? slept || (step + 1) % AutoCkptSteps == 0
      :                             (step + 1) % AutoCkptSteps == 0;

    // ── the stride law's two constants ──
    private const int StrideK = 8;         // re-induce stride = tape/StrideK → the O(tape) induce amortizes to O(StrideK) per byte (LINEAR total); 8 ⇒ re-induce ≈ each +12.5% of tape (fresh enough grammar, geometric induce spacing)
    private const int MintStrideDiv = 2;   // post-drain mint cap = stride/MintStrideDiv → per-step growth < stride ⇒ GEOMETRIC tape growth (the amortization precondition); 2 = the minimal spacing (re-induce ≥ every 2 steps in the capped band)

    // ── the anti-unify pass bounds ──  the growth loop is ~(1 + maxCand + 1) word-Re-Pair inductions of its
    // corpus PER iteration; over the whole tape that was 70-107s/sleep at ~960 spans and O(tape) forever. Windowed
    // + seeded (the paradigm persists in MemoryHierarchy), the pass is a few seconds FLAT regardless of tape size.
    private const int AuWindowSpans = 192; // discovery corpus = the newest N spans by id (the recency window — fresh evidence extends the standing tower)
    private const int AuMaxIter = 2;       // growth iterations per sleep (the tower still climbs — one level per sleep is the cadence, not per pass)
    private const int AuMaxCand = 4;       // MDL-gated candidates per iteration (each gate = one full window re-induction — the pass's cost knob)

    private const int MintSpansPerStep = 4;   // span-rate bound on the autoregressive mint — the replay accretes at the scaffold's IntakeBatch cadence (see Mint); the byte cap alone still let ~300 line-spans land per step

    // ── SLEEP consolidation ──  Δ-FEED the persistent consolidationPhase-shift
    // indexes (SimHash LSH + containment grams — accreted across the machine's whole life, NEVER rebuilt per pass),
    // DEFRAG the tape (default: the O(Δ) WEAVE off the persistent index; --no-simhash keeps the proven exact
    // O(spans²) re-seriation as the kill-line control arm), RE-INDUCE the consolidated tape, GC-DEMOTE the
    // lowest-rent literals to tape-refs under the surface-bit budget (MemoryHierarchy.Gc — identity, multi-span,
    // containment, near-dupe), then the REVERSE-INDEX + NAVIGABILITY reads. Returns the consolidated grammar (the
    // drive carries it into the next GENERATE) + the eviction/promotion counters (the reserved LossReading columns)
    // + the re-induce's MERGE STREAM (the thought-channel food — see below). Every pass records to the journal —
    // the self-indexing memory·lore·dawn tape; the per-pass wall + Δ land on the `sleep` trace boundary (the
    // acceptance curve: per-sleep ms vs tape spans must not climb quadratically with the tape).
    /// One consolidationPhase's outcome — the consolidated grammar the drive carries into the next GENERATE, the GC
    /// counters, the re-induce's thought stream, the breach's activity (default = a dark organ), and the
    /// phase-3 evacuation readouts (self-compression merges + spans shed/dropped THIS consolidationPhase).
    private readonly record struct ConsolidationPhasePass(
        RePairResult Grammar, int Evicted, int Promoted, List<MergeEvent> Events, int Demoted, long GrammarBits,
        AnnealEvict.BreachConsolidationPhase Breach, int Merged = 0, int ShedN = 0, int DropN = 0);

    // ── THE CONSOLIDATION_PHASE LADDER (O(Δ) — the batch re-greed is DEAD) ──  the full consolidationPhase once ran a fresh batch Re-Pair over
    // the whole view (the "greed") to re-anchor greedy-in-arrival merge order — MEASURED O(view²) (~res^2.41, 81% of
    // the consolidationPhase, 38.9s @ 10689 residents), the wall the self-played mind hit before its replay era (cogito#5). It is
    // gone: the STANDING loom already holds the O(Δ) grammar (loom.Result — the same parse the day and light consolidationPhases
    // already trust), and no downstream organ needs batch-optimality (every consumer wants only a pure binary
    // Re-Pair grammar reconstructing the view; the greedy-in-arrival MDL gap stays bounded — verify-loom's ≤+20% arm
    // — and the GC prunes the redundant tail). The standing count plane applies reflected weights and dropped ids
    // in place, so a full consolidationPhase pays only the exact delta fold + vest + drops; a grok-LOCK sleep (the lock
    // storms that made 77/89 consolidationPhases WASTED) is a LIGHT consolidationPhase (no vest). Two laws keep light exact:
    //   · DROPS ride full consolidationPhases only — a drop removes counts that a checkpoint reload must also omit.
    //   · VEST rides full consolidationPhases only — corroboration re-prices segments in the same delta application.
    /// `fullConsolidationPhase` = pay the full delta + vest + drops; false = the light pass.
    /// `weaveBase` — the PURE loom-lineage grammar the standing weave model scores against (the live
    /// install revision snapshot). The composed `grammar` interleaves antiunify's side layer after the binary
    /// prefix, so consecutive composed images disagree at the old base length and every WeaveModel.Ensure
    /// degenerated to a full reset (Couplings.Learn + BuildScorer O(symbols) + coverCache loss per
    /// consolidationPhase). Null falls back to `grammar` (the probe path and the exact control arm).
    private static ConsolidationPhasePass Consolidate(
        Tape tape, RePairResult grammar, MemoryHierarchy memory, Journal journal, int step, CortexRunConfig cfg, long budgetBits, int breachQuota, ulong navSeed, Loom? loom = null, bool fullConsolidationPhase = true, Seriate.WeaveModel? weaveModel = null, PearlAuditCache? auditCache = null, RePairResult? weaveBase = null, List<TapeEventID>? foldedAdmissions = null)
    {
        long t0 = Trace.NowTicks;
        // the consolidationPhase's TOTAL-ACCOUNTING frame data — every sub-organ's wall bracketed, Σsegments vs total emitted
        // on the `sleep.sub` boundary below; a dark stretch (Σ ≪ total) is an unmapped organ and reads as a bug.
        long msIdx = 0, msWeave = 0, msShed = 0, msGreed = 0, msCompact = 0, msVest = 0, msDelta = 0, msBreach = 0, msGc = 0, msNav = 0, msBidx = 0;
        long tP = Trace.NowTicks;
        int maxSpanBefore = Engine.ComputeMaxSpan(grammar);
        long msRenorm = Trace.ElapsedMs(tP);

        // Δ-FEED — fingerprint + bucket + gram-post ONLY the spans appended since the last pass (: O(Δ); the
        // index is the machine's standing recall organ, not a per-sleep scaffold). BEFORE evacuation, so every id
        // is indexed while resident (the reload feed re-reads evacuated ids through the event byte log).
        tP = Trace.NowTicks;
        var (firstNewSlot, added) = memory.IndexNewEvents(tape);
        MemoryIndexFeedReceipt indexFeed = memory.LastIndexFeed;
        msIdx = Trace.ElapsedMs(tP);

        // DEFRAG — re-order the tape so affine (template-sharing) spans abut, recovering the relational scale a
        // residual/mixed intake order capped. Default (auto/on): the O(Δ) WEAVE — only the Δ new spans are
        // candidate-generated (bucket co-members off the persistent index), φ-scored over the touched subset, and
        // spliced after their most-affine earlier partner; the standing order is the accumulated product of every
        // previous sleep. --no-simhash: the proven exact all-pairs affinity + full greedy re-seriation (Seriate.
        // LineAffinity+Chain, the 133→304B label-free recovery — O(spans²), the kill-line control arm for A/B at
        // small scale). Same φ basis on both arms; pairs-scored is the kill-line readout (vs n(n−1)/2 exact).
        bool exact = cfg.Simhash == "off";
        long exactPairs = (long)tape.Count * (tape.Count - 1) / 2;
        int pairsScored; int placed = 0; double weave = 0; double navAff = 0;
        tP = Trace.NowTicks;
        if (exact)
        {
            var spans = tape.ResidentEventBytes.ToArray();
            var aff = Seriate.LineAffinity(grammar, spans, SleepPhi);
            pairsScored = (int)Math.Min(int.MaxValue, exactPairs);
            tape.Reorder(Seriate.Chain(aff, spans.Length));
            navAff = MemoryHierarchy.Navigability(aff, spans.Length, navSeed);   // the φ-kNN small-world read — priced only on the arm that already built the dense matrix
        }
        else
        {
            var order = Seriate.WeaveNew(weaveBase ?? grammar, tape, memory.Index, firstNewSlot, SleepPhi, weaveModel, out pairsScored, out placed, out weave);
            if (order is not null) tape.Reorder(order);
        }
        msWeave = Trace.ElapsedMs(tP);

        // ── TAPE-SHED + REPLAY-DROP (phase 3, loom arm) ──  the consolidationPhase's evacuation, decided before delta application so
        // the whole consolidationPhase reads ONE canonical view. SHED: an evidence span whose CURRENT parse is a single symbol
        // (loom.ParsedLenOf ≤ 1 — the grammar generates it whole) moves its raw bytes to the event byte log and stays in
        // the view: not one count, use, or criticality read moves — only the RAM. DROP: an unvested Replay older
        // than the turnover window leaves the view — a hypothesis reality never corroborated; its counts vanish in
        // the delta application below (absent from GetEventViews) and its ReplayCount slot frees mint headroom. Both sets are
        // id-ascending (deterministic); the recency guard keeps every recent-window organ (promotion, anti-unify,
        // weave partners) fully resident.
        int shedN = 0, dropN = 0, merged = 0;
        tP = Trace.NowTicks;
        if (loom is not null && cfg.Shed)
        {
            // the shed criterion reads the LIVE loom's arrival-order parse. The exact tape delta is applied below;
            // the mesh/solve consolidationPhases have their own multi-loom orchestration. `fullConsolidationPhase` gates DROP: a light
            // consolidationPhase withholds drops so no live count plane can diverge from a resume.
            // leaves the view (kill→resume exactness).
            (shedN, dropN) = ConsolidationPhase.Evacuate(tape, loom, dropUnvested: fullConsolidationPhase);
            if (shedN + dropN > 0)
                journal.Consolidation(step, $"evacuate · shed {shedN} (learned → event byte log) · dropped {dropN} (stale unvested dreams) · residents {tape.Count}");
        }
        msShed = Trace.ElapsedMs(tP);

        // RE-INDUCE the consolidated VIEW → the grammar GC + index read (and the next GENERATE's grammar). TRACED: the
        // sleep re-induce is cognition too, and in the composed (sleep-heavy) path — where GrokBell schools a large
        // multi-domain pool one batch at a time — the accreted-tape byte-stride re-induce rarely fires (the tape grows
        // slower than the stride, and every sleep resets lastInduceBytes), so THIS is the dominant induce. Left on plain
        // Induce it discarded its merge stream and the thought channel went dark (0 events vs 1408 in the fast-drain
        // flatpool shakedown — the measured BUG). InduceTraced is byte-identical to Induce, so the grammar is unchanged.
        //
        // LOOM ARM ORDER (phases 2+3, O(Δ)): apply the exact TapeDelta (append + reflect + shed + drop) → Pump → harvest
        // the STANDING grammar (loom.Result — no batch re-greed). VEST-audit + corroborate runs on that grammar and
        // the next delta reprices reflected segments in place. SELF-COMPRESS is retired from the loom
        // path: it merged expansion-identical duplicates the BATCH re-greed produced, but the standing loom's rank
        // plane already forbids duplicate DIGRAMS, and its greedy-in-arrival redundancy stays MDL-bounded (verify-loom
            // ≤+20%) and GC-pruned — not worth a second full-grammar pass over the standing state.
        RePairResult g2; List<MergeEvent> reinduceEvents; PearlAudit? audit = null;
        if (loom is not null && !fullConsolidationPhase)
        {
            // ── THE LIGHT CONSOLIDATION_PHASE ──  fold the day tail appended THIS step (Draw/mint/mix land after the day
            // fold — the day path would splice it next step-top; folding here is id-ascending either way, so
            // the standing state is identical and Result's grammar-view splice contract holds), then the
            // standing loom's parse IS the grammar (Result: O(live) emit, zero re-derivation; rules array
            // IDENTITY preserved when nothing minted — downstream stride caches keep hitting). The tail's
            // mints are the light consolidationPhase's thought stream (genuine Δ cognition, not a from-scratch replay).
            // No vest and no whole-grammar rebuild — those are the full-consolidationPhase organs.
            tP = Trace.NowTicks;
            var lightEvents = new List<MergeEvent>();
            FoldLoomDelta(tape, loom, lightEvents, foldedAdmissions);
            g2 = loom.Result(tape);
            reinduceEvents = lightEvents;
            msGreed = Trace.ElapsedMs(tP);
        }
        else if (loom is not null)
        {
            // ── THE FULL CONSOLIDATION_PHASE (O(Δ) — the batch re-greed is dead) ──  the standing loom IS the grammar, exactly as
            // the day and the light consolidationPhase already trust it (Cortex day-stride harvest · the light arm above). The old
            // O(view²) organ built a SECOND amnesiac loom from zero every full consolidationPhase (Engine.InduceTraced → LoomBatch:
            // splice-all + PUMP the whole merge cascade from empty) purely to (a) re-anchor greedy-in-arrival merge
            // order and (b) feed a pure grammar to the vest-reweigh path — measured 81% of the consolidationPhase, ~res^2.41,
            // walling the mind before its replay era. Neither downstream need requires batch-optimality: every consumer
            // (Vest co-walk · GC rent · breach shape · the returned grammar) needs only a pure binary Re-Pair grammar
            // that reconstructs the view, which loom.Result IS (Pump mints 2-ary only; reconstruction is hard-gated).
            // The greedy-in-arrival MDL gap stays bounded (verify-loom's ≤+20% arm, never globally rebuilt in that gate either)
            // and the GC prunes the redundant tail. Result emits the canonical view after the precise delta; no global
            // reparse is needed because reflection reprices existing segments and drops remove their counts in place.
            tP = Trace.NowTicks;
            var fullEvents = new List<MergeEvent>();
            FoldLoomDelta(tape, loom, fullEvents, foldedAdmissions);                // fold the exact tape delta (id-ascending, Result's grammar-view splice contract) — the Δ mints are this consolidationPhase's thought stream, not a from-scratch replay
            g2 = loom.Result(tape);
            reinduceEvents = fullEvents;
            msGreed = Trace.ElapsedMs(tP);
            tP = Trace.NowTicks;
            if (cfg.WScale > 1)
            {
                var a = auditCache?.Get(tape, in g2, cfg.WScale, cfg.CrossReflect) ?? Pearl.Audit(tape, in g2, cfg.WScale, cfg.CrossReflect);
                List<TapeEventID>? vestedIDs = auditCache is null ? null : new List<TapeEventID>();
                int vested = Pearl.Corroborate(a, tape, journal, step, vestedIDs);
                if (vested > 0) Trace.Cortex.Boundary("vest", $"step={step} vested={vested} · tape real={tape.RealCount} replay={tape.ReplayCount} vested={tape.ReflectedReplayCount}");
                // fold the exact reflected set into the cached audit — the re-audit below becomes a cache
                // hit (or an append-only delta) instead of the guaranteed full O(view) rebuild the
                // NonAppendRevision bump used to force
                if (vestedIDs is { Count: > 0 }) auditCache!.RepriceReflected(tape, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vestedIDs));
                audit = a;
            }
            msVest = Trace.ElapsedMs(tP);
            // Corroboration above mutates Tape provenance. Apply that reflected
            // subset as a second precise delta before the full-view harvest.
            tP = Trace.NowTicks;
            FoldLoomDelta(tape, loom, fullEvents, foldedAdmissions);
            g2 = loom.Result(tape);
            if (cfg.WScale > 1) audit = auditCache?.Get(tape, in g2, cfg.WScale, cfg.CrossReflect) ?? Pearl.Audit(tape, in g2, cfg.WScale, cfg.CrossReflect);
            msDelta = Trace.ElapsedMs(tP);
        }
        else
        {
            tP = Trace.NowTicks;
            (g2, reinduceEvents, audit) = InduceOutcomeCredited(tape, journal, step, cfg.WScale, cfg.CrossReflect, auditCache);
            msGreed = Trace.ElapsedMs(tP);
        }

        // BREACH-AND-LOWER — the homeostat's Stalled grant spends HERE, on the fresh
        // induce (pure Re-Pair output, the shape the anneal is defined over): BREACH mints count-2 candidates the
        // greedy floor structurally cannot reach, LOWER keeps only what pays post-compaction MDL (demote-don't-
        // delete: the retired basis rides the returned grammar until the next re-induce; Gc can TapeRef it under
        // budget — the composed Memory path). Lossless (guarded against the tape) and grammar-only: the tape is
        // untouched, so the ratchet persists through GENERATE→mint→re-induce, not through grammar state.
        var breach = default(AnnealEvict.BreachConsolidationPhase);
        if (breachQuota > 0)
        {
            long tb0 = Trace.NowTicks;
            breach = AnnealEvict.Breach(g2, tape, breachQuota, navSeed);   // tape-backed guard — the lossless oracle walks the view in place, no Concat materialization
            if (breach.Fired)
            {
                g2 = breach.Grammar;
                if (cfg.WScale > 1) audit = auditCache?.Get(tape, in g2, cfg.WScale, cfg.CrossReflect) ?? Pearl.Audit(tape, in g2, cfg.WScale, cfg.CrossReflect);   // re-price the weighted rent on the breached rule set (no re-Corroborate — reflection settled at the induce)
                journal.Consolidation(step, $"breach · quota {breachQuota} · {breach.Line}");
            }
            msBreach = Trace.ElapsedMs(tb0);
            Trace.Cortex.Boundary("breach", $"ms={msBreach} quota={breachQuota} {breach.Line}{(breach.Fired ? " · cvz masked until the post-LOWER re-induce (C2)" : " · no-op (nothing past the floor)")}");
        }
        tP = Trace.NowTicks;
        int maxSpanAfter = Engine.ComputeMaxSpan(g2);
        msRenorm += Trace.ElapsedMs(tP);

        // GC-DEMOTION — evict lowest-rent literals to tape refs under the surface-bit budget: whole-span
        // identity, multi-span chains, sub-line gram-CONTAINMENT (the below-16B accretion fix), and — with
        // Near-duplicate containment — the Hamming-family exemplar demotion (Mount 2). All lossless (byte-exact sieves); the
        // near-dupe query rides the PERSISTENT index, never a per-pass rebuild. Armed (wScale>1) the rent is
        // provenance-weighted (audit.WUses): replay-echo rules rank lowest and demote first.
        tP = Trace.NowTicks;
        var gc = memory.Gc(g2, tape, budgetBits, cfg.NearDupe, memory.Index, audit?.WUses, cfg.WScale);
        var consolidated = new RePairResult(gc.Rules, g2.Compressed, g2.TotalSavings, g2.AlphabetSize);
        msGc = Trace.ElapsedMs(tP);

        // REVERSE-INDEX + NAVIGABILITY — the self-indexing tape (concept→span postings + the SimHash hub, both
        // Index events) + the "5-clicks" small-world read over the SimHash bucket-hop graph (Mount 3; the weave arm
        // reports its own placement readout instead of the dense φ-kNN read the exact arm prices).
        tP = Trace.NowTicks;
        double navBucket = memory.NavigabilityOver(navSeed);   // the STANDING adjacency (AdjOf) — the eager BucketGraph rebuild was the consolidationPhase's O(slots×hub) organ
        msNav = Trace.ElapsedMs(tP);
        tP = Trace.NowTicks;
        var (concepts, postings, indexSummary) = memory.BuildIndex(g2, tape);
        journal.Index(step, indexSummary);
        journal.Index(step, "simhash-hub · " + memory.Index.HubSummary());   // MOUNT 3: bucket-key → member TapeEventIDs, the self-indexing tape
        msBidx = Trace.ElapsedMs(tP);

        string defragNote = exact ? $"nav aff {navAff:F2}" : $"weave placed {placed}/{added} μ{weave:F2}";
        string simNote = $"simhash={(exact ? "off" : "on")} Δ{added} pairs {pairsScored}/{exactPairs} · {memory.Index.HubSummary()} · {defragNote} · nav bucket {navBucket:F2}"
                       + (cfg.NearDupe ? $" · neardupe evict {gc.NearDupeEvicted} fam {gc.Families}" : "");
        journal.Consolidation(step, $"defrag maxSpan {maxSpanBefore}→{maxSpanAfter}B · gc evict {gc.Evicted}(ms{gc.MultiSpanEvicted}) promote {gc.Promoted} bits {gc.GrammarBits}/{(budgetBits == 0 ? "∞" : budgetBits.ToString())}{(gc.ResidualBits > 0 ? $" residual {gc.ResidualBits}" : "")} resolve {gc.Resolved}/{gc.Demoted} · {simNote}");
        // the trace boundary carries the WALL (ms) — the acceptance curve datum (spans, Δ, ms). Wall-clock stays
        // OUT of the journal: journal.log is a deterministic artifact, the trace is the run's telemetry plane.
        long msTotal = Trace.ElapsedMs(t0);
        long msSum = msIdx + msWeave + msShed + msGreed + msCompact + msVest + msDelta + msBreach + msGc + msNav + msBidx + msRenorm;
        Trace.Cortex.Boundary("sleep.sub", $"step={step} {(fullConsolidationPhase ? "FULL" : "light")} ms: idx={msIdx} weave={msWeave} shed={msShed} greed={msGreed} compact={msCompact} vest={msVest} delta={msDelta} breach={msBreach} gc={msGc} nav={msNav} bidx={msBidx} renorm={msRenorm} Σ={msSum}/{msTotal} · view={tape.ByteLength}B residents={tape.Count} Δ={added} rules={g2.Rules.Length} · idxops bytes={indexFeed.Bytes} shingles={indexFeed.Shingles} grams={indexFeed.GramWindows} lsm-merge={indexFeed.Simhash.LsmMerges} adj-self={indexFeed.Simhash.AdjSelfChecks} adj-backfill={indexFeed.Simhash.AdjBackfillVisits}/{indexFeed.Simhash.AdjBackfillAdds}");
        Trace.Cortex.Boundary("sleep", $"ms={msTotal} spans={tape.Count} Δ={added} pairs={pairsScored} · maxSpan {maxSpanBefore}→{maxSpanAfter}B · evict {gc.Evicted}(ms{gc.MultiSpanEvicted},+{gc.NearDupeEvicted}nd) promote {gc.Promoted} demoted {gc.Demoted} bits {gc.GrammarBits}{(budgetBits == 0 ? "" : "/" + budgetBits)}{(gc.ResidualBits > 0 ? $" residual {gc.ResidualBits}" : "")} · resolve {gc.Resolved}/{gc.Demoted}"
            + $"{(shedN + dropN > 0 ? $" · shed {shedN} drop {dropN}" : "")}{(merged > 0 ? $" · selfcompress {merged}" : "")} · {simNote}");
        return new ConsolidationPhasePass(consolidated, gc.Evicted + gc.NearDupeEvicted, gc.Promoted, reinduceEvents, gc.Demoted, gc.GrammarBits, breach, merged, shedN, dropN);
    }

    // ── the intake pool ──  corpus lines → the un-ingested span POOL (every 10th line held out, off-tape, as the
    // generalization probe). Spans accrete onto the tape one frontier-pick at a time (the developmental fork).
    private static (List<byte[]> Pool, byte[] Heldout) SplitPool(byte[] corpus)
    {
        var pool = new List<byte[]>();
        var held = new List<byte>();
        int line = 0;
        foreach (var mem in Engine.SplitLines(corpus))
        {
            if (line++ % 10 == 9) { held.AddRange(mem.Span); held.Add((byte)'\n'); continue; }   // held-out: off the tape
            pool.Add(mem.ToArray());
        }
        return (pool, held.ToArray());
    }

    // FileCorpus's family-labeled spans → one newline-joined byte block (the held-out probe / the pool-fallback probe).
    private static byte[] ConcatSpans(IReadOnlyList<(int Fam, byte[] Bytes)> spans)
    {
        var held = new List<byte>();
        foreach (var (_, b) in spans) { held.AddRange(b); held.Add((byte)'\n'); }
        return held.ToArray();
    }
    private static byte[] ConcatPool(IReadOnlyList<byte[]> pool)
    {
        var buf = new List<byte>();
        foreach (var b in pool) { buf.AddRange(b); buf.Add((byte)'\n'); }
        return buf.ToArray();
    }

    /// The COMPLETE rule set, one rule per line, no cap. DumpGrammar below is a 60-rule digest for a
    /// human skimming mid-run; it is not an instrument, and reading absence from it as evidence is a
    /// mistake this project has now made in earnest (a 62-line view of a 4236-rule grammar was briefly
    /// taken as proof that a rule did not exist). Questions of the form "did the grammar learn X?" are
    /// asked constantly and deserve an artifact that can actually answer them, so the full dump lands
    /// beside the digest at run end where it can be searched.
    internal static string DumpGrammarRules(RePairResult r)
    {
        if (r.Rules is null || r.Rules.Length == 0) return "(empty grammar)\n";
        var sb = new StringBuilder();
        sb.AppendLine($"grammar · {r.Compressed.Length} symbols + {r.Rules.Length} rules · Δmdl {r.TotalSavings}");
        for (int index = 0; index < r.Rules.Length; index++)
        {
            uint nonterminal = Symbol.FirstNonterminal + (uint)index;
            byte[] expansion = Reconstruct.Expand(r.Rules, [new Symbol(nonterminal)]);
            // Escaped, never truncated: a rule that matters may be long, and the search that finds it
            // is the whole point of the artifact.
            sb.Append('N').Append(nonterminal).Append('\t')
              .AppendLine(Encoding.UTF8.GetString(expansion).Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"));
        }
        return sb.ToString();
    }

    private static string DumpGrammar(RePairResult r)
    {
        if (r.Rules is null || r.Rules.Length == 0) return "(empty grammar)\n";
        var sb = new StringBuilder();
        sb.AppendLine($"grammar · {r.Compressed.Length} symbols + {r.Rules.Length} rules · Δmdl {r.TotalSavings}");
        int shown = 0;
        foreach (var i in Enumerable.Range(0, r.Rules.Length))
        {
            if (shown++ >= 60) { sb.AppendLine($"  …+{r.Rules.Length - 60} more"); break; }
            uint nt = Symbol.FirstNonterminal + (uint)i;
            var exp = Reconstruct.Expand(r.Rules, [new Symbol(nt)]);
            var text = Encoding.UTF8.GetString(exp).Replace("\n", "\\n");
            sb.AppendLine($"  N{nt,-6} {(text.Length > 80 ? text[..80] : text)}");
        }
        return sb.ToString();
    }

}
