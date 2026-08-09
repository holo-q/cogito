namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;

/// Exact threshold arithmetic owned by the policy boundary domain. The EML proof vocabulary is deliberately not
/// reused here: a policy boundary is a production guard, not a residual-expression proof.
public readonly struct PolicyBoundaryRational : IComparable<PolicyBoundaryRational>, IEquatable<PolicyBoundaryRational>
{
    public PolicyBoundaryRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException("policy boundary denominator cannot be zero");
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        BigInteger gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public static PolicyBoundaryRational Zero => new(0, 1);

    public static PolicyBoundaryRational Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        int slash = text.IndexOf('/');
        if (slash >= 0)
        {
            if (text.IndexOf('/', slash + 1) >= 0) throw new FormatException("policy boundary rational has more than one slash");
            return new PolicyBoundaryRational(
                BigInteger.Parse(text[..slash], CultureInfo.InvariantCulture),
                BigInteger.Parse(text[(slash + 1)..], CultureInfo.InvariantCulture));
        }
        int dot = text.IndexOf('.');
        if (dot < 0) return new PolicyBoundaryRational(BigInteger.Parse(text, CultureInfo.InvariantCulture), 1);
        bool negative = text[0] == '-';
        string digits = (negative ? text[1..] : text).Replace(".", string.Empty, StringComparison.Ordinal);
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) throw new FormatException($"invalid policy boundary '{text}'");
        BigInteger numerator = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (negative) numerator = -numerator;
        return new PolicyBoundaryRational(numerator, BigInteger.Pow(10, text.Length - dot - 1));
    }

    internal static bool TryParse(string text, out PolicyBoundaryRational value)
    {
        try { value = Parse(text); return true; }
        catch (FormatException) { value = default; return false; }
        catch (OverflowException) { value = default; return false; }
    }

    public static PolicyBoundaryRational FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "boundary must be finite");
        return Parse(value.ToString("R", CultureInfo.InvariantCulture));
    }

    public double ToDouble() => (double)Numerator / (double)Denominator;
    public int CompareTo(PolicyBoundaryRational other)
        => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public bool Equals(PolicyBoundaryRational other)
        => Numerator == other.Numerator && Denominator == other.Denominator;
    public override bool Equals(object? obj) => obj is PolicyBoundaryRational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public override string ToString() => Denominator.IsOne
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : string.Concat(Numerator.ToString(CultureInfo.InvariantCulture), "/", Denominator.ToString(CultureInfo.InvariantCulture));
    public static bool operator <(PolicyBoundaryRational left, PolicyBoundaryRational right) => left.CompareTo(right) < 0;
    public static bool operator >(PolicyBoundaryRational left, PolicyBoundaryRational right) => left.CompareTo(right) > 0;
    public static bool operator <=(PolicyBoundaryRational left, PolicyBoundaryRational right) => left.CompareTo(right) <= 0;
    public static bool operator >=(PolicyBoundaryRational left, PolicyBoundaryRational right) => left.CompareTo(right) >= 0;
    public static bool operator ==(PolicyBoundaryRational left, PolicyBoundaryRational right) => left.Equals(right);
    public static bool operator !=(PolicyBoundaryRational left, PolicyBoundaryRational right) => !left.Equals(right);
}

public readonly struct PolicyBoundaryObligationID : IEquatable<PolicyBoundaryObligationID>
{
    public PolicyBoundaryObligationID(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("policy boundary obligation id is required", nameof(value))
            : value;
    }

    public string Value { get; }
    public bool Equals(PolicyBoundaryObligationID other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is PolicyBoundaryObligationID other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
    public static bool operator ==(PolicyBoundaryObligationID left, PolicyBoundaryObligationID right) => left.Equals(right);
    public static bool operator !=(PolicyBoundaryObligationID left, PolicyBoundaryObligationID right) => !left.Equals(right);
}

/// The identity binds every semantic input that can change the meaning of a threshold. A value-only key is not
/// sufficient: the same number in another grammar revision or production has a different obligation.
public readonly record struct PolicyBoundaryIdentity(
    CortexPolicyID Policy,
    string Candidate,
    string Grammar,
    string Production,
    string Feature,
    string Statistic)
{
    public PolicyBoundaryObligationID ObligationID
    {
        get
        {
            string canonical = string.Join('\u001F', Policy.Value, Candidate, Grammar, Production, Feature, Statistic);
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return new PolicyBoundaryObligationID(Convert.ToHexStringLower(digest));
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Candidate) || string.IsNullOrWhiteSpace(Grammar)
            || string.IsNullOrWhiteSpace(Production) || string.IsNullOrWhiteSpace(Feature)
            || string.IsNullOrWhiteSpace(Statistic))
            throw new InvalidDataException("policy boundary identity requires candidate, grammar, production, feature, and statistic");
    }
}

public enum PolicyBoundaryComparisons : byte
{
    Unknown,
    LessThanOrEqual,
    GreaterThanOrEqual,
}

public readonly record struct PolicyBoundaryCandidate(
    PolicyBoundaryRational Boundary,
    PolicyBoundaryComparisons Comparison,
    string Provenance)
{
    public bool Allows(double observed)
    {
        PolicyBoundaryRational value = PolicyBoundaryRational.FromDouble(observed);
        return Comparison == PolicyBoundaryComparisons.LessThanOrEqual
            ? value <= Boundary
            : value >= Boundary;
    }
}

public enum PolicyBoundaryArms : byte
{
    Baseline,
    Candidate,
    ForcedDivergentNull,
    ReflexFrozenControl,
}

