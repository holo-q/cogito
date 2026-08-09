namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

internal enum EmlRung0Statuses
{
    Composed,
    NoCandidate,
    Exhausted,
    GuardRejected,
}

internal enum EmlRung0AuditStatuses
{
    NotSelected,
    Agreed,
    Disagreed,
}

internal enum EmlRung0AuditSelectionSpecies : byte
{
    DigestCadence,
    MinimumOne,
}

internal enum EmlRung0RuleTransitionKinds
{
    Quarantined,
    Repromoted,
}

internal readonly record struct EmlRung0Budget(
    int MaxDepth,
    int MaxStates,
    int MaxApplications)
{
    public static EmlRung0Budget Default => new(4, 256, 4096);

    public void Validate()
    {
        if (MaxDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        if (MaxStates < 2) throw new ArgumentOutOfRangeException(nameof(MaxStates));
        if (MaxApplications < 1) throw new ArgumentOutOfRangeException(nameof(MaxApplications));
    }

    public string Canonical()
        => string.Concat(
            MaxDepth.ToString(CultureInfo.InvariantCulture), ":",
            MaxStates.ToString(CultureInfo.InvariantCulture), ":",
            MaxApplications.ToString(CultureInfo.InvariantCulture));
}

internal readonly record struct EmlRung0Work(
    int ExpandedStates,
    int VisitedStates,
    int Applications,
    int GuardRejections)
{
    public bool DidWork => ExpandedStates > 0 && Applications > 0;
}

/// One claim-owned evaluation context. Every state in a derivation is evaluated at the same
/// concrete input pair and carries its own guard digest; an RPN reached under another branch
/// context is therefore a distinct search state, not a visited-set collision.
internal readonly record struct EmlRewritePredictionCarrier(
    EmlPredictionID PredictionID,
    string SourceDigest,
    Complex X,
    Complex Y)
{
    public static EmlRewritePredictionCarrier Create(
        EmlPredictionID claimID,
        string sourceDigest,
        Complex x,
        Complex y)
    {
        if (claimID.Value < 0) throw new ArgumentOutOfRangeException(nameof(claimID));
        if (string.IsNullOrEmpty(sourceDigest)) throw new ArgumentException("source digest must be nonempty", nameof(sourceDigest));
        if (!IsFinite(x) || !IsFinite(y)) throw new ArgumentException("rewrite carrier inputs must be finite");
        return new EmlRewritePredictionCarrier(claimID, sourceDigest, x, y);
    }

    public EmlRewriteState CreateState(string rpn)
    {
        EmlTree tree = EmlTree.ParseRPN(rpn);
        EmlTreeEvaluation evaluation = tree.EvaluateAt(X, Y);
        return new EmlRewriteState(rpn, evaluation, CalculateGuardContextDigest(rpn, evaluation));
    }

    private ulong CalculateGuardContextDigest(string rpn, EmlTreeEvaluation evaluation)
    {
        ulong hash = 14695981039346656037UL;
        EmlRung0Digest.HashText(ref hash, PredictionID.Value.ToString(CultureInfo.InvariantCulture));
        EmlRung0Digest.HashText(ref hash, SourceDigest);
        EmlRung0Digest.HashText(ref hash, rpn);
        EmlRung0Digest.HashText(ref hash, X.Real.ToString("R", CultureInfo.InvariantCulture));
        EmlRung0Digest.HashText(ref hash, X.Imaginary.ToString("R", CultureInfo.InvariantCulture));
        EmlRung0Digest.HashText(ref hash, Y.Real.ToString("R", CultureInfo.InvariantCulture));
        EmlRung0Digest.HashText(ref hash, Y.Imaginary.ToString("R", CultureInfo.InvariantCulture));
        List<KeyValuePair<EmlPath, EmlNodeEvaluation>> nodes = new(evaluation.Nodes);
        nodes.Sort(static (left, right) => string.CompareOrdinal(left.Key.Steps, right.Key.Steps));
        for (int i = 0; i < nodes.Count; i++)
        {
            KeyValuePair<EmlPath, EmlNodeEvaluation> row = nodes[i];
            EmlRung0Digest.HashText(ref hash, row.Key.Steps);
            EmlRung0Digest.HashText(ref hash, new EmlTree(row.Value.Node).RenderRPN());
            EmlRung0Digest.HashText(ref hash, EmlEnclosureWitness.FromConcreteProbe(row.Value.P1).Canonical());
            EmlPrincipalBranch branch = row.Value.P1.PrincipalBranch;
            EmlRung0Digest.HashText(ref hash, string.Concat(
                branch.LogDefined ? '1' : '0',
                branch.OnNegativeRealCut ? '1' : '0',
                branch.EnclosureCrossesNegativeRealCut ? '1' : '0',
                branch.ExpAfterLogRoundTrips ? '1' : '0',
                branch.LogAfterExpRoundTrips ? '1' : '0', ":",
                branch.ExponentialTurn.ToString(CultureInfo.InvariantCulture)));
        }
        return hash;
    }

    private static bool IsFinite(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
}

internal readonly record struct EmlRewriteState(
    string RPN,
    EmlTreeEvaluation Evaluation,
    ulong GuardContextDigest);

internal readonly record struct EmlRewriteStateKey(string RPN, ulong GuardContextDigest);

internal readonly record struct EmlRung0Proof(
    EmlPredictionID PredictionID,
    string SourceDigest,
    string AntecedentRPN,
    string ConsequentRPN,
    int SearchRevision,
    EmlRung0Budget Budget,
    IReadOnlyList<EmlCompositionStep> Steps,
    EmlRung0Work Work,
    ulong Digest)
{
    public bool IsValidShape
        => PredictionID.Value >= 0
            && !string.IsNullOrEmpty(SourceDigest)
            && !string.IsNullOrEmpty(AntecedentRPN)
            && !string.IsNullOrEmpty(ConsequentRPN)
            && SearchRevision >= 1
            && Budget.MaxDepth >= 1
            && Budget.MaxStates >= 2
            && Budget.MaxApplications >= 1
            && Steps.Count > 0
            && Steps.Count <= Budget.MaxDepth
            && Work.DidWork
            && Work.ExpandedStates >= 0
            && Work.VisitedStates >= 0
            && Work.Applications >= 0
            && Work.GuardRejections >= 0
            && Work.Applications >= Steps.Count
            && Work.ExpandedStates <= Budget.MaxStates
            && Work.VisitedStates <= Budget.MaxStates
            && Work.Applications <= Budget.MaxApplications
            && EmlRung0Digest.IsCanonicalRPN(AntecedentRPN)
            && EmlRung0Digest.IsCanonicalRPN(ConsequentRPN)
            && string.Equals(Steps[0].AntecedentRpn, AntecedentRPN, StringComparison.Ordinal)
            && string.Equals(Steps[^1].ConsequentRpn, ConsequentRPN, StringComparison.Ordinal)
            && EmlRung0Digest.HasPortableStepChain(this)
            && EmlRung0Digest.Calculate(this with { Digest = 0 }) == Digest;
}

internal readonly record struct EmlRung0Result(
    EmlRung0Statuses Status,
    EmlRung0Proof? Proof,
    EmlRung0Work Work)
{
    public bool Composed => Status == EmlRung0Statuses.Composed && Proof is not null;
}

internal readonly record struct EmlRung0NullExecution(
    string StartRPN,
    string TerminalRPN,
    EmlRung0Budget Budget,
    EmlRung0Work Work,
    EmlRuleID RuleID,
    int AuthoritativeCompositions)
{
    public bool Powered
        => Work.DidWork
            && Work.Applications > 0
            && AuthoritativeCompositions == 0
            && !string.Equals(StartRPN, TerminalRPN, StringComparison.Ordinal);
}

internal readonly record struct EmlRung0Audit(
    ulong ProofDigest,
    EmlRung0AuditStatuses Status,
    long EvaluatorCalls,
    bool NumericVerified,
    bool GuardVerified,
    IReadOnlyList<EmlRuleID> Rules,
    EmlRung0AuditSelectionSpecies Selection = EmlRung0AuditSelectionSpecies.DigestCadence);

internal readonly record struct EmlRung0RuleTransition(
    long Sequence,
    EmlRuleID RuleID,
    EmlRung0RuleTransitionKinds Kind,
    ulong ProofDigest,
    bool NumericVerified,
    bool GuardVerified,
    long EvaluatorCalls);

internal enum EmlComposedFormAdmissionStatuses
{
    Accepted,
    InvalidProof,
    AuditDisagreed,
    SourceMissing,
    SourceNotExact,
    CandidateMatchesSource,
    CandidateAlreadyAdmitted,
    RepresentativeWouldChange,
}

/// The typed turnstiles a rung-0 candidate crosses.  These are receipts, not a second
/// scheduler: they expose the ordinary path that already ran so the loop-closure adjudicator
/// can bind opportunity, eligibility, funding, attempt, and closure without inferring it from
/// aggregate counters.
internal enum EmlRung0FunnelStages
{
    Opportunity,
    Eligibility,
    Funding,
    Attempt,
    Closure,
    RelationNull,
}

internal readonly record struct EmlRung0FunnelReceipt(
    EmlRung0FunnelStages Stage,
    EmlPredictionID ObligationPredictionID,
    string ObligationID,
    EmlRuleID RuleID,
    bool Accepted,
    string Reason,
    string ProofID,
    string AuditID,
    string AdmissionID,
    string ClosureID,
    EmlEvaluatorInterval Evaluation,
    EmlRelationNullDonorProvenance? RelationNullDonor = null,
    EmlRung0AuditSelectionSpecies AuditSelection = EmlRung0AuditSelectionSpecies.DigestCadence)
{
    public bool IsRelationNull => Stage == EmlRung0FunnelStages.RelationNull;
}

internal readonly record struct EmlRelationNullDonorProvenance(
    EmlPredictionID SourcePredictionID,
    string ObligationID,
    IReadOnlyList<TapeEventID> SupportEventIDs,
    IReadOnlyList<string> LawAdmissionIDs);

internal readonly record struct EmlRelationNullDonor(
    EmlLawRewrite Rewrite,
    EmlRelationNullDonorProvenance Provenance);

/// The exact claim admitted by the rung-0 turnstile.  The guard package keeps its complete
/// canonical witness chain in addition to the legacy terminal digest: a comparator must receive
/// this descriptor, not just the two RPN strings, or it can silently grade a different claim.
internal readonly record struct EmlRung0AdmissionPath(
    EmlPredictionID ObligationPredictionID,
    string LhsRPN,
    string RhsRPN,
    string GuardPackageCanonical,
    string GuardPackageDigest,
    string GuardPackageFingerprint)
{
    // Frozen packet species token; identifier-side name is Rung0ComposedForm.
    public const string Species = "Rung0DerivedForm";
    public string PredictionSpecies => Species;

    public static EmlRung0AdmissionPath Create(EmlPredictionID obligationPredictionID, in EmlRung0Proof proof)
    {
        if (proof.Steps.Count == 0) throw new InvalidDataException("rung-0 admission path requires guarded proof steps");
        StringBuilder canonical = new();
        for (int i = 0; i < proof.Steps.Count; i++)
        {
            if (i != 0) canonical.Append('|');
            EmlCompositionStep step = proof.Steps[i];
            canonical.Append(step.RuleID.Value).Append(':')
                .Append(step.DomainGuardDigest.ToString("X16", CultureInfo.InvariantCulture)).Append(':')
                .Append(step.GuardWitness.Canonical());
        }
        string guardPackageCanonical = canonical.ToString();
        string guardPackageFingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(guardPackageCanonical)));
        return new(
            obligationPredictionID,
            proof.AntecedentRPN,
            proof.ConsequentRPN,
            guardPackageCanonical,
            proof.Steps[^1].GuardWitness.Digest.ToString("X16", CultureInfo.InvariantCulture),
            guardPackageFingerprint);
    }

    public bool IsBound
        => PredictionSpecies == Species
            && ObligationPredictionID.Value >= 0
            && !string.IsNullOrEmpty(LhsRPN)
            && !string.IsNullOrEmpty(RhsRPN)
            && !string.IsNullOrEmpty(GuardPackageCanonical)
            && !string.IsNullOrEmpty(GuardPackageDigest)
            && string.Equals(
                GuardPackageFingerprint,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(GuardPackageCanonical))),
                StringComparison.Ordinal);

    public bool Matches(in EmlRung0AdmissionPath other)
        => ObligationPredictionID == other.ObligationPredictionID
            && string.Equals(LhsRPN, other.LhsRPN, StringComparison.Ordinal)
            && string.Equals(RhsRPN, other.RhsRPN, StringComparison.Ordinal)
            && string.Equals(GuardPackageCanonical, other.GuardPackageCanonical, StringComparison.Ordinal)
            && string.Equals(GuardPackageDigest, other.GuardPackageDigest, StringComparison.Ordinal)
            && string.Equals(GuardPackageFingerprint, other.GuardPackageFingerprint, StringComparison.Ordinal);

    public bool MatchesProof(in EmlRung0Proof proof)
        => Matches(Create(ObligationPredictionID, in proof));

    /// Execute the ordinary numeric comparator for this exact path.  The positive evaluator
    /// interval is measured by the caller; no counterfactual estimate is admitted.
    public EmlVerdict Grade(EmlGrader grader)
    {
        ArgumentNullException.ThrowIfNull(grader);
        if (!IsBound) throw new InvalidDataException("rung-0 admission path is not bound to a claim and guard package");
        return grader.GradeRpn(LhsRPN, RhsRPN);
    }
}

