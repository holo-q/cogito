namespace Cogito;

/// The append-only part of one EML checkpoint mutation.  The mint journal is
/// the source address for every proof, so its cursor is checked before any
/// child record is accepted.  The transient queues are copied as replacement
/// state; they are bounded by the current draw and are not historical logs.
internal readonly record struct EmlPredictionMintEventDelta(EmlPredictionID PredictionID, TapeEventID EventID);
internal readonly record struct EmlPredictionCompositionEventDelta(EmlPredictionID PredictionID, TapeEventID EventID);

internal readonly record struct EmlSieveCheckpointDelta(
    int MintCursor,
    EmlMint[] Mints,
    EmlCert[] MintCertificates,
    TapeEventID[][] MintOpportunityEvents,
    int ObligationCursor,
    EmlObligation[] Obligations,
    int ResidualProofCursor,
    EmlResidualProof[] ResidualProofs,
    int ExactCompositionCursor,
    EmlExactCompositionObligation[] ExactCompositions,
    int ClosureCursor,
    EmlObligationClosure[] Closures,
    int ProcessProofCursor,
    EmlProcessResidualProof[] ProcessProofs,
    int ComposedFormProofCursor,
    EmlComposedFormProof[] ComposedFormProofs,
    int DeliberationAdmissionCursor,
    EmlDeliberationAdmission[] DeliberationAdmissions,
    int DeliberationPhaseCursor,
    EmlDeliberationPhaseReceipt[] DeliberationPhases,
    int DeliberationSettlementCursor,
    EmlDeliberationSettlement[] DeliberationSettlements,
    EmlPredictionMintEventDelta[] PredictionMintEvents,
    EmlPredictionCompositionEventDelta[] PredictionCompositionEvents,
    int Identities,
    int ValueHits,
    int KFrontier,
    long FiniteOffers,
    EmlMint[] PendingMints,
    EmlCertificateDelta[] PendingSemanticDeltas)
{
    internal bool IsEmpty
        => Mints.Length == 0 && Obligations.Length == 0 && ResidualProofs.Length == 0
            && ExactCompositions.Length == 0 && Closures.Length == 0 && ProcessProofs.Length == 0
            && ComposedFormProofs.Length == 0 && DeliberationAdmissions.Length == 0
            && DeliberationPhases.Length == 0 && DeliberationSettlements.Length == 0
            && PredictionMintEvents.Length == 0 && PredictionCompositionEvents.Length == 0
            && PendingMints.Length == 0 && PendingSemanticDeltas.Length == 0;
}

public sealed partial class EmlSieve
{
    private int _checkpointMintCount;
    private int _checkpointObligationCount;
    private int _checkpointResidualProofCount;
    private int _checkpointExactCompositionCount;
    private int _checkpointClosureCount;
    private int _checkpointProcessProofCount;
    private int _checkpointComposedFormProofCount;
    private int _checkpointDeliberationAdmissions;
    private int _checkpointDeliberationPhases;
    private int _checkpointDeliberationSettlements;
    private readonly List<EmlPredictionMintEventDelta> _checkpointPredictionMintEvents = new();
    private readonly List<EmlPredictionCompositionEventDelta> _checkpointPredictionCompositionEvents = new();

    internal EmlSieveCheckpointDelta CaptureCheckpointDelta()
    {
        if (_checkpointMintCount < 0 || _checkpointMintCount > _mintLog.Count)
            throw new InvalidDataException("EML mint checkpoint cursor is outside the append-only journal");
        if (_checkpointObligationCount < 0 || _checkpointObligationCount > _obligations.Count)
            throw new InvalidDataException("EML obligation checkpoint cursor is outside the append-only register");
        if (_checkpointResidualProofCount < 0 || _checkpointResidualProofCount > _residualProofs.Count)
            throw new InvalidDataException("EML residual-proof checkpoint cursor is outside the append-only log");
        ValidateCursor(_checkpointExactCompositionCount, _exactCompositionObligations.Count, "exact derivation");
        ValidateCursor(_checkpointClosureCount, _obligationClosures.Count, "obligation closure");
        ValidateCursor(_checkpointProcessProofCount, _processResidualProofs.Count, "process residual proof");
        ValidateCursor(_checkpointComposedFormProofCount, _derivedFormProofs.Count, "derived-form proof");
        ValidateCursor(_checkpointDeliberationAdmissions, _deliberationJournal.Admissions.Count, "deliberation admission");
        ValidateCursor(_checkpointDeliberationPhases, _deliberationJournal.Phases.Count, "deliberation phase");
        ValidateCursor(_checkpointDeliberationSettlements, _deliberationJournal.Settlements.Count, "deliberation settlement");

        int mintCount = _mintLog.Count - _checkpointMintCount;
        EmlMint[] mints = mintCount == 0 ? [] : _mintLog.GetRange(_checkpointMintCount, mintCount).ToArray();
        EmlCert[] certificates = mintCount == 0 ? [] : _mintCerts.GetRange(_checkpointMintCount, mintCount).ToArray();
        if (certificates.Length != mints.Length)
            throw new InvalidDataException("EML mint checkpoint certificates are not parallel to the mint journal");
        TapeEventID[][] opportunities = mintCount == 0 ? [] : new TapeEventID[mintCount][];
        for (int i = 0; i < opportunities.Length; i++)
            opportunities[i] = _mintOpportunityEvents[_checkpointMintCount + i].ToArray();
        EmlObligation[] obligations = _obligations.Count == _checkpointObligationCount
            ? [] : _obligations.GetRange(_checkpointObligationCount, _obligations.Count - _checkpointObligationCount).ToArray();
        EmlResidualProof[] residuals = _residualProofs.Count == _checkpointResidualProofCount
            ? [] : _residualProofs.GetRange(_checkpointResidualProofCount, _residualProofs.Count - _checkpointResidualProofCount).ToArray();
        EmlExactCompositionObligation[] exactCompositions = _exactCompositionObligations.Skip(_checkpointExactCompositionCount).ToArray();
        EmlObligationClosure[] closures = _obligationClosures.Skip(_checkpointClosureCount).ToArray();
        EmlProcessResidualProof[] processProofs = _processResidualProofs.Skip(_checkpointProcessProofCount).ToArray();
        EmlComposedFormProof[] derivedFormProofs = _derivedFormProofs.Skip(_checkpointComposedFormProofCount).ToArray();
        EmlDeliberationAdmission[] admissions = _deliberationJournal.Admissions.Skip(_checkpointDeliberationAdmissions).ToArray();
        EmlDeliberationPhaseReceipt[] phases = _deliberationJournal.Phases.Skip(_checkpointDeliberationPhases).ToArray();
        EmlDeliberationSettlement[] settlements = _deliberationJournal.Settlements.Skip(_checkpointDeliberationSettlements).ToArray();
        return new(
            _checkpointMintCount, mints, certificates, opportunities,
            _checkpointObligationCount, obligations,
            _checkpointResidualProofCount, residuals,
            _checkpointExactCompositionCount, exactCompositions,
            _checkpointClosureCount, closures,
            _checkpointProcessProofCount, processProofs,
            _checkpointComposedFormProofCount, derivedFormProofs,
            _checkpointDeliberationAdmissions, admissions,
            _checkpointDeliberationPhases, phases,
            _checkpointDeliberationSettlements, settlements,
            _checkpointPredictionMintEvents.ToArray(),
            _checkpointPredictionCompositionEvents.ToArray(),
            Identities, ValueHits, KFrontier, FiniteOffers,
            _newMints.ToArray(), _newSemanticDeltas.ToArray());
    }

