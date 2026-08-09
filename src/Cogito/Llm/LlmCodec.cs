namespace Cogito;

using System.Text;

// The associative codec beyond code-golf. Re-Pair gives the mechanical floor (literal repetition, free). An LLM
// proposes the ASSOCIATIVE residual — structure it predicts but Re-Pair can't, because the pattern is
// semantic, not byte-literal — carried as a dense codec + cue-sheet (the firestarter, DATA not weights).
// A FROZEN reader cold-reads it back; the codec prices the result against the mechanical floor.
//
// The seams are client-agnostic: Errloom.ITextModel or cogito's own HTTP plug in behind IProposer /
// IColdReader. Until a live model is wired, IdentityProposer proves the codec's plumbing end to end.

/// A dense codec: the compressed body + the cue-sheet (carried as data, reconstructed by the reader's priors).
public readonly record struct Pack(string Body, string CueSheet)
{
    /// Budget in bytes. The HONEST metric is the reader's BPE tokenizer (o200k); bytes is the v0 proxy
    /// (the PRICING LAW: density is only real in the reader's tokenizer — wire o200k at Phase 2).
    public int Budget => Encoding.UTF8.GetByteCount(Body) + Encoding.UTF8.GetByteCount(CueSheet);
}

/// Proposes an associative compression beyond the mechanical floor (the LLM seam, or a deterministic stub).
public interface IProposer { Pack Propose(byte[] source); }

/// The cold-read verifier: a frozen reader reconstructs the bytes from (codec, cue-sheet) ALONE.
public interface IColdReader { byte[] Decode(Pack codec); }

public readonly record struct LlmCodecResult(bool Lossless, int Budget, int FloorBudget, int SourceBytes)
{
    /// Beyond code-golf iff it reconstructs exactly AND undercuts the mechanical floor.
    public bool BeyondGolf => Lossless && Budget < FloorBudget;
}

public static class LlmCodec
{
    /// propose → cold-read → verify (H==H) → price against the Re-Pair mechanical floor.
    public static LlmCodecResult Prove(byte[] source, IProposer proposer, IColdReader reader)
    {
        var pack = proposer.Propose(source);
        var recon = reader.Decode(pack);
        bool lossless = recon.AsSpan().SequenceEqual(source);
        return new LlmCodecResult(lossless, pack.Budget, FloorBudget(source), source.Length);
    }

    /// The Re-Pair grammar's honest byte size — the baseline the associative proposer must beat. Each
    /// symbol (compressed start seq + every rule RHS) priced at log2(|V|) bits, entropy-coded — NOT a
    /// profligate U32. This is the information-theoretic floor; it MUST come out below the source size,
    /// or it isn't compression. (v0 prices in bytes; the honest metric is the reader's BPE tokenizer.)
    public static int FloorBudget(byte[] source)
    {
        var (_, _, r) = Engine.Induce(source);
        int symbols = r.Compressed.Length;
        foreach (var rule in r.Rules) symbols += rule.Pattern.Length;
        double bitsPerSymbol = Math.Log2(Math.Max(2, (int)Symbol.FirstNonterminal + r.Rules.Length));
        return (int)Math.Ceiling(symbols * bitsPerSymbol / 8.0);
    }
}

/// Plumbing smoke: pack body = the source, base64'd, no compression. Lossless, budget ≈ source size.
/// Proves the codec's propose→read→verify→price loop end to end with zero deps; the LLM proposer is the real one.
public sealed class IdentityProposer : IProposer, IColdReader
{
    public Pack Propose(byte[] source) => new(Convert.ToBase64String(source), "");
    public byte[] Decode(Pack codec) => Convert.FromBase64String(codec.Body);
}
