namespace Cogito;

/// One semantic-class row in an EML law-store mutation.  Classes are a
/// replacement register because a new member can change both the member count
/// and the cheapest representative without appending a new key.
internal readonly record struct EmlLawClassCheckpointRow(
    EmlLawBehaviorCertificate Certificate,
    int Members,
    int FirstCapture,
    EmlVerifiedLaw Representative);

internal readonly record struct EmlVerifiedLawSupportStateDelta(
    string Digest,
    bool Consumed,
    TapeEventID? ExecutionEventID,
    TapeEventID? SupportEventID,
    int[] GeneratedPredictionIDs);

internal readonly record struct EmlPatternGrammarAdmissionStateDelta(
    string AuthorityID,
    string DomainID,
    EmlPatternGrammarAdmissionReceipt Receipt);

/// Typed mutation of the EML law store.  Historical proof/support journals use
/// cursors; mutable indexes and counters are explicit replacement state.
internal readonly record struct EmlLawStoreCheckpointDelta(
    int AdmissionCursor,
    string[] Admissions,
    EmlLawClassCheckpointRow[] Classes,
    long GeneratedOffers,
    long GeneratedMints,
    long DirectWitnessMatches,
    long FormFarmAttempted,
    long FormFarmAccepted,
    long FormFarmRejected,
    EmlEvaluatorInterval LastFormFarmEvaluation,
    EmlPredictionBoundRewriteCensus LastPredictionBoundRewriteCensus,
    int RewriteSearchRevision,
    int RewriteSearchBudget,
    ulong CompositionDigest,
    EmlCompositionStep[] CompositionSteps,
    EmlVerifiedLaw[] Rung0BasisArchive,
    int Rung0ProofCursor,
    EmlRung0Proof[] Rung0Proofs,
    int Rung0AuditCursor,
    EmlRung0Audit[] Rung0Audits,
    int Rung0TransitionCursor,
    EmlRung0RuleTransition[] Rung0RuleTransitions,
    EmlRuleID[] QuarantinedRung0Rules,
    int VerifiedLawSupportCursor,
    EmlVerifiedLawSupportReceipt[] VerifiedLawSupports,
    EmlVerifiedLawSupportStateDelta[] VerifiedLawSupportStates,
    EmlLawClassCheckpointRow[] ClassUpserts,
    EmlLawBehaviorCertificate[] ClassRemovals,
    int CompositionCursor,
    EmlVerifiedLaw[] BasisAdds,
    string[] BasisRemovals,
    EmlRuleID[] QuarantineAdds,
    EmlRuleID[] QuarantineRemovals,
    EmlVerifiedLawSupportStateDelta[] VerifiedLawSupportStateUpdates,
    EmlRung0Audit[]? Rung0AuditReplacements = null,
    int PatternGrammarAdmissionCursor = 0,
    EmlPatternGrammarAdmissionReceipt[]? PatternGrammarAdmissions = null,
    EmlPatternGrammarAdmissionStateDelta[]? PatternGrammarAdmissionUpdates = null,
    int PatternGrammarAdmissionEconomicsCursor = 0,
    EmlPatternGrammarAdmissionEconomicsRecord[]? PatternGrammarAdmissionEconomics = null);

