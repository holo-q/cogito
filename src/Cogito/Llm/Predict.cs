namespace Cogito;

using System.Text;

// The predictive stage: compression = prediction + residual (the two-part MDL, made live).
// The LLM is a DETERMINISTIC predictor (verified: same prompt → byte-identical output across calls), so
// cogito can treat f(prompt) as a fixed decompressor whose "dictionary" is the model's priors. cogito OWNS
// the residual — the byte-diff between the LLM's regeneration and the true source — so reconstruction is
// LOSSLESS even where the priors are only approximately right. Codec = (description, residual); for code
// inside the model's canonical attractor the residual vanishes and the codec is just the description.
// cogito drives + verifies; the LLM is a bounded predictor, never the owner of the codec.

/// A contiguous byte-delta turning `from` into `to`: shared prefix + shared suffix + the differing middle.
/// Optimal when the prediction and the target differ in one localized region (the common case for good priors).
public readonly record struct Delta(int Prefix, int Suffix, byte[] Middle)
{
    public int Size => 8 + Middle.Length;   // 2×int32 framing + the literal middle bytes

    public static Delta Between(byte[] from, byte[] to)
    {
        int p = 0;
        while (p < from.Length && p < to.Length && from[p] == to[p]) p++;
        int s = 0;
        while (s < from.Length - p && s < to.Length - p && from[^(s + 1)] == to[^(s + 1)]) s++;
        return new Delta(p, s, to[p..(to.Length - s)]);
    }

    public byte[] ApplyTo(byte[] from)
    {
        var res = new byte[Prefix + Middle.Length + Suffix];
        from.AsSpan(0, Prefix).CopyTo(res);
        Middle.CopyTo(res.AsSpan(Prefix));
        from.AsSpan(from.Length - Suffix, Suffix).CopyTo(res.AsSpan(Prefix + Middle.Length));
        return res;
    }
}

/// The predictive codec. ENCODE: describe the source → the LLM regenerates it → cogito diffs (prediction
/// vs target) → residual. DECODE: re-run the same (deterministic) regeneration → cogito applies the residual.
public sealed class PredictiveCodec(ILlm llm)
{
    public readonly record struct Encoded(string Description, Delta Residual, byte[] Prediction)
    {
        public int Budget => Encoding.UTF8.GetByteCount(Description) + Residual.Size;
    }

    public Encoded Encode(byte[] source)
    {
        var description = llm.Complete(Describe, Encoding.UTF8.GetString(source)).Trim();
        var prediction = Regenerate(description);
        return new Encoded(description, Delta.Between(prediction, source), prediction);
    }

    public byte[] Decode(string description, Delta residual) => residual.ApplyTo(Regenerate(description));

    private byte[] Regenerate(string description)
        => Encoding.UTF8.GetBytes(LlmText.Exact(llm.Complete(Regen, description), "<<<SRC", "SRC>>>"));

    private const string Describe = """
        Describe the following source as TERSELY as possible — a regeneration prompt for yourself. Given
        ONLY your description (no access to the original), you must regenerate this source as closely as you
        can. Lean on your canonical style and shared coding priors: name standard algorithms rather than
        spelling them out; give only the specifics you could not otherwise predict (identifier names, exact
        literals, unusual formatting). Output ONLY the description — one block, no preamble, no fences.
        """;

    private const string Regen = """
        Regenerate the source from the description below, in your most canonical style — byte-for-byte as you
        would naturally write it. Emit it delimited by sentinels: the source's FIRST byte IMMEDIATELY after
        `<<<SRC` (no inserted newline), `SRC>>>` IMMEDIATELY after the source's LAST byte. Output nothing else.
        <<<SRC{the source}SRC>>>
        """;
}
