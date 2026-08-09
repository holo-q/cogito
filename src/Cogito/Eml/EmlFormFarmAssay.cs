namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Induct;

internal static class EmlFormFarmAssay
{
    private const string LogExpTemplate = "11?E1EE1E = ?";
    private const int AttemptLimit = 32;

    public static int Run(ulong seed, int signatureDigits)
    {
        EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
        EmlSieve armed = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        EmlSieve shadow = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        EmlLawStore armedStore = CreateLawStore(signatureDigits);
        EmlLawStore shadowStore = CreateLawStore(signatureDigits);
        EmlFormFarmPlan armedPlan = EmlFormFarm.CreatePlan(armed, armedStore, AttemptLimit);
        EmlFormFarmPlan shadowPlan = EmlFormFarm.CreatePlan(shadow, shadowStore, AttemptLimit);
        string armedPlanDigest = CalculatePlanDigest(armedPlan);
        string shadowPlanDigest = CalculatePlanDigest(shadowPlan);
        if (!string.Equals(armedPlanDigest, shadowPlanDigest, StringComparison.Ordinal))
            throw new InvalidDataException("form-farm arms did not receive identical deterministic rewrite plans");

        Tape mainTape = new();
        int tapeEventsBefore = mainTape.Count;
        ArmBaseline armedBefore = CaptureBaseline(armed);
        ArmBaseline shadowBefore = CaptureBaseline(shadow);
        EmlLawFunnel funnelBefore = MeasureFunnel(armed, seed);

        EmlFormFarmResult armedResult = EmlFormFarm.Execute(armed, armedPlan, retainAdmissions: true);
        EmlFormFarmResult shadowResult = EmlFormFarm.Execute(shadow, shadowPlan, retainAdmissions: false);
        armedStore.RecordFormFarm(in armedResult);
        shadowStore.RecordFormFarm(in shadowResult);

        ArmDelta armedDelta = MeasureDelta(armedBefore, armed);
        ArmDelta shadowDelta = MeasureDelta(shadowBefore, shadow);
        EmlLawFunnel funnelAfter = MeasureFunnel(armed, seed);
        bool sieveSaveIdentity = VerifySieveSaveIdentity(armed, signatureDigits);
        bool lawStoreSaveIdentity = VerifyLawStoreSaveIdentity(armedStore);
        int mainTapeEvents = mainTape.Count - tapeEventsBefore;
        bool accepted = armedResult.Accepted > 0
            && armedResult.Retained == armedResult.Accepted
            && shadowResult.Retained == 0
            && armedResult.FirstCaptures == 0
            && shadowResult.FirstCaptures == 0
            && armedResult.RepresentativeChanges == 0
            && shadowResult.RepresentativeChanges == 0
            && armedDelta.Corroborations == 0
            && shadowDelta.Corroborations == 0
            && armedDelta.NewMints == 0
            && shadowDelta.NewMints == 0
            && armedDelta.SemanticDeltas == 0
            && shadowDelta.SemanticDeltas == 0
            && armedDelta.ExactClasses == 0
            && shadowDelta.ExactClasses == 0
            && armedDelta.ExactGradeCensus == armedResult.Accepted
            && shadowDelta.ExactGradeCensus == 0
            && armedDelta.DiscoveryCensus == 0
            && shadowDelta.DiscoveryCensus == 0
            && armedDelta.FiniteOffers == 0
            && shadowDelta.FiniteOffers == 0
            && armedDelta.Frontier == 0
            && shadowDelta.Frontier == 0
            && armedResult.Evaluation.Calls == shadowResult.Evaluation.Calls
            && shadowDelta.MintLog == 0
            && mainTapeEvents == 0
            && funnelAfter.InputPredictions - funnelBefore.InputPredictions == armedResult.Accepted
            && funnelAfter.CandidateForms >= funnelBefore.CandidateForms
            && sieveSaveIdentity
            && lawStoreSaveIdentity;

        StringBuilder report = new();
        report.AppendLine("metric\tarmed\tsemantic_shadow");
        AppendMetric(report, "attempted", armedResult.Attempted, shadowResult.Attempted);
        AppendMetric(report, "accepted", armedResult.Accepted, shadowResult.Accepted);
        AppendMetric(report, "retained_forms", armedResult.Retained, shadowResult.Retained);
        AppendMetric(report, "rejected", armedResult.Rejected, shadowResult.Rejected);
        AppendMetric(report, "evaluator_calls", armedResult.Evaluation.Calls, shadowResult.Evaluation.Calls);
        AppendMetric(report, "first_captures", armedResult.FirstCaptures, shadowResult.FirstCaptures);
        AppendMetric(report, "representative_changes", armedResult.RepresentativeChanges, shadowResult.RepresentativeChanges);
        AppendMetric(report, "corroborations_delta", armedDelta.Corroborations, shadowDelta.Corroborations);
        AppendMetric(report, "new_mints_delta", armedDelta.NewMints, shadowDelta.NewMints);
        AppendMetric(report, "semantic_deltas_delta", armedDelta.SemanticDeltas, shadowDelta.SemanticDeltas);
        AppendMetric(report, "exact_classes_delta", armedDelta.ExactClasses, shadowDelta.ExactClasses);
        AppendMetric(report, "exact_grade_census_delta", armedDelta.ExactGradeCensus, shadowDelta.ExactGradeCensus);
        AppendMetric(report, "discovery_census_delta", armedDelta.DiscoveryCensus, shadowDelta.DiscoveryCensus);
        AppendMetric(report, "finite_offers_delta", armedDelta.FiniteOffers, shadowDelta.FiniteOffers);
        AppendMetric(report, "frontier_delta", armedDelta.Frontier, shadowDelta.Frontier);
        AppendMetric(report, "mint_log_delta", armedDelta.MintLog, shadowDelta.MintLog);
        report.Append("funnel\tinput_claims\t").Append(funnelBefore.InputPredictions).Append("\t").Append(funnelAfter.InputPredictions).AppendLine();
        report.Append("funnel\tcandidate_forms\t").Append(funnelBefore.CandidateForms).Append("\t").Append(funnelAfter.CandidateForms).AppendLine();
        report.Append("plan\tdigest\t").Append(armedPlanDigest).Append("\t").Append(shadowPlanDigest).AppendLine();
        report.Append("invariant\tmain_tape_events\t").Append(mainTapeEvents).AppendLine();
        report.Append("invariant\tsieve_save_load_save_identity\t").Append(sieveSaveIdentity ? 1 : 0).AppendLine();
        report.Append("invariant\tlaw_store_save_load_save_identity\t").Append(lawStoreSaveIdentity ? 1 : 0).AppendLine();
        report.Append("status\t").Append(accepted ? "accepted" : "rejected").AppendLine();

        Run receipt = Cogito.Run.New("eml-form-farm-assay");
        const string ReceiptName = "eml_form_farm_assay.tsv";
        receipt.Write(ReceiptName, report.ToString());
        Console.WriteLine($"  EML form farm assay -> {Path.GetRelativePath(Environment.CurrentDirectory, receipt.PathOf(ReceiptName))}");
        Console.WriteLine($"  armed {armedResult.Accepted}/{armedResult.Attempted} accepted · shadow retained {shadowResult.Retained} · evaluator {armedResult.Evaluation.Calls}/{shadowResult.Evaluation.Calls}");
        Console.WriteLine($"  anti-unifier forms {funnelBefore.CandidateForms}->{funnelAfter.CandidateForms} · input claims {funnelBefore.InputPredictions}->{funnelAfter.InputPredictions} · tape {mainTapeEvents}");
        return accepted ? 0 : 1;
    }

