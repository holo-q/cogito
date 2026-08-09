namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

internal static class EmlProcessResidualRematchAssay
{
    private const int RequiredObligations = 4;
    private const int MinimumReplicates = 3;
    private const int BankedBoundedSupplyProposals = 99;
    private const int BankedBoundedSupplyAdmissions = 0;
    private const string Live139Lhs = "xx11y1Ex11EEEy1Ex11EEEyEEEyEEE1EE";
    private const string Live139Rhs = "xx11y1ExEy1ExEyEEEyEEE1EE";
    private const string Live140Lhs = "1111y1Ex11EEEy1Ex11EEEyEEEyEEE1EE";
    private const string Live140Rhs = "xx11y1ExEy1ExEyEEEyEEE1EE";
    private const string FiniteNegativeLogXProgram = "111E1EE1111EE1EE111111EE1EE11EEE1EE11xE1EE1EE1EE1EE";
    private const string FiniteNegativeLogYProgram = "111E1EE1111EE1EE111111EE1EE11EEE1EE11yE1EE1EE1EE1EE";

    public static int Run(ulong seed, int signatureDigits, int replicates, long processFuel)
    {
        if (replicates < MinimumReplicates)
            throw new ArgumentOutOfRangeException(nameof(replicates), replicates,
                $"process residual rematch requires at least {MinimumReplicates} replicates");

        Run receipt = Cogito.Run.New("eml-process-residual-rematch");
        StringBuilder report = new();
        report.AppendLine("section\treplicate\tarm\tsource_claim\tsource_text\tresidual_signature\tbinding\tshuffled_binding\tused_binding\ttarget_p1\ttarget_p2\ttarget_p3\tproposals\tadmitted_repairs\tevaluator_calls\tprocess_fuel\tcertificate_check\tcheckpoint_bytes\tcheckpoint_digest\tsave_load_save_identity\tstatus");
        AppendBankedBoundedSupplyBaseline(report);

        EmlRematchFixture finiteFixture = EmlRematchFixture.Create(signatureDigits);
        if (finiteFixture.Obligations.Count != RequiredObligations)
            throw new InvalidDataException($"finite residual rematch requires exactly {RequiredObligations} obligations; found {finiteFixture.Obligations.Count}");
        bool accepted = AppendFiniteExpressibilityFalsifier(report, finiteFixture, signatureDigits);
        accepted &= AppendExponentialTailContract(report, processFuel, signatureDigits);
        accepted &= AppendAmbiguousLiveTailRejection(report, processFuel);
        accepted &= AppendNestedLogRatioRematch(report, signatureDigits, processFuel);
        for (int replicate = 0; replicate < replicates; replicate++)
        {
            ulong replicateSeed = MixReplicateSeed(seed, replicate);
            EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
            if (fixture.Obligations.Count != RequiredObligations)
                throw new InvalidDataException($"process residual rematch requires exactly {RequiredObligations} finite obligations; found {fixture.Obligations.Count}");

            List<ProcessBinding> bindings = BindProcessCandidates(fixture, processFuel);
            int[] shuffled = CreateDerangement(bindings, replicateSeed);
            EmlSieve real = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
            EmlSieve nullArm = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
            byte[] realStart = real.CaptureAdmissionState();
            byte[] nullStart = nullArm.CaptureAdmissionState();
            if (!realStart.AsSpan().SequenceEqual(nullStart))
                throw new InvalidDataException("real and null process rematch arms did not start from identical sieve state");

            List<RematchArmRow> rows = new(RequiredObligations * 2);
            int shuffledPositions = 0;
            int realProposals = 0;
            int realAdmissions = 0;
            int nullProposals = 0;
            int nullAdmissions = 0;
            long realEvaluatorCalls = 0;
            long nullEvaluatorCalls = 0;
            long realProcessFuel = 0;
            long nullProcessFuel = 0;
            bool certificateChecksPassed = true;

            for (int obligationIndex = 0; obligationIndex < bindings.Count; obligationIndex++)
            {
                ProcessBinding binding = bindings[obligationIndex];
                ProcessBinding shuffledBinding = bindings[shuffled[obligationIndex]];
                EmlObligationResolution obligation = binding.Obligation;
                if (binding.Obligation.ResidualSignature == shuffledBinding.Obligation.ResidualSignature)
                    throw new InvalidDataException("process residual null retained the source residual signature");
                if (string.Equals(binding.Candidate.Program, shuffledBinding.Candidate.Program, StringComparison.Ordinal))
                    throw new InvalidDataException("process residual null retained the source process binding");
                shuffledPositions++;

                RematchArmRow realRow = ExecuteProcessArm(
                    "real",
                    real,
                    in obligation,
                    in binding,
                    in shuffledBinding,
                    binding.Candidate,
                    admitRepair: true);
                RematchArmRow nullRow = ExecuteProcessArm(
                    "null",
                    nullArm,
                    in obligation,
                    in binding,
                    in shuffledBinding,
                    shuffledBinding.Candidate,
                    admitRepair: false);
                rows.Add(realRow);
                rows.Add(nullRow);
                realProposals = checked(realProposals + realRow.Proposals);
                realAdmissions = checked(realAdmissions + realRow.AdmittedRepairs);
                nullProposals = checked(nullProposals + nullRow.Proposals);
                nullAdmissions = checked(nullAdmissions + nullRow.AdmittedRepairs);
                realEvaluatorCalls = checked(realEvaluatorCalls + realRow.EvaluatorCalls);
                nullEvaluatorCalls = checked(nullEvaluatorCalls + nullRow.EvaluatorCalls);
                realProcessFuel = checked(realProcessFuel + realRow.ProcessFuel);
                nullProcessFuel = checked(nullProcessFuel + nullRow.ProcessFuel);
                certificateChecksPassed &= realRow.CertificateCheck && nullRow.CertificateCheck;
            }

            byte[] admittedImage = real.CaptureAdmissionState();
            EmlSieve reloaded = EmlRematchFixture.CloneSieve(signatureDigits, admittedImage);
            byte[] resavedImage = reloaded.CaptureAdmissionState();
            bool saveLoadSaveIdentity = admittedImage.AsSpan().SequenceEqual(resavedImage);
            string checkpointDigest = Convert.ToHexStringLower(SHA256.HashData(admittedImage));
            bool replicateAccepted = shuffledPositions >= 1
                && realProposals >= 1
                && realAdmissions >= 1
                && nullProposals == 0
                && nullAdmissions == 0
                && realProcessFuel == nullProcessFuel
                && certificateChecksPassed
                && saveLoadSaveIdentity;
            accepted &= replicateAccepted;

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                AppendArmRow(report, replicate, rows[rowIndex], admittedImage.Length, checkpointDigest, saveLoadSaveIdentity);
            report.Append("summary\t").Append(replicate).Append("\tmatched-process\t\t\t\t\t\t\t\t\t\t")
                .Append(realProposals).Append('/').Append(nullProposals).Append('\t')
                .Append(realAdmissions).Append('/').Append(nullAdmissions).Append('\t')
                .Append(realEvaluatorCalls).Append('/').Append(nullEvaluatorCalls).Append('\t')
                .Append(realProcessFuel).Append('/').Append(nullProcessFuel).Append('\t')
                .Append(certificateChecksPassed ? 1 : 0).Append('\t')
                .Append(admittedImage.Length).Append('\t').Append(checkpointDigest).Append('\t')
                .Append(saveLoadSaveIdentity ? 1 : 0).Append('\t')
                .Append(replicateAccepted ? "accepted" : "rejected").AppendLine();
        }

        string receiptName = "eml_process_residual_rematch.tsv";
        receipt.Write(receiptName, report.ToString());
        Console.WriteLine($"  EML process residual rematch -> {Path.GetRelativePath(Environment.CurrentDirectory, receipt.PathOf(receiptName))}");
        Console.WriteLine($"  {replicates:N0} matched replicates · {RequiredObligations:N0} obligations · process fuel {processFuel:N0}");
        return accepted ? 0 : 1;
    }

