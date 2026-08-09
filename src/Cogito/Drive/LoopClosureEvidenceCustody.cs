namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Grammar;

/// Reconstructs the ordinary tape/journal/checkpoint custody behind the immutable
/// loop-closure receipts.  A typed RON receipt is only a prediction about an event; this
/// verifier requires the event to still be addressable through the run's three
/// ordinary durability surfaces.
internal static class LoopClosureEvidenceCustody
{
    internal static bool VerifyReadoutR4(Tape tape, Journal journal, in LoopClosureR4Provenance r4, out string failure)
    {
        failure = "";
        try
        {
            r4.Validate();
            Dictionary<long, byte[]> payloads = tape.GetEventViews().ToDictionary(view => view.Id.Value, view => tape.Resolve(view.Id, out byte[] payload) ? payload : []);
            Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> journalRows = new();
            HashSet<long> journalEvents = new();
            foreach (string line in journal.EnumerateAllLines(journal.DurablePath))
            {
                if (!Journal.TryParseBindingRow(line, out int step, out TapeEventID eventID, out string source)) continue;
                string[] fields = line.Split('\t');
                if (fields.Length < 5 || !TryParseByteCount(fields[^1], out int byteCount)) continue;
                string rowSource = fields[1] == "repository-verification" ? "repository:verification" : source;
                if (!journalRows.TryAdd(eventID.Value, (step, rowSource,
                        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(line))), byteCount, line)))
                {
                    failure = $"journal custody repeats event {eventID.Value}";
                    return false;
                }
                journalEvents.Add(eventID.Value);
            }
            Dictionary<long, string> tapeSources = tape.GetEventViews().ToDictionary(view => view.Id.Value, view => view.Source);
            Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority = tape.GetEventViews()
                .ToDictionary(view => view.Id.Value, view => (view.Source, view.Provenance, view.Roles));
            if (!RequireEvents("R4 episode", [r4.Episode.CompositionEventID, .. r4.Episode.EvidenceEventIDs], payloads, journalEvents, out failure)
                || !VerifyNativeR4Events("R4 episode", [r4.Episode.CompositionEventID, .. r4.Episode.EvidenceEventIDs], tapeAuthority, out failure)
                || !VerifyJournalBindings("R4 episode", [r4.Episode.CompositionEventID, .. r4.Episode.EvidenceEventIDs], payloads, tapeSources, journalRows, out failure)
                || !RequireEvents("R4 fold", r4.Fold.ConsumedEventIDs, payloads, journalEvents, out failure)
                || !VerifyFoldInstallRevision(r4.Fold, payloads, journalEvents, tapeSources, journalRows, out failure)
                || !RequireEvents("R4 teacher", r4.Teacher.MatchedEventIDs, payloads, journalEvents, out failure)
                || !VerifyNativeR4Events("R4 teacher", r4.Teacher.MatchedEventIDs, tapeAuthority, out failure)
                || !VerifyJournalBindings("R4 teacher", r4.Teacher.MatchedEventIDs, payloads, tapeSources, journalRows, out failure)
                || !VerifyTeacherPacketAuthority(r4.Training.TeacherPacketEventID, tapeAuthority, out failure)
                || !VerifyJournalBindings("R4 teacher packet", [r4.Training.TeacherPacketEventID], payloads, tapeSources, journalRows, out failure)
                || !VerifyRepositoryCompositionPacket(r4.Episode.CompositionEventID, r4.Episode, payloads, journalEvents,
                    LoopLineageVerifier.ReadTapeEdges(tape), tapeAuthority, tapeSources, journalRows, out failure)
                || !VerifyTeacherPacket(r4.Teacher, r4.Fold.ConsumedEventIDs, r4.Training.TeacherPacketEventID, payloads, out _, out failure)) return false;
            return true;
        }
        catch (InvalidDataException error) { failure = error.Message; return false; }
    }
    internal static bool VerifyPatternLineageCustodyFixture()
    {
        byte[] worldPayload10 = Encoding.UTF8.GetBytes("world-root-10");
        byte[] worldPayload2 = Encoding.UTF8.GetBytes("world-root-2");
        byte[] lawPayload10 = Encoding.ASCII.GetBytes("LAW\tclass\tx = y\tclaim\t000000000000000A\tx = y\u0001000000000000000A\u0001claim");
        byte[] lawPayload2 = Encoding.ASCII.GetBytes("LAW\tclass\ty = x\tclaim\t000000000000000B\ty = x\u0001000000000000000B\u0001claim");
        byte[] mintPayload = Encoding.ASCII.GetBytes("x = y");
        string mintLineDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(mintPayload));
        string worldDigest10 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(worldPayload10));
        string worldDigest2 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(worldPayload2));
        string lawDigest10 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(lawPayload10));
        string lawDigest2 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(lawPayload2));
        LoopLineageNodeID worldID10 = new("world-10");
        LoopLineageNodeID worldID2 = new("world-2");
        LoopLineageNodeID lawID10 = new("law-10");
        LoopLineageNodeID lawID2 = new("law-2");
        LoopLineageNodeID compositionID = new("derivation-node");
        LoopLineageEdgeReceipt world10 = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("world-edge-10"),
            new LoopLineageNode(worldID10, LoopLineageNodeSpecies.AdmissionPlan, new TapeEventID(10), worldDigest10, null, new LoopLineageCausalID("world:10")),
            [], [], "");
        LoopLineageEdgeReceipt world2 = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("world-edge-2"),
            new LoopLineageNode(worldID2, LoopLineageNodeSpecies.AdmissionPlan, new TapeEventID(2), worldDigest2, null, new LoopLineageCausalID("world:2")),
            [], [], world10.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt law10 = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("law-edge-10"),
            new LoopLineageNode(lawID10, LoopLineageNodeSpecies.VerifiedLaw, new TapeEventID(100), lawDigest10, null, new LoopLineageCausalID("law:10")),
            [worldID10], [worldDigest10], world2.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt law2 = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("law-edge-2"),
            new LoopLineageNode(lawID2, LoopLineageNodeSpecies.VerifiedLaw, new TapeEventID(200), lawDigest2, null, new LoopLineageCausalID("law:2")),
            [worldID2], [worldDigest2], law10.CanonicalLineageSHA256);
        string lawAdmissionID10 = "x = y\u0001000000000000000A\u0001claim";
        string lawAdmissionID2 = "y = x\u0001000000000000000B\u0001claim";
        byte[] supportPayload = Encoding.ASCII.GetBytes(
            "LAW-SUPPORT\tcandidate=" + lawAdmissionID2
            + "\tauthority=" + lawAdmissionID2
            + "\tcertificate=fixture"
            + "\tpackage=" + new string('f', 64)
            + "\tclaims=1\tclaim-digests=" + new string('c', 64)
            + "\tmint-line-digests=" + mintLineDigest
            + "\tclaim-map=1:" + new string('c', 64) + ":2"
            + "\tadmissions=1:101"
            + "\tset=" + new string('e', 64)
            + "\tworld=2\tstep=1\tindex=0\tfirst=1\trepresentative=0\tdigest=" + new string('d', 64));
        string supportDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(supportPayload));
        LoopLineageNodeID supportID = new("support-2");
        LoopLineageEdgeReceipt occurrence = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("support-edge-2"),
            new LoopLineageNode(supportID, LoopLineageNodeSpecies.VerifiedLawSupport, new TapeEventID(150), supportDigest, null,
                LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, [worldID2, lawID2])),
            [worldID2, lawID2], [worldDigest2, lawDigest2], law2.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt composition = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("derivation-edge"),
            new LoopLineageNode(compositionID, LoopLineageNodeSpecies.Rung0Composition, new TapeEventID(300), lawDigest2, null, new LoopLineageCausalID("law:1")),
            [lawID10, lawID2], [lawDigest10, lawDigest2], occurrence.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt[] lineage = [world10, world2, law10, law2, occurrence, composition];
        Dictionary<long, byte[]> payloads = new()
        {
            [1] = Encoding.ASCII.GetBytes("WORLD-ENCOUNTER\tobservation=2\titem=0\tdomain=0\tfresh=1\tcoverage=nan"),
            [2] = worldPayload2, [9] = Encoding.ASCII.GetBytes("WORLD-ENCOUNTER\tobservation=10\titem=1\tdomain=0\tfresh=1\tcoverage=nan"), [10] = worldPayload10,
            [100] = lawPayload10, [101] = mintPayload, [200] = lawPayload2, [300] = lawPayload2,
            [150] = supportPayload,
        };
        HashSet<long> journalEvents = [1, 2, 9, 10, 100, 101, 150, 200, 300];
        PatternBecameThoughtCorroboration corroboration = new(
            new EmlPredictionID(0), new EmlPredictionID(1), compositionID,
            new LoopClosureDigest(new string('a', 64)), new LoopClosureDigest(new string('b', 64)),
            0, 1, EmlObligationTargetSpecies.ExactComposition, [new TapeEventID(2), new TapeEventID(10)], [lawAdmissionID10, lawAdmissionID2]);
        bool baseline = VerifyPatternLineageCustody(corroboration, new TapeEventID(300), lineage, payloads, journalEvents);
        bool lawSwapRejected = !VerifyPatternLineageCustody(
            new PatternBecameThoughtCorroboration(
                new EmlPredictionID(0), new EmlPredictionID(1), compositionID,
                new LoopClosureDigest(new string('a', 64)), new LoopClosureDigest(new string('b', 64)),
                0, 1, EmlObligationTargetSpecies.ExactComposition, [new TapeEventID(2), new TapeEventID(10)], [lawAdmissionID10, "y = x\u0001000000000000000C\u0001claim"]),
            new TapeEventID(300), lineage, payloads, journalEvents);
        bool supportSwapRejected = !VerifyPatternLineageCustody(
            new PatternBecameThoughtCorroboration(
                new EmlPredictionID(0), new EmlPredictionID(1), compositionID,
                new LoopClosureDigest(new string('a', 64)), new LoopClosureDigest(new string('b', 64)),
                0, 1, EmlObligationTargetSpecies.ExactComposition, [new TapeEventID(4), new TapeEventID(10)], [lawAdmissionID10, lawAdmissionID2]),
            new TapeEventID(300), lineage, payloads, journalEvents);
        bool omittedJournalRejected = !VerifyPatternLineageCustody(
            corroboration, new TapeEventID(300), lineage, payloads, [2, 100, 200, 300]);
        LoopLineageEdgeReceipt omittedLaw = composition.Rebind([lawID10], [lawDigest10], law2.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt extraLaw = composition.Rebind([lawID10, lawID2, new LoopLineageNodeID("law-extra")], [lawDigest10, lawDigest2, lawDigest10], law2.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt duplicateLaw = composition.Rebind([lawID10, lawID10, lawID2], [lawDigest10, lawDigest10, lawDigest2], law2.CanonicalLineageSHA256);
        LoopLineageEdgeReceipt shuffledLaws = composition.Rebind([lawID2, lawID10], [lawDigest2, lawDigest10], law2.CanonicalLineageSHA256);
        bool omittedAncestryRejected = !VerifyPatternLineageCustody(corroboration, new TapeEventID(300), [world10, world2, law10, law2, omittedLaw], payloads, journalEvents);
        bool extraAncestryRejected = !VerifyPatternLineageCustody(corroboration, new TapeEventID(300), [world10, world2, law10, law2, extraLaw], payloads, journalEvents);
        bool duplicateAncestryRejected = !VerifyPatternLineageCustody(corroboration, new TapeEventID(300), [world10, world2, law10, law2, duplicateLaw], payloads, journalEvents);
        bool shuffledAncestryRejected = !VerifyPatternLineageCustody(corroboration, new TapeEventID(300), [world10, world2, law10, law2, shuffledLaws], payloads, journalEvents);
        bool reportRonCustody = LoopClosureReport.VerifyPatternCorroborationRonFixture();
        // Frozen tape source token world:encounter; identifier-side name is AdmissionPlan.
        Dictionary<long, (string Source, Provenances Provenance)> fixtureSources = new()
        {
            [1] = ("world:encounter", Provenances.Execution), [2] = ("corpus", Provenances.Real),
            [9] = ("world:encounter", Provenances.Execution), [10] = ("corpus", Provenances.Real),
            [100] = ("eml:law", Provenances.Reflected), [101] = ("eml", Provenances.Reflected),
            [150] = ("eml:law-support", Provenances.Reflected), [200] = ("eml:law", Provenances.Reflected)
        };
        bool supportCustody = VerifyLawSupportPackets(lineage, payloads, journalEvents, fixtureSources, out string supportFailure);
        Dictionary<long, (string Source, Provenances Provenance)> wrongSupportSource = new(fixtureSources) { [150] = ("eml:law-execution", Provenances.Reflected) };
        bool supportSourceRejected = !VerifyLawSupportPackets(lineage, payloads, journalEvents, wrongSupportSource, out _);
        Dictionary<long, (string Source, Provenances Provenance)> wrongWorldSource = new(fixtureSources) { [2] = ("eml", Provenances.Reflected) };
        bool supportWorldSourceRejected = !VerifyLawSupportPackets(lineage, payloads, journalEvents, wrongWorldSource, out _);
        Dictionary<long, (string Source, Provenances Provenance)> wrongWorldProvenance = new(fixtureSources) { [1] = ("world:encounter", Provenances.Real) };
        bool supportWorldProvenanceRejected = !VerifyLawSupportPackets(lineage, payloads, journalEvents, wrongWorldProvenance, out _);
        Dictionary<long, (string Source, Provenances Provenance)> wrongMintProvenance = new(fixtureSources) { [101] = ("eml", Provenances.Real) };
        bool supportMintProvenanceRejected = !VerifyLawSupportPackets(lineage, payloads, journalEvents, wrongMintProvenance, out _);
        bool supportProvenanceRejected = !VerifyLawSupportPackets(lineage, payloads, [.. journalEvents.Where(static id => id != 150)], fixtureSources, out _);
        Dictionary<long, byte[]> forgedSupportPayloads = new(payloads)
        {
            [150] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(supportPayload).Replace("world=2", "world=10", StringComparison.Ordinal))
        };
        bool forgedSupportRejected = !VerifyLawSupportPackets(lineage, forgedSupportPayloads, journalEvents, fixtureSources, out _);
        Dictionary<long, byte[]> forgedMintPayloads = new(payloads)
        {
            [150] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(supportPayload).Replace("admissions=1:101", "admissions=1:200", StringComparison.Ordinal))
        };
        bool supportMintRejected = !VerifyLawSupportPackets(lineage, forgedMintPayloads, journalEvents, fixtureSources, out _);
        Dictionary<long, byte[]> forgedSourcePayloads = new(payloads)
        {
            [150] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(supportPayload).Replace("claims=1", "claims=2", StringComparison.Ordinal))
        };
        bool supportPredictionSourceRejected = !VerifyLawSupportPackets(lineage, forgedSourcePayloads, journalEvents, fixtureSources, out _);
        Dictionary<long, byte[]> forgedOrderPayloads = new(payloads)
        {
            [150] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(supportPayload).Replace("claim-map=1:" + new string('c', 64) + ":2", "claim-map=1:" + new string('c', 64) + ":2,1", StringComparison.Ordinal))
        };
        bool supportOrderRejected = !VerifyLawSupportPackets(lineage, forgedOrderPayloads, journalEvents, fixtureSources, out _);
        Dictionary<long, byte[]> forgedLineDigestPayloads = new(payloads)
        {
            [150] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(supportPayload).Replace(mintLineDigest, new string('a', 64), StringComparison.Ordinal))
        };
        bool supportLineDigestRejected = !VerifyLawSupportPackets(lineage, forgedLineDigestPayloads, journalEvents, fixtureSources, out _);
        Dictionary<long, byte[]> forgedPackagePayloads = new(payloads)
        {
            [150] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(supportPayload).Replace("package=" + new string('f', 64), "package=" + new string('F', 64), StringComparison.Ordinal))
        };
        bool supportPackageRejected = !VerifyLawSupportPackets(lineage, forgedPackagePayloads, journalEvents, fixtureSources, out _);
        Console.WriteLine($"  law-support mutation matrix · baseline={baseline} custody={supportCustody} source={supportSourceRejected} world-source={supportWorldSourceRejected} world-provenance={supportWorldProvenanceRejected} mint-provenance={supportMintProvenanceRejected} provenance={supportProvenanceRejected} world={forgedSupportRejected} mint={supportMintRejected} claim={supportPredictionSourceRejected} order={supportOrderRejected} line={supportLineDigestRejected} package={supportPackageRejected} report-ron={reportRonCustody} reason={supportFailure}");
        return baseline && supportCustody && lawSwapRejected && supportSwapRejected && omittedJournalRejected
            && forgedSupportRejected && supportSourceRejected && supportWorldSourceRejected && supportWorldProvenanceRejected && supportMintProvenanceRejected && supportProvenanceRejected && supportMintRejected && reportRonCustody
            && supportPredictionSourceRejected && supportOrderRejected && supportLineDigestRejected && supportPackageRejected
            && omittedAncestryRejected && extraAncestryRejected
            && duplicateAncestryRejected && shuffledAncestryRejected;
    }

    /// One read-only custody view shared by every corroboration on a recertification pass.
    /// Opening the checkpoint/tape once is essential: each corroboration is a different
    /// prediction over the same sealed bytes, not a request to rebuild the world.
    internal sealed class View : IDisposable
    {
        private View(string directory, Tape tape, LoopLineageTapeSnapshot lineageSource,
            IReadOnlyList<LoopLineageEdgeReceipt> lineage,
            Dictionary<long, byte[]> payloads, HashSet<long> journalEvents,
            bool packetCustody, string packetFailure, bool journalCustody, string journalFailure)
        {
            Directory = directory; Tape = tape; LineageSource = lineageSource; Lineage = lineage; Payloads = payloads;
            JournalEvents = journalEvents; PacketCustody = packetCustody; PacketFailure = packetFailure;
            JournalCustody = journalCustody; JournalFailure = journalFailure;
        }

        internal string Directory { get; }
        internal Tape Tape { get; }
        internal LoopLineageTapeSnapshot LineageSource { get; }
        internal IReadOnlyList<LoopLineageEdgeReceipt> Lineage { get; }
        internal Dictionary<long, byte[]> Payloads { get; }
        internal HashSet<long> JournalEvents { get; }
        internal bool PacketCustody { get; }
        internal string PacketFailure { get; }
        internal bool JournalCustody { get; }
        internal string JournalFailure { get; }

        internal static View Open(string directory)
        {
            Tape tape = Checkpoint.LoadTape(directory);
            try
            {
                Dictionary<long, byte[]> payloads = ReadTapePayloads(tape);
                HashSet<long> journalEvents = ReadJournalEvents(directory);
                LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
                IReadOnlyList<LoopLineageEdgeReceipt> lineage = LoopLineageVerifier.ReadTapeEdges(source);
                bool packets = LoopLineageVerifier.VerifyPacketBijection(source, lineage, out string packetFailure);
                bool journal = LoopLineageVerifier.VerifyJournalLineageRows(
                    source, Path.Combine(directory, "journal.log"), out string journalFailure);
                return new(directory, tape, source, lineage, payloads, journalEvents,
                    packets, packetFailure, journal, journalFailure);
            }
            catch
            {
                tape.Dispose();
                throw;
            }
        }

        public void Dispose() => Tape.Dispose();
    }

    internal static bool Verify(
        string directory,
        RunAuthority authority,
        in LoopClosureR4Provenance r4,
        in PatternBecameThoughtCorroboration pattern,
        PolicyBoundaryDivergenceAdjudication? divergence,
        in ObjectLoopClosedCorroboration outcome,
        IPolicyBoundaryDomain domain,
        out string failure)
    {
        using View view = View.Open(directory);
        return Verify(view, authority, in r4, in pattern, divergence, in outcome, domain, out failure);
    }

    internal static bool Verify(
        View view,
        RunAuthority authority,
        in LoopClosureR4Provenance r4,
        in PatternBecameThoughtCorroboration pattern,
        PolicyBoundaryDivergenceAdjudication? divergence,
        in ObjectLoopClosedCorroboration outcome,
        IPolicyBoundaryDomain domain,
        out string failure)
    {
        failure = "";
        try
        {
            r4.Validate();
            pattern.Validate(requireCorroboration: true);
            outcome.Validate(requireCorroboration: true);
            if (!VerifyCheckpoint(view.Directory, authority, out failure)) return false;

            if (!view.PacketCustody || !view.JournalCustody)
            {
                failure = $"lineage custody is incomplete: {view.PacketFailure}{view.JournalFailure}";
                return false;
            }
            LoopLineageOccurrenceCheckResult lineageResult = LoopLineageVerifier.Verify(view.Lineage, view.LineageSource);
            if (!lineageResult.Passed || !string.Equals(lineageResult.LineageSHA256, outcome.LineageSHA256.Value, StringComparison.Ordinal))
            {
                failure = "object corroboration does not bind the sealed canonical lineage digest";
                return false;
            }

            TapeEventID[] episodeEvents = [r4.Episode.CompositionEventID, .. r4.Episode.EvidenceEventIDs];
            if (!RequireEvents("R4 episode", episodeEvents, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!RequireEvents("R4 fold", r4.Fold.ConsumedEventIDs, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!VerifyFoldInstallRevision(r4.Fold, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!RequireEvents("R4 teacher", r4.Teacher.MatchedEventIDs, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!VerifyTeacherPacket(r4.Teacher, r4.Fold.ConsumedEventIDs, r4.Training.TeacherPacketEventID, view.Payloads, out long trainingTeacherPacketEventID, out failure)) return false;
            if (trainingTeacherPacketEventID != r4.Training.TeacherPacketEventID.Value)
            {
                failure = "R4 training corroboration teacher packet identity is not the tape-custodied teacher packet";
                return false;
            }
            if (!VerifyPatternPackets(pattern, r4, view.Lineage, view.Payloads, view.JournalEvents, out failure)) return false;
            Dictionary<long, (string Source, Provenances Provenance)> sources = view.Tape.GetEventViews()
                .ToDictionary(static eventView => eventView.Id.Value, static eventView => (eventView.Source, eventView.Provenance));
            if (!VerifyLawSupportPackets(view.Lineage, view.Payloads, view.JournalEvents, sources, out failure)) return false;

            if (divergence is PolicyBoundaryDivergenceAdjudication accepted)
            {
                accepted.Validate(domain);
                PolicyBoundaryDivergenceProof proof = accepted.Proof;
                if (proof.Provenance is LoopClosureR4Provenance divergenceR4)
                {
                    divergenceR4.Validate();
                    if (divergenceR4.Episode.EpisodeDigest != r4.Episode.EpisodeDigest
                        || divergenceR4.Fold.ReceiptDigest != r4.Fold.ReceiptDigest
                        || divergenceR4.Teacher.ProvenanceDigest != r4.Teacher.ProvenanceDigest)
                    {
                        failure = "divergence R4 provenance is not the selected R4 receipt";
                        return false;
                    }
                }
                if (proof.Teacher is not PolicyBoundaryTeacherCorroboration teacher)
                {
                    failure = "divergence proof omits teacher event custody";
                    return false;
                }
                teacher.Validate();
                if (!RequireEvents("divergence teacher", teacher.TeacherEventIDs, view.Payloads, view.JournalEvents, out failure)) return false;
                if (!FindBoundaryPacket(proof, view.Payloads, out long boundaryEventID))
                {
                    failure = "divergence policy-boundary packet is absent from the ordinary tape";
                    return false;
                }
                if (!view.JournalEvents.Contains(boundaryEventID))
                {
                    failure = $"divergence policy-boundary event {boundaryEventID} is absent from journal.log";
                    return false;
                }
            }

            if (outcome.TerminalOutcomeEventID < 0 || !view.Payloads.TryGetValue(outcome.TerminalOutcomeEventID, out byte[]? outcomePacket)
                || !view.JournalEvents.Contains(outcome.TerminalOutcomeEventID))
            {
                failure = "object terminal outcome event is not present in tape and journal";
                return false;
            }
            if (!TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "decision", out string decisionID)
                || divergence is not PolicyBoundaryDivergenceAdjudication acceptedDivergence
                || !TryParseTypedU64(decisionID, out ulong outcomeDecision)
                || outcomeDecision != acceptedDivergence.Proof.DecisionID.Value)
            {
                failure = "object terminal outcome event does not bind the selected divergence decision";
                return false;
            }
            PolicyBoundaryDivergenceProof acceptedProof = acceptedDivergence.Proof;
            if (!VerifyReadoutLineage(view.Tape, view.Lineage, in r4, in acceptedProof, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!VerifyOutcomeLineage(outcome, outcomePacket, view.Payloads, view.Lineage, in acceptedProof, out failure)) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or OverflowException)
        {
            failure = ex.Message;
            return false;
        }
    }

    internal static bool VerifyPattern(
        string directory,
        RunAuthority authority,
        in PatternBecameThoughtCorroboration pattern,
        out string failure)
    {
        using View view = View.Open(directory);
        return VerifyPattern(view, authority, in pattern, out failure);
    }

    internal static bool VerifyPattern(
        View view,
        RunAuthority authority,
        in PatternBecameThoughtCorroboration pattern,
        out string failure)
    {
        failure = "";
        try
        {
            pattern.Validate(requireCorroboration: true);
            if (!VerifyCheckpoint(view.Directory, authority, out failure)) return false;
            if (!view.PacketCustody || !view.JournalCustody)
            {
                failure = $"lineage custody is incomplete: {view.PacketFailure}{view.JournalFailure}";
                return false;
            }
            if (!LoopLineageVerifier.Verify(view.Lineage, view.LineageSource).Passed)
            {
                failure = "pattern lineage is not canonically valid";
                return false;
            }
            LoopLineageNodeID compositionNodeID = pattern.CompositionNodeID;
            LoopLineageEdgeReceipt? composition = view.Lineage.FirstOrDefault(edge =>
                edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition
                && edge.Node.NodeID == compositionNodeID);
            if (composition is null)
            {
                failure = "pattern composition node is absent from lineage";
                return false;
            }
            if (!RequireEvents("pattern composition", [composition.Node.EventID], view.Payloads, view.JournalEvents, out failure)) return false;
            return VerifyPatternPacket(pattern, composition.Node.EventID, view.Lineage, view.Payloads, view.JournalEvents, out failure);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or OverflowException)
        {
            failure = ex.Message;
            return false;
        }
    }

    internal static bool VerifyDivergence(
        string directory,
        RunAuthority authority,
        in LoopClosureR4Provenance r4,
        in PatternBecameThoughtCorroboration pattern,
        in PolicyBoundaryDivergenceAdjudication divergence,
        IPolicyBoundaryDomain domain,
        out string failure)
    {
        using View view = View.Open(directory);
        return VerifyDivergence(view, authority, in r4, in pattern, in divergence, domain, out failure);
    }

    internal static bool VerifyDivergence(
        View view,
        RunAuthority authority,
        in LoopClosureR4Provenance r4,
        in PatternBecameThoughtCorroboration pattern,
        in PolicyBoundaryDivergenceAdjudication divergence,
        IPolicyBoundaryDomain domain,
        out string failure)
    {
        failure = "";
        try
        {
            r4.Validate();
            pattern.Validate(requireCorroboration: true);
            divergence.Validate(domain);
            if (!VerifyCheckpoint(view.Directory, authority, out failure)) return false;
            if (!view.PacketCustody || !view.JournalCustody)
            {
                failure = $"lineage custody is incomplete: {view.PacketFailure}{view.JournalFailure}";
                return false;
            }
            if (!LoopLineageVerifier.Verify(view.Lineage, view.LineageSource).Passed)
            {
                failure = "divergence lineage is not canonically valid";
                return false;
            }
            PolicyBoundaryDivergenceProof divergenceProof = divergence.Proof;
            if (!VerifyReadoutLineage(view.Tape, view.Lineage, in r4, in divergenceProof, view.Payloads, view.JournalEvents, out failure)) return false;
            TapeEventID[] episodeEvents = [r4.Episode.CompositionEventID, .. r4.Episode.EvidenceEventIDs];
            if (!RequireEvents("R4 episode", episodeEvents, view.Payloads, view.JournalEvents, out failure)
                || !RequireEvents("R4 fold", r4.Fold.ConsumedEventIDs, view.Payloads, view.JournalEvents, out failure)
                || !VerifyFoldInstallRevision(r4.Fold, view.Payloads, view.JournalEvents, out failure)
                || !RequireEvents("R4 teacher", r4.Teacher.MatchedEventIDs, view.Payloads, view.JournalEvents, out failure)
                || !VerifyPatternPackets(pattern, r4, view.Lineage, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!VerifyTeacherPacket(r4.Teacher, r4.Fold.ConsumedEventIDs, r4.Training.TeacherPacketEventID, view.Payloads, out long trainingTeacherPacketEventID, out failure)) return false;
            if (trainingTeacherPacketEventID != r4.Training.TeacherPacketEventID.Value)
            {
                failure = "R4 training corroboration teacher packet identity is not the tape-custodied teacher packet";
                return false;
            }
            PolicyBoundaryDivergenceProof proof = divergence.Proof;
            if (proof.Provenance is not LoopClosureR4Provenance divergenceR4
                || divergenceR4.Episode.EpisodeDigest != r4.Episode.EpisodeDigest
                || divergenceR4.Fold.ReceiptDigest != r4.Fold.ReceiptDigest
                || divergenceR4.Teacher.ProvenanceDigest != r4.Teacher.ProvenanceDigest)
            {
                failure = "divergence R4 provenance is not the selected R4 receipt";
                return false;
            }
            if (proof.Teacher is not PolicyBoundaryTeacherCorroboration teacher)
            {
                failure = "divergence proof omits teacher event custody";
                return false;
            }
            teacher.Validate();
            if (!RequireEvents("divergence teacher", teacher.TeacherEventIDs, view.Payloads, view.JournalEvents, out failure)) return false;
            if (!FindBoundaryPacket(proof, view.Payloads, out long boundaryEventID)
                || !view.JournalEvents.Contains(boundaryEventID))
            {
                failure = "divergence policy-boundary packet is absent from tape or journal";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or OverflowException)
        {
            failure = ex.Message;
            return false;
        }
    }

    private static bool VerifyCheckpoint(string directory, RunAuthority authority, out string failure)
    {
        failure = "";
        string checkpoint = Path.Combine(directory, Checkpoint.FileName);
        if (!File.Exists(checkpoint)) { failure = "checkpoint.bin is absent"; return false; }
        (string _, string chain) = CheckpointDelta.ReadPhysicalAuthority(directory);
        if (!string.Equals(chain, authority.Checkpoint.PhysicalChainSHA256, StringComparison.Ordinal))
        {
            failure = "checkpoint physical chain disagrees with arm authority";
            return false;
        }
        return true;
    }

    private static Dictionary<long, byte[]> ReadTapePayloads(Tape tape)
    {
        Dictionary<long, byte[]> payloads = new();
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] payload))
                throw new InvalidDataException($"tape event {view.Id.Value} is not resolvable");
            payloads.Add(view.Id.Value, payload);
        }
        return payloads;
    }

    private static HashSet<long> ReadJournalEvents(string directory)
    {
        string path = Path.Combine(directory, "journal.log");
        if (!File.Exists(path)) throw new FileNotFoundException("journal.log is absent", path);
        HashSet<long> events = new();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (fields.Length < 3) continue;
            string token = fields[2];
            if (token.Length > 1 && token[0] == 's'
                && long.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                events.Add(value);
        }
        return events;
    }

    private static bool RequireEvents(string role, IReadOnlyList<TapeEventID> ids, Dictionary<long, byte[]> payloads, HashSet<long> journal, out string failure)
    {
        for (int index = 0; index < ids.Count; index++)
        {
            long id = ids[index].Value;
            if (id < 0 || !payloads.ContainsKey(id) || !journal.Contains(id))
            {
                failure = $"{role} event {id} is receipt-only (missing tape or journal custody)";
                return false;
            }
        }
        failure = "";
        return true;
    }

    private static bool VerifyJournalBindings(
        string role,
        IReadOnlyList<TapeEventID> ids,
        Dictionary<long, byte[]> payloads,
        Dictionary<long, string> tapeSources,
        Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> journalRows,
        out string failure)
    {
        foreach (TapeEventID id in ids)
        {
            if (id.Value < 0 || !payloads.TryGetValue(id.Value, out byte[]? payload)
                || !tapeSources.TryGetValue(id.Value, out string? tapeSource)
                || !journalRows.TryGetValue(id.Value, out (int Step, string Source, string LineSHA256, int ByteCount, string Line) row)
                || !string.Equals(row.Source, tapeSource, StringComparison.Ordinal)
                || row.ByteCount != payload.Length)
            {
                failure = $"{role} event {id.Value} journal row does not bind tape source and payload length";
                return false;
            }
            string[] lineFields = row.Line.Split('\t');
            if (lineFields.Length < 5 || lineFields[^1] != row.ByteCount.ToString(CultureInfo.InvariantCulture) + "B"
                || TryReadPacketStep(payload, out int packetStep) && row.Step != packetStep
                || !IsCanonicalDigest(row.LineSHA256))
                throw new InvalidDataException($"{role} event {id.Value} journal line digest is malformed");
        }
        failure = "";
        return true;
    }

    private static bool TryReadPacketStep(byte[] payload, out int step)
    {
        step = 0;
        if (TryDecodeRepositoryOccurrenceCheckReceipt(payload, out RepositoryOccurrenceCheckReceipt occurrenceCheck))
        {
            step = occurrenceCheck.Step;
            return true;
        }
        if (!TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out _, out string canonical, out _)
            || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields)
            || fields.Length == 0)
            return false;
        return int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out step);
    }

    private static bool VerifyNativeR4Events(
        string role,
        IReadOnlyList<TapeEventID> ids,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> authority,
        out string failure)
    {
        foreach (TapeEventID id in ids)
        {
            if (!authority.TryGetValue(id.Value, out (string Source, Provenances Provenance, TapeEventRoles Roles) eventAuthority)
                || eventAuthority.Source != "repository:lineage"
                || eventAuthority.Provenance != Provenances.Execution
                || !eventAuthority.Roles.HasFlag(TapeEventRoles.Measurement)
                || !eventAuthority.Roles.HasFlag(TapeEventRoles.AuditOnly))
            {
                failure = $"{role} event {id.Value} lacks repository lineage source/provenance/roles";
                return false;
            }
        }
        failure = "";
        return true;
    }

    private static bool TryParseByteCount(string token, out int count)
    {
        count = 0;
        return token.Length > 1 && token[^1] == 'B'
            && int.TryParse(token.AsSpan(0, token.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out count)
            && count >= 0;
    }

    private static bool VerifyRepositoryCompositionPacket(
        TapeEventID compositionEventID,
        in LoopClosureCompositionEpisode episode,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority,
        Dictionary<long, string> tapeSources,
        Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> journalRows,
        out string failure)
    {
        if (!payloads.TryGetValue(compositionEventID.Value, out byte[]? payload)
            || !journalEvents.Contains(compositionEventID.Value)
            || !TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
            // Frozen journal row kind; identifier-side name is ComposedCandidate.
            || kind != "derived-candidate"
            || !string.Equals(digest, RepositoryLineageReceiptCodec.Digest(kind, canonical), StringComparison.Ordinal)
            || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields)
            || fields.Length != 18
            || fields[1] != RepositoryNavigationRule.CreateSharedIdentifierSearchTerm().ID.Value
            || !long.TryParse(fields[14], NumberStyles.None, CultureInfo.InvariantCulture, out long packetCompositionEvent)
            || packetCompositionEvent != compositionEventID.Value
            || !RepositoryCandidate.TryParseCanonical(fields[5], out RepositoryCandidate candidate)
            || !Enum.TryParse(fields[4], out RepositoryCandidateSpecies packetSpecies)
            || packetSpecies != candidate.Species
            || !string.Equals(fields[6], candidate.Digest.ToString(), StringComparison.Ordinal)
            || !RepositoryLineageReceiptCodec.IsSHA(fields[7])
            || !RepositoryLineageReceiptCodec.IsSHA(fields[8])
            || !RepositoryLineageReceiptCodec.IsSHA(fields[15])
            || !RepositoryLineageReceiptCodec.IsSHA(fields[16]))
        {
            failure = "R4 composition packet is absent or malformed";
            return false;
        }
        if (!VerifyLineagePayload(compositionEventID, LoopLineageNodeSpecies.Rung0Composition, payload, lineage))
        {
            failure = "R4 composition packet payload is not lineage-bound";
            return false;
        }
        LoopLineageEdgeReceipt compositionEdge = lineage.Single(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition
            && edge.Node.EventID == compositionEventID);
        if (compositionEdge.PredecessorIDs.Count != 1)
        {
            failure = "R4 composition lineage predecessor is missing or duplicated";
            return false;
        }
        LoopLineageEdgeReceipt[] occurrenceEdges = lineage.Where(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.VerifiedLawSupport
            && edge.PredecessorIDs.Contains(compositionEdge.PredecessorIDs[0])).ToArray();
        if (occurrenceEdges.Length != 1)
        {
            failure = "R4 composition lineage occurrence edge is missing or duplicated";
            return false;
        }
        LoopLineageEdgeReceipt lawPredecessor = lineage.Single(edge => edge.Node.NodeID == compositionEdge.PredecessorIDs[0]);
        if (!long.TryParse(fields[17], NumberStyles.None, CultureInfo.InvariantCulture, out long packetPredecessor)
            || packetPredecessor != lawPredecessor.Node.EventID.Value)
        {
            failure = "R4 composition packet predecessor is not the lineage predecessor";
            return false;
        }
        string[] occurrenceTokens = fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] episodeTokens = episode.EvidenceEventIDs.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (occurrenceTokens.Length == 0
            || !occurrenceTokens.SequenceEqual(episodeTokens)
            || occurrenceTokens.Any(token => !long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id < 0)
            || !occurrenceTokens.Select(token => long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : -1)
                .SequenceEqual(occurrenceTokens.Select(token => long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : -1).OrderBy(static id => id))
            || occurrenceTokens.Distinct(StringComparer.Ordinal).Count() != occurrenceTokens.Length)
        {
            failure = "R4 composition packet occurrence IDs are not canonical";
            return false;
        }
        foreach (string occurrenceToken in occurrenceTokens)
        {
            long occurrenceEvent = long.Parse(occurrenceToken, CultureInfo.InvariantCulture);
            if (!TryFindRepositoryOccurrenceReceipt(occurrenceEvent, payloads, journalEvents, [occurrenceEdges[0]], tapeAuthority,
                    out RepositoryConfirmedOccurrenceReceipt occurrenceReceipt, out failure)
                || !payloads.TryGetValue(occurrenceEvent, out byte[]? occurrenceCheckPayload)
                || !TryDecodeRepositoryOccurrenceCheckReceipt(occurrenceCheckPayload, out RepositoryOccurrenceCheckReceipt occurrenceCheckReceipt)
                || occurrenceReceipt.PredictionSHA256 != occurrenceCheckReceipt.PredictionSHA256
                || occurrenceReceipt.EvidenceSHA256 != occurrenceCheckReceipt.EvidenceSHA256
                || occurrenceReceipt.OccurrenceSHA256 != occurrenceCheckReceipt.ReceiptSHA256
                || occurrenceReceipt.PredecessorEventID.Value != lawPredecessor.Node.EventID.Value
                || !HasRepositoryOccurrenceCheckAuthority(occurrenceEvent, tapeAuthority)
                || occurrenceCheckReceipt.WorldSHA256 != fields[15]
                || occurrenceCheckReceipt.AccessSHA256 != fields[16]
                || !VerifyRepositoryOccurrenceCheckJournalRow(occurrenceEvent, occurrenceCheckReceipt, journalRows, out failure)
                || !VerifyRepositoryLineageJournalRow(occurrenceEdges[0].Node.EventID.Value, occurrenceReceipt.Step, payloads, tapeSources, journalRows, out failure)
                || !VerifyJournalBindings("R4 composition occurrence", [new TapeEventID(occurrenceEvent)], payloads, tapeSources, journalRows, out failure))
            {
                if (string.IsNullOrEmpty(failure)) failure = "R4 composition packet occurrence is not joined to confirmed-prediction lineage authority";
                return false;
            }
        }
        failure = "";
        return true;
    }

    private static bool TryFindRepositoryOccurrenceReceipt(
        long occurrenceCheckEventID,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority,
        out RepositoryConfirmedOccurrenceReceipt occurrenceReceipt,
        out string failure)
    {
        occurrenceReceipt = default;
        LoopLineageEdgeReceipt[] occurrences = lineage.Where(edge => edge.Node.Species == LoopLineageNodeSpecies.VerifiedLawSupport).ToArray();
        foreach (LoopLineageEdgeReceipt edge in occurrences)
        {
            if (!payloads.TryGetValue(edge.Node.EventID.Value, out byte[]? payload)
                || !journalEvents.Contains(edge.Node.EventID.Value)
                || !HasNativeCustodyAuthority(edge.Node.EventID.Value, tapeAuthority)
                || !VerifyLineagePayload(edge.Node.EventID, LoopLineageNodeSpecies.VerifiedLawSupport, payload, lineage)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
                // Frozen journal row kind; identifier-side name is ConfirmedOccurrence.
                || kind != "verified-support"
                || digest != RepositoryLineageReceiptCodec.Digest(kind, canonical)
                || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields)
                || fields.Length != 5
                || !journalEvents.Contains(occurrenceCheckEventID)
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int step)
                || !RepositoryLineageReceiptCodec.IsSHA(fields[1])
                || !RepositoryLineageReceiptCodec.IsSHA(fields[2])
                || !RepositoryLineageReceiptCodec.IsSHA(fields[3])
                || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out long predecessor)
                || !TryFindRepositoryOccurrenceCheckReceipt(fields[3], occurrenceCheckEventID, payloads, tapeAuthority)) continue;
            RepositoryConfirmedOccurrenceReceipt candidate = new(step, fields[1], fields[2], fields[3], new TapeEventID(predecessor),
                RepositoryLineageReceiptCodec.Digest(kind, canonical));
            try { candidate.Validate(); }
            catch (InvalidDataException) { continue; }
            occurrenceReceipt = candidate;
            failure = "";
            return true;
        }
        failure = "R4 composition packet occurrence receipt is not lineage-bound";
        return false;
    }

    private static bool TryFindRepositoryOccurrenceCheckReceipt(
        string receiptSHA,
        long eventID,
        Dictionary<long, byte[]> payloads,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority)
        => payloads.TryGetValue(eventID, out byte[]? payload)
            && TryReadRepositoryOccurrenceCheckPacket(payload, out _, out _, out string foundReceipt)
            && foundReceipt == receiptSHA
            && HasRepositoryOccurrenceCheckAuthority(eventID, tapeAuthority);

    private static bool TryReadRepositoryOccurrenceCheckPacket(byte[] payload,
        out string worldSHA, out string accessSHA, out string receiptSHA)
    {
        worldSHA = accessSHA = receiptSHA = "";
        string text = Encoding.UTF8.GetString(payload);
        if (!text.StartsWith("REPOSITORY-VERIFICATION\t", StringComparison.Ordinal)) return false;
        foreach (string field in text.Split('\t'))
        {
            if (field.StartsWith("world=", StringComparison.Ordinal)) worldSHA = field["world=".Length..];
            else if (field.StartsWith("access=", StringComparison.Ordinal)) accessSHA = field["access=".Length..];
            else if (field.StartsWith("receipt=", StringComparison.Ordinal)) receiptSHA = field["receipt=".Length..];
        }
        return RepositoryLineageReceiptCodec.IsSHA(worldSHA)
            && RepositoryLineageReceiptCodec.IsSHA(accessSHA)
            && RepositoryLineageReceiptCodec.IsSHA(receiptSHA);
    }

    private static bool VerifyRepositoryOccurrenceCheckJournalRow(
        long eventID,
        in RepositoryOccurrenceCheckReceipt receipt,
        Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> journalRows,
        out string failure)
    {
        if (!journalRows.TryGetValue(eventID, out var row))
        {
            failure = $"repository occurrence check event {eventID} has no journal row";
            return false;
        }
        // Frozen journal source, row kind, and payload field tokens; identifier-side names are OccurrenceCheck and Prediction.
        string expected = string.Join('\t',
            receipt.Step.ToString(CultureInfo.InvariantCulture), "repository-verification", new TapeEventID(eventID).ToString(),
            $"species={receipt.Prediction.Species}", $"outcome={receipt.Outcome}", $"claim={receipt.PredictionSHA256}",
            $"evidence={receipt.EvidenceSHA256}", $"world={receipt.WorldSHA256}", $"access={receipt.AccessSHA256}",
            $"evaluator-cost={receipt.EvaluatorCost}", $"access-cost={receipt.AccessCost}",
            $"predecessor={receipt.PredecessorEventID.Value}", $"call={receipt.CallSHA256}",
            $"receipt={receipt.ReceiptSHA256}", $"{row.ByteCount}B");
        if (row.Step != receipt.Step || !string.Equals(row.Line, expected, StringComparison.Ordinal))
        {
            failure = $"repository occurrence check event {eventID} journal row diverges from typed packet";
            return false;
        }
        failure = "";
        return true;
    }

    private static bool VerifyRepositoryLineageJournalRow(
        long eventID,
        int expectedStep,
        Dictionary<long, byte[]> payloads,
        Dictionary<long, string> tapeSources,
        Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> journalRows,
        out string failure)
    {
        if (!payloads.TryGetValue(eventID, out byte[]? payload)
            || !tapeSources.TryGetValue(eventID, out string? source)
            || !journalRows.TryGetValue(eventID, out var row))
        {
            failure = $"repository lineage event {eventID} has incomplete journal custody";
            return false;
        }
        string expected = $"{expectedStep}\tmint\t{new TapeEventID(eventID)}\t{source}\t{payload.Length}B";
        if (row.Step != expectedStep || !string.Equals(source, "repository:lineage", StringComparison.Ordinal)
            || !string.Equals(row.Line, expected, StringComparison.Ordinal))
        {
            failure = $"repository lineage event {eventID} journal row diverges from typed packet";
            return false;
        }
        failure = "";
        return true;
    }

    private static bool TryDecodeRepositoryOccurrenceCheckReceipt(byte[] payload,
        out RepositoryOccurrenceCheckReceipt receipt)
    {
        receipt = default;
        // Frozen tape source and claim field token; identifier-side names are OccurrenceCheck and Prediction.
        string text = Encoding.UTF8.GetString(payload);
        if (!text.StartsWith("REPOSITORY-VERIFICATION\t", StringComparison.Ordinal)) return false;
        int predictionStart = text.IndexOf("\tclaim=", StringComparison.Ordinal);
        int outcomeStart = text.IndexOf("\toutcome=", predictionStart + 7, StringComparison.Ordinal);
        if (predictionStart < 0 || outcomeStart < 0) return false;
        string predictionText = text[(predictionStart + 7)..outcomeStart];
        if (!RepositoryPrediction.TryParse(predictionText, out RepositoryPrediction prediction)) return false;
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string field in text["REPOSITORY-VERIFICATION\t".Length..predictionStart].Split('\t'))
        {
            int equals = field.IndexOf('=');
            if (equals > 0 && !fields.TryAdd(field[..equals], field[(equals + 1)..])) return false;
        }
        foreach (string field in text[(outcomeStart + 1)..].Split('\t'))
        {
            int equals = field.IndexOf('=');
            if (equals > 0 && !fields.TryAdd(field[..equals], field[(equals + 1)..])) return false;
        }
        if (!int.TryParse(fields.GetValueOrDefault("step"), NumberStyles.None, CultureInfo.InvariantCulture, out int step)
            || !Enum.TryParse(fields.GetValueOrDefault("species"), out RepositoryPredictionSpecies packetSpecies)
            || packetSpecies != prediction.Species
            || !Enum.TryParse(fields.GetValueOrDefault("outcome"), out RepositoryOccurrenceCheckOutcomes outcome)
            || !long.TryParse(fields.GetValueOrDefault("access-sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long accessSequence)
            || !int.TryParse(fields.GetValueOrDefault("access-entry-count"), NumberStyles.None, CultureInfo.InvariantCulture, out int accessEntryCount)
            || !long.TryParse(fields.GetValueOrDefault("evaluator-cost"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long evaluatorCost)
            || !long.TryParse(fields.GetValueOrDefault("access-cost"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long accessCost)
            || !long.TryParse(fields.GetValueOrDefault("predecessor"), NumberStyles.None, CultureInfo.InvariantCulture, out long predecessor)
            || !RepositoryLineageReceiptCodec.IsSHA(fields.GetValueOrDefault("world"))
            || !RepositoryLineageReceiptCodec.IsSHA(fields.GetValueOrDefault("access"))
            || !RepositoryLineageReceiptCodec.IsSHA(fields.GetValueOrDefault("claim-sha256"))
            || !RepositoryLineageReceiptCodec.IsSHA(fields.GetValueOrDefault("evidence"))
            || !RepositoryLineageReceiptCodec.IsSHA(fields.GetValueOrDefault("call"))
            || !RepositoryLineageReceiptCodec.IsSHA(fields.GetValueOrDefault("receipt"))) return false;
        receipt = new RepositoryOccurrenceCheckReceipt(step, prediction, outcome,
            fields["world"], fields["access"], fields["claim-sha256"], fields["evidence"], evaluatorCost, accessCost,
            new TapeEventID(predecessor), fields["call"], fields["receipt"])
        {
            AccessSequence = accessSequence,
            AccessEntrySHA256 = fields.GetValueOrDefault("access-entry-sha256") ?? "",
            AccessEntryCount = accessEntryCount,
        };
        try { receipt.Validate(); return true; }
        catch (InvalidDataException) { receipt = default; return false; }
    }

    private static bool HasRepositoryOccurrenceCheckAuthority(long eventID,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority)
        => tapeAuthority.TryGetValue(eventID, out (string Source, Provenances Provenance, TapeEventRoles Roles) authority)
            && authority.Source == "repository:verification"
            && authority.Provenance == Provenances.Execution
            && authority.Roles.HasFlag(TapeEventRoles.Measurement)
            && authority.Roles.HasFlag(TapeEventRoles.AuditOnly);

    private static bool HasNativeCustodyAuthority(long eventID,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority)
        => tapeAuthority.TryGetValue(eventID, out (string Source, Provenances Provenance, TapeEventRoles Roles) authority)
            && authority.Source == "repository:lineage"
            && authority.Provenance == Provenances.Execution
            && authority.Roles.HasFlag(TapeEventRoles.Measurement)
            && authority.Roles.HasFlag(TapeEventRoles.AuditOnly);

    private static bool VerifyTeacherPacketAuthority(
        TapeEventID eventID,
        Dictionary<long, (string Source, Provenances Provenance, TapeEventRoles Roles)> tapeAuthority,
        out string failure)
    {
        if (!tapeAuthority.TryGetValue(eventID.Value, out (string Source, Provenances Provenance, TapeEventRoles Roles) authority)
            || !authority.Source.StartsWith("policy-teacher:", StringComparison.Ordinal)
            || authority.Provenance != Provenances.Execution
            || !authority.Roles.HasFlag(TapeEventRoles.Measurement)
            || !authority.Roles.HasFlag(TapeEventRoles.AuditOnly))
        {
            failure = $"R4 teacher packet event {eventID.Value} lacks teacher custody source/provenance/roles";
            return false;
        }
        failure = "";
        return true;
    }

    private static bool VerifyPatternPackets(
        PatternBecameThoughtCorroboration pattern,
        in LoopClosureR4Provenance r4,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        out string failure)
        => VerifyPatternPacket(pattern, r4.Episode.CompositionEventID, lineage, payloads, journalEvents, out failure);

    private static bool VerifyFoldInstallRevision(
        in GrammarFoldProvenanceReceipt expected,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        out string failure)
    {
        Dictionary<long, string> sources = new();
        Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> rows = new();
        return VerifyFoldInstallRevision(in expected, payloads, journalEvents, sources, rows, out failure, requireRow: false);
    }

    private static bool VerifyFoldInstallRevision(
        in GrammarFoldProvenanceReceipt expected,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        Dictionary<long, string> tapeSources,
        Dictionary<long, (int Step, string Source, string LineSHA256, int ByteCount, string Line)> journalRows,
        out string failure,
        bool requireRow = true)
    {
        long match = -1;
        foreach ((long id, byte[] payload) in payloads)
        {
            if (!TapePacketCreator.TryDecodeGrammarFoldInstallRevision(payload, out GrammarFoldProvenanceReceipt actual)) continue;
            if (actual.PreviousRevision != expected.PreviousRevision
                || actual.Revision != expected.Revision
                || actual.ConsumedEventDigest != expected.ConsumedEventDigest
                || actual.ReceiptDigest != expected.ReceiptDigest
                || !actual.ConsumedEventIDs.SequenceEqual(expected.ConsumedEventIDs)
                || !actual.CompositionEpisodeDigests.SequenceEqual(expected.CompositionEpisodeDigests)) continue;
            if (match >= 0)
            {
                failure = "R4 grammar fold publication is duplicated";
                return false;
            }
            match = id;
        }
        if (match >= 0)
        {
            if (!journalEvents.Contains(match))
            {
                failure = $"grammar fold publication packet {match} is absent from journal.log";
                return false;
            }
            if (requireRow)
            {
                if (!tapeSources.TryGetValue(match, out string? source) || source != "grammar:fold")
                {
                    failure = $"grammar fold publication packet {match} is not a grammar:fold event";
                    return false;
                }
                if (!VerifyJournalBindings("R4 fold", [new TapeEventID(match)], payloads, tapeSources, journalRows, out failure))
                    return false;
            }
        }
        if (match >= 0) { failure = ""; return true; }
        failure = "R4 fold provenance is not bound to an ordinary grammar publication packet";
        return false;
    }

    private static bool VerifyPatternPacket(
        PatternBecameThoughtCorroboration pattern,
        TapeEventID compositionEventID,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        out string failure)
    {
        if (!payloads.TryGetValue(compositionEventID.Value, out byte[]? compositionPacket)
            || !VerifyLineagePayload(compositionEventID, LoopLineageNodeSpecies.Rung0Composition, compositionPacket, lineage)
            || !TapePacketCreator.TryReadEmlRung0Closure(compositionPacket, out TapePacketCreator.EmlRung0ClosurePacket packet)
            || packet.Kind != "RUNG0-DERIVATION"
            || packet.Species != EmlRung0AdmissionPath.Species
            || packet.Status != "Accepted"
            || packet.SourcePredictionID != pattern.SourcePredictionID
            || packet.ComposedPredictionID != pattern.ComposedPredictionID
            || packet.ComposedPredictionID == packet.SourcePredictionID
            || packet.TargetSpecies != pattern.TargetSpecies
            || !packet.SupportEventIDs.SequenceEqual(pattern.SupportEventIDs)
            || !packet.LawAdmissionIDs.SequenceEqual(pattern.BasisLawAdmissionIDs)
            || !VerifyPatternLineageCustody(pattern, compositionEventID, lineage, payloads, journalEvents)
            || (packet.TargetSpecies == EmlObligationTargetSpecies.ExactComposition && packet.SupportEventIDs.Count == 0)
            || packet.SupportEventIDs.Distinct().Count() != packet.SupportEventIDs.Count
            || packet.SupportEventIDs.Any(eventID => !payloads.ContainsKey(eventID.Value))
            || !string.Equals(packet.OccurrenceDigest,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Join(',', packet.SupportEventIDs.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture)))))),
                StringComparison.Ordinal)
            || packet.TargetSpecies == EmlObligationTargetSpecies.ExactComposition
                && (packet.LawAdmissionIDs.Count == 0 || !packet.LawAdmissionIDs.SequenceEqual(packet.LawAdmissionIDs.Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal))
                    || packet.LawAdmissionIDs.Any(admissionID => !IsCanonicalLawAdmissionID(admissionID)))
            || !string.Equals(packet.ProofSHA256, pattern.ProofSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(packet.AuditSHA256, pattern.AuditSHA256.Value, StringComparison.Ordinal)
            || packet.WorldContacts != 0
            || packet.MainEvaluation.Calls != pattern.MainEvaluatorDelta
            || packet.ComparatorEvaluation.Calls != pattern.NumericEvaluatorDelta
            || pattern.MainEvaluatorDelta != 0
            || pattern.NumericEvaluatorDelta <= 0
            || packet.MainEvaluation.Calls != 0
            || packet.ComparatorEvaluation.Calls <= 0
            || packet.AdmissionPath.ObligationPredictionID != pattern.SourcePredictionID
            || string.IsNullOrEmpty(packet.ObligationID)
            || !string.Equals(packet.LhsRPN, packet.AdmissionPath.LhsRPN, StringComparison.Ordinal)
            || !string.Equals(packet.RhsRPN, packet.AdmissionPath.RhsRPN, StringComparison.Ordinal)
            || !packet.AdmissionPath.IsBound
            || !string.Equals(packet.CandidateDigest,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(packet.LhsRPN + "|" + packet.RhsRPN + "|guard=" + packet.AdmissionPath.GuardPackageDigest))),
                StringComparison.Ordinal)
            || !IsCanonicalDigest(packet.ProofSHA256)
            || !IsCanonicalDigest(packet.AuditSHA256)
            || !IsProofID(packet.ProofID)
            || !string.Equals(packet.AuditID, packet.ProofID + ":audit", StringComparison.Ordinal)
            || !string.Equals(packet.AdmissionID,
                packet.ProofID + ":admission:" + packet.ComposedPredictionID.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !string.Equals(packet.ClosureID,
                // Frozen digest species token; identifier-side name is Rung0ComposedForm.
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(packet.ObligationID + "|attachment|Rung0DerivedForm|" + packet.CandidateDigest))),
                StringComparison.Ordinal))
        {
            failure = "pattern corroboration does not match the exact ordinary rung-0 admission packet";
            return false;
        }
        failure = "";
        return true;
    }

    private static bool VerifyPatternLineageCustody(
        PatternBecameThoughtCorroboration pattern,
        TapeEventID compositionEventID,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents)
    {
        if (pattern.TargetSpecies != EmlObligationTargetSpecies.ExactComposition) return true;
        LoopLineageEdgeReceipt? composition = lineage.FirstOrDefault(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition
            && edge.Node.EventID == compositionEventID);
        if (composition is null) return false;
        Dictionary<LoopLineageNodeID, LoopLineageEdgeReceipt> byNode = lineage
            .ToDictionary(static edge => edge.Node.NodeID);
        List<string> lawAdmissionIDs = new(composition.PredecessorIDs.Count);
        List<TapeEventID> supportEventIDs = new();
        foreach (LoopLineageNodeID predecessorID in composition.PredecessorIDs)
        {
            if (!byNode.TryGetValue(predecessorID, out LoopLineageEdgeReceipt? lawEdge)
                || lawEdge.Node.Species != LoopLineageNodeSpecies.VerifiedLaw
                || !payloads.TryGetValue(lawEdge.Node.EventID.Value, out byte[]? lawPayload)
                || !VerifyLineagePayload(lawEdge.Node.EventID, LoopLineageNodeSpecies.VerifiedLaw, lawPayload, lineage)
                || !journalEvents.Contains(lawEdge.Node.EventID.Value)
                || !TapePacketCreator.TryReadEmlLawAdmissionID(lawPayload, out string admissionID)) return false;
            lawAdmissionIDs.Add(admissionID);
            foreach (LoopLineageNodeID rootID in lawEdge.PredecessorIDs)
            {
                if (!byNode.TryGetValue(rootID, out LoopLineageEdgeReceipt? rootEdge)
                    || rootEdge.Node.Species != LoopLineageNodeSpecies.AdmissionPlan
                    || !payloads.ContainsKey(rootEdge.Node.EventID.Value)
                    || !VerifyLineagePayload(rootEdge.Node.EventID, LoopLineageNodeSpecies.AdmissionPlan, payloads[rootEdge.Node.EventID.Value], lineage)
                    || !journalEvents.Contains(rootEdge.Node.EventID.Value)) return false;
                supportEventIDs.Add(rootEdge.Node.EventID);
            }
        }
        string[] canonicalLawAdmissionIDs = lawAdmissionIDs.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        TapeEventID[] canonicalSupportEventIDs = supportEventIDs
            .Distinct()
            .OrderBy(static id => id.Value)
            .ToArray();
        return lawAdmissionIDs.Distinct(StringComparer.Ordinal).Count() == lawAdmissionIDs.Count
            && lawAdmissionIDs.SequenceEqual(canonicalLawAdmissionIDs)
            && lawAdmissionIDs.SequenceEqual(pattern.BasisLawAdmissionIDs)
            && supportEventIDs.Distinct().Count() == supportEventIDs.Count
            && pattern.SupportEventIDs.Distinct().Count() == pattern.SupportEventIDs.Length
            && canonicalSupportEventIDs.SequenceEqual(pattern.SupportEventIDs.OrderBy(static id => id.Value));
    }

    private static bool VerifyLineagePayload(
        TapeEventID eventID,
        LoopLineageNodeSpecies species,
        byte[] payload,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage)
    {
        LoopLineageEdgeReceipt[] matches = lineage.Where(edge => edge.Node.EventID == eventID && edge.Node.Species == species).ToArray();
        return matches.Length == 1
            && string.Equals(matches[0].Node.PayloadSHA256,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload)), StringComparison.Ordinal);
    }

    private static bool VerifyLawSupportPackets(
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        IReadOnlyDictionary<long, (string Source, Provenances Provenance)> sources,
        out string failure)
    {
        Dictionary<LoopLineageNodeID, LoopLineageEdgeReceipt> byNode = lineage
            .ToDictionary(static edge => edge.Node.NodeID);
        HashSet<string> authorities = new(StringComparer.Ordinal);
        foreach (LoopLineageEdgeReceipt edge in lineage)
        {
            if (edge.Node.Species != LoopLineageNodeSpecies.VerifiedLaw) continue;
            if (!payloads.TryGetValue(edge.Node.EventID.Value, out byte[]? payload)
                || !journalEvents.Contains(edge.Node.EventID.Value)
                || !sources.TryGetValue(edge.Node.EventID.Value, out (string Source, Provenances Provenance) authoritySource)
                || !string.Equals(authoritySource.Source, "eml:law", StringComparison.Ordinal)
                || authoritySource.Provenance != Provenances.Reflected
                || !TapePacketCreator.TryReadEmlLawAdmissionID(payload, out string admissionID))
            {
                failure = "verified-law support authority has no canonical law admission";
                return false;
            }
            authorities.Add(admissionID);
        }
        foreach (LoopLineageEdgeReceipt edge in lineage)
        {
            if (edge.Node.Species != LoopLineageNodeSpecies.VerifiedLawSupport) continue;
            if (!payloads.TryGetValue(edge.Node.EventID.Value, out byte[]? payload)
                || !journalEvents.Contains(edge.Node.EventID.Value)
                || !sources.TryGetValue(edge.Node.EventID.Value, out (string Source, Provenances Provenance) source)
                || !string.Equals(source.Source, "eml:law-support", StringComparison.Ordinal)
                || source.Provenance != Provenances.Reflected
                || !TapePacketCreator.TryReadEmlLawSupport(payload, out TapePacketCreator.EmlLawSupportPacket occurrence))
            {
                failure = "verified-law support edge has no strict LAW-SUPPORT packet";
                return false;
            }
            if (!authorities.Contains(occurrence.CanonicalAuthorityID))
            {
                failure = "verified-law support packet does not join candidate and canonical law admission";
                return false;
            }
            List<TapeEventID> roots = new();
            string? canonicalAuthority = null;
            foreach (LoopLineageNodeID predecessorID in edge.PredecessorIDs)
            {
                if (!byNode.TryGetValue(predecessorID, out LoopLineageEdgeReceipt? predecessor)
                    || !payloads.ContainsKey(predecessor.Node.EventID.Value)
                    || !journalEvents.Contains(predecessor.Node.EventID.Value))
                {
                    failure = "verified-law support edge names an uncommitted predecessor";
                    return false;
                }
                if (predecessor.Node.Species == LoopLineageNodeSpecies.AdmissionPlan)
                {
                    if (!sources.TryGetValue(predecessor.Node.EventID.Value, out (string Source, Provenances Provenance) worldSource)
                        || !string.Equals(worldSource.Source, "corpus", StringComparison.Ordinal)
                        || worldSource.Provenance != Provenances.Real
                        || !TryReadWorldReceipt(predecessor.Node.EventID, payloads, journalEvents, sources))
                    {
                        failure = "verified-law support world predecessor is not a corpus event";
                        return false;
                    }
                    roots.Add(predecessor.Node.EventID);
                }
                else if (predecessor.Node.Species == LoopLineageNodeSpecies.VerifiedLaw
                    && sources.TryGetValue(predecessor.Node.EventID.Value, out (string Source, Provenances Provenance) lawSource)
                    && string.Equals(lawSource.Source, "eml:law", StringComparison.Ordinal)
                    && lawSource.Provenance == Provenances.Reflected
                    && TapePacketCreator.TryReadEmlLawAdmissionID(payloads[predecessor.Node.EventID.Value], out string authorityID))
                    canonicalAuthority = authorityID;
                else
                {
                    failure = "verified-law support edge names a non-world/non-law predecessor";
                    return false;
                }
            }
            TapeEventID[] expectedWorld = roots.Distinct().OrderBy(static id => id.Value).ToArray();
            if (occurrence.SourcePredictionMintEvents.Any(mintEvent => mintEvent is TapeEventID eventID
                    && (occurrence.WorldOpportunityEventIDs.Any(worldEvent => worldEvent.Value >= eventID.Value)
                        || eventID.Value > edge.Node.EventID.Value)))
            {
                failure = "verified-law support custody has invalid world-to-mint-to-support ordering";
                return false;
            }
            if (canonicalAuthority is null
                || !string.Equals(canonicalAuthority, occurrence.CanonicalAuthorityID, StringComparison.Ordinal)
                || !expectedWorld.SequenceEqual(occurrence.WorldOpportunityEventIDs)
                || occurrence.WorldOpportunityEventIDs.Count == 0
                || occurrence.SourcePredictionMintEvents.Any(mintEvent => mintEvent is TapeEventID eventID
                    && (!payloads.ContainsKey(eventID.Value) || !journalEvents.Contains(eventID.Value)
                        || !sources.TryGetValue(eventID.Value, out (string Source, Provenances Provenance) mintSource)
                        || mintSource.Provenance != Provenances.Reflected
                        || (mintSource.Source is not ("eml" or "node0" or "eml:law-execution"))))
                || occurrence.SourcePredictionOpportunityEvents.SelectMany(static events => events)
                    .Distinct().OrderBy(static id => id.Value).ToArray()
                    .SequenceEqual(occurrence.WorldOpportunityEventIDs) is false)
            {
                failure = "verified-law support packet world ancestry disagrees with its lineage edge";
                return false;
            }
            for (int predictionIndex = 0; predictionIndex < occurrence.SourcePredictionIDs.Count; predictionIndex++)
            {
                if (occurrence.SourcePredictionMintEvents[predictionIndex] is not TapeEventID mintEvent
                    || !payloads.TryGetValue(mintEvent.Value, out byte[]? mintPayload)
                    || !sources.TryGetValue(mintEvent.Value, out (string Source, Provenances Provenance) mintSource))
                {
                    failure = "verified-law support mint payload is not an exact EML claim";
                    return false;
                }
                if (mintSource.Provenance != Provenances.Reflected)
                {
                    failure = "verified-law support mint provenance is not reflected";
                    return false;
                }
                if (mintSource.Source is "eml" or "node0")
                {
                    if (!EmlPrediction.TryParse(Encoding.ASCII.GetString(mintPayload), out _))
                    {
                        failure = "ordinary EML mint payload is not a claim";
                        return false;
                    }
                    string resolvedLineDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(mintPayload));
                    if (!string.Equals(resolvedLineDigest, occurrence.SourcePredictionMintLineDigests[predictionIndex], StringComparison.Ordinal))
                    {
                        failure = "verified-law support mint line digest disagrees with ordinary payload";
                        return false;
                    }
                }
                else if (mintSource.Source == "eml:law-execution")
                {
                    if (!TapePacketCreator.TryReadEmlLawExecutionSupports(mintPayload,
                            out TapePacketCreator.EmlLawExecutionSupportPacket execution)
                        || !execution.PredictionIDs.Contains(occurrence.SourcePredictionIDs[predictionIndex])
                        || !execution.Digests.Contains(occurrence.Digest, StringComparer.Ordinal)
                        || !execution.Ranges.Any(range => string.Equals(range.Digest, occurrence.Digest, StringComparison.Ordinal)
                            && range.Start <= occurrence.SourcePredictionIDs[predictionIndex]
                            && range.Start + range.Count > occurrence.SourcePredictionIDs[predictionIndex]))
                    {
                        failure = "law-frontier mint payload does not own the support claim range";
                        return false;
                    }
                }
                else
                {
                    failure = "verified-law support mint source is unknown";
                    return false;
                }
            }
        }
        failure = "";
        return true;
    }

    private static bool TryReadWorldReceipt(
        TapeEventID corpusEventID,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        IReadOnlyDictionary<long, (string Source, Provenances Provenance)> sources)
    {
        foreach ((long eventID, (string Source, Provenances Provenance) source) in sources)
        {
            if (!string.Equals(source.Source, "world:encounter", StringComparison.Ordinal)
                || source.Provenance != Provenances.Execution
                || eventID + 1 != corpusEventID.Value
                || !journalEvents.Contains(eventID)
                || !payloads.TryGetValue(eventID, out byte[]? payload)
                || !TapePacketCreator.TryReadWorldEncounterObservation(payload, out TapeEventID observed)) continue;
            if (observed == corpusEventID) return true;
        }
        return false;
    }

    private static bool IsCanonicalDigest(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit) && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static bool IsProofID(string value)
        => value.Length == 16 && value.All(Uri.IsHexDigit);

    private static bool IsCanonicalLawAdmissionID(string value)
    {
        string[] parts = value.Split('\u0001');
        return parts.Length == 3 && parts.All(static part => part.Length > 0)
            && parts[1].Length == 16 && parts[1].All(Uri.IsHexDigit);
    }

    private static bool VerifyTeacherPacket(
        in LoopClosureTeacherPacketProvenance expected,
        IReadOnlyList<TapeEventID> consumedEventIDs,
        Dictionary<long, byte[]> payloads,
        out long packetEventID,
        out string failure)
    {
        foreach (long id in payloads.Keys.OrderBy(static id => id))
            if (consumedEventIDs.Contains(new TapeEventID(id)))
            {
                if (VerifyTeacherPacket(in expected, consumedEventIDs, new TapeEventID(id), payloads, out packetEventID, out failure)) return true;
            }
        packetEventID = -1;
        failure = "R4 teacher provenance is absent from the ordinary tape";
        return false;
    }

    private static bool VerifyTeacherPacket(
        in LoopClosureTeacherPacketProvenance expected,
        IReadOnlyList<TapeEventID> consumedEventIDs,
        TapeEventID expectedPacketEventID,
        Dictionary<long, byte[]> payloads,
        out long packetEventID,
        out string failure)
    {
        packetEventID = -1;
        int matches = 0;
        foreach ((long id, byte[] payload) in payloads)
        {
            if (id != expectedPacketEventID.Value || !consumedEventIDs.Contains(new TapeEventID(id))) continue;
            if (!Encoding.ASCII.GetString(payload).Contains("\tFOLD-REVISION=", StringComparison.Ordinal)) continue;
            try
            {
                LoopClosureTeacherPacketProvenance actual = LoopClosureTeacherPacketProvenance.DecodePacketFields(payload);
                if (actual.EpisodeID == expected.EpisodeID
                    && actual.FoldRevision == expected.FoldRevision
                    && actual.EvidenceDigest == expected.EvidenceDigest
                    && actual.CorroborationDigest == expected.CorroborationDigest
                    && actual.ProvenanceDigest == expected.ProvenanceDigest
                    && actual.MatchedEventIDs.SequenceEqual(expected.MatchedEventIDs))
                {
                    packetEventID = id; matches++;
                }
            }
            catch (InvalidDataException) { }
        }
        if (matches == 1) { failure = ""; return true; }
        failure = matches == 0 ? "R4 teacher provenance is absent from the expected ordinary tape event" : "R4 teacher provenance is duplicated";
        return false;
    }

    private static bool VerifyReadoutLineage(
        Tape tape,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        in LoopClosureR4Provenance r4,
        in PolicyBoundaryDivergenceProof proof,
        Dictionary<long, byte[]> payloads,
        HashSet<long> journalEvents,
        out string failure)
    {
        PolicyBoundaryDivergenceProof selectedProof = proof;
        LoopLineageNodeID episodeNodeID = new(r4.Episode.EpisodeID.Value);
        GrammarRevisionID learnedRevision = r4.LearnedReadoutRevision;
        ulong expectedSupport = r4.ReadoutOccurrenceDigest;
        if (!r4.Training.DecisionID.Equals(selectedProof.DecisionID)
            || r4.Training.SelectedCandidateFingerprint != selectedProof.ReadoutCandidateFingerprint
            || r4.Training.SelectedCandidateOccurrenceDigest != selectedProof.ReadoutOccurrenceDigest
            || r4.Training.SelectedCandidateRevision != selectedProof.ReadoutRevision)
        {
            failure = "R4 training corroboration does not bind the selected divergence readout";
            return false;
        }
        if (expectedSupport == 0)
        {
            failure = "R4 provenance omits the learned readout support digest";
            return false;
        }
        LoopLineageEdgeReceipt? composition = lineage.FirstOrDefault(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition
            && edge.Node.NodeID == episodeNodeID);
        if (composition is null)
        {
            failure = "R4 composition node is absent from the sealed lineage";
            return false;
        }
        LoopLineageEdgeReceipt? displaced = lineage.FirstOrDefault(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.DisplacedEvaluation
            && edge.PredecessorIDs.Count == 1 && edge.PredecessorIDs[0] == composition.Node.NodeID);
        if (displaced is null)
        {
            failure = "R4 episode has no exact displaced-evaluation predecessor";
            return false;
        }
        foreach (LoopLineageEdgeReceipt readout in lineage.Where(edge =>
                     edge.Node.Species == LoopLineageNodeSpecies.LearnedReadout
                     && edge.PredecessorIDs.Count == 1 && edge.PredecessorIDs[0] == displaced.Node.NodeID))
        {
            if (!tape.Resolve(readout.Node.EventID, out byte[] payload)) continue;
            CortexPolicyDecisionPacket packet;
            try { packet = TapePacketCreator.DecodePolicyDecision(payload); }
            catch (InvalidDataException) { continue; }
            TapeEventView? view = tape.GetEventViews().FirstOrDefault(candidate => candidate.Id == readout.Node.EventID);
            if (view is null || !view.Value.Source.StartsWith("policy:", StringComparison.Ordinal)) continue;
            if (!packet.DecisionID.Equals(proof.DecisionID)
                || !new CortexPolicyID(view.Value.Source["policy:".Length..]).Equals(proof.Policy)
                || packet.Readout.GrammarRevision != learnedRevision
                || readout.Node.GrammarRevision != learnedRevision
                || readout.Node.EventID.Value != r4.Training.DecisionEventID.Value
                || packet.Readout.ReadoutCandidateFingerprint != r4.Training.SelectedCandidateFingerprint
                || packet.Readout.ReadoutCandidateOccurrenceDigest != r4.Training.SelectedCandidateOccurrenceDigest
                || GrammarPolicyReadout.ComputeFingerprint(packet.Readout.GrammarRevision, proof.Policy) != proof.ReadoutFingerprint
                || readout.Node.CausalID != displaced.Node.CausalID)
                continue;
            ulong actualSupport = packet.Readout.ReadoutCandidateOccurrenceDigest;
            if (actualSupport == 0)
            {
                failure = "R4 policy decision packet omits the learned readout support digest";
                return false;
            }
            if (actualSupport != expectedSupport)
            {
                failure = "R4 readout support digest differs from the sealed policy readout";
                return false;
            }
            bool fundingPacketDecoded = false;
            LoopLineageEdgeReceipt? funding = null;
            CortexPolicyTrialQuotaDecision fundingPacket = default;
            bool fundingHasSeedCustody = false;
            bool fundingHasReadoutFingerprint = false;
            int reusedFundingPacketCount = 0;
            bool fundingRailMalformed = false;
            foreach (LoopLineageEdgeReceipt candidateFunding in lineage.Where(edge =>
                         edge.Node.Species == LoopLineageNodeSpecies.Quota
                         && edge.PredecessorIDs.Count == 1
                         && edge.PredecessorIDs[0] == readout.Node.NodeID
                         && edge.Node.GrammarRevision == selectedProof.ReadoutRevision
                         && edge.Node.CausalID == readout.Node.CausalID))
            {
                if (!tape.Resolve(candidateFunding.Node.EventID, out _)
                    || !payloads.TryGetValue(candidateFunding.Node.EventID.Value, out byte[]? candidatePayload)
                    || !journalEvents.Contains(candidateFunding.Node.EventID.Value)
                    || !TapePacketCreator.TryDecodePolicyTrialQuota(candidatePayload, out CortexPolicyTrialQuotaDecision candidatePacket,
                        out bool candidateHasSeedCustody, out bool candidateHasReadoutFingerprint))
                {
                    fundingRailMalformed = true;
                    continue;
                }
                if (candidatePacket.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
                {
                    fundingRailMalformed = true;
                    continue;
                }
                if (candidatePacket.Decision == CortexPolicyQuotaDecisions.Reused)
                    reusedFundingPacketCount++;
                // A replay receipt is the authenticated tape fact when both the original Paid
                // packet and its zero-charge Reused repair are present.  Never let lineage order
                // silently select the stale parent packet.
                if (!fundingPacketDecoded || candidatePacket.Decision == CortexPolicyQuotaDecisions.Reused)
                {
                    funding = candidateFunding;
                    fundingPacket = candidatePacket;
                    fundingHasSeedCustody = candidateHasSeedCustody;
                    fundingHasReadoutFingerprint = candidateHasReadoutFingerprint;
                    fundingPacketDecoded = true;
                }
            }
            if (fundingRailMalformed)
            {
                failure = "R4 funding rail contains an unreadable or non-funding packet";
                return false;
            }
            if (reusedFundingPacketCount > 1)
            {
                failure = "R4 funding rail contains duplicate Reused packets";
                return false;
            }
            if (!fundingPacketDecoded
                || !fundingPacket.QuotaDecisionID.Equals(selectedProof.Funding.QuotaDecisionID)
                || !fundingPacket.Policy.Equals(selectedProof.Policy)
                || fundingPacket.CandidateFingerprint != selectedProof.ReadoutCandidateFingerprint
                || fundingPacket.ReadoutFingerprint != selectedProof.ReadoutFingerprint
                || !fundingHasReadoutFingerprint
                || fundingPacket.CandidateRevision != selectedProof.ReadoutRevision
                || fundingPacket.QuotaStep != selectedProof.Funding.QuotaStep
                || fundingPacket.RequestedHorizonSteps != selectedProof.Funding.RequestedHorizonSteps
                || fundingPacket.ArmCount != selectedProof.Funding.ArmCount
                || fundingPacket.PlannedArmSteps != selectedProof.Funding.PlannedArmSteps
                || fundingPacket.HeldArmSteps != selectedProof.Funding.HeldArmSteps
                || fundingPacket.Decision != selectedProof.Funding.Decision
                || fundingPacket.UsedSteps != selectedProof.Funding.UsedSteps
                || fundingPacket.RemainingQuota != selectedProof.Funding.RemainingQuota
                || fundingPacket.AllocationIdentity != selectedProof.Funding.AllocationIdentity
                || fundingPacket.AllocationDigest != selectedProof.Funding.AllocationDigest
                || fundingPacket.AllocationArmSteps != selectedProof.Funding.AllocationArmSteps
                || selectedProof.Funding.Policy.Equals(Homeostat.PolicyID)
                    && selectedProof.Funding.Decision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                    && !fundingHasSeedCustody
                || fundingPacket.SeedAuditOnlyDigest != selectedProof.Funding.SeedAuditOnlyDigest)
            {
                failure = "R4 funding edge does not bind the exact decision, fingerprint, revision, readout rail, and seed custody";
                return false;
            }
            failure = "";
            return true;
        }
        failure = "R4 learned-readout edge does not descend from the selected episode or decision";
        return false;
    }

    private static bool FindBoundaryPacket(in PolicyBoundaryDivergenceProof proof, Dictionary<long, byte[]> payloads, out long eventID)
    {
        PolicyBoundaryForkReceipt forkReceipt = proof.ForkReceipt;
        string expectedDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in forkReceipt);
        foreach ((long id, byte[] payload) in payloads)
        {
            if (!TryReadField(payload, "POLICY-BOUNDARY", "id", out string obligation)
                || !string.Equals(obligation, proof.ForkReceipt.Obligation.Value, StringComparison.Ordinal)) continue;
            if (!TryReadField(payload, "POLICY-BOUNDARY", "policy", out string policy)
                || !string.Equals(policy, proof.Policy.Value, StringComparison.Ordinal)) continue;
            if (!TryReadField(payload, "POLICY-BOUNDARY", "source-fingerprint", out string fingerprint)
                || !ulong.TryParse(fingerprint, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedFingerprint)
                || parsedFingerprint != proof.ReadoutFingerprint) continue;
            if (!TryReadField(payload, "POLICY-BOUNDARY", "source-revision", out string revision)
                || !ulong.TryParse(revision, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedRevision)
                || parsedRevision != proof.ReadoutRevision.Value) continue;
            if (!TryReadField(payload, "POLICY-BOUNDARY", "digest", out string digest)
                || !string.Equals(digest, expectedDigest, StringComparison.Ordinal)) continue;
            eventID = id;
            return true;
        }
        eventID = -1;
        return false;
    }

    private static bool VerifyOutcomeLineage(
        in ObjectLoopClosedCorroboration outcome,
        byte[] outcomePacket,
        Dictionary<long, byte[]> payloads,
        IReadOnlyList<LoopLineageEdgeReceipt> lineage,
        in PolicyBoundaryDivergenceProof proof,
        out string failure)
    {
        PolicyBoundaryDivergenceProof selectedProof = proof;
        if (!TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "adjudication", out string adjudication)
            || !string.Equals(adjudication, outcome.DivergenceEvidenceSHA256.Value, StringComparison.Ordinal))
        {
            failure = "object outcome is not the post-paid typed adjudication packet";
            return false;
        }
        PolicyBoundaryDivergenceCandidateTerminal candidate = selectedProof.Candidate;
        PolicyBoundaryDivergenceArmOutcome? candidateExecution = candidate.ExecutedOutcome;
        PolicyBoundaryDivergenceArmOutcome forcedNull = selectedProof.ForcedNull;
        if (!TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate", out string candidateOutcome)
            || !string.Equals(candidateOutcome, candidateExecution?.OutcomeID.Value ?? "none", StringComparison.Ordinal)
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-action", out string candidateAction)
            || !int.TryParse(candidateAction, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCandidateAction)
            || parsedCandidateAction != (candidateExecution?.Action ?? -1)
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-launchpad", out string candidateLaunchpad)
            || !int.TryParse(candidateLaunchpad, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCandidateLaunchpad)
            || parsedCandidateLaunchpad != (candidateExecution?.LaunchpadAction ?? -1)
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-raw", out string candidateRaw)
            || !int.TryParse(candidateRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCandidateRaw)
            || parsedCandidateRaw != (candidateExecution?.RawCandidateAction ?? -1)
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-executed", out string candidateExecuted)
            || candidateExecuted != (candidateExecution?.BehaviorallyExecuted == true ? "1" : "0")
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-outcome", out string candidateTerminalOutcome)
            || !Enum.TryParse(candidateTerminalOutcome, out CortexPolicyTrialExecutionOutcomes parsedCandidateTerminalOutcome)
            || parsedCandidateTerminalOutcome != candidate.Outcome
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-requested", out string candidateRequested)
            || !long.TryParse(candidateRequested, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedCandidateRequested)
            || parsedCandidateRequested != candidate.RequestCount
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "candidate-admitted", out string candidateAdmitted)
            || !long.TryParse(candidateAdmitted, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedCandidateAdmitted)
            || parsedCandidateAdmitted != candidate.GuardAdmittedCount
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-null", out string forcedOutcome)
            || !string.Equals(forcedOutcome, forcedNull.OutcomeID.Value, StringComparison.Ordinal)
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-decision", out string forcedDecision)
            || !TryParseTypedU64(forcedDecision, out ulong parsedForcedDecision)
            || parsedForcedDecision != forcedNull.DecisionID.Value
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-action", out string forcedAction)
            || !int.TryParse(forcedAction, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedForcedAction)
            || parsedForcedAction != forcedNull.Action
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-launchpad", out string forcedLaunchpad)
            || !int.TryParse(forcedLaunchpad, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedForcedLaunchpad)
            || parsedForcedLaunchpad != forcedNull.LaunchpadAction
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-raw", out string forcedRaw)
            || !int.TryParse(forcedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedForcedRaw)
            || parsedForcedRaw != forcedNull.RawCandidateAction
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-cause", out string forcedCause)
            || !Enum.TryParse(forcedCause, out CortexPolicySelectionCauses parsedForcedCause)
            || parsedForcedCause != CortexPolicySelectionCauses.TrialOverride
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-outcome-event", out string forcedOutcomeEvent)
            || !long.TryParse(forcedOutcomeEvent, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedForcedOutcomeEvent)
            || parsedForcedOutcomeEvent != forcedNull.ExecutedOutcomeEventID.Value
            || !TryReadField(outcomePacket, "POLICY-BOUNDARY-OUTCOME", "forced-outcome-payload", out string forcedOutcomePayload)
            || !string.Equals(forcedOutcomePayload, forcedNull.ExecutedOutcomePayloadSHA256, StringComparison.Ordinal)
            || parsedForcedOutcomeEvent <= 0
            || forcedOutcomePayload.Length != 64
            || forcedNull.SelectionCause != CortexPolicySelectionCauses.TrialOverride
            || !forcedNull.Diverged
            || forcedNull.Action == forcedNull.LaunchpadAction
            || forcedNull.Action == forcedNull.RawCandidateAction)
        {
            failure = "object outcome packet does not carry the distinct forced executed-divergence corroboration";
            return false;
        }
        foreach (byte[] payload in payloads.Values)
        {
            if (!TapePacketCreator.TryDecodeLoopLineageEdge(payload, out LoopLineageEdgeReceipt receipt)) continue;
            if (receipt.Node.NodeID == outcome.OutcomeNodeID && receipt.Node.EventID.Value == outcome.TerminalOutcomeEventID
                && receipt.Node.Species == LoopLineageNodeSpecies.AdjudicatedOutcome
                && receipt.PredecessorIDs.Count == 1
                && lineage.Any(edge => edge.Node.Species == LoopLineageNodeSpecies.PaidDivergence
                    && edge.Node.NodeID == receipt.PredecessorIDs[0]
                    && payloads.TryGetValue(edge.Node.EventID.Value, out byte[]? divergencePacket)
                    && TryReadField(divergencePacket, "POLICY-FUNDED-DISSENT", "decision", out string divergenceDecision)
                    && TryParseTypedU64(divergenceDecision, out ulong divergenceDecisionID)
                    && divergenceDecisionID == selectedProof.DecisionID.Value
                    && TryReadField(divergencePacket, "POLICY-FUNDED-DISSENT", "funding", out string divergenceFunding)
                    && ulong.TryParse(divergenceFunding, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong divergenceQuotaID)
                    && divergenceQuotaID == selectedProof.Funding.QuotaDecisionID.Value
                    && TryReadField(divergencePacket, "POLICY-FUNDED-DISSENT", "readout", out string divergenceReadout)
                    && ulong.TryParse(divergenceReadout, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong divergenceFingerprint)
                    && divergenceFingerprint == selectedProof.ReadoutFingerprint
                    && TryReadField(divergencePacket, "POLICY-FUNDED-DISSENT", "revision", out string divergenceRevision)
                    && ulong.TryParse(divergenceRevision, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong divergenceRevisionValue)
                    && divergenceRevisionValue == selectedProof.ReadoutRevision.Value
                    && TryReadField(divergencePacket, "POLICY-FUNDED-DISSENT", "execution", out string divergenceExecution)
                    && divergenceExecution == (selectedProof.ForkReceipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "none")))
            {
                failure = "";
                return true;
            }
        }
        failure = "object outcome node is not a tape-custodied adjudicated-outcome edge";
        return false;
    }

    private static bool TryReadField(byte[] packet, string prefix, string field, out string value)
    {
        string text = Encoding.ASCII.GetString(packet);
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) { value = ""; return false; }
        string marker = "\t" + field + "=";
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { value = ""; return false; }
        start += marker.Length;
        int end = text.IndexOf('\t', start);
        value = end < 0 ? text[start..] : text[start..end];
        return value.Length != 0;
    }

    private static bool TryParseTypedU64(string token, out ulong value)
    {
        value = 0;
        return token.Length == 18 && token[0] == 'u' && token[1] == ':'
            && ulong.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
