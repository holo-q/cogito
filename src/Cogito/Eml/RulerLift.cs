namespace Cogito;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE LIFT OPERATOR — the homeostat's ruler actuator
//
//  "Self-perpetuation = the LIFT operator — and today NO organ lifts." This is that organ. The box-completion
//  theorem: fix (σ, K, probes) + full support → vesting is monotone accretion on a finite lattice → a box
//  has a finite-time TRUE fixed point (novelty→0, census complete) — and the TOE is the COLIMIT over box-closures
//  under ruler-lifts, each lift re-opening the frontier and re-zeroing novelty. The controller below watches the
//  ONE signal that can be Goodharted by NOTHING the generator does cheaply — the new EXACT-certificate rate
// —
//  and lifts the K-ruler when the box completes:
//    BOX-COMPLETE  =  the E-CLASS rate falls to a fraction of the box's OWN peak rate-home (novelty→0 at
//                     practical scale — an absolute cut would never fire on a fat box, the HomeBand law),
//                     sustained ≥ Sustain windows (the homeostat's streak hysteresis),
//                  ∧  the exact tier is GROKKED — the −0.70/cvz lock on the E-tier corpus (the RG attractor
//                      measured; KAwareLock is the one shared lock authority). Lazy: the E-tier induce runs
//                     only once the census leg is already sustained (the AND short-circuits the expensive stage).
//  WHY THE E-TIER CLOCK, not the whole E+A census — measured composition, not taste (run 0052, per 50 steps:
//  newE 0–24 sparse vs newA 50–70 SUSTAINED through the run's end; the lift flagship confirmed the sum flat at
//  ~46/window through step 2500 with zero decay): A-families are absorption-noise geometry — every deep tower
//  minus every small term keys a fresh (FamilySig, rate-band) class, a font that scales with program space
//  itself and so can NEVER plateau at practical budget. The box the RULER seals is the EXACT-reachable value
//  lattice (: "VESTING is monotone accretion on a finite lattice → census complete" — vested ⟺ Witnessed ⟺
//  E-tier). The E+A census still rides the tally as the report's frontier read; it just isn't the completion bell.
//  THE HEALTH INVARIANT, checkable: novelty CONSERVED across lifts (the rate must RE-IGNITE in the fresh
//  box — RateAfter is patched into each LiftEvent as the post-lift windows close), census CLOSED behind them
//  (the K-ruler lives entirely in the SAMPLER — sieve/grader/CAS are ruler-free, so a lift cannot re-key one
//  vested certificate; ReplayCalc asserts the census across the lift instant, fail-loud).
//  THE σ-AXIS (secondary, witness-axis): the margin-mass sense — the share of new A-certificates in the
//  SUB-RESOLUTION rate-band (drift proven ≠ 0 by the enclosure yet below the σ-ruler: mass piled ON the
//  resolution line) — is watched and reported. Its ACTUATOR is structurally pinned at mount: MountSig = 9 IS
//  Eml.Q's collision-free packing bound (sig ≤ 9 keeps |mantissa| < 10¹⁰), so the σ-ruler is born at its
//  medium's ceiling — lifting it requires widening the packed-key medium first (a follow-up organ), and an
//  in-place σ re-key would forge census novelty (re-keying vested E-limits), breaking the closure invariant.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The lift organ's knob set — one shape from CLI to controller. `KMax` = the ruler ceiling (0 DISARMS the organ
/// — the Vow arm: rulers pinned, byte-identical); `Factor` = geometric ruler growth per lift; `Window` = steps per
/// census window (the controller's close cadence); `Sustain` = consecutive plateau windows before the gate opens
/// (LockRounds' analog); `Frac` = the plateau line as a fraction of the box's own peak rate-home; `MeanzBand` =
/// |meanz+0.70| tolerance of the exact-tier lock; `LockMeanz` gates on the meanz-band ALONE — cvz measured and
/// telegraphed, not gating (measured law: at a REAL census plateau the E-tier sits ON the attractor, meanz
/// −0.78..−0.84, while KAwareLock's cvz bar — calibrated for whole-tape grammars — reads 0.47..0.59 at k11 and
/// starves the gate; dreamlift_0002 step 6149–6799, seven sustained plateau windows, all "plateau·unlocked");
/// `CensusOnly` drops the grok leg entirely (the harder ablation arm).
internal readonly record struct LiftKnobs(int KMax, double Factor, int Window, int Sustain, double Frac, double MeanzBand, bool CensusOnly, bool LockMeanz = false)
{
    public bool Armed => KMax > 0;
    public static LiftKnobs Off => default;
}

