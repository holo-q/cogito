namespace Cogito;

// ── THE MESH HOMEOSTAT (proprioceptive dream-throttle — BOREDOM, not MIND-BREAK) ──  the negative-feedback loop the
// witnessed mesh (Mesh.Drive) never had. The TRUNK regulates itself (Homeostat.cs: senses cvz/cost/collapse,
// actuates the sleep-plane); the MESH dreamed OPEN-LOOP — post-drain it minted MintSpansPerStep every step FOREVER
// regardless of state, so when novelty ran out it kept flooding dreams at full intensity and over-fit its own dream
// structure into the sealed criticality sink (measured: triangle_0047 meanz −0.80→−1.20 by 4000 steps, PAST the
// −1.11 sink; dreams_n0 climbs perfectly linearly ~4/step, dreams_peer ~8/step — a fixed mint rate, nothing reading
// its own criticality). This organ is the missing proprioception: the mesh READS its own criticality (meanz vs the
// −0.70 basin, the honest RG axis — cvz DE-groks under the flood, so meanz is the sense that stays honest) and
// MODULATES its dream MINT RATE to HOLD the basin.
//
// THE DIRECTOR'S REFRAME — when novelty runs out the mind should experience BOREDOM (a stable low-activity REST state,
// criticality HELD, waiting for new input), NOT MIND-BREAK (the collapse — a mind manic on its own dreams because
// nothing tells it to stop). Boredom is HEALTHY; the −1.2 sink is the pathology. So the control law is a directional
// negative feedback on the ONE honest axis:
//
//   err = meanz − Basin          // >0 : meanz sits ABOVE basin (the −0.50 side) — headroom, the up-swing → dream freely
//                                // <0 : meanz DROPPED BELOW basin (toward the −1.11 sink) — the DANGER → throttle down
//
//   throttle ∈ [Floor, 1]  scales MintSpansPerStep. Integral (relax, no ratchet):
//     • meanz at/above basin, not sinking       → throttle relaxes → 1 (dream freely — combust, the up-swing)
//     • meanz below basin OR meanzDrift sinking  → throttle DOWN each step (down-regulate — the mind RESTS)
//     • meanz recovers in-band + drift flat      → throttle relaxes back up (RE-IGNITE readiness — the wake up-swing)
//
// BOREDOM is the emergent settled state: novelty exhausted (post-drain) + meanz holding basin ⟹ throttle settles low,
// mint minimal, tape bounded (the night's shed/drop keeps pace with a minimal mint), criticality held, waiting. It is
// MINIMAL activity, never ZERO — the Floor is the anti-dark-room guard (a throttle that could hit 0 = "stop learning"
// = the dark room; a bored mind idles, it does not die, and it must keep a trickle so a fresh input can still be
// witnessed and RE-IGNITE it). WAKE is the same loop run forward: a fresh-input pulse (a genuinely novel MIX span, or
// a new corpus) lifts meanz back toward/above basin ⟹ err climbs ⟹ throttle relaxes ⟹ mint re-ignites.
//
// THE VOW: control on the honest deterministic read (meanz off the shared reality's grammar), integer-only mint
// clamp downstream, whole state checkpointed — kill→resume byte-exact, off-arm byte-identical to the pre-organ mesh.

/// The proprioceptive dream-throttle. Reads the mesh's own criticality (meanz + its drift, off the shared reality's
/// row) each step and holds a `Throttle` ∈ [Floor, 1] that scales the per-node mint rate. Constructed ALWAYS (its
/// state rides the mesh checkpoint uniformly, the HOME pattern — like Reads/Rhythm); CONSULTED only when armed.
public sealed class MeshHomeostat
{
    // ── the honest meanz axis (Scoreboard's −0.70 critical class — ONE owner for the band, no drift) ──
    public const double Basin = -0.70;        // the RG attractor the witnessed mesh must hold (Scoreboard: mid of [−0.95,−0.50])
    public const double BandLo = -0.95;       // the in-band floor — below this, meanz is sliding toward the −1.11 sink (the DANGER edge)
    public const double BandHi = -0.50;       // the in-band ceiling — above this, meanz is too random/undertrained (the up-swing side)