public readonly record struct PolicyBoundaryArmReceipt(
    PolicyBoundaryArms Arm,
    int Horizon,
    long PaidCloseDelta,
    long MatchedSpend,
    bool ContinuityExact,
    bool ChildProcessCompleted,
    long GrammarExecutionsDelta = 0,
    long TrialAdaptationTransitions = 0,
    bool AdaptationEnabled = true)
{
    /// Identity of the last policy decision actually executed on this child rail.
    /// Legacy/historical authority receipts may omit it; live paid divergence cannot.
    public CortexPolicyDecisionID ExecutedDecisionID { get; init; }
    public CortexPolicyTrialExecutionOutcomes ExecutionOutcome { get; init; } = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
    public long RequestCount { get; init; }
    public long GuardAdmittedCount { get; init; }
    public CortexPolicyDecisionID LastRequestDecisionID { get; init; }
    public int LastRequestStep { get; init; } = -1;
    public CortexPolicyDecisionReadout LastRequestReadout { get; init; }
    public int ExecutedStep { get; init; } = -1;
    public int ExecutedLaunchpadAction { get; init; } = -1;
    public int ExecutedRawCandidateAction { get; init; } = -1;
    public int ExecutedSelectedCandidateAction { get; init; } = -1;
    public int ExecutedAction { get; init; } = -1;
    public CortexPolicyAuthorities ExecutedAuthority { get; init; } = CortexPolicyAuthorities.Launchpad;
    public CortexPolicySelectionCauses ExecutedSelectionCause { get; init; } = CortexPolicySelectionCauses.Launchpad;
    public ulong ExecutedReadoutFingerprint { get; init; }
    public ulong ExecutedReadoutRevision { get; init; }
    public ulong ExecutedReadoutOccurrenceDigest { get; init; }
    public ulong ExecutedCandidateFingerprint { get; init; }
    public PolicyCanonicalStateID ExecutedCanonicalState { get; init; }
    public TapeEventID ExecutedDecisionEventID { get; init; }
    public TapeEventID ExecutedOutcomeEventID { get; init; }
    public string ExecutedOutcomePayloadSHA256 { get; init; } = "";
    public ulong ForcedDivergenceSeed { get; init; }
    /// Terminal state divergence is independent evidence from process completion and policy execution.
    /// It is meaningful only for the forced-null rail and must never be inferred from either axis.
    public bool Diverged { get; init; }
    public bool HasExecutedDecisionIdentity
        => ExecutedDecisionID.Value != 0 && ExecutedStep >= 0 && ExecutedLaunchpadAction >= 0 && ExecutedRawCandidateAction >= -1
            && ExecutedSelectedCandidateAction >= -1 && ExecutedAction >= 0
            && ExecutedReadoutFingerprint != 0 && ExecutedReadoutRevision != 0
            && Enum.IsDefined(ExecutedAuthority) && Enum.IsDefined(ExecutedSelectionCause);
    public bool HasAnyExecutedDecisionIdentityData
        => ExecutedDecisionID.Value != 0 || ExecutedStep != -1 || ExecutedLaunchpadAction != -1
            || ExecutedRawCandidateAction != -1 || ExecutedSelectedCandidateAction != -1 || ExecutedAction != -1
            || ExecutedAuthority != CortexPolicyAuthorities.Launchpad || ExecutedSelectionCause != CortexPolicySelectionCauses.Launchpad
            || ExecutedReadoutFingerprint != 0 || ExecutedReadoutRevision != 0
            || ExecutedReadoutOccurrenceDigest != 0 || ExecutedCandidateFingerprint != 0
            || ExecutedCanonicalState.Version != 0
            || ExecutedDecisionEventID.Value != 0 || ExecutedOutcomeEventID.Value != 0
            || ExecutedOutcomePayloadSHA256.Length != 0 || ForcedDivergenceSeed != 0;

    public bool BehaviorallyExecuted
        => ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && GuardAdmittedCount > 0
            && HasExecutedDecisionIdentity;

    internal void ValidateRequestAccounting(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!Enum.IsDefined(ExecutionOutcome) || RequestCount < 0 || GuardAdmittedCount < 0 || GuardAdmittedCount > RequestCount)
            throw new InvalidDataException("policy boundary arm request accounting is malformed");
        if (ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
            && (RequestCount != 0 || GuardAdmittedCount != 0))
            throw new InvalidDataException("policy boundary arm marks not-attempted execution with requests");
        if (ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.GuardDenied
            && (RequestCount == 0 || GuardAdmittedCount != 0))
            throw new InvalidDataException("policy boundary arm carries invalid guard-denied accounting");
        if ((ExecutionOutcome is CortexPolicyTrialExecutionOutcomes.NotAttempted or CortexPolicyTrialExecutionOutcomes.GuardDenied)
            && HasAnyExecutedDecisionIdentityData)
            throw new InvalidDataException("policy boundary arm carries partial executed identity without configured execution");
        if (ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (!HasExecutedDecisionIdentity || !domain.ValidateCanonicalState(ExecutedCanonicalState)))
            throw new InvalidDataException("policy boundary arm marks configured execution observed without an identity");
        if (RequestCount == 0)
        {
            if (LastRequestDecisionID.Value != 0 || LastRequestStep != -1)
                throw new InvalidDataException("policy boundary arm invents a request identity for zero requests");
        }
        else
        {
            if (LastRequestDecisionID.Value == 0 || LastRequestStep < 0)
                throw new InvalidDataException("policy boundary arm omits its last request identity");
            LastRequestReadout.Validate(domain.Schema.ActionCount);
        }
    }

    internal void ValidateExecutedDecisionIdentity(IPolicyBoundaryDomain domain, bool requireGrammar = false)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if ((ExecutionOutcome is CortexPolicyTrialExecutionOutcomes.NotAttempted or CortexPolicyTrialExecutionOutcomes.GuardDenied)
            && GuardAdmittedCount == 0)
        {
            if (HasAnyExecutedDecisionIdentityData)
                throw new InvalidDataException("policy boundary arm carries partial executed identity without configured execution");
            return;
        }
        if (!HasExecutedDecisionIdentity || !domain.ValidateCanonicalState(ExecutedCanonicalState))
            throw new InvalidDataException("policy boundary arm omits its executed decision identity");
        if (ExecutedSelectionCause is not (CortexPolicySelectionCauses.Launchpad
            or CortexPolicySelectionCauses.ShadowCandidate
            or CortexPolicySelectionCauses.GrammarCandidate
            or CortexPolicySelectionCauses.TrialOverride))
            throw new InvalidDataException("policy boundary arm carries a non-trial execution cause");
        if ((ExecutedRawCandidateAction == -1) != (ExecutedSelectedCandidateAction == -1))
            throw new InvalidDataException("policy boundary arm candidate action presence is inconsistent");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.Launchpad
            && (ExecutedRawCandidateAction >= 0 || ExecutedSelectedCandidateAction >= 0
                || ExecutedAction != ExecutedLaunchpadAction
                || ExecutedReadoutOccurrenceDigest != 0 || ExecutedCandidateFingerprint != 0))
            throw new InvalidDataException("policy boundary arm launchpad identity is inconsistent");
        if (ExecutedSelectionCause != CortexPolicySelectionCauses.Launchpad
            && (ExecutedReadoutOccurrenceDigest == 0 || ExecutedCandidateFingerprint == 0))
            throw new InvalidDataException("policy boundary arm configured execution omits readout support");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.ShadowCandidate
            && (ExecutedSelectedCandidateAction < 0 || ExecutedAction != ExecutedLaunchpadAction))
            throw new InvalidDataException("policy boundary arm shadow candidate identity is inconsistent");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.GrammarCandidate
            && ExecutedSelectedCandidateAction != ExecutedAction)
            throw new InvalidDataException("policy boundary arm Grammar candidate identity is inconsistent");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && (ExecutedSelectedCandidateAction != ExecutedAction
                || ExecutedSelectedCandidateAction == ExecutedRawCandidateAction
                || ExecutedDecisionEventID.Value <= 0 || ForcedDivergenceSeed == 0))
            throw new InvalidDataException("policy boundary arm trial override identity is inconsistent");
        if ((ExecutedOutcomeEventID.Value == 0) != (ExecutedOutcomePayloadSHA256.Length == 0)
            || ExecutedOutcomeEventID.Value < 0
            || ExecutedOutcomePayloadSHA256.Length != 0
                && (ExecutedOutcomePayloadSHA256.Length != 64
                    || ExecutedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("policy boundary arm ordinary outcome identity is malformed");
        if (ExecutedSelectionCause != CortexPolicySelectionCauses.TrialOverride
            && (ExecutedDecisionEventID.Value != 0 || ForcedDivergenceSeed != 0))
            throw new InvalidDataException("policy boundary non-TrialOverride arm carries forced-event custody");
        if (!domain.ValidateExecutionAuthority(ExecutedAuthority, ExecutedSelectionCause, requireGrammar))
            throw new InvalidDataException("policy boundary arm execution authority is not owned by its policy domain");
        if (!domain.ValidateActionRelation(
                ExecutedSelectionCause, ExecutedLaunchpadAction, ExecutedRawCandidateAction,
                ExecutedSelectedCandidateAction, ExecutedAction))
            throw new InvalidDataException("policy boundary arm action relation is not owned by its policy domain");
    }

    public void ValidateExecutedReadoutAncestry(
        CortexPolicyID policy,
        ulong sourceRevision,
        IPolicyBoundaryDomain domain,
        ulong expectedCurrentReadoutFingerprint = 0,
        ulong expectedCurrentReadoutRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ValidateExecutedDecisionIdentity(domain);
        if (sourceRevision == 0 || ExecutedReadoutRevision < sourceRevision)
            throw new InvalidDataException("policy boundary arm executed readout predates its paid source");
        // The executed identity is the canonical program digest carried by the
        // execution outcome. A publication revision is provenance only; it is
        // not an authority from which the digest may be reconstructed.
        if (ExecutedReadoutFingerprint == 0)
            throw new InvalidDataException("policy boundary arm executed readout omits its carried program digest");
        if (expectedCurrentReadoutFingerprint != 0
            && (ExecutedReadoutFingerprint != expectedCurrentReadoutFingerprint
                || expectedCurrentReadoutRevision == 0
                || ExecutedReadoutRevision != expectedCurrentReadoutRevision))
            throw new InvalidDataException("policy boundary arm executed readout disagrees with its sibling current readout identity");
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Arm) || Horizon <= 0 || PaidCloseDelta < 0 || MatchedSpend < 0 || GrammarExecutionsDelta < 0 || TrialAdaptationTransitions < 0)
            throw new InvalidDataException("invalid policy boundary arm receipt");
        if (!ContinuityExact)
            throw new InvalidDataException("policy boundary arm lost exact continuity");
        if (Diverged && Arm != PolicyBoundaryArms.ForcedDivergentNull)
            throw new InvalidDataException("policy boundary divergence is only valid on the forced-null arm");
        if (Diverged && !ChildProcessCompleted)
            throw new InvalidDataException("policy boundary arm records divergence without a completed child process");
        bool expectedAdaptation = Arm != PolicyBoundaryArms.ReflexFrozenControl;
        if (AdaptationEnabled != expectedAdaptation)
            throw new InvalidDataException($"policy boundary arm {Arm} adaptation-enabled state does not match its rail");
        if (Arm == PolicyBoundaryArms.ReflexFrozenControl && GrammarExecutionsDelta != 0)
            throw new InvalidDataException("reflex frozen control executed policy grammar");
        if (Arm == PolicyBoundaryArms.ReflexFrozenControl && TrialAdaptationTransitions != 0)
            throw new InvalidDataException("reflex frozen control recorded adaptation transitions");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && (ExecutedDecisionEventID.Value <= 0 || ForcedDivergenceSeed == 0))
            throw new InvalidDataException("policy boundary TrialOverride arm lacks event and forced-seed custody");
        if ((ExecutedOutcomeEventID.Value == 0) != (ExecutedOutcomePayloadSHA256.Length == 0)
            || ExecutedOutcomeEventID.Value < 0
            || ExecutedOutcomePayloadSHA256.Length != 0
                && (ExecutedOutcomePayloadSHA256.Length != 64
                    || ExecutedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("policy boundary arm ordinary outcome identity is malformed");
    }

    internal void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        Validate();
        ValidateRequestAccounting(domain);
        ValidateExecutedDecisionIdentity(domain);
    }
}

/// The Verified conjunction, computed ONCE so the runner, the tape verifier, the receipt's own
/// Validate, and the inter-rung kill can never drift. Each conjunct stays a separate field because
/// callers respond differently: the runner THROWS on a broken frozen-control (an invariant violation,
/// not a verdict), while the tape verifier only compares the fused result against its stored flag.
/// The two forced-null conjuncts are meaningful only at the terminal horizon and read true over an
/// absent row (`.All` semantics) — a partial ladder must therefore ignore them (they cannot yet be
/// decided), which is exactly why they are excluded from the monotone-fatal kill test below.
internal readonly record struct PolicyBoundaryVerdict(
    bool ContinuityExact,
    bool MatchedSpend,
    bool AllChildrenCompleted,
    bool ForcedNullBehaviorExecuted,
    bool ForcedNullDiverged,
    bool ReflexGrammarFrozen,
    bool ReflexAdaptationFrozen,
    bool BaselineNoWorse)
{
    public bool ReflexFrozen => ReflexGrammarFrozen && ReflexAdaptationFrozen;
    public bool Verified => ContinuityExact && MatchedSpend && AllChildrenCompleted
        && ForcedNullBehaviorExecuted && ForcedNullDiverged && ReflexFrozen && BaselineNoWorse;

    /// The conjuncts that are AND-folded over ALL rows and become monotonically unrecoverable the
    /// instant one completed rung breaks them: a false here can never be restored by a later rung, so
    /// the terminal verdict is already decided FAIL. The two forced-null (terminal-only) conjuncts and
    /// the frozen-control throws are deliberately NOT part of this test — the former are undecidable
    /// before the terminal rung, the latter are raised as invariant violations by the caller.
    public bool MonotoneFatal => !ContinuityExact || !MatchedSpend || !AllChildrenCompleted || !BaselineNoWorse;

    /// Fold the arm rows into the verdict. `horizons[^1]` names the terminal horizon whose forced-null
    /// rail carries the behavioral-execution and divergence evidence.
    internal static PolicyBoundaryVerdict Compute(
        IReadOnlyList<PolicyBoundaryArmReceipt> rows, IReadOnlyList<int> horizons)
    {
        if (rows is null || horizons is null || horizons.Count == 0)
            throw new InvalidDataException("policy boundary verdict requires arm rows and horizons");
        int terminalHorizon = horizons[^1];
        bool continuity = true, children = true, matched = true, baselineNoWorse = true;
        bool reflexGrammar = true, reflexAdaptation = true;
        bool forcedBehavior = true, forcedDiverged = true;
        for (int r = 0; r < rows.Count; r++)
        {
            PolicyBoundaryArmReceipt row = rows[r];
            if (!row.ContinuityExact) continuity = false;
            if (!row.ChildProcessCompleted) children = false;
            if (row.Arm == PolicyBoundaryArms.ReflexFrozenControl)
            {
                if (row.GrammarExecutionsDelta != 0) reflexGrammar = false;
                if (row.TrialAdaptationTransitions != 0 || row.AdaptationEnabled) reflexAdaptation = false;
            }
            if (row.Arm == PolicyBoundaryArms.ForcedDivergentNull && row.Horizon == terminalHorizon)
            {
                if (!row.BehaviorallyExecuted) forcedBehavior = false;
                if (!row.Diverged) forcedDiverged = false;
            }
        }
        for (int h = 0; h < horizons.Count; h++)
        {
            int horizon = horizons[h];
            long baselineSpend = 0, baselinePaid = 0, candidatePaid = 0;
            bool haveBaseline = false, haveCandidate = false;
            for (int r = 0; r < rows.Count; r++)
            {
                PolicyBoundaryArmReceipt row = rows[r];
                if (row.Horizon != horizon) continue;
                if (row.Arm == PolicyBoundaryArms.Baseline) { baselineSpend = row.MatchedSpend; baselinePaid = row.PaidCloseDelta; haveBaseline = true; }
                else if (row.Arm == PolicyBoundaryArms.Candidate) { candidatePaid = row.PaidCloseDelta; haveCandidate = true; }
            }
            if (!haveBaseline) { matched = false; baselineNoWorse = false; continue; }
            for (int r = 0; r < rows.Count; r++)
            {
                PolicyBoundaryArmReceipt row = rows[r];
                if (row.Horizon == horizon && row.MatchedSpend != baselineSpend) matched = false;
            }
            if (!haveCandidate || candidatePaid < baselinePaid) baselineNoWorse = false;
        }
        return new PolicyBoundaryVerdict(continuity, matched, children,
            forcedBehavior, forcedDiverged, reflexGrammar, reflexAdaptation, baselineNoWorse);
    }
}