    private static EmlLawStore CreateLawStore(int signatureDigits)
    {
        EmlGrader grader = new();
        EmlVerdict xVerdict = grader.GradeRpn("11xE1EE1E", "x");
        EmlVerdict yVerdict = grader.GradeRpn("11yE1EE1E", "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in xVerdict, signatureDigits), "11xE1EE1E", "x"),
            new EmlLawPrediction(EmlCert.Of(in yVerdict, signatureDigits), "11yE1EE1E", "y"),
        ];
        EmlLaw law = new(LogExpTemplate, 2, 2, 16.0, "1", "111E1EE1E = 1");
        if (!EmlVerifiedLaw.TryVerify(in law, support, signatureDigits, out EmlVerifiedLaw? verified)
            || verified is null)
            throw new InvalidDataException("independent log-exp law verification failed");
        EmlLawStore store = new();
        if (!store.TryAdmit(verified, 0,
                out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)
            || !admission.FirstCapture)
            throw new InvalidDataException("independently verified log-exp law did not open its class");
        return store;
    }

    private static EmlLawFunnel MeasureFunnel(EmlSieve sieve, ulong seed)
    {
        RePairResult grammar = Engine.Induce(sieve.TierBytes(static mint => mint.Grade == 'E')).Result;
        EmlAntiUnify.DiscoverCandidates(sieve, grammar, seed, out EmlLawFunnel funnel);
        return funnel;
    }