    private static bool AppendExponentialTailContract(StringBuilder report, long processFuel, int signatureDigits)
    {
        if (processFuel <= 0) throw new ArgumentOutOfRangeException(nameof(processFuel));
        EmlPredictionID sourcePredictionID = new(39);
        const string fixtureA = "xxy1E1EyyEyEEE1EE";
        const string fixtureB = "y1E1E";
        const string syntheticArgument = "11" + fixtureA + "E1EE" + fixtureB + "1EE";
        const string syntheticLhs = syntheticArgument + "11EE";
        string syntheticLine = syntheticLhs + " ~ " + syntheticArgument;
        EmlPrediction sourcePrediction = new(syntheticLine, syntheticLhs, syntheticArgument, true, true);
        EmlGrader grader = new();
        if (!grader.TryDescribeResidual(in sourcePrediction, EmlSieve.LabelChart(), out EmlResidualWitness witness))
            return false;
        if (!EmlTree.TryParseRPN(syntheticLhs, out EmlTree? fixtureTree))
            throw new InvalidDataException("cortex_0073 exponential fixture did not parse");
        int directTailMatches = CountExponentialTailMatches(fixtureTree!.Root);
        if (directTailMatches != 1)
        {
            report.Append("contract\t-3\texp-series\tcortex_0073\troot-census=").Append(directTailMatches)
                .Append("\trejected\n");
            return false;
        }
        if (!EmlResidualDeriver.TryDeriveExponentialTail(sourcePredictionID, in sourcePrediction, processFuel, out EmlResidualComposition derivation))
        {
            report.Append("contract\t-3\texp-series\tsynthetic-exp-tail\tderivation-rejected\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\trejected\n");
            return false;
        }

        EmlProcessFunction realFunction = derivation.Process;
        EmlProcessFunction nullFunction = EmlProcessFunctions.CreateExpSeries("y", processFuel);
        EmlProcessFunctionCertificate realCertificate = EmlProcessFunctions.Certify(in realFunction);
        EmlProcessFunctionCertificate nullCertificate = EmlProcessFunctions.Certify(in nullFunction);
        EmlProcessFunctionCheck realCheck = EmlProcessFunctionChecker.Check(in realCertificate);
        EmlProcessFunctionCheck nullCheck = EmlProcessFunctionChecker.Check(in nullCertificate);
        EmlProcessFunctionCertificate tampered = realCertificate with
        {
            P1 = realCertificate.P1 with { ExactStateDigest = new string('0', 64) },
        };
        EmlProcessFunctionCheck tamperedCheck = EmlProcessFunctionChecker.Check(in tampered);
        EmlProcessFunctionProbeCertificate complexEnvelopeTamperedProbe = realCertificate.P3 with
        {
            Enclosure = realCertificate.P3.Enclosure with { Im = EmlIv.Point(0.0) },
        };
        bool complexEnvelopeTamperRejected = !EmlProcessResidualVerifier.HasCertifiedErrorEnvelope(
            in complexEnvelopeTamperedProbe,
            EmlProcessFunctionAlgorithms.ExponentialSeries);
        EmlProcessResidualOccurrenceCheck realOccurrenceCheck = EmlProcessResidualVerifier.Verify(
            sourcePredictionID,
            in sourcePrediction,
            in witness,
            in realFunction,
            derivation,
            EmlSieve.LabelChart(),
            new EmlEvaluatorClock());
        EmlProcessResidualOccurrenceCheck nullOccurrenceCheck = EmlProcessResidualVerifier.Verify(
            sourcePredictionID,
            in sourcePrediction,
            in witness,
            in nullFunction,
            null,
            EmlSieve.LabelChart(),
            new EmlEvaluatorClock());
        EmlProcessFunctionCertificate resumed = EmlProcessFunctions.Certify(in realFunction);
        bool identity = string.Equals(realCertificate.Digest, resumed.Digest, StringComparison.Ordinal);
        const long monotoneFuel = 2;
        EmlProcessFunction monotoneFunction = EmlProcessFunctions.CreateExpSeries("x", monotoneFuel);
        EmlProcessFunction liftedFunction = EmlProcessFunctions.CreateExpSeries("x", checked(monotoneFuel * 2));
        EmlProcessFunctionCertificate monotoneCertificate = EmlProcessFunctions.Certify(in monotoneFunction);
        EmlProcessFunctionCertificate liftedCertificate = EmlProcessFunctions.Certify(in liftedFunction);
        bool monotone = EmlProcessFunctions.ValidateMonotoneLift(in monotoneCertificate, in liftedCertificate);
        Complex argumentP1 = Eml.EvalLadder(realFunction.NumeratorRPN, EmlTree.P1.X, EmlTree.P1.Y).Plain.Value;
        Complex argumentP2 = Eml.EvalLadder(realFunction.NumeratorRPN, EmlTree.P2.X, EmlTree.P2.Y).Plain.Value;
        Complex argumentP3 = Eml.EvalLadder(realFunction.NumeratorRPN, EmlTree.P3.X, EmlTree.P3.Y).Plain.Value;
        bool exactCortex0073Witness = NearlyEqual(argumentP1.Real, 0.0)
            && NearlyEqual(argumentP2.Real, -2.511768570911954e-12)
            && NearlyEqual(argumentP3.Real, -0.02972204388125288)
            && NearlyEqual(witness.P1.Value.Real, 0.0)
            && NearlyEqual(witness.P2.Value.Real, 0.0)
            && NearlyEqual(witness.P3.Value.Real, 0.00043735619531404257)
            && argumentP1.Imaginary == 0.0
            && argumentP2.Imaginary == 0.0
            && argumentP3.Imaginary == 0.0;
        bool p3FuelAboveFloor = liftedCertificate.P3.RemainderRadius > double.Epsilon
            && liftedCertificate.P3.RemainderRadius < monotoneCertificate.P3.RemainderRadius;
        EmlResidualExpression expression = EmlResidualExpression.CreateProcessFunction(in realFunction);
        string canonical = expression.RenderCanonical();
        EmlResidualExpression roundTrip = EmlResidualExpression.ParseCanonical(canonical);
        bool expressionRoundTrip = roundTrip.RenderCanonical() == canonical
            && roundTrip.TryGetProcessFunction(out EmlProcessFunction roundTripFunction)
            && roundTripFunction == realFunction;
        EmlGaussianRational resumeArgument = new(
            new EmlExactRational(BigInteger.One, new BigInteger(2)),
            new EmlExactRational(BigInteger.MinusOne, new BigInteger(3)));
        long splitFuel = processFuel / 2;
        EmlExpSeriesState splitState = EmlExpSeriesState.Create(resumeArgument).Advance(splitFuel);
        EmlExpSeriesState resumedState = splitState.Advance(processFuel - splitFuel);
        EmlExpSeriesState fullState = EmlExpSeriesState.Create(resumeArgument).Advance(processFuel);
        EmlExpSeriesState tamperedState = splitState with
        {
            PartialSum = splitState.PartialSum + new EmlGaussianRational(EmlExactRational.One, EmlExactRational.Zero),
        };
        bool exactResume = resumedState == fullState && resumedState.Digest() == fullState.Digest();
        bool exactTamperRejected = !tamperedState.IsValid();

        EmlSieve syntheticSieve = new(signatureDigits);
        EmlPredictionID syntheticPredictionID = syntheticSieve.SeedSyntheticObligation(syntheticLhs, syntheticArgument);
        if (syntheticSieve.GetPredictionCertificate(syntheticPredictionID).Grade != 'A')
            throw new InvalidDataException("synthetic source fixture did not mint an A-grade claim");
        syntheticSieve.DrainNewMints();
        EmlObligationResolution admissionObligation = syntheticSieve.ResolveObligation(syntheticPredictionID);
        EmlPrediction syntheticPrediction = sourcePrediction;
        if (!EmlResidualDeriver.TryDeriveExponentialTail(
                syntheticPredictionID,
                in syntheticPrediction,
                processFuel,
                out EmlResidualComposition syntheticComposition))
            throw new InvalidDataException("synthetic exponential obligation has no structural ExponentialTail derivation");
        EmlProcessFunction syntheticFunction = syntheticComposition.Process;
        if (syntheticFunction != realFunction)
            throw new InvalidDataException("synthetic exponential derivation drifted from the certified process");
        byte[] syntheticImage = syntheticSieve.CaptureAdmissionState();
        EmlSieve admissionReal = EmlRematchFixture.CloneSieve(signatureDigits, syntheticImage);
        EmlSieve admissionNull = EmlRematchFixture.CloneSieve(signatureDigits, syntheticImage);
        EmlResidualComposition nullAdmissionComposition = syntheticComposition with { SourcePredictionID = new EmlPredictionID(-902) };
        long realAdmissionStart = admissionReal.EvaluatorClock.ProgramPointEvaluations;
        EmlObligationClosureResult realAdmission = admissionReal.AdmitProcessResidualProof(
            admissionObligation.SourcePredictionID, in realFunction, syntheticComposition, realAdmissionStart);
        long realAdmissionWork = admissionReal.EvaluatorClock.ProgramPointEvaluations - realAdmissionStart;
        long nullAdmissionStart = admissionNull.EvaluatorClock.ProgramPointEvaluations;
        EmlObligationClosureResult nullAdmission = admissionNull.AdmitProcessResidualProof(
            admissionObligation.SourcePredictionID, in realFunction, nullAdmissionComposition, nullAdmissionStart);
        long nullAdmissionWork = admissionNull.EvaluatorClock.ProgramPointEvaluations - nullAdmissionStart;
        bool divergentExecution = realCertificate.P1.Value != nullCertificate.P1.Value
            || realCertificate.P2.Value != nullCertificate.P2.Value
            || realCertificate.P3.Value != nullCertificate.P3.Value;
        bool equalPoweredWork = realAdmissionWork == nullAdmissionWork && realAdmissionWork > 0;
        bool nullAdmissionsZero = !nullAdmission.Accepted;
        EmlProcessFunction insufficientPower = new(
            EmlProcessFunctionAlgorithms.ExponentialSeries,
            EmlProcessFunctions.AlgorithmVersion,
            "x",
            Eml.One.ToString(),
            0);
        EmlObligationClosureResult insufficient = admissionNull.AdmitProcessResidualProof(
            admissionObligation.SourcePredictionID, in insufficientPower, syntheticComposition, admissionNull.EvaluatorClock.ProgramPointEvaluations);
        bool insufficientPowerRejected = insufficient.Closure.Status == EmlObligationClosureStatuses.InvalidPolicy;
        byte[] sieveImage = admissionReal.CaptureAdmissionState();
        EmlSieve sieveReloaded = EmlRematchFixture.CloneSieve(signatureDigits, sieveImage);
        bool sieveRoundTrip = sieveImage.AsSpan().SequenceEqual(sieveReloaded.CaptureAdmissionState());
        byte[] sieveTamper = (byte[])sieveImage.Clone();
        byte[] sourceDigest = SHA256.HashData(Encoding.UTF8.GetBytes(syntheticLine));
        string sourceDigestHex = Convert.ToHexStringLower(sourceDigest);
        byte[] sourceDigestBytes = Encoding.ASCII.GetBytes(sourceDigestHex);
        int sourceDigestOffset = FindBytes(sieveTamper, sourceDigestBytes);
        if (sourceDigestOffset < 0)
            throw new InvalidDataException("synthetic checkpoint omitted its source digest");
        sieveTamper[sourceDigestOffset] ^= 0x01;
        bool sieveTamperRejected = ThrowsCheckpointReject(signatureDigits, sieveTamper);
        bool accepted = realCheck.Accepted
            && nullCheck.Accepted
            && !tamperedCheck.Accepted
            && complexEnvelopeTamperRejected
            && realOccurrenceCheck.Accepted
            && !nullOccurrenceCheck.Accepted
            && identity
            && monotone
            && exactCortex0073Witness
            && p3FuelAboveFloor
            && expressionRoundTrip
            && exactResume
            && exactTamperRejected
            && equalPoweredWork
            && divergentExecution
            && nullAdmissionsZero
            && insufficientPowerRejected
            && sieveRoundTrip
            && sieveTamperRejected;
        report.Append("contract\t-3\texp-series\tsynthetic-exp-tail(no-live-closure)\tsynthetic-only: Σ(n=2..∞) u^n/n!\t")
            .Append(realFunction.NumeratorRPN).Append("\t")
            .Append(nullFunction.NumeratorRPN).Append("\t")
            .Append(realCertificate.P1.FuelSpent + realCertificate.P2.FuelSpent + realCertificate.P3.FuelSpent).Append("\t")
            .Append(nullCertificate.P1.FuelSpent + nullCertificate.P2.FuelSpent + nullCertificate.P3.FuelSpent).Append("\t")
            .Append(realCertificate.P1.RhoUpper.ToString("R", CultureInfo.InvariantCulture)).Append("\t")
            .Append(realCertificate.P1.RemainderRadius.ToString("R", CultureInfo.InvariantCulture)).Append("\t")
            .Append(realCertificate.Digest).Append("\t")
            .Append(identity ? 1 : 0).Append("\t")
            .Append(monotone ? 1 : 0).Append("\t")
            .Append(liftedCertificate.P1.RemainderRadius.ToString("R", CultureInfo.InvariantCulture)).Append("\t")
            .Append("u=").Append(FormatComplex(argumentP1)).Append(',').Append(FormatComplex(argumentP2)).Append(',').Append(FormatComplex(argumentP3))
            .Append(";res=").Append(FormatComplex(witness.P1.Value)).Append(',').Append(FormatComplex(witness.P2.Value)).Append(',').Append(FormatComplex(witness.P3.Value)).Append("\t")
            .Append(exactCortex0073Witness ? 1 : 0).Append("\t")
            .Append(p3FuelAboveFloor ? 1 : 0).Append("\t")
            .Append(expressionRoundTrip ? 1 : 0).Append("\t")
            .Append(exactResume ? 1 : 0).Append("\t")
            .Append(exactTamperRejected ? 1 : 0).Append("\t")
            .Append(complexEnvelopeTamperRejected ? 1 : 0).Append("\t")
            .Append(realAdmissionWork).Append('/').Append(nullAdmissionWork).Append("\t")
            .Append(divergentExecution ? 1 : 0).Append("\t")
            .Append(nullAdmissionsZero ? 1 : 0).Append("\t")
            .Append(insufficientPowerRejected ? 1 : 0).Append("\t")
            .Append(sieveRoundTrip ? 1 : 0).Append("\t")
            .Append(sieveTamperRejected ? 1 : 0).Append("\t")
            .Append(accepted ? "accepted" : "rejected").AppendLine();
        return accepted;
    }

