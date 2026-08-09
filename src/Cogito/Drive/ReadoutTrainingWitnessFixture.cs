namespace Cogito;

using Cogito.Grammar;

/// Executable mutation matrix for the R4 training and funded divergence corroboration.
/// Every mutation keeps the original digest, so acceptance would prove that a
/// causal field is not actually covered by the typed identity.
internal static class ReadoutTrainingCorroborationFixture
{
    internal static bool Run(TextWriter output)
    {
        CortexPolicyID policy = new("fixture.policy");
        PolicyCanonicalStateID state = new(policy, PolicyCanonicalStateKinds.Generic, 1, 7);
        GrammarPolicyContextKey context = new(in state, 2, 1);
        TapeEventID[] teacherEvidenceEvents = [new(11), new(12)];
        TapeEventID[] foldEvents = [new(10), new(11), new(12), new(13)];
        ReadoutTrainingCorroboration corroboration = ReadoutTrainingCorroboration.Create(
            policy, new TapeEventID(13), new TapeEventID(10), teacherEvidenceEvents,
            Digest('a'), new LoopClosureCompositionEpisodeID("fixture-episode"), Digest('b'),
            new GrammarRevisionID(1), new GrammarRevisionID(2), foldEvents,
            new LoopClosureDigest(LoopClosureCompositionEpisode.ComputeEventDigest(foldEvents)), Digest('c'),
            in state, in context, 0x1111, 0x2222, new GrammarRevisionID(3),
            new CortexPolicyDecisionID(5), new TapeEventID(14));

        bool baseline = Accept(() => corroboration.Validate());
        bool teacherPacket = Reject(() => Rebuild(corroboration, teacherPacketEventID: new TapeEventID(99)));
        bool teacherComposition = Reject(() => Rebuild(corroboration, teacherCompositionEventID: new TapeEventID(99)));
        bool teacherEvidence = Reject(() => Rebuild(corroboration, teacherEvidenceEventIDs: [new TapeEventID(12), new TapeEventID(11)]));
        bool foldConsumed = Reject(() => Rebuild(corroboration, consumingFoldConsumedEventIDs: [new TapeEventID(10), new TapeEventID(11), new TapeEventID(13)]));
        bool foldDigest = Reject(() => Rebuild(corroboration, consumingFoldConsumedEventSHA256: Digest('d')));
        bool contextIdentity = Reject(() => Rebuild(corroboration, contextDigest: corroboration.ContextDigest ^ 1UL));
        bool contextActionCount = Reject(() => Rebuild(corroboration, contextActionCount: 0));
        bool canonicalContextCardinality = RejectArgument(() => _ = new GrammarPolicyContextKey(in state, 0, 1));
        bool support = Reject(() => Rebuild(corroboration, selectedCandidateOccurrenceDigest: corroboration.SelectedCandidateOccurrenceDigest ^ 1UL));
        bool fingerprint = Reject(() => Rebuild(corroboration, selectedCandidateFingerprint: corroboration.SelectedCandidateFingerprint ^ 1UL));

        PaidDivergenceExecutionCorroboration execution = PaidDivergenceExecutionCorroboration.Create(
            corroboration.ReadoutTrainingCorroborationSHA256, new CortexPolicyQuotaDecisionID(1),
            0x4444, 0x3333,
            corroboration.SelectedCandidateRevision, Digest('e'), Digest('f'),
            new CortexPolicyDecisionID(7), Digest('2'), new TapeEventID(700), new string('a', 64));
        bool funding = Reject(() => Rebuild(execution, fundingDecisionID: new CortexPolicyQuotaDecisionID(2)));
        bool fundingReadout = Reject(() => Rebuild(execution, fundingReadoutFingerprint: execution.QuotaReadoutFingerprint ^ 1UL));
        bool fundingFingerprint = Reject(() => Rebuild(execution, fundingCandidateFingerprint: execution.QuotaCandidateFingerprint ^ 1UL));
        bool fundingRevision = Reject(() => Rebuild(execution, fundingCandidateRevision: new GrammarRevisionID(4)));
        bool executedDivergence = Reject(() => Rebuild(execution, executedDivergenceDecisionID: new CortexPolicyDecisionID(8)));
        bool childReceipt = Reject(() => Rebuild(execution, childExecutionReceiptSHA256: Digest('0')));
        bool executedDivergenceOutcome = Reject(() => Rebuild(execution, executedDivergenceOutcomeID: Digest('3')));
        bool executedDivergenceOutcomeEvent = Reject(() => Rebuild(execution, executedDivergenceOutcomeEventID: new TapeEventID(701)));
        bool executedDivergenceOutcomePayload = Reject(() => Rebuild(execution, executedDivergenceOutcomePayloadSHA256: new string('q', 64)));
        bool forkArm = Reject(() => Rebuild(execution, forkArmSHA256: Digest('0')));

        bool passed = baseline && teacherPacket && teacherComposition && teacherEvidence && foldConsumed
            && foldDigest && contextIdentity && contextActionCount && canonicalContextCardinality && support && fingerprint && funding && fundingReadout && fundingFingerprint && fundingRevision
            && executedDivergence && childReceipt && executedDivergenceOutcome && executedDivergenceOutcomeEvent && executedDivergenceOutcomePayload && forkArm;
        output.WriteLine($"  r4 witness mutation matrix · baseline={(baseline ? "valid" : "FAIL")} teacher-packet={(teacherPacket ? "rejected" : "ACCEPTED")} teacher-derivation={(teacherComposition ? "rejected" : "ACCEPTED")} teacher-evidence={(teacherEvidence ? "rejected" : "ACCEPTED")} fold-consumed={(foldConsumed ? "rejected" : "ACCEPTED")} fold-digest={(foldDigest ? "rejected" : "ACCEPTED")} context={(contextIdentity ? "rejected" : "ACCEPTED")} context-action-count={(contextActionCount ? "rejected" : "ACCEPTED")} canonical-context-cardinality={(canonicalContextCardinality ? "rejected" : "ACCEPTED")} support={(support ? "rejected" : "ACCEPTED")} fingerprint={(fingerprint ? "rejected" : "ACCEPTED")} funding={(funding ? "rejected" : "ACCEPTED")} funding-readout={(fundingReadout ? "rejected" : "ACCEPTED")} funding-fingerprint={(fundingFingerprint ? "rejected" : "ACCEPTED")} funding-revision={(fundingRevision ? "rejected" : "ACCEPTED")} executed-dissent={(executedDivergence ? "rejected" : "ACCEPTED")} child-receipt={(childReceipt ? "rejected" : "ACCEPTED")} executed-dissent-outcome={(executedDivergenceOutcome ? "rejected" : "ACCEPTED")} outcome-event={(executedDivergenceOutcomeEvent ? "rejected" : "ACCEPTED")} outcome-payload={(executedDivergenceOutcomePayload ? "rejected" : "ACCEPTED")} fork-arm={(forkArm ? "rejected" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static LoopClosureDigest Digest(char value) => new(new string(value, 64));

    private static bool Accept(Action action)
    {
        try { action(); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static bool RejectArgument(Action action)
    {
        try { action(); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }

    private static bool Reject(Func<ReadoutTrainingCorroboration> build)
    {
        try { _ = build(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static bool Reject(Func<PaidDivergenceExecutionCorroboration> build)
    {
        try { _ = build(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static ReadoutTrainingCorroboration Rebuild(
        in ReadoutTrainingCorroboration source,
        TapeEventID? teacherPacketEventID = null,
        TapeEventID? teacherCompositionEventID = null,
        IReadOnlyList<TapeEventID>? teacherEvidenceEventIDs = null,
        IReadOnlyList<TapeEventID>? consumingFoldConsumedEventIDs = null,
        LoopClosureDigest? consumingFoldConsumedEventSHA256 = null,
        ulong? contextDigest = null,
        int? contextActionCount = null,
        ulong? selectedCandidateFingerprint = null,
        ulong? selectedCandidateOccurrenceDigest = null)
    {
        PolicyCanonicalStateID state = source.CanonicalState;
        return new(
            source.Policy, teacherPacketEventID ?? source.TeacherPacketEventID,
            teacherCompositionEventID ?? source.TeacherCompositionEventID,
            teacherEvidenceEventIDs ?? source.TeacherEvidenceEventIDs,
            source.TeacherEvidenceSHA256, source.SourceEpisodeID, source.SourceEpisodeSHA256,
            source.ConsumingFoldPreviousRevision, source.ConsumingFoldRevision,
            consumingFoldConsumedEventIDs ?? source.ConsumingFoldConsumedEventIDs,
            consumingFoldConsumedEventSHA256 ?? source.ConsumingFoldConsumedEventSHA256,
            source.ConsumingFoldReceiptSHA256, in state, contextDigest ?? source.ContextDigest,
            contextActionCount ?? source.ContextActionCount, source.ContextDeliberationDepth,
            selectedCandidateFingerprint ?? source.SelectedCandidateFingerprint,
            selectedCandidateOccurrenceDigest ?? source.SelectedCandidateOccurrenceDigest,
            source.SelectedCandidateRevision, source.DecisionID, source.DecisionEventID,
            source.ReadoutTrainingCorroborationSHA256);
    }

    private static PaidDivergenceExecutionCorroboration Rebuild(
        in PaidDivergenceExecutionCorroboration source,
        CortexPolicyQuotaDecisionID? fundingDecisionID = null,
        ulong? fundingReadoutFingerprint = null,
        ulong? fundingCandidateFingerprint = null,
        GrammarRevisionID? fundingCandidateRevision = null,
        LoopClosureDigest? forkArmSHA256 = null,
        LoopClosureDigest? childExecutionReceiptSHA256 = null,
        CortexPolicyDecisionID? executedDivergenceDecisionID = null,
        LoopClosureDigest? executedDivergenceOutcomeID = null,
        TapeEventID? executedDivergenceOutcomeEventID = null,
        string? executedDivergenceOutcomePayloadSHA256 = null)
        => new(
            source.ReadoutTrainingCorroborationSHA256, fundingDecisionID ?? source.QuotaDecisionID,
            fundingReadoutFingerprint ?? source.QuotaReadoutFingerprint,
            fundingCandidateFingerprint ?? source.QuotaCandidateFingerprint,
            fundingCandidateRevision ?? source.FundingCandidateRevision,
            forkArmSHA256 ?? source.ForkArmSHA256,
            childExecutionReceiptSHA256 ?? source.ChildExecutionReceiptSHA256,
            executedDivergenceDecisionID ?? source.ExecutedDivergenceDecisionID,
            executedDivergenceOutcomeID ?? source.ExecutedDivergenceOutcomeID,
            source.PaidDivergenceExecutionCorroborationSHA256,
            executedDivergenceOutcomeEventID ?? source.ExecutedDivergenceOutcomeEventID,
            executedDivergenceOutcomePayloadSHA256 ?? source.ExecutedDivergenceOutcomePayloadSHA256);
}