    internal void CommitCheckpointDelta(in EmlSieveCheckpointDelta delta)
    {
        if (delta.MintCursor != _checkpointMintCount || delta.ObligationCursor != _checkpointObligationCount
            || delta.ResidualProofCursor != _checkpointResidualProofCount
            || delta.ExactCompositionCursor != _checkpointExactCompositionCount
            || delta.ClosureCursor != _checkpointClosureCount
            || delta.ProcessProofCursor != _checkpointProcessProofCount
            || delta.ComposedFormProofCursor != _checkpointComposedFormProofCount
            || delta.DeliberationAdmissionCursor != _checkpointDeliberationAdmissions
            || delta.DeliberationPhaseCursor != _checkpointDeliberationPhases
            || delta.DeliberationSettlementCursor != _checkpointDeliberationSettlements)
            throw new InvalidDataException("EML checkpoint commit does not match the captured cursors");
        _checkpointMintCount = _mintLog.Count;
        _checkpointObligationCount = _obligations.Count;
        _checkpointResidualProofCount = _residualProofs.Count;
        _checkpointExactCompositionCount = _exactCompositionObligations.Count;
        _checkpointClosureCount = _obligationClosures.Count;
        _checkpointProcessProofCount = _processResidualProofs.Count;
        _checkpointComposedFormProofCount = _derivedFormProofs.Count;
        _checkpointDeliberationAdmissions = _deliberationJournal.Admissions.Count;
        _checkpointDeliberationPhases = _deliberationJournal.Phases.Count;
        _checkpointDeliberationSettlements = _deliberationJournal.Settlements.Count;
        _checkpointPredictionMintEvents.Clear();
        _checkpointPredictionCompositionEvents.Clear();
    }

    private static void ValidateCursor(int cursor, int count, string label)
    {
        if (cursor < 0 || cursor > count)
            throw new InvalidDataException($"EML {label} checkpoint cursor is outside its append-only log");
    }

    private static int ReadCursor(CkptReader reader, string label)
    {
        int cursor = reader.I32();
        if (cursor < 0) throw new InvalidDataException($"invalid EML {label} delta cursor");
        return cursor;
    }