    private static bool ThrowsCheckpointReject(int signatureDigits, byte[] image)
    {
        try
        {
            _ = EmlRematchFixture.CloneSieve(signatureDigits, image);
            return false;
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            return true;
        }
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (int offset = 0; offset <= haystack.Length - needle.Length; offset++)
        {
            if (haystack.AsSpan(offset, needle.Length).SequenceEqual(needle)) return offset;
        }
        return -1;
    }

    private static bool AppendAmbiguousLiveTailRejection(StringBuilder report, long processFuel)
    {
        EmlPrediction[] claims =
        [
            new EmlPrediction($"{Live139Lhs} ~ {Live139Rhs}", Live139Lhs, Live139Rhs, true, true),
            new EmlPrediction($"{Live140Lhs} ~ {Live140Rhs}", Live140Lhs, Live140Rhs, true, true),
        ];
        bool accepted = true;
        for (int i = 0; i < claims.Length; i++)
        {
            EmlPrediction claim = claims[i];
            if (!EmlTree.TryParseRPN(claim.Lhs, out EmlTree? left)
                || !EmlTree.TryParseRPN(claim.Rhs, out EmlTree? right))
                throw new InvalidDataException("live exp ambiguity fixture did not parse");
            int matches = CountExponentialTailMatches(left!.Root);
            bool rejected = !EmlResidualDeriver.TryDeriveExponentialTail(new EmlPredictionID(139 + i), in claim, processFuel, out _);
            bool rowAccepted = matches == 2 && rejected;
            accepted &= rowAccepted;
            report.Append("ambiguous-live\t").Append(139 + i).Append("\tmatches=").Append(matches)
                .Append("\troot-deriver=").Append(rejected ? "rejected" : "accepted")
                .Append("\t").Append(rowAccepted ? "accepted" : "rejected").AppendLine();
        }
        return accepted;
    }

