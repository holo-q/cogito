namespace Cogito;

using System.Numerics;
using System.Text;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Cogito.Induct;

internal static class EmlRung0Assay
{
    private const string ExpLogX = "11xE1EE1E";
    private const string ExpLogY = "11yE1EE1E";

    public static int Run()
    {
        const int SignatureDigits = 9;
        EmlRematchFixture fixture = EmlRematchFixture.Create(SignatureDigits);
        EmlSieve sieve = fixture.Sieve;
        EmlLawStore store = CreateLawStore();
        if (!sieve.TryCreateRewriteCarrier(ExpLogX, out EmlRewritePredictionCarrier carrier, out _))
            throw new InvalidDataException("rung-0 assay did not find its authoritative exact source claim");

        EmlLawRewrite realRewrite = FindRewrite(store, in carrier, ExpLogX, "x");
        EmlLawCandidateInstantiation candidate = new(default, realRewrite, carrier);
        EmlRung0Proof agreedProof = FindCadenceSelectedProof(store, in carrier, ExpLogX, "x", 0);
        EmlRung0Audit agreedAudit = EmlRung0Auditor.Audit(store, in agreedProof);
        EmlRung0Proof reverseProof = CreatePortableRightToLeftProof(in agreedProof);
        string agreedProofSHA256 = EmlRung0Checkpoint.ProofSHA256(agreedProof);
        string agreedAuditSHA256 = EmlRung0Checkpoint.AuditSHA256(agreedAudit);
        bool canonicalRecordDigests = agreedProofSHA256.Length == 64
            && agreedAuditSHA256.Length == 64
            && agreedProofSHA256 == EmlRung0Checkpoint.ProofSHA256(agreedProof)
            && agreedAuditSHA256 == EmlRung0Checkpoint.AuditSHA256(agreedAudit);
        bool reverseOrientationPortable = reverseProof.IsValidShape
            && EmlRung0Digest.HasPortableStepChain(in reverseProof)
            && string.Equals(
                EmlRung0Digest.DescribeNonPortableStepChain(in reverseProof),
                "portable",
                StringComparison.Ordinal);
        EmlRung0AdmissionPath agreedPath = EmlRung0AdmissionPath.Create(carrier.PredictionID, in agreedProof);
        long mainEvaluatorStart = sieve.EvaluatorClock.ProgramPointEvaluations;
        EmlComposedFormAdmission derivedAdmission = default;
        bool derivedAdmitted = agreedAudit.Status == EmlRung0AuditStatuses.Agreed
            && agreedAudit.EvaluatorCalls > 0
            && reverseOrientationPortable
            && sieve.TryAdmitComposedForm(in agreedProof, in agreedAudit, out derivedAdmission)
            && derivedAdmission.Accepted
            && derivedAdmission.Evaluation.Calls == 0;
        bool admissionPathBound = derivedAdmitted
            && derivedAdmission.AdmissionPath.IsBound
            && derivedAdmission.AdmissionPath.Matches(agreedPath)
            && agreedPath.MatchesProof(in agreedProof);
        EmlEvaluatorClock comparatorClock = new();
        long comparatorStart = comparatorClock.ProgramPointEvaluations;
        EmlVerdict comparatorVerdict = agreedPath.Grade(new EmlGrader(comparatorClock));
        EmlEvaluatorInterval comparatorEvaluation = comparatorClock.MeasureFrom(comparatorStart);
        bool exactAdmissionDifferential = admissionPathBound
            && derivedAdmission.Evaluation.Start == mainEvaluatorStart
            && new EmlRung0AdmissionPathReceipt(
                agreedPath,
                derivedAdmission.Evaluation,
                WorldContacts: 0).IsZeroAdditionalAdmission
            && new EmlRung0AdmissionPathReceipt(
                agreedPath,
                comparatorEvaluation,
                WorldContacts: 0).IsPositiveComparator
            && comparatorVerdict.Grade == 'E';
        long mainEvaluatorDelta = sieve.EvaluatorClock.ProgramPointEvaluations - mainEvaluatorStart;

        EmlRewritePredictionCarrier rejectedCarrier = EmlRewritePredictionCarrier.Create(
            carrier.PredictionID,
            carrier.SourceDigest,
            new Complex(-1, 0),
            Complex.One);
        EmlRung0Budget branchBudget = new(1, 8, 64);
        EmlRung0Result branchRejected = store.DeriveRung0(
            in rejectedCarrier, ExpLogX, "x", in branchBudget);

        EmlDeliberationJournal journal = new();
        EmlObligationResolution meteredObligation = fixture.Obligations.Count > 0
            ? fixture.Obligations[0]
            : default;
        EmlDeliberationQuota quota = new(
            CandidateEvaluations: 0,
            LogicalProgramPoints: 0,
            ExecutedProgramPoints: 0,
            InverseTransforms: 0,
            HashProbes: 0,
            JoinAttempts: 0,
            JoinHits: 0,
            ProcessTerms: 0,
            VerifierProgramPoints: 0,
            CandidateSupplyItems: 0,
            LawRewriteApplications: 1,
            LawRewriteTreeNodes: 256);
        EmlDeliberationLease lease = journal.Reserve(
            in meteredObligation,
            quota,
            "rung0-assay",
            "bounded-exhaustion",
            "rung0-v2",
            "rung0-v2");
        lease.BeginPhase("rung0-derivation");
        EmlRung0Budget exhaustionBudget = new(1, 8, 1);
        EmlRung0Result exhausted = store.DeriveRung0(
            in carrier, ExpLogX, "y", in exhaustionBudget, lease);
        EmlDeliberationSettlement settlement = lease.Complete(EmlDeliberationOutcomes.Exhausted, "bounded-rung0-assay");
        bool exactRefund = exhausted.Status == EmlRung0Statuses.Exhausted
            && exhausted.Work.Applications == 1
            && settlement.Actual.LawRewriteApplications == exhausted.Work.Applications
            && settlement.Actual.LawRewriteTreeNodes > 0
            && settlement.Planned.LawRewriteApplications
                == settlement.Actual.LawRewriteApplications + settlement.Refund.LawRewriteApplications
            && settlement.Planned.LawRewriteTreeNodes
                == settlement.Actual.LawRewriteTreeNodes + settlement.Refund.LawRewriteTreeNodes;

        EmlRung0Proof disagreementProof = FindCadenceSelectedProof(
            store, in carrier, ExpLogX, "x", agreedProof.Digest);
        EmlRung0Audit disagreementAudit = EmlRung0Auditor.Audit(
            store, in disagreementProof, forceDisagreement: true);
        bool allRulesQuarantined = disagreementAudit.Status == EmlRung0AuditStatuses.Disagreed
            && disagreementAudit.EvaluatorCalls > 0
            && disagreementAudit.Rules.Count > 0;
        for (int i = 0; i < disagreementAudit.Rules.Count; i++)
            allRulesQuarantined &= store.IsRung0RuleQuarantined(disagreementAudit.Rules[i]);
        EmlRung0AdmissionResult quarantined = EmlRung0Admission.TryAdmit(
            sieve, store, in candidate, searchBudget: branchBudget);
        long fallbackCalls = quarantined.MainEvaluatorDelta;
        bool fallbackProhibited = quarantined.Composition.Status == EmlRung0Statuses.GuardRejected
            && quarantined.NumericFallbackProhibited
            && fallbackCalls == 0;

        bool repromoted = true;
        for (int i = 0; i < disagreementAudit.Rules.Count; i++)
            repromoted &= store.TryRepromoteRung0Rule(
                disagreementAudit.Rules[i], disagreementProof.Digest, in carrier, new EmlGrader());
        EmlRung0Result afterRepromotion = store.DeriveRung0(
            in carrier, ExpLogX, "x", in branchBudget);
        repromoted &= afterRepromotion.Status == EmlRung0Statuses.Composed;

        EmlLawRewrite donorRewrite = FindRewrite(store, in carrier, ExpLogY, "y");
        bool relationNullCreated = EmlLawRewrite.TryCreateRelationNull(
            in realRewrite, in donorRewrite, 0xC2C2C2C2UL, new EmlGrader(), out EmlLawRewrite relationNull);
        EmlRung0Result realExecution = store.DeriveRung0(
            in carrier, ExpLogX, "x", in branchBudget);
        EmlRung0NullExecution nullExecution = relationNullCreated
            ? store.DeriveRung0Null(in carrier, ExpLogX, in relationNull, in branchBudget)
            : default;
        EmlLawRewrite invalidNull = relationNull with { RelationNullSalt = 0 };
        EmlRung0NullExecution zeroWorkExecution = relationNullCreated
            ? store.DeriveRung0Null(in carrier, ExpLogX, in invalidNull, in branchBudget)
            : default;
        bool poweredNull = relationNullCreated
            && realExecution.Composed
            && nullExecution.Powered
            && nullExecution.Budget == branchBudget
            && string.Equals(nullExecution.StartRPN, ExpLogX, StringComparison.Ordinal)
            && !string.Equals(nullExecution.TerminalRPN, realExecution.Proof!.Value.ConsequentRPN, StringComparison.Ordinal);
        EmlLawCandidateInstantiation repeatedNullCandidate = new(meteredObligation, relationNull);
        (bool firstReuseVerified, bool secondReuseVerified, long firstReuseCalls, long secondReuseCalls) = relationNullCreated
            ? EmlIntensionalRematchRunner.ProbeReusedLawCandidate(
                SignatureDigits,
                fixture.AdmissionImage,
                fixture.Bindings,
                store,
                in repeatedNullCandidate)
            : default;
        bool reusedNoNumericFallback = relationNullCreated
            && !firstReuseVerified
            && !secondReuseVerified
            && secondReuseCalls == 0;
        bool zeroWorkRejected = relationNullCreated
            && !zeroWorkExecution.Powered
            && !zeroWorkExecution.Work.DidWork
            && zeroWorkExecution.Work.Applications == 0;
        EmlGrader nullGrader = new();
        bool nullAccidentallyValid = relationNullCreated
            && nullGrader.GradeRpn(relationNull.AntecedentRpn, relationNull.ConsequentRpn).Grade == 'E';
        bool nullHasNoAuthority = relationNullCreated
            && relationNull.IsRelationNull
            && !relationNull.IsRung0Eligible
            && !relationNull.RuleID.IsEmpty
            && !relationNull.RelationNullSourceID.IsEmpty
            && !relationNull.RelationNullDonorID.IsEmpty
            && relationNull.RelationNullSalt != 0
            && !nullAccidentallyValid
            && !string.Equals(realRewrite.ConsequentRpn, relationNull.ConsequentRpn, StringComparison.Ordinal);

        byte[] lawImage = Save(store);
        EmlLawStore loadedStore = LoadStore(lawImage);
        bool lawSaveIdentity = lawImage.AsSpan().SequenceEqual(Save(loadedStore));
        byte[] sieveImage = sieve.CaptureAdmissionState();
        EmlSieve loadedSieve = EmlRematchFixture.CloneSieve(SignatureDigits, sieveImage);
        bool sieveSaveIdentity = sieveImage.AsSpan().SequenceEqual(loadedSieve.CaptureAdmissionState());
        bool lawCorruptionRejected = CorruptDigestAndReject(
            lawImage, agreedProof.Digest, static image => _ = LoadStore(image));
        EmlLawBehaviorCertificate archivedCertificate = store.Classes.Values.First().Rep.Certificate;
        bool archiveCertificateCorruptionRejected = CorruptArchivedCertificateAndReject(
            lawImage, in archivedCertificate);
        bool sieveCorruptionRejected = CorruptDigestAndReject(
            sieveImage,
            agreedProof.Digest,
            static image => _ = EmlRematchFixture.CloneSieve(SignatureDigits, image));
        EmlCompositionStep rankTamperedStep = agreedProof.Steps[0] with
        {
            RankAfter = agreedProof.Steps[0].RankBefore,
        };
        bool digestValidCapViolationsRejected = RejectProof(
                agreedProof with
                {
                    Work = agreedProof.Work with { ExpandedStates = agreedProof.Budget.MaxStates + 1 },
                })
            && RejectProof(
                agreedProof with
                {
                    Work = agreedProof.Work with { Applications = agreedProof.Budget.MaxApplications + 1 },
                })
            && RejectProof(
                agreedProof with
                {
                    Steps = [agreedProof.Steps[0], agreedProof.Steps[0]],
                    Work = agreedProof.Work with { Applications = Math.Max(2, agreedProof.Work.Applications) },
                })
            && RejectProof(
                agreedProof with
                {
                    Work = agreedProof.Work with { GuardRejections = -1 },
                })
            && RejectProof(agreedProof with { Steps = [rankTamperedStep] });
        bool verifiedLawSupportRoundTrip = VerifyVerifiedLawSupportRoundTrip();
        IReadOnlyDictionary<string, bool> verifiedLawSupportMutations = VerifyVerifiedLawSupportMutationMatrix();
        bool verifiedLawSupportMutationMatrix = verifiedLawSupportMutations.Values.All(static value => value);

        bool accepted = derivedAdmitted
            && reverseOrientationPortable
            && canonicalRecordDigests
            && exactAdmissionDifferential
            && mainEvaluatorDelta == 0
            && branchRejected.Status == EmlRung0Statuses.GuardRejected
            && branchRejected.Work.DidWork
            && exactRefund
            && allRulesQuarantined
            && fallbackProhibited
            && repromoted
            && poweredNull
            && reusedNoNumericFallback
            && zeroWorkRejected
            && nullHasNoAuthority
            && lawSaveIdentity
            && sieveSaveIdentity
            && lawCorruptionRejected
            && archiveCertificateCorruptionRejected
            && sieveCorruptionRejected
            && digestValidCapViolationsRejected
            && verifiedLawSupportRoundTrip
            && verifiedLawSupportMutationMatrix;

        StringBuilder report = new("metric\tvalue\n");
        Append(report, "derived_admitted", derivedAdmitted);
        Append(report, "reverse_orientation_portable", reverseOrientationPortable);
        Append(report, "admission_path_bound", admissionPathBound);
        Append(report, "exact_admission_differential", exactAdmissionDifferential);
        report.Append("comparator_calls\t").Append(comparatorEvaluation.Calls).AppendLine();
        report.Append("main_evaluator_delta\t").Append(mainEvaluatorDelta).AppendLine();
        report.Append("audit_calls\t").Append(agreedAudit.EvaluatorCalls).AppendLine();
        report.Append("branch_status\t").Append(branchRejected.Status).AppendLine();
        report.Append("branch_work\t").Append(branchRejected.Work.Applications).AppendLine();
        report.Append("exhausted_applications\t").Append(exhausted.Work.Applications).AppendLine();
        report.Append("metered_tree_nodes\t").Append(settlement.Actual.LawRewriteTreeNodes).AppendLine();
        Append(report, "exact_refund", exactRefund);
        Append(report, "all_rules_quarantined", allRulesQuarantined);
        report.Append("quarantined_status\t").Append(quarantined.Composition.Status).AppendLine();
        Append(report, "quarantined_rule_prohibited", quarantined.NumericFallbackProhibited);
        Append(report, "numeric_fallback_prohibited", fallbackProhibited);
        report.Append("fallback_calls\t").Append(fallbackCalls).AppendLine();
        Append(report, "repromoted", repromoted);
        Append(report, "powered_relation_null", poweredNull);
        Append(report, "reused_no_numeric_fallback", reusedNoNumericFallback);
        report.Append("reused_first_calls\t").Append(firstReuseCalls).AppendLine();
        report.Append("reused_second_calls\t").Append(secondReuseCalls).AppendLine();
        Append(report, "zero_work_null_rejected", zeroWorkRejected);
        Append(report, "relation_null_no_authority", nullHasNoAuthority);
        Append(report, "law_save_load_save", lawSaveIdentity);
        Append(report, "sieve_save_load_save", sieveSaveIdentity);
        Append(report, "law_corruption_rejected", lawCorruptionRejected);
        Append(report, "archive_certificate_corruption_rejected", archiveCertificateCorruptionRejected);
        Append(report, "sieve_corruption_rejected", sieveCorruptionRejected);
        Append(report, "digest_valid_cap_violations_rejected", digestValidCapViolationsRejected);
        Append(report, "verified_law_support_round_trip", verifiedLawSupportRoundTrip);
        foreach ((string name, bool value) in verifiedLawSupportMutations)
            Append(report, "verified_law_support_mutation_" + name, value);
        Append(report, "verified_law_support_mutation_matrix", verifiedLawSupportMutationMatrix);
        report.Append("status\t").Append(accepted ? "accepted" : "rejected").AppendLine();

        Run receipt = Cogito.Run.New("eml-rung0-assay");
        const string ReceiptName = "eml_rung0_assay.tsv";
        receipt.Write(ReceiptName, report.ToString());
        Console.WriteLine($"  EML rung-0 assay -> {Path.GetRelativePath(Environment.CurrentDirectory, receipt.PathOf(ReceiptName))}");
        Console.WriteLine($"  derived zero-call={derivedAdmitted} · branch={branchRejected.Status} · exhausted={exhausted.Work.Applications} edge");
        Console.WriteLine($"  audit={agreedAudit.Status}/{agreedAudit.EvaluatorCalls} · quarantine={allRulesQuarantined} · repromoted={repromoted} · null={poweredNull}");
        return accepted ? 0 : 1;
    }

