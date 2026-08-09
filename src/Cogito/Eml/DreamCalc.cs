namespace Cogito;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

// ── THE EML DREAM-CALCULATOR ENV ──  the first REPL env of the composed run: a pure RLEI loop
// with a free perfect verifier, no external data. GENERATE candidate RPN-EML expressions (grammar-biased to the
// learnable edge) → EVALUATE (clamped complex128, Eml.Eval) → SIEVE (dual-point Schanuel equivalence, EmlSieve) →
// MINT the discovered identities/value-hits as RPN-token text onto the tape → RE-INDUCE per stride so the grammar's
// chunks become named subroutines that later generation samples → deeper shells reachable (the spiral re-centering).
// Math discovery IS grammar compression over expression space; the kernel (eml + 1) becomes its own corpus.
//
// Two homes: ReplayCalc is a real ICurriculum (Cortex can drive it exactly like FlatPool/GrokBell — its "intake"
// is minting discoveries, its "pool" is expression space), and `cogito dreamcalc` drives it directly through the
// induce→draw→read loop to produce the observatory (the K-shell frontier, the minimal-program bench vs the paper,
// the ON/OFF chunking kill-line, the grok read over the identity corpus).

/// The dream-calculator curriculum. Each `Draw` is one dream step: generate a batch of candidate RPN programs —
/// grammar-biased from the current chunks when `bias`, else the paper's breadth-first enumeration — feed them to the
/// sieve, and accrete the freshly-minted identity/value lines onto the tape (+ journal). The trunk re-induces the
/// growing identity corpus on its stride, so the chunks the next `Draw` samples ARE the subroutines just discovered.
public sealed partial class ReplayCalc : ICurriculum, ICurriculumCheckpointDeltaOwner
{
    internal const string DeepRematchFuelCursorSidecarFile = "eml_deep_rematch_handshake.ron";
    // ── the default EML curriculum knob set (mirrors the observatory's `cogito dreamcalc` defaults) ──
    // The public surface is CortexEmlCurriculum; these constants are only the mount defaults and legacy
    // checkpoint fallback. Campfire mounts the same set — one EML env, two mount points.
    internal const int MountSig     = 9;                 // dual-point sig figures (the Schanuel sieve's quantizer)
    internal const int MountSeedK   = 7;                 // seed shells enumerated at bootstrap (the axiom rediscovery)
    internal const int MountMaxLen  = 40;                // sampled-program length cap
    internal const int MountMaxEnum = 13;                // the OFF arm's enumeration cap (unused by the biased mounts)
    internal const int MountUnits   = 6;                 // sampled units per program (the base draw — the ε-tail extends it)
    internal const int MountGain    = 4;                 // chunk-frequency bias vs the flat token weight
    internal const double MountEps  = 0.125;             // the uniform ε (EmlGen.Sample) — the support floor's mass; 0 = the proven basin trap (kill-line control only)
    // the ε-ENUMERATION rail's mass (EmlSampler) — the COVERAGE rail. Sized to the subtraction-face: x−1/x−y
    // (K=11, the shell only enumeration ever hit) sit ~4.6k systematic programs past the seed shells (shell 9's
    // 3402 + ~1.2k into shell 11's 30618 — MEASURED: the rail-only probe minted them at step 143/144×32 ≈ 4.6k).
    // 0.4 × the flagship 600×32 budget crosses at step ~360, leaving ~240 steps for the reward-driven composition
    // cascade (at 0.3 the crossing landed ~480/600 and the cascade had no runway). 0 = rail off (byte-identical
    // control with eps=0).
    internal const double MountEpsEnum = 0.4;
    // the NOVEL-corroboration tape weight (the second-witness REWARD, Weitzman-shaped): the FIRST E-witness of a
    // registered target (bench/anomaly) re-ingests at this weight — repetition IS weight to Re-Pair, so the fresh
    // face chunks immediately and the bias concentrates adjacent-to-known. Re-hits pay 1 (at flat ×4-for-all, the
    // seed-round constants soaked the reward mass in re-hits and the cascade never fired); each target pays ONCE,
    // so the weight can be strong without a wirehead loop. 1 = off.
    internal const int MountCorrobW = 16;
    // the CERT-NOVELTY accretion gate: a FIRST-capture (new
    // EmlCert class) accretes ×this, a PARAPHRASE (existing class) accretes ×0 — the tape stops feeding the
    // generator baroque D-variants of theorems it already owns (74% of flagship mints were paraphrase subsidy)
    // and its budget redirects to genuine new certificates. Sized VOLUME-NEUTRAL: at ~74% paraphrase share the
    // starve cuts ~3/4 of tape lines, and first-captures (~26%) at ×4 restore ≈104% — the tape growth rate (and
    // the stride-gated re-induce cadence) stays comparable while 100% of the chunk fuel is first-capture lines.
    // 1 = off (legacy every-mint-×1 — the kill-line control arm).
    internal const int MountCertW = 4;

    /// The runtime curriculum mount — `--curriculum eml`: grammar-biased (the spiral), the observatory's default knobs.
    public static ReplayCalc Mount(ulong seed) => new(new EmlSieve(MountSig), bias: true, EmlKnobs.Mount, seed, deliberationQuota: EmlDeliberationQuota.Default);
    public static ReplayCalc Mount(ulong seed, CortexEmlCurriculum curriculum)
    {
        if (curriculum is null) throw new ArgumentNullException(nameof(curriculum));
        if (curriculum.Generation is null) throw new ArgumentException("CortexEmlCurriculum.Generation is required.", nameof(curriculum));
        if (curriculum.Lift is null) throw new ArgumentException("CortexEmlCurriculum.Lift is required.", nameof(curriculum));
        return new ReplayCalc(
            BuildSieve(curriculum.SignatureDigits, curriculum.HoldoutFraction, curriculum.HoldoutSeed, seed, curriculum.TargetCatalog),
            bias: true,
            curriculum.ToKnobs(),
            seed,
            curriculum.Actions,
            curriculum.GrammarSampling == EmlGrammarSamplingModes.Frozen,
            curriculum.ProcessCatalog,
            curriculum.Rung0,
            curriculum.Deliberation,
            curriculum.DeliberationBudget);
    }

    internal static EmlSieve BuildSieve(
        int sig,
        double holdoutFraction,
        ulong holdoutSeed,
        ulong runSeed,
        EmlTargetCatalogs targetCatalog = EmlTargetCatalogs.LeafCount)
    {
        if (targetCatalog == EmlTargetCatalogs.ScientificCalculator)
        {
            if (holdoutFraction > 0)
                throw new ArgumentException("scientific-calculator target holdout requires an explicit catalog-sized mask");
            return new EmlSieve(sig, EmlScientificCalculatorBasis.CreateTargets());
        }
        if (holdoutFraction <= 0) return new EmlSieve(sig);
        var (mask, _, _) = EmlSieve.HoldoutMask(holdoutFraction, holdoutSeed == 0 ? runSeed : holdoutSeed);
        return new EmlSieve(sig, mask);
    }

    private readonly EmlSieve _sieve;
    private readonly ulong _seed;
    private readonly bool _bias;                          // grammar-biased sampling (the spiral) vs pure enumeration (the null)
    private readonly int _seedK, _maxEnum, _corrobW, _certW;
    private readonly EmlSampler _sampler;                // the ON arm's three-rail candidate source (bias · uniform ε · the ε-enumeration sweep)
    private readonly RulerLift? _lift;                   // THE LIFT OPERATOR — null = rulers pinned (the Vow arm; every pre-lift mount)
    private const uint LiftTag = 0x4C494654;             // "LIFT" — the armed checkpoint's fail-loud section gate
    private const uint AnytimeTag = 0x41544356;          // ATCV: typed anytime curve authority
    private const uint AnytimeFuelCursorTag = 0x41464355; // AFCU: persisted post-handshake fuel cursor
    private const uint PairedFuelScheduleTag = 0x50465343; // PFSC: paired step-wallet schedule
    private EmlAnytimeCurve _anytimeCurve = new();
    private int _checkpointAnytimePointCount;
    private int _checkpointAnytimeKillCount;
    private bool _anytimeCheckpointRebasePending;
    private string _anytimeRebasePredecessorRunID = "";
    private string _anytimeRebasePredecessorConfigID = "";
    private string _anytimeRebasePredecessorChainID = "";
    private string _anytimeRebasePredecessorArmID = "";
    private Run? _anytimeRun;
    private string _anytimeConfigID = "";
    private string _anytimeChainID = "";
    private string _anytimeArmID = "";
    private int _anytimeRung;
    private string _anytimeParentPointID = "";
    private long _anytimeEvaluatorBaseline;
    private EmlAnytimeCommitments _anytimeCommitmentBaseline;
    private EmlDeliberationCounts _anytimeFuelBaseline;
    private EmlDeliberationCounts _anytimePlannedFuelBaseline;
    private EmlDeepRematchFuelCursor _anytimeFuelCursor;
    private bool _anytimeFuelCursorPresent;
    private EmlPairedFuelSchedule? _pairedFuelSchedule;
    private EmlPairedFuelScheduleCursor? _pairedFuelCursor;
    private bool _pairedFuelCursorDirty;
    private EmlDeliberationCounts _pairedFuelWallet;
    private EmlDeliberationCounts _pairedFuelStepPlanned;
    private int _pairedFuelStep = -1;
    private int _pairedFuelSettlementStart;
    private bool _pairedFuelStepAllocated;
    private bool _pairedFuelStepInSchedule;
    private long _anytimeWindowStartedTicks;
    private double _anytimeWindowElapsedMilliseconds;
    private IEnumerator<string>? _enum;                  // the OFF arm's breadth-first continuation past the seed shells
    private int _enumTaken;                              // successful MoveNexts — the enumeration's checkpoint cursor (LoadState replays it; the walk is deterministic)
    private bool _enumDone;
    private int _minted;                                 // total spans accreted, corroboration weight included (the IngestedCount column)
    private TapeEventID[] _worldOpportunityEvents = Array.Empty<TapeEventID>();
    private TapeEventID[] _currentWorldOpportunityEvents = Array.Empty<TapeEventID>();
    private int _worldOpportunityCursor;
    private string? _anchor;                             // the shallowest minted line — the MIX re-ingest anchor (self-data reality)
    private List<EmlGen.Chunk>? _chunks;                 // pure-RPN chunk cache, keyed on grammar identity …
    private GrammarRule[]? _chunkRules;                  // … rebuilt only on re-induce (PureChunks expands every rule — per-stride work, not per-step)
    private readonly bool _freezeSamplingGrammar;
    private readonly EmlProcessCatalogs _processCatalog;
    private readonly EmlRung0Modes _rung0Mode;
    private readonly EmlDeliberationModes _deliberationMode;
    private readonly EmlDeliberationQuota _deliberationQuota;
    private const uint SamplingGrammarTag = 0x5347524D;  // SGRM: frozen grammar-sampler state, absent from normal checkpoints.
    private const uint WorldOpportunityTag = 0x574F5050; // WOPP: arm-neutral ordinary-world opportunity cursor.
    private const uint WorldOpportunityEventsTag = 0x574F5045; // WOPE: the tape-derived lineage prefix held by WOPP.

