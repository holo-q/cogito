// ── THE INTEROCEPTIVE HOMEOSTAT ──  the operator
// applied by the machine TO ITSELF: PREDICT its own cost/criticality from accreted senses → METER the residual
// (trend excursions past a floor) → MINT an actuation → RE-CENTER (relax to rest). The slow plane (this) senses
// the machine each step and allocates the recurrence organs at sleep boundaries; the fast plane (the existing
// WeightController) stays the per-step generation limb, composed by ref as `Fast`. It regulates DAY cost only
// (aestivation is free), acts only at sleep boundaries (dead-time-safe), and NEVER actuates the intake GATE narrower
// than rest (the anti-dark-room guard: a cost-minimiser must not be able to "stop learning").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

namespace Cogito;

/// The named conditions the slow plane classifies, PRIORITY-ORDERED (safety first; on conflict the
/// lower number wins, one actuation set per sleep boundary). Each is a HomeWatch-style excursion that
/// must persist ≥2 periods (the hysteresis) before it actuates.
public enum HomeoConditions
{
    Quiet = 0,      // grok-locked + calm (surprise at/below its own home) + flat depth → feed the SOC, sleep toward rest
    Collapsing,     // collFrac high / dfThird sagging → more reality (MixEvery↓), sleep sooner
    Sealed,         // dream-era + JS→converge + thought-mint→0 → re-anchor (MixEvery↓, IntakeBatch↑)
    Hot,            // induce/gen OPB departed above its own home → sleep sooner/harder, budget↓, breach POSTPONED
    Surprised,      // excursion-mint departed above its own home → sleep-on-surprise
    Heavy,          // bits/span departed above its own home, or dream-heavy growth → budget↓, ForceGeneralize
    Stalled,        // maxSpan plateau + novelChain flat → breach next aestivation (iff NOT hot)
    Speculative,    // (Wired+) unvested-dream stock departed above ITS own home / vest-rate below — hypotheses
                    // outran corroboration → re-anchor (MixEvery↓, IntakeBatch↑). LOWEST non-Quiet priority by
                    // design: a new condition must never steal a proven winner's boundary (the Heavy-shadowed-
                    // Stalled lockout is the case law), so it fires only when nothing above it sustains.
}

/// The slow plane's policy tier: how far up the internalization ladder the actuation side
/// reaches; the senses were always L2/L3, the hands were L0. Reflex = the shipped reactive table,
/// byte-identically (the Vow arm — every banked readout rides it). Wired = the seven dark senses
/// (ExcHit · Depth · MaxSpan-EMA · raw Js · Distinct · UnvestedFrac · VestRate) gain condition consumers —
/// enriched REACTIVE policy, no forecast consumed. Predict = Wired + the L3→L4 takeover: the self-model's
/// standing next-excursion forecast LEADS/VETOES conditions through the SAME reflex table (the heuristic
/// stays in command — the forecast only moves WHEN a condition fires, never WHAT it does), and every
/// decisive lead is metered by the following aestivation's yield and VESTED per class — SPECULATE → METER →
/// VEST → RE-CENTER, the Rhythm.cs seam applied to the control law itself.
public enum HomeoPolicies : byte { Reflex, Wired, Predict }

internal enum HomeostatForecastLeadActions : byte { Ignore, Apply }

/// Legacy configuration token retained at the world boundary. Runtime policy authority is owned exclusively by Cortex.
public enum HomeoAuthorityModes : byte { Reflex, Shadow, Takeover }

/// The adaptive constants whose fixed-law decisions may be observed. The BIOS rent floor is deliberately absent.
public enum HomeostatAdaptiveConstants : byte
{
    PromoteWindowEvents,
    HomeBandK,
    HomeBandDrift,
    HomeBandFloorFrac,
    BreachQuotaBase,
    BreachAmplitude,
}

public readonly record struct HomeostatAdaptiveConstantReceipt(
    HomeostatAdaptiveConstants Constant,
    sbyte Decision,
    string Context,
    bool Paid,
    int Close);

internal readonly record struct HomeostatCheckpointDelta(
    int Cursor,
    HomeostatAdaptiveConstantReceipt[] Receipts,
    bool SharedPolicyOutcomePending,
    bool SharedPolicyDecisionInvariantClean,
    CortexPolicyDecision SharedPolicyDecision,
    bool LeadPolicyOutcomePending,
    bool LeadPolicyDecisionInvariantClean,
    CortexPolicyDecision LeadPolicyDecision)
{
    // A captured delta always carries the O(1) pending-slot replacement. The
    // default value is the only absent state used by payloads without organs.
    internal bool IsEmpty => Receipts is null;

    internal void Validate()
    {
        if (Receipts is null || Cursor < 0)
            throw new InvalidDataException("homeostat checkpoint delta is missing its receipt cursor or rows");
        if (Receipts.Length > 1_000_000)
            throw new InvalidDataException("homeostat receipt delta exceeds bound");
        for (int i = 0; i < Receipts.Length; i++)
        {
            HomeostatAdaptiveConstantReceipt receipt = Receipts[i];
            if (!Enum.IsDefined(receipt.Constant) || receipt.Decision is < -1 or > 1
                || string.IsNullOrWhiteSpace(receipt.Context) || receipt.Close < 0)
                throw new InvalidDataException("homeostat adaptive receipt is malformed");
        }
        CortexPolicyDecision sharedDecision = SharedPolicyDecision;
        CortexPolicyDecision leadDecision = LeadPolicyDecision;
        ValidatePendingSlot(SharedPolicyOutcomePending, SharedPolicyDecisionInvariantClean,
            in sharedDecision, Homeostat.PolicyID, Homeostat.PolicySchema.ActionCount, "shared");
        ValidatePendingSlot(LeadPolicyOutcomePending, LeadPolicyDecisionInvariantClean,
            in leadDecision, Homeostat.ForecastLeadPolicyID, Homeostat.ForecastLeadPolicySchema.ActionCount, "lead");
        if (SharedPolicyOutcomePending && LeadPolicyOutcomePending
            && SharedPolicyDecision.DecisionID.Equals(LeadPolicyDecision.DecisionID))
            throw new InvalidDataException("homeostat checkpoint delta reuses one decision id across pending slots");
    }

    private static void ValidatePendingSlot(
        bool pending,
        bool invariantClean,
        in CortexPolicyDecision decision,
        CortexPolicyID policy,
        int actionCount,
        string slot)
    {
        if (!pending)
        {
            if (!invariantClean || !IsDefault(in decision))
                throw new InvalidDataException($"homeostat {slot} pending slot is not default when closed");
            return;
        }
        if (decision.DecisionID.Value == 0 || !decision.Policy.Equals(policy))
            throw new InvalidDataException($"homeostat {slot} pending slot has the wrong policy decision");
        decision.Readout.Validate(actionCount);
    }

    private static bool IsDefault(in CortexPolicyDecision decision)
        => decision.DecisionID.Value == 0 && decision.Policy.Value is null && decision.Readout.Equals(default);
}

/// The per-step interoceptive vector — all fields deterministic op-counts (LAW: control on ops, log
/// the wall; wall-clock is telemetry, never a control input, or the Vow breaks). Populated by the
/// drive loop (Cortex.Drive's MODEL phase). Cost reads are per-BYTE (doing nothing can't lower them —
/// the dark-room guard).
public readonly record struct Interocept(
    // cost plane (day ops only)
    double InduceOpb,     // reinduce merges / Δtape-bytes — falls as structure repeats
    double GenOpb,        // relax ops / generated-bytes — falls as rules deepen (the DL differentiator)
    double GrowthRate,    // Δspans / step vs mint-parity
    double BitsPerSpan,   // grammar surface bits / REAL span at each sleep
    // self plane (the wire that un-darkens the self-model)
    double ExcMint, double ExcHit, double ThtMint,
    // criticality plane
    double Cvz, int Kz,   // k-aware band via the shared CV* owner (DomainMeter.CvStar) — never chase estimator noise
    // collapse plane (Goodhart-orthogonal — the sampler does NOT optimise these; the guard)
    int Distinct, int NovelChain, double CollFrac, double DfThird, double Js, bool LoopConverged,
    // comprehension plane
    double Depth, double MaxSpan, bool MomentumStalled,
    // provenance plane
    double UnvestedFrac, double VestRate,
    // phase tags
    bool ReplayEra, bool CvzMasked);