    private static ArmBaseline CaptureBaseline(EmlSieve sieve)
        => new(sieve.MintLog.Count, sieve.NewMints.Count, sieve.NewSemanticDeltas.Count,
            sieve.ExactClasses, sieve.CorrobExact(), sieve.GradeCount('E'), sieve.Identities + sieve.ValueHits,
            sieve.FiniteOffers, sieve.KFrontier);

    private static ArmDelta MeasureDelta(in ArmBaseline before, EmlSieve sieve)
        => new(
            sieve.MintLog.Count - before.MintLog,
            sieve.NewMints.Count - before.NewMints,
            sieve.NewSemanticDeltas.Count - before.SemanticDeltas,
            sieve.ExactClasses - before.ExactClasses,
            sieve.CorrobExact() - before.Corroborations,
            sieve.GradeCount('E') - before.ExactGradeCensus,
            sieve.Identities + sieve.ValueHits - before.DiscoveryCensus,
            sieve.FiniteOffers - before.FiniteOffers,
            sieve.KFrontier - before.Frontier);

    private static bool VerifySieveSaveIdentity(EmlSieve sieve, int signatureDigits)
    {
        byte[] image = sieve.CaptureAdmissionState();
        EmlSieve loaded = EmlRematchFixture.CloneSieve(signatureDigits, image);
        return image.AsSpan().SequenceEqual(loaded.CaptureAdmissionState());
    }

    private static bool VerifyLawStoreSaveIdentity(EmlLawStore store)
    {
        byte[] image = SaveLawStore(store);
        EmlLawStore loaded = new();
        using (MemoryStream stream = new(image, writable: false))
        using (CkptReader reader = new(stream)) loaded.Load(reader);
        return image.AsSpan().SequenceEqual(SaveLawStore(loaded));
    }

    private static byte[] SaveLawStore(EmlLawStore store)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) store.Save(writer);
        return stream.ToArray();
    }

    private static string CalculatePlanDigest(EmlFormFarmPlan plan)
    {
        StringBuilder text = new();
        for (int i = 0; i < plan.Attempts.Count; i++)
        {
            EmlFormFarmAttempt attempt = plan.Attempts[i];
            text.Append(attempt.AntecedentCertificate.Hex()).Append('\t')
                .Append(attempt.Rewrite.AntecedentRpn).Append('\t')
                .Append(attempt.Rewrite.ConsequentRpn).Append('\t')
                .Append(attempt.Rewrite.LawProof.OccurrenceDigest.ToString("X16")).AppendLine();
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void AppendMetric(StringBuilder report, string name, long armed, long shadow)
        => report.Append(name).Append('\t').Append(armed).Append('\t').Append(shadow).AppendLine();

    private readonly record struct ArmBaseline(
        int MintLog,
        int NewMints,
        int SemanticDeltas,
        int ExactClasses,
        int Corroborations,
        int ExactGradeCensus,
        int DiscoveryCensus,
        long FiniteOffers,
        int Frontier);

    private readonly record struct ArmDelta(
        int MintLog,
        int NewMints,
        int SemanticDeltas,
        int ExactClasses,
        int Corroborations,
        int ExactGradeCensus,
        int DiscoveryCensus,
        long FiniteOffers,
        int Frontier);
}