    /// `bias` picks the arm (the three-rail sampler vs the pure-enumeration null); the knob set parameterizes
    /// generation + the corroboration reward (EmlKnobs — one shape from CLI/mount to env); `seed` is the Vow's
    /// replay key.
    internal ReplayCalc(
        EmlSieve sieve,
        bool bias,
        in EmlKnobs k,
        ulong seed,
        EmlActionSelections actions = EmlActionSelections.Off,
        bool freezeSamplingGrammar = false,
        EmlProcessCatalogs processCatalog = EmlProcessCatalogs.Full,
        EmlRung0Modes rung0Mode = EmlRung0Modes.Armed,
        EmlDeliberationModes deliberationMode = EmlDeliberationModes.Adaptive,
        EmlDeliberationQuota deliberationQuota = default)
    {
        _sieve = sieve; _seed = seed; _bias = bias; _seedK = k.SeedK; _maxEnum = k.MaxEnum; _corrobW = Math.Max(1, k.CorrobW); _certW = Math.Max(1, k.CertW);
        _freezeSamplingGrammar = freezeSamplingGrammar;
        if (!Enum.IsDefined(processCatalog)) throw new ArgumentOutOfRangeException(nameof(processCatalog));
        if (!Enum.IsDefined(rung0Mode)) throw new ArgumentOutOfRangeException(nameof(rung0Mode));
        if (!Enum.IsDefined(deliberationMode)) throw new ArgumentOutOfRangeException(nameof(deliberationMode));
        _processCatalog = processCatalog;
        _rung0Mode = rung0Mode;
        _deliberationMode = deliberationMode;
        _deliberationQuota = deliberationQuota == default ? EmlDeliberationQuota.Default : deliberationQuota;
        _deliberationQuota.Validate();
        _sampler = new EmlSampler(k.Units, k.MaxLen, k.Gain, k.Eps, k.EpsEnum, k.SeedK, seed);
        _lift = bias && k.Lift.Armed ? new RulerLift(k.Lift, k.MaxLen) : null;   // the lift rides the ON policy only (the OFF null keeps the fixed ruler)
        ConfigureOrdinaryRunRung0Digests("seed=" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        InitializeActionState(actions, seed, in k);
    }

    /// The lift organ (null when disarmed) + the live K-ruler — the observatory's tower-walk reads.
    internal RulerLift? Lift => _lift;
    public int Ruler => _sampler.MaxLen;

    public bool Drained => _enumDone;                    // OFF: the enumeration ran dry; ON: never (the loop guards on steps)
    public bool Exhausted => _enumDone;                  // generative — a dry enumeration IS exhaustion; no abandoned pool to mop up, so Drained IS Exhausted and the trunk's mop-up era is unreachable (ICurriculum.Advance default = false)
    public int IngestedCount => _minted;
    public int WorldOpportunityCursor => _worldOpportunityCursor;
    public int MixEvery { get; set; }                    // ICurriculum's homeostat actuator — UNCONSULTED here: Mix re-anchors the axiom every call (the dream's reality is the operator itself, cadence-free)
    public int StreakResets => 0;                        // no grok bells — nothing to thrash (ICurriculum's C2 read)

    /// The Cortex world mouth binds the corpus event IDs returned by CommitWorldEncounter.
    /// They are retained as source identity; only a bounded deterministic slice is copied into
    /// each offer context, so no offer can accidentally cite the whole precommitted world.
    public void BindWorldOpportunityEvents(IReadOnlyList<TapeEventID> eventIDs)
    {
        ArgumentNullException.ThrowIfNull(eventIDs);
        TapeEventID previous = default;
        for (int i = 0; i < eventIDs.Count; i++)
        {
            TapeEventID current = eventIDs[i];
            if (i > 0 && current.Value <= previous.Value)
                throw new InvalidDataException("ReplayCalc world opportunity stream is not strictly increasing: duplicate or reordered lineage");
            previous = current;
        }

        // The tape is the authority on the restored prefix. A resumed WOPP state may
        // have consumed fewer events than the world mouth has already committed, but
        // it may never claim a cursor beyond that exact tape-derived prefix.
        if (_worldOpportunityEvents.Length == 0 && eventIDs.Count < _worldOpportunityCursor)
            throw new InvalidDataException("ReplayCalc world opportunity tape prefix is shorter than persisted cursor");
        if (_worldOpportunityEvents.Length > eventIDs.Count)
            throw new InvalidDataException("ReplayCalc world opportunity stream shrank during append-only bind");
        for (int i = 0; i < _worldOpportunityEvents.Length; i++)
            if (_worldOpportunityEvents[i] != eventIDs[i])
                throw new InvalidDataException("ReplayCalc world opportunity lineage prefix changed during append-only bind");

        _worldOpportunityEvents = eventIDs.ToArray();
        if (_worldOpportunityCursor < 0 || _worldOpportunityCursor > _worldOpportunityEvents.Length)
            throw new InvalidDataException("ReplayCalc world opportunity cursor is outside the bound event stream");
    }

    private IReadOnlyList<TapeEventID> BeginWorldOpportunityBatch(int requested)
    {
        _currentWorldOpportunityEvents = Array.Empty<TapeEventID>();
        if (requested <= 0 || _worldOpportunityCursor >= _worldOpportunityEvents.Length)
        {
            if (requested > 0) _worldOpportunityCursor = Math.Min(_worldOpportunityCursor, _worldOpportunityEvents.Length);
            return _currentWorldOpportunityEvents;
        }
        int count = Math.Min(Math.Min(requested, 1024), _worldOpportunityEvents.Length - _worldOpportunityCursor);
        _currentWorldOpportunityEvents = new TapeEventID[count];
        Array.Copy(_worldOpportunityEvents, _worldOpportunityCursor, _currentWorldOpportunityEvents, 0, count);
        _worldOpportunityCursor = checked(_worldOpportunityCursor + count);
        return _currentWorldOpportunityEvents;
    }

    private void OfferWithCurrentOpportunity(string program)
    {
        EmlOfferContext context = new(_currentWorldOpportunityEvents);
        _sieve.Offer(program, in context);
    }

    internal EmlAnytimeCurve AnytimeCurve => _anytimeCurve;
    internal EmlDeepRematchFuelCursor DeepRematchFuelCursor
        => _anytimeFuelCursorPresent
            ? _anytimeFuelCursor
            : throw new InvalidDataException("deep-rematch EML handshake cursor is not bound");
    internal EmlDeepRematchFuelCursor? PersistedDeepRematchFuelCursor
        => _anytimeFuelCursorPresent ? _anytimeFuelCursor : null;
    internal string DeepRematchFuelCursorSidecarPath
        => _anytimeRun?.PathOf(DeepRematchFuelCursorSidecarFile)
            ?? throw new InvalidDataException("deep-rematch EML cursor sidecar has no bound run");
    internal string DeepRematchFuelCursorSidecarSHA256
        => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(DeepRematchFuelCursorSidecarPath)));

    internal bool HasPairedFuelSchedule => _pairedFuelSchedule is not null;
    internal EmlPairedFuelSchedule PairedFuelSchedule
        => _pairedFuelSchedule ?? throw new InvalidDataException("paired fuel schedule is not configured");
    internal EmlPairedFuelScheduleCursor PairedFuelScheduleCursor
        => _pairedFuelSchedule is { } schedule && _pairedFuelCursor is { } cursor
            ? cursor.Validate(in schedule)
            : throw new InvalidDataException("paired fuel schedule is not configured");

    internal void ConfigurePairedFuelSchedule(int horizon, in EmlDeliberationQuota quota, string identity = "paired-gate-fuel-v1")
    {
        EmlDeliberationCounts total = Counts(in quota);
        EmlPairedFuelSchedule schedule = EmlPairedFuelSchedule.Create(identity, horizon, in total);
        if (_pairedFuelSchedule is { } existing && existing != schedule)
            throw new InvalidDataException("paired fuel schedule configuration changed during resume");
        _pairedFuelSchedule = schedule;
        if (_pairedFuelCursor is null)
            _pairedFuelCursor = EmlPairedFuelScheduleCursor.Create(in schedule);
        else
            _pairedFuelCursor.Validate(in schedule);
        _pairedFuelCursorDirty = false;
    }

    internal void BeginPairedFuelStep(Cortex cortex, int step)
    {
        ArgumentNullException.ThrowIfNull(cortex);
        if (_pairedFuelSchedule is not { } schedule) return;
        if (step < 0) throw new InvalidDataException($"paired fuel step {step} is outside schedule horizon {schedule.Horizon}");
        if (_pairedFuelStep == step) return;
        if (_pairedFuelStep >= 0 && _pairedFuelStep != step - 1)
            throw new InvalidDataException("paired fuel step cursor skipped or repeated a step");
        EmlPairedFuelScheduleCursor cursor = _pairedFuelCursor
            ?? throw new InvalidDataException("paired fuel schedule has no cursor");
        if (step >= schedule.Horizon)
        {
            if (cortex.AllowsAutonomicSpawning)
                throw new InvalidDataException($"paired fuel step {step} is outside schedule horizon {schedule.Horizon}");
            cursor.ValidateClosed(in schedule);
            // A funded fork may outlive the registered parent horizon. Its arm-step allocation owns that
            // tail; the sealed paired prefix remains in custody and must not mint unregistered EML fuel.
            _pairedFuelStep = step;
            _pairedFuelStepPlanned = EmlDeliberationCounts.Zero;
            _pairedFuelWallet = EmlDeliberationCounts.Zero;
            _pairedFuelStepAllocated = false;
            _pairedFuelStepInSchedule = false;
            _pairedFuelSettlementStart = _sieve.DeliberationJournal.Settlements.Count;
            if (_pairedFuelStep == schedule.Horizon)
                Trace.Cortex.Boundary("eml.paired-fuel.closed-prefix",
                    $"step={step} horizon={schedule.Horizon} rail={cortex.ForkRailRole} rows={cursor.RowCount} active=false");
            return;
        }
        if (cursor.Validate(in schedule).RowCount != step)
            throw new InvalidDataException("paired fuel schedule cursor does not own the requested step");
        _pairedFuelStep = step;
        _pairedFuelStepPlanned = schedule.Row(step);
        _pairedFuelWallet = _pairedFuelStepPlanned;
        _pairedFuelStepAllocated = false;
        _pairedFuelStepInSchedule = true;
        _pairedFuelSettlementStart = _sieve.DeliberationJournal.Settlements.Count;
    }

    internal EmlDeliberationQuota PairedFuelWalletQuota()
    {
        _pairedFuelWallet.ValidateNonnegative("paired fuel step wallet");
        return new(_pairedFuelWallet.CandidateEvaluations, _pairedFuelWallet.LogicalProgramPoints, _pairedFuelWallet.ExecutedProgramPoints,
            _pairedFuelWallet.InverseTransforms, _pairedFuelWallet.HashProbes, _pairedFuelWallet.JoinAttempts, _pairedFuelWallet.JoinHits,
            _pairedFuelWallet.ProcessTerms, _pairedFuelWallet.VerifierProgramPoints, _pairedFuelWallet.CandidateSupplyItems,
            _pairedFuelWallet.LawRewriteApplications, _pairedFuelWallet.LawRewriteTreeNodes);
    }

    internal void MarkPairedFuelLeaseAllocated()
    {
        if (_pairedFuelSchedule is null || _pairedFuelStepAllocated) return;
        _pairedFuelStepAllocated = true;
        _pairedFuelWallet = EmlDeliberationCounts.Zero;
    }

    internal EmlDeliberationCounts ReadDeepRematchFuelTotalsSinceHandshake(bool planned, bool refund)
        => _anytimeFuelCursorPresent
            ? ReadDeepRematchFuelTotals(in _anytimeFuelCursor, planned, refund)
            : ReadDeepRematchFuelTotals(planned, refund);

    internal EmlDeepRematchFuelCursor ValidateDeepRematchFuelCursorAgainstLoadedCheckpoint()
    {
        EmlDeepRematchFuelCursor cursor = DeepRematchFuelCursor.Validate();
        _ = ReadDeepRematchFuelTotals(in cursor, planned: true, refund: false);
        _ = ReadDeepRematchFuelTotals(in cursor, planned: false, refund: false);
        _ = ReadDeepRematchFuelTotals(in cursor, planned: false, refund: true);
        return cursor;
    }

    internal void BindAnytimeRun(Run run, string configID, string chainID, string armID, int rung = 0, string parentPointID = "")
    {
        ArgumentNullException.ThrowIfNull(run);
        string runID = Path.GetFileName(run.Dir);
        bool inheritedCheckpointCursor = _checkpointAnytimePointCount != 0 || _checkpointAnytimeKillCount != 0
            || _anytimeFuelCursorPresent;
        bool persistedScope = _anytimeCurve.HasPersistedScope;
        bool scopeIdentityChanged = persistedScope && (_anytimeCurve.ScopeConfigID != configID
            || _anytimeCurve.ScopeChainID != chainID || _anytimeCurve.ScopeArmID != armID);
        // A rebase is allowed to leave a typed successor scope with no points yet. The old
        // point/kill/AFCU cursors still belong to the inherited keyframe in that shape; treating
        // the empty log as "no scope" was the hole that let a parent cursor survive BindAnytimeRun.
        bool emptyLogCarriesInheritedCursor = _anytimeCurve.Points.Count == 0 && inheritedCheckpointCursor;
        bool scopeChanged = scopeIdentityChanged || emptyLogCarriesInheritedCursor;
        if (scopeChanged)
        {
            // A child arm inherits the checkpoint's absolute EML state, but owns a new curve chain.
            // Replace every cursor that mutates the curve together: a fresh curve with a parent cursor
            // is not a resumable prefix and would make the first child delta address a nonexistent point.
            string parentDigest = _anytimeCurve.Digest;
            if (parentDigest.Length == 0) parentDigest = _anytimeCurve.RebaseParentPointID;
            if (parentDigest.Length == 0) parentDigest = _anytimeParentPointID;
            _anytimeRebasePredecessorRunID = _anytimeCurve.ScopeRunID;
            _anytimeRebasePredecessorConfigID = _anytimeCurve.ScopeConfigID;
            _anytimeRebasePredecessorChainID = _anytimeCurve.ScopeChainID;
            _anytimeRebasePredecessorArmID = _anytimeCurve.ScopeArmID;
            parentPointID = string.IsNullOrWhiteSpace(parentPointID) ? parentDigest : parentPointID;
            if (parentDigest.Length == 0 || parentPointID != parentDigest)
                throw new InvalidDataException("EML anytime child parent point does not match the spawning curve");
            _anytimeCurve = new EmlAnytimeCurve();
            _checkpointAnytimePointCount = 0;
            _checkpointAnytimeKillCount = 0;
            _anytimeCheckpointRebasePending = true;
            // AFCU is the keyframe for the curve's fuel/evaluator axes. It must be recaptured by the
            // child's handshake; retaining the parent's cursor would bind child deltas to parent custody.
            _anytimeFuelCursor = default;
            _anytimeFuelCursorPresent = false;
            _anytimeEvaluatorBaseline = _sieve.EvaluatorClock.ProgramPointEvaluations;
            _anytimeCommitmentBaseline = ReadAnytimeCommitments();
            _anytimeFuelBaseline = ReadAnytimeFuelTotals();
            _anytimePlannedFuelBaseline = ReadAnytimePlannedFuelTotals();
            _anytimeWindowElapsedMilliseconds = 0;
            _anytimeWindowStartedTicks = Stopwatch.GetTimestamp();
        }
        else if (_anytimeCurve.Points.Count == 0)
        {
            _anytimeEvaluatorBaseline = _sieve.EvaluatorClock.ProgramPointEvaluations;
            _anytimeCommitmentBaseline = ReadAnytimeCommitments();
            _anytimeFuelBaseline = ReadAnytimeFuelTotals();
            _anytimePlannedFuelBaseline = ReadAnytimePlannedFuelTotals();
        }
        else
        {
            EmlAnytimeCurvePoint last = _anytimeCurve.Points[^1];
            long absoluteCalls = _sieve.EvaluatorClock.ProgramPointEvaluations;
            _anytimeEvaluatorBaseline = checked(absoluteCalls - last.EvaluatorIntervals);
            if (_anytimeEvaluatorBaseline < 0) throw new InvalidDataException("EML anytime evaluator baseline regressed on resume");
            EmlAnytimeCommitments absoluteCommitments = ReadAnytimeCommitments();
            EmlAnytimeCommitments priorQuality = last.Quality;
            _anytimeCommitmentBaseline = EmlAnytimeCommitments.Subtract(in absoluteCommitments, in priorQuality);
            _anytimeCommitmentBaseline.Validate("resume baseline");
            EmlDeliberationCounts absoluteFuel = ReadAnytimeFuelTotals();
            EmlDeliberationCounts priorFuel = last.Fuel;
            _anytimeFuelBaseline = EmlDeliberationCounts.Subtract(in absoluteFuel, in priorFuel);
            _anytimeFuelBaseline.ValidateNonnegative("resume fuel baseline");
            EmlDeliberationCounts absolutePlannedFuel = ReadAnytimePlannedFuelTotals();
            EmlDeliberationCounts priorPlannedFuel = _anytimeCurve.PlannedFuel;
            _anytimePlannedFuelBaseline = EmlDeliberationCounts.Subtract(in absolutePlannedFuel, in priorPlannedFuel);
            _anytimePlannedFuelBaseline.ValidateNonnegative("resume planned fuel baseline");
        }
        _anytimeRun = run; _anytimeConfigID = configID; _anytimeChainID = chainID; _anytimeArmID = armID; _anytimeRung = rung;
        if (_pairedFuelSchedule is { } pairedSchedule)
        {
            string schedulePath = run.PathOf(EmlPairedFuelSchedule.SidecarFile);
            if (File.Exists(schedulePath))
            {
                (EmlPairedFuelSchedule restoredSchedule, EmlPairedFuelScheduleCursor restoredCursor) =
                    EmlPairedFuelScheduleJournal.Decode(File.ReadAllBytes(schedulePath));
                if (restoredSchedule != pairedSchedule) throw new InvalidDataException("paired fuel schedule sidecar disagrees with configured schedule");
                if (_pairedFuelCursor is null)
                {
                    _pairedFuelCursor = restoredCursor;
                    _pairedFuelCursorDirty = false;
                }
                else
                {
                    EmlPairedFuelScheduleCursor checkpointCursor = _pairedFuelCursor.Validate(in pairedSchedule);
                    _ = EmlPairedFuelScheduleCursor.ReconcileResumeCursor(in pairedSchedule, in checkpointCursor, in restoredCursor);
                    // A kill after the checkpoint commit but before this sidecar replacement leaves a valid,
                    // older witness. Keep the checkpoint cursor authoritative and repair the witness at the next
                    // committed horizon; never let the sidecar advance resumed work.
                    _pairedFuelCursorDirty = restoredCursor.RowCount < checkpointCursor.RowCount;
                }
            }
            else if (_pairedFuelCursor is { RowCount: > 0 })
                throw new InvalidDataException("paired fuel schedule cursor has no matching sidecar");
        }
        _anytimeParentPointID = parentPointID;
        if (_anytimeCurve.Points.Count > 0 && _anytimeFuelCursorPresent)
        {
            if (!_anytimeCurve.Points[0].IsHandshake || _anytimeCurve.Points[0].PointID != _anytimeFuelCursor.PointID
                || _anytimeCurve.Points[0].Digest != _anytimeFuelCursor.PointDigest)
                throw new InvalidDataException("EML anytime resume would duplicate or replace the cold handshake point");
            _anytimeEvaluatorBaseline = 0;
            _anytimeFuelBaseline = EmlDeliberationCounts.Zero;
            _anytimePlannedFuelBaseline = EmlDeliberationCounts.Zero;
        }
        if (_anytimeWindowStartedTicks == 0) _anytimeWindowStartedTicks = Stopwatch.GetTimestamp();
    }

    /// Captures the physical evaluation step zero after the cold policy decision has settled. The point is
    /// intentionally unscored, but remains the first digest-chain link and the baseline for all later fuel axes.
    /// Repeated callback delivery is idempotent and rejects any attempt to replace the original handshake.
    internal EmlAnytimeCurvePoint CaptureDeepRematchEvaluationHandshake()
    {
        if (_anytimeRun is null) throw new InvalidOperationException("cannot capture an EML handshake before binding the anytime run");
        if (_anytimeCurve.Points.Count > 0)
        {
            EmlAnytimeCurvePoint existing = _anytimeCurve.Points[0];
            if (!existing.IsHandshake || !_anytimeFuelCursorPresent
                || existing.PointID != _anytimeFuelCursor.PointID || existing.Digest != _anytimeFuelCursor.PointDigest)
                throw new InvalidDataException("EML evaluation handshake is already occupied by a different point");
            PersistDeepRematchFuelCursorSidecar();
            return existing;
        }
        string evidenceDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            _anytimeRun.Dir, _anytimeConfigID, _anytimeChainID, _anytimeArmID, _anytimeParentPointID, "handshake"))));
        EmlAnytimeBoundaryReceipt handshake = new(
            Path.GetFileName(_anytimeRun.Dir), _anytimeConfigID, _anytimeChainID, _anytimeArmID, _anytimeParentPointID,
            _anytimeRung, 0, 0, "evaluation.cold.handshake", EmlAnytimeCommitments.Zero,
            EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero,
            0, 0, 0, false, true, false, false, false, false, false, false, 0, "", evidenceDigest,
            double.NaN, double.NaN, double.NaN, 0);
        EmlAnytimeCurvePoint point = _anytimeCurve.Append(in handshake);
        _anytimeFuelCursor = CaptureDeepRematchFuelCursor(point.PointID, point.Digest);
        _anytimeFuelCursorPresent = true;
        _anytimeCommitmentBaseline = ReadAnytimeCommitments();
        _anytimeEvaluatorBaseline = 0;
        _anytimeFuelBaseline = EmlDeliberationCounts.Zero;
        _anytimePlannedFuelBaseline = EmlDeliberationCounts.Zero;
        _anytimeCurve.WriteTSV(_anytimeRun.PathOf("eml_anytime_curve.tsv"));
        PersistDeepRematchFuelCursorSidecar();
        return point;
    }

    private void PersistDeepRematchFuelCursorSidecar()
    {
        if (!_anytimeFuelCursorPresent || _anytimeRun is null)
            throw new InvalidDataException("deep-rematch EML cursor sidecar requires a captured handshake");
        EmlDeepRematchFuelCursorDocument document = EmlDeepRematchFuelCursorDocument.FromCursor(in _anytimeFuelCursor);
        byte[] bytes = RonSerializer.SerializeToUtf8(in document);
        _anytimeRun.WriteAtomic(DeepRematchFuelCursorSidecarFile, stream => stream.Write(bytes));
        EmlDeepRematchFuelCursorDocument restored = RonSerializer.Deserialize<EmlDeepRematchFuelCursorDocument>(File.ReadAllBytes(DeepRematchFuelCursorSidecarPath));
        if (!restored.ToCursor().Equals(_anytimeFuelCursor) || !bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in restored)))
            throw new InvalidDataException("deep-rematch EML cursor sidecar SaveLoadSave drifted");
    }

    /// Land the paired-fuel cursor only at a durable checkpoint or final seal.
    /// The cursor is validated when the run opens; the hot step close only
    /// advances the in-memory chain and marks this sidecar dirty.
    internal void PersistPairedFuelScheduleSidecar()
    {
        if (!_pairedFuelCursorDirty)
            return;
        if (_anytimeRun is null)
            throw new InvalidDataException("paired fuel cursor is dirty before an anytime run is bound");
        if (_pairedFuelSchedule is not { } schedule || _pairedFuelCursor is not { } cursor)
            throw new InvalidDataException("paired fuel cursor is dirty without a configured schedule");
        cursor.Validate(in schedule);
        EmlPairedFuelScheduleJournal.WriteAtomic(_anytimeRun, in schedule, cursor);
        _pairedFuelCursorDirty = false;
    }

    internal EmlDeepRematchFuelCursor ReadDeepRematchFuelCursorSidecar(string path)
    {
        EmlDeepRematchFuelCursorDocument document = RonSerializer.Deserialize<EmlDeepRematchFuelCursorDocument>(File.ReadAllBytes(path));
        return document.ToCursor();
    }

    /// Read the typed AFCU section from a persisted checkpoint image.  The checkpoint is a binary
    /// multiplex of independently tagged organs; this narrow reader proves that the cursor carried by
    /// an evaluation record is present in the exact final image, rather than merely agreeing with the
    /// curve's handshake row or a detached RON sidecar.
    internal static EmlDeepRematchFuelCursor ReadDeepRematchFuelCursorFromCheckpointImage(ReadOnlySpan<byte> image)
    {
        const int tagBytes = sizeof(uint);
        EmlDeepRematchFuelCursor? found = null;
        bool duplicate = false;
        for (int offset = 0; offset <= image.Length - tagBytes; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(image[offset..]) != AnytimeFuelCursorTag)
                continue;
            try
            {
                using MemoryStream stream = new(image[(offset + tagBytes)..].ToArray(), writable: false);
                using CkptReader reader = new(stream);
                EmlDeepRematchFuelCursor candidate = ReadDeepRematchFuelCursor(reader).Validate();
                if (found is not null)
                {
                    duplicate = true;
                    break;
                }
                found = candidate;
            }
            catch (EndOfStreamException)
            {
                // A coincidental four-byte tag in another organ is not an AFCU section unless its
                // versioned cursor payload parses and validates completely.
            }
            catch (InvalidDataException)
            {
                // Same rule for malformed candidates: keep scanning for the one structurally valid
                // section and fail below if none exists.
            }
        }
        if (duplicate) throw new InvalidDataException("checkpoint carries duplicate AFCU cursor sections");
        return found ?? throw new InvalidDataException("checkpoint omits the AFCU deep-rematch fuel cursor section");
    }

    public void RegisterPolicies(Cortex cortex)
    {
        if (_actionSelection != EmlActionSelections.Off) cortex.RegisterPolicy(ActionPolicySchema);
        if (_lift is not null) cortex.RegisterPolicy(RulerLift.PolicySchema);
    }

    public void OnStepCompleted(Cortex cortex, int step)
    {
        ClosePairedFuelStep(cortex, step);
        int completedStep = step + 1;
        if (_lift is not null && _lift.IsWindowDue(completedStep)) CloseLiftWindow(completedStep, cortex);
    }

    private void ClosePairedFuelStep(Cortex cortex, int step)
    {
        if (_pairedFuelSchedule is not { } schedule) return;
        if (_pairedFuelStep != step) BeginPairedFuelStep(cortex, step);
        if (!_pairedFuelStepInSchedule)
        {
            if (_sieve.DeliberationJournal.Settlements.Count != _pairedFuelSettlementStart)
                throw new InvalidDataException("closed paired fuel prefix admitted unplanned deliberation work");
            _pairedFuelCursor?.ValidateClosed(in schedule);
            return;
        }
        EmlDeliberationCounts actual = EmlDeliberationCounts.Zero;
        IReadOnlyList<EmlDeliberationSettlement> settlements = _sieve.DeliberationJournal.Settlements;
        for (int i = _pairedFuelSettlementStart; i < settlements.Count; i++)
        {
            EmlDeliberationCounts settlementActual = settlements[i].Actual;
            actual = EmlDeliberationCounts.Add(in actual, in settlementActual);
        }
        actual.ValidateNonnegative("paired fuel actual row");
        EmlPairedFuelScheduleCursor cursor = _pairedFuelCursor
            ?? throw new InvalidDataException("paired fuel schedule has no cursor");
        _pairedFuelCursor = cursor.Append(in schedule, step, in _pairedFuelStepPlanned, in actual);
        _pairedFuelWallet = EmlDeliberationCounts.Zero;
        _pairedFuelCursorDirty = true;
    }
    // Advance + IngestDiversity: the interface defaults hold — Drained==Exhausted means the mop-up era never opens
    // (ReplayCalc GENERATES its corpus, no abandoned pool to re-engage), and the dream reports one domain (eml itself).

    public void DefineWorkspace(CogitoWorkspace workspace)
    {
        workspace.Define(
            "eml.targets.train_hit",
            "eml.targets.train_total",
            "eml.targets.held_hit",
            "eml.targets.held_total",
            "eml.census.exact",
            "eml.census.theorem",
            "eml.census.certs",
            "eml.census.values",
            "eml.laws.classes",
            "eml.laws.exact_claim_highwater",
            "eml.laws.generated_offers",
            "eml.laws.generated_mints",
            "eml.laws.direct_witness_matches",
            "eml.evaluator.calls",
            "eml.evaluator.history_complete",
            "eml.evaluator.offer_requests",
            "eml.evaluator.offer_calls",
            "eml.evaluator.ladder_requests",
            "eml.evaluator.ladder_cache_hits",
            "eml.evaluator.ladder_cache_misses",
            "eml.evaluator.ladder_calls",
            "eml.evaluator.ladder_executed_calls",
            "eml.evaluator.ood_probe_calls",
            "eml.evaluator.inverse_transforms",
            "eml.evaluator.hash_probes",
            "eml.evaluator.offered_join_hits",
            "eml.frontier.k",
            "eml.ruler.k",
            "eml.lift.windows",
            "eml.lift.lifts",
            "eml.lift.streak",
            "eml.lift.rate_e",
            "eml.lift.rate_ea");
        if (_actionSelection == EmlActionSelections.Off) return;
        workspace.Define(
            "eml.actions.decisions",
            "eml.actions.fresh_bias.selections", "eml.actions.fresh_bias.yield", "eml.actions.fresh_bias.evaluator_calls",
            "eml.actions.fresh_enum.selections", "eml.actions.fresh_enum.yield", "eml.actions.fresh_enum.evaluator_calls",
            "eml.actions.solve_hole.selections", "eml.actions.solve_hole.yield", "eml.actions.solve_hole.evaluator_calls",
            "eml.actions.counterexample.selections", "eml.actions.counterexample.yield", "eml.actions.counterexample.evaluator_calls",
            "eml.actions.compare.selections", "eml.actions.compare.yield", "eml.actions.compare.evaluator_calls",
            "eml.actions.fallbacks", "eml.actions.global_yield", "eml.actions.global_outcomes",
            "eml.actions.causes.bootstrap", "eml.actions.causes.yield", "eml.actions.causes.revival",
            "eml.actions.causes.fixed_schedule", "eml.actions.causes.abstention",
            "eml.procedure.started", "eml.procedure.completed", "eml.procedure.bound",
            "eml.procedure.shuffled", "eml.procedure.obligation_match", "eml.procedure.new_delta",
            "eml.procedure.canonical_deltas", "eml.procedure.variant", "eml.procedure.guards_passed",
            "eml.procedure.guards_skipped", "eml.procedure.guards_abstained",
            "eml.frontier.residual", "eml.frontier.epoch", "eml.frontier.first_generative_step", "eml.frontier.first_generative_decision",
            "eml.futility.attempts", "eml.futility.suppressions", "eml.futility.revivals",
            "eml.futility.resolved", "eml.futility.suppressed_calls", "eml.futility.cold",
            "eml.execution.admitted", "eml.execution.affirm_skips", "eml.hypothesis.cap_skips");
    }

    public void PostWorkspace(CogitoWorkspace workspace)
    {
        workspace.Post("eml.targets.train_hit", _sieve.TargetsHit());
        workspace.Post("eml.targets.train_total", _sieve.TrainTargetCount);
        workspace.Post("eml.targets.held_hit", _sieve.HeldCapturedCount);
        workspace.Post("eml.targets.held_total", _sieve.HeldTargetCount);
        workspace.Post("eml.census.exact", _sieve.ExactClasses);
        workspace.Post("eml.census.theorem", _sieve.TheoremClasses);
        workspace.Post("eml.census.certs", _sieve.DistinctCerts);
        workspace.Post("eml.census.values", _sieve.DistinctValues);
        workspace.Post("eml.laws.classes", LawCount);
        workspace.Post("eml.laws.exact_claim_highwater", _lawExactPredictionHighWater);
        workspace.Post("eml.laws.generated_offers", LawGeneratedOffers);
        workspace.Post("eml.laws.generated_mints", LawGeneratedMints);
        workspace.Post("eml.laws.direct_witness_matches", LawDirectWitnessMatches);
        workspace.Post("eml.laws.form_farm_attempted", LawFormFarmAttempted);
        workspace.Post("eml.laws.form_farm_accepted", LawFormFarmAccepted);
        workspace.Post("eml.laws.form_farm_rejected", LawFormFarmRejected);
        EmlEvaluatorClock clock = _sieve.EvaluatorClock;
        workspace.Post("eml.evaluator.calls", clock.ProgramPointEvaluations);
        workspace.Post("eml.evaluator.history_complete", clock.HistoryComplete ? 1 : 0);
        workspace.Post("eml.evaluator.offer_requests", clock.OfferRequests);
        workspace.Post("eml.evaluator.offer_calls", clock.OfferProgramPointEvaluations);
        workspace.Post("eml.evaluator.ladder_requests", clock.LadderRequests);
        workspace.Post("eml.evaluator.ladder_cache_hits", clock.LadderCacheHits);
        workspace.Post("eml.evaluator.ladder_cache_misses", clock.LadderCacheMisses);
        workspace.Post("eml.evaluator.ladder_calls", clock.LadderProgramPointEvaluations);
        workspace.Post("eml.evaluator.ladder_executed_calls", clock.ExecutedLadderProgramPointEvaluations);
        workspace.Post("eml.evaluator.ood_probe_calls", clock.OutOfDistributionProbeCalls);
        workspace.Post("eml.evaluator.inverse_transforms", clock.InverseTransforms);
        workspace.Post("eml.evaluator.hash_probes", clock.HashProbes);
        workspace.Post("eml.evaluator.offered_join_hits", clock.OfferedJoinHits);
        workspace.Post("eml.frontier.k", _sieve.KFrontier);
        workspace.Post("eml.ruler.k", Ruler);
        workspace.Post("eml.lift.windows", _lift?.Windows.Count ?? 0);
        workspace.Post("eml.lift.lifts", _lift?.Lifts.Count ?? 0);
        if (_lift?.Windows.Count > 0)
        {
            var w = _lift.Windows[^1];
            workspace.Post("eml.lift.streak", w.Streak);
            workspace.Post("eml.lift.rate_e", w.RateE);
            workspace.Post("eml.lift.rate_ea", w.RateEA);
        }
        else
        {
            workspace.Post("eml.lift.streak", 0);
            workspace.Post("eml.lift.rate_e", double.NaN);
            workspace.Post("eml.lift.rate_ea", double.NaN);
        }
        if (_actionSelection != EmlActionSelections.Off) PostActionWorkspace(workspace);
    }

    /// Bootstrap: enumerate the shallow shells (K ≤ seedK), offer each to the sieve, and accrete the discovered
    /// lines. This is the seed round both arms share — it rediscovers the paper's own reductions and gives the ON
    /// arm a starter grammar to bias from. The OFF arm's enumeration then continues from the first un-enumerated shell.
    public void Seed(Tape tape, Journal journal)
    {
        List<string> programs = EmlGen.Enumerate(1, _seedK).ToList();
        EmlOfferContext context = new(BeginWorldOpportunityBatch(programs.Count));
        foreach (string prog in programs) _sieve.Offer(prog, in context);
        Accrete(tape, journal, step: 0);
        _enum = EmlGen.Enumerate(_seedK + 2, _maxEnum).GetEnumerator();
    }

    public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        EmlOfferContext context = new(BeginWorldOpportunityBatch(batch));
        if (_actionSelection != EmlActionSelections.Off)
        {
            ResolveChunks(grammar);
            return new IntakeStep(0, Advanced: false, Domain: _sieve.KFrontier);
        }
        int kBefore = _sieve.KFrontier;
        if (_bias)
        {
            ResolveChunks(grammar);
            for (int b = 0; b < batch; b++)
                _sieve.Offer(_sampler.Next(_chunks!), in context);        // the three-rail draw (bias · uniform ε · the ε-enumeration sweep)
        }
        else
        {
            for (int b = 0; b < batch && !_enumDone; b++)
            {
                if (_enum!.MoveNext()) { _enumTaken++; _sieve.Offer(_enum.Current, in context); }
                else _enumDone = true;
            }
        }
        int got = Accrete(tape, journal, step).AppendedWeight;
        AdmitNewLaws(grammar, tape, journal, step);
        return new IntakeStep(got, Advanced: _sieve.KFrontier > kBefore, Domain: _sieve.KFrontier);
    }

    // one census-window close of the lift organ: fold the senses, maybe LIFT the K-ruler (the sampler is the
    // ruler's only home — sieve/grader/CAS are ruler-free), and CHECK the health invariant live: the census must
    // ride the lift instant unchanged (closure — a lift re-keys nothing) and may only grow after (monotone
    // accretion; the CAS is append-only). Fail-loud: a broken invariant means the ruler leaked into the witness.
    private void CloseLiftWindow(int step, Cortex? cortex)
    {
        int before = _sieve.TheoremClasses;
        RulerLiftProposal proposal = _lift!.SenseWindow(step, _sieve.ExactClasses, before, _sieve.TargetsHit(), SubResClasses(), GrokExactTier);
        RulerLiftChoice choice;
        if (cortex is null) choice = RulerLift.CreateLaunchpadChoice(in proposal);
        else
        {
            _lift.AdvancePolicyOutcomes(cortex, in proposal, _sieve.EvaluatorClock.ProgramPointEvaluations);
            choice = _lift.HasPendingLiftOutcome
                ? RulerLift.CreateLaunchpadChoice(in proposal)
                : _lift.Choose(cortex, in proposal, _sieve.EvaluatorClock.ProgramPointEvaluations);
        }
        int toK = RulerLift.ResolveRuler(in choice);
        if (toK > 0) _sampler.LiftMaxLen(toK);
        int committedRuler = _lift.Commit(in choice);
        if (committedRuler != toK)
            throw new InvalidOperationException($"ruler proposal committed {committedRuler}, sampler applied {toK}");
        if (_sieve.TheoremClasses != before)
            throw new InvalidOperationException($"lift broke census closure: {before} theorem-classes before the lift instant, {_sieve.TheoremClasses} after — the K-ruler re-keyed vested certificates");
        LiftWindow win = _lift.Windows[^1];
        Trace.Note(_lift.Line(in win));
        RecordAnytimeWindow(step, in win);
        if (toK <= 0) return;
        Trace.Note($"lift ▲ K-RULER {win.Verdict} · census {_sieve.ExactClasses}E/{before}E+A closed behind · bench {_sieve.TargetsHit()}/{_sieve.Targets.Count} · frontier re-opened (rail {(_sampler.RailReads.Done ? "dry" : $"sweeping K={_sampler.RailReads.Shell}")})");
    }

    private void RecordAnytimeWindow(int step, in LiftWindow win)
    {
        if (_anytimeRun is null) return;
        EmlEvaluatorClock clock = _sieve.EvaluatorClock;
        long absoluteCalls = clock.ProgramPointEvaluations;
        long localCalls = _anytimeFuelCursorPresent
            ? checked(absoluteCalls - _anytimeFuelCursor.EvaluatorCalls)
            : checked(absoluteCalls - _anytimeEvaluatorBaseline);
        long priorCalls = _anytimeCurve.Points.Count == 0 ? 0 : _anytimeCurve.Points[^1].EvaluatorIntervals;
        long windowCalls = checked(localCalls - priorCalls);
        if (windowCalls < 0) throw new InvalidDataException("EML evaluator clock regressed across anytime window");
        EmlDeliberationCounts absoluteFuel = ReadAnytimeFuelTotals();
        EmlDeliberationCounts cumulativeFuel = EmlDeliberationCounts.Subtract(in absoluteFuel, in _anytimeFuelBaseline);
        cumulativeFuel.ValidateNonnegative("cumulative fuel");
        EmlDeliberationCounts priorFuel = _anytimeCurve.Points.Count == 0
            ? EmlDeliberationCounts.Zero
            : _anytimeCurve.Points[^1].Fuel;
        EmlDeliberationCounts windowFuel = EmlDeliberationCounts.Subtract(in cumulativeFuel, in priorFuel);
        windowFuel.ValidateNonnegative("window fuel");
        EmlDeliberationCounts plannedFuel = _pairedFuelSchedule is { } pairedSchedule
            ? PairedFuelScheduleCursor.Validate(in pairedSchedule).Planned
            : ReadAnytimePlannedFuelTotals();
        plannedFuel = EmlDeliberationCounts.Subtract(in plannedFuel, in _anytimePlannedFuelBaseline);
        plannedFuel.ValidateNonnegative("planned fuel");
        EmlDeliberationCounts priorPlannedFuel = _anytimeCurve.PlannedFuel;
        EmlDeliberationCounts windowPlannedFuel = EmlDeliberationCounts.Subtract(in plannedFuel, in priorPlannedFuel);
        windowPlannedFuel.ValidateNonnegative("window planned fuel");
        EmlAnytimeCommitments absoluteCommitments = ReadAnytimeCommitments();
        EmlAnytimeCommitments commitments = EmlAnytimeCommitments.Subtract(in absoluteCommitments, in _anytimeCommitmentBaseline);
        commitments.Validate("window");
        double wallMilliseconds = CaptureAnytimeWindowWall(reset: true);
        string parentPointID = _anytimeCurve.Points.Count == 0 ? _anytimeParentPointID : _anytimeCurve.Digest;
        string evidenceMaterial = string.Join('|', _anytimeRun.Dir, step, win.RateE, win.RateEA, commitments, localCalls, parentPointID);
        string evidenceDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceMaterial)));
        EmlAnytimeBoundaryReceipt receipt = new(
            Path.GetFileName(_anytimeRun.Dir), _anytimeConfigID, _anytimeChainID, _anytimeArmID, parentPointID,
            _anytimeRung, step, win.Step / Math.Max(1, _lift!.Knobs.Window), "ruler.window.commit", commitments,
            cumulativeFuel, windowPlannedFuel, windowFuel, localCalls, windowCalls, windowCalls, true, clock.HistoryComplete,
            false, false, false, _lift.HasPendingLiftOutcome, true, false, 0, "", evidenceDigest,
            double.NaN, win.RateE, win.MeanzE, wallMilliseconds);
        EmlAnytimeCurvePoint point = _anytimeCurve.Append(in receipt);
        _ = _anytimeCurve.EvaluateKill(in receipt, in point);
        _anytimeCurve.WriteTSV(_anytimeRun.PathOf("eml_anytime_curve.tsv"));
    }

    /// Close the anytime authority at the physical run horizon. A lift outcome normally waits for three
    /// subsequent windows; a finite run may end before that observation horizon. Complete those decisions
    /// against the counters already present, append a final partial interval when the horizon falls between
    /// window closes, then append one zero-spend terminal point at that interval's coordinate. Repeated calls
    /// return the existing terminal point and cannot duplicate policy outcomes.
    internal EmlAnytimeCurvePoint? SettleAnytimeRunTerminal(Cortex cortex, int step)
    {
        if (_anytimeRun is null || _anytimeCurve.Points.Count < 2) return null;
        EmlAnytimeCurvePoint last = _anytimeCurve.Points[^1];
        if (last.RunTerminal)
        {
            if (_lift?.HasPendingPolicyOutcome == true)
                throw new InvalidDataException("terminal anytime point still carries pending RulerLift outcomes");
            return last;
        }

        if (step < last.PrefixStep)
            throw new InvalidDataException($"terminal anytime prefix regressed from {last.PrefixStep} to {step}");

        // Terminal policy settlement is state-only: it cannot mint EML work, but it must happen before
        // the final authority row so the row's pending-resolution bit is truthful on both fresh and resumed
        // runs.
        _lift?.SettlePendingPolicyOutcomes(cortex);
        if (_lift?.HasPendingPolicyOutcome == true)
            throw new InvalidDataException("terminal RulerLift settlement left pending outcomes");

        long absoluteEvaluatorCalls = _sieve.EvaluatorClock.ProgramPointEvaluations;
        long evaluatorIntervals = checked(absoluteEvaluatorCalls - _anytimeEvaluatorBaseline);
        EmlAnytimeCommitments absoluteCommitments = ReadAnytimeCommitments();
        EmlAnytimeCommitments commitments = EmlAnytimeCommitments.Subtract(in absoluteCommitments, in _anytimeCommitmentBaseline);
        commitments.Validate("terminal");
        EmlDeliberationCounts absoluteFuel = ReadAnytimeFuelTotals();
        EmlDeliberationCounts fuel = EmlDeliberationCounts.Subtract(in absoluteFuel, in _anytimeFuelBaseline);
        fuel.ValidateNonnegative("terminal fuel");
        EmlDeliberationCounts plannedFuel = _pairedFuelSchedule is { } pairedSchedule
            ? PairedFuelScheduleCursor.Validate(in pairedSchedule).Planned
            : ReadAnytimePlannedFuelTotals();
        plannedFuel = EmlDeliberationCounts.Subtract(in plannedFuel, in _anytimePlannedFuelBaseline);
        plannedFuel.ValidateNonnegative("terminal planned fuel");
        EmlDeliberationCounts priorPlannedFuel = _anytimeCurve.PlannedFuel;
        EmlDeliberationCounts windowPlannedFuel = EmlDeliberationCounts.Subtract(in plannedFuel, in priorPlannedFuel);
        windowPlannedFuel.ValidateNonnegative("terminal window planned fuel");
        EmlDeliberationCounts priorFuel = last.Fuel;
        EmlDeliberationCounts windowFuel = EmlDeliberationCounts.Subtract(in fuel, in priorFuel);
        windowFuel.ValidateNonnegative("terminal window fuel");
        long windowEvaluatorIntervals = checked(evaluatorIntervals - last.EvaluatorIntervals);
        if (windowEvaluatorIntervals < 0)
            throw new InvalidDataException("terminal anytime evaluator clock regressed");
        int physicalWindowIndex = step / Math.Max(1, _lift?.Knobs.Window ?? 1);
        bool sameCoordinate = step == last.PrefixStep
            && (physicalWindowIndex == last.WindowIndex || last.WindowIndex == physicalWindowIndex + 1);
        int windowIndex = sameCoordinate ? last.WindowIndex : checked(last.WindowIndex + 1);
        if (sameCoordinate && (!windowFuel.Equals(EmlDeliberationCounts.Zero) || windowEvaluatorIntervals != 0
            || !windowPlannedFuel.Equals(EmlDeliberationCounts.Zero) || commitments != last.Quality))
            throw new InvalidDataException("same-horizon terminal settlement changed its zero-spend coordinate");
        if (!sameCoordinate)
        {
            double partialWallMilliseconds = CaptureAnytimeWindowWall(reset: true);
            string partialEvidenceMaterial = string.Join('|', _anytimeRun.Dir, step, windowIndex,
                last.Digest, "ruler.window.commit", commitments, fuel, windowPlannedFuel,
                windowFuel, evaluatorIntervals, windowEvaluatorIntervals, partialWallMilliseconds);
            string partialEvidenceDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(partialEvidenceMaterial)));
            EmlAnytimeBoundaryReceipt partialReceipt = new(
                Path.GetFileName(_anytimeRun.Dir), _anytimeConfigID, _anytimeChainID, _anytimeArmID, last.Digest,
                _anytimeRung, step, windowIndex, "ruler.window.commit", commitments,
                fuel, windowPlannedFuel, windowFuel, evaluatorIntervals, windowEvaluatorIntervals, windowEvaluatorIntervals,
                true, _sieve.EvaluatorClock.HistoryComplete, false, false, false, false, true, false,
                last.GraceUntilWindow, "", partialEvidenceDigest, last.Residual, last.Rate, last.Meanz, partialWallMilliseconds);
            last = _anytimeCurve.Append(in partialReceipt);
            _ = _anytimeCurve.EvaluateKill(in partialReceipt, in last);
        }

        string evidenceMaterial = string.Join('|', _anytimeRun.Dir, step, windowIndex,
            last.Digest, "ruler.window.terminal", commitments, fuel, sameCoordinate ? "zero-spend" : "partial-terminal");
        string evidenceDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceMaterial)));
        EmlAnytimeBoundaryReceipt receipt = new(
            Path.GetFileName(_anytimeRun.Dir), _anytimeConfigID, _anytimeChainID, _anytimeArmID, last.Digest,
            _anytimeRung, step, windowIndex, "ruler.window.terminal", commitments,
            fuel, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, evaluatorIntervals, 0, 0,
            true, _sieve.EvaluatorClock.HistoryComplete, false, false, false, false, true, true,
            last.GraceUntilWindow, "", evidenceDigest, last.Residual, last.Rate, last.Meanz, 0);
        EmlAnytimeCurvePoint point = _anytimeCurve.Append(in receipt);
        _ = _anytimeCurve.EvaluateKill(in receipt, in point);
        _anytimeCurve.WriteTSV(_anytimeRun.PathOf("eml_anytime_curve.tsv"));
        return point;
    }

    private EmlAnytimeCommitments ReadAnytimeCommitments()
        => new(_sieve.ExactClasses, _sieve.TheoremClasses, _sieve.DistinctCerts, 0,
            _sieve.HeldCapturedCount, _sieve.TargetsHit(), LawCount, 0);

    private EmlDeliberationCounts ReadAnytimeFuelTotals()
    {
        if (_anytimeFuelCursorPresent)
            return ReadDeepRematchFuelTotals(in _anytimeFuelCursor, planned: false, refund: false);
        SyncAnytimeSettlementTotals();
        return _anytimeSettledActual;
    }

    // Running settlement totals — the journal is append-only between rewinds, so each window close folds
    // only the delta instead of re-walking every settlement from zero; a shrunk journal (speculative
    // rollback / checkpoint restore) resets the cursor and replays.
    private int _anytimeSettlementCursor;
    private EmlDeliberationCounts _anytimeSettledActual;
    private EmlDeliberationCounts _anytimeSettledPlanned;

    private void SyncAnytimeSettlementTotals()
    {
        IReadOnlyList<EmlDeliberationSettlement> settlements = _sieve.DeliberationJournal.Settlements;
        if (_anytimeSettlementCursor > settlements.Count)
        {
            _anytimeSettlementCursor = 0;
            _anytimeSettledActual = EmlDeliberationCounts.Zero;
            _anytimeSettledPlanned = EmlDeliberationCounts.Zero;
        }
        for (int i = _anytimeSettlementCursor; i < settlements.Count; i++)
        {
            _anytimeSettledActual = EmlDeliberationCounts.Add(in _anytimeSettledActual, settlements[i].Actual);
            _anytimeSettledPlanned = EmlDeliberationCounts.Add(in _anytimeSettledPlanned, settlements[i].Planned);
        }
        _anytimeSettlementCursor = settlements.Count;
    }

    private static EmlDeliberationCounts Counts(in EmlDeliberationQuota value)
        => new(value.CandidateEvaluations, value.LogicalProgramPoints, value.ExecutedProgramPoints, value.InverseTransforms,
            value.HashProbes, value.JoinAttempts, value.JoinHits, value.ProcessTerms, value.VerifierProgramPoints,
            value.CandidateSupplyItems, value.LawRewriteApplications, value.LawRewriteTreeNodes);

    private EmlDeliberationCounts ReadAnytimePlannedFuelTotals()
    {
        if (_anytimeFuelCursorPresent)
            return ReadDeepRematchFuelTotals(in _anytimeFuelCursor, planned: true, refund: false);
        SyncAnytimeSettlementTotals();
        return _anytimeSettledPlanned;
    }

    private double CaptureAnytimeWindowWall(bool reset)
    {
        if (_anytimeWindowStartedTicks == 0) _anytimeWindowStartedTicks = Stopwatch.GetTimestamp();
        double elapsed = _anytimeWindowElapsedMilliseconds + Stopwatch.GetElapsedTime(_anytimeWindowStartedTicks).TotalMilliseconds;
        if (!double.IsFinite(elapsed) || elapsed < 0) throw new InvalidDataException("EML anytime window wall time is invalid");
        if (reset)
        {
            _anytimeWindowElapsedMilliseconds = 0;
            _anytimeWindowStartedTicks = Stopwatch.GetTimestamp();
        }
        return elapsed;
    }

    // margin-mass on the σ-line: A-classes whose drift the enclosure proved ≠ 0 yet sits below the witness ruler
    // (EmlCert.SubResolution — no readable law at this σ). The lift organ's witness-axis sense, one CAS walk per
    // window close.
    private int SubResClasses()
    {
        int n = 0;
        foreach (var kv in _sieve.Cas) if (kv.Key.Grade == 'A' && kv.Key.RateRe == EmlCert.SubResolution) n++;
        return n;
    }

    // the exact-tier grok read — the −0.70/cvz lock tier of the box-completion gate (lazy: RulerLift invokes it
    // only once the census leg is sustained, so the E-tier induce never rides the common window).
    private (double MeanZ, double CvZ, int Kz) GrokExactTier()
    {
        var bytes = _sieve.TierBytes(m => m.Grade == 'E');
        if (bytes.Length == 0) return (double.NaN, double.NaN, 0);
        var st = Engine.RenormStats(Engine.Induce(bytes).Result);
        return (st.MeanZ, st.CvZ, st.KZ);
    }

    /// The MIX rail — re-append the base-case axiom line (deterministic), keeping the primitives {1, eml} anchored in
    /// the working grammar after the corpus grows (the self-data analog of 's permanent extrinsic anchor:
    /// the dream's "reality" is the operator itself, so the anchor is its shallowest EXACT identity — the ladder
    /// certified it, so it re-ingests as Reflected evidence, exactly like the corpus MIX re-ingests Real).
    // grammar + affirmCut are UNCONSULTED: the intake-affirm gate is the WORLD mouth's (skip a corpus span the grammar
    // already generates — ). This is the INTRINSIC mouth — the axiom re-anchor keeps the operator's
    // shallowest EXACT identity mounted regardless of how well the grammar parses it (the dream's "reality" IS the
    // operator, cadence-free; affirming it away would starve the primitive the pump re-induces against). Ungated by design.
    public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
    {
        if (_anchor is null) return;
        var bytes = Encoding.ASCII.GetBytes(_anchor);
        var sid = tape.Append(bytes, "axiom", Provenances.Reflected);
        journal.Ingest(step, sid, "axiom", bytes);
    }

    /// CHECKPOINT — the dream's whole memory: the sieve record (canon · mint log · basins · bench), the three-rail
    /// sampler (LCG + the ε-enumeration rail's cursor), and the OFF arm's enumeration CURSOR (`_enumTaken`
    /// successful MoveNexts — the walk is deterministic, so LoadState replays the position instead of serializing
    /// a live enumerator). The seed-shell offers are NOT re-run on load (they live in the serialized sieve —
    /// re-offering would inflate FiniteOffers/basins). The chunk cache is identity-keyed and rebuilds off the
    /// restored grammar.
    public void SaveState(CkptWriter w)
    {
        // the RULER-STATE section — armed mounts ONLY, so every pre-lift/disarmed checkpoint stays byte-identical
        // (the Vow); the section tag is the fail-loud gate against a config/format mismatch on resume. It leads
        // the stream because LoadState must adopt the lifted ruler BEFORE _sampler.Load rebuilds the rail at it.
        if (_lift is not null) { w.Section(LiftTag); w.I32(_sampler.MaxLen); _lift.Save(w); }
        w.Section(AnytimeTag); _anytimeCurve.Save(w);
        if (_anytimeFuelCursorPresent)
        {
            w.Section(AnytimeFuelCursorTag);
            WriteDeepRematchFuelCursor(w, in _anytimeFuelCursor);
        }
        if (_pairedFuelSchedule is { } pairedSchedule)
        {
            w.Section(PairedFuelScheduleTag);
            w.Bytes(EmlPairedFuelScheduleJournal.Encode(in pairedSchedule, PairedFuelScheduleCursor));
        }
        if (_ordinaryRung0StateLoaded || _rung0Opportunities > 0 || _rung0Audits > 0 || _relationNullExecutions > 0
            || _rung0CarrierBoundCandidates > 0 || _relationNullPairsConsidered > 0 || _rung0FunnelReceipts.Count > 0
            || _rung0CompositionDigest != 0)
        {
            w.Section(OrdinaryRung0Tag);
            w.I32(5);
            w.I32(_rung0Opportunities);
            w.I32(_rung0CarrierBoundCandidates); w.I32(_rung0GuardEligibleCandidates);
            w.I32(_rung0PaidAttempts); w.I32(_rung0AttemptedCandidates);
            w.I32(_rung0Compositions); w.I32(_rung0ZeroEvaluatorCompositions);
            w.I32(_rung0Audits); w.I32(_rung0AgreedAudits); w.I32(_rung0DisagreedAudits); w.I32(_rung0NotSelectedAudits);
            w.I32(_relationNullExecutions); w.I32(_relationNullDivergences); w.I32(_relationNullAuthorityPredictions);
            w.I32(_relationNullPairsConsidered); w.I32(_relationNullPairsCreated);
            w.I32(_relationNullRejectNoCarrier); w.I32(_relationNullRejectShape); w.I32(_relationNullRejectGrade);
            w.U64(_rung0CompositionDigest); w.Str(_rung0SourceDigest); w.Str(_rung0ConfigDigest);
            w.I32(_rung0FunnelReceipts.Count);
            for (int i = 0; i < _rung0FunnelReceipts.Count; i++)
            {
                EmlRung0FunnelReceipt receipt = _rung0FunnelReceipts[i];
                w.I32((int)receipt.Stage); w.I32(receipt.ObligationPredictionID.Value); w.Str(receipt.ObligationID ?? string.Empty);
                w.Str(receipt.RuleID.Value ?? string.Empty); w.Bool(receipt.Accepted); w.Str(receipt.Reason ?? string.Empty);
                w.Str(receipt.ProofID ?? string.Empty); w.Str(receipt.AuditID ?? string.Empty); w.Str(receipt.AdmissionID ?? string.Empty); w.Str(receipt.ClosureID ?? string.Empty);
                w.I64(receipt.Evaluation.Start); w.I64(receipt.Evaluation.End);
                w.Bool(receipt.RelationNullDonor.HasValue);
                if (receipt.RelationNullDonor is EmlRelationNullDonorProvenance donor)
                {
                    w.I32(donor.SourcePredictionID.Value); w.Str(donor.ObligationID);
                    w.I32(donor.SupportEventIDs.Count);
                    for (int j = 0; j < donor.SupportEventIDs.Count; j++) w.I64(donor.SupportEventIDs[j].Value);
                    w.I32(donor.LawAdmissionIDs.Count);
                    for (int j = 0; j < donor.LawAdmissionIDs.Count; j++) w.Str(donor.LawAdmissionIDs[j]);
                }
                w.U8((byte)receipt.AuditSelection);
            }
        }
        if (_freezeSamplingGrammar) SaveSamplingGrammar(w);
        _sieve.Save(w);
        _sampler.Save(w);
        w.Bool(_enumDone); w.I32(_enumTaken);
        w.I32(_minted);
        w.Bool(_anchor is not null);
        if (_anchor is not null) w.Str(_anchor);
        SaveActionState(w);
        SaveLawState(w);
        SaveProcessConstantState(w);
        w.Section(WorldOpportunityTag);
        w.I32(_worldOpportunityCursor);
        w.Section(WorldOpportunityEventsTag);
        w.I32(_worldOpportunityEvents.Length);
        for (int i = 0; i < _worldOpportunityEvents.Length; i++)
            w.I64(_worldOpportunityEvents[i].Value);
    }

    public void LoadState(CkptReader r)
    {
        if (_lift is not null) { r.Expect(LiftTag); _sampler.AdoptRuler(r.I32()); _lift.Load(r); }
        r.Expect(AnytimeTag); if (!_anytimeCurve.TryLoad(r)) throw new InvalidDataException("missing typed anytime curve checkpoint");
        _anytimeFuelCursorPresent = r.TryExpect(AnytimeFuelCursorTag);
        if (_anytimeFuelCursorPresent)
            _anytimeFuelCursor = ReadDeepRematchFuelCursor(r).Validate();
        if (_anytimeFuelCursorPresent && (_anytimeCurve.Points.Count == 0 || !_anytimeCurve.Points[0].IsHandshake
            || _anytimeCurve.Points[0].PointID != _anytimeFuelCursor.PointID
            || _anytimeCurve.Points[0].Digest != _anytimeFuelCursor.PointDigest))
            throw new InvalidDataException("anytime checkpoint handshake point/cursor identity mismatch");
        bool pairedFuelSchedulePresent = r.TryExpect(PairedFuelScheduleTag);
        if (pairedFuelSchedulePresent)
        {
            (EmlPairedFuelSchedule restoredSchedule, EmlPairedFuelScheduleCursor restoredCursor) =
                EmlPairedFuelScheduleJournal.Decode(r.Bytes());
            if (_pairedFuelSchedule is not { } pairedSchedule)
                throw new InvalidDataException("ordinary checkpoint unexpectedly carries paired fuel schedule state");
            if (restoredSchedule != pairedSchedule)
                throw new InvalidDataException("paired fuel schedule checkpoint disagrees with configured schedule");
            _pairedFuelCursor = restoredCursor.Validate(in restoredSchedule);
        }
        else if (_pairedFuelSchedule is not null)
            throw new InvalidDataException("configured paired fuel schedule is missing from checkpoint");
        _ordinaryRung0StateLoaded = r.TryExpect(OrdinaryRung0Tag);
        if (_ordinaryRung0StateLoaded)
        {
            int ordinarySchema = r.I32();
            if (ordinarySchema is not (1 or 2 or 3 or 4 or 5)) throw new InvalidDataException("unsupported ordinary rung-0 checkpoint schema");
            _rung0Opportunities = r.I32();
            if (ordinarySchema >= 2)
            {
                _rung0CarrierBoundCandidates = r.I32(); _rung0GuardEligibleCandidates = r.I32();
                _rung0PaidAttempts = r.I32(); _rung0AttemptedCandidates = r.I32();
            }
            else
            {
                _rung0CarrierBoundCandidates = 0; _rung0GuardEligibleCandidates = 0;
                _rung0PaidAttempts = 0; _rung0AttemptedCandidates = 0;
            }
            _rung0Compositions = r.I32(); _rung0ZeroEvaluatorCompositions = r.I32();
            _rung0Audits = r.I32(); _rung0AgreedAudits = r.I32(); _rung0DisagreedAudits = r.I32(); _rung0NotSelectedAudits = r.I32();
            _relationNullExecutions = r.I32(); _relationNullDivergences = r.I32(); _relationNullAuthorityPredictions = r.I32();
            if (ordinarySchema >= 2)
            {
                _relationNullPairsConsidered = r.I32(); _relationNullPairsCreated = r.I32();
                _relationNullRejectNoCarrier = r.I32(); _relationNullRejectShape = r.I32(); _relationNullRejectGrade = r.I32();
            }
            else
            {
                _relationNullPairsConsidered = 0; _relationNullPairsCreated = 0;
                _relationNullRejectNoCarrier = 0; _relationNullRejectShape = 0; _relationNullRejectGrade = 0;
            }
            _rung0CompositionDigest = r.U64();
            string sourceDigest = r.Str(); string configDigest = r.Str();
            if (!string.Equals(sourceDigest, _rung0SourceDigest, StringComparison.Ordinal)
                || !string.Equals(configDigest, _rung0ConfigDigest, StringComparison.Ordinal))
                throw new InvalidDataException("ordinary rung-0 checkpoint digest configuration drifted");
            _rung0FunnelReceipts.Clear();
            if (ordinarySchema >= 3)
            {
                int receiptCount = r.I32();
                if (receiptCount < 0 || receiptCount > 1_000_000)
                    throw new InvalidDataException("ordinary rung-0 funnel receipt count is invalid");
                for (int i = 0; i < receiptCount; i++)
                {
                    int stage = r.I32();
                    if (stage < (int)EmlRung0FunnelStages.Opportunity || stage > (int)EmlRung0FunnelStages.RelationNull)
                        throw new InvalidDataException("ordinary rung-0 funnel receipt stage is invalid");
                    EmlRung0FunnelReceipt receipt = new(
                        (EmlRung0FunnelStages)stage,
                        new EmlPredictionID(r.I32()),
                        r.Str(),
                        new EmlRuleID(r.Str()),
                        r.Bool(),
                        r.Str(),
                        r.Str(), r.Str(), r.Str(), r.Str(),
                        new EmlEvaluatorInterval(r.I64(), r.I64()),
                        ordinarySchema >= 4 && r.Bool()
                            ? ReadRelationNullDonorProvenance(r)
                            : null,
                        ordinarySchema >= 5 ? (EmlRung0AuditSelectionSpecies)r.U8() : EmlRung0AuditSelectionSpecies.DigestCadence);
                    _rung0FunnelReceipts.Add(receipt);
                }
            }
            if (_rung0Opportunities < 0 || _rung0CarrierBoundCandidates < 0 || _rung0GuardEligibleCandidates < 0
                || _rung0PaidAttempts < 0 || _rung0AttemptedCandidates < 0
                || _rung0Compositions < 0 || _rung0ZeroEvaluatorCompositions < 0
                || _rung0Audits < 0 || _rung0AgreedAudits < 0 || _rung0DisagreedAudits < 0 || _rung0NotSelectedAudits < 0
                || _rung0Compositions > _rung0AttemptedCandidates || _rung0ZeroEvaluatorCompositions > _rung0Compositions
                || _rung0Audits != _rung0AgreedAudits + _rung0DisagreedAudits + _rung0NotSelectedAudits
                || _relationNullDivergences < 0 || _relationNullDivergences > _relationNullExecutions
                || _relationNullAuthorityPredictions < 0 || _relationNullAuthorityPredictions > _relationNullExecutions
                || _rung0GuardEligibleCandidates > _rung0CarrierBoundCandidates
                || _relationNullPairsConsidered < 0 || _relationNullPairsCreated < 0
                || _relationNullRejectNoCarrier < 0 || _relationNullRejectShape < 0 || _relationNullRejectGrade < 0
                || _relationNullPairsCreated > _relationNullPairsConsidered)
                throw new InvalidDataException("ordinary rung-0 checkpoint counters do not close");
            _checkpointRung0FunnelReceiptCount = _rung0FunnelReceipts.Count;
        }
        else
        {
            _rung0Opportunities = 0; _rung0CarrierBoundCandidates = 0; _rung0GuardEligibleCandidates = 0;
            _rung0PaidAttempts = 0; _rung0AttemptedCandidates = 0;
            _rung0Compositions = 0; _rung0ZeroEvaluatorCompositions = 0;
            _rung0Audits = 0; _rung0AgreedAudits = 0; _rung0DisagreedAudits = 0; _rung0NotSelectedAudits = 0;
            _relationNullExecutions = 0; _relationNullDivergences = 0; _relationNullAuthorityPredictions = 0;
            _relationNullPairsConsidered = 0; _relationNullPairsCreated = 0;
            _relationNullRejectNoCarrier = 0; _relationNullRejectShape = 0; _relationNullRejectGrade = 0;
            _rung0CompositionDigest = 0;
            _rung0FunnelReceipts.Clear();
            _checkpointRung0FunnelReceiptCount = 0;
        }
        // Monotonic wall time is process-local telemetry: a checkpoint does not claim the paused interval.
        // BindAnytimeRun rebases counters from the restored last point and starts a fresh stopwatch segment.
        _anytimeEvaluatorBaseline = 0;
        _anytimeCommitmentBaseline = default;
        _anytimeWindowElapsedMilliseconds = 0;
        _anytimeParentPointID = _anytimeCurve.Points.Count == 0 ? "" : _anytimeCurve.Points[0].ParentPointID;
        _anytimeWindowStartedTicks = Stopwatch.GetTimestamp();
        if (_freezeSamplingGrammar) LoadSamplingGrammar(r);
        _sieve.Load(r);
        _sampler.Load(r);
        _enumDone = r.Bool(); _enumTaken = r.I32();
        _minted = r.I32();
        _anchor = r.Bool() ? r.Str() : null;
        _enum = EmlGen.Enumerate(_seedK + 2, _maxEnum).GetEnumerator();   // Seed never runs on resume — rebuild the continuation and fast-forward the deterministic walk
        for (int i = 0; i < _enumTaken; i++) _enum.MoveNext();
        _chunkRules = null;
        if (!_freezeSamplingGrammar) _chunks = null;
        LoadActionState(r);
        LoadLawState(r);
        if (_ordinaryRung0StateLoaded)
        {
            // The store retains one audit per proof digest; the ordinary receipt counts
            // every closed execution, including later executions that reuse that audit.
            if (_lawStore.Rung0Audits.Count > _rung0Audits)
                throw new InvalidDataException("ordinary rung-0 checkpoint has fewer audit executions than retained proof audits");
            int agreed = 0, disagreed = 0, notSelected = 0;
            for (int i = 0; i < _lawStore.Rung0Audits.Count; i++)
            {
                switch (_lawStore.Rung0Audits[i].Status)
                {
                    case EmlRung0AuditStatuses.Agreed: agreed++; break;
                    case EmlRung0AuditStatuses.Disagreed: disagreed++; break;
                    case EmlRung0AuditStatuses.NotSelected: notSelected++; break;
                    default: throw new InvalidDataException("ordinary rung-0 law store carries an unknown audit status");
                }
            }
            if (agreed > _rung0AgreedAudits || disagreed > _rung0DisagreedAudits || notSelected > _rung0NotSelectedAudits)
                throw new InvalidDataException("ordinary rung-0 checkpoint status executions do not cover retained proof audits");
        }
        if (_anytimeFuelCursorPresent)
        {
            _ = ReadDeepRematchFuelTotals(in _anytimeFuelCursor, planned: true, refund: false);
            _ = ReadDeepRematchFuelTotals(in _anytimeFuelCursor, planned: false, refund: false);
            _ = ReadDeepRematchFuelTotals(in _anytimeFuelCursor, planned: false, refund: true);
        }
        LoadProcessConstantState(r);
        if (r.TryExpect(WorldOpportunityTag))
        {
            _worldOpportunityCursor = r.I32();
            if (_worldOpportunityCursor < 0)
                throw new InvalidDataException("ReplayCalc world opportunity cursor is negative");
            if (r.TryExpect(WorldOpportunityEventsTag))
            {
                int count = r.I32();
                if (count < 0 || count > 1_000_000)
                    throw new InvalidDataException($"invalid world opportunity lineage count {count}");
                _worldOpportunityEvents = new TapeEventID[count];
                for (int i = 0; i < count; i++)
                {
                    TapeEventID current = new(r.I64());
                    if (i > 0 && current.Value <= _worldOpportunityEvents[i - 1].Value)
                        throw new InvalidDataException("ReplayCalc checkpoint world opportunity lineage is not strictly increasing");
                    _worldOpportunityEvents[i] = current;
                }
                if (_worldOpportunityCursor > count)
                    throw new InvalidDataException("ReplayCalc checkpoint world opportunity cursor exceeds its lineage prefix");
            }
            else
                _worldOpportunityEvents = Array.Empty<TapeEventID>();
        }
        else
        {
            _worldOpportunityCursor = 0;
            _worldOpportunityEvents = Array.Empty<TapeEventID>();
        }
        _currentWorldOpportunityEvents = Array.Empty<TapeEventID>();
        _checkpointAnytimePointCount = _anytimeCurve.Points.Count;
        _checkpointAnytimeKillCount = _anytimeCurve.Kills.Count;
    }

    private static EmlRelationNullDonorProvenance ReadRelationNullDonorProvenance(CkptReader r)
    {
        int sourcePrediction = r.I32();
        string obligationID = r.Str();
        int supportCount = r.I32();
        if (sourcePrediction < 0 || obligationID.Length == 0 || supportCount <= 0 || supportCount > 4096)
            throw new InvalidDataException("ordinary rung-0 relation-null donor provenance is invalid");
        TapeEventID[] supports = new TapeEventID[supportCount];
        for (int i = 0; i < supports.Length; i++)
        {
            long value = r.I64();
            if (value < 0) throw new InvalidDataException("ordinary rung-0 relation-null donor support event is invalid");
            supports[i] = new TapeEventID(value);
        }
        int lawCount = r.I32();
        if (lawCount <= 0 || lawCount > 4096) throw new InvalidDataException("ordinary rung-0 relation-null donor law provenance is invalid");
        string[] laws = new string[lawCount];
        for (int i = 0; i < laws.Length; i++)
        {
            laws[i] = r.Str();
            if (laws[i].Length == 0) throw new InvalidDataException("ordinary rung-0 relation-null donor law provenance is empty");
        }
        return new(new EmlPredictionID(sourcePrediction), obligationID, supports, laws);
    }

    private static void WriteDeepRematchFuelCursor(CkptWriter writer, in EmlDeepRematchFuelCursor cursor)
    {
        writer.I32(1); writer.I32(cursor.SettlementCount); writer.I64(cursor.EvaluatorCalls);
        EmlDeliberationCounts planned = cursor.Planned;
        EmlDeliberationCounts actual = cursor.Actual;
        EmlDeliberationCounts refund = cursor.Refund;
        WriteCursorCounts(writer, in planned); WriteCursorCounts(writer, in actual); WriteCursorCounts(writer, in refund);
        writer.Str(cursor.Digest); writer.Str(cursor.PointID); writer.Str(cursor.PointDigest); writer.Str(cursor.SettlementDigest);
    }

    private static EmlDeepRematchFuelCursor ReadDeepRematchFuelCursor(CkptReader reader)
    {
        if (reader.I32() != 1) throw new InvalidDataException("unsupported deep-rematch EML fuel cursor schema");
        int settlementCount = reader.I32(); long evaluatorCalls = reader.I64();
        EmlDeliberationCounts planned = ReadCursorCounts(reader); EmlDeliberationCounts actual = ReadCursorCounts(reader); EmlDeliberationCounts refund = ReadCursorCounts(reader);
        return new(settlementCount, evaluatorCalls, planned, actual, refund, reader.Str(), reader.Str(), reader.Str(), reader.Str());
    }

    private static void WriteCursorCounts(CkptWriter writer, in EmlDeliberationCounts counts)
    {
        writer.I64(counts.CandidateEvaluations); writer.I64(counts.LogicalProgramPoints); writer.I64(counts.ExecutedProgramPoints);
        writer.I64(counts.InverseTransforms); writer.I64(counts.HashProbes); writer.I64(counts.JoinAttempts); writer.I64(counts.JoinHits);
        writer.I64(counts.ProcessTerms); writer.I64(counts.VerifierProgramPoints); writer.I64(counts.CandidateSupplyItems);
        writer.I64(counts.LawRewriteApplications); writer.I64(counts.LawRewriteTreeNodes);
    }

    private static EmlDeliberationCounts ReadCursorCounts(CkptReader reader)
        => new(reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64());

    /// The ε-enumeration rail's live cursor — the observatory's coverage read (systematic candidates issued · the
    /// shell being swept · dry).
    public (int Taken, int Shell, bool Done) RailReads => _sampler.RailReads;

    /// THE CERT-GATED ACCRETION LAW — the ONE weight authority both EML mouths pay (ReplayCalc.Accrete and
    /// Campfire.QueueFresh; a fork here would let the farm survive at one mouth). Precedence: the CORROBORATION
    /// reward first — the FIRST E-witness of a REGISTERED named target re-ingests at corrobW (once per target
    /// ever, so it cannot farm); then CERT-NOVELTY gates everything else — a FIRST-capture (new certificate
    /// class) pays certW (repetition IS weight to Re-Pair: genuine discovery amplifies), a PARAPHRASE pays ZERO.
    /// A starved paraphrase still lands in the CORPUS (graded, CAS-counted, scored) — it just stops feeding the
    /// TAPE the generator re-induces. certW ≤ 1
    /// disarms the gate (every mint ×1 — byte-identical legacy, the kill-line control arm).
    internal static int AccreteWeight(in EmlMint m, bool first, int corrobW, int certW)
        => m.Corrob && m.Grade == 'E' ? corrobW
         : certW <= 1                 ? 1
         : first                      ? certW
         : 0;

    // accrete the sieve's fresh mints onto the tape + journal (the discovery is the intake); returns how many
    // spans landed. THE GRADE ROUTES THE PROVENANCE (the Reflection Law's math organ): an EXACT mint is witness-
    // certified by the machine's own carried evaluator → Reflected (born evidence, full count weight under an
    // armed wScale); everything else (A/S/D/U) stays Replay — hypothesis at ε until reality corroborates,
    // GC-evictable first. THE WEIGHT IS AccreteWeight's — the corroboration reward (second witness PAYS,
    // , Weitzman-shaped: this is what KEEPS the off-basin shells the enumeration rail reaches) over
    // the cert-novelty gate.
    internal EmlAccretion Accrete(Tape tape, Journal journal, int step, List<TapeEventID>? eventIDs = null, Cortex? cortex = null)
    {
        int n = 0;
        EmlDeltaSummary deltas = CollectPendingDeltas();
        IReadOnlyList<EmlMint> mints = _sieve.NewMints;
        for (int i = 0; i < mints.Count; i++)
        {
            EmlMint m = mints[i];
            bool first = _sieve.NewMintFirst(i);
            int w = AccreteWeight(in m, first, _corrobW, _certW);
            EmlPredictionID claimID = _sieve.NewMintPredictionID(i);
            if (w > 0)
            {
                int appended = 0;
                for (int j = 0; j < w; j++)
                {
                    if (m.Grade != 'E' && cortex is not null && !cortex.CanAppendReplay())
                    {
                        _hypothesisCapSkips++;
                        continue;
                    }
                    TapeEventID eventID = TapePacketCreator.AppendEmlMint(tape, journal, step, in m, "node0");
                    _sieve.BindMintEvent(in m, eventID);
                    eventIDs?.Add(eventID);
                    appended++;
                }
                n += appended;
            }
            else if (m.Grade == 'E'
                && _sieve.TryReadMintOpportunityEvents(claimID, out IReadOnlyList<TapeEventID> opportunities)
                && opportunities.Count > 0
                && !_sieve.TryReadPredictionMintEvent(claimID, out _))
            {
                // Accretion weight governs grammar intake, not whether world-backed evidence exists on the tape.
                TapeEventID eventID = TapePacketCreator.AppendEmlMint(tape, journal, step, in m, "node0");
                _sieve.BindPredictionEvent(claimID, eventID);
                eventIDs?.Add(eventID);
            }
            if (m.Grade == 'E') _anchor ??= m.Line;                      // the MIX anchor must itself be certified — never re-anchor on an asymptotic
        }
        _minted += n;
        int counterexamples = 0;
        if (_pendingCounterexample is not null)
        {
            TapeEventID counterexampleID = TapePacketCreator.AppendEmlCounterexample(tape, journal, step, _pendingCounterexample);
            eventIDs?.Add(counterexampleID);
            _pendingCounterexample = null;
            counterexamples = 1;
        }
        _sieve.DrainNewMints();
        _sieve.DrainSemanticDeltas();
        for (int i = 0; i < _pendingRung0Closures.Count; i++)
        {
            EmlPredictionID sourcePredictionID = _pendingRung0Closures[i];
            if (!_sieve.TryReadRung0ComposedFormClosure(sourcePredictionID, out EmlRung0ComposedFormObligationEvidence evidence))
                throw new InvalidDataException($"rung-0 closure {sourcePredictionID.Value} disappeared before tape emission");
            EmlObligationTargetSpecies targetSpecies = _sieve.TryReadExactCompositionObligation(sourcePredictionID, out _)
                ? EmlObligationTargetSpecies.ExactComposition
                : EmlObligationTargetSpecies.Residual;
            IReadOnlyList<TapeEventID> targetSupports = targetSpecies == EmlObligationTargetSpecies.ExactComposition
                && _sieve.TryReadExactCompositionObligation(sourcePredictionID, out EmlExactCompositionObligation exactTarget)
                ? exactTarget.Supports : Array.Empty<TapeEventID>();
            IReadOnlyList<string> lawAdmissions = _lawStore.TryReadRung0BasisLawAdmissionIDs(sourcePredictionID, out IReadOnlyList<EmlRung0BasisLawIdentity> basis)
                ? basis.Select(static id => id.AdmissionID).ToArray() : Array.Empty<string>();
            TapeEventID derivationEventID = TapePacketCreator.AppendEmlRung0Closure(tape, journal, step, evidence, "derivation", targetSpecies, targetSupports, lawAdmissions);
            TapeEventID displacedEventID = TapePacketCreator.AppendEmlRung0Closure(tape, journal, step, evidence, "displaced", targetSpecies, targetSupports, lawAdmissions);
            // A rung-0 result is admitted by its derivation packet, not by a
            // mint packet. Keep that provenance typed so later powered support
            // custody can prove the actual admission path.
            _sieve.BindComposedPredictionEvent(evidence.ComposedPredictionID, derivationEventID);
            eventIDs?.Add(derivationEventID);
            eventIDs?.Add(displacedEventID);
            if (cortex?.LoopLineage is LoopLineageTurnstile lineage)
            {
                IReadOnlyList<LoopLineageNode> lawNodes = Array.Empty<LoopLineageNode>();
                bool chainEligible = _sieve.TryReadTargetEvents(sourcePredictionID, out IReadOnlyList<TapeEventID> opportunityEvents, out _, out _)
                    && opportunityEvents.Count > 0
                    && _lawStore.TryReadRung0BasisLawAdmissionIDs(sourcePredictionID, out IReadOnlyList<EmlRung0BasisLawIdentity> basisLawIdentities)
                    && cortex.TryGetRung0BasisLaws(basisLawIdentities, out lawNodes);
                if (!chainEligible) continue;
                LoopLineageNodeID[] lawPredecessors = lawNodes.Select(static node => node.NodeID)
                    .Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray();
                LoopLineageCausalID causalID = lawPredecessors.Length == 1
                    ? lawNodes[0].CausalID
                    : LoopLineageCausalID.Merge(LoopLineageNodeSpecies.Rung0Composition, lawPredecessors);
                GrammarRevisionID? preFoldRevision = cortex.InstallRevision?.Revision;
                bool emittedComposition = lineage.TryEmit(step, LoopLineageNodeSpecies.Rung0Composition, derivationEventID,
                    preFoldRevision, lawPredecessors, causalID);
                LoopLineageEdgeReceipt? derivationEdge = emittedComposition ? lineage.Receipts[^1] : null;
                bool emittedDisplaced = emittedComposition && lineage.TryEmit(step, LoopLineageNodeSpecies.DisplacedEvaluation, displacedEventID,
                    predecessorIDs: [derivationEdge!.Node.NodeID], causalID: causalID);
                if (!emittedComposition)
                    throw new InvalidDataException("registered loop-closure rung-0 lineage emission did not close");
                if (!emittedDisplaced)
                    throw new InvalidDataException("registered loop-closure displaced lineage emission did not close");
                if (derivationEdge is LoopLineageEdgeReceipt emittedRung0)
                    cortex.RegisterLoopClosureComposition(emittedRung0);
                if (derivationEdge is LoopLineageEdgeReceipt edge)
                    cortex.WriteLoopClosurePattern(
                        evidence.AdmissionID,
                        new PatternBecameThoughtCorroboration(
                            sourcePredictionID,
                            evidence.ComposedPredictionID,
                            edge.Node.NodeID,
                            new LoopClosureDigest(evidence.ProofSHA256),
                            new LoopClosureDigest(evidence.AuditSHA256),
                            evidence.MainEvaluatorCalls,
                            evidence.ComparatorEvaluation.Calls,
                            targetSpecies,
                            targetSupports,
                            lawAdmissions));
            }
        }
        _pendingRung0Closures.Clear();
        EmlOrdinaryRunRung0Receipt receipt = ReadOrdinaryRunRung0Receipt();
        TapePacketCreator.AppendEmlOrdinaryRunRung0Receipt(tape, journal, step, in receipt);
        _lastAccretion = new EmlAccretion(
            n,
            _lastDeltas.Count,
            deltas.FirstCaptures,
            deltas.RepresentativeImprovements,
            deltas.TargetCaptures,
            counterexamples);
        if (_lastDeltas.Count > 0) _actionBatchHadCanonicalDelta = true;
        if (_actionSelection != EmlActionSelections.Off && _actionInFlight)
        {
            int arm = (int)_currentActionArm;
            _actionFirstCaptures[arm] += deltas.FirstCaptures;
            if (_lastDeltas.Count > 0) _actionDeltaOutcomes[arm]++;
        }
        _accretionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        return _lastAccretion;
    }

    private EmlDeltaSummary CollectPendingDeltas()
    {
        int firstCaptures = 0;
        int representativeImprovements = 0;
        int targetCaptures = 0;
        _lastDeltas.Clear();
        EmlEvaluatorInterval evaluation = _sieve.EvaluatorClock.MeasureFrom(_accretionEvaluatorStart);
        IReadOnlyList<EmlCertificateDelta> semanticDeltas = _sieve.NewSemanticDeltas;
        for (int i = 0; i < semanticDeltas.Count; i++)
        {
            EmlCertificateDelta delta = semanticDeltas[i];
            _lastDeltas.Add(delta);
            if (delta.Change == EmlCertificateChanges.ProofAttached) targetCaptures++;
        }
        IReadOnlyList<EmlMint> mints = _sieve.NewMints;
        for (int i = 0; i < mints.Count; i++)
        {
            EmlMint mint = mints[i];
            EmlCert certificate = _sieve.NewMintCert(i);
            EmlPredictionID claimID = _sieve.NewMintPredictionID(i);
            if (_sieve.NewMintFirst(i))
            {
                firstCaptures++;
                _lastDeltas.Add(new EmlCertificateDelta(
                    EmlCertificateChanges.ClassOpened,
                    claimID,
                    null,
                    certificate,
                    Evaluation: evaluation,
                    DescriptionBits: 0));
            }
            else if (_sieve.NewMintRepresentativeChanged(i))
            {
                representativeImprovements++;
                _lastDeltas.Add(new EmlCertificateDelta(
                    EmlCertificateChanges.RepresentativeImproved,
                    claimID,
                    certificate,
                    certificate,
                    Evaluation: evaluation,
                    DescriptionBits: 0));
            }
            if (mint.Corrob && mint.Grade == 'E')
            {
                targetCaptures++;
                _lastDeltas.Add(new EmlCertificateDelta(
                    EmlCertificateChanges.TargetCaptured,
                    claimID,
                    certificate,
                    certificate,
                    Evaluation: evaluation,
                    DescriptionBits: 0));
            }
        }
        return new EmlDeltaSummary(firstCaptures, representativeImprovements, targetCaptures);
    }

    private readonly record struct EmlDeltaSummary(
        int FirstCaptures,
        int RepresentativeImprovements,
        int TargetCaptures);

    public void AppendProbeSamples(List<byte[]> samples) => samples.Add("eml-rpn-1\n"u8.ToArray());
}

