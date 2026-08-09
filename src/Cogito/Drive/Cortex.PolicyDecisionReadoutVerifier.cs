namespace Cogito;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Cogito.Grammar;

internal readonly record struct CortexPolicyDecisionReadoutOccurrenceCheck(
    bool Passed,
    int PacketRows,
    int JournalRows,
    bool ReadoutRowsExact,
    bool NullSemanticsExact,
    bool ResumeExact,
    bool BehavioralRowsExact);

internal static class CortexPolicyDecisionReadoutVerifier
{
    internal const string ReceiptFile = "policy_decisions.tsv";

    internal static CortexPolicyDecisionReadoutOccurrenceCheck Verify(string runDirectory, TextWriter output)
    {
        using CortexPolicyOccurrenceCheckBundle bundle = new(runDirectory);
        return Verify(bundle, output);
    }

    internal static CortexPolicyDecisionReadoutOccurrenceCheck Verify(CortexPolicyOccurrenceCheckBundle bundle, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(output);
        string[] receiptLines = bundle.DecisionReceiptLines;
        string[] journalLines = bundle.JournalLines;
        Tape tape = bundle.Tape;
        Dictionary<TapeEventID, string> eventSources = new();
        foreach (TapeEventView view in tape.GetEventViews()) eventSources[view.Id] = view.Source;
        Dictionary<string, string> journalByEvent = new(StringComparer.Ordinal);
        for (int i = 0; i < journalLines.Length; i++)
        {
            string[] columns = journalLines[i].Split('\t');
            if (columns.Length > 3 && columns[1] == "policy-decision")
            {
                if (!journalByEvent.TryAdd(columns[2], journalLines[i]))
                    throw new InvalidDataException($"duplicate policy journal event '{columns[2]}'");
            }
        }

        bool rowsExact = true;
        bool packetExact = true;
        bool nullExact = true;
        bool behavioralExact = true;
        bool resumeExact = true;
        HashSet<string> decisions = new(StringComparer.Ordinal);
        HashSet<string> events = new(StringComparer.Ordinal);
        int packetRows = 0;
        int journalRows = journalByEvent.Count;
        for (int i = 1; i < receiptLines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(receiptLines[i])) continue;
            string[] c = receiptLines[i].Split('\t');
            if (c.Length != 14) throw new InvalidDataException($"policy decision readout row {i} has {c.Length} columns, expected 14");
            packetRows++;
            if (!events.Add(c[1])) throw new InvalidDataException($"duplicate policy decision event '{c[1]}'");
            if (!decisions.Add(c[2])) throw new InvalidDataException($"duplicate policy decision '{c[2]}'");
            int launchpad = ParseInt(c[4]);
            int raw = ParseInt(c[5]);
            int selected = ParseInt(c[6]);
            int executed = ParseInt(c[7]);
            int actionCount = ParseInt(c[8]);
            CortexPolicyAuthorities authority = ParseEnum<CortexPolicyAuthorities>(c[9]);
            _ = ulong.Parse(c[10], CultureInfo.InvariantCulture);
            CortexPolicySelectionCauses cause = ParseEnum<CortexPolicySelectionCauses>(c[11]);
            bool drill = c[12] == "1";
            CortexPolicyDecisionPacket packet;
            try
            {
                byte[] materialized = Convert.FromBase64String(c[13]);
                if (!TryParseEventID(c[1], out TapeEventID eventID) || !tape.Resolve(eventID, out byte[] resolved))
                    throw new InvalidDataException($"policy decision event '{c[1]}' does not resolve from Tape");
                packetExact &= resolved.AsSpan().SequenceEqual(materialized);
                if (!eventSources.TryGetValue(eventID, out string? source) || source != "policy:" + c[3])
                    throw new InvalidDataException($"policy decision event '{c[1]}' source does not identify policy '{c[3]}'");
                packet = TapePacketCreator.DecodePolicyDecision(resolved);
            }
            catch (Exception error) when (error is FormatException or InvalidDataException or OverflowException)
            {
                packetExact = false;
                continue;
            }
            packetExact &= packet.DecisionID.Value.ToString(CultureInfo.InvariantCulture) == c[2]
                && packet.ActionCount == actionCount
                && packet.Readout.LaunchpadAction == launchpad
                && packet.Readout.RawCandidateAction == raw
                && packet.Readout.SelectedCandidateAction == selected
                && packet.Readout.ExecutedAction == executed
                && packet.Readout.Authority.ToString() == c[9]
                && packet.Readout.GrammarRevision.Value.ToString(CultureInfo.InvariantCulture) == c[10]
                && packet.Readout.SelectionCause.ToString() == c[11]
                && (packet.Readout.RollbackDrill ? "1" : "0") == c[12];
            if (launchpad < 0 || launchpad >= actionCount || executed < 0 || executed >= actionCount)
                throw new InvalidDataException($"policy decision {c[2]} action is outside action-count {actionCount}");
            if (raw >= actionCount || selected >= actionCount || raw < -1 || selected < -1 || ((raw == -1) != (selected == -1)))
                throw new InvalidDataException($"policy decision {c[2]} candidate action is outside action-count {actionCount}");

            bool nulls = cause == CortexPolicySelectionCauses.Launchpad
                ? raw == -1 && selected == -1 && launchpad == executed && authority == CortexPolicyAuthorities.Launchpad
                : raw >= 0 && selected >= 0;
            nullExact &= nulls;
            bool causeShape = cause switch
            {
                CortexPolicySelectionCauses.Launchpad => !drill,
                CortexPolicySelectionCauses.ShadowCandidate => !drill && authority == CortexPolicyAuthorities.Shadow,
                CortexPolicySelectionCauses.GrammarCandidate => !drill && authority == CortexPolicyAuthorities.Grammar && selected == executed,
                CortexPolicySelectionCauses.TrialOverride => !drill && authority == CortexPolicyAuthorities.Grammar && selected != raw && selected == executed,
                CortexPolicySelectionCauses.RollbackDrill => drill && authority == CortexPolicyAuthorities.Grammar && selected == executed && selected != raw && selected != launchpad,
                _ => false,
            };
            rowsExact &= causeShape;

            if (!journalByEvent.TryGetValue(c[1], out string? journal))
            {
                rowsExact = false;
                continue;
            }
            rowsExact &= journal.Contains("decision=" + c[2], StringComparison.Ordinal)
                && journal.Contains("authority=" + c[9], StringComparison.Ordinal)
                && journal.Contains("revision=" + c[10], StringComparison.Ordinal)
                && journal.Contains("launchpad=" + c[4], StringComparison.Ordinal)
                && journal.Contains("raw=" + c[5], StringComparison.Ordinal)
                && journal.Contains("selected=" + c[6], StringComparison.Ordinal)
                && journal.Contains("action=" + c[7] + "/" + c[8], StringComparison.Ordinal)
                && journal.Contains("cause=" + c[11], StringComparison.Ordinal)
                && journal.Contains("drill=" + c[12], StringComparison.Ordinal);
            behavioralExact &= journal.Contains("authority=" + c[9], StringComparison.Ordinal)
                && journal.Contains("revision=" + c[10], StringComparison.Ordinal)
                && journal.Contains("action=" + c[7] + "/" + c[8], StringComparison.Ordinal);
        }
        rowsExact &= packetRows == journalRows;
        resumeExact = events.Count == packetRows && decisions.Count == packetRows;
        bool passed = packetRows > 0 && packetExact && rowsExact && nullExact && resumeExact && behavioralExact;
        output.WriteLine($"  policy readout · packets={packetRows} journals={journalRows} packet={(packetExact ? "exact" : "BROKEN")} journal={(rowsExact ? "exact" : "BROKEN")} nulls={(nullExact ? "exact" : "BROKEN")} resume={(resumeExact ? "exact" : "BROKEN")} behavior={(behavioralExact ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return new CortexPolicyDecisionReadoutOccurrenceCheck(passed, packetRows, journalRows, packetExact && rowsExact, nullExact, resumeExact, behavioralExact);
    }

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static bool TryParseEventID(string value, out TapeEventID eventID)
    {
        if (value.Length > 1 && value[0] == 's' && long.TryParse(value.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
        {
            eventID = new TapeEventID(id);
            return true;
        }
        eventID = default;
        return false;
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.Parse<T>(value, ignoreCase: false);

    internal static bool VerifyFixture(TextWriter output)
    {
        const int actionCount = 3;
        CortexPolicyID policy = new("fixture.policy");
        MetricSample[] features = [new(new MetricID(0), NumericValue.FromI64(1))];
        CortexPolicyDecisionReadout[] readouts =
        [
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad, new global::Cogito.Grammar.GrammarRevisionID(1)),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, 1, 1, 0, CortexPolicyAuthorities.Shadow, new global::Cogito.Grammar.GrammarRevisionID(1), readoutCandidateFingerprint: 0x1111222233334444),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, 1, 1, 1, CortexPolicyAuthorities.Grammar, new global::Cogito.Grammar.GrammarRevisionID(1), readoutCandidateFingerprint: 0x2222333344445555),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, 1, 2, 2, CortexPolicyAuthorities.Grammar, new global::Cogito.Grammar.GrammarRevisionID(1), readoutCandidateFingerprint: 0x3333444455556666, trialOverride: true),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, 1, 2, 2, CortexPolicyAuthorities.Grammar, new global::Cogito.Grammar.GrammarRevisionID(1), readoutCandidateFingerprint: 0x4444555566667777, rollbackDrill: true),
        ];
        using Tape tape = new();
        Journal journal = new();
        bool packetExact = true;
        bool teacherExact = true;
        bool checkpointExact = true;
        bool candidateFingerprintCustody = true;
        bool candidateFingerprintOmissionRejected = true;
        bool candidateFingerprintTamperRejected = true;
        for (int i = 0; i < readouts.Length; i++)
        {
            CortexPolicyDecision decision = new(new CortexPolicyDecisionID((ulong)i + 1), policy, readouts[i]);
            TapeEventID teacherID = TapePacketCreator.AppendPolicyExample(
                tape, journal, i, policy, readouts[i].LaunchpadAction, features, actionCount);
            TapeEventID eventID = TapePacketCreator.AppendPolicyDecision(tape, journal, i, in decision, features, actionCount, out _);
            if (!tape.Resolve(teacherID, out byte[] teacherBytes)) throw new InvalidDataException("fixture teacher packet did not resolve");
            byte[] teacherContinuation = TapePacketCreator.EncodePolicyGrammarContinuation(readouts[i].LaunchpadAction);
            teacherExact &= teacherID.Value < eventID.Value
                && teacherBytes.Length > teacherContinuation.Length
                && TapePacketCreator.ValidatePolicyGrammarContext(teacherBytes.AsSpan()[..^teacherContinuation.Length], policy, actionCount, features.Length)
                && teacherBytes.AsSpan()[^teacherContinuation.Length..].SequenceEqual(teacherContinuation)
                && (readouts[i].SelectedCandidateAction < 0
                    || !teacherBytes.AsSpan().SequenceEqual(TapePacketCreator.EncodePolicyGrammarContinuation(readouts[i].SelectedCandidateAction)));
            if (!tape.Resolve(eventID, out byte[] bytes)) throw new InvalidDataException("fixture packet did not resolve");
            CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(bytes);
            packetExact &= packet.DecisionID.Equals(decision.DecisionID) && packet.Readout == decision.Readout;
            candidateFingerprintCustody &= packet.Readout.ReadoutCandidateFingerprint == decision.Readout.ReadoutCandidateFingerprint;
            if (readouts[i].RawCandidateAction >= 0)
            {
                byte[] omitted = Encoding.ASCII.GetBytes(string.Join('\t', Encoding.ASCII.GetString(bytes).Split('\t').Where(field => !field.StartsWith("candidate-fingerprint=", StringComparison.Ordinal))));
                try { _ = TapePacketCreator.DecodePolicyDecision(omitted); candidateFingerprintOmissionRejected = false; }
                catch (InvalidDataException) { }
                byte[] tampered = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace(
                    $"candidate-fingerprint=u:{readouts[i].ReadoutCandidateFingerprint:X16}",
                    "candidate-fingerprint=u:DEADBEEFDEADBEEF", StringComparison.Ordinal));
                try
                {
                    CortexPolicyDecisionPacket tamperedPacket = TapePacketCreator.DecodePolicyDecision(tampered);
                    candidateFingerprintTamperRejected &= tamperedPacket.Readout.ReadoutCandidateFingerprint != decision.Readout.ReadoutCandidateFingerprint;
                }
                catch (InvalidDataException) { candidateFingerprintTamperRejected = false; }
            }
            using MemoryStream image = new();
            using (CkptWriter writer = new(image)) CortexPolicyDecisionCheckpoint.Write(writer, in decision);
            image.Position = 0;
            CortexPolicyDecision restored;
            using (CkptReader reader = new(image)) restored = CortexPolicyDecisionCheckpoint.Read(reader, policy, actionCount);
            checkpointExact &= restored.DecisionID.Equals(decision.DecisionID) && restored.Readout == decision.Readout;
        }
        byte[] corrupt = Encoding.ASCII.GetBytes("POLICY-DECISION\tdecision=u:0000000000000001");
        bool corruptionRejected;
        try { _ = TapePacketCreator.DecodePolicyDecision(corrupt); corruptionRejected = false; }
        catch (InvalidDataException) { corruptionRejected = true; }
        bool grammarReadout = VerifyGrammarReadoutFixture(policy, features, actionCount, output, out string grammarReadoutDetail);
        bool canonicalState = VerifyCanonicalStateFixture(output);
        bool canonicalEvidence = VerifyCanonicalEvidenceFixture(output);
        bool policyDeltaReadout = VerifyPolicyDeltaReadoutFixture(output);
        bool canonicalCoverage = VerifyCanonicalCoverageFixture(output);
        bool canonicalScope = VerifyCanonicalScopeFixture(output);
        bool canonicalFundingOutcomes = VerifyCanonicalFundingOutcomeFixture(output);
        bool canonicalMaturityChurn = VerifyCanonicalMaturityChurnFixture(output, out string maturityFailureDetail);
        bool homeostatOccurrenceCheckKey = VerifyHomeostatOccurrenceCheckKeyFixture(output);
        bool frozenSuccession = VerifyFrozenPolicySuccessionFixture(output);
        bool causalFixture = RulerLiftAutonomyReward.VerifyCausalClassificationFixture(output);
        bool checkpointDialect = Checkpoint.VerifyDialectFixture(output);
        bool passed = packetExact && teacherExact && checkpointExact && candidateFingerprintCustody && candidateFingerprintOmissionRejected
            && candidateFingerprintTamperRejected && corruptionRejected && grammarReadout && canonicalState
            && canonicalEvidence && policyDeltaReadout && canonicalCoverage && canonicalScope && canonicalFundingOutcomes && canonicalMaturityChurn && homeostatOccurrenceCheckKey && frozenSuccession && causalFixture && checkpointDialect;
        output.WriteLine($"  policy readout fixture · causes={readouts.Length} packet={(packetExact ? "exact" : "BROKEN")} custody={(candidateFingerprintCustody ? "exact" : "BROKEN")} omission={(candidateFingerprintOmissionRejected ? "rejected" : "ACCEPTED")} tamper={(candidateFingerprintTamperRejected ? "diverged" : "ACCEPTED")} teacher={(teacherExact ? "launchpad-context" : "BROKEN")} checkpoint={(checkpointExact ? "exact" : "BROKEN")} corruption={(corruptionRejected ? "rejected" : "ACCEPTED")} grammar={(grammarReadout ? "exact" : grammarReadoutDetail)} canonical={(canonicalState ? "semantic-state" : "BROKEN")} evidence={(canonicalEvidence ? "per-state" : "BROKEN")} delta={(policyDeltaReadout ? "ready/exact" : "BROKEN")} coverage={(canonicalCoverage ? "typed" : "BROKEN")} scope={(canonicalScope ? "state-bound" : "BROKEN")} funding={(canonicalFundingOutcomes ? "typed-outcomes" : "BROKEN")} maturity={(canonicalMaturityChurn ? "stable" : maturityFailureDetail)} verification-key={(homeostatOccurrenceCheckKey ? "revision/state-bound" : "BROKEN")} frozen-succession={(frozenSuccession ? "succession-refused/transition-stable" : "BROKEN")} causal={(causalFixture ? "exact" : "BROKEN")} dialect={(checkpointDialect ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyHomeostatOccurrenceCheckKeyFixture(TextWriter output)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        int actionCount = Homeostat.PolicySchema.ActionCount;
        CortexConfig config = new()
        {
            Tools = [], ActionPolicies = [], Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 8,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        PolicyCanonicalStateID[] domain = PolicyCanonicalStates.HomeostatDomain(policy);
        HomeoActuation rest = new(1.0 / 8, 8, 4, 1024, 128, false);
        Cortex cortex = new(config);
        cortex.RegisterPolicy(Homeostat.PolicySchema);
        Homeostat homeostat = new(new WeightController(new Weights(0.2, 0.2, 0.2, 0.2, 0.2)), rest);
        string fixtureRoot = Path.Combine(Environment.CurrentDirectory, ".tmp", "policy-readout-homeostat-key-" + Guid.NewGuid().ToString("N"));
        Run fixtureRun = Run.Create(fixtureRoot);
        using Tape fixtureTape = new();
        Journal fixtureJournal = new();
        cortex.BindRuntime(fixtureRun, fixtureTape, fixtureJournal, homeostat, dreamRatio: 0);
        Interocept calm = default(Interocept) with { DfThird = 1.0 };
        ConsolidationPhaseYield productive = new(1, 0, 0, 0, 0);

        global::Cogito.Grammar.InstallRevision rev42 = BuildCanonicalVerifierInstallRevision(
            policy, domain, actionCount, revision: 42);
        global::Cogito.Induct.RePairResult rev42Grammar = rev42.Snapshot.ToRePairResult();
        cortex.SwapGrammar(in rev42, advancePolicies: false);
        cortex.BindRuntimeStep(206, in rev42Grammar);
        homeostat.SenseStep(in calm);
        for (int i = 0; i < 12; i++) homeostat.CloseSleep(cortex, in productive);
        bool read42 = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt receipt42);
        PolicyCanonicalStateID state42 = receipt42.CanonicalState;
        bool scope42 = read42 && cortex.IsVerifiedPolicyScope(policy, in state42,
            receipt42.Fingerprint, receipt42.CandidateFingerprint, receipt42.CandidateOccurrenceDigest, receipt42.Revision);
        int verifierComparisons = cortex.ReadCanonicalCoverage(policy).VerifierComparisons;
        homeostat.TryGrantSharedPolicyAuthority(cortex);
        int unchangedComparisons = cortex.ReadCanonicalCoverage(policy).VerifierComparisons;
        bool sameKeyNoOp = verifierComparisons != 0 && unchangedComparisons == verifierComparisons;

        global::Cogito.Grammar.InstallRevision rev44 = BuildCanonicalVerifierInstallRevision(
            policy, domain, actionCount, revision: 44);
        global::Cogito.Induct.RePairResult rev44Grammar = rev44.Snapshot.ToRePairResult();
        cortex.SwapGrammar(in rev44, advancePolicies: false);
        cortex.BindRuntimeStep(207, in rev44Grammar);
        for (int i = 0; i < 12; i++) homeostat.CloseSleep(cortex, in productive);
        bool read44 = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt receipt44);
        bool sameCandidate = read42 && read44
            && receipt44.CandidateFingerprint == receipt42.CandidateFingerprint
            && receipt44.CandidateOccurrenceDigest == receipt42.CandidateOccurrenceDigest
            && receipt44.CanonicalState == receipt42.CanonicalState;
        PolicyCanonicalStateID state44 = receipt44.CanonicalState;
        bool scope44 = read44 && cortex.IsVerifiedPolicyScope(policy, in state44,
            receipt44.Fingerprint, receipt44.CandidateFingerprint, receipt44.CandidateOccurrenceDigest, receipt44.Revision);
        bool revisionReverified = scope44 && receipt44.Revision.Value == 44
            && (!read42 || receipt42.Revision.Value != receipt44.Revision.Value);

        PolicyCanonicalStateID separateState = domain[1];
        MetricSample[] separateFeatures = new MetricSample[HomeostatPolicyFeatures.Count];
        for (int index = 0; index < separateFeatures.Length; index++)
            separateFeatures[index] = new MetricSample(Homeostat.GetPolicyFeatureMetricID(index), NumericValue.FromI64(0));
        cortex.BindRuntimeStep(208, in rev44Grammar);
        for (int i = 0; i < 12; i++)
        {
            cortex.ChoosePolicyAction(policy, 0, in separateState, separateFeatures);
            homeostat.TryGrantSharedPolicyAuthority(cortex);
        }
        bool readGrowth = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt growthReceipt);
        bool separateStateObserved = readGrowth && growthReceipt.CanonicalState == separateState;
        HomeostatPolicyReadoutEnclosureReceipt separateEnclosure = readGrowth
            ? Homeostat.VerifySharedPolicyReadout(cortex, in rest, 0.05, 128, separateState)
            : default;
        bool separateScope = separateStateObserved && separateEnclosure.IsExact
            && cortex.TryGrantVerifiedPolicyScope(policy, in separateState,
                growthReceipt.Fingerprint, growthReceipt.CandidateFingerprint,
                growthReceipt.CandidateOccurrenceDigest, growthReceipt.Revision);
        bool passed = scope42 && sameKeyNoOp && sameCandidate && revisionReverified && separateStateObserved && separateScope;
        output.WriteLine($"  homeostat verification-key fixture · rev42={(scope42 ? $"scope(state={receipt42.CanonicalState.Value})" : "DENIED")} same-key={(sameKeyNoOp ? "no-op" : $"REASSAYED({verifierComparisons}->{unchangedComparisons})")} rev44={(revisionReverified ? $"reverified(state={receipt44.CanonicalState.Value})" : "STALE")} separate={(separateStateObserved && separateScope ? $"separate-scope(state={growthReceipt.CanonicalState.Value})" : $"LEAKED(state={growthReceipt.CanonicalState.Value},scope={separateScope})")} · {(passed ? "PASS" : "FAIL")}");
        cortex.UnbindCheckpointRuntime();
        if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);
        return passed;
    }

    private static bool VerifyFrozenPolicySuccessionFixture(TextWriter output)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        int actionCount = Homeostat.PolicySchema.ActionCount;
        CortexConfig config = new()
        {
            Tools = [], ActionPolicies = [], Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 8,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        PolicyCanonicalStateID[] domain = PolicyCanonicalStates.HomeostatDomain(policy);
        global::Cogito.Grammar.InstallRevision publication = BuildCanonicalVerifierInstallRevision(
            policy, domain, actionCount, revision: 42);
        global::Cogito.Induct.RePairResult grammar = publication.Snapshot.ToRePairResult();
        Cortex cortex = new(config);
        cortex.RegisterPolicy(Homeostat.PolicySchema);
        Homeostat homeostat = new(new WeightController(new Weights(0.2, 0.2, 0.2, 0.2, 0.2)),
            new HomeoActuation(1.0 / 8, 8, 4, 1024, 128, false));
        string fixtureRoot = Path.Combine(Environment.CurrentDirectory, ".tmp", "policy-readout-frozen-succession-" + Guid.NewGuid().ToString("N"));
        Run fixtureRun = Run.Create(fixtureRoot);
        using Tape fixtureTape = new();
        Journal fixtureJournal = new();
        cortex.BindRuntime(fixtureRun, fixtureTape, fixtureJournal, homeostat, dreamRatio: 0);
        Interocept calm = default(Interocept) with { DfThird = 1.0 };
        ConsolidationPhaseYield productive = new(1, 0, 0, 0, 0);
        cortex.SwapGrammar(in publication, advancePolicies: false);
        cortex.BindRuntimeStep(206, in grammar);
        homeostat.SenseStep(in calm);
        for (int i = 0; i < 12; i++) homeostat.CloseSleep(cortex, in productive);

        bool readoutReady = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt readout)
            && readout.IsExact
            && cortex.IsPolicyReadoutReady(policy, readout.Fingerprint);
        PolicyCanonicalStateID canonicalState = readout.CanonicalState;
        bool scopeVerified = readoutReady
            && cortex.IsVerifiedPolicyScope(policy, in canonicalState, readout.Fingerprint,
                readout.CandidateFingerprint, readout.CandidateOccurrenceDigest, readout.Revision);
        homeostat.TryGrantSharedPolicyAuthority(cortex);
        CortexPolicyTrialAuthorityIdentity identity = CortexPolicyTrialAuthorityIdentity.FromReadout(in readout);
        cortex.DisableAutonomicSpawning();
        cortex.SetPolicyTrialAuthority(policy, in identity, CortexPolicyAuthorities.Shadow, freezeAdaptation: true);
        CortexPolicyRuntimeReceipt frozenBefore = cortex.ReadPolicyRuntimeReceipt(policy);
        bool frozenRefused = !cortex.TryGrantVerifiedPolicySuccession(
            policy, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision);
        CortexPolicyRuntimeReceipt frozenAfter = cortex.ReadPolicyRuntimeReceipt(policy);
        bool frozenStable = frozenAfter.Authority == CortexPolicyAuthorities.Shadow
            && frozenAfter.TrialFrozen
            && frozenAfter.GrammarExecutions == frozenBefore.GrammarExecutions
            && frozenAfter.TrialAdaptationTransitions == frozenBefore.TrialAdaptationTransitions;

        cortex.SetPolicyTrialAuthority(policy, in identity, CortexPolicyAuthorities.Shadow, freezeAdaptation: false);
        CortexPolicyRuntimeReceipt unfrozenBefore = cortex.ReadPolicyRuntimeReceipt(policy);
        bool unfrozenGranted = cortex.TryGrantVerifiedPolicySuccession(
            policy, readout.Fingerprint, readout.CandidateFingerprint, readout.Revision);
        CortexPolicyRuntimeReceipt unfrozenAfter = cortex.ReadPolicyRuntimeReceipt(policy);
        bool unfrozenStable = unfrozenGranted
            && unfrozenAfter.Authority == CortexPolicyAuthorities.Grammar
            && !unfrozenAfter.TrialFrozen
            && unfrozenAfter.GrammarExecutions == unfrozenBefore.GrammarExecutions
            && unfrozenAfter.TrialAdaptationTransitions == unfrozenBefore.TrialAdaptationTransitions + 1;
        bool passed = scopeVerified && frozenRefused && frozenStable && unfrozenStable;
        output.WriteLine($"  policy frozen succession fixture · scope={(scopeVerified ? "verified" : "DENIED")} frozen={(frozenRefused && frozenStable ? $"succession-refused/transition-stable(g={frozenAfter.GrammarExecutions},t={frozenAfter.TrialAdaptationTransitions})" : "BROKEN")} unfrozen={(unfrozenStable ? $"succession-granted/transition-recorded(g={unfrozenAfter.GrammarExecutions},t={unfrozenAfter.TrialAdaptationTransitions})" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        cortex.UnbindCheckpointRuntime();
        if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);
        return passed;
    }

    private static bool VerifyPolicyDeltaReadoutFixture(TextWriter output)
    {
        CortexPolicyID policy = new("fixture.policy-delta-readout");
        PolicyCanonicalStateID canonicalState = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0xD1);
        CortexPolicySchema schema = new(policy, 1, 3, 1);
        MetricSample[] features = [new(new MetricID(640), NumericValue.FromF64(1.0))];
        CortexConfig config = new()
        {
            Tools = [], ActionPolicies = [], Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 2,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        global::Cogito.Grammar.InstallRevision publication = BuildCanonicalInstallRevision(
            policy, [canonicalState], schema.ActionCount, extraActionOne: 0, extraAction: 0, revision: 1, out _);
        global::Cogito.Induct.RePairResult grammar = publication.Snapshot.ToRePairResult();
        Cortex source = new(config);
        source.RegisterPolicy(schema);
        source.SwapGrammar(in publication, advancePolicies: false);
        source.BindRuntimeStep(1, in grammar);
        source.ChoosePolicyAction(policy, 0, in canonicalState, features);
        source.BindRuntimeStep(2, in grammar);
        source.ChoosePolicyAction(policy, 0, in canonicalState, features);
        if (!source.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt keyedReceipt))
        {
            output.WriteLine("  policy delta readout · seed=NOT-READY · FAIL");
            return false;
        }
        bool keyedReady = source.IsPolicyReadoutReady(policy, keyedReceipt.Fingerprint);
        using MemoryStream keyframe = new();
        using (CkptWriter writer = new(keyframe)) source.SavePolicyState(writer);
        source.CommitPolicyCheckpointDelta();

        source.BindRuntimeStep(3, in grammar);
        source.ChoosePolicyAction(policy, 0, in canonicalState, features);
        bool sourceReadout = source.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt sourceReceipt);
        bool sourceReady = source.IsPolicyReadoutReady(policy, sourceReceipt.Fingerprint);
        CortexPolicyCheckpointDelta captured = source.CapturePolicyCheckpointDelta();
        CortexPolicyCheckpointDelta replayDelta;
        byte[] currentBytes;
        using (MemoryStream encoded = new())
        {
            using (CkptWriter writer = new(encoded)) Cortex.WriteCheckpointDelta(writer, in captured);
            currentBytes = encoded.ToArray();
            encoded.Position = 0;
            using CkptReader reader = new(encoded);
            replayDelta = Cortex.ReadCheckpointDelta(reader);
        }

        CortexPolicyCheckpointDelta historicalDelta;
        using (MemoryStream encoded = new())
        {
            using (CkptWriter writer = new(encoded)) Cortex.WriteHistoricalCheckpointDeltaFixture(writer, in captured);
            encoded.Position = 0;
            using CkptReader reader = new(encoded);
            historicalDelta = Cortex.ReadCheckpointDelta(reader);
        }

        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        keyframe.Position = 0;
        using (CkptReader reader = new(keyframe)) restored.LoadPolicyState(reader);
        restored.SwapGrammar(in publication, advancePolicies: false);
        restored.ApplyPolicyCheckpointDelta(in replayDelta);
        bool replayReadout = restored.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt replayReceipt);
        bool replayReady = restored.IsPolicyReadoutReady(policy, replayReceipt.Fingerprint);
        using MemoryStream replayImage = new();
        using (CkptWriter writer = new(replayImage)) restored.SavePolicyState(writer);
        bool replayExact = sourceReadout && replayReadout
            && sourceReceipt == replayReceipt
            && sourceReceipt.IsExact == replayReceipt.IsExact
            && sourceReady == replayReady
            && sourceReady
            && replayDelta.States.Single().LearnerEvidenceTrusted
            && replayImage.Length > 0;

        Cortex historical = new(config);
        historical.RegisterPolicy(schema);
        keyframe.Position = 0;
        using (CkptReader reader = new(keyframe)) historical.LoadPolicyState(reader);
        historical.SwapGrammar(in publication, advancePolicies: false);
        historical.ApplyPolicyCheckpointDelta(in historicalDelta);
        bool historicalDecoded = historicalDelta.States.Single() is { LearnerEvidenceTrusted: false, ShadowComparisons: 0, ShadowAgreements: 0, EmulationMisses: 0 };
        bool historicalReadout = historical.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt historicalReceipt)
            && historicalReceipt.Comparisons == keyedReceipt.Comparisons
            && !historical.IsPolicyReadoutReady(policy, historicalReceipt.Fingerprint);

        Cortex mutated = new(config);
        mutated.RegisterPolicy(schema);
        keyframe.Position = 0;
        using (CkptReader reader = new(keyframe)) mutated.LoadPolicyState(reader);
        mutated.SwapGrammar(in publication, advancePolicies: false);
        CortexPolicyReadoutStateReplacement state = replayDelta.States.Single();
        CortexPolicyCheckpointDelta forged = replayDelta with
        {
            States = [state with { ShadowAgreements = checked(state.ShadowComparisons + 1) }],
        };
        bool mutationRejected = false;
        try { mutated.ApplyPolicyCheckpointDelta(in forged); }
        catch (InvalidDataException) { mutationRejected = true; }
        bool mutationClosed = mutationRejected
            && mutated.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt mutationReceipt)
            && mutationReceipt.Comparisons == keyedReceipt.Comparisons
            && mutationReceipt.Agreements == keyedReceipt.Agreements;

        string sidecarRoot = Path.Combine(Environment.CurrentDirectory, ".tmp", "policy-readout-sidecar-" + Guid.NewGuid().ToString("N"));
        bool sidecarClosed = false;
        bool historicalSidecarClosed = false;
        try
        {
            Run run = Run.Create(sidecarRoot);
            string sidecar = "policy\tfingerprint\tcomparisons\tagreements\tfailures\tpassed\n"
                + $"{policy.Value}\t{keyedReceipt.Fingerprint:X16}\t999\t999\t0\t1\n";
            Cortex sidecarCortex = new(config);
            sidecarCortex.RegisterPolicy(schema);
            sidecarCortex.BindRuntime(run, new Tape(), new Journal(), null!, 0);
            sidecarCortex.SwapGrammar(in publication, advancePolicies: false);
            sidecarCortex.BindRuntimeStep(1, in grammar);
            sidecarCortex.ChoosePolicyAction(policy, 0, in canonicalState, features);
            File.WriteAllText(run.PathOf("policy_verifications.tsv"), sidecar);
            bool legacySidecarRejected = false;
            try { sidecarCortex.RestorePolicyOccurrenceCheckReceipts(); }
            catch (InvalidDataException) { legacySidecarRejected = true; }
            sidecarClosed = legacySidecarRejected
                && !sidecarCortex.IsPolicyReadoutReady(policy, keyedReceipt.Fingerprint)
                && sidecarCortex.ReadPolicyRuntimeReceipt(policy).ShadowComparisons == 1;

            File.Delete(run.PathOf("policy_verifications.tsv"));
            historical.BindRuntime(run, new Tape(), new Journal(), null!, 0);
            File.WriteAllText(run.PathOf("policy_verifications.tsv"), sidecar);
            bool historicalLegacySidecarRejected = false;
            try { historical.RestorePolicyOccurrenceCheckReceipts(); }
            catch (InvalidDataException) { historicalLegacySidecarRejected = true; }
            historicalSidecarClosed = historicalLegacySidecarRejected
                && !historical.IsPolicyReadoutReady(policy, historicalReceipt.Fingerprint)
                && historical.ReadPolicyRuntimeReceipt(policy).ShadowComparisons == keyedReceipt.Comparisons;
        }
        finally
        {
            if (Directory.Exists(sidecarRoot)) Directory.Delete(sidecarRoot, recursive: true);
        }

        bool truncatedRejected = RejectPolicyDeltaBytes(currentBytes[..^1]);
        byte[] malformedVersion = currentBytes.ToArray(); malformedVersion[0] = 0x7F;
        bool malformedVersionRejected = RejectPolicyDeltaBytes(malformedVersion);
        bool passed = keyedReady && replayExact && mutationClosed && sidecarClosed && historicalDecoded
            && historicalReadout && historicalSidecarClosed && truncatedRejected && malformedVersionRejected;
        output.WriteLine($"  policy delta readout · keyframe-ready={(keyedReady ? "yes" : "NO")} replay={(replayExact ? "receipt+ready exact" : "BROKEN")} historical={(historicalDecoded && historicalReadout ? "decoded/closed" : "BROKEN")} mutation={(mutationClosed ? "rejected/closed" : mutationRejected ? "rejected/drift" : "ACCEPTED")} sidecar={(sidecarClosed && historicalSidecarClosed ? "cannot-teach" : "TEACHES")} malformed={(truncatedRejected && malformedVersionRejected ? "rejected" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool RejectPolicyDeltaBytes(byte[] bytes)
    {
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using CkptReader reader = new(stream);
            _ = Cortex.ReadCheckpointDelta(reader);
            return false;
        }
        catch (InvalidDataException) { return true; }
        catch (EndOfStreamException) { return true; }
    }

    internal static bool VerifyCanonicalCoverageFixture(TextWriter output)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        PolicyCanonicalStateID[] domain = PolicyCanonicalStates.HomeostatDomain(policy);
        int actionCount = Homeostat.PolicySchema.ActionCount;
        MetricSample[] features = new MetricSample[HomeostatPolicyFeatures.Count];
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Shadow,
                    ShadowDecisions = 2,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };

        PolicyCanonicalStateID observed = domain[^1];
        global::Cogito.Grammar.InstallRevision partialInstallRevision = BuildCanonicalInstallRevision(
            policy, in observed, actionCount, extraActionOne: 0, revision: 1, out _);
        global::Cogito.Induct.RePairResult partialGrammar = partialInstallRevision.Snapshot.ToRePairResult();
        Cortex partial = new(config);
        partial.RegisterPolicy(Homeostat.PolicySchema);
        partial.SwapGrammar(in partialInstallRevision, advancePolicies: false);
        for (int step = 1; step <= 8; step++)
        {
            partial.BindRuntimeStep(step, in partialGrammar);
            partial.ChoosePolicyAction(policy, 0, in observed, features);
        }
        bool partialReadout = partial.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt partialReceipt);
        PolicyCanonicalCoverageReceipt partialCoverage = partialReceipt.CanonicalCoverage;
        bool partialStarved = partialReadout
            && partialCoverage.RequiredStateCount == 36
            && partialCoverage.CoveredStateCount == 1
            && partialCoverage.MissingStateCount == 35
            && partialCoverage.Attribution == PolicyCanonicalCoverageAttributions.CoverageStarvation
            && partialCoverage.IsStarved
            && partial.ReadPolicyRuntimeReceipt(policy).Authority is CortexPolicyAuthorities.Shadow or CortexPolicyAuthorities.Launchpad;

        global::Cogito.Grammar.InstallRevision completeInstallRevision = BuildCanonicalInstallRevision(
            policy, domain, actionCount, extraActionOne: 0, extraAction: 0, revision: 2, out _);
        global::Cogito.Induct.RePairResult completeGrammar = completeInstallRevision.Snapshot.ToRePairResult();
        Cortex complete = new(config);
        complete.RegisterPolicy(Homeostat.PolicySchema);
        complete.SwapGrammar(in completeInstallRevision, advancePolicies: false);
        for (int index = 0; index < domain.Length; index++)
        {
            complete.BindRuntimeStep(index + 1, in completeGrammar);
            complete.ChoosePolicyAction(policy, 0, in domain[index], features);
        }
        bool completeReadout = complete.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt completeReceipt);
        PolicyCanonicalCoverageReceipt completeCoverage = completeReceipt.CanonicalCoverage;
        bool completeMap = completeReadout
            && completeCoverage.RequiredStateCount == 36
            && completeCoverage.CoveredStateCount == 36
            && completeCoverage.MissingStateCount == 0
            && completeCoverage.Attribution == PolicyCanonicalCoverageAttributions.CompleteCoverage;
        bool requiredDomainExact = partialCoverage.RequiredStatesDigest != 0
            && partialCoverage.RequiredStatesDigest == completeCoverage.RequiredStatesDigest;

        bool custodyExact = true;
        try { partialCoverage.Validate(); completeCoverage.Validate(); }
        catch (InvalidDataException) { custodyExact = false; }
        PolicyCanonicalCoverageEntry[] forgedEntries = [.. partialCoverage.Entries];
        int forgedIndex = Array.FindIndex(forgedEntries, static entry => !entry.Covered);
        if (forgedIndex < 0) forgedIndex = 0;
        PolicyCanonicalCoverageEntry forged = forgedEntries[forgedIndex];
        forgedEntries[forgedIndex] = forged with
        {
            Covered = true,
            Action = 0,
            CandidateFingerprint = 1,
            OccurrenceDigest = 1,
            Revision = new global::Cogito.Grammar.GrammarRevisionID(1),
            Comparisons = 1,
            Agreements = 1,
        };
        bool forgedRejected = false;
        try { (partialCoverage with { Entries = forgedEntries }).Validate(); }
        catch (InvalidDataException) { forgedRejected = true; }
        bool enclosure = VerifyHomeostatReadoutEnclosureFixture(output);
        output.WriteLine($"  policy canonical coverage fixture · partial={partialCoverage.CoveredStateCount}/{partialCoverage.RequiredStateCount} missing={partialCoverage.MissingStateCount} attribution={partialCoverage.Attribution} complete={(completeMap ? "36/36" : "BROKEN")} required-digest={(requiredDomainExact ? "stable" : "BROKEN")} custody={(custodyExact ? "exact" : "BROKEN")} forged={(forgedRejected ? "rejected" : "ACCEPTED")} enclosure={(enclosure ? "pure/accounted" : "BROKEN")} · {(partialStarved && completeMap && requiredDomainExact && custodyExact && forgedRejected && enclosure ? "PASS(starvation)" : "FAIL")}");
        return partialStarved && completeMap && requiredDomainExact && custodyExact && forgedRejected && enclosure;
    }

    private static bool VerifyHomeostatReadoutEnclosureFixture(TextWriter output)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        PolicyCanonicalStateID[] domain = PolicyCanonicalStates.HomeostatDomain(policy);
        int actionCount = Homeostat.PolicySchema.ActionCount;
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Shadow,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        global::Cogito.Grammar.InstallRevision publication = BuildCanonicalVerifierInstallRevision(
            policy, domain, actionCount, revision: 17);
        Cortex cortex = new(config);
        cortex.RegisterPolicy(Homeostat.PolicySchema);
        cortex.SwapGrammar(in publication, advancePolicies: false);
        CortexPolicyRuntimeReceipt before = cortex.ReadPolicyRuntimeReceipt(policy);
        int fundingBefore = cortex.ReadPolicyReadoutQuotaCount();
        HomeoActuation rest = new(1.0 / 8, 8, 4, 1024, 128, false);
        HomeostatPolicyReadoutEnclosureReceipt receipt = Homeostat.VerifySharedPolicyReadout(
            cortex, in rest, 0.05, 128);
        CortexPolicyRuntimeReceipt after = cortex.ReadPolicyRuntimeReceipt(policy);
        int fundingAfter = cortex.ReadPolicyReadoutQuotaCount();
        bool pure = RuntimeReceiptsEqual(in before, in after) && fundingBefore == fundingAfter;
        bool accounted = receipt.RequiredStateCount == 36
            && receipt.FoundStateCount == 36 && receipt.MissingStateCount == 0
            && receipt.IndexQueries == 36 && receipt.Comparisons == 36 * 48
            && receipt.Agreements == receipt.Comparisons && receipt.Disagreements == 0
            && receipt.ScannedBytes > 0;
        bool revisionBound = publication.GetReadoutCorpusIndex().Revision == receipt.Revision
            && publication.GetReadoutCorpusIndex().EffectiveDigest == receipt.EffectiveDigest;
        bool revisionRejected;
        global::Cogito.Grammar.InstallRevision wrongRevision = BuildCanonicalVerifierInstallRevision(
            policy, domain, actionCount, revision: 18);
        try
        {
            publication.GetReadoutCorpusIndex().RequireCompatible(wrongRevision.Revision, wrongRevision.EffectiveSnapshot.ContentDigest);
            revisionRejected = false;
        }
        catch (InvalidDataException) { revisionRejected = true; }
        global::Cogito.Grammar.InstallRevision partialInstallRevision = BuildCanonicalVerifierInstallRevision(
            policy, [domain[0]], actionCount, revision: 19);
        Cortex partial = new(config);
        partial.RegisterPolicy(Homeostat.PolicySchema);
        partial.SwapGrammar(in partialInstallRevision, advancePolicies: false);
        HomeostatPolicyReadoutEnclosureReceipt partialReceipt = Homeostat.VerifySharedPolicyReadout(
            partial, in rest, 0.05, 128);
        bool missingIsNotDisagreement = partialReceipt.FoundStateCount == 1
            && partialReceipt.MissingStateCount == 35
            && partialReceipt.Comparisons == 48
            && partialReceipt.Agreements == 48
            && partialReceipt.Disagreements == 0
            && !partialReceipt.IsExact;
        output.WriteLine($"  homeostat enclosure fixture · states={receipt.FoundStateCount}/{receipt.RequiredStateCount} missing={receipt.MissingStateCount} queries={receipt.IndexQueries} comparisons={receipt.Comparisons} agreements={receipt.Agreements} scanned={receipt.ScannedBytes} expanded={receipt.ExpandedEdges} pure={(pure ? "yes" : "MUTATED")} missing-isolation={(missingIsNotDisagreement ? "exact" : "BROKEN")} revision={(revisionBound && revisionRejected ? "bound" : "BROKEN")} · {(pure && accounted && revisionBound && revisionRejected && missingIsNotDisagreement ? "PASS" : "FAIL")}");
        return pure && accounted && revisionBound && revisionRejected && missingIsNotDisagreement && receipt.IsExact;
    }

    private static bool VerifyCanonicalScopeFixture(TextWriter output)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        PolicyCanonicalStateID[] domain = PolicyCanonicalStates.HomeostatDomain(policy);
        PolicyCanonicalStateID stateA = domain[0];
        PolicyCanonicalStateID stateB = domain[1];
        HomeostatPolicyContext contextA = new((HomeostatPolicyConditions)(stateA.Value & 0xFF),
            (stateA.Value & (1UL << 8)) != 0, (stateA.Value & (1UL << 9)) != 0);
        HomeostatPolicyContext contextB = new((HomeostatPolicyConditions)(stateB.Value & 0xFF),
            (stateB.Value & (1UL << 8)) != 0, (stateB.Value & (1UL << 9)) != 0);
        HomeostatPolicyProgram programA = Homeostat.CompilePolicyProgram(in contextA);
        HomeostatPolicyProgram programB = Homeostat.CompilePolicyProgram(in contextB);
        int launchpadA = Homeostat.FindDestinationPolicyAction(in programA);
        int launchpadB = Homeostat.FindDestinationPolicyAction(in programB);
        int actionCount = Homeostat.PolicySchema.ActionCount;
        MetricSample[] features = new MetricSample[HomeostatPolicyFeatures.Count];
        CortexConfig config = new()
        {
            Tools = [], ActionPolicies = [], Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 1,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        global::Cogito.Grammar.InstallRevision publication = BuildCanonicalVerifierInstallRevision(
            policy, domain, actionCount, revision: 31);
        global::Cogito.Induct.RePairResult grammar = publication.Snapshot.ToRePairResult();
        Cortex cortex = new(config);
        cortex.RegisterPolicy(Homeostat.PolicySchema);
        cortex.SwapGrammar(in publication, advancePolicies: false);
        cortex.BindRuntimeStep(1, in grammar);
        CortexPolicyDecision seedDecision = cortex.ChoosePolicyAction(policy, launchpadA, in stateA, features);
        bool seeded = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt seedReceipt)
            && seedReceipt.CanonicalState == stateA
            && seedReceipt.IsExact;
        HomeoActuation rest = new(1.0 / 8, 8, 4, 1024, 128, false);
        HomeostatPolicyReadoutEnclosureReceipt enclosure = seeded
            ? Homeostat.VerifySharedPolicyReadout(cortex, in rest, 0.05, 128, stateA)
            : default;
        bool exactAssay = seeded && enclosure.IsExact && enclosure.Comparisons == 48;
        bool scopeGranted = exactAssay && cortex.TryGrantVerifiedPolicyScope(
            policy, in stateA, seedReceipt.Fingerprint, seedReceipt.CandidateFingerprint,
            seedReceipt.CandidateOccurrenceDigest, seedReceipt.Revision);

        using MemoryStream keyframe = new();
        using (CkptWriter writer = new(keyframe)) cortex.SavePolicyState(writer);
        cortex.CommitPolicyCheckpointDelta();
        // Scope custody is already in the keyframe. Keep this delta probe before
        // entering an unfunded trial arm; completed execution history belongs to
        // the funded Homeostat boundary fixture, not this scope-only assay.
        CortexPolicyCheckpointDelta captured = cortex.CapturePolicyCheckpointDelta();
        cortex.RecordPolicyOccurrenceCheck(policy, seedReceipt.Fingerprint, seedReceipt.Comparisons,
            seedReceipt.Agreements, seedReceipt.Misses, passed: true, coverage: seedReceipt.CanonicalCoverage);
        cortex.DisableAutonomicSpawning();
        cortex.SetPolicyTrialAuthority(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in seedReceipt), CortexPolicyAuthorities.Grammar);
        cortex.BindRuntimeStep(2, in grammar);
        CortexPolicyDecision grammarDecision = cortex.ChoosePolicyAction(policy, launchpadA, in stateA, features);
        bool grammarEligible = grammarDecision.Readout.Authority == CortexPolicyAuthorities.Grammar;

        CortexPolicyCheckpointDelta restoredDelta;
        using (MemoryStream encoded = new())
        {
            using (CkptWriter writer = new(encoded)) Cortex.WriteCheckpointDelta(writer, in captured);
            encoded.Position = 0;
            using CkptReader reader = new(encoded);
            restoredDelta = Cortex.ReadCheckpointDelta(reader);
        }
        Cortex restored = new(config);
        restored.RegisterPolicy(Homeostat.PolicySchema);
        keyframe.Position = 0;
        using (CkptReader reader = new(keyframe)) restored.LoadPolicyState(reader);
        restored.SwapGrammar(in publication, advancePolicies: false);
        restored.ApplyPolicyCheckpointDelta(in restoredDelta);
        bool deltaExact = restored.IsVerifiedPolicyScope(policy, in stateA,
            seedReceipt.Fingerprint, seedReceipt.CandidateFingerprint,
            seedReceipt.CandidateOccurrenceDigest, seedReceipt.Revision);

        cortex.BindRuntimeStep(3, in grammar);
        CortexPolicyDecision shadowDecision = cortex.ChoosePolicyAction(policy, launchpadB, in stateB, features);
        shadowDecision = cortex.ChoosePolicyAction(policy, launchpadB, in stateB, features);
        CortexPolicyReadoutReceipt stateBReceipt = default;
        bool stateBReadout = cortex.TryReadPolicyReadout(policy, out stateBReceipt);
        bool stateBFallback = shadowDecision.Readout.Authority is CortexPolicyAuthorities.Shadow or CortexPolicyAuthorities.Launchpad
            && stateBReadout
            && stateBReceipt.CanonicalState == stateB;
        CortexPolicyTrialQuotaDecision stateBFunding = default;
        if (stateBFallback)
            stateBFunding = cortex.DecidePolicyTrialQuota(policy,
                CortexPolicyTrialAuthorityIdentity.FromReadout(in stateBReceipt), 1, 1);
        bool stateBDenied = stateBFallback
            && stateBFunding.Decision == CortexPolicyQuotaDecisions.Denied
            && stateBFunding.DenialReason == CortexPolicyTrialDenialReasons.CanonicalScopeMissing;

        global::Cogito.Grammar.InstallRevision divergentInstallRevision = BuildCanonicalInstallRevision(
            policy, in stateA, actionCount, extraActionOne: 24, revision: 32, out _);
        global::Cogito.Induct.RePairResult divergentGrammar = divergentInstallRevision.Snapshot.ToRePairResult();
        Cortex divergent = new(config);
        divergent.RegisterPolicy(Homeostat.PolicySchema);
        divergent.SwapGrammar(in divergentInstallRevision, advancePolicies: false);
        HomeostatPolicyReadoutEnclosureReceipt divergentReceipt = Homeostat.VerifySharedPolicyReadout(
            divergent, in rest, 0.05, 128, stateA);
        bool divergentRejected = !divergentReceipt.IsExact
            && divergentReceipt.Comparisons == 48
            && divergentReceipt.Agreements < divergentReceipt.Comparisons;
        output.WriteLine($"  policy canonical scope fixture · seed={(seeded ? "exact" : $"broken(state={seedReceipt.CanonicalState},cmp={seedReceipt.Comparisons},agr={seedReceipt.Agreements})")} assay={(exactAssay ? "stateA/48-exact" : $"BROKEN({enclosure.FoundStateCount}/{enclosure.RequiredStateCount},{enclosure.Agreements}/{enclosure.Comparisons})")} grant={(scopeGranted ? "earned" : "MISSING")} grammar={(grammarEligible ? "stateA" : "DENIED")} delta={(deltaExact ? "checkpoint+delta-exact" : "BROKEN")} stateB={(stateBFallback && stateBDenied ? "reflex/no-funding" : $"LEAKED(authority={shadowDecision.Readout.Authority},readout={stateBReadout},state={stateBReceipt.CanonicalState},denial={stateBFunding.DenialReason})")} divergent={(divergentRejected ? "48-reject" : "ACCEPTED")} · {(scopeGranted && grammarEligible && deltaExact && stateBDenied && divergentRejected ? "PASS" : "FAIL")}");
        return scopeGranted && grammarEligible && deltaExact && stateBDenied && divergentRejected;
    }

    private static bool RuntimeReceiptsEqual(
        in CortexPolicyRuntimeReceipt left,
        in CortexPolicyRuntimeReceipt right)
        => left.Authority == right.Authority
            && left.CachedContexts == right.CachedContexts
            && left.ShadowComparisons == right.ShadowComparisons
            && left.ShadowAgreements == right.ShadowAgreements
            && left.Decisions == right.Decisions
            && left.Outcomes == right.Outcomes
            && left.ActionExecutions.AsSpan().SequenceEqual(right.ActionExecutions)
            && left.ConservedCost == right.ConservedCost
            && left.ActionReversals == right.ActionReversals
            && left.GrammarExecutions == right.GrammarExecutions
            && left.GrammarOutcomes == right.GrammarOutcomes
            && left.PaidGrammarOutcomes == right.PaidGrammarOutcomes
            && left.DivergentGrammarExecutions == right.DivergentGrammarExecutions
            && left.Readmissions == right.Readmissions
            && left.RollbackDrillPending == right.RollbackDrillPending
            && left.RollbackDrillCompleted == right.RollbackDrillCompleted
            && left.LastGrammarLaunchpadAction == right.LastGrammarLaunchpadAction
            && left.LastGrammarAction == right.LastGrammarAction
            && left.LastGrammarFeatures.AsSpan().SequenceEqual(right.LastGrammarFeatures)
            && left.TrialAdaptationTransitions == right.TrialAdaptationTransitions
            && left.TrialFrozen == right.TrialFrozen
            && left.AdaptationEnabled == right.AdaptationEnabled;

    private static bool VerifyCanonicalStateFixture(TextWriter output)
    {
        CortexPolicyID policy = new("fixture.canonical");
        int actionCount = 3;
        PolicyCanonicalStateID state = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x0201);
        PolicyCanonicalStateID otherState = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x0202);
        MetricSample[] firstRaw = [new(new MetricID(640), NumericValue.FromF64(1.25)), new(new MetricID(641), NumericValue.FromI64(4))];
        MetricSample[] secondRaw = [new(new MetricID(640), NumericValue.FromF64(99.75)), new(new MetricID(641), NumericValue.FromI64(700))];
        global::Cogito.Grammar.InstallRevision first = BuildCanonicalInstallRevision(policy, in state, actionCount, extraActionOne: 0, revision: 1, out _);
        PolicyReadoutCache cache = new();
        PolicyReadoutCacheReceipt firstMiss = GrammarPolicyReadout.ReadCanonicalCache(in first, policy, in state, actionCount, 0, cache);
        GrammarPolicyContextKey firstKey = firstMiss.Context;
        PolicyReadoutCacheReceipt firstRefill = GrammarPolicyReadout.RefillCanonical(
            in first, policy, in state, actionCount, 0, new GrammarContinuationQuota(1), cache, default, 0, out _);
        GrammarPolicyDecision firstDecision = firstRefill.Decision;
        PolicyReadoutCacheReceipt sameState = GrammarPolicyReadout.ReadCanonicalCache(in first, policy, in state, actionCount, 0, cache);
        PolicyReadoutCacheReceipt distinctState = GrammarPolicyReadout.ReadCanonicalCache(in first, policy, in otherState, actionCount, 0, cache);
        byte[] cacheImage;
        using (MemoryStream stream = new())
        {
            using (CkptWriter writer = new(stream)) cache.Save(writer);
            cacheImage = stream.ToArray();
        }
        PolicyReadoutCache restoredCache = new();
        using (MemoryStream stream = new(cacheImage))
        using (CkptReader reader = new(stream)) restoredCache.Load(reader, actionCount);
        using MemoryStream restoredStream = new();
        using (CkptWriter restoredWriter = new(restoredStream)) restoredCache.Save(restoredWriter);
        bool cacheRoundTrip = cacheImage.AsSpan().SequenceEqual(restoredStream.ToArray())
            && GrammarPolicyReadout.ReadCanonicalCache(in first, policy, in state, actionCount, 0, restoredCache).Outcome == PolicyReadoutCacheOutcomes.Hit;
        bool rawVectorsCollapse = firstKey.Equals(new GrammarPolicyContextKey(in state, actionCount, 0))
            && firstRaw[0].Value.Bits != secondRaw[0].Value.Bits
            && sameState.Outcome == PolicyReadoutCacheOutcomes.Hit
            && sameState.Decision.Action == firstDecision.Action;

        ulong firstCandidateSetDigest = cache.ComputeCanonicalCandidateSetDigest(policy, actionCount, first.Revision);
        global::Cogito.Grammar.InstallRevision otherInstallRevision = BuildCanonicalInstallRevision(policy, in otherState, actionCount, extraActionOne: 0, revision: 1, out _);
        PolicyReadoutCacheReceipt otherMiss = GrammarPolicyReadout.ReadCanonicalCache(in otherInstallRevision, policy, in otherState, actionCount, 0, cache);
        PolicyReadoutCacheReceipt otherRefill = GrammarPolicyReadout.RefillCanonical(
            in otherInstallRevision, policy, in otherState, actionCount, 0, new GrammarContinuationQuota(1), cache, default, 0, out _);
        ulong distinctCandidateSetDigest = cache.ComputeCanonicalCandidateSetDigest(policy, actionCount, first.Revision);

        global::Cogito.Grammar.InstallRevision sameRevisionSemantics = BuildCanonicalInstallRevision(policy, in state, actionCount, extraActionOne: 0, revision: 2, out _);
        cache.MoveToRevision(sameRevisionSemantics.Revision);
        PolicyReadoutCacheReceipt revalidatedMiss = GrammarPolicyReadout.ReadCanonicalCache(in sameRevisionSemantics, policy, in state, actionCount, 0, cache);
        bool cacheRetainsCanonicalEvidence = cache.Count == 2;
        PolicyReadoutCacheReceipt revalidated = GrammarPolicyReadout.RefillCanonical(
            in sameRevisionSemantics, policy, in state, actionCount, 0, new GrammarContinuationQuota(1), cache, default, 0, out _);
        ulong firstIdentity = GrammarPolicyReadout.ComputeCandidateFingerprint(policy, in state, in firstDecision);
        GrammarPolicyDecision revalidatedDecision = revalidated.Decision;
        ulong revalidatedIdentity = GrammarPolicyReadout.ComputeCandidateFingerprint(policy, in state, in revalidatedDecision);
        bool revisionPreserved = revalidatedMiss.Outcome == PolicyReadoutCacheOutcomes.Miss
            && revalidatedIdentity == firstIdentity
            && revalidated.Decision.OccurrenceDigest == firstDecision.OccurrenceDigest;

        global::Cogito.Grammar.InstallRevision supportGrowth = BuildCanonicalInstallRevision(
            policy, [state], actionCount, extraActionOne: 0, extraAction: 8, revision: 3, out _);
        cache.MoveToRevision(supportGrowth.Revision);
        _ = GrammarPolicyReadout.ReadCanonicalCache(in supportGrowth, policy, in state, actionCount, 0, cache);
        PolicyReadoutCacheReceipt supportGrowthReadout = GrammarPolicyReadout.RefillCanonical(
            in supportGrowth, policy, in state, actionCount, 0, new GrammarContinuationQuota(1), cache, default, 0, out _);
        GrammarPolicyDecision supportGrowthDecision = supportGrowthReadout.Decision;
        ulong supportGrowthIdentity = GrammarPolicyReadout.ComputeCandidateFingerprint(policy, in state, in supportGrowthDecision);
        bool supportGrowthPreservesIdentity = supportGrowthIdentity == revalidatedIdentity
            && supportGrowthReadout.Decision.OccurrenceDigest != revalidated.Decision.OccurrenceDigest;

        global::Cogito.Grammar.InstallRevision changed = BuildCanonicalInstallRevision(policy, in state, actionCount, extraActionOne: 8, revision: 4, out _);
        cache.MoveToRevision(changed.Revision);
        _ = GrammarPolicyReadout.ReadCanonicalCache(in changed, policy, in state, actionCount, 0, cache);
        PolicyReadoutCacheReceipt changedReadout = GrammarPolicyReadout.RefillCanonical(
            in changed, policy, in state, actionCount, 0, new GrammarContinuationQuota(1), cache, default, 0, out _);
        GrammarPolicyDecision changedDecision = changedReadout.Decision;
        ulong changedIdentity = GrammarPolicyReadout.ComputeCandidateFingerprint(policy, in state, in changedDecision);
        bool actionChangeResets = changedIdentity != supportGrowthIdentity;

        using Tape teacherTape = new();
        Journal teacherJournal = new();
        TapePacketCreator.PolicyTeacherPacketIDs teacher = TapePacketCreator.AppendPolicyCanonicalExample(
            teacherTape, teacherJournal, 0, policy, in state, 2, firstRaw, actionCount);
        teacherTape.Resolve(teacher.GrammarEventID, out byte[] teacherBytes);
        teacherTape.Resolve(teacher.AuditOnlyEventID, out byte[] teacherCustodyBytes);
        byte[] canonicalContext = TapePacketCreator.EncodePolicyCanonicalGrammarContext(policy, in state, actionCount);
        byte[] continuation = TapePacketCreator.EncodePolicyGrammarContinuation(2);
        bool teacherCarriesSeparateEvidence = teacherBytes.AsSpan().StartsWith(canonicalContext)
            && teacherBytes.AsSpan()[canonicalContext.Length..].StartsWith(continuation)
            && teacherBytes.AsSpan().IndexOf("\tRAW-EVIDENCE="u8) < 0
            && teacherCustodyBytes.AsSpan().StartsWith("POLICY-TEACHER-CUSTODY"u8)
            && teacherCustodyBytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes($"grammar-event=i:{teacher.GrammarEventID.Value:X16}")) >= 0
            && teacherCustodyBytes.AsSpan().IndexOf("\tRAW-EVIDENCE="u8) >= 0
            && teacherJournal.ResidentLines.Any(static line => line.Contains("state=", StringComparison.Ordinal) && line.Contains("custody=", StringComparison.Ordinal));

        bool candidateSetStable = firstCandidateSetDigest != 0
            && distinctCandidateSetDigest != 0
            && distinctCandidateSetDigest != firstCandidateSetDigest
            && otherMiss.Outcome == PolicyReadoutCacheOutcomes.Miss
            && otherRefill.HasDecision;
        bool passed = rawVectorsCollapse && distinctState.Outcome == PolicyReadoutCacheOutcomes.Miss && cacheRoundTrip
            && candidateSetStable
            && revisionPreserved && cacheRetainsCanonicalEvidence && supportGrowthPreservesIdentity && actionChangeResets && teacherCarriesSeparateEvidence;
        output.WriteLine($"  policy canonical-state fixture · same-raw={(rawVectorsCollapse ? "collapsed" : "BROKEN")} distinct={(distinctState.Outcome == PolicyReadoutCacheOutcomes.Miss ? "miss" : "COLLISION")} set={(candidateSetStable ? "distinct-digest" : "BROKEN")} cache={(cacheRoundTrip ? "roundtrip" : "BROKEN")} revalidation={(revisionPreserved && cacheRetainsCanonicalEvidence ? "preserved" : "RESET")} support-growth={(supportGrowthPreservesIdentity ? "preserved" : "RESET")} action-change={(actionChangeResets ? "reset" : "PRESERVED")} teacher={(teacherCarriesSeparateEvidence ? "canonical+raw" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyCanonicalEvidenceFixture(TextWriter output)
    {
        CortexPolicyID policy = new("fixture.canonical.evidence");
        int actionCount = 3;
        CortexPolicySchema schema = new(policy, 1, actionCount, 1);
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 2,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        PolicyCanonicalStateID stateA = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x31);
        PolicyCanonicalStateID stateB = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x32);
        MetricSample[] features = [new(new MetricID(640), NumericValue.FromF64(1.0))];
        global::Cogito.Grammar.InstallRevision first = BuildCanonicalInstallRevision(
            policy, [stateA, stateB], actionCount, extraActionOne: 0, extraAction: 0, revision: 1, out _);
        Cortex cortex = new(config);
        cortex.RegisterPolicy(schema);
        global::Cogito.Induct.RePairResult firstGrammar = first.Snapshot.ToRePairResult();
        cortex.BindRuntimeStep(1, in firstGrammar);
        cortex.SwapGrammar(in first, advancePolicies: false);
        cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        cortex.BindRuntimeStep(2, in firstGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        bool firstReady = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt firstReceipt);
        ulong stateFundingIdentity = GrammarPolicyReadout.ComputeStateFingerprint(policy, in stateA);

        cortex.BindRuntimeStep(3, in firstGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateB, features);
        bool interleaved = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt interleavedReceipt)
            && interleavedReceipt.Fingerprint != firstReceipt.Fingerprint
            && interleavedReceipt.Comparisons >= firstReceipt.Comparisons
            && interleavedReceipt.Agreements == interleavedReceipt.Comparisons
            && interleavedReceipt.Fingerprint != stateFundingIdentity;

        global::Cogito.Grammar.InstallRevision supportGrowth = BuildCanonicalInstallRevision(
            policy, [stateA, stateB], actionCount, extraActionOne: 0, extraAction: 8, revision: 2, out _);
        global::Cogito.Induct.RePairResult supportGrammar = supportGrowth.Snapshot.ToRePairResult();
        cortex.BindRuntimeStep(4, in supportGrammar);
        cortex.SwapGrammar(in supportGrowth);
        cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        cortex.BindRuntimeStep(5, in supportGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateB, features);
        bool revisionPreserved = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt revisionReceipt)
            && revisionReceipt.Fingerprint == interleavedReceipt.Fingerprint
            && revisionReceipt.Comparisons >= interleavedReceipt.Comparisons;

        global::Cogito.Grammar.InstallRevision missingB = BuildCanonicalInstallRevision(
            policy, [stateA], actionCount, extraActionOne: 0, extraAction: 0, revision: 3, out _);
        global::Cogito.Induct.RePairResult missingGrammar = missingB.Snapshot.ToRePairResult();
        cortex.BindRuntimeStep(6, in missingGrammar);
        cortex.SwapGrammar(in missingB);
        cortex.ChoosePolicyAction(policy, 0, in stateB, features);
        bool noMatchFailsClosed = !cortex.TryReadPolicyReadout(policy, out _);
        cortex.BindRuntimeStep(7, in missingGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        bool survivingStateRemains = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt survivingReceipt)
            && survivingReceipt.Fingerprint != interleavedReceipt.Fingerprint
            && survivingReceipt.Comparisons >= interleavedReceipt.Comparisons;

        using MemoryStream checkpoint = new();
        using (CkptWriter writer = new(checkpoint)) cortex.SavePolicyState(writer);
        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        checkpoint.Position = 0;
        using (CkptReader reader = new(checkpoint)) restored.LoadPolicyState(reader);
        restored.BindRuntimeStep(7, in missingGrammar);
        restored.SwapGrammar(in missingB, advancePolicies: false);
        bool restoredReadout = restored.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt restoredReceipt)
            && restoredReceipt == survivingReceipt;
        using MemoryStream checkpointAfter = new();
        using (CkptWriter writer = new(checkpointAfter)) restored.SavePolicyState(writer);
        bool checkpointExact = checkpoint.ToArray().AsSpan().SequenceEqual(checkpointAfter.ToArray());

        bool passed = firstReady && interleaved && revisionPreserved && noMatchFailsClosed && survivingStateRemains
            && restoredReadout && checkpointExact;
        output.WriteLine($"  policy canonical evidence fixture · interleave={(interleaved ? "preserved" : "OVERWROTE")} revision={(revisionPreserved ? "preserved" : "RESET")} no-match={(noMatchFailsClosed ? "closed" : "STALE")} survivor={(survivingStateRemains ? "retained" : "LOST")} checkpoint={(restoredReadout && checkpointExact ? "exact" : "BROKEN")} funding={(stateFundingIdentity != interleavedReceipt.Fingerprint ? "separate" : "MIXED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyCanonicalFundingOutcomeFixture(TextWriter output)
    {
        CortexPolicyID policy = new("fixture.canonical.funding-outcomes");
        const int actionCount = 3;
        CortexPolicySchema schema = new(policy, 1, actionCount, 1);
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 2,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        PolicyCanonicalStateID stateA = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x41);
        PolicyCanonicalStateID stateC = new(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x43);
        MetricSample[] features = [new(new MetricID(640), NumericValue.FromF64(1.0))];
        global::Cogito.Grammar.InstallRevision first = BuildCanonicalInstallRevision(
            policy, [stateA], actionCount, extraActionOne: 0, extraAction: 0, revision: 1, out _);
        Cortex cortex = new(config);
        cortex.RegisterPolicy(schema);
        global::Cogito.Induct.RePairResult firstGrammar = first.Snapshot.ToRePairResult();
        cortex.SwapGrammar(in first, advancePolicies: false);
        for (int step = 1; step <= 8; step++)
        {
            cortex.BindRuntimeStep(step, in firstGrammar);
            cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        }
        bool seeded = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt seededReceipt)
            && seededReceipt.Comparisons >= 8
            && seededReceipt.Comparisons == seededReceipt.Agreements;

        global::Cogito.Grammar.InstallRevision revalidation = BuildCanonicalInstallRevision(
            policy, [stateA], actionCount, extraActionOne: 0, extraAction: 0, revision: 2, out _);
        global::Cogito.Induct.RePairResult revalidationGrammar = revalidation.Snapshot.ToRePairResult();
        cortex.SwapGrammar(in revalidation);
        cortex.BindRuntimeStep(9, in revalidationGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateC, features); // paid no-match spends this step's only credit
        cortex.ChoosePolicyAction(policy, 0, in stateA, features); // denied: no validation, not counterevidence
        bool deniedClosed = !cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt deniedReceipt)
            && deniedReceipt.Comparisons >= seededReceipt.Comparisons
            && deniedReceipt.Agreements == deniedReceipt.Comparisons
            && deniedReceipt.Misses == 0;

        cortex.BindRuntimeStep(10, in revalidationGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        bool paidRestore = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt restoredReceipt)
            && restoredReceipt.Comparisons > deniedReceipt.Comparisons
            && restoredReceipt.Comparisons == restoredReceipt.Agreements;

        cortex.DisableAutonomicSpawning();
        cortex.SetPolicyTrialAuthority(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in restoredReceipt), CortexPolicyAuthorities.Grammar);
        cortex.ChoosePolicyAction(policy, 0, in stateC, features);
        bool suppressedPreservesEvidence = !cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt suppressedReceipt)
            && suppressedReceipt.Comparisons == restoredReceipt.Comparisons
            && suppressedReceipt.Agreements == restoredReceipt.Agreements
            && suppressedReceipt.Misses == restoredReceipt.Misses;

        global::Cogito.Grammar.InstallRevision missingA = BuildCanonicalInstallRevision(
            policy, [stateC], actionCount, extraActionOne: 0, extraAction: 0, revision: 3, out _);
        global::Cogito.Induct.RePairResult missingAGrammar = missingA.Snapshot.ToRePairResult();
        cortex.SwapGrammar(in missingA);
        cortex.BindRuntimeStep(11, in missingAGrammar);
        cortex.ChoosePolicyAction(policy, 0, in stateA, features);
        bool paidNoMatchResets = !cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt removedReceipt)
            && removedReceipt.Comparisons == 0
            && removedReceipt.Agreements == 0
            && removedReceipt.Misses == 0;

        bool passed = seeded && deniedClosed && paidRestore && suppressedPreservesEvidence && paidNoMatchResets;
        output.WriteLine($"  policy canonical funding fixture · seed={(seeded ? ">=8" : "BROKEN")} denied={(deniedClosed ? "preserved/closed" : "ERASED")} restore={(paidRestore ? "same-action" : "BROKEN")} suppress={(suppressedPreservesEvidence ? "preserved" : "MUTATED")} no-match={(paidNoMatchResets ? "removed" : "STALE")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyCanonicalMaturityChurnFixture(TextWriter output, out string failureDetail)
    {
        CortexPolicyID policy = new("fixture.canonical.maturity-churn");
        int actionCount = Homeostat.PolicySchema.ActionCount;
        CortexPolicySchema schema = new(policy, 1, actionCount, 1);
        CortexConfig config = new()
        {
            Tools = [], ActionPolicies = [], Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 8,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        PolicyCanonicalStateID state = PolicyCanonicalStates.HomeostatDomain(policy)[0];
        MetricSample[] features = [new(new MetricID(640), NumericValue.FromF64(1.0))];
        Cortex cortex = new(config);
        cortex.RegisterPolicy(schema);
        bool originStable = true;
        bool behavioralIdentityStable = true;
        bool countersMonotone = true;
        bool coverageMonotone = true;
        bool replayNoOp = true;
        bool pendingClosed = true;
        bool verificationSeeded = false;
        bool verificationCleared = false;
        bool authorityDemoted = false;
        bool readyAfterChurn = false;
        bool fundingPossible = false;
        int originStep = -1;
        ulong semanticFingerprint = 0;
        CortexPolicyReadoutReceipt firstReceipt = default;
        CortexPolicyReadoutReceipt previousReceipt = default;
        int previousAction = -1;
        bool maturityReason = false;
        for (int step = 1; step <= 10; step++)
        {
            global::Cogito.Grammar.InstallRevision publication = BuildCanonicalInstallRevision(
                policy, [state], actionCount, extraActionOne: 0, extraAction: 0,
                revision: (ulong)step, out _);
            global::Cogito.Induct.RePairResult grammar = publication.Snapshot.ToRePairResult();
            cortex.BindRuntimeStep(step, in grammar);
            cortex.SwapGrammar(in publication);
            CortexPolicyDecision decision = cortex.ChoosePolicyAction(policy, 0, in state, features);
            bool current = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt currentReceipt);
            pendingClosed &= current;
            if (step == 1)
            {
                firstReceipt = currentReceipt;
                previousReceipt = currentReceipt;
                previousAction = decision.Readout.RawCandidateAction;
                originStep = step;
                semanticFingerprint = firstReceipt.CandidateFingerprint;
                CortexPolicyTrialQuotaDecision early = cortex.DecidePolicyTrialQuota(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in firstReceipt), 256, 1);
                maturityReason = early.Decision == CortexPolicyQuotaDecisions.Denied
                    && early.DenialReason == CortexPolicyTrialDenialReasons.MaturityWindow;

                // Seed a strict verification tuple and force Grammar authority so the next
                // publication proves that revision drift invalidates authority without
                // erasing the behavioral evidence underneath it.
                cortex.RecordVerifiedPolicyReadout(policy, firstReceipt.Fingerprint,
                    firstReceipt.CandidateFingerprint, firstReceipt.Revision);
                PolicyCanonicalStateID firstState = firstReceipt.CanonicalState;
                bool scopeSeeded = cortex.TryGrantVerifiedPolicyScope(
                    policy, in firstState, firstReceipt.Fingerprint,
                    firstReceipt.CandidateFingerprint, firstReceipt.CandidateOccurrenceDigest,
                    firstReceipt.Revision);
                cortex.DisableAutonomicSpawning();
                cortex.SetPolicyTrialAuthority(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in firstReceipt), CortexPolicyAuthorities.Grammar);
                bool verifiedBeforeDrift = cortex.HasPolicyOccurrenceCheck(policy,
                    firstReceipt.Fingerprint, firstReceipt.CandidateFingerprint, firstReceipt.Revision, out bool verifiedPassed)
                    && verifiedPassed;
                verificationSeeded = verifiedBeforeDrift && scopeSeeded;
            }
            else
            {
                if (step == 2)
                    previousAction = decision.Readout.RawCandidateAction;
                else
                    behavioralIdentityStable &= currentReceipt.CandidateFingerprint == semanticFingerprint
                        && decision.Readout.RawCandidateAction == previousAction;
                countersMonotone &= currentReceipt.Comparisons >= previousReceipt.Comparisons
                    && currentReceipt.Agreements >= previousReceipt.Agreements
                    && currentReceipt.Misses == previousReceipt.Misses;
                PolicyCanonicalCoverageReceipt coverage = cortex.ReadCanonicalCoverage(policy);
                coverageMonotone &= coverage.VerifierComparisons == currentReceipt.Comparisons
                    && coverage.VerifierAgreements == currentReceipt.Agreements
                    && coverage.VerifierMisses == currentReceipt.Misses;
                if (step == 2)
                {
                    authorityDemoted = cortex.ReadPolicyRuntimeReceipt(policy).Authority == CortexPolicyAuthorities.Shadow;
                    verificationCleared = !cortex.HasPolicyOccurrenceCheck(policy, firstReceipt.Fingerprint,
                        firstReceipt.CandidateFingerprint, firstReceipt.Revision, out _);
                }
                readyAfterChurn |= currentReceipt.Comparisons >= 8
                    && currentReceipt.IsExact
                    && cortex.IsPolicyReadoutReady(policy, currentReceipt.Fingerprint);
                previousReceipt = currentReceipt;
                previousAction = decision.Readout.RawCandidateAction;
                originStable &= currentReceipt.CandidateFingerprint == semanticFingerprint
                    && currentReceipt.Comparisons >= firstReceipt.Comparisons;
            }
            int beforeFunding = cortex.ReadPolicyReadoutQuotaCount();
            cortex.SwapGrammar(in publication);
            replayNoOp &= cortex.ReadPolicyReadoutQuotaCount() == beforeFunding;
        }
        cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt finalReceipt);
        CortexPolicyTrialQuotaDecision funding = cortex.DecidePolicyTrialQuota(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in finalReceipt), 1, 1);
        bool mature = funding.Decision == CortexPolicyQuotaDecisions.Paid
            && funding.CandidateState == CortexPolicyTrialCandidateStates.Active
            && funding.CandidateOriginStep == originStep
            && funding.CandidateRequiredStep == originStep + 1;
        fundingPossible = mature;

        using MemoryStream image = new();
        using (CkptWriter writer = new(image)) cortex.SavePolicyState(writer);
        byte[] before = image.ToArray();
        image.Position = 0;
        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        using (CkptReader reader = new(image)) restored.LoadPolicyState(reader);
        using MemoryStream resaved = new();
        using (CkptWriter writer = new(resaved)) restored.SavePolicyState(writer);
        bool checkpointExact = before.AsSpan().SequenceEqual(resaved.ToArray());
        bool passed = originStable && behavioralIdentityStable && countersMonotone && coverageMonotone
            && pendingClosed && replayNoOp && maturityReason && verificationSeeded && verificationCleared
            && authorityDemoted && readyAfterChurn && fundingPossible && mature && checkpointExact;
        List<string> failures = [];
        if (!originStable) failures.Add("origin");
        if (!behavioralIdentityStable) failures.Add("identity");
        if (!countersMonotone) failures.Add("counters");
        if (!coverageMonotone) failures.Add("coverage");
        if (!pendingClosed) failures.Add("pending");
        if (!replayNoOp) failures.Add("replay");
        if (!maturityReason) failures.Add("early-reason");
        if (!verificationSeeded || !verificationCleared) failures.Add("verification");
        if (!authorityDemoted) failures.Add("demotion");
        if (!readyAfterChurn) failures.Add("ready");
        if (!fundingPossible) failures.Add("boundary-funding");
        if (!mature) failures.Add("mature-funding");
        if (!checkpointExact) failures.Add("checkpoint");
        failureDetail = failures.Count == 0 ? "stable" : string.Join(',', failures);
        output.WriteLine($"  policy canonical maturity fixture · churn={(originStable && behavioralIdentityStable ? "origin-stable" : "RESET")} counters={(countersMonotone ? "monotone" : "RESET")} coverage={(coverageMonotone ? "preserved" : "RESET")} pending={(pendingClosed ? "closed" : "OPEN")} replay={(replayNoOp ? "no-op" : "REPAID")} verification={(verificationSeeded && verificationCleared && authorityDemoted ? "invalidated/demoted" : $"STALE(seed={verificationSeeded},clear={verificationCleared},demote={authorityDemoted})")} ready={(readyAfterChurn ? ">=8-exact" : "DENIED")} early={(maturityReason ? "typed-maturity" : "UNTYPED")} mature={(mature ? "funded" : "DENIED")} checkpoint={(checkpointExact ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyGrammarReadoutFixture(
        CortexPolicyID policy,
        MetricSample[] features,
        int actionCount,
        TextWriter output,
        out string failureDetail)
    {
        global::Cogito.Grammar.InstallRevision first = BuildInstallRevision(
            policy, features, actionCount, extraActionOne: 0, revision: 1, out byte[] firstTapeBytes);
        PolicyReadoutCache cache = new();
        PolicyReadoutCacheReceipt firstMiss = GrammarPolicyReadout.ReadCache(in first, policy, features, actionCount, 2, cache);
        GrammarPolicyContextKey firstKey = firstMiss.Context;
        PolicyReadoutCacheReceipt firstRefill = GrammarPolicyReadout.Refill(
            in first, policy, features, actionCount, 2, new GrammarContinuationQuota(3), cache,
            in firstKey, default, 0, out _);
        GrammarPolicyDecision firstDecision = firstRefill.Decision;
        PolicyReadoutCacheReceipt repeated = GrammarPolicyReadout.ReadCache(
            in first, policy, features, actionCount, 2, cache);
        GrammarPolicyDecision repeatedDecision = repeated.Decision;
        PolicyReadoutCacheReceipt siblingMiss = GrammarPolicyReadout.ReadCache(
            in first, policy, features, actionCount, 0, cache);
        GrammarPolicyContextKey siblingKey = siblingMiss.Context;
        PolicyReadoutCacheReceipt siblingRefill = GrammarPolicyReadout.Refill(
            in first, policy, features, actionCount, 0, new GrammarContinuationQuota(1), cache,
            in siblingKey, default, 0, out _);
        GrammarPolicyDecision siblingDecision = siblingRefill.Decision;
        bool firstFound = firstRefill.HasDecision;
        bool repeatedFound = repeated.Outcome == PolicyReadoutCacheOutcomes.Hit;
        bool siblingFound = siblingRefill.HasDecision;
        bool cacheRoundTrip = siblingFound && VerifyReadoutCacheRoundTrip(
            cache, in first, policy, actionCount,
            in firstKey, in firstDecision, in siblingKey, in siblingDecision);
        byte[] contextA = TapePacketCreator.EncodePolicyGrammarContext(policy, features, actionCount);
        byte[] contextB = TapePacketCreator.EncodePolicyGrammarContext(policy, features, actionCount);
        bool deterministic = firstMiss.Outcome == PolicyReadoutCacheOutcomes.Miss
            && firstRefill.Outcome == PolicyReadoutCacheOutcomes.Refilled
            && firstFound && repeatedFound && firstDecision == repeatedDecision
            && contextA.AsSpan().SequenceEqual(contextB);
        byte[] context = TapePacketCreator.EncodePolicyGrammarContext(policy, features, actionCount);
        byte[][] continuations = new byte[actionCount][];
        for (int action = 0; action < actionCount; action++)
            continuations[action] = TapePacketCreator.EncodePolicyGrammarContinuation(action);
        bool scored = first.TryChooseContinuation(
            context, continuations, new GrammarContinuationQuota(1), 0,
            out GrammarContinuationDecision tieDecision);
        bool ordinalTie = scored && tieDecision.CandidateScores[0] == tieDecision.CandidateScores[1]
            && tieDecision.Continuation == 0;
        bool publicationExact = first.ReconstructPublishedBytes().AsSpan().SequenceEqual(firstTapeBytes);
        bool conserved = firstDecision.Completion.Held
            == firstDecision.Completion.Used + firstDecision.Completion.Reclaimed;

        global::Cogito.Grammar.InstallRevision second = BuildInstallRevision(
            policy, features, actionCount, extraActionOne: 3, revision: 2, out _);
        PolicyReadoutCacheReceipt secondMiss = GrammarPolicyReadout.ReadCache(in second, policy, features, actionCount, 0, cache);
        GrammarPolicyContextKey secondKey = secondMiss.Context;
        PolicyReadoutCacheReceipt secondRefill = GrammarPolicyReadout.Refill(
            in second, policy, features, actionCount, 0, new GrammarContinuationQuota(1), cache,
            in secondKey, default, 0, out _);
        GrammarPolicyDecision secondDecision = secondRefill.Decision;
        bool secondFound = secondMiss.Outcome == PolicyReadoutCacheOutcomes.Miss
            && secondRefill.Outcome == PolicyReadoutCacheOutcomes.Refilled;
        bool oldEntryAbsent = !cache.TryGet(second.Revision, in firstKey, out _);
        bool invalidated = secondFound && oldEntryAbsent && cache.Count == 1
            && cache.Revision == second.Revision && secondDecision.Revision == second.Revision && secondDecision.Action == 1;

        bool coordinatedTamperRejected = VerifyCurrentCacheTamper(
            in first, policy, features, actionCount, in firstKey, in firstDecision);
        bool boundedContinuous = VerifyBoundedContinuousCache(policy, actionCount, first.Revision, in firstDecision);

        bool corruptAccountingRejected;
        try { _ = new GrammarContinuationQuotaCompletion(2, 2, 1, 1, 0); corruptAccountingRejected = false; }
        catch (InvalidDataException) { corruptAccountingRejected = true; }
        bool runtimeExact = VerifyGrammarReadoutRuntimeFixture(
            policy, features, actionCount, in first, in second, output, out string runtimeFailureDetail);
        bool semanticProvenance = VerifySemanticProvenanceCacheBoundary(output);
        bool corpusIndex = VerifyReadoutCorpusIndexFixture(in first, in second, in firstKey, in firstDecision, actionCount, output);
        bool passed = deterministic && cacheRoundTrip && ordinalTie && publicationExact && invalidated && conserved
            && corruptAccountingRejected && coordinatedTamperRejected && boundedContinuous && runtimeExact && semanticProvenance && corpusIndex;
        List<string> failures = [];
        if (!deterministic) failures.Add("tokens");
        if (!cacheRoundTrip) failures.Add("cache");
        if (!ordinalTie) failures.Add("tie");
        if (!publicationExact) failures.Add("publication");
        if (!invalidated) failures.Add("revision");
        if (!conserved || !corruptAccountingRejected) failures.Add("budget");
        if (!coordinatedTamperRejected) failures.Add("tamper");
        if (!boundedContinuous) failures.Add("bound");
        if (!runtimeExact) failures.Add("authority:" + runtimeFailureDetail);
        if (!semanticProvenance) failures.Add("semantic");
        if (!corpusIndex) failures.Add("corpus-index");
        failureDetail = failures.Count == 0 ? "exact" : string.Join(',', failures);
        output.WriteLine($"  grammar policy fixture · tokens={(deterministic ? "exact" : "BROKEN")} cache={(cacheRoundTrip ? "multi-context/exact" : "BROKEN")} publication={(publicationExact ? "source-exact" : "DRIFT")} tie={(ordinalTie ? "equal-score/ordinal" : "BROKEN")} revision={(invalidated ? "refilled" : "STALE")} budget={(conserved && corruptAccountingRejected ? "conserved/rejects-corruption" : "BROKEN")} tamper={(coordinatedTamperRejected ? "rejected" : "ACCEPTED")} bound={(boundedContinuous ? "lru/deterministic" : "BROKEN")} authority={(runtimeExact ? "e2e" : "BYPASSED")} semantic={(semanticProvenance ? "nonce-ignored/change-misses" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyReadoutCorpusIndexFixture(
        in global::Cogito.Grammar.InstallRevision first,
        in global::Cogito.Grammar.InstallRevision second,
        in GrammarPolicyContextKey context,
        in GrammarPolicyDecision expectedDepthTwo,
        int actionCount,
        TextWriter output)
    {
        ReadoutCorpusIndex index = first.GetReadoutCorpusIndex();
        bool sameRevisionContexts = true;
        byte[][] continuations = new byte[actionCount][];
        for (int action = 0; action < actionCount; action++)
            continuations[action] = TapePacketCreator.EncodePolicyGrammarContinuation(action);
        GrammarContinuationQuotaCompletion depthTwoSettlement = default;
        GrammarPolicyDecision depthTwoDecision = default;
        for (int depth = 0; depth <= 2; depth++)
        {
            GrammarContinuationQuota funding = new(checked(depth + 1));
            bool found = index.TryChooseContinuation(context.Context, continuations, funding, depth, out GrammarContinuationDecision choice, out GrammarContinuationReadoutReceipt receipt);
            GrammarContinuationQuotaCompletion settlement = funding.Complete();
            sameRevisionContexts &= found
                && receipt.Revision == first.Revision
                && receipt.EffectiveDigest == index.EffectiveDigest
                && receipt.CorpusBytes == choice.ScannedBytes
                && settlement.ScannedBytes == choice.ScannedBytes
                && settlement.ExpandedEdges == choice.ExpandedEdges
                && settlement.Used == checked(depth + 1)
                && receipt.MatchingRecords == choice.MatchingRecords;
            if (depth == 2)
            {
                depthTwoSettlement = settlement;
                depthTwoDecision = new GrammarPolicyDecision(
                    choice.Continuation,
                    choice.LearnedWeight,
                    choice.MatchingRecords,
                    first.Revision,
                    settlement,
                    0)
                {
                    OccurrenceDigest = PolicySupportDigest.Compute(choice.CandidateScores, choice.CandidateCounts, choice.MatchingRecords),
                };
            }
        }
        bool decisionsPreserved = sameRevisionContexts
            && depthTwoDecision.Action == expectedDepthTwo.Action
            && depthTwoDecision.LearnedWeight == expectedDepthTwo.LearnedWeight
            && depthTwoDecision.MatchingRecords == expectedDepthTwo.MatchingRecords
            && depthTwoDecision.Completion.Held == expectedDepthTwo.Completion.Held
            && depthTwoDecision.Completion.Used == expectedDepthTwo.Completion.Used
            && depthTwoDecision.Completion.Reclaimed == expectedDepthTwo.Completion.Reclaimed
            && depthTwoDecision.Completion.ScannedBytes == expectedDepthTwo.Completion.ScannedBytes
            && depthTwoDecision.Completion.ExpandedEdges == expectedDepthTwo.Completion.ExpandedEdges;

        bool indexReused = ReferenceEquals(index, first.GetReadoutCorpusIndex());
        bool revisionBumpRejected;
        try
        {
            index.RequireCompatible(second.Revision, second.EffectiveSnapshot.ContentDigest);
            revisionBumpRejected = false;
        }
        catch (InvalidDataException) { revisionBumpRejected = true; }

        bool mutationRejected = false;
        GrammarSnapshot effective = first.EffectiveSnapshot;
        if (effective.Compressed.Length > 0)
        {
            Symbol original = effective.Compressed[0];
            effective.Compressed[0] = new Symbol(original.Value ^ 1U);
            try
            {
                index.RequireCompatible(effective);
            }
            catch (InvalidDataException)
            {
                mutationRejected = true;
            }
            finally
            {
                effective.Compressed[0] = original;
            }
        }

        bool resumeOnce = index.BuildReceipt.RecordCount == index.RecordCount
            && index.BuildReceipt.CorpusBytes == index.CorpusBytes
            && index.BuildReceipt.EffectiveDigest == index.EffectiveDigest
            && depthTwoSettlement.ScannedBytes == expectedDepthTwo.Completion.ScannedBytes;
        bool passed = sameRevisionContexts && decisionsPreserved && indexReused && revisionBumpRejected && mutationRejected && resumeOnce;
        output.WriteLine($"  policy readout corpus index · same-revision-N={(sameRevisionContexts ? "exact" : "BROKEN")} decision={(decisionsPreserved ? "byte-exact" : "DRIFT")} reuse={(indexReused ? "once" : "REBUILT")} revision={(revisionBumpRejected ? "rejected" : "ACCEPTED")} mutation={(mutationRejected ? "rejected" : "ACCEPTED")} resume={(resumeOnce ? "once" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifySemanticProvenanceCacheBoundary(TextWriter output)
    {
        CortexPolicyID policy = new("fixture.semantic-provenance");
        const int actionCount = 3;
        MetricID decisionIndex = new(610);
        MetricID[] excluded = [decisionIndex];
        MetricSample[] semantic = [new(new MetricID(10), NumericValue.FromI64(1))];
        MetricSample[] rawFirst =
        [
            semantic[0],
            new(decisionIndex, NumericValue.FromI64(1)),
        ];
        MetricSample[] rawSameState =
        [
            semantic[0],
            new(decisionIndex, NumericValue.FromI64(99)),
        ];
        MetricSample[] rawChangedState =
        [
            new(new MetricID(10), NumericValue.FromI64(2)),
            new(decisionIndex, NumericValue.FromI64(99)),
        ];
        using Tape trainingTape = new();
        Journal trainingJournal = new();
        int trainingStep = 0;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            TapePacketCreator.AppendPolicySemanticExample(
                trainingTape, trainingJournal, trainingStep++, policy, 0, rawFirst, actionCount, excluded);
            TapePacketCreator.AppendPolicySemanticExample(
                trainingTape, trainingJournal, trainingStep++, policy, 1, rawSameState, actionCount, excluded);
        }
        TapePacketCreator.AppendPolicySemanticExample(
            trainingTape, trainingJournal, trainingStep, policy, 2, rawFirst, actionCount, excluded);
        global::Cogito.Induct.RePairResult trained = Engine.Induce(trainingTape, 1).Result;
        global::Cogito.Grammar.InstallRevision publication =
            global::Cogito.Grammar.InstallRevision.FromRePair(
                new global::Cogito.Grammar.GrammarRevisionID(1),
                global::Cogito.Grammar.GrammarRevisionID.Zero,
                in trained);
        PolicyReadoutCache cache = new();
        PolicyReadoutCacheReceipt cold = GrammarPolicyReadout.ReadCache(
            in publication, policy, rawFirst, actionCount, 0, cache, excluded);
        GrammarPolicyContextKey key = cold.Context;
        PolicyReadoutCacheReceipt refill = GrammarPolicyReadout.Refill(
            in publication, policy, rawFirst, actionCount, 0,
            new GrammarContinuationQuota(1), cache, in key, default, 0, out _);
        PolicyReadoutCacheReceipt nonceOnly = GrammarPolicyReadout.ReadCache(
            in publication, policy, rawSameState, actionCount, 0, cache, excluded);
        PolicyReadoutCacheReceipt semanticChange = GrammarPolicyReadout.ReadCache(
            in publication, policy, rawChangedState, actionCount, 0, cache, excluded);
        using MemoryStream cacheImage = new();
        using (CkptWriter writer = new(cacheImage)) cache.Save(writer);
        cacheImage.Position = 0;
        PolicyReadoutCache restoredCache = new();
        using (CkptReader reader = new(cacheImage)) restoredCache.Load(reader, actionCount);
        PolicyReadoutCacheReceipt resumedNonceOnly = GrammarPolicyReadout.ReadCache(
            in publication, policy, rawSameState, actionCount, 0, restoredCache, excluded);

        using Tape tape = new();
        Journal journal = new();
        TapeEventID packetID = TapePacketCreator.AppendPolicyExample(
            tape, journal, 0, policy, 0, rawSameState, actionCount);
        bool rawPacketRetainedNonce = tape.Resolve(packetID, out byte[] packetBytes)
            && packetBytes.Length > TapePacketCreator.EncodePolicyGrammarContinuation(0).Length
            && TapePacketCreator.ValidatePolicyGrammarContext(
                packetBytes.AsSpan()[..^TapePacketCreator.EncodePolicyGrammarContinuation(0).Length],
                policy, actionCount, rawSameState.Length);
        TapePacketCreator.PolicyTeacherPacketIDs semanticTeacher = TapePacketCreator.AppendPolicySemanticExample(
            tape, journal, 1, policy, 0, rawSameState, actionCount, excluded);
        int semanticContinuationLength = TapePacketCreator.EncodePolicyGrammarContinuation(0).Length;
        bool semanticTeacherSeparatesEvidence = tape.Resolve(semanticTeacher.GrammarEventID, out byte[] semanticTeacherBytes)
            && tape.Resolve(semanticTeacher.AuditOnlyEventID, out byte[] semanticCustodyBytes)
            && semanticTeacherBytes.AsSpan().IndexOf("\tRAW-EVIDENCE="u8) < 0
            && semanticCustodyBytes.AsSpan().StartsWith("POLICY-TEACHER-CUSTODY"u8)
            && semanticCustodyBytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes($"grammar-event=i:{semanticTeacher.GrammarEventID.Value:X16}")) >= 0
            && semanticCustodyBytes.AsSpan().IndexOf("\tRAW-EVIDENCE="u8) >= 0
            && TapePacketCreator.ValidatePolicyGrammarContext(
                semanticTeacherBytes.AsSpan()[..^semanticContinuationLength],
                policy, actionCount, semantic.Length)
            && journal.ResidentLines.Any(static line => line.Contains("semantic-features=1\traw-features=2\tcustody=", StringComparison.Ordinal));
        bool passed = cold.Outcome == PolicyReadoutCacheOutcomes.Miss
            && refill.Outcome == PolicyReadoutCacheOutcomes.Refilled
            && nonceOnly.Outcome == PolicyReadoutCacheOutcomes.Hit
            && semanticChange.Outcome == PolicyReadoutCacheOutcomes.Miss
            && resumedNonceOnly.Outcome == PolicyReadoutCacheOutcomes.Hit
            && rawPacketRetainedNonce && semanticTeacherSeparatesEvidence;
        output.WriteLine($"  policy semantic-cache boundary · cold={(cold.Outcome == PolicyReadoutCacheOutcomes.Miss ? "miss" : "BROKEN")} nonce-only={(nonceOnly.Outcome == PolicyReadoutCacheOutcomes.Hit ? "hit" : "FRAGMENTED")} semantic-change={(semanticChange.Outcome == PolicyReadoutCacheOutcomes.Miss ? "miss" : "COLLISION")} resume={(resumedNonceOnly.Outcome == PolicyReadoutCacheOutcomes.Hit ? "hit" : "BROKEN")} raw-packet={(rawPacketRetainedNonce ? "evidence-kept" : "LOST")} teacher={(semanticTeacherSeparatesEvidence ? "semantic+raw" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyBoundedContinuousCache(
        CortexPolicyID policy,
        int actionCount,
        GrammarRevisionID revision,
        in GrammarPolicyDecision decision)
    {
        PolicyReadoutCache left = new();
        PolicyReadoutCache right = new();
        List<GrammarPolicyContextKey> keys = new(PolicyReadoutCache.MaxEntries + 1);
        for (int index = 0; index < PolicyReadoutCache.MaxEntries + 1; index++)
        {
            MetricSample[] sample = [new(new MetricID(640), NumericValue.FromF64(index + 0.125))];
            GrammarPolicyContextKey key = new(TapePacketCreator.EncodePolicyGrammarContext(policy, sample, actionCount), 0);
            if (keys.Count > 0 && key.Equals(keys[^1]))
                throw new InvalidDataException($"bounded cache context duplicate at {index}");
            keys.Add(key);
            left.Store(policy, revision, in key, in decision);
            right.Store(policy, revision, in key, in decision);
            if (index == PolicyReadoutCache.MaxEntries - 1)
            {
                GrammarPolicyContextKey oldest = keys[0];
                _ = left.TryGet(revision, in oldest, out _);
                _ = right.TryGet(revision, in oldest, out _);
            }
        }
        GrammarPolicyContextKey retained = keys[0];
        GrammarPolicyContextKey evictedKey = keys[1];
        bool evicted = left.Count == PolicyReadoutCache.MaxEntries
            && left.FundingCount == PolicyReadoutCache.MaxEntries
            && Contains(left, revision, in retained)
            && !Contains(left, revision, in evictedKey)
            && right.Count == PolicyReadoutCache.MaxEntries
            && right.FundingCount == PolicyReadoutCache.MaxEntries
            && Contains(right, revision, in retained)
            && !Contains(right, revision, in evictedKey);
        byte[] leftImage = SaveCache(left);
        byte[] rightImage = SaveCache(right);
        PolicyReadoutCache restored = new();
        using (MemoryStream image = new(leftImage))
        using (CkptReader reader = new(image))
            restored.Load(reader, actionCount);
        byte[] restoredImage = SaveCache(restored);
        bool roundTrip = restored.Count == PolicyReadoutCache.MaxEntries
            && restored.FundingCount == PolicyReadoutCache.MaxEntries
            && leftImage.AsSpan().SequenceEqual(rightImage)
            && leftImage.AsSpan().SequenceEqual(restoredImage);
        PolicyReadoutCache uninterrupted = new();
        PolicyReadoutCache split = new();
        for (int index = 0; index < PolicyReadoutCache.MaxEntries; index++)
        {
            GrammarPolicyContextKey key = keys[index];
            uninterrupted.Store(policy, revision, in key, in decision);
            split.Store(policy, revision, in key, in decision);
        }
        GrammarPolicyContextKey accessed = keys[100];
        GrammarPolicyContextKey retainedKey = keys[0];
        _ = uninterrupted.TryGet(revision, in accessed, out _);
        _ = uninterrupted.TryGet(revision, in retainedKey, out _);
        _ = split.TryGet(revision, in accessed, out _);
        _ = split.TryGet(revision, in retainedKey, out _);
        byte[] splitImage = SaveCache(split);
        PolicyReadoutCache splitRestored = new();
        using (MemoryStream image = new(splitImage))
        using (CkptReader reader = new(image))
            splitRestored.Load(reader, actionCount);
        MetricSample[] nextSample = [new(new MetricID(640), NumericValue.FromF64(PolicyReadoutCache.MaxEntries + 0.125))];
        GrammarPolicyContextKey nextKey = new(TapePacketCreator.EncodePolicyGrammarContext(policy, nextSample, actionCount), 0);
        uninterrupted.Store(policy, revision, in nextKey, in decision);
        splitRestored.Store(policy, revision, in nextKey, in decision);
        List<PolicyReadoutCacheEntry> uninterruptedEntries = new();
        List<PolicyReadoutCacheEntry> splitEntries = new();
        uninterrupted.AppendEntries(uninterruptedEntries);
        splitRestored.AppendEntries(splitEntries);
        bool splitEviction = !Contains(uninterrupted, revision, in evictedKey)
            && !Contains(splitRestored, revision, in evictedKey)
            && Contains(uninterrupted, revision, in retainedKey)
            && Contains(splitRestored, revision, in retainedKey)
            && uninterruptedEntries.Count == splitEntries.Count
            && uninterruptedEntries.Select(static entry => entry.QuotaID).SequenceEqual(splitEntries.Select(static entry => entry.QuotaID));
        bool splitBytes = SaveCache(uninterrupted).AsSpan().SequenceEqual(SaveCache(splitRestored));
        return evicted && roundTrip && splitEviction && splitBytes;

        static bool Contains(PolicyReadoutCache cache, GrammarRevisionID revision, in GrammarPolicyContextKey key)
            => cache.TryGet(revision, in key, out _);

        static byte[] SaveCache(PolicyReadoutCache cache)
        {
            using MemoryStream image = new();
            using (CkptWriter writer = new(image)) cache.Save(writer);
            return image.ToArray();
        }
    }

    private static bool VerifyCurrentCacheTamper(
        in global::Cogito.Grammar.InstallRevision publication,
        CortexPolicyID policy,
        MetricSample[] features,
        int actionCount,
        in GrammarPolicyContextKey context,
        in GrammarPolicyDecision source)
    {
        GrammarPolicyDecision wrongPayload = source with
        {
            Action = (source.Action + 1) % actionCount,
            LearnedWeight = checked(source.LearnedWeight + 1),
            Completion = new GrammarContinuationQuotaCompletion(
                source.Completion.Held, source.Completion.Used, source.Completion.Reclaimed,
                checked(source.Completion.ScannedBytes + 1), source.Completion.ExpandedEdges),
            Fingerprint = GrammarPolicyReadout.ComputeFingerprint(publication.Revision, policy),
        };
        GrammarPolicyDecision wrongFingerprint = source with { Fingerprint = 0xDEADBEEFCAFEBABEUL };
        GrammarPolicyDecision coordinatedFunding = source with
        {
            Completion = new GrammarContinuationQuotaCompletion(
                checked(source.Completion.Held + 4), source.Completion.Used,
                checked(source.Completion.Reclaimed + 4), source.Completion.ScannedBytes, source.Completion.ExpandedEdges),
        };
        GrammarPolicyDecision oversizedFunding = source with
        {
            Completion = new GrammarContinuationQuotaCompletion(
                PolicyReadoutCache.MaxFundingReservation + 1, source.Completion.Used,
                checked(PolicyReadoutCache.MaxFundingReservation + 1 - source.Completion.Used),
                source.Completion.ScannedBytes, source.Completion.ExpandedEdges),
        };
        GrammarPolicyContextKey alienPolicy = new(
            TapePacketCreator.EncodePolicyGrammarContext(new CortexPolicyID("alien.policy"), features, actionCount),
            context.DeliberationDepth);
        GrammarPolicyContextKey alienActionCount = new(
            TapePacketCreator.EncodePolicyGrammarContext(policy, features, actionCount + 1),
            context.DeliberationDepth);
        MetricSample[] alienFeatures = [features[0], new MetricSample(new MetricID(1), NumericValue.FromF64(9.0))];
        GrammarPolicyContextKey alienFeatureCount = new(
            TapePacketCreator.EncodePolicyGrammarContext(policy, alienFeatures, actionCount),
            context.DeliberationDepth);
        return RejectsCurrentCacheEntry(in publication, policy, features, actionCount, in context, in wrongPayload)
            && RejectsCurrentCacheEntry(in publication, policy, features, actionCount, in context, in wrongFingerprint)
            && RejectsCurrentCacheEntry(in publication, policy, features, actionCount, in context, in oversizedFunding, source.Completion)
            && RejectsCurrentCacheEntry(in publication, policy, features, actionCount, in alienPolicy, in source)
            && RejectsCurrentCacheEntry(in publication, policy, features, actionCount, in alienActionCount, in source)
            && RejectsCurrentCacheEntry(in publication, policy, features, actionCount, in alienFeatureCount, in source);

        static bool RejectsCurrentCacheEntry(
            in global::Cogito.Grammar.InstallRevision publication,
            CortexPolicyID policy,
            MetricSample[] features,
            int actionCount,
            in GrammarPolicyContextKey context,
            in GrammarPolicyDecision decision,
            GrammarContinuationQuotaCompletion? bindingSource = null)
        {
            PolicyReadoutCache cache = new();
            GrammarPolicyDecision bindingDecision = bindingSource is { } settlement
                ? decision with { Completion = settlement }
                : decision;
            cache.StoreBound(
                policy, publication.Revision, in context, in decision,
                GrammarPolicyReadout.ComputeQuotaID(policy, publication.Revision, 0, in context, in bindingDecision));
            // Store is the trusted producer path and intentionally does not trigger an
            // O(total)-entry sweep. Round-trip through Load to exercise the untrusted persisted
            // cache boundary, where the owning publication must revalidate every resident entry.
            try
            {
                using MemoryStream image = new();
                using (CkptWriter writer = new(image)) cache.Save(writer);
                image.Position = 0;
                PolicyReadoutCache restored = new();
                using (CkptReader reader = new(image)) restored.Load(reader, actionCount);
                _ = GrammarPolicyReadout.ReadCache(
                    in publication, policy, features, actionCount, context.DeliberationDepth, restored);
                return false;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or EndOfStreamException)
            {
                return true;
            }
        }
    }

    private static bool VerifyReadoutCacheRoundTrip(
        PolicyReadoutCache source,
        in global::Cogito.Grammar.InstallRevision publication,
        CortexPolicyID policy,
        int actionCount,
        in GrammarPolicyContextKey firstKey,
        in GrammarPolicyDecision firstDecision,
        in GrammarPolicyContextKey secondKey,
        in GrammarPolicyDecision secondDecision)
    {
        using MemoryStream before = new();
        using (CkptWriter writer = new(before)) source.Save(writer);
        PolicyReadoutCache restored = new();
        before.Position = 0;
        using (CkptReader reader = new(before)) restored.Load(reader, actionCount);
        using MemoryStream after = new();
        using (CkptWriter writer = new(after)) restored.Save(writer);
        List<PolicyReadoutCacheEntry> entries = new();
        restored.AppendEntries(entries);
        bool directExact = entries.Count == restored.Count;
        for (int index = 0; directExact && index < entries.Count; index++)
        {
            PolicyReadoutCacheEntry entry = entries[index];
            GrammarPolicyContextKey context = entry.Context;
            PolicyReadoutCache directCache = new();
            bool found = GrammarPolicyReadout.TryChooseCanonicalContext(
                in publication, policy, actionCount, context.DeliberationDepth,
                new GrammarContinuationQuota(entry.Decision.Completion.Held), directCache,
                in context, default, 0, out GrammarPolicyDecision direct, out _);
            directExact = found && direct == entry.Decision;
        }
        return restored.Revision == publication.Revision
            && restored.Count == 2
            && restored.TryGet(publication.Revision, in firstKey, out GrammarPolicyDecision restoredFirst)
            && restored.TryGet(publication.Revision, in secondKey, out GrammarPolicyDecision restoredSecond)
            && restoredFirst == firstDecision
            && restoredSecond == secondDecision
            && directExact
            && before.ToArray().AsSpan().SequenceEqual(after.ToArray());
    }

    private static bool VerifyGrammarReadoutRuntimeFixture(
        CortexPolicyID policy,
        MetricSample[] features,
        int actionCount,
        in global::Cogito.Grammar.InstallRevision first,
        in global::Cogito.Grammar.InstallRevision second,
        TextWriter output,
        out string failureDetail)
    {
        failureDetail = "readout-unavailable";
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 2,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        CortexPolicySchema schema = new(policy, features.Length, actionCount, 1);
        Cortex cortex = new(config);
        cortex.RegisterPolicy(schema);
        PolicyCanonicalStateID canonicalState = new(
            policy, PolicyCanonicalStateKinds.Generic, version: 1, value: 1);
        global::Cogito.Grammar.InstallRevision runtimeFirst = BuildCanonicalInstallRevision(
            policy, in canonicalState, actionCount, extraActionOne: 0,
            revision: first.Revision.Value, out _);
        global::Cogito.Grammar.InstallRevision runtimeSecond = BuildCanonicalInstallRevision(
            policy, in canonicalState, actionCount, extraActionOne: 3,
            revision: second.Revision.Value, out _);
        global::Cogito.Induct.RePairResult grammar = runtimeFirst.Snapshot.ToRePairResult();
        global::Cogito.Induct.RePairResult secondGrammar = runtimeSecond.Snapshot.ToRePairResult();
        cortex.BindRuntimeStep(1, in grammar);
        cortex.SwapGrammar(in runtimeFirst, advancePolicies: false);
        CortexPolicyDecision shadow = cortex.ChoosePolicyAction(policy, 0, in canonicalState, features);
        if (!cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt installedReadout))
            return false;
        if (!cortex.TryGrantVerifiedPolicyScope(
                policy, in canonicalState, installedReadout.Fingerprint,
                installedReadout.CandidateFingerprint, installedReadout.CandidateOccurrenceDigest,
                installedReadout.Revision))
            return false;
        cortex.BindRuntimeStep(100, in grammar);
        CortexPolicyTrialQuotaDecision forkBudget = cortex.DecidePolicyTrialQuota(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in installedReadout), 1, 1);
        bool currenciesSeparate = forkBudget.Decision == CortexPolicyQuotaDecisions.Paid
            && forkBudget.RemainingQuota == 99;
        CortexPolicyDecision promoted = cortex.ChoosePolicyAction(policy, 0, in canonicalState, features);
        // This generic policy owns the readout/funding ledger, but not the Homeostat
        // seed-custody authority that authenticates a completed trial history. Capture
        // its durable checkpoint before entering the forced execution arm; the funded
        // history SaveLoad proof belongs to the Homeostat boundary fixture.
        using MemoryStream before = new();
        CortexPolicyReadoutCheckpointLayout beforeLayout;
        using (CkptWriter writer = new(before)) cortex.SavePolicyState(writer, 12, out beforeLayout);
        cortex.DisableAutonomicSpawning();
        cortex.SetPolicyTrialAuthority(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in installedReadout), CortexPolicyAuthorities.Grammar, forcedDivergenceSeed: 7);
        CortexPolicyDecision diverged = cortex.ChoosePolicyAction(policy, 0, in canonicalState, features);
        cortex.BindRuntimeStep(101, in secondGrammar);
        cortex.SwapGrammar(in runtimeSecond, advancePolicies: false);
        // Let the publication-drift guard retire the completed forced arm before
        // the canonical successor decision is consumed.  The successor itself
        // remains on the canonical-state overload below.
        _ = cortex.ChoosePolicyAction(policy, 1, features);
        CortexPolicyDecision cutover = cortex.ChoosePolicyAction(policy, 1, in canonicalState, features);
        bool journalExact = cortex.TryReadPolicyReadoutQuota(
                policy, GrammarPolicyReadout.ComputeStateFingerprint(policy, in canonicalState), 1,
                out CortexPolicyReadoutQuotaDecision readoutFunding)
            && cortex.TryReadPolicyReadoutCompletion(readoutFunding.QuotaDecisionID, out CortexPolicyTrialCompletion readoutSettlement)
            && VerifyReadoutFundingJournal(in readoutFunding, in readoutSettlement, cortex, output);
        bool sameIDRetryExact = VerifySameIDReadoutRetry(config, schema, policy, features, in grammar, in first);
        bool distinctDeniedExact = VerifyDistinctDeniedReadoutIDs(config, schema, policy, features, in grammar, in first);
        Cortex budgetCortex = new(config);
        budgetCortex.RegisterPolicy(schema);
        budgetCortex.BindRuntimeStep(512, in grammar);
        budgetCortex.SwapGrammar(in first, advancePolicies: false);
        budgetCortex.ChoosePolicyAction(policy, 0, features);
        if (!budgetCortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt budgetReadout))
            return false;
        budgetCortex.BindRuntimeStep(768, in grammar);
        budgetCortex.ChoosePolicyAction(policy, 0, features);
        ulong budgetFingerprint = budgetReadout.Fingerprint;
        CortexPolicyTrialQuotaDecision horizonBudget = budgetCortex.DecidePolicyTrialQuota(
            policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in budgetReadout), 256, 3);
        bool step768Currency = horizonBudget.Decision == CortexPolicyQuotaDecisions.Paid
            && horizonBudget.PlannedArmSteps == 768
            && horizonBudget.HeldArmSteps == 768
            && horizonBudget.RemainingQuota == 0;

        bool trialReceipt = cortex.TryReadPolicyTrialExecutionReceipt(
            policy, out CortexPolicyTrialExecutionOutcomes trialOutcome, out long trialRequests,
            out long trialGuardAdmitted, out CortexPolicyDecisionReadout trialLastRequestReadout,
            out CortexPolicyDecisionID trialLastRequestID, out int trialLastRequestStep,
            out CortexPolicyDecisionReadout trialExecutionReadout, out CortexPolicyDecisionID trialExecutionID,
            out ulong trialExecutionFingerprint, out int trialExecutionStep);
        bool genericHistoryDeferred = !trialReceipt
            && trialOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
            && trialRequests == 0 && trialGuardAdmitted == 0;
        output.WriteLine(
            $"  policy generic trial history · custody={(genericHistoryDeferred ? "deferred-homeostat" : "BROKEN")} receipt={trialReceipt} outcome={trialOutcome} requests={trialRequests} guard-admitted={trialGuardAdmitted} funding={forkBudget.QuotaDecisionID} last={trialLastRequestID}/{trialLastRequestStep}/{trialLastRequestReadout.SelectionCause} execution={trialExecutionID}/{trialExecutionStep}/{trialExecutionReadout.SelectionCause} fingerprint={trialExecutionFingerprint:X16}");
        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        before.Position = 0;
        using (CkptReader reader = new(before)) restored.LoadPolicyState(reader, policySchema: 12);
        bool genericHistoryAbsent = !restored.TryReadPolicyTrialExecutionIdentity(
            policy, out _, out _, out _, out _);
        using MemoryStream after = new();
        // The round trip stays on the pre-forced-arm capture's schema 12; a default
        // (13) resave appends the per-policy readout fingerprint and breaks byte parity.
        using (CkptWriter writer = new(after)) restored.SavePolicyState(writer, 12);
        bool retiredSchemaRejected = VerifyRetiredPolicyStateRejected(config, schema, cortex);
        bool corruptCacheRejected = VerifyCorruptReadoutCache();
        bool corruptFundingCheckpointRejected = VerifyCorruptReadoutFundingCheckpoint(
            config, schema, before.ToArray(), readoutFunding.QuotaDecisionID, readoutFunding.Policy, in beforeLayout);
        bool boundaryGateExact = VerifyBoundaryShadowGateFixture(
            policy, features, actionCount, in first, output, out string boundaryFailureDetail);
        bool authorityCeiling = VerifyAuthorityCeilingFixture(policy, features, actionCount, in first, output);
        bool economy = VerifyReadoutEconomyFixture(schema, in grammar, output);
        bool forkAuthority = VerifyReadoutForkAuthorityFixture(config, schema, in readoutFunding, before.ToArray(), output);
        bool publishedAuthority = VerifyPublishedAuthorityResumeFixture(config, schema, policy, features, in first, in second, output);
        bool shadowExact = shadow.Authority == CortexPolicyAuthorities.Shadow
            && shadow.Action == shadow.LaunchpadAction;
        bool promotedExact = promoted.Authority == CortexPolicyAuthorities.Grammar;
        bool divergenceExact = diverged.SelectionCause == CortexPolicySelectionCauses.TrialOverride
            && diverged.Action != diverged.RawCandidateAction;
        bool cutoverExact = cutover.Authority == CortexPolicyAuthorities.Launchpad
            && cutover.SelectionCause == CortexPolicySelectionCauses.Launchpad
            && cutover.RawCandidateAction == -1
            && cutover.SelectedCandidateAction == -1
            && cutover.Action == cutover.LaunchpadAction;
        bool saveLoadExact = before.ToArray().AsSpan().SequenceEqual(after.ToArray());
        bool runtimeExact = shadowExact && promotedExact && divergenceExact && cutoverExact
            && journalExact && currenciesSeparate && step768Currency && sameIDRetryExact
            && distinctDeniedExact && retiredSchemaRejected && corruptCacheRejected
            && corruptFundingCheckpointRejected && boundaryGateExact && authorityCeiling
            && economy && forkAuthority && publishedAuthority && saveLoadExact;
        List<string> failures = [];
        if (!shadowExact) failures.Add("shadow");
        if (!promotedExact) failures.Add("promotion");
        if (!divergenceExact) failures.Add("divergence");
        if (!cutoverExact) failures.Add("cutover");
        if (!journalExact) failures.Add("journal");
        if (!currenciesSeparate) failures.Add("currencies");
        if (!step768Currency) failures.Add("step768");
        if (!sameIDRetryExact) failures.Add("retry");
        if (!distinctDeniedExact) failures.Add("denied");
        if (!retiredSchemaRejected) failures.Add("retired-schema");
        if (!corruptCacheRejected) failures.Add("cache-corruption");
        if (!corruptFundingCheckpointRejected) failures.Add("funding-corruption");
        if (!boundaryGateExact) failures.Add("boundary:" + boundaryFailureDetail);
        if (!authorityCeiling) failures.Add("ceiling");
        if (!economy) failures.Add("economy");
        if (!forkAuthority) failures.Add("fork-authority");
        if (!publishedAuthority) failures.Add("published-authority");
        if (!saveLoadExact) failures.Add("save-load");
        failureDetail = failures.Count == 0 ? "exact" : string.Join(',', failures);
        output.WriteLine(
            $"  policy runtime transition detail · diverged=authority:{diverged.Authority},cause:{diverged.SelectionCause},raw:{diverged.RawCandidateAction},selected:{diverged.SelectedCandidateAction},action:{diverged.Action},generic-history={(genericHistoryAbsent ? "deferred-homeostat" : "PRESENT")} cutover=authority:{cutover.Authority},cause:{cutover.SelectionCause},raw:{cutover.RawCandidateAction},selected:{cutover.SelectedCandidateAction},action:{cutover.Action}");
        output.WriteLine($"  policy runtime detail · shadow={shadowExact} promotion={promotedExact} divergence={divergenceExact} cutover={cutoverExact} journal={journalExact} currencies={currenciesSeparate} step768={step768Currency} retry={sameIDRetryExact} denied={distinctDeniedExact} retired={retiredSchemaRejected} cacheCorruption={corruptCacheRejected} fundingCorruption={corruptFundingCheckpointRejected} boundary={boundaryGateExact} ceiling={authorityCeiling} economy={economy} forkAuthority={forkAuthority} publishedAuthority={publishedAuthority} saveLoad={saveLoadExact}");
        output.WriteLine($"  policy funding currencies · readout-trial=separate trial-step100={(currenciesSeparate ? "funded/remaining99" : "FAIL")} trial-step768={(step768Currency ? "funded/reserved768/remaining0" : "FAIL")} · {(currenciesSeparate && step768Currency ? "PASS" : "FAIL")}");
        return runtimeExact;
    }

    private static bool VerifyAuthorityCeilingFixture(
        CortexPolicyID policy,
        MetricSample[] features,
        int actionCount,
        in global::Cogito.Grammar.InstallRevision publication,
        TextWriter output)
    {
        CortexConfig launchpadConfig = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Shadow,
                    AuthorityCeiling = CortexPolicyAuthorities.Launchpad,
                    ShadowDecisions = 2,
                },
            },
        };
        CortexPolicySchema schema = new(policy, features.Length, actionCount, 1);
        Cortex launchpad = new(launchpadConfig);
        launchpad.RegisterPolicy(schema);
        global::Cogito.Induct.RePairResult grammar = publication.Snapshot.ToRePairResult();
        launchpad.BindRuntimeStep(1, in grammar);
        launchpad.SwapGrammar(in publication, advancePolicies: false);
        CortexPolicyDecision first = launchpad.ChoosePolicyAction(policy, 0, features);
        CortexPolicyDecision second = launchpad.ChoosePolicyAction(policy, 0, features);
        CortexPolicyRuntimeReceipt launchpadReceipt = launchpad.ReadPolicyRuntimeReceipt(policy);
        bool teacherObservation = launchpadReceipt.CachedContexts > 0
            && launchpadReceipt.ShadowComparisons >= 2
            && first.Authority == CortexPolicyAuthorities.Launchpad
            && second.Authority == CortexPolicyAuthorities.Launchpad
            && second.SelectionCause == CortexPolicySelectionCauses.Launchpad
            && second.RawCandidateAction == -1
            && second.SelectedCandidateAction == -1
            && second.Action == second.LaunchpadAction
            && launchpadReceipt.GrammarExecutions == 0
            && launchpadReceipt.Authority == CortexPolicyAuthorities.Launchpad;

        bool externalRejected = launchpad.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt readout)
            && !launchpad.TryGrantPolicyAuthority(policy, readout.Fingerprint);
        launchpad.DisableAutonomicSpawning();
        bool trialRejected;
        try
        {
            launchpad.SetPolicyTrialAuthority(policy, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), CortexPolicyAuthorities.Grammar);
            trialRejected = false;
        }
        catch (InvalidOperationException)
        {
            trialRejected = true;
        }

        CortexConfig grammarConfig = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    AuthorityCeiling = CortexPolicyAuthorities.Grammar,
                    ShadowDecisions = 2,
                },
            },
        };
        Cortex grammarDefault = new(grammarConfig);
        grammarDefault.RegisterPolicy(schema);
        grammarDefault.BindRuntimeStep(1, in grammar);
        grammarDefault.SwapGrammar(in publication, advancePolicies: false);
        _ = grammarDefault.ChoosePolicyAction(policy, 0, features);
        CortexPolicyDecision grammarDecision = grammarDefault.ChoosePolicyAction(policy, 0, features);
        bool defaultGrammar = grammarDecision.Authority == CortexPolicyAuthorities.Grammar
            && grammarDecision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
            && grammarDecision.Action == grammarDecision.RawCandidateAction;

        output.WriteLine($"  policy authority ceiling · launchpad-readout={(teacherObservation ? "observed/launchpad" : "BROKEN")} external={(externalRejected ? "rejected" : "ACCEPTED")} trial={(trialRejected ? "rejected" : "ACCEPTED")} default-grammar={(defaultGrammar ? "byte-shape" : "BROKEN")} · {(teacherObservation && externalRejected && trialRejected && defaultGrammar ? "PASS" : "FAIL")}");
        return teacherObservation && externalRejected && trialRejected && defaultGrammar;
    }

    private static bool VerifyPublishedAuthorityResumeFixture(
        CortexConfig config,
        CortexPolicySchema schema,
        CortexPolicyID policy,
        MetricSample[] features,
        in global::Cogito.Grammar.InstallRevision published,
        in global::Cogito.Grammar.InstallRevision working,
        TextWriter output)
    {
        Cortex source = new(config);
        source.RegisterPolicy(schema);
        global::Cogito.Induct.RePairResult publishedGrammar = published.Snapshot.ToRePairResult();
        global::Cogito.Induct.RePairResult workingGrammar = working.Snapshot.ToRePairResult();
        source.BindRuntimeStep(1, in publishedGrammar);
        source.SwapGrammar(in published, advancePolicies: false);
        _ = source.ChoosePolicyAction(policy, 0, features);
        bool sourceWarm = source.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt sourceReceipt);
        source.BindRuntimeGrammar(in workingGrammar);
        using MemoryStream checkpoint = new();
        using (CkptWriter writer = new(checkpoint)) source.SavePolicyState(writer);

        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        checkpoint.Position = 0;
        using (CkptReader reader = new(checkpoint)) restored.LoadPolicyState(reader);
        restored.BindRuntimeStep(2, in workingGrammar);
        restored.SwapGrammar(in published, advancePolicies: false);
        bool restoredWarm = restored.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt restoredReceipt);
        GrammarSnapshot publishedSnapshot = published.Snapshot;
        GrammarSnapshot workingSnapshot = working.Snapshot;
        global::Cogito.Induct.RePairResult restoredGrammar = restored.Grammar;
        bool exact = sourceWarm
            && restoredWarm
            && sourceReceipt.Equals(restoredReceipt)
            && !publishedSnapshot.Matches(in workingGrammar)
            && restored.InstallRevision is { } restoredInstallRevision
            && restoredInstallRevision.Snapshot.Revision == published.Revision
            && workingSnapshot.Matches(in restoredGrammar);

        global::Cogito.Grammar.InstallRevision wrong = global::Cogito.Grammar.InstallRevision.FromRePair(
            published.Revision, published.Revision, in workingGrammar);
        bool wrongRejected;
        try
        {
            restored.SwapGrammar(in wrong, advancePolicies: false);
            _ = restored.ChoosePolicyAction(policy, 0, features);
            wrongRejected = false;
        }
        catch (InvalidDataException) { wrongRejected = true; }
        output.WriteLine($"  published authority resume · working-distinct={(!publishedSnapshot.Matches(in workingGrammar) ? "yes" : "NO")} publication={(restored.InstallRevision?.Revision == publishedSnapshot.Revision ? "P@rev" : "BROKEN")} warm={(exact ? "exact" : "BROKEN")} wrong-same-rev={(wrongRejected ? "rejected" : "ACCEPTED")}");
        return exact && wrongRejected;
    }

    private static bool VerifyReadoutForkAuthorityFixture(
        CortexConfig config,
        CortexPolicySchema schema,
        in CortexPolicyReadoutQuotaDecision funding,
        byte[] checkpoint,
        TextWriter output)
    {
        string root = Path.Combine(Environment.CurrentDirectory, ".tmp", "policy-readout-fork-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CortexForkSeed seed = CortexForkSeed.Materialize(101, checkpoint, [], []);
            bool exact = true;
            for (int index = 0; index < 4; index++)
            {
                string childDirectory = Path.Combine(root, $"child-{index}");
                seed.WriteRunDirectory(childDirectory);
                byte[] childImage = File.ReadAllBytes(Path.Combine(childDirectory, Checkpoint.FileName));
                Cortex child = new(config);
                child.RegisterPolicy(schema);
                // The parent image is the pre-forced-arm capture, written at schema 12;
                // the round trip must stay on that schema or the per-policy readout
                // fingerprint skews the reader into the funding section.
                using (MemoryStream stream = new(childImage))
                using (CkptReader reader = new(stream))
                    child.LoadPolicyState(reader, policySchema: 12);
                bool authority = child.TryReadPolicyReadoutQuota(
                    funding.Policy, funding.CandidateFingerprint, funding.QuotaStep,
                    out CortexPolicyReadoutQuotaDecision restoredFunding)
                    && restoredFunding == funding
                    && restoredFunding.AllocationSequence == funding.AllocationSequence
                    && restoredFunding.RosterDigest == funding.RosterDigest;
                using MemoryStream resaved = new();
                using (CkptWriter writer = new(resaved)) child.SavePolicyState(writer, 12);
                exact &= authority && childImage.AsSpan().SequenceEqual(resaved.ToArray());
            }
            // The journal verifier already exercises missing and tampered allocation
            // sidecars; the seed image itself is intentionally self-contained.
            output.WriteLine($"  readout fork authority · children=4/{(exact ? "validated" : "BROKEN")} checkpoint={(exact ? "SaveLoad-exact" : "DRIFT")}");
            return exact;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool VerifyReadoutEconomyFixture(
        CortexPolicySchema primarySchema,
        in global::Cogito.Induct.RePairResult grammar,
        TextWriter output)
    {
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ReadoutDeliberationQuota = 1,
                },
            },
        };
        CortexPolicyID primary = primarySchema.Policy;
        CortexPolicyID secondary = new("fixture.policy.z");
        CortexPolicySchema secondarySchema = new(secondary, primarySchema.FeatureCount, primarySchema.ActionCount, primarySchema.OutcomeCount);
        Cortex cortex = new(config);
        // Registration order is deliberately opposite the stable roster order.
        cortex.RegisterPolicy(secondarySchema);
        cortex.RegisterPolicy(primarySchema);
        cortex.BindRuntimeStep(0, in grammar);
        cortex.CompleteRuntimeStep(0);
        cortex.BindRuntimeStep(1, in grammar);
        cortex.CompleteRuntimeStep(1);
        cortex.CompleteRuntimeStep(1); // repeated completion is a no-op
        cortex.BindRuntimeStep(2, in grammar);
        cortex.CompleteRuntimeStep(2);
        cortex.BindRuntimeStep(3, in grammar);
        cortex.CompleteRuntimeStep(3);
        cortex.BindRuntimeStep(4, in grammar);
        cortex.CompleteRuntimeStep(4);
        List<CortexPolicyReadoutAllocation> allocations = new();
        cortex.AppendPolicyReadoutAllocations(allocations);
        List<CortexPolicyReadoutAllocationReceipt> accounts = new();
        cortex.AppendPolicyReadoutAllocationStates(accounts);
        bool rosterOrder = allocations.Count == 5
            && allocations[0].Policy.Equals(primary)
            && allocations[1].Policy.Equals(secondary)
            && allocations[2].Policy.Equals(primary)
            && allocations[3].Policy.Equals(secondary)
            && allocations[4].Policy.Equals(primary);
        bool qAndExpiry = allocations.Count == 5
            && allocations[4].ExpiredUnits == 1
            && allocations.TrueForAll(static row => row.AvailableBefore >= 0 && row.AvailableAfter >= 0)
            && accounts.Count == 2
            && accounts[0].AvailableUnits == 2
            && accounts[1].AvailableUnits == 2;
        using MemoryStream before = new();
        using (CkptWriter writer = new(before)) cortex.SavePolicyState(writer);
        Cortex restored = new(config);
        restored.RegisterPolicy(secondarySchema);
        restored.RegisterPolicy(primarySchema);
        before.Position = 0;
        using (CkptReader reader = new(before)) restored.LoadPolicyState(reader);
        restored.BindRuntimeStep(5, in grammar);
        restored.CompleteRuntimeStep(4);
        using MemoryStream after = new();
        using (CkptWriter writer = new(after)) restored.SavePolicyState(writer);
        bool terminalExact = before.ToArray().AsSpan().SequenceEqual(after.ToArray())
            && restored.ReadPolicyReadoutAllocationCount() == 5;
        output.WriteLine($"  readout economy detail · roster={(rosterOrder ? "reversed-registration/stable" : "BROKEN")} q={(qAndExpiry ? "2/bounded-expiry" : "BROKEN")} terminal={(terminalExact ? "exact-once" : "BROKEN")}");
        return rosterOrder && qAndExpiry && terminalExact;
    }

    private static bool VerifySameIDReadoutRetry(
        CortexConfig config,
        CortexPolicySchema schema,
        CortexPolicyID policy,
        MetricSample[] features,
        in global::Cogito.Induct.RePairResult grammar,
        in global::Cogito.Grammar.InstallRevision publication)
    {
        if (features.Length == 0) return false;
        Cortex retry = new(config);
        retry.RegisterPolicy(schema);
        retry.BindRuntimeStep(1, in grammar);
        retry.SwapGrammar(in publication, advancePolicies: false);
        retry.ChoosePolicyAction(policy, 0, features);
        retry.ChoosePolicyAction(policy, 0, features);
        using MemoryStream before = new();
        using (CkptWriter writer = new(before)) retry.SavePolicyState(writer);
        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        before.Position = 0;
        using (CkptReader reader = new(before)) restored.LoadPolicyState(reader);
        using MemoryStream after = new();
        using (CkptWriter writer = new(after)) restored.SavePolicyState(writer);
        return retry.ReadPolicyReadoutQuotaCount() == 1
            && restored.ReadPolicyReadoutQuotaCount() == 1
            && before.ToArray().AsSpan().SequenceEqual(after.ToArray());
    }

    private static bool VerifyDistinctDeniedReadoutIDs(
        CortexConfig config,
        CortexPolicySchema schema,
        CortexPolicyID policy,
        MetricSample[] features,
        in global::Cogito.Induct.RePairResult grammar,
        in global::Cogito.Grammar.InstallRevision publication)
    {
        if (features.Length == 0) return false;
        Cortex denied = new(config);
        denied.RegisterPolicy(schema);
        denied.BindRuntimeStep(1, in grammar);
        denied.SwapGrammar(in publication, advancePolicies: false);
        denied.ChoosePolicyAction(policy, 0, features);
        MetricSample[] alternate = features.ToArray();
        alternate[0] = new(alternate[0].MetricID, NumericValue.FromF64(321.5));
        denied.ChoosePolicyAction(policy, 0, alternate);
        using MemoryStream before = new();
        using (CkptWriter writer = new(before)) denied.SavePolicyState(writer);
        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        before.Position = 0;
        using (CkptReader reader = new(before)) restored.LoadPolicyState(reader);
        using MemoryStream after = new();
        using (CkptWriter writer = new(after)) restored.SavePolicyState(writer);
        CortexPolicyDecisionReadout deniedReadout = denied.ReadPolicyDecisionReadout(policy);
        CortexPolicyDecisionReadout restoredReadout = restored.ReadPolicyDecisionReadout(policy);
        bool deniedReadoutExact = deniedReadout.RawCandidateAction == -1
            && deniedReadout.SelectedCandidateAction == -1
            && deniedReadout.ReadoutCandidateOccurrenceDigest == 0
            && deniedReadout.ReadoutCandidateFingerprint == 0
            && deniedReadout.SelectionCause == CortexPolicySelectionCauses.Launchpad
            && restoredReadout == deniedReadout;
        return denied.ReadPolicyReadoutQuotaCount() == 2
            && restored.ReadPolicyReadoutQuotaCount() == 2
            && before.ToArray().AsSpan().SequenceEqual(after.ToArray())
            && deniedReadoutExact;
    }

    private static bool VerifyBoundaryShadowGateFixture(
        CortexPolicyID policy,
        MetricSample[] features,
        int actionCount,
        in global::Cogito.Grammar.InstallRevision publication,
        TextWriter output,
        out string failureDetail)
    {
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    ShadowDecisions = 3,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        CortexPolicySchema schema = new(policy, features.Length, actionCount, 1);
        Cortex cortex = new(config);
        cortex.RegisterPolicy(schema);
        CortexPolicyID boundaryPolicy = Homeostat.PolicyID;
        cortex.RegisterPolicy(Homeostat.PolicySchema);
        cortex.RegisterPolicyBoundaryDomain(HomeostatPolicyBoundaryDomain.Instance);
        PolicyBoundaryIdentity identity = new(
            boundaryPolicy, "fixture-candidate", "fixture-grammar", "fixture-production", "0", "criticality");
        PolicyBoundaryObligation obligation = new(identity);
        cortex.RegisterPolicyBoundaryObligation(obligation);
        global::Cogito.Induct.RePairResult grammar = publication.Snapshot.ToRePairResult();
        cortex.BindRuntimeStep(100, in grammar);
        cortex.SwapGrammar(in publication, advancePolicies: false);

        CortexPolicyDecision closedFirst = cortex.ChoosePolicyAction(policy, 0, features);
        CortexPolicyDecision closedSecond = cortex.ChoosePolicyAction(policy, 0, features);
        CortexPolicyDecision closedThird = cortex.ChoosePolicyAction(policy, 0, features);
        bool promotedAfterShadow = closedThird.Authority == CortexPolicyAuthorities.Grammar;
        bool readoutReady = cortex.TryReadPolicyReadout(policy, out CortexPolicyReadoutReceipt readout)
            && readout.IsExact
            && cortex.IsPolicyReadoutReady(policy, readout.Fingerprint);
        bool safeShadow = readoutReady
            && closedFirst.Authority == CortexPolicyAuthorities.Shadow
            && closedSecond.Authority == CortexPolicyAuthorities.Shadow
            && closedSecond.SelectionCause == CortexPolicySelectionCauses.ShadowCandidate
            && closedSecond.RawCandidateAction >= 0
            && closedSecond.Action == closedSecond.LaunchpadAction
            && promotedAfterShadow;

        PolicyBoundaryRational candidateBoundary = PolicyBoundaryRational.FromDouble(1.0);
        obligation.Propose(new PolicyBoundaryCandidate(
            candidateBoundary, PolicyBoundaryComparisons.LessThanOrEqual, "fixture-boundary"));
        PolicyBoundaryArmReceipt[] arms =
        [
            new(PolicyBoundaryArms.Baseline, 16, 1, 16, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.Candidate, 16, 1, 16, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.ForcedDivergentNull, 16, 0, 16, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.ReflexFrozenControl, 16, 0, 16, true, true, AdaptationEnabled: false),
            new(PolicyBoundaryArms.Baseline, 64, 1, 64, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.Candidate, 64, 1, 64, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.ForcedDivergentNull, 64, 0, 64, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.ReflexFrozenControl, 64, 0, 64, true, true, AdaptationEnabled: false),
            new(PolicyBoundaryArms.Baseline, 256, 1, 256, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.Candidate, 256, 1, 256, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.ForcedDivergentNull, 256, 0, 256, true, true, AdaptationEnabled: true),
            new(PolicyBoundaryArms.ReflexFrozenControl, 256, 0, 256, true, true, AdaptationEnabled: false),
        ];
        arms = [.. arms.Select(arm => arm with
        {
            RequestCount = arm.Arm is PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull ? 1 : 0,
            GuardAdmittedCount = arm.Arm is PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull ? 1 : 0,
            LastRequestDecisionID = arm.Arm is PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull
                ? new CortexPolicyDecisionID(checked((ulong)(arm.Horizon * 1000 + (int)arm.Arm + 11)))
                : default,
            LastRequestStep = arm.Arm is PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull ? arm.Horizon : -1,
            LastRequestReadout = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate => new CortexPolicyDecisionReadout(
                    0, 1, 2, 2, CortexPolicyAuthorities.Grammar, new GrammarRevisionID(1),
                    CortexPolicySelectionCauses.GrammarCandidate, 0xBEEFUL, 0xCAFEUL),
                PolicyBoundaryArms.ForcedDivergentNull => new CortexPolicyDecisionReadout(
                    0, 1, 3, 3, CortexPolicyAuthorities.Grammar, new GrammarRevisionID(1),
                    CortexPolicySelectionCauses.TrialOverride, 0xBEEFUL, 0xCAFEUL),
                _ => new CortexPolicyDecisionReadout(
                    0, -1, -1, 0, CortexPolicyAuthorities.Launchpad, new GrammarRevisionID(1),
                    CortexPolicySelectionCauses.Launchpad),
            },
            ExecutedDecisionID = new CortexPolicyDecisionID(checked((ulong)(arm.Horizon * 10 + (int)arm.Arm + 1))),
            ExecutedStep = arm.Horizon,
            ExecutedLaunchpadAction = 0,
            ExecutedRawCandidateAction = arm.Arm == PolicyBoundaryArms.Baseline ? -1 : 1,
            ExecutedSelectedCandidateAction = arm.Arm switch
            {
                PolicyBoundaryArms.Baseline => -1,
                PolicyBoundaryArms.Candidate => 2,
                PolicyBoundaryArms.ForcedDivergentNull => 3,
                _ => 1,
            },
            ExecutedAction = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate => 2,
                PolicyBoundaryArms.ForcedDivergentNull => 3,
                _ => 0,
            },
            ExecutedAuthority = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull => CortexPolicyAuthorities.Grammar,
                PolicyBoundaryArms.ReflexFrozenControl => CortexPolicyAuthorities.Shadow,
                _ => CortexPolicyAuthorities.Launchpad,
            },
            ExecutedSelectionCause = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate => CortexPolicySelectionCauses.GrammarCandidate,
                PolicyBoundaryArms.ForcedDivergentNull => CortexPolicySelectionCauses.TrialOverride,
                PolicyBoundaryArms.ReflexFrozenControl => CortexPolicySelectionCauses.ShadowCandidate,
                _ => CortexPolicySelectionCauses.Launchpad,
            },
            ExecutedReadoutFingerprint = readout.Fingerprint,
            ExecutedReadoutRevision = readout.Revision.Value,
            ExecutedReadoutOccurrenceDigest = arm.Arm == PolicyBoundaryArms.Baseline
                ? 0UL : readout.ReadoutCandidateOccurrenceDigest,
            ExecutedCandidateFingerprint = arm.Arm == PolicyBoundaryArms.Baseline
                ? 0UL : readout.ReadoutCandidateFingerprint,
            ExecutedCanonicalState = new PolicyCanonicalStateID(
                Homeostat.PolicyID, PolicyCanonicalStateKinds.Homeostat, PolicyCanonicalStates.HomeostatVersion, 0x205UL),
            ExecutedDecisionEventID = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                ? new TapeEventID((long)(arm.Horizon * 100 + 7)) : default,
            ForcedDivergenceSeed = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                ? 0xD1E3UL + (ulong)arm.Horizon : 0UL,
            Diverged = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull,
        })];
        PolicyBoundaryForkReceipt receipt = new(
            identity.ObligationID, PolicyBoundaryRational.Zero, candidateBoundary, [16, 64, 256], arms,
            ContinuityExact: true, MatchedSpend: true, ForcedNullBehaviorExecuted: true, Verified: true,
            SourceDecisionReadoutFingerprint: readout.Fingerprint, SourceDecisionReadoutRevision: readout.Revision.Value)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(1),
            SourceDecisionCandidateFingerprint = readout.ReadoutCandidateFingerprint,
        };
        obligation.Select(receipt, HomeostatPolicyBoundaryDomain.Instance);
        bool policyBoundaryCheckpoint = PolicyBoundaryObligation.VerifyCheckpointRoundTripFixture(obligation);
        byte[] boundaryPacket = TapePacketCreator.EncodePolicyBoundaryReceipt(
            boundaryPolicy, HomeostatPolicyBoundaryDomain.Instance, in receipt);
        bool policyBoundaryTape = PolicyBoundaryTapeVerifier.TryRead(boundaryPacket, HomeostatPolicyBoundaryDomain.Instance,
                out PolicyBoundaryForkReceipt decodedBoundary, out CortexPolicyID decodedBoundaryPolicy)
            && decodedBoundaryPolicy.Equals(boundaryPolicy)
            && PolicyBoundaryObligation.ComputeReceiptDigest(in decodedBoundary) == PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        CortexPolicyDecision opened = cortex.ChoosePolicyAction(policy, 0, features);
        bool verifiedGrammar = opened.Authority == CortexPolicyAuthorities.Grammar
            && opened.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
            && opened.Action == opened.RawCandidateAction;
        List<string> failures = [];
        if (!readoutReady) failures.Add("readout");
        if (!safeShadow)
            failures.Add($"shadow[first={closedFirst.Authority},second={closedSecond.Authority},cause={closedSecond.SelectionCause},raw={closedSecond.RawCandidateAction},action={closedSecond.Action},launch={closedSecond.LaunchpadAction}]");
        if (!verifiedGrammar) failures.Add("grammar");
        if (!policyBoundaryCheckpoint) failures.Add("checkpoint");
        if (!policyBoundaryTape) failures.Add("tape");
        failureDetail = failures.Count == 0 ? "exact" : string.Join(',', failures);
        output.WriteLine(
            $"  policy boundary shadow gate · ready={(readoutReady ? "yes" : "no")} safe-shadow={(safeShadow ? "yes" : "no")} verified-grammar={(verifiedGrammar ? "yes" : "no")} checkpoint={(policyBoundaryCheckpoint ? "exact" : "BROKEN")} tape={(policyBoundaryTape ? "exact" : "BROKEN")} · {(safeShadow && verifiedGrammar && policyBoundaryCheckpoint && policyBoundaryTape ? "PASS" : "FAIL")}");
        return safeShadow && verifiedGrammar && policyBoundaryCheckpoint && policyBoundaryTape;
    }

    private static bool VerifyRetiredPolicyStateRejected(CortexConfig config, CortexPolicySchema schema, Cortex source)
    {
        byte[] schema5;
        bool schema5Representable;
        using (MemoryStream legacy = new())
        {
            try
            {
                using CkptWriter writer = new(legacy);
                source.SavePolicyState(writer, policySchema: 5);
                schema5Representable = true;
            }
            catch (InvalidDataException)
            {
                // Current execution-history custody is intentionally not
                // representable in the retired schema; that rejection is the
                // compatibility verdict for a live funded trial.
                schema5Representable = false;
            }
            schema5 = legacy.ToArray();
        }
        bool schema5Exact = !schema5Representable;
        if (schema5Representable)
        {
            Cortex schema5Restored = new(config);
            schema5Restored.RegisterPolicy(schema);
            using (MemoryStream legacy = new(schema5, writable: false))
            using (CkptReader reader = new(legacy))
                schema5Restored.LoadPolicyState(reader, policySchema: 5);
            using (MemoryStream legacy = new())
            {
                using (CkptWriter writer = new(legacy)) schema5Restored.SavePolicyState(writer, policySchema: 5);
                schema5Exact = schema5.AsSpan().SequenceEqual(legacy.ToArray());
            }
        }

        bool saveRejected;
        using (MemoryStream legacy = new())
        using (CkptWriter writer = new(legacy))
        {
            try { source.SavePolicyState(writer, policySchema: 2); saveRejected = false; }
            catch (InvalidDataException) { saveRejected = true; }
        }

        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        bool loadRejected;
        using (MemoryStream legacy = new())
        using (CkptReader reader = new(legacy))
        {
            try { restored.LoadPolicyState(reader, policySchema: 2); loadRejected = false; }
            catch (InvalidDataException) { loadRejected = true; }
        }
        return schema5Exact && saveRejected && loadRejected;
    }

    private static bool VerifyCorruptReadoutCache()
    {
        bool revisionMismatchRejected = RejectsCacheImage(static writer =>
        {
            writer.U64(1);
            writer.I32(1);
            writer.Bytes([1, 2, 3]);
            writer.I32(0);
            writer.I32(0);
            writer.I64(1);
            writer.I32(1);
            writer.U64(2); // Entry revision must equal the cache revision above.
            writer.I32(1);
            writer.I32(1);
            writer.I32(0);
            writer.I64(3);
            writer.I64(0);
            writer.U64(4);
        });
        bool duplicateContextRejected = RejectsCacheImage(static writer =>
        {
            writer.U64(1);
            writer.I32(2);
            WriteCacheEntry(writer, [1, 2, 3], 0, 0, 1);
            WriteCacheEntry(writer, [1, 2, 3], 0, 1, 1);
        });
        bool actionUpperBoundRejected = RejectsCacheImage(static writer =>
        {
            writer.U64(1);
            writer.I32(1);
            WriteCacheEntry(writer, [1, 2, 3], 0, 3, 1);
        }, actionCount: 3);
        bool oversizedContextRejected = RejectsCacheImage(static writer =>
        {
            writer.U64(1);
            writer.I32(1);
            writer.I32(PolicyReadoutCache.MaxContextBytes + 1);
        });

        GrammarRevisionID revision = new(1);
        GrammarPolicyContextKey first = new([1, 2, 3], 0);
        GrammarPolicyContextKey second = new([4, 5, 6], 0);
        GrammarContinuationQuotaCompletion settlement = new(1, 1, 0, 3, 0);
        GrammarPolicyDecision firstDecision = new(0, 1, 1, revision, settlement, 1);
        GrammarPolicyDecision secondDecision = new(1, 2, 1, revision, settlement, 2);
        CortexPolicyID policy = new("fixture.policy");
        PolicyReadoutCache collisions = new(ForcedCollisionComparer.Instance);
        collisions.Store(policy, revision, in first, in firstDecision);
        collisions.Store(policy, revision, in second, in secondDecision);
        bool collisionExact = collisions.Count == 2
            && collisions.TryGet(revision, in first, out GrammarPolicyDecision readFirst) && readFirst == firstDecision
            && collisions.TryGet(revision, in second, out GrammarPolicyDecision readSecond) && readSecond == secondDecision;
        return revisionMismatchRejected && duplicateContextRejected && actionUpperBoundRejected
            && oversizedContextRejected && collisionExact;

        static bool RejectsCacheImage(Action<CkptWriter> write, int actionCount = 0)
        {
            using MemoryStream image = new();
            using (CkptWriter writer = new(image)) write(writer);
            image.Position = 0;
            try
            {
                PolicyReadoutCache cache = new();
                using CkptReader reader = new(image);
                cache.Load(reader, actionCount);
                return false;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or EndOfStreamException)
            {
                return true;
            }
        }

        static void WriteCacheEntry(CkptWriter writer, byte[] context, int depth, int action, ulong revision)
        {
            writer.Bytes(context);
            writer.I32(depth);
            writer.I32(action);
            writer.I64(1);
            writer.I32(1);
            writer.U64(revision);
            writer.I32(1);
            writer.I32(1);
            writer.I32(0);
            writer.I64(3);
            writer.I64(0);
            writer.U64(4);
        }
    }

    private static bool VerifyCorruptReadoutFundingCheckpoint(
        CortexConfig config,
        CortexPolicySchema schema,
        byte[] source,
        CortexPolicyQuotaDecisionID fundingID,
        CortexPolicyID policy,
        in CortexPolicyReadoutCheckpointLayout layout)
    {
        CortexPolicyReadoutQuotaCheckpointRow fundingRow = layout.QuotaRows
            .FirstOrDefault(row => row.QuotaDecisionID.Equals(fundingID));
        CortexPolicyReadoutCompletionCheckpointRow settlementRow = layout.CompletionRows
            .FirstOrDefault(row => row.QuotaDecisionID.Equals(fundingID));
        if (fundingRow.QuotaDecisionID.Value == 0 || settlementRow.QuotaDecisionID.Value == 0)
            return false;

        int fundingStart = CheckedOffset(fundingRow.RowOffset, source.Length);
        int recordLength = checked((int)fundingRow.RowLength);
        int enumOffset = CheckedOffset(fundingRow.DecisionOffset, source.Length);
        int settlementStart = CheckedOffset(settlementRow.RowOffset, source.Length);
        int totalsStart = CheckedOffset(layout.UsedUnitsOffset, source.Length);
        int fundingCountOffset = CheckedOffset(layout.QuotaCountOffset, source.Length);
        if (recordLength <= 0 || enumOffset >= fundingStart + recordLength
            || settlementStart + settlementRow.RowLength > source.Length)
            return false;

        bool fundingEnum = Rejects(Mutate(source, bytes => bytes[enumOffset] = 0xFF));
        bool orphanSettlement = Rejects(Mutate(source, bytes => WriteU64(bytes, settlementStart, 0)));
        bool totals = Rejects(Mutate(source, bytes => WriteI64(bytes, totalsStart, ReadI64(bytes, totalsStart) + 1)));
        bool duplicate = Rejects(DuplicateFundingRow(source, fundingStart, recordLength, fundingCountOffset));
        Trace.Cortex.Boundary("policy.readout-funding-corruption",
            $"policy={policy.Value} funding={fundingID.Value:X16} start={fundingStart} settlement={settlementStart} length={recordLength} enum={enumOffset} totals={totalsStart} outcomes={fundingEnum}/{orphanSettlement}/{totals}/{duplicate}");
        return fundingEnum && orphanSettlement && totals && duplicate;

        static int CheckedOffset(long offset, int length)
        {
            if (offset < 0 || offset > int.MaxValue || offset >= length)
                throw new InvalidDataException($"checkpoint layout offset {offset} is outside the fixture image");
            return (int)offset;
        }

        bool Rejects(byte[] image)
        {
            Cortex restored = new(config);
            restored.RegisterPolicy(schema);
            try
            {
                using MemoryStream stream = new(image);
                using CkptReader reader = new(stream);
                restored.LoadPolicyState(reader);
                return false;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or EndOfStreamException)
            {
                return true;
            }
        }

        static byte[] Mutate(byte[] source, Action<byte[]> mutation)
        {
            byte[] copy = (byte[])source.Clone();
            mutation(copy);
            return copy;
        }

        static byte[] DuplicateFundingRow(byte[] source, int start, int length, int countOffset)
        {
            byte[] copy = new byte[source.Length + length];
            int insert = start + length;
            Buffer.BlockCopy(source, 0, copy, 0, insert);
            Buffer.BlockCopy(source, start, copy, insert, length);
            Buffer.BlockCopy(source, insert, copy, insert + length, source.Length - insert);
            WriteI32(copy, countOffset, checked(ReadI32(copy, countOffset) + 1));
            return copy;
        }

        static long ReadI64(byte[] bytes, int offset)
            => BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)));
        static int ReadI32(byte[] bytes, int offset)
            => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
        static void WriteU64(byte[] bytes, int offset, ulong value)
            => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, sizeof(ulong)), value);
        static void WriteI64(byte[] bytes, int offset, long value)
            => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, sizeof(long)), value);
        static void WriteI32(byte[] bytes, int offset, int value)
            => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);
    }

    private sealed class ForcedCollisionComparer : IEqualityComparer<GrammarPolicyContextKey>
    {
        public static readonly ForcedCollisionComparer Instance = new();
        public bool Equals(GrammarPolicyContextKey left, GrammarPolicyContextKey right) => left.Equals(right);
        public int GetHashCode(GrammarPolicyContextKey context) => 0;
    }

    private static bool VerifyReadoutFundingJournal(
        in CortexPolicyReadoutQuotaDecision funding,
        in CortexPolicyTrialCompletion settlement,
        Cortex cortex,
        TextWriter output)
    {
        CortexPolicyReadoutQuotaDecision fundingRow = funding;
        CortexPolicyTrialCompletion settlementRow = settlement;
        List<CortexPolicyReadoutAllocation> allocationRows = new();
        cortex.AppendPolicyReadoutAllocations(allocationRows);
        string root = Path.Combine(Environment.CurrentDirectory, "tmp", "policy-readout-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            bool baseline = WriteAndVerify("baseline", null, output);
            bool corruptQuotaID = !WriteAndVerify("funding-id", static (fundingColumns, _, _) =>
                fundingColumns[0] = "0", TextWriter.Null);
            bool corruptOutcome = !WriteAndVerify("outcome", static (_, settlementColumns, _) =>
                settlementColumns[4] = CortexPolicyVerifierOutcomes.NotRecorded.ToString(), TextWriter.Null);
            bool corruptActual = !WriteAndVerify("actual", static (_, settlementColumns, _) =>
                settlementColumns[1] = (long.Parse(settlementColumns[1], CultureInfo.InvariantCulture) + 1)
                    .ToString(CultureInfo.InvariantCulture), TextWriter.Null);
            bool corruptRefund = !WriteAndVerify("refund", static (_, settlementColumns, _) =>
                settlementColumns[2] = (long.Parse(settlementColumns[2], CultureInfo.InvariantCulture) + 1)
                    .ToString(CultureInfo.InvariantCulture), TextWriter.Null);
            bool corruptAllocationStep = !WriteAndVerify("allocation-step", static (_, _, allocationColumns) =>
                allocationColumns[1] = (int.Parse(allocationColumns[1], CultureInfo.InvariantCulture) + 2)
                    .ToString(CultureInfo.InvariantCulture), TextWriter.Null);
            bool corruptAllocationBalance = !WriteAndVerify("allocation-balance", static (_, _, allocationColumns) =>
                allocationColumns[4] = (long.Parse(allocationColumns[4], CultureInfo.InvariantCulture) + 1)
                    .ToString(CultureInfo.InvariantCulture), TextWriter.Null);
            bool corruptAllocationNegative = !WriteAndVerify("allocation-negative", static (_, _, allocationColumns) =>
                allocationColumns[7] = "-1", TextWriter.Null);
            bool missingAllocation = !WriteMissingAllocationAndVerify(TextWriter.Null);
            bool duplicateReused = !WriteDuplicateRowsAndVerify(
                "duplicate-reused", fundingRow,
                fundingRow with { Decision = CortexPolicyQuotaDecisions.Reused, UsedUnits = 0 },
                settlementRow, TextWriter.Null);
            bool duplicateDenied = !WriteDuplicateRowsAndVerify(
                "duplicate-denied", fundingRow,
                fundingRow with { Decision = CortexPolicyQuotaDecisions.Denied, HeldUnits = 0, UsedUnits = 0 },
                settlementRow, TextWriter.Null);
            output.WriteLine($"  readout journal detail · baseline={baseline} funding-id={corruptQuotaID} outcome={corruptOutcome} actual={corruptActual} refund={corruptRefund} allocation-step={corruptAllocationStep} allocation-balance={corruptAllocationBalance} allocation-negative={corruptAllocationNegative} missing-allocation={missingAllocation} duplicate-reused={duplicateReused} duplicate-denied={duplicateDenied}");
            return baseline && corruptQuotaID && corruptOutcome && corruptActual && corruptRefund
                && corruptAllocationStep && corruptAllocationBalance && corruptAllocationNegative && missingAllocation
                && duplicateReused && duplicateDenied;

            bool WriteAndVerify(string name, Action<string[], string[], string[]>? corrupt, TextWriter receiptOutput)
                => WriteRowsAndVerify(name, fundingRow, settlementRow, corrupt, receiptOutput);

            bool WriteRowsAndVerify(
                string name,
                CortexPolicyReadoutQuotaDecision fundingSource,
                CortexPolicyTrialCompletion settlementSource,
                Action<string[], string[], string[]>? corrupt,
                TextWriter receiptOutput)
            {
                string directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                string[] fundingColumns = Cortex.FormatPolicyReadoutQuotaRow(in fundingSource).Split('\t');
                string[] settlementColumns = Cortex.FormatPolicyTrialCompletionRow(in settlementSource).Split('\t');
                CortexPolicyReadoutAllocation allocationSource = allocationRows[^1];
                string[] allocationColumns = Cortex.FormatPolicyReadoutAllocationRow(in allocationSource).Split('\t');
                corrupt?.Invoke(fundingColumns, settlementColumns, allocationColumns);
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_funding.journal.tsv"),
                    "funding_id\tpolicy\tcandidate_fingerprint\tgrammar_revision\tcontext_digest\tcontext_bytes\tdeliberation_depth\tfunding_step\tplanned_units\treserved_units\tdecision\tcharged_units\tremaining_budget\tallocation_sequence\troster_digest\taccount_balance_before\taccount_balance_after\n"
                    + string.Join('\t', fundingColumns) + "\n");
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_settlements.journal.tsv"),
                    "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds\n"
                    + string.Join('\t', settlementColumns) + "\n");
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_allocations.journal.tsv"),
                    FormatAllocationJournal(allocationColumns));
                return CortexPolicyTrialJournalVerifier.VerifyReadout(directory, receiptOutput).Passed;
            }

            bool WriteMissingAllocationAndVerify(TextWriter receiptOutput)
            {
                string directory = Path.Combine(root, "missing-allocation");
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_funding.journal.tsv"),
                    "funding_id\tpolicy\tcandidate_fingerprint\tgrammar_revision\tcontext_digest\tcontext_bytes\tdeliberation_depth\tfunding_step\tplanned_units\treserved_units\tdecision\tcharged_units\tremaining_budget\tallocation_sequence\troster_digest\taccount_balance_before\taccount_balance_after\n"
                    + Cortex.FormatPolicyReadoutQuotaRow(in fundingRow) + "\n");
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_settlements.journal.tsv"),
                    "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds\n"
                    + Cortex.FormatPolicyTrialCompletionRow(in settlementRow) + "\n");
                return CortexPolicyTrialJournalVerifier.VerifyReadout(directory, receiptOutput).Passed;
            }

            bool WriteDuplicateRowsAndVerify(
                string name,
                CortexPolicyReadoutQuotaDecision firstFunding,
                CortexPolicyReadoutQuotaDecision duplicateFunding,
                CortexPolicyTrialCompletion settlementSource,
                TextWriter receiptOutput)
            {
                string directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_funding.journal.tsv"),
                    "funding_id\tpolicy\tcandidate_fingerprint\tgrammar_revision\tcontext_digest\tcontext_bytes\tdeliberation_depth\tfunding_step\tplanned_units\treserved_units\tdecision\tcharged_units\tremaining_budget\tallocation_sequence\troster_digest\taccount_balance_before\taccount_balance_after\n"
                    + Cortex.FormatPolicyReadoutQuotaRow(in firstFunding) + "\n"
                    + Cortex.FormatPolicyReadoutQuotaRow(in duplicateFunding) + "\n");
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_settlements.journal.tsv"),
                    "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds\n"
                    + Cortex.FormatPolicyTrialCompletionRow(in settlementSource) + "\n");
                File.WriteAllText(
                    Path.Combine(directory, "policy_readout_allocations.journal.tsv"),
                    FormatAllocationJournal(null));
                return CortexPolicyTrialJournalVerifier.VerifyReadout(directory, receiptOutput).Passed;
            }

            string FormatAllocation(CortexPolicyReadoutAllocation allocation)
                => Cortex.FormatPolicyReadoutAllocationRow(in allocation);

            string FormatAllocationJournal(string[]? replacementLast)
            {
                StringBuilder text = new("sequence\tstep\troster_digest\tpolicy\tbalance_before\tcredited_units\texpired_units\tbalance_after\n");
                for (int i = 0; i < allocationRows.Count; i++)
                {
                    string row = i == allocationRows.Count - 1 && replacementLast is not null
                        ? string.Join('\t', replacementLast)
                        : FormatAllocation(allocationRows[i]);
                    text.Append(row).Append('\n');
                }
                return text.ToString();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static global::Cogito.Grammar.InstallRevision BuildInstallRevision(
        CortexPolicyID policy,
        MetricSample[] features,
        int actionCount,
        int extraActionOne,
        ulong revision,
        out byte[] tapeBytes)
    {
        using Tape tape = new();
        Journal journal = new();
        int step = 0;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, policy, 0, features, actionCount);
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, policy, 1, features, actionCount);
        }
        TapePacketCreator.AppendPolicyExample(tape, journal, step++, policy, 2, features, actionCount);
        for (int repeat = 0; repeat < extraActionOne; repeat++)
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, policy, 1, features, actionCount);
        tapeBytes = tape.Concat();
        global::Cogito.Induct.RePairResult result = Engine.Induce(tape, 1).Result;
        global::Cogito.Grammar.GrammarRevisionID revisionID = new(revision);
        global::Cogito.Grammar.GrammarRevisionID parent = revision == 1
            ? global::Cogito.Grammar.GrammarRevisionID.Zero
            : new global::Cogito.Grammar.GrammarRevisionID(revision - 1);
        return global::Cogito.Grammar.InstallRevision.FromRePair(revisionID, parent, in result);
    }

    private static global::Cogito.Grammar.InstallRevision BuildCanonicalInstallRevision(
        CortexPolicyID policy,
        in PolicyCanonicalStateID state,
        int actionCount,
        int extraActionOne,
        ulong revision,
        out byte[] tapeBytes)
        => BuildCanonicalInstallRevision(policy, [state], actionCount, extraActionOne, 1, revision, out tapeBytes);

    private static global::Cogito.Grammar.InstallRevision BuildCanonicalInstallRevision(
        CortexPolicyID policy,
        PolicyCanonicalStateID[] states,
        int actionCount,
        int extraActionOne,
        int extraAction,
        ulong revision,
        out byte[] tapeBytes)
    {
        using Tape tape = new();
        Journal journal = new();
        int step = 0;
        MetricSample[] evidence = [new(new MetricID(640), NumericValue.FromF64(step + 0.25))];
        for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
        {
            PolicyCanonicalStateID state = states[stateIndex];
            for (int repeat = 0; repeat < 3; repeat++)
            {
                TapePacketCreator.AppendPolicyCanonicalExample(tape, journal, step++, policy, in state, 0, evidence, actionCount);
                TapePacketCreator.AppendPolicyCanonicalExample(tape, journal, step++, policy, in state, 1, evidence, actionCount);
            }
            TapePacketCreator.AppendPolicyCanonicalExample(tape, journal, step++, policy, in state, 2, evidence, actionCount);
            for (int repeat = 0; repeat < extraActionOne; repeat++)
                TapePacketCreator.AppendPolicyCanonicalExample(tape, journal, step++, policy, in state, 1, evidence, actionCount);
            for (int repeat = 0; repeat < extraAction; repeat++)
                TapePacketCreator.AppendPolicyCanonicalExample(tape, journal, step++, policy, in state, 0, evidence, actionCount);
        }
        tapeBytes = tape.Concat();
        global::Cogito.Induct.RePairResult result = Engine.Induce(tape, 1).Result;
        global::Cogito.Grammar.GrammarRevisionID revisionID = new(revision);
        global::Cogito.Grammar.GrammarRevisionID parent = revision == 1
            ? global::Cogito.Grammar.GrammarRevisionID.Zero
            : new global::Cogito.Grammar.GrammarRevisionID(revision - 1);
        return global::Cogito.Grammar.InstallRevision.FromRePair(revisionID, parent, in result);
    }

    private static global::Cogito.Grammar.InstallRevision BuildCanonicalVerifierInstallRevision(
        CortexPolicyID policy,
        PolicyCanonicalStateID[] states,
        int actionCount,
        ulong revision)
    {
        using Tape tape = new();
        Journal journal = new();
        int step = 0;
        MetricSample[] evidence = [new(new MetricID(640), NumericValue.FromF64(0.25))];
        for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
        {
            PolicyCanonicalStateID state = states[stateIndex];
            HomeostatPolicyContext context = new(
                (HomeostatPolicyConditions)(state.Value & 0xFF),
                (state.Value & (1UL << 8)) != 0,
                (state.Value & (1UL << 9)) != 0);
            HomeostatPolicyProgram program = Homeostat.CompilePolicyProgram(in context);
            int action = Homeostat.FindDestinationPolicyAction(in program);
            for (int repeat = 0; repeat < 5; repeat++)
                TapePacketCreator.AppendPolicyCanonicalExample(tape, journal, step++, policy, in state, action, evidence, actionCount);
        }
        global::Cogito.Induct.RePairResult result = Engine.Induce(tape, 1).Result;
        global::Cogito.Grammar.GrammarRevisionID revisionID = new(revision);
        global::Cogito.Grammar.GrammarRevisionID parent = revision == 1
            ? global::Cogito.Grammar.GrammarRevisionID.Zero
            : new global::Cogito.Grammar.GrammarRevisionID(revision - 1);
        return global::Cogito.Grammar.InstallRevision.FromRePair(revisionID, parent, in result);
    }
}
