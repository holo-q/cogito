namespace Cogito;

using System.Numerics;

internal readonly record struct EmlProcessProbeEquality(
    EmlResidualProbe Source,
    EmlProcessFunctionProbeCertificate Candidate,
    EmlRect Difference,
    bool ContainsSourceValue,
    bool ErrorBounded,
    bool EnclosureEquivalent)
{
    // Structural syntax is the equality proof. Source-value containment is only a corroborating read because the
    // source plain subtraction may have already erased the residual on a branch cut.
    public bool HasCertifiedCandidate => ErrorBounded;
    public bool HasGenericValueCertificate
        => ContainsSourceValue && ErrorBounded && Source.Value == Candidate.Value;
}

internal readonly record struct EmlProcessResidualOccurrenceCheck(
    bool Accepted,
    string Detail,
    EmlResidualWitness Source,
    EmlProcessFunctionCertificate Process,
    EmlProcessProbeEquality P1,
    EmlProcessProbeEquality P2,
    EmlProcessProbeEquality P3)
{
    public EmlHoleRepairOccurrenceCheck ToHoleRepairOccurrenceCheck()
        => new(Accepted, Detail, null);
}

internal static class EmlProcessResidualVerifier
{
    internal static EmlProcessResidualOccurrenceCheck Verify(
        EmlPredictionID sourcePredictionID,
        in EmlPrediction sourcePrediction,
        in EmlResidualWitness expectedSource,
        in EmlProcessFunction function,
        EmlResidualComposition? derivation,
        Dictionary<string, Func<Complex, Complex, Complex>> references,
        EmlEvaluatorClock clock,
        EmlDeliberationLease? deliberationLease = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(clock);
        try
        {
            EmlGrader grader = new(clock, deliberationLease);
            if (!grader.TryDescribeResidual(in sourcePrediction, references, out EmlResidualWitness source))
                return Reject("source residual cannot be reconstructed", in source);
            if (source != expectedSource)
                return Reject("source residual reconstruction differs from the claim-bound obligation", in source);

            deliberationLease?.ReserveVerifierProgramPoints(1);
            EmlProcessFunctionCertificate process = EmlProcessFunctions.Certify(in function, deliberationLease);
            deliberationLease?.ReserveVerifierProgramPoints(1);
            EmlProcessFunctionCheck certificateCheck = EmlProcessFunctionChecker.Check(in process, deliberationLease);
            if (!certificateCheck.Accepted)
                return Reject("process certificate rejected: " + certificateCheck.Detail, in source, in process);
            if (function.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries
                && derivation is not { Law: EmlResidualCompositionLaws.ExponentialTail })
                return Reject("exponential-series process requires an ExponentialTail structural derivation", in source, in process);

            EmlProcessProbeEquality p1 = CompareProbe(source.P1, process.P1, function.Algorithm);
            EmlProcessProbeEquality p2 = CompareProbe(source.P2, process.P2, function.Algorithm);
            EmlProcessProbeEquality p3 = CompareProbe(source.P3, process.P3, function.Algorithm);
            if (derivation is not EmlResidualComposition structural)
            {
                bool equivalent = p1.HasGenericValueCertificate
                    && p2.HasGenericValueCertificate
                    && p3.HasGenericValueCertificate;
                return new EmlProcessResidualOccurrenceCheck(
                    equivalent,
                    equivalent ? "generic-process-value-certificate" : CreateProbeDetail("generic-process-value-certificate-rejected", in p1, in p2, in p3),
                    source,
                    process,
                    p1,
                    p2,
                    p3);
            }

            bool reconstructed = structural.SourcePredictionID == sourcePredictionID
                && TryReconstructComposition(
                    sourcePredictionID,
                    in sourcePrediction,
                    function,
                    out EmlResidualComposition expectedComposition,
                    deliberationLease)
                && expectedComposition == structural
                && expectedComposition.Process == function;
            bool probesProved = p1.HasCertifiedCandidate
                && p2.HasCertifiedCandidate
                && p3.HasCertifiedCandidate;
            bool accepted = reconstructed && probesProved;
            string detail = accepted
                ? structural.Law == EmlResidualCompositionLaws.ExponentialTail
                    ? "structural-exp-series-syntax-equality-certified"
                    : "structural-log-ratio-syntax-equality-certified"
                : !reconstructed
                    ? "structural-log-ratio-source-binding-rejected"
                    : CreateProbeDetail("structural-log-ratio-equality-unproved", in p1, in p2, in p3);
            return new EmlProcessResidualOccurrenceCheck(accepted, detail, source, process, p1, p2, p3);
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            EmlResidualWitness source = expectedSource;
            return Reject(error.Message, in source);
        }
    }

