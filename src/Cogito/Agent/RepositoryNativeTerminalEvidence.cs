namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// The terminal native repository authority is captured from the mounted
/// runtime, then written as one self-contained RON document. It carries bytes,
/// typed rows, and historical digests; a later adjudicator can consume this
/// document without reopening the source repository or the live tape/journal.
internal static class RepositoryNativeTerminalEvidence
{
    internal const string FileName = "repository-native-terminal.ron";

    internal static RepositoryNativeTerminalEvidenceRON Capture(
        Run? run,
        RepositoryNativeRuntimeSnapshot runtime,
        Tape tape,
        LoopLineageTapeSnapshot preSealTape,
        JournalSnapshot preSealJournal,
        string? immutableAuthoritySHA256,
        RepositoryLoopClosureTapeSeal seal,
        LoopLineageTapeSnapshot finalTape,
        JournalSnapshot finalJournal,
        RepositoryNativeRegisteredAuthorityRON? registeredAuthority = null)
    {
        RepositoryNativeTerminalEvidenceRON document = new()
        {
            schemaVersion = 4,
            runID = run is null ? "" : Run.RunIDFromDirectory(run.Dir),
            root = runtime.Root,
            glob = runtime.Glob,
            query = runtime.Query,
            querySHA256 = runtime.QuerySHA256,
            sourceAuthoritySHA256 = runtime.SourceAuthoritySHA256,
            worldSHA256 = runtime.World.WorldSHA256,
            worldSnapshotSHA256 = runtime.World.SnapshotSHA256,
            accessSHA256 = runtime.Access.AccessSHA256,
            accessAuthoritySHA256 = "",
            frontierRevision = runtime.Frontier.Revision.Value,
            frontierRuntimeAuthoritySHA256 = runtime.Frontier.AuthoritySHA256,
            theoryCommittedAuthoritySHA256 = runtime.Pattern.CommittedAuthoritySHA256,
            theoryPendingAuthoritySHA256 = runtime.Pattern.PendingAuthoritySHA256,
            preSealTapeSHA256 = preSealTape.Digest,
            preSealJournalSHA256 = preSealJournal.JournalSHA256,
            immutableAuthoritySHA256 = immutableAuthoritySHA256,
            tapeSHA256 = finalTape.Digest,
            journalSHA256 = finalJournal.JournalSHA256,
            sealEventID = seal.EventID.Value,
            sealPayloadSHA256 = seal.PayloadSHA256,
            sealReceiptSHA256 = seal.ReceiptSHA256,
        };
        if (registeredAuthority is not null)
        {
            registeredAuthority.Validate();
            document.registeredAuthority = registeredAuthority;
        }
        document.worldFiles.AddRange(runtime.World.Files.Select(static file => new RepositoryNativeWorldFileRON
        {
            path = file.Path.Value, bytes = file.Bytes, sha256 = file.SHA256,
            contentBase64 = Convert.ToBase64String(file.Content.ToArray()),
        }));
        document.accessEntries.AddRange(CaptureAccessRows(runtime.Access.Entries));
        document.accessAuthoritySHA256 = ComputeAccessAuthoritySHA256(document.accessEntries);
        RepositoryLoopClosureAccessSnapshot accessSnapshot = new(
            runtime.Access.Entries.ToArray(), runtime.World);
        accessSnapshot.Validate();
        document.accessSnapshotSHA256 = accessSnapshot.AccessSHA256;
        document.frontierCandidates.AddRange(CaptureCandidateRows(runtime.Frontier.Candidates));
        document.frontierTransitions.AddRange(CaptureTransitionRows(runtime.Frontier.Transitions));
        document.observedPaths.AddRange(runtime.Frontier.ObservedPaths);
        document.theoryOccurrences.AddRange(CapturePatternOccurrenceRows(runtime.Pattern.Occurrences));
        document.theoryCompositions.AddRange(CapturePatternCompositionRows(runtime.Pattern.Compositions));
        document.theoryAdmissions.AddRange(CapturePatternAdmissionRows(runtime.Pattern.Admissions));
        document.pendingAdmissions.AddRange(CapturePendingAdmissionRows(runtime.Pattern.PendingAdmissions));
        document.theoryRule = new RepositoryNativePatternRuleRON
        {
            id = runtime.Pattern.Rule.ID.Value, canonical = runtime.Pattern.Rule.Canonical,
            derivedSpecies = (byte)runtime.Pattern.Rule.ComposedSpecies,
            derivedAdmissionPath = runtime.Pattern.Rule.ComposedAdmissionPath,
            alternativeAdmissionPath = runtime.Pattern.Rule.AlternativeAdmissionPath,
        };
        List<RepositoryLoopClosureFrontierSelectionCorroboration> frontierSelections = CaptureFrontierSelections(tape);
        document.frontierSelections.AddRange(frontierSelections.Select(static selection => new RepositoryNativeSelectionRON
        {
            revision = selection.Revision.Value, runtimeAuthoritySHA256 = selection.RuntimeAuthoritySHA256,
            ordinal = selection.Ordinal, selectionEventID = selection.SelectionEventID.Value,
            selectionReceiptSHA256 = selection.SelectionReceiptSHA256,
            candidateDigest = selection.CandidateDigest.Value, candidateCanonical = selection.CandidateCanonical,
        }));
        RepositoryLoopClosureFrontierSnapshot frontierSnapshot = new(
            runtime.Frontier.Revision, runtime.Frontier.Candidates, runtime.Frontier.Transitions,
            runtime.Frontier.ObservedPaths, runtime.Frontier.AuthoritySHA256, frontierSelections);
        frontierSnapshot.Validate();
        document.frontierSnapshotSHA256 = frontierSnapshot.FrontierSHA256;
        RepositoryLoopClosurePatternSnapshot patternSnapshot = new(
            runtime.Pattern.Rule, runtime.Pattern.Occurrences, runtime.Pattern.Compositions, runtime.Pattern.Admissions,
            runtime.Pattern.PendingAdmissionDigests.ToArray(), runtime.Pattern.PendingAuthoritySHA256,
            runtime.Pattern.PendingAdmissions.Select(static pending => pending.Canonical).ToArray());
        patternSnapshot.Validate();
        document.theorySnapshotSHA256 = patternSnapshot.PatternSHA256;
        document.accessKeyframeBase64 = EncodeState(runtime.Access.SaveState);
        document.frontierKeyframeBase64 = EncodeState(runtime.FrontierState.SaveState);
        document.theoryKeyframeBase64 = EncodeState(runtime.PatternState.SaveState);
        document.tapeEvents.AddRange(CaptureTapeEvents(tape));
        /*
         * The lineage snapshot intentionally omits Tape's reflected/evidence bit.
         * The terminal DTO carries that bit from the live Tape view so its authority
         * cannot be reduced to event id + payload alone.
         */
        document.tapeSHA256 = finalTape.Digest;
        document.tapeAuthoritySHA256 = ComputeTapeAuthoritySHA256(document.tapeEvents);
        document.journalAuthoritySHA256 = ComputeJournalAuthoritySHA256(finalJournal);
        document.worldAuthoritySHA256 = ComputeWorldAuthoritySHA256(document.worldFiles);
        document.frontierAuthoritySHA256 = ComputeFrontierAuthoritySHA256(document);
        document.theoryAuthoritySHA256 = ComputePatternAuthoritySHA256(document);
        string computedImmutable = ComputeImmutableAuthoritySHA256(document);
        if (immutableAuthoritySHA256 is not null
            && !string.Equals(computedImmutable, immutableAuthoritySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("native repository immutable authority diverges from terminal DTO");
        document.immutableAuthoritySHA256 = computedImmutable;
        document.sealedEvidenceAuthoritySHA256 = ComputeSealedEvidenceAuthoritySHA256(document);
        document.journalLines.AddRange(finalJournal.Lines);
        document.journalRows.AddRange(finalJournal.Rows.Select(static row => new RepositoryNativeJournalRowRON
        {
            lineIndex = row.LineIndex, step = row.Step, eventID = row.EventID.Value,
            source = row.Source, sha256 = row.SHA256,
        }));
        return document;
    }

    internal static string ComputeImmutableAuthoritySHA256(
        RepositoryNativeRuntimeSnapshot runtime,
        Tape tape,
        LoopLineageTapeSnapshot preSealTape,
        JournalSnapshot preSealJournal,
        RepositoryNativeRegisteredAuthorityRON? registeredAuthority = null)
    {
        RepositoryNativeTerminalEvidenceRON document = Capture(
            null, runtime, tape, preSealTape, preSealJournal, null,
            default, preSealTape, preSealJournal, registeredAuthority);
        return document.immutableAuthoritySHA256;
    }

    private static string ComputeImmutableAuthoritySHA256(RepositoryNativeTerminalEvidenceRON document)
        => DigestFields("repository-native-immutable-authority-v4", document.sourceAuthoritySHA256,
            document.worldAuthoritySHA256, document.accessAuthoritySHA256, document.accessSnapshotSHA256, document.frontierAuthoritySHA256,
            document.theoryAuthoritySHA256, document.frontierRuntimeAuthoritySHA256, document.frontierSnapshotSHA256,
            document.theoryCommittedAuthoritySHA256, document.theorySnapshotSHA256, document.theoryPendingAuthoritySHA256,
            document.registeredAuthority?.authoritySHA256 ?? "",
            document.accessKeyframeBase64, document.frontierKeyframeBase64, document.theoryKeyframeBase64,
            document.preSealTapeSHA256, document.preSealJournalSHA256);

    private static string ComputeSealedEvidenceAuthoritySHA256(RepositoryNativeTerminalEvidenceRON document)
        => DigestFields("repository-native-sealed-evidence-v4", document.sourceAuthoritySHA256,
            document.worldAuthoritySHA256, document.accessAuthoritySHA256, document.accessSnapshotSHA256, document.frontierAuthoritySHA256,
            document.theoryAuthoritySHA256, document.frontierRuntimeAuthoritySHA256, document.frontierSnapshotSHA256,
            document.theoryCommittedAuthoritySHA256, document.theorySnapshotSHA256, document.theoryPendingAuthoritySHA256,
            document.immutableAuthoritySHA256, document.tapeAuthoritySHA256,
            document.journalAuthoritySHA256, document.sealEventID.ToString(), document.sealPayloadSHA256,
            document.sealReceiptSHA256);

    private static string DigestFields(string tag, params string[] fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, tag);
        foreach (string field in fields) AppendField(hash, field ?? "");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }

    private static string EncodeState(Action<CkptWriter> save)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) save(writer);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static T DecodeState<T>(string encoded, Func<CkptReader, T> load)
    {
        if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidDataException("native repository terminal state keyframe is missing");
        using MemoryStream stream = new(Convert.FromBase64String(encoded), writable: false);
        using CkptReader reader = new(stream);
        T value = load(reader);
        if (stream.Position != stream.Length) throw new InvalidDataException("native repository terminal state keyframe has trailing bytes");
        return value;
    }

