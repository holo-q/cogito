namespace Cogito;

using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

public enum EmlProcessFunctionAlgorithms
{
    NegativeLogSeries = 1,
    LogRatioSeries = 2,
    ExponentialSeries = 3,
}

public enum EmlProcessInputSlots
{
    X = 1,
    Y = 2,
}

public readonly record struct EmlProcessFunction(
    EmlProcessFunctionAlgorithms Algorithm,
    int Version,
    string NumeratorRPN,
    string DenominatorRPN,
    long Fuel);

public readonly record struct EmlProcessFunctionProbeCertificate(
    Complex Value,
    EmlRect Enclosure,
    double RhoUpper,
    double RemainderRadius,
    int PrincipalBranchTurn,
    long FuelSpent,
    string ExactStateDigest = "");

public readonly record struct EmlProcessFunctionCertificate(
    EmlProcessFunction Descriptor,
    string Digest,
    EmlProcessFunctionProbeCertificate P1,
    EmlProcessFunctionProbeCertificate P2,
    EmlProcessFunctionProbeCertificate P3);

public readonly record struct EmlProcessFunctionCheck(bool Accepted, string Detail);

/// Exact resumable state for the exponential tail
/// `exp(u) - 1 - u = sum(n=2..infinity, u^n/n!)`. The first omitted term after
/// `fuel` terms is `u^(fuel+2)/(fuel+2)!`; the ratio bound is
/// `rho = |u|/(fuel+3) < 1`, so the remaining tail is bounded by
/// `u^(fuel+2)/(fuel+2)! / (1-rho)`. Real and imaginary parts are Gaussian
/// rationals because every finite `double` probe is a binary rational; the
/// ratio proof compares exact `|u|²` and the persisted partial sum is
/// independent of floating-point rounding.
/// Exact Gaussian rational `(real + i*imaginary)` used by the exponential
/// residual state. Its `AbsSquared` is an exact nonnegative rational, so the
/// ratio majorant never compares a rounded magnitude.
public readonly record struct EmlGaussianRational(
    EmlExactRational Real,
    EmlExactRational Imaginary)
{
    public static EmlGaussianRational Zero => new(EmlExactRational.Zero, EmlExactRational.Zero);
    public EmlExactRational AbsSquared => Real * Real + Imaginary * Imaginary;

    public static EmlGaussianRational operator +(EmlGaussianRational left, EmlGaussianRational right)
        => new(left.Real + right.Real, left.Imaginary + right.Imaginary);

    public static EmlGaussianRational operator -(EmlGaussianRational left, EmlGaussianRational right)
        => new(left.Real - right.Real, left.Imaginary - right.Imaginary);

    public static EmlGaussianRational operator *(EmlGaussianRational left, EmlGaussianRational right)
        => new(
            left.Real * right.Real - left.Imaginary * right.Imaginary,
            left.Real * right.Imaginary + left.Imaginary * right.Real);

    public static EmlGaussianRational operator /(EmlGaussianRational value, EmlExactRational scalar)
        => new(value.Real / scalar, value.Imaginary / scalar);
}

