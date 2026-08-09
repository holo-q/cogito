namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE CURRICULUM (the scheduler seam) ──  the developmental curriculum, extracted from the two proven organs
// as ONE contract: WHAT span to ingest next (residual frontier-pick, RLEI-root), WHEN the current focus is
// grokked (move on), and WHICH domain to cross to next (the coupling bridge-order). The drive drives the loop;
// the curriculum decides the intake. Two implementations behind the seam:
//   • FlatPool  — the isotropic arm (ONE pool, whole-pool residual frontier-pick, no domains, the MIX
//                 rail). Fully wired so the drive runs day-one.
//   • GrokBell  — CritLock's proven scheduler (per-domain concentration until the isolated criticality-CV
//                 LOCKS, then cross the strongest coupling-bridge into the adjacent domain — grok → move-on →
//                 grok). Extract from CritLock.DomainMeter (the CV-lock hysteresis) + BridgeOrder. Pass-2b.

/// One curriculum step's outcome — how many spans landed on the tape this step + whether the schedule ADVANCED
/// (a domain grokked → the bell rang → the frontier crossed a coupling-bridge into the next domain). `Domain` is
/// the current focus after the step (0 for FlatPool, which has no domains). The drive logs an advance (a schedule
/// transition) and may trigger consolidation on it.
public readonly record struct IntakeStep(int Ingested, bool Advanced, int Domain);

internal readonly record struct FlatPoolCheckpointDelta(bool[] Ingested, int Drained, int MixCursor)
    : ICurriculumCheckpointDelta
{
    public string Kind => "flat-pool";
    public void Write(CkptWriter writer) => FlatPool.WriteCheckpointDelta(writer, in this);
}

internal readonly record struct DomainMeterCheckpointDelta(
    int Spans, double Cv, int K, double BestSym, int BelowStreak, int StreakResets,
    int Crossings, int FirstCrossRound, int LockRound, int LockBytes, bool WasBelow);

internal readonly record struct GrokBellMaskEdit(int Domain, int Index);

internal readonly record struct GrokBellCheckpointDelta(
    bool[][] DomainIngested, GrokBellMaskEdit[] IngestedEdits, int Cursor, int Round, int Ingested, int MixCursor,
    int[] LastSpans, double[] CachedCv, int[] CachedK, DomainMeterCheckpointDelta[] Meters,
    int[] RecentDomains) : ICurriculumCheckpointDelta
{
    public string Kind => "grok-bell";
    public void Write(CkptWriter writer) => GrokBell.WriteCheckpointDelta(writer, in this);
}


/// The scheduler seam. `Seed` bootstraps the anchor spans so the residual can discriminate; `Draw` runs one step
/// of residual-driven accretion (frontier-pick + ingest + fold the grok-signal into the move-on bell) scoped to
/// the current focus (whole pool for FlatPool, the current domain for GrokBell); `Mix` is the MIX rail (re-ingest
/// a real corpus span after drain — extrinsic reality mounted permanently). The curriculum records its
/// own ingests to the Journal (the intake happens here); the drive records mint/excursion/consolidation.
public interface ICurriculum
{
    /// Bind the exact append-only authorities restored for this runtime before
    /// mutable curriculum state or checkpoint deltas are decoded. Most
    /// curricula derive their evidence from their own state and keep the
    /// default no-op; runtime-owned curricula use this seam to retain the
    /// loaded tape/journal after the bootstrap-only `Seed` phase is skipped.
    void BindRuntimeTape(Tape tape, Journal journal) { }

    /// Authenticate runtime-owned durable promotion evidence immediately after checkpoint load,
    /// before the restored grammar install revision can be consumed by the drive.
    void VerifyLoadedState(Tape tape, Journal journal) { }

    /// Optional lineage root predicate for a runtime-owned world. Corpus curricula
    /// leave the generic corpus predicate in force; native curricula bind their
    /// own admitted admissionPlan pair without changing the generic loop contract.
    Func<Tape, TapeEventID, bool>? LineageWorldRootPredicate => null;

    /// Bind the exact ordinary-world corpus event IDs that this runtime may cite as
    /// causal opportunities. Runtime-owned curricula consume these through their
    /// own deterministic cursor; corpus schedulers ignore the handoff.
    void BindWorldOpportunityEvents(IReadOnlyList<TapeEventID> eventIDs) { }

    /// Bind the Cortex-owned grammar analysis plane. Corpus schedulers with a
    /// compatible cover basis reuse it; schedulers that intentionally cap their
    /// frontier basis may keep their own capped view.
    void BindGrammarShape(GrammarShape shape) { }

    /// Complete pending theory-to-grammar promotions against this exact install revision.
    /// The generic loop calls this at every install revision boundary; non-EML curricula
    /// retain the no-op default while ReplayCalc owns the existing EML store.
    bool SettleInstallRevision(
        in InstallRevision installRevision,
        IReadOnlyList<TapeEventID> foldedAppends,
        Func<TapeEventID, bool> foldedPredicate,
        LoopLineageTurnstile? lineage,
        Tape tape,
        Journal journal,
        int step) => false;

    /// Bootstrap the anchor spans onto the tape (+ journal) so the frontier residual can discriminate before the
    /// grammar self-drives its own intake. Called once, before the loop.
    void Seed(Tape tape, Journal journal);

    /// Append stable domain samples used by generic runtime probes. A runtime curriculum owns the bytes that
    /// represent its world; the Cortex must never substitute a type name for that evidence.
    void AppendProbeSamples(List<byte[]> samples);

    /// Declare workspace keys this curriculum can post. The runtime resolves configured readout selectors against
    /// this schema before the curve header lands, so selector typos fail before a long run starts.
    void DefineWorkspace(CogitoWorkspace workspace) { }

    /// Publish the curriculum's current observations into the shared workspace. Curve/report readouts are selectors
    /// over this plane; the curriculum owns only the observations, not their presentation.
    void PostWorkspace(CogitoWorkspace workspace) { }

    /// One INTAKE step — frontier-pick `batch` spans the current (stride-stale) grammar compresses best, accrete
    /// them onto the tape (+ journal), and fold this step's grok-signal into the move-on bell. Returns the step's
    /// outcome (spans landed + whether the schedule advanced).
    IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch);

    /// Has the SCHOOL finished — the scheduled pass over the pool complete? For FlatPool this IS pool-empty; for
    /// GrokBell it means the schedule crossed every bridge (grok→move-on left real spans behind), so `Drained` and
    /// `Exhausted` DIVERGE. Drained is a CURRICULUM-ADVANCE signal only — it ends the schooled intake (Draw) and
    /// opens the mop-up era (Advance); it never gates the replay-fork (THE SELF-REGULATION LAW: the machine consumes
    /// its WORLD, not its schedule — the schedule-gated fork left 84% of the pool un-ingested and the unmatched
    /// replay mint diluted the real anchor off criticality, meanz −0.78→−0.66).
    bool Drained { get; }

    /// Is there genuinely nothing left to ingest — every real span consumed? THE REPLAY-FORK GATE (Cortex FORK): the
    /// autoregressive mint opens only here, so a WALL verdict in the replay era is a true plateau by construction
    /// (flat-while-un-exhausted was starvation, and the volume-gated fork makes it unreachable). FlatPool:
    /// `== Drained` (one pool). GrokBell: every domain's spans ingested — NOT merely schedule-done.
    bool Exhausted { get; }

    /// Spans accreted so far — the `ingested` sparkline column.
    int IngestedCount { get; }

    /// World admissionPlans landed by the most recent intake operation.  The Cortex
    /// binds these concrete tape events to the registered lineage rail immediately
    /// after the curriculum returns; the curriculum never invents ancestry.
    IReadOnlyList<TapeEventID> LastWorldEventIDs => Array.Empty<TapeEventID>();

    /// Number of arm-neutral world opportunities already consumed by this
    /// curriculum. Checkpoint loaders use it to bind the source cursor exactly.
    int WorldOpportunityCursor => 0;

    /// The scheduler's visible work horizon for generic Cortex readouts. Corpus curricula use their pool count;
    /// runtime curricula that do not own a byte pool report their episode/task count here.
    int WorkloadCount => 0;

    /// MIX re-ingests the intake-affirm gate SKIPPED.
    /// THE SELF-MAINTENANCE RECEIPT: on a re-admissionPlan of learned data this climbs while IngestedCount holds — the
    /// tape stops growing from repetition. Zero when the gate is disarmed
    /// (affirmCut &lt; 0) or never fires. Schedulers without a corpus MIX mouth (ReplayCalc's axiom re-anchor) report 0.
    int MixAffirmSkips => 0;

    /// The MOP-UP era's intake (Cortex FORK — the preventive guard, promoted from the reactive momentum-meadow
    /// WALL-nudge): the schedule is Drained but real spans remain (grok→move-on abandoned them), so the drive keeps
    /// draining reality instead of forking to replay. Ingest up to `batch` leftover spans by the SAME residual
    /// frontier discipline as the school (RLEI-root — the mop-up covers most of the pool's volume, so index-order
    /// would abandon residual-driven intake exactly where it matters most); returns whether it fed anything (false
    /// ⟹ nothing left ⟹ now Exhausted ⟹ the replay-fork opens next step). Default: schedulers whose Drained IS
    /// Exhausted (FlatPool, ReplayCalc) never enter the mop-up era — nothing to feed.
    bool Advance(RePairResult grammar, Tape tape, Journal journal, int step, int batch) => false;

    /// The REPLAY era's intrinsic side-channel (Cortex FORK, Exhausted): a curriculum with its OWN generative source
    /// (Campfire's sieve-verified EML pump) keeps dreaming after the world is consumed — beside the drive's
    /// autoregressive loopback, in the same era and under the same reflection law (both draw the one unvested-replay
    /// headroom). Returns spans landed. Default: none — flatpool/grokbell/replaycalc have no side-channel; the
    /// loopback is the replay era's only mint.
    int Replay(RePairResult grammar, Tape tape, Journal journal, int step, int batch) => 0;

    /// Distinct domains among this curriculum's recent real appends (schooled ingest + mop-up + MIX) — the
    /// `ingest_div` sparkline: are modalities still being eaten, or has the real diet collapsed to one domain?
    /// Schedulers without domains report 1.
    int IngestDiversity => 1;

    /// The MIX rail — re-append one real corpus span (deterministic round-robin over the pool), so extrinsic
    /// reality stays mounted permanently after drain. No-op for an empty pool.
    /// THE INTAKE-AFFIRM GATE rides here: the CURRENT grammar + θ_affirm cut decide whether the
    /// re-ingest is genuine residual (append) or a span the grammar already generates (skip — the self-maintaining
    /// no-op). The grammar is the gate's key (GrammarCover.ParsedSize); affirmCut &lt; 0 disarms it (byte-identical).
    void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut);

    /// ONE cadence-free MIX re-ingest — re-append the next real corpus span NOW, subject to the intake-affirm gate.
    /// The rhythm's post-drain Day phase (Rhythm.cs): a CHOSEN re-admissionPlan with reality, not the rail's step-cadence
    /// drip — the world-channel stays open at any replay-fraction. Default: the cadenced Mix (schedulers with their own
    /// reality semantics — ReplayCalc re-anchors its axiom cadence-free, ungated — keep them).
    void MixOne(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
        => Mix(cortex, grammar, tape, journal, step, affirmCut);

    /// The mean grammar-coverage of the spans the LAST Draw/Advance frontier-pick ingested (NaN when that call
    /// scored nothing — bootstrap anchors and bell move-ons don't count; reset per call, always fresh). The
    /// rhythm's frontier-residual food: 1 − this = how much novel structure the world's EDGE still holds — the
    /// pick takes max-coverage spans (edge-of-known), so a low read means even the most-attachable spans are
    /// mostly unexplained (the world still teaches) and a high read means the edge is grokked.
    double LastPickCoverage => double.NaN;

    /// A curriculum-owned intrinsic frontier residual. Executable discovery domains publish this when semantic
    /// store motion, rather than coverage of externally sourced bytes, is the frontier observation.
    double IntrinsicFrontierResidual => double.NaN;

    /// The MIX rail's LIVE cadence (steps between real re-ingests; 0 = the rail is OFF — a config MODE the
    /// homeostat never overrides, not a magnitude). Settable because it is a homeostat ACTUATOR (face 1):
    /// one-sided-open toward more reality (smaller = more re-ingest — the anti-dark-room clamp lives in the
    /// homeostat). The off-arm never writes it, so the ctor value IS today's behavior; NOT checkpointed here —
    /// the homeostat's actuation state owns it and the drive re-applies it every step (resume-exact for free).
    int MixEvery { get; set; }

    /// Σ pre-lock grok-bell streak breakages across domains (DomainMeter.StreakResets) — the C2 bell-vs-breach
    /// no-thrash read: the drive attributes increments landing inside a breach's cvz-mask window to breach heat
    /// (kill-line: must be 0). Zero for schedulers without bells.
    int StreakResets { get; }

    /// Register domain-owned policy schemas before checkpoint state loads. The curriculum owns which decisions
    /// exist; Cortex owns their grammar, authority, outcomes, and persistence.
    void RegisterPolicies(Cortex cortex) { }

    /// Advance domain clocks exactly once after a completed Cortex step. Domain policies use this clock instead of
    /// counting whichever intake/action calls happened to run inside the step.
    void OnStepCompleted(Cortex cortex, int step) { }

    /// CHECKPOINT — the schedule's MUTABLE state only (drain masks, cursors, bells). The structural half (pools,
    /// domain order, bridge graph) is rebuilt deterministically from corpus + config by the resume path's PHASE 0,
    /// so it is never stored; Load restores into that freshly-built structure.
    void SaveState(CkptWriter w);
    void LoadState(CkptReader r);
}