internal readonly record struct EmlRung0AdmissionPathReceipt(
    EmlRung0AdmissionPath Path,
    EmlEvaluatorInterval Evaluation,
    int WorldContacts)
{
    public bool IsZeroAdditionalAdmission
        => Path.IsBound && WorldContacts == 0 && Evaluation.Calls == 0;

    public bool IsPositiveComparator
        => Path.IsBound && WorldContacts == 0 && Evaluation.Calls > 0;
}

/// The durable R3 witness for the zero-evaluator admission path.  The canonical lhs/rhs and
/// guard package digest are retained alongside the typed IDs; a scalar call count alone cannot
/// prove that the comparator graded the identical claim.
internal readonly record struct EmlRung0ComposedFormProof(
    EmlPredictionID ObligationPredictionID,
    string ObligationID,
    EmlPredictionID ComposedPredictionID,
    string LhsRPN,
    string RhsRPN,
    string GuardPackageDigest,
    string ProofID,
    string AuditID,
    string AdmissionID,
    string ClosureID,
    string Comparator,
    EmlEvaluatorInterval AdmissionEvaluation,
    EmlEvaluatorInterval ComparatorEvaluation,
    EmlRung0Proof Proof,
    EmlRung0Audit Audit)
{
    public EmlRung0AdmissionPath AdmissionPath
    {
        get
        {
            EmlRung0Proof proof = Proof;
            return EmlRung0AdmissionPath.Create(ObligationPredictionID, in proof);
        }
    }

    public EmlRung0AdmissionPathReceipt AdmissionReceipt
        => new(AdmissionPath, AdmissionEvaluation, WorldContacts: 0);

    public EmlRung0AdmissionPathReceipt ComparatorReceipt
        => new(AdmissionPath, ComparatorEvaluation, WorldContacts: 0);

    public bool IsExactZeroAdmission
        => AdmissionReceipt.IsZeroAdditionalAdmission
            && ComparatorReceipt.IsPositiveComparator
            && string.Equals(Comparator, "EmlGrader.GradeRpn", StringComparison.Ordinal)
            && Proof.PredictionID == ObligationPredictionID
            && Proof.AntecedentRPN == LhsRPN
            && Proof.ConsequentRPN == RhsRPN
            && string.Equals(GuardPackageDigest, AdmissionPath.GuardPackageDigest, StringComparison.Ordinal)
            && Proof.Digest.ToString("X16", CultureInfo.InvariantCulture) == ProofID
            && Audit.ProofDigest == Proof.Digest;
}

