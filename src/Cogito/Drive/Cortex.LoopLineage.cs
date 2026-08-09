namespace Cogito;

using System.Text;
using Cogito.Grammar;

public sealed partial class Cortex
{
    private const uint LoopClosureLinkStateMagic = 0x314B4E4C;
    private bool _loopLineageEnabled;
    private LoopLineageTurnstile? _loopLineage;
    private Func<Tape, TapeEventID, bool>? _loopLineageWorldRootPredicate;
    private readonly List<(LoopClosureCompositionEpisode Episode, GrammarFoldProvenanceReceipt Fold)> _loopClosureFolds = new();
    private readonly List<LoopClosureCompositionEpisode> _pendingLoopClosureEpisodes = new();
    private readonly List<PendingLoopClosureTeacher> _pendingLoopClosureTeachers = new();
    private readonly Dictionary<LoopLineageNodeID, PatternBecameThoughtCorroboration> _loopClosureTheories = new();
    private readonly List<PendingLoopClosureObject> _pendingLoopClosureObjects = new();
    private readonly record struct LoopClosurePolicyRail(
        LoopLineageCausalID CausalID,
        LoopLineageNodeID ReadoutNodeID);

    private readonly Dictionary<(CortexPolicyID Policy, CortexPolicyReadoutFingerprint Fingerprint, GrammarRevisionID Revision), LoopClosurePolicyRail> _loopClosurePolicyRails = new();
    private readonly List<LoopClosureLinkAttempt> _loopClosureLinkAttempts = new();
    private int _loopClosureLinkCheckpointCursor;

    private readonly record struct PendingLoopClosureObject(
        string OutcomeID,
        LoopLineageNodeID OutcomeNodeID,
        LoopClosureDigest PatternEvidenceSHA256,
        LoopClosureDigest DivergenceEvidenceSHA256,
        long TerminalOutcomeEventID);

    /// A teacher packet is emitted before its policy decision, then waits at the
    /// publication seam. The next fold consumes this concrete tape event; keeping
    /// it in checkpoint state prevents a kill between decision and fold from
    /// turning the teacher into an unconsumed audit orphan.
    private readonly record struct PendingLoopClosureTeacher(
        LoopClosureTeacherPacketProvenance Teacher,
        TapeEventID EventID);

    /// Registered loop-closure arms opt into lineage emission before Run(). All other
    /// Cortex instances leave this disabled so historical paired artifacts stay exact.
    internal void EnableLoopLineage() => _loopLineageEnabled = true;

    internal LoopLineageTurnstile? LoopLineage => _loopLineage;

    /// Record one causal link at the transition that reached it.  The tape
    /// packet and the arm projection are landed together; later adjudication
    /// may verify them but never invent either one.
    internal bool TryRecordLoopClosureLinkAttempt(in LoopClosureLinkAttempt attempt)
        => TryRecordLoopClosureLinkAttempt(in attempt, out _);