internal sealed partial class EmlLawStore
{
    internal EmlLawStoreCheckpointDelta CaptureCheckpointDelta()
    {
        if (_checkpointAdmissionCount < 0 || _checkpointAdmissionCount > _admissionJournal.Count)
            throw new InvalidDataException("EML law admission checkpoint cursor is outside its journal");
        if (_checkpointRung0ProofCount < 0 || _checkpointRung0ProofCount > _rung0Proofs.Count
            || _checkpointRung0AuditCount < 0 || _checkpointRung0AuditCount > _rung0Audits.Count
            || _checkpointRung0TransitionCount < 0 || _checkpointRung0TransitionCount > _rung0RuleTransitions.Count
            || _checkpointVerifiedLawSupportCount < 0 || _checkpointVerifiedLawSupportCount > _verifiedLawSupports.Count
            || _checkpointPatternGrammarAdmissionCount < 0 || _checkpointPatternGrammarAdmissionCount > _theoryGrammarAdmissions.Count)
            throw new InvalidDataException("EML law journal checkpoint cursor is outside its journal");

        List<EmlLawClassCheckpointRow> classes = new(_classes.Classes.Count);
        foreach (KeyValuePair<EmlLawBehaviorCertificate, SemanticCASClass<EmlVerifiedLaw>> row in _classes.Classes)
            classes.Add(new(row.Key, row.Value.Members, row.Value.FirstCapture, row.Value.Rep));
        classes.Sort(static (left, right) => CompareCertificates(left.Certificate, right.Certificate));

        EmlVerifiedLawSupportStateDelta[] supportStates = new EmlVerifiedLawSupportStateDelta[_verifiedLawSupports.Count];
        for (int i = 0; i < supportStates.Length; i++)
        {
            EmlVerifiedLawSupportReceipt support = _verifiedLawSupports[i];
            supportStates[i] = new(support.Digest, support.Consumed, support.ExecutionEventID,
                support.SupportEventID, support.GeneratedPredictionIDs.ToArray());
        }

        Dictionary<EmlLawBehaviorCertificate, EmlLawClassCheckpointRow> previousClasses = _checkpointClasses ?? new();
        List<EmlLawClassCheckpointRow> classUpserts = new();
        foreach (EmlLawClassCheckpointRow row in classes)
        {
            if (!previousClasses.TryGetValue(row.Certificate, out EmlLawClassCheckpointRow prior)
                || row.Members != prior.Members || row.FirstCapture != prior.FirstCapture
                || !Equals(row.Representative, prior.Representative))
                classUpserts.Add(row);
        }
        List<EmlLawBehaviorCertificate> classRemovals = new();
        foreach (EmlLawBehaviorCertificate certificate in previousClasses.Keys)
            if (!_classes.Classes.ContainsKey(certificate)) classRemovals.Add(certificate);
        classRemovals.Sort(CompareCertificates);

        Dictionary<string, EmlVerifiedLaw> previousBasis = _checkpointBasis ?? new(StringComparer.Ordinal);
        List<EmlVerifiedLaw> basisAdds = new();
        foreach (KeyValuePair<string, EmlVerifiedLaw> row in _rung0BasisArchive)
            if (!previousBasis.ContainsKey(row.Key)) basisAdds.Add(row.Value);
        List<string> basisRemovals = new();
        foreach (string key in previousBasis.Keys)
            if (!_rung0BasisArchive.ContainsKey(key)) basisRemovals.Add(key);
        basisRemovals.Sort(StringComparer.Ordinal);

        HashSet<EmlRuleID> previousQuarantine = _checkpointQuarantine ?? new();
        EmlRuleID[] quarantineAdds = _quarantinedRung0Rules.Except(previousQuarantine).OrderBy(static rule => rule.Value, StringComparer.Ordinal).ToArray();
        EmlRuleID[] quarantineRemovals = previousQuarantine.Except(_quarantinedRung0Rules).OrderBy(static rule => rule.Value, StringComparer.Ordinal).ToArray();

        Dictionary<string, EmlVerifiedLawSupportStateDelta> previousStates = _checkpointSupportStates ?? new(StringComparer.Ordinal);
        List<EmlVerifiedLawSupportStateDelta> stateUpdates = new();
        for (int i = 0; i < supportStates.Length; i++)
        {
            EmlVerifiedLawSupportStateDelta state = supportStates[i];
            if (!previousStates.TryGetValue(state.Digest, out EmlVerifiedLawSupportStateDelta prior)
                || !SupportStateEquals(state, prior)) stateUpdates.Add(state);
        }

        Dictionary<ulong, EmlRung0Audit> previousAudits = _checkpointAudits ?? new();
        List<EmlRung0Audit> auditReplacements = new();
        for (int i = 0; i < _rung0Audits.Count; i++)
        {
            EmlRung0Audit audit = _rung0Audits[i];
            if (previousAudits.TryGetValue(audit.ProofDigest, out EmlRung0Audit prior) && !AuditEquals(prior, audit))
                auditReplacements.Add(audit);
        }

        Dictionary<string, EmlPatternGrammarAdmissionReceipt> previousAdmissions = _checkpointPatternGrammarAdmissions ?? new(StringComparer.Ordinal);
        List<EmlPatternGrammarAdmissionStateDelta> promotionUpdates = new();
        foreach (int i in _dirtyPatternGrammarAdmissionIndices.OrderBy(static index => index))
        {
            EmlPatternGrammarAdmissionReceipt promotion = _theoryGrammarAdmissions[i];
            string key = promotion.AuthorityID + "\u0001" + promotion.Domain.Value;
            if (previousAdmissions.TryGetValue(key, out EmlPatternGrammarAdmissionReceipt? prior)
                && prior is not null
                && !string.Equals(prior.Digest, promotion.Digest, StringComparison.Ordinal))
                promotionUpdates.Add(new(promotion.AuthorityID, promotion.Domain.Value, promotion));
        }

        // The four full-snapshot fields (Classes, Rung0BasisArchive, QuarantinedRung0Rules,
        // VerifiedLawSupportStates) are LEGACY-READ-ONLY: the schema-7 writer emits only the
        // incremental upserts/adds/removals/updates, and a freshly-captured delta always applies
        // incrementally (ClassUpserts is non-null). Capture therefore leaves them empty instead of
        // rebuilding O(total) snapshots the writer never touches. `classes`/`supportStates` above
        // stay live because the incremental diffs are computed from them.
        return new(
            _checkpointAdmissionCount,
            _admissionJournal.Skip(_checkpointAdmissionCount).ToArray(),
            Array.Empty<EmlLawClassCheckpointRow>(),
            GeneratedOffers,
            GeneratedMints,
            DirectWitnessMatches,
            FormFarmAttempted,
            FormFarmAccepted,
            FormFarmRejected,
            LastFormFarmEvaluation,
            LastPredictionBoundRewriteCensus,
            _rewriteSearchRevision,
            _rewriteSearchBudget,
            _derivationDigest,
            _derivationSteps.Skip(_checkpointCompositionCount).ToArray(),
            Array.Empty<EmlVerifiedLaw>(),
            _checkpointRung0ProofCount,
            _rung0Proofs.Skip(_checkpointRung0ProofCount).ToArray(),
            _checkpointRung0AuditCount,
            _rung0Audits.Skip(_checkpointRung0AuditCount).ToArray(),
            _checkpointRung0TransitionCount,
            _rung0RuleTransitions.Skip(_checkpointRung0TransitionCount).ToArray(),
            Array.Empty<EmlRuleID>(),
            _checkpointVerifiedLawSupportCount,
            _verifiedLawSupports.Skip(_checkpointVerifiedLawSupportCount).ToArray(),
            Array.Empty<EmlVerifiedLawSupportStateDelta>(),
            classUpserts.ToArray(), classRemovals.ToArray(),
            _checkpointCompositionCount,
            basisAdds.ToArray(), basisRemovals.ToArray(), quarantineAdds, quarantineRemovals,
            stateUpdates.ToArray(), auditReplacements.ToArray(), _checkpointPatternGrammarAdmissionCount,
            _theoryGrammarAdmissions.Skip(_checkpointPatternGrammarAdmissionCount).ToArray(), promotionUpdates.ToArray(),
            _checkpointPatternGrammarAdmissionEconomicsCount,
            _theoryGrammarAdmissionEconomics.Skip(_checkpointPatternGrammarAdmissionEconomicsCount).ToArray());
    }