    private static int CountExponentialTailMatches(EmlTree.Node node)
    {
        int count = IsExponentialTailMatch(node) ? 1 : 0;
        if (!node.IsGate) return count;
        return count
            + CountExponentialTailMatches(node.Left!)
            + CountExponentialTailMatches(node.Right!);
    }

    private static bool IsExponentialTailMatch(EmlTree.Node node)
        => node.IsGate
            && node.Right is { IsGate: true, Left.Token: Eml.One, Right.Token: Eml.One };

    private static bool AppendNestedLogRatioRematch(
        StringBuilder report,
        int signatureDigits,
        long processFuel)
    {
        (string Left, string Right)[] claims =
        [
            ("111Ey1E1E111EEEEE", "111Ey1E1Ey1E1E11EEEEE"),
            ("11y1E1E1EEE", "11y1E1EyEEE"),
            ("11y1E1E1111EEEEEE", "11y1E1EyEEE"),
            ("y1E1E111EEE", "y1E1E1E"),
        ];
        EmlSieve source = new(signatureDigits);
        for (int i = 0; i < claims.Length; i++)
        {
            source.Offer(claims[i].Right);
            source.Offer(claims[i].Left);
        }
        source.DrainNewMints();
        List<EmlObligationResolution> obligations = new(source.Obligations.Count);
        for (int i = 0; i < source.Obligations.Count; i++)
            obligations.Add(source.ResolveObligation(source.Obligations[i].SourcePredictionID));
        obligations.Sort(static (left, right) => left.SourcePredictionID.Value.CompareTo(right.SourcePredictionID.Value));
        if (obligations.Count != claims.Length)
            throw new InvalidDataException($"nested log-ratio rematch expected {claims.Length} obligations; found {obligations.Count}");

        List<ProcessBinding> bindings = BindStructuralCandidates(source, obligations, processFuel);
        int[] shuffled = CreateDerangement(bindings, 0x9E3779B97F4A7C15UL);
        byte[] start = source.CaptureAdmissionState();
        EmlSieve real = EmlRematchFixture.CloneSieve(signatureDigits, start);
        EmlSieve nullArm = EmlRematchFixture.CloneSieve(signatureDigits, start);
        int realProposals = 0;
        int realAdmissions = 0;
        int nullProposals = 0;
        int nullAdmissions = 0;
        long realCalls = 0;
        long nullCalls = 0;
        long realFuel = 0;
        long nullFuel = 0;
        bool certificates = true;
        List<RematchArmRow> rows = new(bindings.Count * 2);
        for (int i = 0; i < bindings.Count; i++)
        {
            ProcessBinding binding = bindings[i];
            ProcessBinding shuffledBinding = bindings[shuffled[i]];
            EmlObligationResolution obligation = binding.Obligation;
            RematchArmRow realRow = ExecuteProcessArm(
                "nested-real", real, in obligation, in binding, in shuffledBinding, binding.Candidate, admitRepair: true);
            RematchArmRow nullRow = ExecuteProcessArm(
                "nested-null", nullArm, in obligation, in binding, in shuffledBinding, shuffledBinding.Candidate, admitRepair: false);
            rows.Add(realRow);
            rows.Add(nullRow);
            realProposals += realRow.Proposals;
            realAdmissions += realRow.AdmittedRepairs;
            nullProposals += nullRow.Proposals;
            nullAdmissions += nullRow.AdmittedRepairs;
            realCalls += realRow.EvaluatorCalls;
            nullCalls += nullRow.EvaluatorCalls;
            realFuel += realRow.ProcessFuel;
            nullFuel += nullRow.ProcessFuel;
            certificates &= realRow.CertificateCheck && nullRow.CertificateCheck;
        }

        byte[] admitted = real.CaptureAdmissionState();
        EmlSieve loaded = EmlRematchFixture.CloneSieve(signatureDigits, admitted);
        bool checkpointIdentity = admitted.AsSpan().SequenceEqual(loaded.CaptureAdmissionState());
        string digest = Convert.ToHexStringLower(SHA256.HashData(admitted));
        bool accepted = realProposals == claims.Length
            && realAdmissions == claims.Length
            && nullProposals == 0
            && nullAdmissions == 0
            && realFuel == nullFuel
            && certificates
            && checkpointIdentity;
        for (int i = 0; i < rows.Count; i++)
            AppendArmRow(report, -2, rows[i], admitted.Length, digest, checkpointIdentity);
        report.Append("summary\t-2\tnested-log-ratio\t\t\t\t\t\t\t\t\t\t")
            .Append(realProposals).Append('/').Append(nullProposals).Append('\t')
            .Append(realAdmissions).Append('/').Append(nullAdmissions).Append('\t')
            .Append(realCalls).Append('/').Append(nullCalls).Append('\t')
            .Append(realFuel).Append('/').Append(nullFuel).Append('\t')
            .Append(certificates ? 1 : 0).Append('\t')
            .Append(admitted.Length).Append('\t').Append(digest).Append('\t')
            .Append(checkpointIdentity ? 1 : 0).Append('\t')
            .Append(accepted ? "accepted" : "rejected").AppendLine();
        return accepted;
    }