    internal bool TryRecordLoopClosureLinkAttempt(in LoopClosureLinkAttempt attempt, out LoopClosureLinkAttempt recorded)
    {
        recorded = default;
        if (!_loopLineageEnabled || _runtimeRun is null || _runtimeTape is null || _runtimeJournal is null)
            return false;
        attempt.Validate();
        string runID = Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir));
        if (!string.Equals(attempt.RunID, runID, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure link run identity disagrees with the live arm");
        // The identity fields are read out of the `in` parameter before any query: a readonly
        // reference cannot be captured by a lambda, and the closure only ever needs these scalars.
        string recordID = attempt.RecordID;
        LoopClosureLinkSpecies species = attempt.Species;
        TapeEventID eventID = attempt.EventID;
        string evidenceRunID = attempt.EvidenceRunID;
        LoopClosureDigest predecessorAttemptSHA256 = attempt.PredecessorAttemptSHA256;
        long predecessorEventID = attempt.PredecessorEventID;
        LoopClosureLinkAttempt? existing = _loopClosureLinkAttempts.FirstOrDefault(candidate =>
            string.Equals(candidate.RecordID, recordID, StringComparison.Ordinal));
        if (existing is { } prior)
        {
            if (prior.AttemptSHA256 == attempt.AttemptSHA256)
            {
                recorded = prior;
                return true;
            }
            throw new InvalidDataException($"loop-closure link {attempt.RecordID} changed after admission");
        }
        if (_loopClosureLinkAttempts.Any(candidate => candidate.Species == species
            && candidate.EventID == eventID
            && string.Equals(candidate.EvidenceRunID, evidenceRunID, StringComparison.Ordinal)))
            throw new InvalidDataException("loop-closure link event identity repeats in its tape namespace");
        if (attempt.Species is not (LoopClosureLinkSpecies.PreferenceDivergence or LoopClosureLinkSpecies.InterventionDivergence))
        {
            LoopClosureLinkAttempt? predecessor = _loopClosureLinkAttempts.FirstOrDefault(candidate =>
                candidate.AttemptSHA256 == predecessorAttemptSHA256
                && candidate.EventID.Value == predecessorEventID);
            if (predecessor is not { } predecessorAttempt
                || (byte)predecessorAttempt.Species + 1 != (byte)attempt.Species)
                throw new InvalidDataException("loop-closure link predecessor is not the immediately preceding typed transition");
        }
        TapeEventID packetEventID = TapePacketCreator.AppendRepositoryLoopClosureLink(
            _runtimeTape, _runtimeJournal, attempt.Step, in attempt);
        if (!_runtimeTape.Resolve(packetEventID, out byte[] linkPacket))
            throw new InvalidDataException("loop-closure link packet was not retained by the tape");
        recorded = attempt with
        {
            LinkEventID = packetEventID,
            LinkPacketSHA256 = new(RepositoryLineageReceiptCodec.Digest(attempt.Kind, attempt.Canonical)),
            LinkJournalSHA256 = LoopClosureLinkAttemptStore.DigestLoopClosureLinkJournalReceipt(
                attempt.Step, packetEventID.Value, linkPacket.Length),
        };
        ValidateLoopClosureLinkPacket(recorded);
        try
        {
            LoopClosureLinkAttemptStore.Write(_runtimeRun, in recorded);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            throw new InvalidDataException("loop-closure link packet landed without its arm authority row; terminal run is invalid", error);
        }
        _loopClosureLinkAttempts.Add(recorded);
        return true;
    }

    private void ValidateLoopClosureLinkPacket(in LoopClosureLinkAttempt attempt)
    {
        if (_runtimeTape is null || _runtimeJournal is null)
            throw new InvalidDataException("loop-closure link packet validation has no live tape/journal");
        ValidateLoopClosureLinkPacket(_runtimeTape, _runtimeJournal, in attempt);
    }

    private static void ValidateLoopClosureLinkPacket(Tape tape, Journal journal, in LoopClosureLinkAttempt attempt)
    {
        if (attempt.LinkEventID.Value <= 0
            || !attempt.LinkPacketSHA256.IsValid || !attempt.LinkJournalSHA256.IsValid
            || !tape.Resolve(attempt.LinkEventID, out byte[] packet)
            || !tape.TryGetEventView(attempt.LinkEventID, out TapeEventView packetView)
            || packetView.Source != "repository:loop-link"
            || packetView.Provenance != Provenances.Execution
            || packetView.Roles != TapeEventRoles.AuditOnly
            || !TapePacketCreator.TryReadRepositoryLineageReceipt(packet, out string kind, out string canonical, out string digest)
            || kind != attempt.Kind
            || !string.Equals(canonical, attempt.Canonical, StringComparison.Ordinal)
            || !string.Equals(digest, attempt.LinkPacketSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(digest, RepositoryLineageReceiptCodec.Digest(attempt.Kind, attempt.Canonical), StringComparison.Ordinal)
            || !JournalContainsLoopLink(journal, attempt.Step, attempt.LinkEventID, packet.Length, attempt.LinkJournalSHA256))
            throw new InvalidDataException("loop-closure link packet custody diverged");
    }

    private static bool JournalContainsLoopLink(Journal journal, int step, TapeEventID eventID, int payloadLength, LoopClosureDigest digest)
    {
        string expected = LoopClosureLinkAttemptStore.DigestLoopClosureLinkJournalReceipt(step, eventID.Value, payloadLength).Value;
        return digest.Value == expected && journal.ResidentLines.Any(line =>
        {
            string[] fields = line.Split('\t');
            return fields.Length >= 5 && fields[0] == step.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && fields[1] == "mint" && fields[2] == eventID.ToString() && fields[3] == "repository:loop-link"
                && fields[4] == payloadLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + "B";
        });
    }

    internal IReadOnlyList<LoopClosureLinkAttempt> RecordedLoopClosureLinks
        => _loopClosureLinkAttempts;

    internal LoopClosureLinkAttempt[] CaptureLoopClosureLinkCheckpointDelta()
    {
        if (_loopClosureLinkCheckpointCursor < 0 || _loopClosureLinkCheckpointCursor > _loopClosureLinkAttempts.Count)
            throw new InvalidDataException("loop-closure link checkpoint cursor is outside the native rail");
        return _loopClosureLinkAttempts.Skip(_loopClosureLinkCheckpointCursor).ToArray();
    }

    internal void CommitLoopClosureLinkCheckpointDelta()
        => _loopClosureLinkCheckpointCursor = _loopClosureLinkAttempts.Count;

    internal void ApplyLoopClosureLinkCheckpointDelta(IReadOnlyList<LoopClosureLinkAttempt> attempts, Tape tape, Journal journal)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        HashSet<(LoopClosureLinkSpecies Species, string EvidenceRunID, long EventID)> evidenceIdentities =
            _loopClosureLinkAttempts
                .Select(static attempt => (attempt.Species, attempt.EvidenceRunID, attempt.EventID.Value))
                .ToHashSet();
        HashSet<long> linkPacketEvents = _loopClosureLinkAttempts
            .Where(static attempt => attempt.LinkEventID.Value > 0)
            .Select(static attempt => attempt.LinkEventID.Value)
            .ToHashSet();
        for (int index = 0; index < attempts.Count; index++)
        {
            LoopClosureLinkAttempt attempt = attempts[index];
            attempt.Validate();
            if (_loopClosureLinkAttempts.Any(existing => existing.RecordID == attempt.RecordID))
                throw new InvalidDataException("typed mutation repeats a loop-closure link attempt");
            if (!evidenceIdentities.Add((attempt.Species, attempt.EvidenceRunID, attempt.EventID.Value)))
                throw new InvalidDataException("typed mutation repeats a loop-closure evidence event identity");
            if (attempt.LinkEventID.Value > 0 && !linkPacketEvents.Add(attempt.LinkEventID.Value))
                throw new InvalidDataException("typed mutation repeats a loop-closure link packet event identity");
            if (attempt.Species is not (LoopClosureLinkSpecies.PreferenceDivergence or LoopClosureLinkSpecies.InterventionDivergence))
            {
                LoopClosureLinkAttempt? predecessor = _loopClosureLinkAttempts.FirstOrDefault(candidate =>
                    candidate.AttemptSHA256 == attempt.PredecessorAttemptSHA256
                    && candidate.EventID.Value == attempt.PredecessorEventID);
                if (predecessor is not { } prior || (byte)prior.Species + 1 != (byte)attempt.Species)
                    throw new InvalidDataException("typed mutation loop-link predecessor is not the preceding transition");
            }
            ValidateLoopClosureLinkPacket(tape, journal, in attempt);
            _loopClosureLinkAttempts.Add(attempt);
        }
        _loopClosureLinkCheckpointCursor = _loopClosureLinkAttempts.Count;
    }

    internal IReadOnlyList<LoopClosureGateLiveness> BuildLoopClosureLinkLiveness()
    {
        LoopClosureGateLiveness[] meters = new LoopClosureGateLiveness[LoopClosureLinkContract.OrderedSpecies.Count];
        for (int index = 0; index < meters.Length; index++)
        {
            LoopClosureLinkSpecies species = LoopClosureLinkContract.OrderedSpecies[index];
            List<LoopClosureLinkAttempt> rows = _loopClosureLinkAttempts.Where(row => row.Species == species).ToList();
            Dictionary<LoopClosureGateDenialReasons, long> denials = new();
            foreach (LoopClosureLinkAttempt row in rows.Where(row => row.State == LoopClosureLinkStates.Denied))
                denials[row.DenialReason] = denials.TryGetValue(row.DenialReason, out long count) ? checked(count + 1) : 1;
            meters[index] = LoopClosureGateLiveness.Create(species, rows.Count,
                rows.Count(row => row.State == LoopClosureLinkStates.Admitted),
                rows.Count(row => row.State == LoopClosureLinkStates.Denied),
                denials.OrderBy(row => row.Key).Select(row => new LoopClosureGateDenial(row.Key, row.Value)).ToArray());
        }
        return meters;
    }

    internal bool TryGetRung0BasisLaws(
        IReadOnlyList<EmlRung0BasisLawIdentity> basisLawIdentities,
        out IReadOnlyList<LoopLineageNode> lawNodes)
    {
        lawNodes = Array.Empty<LoopLineageNode>();
        if (!_loopLineageEnabled || _loopLineage is null || _runtimeTape is null
            || basisLawIdentities is null || basisLawIdentities.Count == 0) return false;
        List<LoopLineageNode> selected = new();
        foreach (EmlRung0BasisLawIdentity identity in basisLawIdentities.Distinct())
        {
            if (!identity.IsValid) return false;
            LoopLineageNode? found = null;
            foreach (LoopLineageEdgeReceipt edge in _loopLineage.Receipts)
            {
                if (edge.Node.Species != LoopLineageNodeSpecies.VerifiedLaw
                    || !_runtimeTape.Resolve(edge.Node.EventID, out byte[] payload)
                    || !TapePacketCreator.TryReadEmlLawAdmissionID(payload, out string admissionID)
                    || !string.Equals(admissionID, identity.AdmissionID, StringComparison.Ordinal)) continue;
                if (found is null || edge.Node.EventID.Value > found.Value.EventID.Value) found = edge.Node;
            }
            if (found is not LoopLineageNode law) return false;
            selected.Add(law);
        }
        lawNodes = selected.OrderBy(static node => node.EventID.Value).ToArray();
        return selected.Count > 0;
    }

    internal bool TryGetLoopClosureReadoutBinding(
        in LoopClosureTeacherPacketProvenance teacher,
        out LoopLineageNode predecessor,
        out LoopLineageCausalID causalID)
    {
        predecessor = default;
        causalID = default;
        if (!_loopLineageEnabled || _loopLineage is null) return false;
        foreach ((LoopClosureCompositionEpisode episode, GrammarFoldProvenanceReceipt fold) in _loopClosureFolds)
        {
            // A fold may publish several derivation episodes in one revision.  The
            // teacher belongs to one episode's exact event set, never to the fold-wide
            // union; matching the union made the first displaced edge win for every
            // later episode on the same rail.
            TapeEventID[] episodeEvents = LoopClosureCompositionEpisode.NormalizeEventIDs(
                [episode.CompositionEventID, .. episode.EvidenceEventIDs]);
            if (episode.EpisodeID != teacher.EpisodeID
                || fold.Revision != teacher.FoldRevision
                || !episodeEvents.SequenceEqual(teacher.MatchedEventIDs)
                || teacher.EvidenceDigest != episode.EvidenceDigest) continue;
            LoopLineageNodeID derivationNodeID = new(episode.EpisodeID.Value);
            LoopLineageEdgeReceipt[] displaced = _loopLineage.Receipts.Where(edge =>
                edge.Node.Species == LoopLineageNodeSpecies.DisplacedEvaluation
                && edge.PredecessorIDs.Count == 1
                && edge.PredecessorIDs[0] == derivationNodeID).ToArray();
            if (displaced.Length != 1) return false;
            predecessor = displaced[0].Node;
            causalID = predecessor.CausalID;
            return causalID.IsValid;
        }
        return false;
    }

    internal void BindLoopClosurePolicyRail(
        CortexPolicyID policy,
        CortexPolicyReadoutFingerprint fingerprint,
        GrammarRevisionID revision,
        LoopLineageCausalID causalID,
        LoopLineageNodeID readoutNodeID)
    {
        if (_loopLineageEnabled && causalID.IsValid && readoutNodeID.IsValid && revision != GrammarRevisionID.Zero)
            _loopClosurePolicyRails[(policy, fingerprint, revision)] = new(causalID, readoutNodeID);
    }

    internal bool TryGetLoopClosurePolicyRail(
        CortexPolicyID policy,
        CortexPolicyReadoutFingerprint fingerprint,
        GrammarRevisionID revision,
        out LoopLineageCausalID causalID)
    {
        bool found = _loopClosurePolicyRails.TryGetValue((policy, fingerprint, revision), out LoopClosurePolicyRail binding);
        causalID = found ? binding.CausalID : default;
        return found;
    }

    internal bool TryGetLoopClosurePolicyReadout(
        CortexPolicyID policy,
        CortexPolicyReadoutFingerprint fingerprint,
        GrammarRevisionID revision,
        out LoopLineageNode readout)
    {
        readout = default;
        if (!_loopClosurePolicyRails.TryGetValue((policy, fingerprint, revision), out LoopClosurePolicyRail binding)
            || _loopLineage is null)
            return false;
        return _loopLineage.TryFindNode(node => node.NodeID == binding.ReadoutNodeID, out readout);
    }

    private void EmitLoopClosurePolicyQuota(
        TapeEventID eventID,
        in CortexPolicyTrialQuotaDecision funding)
    {
        if (_loopLineage is null
            || !TryGetLoopClosurePolicyRail(funding.Policy, funding.ReadoutIdentity, funding.CandidateRevision, out _))
            return;
        if (!TryGetLoopClosurePolicyReadout(funding.Policy, funding.ReadoutIdentity,
                funding.CandidateRevision, out LoopLineageNode readout))
            throw new InvalidDataException("registered policy funding has no exact learned-readout predecessor");
        if (!_loopLineage.TryEmit(Step, LoopLineageNodeSpecies.Quota, eventID, funding.CandidateRevision,
                [readout.NodeID], readout.CausalID))
            throw new InvalidDataException("registered policy funding lineage emission did not close");
    }

    /// Lay the legal spine a LearnedReadout stands on: world → verified law → rung-0 derivation
    /// → displaced evaluation → readout. The predecessor law admits nothing shorter, so a fixture
    /// that wants a readout must pay for the chain that makes one admissible. The readout's own
    /// event is appended by the caller's delegate LAST — a predecessor that lands later on the
    /// tape than its child is not a predecessor.
    private static LoopLineageEdgeReceipt EmitFixtureReadoutSpine(
        LoopLineageTurnstile lineage,
        Tape tape,
        string tag,
        LoopLineageEdgeReceipt worldRoot,
        Func<TapeEventID> appendReadoutEvent,
        GrammarRevisionID revision)
    {
        LoopLineageEdgeReceipt law = lineage.Emit(0, LoopLineageNodeSpecies.VerifiedLaw,
            tape.Append(Encoding.UTF8.GetBytes(tag + "-law"), "fixture:law", Provenances.Execution),
            null, [worldRoot.Node.NodeID]);
        LoopLineageEdgeReceipt derivation = lineage.Emit(0, LoopLineageNodeSpecies.Rung0Composition,
            tape.Append(Encoding.UTF8.GetBytes(tag + "-derivation"), "fixture:derivation", Provenances.Execution),
            revision, [law.Node.NodeID], law.Node.CausalID);
        LoopLineageEdgeReceipt displaced = lineage.Emit(0, LoopLineageNodeSpecies.DisplacedEvaluation,
            tape.Append(Encoding.UTF8.GetBytes(tag + "-displaced"), "fixture:displaced", Provenances.Execution),
            null, [derivation.Node.NodeID], derivation.Node.CausalID);
        return lineage.Emit(0, LoopLineageNodeSpecies.LearnedReadout, appendReadoutEvent(), revision,
            [displaced.Node.NodeID], displaced.Node.CausalID);
    }

    internal static bool VerifyPolicyFundingLineageIdentityFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        CortexPolicyID policy = new("fixture.funding-lineage");
        GrammarRevisionID revision = new(7);
        CortexPolicyReadoutFingerprint readoutIdentity = new(0x700D700D700D700DUL);
        ulong candidateFingerprint = 0xB190B190B190B190UL;
        using Tape tape = new();
        Journal journal = new();
        Cortex cortex = new(new CortexConfig { Tools = [], ActionPolicies = [], Rewards = [] });
        cortex.EnableLoopLineage();
        cortex._runtimeTape = tape;
        cortex._runtimeJournal = journal;
        cortex.BindLoopLineage(tape, journal);

        TapeEventID worldEvent = TapePacketCreator.CommitWorldEncounter(
            tape, journal, 0, "funding-lineage-world"u8.ToArray(), 0, 0, fresh: true, coverage: 1.0);
        LoopLineageEdgeReceipt world = cortex._loopLineage!.Emit(0, LoopLineageNodeSpecies.AdmissionPlan, worldEvent);
        LoopLineageEdgeReceipt readout = EmitFixtureReadoutSpine(cortex._loopLineage!, tape, "funding-lineage", world,
            () => tape.Append("funding-lineage-readout"u8.ToArray(), "policy:" + policy.Value, Provenances.Execution), revision);
        cortex.BindLoopClosurePolicyRail(policy, readoutIdentity, revision, readout.Node.CausalID, readout.Node.NodeID);

        PolicyCanonicalStateID canonicalState = new(
            policy, PolicyCanonicalStateKinds.Homeostat, 1, 0xC0DEUL);
        CortexPolicyDecision canonicalDecision = new(
            new CortexPolicyDecisionID(77), policy,
            new CortexPolicyDecisionReadout(
                0, 1, 1, 1, CortexPolicyAuthorities.Grammar, revision,
                CortexPolicySelectionCauses.GrammarCandidate, 0xABCDUL,
                candidateFingerprint, readoutIdentity.Value));
        CortexPolicyDecision canonicalDecisionValue = canonicalDecision;
        TapeEventID canonicalDecisionEvent = default;
        LoopLineageEdgeReceipt canonicalReadout = EmitFixtureReadoutSpine(cortex._loopLineage, tape, "funding-lineage-canonical", world,
            () => canonicalDecisionEvent = TapePacketCreator.AppendPolicyDecision(
                tape, journal, 0, in canonicalDecisionValue,
                [new MetricSample(new MetricID(1), NumericValue.FromI64(0))], 2, out _), revision);
        bool canonicalIdentityDistinct = readoutIdentity.Value
            != GrammarPolicyReadout.ComputeFingerprint(revision, policy)
            && GrammarPolicyReadout.ComputeStateFingerprint(policy, in canonicalState)
                != GrammarPolicyReadout.ComputeFingerprint(revision, policy)
            && canonicalDecision.ReadoutIdentity == readoutIdentity;

        CortexPolicyTrialQuotaDecision funding = new(
            new CortexPolicyQuotaDecisionID(1), policy, candidateFingerprint, 0, 1, 1, 1, 1,
            CortexPolicyQuotaDecisions.Paid, 1, 0)
        {
            ReadoutFingerprint = readoutIdentity.Value,
            CandidateRevision = revision,
            CandidateState = CortexPolicyTrialCandidateStates.Active,
            DenialReason = CortexPolicyTrialDenialReasons.None,
            CandidateOriginStep = 0,
            CandidateCurrentStep = 0,
            CandidateRequiredStep = 0,
            CanonicalState = new PolicyCanonicalStateID(
                policy, PolicyCanonicalStateKinds.Homeostat, 1, 0xC0DEUL),
            AllocationIdentity = "fixture-lineage-rebind",
            AllocationDigest = CortexPolicyTrialAllocation.ComputeDigest(
                policy, CortexPolicyAuthorities.Grammar, 1, "fixture-lineage-rebind"),
            AllocationArmSteps = 1,
            SeedAuditOnlyDigest = new string('a', 64),
        };
        TapeEventID fundingEvent = TapePacketCreator.AppendPolicyTrialQuota(tape, journal, 0, in funding);
        cortex.EmitLoopClosurePolicyQuota(fundingEvent, in funding);
        int exactFundingEdges = cortex._loopLineage.Receipts.Count(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.Quota && edge.Node.EventID == fundingEvent);
        LoopLineageEdgeReceipt fundingEdge = cortex._loopLineage.Receipts.Single(edge =>
            edge.Node.Species == LoopLineageNodeSpecies.Quota && edge.Node.EventID == fundingEvent);
        CortexPolicyTrialQuotaDecision wrongIdentity = funding with { ReadoutFingerprint = candidateFingerprint };
        TapeEventID wrongFundingEvent = TapePacketCreator.AppendPolicyTrialQuota(tape, journal, 0, in wrongIdentity);
        cortex.EmitLoopClosurePolicyQuota(wrongFundingEvent, in wrongIdentity);
        bool wrongIdentityRejected = !cortex.TryGetLoopClosurePolicyRail(policy,
                wrongIdentity.ReadoutIdentity, revision, out _)
            && !cortex._loopLineage.Receipts.Any(edge =>
                edge.Node.Species == LoopLineageNodeSpecies.Quota && edge.Node.EventID == wrongFundingEvent);

        CortexPolicyID secondPolicy = new("fixture.funding-lineage.second");
        GrammarRevisionID secondRevision = new(11);
        CortexPolicyReadoutFingerprint secondReadoutIdentity = new(0x700D700D700D7011UL);
        TapeEventID secondWorldEvent = TapePacketCreator.CommitWorldEncounter(
            tape, journal, 0, "funding-lineage-world-second"u8.ToArray(), 0, 1, fresh: true, coverage: 1.0);
        LoopLineageEdgeReceipt secondWorld = cortex._loopLineage.Emit(
            0, LoopLineageNodeSpecies.AdmissionPlan, secondWorldEvent);
        LoopLineageEdgeReceipt secondReadout = EmitFixtureReadoutSpine(cortex._loopLineage, tape, "funding-lineage-second", secondWorld,
            () => tape.Append("funding-lineage-readout-second"u8.ToArray(), "policy:" + secondPolicy.Value, Provenances.Execution),
            secondRevision);
        cortex.BindLoopClosurePolicyRail(secondPolicy, secondReadoutIdentity, secondRevision,
            secondReadout.Node.CausalID, secondReadout.Node.NodeID);

        byte[] checkpointImage;
        using (MemoryStream image = new())
        {
            using (CkptWriter writer = new(image)) cortex.SaveLoopClosureState(writer);
            checkpointImage = image.ToArray();
        }
        byte[] checkpointImageAgain;
        using (MemoryStream image = new())
        {
            using (CkptWriter writer = new(image)) cortex.SaveLoopClosureState(writer);
            checkpointImageAgain = image.ToArray();
        }
        Cortex restored = new(new CortexConfig { Tools = [], ActionPolicies = [], Rewards = [] });
        using (MemoryStream image = new(checkpointImage))
        using (CkptReader reader = new(image)) restored.LoadLoopClosureState(reader);
        Cortex rebound = new(new CortexConfig { Tools = [], ActionPolicies = [], Rewards = [] });
        rebound.EnableLoopLineage();
        rebound.BindLoopLineage(tape, journal);
        bool checkpointRails = checkpointImage.AsSpan().SequenceEqual(checkpointImageAgain)
            && restored.TryGetLoopClosurePolicyRail(policy, readoutIdentity, revision, out LoopLineageCausalID restoredCausal)
            && restoredCausal == readout.Node.CausalID
            && restored.TryGetLoopClosurePolicyRail(secondPolicy, secondReadoutIdentity, secondRevision, out LoopLineageCausalID restoredSecondCausal)
            && restoredSecondCausal == secondReadout.Node.CausalID
            && rebound.TryGetLoopClosurePolicyRail(policy, readoutIdentity, revision, out LoopLineageCausalID reboundCausal)
            && reboundCausal == canonicalReadout.Node.CausalID;

        bool reboundOwner = VerifyPolicyFundingLineageRebindFixture(output);
        bool exact = exactFundingEdges == 1
            && fundingEdge.PredecessorIDs.Count == 1
            && fundingEdge.PredecessorIDs[0] == readout.Node.NodeID
            && fundingEdge.Node.CausalID == readout.Node.CausalID
            && wrongIdentityRejected && canonicalIdentityDistinct && checkpointRails && reboundOwner;
        output.WriteLine($"  policy funding lineage identity · readout={readoutIdentity} candidate={candidateFingerprint:X16} · funding={(exactFundingEdges == 1 ? "one/exact" : "DUPLICATE/MISSING")} · wrong-identity={(wrongIdentityRejected ? "rejected" : "ACCEPTED")} · canonical={(canonicalIdentityDistinct ? "distinct/exact" : "COLLAPSED")} · checkpoint-rails={(checkpointRails ? "two/exact/deterministic/tape" : "BROKEN")} · rebound-owner={(reboundOwner ? "exact" : "STALE")} · {(exact ? "PASS" : "FAIL")}");
        return exact;
    }

    private static bool VerifyPolicyFundingLineageRebindFixture(TextWriter output)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        LoopClosurePolicyBinding policyBinding = HomeostatPolicyBoundaryDomain.Instance.PolicyBinding;
        GrammarRevisionID revision = new(13);
        CortexPolicyReadoutFingerprint readoutIdentity = new(0x700D700D700D7013UL);
        ulong candidateFingerprint = 0xB190B190B190B193UL;
        CortexPolicyDecisionID staleDecisionID = new(87);
        CortexPolicyDecisionID reboundDecisionID = new(88);
        CortexPolicyQuotaDecisionID fundingID = new(2);

        using Tape tapeA = new();
        Journal journalA = new();
        Cortex cortex = new(new CortexConfig { Tools = [], ActionPolicies = [], Rewards = [] });
        cortex.EnableLoopLineage();
        cortex._runtimeTape = tapeA;
        cortex._runtimeJournal = journalA;
        cortex.BindLoopLineage(tapeA, journalA);
        LoopLineageEdgeReceipt worldA = cortex._loopLineage!.Emit(0, LoopLineageNodeSpecies.AdmissionPlan,
            TapePacketCreator.CommitWorldEncounter(tapeA, journalA, 0,
                "funding-lineage-rebind-a"u8.ToArray(), 0, 0, fresh: true, coverage: 1.0));
        CortexPolicyDecision staleDecision = CreatePolicyFundingLineageFixtureDecision(
            staleDecisionID, policy, revision, readoutIdentity, candidateFingerprint);
        CortexPolicyDecision staleDecisionValue = staleDecision;
        TapeEventID staleDecisionEvent = default;
        EmitFixtureReadoutSpine(cortex._loopLineage, tapeA, "funding-lineage-rebind-a", worldA,
            () => staleDecisionEvent = TapePacketCreator.AppendPolicyDecision(
                tapeA, journalA, 0, in staleDecisionValue,
                [new MetricSample(new MetricID(1), NumericValue.FromI64(0))], 2, out _), revision);
        LoopClosurePredecessorResolution prior = cortex._loopLineage.ResolvePolicyFundingPredecessors(
            staleDecisionID, fundingID, in policyBinding);
        bool primed = prior.Kind == LoopClosurePredecessorResolutionKinds.FundingMissing
            && prior.Readout.NodeID.IsValid;

        using Tape tapeB = new();
        Journal journalB = new();
        LoopLineageTurnstile lineageB = new(tapeB, journalB);
        LoopLineageEdgeReceipt worldB = lineageB.Emit(0, LoopLineageNodeSpecies.AdmissionPlan,
            TapePacketCreator.CommitWorldEncounter(tapeB, journalB, 0,
                "funding-lineage-rebind-b"u8.ToArray(), 0, 0, fresh: true, coverage: 1.0));
        CortexPolicyDecision reboundDecision = CreatePolicyFundingLineageFixtureDecision(
            reboundDecisionID, policy, revision, readoutIdentity, candidateFingerprint);
        TapeEventID reboundDecisionEvent = default;
        LoopLineageEdgeReceipt reboundReadout = EmitFixtureReadoutSpine(lineageB, tapeB, "funding-lineage-rebind-b", worldB,
            () => reboundDecisionEvent = TapePacketCreator.AppendPolicyDecision(
                tapeB, journalB, 0, in reboundDecision,
                [new MetricSample(new MetricID(1), NumericValue.FromI64(0))], 2, out _), revision);
        CortexPolicyTrialQuotaDecision funding = new(
            fundingID, policy, candidateFingerprint, 0, 1, 1, 1, 1,
            CortexPolicyQuotaDecisions.Paid, 1, 0)
        {
            ReadoutFingerprint = readoutIdentity.Value,
            CandidateRevision = revision,
            CandidateState = CortexPolicyTrialCandidateStates.Active,
            DenialReason = CortexPolicyTrialDenialReasons.None,
            SeedAuditOnlyDigest = new string('a', 64),
        };
        TapeEventID fundingEvent = TapePacketCreator.AppendPolicyTrialQuota(tapeB, journalB, 0, in funding);
        bool fundingPacketResolved = tapeB.Resolve(fundingEvent, out byte[] fundingPayload);
        bool fundingPacketDecoded = fundingPacketResolved
            && TapePacketCreator.TryReadPolicyTrialQuotaIdentity(fundingPayload, out CortexPolicyQuotaDecisionID decodedQuotaID)
            && decodedQuotaID.Equals(fundingID);
        LoopLineageEdgeReceipt reboundFunding = lineageB.Emit(
            0, LoopLineageNodeSpecies.Quota, fundingEvent, revision,
            [reboundReadout.Node.NodeID], reboundReadout.Node.CausalID);

        cortex._runtimeTape = tapeB;
        cortex._runtimeJournal = journalB;
        cortex.BindLoopLineage(tapeB, journalB);
        LoopClosurePredecessorResolution rebound = cortex._loopLineage.ResolvePolicyFundingPredecessors(
            reboundDecisionID, fundingID, in policyBinding);
        bool readoutFound = rebound.Readout.NodeID == reboundReadout.Node.NodeID;
        bool fundingFound = rebound.IsExact
            && rebound.Funding.NodeID == reboundFunding.Node.NodeID
            && rebound.Funding.CausalID == rebound.Readout.CausalID;
        LoopClosurePredecessorResolution stale = cortex._loopLineage.ResolvePolicyFundingPredecessors(
            staleDecisionID, fundingID, in policyBinding);
        bool staleRejected = stale.Kind == LoopClosurePredecessorResolutionKinds.ReadoutMissing;
        CortexPolicyTrialQuotaDecision mismatchedFunding = funding with
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(3),
        };
        // The mismatch under test is funding-behind-the-WRONG-readout, so the decoy funding must
        // still stand behind a real readout — one the policy rail was never bound to.
        CortexPolicyDecision decoyDecision = CreatePolicyFundingLineageFixtureDecision(
            new CortexPolicyDecisionID(89), policy, revision, readoutIdentity, candidateFingerprint);
        LoopLineageEdgeReceipt decoyReadout = EmitFixtureReadoutSpine(cortex._loopLineage, tapeB,
            "funding-lineage-rebind-decoy", worldB,
            () => TapePacketCreator.AppendPolicyDecision(
                tapeB, journalB, 0, in decoyDecision,
                [new MetricSample(new MetricID(1), NumericValue.FromI64(0))], 2, out _), revision);
        TapeEventID mismatchedFundingEvent = TapePacketCreator.AppendPolicyTrialQuota(
            tapeB, journalB, 0, in mismatchedFunding);
        cortex._loopLineage.Emit(0, LoopLineageNodeSpecies.Quota, mismatchedFundingEvent, revision,
            [decoyReadout.Node.NodeID], decoyReadout.Node.CausalID);
        LoopClosurePredecessorResolution mismatch = cortex._loopLineage.ResolvePolicyFundingPredecessors(
            reboundDecisionID, mismatchedFunding.QuotaDecisionID, in policyBinding);
        bool mismatchRejected = mismatch.Kind == LoopClosurePredecessorResolutionKinds.FundingReadoutMismatch;
        EmitFixtureReadoutSpine(cortex._loopLineage, tapeB, "funding-lineage-rebind-duplicate", worldB,
            () => TapePacketCreator.AppendPolicyDecision(
                tapeB, journalB, 0, in reboundDecision,
                [new MetricSample(new MetricID(1), NumericValue.FromI64(0))], 2, out _), revision);
        LoopClosurePredecessorResolution duplicate = cortex._loopLineage.ResolvePolicyFundingPredecessors(
            reboundDecisionID, fundingID, in policyBinding);
        bool duplicateRejected = duplicate.Kind == LoopClosurePredecessorResolutionKinds.ReadoutDuplicate;
        bool exact = primed && fundingPacketDecoded && cortex._loopLineage.IsBoundTo(tapeB, journalB)
            && readoutFound && fundingFound && staleRejected && mismatchRejected && duplicateRejected;
        output.WriteLine($"  policy funding lineage rebind · primed={(primed ? "yes" : "NO")} · owner={(cortex._loopLineage.IsBoundTo(tapeB, journalB) ? "current" : "STALE")} · packet={(fundingPacketDecoded ? "exact" : "INVALID")} · readout={(readoutFound ? "exact" : "MISSING")} · funding={(fundingFound ? "exact" : "MISSING")} · stale={(staleRejected ? "rejected" : "RETAINED")} · mismatch={(mismatchRejected ? "rejected" : "ACCEPTED")} · duplicate={(duplicateRejected ? "rejected" : "ACCEPTED")} · {(exact ? "PASS" : "FAIL")}");
        return exact;
    }

    private static CortexPolicyDecision CreatePolicyFundingLineageFixtureDecision(
        CortexPolicyDecisionID decisionID,
        CortexPolicyID policy,
        GrammarRevisionID revision,
        CortexPolicyReadoutFingerprint readoutIdentity,
        ulong candidateFingerprint)
        => new(decisionID, policy,
            new CortexPolicyDecisionReadout(
                0, 1, 1, 1, CortexPolicyAuthorities.Grammar, revision,
                CortexPolicySelectionCauses.GrammarCandidate, 0xABCDUL,
                candidateFingerprint, readoutIdentity.Value));

    internal bool TryGetLoopClosureFold(
        GrammarRevisionID revision,
        out GrammarFoldProvenanceReceipt fold)
    {
        for (int index = _loopClosureFolds.Count - 1; index >= 0; index--)
        {
            if (_loopClosureFolds[index].Fold.Revision != revision) continue;
            fold = _loopClosureFolds[index].Fold;
            return true;
        }
        fold = default;
        return false;
    }

    private void RestoreLoopClosurePolicyRails(Tape tape)
    {
        if (!_loopLineageEnabled || _loopLineage is null) return;
        Dictionary<TapeEventID, string> sources = tape.GetEventViews().ToDictionary(static view => view.Id, static view => view.Source);
        foreach (LoopLineageEdgeReceipt edge in _loopLineage.Receipts)
        {
            if (edge.Node.Species != LoopLineageNodeSpecies.LearnedReadout
                || !sources.TryGetValue(edge.Node.EventID, out string? source)
                || !source.StartsWith("policy:", StringComparison.Ordinal)) continue;
            string policyValue = source["policy:".Length..];
            if (policyValue.Length == 0 || !tape.Resolve(edge.Node.EventID, out byte[] payload)) continue;
            CortexPolicyDecisionPacket packet;
            try { packet = TapePacketCreator.DecodePolicyDecision(payload); }
            catch (InvalidDataException) { continue; }
            CortexPolicyID policy = new(policyValue);
            if (packet.Readout.ReadoutFingerprint == 0) continue;
            CortexPolicyReadoutFingerprint fingerprint = new(packet.Readout.ReadoutFingerprint);
            if (!fingerprint.IsValid) continue;
            _loopClosurePolicyRails[(policy, fingerprint, packet.Readout.GrammarRevision)] =
                new(edge.Node.CausalID, edge.Node.NodeID);
        }
    }

    // Evidence custody is opt-in with the registered arm. Ordinary Cortex runs have
    // no loop-closure directory and therefore cannot accidentally mint certification
    // receipts while exercising the same mechanisms.
    internal void WriteLoopClosurePattern(string admissionID, in PatternBecameThoughtCorroboration corroboration)
    {
        if (_loopLineageEnabled)
        {
            corroboration.Validate(requireCorroboration: true);
            LoopClosureEvidenceStore.WritePattern(CurrentRun, admissionID, in corroboration);
            _loopClosureTheories[corroboration.CompositionNodeID] = corroboration;
        }
    }

    internal void WriteLoopClosureR4(string recordID, in LoopClosureR4Provenance provenance)
    {
        if (_loopLineageEnabled) LoopClosureEvidenceStore.WriteR4(CurrentRun, recordID, in provenance);
    }

    internal void WriteLoopClosureDivergence(string fundingID, in ThoughtOverruledInstinctCorroboration corroboration, IPolicyBoundaryDomain domain)
    {
        if (_loopLineageEnabled) LoopClosureEvidenceStore.WriteDivergence(CurrentRun, fundingID, in corroboration, domain);
    }

    internal void WriteLoopClosureDivergence(string fundingID, in PolicyBoundaryDivergenceAdjudication adjudication)
    {
        if (_loopLineageEnabled)
            LoopClosureEvidenceStore.WriteDivergence(CurrentRun, fundingID, in adjudication,
                RequirePolicyBoundaryDomain(adjudication.Proof.Policy));
    }

    internal void WriteLoopClosureDivergenceProof(in PolicyBoundaryDivergenceAdjudication adjudication)
    {
        if (_loopLineageEnabled)
            LoopClosureEvidenceStore.WriteDivergenceProof(CurrentRun, in adjudication,
                RequirePolicyBoundaryDomain(adjudication.Proof.Policy));
    }

    internal void WriteLoopClosureObject(string outcomeID, in ObjectLoopClosedCorroboration corroboration)
    {
        if (_loopLineageEnabled) LoopClosureEvidenceStore.WriteObject(CurrentRun, outcomeID, in corroboration);
    }

    internal void CloseLoopClosureAdjudication(in PolicyBoundaryDivergenceAdjudication adjudication)
    {
        CloseLoopClosureAdjudication(in adjudication, out _, out _, out _, out _);
    }

    internal void CloseLoopClosureAdjudication(
        in PolicyBoundaryDivergenceAdjudication adjudication,
        out TapeEventID fundedDivergenceEventID,
        out byte[] fundedDivergencePayload,
        out TapeEventID outcomeEventID,
        out byte[] outcomePayload)
    {
        fundedDivergenceEventID = default;
        fundedDivergencePayload = [];
        outcomeEventID = default;
        outcomePayload = [];
        if (!_loopLineageEnabled) return;
        if (_runtimeTape is null || _runtimeJournal is null || _loopLineage is null)
            throw new InvalidDataException("registered loop-closure adjudication has no runtime lineage tape");
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(adjudication.Proof.Policy);
        adjudication.Validate(domain);
        LoopLineageNodeID episodeNode = adjudication.Proof.Provenance is LoopClosureR4Provenance provenance
            ? new LoopLineageNodeID(provenance.Episode.EpisodeID.Value) : default;
        if (!episodeNode.IsValid || !_loopClosureTheories.TryGetValue(episodeNode, out PatternBecameThoughtCorroboration theory))
            throw new InvalidDataException("registered loop-closure object has no producing theory corroboration");
        LoopClosurePredecessorResolution predecessors = _loopLineage.ResolvePolicyFundingPredecessors(
            adjudication.Proof.DecisionID, adjudication.Proof.Funding.QuotaDecisionID,
            domain.PolicyBinding);
        if (!predecessors.IsExact)
        {
            string diagnostic = predecessors.FormatDiagnostic(
                adjudication.Proof.DecisionID, adjudication.Proof.Funding.QuotaDecisionID);
            Trace.Cortex.Boundary("loop-lineage.predecessor-rejected", diagnostic);
            throw new InvalidDataException($"registered paid divergence lineage predecessor rejected: {diagnostic}");
        }
        LoopLineageNode readoutNode = predecessors.Readout;
        LoopLineageNode fundingNode = predecessors.Funding;
        TapeEventID divergenceEvent = TapePacketCreator.AppendPolicyFundedDissent(_runtimeTape, _runtimeJournal, Step, in adjudication, domain);
        if (!_runtimeTape.Resolve(divergenceEvent, out fundedDivergencePayload))
            throw new InvalidDataException("registered paid divergence packet was not retained by the tape");
        fundedDivergenceEventID = divergenceEvent;
        LoopLineageEdgeReceipt divergenceEdge = _loopLineage.Emit(Step, LoopLineageNodeSpecies.PaidDivergence, divergenceEvent,
            adjudication.Proof.ReadoutRevision, [fundingNode.NodeID, readoutNode.NodeID], readoutNode.CausalID);
        TapeEventID outcomeEvent = TapePacketCreator.AppendPolicyBoundaryAdjudicatedOutcome(_runtimeTape, _runtimeJournal, Step, in adjudication, domain);
        if (!_runtimeTape.Resolve(outcomeEvent, out outcomePayload))
            throw new InvalidDataException("registered policy boundary outcome packet was not retained by the tape");
        outcomeEventID = outcomeEvent;
        LoopLineageEdgeReceipt outcomeEdge = _loopLineage.Emit(Step, LoopLineageNodeSpecies.AdjudicatedOutcome, outcomeEvent,
            adjudication.Proof.ReadoutRevision, [divergenceEdge.Node.NodeID], divergenceEdge.Node.CausalID);
        TapeEventID evidenceEvent = TapePacketCreator.AppendLoopLineageEvidence(_runtimeTape, _runtimeJournal, Step, outcomeEvent);
        _loopLineage.Emit(Step, LoopLineageNodeSpecies.NewTapeEvidence, evidenceEvent,
            adjudication.Proof.ReadoutRevision, [outcomeEdge.Node.NodeID], outcomeEdge.Node.CausalID);
        // The outcome is not the terminal lineage edge: the evidence edge above is
        // appended after it, and a final checkpoint can still restore the tape view.
        // Queue the object corroboration and materialize it only at run settlement, against
        // the final canonical digest, so its receipt cannot certify a prefix.
        _pendingLoopClosureObjects.Add(new PendingLoopClosureObject(
            outcomeEvent.ToString(), outcomeEdge.Node.NodeID,
            LoopClosureEvidenceStore.DigestPattern(in theory), adjudication.EvidenceSHA256,
            outcomeEvent.Value));
    }

    /// Materialize queued object corroborationes after the final lineage/tape checkpoint.
    /// No lineage-producing operation may run after this boundary.
    internal void FlushLoopClosureObjects()
    {
        if (!_loopLineageEnabled || _loopLineage is null || _pendingLoopClosureObjects.Count == 0) return;
        string finalDigest = _loopLineage.CanonicalDigest;
        string runID = Path.GetFileName(Path.GetFullPath(CurrentRun.Dir));
        IReadOnlyList<LoopClosureLinkAttempt> linkAttempts = _policyBoundaryDomains.Values.Any(static domain => domain is RepositoryPolicyBoundaryDomain)
            ? _loopClosureLinkAttempts
            : LoopClosureLinkAttemptStore.Read(CurrentRun.Dir, runID);
        foreach (PendingLoopClosureObject pending in _pendingLoopClosureObjects)
        {
            LoopClosureChildOutcomeReference childOutcome = linkAttempts
                .Where(static attempt => attempt.Species == LoopClosureLinkSpecies.ExecutedDivergence)
                .SingleOrDefault(attempt => attempt.EventID.Value == pending.TerminalOutcomeEventID)
                .ChildOutcome;
            ObjectLoopClosedCorroboration corroboration = new(
                pending.OutcomeNodeID, new LoopClosureDigest(finalDigest),
                pending.PatternEvidenceSHA256, pending.DivergenceEvidenceSHA256,
                pending.TerminalOutcomeEventID, childOutcome);
            WriteLoopClosureObject(pending.OutcomeID, in corroboration);
        }
        _pendingLoopClosureObjects.Clear();
    }

    /// Record the exact rung-0 episode that was just emitted.  Fold capture consumes
    /// this queue; it never scans the whole tape or picks a global latest rung.
    internal void RegisterLoopClosureComposition(LoopLineageEdgeReceipt rung0)
    {
        if (!_loopLineageEnabled || _loopLineage is null || rung0.Node.Species != LoopLineageNodeSpecies.Rung0Composition)
            return;
        LoopClosureCompositionEpisode episode = CreateLoopClosureEpisode(rung0);
        if (_pendingLoopClosureEpisodes.All(candidate => candidate.EpisodeDigest != episode.EpisodeDigest))
            _pendingLoopClosureEpisodes.Add(episode);
    }

    private LoopClosureCompositionEpisode CreateLoopClosureEpisode(LoopLineageEdgeReceipt rung0)
    {
        if (_loopLineage is null || rung0.Node.Species != LoopLineageNodeSpecies.Rung0Composition)
            throw new InvalidDataException("loop-closure episode requires a rung-0 lineage edge");
        List<TapeEventID> evidence = new();
        foreach (LoopLineageNodeID predecessorID in rung0.PredecessorIDs)
            if (_loopLineage.Receipts.FirstOrDefault(edge => edge.Node.NodeID == predecessorID) is { } predecessor)
                evidence.Add(predecessor.Node.EventID);
        if (evidence.Count == 0) throw new InvalidDataException("rung-0 lineage episode has no typed opportunity evidence");
        GrammarRevisionID preFold = rung0.Node.GrammarRevision
            ?? InstallRevision?.Revision
            ?? throw new InvalidDataException("rung-0 lineage episode has no pre-fold grammar revision");
        return LoopClosureCompositionEpisode.Create(
            new LoopClosureCompositionEpisodeID(rung0.Node.NodeID.Value), rung0.Node.EventID, evidence, preFold);
    }

    internal bool TryCreateLoopClosureTeacher(
        GrammarRevisionID learnedReadoutRevision,
        out LoopClosureTeacherPacketProvenance teacher)
    {
        teacher = default;
        if (!_loopLineageEnabled || _loopLineage is null || learnedReadoutRevision == GrammarRevisionID.Zero) return false;
        for (int index = _loopClosureFolds.Count - 1; index >= 0; index--)
        {
            (LoopClosureCompositionEpisode episode, GrammarFoldProvenanceReceipt fold) = _loopClosureFolds[index];
            if (fold.Revision.CompareTo(learnedReadoutRevision) >= 0) continue;
            if (TryFindConsumedLoopClosureTeacher(episode, in fold, out teacher, out _)) return true;
        }
        // Bootstrap the first teacher from the source fold. It cannot certify that
        // fold yet; its packet is queued and a later publication must consume it.
        for (int index = _loopClosureFolds.Count - 1; index >= 0; index--)
        {
            (LoopClosureCompositionEpisode episode, GrammarFoldProvenanceReceipt fold) = _loopClosureFolds[index];
            if (fold.Revision.CompareTo(learnedReadoutRevision) >= 0) continue;
            TapeEventID[] episodeEvents = LoopClosureCompositionEpisode.NormalizeEventIDs(
                [episode.CompositionEventID, .. episode.EvidenceEventIDs]);
            teacher = LoopClosureTeacherPacketProvenance.Create(episode.EpisodeID, fold.Revision, episodeEvents, episode.EvidenceDigest);
            return true;
        }
        return false;
    }

    internal void RegisterLoopClosureTeacher(
        in LoopClosureTeacherPacketProvenance teacher,
        TapeEventID eventID)
    {
        if (!_loopLineageEnabled || eventID.Value < 0) return;
        teacher.Validate();
        if (_pendingLoopClosureTeachers.Any(pending => pending.EventID == eventID)) return;
        _pendingLoopClosureTeachers.Add(new PendingLoopClosureTeacher(teacher, eventID));
    }

    internal bool TryCreateLoopClosureR4(
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        in GrammarPolicyContextKey context,
        GrammarRevisionID learnedReadoutRevision,
        ulong candidateFingerprint,
        ulong supportDigest,
        CortexPolicyDecisionID decisionID,
        TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance)
    {
        provenance = default;
        if (!_loopLineageEnabled || learnedReadoutRevision == GrammarRevisionID.Zero || supportDigest == 0
            || candidateFingerprint == 0 || decisionID.Value == 0 || decisionEventID.Value < 0
            || !policy.Value.Any() || !context.IsCanonical || !canonicalState.Policy.Equals(policy)) return false;
        for (int index = _loopClosureFolds.Count - 1; index >= 0; index--)
        {
            (LoopClosureCompositionEpisode episode, GrammarFoldProvenanceReceipt fold) = _loopClosureFolds[index];
            if (fold.Revision.CompareTo(learnedReadoutRevision) >= 0) continue;
            if (!TryFindConsumedLoopClosureTeacher(episode, in fold, out LoopClosureTeacherPacketProvenance teacher, out TapeEventID teacherPacketEventID)) continue;
            ReadoutTrainingCorroboration training = ReadoutTrainingCorroboration.Create(
                policy, teacherPacketEventID, episode.CompositionEventID, episode.EvidenceEventIDs,
                episode.EvidenceDigest, episode.EpisodeID, episode.EpisodeDigest,
                fold.PreviousRevision, fold.Revision, fold.ConsumedEventIDs,
                fold.ConsumedEventDigest, fold.ReceiptDigest, in canonicalState, in context,
                candidateFingerprint, supportDigest, learnedReadoutRevision, decisionID, decisionEventID);
            provenance = LoopClosureR4Provenance.Create(episode, fold, teacher, in training);
            return true;
        }
        return false;
    }

    /// Resolve the generic learned-readout custody for a policy decision.  This is
    /// the authority seam for domain receipts: the caller supplies only its typed
    /// candidate identity and consumes the same decision packet, teacher packet,
    /// derivation episode, and fold that generic Cortex already sealed.
    internal bool TryCreatePolicyReadoutCustody(
        in CortexPolicyDecision decision,
        out TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance)
    {
        decisionEventID = new TapeEventID(-1);
        provenance = default;
        if (decision.SelectionCause != CortexPolicySelectionCauses.GrammarCandidate
            || decision.Authority != CortexPolicyAuthorities.Grammar
            || decision.Readout.GrammarRevision == GrammarRevisionID.Zero
            || decision.Readout.ReadoutCandidateFingerprint == 0
            || decision.Readout.ReadoutCandidateOccurrenceDigest == 0
            || !TryFindPolicyDecisionEvent(in decision, out decisionEventID))
            return false;
        if (!decision.ReadoutContext.IsCanonical)
            return false;
        PolicyCanonicalStateID canonicalState = decision.ReadoutContext.CanonicalState;
        if (!_policyBoundaryDomains.TryGetValue(decision.Policy, out IPolicyBoundaryDomain domain)
            || !domain.ValidateCanonicalState(in canonicalState)
            || !TryCreateLoopClosureR4(
                decision.Policy,
                in canonicalState,
                decision.ReadoutContext,
                decision.Readout.GrammarRevision,
                decision.Readout.ReadoutCandidateFingerprint,
                decision.Readout.ReadoutCandidateOccurrenceDigest,
                decision.DecisionID,
                decisionEventID,
                out provenance))
        {
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            return false;
        }
        if (_loopLineage is not LoopLineageTurnstile lineage
            || !TryGetLoopClosureReadoutBinding(provenance.Teacher,
                out LoopLineageNode predecessor, out LoopLineageCausalID causalID))
        {
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            return false;
        }
        TapeEventID learnedEventID = decisionEventID;
        LoopLineageEdgeReceipt[] learned = lineage.Receipts
            .Where(edge => edge.Node.Species == LoopLineageNodeSpecies.LearnedReadout
                && edge.Node.EventID == learnedEventID)
            .ToArray();
        if (learned.Length == 0)
        {
            if (!lineage.TryEmit(Step, LoopLineageNodeSpecies.LearnedReadout, decisionEventID,
                    decision.Readout.GrammarRevision, [predecessor.NodeID], causalID))
            {
                decisionEventID = new TapeEventID(-1);
                provenance = default;
                return false;
            }
            learned = [lineage.Receipts[^1]];
        }
        if (learned.Length != 1
            || learned[0].Node.GrammarRevision != decision.Readout.GrammarRevision
            || learned[0].PredecessorIDs.Count != 1
            || learned[0].PredecessorIDs[0] != predecessor.NodeID
            || learned[0].Node.CausalID != causalID)
        {
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            return false;
        }
        BindLoopClosurePolicyRail(decision.Policy, decision.ReadoutIdentity,
            decision.Readout.GrammarRevision, causalID, learned[0].Node.NodeID);
        return true;
    }

    private bool TryFindConsumedLoopClosureTeacher(
        in LoopClosureCompositionEpisode episode,
        in GrammarFoldProvenanceReceipt consumingFold,
        out LoopClosureTeacherPacketProvenance teacher,
        out TapeEventID teacherPacketEventID)
    {
        teacher = default;
        teacherPacketEventID = default;
        if (_runtimeTape is null) return false;
        TapeEventID[] expectedEvents = LoopClosureCompositionEpisode.NormalizeEventIDs(
            [episode.CompositionEventID, .. episode.EvidenceEventIDs]);
        List<(LoopClosureTeacherPacketProvenance Teacher, TapeEventID EventID)> matches = new();
        foreach (TapeEventID eventID in consumingFold.ConsumedEventIDs)
        {
            if (!_runtimeTape.Resolve(eventID, out byte[] payload)
                || !Encoding.ASCII.GetString(payload).Contains("\tFOLD-REVISION=", StringComparison.Ordinal)) continue;
            try
            {
                LoopClosureTeacherPacketProvenance candidate = LoopClosureTeacherPacketProvenance.DecodePacketFields(payload);
                if (candidate.EpisodeID != episode.EpisodeID
                    || !(candidate.FoldRevision < consumingFold.Revision)
                    || !candidate.MatchedEventIDs.SequenceEqual(expectedEvents)
                    || candidate.EvidenceDigest != episode.EvidenceDigest) continue;
                matches.Add((candidate, eventID));
            }
            catch (InvalidDataException) { }
        }
        if (matches.Count != 1) return false;
        (teacher, teacherPacketEventID) = matches[0];
        return true;
    }

    /// Create the fold receipt at the publication owner, before the publication is
    /// constructed. This is the only production path that consumes pending episodes
    /// and teacher packets; a post-publication scan would detach the receipt from the
    /// grammar it claims.
    internal bool TryCreateLoopClosureFold(
        GrammarRevisionID parentRevision,
        GrammarRevisionID revision,
        out GrammarFoldProvenanceReceipt fold)
    {
        fold = default;
        if (!_loopLineageEnabled || _loopLineage is null || _runtimeTape is null || parentRevision == GrammarRevisionID.Zero)
            return false;
        // InstallRevisions are ordinary grammar revisions, not a dedicated loop-closure
        // cadence.  A teacher can be emitted after its source fold while one or more
        // ordinary publications advance the parent revision; the first subsequent
        // publication still consumes that teacher.  Requiring equality here leaves
        // the teacher permanently pending once the parent moves from (for example)
        // rev5 to rev7 before the next fold seam.
        LoopClosureCompositionEpisode[] pendingEpisodes = _pendingLoopClosureEpisodes
            .Where(episode => episode.PreFoldRevision.CompareTo(parentRevision) <= 0)
            .ToArray();
        PendingLoopClosureTeacher[] pendingTeachers = _pendingLoopClosureTeachers
            .Where(pending => pending.Teacher.FoldRevision.CompareTo(parentRevision) <= 0)
            .ToArray();
        LoopClosureCompositionEpisode[] teacherEpisodes = pendingTeachers
            .Select(pending => _loopClosureFolds.FirstOrDefault(item =>
                item.Fold.Revision == pending.Teacher.FoldRevision
                && item.Episode.EpisodeID == pending.Teacher.EpisodeID).Episode)
            .Where(static episode => episode.EpisodeID.IsValid)
            .ToArray();
        LoopClosureCompositionEpisode[] episodes = pendingEpisodes
            .Concat(teacherEpisodes)
            .GroupBy(static episode => episode.EpisodeDigest.Value, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        if (episodes.Length == 0) return false;
        TapeEventID[] consumed = episodes.SelectMany(static episode =>
                new[] { episode.CompositionEventID }.Concat(episode.EvidenceEventIDs))
            .Concat(pendingTeachers.Select(static pending => pending.EventID))
            .Distinct().OrderBy(static id => id.Value).ToArray();
        GrammarFoldProvenanceReceipt createdFold = GrammarFoldProvenanceReceipt.Create(parentRevision, revision, consumed, episodes);
        fold = createdFold;
        if (_loopClosureFolds.Any(item => item.Fold.Revision == createdFold.Revision)) return true;
        foreach (LoopClosureCompositionEpisode episode in episodes)
            _loopClosureFolds.Add((episode, createdFold));
        _pendingLoopClosureEpisodes.RemoveAll(episode => episodes.Any(selected => selected.EpisodeDigest == episode.EpisodeDigest));
        _pendingLoopClosureTeachers.RemoveAll(pending => pendingTeachers.Any(selected => selected.EventID == pending.EventID));
        return true;
    }

    /// Preserve loop-closure publication/pending state inside the ordinary policy
    /// checkpoint section.  Tape packets remain the event authority; this state is
    /// only the in-flight join index needed when a kill lands between fold, readout,
    /// and terminal object settlement.
    internal void SaveLoopClosureState(CkptWriter writer)
    {
        writer.Bool(_loopLineageEnabled);
        writer.I32(_loopClosureFolds.Count);
        foreach ((LoopClosureCompositionEpisode episode, GrammarFoldProvenanceReceipt fold) in _loopClosureFolds
                     .OrderBy(static item => item.Fold.Revision.Value)
                     .ThenBy(static item => item.Episode.EpisodeID.Value, StringComparer.Ordinal))
        {
            WriteLoopClosureEpisode(writer, in episode);
            WriteLoopClosureFold(writer, in fold);
        }
        writer.I32(_pendingLoopClosureEpisodes.Count);
        foreach (LoopClosureCompositionEpisode episode in _pendingLoopClosureEpisodes
                     .OrderBy(static item => item.EpisodeDigest.Value, StringComparer.Ordinal))
            WriteLoopClosureEpisode(writer, in episode);
        writer.I32(_pendingLoopClosureTeachers.Count);
        foreach (PendingLoopClosureTeacher pending in _pendingLoopClosureTeachers
                     .OrderBy(static item => item.Teacher.FoldRevision.Value)
                     .ThenBy(static item => item.Teacher.EpisodeID.Value, StringComparer.Ordinal)
                     .ThenBy(static item => item.EventID.Value))
        {
            LoopClosureTeacherPacketProvenance teacher = pending.Teacher;
            WriteLoopClosureTeacher(writer, in teacher);
            writer.I64(pending.EventID.Value);
        }
        writer.I32(_loopClosureTheories.Count);
        foreach (PatternBecameThoughtCorroboration theory in _loopClosureTheories.Values
                     .OrderBy(static item => item.CompositionNodeID.Value, StringComparer.Ordinal))
        {
            writer.I64(theory.SourcePredictionID.Value); writer.I64(theory.ComposedPredictionID.Value);
            writer.Str(theory.CompositionNodeID.Value); writer.Str(theory.ProofSHA256.Value); writer.Str(theory.AuditSHA256.Value);
            writer.I64(theory.MainEvaluatorDelta); writer.I64(theory.NumericEvaluatorDelta);
            writer.I32((int)theory.TargetSpecies); writer.I32(theory.SupportEventIDs.Length);
            foreach (TapeEventID eventID in theory.SupportEventIDs) writer.I64(eventID.Value);
            writer.I32(theory.BasisLawAdmissionIDs.Length);
            foreach (string admissionID in theory.BasisLawAdmissionIDs) writer.Str(admissionID);
        }
        writer.I32(_pendingLoopClosureObjects.Count);
        foreach (PendingLoopClosureObject pending in _pendingLoopClosureObjects
                     .OrderBy(static item => item.TerminalOutcomeEventID))
        {
            writer.Str(pending.OutcomeID); writer.Str(pending.OutcomeNodeID.Value);
            writer.Str(pending.PatternEvidenceSHA256.Value); writer.Str(pending.DivergenceEvidenceSHA256.Value);
            writer.I64(pending.TerminalOutcomeEventID);
        }
        writer.I32(_loopClosurePolicyRails.Count);
        foreach (var row in _loopClosurePolicyRails
                     .OrderBy(static row => row.Key.Policy.Value, StringComparer.Ordinal)
                     .ThenBy(static row => row.Key.Fingerprint.Value)
                     .ThenBy(static row => row.Key.Revision.Value))
        {
            writer.Str(row.Key.Policy.Value); writer.U64(row.Key.Fingerprint.Value); writer.U64(row.Key.Revision.Value);
            writer.Str(row.Value.CausalID.Value); writer.Str(row.Value.ReadoutNodeID.Value);
        }
        writer.U32(LoopClosureLinkStateMagic);
        writer.I32(_loopClosureLinkAttempts.Count);
        foreach (LoopClosureLinkAttempt attempt in _loopClosureLinkAttempts
                     .OrderBy(static row => row.EventID.Value)
                     .ThenBy(static row => row.Species)
                     .ThenBy(static row => row.RecordID, StringComparer.Ordinal))
            writer.Bytes(LoopClosureLinkAttemptStore.EncodeCheckpoint(in attempt));
    }

    internal void LoadLoopClosureState(CkptReader reader)
    {
        bool enabled = reader.Bool();
        if (enabled) _loopLineageEnabled = true;
        _loopClosureFolds.Clear(); _pendingLoopClosureEpisodes.Clear(); _pendingLoopClosureTeachers.Clear(); _loopClosureTheories.Clear();
        _pendingLoopClosureObjects.Clear(); _loopClosurePolicyRails.Clear();
        int foldCount = ReadLoopClosureCount(reader, "fold");
        for (int index = 0; index < foldCount; index++)
        {
            LoopClosureCompositionEpisode episode = ReadLoopClosureEpisode(reader);
            GrammarFoldProvenanceReceipt fold = ReadLoopClosureFold(reader);
            _loopClosureFolds.Add((episode, fold));
        }
        int pendingCount = ReadLoopClosureCount(reader, "pending episode");
        for (int index = 0; index < pendingCount; index++) _pendingLoopClosureEpisodes.Add(ReadLoopClosureEpisode(reader));
        int pendingTeacherCount = ReadLoopClosureCount(reader, "pending teacher");
        for (int index = 0; index < pendingTeacherCount; index++)
            _pendingLoopClosureTeachers.Add(new PendingLoopClosureTeacher(ReadLoopClosureTeacher(reader), new TapeEventID(reader.I64())));
        int theoryCount = ReadLoopClosureCount(reader, "theory");
        for (int index = 0; index < theoryCount; index++)
        {
            PatternBecameThoughtCorroboration theory = new(
                new EmlPredictionID(checked((int)reader.I64())), new EmlPredictionID(checked((int)reader.I64())), new LoopLineageNodeID(reader.Str()),
                new LoopClosureDigest(reader.Str()), new LoopClosureDigest(reader.Str()), reader.I64(), reader.I64(),
                ReadPatternTargetSpecies(reader),
                ReadPatternSupportEvents(reader), ReadPatternLawAdmissions(reader));
            theory.Validate(requireCorroboration: true);
            _loopClosureTheories.Add(theory.CompositionNodeID, theory);
        }
        int pendingObjectCount = ReadLoopClosureCount(reader, "pending object");
        for (int index = 0; index < pendingObjectCount; index++)
            _pendingLoopClosureObjects.Add(new PendingLoopClosureObject(
                reader.Str(), new LoopLineageNodeID(reader.Str()), new LoopClosureDigest(reader.Str()),
                new LoopClosureDigest(reader.Str()), reader.I64()));
        int railCount = ReadLoopClosureCount(reader, "policy rail");
        for (int index = 0; index < railCount; index++)
        {
            CortexPolicyID policy = new(reader.Str()); CortexPolicyReadoutFingerprint fingerprint = new(reader.U64());
            GrammarRevisionID revision = new(reader.U64());
            LoopLineageCausalID causalID = new(reader.Str());
            LoopLineageNodeID readoutNodeID = new(reader.Str());
            if (!causalID.IsValid) throw new InvalidDataException("checkpoint carries an invalid loop-closure policy rail");
            if (!readoutNodeID.IsValid || revision == GrammarRevisionID.Zero)
                throw new InvalidDataException("checkpoint carries an incomplete loop-closure policy rail");
            _loopClosurePolicyRails.Add((Policy: policy, Fingerprint: fingerprint, Revision: revision), new(causalID, readoutNodeID));
        }
        _loopClosureLinkAttempts.Clear();
        _loopClosureLinkCheckpointCursor = 0;
        if (!reader.TryExpect(LoopClosureLinkStateMagic)) return;
        int linkCount = ReadLoopClosureCount(reader, "link attempt");
        for (int index = 0; index < linkCount; index++)
        {
            LoopClosureLinkAttempt attempt = LoopClosureLinkAttemptStore.DecodeCheckpoint(reader.Bytes(1_000_000));
            if (_loopClosureLinkAttempts.Any(existing => existing.RecordID == attempt.RecordID))
                throw new InvalidDataException("checkpoint repeats a loop-closure link attempt");
            _loopClosureLinkAttempts.Add(attempt);
        }
        _loopClosureLinkCheckpointCursor = _loopClosureLinkAttempts.Count;
        if (_loopClosureLinkAttempts.Count > 1)
        {
            LoopClosureLinkAttempt[] ordered = _loopClosureLinkAttempts.OrderBy(static row => row.EventID.Value).ToArray();
            if (!ordered.Select(static row => row.EventID).SequenceEqual(_loopClosureLinkAttempts.Select(static row => row.EventID)))
                throw new InvalidDataException("checkpoint loop-closure links are not in tape order");
        }
    }

    private static EmlObligationTargetSpecies ReadPatternTargetSpecies(CkptReader reader)
    {
        EmlObligationTargetSpecies species = (EmlObligationTargetSpecies)reader.I32();
        if (!Enum.IsDefined(species))
            throw new InvalidDataException("checkpoint carries an invalid theory target species");
        return species;
    }

    private static int ReadLoopClosureCount(CkptReader reader, string role)
    {
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"checkpoint carries an invalid loop-closure {role} count");
        return count;
    }

    private static TapeEventID[] ReadPatternSupportEvents(CkptReader reader)
    {
        int count = ReadLoopClosureCount(reader, "theory support");
        TapeEventID[] events = new TapeEventID[count];
        for (int index = 0; index < count; index++) events[index] = new TapeEventID(reader.I64());
        return events;
    }

    private static string[] ReadPatternLawAdmissions(CkptReader reader)
    {
        int count = ReadLoopClosureCount(reader, "theory law admission");
        string[] admissions = new string[count];
        for (int index = 0; index < count; index++) admissions[index] = reader.Str();
        return admissions;
    }

    private static void WriteLoopClosureEpisode(CkptWriter writer, in LoopClosureCompositionEpisode episode)
    {
        episode.Validate(); writer.Str(episode.EpisodeID.Value); writer.I64(episode.CompositionEventID.Value);
        writer.I32(episode.EvidenceEventIDs.Length); foreach (TapeEventID id in episode.EvidenceEventIDs) writer.I64(id.Value);
        writer.U64(episode.PreFoldRevision.Value); writer.Str(episode.EvidenceDigest.Value); writer.Str(episode.EpisodeDigest.Value);
    }

    private static LoopClosureCompositionEpisode ReadLoopClosureEpisode(CkptReader reader)
    {
        LoopClosureCompositionEpisodeID episodeID = new(reader.Str());
        TapeEventID derivation = new(reader.I64());
        int count = ReadLoopClosureCount(reader, "episode evidence");
        TapeEventID[] evidence = new TapeEventID[count]; for (int index = 0; index < count; index++) evidence[index] = new TapeEventID(reader.I64());
        return new(episodeID, derivation, evidence,
            new GrammarRevisionID(reader.U64()), new LoopClosureDigest(reader.Str()), new LoopClosureDigest(reader.Str()));
    }

    private static void WriteLoopClosureTeacher(CkptWriter writer, in LoopClosureTeacherPacketProvenance teacher)
    {
        teacher.Validate();
        writer.Str(teacher.EpisodeID.Value); writer.U64(teacher.FoldRevision.Value);
        writer.I32(teacher.MatchedEventIDs.Length);
        foreach (TapeEventID id in teacher.MatchedEventIDs) writer.I64(id.Value);
        writer.Str(teacher.EvidenceDigest.Value); writer.Str(teacher.CorroborationDigest.Value); writer.Str(teacher.ProvenanceDigest.Value);
    }

    private static LoopClosureTeacherPacketProvenance ReadLoopClosureTeacher(CkptReader reader)
    {
        LoopClosureTeacherPacketProvenance teacher = new(
            new LoopClosureCompositionEpisodeID(reader.Str()), new GrammarRevisionID(reader.U64()),
            ReadLoopClosureEventIDs(reader, "teacher event"), new LoopClosureDigest(reader.Str()),
            new LoopClosureDigest(reader.Str()), new LoopClosureDigest(reader.Str()));
        teacher.Validate();
        return teacher;
    }

    private static TapeEventID[] ReadLoopClosureEventIDs(CkptReader reader, string role)
    {
        int count = ReadLoopClosureCount(reader, role);
        TapeEventID[] ids = new TapeEventID[count];
        for (int index = 0; index < count; index++) ids[index] = new TapeEventID(reader.I64());
        return ids;
    }

    private static void WriteLoopClosureFold(CkptWriter writer, in GrammarFoldProvenanceReceipt fold)
    {
        fold.Validate(); writer.U64(fold.PreviousRevision.Value); writer.U64(fold.Revision.Value);
        writer.I32(fold.ConsumedEventIDs.Length); foreach (TapeEventID id in fold.ConsumedEventIDs) writer.I64(id.Value);
        writer.I32(fold.CompositionEpisodeDigests.Length); foreach (LoopClosureDigest digest in fold.CompositionEpisodeDigests) writer.Str(digest.Value);
        writer.Str(fold.ConsumedEventDigest.Value); writer.Str(fold.ReceiptDigest.Value);
    }

    private static GrammarFoldProvenanceReceipt ReadLoopClosureFold(CkptReader reader)
    {
        GrammarRevisionID previous = new(reader.U64()); GrammarRevisionID revision = new(reader.U64());
        int consumedCount = ReadLoopClosureCount(reader, "fold event");
        TapeEventID[] consumed = new TapeEventID[consumedCount]; for (int index = 0; index < consumedCount; index++) consumed[index] = new TapeEventID(reader.I64());
        int episodeCount = ReadLoopClosureCount(reader, "fold episode");
        LoopClosureDigest[] episodes = new LoopClosureDigest[episodeCount]; for (int index = 0; index < episodeCount; index++) episodes[index] = new LoopClosureDigest(reader.Str());
        return new(previous, revision, consumed, episodes, new LoopClosureDigest(reader.Str()), new LoopClosureDigest(reader.Str()));
    }

    internal void BindLoopLineage(Tape tape, Journal journal, Func<Tape, TapeEventID, bool>? worldRootPredicate = null)
    {
        if (!_loopLineageEnabled) return;
        // An OMITTED predicate means "keep what is installed", never "reset to the corpus default".
        // BindRuntime rebinds without one — it has no curriculum in hand — and treating that as a
        // reset silently swapped the native crawler's world-root predicate for the corpus one after
        // the loop had installed it. The tape then held perfectly valid repository:world roots that
        // no lineage edge could ever cite, so the native runner could not mint its first edge and
        // every arm of the assay died upstream of its own question.
        bool predicateChanged = worldRootPredicate is not null && !Equals(worldRootPredicate, _loopLineageWorldRootPredicate);
        if (predicateChanged) _loopLineageWorldRootPredicate = worldRootPredicate;
        bool rebound = _loopLineage is not null && !_loopLineage.IsBoundTo(tape, journal);
        int previousReceipts = _loopLineage?.Receipts.Count ?? 0;
        if (_loopLineage is null || rebound || predicateChanged)
            _loopLineage = new LoopLineageTurnstile(tape, journal, _loopLineageWorldRootPredicate);
        else _loopLineage.RestoreFromTape();
        if (rebound)
            Trace.Cortex.Boundary("loop-lineage.rebind",
                $"previous_receipts={previousReceipts} current_receipts={_loopLineage.Receipts.Count}");
        RestoreLoopClosurePolicyRails(tape);
    }

    internal bool VerifyLoopClosureReadoutBindingAfterLoad(
        Tape tape,
        Journal journal,
        string directory,
        GrammarRevisionID learnedReadoutRevision,
        out LoopClosureTeacherPacketProvenance teacher,
        out LoopLineageNode predecessor)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        EnableLoopLineage();
        _runtimeTape = tape;
        _runtimeJournal = journal;
        BindLoopLineage(tape, journal);
        RestoreLoopClosureFolds(directory);
        if (!TryCreateLoopClosureTeacher(learnedReadoutRevision, out teacher))
        {
            predecessor = default;
            return false;
        }
        return TryGetLoopClosureReadoutBinding(in teacher, out predecessor, out _);
    }

    internal bool VerifyLoopClosureFirstPolicyDecisionAfterLoad(
        Tape tape,
        Journal journal,
        string directory,
        in CortexPolicyDecision decision,
        in PolicyCanonicalStateID canonicalState,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        out LoopLineageNode learnedReadout)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        EnableLoopLineage();
        _runtimeTape = tape;
        _runtimeJournal = journal;
        BindLoopLineage(tape, journal);
        RestoreLoopClosureFolds(directory);
        GrammarPolicyContextKey context = new(in canonicalState, actionCount, 0);
        AppendPolicyDecision(in decision, in canonicalState, features, actionCount, in context);
        learnedReadout = _loopLineage?.Receipts.LastOrDefault()?.Node ?? default;
        return learnedReadout.Species == LoopLineageNodeSpecies.LearnedReadout;
    }

    internal void RestoreLoopClosureFolds(string directory)
    {
        if (!_loopLineageEnabled || string.IsNullOrWhiteSpace(directory)) return;
        string runID = Path.GetFileName(Path.GetFullPath(directory));
        HashSet<string> derivedFoldReceipts = _loopClosureFolds
            .Select(static item => item.Fold.ReceiptDigest.Value).ToHashSet(StringComparer.Ordinal);
        RehydrateLoopClosureFoldsFromTape();
        foreach (string receipt in derivedFoldReceipts)
            if (!_loopClosureFolds.Any(item => item.Fold.ReceiptDigest.Value == receipt))
                throw new InvalidDataException($"checkpoint fold {receipt} has no authoritative grammar-fold publication");
        foreach (PatternBecameThoughtCorroboration theory in LoopClosureEvidenceStore.ReadPattern(directory, runID))
            _loopClosureTheories[theory.CompositionNodeID] = theory;
        foreach (LoopClosureR4Provenance provenance in LoopClosureEvidenceStore.ReadR4(directory, runID))
        {
            if (!_loopClosureFolds.Any(item => item.Fold.ReceiptDigest == provenance.Fold.ReceiptDigest))
                throw new InvalidDataException($"R4 fold {provenance.Fold.Revision.Value} has no authoritative grammar-fold publication");
            if (_loopClosureFolds.Any(item => item.Fold.ReceiptDigest == provenance.Fold.ReceiptDigest)) continue;
            _loopClosureFolds.Add((provenance.Episode, provenance.Fold));
        }
        if (_loopLineage is null) return;
        HashSet<LoopClosureCompositionEpisodeID> folded = _loopClosureFolds
            .Select(static item => item.Episode.EpisodeID).ToHashSet();
        foreach (LoopLineageEdgeReceipt rung0 in _loopLineage.Receipts.Where(static edge =>
                     edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition))
        {
            LoopClosureCompositionEpisodeID episodeID = new(rung0.Node.NodeID.Value);
            if (folded.Contains(episodeID)
                || !_loopLineage.Receipts.Any(edge => edge.Node.Species == LoopLineageNodeSpecies.DisplacedEvaluation
                    && edge.PredecessorIDs.Count == 1 && edge.PredecessorIDs[0] == rung0.Node.NodeID)) continue;
            LoopClosureCompositionEpisode episode = CreateLoopClosureEpisode(rung0);
            if (_pendingLoopClosureEpisodes.All(candidate => candidate.EpisodeDigest != episode.EpisodeDigest))
                _pendingLoopClosureEpisodes.Add(episode);
        }
    }

    /// Rebuild the fold index from the ordinary grammar publication packets. The
    /// checkpoint/sidecar copy is derived state: it may accelerate a load, but it
    /// cannot be the authority that binds a learned readout to its teacher fold.
    private void RehydrateLoopClosureFoldsFromTape()
    {
        if (_runtimeTape is null || _loopLineage is null)
            throw new InvalidDataException("loop-closure fold rehydration requires the replayed tape and lineage");

        Dictionary<string, LoopClosureCompositionEpisode> episodesByDigest = new(StringComparer.Ordinal);
        foreach (LoopLineageEdgeReceipt rung0 in _loopLineage.Receipts.Where(static edge =>
                     edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition))
        {
            LoopClosureCompositionEpisode episode = CreateLoopClosureEpisode(rung0);
            if (!episodesByDigest.TryAdd(episode.EpisodeDigest.Value, episode)
                && episodesByDigest[episode.EpisodeDigest.Value].EpisodeID != episode.EpisodeID)
                throw new InvalidDataException($"grammar fold episode digest collides for {episode.EpisodeID.Value}");
        }

        List<(LoopClosureCompositionEpisode Episode, GrammarFoldProvenanceReceipt Fold)> authoritative = [];
        HashSet<string> receipts = new(StringComparer.Ordinal);
        foreach (TapeEventView view in _runtimeTape.GetEventViews().OrderBy(static view => view.Id.Value))
        {
            if (!_runtimeTape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodeGrammarFoldInstallRevision(payload, out GrammarFoldProvenanceReceipt fold)) continue;
            if (!receipts.Add(fold.ReceiptDigest.Value)) continue;
            foreach (LoopClosureDigest episodeDigest in fold.CompositionEpisodeDigests)
            {
                if (!episodesByDigest.TryGetValue(episodeDigest.Value, out LoopClosureCompositionEpisode episode))
                    throw new InvalidDataException($"grammar fold {fold.Revision.Value} names an unknown derivation episode {episodeDigest.Value}");
                if (!_loopLineage.Receipts.Any(edge => edge.Node.Species == LoopLineageNodeSpecies.DisplacedEvaluation
                    && edge.PredecessorIDs.Count == 1 && edge.PredecessorIDs[0] == new LoopLineageNodeID(episode.EpisodeID.Value)))
                    throw new InvalidDataException($"grammar fold {fold.Revision.Value} episode {episode.EpisodeID.Value} has no exact displaced predecessor");
                authoritative.Add((episode, fold));
            }
        }

        _loopClosureFolds.Clear();
        _loopClosureFolds.AddRange(authoritative);
    }
}
