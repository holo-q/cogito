namespace Cogito;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Cross-cutting atoms — the identifiers and quantities every module speaks in.
// Each domain identifier is newtyped so a BlobRef can never be passed where a RuleID
// is meant (a compile error, not a silent bug). Acronym-free; *ID stays uppercase.

/// A 32-byte SHA-256 digest (BLAKE3 swap pending — NEXT.md) — the substrate's sole hash width.
[InlineArray(32)]
public struct Hash256 : IEquatable<Hash256>
{
    private byte _b0;

    /// Copy 32 raw bytes (a digest) into a Hash256. The only mint path.
    public static Hash256 From(ReadOnlySpan<byte> bytes)
    {
        Hash256 h = default;
        bytes[..32].CopyTo(MemoryMarshal.CreateSpan(ref h._b0, 32));
        return h;
    }

    public readonly ReadOnlySpan<byte> AsSpan() => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in _b0), 32);
    public readonly bool Equals(Hash256 o) => AsSpan().SequenceEqual(o.AsSpan());
    public override readonly bool Equals(object? o) => o is Hash256 h && Equals(h);
    public override readonly int GetHashCode() => BitConverter.ToInt32(AsSpan());
    public override readonly string ToString() => Convert.ToHexStringLower(AsSpan()[..8]) + "…";
}

/// Content address of a stored blob: BLAKE3 over its canonical envelope bytes. Identity for replay/audit.
public readonly struct BlobRef(Hash256 hash) : IEquatable<BlobRef>
{
    public readonly Hash256 Hash = hash;
    public bool Equals(BlobRef o) => Hash.Equals(o.Hash);
    public override bool Equals(object? o) => o is BlobRef b && Equals(b);
    public override int GetHashCode() => Hash.GetHashCode();
    public override string ToString() => Hash.ToString();
}

/// Content address of a grammar rule's (pattern, context): identical patterns converge to one ID across runs.
public readonly struct RuleID(Hash256 hash) : IEquatable<RuleID>
{
    public readonly Hash256 Hash = hash;
    public bool Equals(RuleID o) => Hash.Equals(o.Hash);
    public override bool Equals(object? o) => o is RuleID r && Equals(r);
    public override int GetHashCode() => Hash.GetHashCode();
    public override string ToString() => Hash.ToString();
}

/// Schema identifier (u16 space, ≤ 2025). What kind of packet an Envelope wraps.
public readonly struct SchemaID(ushort value) : IEquatable<SchemaID>
{
    public readonly ushort Value = value;
    public bool Equals(SchemaID o) => Value == o.Value;
    public override bool Equals(object? o) => o is SchemaID s && Equals(s);
    public override int GetHashCode() => Value;
    public override string ToString() => Value.ToString();
}

/// Stable identity of a numeric quantity emitted by a runtime organ. IDs come from the metric catalog;
/// they are never hashes of display names, so renaming a curve cannot mutate the learner's vocabulary.
public readonly struct MetricID(ushort value) : IEquatable<MetricID>, IComparable<MetricID>
{
    public readonly ushort Value = value;
    public int CompareTo(MetricID other) => Value.CompareTo(other.Value);
    public bool Equals(MetricID other) => Value == other.Value;
    public override bool Equals(object? other) => other is MetricID metricID && Equals(metricID);
    public override int GetHashCode() => Value;
    public override string ToString() => Value.ToString();
}

/// Numeric representations carried by a metric frame. The kind is part of the value: equal payload bits
/// under different kinds are different observations.
public enum NumericKinds : byte
{
    I64 = 0,
    U64 = 1,
    F64 = 2
}

