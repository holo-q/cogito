namespace Cogito;

using System.Numerics;

public readonly record struct EmlProcessConstantCheck(
    bool Accepted,
    string Detail,
    EmlCertifiedInterval ReconstructedBounds);

public static class EmlProcessConstantChecker
{
    public static EmlProcessConstantCheck Check(in EmlProcessConstantCertificate certificate)
    {
        try
        {
            if (certificate.Version != EmlProcessConstants.AlgorithmVersion)
                return Reject($"unsupported version {certificate.Version}");
            if (certificate.Terms < 0 || certificate.Fuel < 0 || certificate.Terms != certificate.Fuel)
                return Reject("terms and fuel are not the same non-negative work count");
            if (certificate.Algorithm == EmlProcessConstantAlgorithms.Zeta3IntegralTail && certificate.Terms == 0)
                return Reject("zeta(3) requires at least one summed term");

            EmlExactRational sum = ReconstructPartial(certificate.Algorithm, certificate.Terms);
            EmlProcessRemainderCorroboration witness = ReconstructCorroboration(certificate.Algorithm, certificate.Terms);
            EmlCertifiedInterval bounds = new(sum + witness.LowerOffset, sum + witness.UpperOffset);
            EmlProcessConstantState state = new(
                certificate.Algorithm,
                certificate.Version,
                certificate.Terms,
                certificate.Fuel,
                sum);
            string digest = EmlProcessConstantEncoding.ComputeStateDigest(in state);

            if (!string.Equals(certificate.StateDigest, digest, StringComparison.Ordinal))
                return Reject("state digest does not bind the reconstructed exact state", bounds);
            if (certificate.RemainderCorroboration != witness)
                return Reject("remainder witness does not match the algorithm theorem", bounds);
            if (certificate.Bounds != bounds)
                return Reject("claimed bounds do not match the reconstructed exact bounds", bounds);
            return new EmlProcessConstantCheck(true, "accepted", bounds);
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            return Reject(error.Message);
        }
    }

    public static EmlProcessConstantCheck ValidateMonotoneLift(
        in EmlProcessConstantCertificate earlier,
        in EmlProcessConstantCertificate later)
    {
        EmlProcessConstantCheck first = Check(in earlier);
        if (!first.Accepted) return Reject("earlier certificate rejected: " + first.Detail);
        EmlProcessConstantCheck second = Check(in later);
        if (!second.Accepted) return Reject("later certificate rejected: " + second.Detail);
        if (earlier.Algorithm != later.Algorithm || earlier.Version != later.Version)
            return Reject("lift changed algorithm identity");
        if (later.Terms <= earlier.Terms || later.Fuel <= earlier.Fuel)
            return Reject("lift did not spend positive fuel on additional terms");
        if (!first.ReconstructedBounds.Contains(second.ReconstructedBounds))
            return Reject("lifted bounds are not nested inside the earlier bounds", second.ReconstructedBounds);
        if (second.ReconstructedBounds.Width >= first.ReconstructedBounds.Width)
            return Reject("lifted bounds did not become strictly narrower", second.ReconstructedBounds);
        return new EmlProcessConstantCheck(true, "accepted", second.ReconstructedBounds);
    }

    private static EmlExactRational ReconstructPartial(EmlProcessConstantAlgorithms algorithm, long terms)
    {
        EmlExactRational sum = EmlExactRational.Zero;
        for (long index = 0; index < terms; index++)
        {
            BigInteger n;
            switch (algorithm)
            {
                case EmlProcessConstantAlgorithms.CatalanAlternating:
                    n = 2 * (BigInteger)index + 1;
                    sum += new EmlExactRational((index & 1) == 0 ? BigInteger.One : BigInteger.MinusOne, n * n);
                    break;
                case EmlProcessConstantAlgorithms.Zeta3IntegralTail:
                    n = index + 1;
                    sum += new EmlExactRational(BigInteger.One, n * n * n);
                    break;
                default:
                    throw new InvalidDataException($"unknown process-constant algorithm {(int)algorithm}");
            }
        }
        return sum;
    }

    private static EmlProcessRemainderCorroboration ReconstructCorroboration(EmlProcessConstantAlgorithms algorithm, long terms)
    {
        if (algorithm == EmlProcessConstantAlgorithms.CatalanAlternating)
        {
            BigInteger odd = 2 * (BigInteger)terms + 1;
            EmlExactRational next = new((terms & 1) == 0 ? BigInteger.One : BigInteger.MinusOne, odd * odd);
            return next.Numerator.Sign > 0
                ? new("alternating-next-term", EmlExactRational.Zero, next)
                : new("alternating-next-term", next, EmlExactRational.Zero);
        }

        if (algorithm == EmlProcessConstantAlgorithms.Zeta3IntegralTail)
        {
            BigInteger n = terms;
            return new EmlProcessRemainderCorroboration(
                "decreasing-integral-tail",
                new EmlExactRational(BigInteger.One, 2 * (n + 1) * (n + 1)),
                new EmlExactRational(BigInteger.One, 2 * n * n));
        }

        throw new InvalidDataException($"unknown process-constant algorithm {(int)algorithm}");
    }

    private static EmlProcessConstantCheck Reject(string detail, EmlCertifiedInterval bounds = default)
        => new(false, detail, bounds);
}