/// The lowered EML generation + reward knob set. Public callers configure EmlGenerationConfig; mounts and Cortex
/// lower into this compact transport before constructing the curriculum. Masses compose outermost-first: the
/// ε-enumeration rail takes EpsEnum of every draw; the uniform floor takes Eps of the remainder; the bias rail
/// keeps the rest (EmlSampler's fork order). CertW is the cert-novelty accretion gate (first-capture ×W ·
/// paraphrase ×0; ≤1 = off — ReplayCalc.AccreteWeight, the one law both mouths pay). Lift arms the ruler organ
/// (RulerLift — MaxLen becomes a LIVE ruler the box-completion gate raises); default = disarmed, rulers pinned.
internal readonly record struct EmlKnobs(int SeedK, int MaxLen, int MaxEnum, int Units, int Gain, double Eps, double EpsEnum, int CorrobW, int CertW, LiftKnobs Lift = default)
{
    /// The runtime/campfire mount defaults — the observatory's own defaults, frozen as constants (ReplayCalc's knob block).
    public static EmlKnobs Mount => new(ReplayCalc.MountSeedK, ReplayCalc.MountMaxLen, ReplayCalc.MountMaxEnum,
                                        ReplayCalc.MountUnits, ReplayCalc.MountGain, ReplayCalc.MountEps,
                                        ReplayCalc.MountEpsEnum, ReplayCalc.MountCorrobW, ReplayCalc.MountCertW);
}