/// One fired lift — the tower-walk trajectory row. `RateBefore` = the plateau window's E-class rate (the →0
/// readout) and `Line` the completion line that fired (Frac × the box's peak E-rate-home); `RateAfter1`/
/// `RateAfter3` = the fresh box's first-window / first-three-window mean E-rate (the re-ignition readout, patched
/// in as those windows close); `Reignited` = the fresh box's rate re-exceeds the very line that declared the old
/// box complete (RateAfter3 > max(1/win, Line)) — novelty demonstrably re-opened above the completion verdict.
/// `CensusE`/`Census`/`Bench` = the E-class count, E+A census, and named-bench at the lift instant (census-
/// closure's anchor: the post-lift census may only grow past it).
public record struct LiftEvent(int Step, int FromK, int ToK, int CensusE, int Census, int Bench,
                               double RateBefore, double Line, double RateAfter1, double RateAfter3, bool Reignited);

/// The per-window sense row the lift controller reads + the verdict it returns — the observatory's liftwins.tsv
/// spine and the trace telegraph's payload. `RateE` is the completion clock (new EXACT classes); `RateEA` the
/// whole-census contrast column (the A-font rides it). Grok fields are NaN on windows where the lazy stage never ran.
public readonly record struct LiftWindow(int Step, int Ruler, int RateE, int RateEA, double RateHome, double PeakHome,
                                         int Streak, double MeanzE, double CvzE, int KzE, double SubResShare,
                                         bool SigmaDue, string Verdict);

public enum RulerLiftActions : byte { Hold, Lift }

internal enum RulerLiftMetricIDs : ushort
{
    RateE = 700,
    RateEA,
    RateHome,
    PeakHome,
    Streak,
    Refractory,
    Ruler,
    Ceiling,
    MeanZE,
    CvZE,
    KzE,
    SubResolutionShare,
    SigmaDue,
    ProposedRuler,
    ExactDelta = 720,
    TheoremDelta,
    BenchDelta,
    FrontierOpen,
    EvaluatorCalls,
}

internal readonly record struct RulerLiftProposal(
    int Step,
    int EClasses,
    int Census,
    int Bench,
    int SubResolutionClasses,
    int RateE,
    int RateEA,
    double RateHome,
    double PeakHome,
    int Streak,
    int Refractory,
    int Ruler,
    bool Ceiling,
    double MeanZE,
    double CvZE,
    int KzE,
    double SubResolutionShare,
    bool SigmaDue,
    int ProposedRuler,
    bool LaunchpadLift,
    string LaunchpadVerdict,
    double CompletionLine,
    HomeBand NextRate,
    int NextSigmaDue);

internal readonly record struct RulerLiftChoice(
    RulerLiftProposal Proposal,
    CortexPolicyDecision PolicyDecision,
    RulerLiftActions Action,
    bool Observed,
    long EvaluatorStart);

internal readonly record struct RulerLiftPendingPolicyOutcomeReceipt(
    CortexPolicyDecisionID DecisionID,
    CortexPolicyDecisionReadout Readout,
    RulerLiftActions Action,
    int ExactBefore,
    int TheoremBefore,
    int BenchBefore,
    long EvaluatorBefore,
    int WindowsRemaining,
    double CompletionLine);

/// The box-completion controller — the homeostat organ that lifts the K-ruler (the header block's law). Owns the
/// DECISION only: the mount (ReplayCalc) feeds it census reads per draw, supplies the lazy exact-tier senses at
/// window closes, and actuates the sampler when a lift fires. Checkpoints whole (armed mounts serialize it behind
/// a fail-loud section tag).
internal sealed class RulerLift
{
    public static CortexPolicyID PolicyID { get; } = CortexPolicyID.Parse("eml.ruler-lift");
    public static CortexPolicySchema PolicySchema { get; } = new(
        PolicyID,
        featureCount: 14,
        actionCount: 2,
        outcomeCount: 5,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);

