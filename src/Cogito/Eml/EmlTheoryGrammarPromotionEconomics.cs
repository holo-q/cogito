namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

/// The decision made after a verified law has produced one ordinary claim.  A
/// refusal is a real outcome: the law and its derivation remain durable, but no
/// grammar-input event is admitted.
public enum EmlPatternGrammarAdmissionEconomicsDecisionKinds : byte
{
    RefusedNonPositiveMarginalMdl,
    Admitted,
}

/// A typed, integer-only price for one candidate materialization.  LiteralCost
/// is the standing grammar plus the candidate as residual terminals; the
/// materialized cost is the exact re-induction of the same framed corpus.  The
/// difference is therefore a marginal admission price, not a forecast.
public readonly record struct EmlPatternGrammarAdmissionMdlPrice(
    long LiteralCostMbits,
    long MaterializedCostMbits,
    long MarginalSavingsMbits)
{
    public bool IsPositive => MarginalSavingsMbits > 0;

    public void Validate()
    {
        if (MarginalSavingsMbits != checked(LiteralCostMbits - MaterializedCostMbits))
            throw new InvalidDataException("theory-to-grammar marginal MDL delta does not close");
    }
}

/// Durable economics receipt for one exact law/claim candidate.  The boolean
/// funnel fields are deliberately persisted: a refusal must show that
/// eligibility, claim selection, rank proof, and pricing all happened before
/// the final materialization decision.
public sealed class EmlPatternGrammarAdmissionEconomicsReceipt
{
    public const int SchemaVersion = 1;
    public const string WireSpecies = "theory_to_grammar_marginal_mdl";

    private EmlPatternGrammarAdmissionEconomicsReceipt(
        string authorityID,
        string supportSetDigest,
        string admissionID,
        EmlLawDomainID domain,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        string candidateSHA256,
        GrammarRevisionID admissionRevision,
        string promotionIdentityDigest,
        int wScale,
        string pricingBasisDigest,
        int baselineRuleCount,
        int baselineCompressedLength,
        int rawSymbolLength,
        int rawWeightLength,
        bool eligible,
        bool claimSelected,
        bool rankVerified,
        bool mdlPriced,
        bool materializationAdmitted,
        EmlPatternGrammarAdmissionMdlPrice price,
        EmlPatternGrammarAdmissionEconomicsDecisionKinds decision,
        string digest)
    {
        AuthorityID = authorityID;
        SupportSetDigest = supportSetDigest;
        AdmissionID = admissionID;
        Domain = domain;
        GeneratedPrediction = generatedPrediction;
        CandidateSHA256 = candidateSHA256;
        AdmissionRevision = admissionRevision;
        AdmissionIdentityDigest = promotionIdentityDigest;
        WScale = wScale;
        PricingBasisDigest = pricingBasisDigest;
        BaselineRuleCount = baselineRuleCount;
        BaselineCompressedLength = baselineCompressedLength;
        RawSymbolLength = rawSymbolLength;
        RawWeightLength = rawWeightLength;
        Eligible = eligible;
        PredictionSelected = claimSelected;
        RankVerified = rankVerified;
        MdlPriced = mdlPriced;
        MaterializationAdmitted = materializationAdmitted;
        Price = price;
        Decision = decision;
        Digest = digest;
    }

    public string AuthorityID { get; }
    public string SupportSetDigest { get; }
    public string AdmissionID { get; }
    public EmlLawDomainID Domain { get; }
    public EmlPatternGrammarGeneratedPrediction GeneratedPrediction { get; }
    public string CandidateSHA256 { get; }
    public GrammarRevisionID AdmissionRevision { get; }
    public string AdmissionIdentityDigest { get; }
    public int WScale { get; }
    public string PricingBasisDigest { get; }
    public int BaselineRuleCount { get; }
    public int BaselineCompressedLength { get; }
    public int RawSymbolLength { get; }
    public int RawWeightLength { get; }
    public bool Eligible { get; }
    public bool PredictionSelected { get; }
    public bool RankVerified { get; }
    public bool MdlPriced { get; }
    public bool MaterializationAdmitted { get; }
    public EmlPatternGrammarAdmissionMdlPrice Price { get; }
    public EmlPatternGrammarAdmissionEconomicsDecisionKinds Decision { get; }
    public string Digest { get; }

