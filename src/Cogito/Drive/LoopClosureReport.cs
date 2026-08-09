namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using System.Collections.ObjectModel;
using Cogito.Grammar;
using Ronmamon;

public enum LoopClosureAssayStatuses : byte { Exact, Invalid }
public enum LoopClosurePowerStatuses : byte { Powered, Unpowered }
public enum LoopClosureVerdictStatuses : byte { PASS, FAIL, BANKED_NULL, INVALID }

public enum LoopClosureVerdictSpecies : byte
{
    PatternBecameThought,
    ThoughtOverruledInstinct,
    ObjectLoopClosed,
}

public readonly record struct LoopClosureDigest(string Value)
{
    public bool IsValid => !string.IsNullOrEmpty(Value) && Value.Length == 64 && Value.All(Uri.IsHexDigit);
    public override string ToString() => Value ?? "";

    /// An absent digest has exactly one meaning and must have exactly one identity.
    /// `default(LoopClosureDigest)` bypasses the constructor and leaves a null string while a
    /// decoded absent digest carries "", so structural equality would call a receipt unequal to
    /// its own round-trip over a field nobody ever set. Absence compares as absence.
    public bool Equals(LoopClosureDigest other)
        => string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);

    public override int GetHashCode() => (Value ?? "").GetHashCode(StringComparison.Ordinal);
}

public readonly record struct LoopClosureQuotaID(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}

public readonly record struct PatternBecameThoughtCorroboration
{
    internal PatternBecameThoughtCorroboration(
        EmlPredictionID sourcePredictionID,
        EmlPredictionID derivedPredictionID,
        LoopLineageNodeID derivationNodeID,
        LoopClosureDigest proofSHA256,
        LoopClosureDigest auditSHA256,
        long mainEvaluatorDelta,
        long numericEvaluatorDelta,
        EmlObligationTargetSpecies targetSpecies = EmlObligationTargetSpecies.Residual,
        IReadOnlyList<TapeEventID>? supportEventIDs = null,
        IReadOnlyList<string>? basisLawAdmissionIDs = null)
    {
        SourcePredictionID = sourcePredictionID;
        ComposedPredictionID = derivedPredictionID;
        CompositionNodeID = derivationNodeID;
        ProofSHA256 = proofSHA256;
        AuditSHA256 = auditSHA256;
        MainEvaluatorDelta = mainEvaluatorDelta;
        NumericEvaluatorDelta = numericEvaluatorDelta;
        TargetSpecies = targetSpecies;
        SupportEventIDs = supportEventIDs?.ToArray() ?? Array.Empty<TapeEventID>();
        BasisLawAdmissionIDs = basisLawAdmissionIDs?.ToArray() ?? Array.Empty<string>();
    }

    public EmlPredictionID SourcePredictionID { get; }
    public EmlPredictionID ComposedPredictionID { get; }
    public LoopLineageNodeID CompositionNodeID { get; }
    public LoopClosureDigest ProofSHA256 { get; }
    public LoopClosureDigest AuditSHA256 { get; }
    public long MainEvaluatorDelta { get; }
    public long NumericEvaluatorDelta { get; }
    public EmlObligationTargetSpecies TargetSpecies { get; }
    public TapeEventID[] SupportEventIDs { get; }
    public string[] BasisLawAdmissionIDs { get; }

    public void Validate(bool requireCorroboration)
    {
        if (!requireCorroboration) return;
        if (!Enum.IsDefined(TargetSpecies))
            throw new InvalidDataException("theory-became-thought verdict carries an invalid target species");
        if (!ProofSHA256.IsValid || !AuditSHA256.IsValid || SourcePredictionID.Value < 0 || ComposedPredictionID.Value < 0 || !CompositionNodeID.IsValid)
            throw new InvalidDataException("theory-became-thought verdict omits its derivation corroboration");
        if (MainEvaluatorDelta != 0 || NumericEvaluatorDelta <= 0)
            throw new InvalidDataException("theory-became-thought verdict omits the exact zero-versus-positive evaluator differential");
        if (TargetSpecies == EmlObligationTargetSpecies.ExactComposition)
        {
            if (SupportEventIDs.Length == 0 || SupportEventIDs.Distinct().Count() != SupportEventIDs.Length
                || !SupportEventIDs.SequenceEqual(SupportEventIDs.OrderBy(static id => id.Value))
                || SupportEventIDs.Any(static id => id.Value < 0)
                || BasisLawAdmissionIDs.Length == 0
                || !BasisLawAdmissionIDs.SequenceEqual(BasisLawAdmissionIDs.Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal)))
                throw new InvalidDataException("exact theory corroboration omits canonical target support or basis law custody");
        }
        else if (SupportEventIDs.Length != 0 || BasisLawAdmissionIDs.Length != 0)
            throw new InvalidDataException("residual theory corroboration carries exact target custody");
    }
}

public readonly record struct ThoughtOverruledInstinctCorroboration
{
    internal ThoughtOverruledInstinctCorroboration(
        LoopLineageNodeID foldNodeID,
        GrammarRevisionID foldRevision,
        LoopClosureDigest teacherEvidenceSHA256,
        LoopClosureQuotaID fundingID,
        GrammarRevisionID divergenceRevision,
        int launchpadAction,
        CortexPolicyTrialExecutionOutcomes candidateExecutionOutcome,
        long candidateRequestCount,
        long candidateGuardAdmittedCount,
        int candidateAction,
        CortexPolicyDecisionID forcedDecisionID,
        int forcedAction,
        bool forcedDiverged,
        LoopClosureDigest nullReceiptSHA256,
        string? divergenceEvidenceBase64 = null)
    {
        FoldNodeID = foldNodeID;
        FoldRevision = foldRevision;
        TeacherEvidenceSHA256 = teacherEvidenceSHA256;
        QuotaID = fundingID;
        DivergenceRevision = divergenceRevision;
        LaunchpadAction = launchpadAction;
        CandidateExecutionOutcome = candidateExecutionOutcome;
        CandidateRequestCount = candidateRequestCount;
        CandidateGuardAdmittedCount = candidateGuardAdmittedCount;
        CandidateAction = candidateAction;
        ForcedDecisionID = forcedDecisionID;
        ForcedAction = forcedAction;
        ForcedDiverged = forcedDiverged;
        NullReceiptSHA256 = nullReceiptSHA256;
        DivergenceEvidenceBase64 = divergenceEvidenceBase64 ?? "";
    }

    public LoopLineageNodeID FoldNodeID { get; }
    public GrammarRevisionID FoldRevision { get; }
    public LoopClosureDigest TeacherEvidenceSHA256 { get; }
    public LoopClosureQuotaID QuotaID { get; }
    public GrammarRevisionID DivergenceRevision { get; }
    public int LaunchpadAction { get; }
    public CortexPolicyTrialExecutionOutcomes CandidateExecutionOutcome { get; }
    public long CandidateRequestCount { get; }
    public long CandidateGuardAdmittedCount { get; }
    public int CandidateAction { get; }
    public bool CandidateExecuted => CandidateExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
    public CortexPolicyDecisionID ForcedDecisionID { get; }
    public int ForcedAction { get; }
    public bool ForcedDiverged { get; }
    public LoopClosureDigest NullReceiptSHA256 { get; }
    public string DivergenceEvidenceBase64 { get; }

    public void Validate(bool requireCorroboration, IPolicyBoundaryDomain domain)
    {
        if (!requireCorroboration) return;
        if (!FoldNodeID.IsValid || FoldRevision.Value == 0 || !TeacherEvidenceSHA256.IsValid || !QuotaID.IsValid || !NullReceiptSHA256.IsValid
            || !Enum.IsDefined(CandidateExecutionOutcome) || CandidateRequestCount < 0 || CandidateGuardAdmittedCount < 0
            || CandidateGuardAdmittedCount > CandidateRequestCount || CandidateAction < -1
            || ForcedDecisionID.Value == 0 || ForcedAction < 0 || !ForcedDiverged)
            throw new InvalidDataException("thought-overruled-instinct verdict omits its teacher/payment/forced-execution corroboration");
        if ((!CandidateExecuted && CandidateAction != -1)
            || (CandidateExecuted && CandidateAction < 0))
            throw new InvalidDataException("thought-overruled-instinct candidate action does not match its typed terminal status");
        if (string.IsNullOrWhiteSpace(DivergenceEvidenceBase64))
            throw new InvalidDataException("thought-overruled-instinct verdict omits its typed divergence audit");
        PolicyBoundaryDivergenceAdjudication adjudication;
        ArgumentNullException.ThrowIfNull(domain);
        try { adjudication = LoopClosureDivergenceEvidence.Decode(Convert.FromBase64String(DivergenceEvidenceBase64), LoopClosureEvidenceStore.ResolveRegisteredDomain(domain)); }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        { throw new InvalidDataException("thought-overruled-instinct verdict carries invalid typed divergence audit", ex); }
        if (adjudication.Proof.Teacher is not PolicyBoundaryTeacherCorroboration teacher
            || teacher.FoldNodeID != FoldNodeID
            || teacher.FoldRevision != FoldRevision
            || teacher.EvidenceSHA256 != TeacherEvidenceSHA256.Value
            || adjudication.Proof.Funding.QuotaDecisionID.ToString() != QuotaID.Value
            || adjudication.Proof.ReadoutRevision != DivergenceRevision
            || adjudication.Proof.LaunchpadAction != LaunchpadAction
            || adjudication.Proof.Candidate.Outcome != CandidateExecutionOutcome
            || adjudication.Proof.Candidate.RequestCount != CandidateRequestCount
            || adjudication.Proof.Candidate.GuardAdmittedCount != CandidateGuardAdmittedCount
            || adjudication.Proof.Candidate.ExecutedOutcome?.Action != (CandidateExecuted ? CandidateAction : null)
            || !adjudication.Proof.ForcedNull.DecisionID.Equals(ForcedDecisionID)
            || adjudication.Proof.ForcedNull.Action != ForcedAction
            || adjudication.Proof.ForcedNull.Diverged != ForcedDiverged
            || adjudication.Proof.ForcedNull.SelectionCause != CortexPolicySelectionCauses.TrialOverride
            || adjudication.Proof.ForcedNull.BehaviorallyExecuted != true
            || adjudication.Proof.ForcedNull.OutcomeID != NullReceiptSHA256)
            throw new InvalidDataException("thought-overruled-instinct typed divergence audit disagrees with its corroboration");
    }

    internal LoopClosureDigest ReadDivergenceEvidenceDigest(IPolicyBoundaryDomain domain)
        => LoopClosureDivergenceEvidence.Decode(Convert.FromBase64String(DivergenceEvidenceBase64), LoopClosureEvidenceStore.ResolveRegisteredDomain(domain)).EvidenceSHA256;
}