    internal static bool VerifySamplerFixture()
    {
        const int signatureDigits = 9;
        EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
        EmlSieve sieve = fixture.Sieve;
        EmlLawStore store = CreateLawStore();
        if (!sieve.TryCreateRewriteCarrier(ExpLogX, out EmlRewritePredictionCarrier carrier, out _)) return false;

        EmlRung0Proof? firstProof = null;
        EmlRung0Proof? cadenceProof = null;
        for (int salt = 0; salt < 512 && (firstProof is null || cadenceProof is null); salt++)
        {
            EmlRung0Budget budget = new(1, 2 + salt, 64);
            EmlRung0Result result = store.DeriveRung0(in carrier, ExpLogX, "x", in budget);
            if (result.Proof is not EmlRung0Proof proof) continue;
            if (!EmlRung0Digest.SelectNumericAudit(proof.Digest) && firstProof is null) firstProof = proof;
            if (EmlRung0Digest.SelectNumericAudit(proof.Digest) && cadenceProof is null) cadenceProof = proof;
        }
        if (firstProof is not EmlRung0Proof first || cadenceProof is not EmlRung0Proof cadence) return false;

        store.RecordRung0Proof(in first);
        EmlRung0Audit minimumOne = EmlRung0Auditor.Audit(store, in first, EmlRung0AuditSelectionSpecies.MinimumOne, persist: false);
        store.RecordRung0Audit(in minimumOne);
        int firstCount = store.Rung0Audits.Count;
        store.RecordRung0Audit(in minimumOne);
        bool firstSelected = minimumOne.Status == EmlRung0AuditStatuses.Agreed
            && minimumOne.Selection == EmlRung0AuditSelectionSpecies.MinimumOne
            && minimumOne.EvaluatorCalls > 0
            && store.Rung0Audits.Count == firstCount;

        byte[] fullImage = Save(store);
        EmlLawStore fullReplay = LoadStore(fullImage);
        bool fullRoundTrip = fullReplay.Rung0Audits.Count == 1
            && fullReplay.Rung0Audits[0].Selection == EmlRung0AuditSelectionSpecies.MinimumOne
            && Save(fullReplay).AsSpan().SequenceEqual(fullImage);

        store.RecordRung0Proof(in cadence);
        EmlRung0Audit cadenceAudit = EmlRung0Auditor.Audit(store, in cadence,
            EmlRung0AuditSelectionSpecies.DigestCadence, persist: false);
        store.RecordRung0Audit(in cadenceAudit);
        bool cadenceSelected = cadenceAudit.Selection == EmlRung0AuditSelectionSpecies.DigestCadence
            && cadenceAudit.Status == EmlRung0AuditStatuses.Agreed;

        EmlRung0Proof? laterProof = null;
        for (int salt = 0; salt < 512 && laterProof is null; salt++)
        {
            EmlRung0Budget budget = new(1, 2 + salt, 65);
            EmlRung0Result result = store.DeriveRung0(in carrier, ExpLogX, "x", in budget);
            if (result.Proof is EmlRung0Proof proof
                && !EmlRung0Digest.SelectNumericAudit(proof.Digest)
                && proof.Digest != first.Digest)
                laterProof = proof;
        }
        if (laterProof is not EmlRung0Proof later) return false;
        store.RecordRung0Proof(in later);
        EmlRung0Audit laterAudit = EmlRung0Auditor.Audit(store, in later,
            EmlRung0AuditSelectionSpecies.DigestCadence, persist: false);
        store.RecordRung0Audit(in laterAudit);
        bool laterSkipped = laterAudit.Selection == EmlRung0AuditSelectionSpecies.DigestCadence
            && laterAudit.Status == EmlRung0AuditStatuses.NotSelected;

        EmlLawStore deltaBase = LoadStore(fullImage);
        EmlLawStore deltaTarget = LoadStore(fullImage);
        deltaTarget.RecordRung0Proof(in cadence);
        deltaTarget.RecordRung0Audit(in cadenceAudit);
        EmlLawStoreCheckpointDelta appendDelta = deltaTarget.CaptureCheckpointDelta();
        using MemoryStream deltaStream = new();
        using (CkptWriter deltaWriter = new(deltaStream)) EmlLawStore.WriteCheckpointDelta(deltaWriter, in appendDelta);
        deltaStream.Position = 0;
        EmlLawStoreCheckpointDelta decodedDelta;
        using (CkptReader deltaReader = new(deltaStream)) decodedDelta = EmlLawStore.ReadCheckpointDelta(deltaReader);
        deltaBase.ApplyCheckpointDelta(in decodedDelta);
        bool deltaRoundTrip = deltaBase.Rung0Audits.Count == 2
            && deltaBase.Rung0Audits[0].Selection == EmlRung0AuditSelectionSpecies.MinimumOne
            && deltaBase.Rung0Audits[1].Selection == EmlRung0AuditSelectionSpecies.DigestCadence;

        EmlRung0Proof legacyProof = later;
        EmlRung0Audit legacy = laterAudit;
        EmlLawStore promotedBase = LoadStore(fullImage);
        promotedBase.RecordRung0Proof(in legacyProof);
        promotedBase.RecordRung0Audit(in legacy);
        EmlRung0Audit promoted = EmlRung0Auditor.Audit(promotedBase, in legacyProof,
            EmlRung0AuditSelectionSpecies.MinimumOne, persist: false);
        promotedBase.PromoteRung0Audit(in promoted);
        bool promotedInPlace = promotedBase.Rung0Audits.Count == 2
            && promotedBase.Rung0Audits[^1].Selection == EmlRung0AuditSelectionSpecies.MinimumOne
            && promotedBase.Rung0Audits[^1].Status == EmlRung0AuditStatuses.Agreed;
        EmlLawStore promotionBase = LoadStore(fullImage);
        EmlLawStoreCheckpointDelta promotionDelta = promotedBase.CaptureCheckpointDelta();
        using MemoryStream promotionStream = new();
        using (CkptWriter promotionWriter = new(promotionStream)) EmlLawStore.WriteCheckpointDelta(promotionWriter, in promotionDelta);
        promotionStream.Position = 0;
        using (CkptReader promotionReader = new(promotionStream))
        {
            EmlLawStoreCheckpointDelta decodedAdmission = EmlLawStore.ReadCheckpointDelta(promotionReader);
            promotionBase.ApplyCheckpointDelta(in decodedAdmission);
        }
        bool promotionReplay = promotionBase.Rung0Audits.Count == 2
            && promotionBase.Rung0Audits[^1].Selection == EmlRung0AuditSelectionSpecies.MinimumOne;

        EmlRung0Proof? rollbackCandidate = null;
        for (int salt = 0; salt < 512 && rollbackCandidate is null; salt++)
        {
            EmlRung0Budget budget = new(1, 2 + salt, 66);
            EmlRung0Result result = store.DeriveRung0(in carrier, ExpLogX, "x", in budget);
            if (result.Proof is EmlRung0Proof proof
                && !store.TryGetRung0Audit(proof.Digest, out _)) rollbackCandidate = proof;
        }
        if (rollbackCandidate is not EmlRung0Proof rollbackProof) return false;
        EmlRung0Audit rollbackAudit = EmlRung0Auditor.Audit(store, in rollbackProof,
            EmlRung0AuditSelectionSpecies.MinimumOne, persist: false);
        int beforeRollback = store.Rung0Audits.Count;
        using (EmlSieve.SpeculativeTransaction transaction = sieve.BeginSpeculativeTransaction())
        {
            sieve.StageRung0Audit(store, in rollbackAudit);
            transaction.Rollback();
        }
        bool rollbackDebtDiscarded = store.Rung0Audits.Count == beforeRollback;

        bool relationNullUnchanged = true;
        EmlOrdinaryRunRung0Receipt relationNullReceipt = EmlOrdinaryRunRung0Receipt.Create(
            EmlRung0Modes.Armed, EmlRematchAssayStatuses.Exact, EmlRematchPowerStatuses.Unpowered,
            opportunities: 1, derivations: 0, zeroEvaluatorCompositions: 0, audits: 0,
            relationNullExecutions: 3, relationNullDivergences: 3, relationNullAuthorityPredictions: 0,
            "D", "S", "C");
        relationNullUnchanged = relationNullReceipt.RelationNullExecutions == 3
            && relationNullReceipt.RelationNullDivergences == 3
            && relationNullReceipt.RelationNullAuthorityPredictions == 0;
        return firstSelected && fullRoundTrip && cadenceSelected && laterSkipped && deltaRoundTrip
            && promotedInPlace && promotionReplay && rollbackDebtDiscarded && relationNullUnchanged;
    }