    internal void ApplyCheckpointDelta(in EmlLawStoreCheckpointDelta delta)
    {
        if (delta.AdmissionCursor != _admissionJournal.Count)
            throw new InvalidDataException($"EML law admission cursor gap: expected {_admissionJournal.Count}, got {delta.AdmissionCursor}");
        if (delta.Rung0ProofCursor != _rung0Proofs.Count
            || delta.Rung0AuditCursor != _rung0Audits.Count
            || delta.Rung0TransitionCursor != _rung0RuleTransitions.Count
            || delta.VerifiedLawSupportCursor != _verifiedLawSupports.Count)
            throw new InvalidDataException("EML law journal cursor gap during replay");

        for (int i = 0; i < delta.Admissions.Length; i++)
        {
            string admission = delta.Admissions[i];
            if (!_admissions.Add(admission)) throw new InvalidDataException("EML law delta repeats an admission identity");
            _admissionJournal.Add(admission);
        }

        bool incremental = delta.ClassUpserts is not null;
        if (incremental)
        {
            for (int i = 0; i < delta.ClassRemovals!.Length; i++) _classes.Remove(delta.ClassRemovals[i]);
        }
        else _classes.Clear();
        HashSet<EmlLawBehaviorCertificate> classCertificates = new();
        EmlLawClassCheckpointRow[] classesToApply = incremental ? delta.ClassUpserts! : delta.Classes;
        for (int i = 0; i < classesToApply.Length; i++)
        {
            EmlLawClassCheckpointRow row = classesToApply[i];
            if (row.Members <= 0 || row.FirstCapture < 0 || row.Representative.Certificate != row.Certificate)
                throw new InvalidDataException("EML law delta carries an invalid semantic class");
            if (!classCertificates.Add(row.Certificate))
                throw new InvalidDataException("EML law delta repeats a semantic class certificate");
            _classes.Set(row.Certificate, new SemanticCASClass<EmlVerifiedLaw>(row.Representative, row.Members, row.FirstCapture));
        }

        GeneratedOffers = delta.GeneratedOffers;
        GeneratedMints = delta.GeneratedMints;
        DirectWitnessMatches = delta.DirectWitnessMatches;
        FormFarmAttempted = delta.FormFarmAttempted;
        FormFarmAccepted = delta.FormFarmAccepted;
        FormFarmRejected = delta.FormFarmRejected;
        LastFormFarmEvaluation = delta.LastFormFarmEvaluation;
        LastPredictionBoundRewriteCensus = delta.LastPredictionBoundRewriteCensus;
        if (GeneratedOffers < 0 || GeneratedMints < 0 || DirectWitnessMatches < 0
            || FormFarmAttempted < 0 || FormFarmAccepted < 0 || FormFarmRejected < 0
            || FormFarmAttempted != FormFarmAccepted + FormFarmRejected
            || LastFormFarmEvaluation.End < LastFormFarmEvaluation.Start)
            throw new InvalidDataException("EML law delta carries invalid accounting");

        _rewriteSearchRevision = delta.RewriteSearchRevision;
        _rewriteSearchBudget = delta.RewriteSearchBudget;
        _derivationDigest = delta.CompositionDigest;
        if (incremental)
        {
            if (delta.CompositionCursor != _derivationSteps.Count)
                throw new InvalidDataException($"EML law derivation cursor gap: expected {_derivationSteps.Count}, got {delta.CompositionCursor}");
            _derivationSteps.AddRange(delta.CompositionSteps);
        }
        else
        {
            _derivationSteps.Clear();
            _derivationSteps.AddRange(delta.CompositionSteps);
        }
        if (_rewriteSearchRevision < 1 || _rewriteSearchBudget < 1
            || EmlCompositionDigest.Calculate(_rewriteSearchRevision, _rewriteSearchBudget, _derivationSteps) != _derivationDigest)
            throw new InvalidDataException("EML law delta derivation digest mismatch");
        _rewriteSystem = null;

        if (!incremental) ClearRung0BasisArchive();
        string[] basisRemovalsToApply = incremental ? delta.BasisRemovals : Array.Empty<string>();
        for (int i = 0; i < basisRemovalsToApply.Length; i++) RemoveRung0BasisArchive(basisRemovalsToApply[i]);
        EmlVerifiedLaw[] basisToApply = incremental ? delta.BasisAdds : delta.Rung0BasisArchive;
        for (int i = 0; i < basisToApply.Length; i++)
        {
            EmlVerifiedLaw basis = basisToApply[i];
            string admissionID = CreateAdmissionID(basis);
            if (!AddRung0BasisArchive(admissionID, basis))
                throw new InvalidDataException("EML law delta repeats a rung-0 basis archive entry");
        }
        for (int i = 0; i < delta.Rung0Proofs.Length; i++)
        {
            EmlRung0Proof proof = delta.Rung0Proofs[i];
            ValidateRung0Proof(in proof);
            AppendRung0Proof(in proof);
        }
        for (int i = 0; i < delta.Rung0Audits.Length; i++)
        {
            EmlRung0Audit audit = delta.Rung0Audits[i];
            ValidateRung0Audit(in audit);
            AppendRung0Audit(in audit);
        }
        if (delta.Rung0AuditReplacements is EmlRung0Audit[] replacements)
        {
            for (int i = 0; i < replacements.Length; i++)
            {
                EmlRung0Audit replacement = replacements[i];
                ValidateRung0Audit(in replacement);
                // Replacement preserves ProofDigest, so the audit index slot stays valid.
                if (!_rung0AuditIndex.TryGetValue(replacement.ProofDigest, out int j))
                    throw new InvalidDataException("EML law delta audit replacement names an unknown proof");
                _rung0Audits[j] = replacement;
            }
        }
        for (int i = 0; i < delta.Rung0RuleTransitions.Length; i++)
        {
            EmlRung0RuleTransition transition = delta.Rung0RuleTransitions[i];
            if (transition.Sequence != _rung0RuleTransitions.Count)
                throw new InvalidDataException("EML law delta rung-0 transition sequence gap");
            ApplyRung0RuleTransition(in transition);
        }
        if (!incremental) _quarantinedRung0Rules.Clear();
        EmlRuleID[] quarantineToAdd = incremental ? delta.QuarantineAdds : delta.QuarantinedRung0Rules;
        for (int i = 0; i < quarantineToAdd.Length; i++)
            if (!_quarantinedRung0Rules.Add(quarantineToAdd[i]))
                throw new InvalidDataException("EML law delta repeats a quarantined rung-0 rule");
        if (incremental)
            for (int i = 0; i < delta.QuarantineRemovals.Length; i++) _quarantinedRung0Rules.Remove(delta.QuarantineRemovals[i]);

        _verifiedLawAuthorities.Clear();
        for (int i = 0; i < _verifiedLawSupports.Count; i++) IndexVerifiedLawAuthority(_verifiedLawSupports[i]);
        for (int i = 0; i < delta.VerifiedLawSupports.Length; i++)
        {
            EmlVerifiedLawSupportReceipt support = delta.VerifiedLawSupports[i];
            support.ValidateAfterLoad();
            if (!_verifiedLawSupportDigests.Add(support.Digest))
                throw new InvalidDataException("EML law delta repeats a verified-law support receipt");
            IndexVerifiedLawAuthority(support);
            _verifiedLawSupportIndexByDigest.Add(support.Digest, _verifiedLawSupports.Count);
            _verifiedLawSupports.Add(support);
            _verifiedLawSupportsByDigest.Add(support.Digest, support);
        }
        EmlVerifiedLawSupportStateDelta[] statesToApply = incremental ? delta.VerifiedLawSupportStateUpdates : delta.VerifiedLawSupportStates;
        if (!incremental && statesToApply.Length != _verifiedLawSupports.Count)
            throw new InvalidDataException("EML law delta support state count disagrees with the support journal");
        for (int i = 0; i < statesToApply.Length; i++)
        {
            EmlVerifiedLawSupportStateDelta state = statesToApply[i];
            if (!_verifiedLawSupportsByDigest.TryGetValue(state.Digest, out EmlVerifiedLawSupportReceipt? support))
                throw new InvalidDataException("EML law delta support state names an unknown receipt");
            support.RestoreCheckpointState(state.Consumed, state.ExecutionEventID, state.SupportEventID, state.GeneratedPredictionIDs);
        }
        if (delta.PatternGrammarAdmissions is EmlPatternGrammarAdmissionReceipt[] promotions)
        {
            if (delta.PatternGrammarAdmissionCursor != _theoryGrammarAdmissions.Count)
                throw new InvalidDataException("EML theory-grammar promotion cursor gap");
            for (int i = 0; i < promotions.Length; i++)
            {
                promotions[i].Validate();
                string key = promotions[i].AuthorityID + "\u0001" + promotions[i].Domain.Value;
                if (!_theoryGrammarAdmissionIndexByAuthorityDomain.TryAdd(key, _theoryGrammarAdmissions.Count))
                    throw new InvalidDataException("EML law delta repeats a theory-grammar promotion authority/domain");
                _theoryGrammarAdmissions.Add(promotions[i]);
                if (!promotions[i].Consumed) AddPendingPatternGrammarAdmission(_theoryGrammarAdmissions.Count - 1);
            }
            foreach (EmlPatternGrammarAdmissionStateDelta update in delta.PatternGrammarAdmissionUpdates ?? Array.Empty<EmlPatternGrammarAdmissionStateDelta>())
            {
                string key = update.AuthorityID + "\u0001" + update.DomainID;
                if (!_theoryGrammarAdmissionIndexByAuthorityDomain.TryGetValue(key, out int index))
                    throw new InvalidDataException("EML law delta promotion update names an unknown authority/domain");
                update.Receipt.Validate();
                if (!string.Equals(update.Receipt.AuthorityID, update.AuthorityID, StringComparison.Ordinal)
                    || !string.Equals(update.Receipt.Domain.Value, update.DomainID, StringComparison.Ordinal))
                    throw new InvalidDataException("EML law delta promotion update key disagrees with its receipt");
                EmlPatternGrammarAdmissionReceipt prior = _theoryGrammarAdmissions[index];
                bool wasConsumed = prior.Consumed;
                if (wasConsumed || !update.Receipt.Consumed || !AdmissionIdentityEquals(prior, update.Receipt))
                    throw new InvalidDataException("EML law delta promotion update is not the exact pending-to-consumed transition");
                _theoryGrammarAdmissions[index] = update.Receipt;
                int pendingIndex = _pendingPatternGrammarAdmissionIndices.IndexOf(index);
                if (pendingIndex < 0) throw new InvalidDataException("EML law delta promotion update lost its pending index");
                _pendingPatternGrammarAdmissionIndices.RemoveAt(pendingIndex);
            }
        }
        if (delta.PatternGrammarAdmissionEconomics is EmlPatternGrammarAdmissionEconomicsRecord[] economics)
        {
            if (delta.PatternGrammarAdmissionEconomicsCursor != _theoryGrammarAdmissionEconomics.Count)
                throw new InvalidDataException("EML theory-grammar economics cursor gap");
            for (int i = 0; i < economics.Length; i++)
            {
                economics[i].Validate(economics[i].Receipt.Encode());
                if (!_theoryGrammarAdmissionEconomicsIndex.TryAdd(economics[i].IdentityKey, _theoryGrammarAdmissionEconomics.Count))
                    throw new InvalidDataException("EML law delta repeats a theory-grammar economics identity");
                _theoryGrammarAdmissionEconomics.Add(economics[i]);
            }
        }
        _validatedSupportStates.Clear();
        _persistedLawExecutions.Clear();
        _persistedLawExecutionIndexMark = 0;
        SetCheckpointCursors();
        CaptureCheckpointBaseline();
    }

