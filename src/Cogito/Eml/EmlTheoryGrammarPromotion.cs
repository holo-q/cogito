namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

/// The domain identity carried by an EML law promotion.  A domain is an explicit
/// authority boundary; support from another domain is never silently joined by a
/// matching digest.
public readonly struct EmlLawDomainID : IEquatable<EmlLawDomainID>
{
    private const string Prefix = "eml-law-domain-v1:";

    private EmlLawDomainID(string value, string templateSHA256, string behaviorSHA256,
        string guardSHA256, string authoritySHA256)
    {
        Value = value;
        TemplateSHA256 = templateSHA256;
        BehaviorSHA256 = behaviorSHA256;
        GuardSHA256 = guardSHA256;
        AuthoritySHA256 = authoritySHA256;
    }

    public string Value { get; }
    public string TemplateSHA256 { get; }
    public string BehaviorSHA256 { get; }
    public string GuardSHA256 { get; }
    public string AuthoritySHA256 { get; }

    public bool IsValid
        => Value is not null
            && Value.StartsWith(Prefix, StringComparison.Ordinal)
            && IsDigest(TemplateSHA256)
            && IsDigest(BehaviorSHA256)
            && IsDigest(GuardSHA256)
            && IsDigest(AuthoritySHA256)
            && string.Equals(Value, BuildValue(TemplateSHA256, BehaviorSHA256, GuardSHA256, AuthoritySHA256), StringComparison.Ordinal);

    public override string ToString() => Value;
    public bool Equals(EmlLawDomainID other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is EmlLawDomainID other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public static bool operator ==(EmlLawDomainID left, EmlLawDomainID right) => left.Equals(right);
    public static bool operator !=(EmlLawDomainID left, EmlLawDomainID right) => !left.Equals(right);

    internal static EmlLawDomainID Derive(
        in EmlLaw law,
        in EmlLawBehaviorCertificate certificate,
        in EmlLawProof proof,
        string canonicalAuthorityID)
    {
        if (string.IsNullOrWhiteSpace(law.Template) || string.IsNullOrWhiteSpace(canonicalAuthorityID))
            throw new ArgumentException("law domain derivation requires canonical law and authority");
        string template = Digest(law.Template);
        string behavior = Digest(string.Join('|',
            certificate.AtOne.R1, certificate.AtOne.I1, certificate.AtOne.R2, certificate.AtOne.I2,
            certificate.AtX.R1, certificate.AtX.I1, certificate.AtX.R2, certificate.AtX.I2,
            certificate.AtY.R1, certificate.AtY.I1, certificate.AtY.R2, certificate.AtY.I2));
        string guard = Digest(string.Join('|', proof.DomainGuardDigest.ToString("X16"),
            proof.DomainGuards?.Canonical() ?? string.Empty, proof.GuardWitness.Canonical(), proof.GuardScheme));
        string authority = Digest(canonicalAuthorityID);
        return new(BuildValue(template, behavior, guard, authority), template, behavior, guard, authority);
    }

    internal static EmlLawDomainID DeriveForFixture(string template, string authority)
        => Derive(new EmlLaw(template, 2, 2, 1, "1", "1 = 1"), default, default, authority);

    internal static EmlLawDomainID Restore(string value, string templateSHA256, string behaviorSHA256,
        string guardSHA256, string authoritySHA256)
        => new(value, templateSHA256, behaviorSHA256, guardSHA256, authoritySHA256);

    private static string BuildValue(string template, string behavior, string guard, string authority)
        => Prefix + Digest(string.Join('|', template, behavior, guard, authority));

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsDigest(string? value)
        => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();
}

public enum EmlPatternGrammarAdmissionValidationStatuses : byte
{
    Admitted,
    ProposalOnly,
    ForeignSupport,
    ForeignDomain,
    NonRankReducing,
    FillerAmplification,
    GeneratedPredictionCount,
    Invalid,
}

public enum EmlPatternGrammarAdmissionReportMechanismSpecies : byte
{
    PatternToGrammarAdmitted,
}

/// The report wire spelling is deliberately not derived from enum casing: it is a
/// stable mechanism species consumed by report readers outside the EML namespace.
public readonly record struct EmlPatternGrammarAdmissionMechanismReport(
    EmlPatternGrammarAdmissionReportMechanismSpecies Species,
    EmlPatternGrammarAdmissionValidationStatuses Status,
    string ReceiptSHA256,
    bool Consumed)
{
    public const string WireSpecies = "theory_to_grammar_admitted";

    public string SpeciesName => WireSpecies;

    public void Validate()
    {
        if (Species != EmlPatternGrammarAdmissionReportMechanismSpecies.PatternToGrammarAdmitted
            || Status != EmlPatternGrammarAdmissionValidationStatuses.Admitted
            || !Consumed
            || ReceiptSHA256 is null
            || ReceiptSHA256.Length != 64
            || !ReceiptSHA256.All(Uri.IsHexDigit))
            throw new InvalidDataException("theory-to-grammar mechanism report is malformed");
    }

    public static EmlPatternGrammarAdmissionMechanismReport FromReceipt(EmlPatternGrammarAdmissionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        EmlPatternGrammarAdmissionMechanismReport report = new(
            EmlPatternGrammarAdmissionReportMechanismSpecies.PatternToGrammarAdmitted,
            EmlPatternGrammarAdmissionValidationStatuses.Admitted,
            receipt.Digest,
            receipt.Consumed);
        report.Validate();
        return report;
    }
}

/// The one generated claim admitted by a promotion.  Metadata is intentionally
/// absent: Line is the exact ordinary EML claim payload later appended to Tape as
/// a Reflected event.  Receipt metadata lives in the side receipt, never in bytes.
public readonly record struct EmlPatternGrammarGeneratedPrediction(
    EmlPredictionID PredictionID,
    TapeEventID LawExecutionEventID,
    TapeEventID SupportEventID,
    string Line,
    string LhsRPN,
    string RhsRPN)
{
    public static EmlPatternGrammarGeneratedPrediction Create(
        EmlPredictionID claimID,
        TapeEventID lawExecutionEventID,
        TapeEventID supportEventID,
        string lhsRPN,
        string rhsRPN)
    {
        if (!EmlRung0Digest.IsCanonicalRPN(lhsRPN) || !EmlRung0Digest.IsCanonicalRPN(rhsRPN))
            throw new ArgumentException("promotion claim must contain canonical closed RPN programs");
        return new(claimID, lawExecutionEventID, supportEventID, lhsRPN + " = " + rhsRPN, lhsRPN, rhsRPN);
    }

    public void Validate()
    {
        if (PredictionID.Value < 0
            || LawExecutionEventID.Value < 0
            || SupportEventID.Value < 0
            || LawExecutionEventID.Value >= SupportEventID.Value
            || !EmlRung0Digest.IsCanonicalRPN(LhsRPN)
            || !EmlRung0Digest.IsCanonicalRPN(RhsRPN)
            || string.IsNullOrEmpty(Line)
            || !string.Equals(Line, LhsRPN + " = " + RhsRPN, StringComparison.Ordinal)
            || !EmlPrediction.TryParse(Line, out EmlPrediction claim)
            || claim.Tilde
            || !claim.RhsRpn
            || !string.Equals(claim.Lhs, LhsRPN, StringComparison.Ordinal)
            || !string.Equals(claim.Rhs, RhsRPN, StringComparison.Ordinal))
            throw new InvalidDataException("promotion claim is not one line-only canonical RPN claim");
    }

    public byte[] CreateLinePayload()
    {
        Validate();
        return Encoding.ASCII.GetBytes(Line);
    }
}

/// A proof of the well-founded EML rewrite ordering.  Length is the primary rank;
/// ordinal order breaks equal-length ties exactly as EmlRewriteSystem does.
public readonly record struct EmlPatternGrammarRankProof(
    string SourceRPN,
    string GeneratedRPN,
    int SourceRank,
    int GeneratedRank)
{
    public static EmlPatternGrammarRankProof Create(string sourceRPN, string generatedRPN)
    {
        if (!EmlRung0Digest.IsCanonicalRPN(sourceRPN) || !EmlRung0Digest.IsCanonicalRPN(generatedRPN))
            throw new ArgumentException("rank proof requires canonical closed RPN programs");
        return new(sourceRPN, generatedRPN, sourceRPN.Length, generatedRPN.Length);
    }

    public bool IsRankReducing => EmlRewriteSystem.ReducesRank(SourceRPN, GeneratedRPN);

    public void Validate()
    {
        if (!EmlRung0Digest.IsCanonicalRPN(SourceRPN)
            || !EmlRung0Digest.IsCanonicalRPN(GeneratedRPN)
            || SourceRank != SourceRPN.Length
            || GeneratedRank != GeneratedRPN.Length
            || !IsRankReducing)
            throw new InvalidDataException("promotion rank proof is not deterministic and rank-reducing");
    }
}

/// Durable side receipt for one earned theory-to-grammar admission.  The receipt is
/// immutable and independently digest-bound; grammar state is advanced only later by
/// the ordinary TapeDelta -> Loom -> stride path after the line payload is appended.
public sealed class EmlPatternGrammarAdmissionReceipt
{
    public const int SchemaVersion = 1;

    private EmlPatternGrammarAdmissionReceipt(
        EmlLawDomainID domain,
        string authorityID,
        string supportAuthorityID,
        string supportSetDigest,
        string admissionID,
        string candidatePackageDigest,
        string canonicalFiller,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        EmlPatternGrammarRankProof rankProof,
        GrammarRevisionID admissionRevision,
        int fillerAmplification,
        bool consumed,
        GrammarRevisionID? consumedGrammarRevision,
        TapeEventID? reflectedTapeEventID,
        LoopLineageNodeID? lineageNodeID,
        string digest)
    {
        Domain = domain;
        AuthorityID = authorityID;
        SupportAuthorityID = supportAuthorityID;
        SupportSetDigest = supportSetDigest;
        AdmissionID = admissionID;
        CandidatePackageDigest = candidatePackageDigest;
        CanonicalFiller = canonicalFiller;
        GeneratedPrediction = generatedPrediction;
        RankProof = rankProof;
        AdmissionRevision = admissionRevision;
        FillerAmplification = fillerAmplification;
        Consumed = consumed;
        ConsumedGrammarRevision = consumedGrammarRevision;
        ReflectedTapeEventID = reflectedTapeEventID;
        LineageNodeID = lineageNodeID;
        Digest = digest;
    }

    public EmlLawDomainID Domain { get; }
    public EmlLawDomainID DomainID => Domain;
    public string AuthorityID { get; }
    public string CanonicalAuthorityID => AuthorityID;
    public string SupportAuthorityID { get; }
    public string SupportSetDigest { get; }
    public string OccurrenceDigest => SupportSetDigest;
    public string AdmissionID { get; }
    public string CandidatePackageDigest { get; }
    public string PackageDigest => CandidatePackageDigest;
    public string CanonicalFiller { get; }
    public EmlPatternGrammarGeneratedPrediction GeneratedPrediction { get; }
    public EmlPatternGrammarRankProof RankProof { get; }
    public GrammarRevisionID AdmissionRevision { get; }
    public GrammarRevisionID Revision => AdmissionRevision;
    public int FillerAmplification { get; }
    public bool Consumed { get; }
    public GrammarRevisionID? ConsumedGrammarRevision { get; }
    public TapeEventID? ReflectedTapeEventID { get; }
    public LoopLineageNodeID? LineageNodeID { get; }
    public string Digest { get; }
    public int GeneratedPredictionCount => 1;
    public EmlPatternGrammarAdmissionReportMechanismSpecies Species
        => EmlPatternGrammarAdmissionReportMechanismSpecies.PatternToGrammarAdmitted;

    public static EmlPatternGrammarAdmissionReceipt Create(
        EmlLawDomainID domain,
        string authorityID,
        string supportAuthorityID,
        string supportSetDigest,
        string admissionID,
        string candidatePackageDigest,
        string canonicalFiller,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        GrammarRevisionID admissionRevision,
        int fillerAmplification = 1)
    {
        if (!TryAdmit(true, domain, domain, authorityID, supportAuthorityID, supportSetDigest,
                admissionID, candidatePackageDigest, canonicalFiller, generatedPrediction,
                admissionRevision, 1, fillerAmplification, out EmlPatternGrammarAdmissionReceipt? receipt,
                out EmlPatternGrammarAdmissionValidationStatuses status)
            || receipt is null)
            throw new InvalidDataException($"theory-to-grammar promotion was not admitted: {status}");
        return receipt;
    }

    public void Validate()
    {
        if (!Domain.IsValid
            || string.IsNullOrWhiteSpace(AuthorityID)
            || string.IsNullOrWhiteSpace(SupportAuthorityID)
            || !string.Equals(AuthorityID, SupportAuthorityID, StringComparison.Ordinal)
            || !string.Equals(Domain.AuthoritySHA256,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AuthorityID))), StringComparison.Ordinal)
            || !IsDigest(SupportSetDigest)
            || string.IsNullOrWhiteSpace(AdmissionID)
            || !IsDigest(CandidatePackageDigest)
            || !EmlRung0Digest.IsCanonicalRPN(CanonicalFiller)
            || AdmissionRevision.Value == 0
            || FillerAmplification != 1
            || GeneratedPredictionCount != 1)
            throw new InvalidDataException("theory-to-grammar promotion receipt identity is malformed");

        GeneratedPrediction.Validate();
        RankProof.Validate();
        if (Consumed != (ConsumedGrammarRevision is not null))
            throw new InvalidDataException("promotion receipt consumed state disagrees with its grammar revision");
        if (Consumed)
        {
            if (ConsumedGrammarRevision is not GrammarRevisionID consumedRevision)
                throw new InvalidDataException("consumed promotion receipt omits its grammar revision");
            if (consumedRevision < AdmissionRevision || consumedRevision == AdmissionRevision)
                throw new InvalidDataException("consumed promotion receipt does not witness a later grammar revision");
            if (ReflectedTapeEventID is not TapeEventID reflected || reflected.Value < 0)
                throw new InvalidDataException("consumed promotion receipt omits its reflected tape event");
            if (LineageNodeID is LoopLineageNodeID lineage && !lineage.IsValid)
                throw new InvalidDataException("consumed promotion receipt carries an invalid lineage node");
        }
        else if (LineageNodeID is not null)
            throw new InvalidDataException("unconsumed promotion receipt carries settled lineage identity");
        if (!string.Equals(RankProof.SourceRPN, GeneratedPrediction.LhsRPN, StringComparison.Ordinal)
            || !string.Equals(RankProof.GeneratedRPN, GeneratedPrediction.RhsRPN, StringComparison.Ordinal)
            || !IsDigest(Digest)
            || !string.Equals(Digest, ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar promotion receipt does not close its rank or digest proof");
    }

    public byte[] Encode()
    {
        Validate();
        EmlPatternGrammarAdmissionReceiptRON document = new()
        {
            schemaVersion = SchemaVersion,
            species = EmlPatternGrammarAdmissionMechanismReport.WireSpecies,
            domain = Domain.Value,
            templateSHA256 = Domain.TemplateSHA256,
            behaviorSHA256 = Domain.BehaviorSHA256,
            guardSHA256 = Domain.GuardSHA256,
            authoritySHA256 = Domain.AuthoritySHA256,
            authorityID = AuthorityID,
            supportAuthorityID = SupportAuthorityID,
            supportSetDigest = SupportSetDigest,
            admissionID = AdmissionID,
            candidatePackageDigest = CandidatePackageDigest,
            canonicalFiller = CanonicalFiller,
            claimID = GeneratedPrediction.PredictionID.Value,
            lawExecutionEventID = GeneratedPrediction.LawExecutionEventID.Value,
            supportEventID = GeneratedPrediction.SupportEventID.Value,
            line = GeneratedPrediction.Line,
            lhsRPN = GeneratedPrediction.LhsRPN,
            rhsRPN = GeneratedPrediction.RhsRPN,
            sourceRank = RankProof.SourceRank,
            generatedRank = RankProof.GeneratedRank,
            admissionRevision = AdmissionRevision.Value,
            fillerAmplification = FillerAmplification,
            consumed = Consumed,
            hasConsumedGrammarRevision = ConsumedGrammarRevision is not null,
            consumedGrammarRevision = ConsumedGrammarRevision?.Value ?? 0,
            hasReflectedTapeEventID = ReflectedTapeEventID is not null,
            reflectedTapeEventID = ReflectedTapeEventID?.Value ?? 0,
            hasLineageNodeID = LineageNodeID is not null,
            lineageNodeID = LineageNodeID?.Value ?? "",
            digest = Digest,
        };
        return RonSerializer.SerializeToUtf8(in document);
    }

    public static EmlPatternGrammarAdmissionReceipt Decode(ReadOnlySpan<byte> bytes)
    {
        EmlPatternGrammarAdmissionReceiptRON document = RonSerializer.Deserialize<EmlPatternGrammarAdmissionReceiptRON>(bytes);
        if (document.schemaVersion != SchemaVersion
            || !string.Equals(document.species, EmlPatternGrammarAdmissionMechanismReport.WireSpecies, StringComparison.Ordinal))
            throw new InvalidDataException("unsupported theory-to-grammar promotion receipt schema");
        EmlPatternGrammarAdmissionReceipt receipt = new(
            EmlLawDomainID.Restore(document.domain, document.templateSHA256, document.behaviorSHA256, document.guardSHA256, document.authoritySHA256), document.authorityID, document.supportAuthorityID,
            document.supportSetDigest, document.admissionID, document.candidatePackageDigest,
            document.canonicalFiller,
            new EmlPatternGrammarGeneratedPrediction(new EmlPredictionID(document.claimID), new TapeEventID(document.lawExecutionEventID), new TapeEventID(document.supportEventID), document.line, document.lhsRPN, document.rhsRPN),
            new EmlPatternGrammarRankProof(document.lhsRPN, document.rhsRPN, document.sourceRank, document.generatedRank),
            new GrammarRevisionID(document.admissionRevision), document.fillerAmplification, document.consumed,
            document.hasConsumedGrammarRevision ? new GrammarRevisionID(document.consumedGrammarRevision) : null,
            document.hasReflectedTapeEventID ? new TapeEventID(document.reflectedTapeEventID) : null,
            document.hasLineageNodeID ? new LoopLineageNodeID(document.lineageNodeID) : null, document.digest);
        receipt.Validate();
        if (!receipt.Encode().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("theory-to-grammar promotion receipt RON round-trip changed bytes");
        return receipt;
    }

    public static bool TryAdmit(
        bool authorityVerified,
        EmlLawDomainID domain,
        EmlLawDomainID supportDomain,
        string authorityID,
        string supportAuthorityID,
        string supportSetDigest,
        string admissionID,
        string candidatePackageDigest,
        string canonicalFiller,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        GrammarRevisionID admissionRevision,
        int generatedPredictionCount,
        int fillerAmplification,
        out EmlPatternGrammarAdmissionReceipt? receipt,
        out EmlPatternGrammarAdmissionValidationStatuses status)
    {
        receipt = null;
        if (!authorityVerified) { status = EmlPatternGrammarAdmissionValidationStatuses.ProposalOnly; return false; }
        if (!string.Equals(authorityID, supportAuthorityID, StringComparison.Ordinal))
        { status = EmlPatternGrammarAdmissionValidationStatuses.ForeignSupport; return false; }
        if (domain != supportDomain)
        { status = EmlPatternGrammarAdmissionValidationStatuses.ForeignDomain; return false; }
        if (generatedPredictionCount != 1)
        { status = EmlPatternGrammarAdmissionValidationStatuses.GeneratedPredictionCount; return false; }
        if (fillerAmplification != 1)
        { status = EmlPatternGrammarAdmissionValidationStatuses.FillerAmplification; return false; }
        if (!EmlRung0Digest.IsCanonicalRPN(generatedPrediction.LhsRPN)
            || !EmlRung0Digest.IsCanonicalRPN(generatedPrediction.RhsRPN)
            || !EmlRewriteSystem.ReducesRank(generatedPrediction.LhsRPN, generatedPrediction.RhsRPN))
        { status = EmlPatternGrammarAdmissionValidationStatuses.NonRankReducing; return false; }
        try
        {
            generatedPrediction.Validate();
            EmlPatternGrammarRankProof rank = EmlPatternGrammarRankProof.Create(generatedPrediction.LhsRPN, generatedPrediction.RhsRPN);
            EmlPatternGrammarAdmissionReceipt candidate = new(
                domain, authorityID, supportAuthorityID, supportSetDigest, admissionID,
                candidatePackageDigest, canonicalFiller, generatedPrediction, rank, admissionRevision, fillerAmplification,
                false, null, null, null, "");
            receipt = new(
                domain, authorityID, supportAuthorityID, supportSetDigest, admissionID,
                candidatePackageDigest, canonicalFiller, generatedPrediction, rank, admissionRevision, fillerAmplification,
                false, null, null, null,
                candidate.ComputeDigest());
            receipt.Validate();
            status = EmlPatternGrammarAdmissionValidationStatuses.Admitted;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            receipt = null;
            status = EmlPatternGrammarAdmissionValidationStatuses.Invalid;
            return false;
        }
    }

    public EmlPatternGrammarAdmissionReceipt BindConsumption(
        GrammarRevisionID consumedGrammarRevision,
        TapeEventID reflectedTapeEventID,
        LoopLineageNodeID lineageNodeID)
    {
        if (Consumed) throw new InvalidOperationException("promotion receipt is already consumed");
        if (consumedGrammarRevision < AdmissionRevision || consumedGrammarRevision == AdmissionRevision
            || reflectedTapeEventID.Value < 0
            || (ReflectedTapeEventID is TapeEventID admittedEvent && admittedEvent != reflectedTapeEventID)
            || (lineageNodeID.Value is not null && !lineageNodeID.IsValid))
            throw new InvalidDataException("promotion consumption must be a later grammar revision with settled identities");
        LoopLineageNodeID? settledLineage = lineageNodeID.IsValid ? lineageNodeID : null;
        EmlPatternGrammarAdmissionReceipt settled = new(
            Domain, AuthorityID, SupportAuthorityID, SupportSetDigest, AdmissionID,
            CandidatePackageDigest, CanonicalFiller, GeneratedPrediction, RankProof, AdmissionRevision,
            FillerAmplification, true, consumedGrammarRevision, reflectedTapeEventID, settledLineage, "");
        return new(
            Domain, AuthorityID, SupportAuthorityID, SupportSetDigest, AdmissionID,
            CandidatePackageDigest, CanonicalFiller, GeneratedPrediction, RankProof, AdmissionRevision,
            FillerAmplification, true, consumedGrammarRevision, reflectedTapeEventID, settledLineage,
            settled.ComputeDigest());
    }

    /// Bind the ordinary line's reflected tape event at admission time.  The
    /// event is durable before the later grammar revision consumes it; lineage
    /// and the consumed revision remain settlement-only identities.
    public EmlPatternGrammarAdmissionReceipt BindReflection(TapeEventID reflectedTapeEventID)
    {
        if (Consumed || reflectedTapeEventID.Value < 0)
            throw new InvalidDataException("promotion reflection must precede settlement");
        if (ReflectedTapeEventID is TapeEventID existing)
        {
            if (existing != reflectedTapeEventID)
                throw new InvalidDataException("promotion reflection event was rebound");
            return this;
        }
        EmlPatternGrammarAdmissionReceipt reflected = new(
            Domain, AuthorityID, SupportAuthorityID, SupportSetDigest, AdmissionID,
            CandidatePackageDigest, CanonicalFiller, GeneratedPrediction, RankProof, AdmissionRevision,
            FillerAmplification, false, null, reflectedTapeEventID, null, "");
        return new(
            Domain, AuthorityID, SupportAuthorityID, SupportSetDigest, AdmissionID,
            CandidatePackageDigest, CanonicalFiller, GeneratedPrediction, RankProof, AdmissionRevision,
            FillerAmplification, false, null, reflectedTapeEventID, null,
            reflected.ComputeDigest());
    }

    private string ComputeDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            SchemaVersion,
            EmlPatternGrammarAdmissionMechanismReport.WireSpecies,
            Domain.Value,
            AuthorityID,
            SupportAuthorityID,
            SupportSetDigest,
            AdmissionID,
            CandidatePackageDigest,
            CanonicalFiller,
            GeneratedPrediction.Line,
            GeneratedPrediction.LhsRPN,
            GeneratedPrediction.RhsRPN,
            GeneratedPrediction.PredictionID.Value,
            GeneratedPrediction.LawExecutionEventID.Value,
            GeneratedPrediction.SupportEventID.Value,
            RankProof.SourceRank,
            RankProof.GeneratedRank,
            AdmissionRevision.Value,
            FillerAmplification,
            Consumed ? "1" : "0",
            ConsumedGrammarRevision?.Value ?? 0,
            ReflectedTapeEventID?.Value ?? -1,
            LineageNodeID?.Value ?? ""))));

    private static bool IsDigest(string? value)
        => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();
}

