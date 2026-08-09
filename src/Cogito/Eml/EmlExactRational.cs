namespace Cogito;

using System.Globalization;
using System.Numerics;

public readonly struct EmlExactRational : IComparable<EmlExactRational>, IEquatable<EmlExactRational>
{
    private readonly BigInteger _denominatorMinusOne;

    public EmlExactRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException("an exact rational denominator cannot be zero");
        if (denominator.Sign < 0)
        {
            numerator = BigInteger.Negate(numerator);
            denominator = BigInteger.Negate(denominator);
        }

        BigInteger gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        _denominatorMinusOne = denominator / gcd - BigInteger.One;
    }

    public BigInteger Numerator { get; }
    public BigInteger Denominator => _denominatorMinusOne + BigInteger.One;

    public static EmlExactRational Zero => new(BigInteger.Zero, BigInteger.One);
    public static EmlExactRational One => new(BigInteger.One, BigInteger.One);

    public static EmlExactRational operator +(EmlExactRational left, EmlExactRational right)
        => new(left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static EmlExactRational operator -(EmlExactRational left, EmlExactRational right)
        => new(left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static EmlExactRational operator -(EmlExactRational value)
        => new(BigInteger.Negate(value.Numerator), value.Denominator);

    public static EmlExactRational operator *(EmlExactRational left, EmlExactRational right)
        => new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static EmlExactRational operator /(EmlExactRational left, EmlExactRational right)
        => new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);

    public static bool operator <(EmlExactRational left, EmlExactRational right) => left.CompareTo(right) < 0;
    public static bool operator >(EmlExactRational left, EmlExactRational right) => left.CompareTo(right) > 0;
    public static bool operator <=(EmlExactRational left, EmlExactRational right) => left.CompareTo(right) <= 0;
    public static bool operator >=(EmlExactRational left, EmlExactRational right) => left.CompareTo(right) >= 0;
    public static bool operator ==(EmlExactRational left, EmlExactRational right) => left.Equals(right);
    public static bool operator !=(EmlExactRational left, EmlExactRational right) => !left.Equals(right);

    public int CompareTo(EmlExactRational other)
        => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool Equals(EmlExactRational other)
        => Numerator.Equals(other.Numerator) && Denominator.Equals(other.Denominator);

    public override bool Equals(object? obj) => obj is EmlExactRational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public override string ToString() => Denominator.IsOne
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : string.Concat(
            Numerator.ToString(CultureInfo.InvariantCulture),
            "/",
            Denominator.ToString(CultureInfo.InvariantCulture));
}

public readonly struct EmlCertifiedInterval : IEquatable<EmlCertifiedInterval>
{
    public EmlCertifiedInterval(EmlExactRational lower, EmlExactRational upper)
    {
        if (lower > upper) throw new ArgumentOutOfRangeException(nameof(lower), "interval lower bound exceeds upper bound");
        Lower = lower;
        Upper = upper;
    }

    public EmlExactRational Lower { get; }
    public EmlExactRational Upper { get; }
    public EmlExactRational Width => Upper - Lower;

    public bool Contains(EmlCertifiedInterval other) => Lower <= other.Lower && other.Upper <= Upper;
    public bool Equals(EmlCertifiedInterval other) => Lower == other.Lower && Upper == other.Upper;
    public override bool Equals(object? obj) => obj is EmlCertifiedInterval other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Lower, Upper);
    public static bool operator ==(EmlCertifiedInterval left, EmlCertifiedInterval right) => left.Equals(right);
    public static bool operator !=(EmlCertifiedInterval left, EmlCertifiedInterval right) => !left.Equals(right);
    public override string ToString() => $"[{Lower},{Upper}]";
}
