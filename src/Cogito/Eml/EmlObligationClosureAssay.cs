namespace Cogito;

using System.Text;

internal static class EmlObligationClosureAssay
{
    private const string FiniteNegativeLogXProgram = "111E1EE1111EE1EE111111EE1EE11EEE1EE11xE1EE1EE1EE1EE";
    private const string FiniteNegativeLogYProgram = "111E1EE1111EE1EE111111EE1EE11EEE1EE11yE1EE1EE1EE1EE";

    public static int Run(int signatureDigits)
    {
        EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
        if (fixture.Obligations.Count == 0) throw new InvalidDataException("obligation closure assay fixture is empty");
        EmlObligationResolution obligation = fixture.Obligations[0];
        EmlMint source = fixture.Sieve.MintLog[obligation.SourcePredictionID.Value];
        if (!EmlPrediction.TryParse(source.Line, out EmlPrediction claim)) throw new InvalidDataException("assay source claim is malformed");
        if (!EmlResidualDeriver.TryDeriveSharedExponentialArgument(
            obligation.SourcePredictionID, in claim, 32, out EmlResidualComposition derivation))
            throw new InvalidDataException("assay fixture did not expose a process residual");

        StringBuilder report = new("case\tstatus\tevaluator_calls\tfuel_per_probe\tfuel_total\tclosures\tidentity\n");
        EmlSieve finiteSieve = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        string finiteProgram = derivation.Process.DenominatorRPN == "x" ? FiniteNegativeLogXProgram : FiniteNegativeLogYProgram;
        EmlObligationClosureResult finite = finiteSieve.AdmitResidualProof(
            obligation.SourcePredictionID, finiteProgram, finiteSieve.EvaluatorClock.ProgramPointEvaluations);
        Require(finite.Accepted, "finite residual proof did not close the fixture obligation");
        Append(report, "finite", finite, finite.Closure.FiniteEvidence?.Evaluator.Calls ?? 0);
        byte[] finiteImage = finiteSieve.CaptureAdmissionState();
        EmlSieve finiteReloaded = EmlRematchFixture.CloneSieve(signatureDigits, finiteImage);
        byte[] finiteReloadedImage = finiteReloaded.CaptureAdmissionState();
        Require(finiteImage.AsSpan().SequenceEqual(finiteReloadedImage), "finite closure SaveLoadSave drifted " + DescribeDrift(finiteImage, finiteReloadedImage));

        EmlSieve zero = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        long zeroBefore = zero.EvaluatorClock.ProgramPointEvaluations;
        EmlObligationClosureResult wrongKind = zero.RejectWrongObligationKind(
            obligation.SourcePredictionID, EmlObligationProofKinds.ProcessFunction,
            EmlObligationProofKinds.FiniteRPN, zeroBefore);
        Require(wrongKind.Closure.Status == EmlObligationClosureStatuses.WrongKind, "wrong-kind proof did not reject before work");
        Require(zero.EvaluatorClock.ProgramPointEvaluations == zeroBefore, "wrong-kind proof consumed evaluator work");
        Append(report, "wrong-kind", wrongKind, 0);
        EmlProcessFunction zeroFunction = new(
            derivation.Process.Algorithm, derivation.Process.Version,
            derivation.Process.NumeratorRPN, derivation.Process.DenominatorRPN, 0);
        EmlObligationClosureResult zeroResult = zero.AdmitProcessResidualProof(
            obligation.SourcePredictionID, in zeroFunction, derivation, zeroBefore);
        Require(zeroResult.Closure.Status == EmlObligationClosureStatuses.InvalidPolicy, "fuel0 must reject before work");
        Require(zero.EvaluatorClock.ProgramPointEvaluations == zeroBefore, "fuel0 consumed evaluator work");
        Append(report, "fuel0", zeroResult, zero.EvaluatorClock.ProgramPointEvaluations - zeroBefore);

        EmlObligationClosureResult accepted = default;
        EmlSieve acceptedSieve = zero;
        bool sawFuel1 = false, sawFuel32 = false;
        foreach (long fuel in new long[] { 1, 32 })
        {
            EmlSieve candidate = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
            EmlProcessFunction function = new(
                derivation.Process.Algorithm, derivation.Process.Version,
                derivation.Process.NumeratorRPN, derivation.Process.DenominatorRPN, fuel);
            accepted = candidate.AdmitProcessResidualProof(
                obligation.SourcePredictionID, in function, derivation with { Process = function },
                candidate.EvaluatorClock.ProgramPointEvaluations);
            Append(report, "fuel" + fuel, accepted, accepted.Closure.ProcessEvidence?.Evaluator.Calls ?? 0);
            if (fuel == 1) sawFuel1 = accepted.Accepted;
            if (fuel == 32) sawFuel32 = accepted.Accepted;
            if (accepted.Accepted) acceptedSieve = candidate;
        }
        Require(sawFuel1 && sawFuel32, "fuel1/fuel32 did not close the process obligation");
        EmlProcessObligationProofEvidence acceptedEvidence = accepted.Closure.ProcessEvidence
            ?? throw new InvalidDataException("accepted process closure has no typed evidence");
        Require(acceptedEvidence.FuelTotal == checked(acceptedEvidence.FuelPerProbe * 3), "process fuel journal is not 3 probes");

        byte[] acceptedImage = acceptedSieve.CaptureAdmissionState();
        string[] corruptionMarkers =
        [
            accepted.Closure.ObligationID,
            accepted.Closure.AttemptID,
            accepted.Closure.AttachmentID,
            accepted.Closure.SourceDigest,
            "accepted",
            accepted.Closure.ProcessPolicy?.VerifierRevision ?? "",
            acceptedEvidence.CertificateDigest,
        ];
        int corruptionCases = 0;
        for (int i = 0; i < corruptionMarkers.Length; i++)
        {
            if (corruptionMarkers[i].Length == 0) continue;
            byte[] marker = Encoding.UTF8.GetBytes(corruptionMarkers[i]);
            int offset = FindBytes(acceptedImage, marker);
            Require(offset >= 0, "assay could not locate corruption marker " + corruptionMarkers[i]);
            AssertRejects(signatureDigits, acceptedImage, offset, "corrupt-" + i);
            corruptionCases++;
        }
        int candidateOffset = FindBytes(acceptedImage, Encoding.UTF8.GetBytes(acceptedEvidence.CertificateDigest));
        int attachmentOffset = FindBytes(acceptedImage, Encoding.UTF8.GetBytes(accepted.Closure.AttachmentID));
        Require(candidateOffset >= 0 && attachmentOffset >= 0, "assay could not locate coordinated candidate/attachment fields");
        AssertRejectsOffsets(signatureDigits, acceptedImage, [candidateOffset, attachmentOffset], "corrupt-coordinated-candidate-attachment");
        corruptionCases++;
        byte[] fuelMarker = BitConverter.GetBytes(acceptedEvidence.FuelPerProbe);
        int fuelOffset = FindBytes(acceptedImage, fuelMarker);
        Require(fuelOffset >= 0, "assay could not locate process fuel policy");
        AssertRejects(signatureDigits, acceptedImage, fuelOffset, "corrupt-fuel");
        corruptionCases++;
        report.Append("corruption-matrix\taccepted\t").Append(corruptionCases).AppendLine();

        EmlSieve duplicateSieve = EmlRematchFixture.CloneSieve(signatureDigits, acceptedSieve.CaptureAdmissionState());
        EmlProcessObligationProofPolicy acceptedPolicy = accepted.Closure.ProcessPolicy
            ?? throw new InvalidDataException("accepted process closure has no policy");
        EmlProcessFunction duplicateFunction = new(
            derivation.Process.Algorithm, derivation.Process.Version,
            derivation.Process.NumeratorRPN, derivation.Process.DenominatorRPN,
            acceptedPolicy.FuelPerProbe);
        long duplicateBefore = duplicateSieve.EvaluatorClock.ProgramPointEvaluations;
        EmlObligationClosureResult duplicate = duplicateSieve.AdmitProcessResidualProof(
            obligation.SourcePredictionID, in duplicateFunction, derivation, duplicateBefore);
        Require(duplicate.Closure.Status == EmlObligationClosureStatuses.DuplicateAttachment, "duplicate proof did not return idempotent rejection");
        Require(duplicateSieve.EvaluatorClock.ProgramPointEvaluations == duplicateBefore, "duplicate proof consumed evaluator work");
        Append(report, "duplicate", duplicate, 0);

        byte[] image = acceptedSieve.CaptureAdmissionState();
        EmlSieve reloaded = EmlRematchFixture.CloneSieve(signatureDigits, image);
        byte[] reloadedImage = reloaded.CaptureAdmissionState();
        Require(image.AsSpan().SequenceEqual(reloadedImage), "obligation closure SaveLoadSave drifted " + DescribeDrift(image, reloadedImage));
        Require(RunExactCompositionFixture(signatureDigits, report), "exact derivation target fixture failed");
        Console.Write(report.ToString());
        Console.WriteLine("obligation-closure-assay\taccepted");
        return 0;
    }