[RonObject]
internal partial class EmlPatternGrammarAdmissionReceiptRON
{
    public int schemaVersion;
    public string species = "";
    public string domain = "";
    public string templateSHA256 = "";
    public string behaviorSHA256 = "";
    public string guardSHA256 = "";
    public string authoritySHA256 = "";
    public string authorityID = "";
    public string supportAuthorityID = "";
    public string supportSetDigest = "";
    public string admissionID = "";
    public string candidatePackageDigest = "";
    public string canonicalFiller = "";
    public int claimID;
    public long lawExecutionEventID;
    public long supportEventID;
    public string line = "";
    public string lhsRPN = "";
    public string rhsRPN = "";
    public int sourceRank;
    public int generatedRank;
    public ulong admissionRevision;
    public int fillerAmplification;
    public bool consumed;
    public bool hasConsumedGrammarRevision;
    public ulong consumedGrammarRevision;
    public bool hasReflectedTapeEventID;
    public long reflectedTapeEventID;
    public bool hasLineageNodeID;
    public string lineageNodeID = "";
    public string digest = "";
}

/// The EmlLawStore seam is intentionally an integration hook.  It derives exactly
/// one claim from the verified law's canonical filler and the validated support receipt;
/// it does not append tape bytes or mutate InstallRevision.
internal static class EmlPatternGrammarAdmissionAdmission
{
    internal static bool TryCreateFromVerifiedLaw(
        EmlVerifiedLaw law,
        EmlVerifiedLawSupportReceipt support,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        EmlLawDomainID domain,
        GrammarRevisionID admissionRevision,
        out EmlPatternGrammarAdmissionReceipt? receipt,
        out EmlPatternGrammarAdmissionValidationStatuses status)
    {
        receipt = null;
        status = EmlPatternGrammarAdmissionValidationStatuses.Invalid;
        if (law is null || support is null) return false;
        try { support.ValidateAfterLoad(); }
        catch (InvalidDataException) { return false; }
        if (support.ExecutionEventID is not TapeEventID executionEvent
            || support.SupportEventID is not TapeEventID supportEvent
            || !support.GeneratedPredictionIDs.Contains(generatedPrediction.PredictionID.Value))
            return false;
        if (executionEvent != generatedPrediction.LawExecutionEventID
            || supportEvent != generatedPrediction.SupportEventID)
            return false;
        if (!law.Law.Equals(support.Candidate.Law)
            || !law.Proof.Equals(support.Candidate.Proof)
            || !string.Equals(EmlLawStore.CreateAdmissionID(law), support.CandidateAdmissionID, StringComparison.Ordinal)
            || !EmlLawInstantiation.TryCreate(law.Law.Template, law.Proof.AbsentFiller, out EmlLawInstantiation canonicalInstance)
            || !string.Equals(generatedPrediction.LhsRPN, canonicalInstance.LeftRpn, StringComparison.Ordinal)
            || !string.Equals(generatedPrediction.RhsRPN, canonicalInstance.RightRpn, StringComparison.Ordinal))
            return false;
        EmlLawDomainID expectedDomain = EmlLawDomainID.Derive(law.Law, law.Certificate, law.Proof, support.CanonicalAuthorityID);
        if (domain != expectedDomain) return false;
        return EmlPatternGrammarAdmissionReceipt.TryAdmit(
            // EmlVerifiedLaw is the validation authority for this seam.  Rung-0
            // guard eligibility is a different admission path and must not turn a
            // validated law into a proposal-only promotion by accident.
            authorityVerified: true,
            expectedDomain,
            domain,
            support.CanonicalAuthorityID,
            support.CanonicalAuthorityID,
            support.SupportSetDigest,
            support.CandidateAdmissionID,
            support.CandidatePackageDigest,
            law.Proof.AbsentFiller,
            generatedPrediction,
            admissionRevision,
            generatedPredictionCount: 1,
            fillerAmplification: 1,
            out receipt,
            out status);
    }
}