    private static string ComputeWorldAuthoritySHA256(IEnumerable<RepositoryNativeWorldFileRON> files)
        => DigestRows("repository-native-world-authority-v2", files.OrderBy(static file => file.path, StringComparer.Ordinal)
            .Select(static file => string.Join('\t', file.path, file.bytes, file.sha256, file.contentBase64)));

    private static string ComputeAccessAuthoritySHA256(IEnumerable<RepositoryNativeAccessEntryRON> entries)
        => DigestRows("repository-native-access-authority-v2", entries.OrderBy(static entry => entry.sequence)
            .Select(static entry => string.Join('\t', entry.step, entry.sequence, entry.callSHA256, entry.verb,
                entry.argument, string.Join(',', entry.paths), string.Join(',', entry.loci), entry.renderedBase64, entry.entrySHA256)));

    private static List<RepositoryNativeAccessEntryRON> CaptureAccessRows(IEnumerable<RepositoryAccessEntry> entries)
        => entries.Select(static entry => new RepositoryNativeAccessEntryRON
        {
            step = entry.Step, sequence = entry.Sequence, callSHA256 = entry.CallSHA256, verb = (byte)entry.Verb,
            argument = entry.Argument, paths = entry.Paths.Select(static path => path.Value).ToArray(),
            loci = entry.Loci.Select(static locus => $"{locus.Path.Value}:{locus.Line}").ToArray(),
            renderedBase64 = Convert.ToBase64String(entry.RenderedBytes), entrySHA256 = entry.EntrySHA256,
        }).ToList();

    private static List<RepositoryNativeCandidateRON> CaptureCandidateRows(IEnumerable<RepositoryCandidate> candidates)
        => candidates.Select(static candidate => new RepositoryNativeCandidateRON
        {
            species = (byte)candidate.Species, digest = candidate.Digest.Value, canonical = candidate.Canonical,
        }).ToList();

    private static List<RepositoryNativeTransitionRON> CaptureTransitionRows(IEnumerable<RepositoryCandidateTransition> transitions)
        => transitions.Select(static transition => new RepositoryNativeTransitionRON
        {
            digest = transition.CandidateDigest.Value, canonical = transition.CandidateCanonical,
            state = (byte)transition.State, attempts = transition.Attempts,
            sourceEventID = transition.SourceEventID.Value, predecessorEventID = transition.PredecessorEventID.Value,
            callSHA256 = transition.CallSHA256, accessSHA256 = transition.AccessSHA256,
            verificationOutcome = transition.VerifierOutcome is { } outcome ? (byte)outcome : byte.MaxValue,
            theoryOrigin = transition.PatternOrigin?.Receipt.ReceiptSHA256 ?? "",
            theoryOriginRuleID = transition.PatternOrigin?.RuleID.Value ?? "",
            theoryOriginOccurrenceSetSHA256 = transition.PatternOrigin?.OccurrenceSet.OccurrenceSetSHA256 ?? "",
            theoryOriginReceiptCanonical = transition.PatternOrigin?.Receipt.Canonical ?? "",
        }).ToList();

    private static List<RepositoryNativePatternOccurrenceRON> CapturePatternOccurrenceRows(IEnumerable<RepositoryPatternOccurrence> occurrences)
        => occurrences.Select(static occurrence => new RepositoryNativePatternOccurrenceRON
        {
            claimID = occurrence.PredictionID.Value, claim = occurrence.Prediction.Canonical,
            claimSpecies = (byte)occurrence.Prediction.Species, claimPath = occurrence.Prediction.Path, claimLine = occurrence.Prediction.Line,
            claimValue = occurrence.Prediction.Value, claimOtherPath = occurrence.Prediction.OtherPath,
            verificationCanonical = occurrence.OccurrenceCheck.Canonical,
            verificationReceiptSHA256 = occurrence.OccurrenceCheck.ReceiptSHA256,
            sourceEventID = occurrence.SourceEventID.Value, verificationEventID = occurrence.OccurrenceCheckReceiptEventID.Value,
        }).ToList();

    private static List<RepositoryNativePatternCompositionRON> CapturePatternCompositionRows(IEnumerable<RepositoryPatternComposition> compositions)
        => compositions.Select(static composition => new RepositoryNativePatternCompositionRON
        {
            candidateDigest = composition.Conclusion.CandidateDigest.Value,
            candidateCanonical = composition.Conclusion.Candidate.Canonical,
            receiptSHA256 = composition.Receipt.ReceiptSHA256,
            ruleID = composition.Conclusion.RuleID.Value,
            supportReceiptEventIDs = composition.Conclusion.OccurrenceSet.Occurrences.Select(static occurrence => occurrence.OccurrenceCheckReceiptEventID.Value).ToArray(),
            receiptCanonical = composition.Receipt.Canonical,
            supportSetSHA256 = composition.Conclusion.OccurrenceSet.OccurrenceSetSHA256,
        }).ToList();

    private static List<RepositoryNativePatternAdmissionRON> CapturePatternAdmissionRows(IEnumerable<RepositoryPatternGrammarAdmissionReceipt> admissions)
        => admissions.Select(static admission => new RepositoryNativePatternAdmissionRON
        {
            candidateDigest = admission.CandidateDigest.Value, candidateCanonical = admission.CandidateCanonical,
            ruleID = admission.RuleID.Value, supportSetSHA256 = admission.OccurrenceSetSHA256,
            derivationReceiptSHA256 = admission.CompositionReceiptSHA256, worldSHA256 = admission.WorldSHA256,
            accessSHA256 = admission.AccessSHA256, parentRevision = admission.ParentRevision.Value,
            wScale = admission.WScale, pricingBasisDigest = admission.PricingBasisDigest,
            baselineRuleCount = admission.BaselineRuleCount, baselineCompressedLength = admission.BaselineCompressedLength,
            rawSymbolLength = admission.RawSymbolLength, rawWeightLength = admission.RawWeightLength,
            literalCostMbits = admission.Price.LiteralCostMbits, materializedCostMbits = admission.Price.MaterializedCostMbits,
            marginalSavingsMbits = admission.Price.MarginalSavingsMbits, decision = (byte)admission.Decision,
            economicsEventID = admission.EconomicsEventID?.Value ?? -1, economicsPayloadSHA256 = admission.EconomicsPayloadSHA256,
            economicsJournal = EncodeJournalBinding(admission.EconomicsJournalBinding),
            reflectedTapeEventID = admission.ReflectedTapeEventID?.Value ?? -1,
            reflectionJournal = EncodeJournalBinding(admission.ReflectionJournalBinding),
            consumedRevision = admission.ConsumedRevision?.Value ?? 0, lineageNodeID = admission.LineageNodeID?.Value ?? "",
            digest = admission.Digest, identityKey = admission.IdentityKey,
        }).ToList();