    private static bool AppendFiniteExpressibilityFalsifier(
        StringBuilder report,
        EmlRematchFixture fixture,
        int signatureDigits)
    {
        EmlHoleCandidate xCandidate = new(FiniteNegativeLogXProgram, "derived:k51:negative-log:x", 51);
        EmlHoleCandidate yCandidate = new(FiniteNegativeLogYProgram, "derived:k51:negative-log:y", 51);
        List<FiniteBinding> bindings = BindFiniteCandidates(fixture, xCandidate, yCandidate);
        EmlSieve real = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        EmlSieve shuffled = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        byte[] realStart = real.CaptureAdmissionState();
        byte[] shuffledStart = shuffled.CaptureAdmissionState();
        if (!realStart.AsSpan().SequenceEqual(shuffledStart))
            throw new InvalidDataException("real and shuffled finite rematch arms did not start from identical sieve state");

        List<RematchArmRow> rows = new(RequiredObligations * 2);
        int realBindings = 0;
        int shuffledBindings = 0;
        int realAdmissions = 0;
        int shuffledAdmissions = 0;
        long realEvaluatorCalls = 0;
        long shuffledEvaluatorCalls = 0;
        long realProcessFuel = 0;
        long shuffledProcessFuel = 0;
        bool certificateChecksPassed = true;
        for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
        {
            FiniteBinding binding = bindings[bindingIndex];
            EmlObligationResolution obligation = binding.Obligation;
            EmlHoleCandidate shuffledCandidate = binding.Candidate.Program == xCandidate.Program ? yCandidate : xCandidate;
            RematchArmRow realRow = ExecuteFiniteArm(
                "finite-real",
                real,
                in obligation,
                binding.Candidate,
                shuffledCandidate,
                binding.Candidate,
                binding.Input,
                expectBinding: true);
            RematchArmRow shuffledRow = ExecuteFiniteArm(
                "finite-shuffled",
                shuffled,
                in obligation,
                binding.Candidate,
                shuffledCandidate,
                shuffledCandidate,
                binding.Input,
                expectBinding: false);
            rows.Add(realRow);
            rows.Add(shuffledRow);
            realBindings = checked(realBindings + realRow.Proposals);
            shuffledBindings = checked(shuffledBindings + shuffledRow.Proposals);
            realAdmissions = checked(realAdmissions + realRow.AdmittedRepairs);
            shuffledAdmissions = checked(shuffledAdmissions + shuffledRow.AdmittedRepairs);
            realEvaluatorCalls = checked(realEvaluatorCalls + realRow.EvaluatorCalls);
            shuffledEvaluatorCalls = checked(shuffledEvaluatorCalls + shuffledRow.EvaluatorCalls);
            realProcessFuel = checked(realProcessFuel + realRow.ProcessFuel);
            shuffledProcessFuel = checked(shuffledProcessFuel + shuffledRow.ProcessFuel);
            certificateChecksPassed &= realRow.CertificateCheck && shuffledRow.CertificateCheck;
        }

        byte[] admittedImage = real.CaptureAdmissionState();
        EmlSieve reloaded = EmlRematchFixture.CloneSieve(signatureDigits, admittedImage);
        byte[] resavedImage = reloaded.CaptureAdmissionState();
        bool saveLoadSaveIdentity = admittedImage.AsSpan().SequenceEqual(resavedImage);
        string checkpointDigest = Convert.ToHexStringLower(SHA256.HashData(admittedImage));
        bool accepted = realBindings == RequiredObligations
            && shuffledBindings == 0
            && realAdmissions == RequiredObligations
            && shuffledAdmissions == 0
            && realEvaluatorCalls == shuffledEvaluatorCalls
            && realProcessFuel == 0
            && shuffledProcessFuel == 0
            && certificateChecksPassed
            && saveLoadSaveIdentity;

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            AppendArmRow(report, -1, rows[rowIndex], admittedImage.Length, checkpointDigest, saveLoadSaveIdentity);
        report.Append("summary\t-1\tdirect-finite-eml\t\t\t\t\t\t\t\t\t\t")
            .Append(realBindings).Append('/').Append(shuffledBindings).Append('\t')
            .Append(realAdmissions).Append('/').Append(shuffledAdmissions).Append('\t')
            .Append(realEvaluatorCalls).Append('/').Append(shuffledEvaluatorCalls).Append('\t')
            .Append(realProcessFuel).Append('/').Append(shuffledProcessFuel).Append('\t')
            .Append(certificateChecksPassed ? 1 : 0).Append('\t')
            .Append(admittedImage.Length).Append('\t').Append(checkpointDigest).Append('\t')
            .Append(saveLoadSaveIdentity ? 1 : 0).Append('\t')
            .Append(accepted ? "accepted" : "rejected").AppendLine();
        return accepted;
    }