    private static bool RunExactCompositionFixture(int signatureDigits, StringBuilder report)
    {
        EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
        EmlSieve sieve = fixture.Sieve;
        EmlPredictionID sourceID = new(-1);
        for (int i = 0; i < sieve.MintLog.Count; i++)
        {
            EmlMint mint = sieve.MintLog[i];
            if (mint.Grade == 'E' && EmlPrediction.TryParse(mint.Line, out EmlPrediction claim)
                && claim.RhsRpn && (claim.Lhs == "11xE1EE1E" || claim.Lhs == "11yE1EE1E"))
            {
                sourceID = new EmlPredictionID(i);
                break;
            }
        }
        if (sourceID.Value < 0) throw new InvalidDataException("exact fixture: no exact source claim");
        TapeEventID support = new(7001);
        TapeEventID mintEvent = new(7004);
        sieve.BindPredictionEvent(sourceID, mintEvent);
        EmlSieve arbitraryEventSieve = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        arbitraryEventSieve.BindPredictionEvent(sourceID, mintEvent);
        bool arbitraryEventRejected = !arbitraryEventSieve.RegisterExactCompositionObligation(sourceID, [new TapeEventID(7009)], mintEvent);
        EmlSieve duplicateEventSieve = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        duplicateEventSieve.BindPredictionEvent(sourceID, mintEvent);
        bool duplicateEventRejected = !duplicateEventSieve.RegisterExactCompositionObligation(sourceID, [support, support], mintEvent);
        if (!arbitraryEventRejected || !duplicateEventRejected)
            throw new InvalidDataException("exact fixture: unbound or duplicate support event was accepted");
        if (!sieve.RegisterExactCompositionObligation(sourceID, [support], mintEvent)
            || !sieve.TryReadExactCompositionObligation(sourceID, out EmlExactCompositionObligation target)) throw new InvalidDataException("exact fixture: target registration failed");
        EmlPredictionID donorID = new(-1);
        for (int i = 0; i < sieve.MintLog.Count; i++)
        {
            EmlMint mint = sieve.MintLog[i];
            if (i != sourceID.Value && mint.Grade == 'E' && EmlPrediction.TryParse(mint.Line, out EmlPrediction donorPrediction) && donorPrediction.RhsRpn
                && sieve.TryReadMintOpportunityEvents(new EmlPredictionID(i), out IReadOnlyList<TapeEventID> donorEvents)
                && donorEvents.SequenceEqual([new TapeEventID(7002)]))
            {
                donorID = new EmlPredictionID(i);
                break;
            }
        }
        if (donorID.Value < 0) throw new InvalidDataException("exact fixture: no second exact donor claim");
        sieve.BindPredictionEvent(donorID, new TapeEventID(7003));
        if (!sieve.RegisterExactCompositionObligation(donorID, [new TapeEventID(7002)], new TapeEventID(7003)))
            throw new InvalidDataException("exact fixture: donor target registration failed");
        EmlMint sourceMint = sieve.MintLog[sourceID.Value];
        bool claimRebindRejected = RejectsExactEventRebind(() => sieve.BindPredictionEvent(sourceID, new TapeEventID(7005)));
        bool mintRebindRejected = RejectsExactEventRebind(() => sieve.BindMintEvent(in sourceMint, new TapeEventID(7006)));
        byte[] eventImage = sieve.CaptureAdmissionState();
        EmlSieve eventRestored = EmlRematchFixture.CloneSieve(signatureDigits, eventImage);
        bool restoredPredictionRebindRejected = RejectsExactEventRebind(() => eventRestored.BindPredictionEvent(sourceID, new TapeEventID(7007)));
        bool restoredMintRebindRejected = RejectsExactEventRebind(() => eventRestored.BindMintEvent(in sourceMint, new TapeEventID(7008)));
        bool eventSaveLoadSave = eventImage.AsSpan().SequenceEqual(eventRestored.CaptureAdmissionState());
        EmlLawStore store = new();
        EmlVerifiedLaw law = EmlGuardedRewriteAssay.CreateVerifiedLaw();
        if (!store.TryAdmit(law, 0, out _)) throw new InvalidDataException("exact fixture: law admission failed");
        List<EmlLawCandidateInstantiation> candidates = new();
        store.AppendExactPredictionBoundCandidateRewrites(in target, sieve, candidates);
        if (candidates.Count == 0) throw new InvalidDataException("exact fixture: no guarded rank-reducing rewrite");
        EmlLawCandidateInstantiation candidate = candidates[0];
        bool relationNullExecuted = TryExecuteWorldFedRelationNull(sieve, store, in candidate, requireDifferentDonorTarget: true);
        EmlSieve unregisteredDonorSieve = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        unregisteredDonorSieve.BindPredictionEvent(sourceID, mintEvent);
        if (!unregisteredDonorSieve.RegisterExactCompositionObligation(sourceID, [support], mintEvent))
            throw new InvalidDataException("exact fixture: unregistered-donor control source registration failed");
        bool unregisteredDonorRejected = !TryExecuteWorldFedRelationNull(unregisteredDonorSieve, store, in candidate, requireDifferentDonorTarget: true);
        EmlSieve unregisteredExactSieve = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        EmlObligationResolution unregisteredExactAddress = new(
            sourceID, default, "exact-derivation", default, default, 0, target.Supports, target.MintEventID);
        EmlLawCandidateInstantiation unregisteredExactCandidate = new(
            unregisteredExactAddress, candidate.Rewrite, candidate.PredictionCarrier,
            EmlObligationTarget.ExactComposition(sourceID));
        EmlRung0AdmissionResult unregisteredExactAdmission = EmlRung0Admission.TryAdmit(
            unregisteredExactSieve, store, in unregisteredExactCandidate);
        bool unregisteredExactRejected = !unregisteredExactAdmission.Admitted
            && unregisteredExactAdmission.MainEvaluatorDelta == 0
            && unregisteredExactAdmission.FunnelReceipts.Any(static receipt
                => receipt.Stage == EmlRung0FunnelStages.Opportunity
                    && receipt.Reason == "target-unregistered");
        EmlRewritePredictionCarrier forgedCarrier = candidate.PredictionCarrier!.Value with { SourceDigest = "forged-source-digest" };
        EmlLawCandidateInstantiation forgedCarrierCandidate = new(
            candidate.Obligation, candidate.Rewrite, forgedCarrier,
            EmlObligationTarget.ExactComposition(sourceID));
        EmlRung0AdmissionResult forgedCarrierAdmission = EmlRung0Admission.TryAdmit(
            sieve, store, in forgedCarrierCandidate);
        bool forgedCarrierRejected = !forgedCarrierAdmission.Admitted
            && forgedCarrierAdmission.MainEvaluatorDelta == 0
            && forgedCarrierAdmission.FunnelReceipts.Any(static receipt
                => receipt.Stage == EmlRung0FunnelStages.Opportunity
                    && receipt.Reason == "exact-carrier-mismatch");
        EmlLawCandidateInstantiation unknownSpeciesCandidate = new(
            candidate.Obligation, candidate.Rewrite, candidate.PredictionCarrier,
            new EmlObligationTarget((EmlObligationTargetSpecies)255, sourceID));
        EmlRung0AdmissionResult unknownSpeciesAdmission = EmlRung0Admission.TryAdmit(
            sieve, store, in unknownSpeciesCandidate);
        bool unknownSpeciesRejected = !unknownSpeciesAdmission.Admitted
            && unknownSpeciesAdmission.MainEvaluatorDelta == 0
            && unknownSpeciesAdmission.FunnelReceipts.Any(static receipt
                => receipt.Stage == EmlRung0FunnelStages.Opportunity
                    && receipt.Reason == "target-species-unknown");
        EmlObligationResolution crossObligation = fixture.Obligations.First(obligation => obligation.SourcePredictionID != sourceID);
        EmlLawCandidateInstantiation crossSource = new(crossObligation, candidate.Rewrite, candidate.PredictionCarrier,
            EmlObligationTarget.ExactComposition(sourceID));
        byte[] crossBefore = sieve.CaptureAdmissionState();
        EmlRung0AdmissionResult crossRejected = EmlRung0Admission.TryAdmit(sieve, store, in crossSource);
        bool crossIDRejected = !crossRejected.Admitted && crossRejected.MainEvaluatorDelta == 0
            && crossBefore.AsSpan().SequenceEqual(sieve.CaptureAdmissionState());
        long before = sieve.EvaluatorClock.ProgramPointEvaluations;
        EmlRung0AdmissionResult admission = EmlRung0Admission.TryAdmit(sieve, store, in candidate);
        bool zeroAdmissionDifferential = admission.ClosureProof is EmlRung0ComposedFormProof closureWitness
            && closureWitness.IsExactZeroAdmission;
        bool exactClosed = admission.Admitted
            && admission.MainEvaluatorDelta == 0
            && zeroAdmissionDifferential
            && sieve.IsObligationClosed(sourceID)
            && sieve.ObligationClosures.Any(closure => closure.SourcePredictionID == sourceID
                && closure.Species == EmlObligationTargetSpecies.ExactComposition
                && closure.Kind == EmlObligationProofKinds.Rung0ComposedForm);
        bool residualUnclosed = fixture.Obligations.All(obligation => !sieve.IsObligationClosed(obligation.SourcePredictionID));
        EmlLawCandidateInstantiation mismatch = new(candidate.Obligation, candidate.Rewrite, candidate.PredictionCarrier);
        EmlRung0AdmissionResult rejected = EmlRung0Admission.TryAdmit(sieve, store, in mismatch);
        bool mismatchRejected = !rejected.Admitted && rejected.MainEvaluatorDelta == 0;
        byte[] image = sieve.CaptureAdmissionState();
        EmlSieve restored = EmlRematchFixture.CloneSieve(signatureDigits, image);
        bool saveLoadSave = image.AsSpan().SequenceEqual(restored.CaptureAdmissionState());
        report.Append("exact-target\t").Append(exactClosed ? "accepted" : "rejected").Append('\t')
            .Append(sieve.ClosureCount(sourceID)).Append('\t').Append(before).Append("\t")
            .Append(mismatchRejected ? "mismatch-rejected" : "MISMATCH-ACCEPTED").Append("\t")
            .Append(saveLoadSave ? "save-load-save" : "SAVE-LOAD-DRIFT").Append("\t")
            .Append(arbitraryEventRejected && duplicateEventRejected && claimRebindRejected && mintRebindRejected
                && restoredPredictionRebindRejected && restoredMintRebindRejected ? "event-binding-rejected" : "EVENT-BINDING-ACCEPTED").Append("\t")
            .Append(eventSaveLoadSave ? "event-save-load-save" : "EVENT-SAVE-LOAD-DRIFT").Append("\t")
            .Append(crossIDRejected ? "cross-id-rejected" : "CROSS-ID-ACCEPTED").Append("\t")
            .Append(unregisteredExactRejected ? "unregistered-exact-rejected" : "UNREGISTERED-EXACT-ACCEPTED").Append("\t")
            .Append(forgedCarrierRejected ? "carrier-mismatch-rejected" : "CARRIER-MISMATCH-ACCEPTED").Append("\t")
            .Append(unknownSpeciesRejected ? "unknown-species-rejected" : "UNKNOWN-SPECIES-ACCEPTED").Append("\t")
            .Append(zeroAdmissionDifferential ? "zero-vs-positive" : "ZERO-DIFFERENTIAL-MISSED").Append("\t")
            .Append(relationNullExecuted ? "relation-null-executed" : "RELATION-NULL-MISSED").Append("\t")
            .Append(unregisteredDonorRejected ? "unregistered-donor-rejected" : "UNREGISTERED-DONOR-ACCEPTED").Append("\t")
            .Append(sieve.Obligations.Count + sieve.ExactCompositionObligations.Count <= 1024 ? "cap-ok" : "CAP-EXCEEDED").AppendLine();
        return exactClosed && residualUnclosed && mismatchRejected && saveLoadSave && arbitraryEventRejected && duplicateEventRejected
            && claimRebindRejected && mintRebindRejected && restoredPredictionRebindRejected && restoredMintRebindRejected && eventSaveLoadSave && crossIDRejected
            && unregisteredExactRejected && forgedCarrierRejected && unknownSpeciesRejected
            && relationNullExecuted
            && unregisteredDonorRejected
            && sieve.Obligations.Count + sieve.ExactCompositionObligations.Count <= 1024;
    }