public sealed class EmlGenerationConfig
{
    public int SeedShells { get; init; } = ReplayCalc.MountSeedK;
    public int MaxLength { get; init; } = ReplayCalc.MountMaxLen;
    public int MaxEnumerationLength { get; init; } = ReplayCalc.MountMaxEnum;
    public int SampleUnits { get; init; } = ReplayCalc.MountUnits;
    public int ChunkGain { get; init; } = ReplayCalc.MountGain;
    public double UniformEpsilon { get; init; } = ReplayCalc.MountEps;
    public double EnumerationEpsilon { get; init; } = ReplayCalc.MountEpsEnum;
    public int CorroborationWeight { get; init; } = ReplayCalc.MountCorrobW;
    public int CertificateWeight { get; init; } = ReplayCalc.MountCertW;

    internal EmlKnobs ToKnobs(LiftKnobs lift = default) => new(
        SeedK: SeedShells,
        MaxLen: MaxLength,
        MaxEnum: MaxEnumerationLength,
        Units: SampleUnits,
        Gain: ChunkGain,
        Eps: UniformEpsilon,
        EpsEnum: EnumerationEpsilon,
        CorrobW: CorroborationWeight,
        CertW: CertificateWeight,
        Lift: lift);

}

public sealed partial class ReplayCalc
{
    private static CortexEmlCurriculum RequireEmlCurriculum(CortexConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (config.Curriculum is not CortexEmlCurriculum eml)
            throw new ArgumentException("CortexConfig.Curriculum must be CortexEmlCurriculum.", nameof(config));
        if (eml.Generation is null) throw new ArgumentException("CortexEmlCurriculum.Generation is required.", nameof(config));
        if (eml.Lift is null) throw new ArgumentException("CortexEmlCurriculum.Lift is required.", nameof(config));
        return eml;
    }

    internal static int RunLiftObservatory(CortexConfig config)
    {
        var eml = RequireEmlCurriculum(config);
        int steps = config.Steps;
        int batch = eml.IntakeBatch;
        int sig = eml.SignatureDigits;
        double strf = eml.Lift.StrideFraction;
        ulong seed = config.Seed;
        var lift = eml.Lift.ToKnobs();
        var k = eml.Generation.ToKnobs(lift);

        var run = Cogito.Run.New("dreamlift");
        Trace.Note($"dreamlift · THE LIFT LADDER · {steps}×{batch} · ruler {k.MaxLen}→≤{lift.KMax} ×{lift.Factor:F2}/lift · gate: rate≤{lift.Frac:F2}×peak ⌂ sustained {lift.Sustain}×{lift.Window}-step windows{(lift.CensusOnly ? "" : $" ∧ exact-tier lock (|meanz+0.70|≤{lift.MeanzBand:F2}{(lift.LockMeanz ? ", cvz telegraphed not gating" : ", k-aware cvz")})")} · stride {strf:P0} of tape · sig {sig}");

        var sieve = new EmlSieve(sig);
        var dream = new ReplayCalc(sieve, bias: true, k, seed);
        var tape = new Tape(); var journal = new Journal();
        dream.Seed(tape, journal);

        var (_, _, g) = Engine.Induce(tape);
        long lastBytes = tape.GrammarByteLength;
        double cvz = Engine.RenormStats(g).CvZ;
        var sparks = new List<LiftSpark>(steps);
        var capturedAt = new int[sieve.Targets.Count];      // first E-capture step per named target (−1 = never; the sealed-class table's readout)
        Array.Fill(capturedAt, -1);
        for (int i = 0; i < sieve.Targets.Count; i++) if (sieve.BestK(i) >= 0) capturedAt[i] = 0;   // seed round

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int step = 0; step < steps; step++)
        {
            if (tape.GrammarByteLength - lastBytes >= Math.Max(800L, (long)(strf * tape.GrammarByteLength)))
            {
                (_, _, g) = Engine.Induce(tape); lastBytes = tape.GrammarByteLength; cvz = Engine.RenormStats(g).CvZ;
            }
            dream.Draw(g, tape, journal, step, batch);
            if (dream._lift is not null && dream._lift.IsWindowDue(step + 1)) dream.CloseLiftWindow(step + 1, cortex: null);
            for (int i = 0; i < capturedAt.Length; i++) if (capturedAt[i] < 0 && sieve.BestK(i) >= 0) capturedAt[i] = step;
            sparks.Add(new LiftSpark(sieve.Identities + sieve.ValueHits, sieve.KFrontier, cvz, sieve.DistinctCerts, sieve.TargetsHit(), sieve.TheoremClasses, sieve.ExactClasses, dream.Ruler));
            if (step > 0 && step % 200 == 0)
                Trace.Note($"{sw.ElapsedMilliseconds}ms · lift · step {step}/{steps} · ruler {dream.Ruler} · census {sieve.ExactClasses}E/{sieve.TheoremClasses} · bench {sieve.TargetsHit()}/{sieve.Targets.Count} · K {sieve.KFrontier} · grammar {tape.GrammarByteLength / 1024}KB");
        }