    private static EmlProcessProbeEquality CompareProbe(
        EmlResidualProbe source,
        EmlProcessFunctionProbeCertificate candidate,
        EmlProcessFunctionAlgorithms algorithm)
    {
        EmlRect difference = EmlRect.Sub(source.Enclosure, candidate.Enclosure);
        bool containsSourceValue = IsFinite(source.Value)
            && IsFinite(candidate.Value)
            && !candidate.Enclosure.IsBlown
            && candidate.Enclosure.Re.Contains(source.Value.Real)
            && candidate.Enclosure.Im.Contains(source.Value.Imaginary);
        bool errorBounded = HasCertifiedErrorEnvelope(in candidate, algorithm);
        bool equivalent = source.Value == candidate.Value && source.Enclosure == candidate.Enclosure;
        return new EmlProcessProbeEquality(source, candidate, difference, containsSourceValue, errorBounded, equivalent);
    }

    internal static bool HasCertifiedErrorEnvelope(
        in EmlProcessFunctionProbeCertificate candidate,
        EmlProcessFunctionAlgorithms algorithm)
    {
        if (!IsFinite(candidate.Value)
            || candidate.Enclosure.IsBlown
            || !double.IsFinite(candidate.RhoUpper)
            || candidate.RhoUpper < 0.0
            || candidate.RhoUpper >= 1.0
            || !double.IsFinite(candidate.RemainderRadius)
            || candidate.RemainderRadius < 0.0
            || candidate.PrincipalBranchTurn != 0)
            return false;

        double realLo = EmlIv.Dn(candidate.Value.Real - candidate.RemainderRadius);
        double realHi = EmlIv.Up(candidate.Value.Real + candidate.RemainderRadius);
        double imaginaryLo = EmlIv.Dn(candidate.Value.Imaginary - candidate.RemainderRadius);
        double imaginaryHi = EmlIv.Up(candidate.Value.Imaginary + candidate.RemainderRadius);
        EmlRect errorEnvelope = algorithm switch
        {
            EmlProcessFunctionAlgorithms.NegativeLogSeries or EmlProcessFunctionAlgorithms.LogRatioSeries
                => new EmlRect(new EmlIv(realLo, realHi), EmlIv.Point(0.0)),
            EmlProcessFunctionAlgorithms.ExponentialSeries
                => new EmlRect(new EmlIv(realLo, realHi), new EmlIv(imaginaryLo, imaginaryHi)),
            _ => EmlRect.Blown,
        };
        return Contains(candidate.Enclosure, errorEnvelope);
    }

    private static bool Contains(EmlRect outer, EmlRect inner)
        => !outer.IsBlown
            && !inner.IsBlown
            && outer.Re.Contains(inner.Re.Lo)
            && outer.Re.Contains(inner.Re.Hi)
            && outer.Im.Contains(inner.Im.Lo)
            && outer.Im.Contains(inner.Im.Hi);

    internal static string Describe(
        EmlPredictionID sourcePredictionID,
        in EmlPrediction sourcePrediction,
        in EmlResidualWitness expectedSource,
        in EmlProcessFunction function,
        EmlResidualComposition? derivation,
        Dictionary<string, Func<Complex, Complex, Complex>> references)
        => Verify(sourcePredictionID, in sourcePrediction, in expectedSource, in function, derivation, references, new EmlEvaluatorClock()).Detail;

    private static string CreateProbeDetail(
        string prefix,
        in EmlProcessProbeEquality p1,
        in EmlProcessProbeEquality p2,
        in EmlProcessProbeEquality p3)
        => prefix + ":source-value-"
            + (p1.ContainsSourceValue ? 'o' : 'x')
            + (p2.ContainsSourceValue ? 'o' : 'x')
            + (p3.ContainsSourceValue ? 'o' : 'x')
            + ":error-"
            + (p1.ErrorBounded ? 'o' : 'x')
            + (p2.ErrorBounded ? 'o' : 'x')
            + (p3.ErrorBounded ? 'o' : 'x')
            + ":equivalent-"
            + (p1.EnclosureEquivalent ? 'o' : 'x')
            + (p2.EnclosureEquivalent ? 'o' : 'x')
            + (p3.EnclosureEquivalent ? 'o' : 'x');

    private static EmlProcessResidualOccurrenceCheck Reject(
        string detail,
        in EmlResidualWitness source)
        => new(false, detail, source, default, default, default, default);

    private static EmlProcessResidualOccurrenceCheck Reject(
        string detail,
        in EmlResidualWitness source,
        in EmlProcessFunctionCertificate process)
        => new(false, detail, source, process, default, default, default);

    private static bool IsFinite(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    private static bool TryReconstructComposition(
        EmlPredictionID sourcePredictionID,
        in EmlPrediction sourcePrediction,
        in EmlProcessFunction function,
        out EmlResidualComposition derivation,
        EmlDeliberationLease? deliberationLease)
        => function.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries
            ? EmlResidualDeriver.TryDeriveExponentialTail(sourcePredictionID, in sourcePrediction, function.Fuel, out derivation, deliberationLease)
            : EmlResidualDeriver.TryDeriveSharedExponentialArgument(sourcePredictionID, in sourcePrediction, function.Fuel, out derivation, deliberationLease);
}