    readonly double _floor;                   // the boredom floor — the minimal mint fraction a rested mind still dreams at (anti-dark-room; >0 so a fresh input can re-ignite)
    readonly double _gain;                    // integral gain per step — how fast the throttle chases its target (small: a smooth regulator, not a bang-bang)
    readonly double _driftEps;                // the drift dead-band — |meanzDrift| below this reads as FLAT (not sinking), so estimator noise can't force a throttle-down

    double _throttle = 1.0;                    // the live mint-rate scale ∈ [Floor,1] — 1 at birth (dream freely until the mind has a criticality to defend)
    double _selectionGain;                     // the actor-coupling strength ∈ [0,1], regulated by criticality + calibration (no CLI gain knob)
    double _meanzEma = double.NaN;             // EMA of meanz (the honest axis, smoothed — a single noisy read must not swing the throttle)
    double _driftEma;                          // EMA of meanzDrift (the onset alarm, smoothed)
    readonly double _alpha;                    // the sense EMA weight (≈ 1/periods) — the dead-time horizon (the throttle can't chase faster than it measures)
    int _lastKZ = -1;                           // last KZ sample count; a falling KZ is a criticality degradation
    double _lastCalibrationAbs = double.NaN;    // last |calibration error|; a rising error retreats actor coupling

    // the readouts plane (the "is it regulating" telegraph + land-report) — pure counters, no control role
    int _stepsThrottled, _stepsRelaxed, _stepsBored;   // steps the loop pushed the throttle DOWN · relaxed UP · sat at the floor (boredom)
    int _selectionUp, _selectionDown;                   // actor-coupling homeostat readouts
    double _minThrottle = 1.0;                          // the deepest the throttle ever went (the rest-depth readout)

    public MeshHomeostat(double floor = 0.05, double gain = 0.30, int periods = 4, double driftEps = 1e-4)
    {
        _floor = Math.Clamp(floor, 0.0, 1.0); _gain = gain; _alpha = 1.0 / Math.Max(1, periods); _driftEps = driftEps;
    }

    /// Is the mind at rest (throttle sitting at/near the boredom floor)? The boredom telegraph (read by the trace
    /// line + land report) — a fresh-input wake lifts meanz, the danger falls, and the throttle relaxes off this floor
    /// (the re-ignition).
    public bool Bored => _throttle <= _floor + 1e-9;
    public double SelectionGain => _selectionGain;