/// A receipt is admissible only when the four rails share spend and continuity, the forced null diverges, and the
/// reflex-frozen control performs neither policy grammar nor adaptation transitions. This is evidence, never an actuator by itself.
public readonly record struct PolicyBoundaryForkReceipt(
    PolicyBoundaryObligationID Obligation,
    PolicyBoundaryRational BaselineBoundary,
    PolicyBoundaryRational CandidateBoundary,
    int[] Horizons,
    PolicyBoundaryArmReceipt[] Arms,
    bool ContinuityExact,
    bool MatchedSpend,
    bool ForcedNullBehaviorExecuted,
    bool Verified,
    ulong SourceDecisionReadoutFingerprint = 0,
    ulong SourceDecisionReadoutRevision = 0,
    PolicyBoundaryTeacherCorroboration? TeacherCorroboration = null,
    PaidDivergenceExecutionCorroboration? ExecutionCorroboration = null)
{
    public long ComputeLadderMatchedSpend()
    {
        if (Arms is null) throw new InvalidDataException("policy boundary receipt has no arm rows");
        long spend = 0;
        for (int index = 0; index < Arms.Length; index++)
            spend = checked(spend + Arms[index].MatchedSpend);
        return spend;
    }

    public long ComputeTerminalMatchedSpend()
    {
        if (Horizons is null || Horizons.Length == 0 || Arms is null)
            throw new InvalidDataException("policy boundary receipt has no terminal horizon");
        int terminalHorizon = Horizons[^1];
        long spend = 0;
        int rows = 0;
        for (int index = 0; index < Arms.Length; index++)
        {
            PolicyBoundaryArmReceipt arm = Arms[index];
            if (arm.Horizon != terminalHorizon) continue;
            spend = checked(spend + arm.MatchedSpend);
            rows++;
        }
        if (rows != 4)
            throw new InvalidDataException("policy boundary receipt does not carry four terminal arm rows");
        return spend;
    }

    /// Semantic candidate identity is separate from the publication/readout identity.
    public ulong SourceDecisionCandidateFingerprint { get; init; }
    public CortexPolicyQuotaDecisionID QuotaDecisionID { get; init; }
    public bool AllChildrenCompleted
        => Arms is not null && Arms.Length > 0 && Arms.All(static arm => arm.ChildProcessCompleted);

    public bool ForcedNullDiverged
    {
        get
        {
            if (Arms is null || Horizons is null || Horizons.Length == 0) return false;
            int terminalHorizon = Horizons[^1];
            bool found = false;
            for (int index = 0; index < Arms.Length; index++)
            {
                PolicyBoundaryArmReceipt arm = Arms[index];
                if (arm.Arm != PolicyBoundaryArms.ForcedDivergentNull || arm.Horizon != terminalHorizon) continue;
                found = true;
                if (!arm.Diverged) return false;
            }
            return found;
        }
    }
    public bool BaselineNoWorse
    {
        get
        {
            if (Horizons is null || Arms is null) return false;
            for (int i = 0; i < Horizons.Length; i++)
            {
                int horizon = Horizons[i];
                long baseline = 0;
                long candidate = 0;
                bool foundBaseline = false;
                bool foundCandidate = false;
                for (int j = 0; j < Arms.Length; j++)
                {
                    PolicyBoundaryArmReceipt arm = Arms[j];
                    if (arm.Horizon != horizon) continue;
                    if (arm.Arm == PolicyBoundaryArms.Baseline) { baseline = arm.PaidCloseDelta; foundBaseline = true; }
                    if (arm.Arm == PolicyBoundaryArms.Candidate) { candidate = arm.PaidCloseDelta; foundCandidate = true; }
                }
                if (!foundBaseline || !foundCandidate || candidate < baseline) return false;
            }
            return true;
        }
    }

    internal void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (SourceDecisionReadoutFingerprint == 0 || SourceDecisionReadoutRevision == 0)
            throw new InvalidDataException("policy boundary receipt lacks its source decision readout identity");
        if (SourceDecisionCandidateFingerprint == 0)
            throw new InvalidDataException("policy boundary receipt lacks its source candidate identity");
        if (Horizons is null || Arms is null || Horizons.Length != 3 || Arms.Length != Horizons.Length * 4)
            throw new InvalidDataException("policy boundary receipt must carry four arms at every horizon");
        int prior = 0;
        for (int i = 0; i < Horizons.Length; i++)
        {
            if (Horizons[i] <= prior) throw new InvalidDataException("policy boundary horizons must increase strictly");
            prior = Horizons[i];
            for (int arm = 0; arm < 4; arm++)
            {
                PolicyBoundaryArmReceipt row = Arms[i * 4 + arm];
                row.Validate(domain);
                if ((int)row.Arm != arm || row.Horizon != Horizons[i])
                    throw new InvalidDataException("policy boundary arm ordering does not match its horizon");
            }
        }
        int terminalHorizon = Horizons[^1];
        PolicyBoundaryArmReceipt terminalCandidate = Arms.First(arm => arm.Arm == PolicyBoundaryArms.Candidate && arm.Horizon == terminalHorizon);
        PolicyBoundaryArmReceipt terminalForcedNull = Arms.First(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == terminalHorizon);
        ValidateTerminal(terminalCandidate, requireConfiguredExecution: false, requireDivergence: false);
        ValidateTerminal(terminalForcedNull, requireConfiguredExecution: true, requireDivergence: true);
        if (terminalCandidate.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (terminalCandidate.ExecutedAuthority != domain.SeedAuthority.CandidateAuthority
                || terminalCandidate.ExecutedSelectionCause != domain.SeedAuthority.CandidateSelectionCause))
            throw new InvalidDataException("policy boundary candidate rail authority/cause disagrees with its policy domain");
        if (terminalForcedNull.ExecutedAuthority != domain.SeedAuthority.ForcedNullAuthority
            || terminalForcedNull.ExecutedSelectionCause != domain.SeedAuthority.ForcedNullSelectionCause)
            throw new InvalidDataException("policy boundary forced-null rail authority/cause disagrees with its policy domain");
        PolicyBoundaryVerdict verdict = PolicyBoundaryVerdict.Compute(Arms, Horizons);
        bool computedVerified = verdict.Verified;
        if (ContinuityExact != verdict.ContinuityExact || MatchedSpend != verdict.MatchedSpend
            || ForcedNullBehaviorExecuted != verdict.ForcedNullBehaviorExecuted || Verified != computedVerified)
            throw new InvalidDataException("policy boundary receipt summary flags disagree with its arm rows");
        if (!computedVerified)
            throw new InvalidDataException("policy boundary receipt lacks continuity, matched-spend, completed-child, behavioral-execution, forced-null-divergence, frozen-control, baseline, or verification proof");
        for (int index = 0; index < Arms.Length; index++)
        {
            PolicyBoundaryArmReceipt row = Arms[index];
            row.ValidateRequestAccounting(domain);
            if ((row.ExecutionOutcome is CortexPolicyTrialExecutionOutcomes.NotAttempted or CortexPolicyTrialExecutionOutcomes.GuardDenied)
                && row.GuardAdmittedCount == 0)
                continue;
            row.ValidateExecutedReadoutAncestry(domain.PolicyID, SourceDecisionReadoutRevision, domain);
            if (TeacherCorroboration is not null && row.ExecutedReadoutRevision <= TeacherCorroboration.TeacherRevision.Value)
                throw new InvalidDataException("policy boundary arm executed readout predates its teacher corroboration");
        }
        if (TeacherCorroboration is not null) TeacherCorroboration.Validate();
        if (ExecutionCorroboration is PaidDivergenceExecutionCorroboration execution)
        {
            execution.Validate();
            if (!execution.QuotaDecisionID.Equals(QuotaDecisionID)
                || execution.QuotaReadoutFingerprint != SourceDecisionReadoutFingerprint
                || execution.QuotaCandidateFingerprint != SourceDecisionCandidateFingerprint
                || execution.FundingCandidateRevision.Value != SourceDecisionReadoutRevision)
                throw new InvalidDataException("policy boundary execution corroboration payment/readout identity drifted");
            PolicyBoundaryArmReceipt forcedNull = Arms.First(arm => arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == terminalHorizon);
            if (!forcedNull.ExecutedDecisionID.Equals(execution.ExecutedDivergenceDecisionID))
                throw new InvalidDataException("policy boundary execution corroboration child decision identity drifted");
            PolicyBoundaryForkReceipt currentReceipt = this;
            if (execution.ExecutedDivergenceOutcomeID != PolicyBoundaryDivergenceProof.ComputeOutcomeID(
                    in currentReceipt, in forcedNull, forcedNull.ExecutedAction,
                    domain)
                || execution.ChildExecutionReceiptSHA256 != Cortex.DigestExecutedDivergenceChildExecution(
                    execution.ExecutedDivergenceDecisionID, execution.ExecutedDivergenceOutcomeID))
                throw new InvalidDataException("policy boundary execution corroboration child receipt identity drifted");
        }

        static void ValidateTerminal(PolicyBoundaryArmReceipt arm, bool requireConfiguredExecution, bool requireDivergence)
        {
            string tuple = $"arm={arm.Arm} process={(arm.ChildProcessCompleted ? 1 : 0)} outcome={arm.ExecutionOutcome} request={arm.RequestCount} admitted={arm.GuardAdmittedCount} decision={arm.ExecutedDecisionID.Value} step={arm.ExecutedStep} cause={arm.ExecutedSelectionCause}";
            bool configuredExecution = arm.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
            if (!arm.ChildProcessCompleted)
                Fail("child-process-completed", tuple);
            if (requireConfiguredExecution && !configuredExecution)
                Fail("configured-cause-executed", tuple);
            if ((requireConfiguredExecution || configuredExecution) && arm.RequestCount <= 0)
                Fail("request-count-positive", tuple);
            if ((requireConfiguredExecution || configuredExecution) && arm.GuardAdmittedCount <= 0)
                Fail("guard-admitted-count-positive", tuple);
            if ((requireConfiguredExecution || configuredExecution) && !arm.HasExecutedDecisionIdentity)
                Fail("executed-decision-identity", tuple);
            if (!requireConfiguredExecution && arm.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
                && (arm.RequestCount != 0 || arm.GuardAdmittedCount != 0 || arm.HasAnyExecutedDecisionIdentityData))
                Fail("not-attempted-accounting", tuple);
            if (!requireConfiguredExecution && arm.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.GuardDenied
                && (arm.RequestCount <= 0 || arm.GuardAdmittedCount != 0 || arm.HasAnyExecutedDecisionIdentityData))
                Fail("guard-denied-accounting", tuple);
            if (requireDivergence && !arm.Diverged)
                Fail("forced-null-diverged", tuple);

            static void Fail(string predicate, string tuple)
                => throw new InvalidDataException($"policy boundary terminal predicate failed: {predicate}; {tuple}");
        }
    }

    internal void ValidateDivergenceCorroboration(ulong readoutFingerprint, GrammarRevisionID readoutRevision, IPolicyBoundaryDomain domain)
    {
        Validate(domain);
        if (SourceDecisionReadoutFingerprint != readoutFingerprint || SourceDecisionReadoutRevision != readoutRevision.Value)
            throw new InvalidDataException("policy boundary divergence corroboration does not bind the receipt readout");
        if (TeacherCorroboration is not null && TeacherCorroboration.TeacherRevision.Value >= readoutRevision.Value)
            throw new InvalidDataException("policy boundary divergence corroboration revision ordering is not fold-to-teacher-to-readout");
    }
}

public readonly record struct PolicyBoundaryReadout(
    PolicyBoundaryObligationID Obligation,
    PolicyBoundaryRational Boundary,
    PolicyBoundaryComparisons Comparison,
    bool Verified,
    bool GuardSatisfied,
    string ReceiptDigest)
{
    public bool CanActuate => Verified && GuardSatisfied;
}

