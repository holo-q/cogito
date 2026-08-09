namespace Cogito;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

/// Exact identity for a source-backed repository conclusion. The value is the
/// canonical prediction digest; arbitrary labels cannot enter the pattern protocol.
public readonly record struct RepositoryPatternPredictionID(string Value)
{
    public static RepositoryPatternPredictionID Create(RepositoryPrediction prediction)
    {
        prediction.Validate();
        return new(prediction.SHA256);
    }

    public bool IsValid => IsSHA256(Value);

    public void Validate()
    {
        if (!IsValid) throw new InvalidDataException("repository pattern prediction identity is malformed");
    }

    private static bool IsSHA256(string value)
        => value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// Exact identity for a repository rule. The canonical rule form is hashed here
/// so checkpoint and frontier custody cannot substitute a human-readable label.
public readonly record struct RepositoryPatternRuleID(string Value)
{
    public static RepositoryPatternRuleID Create(string canonicalRule)
    {
        if (string.IsNullOrWhiteSpace(canonicalRule))
            throw new InvalidDataException("repository pattern rule canonical form is empty");
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRule))));
    }

    public bool IsValid => Value is { Length: 64 } && Value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public void Validate()
    {
        if (!IsValid) throw new InvalidDataException("repository pattern rule identity is malformed");
    }
}

public enum RepositoryPatternConclusionOrigins : byte
{
    SourcePrediction = 1,
    VerifiedPrediction = 2,
    Candidate = 3,
}

/// Exact source occurrence for a repository pattern prediction. The occurrenceCheck receipt
/// is a distinct tape event after the source and its predecessor; occurrence cannot
/// be reconstructed from a prediction label alone.
public readonly record struct RepositoryPatternOccurrence(
    RepositoryPatternPredictionID PredictionID,
    RepositoryPrediction Prediction,
    RepositoryOccurrenceCheckReceipt OccurrenceCheck,
    TapeEventID SourceEventID,
    TapeEventID OccurrenceCheckReceiptEventID)
{
    public string EvidenceSHA256 => OccurrenceCheck.EvidenceSHA256;

    public void Validate()
    {
        PredictionID.Validate();
        Prediction.Validate();
        OccurrenceCheck.Validate();
        if (SourceEventID.Value < 0
            || OccurrenceCheckReceiptEventID.Value <= OccurrenceCheck.PredecessorEventID.Value
            || OccurrenceCheck.PredecessorEventID.Value <= SourceEventID.Value
            || OccurrenceCheckReceiptEventID == SourceEventID
            || OccurrenceCheck.Outcome != RepositoryOccurrenceCheckOutcomes.Confirmed
            || PredictionID.Value != Prediction.SHA256
            || OccurrenceCheck.PredictionSHA256 != Prediction.SHA256
            || OccurrenceCheck.Prediction != Prediction)
            throw new InvalidDataException("repository pattern source occurrence does not match confirmed occurrence-check evidence");
    }
}

