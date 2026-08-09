namespace Cogito;

using System.Numerics;
using System.Security.Cryptography;
using System.Text;

public enum EmlProcessConstantAlgorithms
{
    CatalanAlternating = 1,
    Zeta3IntegralTail = 2,
}

public readonly record struct EmlProcessConstantState(
    EmlProcessConstantAlgorithms Algorithm,
    int Version,
    long Terms,
    long FuelSpent,
    EmlExactRational PartialSum);

public readonly record struct EmlProcessRemainderCorroboration(
    string Rule,
    EmlExactRational LowerOffset,
    EmlExactRational UpperOffset);

public readonly record struct EmlProcessConstantCertificate(
    EmlProcessConstantAlgorithms Algorithm,
    int Version,
    long Terms,
    long Fuel,
    string StateDigest,
    EmlCertifiedInterval Bounds,
    EmlProcessRemainderCorroboration RemainderCorroboration);

public static class EmlProcessConstants
{
    public const int AlgorithmVersion = 1;

    public static EmlProcessConstantState CreateCatalanState()
        => new(EmlProcessConstantAlgorithms.CatalanAlternating, AlgorithmVersion, 0, 0, EmlExactRational.Zero);

    public static EmlProcessConstantState CreateZeta3State()
        => new(EmlProcessConstantAlgorithms.Zeta3IntegralTail, AlgorithmVersion, 0, 0, EmlExactRational.Zero);

    public static EmlProcessConstantState Advance(in EmlProcessConstantState state, long fuel)
    {
        if (fuel < 0) throw new ArgumentOutOfRangeException(nameof(fuel), fuel, "process fuel cannot be negative");
        ValidateStateShape(in state);

        EmlExactRational sum = state.PartialSum;
        long terms = state.Terms;
        for (long spent = 0; spent < fuel; spent++)
        {
            sum += state.Algorithm switch
            {
                EmlProcessConstantAlgorithms.CatalanAlternating => CreateCatalanTerm(terms),
                EmlProcessConstantAlgorithms.Zeta3IntegralTail => CreateZeta3Term(checked(terms + 1)),
                _ => throw new ArgumentOutOfRangeException(nameof(state), state.Algorithm, "unknown process-constant algorithm"),
            };
            terms = checked(terms + 1);
        }

        return state with { Terms = terms, FuelSpent = checked(state.FuelSpent + fuel), PartialSum = sum };
    }

    public static EmlProcessConstantCertificate Certify(in EmlProcessConstantState state)
    {
        ValidateState(in state);
        EmlProcessRemainderCorroboration witness = CreateCorroboration(in state);
        EmlCertifiedInterval bounds = new(
            state.PartialSum + witness.LowerOffset,
            state.PartialSum + witness.UpperOffset);
        return new EmlProcessConstantCertificate(
            state.Algorithm,
            state.Version,
            state.Terms,
            state.FuelSpent,
            EmlProcessConstantEncoding.ComputeStateDigest(in state),
            bounds,
            witness);
    }

    internal static string GetAlgorithmToken(EmlProcessConstantAlgorithms algorithm) => algorithm switch
    {
        EmlProcessConstantAlgorithms.CatalanAlternating => "catalan-alternating",
        EmlProcessConstantAlgorithms.Zeta3IntegralTail => "zeta3-integral-tail",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "unknown process-constant algorithm"),
    };

    private static void ValidateState(in EmlProcessConstantState state)
    {
        ValidateStateShape(in state);
        EmlExactRational expected = EmlExactRational.Zero;
        for (long term = 0; term < state.Terms; term++)
            expected += state.Algorithm switch
            {
                EmlProcessConstantAlgorithms.CatalanAlternating => CreateCatalanTerm(term),
                EmlProcessConstantAlgorithms.Zeta3IntegralTail => CreateZeta3Term(checked(term + 1)),
                _ => throw new InvalidDataException($"unknown process-constant algorithm {(int)state.Algorithm}"),
            };
        if (state.PartialSum != expected) throw new InvalidDataException("process-constant state partial sum is not exact");
    }

    private static void ValidateStateShape(in EmlProcessConstantState state)
    {
        if (state.Version != AlgorithmVersion)
            throw new InvalidDataException($"unsupported process-constant algorithm version {state.Version}");
        if (state.Terms < 0 || state.FuelSpent < 0 || state.Terms != state.FuelSpent)
            throw new InvalidDataException("process-constant state terms and fuel must be equal and non-negative");
    }

    private static EmlProcessRemainderCorroboration CreateCorroboration(in EmlProcessConstantState state)
    {
        if (state.Algorithm == EmlProcessConstantAlgorithms.CatalanAlternating)
        {
            EmlExactRational next = CreateCatalanTerm(state.Terms);
            return next.Numerator.Sign > 0
                ? new("alternating-next-term", EmlExactRational.Zero, next)
                : new("alternating-next-term", next, EmlExactRational.Zero);
        }

        if (state.Algorithm == EmlProcessConstantAlgorithms.Zeta3IntegralTail)
        {
            if (state.Terms == 0) throw new InvalidOperationException("zeta(3) integral-tail certification requires at least one term");
            BigInteger n = state.Terms;
            EmlExactRational lower = new(BigInteger.One, 2 * BigInteger.Pow(n + 1, 2));
            EmlExactRational upper = new(BigInteger.One, 2 * BigInteger.Pow(n, 2));
            return new("decreasing-integral-tail", lower, upper);
        }

        throw new InvalidDataException($"unknown process-constant algorithm {(int)state.Algorithm}");
    }

    private static EmlExactRational CreateCatalanTerm(long index)
    {
        BigInteger odd = 2 * (BigInteger)index + 1;
        BigInteger numerator = (index & 1) == 0 ? BigInteger.One : BigInteger.MinusOne;
        return new EmlExactRational(numerator, odd * odd);
    }

    private static EmlExactRational CreateZeta3Term(long denominator)
    {
        BigInteger n = denominator;
        return new EmlExactRational(BigInteger.One, n * n * n);
    }
}

internal static class EmlProcessConstantEncoding
{
    internal static string ComputeStateDigest(in EmlProcessConstantState state)
    {
        string payload = string.Join('\n',
            "eml-process-constant-state",
            EmlProcessConstants.GetAlgorithmToken(state.Algorithm),
            state.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.Terms.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.FuelSpent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.PartialSum.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.PartialSum.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