/// The narrow outcome contract that the existing Cortex fork engine feeds into a policy boundary receipt. No trial
/// scheduler lives here: callers mount the ordinary funding lease and pass the engine's matched arms through this
/// adapter.
internal readonly record struct PolicyBoundaryTrialOutcome(
    long PaidCloseDelta,
    long MatchedSpend,
    bool ChildProcessCompleted,
    long GrammarExecutionsDelta = 0,
    long TrialAdaptationTransitions = 0,
    bool AdaptationEnabled = true)
{
    public bool ContinuityExact { get; init; } = true;
    public CortexPolicyTrialExecutionOutcomes ExecutionOutcome { get; init; } = CortexPolicyTrialExecutionOutcomes.NotAttempted;
    public long RequestCount { get; init; }
    public long GuardAdmittedCount { get; init; }
    public CortexPolicyDecisionID LastRequestDecisionID { get; init; }
    public int LastRequestStep { get; init; } = -1;
    public CortexPolicyDecisionReadout LastRequestReadout { get; init; }
    public CortexPolicyDecisionID ExecutedDecisionID { get; init; }
    public int ExecutedStep { get; init; } = -1;
    public int ExecutedLaunchpadAction { get; init; } = -1;
    public int ExecutedRawCandidateAction { get; init; } = -1;
    public int ExecutedSelectedCandidateAction { get; init; } = -1;
    public int ExecutedAction { get; init; } = -1;
    public CortexPolicyAuthorities ExecutedAuthority { get; init; } = CortexPolicyAuthorities.Launchpad;
    public CortexPolicySelectionCauses ExecutedSelectionCause { get; init; } = CortexPolicySelectionCauses.Launchpad;
    public ulong ExecutedReadoutFingerprint { get; init; }
    public ulong ExecutedReadoutRevision { get; init; }
    public ulong ExecutedReadoutOccurrenceDigest { get; init; }
    public ulong ExecutedCandidateFingerprint { get; init; }
    public PolicyCanonicalStateID ExecutedCanonicalState { get; init; }
    public TapeEventID ExecutedDecisionEventID { get; init; }
    public TapeEventID ExecutedOutcomeEventID { get; init; }
    public string ExecutedOutcomePayloadSHA256 { get; init; } = "";
    public ulong ForcedDivergenceSeed { get; init; }

    public bool HasExecutedDecisionIdentity
        => ExecutedDecisionID.Value != 0 && ExecutedStep >= 0 && ExecutedLaunchpadAction >= 0 && ExecutedRawCandidateAction >= -1
            && ExecutedSelectedCandidateAction >= -1 && ExecutedAction >= 0
            && ExecutedReadoutFingerprint != 0 && ExecutedReadoutRevision != 0
            && Enum.IsDefined(ExecutedAuthority) && Enum.IsDefined(ExecutedSelectionCause);

    public void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (PaidCloseDelta < 0 || MatchedSpend < 0 || GrammarExecutionsDelta < 0 || TrialAdaptationTransitions < 0
            || !Enum.IsDefined(ExecutionOutcome) || RequestCount < 0 || GuardAdmittedCount < 0 || GuardAdmittedCount > RequestCount)
            throw new InvalidDataException("invalid policy boundary trial outcome");
        if (ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
            && (RequestCount != 0 || GuardAdmittedCount != 0))
            throw new InvalidDataException("policy boundary trial outcome marks not-attempted execution with requests");
        if (ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.GuardDenied
            && (RequestCount == 0 || GuardAdmittedCount != 0))
            throw new InvalidDataException("policy boundary trial outcome carries invalid guard-denied accounting");
        if ((ExecutionOutcome is CortexPolicyTrialExecutionOutcomes.NotAttempted or CortexPolicyTrialExecutionOutcomes.GuardDenied)
            && GuardAdmittedCount == 0
            && (ExecutedDecisionID.Value != 0 || ExecutedStep != -1 || ExecutedLaunchpadAction != -1
                || ExecutedRawCandidateAction != -1 || ExecutedSelectedCandidateAction != -1 || ExecutedAction != -1
                || ExecutedAuthority != CortexPolicyAuthorities.Launchpad || ExecutedSelectionCause != CortexPolicySelectionCauses.Launchpad
                || ExecutedReadoutFingerprint != 0 || ExecutedReadoutRevision != 0 || ExecutedReadoutOccurrenceDigest != 0 || ExecutedCandidateFingerprint != 0
                || ExecutedCanonicalState.Version != 0 || ExecutedDecisionEventID.Value != 0
                || ExecutedOutcomeEventID.Value != 0 || ExecutedOutcomePayloadSHA256.Length != 0 || ForcedDivergenceSeed != 0))
            throw new InvalidDataException("policy boundary trial outcome carries partial executed identity without configured execution");
        if (ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (!HasExecutedDecisionIdentity || ExecutedCanonicalState.Version == 0))
            throw new InvalidDataException("policy boundary trial outcome marks configured execution observed without an identity");
        if (RequestCount == 0)
        {
            if (LastRequestDecisionID.Value != 0 || LastRequestStep != -1)
                throw new InvalidDataException("policy boundary trial outcome invents a request identity for zero requests");
        }
        else
        {
            if (LastRequestDecisionID.Value == 0 || LastRequestStep < 0)
                throw new InvalidDataException("policy boundary trial outcome omits its last request identity");
            LastRequestReadout.Validate(domain.Schema.ActionCount);
        }
    }

    public void ValidateExecutionIdentity(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if ((ExecutionOutcome is CortexPolicyTrialExecutionOutcomes.NotAttempted or CortexPolicyTrialExecutionOutcomes.GuardDenied)
            && GuardAdmittedCount == 0)
        {
            if (ExecutedDecisionID.Value != 0 || ExecutedStep != -1 || ExecutedLaunchpadAction != -1
                || ExecutedRawCandidateAction != -1 || ExecutedSelectedCandidateAction != -1 || ExecutedAction != -1
                || ExecutedAuthority != CortexPolicyAuthorities.Launchpad || ExecutedSelectionCause != CortexPolicySelectionCauses.Launchpad
                || ExecutedReadoutFingerprint != 0 || ExecutedReadoutRevision != 0 || ExecutedReadoutOccurrenceDigest != 0 || ExecutedCandidateFingerprint != 0
                || ExecutedCanonicalState.Version != 0 || ExecutedDecisionEventID.Value != 0
                || ExecutedOutcomeEventID.Value != 0 || ExecutedOutcomePayloadSHA256.Length != 0 || ForcedDivergenceSeed != 0)
                throw new InvalidDataException("policy boundary trial outcome carries partial executed identity without configured execution");
            return;
        }
        if (!HasExecutedDecisionIdentity || ExecutedCanonicalState.Version == 0)
            throw new InvalidDataException("policy boundary trial outcome omits its executed decision identity");
        if (ExecutedSelectionCause is not (CortexPolicySelectionCauses.Launchpad
            or CortexPolicySelectionCauses.ShadowCandidate
            or CortexPolicySelectionCauses.GrammarCandidate
            or CortexPolicySelectionCauses.TrialOverride))
            throw new InvalidDataException("policy boundary trial outcome carries a non-trial execution cause");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && (ExecutedDecisionEventID.Value <= 0 || ForcedDivergenceSeed == 0))
            throw new InvalidDataException("policy boundary TrialOverride outcome omits event or forced-seed custody");
        if ((ExecutedOutcomeEventID.Value == 0) != (ExecutedOutcomePayloadSHA256.Length == 0)
            || ExecutedOutcomeEventID.Value < 0
            || ExecutedOutcomePayloadSHA256.Length != 0
                && (ExecutedOutcomePayloadSHA256.Length != 64
                    || ExecutedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("policy boundary ordinary outcome identity is malformed");
        if (ExecutedSelectionCause != CortexPolicySelectionCauses.TrialOverride
            && (ExecutedDecisionEventID.Value != 0 || ForcedDivergenceSeed != 0))
            throw new InvalidDataException("policy boundary non-TrialOverride outcome carries forced-event custody");
        if (!Enum.IsDefined(ExecutedAuthority) || !Enum.IsDefined(ExecutedSelectionCause)
            || (ExecutedRawCandidateAction == -1) != (ExecutedSelectedCandidateAction == -1))
            throw new InvalidDataException("policy boundary trial outcome carries malformed executed authority/cause identity");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.Launchpad
            && (ExecutedRawCandidateAction >= 0 || ExecutedSelectedCandidateAction >= 0
                || ExecutedAuthority != CortexPolicyAuthorities.Launchpad || ExecutedAction != ExecutedLaunchpadAction
                || ExecutedReadoutOccurrenceDigest != 0 || ExecutedCandidateFingerprint != 0))
            throw new InvalidDataException("policy boundary trial outcome launchpad identity is inconsistent");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.ShadowCandidate
            && (ExecutedSelectedCandidateAction < 0 || ExecutedAction != ExecutedLaunchpadAction))
            throw new InvalidDataException("policy boundary trial outcome shadow candidate identity is inconsistent");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.GrammarCandidate
            && ExecutedSelectedCandidateAction != ExecutedAction)
            throw new InvalidDataException("policy boundary trial outcome Grammar identity is inconsistent");
        if (ExecutedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && (ExecutedSelectedCandidateAction != ExecutedAction
                || ExecutedSelectedCandidateAction == ExecutedRawCandidateAction))
            throw new InvalidDataException("policy boundary trial outcome trial identity is inconsistent");
        if (!domain.ValidateExecutionAuthority(ExecutedAuthority, ExecutedSelectionCause))
            throw new InvalidDataException("policy boundary trial outcome execution authority is not owned by its policy domain");
        if (ExecutedSelectionCause != CortexPolicySelectionCauses.Launchpad
            && (ExecutedReadoutOccurrenceDigest == 0 || ExecutedCandidateFingerprint == 0))
            throw new InvalidDataException("policy boundary trial outcome configured execution omits readout support");
    }
}

