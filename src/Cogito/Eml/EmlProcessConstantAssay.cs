namespace Cogito;

using System.Text;

public static class EmlProcessConstantAssay
{
    public static int Run(long fuel, long liftFuel)
    {
        string report = Render(fuel, liftFuel, out bool passed);
        Console.Write(report);
        return passed ? 0 : 1;
    }

    public static string Render(long fuel, long liftFuel, out bool passed)
    {
        if (fuel <= 0) throw new ArgumentOutOfRangeException(nameof(fuel), fuel, "initial fuel must be positive");
        if (liftFuel <= 0) throw new ArgumentOutOfRangeException(nameof(liftFuel), liftFuel, "lift fuel must be positive");

        StringBuilder report = new();
        report.AppendLine("algorithm\tversion\tbase_terms\tlifted_terms\tbase_fuel\tlifted_fuel\tbase_digest\tlifted_digest\tbase_lower\tbase_upper\tlifted_lower\tlifted_upper\tbase_remainder\tlifted_remainder\tbase_check\tlifted_check\tresume_exact\tmonotone_lift\ttamper_rejected");
        passed = AppendProcess(report, EmlProcessConstants.CreateCatalanState(), fuel, liftFuel);
        passed &= AppendProcess(report, EmlProcessConstants.CreateZeta3State(), fuel, liftFuel);
        return report.ToString();
    }

    private static bool AppendProcess(StringBuilder report, EmlProcessConstantState initial, long fuel, long liftFuel)
    {
        EmlProcessConstantState baseState = EmlProcessConstants.Advance(in initial, fuel);
        EmlProcessConstantState liftedState = EmlProcessConstants.Advance(in baseState, liftFuel);
        EmlProcessConstantState directState = EmlProcessConstants.Advance(in initial, checked(fuel + liftFuel));
        long firstLegFuel = checked((fuel + liftFuel) / 2);
        EmlProcessConstantState firstLeg = EmlProcessConstants.Advance(in initial, firstLegFuel);
        EmlProcessConstantState resumedState = EmlProcessConstants.Advance(in firstLeg, checked(fuel + liftFuel - firstLegFuel));

        EmlProcessConstantCertificate baseCertificate = EmlProcessConstants.Certify(in baseState);
        EmlProcessConstantCertificate liftedCertificate = EmlProcessConstants.Certify(in liftedState);
        EmlProcessConstantCheck baseCheck = EmlProcessConstantChecker.Check(in baseCertificate);
        EmlProcessConstantCheck liftedCheck = EmlProcessConstantChecker.Check(in liftedCertificate);
        EmlProcessConstantCheck monotone = EmlProcessConstantChecker.ValidateMonotoneLift(in baseCertificate, in liftedCertificate);
        bool resumeExact = directState == resumedState && directState == liftedState;

        EmlCertifiedInterval forgedBounds = new(baseCertificate.Bounds.Lower, baseCertificate.Bounds.Upper + EmlExactRational.One);
        EmlProcessConstantCertificate forgedCertificate = baseCertificate with { Bounds = forgedBounds };
        bool tamperRejected = !EmlProcessConstantChecker.Check(in forgedCertificate).Accepted;

        report.Append(EmlProcessConstants.GetAlgorithmToken(initial.Algorithm)).Append('\t')
            .Append(baseCertificate.Version).Append('\t')
            .Append(baseCertificate.Terms).Append('\t').Append(liftedCertificate.Terms).Append('\t')
            .Append(baseCertificate.Fuel).Append('\t').Append(liftedCertificate.Fuel).Append('\t')
            .Append(baseCertificate.StateDigest).Append('\t').Append(liftedCertificate.StateDigest).Append('\t')
            .Append(baseCertificate.Bounds.Lower).Append('\t').Append(baseCertificate.Bounds.Upper).Append('\t')
            .Append(liftedCertificate.Bounds.Lower).Append('\t').Append(liftedCertificate.Bounds.Upper).Append('\t')
            .Append(FormatCorroboration(baseCertificate.RemainderCorroboration)).Append('\t')
            .Append(FormatCorroboration(liftedCertificate.RemainderCorroboration)).Append('\t')
            .Append(baseCheck.Accepted ? "PASS" : "FAIL:" + baseCheck.Detail).Append('\t')
            .Append(liftedCheck.Accepted ? "PASS" : "FAIL:" + liftedCheck.Detail).Append('\t')
            .Append(resumeExact ? "PASS" : "FAIL").Append('\t')
            .Append(monotone.Accepted ? "PASS" : "FAIL:" + monotone.Detail).Append('\t')
            .AppendLine(tamperRejected ? "PASS" : "FAIL");

        return baseCheck.Accepted && liftedCheck.Accepted && resumeExact && monotone.Accepted && tamperRejected;
    }

    private static string FormatCorroboration(in EmlProcessRemainderCorroboration witness)
        => $"{witness.Rule}:{witness.LowerOffset}..{witness.UpperOffset}";
}
