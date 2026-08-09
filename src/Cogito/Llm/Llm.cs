namespace Cogito;

using System.Text;

// The LLM seam for the associative stage — the proposer + the cold reader. Client-agnostic: a live client
// (codex / Errloom.ITextModel / own HTTP) implements ILlm; the proposer/reader logic + the prompts are
// independent of WHICH model answers. The prompts iterate as the live model reveals its behavior — the
// encode-search is exactly where iteration pays.

/// A chat-completion seam: one system+user turn → the model's text. The only endpoint-dependent piece.
public interface ILlm { string Complete(string system, string user); }

/// The associative proposer — asks the model to compress source into a dense pack + cue-sheet that IT can
/// reconstruct. The compression beyond Re-Pair's literal floor lives in the model's shared priors.
public sealed class LlmProposer(ILlm llm) : IProposer
{
    public Pack Propose(byte[] source)
    {
        int floor = LlmCodec.FloorBudget(source);   // cogito's Re-Pair floor — the literal bar the LLM must beat
        var user = $"Re-Pair already compressed this source to {floor} bytes by extracting LITERAL repetition.\n"
                 + $"Your job is the residual it can't reach. Beat the {floor}-byte floor with associative rules.\n\n"
                 + $"SOURCE:\n{Encoding.UTF8.GetString(source)}";
        var resp = llm.Complete(System, user);
        return new Pack(LlmText.Between(resp, "<pack>", "</pack>"), LlmText.Between(resp, "<cue>", "</cue>"));
    }

    private const string System = """
        You are the ASSOCIATIVE half of a two-stage compressor. cogito's Re-Pair already took the mechanical
        floor — literal repetition → rules, deterministically. Your job is the RESIDUAL it cannot reach:
        structure that is predictable from shared coding priors but is NOT literal repetition.

        Emit a GRAMMAR — glyph-rules (cue) + a skeleton (pack) — that your counterpart, a frozen reader
        sharing your exact priors, reconstructs into the source BYTE-FOR-BYTE from them alone. Your glyphs
        LEAN ON PRIORS: a glyph may be far shorter than what it expands to, because the reader regenerates
        the rest — a standard algorithm → its name; a family of similar functions → the pattern + the varying
        params; idiomatic boilerplate → a hint. That is the ONLY way to beat the literal floor.

        The total (skeleton + glyph-rules) MUST come in UNDER the Re-Pair floor stated in the message, or you
        have added nothing beyond code-golf. Minimize it.

        RULES:
        - Reconstruct byte-for-byte: every space, every blank line (count them exactly), the exact final newline.
        - Any shorthand you define WILL be literally substituted by the reader at every occurrence.
        - SELF-CHECK before emitting: mentally reconstruct from your skeleton+rules and confirm byte-identical.
          If it differs, fix and re-check. Do NOT emit until it round-trips exactly.

        Output EXACTLY this, nothing else:
        <pack>
        {skeleton: the source as glyph-references + the literal gaps between them}
        </pack>
        <cue>
        {glyph-rules: each `glyph = what it expands to / how to regenerate it`, + atomic unpredictable tokens}
        </cue>
        """;
}

/// The cold reader — a FROZEN model reconstructs the source from (pack, cue) alone. H(recon)==H(src) ⟹
/// the pack is load-bearing: the structure transferred through shared priors, not the encoder's private memory.
/// The source is delimited by sentinels so trailing whitespace survives the transport's message-trim.
public sealed class LlmColdReader(ILlm llm) : IColdReader
{
    public byte[] Decode(Pack pack)
    {
        var user = $"<pack>\n{pack.Body}\n</pack>\n<cue>\n{pack.CueSheet}\n</cue>";
        var resp = llm.Complete(System, user);
        return Encoding.UTF8.GetBytes(LlmText.Exact(resp, "<<<SRC", "SRC>>>"));
    }

    private const string System = """
        You are a LOSSLESS code decompressor sharing your counterpart's exact priors. Given a <pack> (dense
        structural recipe) and <cue> (atomic unpredictable tokens + legend), regenerate the EXACT original
        source, byte-for-byte: every space, blank line, and the precise final newline.

        Emit the source delimited by the two sentinels below — the source's FIRST byte IMMEDIATELY after
        `<<<SRC` (no inserted newline) and `SRC>>>` IMMEDIATELY after the source's LAST byte. Output nothing
        outside the sentinels.
        <<<SRC{the exact source bytes}SRC>>>
        """;
}

internal static class LlmText
{
    /// Content between the first `open` and the next `close`, stripped of wrapping newlines (for pack/cue).
    public static string Between(string s, string open, string close)
    {
        int a = s.IndexOf(open, StringComparison.Ordinal);
        if (a < 0) return "";
        a += open.Length;
        int b = s.IndexOf(close, a, StringComparison.Ordinal);
        return (b < 0 ? s[a..] : s[a..b]).Trim('\n', '\r');
    }

    /// Content between sentinels, EXACT — no trimming (for the reconstructed source, where every byte counts).
    /// Markers missing → return the whole response, so a non-cooperating model still yields bytes to diagnose.
    public static string Exact(string s, string open, string close)
    {
        int a = s.IndexOf(open, StringComparison.Ordinal);
        if (a < 0) return s;
        a += open.Length;
        int b = s.IndexOf(close, a, StringComparison.Ordinal);
        return b < 0 ? s[a..] : s[a..b];
    }
}
