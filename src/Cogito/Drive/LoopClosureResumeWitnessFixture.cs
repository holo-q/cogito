namespace Cogito;

using Cogito.Grammar;

/// Save/load corroboration for the R4 resume seam. The fold index is intentionally
/// absent from the loaded Cortex: only ordinary tape publications may recreate
/// the exact teacher fold and displaced predecessor.
internal static class LoopClosureResumeCorroborationFixture
{
    internal static bool Run(TextWriter output)
    {
        string directory = Path.Combine(".tmp", $"r4-resume-witness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            (Tape source, TapeEventID displacedEvent, LoopClosureTeacherPacketProvenance teacher, CortexPolicyID policy,
                PolicyCanonicalStateID state, MetricSample[] features) = BuildTape(includeSourceFold: true);
            using (source)
            {
                Tape loaded = RoundTrip(source);
                using (loaded)
                {
                    Cortex cortex = new(new CortexConfig { Tools = [], ActionPolicies = [], Rewards = [] });
                    CortexPolicyDecision decision = new(
                        new CortexPolicyDecisionID(1), policy,
                        new CortexPolicyDecisionReadout(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad,
                            new GrammarRevisionID(8), CortexPolicySelectionCauses.Launchpad));
                    bool firstDecision = cortex.VerifyLoopClosureFirstPolicyDecisionAfterLoad(
                        loaded, new Journal(), directory, in decision, in state, features, 2, out LoopLineageNode learnedReadout);
                    bool bindingAfterFirst = cortex.VerifyLoopClosureReadoutBindingAfterLoad(
                        loaded, new Journal(), directory, new GrammarRevisionID(8), out LoopClosureTeacherPacketProvenance restored,
                        out LoopLineageNode predecessor);
                    bool exact = firstDecision
                        && learnedReadout.Species == LoopLineageNodeSpecies.LearnedReadout
                        && learnedReadout.GrammarRevision == decision.Readout.GrammarRevision
                        && bindingAfterFirst
                        && restored.EpisodeID == teacher.EpisodeID
                        && restored.FoldRevision == teacher.FoldRevision
                        && restored.FoldRevision < learnedReadout.GrammarRevision
                        && restored.MatchedEventIDs.SequenceEqual(teacher.MatchedEventIDs)
                        && predecessor.Species == LoopLineageNodeSpecies.DisplacedEvaluation
                        && predecessor.EventID == displacedEvent;

                    (Tape missingSource, _, _, _, _, _) = BuildTape(includeSourceFold: false);
                    using (missingSource)
                    {
                        Tape missingLoaded = RoundTrip(missingSource);
                        using (missingLoaded)
                        {
                            Cortex missing = new(new CortexConfig { Tools = [], ActionPolicies = [], Rewards = [] });
                            bool missingRejected;
                            try
                            {
                                missingRejected = !missing.VerifyLoopClosureFirstPolicyDecisionAfterLoad(
                                    missingLoaded, new Journal(), directory, in decision, in state, features, 2, out _);
                            }
                            catch (InvalidDataException)
                            {
                                missingRejected = true;
                            }

                            output.WriteLine($"  r4 resume corroboration · save-load={(exact ? "exact" : "BROKEN")} first-decision={(firstDecision && bindingAfterFirst ? "exact" : "BROKEN")} · missing-fold={(missingRejected ? "rejected" : "ACCEPTED")} · {(exact && missingRejected ? "PASS" : "FAIL")}");
                            return exact && missingRejected;
                        }
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static (Tape Tape, TapeEventID DisplacedEvent, LoopClosureTeacherPacketProvenance Teacher, CortexPolicyID Policy,
        PolicyCanonicalStateID State, MetricSample[] Features) BuildTape(bool includeSourceFold)
    {
        Tape tape = new();
        Journal journal = new();
        TapeEventID sourceEvent = TapePacketCreator.CommitWorldEncounter(
            tape, journal, 0, "r4-resume-witness"u8.ToArray(), 0, 0, fresh: true, coverage: 1.0);
        LoopLineageTurnstile lineage = new(tape, journal);
        LoopLineageEdgeReceipt world = lineage.Emit(0, LoopLineageNodeSpecies.AdmissionPlan, sourceEvent);
        TapeEventID lawEvent = tape.Append("verified-law"u8.ToArray(), "fixture:law", Provenances.Execution);
        LoopLineageEdgeReceipt law = lineage.Emit(0, LoopLineageNodeSpecies.VerifiedLaw, lawEvent, predecessorIDs: [world.Node.NodeID]);
        TapeEventID derivationEvent = tape.Append("rung0-derivation"u8.ToArray(), "fixture:rung0", Provenances.Execution);
        LoopLineageEdgeReceipt derivation = lineage.Emit(0, LoopLineageNodeSpecies.Rung0Composition, derivationEvent,
            grammarRevision: new GrammarRevisionID(5), predecessorIDs: [law.Node.NodeID]);
        TapeEventID displacedEvent = tape.Append("displaced-evaluation"u8.ToArray(), "fixture:displaced", Provenances.Execution);
        _ = lineage.Emit(0, LoopLineageNodeSpecies.DisplacedEvaluation, displacedEvent,
            grammarRevision: new GrammarRevisionID(5), predecessorIDs: [derivation.Node.NodeID]);

        LoopClosureCompositionEpisode episode = LoopClosureCompositionEpisode.Create(
            new LoopClosureCompositionEpisodeID(derivation.Node.NodeID.Value), derivation.Node.EventID,
            [law.Node.EventID], new GrammarRevisionID(5));
        GrammarFoldProvenanceReceipt sourceFold = GrammarFoldProvenanceReceipt.Create(
            new GrammarRevisionID(5), new GrammarRevisionID(6), [derivation.Node.EventID, law.Node.EventID], [episode]);
        if (includeSourceFold) _ = TapePacketCreator.AppendGrammarFoldInstallRevision(tape, journal, 0, in sourceFold);

        CortexPolicyID policy = new("fixture.r4.resume");
        PolicyCanonicalStateID state = new(policy, PolicyCanonicalStateKinds.Generic, 1, 1);
        MetricSample[] features = [new(new MetricID(1), NumericValue.FromI64(1))];
        LoopClosureTeacherPacketProvenance teacher = LoopClosureTeacherPacketProvenance.Create(
            episode.EpisodeID, sourceFold.Revision, [derivation.Node.EventID, law.Node.EventID], episode.EvidenceDigest);
        TapeEventID teacherEvent = TapePacketCreator.AppendPolicyCanonicalExample(
            tape, journal, 0, policy, in state, 0, features, 2, in teacher).GrammarEventID;
        GrammarFoldProvenanceReceipt consumingFold = GrammarFoldProvenanceReceipt.Create(
            sourceFold.Revision, new GrammarRevisionID(7), [.. sourceFold.ConsumedEventIDs, teacherEvent], [episode]);
        _ = TapePacketCreator.AppendGrammarFoldInstallRevision(tape, journal, 0, in consumingFold);
        return (tape, displacedEvent, teacher, policy, state, features);
    }

    private static Tape RoundTrip(Tape source)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) source.Save(writer);
        stream.Position = 0;
        Tape loaded = new();
        using (CkptReader reader = new(stream)) loaded.Load(reader);
        return loaded;
    }

}