/// Ordered custody for every receipt used by one composition. The count and
/// length-delimited fields make the digest injective over occurrence order and
/// prevent a single-prediction origin from standing in for a occurrence chain.
public readonly record struct RepositoryPatternOccurrenceSet(
    IReadOnlyList<RepositoryPatternOccurrence> Occurrences,
    string OccurrenceSetSHA256)
{
    public bool IsValid
    {
        get
        {
            try
            {
                ValidateOccurrences();
                return IsSHA256(OccurrenceSetSHA256)
                    && string.Equals(OccurrenceSetSHA256, ComputeSHA256(Occurrences), StringComparison.Ordinal);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }

    public static RepositoryPatternOccurrenceSet Create(IReadOnlyList<RepositoryPatternOccurrence> occurrences)
    {
        ValidateOccurrences(occurrences);
        return new(occurrences, ComputeSHA256(occurrences));
    }

    public void Validate()
    {
        ValidateOccurrences();
        if (!IsSHA256(OccurrenceSetSHA256)
            || !string.Equals(OccurrenceSetSHA256, ComputeSHA256(Occurrences), StringComparison.Ordinal))
            throw new InvalidDataException("repository pattern occurrence-set digest diverges");
    }

    private void ValidateOccurrences() => ValidateOccurrences(Occurrences);

    private static void ValidateOccurrences(IReadOnlyList<RepositoryPatternOccurrence>? occurrences)
    {
        if (occurrences is not { Count: > 0 })
            throw new InvalidDataException("repository pattern occurrence set is empty");
        long previousReceipt = -1;
        foreach (RepositoryPatternOccurrence occurrence in occurrences)
        {
            occurrence.Validate();
            long receipt = occurrence.OccurrenceCheckReceiptEventID.Value;
            if (receipt <= previousReceipt)
                throw new InvalidDataException("repository pattern occurrence receipts are not strictly ordered");
            previousReceipt = receipt;
        }
    }

    private static string ComputeSHA256(IReadOnlyList<RepositoryPatternOccurrence> occurrences)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, occurrences.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (RepositoryPatternOccurrence occurrence in occurrences)
        {
            AppendField(hash, occurrence.PredictionID.Value);
            AppendField(hash, occurrence.SourceEventID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendField(hash, occurrence.OccurrenceCheck.PredecessorEventID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendField(hash, occurrence.OccurrenceCheckReceiptEventID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendField(hash, occurrence.OccurrenceCheck.ReceiptSHA256);
            AppendField(hash, occurrence.EvidenceSHA256);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendFieldLength(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendFieldLength(IncrementalHash hash, int length)
        => hash.AppendData(Encoding.UTF8.GetBytes($"{length}:"));

    private static bool IsSHA256(string value)
        => value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// A typed candidate conclusion that a repository pattern rule may later occurrence.
/// This schema carries all ordered source occurrence; it does not mutate a frontier.
public readonly record struct RepositoryPatternCandidateConclusion(
    RepositoryPatternRuleID RuleID,
    RepositoryPatternOccurrenceSet OccurrenceSet,
    RepositoryCandidateDigest CandidateDigest,
    RepositoryCandidate Candidate)
{
    public void Validate()
    {
        RuleID.Validate();
        OccurrenceSet.Validate();
        if (!CandidateDigest.IsValid || Candidate is null || Candidate.Digest != CandidateDigest)
            throw new InvalidDataException("repository pattern candidate conclusion is malformed");
    }
}

/// Typed origin carried alongside a conclusion. It binds the repository rule and
/// ordered occurrence set before the conclusion enters the candidate frontier.
public readonly record struct RepositoryPatternConclusionOrigin(
    RepositoryPatternConclusionOrigins Kind,
    RepositoryPatternRuleID RuleID,
    RepositoryPatternOccurrenceSet OccurrenceSet,
    string OriginSHA256)
{
    public static RepositoryPatternConclusionOrigin FromSourceOccurrence(
        RepositoryPatternOccurrence occurrence,
        RepositoryPatternRuleID ruleID)
        => FromOccurrenceSet(RepositoryPatternOccurrenceSet.Create([occurrence]), ruleID,
            RepositoryPatternConclusionOrigins.VerifiedPrediction);

    public static RepositoryPatternConclusionOrigin FromOccurrenceSet(
        RepositoryPatternOccurrenceSet occurrenceSet,
        RepositoryPatternRuleID ruleID,
        RepositoryPatternConclusionOrigins kind = RepositoryPatternConclusionOrigins.VerifiedPrediction)
    {
        occurrenceSet.Validate();
        ruleID.Validate();
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("repository pattern conclusion origin kind is malformed");
        string originSHA = ComputeSHA256(kind, ruleID, occurrenceSet);
        return new(kind, ruleID, occurrenceSet, originSHA);
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException("repository pattern conclusion origin is malformed");
        RuleID.Validate();
        OccurrenceSet.Validate();
        if (!IsSHA256(OriginSHA256)
            || !string.Equals(OriginSHA256, ComputeSHA256(Kind, RuleID, OccurrenceSet), StringComparison.Ordinal))
            throw new InvalidDataException("repository pattern conclusion origin digest diverges");
    }

    private static string ComputeSHA256(
        RepositoryPatternConclusionOrigins kind,
        RepositoryPatternRuleID ruleID,
        RepositoryPatternOccurrenceSet occurrenceSet)
    {
        string canonical = $"{(byte)kind}:{ruleID.Value.Length}:{ruleID.Value}:{occurrenceSet.OccurrenceSetSHA256.Length}:{occurrenceSet.OccurrenceSetSHA256}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSHA256(string value)
        => value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// A registered repository navigation law. The rule carries the exact canonical
/// identity and both admission paths it compares; no store reaches into a
/// constants bag to decide which law it is executing.
public readonly record struct RepositoryNavigationRule(
    RepositoryPatternRuleID ID,
    string Canonical,
    RepositoryCandidateSpecies ComposedSpecies,
    string ComposedAdmissionPath,
    string AlternativeAdmissionPath)
{
    public static RepositoryNavigationRule CreateSharedIdentifierSearchTerm()
    {
        const string canonical = "shared-identifier:v1\tconfirmed\tsearch-term";
        // Frozen admission-path tokens; identifier-side names are Pattern and OccurrenceCheck.
        return new(RepositoryPatternRuleID.Create(canonical), canonical, RepositoryCandidateSpecies.SearchTerm,
            "repository-theory/shared-identifier-v1", "repository-verify-claim/shared-identifier");
    }

    public void Validate()
    {
        ID.Validate();
        if (string.IsNullOrWhiteSpace(Canonical)
            || RepositoryPatternRuleID.Create(Canonical) != ID
            || !Enum.IsDefined(ComposedSpecies)
            || string.IsNullOrWhiteSpace(ComposedAdmissionPath)
            || string.IsNullOrWhiteSpace(AlternativeAdmissionPath)
            || ComposedAdmissionPath == AlternativeAdmissionPath)
            throw new InvalidDataException("repository navigation rule is malformed");
    }
}

public readonly record struct RepositoryPatternComposition(
    RepositoryPatternCandidateConclusion Conclusion,
    RepositoryComposedCandidateReceipt Receipt)
{
    public void Validate()
    {
        Conclusion.Validate();
        Receipt.Validate();
        if (Receipt.RuleID != Conclusion.RuleID || Receipt.CandidateDigest != Conclusion.CandidateDigest
            || Receipt.OccurrenceSetSHA256 != Conclusion.OccurrenceSet.OccurrenceSetSHA256
            || Receipt.CandidateCanonical != Conclusion.Candidate.Canonical
            || !Receipt.OccurrenceReceiptEventIDs.SequenceEqual(Conclusion.OccurrenceSet.Occurrences.Select(static occurrence => occurrence.OccurrenceCheckReceiptEventID))
            || Receipt.WorldSHA256 != Conclusion.OccurrenceSet.Occurrences[0].OccurrenceCheck.WorldSHA256
            || Receipt.AccessSHA256 != Conclusion.OccurrenceSet.Occurrences[0].OccurrenceCheck.AccessSHA256)
            throw new InvalidDataException("repository pattern composition custody diverges");
    }
}

public enum RepositoryPatternGrammarAdmissionDecisionKinds : byte
{
    RefusedNonPositiveMarginalMdl,
    Admitted,
}

/// Durable admission economics for one repository composition.  The identity is
/// deliberately wider than the candidate digest: changing rule, occurrence,
/// composition, world/access authority, parent revision, WScale, or the priced
/// baseline creates a different admission and can never silently reprice it.
public sealed record RepositoryPatternGrammarAdmissionReceipt
{
    // Frozen tape source tokens; identifier-side names are PatternEconomicsSource and PatternSource.
    internal const string PatternEconomicsSource = "repository:theory-economics";
    internal const string PatternSource = "repository:theory";
    // Frozen economics frame token; identifier-side name is PatternEconomicsFrame.
    private const string PatternEconomicsFrame = "REPOSITORY-THEORY-ECONOMICS";

    private RepositoryPatternGrammarAdmissionReceipt(
        RepositoryPatternRuleID ruleID,
        string occurrenceSetSHA256,
        RepositoryCandidateDigest candidateDigest,
        string candidateCanonical,
        string compositionReceiptSHA256,
        string worldSHA256,
        string accessSHA256,
        GrammarRevisionID parentRevision,
        int wScale,
        string pricingBasisDigest,
        int baselineRuleCount,
        int baselineCompressedLength,
        int rawSymbolLength,
        int rawWeightLength,
        GrammarAdmissionMdlPrice price,
        RepositoryPatternGrammarAdmissionDecisionKinds decision,
        TapeEventID? economicsEventID,
        string economicsPayloadSHA256,
        JournalRowBinding? economicsJournalBinding,
        TapeEventID? reflectedTapeEventID,
        JournalRowBinding? reflectionJournalBinding,
        GrammarRevisionID? consumedRevision,
        LoopLineageNodeID? lineageNodeID,
        string digest)
    {
        RuleID = ruleID; OccurrenceSetSHA256 = occurrenceSetSHA256; CandidateDigest = candidateDigest;
        CandidateCanonical = candidateCanonical; CompositionReceiptSHA256 = compositionReceiptSHA256;
        WorldSHA256 = worldSHA256; AccessSHA256 = accessSHA256; ParentRevision = parentRevision;
        WScale = wScale; PricingBasisDigest = pricingBasisDigest; BaselineRuleCount = baselineRuleCount;
        BaselineCompressedLength = baselineCompressedLength; RawSymbolLength = rawSymbolLength;
        RawWeightLength = rawWeightLength; Price = price; Decision = decision;
        EconomicsEventID = economicsEventID; EconomicsPayloadSHA256 = economicsPayloadSHA256;
        EconomicsJournalBinding = economicsJournalBinding;
        ReflectedTapeEventID = reflectedTapeEventID; ReflectionJournalBinding = reflectionJournalBinding;
        ConsumedRevision = consumedRevision;
        LineageNodeID = lineageNodeID; Digest = digest;
    }

    public RepositoryPatternRuleID RuleID { get; }
    public string OccurrenceSetSHA256 { get; }
    public RepositoryCandidateDigest CandidateDigest { get; }
    public string CandidateCanonical { get; }
    public string CompositionReceiptSHA256 { get; }
    public string WorldSHA256 { get; }
    public string AccessSHA256 { get; }
    public GrammarRevisionID ParentRevision { get; }
    public int WScale { get; }
    public string PricingBasisDigest { get; }
    public int BaselineRuleCount { get; }
    public int BaselineCompressedLength { get; }
    public int RawSymbolLength { get; }
    public int RawWeightLength { get; }
    public GrammarAdmissionMdlPrice Price { get; }
    public RepositoryPatternGrammarAdmissionDecisionKinds Decision { get; }
    public TapeEventID? EconomicsEventID { get; }
    public string EconomicsPayloadSHA256 { get; }
    public JournalRowBinding? EconomicsJournalBinding { get; }
    public TapeEventID? ReflectedTapeEventID { get; }
    public JournalRowBinding? ReflectionJournalBinding { get; }
    public GrammarRevisionID? ConsumedRevision { get; }
    public LoopLineageNodeID? LineageNodeID { get; }
    public string Digest { get; init; }
    public bool IsRefusal => Decision == RepositoryPatternGrammarAdmissionDecisionKinds.RefusedNonPositiveMarginalMdl;
    public bool MaterializationAdmitted => Decision == RepositoryPatternGrammarAdmissionDecisionKinds.Admitted;
    public string IdentityKey => ComputeIdentityDigest();

    public static RepositoryPatternGrammarAdmissionReceipt Create(
        RepositoryPatternComposition composition,
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        GrammarRevisionID parentRevision,
        int wScale)
    {
        composition.Validate();
        if (parentRevision == GrammarRevisionID.Zero)
            throw new InvalidDataException("repository pattern admission requires a published parent revision");
        Tape.RequireWScale(wScale);
        if (rawWeights.Length != rawTape.Length)
            throw new InvalidDataException("repository pattern admission symbols and weights differ");
        byte[] payload = CandidatePayload(composition.Conclusion.Candidate);
        GrammarAdmissionMdlPrice price = GrammarAdmissionEconomics.PriceMaterialization(
            in baseline, rawTape, rawWeights, payload, wScale);
        string basis = GrammarAdmissionEconomics.ComputeBasisDigest(in baseline, rawTape, rawWeights, payload, wScale);
        var candidate = new RepositoryPatternGrammarAdmissionReceipt(
            composition.Conclusion.RuleID, composition.Conclusion.OccurrenceSet.OccurrenceSetSHA256,
            composition.Conclusion.CandidateDigest, composition.Conclusion.Candidate.Canonical,
            composition.Receipt.ReceiptSHA256, composition.Receipt.WorldSHA256, composition.Receipt.AccessSHA256,
            parentRevision, wScale, basis, baseline.Rules.Length, baseline.Compressed.Length,
            rawTape.Length, rawWeights.Length, price,
            price.IsPositive ? RepositoryPatternGrammarAdmissionDecisionKinds.Admitted : RepositoryPatternGrammarAdmissionDecisionKinds.RefusedNonPositiveMarginalMdl,
            null, "", null, null, null, null, null, "");
        RepositoryPatternGrammarAdmissionReceipt priced = candidate with { Digest = candidate.ComputeDigest() };
        priced.Validate(composition, requireBoundEvidence: false);
        return priced;
    }

    /// The checkpoint reader is the only door to a receipt other than Create: it rebuilds
    /// one whose fields were validated when it was first minted. Construction stays closed
    /// so no call site can assemble a admission receipt that never priced itself.
    internal static RepositoryPatternGrammarAdmissionReceipt RestoreFromCheckpoint(
        RepositoryPatternRuleID ruleID, string occurrenceSetSHA256, RepositoryCandidateDigest candidateDigest,
        string candidateCanonical, string compositionReceiptSHA256, string worldSHA256, string accessSHA256,
        GrammarRevisionID parentRevision, int wScale, string pricingBasisDigest, int baselineRuleCount,
        int baselineCompressedLength, int rawSymbolLength, int rawWeightLength, GrammarAdmissionMdlPrice price,
        RepositoryPatternGrammarAdmissionDecisionKinds decision, TapeEventID? economicsEventID,
        string economicsPayloadSHA256, JournalRowBinding? economicsJournalBinding, TapeEventID? reflectedTapeEventID,
        JournalRowBinding? reflectionJournalBinding, GrammarRevisionID? consumedRevision,
        LoopLineageNodeID? lineageNodeID, string digest)
        => new(ruleID, occurrenceSetSHA256, candidateDigest, candidateCanonical, compositionReceiptSHA256, worldSHA256,
            accessSHA256, parentRevision, wScale, pricingBasisDigest, baselineRuleCount, baselineCompressedLength,
            rawSymbolLength, rawWeightLength, price, decision, economicsEventID, economicsPayloadSHA256,
            economicsJournalBinding, reflectedTapeEventID, reflectionJournalBinding, consumedRevision,
            lineageNodeID, digest);

    private static byte[] CandidatePayload(RepositoryCandidate candidate)
        => Encoding.UTF8.GetBytes(candidate.Canonical + "\n");

    public byte[] CreateCandidatePayload() => Encoding.UTF8.GetBytes(CandidateCanonical + "\n");

    public RepositoryPatternGrammarAdmissionReceipt BindEconomics(TapeEventID eventID, string payloadSHA256, in JournalRowBinding journalBinding)
    {
        if (eventID.Value < 0 || !IsSHA(payloadSHA256)) throw new InvalidDataException("repository pattern economics binding is malformed");
        if (EconomicsEventID is TapeEventID prior && prior != eventID || EconomicsPayloadSHA256.Length != 0 && EconomicsPayloadSHA256 != payloadSHA256)
            throw new InvalidDataException("repository pattern economics binding was rebound");
        ValidateJournalBinding(journalBinding, eventID, PatternEconomicsSource);
        return Rebuild(eventID, payloadSHA256, journalBinding, ReflectedTapeEventID, ReflectionJournalBinding, ConsumedRevision, LineageNodeID);
    }

    public RepositoryPatternGrammarAdmissionReceipt BindReflection(TapeEventID eventID, in JournalRowBinding journalBinding)
    {
        if (!MaterializationAdmitted || eventID.Value < 0 || EconomicsEventID is null)
            throw new InvalidDataException("repository pattern reflection requires an admitted, priced candidate");
        if (ReflectedTapeEventID is TapeEventID prior && prior != eventID)
            throw new InvalidDataException("repository pattern reflection event was rebound");
        ValidateJournalBinding(journalBinding, eventID, PatternSource);
        return Rebuild(EconomicsEventID, EconomicsPayloadSHA256, EconomicsJournalBinding, eventID, journalBinding, ConsumedRevision, LineageNodeID);
    }

    public RepositoryPatternGrammarAdmissionReceipt BindConsumption(GrammarRevisionID revision)
    {
        if (!MaterializationAdmitted || ReflectedTapeEventID is null || revision.Value <= ParentRevision.Value)
            throw new InvalidDataException("repository pattern consumption requires a later folded publication");
        if (ConsumedRevision is GrammarRevisionID prior && prior != revision)
            throw new InvalidDataException("repository pattern consumption was rebound");
        return Rebuild(EconomicsEventID, EconomicsPayloadSHA256, EconomicsJournalBinding, ReflectedTapeEventID, ReflectionJournalBinding, revision, LineageNodeID);
    }

    /// `requireBoundEvidence: false` is the AT-MINT reading. A receipt is priced before either of its
    /// tape bindings exists — the economics packet and, for an admitted admission, the reflection that
    /// materializes the rule are both appended AFTER the receipt that describes them. Demanding either
    /// at mint is a contradiction, and the reflection clause used to demand exactly that: every
    /// admission whose marginal MDL priced POSITIVE threw at creation, so the admitted path had never
    /// once executed and only refusals (non-positive price, no materialization) survived Create. The
    /// two clauses are the same kind of evidence and now share the same gate.
    public void Validate(RepositoryPatternComposition composition, bool requireBoundEvidence = true)
    {
        composition.Validate();
        RuleID.Validate();
        if (!string.Equals(OccurrenceSetSHA256, composition.Conclusion.OccurrenceSet.OccurrenceSetSHA256, StringComparison.Ordinal)
            || !CandidateDigest.IsValid || CandidateDigest != composition.Conclusion.CandidateDigest || CandidateCanonical != composition.Conclusion.Candidate.Canonical
            || CompositionReceiptSHA256 != composition.Receipt.ReceiptSHA256 || WorldSHA256 != composition.Receipt.WorldSHA256
            || AccessSHA256 != composition.Receipt.AccessSHA256 || ParentRevision == GrammarRevisionID.Zero
            || !IsSHA(OccurrenceSetSHA256) || !IsSHA(CompositionReceiptSHA256) || !IsSHA(WorldSHA256) || !IsSHA(AccessSHA256)
            || WScale < 1 || WScale > 128 || (WScale & (WScale - 1)) != 0 || !IsSHA(PricingBasisDigest)
            || BaselineRuleCount < 0 || BaselineCompressedLength < 0 || RawSymbolLength < 0 || RawWeightLength != RawSymbolLength
            || !Enum.IsDefined(Decision) || (MaterializationAdmitted != Price.IsPositive)
            || (EconomicsEventID is TapeEventID economicsID && economicsID.Value < 0)
            || (ReflectedTapeEventID is TapeEventID reflectionID && reflectionID.Value < 0)
            || (ConsumedRevision is { } consumedRevision && (ReflectedTapeEventID is null || consumedRevision.Value <= ParentRevision.Value))
            || (LineageNodeID is not null && ConsumedRevision is null))
            throw new InvalidDataException("repository pattern admission identity is malformed");
        Price.Validate();
        if (requireBoundEvidence && (EconomicsEventID is not TapeEventID || !IsSHA(EconomicsPayloadSHA256)
            || EconomicsJournalBinding is not JournalRowBinding economicsBinding))
            throw new InvalidDataException("repository pattern admission economics evidence is not bound");
        if (EconomicsJournalBinding is JournalRowBinding boundEconomics)
            ValidateJournalBinding(boundEconomics, EconomicsEventID!.Value, PatternEconomicsSource);
        if (requireBoundEvidence && MaterializationAdmitted && (ReflectedTapeEventID is not TapeEventID || ReflectionJournalBinding is not JournalRowBinding reflectionBinding || reflectionBinding.EventID != ReflectedTapeEventID
            || reflectionBinding.Source != PatternSource || !IsSHA(reflectionBinding.SHA256)))
            throw new InvalidDataException("repository pattern reflected grammar evidence is not bound");
        if (MaterializationAdmitted && ReflectionJournalBinding is JournalRowBinding boundReflection)
            ValidateJournalBinding(boundReflection, ReflectedTapeEventID!.Value, PatternSource);
        if (!MaterializationAdmitted && (ReflectedTapeEventID is not null || ConsumedRevision is not null || LineageNodeID is not null))
            throw new InvalidDataException("repository pattern refusal carries materialization state");
        if (LineageNodeID is LoopLineageNodeID lineage && !lineage.IsValid)
            throw new InvalidDataException("repository pattern admission lineage identity is malformed");
        if (!IsSHA(Digest) || Digest != ComputeDigest()) throw new InvalidDataException("repository pattern admission digest diverges");
    }

    internal static byte[] EncodeEconomics(RepositoryPatternGrammarAdmissionReceipt receipt)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        WriteFramed(writer, PatternEconomicsFrame); WriteFramed(writer, receipt.IdentityKey);
        writer.Write((byte)receipt.Decision); writer.Write(receipt.Price.LiteralCostMbits);
        writer.Write(receipt.Price.MaterializedCostMbits); writer.Write(receipt.Price.MarginalSavingsMbits);
        return stream.ToArray();
    }

    internal static void DecodeEconomics(ReadOnlySpan<byte> payload, RepositoryPatternGrammarAdmissionReceipt receipt)
    {
        using MemoryStream stream = new(payload.ToArray());
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        if (ReadFramed(reader) != PatternEconomicsFrame || ReadFramed(reader) != receipt.IdentityKey
            || reader.ReadByte() != (byte)receipt.Decision
            || reader.ReadInt64() != receipt.Price.LiteralCostMbits
            || reader.ReadInt64() != receipt.Price.MaterializedCostMbits
            || reader.ReadInt64() != receipt.Price.MarginalSavingsMbits
            || stream.Position != stream.Length)
            throw new InvalidDataException("repository pattern economics payload diverges");
    }

    internal static bool TryReadEconomicsIdentity(ReadOnlySpan<byte> payload, out string identity)
    {
        identity = "";
        try
        {
            using MemoryStream stream = new(payload.ToArray());
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            if (ReadFramed(reader) != PatternEconomicsFrame) return false;
            identity = ReadFramed(reader);
            _ = reader.ReadByte(); _ = reader.ReadInt64(); _ = reader.ReadInt64(); _ = reader.ReadInt64();
            return stream.Position == stream.Length;
        }
        catch (EndOfStreamException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private static void WriteFramed(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    private static string ReadFramed(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 1_000_000) throw new InvalidDataException("repository pattern economics frame is malformed");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("repository pattern economics frame is truncated");
        return Encoding.UTF8.GetString(bytes);
    }

    private RepositoryPatternGrammarAdmissionReceipt Rebuild(TapeEventID? economics, string economicsPayload, JournalRowBinding? economicsBinding,
        TapeEventID? reflection, JournalRowBinding? reflectionBinding, GrammarRevisionID? consumed, LoopLineageNodeID? lineage)
    {
        RepositoryPatternGrammarAdmissionReceipt value = new(RuleID, OccurrenceSetSHA256, CandidateDigest, CandidateCanonical,
            CompositionReceiptSHA256, WorldSHA256, AccessSHA256, ParentRevision, WScale, PricingBasisDigest,
            BaselineRuleCount, BaselineCompressedLength, RawSymbolLength, RawWeightLength, Price, Decision,
            economics, economicsPayload, economicsBinding, reflection, reflectionBinding, consumed, lineage, "");
        return value with { Digest = value.ComputeDigest() };
    }

    private string ComputeDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        Framed("digest", RuleID.Value, OccurrenceSetSHA256, CandidateDigest.Value.ToString(), CandidateCanonical,
            CompositionReceiptSHA256, WorldSHA256, AccessSHA256, ParentRevision.Value.ToString(), WScale.ToString(),
            PricingBasisDigest, BaselineRuleCount.ToString(), BaselineCompressedLength.ToString(), RawSymbolLength.ToString(), RawWeightLength.ToString(),
            Price.LiteralCostMbits.ToString(), Price.MaterializedCostMbits.ToString(), Price.MarginalSavingsMbits.ToString(), ((byte)Decision).ToString(),
            EconomicsEventID?.Value.ToString() ?? "-1", EconomicsPayloadSHA256, FormatBinding(EconomicsJournalBinding),
            ReflectedTapeEventID?.Value.ToString() ?? "-1", FormatBinding(ReflectionJournalBinding),
            ConsumedRevision?.Value.ToString() ?? "0", LineageNodeID?.Value ?? ""))));

    private static string FormatBinding(JournalRowBinding? binding)
        => binding is JournalRowBinding value
            ? Framed("binding", value.LineIndex.ToString(), value.Step.ToString(), value.EventID.Value.ToString(), value.Source, value.SHA256)
            : "";

    private string ComputeIdentityDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        Framed("identity", RuleID.Value, OccurrenceSetSHA256, CandidateDigest.Value.ToString(), CandidateCanonical,
            CompositionReceiptSHA256, WorldSHA256, AccessSHA256, ParentRevision.Value.ToString(), WScale.ToString(), PricingBasisDigest,
            BaselineRuleCount.ToString(), BaselineCompressedLength.ToString(), RawSymbolLength.ToString(), RawWeightLength.ToString(),
            Price.LiteralCostMbits.ToString(), Price.MaterializedCostMbits.ToString(), Price.MarginalSavingsMbits.ToString()))));

    private static string Framed(string tag, params string[] values)
    {
        StringBuilder result = new();
        AppendFrame(result, tag);
        foreach (string value in values) AppendFrame(result, value);
        return result.ToString();
    }

    private static void AppendFrame(StringBuilder result, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        result.Append(bytes.Length).Append(':').Append(value);
    }

    private static bool IsSHA(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateJournalBinding(JournalRowBinding binding, TapeEventID eventID, string source)
    {
        if (binding.LineIndex < 0 || binding.Step < 0 || binding.EventID.Value < 0
            || binding.EventID != eventID || binding.Source != source || !IsSHA(binding.SHA256))
            throw new InvalidDataException("repository pattern journal binding is malformed");
    }
}

/// A frontier member's typed composition custody. This stays separate from the
/// action adapter so persistence cannot silently turn a pattern result into text.
public readonly record struct RepositoryPatternCandidateOrigin(
    RepositoryPatternRuleID RuleID,
    RepositoryPatternOccurrenceSet OccurrenceSet,
    RepositoryComposedCandidateReceipt Receipt)
{
    public void Validate()
    {
        RuleID.Validate();
        OccurrenceSet.Validate();
        Receipt.Validate();
        if (Receipt.RuleID != RuleID || Receipt.OccurrenceSetSHA256 != OccurrenceSet.OccurrenceSetSHA256)
            throw new InvalidDataException("repository pattern candidate origin diverges");
    }
}

internal readonly record struct RepositoryPatternCandidateKey(RepositoryCandidateDigest Digest, string Canonical)
{
    public static RepositoryPatternCandidateKey Create(RepositoryCandidate candidate)
    {
        if (candidate is null || !candidate.Digest.IsValid || string.IsNullOrEmpty(candidate.Canonical))
            throw new InvalidDataException("repository pattern candidate key is malformed");
        return new(candidate.Digest, candidate.Canonical);
    }

    public void Validate()
    {
        if (!Digest.IsValid || string.IsNullOrEmpty(Canonical))
            throw new InvalidDataException("repository pattern candidate key is malformed");
    }
}

public readonly record struct RepositoryPatternPendingAdmission(
    RepositoryCandidateDigest Digest,
    string Canonical)
{
    public void Validate()
    {
        if (!Digest.IsValid || string.IsNullOrWhiteSpace(Canonical)
            || !RepositoryCandidate.TryParseCanonical(Canonical, out RepositoryCandidate candidate)
            || candidate.Digest != Digest)
            throw new InvalidDataException("repository pattern pending admission is malformed");
    }
}

/// Monotone repository pattern memory. It accepts sealed confirmed receipts and
/// derives only from those receipts; deriving never receives a world snapshot
/// and therefore cannot reopen the repository behind the tape authority.
public sealed class RepositoryPatternStore
{
    internal readonly record struct PendingMutation(bool Added, RepositoryPatternCandidateKey Key);
    // The state payload includes the access-entry authority fields below.  A new
    // tag makes pre-fidelity keyframes fail at the organ boundary instead of
    // being decoded with shifted fields and a misleading receipt error.
    private const uint StateTag = 0x5254485A; // RTHZ
    private readonly RepositoryNavigationRule _rule;
    private readonly Dictionary<RepositoryPatternPredictionID, RepositoryPatternOccurrence> _occurrences = new();
    private readonly Dictionary<RepositoryPatternCandidateKey, RepositoryPatternComposition> _compositions = new();
    private readonly Dictionary<RepositoryPatternCandidateKey, RepositoryPatternGrammarAdmissionReceipt> _admissions = new();
    private readonly HashSet<RepositoryPatternCandidateKey> _pendingAdmissions = new();
    private readonly RepositoryOrderedMerkleMap _pendingAuthority = new();
    private readonly Dictionary<string, int> _economicsPacketCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _reflectionPacketCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _journalMintCounts = new(StringComparer.Ordinal);
    private readonly List<RepositoryPatternGrammarAdmissionReceipt> _admissionMutationLog = new();
    private readonly List<PendingMutation> _pendingMutationLog = new();
    private readonly List<RepositoryPatternOccurrence> _occurrenceMutationLog = new();
    private readonly List<RepositoryPatternComposition> _compositionMutationLog = new();
    private int _checkpointOccurrenceCursor;
    private int _checkpointCompositionCursor;
    private int _checkpointAdmissionCursor;
    private int _checkpointOccurrenceLogCursor;
    private int _checkpointCompositionLogCursor;
    private int _checkpointPendingLogCursor;

    public RepositoryPatternStore(RepositoryNavigationRule rule)
    {
        rule.Validate();
        _rule = rule;
    }

    public IReadOnlyCollection<RepositoryPatternOccurrence> Occurrences
        => _occurrences.Values.OrderBy(static occurrence => occurrence.OccurrenceCheckReceiptEventID.Value).ToArray();
    public IReadOnlyCollection<RepositoryPatternComposition> Compositions
        => _compositions.Values.OrderBy(static composition => composition.Receipt.CompositionEventID.Value).ToArray();
    public IReadOnlyCollection<RepositoryPatternGrammarAdmissionReceipt> Admissions
        => _admissions.Values.OrderBy(static receipt => receipt.CandidateDigest.Value).ToArray();
    public IReadOnlyCollection<RepositoryCandidateDigest> PendingAdmissionDigests
        => _pendingAdmissions.Select(static key => key.Digest).ToArray();
    public IReadOnlyCollection<RepositoryPatternPendingAdmission> PendingAdmissions
        => _pendingAdmissions.OrderBy(static key => key.Digest.Value).ThenBy(static key => key.Canonical, StringComparer.Ordinal)
            .Select(static key => new RepositoryPatternPendingAdmission(key.Digest, key.Canonical)).ToArray();

    public string PendingAuthoritySHA256 => _pendingAuthority.RootHash;
    public string CommittedAuthoritySHA256
        => ComputeCommittedAuthoritySHA256(_rule, Occurrences, Compositions, Admissions, PendingAdmissions);

    public RepositoryPatternStoreSnapshot CaptureSnapshot()
    {
        RepositoryPatternStoreSnapshot snapshot = new(
            _rule, Occurrences.ToArray(), Compositions.ToArray(), Admissions.ToArray(), PendingAdmissions.ToArray(),
            PendingAuthoritySHA256, CommittedAuthoritySHA256);
        snapshot.Validate();
        return snapshot;
    }

    internal IReadOnlyCollection<RepositoryPatternCandidateKey> PendingAdmissionKeys
        => _pendingAdmissions.ToArray();

    internal int OccurrenceCount => _occurrences.Count;
    internal int CompositionCount => _compositions.Count;
    internal int AdmissionCount => _admissions.Count;
    internal int PendingAdmissionCount => _pendingAdmissions.Count;

    internal string PendingAuthorityRoot => _pendingAuthority.RootHash;

    internal (RepositoryPatternOccurrence[] Occurrences, RepositoryPatternComposition[] Compositions,
        RepositoryPatternGrammarAdmissionReceipt[] Admissions, PendingMutation[] PendingMutations) CaptureCheckpointDelta()
    {
        return (_occurrenceMutationLog.GetRange(_checkpointOccurrenceLogCursor, _occurrenceMutationLog.Count - _checkpointOccurrenceLogCursor).ToArray(), _compositionMutationLog.GetRange(_checkpointCompositionLogCursor, _compositionMutationLog.Count - _checkpointCompositionLogCursor).ToArray(),
            _admissionMutationLog.GetRange(_checkpointAdmissionCursor, _admissionMutationLog.Count - _checkpointAdmissionCursor).ToArray(),
            _pendingMutationLog.GetRange(_checkpointPendingLogCursor, _pendingMutationLog.Count - _checkpointPendingLogCursor).ToArray());
    }

    internal void ValidateCheckpointDelta(
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<PendingMutation> pendingMutations)
        => ApplyCheckpointDeltaCore(occurrences, compositions, admissions, pendingMutations, commit: false);

    internal readonly struct PreparedCheckpointDelta
    {
        internal PreparedCheckpointDelta(RepositoryPatternOccurrence[] occurrences,
            RepositoryPatternComposition[] compositions,
            RepositoryPatternGrammarAdmissionReceipt[] admissions,
            PendingMutation[] pendingMutations)
        {
            Occurrences = occurrences; Compositions = compositions; Admissions = admissions; PendingMutations = pendingMutations;
        }

        internal RepositoryPatternOccurrence[] Occurrences { get; }
        internal RepositoryPatternComposition[] Compositions { get; }
        internal RepositoryPatternGrammarAdmissionReceipt[] Admissions { get; }
        internal PendingMutation[] PendingMutations { get; }
    }

    internal PreparedCheckpointDelta PrepareCheckpointDelta(
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<PendingMutation> pendingMutations)
    {
        ApplyCheckpointDeltaCore(occurrences, compositions, admissions, pendingMutations, commit: false);
        return new(occurrences.ToArray(), compositions.ToArray(), admissions.ToArray(), pendingMutations.ToArray());
    }

    internal string ComputePendingAuthorityAfterDelta(
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<PendingMutation> pendingMutations)
    {
        ValidateCheckpointDelta(occurrences, compositions, admissions, pendingMutations);
        List<(bool Set, string Key, string Value)> operations = new(pendingMutations.Count);
        foreach (PendingMutation mutation in pendingMutations)
            operations.Add((mutation.Added, PendingKey(mutation.Key), mutation.Key.Canonical));
        return _pendingAuthority.PreviewRootAfter(operations);
    }

    internal void ApplyCheckpointDelta(
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<PendingMutation> pendingMutations)
        => CommitPreparedCheckpointDelta(PrepareCheckpointDelta(occurrences, compositions, admissions, pendingMutations));

    internal void CommitPreparedCheckpointDelta(in PreparedCheckpointDelta prepared)
    {
        foreach (RepositoryPatternOccurrence occurrence in prepared.Occurrences)
        {
            if (_occurrences.ContainsKey(occurrence.PredictionID)) continue;
            _occurrences.Add(occurrence.PredictionID, occurrence);
            _occurrenceMutationLog.Add(occurrence);
        }
        foreach (RepositoryPatternComposition composition in prepared.Compositions)
        {
            RepositoryPatternCandidateKey key = RepositoryPatternCandidateKey.Create(composition.Conclusion.Candidate);
            if (_compositions.ContainsKey(key)) continue;
            _compositions.Add(key, composition);
            _compositionMutationLog.Add(composition);
        }
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in prepared.Admissions)
        {
            RepositoryPatternCandidateKey key = new(admission.CandidateDigest, admission.CandidateCanonical);
            if (_admissions.ContainsKey(key)) continue;
            _admissions.Add(key, admission);
            _admissionMutationLog.Add(admission);
        }
        foreach (PendingMutation mutation in prepared.PendingMutations)
        {
            if (mutation.Added) _pendingAdmissions.Add(mutation.Key);
            else _pendingAdmissions.Remove(mutation.Key);
            if (mutation.Added) _pendingAuthority.Set(PendingKey(mutation.Key), mutation.Key.Canonical);
            else _pendingAuthority.Remove(PendingKey(mutation.Key));
            _pendingMutationLog.Add(mutation);
        }
    }

    private void ApplyCheckpointDeltaCore(
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<PendingMutation> pendingMutations,
        bool commit)
    {
        Dictionary<RepositoryPatternPredictionID, RepositoryPatternOccurrence> stagedOccurrences = new();
        foreach (RepositoryPatternOccurrence occurrence in occurrences)
        {
            occurrence.Validate();
            if (_occurrences.TryGetValue(occurrence.PredictionID, out RepositoryPatternOccurrence existing))
            {
                if (existing != occurrence) throw new InvalidDataException("repository pattern checkpoint occurrence duplicated or diverged");
                continue;
            }
            if (!stagedOccurrences.TryAdd(occurrence.PredictionID, occurrence))
                throw new InvalidDataException("repository pattern checkpoint occurrence duplicated or diverged");
        }

        Dictionary<RepositoryPatternCandidateKey, RepositoryPatternComposition> stagedCompositions = new();
        foreach (RepositoryPatternComposition composition in compositions)
        {
            composition.Validate();
            foreach (RepositoryPatternOccurrence occurrence in composition.Conclusion.OccurrenceSet.Occurrences)
                if (!_occurrences.ContainsKey(occurrence.PredictionID) && !stagedOccurrences.ContainsKey(occurrence.PredictionID))
                    throw new InvalidDataException("repository pattern checkpoint composition names unknown occurrence");
            RepositoryPatternCandidateKey key = RepositoryPatternCandidateKey.Create(composition.Conclusion.Candidate);
            if (_compositions.ContainsKey(key) || !stagedCompositions.TryAdd(key, composition))
                throw new InvalidDataException("repository pattern checkpoint composition duplicated");
        }

        Dictionary<RepositoryPatternCandidateKey, RepositoryPatternGrammarAdmissionReceipt> stagedAdmissions = new();
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in admissions)
        {
            RepositoryPatternCandidateKey key = new(admission.CandidateDigest, admission.CandidateCanonical);
            if (_admissions.ContainsKey(key) || !stagedAdmissions.TryAdd(key, admission))
                throw new InvalidDataException("repository pattern checkpoint admission duplicated");
            if (!stagedCompositions.TryGetValue(key, out RepositoryPatternComposition composition)
                && !_compositions.TryGetValue(key, out composition))
                throw new InvalidDataException("repository pattern checkpoint admission names unknown composition");
            admission.Validate(composition);
        }

        HashSet<RepositoryPatternCandidateKey> stagedPending = new(_pendingAdmissions);
        foreach (PendingMutation mutation in pendingMutations)
        {
            mutation.Key.Validate();
            bool changed = mutation.Added ? stagedPending.Add(mutation.Key) : stagedPending.Remove(mutation.Key);
            if (!changed || mutation.Added && !(_admissions.ContainsKey(mutation.Key) || stagedAdmissions.ContainsKey(mutation.Key)))
                throw new InvalidDataException("repository pattern checkpoint pending admission mutation diverged");
        }

        if (!commit) return;

        CommitPreparedCheckpointDelta(new(occurrences.ToArray(), compositions.ToArray(), admissions.ToArray(), pendingMutations.ToArray()));
    }

    internal void CommitCheckpointDelta()
    {
        _checkpointOccurrenceCursor = _occurrenceMutationLog.Count;
        _checkpointCompositionCursor = _compositionMutationLog.Count;
        _checkpointAdmissionCursor = _admissionMutationLog.Count;
        _checkpointOccurrenceLogCursor = _occurrenceMutationLog.Count;
        _checkpointCompositionLogCursor = _compositionMutationLog.Count;
        _checkpointPendingLogCursor = _pendingMutationLog.Count;
    }

    internal static void WriteCheckpointDelta(CkptWriter writer,
        (RepositoryPatternOccurrence[] Occurrences, RepositoryPatternComposition[] Compositions,
            RepositoryPatternGrammarAdmissionReceipt[] Admissions, PendingMutation[] PendingMutations) delta)
    {
        writer.U8(2); writer.I32(delta.Occurrences.Length); foreach (var occurrence in delta.Occurrences) WriteOccurrence(writer, occurrence);
        writer.I32(delta.Compositions.Length); foreach (var composition in delta.Compositions) WriteComposition(writer, composition);
        writer.I32(delta.Admissions.Length); foreach (var admission in delta.Admissions) WriteAdmission(writer, admission);
        writer.I32(delta.PendingMutations.Length); foreach (PendingMutation mutation in delta.PendingMutations) { mutation.Key.Validate(); writer.Bool(mutation.Added); writer.U64(mutation.Key.Digest.Value); writer.Str(mutation.Key.Canonical); }
    }

    internal static (RepositoryPatternOccurrence[] Occurrences, RepositoryPatternComposition[] Compositions,
        RepositoryPatternGrammarAdmissionReceipt[] Admissions, PendingMutation[] PendingMutations) ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 2) throw new InvalidDataException("unknown repository pattern checkpoint delta version");
        int occurrences = reader.I32(); if (occurrences < 0 || occurrences > 1_000_000) throw new InvalidDataException("repository pattern occurrence delta is malformed");
        RepositoryPatternOccurrence[] occurrenceRows = new RepositoryPatternOccurrence[occurrences]; for (int i = 0; i < occurrences; i++) occurrenceRows[i] = ReadOccurrence(reader);
        int compositions = reader.I32(); if (compositions < 0 || compositions > 1_000_000) throw new InvalidDataException("repository pattern composition delta is malformed");
        RepositoryPatternComposition[] compositionRows = new RepositoryPatternComposition[compositions]; for (int i = 0; i < compositions; i++) compositionRows[i] = ReadComposition(reader);
        int admissions = reader.I32(); if (admissions < 0 || admissions > 1_000_000) throw new InvalidDataException("repository pattern admission delta is malformed");
        RepositoryPatternGrammarAdmissionReceipt[] admissionRows = new RepositoryPatternGrammarAdmissionReceipt[admissions]; for (int i = 0; i < admissions; i++) admissionRows[i] = ReadAdmission(reader);
        int pending = reader.I32(); if (pending < 0 || pending > 1_000_000) throw new InvalidDataException("repository pattern pending delta is malformed");
        PendingMutation[] pendingRows = new PendingMutation[pending]; for (int i = 0; i < pending; i++) pendingRows[i] = new(reader.Bool(), new(new RepositoryCandidateDigest(reader.U64()), reader.Str()));
        return (occurrenceRows, compositionRows, admissionRows, pendingRows);
    }

    internal void ReplaceState(
        IReadOnlyCollection<RepositoryPatternOccurrence> occurrences,
        IReadOnlyCollection<RepositoryPatternComposition> compositions,
        IReadOnlyCollection<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyCollection<RepositoryPatternCandidateKey> pending)
    {
        _occurrences.Clear(); _compositions.Clear(); _admissions.Clear(); _pendingAdmissions.Clear(); _pendingAuthority.Clear(); _admissionMutationLog.Clear();
        _occurrenceMutationLog.Clear(); _compositionMutationLog.Clear(); _pendingMutationLog.Clear();
        foreach (RepositoryPatternOccurrence occurrence in occurrences)
        {
            occurrence.Validate();
            if (!_occurrences.TryAdd(occurrence.PredictionID, occurrence)) throw new InvalidDataException("repository pattern mutation duplicated occurrence");
        }
        foreach (RepositoryPatternComposition composition in compositions)
        {
            composition.Validate();
            RepositoryPatternCandidateKey key = RepositoryPatternCandidateKey.Create(composition.Conclusion.Candidate);
            if (!_compositions.TryAdd(key, composition)
                || composition.Conclusion.OccurrenceSet.Occurrences.Any(occurrence => !_occurrences.ContainsKey(occurrence.PredictionID)))
                throw new InvalidDataException("repository pattern mutation composition is malformed");
        }
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in admissions)
        {
            RepositoryPatternCandidateKey key = new(admission.CandidateDigest, admission.CandidateCanonical);
            if (!_compositions.TryGetValue(key, out RepositoryPatternComposition composition))
                throw new InvalidDataException("repository pattern mutation admission is malformed");
            admission.Validate(composition, requireBoundEvidence: false);
            if (!_admissions.TryAdd(key, admission))
                throw new InvalidDataException("repository pattern mutation admission is duplicated");
        }
        foreach (RepositoryPatternCandidateKey key in pending)
        {
            if (!_admissions.TryGetValue(key, out RepositoryPatternGrammarAdmissionReceipt admission)
                || !admission.MaterializationAdmitted || admission.ConsumedRevision is not null
                || !_pendingAdmissions.Add(key))
                throw new InvalidDataException("repository pattern mutation pending admission is malformed");
            _pendingAuthority.Set(PendingKey(key), key.Canonical);
        }
        _checkpointOccurrenceCursor = _occurrences.Count;
        _checkpointCompositionCursor = _compositions.Count;
        _checkpointAdmissionCursor = _admissionMutationLog.Count;
        _checkpointOccurrenceLogCursor = _occurrenceMutationLog.Count;
        _checkpointCompositionLogCursor = _compositionMutationLog.Count;
        _checkpointPendingLogCursor = _pendingMutationLog.Count;
    }

    public bool TryAdmitOccurrence(RepositoryPatternOccurrence occurrence)
    {
        occurrence.Validate();
        if (_occurrences.TryGetValue(occurrence.PredictionID, out RepositoryPatternOccurrence existing))
        {
            if (existing != occurrence) throw new InvalidDataException("repository pattern occurrence identity was reused");
            return false;
        }
        _occurrences.Add(occurrence.PredictionID, occurrence);
        _occurrenceMutationLog.Add(occurrence);
        return true;
    }

    public bool TryAdmitOccurrence(RepositoryOccurrenceCheckReceipt occurrenceCheck, TapeEventID sourceEventID,
        TapeEventID occurrenceCheckReceiptEventID, RepositoryAccessJournal sealedAccess)
    {
        occurrenceCheck.Validate();
        // Per-clause: "requires confirmed sealed access evidence" over a fused conjunction cannot
        // tell an unconfirmed prediction from a journal that moved.
        if (occurrenceCheck.Outcome != RepositoryOccurrenceCheckOutcomes.Confirmed)
            throw new InvalidDataException($"repository pattern occurrence requires a CONFIRMED occurrence check, not {occurrenceCheck.Outcome}");
        ArgumentNullException.ThrowIfNull(sealedAccess);
        // The receipt attests to the journal AT ITS OWN COUNT, not to the journal's head — the same
        // one-instant rule the receipt is stamped under. Comparing against the head fails the moment
        // any access lands after the prediction was evaluated, which now happens on every verify, because
        // a verify's own access is recorded like any other.
        string attested = sealedAccess.ComputeAccessSHA256AfterDelta(occurrenceCheck.AccessEntryCount, []);
        if (!string.Equals(attested, occurrenceCheck.AccessSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"repository pattern occurrence access authority diverges: receipt attests {occurrenceCheck.AccessSHA256} at {occurrenceCheck.AccessEntryCount} entries, sealed journal computes {attested}");
        return TryAdmitOccurrence(new RepositoryPatternOccurrence(
            RepositoryPatternPredictionID.Create(occurrenceCheck.Prediction), occurrenceCheck.Prediction, occurrenceCheck,
            sourceEventID, occurrenceCheckReceiptEventID));
    }

    public bool TryComposeSharedIdentifier(int compositionStep, TapeEventID compositionEventID, TapeEventID predecessorEventID,
        out RepositoryPatternComposition composition)
    {
        if (compositionStep < 0) throw new InvalidDataException("repository pattern composition step is malformed");
        foreach (RepositoryPatternOccurrence occurrence in Occurrences)
        {
            if (occurrence.Prediction.Species != RepositoryPredictionSpecies.SharedIdentifier) continue;
            if (compositionStep < occurrence.OccurrenceCheck.Step)
                throw new InvalidDataException("repository pattern composition precedes its occurrence");
            if (_rule.ComposedSpecies != RepositoryCandidateSpecies.SearchTerm) continue;
            RepositoryCandidate candidate = RepositoryCandidate.CreateSearchTerm(new RepositorySearchTerm(occurrence.Prediction.Value));
            RepositoryPatternCandidateKey candidateKey = RepositoryPatternCandidateKey.Create(candidate);
            if (_compositions.ContainsKey(candidateKey)) continue;
            long alternativeCalls = occurrence.OccurrenceCheck.EvaluatorCost;
            if (alternativeCalls <= 0) throw new InvalidDataException("repository pattern alternative evaluator cost is not positive");
            RepositoryPatternOccurrenceSet occurrenceSet = RepositoryPatternOccurrenceSet.Create([occurrence]);
            RepositoryComposedCandidateReceipt receipt = RepositoryComposedCandidateReceipt.Create(
                compositionStep, _rule.ID, occurrenceSet, candidate,
                compositionEventID, _rule.ComposedAdmissionPath,
                _rule.AlternativeAdmissionPath, 0, alternativeCalls, predecessorEventID);
            var conclusion = new RepositoryPatternCandidateConclusion(
                _rule.ID, occurrenceSet, candidate.Digest, candidate);
            composition = new RepositoryPatternComposition(conclusion, receipt);
            composition.Validate();
            _compositions.Add(candidateKey, composition);
            _compositionMutationLog.Add(composition);
            return true;
        }
        composition = default;
        return false;
    }

    /// Price and, only when the exact marginal price is positive, reflect one
    /// composed candidate into the ordinary GrammarInput rail.  Economics are
    /// emitted as custody first, so a refusal is durable without appending input.
    public bool TryAdmitComposedCandidate(
        RepositoryPatternComposition composition,
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        GrammarRevisionID parentRevision,
        int wScale,
        Tape tape,
        Journal journal,
        int step,
        out RepositoryPatternGrammarAdmissionReceipt admission)
    {
        composition.Validate();
        RepositoryPatternCandidateKey candidateKey = RepositoryPatternCandidateKey.Create(composition.Conclusion.Candidate);
        if (_admissions.TryGetValue(candidateKey, out RepositoryPatternGrammarAdmissionReceipt? prior))
        {
            prior.Validate(composition);
            RepositoryPatternGrammarAdmissionReceipt incoming = RepositoryPatternGrammarAdmissionReceipt.Create(
                composition, in baseline, rawTape, rawWeights, parentRevision, wScale);
            if (incoming.IdentityKey != prior.IdentityKey)
                throw new InvalidDataException("repository pattern admission replay pricing diverges");
            admission = prior;
            VerifyEconomicsBinding(prior, tape, journal);
            if (prior.MaterializationAdmitted && prior.ReflectedTapeEventID is not TapeEventID)
                throw new InvalidDataException("repository pattern admission lost its reflected packet identity");
            if (prior.MaterializationAdmitted)
                VerifyReflectionBinding(prior, tape, journal);
            return prior.MaterializationAdmitted;
        }

        RepositoryPatternGrammarAdmissionReceipt priced = RepositoryPatternGrammarAdmissionReceipt.Create(
            composition, in baseline, rawTape, rawWeights, parentRevision, wScale);
        byte[] economicsPayload = RepositoryPatternGrammarAdmissionReceipt.EncodeEconomics(priced);
        TapeEventID economicsEvent = TapePacketCreator.AppendRepositoryPatternAdmissionEconomics(
            tape, journal, step, economicsPayload, out JournalRowBinding economicsBinding);
        priced = priced.BindEconomics(economicsEvent,
            Convert.ToHexStringLower(SHA256.HashData(economicsPayload)), in economicsBinding);
        IncrementJournalMintCount(economicsEvent, RepositoryPatternGrammarAdmissionReceipt.PatternEconomicsSource);
        EnsurePacketIdentityAvailable(priced);
        VerifyEconomicsBinding(priced, tape, journal);
        _economicsPacketCounts[priced.IdentityKey] = _economicsPacketCounts.GetValueOrDefault(priced.IdentityKey) + 1;
        if (priced.MaterializationAdmitted)
        {
            TapeEventID reflectedEvent = TapePacketCreator.AppendRepositoryPatternGrammarInput(
                tape, journal, step, priced.CreateCandidatePayload(), out JournalRowBinding reflectionBinding);
            priced = priced.BindReflection(reflectedEvent, in reflectionBinding);
            IncrementJournalMintCount(reflectedEvent, RepositoryPatternGrammarAdmissionReceipt.PatternSource);
            EnsureReflectionIdentityAvailable(priced);
            VerifyReflectionBinding(priced, tape, journal);
            _reflectionPacketCounts[priced.CandidateCanonical] = _reflectionPacketCounts.GetValueOrDefault(priced.CandidateCanonical) + 1;
        }
        priced.Validate(composition);
        if (!_admissions.TryAdd(candidateKey, priced))
            throw new InvalidDataException("repository pattern admission identity was admitted twice");
        _admissionMutationLog.Add(priced);
        if (priced.MaterializationAdmitted && !_pendingAdmissions.Add(candidateKey))
            throw new InvalidDataException("repository pattern pending admission identity was admitted twice");
        if (priced.MaterializationAdmitted) { _pendingMutationLog.Add(new(true, candidateKey)); _pendingAuthority.Set(PendingKey(candidateKey), candidateKey.Canonical); }
        admission = priced;
        return priced.MaterializationAdmitted;
    }

    public bool SettleInstallRevision(
        in InstallRevision publication,
        IReadOnlyList<TapeEventID> foldedAppends,
        Func<TapeEventID, bool> foldedPredicate,
        Tape tape,
        Journal journal)
    {
        if (foldedAppends is null || foldedPredicate is null || foldedAppends.Count == 0) return false;
        HashSet<TapeEventID> folded = foldedAppends.ToHashSet();
        bool changed = false;
        foreach (RepositoryPatternCandidateKey candidateKey in _pendingAdmissions.ToArray())
        {
            RepositoryPatternGrammarAdmissionReceipt prior = _admissions[candidateKey];
            if (prior.ConsumedRevision is not null) throw new InvalidDataException("repository pattern pending set contains settled admission");
            if (prior.ReflectedTapeEventID is not TapeEventID reflected) throw new InvalidDataException("repository pattern admission has no reflection");
            if (!folded.Contains(reflected)) continue;
            if (publication.ParentRevision != prior.ParentRevision)
                throw new InvalidDataException("repository pattern folded admission belongs to a different publication parent");
            if (publication.Revision.Value <= prior.ParentRevision.Value) continue;
            if (!foldedPredicate(reflected)) throw new InvalidDataException("repository pattern reflection folding predicate contradicts folded appends");
            if (!tape.TryGetEventView(reflected, out TapeEventView view)
                || view.Roles != TapeEventRoles.GrammarInput || view.Provenance != Provenances.Reflected
                || !tape.Resolve(reflected, out byte[] payload)
                || !payload.AsSpan().SequenceEqual(prior.CreateCandidatePayload()))
                throw new InvalidDataException("repository pattern reflected packet contradicts pending admission");
            VerifyEconomicsBinding(prior, tape, journal);
            VerifyReflectionBinding(prior, tape, journal);
            // Membership is established by the exact GrammarInput packet and the
            // Loom parsed-length predicate; no lineage node is fabricated here.
            RepositoryPatternGrammarAdmissionReceipt settled = prior.BindConsumption(publication.Revision);
            _admissions[candidateKey] = settled;
            _admissionMutationLog.Add(settled);
            _pendingAdmissions.Remove(candidateKey);
            _pendingMutationLog.Add(new(false, candidateKey));
            _pendingAuthority.Remove(PendingKey(candidateKey));
            changed = true;
        }
        return changed;
    }

    private void VerifyEconomicsBinding(RepositoryPatternGrammarAdmissionReceipt receipt, Tape tape, Journal journal)
    {
        if (receipt.EconomicsEventID is not TapeEventID eventID || !tape.TryGetEventView(eventID, out TapeEventView view)
            || view.Source != RepositoryPatternGrammarAdmissionReceipt.PatternEconomicsSource
            || view.Provenance != Provenances.Execution
            || view.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
            || !tape.Resolve(eventID, out byte[] payload)
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(payload)), receipt.EconomicsPayloadSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository pattern economics custody is missing or mutated");
        if (receipt.EconomicsJournalBinding is not JournalRowBinding binding || !journal.VerifyBinding(binding))
            throw new InvalidDataException("repository pattern economics journal custody is missing or mutated");
        RepositoryPatternGrammarAdmissionReceipt.DecodeEconomics(payload, receipt);
        if (_economicsPacketCounts.TryGetValue(receipt.IdentityKey, out int count) && count != 1)
            throw new InvalidDataException("repository pattern economics packet identity is duplicated");
        if (GetJournalMintCount(eventID, RepositoryPatternGrammarAdmissionReceipt.PatternEconomicsSource) != 1)
            throw new InvalidDataException("repository pattern economics journal row is duplicated");
    }

    private void VerifyReflectionBinding(RepositoryPatternGrammarAdmissionReceipt receipt, Tape tape, Journal journal)
    {
        if (receipt.ReflectedTapeEventID is not TapeEventID eventID || !tape.TryGetEventView(eventID, out TapeEventView view)
            || view.Source != RepositoryPatternGrammarAdmissionReceipt.PatternSource || view.Roles != TapeEventRoles.GrammarInput
            || view.Provenance != Provenances.Reflected || !tape.Resolve(eventID, out byte[] payload)
            || !payload.AsSpan().SequenceEqual(receipt.CreateCandidatePayload()))
            throw new InvalidDataException("repository pattern reflected grammar packet is missing or mutated");
        if (receipt.ReflectionJournalBinding is not JournalRowBinding binding || !journal.VerifyBinding(binding))
            throw new InvalidDataException("repository pattern reflection journal custody is missing or mutated");
        if (_reflectionPacketCounts.TryGetValue(receipt.CandidateCanonical, out int count) && count != 1)
            throw new InvalidDataException("repository pattern reflection packet identity is duplicated");
        if (GetJournalMintCount(eventID, RepositoryPatternGrammarAdmissionReceipt.PatternSource) != 1)
            throw new InvalidDataException("repository pattern reflection journal row is duplicated");
    }

    private void EnsurePacketIdentityAvailable(RepositoryPatternGrammarAdmissionReceipt receipt)
    {
        if (_economicsPacketCounts.TryGetValue(receipt.IdentityKey, out int count) && count != 0)
            throw new InvalidDataException("repository pattern economics packet identity was admitted twice");
    }

    private void EnsureReflectionIdentityAvailable(RepositoryPatternGrammarAdmissionReceipt receipt)
    {
        if (_reflectionPacketCounts.TryGetValue(receipt.CandidateCanonical, out int count) && count != 0)
            throw new InvalidDataException("repository pattern reflection packet identity was admitted twice");
    }

    public void VerifyAdmissionBindings(Tape tape, Journal journal)
    {
        _economicsPacketCounts.Clear();
        _reflectionPacketCounts.Clear();
        _journalMintCounts.Clear();
        BuildPacketIdentityIndex(tape);
        foreach ((string key, int count) in journal.BuildMintRowIndex()) _journalMintCounts[key] = count;
        foreach (KeyValuePair<RepositoryPatternCandidateKey, RepositoryPatternGrammarAdmissionReceipt> item in _admissions)
        {
            if (!_compositions.TryGetValue(item.Key, out RepositoryPatternComposition composition))
                throw new InvalidDataException("repository pattern admission has no composition");
            item.Value.Validate(composition);
            VerifyEconomicsBinding(item.Value, tape, journal);
            if (item.Value.MaterializationAdmitted) VerifyReflectionBinding(item.Value, tape, journal);
        }
    }

    private void BuildPacketIdentityIndex(Tape tape)
    {
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] payload)) continue;
            if (view.Source == RepositoryPatternGrammarAdmissionReceipt.PatternEconomicsSource && view.Provenance == Provenances.Execution
                && view.Roles == (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                && RepositoryPatternGrammarAdmissionReceipt.TryReadEconomicsIdentity(payload, out string identity))
                _economicsPacketCounts[identity] = _economicsPacketCounts.GetValueOrDefault(identity) + 1;
            else if (view.Source == RepositoryPatternGrammarAdmissionReceipt.PatternSource && view.Provenance == Provenances.Reflected
                && view.Roles == TapeEventRoles.GrammarInput)
            {
                string canonical = Encoding.UTF8.GetString(payload);
                if (canonical.EndsWith('\n')) canonical = canonical[..^1];
                _reflectionPacketCounts[canonical] = _reflectionPacketCounts.GetValueOrDefault(canonical) + 1;
            }
        }
    }

    private void IncrementJournalMintCount(TapeEventID eventID, string source)
    {
        string key = JournalMintKey(eventID, source);
        _journalMintCounts[key] = _journalMintCounts.GetValueOrDefault(key) + 1;
    }

    private int GetJournalMintCount(TapeEventID eventID, string source)
        => _journalMintCounts.GetValueOrDefault(JournalMintKey(eventID, source));

    private static string JournalMintKey(TapeEventID eventID, string source) => $"{eventID.Value}\u0000{source}";

    public void SaveState(CkptWriter writer)
    {
        writer.Section(StateTag);
        writer.Str(_rule.Canonical); writer.U8((byte)_rule.ComposedSpecies);
        writer.Str(_rule.ComposedAdmissionPath); writer.Str(_rule.AlternativeAdmissionPath);
        writer.I32(_occurrences.Count);
        foreach (RepositoryPatternOccurrence occurrence in Occurrences) WriteOccurrence(writer, occurrence);
        writer.I32(_compositions.Count);
        foreach (RepositoryPatternComposition composition in Compositions) WriteComposition(writer, composition);
        writer.I32(_admissions.Count);
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in Admissions)
        {
            RepositoryPatternCandidateKey key = new(admission.CandidateDigest, admission.CandidateCanonical);
            if (!_compositions.TryGetValue(key, out RepositoryPatternComposition composition))
                throw new InvalidDataException("repository pattern admission has no composition");
            admission.Validate(composition);
            WriteAdmission(writer, admission);
        }
        writer.I32(_pendingAdmissions.Count);
        foreach (RepositoryPatternCandidateKey key in _pendingAdmissions.OrderBy(static key => key.Digest.Value).ThenBy(static key => key.Canonical, StringComparer.Ordinal))
        {
            key.Validate();
            writer.U64(key.Digest.Value); writer.Str(key.Canonical);
        }
    }

    public void LoadState(CkptReader reader)
    {
        reader.Expect(StateTag);
        string canonical = reader.Str();
        var species = (RepositoryCandidateSpecies)reader.U8();
        string composedPath = reader.Str(); string alternativePath = reader.Str();
        if (canonical != _rule.Canonical || species != _rule.ComposedSpecies
            || composedPath != _rule.ComposedAdmissionPath || alternativePath != _rule.AlternativeAdmissionPath)
            throw new InvalidDataException("repository pattern rule authority changed");
        _occurrences.Clear(); _compositions.Clear(); _admissions.Clear(); _pendingAdmissions.Clear(); _pendingAuthority.Clear();
        int occurrenceCount = reader.I32();
        if (occurrenceCount < 0 || occurrenceCount > 1_000_000) throw new InvalidDataException("repository pattern occurrence count is malformed");
        for (int i = 0; i < occurrenceCount; i++)
        {
            RepositoryPatternOccurrence occurrence = ReadOccurrence(reader);
            if (!_occurrences.TryAdd(occurrence.PredictionID, occurrence)) throw new InvalidDataException("repository pattern occurrence is duplicated");
        }
        int compositionCount = reader.I32();
        if (compositionCount < 0 || compositionCount > 1_000_000) throw new InvalidDataException("repository pattern composition count is malformed");
        for (int i = 0; i < compositionCount; i++)
        {
            RepositoryPatternComposition composition = ReadComposition(reader);
            composition.Validate();
            if (composition.Conclusion.OccurrenceSet.Occurrences.Any(occurrence => !_occurrences.ContainsKey(occurrence.PredictionID))
                || !_compositions.TryAdd(RepositoryPatternCandidateKey.Create(composition.Conclusion.Candidate), composition))
                throw new InvalidDataException("repository pattern composition is not monotone or is duplicated");
        }
        int admissionCount = reader.I32();
        if (admissionCount < 0 || admissionCount > 1_000_000) throw new InvalidDataException("repository pattern admission count is malformed");
        for (int i = 0; i < admissionCount; i++)
        {
            RepositoryPatternGrammarAdmissionReceipt admission = ReadAdmission(reader);
            RepositoryPatternCandidateKey key = new(admission.CandidateDigest, admission.CandidateCanonical);
            if (!_compositions.TryGetValue(key, out RepositoryPatternComposition composition)
                || !_admissions.TryAdd(key, admission))
                throw new InvalidDataException("repository pattern admission is not monotone or is duplicated");
            admission.Validate(composition);
        }
        int pendingCount = reader.I32();
        if (pendingCount < 0 || pendingCount > _admissions.Count) throw new InvalidDataException("repository pattern pending admission count is malformed");
        for (int i = 0; i < pendingCount; i++)
        {
            RepositoryPatternCandidateKey key = new(new RepositoryCandidateDigest(reader.U64()), reader.Str());
            key.Validate();
            if (!_admissions.TryGetValue(key, out RepositoryPatternGrammarAdmissionReceipt admission)
                || !admission.MaterializationAdmitted || admission.ConsumedRevision is not null || !_pendingAdmissions.Add(key))
                throw new InvalidDataException("repository pattern pending admission index diverges");
            _pendingAuthority.Set(PendingKey(key), key.Canonical);
        }
        if (_admissions.Values.Count(static admission => admission.MaterializationAdmitted && admission.ConsumedRevision is null) != _pendingAdmissions.Count)
            throw new InvalidDataException("repository pattern pending admission index is incomplete");
        _admissionMutationLog.AddRange(_admissions.Values);
        _occurrenceMutationLog.AddRange(_occurrences.Values);
        _compositionMutationLog.AddRange(_compositions.Values);
        foreach (RepositoryPatternCandidateKey key in _pendingAdmissions) _pendingMutationLog.Add(new(true, key));
        CommitCheckpointDelta();
    }

    private static string PendingKey(RepositoryPatternCandidateKey key)
        => key.Digest.Value.ToString("X16") + "\u0000" + key.Canonical;

    internal static string ComputeCommittedAuthoritySHA256(
        RepositoryNavigationRule rule,
        IReadOnlyCollection<RepositoryPatternOccurrence> occurrences,
        IReadOnlyCollection<RepositoryPatternComposition> compositions,
        IReadOnlyCollection<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyCollection<RepositoryPatternPendingAdmission> pending)
    {
        rule.Validate();
        StringBuilder canonical = new();
        canonical.Append("rule\t").Append(rule.ID.Value).Append('\n');
        foreach (RepositoryPatternOccurrence occurrence in occurrences.OrderBy(static value => value.OccurrenceCheckReceiptEventID.Value))
        {
            occurrence.Validate();
            // Frozen digest row kind; identifier-side name is Occurrence.
            canonical.Append("support\t").Append(occurrence.PredictionID.Value).Append('\t')
                .Append(occurrence.SourceEventID.Value).Append('\t').Append(occurrence.OccurrenceCheckReceiptEventID.Value).Append('\t')
                .Append(occurrence.OccurrenceCheck.ReceiptSHA256).Append('\t').Append(occurrence.EvidenceSHA256).Append('\n');
        }
        foreach (RepositoryPatternComposition composition in compositions.OrderBy(static value => value.Receipt.CompositionEventID.Value))
        {
            composition.Validate();
            // Frozen digest row kind; identifier-side name is Composition.
            canonical.Append("derivation\t").Append(composition.Conclusion.CandidateDigest.Value).Append('\t')
                .Append(composition.Conclusion.Candidate.Canonical).Append('\t').Append(composition.Receipt.ReceiptSHA256).Append('\n');
        }
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in admissions.OrderBy(static value => value.CandidateDigest.Value))
        {
            // Frozen digest row kind; identifier-side name is Admission.
            canonical.Append("promotion\t").Append(admission.CandidateDigest.Value).Append('\t')
                .Append(admission.CandidateCanonical).Append('\t').Append(admission.IdentityKey).Append('\t')
                .Append(admission.Digest).Append('\n');
        }
        foreach (RepositoryPatternPendingAdmission value in pending.OrderBy(static value => value.Digest.Value).ThenBy(static value => value.Canonical, StringComparer.Ordinal))
        {
            value.Validate();
            canonical.Append("pending\t").Append(value.Digest.Value).Append('\t').Append(value.Canonical).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    internal static void WriteOrigin(CkptWriter writer, RepositoryPatternCandidateOrigin origin)
    {
        origin.Validate();
        writer.Str(origin.RuleID.Value); WriteOccurrenceSet(writer, origin.OccurrenceSet); WriteReceipt(writer, origin.Receipt);
    }

    internal static RepositoryPatternCandidateOrigin ReadOrigin(CkptReader reader)
    {
        var origin = new RepositoryPatternCandidateOrigin(new RepositoryPatternRuleID(reader.Str()), ReadOccurrenceSet(reader), ReadReceipt(reader));
        origin.Validate();
        return origin;
    }

    private static void WriteComposition(CkptWriter writer, RepositoryPatternComposition composition)
    {
        writer.Str(composition.Conclusion.RuleID.Value); WriteOccurrenceSet(writer, composition.Conclusion.OccurrenceSet);
        WriteCandidate(writer, composition.Conclusion.Candidate); WriteReceipt(writer, composition.Receipt);
    }

    private static RepositoryPatternComposition ReadComposition(CkptReader reader)
    {
        var rule = new RepositoryPatternRuleID(reader.Str());
        RepositoryPatternOccurrenceSet occurrences = ReadOccurrenceSet(reader);
        RepositoryCandidate candidate = ReadCandidate(reader);
        var conclusion = new RepositoryPatternCandidateConclusion(rule, occurrences, candidate.Digest, candidate);
        return new RepositoryPatternComposition(conclusion, ReadReceipt(reader));
    }

    private static void WriteAdmission(CkptWriter writer, RepositoryPatternGrammarAdmissionReceipt admission)
    {
        writer.Str(admission.RuleID.Value); writer.Str(admission.OccurrenceSetSHA256); writer.U64(admission.CandidateDigest.Value);
        writer.Str(admission.CandidateCanonical); writer.Str(admission.CompositionReceiptSHA256);
        writer.Str(admission.WorldSHA256); writer.Str(admission.AccessSHA256); writer.U64(admission.ParentRevision.Value);
        writer.I32(admission.WScale); writer.Str(admission.PricingBasisDigest); writer.I32(admission.BaselineRuleCount);
        writer.I32(admission.BaselineCompressedLength); writer.I32(admission.RawSymbolLength); writer.I32(admission.RawWeightLength);
        writer.I64(admission.Price.LiteralCostMbits); writer.I64(admission.Price.MaterializedCostMbits); writer.I64(admission.Price.MarginalSavingsMbits);
        writer.U8((byte)admission.Decision); writer.Bool(admission.EconomicsEventID is not null);
        if (admission.EconomicsEventID is TapeEventID economics) writer.I64(economics.Value);
        writer.Str(admission.EconomicsPayloadSHA256); writer.Bool(admission.EconomicsJournalBinding is not null);
        if (admission.EconomicsJournalBinding is JournalRowBinding economicsBinding) WriteJournalBinding(writer, economicsBinding);
        writer.Bool(admission.ReflectedTapeEventID is not null);
        if (admission.ReflectedTapeEventID is TapeEventID reflected) writer.I64(reflected.Value);
        writer.Bool(admission.ReflectionJournalBinding is not null);
        if (admission.ReflectionJournalBinding is JournalRowBinding reflectionBinding) WriteJournalBinding(writer, reflectionBinding);
        writer.Bool(admission.ConsumedRevision is not null);
        if (admission.ConsumedRevision is GrammarRevisionID consumed) writer.U64(consumed.Value);
        writer.Bool(admission.LineageNodeID is not null);
        if (admission.LineageNodeID is LoopLineageNodeID lineage) writer.Str(lineage.Value);
        writer.Str(admission.Digest);
    }

    private static RepositoryPatternGrammarAdmissionReceipt ReadAdmission(CkptReader reader)
    {
        var rule = new RepositoryPatternRuleID(reader.Str()); string occurrence = reader.Str(); var candidate = new RepositoryCandidateDigest(reader.U64());
        string canonical = reader.Str(); string composition = reader.Str(); string world = reader.Str(); string access = reader.Str();
        var parent = new GrammarRevisionID(reader.U64()); int wScale = reader.I32(); string basis = reader.Str(); int ruleCount = reader.I32();
        int compressed = reader.I32(); int raw = reader.I32(); int weights = reader.I32();
        var price = new GrammarAdmissionMdlPrice(reader.I64(), reader.I64(), reader.I64());
        var decision = (RepositoryPatternGrammarAdmissionDecisionKinds)reader.U8();
        TapeEventID? economics = reader.Bool() ? new TapeEventID(reader.I64()) : null;
        string economicsPayload = reader.Str(); JournalRowBinding? economicsBinding = reader.Bool() ? ReadJournalBinding(reader) : null;
        TapeEventID? reflected = reader.Bool() ? new TapeEventID(reader.I64()) : null;
        JournalRowBinding? reflectionBinding = reader.Bool() ? ReadJournalBinding(reader) : null;
        GrammarRevisionID? consumed = reader.Bool() ? new GrammarRevisionID(reader.U64()) : null;
        LoopLineageNodeID? lineage = reader.Bool() ? new LoopLineageNodeID(reader.Str()) : null;
        string digest = reader.Str();
        return RepositoryPatternGrammarAdmissionReceipt.RestoreFromCheckpoint(rule, occurrence, candidate, canonical, composition, world, access,
            parent, wScale, basis, ruleCount, compressed, raw, weights, price, decision, economics, economicsPayload,
            economicsBinding, reflected, reflectionBinding, consumed, lineage, digest);
    }

    private static void WriteJournalBinding(CkptWriter writer, JournalRowBinding binding)
    {
        writer.I32(binding.LineIndex); writer.I32(binding.Step); writer.I64(binding.EventID.Value);
        writer.Str(binding.Source); writer.Str(binding.SHA256);
    }

    private static JournalRowBinding ReadJournalBinding(CkptReader reader)
    {
        JournalRowBinding binding = new(reader.I32(), reader.I32(), new TapeEventID(reader.I64()), reader.Str(), reader.Str());
        if (binding.LineIndex < 0 || binding.Step < 0 || binding.EventID.Value < 0
            || !RepositoryLineageReceiptCodec.IsSHA(binding.SHA256))
            throw new InvalidDataException("repository pattern journal binding is malformed");
        return binding;
    }

    private static void WriteOccurrenceSet(CkptWriter writer, RepositoryPatternOccurrenceSet occurrenceSet)
    {
        occurrenceSet.Validate(); writer.I32(occurrenceSet.Occurrences.Count);
        foreach (RepositoryPatternOccurrence occurrence in occurrenceSet.Occurrences) WriteOccurrence(writer, occurrence);
        writer.Str(occurrenceSet.OccurrenceSetSHA256);
    }

    private static RepositoryPatternOccurrenceSet ReadOccurrenceSet(CkptReader reader)
    {
        int count = reader.I32();
        if (count <= 0 || count > 1_000_000) throw new InvalidDataException("repository pattern occurrence-set count is malformed");
        var occurrences = new RepositoryPatternOccurrence[count];
        for (int i = 0; i < count; i++) occurrences[i] = ReadOccurrence(reader);
        var result = new RepositoryPatternOccurrenceSet(occurrences, reader.Str()); result.Validate(); return result;
    }

    private static void WriteOccurrence(CkptWriter writer, RepositoryPatternOccurrence occurrence)
    {
        occurrence.Validate(); writer.Str(occurrence.PredictionID.Value); WritePrediction(writer, occurrence.Prediction); WriteOccurrenceCheck(writer, occurrence.OccurrenceCheck);
        writer.I64(occurrence.SourceEventID.Value); writer.I64(occurrence.OccurrenceCheckReceiptEventID.Value);
    }

    private static RepositoryPatternOccurrence ReadOccurrence(CkptReader reader)
    {
        var predictionID = new RepositoryPatternPredictionID(reader.Str()); RepositoryPrediction prediction = ReadPrediction(reader);
        RepositoryOccurrenceCheckReceipt occurrenceCheck = ReadOccurrenceCheck(reader);
        var occurrence = new RepositoryPatternOccurrence(predictionID, prediction, occurrenceCheck, new TapeEventID(reader.I64()), new TapeEventID(reader.I64()));
        occurrence.Validate(); return occurrence;
    }

    private static void WriteReceipt(CkptWriter writer, RepositoryComposedCandidateReceipt receipt)
    {
        receipt.Validate(); writer.I32(receipt.Step); writer.Str(receipt.RuleID.Value); writer.Str(receipt.OccurrenceSetSHA256);
        writer.I32(receipt.OccurrenceReceiptEventIDs.Length); foreach (TapeEventID id in receipt.OccurrenceReceiptEventIDs) writer.I64(id.Value);
        writer.Str(receipt.CandidateCanonical); writer.U8((byte)receipt.CandidateSpecies); writer.U64(receipt.CandidateDigest.Value);
        writer.Str(receipt.PredictionSHA256); writer.Str(receipt.SourceEvidenceSHA256); writer.Str(receipt.ComposedAdmissionPath); writer.Str(receipt.AlternativeAdmissionPath);
        writer.I64(receipt.ComposedEvaluatorCalls); writer.I64(receipt.AlternativeEvaluatorCalls); writer.I64(receipt.EvaluatorDelta);
        writer.I64(receipt.CompositionEventID.Value); writer.Str(receipt.WorldSHA256); writer.Str(receipt.AccessSHA256); writer.I64(receipt.PredecessorEventID.Value); writer.Str(receipt.ReceiptSHA256);
    }

    private static RepositoryComposedCandidateReceipt ReadReceipt(CkptReader reader)
    {
        int step = reader.I32(); var rule = new RepositoryPatternRuleID(reader.Str()); string occurrence = reader.Str(); int n = reader.I32();
        if (n <= 0 || n > 1_000_000) throw new InvalidDataException("repository pattern receipt occurrence count is malformed");
        TapeEventID[] ids = new TapeEventID[n]; for (int i = 0; i < n; i++) ids[i] = new TapeEventID(reader.I64());
        string canonical = reader.Str(); var species = (RepositoryCandidateSpecies)reader.U8(); var digest = new RepositoryCandidateDigest(reader.U64());
        var receipt = new RepositoryComposedCandidateReceipt(step, rule, occurrence, ids, canonical, species, digest, reader.Str(), reader.Str(), reader.Str(), reader.Str(), reader.I64(), reader.I64(), reader.I64(), new TapeEventID(reader.I64()), reader.Str(), reader.Str(), new TapeEventID(reader.I64()), reader.Str());
        receipt.Validate(); return receipt;
    }

    private static void WritePrediction(CkptWriter writer, RepositoryPrediction prediction)
    { prediction.Validate(); writer.U8((byte)prediction.Species); writer.Str(prediction.Path); writer.I32(prediction.Line); writer.Str(prediction.Value); writer.Str(prediction.OtherPath); }
    private static RepositoryPrediction ReadPrediction(CkptReader reader) => new((RepositoryPredictionSpecies)reader.U8(), reader.Str(), reader.I32(), reader.Str(), reader.Str());
    private static void WriteOccurrenceCheck(CkptWriter writer, RepositoryOccurrenceCheckReceipt receipt)
    {
        writer.I32(receipt.Step); WritePrediction(writer, receipt.Prediction); writer.U8((byte)receipt.Outcome);
        writer.Str(receipt.WorldSHA256); writer.Str(receipt.AccessSHA256);
        writer.I64(receipt.AccessSequence); writer.Str(receipt.AccessEntrySHA256); writer.I32(receipt.AccessEntryCount);
        writer.Str(receipt.PredictionSHA256); writer.Str(receipt.EvidenceSHA256);
        writer.I64(receipt.EvaluatorCost); writer.I64(receipt.AccessCost); writer.I64(receipt.PredecessorEventID.Value);
        writer.Str(receipt.CallSHA256); writer.Str(receipt.ReceiptSHA256);
    }

    private static RepositoryOccurrenceCheckReceipt ReadOccurrenceCheck(CkptReader reader)
    {
        int step = reader.I32(); RepositoryPrediction prediction = ReadPrediction(reader);
        var outcome = (RepositoryOccurrenceCheckOutcomes)reader.U8(); string world = reader.Str(); string access = reader.Str();
        long accessSequence = reader.I64(); string accessEntrySHA = reader.Str(); int accessEntryCount = reader.I32();
        RepositoryOccurrenceCheckReceipt receipt = new(step, prediction, outcome, world, access, reader.Str(), reader.Str(),
            reader.I64(), reader.I64(), new TapeEventID(reader.I64()), reader.Str(), reader.Str())
        {
            AccessSequence = accessSequence,
            AccessEntrySHA256 = accessEntrySHA,
            AccessEntryCount = accessEntryCount,
        };
        receipt.Validate();
        return receipt;
    }
    private static void WriteCandidate(CkptWriter writer, RepositoryCandidate candidate)
    {
        writer.U8((byte)candidate.Species); writer.Str(candidate.Canonical);
    }
    private static RepositoryCandidate ReadCandidate(CkptReader reader)
    {
        var species = (RepositoryCandidateSpecies)reader.U8(); string canonical = reader.Str();
        RepositoryCandidate candidate = species switch
        {
            RepositoryCandidateSpecies.SearchTerm when canonical.StartsWith("search-term\t", StringComparison.Ordinal) => RepositoryCandidate.CreateSearchTerm(new RepositorySearchTerm(canonical[12..])),
            RepositoryCandidateSpecies.ListPrefix when canonical.StartsWith("list-prefix\t", StringComparison.Ordinal) => RepositoryCandidate.CreateListPrefix(new RepositoryListPrefix(canonical[12..])),
            RepositoryCandidateSpecies.OpenPath when canonical.StartsWith("open-path\t", StringComparison.Ordinal) => RepositoryCandidate.CreateOpenPath(new RepositoryOpenPath(canonical[10..])),
            RepositoryCandidateSpecies.ReadLocus when canonical.StartsWith("read-locus\t", StringComparison.Ordinal) => RepositoryCandidate.CreateReadLocus(new RepositoryReadLocus(ParseLocus(canonical[11..]))),
            // Frozen canonical prefix; identifier-side name is VerifyPrediction.
            RepositoryCandidateSpecies.VerifyPrediction when canonical.StartsWith("verify-claim\t", StringComparison.Ordinal)
                && RepositoryPrediction.TryParse(canonical[13..], out RepositoryPrediction prediction)
                => RepositoryCandidate.CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(prediction)),
            RepositoryCandidateSpecies.AnswerPath when canonical.StartsWith("answer-path\t", StringComparison.Ordinal) => RepositoryCandidate.CreateAnswerPath(new RepositoryAnswerPath(canonical[12..])),
            _ => throw new InvalidDataException("repository pattern candidate is not reconstructible"),
        };
        if (candidate.Canonical != canonical) throw new InvalidDataException("repository pattern candidate canonical form diverges");
        return candidate;
    }

    private static Tool.RepositoryLocus ParseLocus(string value)
    {
        int colon = value.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(value[(colon + 1)..], out int line)) throw new InvalidDataException("repository pattern locus is malformed");
        return new Tool.RepositoryLocus(value[..colon], line);
    }
}