internal readonly record struct EmlComposedFormProof(
    EmlPredictionID SourcePredictionID,
    EmlPredictionID ComposedPredictionID,
    string Program,
    EmlCert Certificate,
    EmlRung0Proof Proof,
    EmlRung0Audit Audit);

internal readonly record struct EmlComposedFormAdmission(
    EmlComposedFormAdmissionStatuses Status,
    EmlPredictionID PredictionID,
    EmlCert Certificate,
    EmlEvaluatorInterval Evaluation,
    ulong ProofDigest,
    string AdmissionID = "",
    string ProofID = "",
    string AuditID = "")
{
    public bool Accepted => Status == EmlComposedFormAdmissionStatuses.Accepted;
    public EmlRung0AdmissionPath AdmissionPath { get; init; }
}

internal readonly record struct EmlRung0AdmissionResult(
    EmlRung0Result Composition,
    EmlRung0Audit? Audit,
    EmlComposedFormAdmission? Admission,
    long MainEvaluatorDelta,
    bool NumericFallbackProhibited)
{
    public bool Admitted => Admission?.Accepted == true && MainEvaluatorDelta == 0;
    public IReadOnlyList<EmlRung0FunnelReceipt> FunnelReceipts { get; init; } = Array.Empty<EmlRung0FunnelReceipt>();
    public EmlRung0ComposedFormProof? ClosureProof { get; init; }
}

internal static class EmlRung0Admission
{
    private static string ResolveObligationID(EmlSieve sieve, EmlPredictionID sourcePredictionID, string candidateIdentity)
    {
        if (!string.IsNullOrEmpty(candidateIdentity)) return candidateIdentity;
        if (sieve.TryReadTargetIdentity(sourcePredictionID, out string identity, out _)) return identity;
        return "unregistered:" + sourcePredictionID.Value.ToString(CultureInfo.InvariantCulture);
    }