internal static class PolicyBoundaryForkRunner
{
    internal static PolicyBoundaryForkReceipt Run(
        Cortex spawning,
        IPolicyBoundaryDomain domain,
        CortexForkSeed seed,
        PolicyBoundaryIdentity identity,
        PolicyBoundaryRational baselineBoundary,
        PolicyBoundaryRational candidateBoundary,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] baselineArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] candidateArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] forcedNullArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] reflexArms,
        int[] horizons,
        ulong sourceDecisionReadoutFingerprint,
        ulong sourceDecisionCandidateFingerprint,
        ulong sourceDecisionReadoutRevision)
    {
        ArgumentNullException.ThrowIfNull(spawning);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(seed);
        identity.Validate();
        if (baselineArms is null || candidateArms is null || forcedNullArms is null || reflexArms is null || horizons is null
            || baselineArms.Length != horizons.Length || candidateArms.Length != horizons.Length || forcedNullArms.Length != horizons.Length || reflexArms.Length != horizons.Length
            || horizons.Length != 3)
            throw new ArgumentException("policy boundary forks require tree-era, candidate, forced-null, and reflex-frozen arms per horizon");
        for (int i = 1; i < horizons.Length; i++)
            if (horizons[i] <= horizons[i - 1])
                throw new ArgumentException("policy boundary horizons must increase strictly", nameof(horizons));
        PolicyBoundaryObligationID obligation = identity.ObligationID;
        if (baselineArms[0].InterveneAfterLoad is null
            || candidateArms[0].InterveneAfterLoad is null
            || forcedNullArms[0].InterveneAfterLoad is null
            || reflexArms[0].InterveneAfterLoad is null)
            throw new ArgumentException("policy boundary fork points must carry explicit post-load interventions");
        int[] absoluteHorizons = horizons.Select(horizon => checked(seed.NextStep + horizon)).ToArray();
        PolicyBoundaryRational forcedBoundary = candidateBoundary >= baselineBoundary
            ? new(candidateBoundary.Numerator + candidateBoundary.Denominator, candidateBoundary.Denominator)
            : new(baselineBoundary.Numerator + baselineBoundary.Denominator, baselineBoundary.Denominator);
        CortexForkArm<PolicyBoundaryTrialOutcome>[][] armLadder = new CortexForkArm<PolicyBoundaryTrialOutcome>[horizons.Length][];
        for (int i = 0; i < horizons.Length; i++)
            armLadder[i] = [WrapArm(baselineArms[i], PolicyBoundaryArms.Baseline, baselineBoundary),
                WrapArm(candidateArms[i], PolicyBoundaryArms.Candidate, candidateBoundary),
                WrapArm(forcedNullArms[i], PolicyBoundaryArms.ForcedDivergentNull, forcedBoundary),
                WrapArm(reflexArms[i], PolicyBoundaryArms.ReflexFrozenControl, baselineBoundary)];
        // Inter-rung adaptive kill: after each rung lands, judge only the verdict conjuncts that are
        // AND-folded over every row and so become monotonically unrecoverable the instant one rung
        // breaks them (continuity, matched-spend, child-completion, baseline-no-worse — the rest are
        // terminal-only and undecidable here). A break before the terminal rung means no later rung can
        // restore the terminal verdict; it is already decided FAIL, so we stop the ladder exactly as the
        // final Validate would, skipping the remaining rungs' step-execution and checkpoint cycles (the
        // ~80%-of-wall doomed-trial burn). The frozen-control rails throw here as invariant violations,
        // never a kill. Only the newest rung's four rows need judging: prior rungs already passed this
        // gate on their own landing, so continuity/child-completion carry forward and matched-spend /
        // baseline-no-worse are per-horizon.
        List<CortexMatchedForkNReceipt<PolicyBoundaryTrialOutcome>> ladder = CortexForkRunner.RunMatchedForkNLadder(
            spawning, seed, armLadder, absoluteHorizons, verifyEveryTerminal: true,
            inspectAfterRung: landed =>
            {
                int rung = landed.Count - 1;
                PolicyBoundaryArmReceipt[] rungRows = new PolicyBoundaryArmReceipt[4];
                BuildPolicyBoundaryArmRows(rungRows, 0, horizons[rung], landed[rung], domain);
                PolicyBoundaryVerdict rungVerdict = PolicyBoundaryVerdict.Compute(rungRows, [horizons[rung]]);
                if (!rungVerdict.ReflexGrammarFrozen) throw new InvalidDataException("reflex frozen control executed policy grammar");
                if (!rungVerdict.ReflexAdaptationFrozen) throw new InvalidDataException("reflex frozen control recorded adaptation transitions or remained enabled");
                if (rung < horizons.Length - 1 && rungVerdict.MonotoneFatal)
                {
                    Trace.Cortex.Boundary("policy.boundary.trial-kill",
                        $"obligation={obligation} rung={rung} horizon={horizons[rung]} continuity={(rungVerdict.ContinuityExact ? 1 : 0)} matched-spend={(rungVerdict.MatchedSpend ? 1 : 0)} child-completed={(rungVerdict.AllChildrenCompleted ? 1 : 0)} baseline-no-worse={(rungVerdict.BaselineNoWorse ? 1 : 0)} skipped-rungs={horizons.Length - 1 - rung}");
                    throw new InvalidDataException("policy boundary trial doomed: a monotone-fatal verdict conjunct broke before the terminal rung");
                }
            });
        PolicyBoundaryArmReceipt[] rows = new PolicyBoundaryArmReceipt[horizons.Length * 4];
        for (int i = 0; i < horizons.Length; i++)
            BuildPolicyBoundaryArmRows(rows, i * 4, horizons[i], ladder[i], domain);
        PolicyBoundaryVerdict verdict = PolicyBoundaryVerdict.Compute(rows, horizons);
        // A broken frozen-control is an invariant violation, not a verdict: the reflex rail must
        // NEVER execute grammar or adapt, so it throws here rather than folding into Verified.
        if (!verdict.ReflexGrammarFrozen) throw new InvalidDataException("reflex frozen control executed policy grammar");
        if (!verdict.ReflexAdaptationFrozen) throw new InvalidDataException("reflex frozen control recorded adaptation transitions or remained enabled");
        PolicyBoundaryForkReceipt receipt = new(obligation, baselineBoundary, candidateBoundary, [.. horizons], rows,
            verdict.ContinuityExact, verdict.MatchedSpend, verdict.ForcedNullBehaviorExecuted, verdict.Verified,
            sourceDecisionReadoutFingerprint, sourceDecisionReadoutRevision)
        {
            SourceDecisionCandidateFingerprint = sourceDecisionCandidateFingerprint,
        };
        receipt.Validate(domain);
        return receipt;

        CortexForkArm<PolicyBoundaryTrialOutcome> WrapArm(
            CortexForkArm<PolicyBoundaryTrialOutcome> arm, PolicyBoundaryArms kind, PolicyBoundaryRational boundary)
            => new(arm.RunDirectory, arm.CreateCortex, arm.ReadCompletion, arm.InterveneAfterLoad,
                arm.CompletionMode, arm.IsCompletionSatisfied, arm.AnytimeIdentity,
                arm.RailRole, (trial, window) =>
                {
                    trial.SetPolicyBoundaryTrialOverride(identity.Policy, obligation, kind,
                        ushort.Parse(identity.Feature, CultureInfo.InvariantCulture), boundary);
                    arm.AfterRuntimeBind?.Invoke(trial, window);
                }, arm.ParentRunID, arm.AfterCompletedStep,
                arm.AfterCompletedStepEveryStep, arm.AfterRunLanded, arm.BeforeCompletedStep,
                arm.MaterializationContract, arm.PersistCompletionBeforeLanding);
    }

    /// Fold one landed rung's four arm outcomes into their receipt rows at rows[baseIndex .. baseIndex+3].
    /// Shared by the terminal row assembly and the inter-rung kill inspector so the kill's verdict can
    /// never drift from the verdict the final receipt is judged by.
    private static void BuildPolicyBoundaryArmRows(
        PolicyBoundaryArmReceipt[] rows, int baseIndex, int horizon,
        CortexMatchedForkNReceipt<PolicyBoundaryTrialOutcome> fork,
        IPolicyBoundaryDomain domain)
    {
        PolicyBoundaryTrialOutcome baseline = fork.Arms[0].Outcome;
        PolicyBoundaryTrialOutcome candidate = fork.Arms[1].Outcome;
        PolicyBoundaryTrialOutcome nullOutcome = fork.Arms[2].Outcome;
        PolicyBoundaryTrialOutcome reflex = fork.Arms[3].Outcome;
        baseline.Validate(domain); candidate.Validate(domain); nullOutcome.Validate(domain); reflex.Validate(domain);
        if (!baseline.AdaptationEnabled || !candidate.AdaptationEnabled || !nullOutcome.AdaptationEnabled || reflex.AdaptationEnabled)
            throw new InvalidDataException("policy boundary arms reported adaptation state inconsistent with their intervention rails");
        bool continuity = fork.IsExact;
        long baselineSpend = fork.Arms[0].StepSpan.ActualSteps;
        long candidateSpend = fork.Arms[1].StepSpan.ActualSteps;
        long nullSpend = fork.Arms[2].StepSpan.ActualSteps;
        bool baselineChildProcessCompleted = fork.Arms[0].ExitCode == 0;
        bool candidateChildProcessCompleted = fork.Arms[1].ExitCode == 0;
        bool nullDiverged = nullOutcome.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && nullOutcome.GuardAdmittedCount > 0
            && nullOutcome.HasExecutedDecisionIdentity
            && nullOutcome.ExecutedAction != nullOutcome.ExecutedLaunchpadAction
            && nullOutcome.ExecutedAction != nullOutcome.ExecutedRawCandidateAction;
        bool nullChildProcessCompleted = fork.Arms[2].ExitCode == 0;
        long reflexSpend = fork.Arms[3].StepSpan.ActualSteps;
        rows[baseIndex] = new PolicyBoundaryArmReceipt(PolicyBoundaryArms.Baseline, horizon, baseline.PaidCloseDelta, baselineSpend, continuity, baselineChildProcessCompleted, baseline.GrammarExecutionsDelta, baseline.TrialAdaptationTransitions, baseline.AdaptationEnabled)
        {
            ExecutionOutcome = baseline.ExecutionOutcome,
            RequestCount = baseline.RequestCount,
            GuardAdmittedCount = baseline.GuardAdmittedCount,
            LastRequestDecisionID = baseline.LastRequestDecisionID,
            LastRequestStep = baseline.LastRequestStep,
            LastRequestReadout = baseline.LastRequestReadout,
            ExecutedDecisionID = baseline.ExecutedDecisionID,
            ExecutedStep = baseline.ExecutedStep,
            ExecutedLaunchpadAction = baseline.ExecutedLaunchpadAction,
            ExecutedRawCandidateAction = baseline.ExecutedRawCandidateAction,
            ExecutedSelectedCandidateAction = baseline.ExecutedSelectedCandidateAction,
            ExecutedAction = baseline.ExecutedAction,
            ExecutedAuthority = baseline.ExecutedAuthority,
            ExecutedSelectionCause = baseline.ExecutedSelectionCause,
            ExecutedReadoutFingerprint = baseline.ExecutedReadoutFingerprint,
            ExecutedReadoutRevision = baseline.ExecutedReadoutRevision,
            ExecutedReadoutOccurrenceDigest = baseline.ExecutedReadoutOccurrenceDigest,
            ExecutedCandidateFingerprint = baseline.ExecutedCandidateFingerprint,
            ExecutedCanonicalState = baseline.ExecutedCanonicalState,
            ExecutedDecisionEventID = baseline.ExecutedDecisionEventID,
            ExecutedOutcomeEventID = baseline.ExecutedOutcomeEventID,
            ExecutedOutcomePayloadSHA256 = baseline.ExecutedOutcomePayloadSHA256,
            ForcedDivergenceSeed = baseline.ForcedDivergenceSeed,
            ContinuityExact = continuity,
        };
        rows[baseIndex + 1] = new PolicyBoundaryArmReceipt(PolicyBoundaryArms.Candidate, horizon, candidate.PaidCloseDelta, candidateSpend, continuity, candidateChildProcessCompleted, candidate.GrammarExecutionsDelta, candidate.TrialAdaptationTransitions, candidate.AdaptationEnabled)
        {
            ExecutionOutcome = candidate.ExecutionOutcome,
            RequestCount = candidate.RequestCount,
            GuardAdmittedCount = candidate.GuardAdmittedCount,
            LastRequestDecisionID = candidate.LastRequestDecisionID,
            LastRequestStep = candidate.LastRequestStep,
            LastRequestReadout = candidate.LastRequestReadout,
            ExecutedDecisionID = candidate.ExecutedDecisionID,
            ExecutedStep = candidate.ExecutedStep,
            ExecutedLaunchpadAction = candidate.ExecutedLaunchpadAction,
            ExecutedRawCandidateAction = candidate.ExecutedRawCandidateAction,
            ExecutedSelectedCandidateAction = candidate.ExecutedSelectedCandidateAction,
            ExecutedAction = candidate.ExecutedAction,
            ExecutedAuthority = candidate.ExecutedAuthority,
            ExecutedSelectionCause = candidate.ExecutedSelectionCause,
            ExecutedReadoutFingerprint = candidate.ExecutedReadoutFingerprint,
            ExecutedReadoutRevision = candidate.ExecutedReadoutRevision,
            ExecutedReadoutOccurrenceDigest = candidate.ExecutedReadoutOccurrenceDigest,
            ExecutedCandidateFingerprint = candidate.ExecutedCandidateFingerprint,
            ExecutedCanonicalState = candidate.ExecutedCanonicalState,
            ExecutedDecisionEventID = candidate.ExecutedDecisionEventID,
            ExecutedOutcomeEventID = candidate.ExecutedOutcomeEventID,
            ExecutedOutcomePayloadSHA256 = candidate.ExecutedOutcomePayloadSHA256,
            ForcedDivergenceSeed = candidate.ForcedDivergenceSeed,
            ContinuityExact = continuity,
        };
        rows[baseIndex + 2] = new PolicyBoundaryArmReceipt(PolicyBoundaryArms.ForcedDivergentNull, horizon, nullOutcome.PaidCloseDelta, nullSpend, continuity, nullChildProcessCompleted, nullOutcome.GrammarExecutionsDelta, nullOutcome.TrialAdaptationTransitions, nullOutcome.AdaptationEnabled)
        {
            ExecutionOutcome = nullOutcome.ExecutionOutcome,
            RequestCount = nullOutcome.RequestCount,
            GuardAdmittedCount = nullOutcome.GuardAdmittedCount,
            LastRequestDecisionID = nullOutcome.LastRequestDecisionID,
            LastRequestStep = nullOutcome.LastRequestStep,
            LastRequestReadout = nullOutcome.LastRequestReadout,
            ExecutedDecisionID = nullOutcome.ExecutedDecisionID,
            ExecutedStep = nullOutcome.ExecutedStep,
            ExecutedLaunchpadAction = nullOutcome.ExecutedLaunchpadAction,
            ExecutedRawCandidateAction = nullOutcome.ExecutedRawCandidateAction,
            ExecutedSelectedCandidateAction = nullOutcome.ExecutedSelectedCandidateAction,
            ExecutedAction = nullOutcome.ExecutedAction,
            ExecutedAuthority = nullOutcome.ExecutedAuthority,
            ExecutedSelectionCause = nullOutcome.ExecutedSelectionCause,
            ExecutedReadoutFingerprint = nullOutcome.ExecutedReadoutFingerprint,
            ExecutedReadoutRevision = nullOutcome.ExecutedReadoutRevision,
            ExecutedReadoutOccurrenceDigest = nullOutcome.ExecutedReadoutOccurrenceDigest,
            ExecutedCandidateFingerprint = nullOutcome.ExecutedCandidateFingerprint,
            ExecutedCanonicalState = nullOutcome.ExecutedCanonicalState,
            ExecutedDecisionEventID = nullOutcome.ExecutedDecisionEventID,
            ExecutedOutcomeEventID = nullOutcome.ExecutedOutcomeEventID,
            ExecutedOutcomePayloadSHA256 = nullOutcome.ExecutedOutcomePayloadSHA256,
            ForcedDivergenceSeed = nullOutcome.ForcedDivergenceSeed,
            Diverged = nullDiverged,
            ContinuityExact = continuity,
        };
        rows[baseIndex + 3] = new PolicyBoundaryArmReceipt(PolicyBoundaryArms.ReflexFrozenControl, horizon, reflex.PaidCloseDelta, reflexSpend, continuity, fork.Arms[3].ExitCode == 0, reflex.GrammarExecutionsDelta, reflex.TrialAdaptationTransitions, reflex.AdaptationEnabled)
        {
            ExecutionOutcome = reflex.ExecutionOutcome,
            RequestCount = reflex.RequestCount,
            GuardAdmittedCount = reflex.GuardAdmittedCount,
            LastRequestDecisionID = reflex.LastRequestDecisionID,
            LastRequestStep = reflex.LastRequestStep,
            LastRequestReadout = reflex.LastRequestReadout,
            ExecutedDecisionID = reflex.ExecutedDecisionID,
            ExecutedStep = reflex.ExecutedStep,
            ExecutedLaunchpadAction = reflex.ExecutedLaunchpadAction,
            ExecutedRawCandidateAction = reflex.ExecutedRawCandidateAction,
            ExecutedSelectedCandidateAction = reflex.ExecutedSelectedCandidateAction,
            ExecutedAction = reflex.ExecutedAction,
            ExecutedAuthority = reflex.ExecutedAuthority,
            ExecutedSelectionCause = reflex.ExecutedSelectionCause,
            ExecutedReadoutFingerprint = reflex.ExecutedReadoutFingerprint,
            ExecutedReadoutRevision = reflex.ExecutedReadoutRevision,
            ExecutedReadoutOccurrenceDigest = reflex.ExecutedReadoutOccurrenceDigest,
            ExecutedCandidateFingerprint = reflex.ExecutedCandidateFingerprint,
            ExecutedCanonicalState = reflex.ExecutedCanonicalState,
            ExecutedDecisionEventID = reflex.ExecutedDecisionEventID,
            ExecutedOutcomeEventID = reflex.ExecutedOutcomeEventID,
            ExecutedOutcomePayloadSHA256 = reflex.ExecutedOutcomePayloadSHA256,
            ForcedDivergenceSeed = reflex.ForcedDivergenceSeed,
            ContinuityExact = continuity,
        };
    }
}