    public bool IsRefusal => Decision == EmlPatternGrammarAdmissionEconomicsDecisionKinds.RefusedNonPositiveMarginalMdl;
    public string IdentityKey => string.Join('\u0001', AuthorityID, Domain.Value, SupportSetDigest, AdmissionID, CandidateSHA256, AdmissionRevision.Value, AdmissionIdentityDigest, WScale, PricingBasisDigest, BaselineRuleCount, BaselineCompressedLength, RawSymbolLength, RawWeightLength);

    internal static EmlPatternGrammarAdmissionEconomicsReceipt CreateFromInduced(
        string authorityID,
        string supportSetDigest,
        string admissionID,
        EmlLawDomainID domain,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        GrammarRevisionID admissionRevision,
        int wScale)
    {
        generatedPrediction.Validate();
        if (string.IsNullOrWhiteSpace(authorityID) || string.IsNullOrWhiteSpace(supportSetDigest)
            || string.IsNullOrWhiteSpace(admissionID) || !domain.IsValid)
            throw new InvalidDataException("theory-to-grammar economics identity is incomplete");
        Tape.RequireWScale(wScale);
        if (rawTape.Length != rawWeights.Length)
            throw new InvalidDataException("theory-to-grammar economics symbols and weights differ");
        return CreateWithPrice(authorityID, supportSetDigest, admissionID, domain, generatedPrediction,
            PriceMaterialization(in baseline, rawTape, rawWeights, generatedPrediction.CreateLinePayload(), wScale), admissionRevision, wScale,
            ComputeBasisDigest(in baseline, rawTape, rawWeights, generatedPrediction.CreateLinePayload(), wScale), baseline.Rules.Length, baseline.Compressed.Length, rawTape.Length, rawWeights.Length);
    }