/// Focused mechanism fixture for the side receipt and the appended lineage edge.
internal static class EmlPatternGrammarAdmissionFixture
{
    internal static bool Verify(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string digest = new string('a', 64);
        EmlLawDomainID domain = EmlLawDomainID.DeriveForFixture("11?E1EE1E = ?", "authority:fixture");
        EmlLawDomainID foreign = EmlLawDomainID.DeriveForFixture("xx?E1EE = 11?E1EE", "authority:fixture");
        EmlPatternGrammarGeneratedPrediction claim = EmlPatternGrammarGeneratedPrediction.Create(
            new EmlPredictionID(42), new TapeEventID(10), new TapeEventID(11), "11xE1EE1E", "x");
        bool admitted = EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, domain, domain, "authority:fixture", "authority:fixture", digest,
            "law-admission-fixture", digest, "1", claim, new GrammarRevisionID(7),
            1, 1, out EmlPatternGrammarAdmissionReceipt? receipt,
            out EmlPatternGrammarAdmissionValidationStatuses status);
        bool roundTrip = false;
        bool lineOnly = false;
        if (receipt is not null)
        {
            EmlPatternGrammarAdmissionReceipt restored = EmlPatternGrammarAdmissionReceipt.Decode(receipt.Encode());
            roundTrip = restored.Digest == receipt.Digest;
            lineOnly = restored.GeneratedPrediction.CreateLinePayload().AsSpan().SequenceEqual(Encoding.ASCII.GetBytes("11xE1EE1E = x"));
        }