/// Immutable capture of the live pattern store. It carries both the pending
/// admission index and the full committed authority so R4 consumers can bind
/// to exactly what the runtime admitted without reopening checkpoint state.
public sealed class RepositoryPatternStoreSnapshot
{
    public RepositoryPatternStoreSnapshot(
        RepositoryNavigationRule rule,
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyCollection<RepositoryPatternPendingAdmission> pendingAdmissions,
        string pendingAuthoritySHA256,
        string committedAuthoritySHA256)
    {
        Rule = rule;
        Occurrences = Array.AsReadOnly((occurrences ?? throw new ArgumentNullException(nameof(occurrences)))
            .Select(CloneOccurrence).ToArray());
        Compositions = Array.AsReadOnly((compositions ?? throw new ArgumentNullException(nameof(compositions)))
            .Select(CloneComposition).ToArray());
        Admissions = Array.AsReadOnly((admissions ?? throw new ArgumentNullException(nameof(admissions))).ToArray());
        PendingAdmissions = Array.AsReadOnly((pendingAdmissions ?? throw new ArgumentNullException(nameof(pendingAdmissions))).ToArray());
        PendingAuthoritySHA256 = pendingAuthoritySHA256;
        CommittedAuthoritySHA256 = committedAuthoritySHA256;
    }