    /// SENSE + ACTUATE — one step of the proprioceptive loop, called from the mesh's READ block with the shared
    /// reality's just-measured `meanz` (the honest RG axis off the shared grammar) and `meanzDrift` (its onset slope).
    /// EMAs both senses (dead-time safety), then relaxes the throttle one gain-step toward the target the error
    /// dictates: meanz above basin + not sinking → target 1 (dream freely); meanz below basin OR drift sinking →
    /// target Floor (rest). Deterministic; NaN reads (a too-shallow grammar has no meanz) HOLD the throttle (no
    /// criticality to defend yet — the boot regime dreams freely). Returns the new throttle.
    public double Sense(double meanz, double meanzDrift)
    {
        if (double.IsNaN(meanz))
        {
            // no criticality read yet (shallow grammar) — nothing to regulate; hold. The throttle stays wherever it
            // settled (1.0 at boot), so the early combustion dreams at full rate exactly as the open-loop mesh did.
            return _throttle;
        }
        _meanzEma = double.IsNaN(_meanzEma) ? meanz : _meanzEma + _alpha * (meanz - _meanzEma);
        _driftEma += _alpha * (meanzDrift - _driftEma);

        // THE ERROR → THE TARGET.  The collapse is PREVENTIVE-only: once the vested-dream mass out-masses real, meanz
        // sits in the sink and no mint-throttle can restore it (the throttle governs NEW mint, not the accreted mass) —
        // the recovery force is the MIX real feed, which is slow. So the throttle must clamp HARD and EARLY, before the
        // mass forms, keeping dream from ever out-massing real. Two dangers, either forces rest:
        //   (1) LEVEL — meanz DROPPED below the basin. NOT linear-to-BandLo (that let meanz leak halfway before the
        //       throttle bottomed): a SHARP pull that saturates at ~⅓ of the way to the sink edge, so a small dip past
        //       the basin already slams the throttle toward the floor (prevent, don't chase). Above basin → 0 danger.
        //   (2) SINK — meanz is DRIFTING DOWN (drift below −driftEps past the dead-band): the ONSET alarm, fires BEFORE
        //       the level crosses (meanz_drift shows the −0.78→−0.66 dilution long before two raw windows make it
        //       legible — Reads ). An active sink forces rest even while the level is still nominally at basin.
        // Above basin with a flat/rising drift → target 1: dream freely (the up-swing / the wake). MAX-combined (the
        // worse governs), then target interpolates Floor (full danger) ↔ 1 (none).
        const double LevelSaturate = 0.33;   // meanz this fraction of the way basin→BandLo already saturates the level danger — the SHARP preventive pull (a shallow dip = full clamp)
        double belowBasin = Math.Max(0.0, Basin - _meanzEma);                                   // how far meanz sank past the basin (0 above it)
        double levelDanger = Math.Clamp(belowBasin / (LevelSaturate * (Basin - BandLo)), 0.0, 1.0);   // saturates at ⅓ the way to the sink edge — clamp EARLY
        double sinkDanger  = _driftEma < -_driftEps ? Math.Clamp(-_driftEma / (10 * _driftEps), 0.0, 1.0) : 0.0;   // the onset alarm, saturating a decade past the dead-band
        double danger = Math.Max(levelDanger, sinkDanger);
        double target = _floor + (1.0 - _floor) * (1.0 - danger);   // danger 0 → 1 (dream freely) · danger 1 → Floor (rest)

        double before = _throttle;
        _throttle += _gain * (target - _throttle);                  // integral relax — no ratchet, both directions
        _throttle = Math.Clamp(_throttle, _floor, 1.0);

        if (_throttle < before - 1e-9) _stepsThrottled++;
        else if (_throttle > before + 1e-9) _stepsRelaxed++;
        if (Bored) _stepsBored++;
        if (_throttle < _minThrottle) _minThrottle = _throttle;
        return _throttle;
    }

    /// ACTOR COUPLING — the second setpoint in the same homeostat organ. Coupling strength rises only while the
    /// criticality read stays in the basin (MeanZ in-band, KZ not degrading) AND the calibration meter is not getting
    /// worse. Either meter degrading retreats the gain. Step size is derived from KZ: a shallow/noisy criticality read
    /// moves slowly; a well-populated read can regulate faster.
    public double SenseSelection(double meanz, int kz, bool calibrationReady, double calibrationError)
    {
        double target = 0.0;
        if (!double.IsNaN(meanz) && kz > 0 && calibrationReady)
        {
            bool criticalityHolds = meanz >= BandLo && meanz <= BandHi && (_lastKZ < 0 || kz >= _lastKZ);
            double calAbs = Math.Abs(calibrationError);
            bool calibrationHolds = double.IsNaN(_lastCalibrationAbs) || calAbs <= _lastCalibrationAbs;
            if (criticalityHolds && calibrationHolds) target = 1.0;
            _lastCalibrationAbs = calAbs;
            _lastKZ = kz;
        }

        double before = _selectionGain;
        double step = 1.0 / (1 + Math.Max(1, kz));
        _selectionGain += (target - _selectionGain) * step;
        _selectionGain = Math.Clamp(_selectionGain, 0.0, 1.0);
        if (_selectionGain > before + 1e-9) _selectionUp++;
        else if (_selectionGain < before - 1e-9) _selectionDown++;
        return _selectionGain;
    }