/// A curriculum-owned terminal transition runs after the drive has flushed its
/// live loop-link/object custody and immediately before the terminal checkpoint
/// is written. Runtime-owned worlds use this seam to capture their mounted
/// authorities and append a final seal; ordinary curricula keep the default
/// no-op.
internal interface ICurriculumTerminalTransition
{
    void CaptureTerminalTransition(Cortex cortex, Run run, Tape tape, Journal journal) { }
}

/// A curriculum's right to say "not yet" to the momentum wall.
///
/// The wall halts the drive when the BYTE grammar's savings-slope goes flat, which is the correct read
/// for an organism that is only a byte grammar. A multi-plane organism has several learners with
/// different time constants, and the wall watches exactly one of them: measured on the repo crawler, a
/// 120-step budget halted at step 32 with the policy plane still cold, having never produced a single
/// learned selection. A stopping rule tuned to the fastest learner silently caps every slower learner
/// mounted beside it — no budget can fix that, because the wall fires on a slope, not a step count.
///
/// So a curriculum that mounts a slower plane may veto the halt while that plane is still maturing.
/// The veto is deliberately narrow: it defers the WALL, nothing else. `--steps` remains the hard cap,
/// consolidationPhase still preempts, and a curriculum that never stops vetoing simply runs to its horizon —
/// which is a visible, bounded outcome rather than a hang.
internal interface ICurriculumMomentumHaltVeto
{
    /// True while a plane slower than the byte grammar is still immature, and the drive would be
    /// stopping on the wrong learner's clock.
    bool VetoesMomentumHalt { get; }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  FLATPOOL — the whole-pool residual frontier (day-one scheduler)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The flat-pool curriculum — the isotropic arm behind the seam: ONE residual frontier over the WHOLE pool (no
/// domains), a fixed seed anchor, and the round-robin MIX rail. This is the day-one scheduler so the drive runs the
/// instant it lands; GrokBell is the anisotropic (per-domain grok-belled) upgrade.
public sealed class FlatPool(List<byte[]> pool, int seedSpans, int mixEvery) : ICurriculum, ICurriculumCheckpointDeltaOwner
{
    private readonly bool[] _ingested = new bool[pool.Count];
    private readonly FrontierIndex _frontier = new(pool);   // pool postings, built once at setup (face 3c — Draw scores candidates, never the pool)
    private int _drained;
    private int _mixCursor;                              // round-robin cursor over the pool (deterministic — the Vow)
    private Engine.GrammarCover? _cover;                 // frontier cover cache, keyed on grammar identity …
    private GrammarRule[]? _coverRules;                  // … rebuilt only when the drive re-induces (a fresh Rules array) — else the byte-exact stale cover is reused (the O(Δ) fix: the cover was rebuilt EVERY step though g changes only per stride)
    private GrammarShape? _sharedShape;
    private readonly List<TapeEventID> _lastWorldEventIDs = new();

    public void BindGrammarShape(GrammarShape shape)
    {
        _sharedShape = shape ?? throw new ArgumentNullException(nameof(shape));
        _cover ??= new Engine.GrammarCover(shape);
        _coverRules = null;
    }

    public bool Drained => _drained >= pool.Count;
    public bool Exhausted => Drained;                    // one flat pool → drained IS exhausted; the drive's mop-up era is unreachable for FlatPool (Drained && !Exhausted is a contradiction) — the interface's Advance/IngestDiversity defaults apply
    public int IngestedCount => _drained;
    public IReadOnlyList<TapeEventID> LastWorldEventIDs => _lastWorldEventIDs;

    internal FlatPoolCheckpointDelta CaptureCheckpointDelta()
        => new(_ingested.ToArray(), _drained, _mixCursor);

    internal void ApplyCheckpointDelta(in FlatPoolCheckpointDelta delta)
    {
        if (delta.Ingested is null || delta.Ingested.Length != _ingested.Length)
            throw new InvalidDataException("flat-pool checkpoint skew");
        delta.Ingested.CopyTo(_ingested, 0); _drained = delta.Drained; _mixCursor = delta.MixCursor;
        _coverRules = null; _cover = null;
    }

    internal void CommitCheckpointDelta() { }

    internal static void WriteCheckpointDelta(CkptWriter writer, in FlatPoolCheckpointDelta delta)
    { writer.U8(1); writer.I32(delta.Ingested.Length); foreach (bool value in delta.Ingested) writer.Bool(value); writer.I32(delta.Drained); writer.I32(delta.MixCursor); }

    internal static FlatPoolCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    { if (reader.U8() != 1) throw new InvalidDataException("unknown flat-pool checkpoint delta version"); int n = reader.I32(); if (n < 0 || n > 1_000_000) throw new InvalidDataException("flat-pool checkpoint exceeds bound"); bool[] ingested = new bool[n]; for (int i = 0; i < n; i++) ingested[i] = reader.Bool(); return new(ingested, reader.I32(), reader.I32()); }
    public int WorkloadCount => pool.Count;
    public int MixAffirmSkips => _mixSkips;              // MIX re-ingests the affirm-gate refused
    private int _mixSkips;
    public int StreakResets => 0;                        // no bells — nothing to thrash
    public int MixEvery { get; set; } = mixEvery;        // the live MIX cadence (the homeostat's actuator; ctor value = today's)
    public double LastPickCoverage => _lastPickCov;      // mean pick-coverage of the last Draw (the rhythm's frontier-residual food)
    private double _lastPickCov = double.NaN;

    ICurriculumCheckpointDelta? ICurriculumCheckpointDeltaOwner.CaptureCheckpointDelta()
        => CaptureCheckpointDelta();

    void ICurriculumCheckpointDeltaOwner.ApplyCheckpointDelta(ICurriculumCheckpointDelta delta, in CheckpointReplayContext replayContext)
    {
        if (!string.Equals(delta.Kind, "flat-pool", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {delta.Kind} does not belong to FlatPool");
        FlatPoolCheckpointDelta typed = delta switch
        {
            FlatPoolCheckpointDelta value => value,
            OpaqueCurriculumCheckpointDelta value => ReadOpaque(value),
            _ => throw new InvalidDataException($"curriculum checkpoint delta {delta.Kind} does not belong to FlatPool"),
        };
        ApplyCheckpointDelta(in typed);

        static FlatPoolCheckpointDelta ReadOpaque(OpaqueCurriculumCheckpointDelta value)
        {
            using MemoryStream stream = new(value.Payload, writable: false);
            using CkptReader reader = new(stream);
            FlatPoolCheckpointDelta delta = FlatPool.ReadCheckpointDelta(reader);
            if (reader.RemainingBytes != 0) throw new InvalidDataException("flat-pool checkpoint delta has trailing bytes");
            return delta;
        }
    }

    void ICurriculumCheckpointDeltaOwner.CommitCheckpointDelta(ICurriculumCheckpointDelta captured)
    {
        if (captured is not FlatPoolCheckpointDelta || !string.Equals(captured.Kind, "flat-pool", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {captured.Kind} does not belong to FlatPool");
        CommitCheckpointDelta();
    }

    /// The drain mask (parallel to the pool) — the kill-line reads it to attribute FlatPool's whole-pool frontier
    /// picks to domains (poolDom) and measure per-domain grok-bell locks it does not track itself.
    internal bool[] IngestedMask => _ingested;

    /// MemStat census read — the whole-pool frontier postings.
    internal FrontierIndex Frontier => _frontier;

    public void Seed(Tape tape, Journal journal)
    {
        _lastWorldEventIDs.Clear();
        int seed0 = Math.Min(seedSpans, pool.Count);
        for (int i = 0; i < seed0; i++) Ingest(tape, journal, step: 0, i);
    }

    public void AppendProbeSamples(List<byte[]> samples) => samples.AddRange(pool);

    public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        _lastWorldEventIDs.Clear();
        int before = _drained;
        _lastPickCov = double.NaN;
        if (_sharedShape is null && !ReferenceEquals(grammar.Rules, _coverRules)) { _cover = new Engine.GrammarCover(grammar.Rules); _coverRules = grammar.Rules; }   // uncapped byte-exact cover, built once per stride (not per step)
        double covSum = 0; int covN = 0;
        foreach (int i in Radula.FrontierPick(_cover!, pool, _ingested, batch, _frontier))
        {
            double coverage = _cover!.Coverage(pool[i]);
            covSum += coverage;
            covN++;
            Ingest(tape, journal, step, i, coverage);
        }
        if (covN > 0) _lastPickCov = covSum / covN;
        return new IntakeStep(_drained - before, Advanced: false, Domain: 0);   // one flat pool — never advances
    }

    public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
    {
        if (MixEvery <= 0 || step % MixEvery != 0) return;
        MixOne(cortex, grammar, tape, journal, step, affirmCut);
    }

    /// One cadence-free re-ingest (the rail's body — ICurriculum.MixOne, the rhythm's post-drain Day phase), GATED by
    /// the intake-affirm veto: the span the round-robin cursor lands on is re-appended ONLY if the current grammar
    /// does not already generate it (CortexTapeAdmission). The cover cache is the SAME grammar-identity-keyed one Draw
    /// builds — an affirmed re-admissionPlan costs one ParsedSize read and no tape byte (the self-maintaining no-op).
    public void MixOne(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
    {
        _lastWorldEventIDs.Clear();
        if (pool.Count == 0) return;
        int pi = _mixCursor++ % pool.Count;
        byte[] span = pool[pi];
        if (_sharedShape is null && !ReferenceEquals(grammar.Rules, _coverRules)) { _cover = new Engine.GrammarCover(grammar.Rules); _coverRules = grammar.Rules; }
        CortexTapeAdmissionChoice admission = cortex.ChooseTapeAdmission(_cover, span, span.Length, Provenances.Real, affirmCut);
        if (admission.Action == CortexTapeAdmissionActions.Reject)
        {
            cortex.CompleteTapeAdmission(in admission, appended: false);
            _mixSkips++;
            return;
        }
        bool fresh = !_ingested[pi];
        double coverage = _cover!.Coverage(span);
        TapeEventID admissionPlanEventID = TapePacketCreator.CommitWorldEncounter(tape, journal, step, pool, pi, domain: 0, fresh, coverage);
        _lastWorldEventIDs.Add(admissionPlanEventID);
        if (fresh)
        {
            _ingested[pi] = true;
            _drained++;
        }
        cortex.CompleteTapeAdmission(in admission, appended: true);
    }

    // checkpoint — the drain mask + the two cursors (the pool itself is rebuilt from the corpus).
    public void SaveState(CkptWriter w)
    {
        w.I32(_ingested.Length);
        foreach (var b in _ingested) w.Bool(b);
        w.I32(_drained); w.I32(_mixCursor);
    }

    public void LoadState(CkptReader r)
    {
        int n = r.I32();
        if (n != _ingested.Length) throw new InvalidDataException($"FlatPool checkpoint skew: {n} pool spans checkpointed, {_ingested.Length} rebuilt");
        for (int i = 0; i < n; i++) _ingested[i] = r.Bool();
        _drained = r.I32(); _mixCursor = r.I32();
        _coverRules = null; _cover = null;                                // identity cache — rebuilds off the restored grammar
    }

    // accrete one pool span onto the tape + journal, marking it drained (the scaffold drain).
    private void Ingest(Tape tape, Journal journal, int step, int i, double coverage = double.NaN)
    {
        if (_ingested[i]) return;
        TapeEventID admissionPlanEventID = TapePacketCreator.CommitWorldEncounter(tape, journal, step, pool, i, domain: 0, fresh: true, coverage);
        _lastWorldEventIDs.Add(admissionPlanEventID);
        _ingested[i] = true;
        _drained++;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  SHARED DOMAIN-READ ORGANS — the grok-bell knob authority + the per-domain crystallization read (GrokBell + CritLock)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The grok-bell / stride-discipline knob DEFAULTS — the ONE authority. CortexRunConfig's fields, CritLock's kill-line
/// Args defaults, and GrokBell's ctor defaults all read these, so a knob never triplicates across the fusion (the
/// values were byte-identical in all three homes; this makes that structural). The WHY of each number lives at its
/// consumer — the k-aware lock line on DomainMeter, the O(n²) stride rationale on the drive header.
internal static class GrokDefaults
{
    public const double Cv = 0.15;            // the grok lock-line FLOOR — the scale-scatter a critical state keeps (the SOC value; DomainMeter adds the k-aware band)
    public const int LockRounds = 3;          // CV-lock hysteresis depth — consecutive below-line rounds that ring the bell (anti-chatter)
    public const int MinDomainSpans = 8;      // below this a domain's grammar has too few rules per scale → CvZ is NaN noise (RenormStats needs ≥2 depth-levels × ≥4 rules)
    public const int ReStrideBytes = 5000;    // re-induce the accreted tape only past this byte growth (the mandatory O(n²) fix — else reuse the ≤stride-stale grammar, lossless for the schedule)
    public const int DomStrideSpans = 6;      // per-domain isolated re-induce stride — a domain's isolated grammar changes only when its own span count crosses this
    public const int FrontierCapExps = 400;   // frontier cover-basis cap — score a large pool against the top-N longest expansions (the deep rules drive the residual), not all rules
    public const int CvWindowSpans = 512;     // per-domain isolated-CV RECENCY WINDOW — a stride miss re-induces over the domain's most recent N ingested spans, not its whole history (Concat copied + Re-Pair'd the entire domain per fire → O(domain²/stride) cumulative on the live GrokBell path). The CV becomes a windowed statistic of the domain's RECENT scale-scatter; 512 spans ≫ the ≥2-levels×≥4-rules floor RenormStats needs, so a critical domain reads the same as the all-history CV until histories far outgrow the window. ≤0 = whole history (the pre-window read).
}

/// The gather of one domain's ingested spans → the isolated-grammar induce input. A struct-typed source so the
/// O(Δ) read (DomainMeter.ReadCv) dispatches monomorphized — zero closure alloc; the concat itself allocs only on
/// a stride miss (the cold path the gate exists to make rare).
internal interface IDomainSpanSource { byte[] Concat(); }

/// A domain read over its OWN drain mask + sub-pool (GrokBell's native per-domain view + the drive focus read).
/// `windowSpans` bounds the gather to the most recent N ingested spans (GrokDefaults.CvWindowSpans — the fire is
/// O(window), not O(all-history)); ≤0 = unbounded.
internal readonly struct DomainSpans(List<byte[]> spans, bool[] ingested, int windowSpans = GrokDefaults.CvWindowSpans) : IDomainSpanSource
{
    public byte[] Concat()
    {
        int start = 0;
        if (windowSpans > 0)
        {
            start = spans.Count; int seen = 0;
            for (int i = spans.Count - 1; i >= 0 && seen < windowSpans; i--) if (ingested[i]) { seen++; start = i; }
        }
        var buf = new List<byte>();
        for (int i = start; i < spans.Count; i++) if (ingested[i]) { buf.AddRange(spans[i]); buf.Add((byte)'\n'); }
        return buf.ToArray();
    }
}

/// A domain read over a FLAT pool by per-span domain-label attribution (the whole-pool FlatPool arm + CritLock's
/// global drain have no per-domain structure of their own — poolDom labels carve the domain out). Same recency
/// window as DomainSpans.
internal readonly struct PoolDomainSpans(List<byte[]> pool, List<int> poolDom, bool[] ingested, int domain, int windowSpans = GrokDefaults.CvWindowSpans) : IDomainSpanSource
{
    public byte[] Concat()
    {
        int start = 0;
        if (windowSpans > 0)
        {
            start = pool.Count; int seen = 0;
            for (int i = pool.Count - 1; i >= 0 && seen < windowSpans; i--) if (ingested[i] && poolDom[i] == domain) { seen++; start = i; }
        }
        var buf = new List<byte>();
        for (int i = start; i < pool.Count; i++) if (ingested[i] && poolDom[i] == domain) { buf.AddRange(pool[i]); buf.Add((byte)'\n'); }
        return buf.ToArray();
    }
}

/// UNION DEPTH — the depth read across ALL domains on an accreted grammar (the expensive held-out sweep, kept off
/// the per-round path): maxSpan = the deepest rule's byte extent (correlation length); meanSym = the mean held-out
/// symbols/byte over a deterministic subsample (LOWER = deeper — a learned template collapses a line to ~1 symbol,
/// mere words leave it near its byte count). The cover basis is built ONCE for the sweep. The one depth read both
/// CritLock's kill-lines and GrokBell's commit to — the bridge-order hypothesis is that warm cross-domain starts
/// drive BOTH numbers deeper at the same budget.
internal static class DomainDepth
{
    public static (double MaxSpan, double MeanSym) Union(RePairResult g, IReadOnlyList<(int Fam, byte[] Bytes)> heldout)
    {
        double maxSpan = Engine.RenormStats(g).MaxSpan;
        if (heldout.Count == 0) return (maxSpan, 1.0);
        var cover = new Engine.GrammarCover(g.Rules);                  // basis built ONCE for the held-out sweep
        int step = Math.Max(1, heldout.Count / 120);                  // deterministic subsample — bound the final read
        double acc = 0; int cnt = 0;
        for (int i = 0; i < heldout.Count; i += step) { var hb = heldout[i].Bytes; if (hb.Length == 0) continue; acc += (double)cover.ParsedSize(hb) / hb.Length; cnt++; }
        return (maxSpan, cnt == 0 ? 1.0 : acc / cnt);
    }

    /// Union depth of a schedule-ordered tape truncated to `budget` bytes — the matched-budget read (only the intake
    /// ORDER differs between arms at equal bytes, so any depth gap is attributable to the SEQUENCE alone).
    public static (double MaxSpan, double MeanSym) UnionAt(byte[] tape, int budget, IReadOnlyList<(int Fam, byte[] Bytes)> heldout)
        => Union(Engine.Induce(budget >= tape.Length ? tape : tape[..budget]).Result, heldout);
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  GROKBELL — CritLock's proven scheduler, promoted into the drive (pass-2b)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The grok-bell curriculum — CritLock's proven self-scheduling school promoted into the drive: concentrate
/// residual-frontier intake on ONE domain until its ISOLATED criticality-CV LOCKS (< the k-aware lock line —
/// floor + sampling band, DomainMeter — for LockRounds consecutive rounds), then CROSS the strongest coupling-BRIDGE
/// (the DomainGraph greedy bridge-order) into the adjacent un-grokked domain. grok → move-on → grok, deterministic,
/// no LLM. This lifts CritLock.DrainScheduled's per-domain machinery — the sub-pools, the DomStride-gated isolated
/// re-induce, the DomainMeter bell, the greedy bridge-order — behind the ICurriculum seam, so the ONE drive drain
/// loop drives it (the drive owns the tape + the stride-gated accreted induce; GrokBell schedules WHICH span next
/// and reads the per-domain bell). Each `Draw` is one round of the meter; the current focus stays until it groks.
public sealed class GrokBell : ICurriculum, ICurriculumCheckpointDeltaOwner
{
    // ── the schedulable domains + the grok-bell's per-domain memory ──
    private List<byte[]>[] _byDom = null!;               // per-domain sub-pools (each file = a domain — FileCorpus provenance)
    private FrontierIndex[] _domFrontier = null!;        // per-domain pool postings, built once at Init (face 3c; Σ builds = O(pool bytes))
    private bool[][] _domIngested = null!;              // per-domain drain mask (parallel to _byDom[d])
    private int[] _domRemaining = null!;                 // un-ingested spans per domain — maintained by every mask mutation
    private int[] _order = null!;                        // the domain crossing sequence (bridge / shuffle / sequential)
    private DomainMeter[] _meters = null!;              // per-domain criticality-CV lock bells (the grok signal's memory)
    private int[] _lastSpans = null!;                    // per-domain span count at last isolated re-induce (the O(Δ) gate input)
    private double[] _cachedCv = null!;                  // per-domain last isolated CV (held between strides — the streak accrues on it)
    private int[] _cachedK = null!;                      // per-domain last isolated KZ (pairs with _cachedCv — the band needs the k that produced the cv)
    private List<byte[]> _mixPool = null!;               // the flattened pool for the MIX rail (deterministic round-robin re-ingest)
    private int[] _mixDom = null!;                       // domain of each _mixPool span — the MIX rail's ingest-diversity attribution
    private bool[] _domSeen = null!;                     // reused distinct-count scratch for IngestDiversity (zero-alloc per-step read)
    private int _domStride, _frontierCap;

    private int _cursor;                                 // index into _order — the current focus domain's position
    private int _mopCursor;                              // first domain not yet proven empty during mop-up, in _order
    private int _round;                                  // Draw count — the meter's round unit (each Draw = one round)
    private int _ingested;                               // total spans accreted (the IngestedCount sparkline column)
    private int _totalSpans;                             // Σ domain-span counts — the exhaustion denominator (Exhausted ⟺ _ingested == this)
    private int _mixCursor;                              // round-robin cursor over _mixPool (deterministic — the Vow)
    private readonly Queue<int> _recentDoms = new();     // domains of the last RecentIngestWin real appends (ingest + mop-up + MIX) — the ingest_div window
    private List<int>[] _checkpointIngested = null!;    // mask edits since the last keyframe/mutation receipt
    private readonly List<TapeEventID> _lastWorldEventIDs = new();
    private Engine.GrammarCover? _cover;                 // frontier cover cache, keyed on grammar identity …
    private GrammarRule[]? _coverRules;                  // … rebuilt only when the drive re-induces (a fresh Rules array)

    private const int RecentIngestWin = 32;              // the ingest_div window — recent real appends the diversity read spans

    /// The drive ctor — take the ALREADY-BUILT domain-structured FileCorpus (each file = a domain; Cortex PHASE-0
    /// built it ONCE and derives the pool/held-out probe from the same instance — no double corpus read), learn the
    /// DomainGraph over the union, and follow its greedy bridge-order. `order` selects the crossing policy (bridge =
    /// coupling-adjacency; shuffle / sequential = the kill-line nulls). `domStride` / `frontierCap` are the drive's
    /// stride-discipline knobs (CortexRunConfig owns the numbers); `mixEvery` is the MIX-rail cadence.
    public GrokBell(FileCorpus fc, double grokCv, int lockRounds, ulong seed, int mixEvery,
        int domStride = GrokDefaults.DomStrideSpans, int frontierCap = GrokDefaults.FrontierCapExps, string order = "bridge", double bandSigmas = 1.5)
    {
        int D = fc.Families;
        if (D == 0) throw new InvalidDataException("grokbell: the corpus yielded 0 domains — the glob matched no files (check the corpus dir + --glob)");   // fail-loud at the mouth: D=0 used to ride GreedyOrder's [0] into Seed → Bootstrap → _byDom[0] IndexOutOfRange
        var byDom = new List<byte[]>[D];
        for (int d = 0; d < D; d++) byDom[d] = new();
        foreach (var (fm, b) in fc.Lines) byDom[fm].Add(b);
        var graph = DomainGraph.Build(BlocksOf(byDom));
        int[] ord = order switch
        {
            "shuffle"    => ShuffledOrder(D, seed),
            "sequential" => Enumerable.Range(0, D).ToArray(),
            _            => graph.GreedyOrder(),
        };
        Init(byDom, ord, grokCv, lockRounds, domStride, frontierCap, mixEvery, bandSigmas);
    }

    /// The kill-line ctor — a caller-built domain pool + an explicit crossing order, so the proof harness drives the
    /// SAME promoted mechanism under bridge / shuffle / sequential orders on ONE shared pool (the order is the only
    /// free variable, exactly CritLock kill-line (b)'s setup).
    internal GrokBell(List<byte[]>[] byDom, int[] order, double grokCv, int lockRounds, int mixEvery, int domStride, int frontierCap, double bandSigmas = 1.5)
        => Init(byDom, order, grokCv, lockRounds, domStride, frontierCap, mixEvery, bandSigmas);

    public int MixEvery { get; set; }                    // the live MIX cadence (the homeostat's actuator; Init sets today's)

    public void AppendProbeSamples(List<byte[]> samples) => samples.AddRange(_mixPool);

    private void Init(List<byte[]>[] byDom, int[] order, double grokCv, int lockRounds, int domStride, int frontierCap, int mixEvery, double bandSigmas)
    {
        int D = byDom.Length;
        _byDom = byDom; _order = order; _domStride = domStride; _frontierCap = frontierCap; MixEvery = mixEvery;
        _domIngested = new bool[D][];
        _domRemaining = new int[D];
        _domFrontier = new FrontierIndex[D];
        _meters = new DomainMeter[D];
        _lastSpans = new int[D];
        _cachedCv = new double[D];
        _cachedK = new int[D];
        _mixPool = new();
        _domSeen = new bool[D];
        _checkpointIngested = new List<int>[D];
        for (int d = 0; d < D; d++)
        {
            _domIngested[d] = new bool[byDom[d].Count];
            _checkpointIngested[d] = new();
            _domRemaining[d] = byDom[d].Count;
            _domFrontier[d] = new FrontierIndex(byDom[d]);
            _meters[d] = new DomainMeter(grokCv, lockRounds, bandSigmas);
            _lastSpans[d] = -1;                          // never induced → force the first read
            _mixPool.AddRange(byDom[d]);
            _totalSpans += byDom[d].Count;
        }
        _mixDom = new int[_mixPool.Count];
        for (int d = 0, i = 0; d < D; d++) for (int j = 0; j < byDom[d].Count; j++) _mixDom[i++] = d;   // _mixPool is byDom-concatenated — the parallel domain map falls out
    }

    /// MemStat census read — the pool payload + the per-domain frontier indexes (the pool-proportional residents).
    internal (long PoolSpans, long PoolBytes, FrontierIndex[] Frontiers) Mass()
    {
        long spans = 0, bytes = 0;
        foreach (var dom in _byDom) { spans += dom.Count; foreach (var s in dom) bytes += s.Length; }
        return (spans, bytes, _domFrontier);
    }

    public bool Drained => _cursor >= _order.Length;     // the schedule walked every domain (grokked or exhausted) — nothing left to SCHOOL (but grok→move-on left real spans un-consumed: Drained ≠ Exhausted — the mop-up era opens here)
    public bool Exhausted => _ingested >= _totalSpans;   // every real span consumed — THE REPLAY-FORK GATE (reached via Advance's residual mop-up of what grok→move-on abandoned; schedule-done ≠ this)
    public int IngestedCount => _ingested;
    public int WorkloadCount => _totalSpans;
    public int MixAffirmSkips => _mixSkips;              // MIX re-ingests the affirm-gate refused
    private int _mixSkips;

    ICurriculumCheckpointDelta? ICurriculumCheckpointDeltaOwner.CaptureCheckpointDelta()
        => CaptureCheckpointDelta();

    void ICurriculumCheckpointDeltaOwner.ApplyCheckpointDelta(ICurriculumCheckpointDelta delta, in CheckpointReplayContext replayContext)
    {
        if (!string.Equals(delta.Kind, "grok-bell", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {delta.Kind} does not belong to GrokBell");
        GrokBellCheckpointDelta typed = delta switch
        {
            GrokBellCheckpointDelta value => value,
            OpaqueCurriculumCheckpointDelta value => ReadOpaque(value),
            _ => throw new InvalidDataException($"curriculum checkpoint delta {delta.Kind} does not belong to GrokBell"),
        };
        ApplyCheckpointDelta(in typed);

        static GrokBellCheckpointDelta ReadOpaque(OpaqueCurriculumCheckpointDelta value)
        {
            using MemoryStream stream = new(value.Payload, writable: false);
            using CkptReader reader = new(stream);
            GrokBellCheckpointDelta delta = GrokBell.ReadCheckpointDelta(reader);
            if (reader.RemainingBytes != 0) throw new InvalidDataException("grok-bell checkpoint delta has trailing bytes");
            return delta;
        }
    }

    void ICurriculumCheckpointDeltaOwner.CommitCheckpointDelta(ICurriculumCheckpointDelta captured)
    {
        if (captured is not GrokBellCheckpointDelta || !string.Equals(captured.Kind, "grok-bell", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {captured.Kind} does not belong to GrokBell");
        CommitCheckpointDelta();
    }
    public double LastPickCoverage => _lastPickCov;      // mean pick-coverage of the last Draw/Advance (the rhythm's frontier-residual food)
    private double _lastPickCov = double.NaN;
    public IReadOnlyList<TapeEventID> LastWorldEventIDs => _lastWorldEventIDs;

    /// Distinct domains among the last RecentIngestWin real appends (ingest + mop-up + MIX) — the ingest_div
    /// sparkline read, zero-alloc off the reused _domSeen scratch.
    public int IngestDiversity
    {
        get
        {
            Array.Clear(_domSeen);
            int n = 0;
            foreach (int d in _recentDoms) if (!_domSeen[d]) { _domSeen[d] = true; n++; }
            return n;
        }
    }

    /// Bootstrap the FIRST domain's anchor span so its residual can discriminate before the frontier self-drives.
    public void Seed(Tape tape, Journal journal)
    {
        _lastWorldEventIDs.Clear();
        if (_order.Length > 0) Bootstrap(_order[0], tape, journal, step: 0);
    }

    /// One INTAKE round of the school: read the CURRENT focus domain's isolated criticality-CV (the grok bell),
    /// and either MOVE ON (the bell locked, or the domain exhausted → cross the bridge-order into the next domain,
    /// bootstrapping it) or CONCENTRATE (frontier-pick the domain's un-ingested spans the accreted grammar
    /// compresses best). Returns whether the schedule ADVANCED + the focus domain after the step.
    public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        _lastWorldEventIDs.Clear();
        _lastPickCov = double.NaN;                       // fresh per call — a pickless round (bell read / move-on) must not replay a stale frontier read
        if (_cursor >= _order.Length) return new IntakeStep(0, Advanced: false, Domain: -1);   // drained (the drive guards on Drained; belt-and-suspenders)
        int d = _order[_cursor];

        // ── the grok-bell read ──  fold the focus domain's ISOLATED criticality-CV into its meter (DomainMeter.ReadCv,
        // the shared O(Δ)-gated crystallization read); the caller-owned stride cache (_lastSpans/_cachedCv/_cachedK) is
        // checkpointed below, so the streak accrues across a resume.
        int cnt = _byDom[d].Count - _domRemaining[d];
        bool locked = _meters[d].ReadCv(cnt, GrokDefaults.MinDomainSpans, _domStride, ref _lastSpans[d], ref _cachedCv[d], ref _cachedK[d], _round, (int)tape.GrammarByteLength, new DomainSpans(_byDom[d], _domIngested[d]));
        _round++;

        // ── the bell rang (or the domain drained) → MOVE ON ──  cross the bridge-order into the next un-grokked
        // domain and bootstrap its anchor, so the NEXT round concentrates there. The schedule ADVANCED.
        int rem = _domRemaining[d];
        if (locked || rem == 0)
        {
            _cursor++;
            if (_cursor >= _order.Length) return new IntakeStep(0, Advanced: true, Domain: -1);   // the LAST domain grokked — scaffold done
            int nd = _order[_cursor];
            int got = Bootstrap(nd, tape, journal, step);
            return new IntakeStep(got, Advanced: true, Domain: nd);
        }

        // ── concentrate ──  frontier-pick the focus domain's un-ingested spans the CURRENT (stride-stale) accreted
        // grammar compresses best — the residual self-concentration, scoped to ONE domain by the schedule. The cover
        // basis is rebuilt only when the drive re-induced (a fresh Rules array) — else the ≤stride-stale one is reused.
        if (!ReferenceEquals(grammar.Rules, _coverRules)) { _cover = new Engine.GrammarCover(grammar.Rules, _frontierCap); _coverRules = grammar.Rules; }
        int taken = 0; double covSum = 0;
        foreach (int i in Radula.FrontierPick(_cover!, _byDom[d], _domIngested[d], Math.Min(batch, rem), _domFrontier[d]))
        {
            double coverage = _cover!.Coverage(_byDom[d][i]);
            covSum += coverage;
            Take(d, i, tape, journal, step, coverage);
            taken++;
        }
        if (taken > 0) _lastPickCov = covSum / taken;
        return new IntakeStep(taken, Advanced: false, Domain: d);
    }

    /// The MIX rail — re-append one real corpus span (deterministic round-robin over the flattened multi-domain
    /// pool), so extrinsic reality stays mounted permanently after the schedule drains. NOT marked
    /// ingested — it is re-ingest, not scaffold drain.
    public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
    {
        _lastWorldEventIDs.Clear();
        if (MixEvery <= 0 || step % MixEvery != 0) return;
        MixOne(cortex, grammar, tape, journal, step, affirmCut);
    }

    /// One cadence-free re-ingest (the rail's body — ICurriculum.MixOne, the rhythm's post-drain Day phase), GATED by
    /// the intake-affirm veto (CortexTapeAdmission over the SAME grammar-identity-keyed cover cache Draw builds). An
    /// affirmed re-admissionPlan neither appends NOR counts toward the modality diet (a skipped span never landed) — the
    /// self-maintaining no-op; the round-robin cursor still advances (deterministic — the Vow).
    public void MixOne(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
    {
        _lastWorldEventIDs.Clear();
        if (_mixPool.Count == 0) return;
        int mi = _mixCursor++ % _mixPool.Count;
        byte[] span = _mixPool[mi];
        if (!ReferenceEquals(grammar.Rules, _coverRules)) { _cover = new Engine.GrammarCover(grammar.Rules, _frontierCap); _coverRules = grammar.Rules; }
        CortexTapeAdmissionChoice admission = cortex.ChooseTapeAdmission(_cover, span, span.Length, Provenances.Real, affirmCut);
        if (admission.Action == CortexTapeAdmissionActions.Reject)
        {
            cortex.CompleteTapeAdmission(in admission, appended: false);
            _mixSkips++;
            return;
        }
        int domain = _mixDom[mi];
        int localIndex = mi;
        for (int priorDomain = 0; priorDomain < domain; priorDomain++) localIndex -= _byDom[priorDomain].Count;
        bool fresh = !_domIngested[domain][localIndex];
        double coverage = _cover!.Coverage(span);
        TapeEventID admissionPlanEventID = TapePacketCreator.CommitWorldEncounter(tape, journal, step, _mixPool, mi, domain, fresh, coverage);
        _lastWorldEventIDs.Clear();
        _lastWorldEventIDs.Add(admissionPlanEventID);
        if (fresh)
        {
            MarkIngested(domain, localIndex);
        }
        cortex.CompleteTapeAdmission(in admission, appended: true);
        PushDom(domain);
    }

    /// The MOP-UP era's intake (ICurriculum.Advance — the drive's preventive replay-fork guard): the schedule already
    /// crossed every bridge (Drained), but grok→move-on abandoned real spans in the domains it locked early — on the
    /// vast world that was 84% of the pool, so the mop-up IS the volume majority and keeps the FULL residual
    /// discipline: schedule-order over the domains (the bridge sequence still carries the coupling adjacency), and
    /// within the first domain that still has spans, frontier-pick the ones the CURRENT grammar compresses best
    /// (RLEI-root — index-order here would hand most of the world to a batch-dump). The indexed pick's zero-coverage
    /// fill guarantees monotone progress (Exhausted is always reachable — no livelock on zero-scoring leftovers).
    /// Deterministic (the Vow). Returns whether it fed anything — false ⟺ every span consumed ⟺ now Exhausted,
    /// and the drive's replay-fork opens.
    public bool Advance(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        _lastWorldEventIDs.Clear();
        _lastPickCov = double.NaN;
        if (!ReferenceEquals(grammar.Rules, _coverRules)) { _cover = new Engine.GrammarCover(grammar.Rules, _frontierCap); _coverRules = grammar.Rules; }   // same stride-stale cover cache as Draw
        while (_mopCursor < _order.Length)
        {
            int d = _order[_mopCursor];
            int rem = _domRemaining[d];
            if (rem == 0) { _mopCursor++; continue; }
            int taken = 0; double covSum = 0;
            foreach (int i in Radula.FrontierPick(_cover!, _byDom[d], _domIngested[d], Math.Min(batch, rem), _domFrontier[d]))
            {
                double coverage = _cover!.Coverage(_byDom[d][i]);
                covSum += coverage;
                Take(d, i, tape, journal, step, coverage);
                taken++;
            }
            if (_domRemaining[d] == 0) _mopCursor++;
            if (taken > 0) _lastPickCov = covSum / taken;
            return taken > 0;
        }
        return false;
    }

    /// The per-domain grok bells — the kill-line reads the lock trail (bytes-to-first-lock per domain) off these.
    internal IReadOnlyList<DomainMeter> Meters => _meters;

    internal GrokBellCheckpointDelta CaptureCheckpointDelta()
    {
        List<GrokBellMaskEdit> edits = new();
        for (int d = 0; d < _checkpointIngested.Length; d++)
            foreach (int index in _checkpointIngested[d]) edits.Add(new(d, index));
        return new(Array.Empty<bool[]>(), edits.ToArray(), _cursor, _round, _ingested, _mixCursor,
            _lastSpans.ToArray(), _cachedCv.ToArray(), _cachedK.ToArray(), _meters.Select(static meter => meter.CaptureCheckpointDelta()).ToArray(), _recentDoms.ToArray());
    }

    internal void ApplyCheckpointDelta(in GrokBellCheckpointDelta delta)
    {
        if (delta.LastSpans.Length != _lastSpans.Length || delta.CachedCv.Length != _cachedCv.Length
            || delta.CachedK.Length != _cachedK.Length || delta.Meters.Length != _meters.Length)
            throw new InvalidDataException("grokbell checkpoint skew");
        if (delta.IngestedEdits is { Length: > 0 })
        {
            foreach (GrokBellMaskEdit edit in delta.IngestedEdits)
            {
                if ((uint)edit.Domain >= (uint)_domIngested.Length || (uint)edit.Index >= (uint)_domIngested[edit.Domain].Length)
                    throw new InvalidDataException($"grokbell checkpoint mask edit {edit.Domain}:{edit.Index} is out of range");
                if (!_domIngested[edit.Domain][edit.Index])
                {
                    _domIngested[edit.Domain][edit.Index] = true;
                    _domRemaining[edit.Domain]--;
                }
            }
        }
        else if (delta.DomainIngested is { Length: > 0 })
        {
            if (delta.DomainIngested.Length != _domIngested.Length)
                throw new InvalidDataException("grokbell checkpoint domain count skew");
            for (int d = 0; d < _domIngested.Length; d++)
            {
                if (delta.DomainIngested[d] is null || delta.DomainIngested[d].Length != _domIngested[d].Length)
                    throw new InvalidDataException($"grokbell checkpoint domain {d} skew");
                delta.DomainIngested[d].CopyTo(_domIngested[d], 0);
                _domRemaining[d] = _domIngested[d].Count(static ingested => !ingested);
            }
        }
        _cursor = delta.Cursor; _round = delta.Round; _ingested = delta.Ingested; _mixCursor = delta.MixCursor;
        delta.LastSpans.CopyTo(_lastSpans, 0); delta.CachedCv.CopyTo(_cachedCv, 0); delta.CachedK.CopyTo(_cachedK, 0);
        for (int d = 0; d < _meters.Length; d++) _meters[d].ApplyCheckpointDelta(delta.Meters[d]);
        _mopCursor = 0; while (_mopCursor < _order.Length && _domRemaining[_order[_mopCursor]] == 0) _mopCursor++;
        _recentDoms.Clear(); foreach (int domain in delta.RecentDomains) _recentDoms.Enqueue(domain);
        _coverRules = null; _cover = null;
    }

    internal void CommitCheckpointDelta()
    {
        foreach (List<int> edits in _checkpointIngested) edits.Clear();
    }

    internal void CommitCheckpointDelta(in GrokBellCheckpointDelta delta)
    {
        if (delta.IngestedEdits is null) throw new InvalidDataException("grokbell checkpoint delta is missing its captured edits");
        foreach (List<int> edits in _checkpointIngested) edits.Clear();
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in GrokBellCheckpointDelta delta)
    {
        writer.U8(2); writer.I32(delta.IngestedEdits.Length); foreach (GrokBellMaskEdit edit in delta.IngestedEdits) { writer.I32(edit.Domain); writer.I32(edit.Index); } writer.I32(delta.LastSpans.Length);
        writer.I32(delta.Cursor); writer.I32(delta.Round); writer.I32(delta.Ingested); writer.I32(delta.MixCursor); foreach (int value in delta.LastSpans) writer.I32(value); foreach (double value in delta.CachedCv) writer.F64(value); foreach (int value in delta.CachedK) writer.I32(value); writer.I32(delta.Meters.Length); foreach (DomainMeterCheckpointDelta meter in delta.Meters) DomainMeter.WriteCheckpointDelta(writer, in meter); writer.I32(delta.RecentDomains.Length); foreach (int value in delta.RecentDomains) writer.I32(value);
    }

    internal static GrokBellCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte version = reader.U8();
        bool[][] ingested = Array.Empty<bool[]>(); GrokBellMaskEdit[] edits;
        int domains;
        if (version == 1)
        {
            domains = reader.I32(); if (domains < 0 || domains > 1_000_000) throw new InvalidDataException("grokbell domains exceed bound");
            ingested = new bool[domains][];
            List<GrokBellMaskEdit> legacyEdits = new();
            for (int d = 0; d < domains; d++) { int n = reader.I32(); if (n < 0 || n > 1_000_000) throw new InvalidDataException("grokbell domain exceeds bound"); ingested[d] = new bool[n]; for (int i = 0; i < n; i++) if (reader.Bool()) { ingested[d][i] = true; legacyEdits.Add(new(d, i)); } }
            edits = legacyEdits.ToArray();
        }
        else if (version == 2)
        {
            int editCount = reader.I32(); if (editCount < 0 || editCount > 1_000_000) throw new InvalidDataException("grokbell mask edit count exceeds bound");
            edits = new GrokBellMaskEdit[editCount]; for (int i = 0; i < editCount; i++) edits[i] = new(reader.I32(), reader.I32());
            domains = reader.I32();
        }
        else throw new InvalidDataException("unknown grokbell checkpoint delta version");
        int cursor = reader.I32(), round = reader.I32(), count = reader.I32(), mix = reader.I32(); int[] last = ReadInts(reader, domains); double[] cv = ReadDoubles(reader, domains); int[] k = ReadInts(reader, domains); int meterCount = reader.I32(); if (meterCount != domains) throw new InvalidDataException("grokbell meter skew"); DomainMeterCheckpointDelta[] meters = new DomainMeterCheckpointDelta[domains]; for (int i = 0; i < domains; i++) meters[i] = DomainMeter.ReadCheckpointDelta(reader); int recentCount = reader.I32(); if (recentCount < 0 || recentCount > 1_000_000) throw new InvalidDataException("grokbell recent-domain window exceeds bound"); int[] recent = new int[recentCount]; for (int i = 0; i < recentCount; i++) recent[i] = reader.I32(); return new(ingested, edits, cursor, round, count, mix, last, cv, k, meters, recent);
    }

    private static int[] ReadInts(CkptReader reader, int count) { int[] values = new int[count]; for (int i = 0; i < count; i++) values[i] = reader.I32(); return values; }
    private static double[] ReadDoubles(CkptReader reader, int count) { double[] values = new double[count]; for (int i = 0; i < count; i++) values[i] = reader.F64(); return values; }

    /// Σ pre-lock streak breakages across the bells (the C2 no-thrash read — ICurriculum's contract).
    public int StreakResets
    {
        get { int n = 0; foreach (var m in _meters) n += m.StreakResets; return n; }
    }

    // checkpoint — per-domain drain masks + the schedule cursor/round + the stride caches + every bell's memory.
    // The structural half (_byDom/_order/_mixPool/_totalSpans and the bridge graph behind _order) is rebuilt
    // deterministically from corpus + config before Load runs.
    public void SaveState(CkptWriter w)
    {
        w.I32(_byDom.Length);
        for (int d = 0; d < _byDom.Length; d++)
        {
            w.I32(_domIngested[d].Length);
            foreach (var b in _domIngested[d]) w.Bool(b);
        }
        w.I32(_cursor); w.I32(_round); w.I32(_ingested); w.I32(_mixCursor);
        foreach (var s in _lastSpans) w.I32(s);
        foreach (var cv in _cachedCv) w.F64(cv);
        foreach (var k in _cachedK) w.I32(k);
        foreach (var m in _meters) m.Save(w);
        w.I32(_recentDoms.Count);                                          // the ingest_div window — a resumed curve must read the same diversity as straight-through
        foreach (var d in _recentDoms) w.I32(d);
    }

    public void LoadState(CkptReader r)
    {
        int D = r.I32();
        if (D != _byDom.Length) throw new InvalidDataException($"GrokBell checkpoint skew: {D} domains checkpointed, {_byDom.Length} rebuilt");
        for (int d = 0; d < D; d++)
        {
            int n = r.I32();
            if (n != _domIngested[d].Length) throw new InvalidDataException($"GrokBell checkpoint skew: domain {d} has {n} spans checkpointed, {_domIngested[d].Length} rebuilt");
            _domRemaining[d] = n;
            for (int i = 0; i < n; i++)
            {
                _domIngested[d][i] = r.Bool();
                if (_domIngested[d][i]) _domRemaining[d]--;
            }
        }
        _cursor = r.I32(); _round = r.I32(); _ingested = r.I32(); _mixCursor = r.I32();
        for (int d = 0; d < D; d++) _lastSpans[d] = r.I32();
        for (int d = 0; d < D; d++) _cachedCv[d] = r.F64();
        for (int d = 0; d < D; d++) _cachedK[d] = r.I32();
        foreach (var m in _meters) m.Load(r);
        _mopCursor = 0;
        while (_mopCursor < _order.Length && _domRemaining[_order[_mopCursor]] == 0) _mopCursor++;
        _recentDoms.Clear();
        int nDoms = r.I32();
        for (int i = 0; i < nDoms; i++) _recentDoms.Enqueue(r.I32());
        CommitCheckpointDelta();
        _coverRules = null; _cover = null;                                // identity cache — rebuilds off the restored grammar
    }

    // ── the intake primitives (the schedule's hands on the tape) ──
    private void Take(int d, int i, Tape tape, Journal journal, int step, double coverage = double.NaN)   // accrete one domain span onto the drive's tape + journal
    {
        if (_domIngested[d][i]) return;
        TapeEventID admissionPlanEventID = TapePacketCreator.CommitWorldEncounter(tape, journal, step, _byDom[d], i, d, fresh: true, coverage);
        _lastWorldEventIDs.Add(admissionPlanEventID);
        MarkIngested(d, i);
        PushDom(d);
    }
    private void MarkIngested(int d, int i)
    {
        if (_domIngested[d][i]) return;
        _domIngested[d][i] = true;
        _domRemaining[d]--;
        _ingested++;
        _checkpointIngested[d].Add(i);
    }
    private void PushDom(int d)                           // fold one real append's domain into the ingest_div window
    {
        _recentDoms.Enqueue(d);
        while (_recentDoms.Count > RecentIngestWin) _recentDoms.Dequeue();
    }
    private int Bootstrap(int d, Tape tape, Journal journal, int step)      // take the domain's first un-ingested span (the anchor)
    {
        for (int i = 0; i < _byDom[d].Count; i++) if (!_domIngested[d][i]) { Take(d, i, tape, journal, step); return 1; }
        return 0;
    }
    /// The DomainGraph's per-domain block cap. DomainGraph.Build expects CALLER-CAPPED blocks (its contract line);
    /// CritLock honors it at this same 40KB (CritLock.cs PerDomCap) — GrokBell didn't, which was invisible on
    /// thimble corpora (domains ≤ ~20KB) and pathological on the vast world pool (data/code: MB-scale domains →
    /// PHASE-0 would induce the ENTIRE multi-MB union). The graph is a domain-LEVEL coupling read; a 40KB prefix
    /// carries the domain's idiom vocabulary. The SCHEDULE pools stay uncapped — only the graph read is bounded.
    private const int GraphBlockCap = 40_000;

    // per-domain byte BLOCKS for the DomainGraph (a domain's spans in pool order, newline-joined, GraphBlockCap-
    // capped — the bridge-graph reads the domain's vocabulary, not the ingested subset). Anonymous `d{i}` labels
    // (the render names them; the schedule is by index).
    private static (string Name, byte[] Block)[] BlocksOf(List<byte[]>[] byDom)
    {
        var blocks = new (string, byte[])[byDom.Length];
        for (int d = 0; d < byDom.Length; d++)
        {
            var buf = new List<byte>(); int taken = 0;
            foreach (var b in byDom[d]) { if (taken + b.Length > GraphBlockCap) break; buf.AddRange(b); buf.Add((byte)'\n'); taken += b.Length + 1; }
            blocks[d] = ($"d{d}", buf.ToArray());
        }
        return blocks;
    }

    /// A seeded shuffle of the domain order, rotated so it still STARTS at domain 0 (isolate the ORDER, not the start
    /// point) — the bridge-order's null arm. The ONE home for the domain-order null: CritLock's kill-line (b) reads it
    /// here too. The `^ 0x5117A` salt keeps this permutation stream independent of the pool/held-out shuffles.
    internal static int[] ShuffledOrder(int D, ulong seed)
    {
        var order = Enumerable.Range(0, D).ToArray();
        Engine.Shuffle(order, seed ^ 0x5117A);
        int z = Array.IndexOf(order, 0);
        if (z > 0) { var head = order[z]; for (int i = z; i > 0; i--) order[i] = order[i - 1]; order[0] = head; }
        return order;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    //  THE KILL-LINE — does the grok-bell + bridge-order SCHEDULE the frontier-intake win across domains?
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    //  The intake proof (Radula.cs) established: on near-disjoint families, residual-frontier intake CONCENTRATES
    //  the budget and groks the deep scale a globally-mixed feed starves. This kill-line asks whether the promoted
    //  SCHEDULE turns that into a curriculum: at MATCHED steps on ONE multi-domain pool, does GrokBell (bridge-order)
    //  lock each domain's grok-bell in FEWER bytes and reach a DEEPER union than the whole-pool frontier (FlatPool)
    //  and the shuffled / sequential domain orders? Every arm is a real ICurriculum driven through the same
    //  induce→read→draw loop the drive runs; the ONLY variable is the schedule. Deterministic (the Vow).

    /// usage: grokbell <file|dir> [<file|dir>...] [--steps N] [--batch M] [--cv F] [--band S] [--lock L] [--glob G]
    ///                 [--mix N] [--domstride N] [--frontiercap N] [--seedspans N] [--seed HEX]
    ///        each FILE is a domain; a DIR contributes every glob-matched file (recursively) as its own domain.
    ///        --cv is the lock-line FLOOR (the SOC scale-scatter); --band the sampling-sd headroom (0 = the flat
    ///        legacy bell: thr ≡ floor — the k-aware kill-line's control arm).
    public static int KillLine(string[] args)
    {
        int steps      = Args.Int(args, "--steps", 120);
        int batch      = Args.Int(args, "--batch", 4);
        double cv      = Args.Double(args, "--cv", 0.15);
        double band    = Args.Double(args, "--band", 1.5);
        int lockRounds = Args.Int(args, "--lock", 3);
        string glob    = Args.Str(args, "--glob", "*.cs,*.py,*.md,*.txt");
        int mixEvery   = Args.Int(args, "--mix", 0);          // the kill-line measures INTAKE — the MIX rail is off (drain-only)
        int domStride  = Args.Int(args, "--domstride", 6);
        int frontierCap= Args.Int(args, "--frontiercap", 400);
        int seedSpans  = Args.Int(args, "--seedspans", 3);
        int stride     = Args.Int(args, "--stride", 5000);
        int perDom     = Args.Int(args, "--perdomain", 120);  // cap trainable spans/domain — bounds the O(pool) whole-pool-frontier arm + the isolated CV induces (the proof stays fast + replayable)
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);

        // ── the multi-domain pool ──  each file is a domain (blocked, non-blank TrimEnd lines; every 8th held out).
        var files = GatherDomainFiles(args, glob);
        if (files.Count < 2)
        {
            Console.Error.WriteLine("  usage: grokbell <file|dir> [<file|dir>...] [--steps N] [--batch M] [--cv F] [--band S] [--lock L] [--glob G] [--mix N]");
            Console.Error.WriteLine("  need ≥2 domains — each file (or glob-matched file under a dir) is one domain; the union induces the bridge-graph.");
            return 1;
        }
        int D = files.Count;
        var byDom = new List<byte[]>[D];
        var heldout = new List<(int Fam, byte[] Bytes)>();
        var flatPool = new List<byte[]>(); var poolDom = new List<int>();
        for (int d = 0; d < D; d++)
        {
            byDom[d] = new();
            int ln = 0;
            foreach (var raw in File.ReadLines(files[d]))
            {
                var text = raw.TrimEnd();
                if (text.Trim().Length == 0) continue;
                var bytes = Encoding.UTF8.GetBytes(text);
                if (ln++ % 8 == 7) heldout.Add((d, bytes));
                else if (byDom[d].Count < perDom) { byDom[d].Add(bytes); flatPool.Add(bytes); poolDom.Add(d); }   // flat pool = blocked union (domain 0 first, capped at perDom) — the FlatPool arm's pool

            }
        }

        var graph = DomainGraph.Build(BlocksOf(byDom));
        var bridgeOrder     = graph.GreedyOrder();
        var shuffledOrder   = ShuffledOrder(D, seed);
        var sequentialOrder = Enumerable.Range(0, D).ToArray();

        var run = Cogito.Run.New("grokbell");
        Trace.Note($"grokbell kill-line · {D} domains · {flatPool.Count} spans + {heldout.Count} held-out · {steps} steps · batch {batch} · cv floor {cv:P0} + {band:F1}σ k-band /{lockRounds}r{(band == 0 ? " (FLAT bell)" : "")} · no LLM");
        Trace.Note(graph.RenderBridgeMatrix(bridgeOrder).Replace("\n", "\n  "));

        // ── the arms ──  three schedules over ONE shared per-domain pool + the whole-pool frontier baseline.
        var arms = new List<ArmResult>
        {
            DriveGrok("bridge",     new GrokBell(byDom, bridgeOrder,     cv, lockRounds, mixEvery, domStride, frontierCap, band), steps, batch, stride),
            DriveGrok("shuffle",    new GrokBell(byDom, shuffledOrder,   cv, lockRounds, mixEvery, domStride, frontierCap, band), steps, batch, stride),
            DriveGrok("sequential", new GrokBell(byDom, sequentialOrder, cv, lockRounds, mixEvery, domStride, frontierCap, band), steps, batch, stride),
            DriveFlat("flat", flatPool, poolDom, D, cv, band, lockRounds, seedSpans, mixEvery, steps, batch, stride, domStride),
        };

        Report(run, arms, graph, bridgeOrder, shuffledOrder, D, heldout, cv, lockRounds, steps);
        return 0;
    }

    private sealed record ArmResult(string Label, DomainMeter[] Meters, int Bytes, int Steps, int Ingested, byte[] Tape);

    // ── drive a scheduled (GrokBell) arm ──  the drive's induce→draw loop; the bell reads happen INSIDE Draw, so the
    // arm's meters ARE the per-domain lock trail. Stride-gated re-induce (the O(n²) fix) exactly as the drive.
    private static ArmResult DriveGrok(string label, GrokBell cur, int steps, int batch, int stride)
    {
        var tape = new Tape(); var journal = new Journal();
        cur.Seed(tape, journal);
        var (_, _, g) = Engine.Induce(tape);
        long lastBytes = tape.GrammarByteLength;
        int step = 0;
        for (; step < steps && !cur.Drained; step++)
        {
            if (tape.GrammarByteLength - lastBytes >= stride) { (_, _, g) = Engine.Induce(tape); lastBytes = tape.GrammarByteLength; }
            cur.Draw(g, tape, journal, step, batch);
        }
        return new ArmResult(label, cur.Meters.ToArray(), (int)tape.GrammarByteLength, step, cur.IngestedCount, tape.Concat());
    }

    // ── drive the whole-pool frontier (FlatPool) arm ──  FlatPool has no domain concept, so the per-domain bell is
    // read EXTERNALLY here (poolDom attribution over its ingested mask) — the global-frontier lock trail: whole-pool
    // frontier DOES concentrate + lock domains, but in coverage-greedy order and it over-trains (no move-on).
    private static ArmResult DriveFlat(string label, List<byte[]> pool, List<int> poolDom, int D, double cv, double band, int lockRounds, int seedSpans, int mixEvery, int steps, int batch, int stride, int domStride)
    {
        var flat = new FlatPool(pool, seedSpans, mixEvery);
        var tape = new Tape(); var journal = new Journal();
        flat.Seed(tape, journal);
        var meters = new DomainMeter[D]; for (int d = 0; d < D; d++) meters[d] = new DomainMeter(cv, lockRounds, band);
        var lastSpans = new int[D]; for (int d = 0; d < D; d++) lastSpans[d] = -1;
        var cachedCv = new double[D]; var cachedK = new int[D];
        var (_, _, g) = Engine.Induce(tape);
        long lastBytes = tape.GrammarByteLength;
        int round = 0, step = 0;
        for (; step < steps && !flat.Drained; step++)
        {
            if (tape.GrammarByteLength - lastBytes >= stride) { (_, _, g) = Engine.Induce(tape); lastBytes = tape.GrammarByteLength; }
            ReadAllDomains(meters, pool, poolDom, flat.IngestedMask, D, round++, (int)tape.GrammarByteLength, lastSpans, cachedCv, cachedK, domStride);
            flat.Draw(g, tape, journal, step, batch);
        }
        return new ArmResult(label, meters, (int)tape.GrammarByteLength, step, flat.IngestedCount, tape.Concat());
    }

    // read EVERY domain's isolated (CV, k) over the flat arm's ingested spans (poolDom attribution), O(Δ)-gated per
    // domain — the global-frontier per-domain lock read (GrokBell reads only its focus domain; the flat arm has none).
    private static void ReadAllDomains(DomainMeter[] meters, List<byte[]> pool, List<int> poolDom, bool[] ingested, int D, int round, int totalBytes, int[] lastSpans, double[] cachedCv, int[] cachedK, int domStride)
    {
        for (int d = 0; d < D; d++)
        {
            int cnt = 0; for (int i = 0; i < pool.Count; i++) if (ingested[i] && poolDom[i] == d) cnt++;
            meters[d].ReadCv(cnt, GrokDefaults.MinDomainSpans, domStride, ref lastSpans[d], ref cachedCv[d], ref cachedK[d], round, totalBytes, new PoolDomainSpans(pool, poolDom, ingested, d));
        }
    }

    // ── the report ──  the payload (world boundary → stdout). Bytes-to-first-lock per domain per arm, locks
    // achieved, union depth at own + matched budget, and the verdict (does the schedule move the critical point?).
    private static void Report(Run run, List<ArmResult> arms, DomainGraph graph, int[] bridgeOrder, int[] shuffledOrder, int D, IReadOnlyList<(int Fam, byte[] Bytes)> heldout, double cv, int lockRounds, int steps)
    {
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine("════════════════════════════════════════════════════════════════════════════════");
        o.AppendLine($"  THE CURRICULUM KILL-LINE — does the grok-bell + bridge-order SCHEDULE the frontier win? ({D} domains, {steps} steps matched)");
        o.AppendLine($"    bridge-order  {string.Join(" → ", bridgeOrder)}      shuffle-order {string.Join(" → ", shuffledOrder)}");
        o.AppendLine();

        // per-arm summary
        o.AppendLine("  arm          steps  bytes   ingested  locks");
        foreach (var a in arms)
            o.AppendLine($"    {a.Label,-10} {a.Steps,4}  {a.Bytes,6}  {a.Ingested,7}   {LockedCount(a.Meters)}/{D}");
        o.AppendLine();

        // bytes-to-first-lock per domain (the (a) read — does frontier+schedule crystallize each domain sooner?)
        // k = the bridge arm's final per-scale slope count (the CV's sample size — the lock line is floor + k-band,
        // so the read is "lock-bytes stay bounded as k varies", shallow small-k domains included).
        o.AppendLine("  bytes-to-first-lock per domain (── = never locked in budget):");
        o.Append("    domain  "); foreach (var a in arms) o.Append($"{a.Label,12}"); o.AppendLine("   k");
        for (int d = 0; d < D; d++)
        {
            o.Append($"    d{d,-6}");
            foreach (var a in arms) o.Append($"{(a.Meters[d].Locked ? $"{a.Meters[d].LockBytes}B@r{a.Meters[d].LockRound}" : "──"),12}");
            o.AppendLine($"   {arms[0].Meters[d].K,2}");
        }
        o.AppendLine();

        // bytes-to-grok-the-WHOLE-set headline (the last domain to crystallize)
        o.AppendLine("  bytes to grok the WHOLE union (the last domain crystallizes; incomplete = a domain never locked):");
        foreach (var a in arms)
        {
            int all = BytesToGrokAll(a.Meters);
            o.AppendLine($"    {a.Label,-10} {(all < 0 ? $"incomplete ({LockedCount(a.Meters)}/{D} locked)" : all + "B")}");
        }
        o.AppendLine();

        // union depth at own budget + matched budget B* (the (b) read — does the SEQUENCE deepen at equal bytes?)
        int bStar = int.MaxValue; foreach (var a in arms) bStar = Math.Min(bStar, a.Tape.Length);
        o.AppendLine("  union depth (maxSpan · held-out sym/byte, ↓deeper):");
        o.AppendLine($"    arm          own-budget                       @ matched B*={bStar}B");
        foreach (var a in arms)
        {
            var own = DomainDepth.Union(Engine.Induce(a.Tape).Result, heldout);
            var mat = DomainDepth.UnionAt(a.Tape, bStar, heldout);
            o.AppendLine($"    {a.Label,-10} maxSpan {own.MaxSpan,4:F0}B sym/byte {own.MeanSym:F4} @ {a.Bytes,6}B   ·   maxSpan {mat.MaxSpan,4:F0}B sym/byte {mat.MeanSym:F4}");
        }
        o.AppendLine();

        // the verdict — the bell should schedule the intake proof's win: grok the set in ≤ flat's bytes with ≥ flat's
        // locks, and the coupling-adjacency SEQUENCE should reach a deeper union at matched budget than the shuffle.
        var bridge = arms.First(a => a.Label == "bridge");
        var flat   = arms.First(a => a.Label == "flat");
        var shuffle= arms.First(a => a.Label == "shuffle");
        int bridgeAll = BytesToGrokAll(bridge.Meters), flatAll = BytesToGrokAll(flat.Meters);
        bool scheduleLocksMore = LockedCount(bridge.Meters) >= LockedCount(flat.Meters);
        bool scheduleGroksSooner = bridgeAll >= 0 && (flatAll < 0 || bridgeAll <= flatAll);
        var bM = DomainDepth.UnionAt(bridge.Tape, bStar, heldout); var sM = DomainDepth.UnionAt(shuffle.Tape, bStar, heldout);
        bool bridgeDeeper = bM.MeanSym < sM.MeanSym - 1e-4 || bM.MaxSpan > sM.MaxSpan;
        o.AppendLine("  ── verdict ──");
        o.AppendLine($"    grok-bell reliable (every domain crystallizes under some arm) : {(arms.Any(a => LockedCount(a.Meters) == D) ? "YES" : "partial")} — {LockedCount(bridge.Meters)}/{D} lock under the bridge schedule");
        o.AppendLine($"    schedule locks ≥ whole-pool frontier                          : {(scheduleLocksMore ? "YES" : "no")} (bridge {LockedCount(bridge.Meters)} vs flat {LockedCount(flat.Meters)})");
        o.AppendLine($"    schedule groks the SET in ≤ flat's bytes                      : {(scheduleGroksSooner ? "YES" : "no")} (bridge {(bridgeAll < 0 ? "incomplete" : bridgeAll + "B")} vs flat {(flatAll < 0 ? "incomplete" : flatAll + "B")})");
        o.AppendLine($"    bridge-order deeper than shuffle at matched budget            : {(bridgeDeeper ? "YES" : "no")} (sym/byte {bM.MeanSym:F4} vs {sM.MeanSym:F4}, maxSpan {bM.MaxSpan:F0} vs {sM.MaxSpan:F0})");
        o.Append(scheduleLocksMore && scheduleGroksSooner
            ? "  ⇒ THE BELL SCHEDULES THE WIN — grok → move-on → grok crystallizes the multi-domain set at least as early and completely as the whole-pool frontier, with the coupling bridge-order as the sequence."
            : "  ⇒ no scheduling win at this pool/budget — the whole-pool frontier already concentrates enough (too few domains, too-uniform bridges, or the budget over-runs the schedule; sweep --steps down / more distinct domains).");
        o.AppendLine();

        Console.Write(o.ToString());

        // land the arm curves (the durable corpus — surplus for meta-analysis)
        var sb = new StringBuilder("arm\tdomain\tspans\tlock_round\tlock_bytes\tfinal_cv\tk\tthr\n");
        foreach (var a in arms) for (int d = 0; d < D; d++) { var m = a.Meters[d]; sb.AppendLine($"{a.Label}\t{d}\t{m.Spans}\t{m.LockRound}\t{m.LockBytes}\t{(double.IsNaN(m.Cv) ? "nan" : m.Cv.ToString("F4"))}\t{m.K}\t{m.Threshold(m.Cv, m.K):F4}"); }
        run.Write("locks.tsv", sb.ToString());
        run.Write("bridge.txt", graph.RenderBridgeMatrix(bridgeOrder));
    }

    private static int LockedCount(DomainMeter[] m) { int c = 0; foreach (var x in m) if (x.Locked) c++; return c; }
    private static int BytesToGrokAll(DomainMeter[] m) { int mx = 0; foreach (var x in m) { if (!x.Locked) return -1; mx = Math.Max(mx, x.LockBytes); } return mx; }

    // a FILE arg is a domain; a DIR arg contributes every glob-matched file (recursively) as its own domain.
    private static List<string> GatherDomainFiles(string[] args, string glob)
    {
        var found = new List<string>();
        foreach (var a in Args.Positionals(args, 1))
        {
            if (File.Exists(a)) found.Add(a);
            else if (Directory.Exists(a))
                foreach (var pat in glob.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    found.AddRange(Directory.GetFiles(a, pat, SearchOption.AllDirectories));
        }
        return found.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  CAMPFIRE — the self-regulating 3-WAY (code + NL + EML) as ONE developmental trajectory
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The 3-way campfire: GrokBell over the EXTRINSIC world (code + NL — every FileCorpus domain, sequenced by the
/// coupling bridge-order) braided with the EML replay-calculator as the INTRINSIC generative source. The governance
/// is the machine's own organs, never a mixing ratio:
///   · the GROK-BELL sequences the extrinsic domains (grok → move-on → grok, bridge-order);
///   · the FORK-FIX eats the whole extrinsic world before the replay era — Drained/Exhausted ARE the bell's
///     (EML is bottomless and gates nothing), so the drive's school → mop-up → replay arc is untouched;
///   · the HOMEOSTAT's intake actuator paces BOTH mouths — the one `batch` clock feeds the bell's frontier-pick
///     AND the EML candidate pump (the surprise-clock pacing, no second cadence);
///   · THE REFLECTION LAW gates every EML mint onto the tape BY ITS GRADE:
///     a ladder-certified EXACT mint is WITNESSED — corroborated by the machine's own carried evaluator (math's
///     free corroboration, ) — and lands as born evidence, outside the replay throttle; a non-exact mint (A/S/D/U)
///     is still a REPLAY (hypothesis provenance) until reality corroborates it, so those land only under the
///     unvested-replay headroom (≤ ReplayRatio × real spans — the drive's own rate law, one shared budget with
///     the loopback) and queue FIFO when reality lags. The pending queues are BACKPRESSURE, not a drop filter —
///     a discovery waits for the world to catch up, never dies (the sieve cannot re-mint a deduped line).
/// EML generation is biased by the SHARED drive grammar: pure-RPN chunks surface in the same rule set that holds
/// the code+NL structure, so cross-domain compression IS the spiral's bias — one vocabulary, not three silos —
/// under the THREE-RAIL policy MIX (EmlSampler: chunk-bias for depth · uniform ε for support · the systematic
/// ε-enumeration sweep for coverage — bias may concentrate but never exclude, and the sweep makes coverage a
/// guarantee), with the CORROBORATION REWARD paying the second corroboration (a corroborated-EXACT value-hit enqueues
/// at weight — the bench/anomaly registries corroborate, the grammar concentrates toward the named frontier).
/// Deterministic (the Vow): the bell's schedule, the sampler LCG + rail cursor, and the FIFO landing all replay
/// from seed + state.
public sealed partial class Campfire : ICurriculum, ICurriculumCheckpointDeltaOwner
{
    private readonly GrokBell _bell;                     // the extrinsic backbone (code + NL under the bell + bridge-order)
    private readonly EmlSieve _sieve;                    // the intrinsic verifier-register (dual-point sieve + the live grade ladder)
    private readonly double _dreamRatio;                 // rate law — unvested EML dreams ≤ ratio × real spans (≤0 = the unbounded control arm)
    private readonly int _corrobW;                       // corroborated-EXACT landing weight — the second-corroboration reward (mount default; 1 = off)
    private readonly int _certW;                         // cert-novelty accretion gate — first-capture ×W · paraphrase ×0 (one law with ReplayCalc.AccreteWeight; ≤1 = off)
    private readonly Queue<EmlMint> _pendingE = new();   // EXACT mints awaiting the batch cadence (FIFO — mint order; land as Corroborationed, no headroom)
    private readonly Queue<EmlMint> _pendingH = new();   // hypothesis mints (A/S/D/U) awaiting vest headroom (FIFO; land as Replay at ε) — split so a starved throttle can never block certified evidence behind it
    private readonly EmlSampler _sampler;                // the three-rail candidate source (bias · uniform ε · the ε-enumeration sweep), cursor checkpointed
    private int _emlMinted;                              // EML lines landed on the tape
    private List<EmlGen.Chunk>? _chunks;                 // pure-RPN chunk cache, keyed on grammar identity …
    private GrammarRule[]? _chunkRules;                  // … rebuilt only when the drive re-induces (the cover-cache discipline — PureChunks expands every rule, too heavy per step)

    internal Campfire(FileCorpus fc, CortexRunConfig cfg)
    {
        _bell = new GrokBell(fc, cfg.GrokCv, cfg.LockRounds, cfg.Seed, cfg.MixEvery, cfg.DomStrideSpans, cfg.FrontierCapExps);
        _sieve = new EmlSieve(ReplayCalc.MountSig);
        _dreamRatio = cfg.ReplayRatio;
        var k = EmlKnobs.Mount;
        _corrobW = Math.Max(1, k.CorrobW);
        _certW = Math.Max(1, k.CertW);
        _sampler = new EmlSampler(k.Units, k.MaxLen, k.Gain, k.Eps, k.EpsEnum, k.SeedK, cfg.Seed);
    }

    // the schedule reads are the BELL's — the extrinsic world governs the eras; the intrinsic source gates nothing.
    public bool Drained => _bell.Drained;
    public bool Exhausted => _bell.Exhausted;
    public int IngestedCount => _bell.IngestedCount;     // extrinsic spans only — fork_vol_frac's denominator is the real pool
    public int WorkloadCount => _bell.WorkloadCount;
    public int MixAffirmSkips => _bell.MixAffirmSkips;   // the affirm-gate rides the bell's world mouth
    public int IngestDiversity => _bell.IngestDiversity;
    public int StreakResets => _bell.StreakResets;
    public int MixEvery { get => _bell.MixEvery; set => _bell.MixEvery = value; }
    public double LastPickCoverage => _bell.LastPickCoverage;   // the extrinsic frontier's read — EML mints are intrinsic, not world-edge
    public IReadOnlyList<TapeEventID> LastWorldEventIDs => _bell.LastWorldEventIDs;

    /// The EML tally (mints · K-frontier · pending backpressure) — the campfire's own telegraph reads.
    public (int Minted, int KFrontier, int Pending) EmlReads => (_emlMinted, _sieve.KFrontier, _pendingE.Count + _pendingH.Count);

    public void AppendProbeSamples(List<byte[]> samples) => _bell.AppendProbeSamples(samples);

    // ── MemStat census reads — the campfire's two stateful organs + the pending lanes. Counts only. ──
    internal GrokBell Bell => _bell;
    internal EmlSieve SieveOrgan => _sieve;
    internal (int E, int H) PendingMass() => (_pendingE.Count, _pendingH.Count);

    /// Reality first: the bell's anchor span lands, then the axiom shells (K ≤ MountSeedK — e, eˣ, ln re-found)
    /// are sieved and queued; the newborn's EXACT axioms land as Corroborationed (certified — no throttle), while its
    /// hypothesis mints land only as much as its one real span affords (the vest gate holds from step 0).
    public void Seed(Tape tape, Journal journal)
    {
        _bell.Seed(tape, journal);
        EmlOfferContext context = new(_bell.LastWorldEventIDs);
        foreach (var prog in EmlGen.Enumerate(1, ReplayCalc.MountSeedK)) _sieve.Offer(prog, in context);
        QueueFresh();
        Land(tape, journal, step: 0, cap: int.MaxValue);
    }

    public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        var ext = _bell.Draw(grammar, tape, journal, step, batch);       // extrinsic before intrinsic — reality precedes the replay
        int landed = Pump(grammar, tape, journal, step, batch, _bell.LastWorldEventIDs);
        return new IntakeStep(ext.Ingested + landed, ext.Advanced, ext.Domain);
    }

    public bool Advance(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        bool fed = _bell.Advance(grammar, tape, journal, step, batch);   // the mop-up keeps eating reality …
        Pump(grammar, tape, journal, step, batch, _bell.LastWorldEventIDs); // … and the replay keeps pace beside it
        return fed;                                                      // exhaustion is the bell's alone
    }

    /// Post-fork the verified pump keeps replaying (ICurriculum.Replay — the drive's replay-era side-channel), sharing
    /// the one unvested headroom with the autoregressive loopback: corroborate more, replay more.
    public int Replay(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
        => Pump(grammar, tape, journal, step, batch, Array.Empty<TapeEventID>());

    public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
        => _bell.Mix(cortex, grammar, tape, journal, step, affirmCut);

    public void MixOne(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
        => _bell.MixOne(cortex, grammar, tape, journal, step, affirmCut);

    // one EML pump: draw `batch` candidates off the three-rail sampler (chunk-bias over the SHARED drive grammar's
    // pure-RPN chunks · uniform ε · the systematic ε-enumeration sweep), sieve them, queue the fresh graded mints,
    // land at the same batch cadence.
    private int Pump(RePairResult grammar, Tape tape, Journal journal, int step, int batch, IReadOnlyList<TapeEventID> opportunityEvents)
    {
        int kBefore = _sieve.KFrontier;
        if (!ReferenceEquals(grammar.Rules, _chunkRules)) { _chunks = EmlGen.PureChunks(grammar); _chunkRules = grammar.Rules; }
        EmlOfferContext context = new(opportunityEvents);
        for (int b = 0; b < batch; b++)
            _sieve.Offer(_sampler.Next(_chunks!), in context);
        QueueFresh();
        int landed = Land(tape, journal, step, cap: batch);
        if (_sieve.KFrontier > kBefore)
            Trace.Cortex.Boundary("eml.k", $"step={step} K-frontier {kBefore}→{_sieve.KFrontier} · mints {_sieve.MintLog.Count} · landed {_emlMinted} · pending {_pendingE.Count + _pendingH.Count}");
        return landed;
    }

    // route fresh mints by their live grade under THE CERT-GATED ACCRETION LAW (ReplayCalc.AccreteWeight — ONE
    // weight authority, two mouths): a novel-corroborated EXACT enqueues at the second-corroboration weight (repetition
    // IS weight to Re-Pair — the reward concentrates the shared grammar adjacent-to-known), a certificate
    // FIRST-capture at the cert weight, a PARAPHRASE not at all (the sieve register keeps it; the tape starves it —
    // ). EXACT → the Corroborationed lane; the rest → the hypothesis (Replay) lane.
    private void QueueFresh()
    {
        var mints = _sieve.NewMints;
        for (int i = 0; i < mints.Count; i++)
        {
            var m = mints[i];
            var lane = m.Grade == 'E' ? _pendingE : _pendingH;
            for (int j = 0, w = ReplayCalc.AccreteWeight(in m, _sieve.NewMintFirst(i), _corrobW, _certW); j < w; j++)
                lane.Enqueue(m);
        }
        _sieve.DrainNewMints();
    }

    // THE VESTING GATE, grade-routed — at most `cap` spans land per call (the batch cadence, 's span-rate
    // discipline). EXACT mints land FIRST as Corroborationed (born evidence — the ladder is their independent corroboration,
    // so the replay throttle does not apply and a starved throttle can never block certified evidence); hypothesis
    // mints then land as Replay under the unvested-replay headroom (ReplayRatio × real − unvested; the drive's
    // replay-throttle formula verbatim — one law, two mouths). The rest wait in FIFO.
    private int Land(Tape tape, Journal journal, int step, int cap)
    {
        int landed = 0;
        while (landed < cap && _pendingE.Count > 0)
        {
            EmlMint mint = _pendingE.Dequeue();
            TapeEventID eventID = TapePacketCreator.AppendEmlMint(tape, journal, step, mint);
            _sieve.BindMintEvent(in mint, eventID);
            landed++; _emlMinted++;
        }
        long headroom = _dreamRatio <= 0 ? long.MaxValue : tape.ComputeUnreflectedHeadroom(_dreamRatio);
        int hLanded = 0;
        while (landed < cap && hLanded < headroom && _pendingH.Count > 0)
        {
            EmlMint mint = _pendingH.Dequeue();
            TapeEventID eventID = TapePacketCreator.AppendEmlMint(tape, journal, step, mint);
            _sieve.BindMintEvent(in mint, eventID);
            landed++; hLanded++; _emlMinted++;
        }
        return landed;
    }

    // checkpoint — the bell whole, then the EML side: the sieve register, the three-rail sampler (LCG + the
    // ε-enumeration cursor), the landed count, the two pending backpressure queues (FIFO order preserved per
    // lane). The chunk cache is identity-keyed — rebuilds off the restored grammar.
    public void SaveState(CkptWriter w)
    {
        _bell.SaveState(w);
        _sieve.Save(w);
        _sampler.Save(w); w.I32(_emlMinted);
        w.I32(_pendingE.Count);
        foreach (var m in _pendingE) WritePendingMint(w, in m);
        w.I32(_pendingH.Count);
        foreach (var m in _pendingH) WritePendingMint(w, in m);
    }

    public void LoadState(CkptReader r)
    {
        _bell.LoadState(r);
        _sieve.Load(r);
        _sampler.Load(r); _emlMinted = r.I32();
        _pendingE.Clear();
        int ne = r.I32();
        for (int i = 0; i < ne; i++) _pendingE.Enqueue(ReadPendingMint(r));
        _pendingH.Clear();
        int nh = r.I32();
        for (int i = 0; i < nh; i++) _pendingH.Enqueue(ReadPendingMint(r));
        _chunkRules = null; _chunks = null;
    }

    private static void WritePendingMint(CkptWriter w, in EmlMint mint)
    {
        w.Str(mint.Line);
        w.Str(mint.Prog);
        w.I64(mint.Sig.R1); w.I64(mint.Sig.I1); w.I64(mint.Sig.R2); w.I64(mint.Sig.I2);
        w.U8((byte)mint.Grade);
        w.Bool(mint.Corrob);
    }

    private static EmlMint ReadPendingMint(CkptReader r)
        => new(r.Str(), r.Str(), new EmlSig(r.I64(), r.I64(), r.I64(), r.I64()), (char)r.U8(), r.Bool());
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE GROK-BELL METER — the curriculum's per-domain "I've grokked this" signal (shared by GrokBell + CritLock)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// One domain's grok state as its spans accrete: the criticality-CV LOCK bell + the chatter it debounces. The
/// crystallization signal is Engine.RenormStats.CvZ (the per-scale Zipf-exponent variation; LOW ⟹ the SAME
/// power-law at every scale = a critical RG fixed point = the grok). A raw single-round CV<line CHATTERS on a
/// mixed feed, so the bell fires only on a LOCK — CV below the lock line for `lockRounds` consecutive rounds
/// (the hysteresis). THE LOCK LINE IS k-AWARE: CvZ is a CV estimated from only k = RenormStat.KZ per-scale
/// slopes, so it carries sampling noise sd = CV·√(1/(2k)+CV²/k) that NO amount of grokking removes (RESULTS
/// "correction": trunk_0106's post-warmup cvz sd 0.042 ≡ the analytic sd at k≈12 — the chatter IS the estimator).
/// The line is therefore cvFloor (the scale-scatter a genuinely critical state keeps — the SOC value, 0.15)
/// + bandSigmas sampling-sds of headroom: thr = cvFloor + bandSigmas·cv·√(1/(2k)+cv²/k). Per-domain
/// stratification falls out free — a shallow domain (small k) gets a wide band instead of starving on estimator
/// noise; a deep one (large k) tightens onto the floor. bandSigmas=0 restores the flat-threshold bell (the
/// kill-line's control arm); the old flat 0.20 ≈ this formula's k≈12 special case. `BelowStreak` is the live
/// counter; `Crossings` counts downward touches (the reliability control's chatter magnitude); `FirstCrossRound`
/// is the naive un-debounced bell; `LockRound`/`LockBytes` are the REAL bell (when the streak first reached
/// `lockRounds`). Never-locked reports LockRound = −1. Floor + depth are per-meter (the --cv / --lock knobs),
/// so a sweep changes the bell, not a const. Promoted from CritLock into the curriculum organ (its true home —
/// the grok bell IS the curriculum's move-on signal); CritLock reads it too.
internal sealed class DomainMeter(double cvFloor, int lockRounds, double bandSigmas = 1.5)
{
    public int Spans;                    // this domain's own spans on the tape
    public double Cv = double.NaN;       // last per-domain CV read
    public int K;                        // last per-domain KZ read (the CV's sample size — the band input)
    public double BestSym = 1.0;         // final per-domain deepest held-out sym/byte (↓ = deeper), filled at drain end
    public int BelowStreak;              // consecutive rounds Cv < the lock line
    public int StreakResets;             // pre-lock streak breakages (BelowStreak>0 → 0) — C2's bell-thrash read (the cvz mask must keep this at 0 inside breach windows)
    public int Crossings;                // # downward crossings of the lock line (chatter magnitude)
    public int FirstCrossRound = -1;     // round of the FIRST Cv < line (the naive, un-debounced bell)
    public int LockRound = -1;           // round the streak first hit lockRounds (the REAL bell)
    public int LockBytes = -1;           // total accreted bytes at the lock (bytes-to-grok)
    private bool _wasBelow;

    /// THE ONE k-aware CV* OWNER: the
    /// lock line for a (cv, k) read = floor + band sampling-sds. The instance Threshold below and the
    /// Homeostat's KAwareLock both read THIS — one formula, no drift. k<2 ⟺ CvZ is NaN anyway (a CV needs ≥2
    /// slopes), so the degenerate band is 0 and lock reads stay false on the NaN guard.
    internal static double CvStar(double cv, int k, double cvFloor, double bandSigmas)
        => double.IsNaN(cv) || k < 2 ? cvFloor : cvFloor + bandSigmas * cv * Math.Sqrt(1.0 / (2 * k) + cv * cv / k);

    /// The k-aware lock line at this meter's floor/band knobs.
    public double Threshold(double cv, int k) => CvStar(cv, k, cvFloor, bandSigmas);

    /// Fold this round's per-domain (CV, k) read into the meter; returns true on the round the bell LOCKS.
    public bool Observe(double cv, int k, int round, int totalBytes)
    {
        Cv = cv; K = k;
        bool below = !double.IsNaN(cv) && cv < Threshold(cv, k);
        if (below && !_wasBelow) { Crossings++; if (FirstCrossRound < 0) FirstCrossRound = round; }
        _wasBelow = below;
        if (!below && BelowStreak > 0 && LockRound < 0) StreakResets++;   // a building lock broken pre-bell — the thrash quantum C2 must hold at 0 inside breach windows
        BelowStreak = below ? BelowStreak + 1 : 0;
        if (LockRound < 0 && BelowStreak >= lockRounds) { LockRound = round; LockBytes = totalBytes; return true; }
        return false;
    }

    public bool Locked => LockRound >= 0;

    internal DomainMeterCheckpointDelta CaptureCheckpointDelta()
        => new(Spans, Cv, K, BestSym, BelowStreak, StreakResets, Crossings, FirstCrossRound, LockRound, LockBytes, _wasBelow);

    internal void ApplyCheckpointDelta(in DomainMeterCheckpointDelta delta)
    {
        Spans = delta.Spans; Cv = delta.Cv; K = delta.K; BestSym = delta.BestSym;
        BelowStreak = delta.BelowStreak; StreakResets = delta.StreakResets; Crossings = delta.Crossings;
        FirstCrossRound = delta.FirstCrossRound; LockRound = delta.LockRound; LockBytes = delta.LockBytes; _wasBelow = delta.WasBelow;
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in DomainMeterCheckpointDelta delta)
    { writer.I32(delta.Spans); writer.F64(delta.Cv); writer.I32(delta.K); writer.F64(delta.BestSym); writer.I32(delta.BelowStreak); writer.I32(delta.StreakResets); writer.I32(delta.Crossings); writer.I32(delta.FirstCrossRound); writer.I32(delta.LockRound); writer.I32(delta.LockBytes); writer.Bool(delta.WasBelow); }

    internal static DomainMeterCheckpointDelta ReadCheckpointDelta(CkptReader reader)
        => new(reader.I32(), reader.F64(), reader.I32(), reader.F64(), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.Bool());

    /// THE per-domain crystallization read (the one shared by GrokBell's focus-domain Draw, the flat-arm's external
    /// per-domain trail, and CritLock's kill-lines — collapsed here so the stride/cache/NaN-guard logic can't drift).
    /// Fold this domain's ISOLATED criticality-CV into the bell, O(Δ)-gated: re-induce its isolated grammar only
    /// across a `domStride` span jump (else replay the cached (CV, k) — a grokked domain stays grokked while intake
    /// concentrates elsewhere). Below `minSpans` the grammar has too few rules per scale and the CV is NaN. `cnt` is
    /// this domain's ingested span count; `src` materializes its spans (called only on a stride miss). The stride
    /// cache is CALLER-OWNED (`lastSpans`/`cachedCv`/`cachedK` — GrokBell checkpoints them; the kill-lines keep them
    /// local) so the checkpoint dialect is untouched. Returns true on the LOCK round.
    public bool ReadCv<TSrc>(int cnt, int minSpans, int domStride, ref int lastSpans, ref double cachedCv, ref int cachedK, int round, int totalBytes, TSrc src)
        where TSrc : struct, IDomainSpanSource
    {
        Spans = cnt;
        double cv; int kz;
        if (cnt < minSpans) { cv = double.NaN; kz = 0; }
        else if (lastSpans < 0 || cnt - lastSpans >= domStride)   // re-induce only past the span-stride (O(Δ) gate)
        {
            var rn = Engine.RenormStats(Engine.Induce(src.Concat()).Result);
            cv = rn.CvZ; kz = rn.KZ;
            lastSpans = cnt; cachedCv = cv; cachedK = kz;
        }
        else { cv = cachedCv; kz = cachedK; }                     // unchanged domain: (CV, k) hold (streak continues)
        return Observe(cv, kz, round, totalBytes);
    }

    // checkpoint — the bell's whole memory, hysteresis edge included (cvFloor/lockRounds/bandSigmas are ctor
    // inputs, rebuilt).
    public void Save(CkptWriter w)
    {
        w.I32(Spans); w.F64(Cv); w.I32(K); w.F64(BestSym);
        w.I32(BelowStreak); w.I32(StreakResets); w.I32(Crossings); w.I32(FirstCrossRound); w.I32(LockRound); w.I32(LockBytes);
        w.Bool(_wasBelow);
    }

    public void Load(CkptReader r)
    {
        Spans = r.I32(); Cv = r.F64(); K = r.I32(); BestSym = r.F64();
        BelowStreak = r.I32(); StreakResets = r.I32(); Crossings = r.I32(); FirstCrossRound = r.I32(); LockRound = r.I32(); LockBytes = r.I32();
        _wasBelow = r.Bool();
    }
}
