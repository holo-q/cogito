namespace Cogito;

using System.Text;
using Cogito.Induct;

// ── THE RHYTHM (EMERGENT METABOLISM) ──  day/dream/aestivation is ONE loop with different INPUT, and the machine
// chooses, EVERY step, where its next input comes from — the world (frontier intake), itself (the grammar's own
// utterance looped back), or nothing (reorganize). This replaces the hard school→dream fork (a binary switch at
// world-drain) with a per-step decision read off the machine's own senses:
//
//   DAY    fold EXTERNAL input   — frontier-pick corpus spans (school / mop-up), or re-admissionPlan reality via the
//                                  MIX rail once the pool is volume-consumed (the world-channel NEVER closes).
//   DREAM  fold SELF input       — mint the generated block onto the tape as Replay-provenance spans (the same
//                                  splice+pump fold; only the span SOURCE differs), outcomeCredit-throttled.
//   AESTIVATION  fold NOTHING          — an input-free step; the sleep gate consolidates (rebase/compress/shed/breach).
//
// THE DECISION is a heuristic over the current window's features, structured to graduate to a learned predictor:
//   dream-worth = gain · explained · (Wg·grok + Wc·coverage + Ws·calm)
//     explained = 1 − frontier-residual (EMA of 1 − pick-coverage over recent day draws: how much novel structure
//                 the world's EDGE still holds — falls as the frontier is eaten/grokked, jumps on a fresh domain)
//     grok      = the k-aware whole-grammar bell proximity (CvStar/cvz — 1 at/below the lock line)
//     coverage  = held-out breadth (the generalization saturation read)
//     calm      = 1 − self-model excursion mint-rate (the machine is not currently surprising itself)
//     gain      = 0.5 + yield⌂ in steady state, but FLOORED at 1.0 inside a fresh-frontier window (EMA of resolved
//                 dream-cohort vest-fraction — OUTCOME feedback that MODULATES, never terminally GATES). Two regimes,
//                 the FRONTIER (not the yield) picking between them: a fresh-domain re-opening (residual rising
//                 ≥FreshFrontier over its low-water) floors the gain at 1.0 for FreshWindowSteps, so the re-opened
//                 edge crosses ReplayLine on its OWN whatever the stale yield says (the RETURN-swing); in steady state
//                 0.5+yield lets a outcomeCredit streak AMPLIFY (>1) and a barren streak THROTTLE (→0.5, worth dips
//                 sub-line → the mind flips to day-drain, the DOWN-swing that opens the next fresh frontier). The
//                 first shape was steady-state ONLY: once a barren stretch drove yield→0 it HALVED the worth even at
//                 a re-opened frontier — a ONE-WAY latch off (cogito#6, the terminal habit-lock). Neutral 1.0 until
//                 the first outcome lands. The window is the missing up-swing; the yield throttle is the down-swing.
//   MULTIPLICATIVE in explained, deliberately: the world's edge must be explained BEFORE the other senses can
//   vote to dream — explained is the one true veto (a composed-out edge shows as low residual → the machine
//   returns to day, whatever the other senses say). The first (additive) shape let saturated grok/coverage/calm
//   OUTVOTE a fully-novel frontier (measured: a late fresh-domain crossing shot residual to 0.96 while dream_frac
//   held 1.0 — worth floored at ~0.59 with explained=0). Replay-fraction therefore RISES emergently as
//   frontier-residual falls + grok locks + coverage saturates — never at a fixed step — and COLLAPSES back to day
//   when a fresh domain re-opens the residual, then RE-IGNITES as that fresh edge is explained (the return-swing).
//
// THE ε-BOOTSTRAP: the decision cannot be learned without outcome data, so early on (until BootOutcomes dream
// cohorts have resolved, step-ceilinged for the wScale=1 arm where vests never fire) a would-be-Day step becomes
// a Replay with annealing probability ε — the machine DISCOVERS that dreaming exists and which windows make it
// productive. Deterministic (stateless seed+step draw — the Vow). The anneal is PER-EPISODE, not birth-to-death:
// a fresh-domain re-opening (frontier-residual rising ≥FreshFrontier over its recent low-water) RE-ORIGINS the ε
// epoch, so a barren-latched machine regains bootstrap ε to re-discover productive dreams on the NEW edge — even
// before grok re-locks on it (the gain floor covers the already-grokked case; the ε re-seed covers the fresh-and-
// ungrokked one). Without the re-seed the anneal was a ONE-WAY door: nothing re-opened exploration once it shut.
//
// THE SELF-REFERENCE: every decision token (d/m/a) and every resolved outcome token (v+/v0) folds into a third
// MetaGrammar channel — the SAME instrument the self-model runs on its excursion/thought streams — so the
// when-to-dream pattern becomes grammatical structure the machine predicts (the rhythm channel's mint-rate falls
// as its own rhythm becomes lawful). The heuristic stays in command; the channel's prediction + hit-rate are the
// graduation seam (a learned decider would read _met.Predict — deliberately NOT armed by default: a predictor
// that drives the decisions it predicts can lock into pure habit, detached from the features; the readout comes
// first, the takeover is a future knob).
//
// THE NEVER-PURE INVARIANT: a dream vests ONLY by corroborating against real exercise (the
// Reflection Law — unchanged), the unvested-dream stock stays capped at ReplayRatio x born evidence (a bound decision
// falls back to Day: evidence must catch up before more hypotheses), and the MIX rail rides dream steps while post-exhaustion
// Day steps re-ingest reality DIRECTLY — at any dream-fraction, the world-channel stays open.
//
// AESTIVATION stays the HOMEOSTAT's law (extend the surprise-clock, no parallel machinery): the rhythm never defers a
// due aestivation — it CONSUMES the geometric byte-stride read at step top (ConsolidationPhaseDue), so a due aestivation becomes a chosen,
// input-free step and Consolidate fires at the standing gate. Grok-lock naps (a day step whose draw crossed a
// bridge) stay same-step light aestivations, exactly as today.