    private readonly record struct PendingPolicyOutcome(
        CortexPolicyDecision Decision,
        RulerLiftActions Action,
        int ExactBefore,
        int TheoremBefore,
        int BenchBefore,
        long EvaluatorBefore,
        int WindowsRemaining,
        double CompletionLine);

    readonly LiftKnobs _k;
    int _completedWindows;
    int _lastE, _lastEA, _lastSubRes; // last close's cumulative E-class / E+A census / sub-resolution counts (the Δ anchors)
    HomeBand _rate = new();           // the E-rate home — REBORN on lift
    double _peak;                     // the box's peak rate-home (the plateau line's denominator)
    int _streak;                      // consecutive plateau windows (hysteresis)
    int _refractory;                  // windows to hold after a lift (the fresh box's establishment warmup)
    int _ruler;                       // the live K-ruler (the controller's record copy; the sampler holds the working one)
    bool _ceiling;                    // KMax reached — the walk's last box (no further lifts; the run's natural end)
    int _sigmaDue;                    // σ-alarm census: windows where margin-mass piled on the resolution line
    readonly List<LiftEvent> _lifts = new();
    readonly List<LiftWindow> _wins = new();
    int _patch1 = -1, _patch3 = -1;   // lift index awaiting its RateAfter1 / RateAfter3 patch; _patchSum/_patchN fold the 3-window mean
    double _patchSum; int _patchN;
    readonly List<PendingPolicyOutcome> _pendingPolicyOutcomes = new();

    public RulerLift(in LiftKnobs k, int ruler) { _k = k; _ruler = ruler; }

    public LiftKnobs Knobs => _k;
    public int Ruler => _ruler;
    public int CompletedWindows => _completedWindows;
    public bool AtCeiling => _ceiling;
    public int SigmaDue => _sigmaDue;
    public IReadOnlyList<LiftEvent> Lifts => _lifts;
    public IReadOnlyList<LiftWindow> Windows => _wins;
    public bool HasPendingLiftOutcome
    {
        get
        {
            for (int i = 0; i < _pendingPolicyOutcomes.Count; i++)
                if (_pendingPolicyOutcomes[i].Action == RulerLiftActions.Lift) return true;
            return false;
        }
    }

    public bool HasPendingPolicyOutcome => _pendingPolicyOutcomes.Count != 0;

    internal RulerLiftPendingPolicyOutcomeReceipt[] ReadPendingPolicyOutcomes()
    {
        RulerLiftPendingPolicyOutcomeReceipt[] receipts = new RulerLiftPendingPolicyOutcomeReceipt[_pendingPolicyOutcomes.Count];
        for (int i = 0; i < _pendingPolicyOutcomes.Count; i++)
        {
            PendingPolicyOutcome pending = _pendingPolicyOutcomes[i];
            receipts[i] = new(
                pending.Decision.DecisionID,
                pending.Decision.Readout,
                pending.Action,
                pending.ExactBefore,
                pending.TheoremBefore,
                pending.BenchBefore,
                pending.EvaluatorBefore,
                pending.WindowsRemaining,
                pending.CompletionLine);
        }
        return receipts;
    }

    public bool IsWindowDue(int completedStep)
        => completedStep > 0 && completedStep % Math.Max(1, _k.Window) == 0;