    public RepositoryNavigationRule Rule { get; }
    public IReadOnlyList<RepositoryPatternOccurrence> Occurrences { get; }
    public IReadOnlyList<RepositoryPatternComposition> Compositions { get; }
    public IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> Admissions { get; }
    public IReadOnlyList<RepositoryPatternPendingAdmission> PendingAdmissions { get; }
    public IReadOnlyList<RepositoryCandidateDigest> PendingAdmissionDigests
        => PendingAdmissions.Select(static pending => pending.Digest).ToArray();
    public string PendingAuthoritySHA256 { get; }
    public string CommittedAuthoritySHA256 { get; }
    public string PatternSHA256 => CommittedAuthoritySHA256;

    public void Validate()
    {
        Rule.Validate();
        if (!IsSHA(PendingAuthoritySHA256) || !IsSHA(CommittedAuthoritySHA256))
            throw new InvalidDataException("repository pattern snapshot authority is malformed");

        HashSet<RepositoryPatternPredictionID> occurrenceIDs = new();
        foreach (RepositoryPatternOccurrence occurrence in Occurrences)
        {
            occurrence.Validate();
            if (!occurrenceIDs.Add(occurrence.PredictionID))
                throw new InvalidDataException("repository pattern snapshot occurrence is duplicated");
        }

        HashSet<RepositoryPatternCandidateDigestKey> compositionKeys = new();
        foreach (RepositoryPatternComposition composition in Compositions)
        {
            composition.Validate();
            if (composition.Conclusion.OccurrenceSet.Occurrences.Any(occurrence => !occurrenceIDs.Contains(occurrence.PredictionID))
                || !compositionKeys.Add(new(composition.Conclusion.CandidateDigest, composition.Conclusion.Candidate.Canonical)))
                throw new InvalidDataException("repository pattern snapshot composition is malformed");
        }

        HashSet<RepositoryPatternCandidateDigestKey> admissionKeys = new();
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in Admissions)
        {
            RepositoryPatternCandidateDigestKey key = new(admission.CandidateDigest, admission.CandidateCanonical);
            if (!admissionKeys.Add(key)
                || !Compositions.Any(composition => composition.Conclusion.CandidateDigest == key.Digest
                    && composition.Conclusion.Candidate.Canonical == key.Canonical))
                throw new InvalidDataException("repository pattern snapshot admission is malformed");
            RepositoryPatternComposition composition = Compositions.First(value => value.Conclusion.CandidateDigest == key.Digest
                && value.Conclusion.Candidate.Canonical == key.Canonical);
            admission.Validate(composition);
        }