        bool proposalOnly = !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            false, domain, domain, "a", "a", digest, "admission", digest, "1", claim, new GrammarRevisionID(1), 1, 1,
            out _, out EmlPatternGrammarAdmissionValidationStatuses proposalStatus)
            && proposalStatus == EmlPatternGrammarAdmissionValidationStatuses.ProposalOnly;
        bool foreignSupport = !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, domain, domain, "a", "b", digest, "admission", digest, "1", claim, new GrammarRevisionID(1), 1, 1,
            out _, out EmlPatternGrammarAdmissionValidationStatuses supportStatus)
            && supportStatus == EmlPatternGrammarAdmissionValidationStatuses.ForeignSupport;
        bool foreignDomain = !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, domain, foreign, "a", "a", digest, "admission", digest, "1", claim, new GrammarRevisionID(1), 1, 1,
            out _, out EmlPatternGrammarAdmissionValidationStatuses domainStatus)
            && domainStatus == EmlPatternGrammarAdmissionValidationStatuses.ForeignDomain;
        bool freeFormDomain = !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, default, default, "a", "a", digest, "admission", digest, "1", claim, new GrammarRevisionID(1), 1, 1,
            out _, out EmlPatternGrammarAdmissionValidationStatuses freeFormStatus)
            && freeFormStatus == EmlPatternGrammarAdmissionValidationStatuses.Invalid;
        bool nonReducing = !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, domain, domain, "a", "a", digest, "admission", digest, "1", claim with { LhsRPN = "x", RhsRPN = "x", Line = "x = x" }, new GrammarRevisionID(1), 1, 1,
            out _, out EmlPatternGrammarAdmissionValidationStatuses rankStatus)
            && rankStatus == EmlPatternGrammarAdmissionValidationStatuses.NonRankReducing;
        bool amplified = !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, domain, domain, "a", "a", digest, "admission", digest, "1", claim, new GrammarRevisionID(1), 1, 2,
            out _, out EmlPatternGrammarAdmissionValidationStatuses fillerStatus)
            && fillerStatus == EmlPatternGrammarAdmissionValidationStatuses.FillerAmplification;
        bool swappedEvents = receipt is not null && !EmlPatternGrammarAdmissionReceipt.TryAdmit(
            true, domain, domain, "authority:fixture", "authority:fixture", digest,
            "law-admission-fixture", digest, "1",
            claim with { LawExecutionEventID = new TapeEventID(11), SupportEventID = new TapeEventID(10) },
            new GrammarRevisionID(7), 1, 1, out _, out _);
        EmlPatternGrammarAdmissionReceipt? settled = receipt?.BindConsumption(
            new GrammarRevisionID(8), new TapeEventID(12), new LoopLineageNodeID("promotion-lineage-node"));
        bool settledRoundTrip = settled is not null
            && EmlPatternGrammarAdmissionReceipt.Decode(settled.Encode()).Consumed
            && settled.ConsumedGrammarRevision == new GrammarRevisionID(8);
        bool consumedRevisionRejected = false;
        try { _ = receipt?.BindConsumption(new GrammarRevisionID(7), new TapeEventID(12), new LoopLineageNodeID("promotion-lineage-node")); }
        catch (InvalidDataException) { consumedRevisionRejected = true; }
        bool claimIDMutationRejected = receipt is not null
            && RejectReceiptMutation(receipt, static document => document.claimID = (int)document.lawExecutionEventID);
        bool lawExecutionEventMutationRejected = receipt is not null
            && RejectReceiptMutation(receipt, static document => document.lawExecutionEventID = document.claimID);
        bool supportEventMutationRejected = receipt is not null
            && RejectReceiptMutation(receipt, static document => document.supportEventID = document.claimID);
        bool consumedRevisionMutationRejected = settled is not null
            && RejectReceiptMutation(settled, static document => document.consumedGrammarRevision = document.admissionRevision);
        bool reflectedEventMutationRejected = settled is not null
            && RejectReceiptMutation(settled, static document => document.reflectedTapeEventID = document.lawExecutionEventID);
        bool lineageMutationRejected = settled is not null
            && RejectReceiptMutation(settled, static document => document.lineageNodeID = "other-lineage-node");
        bool identityMutations = claimIDMutationRejected && lawExecutionEventMutationRejected && supportEventMutationRejected;
        bool settlementMutations = consumedRevisionMutationRejected && reflectedEventMutationRejected && lineageMutationRejected;
        bool mechanism = settled is not null
            && EmlPatternGrammarAdmissionMechanismReport.FromReceipt(settled).SpeciesName
                == EmlPatternGrammarAdmissionMechanismReport.WireSpecies;
        bool ordinal = (byte)LoopLineageNodeSpecies.PatternGrammarAdmission
            == (byte)LoopLineageNodeSpecies.NewTapeEvidence + 1;
        bool lineage = VerifyAdmissionLineage();
        bool production = VerifyProductionFold(output);
        bool economics = EmlPatternGrammarAdmissionEconomicsFixture.Verify(output);
        bool passed = admitted && status == EmlPatternGrammarAdmissionValidationStatuses.Admitted
            && roundTrip && lineOnly && settledRoundTrip && proposalOnly && foreignSupport && foreignDomain && freeFormDomain
            && nonReducing && amplified && swappedEvents && consumedRevisionRejected && identityMutations && settlementMutations
            && mechanism && ordinal && lineage && production && economics;
        output.WriteLine($"  theory-to-grammar promotion fixture · admitted={(admitted ? "yes" : "no")} · round-trip={(roundTrip ? "exact" : "DRIFT")} · settled={(settledRoundTrip ? "bound" : "BROKEN")} · identities={(identityMutations ? "bound" : "BROKEN")} · settlement={(settlementMutations ? "bound" : "BROKEN")} · nulls={(proposalOnly && foreignSupport && foreignDomain && freeFormDomain && nonReducing && amplified && swappedEvents && consumedRevisionRejected ? "typed" : "BROKEN")} · ordinal={(ordinal ? "append" : "DRIFT")} · lineage={(lineage ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyProductionFold(TextWriter output)
    {
        using Tape tape = new();
        using Loom loom = new(256, '\n', 1);
        Journal journal = new();
        byte[] repeatedPrediction = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("11xE1EE1E = x\n", 64)));
        TapeEventID execution = tape.Append(repeatedPrediction, "eml:law-execution", Provenances.Real, TapeEventRoles.GrammarInput);
        TapeEventID support = tape.Append(repeatedPrediction, "eml:law-support", Provenances.Real, TapeEventRoles.GrammarInput);
        EmlLawDomainID domain = EmlLawDomainID.DeriveForFixture("11?E1EE1E = ?", "authority:fixture");
        EmlPatternGrammarGeneratedPrediction claim = EmlPatternGrammarGeneratedPrediction.Create(
            new EmlPredictionID(42), execution, support, "11xE1EE1E", "x");
        string candidatePackageDigest = Convert.ToHexStringLower(SHA256.HashData(claim.CreateLinePayload()));
        if (!EmlPatternGrammarAdmissionReceipt.TryAdmit(true, domain, domain, "authority:fixture", "authority:fixture",
                new string('a', 64), "law-admission-fixture", candidatePackageDigest, claim.LhsRPN, claim,
                new GrammarRevisionID(7), 1, 1, out EmlPatternGrammarAdmissionReceipt? admitted, out _)
            || admitted is null)
            return false;
        EmlLawStore store = new();
        EmlPatternGrammarAdmissionEconomicsReceipt economics;
        if (!store.EnsurePatternGrammarAdmission(
                admitted, tape, journal, 1, out EmlPatternGrammarAdmissionReceipt? ensured, 1)
            || ensured is null)
            return false;
        economics = store.PatternGrammarAdmissionEconomics[0].Receipt;
        if (!economics.MaterializationAdmitted || economics.AdmissionIdentityDigest != admitted.Digest)
            return false;
        EmlPatternGrammarAdmissionReceipt pending = store.PatternGrammarAdmissions.Single();
        TapeDelta delta = tape.DrainDelta();
        TapeEventID reflected = pending.ReflectedTapeEventID!.Value;
        bool oneOrdinaryLine = delta.Appended.Length == 4
            && tape.TryGetEventView(reflected, out TapeEventView reflectedView)
            && reflectedView.Source == "eml:theory-grammar"
            && reflectedView.Provenance == Provenances.Reflected
            && tape.Resolve(reflected, out byte[] reflectedPayload)
            && reflectedPayload.AsSpan().SequenceEqual(claim.CreateLinePayload());
        byte[] pendingImage = SaveStore(store);
        bool appendOnlyRejected = !store.SettlePatternGrammarAdmissions(new GrammarRevisionID(8), [reflected], static _ => false, null, tape, journal, 2);
        loom.ApplyTapeDelta(tape, in delta);
        loom.Pump();
        bool folded = loom.ParsedLenOf(reflected.Value) >= 0;
        bool sameRevisionRejected = !store.SettlePatternGrammarAdmissions(new GrammarRevisionID(7), [reflected], _ => true, null, tape, journal, 3);
        bool staleRejected = !store.SettlePatternGrammarAdmissions(new GrammarRevisionID(6), [reflected], _ => true, null, tape, journal, 3);
        TapeEventID wrongProvenance = tape.Append(claim.CreateLinePayload(), "eml:theory-grammar", Provenances.Real);
        TapeEventID duplicate = tape.Append(claim.CreateLinePayload(), "eml:theory-grammar", Provenances.Reflected);
        bool foreignRejected = !store.SettlePatternGrammarAdmissions(new GrammarRevisionID(8), [wrongProvenance], _ => true, null, tape, journal, 4);
        bool duplicateWrongIDRejected = !store.SettlePatternGrammarAdmissions(new GrammarRevisionID(8), [duplicate], _ => true, null, tape, journal, 4);
        bool settledExact = store.SettlePatternGrammarAdmissions(new GrammarRevisionID(8), [reflected], id => id == reflected && loom.ParsedLenOf(id.Value) >= 0, null, tape, journal, 5)
            && store.PatternGrammarAdmissions.Single().Consumed;
        byte[] fullImage;
        using (MemoryStream full = new()) { using (CkptWriter writer = new(full)) store.Save(writer); fullImage = full.ToArray(); }
        EmlLawStore resumed = new();
        using (MemoryStream full = new(fullImage)) using (CkptReader reader = new(full)) resumed.Load(reader);
        bool fullRoundTrip = SaveStore(resumed).AsSpan().SequenceEqual(fullImage);
        EmlLawStore settledTarget = LoadStore(pendingImage);
        bool deltaSettled = settledTarget.SettlePatternGrammarAdmissions(new GrammarRevisionID(8), [reflected], _ => true, null, tape, journal, 6);
        EmlLawStoreCheckpointDelta stateDelta = settledTarget.CaptureCheckpointDelta();
        using MemoryStream deltaBytes = new();
        using (CkptWriter deltaWriter = new(deltaBytes)) EmlLawStore.WriteCheckpointDelta(deltaWriter, in stateDelta);
        deltaBytes.Position = 0;
        EmlLawStoreCheckpointDelta decodedStateDelta;
        using (CkptReader deltaReader = new(deltaBytes)) decodedStateDelta = EmlLawStore.ReadCheckpointDelta(deltaReader);
        EmlLawStore deltaResumed = LoadStore(pendingImage);
        deltaResumed.ApplyCheckpointDelta(in decodedStateDelta);
        bool deltaRoundTrip = deltaSettled && SaveStore(deltaResumed).AsSpan().SequenceEqual(SaveStore(settledTarget));
        EmlLawStore mutationResumed = LoadStore(pendingImage);
        EmlPatternGrammarAdmissionReceipt mutated = admitted.BindReflection(new TapeEventID(reflected.Value + 10));
        EmlLawStoreCheckpointDelta mutatedDelta = decodedStateDelta with
        {
            PatternGrammarAdmissionUpdates = [new(admitted.AuthorityID, admitted.Domain.Value, mutated)]
        };
        bool identityMutationRejected = false;
        try { mutationResumed.ApplyCheckpointDelta(in mutatedDelta); }
        catch (InvalidDataException) { identityMutationRejected = true; }
        bool idempotent = store.EnsurePatternGrammarAdmission(admitted, tape, journal, 7, out EmlPatternGrammarAdmissionReceipt? replayedAdmission, 1)
            && replayedAdmission?.ReflectedTapeEventID == reflected;
        output.WriteLine($"  theory-to-grammar production fold · append={(oneOrdinaryLine ? "exact" : "BROKEN")} · folded={(folded ? "yes" : "no")} · delayed={(settledExact ? "settled" : "BROKEN")} · full={(fullRoundTrip ? "exact" : "DRIFT")} · delta={(deltaRoundTrip ? "exact" : "DRIFT")} · guards={(appendOnlyRejected && sameRevisionRejected && staleRejected && foreignRejected && duplicateWrongIDRejected && identityMutationRejected && idempotent ? "typed" : "BROKEN")}");
        return oneOrdinaryLine && folded && appendOnlyRejected && sameRevisionRejected && staleRejected
            && foreignRejected && duplicateWrongIDRejected && settledExact && fullRoundTrip && deltaRoundTrip && identityMutationRejected && idempotent;
    }

    private static byte[] SaveStore(EmlLawStore store)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) store.Save(writer);
        return stream.ToArray();
    }

    private static EmlLawStore LoadStore(byte[] image)
    {
        EmlLawStore store = new();
        using MemoryStream stream = new(image);
        using CkptReader reader = new(stream);
        store.Load(reader);
        return store;
    }


    private static bool RejectReceiptMutation(
        EmlPatternGrammarAdmissionReceipt receipt,
        Action<EmlPatternGrammarAdmissionReceiptRON> mutate)
    {
        try
        {
            EmlPatternGrammarAdmissionReceiptRON document =
                RonSerializer.Deserialize<EmlPatternGrammarAdmissionReceiptRON>(receipt.Encode());
            mutate(document);
            _ = EmlPatternGrammarAdmissionReceipt.Decode(RonSerializer.SerializeToUtf8(in document));
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static bool VerifyAdmissionLineage()
    {
        byte[] worldPayload = "world"u8.ToArray();
        byte[] lawPayload = "law"u8.ToArray();
        byte[] supportPayload = "support"u8.ToArray();
        byte[] promotionPayload = "11xE1EE1E = x"u8.ToArray();
        LoopLineageNode world = new(new("promotion-world"), LoopLineageNodeSpecies.AdmissionPlan, new(0), Digest(worldPayload), null, new("promotion-fixture"));
        LoopLineageNode law = new(new("promotion-law"), LoopLineageNodeSpecies.VerifiedLaw, new(1), Digest(lawPayload), null, LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLaw, [world.NodeID]));
        LoopLineageNode support = new(new("promotion-support"), LoopLineageNodeSpecies.VerifiedLawSupport, new(2), Digest(supportPayload), null, LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, [world.NodeID, law.NodeID]));
        LoopLineageNode promotion = new(new("promotion-node"), LoopLineageNodeSpecies.PatternGrammarAdmission, new(3), Digest(promotionPayload), new GrammarRevisionID(7), LoopLineageCausalID.Merge(LoopLineageNodeSpecies.PatternGrammarAdmission, [law.NodeID, support.NodeID]));
        LoopLineageEdgeReceipt first = LoopLineageEdgeReceipt.Create(new("promotion-edge-0"), world, [], [], "");
        LoopLineageEdgeReceipt second = LoopLineageEdgeReceipt.Create(new("promotion-edge-1"), law, [world.NodeID], [world.PayloadSHA256], first.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt third = LoopLineageEdgeReceipt.Create(new("promotion-edge-2"), support, [world.NodeID, law.NodeID], [world.PayloadSHA256, law.PayloadSHA256], second.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt fourth = LoopLineageEdgeReceipt.Create(new("promotion-edge-3"), promotion, [law.NodeID, support.NodeID], [law.PayloadSHA256, support.PayloadSHA256], third.CanonicalLineageSHA256);
        return LoopLineageVerifier.Verify([first, second, third, fourth]).Passed;
    }

    private static string Digest(byte[] payload) => Convert.ToHexStringLower(SHA256.HashData(payload));
}