/// The ablation instrument (--sense-mask): named sense-PLANES pinned to their DARK values at POPULATION, so a
/// masked plane carries no information into the EMA/classify/actuate — the attribution knob for "which sense
/// drives the homeostat's win". A pinned sense reads as if its organ were never wired: fires-HIGH senses pin to
/// 0 (their genuine dark reading — the excursion can never trip), the two fires-LOW senses (ThtMint→Sealed,
/// DfThird→Collapsing) pin to NaN ("no reading" — every comparison is false, the condition is unreachable)
/// because 0 would JAM their conditions permanently ON (a lying sense, not a dark one). ExcMint pins to 0
/// specifically so QUIET's calm-guard still passes: a dark surprise-sense must not block rest, or masking the
/// self-stream would silently ablate QUIET (the cvz-driven relax path) too and confound the attribution.
public readonly record struct SenseMask(bool SelfStream, bool Cost, bool Collapse, bool Provenance)
{
    public static readonly SenseMask None = default;
    public bool Any => SelfStream || Cost || Collapse || Provenance;

    /// Parse the comma-list spec ("self-stream,cost" · "" = none). Unknown plane names fail LOUD — a typo'd
    /// ablation arm must never run silently unmasked.
    public static SenseMask Parse(string spec)
    {
        SenseMask m = None;
        foreach (string p in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            m = p switch
            {
                "self-stream" or "selfstream" or "self-model" or "selfmodel" => m with { SelfStream = true },
                "cost"                      => m with { Cost = true },
                "collapse"                  => m with { Collapse = true },
                "provenance"                => m with { Provenance = true },
                _ => throw new ArgumentException($"unknown sense plane '{p}' (planes: self-stream, cost, collapse, provenance)"),
            };
        return m;
    }

    /// Apply at POPULATION (the drive's Interocept fold) — the masked plane never enters the EMA, exactly as if
    /// the sense were still dark. `None` is the identity (the Vow: the unmasked machine is byte-identical).
    public Interocept Apply(in Interocept s)
    {
        Interocept m = s;
        if (Cost)       m = m with { InduceOpb = 0, GenOpb = 0, BitsPerSpan = 0 };
        if (SelfStream)  m = m with { ExcMint = 0, ExcHit = 0, ThtMint = double.NaN };
        if (Collapse)   m = m with { CollFrac = 0, DfThird = double.NaN, Js = double.NaN };
        if (Provenance) m = m with { UnvestedFrac = 0, VestRate = 0 };
        return m;
    }
}

/// The allocation the slow plane emits — the drive reads these instead of the config constants.
/// Recurrence actuators are two-sided-clamped; perception actuators (MixEvery, IntakeBatch) are
/// ONE-SIDED-OPEN toward more reality (anti-dark-room).
public readonly record struct HomeoActuation(
    double SleepFrac,     // ∈ [1/32, 1/4] — geometric sleep-stride fraction (subsumes #20: sleep when the tape grew SleepFrac× since the last)
    int MixEvery,         // ∈ [rest/4, rest] — smaller = more real re-ingest (0 stays 0: rail-off is a config MODE)
    int IntakeBatch,      // ∈ [rest, 4·rest] — floor at rest (never starve intake)
    long BudgetBits,      // ∈ [rest/2, 2·rest] (0 stays 0: unbounded is a config MODE)
    int BreachQuota,      // 0 = none; >0 → breach inside the next aestivation (iff not Hot)
    bool ForceGeneralize);// antiunify off-cadence

/// Read-only enclosure evidence for the Homeostat's finite canonical policy domain.  This is
/// deliberately separate from the streaming coverage receipt: a missing corpus state is absent
/// evidence, not a behavioral disagreement, while every query and its bounded work remains
/// attributable to the immutable publication image that supplied it.
internal readonly record struct HomeostatPolicyReadoutEnclosureReceipt(
    global::Cogito.Grammar.GrammarRevisionID Revision,
    ulong EffectiveDigest,
    int RequiredStateCount,
    int FoundStateCount,
    int MissingStateCount,
    PolicyCanonicalStateID[] FoundStates,
    PolicyCanonicalStateID[] MissingStates,
    int IndexQueries,
    int Comparisons,
    int Agreements,
    int Disagreements,
    long ScannedBytes,
    long ExpandedEdges)
{
    public bool IsComplete => RequiredStateCount > 0 && MissingStateCount == 0;
    public bool IsExact => IsComplete && Comparisons > 0 && Comparisons == Agreements && Disagreements == 0;

    internal void Validate()
    {
        if (Revision == global::Cogito.Grammar.GrammarRevisionID.Zero || EffectiveDigest == 0
            || RequiredStateCount < 0 || FoundStateCount < 0 || MissingStateCount < 0
            || FoundStateCount + MissingStateCount != RequiredStateCount
            || FoundStates is null || MissingStates is null
            || FoundStates.Length != FoundStateCount || MissingStates.Length != MissingStateCount
            || IndexQueries != RequiredStateCount || Comparisons < 0 || Agreements < 0 || Disagreements < 0
            || Agreements > Comparisons || Disagreements > Comparisons
            || ScannedBytes < 0 || ExpandedEdges < 0)
            throw new InvalidDataException("Homeostat readout enclosure receipt is malformed");
        for (int i = 1; i < FoundStates.Length; i++)
            if (FoundStates[i - 1].CompareTo(FoundStates[i]) >= 0)
                throw new InvalidDataException("Homeostat readout enclosure found states are not ordered");
        for (int i = 1; i < MissingStates.Length; i++)
            if (MissingStates[i - 1].CompareTo(MissingStates[i]) >= 0)
                throw new InvalidDataException("Homeostat readout enclosure missing states are not ordered");
        HashSet<PolicyCanonicalStateID> found = new(FoundStates);
        for (int i = 0; i < MissingStates.Length; i++)
            if (!found.Add(MissingStates[i]))
                throw new InvalidDataException("Homeostat readout enclosure repeats a state across found/missing sets");
    }
}

/// The just-finished sleep's yield — the wasted-sleep read (a cadence sleep with a no-op aestivation is
/// wasted). Filled from Consolidate's return (+ the drive's antiunify gauge). `Breached` counts the aestivation's
/// breach-and-lower ops — a aestivation that only breached still worked.
public readonly record struct ConsolidationPhaseYield(int Evicted, int Promoted, int Demoted, int Slotted, long BitsSaved, int Breached = 0);

/// The Homeostat-owned decision that authorizes a cold destination mount.  This is deliberately a
/// concrete owner receipt rather than a copy of Cortex's mutable last-readout slot: policy verification
/// performs many ordinary ChoosePolicyAction calls after a real boundary decision, so only this receipt
/// can identify the actuation that actually drove the destination handshake.
[RonObject]
public partial class HomeostatDestinationHandshakeReceipt
{
    public int schemaVersion = 2;
    public ulong decisionID;
    public string policy = "";
    public int physicalStep = -1;
    public string source = ""; // natural | explicit
    public int launchpadAction;
    public int rawCandidateAction;
    public int selectedCandidateAction;
    public int executedAction;
    public CortexPolicyAuthorities authority;
    public ulong grammarRevision;
    public CortexPolicySelectionCauses selectionCause;
    public ulong readoutFingerprint;
    public ulong readoutCandidateFingerprint;
    public ulong readoutCandidateOccurrenceDigest;
    public string policyProgram = "";
    public string policyContext = "";
    public double sleepFrac;
    public int mixEvery;
    public int intakeBatch;
    public long budgetBits;
    public int breachQuota;
    public bool forceGeneralize;
    public string receiptDigest = "";

    public string ReceiptDigest => receiptDigest;
    public ulong DecisionID => decisionID;
    public CortexPolicyID Policy => new(policy);
    public int PhysicalStep => physicalStep;
    public bool IsNatural => string.Equals(source, "natural", StringComparison.Ordinal);
    public CortexPolicyDecisionReadout Readout => new(
        launchpadAction, rawCandidateAction, selectedCandidateAction, executedAction,
            authority, new global::Cogito.Grammar.GrammarRevisionID(grammarRevision), selectionCause,
            readoutCandidateOccurrenceDigest, readoutCandidateFingerprint);
    public HomeostatPolicyProgram Program => HomeostatPolicyProgram.ParseToken(policyProgram);
    public HomeostatPolicyContext Context => HomeostatPolicyContext.ParseToken(policyContext);
    public HomeoActuation Actuation => new(sleepFrac, mixEvery, intakeBatch, budgetBits, breachQuota, forceGeneralize);

    public string ComputeDigest()
    {
        string canonical = string.Join('|',
            schemaVersion, decisionID, policy, physicalStep, source,
            launchpadAction, rawCandidateAction, selectedCandidateAction, executedAction,
            authority, grammarRevision.ToString(CultureInfo.InvariantCulture), selectionCause,
            readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            readoutCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            readoutCandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture),
            policyProgram, policyContext,
            sleepFrac.ToString("R", CultureInfo.InvariantCulture), mixEvery, intakeBatch, budgetBits,
            breachQuota, forceGeneralize ? 1 : 0, "homeostat-destination-handshake-v2");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public void Validate()
    {
        if (schemaVersion != 2 || decisionID == 0 || !string.Equals(policy, Homeostat.PolicyID.Value, StringComparison.Ordinal)
            || physicalStep < 0 || source is not ("natural" or "explicit")
            || readoutFingerprint == 0 || grammarRevision == 0
            || string.IsNullOrWhiteSpace(policyProgram) || string.IsNullOrWhiteSpace(policyContext)
            || !string.Equals(receiptDigest, ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("Homeostat destination handshake receipt is not self-verified");
        Readout.Validate(Homeostat.PolicySchema.ActionCount);
        HomeostatPolicyProgram.Validate(Program);
        HomeostatPolicyContext context = Context;
        HomeostatPolicyProgram program = Program;
        HomeoActuation actuation = Actuation;
        CortexPolicyDecisionReadout readout = Readout;
        Homeostat.ValidateDestinationProgram(in readout, in context, in program, in actuation);
        if (!string.Equals(program.RenderToken(), policyProgram, StringComparison.Ordinal)
            || readoutFingerprint != GrammarPolicyReadout.ComputeFingerprint(Readout.GrammarRevision, Policy)
            || Readout.ReadoutCandidateFingerprint != readoutCandidateFingerprint
            || Readout.ReadoutCandidateOccurrenceDigest != readoutCandidateOccurrenceDigest)
            throw new InvalidDataException("Homeostat destination handshake policy identity is inconsistent");
        if (Readout.ExecutedAction < 0 || Readout.ExecutedAction >= Homeostat.PolicySchema.ActionCount)
            throw new InvalidDataException("Homeostat destination handshake action is outside its schema");
    }

    public void ValidateForPhysicalStep(int expectedStep)
    {
        Validate();
        if (physicalStep != expectedStep)
            throw new InvalidDataException($"Homeostat destination handshake step {physicalStep} disagrees with expected step {expectedStep}");
    }

    public static byte[] Encode(in HomeostatDestinationHandshakeReceipt receipt)
    {
        receipt.Validate();
        return RonSerializer.SerializeToUtf8(in receipt);
    }

    public static HomeostatDestinationHandshakeReceipt Decode(ReadOnlySpan<byte> bytes)
    {
        HomeostatDestinationHandshakeReceipt receipt = RonSerializer.Deserialize<HomeostatDestinationHandshakeReceipt>(bytes);
        receipt.Validate();
        return receipt;
    }
}

/// The two-plane interoceptive controller. FAST plane = the existing WeightController (generation
/// limb, unchanged, composed by ref). SLOW plane = this: EMA the senses over ≥2 sleep periods,
/// classify into a condition with streak-hysteresis, actuate one notch at the sleep boundary, relax
/// to rest otherwise. Save/Load whole (checkpoint-resumable, the Vow across kill/resume).
public sealed class Homeostat
{
    public static readonly CortexPolicyID PolicyID = new("homeostat.actuation");
    public static readonly CortexPolicyID ForecastLeadPolicyID = new("homeostat.forecast-lead");
    private static readonly HomeostatPolicyProgram[] SharedPolicyActions = CreateSharedPolicyActions();
    public static readonly CortexPolicySchema PolicySchema = new(
        PolicyID, HomeostatPolicyFeatures.Count, SharedPolicyActions.Length, outcomeCount: 2,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);
    public static readonly CortexPolicySchema ForecastLeadPolicySchema = new(
        ForecastLeadPolicyID, featureCount: 12, actionCount: 2, outcomeCount: 2,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);

    private enum SharedPolicyMetricIDs : ushort
    {
        FeatureBase = 400,
        Productive = 500,
        StructuralDelta,
    }

    internal static MetricID GetPolicyFeatureMetricID(int featureIndex)
    {
        if ((uint)featureIndex >= (uint)PolicySchema.FeatureCount)
            throw new ArgumentOutOfRangeException(nameof(featureIndex));
        return new MetricID(checked((ushort)((ushort)SharedPolicyMetricIDs.FeatureBase + featureIndex)));
    }

    private enum ForecastLeadMetricIDs : ushort
    {
        LeadClass = 520,
        Criticality,
        CriticalitySamples,
        ExperienceMint,
        CollapseFraction,
        ThirdDerivativeFraction,
        MaximumSpan,
        NovelChain,
        AccuracyDeparture,
        PredictsCriticalityRise,
        PredictsCoverageFall,
        PredictsDepthLoss,
        Productive = 540,
        StructuralDelta,
    }

    private readonly record struct PendingConstantDecision(HomeostatAdaptiveConstants Constant, sbyte Decision, string Context);

    // ── the SOC floor + noise-corrected criticality band — read off the ONE shared CV* owner
    //    (DomainMeter.CvStar, the k-bell's home; C1=C2: the bell owner claims the threshold). ──
    const double SocFloor = 0.15, BandSigmas = 1.5;
    public static double CvStar(double cv, int k) => DomainMeter.CvStar(cv, k, SocFloor, BandSigmas);
    public static bool KAwareLock(double cv, int k)
        => !double.IsNaN(cv) && k >= 2 && cv < CvStar(cv, k);

    readonly HomeoActuation _rest;          // the config rest-point every actuator relaxes toward
    readonly double _gain;                  // integral gain per sleep (~5%/sleep), small — no ratchet
    HomeoActuation _act;                    // the live actuation the drive reads

    // EMA state (≥2-period horizon = dead-time safety: the controller can't act faster than it measures)
    Interocept _ema; bool _seeded;
    readonly double _alpha;                 // EMA weight (≈ 1/periods)

    // per-condition consecutive-period streaks (the hysteresis)
    const int NConds = 8;                   // the HomeoConditions cardinality (streaks + census size off it)
    readonly int[] _streak = new int[NConds];
    const int LockRounds = 2;               // a condition must persist ≥2 periods before it actuates

    // the cvz mask horizon (bell-vs-breach: while a breach re-heats cvz, QUIET skips the period and the
    // fast plane rides NaN — it cannot cool on breach-heated readings)
    bool _cvzMasked;

    readonly HomeoPolicies _policy;         // the actuation side's internalization tier (Reflex = the Vow arm)
    readonly HomeostatAutonomyModes _autonomyMode;
    readonly bool _observeAdaptiveConstants;
    bool _sharedPolicyOutcomePending;
    CortexPolicyDecision _sharedPolicyDecision;
    bool _sharedPolicyDecisionInvariantClean = true;
    private readonly record struct SharedPolicyOccurrenceCheckKey(
        ulong ActiveReadoutFingerprint,
        ulong CandidateFingerprint,
        ulong OccurrenceDigest,
        global::Cogito.Grammar.GrammarRevisionID Revision,
        PolicyCanonicalStateID CanonicalState)
    {
        internal bool IsValid => ActiveReadoutFingerprint != 0 && CandidateFingerprint != 0
            && OccurrenceDigest != 0 && Revision != global::Cogito.Grammar.GrammarRevisionID.Zero
            && CanonicalState.Version != 0;
    }

    SharedPolicyOccurrenceCheckKey _lastSharedPolicyOccurrenceCheckKey;
    CortexPolicyDecision _lastBoundaryPolicyDecision;
    HomeostatPolicyContext _lastBoundaryPolicyContext;
    PolicyCanonicalStateID _lastBoundaryCanonicalState;
    HomeostatPolicyProgram _lastBoundaryPolicyProgram;
    HomeoActuation _lastBoundaryPolicyActuation;
    int _lastBoundaryPolicyStep = -1;

    public Homeostat(WeightController fast, HomeoActuation rest, double gain = 0.05, int periods = 2, double mintParity = 4,
                     HomeoPolicies policy = HomeoPolicies.Reflex, ulong seed = 0,
                     HomeoAuthorityModes authority = HomeoAuthorityModes.Reflex,
                     bool observeAdaptiveConstants = false,
                     HomeostatAutonomyModes autonomy = HomeostatAutonomyModes.Off)
    {
        if (!Enum.IsDefined(autonomy)) throw new ArgumentOutOfRangeException(nameof(autonomy));
        Fast = fast; _rest = rest; _act = rest; _gain = gain; _alpha = 1.0 / Math.Max(1, periods);
        _mintParity = mintParity; _policy = policy;
        _observeAdaptiveConstants = observeAdaptiveConstants; _autonomyMode = autonomy;
        _leadOutcomes = new OutcomeMeter<int>(new int[] { LeadSurprised, LeadCollapsing }, LeadYieldDrift);
        if (authority != HomeoAuthorityModes.Reflex)
            throw new ArgumentException("Homeostat authority is retired; Cortex owns published-grammar authority", nameof(authority));
    }

    /// The fast plane — the per-step generation limb (WeightController), unchanged. The drive still
    /// calls Fast.Nudge each step; the homeostat only MASKS the cvz it feeds (LAW B) and owns the
    /// slow-cadence knobs below.
    public WeightController Fast { get; }

    // the boundary knobs the drive reads instead of CortexRunConfig constants
    public double SleepFrac      => _act.SleepFrac;
    public int    MixEvery       => _act.MixEvery;
    public int    IntakeBatch    => _act.IntakeBatch;
    public long   BudgetBits     => _act.BudgetBits;
    public int    BreachQuota    => _act.BreachQuota;
    public bool   ForceGeneralize=> _act.ForceGeneralize;
    public bool   CvzMasked      => _cvzMasked;
    internal double CurrentCriticality => _ema.Cvz;
    /// The exact decision that drove the last boundary actuation.  Boundary
    /// custody reads this identity rather than rebuilding one from a readout.
    internal CortexPolicyDecision LastBoundaryPolicyDecision => _lastBoundaryPolicyDecision;
    /// The canonical state is captured at the Homeostat boundary because the
    /// decision's readout context is transient and is not carried by every tape
    /// or checkpoint representation of that decision.
    internal bool TryReadLastBoundaryCanonicalState(
        out PolicyCanonicalStateID canonicalState,
        out int boundaryStep)
    {
        canonicalState = _lastBoundaryCanonicalState;
        boundaryStep = _lastBoundaryPolicyStep;
        return boundaryStep >= 0 && canonicalState.IsValidFor(PolicyID);
    }
    /// The last boundary's winning condition (null = nothing sustained → relaxed) — the trace telegraph.
    public HomeoConditions? LastCondition { get; private set; }
    /// The policy plane is enriched beyond Reflex (Wired/Predict) — gates the homeostat.txt landing so the
    /// Reflex arm's run dir stays artifact-identical to the pre-wave machine.
    public bool PolicyArmed => _policy != HomeoPolicies.Reflex || _autonomyMode != HomeostatAutonomyModes.Off;
    public bool Autonomic => _autonomyMode != HomeostatAutonomyModes.Off;
    /// Sleeps whose aestivation moved nothing (no evict/promote/demote/slot, zero bits saved) — the kill-line's
    /// wasted-sleep count (the homeostat should sleep when there is work, so this must FALL vs the fixed cadence).
    public int WastedSleeps { get; private set; }
    /// Seriate boundaries closed (the wasted-sleep denominator).
    public int SleepsClosed { get; private set; }

    /// The geometric sleep-stride query (subsumes task #20): sleep when the tape grew ≥ SleepFrac of itself
    /// since the last sleep — O(Δ) spacing that stretches as the tape grows, tightened by the conditions above.
    public bool SleepDue(long tapeBytes, long lastSleepBytes)
        => tapeBytes - lastSleepBytes >= Math.Max(1L, (long)(_act.SleepFrac * tapeBytes));

    /// Called per step from Cortex.Drive's MODEL phase with the freshly-folded Interocept (beside the
    /// controller.Nudge site). Masks cvz per the breach horizon, EMAs the vector. Returns the masked
    /// cvz the fast plane should Nudge on (NaN while masked → the fast plane can't cool on
    /// breach-heated readings, for free). `excPred`/`excArm` = the self-model's STANDING next-excursion
    /// forecast — a token, not a scalar, so it rides beside the
    /// Interocept, never through its EMA; the Predict tier's Classify navigates the latest one at the close.
    /// The drive feeds "·" when predictive homeostasis is disabled (and pins it "·" under a self-model sense-mask —
    /// a masked plane's forecast is no forecast).
    public double SenseStep(in Interocept s, string excPred = "·", char excArm = 'x')
    {
        _predToken = excPred; _predArm = excArm;
        double cvz = _cvzMasked ? double.NaN : s.Cvz;
        Interocept m = s with { Cvz = cvz, CvzMasked = _cvzMasked };
        _ema = _seeded ? Ema(_ema, m, _alpha) : m;
        _seeded = true;
        return cvz;
    }

    /// Open/close the cvz mask around a breach (Cortex sets this when a breach fires at sleep n; clears
    /// at the first re-induce after LOWER completes). While masked, the bell reads post-LOWER only.
    public void MaskCvz(bool on) => _cvzMasked = on;

    /// Called at each sleep boundary (the slept branch of Cortex.Drive). Classifies the EMA'd senses,
    /// actuates ONE notch on the winning condition (or relaxes toward rest), returns the new
    /// allocation. `aestivation` carries the just-finished sleep's yield (the wasted-sleep read).
    /// On the Wired+ tiers this is also the POLICY-VESTING boundary (the Rhythm.cs seam): the aestivation that
    /// just ran is the METER for the lead speculated at the PREVIOUS close (a lead tightens the stride so
    /// the aestivation lands early — an early aestivation that finds work paid, one that finds nothing didn't; the
    /// wasted-bit is the law's one standing outcome sensor, ), and the (condition, yield)
    /// pair folds into the policy channel — the control law becoming a predictable stream.
    public HomeoActuation CloseSleep(Cortex cortex, in ConsolidationPhaseYield aestivation)
    {
        SleepsClosed++;
        bool aestivationWasted = aestivation is { Evicted: 0, Promoted: 0, Demoted: 0, Slotted: 0, BitsSaved: 0, Breached: 0 };
        if (aestivationWasted) WastedSleeps++;
        long structuralDelta = aestivation.Evicted + aestivation.Promoted + aestivation.Demoted
            + aestivation.Slotted + aestivation.BitsSaved + aestivation.Breached;
        if (_sharedPolicyOutcomePending)
        {
            Span<MetricSample> outcomes = stackalloc MetricSample[2]
            {
                new(new MetricID((ushort)SharedPolicyMetricIDs.Productive), NumericValue.FromI64(aestivationWasted ? 0 : 1)),
                new(new MetricID((ushort)SharedPolicyMetricIDs.StructuralDelta), NumericValue.FromI64(structuralDelta)),
            };
            cortex.ResolvePolicyOutcome(in _sharedPolicyDecision, outcomes, _sharedPolicyDecisionInvariantClean, conservedCost: 1);
            _sharedPolicyOutcomePending = false;
            _sharedPolicyDecisionInvariantClean = true;
            _sharedPolicyDecision = default;
        }
        if (_leadPolicyOutcomePending)
        {
            Span<MetricSample> outcomes = stackalloc MetricSample[2]
            {
                new(new MetricID((ushort)ForecastLeadMetricIDs.Productive), NumericValue.FromI64(aestivationWasted ? 0 : 1)),
                new(new MetricID((ushort)ForecastLeadMetricIDs.StructuralDelta), NumericValue.FromI64(structuralDelta)),
            };
            cortex.ResolvePolicyOutcome(in _leadPolicyDecision, outcomes, _leadPolicyDecisionInvariantClean, conservedCost: 1);
            _leadPolicyOutcomePending = false;
            _leadPolicyDecisionInvariantClean = true;
            _leadPolicyDecision = default;
        }
        bool journal = _policy != HomeoPolicies.Reflex || Autonomic;
        ResolveConstantOutcomes(aestivationWasted);
        if (journal && _leadOutcomes.PendingIndex >= 0)
        {
            double y = aestivationWasted ? 0 : 1;
            int pendingLead = _leadOutcomes.ArmAt(_leadOutcomes.PendingIndex);
            _leadOutcomes.Meter(y);
            OutcomeArmState lead = _leadOutcomes.Read(pendingLead);
            Cogito.Trace.Cortex.Boundary("homeo.vest", $"lead {FormatLeadName(pendingLead)} {(y > 0 ? "PAID" : "wasted")} · yield⌂={lead.YieldEma:F2} outcomes={lead.Outcomes}");
        }
        Interocept s = _ema;
        HomeoConditions? cond = Classify(cortex, s, aestivationWasted);
        LastCondition = cond;
        _census[cond is null ? NConds : (int)cond.Value]++;   // the boundary census (all tiers — pure counter, the A/B table's spine)
        _prevNovelChain = s.NovelChain;                    // the Stalled flatness anchor — period-scale, one sample per close
        _prevMaxSpan = s.MaxSpan;                          // the Stalled span-flatness anchor (Wired) — same Δ-band shape, same cadence
        HomeoActuation before = _act;
        int reflexBreachAmplitude;
        HomeostatPolicyProgram reflexProgram;
        HomeoActuation reflex = ComputeReflexActuation(cond, s, aestivationWasted, out reflexBreachAmplitude, out reflexProgram);
        HomeostatPolicyContext context = HomeostatPolicyContext.From(cond, aestivationWasted, s.GrowthRate > _mintParity);
        HomeostatPolicyInput input = new(context, s, before);
        Span<MetricSample> policyFeatures = stackalloc MetricSample[HomeostatPolicyFeatures.Count];
        ReadSharedPolicyFeatures(in input, policyFeatures);
        int reflexAction = FindSharedPolicyAction(in reflexProgram);
        PolicyCanonicalStateID canonicalState = HomeostatPolicyFeatures.ReadCanonicalState(in input);
        SelectAndApplySharedPolicy(cortex, in context, in canonicalState, policyFeatures,
            reflexAction, in reflexProgram, cond, in before, in reflex, reflexBreachAmplitude,
            out CortexPolicyDecision sharedDecision, out HomeostatPolicyProgram executedProgram,
            out HomeoActuation sharedAction);
        _sharedPolicyDecision = sharedDecision;
        _act = sharedAction;
        _lastBoundaryPolicyDecision = _sharedPolicyDecision;
        _lastBoundaryPolicyContext = context;
        _lastBoundaryCanonicalState = canonicalState;
        _lastBoundaryPolicyProgram = executedProgram;
        _lastBoundaryPolicyActuation = _act;
        _lastBoundaryPolicyStep = cortex.Step;
        if (cond is not null) CountReversals(before, _act);
        if (journal)
        {
            if (_lastDecisive >= 0)
                _leadOutcomes.Pend((int)_lastDecisive);
        }
        return _act;
    }

    /// Capture the exact Homeostat policy actuation that authorizes a cold destination. A natural close is
    /// reused verbatim; a cold step with no close creates one ordinary policy decision from the real step EMA,
    /// applies its selected program, and leaves its outcome pending for the next real sleep.
    internal HomeostatDestinationHandshakeReceipt CreateDestinationHandshake(Cortex cortex, int physicalStep, bool forceExplicit = false)
    {
        if (physicalStep < 0) throw new ArgumentOutOfRangeException(nameof(physicalStep));
        CortexPolicyDecision decision;
        HomeostatPolicyContext context;
        HomeostatPolicyProgram program;
        HomeoActuation actuation;
        string source;
        if (!forceExplicit && _lastBoundaryPolicyStep == physicalStep)
        {
            decision = _lastBoundaryPolicyDecision;
            context = _lastBoundaryPolicyContext;
            program = _lastBoundaryPolicyProgram;
            actuation = _lastBoundaryPolicyActuation;
            source = "natural";
        }
        else
        {
            context = HomeostatPolicyContext.From(LastCondition, previousConsolidationPhaseWasted: false, _ema.GrowthRate > _mintParity);
            HomeostatPolicyInput input = new(context, _ema, _act);
            Span<MetricSample> features = stackalloc MetricSample[HomeostatPolicyFeatures.Count];
            ReadSharedPolicyFeatures(in input, features);
            HomeostatPolicyProgram reflexProgram = CompileReflexProgram(in context);
            int launchpadAction = FindSharedPolicyAction(in reflexProgram);
            PolicyCanonicalStateID canonicalState = HomeostatPolicyFeatures.ReadCanonicalState(in input);
            HomeoActuation reflex = reflexProgram.Execute(_act, _rest, _gain, _breachAmp);
            int reflexBreachAmplitude = context.Condition == HomeostatPolicyConditions.Stalled
                ? Math.Min(_breachAmp * 2, BreachQuotaBase * 16)
                : BreachQuotaBase;
            SelectAndApplySharedPolicy(cortex, in context, in canonicalState, features,
                launchpadAction, in reflexProgram, LastCondition, in _act, in reflex, reflexBreachAmplitude,
                out decision, out program, out actuation);
            source = "explicit";
        }

        if (!decision.Policy.Equals(PolicyID) || decision.DecisionID.Value == 0)
            throw new InvalidDataException("Homeostat destination handshake is not owned by the Homeostat policy");
        CortexPolicyDecisionReadout readout = decision.Readout;
        readout.Validate(PolicySchema.ActionCount);
        ulong fingerprint = GrammarPolicyReadout.ComputeFingerprint(readout.GrammarRevision, PolicyID);
        if (readout.GrammarRevision == global::Cogito.Grammar.GrammarRevisionID.Zero || fingerprint == 0)
            throw new InvalidDataException("Homeostat destination handshake has no published policy revision");
        HomeostatDestinationHandshakeReceipt receipt = new()
        {
            decisionID = decision.DecisionID.Value,
            policy = PolicyID.Value,
            physicalStep = physicalStep,
            source = source,
            launchpadAction = readout.LaunchpadAction,
            rawCandidateAction = readout.RawCandidateAction,
            selectedCandidateAction = readout.SelectedCandidateAction,
            executedAction = readout.ExecutedAction,
            authority = readout.Authority,
            grammarRevision = readout.GrammarRevision.Value,
            selectionCause = readout.SelectionCause,
            readoutFingerprint = fingerprint,
            readoutCandidateFingerprint = readout.ReadoutCandidateFingerprint,
            readoutCandidateOccurrenceDigest = readout.ReadoutCandidateOccurrenceDigest,
            policyProgram = program.RenderToken(),
            policyContext = context.RenderToken(),
            sleepFrac = actuation.SleepFrac,
            mixEvery = actuation.MixEvery,
            intakeBatch = actuation.IntakeBatch,
            budgetBits = actuation.BudgetBits,
            breachQuota = actuation.BreachQuota,
            forceGeneralize = actuation.ForceGeneralize,
        };
        receipt.receiptDigest = receipt.ComputeDigest();
        receipt.Validate();
        return receipt;
    }

    private void SelectAndApplySharedPolicy(
        Cortex cortex,
        in HomeostatPolicyContext context,
        in PolicyCanonicalStateID canonicalState,
        ReadOnlySpan<MetricSample> features,
        int reflexAction,
        in HomeostatPolicyProgram reflexProgram,
        HomeoConditions? condition,
        in HomeoActuation before,
        in HomeoActuation reflex,
        int reflexBreachAmplitude,
        out CortexPolicyDecision decision,
        out HomeostatPolicyProgram executedProgram,
        out HomeoActuation actuation)
    {
        Cortex.CortexPolicyActionPreparation prepared = cortex.PreparePolicyAction(
            PolicyID, reflexAction, in canonicalState, true, features, ReadOnlySpan<MetricID>.Empty);
        _sharedPolicyOutcomePending = true;
        _sharedPolicyDecisionInvariantClean = true;
        try
        {
            TryGrantSharedPolicyAuthority(cortex, in prepared);
            decision = cortex.ChoosePreparedPolicyAction(prepared, features, ReadOnlySpan<MetricID>.Empty);
        }
        catch
        {
            cortex.DiscardPreparedPolicyAction(in prepared);
            throw;
        }
        _sharedPolicyDecision = decision;
        HomeostatPolicyProgram sharedProgram = SharedPolicyActions[decision.Action];
        HomeoActuation proposed = sharedProgram.Execute(before, _rest, _gain, _breachAmp);
        int nextBreachAmplitude = decision.Authority == CortexPolicyAuthorities.Grammar
            ? ComputeCandidateBreachAmplitude(condition, proposed)
            : reflexBreachAmplitude;
        if (ApplyPolicyProgram(in context, sharedProgram, in proposed, nextBreachAmplitude, out actuation))
        {
            executedProgram = sharedProgram;
            return;
        }
        _sharedPolicyDecisionInvariantClean = false;
        actuation = reflex;
        _act = reflex;
        _breachAmp = reflexBreachAmplitude;
        executedProgram = reflexProgram;
    }

    /// The aestivation that breached spends its grant (the drive calls this at breach fire): Stalled re-grants at the
    /// next close — the oscillate cadence, one inhale per aestivation — while a non-Stalled close must never carry a
    /// stale grant into a later aestivation (Actuate mutates FROM the live actuation, so an unspent quota would ride
    /// every condition that doesn't explicitly touch it).
    public void SpendBreach() => _act = _act with { BreachQuota = 0 };

    // ── C2's NO-THRASH REGISTER (bell-vs-breach kill-line #4) ──  a CONDITION-driven move that flips an
    // actuator's direction within ReversalHorizon closes of its previous condition-driven move is the
    // controller fighting itself — the thrash the cvz mask exists to prevent; must stay 0 with breach firing.
    // Relaxation is the sanctioned return to rest and never counts. Dials 0..3 = SleepFrac · MixEvery ·
    // IntakeBatch · BudgetBits (BreachQuota is a grant, not a dial).
    public const int ReversalHorizon = 2 * LockRounds;
    public int SignReversals { get; private set; }
    readonly sbyte[] _lastDir = new sbyte[4];
    readonly int[] _lastMoveAt = [int.MinValue / 2, int.MinValue / 2, int.MinValue / 2, int.MinValue / 2];

    void CountReversals(in HomeoActuation before, in HomeoActuation after)
    {
        Span<double> d =
        [
            after.SleepFrac - before.SleepFrac,
            after.MixEvery - before.MixEvery,
            after.IntakeBatch - before.IntakeBatch,
            after.BudgetBits - before.BudgetBits,
        ];
        for (int i = 0; i < d.Length; i++)
        {
            sbyte dir = (sbyte)Math.Sign(d[i]);
            if (dir == 0) continue;
            if (dir == -_lastDir[i] && SleepsClosed - _lastMoveAt[i] <= ReversalHorizon) SignReversals++;
            _lastDir[i] = dir;
            _lastMoveAt[i] = SleepsClosed;
        }
    }

    // ── classify: priority-ordered, streak-hysteresis. Returns the winning condition or null (relax). ──
    HomeoConditions? Classify(Cortex cortex, in Interocept s, bool aestivationWasted)
    {
        // raw excursion tests (period-level); a condition fires only after LockRounds consecutive periods.
        // The banded senses fold their close-cadence Homes FIRST, unconditionally and in fixed order — every
        // band observes every close exactly once, or its baseline goes stale/order-dependent (no short-circuit).
        int excDir  = _excBand.Observe(s.ExcMint);
        int iopbDir = _iopbBand.Observe(s.InduceOpb), gopbDir = _gopbBand.Observe(s.GenOpb);
        int bitsDir = _bitsBand.Observe(s.BitsPerSpan);
        ObserveHomeBandConstants("exc", excDir);
        ObserveHomeBandConstants("iopb", iopbDir);
        ObserveHomeBandConstants("gopb", gopbDir);
        ObserveHomeBandConstants("bits", bitsDir);
        // ── the WIRED layer: the seven folded-but-unread senses gain
        // consumers. The folds are gated on the tier — Reflex must remain the untouched code path (the Vow),
        // and a per-run policy is checkpoint-constant, so the bands can never see a mixed fold history.
        // Same fixed-order unconditional-within-the-arm law as the four bands above.
        bool wired = _policy != HomeoPolicies.Reflex;
        int hitDir = 0, depthDir = 0, jsDir = 0, distinctDir = 0, unvDir = 0, vestDir = 0;
        bool spanFlat = true;
        if (wired)
        {
            hitDir      = _hitBand.Observe(s.ExcHit);         // self-model lifetime accuracy — the forecaster's earned trust
            depthDir    = _depthBand.Observe(s.Depth);        // held-out sym/byte — Quiet's documented-but-never-wired "flat depth" leg
            jsDir       = _jsBand.Observe(s.Js);              // raw generation-to-generation JS — Sealed's approach sense (NaN-guarded by HomeBand)
            distinctDir = _distinctBand.Observe(s.Distinct);  // block diversity — Collapsing's departure leg (the two absolute cuts kept beside it)
            unvDir      = _unvBand.Observe(s.UnvestedFrac);   // hypothesis stock — Speculative's overhang leg (provenance plane, un-darkened)
            vestDir     = _vestBand.Observe(s.VestRate);      // corroboration rate — Speculative's sag leg
            ObserveHomeBandConstants("hit", hitDir);
            ObserveHomeBandConstants("depth", depthDir);
            ObserveHomeBandConstants("js", jsDir);
            ObserveHomeBandConstants("distinct", distinctDir);
            ObserveHomeBandConstants("unvested", unvDir);
            ObserveHomeBandConstants("vest", vestDir);
            // MaxSpan-EMA: Δ-flat across closes (the novelChain shape) — RELATIVE band, maxSpan spans decades.
            spanFlat = !double.IsNaN(_prevMaxSpan)
                       && Math.Abs(s.MaxSpan - _prevMaxSpan) <= SpanFlatFrac * Math.Max(Math.Abs(_prevMaxSpan), Math.Abs(s.MaxSpan));
        }
        // ── the PREDICT layer (L3→L4): navigate the standing next-excursion forecast through the SAME table.
        // The probe alphabet is Reads' HomeWatch("zcsd"): z=CvZ (+ = de-grokking), c=Coverage (− = regressing),
        // s=MaxSpan (+ = deepening), d=Depth sym/byte (+ = shallowing). Leads pre-fire the two conditions whose
        // probe-classes map (Surprised ← any destabilization forecast; Collapsing ← regression forecast);
        // vetoes hold Quiet (don't rest into forecast turbulence) and Stalled (don't spend the O(quota·tape)
        // breach when depth motion is forecast). Hot/Heavy/Sealed stay arrived-only — the zcsd alphabet carries
        // no cost/loop probe, and a lead without a probe would be invented signal (a future sense-LIFT extends
        // the map). All of it rides the TRUST GATE: leads/vetoes only while the forecaster's lifetime accuracy
        // sits at/above its own home (hitDir ≥ 0) — an inaccurate self-model gets no vote (the habit-lock guard's
        // first wall; the vest meter in CloseSleep is the second).
        bool trust = _policy == HomeoPolicies.Predict && hitDir >= 0;
        bool predictsCriticalityRise = _predToken.Contains("z+");
        bool predictsCoverageFall = _predToken.Contains("c-");
        bool predictsDepthLoss = _predToken.Contains("d+");
        bool destab = trust && (predictsCriticalityRise || predictsCoverageFall || predictsDepthLoss);
        bool collwd = trust && (predictsCoverageFall || predictsDepthLoss);
        bool deepwd = trust && _predToken.Contains("s+");
        CortexPolicyDecision surprisedDecision = default;
        CortexPolicyDecision collapsingDecision = default;
        bool applySurprised = destab && ChooseForecastLead(cortex, LeadSurprised, s, hitDir,
            predictsCriticalityRise, predictsCoverageFall, predictsDepthLoss, out surprisedDecision);
        bool applyCollapsing = collwd && ChooseForecastLead(cortex, LeadCollapsing, s, hitDir,
            predictsCriticalityRise, predictsCoverageFall, predictsDepthLoss, out collapsingDecision);

        Span<bool> raw = stackalloc bool[NConds];
        bool collArrived = s.CollFrac > 0.5 || s.DfThird < 0.85 || (wired && distinctDir < 0);
        raw[(int)HomeoConditions.Collapsing] = collArrived || applyCollapsing;
        raw[(int)HomeoConditions.Sealed]     = s.ReplayEra && (s.LoopConverged || (wired && jsDir < 0)) && s.ThtMint < 1e-3;
        raw[(int)HomeoConditions.Hot]        = iopbDir > 0 || gopbDir > 0;
        raw[(int)HomeoConditions.Surprised]  = excDir > 0 || applySurprised;
        raw[(int)HomeoConditions.Heavy]      = bitsDir > 0 || s.GrowthRate > _mintParity;
        // Stalled reads novelChain FLAT (the enum's spec) — a Δ-band across closes, NOT an absolute floor: a
        // healthy sampler on a grokked grammar keeps recombining (nc 4-7 forever), so `nc ≤ 3` was a near-collapse
        // read that made Stalled unreachable on every measured world (trunk_0131..0139) while the machine sat in
        // Quiet-locked equilibrium ON TOP of greedy-unreachable depth — the exact plateau breach exists for.
        // Wired adds the maxSpan Δ-flat leg (the enum spec's "maxSpan plateau", finally read off the EMA'd sense
        // itself — a maxSpan still climbing while savings walls is deepening, not a stall; the ladder's measured
        // 170→170B stall stays flat under it). Predict adds the deepening VETO — hold the breach grant when the
        // machine's own forecast says depth motion is imminent.
        bool stallArrived = s.MomentumStalled && !double.IsNaN(_prevNovelChain)
                            && Math.Abs(s.NovelChain - _prevNovelChain) <= _chainFlatBand
                            && (!wired || spanFlat);
        if (stallArrived && deepwd) _vetoStalled++;
        raw[(int)HomeoConditions.Stalled]    = stallArrived && !deepwd;
        // Speculative (Wired+): the provenance plane's condition — unvested stock departed above its own home,
        // or the vest-rate below: dreams outran the reality corroborating them → re-anchor. Pure departure, no
        // era gate (pre-dream both senses sit flat at 0 inside home and can never fire; under rhythm dreams
        // precede exhaustion, so an era gate would blind exactly the arm that needs it).
        raw[(int)HomeoConditions.Speculative] = wired && (unvDir > 0 || vestDir < 0);
        // QUIET only when genuinely grok-locked (k-aware, never noise), calm, AND the aestivation shift found nothing
        // to do — and NOT while masked. Calm = surprise did not depart ABOVE home this close (a masked ExcMint
        // pins 0 → seeds home 0, never departs → a dark surprise-sense still cannot block rest, the SenseMask
        // law). The wasted-aestivation gate replaces the retired absolute calm level's hidden second duty: a pure
        // departure test reads a self-model-hot world as "calm" at its own elevated normal and rests it to
        // death (measured: Quiet 14 closes, WALL death at step 63 — the blinded-arm anatomy). A aestivation that
        // MOVED things is the machine's own counter-evidence to "nothing to consolidate"; requiring the wasted
        // read also closes a negative feedback — rest stretches the stride, stretched aestivations accumulate work,
        // working aestivations block further rest.
        // Wired adds the enum spec's flat-depth leg (a departing depth = still moving, not quiet) and the
        // accuracy leg (a self-model losing the thread of its own dynamics — hit rate departing BELOW its home —
        // is not a machine that understands itself well enough to rest, even at a calm mint level: the mint can
        // idle while the hit split degrades toward the fallback arm). Predict adds the turbulence veto.
        bool quietArrived = !s.CvzMasked && KAwareLock(s.Cvz, s.Kz) && aestivationWasted
                            && excDir <= 0 && !raw[(int)HomeoConditions.Heavy];
        if (wired && quietArrived)
        {
            if (hitDir < 0)   { _quietHitBlocks++;   quietArrived = false; }
            else if (depthDir != 0) { _quietDepthBlocks++; quietArrived = false; }
        }
        if (quietArrived && destab) _vetoQuiet++;
        raw[(int)HomeoConditions.Quiet]      = quietArrived && !destab;
        // the wired-leg census (the "no dead sensors" readout — each consumer's contribution counted)
        if (wired)
        {
            if (distinctDir < 0) _cntDistinctLeg++;
            if (s.ReplayEra && !s.LoopConverged && jsDir < 0 && s.ThtMint < 1e-3) _cntJsLeg++;
            if (raw[(int)HomeoConditions.Speculative]) _cntSpecRaw++;
            if (s.MomentumStalled && !double.IsNaN(_prevNovelChain)
                && Math.Abs(s.NovelChain - _prevNovelChain) <= _chainFlatBand && !spanFlat) _cntStalledSpanBlocks++;
        }

        HomeoConditions? win = null;
        for (int c = 0; c < NConds; c++)
        {
            _streak[c] = raw[c] ? _streak[c] + 1 : 0;
            if (win is null && _streak[c] >= LockRounds && c != (int)HomeoConditions.Quiet)
                win = (HomeoConditions)c;              // first (highest-priority) sustained non-Quiet wins
        }
        // Quiet is the lowest priority: only if nothing else sustained
        if (win is null && _streak[(int)HomeoConditions.Quiet] >= LockRounds)
            win = HomeoConditions.Quiet;
        // DECISIVE = the winning close fired on the forecast alone (arrived legs false) — the speculation the
        // seam meters. A lead merely echoing arrived reality is not a speculation; nothing to vest.
        _lastDecisive = (sbyte)(win switch
        {
            HomeoConditions.Surprised when excDir <= 0 => LeadSurprised,
            HomeoConditions.Collapsing when !collArrived => LeadCollapsing,
            _ => -1,
        });
        if (_lastDecisive == LeadSurprised && applySurprised)
        {
            _leadPolicyDecision = surprisedDecision;
            _leadPolicyDecisionInvariantClean = true;
            _leadPolicyOutcomePending = true;
        }
        else if (_lastDecisive == LeadCollapsing && applyCollapsing)
        {
            _leadPolicyDecision = collapsingDecision;
            _leadPolicyDecisionInvariantClean = true;
            _leadPolicyOutcomePending = true;
        }
        return win;
    }

    private bool ChooseForecastLead(
        Cortex cortex,
        int lead,
        in Interocept senses,
        int accuracyDeparture,
        bool predictsCriticalityRise,
        bool predictsCoverageFall,
        bool predictsDepthLoss,
        out CortexPolicyDecision decision)
    {
        _leadOutcomes.RecordFire(lead);
        Span<MetricSample> features = stackalloc MetricSample[12]
        {
            new(new MetricID((ushort)ForecastLeadMetricIDs.LeadClass), NumericValue.FromI64(lead)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.Criticality), NumericValue.FromF64(senses.Cvz)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.CriticalitySamples), NumericValue.FromI64(senses.Kz)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.ExperienceMint), NumericValue.FromF64(senses.ExcMint)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.CollapseFraction), NumericValue.FromF64(senses.CollFrac)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.ThirdDerivativeFraction), NumericValue.FromF64(senses.DfThird)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.MaximumSpan), NumericValue.FromF64(senses.MaxSpan)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.NovelChain), NumericValue.FromI64(senses.NovelChain)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.AccuracyDeparture), NumericValue.FromI64(accuracyDeparture)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.PredictsCriticalityRise), NumericValue.FromI64(predictsCriticalityRise ? 1 : 0)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.PredictsCoverageFall), NumericValue.FromI64(predictsCoverageFall ? 1 : 0)),
            new(new MetricID((ushort)ForecastLeadMetricIDs.PredictsDepthLoss), NumericValue.FromI64(predictsDepthLoss ? 1 : 0)),
        };
        decision = cortex.ChoosePolicyAction(ForecastLeadPolicyID, (int)HomeostatForecastLeadActions.Apply, features);
        return decision.Action == (int)HomeostatForecastLeadActions.Apply;
    }

    private void ObserveHomeBandConstants(string band, int decision)
    {
        ObserveAdaptiveConstant(HomeostatAdaptiveConstants.HomeBandK, decision, $"{band}:k={HomeBand.K:R}");
        ObserveAdaptiveConstant(HomeostatAdaptiveConstants.HomeBandDrift, decision, $"{band}:drift={HomeBand.Drift:R}");
        ObserveAdaptiveConstant(HomeostatAdaptiveConstants.HomeBandFloorFrac, decision, $"{band}:floor={HomeBand.FloorFrac:R}");
    }

    private HomeoActuation ComputeReflexActuation(
        HomeoConditions? condition,
        in Interocept senses,
        bool aestivationWasted,
        out int nextBreachAmplitude,
        out HomeostatPolicyProgram program)
    {
        HomeostatPolicyContext context = HomeostatPolicyContext.From(
            condition, aestivationWasted, senses.GrowthRate > _mintParity);
        program = CompileReflexProgram(context);
        HomeoActuation action = program.Execute(_act, _rest, _gain, _breachAmp);
        nextBreachAmplitude = condition == HomeoConditions.Stalled ? _breachAmp : BreachQuotaBase;
        if (condition == HomeoConditions.Stalled)
            nextBreachAmplitude = Math.Min(_breachAmp * 2, BreachQuotaBase * 16);
        int breachDecision = condition == HomeoConditions.Stalled ? 1 : condition == HomeoConditions.Hot ? -1 : 0;
        ObserveAdaptiveConstant(HomeostatAdaptiveConstants.BreachQuotaBase, breachDecision, $"condition={condition}:base={BreachQuotaBase}");
        if (condition == HomeoConditions.Stalled)
            ObserveAdaptiveConstant(HomeostatAdaptiveConstants.BreachAmplitude, 1, $"amplitude={_breachAmp}:next={nextBreachAmplitude}");
        return action;
    }

    private static HomeostatPolicyProgram CompileReflexProgram(in HomeostatPolicyContext context)
        => context.Condition switch
        {
            HomeostatPolicyConditions.Relax => new HomeostatPolicyProgram(
                context.PreviousConsolidationPhaseWasted ? HomeostatScalarMoves.Relax : HomeostatScalarMoves.Hold,
                HomeostatScalarMoves.Relax, HomeostatScalarMoves.Relax, HomeostatScalarMoves.Relax,
                HomeostatBreachMoves.Clear, HomeostatForceGeneralizeMoves.Disable),
            HomeostatPolicyConditions.Quiet => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Up, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Up, HomeostatScalarMoves.Hold,
                HomeostatBreachMoves.Hold, HomeostatForceGeneralizeMoves.Hold),
            HomeostatPolicyConditions.Collapsing => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Down, HomeostatScalarMoves.Down, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold,
                HomeostatBreachMoves.Hold, HomeostatForceGeneralizeMoves.Hold),
            HomeostatPolicyConditions.Sealed or HomeostatPolicyConditions.Speculative => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Hold, HomeostatScalarMoves.Down, HomeostatScalarMoves.Up, HomeostatScalarMoves.Hold,
                HomeostatBreachMoves.Hold, HomeostatForceGeneralizeMoves.Hold),
            HomeostatPolicyConditions.Hot => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Down, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Down,
                HomeostatBreachMoves.Clear, HomeostatForceGeneralizeMoves.Hold),
            HomeostatPolicyConditions.Surprised => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Down, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold,
                HomeostatBreachMoves.Hold, HomeostatForceGeneralizeMoves.Hold),
            HomeostatPolicyConditions.Heavy => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Hold,
                context.GrowthAboveMintParity ? HomeostatScalarMoves.Down : HomeostatScalarMoves.Hold,
                HomeostatScalarMoves.Hold, HomeostatScalarMoves.Down,
                HomeostatBreachMoves.Hold, HomeostatForceGeneralizeMoves.Enable),
            HomeostatPolicyConditions.Stalled => new HomeostatPolicyProgram(
                HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold, HomeostatScalarMoves.Hold,
                HomeostatBreachMoves.Grant, HomeostatForceGeneralizeMoves.Hold),
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };

    private static HomeostatPolicyProgram[] CreateSharedPolicyActions()
    {
        List<HomeostatPolicyProgram> actions = new();
        for (int c = 0; c <= (int)HomeostatPolicyConditions.Speculative; c++)
        {
            for (int wasted = 0; wasted < 2; wasted++)
            {
                for (int growth = 0; growth < 2; growth++)
                {
                    HomeostatPolicyContext context = new((HomeostatPolicyConditions)c, wasted != 0, growth != 0);
                    HomeostatPolicyProgram program = CompileReflexProgram(in context);
                    if (!actions.Contains(program)) actions.Add(program);
                }
            }
        }
        actions.Sort(static (left, right) => string.CompareOrdinal(left.RenderToken(), right.RenderToken()));
        return actions.ToArray();
    }

    private static int FindSharedPolicyAction(in HomeostatPolicyProgram program)
    {
        for (int i = 0; i < SharedPolicyActions.Length; i++)
            if (SharedPolicyActions[i] == program) return i;
        throw new InvalidOperationException($"Homeostat launchpad emitted an unregistered policy action '{program.RenderToken()}'");
    }

    internal static int FindDestinationPolicyAction(in HomeostatPolicyProgram program)
        => FindSharedPolicyAction(in program);

    internal static HomeostatPolicyProgram CompilePolicyProgram(in HomeostatPolicyContext context)
        => CompileReflexProgram(in context);

    private static void ReadSharedPolicyFeatures(in HomeostatPolicyInput input, Span<MetricSample> destination)
    {
        Span<double> features = stackalloc double[HomeostatPolicyFeatures.Count];
        HomeostatPolicyFeatures.Read(in input, features);
        if (destination.Length != features.Length) throw new ArgumentException("Homeostat policy feature destination differs from its schema", nameof(destination));
        for (int i = 0; i < destination.Length; i++)
            destination[i] = new MetricSample(
                GetPolicyFeatureMetricID(i),
                NumericValue.FromF64(features[i]));
    }

    internal void TryGrantSharedPolicyAuthority(Cortex cortex)
    {
        if (!cortex.TryReadPolicyReadout(PolicyID, out CortexPolicyReadoutReceipt readout)
            || !readout.IsExact
            || !cortex.IsPolicyReadoutReady(PolicyID, readout.Fingerprint))
            return;
        if (readout.CanonicalState.Version == 0)
            return;
        SharedPolicyOccurrenceCheckKey verificationKey = new(
            readout.Fingerprint, readout.CandidateFingerprint, readout.CandidateOccurrenceDigest,
            readout.Revision, readout.CanonicalState);
        PolicyCanonicalStateID canonicalState = readout.CanonicalState;
        bool scopeCurrent = cortex.IsVerifiedPolicyScope(
            PolicyID, in canonicalState, readout.Fingerprint,
            readout.CandidateFingerprint, readout.CandidateOccurrenceDigest, readout.Revision);
        if (verificationKey.IsValid && verificationKey == _lastSharedPolicyOccurrenceCheckKey && scopeCurrent)
            return;
        HomeostatPolicyReadoutEnclosureReceipt enclosure = VerifySharedPolicyReadout(cortex, in _rest, _gain, _breachAmp, readout.CanonicalState);
        int adversarialComparisons = enclosure.Comparisons;
        int adversarialAgreements = enclosure.Agreements;
        int invariantFailures = enclosure.Disagreements;
        bool passed = enclosure.IsExact;
        PolicyCanonicalStateID observedState = readout.CanonicalState;
        cortex.RecordPolicyOccurrenceCheck(
            PolicyID, readout.Fingerprint, adversarialComparisons, adversarialAgreements, invariantFailures, passed,
            readout.CanonicalCoverage);
        _lastSharedPolicyOccurrenceCheckKey = verificationKey;
        Trace.Cortex.Boundary("policy.verify",
            $"policy={PolicyID} fp={readout.Fingerprint:X16} observed={readout.Agreements}/{readout.Comparisons} enclosure={enclosure.Agreements}/{enclosure.Comparisons} missing={enclosure.MissingStateCount} scanned={enclosure.ScannedBytes} expanded={enclosure.ExpandedEdges} failures={invariantFailures} result={(passed ? "PASS" : "reject")}");
        if (passed)
            TryGrantSharedPolicyScope(
                cortex, in observedState, readout.Fingerprint,
                readout.CandidateFingerprint, readout.CandidateOccurrenceDigest, readout.Revision);
    }

    internal bool TryGrantSharedPolicyAuthority(
        Cortex cortex,
        in Cortex.CortexPolicyActionPreparation prepared)
    {
        if (!prepared.HasCanonicalState || !prepared.HasGrammarReadout
            || !prepared.CanonicalState.IsValidFor(PolicyID)
            || !prepared.Policy.Equals(PolicyID))
            return false;
        if (!cortex.TryReadPolicyReadout(PolicyID, out CortexPolicyReadoutReceipt current)
            || current.Revision != prepared.InstallRevision
            || current.Fingerprint != prepared.ActiveProgramFingerprint
            || current.CanonicalState != prepared.CanonicalState
            || current.CandidateFingerprint != prepared.CandidateFingerprint
            || current.CandidateOccurrenceDigest != prepared.CandidateOccurrenceDigest
            || !current.IsExact)
            return false;
        if (!cortex.IsPolicyReadoutReady(PolicyID, prepared.ActiveProgramFingerprint))
            return false;
        PolicyCanonicalStateID canonicalState = prepared.CanonicalState;
        if (cortex.IsVerifiedPolicyScope(
                PolicyID, in canonicalState, prepared.ActiveProgramFingerprint,
                prepared.CandidateFingerprint, prepared.CandidateOccurrenceDigest,
                prepared.CandidateRevision))
            return false;
        HomeostatPolicyReadoutEnclosureReceipt enclosure = VerifySharedPolicyReadout(
            cortex, in _rest, _gain, _breachAmp, canonicalState);
        cortex.RecordPolicyOccurrenceCheck(
            PolicyID, prepared.ActiveProgramFingerprint, enclosure.Comparisons,
            enclosure.Agreements, enclosure.Disagreements, enclosure.IsExact,
            current.CanonicalCoverage);
        if (!enclosure.IsExact)
            return false;
        return TryGrantSharedPolicyScope(
            cortex, in canonicalState, prepared.ActiveProgramFingerprint,
            prepared.CandidateFingerprint, prepared.CandidateOccurrenceDigest,
            prepared.CandidateRevision);
    }

    internal static bool TryGrantSharedPolicyScope(
        Cortex cortex,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong supportDigest,
        global::Cogito.Grammar.GrammarRevisionID revision)
    {
        if (!cortex.TryGrantVerifiedPolicyScope(
                PolicyID, in canonicalState, readoutFingerprint,
                candidateFingerprint, supportDigest, revision))
            return false;
        cortex.AppendPolicyOccurrenceCheckScope(
            PolicyID, readoutFingerprint, candidateFingerprint,
            supportDigest, revision, in canonicalState);
        cortex.TryGrantVerifiedPolicySuccession(
            PolicyID, readoutFingerprint, candidateFingerprint, revision);
        return true;
    }

    internal HomeostatPolicyReadoutEnclosureReceipt VerifySharedPolicyReadout(Cortex cortex)
        => VerifySharedPolicyReadout(cortex, in _rest, _gain, _breachAmp);

    internal HomeostatPolicyReadoutEnclosureReceipt VerifySharedPolicyReadoutState(
        Cortex cortex, in PolicyCanonicalStateID state)
        => VerifySharedPolicyReadout(cortex, in _rest, _gain, _breachAmp, state);

    internal static HomeostatPolicyReadoutEnclosureReceipt VerifySharedPolicyReadout(
        Cortex cortex,
        in HomeoActuation rest,
        double gain,
        int breachAmplitude,
        PolicyCanonicalStateID? observedState = null)
    {
        ArgumentNullException.ThrowIfNull(cortex);
        if (!double.IsFinite(gain) || gain < 0 || gain > 1) throw new ArgumentOutOfRangeException(nameof(gain));
        if (breachAmplitude < 0) throw new ArgumentOutOfRangeException(nameof(breachAmplitude));
        if (cortex.InstallRevision is not { } publication)
            throw new InvalidOperationException("Homeostat readout enclosure requires a bound grammar publication");

        global::Cogito.Grammar.ReadoutCorpusIndex index = publication.GetReadoutCorpusIndex();
        index.RequireCompatible(publication.EffectiveSnapshot);
        PolicyCanonicalStateID[] required = observedState is PolicyCanonicalStateID scopedState
            ? [scopedState]
            : PolicyCanonicalStates.HomeostatDomain(PolicyID);
        int actionCount = SharedPolicyActions.Length;
        int deliberationDepth = cortex.Config.Learning.Policies.ReadoutDeliberationQuota;
        byte[][] continuations = new byte[actionCount][];
        for (int action = 0; action < actionCount; action++)
            continuations[action] = TapePacketCreator.EncodePolicyGrammarContinuation(action);

        List<PolicyCanonicalStateID> foundStates = new(required.Length);
        List<PolicyCanonicalStateID> missingStates = new(required.Length);
        int comparisons = 0;
        int agreements = 0;
        int disagreements = 0;
        long scannedBytes = 0;
        long expandedEdges = 0;
        for (int stateIndex = 0; stateIndex < required.Length; stateIndex++)
        {
            PolicyCanonicalStateID state = required[stateIndex];
            GrammarPolicyContextKey context = new(in state, actionCount, deliberationDepth);
            global::Cogito.Grammar.GrammarContinuationQuota quota = new(checked(deliberationDepth + 1));
            bool found = index.TryChooseContinuation(
                context.Context, continuations, quota, deliberationDepth,
                out global::Cogito.Grammar.GrammarContinuationDecision choice,
                out global::Cogito.Grammar.GrammarContinuationReadoutReceipt readout);
            global::Cogito.Grammar.GrammarContinuationQuotaCompletion completion = quota.Complete();
            if (readout.Revision != index.Revision || readout.EffectiveDigest != index.EffectiveDigest
                || readout.CorpusBytes != choice.ScannedBytes
                || readout.MatchingRecords != choice.MatchingRecords
                || readout.ExpandedEdges != choice.ExpandedEdges
                || completion.ScannedBytes != choice.ScannedBytes
                || completion.ExpandedEdges != choice.ExpandedEdges)
                throw new InvalidDataException("Homeostat readout enclosure index receipt diverges from its decision");
            scannedBytes = checked(scannedBytes + completion.ScannedBytes);
            expandedEdges = checked(expandedEdges + completion.ExpandedEdges);
            if (!found)
            {
                missingStates.Add(state);
                continue;
            }

            foundStates.Add(state);
            GrammarPolicyDecision candidate = new(
                choice.Continuation,
                choice.LearnedWeight,
                choice.MatchingRecords,
                publication.Revision,
                completion,
                GrammarPolicyReadout.ComputeStateFingerprint(PolicyID, in state))
            {
                OccurrenceDigest = PolicySupportDigest.Compute(choice.CandidateScores, choice.CandidateCounts, choice.MatchingRecords),
            };
            HomeostatPolicyContext policyContext = DecodeCanonicalContext(in state);
            HomeostatPolicyProgram oracleProgram = CompileReflexProgram(in policyContext);
            HomeostatPolicyProgram candidateProgram = (uint)candidate.Action < (uint)SharedPolicyActions.Length
                ? SharedPolicyActions[candidate.Action]
                : default;
            foreach (HomeoActuation current in CreateAuxiliaryActuations(in rest))
            {
                comparisons++;
                if ((uint)candidate.Action >= (uint)SharedPolicyActions.Length
                    || !ValidatePolicyProgram(policyContext, in candidateProgram))
                {
                    disagreements++;
                    continue;
                }
                HomeoActuation proposed = candidateProgram.Execute(in current, in rest, gain, breachAmplitude);
                if (!ValidateActionSchema(in proposed, in rest)
                    || candidateProgram != oracleProgram)
                    disagreements++;
                else
                    agreements++;
            }
        }

        HomeostatPolicyReadoutEnclosureReceipt receipt = new(
            index.Revision, index.EffectiveDigest, required.Length,
            foundStates.Count, missingStates.Count, foundStates.ToArray(), missingStates.ToArray(),
            required.Length, comparisons, agreements, disagreements, scannedBytes, expandedEdges);
        receipt.Validate();
        return receipt;
    }

    private static HomeostatPolicyContext DecodeCanonicalContext(in PolicyCanonicalStateID state)
    {
        if (!state.Policy.Equals(PolicyID) || state.Kind != PolicyCanonicalStateKinds.Homeostat
            || state.Version != PolicyCanonicalStates.HomeostatVersion
            || (state.Value & ~0x3FFUL) != 0 || (state.Value & 0xFF) > 8)
            throw new InvalidDataException("Homeostat readout enclosure received an invalid canonical state");
        return new HomeostatPolicyContext(
            (HomeostatPolicyConditions)(state.Value & 0xFF),
            (state.Value & (1UL << 8)) != 0,
            (state.Value & (1UL << 9)) != 0);
    }

    private static HomeoActuation[] CreateAuxiliaryActuations(in HomeoActuation rest)
    {
        double[] sleep = [1.0 / 32, rest.SleepFrac, 1.0 / 4];
        int[] mix = [rest.MixEvery == 0 ? 0 : Math.Max(0, rest.MixEvery / 4), rest.MixEvery];
        int[] intake = [rest.IntakeBatch, checked(rest.IntakeBatch * 4)];
        long[] budget = [rest.BudgetBits == 0 ? 0 : rest.BudgetBits / 2, rest.BudgetBits == 0 ? 0 : checked(rest.BudgetBits * 2)];
        int[] breach = [0, BreachQuotaBase];
        HomeoActuation[] variants = new HomeoActuation[3 * 2 * 2 * 2 * 2];
        int index = 0;
        for (int sl = 0; sl < sleep.Length; sl++)
            for (int mx = 0; mx < mix.Length; mx++)
                for (int intakeAt = 0; intakeAt < intake.Length; intakeAt++)
                    for (int bits = 0; bits < budget.Length; bits++)
                        for (int br = 0; br < breach.Length; br++)
                            variants[index++] = new(sleep[sl], mix[mx], intake[intakeAt], budget[bits], breach[br], false);
        return variants;
    }

    private int ComputeCandidateBreachAmplitude(HomeoConditions? condition, in HomeoActuation action)
        => ComputeCandidateBreachAmplitude(condition, _breachAmp, BreachQuotaBase, in action);

    internal static int ComputeCandidateBreachAmplitude(
        HomeoConditions? condition, int currentBreachAmplitude, int breachQuotaBase, in HomeoActuation action)
    {
        if (currentBreachAmplitude < 0 || breachQuotaBase <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentBreachAmplitude));
        if (condition != HomeoConditions.Stalled) return breachQuotaBase;
        return action.BreachQuota > 0
            ? Math.Min(checked(currentBreachAmplitude * 2), checked(breachQuotaBase * 16))
            : currentBreachAmplitude;
    }

    // ── the condition bands, EMA-RELATIVE (the retired `_excBand=0.5`/`_hotBand=1.0`/`_bitsBand=110` bodge) ──
    // Each gated sense carries a close-cadence home — the machine's own operator applied to its own
    // interoception: PREDICT the baseline (the sense's long-run home) → METER the deviation → FIRE on departure
    // (beyond k natural widths) → RE-CENTER (the home walks to the new level). A condition detects DEPARTURE
    // from the machine's own normal, never presence above an absolute cut — an absolute cut saturates on any
    // world whose baseline sits past the literal (the ladder rides ExcMint 0.75–1.0 against the old 0.5:
    // Surprised locked all 93 closes and starved Stalled/Collapsing — trunk_0131, 0 breach aestivations;
    // ABLATION-SENSES re-verified the failure mode. The cost bands warm up for one EMA horizon (see HomeBand).
    readonly HomeBand _excBand = new();
    readonly HomeBand _iopbBand = new(HomeBand.EmaHorizon), _gopbBand = new(HomeBand.EmaHorizon), _bitsBand = new(HomeBand.EmaHorizon);
    double _chainFlatBand = 3;             // Stalled's novelChain Δ-band — already relative by construction (flatness ACROSS closes)
    double _prevNovelChain = double.NaN;   // last close's EMA'd novelChain (NaN = no close yet) — Stalled's flatness reference
    // Heavy's GROWTH leg compares against the machine's own append ceiling, ctor-derived (the drive passes its
    // mint cadence + the intake actuator's clamp max). The old literal 4 == the drive's MintSpansPerStep, so the
    // NORMAL dream cadence rode the EMA to ≥4 and Heavy permanently shadowed Stalled — the measured lockout that
    // kept the breach organ unreachable (trunk_0131..0138). Anomalous growth = beyond what the machine's own
    // span-rate laws can produce, never the standing cadence itself.
    readonly double _mintParity;
    // The anneal's amplitude (BreachQuota granted per sustained-Stalled close): starts at the standalone
    // kill-line's proven quota0 (128), doubles per consecutive Stalled grant, re-centers when
    // the stall breaks, ceilinged at 16× (the breach is O(quota·tape) aestivation work — bounded, never unbounded).
    const int BreachQuotaBase = 128;
    int _breachAmp = BreachQuotaBase;

    // ── THE POLICY PLANE (SELFMODEL wave — Wired/Predict tiers; dormant state on Reflex, serialized uniformly
    //    like RYTM so the checkpoint shape never depends on the arm) ──
    // the six wired-sense homes (the dark-sense cure — same HomeBand instrument, cost-style warmup: a working
    // level must EXIST before departing it means anything; the boot climb from zero must not hand early
    // boundaries to the new legs — the measured budget-floored-by-close-6 death class)
    readonly HomeBand _hitBand = new(HomeBand.EmaHorizon), _depthBand = new(HomeBand.EmaHorizon), _jsBand = new(HomeBand.EmaHorizon);
    readonly HomeBand _distinctBand = new(HomeBand.EmaHorizon), _unvBand = new(HomeBand.EmaHorizon), _vestBand = new(HomeBand.EmaHorizon);
    double _prevMaxSpan = double.NaN;      // Stalled's span-flatness anchor (the novelChain shape, second instance)
    const double SpanFlatFrac = 0.05;      // Δ-flat = within 5% of level — RELATIVE (maxSpan spans decades; a byte-band would be the retired absolute-cut bodge)
    // the standing forecast (fed per step; navigated at the close). Checkpointed for Save∘Load∘Save exactness —
    // a close and a save can land on the same step, and the image must re-encode what the close consumed.
    string _predToken = "·"; char _predArm = 'x';
    // A speculation opened at close k is resolved at close k+1; the aestivation between them is its causal meter.
    const int LeadSurprised = 0, LeadCollapsing = 1;
    const double LeadYieldDrift = 1.0 / 8;
    readonly OutcomeMeter<int> _leadOutcomes;
    bool _leadPolicyOutcomePending;
    bool _leadPolicyDecisionInvariantClean = true;
    CortexPolicyDecision _leadPolicyDecision;
    sbyte _lastDecisive = -1;              // Classify's verdict: the win fired on forecast alone (−1 = arrived)
    // the readouts plane: per-leg fire counters (the "no dead sensors" readout), veto census, boundary census
    int _cntDistinctLeg, _cntJsLeg, _cntSpecRaw, _cntStalledSpanBlocks, _quietHitBlocks, _quietDepthBlocks;
    int _vetoQuiet, _vetoStalled;
    readonly int[] _census = new int[NConds + 1];   // per-condition boundary wins + relax at [NConds] (all tiers — the A/B table's spine)
    readonly List<HomeostatAdaptiveConstantReceipt> _adaptiveConstantReceipts = new();
    readonly List<PendingConstantDecision> _pendingConstantDecisions = new();
    int _checkpointAdaptiveReceiptCursor;

    internal HomeostatCheckpointDelta CaptureCheckpointDelta()
    {
        if (_checkpointAdaptiveReceiptCursor < 0 || _checkpointAdaptiveReceiptCursor > _adaptiveConstantReceipts.Count)
            throw new InvalidDataException("homeostat adaptive-receipt cursor is invalid");
        int count = _adaptiveConstantReceipts.Count - _checkpointAdaptiveReceiptCursor;
        HomeostatCheckpointDelta delta = new(_checkpointAdaptiveReceiptCursor, count == 0
            ? Array.Empty<HomeostatAdaptiveConstantReceipt>()
            : _adaptiveConstantReceipts.GetRange(_checkpointAdaptiveReceiptCursor, count).ToArray(),
            _sharedPolicyOutcomePending, _sharedPolicyDecisionInvariantClean, _sharedPolicyDecision,
            _leadPolicyOutcomePending, _leadPolicyDecisionInvariantClean, _leadPolicyDecision);
        delta.Validate();
        return delta;
    }

    internal void ApplyCheckpointDelta(in HomeostatCheckpointDelta delta)
    {
        delta.Validate();
        if (delta.Receipts is null || delta.Cursor < 0 || delta.Cursor != _adaptiveConstantReceipts.Count)
            throw new InvalidDataException("homeostat adaptive-receipt cursor gap");
        _adaptiveConstantReceipts.AddRange(delta.Receipts);
        _checkpointAdaptiveReceiptCursor = _adaptiveConstantReceipts.Count;
        _sharedPolicyOutcomePending = delta.SharedPolicyOutcomePending;
        _sharedPolicyDecisionInvariantClean = delta.SharedPolicyDecisionInvariantClean;
        _sharedPolicyDecision = delta.SharedPolicyDecision;
        _leadPolicyOutcomePending = delta.LeadPolicyOutcomePending;
        _leadPolicyDecisionInvariantClean = delta.LeadPolicyDecisionInvariantClean;
        _leadPolicyDecision = delta.LeadPolicyDecision;
    }

    internal void CommitCheckpointDelta() => _checkpointAdaptiveReceiptCursor = _adaptiveConstantReceipts.Count;

    internal static void WriteCheckpointDelta(CkptWriter writer, in HomeostatCheckpointDelta delta)
    {
        delta.Validate();
        writer.U8(2); writer.I32(delta.Cursor); writer.I32(delta.Receipts.Length);
        foreach (HomeostatAdaptiveConstantReceipt receipt in delta.Receipts)
        {
            writer.U8((byte)receipt.Constant); writer.U8(unchecked((byte)receipt.Decision)); writer.Str(receipt.Context); writer.Bool(receipt.Paid); writer.I32(receipt.Close);
        }
        writer.Bool(delta.SharedPolicyOutcomePending);
        writer.Bool(delta.SharedPolicyDecisionInvariantClean);
        if (delta.SharedPolicyOutcomePending)
        {
            CortexPolicyDecision sharedDecision = delta.SharedPolicyDecision;
            CortexPolicyDecisionCheckpoint.Write(writer, in sharedDecision);
        }
        writer.Bool(delta.LeadPolicyOutcomePending);
        writer.Bool(delta.LeadPolicyDecisionInvariantClean);
        if (delta.LeadPolicyOutcomePending)
        {
            CortexPolicyDecision leadDecision = delta.LeadPolicyDecision;
            CortexPolicyDecisionCheckpoint.Write(writer, in leadDecision);
        }
    }

    internal static HomeostatCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte version = reader.U8();
        if (version != 2) throw new InvalidDataException("unknown homeostat checkpoint delta version");
        int cursor = reader.I32(); int count = reader.I32();
        if (cursor < 0 || count < 0 || count > 1_000_000) throw new InvalidDataException("homeostat receipt delta exceeds bound");
        HomeostatAdaptiveConstantReceipt[] receipts = new HomeostatAdaptiveConstantReceipt[count];
        for (int i = 0; i < count; i++) receipts[i] = new((HomeostatAdaptiveConstants)reader.U8(), unchecked((sbyte)reader.U8()), reader.Str(), reader.Bool(), reader.I32());
        bool sharedPending = reader.Bool();
        bool sharedInvariantClean = reader.Bool();
        CortexPolicyDecision sharedDecision = sharedPending
            ? CortexPolicyDecisionCheckpoint.Read(reader, PolicyID, PolicySchema.ActionCount)
            : default;
        bool leadPending = reader.Bool();
        bool leadInvariantClean = reader.Bool();
        CortexPolicyDecision leadDecision = leadPending
            ? CortexPolicyDecisionCheckpoint.Read(reader, ForecastLeadPolicyID, ForecastLeadPolicySchema.ActionCount)
            : default;
        HomeostatCheckpointDelta delta = new(cursor, receipts, sharedPending, sharedInvariantClean, sharedDecision,
            leadPending, leadInvariantClean, leadDecision);
        delta.Validate();
        return delta;
    }

    /// Record a fixed-law decision for external obligation measurement. Homeostat never reads these receipts when
    /// choosing or actuating, and they are deliberately absent from its checkpoint and policy report.
    public void ObserveAdaptiveConstant(HomeostatAdaptiveConstants constant, int decision, string context)
    {
        if (!_observeAdaptiveConstants) return;
        if (decision is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(decision), "constant decisions are -1, 0, or +1");
        if (string.IsNullOrWhiteSpace(context)) throw new ArgumentException("constant decision context is required", nameof(context));
        _pendingConstantDecisions.Add(new PendingConstantDecision(constant, (sbyte)decision, context));
    }

    public void DrainAdaptiveConstantReceipts(List<HomeostatAdaptiveConstantReceipt> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.AddRange(_adaptiveConstantReceipts);
        _adaptiveConstantReceipts.Clear();
        _checkpointAdaptiveReceiptCursor = 0;
    }

    private void ResolveConstantOutcomes(bool aestivationWasted)
    {
        bool paid = !aestivationWasted;
        for (int i = 0; i < _pendingConstantDecisions.Count; i++)
        {
            PendingConstantDecision decision = _pendingConstantDecisions[i];
            _adaptiveConstantReceipts.Add(new HomeostatAdaptiveConstantReceipt(
                decision.Constant, decision.Decision, decision.Context, paid, SleepsClosed - 1));
        }
        _pendingConstantDecisions.Clear();
    }

    private static bool ValidatePolicyProgram(in HomeostatPolicyContext context, in HomeostatPolicyProgram program)
    {
        HomeostatPolicyProgram.Validate(program);
        if (program.Breach == HomeostatBreachMoves.Grant && context.Condition != HomeostatPolicyConditions.Stalled) return false;
        if (context.Condition == HomeostatPolicyConditions.Hot && program.Breach != HomeostatBreachMoves.Clear) return false;
        return true;
    }

    private bool ValidateActionSchema(in HomeoActuation action)
        => ValidateActionSchema(in action, in _rest);

    private static bool ValidateActionSchema(in HomeoActuation action, in HomeoActuation rest)
    {
        if (!double.IsFinite(action.SleepFrac) || action.SleepFrac < 1.0 / 32 || action.SleepFrac > 1.0 / 4) return false;
        if (rest.MixEvery == 0)
        {
            if (action.MixEvery != 0) return false;
        }
        else if (action.MixEvery < Math.Max(0, rest.MixEvery / 4) || action.MixEvery > rest.MixEvery) return false;
        if (action.IntakeBatch < rest.IntakeBatch || action.IntakeBatch > rest.IntakeBatch * 4) return false;
        if (rest.BudgetBits == 0)
        {
            if (action.BudgetBits != 0) return false;
        }
        else if (action.BudgetBits < rest.BudgetBits / 2 || action.BudgetBits > rest.BudgetBits * 2) return false;
        return action.BreachQuota is >= 0 and <= BreachQuotaBase * 16;
    }

    private bool ApplyPolicyProgram(
        in HomeostatPolicyContext context,
        in HomeostatPolicyProgram program,
        in HomeoActuation proposed,
        int nextBreachAmplitude,
        out HomeoActuation actuation)
    {
        actuation = proposed;
        if (!ValidatePolicyProgram(context, program) || !ValidateActionSchema(actuation)) return false;
        _act = actuation;
        _breachAmp = nextBreachAmplitude;
        return true;
    }

    internal static void ValidateDestinationProgram(
        in CortexPolicyDecisionReadout readout,
        in HomeostatPolicyContext context,
        in HomeostatPolicyProgram program,
        in HomeoActuation actuation)
    {
        if ((uint)readout.ExecutedAction >= (uint)SharedPolicyActions.Length
            || SharedPolicyActions[readout.ExecutedAction] != program
            || !ValidatePolicyProgram(context, program)
            || !double.IsFinite(actuation.SleepFrac) || actuation.SleepFrac < 1.0 / 32 || actuation.SleepFrac > 1.0 / 4
            || actuation.MixEvery < 0 || actuation.IntakeBatch < 1 || actuation.BudgetBits < 0
            || actuation.BreachQuota is < 0 or > BreachQuotaBase * 16)
            throw new InvalidDataException("Homeostat destination handshake program or actuation is outside the policy schema");
    }

    static string FormatLeadName(int li) => li == LeadSurprised ? "surprise" : "collapse";
    static string FormatYieldToken(bool wasted) => wasted ? "y0" : "y+";

    static int ComparePolicyRepresentatives(string left, string right)
    {
        int byLength = left.Length.CompareTo(right.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
    }

    static string FormatActionToken(in HomeoActuation before, in HomeoActuation after)
    {
        StringBuilder sb = new();
        AppendMove(sb, "sl", before.SleepFrac, after.SleepFrac);
        AppendMove(sb, "mx", before.MixEvery, after.MixEvery);
        AppendMove(sb, "in", before.IntakeBatch, after.IntakeBatch);
        AppendMove(sb, "bb", before.BudgetBits, after.BudgetBits);
        AppendMove(sb, "br", before.BreachQuota, after.BreachQuota);
        AppendFlagMove(sb, "fg", before.ForceGeneralize, after.ForceGeneralize);
        return sb.Length == 0 ? "hold" : sb.ToString();
    }

    static void AppendMove(StringBuilder sb, string label, double before, double after)
    {
        if (after > before) AppendToken(sb, label, '+');
        else if (after < before) AppendToken(sb, label, '-');
    }

    static void AppendMove(StringBuilder sb, string label, long before, long after)
    {
        if (after > before) AppendToken(sb, label, '+');
        else if (after < before) AppendToken(sb, label, '-');
    }

    static void AppendFlagMove(StringBuilder sb, string label, bool before, bool after)
    {
        if (before != after) AppendToken(sb, label, after ? '+' : '-');
    }

    static void AppendToken(StringBuilder sb, string label, char direction)
    {
        if (sb.Length > 0) sb.Append(',');
        sb.Append(label).Append(direction);
    }

    /// The boundary telegraph (trace-only — never a journal artifact): the winning condition, the live
    /// allocation, and the EMA'd senses that drove it.
    public string Line()
        => $"cond={LastCondition?.ToString() ?? "relax"} · sleepfrac={_act.SleepFrac:F3} mix={_act.MixEvery} intake={_act.IntakeBatch} budget={_act.BudgetBits} breach={_act.BreachQuota} gen={_act.ForceGeneralize}"
         + $" · ema cvz={_ema.Cvz:F3}/k{_ema.Kz} exc={_ema.ExcMint:F2} opb i={_ema.InduceOpb:F3} g={_ema.GenOpb:F3} bits/real={_ema.BitsPerSpan:F0} unvested={_ema.UnvestedFrac:F2} vestrate={_ema.VestRate:F2}"
         + $" · stall={(_ema.MomentumStalled ? 1 : 0)}/nc{_ema.NovelChain} grow={_ema.GrowthRate:F1}"   // the comprehension plane — Stalled's two legs + Heavy's growth leg (the boundary's gating senses must all telegraph)
         + $" · ⌂ exc={_excBand.Mu:F2}±{_excBand.Width:F2} i={_iopbBand.Mu:F3}±{_iopbBand.Width:F3} g={_gopbBand.Mu:F3}±{_gopbBand.Width:F3} bits={_bitsBand.Mu:F0}±{_bitsBand.Width:F0}"   // the band homes — where each gated sense's normal sits (a condition fires on departure FROM here)
         + $" · wasted {WastedSleeps}/{SleepsClosed}"
         + (PolicyArmed
             ? $" · pol={(_policy == HomeoPolicies.Predict ? "P" : _policy == HomeoPolicies.Wired ? "W" : "R")} pred={_predToken}/{_predArm}"
               + $" lead s:{FormatLeadCell(LeadSurprised)} c:{FormatLeadCell(LeadCollapsing)} pend={(_leadOutcomes.PendingIndex < 0 ? "—" : FormatLeadName(_leadOutcomes.ArmAt(_leadOutcomes.PendingIndex)))}"
               + $" veto q{_vetoQuiet}/t{_vetoStalled}"
             : "");

    string FormatLeadCell(int li)
    {
        OutcomeArmState lv = _leadOutcomes.Read(li);
        string ema = double.IsNaN(lv.YieldEma) ? "—" : lv.YieldEma.ToString("F2");
        return $"{ema}({lv.Decisive}/{lv.Outcomes})";
    }

    /// The land-time report (homeostat.txt, Wired/Predict arms only) — the boundary census, the wired-leg
    /// readouts (no dead sensors: every consumer's contribution counted), the lead-vest registers, and the
    /// policy channel's self-prediction.
    internal string Report(in CortexPolicyRuntimeReceipt policyReceipt)
    {
        StringBuilder sb = new();
        sb.AppendLine($"── HOMEOSTAT · the policy plane ({_policy}/{policyReceipt.Authority}) — wired senses + forecast leads on the vest seam ──");
        sb.Append($"  closes {SleepsClosed} (wasted {WastedSleeps}) · reversals {SignReversals} · census:");
        for (int c = 0; c < NConds; c++) if (_census[c] > 0) sb.Append($" {(HomeoConditions)c} {_census[c]} ·");
        sb.AppendLine($" relax {_census[NConds]}");
        sb.AppendLine($"  wired legs: Collapsing∋distinct↓ {_cntDistinctLeg} · Sealed∋js↓ {_cntJsLeg} · Speculative raw {_cntSpecRaw} (won {_census[(int)HomeoConditions.Speculative]})"
                    + $" · Quiet blocked by hit⌂ {_quietHitBlocks} / depth⌂ {_quietDepthBlocks} · Stalled blocked by span-flat {_cntStalledSpanBlocks}");
        sb.AppendLine($"  homes: hit⌂ {_hitBand.Mu:F3}±{_hitBand.Width:F3} · depth⌂ {_depthBand.Mu:F3}±{_depthBand.Width:F3} · js⌂ {_jsBand.Mu:F3}±{_jsBand.Width:F3}"
                    + $" · distinct⌂ {_distinctBand.Mu:F0}±{_distinctBand.Width:F0} · unvested⌂ {_unvBand.Mu:F3}±{_unvBand.Width:F3} · vestrate⌂ {_vestBand.Mu:F3}±{_vestBand.Width:F3}");
        if (_policy == HomeoPolicies.Predict)
        {
            sb.AppendLine($"  forecast: standing '{_predToken}'/{_predArm} · leads (Cortex policy, metered by the following aestivation):");
            for (int li = 0; li <= LeadCollapsing; li++)
            {
                OutcomeArmState lv = _leadOutcomes.Read(li);
                sb.AppendLine($"    {FormatLeadName(li),-9} forecast-fires {lv.Fires} · decisive {lv.Decisive} · outcomes {lv.Outcomes} · yield⌂ {(double.IsNaN(lv.YieldEma) ? "—" : lv.YieldEma.ToString("F2"))}");
            }
            sb.AppendLine($"    vetoes: Quiet {_vetoQuiet} · Stalled {_vetoStalled} (trust-gated — the counterfactual aestivation is unobservable, so their meter is the accuracy home itself)");
            if (_leadOutcomes.PendingIndex >= 0) sb.AppendLine($"    pending: {FormatLeadName(_leadOutcomes.ArmAt(_leadOutcomes.PendingIndex))} (unresolved at land)");
        }
        sb.AppendLine($"  readout cache: {policyReceipt.CachedContexts} contexts · authority {policyReceipt.Authority}");
        sb.AppendLine($"  shadow agreement: {policyReceipt.ShadowAgreements}/{policyReceipt.ShadowComparisons}");
        sb.AppendLine($"  decision conservation: {policyReceipt.Outcomes}/{policyReceipt.Decisions} closed · unresolved {policyReceipt.Decisions - policyReceipt.Outcomes}");
        sb.AppendLine($"  valid/paid grammar: {policyReceipt.GrammarExecutions}/{policyReceipt.PaidGrammarOutcomes} · outcomes {policyReceipt.GrammarOutcomes}");
        sb.AppendLine($"  rollback drill / re-admission: pending {(policyReceipt.RollbackDrillPending ? "yes" : "no")} · completed {(policyReceipt.RollbackDrillCompleted ? "yes" : "no")} · re-promotions {policyReceipt.Readmissions}");
        return sb.ToString();
    }



    /// The Homeostat checkpoint carries only unresolved shared decisions.
    /// Learned authority, readout cache, verification, and rollback state are owned by Cortex.
    public void SaveAbsorptionState(CkptWriter writer)
    {
        writer.Bool(_sharedPolicyOutcomePending);
        writer.Bool(_sharedPolicyDecisionInvariantClean);
        if (_sharedPolicyOutcomePending)
            CortexPolicyDecisionCheckpoint.Write(writer, in _sharedPolicyDecision);
        writer.Bool(_leadPolicyOutcomePending);
        writer.Bool(_leadPolicyDecisionInvariantClean);
        if (_leadPolicyOutcomePending)
            CortexPolicyDecisionCheckpoint.Write(writer, in _leadPolicyDecision);
        // Only the active fingerprint fits the legacy absorption slot.  The remaining
        // typed key fields intentionally start empty after restore, forcing a fresh assay.
        writer.U64(_lastSharedPolicyOccurrenceCheckKey.ActiveReadoutFingerprint);
    }

    public void LoadAbsorptionState(CkptReader reader)
    {
        _sharedPolicyOutcomePending = reader.Bool();
        _sharedPolicyDecisionInvariantClean = reader.Bool();
        if (_sharedPolicyOutcomePending)
            _sharedPolicyDecision = CortexPolicyDecisionCheckpoint.Read(reader, PolicyID, PolicySchema.ActionCount);
        else
        {
            _sharedPolicyDecisionInvariantClean = true;
            _sharedPolicyDecision = default;
        }
        _leadPolicyOutcomePending = reader.Bool();
        _leadPolicyDecisionInvariantClean = reader.Bool();
        if (_leadPolicyOutcomePending)
            _leadPolicyDecision = CortexPolicyDecisionCheckpoint.Read(reader, ForecastLeadPolicyID, ForecastLeadPolicySchema.ActionCount);
        else
        {
            _leadPolicyDecisionInvariantClean = true;
            _leadPolicyDecision = default;
        }
        _lastSharedPolicyOccurrenceCheckKey = new SharedPolicyOccurrenceCheckKey(
            reader.U64(), 0, 0, global::Cogito.Grammar.GrammarRevisionID.Zero, default);
        _adaptiveConstantReceipts.Clear();
        _pendingConstantDecisions.Clear();
    }

    // ── CHECKPOINT — the slow plane whole: the live actuation, the EMA'd senses, the streaks, the mask,
    //    the condition-band homes, the wasted-sleep counters. The fast plane (WeightController) checkpoints
    //    itself; rest/gain/alpha are ctor inputs rebuilt from config. Field order is declaration order
    //    (the Vow: raw F64s). ──
    public void Save(CkptWriter w)
    {
        w.Bool(_seeded); w.Bool(_cvzMasked); w.I32(WastedSleeps); w.I32(SleepsClosed);
        w.U8((byte)(LastCondition is { } c ? (int)c : 255));
        w.F64(_act.SleepFrac); w.I32(_act.MixEvery); w.I32(_act.IntakeBatch); w.I64(_act.BudgetBits); w.I32(_act.BreachQuota); w.Bool(_act.ForceGeneralize);
        foreach (int s in _streak) w.I32(s);
        w.I32(SignReversals);
        for (int i = 0; i < _lastDir.Length; i++) { w.U8(unchecked((byte)_lastDir[i])); w.I32(_lastMoveAt[i]); }
        _excBand.Save(w); _iopbBand.Save(w); _gopbBand.Save(w); _bitsBand.Save(w);
        w.F64(_prevNovelChain); w.I32(_breachAmp);
        WriteSenses(w, _ema);
        // The policy selector itself is a ctor input rebuilt from config; its learned state rides the uniform
        // checkpoint section on every tier.
        _hitBand.Save(w); _depthBand.Save(w); _jsBand.Save(w); _distinctBand.Save(w); _unvBand.Save(w); _vestBand.Save(w);
        w.F64(_prevMaxSpan);
        w.Str(_predToken); w.U8((byte)_predArm);
        w.U8(unchecked((byte)_leadOutcomes.PendingIndex)); w.U8(unchecked((byte)_lastDecisive));
        _leadOutcomes.SaveArmState(w);
        w.I32(_cntDistinctLeg); w.I32(_cntJsLeg); w.I32(_cntSpecRaw); w.I32(_cntStalledSpanBlocks);
        w.I32(_quietHitBlocks); w.I32(_quietDepthBlocks); w.I32(_vetoQuiet); w.I32(_vetoStalled);
        foreach (int n in _census) w.I32(n);
    }

    public void Load(CkptReader r)
    {
        _seeded = r.Bool(); _cvzMasked = r.Bool(); WastedSleeps = r.I32(); SleepsClosed = r.I32();
        byte c = r.U8();
        LastCondition = c == 255 ? null : (HomeoConditions)c;
        _act = new HomeoActuation(r.F64(), r.I32(), r.I32(), r.I64(), r.I32(), r.Bool());
        for (int i = 0; i < _streak.Length; i++) _streak[i] = r.I32();
        SignReversals = r.I32();
        for (int i = 0; i < _lastDir.Length; i++) { _lastDir[i] = unchecked((sbyte)r.U8()); _lastMoveAt[i] = r.I32(); }
        _excBand.Load(r); _iopbBand.Load(r); _gopbBand.Load(r); _bitsBand.Load(r);
        _prevNovelChain = r.F64(); _breachAmp = r.I32();
        _ema = ReadSenses(r);
        _hitBand.Load(r); _depthBand.Load(r); _jsBand.Load(r); _distinctBand.Load(r); _unvBand.Load(r); _vestBand.Load(r);
        _prevMaxSpan = r.F64();
        _predToken = r.Str(); _predArm = (char)r.U8();
        _leadOutcomes.RestorePendingIndex(unchecked((sbyte)r.U8())); _lastDecisive = unchecked((sbyte)r.U8());
        _leadOutcomes.LoadArmState(r);
        _cntDistinctLeg = r.I32(); _cntJsLeg = r.I32(); _cntSpecRaw = r.I32(); _cntStalledSpanBlocks = r.I32();
        _quietHitBlocks = r.I32(); _quietDepthBlocks = r.I32(); _vetoQuiet = r.I32(); _vetoStalled = r.I32();
        for (int i = 0; i < _census.Length; i++) _census[i] = r.I32();
        _lastBoundaryPolicyDecision = default;
        _lastBoundaryPolicyContext = default;
        _lastBoundaryCanonicalState = default;
        _lastBoundaryPolicyProgram = default;
        _lastBoundaryPolicyActuation = default;
        _lastBoundaryPolicyStep = -1;
    }

    static void WriteSenses(CkptWriter w, in Interocept s)
    {
        w.F64(s.InduceOpb); w.F64(s.GenOpb); w.F64(s.GrowthRate); w.F64(s.BitsPerSpan);
        w.F64(s.ExcMint); w.F64(s.ExcHit); w.F64(s.ThtMint);
        w.F64(s.Cvz); w.I32(s.Kz);
        w.I32(s.Distinct); w.I32(s.NovelChain); w.F64(s.CollFrac); w.F64(s.DfThird); w.F64(s.Js); w.Bool(s.LoopConverged);
        w.F64(s.Depth); w.F64(s.MaxSpan); w.Bool(s.MomentumStalled);
        w.F64(s.UnvestedFrac); w.F64(s.VestRate);
        w.Bool(s.ReplayEra); w.Bool(s.CvzMasked);
    }

    static Interocept ReadSenses(CkptReader r) => new(
        r.F64(), r.F64(), r.F64(), r.F64(),
        r.F64(), r.F64(), r.F64(),
        r.F64(), r.I32(),
        r.I32(), r.I32(), r.F64(), r.F64(), r.F64(), r.Bool(),
        r.F64(), r.F64(), r.Bool(),
        r.F64(), r.F64(),
        r.Bool(), r.Bool());

    // NaN-aware scalar fold for the two senses that are legitimately undefined early (Cvz before ≥2 scales /
    // while masked; Js before the second generation): a NaN INPUT holds the state (the mask law: the bell
    // reads post-LOWER only), a NaN STATE adopts the first real reading — a plain lerp would poison the EMA
    // permanently (NaN + a·(x−NaN) = NaN forever) and QUIET could never fire for the whole run.
    static double EmaN(double p, double x, double a)
        => double.IsNaN(x) ? p : double.IsNaN(p) ? x : p + a * (x - p);

    static Interocept Ema(in Interocept p, in Interocept x, double a)
        => new(
            p.InduceOpb + a*(x.InduceOpb-p.InduceOpb), p.GenOpb + a*(x.GenOpb-p.GenOpb),
            p.GrowthRate + a*(x.GrowthRate-p.GrowthRate), p.BitsPerSpan + a*(x.BitsPerSpan-p.BitsPerSpan),
            p.ExcMint + a*(x.ExcMint-p.ExcMint), p.ExcHit + a*(x.ExcHit-p.ExcHit), p.ThtMint + a*(x.ThtMint-p.ThtMint),
            EmaN(p.Cvz, x.Cvz, a), x.Kz, x.Distinct, x.NovelChain,
            p.CollFrac + a*(x.CollFrac-p.CollFrac), p.DfThird + a*(x.DfThird-p.DfThird), EmaN(p.Js, x.Js, a),
            x.LoopConverged, p.Depth + a*(x.Depth-p.Depth), p.MaxSpan + a*(x.MaxSpan-p.MaxSpan),
            x.MomentumStalled, p.UnvestedFrac + a*(x.UnvestedFrac-p.UnvestedFrac),
            p.VestRate + a*(x.VestRate-p.VestRate), x.ReplayEra, x.CvzMasked);
}