    public static EmlRung0AdmissionResult TryAdmit(
        EmlSieve sieve,
        EmlLawStore store,
        in EmlLawCandidateInstantiation candidate,
        EmlDeliberationLease? deliberationLease = null,
        EmlRung0Budget? searchBudget = null)
    {
        ArgumentNullException.ThrowIfNull(sieve);
        ArgumentNullException.ThrowIfNull(store);
        long evaluatorStart = sieve.EvaluatorClock.ProgramPointEvaluations;
        List<EmlRung0FunnelReceipt> funnel = new();
        EmlObligationTarget address = candidate.Address;
        if (address.SourcePredictionID != candidate.Obligation.SourcePredictionID)
        {
            EmlRung0Result mismatch = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Opportunity, candidate.Obligation.SourcePredictionID,
                "mismatch", candidate.Rewrite.RuleID, false, "target-source-mismatch", "", "", "", "",
                EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
            return new EmlRung0AdmissionResult(mismatch, null, null, 0, false) { FunnelReceipts = funnel };
        }
        if (!Enum.IsDefined(address.Species))
        {
            EmlRung0Result unknownSpecies = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Opportunity, candidate.Obligation.SourcePredictionID,
                "unknown-species", candidate.Rewrite.RuleID, false, "target-species-unknown", "", "", "", "",
                EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
            return new EmlRung0AdmissionResult(unknownSpecies, null, null, 0, false) { FunnelReceipts = funnel };
        }
        bool targetRegistered = sieve.TryReadTargetIdentity(
            address.SourcePredictionID, out _, out EmlObligationTargetSpecies actualSpecies);
        if (address.Species == EmlObligationTargetSpecies.ExactComposition && !targetRegistered)
        {
            EmlRung0Result unregistered = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Opportunity, candidate.Obligation.SourcePredictionID,
                "unregistered:" + address.SourcePredictionID.Value.ToString(CultureInfo.InvariantCulture), candidate.Rewrite.RuleID,
                false, "target-unregistered", "", "", "", "", EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
            return new EmlRung0AdmissionResult(unregistered, null, null, 0, false) { FunnelReceipts = funnel };
        }
        if (targetRegistered && actualSpecies != address.Species)
        {
            EmlRung0Result mismatch = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Opportunity, candidate.Obligation.SourcePredictionID,
                "mismatch", candidate.Rewrite.RuleID, false, "target-species-mismatch", "", "", "", "",
                EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
            return new EmlRung0AdmissionResult(mismatch, null, null, 0, false) { FunnelReceipts = funnel };
        }
        if (address.Species == EmlObligationTargetSpecies.ExactComposition)
        {
            if (!sieve.TryResolveExactCompositionObligation(address.SourcePredictionID, out EmlExactCompositionObligation exactTarget)
                || candidate.PredictionCarrier is not EmlRewritePredictionCarrier exactCarrier
                || exactCarrier.PredictionID != exactTarget.SourcePredictionID
                || !string.Equals(exactCarrier.SourceDigest, exactTarget.SourceDigest, StringComparison.Ordinal)
                || !string.Equals(candidate.Instantiation.LeftRpn, exactTarget.CarrierRPN, StringComparison.Ordinal)
                || sieve.GetPredictionCertificate(exactTarget.SourcePredictionID) != exactTarget.SourceCertificate)
            {
                EmlRung0Result custodyMismatch = new(EmlRung0Statuses.NoCandidate, null, default);
                funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Opportunity, candidate.Obligation.SourcePredictionID,
                    "mismatch", candidate.Rewrite.RuleID, false, "exact-carrier-mismatch", "", "", "", "",
                    EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
                return new EmlRung0AdmissionResult(custodyMismatch, null, null, 0, false) { FunnelReceipts = funnel };
            }
        }
        string obligationID = ResolveObligationID(sieve, candidate.Obligation.SourcePredictionID, "");
        funnel.Add(new EmlRung0FunnelReceipt(
            EmlRung0FunnelStages.Opportunity,
            candidate.Obligation.SourcePredictionID,
            obligationID,
            candidate.Rewrite.RuleID,
            Accepted: true,
            "candidate-selected",
            "", "", "", "",
            EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
        funnel.Add(new EmlRung0FunnelReceipt(
            EmlRung0FunnelStages.Eligibility,
            candidate.Obligation.SourcePredictionID,
            obligationID,
            candidate.Rewrite.RuleID,
            candidate.PredictionCarrier is EmlRewritePredictionCarrier && candidate.Rewrite.IsRung0Eligible,
            candidate.PredictionCarrier is EmlRewritePredictionCarrier && candidate.Rewrite.IsRung0Eligible ? "guarded-carrier" : "ineligible",
            "", "", "", "",
            EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
        funnel.Add(new EmlRung0FunnelReceipt(
            EmlRung0FunnelStages.Funding,
            candidate.Obligation.SourcePredictionID,
            obligationID,
            candidate.Rewrite.RuleID,
            deliberationLease is not null,
            deliberationLease is null ? "no-lease" : "funded",
            "", "", "", "",
            EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
        if (candidate.PredictionCarrier is not EmlRewritePredictionCarrier carrier)
        {
            EmlRung0Result missing = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Attempt, candidate.Obligation.SourcePredictionID,
                obligationID, candidate.Rewrite.RuleID, false, "missing-carrier", "", "", "", "",
                EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
            return new EmlRung0AdmissionResult(missing, null, null, 0, false) { FunnelReceipts = funnel };
        }
        if (!candidate.Rewrite.IsRung0Eligible
            || candidate.Rewrite.IsRelationNull
            || !EmlRewriteSystem.ReducesRank(candidate.Rewrite.AntecedentRpn, candidate.Rewrite.ConsequentRpn))
        {
            EmlRung0Result ineligible = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Attempt, candidate.Obligation.SourcePredictionID,
                obligationID, candidate.Rewrite.RuleID, false, "ineligible", "", "", "", "",
                EmlEvaluatorInterval.EmptyAt(evaluatorStart)));
            return new EmlRung0AdmissionResult(ineligible, null, null, 0, false) { FunnelReceipts = funnel };
        }

        EmlRung0Budget budget = searchBudget ?? EmlRung0Budget.Default;
        EmlRung0Result derivation = store.DeriveRung0(
            in carrier,
            candidate.Instantiation.LeftRpn,
            candidate.Instantiation.RightRpn,
            in budget,
            deliberationLease);
        if (!derivation.Composed)
        {
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Attempt, candidate.Obligation.SourcePredictionID,
                obligationID, candidate.Rewrite.RuleID, false,
                derivation.Status.ToString(), "", "", "", "",
                sieve.EvaluatorClock.MeasureFrom(evaluatorStart)));
            return new EmlRung0AdmissionResult(
                derivation,
                null,
                null,
                sieve.EvaluatorClock.ProgramPointEvaluations - evaluatorStart,
                derivation.Status == EmlRung0Statuses.GuardRejected
                    && store.IsRung0RuleQuarantined(candidate.Rewrite.RuleID))
            { FunnelReceipts = funnel };
        }

        EmlRung0Proof proof = derivation.Proof!.Value;
        EmlLawRewrite candidateRewrite = candidate.Rewrite;
        if (!MatchesCandidateRewrite(in proof, in candidateRewrite))
        {
            EmlRung0Result proofMismatch = new(EmlRung0Statuses.NoCandidate, null, default);
            funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Attempt, candidate.Obligation.SourcePredictionID,
                obligationID, candidate.Rewrite.RuleID, false, "candidate-proof-mismatch", "", "", "", "",
                sieve.EvaluatorClock.MeasureFrom(evaluatorStart)));
            return new EmlRung0AdmissionResult(
                proofMismatch,
                null,
                null,
                sieve.EvaluatorClock.ProgramPointEvaluations - evaluatorStart,
                false)
            { FunnelReceipts = funnel };
        }
        EmlRung0AuditSelectionSpecies selection = sieve.HasAcceptedComposedFormProofs
            ? EmlRung0AuditSelectionSpecies.DigestCadence
            : EmlRung0AuditSelectionSpecies.MinimumOne;
        EmlRung0Audit audit;
        bool promoteRetainedAudit = false;
        if (store.TryGetRung0Audit(proof.Digest, out EmlRung0Audit retainedAudit))
        {
            // A legacy cadence miss may have been retained before this proof became the
            // first accepted derived form. It is not evidence for admission: promote it
            // through the typed sampler, preserving the proof/rules/guard identity.
            if (selection == EmlRung0AuditSelectionSpecies.MinimumOne
                && retainedAudit.Status == EmlRung0AuditStatuses.NotSelected
                && retainedAudit.Selection == EmlRung0AuditSelectionSpecies.DigestCadence)
            {
                audit = EmlRung0Auditor.Audit(store, in proof, selection, persist: false);
                promoteRetainedAudit = true;
            }
            else
                audit = retainedAudit;
        }
        else
            audit = EmlRung0Auditor.Audit(store, in proof, selection, persist: false);
        EmlComposedFormAdmission? admission = null;
        if (audit.Status != EmlRung0AuditStatuses.Disagreed
            && sieve.TryAdmitComposedForm(in proof, in audit, out EmlComposedFormAdmission admitted))
            admission = admitted;
        if (admission is EmlComposedFormAdmission acceptedAdmission && acceptedAdmission.Accepted)
            sieve.StageRung0Audit(store, in audit, promoteRetainedAudit);
        else if (audit.Status == EmlRung0AuditStatuses.Disagreed && sieve.HasAcceptedComposedFormProofs)
            sieve.StageRung0Audit(store, in audit, promoteRetainedAudit);
        long delta = sieve.EvaluatorClock.ProgramPointEvaluations - evaluatorStart;
        if (delta != 0) throw new InvalidOperationException("rung-0 admission changed the main evaluator clock");
        bool numericFallbackProhibited = audit.Status == EmlRung0AuditStatuses.Disagreed;
        for (int i = 0; i < proof.Steps.Count && !numericFallbackProhibited; i++)
            numericFallbackProhibited = store.IsRung0RuleQuarantined(proof.Steps[i].RuleID);
        EmlRung0AdmissionResult admissionResult = new EmlRung0AdmissionResult(
            derivation,
            audit,
            admission,
            delta,
            numericFallbackProhibited);
        funnel.Add(new EmlRung0FunnelReceipt(EmlRung0FunnelStages.Attempt, candidate.Obligation.SourcePredictionID,
            obligationID, candidate.Rewrite.RuleID, admissionResult.Admitted,
            admissionResult.Admitted ? "derived" : derivation.Status.ToString(),
            proof.Digest.ToString("X16", CultureInfo.InvariantCulture),
            audit.ProofDigest.ToString("X16", CultureInfo.InvariantCulture),
            admission?.AdmissionID ?? "", "", sieve.EvaluatorClock.MeasureFrom(evaluatorStart),
            AuditSelection: audit.Selection));
        if (admission is EmlComposedFormAdmission admittedForm && admittedForm.Accepted)
        {
            // Admission is not completion.  A rung-0 success is only a solved
            // obligation once the same world-bound target owns its durable closure
            // packet; otherwise callers can mark the search changed while the
            // adjudicator quite correctly sees no closure authority.
            if (!sieve.HasObligation(candidate.Obligation.SourcePredictionID))
                throw new InvalidDataException("rung-0 admission succeeded for an unregistered obligation");
            EmlRung0ComposedFormProof closureProof = sieve.AdmitRung0ComposedFormClosure(
                in proof, in audit, in admittedForm, candidate.Obligation.SourcePredictionID, out EmlRung0FunnelReceipt closureReceipt);
            if (!closureProof.IsExactZeroAdmission || !sieve.IsObligationClosed(candidate.Obligation.SourcePredictionID))
                throw new InvalidDataException("rung-0 admission did not emit a durable obligation closure");
            funnel.Add(closureReceipt with { AuditSelection = audit.Selection });
            admissionResult = admissionResult with { ClosureProof = closureProof };
        }
        return admissionResult with { FunnelReceipts = funnel };
    }