public readonly record struct EmlExpSeriesState(
    EmlGaussianRational Argument,
    long Terms,
    long FuelSpent,
    EmlGaussianRational PartialSum)
{
    public static EmlExpSeriesState Create(EmlGaussianRational argument)
        => new(argument, 0, 0, EmlGaussianRational.Zero);

    public EmlExpSeriesState Advance(long fuel)
    {
        if (fuel < 0) throw new ArgumentOutOfRangeException(nameof(fuel), fuel, "process fuel cannot be negative");
        Validate();
        EmlGaussianRational sum = PartialSum;
        long terms = Terms;
        for (long spent = 0; spent < fuel; spent++)
        {
            long power = checked(terms + 2);
            sum += Power(Argument, power) / new EmlExactRational(Factorial(power), BigInteger.One);
            terms = checked(terms + 1);
        }
        return this with { Terms = terms, FuelSpent = checked(FuelSpent + fuel), PartialSum = sum };
    }

    public string Digest()
    {
        Validate();
        return EmlExpSeriesEncoding.ComputeStateDigest(in this);
    }

    public bool IsValid()
    {
        try
        {
            Validate();
            return true;
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    private void Validate()
    {
        if (Terms < 0 || FuelSpent != Terms)
            throw new InvalidDataException("exponential-series terms and fuel must be equal and non-negative");
        EmlGaussianRational expected = EmlGaussianRational.Zero;
        for (long term = 0; term < Terms; term++)
        {
            long power = checked(term + 2);
            expected += Power(Argument, power) / new EmlExactRational(Factorial(power), BigInteger.One);
        }
        if (expected != PartialSum)
            throw new InvalidDataException("exponential-series partial sum is not exact");
    }

    private static BigInteger Factorial(long value)
    {
        BigInteger result = BigInteger.One;
        for (long i = 2; i <= value; i++) result *= i;
        return result;
    }

    private static EmlGaussianRational Power(EmlGaussianRational value, long power)
    {
        EmlGaussianRational result = new(EmlExactRational.One, EmlExactRational.Zero);
        for (long i = 0; i < power; i++) result *= value;
        return result;
    }
}

public static class EmlProcessFunctions
{
    public const int AlgorithmVersion = 3;

    public static EmlProcessFunction CreateNegativeLog(EmlProcessInputSlots input, long fuel)
    {
        string denominator = input switch
        {
            EmlProcessInputSlots.X => Eml.VarX.ToString(),
            EmlProcessInputSlots.Y => Eml.VarY.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "unknown process-function input slot"),
        };
        return CreateLogRatio(
            EmlProcessFunctionAlgorithms.NegativeLogSeries,
            Eml.One.ToString(),
            denominator,
            fuel);
    }

    public static EmlProcessFunction CreateLogRatio(string numeratorRPN, string denominatorRPN, long fuel)
        => CreateLogRatio(EmlProcessFunctionAlgorithms.LogRatioSeries, numeratorRPN, denominatorRPN, fuel);

    /// Creates the process leaf for the nonlinear residual left after removing
    /// the affine base of exp: `exp(u) - 1 - u`.  This is intentionally a
    /// process species rather than an RPN token so its fuel, enclosure theorem,
    /// and source binding remain visible to admission.
    public static EmlProcessFunction CreateExpSeries(string argumentRPN, long fuel)
    {
        ValidateProgram(argumentRPN, nameof(argumentRPN));
        ValidateFuel(fuel);
        return new EmlProcessFunction(
            EmlProcessFunctionAlgorithms.ExponentialSeries,
            AlgorithmVersion,
            argumentRPN,
            Eml.One.ToString(),
            fuel);
    }

    private static EmlProcessFunction CreateLogRatio(
        EmlProcessFunctionAlgorithms algorithm,
        string numeratorRPN,
        string denominatorRPN,
        long fuel)
    {
        ValidateProgram(numeratorRPN, nameof(numeratorRPN));
        ValidateProgram(denominatorRPN, nameof(denominatorRPN));
        ValidateFuel(fuel);
        return new EmlProcessFunction(algorithm, AlgorithmVersion, numeratorRPN, denominatorRPN, fuel);
    }

    public static EmlProcessFunctionCertificate Certify(in EmlProcessFunction descriptor)
        => Certify(in descriptor, null);

    public static EmlProcessFunctionCertificate Certify(in EmlProcessFunction descriptor, EmlDeliberationLease? deliberationLease)
    {
        ValidateDescriptor(in descriptor);
        EmlProcessFunctionProbeCertificate p1 = EvaluateProbe(in descriptor, EmlTree.P1, deliberationLease);
        EmlProcessFunctionProbeCertificate p2 = EvaluateProbe(in descriptor, EmlTree.P2, deliberationLease);
        EmlProcessFunctionProbeCertificate p3 = EvaluateProbe(in descriptor, EmlTree.P3, deliberationLease);
        string digest = EmlProcessFunctionEncoding.ComputeDigest(in descriptor, in p1, in p2, in p3);
        return new EmlProcessFunctionCertificate(descriptor, digest, p1, p2, p3);
    }

    internal static void ValidateDescriptor(in EmlProcessFunction descriptor)
    {
        if (descriptor.Algorithm is not EmlProcessFunctionAlgorithms.NegativeLogSeries
            and not EmlProcessFunctionAlgorithms.LogRatioSeries
            and not EmlProcessFunctionAlgorithms.ExponentialSeries)
            throw new InvalidDataException($"unknown process-function algorithm {(int)descriptor.Algorithm}");
        if (descriptor.Version != AlgorithmVersion)
            throw new InvalidDataException($"unsupported process-function algorithm version {descriptor.Version}");
        ValidateProgram(descriptor.NumeratorRPN, nameof(descriptor.NumeratorRPN));
        ValidateProgram(descriptor.DenominatorRPN, nameof(descriptor.DenominatorRPN));
        if (descriptor.Algorithm == EmlProcessFunctionAlgorithms.NegativeLogSeries
            && !string.Equals(descriptor.NumeratorRPN, Eml.One.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("negative-log process numerator must be the EML unit");
        if (descriptor.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries
            && !string.Equals(descriptor.DenominatorRPN, Eml.One.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("exponential-series process denominator must be the EML unit");
        ValidateFuel(descriptor.Fuel);
    }

    internal static EmlProcessFunctionProbeCertificate EvaluateNegativeLog(Complex input, long fuel)
    {
        EmlLadder numerator = CreatePointLadder(Complex.One);
        EmlLadder denominator = CreatePointLadder(input);
        return EvaluateLogRatio(in numerator, in denominator, fuel);
    }

    private static EmlProcessFunctionProbeCertificate EvaluateLogRatio(
        in EmlLadder numerator,
        in EmlLadder denominator,
        long fuel)
    {
        ValidateRealRatioOperands(in numerator, in denominator);
        double plainT = (numerator.Plain.Value.Real - denominator.Plain.Value.Real)
            / (numerator.Plain.Value.Real + denominator.Plain.Value.Real);
        double plainPower = plainT;
        double plainSum = 0.0;

        EmlIv ratioNumerator = EmlIv.Sub(numerator.Rect.Re, denominator.Rect.Re);
        EmlIv ratioDenominator = EmlIv.Add(numerator.Rect.Re, denominator.Rect.Re);
        EmlIv t = DivideNonZero(in ratioNumerator, in ratioDenominator);
        double rho = t.AbsMax;
        if (!double.IsFinite(rho) || rho >= 1.0)
            throw new InvalidDataException("real log-ratio atanh transform does not prove rho < 1");
        EmlIv tSquared = EmlIv.Mul(t, t);
        EmlIv power = t;
        EmlIv sum = EmlIv.Point(0.0);
        long odd = 1;

        for (long term = 0; term < fuel; term++)
        {
            plainSum += plainPower / odd;
            EmlIv reciprocalOdd = CreatePositiveReciprocal(odd);
            sum = EmlIv.Add(sum, EmlIv.Mul(power, reciprocalOdd));
            plainPower *= plainT * plainT;
            power = EmlIv.Mul(power, tSquared);
            odd = checked(odd + 2);
        }

        EmlIv scaledSum = EmlIv.Mul(EmlIv.Point(2.0), sum);
        double remainder = ComputeRemainderUpper(in power, odd, rho);
        EmlIv real = EmlIv.Add(scaledSum, new EmlIv(-remainder, remainder));
        Complex value = new Complex(2.0 * plainSum, 0.0);
        EmlRect enclosure = new EmlRect(real, EmlIv.Point(0.0));
        return new EmlProcessFunctionProbeCertificate(value, enclosure, rho, remainder, 0, fuel);
    }

    private static EmlProcessFunctionProbeCertificate EvaluateExpSeries(
        in EmlLadder argument,
        long fuel)
    {
        ValidateExpArgument(in argument);
        EmlGaussianRational argumentExact = new(
            ToExactRational(argument.Plain.Value.Real),
            ToExactRational(argument.Plain.Value.Imaginary));
        EmlExpSeriesState state = EmlExpSeriesState.Create(argumentExact).Advance(fuel);
        long omittedPower = checked(fuel + 2);
        EmlExactRational ratioDenominator = new EmlExactRational(checked(fuel + 3), BigInteger.One);
        EmlExactRational rhoSquared = argumentExact.AbsSquared
            / (ratioDenominator * ratioDenominator);
        if (rhoSquared < EmlExactRational.Zero || rhoSquared >= EmlExactRational.One)
            throw new InvalidDataException("exponential-series ratio majorant does not prove rho < 1");
        EmlGaussianRational omittedTerm = Power(argumentExact, omittedPower)
            / new EmlExactRational(Factorial(omittedPower), BigInteger.One);
        double rho = ToDoubleUpperSqrt(rhoSquared);
        double omittedMagnitude = ToDoubleUpperSqrt(omittedTerm.AbsSquared);
        double denominatorLower = Math.BitDecrement(1.0 - rho);
        double tail = DirectedUp(omittedMagnitude / denominatorLower);
        double valueReal = ToNearestDouble(state.PartialSum.Real);
        double valueImaginary = ToNearestDouble(state.PartialSum.Imaginary);
        double roundingReal = ToDoubleUpper(Absolute(ToExactRational(valueReal) - state.PartialSum.Real));
        double roundingImaginary = ToDoubleUpper(Absolute(ToExactRational(valueImaginary) - state.PartialSum.Imaginary));
        double remainder = DirectedUp(tail + Math.Max(roundingReal, roundingImaginary));
        EmlIv real = new(EmlIv.Dn(valueReal - remainder), EmlIv.Up(valueReal + remainder));
        EmlIv imaginary = new(EmlIv.Dn(valueImaginary - remainder), EmlIv.Up(valueImaginary + remainder));
        Complex value = new(valueReal, valueImaginary);
        EmlRect enclosure = new(real, imaginary);
        return new EmlProcessFunctionProbeCertificate(value, enclosure, rho, remainder, 0, fuel, state.Digest());
    }

    internal static EmlProcessFunctionProbeCertificate EvaluateProbe(
        in EmlProcessFunction descriptor,
        EmlProbePoint point,
        EmlDeliberationLease? deliberationLease = null)
    {
        long operandPoints = descriptor.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries ? 1 : 2;
        deliberationLease?.ReserveLogicalProgramPoints(operandPoints);
        deliberationLease?.ReserveExecutedProgramPoints(operandPoints);
        deliberationLease?.ReserveProcessTerms(descriptor.Fuel);
        EmlLadder numerator = Eml.EvalLadder(descriptor.NumeratorRPN, point.X, point.Y);
        if (descriptor.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries)
            return EvaluateExpSeries(in numerator, descriptor.Fuel);
        EmlLadder denominator = Eml.EvalLadder(descriptor.DenominatorRPN, point.X, point.Y);
        return EvaluateLogRatio(in numerator, in denominator, descriptor.Fuel);
    }

    public static bool ValidateMonotoneLift(
        in EmlProcessFunctionCertificate earlier,
        in EmlProcessFunctionCertificate later)
    {
        EmlProcessFunctionCheck first = EmlProcessFunctionChecker.Check(in earlier);
        EmlProcessFunctionCheck second = EmlProcessFunctionChecker.Check(in later);
        if (!first.Accepted || !second.Accepted
            || earlier.Descriptor.Algorithm != EmlProcessFunctionAlgorithms.ExponentialSeries
            || later.Descriptor.Algorithm != EmlProcessFunctionAlgorithms.ExponentialSeries
            || earlier.Descriptor.NumeratorRPN != later.Descriptor.NumeratorRPN
            || earlier.Descriptor.DenominatorRPN != later.Descriptor.DenominatorRPN
            || later.Descriptor.Fuel <= earlier.Descriptor.Fuel)
            return false;
        bool p1 = Tightens(earlier.P1.Enclosure, later.P1.Enclosure);
        bool p2 = Tightens(earlier.P2.Enclosure, later.P2.Enclosure);
        bool p3 = Tightens(earlier.P3.Enclosure, later.P3.Enclosure);
        bool strict = StrictlyTightens(earlier.P1.Enclosure, later.P1.Enclosure)
            || StrictlyTightens(earlier.P2.Enclosure, later.P2.Enclosure)
            || StrictlyTightens(earlier.P3.Enclosure, later.P3.Enclosure)
            || later.P1.RemainderRadius < earlier.P1.RemainderRadius
            || later.P2.RemainderRadius < earlier.P2.RemainderRadius
            || later.P3.RemainderRadius < earlier.P3.RemainderRadius;
        return p1 && p2 && p3
            && later.P1.RemainderRadius <= earlier.P1.RemainderRadius
            && later.P2.RemainderRadius <= earlier.P2.RemainderRadius
            && later.P3.RemainderRadius <= earlier.P3.RemainderRadius
            && strict;
    }

    private static bool Tightens(EmlRect outer, EmlRect inner)
        => !outer.IsBlown && !inner.IsBlown
            && outer.Re.Contains(inner.Re.Lo) && outer.Re.Contains(inner.Re.Hi)
            && outer.Im.Contains(inner.Im.Lo) && outer.Im.Contains(inner.Im.Hi)
            && inner.Re.Hi - inner.Re.Lo <= outer.Re.Hi - outer.Re.Lo
            && inner.Im.Hi - inner.Im.Lo <= outer.Im.Hi - outer.Im.Lo;

    private static bool StrictlyTightens(EmlRect outer, EmlRect inner)
        => inner.Re.Hi - inner.Re.Lo < outer.Re.Hi - outer.Re.Lo
            || inner.Im.Hi - inner.Im.Lo < outer.Im.Hi - outer.Im.Lo;

    private static void ValidateExpArgument(in EmlLadder argument)
    {
        if (!argument.Plain.Finite)
            throw new InvalidDataException("exponential-series argument must be a finite exact Gaussian-rational point");
    }

    private static BigInteger Factorial(long value)
    {
        BigInteger result = BigInteger.One;
        for (long i = 2; i <= value; i++) result *= i;
        return result;
    }

    private static EmlGaussianRational Power(EmlGaussianRational value, long power)
    {
        EmlGaussianRational result = new(EmlExactRational.One, EmlExactRational.Zero);
        for (long i = 0; i < power; i++) result *= value;
        return result;
    }

    private static EmlExactRational Absolute(EmlExactRational value)
        => value < EmlExactRational.Zero ? -value : value;

    private static double ToNearestDouble(EmlExactRational value)
        => (double)value.Numerator / (double)value.Denominator;

    private static double DirectedUp(double value)
    {
        if (double.IsNaN(value)) throw new InvalidDataException("exponential-series bound is not representable");
        if (double.IsPositiveInfinity(value)) return value;
        return EmlIv.Up(value);
    }

    private static double ToDoubleUpperSqrt(EmlExactRational squared)
    {
        if (squared < EmlExactRational.Zero) throw new ArgumentOutOfRangeException(nameof(squared));
        if (squared == EmlExactRational.Zero) return 0.0;
        // Positive finite binary64 values are totally ordered by their bit pattern. Binary-search that
        // finite ordering against the exact rational square; this terminates in at most 63 comparisons and
        // returns the least representable outward root (including the subnormal boundary).
        const ulong maxFiniteBits = 0x7FEFFFFFFFFFFFFFUL;
        double maxFinite = double.MaxValue;
        EmlExactRational maxFiniteExact = ToExactRational(maxFinite);
        if (maxFiniteExact * maxFiniteExact < squared) return double.PositiveInfinity;
        ulong low = 0;
        ulong high = maxFiniteBits;
        while (low < high)
        {
            ulong middle = low + ((high - low) >> 1);
            double candidate = BitConverter.UInt64BitsToDouble(middle);
            EmlExactRational exactCandidate = ToExactRational(candidate);
            if (exactCandidate * exactCandidate >= squared) high = middle;
            else low = middle + 1;
        }
        return BitConverter.UInt64BitsToDouble(low);
    }

    private static double ToDoubleUpper(EmlExactRational value)
    {
        if (value < EmlExactRational.Zero) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == EmlExactRational.Zero) return 0.0;

        // Round the exact ratio outward before converting it to binary64. The
        // quotient is then within a handful of ulps, so the exact comparison
        // below is bounded rather than an unbounded walk from zero.
        int numeratorShift = checked((int)Math.Max(0, value.Numerator.GetBitLength() - 53));
        int denominatorShift = checked((int)Math.Max(0, value.Denominator.GetBitLength() - 53));
        BigInteger numeratorTop = value.Numerator >> numeratorShift;
        if (numeratorShift > 0 && (value.Numerator & ((BigInteger.One << numeratorShift) - 1)) != 0)
            numeratorTop++;
        BigInteger denominatorTop = value.Denominator >> denominatorShift;
        double candidate = Math.ScaleB(
            (double)numeratorTop / (double)denominatorTop,
            numeratorShift - denominatorShift);
        if (double.IsNaN(candidate)) throw new InvalidDataException("exact exponential bound is not representable");
        if (double.IsPositiveInfinity(candidate)) return candidate;
        if (candidate == 0.0)
        {
            candidate = double.Epsilon;
            for (int step = 0; step < 1074 && ToExactRational(candidate) < value; step++)
                candidate = Math.ScaleB(candidate, 1);
            if (ToExactRational(candidate) < value)
                throw new InvalidDataException("exact exponential bound underflowed binary64 range");
            return candidate;
        }
        for (int step = 0; step < 8 && ToExactRational(candidate) < value; step++)
            candidate = Math.BitIncrement(candidate);
        if (ToExactRational(candidate) < value)
            throw new InvalidDataException("exact exponential bound could not be rounded upward");
        return candidate;
    }

    private static EmlExactRational ToExactRational(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("exact exponential-series argument must be finite");
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = (bits & long.MinValue) != 0;
        int exponent = (int)((bits >> 52) & 0x7ffL);
        ulong fraction = (ulong)bits & 0x000f_ffffffffffffUL;
        BigInteger numerator;
        int shift;
        if (exponent == 0)
        {
            numerator = new BigInteger(fraction);
            shift = -1074;
        }
        else
        {
            numerator = new BigInteger(fraction | (1UL << 52));
            shift = exponent - 1023 - 52;
        }
        if (negative) numerator = BigInteger.Negate(numerator);
        if (shift >= 0) return new EmlExactRational(numerator << shift, BigInteger.One);
        return new EmlExactRational(numerator, BigInteger.One << -shift);
    }

    private static EmlIv DivideNonZero(in EmlIv numerator, in EmlIv denominator)
    {
        if (denominator.IsBlown || denominator.Lo <= 0.0 && denominator.Hi >= 0.0)
            throw new InvalidDataException("log-ratio transform denominator contains zero");
        EmlIv reciprocal = new EmlIv(EmlIv.Dn(1.0 / denominator.Hi), EmlIv.Up(1.0 / denominator.Lo));
        return EmlIv.Mul(numerator, reciprocal);
    }

    private static EmlIv CreatePositiveReciprocal(long denominator)
    {
        double reciprocal = 1.0 / denominator;
        return new EmlIv(EmlIv.Dn(reciprocal), EmlIv.Up(reciprocal));
    }

    private static double ComputeRemainderUpper(in EmlIv nextPower, long nextOdd, double rho)
    {
        EmlIv rhoInterval = new EmlIv(0.0, rho);
        EmlIv rhoSquared = EmlIv.Mul(rhoInterval, rhoInterval);
        EmlIv geometricDenominator = EmlIv.Sub(EmlIv.Point(1.0), rhoSquared);
        EmlIv fullDenominator = EmlIv.Mul(EmlIv.Point(nextOdd), geometricDenominator);
        EmlIv numerator = EmlIv.Mul(EmlIv.Point(2.0), new EmlIv(0.0, nextPower.AbsMax));
        if (fullDenominator.IsBlown || fullDenominator.Lo <= 0.0)
            throw new InvalidDataException("log-ratio atanh remainder denominator is not strictly positive");
        return EmlIv.Up(numerator.Hi / fullDenominator.Lo);
    }

    private static EmlLadder CreatePointLadder(Complex value)
    {
        if (!double.IsFinite(value.Real) || value.Imaginary != 0.0)
            throw new InvalidDataException("log-ratio process operand must be finite and real");
        return new EmlLadder(new EmlValue(value, true), new EmlRect(EmlIv.Point(value.Real), EmlIv.Point(0.0)), 1.0, 0);
    }

    private static void ValidateRealRatioOperands(in EmlLadder numerator, in EmlLadder denominator)
    {
        bool positiveIntervals = numerator.Rect.Re.Lo > 0.0 && denominator.Rect.Re.Lo > 0.0;
        bool negativeIntervals = numerator.Rect.Re.Hi < 0.0 && denominator.Rect.Re.Hi < 0.0;
        bool positiveValues = numerator.Plain.Value.Real > 0.0 && denominator.Plain.Value.Real > 0.0;
        bool negativeValues = numerator.Plain.Value.Real < 0.0 && denominator.Plain.Value.Real < 0.0;
        if (!numerator.Plain.Finite || !denominator.Plain.Finite
            || numerator.Rect.IsBlown || denominator.Rect.IsBlown
            || numerator.Plain.Value.Imaginary != 0.0 || denominator.Plain.Value.Imaginary != 0.0
            || numerator.Rect.Im.Lo > 0.0 || numerator.Rect.Im.Hi < 0.0
            || denominator.Rect.Im.Lo > 0.0 || denominator.Rect.Im.Hi < 0.0
            || !(positiveIntervals && positiveValues || negativeIntervals && negativeValues))
            throw new InvalidDataException("log-ratio process operands must be finite nonzero reals with the same sign");

        Complex canonicalNumerator = new Complex(numerator.Plain.Value.Real, 0.0);
        Complex canonicalDenominator = new Complex(denominator.Plain.Value.Real, 0.0);
        Complex principalDifference = Complex.Log(canonicalNumerator) - Complex.Log(canonicalDenominator);
        if (!double.IsFinite(principalDifference.Real) || principalDifference.Imaginary != 0.0)
            throw new InvalidDataException("real log-ratio operands do not cancel their principal branch arguments");
    }

    private static void ValidateProgram(string rpn, string parameter)
    {
        if (string.IsNullOrEmpty(rpn) || !EmlTree.TryParseRPN(rpn, out _))
            throw new ArgumentException("process-function operand must be a valid finite EML program", parameter);
    }

    private static void ValidateFuel(long fuel)
    {
        if (fuel <= 0 || fuel > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(fuel), fuel, "process fuel must be between one and Int32.MaxValue");
    }
}

public static class EmlProcessFunctionChecker
{
    public static EmlProcessFunctionCheck Check(in EmlProcessFunctionCertificate certificate)
        => Check(in certificate, null);

    public static EmlProcessFunctionCheck Check(in EmlProcessFunctionCertificate certificate, EmlDeliberationLease? deliberationLease)
    {
        try
        {
            EmlProcessFunction descriptor = certificate.Descriptor;
            ValidateDescriptor(in descriptor);
            EmlProcessFunctionProbeCertificate p1 = EmlProcessFunctions.EvaluateProbe(in descriptor, EmlTree.P1, deliberationLease);
            EmlProcessFunctionProbeCertificate p2 = EmlProcessFunctions.EvaluateProbe(in descriptor, EmlTree.P2, deliberationLease);
            EmlProcessFunctionProbeCertificate p3 = EmlProcessFunctions.EvaluateProbe(in descriptor, EmlTree.P3, deliberationLease);

            if (certificate.P1 != p1 || certificate.P2 != p2 || certificate.P3 != p3)
                return Reject("probe certificate does not match the reconstructed process");

            string digest = EmlProcessFunctionEncoding.ComputeDigest(in descriptor, in p1, in p2, in p3);
            if (!string.Equals(certificate.Digest, digest, StringComparison.Ordinal))
                return Reject("digest does not bind the reconstructed descriptor and probes");

            return new EmlProcessFunctionCheck(true, "accepted");
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            return Reject(error.Message);
        }
    }

    private static void ValidateDescriptor(in EmlProcessFunction descriptor)
        => EmlProcessFunctions.ValidateDescriptor(in descriptor);

    private static EmlProcessFunctionCheck Reject(string detail)
        => new EmlProcessFunctionCheck(false, detail);
}

internal static class EmlProcessFunctionEncoding
{
    private const int ProbeLength = 76;

    internal static string ComputeDigest(
        in EmlProcessFunction descriptor,
        in EmlProcessFunctionProbeCertificate p1,
        in EmlProcessFunctionProbeCertificate p2,
        in EmlProcessFunctionProbeCertificate p3)
    {
        if (descriptor.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries)
            return ComputeExpDigest(in descriptor, in p1, in p2, in p3);
        byte[] numerator = System.Text.Encoding.ASCII.GetBytes(descriptor.NumeratorRPN);
        byte[] denominator = System.Text.Encoding.ASCII.GetBytes(descriptor.DenominatorRPN);
        const int DomainLength = 8;
        int headerLength = checked(DomainLength + 24 + numerator.Length + denominator.Length);
        byte[] payload = new byte[checked(headerLength + 3 * ProbeLength)];
        Span<byte> bytes = payload;
        ReadOnlySpan<byte> domain = "EMLPFN03"u8;
        domain.CopyTo(bytes);
        int offset = DomainLength;
        WriteInt32(bytes, ref offset, (int)descriptor.Algorithm);
        WriteInt32(bytes, ref offset, descriptor.Version);
        WriteInt32(bytes, ref offset, numerator.Length);
        numerator.CopyTo(bytes[offset..]);
        offset += numerator.Length;
        WriteInt32(bytes, ref offset, denominator.Length);
        denominator.CopyTo(bytes[offset..]);
        offset += denominator.Length;
        WriteInt64(bytes, ref offset, descriptor.Fuel);
        WriteProbe(bytes, ref offset, in p1);
        WriteProbe(bytes, ref offset, in p2);
        WriteProbe(bytes, ref offset, in p3);
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static string ComputeExpDigest(
        in EmlProcessFunction descriptor,
        in EmlProcessFunctionProbeCertificate p1,
        in EmlProcessFunctionProbeCertificate p2,
        in EmlProcessFunctionProbeCertificate p3)
    {
        byte[] numerator = System.Text.Encoding.ASCII.GetBytes(descriptor.NumeratorRPN);
        byte[] denominator = System.Text.Encoding.ASCII.GetBytes(descriptor.DenominatorRPN);
        const int DomainLength = 8;
        const int ProbeLength = 76;
        const int StateDigestLength = 64;
        int headerLength = checked(DomainLength + 24 + numerator.Length + denominator.Length);
        byte[] payload = new byte[checked(headerLength + 3 * (ProbeLength + StateDigestLength))];
        Span<byte> bytes = payload;
        "EMLPFN04"u8.CopyTo(bytes);
        int offset = DomainLength;
        WriteInt32(bytes, ref offset, (int)descriptor.Algorithm);
        WriteInt32(bytes, ref offset, descriptor.Version);
        WriteInt32(bytes, ref offset, numerator.Length);
        numerator.CopyTo(bytes[offset..]);
        offset += numerator.Length;
        WriteInt32(bytes, ref offset, denominator.Length);
        denominator.CopyTo(bytes[offset..]);
        offset += denominator.Length;
        WriteInt64(bytes, ref offset, descriptor.Fuel);
        WriteProbe(bytes, ref offset, in p1);
        WriteProbe(bytes, ref offset, in p2);
        WriteProbe(bytes, ref offset, in p3);
        WriteStateDigest(bytes, ref offset, p1.ExactStateDigest);
        WriteStateDigest(bytes, ref offset, p2.ExactStateDigest);
        WriteStateDigest(bytes, ref offset, p3.ExactStateDigest);
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    internal static bool MatchesLegacyDigest(
        string digest,
        int version,
        in EmlProcessFunction descriptor,
        in EmlProcessFunctionCertificate certificate)
    {
        if (version is not 1 and not 2 || descriptor.Algorithm is not EmlProcessFunctionAlgorithms.NegativeLogSeries
            and not EmlProcessFunctionAlgorithms.LogRatioSeries)
            return false;
        byte[] payload;
        Span<byte> bytes;
        if (version == 1)
        {
            EmlProcessInputSlots input = descriptor.DenominatorRPN switch
            {
                "x" => EmlProcessInputSlots.X,
                "y" => EmlProcessInputSlots.Y,
                _ => (EmlProcessInputSlots)0,
            };
            if (descriptor.Algorithm != EmlProcessFunctionAlgorithms.NegativeLogSeries
                || descriptor.NumeratorRPN != Eml.One.ToString()
                || input is not EmlProcessInputSlots.X and not EmlProcessInputSlots.Y)
                return false;
            payload = new byte[8 + 20 + 3 * 56];
            bytes = payload;
            "EMLPFN01"u8.CopyTo(bytes);
            int offset = 8;
            WriteInt32(bytes, ref offset, (int)descriptor.Algorithm);
            WriteInt32(bytes, ref offset, version);
            WriteInt32(bytes, ref offset, (int)input);
            WriteInt64(bytes, ref offset, descriptor.Fuel);
            EmlProcessFunctionProbeCertificate p1 = certificate.P1;
            EmlProcessFunctionProbeCertificate p2 = certificate.P2;
            EmlProcessFunctionProbeCertificate p3 = certificate.P3;
            WriteLegacyProbe(bytes, ref offset, in p1);
            WriteLegacyProbe(bytes, ref offset, in p2);
            WriteLegacyProbe(bytes, ref offset, in p3);
        }
        else
        {
            int headerLength = 8 + 24 + descriptor.NumeratorRPN.Length + descriptor.DenominatorRPN.Length;
            payload = new byte[checked(headerLength + 3 * 56)];
            bytes = payload;
            "EMLPFN02"u8.CopyTo(bytes);
            int offset = 8;
            WriteInt32(bytes, ref offset, (int)descriptor.Algorithm);
            WriteInt32(bytes, ref offset, version);
            WriteInt32(bytes, ref offset, descriptor.NumeratorRPN.Length);
            System.Text.Encoding.ASCII.GetBytes(descriptor.NumeratorRPN, bytes[offset..]);
            offset += descriptor.NumeratorRPN.Length;
            WriteInt32(bytes, ref offset, descriptor.DenominatorRPN.Length);
            System.Text.Encoding.ASCII.GetBytes(descriptor.DenominatorRPN, bytes[offset..]);
            offset += descriptor.DenominatorRPN.Length;
            WriteInt64(bytes, ref offset, descriptor.Fuel);
            EmlProcessFunctionProbeCertificate p1 = certificate.P1;
            EmlProcessFunctionProbeCertificate p2 = certificate.P2;
            EmlProcessFunctionProbeCertificate p3 = certificate.P3;
            WriteLegacyProbe(bytes, ref offset, in p1);
            WriteLegacyProbe(bytes, ref offset, in p2);
            WriteLegacyProbe(bytes, ref offset, in p3);
        }
        return string.Equals(digest, Convert.ToHexStringLower(SHA256.HashData(payload)), StringComparison.Ordinal);
    }

    private static void WriteLegacyProbe(
        Span<byte> destination,
        ref int offset,
        in EmlProcessFunctionProbeCertificate probe)
    {
        WriteDouble(destination, ref offset, probe.Value.Real);
        WriteDouble(destination, ref offset, probe.Value.Imaginary);
        WriteDouble(destination, ref offset, probe.Enclosure.Re.Lo);
        WriteDouble(destination, ref offset, probe.Enclosure.Re.Hi);
        WriteDouble(destination, ref offset, probe.Enclosure.Im.Lo);
        WriteDouble(destination, ref offset, probe.Enclosure.Im.Hi);
        WriteInt64(destination, ref offset, probe.FuelSpent);
    }

    private static void WriteProbe(
        Span<byte> destination,
        ref int offset,
        in EmlProcessFunctionProbeCertificate probe)
    {
        WriteDouble(destination, ref offset, probe.Value.Real);
        WriteDouble(destination, ref offset, probe.Value.Imaginary);
        WriteDouble(destination, ref offset, probe.Enclosure.Re.Lo);
        WriteDouble(destination, ref offset, probe.Enclosure.Re.Hi);
        WriteDouble(destination, ref offset, probe.Enclosure.Im.Lo);
        WriteDouble(destination, ref offset, probe.Enclosure.Im.Hi);
        WriteDouble(destination, ref offset, probe.RhoUpper);
        WriteDouble(destination, ref offset, probe.RemainderRadius);
        WriteInt32(destination, ref offset, probe.PrincipalBranchTurn);
        WriteInt64(destination, ref offset, probe.FuelSpent);
    }

    private static void WriteStateDigest(Span<byte> destination, ref int offset, string digest)
    {
        if (digest.Length != 64) throw new InvalidDataException("exponential-series state digest must be SHA-256 hex");
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(digest);
        bytes.CopyTo(destination[offset..]);
        offset += bytes.Length;
    }

    private static void WriteDouble(Span<byte> destination, ref int offset, double value)
        => WriteInt64(destination, ref offset, BitConverter.DoubleToInt64Bits(value));

    private static void WriteInt32(Span<byte> destination, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination[offset..], value);
        offset += sizeof(int);
    }

    private static void WriteInt64(Span<byte> destination, ref int offset, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination[offset..], value);
        offset += sizeof(long);
    }
}

internal static class EmlExpSeriesEncoding
{
    internal static string ComputeStateDigest(in EmlExpSeriesState state)
    {
        string payload = string.Join('\n',
            "eml-exp-series-state",
            state.Argument.Real.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.Argument.Real.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.Argument.Imaginary.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.Argument.Imaginary.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.Terms.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.FuelSpent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.PartialSum.Real.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.PartialSum.Real.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.PartialSum.Imaginary.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            state.PartialSum.Imaginary.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "");
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }
}