    private static bool VerifyVerifiedLawSupportRoundTrip()
    {
        EmlVerifiedLaw law = EmlGuardedRewriteAssay.CreateVerifiedLaw();
        EmlGrader grader = new();
        EmlVerdict x = grader.GradeRpn("11xE1EE1E", "x");
        EmlVerdict y = grader.GradeRpn("11yE1EE1E", "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in x, 9), "11xE1EE1E", "x", new EmlPredictionID(10)),
            new EmlLawPrediction(EmlCert.Of(in y, 9), "11yE1EE1E", "y", new EmlPredictionID(11)),
        ];
        EmlLaw lawSpec = law.Law;
        if (!EmlVerifiedLaw.TryVerify(in lawSpec, support, 9, out EmlVerifiedLaw? verified) || verified is null)
            return false;
        EmlLawStore store = new();
        if (!store.TryAdmit(verified, 0, out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission))
            return false;
        Dictionary<int, IReadOnlyList<TapeEventID>> claimEvents = new()
        {
            [10] = [new TapeEventID(101)],
            [11] = [new TapeEventID(103)],
        };
        Dictionary<int, TapeEventID> claimMintEvents = new()
        {
            [10] = new TapeEventID(101),
            [11] = new TapeEventID(103),
        };
        Dictionary<int, string> claimMintDigests = new()
        {
            [10] = new string('a', 64),
            [11] = new string('b', 64),
        };
        Dictionary<int, string> claimMintLineDigests = new()
        {
            [10] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(support[0].LeftRpn))),
            [11] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(support[1].LeftRpn))),
        };
        EmlVerifiedLawSupportReceipt receipt = store.RecordVerifiedLawSupport(
            verified, in admission, support, claimEvents, claimMintEvents, claimMintDigests, claimMintLineDigests,
            [new TapeEventID(101), new TapeEventID(103)], 7, 0);
        EmlLawStore loaded = LoadStore(Save(store));
        List<(EmlVerifiedLaw Law, EmlVerifiedLawSupportReceipt Support)> pending = new();
        loaded.AppendPendingVerifiedLawSupports(pending);
        if (pending.Count != 1 || pending[0].Support.Digest != receipt.Digest) return false;
        loaded.BindVerifiedLawSupportExecution(pending[0].Support, new TapeEventID(900), [20, 21]);
        loaded.MarkVerifiedLawSupportConsumed(pending[0].Support);
        EmlLawStore resumed = LoadStore(Save(loaded));
        List<(EmlVerifiedLaw Law, EmlVerifiedLawSupportReceipt Support)> replay = new();
        resumed.AppendPendingVerifiedLawSupports(replay);
        return replay.Count == 0 && resumed.VerifiedLawSupports.Count == 1 && resumed.VerifiedLawSupports[0].Consumed;
    }

    /// The support receipt is a dense custody seam: every identity, claim atom,
    /// world enclosure, and execution range must fail closed when one byte or one
    /// binding is changed.  Keep these gates named; a single aggregate would hide
    /// which contract regressed.
    private static IReadOnlyDictionary<string, bool> VerifyVerifiedLawSupportMutationMatrix()
    {
        EmlVerifiedLaw law = EmlGuardedRewriteAssay.CreateVerifiedLaw();
        EmlGrader grader = new();
        EmlVerdict x = grader.GradeRpn(ExpLogX, "x");
        EmlVerdict y = grader.GradeRpn(ExpLogY, "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in x, 9), ExpLogX, "x", new EmlPredictionID(10)),
            new EmlLawPrediction(EmlCert.Of(in y, 9), ExpLogY, "y", new EmlPredictionID(11)),
        ];
        EmlLaw lawSpec = law.Law;
        if (!EmlVerifiedLaw.TryVerify(in lawSpec, support, 9, out EmlVerifiedLaw? verified) || verified is null)
            throw new InvalidDataException("support mutation matrix could not construct its verified candidate");
        EmlLawStore store = new();
        if (!store.TryAdmit(verified, 0, out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission))
            throw new InvalidDataException("support mutation matrix could not admit its verified candidate");
        Dictionary<int, IReadOnlyList<TapeEventID>> claimEvents = new()
        {
            [10] = [new TapeEventID(101)],
            [11] = [new TapeEventID(103)],
        };
        Dictionary<int, TapeEventID> claimMintEvents = new()
        {
            [10] = new TapeEventID(101),
            [11] = new TapeEventID(103),
        };
        Dictionary<int, string> claimMintDigests = new()
        {
            [10] = new string('a', 64),
            [11] = new string('b', 64),
        };
        Dictionary<int, string> claimMintLineDigests = new()
        {
            [10] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(support[0].LeftRpn))),
            [11] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(support[1].LeftRpn))),
        };
        EmlVerifiedLawSupportReceipt baseline = store.RecordVerifiedLawSupport(
            verified, in admission, support, claimEvents, claimMintEvents, claimMintDigests, claimMintLineDigests,
            [new TapeEventID(101), new TapeEventID(103)], 7, 0);

        Dictionary<string, bool> gates = new(StringComparer.Ordinal);
        gates["source_claim_id"] = RejectReceiptMutation(baseline, candidateSupport: [
            new EmlVerifiedLawSupportReceipt.SupportPrediction(12, baseline.CandidateSupport[0].Certificate, baseline.CandidateSupport[0].LeftRpn, baseline.CandidateSupport[0].RightRpn),
            baseline.CandidateSupport[1],
        ]);
        gates["certificate"] = RejectReceiptMutation(baseline, candidateSupport: [
            new EmlVerifiedLawSupportReceipt.SupportPrediction(10, new string('f', 64), baseline.CandidateSupport[0].LeftRpn, baseline.CandidateSupport[0].RightRpn),
            baseline.CandidateSupport[1],
        ]);
        gates["lhs"] = RejectReceiptMutation(baseline, candidateSupport: [
            new EmlVerifiedLawSupportReceipt.SupportPrediction(10, baseline.CandidateSupport[0].Certificate, "y", baseline.CandidateSupport[0].RightRpn),
            baseline.CandidateSupport[1],
        ]);
        gates["rhs"] = RejectReceiptMutation(baseline, candidateSupport: [
            new EmlVerifiedLawSupportReceipt.SupportPrediction(10, baseline.CandidateSupport[0].Certificate, baseline.CandidateSupport[0].LeftRpn, "y"),
            baseline.CandidateSupport[1],
        ]);
        string[] mintDigests = baseline.SourcePredictionDigests.ToArray();
        mintDigests[0] = new string('0', 64);
        gates["mint_digest"] = RejectReceiptMutation(baseline, sourcePredictionDigests: mintDigests);
        gates["mint_line_prog_sig_grade_corrob_digest"] = RejectReceiptMutation(
            baseline, sourcePredictionDigests: mintDigests);
        string[] mintLineDigests = baseline.SourcePredictionMintLineDigests.ToArray();
        (mintLineDigests[0], mintLineDigests[1]) = (mintLineDigests[1], mintLineDigests[0]);
        gates["mint_line_digest"] = RejectReceiptMutation(baseline, sourcePredictionMintLineDigests: mintLineDigests);
        gates["swapped_valid_claim"] = RejectReceiptMutation(baseline, candidateSupport: [
            baseline.CandidateSupport[1],
            baseline.CandidateSupport[0],
        ]);
        gates["candidate_support_digest"] = RejectCandidateOccurrenceDigestMutation(baseline);
        gates["candidate_package"] = RejectReceiptMutation(baseline, candidateAdmissionID: baseline.CandidateAdmissionID + "-forged");
        gates["admission"] = RejectReceiptMutation(baseline, canonicalAuthorityID: baseline.CanonicalAuthorityID + "-forged");
        IReadOnlyList<IReadOnlyList<TapeEventID>> claimOrder = [baseline.SourcePredictionOpportunityEvents[0], [new TapeEventID(101)]];
        gates["world_order"] = RejectReceiptMutation(baseline, sourcePredictionOpportunityEvents: claimOrder, worldOpportunityEventIDs: [new TapeEventID(101), new TapeEventID(103)]);
        gates["mint_order"] = RejectReceiptMutation(baseline, sourcePredictionMintEvents: [new TapeEventID(103), new TapeEventID(101)]);
        gates["unpowered_missing_source_event_line"] = VerifyUnpoweredMissingSourceCustody();
        gates["powered_mixed_basis_preflight"] = VerifyPoweredMixedBasisPreflight();

        const string supportDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string supportDigest2 = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        static bool ParseExecution(string text)
            => TapePacketCreator.TryReadEmlLawExecutionSupports(Encoding.ASCII.GetBytes(text), out _);
        string execution = "LAW-EXECUTION\toffers=2\tmints=3\tclaims=0,1,2\tsupports=" + supportDigest + "," + supportDigest2
            + "\tauthorities=authority-a,authority-b\tsupport-ranges=" + supportDigest + ":0:2," + supportDigest2 + ":2:1";
        gates["execution_range_valid"] = ParseExecution(execution);
        gates["execution_range_overlap"] = !ParseExecution(execution.Replace(":2:1", ":1:1", StringComparison.Ordinal));
        gates["execution_range_out_of_bounds"] = !ParseExecution(execution.Replace(":2:1", ":3:1", StringComparison.Ordinal));
        gates["execution_range_zero"] = !ParseExecution(execution.Replace(":2:1", ":2:0", StringComparison.Ordinal));
        gates["execution_authority"] = !ParseExecution(execution.Replace("authority-b", "", StringComparison.Ordinal));
        gates["execution_range_partial"] = !ParseExecution(execution.Replace(supportDigest + ":0:2", supportDigest + ":0:1", StringComparison.Ordinal));
        gates["execution_range_foreign_claim"] = !ParseExecution(execution.Replace("claims=0,1,2", "claims=0,1,3", StringComparison.Ordinal));
        gates["execution_range_order"] = !ParseExecution(execution.Replace(
            supportDigest + ":0:2," + supportDigest2 + ":2:1",
            supportDigest2 + ":2:1," + supportDigest + ":0:2", StringComparison.Ordinal));
        Dictionary<int, TapeEventID> replayPredictionMintEvents = new()
        {
            [10] = new TapeEventID(7),
            [11] = new TapeEventID(7),
        };
        EmlVerifiedLawSupportReceipt replaySupport = store.RecordVerifiedLawSupport(
            verified, in admission, support, claimEvents, replayPredictionMintEvents, claimMintDigests, claimMintLineDigests,
            [new TapeEventID(101), new TapeEventID(103)], 7, 1);
        store.BindVerifiedLawSupportPacket(replaySupport, new TapeEventID(8));
        store.BindVerifiedLawSupportExecution(replaySupport, new TapeEventID(9), [20, 21]);
        EmlVerifiedLawSupportReceipt replaySupport2 = store.RecordVerifiedLawSupport(
            verified, in admission, support, claimEvents, replayPredictionMintEvents, claimMintDigests, claimMintLineDigests,
            [new TapeEventID(101), new TapeEventID(103)], 7, 2);
        store.BindVerifiedLawSupportPacket(replaySupport2, new TapeEventID(8));
        store.BindVerifiedLawSupportExecution(replaySupport2, new TapeEventID(9), [30, 31]);
        TapeEventView replayView = new(new TapeEventID(9), 0, Provenances.Reflected, false, "eml:law-execution");
        TapePacketCreator.EmlLawExecutionSupportPacket replayPacket = new(
            [replaySupport.Digest], [replaySupport.CanonicalAuthorityID],
            [(replaySupport.Digest, 20, 2)], [20, 21], 2, 2);
        TapePacketCreator.EmlLawExecutionSupportPacket partialReplayPacket = replayPacket with
        {
            Ranges = [(replaySupport.Digest, 20, 1)],
        };
        TapePacketCreator.EmlLawExecutionSupportPacket foreignReplayPacket = replayPacket with
        {
            PredictionIDs = [20, 22],
        };
        TapePacketCreator.EmlLawExecutionSupportPacket crossWiredReplayPacket = new(
            [replaySupport2.Digest, replaySupport.Digest],
            [replaySupport2.CanonicalAuthorityID, replaySupport.CanonicalAuthorityID],
            [(replaySupport2.Digest, 20, 2), (replaySupport.Digest, 30, 2)],
            [20, 21, 30, 31], 4, 4);
        gates["execution_replay_exact"] = EmlLawStore.MatchesPersistedLawExecution(replayView, in replayPacket, replaySupport);
        gates["execution_replay_partial"] = !EmlLawStore.MatchesPersistedLawExecution(replayView, in partialReplayPacket, replaySupport);
        gates["execution_replay_foreign_claim"] = !EmlLawStore.MatchesPersistedLawExecution(replayView, in foreignReplayPacket, replaySupport);
        gates["execution_replay_cross_wired"] = !EmlLawStore.MatchesPersistedLawExecution(replayView, in crossWiredReplayPacket, replaySupport);
        bool duplicateReplay = ThrowsInvalid(() => store.RecordVerifiedLawSupport(
            verified, in admission, support, claimEvents, claimMintEvents, claimMintDigests, claimMintLineDigests,
            [new TapeEventID(101), new TapeEventID(103)], 7, 0));
        gates["duplicate_replay"] = duplicateReplay;
        EmlLawStore legacySource = new();
        if (!legacySource.TryAdmit(verified, 0, out _)) throw new InvalidDataException("support mutation matrix legacy admission failed");
        byte[] legacyImage = Save(legacySource);
        BinaryPrimitives.WriteInt32LittleEndian(legacyImage.AsSpan(0, sizeof(int)), 12);
        EmlLawStore legacy = LoadStore(legacyImage);
        gates["schema12_legacy_certification"] = legacy.LegacyWorldSupportUnavailable && legacy.VerifiedLawSupports.Count == 0;
        byte[] legacySupportImage = Save(store);
        BinaryPrimitives.WriteInt32LittleEndian(legacySupportImage.AsSpan(0, sizeof(int)), 13);
        gates["schema13_legacy_support_fail_closed"] = ThrowsInvalid(() => LoadStore(legacySupportImage));
        return gates;
    }

    private static bool VerifyPoweredMixedBasisPreflight()
    {
        EmlGrader grader = new();
        EmlVerdict x = grader.GradeRpn(ExpLogX, "x");
        EmlVerdict y = grader.GradeRpn(ExpLogY, "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in x, 9), ExpLogX, "x", new EmlPredictionID(10)),
            new EmlLawPrediction(EmlCert.Of(in y, 9), ExpLogY, "y", new EmlPredictionID(11)),
        ];
        EmlVerifiedLaw verifiedLaw = EmlGuardedRewriteAssay.CreateVerifiedLaw();
        EmlLaw lawSpec = verifiedLaw.Law;
        if (!EmlVerifiedLaw.TryVerify(in lawSpec, support, 9, out EmlVerifiedLaw? verified) || verified is null)
            return false;
        EmlSourcePredictionAdmission mint10 = new(EmlSourcePredictionAdmissionSpecies.MintPacket, new TapeEventID(101));
        Dictionary<int, IReadOnlyList<TapeEventID>> mixedOpportunities = new() { [10] = [new TapeEventID(101)] };
        Dictionary<int, EmlSourcePredictionAdmission> mixedAdmissions = new() { [10] = mint10 };
        Dictionary<int, string> mixedMintDigests = new() { [10] = new string('a', 64) };
        Dictionary<int, string> mixedLineDigests = new() { [10] = new string('b', 64) };
        EmlLawStore store = new();
        int nextCaptureIndex = 0;
        byte[] before = Save(store);
        if (store.TryAdmitWithSupportCustody(
                verified, ref nextCaptureIndex, support, mixedOpportunities, mixedAdmissions, mixedMintDigests, mixedLineDigests,
                [new TapeEventID(101), new TapeEventID(103)],
                out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> _)
            || nextCaptureIndex != 0
            || !before.SequenceEqual(Save(store)))
            return false;
        Dictionary<int, IReadOnlyList<TapeEventID>> completeOpportunities = new()
        {
            [10] = [new TapeEventID(101)],
            [11] = [new TapeEventID(103)],
        };
        Dictionary<int, EmlSourcePredictionAdmission> completeAdmissions = new()
        {
            [10] = mint10,
            [11] = new EmlSourcePredictionAdmission(EmlSourcePredictionAdmissionSpecies.MintPacket, new TapeEventID(103)),
        };
        Dictionary<int, string> completeMintDigests = new()
        {
            [10] = new string('a', 64),
            [11] = new string('c', 64),
        };
        Dictionary<int, string> completeLineDigests = new()
        {
            [10] = new string('b', 64),
            [11] = new string('d', 64),
        };
        return store.TryAdmitWithSupportCustody(
                verified, ref nextCaptureIndex, support, completeOpportunities, completeAdmissions, completeMintDigests, completeLineDigests,
                [new TapeEventID(101), new TapeEventID(103)],
                out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)
            && nextCaptureIndex == 1
            && admission.FirstCapture;
    }

    private static bool VerifyUnpoweredMissingSourceCustody()
    {
        EmlRematchFixture fixture = EmlRematchFixture.Create(9);
        EmlSieve sieve = fixture.Sieve;
        int xID = -1, yID = -1;
        for (int i = 0; i < sieve.MintLog.Count; i++)
        {
            if (sieve.MintLog[i].Prog == ExpLogX) xID = i;
            if (sieve.MintLog[i].Prog == ExpLogY) yID = i;
        }
        if (xID < 0 || yID < 0) return false;
        EmlGrader grader = new();
        EmlVerdict x = grader.GradeRpn(ExpLogX, "x");
        EmlVerdict y = grader.GradeRpn(ExpLogY, "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in x, 9), ExpLogX, "x", new EmlPredictionID(xID)),
            new EmlLawPrediction(EmlCert.Of(in y, 9), ExpLogY, "y", new EmlPredictionID(yID)),
        ];
        EmlVerifiedLaw law = EmlGuardedRewriteAssay.CreateVerifiedLaw();
        EmlLaw lawSpec = law.Law;
        if (!EmlVerifiedLaw.TryVerify(in lawSpec, support, 9, out EmlVerifiedLaw? verified) || verified is null)
            return false;
        EmlLawStore store = new();
        if (!store.TryAdmit(verified, 0, out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission))
            return false;
        Dictionary<int, string> lineDigests = new()
        {
            [xID] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(sieve.MintLog[xID].Line))),
            [yID] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(sieve.MintLog[yID].Line))),
        };
        EmlVerifiedLawSupportReceipt receipt = store.RecordVerifiedLawSupport(
            verified, in admission, support,
            new Dictionary<int, IReadOnlyList<TapeEventID>>(),
            new Dictionary<int, TapeEventID>(),
            new Dictionary<int, string>(), lineDigests,
            Array.Empty<TapeEventID>(), 7, 0);
        return receipt.SourcePredictionMintEvents.All(static eventID => eventID is null)
            && receipt.SourcePredictionMintLineDigests.SequenceEqual(lineDigests.OrderBy(static pair => pair.Key).Select(static pair => pair.Value));
    }

    private static bool RejectReceiptMutation(
        EmlVerifiedLawSupportReceipt baseline,
        string? candidateAdmissionID = null,
        EmlVerifiedLaw? candidate = null,
        IReadOnlyList<EmlVerifiedLawSupportReceipt.SupportPrediction>? candidateSupport = null,
        string? canonicalAuthorityID = null,
        IReadOnlyList<string>? sourcePredictionDigests = null,
        IReadOnlyList<string>? sourcePredictionMintLineDigests = null,
        IReadOnlyList<IReadOnlyList<TapeEventID>>? sourcePredictionOpportunityEvents = null,
        IReadOnlyList<TapeEventID?>? sourcePredictionMintEvents = null,
        IReadOnlyList<TapeEventID>? worldOpportunityEventIDs = null)
        => ThrowsInvalid(() => _ = new EmlVerifiedLawSupportReceipt(
            candidateAdmissionID ?? baseline.CandidateAdmissionID,
            candidate ?? baseline.Candidate,
            baseline.SupportSetDigest,
            candidateSupport ?? baseline.CandidateSupport,
            baseline.Certificate,
            canonicalAuthorityID ?? baseline.CanonicalAuthorityID,
            baseline.SourcePredictionIDs,
            sourcePredictionDigests ?? baseline.SourcePredictionDigests,
            sourcePredictionMintLineDigests ?? baseline.SourcePredictionMintLineDigests,
            sourcePredictionOpportunityEvents ?? baseline.SourcePredictionOpportunityEvents,
            sourcePredictionMintEvents ?? baseline.SourcePredictionMintEvents,
            worldOpportunityEventIDs ?? baseline.WorldOpportunityEventIDs,
            baseline.CaptureStep,
            baseline.CaptureIndex,
            baseline.FirstCapture,
            baseline.RepresentativeChanged,
            baseline.Digest,
            baseline.Consumed,
            baseline.ExecutionEventID));

    private static bool RejectCandidateOccurrenceDigestMutation(EmlVerifiedLawSupportReceipt baseline)
    {
        try
        {
            using MemoryStream stream = new();
            using (CkptWriter writer = new(stream)) baseline.Candidate.Save(writer);
            byte[] image = stream.ToArray();
            byte[] needle = BitConverter.GetBytes(baseline.Candidate.Proof.OccurrenceDigest);
            int offset = image.AsSpan().IndexOf(needle);
            if (offset < 0) return false;
            image[offset] ^= 0x01;
            EmlVerifiedLaw altered;
            using (MemoryStream mutated = new(image, writable: false))
            using (CkptReader reader = new(mutated))
                altered = EmlVerifiedLaw.LoadVerified(reader, hasGuardSchema: true, hasWitnessContext: true, hasNodeFacts: true);
            return RejectReceiptMutation(baseline, candidate: altered);
        }
        catch (InvalidDataException)
        {
            return true;
        }
        catch (EndOfStreamException)
        {
            return true;
        }
    }


    private static EmlLawStore CreateLawStore()
    {
        EmlLawStore store = new();
        EmlVerifiedLaw law = EmlGuardedRewriteAssay.CreateVerifiedLaw();
        if (!store.TryAdmit(law, 0,
                out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)
            || !admission.FirstCapture)
            throw new InvalidDataException("rung-0 assay could not admit its guarded log-exp basis");
        return store;
    }

    private static EmlLawRewrite FindRewrite(
        EmlLawStore store,
        in EmlRewritePredictionCarrier carrier,
        string antecedent,
        string consequent)
    {
        EmlRewriteState state = carrier.CreateState(antecedent);
        List<EmlLawRewrite> rewrites = new();
        store.AppendRewritesForEvaluation(antecedent, rewrites, state.Evaluation);
        for (int i = 0; i < rewrites.Count; i++)
            if (rewrites[i].IsRung0Eligible
                && string.Equals(rewrites[i].ConsequentRpn, consequent, StringComparison.Ordinal))
                return rewrites[i];
        throw new InvalidDataException($"rung-0 assay did not produce {antecedent} -> {consequent}");
    }

    private static EmlRung0Proof FindCadenceSelectedProof(
        EmlLawStore store,
        in EmlRewritePredictionCarrier carrier,
        string antecedent,
        string consequent,
        ulong excludedDigest)
    {
        for (int salt = 0; salt < 512; salt++)
        {
            EmlRung0Budget budget = new(1, 2 + salt, 64);
            EmlRung0Result result = store.DeriveRung0(
                in carrier, antecedent, consequent, in budget);
            if (result.Proof is EmlRung0Proof proof
                && proof.Digest != excludedDigest
                && EmlRung0Digest.SelectNumericAudit(proof.Digest))
                return proof;
        }
        throw new InvalidDataException("rung-0 assay could not find a deterministic cadence-selected proof");
    }

    private static EmlRung0Proof CreatePortableRightToLeftProof(in EmlRung0Proof basis)
    {
        EmlCompositionStep source = basis.Steps[0];
        const string RulePattern = "? = 1?E";
        const string SubstitutionRpn = "1";
        const string AntecedentRpn = "11E";
        const string ConsequentRpn = "1";
        EmlGuardWitness witness = EmlGuardWitness.Create(
            EmlPath.Root,
            AntecedentRpn,
            SubstitutionRpn,
            AntecedentRpn,
            ConsequentRpn,
            source.GuardWitness.Enclosure,
            source.GuardWitness.Branch);
        EmlCompositionStep step = new(
            EmlRuleID.Create(RulePattern, EmlLawOrientations.RightToLeft,
                source.BasisLawDigest, source.DomainGuardDigest),
            EmlLawOrientations.RightToLeft,
            EmlPath.Root,
            SubstitutionRpn,
            AntecedentRpn,
            ConsequentRpn,
            witness,
            AntecedentRpn.Length,
            ConsequentRpn.Length,
            RulePattern,
            source.BasisLawDigest,
            source.DomainGuardDigest);
        EmlRung0Proof proof = basis with
        {
            AntecedentRPN = AntecedentRpn,
            ConsequentRPN = ConsequentRpn,
            Steps = [step],
            Digest = 0,
        };
        return proof with { Digest = EmlRung0Digest.Calculate(in proof) };
    }

    private static byte[] Save(EmlLawStore store)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) store.Save(writer);
        return stream.ToArray();
    }

    private static bool RejectProof(EmlRung0Proof proof)
    {
        proof = proof with { Digest = 0 };
        proof = proof with { Digest = EmlRung0Digest.Calculate(in proof) };
        try
        {
            CreateLawStore().RecordRung0Proof(in proof);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static EmlLawStore LoadStore(byte[] image)
    {
        EmlLawStore store = new();
        using MemoryStream stream = new(image, writable: false);
        using CkptReader reader = new(stream);
        store.Load(reader);
        return store;
    }

    private static bool CorruptDigestAndReject(byte[] image, ulong digest, Action<byte[]> load)
    {
        byte[] corrupted = (byte[])image.Clone();
        byte[] needle = BitConverter.GetBytes(digest);
        int offset = corrupted.AsSpan().IndexOf(needle);
        if (offset < 0) return false;
        corrupted[offset] ^= 0x01;
        return ThrowsInvalid(() => load(corrupted));
    }

    private static bool CorruptArchivedCertificateAndReject(
        byte[] image,
        in EmlLawBehaviorCertificate certificate)
    {
        using MemoryStream encoded = new();
        using (CkptWriter writer = new(encoded))
        {
            WriteSignature(writer, certificate.AtOne);
            WriteSignature(writer, certificate.AtX);
            WriteSignature(writer, certificate.AtY);
        }
        byte[] needle = encoded.ToArray();
        int offset = image.AsSpan().LastIndexOf(needle);
        if (offset < 0) return false;
        byte[] corrupted = (byte[])image.Clone();
        corrupted[offset] ^= 0x01;
        return ThrowsInvalid(() => _ = LoadStore(corrupted));
    }

    private static void WriteSignature(CkptWriter writer, in EmlSig signature)
    {
        writer.I64(signature.R1);
        writer.I64(signature.I1);
        writer.I64(signature.R2);
        writer.I64(signature.I2);
    }

    private static bool ThrowsInvalid(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
        catch (EndOfStreamException) { return true; }
        catch (FormatException) { return true; }
        catch (ArgumentException) { return true; }
    }

    private static void Append(StringBuilder report, string name, bool value)
        => report.Append(name).Append('\t').Append(value ? 1 : 0).AppendLine();
}