public sealed class PolicyBoundaryObligation
{
    private readonly List<PolicyBoundaryCandidate> _candidates = [];
    private PolicyBoundaryCandidate? _winner;
    private PolicyBoundaryForkReceipt? _receipt;
    // A corroboration is staged outside the durable receipt until divergence adjudication
    // and authority selection succeed together. Checkpointing this object during
    // the crash window must never produce Receipt.HasValue without a winner.
    private PolicyBoundaryForkReceipt? _stagedReceipt;
    private string _receiptDigest = string.Empty;

    public PolicyBoundaryObligation(PolicyBoundaryIdentity identity)
    {
        identity.Validate();
        Identity = identity;
        ID = identity.ObligationID;
    }

    public PolicyBoundaryIdentity Identity { get; }
    public PolicyBoundaryObligationID ID { get; }
    public IReadOnlyList<PolicyBoundaryCandidate> Candidates => _candidates;
    public PolicyBoundaryCandidate? Winner => _winner;
    internal PolicyBoundaryForkReceipt? Receipt => _receipt;
    internal PolicyBoundaryForkReceipt? StagedReceipt => _stagedReceipt;

    internal void AttachExecutionCorroboration(in PaidDivergenceExecutionCorroboration corroboration, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        EnsureDomainOwnsIdentity(domain);
        if (_receipt is not PolicyBoundaryForkReceipt receipt)
            throw new InvalidDataException("policy boundary execution corroboration has no mounted fork receipt");
        if (receipt.ExecutionCorroboration is not null || _stagedReceipt is not null)
            throw new InvalidDataException("policy boundary execution corroboration was attached more than once");
        receipt = receipt with { ExecutionCorroboration = corroboration };
        receipt.Validate(domain);
        _stagedReceipt = receipt;
    }

    /// Stage the execution corroboration on an unmounted fork receipt.  The ordinary
    /// receipt is not durable authority until divergence adjudication succeeds.
    internal void AttachExecutionCorroboration(in PolicyBoundaryForkReceipt source, in PaidDivergenceExecutionCorroboration corroboration, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        EnsureDomainOwnsIdentity(domain);
        if (_receipt is not null || _stagedReceipt is not null)
            throw new InvalidDataException("policy boundary execution corroboration was attached more than once");
        source.Validate(domain);
        PolicyBoundaryForkReceipt receipt = source with { ExecutionCorroboration = corroboration };
        receipt.Validate(domain);
        _stagedReceipt = receipt;
    }

    internal void DiscardStagedExecutionCorroboration()
    {
        _stagedReceipt = null;
    }

    internal void MountVerifiedTrainingReceipt(in PolicyBoundaryForkReceipt receipt, PolicyBoundaryComparisons comparison, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        EnsureDomainOwnsIdentity(domain);
        receipt.Validate(domain);
        if (receipt.Obligation != ID) throw new InvalidDataException("mounted policy boundary receipt addresses a different obligation");
        if (!Enum.IsDefined(comparison) || comparison == PolicyBoundaryComparisons.Unknown)
            throw new InvalidDataException("mounted policy boundary comparison is unsupported");
        string receiptDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        PolicyBoundaryCandidate candidate = new(receipt.CandidateBoundary, comparison, "verified-training:" + receiptDigest);
        Propose(candidate);
        _winner = candidate;
        _receipt = receipt;
        _receiptDigest = receiptDigest;
    }

    public void Propose(PolicyBoundaryCandidate candidate)
    {
        if (!Enum.IsDefined(candidate.Comparison) || candidate.Comparison == PolicyBoundaryComparisons.Unknown || string.IsNullOrWhiteSpace(candidate.Provenance))
            throw new ArgumentException("policy boundary candidate requires comparison and provenance", nameof(candidate));
        if (_candidates.Contains(candidate)) return;
        _candidates.Add(candidate);
        _candidates.Sort(static (left, right) => left.Boundary.CompareTo(right.Boundary));
    }

    public void ProposeObservedStatistics(ReadOnlySpan<double> statistics, string provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        if (statistics.Length == 0) throw new ArgumentException("at least one observed statistic is required", nameof(statistics));
        for (int i = 0; i < statistics.Length; i++)
        {
            if (!double.IsFinite(statistics[i])) continue;
            Propose(new PolicyBoundaryCandidate(PolicyBoundaryRational.FromDouble(statistics[i]),
                PolicyBoundaryComparisons.LessThanOrEqual, provenance));
        }
        if (_candidates.Count == 0) throw new InvalidDataException("observed statistics contained no finite boundary candidates");
    }

    internal void Select(PolicyBoundaryForkReceipt receipt, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        EnsureDomainOwnsIdentity(domain);
        if (receipt.Obligation != ID) throw new InvalidDataException("policy boundary receipt addresses a different obligation");
        receipt.Validate(domain);
        PolicyBoundaryCandidate winner = _candidates.FirstOrDefault(x => x.Boundary == receipt.CandidateBoundary);
        if (winner == default && !_candidates.Any(x => x.Boundary == receipt.CandidateBoundary))
            throw new InvalidDataException("policy boundary receipt selects an unproposed candidate");
        _winner = winner;
        _receipt = receipt;
        _stagedReceipt = null;
        _receiptDigest = ComputeReceiptDigest(in receipt);
    }

    private void EnsureDomainOwnsIdentity(IPolicyBoundaryDomain domain)
    {
        if (!domain.PolicyID.Equals(Identity.Policy))
            throw new InvalidDataException($"policy boundary obligation '{ID}' belongs to '{Identity.Policy}', not domain '{domain.PolicyID}'");
    }