    /// Apply the throttle to a raw mint-span budget — scale by the throttle, rounding to the nearest span. At deep
    /// rest this reaches ZERO mint, and THAT IS CORRECT: the anti-dark-room guarantee is NOT a per-mint span trickle
    /// (a floor of 1 span × N nodes × every post-drain step re-floods the tape and DEFEATS the throttle — measured:
    /// dreams still climbed ~1/step/node at throttle 0.02, meanz still leaked to −1.10), it is that the MIX RAIL keeps
    /// re-ingesting REAL every MixEvery steps — novelty never dies while the world re-arrives, so the mint can safely
    /// rest at 0 and the real feed pulls meanz back toward the basin (the recovery force). A bored mind that mints
    /// nothing still WAKES: fresh real lifts meanz → the throttle relaxes off the floor → mint re-opens. `_floor`
    /// bounds the THROTTLE (so a non-zero floor keeps a proportional trickle when the operator wants one), but the
    /// rounding lets a low-enough throttle × small raw reach 0 — the true rest.
    public int Apply(int rawMintSpans)
    {
        if (rawMintSpans <= 0) return 0;
        return (int)Math.Round(_throttle * rawMintSpans);
    }

    /// The per-step telegraph (trace-only) — the throttle, the smoothed senses, the regime.
    public string Line()
        => $"throttle={_throttle:F3}{(Bored ? " BORED" : "")} · meanz⌂={(double.IsNaN(_meanzEma) ? "—" : _meanzEma.ToString("F3"))} drift⌂={_driftEma:F5}"
         + $" · min={_minThrottle:F3} · steps throttled {_stepsThrottled} relaxed {_stepsRelaxed} bored {_stepsBored}"
         + $" · actor-gain={_selectionGain:F3} up {_selectionUp} down {_selectionDown}";

    /// The land-time report (mesh-homeostat.txt, armed arm only) — did the proprioception hold the basin?
    public string Report()
        => "── MESH HOMEOSTAT · the proprioceptive dream-throttle (boredom, not mind-break) ──\n"
         + $"  throttle now {_throttle:F3}{(Bored ? " (BORED — resting at the floor)" : "")} · deepest rest {_minThrottle:F3} · floor {_floor:F3}\n"
         + $"  meanz⌂ {(double.IsNaN(_meanzEma) ? "—" : _meanzEma.ToString("F3"))} (basin {Basin:F2}, in-band [{BandLo:F2},{BandHi:F2}]) · drift⌂ {_driftEma:F5}\n"
         + $"  regulation: {_stepsThrottled} steps down-regulated · {_stepsRelaxed} relaxed up · {_stepsBored} at the boredom floor\n"
         + $"  actor coupling: gain {_selectionGain:F3} · up {_selectionUp} · down {_selectionDown} · KZ {_lastKZ} · |calerr| {(double.IsNaN(_lastCalibrationAbs) ? "—" : _lastCalibrationAbs.ToString("F3"))}\n";

    // ── CHECKPOINT — the organ whole (rides uniformly, armed or not — the HOME pattern; field order = declaration
    //    order). floor/gain/alpha/driftEps are ctor inputs rebuilt from config, never state. ──
    public void Save(CkptWriter w)
    {
        w.F64(_throttle); w.F64(_selectionGain); w.F64(_meanzEma); w.F64(_driftEma);
        w.I32(_lastKZ); w.F64(_lastCalibrationAbs);
        w.I32(_stepsThrottled); w.I32(_stepsRelaxed); w.I32(_stepsBored); w.I32(_selectionUp); w.I32(_selectionDown); w.F64(_minThrottle);
    }

    public void Load(CkptReader r)
    {
        _throttle = r.F64(); _selectionGain = r.F64(); _meanzEma = r.F64(); _driftEma = r.F64();
        _lastKZ = r.I32(); _lastCalibrationAbs = r.F64();
        _stepsThrottled = r.I32(); _stepsRelaxed = r.I32(); _stepsBored = r.I32(); _selectionUp = r.I32(); _selectionDown = r.I32(); _minThrottle = r.F64();
    }
}