/// One exact numeric value in the metric protocol. Floating-point values preserve their IEEE payload except
/// that signed zero and NaN spellings are canonicalized, preventing equivalent values from fragmenting grammar.
public readonly struct NumericValue : IEquatable<NumericValue>
{
    private const ulong CanonicalNaNBits = 0x7FF8_0000_0000_0000UL;
    private const ulong SignlessBits = 0x7FFF_FFFF_FFFF_FFFFUL;
    private const ulong ExponentBits = 0x7FF0_0000_0000_0000UL;
    private const ulong MantissaBits = 0x000F_FFFF_FFFF_FFFFUL;

    private NumericValue(NumericKinds kind, ulong bits)
    {
        Kind = kind;
        Bits = bits;
    }

    public NumericKinds Kind { get; }
    public ulong Bits { get; }

    public static NumericValue FromI64(long value) => new(NumericKinds.I64, unchecked((ulong)value));
    public static NumericValue FromU64(ulong value) => new(NumericKinds.U64, value);

    public static NumericValue FromF64(double value)
    {
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        ulong magnitude = bits & SignlessBits;
        if (magnitude == 0) bits = 0;
        else if ((magnitude & ExponentBits) == ExponentBits && (magnitude & MantissaBits) != 0)
            bits = CanonicalNaNBits;
        return new NumericValue(NumericKinds.F64, bits);
    }

    public long GetI64() => Kind == NumericKinds.I64
        ? unchecked((long)Bits)
        : throw new InvalidOperationException($"numeric value is {Kind}, not I64");

    public ulong GetU64() => Kind == NumericKinds.U64
        ? Bits
        : throw new InvalidOperationException($"numeric value is {Kind}, not U64");

    public double GetF64() => Kind == NumericKinds.F64
        ? BitConverter.Int64BitsToDouble(unchecked((long)Bits))
        : throw new InvalidOperationException($"numeric value is {Kind}, not F64");

    public bool Equals(NumericValue other) => Kind == other.Kind && Bits == other.Bits;
    public override bool Equals(object? other) => other is NumericValue value && Equals(value);
    public override int GetHashCode() => HashCode.Combine((byte)Kind, Bits);
}

/// A catalogued quantity inside one metric frame.
public readonly struct MetricSample(MetricID metricID, NumericValue value)
{
    public MetricID MetricID { get; } = metricID;
    public NumericValue Value { get; } = value;
}

/// Position in the append-only log. Linear, 0-based, monotonic.
public readonly struct EventID(ulong value) : IEquatable<EventID>, IComparable<EventID>
{
    public static readonly EventID Zero = new(0);
    public readonly ulong Value = value;
    public EventID Next => new(Value + 1);
    public int CompareTo(EventID o) => Value.CompareTo(o.Value);
    public bool Equals(EventID o) => Value == o.Value;
    public override bool Equals(object? o) => o is EventID e && Equals(e);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"#{Value}";
}

/// A grammar symbol. 0–255 are byte terminals (Σ); ≥256 are induced nonterminals.
public readonly struct Symbol(uint value) : IEquatable<Symbol>
{
    public const uint FirstNonterminal = 256;
    public readonly uint Value = value;
    public static Symbol Terminal(byte b) => new(b);
    public bool IsTerminal => Value < FirstNonterminal;
    public bool IsNonterminal => Value >= FirstNonterminal;
    public bool Equals(Symbol o) => Value == o.Value;
    public override bool Equals(object? o) => o is Symbol s && Equals(s);
    public override int GetHashCode() => (int)Value;
    public override string ToString() => IsTerminal ? $"'{(char)Value}'" : $"N{Value}";
}

/// Description length in milli-bits — the MDL currency. Integer-only; no float ever touches consensus.
public readonly struct Mbits(long value) : IComparable<Mbits>, IEquatable<Mbits>
{
    public static readonly Mbits Zero = new(0);
    public readonly long Value = value;
    public static Mbits operator +(Mbits a, Mbits b) => new(a.Value + b.Value);
    public static Mbits operator -(Mbits a, Mbits b) => new(a.Value - b.Value);
    public static Mbits operator *(long k, Mbits b) => new(k * b.Value);
    public static bool operator >=(Mbits a, Mbits b) => a.Value >= b.Value;
    public static bool operator <=(Mbits a, Mbits b) => a.Value <= b.Value;
    public static bool operator >(Mbits a, Mbits b) => a.Value > b.Value;
    public static bool operator <(Mbits a, Mbits b) => a.Value < b.Value;
    public int CompareTo(Mbits o) => Value.CompareTo(o.Value);
    public bool Equals(Mbits o) => Value == o.Value;
    public override bool Equals(object? o) => o is Mbits m && Equals(m);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"{Value}mb";
}