    /// Sense one census window and propose the launchpad action without changing the ruler. `eClasses` =
    /// cumulative EXACT certificate-classes (the vested tier); `census` = cumulative theorem-classes (E+A, the
    /// report's frontier contrast); `subRes` = cumulative sub-resolution A-classes (margin-mass on the σ-line);
    /// `grok` = the LAZY exact-tier renorm read, invoked only once the census leg is sustained (the expensive
    /// tier rides the AND's short-circuit).
    public RulerLiftProposal SenseWindow(int step, int eClasses, int census, int bench, int subRes, Func<(double MeanZ, double CvZ, int Kz)> grok)
    {
        int rate = eClasses - _lastE;
        int rateEA = census - _lastEA;
        int dSub = subRes - _lastSubRes;
        HomeBand nextRate = _rate.Copy();
        nextRate.Observe(rate);
        double nextPeak = Math.Max(_peak, nextRate.Mu);
        double line = _k.Frac * nextPeak;
        bool plateau = _refractory == 0 && nextPeak > 0 && rate <= line;
        int nextStreak = plateau ? _streak + 1 : 0;
        int nextRefractory = Math.Max(0, _refractory - 1);

        // the σ-axis margin-mass alarm (report-only actuator — the header's pinned-medium readout); shares are
        // over the whole census window (A-shrapnel included — margin-mass is an A-tier phenomenon by nature)
        double subShare = rateEA > 0 ? (double)dSub / rateEA : 0;
        bool sigmaDue = rateEA >= 8 && subShare >= 0.5;
        int nextSigmaDue = _sigmaDue + (sigmaDue ? 1 : 0);

        // the grok stage — lazy TWICE over: only a sustained census plateau pays for the exact-tier induce, and
        // the census-only ablation arm never pays it at all (the AND's short-circuit, honored in compute).
        double mz = double.NaN, cvz = double.NaN; int kz = 0;
        string verdict;
        int toK = Math.Min(_k.KMax, (int)Math.Ceiling(_ruler * _k.Factor));
        bool launchpadLift = false;
        bool nextCeiling = _ceiling;
        if (nextCeiling) verdict = "ceiling";
        else if (nextStreak < _k.Sustain) verdict = nextStreak > 0 ? $"plateau {nextStreak}/{_k.Sustain}" : nextRefractory > 0 ? "reopening" : "climbing";
        else
        {
            bool locked = _k.CensusOnly;
            if (!locked)
            {
                (mz, cvz, kz) = grok();
                locked = Math.Abs(mz + 0.70) <= _k.MeanzBand && (_k.LockMeanz || Homeostat.KAwareLock(cvz, kz));
            }
            if (!locked) verdict = "plateau·unlocked";
            else
            {
                if (toK <= _ruler) { nextCeiling = true; toK = 0; verdict = "ceiling"; }
                else
                {
                    verdict = $"LIFT {_ruler}→{toK}";
                    launchpadLift = true;
                }
            }
        }
        return new RulerLiftProposal(step, eClasses, census, bench, subRes, rate, rateEA, nextRate.Mu, nextPeak,
            nextStreak, nextRefractory, _ruler, nextCeiling, mz, cvz, kz, subShare, sigmaDue, toK,
            launchpadLift, verdict, line, nextRate, nextSigmaDue);
    }

    public RulerLiftChoice Choose(Cortex cortex, in RulerLiftProposal proposal, long evaluatorCalls)
    {
        Span<MetricSample> features = stackalloc MetricSample[14]
        {
            new(new MetricID((ushort)RulerLiftMetricIDs.RateE), NumericValue.FromI64(proposal.RateE)),
            new(new MetricID((ushort)RulerLiftMetricIDs.RateEA), NumericValue.FromI64(proposal.RateEA)),
            new(new MetricID((ushort)RulerLiftMetricIDs.RateHome), NumericValue.FromF64(proposal.RateHome)),
            new(new MetricID((ushort)RulerLiftMetricIDs.PeakHome), NumericValue.FromF64(proposal.PeakHome)),
            new(new MetricID((ushort)RulerLiftMetricIDs.Streak), NumericValue.FromI64(proposal.Streak)),
            new(new MetricID((ushort)RulerLiftMetricIDs.Refractory), NumericValue.FromI64(proposal.Refractory)),
            new(new MetricID((ushort)RulerLiftMetricIDs.Ruler), NumericValue.FromI64(proposal.Ruler)),
            new(new MetricID((ushort)RulerLiftMetricIDs.Ceiling), NumericValue.FromI64(proposal.Ceiling ? 1 : 0)),
            new(new MetricID((ushort)RulerLiftMetricIDs.MeanZE), NumericValue.FromF64(proposal.MeanZE)),
            new(new MetricID((ushort)RulerLiftMetricIDs.CvZE), NumericValue.FromF64(proposal.CvZE)),
            new(new MetricID((ushort)RulerLiftMetricIDs.KzE), NumericValue.FromI64(proposal.KzE)),
            new(new MetricID((ushort)RulerLiftMetricIDs.SubResolutionShare), NumericValue.FromF64(proposal.SubResolutionShare)),
            new(new MetricID((ushort)RulerLiftMetricIDs.SigmaDue), NumericValue.FromI64(proposal.SigmaDue ? 1 : 0)),
            new(new MetricID((ushort)RulerLiftMetricIDs.ProposedRuler), NumericValue.FromI64(proposal.ProposedRuler)),
        };
        CortexPolicyDecision decision = cortex.ChoosePolicyAction(
            PolicyID,
            proposal.LaunchpadLift ? (int)RulerLiftActions.Lift : (int)RulerLiftActions.Hold,
            features);
        return new RulerLiftChoice(proposal, decision, (RulerLiftActions)decision.Action,
            Observed: true,
            EvaluatorStart: evaluatorCalls);
    }