    private static List<RepositoryNativePendingAdmissionRON> CapturePendingAdmissionRows(IEnumerable<RepositoryPatternPendingAdmission> pendingAdmissions)
        => pendingAdmissions.Select(static pending => new RepositoryNativePendingAdmissionRON
        {
            digest = pending.Digest.Value, canonical = pending.Canonical,
        }).ToList();

    private static bool AccessRowsEqual(RepositoryNativeAccessEntryRON left, RepositoryNativeAccessEntryRON right)
        => left.step == right.step && left.sequence == right.sequence && left.callSHA256 == right.callSHA256
            && left.verb == right.verb && left.argument == right.argument && left.paths is not null && right.paths is not null
            && left.loci is not null && right.loci is not null && left.paths.SequenceEqual(right.paths)
            && left.loci.SequenceEqual(right.loci) && left.renderedBase64 == right.renderedBase64
            && left.entrySHA256 == right.entrySHA256;

    private static bool CandidateRowsEqual(RepositoryNativeCandidateRON left, RepositoryNativeCandidateRON right)
        => left.species == right.species && left.digest == right.digest && left.canonical == right.canonical;

    private static bool TransitionRowsEqual(RepositoryNativeTransitionRON left, RepositoryNativeTransitionRON right)
        => left.digest == right.digest && left.canonical == right.canonical && left.state == right.state
            && left.attempts == right.attempts && left.sourceEventID == right.sourceEventID
            && left.predecessorEventID == right.predecessorEventID && left.callSHA256 == right.callSHA256
            && left.accessSHA256 == right.accessSHA256 && left.verificationOutcome == right.verificationOutcome
            && left.theoryOrigin == right.theoryOrigin && left.theoryOriginRuleID == right.theoryOriginRuleID
            && left.theoryOriginOccurrenceSetSHA256 == right.theoryOriginOccurrenceSetSHA256
            && left.theoryOriginReceiptCanonical == right.theoryOriginReceiptCanonical;

    private static bool SelectionRowsEqual(RepositoryNativeSelectionRON left, RepositoryNativeSelectionRON right)
        => left.revision == right.revision && left.runtimeAuthoritySHA256 == right.runtimeAuthoritySHA256
            && left.ordinal == right.ordinal && left.selectionEventID == right.selectionEventID
            && left.selectionReceiptSHA256 == right.selectionReceiptSHA256 && left.candidateDigest == right.candidateDigest
            && left.candidateCanonical == right.candidateCanonical;

    private static bool PatternOccurrenceRowsEqual(RepositoryNativePatternOccurrenceRON left, RepositoryNativePatternOccurrenceRON right)
        => left.claimID == right.claimID && left.claim == right.claim && left.claimSpecies == right.claimSpecies
            && left.claimPath == right.claimPath && left.claimLine == right.claimLine && left.claimValue == right.claimValue
            && left.claimOtherPath == right.claimOtherPath && left.verificationCanonical == right.verificationCanonical
            && left.verificationReceiptSHA256 == right.verificationReceiptSHA256 && left.sourceEventID == right.sourceEventID
            && left.verificationEventID == right.verificationEventID;

    private static bool PatternCompositionRowsEqual(RepositoryNativePatternCompositionRON left, RepositoryNativePatternCompositionRON right)
        => left.ruleID == right.ruleID && left.supportSetSHA256 == right.supportSetSHA256
            && left.candidateDigest == right.candidateDigest && left.candidateCanonical == right.candidateCanonical
            && left.supportReceiptEventIDs is not null && right.supportReceiptEventIDs is not null
            && left.supportReceiptEventIDs.SequenceEqual(right.supportReceiptEventIDs)
            && left.receiptCanonical == right.receiptCanonical && left.receiptSHA256 == right.receiptSHA256;

    private static bool PatternAdmissionRowsEqual(RepositoryNativePatternAdmissionRON left, RepositoryNativePatternAdmissionRON right)
        => left.ruleID == right.ruleID && left.supportSetSHA256 == right.supportSetSHA256
            && left.candidateDigest == right.candidateDigest && left.candidateCanonical == right.candidateCanonical
            && left.derivationReceiptSHA256 == right.derivationReceiptSHA256 && left.worldSHA256 == right.worldSHA256
            && left.accessSHA256 == right.accessSHA256 && left.parentRevision == right.parentRevision
            && left.wScale == right.wScale && left.pricingBasisDigest == right.pricingBasisDigest
            && left.baselineRuleCount == right.baselineRuleCount && left.baselineCompressedLength == right.baselineCompressedLength
            && left.rawSymbolLength == right.rawSymbolLength && left.rawWeightLength == right.rawWeightLength
            && left.literalCostMbits == right.literalCostMbits && left.materializedCostMbits == right.materializedCostMbits
            && left.marginalSavingsMbits == right.marginalSavingsMbits && left.decision == right.decision
            && left.economicsEventID == right.economicsEventID && left.economicsPayloadSHA256 == right.economicsPayloadSHA256
            && left.economicsJournal == right.economicsJournal && left.reflectedTapeEventID == right.reflectedTapeEventID
            && left.reflectionJournal == right.reflectionJournal && left.consumedRevision == right.consumedRevision
            && left.lineageNodeID == right.lineageNodeID && left.digest == right.digest && left.identityKey == right.identityKey;

    private static bool PendingRowsEqual(RepositoryNativePendingAdmissionRON left, RepositoryNativePendingAdmissionRON right)
        => left.digest == right.digest && left.canonical == right.canonical;