/// The three input-sources of the one loop — the rhythm's per-step decision (Day = world, Replay = self,
/// ConsolidationPhase = none/reorganize).
public enum MetabolicPhases : byte { Day, Replay, ConsolidationPhase }

internal enum RhythmPolicyMetricIDs : ushort
{
    Criticality = 300,
    CriticalitySamples,
    Coverage,
    ExperienceMint,
    Exhausted,
    ReplayHeadroom,
    ConsolidationPhaseDue,
    FrontierResidual,
    Productivity,
    Magnitude,
}

public readonly struct RhythmChoice
{
    internal RhythmChoice(MetabolicPhases phase, in CortexPolicyDecision policyDecision, bool observed)
    {
        Phase = phase;
        PolicyDecision = policyDecision;
        Observed = observed;
    }

    public MetabolicPhases Phase { get; }
    internal CortexPolicyDecision PolicyDecision { get; }
    internal bool Observed { get; }
}

/// One step's decision features — populated by the drive at the FORK point (reads + self-model fresh).
/// `ReplayHeadroom` is the rate-law headroom (unvested cap minus stock); `ConsolidationPhaseDue` is the homeostat's
/// geometric byte-stride read, consumed here so the due aestivation becomes an input-free chosen step.
public readonly record struct RhythmSenses(
    double Cvz, int Kz,          // the k-aware whole-grammar grok bell (LossReading.CvZ/KZ)
    double Coverage,             // held-out breadth (generalization saturation)
    double ExcMint,              // the self-model's rolling excursion mint-rate (surprise)
    bool Exhausted,              // the pool is volume-consumed (the old fork's gate — now just a feature)
    long ReplayHeadroom,          // ReplayRatio x born evidence - unvested-dream stock (<= 0 = the reflection law binds)
    bool ConsolidationPhaseDue);              // the homeostat's SleepDue at step top (the surprise-clock's rest cadence)

/// The per-step metabolic scheduler — the organ that replaces the hard school→dream fork. Owns the DECISION only:
/// the drive feeds it senses and executes the phase; the curricula stay the hands, the homeostat stays the aestivation's
/// clock. Constructed ALWAYS (its state rides the checkpoint uniformly, like the homeostat); CONSULTED only when
/// The Cortex mounts rhythm by default; --no-rhythm isolates the former fixed three-era scheduler.
public sealed class Rhythm(int cohortHorizonSpans)
{
    public static CortexPolicyID PolicyID { get; } = CortexPolicyID.Parse("rhythm.phase");
    public static CortexPolicySchema PolicySchema { get; } = new(PolicyID, featureCount: 8,
        actionCount: 3, outcomeCount: 2);

    public static PolicyCanonicalStateID CanonicalizePolicyState(
        bool aestivationDue,
        bool headroomBound,
        bool worthAtLeastReplayLine,
        bool epsilonEligible,
        bool freshFrontier,
        bool epsilonFired)
        => PolicyCanonicalStates.Rhythm(
            PolicyID,
            aestivationDue,
            headroomBound,
            worthAtLeastReplayLine,
            epsilonEligible,
            freshFrontier,
            epsilonFired);
    // ── the decision's shape (deterministic consts — the heuristic is the spec, not a config surface) ──
    const double WGrok = 0.42, WCoverage = 0.33, WCalm = 0.25;   // the corroborating-sense blend (Σ=1) — explained multiplies it (the frontier holds veto power)
    const double ReplayLine = 0.50;        // worth at/above this → Replay (≈ a two-thirds-explained edge with the senses mostly saturated)
    const double Eps0 = 0.25;             // ε-bootstrap starting probability (a would-be-Day becomes a Replay)
    const int    BootOutcomes = 16;       // resolved dream cohorts that fully anneal ε → 0 (the outcome-driven arm)
    const int    BootStepCeil = 400;      // hard step ceiling on the anneal — the wScale=1 arm never resolves an outcome (vests never fire), so ε must still die
    const double FreshFrontier = 0.10;    // residual RISE (above the recent low-water reference) that re-opens the edge = a fresh exploration episode → open the fresh-frontier window (jitter-proof: measured domain re-openings shot residual +0.19, EMA noise stays under ~0.05)
    const int    FreshWindowSteps = 256;  // the fresh-frontier window length — the gain floors at 1.0 for this long after a re-opening, giving the re-ignited dreams a full cohort-resolution cycle (~1024 appends ÷ ~4 dream-spans/step) to VEST and hand off to the yield≥0.5 steady state before the floor lifts (else the down-throttle would re-fire before productive dreams could raise the yield)
    const double ResidualDrift = 1.0 / 16;// pick-coverage EMA horizon (the HomeBand close-cadence drift, reused)
    const double YieldDrift = 1.0 / 8;    // cohort-yield EMA horizon (outcomes are sparse — track faster)
    const int    WindowSteps = 64;        // rolling dream-fraction window + the periodic telegraph cadence
    const int    MetaInduceEvery = 16;    // rhythm-channel re-induce cadence in steps (the channel folds EVERY step — per-event induction would double the fold cost for a signal that only needs ≤16-token freshness)
    const int    MaxPendingCohorts = 64;  // bounded outcome journal — the bootstrap needs the early cohorts; steady state rides the EMA'd yield of whichever fit

    readonly int _cohortHorizon = cohortHorizonSpans;   // appends before a cohort's verdict is FINAL — the drive's drop horizon (vested by then, or dropped as a stale hypothesis)