    public int Commit(in RulerLiftChoice choice)
    {
        RulerLiftProposal proposal = choice.Proposal;
        int resolvedRuler = ResolveRuler(in choice);
        bool lift = resolvedRuler > 0;
        string verdict = lift ? $"LIFT {proposal.Ruler}→{proposal.ProposedRuler}"
            : choice.Action == RulerLiftActions.Lift ? "lift·invalid"
            : proposal.LaunchpadLift ? "hold·policy" : proposal.LaunchpadVerdict;
        PatchReignition(proposal.RateE);
        _completedWindows++;
        _lastE = proposal.EClasses;
        _lastEA = proposal.Census;
        _lastSubRes = proposal.SubResolutionClasses;
        _rate = proposal.NextRate;
        _peak = proposal.PeakHome;
        _streak = proposal.Streak;
        _refractory = proposal.Refractory;
        _ceiling = proposal.Ceiling;
        _sigmaDue = proposal.NextSigmaDue;
        int toK = 0;
        if (lift)
        {
            toK = resolvedRuler;
            _lifts.Add(new LiftEvent(proposal.Step, proposal.Ruler, toK, proposal.EClasses, proposal.Census,
                proposal.Bench, proposal.RateE, proposal.CompletionLine, double.NaN, double.NaN, false));
            _patch1 = _patch3 = _lifts.Count - 1;
            _patchSum = 0;
            _patchN = 0;
            _ruler = toK;
            _rate = new HomeBand();
            _peak = 0;
            _streak = 0;
            _refractory = _k.Sustain + 1;
        }
        _wins.Add(new LiftWindow(proposal.Step, _ruler, proposal.RateE, proposal.RateEA, _rate.Mu, _peak,
            _streak, proposal.MeanZE, proposal.CvZE, proposal.KzE, proposal.SubResolutionShare,
            proposal.SigmaDue, verdict));
        if (choice.Observed)
        {
            _pendingPolicyOutcomes.Add(new PendingPolicyOutcome(choice.PolicyDecision, choice.Action,
                proposal.EClasses, proposal.Census, proposal.Bench, choice.EvaluatorStart,
                lift ? 3 : 1, proposal.CompletionLine));
        }
        return toK;
    }

    public static int ResolveRuler(in RulerLiftChoice choice)
        => choice.Action == RulerLiftActions.Lift
            && choice.Proposal.LaunchpadLift
            && choice.Proposal.ProposedRuler > choice.Proposal.Ruler
            && !choice.Proposal.Ceiling
                ? choice.Proposal.ProposedRuler
                : 0;

    public static RulerLiftChoice CreateLaunchpadChoice(in RulerLiftProposal proposal)
    {
        CortexPolicyDecision decision = default;
        return new RulerLiftChoice(proposal, decision,
            proposal.LaunchpadLift ? RulerLiftActions.Lift : RulerLiftActions.Hold,
            Observed: false,
            EvaluatorStart: 0);
    }

    public void AdvancePolicyOutcomes(Cortex cortex, in RulerLiftProposal proposal, long evaluatorCalls)
    {
        for (int i = _pendingPolicyOutcomes.Count - 1; i >= 0; i--)
        {
            PendingPolicyOutcome pending = _pendingPolicyOutcomes[i];
            int remaining = pending.WindowsRemaining - 1;
            if (remaining > 0)
            {
                _pendingPolicyOutcomes[i] = pending with { WindowsRemaining = remaining };
                continue;
            }
            ResolvePolicyOutcome(cortex, in pending, proposal.EClasses, proposal.Census, proposal.Bench, evaluatorCalls);
            _pendingPolicyOutcomes.RemoveAt(i);
        }
    }

