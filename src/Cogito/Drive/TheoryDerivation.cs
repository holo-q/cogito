namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;

/// Custody handed to a pattern-forming host after a source event has been admitted.
/// The protocol is neutral so numeric and repository hosts share one causal rail.
internal readonly record struct PatternSourceOpportunity(
    TapeEventID SourceEventID,
    string SourceSHA256,
    IReadOnlyList<TapeEventID> OpportunityEventIDs,
    int WorldContacts)
{
    public bool IsValid
        => SourceEventID.Value >= 0
            && IsSHA256(SourceSHA256)
            && OpportunityEventIDs is { Count: > 0 }
            && WorldContacts >= 0
            && IsStrictlyIncreasing(OpportunityEventIDs);

    public void Validate()
    {
        if (!IsValid) throw new InvalidDataException("pattern source opportunity is malformed");
    }

    private static bool IsStrictlyIncreasing(IReadOnlyList<TapeEventID> eventIDs)
    {
        long previous = -1;
        foreach (TapeEventID eventID in eventIDs)
        {
            if (eventID.Value <= previous) return false;
            previous = eventID.Value;
        }
        return true;
    }

    private static bool IsSHA256(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// The structural class of the positive-cost admission path. It names how the
/// identical conclusion would be admitted, without naming either host's domain.
internal enum PatternAlternativeAdmissionSpecies : byte
{
    RegisteredEvaluator = 1,
    StructuralReplay = 2,
    StructuralRecompute = 3,
}

/// Durable cross-host origin for a composed conclusion. This is the neutral join
/// point later consumed by a host's candidate frontier; it is not itself a prediction,
/// candidate, composition engine, or verdict.
internal readonly record struct PatternComposedOrigin(
    TapeEventID SourceEventID,
    TapeEventID CompositionEventID,
    GrammarRevisionID CompositionRevision,
    string SourceSHA256,
    string CompositionSHA256,
    PatternAlternativeAdmissionSpecies AlternativeAdmission,
    long ComposedEvaluatorCalls,
    long AlternativeEvaluatorCalls,
    string OriginSHA256)
{
    public bool IsDisplacedEvaluation
        => ComposedEvaluatorCalls == 0 && AlternativeEvaluatorCalls > 0;

    public bool IsValid
        => SourceEventID.Value >= 0
            && CompositionEventID.Value > SourceEventID.Value
            && CompositionRevision != GrammarRevisionID.Zero
            && IsSHA256(SourceSHA256)
            && IsSHA256(CompositionSHA256)
            && Enum.IsDefined(AlternativeAdmission)
            && ComposedEvaluatorCalls == 0
            && AlternativeEvaluatorCalls > 0
            && IsSHA256(OriginSHA256)
            && OriginSHA256 == ComputeSHA256(
                SourceEventID, CompositionEventID, CompositionRevision,
                SourceSHA256, CompositionSHA256, AlternativeAdmission,
                ComposedEvaluatorCalls, AlternativeEvaluatorCalls);

    public void Validate()
    {
        if (!IsValid) throw new InvalidDataException("pattern composed origin is malformed");
    }

    internal static PatternComposedOrigin Create(
        TapeEventID sourceEventID,
        TapeEventID compositionEventID,
        GrammarRevisionID compositionRevision,
        string sourceSHA256,
        string compositionSHA256,
        PatternAlternativeAdmissionSpecies alternativeAdmission,
        long composedEvaluatorCalls,
        long alternativeEvaluatorCalls)
    {
        string originSHA256 = ComputeSHA256(sourceEventID, compositionEventID, compositionRevision,
            sourceSHA256, compositionSHA256, alternativeAdmission, composedEvaluatorCalls, alternativeEvaluatorCalls);
        PatternComposedOrigin origin = new(sourceEventID, compositionEventID, compositionRevision,
            sourceSHA256, compositionSHA256, alternativeAdmission, composedEvaluatorCalls, alternativeEvaluatorCalls,
            originSHA256);
        origin.Validate();
        return origin;
    }

    internal void Write(CkptWriter writer)
    {
        Validate();
        writer.I64(SourceEventID.Value);
        writer.I64(CompositionEventID.Value);
        writer.U64(CompositionRevision.Value);
        writer.Str(SourceSHA256);
        writer.Str(CompositionSHA256);
        writer.U8((byte)AlternativeAdmission);
        writer.I64(ComposedEvaluatorCalls);
        writer.I64(AlternativeEvaluatorCalls);
        writer.Str(OriginSHA256);
    }

    internal static PatternComposedOrigin Read(CkptReader reader)
    {
        PatternComposedOrigin origin = new(
            new TapeEventID(reader.I64()),
            new TapeEventID(reader.I64()),
            new GrammarRevisionID(reader.U64()),
            reader.Str(),
            reader.Str(),
            (PatternAlternativeAdmissionSpecies)reader.U8(),
            reader.I64(),
            reader.I64(),
            reader.Str());
        origin.Validate();
        return origin;
    }

    private static string ComputeSHA256(
        TapeEventID sourceEventID,
        TapeEventID compositionEventID,
        GrammarRevisionID compositionRevision,
        string sourceSHA256,
        string compositionSHA256,
        PatternAlternativeAdmissionSpecies alternativeAdmission,
        long composedEvaluatorCalls,
        long alternativeEvaluatorCalls)
    {
        string canonical = string.Join('|',
            sourceEventID.Value.ToString(CultureInfo.InvariantCulture),
            compositionEventID.Value.ToString(CultureInfo.InvariantCulture),
            compositionRevision.Value.ToString(CultureInfo.InvariantCulture),
            sourceSHA256,
            compositionSHA256,
            alternativeAdmission.ToString(),
            composedEvaluatorCalls.ToString(CultureInfo.InvariantCulture),
            alternativeEvaluatorCalls.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSHA256(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