    private static List<FiniteBinding> BindFiniteCandidates(
        EmlRematchFixture fixture,
        EmlHoleCandidate xCandidate,
        EmlHoleCandidate yCandidate)
    {
        List<FiniteBinding> bindings = new(fixture.Obligations.Count);
        EmlHoleCandidate[] candidates = [xCandidate, yCandidate];
        for (int obligationIndex = 0; obligationIndex < fixture.Obligations.Count; obligationIndex++)
        {
            EmlObligationResolution obligation = fixture.Obligations[obligationIndex];
            string sourceText = fixture.Sieve.MintLog[obligation.SourcePredictionID.Value].Line;
            char input = GetResidualInput(sourceText);
            int candidateIndex = input == Eml.VarX ? 0 : 1;
            bindings.Add(new FiniteBinding(obligation, candidates[candidateIndex], input));
        }
        return bindings;
    }

    private static char GetResidualInput(string sourceText)
    {
        if (!EmlPrediction.TryParse(sourceText, out EmlPrediction claim)
            || !claim.RhsRpn
            || claim.Lhs.Length != claim.Rhs.Length)
            throw new InvalidDataException($"finite residual source does not expose a matched RPN binding: {sourceText}");
        char input = '\0';
        for (int tokenIndex = 0; tokenIndex < claim.Lhs.Length; tokenIndex++)
        {
            if (claim.Lhs[tokenIndex] == claim.Rhs[tokenIndex]) continue;
            char candidate = claim.Lhs[tokenIndex];
            if (claim.Rhs[tokenIndex] != Eml.One
                || candidate is not Eml.VarX and not Eml.VarY
                || input != '\0')
                throw new InvalidDataException($"finite residual source has no unique variable-to-one binding: {sourceText}");
            input = candidate;
        }
        if (input == '\0')
            throw new InvalidDataException($"finite residual source has no variable-to-one binding: {sourceText}");
        return input;
    }

    private static RematchArmRow ExecuteFiniteArm(
        string arm,
        EmlSieve sieve,
        in EmlObligationResolution obligation,
        EmlHoleCandidate binding,
        EmlHoleCandidate shuffledBinding,
        EmlHoleCandidate usedCandidate,
        char input,
        bool expectBinding)
    {
        EmlEvaluatorClock evaluatorClock = sieve.EvaluatorClock;
        long evaluatorStart = sieve.EvaluatorClock.ProgramPointEvaluations;
        EmlGrader grader = new(evaluatorClock);
        Func<Complex, Complex, Complex> reference = input == Eml.VarX
            ? EvaluateNegativeLogX
            : EvaluateNegativeLogY;
        EmlVerdict verdict = grader.GradeRef(usedCandidate.Program, reference);
        bool admitted = sieve.TryAdmitResidualProof(
            obligation.SourcePredictionID,
            usedCandidate.Program,
            evaluatorStart,
            out EmlCertificateDelta _);
        EmlResidualWitness targetWitness = CreateNegativeLogWitness(grader, reference);
        string sourceText = sieve.MintLog[obligation.SourcePredictionID.Value].Line;
        bool matched = verdict.Grade == 'E';
        return new RematchArmRow(
            arm,
            obligation,
            targetWitness,
            sourceText,
            binding.Program,
            shuffledBinding.Program,
            usedCandidate.Program,
            matched ? 1 : 0,
            admitted ? 1 : 0,
            evaluatorClock.ProgramPointEvaluations - evaluatorStart,
            0,
            matched == expectBinding && admitted == expectBinding);
    }