    private static int ReadCount(CkptReader reader, string label)
    {
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"invalid EML {label} delta count");
        return count;
    }

    private void ApplyExactCompositions(EmlExactCompositionObligation[] targets)
    {
        foreach (EmlExactCompositionObligation target in targets)
        {
            EmlPredictionID source = target.SourcePredictionID;
            if ((uint)source.Value >= (uint)_mintLog.Count || (uint)source.Value >= (uint)_mintCerts.Count)
                throw new InvalidDataException("EML exact derivation delta source is outside the mint journal");
            if (_exactCompositionBySource.ContainsKey(source))
                throw new InvalidDataException("EML exact derivation delta repeats a source claim");
            if (target.Supports.Count == 0 || target.Supports.Any(static id => id.Value < 0)
                || !target.Supports.SequenceEqual(target.Supports.OrderBy(static id => id.Value))
                || target.Supports.Distinct().Count() != target.Supports.Count)
                throw new InvalidDataException("EML exact derivation delta supports are not canonical");
            EmlMint mint = _mintLog[source.Value];
            if (target.MintEventID is not TapeEventID mintEvent
                || !_claimMintEvents.TryGetValue(source, out TapeEventID boundMintEvent)
                || boundMintEvent != mintEvent
                || (uint)source.Value >= (uint)_mintOpportunityEvents.Count
                || !target.Supports.SequenceEqual(_mintOpportunityEvents[source.Value]))
                throw new InvalidDataException("EML exact derivation delta world binding mismatch");
            if (mint.Grade != 'E' || !EmlPrediction.TryParse(mint.Line, out EmlPrediction claim)
                || !claim.RhsRpn || !string.Equals(claim.Lhs, target.CarrierRPN, StringComparison.Ordinal)
                || !string.Equals(target.SourceDigest, Digest(mint.Line), StringComparison.Ordinal)
                || target.SourceCertificate != _mintCerts[source.Value]
                || !string.Equals(target.Identity, ComputeExactCompositionIdentity(source, target.SourceDigest,
                    target.CarrierRPN, target.SourceCertificate, target.Supports, target.MintEventID), StringComparison.Ordinal))
                throw new InvalidDataException("EML exact derivation delta source identity mismatch");
            _exactCompositionBySource.Add(source, _exactCompositionObligations.Count);
            _exactCompositionObligations.Add(target);
        }
    }

    private void ApplyClosures(EmlObligationClosure[] closures)
    {
        foreach (EmlObligationClosure closure in closures)
        {
            if ((uint)closure.SourcePredictionID.Value >= (uint)_mintLog.Count)
                throw new InvalidDataException("EML closure delta source is outside the mint journal");
            if (!TryReadTargetIdentity(closure.SourcePredictionID, out string identity, out EmlObligationTargetSpecies species)
                || identity != closure.ObligationID || species != closure.Species)
                throw new InvalidDataException("EML closure delta source identity mismatch");
            string candidateDigest = closure.FiniteEvidence?.CandidateDigest
                ?? closure.ProcessEvidence?.CandidateDigest
                ?? closure.Rung0ComposedFormEvidence?.CandidateDigest ?? "none";
            if (closure.AttemptID != ComputeAttemptID(closure.ObligationID, closure.Kind,
                    closure.FinitePolicy, closure.ProcessPolicy, candidateDigest))
                throw new InvalidDataException("EML closure delta attempt identity mismatch");
            if (closure.AttachmentID.Length != 0
                && closure.AttachmentID != ComputeAttachmentID(closure.ObligationID, closure.Kind, candidateDigest))
                throw new InvalidDataException("EML closure delta attachment identity mismatch");
            if (closure.Closed && !_obligationClosureKeys.TryAdd(closure.AttachmentID, _obligationClosures.Count))
                throw new InvalidDataException("EML closure delta repeats an accepted attachment");
            _obligationClosures.Add(closure);
        }
        ValidateObligationClosures();
    }

    private void ApplyProcessProofs(EmlProcessResidualProof[] proofs)
    {
        foreach (EmlProcessResidualProof proof in proofs)
        {
            if ((uint)proof.SourcePredictionID.Value >= (uint)_mintLog.Count)
                throw new InvalidDataException("EML process proof delta source is outside the mint journal");
            string key = proof.SourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0001" + proof.Digest;
            if (!_processResidualProofKeys.Add(key)) throw new InvalidDataException("EML process proof delta repeats a proof");
            _processResidualProofs.Add(proof);
        }
    }

    private void ApplyComposedFormProofs(EmlComposedFormProof[] proofs)
    {
        foreach (EmlComposedFormProof proof in proofs)
        {
            EmlRung0Proof rung0Proof = proof.Proof;
            EmlRung0Audit rung0Audit = proof.Audit;
            EmlMint derivedMint = (uint)proof.ComposedPredictionID.Value < (uint)_mintLog.Count ? _mintLog[proof.ComposedPredictionID.Value] : default;
            bool derivedPredictionValid = EmlPrediction.TryParse(derivedMint.Line, out EmlPrediction derivedPrediction)
                && !derivedPrediction.Tilde && string.Equals(derivedPrediction.Lhs, proof.Program, StringComparison.Ordinal)
                && string.Equals(derivedPrediction.Rhs, rung0Proof.AntecedentRPN, StringComparison.Ordinal);
            if ((uint)proof.SourcePredictionID.Value >= (uint)_mintLog.Count
                || (uint)proof.ComposedPredictionID.Value >= (uint)_mintLog.Count
                || rung0Proof.PredictionID != proof.SourcePredictionID
                || !rung0Proof.IsValidShape
                || proof.Certificate.Grade != 'E'
                || !derivedPredictionValid
                || !string.Equals(proof.Program, rung0Proof.ConsequentRPN, StringComparison.Ordinal)
                || !string.Equals(rung0Proof.SourceDigest, Digest(_mintLog[proof.SourcePredictionID.Value].Line), StringComparison.Ordinal)
                || rung0Audit.Status == EmlRung0AuditStatuses.Disagreed
                || !IsValidComposedAudit(in rung0Proof, in rung0Audit))
                throw new InvalidDataException("EML derived-form proof delta failed reconstruction");
            string key = proof.SourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\u0001" + rung0Proof.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
            if (!_derivedFormProofKeys.Add(key)) throw new InvalidDataException("EML derived-form proof delta repeats a proof");
            _derivedFormProofs.Add(proof);
        }
    }

    private static void WriteExactComposition(CkptWriter w, in EmlExactCompositionObligation target)
    {
        w.I32(target.SourcePredictionID.Value); w.Str(target.Identity); w.Str(target.SourceDigest); w.Str(target.CarrierRPN);
        WriteCert(w, target.SourceCertificate); w.I32(target.Supports.Count);
        foreach (TapeEventID id in target.Supports) w.I64(id.Value);
        w.Bool(target.MintEventID.HasValue); if (target.MintEventID is TapeEventID mint) w.I64(mint.Value);
    }

    private static EmlExactCompositionObligation ReadExactComposition(CkptReader r)
    {
        EmlPredictionID source = new(r.I32()); string identity = r.Str(); string digest = r.Str(); string carrier = r.Str();
        EmlCert certificate = ReadCert(r); int count = ReadCount(r, "exact derivation support");
        if (count == 0 || count > 1024) throw new InvalidDataException("EML exact derivation support set is invalid");
        TapeEventID[] supports = new TapeEventID[count];
        for (int i = 0; i < count; i++) supports[i] = new TapeEventID(r.I64());
        TapeEventID? mint = r.Bool() ? new TapeEventID(r.I64()) : null;
        return new(source, identity, digest, carrier, certificate, supports, mint);
    }

    private static void WriteClosure(CkptWriter w, in EmlObligationClosure closure)
    {
        w.I32(closure.SourcePredictionID.Value); w.Str(closure.ObligationID); w.Str(closure.AttemptID); w.Str(closure.AttachmentID);
        w.U8((byte)closure.Status); w.Str(closure.SourceDigest); w.U8((byte)closure.Kind);
        w.Bool(closure.FinitePolicy.HasValue);
        if (closure.FinitePolicy is EmlFiniteObligationProofPolicy finite)
        { w.I32(finite.SignatureDigits); w.I32(finite.WitnessVersion); w.Str(finite.VerifierRevision); }
        w.Bool(closure.ProcessPolicy.HasValue);
        if (closure.ProcessPolicy is EmlProcessObligationProofPolicy process)
        { w.I32(process.SignatureDigits); w.I64(process.FuelPerProbe); w.I32(process.ProbeCount); w.I32(process.FunctionVersion); w.I32(process.CompositionVersion); w.Str(process.VerifierRevision); }
        w.Bool(closure.FiniteEvidence.HasValue);
        if (closure.FiniteEvidence is EmlFiniteObligationProofEvidence finiteEvidence)
        { w.I64(finiteEvidence.Evaluator.Start); w.I64(finiteEvidence.Evaluator.End); w.I64(finiteEvidence.WallTicks); w.Str(finiteEvidence.CandidateDigest); w.Str(finiteEvidence.AttachmentDigest); WriteCert(w, finiteEvidence.Before); WriteCert(w, finiteEvidence.After); }
        w.Bool(closure.ProcessEvidence.HasValue);
        if (closure.ProcessEvidence is EmlProcessObligationProofEvidence processEvidence)
        { w.I64(processEvidence.Evaluator.Start); w.I64(processEvidence.Evaluator.End); w.I64(processEvidence.WallTicks); w.I64(processEvidence.FuelPerProbe); w.I64(processEvidence.FuelTotal); w.Str(processEvidence.CandidateDigest); w.Str(processEvidence.AttachmentDigest); w.Str(processEvidence.CertificateDigest); WriteCert(w, processEvidence.Before); WriteCert(w, processEvidence.After); }
        w.Str(closure.Reason); w.U8((byte)closure.Species); w.Bool(closure.Rung0ComposedFormEvidence.HasValue);
        if (closure.Rung0ComposedFormEvidence is EmlRung0ComposedFormObligationEvidence evidence)
        {
            w.I32(evidence.ObligationPredictionID.Value); w.Str(evidence.ObligationID); w.I32(evidence.ComposedPredictionID.Value); w.Str(evidence.LhsRPN); w.Str(evidence.RhsRPN);
            w.Str(evidence.GuardPackageDigest); w.Str(evidence.ProofID); w.Str(evidence.AuditID); w.Str(evidence.ProofSHA256); w.Str(evidence.AuditSHA256);
            w.Str(evidence.AdmissionID); w.Str(evidence.ClosureID); w.Str(evidence.Comparator); w.I64(evidence.Evaluator.Start); w.I64(evidence.Evaluator.End);
            w.I64(evidence.ComparatorEvaluation.Start); w.I64(evidence.ComparatorEvaluation.End); w.Str(evidence.CandidateDigest); w.Str(evidence.AttachmentDigest);
            WriteCert(w, evidence.Before); WriteCert(w, evidence.After); w.Str(evidence.AdmissionPathCanonical); w.Str(evidence.AdmissionPathFingerprint);
        }
    }

    private static EmlObligationClosure ReadClosure(CkptReader r)
    {
        EmlPredictionID source = new(r.I32()); string obligationID = r.Str(); string attemptID = r.Str(); string attachmentID = r.Str();
        EmlObligationClosureStatuses status = (EmlObligationClosureStatuses)r.U8(); if (!Enum.IsDefined(status)) throw new InvalidDataException("invalid EML closure status");
        string sourceDigest = r.Str(); EmlObligationProofKinds kind = (EmlObligationProofKinds)r.U8(); if (!Enum.IsDefined(kind)) throw new InvalidDataException("invalid EML closure kind");
        EmlFiniteObligationProofPolicy? finite = r.Bool() ? new(r.I32(), r.I32(), r.Str()) : null;
        EmlProcessObligationProofPolicy? process = r.Bool() ? new(r.I32(), r.I64(), r.I32(), r.I32(), r.I32(), r.Str()) : null;
        EmlFiniteObligationProofEvidence? finiteEvidence = r.Bool() ? new(new EmlEvaluatorInterval(r.I64(), r.I64()), r.I64(), r.Str(), r.Str(), ReadCert(r), ReadCert(r)) : null;
        EmlProcessObligationProofEvidence? processEvidence = r.Bool() ? new(new EmlEvaluatorInterval(r.I64(), r.I64()), r.I64(), r.I64(), r.I64(), r.Str(), r.Str(), r.Str(), ReadCert(r), ReadCert(r)) : null;
        string reason = r.Str(); EmlObligationTargetSpecies species = (EmlObligationTargetSpecies)r.U8(); if (!Enum.IsDefined(species)) throw new InvalidDataException("invalid EML closure target species");
        EmlRung0ComposedFormObligationEvidence? rung0 = null;
        if (r.Bool())
        {
            EmlPredictionID witnessSource = new(r.I32()); string witnessObligation = r.Str(); EmlPredictionID witnessComposed = new(r.I32()); string lhs = r.Str(); string rhs = r.Str();
            string guard = r.Str(); string proofID = r.Str(); string auditID = r.Str(); string proofSHA = r.Str(); string auditSHA = r.Str(); string admissionID = r.Str(); string closureID = r.Str(); string comparator = r.Str();
            EmlEvaluatorInterval evaluation = new(r.I64(), r.I64()); EmlEvaluatorInterval comparatorEvaluation = new(r.I64(), r.I64()); string candidate = r.Str(); string attachment = r.Str();
            rung0 = new(witnessSource, witnessObligation, witnessComposed, lhs, rhs, guard, proofID, auditID, proofSHA, auditSHA, admissionID, closureID, comparator, evaluation, comparatorEvaluation, candidate, attachment, ReadCert(r), ReadCert(r), r.Str(), r.Str());
        }
        return new(source, obligationID, attemptID, attachmentID, status, sourceDigest, kind, finite, process, finiteEvidence, processEvidence, reason, rung0, species);
    }

    private static void WriteProcessProof(CkptWriter w, in EmlProcessResidualProof proof)
    {
        w.I32(proof.SourcePredictionID.Value); w.I32((int)proof.Function.Algorithm); w.I32(EmlProcessFunctions.AlgorithmVersion);
        w.Str(proof.Function.NumeratorRPN); w.Str(proof.Function.DenominatorRPN); w.I64(proof.Function.Fuel); w.Bool(proof.CompositionLaw.HasValue);
        if (proof.CompositionLaw is EmlResidualCompositionLaws law) w.I32((int)law);
        w.Str(proof.Digest); w.U8((byte)proof.Certificate.Grade); WriteSig(w, proof.Certificate.Limit); w.I64(proof.Certificate.RateRe); w.I64(proof.Certificate.RateIm); w.I64(proof.ProcessFuel);
    }

    private static EmlProcessResidualProof ReadProcessProof(CkptReader r)
    {
        EmlPredictionID source = new(r.I32()); EmlProcessFunctionAlgorithms algorithm = (EmlProcessFunctionAlgorithms)r.I32(); int version = r.I32();
        if (version != EmlProcessFunctions.AlgorithmVersion) throw new InvalidDataException("unsupported EML process proof delta version");
        EmlProcessFunction function = new(algorithm, version, r.Str(), r.Str(), r.I64());
        EmlResidualCompositionLaws? law = r.Bool() ? (EmlResidualCompositionLaws)r.I32() : null; string digest = r.Str();
        EmlCert certificate = new((char)r.U8(), ReadSig(r), r.I64(), r.I64()); return new(source, function, law, digest, certificate, r.I64());
    }

    internal void ApplyCheckpointDelta(in EmlSieveCheckpointDelta delta)
    {
        if (delta.MintCursor != _mintLog.Count)
            throw new InvalidDataException($"EML mint checkpoint cursor gap: expected {_mintLog.Count}, got {delta.MintCursor}");
        if (delta.ObligationCursor != _obligations.Count)
            throw new InvalidDataException($"EML obligation checkpoint cursor gap: expected {_obligations.Count}, got {delta.ObligationCursor}");
        if (delta.ResidualProofCursor != _residualProofs.Count)
            throw new InvalidDataException($"EML residual-proof checkpoint cursor gap: expected {_residualProofs.Count}, got {delta.ResidualProofCursor}");
        if (delta.ExactCompositionCursor != _exactCompositionObligations.Count)
            throw new InvalidDataException($"EML exact-derivation checkpoint cursor gap: expected {_exactCompositionObligations.Count}, got {delta.ExactCompositionCursor}");
        if (delta.ClosureCursor != _obligationClosures.Count)
            throw new InvalidDataException($"EML closure checkpoint cursor gap: expected {_obligationClosures.Count}, got {delta.ClosureCursor}");
        if (delta.ProcessProofCursor != _processResidualProofs.Count)
            throw new InvalidDataException($"EML process-proof checkpoint cursor gap: expected {_processResidualProofs.Count}, got {delta.ProcessProofCursor}");
        if (delta.ComposedFormProofCursor != _derivedFormProofs.Count)
            throw new InvalidDataException($"EML derived-form-proof checkpoint cursor gap: expected {_derivedFormProofs.Count}, got {delta.ComposedFormProofCursor}");
        if (delta.DeliberationAdmissionCursor != _deliberationJournal.Admissions.Count
            || delta.DeliberationPhaseCursor != _deliberationJournal.Phases.Count
            || delta.DeliberationSettlementCursor != _deliberationJournal.Settlements.Count)
            throw new InvalidDataException("EML deliberation checkpoint cursor gap");
        if (delta.MintCertificates.Length != delta.Mints.Length)
            throw new InvalidDataException("EML mint delta certificates are not parallel to the mint journal");
        if (delta.MintOpportunityEvents.Length != delta.Mints.Length)
            throw new InvalidDataException("EML mint delta opportunities are not parallel to the mint journal");

        for (int i = 0; i < delta.Mints.Length; i++)
        {
            EmlMint mint = delta.Mints[i];
            EmlCert certificate = delta.MintCertificates[i];
            if (certificate.Grade != mint.Grade
                || (mint.Grade == 'E' && certificate.Limit != mint.Sig))
                throw new InvalidDataException("EML mint delta certificate does not match its mint");
            _mintLog.Add(mint);
            _mintCerts.Add(certificate);
            _mintOpportunityEvents.Add(delta.MintOpportunityEvents[i].Distinct().OrderBy(static id => id.Value).ToArray());
            _gradeCounts[GradeIdx(mint.Grade)]++;
            if (EmlPrediction.TryParse(mint.Line, out EmlPrediction claim))
                _minted.Add(mint.Prog + "\u0001" + claim.Rhs);
        }
        // Exact derivations validate their world witness against this binding
        // map.  Install the append-only claim→event edges before consuming any
        // proof tails from the same mutation, so a record is self-contained.
        foreach (EmlPredictionMintEventDelta binding in delta.PredictionMintEvents)
        {
            RejectExactEventRebind(binding.PredictionID, binding.EventID);
            if (_claimMintEvents.TryGetValue(binding.PredictionID, out TapeEventID existing) && existing != binding.EventID)
                throw new InvalidDataException("EML claim/mint event delta attempts a rebind");
            _claimMintEvents[binding.PredictionID] = binding.EventID;
        }
        foreach (EmlPredictionCompositionEventDelta binding in delta.PredictionCompositionEvents)
        {
            if ((uint)binding.PredictionID.Value >= (uint)_mintLog.Count || binding.EventID.Value < 0)
                throw new InvalidDataException("EML claim derivation event binding is invalid");
            if (_claimCompositionEvents.TryGetValue(binding.PredictionID, out TapeEventID existing) && existing != binding.EventID)
                throw new InvalidDataException("EML claim derivation event delta attempts a rebind");
            _claimCompositionEvents[binding.PredictionID] = binding.EventID;
        }
        foreach (EmlObligation obligation in delta.Obligations)
        {
            if ((uint)obligation.SourcePredictionID.Value >= (uint)_mintLog.Count)
                throw new InvalidDataException("EML obligation delta source is outside the mint journal");
            if (!_obligationBySource.TryAdd(obligation.SourcePredictionID, _obligations.Count))
                throw new InvalidDataException("EML obligation delta repeats a source claim");
            _obligations.Add(obligation);
            _obligationOpportunityEvents[obligation.SourcePredictionID] = obligation.OpportunityEventIDs ?? Array.Empty<TapeEventID>();
            if (obligation.MintEventID is TapeEventID eventID) _obligationMintEvents[obligation.SourcePredictionID] = eventID;
        }
        foreach (EmlResidualProof proof in delta.ResidualProofs)
        {
            string key = proof.SourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\u0001" + proof.Program;
            if (!_residualProofKeys.Add(key)) throw new InvalidDataException("EML residual-proof delta repeats a proof");
            _residualProofs.Add(proof);
        }
        ApplyExactCompositions(delta.ExactCompositions);
        ApplyProcessProofs(delta.ProcessProofs);
        ApplyComposedFormProofs(delta.ComposedFormProofs);
        ApplyClosures(delta.Closures);
        _deliberationJournal.ApplyCheckpointDelta(
            delta.DeliberationAdmissionCursor, delta.DeliberationAdmissions,
            delta.DeliberationPhaseCursor, delta.DeliberationPhases,
            delta.DeliberationSettlementCursor, delta.DeliberationSettlements);
        Identities = delta.Identities;
        ValueHits = delta.ValueHits;
        KFrontier = delta.KFrontier;
        FiniteOffers = delta.FiniteOffers;
        _newMints.Clear(); _newMints.AddRange(delta.PendingMints);
        _newSemanticDeltas.Clear(); _newSemanticDeltas.AddRange(delta.PendingSemanticDeltas);
        RebuildCas();
        RebuildExactRPNForms();
        RebuildObligationLookup();
        _checkpointMintCount = _mintLog.Count;
        _checkpointObligationCount = _obligations.Count;
        _checkpointResidualProofCount = _residualProofs.Count;
        _checkpointExactCompositionCount = _exactCompositionObligations.Count;
        _checkpointClosureCount = _obligationClosures.Count;
        _checkpointProcessProofCount = _processResidualProofs.Count;
        _checkpointComposedFormProofCount = _derivedFormProofs.Count;
        _checkpointDeliberationAdmissions = _deliberationJournal.Admissions.Count;
        _checkpointDeliberationPhases = _deliberationJournal.Phases.Count;
        _checkpointDeliberationSettlements = _deliberationJournal.Settlements.Count;
        _checkpointPredictionMintEvents.Clear();
        _checkpointPredictionCompositionEvents.Clear();
    }

    internal static void WriteCheckpointDelta(CkptWriter w, in EmlSieveCheckpointDelta delta)
    {
        if (delta.MintCertificates.Length != delta.Mints.Length
            || delta.MintOpportunityEvents.Length != delta.Mints.Length)
            throw new InvalidDataException("EML mint delta arrays are not parallel");
        // Version 3 carries the per-mint certificate alongside each append.
        // A mint line alone cannot reconstruct the provenance used by exact
        // derivation admission (A-grade rate fields are not in EmlMint), so
        // older mutation records are intentionally rejected on replay.
        w.U8(4);
        w.I32(delta.MintCursor); w.I32(delta.Mints.Length);
        for (int i = 0; i < delta.Mints.Length; i++)
        {
            EmlMint mint = delta.Mints[i];
            w.Str(mint.Line); w.Str(mint.Prog); WriteSig(w, mint.Sig); w.U8((byte)mint.Grade); w.Bool(mint.Corrob);
            WriteCert(w, delta.MintCertificates[i]);
            TapeEventID[] events = delta.MintOpportunityEvents[i]; w.I32(events.Length);
            foreach (TapeEventID id in events) w.I64(id.Value);
        }
        w.I32(delta.ObligationCursor); w.I32(delta.Obligations.Length);
        foreach (EmlObligation obligation in delta.Obligations)
        {
            w.I32(obligation.SourcePredictionID.Value); w.Str(obligation.Identity);
            IReadOnlyList<TapeEventID> opportunities = obligation.OpportunityEventIDs ?? Array.Empty<TapeEventID>();
            w.I32(opportunities.Count); foreach (TapeEventID id in opportunities) w.I64(id.Value);
            w.Bool(obligation.MintEventID.HasValue); if (obligation.MintEventID is TapeEventID eventID) w.I64(eventID.Value);
        }
        w.I32(delta.ResidualProofCursor); w.I32(delta.ResidualProofs.Length);
        foreach (EmlResidualProof proof in delta.ResidualProofs)
        {
            w.I32(proof.SourcePredictionID.Value); w.Str(proof.Program); w.U8((byte)proof.Certificate.Grade);
            WriteSig(w, proof.Certificate.Limit); w.I64(proof.Certificate.RateRe); w.I64(proof.Certificate.RateIm);
        }
        w.I32(delta.ExactCompositionCursor); w.I32(delta.ExactCompositions.Length);
        foreach (EmlExactCompositionObligation target in delta.ExactCompositions) WriteExactComposition(w, in target);
        w.I32(delta.ClosureCursor); w.I32(delta.Closures.Length);
        foreach (EmlObligationClosure closure in delta.Closures) WriteClosure(w, in closure);
        w.I32(delta.ProcessProofCursor); w.I32(delta.ProcessProofs.Length);
        foreach (EmlProcessResidualProof proof in delta.ProcessProofs) WriteProcessProof(w, in proof);
        w.I32(delta.ComposedFormProofCursor); w.I32(delta.ComposedFormProofs.Length);
        foreach (EmlComposedFormProof proof in delta.ComposedFormProofs)
        {
            w.I32(proof.SourcePredictionID.Value); w.I32(proof.ComposedPredictionID.Value); w.Str(proof.Program);
            WriteCert(w, proof.Certificate); EmlRung0Checkpoint.WriteProof(w, proof.Proof); EmlRung0Checkpoint.WriteAudit(w, proof.Audit);
        }
        w.I32(delta.DeliberationAdmissionCursor); w.I32(delta.DeliberationAdmissions.Length);
        foreach (EmlDeliberationAdmission admission in delta.DeliberationAdmissions) EmlDeliberationJournal.WriteCheckpointAdmission(w, in admission);
        w.I32(delta.DeliberationPhaseCursor); w.I32(delta.DeliberationPhases.Length);
        foreach (EmlDeliberationPhaseReceipt phase in delta.DeliberationPhases) EmlDeliberationJournal.WriteCheckpointPhase(w, in phase);
        w.I32(delta.DeliberationSettlementCursor); w.I32(delta.DeliberationSettlements.Length);
        foreach (EmlDeliberationSettlement settlement in delta.DeliberationSettlements) EmlDeliberationJournal.WriteCheckpointSettlement(w, in settlement);
        w.I32(delta.PredictionMintEvents.Length);
        foreach (EmlPredictionMintEventDelta binding in delta.PredictionMintEvents) { w.I32(binding.PredictionID.Value); w.I64(binding.EventID.Value); }
        w.I32(delta.PredictionCompositionEvents.Length);
        foreach (EmlPredictionCompositionEventDelta binding in delta.PredictionCompositionEvents) { w.I32(binding.PredictionID.Value); w.I64(binding.EventID.Value); }
        w.I32(delta.Identities); w.I32(delta.ValueHits); w.I32(delta.KFrontier); w.I64(delta.FiniteOffers);
        w.I32(delta.PendingMints.Length);
        foreach (EmlMint mint in delta.PendingMints) { w.Str(mint.Line); w.Str(mint.Prog); WriteSig(w, mint.Sig); w.U8((byte)mint.Grade); w.Bool(mint.Corrob); }
        w.I32(delta.PendingSemanticDeltas.Length);
        foreach (EmlCertificateDelta semantic in delta.PendingSemanticDeltas)
        {
            w.U8((byte)semantic.Change); w.I32(semantic.PredictionID.Value);
            w.Bool(semantic.Before.HasValue); if (semantic.Before is EmlCert before) WriteCert(w, before);
            w.Bool(semantic.After.HasValue); if (semantic.After is EmlCert after) WriteCert(w, after);
            w.I64(semantic.Evaluation.Start); w.I64(semantic.Evaluation.End); w.I32(semantic.DescriptionBits);
        }
    }

    internal static EmlSieveCheckpointDelta ReadCheckpointDelta(CkptReader r)
    {
        byte version = r.U8();
        if (version is not (3 or 4)) throw new InvalidDataException("unsupported EML checkpoint delta version (mint certificates required)");
        int mintCursor = r.I32(); int mintCount = r.I32(); if (mintCursor < 0 || mintCount < 0) throw new InvalidDataException("invalid EML mint delta cursor");
        EmlMint[] mints = new EmlMint[mintCount]; EmlCert[] certificates = new EmlCert[mintCount]; TapeEventID[][] opportunities = new TapeEventID[mintCount][];
        for (int i = 0; i < mintCount; i++) { mints[i] = new(r.Str(), r.Str(), ReadSig(r), (char)r.U8(), r.Bool()); certificates[i] = ReadCert(r); int n = r.I32(); if (n < 0 || n > 1024) throw new InvalidDataException("invalid EML mint opportunity count"); opportunities[i] = new TapeEventID[n]; for (int j = 0; j < n; j++) opportunities[i][j] = new TapeEventID(r.I64()); }
        int obligationCursor = r.I32(); int obligationCount = r.I32(); if (obligationCursor < 0 || obligationCount < 0) throw new InvalidDataException("invalid EML obligation delta cursor");
        EmlObligation[] obligations = new EmlObligation[obligationCount];
        for (int i = 0; i < obligationCount; i++) { EmlPredictionID claim = new(r.I32()); string identity = r.Str(); int n = r.I32(); if (n < 0 || n > 1024) throw new InvalidDataException("invalid EML obligation opportunity count"); TapeEventID[] events = new TapeEventID[n]; for (int j = 0; j < n; j++) events[j] = new TapeEventID(r.I64()); TapeEventID? mint = r.Bool() ? new TapeEventID(r.I64()) : null; obligations[i] = new(claim, identity, events, mint); }
        int proofCursor = r.I32(); int proofCount = r.I32(); if (proofCursor < 0 || proofCount < 0) throw new InvalidDataException("invalid EML proof delta cursor");
        EmlResidualProof[] proofs = new EmlResidualProof[proofCount];
        for (int i = 0; i < proofCount; i++) proofs[i] = new(new EmlPredictionID(r.I32()), r.Str(), new EmlCert((char)r.U8(), ReadSig(r), r.I64(), r.I64()));
        int exactCursor = 0; EmlExactCompositionObligation[] exact = [];
        int closureCursor = 0; EmlObligationClosure[] closures = [];
        int processCursor = 0; EmlProcessResidualProof[] process = [];
        int derivedCursor = 0; EmlComposedFormProof[] derived = [];
        int admissionCursor = 0; EmlDeliberationAdmission[] admissions = [];
        int phaseCursor = 0; EmlDeliberationPhaseReceipt[] phases = [];
        int settlementCursor = 0; EmlDeliberationSettlement[] settlements = [];
        if (version >= 2)
        {
            exactCursor = ReadCursor(r, "exact derivation"); int exactCount = ReadCount(r, "exact derivation");
            exact = new EmlExactCompositionObligation[exactCount];
            for (int i = 0; i < exact.Length; i++) exact[i] = ReadExactComposition(r);
            closureCursor = ReadCursor(r, "closure"); int closureCount = ReadCount(r, "closure");
            closures = new EmlObligationClosure[closureCount];
            for (int i = 0; i < closures.Length; i++) closures[i] = ReadClosure(r);
            processCursor = ReadCursor(r, "process proof"); int processCount = ReadCount(r, "process proof");
            process = new EmlProcessResidualProof[processCount];
            for (int i = 0; i < process.Length; i++) process[i] = ReadProcessProof(r);
            derivedCursor = ReadCursor(r, "derived-form proof"); int derivedCount = ReadCount(r, "derived-form proof");
            derived = new EmlComposedFormProof[derivedCount];
            for (int i = 0; i < derived.Length; i++)
            {
                EmlPredictionID source = new(r.I32()); EmlPredictionID result = new(r.I32()); string program = r.Str();
                derived[i] = new(source, result, program, ReadCert(r), EmlRung0Checkpoint.ReadProof(r), EmlRung0Checkpoint.ReadAudit(r, hasSelection: version >= 4));
            }
            admissionCursor = ReadCursor(r, "deliberation admission"); int admissionCount = ReadCount(r, "deliberation admission");
            admissions = new EmlDeliberationAdmission[admissionCount];
            for (int i = 0; i < admissions.Length; i++) admissions[i] = EmlDeliberationJournal.ReadCheckpointAdmission(r);
            phaseCursor = ReadCursor(r, "deliberation phase"); int phaseCount = ReadCount(r, "deliberation phase");
            phases = new EmlDeliberationPhaseReceipt[phaseCount];
            for (int i = 0; i < phases.Length; i++) phases[i] = EmlDeliberationJournal.ReadCheckpointPhase(r);
            settlementCursor = ReadCursor(r, "deliberation settlement"); int settlementCount = ReadCount(r, "deliberation settlement");
            settlements = new EmlDeliberationSettlement[settlementCount];
            for (int i = 0; i < settlements.Length; i++) settlements[i] = EmlDeliberationJournal.ReadCheckpointSettlement(r);
        }
        int bindingCount = r.I32(); if (bindingCount < 0) throw new InvalidDataException("invalid EML claim binding count"); EmlPredictionMintEventDelta[] bindings = new EmlPredictionMintEventDelta[bindingCount];
        for (int i = 0; i < bindingCount; i++) bindings[i] = new(new EmlPredictionID(r.I32()), new TapeEventID(r.I64()));
        int derivationBindingCount = r.I32(); if (derivationBindingCount < 0) throw new InvalidDataException("invalid EML claim derivation binding count"); EmlPredictionCompositionEventDelta[] derivationBindings = new EmlPredictionCompositionEventDelta[derivationBindingCount];
        for (int i = 0; i < derivationBindingCount; i++) derivationBindings[i] = new(new EmlPredictionID(r.I32()), new TapeEventID(r.I64()));
        int identities = r.I32(), values = r.I32(), frontier = r.I32(); long offers = r.I64();
        int pendingCount = r.I32(); if (pendingCount < 0) throw new InvalidDataException("invalid EML pending mint count"); EmlMint[] pending = new EmlMint[pendingCount];
        for (int i = 0; i < pendingCount; i++) pending[i] = new(r.Str(), r.Str(), ReadSig(r), (char)r.U8(), r.Bool());
        int semanticCount = r.I32(); if (semanticCount < 0 || semanticCount > 1_000_000) throw new InvalidDataException("invalid EML semantic delta count");
        EmlCertificateDelta[] semantics = new EmlCertificateDelta[semanticCount];
        for (int i = 0; i < semanticCount; i++)
        {
            EmlCertificateChanges change = (EmlCertificateChanges)r.U8();
            if (!Enum.IsDefined(change)) throw new InvalidDataException("unknown EML semantic delta kind");
            EmlPredictionID claim = new(r.I32());
            EmlCert? before = r.Bool() ? ReadCert(r) : null;
            EmlCert? after = r.Bool() ? ReadCert(r) : null;
            semantics[i] = new(change, claim, before, after, new EmlEvaluatorInterval(r.I64(), r.I64()), r.I32());
        }
        return new(mintCursor, mints, certificates, opportunities, obligationCursor, obligations, proofCursor, proofs,
            exactCursor, exact, closureCursor, closures, processCursor, process, derivedCursor, derived,
            admissionCursor, admissions, phaseCursor, phases, settlementCursor, settlements,
            bindings, derivationBindings, identities, values, frontier, offers, pending, semantics);
    }
}