    // ── the state (checkpointed whole — kill→resume byte-exact) ──
    double _residual = 1.0;               // frontier-residual EMA — the world is fully novel at birth
    double _residualRef = 1.0;            // the residual's recent LOW-water reference — falls with the residual (edge explained), holds against a rise; a rise ≥FreshFrontier above it is a fresh-domain re-opening (the ε re-seed trigger)
    double _yield = double.NaN;           // cohort vest-fraction EMA — NaN until the first outcome (gain neutral)
    int _outcomes, _cohortsOutcomeCredited;        // resolved cohorts + those with ≥1 vest (the ε anneal + the report)
    int _epsEpochStep, _epsOutcomeBase;   // the ε anneal's ORIGIN — reset to (step, _outcomes) each fresh-domain re-opening: a new frontier deserves fresh exploration budget, so the anneal measures from the epoch, not from birth (the lifetime totals above stay intact for the report + yield EMA)
    int _freshUntil;                      // the step through which the fresh-frontier window is OPEN (gain floors at 1.0 — the return-swing's up-force); 0 = closed, steady-state yield modulation governs
    int _reseeds;                         // fresh-frontier re-ignitions fired (the return-swing census)
    int _daySteps, _dreamSteps, _aestivationSteps, _epsFires, _boundDays;   // the phase census (+ headroom-bound day fallbacks)
    MetabolicPhases _prev = MetabolicPhases.Day;                       // last decision — the trace edge detector
    MetabolicPhases _lastPhase = MetabolicPhases.Day;                  // the curve row's phase column
    double _lastWorth, _lastEps;          // the curve row's decision internals
    readonly Queue<byte> _window = new(); // recent phases — the rolling dream-fraction readout
    readonly Queue<Cohort> _cohorts = new();                           // pending dream cohorts awaiting their verdict
    readonly MetaGrammar _met = new();    // the rhythm channel — the machine predicting its OWN metabolism
    RhythmChoice _pendingDayChoice;
    int _dayPolicyOutcomes;
    int _dreamPolicyOutcomes;
    int _aestivationPolicyOutcomes;
    double _policyProductivity;
    double _policyMagnitude;

    /// One dream step's minted span ids — resolved against the tape once aged past the drop horizon (by then
    /// every member either vested — corroborated by real exercise — or dropped as a stale hypothesis).
    internal readonly record struct Cohort(int Step, long MaxId, long[] Sids, CortexPolicyDecision PolicyDecision);

    internal readonly record struct RhythmCheckpointDelta(
        double Residual,
        double ResidualRef,
        double Yield,
        long PendingLow,
        long PendingHigh,
        bool PendingDayOutcome,
        RhythmChoice PendingDayChoice,
        int DayPolicyOutcomes,
        int ReplayPolicyOutcomes,
        int ConsolidationPhasePolicyOutcomes,
        double PolicyProductivity,
        double PolicyMagnitude,
        int Outcomes,
        int CohortsOutcomeCredited,
        int EpsilonEpochStep,
        int EpsilonOutcomeBase,
        int FreshUntil,
        int Reseeds,
        int DaySteps,
        int ReplaySteps,
        int ConsolidationPhaseSteps,
        int EpsilonFires,
        int BoundDays,
        MetabolicPhases Previous,
        MetabolicPhases LastPhase,
        double LastWorth,
        double LastEpsilon,
        byte[] Window,
        Cohort[] Cohorts)
    {
        internal bool IsEmpty => false;
    }

    internal RhythmCheckpointDelta CaptureCheckpointDelta()
        => new(_residual, _residualRef, _yield, _pendLo, _pendHi, _pendingDayOutcome, _pendingDayChoice,
            _dayPolicyOutcomes, _dreamPolicyOutcomes, _aestivationPolicyOutcomes, _policyProductivity, _policyMagnitude,
            _outcomes, _cohortsOutcomeCredited, _epsEpochStep, _epsOutcomeBase, _freshUntil, _reseeds,
            _daySteps, _dreamSteps, _aestivationSteps, _epsFires, _boundDays, _prev, _lastPhase, _lastWorth, _lastEps,
            _window.ToArray(), _cohorts.ToArray());

    internal void ApplyCheckpointDelta(in RhythmCheckpointDelta delta)
    {
        if (delta.Window is null || delta.Window.Length > WindowSteps)
            throw new InvalidDataException("rhythm window exceeds bound");
        if (delta.Cohorts is null || delta.Cohorts.Length > MaxPendingCohorts)
            throw new InvalidDataException("rhythm cohort queue exceeds bound");
        _residual = delta.Residual; _residualRef = delta.ResidualRef; _yield = delta.Yield;
        _pendLo = delta.PendingLow; _pendHi = delta.PendingHigh; _pendingDayOutcome = delta.PendingDayOutcome;
        _pendingDayChoice = delta.PendingDayChoice;
        _dayPolicyOutcomes = delta.DayPolicyOutcomes; _dreamPolicyOutcomes = delta.ReplayPolicyOutcomes; _aestivationPolicyOutcomes = delta.ConsolidationPhasePolicyOutcomes;
        _policyProductivity = delta.PolicyProductivity; _policyMagnitude = delta.PolicyMagnitude;
        _outcomes = delta.Outcomes; _cohortsOutcomeCredited = delta.CohortsOutcomeCredited;
        _epsEpochStep = delta.EpsilonEpochStep; _epsOutcomeBase = delta.EpsilonOutcomeBase; _freshUntil = delta.FreshUntil; _reseeds = delta.Reseeds;
        _daySteps = delta.DaySteps; _dreamSteps = delta.ReplaySteps; _aestivationSteps = delta.ConsolidationPhaseSteps; _epsFires = delta.EpsilonFires; _boundDays = delta.BoundDays;
        _prev = delta.Previous; _lastPhase = delta.LastPhase; _lastWorth = delta.LastWorth; _lastEps = delta.LastEpsilon;
        _window.Clear(); foreach (byte phase in delta.Window) _window.Enqueue(phase);
        _cohorts.Clear(); foreach (Cohort cohort in delta.Cohorts) _cohorts.Enqueue(cohort);
    }