    private static EmlPatternGrammarAdmissionEconomicsReceipt CreateWithPrice(
        string authorityID,
        string supportSetDigest,
        string admissionID,
        EmlLawDomainID domain,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        EmlPatternGrammarAdmissionMdlPrice price,
        GrammarRevisionID admissionRevision,
        int wScale,
        string pricingBasisDigest,
        int baselineRuleCount,
        int baselineCompressedLength,
        int rawSymbolLength,
        int rawWeightLength)
    {
        generatedPrediction.Validate();
        bool admitted = price.IsPositive;
        string candidateSHA256 = DigestBytes(generatedPrediction.CreateLinePayload());
        if (admissionRevision == GrammarRevisionID.Zero) admissionRevision = new GrammarRevisionID(1);
        string promotionIdentityDigest = EmlPatternGrammarAdmissionReceipt.Create(
            domain, authorityID, authorityID, supportSetDigest, admissionID, candidateSHA256,
            generatedPrediction.LhsRPN, generatedPrediction, admissionRevision).Digest;
        EmlPatternGrammarAdmissionEconomicsReceipt candidate = new(
            authorityID, supportSetDigest, admissionID, domain, generatedPrediction, candidateSHA256,
            admissionRevision, promotionIdentityDigest,
            wScale,
            pricingBasisDigest, baselineRuleCount, baselineCompressedLength, rawSymbolLength, rawWeightLength,
            eligible: true, claimSelected: true, rankVerified: true, mdlPriced: true,
            materializationAdmitted: admitted,
            price,
            admitted
                ? EmlPatternGrammarAdmissionEconomicsDecisionKinds.Admitted
                : EmlPatternGrammarAdmissionEconomicsDecisionKinds.RefusedNonPositiveMarginalMdl,
            "");
        EmlPatternGrammarAdmissionEconomicsReceipt receipt = new(
            authorityID, supportSetDigest, admissionID, domain, generatedPrediction, candidateSHA256,
            admissionRevision, promotionIdentityDigest,
            wScale,
            pricingBasisDigest, baselineRuleCount, baselineCompressedLength, rawSymbolLength, rawWeightLength,
            true, true, true, true, admitted, price, candidate.Decision, candidate.ComputeDigest());
        receipt.Validate();
        return receipt;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AuthorityID) || string.IsNullOrWhiteSpace(SupportSetDigest)
            || string.IsNullOrWhiteSpace(AdmissionID) || !Domain.IsValid
            || !IsDigest(SupportSetDigest) || !IsDigest(CandidateSHA256)
            || AdmissionRevision == GrammarRevisionID.Zero || !IsDigest(AdmissionIdentityDigest)
            || WScale < 1 || WScale > 128 || (WScale & (WScale - 1)) != 0
            || !IsDigest(PricingBasisDigest) || BaselineRuleCount < 0 || BaselineCompressedLength < 0
            || RawSymbolLength < 0 || RawWeightLength != RawSymbolLength
            || !Eligible || !PredictionSelected || !RankVerified || !MdlPriced
            || MaterializationAdmitted != (Decision == EmlPatternGrammarAdmissionEconomicsDecisionKinds.Admitted)
            || (Decision is not EmlPatternGrammarAdmissionEconomicsDecisionKinds.Admitted
                and not EmlPatternGrammarAdmissionEconomicsDecisionKinds.RefusedNonPositiveMarginalMdl))
            throw new InvalidDataException("theory-to-grammar economics receipt funnel is malformed");
        GeneratedPrediction.Validate();
        Price.Validate();
        if (!string.Equals(CandidateSHA256, DigestBytes(GeneratedPrediction.CreateLinePayload()), StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar economics receipt candidate digest changed");
        string promotionIdentityDigest = EmlPatternGrammarAdmissionReceipt.Create(
            Domain, AuthorityID, AuthorityID, SupportSetDigest, AdmissionID, CandidateSHA256,
            GeneratedPrediction.LhsRPN, GeneratedPrediction, AdmissionRevision).Digest;
        if (!string.Equals(AdmissionIdentityDigest, promotionIdentityDigest, StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar economics receipt promotion identity changed");
        if (Decision == EmlPatternGrammarAdmissionEconomicsDecisionKinds.Admitted && !Price.IsPositive)
            throw new InvalidDataException("theory-to-grammar materialization was admitted without positive marginal MDL");
        if (Decision == EmlPatternGrammarAdmissionEconomicsDecisionKinds.RefusedNonPositiveMarginalMdl && Price.IsPositive)
            throw new InvalidDataException("theory-to-grammar refusal has positive marginal MDL");
        if (!IsDigest(Digest) || !string.Equals(Digest, ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar economics receipt digest changed");
    }

    public byte[] Encode()
    {
        Validate();
        EmlPatternGrammarAdmissionEconomicsReceiptRON document = new()
        {
            schemaVersion = SchemaVersion,
            species = WireSpecies,
            authorityID = AuthorityID,
            supportSetDigest = SupportSetDigest,
            admissionID = AdmissionID,
            domain = Domain.Value,
            templateSHA256 = Domain.TemplateSHA256,
            behaviorSHA256 = Domain.BehaviorSHA256,
            guardSHA256 = Domain.GuardSHA256,
            authoritySHA256 = Domain.AuthoritySHA256,
            claimID = GeneratedPrediction.PredictionID.Value,
            lawExecutionEventID = GeneratedPrediction.LawExecutionEventID.Value,
            supportEventID = GeneratedPrediction.SupportEventID.Value,
            line = GeneratedPrediction.Line,
            lhsRPN = GeneratedPrediction.LhsRPN,
            rhsRPN = GeneratedPrediction.RhsRPN,
            candidateSHA256 = CandidateSHA256,
            admissionRevision = checked((long)AdmissionRevision.Value),
            promotionIdentityDigest = AdmissionIdentityDigest,
            wScale = WScale,
            pricingBasisDigest = PricingBasisDigest,
            baselineRuleCount = BaselineRuleCount,
            baselineCompressedLength = BaselineCompressedLength,
            rawSymbolLength = RawSymbolLength,
            rawWeightLength = RawWeightLength,
            eligible = Eligible,
            claimSelected = PredictionSelected,
            rankVerified = RankVerified,
            mdlPriced = MdlPriced,
            materializationAdmitted = MaterializationAdmitted,
            literalCostMbits = Price.LiteralCostMbits,
            materializedCostMbits = Price.MaterializedCostMbits,
            marginalSavingsMbits = Price.MarginalSavingsMbits,
            decision = (byte)Decision,
            digest = Digest,
        };
        return RonSerializer.SerializeToUtf8(in document);
    }

    public static EmlPatternGrammarAdmissionEconomicsReceipt Decode(ReadOnlySpan<byte> bytes)
    {
        EmlPatternGrammarAdmissionEconomicsReceiptRON document = RonSerializer.Deserialize<EmlPatternGrammarAdmissionEconomicsReceiptRON>(bytes);
        if (document.schemaVersion != SchemaVersion || document.species != WireSpecies)
            throw new InvalidDataException("unsupported theory-to-grammar economics receipt schema");
        EmlPatternGrammarAdmissionEconomicsReceipt receipt = new(
            document.authorityID, document.supportSetDigest, document.admissionID,
            EmlLawDomainID.Restore(document.domain, document.templateSHA256, document.behaviorSHA256, document.guardSHA256, document.authoritySHA256),
            new EmlPatternGrammarGeneratedPrediction(new EmlPredictionID(document.claimID), new TapeEventID(document.lawExecutionEventID), new TapeEventID(document.supportEventID), document.line, document.lhsRPN, document.rhsRPN),
            document.candidateSHA256, new GrammarRevisionID(checked((ulong)document.admissionRevision)), document.promotionIdentityDigest, document.wScale,
            document.pricingBasisDigest, document.baselineRuleCount, document.baselineCompressedLength, document.rawSymbolLength, document.rawWeightLength,
            document.eligible, document.claimSelected, document.rankVerified, document.mdlPriced,
            document.materializationAdmitted,
            new(document.literalCostMbits, document.materializedCostMbits, document.marginalSavingsMbits),
            (EmlPatternGrammarAdmissionEconomicsDecisionKinds)document.decision, document.digest);
        receipt.Validate();
        if (!receipt.Encode().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("theory-to-grammar economics receipt RON round-trip changed bytes");
        return receipt;
    }

    private static EmlPatternGrammarAdmissionMdlPrice PriceMaterialization(
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        ReadOnlySpan<byte> candidate,
        int wScale)
    {
        GrammarAdmissionMdlPrice price = GrammarAdmissionEconomics.PriceMaterialization(
            in baseline, rawTape, rawWeights, candidate, wScale);
        return new(price.LiteralCostMbits, price.MaterializedCostMbits, price.MarginalSavingsMbits);
    }

    internal static string ComputeBasisDigest(in RePairResult baseline, ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights, ReadOnlySpan<byte> candidate, int wScale)
    {
        return GrammarAdmissionEconomics.ComputeBasisDigest(in baseline, rawTape, rawWeights, candidate, wScale, "theory-to-grammar");
    }

    private string ComputeDigest()
        => DigestText(string.Join('|', SchemaVersion, WireSpecies, AuthorityID, SupportSetDigest, AdmissionID,
            Domain.Value, GeneratedPrediction.Line, GeneratedPrediction.PredictionID.Value, GeneratedPrediction.LawExecutionEventID.Value,
            GeneratedPrediction.SupportEventID.Value, CandidateSHA256, Eligible ? 1 : 0, PredictionSelected ? 1 : 0,
            AdmissionRevision.Value, AdmissionIdentityDigest, WScale, PricingBasisDigest, BaselineRuleCount, BaselineCompressedLength, RawSymbolLength, RawWeightLength,
            RankVerified ? 1 : 0, MdlPriced ? 1 : 0, MaterializationAdmitted ? 1 : 0,
            Price.LiteralCostMbits, Price.MaterializedCostMbits, Price.MarginalSavingsMbits, (byte)Decision));

    private static string DigestBytes(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string DigestText(string value)
        => DigestBytes(Encoding.UTF8.GetBytes(value));

    private static bool IsDigest(string? value)
        => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();
}

[RonObject]
internal partial class EmlPatternGrammarAdmissionEconomicsReceiptRON
{
    public int schemaVersion;
    public string species = "";
    public string authorityID = "";
    public string supportSetDigest = "";
    public string admissionID = "";
    public string domain = "";
    public string templateSHA256 = "";
    public string behaviorSHA256 = "";
    public string guardSHA256 = "";
    public string authoritySHA256 = "";
    public int claimID;
    public long lawExecutionEventID;
    public long supportEventID;
    public string line = "";
    public string lhsRPN = "";
    public string rhsRPN = "";
    public string candidateSHA256 = "";
    public long admissionRevision;
    public string promotionIdentityDigest = "";
    public int wScale;
    public string pricingBasisDigest = "";
    public int baselineRuleCount;
    public int baselineCompressedLength;
    public int rawSymbolLength;
    public int rawWeightLength;
    public bool eligible;
    public bool claimSelected;
    public bool rankVerified;
    public bool mdlPriced;
    public bool materializationAdmitted;
    public long literalCostMbits;
    public long materializedCostMbits;
    public long marginalSavingsMbits;
    public byte decision;
    public string digest = "";
}

/// Durable binding between the economics decision and its ordinary in-run
/// Measurement|Custody packet. The receipt remains the decision payload; this
/// row carries the tape address and exact payload digest needed for replay.
public sealed class EmlPatternGrammarAdmissionEconomicsRecord
{
    private EmlPatternGrammarAdmissionEconomicsRecord(
        EmlPatternGrammarAdmissionEconomicsReceipt receipt,
        TapeEventID? eventID,
        string payloadSHA256,
        string digest,
        JournalRowBinding? journalBinding)
    {
        Receipt = receipt;
        EventID = eventID;
        PayloadSHA256 = payloadSHA256;
        Digest = digest;
        JournalBinding = journalBinding;
    }

    public EmlPatternGrammarAdmissionEconomicsReceipt Receipt { get; }
    public TapeEventID? EventID { get; }
    public string PayloadSHA256 { get; }
    public string Digest { get; }
    public JournalRowBinding? JournalBinding { get; }
    public string IdentityKey => Receipt.IdentityKey;

    public static EmlPatternGrammarAdmissionEconomicsRecord Create(
        EmlPatternGrammarAdmissionEconomicsReceipt receipt,
        TapeEventID eventID,
        ReadOnlySpan<byte> payload,
        in JournalRowBinding journalBinding)
    {
        receipt.Validate();
        if (eventID.Value < 0) throw new InvalidDataException("theory-to-grammar economics packet event is invalid");
        string payloadSHA256 = Convert.ToHexStringLower(SHA256.HashData(payload));
        if (journalBinding.EventID != eventID || journalBinding.LineIndex < 0 || journalBinding.Step < 0
            || !string.Equals(journalBinding.Source, "eml:theory-grammar-economics", StringComparison.Ordinal)
            || !IsDigest(journalBinding.SHA256))
            throw new InvalidDataException("theory-to-grammar economics journal binding is invalid");
        string digest = ComputeDigest(receipt, eventID, payloadSHA256, journalBinding);
        EmlPatternGrammarAdmissionEconomicsRecord record = new(receipt, eventID, payloadSHA256, digest, journalBinding);
        record.Validate(payload);
        return record;
    }

    public void Validate(ReadOnlySpan<byte> payload)
    {
        Receipt.Validate();
        if (EventID is not TapeEventID eventID || eventID.Value < 0
            || JournalBinding is not JournalRowBinding journalBinding
            || journalBinding.EventID != eventID
            || (journalBinding.LineIndex < 0 || journalBinding.Step < 0)
            || !string.Equals(journalBinding.Source, "eml:theory-grammar-economics", StringComparison.Ordinal)
            || !IsDigest(journalBinding.SHA256)
            || !IsDigest(PayloadSHA256) || !IsDigest(Digest)
            || !string.Equals(PayloadSHA256, Convert.ToHexStringLower(SHA256.HashData(payload)), StringComparison.Ordinal)
            || !string.Equals(Digest, ComputeDigest(Receipt, EventID, PayloadSHA256, JournalBinding), StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar economics tape binding changed");
    }

    public byte[] Encode()
    {
        Validate(Receipt.Encode());
        EmlPatternGrammarAdmissionEconomicsRecordRON document = new()
        {
            receipt = Receipt.Encode(), hasEventID = EventID is not null, eventID = EventID?.Value ?? -1,
            hasJournalBinding = JournalBinding is not null,
            journalLineIndex = JournalBinding?.LineIndex ?? -1,
            journalStep = JournalBinding?.Step ?? -1,
            journalEventID = JournalBinding?.EventID.Value ?? -1,
            journalSource = JournalBinding?.Source ?? "",
            journalSHA256 = JournalBinding?.SHA256 ?? "",
            payloadSHA256 = PayloadSHA256, digest = Digest
        };
        return RonSerializer.SerializeToUtf8(in document);
    }

    public static EmlPatternGrammarAdmissionEconomicsRecord Decode(ReadOnlySpan<byte> bytes)
    {
        EmlPatternGrammarAdmissionEconomicsRecordRON document = RonSerializer.Deserialize<EmlPatternGrammarAdmissionEconomicsRecordRON>(bytes);
        EmlPatternGrammarAdmissionEconomicsReceipt receipt = EmlPatternGrammarAdmissionEconomicsReceipt.Decode(document.receipt);
        JournalRowBinding? journalBinding = document.hasJournalBinding
            ? new JournalRowBinding(document.journalLineIndex, document.journalStep, new TapeEventID(document.journalEventID), document.journalSource, document.journalSHA256)
            : null;
        EmlPatternGrammarAdmissionEconomicsRecord record = new(receipt, document.hasEventID ? new TapeEventID(document.eventID) : null, document.payloadSHA256, document.digest, journalBinding);
        record.Validate(receipt.Encode());
        if (!record.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("theory-to-grammar economics tape binding RON drift");
        return record;
    }

    private static string ComputeDigest(EmlPatternGrammarAdmissionEconomicsReceipt receipt, TapeEventID? eventID, string payloadSHA256, JournalRowBinding? journalBinding)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', receipt.Digest, eventID?.Value ?? -1, payloadSHA256,
            journalBinding?.LineIndex ?? -1, journalBinding?.Step ?? -1, journalBinding?.EventID.Value ?? -1,
            journalBinding?.Source ?? "", journalBinding?.SHA256 ?? ""))));

    private static bool IsDigest(string? value)
        => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();
}

[RonObject]
internal partial class EmlPatternGrammarAdmissionEconomicsRecordRON
{
    public byte[] receipt = [];
    public bool hasEventID;
    public long eventID;
    public bool hasJournalBinding;
    public int journalLineIndex;
    public int journalStep;
    public long journalEventID;
    public string journalSource = "";
    public string journalSHA256 = "";
    public string payloadSHA256 = "";
    public string digest = "";
}

/// Focused receipt fixture for the marginal-MDL admission boundary.  It proves
/// both sides of the decision, the no-grammar-input refusal, and byte-exact
/// persistence/replay without changing the theorem or derivation stores.
internal static class EmlPatternGrammarAdmissionEconomicsFixture
{
    internal static bool Verify(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        const string authority = "authority:economics-fixture";
        const string support = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string admission = "economics-admission-fixture";
        const string selfCompressingLhs = "111E1EE1111EE1EE111111EE1EE11EEE1EE11xE1EE1EE1EE1EE";
        EmlLawDomainID domain = EmlLawDomainID.DeriveForFixture("11?E1EE1E = ?", authority);
        EmlPatternGrammarGeneratedPrediction claim = EmlPatternGrammarGeneratedPrediction.Create(
            new EmlPredictionID(43), new TapeEventID(20), new TapeEventID(21), selfCompressingLhs, "x");
        EmlPatternGrammarGeneratedPrediction refusalPrediction = EmlPatternGrammarGeneratedPrediction.Create(
            new EmlPredictionID(44), new TapeEventID(20), new TapeEventID(21), "1xE", "y");

        using Tape refusalTape = new();
        TapeEventID theorem = refusalTape.Append("theorem"u8.ToArray(), "eml:theorem", Provenances.Real);
        Journal refusalJournal = new();
        int refusalGrammarBefore = refusalTape.GetEventViews().Count(view => view.Roles == TapeEventRoles.GrammarInput);
        (Symbol[] refusalRawTape, int refusalRawCount, RePairResult refusalBaseline) = Engine.Induce(refusalTape, 1);
        byte[] refusalWeights = refusalTape.GrammarWeightsFor(1);
        EmlLawStore refusalStore = new();
        EmlPatternGrammarAdmissionEconomicsReceipt refusal;
        try
        {
            refusal = refusalStore.EvaluatePatternGrammarAdmissionEconomics(
                authority, support, admission, domain, refusalPrediction, in refusalBaseline,
                refusalRawTape.AsSpan(0, refusalRawCount), refusalWeights.AsSpan(0, refusalRawCount),
                new GrammarRevisionID(1), 1, refusalTape, refusalJournal, 1);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(refusalWeights); }
        int tapeBefore = 1;
        bool refusalTyped = refusal.IsRefusal && refusal.Decision
            == EmlPatternGrammarAdmissionEconomicsDecisionKinds.RefusedNonPositiveMarginalMdl
            && refusal.Eligible && refusal.PredictionSelected && refusal.RankVerified && refusal.MdlPriced
            && !refusal.MaterializationAdmitted && refusal.Price.MarginalSavingsMbits <= 0
            && refusalStore.PatternGrammarAdmissions.Count == 0
            && refusalStore.PatternGrammarAdmissionEconomics.Count == 1
            && refusalTape.Count == tapeBefore + 1 && theorem.Value == 0
            && refusalTape.GetEventViews().Count(view => view.Roles == TapeEventRoles.GrammarInput) == refusalGrammarBefore;
        bool refusalRoundTrip = EmlPatternGrammarAdmissionEconomicsReceipt.Decode(refusal.Encode()).Digest == refusal.Digest;
        byte[] refusalImage = SaveStore(refusalStore);
        bool refusalFullRoundTrip = SaveStore(LoadStore(refusalImage)).AsSpan().SequenceEqual(refusalImage);

        using Tape eventTape = new();
        byte[] richCorpus = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("11xE1EE1E = x\n", 64)));
        eventTape.Append(richCorpus, "fixture:grammar", Provenances.Real, TapeEventRoles.GrammarInput);
        Journal eventJournal = new();
        (Symbol[] rawTape, int rawCount, RePairResult baseline) = Engine.Induce(eventTape, 1);
        byte[] rawWeights = eventTape.GrammarWeightsFor(1);
        EmlPatternGrammarAdmissionEconomicsReceipt eventReceipt;
        try
        {
            EmlLawStore eventStore = new();
            eventReceipt = eventStore.EvaluatePatternGrammarAdmissionEconomics(
                authority, support, admission, domain, claim, in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                new GrammarRevisionID(1), 1, eventTape, eventJournal, 1);
            int eventCount = eventTape.Count;
            EmlPatternGrammarAdmissionEconomicsReceipt replayed = eventStore.EvaluatePatternGrammarAdmissionEconomics(
                authority, support, admission, domain, claim, in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                new GrammarRevisionID(1), 1, eventTape, eventJournal, 2);
            EmlPatternGrammarAdmissionReceipt promotion = EmlPatternGrammarAdmissionReceipt.Create(
                domain, authority, authority, support, admission, eventReceipt.CandidateSHA256, claim.LhsRPN, claim, new GrammarRevisionID(1));
            if (!eventStore.EnsurePatternGrammarAdmission(
                    promotion, eventTape, eventJournal, 1, out EmlPatternGrammarAdmissionReceipt? reflected,
                    1) || reflected is null)
                return false;
            byte[] eventImage = SaveStore(eventStore);
            bool eventFullRoundTrip = SaveStore(LoadStore(eventImage)).AsSpan().SequenceEqual(eventImage);
            EmlLawStoreCheckpointDelta delta = eventStore.CaptureCheckpointDelta();
            using MemoryStream deltaStream = new();
            using (CkptWriter writer = new(deltaStream)) EmlLawStore.WriteCheckpointDelta(writer, in delta);
            deltaStream.Position = 0;
            EmlLawStoreCheckpointDelta decoded;
            using (CkptReader reader = new(deltaStream)) decoded = EmlLawStore.ReadCheckpointDelta(reader);
            EmlLawStore deltaResumed = new();
            deltaResumed.ApplyCheckpointDelta(in decoded);
            bool eventDeltaRoundTrip = SaveStore(deltaResumed).AsSpan().SequenceEqual(eventImage);
            EmlPatternGrammarAdmissionEconomicsRecord record = eventStore.PatternGrammarAdmissionEconomics[0];
            bool reflectedIdentity = reflected is not null
                && reflected.Domain == domain
                && string.Equals(reflected.AuthorityID, authority, StringComparison.Ordinal)
                && string.Equals(reflected.SupportSetDigest, support, StringComparison.Ordinal)
                && string.Equals(reflected.AdmissionID, admission, StringComparison.Ordinal)
                && string.Equals(reflected.CandidatePackageDigest, eventReceipt.CandidateSHA256, StringComparison.Ordinal)
                && reflected.GeneratedPrediction.Equals(claim)
                && reflected.AdmissionRevision == new GrammarRevisionID(1)
                && reflected.ReflectedTapeEventID is TapeEventID;
            bool packet = eventTape.Count == eventCount + 1 && replayed.Digest == eventReceipt.Digest
                && eventReceipt.AdmissionIdentityDigest == promotion.Digest
                && reflectedIdentity
                && record.EventID is TapeEventID && record.JournalBinding is JournalRowBinding journalBinding
                && eventJournal.VerifyBinding(in journalBinding)
                && eventTape.GetEventViews().Any(view => view.Source == "eml:theory-grammar-economics"
                    && view.Roles == (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                    && eventTape.Resolve(view.Id, out byte[] payload)
                    && payload.AsSpan().SequenceEqual(eventReceipt.Encode()));
            bool shedReplay = false;
            bool shedMutationRejected = false;
            bool shedOmissionRejected = false;
            string journalPath = Path.Combine(AppContext.BaseDirectory, "eml-theory-grammar-economics-fixture-journal.log");
            try
            {
                string[] residentLines = eventJournal.ResidentLines.ToArray();
                File.WriteAllText(journalPath,
                    Journal.LogHeader + Environment.NewLine + string.Join(Environment.NewLine, residentLines) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                using (FileStream journalFile = new(journalPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter journalSink = new(journalFile, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096))
                {
                    eventJournal.Mount(journalSink, journalPath);
                    eventJournal.CommitCheckpointLines();
                    journalSink.Flush();
                }
                EmlPatternGrammarAdmissionEconomicsReceipt shedReceipt = eventStore.EvaluatePatternGrammarAdmissionEconomics(
                    authority, support, admission, domain, claim, in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                    new GrammarRevisionID(1), 1, eventTape, eventJournal, 3);
                shedReplay = shedReceipt.Digest == eventReceipt.Digest;
                // Tamper the row the binding ACTUALLY names, and only once that row is shed. A null
                // that mutates an arbitrary line proves nothing: a resident row is verified against
                // the in-RAM authority, so the disk edit is invisible and the arm reads ACCEPTED for
                // a reason that has nothing to do with the custody it claims to test.
                if (record.JournalBinding is not JournalRowBinding boundRow)
                    throw new InvalidDataException("theory-to-grammar economics journal binding was omitted before the shed nulls");
                if (boundRow.LineIndex >= eventJournal.ShedLineCount)
                    throw new InvalidDataException($"theory-to-grammar economics shed null is unfaithful: bound row {boundRow.LineIndex} is still resident (shed={eventJournal.ShedLineCount})");
                string[] diskLines = File.ReadAllLines(journalPath);
                diskLines[boundRow.LineIndex + 1] += "-mutated";        // +1 — the log header occupies disk line 0
                File.WriteAllText(journalPath, string.Join(Environment.NewLine, diskLines) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                try
                {
                    _ = eventStore.EvaluatePatternGrammarAdmissionEconomics(
                        authority, support, admission, domain, claim, in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                        new GrammarRevisionID(1), 1, eventTape, eventJournal, 4);
                }
                catch (InvalidDataException) { shedMutationRejected = true; }
                File.WriteAllText(journalPath, Journal.LogHeader + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                try
                {
                    _ = eventStore.EvaluatePatternGrammarAdmissionEconomics(
                        authority, support, admission, domain, claim, in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                        new GrammarRevisionID(1), 1, eventTape, eventJournal, 5);
                }
                catch (InvalidDataException) { shedOmissionRejected = true; }
            }
            finally
            {
                try { eventJournal.Dispose(); }
                finally
                {
                    if (File.Exists(journalPath)) File.Delete(journalPath);
                }
            }
            bool admittedTyped = eventReceipt.Decision == EmlPatternGrammarAdmissionEconomicsDecisionKinds.Admitted
                && eventReceipt.MaterializationAdmitted && eventReceipt.Price.IsPositive;
            bool replayExact = eventStore.PatternGrammarAdmissionEconomics.Count == 1
                && eventStore.PatternGrammarAdmissions.Count == 1;
            bool passed = refusalTyped && refusalRoundTrip && refusalFullRoundTrip && admittedTyped
            && replayExact && eventFullRoundTrip && eventDeltaRoundTrip && packet
            && shedReplay && shedMutationRejected && shedOmissionRejected;
            output.WriteLine($"  theory-to-grammar economics · funnel=eligible→claim→rank→mdl · refusal={(refusalTyped ? "typed" : "BROKEN")} · positive={(admittedTyped ? "admitted" : "REFUSED")} · replay={(replayExact && packet ? "exact" : "DRIFT")} · shed-lines={eventJournal.ShedLineCount} · shed-replay={(shedReplay ? "exact" : "DRIFT")} · shed-mutation={(shedMutationRejected ? "rejected" : "ACCEPTED")} · shed-omission={(shedOmissionRejected ? "rejected" : "ACCEPTED")} · checkpoint={(eventFullRoundTrip && eventDeltaRoundTrip ? "exact" : "DRIFT")} · {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(rawWeights); }
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
        using (CkptReader reader = new(stream)) store.Load(reader);
        return store;
    }
}