    // Commit advances the checkpoint cursors and rebaselines; it reads nothing from the
    // captured delta, so callers commit WITHOUT re-capturing an O(total) delta they discard.
    internal void CommitCheckpointDelta()
    {
        SetCheckpointCursors();
        CaptureCheckpointBaseline();
    }

    private void SetCheckpointCursors()
    {
        _checkpointAdmissionCount = _admissionJournal.Count;
        _checkpointCompositionCount = _derivationSteps.Count;
        _checkpointRung0ProofCount = _rung0Proofs.Count;
        _checkpointRung0AuditCount = _rung0Audits.Count;
        _checkpointRung0TransitionCount = _rung0RuleTransitions.Count;
        _checkpointVerifiedLawSupportCount = _verifiedLawSupports.Count;
        _checkpointPatternGrammarAdmissionCount = _theoryGrammarAdmissions.Count;
        _checkpointPatternGrammarAdmissionEconomicsCount = _theoryGrammarAdmissionEconomics.Count;
    }

    private Dictionary<EmlLawBehaviorCertificate, EmlLawClassCheckpointRow>? _checkpointClasses;
    private Dictionary<string, EmlVerifiedLaw>? _checkpointBasis;
    private HashSet<EmlRuleID>? _checkpointQuarantine;
    private Dictionary<string, EmlVerifiedLawSupportStateDelta>? _checkpointSupportStates;
    private Dictionary<ulong, EmlRung0Audit>? _checkpointAudits;
    private Dictionary<string, EmlPatternGrammarAdmissionReceipt>? _checkpointPatternGrammarAdmissions;