    private static bool RowsEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, Func<T, T, bool> equal)
        => expected.Count == actual.Count && expected.Zip(actual).All(pair => equal(pair.First, pair.Second));

    private static string ComputeFrontierAuthoritySHA256(RepositoryNativeTerminalEvidenceRON document)
    {
        List<string> rows = [$"revision\t{document.frontierRevision}"];
        rows.AddRange(document.frontierCandidates.OrderBy(static candidate => candidate.digest).Select(static candidate =>
            string.Join('\t', "candidate", candidate.species, candidate.digest, candidate.canonical)));
        rows.AddRange(document.frontierTransitions.OrderBy(static transition => transition.digest).Select(static transition =>
            string.Join('\t', "transition", transition.digest, transition.canonical, transition.state, transition.attempts,
                transition.sourceEventID, transition.predecessorEventID, transition.callSHA256, transition.accessSHA256,
                transition.verificationOutcome, transition.theoryOrigin, transition.theoryOriginRuleID,
                transition.theoryOriginOccurrenceSetSHA256, transition.theoryOriginReceiptCanonical)));
        rows.AddRange(document.frontierSelections.OrderBy(static selection => selection.ordinal).Select(static selection =>
            string.Join('\t', "selection", selection.revision, selection.runtimeAuthoritySHA256, selection.ordinal,
                selection.selectionEventID, selection.selectionReceiptSHA256, selection.candidateDigest, selection.candidateCanonical)));
        rows.AddRange(document.observedPaths.Order(StringComparer.Ordinal).Select(static path => "observed\t" + path));
        return DigestRows("repository-native-frontier-authority-v3", rows);
    }

    private static string ComputePatternAuthoritySHA256(RepositoryNativeTerminalEvidenceRON document)
    {
        List<string> rows =
        [
            $"rule\t{document.theoryRule.id}\t{document.theoryRule.canonical}\t{document.theoryRule.derivedSpecies}\t{document.theoryRule.derivedAdmissionPath}\t{document.theoryRule.alternativeAdmissionPath}",
        ];
        rows.AddRange(document.theoryOccurrences.OrderBy(static occurrence => occurrence.verificationEventID).Select(static occurrence =>
            // Frozen digest row kind and RON field names; identifier-side name is Occurrence.
            string.Join('\t', "support", occurrence.claimID, occurrence.claim, occurrence.claimSpecies, occurrence.claimPath,
                occurrence.claimLine, occurrence.claimValue, occurrence.claimOtherPath, occurrence.verificationCanonical,
                occurrence.verificationReceiptSHA256, occurrence.sourceEventID, occurrence.verificationEventID)));
        rows.AddRange(document.theoryCompositions.OrderBy(static composition => composition.candidateDigest).Select(static composition =>
            // Frozen digest row kind and RON field names; identifier-side name is Composition.
            string.Join('\t', "derivation", composition.ruleID, composition.supportSetSHA256, composition.candidateDigest,
                composition.candidateCanonical, string.Join(',', composition.supportReceiptEventIDs), composition.receiptCanonical,
                composition.receiptSHA256)));
        rows.AddRange(document.theoryAdmissions.OrderBy(static admission => admission.candidateDigest).Select(static admission =>
            // Frozen digest row kind and RON field names; identifier-side name is Admission.
            string.Join('\t', "promotion", admission.ruleID, admission.supportSetSHA256, admission.candidateDigest,
                admission.candidateCanonical, admission.derivationReceiptSHA256, admission.worldSHA256, admission.accessSHA256,
                admission.parentRevision, admission.wScale, admission.pricingBasisDigest, admission.baselineRuleCount,
                admission.baselineCompressedLength, admission.rawSymbolLength, admission.rawWeightLength,
                admission.literalCostMbits, admission.materializedCostMbits, admission.marginalSavingsMbits, admission.decision,
                admission.economicsEventID, admission.economicsPayloadSHA256, admission.economicsJournal,
                admission.reflectedTapeEventID, admission.reflectionJournal, admission.consumedRevision, admission.lineageNodeID,
                admission.digest, admission.identityKey)));
        rows.AddRange(document.pendingAdmissions.OrderBy(static pending => pending.digest).Select(static pending =>
            $"pending\t{pending.digest}\t{pending.canonical}"));
        rows.Add($"pending-authority\t{document.theoryPendingAuthoritySHA256}");
        // Frozen authority digest prefix; identifier-side name is PatternAuthority.
        return DigestRows("repository-native-theory-authority-v2", rows);
    }

    private static string ComputeTapeAuthoritySHA256(IEnumerable<RepositoryNativeTapeEventRON> events)
        => DigestRows("repository-native-tape-authority-v2", events.OrderBy(static item => item.eventID).Select(static item =>
            string.Join('\t', item.eventID, item.source, item.provenance, item.roles, item.evidence, item.payloadBase64)));

    private static string ComputeJournalAuthoritySHA256(JournalSnapshot journal)
        => DigestRows("repository-native-journal-authority-v2", journal.Lines.Select((line, index) =>
            $"line\t{index}\t{line}").Concat(journal.Rows.Select(static row =>
            string.Join('\t', "row", row.LineIndex, row.Step, row.EventID.Value, row.Source, row.SHA256))));

    private static string ComputeJournalAuthoritySHA256(RepositoryNativeTerminalEvidenceRON document)
        => DigestRows("repository-native-journal-authority-v2", document.journalLines.Select((line, index) =>
            $"line\t{index}\t{line}").Concat(document.journalRows.Select(static row =>
            string.Join('\t', "row", row.lineIndex, row.step, row.eventID, row.source, row.sha256))));

    private static string DigestRows(string tag, IEnumerable<string> rows)
        => DigestFields(tag, rows.ToArray());

    private static string EncodeJournalBinding(JournalRowBinding? binding)
        => binding is JournalRowBinding value
            ? string.Join('\t', value.LineIndex, value.Step, value.EventID.Value, value.Source, value.SHA256)
            : "";

    private static List<RepositoryNativeTapeEventRON> CaptureTapeEvents(Tape tape)
    {
        List<RepositoryNativeTapeEventRON> events = new();
        foreach (TapeEventView view in tape.GetEventViews().OrderBy(static view => view.Id.Value))
        {
            if (!tape.Resolve(view.Id, out byte[] payload))
                throw new InvalidDataException($"native repository terminal tape event {view.Id} cannot be resolved");
            events.Add(new RepositoryNativeTapeEventRON
            {
                eventID = view.Id.Value, source = view.Source, provenance = (byte)view.Provenance,
                roles = (byte)view.Roles, evidence = view.Evidence,
                payloadBase64 = Convert.ToBase64String(payload),
            });
        }
        return events;
    }

    private static List<RepositoryLoopClosureFrontierSelectionCorroboration> CaptureFrontierSelections(Tape tape)
    {
        List<RepositoryLoopClosureFrontierSelectionCorroboration> selections = new();
        foreach (TapeEventView view in tape.GetEventViews()
            .Where(static view => view.Source == "repository-selection")
            .OrderBy(static view => view.Id.Value))
        {
            // Per-clause, because "not reconstructible" over a fused conjunction names nothing the
            // reader can act on — provenance, roles, custody and codec are four different failures.
            if (view.Provenance != Provenances.Execution || view.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly))
                throw new InvalidDataException($"native repository selection s{view.Id.Value} carries provenance={view.Provenance} roles={view.Roles}");
            if (!tape.Resolve(view.Id, out byte[] payload))
                throw new InvalidDataException($"native repository selection s{view.Id.Value} has no resolvable custody bytes");
            if (!RepositorySelectionReceipt.TryDecode(payload, out RepositorySelectionReceipt receipt))
                throw new InvalidDataException($"native repository selection s{view.Id.Value} does not decode: '{Encoding.UTF8.GetString(payload)}'");
            selections.Add(new RepositoryLoopClosureFrontierSelectionCorroboration(
                receipt.FrontierRevision, receipt.FrontierAuthoritySHA256, receipt.SelectionOrdinal,
                view.Id, receipt.ReceiptSHA256, receipt.CandidateDigest, receipt.CandidateCanonical));
        }
        return selections;
    }

    private static List<RepositoryLoopClosureFrontierSelectionCorroboration> CaptureFrontierSelections(LoopLineageTapeSnapshot tape)
    {
        List<RepositoryLoopClosureFrontierSelectionCorroboration> selections = new();
        foreach (LoopLineageTapeEvent item in tape.Events.Where(static item => item.Source == "repository-selection"))
        {
            if (item.Provenance != Provenances.Execution
                || item.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !RepositorySelectionReceipt.TryDecode(item.Payload.Span, out RepositorySelectionReceipt receipt))
                throw new InvalidDataException("native repository selection tape packet is not reconstructible");
            selections.Add(new RepositoryLoopClosureFrontierSelectionCorroboration(
                receipt.FrontierRevision, receipt.FrontierAuthoritySHA256, receipt.SelectionOrdinal,
                item.EventID, receipt.ReceiptSHA256, receipt.CandidateDigest, receipt.CandidateCanonical));
        }
        return selections;
    }

    internal static void Write(Run run, RepositoryNativeTerminalEvidenceRON document)
    {
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second))
            throw new InvalidDataException("native repository terminal evidence encoding is nondeterministic");
        string path = run.PathOf(FileName);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(first))
                throw new InvalidDataException("native repository terminal evidence already exists with different bytes");
            return;
        }
        run.WriteAtomic(FileName, stream => stream.Write(first));
    }

    internal static RepositoryNativeTerminalEvidenceRON ValidateAndDecode(
        Run run, Tape tape, Journal journal)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        byte[] bytes = File.ReadAllBytes(run.PathOf(FileName));
        RepositoryNativeTerminalDecoded decoded = Decode(bytes);
        ValidateLiveDocument(decoded, run, tape, journal);
        return decoded.Document;
    }

    /// Decode the sealed terminal document without opening the run, repository, tape,
    /// or journal. Every snapshot and lineage packet is reconstructed from the bytes
    /// retained in the document and validated before the decoded view is returned.
    internal static RepositoryNativeTerminalDecoded Decode(
        ReadOnlySpan<byte> bytes,
        RepositoryNativeRegisteredAuthorityRON? expectedRegisteredAuthority = null)
    {
        if (bytes.IsEmpty) throw new InvalidDataException("native repository terminal evidence bytes are empty");
        RepositoryNativeTerminalEvidenceRON document = RonSerializer.Deserialize<RepositoryNativeTerminalEvidenceRON>(bytes);
        byte[] canonical = RonSerializer.SerializeToUtf8(in document);
        if (!canonical.AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("native repository terminal evidence RON is not canonical");
        return DecodeDocument(document, expectedRegisteredAuthority);
    }

    private static RepositoryNativeTerminalDecoded DecodeDocument(
        RepositoryNativeTerminalEvidenceRON document,
        RepositoryNativeRegisteredAuthorityRON? expectedRegisteredAuthority)
    {
        ValidateDocumentShape(document, expectedRegisteredAuthority);

        RepositoryLoopClosureWorldSnapshot world = ReconstructWorld(document);
        RepositoryAccessJournal accessOwner = DecodeState(document.accessKeyframeBase64, RepositoryAccessJournal.ReadState);
        if (accessOwner.AccessSHA256 != document.accessSHA256
            || RepositoryAccessJournal.ComputeAccessSHA256(accessOwner.Entries) != document.accessSHA256)
            throw new InvalidDataException("native repository terminal access keyframe authority diverges");
        RepositoryAccessEntry[] accessEntries = accessOwner.Entries.ToArray();
        List<RepositoryNativeAccessEntryRON> expectedAccessRows = CaptureAccessRows(accessEntries);
        if (!RowsEqual(expectedAccessRows, document.accessEntries, AccessRowsEqual))
            throw new InvalidDataException("native repository terminal access rows diverge from keyframe");
        RepositoryLoopClosureAccessSnapshot access = new(accessEntries, world);
        access.Validate();
        if (access.AccessSHA256 != document.accessSnapshotSHA256)
            throw new InvalidDataException("native repository terminal access snapshot authority diverges");

        RepositoryCandidateFrontier frontierOwner = DecodeState(document.frontierKeyframeBase64, reader =>
        {
            RepositoryCandidateFrontier owner = new();
            owner.LoadState(reader);
            return owner;
        });
        RepositoryCandidateFrontierSnapshot frontierState = frontierOwner.CaptureSnapshot();
        frontierState.Validate();
        if (frontierState.Revision.Value != document.frontierRevision
            || frontierState.AuthoritySHA256 != document.frontierRuntimeAuthoritySHA256)
            throw new InvalidDataException("native repository terminal frontier keyframe authority diverges");
        if (!RowsEqual(CaptureCandidateRows(frontierState.Candidates), document.frontierCandidates, CandidateRowsEqual)
            || !RowsEqual(CaptureTransitionRows(frontierState.Transitions), document.frontierTransitions, TransitionRowsEqual)
            || !frontierState.ObservedPaths.SequenceEqual(document.observedPaths, StringComparer.Ordinal))
            throw new InvalidDataException("native repository terminal frontier rows diverge from keyframe");

        LoopLineageTapeSnapshot tapeSnapshot = ReconstructTape(document);
        IReadOnlyList<LoopLineageEdgeReceipt> lineageEdges = LoopLineageVerifier.ReadTapeEdges(tapeSnapshot);
        List<RepositoryLoopClosureFrontierSelectionCorroboration> frontierSelections = CaptureFrontierSelections(tapeSnapshot);
        List<RepositoryNativeSelectionRON> expectedSelectionRows = frontierSelections.Select(static selection => new RepositoryNativeSelectionRON
        {
            revision = selection.Revision.Value, runtimeAuthoritySHA256 = selection.RuntimeAuthoritySHA256,
            ordinal = selection.Ordinal, selectionEventID = selection.SelectionEventID.Value,
            selectionReceiptSHA256 = selection.SelectionReceiptSHA256,
            candidateDigest = selection.CandidateDigest.Value, candidateCanonical = selection.CandidateCanonical,
        }).ToList();
        if (!RowsEqual(expectedSelectionRows, document.frontierSelections, SelectionRowsEqual))
            throw new InvalidDataException("native repository terminal frontier selection rows diverge from tape");
        RepositoryLoopClosureFrontierSnapshot frontier = new(
            frontierState.Revision, frontierState.Candidates, frontierState.Transitions,
            frontierState.ObservedPaths, frontierState.AuthoritySHA256, frontierSelections);
        frontier.Validate();
        if (frontier.RuntimeAuthoritySHA256 != document.frontierRuntimeAuthoritySHA256
            || frontier.FrontierSHA256 != document.frontierSnapshotSHA256)
            throw new InvalidDataException("native repository terminal frontier snapshot authority diverges");

        RepositoryNavigationRule rule = RepositoryNavigationRule.CreateSharedIdentifierSearchTerm();
        RepositoryPatternStore patternOwner = DecodeState(document.theoryKeyframeBase64, reader =>
        {
            RepositoryPatternStore owner = new(rule);
            owner.LoadState(reader);
            return owner;
        });
        RepositoryPatternStoreSnapshot patternState = patternOwner.CaptureSnapshot();
        patternState.Validate();
        if (patternState.CommittedAuthoritySHA256 != document.theoryCommittedAuthoritySHA256
            || patternState.PendingAuthoritySHA256 != document.theoryPendingAuthoritySHA256
            || patternState.Rule.ID.Value != document.theoryRule.id
            || patternState.Rule.Canonical != document.theoryRule.canonical
            || (byte)patternState.Rule.ComposedSpecies != document.theoryRule.derivedSpecies
            || patternState.Rule.ComposedAdmissionPath != document.theoryRule.derivedAdmissionPath
            || patternState.Rule.AlternativeAdmissionPath != document.theoryRule.alternativeAdmissionPath)
            throw new InvalidDataException("native repository terminal pattern keyframe authority diverges");
        if (!RowsEqual(CapturePatternOccurrenceRows(patternState.Occurrences), document.theoryOccurrences, PatternOccurrenceRowsEqual)
            || !RowsEqual(CapturePatternCompositionRows(patternState.Compositions), document.theoryCompositions, PatternCompositionRowsEqual)
            || !RowsEqual(CapturePatternAdmissionRows(patternState.Admissions), document.theoryAdmissions, PatternAdmissionRowsEqual)
            || !RowsEqual(CapturePendingAdmissionRows(patternState.PendingAdmissions), document.pendingAdmissions, PendingRowsEqual))
            throw new InvalidDataException("native repository terminal pattern rows diverge from keyframe");
        RepositoryLoopClosurePatternSnapshot pattern = new(
            patternState.Rule, patternState.Occurrences, patternState.Compositions, patternState.Admissions,
            patternState.PendingAdmissionDigests.ToArray(), patternState.PendingAuthoritySHA256,
            patternState.PendingAdmissions.Select(static pending => pending.Canonical).ToArray());
        pattern.Validate();
        if (pattern.PendingAuthoritySHA256 != document.theoryPendingAuthoritySHA256
            || pattern.PatternSHA256 != document.theorySnapshotSHA256)
            throw new InvalidDataException("native repository terminal pattern snapshot authority diverges");

        RepositoryLoopClosureJournalSnapshot journal = ReconstructJournal(document);
        journal.Validate();
        if (journal.JournalSHA256 != document.journalSHA256)
            throw new InvalidDataException("native repository terminal journal snapshot authority diverges");
        if (ComputeWorldAuthoritySHA256(document.worldFiles) != document.worldAuthoritySHA256
            || ComputeAccessAuthoritySHA256(document.accessEntries) != document.accessAuthoritySHA256
            || RepositoryAccessJournal.ComputeAccessSHA256(accessEntries) != document.accessSHA256
            || ComputeFrontierAuthoritySHA256(document) != document.frontierAuthoritySHA256
            || ComputePatternAuthoritySHA256(document) != document.theoryAuthoritySHA256)
            throw new InvalidDataException("native repository terminal component authority diverges");

        RepositoryLoopClosureTapeSeal seal = ReconstructSeal(document, tapeSnapshot);
        RepositoryLoopClosureTapeSnapshot tape = new(tapeSnapshot, lineageEdges, seal);
        tape.Validate();
        if (tape.PreSealTapeSHA256 != document.preSealTapeSHA256 || tape.TapeSHA256 != document.tapeSHA256)
            throw new InvalidDataException("native repository terminal tape digest diverges");
        JournalRowBinding sealRow = journal.Rows.SingleOrDefault(row => row.EventID == seal.EventID);
        if (sealRow.EventID != seal.EventID || sealRow.Source != "repository-seal")
            throw new InvalidDataException("native repository terminal journal has no seal binding");
        RepositoryLoopClosureJournalSnapshot preSealJournal = new(
            journal.Lines.Take(sealRow.LineIndex).ToArray(),
            journal.Rows.Where(row => row.LineIndex < sealRow.LineIndex).ToArray());
        preSealJournal.Validate();
        if (preSealJournal.JournalSHA256 != document.preSealJournalSHA256)
            throw new InvalidDataException("native repository terminal pre-seal journal authority diverges");
        if (ComputeImmutableAuthoritySHA256(document) != document.immutableAuthoritySHA256
            || ComputeSealedEvidenceAuthoritySHA256(document) != document.sealedEvidenceAuthoritySHA256)
            throw new InvalidDataException("native repository terminal sealed authority diverges");

        RepositoryNativeTerminalRuntimeSnapshot runtime = new(
            document.root, document.glob, document.query, document.querySHA256, document.sourceAuthoritySHA256,
            world, access, frontier, pattern);
        return new RepositoryNativeTerminalDecoded(document, runtime, world, tape, journal, access, frontier, pattern,
            preSealJournal, lineageEdges, seal);
    }

    private static void ValidateDocumentShape(
        RepositoryNativeTerminalEvidenceRON document,
        RepositoryNativeRegisteredAuthorityRON? expectedRegisteredAuthority)
    {
        if (document.schemaVersion != 4 || string.IsNullOrWhiteSpace(document.runID))
            throw new InvalidDataException("native repository terminal evidence schema or run identity is malformed");
        RequireSHA(document.sourceAuthoritySHA256, "source authority"); RequireSHA(document.worldSHA256, "world");
        RequireSHA(document.worldSnapshotSHA256, "world snapshot"); RequireSHA(document.accessSHA256, "access");
        RequireSHA(document.accessSnapshotSHA256, "access snapshot"); RequireSHA(document.worldAuthoritySHA256, "world component authority");
        RequireSHA(document.accessAuthoritySHA256, "access component authority"); RequireSHA(document.frontierAuthoritySHA256, "frontier component authority");
        RequireSHA(document.theoryAuthoritySHA256, "pattern component authority"); RequireSHA(document.tapeAuthoritySHA256, "tape component authority");
        RequireSHA(document.journalAuthoritySHA256, "journal component authority"); RequireSHA(document.frontierRuntimeAuthoritySHA256, "frontier runtime authority");
        RequireSHA(document.frontierSnapshotSHA256, "frontier snapshot"); RequireSHA(document.theoryCommittedAuthoritySHA256, "pattern committed authority");
        RequireSHA(document.theorySnapshotSHA256, "pattern snapshot"); RequireSHA(document.theoryPendingAuthoritySHA256, "pattern pending");
        RequireSHA(document.preSealTapeSHA256, "pre-seal tape"); RequireSHA(document.preSealJournalSHA256, "pre-seal journal");
        RequireSHA(document.immutableAuthoritySHA256, "immutable authority"); RequireSHA(document.tapeSHA256, "tape");
        RequireSHA(document.journalSHA256, "journal"); RequireSHA(document.sealedEvidenceAuthoritySHA256, "sealed authority");
        RequireSHA(document.sealPayloadSHA256, "seal payload"); RequireSHA(document.sealReceiptSHA256, "seal receipt");
        if (document.worldFiles is null || document.accessEntries is null || document.frontierCandidates is null
            || document.frontierTransitions is null || document.frontierSelections is null || document.observedPaths is null
            || document.theoryOccurrences is null || document.theoryCompositions is null || document.theoryAdmissions is null
            || document.pendingAdmissions is null || document.tapeEvents is null || document.journalLines is null
            || document.journalRows is null || document.sealEventID < 0 || document.worldFiles.Count == 0
            || document.theoryRule is null)
            throw new InvalidDataException("native repository terminal evidence shape is malformed");
        document.registeredAuthority?.Validate();
        if (expectedRegisteredAuthority is not null)
        {
            expectedRegisteredAuthority.Validate();
            if (document.registeredAuthority is null || !RegisteredAuthoritiesEqual(document.registeredAuthority, expectedRegisteredAuthority))
                throw new InvalidDataException("native repository terminal registered authority diverges");
        }
        if (string.IsNullOrWhiteSpace(document.root) || string.IsNullOrWhiteSpace(document.glob)
            || string.IsNullOrWhiteSpace(document.query))
            throw new InvalidDataException("native repository terminal source selection is empty");
        string expectedQuerySHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.query + "\n")));
        if (document.querySHA256 != expectedQuerySHA256)
            throw new InvalidDataException("native repository terminal query authority diverges");
        if (ComputeSourceAuthoritySHA256(document.root, document.glob, document.query, document.querySHA256, document.worldSHA256)
            != document.sourceAuthoritySHA256)
            throw new InvalidDataException("native repository terminal source authority diverges");
    }

    private static void ValidateLiveDocument(
        RepositoryNativeTerminalDecoded decoded, Run run, Tape tape, Journal journal)
    {
        RepositoryNativeTerminalEvidenceRON document = decoded.Document;
        if (document.runID != Run.RunIDFromDirectory(run.Dir))
            throw new InvalidDataException("native repository terminal run identity diverges");
        List<RepositoryNativeTapeEventRON> actualEvents = CaptureTapeEvents(tape);
        if (!RowsEqual(actualEvents, document.tapeEvents, TapeEventsEqual))
            throw new InvalidDataException("native repository terminal tape DTO diverges from mounted tape");
        if (ComputeTapeAuthoritySHA256(document.tapeEvents) != document.tapeAuthoritySHA256)
            throw new InvalidDataException("native repository terminal tape authority diverges");
        JournalSnapshot finalJournal = journal.CaptureSnapshot();
        if (finalJournal.JournalSHA256 != document.journalSHA256
            || ComputeJournalAuthoritySHA256(document) != document.journalAuthoritySHA256)
            throw new InvalidDataException("native repository terminal journal authority diverges");
        if (finalJournal.Rows.Count != decoded.Journal.Rows.Count
            || finalJournal.Rows.Zip(decoded.Journal.Rows).Any(pair => pair.First != pair.Second))
            throw new InvalidDataException("native repository terminal journal DTO diverges from mounted journal");
    }

    private static LoopLineageTapeSnapshot ReconstructTape(RepositoryNativeTerminalEvidenceRON document)
    {
        List<LoopLineageTapeEvent> events = new(document.tapeEvents.Count);
        foreach (RepositoryNativeTapeEventRON row in document.tapeEvents)
        {
            if (row.eventID < 0 || string.IsNullOrWhiteSpace(row.source) || row.payloadBase64 is null
                || !Enum.IsDefined(typeof(Provenances), row.provenance)
                || (row.roles & ~(byte)(TapeEventRoles.GrammarInput | TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)) != 0)
                throw new InvalidDataException("native repository terminal tape row is malformed");
            byte[] payload;
            try { payload = Convert.FromBase64String(row.payloadBase64); }
            catch (FormatException error) { throw new InvalidDataException("native repository terminal tape payload is malformed", error); }
            events.Add(new LoopLineageTapeEvent(new TapeEventID(row.eventID), payload, row.source,
                (Provenances)row.provenance, (TapeEventRoles)row.roles));
        }
        LoopLineageTapeSnapshot snapshot = LoopLineageTapeSnapshot.Create(events);
        if (ComputeTapeAuthoritySHA256(document.tapeEvents) != document.tapeAuthoritySHA256)
            throw new InvalidDataException("native repository terminal tape authority diverges");
        return snapshot;
    }

    private static RepositoryLoopClosureTapeSeal ReconstructSeal(
        RepositoryNativeTerminalEvidenceRON document, LoopLineageTapeSnapshot tape)
    {
        List<LoopLineageTapeEvent> sealEvents = tape.Events.Where(static item => item.Source == "repository-seal").ToList();
        if (sealEvents.Count != 1 || sealEvents[0].EventID.Value != document.sealEventID
            || !TapePacketCreator.TryDecodeRepositoryLoopSeal(sealEvents[0].Payload.Span, out TapeEventID sealEventID,
                out string preSealTapeSHA256, out string immutableAuthoritySHA256)
            || sealEventID != sealEvents[0].EventID
            || preSealTapeSHA256 != document.preSealTapeSHA256
            || immutableAuthoritySHA256 != document.immutableAuthoritySHA256
            || Convert.ToHexStringLower(SHA256.HashData(sealEvents[0].Payload.Span)) != document.sealPayloadSHA256)
            throw new InvalidDataException("native repository terminal seal packet diverges");
        int sealIndex = tape.Events.ToList().FindIndex(item => item.EventID == sealEventID);
        if (sealIndex < 0 || sealIndex != tape.Events.Count - 1)
            throw new InvalidDataException("native repository terminal seal is not the final event");
        LoopLineageTapeSnapshot preSealTape = LoopLineageTapeSnapshot.Create(
            tape.Events.Take(sealIndex).Select(static item => item.Copy()).ToArray());
        RepositoryLoopClosureTapeSeal seal = new(sealEventID, document.sealPayloadSHA256, document.sealReceiptSHA256,
            preSealTape.Digest, "repository-seal", Provenances.Execution, TapeEventRoles.AuditOnly)
        { ImmutableAuthoritySHA256 = immutableAuthoritySHA256 };
        seal.Validate(tape.Events, preSealTape.Digest);
        return seal;
    }

    private static bool RegisteredAuthoritiesEqual(
        RepositoryNativeRegisteredAuthorityRON left, RepositoryNativeRegisteredAuthorityRON right)
        => left.registrationSHA256 == right.registrationSHA256
            && left.registrationDocumentSHA256 == right.registrationDocumentSHA256
            && left.taskAuthoritySHA256 == right.taskAuthoritySHA256
            && left.toolAuthoritySHA256 == right.toolAuthoritySHA256
            && left.policyAuthoritySHA256 == right.policyAuthoritySHA256
            && left.candidateAuthoritySHA256 == right.candidateAuthoritySHA256
            && left.initialStateSHA256 == right.initialStateSHA256
            && left.fuelAuthoritySHA256 == right.fuelAuthoritySHA256
            && left.authoritySHA256 == right.authoritySHA256;

    internal static LoopLineageTapeSnapshot CaptureCanonicalTape(Tape tape)
        => LoopLineageTapeSnapshot.Create(CaptureTapeEvents(tape).Select(static item =>
            new LoopLineageTapeEvent(new TapeEventID(item.eventID), Convert.FromBase64String(item.payloadBase64), item.source,
                (Provenances)item.provenance, (TapeEventRoles)item.roles)).ToArray());

    private static bool TapeEventsEqual(RepositoryNativeTapeEventRON left, RepositoryNativeTapeEventRON right)
        => left.eventID == right.eventID && left.source == right.source && left.provenance == right.provenance
            && left.roles == right.roles && left.evidence == right.evidence && left.payloadBase64 == right.payloadBase64;

    private static void RequireSHA(string value, string name)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"native repository terminal {name} digest is malformed");
    }

    private static string ComputeSourceAuthoritySHA256(string root, string glob, string query, string querySHA256, string worldSHA256)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "repository-native-source-v1", root, glob, query, querySHA256, worldSHA256))));

    private static RepositoryLoopClosureWorldSnapshot ReconstructWorld(RepositoryNativeTerminalEvidenceRON document)
    {
        List<RepositoryLoopClosureWorldFile> files = new();
        foreach (RepositoryNativeWorldFileRON row in document.worldFiles)
        {
            if (row.bytes < 0 || row.path is null || row.sha256 is null || row.contentBase64 is null)
                throw new InvalidDataException("native repository terminal world row is malformed");
            byte[] content;
            try { content = Convert.FromBase64String(row.contentBase64); }
            catch (FormatException error) { throw new InvalidDataException("native repository terminal world bytes are malformed", error); }
            if (content.LongLength != row.bytes)
                throw new InvalidDataException("native repository terminal world byte count diverges");
            if (row.sha256 is not { Length: 64 } || !row.sha256.All(Uri.IsHexDigit)
                || !string.Equals(row.sha256, Convert.ToHexStringLower(SHA256.HashData(content)), StringComparison.Ordinal))
                throw new InvalidDataException("native repository terminal world content authority diverges");
            files.Add(new RepositoryLoopClosureWorldFile(new Tool.RepositoryPath(row.path), content));
        }
        RepositoryLoopClosureWorldSnapshot world = new(files);
        world.Validate();
        if (world.WorldSHA256 != document.worldSHA256 || world.SnapshotSHA256 != document.worldSnapshotSHA256)
            throw new InvalidDataException("native repository terminal world authority diverges");
        return world;
    }

    private static RepositoryLoopClosureJournalSnapshot ReconstructJournal(RepositoryNativeTerminalEvidenceRON document)
    {
        JournalRowBinding[] rows = document.journalRows.Select(static row =>
            new JournalRowBinding(row.lineIndex, row.step, new TapeEventID(row.eventID), row.source, row.sha256)).ToArray();
        JournalSnapshot journal = new(document.journalLines, rows);
        journal.Validate();
        return new RepositoryLoopClosureJournalSnapshot(document.journalLines, rows);
    }

}