    private static bool MatchesCandidateRewrite(in EmlRung0Proof proof, in EmlLawRewrite rewrite)
    {
        if (proof.Steps.Count == 0) return false;
        EmlCompositionStep first = proof.Steps[0];
        return first.RuleID == rewrite.RuleID
            && first.Orientation == rewrite.Orientation
            && first.Path == rewrite.MatchedPath
            && string.Equals(first.SubstitutionRpn, rewrite.SubstitutionRpn, StringComparison.Ordinal)
            && string.Equals(first.ConsequentRpn, rewrite.ConsequentRpn, StringComparison.Ordinal);
    }
}

internal static class EmlRung0Auditor
{
    public static EmlRung0Audit Audit(
        EmlLawStore store,
        in EmlRung0Proof proof,
        bool forceDisagreement = false)
        => Audit(store, in proof, EmlRung0AuditSelectionSpecies.DigestCadence, forceDisagreement, persist: true);

    public static EmlRung0Audit Audit(
        EmlLawStore store,
        in EmlRung0Proof proof,
        EmlRung0AuditSelectionSpecies selection,
        bool forceDisagreement = false,
        bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(store);
        List<EmlRuleID> rules = new();
        HashSet<EmlRuleID> seen = new();
        for (int i = 0; i < proof.Steps.Count; i++)
            if (seen.Add(proof.Steps[i].RuleID)) rules.Add(proof.Steps[i].RuleID);
        rules.Sort(static (left, right) => string.CompareOrdinal(left.Value, right.Value));
        bool selected = EmlRung0Digest.SelectNumericAudit(proof.Digest)
            || selection == EmlRung0AuditSelectionSpecies.MinimumOne;
        if (!selected)
        {
            if (forceDisagreement)
                throw new InvalidOperationException("forced disagreement requires a cadence-selected proof");
            EmlRung0Audit skipped = new(
                proof.Digest,
                EmlRung0AuditStatuses.NotSelected,
                0,
                NumericVerified: false,
                GuardVerified: true,
                rules,
                EmlRung0AuditSelectionSpecies.DigestCadence);
            if (persist) store.RecordRung0Audit(in skipped);
            return skipped;
        }

        EmlEvaluatorClock clock = new();
        long start = clock.ProgramPointEvaluations;
        EmlVerdict verdict = new EmlGrader(clock).GradeRpn(proof.AntecedentRPN, proof.ConsequentRPN);
        long calls = clock.ProgramPointEvaluations - start;
        bool numericVerified = verdict.Grade == 'E' && !forceDisagreement;
        bool guardVerified = store.VerifyRung0ProofGuards(in proof);
        EmlRung0Audit audit = new(
            proof.Digest,
            numericVerified && guardVerified ? EmlRung0AuditStatuses.Agreed : EmlRung0AuditStatuses.Disagreed,
            calls,
            numericVerified,
            guardVerified,
            rules,
            selection);
        if (persist) store.RecordRung0Audit(in audit);
        return audit;
    }
}