    private void CaptureCheckpointBaseline()
    {
        _checkpointClasses = _classes.Classes.ToDictionary(static pair => pair.Key,
            static pair => new EmlLawClassCheckpointRow(pair.Key, pair.Value.Members, pair.Value.FirstCapture, pair.Value.Rep));
        _checkpointBasis = new(_rung0BasisArchive, StringComparer.Ordinal);
        _checkpointQuarantine = new(_quarantinedRung0Rules);
        _checkpointSupportStates = new(StringComparer.Ordinal);
        foreach (EmlVerifiedLawSupportReceipt support in _verifiedLawSupports)
            _checkpointSupportStates[support.Digest] = CaptureSupportState(support);
        _checkpointAudits = new();
        for (int i = 0; i < _rung0Audits.Count; i++) _checkpointAudits[_rung0Audits[i].ProofDigest] = _rung0Audits[i];
        _checkpointPatternGrammarAdmissions = new(StringComparer.Ordinal);
        for (int i = 0; i < _theoryGrammarAdmissions.Count; i++)
        {
            EmlPatternGrammarAdmissionReceipt promotion = _theoryGrammarAdmissions[i];
            _checkpointPatternGrammarAdmissions[promotion.AuthorityID + "\u0001" + promotion.Domain.Value] = promotion;
        }
        _dirtyPatternGrammarAdmissionIndices.Clear();
    }

    private static EmlVerifiedLawSupportStateDelta CaptureSupportState(EmlVerifiedLawSupportReceipt support)
        => new(support.Digest, support.Consumed, support.ExecutionEventID, support.SupportEventID, support.GeneratedPredictionIDs.ToArray());

    private static bool SupportStateEquals(EmlVerifiedLawSupportStateDelta left, EmlVerifiedLawSupportStateDelta right)
        => left.Consumed == right.Consumed && left.ExecutionEventID == right.ExecutionEventID
            && left.SupportEventID == right.SupportEventID && left.GeneratedPredictionIDs.SequenceEqual(right.GeneratedPredictionIDs);

    private static bool AdmissionIdentityEquals(EmlPatternGrammarAdmissionReceipt left, EmlPatternGrammarAdmissionReceipt right)
        => left.Domain == right.Domain
            && string.Equals(left.AuthorityID, right.AuthorityID, StringComparison.Ordinal)
            && string.Equals(left.SupportAuthorityID, right.SupportAuthorityID, StringComparison.Ordinal)
            && string.Equals(left.SupportSetDigest, right.SupportSetDigest, StringComparison.Ordinal)
            && string.Equals(left.AdmissionID, right.AdmissionID, StringComparison.Ordinal)
            && string.Equals(left.CandidatePackageDigest, right.CandidatePackageDigest, StringComparison.Ordinal)
            && string.Equals(left.CanonicalFiller, right.CanonicalFiller, StringComparison.Ordinal)
            && left.GeneratedPrediction.PredictionID == right.GeneratedPrediction.PredictionID
            && left.GeneratedPrediction.LawExecutionEventID == right.GeneratedPrediction.LawExecutionEventID
            && left.GeneratedPrediction.SupportEventID == right.GeneratedPrediction.SupportEventID
            && string.Equals(left.GeneratedPrediction.Line, right.GeneratedPrediction.Line, StringComparison.Ordinal)
            && string.Equals(left.GeneratedPrediction.LhsRPN, right.GeneratedPrediction.LhsRPN, StringComparison.Ordinal)
            && string.Equals(left.GeneratedPrediction.RhsRPN, right.GeneratedPrediction.RhsRPN, StringComparison.Ordinal)
            && left.RankProof.SourceRank == right.RankProof.SourceRank
            && left.RankProof.GeneratedRank == right.RankProof.GeneratedRank
            && left.AdmissionRevision == right.AdmissionRevision
            && left.FillerAmplification == right.FillerAmplification
            && left.ReflectedTapeEventID == right.ReflectedTapeEventID;