public readonly record struct ObjectLoopClosedCorroboration
{
    internal ObjectLoopClosedCorroboration(
        LoopLineageNodeID outcomeNodeID,
        LoopClosureDigest lineageSHA256,
        LoopClosureDigest theoryEvidenceSHA256 = default,
        LoopClosureDigest divergenceEvidenceSHA256 = default,
        long terminalOutcomeEventID = -1,
        LoopClosureChildOutcomeReference childOutcome = default)
    {
        OutcomeNodeID = outcomeNodeID;
        LineageSHA256 = lineageSHA256;
        PatternEvidenceSHA256 = theoryEvidenceSHA256;
        DivergenceEvidenceSHA256 = divergenceEvidenceSHA256;
        TerminalOutcomeEventID = terminalOutcomeEventID;
        ChildOutcome = childOutcome;
    }

    public LoopLineageNodeID OutcomeNodeID { get; }
    public LoopClosureDigest LineageSHA256 { get; }
    public LoopClosureDigest PatternEvidenceSHA256 { get; }
    public LoopClosureDigest DivergenceEvidenceSHA256 { get; }
    public long TerminalOutcomeEventID { get; }
    public LoopClosureChildOutcomeReference ChildOutcome { get; }

    public void Validate(bool requireCorroboration)
    {
        if (!requireCorroboration) return;
        if (!OutcomeNodeID.IsValid || !LineageSHA256.IsValid || !PatternEvidenceSHA256.IsValid || !DivergenceEvidenceSHA256.IsValid || TerminalOutcomeEventID < 0)
            throw new InvalidDataException("object-loop-closed verdict omits its outcome/null/lineage corroboration");
        ChildOutcome.Validate(required: false);
    }
}

/// A typed verdict hierarchy keeps each species' corroboration at its own boundary. There is
/// intentionally no catch-all detail/carrier: an adjudicator must choose one law-shaped
/// verdict and therefore cannot accidentally serialize fields from another arc.
public abstract record LoopClosureVerdict
{
    private protected LoopClosureVerdict(
        LoopClosureAssayStatuses assay,
        LoopClosurePowerStatuses power,
        LoopClosureVerdictStatuses status,
        LoopClosureDigest evidenceSHA256)
    {
        Assay = assay;
        Power = power;
        Status = status;
        EvidenceSHA256 = evidenceSHA256;
    }

    public LoopClosureAssayStatuses Assay { get; }
    public LoopClosurePowerStatuses Power { get; }
    public LoopClosureVerdictStatuses Status { get; }
    public LoopClosureDigest EvidenceSHA256 { get; }
    public abstract LoopClosureVerdictSpecies Species { get; }
    public abstract void Validate(IPolicyBoundaryDomain domain);
    protected void ValidateEnvelope()
    {
        if (!Enum.IsDefined(Assay) || !Enum.IsDefined(Power) || !Enum.IsDefined(Status))
            throw new InvalidDataException("loop-closure verdict carries an unknown typed status");
        if (!EvidenceSHA256.IsValid) throw new InvalidDataException("loop-closure verdict omits evidence digest");
        if (Status == LoopClosureVerdictStatuses.PASS && (Assay != LoopClosureAssayStatuses.Exact || Power != LoopClosurePowerStatuses.Powered))
            throw new InvalidDataException("loop-closure PASS requires exact powered evidence");
        if (Status == LoopClosureVerdictStatuses.BANKED_NULL && Power != LoopClosurePowerStatuses.Unpowered)
            throw new InvalidDataException("loop-closure BANKED_NULL requires an unpowered arc");
    }
}

public sealed record PatternBecameThoughtVerdict : LoopClosureVerdict
{
    internal PatternBecameThoughtVerdict(
        LoopClosureAssayStatuses assay,
        LoopClosurePowerStatuses power,
        LoopClosureVerdictStatuses status,
        LoopClosureDigest evidenceSHA256,
        PatternBecameThoughtCorroboration corroboration)
        : base(assay, power, status, evidenceSHA256) => Corroboration = corroboration;

    public PatternBecameThoughtCorroboration Corroboration { get; }
    public override LoopClosureVerdictSpecies Species => LoopClosureVerdictSpecies.PatternBecameThought;
    public override void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ValidateEnvelope();
        bool requireCorroboration = Status == LoopClosureVerdictStatuses.PASS;
        Corroboration.Validate(requireCorroboration);
        PatternBecameThoughtCorroboration corroboration = Corroboration;
        if (requireCorroboration && EvidenceSHA256 != LoopClosureEvidenceStore.DigestPattern(in corroboration))
            throw new InvalidDataException("theory-became-thought evidence digest does not match its typed corroboration");
    }
}

public sealed record ThoughtOverruledInstinctVerdict : LoopClosureVerdict
{
    internal ThoughtOverruledInstinctVerdict(
        LoopClosureAssayStatuses assay,
        LoopClosurePowerStatuses power,
        LoopClosureVerdictStatuses status,
        LoopClosureDigest evidenceSHA256,
        ThoughtOverruledInstinctCorroboration corroboration)
        : base(assay, power, status, evidenceSHA256) => Corroboration = corroboration;

    public ThoughtOverruledInstinctCorroboration Corroboration { get; }
    public override LoopClosureVerdictSpecies Species => LoopClosureVerdictSpecies.ThoughtOverruledInstinct;
    public override void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        bool requireCorroboration = Status == LoopClosureVerdictStatuses.PASS;
        ValidateEnvelope();
        Corroboration.Validate(requireCorroboration, domain);
        if (requireCorroboration && EvidenceSHA256 != Corroboration.ReadDivergenceEvidenceDigest(domain))
            throw new InvalidDataException("thought-overruled-instinct evidence digest does not match its typed custody");
    }
}

public sealed record ObjectLoopClosedVerdict : LoopClosureVerdict
{
    internal ObjectLoopClosedVerdict(
        LoopClosureAssayStatuses assay,
        LoopClosurePowerStatuses power,
        LoopClosureVerdictStatuses status,
        LoopClosureDigest evidenceSHA256,
        ObjectLoopClosedCorroboration corroboration)
        : base(assay, power, status, evidenceSHA256) => Corroboration = corroboration;

    public ObjectLoopClosedCorroboration Corroboration { get; }
    public override LoopClosureVerdictSpecies Species => LoopClosureVerdictSpecies.ObjectLoopClosed;
    public override void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ValidateEnvelope();
        Corroboration.Validate(Status == LoopClosureVerdictStatuses.PASS);
    }
}

public readonly record struct LoopClosurePairLineVerdict
{
    internal LoopClosurePairLineVerdict(
        string name,
        LoopClosureAssayStatuses assay,
        LoopClosurePowerStatuses power,
        LoopClosureVerdictStatuses status,
        LoopClosureDigest evidenceSHA256)
    {
        Name = name;
        Assay = assay;
        Power = power;
        Status = status;
        EvidenceSHA256 = evidenceSHA256;
    }

    public string Name { get; }
    public LoopClosureAssayStatuses Assay { get; }
    public LoopClosurePowerStatuses Power { get; }
    public LoopClosureVerdictStatuses Status { get; }
    public LoopClosureDigest EvidenceSHA256 { get; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Assay) || !Enum.IsDefined(Power) || !Enum.IsDefined(Status) || !EvidenceSHA256.IsValid)
            throw new InvalidDataException("loop-closure paired line is malformed");
    }
}

public readonly record struct LoopClosureArmReport(
    string RunID,
    string ConfigFingerprint,
    string WorldSHA256,
    string AuthoritySHA256,
    string CheckpointSHA256,
    string ClosureSHA256,
    string BinarySHA256,
    int NextStep)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RunID) || NextStep != LoopClosureRegistration.RegisteredHorizon) throw new InvalidDataException("loop-closure arm identity is not sealed at the registered horizon");
        foreach (string digest in new[] { ConfigFingerprint, WorldSHA256, AuthoritySHA256, CheckpointSHA256, ClosureSHA256, BinarySHA256 })
            if (!IsDigest(digest)) throw new InvalidDataException("loop-closure arm identity omits a digest");
    }
    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