    /// Settles decisions whose normal observation horizon extends beyond the run horizon. This is a
    /// state-only terminal transition: it records censored/no-future evidence, performs no sampling,
    /// evaluation, or policy learning, and clears the persisted pending set exactly once.
    public bool SettlePendingPolicyOutcomes(Cortex cortex)
    {
        ArgumentNullException.ThrowIfNull(cortex);
        if (_pendingPolicyOutcomes.Count == 0) return false;
        for (int i = _pendingPolicyOutcomes.Count - 1; i >= 0; i--)
        {
            PendingPolicyOutcome pending = _pendingPolicyOutcomes[i];
            CortexPolicyDecision source = pending.Decision;
            CortexPolicyDecision decision = new(source.DecisionID, source.Policy, source.Readout);
            cortex.ResolveCensoredPolicyOutcome(in decision);
            _pendingPolicyOutcomes.RemoveAt(i);
        }
        return true;
    }

    private static void ResolvePolicyOutcome(
        Cortex cortex,
        in PendingPolicyOutcome pending,
        int exactClasses,
        int theoremClasses,
        int bench,
        long evaluatorCalls)
    {
        long cost = evaluatorCalls - pending.EvaluatorBefore;
        int exactDelta = exactClasses - pending.ExactBefore;
        int theoremDelta = theoremClasses - pending.TheoremBefore;
        int benchDelta = bench - pending.BenchBefore;
        double frontierOpen = pending.Action == RulerLiftActions.Lift
            ? (double)exactDelta / 3 > Math.Max(1.0, pending.CompletionLine) ? 1 : 0
            : exactDelta > 0 ? 1 : 0;
        Span<MetricSample> outcomes = stackalloc MetricSample[5]
        {
            new(new MetricID((ushort)RulerLiftMetricIDs.ExactDelta), NumericValue.FromI64(exactDelta)),
            new(new MetricID((ushort)RulerLiftMetricIDs.TheoremDelta), NumericValue.FromI64(theoremDelta)),
            new(new MetricID((ushort)RulerLiftMetricIDs.BenchDelta), NumericValue.FromI64(benchDelta)),
            new(new MetricID((ushort)RulerLiftMetricIDs.FrontierOpen), NumericValue.FromF64(frontierOpen)),
            new(new MetricID((ushort)RulerLiftMetricIDs.EvaluatorCalls), NumericValue.FromI64(cost)),
        };
        bool invariantClean = cost >= 0 && exactDelta >= 0 && theoremDelta >= 0 && benchDelta >= 0;
        CortexPolicyDecision decision = pending.Decision;
        cortex.ResolvePolicyOutcome(in decision, outcomes, invariantClean, conservedCost: Math.Max(0, cost));
    }

    // fold post-lift windows into the pending LiftEvent — the re-ignition readout lands as the fresh box speaks.
    // Re-ignition's law: the fresh box's 3-window rate exceeds the very completion LINE that declared the old box
    // done (floored at 2/window) — novelty back above the verdict that fired the lift.
    void PatchReignition(int rate)
    {
        if (_patch1 >= 0) { _lifts[_patch1] = _lifts[_patch1] with { RateAfter1 = rate }; _patch1 = -1; }
        if (_patch3 < 0) return;
        _patchSum += rate; _patchN++;
        if (_patchN < 3) return;
        double a3 = _patchSum / 3;
        var e = _lifts[_patch3];
        _lifts[_patch3] = e with { RateAfter3 = a3, Reignited = a3 > Math.Max(1.0, e.Line) };
        _patch3 = -1;
    }

    /// The window telegraph — one line per close (trace-only, armed mounts).
    public string Line(in LiftWindow w)
        => $"lift·win step={w.Step} ruler={w.Ruler} rateE={w.RateE} ⌂{w.RateHome:F1} peak={w.PeakHome:F1} streak={w.Streak}/{_k.Sustain} · rateE+A={w.RateEA}"
         + (double.IsNaN(w.MeanzE) ? "" : $" · exact meanz={w.MeanzE:F3} cvz={w.CvzE:F3}/k{w.KzE}")
         + (w.SigmaDue ? $" · σ-DUE subres={w.SubResShare:P0}" : "")
         + $" → {w.Verdict}";