    private static bool AuditEquals(in EmlRung0Audit left, in EmlRung0Audit right)
        => left.ProofDigest == right.ProofDigest && left.Status == right.Status
            && left.EvaluatorCalls == right.EvaluatorCalls && left.NumericVerified == right.NumericVerified
            && left.GuardVerified == right.GuardVerified && left.Selection == right.Selection
            && left.Rules.SequenceEqual(right.Rules);

    internal static void WriteCheckpointDelta(CkptWriter writer, in EmlLawStoreCheckpointDelta delta)
    {
        writer.U8(7);
        writer.I32(delta.AdmissionCursor); writer.I32(delta.Admissions.Length);
        foreach (string admission in delta.Admissions) writer.Str(admission);
        writer.I32(delta.ClassUpserts.Length);
        foreach (EmlLawClassCheckpointRow row in delta.ClassUpserts)
        {
            WriteCertificate(writer, row.Certificate); writer.I32(row.Members); writer.I32(row.FirstCapture); row.Representative.Save(writer);
        }
        writer.I32(delta.ClassRemovals.Length); foreach (EmlLawBehaviorCertificate certificate in delta.ClassRemovals) WriteCertificate(writer, certificate);
        writer.I64(delta.GeneratedOffers); writer.I64(delta.GeneratedMints); writer.I64(delta.DirectWitnessMatches);
        writer.I64(delta.FormFarmAttempted); writer.I64(delta.FormFarmAccepted); writer.I64(delta.FormFarmRejected);
        writer.I64(delta.LastFormFarmEvaluation.Start); writer.I64(delta.LastFormFarmEvaluation.End);
        EmlPredictionBoundRewriteCensus census = delta.LastPredictionBoundRewriteCensus;
        WriteRewriteCensus(writer, in census);
        writer.I32(delta.RewriteSearchRevision); writer.I32(delta.RewriteSearchBudget); writer.U64(delta.CompositionDigest);
        writer.I32(delta.CompositionCursor); writer.I32(delta.CompositionSteps.Length); foreach (EmlCompositionStep step in delta.CompositionSteps) WriteCompositionStep(writer, in step);
        writer.I32(delta.BasisAdds.Length); foreach (EmlVerifiedLaw basis in delta.BasisAdds) basis.Save(writer);
        writer.I32(delta.BasisRemovals.Length); foreach (string key in delta.BasisRemovals) writer.Str(key);
        writer.I32(delta.Rung0ProofCursor); writer.I32(delta.Rung0Proofs.Length); foreach (EmlRung0Proof proof in delta.Rung0Proofs) WriteRung0Proof(writer, in proof);
        writer.I32(delta.Rung0AuditCursor); writer.I32(delta.Rung0Audits.Length); foreach (EmlRung0Audit audit in delta.Rung0Audits) WriteRung0Audit(writer, in audit);
        writer.I32(delta.Rung0TransitionCursor); writer.I32(delta.Rung0RuleTransitions.Length); foreach (EmlRung0RuleTransition transition in delta.Rung0RuleTransitions) WriteRung0RuleTransition(writer, in transition);
        writer.I32(delta.QuarantineAdds.Length); foreach (EmlRuleID rule in delta.QuarantineAdds) writer.Str(rule.Value);
        writer.I32(delta.QuarantineRemovals.Length); foreach (EmlRuleID rule in delta.QuarantineRemovals) writer.Str(rule.Value);
        writer.I32(delta.VerifiedLawSupportCursor); writer.I32(delta.VerifiedLawSupports.Length); foreach (EmlVerifiedLawSupportReceipt support in delta.VerifiedLawSupports) WriteVerifiedLawSupport(writer, support);
        writer.I32(delta.VerifiedLawSupportStateUpdates.Length);
        foreach (EmlVerifiedLawSupportStateDelta state in delta.VerifiedLawSupportStateUpdates)
        {
            writer.Str(state.Digest); writer.Bool(state.Consumed); writer.Bool(state.ExecutionEventID.HasValue); if (state.ExecutionEventID is TapeEventID execution) writer.I64(execution.Value);
            writer.Bool(state.SupportEventID.HasValue); if (state.SupportEventID is TapeEventID packet) writer.I64(packet.Value);
            writer.I32(state.GeneratedPredictionIDs.Length); foreach (int claimID in state.GeneratedPredictionIDs) writer.I32(claimID);
        }
        EmlRung0Audit[] replacements = delta.Rung0AuditReplacements ?? Array.Empty<EmlRung0Audit>();
        writer.I32(replacements.Length);
        for (int i = 0; i < replacements.Length; i++) WriteRung0Audit(writer, in replacements[i]);
        writer.I32(delta.PatternGrammarAdmissionCursor);
        EmlPatternGrammarAdmissionReceipt[] promotions = delta.PatternGrammarAdmissions ?? Array.Empty<EmlPatternGrammarAdmissionReceipt>();
        writer.I32(promotions.Length);
        for (int i = 0; i < promotions.Length; i++) writer.Bytes(promotions[i].Encode());
        EmlPatternGrammarAdmissionStateDelta[] updates = delta.PatternGrammarAdmissionUpdates ?? Array.Empty<EmlPatternGrammarAdmissionStateDelta>();
        writer.I32(updates.Length);
        for (int i = 0; i < updates.Length; i++)
        {
            writer.Str(updates[i].AuthorityID); writer.Str(updates[i].DomainID); writer.Bytes(updates[i].Receipt.Encode());
        }
        writer.I32(delta.PatternGrammarAdmissionEconomicsCursor);
        EmlPatternGrammarAdmissionEconomicsRecord[] economics = delta.PatternGrammarAdmissionEconomics ?? Array.Empty<EmlPatternGrammarAdmissionEconomicsRecord>();
        writer.I32(economics.Length);
        for (int i = 0; i < economics.Length; i++) writer.Bytes(economics[i].Encode());
    }