/// A close-cadence LONG-EMA home + z-band over one interoceptive sense: mu is the slow EMA of the readings
/// (the sense's own normal), mad the same-horizon EMA of |deviation| (its own volatility), and the band
/// reports the LEVEL — above home (+1), inside (0), below (−1) — where "above" means beyond K natural
/// widths, floored at FloorFrac of the baseline. Departure-from-own-normal, so a persistently elevated
/// sense HABITUATES with horizon 1/Drift closes (mu walks to the new level, mad learns the departure as
/// volatility) — the saturation cure. NOT Home.cs's jump-re-center shape, for two measured reasons: the
/// event shape loses the REGIME (a plateau's dips re-center downward and reset the streak hysteresis, so
/// Surprised lost the elevated ERA the old level-test held through), and its in-band-only mad can never
/// calibrate to a spiky sense (every large |dev| routes to re-center, mad stays at the noise floor, the
/// band fires forever — the zero-width trap). The floor exists because a sense EMA can sit numerically
/// FLAT for whole eras (the ladder's ExcMint pins 1.000), decaying mad to ~0 where an epsilon wiggle
/// becomes a formal excursion. `warmup` closes must pass before the band may fire (it observes regardless):
/// one EMA horizon (1/Drift) for the cost senses — a working level must EXIST before departing it means
/// overheating, else the boot climb from zero hands every early boundary to the cost cutters (measured:
/// budget floored by close 6, WALL death at 94) — while the surprise band fires from its second close
/// (the world is genuinely new from the machine's first breath). File-scope: the Homeostat's condition
/// bands and the RulerLift's census-rate home are the same instrument.
sealed class HomeBand(int warmup = 1)
{
    internal const double K = 2.0, Drift = 1.0 / 16, FloorFrac = 0.05;   // Home.cs's k + the close-cadence horizon + the width floor
    public const int EmaHorizon = 16;                            // 1/Drift — the cost bands' establishment warmup derives from it
    double _mu, _mad; int _seen;