    public bool TryReadGuard(ReadOnlySpan<MetricSample> features, out PolicyBoundaryReadout readout)
    {
        if (_winner is not PolicyBoundaryCandidate winner || _receipt is not PolicyBoundaryForkReceipt receipt)
        {
            readout = default;
            return false;
        }
        int featureID = int.TryParse(Identity.Feature, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
        double observed = double.NaN;
        for (int i = 0; i < features.Length; i++)
            if (features[i].MetricID.Value == featureID)
            {
                observed = features[i].Value.Kind switch
                {
                    NumericKinds.F64 => features[i].Value.GetF64(),
                    NumericKinds.I64 => features[i].Value.GetI64(),
                    NumericKinds.U64 => features[i].Value.GetU64(),
                    _ => double.NaN,
                };
                break;
            }
        bool guard = double.IsFinite(observed) && winner.Allows(observed);
        readout = new PolicyBoundaryReadout(ID, winner.Boundary, winner.Comparison, receipt.Verified, guard, _receiptDigest);
        return true;
    }

    public PolicyBoundaryReadout Readout(double observed)
    {
        if (_winner is not PolicyBoundaryCandidate winner || _receipt is not PolicyBoundaryForkReceipt receipt)
            return new PolicyBoundaryReadout(ID, default, default, false, false, string.Empty);
        return new PolicyBoundaryReadout(ID, winner.Boundary, winner.Comparison, receipt.Verified, winner.Allows(observed), _receiptDigest);
    }

    internal void Save(CkptWriter writer)
    {
        writer.Str(ID.Value);
        writer.Str(Identity.Policy.Value); writer.Str(Identity.Candidate); writer.Str(Identity.Grammar);
        writer.Str(Identity.Production); writer.Str(Identity.Feature); writer.Str(Identity.Statistic);
        writer.I32(_candidates.Count);
        foreach (PolicyBoundaryCandidate candidate in _candidates)
        {
            writer.Str(candidate.Boundary.ToString()); writer.U8((byte)candidate.Comparison); writer.Str(candidate.Provenance);
        }
        writer.Bool(_winner.HasValue);
        if (_winner is PolicyBoundaryCandidate winner)
        {
            writer.Str(winner.Boundary.ToString()); writer.U8((byte)winner.Comparison); writer.Str(winner.Provenance);
        }
        writer.Bool(_receipt.HasValue);
        if (_receipt is PolicyBoundaryForkReceipt receipt) SaveReceipt(writer, in receipt);
    }

    internal static PolicyBoundaryObligation Load(CkptReader reader, Func<CortexPolicyID, IPolicyBoundaryDomain> resolveDomain, bool readTeacherCorroboration = true, bool readExecutedDecision = true, bool readFundingDecision = true, bool readExecutionCorroboration = true, bool readExecutedStep = true, bool readExecutionAccounting = true, bool readSplitIdentity = true, bool readForcedCustody = true, bool readCanonicalScope = true, bool legacyExecutionOutcome = false, bool readDivergence = true)
    {
        ArgumentNullException.ThrowIfNull(resolveDomain);
        PolicyBoundaryObligationID id = new(reader.Str());
        PolicyBoundaryIdentity identity = new(new CortexPolicyID(reader.Str()), reader.Str(), reader.Str(), reader.Str(), reader.Str(), reader.Str());
        IPolicyBoundaryDomain domain = resolveDomain(identity.Policy)
            ?? throw new InvalidDataException($"no policy-boundary domain is registered for {identity.Policy}");
        PolicyBoundaryObligation obligation = new(identity);
        if (obligation.ID != id) throw new InvalidDataException("policy boundary obligation identity hash mismatch");
        int count = reader.I32();
        if (count < 0 || count > 1024) throw new InvalidDataException("invalid policy boundary candidate count");
        for (int i = 0; i < count; i++) obligation.Propose(new PolicyBoundaryCandidate(
            PolicyBoundaryRational.Parse(reader.Str()), (PolicyBoundaryComparisons)reader.U8(), reader.Str()));
        if (reader.Bool()) obligation._winner = new PolicyBoundaryCandidate(
            PolicyBoundaryRational.Parse(reader.Str()), (PolicyBoundaryComparisons)reader.U8(), reader.Str());
        if (reader.Bool())
        {
            if (!readSplitIdentity)
                throw new InvalidDataException("policy boundary checkpoint predates split readout/candidate identity custody; replay from a v9 checkpoint");
            PolicyBoundaryForkReceipt receipt = ReadReceipt(reader, readTeacherCorroboration, readExecutedDecision, readFundingDecision, readExecutionCorroboration, readExecutedStep, readExecutionAccounting, readForcedCustody, readCanonicalScope, legacyExecutionOutcome, readDivergence);
            if (!readCanonicalScope && receipt.Arms.Any(static arm => arm.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted))
                throw new InvalidDataException("policy boundary checkpoint predates canonical executed scope custody; replay from a v12 checkpoint");
            if (!readFundingDecision)
                throw new InvalidDataException("policy boundary checkpoint producer predates funding receipt custody; replay from a v5 checkpoint");
            if (!readExecutedDecision)
                throw new InvalidDataException("policy boundary checkpoint producer predates executed child decision custody; replay from a v4 checkpoint");
            receipt.Validate(domain);
            if (obligation._winner is not PolicyBoundaryCandidate winner || winner.Boundary != receipt.CandidateBoundary)
                throw new InvalidDataException("policy boundary checkpoint winner does not match its receipt");
            obligation._receipt = receipt;
            obligation._receiptDigest = ComputeReceiptDigest(in receipt);
        }
        return obligation;
    }

    internal static string ComputeReceiptDigest(in PolicyBoundaryForkReceipt receipt)
    {
        StringBuilder text = new(receipt.Obligation.Value);
        text.Append('|').Append(receipt.BaselineBoundary).Append('|').Append(receipt.CandidateBoundary);
        text.Append('|').Append(receipt.QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture));
        text.Append('|').Append(receipt.SourceDecisionReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture));
        text.Append('|').Append(receipt.SourceDecisionCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture));
        text.Append('|').Append(receipt.SourceDecisionReadoutRevision.ToString(CultureInfo.InvariantCulture));
        foreach (int horizon in receipt.Horizons) text.Append('|').Append(horizon);
        foreach (PolicyBoundaryArmReceipt arm in receipt.Arms)
            text.Append('|').Append(arm.Arm).Append('|').Append(arm.Horizon).Append('|').Append(arm.PaidCloseDelta).Append('|').Append(arm.MatchedSpend).Append('|').Append(arm.ContinuityExact ? '1' : '0').Append('|').Append(arm.ChildProcessCompleted ? '1' : '0').Append('|').Append(arm.GrammarExecutionsDelta).Append('|').Append(arm.TrialAdaptationTransitions).Append('|').Append(arm.AdaptationEnabled ? '1' : '0')
                .Append('|').Append(arm.ExecutionOutcome).Append('|').Append(arm.RequestCount).Append('|').Append(arm.GuardAdmittedCount)
                .Append('|').Append(arm.LastRequestDecisionID.Value).Append('|').Append(arm.LastRequestStep)
                .Append('|').Append(arm.LastRequestReadout.LaunchpadAction).Append('|').Append(arm.LastRequestReadout.RawCandidateAction).Append('|').Append(arm.LastRequestReadout.SelectedCandidateAction)
                .Append('|').Append(arm.LastRequestReadout.ExecutedAction).Append('|').Append(arm.LastRequestReadout.Authority).Append('|').Append(arm.LastRequestReadout.GrammarRevision.Value)
                .Append('|').Append(arm.LastRequestReadout.SelectionCause).Append('|').Append(arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest).Append('|').Append(arm.LastRequestReadout.ReadoutCandidateFingerprint)
                .Append('|').Append(arm.ExecutedDecisionID.Value).Append('|').Append(arm.ExecutedStep).Append('|').Append(arm.ExecutedLaunchpadAction).Append('|').Append(arm.ExecutedRawCandidateAction).Append('|').Append(arm.ExecutedSelectedCandidateAction)
                .Append('|').Append(arm.ExecutedAction).Append('|').Append(arm.ExecutedAuthority).Append('|').Append(arm.ExecutedSelectionCause)
                .Append('|').Append(arm.ExecutedReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture)).Append('|').Append(arm.ExecutedReadoutRevision)
                .Append('|').Append(arm.ExecutedReadoutOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture)).Append('|').Append(arm.ExecutedCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture))
                .Append('|').Append(arm.ExecutedCanonicalState.Policy.Value).Append('|').Append((byte)arm.ExecutedCanonicalState.Kind)
                .Append('|').Append(arm.ExecutedCanonicalState.Version).Append('|').Append(arm.ExecutedCanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture))
                .Append('|').Append(arm.ExecutedDecisionEventID.Value).Append('|').Append(arm.ExecutedOutcomeEventID.Value).Append('|').Append(arm.ExecutedOutcomePayloadSHA256).Append('|').Append(arm.ForcedDivergenceSeed.ToString("X16", CultureInfo.InvariantCulture)).Append('|').Append(arm.Diverged ? '1' : '0');
        text.Append('|').Append(receipt.TeacherCorroboration?.Canonical() ?? "none");
        if (receipt.ExecutionCorroboration is PaidDivergenceExecutionCorroboration execution)
            text.Append('|').Append(execution.PaidDivergenceExecutionCorroborationSHA256.Value)
                .Append('|').Append(execution.ReadoutTrainingCorroborationSHA256.Value)
                .Append('|').Append(execution.QuotaDecisionID.Value)
                .Append('|').Append(execution.QuotaReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture))
                .Append('|').Append(execution.QuotaCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture))
                .Append('|').Append(execution.FundingCandidateRevision.Value)
                .Append('|').Append(execution.ForkArmSHA256.Value)
                .Append('|').Append(execution.ChildExecutionReceiptSHA256.Value)
                .Append('|').Append(execution.ExecutedDivergenceDecisionID.Value)
                .Append('|').Append(execution.ExecutedDivergenceOutcomeID.Value)
                .Append('|').Append(execution.ExecutedDivergenceOutcomeEventID.Value)
                .Append('|').Append(execution.ExecutedDivergenceOutcomePayloadSHA256);
        else text.Append("|none");
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    private static void SaveReceipt(CkptWriter writer, in PolicyBoundaryForkReceipt receipt)
    {
        writer.Str(receipt.Obligation.Value); writer.Str(receipt.BaselineBoundary.ToString()); writer.Str(receipt.CandidateBoundary.ToString());
        writer.U64(receipt.QuotaDecisionID.Value);
        writer.U64(receipt.SourceDecisionReadoutFingerprint); writer.U64(receipt.SourceDecisionCandidateFingerprint); writer.U64(receipt.SourceDecisionReadoutRevision);
        writer.I32(receipt.Horizons.Length); foreach (int horizon in receipt.Horizons) writer.I32(horizon);
        writer.I32(receipt.Arms.Length); foreach (PolicyBoundaryArmReceipt arm in receipt.Arms)
        { writer.U8((byte)arm.Arm); writer.I32(arm.Horizon); writer.I64(arm.PaidCloseDelta); writer.I64(arm.MatchedSpend); writer.Bool(arm.ContinuityExact); writer.Bool(arm.ChildProcessCompleted); writer.I64(arm.GrammarExecutionsDelta); writer.I64(arm.TrialAdaptationTransitions); writer.Bool(arm.AdaptationEnabled); writer.U8((byte)arm.ExecutionOutcome); writer.I64(arm.RequestCount); writer.I64(arm.GuardAdmittedCount); writer.U64(arm.LastRequestDecisionID.Value); if (arm.LastRequestDecisionID.Value != 0) { writer.I32(arm.LastRequestStep); writer.I32(arm.LastRequestReadout.LaunchpadAction); writer.I32(arm.LastRequestReadout.RawCandidateAction); writer.I32(arm.LastRequestReadout.SelectedCandidateAction); writer.I32(arm.LastRequestReadout.ExecutedAction); writer.U8((byte)arm.LastRequestReadout.Authority); writer.U64(arm.LastRequestReadout.GrammarRevision.Value); writer.U8((byte)arm.LastRequestReadout.SelectionCause); writer.U64(arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest); writer.U64(arm.LastRequestReadout.ReadoutCandidateFingerprint); } writer.U64(arm.ExecutedDecisionID.Value); writer.I32(arm.ExecutedStep); writer.I32(arm.ExecutedLaunchpadAction); writer.I32(arm.ExecutedRawCandidateAction); writer.I32(arm.ExecutedSelectedCandidateAction); writer.I32(arm.ExecutedAction); writer.U8((byte)arm.ExecutedAuthority); writer.U8((byte)arm.ExecutedSelectionCause); writer.U64(arm.ExecutedReadoutFingerprint); writer.U64(arm.ExecutedReadoutRevision); writer.U64(arm.ExecutedReadoutOccurrenceDigest); writer.U64(arm.ExecutedCandidateFingerprint); writer.Str(arm.ExecutedCanonicalState.Version == 0 ? "" : arm.ExecutedCanonicalState.Policy.Value); writer.U8((byte)arm.ExecutedCanonicalState.Kind); writer.U16(arm.ExecutedCanonicalState.Version); writer.U64(arm.ExecutedCanonicalState.Value); writer.I64(arm.ExecutedDecisionEventID.Value); writer.I64(arm.ExecutedOutcomeEventID.Value); writer.Str(arm.ExecutedOutcomePayloadSHA256); writer.U64(arm.ForcedDivergenceSeed); writer.Bool(arm.Diverged); }
        writer.Bool(receipt.ContinuityExact); writer.Bool(receipt.MatchedSpend); writer.Bool(receipt.ForcedNullBehaviorExecuted); writer.Bool(receipt.Verified);
        writer.Bool(receipt.TeacherCorroboration is not null);
        if (receipt.TeacherCorroboration is PolicyBoundaryTeacherCorroboration teacher)
        {
            writer.I32(teacher.TeacherEventIDs.Count);
            foreach (TapeEventID eventID in teacher.TeacherEventIDs) writer.I64(eventID.Value);
            writer.Str(teacher.EvidenceSHA256); writer.Str(teacher.FoldNodeID.Value); writer.U64(teacher.FoldRevision.Value); writer.U64(teacher.TeacherRevision.Value);
        }
        writer.Bool(receipt.ExecutionCorroboration is not null);
        if (receipt.ExecutionCorroboration is PaidDivergenceExecutionCorroboration execution)
        {
            writer.Str(execution.ReadoutTrainingCorroborationSHA256.Value); writer.U64(execution.QuotaDecisionID.Value); writer.U64(execution.QuotaReadoutFingerprint);
            writer.U64(execution.QuotaCandidateFingerprint); writer.U64(execution.FundingCandidateRevision.Value);
            writer.Str(execution.ForkArmSHA256.Value); writer.Str(execution.ChildExecutionReceiptSHA256.Value);
            writer.U64(execution.ExecutedDivergenceDecisionID.Value); writer.Str(execution.ExecutedDivergenceOutcomeID.Value);
            writer.Str(execution.PaidDivergenceExecutionCorroborationSHA256.Value);
            writer.I64(execution.ExecutedDivergenceOutcomeEventID.Value); writer.Str(execution.ExecutedDivergenceOutcomePayloadSHA256);
        }
    }

    private static PolicyCanonicalStateID ReadCanonicalState(CkptReader reader)
    {
        string policy = reader.Str();
        PolicyCanonicalStateKinds kind = (PolicyCanonicalStateKinds)reader.U8();
        ushort version = reader.U16();
        ulong value = reader.U64();
        if (version == 0)
        {
            if (policy.Length != 0 || kind != 0 || value != 0)
                throw new InvalidDataException("policy boundary checkpoint carries malformed empty canonical scope");
            return default;
        }
        return new PolicyCanonicalStateID(new CortexPolicyID(policy), kind, version, value);
    }

    private static PolicyBoundaryForkReceipt ReadReceipt(CkptReader reader, bool readTeacherCorroboration = true, bool readExecutedDecision = true, bool readFundingDecision = true, bool readExecutionCorroboration = true, bool readExecutedStep = true, bool readExecutionAccounting = true, bool readForcedCustody = true, bool readCanonicalScope = true, bool legacyExecutionOutcome = false, bool readDivergence = true)
    {
        PolicyBoundaryObligationID id = new(reader.Str());
        PolicyBoundaryRational baseline = PolicyBoundaryRational.Parse(reader.Str());
        PolicyBoundaryRational candidate = PolicyBoundaryRational.Parse(reader.Str());
        CortexPolicyQuotaDecisionID fundingDecisionID = readFundingDecision ? new(reader.U64()) : default;
        ulong sourceFingerprint = reader.U64();
        ulong sourceCandidateFingerprint = reader.U64();
        ulong sourceRevision = reader.U64();
        int horizonCount = reader.I32();
        if (horizonCount <= 0 || horizonCount > 16) throw new InvalidDataException("invalid policy boundary horizon count");
        int[] horizons = new int[horizonCount]; for (int i = 0; i < horizonCount; i++) horizons[i] = reader.I32();
        int armCount = reader.I32(); if (armCount != horizonCount * 4) throw new InvalidDataException("invalid policy boundary arm count");
        PolicyBoundaryArmReceipt[] arms = new PolicyBoundaryArmReceipt[armCount];
        for (int i = 0; i < armCount; i++)
        {
            PolicyBoundaryArmReceipt arm = new((PolicyBoundaryArms)reader.U8(), reader.I32(), reader.I64(), reader.I64(), reader.Bool(), reader.Bool(), reader.I64(), reader.I64(), reader.Bool());
            if (readExecutionAccounting)
            {
                byte encodedOutcome = reader.U8();
                arm = arm with
                {
                    ExecutionOutcome = legacyExecutionOutcome
                        ? encodedOutcome switch
                        {
                            0 => CortexPolicyTrialExecutionOutcomes.GuardDenied,
                            1 => CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted,
                            _ => (CortexPolicyTrialExecutionOutcomes)encodedOutcome,
                        }
                        : (CortexPolicyTrialExecutionOutcomes)encodedOutcome,
                    RequestCount = reader.I64(),
                    GuardAdmittedCount = reader.I64(),
                    LastRequestDecisionID = new(reader.U64()),
                };
            }
            if (readExecutionAccounting && arm.LastRequestDecisionID.Value != 0)
                arm = arm with
                {
                    LastRequestStep = reader.I32(),
                    LastRequestReadout = new(
                        reader.I32(), reader.I32(), reader.I32(), reader.I32(), (CortexPolicyAuthorities)reader.U8(),
                        new GrammarRevisionID(reader.U64()), (CortexPolicySelectionCauses)reader.U8(), reader.U64(), reader.U64()),
                };
            if (readExecutedDecision)
                arm = arm with
                {
                    ExecutedDecisionID = new(reader.U64()),
                    ExecutedStep = readExecutedStep ? reader.I32() : -1,
                    ExecutedLaunchpadAction = reader.I32(),
                    ExecutedRawCandidateAction = reader.I32(),
                    ExecutedSelectedCandidateAction = reader.I32(),
                    ExecutedAction = reader.I32(),
                    ExecutedAuthority = (CortexPolicyAuthorities)reader.U8(),
                    ExecutedSelectionCause = (CortexPolicySelectionCauses)reader.U8(),
                    ExecutedReadoutFingerprint = reader.U64(),
                    ExecutedReadoutRevision = reader.U64(),
                    ExecutedReadoutOccurrenceDigest = reader.U64(),
                    ExecutedCandidateFingerprint = reader.U64(),
                    ExecutedCanonicalState = readCanonicalScope ? ReadCanonicalState(reader) : default,
                    ExecutedDecisionEventID = readForcedCustody ? new(reader.I64()) : default,
                    ExecutedOutcomeEventID = readForcedCustody ? new(reader.I64()) : default,
                    ExecutedOutcomePayloadSHA256 = readForcedCustody ? reader.Str() : "",
                    ForcedDivergenceSeed = readForcedCustody ? reader.U64() : 0,
                    Diverged = readDivergence && reader.Bool(),
                };
            arms[i] = arm;
        }
        bool continuity = reader.Bool(); bool matchedSpend = reader.Bool(); bool forcedNull = reader.Bool(); bool verified = reader.Bool();
        PolicyBoundaryTeacherCorroboration? teacher = null;
        if (readTeacherCorroboration && reader.Bool())
        {
            int teacherCount = reader.I32();
            if (teacherCount <= 0 || teacherCount > 4096) throw new InvalidDataException("invalid policy boundary teacher event count");
            TapeEventID[] eventIDs = new TapeEventID[teacherCount];
            for (int i = 0; i < teacherCount; i++) eventIDs[i] = new(reader.I64());
            string evidenceSHA256 = reader.Str();
            LoopLineageNodeID foldNodeID = new(reader.Str());
            GrammarRevisionID foldRevision = new(reader.U64());
            GrammarRevisionID teacherRevision = new(readTeacherCorroboration ? reader.U64() : foldRevision.Value);
            teacher = new(eventIDs, evidenceSHA256, foldNodeID, foldRevision, teacherRevision);
        }
        PaidDivergenceExecutionCorroboration? executionCorroboration = null;
        if (readExecutionCorroboration && reader.Bool())
        {
            executionCorroboration = new PaidDivergenceExecutionCorroboration(
                new LoopClosureDigest(reader.Str()), new CortexPolicyQuotaDecisionID(reader.U64()), reader.U64(), reader.U64(),
                new GrammarRevisionID(reader.U64()), new LoopClosureDigest(reader.Str()), new LoopClosureDigest(reader.Str()),
                new CortexPolicyDecisionID(reader.U64()), new LoopClosureDigest(reader.Str()), new LoopClosureDigest(reader.Str()),
                new TapeEventID(reader.I64()), reader.Str());
        }
        return new PolicyBoundaryForkReceipt(id, baseline, candidate, horizons, arms, continuity, matchedSpend, forcedNull, verified, sourceFingerprint, sourceRevision, teacher, executionCorroboration)
        {
            QuotaDecisionID = fundingDecisionID,
            SourceDecisionCandidateFingerprint = sourceCandidateFingerprint,
        };
    }

    internal static bool VerifyCheckpointRoundTripFixture(PolicyBoundaryObligation source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] Encode(PolicyBoundaryObligation value)
        {
            using MemoryStream image = new();
            using CkptWriter writer = new(image);
            value.Save(writer);
            return image.ToArray();
        }

        byte[] first = Encode(source);
        using MemoryStream stream = new(first);
        using CkptReader reader = new(stream);
        PolicyBoundaryObligation restored = Load(reader, static _ => HomeostatPolicyBoundaryDomain.Instance);
        byte[] second = Encode(restored);
        if (!first.AsSpan().SequenceEqual(second)) return false;
        if (restored.Receipt is not PolicyBoundaryForkReceipt receipt) return false;
        receipt.Validate(HomeostatPolicyBoundaryDomain.Instance);
        return receipt.Arms.All(static arm => arm.AdaptationEnabled == (arm.Arm != PolicyBoundaryArms.ReflexFrozenControl));
    }
}