internal sealed class EmlRewriteEdgeBudget
{
    private readonly int _maximum;

    public EmlRewriteEdgeBudget(int maximum)
    {
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum;
    }

    public int Applications { get; private set; }
    public bool Exhausted { get; private set; }

    public bool TryReserve(EmlDeliberationLease? deliberationLease)
    {
        if (Applications >= _maximum)
        {
            Exhausted = true;
            return false;
        }
        deliberationLease?.ReserveLawRewriteApplication();
        Applications++;
        return true;
    }
}

internal static class EmlRung0Digest
{
    public static bool SelectNumericAudit(ulong proofDigest) => proofDigest != 0 && (proofDigest & 0xFUL) == 0;

    public static ulong Calculate(in EmlRung0Proof proof)
    {
        ulong hash = 14695981039346656037UL;
        HashText(ref hash, proof.PredictionID.Value.ToString(CultureInfo.InvariantCulture));
        HashText(ref hash, proof.SourceDigest);
        HashText(ref hash, proof.AntecedentRPN);
        HashText(ref hash, proof.ConsequentRPN);
        HashText(ref hash, proof.SearchRevision.ToString(CultureInfo.InvariantCulture));
        HashText(ref hash, proof.Budget.Canonical());
        HashText(ref hash, proof.Work.ExpandedStates.ToString(CultureInfo.InvariantCulture));
        HashText(ref hash, proof.Work.VisitedStates.ToString(CultureInfo.InvariantCulture));
        HashText(ref hash, proof.Work.Applications.ToString(CultureInfo.InvariantCulture));
        HashText(ref hash, proof.Work.GuardRejections.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < proof.Steps.Count; i++)
        {
            EmlCompositionStep step = proof.Steps[i];
            HashText(ref hash, step.RuleID.Value);
            HashText(ref hash, step.Orientation.ToString());
            HashText(ref hash, step.Path.Steps);
            HashText(ref hash, step.SubstitutionRpn);
            HashText(ref hash, step.AntecedentRpn);
            HashText(ref hash, step.ConsequentRpn);
            HashText(ref hash, step.GuardWitness.Canonical());
            HashText(ref hash, step.RankBefore.ToString(CultureInfo.InvariantCulture));
            HashText(ref hash, step.RankAfter.ToString(CultureInfo.InvariantCulture));
            HashText(ref hash, step.RulePattern);
            HashText(ref hash, step.BasisLawDigest.ToString("X16", CultureInfo.InvariantCulture));
            HashText(ref hash, step.DomainGuardDigest.ToString("X16", CultureInfo.InvariantCulture));
        }
        return proof.Steps.Count == 0 ? 0 : hash;
    }

    public static bool HasPortableStepChain(in EmlRung0Proof proof)
    {
        string antecedent = proof.AntecedentRPN;
        for (int i = 0; i < proof.Steps.Count; i++)
        {
            EmlCompositionStep step = proof.Steps[i];
            if (step.RuleID.IsEmpty
                || !Enum.IsDefined(step.Orientation)
                || step.BasisLawDigest == 0
                || step.DomainGuardDigest == 0
                || step.RankBefore != step.AntecedentRpn.Length
                || step.RankAfter != step.ConsequentRpn.Length
                || !EmlRewriteSystem.ReducesRank(step.AntecedentRpn, step.ConsequentRpn)
                || !IsCanonicalRPN(step.AntecedentRpn)
                || !IsCanonicalRPN(step.SubstitutionRpn)
                || !IsCanonicalRPN(step.ConsequentRpn)
                || !string.Equals(step.AntecedentRpn, antecedent, StringComparison.Ordinal)
                || EmlRuleID.Create(step.RulePattern, step.Orientation, step.BasisLawDigest, step.DomainGuardDigest) != step.RuleID
                || !step.GuardWitness.HasValidDigest
                || !step.GuardWitness.Matches(
                    step.Path,
                    step.GuardWitness.MatchedTermRpn,
                    step.SubstitutionRpn,
                    step.AntecedentRpn,
                    step.ConsequentRpn)
                || !EmlTree.TryParseRPN(step.AntecedentRpn, out EmlTree? tree)
                || !EmlTree.TryParseRPN(step.SubstitutionRpn, out EmlTree? substitution)
                || !EmlOneHoleLaw.TryParse(step.RulePattern, out EmlOneHoleLaw law)) return false;
            EmlTree replacement = law.InstantiateReplacement(step.Orientation, substitution!);
            EmlTree rewritten;
            try { rewritten = tree!.ReplaceSubtree(step.Path, replacement); }
            catch (ArgumentOutOfRangeException) { return false; }
            if (!string.Equals(rewritten.RenderRPN(), step.ConsequentRpn, StringComparison.Ordinal)) return false;
            antecedent = step.ConsequentRpn;
        }
        return string.Equals(antecedent, proof.ConsequentRPN, StringComparison.Ordinal);
    }