        RepositoryOrderedMerkleMap pendingAuthority = new();
        HashSet<RepositoryPatternCandidateDigestKey> pendingKeys = new();
        foreach (RepositoryPatternPendingAdmission pending in PendingAdmissions)
        {
            pending.Validate();
            RepositoryPatternCandidateDigestKey key = new(pending.Digest, pending.Canonical);
            if (!pendingKeys.Add(key)) throw new InvalidDataException("repository pattern snapshot pending admission is duplicated");
            RepositoryPatternGrammarAdmissionReceipt admission = Admissions.FirstOrDefault(value => value.CandidateDigest == pending.Digest
                && value.CandidateCanonical == pending.Canonical)
                ?? throw new InvalidDataException("repository pattern snapshot pending admission has no receipt");
            if (!admission.MaterializationAdmitted || admission.ConsumedRevision is not null)
                throw new InvalidDataException("repository pattern snapshot pending admission is settled");
            pendingAuthority.Set(PendingKey(pending), pending.Canonical);
        }

        HashSet<RepositoryPatternCandidateDigestKey> expectedPendingKeys = Admissions
            .Where(static admission => admission.MaterializationAdmitted && admission.ConsumedRevision is null)
            .Select(static admission => new RepositoryPatternCandidateDigestKey(admission.CandidateDigest, admission.CandidateCanonical))
            .ToHashSet();
        if (!expectedPendingKeys.SetEquals(pendingKeys))
            throw new InvalidDataException("repository pattern snapshot pending admission index is incomplete");