        Report(run, dream, sieve, sparks, capturedAt, steps, batch, in k, sig);
        return 0;
    }

    private readonly record struct LiftSpark(int Identities, int KFrontier, double CvZ, int Certs, int Bench, int Census, int CensusE, int Ruler);

    private static void Report(Run run, ReplayCalc dream, EmlSieve sieve, List<LiftSpark> sparks, int[] capturedAt, int steps, int batch, in EmlKnobs k, int sig)
    {
        var lift = dream.Lift!;
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine("════════════════════════════════════════════════════════════════════════════════════════════════");
        o.AppendLine("  THE LIFT LADDER — the machine that lifts its own rulers");
        o.AppendLine($"    budget {steps}×{batch}={steps * batch} candidates · mount ruler {k.MaxLen} · ceiling {lift.Knobs.KMax} · ×{lift.Knobs.Factor:F2}/lift");
        o.AppendLine($"    gate   census plateau (rate ≤ {lift.Knobs.Frac:F2}×peak ⌂, {lift.Knobs.Sustain}×{lift.Knobs.Window}-step windows){(lift.Knobs.CensusOnly ? "  [census-only — grok stage ablated]" : $" ∧ exact-tier −0.70/cvz lock (band {lift.Knobs.MeanzBand:F2})")}");
        o.AppendLine();

        // ── THE TOWER WALK — the run's whole arc: ruler staircase over the census/rate clocks ──
        o.AppendLine("  ── THE TOWER WALK (per step / per window) ──");
        o.AppendLine($"     ruler       {ReplayCalc.Spark(sparks.Select(s => (double)s.Ruler))}   (the staircase — each step up is a LIFT)");
        o.AppendLine($"     census(E)   {ReplayCalc.Spark(sparks.Select(s => (double)s.CensusE))}   (the VESTED box census — the completion clock's integral)");
        o.AppendLine($"     E-rate      {ReplayCalc.Spark(lift.Windows.Select(w => (double)w.RateE))}   (new EXACT classes per {lift.Knobs.Window}-step window — climb → plateau → re-ignite)");
        o.AppendLine($"     census(E+A) {ReplayCalc.Spark(sparks.Select(s => (double)s.Census))}   (the whole frontier — the A-font rides it, structurally plateau-free)");
        o.AppendLine($"     K-frontier  {ReplayCalc.Spark(sparks.Select(s => (double)s.KFrontier))}");
        o.AppendLine($"     paper-bench {ReplayCalc.Spark(sparks.Select(s => (double)s.Bench))}");
        o.AppendLine($"     CvZ grok    {ReplayCalc.Spark(sparks.Select(s => s.CvZ))}");
        o.AppendLine();

        // ── the lift trajectory — the organ's own journal ──
        o.AppendLine($"  ── THE LIFT TRAJECTORY — {lift.Lifts.Count} lift(s) fired · {lift.Windows.Count} windows closed{(lift.AtCeiling ? " · CEILING reached" : "")} ──");
        if (lift.Lifts.Count == 0)
            o.AppendLine("     (no lift fired — the box never completed at this budget: read the window journal below)");
        else
        {
            o.AppendLine("     lift   step   ruler      censusE@lift (E+A)   E-rate before (line) → after1 / after3   re-ignited   bench@lift");
            for (int i = 0; i < lift.Lifts.Count; i++)
            {
                var e = lift.Lifts[i];
                o.AppendLine($"     {i + 1,4}  {e.Step,5}   {e.FromK,3} → {e.ToK,-3}  {e.CensusE,10} ({e.Census,5})   {e.RateBefore,8:F0} ({e.Line,5:F1}) → {Col(e.RateAfter1),6} / {Col(e.RateAfter3),6}   {(double.IsNaN(e.RateAfter3) ? "pending " : e.Reignited ? "YES ✓   " : "NO ✗    ")}  {e.Bench,3}/{sieve.Targets.Count}");
            }
        }
        o.AppendLine();

        // ── the window journal tail — the gate's last reads (the full journal rides liftwins.tsv) ──
        o.AppendLine("  ── the window journal (last 8 closes) ──");
        foreach (var w in lift.Windows.TakeLast(8))
            o.AppendLine($"     {lift.Line(w)}");
        o.AppendLine();

        // ── THE SEALED CLASS — the ruler-bound bench targets (sealed at mount ONLY by MountMaxLen) ──
        int mountK = k.MaxLen;
        o.AppendLine($"  ── THE SEALED CLASS — named targets ruler-bound at mount (paper K > {mountK}): does the lift unseal them? ──");
        o.AppendLine("     target      paperK   foundK   captured@step   verdict");
        int sealedN = 0, flipped = 0;
        for (int i = 0; i < sieve.Targets.Count; i++)
        {
            var t = sieve.Targets[i];
            if (t.PaperK <= mountK) continue;
            sealedN++;
            int bk = sieve.BestK(i);
            if (bk >= 0) flipped++;
            string paper = (t.PaperTimedOut ? ">" : "") + t.PaperK;
            o.AppendLine($"     {t.Label,-10} {paper,6}   {(bk >= 0 ? bk.ToString() : "—"),6}   {(capturedAt[i] >= 0 ? capturedAt[i].ToString() : "—"),13}   {(bk >= 0 ? $"FLIPPED — E-captured under the lifted ruler{(bk <= t.PaperK && !t.PaperTimedOut ? $" (K {bk} vs paper {t.PaperK})" : "")}" : "still sealed at this budget")}");
        }
        o.AppendLine($"     ⇒ {flipped}/{sealedN} ruler-bound targets FLIPPED · full bench {sieve.TargetsHit()}/{sieve.Targets.Count} (mount-reachable + unsealed)");
        o.AppendLine();

        // ── THE HEALTH INVARIANT — novelty conserved across lifts, census closed behind them ──
        int reignited = lift.Lifts.Count(e => e.Reignited);
        int settled = lift.Lifts.Count(e => !double.IsNaN(e.RateAfter3));
        o.AppendLine("  ── THE HEALTH INVARIANT — novelty conserved across lifts, census closed behind them ──");
        o.AppendLine($"     census CLOSED    every lift instant asserted live (theorem-classes ride the lift unchanged — the K-ruler");
        o.AppendLine($"                      lives in the SAMPLER; sieve/grader/CAS are ruler-free, so no vested certificate re-keys");
        o.AppendLine($"                      and the old box re-checks under the same witness law) · census monotone E {sparks[0].CensusE}→{sparks[^1].CensusE} · E+A {sparks[0].Census}→{sparks[^1].Census}");
        o.AppendLine($"     novelty CONSERVED {reignited}/{settled} settled lift(s) re-ignited (after3 back above the completion line that fired the lift){(lift.Lifts.Count > settled ? $" · {lift.Lifts.Count - settled} pending (ran out of windows)" : "")}");
        if (settled > 0 && reignited == 0)
            o.AppendLine("     ⇒ LIFTS FIRED BUT NOVELTY DID NOT RE-IGNITE — falsifies the colimit's compositionality at this budget (diagnose, don't force)");
        else if (reignited > 0)
            o.AppendLine("     ⇒ THE TOWER WALK IS REAL — box complete → lift → frontier re-opened → census climbing again");
        o.AppendLine();

        // ── the σ-axis (witness-axis) — the margin-mass sense + the pinned-medium readout ──
        o.AppendLine($"  ── THE σ-AXIS (secondary) — margin-mass on the resolution line: {lift.SigmaDue} window(s) σ-due ──");
        o.AppendLine($"     sense: share of new A-certificates in the SUB-RESOLUTION band (drift proven ≠ 0 by the enclosure yet");
        o.AppendLine($"     below the σ-ruler). Actuator PINNED at mount: sig={sig} IS Eml.Q's collision-free packing bound (≤9);");
        o.AppendLine($"     lifting σ needs the packed-key medium widened first — flagged as the follow-up organ, never forced here.");
        o.AppendLine();

        Console.Write(o.ToString());

        // durable corpus — the arc is the object of study
        var lt = new StringBuilder("lift\tstep\tfromk\ttok\tcensus_e\tcensus\tbench\trate_before\tline\trate_after1\trate_after3\treignited\n");
        for (int i = 0; i < lift.Lifts.Count; i++)
        { var e = lift.Lifts[i]; lt.AppendLine($"{i + 1}\t{e.Step}\t{e.FromK}\t{e.ToK}\t{e.CensusE}\t{e.Census}\t{e.Bench}\t{e.RateBefore:F1}\t{e.Line:F2}\t{Col(e.RateAfter1)}\t{Col(e.RateAfter3)}\t{(e.Reignited ? 1 : 0)}"); }
        run.Write("lifts.tsv", lt.ToString());
        var wt = new StringBuilder("step\truler\trate_e\trate_ea\trate_home\tpeak_home\tstreak\tmeanz_e\tcvz_e\tkz_e\tsubres_share\tsigma_due\tverdict\n");
        foreach (var w in lift.Windows)
            wt.AppendLine($"{w.Step}\t{w.Ruler}\t{w.RateE}\t{w.RateEA}\t{w.RateHome:F2}\t{w.PeakHome:F2}\t{w.Streak}\t{F4(w.MeanzE)}\t{F4(w.CvzE)}\t{w.KzE}\t{w.SubResShare:F3}\t{(w.SigmaDue ? 1 : 0)}\t{w.Verdict}");
        run.Write("liftwins.tsv", wt.ToString());
        var st = new StringBuilder("step\tidentities\tkfrontier\tcvz\tcerts\tbench\tcensus\tcensus_e\truler\n");
        for (int i = 0; i < sparks.Count; i++)
        { var s = sparks[i]; st.AppendLine($"{i}\t{s.Identities}\t{s.KFrontier}\t{(double.IsNaN(s.CvZ) ? "nan" : s.CvZ.ToString("F4"))}\t{s.Certs}\t{s.Bench}\t{s.Census}\t{s.CensusE}\t{s.Ruler}"); }
        run.Write("sparklines.tsv", st.ToString());
        var bench = new StringBuilder("target\tcat\tpaperK\tpaperTimedOut\tk\tcaptured_step\tprog\n");
        for (int i = 0; i < sieve.Targets.Count; i++)
        { var t = sieve.Targets[i]; bench.AppendLine($"{t.Label}\t{t.Cat}\t{t.PaperK}\t{t.PaperTimedOut}\t{sieve.BestK(i)}\t{capturedAt[i]}\t{sieve.BestProg(i) ?? ""}"); }
        run.Write("bench.tsv", bench.ToString());
        run.Write("mints_on.txt", Encoding.ASCII.GetString(sieve.TierBytes(_ => true)));
        run.Write("mints_exact_on.txt", Encoding.ASCII.GetString(sieve.TierBytes(m => m.Grade == 'E')));
    }

    private static string Col(double v) => double.IsNaN(v) ? "—" : v.ToString("F0");
    private static string F4(double v) => double.IsNaN(v) ? "nan" : v.ToString("F4");
}

public sealed class EmlLiftGateConfig
{
    public int MaxRuler { get; init; } = 200;
    public double Factor { get; init; } = 1.4;
    public int Window { get; init; } = 50;
    public int Sustain { get; init; } = 3;
    public double Fraction { get; init; } = 0.25;
    public double MeanzBand { get; init; } = 0.35;
    public double StrideFraction { get; init; } = 0.05;
    public bool CensusOnly { get; init; }
    public bool LockMeanz { get; init; }

    internal LiftKnobs ToKnobs() => new(MaxRuler, Factor, Window, Sustain, Fraction, MeanzBand, CensusOnly, LockMeanz);
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE OBSERVATORY — `cogito dreamcalc` (its own home, like GrokBell.KillLine / Scoreboard.Run — not the Cli bag)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

public sealed partial class ReplayCalc
{
    /// usage: cogito dreamcalc [--steps N] [--batch M] [--seedk K] [--maxlen L] [--maxenum E] [--units U] [--gain G]
    ///                         [--sig S] [--stride B] [--seed HEX] [--top T] [--polmix F] [--polenum F] [--corrob W] [--certw W]
    ///        cogito dreamcalc --regrade <run> [--sig S] [--p3x F] [--p3y F] [--file mints_on.txt]
    ///        cogito dreamcalc --semantic-compress <run> [--sig S] [--p3x F] [--p3y F] [--file mints_on.txt]
    ///        cogito dreamcalc --anneal-len [--kmax K] [--anneal-win W] [--anneal-sustain S] [--anneal-frac F] … (EML lift —
    ///                        THE LIFT LADDER: MaxLen becomes a LIVE ruler the box-completion gate raises)
    ///   Replays identities out of eml(x,y)=exp(x)−ln(y): two matched-budget arms (ON = grammar-biased under the
    ///   three-rail policy MIX — chunk-bias for depth, uniform ε for support, the systematic ε-enumeration sweep
    ///   for coverage — OFF = pure enumeration = the paper's exhaustive search), the sparkline suite, the
    ///   sanity/chunking/noisy-TV kill-lines, the minimal-program bench vs the paper's Table (leaf_count),
    ///   THE GRADE-GATE (every mint graded live by the witness ladder — the `=`/`~` alphabet, the E/A/S/D/U census,
    ///   the exact-tier renorm, the anomaly register), THE CORROBORATION REWARD (`--corrob W`: a corroborated-EXACT
    ///   value-hit re-ingests at weight W — the second witness pays; 1 = off), THE CERT-GATED ACCRETION
    ///   (`--certw W`: a certificate FIRST-capture accretes ×W, a paraphrase ×0 — the farm starves at the tape;
    ///   1 = off), THE FULL-FRONTIER CENSUS (theorem-classes E+A as the growing target set — the paper-32 a
    ///   labeled subset), and THE DISCOVERY READOUT — the self-model scoring its own mints (novelty = surprise ×
    ///   compression × depth × rarity), the rediscovery/frontier split against the charted atlas, and the top-T
    ///   discoveries rendered human-readable.
    ///   `--polmix 0 --polenum 0 --corrob 1 --certw 1` is the pure-bias control arm (the proven basin trap —
    ///   support excluded outside the chunk-closure). Deterministic — same seed, same discoveries. `--regrade` re-offers
    ///   an EXISTING run's mint corpus through the retro witness ladder (EmlRegrade — no search re-run) and emits
    ///   the graded corpus + canon-taint + correction targets. `--semantic-compress` keys an EXISTING corpus by
    ///   theorem certificate and emits the compression ratio + class histogram — the discovery
    ///   process auditing its own novelty by compressing it.
    internal static int RunObservatory(CortexConfig config, int top)
    {
        var eml = RequireEmlCurriculum(config);

        int steps = config.Steps;
        int batch = eml.IntakeBatch;
        int sig = eml.SignatureDigits;
        int stride = config.Stride.ReinduceBytes;
        ulong seed = config.Seed;
        var k = eml.Generation.ToKnobs();
        var run = Cogito.Run.New("dreamcalc");
        Trace.Note($"dreamcalc · eml(x,y)=exp(x)−ln(y) · seed K≤{k.SeedK} shared · {steps}×{batch} cand/arm · maxlen {k.MaxLen} · sig {sig} · dual-point sieve + live grade ladder · rails ε-uniform={k.Eps:F3} ε-enum={k.EpsEnum:F3} · corrob ×{k.CorrobW} · cert-gate ×{k.CertW}");

        var on  = DriveArm("ON",  bias: true,  steps, batch, k, sig, stride, seed);
        var off = DriveArm("OFF", bias: false, steps, batch, k, sig, stride, seed);

        Report(run, on, off, steps, batch, k, sig, top);
        return 0;
    }

    private sealed record ArmRun(string Label, EmlSieve Sieve, byte[] Tape, List<ReplaySpark> Sparks, long Candidates,
                                 List<MintPulse> Pulses, Engine.GrammarCover FinalCover, RePairResult FinalGrammar,
                                 (int Taken, int Shell, bool Done) Rail);
    private readonly record struct ReplaySpark(int Identities, int KFrontier, int Constants, double CvZ, int Certs, int Bench, int Census);

    /// One mint's LIVE reading, taken the step it fired: the step (−1 = the shared seed round, before any grammar
    /// exists), the RAW residual — ParsedSize under the grammar that generated the batch, per byte (1.0 = no rule
    /// covered any of it; →0 = the machine was speaking in named subroutines) — and the EXCURSION: that residual
    /// against the machine's own running EMA of it, squashed to (0,1) via x/(1+x). The excursion form is the
    /// ABLATION-SENSES `_excBand` anti-bodge made per-mint: a surprise only counts as one relative to the machine's
    /// OWN baseline (the newborn's uniform S=1 lands neutral 0.5; a mint that still startles a mature grammar spikes).
    private readonly record struct MintPulse(int Step, double Residual, double Excursion);

    /// One fully-scored discovery (computed at report time, once hindsight exists). N = S·C·D·R, every factor read
    /// off the machine itself:
    ///   S = the excursion pulse (surprise vs the machine's own running baseline — the trunk's ExcMint per-mint);
    ///   C = MDL rent, two legs multiplied: hindsight-capture (1 − ParsedSize_final/len — an isolated fluke stays
    ///       incompressible and pays 0) × COLLAPSE (|K_lhs − K_rhs|/max — the identity's own rewrite saving: a deep
    ///       tower equalling a short form does real compression WORK; blob = blob at ΔK≈0 pays nothing; a value-hit
    ///       collapses to a NAME, K_rhs = 1);
    ///   D = K/maxlen (the shell depth) · R = value-basin self-information (attractors like 0/1/e → 0).
    /// S×C is compression PROGRESS — surprising at mint, lawful in hindsight. Correctness is the sieve's only to its
    /// WITNESS DEPTH (dual-point sig — scale-absorption-blind; witness-grades vest via `--regrade`, EmlRegrade);
    /// novelty is the machine's own surprise at its own mint.
    /// N is the STRING-space read, kept as the farm detector's contrast; the RANKING currency is `Bits` — the
    /// certificate-space novelty: `First` marks the class's first capture (it pays the class's self-
    /// information), an MDL-improving member pays its ΔK, every other paraphrase pays ZERO.
    private readonly record struct Discovery(int Idx, int Step, string Prog, string Line,
                                             double S, double C, double D, double R, double N, string? Chart,
                                             double Bits, bool First);

    // drive one arm through the trunk's own induce→read→draw loop (stride-gated re-induce over the minted identity
    // tape — the O(Δ) discipline the trunk runs), sampling the sparkline suite each step. The grok read (CvZ over the
    // identity corpus) is recomputed only when the grammar re-inducts (once per stride), carried between strides.
    // Each fresh mint is PULSED the step it lands — ParsedSize under the very grammar that generated the batch (one
    // GrammarCover hoisted per induce, the documented perf seam) — so the surprise reading is taken by the self-model
    // that was actually in charge, not reconstructed in hindsight. The OFF null runs REWARD-FREE and GATE-FREE
    // (CorrobW + CertW neutralized): both weights are ON-policy — the null's tape stays the un-weighted
    // enumeration record.
    private static ArmRun DriveArm(string label, bool bias, int steps, int batch, in EmlKnobs knobs, int sig, int stride, ulong seed)
    {
        var k = bias ? knobs : knobs with { CorrobW = 1, CertW = 1 };
        var sieve = new EmlSieve(sig);
        var dream = new ReplayCalc(sieve, bias, k, seed);
        var tape = new Tape(); var journal = new Journal();

        var pulses = new List<MintPulse>();
        var cover = new Engine.GrammarCover([]);          // the newborn self-model — no rules, everything is a surprise
        double emaS = double.NaN;                         // long EMA of the raw residual — the machine's own surprise baseline
        const double EmaAlpha = 2.0 / (256 + 1);          // ~256-mint window; the readout is insensitive to ±2× (a LONG baseline per the _excBand seam)
        void PulseFresh(int step)
        {
            for (int i = pulses.Count; i < sieve.MintLog.Count; i++)
            {
                var bytes = Encoding.ASCII.GetBytes(sieve.MintLog[i].Line);
                double res = (double)cover.ParsedSize(bytes) / bytes.Length;
                if (double.IsNaN(emaS)) emaS = res;       // seed the baseline on the first mint (the newborn is its own home)
                double x = res / Math.Max(1e-9, emaS);    // excursion ratio vs the machine's OWN running baseline
                pulses.Add(new MintPulse(step, res, x / (1 + x)));
                emaS += EmaAlpha * (res - emaS);
            }
        }

        dream.Seed(tape, journal);
        PulseFresh(step: -1);                             // the shared seed round: residual 1 against no grammar → excursion lands neutral (0.5)

        var (_, _, g) = Engine.Induce(tape);
        cover = new Engine.GrammarCover(g.Rules);
        long lastBytes = tape.GrammarByteLength;
        double cvZ = Engine.RenormStats(g).CvZ;
        var sparks = new List<ReplaySpark>(steps);
        long candidates = 0;

        for (int step = 0; step < steps && !dream.Drained; step++)
        {
            if (tape.GrammarByteLength - lastBytes >= stride)
            {
                (_, _, g) = Engine.Induce(tape); lastBytes = tape.GrammarByteLength; cvZ = Engine.RenormStats(g).CvZ;
                cover = new Engine.GrammarCover(g.Rules);
            }
            dream.Draw(g, tape, journal, step, batch);
            PulseFresh(step);
            candidates += batch;
            sparks.Add(new ReplaySpark(sieve.Identities + sieve.ValueHits, sieve.KFrontier, sieve.ConstantsHit(), cvZ, sieve.DistinctCerts, TargetsHit(sieve), sieve.TheoremClasses));
            if (step > 0 && step % 200 == 0)                              // the long-run heartbeat — a black-box dream is untraceable
                Trace.Note($"{label} · step {step}/{steps} · mints {sieve.MintLog.Count} · K {sieve.KFrontier} · grammar {tape.GrammarByteLength / 1024}KB");
        }

        // the HINDSIGHT self-model — one final induce over everything the run dreamt (the C factor's denominator)
        var final = tape.Concat();
        var (_, _, gf) = Engine.Induce(tape);
        return new ArmRun(label, sieve, final, sparks, candidates, pulses, new Engine.GrammarCover(gf.Rules), gf, dream.RailReads);
    }