    public static string DescribeNonPortableStepChain(in EmlRung0Proof proof)
    {
        string antecedent = proof.AntecedentRPN;
        for (int i = 0; i < proof.Steps.Count; i++)
        {
            EmlCompositionStep step = proof.Steps[i];
            if (step.RuleID.IsEmpty) return $"step-{i}-empty-rule";
            if (!Enum.IsDefined(step.Orientation)) return $"step-{i}-orientation";
            if (step.BasisLawDigest == 0) return $"step-{i}-basis";
            if (step.DomainGuardDigest == 0) return $"step-{i}-domain";
            if (step.RankBefore != step.AntecedentRpn.Length) return $"step-{i}-rank-before";
            if (step.RankAfter != step.ConsequentRpn.Length) return $"step-{i}-rank-after";
            if (!EmlRewriteSystem.ReducesRank(step.AntecedentRpn, step.ConsequentRpn)) return $"step-{i}-rank-not-decreasing";
            if (!IsCanonicalRPN(step.AntecedentRpn)) return $"step-{i}-antecedent-not-canonical";
            if (!IsCanonicalRPN(step.SubstitutionRpn)) return $"step-{i}-substitution-not-canonical";
            if (!IsCanonicalRPN(step.ConsequentRpn)) return $"step-{i}-consequent-not-canonical";
            if (!string.Equals(step.AntecedentRpn, antecedent, StringComparison.Ordinal)) return $"step-{i}-antecedent";
            if (EmlRuleID.Create(step.RulePattern, step.Orientation, step.BasisLawDigest, step.DomainGuardDigest) != step.RuleID) return $"step-{i}-rule-id";
            if (!step.GuardWitness.HasValidDigest) return $"step-{i}-witness-digest";
            if (!step.GuardWitness.Matches(step.Path, step.GuardWitness.MatchedTermRpn,
                    step.SubstitutionRpn, step.AntecedentRpn, step.ConsequentRpn)) return $"step-{i}-witness-binding";
            if (!EmlTree.TryParseRPN(step.AntecedentRpn, out EmlTree? tree)) return $"step-{i}-antecedent-rpn";
            if (!EmlTree.TryParseRPN(step.SubstitutionRpn, out EmlTree? substitution)) return $"step-{i}-substitution-rpn";
            if (!EmlOneHoleLaw.TryParse(step.RulePattern, out EmlOneHoleLaw law)) return $"step-{i}-rule-pattern";
            EmlTree replacement = law.InstantiateReplacement(step.Orientation, substitution!);
            EmlTree rewritten;
            try { rewritten = tree!.ReplaceSubtree(step.Path, replacement); }
            catch (ArgumentOutOfRangeException) { return $"step-{i}-path"; }
            if (!string.Equals(rewritten.RenderRPN(), step.ConsequentRpn, StringComparison.Ordinal)) return $"step-{i}-replacement";
            antecedent = step.ConsequentRpn;
        }
        return string.Equals(antecedent, proof.ConsequentRPN, StringComparison.Ordinal)
            ? "portable"
            : "final-consequent";
    }

    public static void HashText(ref ulong hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }
        hash ^= 0xFF;
        hash *= 1099511628211UL;
    }

    public static bool IsCanonicalRPN(string rpn)
        => EmlTree.TryParseRPN(rpn, out EmlTree? tree)
            && string.Equals(tree!.RenderRPN(), rpn, StringComparison.Ordinal);
}

internal static class EmlRung0Checkpoint
{
    public static string ProofSHA256(in EmlRung0Proof proof)
        => SHA256Hex(static (writer, value) => WriteProof(writer, value), in proof);

    public static string AuditSHA256(in EmlRung0Audit audit)
        => SHA256Hex(static (writer, value) => WriteAudit(writer, value), in audit);

    public static string LegacyAuditSHA256(in EmlRung0Audit audit)
        => SHA256Hex(static (writer, value) => WriteAudit(writer, value, includeSelection: false), in audit);