    private static EmlResidualWitness CreateNegativeLogWitness(
        EmlGrader grader,
        Func<Complex, Complex, Complex> reference)
    {
        (Complex X, Complex Y)[] points = grader.Points;
        if (points.Length != 3)
            throw new InvalidDataException($"finite residual rematch requires exactly three grader probes; found {points.Length}");
        return new EmlResidualWitness(
            new EmlResidualProbe(reference(points[0].X, points[0].Y), default),
            new EmlResidualProbe(reference(points[1].X, points[1].Y), default),
            new EmlResidualProbe(reference(points[2].X, points[2].Y), default));
    }

    private static Complex EvaluateNegativeLogX(Complex x, Complex y) => -Complex.Log(x);

    private static Complex EvaluateNegativeLogY(Complex x, Complex y) => -Complex.Log(y);

    private static List<ProcessBinding> BindProcessCandidates(EmlRematchFixture fixture, long processFuel)
        => BindStructuralCandidates(fixture.Sieve, fixture.Obligations, processFuel);

    private static List<ProcessBinding> BindStructuralCandidates(
        EmlSieve sieve,
        IReadOnlyList<EmlObligationResolution> obligations,
        long processFuel)
    {
        List<ProcessBinding> bindings = new(obligations.Count);
        for (int obligationIndex = 0; obligationIndex < obligations.Count; obligationIndex++)
        {
            EmlObligationResolution obligation = obligations[obligationIndex];
            EmlMint mint = sieve.MintLog[obligation.SourcePredictionID.Value];
            if (!EmlPrediction.TryParse(mint.Line, out EmlPrediction claim)
                || !EmlResidualDeriver.TryDeriveSharedExponentialArgument(
                    obligation.SourcePredictionID,
                    in claim,
                    processFuel,
                    out EmlResidualComposition derivation))
                throw new InvalidDataException($"obligation {obligation.SourcePredictionID.Value} has no shared-exponential residual derivation: {mint.Line}");
            EmlProcessFunction process = derivation.Process;
            EmlHoleCandidate candidate = new(
                EmlResidualExpression.CreateProcessFunction(in process),
                "process:" + derivation.Receipt,
                checked(1 + derivation.NumeratorRPN.Length + derivation.DenominatorRPN.Length),
                derivation);
            List<EmlHoleRepairProposal> proposals = new();
            EmlResidualWitness witness = obligation.Corroboration;
            EmlHoleSolver.SolveAgainstWitness(
                sieve.MintLog,
                in obligation,
                in witness,
                [candidate],
                proposals,
                new EmlEvaluatorClock(),
                branchRadius: 0);
            if (proposals.Count != 1)
            {
                EmlProcessFunctionCertificate certificate = EmlProcessFunctions.Certify(in process);
                EmlResidualWitness expectedWitness = obligation.Corroboration;
                string verification = EmlProcessResidualVerifier.Describe(
                    obligation.SourcePredictionID,
                    in claim,
                    in expectedWitness,
                    in process,
                    derivation,
                    EmlSieve.LabelChart());
                throw new InvalidDataException(
                    $"obligation {obligation.SourcePredictionID.Value} structural log-ratio produced {proposals.Count} repairs; expected one; "
                    + $"claim={mint.Line}; process={candidate.Program}; "
                    + $"target={FormatComplex(obligation.Corroboration.P1.Value)},{FormatComplex(obligation.Corroboration.P2.Value)},{FormatComplex(obligation.Corroboration.P3.Value)}; "
                    + $"candidate={FormatComplex(certificate.P1.Value)},{FormatComplex(certificate.P2.Value)},{FormatComplex(certificate.P3.Value)}; "
                    + $"verification={verification}");
            }
            bindings.Add(new ProcessBinding(obligation, candidate));
        }
        return bindings;
    }

    private static int[] CreateDerangement(List<ProcessBinding> bindings, ulong seed)
    {
        int[] assignment = new int[bindings.Count];
        Array.Fill(assignment, -1);
        bool[] used = new bool[bindings.Count];
        if (!TryAssignDerangement(bindings, seed, 0, assignment, used))
            throw new InvalidDataException("bound process candidates cannot be deranged across distinct residual signatures");
        return assignment;
    }

    private static bool TryAssignDerangement(
        List<ProcessBinding> bindings,
        ulong seed,
        int sourceIndex,
        int[] assignment,
        bool[] used)
    {
        if (sourceIndex == bindings.Count) return true;
        int start = (int)((seed >> ((sourceIndex * 11) & 63)) % (ulong)bindings.Count);
        for (int offset = 0; offset < bindings.Count; offset++)
        {
            int candidateIndex = (start + offset) % bindings.Count;
            if (used[candidateIndex] || candidateIndex == sourceIndex) continue;
            if (bindings[sourceIndex].Obligation.ResidualSignature == bindings[candidateIndex].Obligation.ResidualSignature) continue;
            if (string.Equals(
                    bindings[sourceIndex].Candidate.Program,
                    bindings[candidateIndex].Candidate.Program,
                    StringComparison.Ordinal)) continue;
            assignment[sourceIndex] = candidateIndex;
            used[candidateIndex] = true;
            if (TryAssignDerangement(bindings, EmlGen.Lcg(seed), sourceIndex + 1, assignment, used)) return true;
            used[candidateIndex] = false;
            assignment[sourceIndex] = -1;
        }
        return false;
    }