/// Immutable aggregation of the in-run organic comparison stream.  The
/// receipt stream is the authority for these counts; no runtime counter is
/// accepted as a substitute.  `SourceAuthoritySHA256` binds the stream to the
/// sealed LIVE arm that produced it.
public readonly record struct OrganicComparisonSummary(
    LoopClosureDigest SourceAuthoritySHA256,
    LoopClosureDigest StreamSHA256,
    IReadOnlyList<OrganicComparisonReceipt> Receipts,
    int EligibleDecisions,
    int Comparisons,
    int FundingDenied,
    int CompletedNoMatch,
    int CandidateAgreements,
    int CandidateDivergences)
{
    public bool IsPresent => SourceAuthoritySHA256.IsValid || StreamSHA256.IsValid || Receipts is { Count: > 0 }
        || EligibleDecisions != 0 || Comparisons != 0 || FundingDenied != 0 || CompletedNoMatch != 0
        || CandidateAgreements != 0 || CandidateDivergences != 0;

    public void Validate(bool required)
    {
        if (!required && !IsPresent) return;
        if (!SourceAuthoritySHA256.IsValid || !StreamSHA256.IsValid || Receipts is null)
            throw new InvalidDataException("organic comparison summary omits its source authority or stream digest");
        OrganicComparisonReceipt[] receipts = Receipts.ToArray();
        if (required && receipts.Length == 0)
            throw new InvalidDataException("organic comparison summary omits every sealed receipt");
        if (receipts.Length != EligibleDecisions)
            throw new InvalidDataException("organic comparison summary eligible count does not match its receipt stream");
        if (EligibleDecisions != FundingDenied + CompletedNoMatch + CandidateAgreements + CandidateDivergences)
            throw new InvalidDataException("organic comparison summary outcomes do not conserve eligible decisions");
        if (Comparisons != CandidateAgreements + CandidateDivergences)
            throw new InvalidDataException("organic comparison summary comparisons do not conserve candidate outcomes");
        if (receipts.Select(static receipt => receipt.DecisionID.Value).Distinct().Count() != receipts.Length
            || receipts.Select(static receipt => receipt.SourceDecisionEventID.Value).Distinct().Count() != receipts.Length
            || !receipts.Select(static receipt => receipt.SourceDecisionEventID.Value)
                .SequenceEqual(receipts.Select(static receipt => receipt.SourceDecisionEventID.Value).OrderBy(static id => id)))
            throw new InvalidDataException("organic comparison summary receipt stream is duplicate or noncanonical");
        foreach (OrganicComparisonReceipt receipt in receipts) receipt.Validate();
        int fundingDenied = receipts.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.ReadoutQuotaDenied);
        int completedNoMatch = receipts.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch);
        int agreements = receipts.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.CandidateAgreement);
        int divergences = receipts.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.CandidateDivergence);
        if (fundingDenied != FundingDenied || completedNoMatch != CompletedNoMatch
            || agreements != CandidateAgreements || divergences != CandidateDivergences)
            throw new InvalidDataException("organic comparison summary counters disagree with typed receipt outcomes");
        if (StreamSHA256.Value != ComputeStreamSHA256(SourceAuthoritySHA256, receipts))
            throw new InvalidDataException("organic comparison summary stream digest does not match canonical receipts");
    }

    internal static OrganicComparisonSummary Create(
        LoopClosureDigest sourceAuthoritySHA256,
        IReadOnlyList<OrganicComparisonReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        OrganicComparisonReceipt[] ordered = receipts.OrderBy(static receipt => receipt.SourceDecisionEventID.Value).ToArray();
        if (ordered.Length == 0)
            throw new InvalidDataException("organic comparison summary omits every sealed receipt");
        int fundingDenied = ordered.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.ReadoutQuotaDenied);
        int completedNoMatch = ordered.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch);
        int agreements = ordered.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.CandidateAgreement);
        int divergences = ordered.Count(static receipt => receipt.Outcome == OrganicComparisonOutcomeKinds.CandidateDivergence);
        OrganicComparisonSummary summary = new(sourceAuthoritySHA256,
            new LoopClosureDigest(ComputeStreamSHA256(sourceAuthoritySHA256, ordered)), ordered,
            ordered.Length, agreements + divergences, fundingDenied, completedNoMatch, agreements, divergences);
        summary.Validate(required: true);
        return summary;
    }

    internal static string ComputeStreamSHA256(
        LoopClosureDigest sourceAuthoritySHA256,
        IReadOnlyList<OrganicComparisonReceipt> receipts)
    {
        string canonical = string.Join('|', "loop-closure-organic-comparison-stream-v1", sourceAuthoritySHA256.Value,
            string.Join(',', receipts.Select(static receipt => receipt.CanonicalReceiptSHA256)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public abstract record LoopClosureLineageNullOutcome
{
    public abstract bool IsExecuted { get; }
    public abstract void Validate();
}

public sealed record LoopClosureLineageNullMissing : LoopClosureLineageNullOutcome
{
    internal LoopClosureLineageNullMissing(string reason) => Reason = reason;
    public string Reason { get; }
    public override bool IsExecuted => false;
    public override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reason)) throw new InvalidDataException("loop-closure lineage null absence omits its reason");
    }
}

public sealed record LoopClosureLineageNullExecuted : LoopClosureLineageNullOutcome
{
    internal LoopClosureLineageNullExecuted(LoopLineageShuffledNullReceipt receipt) => Receipt = receipt;
    public LoopLineageShuffledNullReceipt Receipt { get; }
    public override bool IsExecuted => true;
    public override void Validate() { Receipt.Validate(); }
}

/// Versioned report owner for `gate loop-closure`. The old PairedGateReport remains
/// a separate schema-1 owner and is never decoded through this type.
public sealed class LoopClosureReport
{
    public const int SchemaVersion = 3;
    private const int LegacySchemaVersion = 2;
    private const string MissingLineageReason = "lineage null evidence was absent or invalid";
    private static readonly string[] PairLineNames = ["vocabulary", "efficiency", "derivation", "inference", "vow", "zero-dark", "organism"];
    private readonly bool _sourceBacked;
    private readonly IPolicyBoundaryDomain _policyDomain;
    private byte[]? _legacyEncoded;

    private LoopClosureReport(LoopClosureReportRON document, IPolicyBoundaryDomain domain)
    {
        _sourceBacked = false;
        _policyDomain = domain ?? throw new ArgumentNullException(nameof(domain));
        SchemaVersionValue = document.schemaVersion;
        RegistrationSHA256 = document.registrationSHA256;
        Live = ReadArm(document.live);
        Control = ReadArm(document.control);
        Lines = new ReadOnlyCollection<LoopClosurePairLineVerdict>(document.lines.Select(ReadLine).ToArray());
        Verdicts = new ReadOnlyCollection<LoopClosureVerdict>([ReadVerdict(document.theory_became_thought), ReadVerdict(document.thought_overruled_instinct), ReadVerdict(document.object_loop_closed)]);
        LinkContractValue = ReadLinkContract(document.linkReceipts, document.gateLiveness);
        OrganicComparisons = ReadOrganicComparisons(document.organicComparisons);
        LineageNull = ReadNull(document.lineageNull);
        Outcome = document.outcome;
        ArtifactNameValue = document.artifactName;
        Digest = document.digest;
    }

    private LoopClosureReport(
        string registrationSHA256,
        LoopClosureArmReport live,
        LoopClosureArmReport control,
        IReadOnlyList<LoopClosurePairLineVerdict> lines,
        IReadOnlyList<LoopClosureVerdict> verdicts,
        LoopClosureLineageNullOutcome lineageNull,
        string outcome,
        LoopClosureLinkContract? linkContract,
        OrganicComparisonSummary? organicComparisons,
        bool sourceBacked,
        IPolicyBoundaryDomain domain)
    {
        _sourceBacked = sourceBacked;
        _policyDomain = domain ?? throw new ArgumentNullException(nameof(domain));
        SchemaVersionValue = SchemaVersion;
        RegistrationSHA256 = registrationSHA256;
        Live = live;
        Control = control;
        Lines = new ReadOnlyCollection<LoopClosurePairLineVerdict>(lines.ToArray());
        Verdicts = new ReadOnlyCollection<LoopClosureVerdict>(verdicts.ToArray());
        LinkContractValue = linkContract;
        OrganicComparisons = organicComparisons;
        LineageNull = lineageNull;
        Outcome = outcome;
        ArtifactNameValue = CanMintClosureCertificate ? "BirthCertificate" : "LoopClosureReport";
        Digest = ComputeDigest(this);
    }