    /// Observe the close's EMA'd sense. Returns the LEVEL vs home: +1 above, −1 below, 0 inside (or warming up).
    public int Observe(double v)
    {
        if (double.IsNaN(v)) return 0;                      // a not-yet-defined sense never folds (HomeWatch's guard)
        if (_seen++ == 0) { _mu = v; _mad = 0.05 * Math.Abs(v) + 1e-6; return 0; }   // seed a relative noise floor
        double dev = v - _mu;
        int dir = Math.Abs(dev) > K * Width && _seen > warmup ? Math.Sign(dev) : 0;
        _mu  += Drift * dev;                                // the home walks toward the reading — continuous re-centering
        _mad += Drift * (Math.Abs(dev) - _mad);             // the width learns ALL deviation — volatility, not just noise
        return dir;
    }

    public double Mu    => _mu;
    public double Width => Math.Max(_mad, FloorFrac * Math.Abs(_mu));

    public HomeBand Copy()
    {
        HomeBand copy = new(warmup);
        copy._mu = _mu;
        copy._mad = _mad;
        copy._seen = _seen;
        return copy;
    }

    // checkpoint — the home IS these three numbers (K/Drift/FloorFrac/warmup are ctor-constants).
    public void Save(CkptWriter w) { w.F64(_mu); w.F64(_mad); w.I32(_seen); }
    public void Load(CkptReader r) { _mu = r.F64(); _mad = r.F64(); _seen = r.I32(); }
}