    private static RematchArmRow ExecuteProcessArm(
        string arm,
        EmlSieve sieve,
        in EmlObligationResolution obligation,
        in ProcessBinding binding,
        in ProcessBinding shuffledBinding,
        EmlHoleCandidate usedCandidate,
        bool admitRepair)
    {
        EmlProcessFunction usedFunction = GetProcessFunction(usedCandidate);
        EmlProcessFunctionCertificate certificate = EmlProcessFunctions.Certify(in usedFunction);
        EmlProcessFunctionCheck certificateCheck = EmlProcessFunctionChecker.Check(in certificate);
        List<EmlHoleCandidate> candidates = [usedCandidate];
        List<EmlHoleRepairProposal> proposals = new();
        long evaluatorStart = sieve.EvaluatorClock.ProgramPointEvaluations;
        EmlResidualWitness witness = obligation.Corroboration;
        EmlHoleSolveResult solve = EmlHoleSolver.SolveAgainstWitness(
            sieve.MintLog,
            in obligation,
            in witness,
            candidates,
            proposals,
            sieve.EvaluatorClock,
            branchRadius: 0,
            grader: sieve.Grader);
        string sourceText = sieve.MintLog[obligation.SourcePredictionID.Value].Line;
        int admittedRepairs = 0;
        if (admitRepair)
        {
            for (int proposalIndex = 0; proposalIndex < proposals.Count; proposalIndex++)
            {
                EmlHoleRepairProposal proposal = proposals[proposalIndex];
                if (!proposal.Expression.TryGetProcessFunction(out EmlProcessFunction proposalFunction)) continue;
                if (!sieve.TryAdmitProcessResidualProof(
                        obligation.SourcePredictionID,
                        in proposalFunction,
                        proposal.Composition,
                        evaluatorStart,
                        out EmlCertificateDelta ignored,
                        out long admittedFuel)) continue;
                if (admittedFuel != proposal.Work.ProcessFuel)
                    throw new InvalidDataException("process residual admission consumed different fuel than its verified proposal");
                admittedRepairs++;
                break;
            }
        }
        return new RematchArmRow(
            arm,
            obligation,
            obligation.Corroboration,
            sourceText,
            binding.Candidate.Program,
            shuffledBinding.Candidate.Program,
            usedCandidate.Program,
            proposals.Count,
            admittedRepairs,
            solve.Work.EvaluatorCalls,
            solve.Work.ProcessFuel,
            certificateCheck.Accepted);
    }

    private static EmlProcessFunction GetProcessFunction(EmlHoleCandidate candidate)
    {
        if (!candidate.Expression.TryGetProcessFunction(out EmlProcessFunction function))
            throw new InvalidDataException("process rematch binding is not a process function");
        return function;
    }

    private static void AppendArmRow(
        StringBuilder report,
        int replicate,
        RematchArmRow row,
        int checkpointBytes,
        string checkpointDigest,
        bool saveLoadSaveIdentity)
    {
        EmlResidualWitness witness = row.TargetWitness;
        report.Append("replicate\t").Append(replicate).Append('\t').Append(row.Arm).Append('\t')
            .Append(row.Obligation.SourcePredictionID.Value).Append('\t')
            .Append(EscapeTSV(row.SourceText)).Append('\t')
            .Append(CreateResidualSignature(row.Obligation.ResidualSignature)).Append('\t')
            .Append(row.Binding).Append('\t').Append(row.ShuffledBinding).Append('\t').Append(row.UsedBinding).Append('\t')
            .Append(FormatComplex(witness.P1.Value)).Append('\t')
            .Append(FormatComplex(witness.P2.Value)).Append('\t')
            .Append(FormatComplex(witness.P3.Value)).Append('\t')
            .Append(row.Proposals).Append('\t').Append(row.AdmittedRepairs).Append('\t')
            .Append(row.EvaluatorCalls).Append('\t').Append(row.ProcessFuel).Append('\t')
            .Append(row.CertificateCheck ? 1 : 0).Append('\t')
            .Append(checkpointBytes).Append('\t').Append(checkpointDigest).Append('\t')
            .Append(saveLoadSaveIdentity ? 1 : 0).Append("\tlive").AppendLine();
    }

    private static void AppendBankedBoundedSupplyBaseline(StringBuilder report)
    {
        report.Append("baseline\t-1\tbanked-bounded-supply\t\tK<=7 canonical supply plus bounded joins\t\t\t\t\t\t\t\t")
            .Append(BankedBoundedSupplyProposals).Append('\t').Append(BankedBoundedSupplyAdmissions)
            .Append("\t\t\t\t\t\t\tbanked-not-live").AppendLine();
    }

    private static string CreateResidualSignature(EmlSig signature)
        => new EmlCert('E', signature, 0, 0).Hex();

    private static string FormatComplex(Complex value)
        => value.Real.ToString("R", CultureInfo.InvariantCulture)
            + (value.Imaginary < 0 ? "" : "+")
            + value.Imaginary.ToString("R", CultureInfo.InvariantCulture) + "i";

    private static bool NearlyEqual(double actual, double expected)
        => Math.Abs(actual - expected) <= Math.Max(1e-24, Math.Abs(expected) * 1e-12);

    private static string EscapeTSV(string value)
        => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static ulong MixReplicateSeed(ulong seed, int replicate)
    {
        ulong mixed = seed ^ unchecked((ulong)(uint)replicate * 0x9E3779B97F4A7C15UL);
        mixed ^= mixed >> 30;
        mixed *= 0xBF58476D1CE4E5B9UL;
        mixed ^= mixed >> 27;
        mixed *= 0x94D049BB133111EBUL;
        return mixed ^ (mixed >> 31);
    }

    private readonly record struct ProcessBinding(
        EmlObligationResolution Obligation,
        EmlHoleCandidate Candidate);

    private readonly record struct FiniteBinding(
        EmlObligationResolution Obligation,
        EmlHoleCandidate Candidate,
        char Input);

    private readonly record struct RematchArmRow(
        string Arm,
        EmlObligationResolution Obligation,
        EmlResidualWitness TargetWitness,
        string SourceText,
        string Binding,
        string ShuffledBinding,
        string UsedBinding,
        int Proposals,
        int AdmittedRepairs,
        long EvaluatorCalls,
        long ProcessFuel,
        bool CertificateCheck);
}