    public string RegistrationSHA256 { get; }
    public int SchemaVersionValue { get; }
    public LoopClosureArmReport Live { get; }
    public LoopClosureArmReport Control { get; }
    public IReadOnlyList<LoopClosurePairLineVerdict> Lines { get; }
    public IReadOnlyList<LoopClosureVerdict> Verdicts { get; }
    public LoopClosureLinkContract? LinkContractValue { get; }
    public OrganicComparisonSummary? OrganicComparisons { get; }
    public IReadOnlyList<LoopClosureLinkReceipt> LinkReceipts => LinkContractValue?.Receipts ?? Array.Empty<LoopClosureLinkReceipt>();
    public IReadOnlyList<LoopClosureGateLiveness> GateLiveness => LinkContractValue?.Liveness ?? Array.Empty<LoopClosureGateLiveness>();
    public LoopClosureLineageNullOutcome LineageNull { get; }
    public string Outcome { get; }
    public string Digest { get; }
    // A decoded report proves its persisted bytes, but not that this process
    // assembled the evidence from the sealed arms; only the latter may mint.
    internal bool IsSourceBacked => _sourceBacked;
    // Keep the persisted spelling for validation; callers see only the title
    // authorized by the current in-memory evidence and source authority.
    private string ArtifactNameValue { get; }
    public string ArtifactName => CanMintClosureCertificate ? "BirthCertificate" : "LoopClosureReport";
    public bool CanMintClosureCertificate
    {
        get
        {
            bool pairedLinesPass = Lines.Count == PairLineNames.Length && Lines.All(static line => line.Status == LoopClosureVerdictStatuses.PASS
                && line.Assay == LoopClosureAssayStatuses.Exact && line.Power == LoopClosurePowerStatuses.Powered);
            bool verdictStatusesPass = Verdicts.Count == 3 && Verdicts.All(static verdict => verdict.Status == LoopClosureVerdictStatuses.PASS);
            bool lineageNullPass = LineageNull is LoopClosureLineageNullExecuted;
            if (!pairedLinesPass || !verdictStatusesPass || !lineageNullPass) return false;
            LoopLineageShuffledNullReceipt nullReceipt = ((LoopClosureLineageNullExecuted)LineageNull).Receipt;
            lineageNullPass = nullReceipt.OriginalStatus == LoopLineageOccurrenceCheckStatuses.PASS
                && nullReceipt.ShuffledStatus == LoopLineageOccurrenceCheckStatuses.FAIL
                && nullReceipt.SameEvents && nullReceipt.SamePayloads && nullReceipt.Derangement
                && nullReceipt.EligibleBucketCount >= 1 && nullReceipt.SwappedEdgeCount > 0
                && nullReceipt.OriginalLineageSHA256 != nullReceipt.ShuffledLineageSHA256
                && nullReceipt.FirstDiscriminatingEdge.IsValid;
            if (!lineageNullPass) return false;
            if (LinkContractValue is not LoopClosureLinkContract linkContract) return false;
            bool linkContractPass = true;
            try { linkContract.Validate(requireComplete: true); }
            catch (InvalidDataException) { return false; }
            bool organicComparisonsPass = false;
            if (OrganicComparisons is not { } organicComparisons) return false;
            try { organicComparisons.Validate(required: true); }
            catch (InvalidDataException) { return false; }
            organicComparisonsPass = true;
            LoopClosureLinkReceipt executedReceipt = linkContract.Receipts[^1];
            bool childOutcomePass;
            try { executedReceipt.ChildOutcome.Validate(required: true); }
            catch (InvalidDataException) { return false; }
            childOutcomePass = true;
            PatternBecameThoughtVerdict[] theories = Verdicts.OfType<PatternBecameThoughtVerdict>().ToArray();
            ThoughtOverruledInstinctVerdict[] divergences = Verdicts.OfType<ThoughtOverruledInstinctVerdict>().ToArray();
            ObjectLoopClosedVerdict[] closedVerdicts = Verdicts.OfType<ObjectLoopClosedVerdict>().ToArray();
            bool speciesPass = theories.Length == 1 && divergences.Length == 1 && closedVerdicts.Length == 1;
            if (!speciesPass) return false;
            PatternBecameThoughtVerdict theory = theories[0];
            ThoughtOverruledInstinctVerdict divergence = divergences[0];
            ObjectLoopClosedVerdict closed = closedVerdicts[0];
            PatternBecameThoughtCorroboration theoryCorroboration = theory.Corroboration;
            bool corroborationDigestsPass;
            try
            {
                corroborationDigestsPass = theory.EvidenceSHA256 == LoopClosureEvidenceStore.DigestPattern(in theoryCorroboration)
                    && divergence.EvidenceSHA256 == divergence.Corroboration.ReadDivergenceEvidenceDigest(_policyDomain);
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
            {
                return false;
            }
            ObjectLoopClosedCorroboration corroboration = closed.Corroboration;
            bool lineageBindingPass = corroboration.LineageSHA256.Value == nullReceipt.OriginalLineageSHA256
                && corroboration.PatternEvidenceSHA256 == theory.EvidenceSHA256
                && corroboration.DivergenceEvidenceSHA256 == divergence.EvidenceSHA256
                && corroboration.TerminalOutcomeEventID >= 0
                && corroboration.ChildOutcome == executedReceipt.ChildOutcome;
            return EvaluateClosureCertificateEligibility(sourceBacked: _sourceBacked, pairedLinesPass: pairedLinesPass,
                verdictStatusesPass: verdictStatusesPass, lineageNullPass: lineageNullPass, linkContractPass: linkContractPass,
                organicComparisonsPass: organicComparisonsPass, childOutcomePass: childOutcomePass,
                speciesPass: speciesPass, corroborationDigestsPass: corroborationDigestsPass, lineageBindingPass: lineageBindingPass);
        }
    }

    private static bool EvaluateClosureCertificateEligibility(
        bool sourceBacked,
        bool pairedLinesPass,
        bool verdictStatusesPass,
        bool lineageNullPass,
        bool linkContractPass,
        bool organicComparisonsPass,
        bool childOutcomePass,
        bool speciesPass,
        bool corroborationDigestsPass,
        bool lineageBindingPass)
        => sourceBacked && pairedLinesPass && verdictStatusesPass && lineageNullPass
            && linkContractPass && organicComparisonsPass && childOutcomePass && speciesPass
            && corroborationDigestsPass && lineageBindingPass;

    internal static LoopClosureReport Create(
        string registrationSHA256,
        LoopClosureArmReport live,
        LoopClosureArmReport control,
        IReadOnlyList<LoopClosurePairLineVerdict> lines,
        IReadOnlyList<LoopClosureVerdict> verdicts,
        LoopClosureLineageNullOutcome lineageNull,
        string outcome,
        IPolicyBoundaryDomain domain,
        LoopClosureLinkContract? linkContract = null,
        OrganicComparisonSummary? organicComparisons = null)
    {
        LoopClosureReport report = new(registrationSHA256, live, control, lines, verdicts, lineageNull, outcome, linkContract, organicComparisons, sourceBacked: true, domain);
        report.Validate();
        return report;
    }

    public void Validate()
    {
        if (SchemaVersionValue is not (LegacySchemaVersion or SchemaVersion)) throw new InvalidDataException("loop-closure report schema is unsupported");
        if (!IsDigest(RegistrationSHA256) || string.IsNullOrWhiteSpace(Outcome)) throw new InvalidDataException("loop-closure report identity is malformed");
        if (!string.Equals(ArtifactNameValue, CanMintClosureCertificate ? "BirthCertificate" : "LoopClosureReport", StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure report artifact name does not match its typed verdict gate");
        Live.Validate(); Control.Validate(); LineageNull.Validate();
        if (Lines.Count != PairLineNames.Length || !Lines.Select(static line => line.Name).SequenceEqual(PairLineNames, StringComparer.Ordinal))
            throw new InvalidDataException("loop-closure report does not carry the seven registered paired lines");
        foreach (LoopClosurePairLineVerdict line in Lines) line.Validate();
        if (Verdicts.Count != 3 || Verdicts.Select(static verdict => verdict.Species).Distinct().Count() != 3)
            throw new InvalidDataException("loop-closure report does not carry all three distinct verdict species");
        foreach (LoopClosureVerdict verdict in Verdicts) verdict.Validate(_policyDomain);
        if (LinkContractValue is not null) LinkContractValue.Validate(requireComplete: false);
        if (SchemaVersionValue == SchemaVersion)
        {
            if (OrganicComparisons is not { } organicComparisons)
                throw new InvalidDataException("fresh loop-closure report omits its organic comparison stream");
            organicComparisons.Validate(required: true);
            if (!string.Equals(organicComparisons.SourceAuthoritySHA256.Value, Live.AuthoritySHA256, StringComparison.Ordinal))
                throw new InvalidDataException("organic comparison stream is not bound to the LIVE arm authority");
        }
        if (!IsDigest(Digest) || SchemaVersionValue == SchemaVersion && !string.Equals(Digest, ComputeDigest(this), StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure report digest does not match its typed payload");
    }

    public byte[] Encode()
    {
        Validate();
        if (SchemaVersionValue == LegacySchemaVersion && _legacyEncoded is not null) return _legacyEncoded.ToArray();
        byte[] first = EncodeDocument(Digest); byte[] second = EncodeDocument(Digest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("loop-closure report RON encoding is nondeterministic");
        return first;
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string output = Path.GetFullPath(path);
        byte[] encoded = Encode();
        if (File.Exists(output))
        {
            if (!File.ReadAllBytes(output).AsSpan().SequenceEqual(encoded))
                throw new IOException($"loop-closure report already exists with different bytes: {output}");
            return;
        }
        if (Directory.Exists(output)) throw new IOException($"loop-closure report destination is a directory: {output}");
        string? parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(output, encoded);
    }

    public static LoopClosureReport Load(string path, IPolicyBoundaryDomain domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string input = Path.GetFullPath(path);
        if (!File.Exists(input)) throw new FileNotFoundException("loop-closure report is missing", input);
        if (Directory.Exists(input)) throw new IOException($"loop-closure report is a directory: {input}");
        return Decode(File.ReadAllBytes(input), domain);
    }

    public static LoopClosureReport Decode(ReadOnlySpan<byte> bytes, IPolicyBoundaryDomain domain)
    {
        LoopClosureReport report = new(RonSerializer.Deserialize<LoopClosureReportRON>(bytes), domain);
        if (report.SchemaVersionValue == LegacySchemaVersion) report._legacyEncoded = bytes.ToArray();
        report.Validate();
        if (!report.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("loop-closure report RON round-trip changed bytes");
        return report;
    }

    internal static bool VerifyPatternCorroborationRonFixture()
    {
        PatternBecameThoughtCorroboration corroboration = new(
            new EmlPredictionID(0), new EmlPredictionID(1), new LoopLineageNodeID("derivation-fixture"),
            new(new string('a', 64)), new(new string('b', 64)), 0, 1,
            EmlObligationTargetSpecies.ExactComposition, [new TapeEventID(2), new TapeEventID(10)],
            ["x = y\u0001000000000000000A\u0001claim", "y = x\u0001000000000000000B\u0001claim"]);
        PatternBecameThoughtVerdict source = new(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Powered,
            LoopClosureVerdictStatuses.PASS, LoopClosureEvidenceStore.DigestPattern(in corroboration), corroboration);
        source.Validate(HomeostatPolicyBoundaryDomain.Instance);
        LoopClosureVerdictRON encoded = WriteVerdict(source);
        LoopClosureVerdictRON restoredDocument = RonSerializer.Deserialize<LoopClosureVerdictRON>(RonSerializer.SerializeToUtf8(in encoded));
        LoopClosureVerdict restored = ReadVerdict(restoredDocument);
        restored.Validate(HomeostatPolicyBoundaryDomain.Instance);
        bool exact = restored is PatternBecameThoughtVerdict theory
            && theory.Corroboration.TargetSpecies == corroboration.TargetSpecies
            && theory.Corroboration.SupportEventIDs.SequenceEqual(corroboration.SupportEventIDs)
            && theory.Corroboration.BasisLawAdmissionIDs.SequenceEqual(corroboration.BasisLawAdmissionIDs);
        LoopClosureVerdictRON supportSwap = RonSerializer.Deserialize<LoopClosureVerdictRON>(RonSerializer.SerializeToUtf8(in encoded));
        supportSwap.supportEventIDs[0] = 4;
        bool supportRejected = Rejects(() => ReadVerdict(supportSwap).Validate(HomeostatPolicyBoundaryDomain.Instance));
        LoopClosureVerdictRON lawSwap = RonSerializer.Deserialize<LoopClosureVerdictRON>(RonSerializer.SerializeToUtf8(in encoded));
        lawSwap.basisLawAdmissionIDs[1] = "y = x\u0001000000000000000C\u0001claim";
        bool lawRejected = Rejects(() => ReadVerdict(lawSwap).Validate(HomeostatPolicyBoundaryDomain.Instance));
        bool linkRon = LoopClosureLinkContract.VerifyRonFixture();
        bool attemptCodec = LoopClosureLinkAttemptStore.VerifyCodecFixture();
        bool attemptCustody = LoopClosureLinkAttemptStore.VerifyCustodyFixture();

        string fixtureDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("loop-closure-report-fixture")));
        LoopClosureArmReport fixtureArm = new("fixture-arm", fixtureDigest, fixtureDigest, fixtureDigest, fixtureDigest, fixtureDigest, fixtureDigest,
            LoopClosureRegistration.RegisteredHorizon);
        LoopClosurePairLineVerdict[] fixtureLines = PairLineNames
            .Select(name => new LoopClosurePairLineVerdict(name, LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Unpowered,
                LoopClosureVerdictStatuses.BANKED_NULL, new LoopClosureDigest(fixtureDigest))).ToArray();
        LoopClosureDigest fixtureEvidence = new(fixtureDigest);
        PatternBecameThoughtCorroboration fixturePatternCorroboration = new(
            new EmlPredictionID(0), new EmlPredictionID(0), new LoopLineageNodeID("fixture"), fixtureEvidence, fixtureEvidence,
            0, 0, EmlObligationTargetSpecies.Residual, [], []);
        ThoughtOverruledInstinctCorroboration fixtureDivergenceCorroboration = new(
            new LoopLineageNodeID("fixture"), new GrammarRevisionID(0), default, default, new GrammarRevisionID(0),
            -1, CortexPolicyTrialExecutionOutcomes.NotAttempted, 0, 0, -1, default, -1, false, default, "");
        LoopClosureVerdict[] fixtureVerdicts =
        [
            new PatternBecameThoughtVerdict(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Unpowered,
                LoopClosureVerdictStatuses.BANKED_NULL, fixtureEvidence, fixturePatternCorroboration),
            new ThoughtOverruledInstinctVerdict(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Unpowered,
                LoopClosureVerdictStatuses.BANKED_NULL, fixtureEvidence, fixtureDivergenceCorroboration),
            new ObjectLoopClosedVerdict(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Unpowered,
                LoopClosureVerdictStatuses.BANKED_NULL, fixtureEvidence, default),
        ];
        OrganicComparisonReceipt fixtureComparison = new(
            0, Homeostat.PolicyID, new CortexPolicyDecisionID(1), new TapeEventID(0), fixtureDigest, fixtureDigest,
            new GrammarRevisionID(1), 1, 1, 1, 0, 0, 0, OrganicComparisonOutcomeKinds.CandidateAgreement,
            null, null, "", "", "");
        fixtureComparison = fixtureComparison with
        { CanonicalReceiptSHA256 = OrganicComparisonReceipt.ComputeCanonicalReceiptSHA256(in fixtureComparison) };
        OrganicComparisonReceipt laterFixtureComparison = fixtureComparison with
        { DecisionID = new CortexPolicyDecisionID(2), SourceDecisionEventID = new TapeEventID(1), CanonicalReceiptSHA256 = "" };
        laterFixtureComparison = laterFixtureComparison with
        { CanonicalReceiptSHA256 = OrganicComparisonReceipt.ComputeCanonicalReceiptSHA256(in laterFixtureComparison) };
        OrganicComparisonSummary fixtureComparisons = OrganicComparisonSummary.Create(fixtureEvidence, [fixtureComparison, laterFixtureComparison]);
        OrganicComparisonSummary permutedFixtureComparisons = OrganicComparisonSummary.Create(fixtureEvidence, [laterFixtureComparison, fixtureComparison]);
        OrganicComparisonReceipt duplicateSource = fixtureComparison with { DecisionID = new CortexPolicyDecisionID(2), CanonicalReceiptSHA256 = "" };
        duplicateSource = duplicateSource with
        { CanonicalReceiptSHA256 = OrganicComparisonReceipt.ComputeCanonicalReceiptSHA256(in duplicateSource) };
        bool organicPermutationAccepted = fixtureComparisons.StreamSHA256 == permutedFixtureComparisons.StreamSHA256
            && fixtureComparisons.Receipts.SequenceEqual(permutedFixtureComparisons.Receipts);
        bool organicDuplicateRejected = Rejects(() => OrganicComparisonSummary.Create(fixtureEvidence, [fixtureComparison, duplicateSource]));
        LoopClosureReport report = Create(fixtureDigest, fixtureArm, fixtureArm, fixtureLines, fixtureVerdicts,
            new LoopClosureLineageNullMissing("fixture does not carry a lineage null"), "schema fixture",
            HomeostatPolicyBoundaryDomain.Instance, organicComparisons: fixtureComparisons);
        byte[] reportBytes = report.Encode();
        LoopClosureReport restoredReport = Decode(reportBytes, HomeostatPolicyBoundaryDomain.Instance);
        bool reportV2RoundTrip = reportBytes.AsSpan().SequenceEqual(restoredReport.Encode());
        const bool allOtherTitleRequirementsPass = true;
        bool sourceMutationGate = !EvaluateClosureCertificateEligibility(sourceBacked: false,
                pairedLinesPass: allOtherTitleRequirementsPass, verdictStatusesPass: allOtherTitleRequirementsPass,
                lineageNullPass: allOtherTitleRequirementsPass, linkContractPass: allOtherTitleRequirementsPass,
                organicComparisonsPass: allOtherTitleRequirementsPass, childOutcomePass: allOtherTitleRequirementsPass,
                speciesPass: allOtherTitleRequirementsPass, corroborationDigestsPass: allOtherTitleRequirementsPass,
                lineageBindingPass: allOtherTitleRequirementsPass)
            && EvaluateClosureCertificateEligibility(sourceBacked: true,
                pairedLinesPass: allOtherTitleRequirementsPass, verdictStatusesPass: allOtherTitleRequirementsPass,
                lineageNullPass: allOtherTitleRequirementsPass, linkContractPass: allOtherTitleRequirementsPass,
                organicComparisonsPass: allOtherTitleRequirementsPass, childOutcomePass: allOtherTitleRequirementsPass,
                speciesPass: allOtherTitleRequirementsPass, corroborationDigestsPass: allOtherTitleRequirementsPass,
                lineageBindingPass: allOtherTitleRequirementsPass);
        bool decodedSourceGate = !restoredReport.IsSourceBacked
            && !restoredReport.CanMintClosureCertificate
            && restoredReport.ArtifactName == "LoopClosureReport";
        LoopClosureReportRON legacyDocument = RonSerializer.Deserialize<LoopClosureReportRON>(reportBytes);
        legacyDocument.schemaVersion = 1;
        bool reportV1Rejected = Rejects(() => Decode(RonSerializer.SerializeToUtf8(in legacyDocument), HomeostatPolicyBoundaryDomain.Instance));
        // A fused verdict names nothing when it fails. Each clause reports itself, so a red
        // fixture points at the one contract that broke instead of at the whole file.
        bool species = encoded.targetSpecies == EmlObligationTargetSpecies.ExactComposition.ToString();
        Console.WriteLine($"  theory-corroboration ron · exact={exact} species={species} support-swap-rejected={supportRejected}"
            + $" law-swap-rejected={lawRejected} link-ron={linkRon} attempt-codec={attemptCodec} attempt-custody={attemptCustody}"
            + $" organic-permutation={organicPermutationAccepted} organic-duplicate-rejected={organicDuplicateRejected}"
            + $" v2-roundtrip={reportV2RoundTrip} v1-rejected={reportV1Rejected} source-mutation={sourceMutationGate}"
            + $" decoded-source={decodedSourceGate}");
        return exact && species && supportRejected && lawRejected
            && linkRon && attemptCodec && attemptCustody && organicPermutationAccepted && organicDuplicateRejected
            && reportV2RoundTrip && reportV1Rejected && sourceMutationGate && decodedSourceGate;
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static string ComputeDigest(LoopClosureReport report)
        => Convert.ToHexStringLower(SHA256.HashData(report.EncodeDocument("")));

    private byte[] EncodeDocument(string digest)
    {
        LoopClosureReportRON document = new()
        {
            schemaVersion = SchemaVersionValue, registrationSHA256 = RegistrationSHA256, live = WriteArm(Live), control = WriteArm(Control),
            lineageNull = WriteNull(LineageNull), outcome = Outcome, artifactName = ArtifactNameValue, digest = digest,
        };
        if (LinkContractValue is not null)
        {
            document.linkReceipts = LinkContractValue.Receipts.Select(WriteLinkReceipt).ToList();
            document.gateLiveness = LinkContractValue.Liveness.Select(WriteGateLiveness).ToList();
        }
        if (OrganicComparisons is { } organicComparisons)
        {
            organicComparisons.Validate(required: true);
            document.organicComparisons = WriteOrganicComparisons(organicComparisons);
        }
        foreach (LoopClosurePairLineVerdict line in Lines)
            document.lines.Add(new() { name = line.Name, assay = line.Assay.ToString(), power = line.Power.ToString(), status = line.Status.ToString(), evidenceSHA256 = line.EvidenceSHA256.Value });
        foreach (LoopClosureVerdict verdict in Verdicts)
        {
            LoopClosureVerdictRON encoded = WriteVerdict(verdict);
            switch (verdict.Species)
            {
                case LoopClosureVerdictSpecies.PatternBecameThought: document.theory_became_thought = encoded; break;
                case LoopClosureVerdictSpecies.ThoughtOverruledInstinct: document.thought_overruled_instinct = encoded; break;
                case LoopClosureVerdictSpecies.ObjectLoopClosed: document.object_loop_closed = encoded; break;
            }
        }
        return RonSerializer.SerializeToUtf8(in document);
    }

    private static LoopClosureArmReport ReadArm(LoopClosureArmReportRON arm)
        => new(arm.runID, arm.configFingerprint, arm.worldSHA256, arm.authoritySHA256, arm.checkpointSHA256, arm.closureSHA256, arm.binarySHA256, arm.nextStep);
    private static LoopClosureArmReportRON WriteArm(LoopClosureArmReport arm)
        => new() { runID = arm.RunID, configFingerprint = arm.ConfigFingerprint, worldSHA256 = arm.WorldSHA256, authoritySHA256 = arm.AuthoritySHA256, checkpointSHA256 = arm.CheckpointSHA256, closureSHA256 = arm.ClosureSHA256, binarySHA256 = arm.BinarySHA256, nextStep = arm.NextStep };
    private static LoopClosurePairLineVerdict ReadLine(LoopClosurePairLineVerdictRON line)
        => new(line.name, Parse<LoopClosureAssayStatuses>(line.assay), Parse<LoopClosurePowerStatuses>(line.power), Parse<LoopClosureVerdictStatuses>(line.status), new LoopClosureDigest(line.evidenceSHA256));
    private static LoopClosureLinkContract? ReadLinkContract(
        IReadOnlyList<LoopClosureLinkReceiptRON> receipts,
        IReadOnlyList<LoopClosureGateLivenessRON> liveness)
    {
        if (receipts.Count == 0 && liveness.Count == 0) return null;
        LoopClosureLinkReceipt[] decodedReceipts = receipts.Select(static receipt => new LoopClosureLinkReceipt(
            Parse<LoopClosureLinkSpecies>(receipt.species), Parse<LoopClosureLinkPaths>(receipt.path), Parse<LoopClosureLinkStates>(receipt.state),
            new(receipt.evidenceSHA256), new(receipt.predecessorEvidenceSHA256), receipt.evidenceEventID,
            new LoopClosureChildOutcomeReference(receipt.childOutcomeRunID, receipt.childOutcomeRelativePath,
                new(receipt.childOutcomeAuthoritySHA256), new(receipt.childOutcomeRailSHA256),
                new CortexPolicyDecisionID(receipt.childOutcomeForcedDecisionID), new TapeEventID(receipt.childOutcomeEventID),
                new(receipt.childOutcomePayloadSHA256), receipt.childOutcomeBeforeSeal))).ToArray();
        LoopClosureGateLiveness[] decodedLiveness = liveness.Select(static meter =>
            new LoopClosureGateLiveness(Parse<LoopClosureLinkSpecies>(meter.species), meter.reached, meter.admitted, meter.denied,
                meter.denialReasons.Select(static denial => new LoopClosureGateDenial(Parse<LoopClosureGateDenialReasons>(denial.reason), denial.count)).ToArray(),
                new(meter.meterSHA256))).ToArray();
        return new LoopClosureLinkContract(decodedReceipts, decodedLiveness);
    }
    private static LoopClosureLinkReceiptRON WriteLinkReceipt(LoopClosureLinkReceipt receipt)
        => new() { species = receipt.Species.ToString(), path = receipt.Path.ToString(), state = receipt.State.ToString(), evidenceSHA256 = receipt.EvidenceSHA256.Value, predecessorEvidenceSHA256 = receipt.PredecessorEvidenceSHA256.Value, evidenceEventID = receipt.EvidenceEventID,
            childOutcomeRunID = receipt.ChildOutcome.RunID, childOutcomeRelativePath = receipt.ChildOutcome.RelativePath,
            childOutcomeAuthoritySHA256 = receipt.ChildOutcome.AuthoritySHA256.Value, childOutcomeRailSHA256 = receipt.ChildOutcome.RailSHA256.Value,
            childOutcomeForcedDecisionID = receipt.ChildOutcome.ForcedDecisionID.Value, childOutcomeEventID = receipt.ChildOutcome.OutcomeEventID.Value,
            childOutcomePayloadSHA256 = receipt.ChildOutcome.OutcomePayloadSHA256.Value, childOutcomeBeforeSeal = receipt.ChildOutcome.BeforeSeal };
    private static LoopClosureGateLivenessRON WriteGateLiveness(LoopClosureGateLiveness meter)
        => new() { species = meter.Species.ToString(), reached = meter.Reached, admitted = meter.Admitted, denied = meter.Denied, meterSHA256 = meter.MeterSHA256.Value, denialReasons = meter.DenialReasons.Select(static denial => new LoopClosureGateDenialRON { reason = denial.Reason.ToString(), count = denial.Count }).ToList() };

    private static OrganicComparisonSummary? ReadOrganicComparisons(LoopClosureOrganicComparisonSummaryRON? value)
    {
        if (value is null || (string.IsNullOrWhiteSpace(value.sourceAuthoritySHA256)
            && string.IsNullOrWhiteSpace(value.streamSHA256) && value.receipts.Count == 0)) return null;
        OrganicComparisonReceipt[] receipts = value.receipts.Select(static receipt => new OrganicComparisonReceipt(
            receipt.step, new CortexPolicyID(receipt.policy), new CortexPolicyDecisionID(receipt.decisionID),
            new TapeEventID(receipt.sourceDecisionEventID), receipt.sourceDecisionPayloadSHA256,
            receipt.sourceDecisionJournalSHA256, new GrammarRevisionID(receipt.readoutRevision), receipt.readoutFingerprint,
            receipt.candidateFingerprint, receipt.candidateOccurrenceDigest, receipt.launchpadAction, receipt.rawCandidateAction,
            receipt.selectedCandidateAction, Parse<OrganicComparisonOutcomeKinds>(receipt.outcome),
            receipt.fundingDecisionID == 0 ? null : new CortexPolicyQuotaDecisionID(receipt.fundingDecisionID),
            string.IsNullOrWhiteSpace(receipt.fundingDecision) ? null : Parse<CortexPolicyQuotaDecisions>(receipt.fundingDecision), receipt.fundingJournalRowSHA256,
            receipt.settlementJournalRowSHA256, receipt.canonicalReceiptSHA256)).ToArray();
        return new OrganicComparisonSummary(new(value.sourceAuthoritySHA256), new(value.streamSHA256), receipts,
            value.eligibleDecisions, value.comparisons, value.fundingDenied, value.completedNoMatch,
            value.candidateAgreements, value.candidateDivergences);
    }

    private static LoopClosureOrganicComparisonSummaryRON WriteOrganicComparisons(OrganicComparisonSummary summary)
        => new()
        {
            sourceAuthoritySHA256 = summary.SourceAuthoritySHA256.Value,
            streamSHA256 = summary.StreamSHA256.Value,
            eligibleDecisions = summary.EligibleDecisions,
            comparisons = summary.Comparisons,
            fundingDenied = summary.FundingDenied,
            completedNoMatch = summary.CompletedNoMatch,
            candidateAgreements = summary.CandidateAgreements,
            candidateDivergences = summary.CandidateDivergences,
            receipts = summary.Receipts.Select(static receipt => new LoopClosureOrganicComparisonReceiptRON
            {
                step = receipt.Step, policy = receipt.Policy.Value, decisionID = receipt.DecisionID.Value,
                sourceDecisionEventID = receipt.SourceDecisionEventID.Value, sourceDecisionPayloadSHA256 = receipt.SourceDecisionPayloadSHA256,
                sourceDecisionJournalSHA256 = receipt.SourceDecisionJournalSHA256, readoutRevision = receipt.ReadoutRevision.Value,
                readoutFingerprint = receipt.ReadoutFingerprint, candidateFingerprint = receipt.CandidateFingerprint,
                candidateOccurrenceDigest = receipt.CandidateOccurrenceDigest, launchpadAction = receipt.LaunchpadAction,
                rawCandidateAction = receipt.RawCandidateAction, selectedCandidateAction = receipt.SelectedCandidateAction,
                outcome = receipt.Outcome.ToString(), fundingDecisionID = receipt.QuotaDecisionID?.Value ?? 0,
                fundingDecision = receipt.FundingDecision?.ToString() ?? "", fundingJournalRowSHA256 = receipt.FundingJournalRowSHA256,
                settlementJournalRowSHA256 = receipt.SettlementJournalRowSHA256, canonicalReceiptSHA256 = receipt.CanonicalReceiptSHA256,
            }).ToList(),
        };
    private static LoopClosureVerdictRON WriteVerdict(LoopClosureVerdict verdict)
    {
        LoopClosureVerdictRON encoded = new() { species = verdict.Species.ToString(), assay = verdict.Assay.ToString(), power = verdict.Power.ToString(), status = verdict.Status.ToString(), evidenceSHA256 = verdict.EvidenceSHA256.Value };
        switch (verdict)
        {
            case PatternBecameThoughtVerdict theory:
                encoded.sourcePredictionID = theory.Corroboration.SourcePredictionID.Value; encoded.derivedPredictionID = theory.Corroboration.ComposedPredictionID.Value; encoded.derivationNodeID = theory.Corroboration.CompositionNodeID.Value;
                encoded.proofSHA256 = theory.Corroboration.ProofSHA256.Value; encoded.auditSHA256 = theory.Corroboration.AuditSHA256.Value; encoded.mainEvaluatorDelta = theory.Corroboration.MainEvaluatorDelta; encoded.numericEvaluatorDelta = theory.Corroboration.NumericEvaluatorDelta;
                encoded.targetSpecies = theory.Corroboration.TargetSpecies.ToString(); encoded.supportEventIDs = theory.Corroboration.SupportEventIDs.Select(static id => (long)id.Value).ToList(); encoded.basisLawAdmissionIDs = theory.Corroboration.BasisLawAdmissionIDs.ToList(); break;
            case ThoughtOverruledInstinctVerdict thought:
                encoded.foldNodeID = thought.Corroboration.FoldNodeID.Value; encoded.foldRevision = thought.Corroboration.FoldRevision.Value; encoded.teacherEvidenceSHA256 = thought.Corroboration.TeacherEvidenceSHA256.Value;
                encoded.fundingID = thought.Corroboration.QuotaID.Value; encoded.dissentRevision = thought.Corroboration.DivergenceRevision.Value; encoded.launchpadAction = thought.Corroboration.LaunchpadAction;
                encoded.candidateExecutionOutcome = thought.Corroboration.CandidateExecutionOutcome.ToString(); encoded.candidateRequestCount = thought.Corroboration.CandidateRequestCount; encoded.candidateGuardAdmittedCount = thought.Corroboration.CandidateGuardAdmittedCount;
                encoded.candidateAction = thought.Corroboration.CandidateAction; encoded.forcedDecisionID = thought.Corroboration.ForcedDecisionID.Value; encoded.forcedAction = thought.Corroboration.ForcedAction; encoded.forcedDiverged = thought.Corroboration.ForcedDiverged;
                encoded.nullReceiptSHA256 = thought.Corroboration.NullReceiptSHA256.Value; encoded.dissentEvidenceBase64 = thought.Corroboration.DivergenceEvidenceBase64; break;
            case ObjectLoopClosedVerdict closed:
                encoded.outcomeNodeID = closed.Corroboration.OutcomeNodeID.Value; encoded.lineageSHA256 = closed.Corroboration.LineageSHA256.Value;
                encoded.theoryEvidenceSHA256 = closed.Corroboration.PatternEvidenceSHA256.Value; encoded.dissentEvidenceSHA256 = closed.Corroboration.DivergenceEvidenceSHA256.Value;
                encoded.terminalOutcomeEventID = closed.Corroboration.TerminalOutcomeEventID;
                encoded.childOutcomeRunID = closed.Corroboration.ChildOutcome.RunID; encoded.childOutcomeRelativePath = closed.Corroboration.ChildOutcome.RelativePath;
                encoded.childOutcomeAuthoritySHA256 = closed.Corroboration.ChildOutcome.AuthoritySHA256.Value; encoded.childOutcomeRailSHA256 = closed.Corroboration.ChildOutcome.RailSHA256.Value;
                encoded.childOutcomeForcedDecisionID = closed.Corroboration.ChildOutcome.ForcedDecisionID.Value; encoded.childOutcomeEventID = closed.Corroboration.ChildOutcome.OutcomeEventID.Value;
                encoded.childOutcomePayloadSHA256 = closed.Corroboration.ChildOutcome.OutcomePayloadSHA256.Value; encoded.childOutcomeBeforeSeal = closed.Corroboration.ChildOutcome.BeforeSeal; break;
            default: throw new InvalidDataException("loop-closure verdict species has no typed corroboration");
        }
        return encoded;
    }
    private static LoopClosureVerdict ReadVerdict(LoopClosureVerdictRON verdict)
    {
        LoopClosureAssayStatuses assay = Parse<LoopClosureAssayStatuses>(verdict.assay);
        LoopClosurePowerStatuses power = Parse<LoopClosurePowerStatuses>(verdict.power);
        LoopClosureVerdictStatuses status = Parse<LoopClosureVerdictStatuses>(verdict.status);
        LoopClosureDigest evidence = new(verdict.evidenceSHA256);
        return Parse<LoopClosureVerdictSpecies>(verdict.species) switch
        {
            LoopClosureVerdictSpecies.PatternBecameThought => new PatternBecameThoughtVerdict(assay, power, status, evidence,
                new(new EmlPredictionID(verdict.sourcePredictionID), new EmlPredictionID(verdict.derivedPredictionID), new LoopLineageNodeID(verdict.derivationNodeID), new(verdict.proofSHA256), new(verdict.auditSHA256), verdict.mainEvaluatorDelta, verdict.numericEvaluatorDelta,
                    ReadTargetSpecies(verdict.targetSpecies), verdict.supportEventIDs.Select(static id => new TapeEventID(id)).ToArray(), verdict.basisLawAdmissionIDs)),
            LoopClosureVerdictSpecies.ThoughtOverruledInstinct => new ThoughtOverruledInstinctVerdict(assay, power, status, evidence,
                new(new LoopLineageNodeID(verdict.foldNodeID), new GrammarRevisionID(verdict.foldRevision), new(verdict.teacherEvidenceSHA256), new(verdict.fundingID), new GrammarRevisionID(verdict.dissentRevision), verdict.launchpadAction,
                    Parse<CortexPolicyTrialExecutionOutcomes>(verdict.candidateExecutionOutcome), verdict.candidateRequestCount, verdict.candidateGuardAdmittedCount, verdict.candidateAction,
                    new CortexPolicyDecisionID(verdict.forcedDecisionID), verdict.forcedAction, verdict.forcedDiverged, new(verdict.nullReceiptSHA256), verdict.dissentEvidenceBase64)),
            LoopClosureVerdictSpecies.ObjectLoopClosed => new ObjectLoopClosedVerdict(assay, power, status, evidence,
                new(new LoopLineageNodeID(verdict.outcomeNodeID), new(verdict.lineageSHA256), new(verdict.theoryEvidenceSHA256), new(verdict.dissentEvidenceSHA256), verdict.terminalOutcomeEventID,
                    new LoopClosureChildOutcomeReference(verdict.childOutcomeRunID, verdict.childOutcomeRelativePath,
                        new(verdict.childOutcomeAuthoritySHA256), new(verdict.childOutcomeRailSHA256),
                        new CortexPolicyDecisionID(verdict.childOutcomeForcedDecisionID), new TapeEventID(verdict.childOutcomeEventID),
                        new(verdict.childOutcomePayloadSHA256), verdict.childOutcomeBeforeSeal))),
            _ => throw new InvalidDataException("loop-closure report carries unknown verdict species")
        };
    }
    private static LoopClosureLineageNullOutcome ReadNull(LoopLineageShuffledNullReceiptRON value)
    {
        LoopLineageOccurrenceCheckStatuses original = Parse<LoopLineageOccurrenceCheckStatuses>(value.originalStatus);
        LoopLineageOccurrenceCheckStatuses shuffled = Parse<LoopLineageOccurrenceCheckStatuses>(value.shuffledStatus);
        if (original == LoopLineageOccurrenceCheckStatuses.INVALID || shuffled == LoopLineageOccurrenceCheckStatuses.INVALID)
            return new LoopClosureLineageNullMissing(string.IsNullOrWhiteSpace(value.reason) ? MissingLineageReason : value.reason);
        return new LoopClosureLineageNullExecuted(new(value.sourceAuthoritySHA256, value.sourceTapeSHA256, value.sourceJournalSHA256, value.eventCount, value.edgeCount, value.eligibleBucketCount, value.permutationSeed, value.permutationSHA256, value.swappedEdgeCount, value.derangement, value.sameEvents, value.samePayloads, value.originalLineageSHA256, original, value.shuffledLineageSHA256, shuffled, new LoopLineageEdgeID(value.firstDiscriminatingEdge)));
    }
    private static EmlObligationTargetSpecies ReadTargetSpecies(string value)
        => string.IsNullOrWhiteSpace(value) ? EmlObligationTargetSpecies.Residual : Parse<EmlObligationTargetSpecies>(value);
    private static LoopLineageShuffledNullReceiptRON WriteNull(LoopClosureLineageNullOutcome value)
    {
        if (value is LoopClosureLineageNullExecuted executed)
        {
            LoopLineageShuffledNullReceipt receipt = executed.Receipt;
            return new() { sourceAuthoritySHA256 = receipt.SourceAuthoritySHA256, sourceTapeSHA256 = receipt.SourceTapeSHA256, sourceJournalSHA256 = receipt.SourceJournalSHA256, eventCount = receipt.EventCount, edgeCount = receipt.EdgeCount, eligibleBucketCount = receipt.EligibleBucketCount, permutationSeed = receipt.PermutationSeed, permutationSHA256 = receipt.PermutationSHA256, swappedEdgeCount = receipt.SwappedEdgeCount, derangement = receipt.Derangement, sameEvents = receipt.SameEvents, samePayloads = receipt.SamePayloads, originalLineageSHA256 = receipt.OriginalLineageSHA256, originalStatus = receipt.OriginalStatus.ToString(), shuffledLineageSHA256 = receipt.ShuffledLineageSHA256, shuffledStatus = receipt.ShuffledStatus.ToString(), firstDiscriminatingEdge = receipt.FirstDiscriminatingEdge.Value };
        }
        LoopClosureLineageNullMissing missing = (LoopClosureLineageNullMissing)value;
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("missing-lineage|" + missing.Reason)));
        return new() { sourceAuthoritySHA256 = digest, sourceTapeSHA256 = digest, sourceJournalSHA256 = digest, permutationSHA256 = digest, originalLineageSHA256 = digest, shuffledLineageSHA256 = digest, originalStatus = LoopLineageOccurrenceCheckStatuses.INVALID.ToString(), shuffledStatus = LoopLineageOccurrenceCheckStatuses.INVALID.ToString(), reason = missing.Reason };
    }
    private static T Parse<T>(string value) where T : struct, Enum => Enum.TryParse(value, out T result) ? result : throw new InvalidDataException($"loop-closure report carries unknown {typeof(T).Name}");
    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

/// Artifact naming is a type boundary: only a fully green report can become this artifact.
public sealed class ClosureCertificate
{
    private ClosureCertificate(LoopClosureReport report) { Report = report; }
    public LoopClosureReport Report { get; }
    public string ArtifactName => "BirthCertificate";
    internal static ClosureCertificate Create(LoopClosureReport report, LoopClosureRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();
        report.Validate();
        if (!string.Equals(report.RegistrationSHA256, registration.Digest, StringComparison.Ordinal))
            throw new InvalidOperationException("closure certificate report is not bound to the supplied registration");
        if (!report.IsSourceBacked) throw new InvalidOperationException("closure certificate requires a report assembled by the source-backed adjudicator");
        if (!report.CanMintClosureCertificate) throw new InvalidOperationException("loop-closure report is not eligible for closure certificate naming");
        return new ClosureCertificate(report);
    }
    public byte[] Encode() => Report.Encode();
}

[RonObject]
internal partial class LoopClosureReportRON
{
    public int schemaVersion;
    public string registrationSHA256 = "";
    public LoopClosureArmReportRON live = new();
    public LoopClosureArmReportRON control = new();
    public List<LoopClosurePairLineVerdictRON> lines = new();
    public LoopClosureVerdictRON theory_became_thought = new();
    public LoopClosureVerdictRON thought_overruled_instinct = new();
    public LoopClosureVerdictRON object_loop_closed = new();
    public List<LoopClosureLinkReceiptRON> linkReceipts = new();
    public List<LoopClosureGateLivenessRON> gateLiveness = new();
    public LoopClosureOrganicComparisonSummaryRON? organicComparisons;
    public LoopLineageShuffledNullReceiptRON lineageNull = new();
    public string outcome = "";
    public string artifactName = "";
    public string digest = "";
}

[RonObject]
internal partial class LoopClosureOrganicComparisonSummaryRON
{
    public string sourceAuthoritySHA256 = "";
    public string streamSHA256 = "";
    public int eligibleDecisions;
    public int comparisons;
    public int fundingDenied;
    public int completedNoMatch;
    public int candidateAgreements;
    public int candidateDivergences;
    public List<LoopClosureOrganicComparisonReceiptRON> receipts = new();
}

[RonObject]
internal partial class LoopClosureOrganicComparisonReceiptRON
{
    public int step;
    public string policy = "";
    public ulong decisionID;
    public long sourceDecisionEventID;
    public string sourceDecisionPayloadSHA256 = "";
    public string sourceDecisionJournalSHA256 = "";
    public ulong readoutRevision;
    public ulong readoutFingerprint;
    public ulong candidateFingerprint;
    public ulong candidateOccurrenceDigest;
    public int launchpadAction;
    public int rawCandidateAction;
    public int selectedCandidateAction;
    public string outcome = "";
    public ulong fundingDecisionID;
    public string fundingDecision = "";
    public string fundingJournalRowSHA256 = "";
    public string settlementJournalRowSHA256 = "";
    public string canonicalReceiptSHA256 = "";
}

[RonObject]
internal partial class LoopClosureLinkReceiptRON
{
    public string species = ""; public string path = ""; public string state = "";
    public string evidenceSHA256 = ""; public string predecessorEvidenceSHA256 = ""; public long evidenceEventID = -1;
    public string childOutcomeRunID = ""; public string childOutcomeRelativePath = "";
    public string childOutcomeAuthoritySHA256 = ""; public string childOutcomeRailSHA256 = "";
    public ulong childOutcomeForcedDecisionID; public long childOutcomeEventID;
    public string childOutcomePayloadSHA256 = ""; public bool childOutcomeBeforeSeal;
}

[RonObject]
internal partial class LoopClosureGateLivenessRON
{
    public string species = ""; public long reached; public long admitted; public long denied;
    public List<LoopClosureGateDenialRON> denialReasons = new(); public string meterSHA256 = "";
}

[RonObject]
internal partial class LoopClosureGateDenialRON
{
    public string reason = ""; public long count;
}

[RonObject]
internal partial class LoopClosureArmReportRON
{
    public string runID = ""; public string configFingerprint = ""; public string worldSHA256 = ""; public string authoritySHA256 = "";
    public string checkpointSHA256 = ""; public string closureSHA256 = ""; public string binarySHA256 = ""; public int nextStep;
}

[RonObject]
internal partial class LoopClosurePairLineVerdictRON
{
    public string name = ""; public string assay = ""; public string power = ""; public string status = ""; public string evidenceSHA256 = "";
}

[RonObject]
// Frozen RON field names retain dissent vocabulary; identifier-side names use Divergence.
internal partial class LoopClosureVerdictRON
{
    public string species = ""; public string assay = ""; public string power = ""; public string status = ""; public string evidenceSHA256 = "";
    public int sourcePredictionID; public int derivedPredictionID; public string derivationNodeID = ""; public string proofSHA256 = ""; public string auditSHA256 = "";
    public long mainEvaluatorDelta; public long numericEvaluatorDelta; public string foldNodeID = ""; public ulong foldRevision; public string teacherEvidenceSHA256 = "";
    public string targetSpecies = ""; public List<long> supportEventIDs = new(); public List<string> basisLawAdmissionIDs = new();
    public string fundingID = ""; public ulong dissentRevision; public int launchpadAction; public string candidateExecutionOutcome = ""; public long candidateRequestCount; public long candidateGuardAdmittedCount; public int candidateAction; public ulong forcedDecisionID; public int forcedAction; public bool forcedDiverged; public string outcomeNodeID = "";
    public string nullReceiptSHA256 = ""; public string dissentEvidenceBase64 = ""; public string lineageSHA256 = "";
    public string theoryEvidenceSHA256 = ""; public string dissentEvidenceSHA256 = ""; public long terminalOutcomeEventID = -1;
    public string childOutcomeRunID = ""; public string childOutcomeRelativePath = "";
    public string childOutcomeAuthoritySHA256 = ""; public string childOutcomeRailSHA256 = "";
    public ulong childOutcomeForcedDecisionID; public long childOutcomeEventID;
    public string childOutcomePayloadSHA256 = ""; public bool childOutcomeBeforeSeal;
}

[RonObject]
internal partial class LoopLineageShuffledNullReceiptRON
{
    public string sourceAuthoritySHA256 = ""; public string sourceTapeSHA256 = ""; public string sourceJournalSHA256 = ""; public int eventCount; public int edgeCount;
    public int eligibleBucketCount; public ulong permutationSeed; public string permutationSHA256 = ""; public int swappedEdgeCount; public bool derangement;
    public bool sameEvents; public bool samePayloads; public string originalLineageSHA256 = ""; public string originalStatus = ""; public string shuffledLineageSHA256 = "";
    public string reason = "";
    public string shuffledStatus = ""; public string firstDiscriminatingEdge = "";
}