/// Immutable terminal reconstruction used by the adjudication assembler. It owns
/// every snapshot decoded from the sealed bytes; no member reaches back into a live
/// run, tape, journal, or repository.
internal sealed class RepositoryNativeTerminalDecoded
{
    internal RepositoryNativeTerminalDecoded(
        RepositoryNativeTerminalEvidenceRON document,
        RepositoryNativeTerminalRuntimeSnapshot runtime,
        RepositoryLoopClosureWorldSnapshot world,
        RepositoryLoopClosureTapeSnapshot tape,
        RepositoryLoopClosureJournalSnapshot journal,
        RepositoryLoopClosureAccessSnapshot access,
        RepositoryLoopClosureFrontierSnapshot frontier,
        RepositoryLoopClosurePatternSnapshot pattern,
        RepositoryLoopClosureJournalSnapshot preSealJournal,
        IReadOnlyList<LoopLineageEdgeReceipt> lineageEdges,
        RepositoryLoopClosureTapeSeal seal)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Runtime = runtime;
        World = world ?? throw new ArgumentNullException(nameof(world));
        Tape = tape ?? throw new ArgumentNullException(nameof(tape));
        Journal = journal ?? throw new ArgumentNullException(nameof(journal));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        Frontier = frontier ?? throw new ArgumentNullException(nameof(frontier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        PreSealJournal = preSealJournal ?? throw new ArgumentNullException(nameof(preSealJournal));
        LineageEdges = Array.AsReadOnly((lineageEdges ?? throw new ArgumentNullException(nameof(lineageEdges))).ToArray());
        Seal = seal;
    }

    internal RepositoryNativeTerminalEvidenceRON Document { get; }
    internal RepositoryNativeTerminalRuntimeSnapshot Runtime { get; }
    internal RepositoryLoopClosureWorldSnapshot World { get; }
    internal RepositoryLoopClosureTapeSnapshot Tape { get; }
    internal RepositoryLoopClosureJournalSnapshot Journal { get; }
    internal RepositoryLoopClosureAccessSnapshot Access { get; }
    internal RepositoryLoopClosureFrontierSnapshot Frontier { get; }
    internal RepositoryLoopClosurePatternSnapshot Pattern { get; }
    internal RepositoryLoopClosureJournalSnapshot PreSealJournal { get; }
    internal IReadOnlyList<LoopLineageEdgeReceipt> LineageEdges { get; }
    internal RepositoryLoopClosureTapeSeal Seal { get; }
    internal string ImmutableAuthoritySHA256 => Document.immutableAuthoritySHA256;
    internal string SealedEvidenceAuthoritySHA256 => Document.sealedEvidenceAuthoritySHA256;
    internal RepositoryNativeRegisteredAuthorityRON? RegisteredAuthority => Document.registeredAuthority;
}

/// Immutable runtime view reconstructed from terminal bytes. The mutable keyframe
/// owners stay inside DecodeDocument only; assemblers receive sealed snapshots.
internal sealed class RepositoryNativeTerminalRuntimeSnapshot
{
    internal RepositoryNativeTerminalRuntimeSnapshot(
        string root, string glob, string query, string querySHA256, string sourceAuthoritySHA256,
        RepositoryLoopClosureWorldSnapshot world, RepositoryLoopClosureAccessSnapshot access,
        RepositoryLoopClosureFrontierSnapshot frontier, RepositoryLoopClosurePatternSnapshot pattern)
    {
        Root = root; Glob = glob; Query = query; QuerySHA256 = querySHA256;
        SourceAuthoritySHA256 = sourceAuthoritySHA256;
        World = world ?? throw new ArgumentNullException(nameof(world));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        Frontier = frontier ?? throw new ArgumentNullException(nameof(frontier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }

    internal string Root { get; }
    internal string Glob { get; }
    internal string Query { get; }
    internal string QuerySHA256 { get; }
    internal string SourceAuthoritySHA256 { get; }
    internal RepositoryLoopClosureWorldSnapshot World { get; }
    internal RepositoryLoopClosureAccessSnapshot Access { get; }
    internal RepositoryLoopClosureFrontierSnapshot Frontier { get; }
    internal RepositoryLoopClosurePatternSnapshot Pattern { get; }
}

/// A narrow DTO keeps the persisted contract generated and RON-native while
/// leaving the live report classes free of serializer concerns.
internal readonly record struct RepositoryNativeRuntimeSnapshot(
    string Root,
    string Glob,
    string Query,
    string QuerySHA256,
    string SourceAuthoritySHA256,
    RepositoryLoopClosureWorldSnapshot World,
    RepositoryAccessJournal Access,
    RepositoryCandidateFrontierSnapshot Frontier,
    RepositoryPatternStoreSnapshot Pattern,
    RepositoryCandidateFrontier FrontierState,
    RepositoryPatternStore PatternState);

[RonObject]
internal partial class RepositoryNativeTerminalEvidenceRON
{
    // Frozen RON field names theory*; identifier-side names are Pattern*.
    public int schemaVersion;
    public string runID = "";
    public string root = "";
    public string glob = "";
    public string query = "";
    public string querySHA256 = "";
    public string sourceAuthoritySHA256 = "";
    public string worldSHA256 = "";
    public string worldSnapshotSHA256 = "";
    public string accessSHA256 = "";
    public string accessSnapshotSHA256 = "";
    public string worldAuthoritySHA256 = "";
    public string accessAuthoritySHA256 = "";
    public string frontierAuthoritySHA256 = "";
    public string theoryAuthoritySHA256 = "";
    public string tapeAuthoritySHA256 = "";
    public string journalAuthoritySHA256 = "";
    public string frontierRuntimeAuthoritySHA256 = "";
    public string frontierSnapshotSHA256 = "";
    public ulong frontierRevision;
    public string theoryCommittedAuthoritySHA256 = "";
    public string theorySnapshotSHA256 = "";
    public string theoryPendingAuthoritySHA256 = "";
    public string accessKeyframeBase64 = "";
    public string frontierKeyframeBase64 = "";
    public string theoryKeyframeBase64 = "";
    public string preSealTapeSHA256 = "";
    public string preSealJournalSHA256 = "";
    public string immutableAuthoritySHA256 = "";
    public string tapeSHA256 = "";
    public string journalSHA256 = "";
    public string sealedEvidenceAuthoritySHA256 = "";
    public long sealEventID = -1;
    public string sealPayloadSHA256 = "";
    public string sealReceiptSHA256 = "";
    public RepositoryNativeRegisteredAuthorityRON? registeredAuthority;
    public List<RepositoryNativeWorldFileRON> worldFiles = new();
    public List<RepositoryNativeAccessEntryRON> accessEntries = new();
    public List<RepositoryNativeCandidateRON> frontierCandidates = new();
    public List<RepositoryNativeTransitionRON> frontierTransitions = new();
    public List<RepositoryNativeSelectionRON> frontierSelections = new();
    public List<string> observedPaths = new();
    public List<RepositoryNativePatternOccurrenceRON> theoryOccurrences = new();
    public List<RepositoryNativePatternCompositionRON> theoryCompositions = new();
    public List<RepositoryNativePatternAdmissionRON> theoryAdmissions = new();
    public RepositoryNativePatternRuleRON theoryRule = new();
    public List<RepositoryNativePendingAdmissionRON> pendingAdmissions = new();
    public List<RepositoryNativeTapeEventRON> tapeEvents = new();
    public List<string> journalLines = new();
    public List<RepositoryNativeJournalRowRON> journalRows = new();
}

[RonObject]
internal partial class RepositoryNativeRegisteredAuthorityRON
{
    public string registrationSHA256 = "";
    public string registrationDocumentSHA256 = "";
    public string taskAuthoritySHA256 = "";
    public string toolAuthoritySHA256 = "";
    public string policyAuthoritySHA256 = "";
    public string candidateAuthoritySHA256 = "";
    public string initialStateSHA256 = "";
    public string fuelAuthoritySHA256 = "";
    public string authoritySHA256 = "";

    internal static RepositoryNativeRegisteredAuthorityRON Create(
        string registrationSHA256,
        string registrationDocumentSHA256,
        string taskAuthoritySHA256,
        string toolAuthoritySHA256,
        string policyAuthoritySHA256,
        string candidateAuthoritySHA256,
        string initialStateSHA256,
        string fuelAuthoritySHA256)
    {
        RepositoryNativeRegisteredAuthorityRON authority = new()
        {
            registrationSHA256 = registrationSHA256,
            registrationDocumentSHA256 = registrationDocumentSHA256,
            taskAuthoritySHA256 = taskAuthoritySHA256,
            toolAuthoritySHA256 = toolAuthoritySHA256,
            policyAuthoritySHA256 = policyAuthoritySHA256,
            candidateAuthoritySHA256 = candidateAuthoritySHA256,
            initialStateSHA256 = initialStateSHA256,
            fuelAuthoritySHA256 = fuelAuthoritySHA256,
        };
        string[] fields = [registrationSHA256, registrationDocumentSHA256, taskAuthoritySHA256,
            toolAuthoritySHA256, policyAuthoritySHA256, candidateAuthoritySHA256, initialStateSHA256, fuelAuthoritySHA256];
        authority.authoritySHA256 = ComputeAuthoritySHA256(fields);
        authority.Validate();
        return authority;
    }

    public void Validate()
    {
        string[] fields = [registrationSHA256, registrationDocumentSHA256, taskAuthoritySHA256,
            toolAuthoritySHA256, policyAuthoritySHA256, candidateAuthoritySHA256, initialStateSHA256, fuelAuthoritySHA256];
        if (fields.Any(static value => value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
            || authoritySHA256 is not { Length: 64 } || !authoritySHA256.All(Uri.IsHexDigit))
            throw new InvalidDataException("native repository registered authority is malformed");
        string expected = ComputeAuthoritySHA256(fields);
        if (authoritySHA256 != expected)
            throw new InvalidDataException("native repository registered authority diverges");
    }

    private static string ComputeAuthoritySHA256(IReadOnlyList<string> fields)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('|', new[] { "repository-native-registered-authority-v1" }.Concat(fields)))));
}

[RonObject]
internal partial class RepositoryNativeWorldFileRON
{
    public string path = ""; public long bytes; public string sha256 = ""; public string contentBase64 = "";
}

[RonObject]
internal partial class RepositoryNativeAccessEntryRON
{
    public int step; public long sequence; public string callSHA256 = ""; public byte verb; public string argument = "";
    public string[] paths = []; public string[] loci = []; public string renderedBase64 = ""; public string entrySHA256 = "";
}

[RonObject]
internal partial class RepositoryNativeCandidateRON
{
    public byte species; public ulong digest; public string canonical = "";
}

[RonObject]
internal partial class RepositoryNativeSelectionRON
{
    public ulong revision; public string runtimeAuthoritySHA256 = ""; public long ordinal;
    public long selectionEventID; public string selectionReceiptSHA256 = "";
    public ulong candidateDigest; public string candidateCanonical = "";
}

[RonObject]
internal partial class RepositoryNativeTransitionRON
{
    public ulong digest; public string canonical = ""; public byte state; public int attempts;
    public long sourceEventID; public long predecessorEventID; public string callSHA256 = ""; public string accessSHA256 = "";
    public byte verificationOutcome; public string theoryOrigin = ""; public string theoryOriginRuleID = "";
    public string theoryOriginOccurrenceSetSHA256 = ""; public string theoryOriginReceiptCanonical = "";
}

// Frozen RON field names theoryOrigin*, claim*, and verification*; identifier-side names are PatternOrigin*, Prediction*, and OccurrenceCheck*.
[RonObject]
internal partial class RepositoryNativePatternOccurrenceRON
{
    public string claimID = ""; public string claim = ""; public byte claimSpecies; public string claimPath = "";
    public int claimLine; public string claimValue = ""; public string claimOtherPath = "";
    public string verificationCanonical = ""; public string verificationReceiptSHA256 = "";
    public long sourceEventID; public long verificationEventID;
}

// Frozen RON field names support*; identifier-side names are Occurrence*.
[RonObject]
internal partial class RepositoryNativePatternCompositionRON
{
    public string ruleID = ""; public string supportSetSHA256 = ""; public ulong candidateDigest; public string candidateCanonical = "";
    public long[] supportReceiptEventIDs = []; public string receiptCanonical = ""; public string receiptSHA256 = "";
}

// Frozen RON field names support* and derivation*; identifier-side names are Occurrence* and Composition*.
[RonObject]
internal partial class RepositoryNativePatternAdmissionRON
{
    public string ruleID = ""; public string supportSetSHA256 = ""; public ulong candidateDigest; public string candidateCanonical = "";
    public string derivationReceiptSHA256 = ""; public string worldSHA256 = ""; public string accessSHA256 = "";
    public ulong parentRevision; public int wScale; public string pricingBasisDigest = "";
    public int baselineRuleCount; public int baselineCompressedLength; public int rawSymbolLength; public int rawWeightLength;
    public long literalCostMbits; public long materializedCostMbits; public long marginalSavingsMbits; public byte decision;
    public long economicsEventID; public string economicsPayloadSHA256 = ""; public string economicsJournal = "";
    public long reflectedTapeEventID; public string reflectionJournal = ""; public ulong consumedRevision; public string lineageNodeID = "";
    public string digest = ""; public string identityKey = "";
}

[RonObject]
internal partial class RepositoryNativePatternRuleRON
{
    // Frozen RON field names derived*; identifier-side names are Composed*.
    public string id = ""; public string canonical = ""; public byte derivedSpecies;
    public string derivedAdmissionPath = ""; public string alternativeAdmissionPath = "";
}

[RonObject]
internal partial class RepositoryNativePendingAdmissionRON
{
    public ulong digest; public string canonical = "";
}

[RonObject]
internal partial class RepositoryNativeTapeEventRON
{
    public long eventID; public string source = ""; public byte provenance; public byte roles; public bool evidence; public string payloadBase64 = "";
}

[RonObject]
internal partial class RepositoryNativeJournalRowRON
{
    public int lineIndex; public int step; public long eventID; public string source = ""; public string sha256 = "";
}