    private static bool TryExecuteWorldFedRelationNull(
        EmlSieve sieve,
        EmlLawStore store,
        in EmlLawCandidateInstantiation sourceCandidate,
        bool requireDifferentDonorTarget)
    {
        if (sourceCandidate.PredictionCarrier is not EmlRewritePredictionCarrier carrier) return false;
        IReadOnlyList<EmlExactRPNForm> forms = sieve.ExactRPNLhsForms;
        for (int formIndex = 0; formIndex < forms.Count; formIndex++)
        {
            EmlExactRPNForm form = forms[formIndex];
            if ((requireDifferentDonorTarget && form.PredictionID == sourceCandidate.Obligation.SourcePredictionID)
                || !sieve.TryReadExactCompositionObligation(form.PredictionID, out _)
                || !sieve.TryCreateRewriteCarrier(in form, out _)) continue;
            EmlTreeEvaluation evaluation = EmlTree.ParseRPN(form.Program)
                .EvaluateAt(EmlTree.P1.X, EmlTree.P1.Y);
            List<EmlLawRewrite> rewrites = new();
            store.AppendRewritesForEvaluation(form.Program, rewrites, evaluation);
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count; rewriteIndex++)
            {
                EmlLawRewrite donor = rewrites[rewriteIndex];
                if (!donor.IsRung0Eligible || donor.IsRelationNull
                    || !EmlRewriteSystem.ReducesRank(donor.AntecedentRpn, donor.ConsequentRpn)) continue;
                ulong salt = 0xC0C1_7001UL + (ulong)(uint)formIndex;
                EmlLawRewrite sourceRewrite = sourceCandidate.Rewrite;
                if (!EmlLawRewrite.TryCreateRelationNull(
                        in sourceRewrite, in donor, salt, new EmlGrader(), out EmlLawRewrite relationNull)) continue;
                EmlRung0NullExecution execution = store.DeriveRung0Null(
                    in carrier, sourceCandidate.Instantiation.LeftRpn, in relationNull, EmlRung0Budget.Default);
                if (execution.Powered && execution.Work.DidWork) return true;
            }
        }
        return false;
    }

    private static void Append(StringBuilder report, string label, EmlObligationClosureResult result, long evaluatorCalls)
    {
        EmlObligationClosure closure = result.Closure;
        long perProbe = closure.ProcessPolicy?.FuelPerProbe ?? 0;
        long totalFuel = closure.ProcessEvidence?.FuelTotal ?? 0;
        report.Append(label).Append('\t').Append(closure.Status).Append('\t').Append(evaluatorCalls).Append('\t')
            .Append(perProbe).Append('\t').Append(totalFuel).Append('\t').Append(closure.Closed ? 1 : 0).Append('\t')
            .Append(closure.ObligationID).AppendLine();
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidDataException("obligation closure assay failed: " + detail);
    }

    private static bool RejectsExactEventRebind(Action bind)
    {
        try
        {
            bind();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static int FindBytes(byte[] image, byte[] marker)
    {
        for (int i = 0; i <= image.Length - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
                if (image[i + j] != marker[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static string DescribeDrift(byte[] expected, byte[] actual)
    {
        int shared = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < shared; i++)
            if (expected[i] != actual[i]) return $"at={i} expected={expected[i]:X2} actual={actual[i]:X2} expected-window={Window(expected, i)} actual-window={Window(actual, i)} diff-count={CountDiffs(expected, actual)} lengths={expected.Length}/{actual.Length}";
        return $"at=end lengths={expected.Length}/{actual.Length}";
    }

    private static string Window(byte[] image, int at)
    {
        int start = Math.Max(0, at - 16);
        int count = Math.Min(image.Length - start, 32);
        return Convert.ToHexString(image, start, count);
    }

    private static int CountDiffs(byte[] expected, byte[] actual)
    {
        int count = Math.Abs(expected.Length - actual.Length);
        for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++)
            if (expected[i] != actual[i]) count++;
        return count;
    }

    private static void AssertRejects(int signatureDigits, byte[] image, int offset, string label)
        => AssertRejectsOffsets(signatureDigits, image, [offset], label);

    private static void AssertRejectsOffsets(int signatureDigits, byte[] image, int[] offsets, string label)
    {
        byte[] corrupted = (byte[])image.Clone();
        for (int i = 0; i < offsets.Length; i++) corrupted[offsets[i]] ^= (byte)(1 << i);
        try
        {
            _ = EmlRematchFixture.CloneSieve(signatureDigits, corrupted);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException)
        {
            return;
        }
        throw new InvalidDataException("obligation closure assay corruption was accepted: " + label);
    }
}