    private static void Report(Run run, ArmRun on, ArmRun off, int steps, int batch, in EmlKnobs knobs, int sig, int top)
    {
        int seedK = knobs.SeedK, maxLen = knobs.MaxLen;
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine("════════════════════════════════════════════════════════════════════════════════════════════════");
        o.AppendLine("  THE EML DREAM-CALCULATOR — eml(x,y)=exp(x)−ln(y) + the constant 1 generates the scientific calculator");
        o.AppendLine($"    probes  P1=(γ,A)=({EmlSieve.Gamma:F6},{EmlSieve.Glaisher:F6})  P2=(G,ζ3)=({EmlSieve.Catalan:F6},{EmlSieve.Apery:F6})   sig={sig}, dual-point Schanuel sieve");
        o.AppendLine($"    budget  seed K≤{seedK} (shared) · then {steps}×{batch}={steps * batch} candidates/arm · maxlen {maxLen}");
        o.AppendLine($"    policy  rails: bias {(1 - knobs.EpsEnum) * (1 - knobs.Eps):F3} · uniform ε {(1 - knobs.EpsEnum) * knobs.Eps:F3} · enum-sweep ε {knobs.EpsEnum:F3} · corrob reward ×{knobs.CorrobW} · cert-gate {(knobs.CertW <= 1 ? "OFF" : $"first ×{knobs.CertW} · paraphrase ×0")}");
        o.AppendLine();

        // ── per-arm summary ──
        o.AppendLine("  arm   candidates   minted(id+val)   distinct-vals   K-frontier   constants-reached");
        foreach (var a in new[] { on, off })
            o.AppendLine($"   {a.Label,-4} {a.Candidates,10}   {a.Sieve.Identities + a.Sieve.ValueHits,14}   {a.Sieve.DistinctValues,13}   {a.Sieve.KFrontier,10}   {a.Sieve.ConstantsHit(),3}/{CountCat(a.Sieve, EmlCats.Constant)}");
        o.AppendLine();

        // ── THE GRADE-GATE — the live witness-ladder census + the exact-tier RG read ──
        o.AppendLine("  ── THE GRADE-GATE — every mint graded LIVE (E exact · A asymptotic · S scale-local · D domain · U unwitnessable) ──");
        o.AppendLine("  arm   mints      E      A      S      D      U   false-exact(A+S)   anomalies(reg/hit)");
        foreach (var a in new[] { on, off })
        {
            int mints = a.Sieve.MintLog.Count;
            int fx = a.Sieve.GradeCount('A') + a.Sieve.GradeCount('S');
            o.AppendLine($"   {a.Label,-4} {mints,6}  {a.Sieve.GradeCount('E'),5}  {a.Sieve.GradeCount('A'),5}  {a.Sieve.GradeCount('S'),5}  {a.Sieve.GradeCount('D'),5}  {a.Sieve.GradeCount('U'),5}   {(mints == 0 ? 0 : 100.0 * fx / mints),6:F1}%           {a.Sieve.Anomalies.Count,4}/{a.Sieve.AnomalyHits}");
        }
        o.AppendLine();

        // ── the coverage rail + the corroboration reward (ON) — the measure and the second witness, live reads ──
        o.AppendLine("  ── THE ε-ENUMERATION RAIL + CORROBORATION REWARD (ON) — systematic measure · the second witness pays ──");
        o.AppendLine($"     rail   {on.Rail.Taken,6} systematic candidates issued → sweeping shell K={on.Rail.Shell}{(on.Rail.Done ? " (ruler swept dry)" : "")}");
        o.AppendLine($"     reward {on.Sieve.CorrobExact(),6} first-capture (novel-corroborated EXACT) value-hits re-ingested ×{knobs.CorrobW} (OFF, reward-free: {off.Sieve.CorrobExact()})");
        o.AppendLine();

        // ── THE SEMANTIC REGISTER — the mint log content-addressed by theorem certificate. The compression
        // ratio audits the discovery process itself (honest search → incompressible; Goodharted → paraphrase
        // families crush), and the explore signal upgrades to NEW-CERTIFICATE rate, which paraphrase cannot feed.
        o.AppendLine("  ── THE SEMANTIC REGISTER — theorems by (grade · limit · rate-law) certificate: dedup = pricing = novelty, one key ──");
        o.AppendLine("  arm   mints   cert-classes   compression   singleton-classes");
        foreach (var a in new[] { on, off })
        {
            int mints = a.Sieve.MintLog.Count, classes = a.Sieve.DistinctCerts;
            int single = a.Sieve.Cas.Values.Count(c => c.Members == 1);
            o.AppendLine($"   {a.Label,-4} {mints,6}   {classes,10}   {(classes == 0 ? 0 : (double)mints / classes),9:F1}×   {single,6} ({Pct(single, Math.Max(1, classes))} of classes)");
        }
        // the composition-wall diagnosis (ON, thirds): STRING mint-rate vs NEW-CERTIFICATE rate — a real frontier
        // kills both; a paraphrase farm keeps minting strings while certificate space runs dry (the metric was the wall).
        var mintsT = new int[3]; var newCertT = new int[3];
        foreach (var cls in on.Sieve.Cas.Values)
        {
            int st = on.Pulses[cls.FirstCapture].Step;
            if (st >= 0) newCertT[Math.Min(2, st * 3 / Math.Max(1, steps))]++;
        }
        for (int i = 0; i < on.Pulses.Count; i++)
            if (on.Pulses[i].Step >= 0) mintsT[Math.Min(2, on.Pulses[i].Step * 3 / Math.Max(1, steps))]++;
        o.AppendLine($"     thirds (ON, seed excluded)   string mints {mintsT[0],5} → {mintsT[1],5} → {mintsT[2],5}   ·   NEW certificates {newCertT[0],5} → {newCertT[1],5} → {newCertT[2],5}");
        o.AppendLine($"     paraphrase share (non-first-capture mints)   {ShareCol(mintsT[0], newCertT[0])} → {ShareCol(mintsT[1], newCertT[1])} → {ShareCol(mintsT[2], newCertT[2])}   (the farm subsidy — kill-line: last third <30% under the cert-gate)");
        string wall = newCertT[2] > 0
            ? $"NO SEMANTIC WALL YET — {newCertT[2]} new certificate(s) still arriving in the last third (paraphrase share {Pct(mintsT[2] - newCertT[2], Math.Max(1, mintsT[2]))})"
            : mintsT[2] > 0
                ? $"PARAPHRASE FARM — {mintsT[2]} string-mints in the last third bought ZERO new certificates: the metric was the wall"
                : "REAL FRONTIER — both rates died: the generator is out of reach, the population is the lever";
        o.AppendLine($"     ⇒ {wall}.");
        o.AppendLine();

        // ── THE FULL-FRONTIER CENSUS — the machine's own discoveries as recognized targets. The paper's
        // 32 named targets are a SPARSE, PARTIAL stick over theorem-space; the census that tracks REAL discovery
        // is the distinct THEOREM classes (E identities + rate-law'd A asymptotics — S/D/U are refuted or
        // unwitnessable, recorded non-theorems), with the paper-32 and the anomaly register riding inside it as
        // LABELED SUBSETS (a paper/anomaly capture IS an E-class). "Un-freezing" = this census grows even while
        // the 32-stick sits still — the bench tracks the machine's actual frontier, not just the paper's map.
        o.AppendLine("  ── THE FULL-FRONTIER CENSUS — theorem-classes (E+A) as the growing target set; the paper-32 a labeled subset ──");
        o.AppendLine("  arm   census(E+A)      E      A   paper-32(anchor)   anomalies reg→captured");
        foreach (var a in new[] { on, off })
        {
            int cE = 0, cA = 0;
            foreach (var kv in a.Sieve.Cas) { if (kv.Key.Grade == 'E') cE++; else if (kv.Key.Grade == 'A') cA++; }
            int anomCap = a.Sieve.Anomalies.Values.Count(x => x.Hits > 0);
            o.AppendLine($"   {a.Label,-4} {a.Sieve.TheoremClasses,10}   {cE,5}  {cA,5}          {TargetsHit(a.Sieve),3}/{a.Sieve.Targets.Count}          {a.Sieve.Anomalies.Count,6}→{anomCap}");
        }
        // the un-freeze clocks (ON): new theorem-classes per third (each class's first-capture step) beside the
        // paper-32 capture curve — kill-line: the census un-freezes iff EITHER clock still moves late.
        var newThmT = new int[3]; int thmSeed = 0;
        foreach (var kv in on.Sieve.Cas)
        {
            if (kv.Key.Grade is not ('E' or 'A')) continue;
            int st = on.Pulses[kv.Value.FirstCapture].Step;
            if (st < 0) thmSeed++; else newThmT[Math.Min(2, st * 3 / Math.Max(1, steps))]++;
        }
        int bT1 = on.Sparks.Count > 0 ? on.Sparks[Math.Min(on.Sparks.Count - 1, steps / 3)].Bench : 0;
        int bT2 = on.Sparks.Count > 0 ? on.Sparks[Math.Min(on.Sparks.Count - 1, 2 * steps / 3)].Bench : 0;
        int bT3 = on.Sparks.Count > 0 ? on.Sparks[^1].Bench : 0;
        o.AppendLine($"     thirds (ON)   new theorem-classes {newThmT[0],5} → {newThmT[1],5} → {newThmT[2],5}   (+{thmSeed} seed)   ·   paper-32 captured {bT1,3} → {bT2,3} → {bT3,3}");
        bool paperMoves = bT3 > bT1;
        o.AppendLine(newThmT[2] > 0 || paperMoves
            ? $"     ⇒ CENSUS UN-FROZEN — {(paperMoves ? $"the named bench still climbing ({bT1}→{bT3}/{on.Sieve.Targets.Count})" : $"the named stick sits at {bT3}/{on.Sieve.Targets.Count}")}{(newThmT[2] > 0 ? $"; {newThmT[2]} new theorem-class(es) in the last third — real discovery the named stick {(paperMoves ? "also shows" : "cannot see")}" : "")}."
            : "     ⇒ CENSUS FROZEN — theorem-space and the named bench both stalled: a real composition wall at this budget (the population is the lever).");
        o.AppendLine();

        // the exact-tier renorm — THE RG-ATTRACTOR KILL-LINE: the ungraded dream renorms off-attractor (−1.08
        // scale-drift, ) because the corpus mixes exact structure with absorption pollution; the
        // EXACT tier alone must renorm to the corroborated fixed point (meanz ≈ −0.70, cvz locking).
        o.AppendLine("  ── THE EXACT-TIER RENORM (ON) — does the graded corpus reach the −0.70 RG attractor the ungraded dream missed? ──");
        var allStats = Engine.RenormStats(Engine.Induce(on.Tape).Result);
        byte[] exactTape = TierTape(on, m => m.Grade == 'E');
        var exStats = exactTape.Length == 0 ? default : Engine.RenormStats(Engine.Induce(exactTape).Result);
        o.AppendLine($"     all-mints tape   meanz {allStats.MeanZ,7:F3}   cvz {allStats.CvZ,6:F3}   scales {allStats.Scales,3}   ({on.Tape.Length}B)");
        if (exactTape.Length > 0)
        {
            o.AppendLine($"     EXACT tier       meanz {exStats.MeanZ,7:F3}   cvz {exStats.CvZ,6:F3}   scales {exStats.Scales,3}   ({exactTape.Length}B)");
            bool nearer = Math.Abs(exStats.MeanZ + 0.70) < Math.Abs(allStats.MeanZ + 0.70);
            bool locked = exStats.CvZ < 0.5 && (double.IsNaN(allStats.CvZ) || exStats.CvZ <= allStats.CvZ);
            o.AppendLine($"     ⇒ {(nearer && exStats.MeanZ < -0.5 ? "THE ATTRACTOR APPEARS on the exact tier" : "exact tier not yet at the attractor")} (|meanz+0.70|: {Math.Abs(allStats.MeanZ + 0.70):F3} → {Math.Abs(exStats.MeanZ + 0.70):F3}{(nearer ? ", nearer" : ", NOT nearer")}) · cvz {(locked ? "tightening" : "loose")} ({allStats.CvZ:F3} → {exStats.CvZ:F3})");
        }
        else o.AppendLine("     EXACT tier       (no exact mints — nothing to renorm)");
        o.AppendLine();

        // the anomaly register — the machine's self-generated bench (A-grade corrections as recognition targets)
        var anoms = on.Sieve.Anomalies.Values.OrderByDescending(a => a.Hits).ThenBy(a => a.Label, StringComparer.Ordinal).Take(6).ToList();
        o.AppendLine($"  ── THE ANOMALY REGISTER (ON) — {on.Sieve.Anomalies.Count} corrections registered as live targets · {on.Sieve.AnomalyHits} chased down ──");
        foreach (var a in anoms)
            o.AppendLine($"     {(a.Hits > 0 ? "HIT " : "open")}  ×{a.Hits,-4} {a.Label}");
        if (anoms.Count == 0) o.AppendLine("     (no asymptotic corrections registered — every mint graded exact/refuted at this budget)");
        o.AppendLine();

        // ── the sparkline suite ──
        o.AppendLine("  ── sparklines (per step, ▁▂▃▄▅▆▇█ over each arm's own range) ──");
        foreach (var a in new[] { on, off })
        {
            o.AppendLine($"   {a.Label}  identities  {Spark(a.Sparks.Select(s => (double)s.Identities))}");
            o.AppendLine($"       K-frontier  {Spark(a.Sparks.Select(s => (double)s.KFrontier))}");
            o.AppendLine($"       constants   {Spark(a.Sparks.Select(s => (double)s.Constants))}");
            o.AppendLine($"       cert-classes{Spark(a.Sparks.Select(s => (double)s.Certs))}   (the SEMANTIC distinct-count — paraphrase cannot move it)");
            o.AppendLine($"       paper-bench {Spark(a.Sparks.Select(s => (double)s.Bench))}   (named targets E-captured — the labeled validation anchor)");
            o.AppendLine($"       census(E+A) {Spark(a.Sparks.Select(s => (double)s.Census))}   (the FULL-FRONTIER CENSUS — the growing target set)");
            o.AppendLine($"       CvZ grok    {Spark(a.Sparks.Select(s => s.CvZ))}   (↓ = arithmetic crystallizing toward a scale-invariant grammar)");
        }
        o.AppendLine();

        // ── KILL-LINE (a) SANITY — the sieve must rediscover the paper's own reductions in the seed shells ──
        o.AppendLine("  ── KILL-LINE (a) SANITY — does the sieve rediscover the paper's reductions? (ON arm, seed shells) ──");
        foreach (var (label, note) in new[] { ("e", "eml(1,1)"), ("exp", "eml(x,1) = eˣ"), ("ln", "eml(1,eml(eml(1,x),1))"), ("0", "extended-real base") })
            o.AppendLine("     " + SanityRow(on.Sieve, label, note));
        o.AppendLine();

        // ── KILL-LINE (b) THE CHUNKING EFFECT — ON vs OFF at matched budget ──
        int kOn = on.Sieve.KFrontier, kOff = off.Sieve.KFrontier;
        int dOn = on.Sieve.DistinctValues, dOff = off.Sieve.DistinctValues;
        int tOn = TargetsHit(on.Sieve), tOff = TargetsHit(off.Sieve), N = on.Sieve.Targets.Count;
        bool spiral = kOn > kOff || (kOn == kOff && dOn > dOff) || tOn > tOff;
        o.AppendLine("  ── KILL-LINE (b) THE CHUNKING EFFECT — grammar-biased (ON) vs pure enumeration (OFF), matched budget ──");
        o.AppendLine($"     K-frontier reached   ON {kOn,4}   vs OFF {kOff,4}    (deeper shell reachable = the spiral re-centering)");
        o.AppendLine($"     distinct values      ON {dOn,4}   vs OFF {dOff,4}");
        o.AppendLine($"     calculator targets   ON {tOn,4}/{N} vs OFF {tOff,4}/{N}");
        o.AppendLine(spiral
            ? $"     ⇒ THE SPIRAL RE-CENTERS — grammar bias reaches a deeper shell / more discoveries at equal compute (chunked subroutines re-zero downstream depth)."
            : $"     ⇒ no chunking win at this budget — enumeration still dominates the shallow shells (raise --steps/--maxlen, lower --seedk to force the deep regime).");
        o.AppendLine();

        // ── KILL-LINE (c) NOISY-TV IMMUNITY — discoveries per candidate as K grows (ON) ──
        o.AppendLine("  ── KILL-LINE (c) NOISY-TV IMMUNITY — discoveries as K grows (ON): does the dream stay on the learnable edge? ──");
        var lens = on.Sieve.DiscoveriesByLen.Keys.Where(k => k <= maxLen).OrderBy(k => k).ToList();
        o.Append("     K        "); foreach (var k in lens) o.Append($"{k,5}"); o.AppendLine();
        o.Append("     discs    "); foreach (var k in lens) o.Append($"{on.Sieve.DiscoveriesByLen[k],5}"); o.AppendLine();
        bool collapses = lens.Count >= 3 && on.Sieve.DiscoveriesByLen[lens[^1]] == 0 && on.Sieve.DiscoveriesByLen[lens[^2]] == 0;
        o.AppendLine($"     ⇒ {(collapses ? "discoveries collapsed at the deep shells — generation drifted off the learnable edge" : "discoveries persist into the deep shells — the grammar bias keeps the dream productive (no noisy-TV collapse)")}");
        o.AppendLine();

        // ── THE PRIZE BENCH — shortest RPN found vs the paper's exhaustive Direct search ──
        o.AppendLine("  ── THE PRIZE BENCH — shortest RPN found vs the paper's exhaustive \"Direct search\" (K≤9 exhaustive limit; > = timed out) ──");
        o.AppendLine("     target      paperK    ON-K   OFF-K   shortest RPN (ON)                    verdict");
        int matched = 0, beaten = 0;
        var bench = new StringBuilder("target\tcat\tpaperK\tpaperTimedOut\tonK\toffK\tonProg\n");
        foreach (var cat in new[] { EmlCats.Constant, EmlCats.Function, EmlCats.Operator })
        {
            for (int i = 0; i < on.Sieve.Targets.Count; i++)
            {
                var t = on.Sieve.Targets[i];
                if (t.Cat != cat) continue;
                int onk = on.Sieve.BestK(i), offk = off.Sieve.BestK(i);
                string paper = (t.PaperTimedOut ? ">" : "") + t.PaperK;
                string verdict = Verdict(onk, t.PaperK, t.PaperTimedOut, ref matched, ref beaten);
                o.AppendLine($"     {t.Label,-10} {paper,6}   {K(onk),5}   {K(offk),5}   {(on.Sieve.BestProg(i) ?? "—"),-34}  {verdict}");
                bench.AppendLine($"{t.Label}\t{t.Cat}\t{t.PaperK}\t{t.PaperTimedOut}\t{onk}\t{offk}\t{on.Sieve.BestProg(i) ?? ""}");
            }
        }
        o.AppendLine();
        o.AppendLine($"  ⇒ BANKABLE: {matched} target(s) matched the paper's minimal RPN, {beaten} beaten (shorter than its Direct-search / past its timed-out frontier).");
        o.AppendLine();

        // ── THE DISCOVERY READOUT — the self-model reading its own mints ──
        var charted = on.Sieve.ChartedBySig();            // sig-space chart (paper bench + classic atlas) — arm-independent
        var don  = ScoreArm(on, charted, maxLen);
        var doff = ScoreArm(off, charted, maxLen);
        ReportDiscovery(o, on, off, don, doff, charted, steps, maxLen, top);

        Console.Write(o.ToString());

        // land the durable corpus (surplus — the arc is the object of study)
        run.Write("bench.tsv", bench.ToString());
        run.Write("mints_on.txt", Encoding.ASCII.GetString(on.Tape));
        run.Write("mints_off.txt", Encoding.ASCII.GetString(off.Tape));
        run.Write("mints_exact_on.txt", Encoding.ASCII.GetString(TierTape(on, m => m.Grade == 'E')));   // the vested-vocabulary corpus (`cogito renorm` it — the RG-attractor readout)
        run.Write("sparklines.tsv", SparkTsv(on, off));
        run.Write("novelty_on.tsv", NoveltyTsv(don, on.Pulses, on.Sieve));
        run.Write("novelty_off.tsv", NoveltyTsv(doff, off.Pulses, off.Sieve));
        var atsv = new StringBuilder("label\tvalue_re\tvalue_im\thits\n");
        foreach (var a in on.Sieve.Anomalies.Values.OrderBy(a => a.Label, StringComparer.Ordinal))
            atsv.AppendLine($"{a.Label}\t{a.Value.Real:R}\t{a.Value.Imaginary:R}\t{a.Hits}");
        run.Write("anomalies_on.tsv", atsv.ToString());
        run.Write("census_on.tsv", CensusTsv(on, sig));
    }

    // THE FULL-FRONTIER CENSUS artifact — every theorem-class (E+A), first-capture-ordered, its limit labeled
    // against the paper bench + atlas + the run's own anomaly register (paper-32 rows are the labeled validation
    // subset; `frontier` rows are the discovery the named stick cannot see).
    private static string CensusTsv(ArmRun a, int sig)
    {
        // the label charts at BOTH certificate rulers: E-certs key their limit at the sieve's own sig,
        // A-certs at the coarser FamilySig (EmlCert.Of's law — the chart must match the ruler to resolve).
        var exact = new Dictionary<EmlSig, string>();
        var family = new Dictionary<EmlSig, string>();
        void Chart(string label, Complex v1, Complex v2)
        {
            exact.TryAdd(Eml.Signature(new EmlValue(v1, true), new EmlValue(v2, true), Math.Min(sig, 9)), label);
            family.TryAdd(Eml.Signature(new EmlValue(v1, true), new EmlValue(v2, true), EmlCert.FamilySig), label);
        }
        Complex p1x = new(EmlSieve.Gamma, 0), p1y = new(EmlSieve.Glaisher, 0);
        Complex p2x = new(EmlSieve.Catalan, 0), p2y = new(EmlSieve.Apery, 0);
        foreach (var (label, fn) in EmlSieve.LabelChart()) Chart(label, fn(p1x, p1y), fn(p2x, p2y));
        foreach (var an in a.Sieve.Anomalies.Values) Chart(an.Label, an.Value, an.Value);

        var sb = new StringBuilder("cert\tgrade\tmembers\tfirst_step\trepk\tlabel\trep\trendered\n");
        foreach (var kv in a.Sieve.Cas.Where(kv => kv.Key.Grade is 'E' or 'A')
                     .OrderBy(kv => a.Pulses[kv.Value.FirstCapture].Step).ThenBy(kv => kv.Key.Hex(), StringComparer.Ordinal))
        {
            string label = (kv.Key.Grade == 'E' ? exact.GetValueOrDefault(kv.Key.Limit) : family.GetValueOrDefault(kv.Key.Limit)) ?? "frontier";
            sb.AppendLine($"{kv.Key.Hex()}\t{kv.Key.Grade}\t{kv.Value.Members}\t{StepCol(a.Pulses[kv.Value.FirstCapture].Step)}\t{kv.Value.Rep.Length}\t{label}\t{kv.Value.Rep}\t{EmlRender.Render(kv.Value.Rep)}");
        }
        return sb.ToString();
    }

    // the tier corpus — the mint LOG filtered by grade (EmlSieve.TierBytes, the one mint-log-read authority).
    private static byte[] TierTape(ArmRun a, Func<EmlMint, bool> keep) => a.Sieve.TierBytes(keep);

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE DISCOVERY READOUT — novelty N = surprise × compression × depth × rarity, all four read off the
    //  machine itself: no Lean, no judge, no label. The sieve guarantees every mint holds at its dual probe
    //  points to sig figures — a CANDIDATE grade, not exactness (scale-absorption mints asymptotic laws as
    //  identities; EmlRegrade's retro ladder grades them); this section reads which mints are DISCOVERIES.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<Discovery> ScoreArm(ArmRun a, Dictionary<EmlSig, string> charted, int maxLen)
    {
        var list = new List<Discovery>(a.Sieve.MintLog.Count);
        double logTotal = Math.Log2(Math.Max(2, a.Sieve.FiniteOffers));
        var repK = new Dictionary<EmlCert, int>();         // the certificate-bits fold's rolling MDL frontier (mint order)
        for (int i = 0; i < a.Sieve.MintLog.Count; i++)
        {
            var m = a.Sieve.MintLog[i];
            var p = a.Pulses[i];
            var bytes = Encoding.ASCII.GetBytes(m.Line);
            int rhsK = RhsK(m);                            // pure-RPN rhs → its K; a bench label is a NAME (K=1)
            double collapse = (double)Math.Abs(m.Prog.Length - rhsK) / Math.Max(1, Math.Max(m.Prog.Length, rhsK));
            double s = p.Excursion;
            double c = (1.0 - (double)a.FinalCover.ParsedSize(bytes) / bytes.Length) * collapse;
            double d = Math.Min(1.0, (double)m.Prog.Length / maxLen);
            double r = Math.Min(1.0, Math.Log2((double)a.Sieve.FiniteOffers / Math.Max(1, a.Sieve.SigHitsOf(m.Sig))) / logTotal);
            var cert = a.Sieve.MintCert(i);
            var (bits, first) = CertBitsStep(cert, RepCand(m), a.Sieve.MintLog.Count, a.Sieve.Cas[cert].Members, repK);
            list.Add(new Discovery(i, p.Step, m.Prog, m.Line, s, c, d, r, s * c * d * r, charted.GetValueOrDefault(m.Sig), bits, first));
        }
        return list;
    }

    /// One step of the certificate-space novelty fold — THE BITS-DENOMINATION LAW: every score in BITS or
    /// the organ inflates a private currency (string-distance paid the ln-family ×82 for paraphrase). The FIRST
    /// capture of a class pays the class's self-information over the corpus (log₂(mints/members) — a farmed family
    /// is cheap, a singleton is the jackpot); a later member pays only its MDL improvement (2 bits per RPN token
    /// shaved off the class representative — the 4-letter alphabet's rate); every other paraphrase pays ZERO.
    /// Σ over a corpus = its semantic information content — the compression audit's ratio, priced per mint.
    /// ONE authority: the live readout (ScoreArm) and the retro audit (EmlSemantic) both walk their corpora here.
    internal static (double Bits, bool First) CertBitsStep(EmlCert cert, int repCand, int totalMints, int classMembers, Dictionary<EmlCert, int> repK)
    {
        if (!repK.TryGetValue(cert, out int k))
        {
            repK[cert] = repCand;
            return (Math.Log2((double)totalMints / Math.Max(1, classMembers)), true);
        }
        if (repCand < k) { repK[cert] = repCand; return (2.0 * (k - repCand), false); }
        return (0.0, false);
    }

    /// The shortest PROGRAM this mint line carries — the class-representative candidate (mirrors
    /// EmlSieve.CertRepresentative: the rhs competes only on an E-grade AND when it is pure RPN; on a
    /// non-exact claim the rhs is the target, not an achiever; a label is a name, not a program).
    internal static int RepCand(EmlMint m)
    {
        if (m.Grade != 'E' || m.Line.Length <= m.Prog.Length + 3) return m.Prog.Length;
        var rhs = m.Line.AsSpan(m.Prog.Length + 3);
        foreach (char c in rhs) if (!Eml.IsToken(c)) return m.Prog.Length;
        return Math.Min(m.Prog.Length, rhs.Length);
    }

    // the rhs shell of a mint line — the canon program's K for an identity, 1 (a name) for a bench-label value-hit.
    private static int RhsK(EmlMint m)
    {
        if (m.Line.Length <= m.Prog.Length + 3) return 1;
        var rhs = m.Line.AsSpan(m.Prog.Length + 3);
        foreach (char c in rhs) if (!Eml.IsToken(c)) return 1;
        return rhs.Length;
    }