    internal void CommitCheckpointDelta() { }

    internal static void WriteCheckpointDelta(CkptWriter writer, in RhythmCheckpointDelta delta)
    {
        writer.U8(1); writer.F64(delta.Residual); writer.F64(delta.ResidualRef); writer.F64(delta.Yield); writer.I64(delta.PendingLow); writer.I64(delta.PendingHigh); writer.Bool(delta.PendingDayOutcome); RhythmChoice pendingChoice = delta.PendingDayChoice; SaveChoice(writer, in pendingChoice);
        writer.I32(delta.DayPolicyOutcomes); writer.I32(delta.ReplayPolicyOutcomes); writer.I32(delta.ConsolidationPhasePolicyOutcomes); writer.F64(delta.PolicyProductivity); writer.F64(delta.PolicyMagnitude);
        writer.I32(delta.Outcomes); writer.I32(delta.CohortsOutcomeCredited); writer.I32(delta.EpsilonEpochStep); writer.I32(delta.EpsilonOutcomeBase); writer.I32(delta.FreshUntil); writer.I32(delta.Reseeds);
        writer.I32(delta.DaySteps); writer.I32(delta.ReplaySteps); writer.I32(delta.ConsolidationPhaseSteps); writer.I32(delta.EpsilonFires); writer.I32(delta.BoundDays); writer.U8((byte)delta.Previous); writer.U8((byte)delta.LastPhase); writer.F64(delta.LastWorth); writer.F64(delta.LastEpsilon);
        writer.I32(delta.Window.Length); foreach (byte phase in delta.Window) writer.U8(phase);
        writer.I32(delta.Cohorts.Length); foreach (Cohort cohort in delta.Cohorts) { writer.I32(cohort.Step); writer.I64(cohort.MaxId); writer.I32(cohort.Sids.Length); foreach (long sid in cohort.Sids) writer.I64(sid); CortexPolicyDecision decision = cohort.PolicyDecision; SavePolicyDecision(writer, in decision); }
    }