        if (PendingAuthoritySHA256 != pendingAuthority.RootHash
            || CommittedAuthoritySHA256 != RepositoryPatternStore.ComputeCommittedAuthoritySHA256(
                Rule, Occurrences, Compositions, Admissions, PendingAdmissions))
            throw new InvalidDataException("repository pattern snapshot authority diverges");
    }

    private static RepositoryPatternOccurrence CloneOccurrence(RepositoryPatternOccurrence occurrence)
        => occurrence;

    private static RepositoryPatternComposition CloneComposition(RepositoryPatternComposition composition)
    {
        RepositoryPatternOccurrenceSet occurrenceSet = RepositoryPatternOccurrenceSet.Create(composition.Conclusion.OccurrenceSet.Occurrences.ToArray());
        RepositoryPatternCandidateConclusion conclusion = composition.Conclusion with { OccurrenceSet = occurrenceSet };
        RepositoryComposedCandidateReceipt receipt = composition.Receipt with
        {
            OccurrenceReceiptEventIDs = composition.Receipt.OccurrenceReceiptEventIDs.ToArray(),
        };
        return new RepositoryPatternComposition(conclusion, receipt);
    }

    private static string PendingKey(RepositoryPatternPendingAdmission pending)
        => pending.Digest.Value.ToString("X16") + "\u0000" + pending.Canonical;

    private readonly record struct RepositoryPatternCandidateDigestKey(RepositoryCandidateDigest Digest, string Canonical);

    private static bool IsSHA(string value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
