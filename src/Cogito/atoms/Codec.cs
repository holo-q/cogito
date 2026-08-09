namespace Cogito.Codec;

using System.Buffers.Binary;
using System.Security.Cryptography;

// The determinism bedrock: canonical byte encoding, domain-separated hashing, fixed-point math.
// Everything the substrate commits to flows through here. No floats, no reflection, no ambiguity.
// Pinned decisions: little-endian ints, LE32 envelope header, trailing-slash domain tags,
// Vec = LE64(len)‖items, String = LE64(len)‖utf8, half-away-from-zero rounding.

/// COGITO Canonical Codec — total, injective, integer-only encoding into a caller-owned span.
public ref struct CccWriter(Span<byte> dest)
{
    private readonly Span<byte> _dest = dest;
    public int Written { get; private set; }

    public void U8(byte v) { _dest[Written] = v; Written += 1; }
    public void U16(ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(_dest[Written..], v); Written += 2; }
    public void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(_dest[Written..], v); Written += 4; }
    public void U64(ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(_dest[Written..], v); Written += 8; }
    public void I64(long v) { BinaryPrimitives.WriteInt64LittleEndian(_dest[Written..], v); Written += 8; }
    public void Bool(bool v) { U8(v ? (byte)1 : (byte)0); }
    public void Bytes(ReadOnlySpan<byte> v) { U64((ulong)v.Length); v.CopyTo(_dest[Written..]); Written += v.Length; }   // LE64(len) ‖ raw
    public void Utf8(ReadOnlySpan<byte> v) => Bytes(v);                                                                   // LE64(len) ‖ utf8 (validated upstream)
    public void Raw(ReadOnlySpan<byte> v) { v.CopyTo(_dest[Written..]); Written += v.Length; }                            // fixed-width, NO length prefix
    public void Digest(in Hash256 h) => Raw(h.AsSpan());                                                                  // a 32-byte digest field
}

/// CCC decoder. Canonicality is enforced by the caller: any trailing byte after a complete decode
/// (i.e. `!AtEnd`) is a hard error (TrailingBytes).
public ref struct CccReader(ReadOnlySpan<byte> src)
{
    private readonly ReadOnlySpan<byte> _src = src;
    public int Offset { get; private set; }
    public readonly bool AtEnd => Offset == _src.Length;

    public byte U8() { var v = _src[Offset]; Offset += 1; return v; }
    public ushort U16() { var v = BinaryPrimitives.ReadUInt16LittleEndian(_src[Offset..]); Offset += 2; return v; }
    public uint U32() { var v = BinaryPrimitives.ReadUInt32LittleEndian(_src[Offset..]); Offset += 4; return v; }
    public ulong U64() { var v = BinaryPrimitives.ReadUInt64LittleEndian(_src[Offset..]); Offset += 8; return v; }
    public long I64() { var v = BinaryPrimitives.ReadInt64LittleEndian(_src[Offset..]); Offset += 8; return v; }
    public bool Bool() => U8() != 0;
    public ReadOnlySpan<byte> Bytes() { var n = (int)U64(); var v = _src.Slice(Offset, n); Offset += n; return v; }
    public ReadOnlySpan<byte> Raw(int n) { var v = _src.Slice(Offset, n); Offset += n; return v; }                        // fixed-width, NO length prefix
    public Hash256 Digest() => Hash256.From(Raw(32));                                                                     // a 32-byte digest field
}

/// Domain-separated hashing. Every committed hash is *computed* here — never hand-typed.
/// v0 pins one suite (SHA-256, BCL, zero-dep); the suite is genesis-tagged and dispatched here, so a
/// future metamorphosis (chain-fork) can re-forge it with a deterministic old→new reference mapping.
/// NOTE(codec proof): swap SHA-256 → BLAKE3 (the canonical Q* suite). Round-trip + replay are
/// hash-agnostic, so the logic proves out now and the swap is this one primitive. Tags carry a slash.
public static class Hash
{
    /// Core construction: H(tag ‖ msg).
    public static Hash256 Domain(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> msg)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(tag);
        h.AppendData(msg);
        Span<byte> digest = stackalloc byte[32];
        h.GetHashAndReset(digest);
        return Hash256.From(digest);
    }

    /// H_blob = H("cogito/blob/" ‖ LE32(schema) ‖ LE32(version) ‖ payload).
    public static BlobRef Blob(SchemaID schema, ushort version, ReadOnlySpan<byte> payload)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData("cogito/blob/"u8);
        Span<byte> hdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, schema.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], version);
        h.AppendData(hdr);
        h.AppendData(payload);
        Span<byte> digest = stackalloc byte[32];
        h.GetHashAndReset(digest);
        return new BlobRef(Hash256.From(digest));
    }

    /// H_event = H("cogito/event/" ‖ LE32(schema) ‖ LE32(version) ‖ LE64(eventId) ‖ payload).
    public static Hash256 Event(SchemaID schema, ushort version, EventID eventId, ReadOnlySpan<byte> payload)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData("cogito/event/"u8);
        Span<byte> hdr = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, schema.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], version);
        BinaryPrimitives.WriteUInt64LittleEndian(hdr[8..], eventId.Value);
        h.AppendData(hdr);
        h.AppendData(payload);
        Span<byte> digest = stackalloc byte[32];
        h.GetHashAndReset(digest);
        return Hash256.From(digest);
    }

    /// rule_id = H("cogito/rule_id/" ‖ ccc(pattern)). Content-addressed rule identity.
    public static RuleID Rule(ReadOnlySpan<byte> patternCcc)
        => new(Domain("cogito/rule_id/"u8, patternCcc));
}

/// Fixed-point milli-bit arithmetic — integer-only; no float touches consensus.
public static class Fixed
{
    /// log2_mbits(v) = round(1000 · log₂ v), integer and deterministic. Integer part from the high
    /// bit; fractional part by the classic squaring method (~20 bits, far past milli resolution).
    /// v ≤ 1 → 0.  (NOTE(gate): pin byte-exact to appendix-a's EXP_NEG_TABLE-class vectors.)
    public static Mbits Log2(uint v)
    {
        if (v <= 1) return Mbits.Zero;
        int intLog = 31 - System.Numerics.BitOperations.LeadingZeroCount(v);
        long milli = (long)intLog * 1000;

        // x is Q32 representing  v / 2^intLog ∈ [1, 2)   (1.0 == 2^32).
        ulong x = (ulong)v << (32 - intLog);
        long add = 500;                                       // weight of fractional bit k: 1000·2^-k
        for (int k = 1; k <= 20 && add > 0; k++)
        {
            x = (ulong)(((UInt128)x * x) >> 32);              // square (Q32)
            if (x >= (1UL << 33)) { x >>= 1; milli += add; }  // x ≥ 2.0 → emit bit, renormalize
            add >>= 1;
        }
        return new Mbits(milli);
    }
}