    private static string SHA256Hex<T>(Action<CkptWriter, T> write, in T value)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) write(writer, value);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static void WriteProof(CkptWriter writer, in EmlRung0Proof proof)
    {
        writer.I32(proof.PredictionID.Value);
        writer.Str(proof.SourceDigest);
        writer.Str(proof.AntecedentRPN);
        writer.Str(proof.ConsequentRPN);
        writer.I32(proof.SearchRevision);
        writer.I32(proof.Budget.MaxDepth);
        writer.I32(proof.Budget.MaxStates);
        writer.I32(proof.Budget.MaxApplications);
        writer.I32(proof.Work.ExpandedStates);
        writer.I32(proof.Work.VisitedStates);
        writer.I32(proof.Work.Applications);
        writer.I32(proof.Work.GuardRejections);
        writer.U64(proof.Digest);
        writer.I32(proof.Steps.Count);
        for (int i = 0; i < proof.Steps.Count; i++) WriteStep(writer, proof.Steps[i]);
    }

    public static EmlRung0Proof ReadProof(CkptReader reader)
    {
        EmlPredictionID claimID = new(reader.I32());
        string sourceDigest = reader.Str();
        string antecedent = reader.Str();
        string consequent = reader.Str();
        int revision = reader.I32();
        EmlRung0Budget budget = new(reader.I32(), reader.I32(), reader.I32());
        EmlRung0Work work = new(reader.I32(), reader.I32(), reader.I32(), reader.I32());
        ulong digest = reader.U64();
        int stepCount = reader.I32();
        if (stepCount < 1 || stepCount > 1024)
            throw new InvalidDataException("rung-0 proof carries an invalid step count");
        EmlCompositionStep[] steps = new EmlCompositionStep[stepCount];
        for (int i = 0; i < steps.Length; i++) steps[i] = ReadStep(reader, legacy: false);
        return new EmlRung0Proof(claimID, sourceDigest, antecedent, consequent, revision, budget, steps, work, digest);
    }

    public static void WriteAudit(CkptWriter writer, in EmlRung0Audit audit, bool includeSelection = true)
    {
        writer.U64(audit.ProofDigest);
        writer.U8((byte)audit.Status);
        writer.I64(audit.EvaluatorCalls);
        writer.Bool(audit.NumericVerified);
        writer.Bool(audit.GuardVerified);
        writer.I32(audit.Rules.Count);
        for (int i = 0; i < audit.Rules.Count; i++) writer.Str(audit.Rules[i].Value);
        if (includeSelection) writer.U8((byte)audit.Selection);
    }

    public static EmlRung0Audit ReadAudit(CkptReader reader, bool hasSelection = true)
    {
        ulong proofDigest = reader.U64();
        EmlRung0AuditStatuses status = (EmlRung0AuditStatuses)reader.U8();
        long evaluatorCalls = reader.I64();
        bool numericVerified = reader.Bool();
        bool guardVerified = reader.Bool();
        int ruleCount = reader.I32();
        if (ruleCount < 1 || ruleCount > 1024)
            throw new InvalidDataException("rung-0 audit carries an invalid rule count");
        EmlRuleID[] rules = new EmlRuleID[ruleCount];
        for (int i = 0; i < rules.Length; i++) rules[i] = new EmlRuleID(reader.Str());
        EmlRung0AuditSelectionSpecies selection = hasSelection
            ? (EmlRung0AuditSelectionSpecies)reader.U8()
            : EmlRung0AuditSelectionSpecies.DigestCadence;
        return new EmlRung0Audit(proofDigest, status, evaluatorCalls, numericVerified, guardVerified, rules, selection);
    }

    public static void WriteStep(CkptWriter writer, in EmlCompositionStep step)
    {
        writer.Str(step.RuleID.Value);
        writer.U8((byte)step.Orientation);
        writer.Str(step.Path.Steps);
        writer.Str(step.SubstitutionRpn);
        writer.Str(step.AntecedentRpn);
        writer.Str(step.ConsequentRpn);
        writer.Str(step.RulePattern);
        writer.U64(step.BasisLawDigest);
        writer.U64(step.DomainGuardDigest);
        writer.Str(step.GuardWitness.MatchedTermRpn ?? string.Empty);
        writer.Str(step.GuardWitness.SubstitutionRpn ?? string.Empty);
        writer.Str(step.GuardWitness.MatchedPath.Steps);
        writer.Str(step.GuardWitness.AntecedentRpn ?? string.Empty);
        writer.Str(step.GuardWitness.ConsequentRpn ?? string.Empty);
        writer.F64(step.GuardWitness.Enclosure.RealLower);
        writer.F64(step.GuardWitness.Enclosure.RealUpper);
        writer.F64(step.GuardWitness.Enclosure.ImaginaryLower);
        writer.F64(step.GuardWitness.Enclosure.ImaginaryUpper);
        writer.Bool(step.GuardWitness.Branch.LogDefined);
        writer.Bool(step.GuardWitness.Branch.EnclosureCrossesNegativeRealCut);
        writer.Bool(step.GuardWitness.Branch.ExpAfterLogRoundTrips);
        writer.Bool(step.GuardWitness.Branch.LogAfterExpRoundTrips);
        writer.I64(step.GuardWitness.Branch.ExponentialTurn);
        writer.U64(step.GuardWitness.Digest);
        int factCount = step.GuardWitness.NodeFacts?.Count ?? 0;
        writer.I32(factCount);
        for (int i = 0; i < factCount; i++)
        {
            EmlGuardNodeFact fact = step.GuardWitness.NodeFacts![i];
            writer.U8((byte)fact.Side);
            writer.Str(fact.Path.Steps);
            writer.F64(fact.Enclosure.RealLower);
            writer.F64(fact.Enclosure.RealUpper);
            writer.F64(fact.Enclosure.ImaginaryLower);
            writer.F64(fact.Enclosure.ImaginaryUpper);
            writer.Bool(fact.Branch.LogDefined);
            writer.Bool(fact.Branch.EnclosureCrossesNegativeRealCut);
            writer.Bool(fact.Branch.ExpAfterLogRoundTrips);
            writer.Bool(fact.Branch.LogAfterExpRoundTrips);
            writer.I64(fact.Branch.ExponentialTurn);
        }
        writer.I32(step.RankBefore);
        writer.I32(step.RankAfter);
    }

    public static EmlCompositionStep ReadStep(CkptReader reader, bool legacy, bool hasNodeFacts = true)
    {
        EmlRuleID ruleID = new(reader.Str());
        EmlLawOrientations orientation = (EmlLawOrientations)reader.U8();
        EmlPath path = new(reader.Str());
        string substitution = reader.Str();
        string antecedent = reader.Str();
        string consequent = reader.Str();
        string rulePattern = legacy ? string.Empty : reader.Str();
        ulong basisDigest = legacy ? 0 : reader.U64();
        ulong domainDigest = legacy ? 0 : reader.U64();
        string matchedTerm = reader.Str();
        string witnessSubstitution = reader.Str();
        EmlPath witnessPath = legacy ? EmlPath.Root : new EmlPath(reader.Str());
        string witnessAntecedent = legacy ? string.Empty : reader.Str();
        string witnessConsequent = legacy ? string.Empty : reader.Str();
        EmlEnclosureWitness enclosure = new(reader.F64(), reader.F64(), reader.F64(), reader.F64());
        EmlBranchWitness branch = new(reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.I64());
        ulong digest = reader.U64();
        List<EmlGuardNodeFact>? facts = null;
        if (hasNodeFacts)
        {
            int count = reader.I32();
            if (count < 0 || count > 4096) throw new InvalidDataException("rung-0 derivation step has an invalid node-fact count");
            facts = new List<EmlGuardNodeFact>(count);
            for (int i = 0; i < count; i++)
            {
                EmlGuardSides side = (EmlGuardSides)reader.U8();
                if (!Enum.IsDefined(side)) throw new InvalidDataException("rung-0 derivation step has an unknown node-fact side");
                facts.Add(new EmlGuardNodeFact(
                    side,
                    new EmlPath(reader.Str()),
                    new EmlEnclosureWitness(reader.F64(), reader.F64(), reader.F64(), reader.F64()),
                    new EmlBranchWitness(reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.I64())));
            }
        }
        EmlGuardWitness witness = new(
            matchedTerm,
            witnessSubstitution,
            enclosure, branch, digest,
            witnessPath,
            witnessAntecedent,
            witnessConsequent,
            facts);
        return new EmlCompositionStep(
            ruleID,
            orientation,
            path,
            substitution,
            antecedent,
            consequent,
            witness,
            reader.I32(),
            reader.I32(),
            rulePattern,
            basisDigest,
            domainDigest);
    }
}