    private static void ReportDiscovery(StringBuilder o, ArmRun on, ArmRun off, List<Discovery> don, List<Discovery> doff,
                                        Dictionary<EmlSig, string> charted, int steps, int maxLen, int top)
    {
        o.AppendLine("  ── THE DISCOVERY READOUT — the self-model reading its own mints (N = surprise × compression × depth × rarity) ──");
        o.AppendLine("     S = the EXCURSION — the mint's residual under the grammar that generated it, vs the machine's own running");
        o.AppendLine("         baseline of that residual (ExcMint per-mint, band-free — the _excBand anti-bodge); newborn = neutral 0.5");
        o.AppendLine("     C = MDL rent — hindsight-capture (1 − ParsedSize_final/len) × COLLAPSE (|K_lhs−K_rhs|/max: the identity's own");
        o.AppendLine("         rewrite saving — deep-tower = short-form pays; blob = blob at ΔK≈0 pays nothing; a value-hit collapses to a NAME)");
        o.AppendLine("     D = K/maxlen (shell depth) · R = value-basin self-information (attractors like 0/1/e → 0) · seed round excluded from clocks");
        o.AppendLine();

        // per-arm mass table — rediscovery vs frontier, novelty mass at matched budget. The two currencies side by
        // side ARE the farm detector: the string N-mass paid to NON-first-capture mints is the paraphrase
        // subsidy (the ×82), which the bits column refuses by construction.
        o.AppendLine("  arm   mints   charted(rediscovery)   frontier        N-mass   paraphrase-N(subsidy)    Σbits   bits→paraphrase");
        foreach (var (a, d) in new[] { (on, don), (off, doff) })
        {
            int ch = d.Count(x => x.Chart is not null);
            double mass = d.Sum(x => x.N);
            double paraN = d.Where(x => !x.First).Sum(x => x.N);
            double bits = d.Sum(x => x.Bits);
            double paraBits = d.Where(x => !x.First).Sum(x => x.Bits);
            o.AppendLine($"   {a.Label,-4} {d.Count,6}   {ch,7} ({Pct(ch, d.Count)})   {d.Count - ch,7} ({Pct(d.Count - ch, d.Count)})   {mass,7:F2}   {paraN,7:F2} ({(mass > 0 ? 100 * paraN / mass : 0),3:F0}%)          {bits,8:F1}   {paraBits,6:F1} ({(bits > 0 ? 100 * paraBits / bits : 0),3:F0}%, MDL-shortenings only)");
        }
        o.AppendLine();

        // ── the rediscovery roster — which charted entries the dream actually reached, and at what surprise ──
        var firstMint = new Dictionary<string, Discovery>();            // chart label → its FIRST mint (mint order)
        foreach (var x in don) if (x.Chart is not null && !firstMint.ContainsKey(x.Chart)) firstMint[x.Chart] = x;
        var reached = charted
            .Select(kv => (Label: kv.Value, Sig: kv.Key, Canon: on.Sieve.CanonOf(kv.Key)))
            .Where(e => e.Canon is not null)
            .OrderBy(e => e.Canon!.Length).ThenBy(e => e.Label)
            .ToList();
        o.AppendLine($"  ── the rediscovery roster (ON) — charted mathematics the dream landed on: {reached.Count}/{charted.Count} entries ──");
        o.AppendLine("     entry       bestK   basin-hits   first mint (step · residual)      shortest program found");
        foreach (var e in reached)
        {
            string mint = firstMint.TryGetValue(e.Label, out var f)
                ? $"{StepCol(f.Step),5} · {on.Pulses[f.Idx].Residual:F2}"
                : "reached, never minted";
            o.AppendLine($"     {e.Label,-10} {e.Canon!.Length,5}   {on.Sieve.SigHitsOf(e.Sig),10}   {mint,-32}  {EmlRender.Render(e.Canon),-40}");
        }
        o.AppendLine();

        // ── the coined vocabulary — the grammar's pure-RPN subroutines ARE the functions the machine invented
        // (a rule expanding to ln's RPN body IS ln — the paper's phylogenetic tree, self-assembled). Two registers:
        // stack-complete chunks are FORMULAS (render as math); the rest are IDIOMS — morphemes straddling formula
        // boundaries (RePair chunks the hottest pairs, which need not close the stack).
        var vocab = EmlGen.PureChunks(on.FinalGrammar);
        var formulas = vocab.Where(c => c.MinReq == 0 && c.DeltaH == 1).OrderByDescending(c => c.Freq).Take(6).ToList();
        var idioms   = vocab.Where(c => !(c.MinReq == 0 && c.DeltaH == 1)).OrderByDescending(c => c.Freq).Take(6).ToList();
        if (vocab.Count > 0)
        {
            o.AppendLine($"  ── the coined vocabulary (ON) — the machine's own function library ({vocab.Count} pure subroutines; formulas vs idioms) ──");
            foreach (var c in formulas)
                o.AppendLine($"     formula  ×{c.Freq,-6} K={c.Toks.Length,-3}  {EmlRender.Render(c.Toks)}");
            if (formulas.Count == 0)
                o.AppendLine("     (no stack-complete formula chunks — the grammar speaks entirely in morphemes at this budget)");
            foreach (var c in idioms)
                o.AppendLine($"     idiom    ×{c.Freq,-6} K={c.Toks.Length,-3}  {c.Toks}  (Δh{c.DeltaH:+0;-0}, needs {c.MinReq})");
            o.AppendLine();
        }

        // ── the saturation clock (ON, thirds of the run, seed round excluded) ──
        string fuel = "short run — no clock (raise --steps)";
        var dream = don.Where(x => x.Step >= 0).ToList();
        if (dream.Count > 0 && steps >= 3)
        {
            int Third(int step) => Math.Min(2, step * 3 / steps);
            var sCh = new List<double>[3]; var sFr = new List<double>[3]; var hi = new int[3]; var massT = new double[3];
            for (int t = 0; t < 3; t++) { sCh[t] = new(); sFr[t] = new(); }
            double p90 = P90(dream.Select(x => x.N));
            foreach (var x in dream)
            {
                int t = Third(x.Step);
                (x.Chart is not null ? sCh[t] : sFr[t]).Add(on.Pulses[x.Idx].Residual);   // the clock reads the RAW residual (the grok signal)
                if (x.N >= p90) hi[t]++;
                massT[t] += x.N;
            }
            int kT1 = on.Sparks[Math.Min(on.Sparks.Count - 1, steps / 3)].KFrontier;
            int kT2 = on.Sparks[Math.Min(on.Sparks.Count - 1, 2 * steps / 3)].KFrontier;
            int kT3 = on.Sparks[^1].KFrontier;

            o.AppendLine("  ── the saturation clock (ON, thirds of the run) — does the machine grok the known and stay hot at the frontier? ──");
            o.AppendLine($"     charted-mint residual       {Thirds(sCh)}   (decay = the known saturating — rediscovery going quiet)");
            o.AppendLine($"     frontier-mint residual      {Thirds(sFr)}   (the frontier's own clock)");
            o.AppendLine($"     high-novelty mints (N≥P90) {hi[0],6} → {hi[1],6} → {hi[2],6}   per third");
            o.AppendLine($"     novelty mass Σ N           {massT[0],6:F2} → {massT[1],6:F2} → {massT[2],6:F2}");
            o.AppendLine($"     K-frontier                 {kT1,6} → {kT2,6} → {kT3,6}");
            // K at/above maxlen = the SAMPLER's own soft cap (stack-close overflows the length budget) — a saturated
            // ruler, not a stalled spiral; the frontier leg of the verdict then rides the novelty clocks alone.
            bool kCapped = kT3 >= maxLen;
            bool compounding = (hi[2] >= hi[0] || massT[2] >= massT[0]) && (kT3 > kT2 || kCapped);
            bool plateau = hi[2] < hi[0] && massT[2] < massT[0] && kT3 == kT2 && !kCapped;
            fuel = compounding
                ? "COMPOUNDING — high-novelty discovery still flowing in the last third" + (kCapped ? " (K-frontier saturated its own sandbox — raise --maxlen for the deeper shells)" : " and the shell frontier still climbing") + "; it screams keep going"
                : plateau
                    ? "PLATEAU — novelty mass and the frontier both flattened; the dream has drained this budget's shell"
                    : "MIXED — one clock still climbing while the other flattens (read the sparklines)";
            o.AppendLine($"     ⇒ {fuel}.");
            o.AppendLine();

            // trajectory sparklines over steps (mint-quality per step; gaps = steps with no mints)
            var nMass = new double[steps]; var sBar = new double[steps]; var cnt = new int[steps];
            Array.Fill(sBar, double.NaN);
            foreach (var x in dream)
            {
                double res = on.Pulses[x.Idx].Residual;
                nMass[x.Step] += x.N; cnt[x.Step]++;
                sBar[x.Step] = double.IsNaN(sBar[x.Step]) ? res : sBar[x.Step] + res;
            }
            for (int i = 0; i < steps; i++) if (cnt[i] > 0) sBar[i] /= cnt[i];
            o.AppendLine("  ── discovery trajectory (ON, per step) ──");
            o.AppendLine($"     N-mass      {Spark(nMass)}");
            o.AppendLine($"     residual    {Spark(sBar)}   (the surprise clock — the machine's own read of how novel its mints still are)");
            o.AppendLine();
        }

        // ── the top discoveries, rendered — what did it actually find? RANKED IN BITS (: the score must be
        // semantic — string-space let ORBIT hide while theorem-space circled 11xE1EE); N rides as the contrast column.
        o.AppendLine($"  ── TOP-{top} DISCOVERIES (ON) — the machine's own flags, human-readable (ranked by certificate bits) ──");
        o.AppendLine("     rank   bits      N      S     C     D(K)     R   step  chart       identity");
        int rank = 0;
        foreach (var x in don.OrderByDescending(x => x.Bits).ThenByDescending(x => x.N).ThenBy(x => x.Idx).Take(top))
        {
            rank++;
            string chart = x.Chart ?? "FRONTIER";
            o.AppendLine($"     {rank,4}  {x.Bits,5:F1}  {x.N,5:F3}  {x.S,5:F2} {x.C,5:F2}  {x.D,4:F2}({x.Prog.Length,2})  {x.R,4:F2}  {StepCol(x.Step),5}  {chart,-10}  {RenderMintClipped(x, 80)}");
        }
        o.AppendLine();

        // ── the post-saturation flags — once surprise on the known has decayed, the
        // machine's REMAINING high-novelty mints are the candidate-novel (the frontier of the known). Top of the
        // LAST third only, so mid-run bulk cannot mask what the mature grammar still flags. FIRST CAPTURES only —
        // a paraphrase cannot fly a flag.
        var late = don.Where(x => x.Step >= 2 * steps / 3 && x.First).OrderByDescending(x => x.Bits).ThenBy(x => x.Idx).Take(Math.Min(6, top)).ToList();
        if (late.Count > 0)
        {
            o.AppendLine("  ── POST-SATURATION FLAGS (ON, last third) — NEW certificates the mature grammar still lands ──");
            foreach (var x in late)
                o.AppendLine($"     bits {x.Bits,5:F1}  N {x.N,5:F3}  step {StepCol(x.Step),5}  {(x.Chart ?? "FRONTIER"),-10}  {RenderMintClipped(x, 80)}");
            o.AppendLine();
        }

        // the three verdicts, computed. Separation is TRIVIAL-vs-DEEP: the seed round IS the
        // trivial-rediscovery corpus (the axioms re-found by shallow enumeration), the dream mints are the machine's
        // own reach — the readout works iff its score buries the former and elevates the latter, with the
        // charted/frontier medians as the secondary read (a deep NEW route to a named thing is still a discovery).
        double medSeed  = Median(don.Where(x => x.Step < 0).Select(x => x.N));
        double medReplay = Median(don.Where(x => x.Step >= 0).Select(x => x.N));
        double medCh = Median(don.Where(x => x.Chart is not null).Select(x => x.N));
        double medFr = Median(don.Where(x => x.Chart is null).Select(x => x.N));
        int topFr = don.OrderByDescending(x => x.Bits).ThenByDescending(x => x.N).Take(top).Count(x => x.Chart is null);
        o.AppendLine("  ── VERDICTS ──");
        o.AppendLine($"     (a) separation   median N — trivial/seed {medSeed:F4} vs dream {medReplay:F4} ({(medSeed > 0 ? $"{medReplay / medSeed:F1}×" : "∞")})  ⇒ {(medReplay > 2 * medSeed ? "SEPARATES: the score buries the axiom-rediscovery floor, elevates the machine's own reach" : "weak — the dream does not yet outscore the seed floor")}");
        o.AppendLine($"                      (secondary: charted {medCh:F4} vs frontier {medFr:F4} — a high charted median = deep NEW routes to named things, not a failure)");
        o.AppendLine($"     (b) fuel         {fuel}");
        o.AppendLine($"     (c) reaching     {topFr}/{top} of the top discoveries are OFF-CHART (true equivalences no atlas entry names)");
        o.AppendLine();
    }

    // ── discovery-readout helpers ──

    /// Render a mint line human-readable: both sides through EmlRender when the rhs is pure RPN (an identity),
    /// `≡ LABEL` when the rhs is a bench label (a value-hit on named mathematics).
    private static string RenderMint(Discovery x)
    {
        var (lhs, eq, rhs) = RenderSides(x);
        return $"{lhs} {eq} {rhs}";
    }

    /// Table-width render that never sacrifices the rhs — the COLLAPSE target is the load-bearing half of an
    /// identity, so the deep lhs is what gets elided.
    private static string RenderMintClipped(Discovery x, int w)
    {
        var (lhs, eq, rhs) = RenderSides(x);
        int budget = w - rhs.Length - eq.Length - 2;
        if (lhs.Length > budget) lhs = budget > 1 ? lhs[..(budget - 1)] + "…" : "…";
        return $"{lhs} {eq} {rhs}";
    }

    private static (string Lhs, string Eq, string Rhs) RenderSides(Discovery x)
    {
        string rhs = x.Line.Length > x.Prog.Length + 3 ? x.Line[(x.Prog.Length + 3)..] : "";
        bool pureRhs = rhs.Length > 0;
        foreach (char c in rhs) if (!Eml.IsToken(c)) { pureRhs = false; break; }
        return pureRhs && rhs.Length > 1
            ? (EmlRender.Render(x.Prog), "=", EmlRender.Render(rhs))
            : (EmlRender.Render(x.Prog), "≡", rhs);
    }

    private static string StepCol(int step) => step < 0 ? "seed" : step.ToString();
    private static string Pct(int n, int total) => total == 0 ? "—" : $"{100 * n / total,3}%";

    // one third's paraphrase share — the mints that were NOT first captures, over the third's mints.
    private static string ShareCol(int mints, int firsts) => mints == 0 ? "  — " : $"{100 * (mints - firsts) / mints,3}%";

    private static string Thirds(List<double>[] t)
        => $"{Mean(t[0]),6:F3} → {Mean(t[1]),6:F3} → {Mean(t[2]),6:F3}";

    private static double Mean(List<double> v) => v.Count == 0 ? double.NaN : v.Average();

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count == 0 ? double.NaN : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    private static double P90(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count == 0 ? double.NaN : v[Math.Min(v.Count - 1, (int)(v.Count * 0.9))];
    }

    private static string NoveltyTsv(List<Discovery> d, List<MintPulse> pulses, EmlSieve sieve)
    {
        var sb = new StringBuilder("idx\tstep\tk\tresid\ts\tc\td\tr\tn\tbits\tfirst\tcert\tchart\tprog\tline\trendered\n");
        foreach (var x in d)
            sb.AppendLine($"{x.Idx}\t{x.Step}\t{x.Prog.Length}\t{pulses[x.Idx].Residual:F4}\t{x.S:F4}\t{x.C:F4}\t{x.D:F4}\t{x.R:F4}\t{x.N:F5}\t{x.Bits:F3}\t{(x.First ? 1 : 0)}\t{sieve.MintCert(x.Idx).Hex()}\t{x.Chart ?? "frontier"}\t{x.Prog}\t{x.Line}\t{RenderMint(x)}");
        return sb.ToString();
    }

    // ── report helpers ──

    private static string SanityRow(EmlSieve s, string label, string note)
    {
        for (int i = 0; i < s.Targets.Count; i++)
            if (s.Targets[i].Label == label)
            {
                int k = s.BestK(i);
                bool ok = k >= 0 && (s.Targets[i].PaperTimedOut || k <= s.Targets[i].PaperK);
                return $"{label,-4} = {note,-28} → {(k >= 0 ? $"found {s.BestProg(i),-14} K={k,-3} (paper {s.Targets[i].PaperK})" : "NOT FOUND"),-48} {(ok ? "✓" : k >= 0 ? "found (longer)" : "✗")}";
            }
        return $"{label}: (not a bench target)";
    }

    private static string Verdict(int onk, int paperK, bool timedOut, ref int matched, ref int beaten)
    {
        if (onk < 0) return "—";
        if (timedOut) { beaten++; return onk <= paperK ? "BEAT (past timed-out frontier)" : $"found K={onk} (paper >{paperK})"; }
        if (onk == paperK) { matched++; return "match"; }
        if (onk < paperK) { beaten++; return $"BEAT by {paperK - onk}"; }
        return $"longer (+{onk - paperK})";
    }

    private static string K(int k) => k < 0 ? "—" : k.ToString();
    private static int CountCat(EmlSieve s, EmlCats c) { int n = 0; for (int i = 0; i < s.Targets.Count; i++) if (s.Targets[i].Cat == c) n++; return n; }
    private static int TargetsHit(EmlSieve s) => s.TargetsHit();

    // an 8-level block sparkline, downsampled (bucket-mean) to `width` columns; NaN samples render as a gap.
    internal static string Spark(IEnumerable<double> values, int width = 60)
    {
        const string blocks = "▁▂▃▄▅▆▇█";
        var v = values.ToList();
        if (v.Count == 0) return "";
        var cols = new double[Math.Min(width, v.Count)];
        var has = new bool[cols.Length];
        for (int c = 0; c < cols.Length; c++)
        {
            int lo = c * v.Count / cols.Length, hi = Math.Max(lo + 1, (c + 1) * v.Count / cols.Length);
            double sum = 0; int cnt = 0;
            for (int i = lo; i < hi && i < v.Count; i++) if (double.IsFinite(v[i])) { sum += v[i]; cnt++; }
            if (cnt > 0) { cols[c] = sum / cnt; has[c] = true; }
        }
        double min = double.MaxValue, max = double.MinValue;
        for (int c = 0; c < cols.Length; c++) if (has[c]) { min = Math.Min(min, cols[c]); max = Math.Max(max, cols[c]); }
        if (min > max) return new string(' ', cols.Length);   // all-NaN
        var sb = new StringBuilder(cols.Length);
        double range = max - min;
        for (int c = 0; c < cols.Length; c++)
            sb.Append(has[c] ? blocks[range < 1e-12 ? 0 : Math.Clamp((int)((cols[c] - min) / range * 7.999), 0, 7)] : ' ');
        return sb.ToString();
    }

    private static string SparkTsv(ArmRun on, ArmRun off)
    {
        var sb = new StringBuilder("arm\tstep\tidentities\tkfrontier\tconstants\tcvz\tcerts\tbench\tcensus\n");
        void Emit(ArmRun a) { for (int i = 0; i < a.Sparks.Count; i++) { var s = a.Sparks[i]; sb.AppendLine($"{a.Label}\t{i}\t{s.Identities}\t{s.KFrontier}\t{s.Constants}\t{(double.IsNaN(s.CvZ) ? "nan" : s.CvZ.ToString("F4"))}\t{s.Certs}\t{s.Bench}\t{s.Census}"); } }
        Emit(on); Emit(off);
        return sb.ToString();
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE RETRO-LADDER CENSUS — `cogito dreamcalc --regrade <run>`
//
//  VERIFICATION IS RENORMALIZATION: a claim's witness-grade is the scaling class of its residual under a
//  witness-refinement flow, and the grade must vest as part of the knowledge. The sieve certifies at a SINGLE
//  scale (P1/P2 both O(1)-argument, sig=9 relative), so it mints ASYMPTOTIC laws as EXACT whenever a deep
//  exp-tower absorbs a subtracted ln-term below the quantizer — often below double eps, where NO resolution
//  tier can see it. This verb re-offers an existing mint corpus (no search re-run) through three orthogonal
//  witness axes:
//    RESOLUTION  sig ∈ {6,9,12} at every point (the quantizer refined);
//    REGIME      a third probe point at SMALL argument (Feigenbaum reciprocals 1/δ, 1/α — believed-independent
//                transcendentals outside the exp-log class) where the towers collapse and absorbed terms surface;
//    ENCLOSURE   outward-rounded rectangle arithmetic (EmlRect) — an enclosure of lhs−rhs excluding 0 DISPROVES
//                exactness threshold-free — plus the per-op absorption witness at exp(a)−ln(b) (Eml.EvalLadder).
//  EMITS the graded corpus (E/A/S/D/U tags — the grade-gated campfire's input), the canon-taint set (absorption
//  artifacts), and the correction-value table (each asymptotic's residual — the machine's own next targets).
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

public static class EmlRegrade
{
    /// The K-shell buckets the census reports over (the kill-line contrasts the first and last).
    private static readonly (string Name, int Lo, int Hi)[] Shells = [("K<=9", 0, 9), ("K11-17", 10, 17), ("K19-25", 18, 25), ("K>=27", 26, int.MaxValue)];

    // one graded mint — the census row (the verdict fields come from the SHARED EmlGrader — one law, two mounts).
    // `Tilde` = the corpus line's own alphabet byte (` ~ ` vs ` = `): on a live-graded corpus (any tilde present)
    // it is the gate's claim, and the census re-derives the grade to report agreement (the drift alarm); a
    // pre-gate corpus is all `=` and the byte carries no claim.
    private readonly record struct Graded(
        string Line, string Lhs, string Rhs, bool RhsRpn, bool Tilde, int K, char Grade, bool Taint, bool Suspect,
        int SubEpsHome, double MinRatioHome, double Rel1, double Rel2, double Rel3, Complex Corr3,
        bool Q9Home, bool Q12Home, bool Q9P3, bool Q12P3, string EnclCols);

    public static int Run(string[] args)
    {
        string runArg = Args.Str(args, "--regrade", "");
        if (runArg.Length == 0 || runArg.StartsWith("--", StringComparison.Ordinal))
        { Console.Error.WriteLine("usage: cogito dreamcalc --regrade <run> [--p3x F] [--p3y F] [--file mints_on.txt]"); return 2; }
        string? dir = Cogito.Run.Resolve(runArg);
        if (dir is null) { Console.Error.WriteLine($"regrade: run dir not found: {runArg}"); return 2; }

        double p3x = Args.Double(args, "--p3x", 1.0 / EmlGrader.FeigenbaumDelta);
        double p3y = Args.Double(args, "--p3y", 1.0 / EmlGrader.FeigenbaumAlpha);
        var grader = new EmlGrader(p3x, p3y);

        string only = Args.Str(args, "--file", "");
        var files = only.Length > 0 ? new[] { only } : new[] { "mints_on.txt", "mints_off.txt" };
        Trace.Note($"regrade · {Path.GetFileName(dir)} · P3=({p3x:F7},{p3y:F7}) · ladder: sig{{9,12}} × {{P1,P2,P3}} × enclosure (EmlGrader — the live gate's own law)");

        int rc = 0;
        foreach (var f in files)
        {
            var path = Path.Combine(dir, f);
            if (!File.Exists(path)) { if (only.Length > 0) { Console.Error.WriteLine($"regrade: no {f} in {dir}"); rc = 2; } continue; }
            Census(dir, f, grader);
        }
        return rc;
    }

    private static void Census(string dir, string file, EmlGrader grader)
    {
        string suffix = Path.GetFileNameWithoutExtension(file).Replace("mints_", "", StringComparison.Ordinal);

        // ── the corpus, de-duplicated (the tape repeats the MIX-rail axiom line; the mint log is unique) ──
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();
        int raw = 0;
        foreach (var line in File.ReadLines(Path.Combine(dir, file)))
        {
            if (line.Length == 0) continue;
            raw++;
            if (seen.Add(line)) lines.Add(line);
        }

        // ── grade every claim (EmlPrediction + LabelChart — the shared line-parsing/label-dispatch authorities) ──
        var refs = EmlSieve.LabelChart();
        var graded = new List<Graded>(lines.Count);
        int malformed = 0, unknownLabel = 0, anomaly = 0, q9Repro = 0;

        foreach (var line in lines)
        {
            if (!EmlPrediction.TryParse(line, out var c)) { malformed++; continue; }
            if (!grader.TryGrade(in c, refs, out var v)) { unknownLabel++; continue; }

            if (v.Q9Home) q9Repro++;                                     // the self-check: the ladder reproduces the original mint criterion
            if (!v.HomeValid) anomaly++;

            graded.Add(new Graded(line, c.Lhs, c.Rhs, c.RhsRpn, c.Tilde, c.Lhs.Length, v.Grade, v.Taint, v.Suspect, v.SubEpsHome, v.MinRatioHome,
                                  v.Rel1, v.Rel2, v.Rel3, v.Corr3, v.Q9Home, v.Q12Home, v.Q9P3, v.Q12P3, v.EnclCols));
        }

        Report(dir, file, suffix, grader.Points, raw, lines.Count, malformed, unknownLabel, anomaly, q9Repro, graded);
    }

    // ── the census report + artifacts ──

    private static void Report(string dir, string file, string suffix, (Complex X, Complex Y)[] pts,
                               int raw, int unique, int malformed, int unknownLabel, int anomaly, int q9Repro, List<Graded> g)
    {
        var o = new StringBuilder();
        int N = g.Count;
        int nE = g.Count(x => x.Grade == 'E'), nA = g.Count(x => x.Grade == 'A'), nS = g.Count(x => x.Grade == 'S');
        int nD = g.Count(x => x.Grade == 'D'), nU = g.Count(x => x.Grade == 'U');
        double fx = Pct(nA + nS, N);

        o.AppendLine();
        o.AppendLine("════════════════════════════════════════════════════════════════════════════════════════════════");
        o.AppendLine($"  THE RETRO-LADDER CENSUS — {Path.GetFileName(dir)}/{file} · verification is renormalization");
        o.AppendLine($"    ladder  home P1=(γ,A) P2=(G,ζ3) · regime P3=({pts[2].X.Real:F7},{pts[2].Y.Real:F7}) [1/δ,1/α Feigenbaum] · sig{{9,12}} + rectangle enclosure");
        o.AppendLine($"    corpus  {raw} tape lines → {unique} unique mints ({malformed} malformed, {unknownLabel} unknown-label skipped)");
        o.AppendLine($"    self-check  q9@home reproduces the mint criterion on {q9Repro}/{N} ({anomaly} home-invalid anomalies)");
        int nTilde = g.Count(x => x.Tilde);
        if (nTilde > 0)                                                  // a live-graded corpus — the retro census must re-derive the gate's own bytes (one law, zero drift)
        {
            int agree = g.Count(x => x.Tilde == (x.Grade != 'E'));
            o.AppendLine($"    live-gate   {nTilde} `~` lines ⇒ corpus was live-graded · retro ladder agrees on {agree}/{N} ({100.0 * agree / Math.Max(1, N):F1}%{(agree == N ? " — no drift" : " — DRIFT: live gate and retro census disagree")})");
        }
        o.AppendLine();

        // ── (a) THE GRADE HISTOGRAM, by K-shell ──
        o.AppendLine("  ── THE GRADE HISTOGRAM — E exact · A asymptotic · S scale-local · D domain-restricted · U unwitnessable ──");
        o.AppendLine("     shell      N      E      A      S      D      U   false-exact(A+S)");
        foreach (var (name, lo, hi) in Shells)
        {
            var s = g.Where(x => x.K >= lo && x.K <= hi).ToList();
            if (s.Count == 0) continue;
            o.AppendLine($"     {name,-8} {s.Count,5}  {Cnt(s, 'E'),5}  {Cnt(s, 'A'),5}  {Cnt(s, 'S'),5}  {Cnt(s, 'D'),5}  {Cnt(s, 'U'),5}   {Pct(Cnt(s, 'A') + Cnt(s, 'S'), s.Count),6:F1}%");
        }
        o.AppendLine($"     {"ALL",-8} {N,5}  {nE,5}  {nA,5}  {nS,5}  {nD,5}  {nU,5}   {fx,6:F1}%");
        o.AppendLine();

        // ── (b) THE CANON-TAINT SET — absorption artifacts (the pollution the campfire would bake) ──
        var taint = g.Where(x => x.Taint).ToList();
        var suspect = g.Where(x => x.Suspect).ToList();
        var towers = g.Where(x => x.SubEpsHome > 0).ToList();            // the ln-tower family: BITWISE absorption at home
        o.AppendLine($"  ── THE CANON-TAINT SET — {taint.Count} proven absorption-artifacts ({Pct(taint.Count, N):F1}% of the corpus); {suspect.Count} absorbed-but-unrefuted (suspect) ──");
        foreach (var x in taint.OrderByDescending(x => x.K).Take(6))
            o.AppendLine($"     {x.Grade} K={x.K,-3} rel@home {MaxHome(x),8:E1} → rel@P3 {x.Rel3,8:E1}   {Clip(Rendered(x), 86)}");
        o.AppendLine();

        // ── (c) THE CORRECTION-VALUE TABLE — each asymptotic's residual IS the machine's next target ──
        var asym = g.Where(x => x.Grade == 'A').ToList();
        o.AppendLine($"  ── THE CORRECTION-VALUE TABLE — {asym.Count} asymptotic mints; residual v_lhs−v_rhs at P3 (the self-generated bench) ──");
        foreach (var x in asym.OrderByDescending(x => Complex.Abs(x.Corr3)).Take(8))
            o.AppendLine($"     K={x.K,-3} corr {Fc(x.Corr3),-24} rel@P3 {x.Rel3,8:E1}   {Clip(Rendered(x), 80)}");
        o.AppendLine();

        // ── PRE-REGISTERED KILL-LINES ──
        int deepest = 0;                                                 // the deepest POPULATED shell — OFF corpora stop shallow
        for (int i = 0; i < Shells.Length; i++) if (!double.IsNaN(ShellRate(g, i))) deepest = i;
        double fxLo = ShellRate(g, 0), fxHi = ShellRate(g, deepest);
        int towersSplit = towers.Count(x => x.Grade is 'A' or 'S');
        int towersQ12 = towers.Count(x => x.Q12Home);
        int enclX = g.Count(x => x.EnclCols.Contains('x'));
        int enclXAbsorbed = g.Count(x => x.EnclCols.Contains('x') && (x.SubEpsHome > 0 || x.MinRatioHome < 1e-9));
        var decAbs = Decades(towers);
        var decOther = Decades(g.Where(x => x.Grade == 'A' && x.SubEpsHome == 0).ToList());
        o.AppendLine("  ── PRE-REGISTERED KILL-LINES ──");
        o.AppendLine($"     (i)   false-exact rate {fx:F1}% (predicted 15–30%, kill <10%)  ⇒ {(fx >= 10 ? "HOLDS" : "KILLED")}");
        o.AppendLine($"           by K: {Shells[0].Name} {fxLo:F1}% vs {Shells[deepest].Name} {fxHi:F1}% (predicted ≤5% vs ≥25%)  ⇒ {(fxHi > fxLo ? "depth-correlation HOLDS" : "FLAT — depth-correlation KILLED (absorption is SCALE-driven, not K-driven)")}");
        o.AppendLine($"     (ii)  ln-tower family (bitwise absorption at home): {towers.Count} mints; {towersSplit}/{towers.Count} split (refuted) on the ladder;");
        o.AppendLine($"           {towersQ12}/{towers.Count} SURVIVE sig=12 alone at home  ⇒ {(towersQ12 == towers.Count ? "resolution and regime are DISTINCT axes (sig can never catch these)" : "some catchable by resolution alone")}");
        o.AppendLine($"     (iii) enclosure disproofs: {enclX} mints with an interval excluding 0 at ≥1 point ({enclXAbsorbed} in the absorption family)");
        o.AppendLine($"     (iv)  residual shape: absorption family jumps {decAbs:F0} decades home→P3 (median); non-absorbed asymptotics {decOther:F0}  ⇒ {(decAbs - decOther >= 3 ? "grades CLUSTER (two mechanisms)" : "no clean cluster")}");
        o.AppendLine();

        // ── THE VERDICT — the irreversible gate ──
        o.AppendLine("  ── VERDICT — the irreversible grade-gate ──");
        o.AppendLine(fx >= 10
            ? $"     ⇒ GATE THE CANON BEFORE THE CAMPFIRE BAKES: {fx:F1}% of this corpus is not-exact-as-minted ({nA} asymptotic + {nS} scale-local);"
            : $"     ⇒ pollution below the 10% kill-line ({fx:F1}%) — the ungraded bake is tolerable on this corpus;");
        o.AppendLine($"        ungraded, these re-induce into chunks and the generator learns to farm absorption-coincidences.");
        o.AppendLine($"        graded corpus → regrade_{suffix}.tsv · corrections → regrade_corrections_{suffix}.tsv (the machine's own next bench)");
        o.AppendLine();

        Console.Write(o.ToString());

        // ── the durable artifacts, colocated with the corpus they grade ──
        var run = Cogito.Run.Open(dir);
        run.Write($"regrade_census_{suffix}.txt", o.ToString());
        var tsv = new StringBuilder("grade\ttaint\tsubeps\tminratio\tk\trel_p1\trel_p2\trel_p3\tq9home\tq12home\tq9p3\tq12p3\tencl\tline\n");
        foreach (var x in g)
            tsv.AppendLine($"{x.Grade}\t{(x.Taint ? 1 : 0)}\t{x.SubEpsHome}\t{x.MinRatioHome:E2}\t{x.K}\t{Fe(x.Rel1)}\t{Fe(x.Rel2)}\t{Fe(x.Rel3)}\t{B(x.Q9Home)}\t{B(x.Q12Home)}\t{B(x.Q9P3)}\t{B(x.Q12P3)}\t{x.EnclCols}\t{x.Line}");
        run.Write($"regrade_{suffix}.tsv", tsv.ToString());
        var corr = new StringBuilder("k\tcorr_re\tcorr_im\tcorr_abs\trel_p1\trel_p3\tline\trendered\n");
        foreach (var x in asym.OrderByDescending(x => Complex.Abs(x.Corr3)))
            corr.AppendLine($"{x.K}\t{x.Corr3.Real:R}\t{x.Corr3.Imaginary:R}\t{Complex.Abs(x.Corr3):E6}\t{Fe(x.Rel1)}\t{Fe(x.Rel3)}\t{x.Line}\t{Rendered(x)}");
        run.Write($"regrade_corrections_{suffix}.tsv", corr.ToString());
    }

    // ── report helpers ──

    private static int Cnt(List<Graded> s, char grade) { int n = 0; foreach (var x in s) if (x.Grade == grade) n++; return n; }
    private static double Pct(int n, int total) => total == 0 ? 0 : 100.0 * n / total;
    private static double MaxHome(Graded x) => Math.Max(double.IsNaN(x.Rel1) ? 0 : x.Rel1, double.IsNaN(x.Rel2) ? 0 : x.Rel2);
    private static string B(bool b) => b ? "1" : "0";
    private static string Fe(double v) => double.IsNaN(v) ? "nan" : v.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);
    private static string Fc(Complex c) => c.Imaginary == 0 ? $"{c.Real:G6}" : $"{c.Real:G6}{(c.Imaginary < 0 ? "" : "+")}{c.Imaginary:G6}i";
    private static string Clip(string s, int w) => s.Length <= w ? s : s[..(w - 1)] + "…";

    private static double ShellRate(List<Graded> g, int shell)
    {
        var (_, lo, hi) = Shells[shell];
        int n = 0, f = 0;
        foreach (var x in g) if (x.K >= lo && x.K <= hi) { n++; if (x.Grade is 'A' or 'S') f++; }
        return n == 0 ? double.NaN : 100.0 * f / n;
    }

    // median decades jumped by the relative residual from the minting scale to the regime point — the
    // renormalization read: absorption is a scale artifact (30–300 decades); a genuine near-miss moves linearly.
    private static double Decades(List<Graded> family)
    {
        var d = new List<double>();
        foreach (var x in family)
        {
            if (double.IsNaN(x.Rel3) || x.Rel3 <= 0) continue;
            double home = Math.Max(MaxHome(x), 1e-300);
            d.Add(Math.Log10(x.Rel3 / home));
        }
        if (d.Count == 0) return double.NaN;
        d.Sort();
        return d[d.Count / 2];
    }

    // render what the tape SAYS — the line's own grade byte rides into the human-readable form (a live-graded
    // corpus's `~` claims stay `~`; a pre-gate corpus's `=` claims stay `=`, however the census re-grades them).
    private static string Rendered(Graded x)
        => x.RhsRpn && x.Rhs.Length > 1 ? $"{EmlRender.Render(x.Lhs)} {(x.Tilde ? '~' : '=')} {EmlRender.Render(x.Rhs)}"
                                        : $"{EmlRender.Render(x.Lhs)} {(x.Tilde ? '≈' : '≡')} {x.Rhs}";
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE SEMANTIC-COMPRESSION AUDIT — `cogito dreamcalc --semantic-compress <run>`
//
//  Key an EXISTING mint corpus by theorem certificate (EmlCert — grade · limit · rate-law) and compress it
//  semantically: the compression ratio IS the audit of the discovery process — honest search → INCOMPRESSIBLE
//  corpus (every entry genuinely new); Goodharted search → COMPRESSIBLE (paraphrase families crush). The
//  compressor auditing its own novelty by compressing it. Emits the class histogram (the paraphrase families,
//  largest first), the ln-family collapse readout (the ×82), the MDL-within-class demotions (the shortest program
//  is the stored representative — the four-gate paragraph demotes automatically), and the corpus's semantic
//  information in BITS (the bits-denomination law: every score in the constitution's currency).
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

public static class EmlSemantic
{
    // one audited mint — the claim, its verdict's essentials, and its certificate (corpus order preserved)
    private readonly record struct Keyed(EmlPrediction Prediction, char Grade, Complex Corr3, EmlCert Cert, int RepCand);

    // one class's audit row — the CAS entry + retro-only enrichments (the exemplar correction that names the
    // rate-law humanly, the longest member = the paragraph the MDL slot demoted)
    private readonly record struct ClassRow(EmlCert Cert, SemanticCASClass<string> Cls, Complex Corr, int MaxLhsK, double FirstBits);

    public static int Run(string[] args)
    {
        string runArg = Args.Str(args, "--semantic-compress", "");
        if (runArg.Length == 0 || runArg.StartsWith("--", StringComparison.Ordinal))
        { Console.Error.WriteLine("usage: cogito dreamcalc --semantic-compress <run> [--sig S] [--p3x F] [--p3y F] [--file mints_on.txt]"); return 2; }
        string? dir = Cogito.Run.Resolve(runArg);
        if (dir is null) { Console.Error.WriteLine($"semantic-compress: run dir not found: {runArg}"); return 2; }

        int sig = Args.Int(args, "--sig", ReplayCalc.MountSig);
        double p3x = Args.Double(args, "--p3x", 1.0 / EmlGrader.FeigenbaumDelta);
        double p3y = Args.Double(args, "--p3y", 1.0 / EmlGrader.FeigenbaumAlpha);
        var grader = new EmlGrader(p3x, p3y);

        string only = Args.Str(args, "--file", "");
        var files = only.Length > 0 ? new[] { only } : new[] { "mints_on.txt", "mints_off.txt" };
        Trace.Note($"semantic-compress · {Path.GetFileName(dir)} · certificate = (grade · limit@sig{{{Math.Min(sig, 9)}|{EmlCert.FamilySig}}} · rate=drift-decade-band) · P3=({p3x:F7},{p3y:F7})");

        int rc = 0;
        foreach (var f in files)
        {
            var path = Path.Combine(dir, f);
            if (!File.Exists(path)) { if (only.Length > 0) { Console.Error.WriteLine($"semantic-compress: no {f} in {dir}"); rc = 2; } continue; }
            Audit(dir, f, grader, sig);
        }
        return rc;
    }

    private static void Audit(string dir, string file, EmlGrader grader, int sig)
    {
        string suffix = Path.GetFileNameWithoutExtension(file).Replace("mints_", "", StringComparison.Ordinal);

        // ── the corpus, de-duplicated (the tape repeats the MIX-rail axiom line; the mint log is unique) ──
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();
        int raw = 0;
        foreach (var line in File.ReadLines(Path.Combine(dir, file)))
        {
            if (line.Length == 0) continue;
            raw++;
            if (seen.Add(line)) lines.Add(line);
        }

        // ── grade → certify → admit in the CAS (same representative law as the live sieve) ──
        Dictionary<string, Func<Complex, Complex, Complex>> refs = EmlSieve.LabelChart();
        List<Keyed> keyed = new(lines.Count);
        SemanticCAS<EmlCert, string> cas = new(EmlSieve.CompareCertRepresentatives);
        Dictionary<EmlCert, int> maxLhsK = new();                       // the paragraph tracker — the longest lhs each class demoted
        Dictionary<EmlCert, Complex> corrOf = new();                    // first member's Corr3 — names the rate-law humanly (corr≈1.54 IS lnδ)
        int malformed = 0, unknownLabel = 0;
        foreach (var line in lines)
        {
            if (!EmlPrediction.TryParse(line, out var c)) { malformed++; continue; }
            if (!grader.TryGrade(in c, refs, out var v)) { unknownLabel++; continue; }
            var cert = EmlCert.Of(in v, sig);
            cas.Admit(cert, EmlSieve.CertRepresentative(cert, c.Lhs, c.Rhs), keyed.Count);
            maxLhsK[cert] = Math.Max(maxLhsK.GetValueOrDefault(cert), c.Lhs.Length);
            corrOf.TryAdd(cert, v.Corr3);
            keyed.Add(new Keyed(c, v.Grade, v.Corr3, cert, v.Grade == 'E' && c.RhsRpn ? Math.Min(c.Lhs.Length, c.Rhs.Length) : c.Lhs.Length));
        }

        // ── the bits fold (ReplayCalc.CertBitsStep — the live readout's own law) + per-class first-capture bits ──
        var repK = new Dictionary<EmlCert, int>();
        var firstBits = new Dictionary<EmlCert, double>();
        double totalBits = 0, shortenBits = 0;
        int shortenings = 0, paraphrases = 0;
        foreach (var k in keyed)
        {
            var (bits, first) = ReplayCalc.CertBitsStep(k.Cert, k.RepCand, keyed.Count, cas[k.Cert].Members, repK);
            totalBits += bits;
            if (first) firstBits[k.Cert] = bits;
            else if (bits > 0) { shortenings++; shortenBits += bits; }
            else paraphrases++;
        }

        // ── the chart — name classes whose limit is charted mathematics (grade-matched resolution) ──
        var byExact = new Dictionary<EmlSig, string>();
        var byFamily = new Dictionary<EmlSig, string>();
        var pts = grader.Points;
        foreach (var (label, fn) in refs)
        {
            var v1 = fn(pts[0].X, pts[0].Y); var v2 = fn(pts[1].X, pts[1].Y);
            byExact.TryAdd(Eml.Signature(new EmlValue(v1, true), new EmlValue(v2, true), Math.Min(sig, 9)), label);
            byFamily.TryAdd(Eml.Signature(new EmlValue(v1, true), new EmlValue(v2, true), EmlCert.FamilySig), label);
        }
        string? ChartOf(EmlCert cert) => cert.Grade == 'E' ? byExact.GetValueOrDefault(cert.Limit) : byFamily.GetValueOrDefault(cert.Limit);

        var rows = cas.Classes.Select(kv => new ClassRow(kv.Key, kv.Value, corrOf[kv.Key], maxLhsK[kv.Key], firstBits.GetValueOrDefault(kv.Key)))
                      .OrderByDescending(r => r.Cls.Members).ThenBy(r => r.Cert.Hex(), StringComparer.Ordinal).ToList();

        Report(dir, suffix, file, raw, lines.Count, malformed, unknownLabel, keyed, rows, ChartOf, totalBits, shortenings, shortenBits, paraphrases);
    }

    private static void Report(string dir, string suffix, string file, int raw, int unique, int malformed, int unknownLabel,
                               List<Keyed> keyed, List<ClassRow> rows, Func<EmlCert, string?> chartOf,
                               double totalBits, int shortenings, double shortenBits, int paraphrases)
    {
        int N = keyed.Count, C = rows.Count;
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine("════════════════════════════════════════════════════════════════════════════════════════════════");
        o.AppendLine($"  THE SEMANTIC-COMPRESSION AUDIT — {Path.GetFileName(dir)}/{file} · theorems by (grade · limit · rate-law) certificate");
        o.AppendLine($"    corpus  {raw} tape lines → {unique} unique mints ({malformed} malformed, {unknownLabel} unknown-label skipped) → {N} graded claims");
        o.AppendLine();

        // ── the compression verdict ──
        int single = rows.Count(r => r.Cls.Members == 1);
        int top5 = rows.Take(5).Sum(r => r.Cls.Members);
        o.AppendLine("  ── THE COMPRESSION VERDICT — honest search is incompressible; a Goodharted corpus crushes ──");
        o.AppendLine($"     {N} mints → {C} certificate-classes   ·   compression ×{(C == 0 ? 0 : (double)N / C),6:F1}   ·   semantic information Σ {totalBits,8:F1} bits");
        o.AppendLine($"     singleton classes {single} ({Pc(single, C)} of classes, {Pc(single, N)} of mints) · top-5 classes hold {top5} mints ({Pc(top5, N)})");
        foreach (var g in "EASDU")
        {
            int gm = keyed.Count(k => k.Grade == g), gc = rows.Count(r => r.Cert.Grade == g);
            if (gm > 0) o.AppendLine($"       {g}: {gm,5} mints → {gc,4} classes (×{(gc == 0 ? 0 : (double)gm / gc),5:F1})");
        }
        o.AppendLine();

        // ── the class histogram — the paraphrase families, largest first ──
        o.AppendLine("  ── THE CLASS HISTOGRAM — the paraphrase families, largest first (rep = the MDL slot) ──");
        o.AppendLine("     members  grade  rate(corr@P3)             repK  maxK  chart       representative");
        foreach (var r in rows.Take(12))
            o.AppendLine($"     {r.Cls.Members,7}  {r.Cert.Grade,5}  {RateCol(r),-24}  {r.Cls.Rep.Length,4}  {r.MaxLhsK,4}  {chartOf(r.Cert) ?? "—",-10}  {Clip(EmlRender.Render(r.Cls.Rep), 60)}");
        o.AppendLine();

        // ── the ln-family readout — the ×82 collapse ──
        var lnRows = rows.Where(r => chartOf(r.Cert) == "ln").ToList();
        int lnMints = lnRows.Sum(r => r.Cls.Members);
        o.AppendLine($"  ── THE ln-FAMILY RECEIPT — {lnMints} mints keyed to the ln limit → {lnRows.Count} classes ──");
        foreach (var r in lnRows.Take(8))
            o.AppendLine($"     {r.Cert.Grade} ×{r.Cls.Members,-5} rate {RateCol(r),-24} rep {Clip(EmlRender.Render(r.Cls.Rep), 46),-48} (K={r.Cls.Rep.Length}, paragraph K={r.MaxLhsK} demoted)");
        o.AppendLine();

        // ── MDL-within-class ──
        var lnE = lnRows.FirstOrDefault(r => r.Cert.Grade == 'E');
        o.AppendLine("  ── MDL-WITHIN-CLASS — the shortest program IS the stored representative; the paragraph demotes at 0 bits ──");
        if (lnE.Cls.Members > 0)
            o.AppendLine($"     exact-ln class: rep {EmlRender.Render(lnE.Cls.Rep)} (K={lnE.Cls.Rep.Length}) · {lnE.Cls.Members} members · longest demoted member K={lnE.MaxLhsK}");
        o.AppendLine($"     corpus-wide: {shortenings} representative-shortenings paid {shortenBits:F1} bits · {paraphrases} pure paraphrases paid ZERO");
        o.AppendLine();

        // ── pre-registered kill-lines ──
        int farmMints = rows.Where(r => r.Cls.Members > 1).Sum(r => r.Cls.Members);
        int farmClasses = rows.Count(r => r.Cls.Members > 1);
        o.AppendLine("  ── PRE-REGISTERED KILL-LINES ──");
        o.AppendLine($"     (i)  the audit: {N} mints → {C} classes (pre-reg <100)  ⇒ {(C < 100 ? "HOLDS — the corpus is paraphrase-compressible" : $"FAILS at face value ({C} classes) — diagnose, don't force")}");
        o.AppendLine($"          the diagnosis: BIMODAL — {farmMints} mints ({Pc(farmMints, N)}) sit in {farmClasses} multi-member classes (the FARM, ×{(farmClasses == 0 ? 0 : (double)farmMints / farmClasses):F1});");
        o.AppendLine($"          {C - farmClasses} singletons ({Pc(C - farmClasses, N)} of mints) are the INCOMPRESSIBLE frontier — the corpus is part-farm, part-honest, now separable");
        o.AppendLine($"          ln-family: {lnMints} mints → {lnRows.Count} classes (pre-reg ≤3)  ⇒ {(lnRows.Count <= 3 ? "HOLDS" : $"{lnRows.Count} classes — the family spans real rate tiers beyond the pre-reg")}");
        o.AppendLine(lnE.Cls.Members > 0
            ? $"     (ii) MDL demotion: exact-ln rep K={lnE.Cls.Rep.Length} vs longest member K={lnE.MaxLhsK}  ⇒ {(lnE.Cls.Rep.Length <= 7 ? "HOLDS — D=1 route holds the slot, the paragraph demoted" : "rep unexpectedly long — inspect")}"
            : "     (ii) MDL demotion: no exact-ln class in this corpus");
        o.AppendLine();

        Console.Write(o.ToString());

        // ── the durable artifacts, colocated with the corpus they audit ──
        var run = Cogito.Run.Open(dir);
        run.Write($"semantic_census_{suffix}.txt", o.ToString());
        var tsv = new StringBuilder("cert\tgrade\tmembers\trepk\tmaxk\tbits_first\tcorr_re\tcorr_im\tchart\trep\trendered\tfirst_line\n");
        foreach (var r in rows)
            tsv.AppendLine($"{r.Cert.Hex()}\t{r.Cert.Grade}\t{r.Cls.Members}\t{r.Cls.Rep.Length}\t{r.MaxLhsK}\t{r.FirstBits:F3}\t{r.Corr.Real:R}\t{r.Corr.Imaginary:R}\t{chartOf(r.Cert) ?? "—"}\t{r.Cls.Rep}\t{EmlRender.Render(r.Cls.Rep)}\t{keyed[r.Cls.FirstCapture].Prediction.Line}");
        run.Write($"semantic_classes_{suffix}.tsv", tsv.ToString());
    }

    // the rate-law column, humanized: an A-class prints its exemplar correction (corr≈1.54099 IS lnδ — the
    // machine's own dropped term), or the sub-resolution band (drift proven ≠ 0 below the witness ruler — the
    // exemplar value would print noise dressed as law); every other grade has no drift to print.
    private static string RateCol(in ClassRow r)
        => r.Cert.Grade != 'A' ? "—"
         : r.Cert.RateRe == EmlCert.SubResolution ? "sub-resolution (≠0)"
         : r.Corr.Imaginary == 0 ? $"corr≈{r.Corr.Real:G6}"
         : $"corr≈{r.Corr.Real:G4}{(r.Corr.Imaginary < 0 ? "" : "+")}{r.Corr.Imaginary:G4}i";

    private static string Pc(int n, int total) => total == 0 ? "—" : $"{100.0 * n / total:F0}%";
    private static string Clip(string s, int w) => s.Length <= w ? s : s[..(w - 1)] + "…";
}