    internal static EmlLawStoreCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte schema = reader.U8();
        if (schema is not (1 or 2 or 3 or 4 or 5 or 6 or 7)) throw new InvalidDataException("unknown EML law-store checkpoint delta version");
        int admissionCursor = ReadCount(reader, 1_000_000, "admission cursor"); int admissionCount = ReadCount(reader, 1_000_000, "admission count");
        string[] admissions = new string[admissionCount]; for (int i = 0; i < admissions.Length; i++) admissions[i] = reader.Str();
        int classCount = ReadCount(reader, 1_000_000, "class count"); EmlLawClassCheckpointRow[] classes = new EmlLawClassCheckpointRow[classCount];
        for (int i = 0; i < classes.Length; i++) classes[i] = new(ReadCertificate(reader), reader.I32(), reader.I32(), EmlVerifiedLaw.LoadVerified(reader, true, true, true));
        EmlLawClassCheckpointRow[] upserts = schema >= 2 ? classes : null!;
        EmlLawBehaviorCertificate[] removals = schema >= 2
            ? ReadCertificates(reader, ReadCount(reader, 1_000_000, "class removal count"))
            : null!;
        long generatedOffers = reader.I64(), generatedMints = reader.I64(), directWitnessMatches = reader.I64();
        long formFarmAttempted = reader.I64(), formFarmAccepted = reader.I64(), formFarmRejected = reader.I64();
        EmlEvaluatorInterval evaluation = new(reader.I64(), reader.I64()); EmlPredictionBoundRewriteCensus census = ReadRewriteCensus(reader);
        int revision = reader.I32(), budget = reader.I32(); ulong derivationDigest = reader.U64();
        int derivationCursor = schema >= 2 ? reader.I32() : 0;
        int derivationCount = ReadCount(reader, 1_000_000, "derivation count"); EmlCompositionStep[] derivations = new EmlCompositionStep[derivationCount]; for (int i = 0; i < derivations.Length; i++) derivations[i] = ReadCompositionStep(reader, false, true);
        int basisCount = ReadCount(reader, 4096 * 32, "basis count"); EmlVerifiedLaw[] basis = new EmlVerifiedLaw[basisCount]; for (int i = 0; i < basis.Length; i++) basis[i] = EmlVerifiedLaw.LoadVerified(reader, true, true, true);
        EmlVerifiedLaw[] basisAdds = schema >= 2 ? basis : null!;
        string[] basisRemovals = schema >= 2 ? ReadStrings(reader, ReadCount(reader, 4096 * 32, "basis removal count")) : null!;
        int proofCursor = reader.I32(); int proofCount = ReadCount(reader, 4096, "proof count"); EmlRung0Proof[] proofs = new EmlRung0Proof[proofCount]; for (int i = 0; i < proofs.Length; i++) proofs[i] = ReadRung0Proof(reader, true);
        int auditCursor = reader.I32(); int auditCount = ReadCount(reader, 4096, "audit count"); EmlRung0Audit[] audits = new EmlRung0Audit[auditCount]; for (int i = 0; i < audits.Length; i++) audits[i] = ReadRung0Audit(reader, hasSelection: schema >= 3);
        int transitionCursor = reader.I32(); int transitionCount = ReadCount(reader, 4096 * 32, "transition count"); EmlRung0RuleTransition[] transitions = new EmlRung0RuleTransition[transitionCount]; for (int i = 0; i < transitions.Length; i++) transitions[i] = ReadRung0RuleTransition(reader);
        int quarantineCount = ReadCount(reader, 4096, "quarantine count"); EmlRuleID[] quarantined = new EmlRuleID[quarantineCount]; for (int i = 0; i < quarantined.Length; i++) quarantined[i] = new(reader.Str());
        EmlRuleID[] quarantineAdds = schema >= 2 ? quarantined : null!;
        int quarantineRemovalCount = schema >= 2 ? ReadCount(reader, 4096, "quarantine removal count") : 0;
        EmlRuleID[] quarantineRemovals = schema >= 2 ? new EmlRuleID[quarantineRemovalCount] : null!;
        if (schema >= 2) for (int i = 0; i < quarantineRemovals.Length; i++) quarantineRemovals[i] = new(reader.Str());
        int supportCursor = reader.I32(); int supportCount = ReadCount(reader, 4096 * 32, "support count"); EmlVerifiedLawSupportReceipt[] supports = new EmlVerifiedLawSupportReceipt[supportCount]; for (int i = 0; i < supports.Length; i++) supports[i] = ReadVerifiedLawSupport(reader, true, true);
        int stateCount = ReadCount(reader, 4096 * 32, "support state count"); EmlVerifiedLawSupportStateDelta[] states = new EmlVerifiedLawSupportStateDelta[stateCount];
        for (int i = 0; i < states.Length; i++)
        {
            string digest = reader.Str(); bool consumed = reader.Bool(); TapeEventID? execution = reader.Bool() ? new TapeEventID(reader.I64()) : null; TapeEventID? packet = reader.Bool() ? new TapeEventID(reader.I64()) : null;
            int generatedCount = ReadCount(reader, 4096 * 32, "generated claim count"); int[] generated = new int[generatedCount]; for (int j = 0; j < generated.Length; j++) generated[j] = reader.I32();
            states[i] = new(digest, consumed, execution, packet, generated);
        }
        EmlRung0Audit[]? replacements = schema >= 4
            ? ReadAudits(reader, ReadCount(reader, 4096, "audit replacement count"))
            : null;
        int promotionCursor = 0;
        EmlPatternGrammarAdmissionReceipt[]? promotions = null;
        EmlPatternGrammarAdmissionStateDelta[]? promotionUpdates = null;
        int economicsCursor = 0;
        EmlPatternGrammarAdmissionEconomicsRecord[]? economics = null;
        if (schema >= 5)
        {
            promotionCursor = ReadCount(reader, 4096 * 32, "theory-grammar promotion cursor");
            int promotionCount = ReadCount(reader, 4096 * 32, "theory-grammar promotion count");
            promotions = new EmlPatternGrammarAdmissionReceipt[promotionCount];
            for (int i = 0; i < promotions.Length; i++)
                promotions[i] = EmlPatternGrammarAdmissionReceipt.Decode(reader.Bytes(1 << 20));
            if (schema >= 6)
            {
                int updateCount = ReadCount(reader, 4096 * 32, "theory-grammar promotion update count");
                promotionUpdates = new EmlPatternGrammarAdmissionStateDelta[updateCount];
                for (int i = 0; i < promotionUpdates.Length; i++)
                    promotionUpdates[i] = new(reader.Str(), reader.Str(), EmlPatternGrammarAdmissionReceipt.Decode(reader.Bytes(1 << 20)));
            }
        }
        if (schema >= 7)
        {
            economicsCursor = ReadCount(reader, 4096 * 32, "theory-grammar economics cursor");
            int economicsCount = ReadCount(reader, 4096 * 32, "theory-grammar economics count");
            economics = new EmlPatternGrammarAdmissionEconomicsRecord[economicsCount];
            for (int i = 0; i < economics.Length; i++)
                economics[i] = EmlPatternGrammarAdmissionEconomicsRecord.Decode(reader.Bytes(1 << 20));
        }
        return new(admissionCursor, admissions, classes, generatedOffers, generatedMints, directWitnessMatches, formFarmAttempted, formFarmAccepted, formFarmRejected, evaluation, census, revision, budget, derivationDigest, derivations, basis, proofCursor, proofs, auditCursor, audits, transitionCursor, transitions, quarantined, supportCursor, supports, states,
            upserts, removals, derivationCursor, basisAdds, basisRemovals, quarantineAdds, quarantineRemovals, schema >= 2 ? states : null!, replacements,
            promotionCursor, promotions, promotionUpdates, economicsCursor, economics);
    }

    private static EmlLawBehaviorCertificate[] ReadCertificates(CkptReader reader, int count)
    {
        EmlLawBehaviorCertificate[] values = new EmlLawBehaviorCertificate[count];
        for (int i = 0; i < count; i++) values[i] = ReadCertificate(reader);
        return values;
    }

    private static string[] ReadStrings(CkptReader reader, int count)
    {
        string[] values = new string[count];
        for (int i = 0; i < count; i++) values[i] = reader.Str();
        return values;
    }

    private static EmlRung0Audit[] ReadAudits(CkptReader reader, int count)
    {
        EmlRung0Audit[] values = new EmlRung0Audit[count];
        for (int i = 0; i < values.Length; i++) values[i] = ReadRung0Audit(reader, hasSelection: true);
        return values;
    }

    private static int ReadCount(CkptReader reader, int max, string name)
    {
        int count = reader.I32();
        if (count < 0 || count > max) throw new InvalidDataException($"EML law delta {name} is invalid");
        return count;
    }

    private static void WriteRewriteCensus(CkptWriter w, in EmlPredictionBoundRewriteCensus c)
    {
        w.I32(c.Calls); w.I32(c.Forms); w.I32(c.CarrierBound); w.I32(c.FormsWithRewrites); w.I32(c.Rewrites); w.I32(c.GuardEligible); w.I32(c.RankReducing); w.I32(c.MaxForms); w.I32(c.MaxCarrierBound); w.I32(c.MaxFormsWithRewrites); w.I32(c.MaxRewrites); w.I32(c.MaxGuardEligible); w.I32(c.MaxRankReducing); w.I32(c.FirstPredictionID); w.Str(c.FirstLawID); w.Str(c.FirstRewriteID); w.Str(c.FirstOrientation); w.Str(c.FirstForm); w.Str(c.FirstRulePattern); w.Str(c.FirstMatchedTerm); w.Str(c.FirstRewriteAntecedent); w.Str(c.FirstRewriteConsequent); w.I32(c.FirstReducingPredictionID); w.Str(c.FirstReducingLawID); w.Str(c.FirstReducingRewriteID); w.Str(c.FirstReducingOrientation); w.Str(c.FirstReducingForm); w.Str(c.FirstReducingAntecedent); w.Str(c.FirstReducingConsequent);
    }

    private static EmlPredictionBoundRewriteCensus ReadRewriteCensus(CkptReader r)
        => new(r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.I32(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str(), r.Str());
}
