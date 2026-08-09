namespace Cogito;

internal readonly record struct EmlFormFarmAttempt(EmlCert AntecedentCertificate, EmlLawRewrite Rewrite);

internal sealed class EmlFormFarmPlan(List<EmlFormFarmAttempt> attempts)
{
    public List<EmlFormFarmAttempt> Attempts { get; } = attempts;
}

internal readonly record struct EmlFormFarmResult(
    int Attempted,
    int Accepted,
    int Rejected,
    int Retained,
    int FirstCaptures,
    int RepresentativeChanges,
    EmlEvaluatorInterval Evaluation);

internal static class EmlFormFarm
{
    public static EmlFormFarmPlan CreatePlan(EmlSieve sieve, EmlLawStore lawStore, int maximumAttempts)
    {
        if (maximumAttempts < 0) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        IReadOnlyList<EmlExactRPNForm> forms = sieve.ExactRPNForms;
        List<EmlFormFarmAttempt> attempts = new(maximumAttempts);
        for (int formIndex = 0; formIndex < forms.Count && attempts.Count < maximumAttempts; formIndex++)
        {
            EmlExactRPNForm form = forms[formIndex];
            List<EmlLawRewrite> rewrites = new();
            lawStore.AppendRewrites([form.Program], rewrites);
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count && attempts.Count < maximumAttempts; rewriteIndex++)
                attempts.Add(new EmlFormFarmAttempt(form.Certificate, rewrites[rewriteIndex]));
        }
        return new EmlFormFarmPlan(attempts);
    }

    public static EmlFormFarmResult Execute(EmlSieve sieve, EmlFormFarmPlan plan, bool retainAdmissions)
    {
        byte[]? admissionState = retainAdmissions ? null : sieve.CaptureAdmissionState();
        long evaluatorStart = sieve.EvaluatorClock.ProgramPointEvaluations;
        int accepted = 0;
        int firstCaptures = 0;
        int representativeChanges = 0;
        for (int attemptIndex = 0; attemptIndex < plan.Attempts.Count; attemptIndex++)
        {
            EmlFormFarmAttempt attempt = plan.Attempts[attemptIndex];
            if (sieve.TryAdmitExactForm(
                    attempt.Rewrite.ConsequentRpn,
                    attempt.AntecedentCertificate,
                    attempt.Rewrite.LawProof.OccurrenceDigest,
                    out EmlExactFormAdmission admission))
                accepted++;
            if (admission.FirstCapture) firstCaptures++;
            if (admission.RepresentativeChanged) representativeChanges++;
        }
        EmlEvaluatorClockSnapshot consumedClock = sieve.EvaluatorClock.Capture();
        if (admissionState is not null) sieve.RestoreAdmissionState(admissionState, in consumedClock);
        EmlEvaluatorInterval evaluation = new(evaluatorStart, consumedClock.ProgramPointEvaluations);
        return new EmlFormFarmResult(
            plan.Attempts.Count,
            accepted,
            plan.Attempts.Count - accepted,
            retainAdmissions ? accepted : 0,
            firstCaptures,
            representativeChanges,
            evaluation);
    }
}