    internal static RhythmCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown rhythm checkpoint delta version");
        double residual = reader.F64(), residualRef = reader.F64(), yield = reader.F64(); long lo = reader.I64(), hi = reader.I64(); bool pending = reader.Bool(); RhythmChoice choice = LoadChoice(reader);
        int dpo = reader.I32(), mpo = reader.I32(), apo = reader.I32(); double productivity = reader.F64(), magnitude = reader.F64(); int outcomes = reader.I32(), vested = reader.I32(), epoch = reader.I32(), baseOutcome = reader.I32(), fresh = reader.I32(), reseeds = reader.I32();
        int daySteps = reader.I32(), dreamSteps = reader.I32(), aestivationSteps = reader.I32(), epsilonFires = reader.I32(), boundDays = reader.I32(); MetabolicPhases previous = (MetabolicPhases)reader.U8(), last = (MetabolicPhases)reader.U8(); double worth = reader.F64(), epsilon = reader.F64();
        int nw = reader.I32(); if (nw < 0 || nw > WindowSteps) throw new InvalidDataException("rhythm window exceeds bound"); byte[] window = new byte[nw]; for (int i = 0; i < nw; i++) window[i] = reader.U8();
        int nc = reader.I32(); if (nc < 0 || nc > MaxPendingCohorts) throw new InvalidDataException("rhythm cohort queue exceeds bound"); Cohort[] cohorts = new Cohort[nc];
        for (int i = 0; i < nc; i++) { int step = reader.I32(); long maxId = reader.I64(); int ns = reader.I32(); if (ns < 0 || ns > 1_000_000) throw new InvalidDataException("rhythm cohort ids exceed bound"); long[] sids = new long[ns]; for (int j = 0; j < ns; j++) sids[j] = reader.I64(); CortexPolicyDecision decision = LoadPolicyDecision(reader); cohorts[i] = new(step, maxId, sids, decision); }
        return new(residual, residualRef, yield, lo, hi, pending, choice, dpo, mpo, apo, productivity, magnitude, outcomes, vested, epoch, baseOutcome, fresh, reseeds, daySteps, dreamSteps, aestivationSteps, epsilonFires, boundDays, previous, last, worth, epsilon, window, cohorts);
    }

    /// The rolling dream-fraction over the last WindowSteps decisions — the emergence readout (plot vs residual).
    public double ReplayFrac
    {
        get
        {
            if (_window.Count == 0) return 0;
            int m = 0;
            foreach (var p in _window) if (p == (byte)MetabolicPhases.Replay) m++;
            return (double)m / _window.Count;
        }
    }

    /// The frontier-residual EMA — the feature the dream-fraction must track (the kill-line plot's x-axis).
    public double Residual => _residual;

    /// CHOOSE this step's input source. Priority: a due aestivation is law (the homeostat's cadence, never deferred);
    /// a bound headroom forces Day (the reflection law: reality must catch up before more hypothesis); then the
    /// worth-line, then the ε-bootstrap on would-be-Day steps. Folds the decision token into the rhythm channel
    /// (predict → mint the residual → learn) and telegraphs edges.
    public RhythmChoice Choose(int step, in RhythmSenses s, ulong seed, Cortex? cortex = null)
    {
        if (s.Exhausted && _residual > 0) _residual = 0;   // nothing external remains un-eaten — the edge is consumed (MIX re-admissionPlans are re-ingest, not frontier)

        // FRESH-DOMAIN RE-OPENING → open a bounded fresh-frontier window (the oscillator's RETURN-swing). The
        // reference tracks the residual's low-water mark (falls as the edge is explained); a RISE ≥FreshFrontier above
        // it is a new domain opening the frontier back up. This window is what decouples the dream RE-TRIGGER from the
        // yield history (cogito#6): while it is open the gain FLOORS at 1.0 (below) and the ε anneal re-origins, so a
        // barren-latched machine (yield→0, ε long dead, worth pinned sub-line) RE-IGNITES dreaming on the fresh edge
        // REGARDLESS of past outcomes — the frontier drives, not the stale yield. Outside the window the yield
        // modulates freely and CAN throttle back to day (that down-swing is what DRAINS fresh world → raises residual
        // → re-opens this very window: the loop closes on itself).
        if (_residual > _residualRef + FreshFrontier)
        {
            _freshUntil = step + FreshWindowSteps; _epsEpochStep = step; _epsOutcomeBase = _outcomes; _reseeds++;
            _residualRef = _residual;                      // the new plateau becomes the reference (no repeat-fire on the same rise)
            Cogito.Trace.Cortex.Boundary("rhythm.reseed", $"step={step} fresh frontier: residual {_residualRef:F2} re-opened (+{FreshFrontier:F2} over low-water) — dream re-ignites (gain floor + ε {Eps0:F2}) for {FreshWindowSteps} steps · reseeds={_reseeds}");
        }
        else if (_residual < _residualRef) _residualRef = _residual;   // follow the residual down — the low-water mark only descends between re-openings

        double explained = 1 - _residual;
        double grok = double.IsNaN(s.Cvz) || s.Cvz <= 0 ? 0 : Math.Clamp(Homeostat.CvStar(s.Cvz, s.Kz) / s.Cvz, 0, 1);
        double covSat = Math.Clamp(s.Coverage, 0, 1);
        double calm = 1 - Math.Clamp(s.ExcMint, 0, 1);
        // gain — yield MODULATES, never terminally GATES. Two regimes, and the FRONTIER (not the yield) picks between
        // them: inside a fresh-frontier window the gain FLOORS at 1.0 so the re-opened edge crosses ReplayLine on its
        // OWN whatever the stale yield says (the return-swing); in steady state gain = 0.5 + yield, so a outcomeCredit streak
        // AMPLIFIES (>1) and a barren streak THROTTLES (→0.5, worth dips sub-line → the mind flips to day-drain, the
        // down-swing that opens the next fresh frontier). The first shape was steady-state ONLY (0.5+yield always):
        // once a barren stretch drove yield→0 it HALVED the worth even at a re-opened frontier — a ONE-WAY latch off
        // (worth pinned ~0.31 < ReplayLine at residual 0.28/grok 0.90: cogito#6). The window is the missing up-swing.
        // Neutral 1.0 until the first outcome lands.
        bool fresh = step < _freshUntil;
        double gain = double.IsNaN(_yield) ? 1.0 : fresh ? Math.Max(1.0, 0.5 + _yield) : 0.5 + _yield;
        double worth = gain * explained * (WGrok * grok + WCoverage * covSat + WCalm * calm);

        double eps = Eps(step);
        bool epsFire = false;
        MetabolicPhases phase;
        if (s.ConsolidationPhaseDue) phase = MetabolicPhases.ConsolidationPhase;
        else if (s.ReplayHeadroom <= 0)
        {
            phase = MetabolicPhases.Day;                   // the cap binds - dreams may not outpace evidence available to corroborate them
            if (worth >= ReplayLine) _boundDays++;
        }
        else if (worth >= ReplayLine) phase = MetabolicPhases.Replay;
        else if (eps > 0 && Rng01(seed, step) < eps) { phase = MetabolicPhases.Replay; epsFire = true; _epsFires++; }
        else phase = MetabolicPhases.Day;

        CortexPolicyDecision policyDecision = default;
        if (cortex is not null)
        {
            Span<MetricSample> features = stackalloc MetricSample[8]
            {
                new(new MetricID((ushort)RhythmPolicyMetricIDs.Criticality), NumericValue.FromF64(s.Cvz)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.CriticalitySamples), NumericValue.FromI64(s.Kz)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.Coverage), NumericValue.FromF64(s.Coverage)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.ExperienceMint), NumericValue.FromF64(s.ExcMint)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.Exhausted), NumericValue.FromI64(s.Exhausted ? 1 : 0)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.ReplayHeadroom), NumericValue.FromI64(s.ReplayHeadroom)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.ConsolidationPhaseDue), NumericValue.FromI64(s.ConsolidationPhaseDue ? 1 : 0)),
                new(new MetricID((ushort)RhythmPolicyMetricIDs.FrontierResidual), NumericValue.FromF64(_residual)),
            };
            PolicyCanonicalStateID canonicalState = CanonicalizePolicyState(
                s.ConsolidationPhaseDue,
                s.ReplayHeadroom <= 0,
                worth >= ReplayLine,
                eps > 0,
                fresh,
                epsFire);
            policyDecision = cortex.ChoosePolicyAction(PolicyID, (int)phase, in canonicalState, features);
            phase = (MetabolicPhases)policyDecision.Action;
        }

        _met.Fold(Token(phase));                           // the decision lands in the stream the rhythm grammar induces over
        if (step % MetaInduceEvery == 0) _met.MaybeInduce(force: false);

        _window.Enqueue((byte)phase);
        while (_window.Count > WindowSteps) _window.Dequeue();
        switch (phase)
        {
            case MetabolicPhases.Day: _daySteps++; break;
            case MetabolicPhases.Replay: _dreamSteps++; break;
            case MetabolicPhases.ConsolidationPhase: _aestivationSteps++; break;
        }
        _lastPhase = phase; _lastWorth = worth; _lastEps = eps;

        if (phase != _prev)
            Cogito.Trace.Cortex.Boundary("rhythm", $"step={step} →{Token(phase)} worth={worth:F2} residual={_residual:F2} grok={grok:F2} cov={covSat:F2} calm={calm:F2} gain={gain:F2} eps={eps:F2}{(epsFire ? " · ε-FIRE" : "")}{(s.ReplayHeadroom <= 0 && worth >= ReplayLine ? " · headroom-bound" : "")} · dream_frac={ReplayFrac:F2}");
        else if (step > 0 && step % WindowSteps == 0)
            Cogito.Trace.Cortex.Boundary("rhythm.win", Line(step));
        _prev = phase;
        return new RhythmChoice(phase, in policyDecision, cortex is not null);
    }

    /// Fold one day draw's frontier read: `meanPickCoverage` = the mean grammar-coverage of the spans the pick
    /// just ingested (the curriculum computes it beside the pick — NaN when nothing frontier-scored landed).
    /// residual ← EMA of (1 − coverage): high pick-coverage = the edge is explained = the world teaches little.
    /// THE BATCH ARM's food only (--no-loom): the capped cover basis (top-FrontierCapExps expansions) makes this
    /// read collapse at scale — past a few hundred rules ordinary frontier lines score ~0 coverage and the
    /// residual pins at ~1 (measured). The loom arm reads the SPLICE residual instead (FoldSpliced).
    public void FoldDraw(double meanPickCoverage)
    {
        if (double.IsNaN(meanPickCoverage)) return;
        FoldResidual(1 - Math.Clamp(meanPickCoverage, 0, 1));
    }

    /// Fold a domain's direct frontier observation. This is the semantic-domain counterpart to pick coverage.
    public void FoldResidual(double frontierResidual)
    {
        if (double.IsNaN(frontierResidual)) return;
        double bounded = Math.Clamp(frontierResidual, 0, 1);
        _residual += ResidualDrift * (bounded - _residual);
    }

    long _pendLo = -1, _pendHi = -1;
    bool _pendingDayOutcome;

    /// Mark a Day step's external appends [lo, hi) by TapeEventID — the day path splices them at the NEXT step's
    /// INDUCE, so the fold point (FoldSpliced, before Choose) reads them one step later, fully parsed.
    public void MarkWorldAppends(long lo, long hi, in RhythmChoice choice, bool resolveDay)
    {
        if (hi <= lo) return;
        _pendLo = lo;
        _pendHi = hi;
        _pendingDayChoice = choice;
        _pendingDayOutcome = resolveDay;
    }

    /// Fold the marked spans' SPLICE residual — the loom arm's frontier read: parsedLen/rawLen per REAL span off
    /// the standing loom (the machine's own fold saying how novel the input was — a learned span parses toward
    /// ONE symbol, novelty ~0; a fresh one stays near byte-per-symbol, novelty ~1). Uncapped, exact, O(1) per
    /// span, and provenance-filtered: only Provenances.Real counts (Campfire's Draw also lands EML mints in the
    /// same id range — Reflected/Replay provenance, intrinsic, not the world's edge).
    public void FoldSpliced(Tape tape, Loom loom, Cortex cortex)
    {
        if (_pendLo < 0) return;
        double sum = 0;
        int n = 0;
        for (long id = _pendLo; id < _pendHi; id++)
        {
            if (tape.ProvenanceOf(new TapeEventID(id)) != Provenances.Real) continue;
            int parsed = loom.ParsedLenOf(id);
            if (parsed < 0 || !tape.Resolve(new TapeEventID(id), out var raw) || raw.Length == 0) continue;
            sum += Math.Clamp((double)parsed / raw.Length, 0, 1);
            n++;
        }
        long eventCount = _pendHi - _pendLo;
        _pendLo = _pendHi = -1;
        double productivity = n == 0 ? 0 : sum / n;
        if (n > 0) _residual += ResidualDrift * (productivity - _residual);
        if (_pendingDayOutcome)
            ResolveChoice(cortex, in _pendingDayChoice, productivity, eventCount, invariantClean: n > 0 || eventCount == 0);
        _pendingDayChoice = default;
        _pendingDayOutcome = false;
    }

    /// Open one dream step's cohort — the minted span ids whose vest-verdict resolves once aged past the drop
    /// horizon. A full journal silently declines (bounded memory; the yield EMA keeps tracking whichever fit).
    public void OpenCohort(Cortex cortex, int step, in RhythmChoice choice, List<TapeEventID> minted)
    {
        if (minted.Count == 0)
        {
            ResolveChoice(cortex, in choice, 0, 0, invariantClean: true);
            return;
        }
        if (_cohorts.Count >= MaxPendingCohorts)
        {
            ResolveChoice(cortex, in choice, 0, minted.Count, invariantClean: false);
            return;
        }
        long[] sids = new long[minted.Count];
        long maxId = 0;
        for (int i = 0; i < minted.Count; i++) { sids[i] = minted[i].Value; maxId = Math.Max(maxId, sids[i]); }
        _cohorts.Enqueue(new Cohort(step, maxId, sids, choice.PolicyDecision));
    }

    /// Resolve every cohort aged past the drop horizon: by now each member either VESTED (IsEvidence — reality
    /// corroborated the hypothesis) or dropped as stale. The vest-fraction folds into the yield EMA (the worth's
    /// outcome gain), counts toward the ε anneal, and lands as an OUTCOME token in the rhythm channel — the
    /// dream-decision and its consequence share one predictable stream (the self-reference).
    public void ResolveCohorts(Cortex cortex, Tape tape, int step)
    {
        while (_cohorts.Count > 0 && tape.NextId - _cohorts.Peek().MaxId >= _cohortHorizon)
        {
            Cohort c = _cohorts.Dequeue();
            int vested = 0;
            foreach (long sid in c.Sids) if (tape.IsEvidence(new TapeEventID(sid))) vested++;
            double frac = (double)vested / c.Sids.Length;
            _yield = double.IsNaN(_yield) ? frac : _yield + YieldDrift * (frac - _yield);
            _outcomes++;
            if (vested > 0) _cohortsOutcomeCredited++;
            _met.Fold(vested > 0 ? "v+" : "v0");
            CortexPolicyDecision policyDecision = c.PolicyDecision;
            RhythmChoice choice = new(MetabolicPhases.Replay, in policyDecision, observed: true);
            ResolveChoice(cortex, in choice, frac, vested, invariantClean: true);
            Cogito.Trace.Cortex.Boundary("rhythm.vest", $"step={step} cohort@{c.Step} vested {vested}/{c.Sids.Length} · yield⌂={_yield:F2} outcomes={_outcomes}/{BootOutcomes} eps={Eps(step):F2}");
        }
    }

    public void ResolveDay(Cortex cortex, in RhythmChoice choice, double productivity, long eventCount)
        => ResolveChoice(cortex, in choice, productivity, eventCount, invariantClean: true);

    public void ResolveConsolidationPhase(Cortex cortex, in RhythmChoice choice, in ConsolidationPhaseYield yield)
    {
        long magnitude = checked((long)yield.Evicted + yield.Promoted + yield.Demoted + yield.Slotted + yield.Breached);
        bool productive = magnitude > 0 || yield.BitsSaved > 0;
        ResolveChoice(cortex, in choice, productive ? 1 : 0, magnitude, invariantClean: true);
    }

    private void ResolveChoice(
        Cortex cortex,
        in RhythmChoice choice,
        double productivity,
        double magnitude,
        bool invariantClean)
    {
        if (!choice.Observed) return;
        switch (choice.Phase)
        {
            case MetabolicPhases.Day: _dayPolicyOutcomes++; break;
            case MetabolicPhases.Replay: _dreamPolicyOutcomes++; break;
            case MetabolicPhases.ConsolidationPhase: _aestivationPolicyOutcomes++; break;
        }
        _policyProductivity += productivity;
        _policyMagnitude += magnitude;
        Span<MetricSample> outcomes = stackalloc MetricSample[2]
        {
            new(new MetricID((ushort)RhythmPolicyMetricIDs.Productivity), NumericValue.FromF64(productivity)),
            new(new MetricID((ushort)RhythmPolicyMetricIDs.Magnitude), NumericValue.FromF64(magnitude)),
        };
        CortexPolicyDecision policyDecision = choice.PolicyDecision;
        cortex.ResolvePolicyOutcome(in policyDecision, outcomes, invariantClean, conservedCost: 1);
    }

    // ε anneals to 0 as outcomes accumulate (primary) or the step ceiling passes (the wScale=1 fallback — the
    // outcome plane is dark there: vests never fire, so the bootstrap must still expire). BOTH terms measure from
    // the ε EPOCH, re-originated on each fresh-domain re-opening: a new frontier restarts the bootstrap so
    // exploration re-seeds on the new edge — the anneal is per-EPISODE, not a one-way birth-to-death climb (cogito#6).
    double Eps(int step)
    {
        double left = 1 - Math.Max((double)(_outcomes - _epsOutcomeBase) / BootOutcomes, (double)(step - _epsEpochStep) / BootStepCeil);
        return left <= 0 ? 0 : Eps0 * left;
    }

    // stateless seed+step draw (the Vow: the RNG position IS the step) — SplitMix64 over the pair, top 53 bits.
    // internal: the homeostat's lead-revive floor draws through the SAME primitive (seed offset per lead-class,
    // step = the close counter) — one implementation of the stateless draw, never a second hash to drift.
    internal static double Rng01(ulong seed, int step)
    {
        ulong z = seed ^ (0x9E3779B97F4A7C15UL * (ulong)(step + 1));
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    static string Token(MetabolicPhases p) => p switch { MetabolicPhases.Day => "d", MetabolicPhases.Replay => "m", _ => "a" };

    // ── the sparkline columns (appended to the curve row at the drive's write site, ARMED ONLY — the off arm's
    //    curve.tsv stays byte-identical to the pre-rhythm machine) ──
    public const string HeaderCols = "\tphase\tdream_frac\tresidual\tdream_worth\teps";
    public string RowCols() => $"\t{Token(_lastPhase)}\t{ReplayFrac:F3}\t{_residual:F4}\t{_lastWorth:F3}\t{_lastEps:F3}";

    /// The periodic window telegraph — the emergence readout in one line (dream-fraction beside the features).
    public string Line(int step)
        => $"step={step} dream_frac={ReplayFrac:F2} residual={_residual:F2} worth={_lastWorth:F2} eps={_lastEps:F2} yield⌂={(double.IsNaN(_yield) ? "—" : _yield.ToString("F2"))}"
         + $" · census d{_daySteps}/m{_dreamSteps}/a{_aestivationSteps} ε{_epsFires} bound{_boundDays} reseed{_reseeds} · outcomes {_outcomes} ({_cohortsOutcomeCredited} productive)"
         + $" · self-pred hit {_met.HitRate:P0} mint {_met.MintRate:P0} ({_met.Events} events)";

    /// The land-time report (rhythm.txt) — the phase census, the outcome yield, the ε trail, and the rhythm
    /// channel's self-prediction readout (mint-rate decay + the machine's own metabolic vocabulary).
    public string Report()
    {
        int total = _daySteps + _dreamSteps + _aestivationSteps;
        var sb = new StringBuilder();
        sb.AppendLine("── RHYTHM · emergent metabolism — the machine schedules its own day/dream/aestivation ──");
        if (total == 0) { sb.AppendLine("  (never consulted — the arm was off)"); return sb.ToString(); }
        sb.AppendLine($"  census: day {_daySteps} ({(double)_daySteps / total:P0}) · dream {_dreamSteps} ({(double)_dreamSteps / total:P0}) · aestivation {_aestivationSteps} ({(double)_aestivationSteps / total:P0}) over {total} steps");
        sb.AppendLine($"  bootstrap: {_epsFires} ε-dreams fired · headroom-bound days {_boundDays} · fresh-frontier re-seeds {_reseeds} (the oscillator's return-swings) · final eps {_lastEps:F3}");
        int policyOutcomes = _dayPolicyOutcomes + _dreamPolicyOutcomes + _aestivationPolicyOutcomes;
        sb.AppendLine($"  policy outcomes: {policyOutcomes} resolved (day {_dayPolicyOutcomes} · dream {_dreamPolicyOutcomes} · aestivation {_aestivationPolicyOutcomes}) · productivity Σ{_policyProductivity:F3} · magnitude Σ{_policyMagnitude:F0}");
        sb.AppendLine($"  dream vesting: {_outcomes} cohorts resolved · {_cohortsOutcomeCredited} productive (≥1 vest) · yield⌂ {(double.IsNaN(_yield) ? "— (no cohort matured)" : _yield.ToString("F3"))}");
        sb.AppendLine($"  window: dream_frac {ReplayFrac:F3} · residual⌂ {_residual:F3} · last worth {_lastWorth:F3}");
        sb.AppendLine($"  self-prediction (the rhythm channel): {_met.Events} events · hit {_met.HitRate:P1} · rolling mint {_met.MintRate:P0}");
        sb.AppendLine($"    mint-rate decay: {_met.DecayCurve()}");
        sb.AppendLine("    top rhythm motifs (the machine's metabolic vocabulary):");
        foreach (var m in _met.TopMotifs(6)) sb.AppendLine($"      {m}");
        return sb.ToString();
    }

    // ── CHECKPOINT — the organ whole (rides uniformly, armed or not — the HOME pattern). Field order is
    //    declaration order; the cohort journal in queue order; the channel via MetaGrammar's own encoding. ──
    public void Save(CkptWriter w)
    {
        w.F64(_residual); w.F64(_residualRef); w.F64(_yield);
        w.I64(_pendLo); w.I64(_pendHi); w.Bool(_pendingDayOutcome);
        SaveChoice(w, in _pendingDayChoice);
        w.I32(_dayPolicyOutcomes); w.I32(_dreamPolicyOutcomes); w.I32(_aestivationPolicyOutcomes);
        w.F64(_policyProductivity); w.F64(_policyMagnitude);
        w.I32(_outcomes); w.I32(_cohortsOutcomeCredited);
        w.I32(_epsEpochStep); w.I32(_epsOutcomeBase); w.I32(_freshUntil); w.I32(_reseeds);
        w.I32(_daySteps); w.I32(_dreamSteps); w.I32(_aestivationSteps); w.I32(_epsFires); w.I32(_boundDays);
        w.U8((byte)_prev); w.U8((byte)_lastPhase);
        w.F64(_lastWorth); w.F64(_lastEps);
        w.I32(_window.Count);
        foreach (var p in _window) w.U8(p);
        w.I32(_cohorts.Count);
        foreach (var c in _cohorts)
        {
            w.I32(c.Step); w.I64(c.MaxId);
            w.I32(c.Sids.Length);
            foreach (var sid in c.Sids) w.I64(sid);
            CortexPolicyDecision policyDecision = c.PolicyDecision;
            SavePolicyDecision(w, in policyDecision);
        }
        _met.Save(w);
    }

    public void Load(CkptReader r)
    {
        _residual = r.F64();
        _residualRef = r.F64();
        _yield = r.F64();
        _pendLo = r.I64(); _pendHi = r.I64(); _pendingDayOutcome = r.Bool();
        _pendingDayChoice = LoadChoice(r);
        _dayPolicyOutcomes = r.I32(); _dreamPolicyOutcomes = r.I32(); _aestivationPolicyOutcomes = r.I32();
        _policyProductivity = r.F64(); _policyMagnitude = r.F64();
        _outcomes = r.I32(); _cohortsOutcomeCredited = r.I32();
        _epsEpochStep = r.I32(); _epsOutcomeBase = r.I32(); _freshUntil = r.I32(); _reseeds = r.I32();
        _daySteps = r.I32(); _dreamSteps = r.I32(); _aestivationSteps = r.I32(); _epsFires = r.I32(); _boundDays = r.I32();
        _prev = (MetabolicPhases)r.U8(); _lastPhase = (MetabolicPhases)r.U8();
        _lastWorth = r.F64(); _lastEps = r.F64();
        _window.Clear();
        int nw = r.I32();
        for (int i = 0; i < nw; i++) _window.Enqueue(r.U8());
        _cohorts.Clear();
        int nc = r.I32();
        for (int i = 0; i < nc; i++)
        {
            int cs = r.I32(); long mx = r.I64();
            int ns = r.I32();
            var sids = new long[ns];
            for (int j = 0; j < ns; j++) sids[j] = r.I64();
            CortexPolicyDecision policyDecision = LoadPolicyDecision(r);
            _cohorts.Enqueue(new Cohort(cs, mx, sids, policyDecision));
        }
        _met.Load(r);
    }

    private static void SaveChoice(CkptWriter writer, in RhythmChoice choice)
    {
        writer.U8((byte)choice.Phase);
        writer.Bool(choice.Observed);
        if (choice.Observed)
        {
            CortexPolicyDecision policyDecision = choice.PolicyDecision;
            SavePolicyDecision(writer, in policyDecision);
        }
    }

    private static RhythmChoice LoadChoice(CkptReader reader)
    {
        MetabolicPhases phase = (MetabolicPhases)reader.U8();
        bool observed = reader.Bool();
        CortexPolicyDecision decision = observed ? LoadPolicyDecision(reader) : default;
        return new RhythmChoice(phase, in decision, observed);
    }

    private static void SavePolicyDecision(CkptWriter writer, in CortexPolicyDecision decision)
        => CortexPolicyDecisionCheckpoint.Write(writer, in decision);

    private static CortexPolicyDecision LoadPolicyDecision(CkptReader reader)
        => CortexPolicyDecisionCheckpoint.Read(reader, PolicyID, PolicySchema.ActionCount);
}