    // ── CHECKPOINT — the organ whole (armed mounts only; the section tag is the fail-loud gate) ──
    public void Save(CkptWriter w)
    {
        w.I32(_completedWindows); w.I32(_lastE); w.I32(_lastEA); w.I32(_lastSubRes);
        w.F64(_peak); w.I32(_streak); w.I32(_refractory); w.I32(_ruler); w.Bool(_ceiling); w.I32(_sigmaDue);
        _rate.Save(w);
        w.I32(_patch1); w.I32(_patch3); w.F64(_patchSum); w.I32(_patchN);
        w.I32(_lifts.Count);
        foreach (var e in _lifts)
        { w.I32(e.Step); w.I32(e.FromK); w.I32(e.ToK); w.I32(e.CensusE); w.I32(e.Census); w.I32(e.Bench); w.F64(e.RateBefore); w.F64(e.Line); w.F64(e.RateAfter1); w.F64(e.RateAfter3); w.Bool(e.Reignited); }
        w.I32(_wins.Count);
        foreach (var x in _wins)
        { w.I32(x.Step); w.I32(x.Ruler); w.I32(x.RateE); w.I32(x.RateEA); w.F64(x.RateHome); w.F64(x.PeakHome); w.I32(x.Streak); w.F64(x.MeanzE); w.F64(x.CvzE); w.I32(x.KzE); w.F64(x.SubResShare); w.Bool(x.SigmaDue); w.Str(x.Verdict); }
        w.I32(_pendingPolicyOutcomes.Count);
        for (int i = 0; i < _pendingPolicyOutcomes.Count; i++)
        {
            PendingPolicyOutcome pending = _pendingPolicyOutcomes[i];
            CortexPolicyDecision decision = pending.Decision;
            SavePolicyDecision(w, in decision);
            w.U8((byte)pending.Action);
            w.I32(pending.ExactBefore); w.I32(pending.TheoremBefore); w.I32(pending.BenchBefore);
            w.I64(pending.EvaluatorBefore); w.I32(pending.WindowsRemaining); w.F64(pending.CompletionLine);
        }
    }

    public void Load(CkptReader r)
    {
        _completedWindows = r.I32(); _lastE = r.I32(); _lastEA = r.I32(); _lastSubRes = r.I32();
        _peak = r.F64(); _streak = r.I32(); _refractory = r.I32(); _ruler = r.I32(); _ceiling = r.Bool(); _sigmaDue = r.I32();
        _rate.Load(r);
        _patch1 = r.I32(); _patch3 = r.I32(); _patchSum = r.F64(); _patchN = r.I32();
        _lifts.Clear();
        int nl = r.I32();
        for (int i = 0; i < nl; i++)
            _lifts.Add(new LiftEvent(r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.F64(), r.F64(), r.F64(), r.F64(), r.Bool()));
        _wins.Clear();
        int nw = r.I32();
        for (int i = 0; i < nw; i++)
            _wins.Add(new LiftWindow(r.I32(), r.I32(), r.I32(), r.I32(), r.F64(), r.F64(), r.I32(), r.F64(), r.F64(), r.I32(), r.F64(), r.Bool(), r.Str()));
        _pendingPolicyOutcomes.Clear();
        int pendingCount = r.I32();
        for (int i = 0; i < pendingCount; i++)
        {
            CortexPolicyDecision decision = LoadPolicyDecision(r);
            RulerLiftActions action = (RulerLiftActions)r.U8();
            _pendingPolicyOutcomes.Add(new PendingPolicyOutcome(decision, action, r.I32(), r.I32(), r.I32(),
                r.I64(), r.I32(), r.F64()));
        }
    }

    private static void SavePolicyDecision(CkptWriter writer, in CortexPolicyDecision decision)
        => CortexPolicyDecisionCheckpoint.Write(writer, in decision);

    private static CortexPolicyDecision LoadPolicyDecision(CkptReader reader)
        => CortexPolicyDecisionCheckpoint.Read(reader, PolicyID, PolicySchema.ActionCount);
}
