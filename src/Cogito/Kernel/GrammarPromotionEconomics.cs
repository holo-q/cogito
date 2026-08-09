namespace Cogito.Grammar;

using System.Security.Cryptography;
using System.Text;
using Cogito.Codec;
using Cogito.Induct;

/// The common marginal-MDL price for promoting one ordinary candidate into the
/// standing grammar.  Candidate identity and custody live with the caller; this
/// substrate prices only the exact baseline/candidate pair.
public readonly record struct GrammarAdmissionMdlPrice(
    long LiteralCostMbits,
    long MaterializedCostMbits,
    long MarginalSavingsMbits)
{
    public bool IsPositive => MarginalSavingsMbits > 0;

    public void Validate()
    {
        if (MarginalSavingsMbits != checked(LiteralCostMbits - MaterializedCostMbits))
            throw new InvalidDataException("grammar promotion marginal MDL delta does not close");
    }
}

/// Candidate-agnostic economics for the one production Loom/Re-Pair path.
/// The raw tape and weights are supplied by the owning runtime, so no promotion
/// path can reopen a repository or substitute source bytes for GrammarInput.
public static class GrammarAdmissionEconomics
{
    public static GrammarAdmissionMdlPrice PriceMaterialization(
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        ReadOnlySpan<byte> candidate,
        int wScale)
    {
        Tape.RequireWScale(wScale);
        byte[] candidateBytes = FrameCandidate(candidate);
        Symbol[] literalSequence = new Symbol[baseline.Compressed.Length + candidateBytes.Length];
        baseline.Compressed.CopyTo(literalSequence, 0);
        for (int i = 0; i < candidateBytes.Length; i++)
            literalSequence[baseline.Compressed.Length + i] = new Symbol(candidateBytes[i]);
        Mbits literal = DescriptionLength(baseline.Rules, literalSequence, baseline.AlphabetSize);

        Symbol[] extended = new Symbol[rawTape.Length + candidateBytes.Length];
        rawTape.CopyTo(extended);
        for (int i = 0; i < candidateBytes.Length; i++)
            extended[rawTape.Length + i] = new Symbol(candidateBytes[i]);
        if (rawWeights.Length != rawTape.Length)
            throw new InvalidDataException("grammar promotion MDL symbols and weights differ");
        byte[] weights = new byte[extended.Length];
        rawWeights.CopyTo(weights);
        weights.AsSpan(rawTape.Length).Fill((byte)wScale);
        RePairResult materialized = new RePair().Induce(extended, Mbits.Zero, baseline.AlphabetSize,
            barrier: (uint)'\n', weights: weights, wScale: wScale);
        Mbits materializedCost = DescriptionLength(materialized.Rules, materialized.Compressed, materialized.AlphabetSize);
        return new(literal.Value, materializedCost.Value, checked(literal.Value - materializedCost.Value));
    }

    public static string ComputeBasisDigest(
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        ReadOnlySpan<byte> candidate,
        int wScale,
        string basisSpecies = "grammar-promotion")
    {
        byte[] framedCandidate = FrameCandidate(candidate);
        if (string.IsNullOrWhiteSpace(basisSpecies)) throw new ArgumentException("basis species is empty", nameof(basisSpecies));
        StringBuilder material = new(basisSpecies + "-priced-basis-v2|literal-raw-terminals-vs-materialized-reinduction|");
        material.Append(wScale).Append('|').Append(baseline.AlphabetSize).Append('|').Append(baseline.Rules.Length).Append('|').Append(baseline.Compressed.Length).Append('|')
            .Append(rawTape.Length).Append('|').Append(rawWeights.Length).Append('|').Append(framedCandidate.Length).Append('|');
        for (int i = 0; i < baseline.Rules.Length; i++) material.Append(baseline.Rules[i].Id).Append(';');
        material.Append('|');
        for (int i = 0; i < baseline.Compressed.Length; i++) material.Append(baseline.Compressed[i].Value).Append(',');
        material.Append('|');
        for (int i = 0; i < rawTape.Length; i++) material.Append(rawTape[i].Value).Append(',');
        material.Append('|');
        for (int i = 0; i < rawWeights.Length; i++) material.Append(rawWeights[i]).Append(',');
        material.Append('|');
        for (int i = 0; i < framedCandidate.Length; i++) material.Append(framedCandidate[i]).Append(',');
        return DigestText(material.ToString());
    }

    internal static Mbits DescriptionLength(GrammarRule[] rules, Symbol[] compressed, uint alphabetSize)
        => GrammarSpec.WithRules(0, rules).Cost + new Mbits(checked((long)compressed.Length * Fixed.Log2(alphabetSize).Value));

    internal static byte[] FrameCandidate(ReadOnlySpan<byte> candidate)
    {
        byte[] framed = new byte[candidate.Length + (candidate.Length == 0 || candidate[^1] == (byte)'\n' ? 0 : 1)];
        candidate.CopyTo(framed);
        if (framed.Length > candidate.Length) framed[^1] = (byte)'\n';
        return framed;
    }

    private static string DigestText(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
