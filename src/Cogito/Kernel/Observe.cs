namespace Cogito.Observe;

using System.Text;
using Cogito.Cas;
using Cogito.Codec;

// The observation channel — the ONLY thing the world-model grammar learns over. Control-plane
// events (grammar versions, theorems) live in the same log but are never a learning target.
// This file is the plane-separation, made structural.

/// Source-of-record UTF-8 text, newline-normalized. The primary observation modality. Schema 10.
public readonly struct TextBlob(ReadOnlyMemory<byte> utf8)
{
    public const ushort Schema = 10;
    public readonly ReadOnlyMemory<byte> Utf8 = utf8;

    /// CRLF/CR → LF, reject invalid UTF-8 at the boundary. Idempotent.
    public static TextBlob Normalize(ReadOnlySpan<byte> raw)
    {
        var buf = new byte[raw.Length];   // newline folding only ever shrinks (CRLF→LF) or preserves length
        var w = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            if (b == 0x0D)                                              // CR
            {
                buf[w++] = 0x0A;                                       // → LF
                if (i + 1 < raw.Length && raw[i + 1] == 0x0A) i++;     // CRLF: swallow the trailing LF
            }
            else buf[w++] = b;
        }
        var utf8 = new ReadOnlyMemory<byte>(buf, 0, w);
        if (!System.Text.Unicode.Utf8.IsValid(utf8.Span))
            throw new ArgumentException("TextBlob.Normalize: input is not valid UTF-8", nameof(raw));
        return new TextBlob(utf8);
    }

    /// Schema-10 envelope; payload is a single CCC Bytes field (LE64(len) ‖ utf8).
    public Envelope ToEnvelope()
    {
        var utf8 = Utf8.Span;
        var buf = new byte[8 + utf8.Length];
        var w = new CccWriter(buf);
        w.Bytes(utf8);
        return new Envelope(new SchemaID(Schema), 1, buf);
    }
}

/// An observation entered the agent's world from `Source`, pointing at the stored text. Schema 100.
public readonly struct ObsTextEvent(string source, BlobRef textRef)
{
    public const ushort Schema = 100;
    public readonly string Source = source;
    public readonly BlobRef TextRef = textRef;

    /// Schema-100 envelope; payload = source (CCC Utf8) ‖ textRef (32 raw digest bytes).
    public Envelope ToEnvelope()
    {
        var src = Encoding.UTF8.GetBytes(Source);
        var buf = new byte[8 + src.Length + 32];
        var w = new CccWriter(buf);
        w.Utf8(src);
        w.Digest(TextRef.Hash);
        return new Envelope(new SchemaID(Schema), 1, buf);
    }

    public static ObsTextEvent Decode(in Envelope e)
    {
        var r = new CccReader(e.Payload.Span);
        var source = Encoding.UTF8.GetString(r.Bytes());
        var textRef = new BlobRef(r.Digest());
        if (!r.AtEnd) throw new FormatException("ObsTextEvent.Decode: trailing bytes after payload");
        return new ObsTextEvent(source, textRef);
    }
}

/// Maps observation bytes → grammar terminal symbols. The seam where BPE / WASM symbolizers plug in later.
public interface ITokenizer
{
    /// Fills a caller-owned buffer (no allocation); returns the symbol count written.
    int Tokenize(ReadOnlySpan<byte> utf8, Span<Symbol> dest);
    int MaxSymbols(int byteCount);
}

/// v0: identity tokenizer. Each byte → one terminal symbol (Σ = 256). Deterministic, model-free, the A7 alphabet.
public sealed class ByteTokenizer : ITokenizer
{
    /// Shared instance — ByteTokenizer is stateless (pure identity map), so one serves every caller; never `new` it in a loop.
    public static readonly ByteTokenizer Instance = new();

    public int Tokenize(ReadOnlySpan<byte> utf8, Span<Symbol> dest)
    {
        for (var i = 0; i < utf8.Length; i++) dest[i] = Symbol.Terminal(utf8[i]);
        return utf8.Length;
    }
    public int MaxSymbols(int byteCount) => byteCount;
}
