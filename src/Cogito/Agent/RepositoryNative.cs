namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

/// Native repository navigation: the query is the only initial intake. A repository
/// snapshot is an external authority, not a corpus; source bytes cross into cognition
/// only when a ToolCall returns an observation and the action owner appends that reply.
public static class RepositoryNative
{
    private const string AuthorityFile = "repository-native.ron";

    /// The custody roles a tool result must carry, and the one role it MAY carry beyond them.
    /// Custody is mandatory: the access happened and the accounting records it whether or not the
    /// bytes were worth eating. GrammarInput is the intake organ's verdict — it marks the result
    /// as diet, and only marginal information earns it — so a custody check that demanded exact
    /// equality would refuse every result the organism actually learned from.
    private const TapeEventRoles WorldObservationCustodyRoles = TapeEventRoles.Measurement | TapeEventRoles.AuditOnly;

    internal static bool CarriesWorldObservationRoles(TapeEventRoles roles)
        => (roles & WorldObservationCustodyRoles) == WorldObservationCustodyRoles
            && (roles & ~(WorldObservationCustodyRoles | TapeEventRoles.GrammarInput)) == 0;

    internal static class Policy
    {
        internal const string CanonicalStateFormulaVersion = "repository-native-policy-state-v2-quantized";
        internal const ushort CanonicalStateVersion = 2;
        internal static readonly CortexPolicyID ID = CortexPolicyID.Parse("repository.native-navigation");
        internal static readonly CortexPolicySchema Schema = new(
            ID,
            featureCount: 8,
            actionCount: 6,
            outcomeCount: 3,
            authorityCeiling: CortexPolicyModes.Autonomic,
            admission: CortexPolicyAdmissionKinds.Verified);

        /// The KIND of situation the crawler is in — deliberately coarse, because a policy plane learns
        /// by RECURRENCE and cannot learn from a state it never sees twice.
        ///
        /// v1 hashed the ordered frontier authority root, the frontier revision and the raw counts. Every
        /// one of those changes on every step, so v1 encoded the organism's exact POSITION and the state
        /// was unique by construction: measured over a 120-step crawl, 360 teacher examples produced 360
        /// distinct states, maximum repetition ONE. A grammar mints a rule when a substring recurs, so
        /// the plane could never form a single rule — at any budget, across any number of lives. That is
        /// the whole reason no learned selection had ever occurred.
        ///
        /// v2 keeps only what makes two moments the SAME KIND of moment: the counts bucketed by
        /// magnitude (the difference between having seen 3 paths and 4 is noise; 3 versus 300 is not),
        /// and whether the loop has answered. Identity inputs are dropped outright — a Merkle root is a
        /// fingerprint, and fingerprints are precisely what must not enter a state partition.
        internal static PolicyCanonicalStateID State(int observedPaths, int frontierCount, bool answered)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(CanonicalStateFormulaVersion));
            Span<byte> scalar = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(scalar, MagnitudeBucket(observedPaths));
            hash.AppendData(scalar);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(scalar, MagnitudeBucket(frontierCount));
            hash.AppendData(scalar);
            hash.AppendData([(byte)(answered ? 1 : 0)]);
            ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash.GetHashAndReset());
            return new(ID, PolicyCanonicalStateKinds.Generic, CanonicalStateVersion, value);
        }

        /// floor(log2(count + 1)) — a count's ORDER OF MAGNITUDE. Adjacent steps land in the same bucket
        /// almost always, which is exactly the recurrence the grammar needs, while a tenfold growth still
        /// moves the organism to a genuinely different kind of moment.
        private static int MagnitudeBucket(int count)
            => count <= 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)count + 1);

        internal static bool IsCanonicalState(PolicyCanonicalStateID state)
        {
            return state.IsValidFor(ID)
                && state.Kind == PolicyCanonicalStateKinds.Generic
                && state.Version == CanonicalStateVersion;
        }

        internal static int Action(RepositoryCandidateSpecies species) => (int)species;

        internal static bool TrySpecies(int action, out RepositoryCandidateSpecies species)
        {
            if ((uint)action < 6)
            {
                species = (RepositoryCandidateSpecies)action;
                return true;
            }
            species = default;
            return false;
        }
    }

    internal enum PolicyMetricIDs : ushort
    {
        FrontierCandidates = 900,
        EligibleCandidates,
        ObservedPaths,
        FrontierRevision,
        QueryLength,
        HasOccurrenceCheck,
        HasAnswer,
        CandidateSpecies,
        EvidenceYield = 920,
        OccurrenceCheckResult,
        TerminalSourceBacking,
    }

    public static int Run(string root, string query, int steps = 32, string? glob = null,
        RepositoryLoopClosureRegistration? registration = null,
        RepositoryToolArms arm = RepositoryToolArms.ToolsLive)
        => Run(root, query, steps, glob, registration, arm, out _);

    /// The overload an assay needs: the run directory it just filled is the only place the
    /// sealed terminal evidence exists, and searching for it afterwards by mtime would race
    /// every peer arm running beside it.
    internal static int Run(string root, string query, int steps, string? glob,
        RepositoryLoopClosureRegistration? registration,
        RepositoryToolArms arm,
        out Run? destination)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("  nav --repo requires a repository root");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("  nav --repo requires --query (the only initial intake)");
            return 1;
        }

        var runtime = new RepositoryNativeRuntime(root, query, glob, configuredSteps: steps, registration: registration, arm: arm);
        CortexConfig config = CreateConfig(runtime, steps);
        Run created = Cogito.Run.New(config.RunName);
        destination = created;
        runtime.PrepareResumeArtifacts(created);

        Console.WriteLine($"nav --repo · root={runtime.RootPath} · files={runtime.World.FileCount} · world={runtime.World.WorldSHA256} · query={runtime.QuerySHA256} · steps={config.Steps} · arm={arm}");
        return CreateNativeCortex(config).Run(created);
    }

    internal static Cortex CreateCheckpointRuntime(CortexRunConfig persisted, string? runDirectory)
    {
        if (string.IsNullOrWhiteSpace(runDirectory))
            throw new InvalidDataException("native repository checkpoint requires its run directory");
        string authorityPath = Path.Combine(runDirectory, AuthorityFile);
        if (!File.Exists(authorityPath))
            throw new InvalidDataException($"native repository checkpoint is missing {AuthorityFile}");

        Dictionary<string, string> authority = RonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(authorityPath));
        var runtime = new RepositoryNativeRuntime(
            ReadAuthority(authority, "root"),
            ReadAuthority(authority, "query"),
            ReadAuthority(authority, "glob"),
            persisted.WScale,
            configuredSteps: persisted.Steps,
            registration: RepositoryNativeTaskAuthority.TryRead(authority, out RepositoryLoopClosureRegistration? registration) ? registration : null,
            arm: RepositoryToolArmNames.Parse(ReadAuthority(authority, "arm")));
        if (!string.Equals(ReadAuthority(authority, "world_sha256"), runtime.World.WorldSHA256, StringComparison.Ordinal)
            || !string.Equals(ReadAuthority(authority, "query_sha256"), runtime.QuerySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("native repository checkpoint authority changed (world/query)");

        return CreateNativeCortex(CreateConfig(runtime, persisted.Steps));
    }

    private static Cortex CreateNativeCortex(CortexConfig config)
    {
        Cortex cortex = new(config);
        cortex.EnableLoopLineage();
        return cortex;
    }

    private static string ReadAuthority(Dictionary<string, string> authority, string key)
        => authority.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"native repository checkpoint authority is missing {key}");

    private static CortexConfig CreateConfig(RepositoryNativeRuntime runtime, int steps)
        => new()
        {
            RunName = "nav-repo",
            Steps = runtime.Registration?.Horizon ?? Math.Max(1, steps),
            Seed = runtime.Registration?.Seed ?? 0xC07E5EEDUL,
            Curriculum = new CortexLocCurriculum { WorkloadCount = 1 },
            RuntimeCurriculum = runtime,
            ActionsPerStep = 1,
            Tools = [.. CreateNativeTools(runtime)],
            ActionPolicies = [new RepositoryNativeActionPolicy(runtime)],
            Rewards = [new RepositoryNativeObservationReward(runtime)],
            Generation = new CortexGenerationConfig { BlockLength = 256, MaxBlockBytes = 4096 },
            Learning = new CortexLearningConfig
            {
                ConsolidationPhaseControl = CortexConsolidationPhaseControl.Interval,
                // Republication cadence, and it is load-bearing rather than tuning. InstallRevision fires
                // at seed, stride, SLEEP, antiunify and land; with interval aestivation off, a crawl
                // published exactly twice — a seed at the start and a land at the very end — so the
                // policy readout scanned a grammar snapshot older than every policy rule the plane
                // ever minted. Measured: 8 policy rules in the final grammar, ZERO in either published
                // revision. A slow plane cannot be read out of a photograph taken before it learned.
                IntervalConsolidationPhase = 16,
                EvidenceWeightScale = runtime.WScale,
                CrossReflect = false,
                ReplayRatio = 0,
                NearDupe = true,
                Antiunify = false,
                Loom = true,
                Shed = true,
                Rhythm = false,
                Homeostat = new CortexHomeostatConfig
                {
                    Policy = HomeoPolicies.Reflex,
                    Autonomy = HomeostatAutonomyModes.Off,
                },
                Policies = new CortexPolicyLearningConfig
                {
                    TrialAllocation = new CortexPolicyTrialAllocationConfig
                    {
                        ArmSteps = RepositoryPolicyBoundaryDomain.Instance.ArmTopology.TrialArmSteps,
                        Identity = RepositoryPolicyBoundaryDomain.Instance.ArmTopology.TrialAllocationIdentity,
                        Authority = RepositoryPolicyBoundaryDomain.Instance.ArmTopology.TrialAllocationAuthority,
                    },
                },
            },
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0, CurveEvery = 1 },
        };

    private static CortexTool[] CreateNativeTools(RepositoryNativeRuntime runtime)
    {
        RepositoryNativeToolAuthority.Validate();
        return RepositoryNativeToolAuthority.Descriptors
            .Select(descriptor => CreateNativeTool(runtime, descriptor))
            .ToArray();
    }

    private static CortexTool CreateNativeTool(RepositoryNativeRuntime runtime, RepositoryNativeToolDescriptor descriptor)
    {
        if (!RepositoryNativeToolAuthority.Matches(descriptor.Verb, descriptor.Name, descriptor.IsTerminal))
            throw new InvalidDataException("native tool mount is outside the registered schema");
        return new RepositoryNativeTool(runtime, descriptor);
    }

    internal static bool TryVerifyReadout(
        Cortex cortex,
        in CortexPolicyDecision decision,
        out TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance)
    {
        if (cortex.MountedCurriculum is RepositoryNativeRuntime runtime)
            return runtime.TryVerifyReadout(cortex, in decision, out decisionEventID, out provenance);
        decisionEventID = new TapeEventID(-1);
        provenance = default;
        return false;
    }

    /// The Cortex runtime owns the ICurriculum boundary, but this runtime has no
    /// curriculum schedule: the query is seeded once and every later observation is admitted
    /// only by a current typed frontier proposal. The name keeps that distinction visible.
    internal interface ICortexLoadedStateVerifier
    {
        void VerifyLoadedState(Cortex cortex, Tape tape, Journal journal);
    }

    private sealed class RepositoryNativeRuntime : ICurriculum, ICurriculumCheckpointDeltaOwner, ICortexLoadedStateVerifier, ICurriculumTerminalTransition, ICurriculumMomentumHaltVeto
    {
        private readonly byte[] _queryBytes;
        private readonly List<string> _observedPaths = new();
        private readonly List<string> _observedPathLog = new();
        private readonly RepositoryAccessJournal _access = new();
        private readonly List<RepositoryOccurrenceCheckReceipt> _occurrenceCheckReceipts = new();
        /// Why each selection happened. The five-link producers are all gated behind
        /// SelectionCause == GrammarCandidate — a LEARNED selection — so a run with no links has two
        /// possible causes: the learned readout was never paid (the readout economy hands one credit
        /// per step, round-robin across the policy roster), or it was paid and produced no candidate.
        /// A link count of zero cannot tell those apart; the cause census can.
        private readonly Dictionary<CortexPolicySelectionCauses, int> _selectionCauses = new();
        private readonly HashSet<string> _occurrenceCheckReceiptIDs = new(StringComparer.Ordinal);
        private readonly List<RepositoryFundingReceipt> _fundingReceipts = new();
        private readonly List<RepositoryPaidOutcomeReceipt> _outcomeReceipts = new();
        private readonly List<RepositoryNewEvidenceReceipt> _evidenceReceipts = new();
        private Tape? _runtimeTape;
        private readonly RepositoryCandidateFrontier _frontier = new();
        private readonly RepositoryToolMediation _mediation;         // G3's valve: the ONE place the organism touches reality
        private readonly RepositoryLoopClosureTaskSpec? _task;
        private readonly RepositoryLoopClosureRegistration? _registration;
        private readonly long? _registeredOfferedFuel;
        private static readonly RepositoryNavigationRule NavigationRule = RepositoryNavigationRule.CreateSharedIdentifierSearchTerm();
        private readonly RepositoryPatternStore _pattern = new(NavigationRule);
        private RepositoryOccurrenceCheckReceipt? _lastOccurrenceCheck;
        private PendingOccurrenceCheck? _pendingOccurrenceCheck;
        private RepositoryCandidateProposal? _pendingProposal;
        private Tool.Observation _pendingObservation;
        private bool _hasPendingObservation;
        private int _lastCommittedStep = -1;
        private RepositoryCandidateSelectionReceipt _lastSelection;
        private RepositorySelectionReceipt _pendingSelectionReceipt;
        private TapeEventID _pendingSelectionEventID;
        private bool _hasPendingSelection;
        private bool _answered;
        private int _look;
        private CortexPolicyDecision _pendingPolicyDecision;
        private bool _hasPendingPolicyDecision;
        private TapeEventID _pendingObservationEventID;
        private bool _hasPendingAdmissionPlan;
        private bool _pendingAdmissionPlanBarren;         // the look happened and rendered nothing — no admissionPlan packet to be its source
        private string _pendingAdmissionPlanCallSHA = ""; // the call of the access entry this admissionPlan RESTS on, which is not always the call that was made
        private RepositoryReadoutReceipt? _lastReadout;
        private CortexPolicyTrialQuotaDecision? _pendingFundingDecision;
        private RepositoryFundingReceipt? _pendingFundingReceipt;
        private bool _terminalSealAppended;
        private int _mixEvery;

        internal RepositoryLoopClosureTaskSpec? Task => _task;
        internal RepositoryLoopClosureRegistration? Registration => _registration;

        private readonly record struct RepositoryNativeCheckpointDelta(
            int Look,
            bool Answered,
            string[] ObservedPathAdds,
            RepositoryAccessEntry[] AccessEntries,
            RepositoryOccurrenceCheckReceipt[] OccurrenceCheckReceipts,
            bool LastOccurrenceCheckChanged,
            bool HasLastOccurrenceCheck,
            RepositoryOccurrenceCheckReceipt LastOccurrenceCheck,
            bool LastReadoutChanged,
            bool HasLastReadout,
            RepositoryReadoutReceipt LastReadout,
            RepositoryFundingReceipt[] FundingReceipts,
            RepositoryPaidOutcomeReceipt[] OutcomeReceipts,
            RepositoryNewEvidenceReceipt[] EvidenceReceipts,
            bool HasPendingFunding,
            RepositoryFundingReceipt PendingFundingReceipt,
            bool HasPendingTuple,
            RepositoryFrontierRevision PendingProposalRevision,
            RepositoryCandidateDigest PendingProposalDigest,
            string PendingProposalCanonical,
            CortexPolicyDecision PendingPolicyDecision,
            PolicyCanonicalStateID PendingContextState,
            int PendingContextActionCount,
            int PendingContextDepth,
            bool HasPendingSelection,
            TapeEventID PendingSelectionEventID,
            RepositorySelectionReceipt PendingSelectionReceipt,
            int AccessCount,
            int FrontierCount,
            int PatternOccurrenceCount,
            int PatternCompositionCount,
            int PatternAdmissionCount,
            int PatternPendingCount,
            string AccessSHA256,
            string FrontierAuthoritySHA256,
            string PatternPendingAuthorityRoot,
            RepositoryFrontierCheckpointDelta Frontier,
            RepositoryPatternOccurrence[] PatternOccurrences,
            RepositoryPatternComposition[] PatternCompositions,
            RepositoryPatternGrammarAdmissionReceipt[] PatternAdmissions,
            RepositoryPatternStore.PendingMutation[] PatternPendingMutations) : ICurriculumCheckpointDelta
        {
            public string Kind => "repository-native";
            public void Write(CkptWriter writer)
            {
                writer.U8(10); writer.I32(Look); writer.Bool(Answered); writer.I32(ObservedPathAdds.Length); foreach (string path in ObservedPathAdds) writer.Str(path);
                writer.I32(AccessCount); writer.I32(FrontierCount); writer.I32(PatternOccurrenceCount); writer.I32(PatternCompositionCount); writer.I32(PatternAdmissionCount); writer.I32(PatternPendingCount);
                writer.Str(AccessSHA256); writer.Str(FrontierAuthoritySHA256); writer.Str(PatternPendingAuthorityRoot);
                RepositoryAccessJournal.WriteCheckpointDelta(writer, AccessEntries);
                writer.I32(OccurrenceCheckReceipts.Length); foreach (RepositoryOccurrenceCheckReceipt receipt in OccurrenceCheckReceipts) WriteOccurrenceCheckState(writer, receipt);
                writer.Bool(LastOccurrenceCheckChanged); if (LastOccurrenceCheckChanged) { writer.Bool(HasLastOccurrenceCheck); if (HasLastOccurrenceCheck) WriteOccurrenceCheckState(writer, LastOccurrenceCheck); }
                writer.Bool(LastReadoutChanged); if (LastReadoutChanged) { writer.Bool(HasLastReadout); if (HasLastReadout) WriteReadoutState(writer, LastReadout); }
                writer.I32(FundingReceipts.Length); foreach (RepositoryFundingReceipt receipt in FundingReceipts) RepositoryLineageReceiptCheckpoint.Write(writer, in receipt);
                writer.I32(OutcomeReceipts.Length); foreach (RepositoryPaidOutcomeReceipt receipt in OutcomeReceipts) RepositoryLineageReceiptCheckpoint.Write(writer, in receipt);
                writer.I32(EvidenceReceipts.Length); foreach (RepositoryNewEvidenceReceipt receipt in EvidenceReceipts) RepositoryLineageReceiptCheckpoint.Write(writer, in receipt);
                writer.Bool(HasPendingFunding); if (HasPendingFunding) RepositoryLineageReceiptCheckpoint.Write(writer, PendingFundingReceipt);
                writer.Bool(HasPendingTuple);
                if (HasPendingTuple)
                {
                    writer.U64(PendingProposalRevision.Value); writer.U64(PendingProposalDigest.Value); writer.Str(PendingProposalCanonical);
                    CortexPolicyDecisionCheckpoint.Write(writer, PendingPolicyDecision);
                    RepositoryLineageReceiptCheckpoint.WriteState(writer, PendingContextState);
                    writer.I32(PendingContextActionCount); writer.I32(PendingContextDepth);
                }
                writer.Bool(HasPendingSelection);
                if (HasPendingSelection)
                {
                    writer.I64(PendingSelectionEventID.Value);
                    WritePendingSelection(writer, PendingSelectionReceipt);
                }
                RepositoryCandidateFrontier.WriteCheckpointDelta(writer, Frontier);
                RepositoryPatternStore.WriteCheckpointDelta(writer, (PatternOccurrences, PatternCompositions, PatternAdmissions, PatternPendingMutations));
            }
        }

        private int _checkpointObservedPathCursor;
        private int _checkpointAccessCursor;
        private int _checkpointOccurrenceCheckCursor;
        private int _checkpointFundingCursor;
        private int _checkpointOutcomeCursor;
        private int _checkpointEvidenceCursor;
        private RepositoryOccurrenceCheckReceipt? _checkpointLastOccurrenceCheck;
        private RepositoryReadoutReceipt? _checkpointLastReadout;
        private RepositoryFundingReceipt? _checkpointPendingFunding;

        ICurriculumCheckpointDelta? ICurriculumCheckpointDeltaOwner.CaptureCheckpointDelta()
        {
            ValidatePendingFundingTuple();
            if (_hasPendingSelection
                && (_pendingFundingReceipt is null || _pendingProposal is null || !_hasPendingPolicyDecision))
                throw new InvalidDataException("native repository pending selection lacks its funding authority tuple");
            (RepositoryPatternOccurrence[] occurrences, RepositoryPatternComposition[] compositions,
                RepositoryPatternGrammarAdmissionReceipt[] admissions, RepositoryPatternStore.PendingMutation[] pendingMutations) = _pattern.CaptureCheckpointDelta();
            RepositoryNativeCheckpointDelta delta = new(
                _look, _answered, _observedPathLog.GetRange(_checkpointObservedPathCursor, _observedPathLog.Count - _checkpointObservedPathCursor).ToArray(), _access.CaptureCheckpointDelta(),
                _occurrenceCheckReceipts.GetRange(_checkpointOccurrenceCheckCursor, _occurrenceCheckReceipts.Count - _checkpointOccurrenceCheckCursor).ToArray(), _lastOccurrenceCheck != _checkpointLastOccurrenceCheck,
                _lastOccurrenceCheck is not null, _lastOccurrenceCheck.GetValueOrDefault(), _lastReadout != _checkpointLastReadout,
                _lastReadout is not null, _lastReadout.GetValueOrDefault(),
                _fundingReceipts.GetRange(_checkpointFundingCursor, _fundingReceipts.Count - _checkpointFundingCursor).ToArray(),
                _outcomeReceipts.GetRange(_checkpointOutcomeCursor, _outcomeReceipts.Count - _checkpointOutcomeCursor).ToArray(),
                _evidenceReceipts.GetRange(_checkpointEvidenceCursor, _evidenceReceipts.Count - _checkpointEvidenceCursor).ToArray(),
                _pendingFundingReceipt is not null, _pendingFundingReceipt.GetValueOrDefault(),
                _pendingFundingReceipt is not null, _pendingProposal?.Revision ?? RepositoryFrontierRevision.Zero,
                _pendingProposal?.CandidateDigest ?? RepositoryCandidateDigest.Zero,
                _pendingProposal?.Candidate.Canonical ?? "", _pendingPolicyDecision,
                // Only a CANONICAL context owns a canonical state; a raw one throws when asked for
                // it, and with no pending decision at all the context is the default raw key. The
                // checkpoint records the state that exists, not one demanded of a decision that was
                // never taken.
                _pendingPolicyDecision.ReadoutContext.IsCanonical ? _pendingPolicyDecision.ReadoutContext.CanonicalState : default,
                _pendingPolicyDecision.ReadoutContext.ActionCount,
                _pendingPolicyDecision.ReadoutContext.DeliberationDepth,
                _hasPendingSelection, _pendingSelectionEventID, _pendingSelectionReceipt,
                _access.Count, _frontier.Count, _pattern.OccurrenceCount, _pattern.CompositionCount, _pattern.AdmissionCount, _pattern.PendingAdmissionCount,
                _access.AccessSHA256, _frontier.AuthoritySHA256, _pattern.PendingAuthorityRoot,
                _frontier.CaptureCheckpointDelta(), occurrences, compositions, admissions, pendingMutations);
            return delta;
        }

        void ICurriculumCheckpointDeltaOwner.ApplyCheckpointDelta(ICurriculumCheckpointDelta delta, in CheckpointReplayContext replayContext)
        {
            if (delta is not OpaqueCurriculumCheckpointDelta opaque || !string.Equals(opaque.Kind, "repository-native", StringComparison.Ordinal))
                throw new InvalidDataException($"curriculum checkpoint delta {delta.Kind} does not belong to RepositoryNativeRuntime");
            using MemoryStream stream = new(opaque.Payload, writable: false);
            using CkptReader reader = new(stream);
            byte version = reader.U8();
            if (version != 10) throw new InvalidDataException("unknown repository-native checkpoint delta version");
            int nextLook = reader.I32(); bool nextAnswered = reader.Bool();
            if (nextLook < 0 || nextLook < _look || _answered && !nextAnswered)
                throw new InvalidDataException("repository-native checkpoint scalar state regresses");
            int observed = reader.I32(); if (observed < 0 || observed > 1_000_000) throw new InvalidDataException("repository-native observed-path delta is malformed");
            string[] observedPaths = new string[observed]; HashSet<string> observedSet = new(StringComparer.Ordinal);
            for (int i = 0; i < observed; i++) if (string.IsNullOrWhiteSpace(observedPaths[i] = reader.Str()) || !observedSet.Add(observedPaths[i]) || _observedPaths.Contains(observedPaths[i])) throw new InvalidDataException("repository-native observed-path delta is duplicated or blank");
            int accessCount = reader.I32(); int frontierCount = reader.I32(); int patternOccurrenceCount = reader.I32(); int patternCompositionCount = reader.I32(); int patternAdmissionCount = reader.I32(); int patternPendingCount = reader.I32();
            if (accessCount < 0 || frontierCount < 0 || patternOccurrenceCount < 0 || patternCompositionCount < 0 || patternAdmissionCount < 0 || patternPendingCount < 0)
                throw new InvalidDataException("repository-native checkpoint child endpoint is malformed");
            string accessAuthority = reader.Str(); string frontierAuthority = reader.Str(); string patternPendingAuthority = reader.Str();
            RepositoryLineageReceiptCodec.RequireSHA(accessAuthority, "access authority");
            RepositoryLineageReceiptCodec.RequireSHA(frontierAuthority, "frontier authority");
            RepositoryLineageReceiptCodec.RequireSHA(patternPendingAuthority, "pattern pending authority");
            // The delta continues the journal already restored, so it is read against that count.
            RepositoryAccessEntry[] accessDelta = RepositoryAccessJournal.ReadCheckpointDelta(reader, accessCount);
            int occurrenceCheckCount = reader.I32(); if (occurrenceCheckCount < 0 || occurrenceCheckCount > 1_000_000) throw new InvalidDataException("repository-native occurrence-check delta is malformed");
            RepositoryOccurrenceCheckReceipt[] occurrenceCheckDelta = new RepositoryOccurrenceCheckReceipt[occurrenceCheckCount];
            HashSet<string> occurrenceCheckIDs = new(StringComparer.Ordinal);
            for (int i = 0; i < occurrenceCheckCount; i++)
            {
                occurrenceCheckDelta[i] = ReadOccurrenceCheckState(reader);
                if (!occurrenceCheckIDs.Add(occurrenceCheckDelta[i].ReceiptSHA256) || _occurrenceCheckReceiptIDs.Contains(occurrenceCheckDelta[i].ReceiptSHA256))
                    throw new InvalidDataException("repository-native occurrence-check delta is duplicated");
            }
            bool lastOccurrenceCheckChanged = reader.Bool();
            RepositoryOccurrenceCheckReceipt? nextLastOccurrenceCheck = lastOccurrenceCheckChanged && reader.Bool() ? ReadOccurrenceCheckState(reader) : lastOccurrenceCheckChanged ? null : _lastOccurrenceCheck;
            bool lastReadoutChanged = reader.Bool();
            RepositoryReadoutReceipt? nextLastReadout = lastReadoutChanged && reader.Bool() ? ReadReadoutState(reader) : lastReadoutChanged ? null : _lastReadout;
            RepositoryFundingReceipt[] fundingDelta = [];
            RepositoryPaidOutcomeReceipt[] outcomeDelta = [];
            RepositoryNewEvidenceReceipt[] evidenceDelta = [];
            RepositoryFundingReceipt? nextPendingFunding = _pendingFundingReceipt;
            bool hasPendingTuple = false;
            RepositoryFrontierRevision pendingProposalRevision = RepositoryFrontierRevision.Zero;
            RepositoryCandidateDigest pendingProposalDigest = RepositoryCandidateDigest.Zero;
            string pendingProposalCanonical = "";
            CortexPolicyDecision pendingPolicyDecision = default;
            PolicyCanonicalStateID pendingContextState = default;
            int pendingContextActionCount = 0;
            int pendingContextDepth = 0;
            bool hasPendingSelection = false;
            TapeEventID pendingSelectionEventID = default;
            RepositorySelectionReceipt pendingSelectionReceipt = default;
            if (version >= 9)
            {
                int fundingCount = reader.I32();
                if (fundingCount < 0 || fundingCount > 1_000_000) throw new InvalidDataException("repository-native funding delta is malformed");
                fundingDelta = new RepositoryFundingReceipt[fundingCount];
                HashSet<string> fundingIDs = new(StringComparer.Ordinal);
                for (int i = 0; i < fundingCount; i++)
                {
                    fundingDelta[i] = RepositoryLineageReceiptCheckpoint.ReadFunding(reader);
                    if (!fundingIDs.Add(fundingDelta[i].ReceiptSHA256) || _fundingReceipts.Any(existing => existing.ReceiptSHA256 == fundingDelta[i].ReceiptSHA256))
                        throw new InvalidDataException("repository-native funding delta is duplicated");
                }
                int outcomeCount = reader.I32();
                if (outcomeCount < 0 || outcomeCount > 1_000_000) throw new InvalidDataException("repository-native outcome delta is malformed");
                outcomeDelta = new RepositoryPaidOutcomeReceipt[outcomeCount];
                HashSet<string> outcomeIDs = new(StringComparer.Ordinal);
                for (int i = 0; i < outcomeCount; i++)
                {
                    outcomeDelta[i] = RepositoryLineageReceiptCheckpoint.ReadPaidOutcome(reader);
                    if (!outcomeIDs.Add(outcomeDelta[i].ReceiptSHA256) || _outcomeReceipts.Any(existing => existing.ReceiptSHA256 == outcomeDelta[i].ReceiptSHA256))
                        throw new InvalidDataException("repository-native outcome delta is duplicated");
                }
                int evidenceCount = reader.I32();
                if (evidenceCount < 0 || evidenceCount > 1_000_000) throw new InvalidDataException("repository-native evidence delta is malformed");
                evidenceDelta = new RepositoryNewEvidenceReceipt[evidenceCount];
                HashSet<string> evidenceIDs = new(StringComparer.Ordinal);
                for (int i = 0; i < evidenceCount; i++)
                {
                    evidenceDelta[i] = RepositoryLineageReceiptCheckpoint.ReadEvidence(reader);
                    if (!evidenceIDs.Add(evidenceDelta[i].ReceiptSHA256) || _evidenceReceipts.Any(existing => existing.ReceiptSHA256 == evidenceDelta[i].ReceiptSHA256))
                        throw new InvalidDataException("repository-native evidence delta is duplicated");
                }
                nextPendingFunding = reader.Bool() ? RepositoryLineageReceiptCheckpoint.ReadFunding(reader) : null;
                hasPendingTuple = reader.Bool();
                if (hasPendingTuple)
                {
                    pendingProposalRevision = new RepositoryFrontierRevision(reader.U64());
                    pendingProposalDigest = new RepositoryCandidateDigest(reader.U64());
                    pendingProposalCanonical = reader.Str();
                    pendingPolicyDecision = CortexPolicyDecisionCheckpoint.Read(reader, Policy.ID, Policy.Schema.ActionCount);
                    pendingContextState = RepositoryLineageReceiptCheckpoint.ReadState(reader);
                    pendingContextActionCount = reader.I32(); pendingContextDepth = reader.I32();
                    GrammarPolicyContextKey pendingContext = new(in pendingContextState, pendingContextActionCount, pendingContextDepth);
                    pendingPolicyDecision = new CortexPolicyDecision(pendingPolicyDecision.DecisionID, Policy.ID,
                        pendingPolicyDecision.Readout, in pendingContext);
                }
                hasPendingSelection = reader.Bool();
                if (hasPendingSelection)
                {
                    pendingSelectionEventID = new TapeEventID(reader.I64());
                    pendingSelectionReceipt = ReadPendingSelection(reader);
                }
            }
            RepositoryFrontierCheckpointDelta frontierDelta = RepositoryCandidateFrontier.ReadCheckpointDelta(reader);
            var pattern = RepositoryPatternStore.ReadCheckpointDelta(reader);
            if (reader.RemainingBytes != 0) throw new InvalidDataException("repository-native checkpoint delta has trailing bytes");

            // Prepare every rail without writes. The second pass is a commit
            // over already-validated typed deltas, so a rejected child cannot
            // leave the native runtime half-applied.
            RepositoryAccessJournal.PreparedCheckpointDelta preparedAccess = _access.PrepareCheckpointDelta(accessDelta);
            foreach (RepositoryOccurrenceCheckReceipt receipt in occurrenceCheckDelta)
                ValidateOccurrenceCheckAccess(receipt, accessDelta);
            RepositoryCandidateFrontier.PreparedCheckpointDelta preparedFrontier = _frontier.PrepareCheckpointDelta(in frontierDelta);
            RepositoryPatternStore.PreparedCheckpointDelta preparedPattern = _pattern.PrepareCheckpointDelta(
                pattern.Occurrences, pattern.Compositions, pattern.Admissions, pattern.PendingMutations);
            string stagedAccessAuthority = _access.ComputeAccessSHA256AfterDelta(_access.Count + accessDelta.Length, accessDelta);
            (string stagedFrontierAuthority, int stagedFrontierCount) = _frontier.ComputeAuthorityAfterDelta(in frontierDelta);
            string stagedPatternPendingAuthority = _pattern.ComputePendingAuthorityAfterDelta(
                pattern.Occurrences, pattern.Compositions, pattern.Admissions, pattern.PendingMutations);
            if (!string.Equals(stagedAccessAuthority, accessAuthority, StringComparison.Ordinal)
                || !string.Equals(stagedFrontierAuthority, frontierAuthority, StringComparison.Ordinal)
                || !string.Equals(stagedPatternPendingAuthority, patternPendingAuthority, StringComparison.Ordinal)
                || accessCount != _access.Count + accessDelta.Length
                || frontierCount != stagedFrontierCount
                || patternOccurrenceCount != _pattern.OccurrenceCount + pattern.Occurrences.Length
                || patternCompositionCount != _pattern.CompositionCount + pattern.Compositions.Length
                || patternAdmissionCount != _pattern.AdmissionCount + pattern.Admissions.Length
                || patternPendingCount != _pattern.PendingAdmissionCount + pattern.PendingMutations.Count(mutation => mutation.Added) - pattern.PendingMutations.Count(mutation => !mutation.Added))
                throw new InvalidDataException("repository-native staged authority endpoint diverged");
            if (nextLastOccurrenceCheck is { } stagedOccurrenceCheck)
                ValidateOccurrenceCheckAccess(stagedOccurrenceCheck, accessDelta);
            if (nextLastReadout is { } stagedReadout)
            {
                stagedReadout.Validate();
                if (!string.Equals(stagedReadout.FrontierAuthoritySHA256, stagedFrontierAuthority, StringComparison.Ordinal)
                    || stagedReadout.FrontierRevision != frontierDelta.Revision
                    || !stagedReadout.SourceEpisodeID.IsValid
                    || stagedReadout.TeacherPacketEventID.Value < 0
                    || stagedReadout.TeacherCompositionEventID.Value < 0
                    || stagedReadout.CompositionEventID.Value < 0)
                    throw new InvalidDataException("repository-native staged readout R4 authority diverged");
                if (!preparedFrontier.ContainsCandidate(stagedReadout.CandidateDigest, stagedReadout.CandidateCanonical))
                    throw new InvalidDataException("repository-native staged readout candidate is absent from frontier");
            }
            foreach (RepositoryFundingReceipt receipt in fundingDelta)
            {
                ValidateFundingReceipt(receipt, stagedFrontierAuthority, frontierDelta.Revision, accessDelta);
                ValidateCanonicalStateCorroboration(in receipt);
            }
            if (nextPendingFunding is { } pendingFunding)
            {
                ValidateFundingReceipt(pendingFunding, stagedFrontierAuthority, frontierDelta.Revision, accessDelta);
                ValidateCanonicalStateCorroboration(in pendingFunding);
            }
            RepositoryFundingReceipt[] stagedFunding = [.. _fundingReceipts, .. fundingDelta];
            foreach (RepositoryPaidOutcomeReceipt outcome in outcomeDelta)
            {
                outcome.Validate();
                ValidateCanonicalStateCorroboration(in outcome);
                RepositoryFundingReceipt funding = stagedFunding.SingleOrDefault(receipt => receipt.QuotaDecisionID == outcome.QuotaDecisionID);
                ValidatePaidOutcomeAuthority(funding, outcome);
                if (outcome.WorldSHA256 != World.WorldSHA256 || outcome.AccessSHA256 != stagedAccessAuthority
                    || funding.QuotaDecisionID != outcome.QuotaDecisionID
                    || funding.DecisionID != outcome.DecisionID
                    || funding.CandidateDigest != outcome.CandidateDigest
                    || funding.CandidateCanonical != outcome.CandidateCanonical
                    || funding.ReadoutFingerprint != outcome.ReadoutFingerprint
                    || funding.CandidateFingerprint != outcome.CandidateFingerprint
                    || funding.CandidateOccurrenceDigest != outcome.CandidateOccurrenceDigest
                    || funding.ReadoutRevision != outcome.ReadoutRevision
                    || funding.CanonicalState != outcome.CanonicalState
                    || funding.FundingDecision.PlannedArmSteps != outcome.PlannedArmSteps)
                    throw new InvalidDataException("repository-native staged outcome funding join diverged");
                if (outcome.Authority.AccessEntryCount != accessCount
                    || outcome.Authority.AccessSequence >= accessCount
                    || outcome.Authority.AccessSequence >= 0
                        && GetStagedAccessEntry(outcome.Authority.AccessSequence, accessDelta).EntrySHA256 != outcome.Authority.AccessEntrySHA256
                    || outcome.SettlementEventID.Value <= outcome.PredecessorEventID.Value
                    || outcome.OutcomeEventID.Value <= outcome.SettlementEventID.Value)
                    throw new InvalidDataException("repository-native staged outcome access/order authority diverged");
            }
            foreach (RepositoryNewEvidenceReceipt evidence in evidenceDelta)
            {
                evidence.Validate();
                ValidateCanonicalStateCorroboration(in evidence);
                RepositoryPaidOutcomeReceipt outcome = outcomeDelta.Concat(_outcomeReceipts).SingleOrDefault(receipt => receipt.EventID == evidence.OutcomeEventID);
                ValidatePaidEvidenceAuthority(outcome, evidence);
                if (evidence.WorldSHA256 != World.WorldSHA256 || evidence.AccessSHA256 != stagedAccessAuthority
                    || outcome.EventID != evidence.OutcomeEventID
                    || outcome.CandidateDigest != evidence.CandidateDigest
                    || outcome.CandidateCanonical != evidence.CandidateCanonical
                    || outcome.DecisionID != evidence.DecisionID
                    || outcome.QuotaDecisionID != evidence.QuotaDecisionID
                    || outcome.OutcomePayloadSource != RepositoryOutcomePayloadSources.WorldObservation
                    || evidence.WorldSHA256 != outcome.WorldSHA256
                    || evidence.AccessSHA256 != outcome.AccessSHA256
                    || evidence.CallSHA256 != outcome.CallSHA256
                    || evidence.EvidenceSHA256 != outcome.OutcomePayloadSHA256
                    || evidence.PredecessorEventID != outcome.EventID
                    || evidence.PredecessorDigest.Value != outcome.ReceiptSHA256
                    || evidence.Authority.AccessEntryCount != accessCount
                    || evidence.Authority.AccessSequence < 0
                    || evidence.Authority.AccessSequence >= accessCount
                    || GetStagedAccessEntry(evidence.Authority.AccessSequence, accessDelta).EntrySHA256 != evidence.Authority.AccessEntrySHA256
                    || evidence.OutcomeEventID.Value >= evidence.EventID.Value)
                    throw new InvalidDataException("repository-native staged evidence join diverged");
            }
            RepositoryPaidOutcomeReceipt[] stagedOutcomes = [.. _outcomeReceipts, .. outcomeDelta];
            RepositoryNewEvidenceReceipt[] stagedEvidence = [.. _evidenceReceipts, .. evidenceDelta];
            foreach (RepositoryPaidOutcomeReceipt outcome in stagedOutcomes)
            {
                int matchingEvidence = stagedEvidence.Count(evidence => evidence.OutcomeEventID == outcome.EventID);
                if (outcome.OutcomePayloadSource == RepositoryOutcomePayloadSources.WorldObservation
                    ? matchingEvidence != 1
                    : matchingEvidence != 0)
                    throw new InvalidDataException("repository-native staged outcome payload custody cardinality diverged");
            }
            RepositoryCandidate? restoredPendingCandidate = null;
            if (hasPendingTuple)
            {
                if (nextPendingFunding is not { } restoredPendingFunding
                    || !RepositoryCandidate.TryParseCanonical(pendingProposalCanonical, out RepositoryCandidate pendingCandidate)
                    || (restoredPendingCandidate = pendingCandidate).Digest != pendingProposalDigest
                    || pendingProposalRevision != frontierDelta.Revision
                    || pendingPolicyDecision.DecisionID != restoredPendingFunding.DecisionID
                    || pendingProposalDigest != restoredPendingFunding.CandidateDigest
                    || pendingProposalCanonical != restoredPendingFunding.CandidateCanonical
                    || pendingProposalRevision != restoredPendingFunding.FrontierRevision
                    || !pendingPolicyDecision.Policy.Equals(Policy.ID)
                    || pendingPolicyDecision.SelectionCause != CortexPolicySelectionCauses.GrammarCandidate
                    || pendingPolicyDecision.Authority != CortexPolicyAuthorities.Grammar
                    || pendingPolicyDecision.Readout.RawCandidateAction < 0
                    || pendingPolicyDecision.Readout.GrammarRevision != restoredPendingFunding.ReadoutRevision
                    || pendingPolicyDecision.Readout.CandidateFingerprint != restoredPendingFunding.CandidateFingerprint.Value
                    || pendingPolicyDecision.Readout.CandidateOccurrenceDigest != restoredPendingFunding.CandidateOccurrenceDigest
                    || pendingPolicyDecision.ReadoutIdentity.Value != restoredPendingFunding.ReadoutFingerprint.Value
                    || pendingPolicyDecision.ReadoutContext.CanonicalState != restoredPendingFunding.CanonicalState
                    || pendingPolicyDecision.Action != Policy.Action(pendingCandidate.Species)
                    || nextLastReadout is not { } pendingReadout
                    || !string.Equals(pendingReadout.PolicyID, pendingPolicyDecision.Policy.Value, StringComparison.Ordinal)
                    || pendingReadout.Authority != pendingPolicyDecision.Authority
                    || pendingReadout.SelectionCause != pendingPolicyDecision.SelectionCause
                    || pendingReadout.LaunchpadAction != pendingPolicyDecision.Readout.LaunchpadAction
                    || pendingReadout.RawCandidateAction != pendingPolicyDecision.Readout.RawCandidateAction
                    || pendingReadout.SelectedCandidateAction != pendingPolicyDecision.Readout.SelectedCandidateAction
                    || pendingReadout.ExecutedAction != pendingPolicyDecision.Readout.ExecutedAction
                    || pendingReadout.DecisionID != pendingPolicyDecision.DecisionID
                    || pendingReadout.CandidateDigest != pendingProposalDigest
                    || pendingReadout.CandidateCanonical != pendingProposalCanonical
                    || pendingReadout.CandidateFingerprint != pendingPolicyDecision.Readout.CandidateFingerprint
                    || pendingReadout.CandidateOccurrenceDigest != pendingPolicyDecision.Readout.CandidateOccurrenceDigest
                    || pendingReadout.ReadoutFingerprint != pendingPolicyDecision.ReadoutIdentity.Value
                    || pendingReadout.ReadoutRevision != pendingPolicyDecision.Readout.GrammarRevision
                    || pendingReadout.CanonicalState != pendingPolicyDecision.ReadoutContext.CanonicalState
                    || pendingReadout.ContextDigest != pendingPolicyDecision.ReadoutContext.ContextDigest
                    || pendingReadout.ContextActionCount != pendingPolicyDecision.ReadoutContext.ActionCount
                    || pendingReadout.ContextDeliberationDepth != pendingPolicyDecision.ReadoutContext.DeliberationDepth
                    || pendingContextState != restoredPendingFunding.CanonicalState
                    || pendingContextActionCount != Policy.Schema.ActionCount
                    || pendingContextDepth < 0)
                    throw new InvalidDataException("repository-native pending funding tuple is incomplete");
            }
            else if (nextPendingFunding is not null)
            {
                throw new InvalidDataException("repository-native pending funding tuple is incomplete");
            }
            if (hasPendingSelection)
            {
                pendingSelectionReceipt.Validate();
                if (pendingSelectionEventID.Value <= 0
                    || restoredPendingCandidate is null
                    || pendingSelectionReceipt.CandidateDigest != pendingProposalDigest
                    || pendingSelectionReceipt.CandidateCanonical != pendingProposalCanonical
                    || pendingSelectionReceipt.FrontierRevision != pendingProposalRevision)
                    throw new InvalidDataException("repository-native pending selection tuple is malformed");
            }
            if (fundingDelta.Length != 0 && accessCount < _access.Count)
                throw new InvalidDataException("repository-native funding delta access endpoint regresses");
            _frontier.CommitPreparedCheckpointDelta(in preparedFrontier);
            _pattern.CommitPreparedCheckpointDelta(in preparedPattern);
            _access.CommitPreparedCheckpointDelta(in preparedAccess);
            foreach (string path in observedPaths) { _observedPaths.Add(path); _observedPathLog.Add(path); }
            _occurrenceCheckReceipts.AddRange(occurrenceCheckDelta);
            foreach (RepositoryOccurrenceCheckReceipt receipt in occurrenceCheckDelta) _occurrenceCheckReceiptIDs.Add(receipt.ReceiptSHA256);
            _fundingReceipts.AddRange(fundingDelta);
            _outcomeReceipts.AddRange(outcomeDelta);
            _evidenceReceipts.AddRange(evidenceDelta);
            _look = nextLook; _answered = nextAnswered;
            if (lastOccurrenceCheckChanged) _lastOccurrenceCheck = nextLastOccurrenceCheck;
            if (lastReadoutChanged) _lastReadout = nextLastReadout;
            _pendingFundingReceipt = nextPendingFunding;
            _pendingFundingDecision = nextPendingFunding?.FundingDecision;
            if (hasPendingTuple)
            {
                _pendingProposal = new RepositoryCandidateProposal(pendingProposalRevision, pendingProposalDigest,
                    restoredPendingCandidate!);
                _pendingPolicyDecision = pendingPolicyDecision;
                _hasPendingPolicyDecision = true;
            }
            else
            {
                _pendingProposal = null;
                _pendingPolicyDecision = default;
                _hasPendingPolicyDecision = false;
            }
            if (hasPendingSelection)
            {
                _pendingSelectionEventID = pendingSelectionEventID;
                _pendingSelectionReceipt = pendingSelectionReceipt;
                _hasPendingSelection = true;
            }
            else
            {
                _pendingSelectionEventID = default;
                _pendingSelectionReceipt = default;
                _hasPendingSelection = false;
            }
        }

        void ICurriculumCheckpointDeltaOwner.CommitCheckpointDelta(ICurriculumCheckpointDelta captured)
        {
            if (captured is not RepositoryNativeCheckpointDelta delta || !string.Equals(captured.Kind, "repository-native", StringComparison.Ordinal))
                throw new InvalidDataException($"curriculum checkpoint delta kind {captured.Kind} does not belong to RepositoryNativeRuntime");
            if (_look != delta.Look || _answered != delta.Answered
                || _observedPathLog.Count != _checkpointObservedPathCursor + delta.ObservedPathAdds.Length
                || _observedPathLog.Skip(_checkpointObservedPathCursor).Take(delta.ObservedPathAdds.Length).SequenceEqual(delta.ObservedPathAdds) == false
                || _access.Count != delta.AccessCount
                || _occurrenceCheckReceipts.Count != _checkpointOccurrenceCheckCursor + delta.OccurrenceCheckReceipts.Length
                || _fundingReceipts.Count != _checkpointFundingCursor + delta.FundingReceipts.Length
                || _outcomeReceipts.Count != _checkpointOutcomeCursor + delta.OutcomeReceipts.Length
                || _evidenceReceipts.Count != _checkpointEvidenceCursor + delta.EvidenceReceipts.Length
                || _frontier.Revision != delta.Frontier.Revision
                || _frontier.Count != delta.FrontierCount
                || _pattern.OccurrenceCount != delta.PatternOccurrenceCount
                || _pattern.CompositionCount != delta.PatternCompositionCount
                || _pattern.AdmissionCount != delta.PatternAdmissionCount
                || _pattern.PendingAdmissionCount != delta.PatternPendingCount
                || !string.Equals(_access.AccessSHA256, delta.AccessSHA256, StringComparison.Ordinal)
                || !string.Equals(_frontier.AuthoritySHA256, delta.FrontierAuthoritySHA256, StringComparison.Ordinal)
                || !string.Equals(_pattern.PendingAuthorityRoot, delta.PatternPendingAuthorityRoot, StringComparison.Ordinal))
                throw new InvalidDataException("repository-native checkpoint changed after delta capture");
            for (int index = 0; index < delta.AccessEntries.Length; index++)
                if (!AccessEntryEquals(_access.Entries[_checkpointAccessCursor + index], delta.AccessEntries[index]))
                    throw new InvalidDataException("repository-native access delta changed after capture");
            for (int index = 0; index < delta.OccurrenceCheckReceipts.Length; index++)
                if (!OccurrenceCheckEquals(_occurrenceCheckReceipts[_checkpointOccurrenceCheckCursor + index], delta.OccurrenceCheckReceipts[index]))
                    throw new InvalidDataException("repository-native occurrence-check delta changed after capture");
            for (int index = 0; index < delta.FundingReceipts.Length; index++)
                if (_fundingReceipts[_checkpointFundingCursor + index].ReceiptSHA256 != delta.FundingReceipts[index].ReceiptSHA256)
                    throw new InvalidDataException("repository-native funding delta changed after capture");
            for (int index = 0; index < delta.OutcomeReceipts.Length; index++)
                if (_outcomeReceipts[_checkpointOutcomeCursor + index].ReceiptSHA256 != delta.OutcomeReceipts[index].ReceiptSHA256)
                    throw new InvalidDataException("repository-native outcome delta changed after capture");
            for (int index = 0; index < delta.EvidenceReceipts.Length; index++)
                if (_evidenceReceipts[_checkpointEvidenceCursor + index].ReceiptSHA256 != delta.EvidenceReceipts[index].ReceiptSHA256)
                    throw new InvalidDataException("repository-native evidence delta changed after capture");
            if (delta.LastOccurrenceCheckChanged)
            {
                if (delta.HasLastOccurrenceCheck != _lastOccurrenceCheck.HasValue
                    || delta.HasLastOccurrenceCheck && (_lastOccurrenceCheck!.Value.Canonical != delta.LastOccurrenceCheck.Canonical
                        || _lastOccurrenceCheck.Value.ReceiptSHA256 != delta.LastOccurrenceCheck.ReceiptSHA256))
                    throw new InvalidDataException("repository-native last occurrence check changed after capture");
            }
            else if (CanonicalOccurrenceCheck(_lastOccurrenceCheck) != CanonicalOccurrenceCheck(_checkpointLastOccurrenceCheck))
                throw new InvalidDataException("repository-native last occurrence check changed after capture");
            if (delta.LastReadoutChanged)
            {
                if (delta.HasLastReadout != _lastReadout.HasValue
                    || delta.HasLastReadout && (_lastReadout!.Value.Canonical != delta.LastReadout.Canonical
                        || _lastReadout.Value.ReceiptSHA256 != delta.LastReadout.ReceiptSHA256))
                    throw new InvalidDataException("repository-native last readout changed after capture");
            }
            else if (CanonicalReadout(_lastReadout) != CanonicalReadout(_checkpointLastReadout))
                throw new InvalidDataException("repository-native last readout changed after capture");
            if ((delta.HasPendingFunding != _pendingFundingReceipt.HasValue)
                || delta.HasPendingFunding && delta.PendingFundingReceipt.ReceiptSHA256 != _pendingFundingReceipt!.Value.ReceiptSHA256)
                throw new InvalidDataException("repository-native pending funding changed after capture");
            if (delta.HasPendingTuple != (_pendingFundingReceipt is not null)
                || delta.HasPendingTuple && (_pendingProposal is not { } pendingProposal
                    || pendingProposal.Revision != delta.PendingProposalRevision
                    || pendingProposal.CandidateDigest != delta.PendingProposalDigest
                    || pendingProposal.Candidate.Canonical != delta.PendingProposalCanonical
                    || _pendingPolicyDecision.DecisionID != delta.PendingPolicyDecision.DecisionID
                    || _pendingPolicyDecision.ReadoutContext.CanonicalState != delta.PendingContextState
                    || _pendingPolicyDecision.ReadoutContext.ActionCount != delta.PendingContextActionCount
                    || _pendingPolicyDecision.ReadoutContext.DeliberationDepth != delta.PendingContextDepth))
                throw new InvalidDataException("repository-native pending funding tuple changed after capture");
            if (delta.HasPendingSelection != _hasPendingSelection
                || delta.HasPendingSelection && (delta.PendingSelectionEventID != _pendingSelectionEventID
                    || delta.PendingSelectionReceipt.ReceiptSHA256 != _pendingSelectionReceipt.ReceiptSHA256))
                throw new InvalidDataException("repository-native pending selection changed after capture");
            _checkpointObservedPathCursor = _observedPathLog.Count;
            _checkpointAccessCursor = _access.Count;
            _checkpointOccurrenceCheckCursor = _occurrenceCheckReceipts.Count;
            _checkpointFundingCursor = _fundingReceipts.Count;
            _checkpointOutcomeCursor = _outcomeReceipts.Count;
            _checkpointEvidenceCursor = _evidenceReceipts.Count;
            _checkpointLastOccurrenceCheck = _lastOccurrenceCheck;
            _checkpointLastReadout = _lastReadout;
            _checkpointPendingFunding = _pendingFundingReceipt;
            _access.CommitCheckpointDelta(); _frontier.CommitCheckpointDelta(); _pattern.CommitCheckpointDelta();
        }

        private static string CanonicalOccurrenceCheck(RepositoryOccurrenceCheckReceipt? receipt)
            => receipt is { } value ? value.Canonical : "";

        private static string CanonicalReadout(RepositoryReadoutReceipt? receipt)
            => receipt is { } value ? value.Canonical : "";

        private static bool OccurrenceCheckEquals(in RepositoryOccurrenceCheckReceipt left, in RepositoryOccurrenceCheckReceipt right)
            => left.Canonical == right.Canonical && left.ReceiptSHA256 == right.ReceiptSHA256;

        private static bool AccessEntryEquals(in RepositoryAccessEntry left, in RepositoryAccessEntry right)
            => left.Step == right.Step && left.Sequence == right.Sequence
                && left.CallSHA256 == right.CallSHA256 && left.Verb == right.Verb && left.Argument == right.Argument
                && left.Paths.SequenceEqual(right.Paths) && left.Loci.SequenceEqual(right.Loci)
                && left.RenderedBytes.AsSpan().SequenceEqual(right.RenderedBytes);
        private readonly record struct PendingPatternOccurrence(RepositoryOccurrenceCheckReceipt OccurrenceCheck,
            TapeEventID SourceEventID, TapeEventID OccurrenceCheckReceiptEventID,
            TapeEventID VerifiedPredictionReceiptEventID, TapeEventID OccurrenceReceiptEventID);
        private PendingPatternOccurrence? _pendingPatternOccurrence;

        public Tool.RepositoryWorldSnapshot World { get; }
        public string RootPath => World.RootPath;
        public string QuerySHA256 { get; }
        public string Query { get; }
        public int WScale { get; }
        public bool HasObservation => _look > 0;
        public IReadOnlyList<string> ObservedPaths => _observedPaths;
        public bool Answered => _answered;
        public RepositoryAccessJournal Access => _access;
        public RepositoryOccurrenceCheckReceipt? LastOccurrenceCheck => _lastOccurrenceCheck;
        public RepositoryCandidateFrontier Frontier => _frontier;
        public RepositoryCandidateSelectionReceipt LastSelection => _lastSelection;
        public RepositoryReadoutReceipt? LastReadout => _lastReadout;
        public Func<Tape, TapeEventID, bool>? LineageWorldRootPredicate => RepositoryLineageWorldRoot.IsRepositoryAdmissionPlan;
        public RepositoryPatternStore Pattern => _pattern;

        public void VerifyLoadedState(Tape tape, Journal journal)
        {
            journal.RecoverRepositoryLoopTaskTransaction(tape);
            _pattern.VerifyAdmissionBindings(tape, journal);
            if (_hasPendingSelection
                && (_pendingProposal is not { } || !MatchesPendingSelectionAuthority(tape, _pendingSelectionEventID, _pendingSelectionReceipt)))
                throw new InvalidDataException("native repository pending selection authority diverges from tape");
            if (_lastReadout is not { } expected) return;
            if (!string.Equals(expected.PolicyID, Policy.ID.Value, StringComparison.Ordinal)
                || !RepositoryCandidate.TryParseCanonical(expected.CandidateCanonical, out RepositoryCandidate candidate)
                || candidate.Digest != expected.CandidateDigest)
                throw new InvalidDataException("native repository readout candidate or policy authority diverged");
            RepositoryCandidateTransition[] frontierMatches = _frontier.Transitions
                .Where(transition => transition.CandidateDigest == expected.CandidateDigest
                    && string.Equals(transition.CandidateCanonical, expected.CandidateCanonical, StringComparison.Ordinal))
                .ToArray();
            if (frontierMatches.Length != 1)
                throw new InvalidDataException("native repository readout candidate is not uniquely present in the frontier");
            if (expected.FrontierRevision.Value > _frontier.Revision.Value)
                throw new InvalidDataException("native repository readout frontier revision is ahead of loaded frontier");
            if (expected.FrontierRevision == _frontier.Revision)
            {
                string currentAuthority = _frontier.AuthoritySHA256;
                PolicyCanonicalStateID expectedState = Policy.State(_observedPaths.Count, _frontier.Count, _answered);
                if (expected.CanonicalState != expectedState || !string.Equals(expected.FrontierAuthoritySHA256, currentAuthority, StringComparison.Ordinal))
                    throw new InvalidDataException("native repository readout canonical policy state diverged from frontier authority");
            }
            else if (!_frontier.TryGetHistoricalAuthority(expected.FrontierRevision, expected.CandidateDigest,
                expected.CandidateCanonical, out string historicalAuthority, out int historicalOrdinal,
                out int historicalObservedPaths, out int historicalFrontierCount)
                || !string.Equals(expected.FrontierAuthoritySHA256, historicalAuthority, StringComparison.Ordinal)
                || expected.SelectionOrdinal != historicalOrdinal
                || !MatchesHistoricalPolicyState(expected, historicalObservedPaths, historicalFrontierCount))
                throw new InvalidDataException("native repository readout frontier authority does not match its decision-time prefix");

            bool MatchesHistoricalPolicyState(RepositoryReadoutReceipt receipt, int observedPaths, int frontierCount)
                => receipt.CanonicalState == Policy.State(observedPaths, frontierCount, false);

            CortexPolicyDecisionPacket? genericPacket = null;
            int genericMatches = 0;
            string policySource = "policy:" + expected.PolicyID;
            foreach (TapeEventView view in tape.GetEventViews())
            {
                if (!string.Equals(view.Source, policySource, StringComparison.Ordinal)
                    || view.Provenance != Provenances.Execution
                    || view.Roles != TapeEventRoles.AuditOnly
                    || !tape.Resolve(view.Id, out byte[] payload)) continue;
                CortexPolicyDecisionPacket packet;
                try { packet = TapePacketCreator.DecodePolicyDecision(payload); }
                catch (InvalidDataException) { continue; }
                if (!packet.DecisionID.Equals(expected.DecisionID)) continue;
                genericMatches++;
                if (view.Id == expected.DecisionEventID
                    && packet.Readout.GrammarRevision == expected.ReadoutRevision
                    && packet.Readout.Authority == expected.Authority
                    && packet.Readout.SelectionCause == expected.SelectionCause
                    && packet.Readout.LaunchpadAction == expected.LaunchpadAction
                    && packet.Readout.RawCandidateAction == expected.RawCandidateAction
                    && packet.Readout.SelectedCandidateAction == expected.SelectedCandidateAction
                    && packet.Readout.ExecutedAction == expected.ExecutedAction
                    && packet.Readout.ReadoutCandidateFingerprint == expected.CandidateFingerprint
                    && packet.Readout.ReadoutCandidateOccurrenceDigest == expected.CandidateOccurrenceDigest
                    && (packet.Readout.ReadoutFingerprint == expected.ReadoutFingerprint
                        || (packet.Readout.ReadoutFingerprint == 0
                            && GrammarPolicyReadout.ComputeFingerprint(packet.Readout.GrammarRevision, Policy.ID) == expected.ReadoutFingerprint)))
                    genericPacket = packet;
            }
            if (genericMatches != 1 || genericPacket is null)
                throw new InvalidDataException("native repository readout generic policy packet is missing, duplicated, or mismatched");

            int teacherMatches = 0;
            int foldMatches = 0;
            LoopClosureTeacherPacketProvenance authoritativeTeacher = default;
            GrammarFoldProvenanceReceipt authoritativeFold = default;
            foreach (TapeEventView view in tape.GetEventViews())
            {
                if (!tape.Resolve(view.Id, out byte[] payload)) continue;
                if (view.Id == expected.TeacherPacketEventID)
                {
                    if (!string.Equals(view.Source, "policy-teacher:" + expected.PolicyID, StringComparison.Ordinal)
                        || view.Provenance != Provenances.Execution
                        || view.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly))
                        continue;
                    try
                    {
                        LoopClosureTeacherPacketProvenance teacher = LoopClosureTeacherPacketProvenance.DecodePacketFields(payload);
                        if (teacher.EpisodeID == expected.SourceEpisodeID
                            && teacher.FoldRevision == expected.FoldRevision
                            && teacher.EvidenceDigest.Value == expected.TeacherEvidenceSHA256
                            && teacher.CorroborationDigest.Value == expected.TeacherCorroborationSHA256
                            && teacher.ProvenanceDigest.Value == expected.TeacherProvenanceSHA256
                            && teacher.MatchedEventIDs.SequenceEqual(
                                LoopClosureCompositionEpisode.NormalizeEventIDs(
                                    [expected.TeacherCompositionEventID, .. expected.TeacherEvidenceEventIDs])))
                        {
                            teacherMatches++;
                            authoritativeTeacher = teacher;
                        }
                    }
                    catch (InvalidDataException) { }
                }
                if (string.Equals(view.Source, "grammar:fold", StringComparison.Ordinal)
                    && view.Provenance == Provenances.Execution
                    && view.Roles == TapeEventRoles.AuditOnly
                    && TapePacketCreator.TryDecodeGrammarFoldInstallRevision(payload, out GrammarFoldProvenanceReceipt fold)
                    && fold.PreviousRevision == expected.FoldPreviousRevision
                    && fold.Revision == expected.FoldRevision
                    && fold.ConsumedEventDigest.Value == expected.FoldConsumedEventSHA256
                    && fold.ReceiptDigest.Value == expected.FoldReceiptSHA256
                    && fold.ConsumedEventIDs.SequenceEqual(expected.FoldConsumedEventIDs))
                {
                    foldMatches++;
                    authoritativeFold = fold;
                }
            }
            if (teacherMatches != 1 || foldMatches != 1)
                throw new InvalidDataException("native repository readout R4 teacher/fold authority is missing, duplicated, or mismatched");
            LoopClosureCompositionEpisode episode = new(
                expected.SourceEpisodeID,
                expected.CompositionEventID,
                authoritativeTeacher.MatchedEventIDs.Where(id => id != expected.CompositionEventID).ToArray(),
                expected.CompositionRevision,
                authoritativeTeacher.EvidenceDigest,
                new LoopClosureDigest(expected.SourceEpisodeSHA256));
            GrammarPolicyContextKey context = new(expected.CanonicalState, expected.ContextActionCount,
                expected.ContextDeliberationDepth);
            if (context.ContextDigest != expected.ContextDigest || genericPacket.Value.ActionCount != expected.ContextActionCount)
                throw new InvalidDataException("native repository readout policy context authority diverged");
            ReadoutTrainingCorroboration training = ReadoutTrainingCorroboration.Create(
                new CortexPolicyID(expected.PolicyID), expected.TeacherPacketEventID,
                expected.TeacherCompositionEventID, expected.TeacherEvidenceEventIDs,
                authoritativeTeacher.EvidenceDigest, expected.SourceEpisodeID,
                new LoopClosureDigest(expected.SourceEpisodeSHA256), expected.FoldPreviousRevision,
                expected.FoldRevision, expected.FoldConsumedEventIDs,
                new LoopClosureDigest(expected.FoldConsumedEventSHA256),
                new LoopClosureDigest(expected.FoldReceiptSHA256), expected.CanonicalState,
                in context, expected.CandidateFingerprint, expected.CandidateOccurrenceDigest,
                expected.ReadoutRevision, expected.DecisionID, expected.DecisionEventID);
            LoopClosureR4Provenance rehydrated = LoopClosureR4Provenance.Create(
                in episode, in authoritativeFold, in authoritativeTeacher, in training);
            if (!LoopClosureEvidenceCustody.VerifyReadoutR4(tape, journal, in rehydrated, out string custodyFailure))
                throw new InvalidDataException($"native repository readout R4 evidence custody failed: {custodyFailure}");
            if (rehydrated.Episode.EpisodeDigest.Value != expected.SourceEpisodeSHA256
                || rehydrated.Fold.Revision != expected.FoldRevision
                || rehydrated.Training.CanonicalState != expected.CanonicalState
                || rehydrated.Training.DecisionEventID != expected.DecisionEventID)
                throw new InvalidDataException("native repository readout R4 rehydration diverged from custody receipt");

            IReadOnlyList<LoopLineageEdgeReceipt> lineage = LoopLineageVerifier.ReadTapeEdges(tape);
            LoopLineageEdgeReceipt[] compositions = lineage.Where(edge =>
                edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition
                && edge.Node.NodeID == new LoopLineageNodeID(expected.SourceEpisodeID.Value)).ToArray();
            if (compositions.Length != 1)
                throw new InvalidDataException("native repository readout R4 composition lineage is missing or duplicated");
            LoopLineageEdgeReceipt[] displaced = lineage.Where(edge =>
                edge.Node.Species == LoopLineageNodeSpecies.DisplacedEvaluation
                && edge.Node.EventID == expected.PredecessorEventID
                && edge.PredecessorIDs.Count == 1
                && edge.PredecessorIDs[0] == compositions[0].Node.NodeID).ToArray();
            LoopLineageEdgeReceipt[] learned = lineage.Where(edge =>
                edge.Node.Species == LoopLineageNodeSpecies.LearnedReadout
                && edge.Node.EventID == expected.DecisionEventID
                && edge.Node.GrammarRevision == expected.ReadoutRevision
                && edge.PredecessorIDs.Count == 1
                && displaced.Length == 1
                && edge.PredecessorIDs[0] == displaced[0].Node.NodeID).ToArray();
            if (displaced.Length != 1 || learned.Length != 1
                || learned[0].Node.CausalID != displaced[0].Node.CausalID)
                throw new InvalidDataException("native repository readout R4 learned-readout lineage is missing, duplicated, or mismatched");

            int matching = 0;
            bool foreign = false;
            foreach (TapeEventView view in tape.GetEventViews())
            {
                if (!string.Equals(view.Source, "repository:lineage", StringComparison.Ordinal)
                    || !view.HasRole(TapeEventRoles.AuditOnly)
                    || !tape.Resolve(view.Id, out byte[] payload)
                    || !TapePacketCreator.TryReadRepositoryLineageReceipt(payload,
                        out string kind, out string canonical, out string digest)
                    || !string.Equals(kind, "readout", StringComparison.Ordinal))
                    continue;
                if (!RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields)
                    || fields.Length < 6
                    || !string.Equals(fields[4], expected.DecisionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || !string.Equals(fields[5], expected.DecisionEventID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    continue;
                if (string.Equals(canonical, expected.Canonical, StringComparison.Ordinal)
                    && string.Equals(digest, expected.ReceiptSHA256, StringComparison.Ordinal)) matching++;
                else foreign = true;
            }
            if (matching != 1 || foreign)
                throw new InvalidDataException("native repository checkpoint learned-readout custody is missing, duplicated, or mismatched");
        }

        public void VerifyLoadedState(Cortex cortex, Tape tape, Journal journal)
        {
            VerifyLoadedState(tape, journal);
            foreach (RepositoryFundingReceipt fundingReceipt in _fundingReceipts)
            {
                ValidateCanonicalStateCorroboration(in fundingReceipt);
                ValidateVerifiedScope(cortex, fundingReceipt.CanonicalState, fundingReceipt.ReadoutFingerprint,
                    fundingReceipt.CandidateFingerprint, fundingReceipt.CandidateOccurrenceDigest, fundingReceipt.ReadoutRevision);
            }
            VerifyPaidOutcomeCustody(cortex, tape);
            if (_pendingFundingReceipt is not { } receipt) return;
            ValidatePendingFundingTuple();
            if (_pendingFundingDecision is not { } funding || _pendingProposal is not { } proposal
                || !_hasPendingPolicyDecision
                || !cortex.TryGetPolicyBoundaryDomain(Policy.ID, out IPolicyBoundaryDomain domain)
                || !cortex.TryGetPolicyBoundaryObligation(Policy.ID, out PolicyBoundaryObligation obligation)
                || !domain.PolicyID.Equals(Policy.ID) || !domain.PolicyBinding.PolicyID.Equals(Policy.ID))
                throw new InvalidDataException("native repository pending funding domain tuple is missing");
            ValidateFundingReceipt(receipt, _frontier.AuthoritySHA256, _frontier.Revision, []);
            ValidateCanonicalStateCorroboration(in receipt);
            ValidateVerifiedScope(cortex, receipt.CanonicalState, receipt.ReadoutFingerprint, receipt.CandidateFingerprint,
                receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision);
            if (receipt.FundingDecision != funding || receipt.CandidateDigest != proposal.CandidateDigest
                || receipt.CandidateCanonical != proposal.Candidate.Canonical
                || receipt.DecisionID != _pendingPolicyDecision.DecisionID
                || receipt.CandidateFingerprint.Value != _pendingPolicyDecision.Readout.CandidateFingerprint
                || receipt.ReadoutFingerprint.Value != _pendingPolicyDecision.ReadoutIdentity.Value
                || receipt.CandidateOccurrenceDigest != _pendingPolicyDecision.Readout.CandidateOccurrenceDigest
                || receipt.ReadoutRevision != _pendingPolicyDecision.Readout.GrammarRevision
                || receipt.CanonicalState != _pendingPolicyDecision.ReadoutContext.CanonicalState
                || receipt.Authority.CandidateSpecies != proposal.Candidate.Species)
                throw new InvalidDataException("native repository pending funding tuple diverged");
            if (!TryResolvePolicyCustodyPacket(cortex, receipt.DecisionEventID, Policy.ID, out byte[] decisionPayload))
                throw new InvalidDataException("native repository pending decision packet is missing");
            if (Convert.ToHexStringLower(SHA256.HashData(decisionPayload)) != receipt.DecisionPayloadSHA256)
                throw new InvalidDataException("native repository pending decision payload digest diverged");
            CortexPolicyDecisionPacket decisionPacket = TapePacketCreator.DecodePolicyDecision(decisionPayload);
            if (decisionPacket.DecisionID != _pendingPolicyDecision.DecisionID
                || decisionPacket.Readout != _pendingPolicyDecision.Readout)
                throw new InvalidDataException("native repository pending decision packet diverged");
            if (!TryFindFundingPacket(cortex, in funding, out TapeEventID fundingEventID, out byte[] fundingPayload)
                || fundingEventID != receipt.FundingEventID
                || Convert.ToHexStringLower(SHA256.HashData(fundingPayload)) != receipt.FundingPayloadSHA256)
                throw new InvalidDataException("native repository pending funding packet is missing or mismatched");
            if (!tape.Resolve(receipt.ReadoutEventID, out byte[] readoutPayload)
                || Convert.ToHexStringLower(SHA256.HashData(readoutPayload)) != receipt.ReadoutPayloadSHA256
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(readoutPayload, out string readoutKind, out string readoutCanonical, out string readoutDigest)
                || readoutKind != "readout" || _lastReadout is not { } lastReadout
                || readoutCanonical != lastReadout.Canonical || readoutDigest != lastReadout.ReceiptSHA256)
                throw new InvalidDataException("native repository pending readout packet is missing or mismatched");
            if (!tape.Resolve(receipt.EventID, out byte[] receiptPayload)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(receiptPayload, out string kind, out string canonical, out string digest)
                || kind != receipt.Kind || canonical != receipt.Canonical || digest != receipt.ReceiptSHA256
                || Convert.ToHexStringLower(SHA256.HashData(receiptPayload)) != receipt.EventPayloadSHA256)
                throw new InvalidDataException("native repository pending receipt packet is missing or mismatched");
        }

        private void VerifyPaidOutcomeCustody(Cortex cortex, Tape tape)
        {
            foreach (RepositoryPaidOutcomeReceipt outcome in _outcomeReceipts)
            {
                outcome.Validate();
                ValidateCanonicalStateCorroboration(in outcome);
                ValidateVerifiedScope(cortex, outcome.CanonicalState, outcome.ReadoutFingerprint, outcome.CandidateFingerprint,
                    outcome.CandidateOccurrenceDigest, outcome.ReadoutRevision);
                if (outcome.WorldSHA256 != World.WorldSHA256 || outcome.AccessSHA256 != _access.AccessSHA256
                    || !tape.Resolve(outcome.EventID, out byte[] payload)
                    || !tape.TryGetEventView(outcome.EventID, out TapeEventView view)
                    || view.Source != "repository-outcome" || view.Provenance != Provenances.Execution
                    || view.Roles != TapeEventRoles.AuditOnly
                    || !TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
                    || kind != outcome.Kind || canonical != outcome.Canonical || digest != outcome.ReceiptSHA256
                    || Convert.ToHexStringLower(SHA256.HashData(payload)) != outcome.EventPayloadSHA256)
                    throw new InvalidDataException("native repository paid outcome packet is missing or mismatched");
                if (!tape.TryGetEventView(outcome.PredecessorEventID, out TapeEventView actionView)
                    || actionView.Source != "repository-action" || actionView.Provenance != Provenances.Execution
                    || actionView.Roles != TapeEventRoles.AuditOnly || !tape.Resolve(outcome.PredecessorEventID, out byte[] actionPayload)
                    || Convert.ToHexStringLower(SHA256.HashData(actionPayload)) != outcome.PredecessorDigest.Value)
                    throw new InvalidDataException("native repository paid outcome action predecessor is missing");
                RepositoryFundingReceipt funding = _fundingReceipts.SingleOrDefault(receipt => receipt.QuotaDecisionID == outcome.QuotaDecisionID);
                ValidatePaidOutcomeAuthority(funding, outcome);
                VerifyPaidOutcomePackets(cortex, tape, outcome, funding);
                CortexPolicyTrialQuotaDecision fundingDecision = funding.FundingDecision;
                CortexPolicyTrialCompletion expectedSettlement = new(
                    funding.QuotaDecisionID, outcome.ActualExecutedArmSteps, outcome.ReclaimedOrUnused,
                    outcome.EvaluatorWorkUnits, outcome.VerifierOutcome, outcome.WallMilliseconds);
                if (funding.QuotaDecisionID != outcome.QuotaDecisionID
                    || funding.DecisionID != outcome.DecisionID
                    || funding.CandidateDigest != outcome.CandidateDigest
                    || funding.CandidateCanonical != outcome.CandidateCanonical
                    || funding.ReadoutFingerprint != outcome.ReadoutFingerprint
                    || funding.CandidateFingerprint != outcome.CandidateFingerprint
                    || funding.CandidateOccurrenceDigest != outcome.CandidateOccurrenceDigest
                    || funding.ReadoutRevision != outcome.ReadoutRevision
                    || funding.CanonicalState != outcome.CanonicalState
                    || !TryFindSettlementPacket(cortex, in fundingDecision, in expectedSettlement,
                        out TapeEventID settlementEventID, out byte[] settlementPayload, out _)
                    || settlementEventID != outcome.SettlementEventID
                    || Convert.ToHexStringLower(SHA256.HashData(settlementPayload)) != outcome.SettlementPayloadSHA256)
                    throw new InvalidDataException("native repository paid outcome settlement join diverged");
                RepositoryNewEvidenceReceipt evidence = _evidenceReceipts.SingleOrDefault(candidate => candidate.OutcomeEventID == outcome.EventID);
                if (outcome.OutcomePayloadSource == RepositoryOutcomePayloadSources.ActionExecution)
                {
                    if (evidence.EventID == outcome.EventID
                        || outcome.OutcomePayloadSHA256 != Convert.ToHexStringLower(SHA256.HashData(actionPayload)))
                        throw new InvalidDataException("native repository paid action outcome bytes are unbound");
                }
                else if (outcome.OutcomePayloadSource == RepositoryOutcomePayloadSources.WorldObservation)
                {
                    ValidatePaidEvidenceAuthority(outcome, evidence);
                    if (evidence.EventID != outcome.EventID
                        || !tape.Resolve(evidence.ObservationEventID, out byte[] resultPayload)
                        || !tape.TryGetEventView(evidence.ObservationEventID, out TapeEventView resultView)
                        || resultView.Source != "repository:world" || resultView.Provenance != Provenances.Real
                        || !CarriesWorldObservationRoles(resultView.Roles)
                        || outcome.OutcomePayloadSHA256 != evidence.EvidenceSHA256
                        || outcome.OutcomePayloadSHA256 != Convert.ToHexStringLower(SHA256.HashData(resultPayload)))
                    throw new InvalidDataException("native repository paid world outcome bytes are unbound");
                }
                else
                    throw new InvalidDataException("native repository paid outcome payload source is unknown");
            }
            foreach (RepositoryNewEvidenceReceipt evidence in _evidenceReceipts)
            {
                evidence.Validate();
                ValidateCanonicalStateCorroboration(in evidence);
                ValidateVerifiedScope(cortex, evidence.CanonicalState, evidence.ReadoutFingerprint, evidence.CandidateFingerprint,
                    evidence.CandidateOccurrenceDigest, evidence.ReadoutRevision);
                RepositoryPaidOutcomeReceipt outcome = _outcomeReceipts.SingleOrDefault(receipt => receipt.EventID == evidence.OutcomeEventID);
                if (evidence.WorldSHA256 != World.WorldSHA256 || evidence.AccessSHA256 != _access.AccessSHA256
                    || outcome.EventID != evidence.OutcomeEventID
                    || outcome.CandidateDigest != evidence.CandidateDigest
                    || outcome.CandidateCanonical != evidence.CandidateCanonical
                    || outcome.DecisionID != evidence.DecisionID
                    || outcome.QuotaDecisionID != evidence.QuotaDecisionID
                    || outcome.OutcomePayloadSource != RepositoryOutcomePayloadSources.WorldObservation
                    || evidence.PredecessorEventID != outcome.EventID
                    || evidence.PredecessorDigest.Value != outcome.ReceiptSHA256
                    || outcome.PredecessorEventID.Value >= outcome.SettlementEventID.Value
                    || outcome.SettlementEventID.Value >= outcome.OutcomeEventID.Value
                    || outcome.OutcomeEventID.Value >= evidence.EventID.Value
                    || outcome.EventPayloadSHA256.Length != 64
                    || !tape.Resolve(evidence.OutcomeEventID, out byte[] outcomePayload)
                    || !TapePacketCreator.TryReadRepositoryLineageReceipt(outcomePayload, out string outcomeKind, out string outcomeCanonical, out string outcomeDigest)
                    || outcomeKind != outcome.Kind || outcomeCanonical != outcome.Canonical || outcomeDigest != outcome.ReceiptSHA256
                    || !tape.Resolve(evidence.EventID, out byte[] payload)
                    || !tape.TryGetEventView(evidence.EventID, out TapeEventView view)
                    || view.Source != "repository-evidence" || view.Provenance != Provenances.Execution
                    || view.Roles != TapeEventRoles.AuditOnly
                    || !TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
                    || kind != evidence.Kind || canonical != evidence.Canonical || digest != evidence.ReceiptSHA256
                    || Convert.ToHexStringLower(SHA256.HashData(payload)) != evidence.EventPayloadSHA256)
                    throw new InvalidDataException("native repository paid evidence packet is missing or mismatched");
                if (!tape.Resolve(evidence.ObservationEventID, out byte[] worldPayload)
                    || !tape.TryGetEventView(evidence.ObservationEventID, out TapeEventView worldView)
                    || worldView.Source != "repository:world" || worldView.Provenance != Provenances.Real
                    || !CarriesWorldObservationRoles(worldView.Roles)
                    || Convert.ToHexStringLower(SHA256.HashData(worldPayload)) != evidence.EvidenceSHA256)
                    throw new InvalidDataException("native paid evidence world audit diverged");
                TapeEventID admissionPlanEventID = new(evidence.ObservationEventID.Value - 1);
                if (admissionPlanEventID.Value >= outcome.OutcomeEventID.Value || outcome.OutcomeEventID.Value >= evidence.EventID.Value)
                    throw new InvalidDataException("native paid evidence chronology diverged");
                if (!tape.Resolve(admissionPlanEventID, out byte[] admissionPlanPayload)
                    || !tape.TryGetEventView(admissionPlanEventID, out TapeEventView admissionPlanView)
                    // Frozen tape source token repository:encounter; identifier-side name is AdmissionPlan.
                    || admissionPlanView.Source != "repository:encounter"
                    || admissionPlanView.Provenance != Provenances.Execution
                    || admissionPlanView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                    || !TapePacketCreator.TryReadRepositoryWorldEncounter(admissionPlanPayload, out RepositoryAdmissionReceipt admissionPlan)
                    || admissionPlan.ObservationEventID != evidence.ObservationEventID
                    || admissionPlan.SourcePath != evidence.SourceLocus.Path.Value
                    || admissionPlan.SourceLine != evidence.SourceLocus.Line
                    || admissionPlan.WorldSHA256 != evidence.WorldSHA256
                    || admissionPlan.AccessSHA256 != evidence.AccessSHA256
                    || admissionPlan.CallSHA256 != evidence.CallSHA256
                    || admissionPlan.EvidenceSHA256 != evidence.EvidenceSHA256
                    || admissionPlan.AccessSequence != evidence.Authority.AccessSequence
                    || admissionPlan.AccessEntrySHA256 != evidence.Authority.AccessEntrySHA256)
                    throw new InvalidDataException("native paid evidence admission join diverged");
                if (evidence.Authority.AccessSequence < 0 || evidence.Authority.AccessSequence >= _access.Count
                    || _access.Entries[(int)evidence.Authority.AccessSequence].EntrySHA256 != evidence.Authority.AccessEntrySHA256)
                    throw new InvalidDataException("native paid evidence access audit diverged");
            }
        }

        private readonly record struct PendingOccurrenceCheck(RepositoryPrediction Prediction, RepositoryOccurrenceCheckResult Result, string CallSHA256);

        public RepositoryNativeRuntime(string root, string query, string? glob, int wScale = 1,
            int? configuredSteps = null, RepositoryLoopClosureRegistration? registration = null,
            RepositoryToolArms arm = RepositoryToolArms.ToolsLive)
        {
            _mediation = new RepositoryToolMediation(arm);
            Tape.RequireWScale(wScale);
            WScale = wScale;
            Query = query.Trim();
            _queryBytes = Encoding.UTF8.GetBytes(Query + "\n");
            QuerySHA256 = Convert.ToHexStringLower(SHA256.HashData(_queryBytes));
            string normalizedGlob = string.IsNullOrWhiteSpace(glob) ? CogitoCorpus.DefaultGlob : glob.Trim();
            World = new Tool.RepositoryWorldSnapshot(root, normalizedGlob);
            _registration = registration;
            if (_registration is not null)
            {
                _ = _registration.Encode();
                _task = _registration.Task;
                _task.Validate();
                if (configuredSteps is not null && configuredSteps.Value != _registration.Horizon)
                    throw new InvalidDataException($"native repository registered horizon {_registration.Horizon} does not match requested steps {configuredSteps.Value}");
                ValidateRegistrationBinding();
                _registeredOfferedFuel = _registration.OfferedFuel;
            }
            _frontier.SeedQuery(Query);
        }

        /// The arm's valve, and the fuel it accounts for. The report reads these so an arm that
        /// spent no fuel, or whose looks all came back empty, is visible as such rather than read as
        /// an organism that chose not to look.
        internal RepositoryToolMediation Mediation => _mediation;

        /// The crawler mounts two learners on very different clocks: the byte grammar, which plateaus
        /// in a few dozen steps, and the POLICY plane, which must accumulate a rule for the navigation
        /// context before it can ever propose one. The momentum wall reads the first and halts the
        /// whole organism — measured, a 120-step budget stopped at step 32 with the policy plane having
        /// produced zero learned selections, which is the guaranteed steady state rather than bad luck.
        ///
        /// So the wall is deferred until the policy plane has spoken ONCE. After that the byte
        /// grammar's plateau is again the honest stopping read, because the slow plane has demonstrably
        /// entered the life rather than sat out of it. The veto cannot run away: --steps is still the
        /// hard cap, so the worst case is a crawl that spends its stated horizon.
        bool ICurriculumMomentumHaltVeto.VetoesMomentumHalt
            => !_selectionCauses.ContainsKey(CortexPolicySelectionCauses.GrammarCandidate);

        internal Tool.Observation MediateToolResult(in Tool.Observation observed) => _mediation.Mediate(in observed);

        private void ValidateRegistrationBinding()
        {
            if (_registration is not { } registration)
                return;
            RepositoryLoopClosureWorldSnapshot world = new(World.CaptureFiles()
                .Select(static file => new RepositoryLoopClosureWorldFile(file.Path, file.Content)).ToArray());
            world.Validate();
            // Per-clause: content-vs-crawler and content-vs-manifest are different drifts —
            // one says the bytes moved, the other says the path/length projection did.
            if (registration.WorldContentSHA256 != world.ContentSHA256)
                throw new InvalidDataException($"native repository registration world content diverges from the captured intake: '{registration.WorldContentSHA256}' vs '{world.ContentSHA256}' ({world.Files.Count} files)");
            if (registration.WorldContentSHA256 != World.WorldSHA256)
                throw new InvalidDataException($"native repository registration world content diverges from the crawler world: '{registration.WorldContentSHA256}' vs '{World.WorldSHA256}' ({World.FileCount} files, glob '{World.Glob}')");
            if (registration.WorldSnapshotSHA256 != world.SnapshotSHA256)
                throw new InvalidDataException($"native repository registration world manifest diverges from the captured intake: '{registration.WorldSnapshotSHA256}' vs '{world.SnapshotSHA256}'");
            if (registration.SourceAuthoritySHA256 != ComputeSourceSHA256(
                    RootPath, World.Glob, Query, QuerySHA256, world.ContentSHA256))
                throw new InvalidDataException("native repository registration source authority diverges from the live intake");
            if (registration.ToolAuthoritySHA256 != new RepositoryLoopClosureToolAuthorityCorroboration().Digest
                || registration.PolicyAuthoritySHA256 != new RepositoryLoopClosurePolicyAuthorityCorroboration().Digest
                || registration.CandidateAuthoritySHA256 != RepositoryLoopClosureCandidateSchemaAuthorityCorroboration.CreateDefault().Digest
                || registration.InitialStateSHA256 != RepositoryLoopClosureInitialStateAuthorityCorroboration.Create(registration.Seed, registration.Horizon).Digest
                || registration.OfferedFuelSHA256 != RepositoryLoopClosureFuelAuthorityCorroboration.Create(registration.OfferedFuel).Digest)
                throw new InvalidDataException("native repository registration runtime authority formulas diverge");
            // These fields belong to the world-novelty triad, not this native
            // crawler. Silently carrying them would make the registration look
            // governing while the runtime ignored them.
            if (registration.OpportunityFloor != 0 || registration.DecisionThreshold != 0)
                throw new InvalidDataException("native repository registration carries unsupported opportunity or decision thresholds");
        }

        public void Seed(Tape tape, Journal journal)
        {
            EnsureTerminalTransitionOpen();
            BindRuntimeTape(tape, journal);
            TapeEventID id = tape.Append(_queryBytes, "user-query", Provenances.Real, TapeEventRoles.GrammarInput);
            journal.Ingest(0, id, "user-query", _queryBytes);
            if (_task is { } task && !tape.GetEventViews().Any(static view => view.Source == "repository-task-prompt"))
            {
                byte[] prompt = Encoding.UTF8.GetBytes($"task={task.TaskID}\tspecies={task.Species}\tprompt={task.Prompt}\n");
                TapeEventID taskID = tape.Append(prompt, "repository-task-prompt", Provenances.Real, TapeEventRoles.GrammarInput);
                journal.Ingest(0, taskID, "repository-task-prompt", prompt);
            }
        }

        public void BindRuntimeTape(Tape tape, Journal journal)
        {
            ArgumentNullException.ThrowIfNull(tape);
            ArgumentNullException.ThrowIfNull(journal);
            _runtimeTape = tape;
        }

        public void RegisterPolicies(Cortex cortex)
        {
            cortex.RegisterPolicy(Policy.Schema);
            cortex.RegisterPolicyBoundaryDomain(RepositoryPolicyBoundaryDomain.Instance);
            cortex.RegisterPolicyBoundaryObligation(CreateBoundaryObligation());
        }

        internal bool TryVerifyReadout(
            Cortex cortex,
            in CortexPolicyDecision decision,
            out TapeEventID decisionEventID,
            out LoopClosureR4Provenance provenance)
        {
            EnsureTerminalTransitionOpen();
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            if (_pendingProposal is not { } pending
                || !decision.Policy.Equals(Policy.ID)
                || decision.SelectionCause != CortexPolicySelectionCauses.GrammarCandidate
                || decision.Authority != CortexPolicyAuthorities.Grammar
                || !decision.ReadoutContext.IsCanonical
                || decision.ReadoutContext.ActionCount != Policy.Schema.ActionCount
                || !Policy.IsCanonicalState(decision.ReadoutContext.CanonicalState))
                return false;

            PolicyCanonicalStateID currentState = Policy.State(_observedPaths.Count, _frontier.Count, _answered);
            GrammarPolicyDecision genericCandidate = new(
                decision.Readout.RawCandidateAction, 0, 0, decision.Readout.GrammarRevision,
                default, decision.Readout.CandidateFingerprint)
            { OccurrenceDigest = decision.Readout.CandidateOccurrenceDigest };
            ulong expectedGenericCandidate = GrammarPolicyReadout.ComputeCandidateFingerprint(
                Policy.ID, in currentState, in genericCandidate);
            if (!Policy.IsCanonicalState(currentState)
                || decision.ReadoutContext.CanonicalState != currentState
                || decision.Readout.ReadoutFingerprint != 0 && decision.Readout.ReadoutFingerprint != decision.ReadoutIdentity.Value
                || decision.Readout.RawCandidateAction < 0
                || decision.Readout.CandidateFingerprint != expectedGenericCandidate
                || decision.Readout.CandidateOccurrenceDigest == 0
                || decision.Readout.GrammarRevision == GrammarRevisionID.Zero
                || !_frontier.IsCurrent(pending)
                || decision.Action != Policy.Action(pending.Candidate.Species))
                return false;
            if (pending.CandidateDigest != RepositoryCandidate.ComputeDigest(pending.Candidate.Canonical)
                || !RepositoryCandidate.TryParseCanonical(pending.Candidate.Canonical, out RepositoryCandidate parsedCandidate)
                || parsedCandidate.Digest != pending.CandidateDigest
                || parsedCandidate.Species != pending.Candidate.Species)
                return false;

            if (_lastReadout is { } readout
                && readout.DecisionID == decision.DecisionID
                && (decision.Readout.CandidateFingerprint != readout.CandidateFingerprint
                    || decision.Readout.CandidateOccurrenceDigest != readout.CandidateOccurrenceDigest
                    || decision.Readout.GrammarRevision != readout.ReadoutRevision
                    || decision.ReadoutIdentity.Value != readout.ReadoutFingerprint
                    || decision.DecisionID != readout.DecisionID
                    || decision.Readout.LaunchpadAction != readout.LaunchpadAction
                    || decision.Readout.RawCandidateAction != readout.RawCandidateAction
                    || decision.Readout.SelectedCandidateAction != readout.SelectedCandidateAction
                    || decision.Readout.ExecutedAction != readout.ExecutedAction
                    || pending.CandidateDigest != readout.CandidateDigest))
                return false;

            if (!cortex.TryCreatePolicyReadoutCustody(in decision, out decisionEventID, out provenance))
                return false;
            if (!provenance.Training.Policy.Equals(Policy.ID)
                || provenance.Training.CanonicalState != currentState)
                return false;
            if (!cortex.TryReadPolicyReadout(Policy.ID, out CortexPolicyReadoutReceipt boundaryReadout)
                || boundaryReadout.CanonicalState != currentState
                || boundaryReadout.Fingerprint != decision.ReadoutIdentity.Value
                || boundaryReadout.ReadoutCandidateFingerprint != decision.Readout.ReadoutCandidateFingerprint
                || boundaryReadout.ReadoutCandidateOccurrenceDigest != decision.Readout.ReadoutCandidateOccurrenceDigest
                || !cortex.TryEmitPolicyBoundarySourceCorroborationForDomain(
                    RepositoryPolicyBoundaryDomain.Instance, in boundaryReadout,
                    in decision, out _, out _))
                return false;
            ulong readoutFingerprint = decision.ReadoutIdentity.Value;
            ulong candidateFingerprint = decision.Readout.ReadoutCandidateFingerprint;
            ulong supportDigest = decision.Readout.ReadoutCandidateOccurrenceDigest;
            if (!cortex.IsVerifiedPolicyScope(Policy.ID, in currentState, readoutFingerprint,
                    candidateFingerprint, supportDigest, decision.Readout.GrammarRevision))
            {
                if (!cortex.TryGrantVerifiedPolicyScope(Policy.ID, in currentState, readoutFingerprint,
                        candidateFingerprint, supportDigest, decision.Readout.GrammarRevision))
                    return false;
                cortex.AppendPolicyOccurrenceCheckScope(Policy.ID, readoutFingerprint, candidateFingerprint,
                    supportDigest, decision.Readout.GrammarRevision, in currentState);
            }
            return cortex.TryGrantVerifiedPolicySuccession(Policy.ID, readoutFingerprint,
                candidateFingerprint, decision.Readout.GrammarRevision);
        }

        private bool TryAcquireFunding(
            Cortex cortex,
            in CortexPolicyDecision decision,
            in RepositoryCandidateProposal proposal,
            TapeEventID decisionEventID,
            TapeEventID readoutEventID,
            out RepositoryFundingReceipt receipt,
            out CortexForkSeed preparedSeed,
            int horizonSteps = 1,
            int armCount = 1,
            bool stagePending = true)
        {
            receipt = default;
            preparedSeed = default;
            CortexForkSeed? capturedSeed = null;
            if (stagePending) _pendingFundingReceipt = null;
            if (!cortex.TryGetPolicyBoundaryDomain(Policy.ID, out IPolicyBoundaryDomain domain)
                || !domain.PolicyID.Equals(Policy.ID)
                || !domain.PolicyBinding.PolicyID.Equals(Policy.ID)
                || !cortex.TryGetPolicyBoundaryObligation(Policy.ID, out PolicyBoundaryObligation obligation)
                || !obligation.Identity.Policy.Equals(Policy.ID)) return false;
            if (!TryResolvePolicyCustodyPacket(cortex, decisionEventID, Policy.ID, out byte[] decisionPayload)
                || !cortex.Tape.Resolve(readoutEventID, out byte[] readoutPayload)
                || !cortex.Tape.TryGetEventView(readoutEventID, out TapeEventView readoutView)
                || readoutView.Provenance != Provenances.Execution
                || !readoutView.HasRole(TapeEventRoles.AuditOnly)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(readoutPayload, out string readoutKind, out _, out _)
                || readoutKind != "readout") return false;
            CortexPolicyDecisionPacket decisionPacket = TapePacketCreator.DecodePolicyDecision(decisionPayload);
            if (decisionPacket.DecisionID != decision.DecisionID
                || decisionPacket.Readout != decision.Readout
                || decisionPacket.ActionCount != Policy.Schema.ActionCount) return false;

            CortexPolicyTrialAuthorityIdentity authority = new(
                decision.ReadoutIdentity,
                new CortexPolicyCandidateFingerprint(decision.Readout.CandidateFingerprint),
                decision.Readout.GrammarRevision)
            { CanonicalState = decision.ReadoutContext.CanonicalState };
            CortexPolicyDecision decisionCopy = decision;
            RepositoryCandidateProposal proposalCopy = proposal;
            if (_registeredOfferedFuel is { } offeredFuel
                && checked((long)horizonSteps * armCount) > offeredFuel)
                return false;
            CortexPolicyTrialQuotaDecision funding;
            try
            {
                funding = cortex.DecidePolicyTrialQuota(
                    Policy.ID, in authority, horizonSteps, armCount,
                    fundingID =>
                    {
                        // Seed custody is the admission transaction's prepare phase. The
                        // funding row and tape packet are not allowed to exist without a
                        // verified child seed already durable on the parent rail.
                        CortexForkSeed seed = cortex.MaterializeCompletedStepForkSeed();
                        capturedSeed = seed;
                        return cortex.PersistPolicyBoundarySeedForDomain(
                            fundingID, Policy.ID, decisionCopy.Readout.CandidateFingerprint,
                            cortex.Step, decisionCopy.Readout.GrammarRevision, seed,
                            decisionCopy.DecisionID.Value, decisionEventID.Value,
                            decisionCopy.Readout.CandidateOccurrenceDigest,
                            decisionCopy.Readout.CandidateFingerprint,
                            decisionCopy.ReadoutIdentity.Value,
                            CortexPolicyQuotaDecisions.Paid,
                            decisionCopy.ReadoutContext.CanonicalState,
                            PolicyBoundaryRational.Zero,
                            PolicyBoundaryComparisons.LessThanOrEqual,
                            "repository-native", cortex.Step, obligation.ID.Value,
                            proposalCopy.Candidate.Canonical,
                            proposalCopy.CandidateDigest.Value,
                            proposalCopy.Revision.Value,
                            _frontier.AuthoritySHA256);
                    });
            }
            catch (InvalidDataException)
            {
                return false;
            }
            if (_registeredOfferedFuel is { } registeredOfferedFuel)
            {
                if (funding.PlannedArmSteps > registeredOfferedFuel
                    || funding.Decision == CortexPolicyQuotaDecisions.Paid
                    && checked(_fundingReceipts
                        .Where(static receipt => receipt.QuotaDecisionID.Value != 0)
                        .GroupBy(static receipt => receipt.QuotaDecisionID)
                        .Sum(static group => group.First().PlannedArmSteps) + funding.PlannedArmSteps) > registeredOfferedFuel)
                {
                    SettleFailedFunding(cortex, in funding);
                    return false;
                }
            }
            if (stagePending) _pendingFundingDecision = funding;
            if (funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
                return false;
            if (!TryFindFundingPacket(cortex, in funding, out TapeEventID fundingEventID, out byte[] fundingPayload))
            {
                SettleFailedFunding(cortex, in funding);
                return false;
            }

            try
            {
                string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Tool.ToolCall.Create(proposal.Candidate.Verb, proposal.Candidate.Argument).Raw)));
            string readoutSHA = Convert.ToHexStringLower(SHA256.HashData(readoutPayload));
            string fundingSHA = Convert.ToHexStringLower(SHA256.HashData(fundingPayload));
            string decisionSHA = Convert.ToHexStringLower(SHA256.HashData(decisionPayload));
            TapeEventID receiptEventID = new(cortex.Tape.NextId);
            RepositoryReceiptAuthority receiptAuthority = new(
                receiptEventID,
                new LoopLineageNodeID($"repository-funding-{funding.QuotaDecisionID.Value:X16}"),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"repository-funding:{funding.QuotaDecisionID.Value:X16}:{proposal.CandidateDigest}"))),
                decisionSHA, readoutEventID, readoutSHA,
                fundingEventID, fundingSHA, default, "", default, "",
                _frontier.AuthoritySHA256, proposal.Revision,
                _frontier.GetSelectionOrdinal(in proposal, proposal.Candidate.Species), proposal.Candidate.Species)
            {
                AccessSequence = -1,
                AccessEntrySHA256 = "",
                AccessEntryCount = _access.Count,
            };
            ValidateCanonicalStateCorroboration(decision.ReadoutContext.CanonicalState, decision.ReadoutIdentity,
                new CortexPolicyCandidateFingerprint(decision.Readout.CandidateFingerprint),
                decision.Readout.CandidateOccurrenceDigest, decision.Readout.GrammarRevision,
                proposal.Revision, _frontier.AuthoritySHA256, _lastReadout);
            ValidateVerifiedScope(cortex, decision.ReadoutContext.CanonicalState, decision.ReadoutIdentity,
                new CortexPolicyCandidateFingerprint(decision.Readout.CandidateFingerprint),
                decision.Readout.CandidateOccurrenceDigest, decision.Readout.GrammarRevision);
            receipt = RepositoryFundingReceipt.Create(
                cortex.Step, Policy.ID, decision.DecisionID, decisionEventID, funding.QuotaDecisionID,
                decision.ReadoutIdentity, new CortexPolicyCandidateFingerprint(decision.Readout.CandidateFingerprint),
                decision.Readout.CandidateOccurrenceDigest, decision.Readout.GrammarRevision,
                decision.ReadoutContext.CanonicalState, proposal.CandidateDigest, proposal.Candidate.Canonical,
                proposal.Revision, World.WorldSHA256, _access.AccessSHA256, callSHA,
                funding.PlannedArmSteps, funding.HeldArmSteps, funding.UsedSteps, funding.RemainingQuota,
                readoutEventID, new LoopClosureDigest(_lastReadout?.ReceiptSHA256 ?? readoutSHA), funding, receiptAuthority);
            receipt = receipt.BindEventPayloadSHA();
            _fundingReceipts.Add(receipt);
            if (stagePending) _pendingFundingReceipt = receipt;
            if (cortex.Tape.NextId != receiptEventID.Value)
                throw new InvalidDataException("repository funding receipt event reservation was consumed");
            TapeEventID emittedReceiptID = TapePacketCreator.AppendRepositoryLineageReceipt(
                cortex.Tape, cortex.Journal, cortex.Step, receipt);
            if (emittedReceiptID != receiptEventID
                || !cortex.Tape.Resolve(emittedReceiptID, out byte[] emittedPayload)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(emittedPayload, out string emittedKind, out string emittedCanonical, out string emittedDigest)
                || emittedKind != receipt.Kind
                || emittedCanonical != receipt.Canonical
                || emittedDigest != receipt.ReceiptSHA256
                || Convert.ToHexStringLower(SHA256.HashData(emittedPayload)) != receipt.EventPayloadSHA256)
                throw new InvalidDataException("repository funding receipt packet identity diverged");
            if (funding.Decision == CortexPolicyQuotaDecisions.Paid)
            {
                if (capturedSeed is not CortexForkSeed exactSeed)
                throw new InvalidDataException("repository paid receipt has no captured prepared seed");
                preparedSeed = exactSeed;
            }
            else if (!cortex.TryLoadPolicyBoundarySeed(in funding, out preparedSeed))
            {
                SettleFailedFunding(cortex, in funding);
                return false;
            }
                return true;
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException
                or FormatException or ArgumentException or InvalidOperationException or OverflowException)
            {
                SettleFailedFunding(cortex, in funding);
                throw;
            }
        }

        private static void SettleFailedFunding(Cortex cortex, in CortexPolicyTrialQuotaDecision funding)
        {
            if (funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
                return;
            try
            {
                if (!cortex.TryReadPolicyTrialCompletion(funding.QuotaDecisionID, out _))
                    cortex.CompletePolicyTrial(in funding, 0, 0, CortexPolicyVerifierOutcomes.Failed, 0);
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            {
                throw new InvalidDataException("repository paid failure could not settle its full refund", error);
            }
        }

        private static bool TryResolvePolicyCustodyPacket(
            Cortex cortex,
            TapeEventID eventID,
            CortexPolicyID policy,
            out byte[] payload)
        {
            payload = [];
            return cortex.Tape.TryGetEventView(eventID, out TapeEventView view)
                && view.Provenance == Provenances.Execution
                && view.HasRole(TapeEventRoles.AuditOnly)
                && string.Equals(view.Source, "policy:" + policy.Value, StringComparison.Ordinal)
                && cortex.Tape.Resolve(eventID, out payload);
        }

        private static bool TryFindFundingPacket(
            Cortex cortex,
            in CortexPolicyTrialQuotaDecision funding,
            out TapeEventID eventID,
            out byte[] payload)
        {
            eventID = default;
            payload = [];
            int matches = 0;
            foreach (TapeEventView view in cortex.Tape.GetEventViews())
            {
                if (!string.Equals(view.Source, "policy:" + funding.Policy.Value, StringComparison.Ordinal)
                    || view.Provenance != Provenances.Execution
                    || !view.HasRole(TapeEventRoles.AuditOnly)
                    || !cortex.Tape.Resolve(view.Id, out byte[] candidatePayload)
                    || !TapePacketCreator.TryDecodePolicyTrialQuota(candidatePayload, out CortexPolicyTrialQuotaDecision packet)
                    || packet != funding) continue;
                matches++;
                if (matches > 1) { eventID = default; payload = []; return false; }
                eventID = view.Id;
                payload = candidatePayload;
            }
            return matches == 1;
        }

        private static bool TryFindSettlementPacket(
            Cortex cortex,
            in CortexPolicyTrialQuotaDecision funding,
            in CortexPolicyTrialCompletion expectedSettlement,
            out TapeEventID eventID,
            out byte[] payload,
            out CortexPolicyTrialCompletion settlement)
        {
            eventID = default;
            payload = [];
            settlement = default;
            int matches = 0;
            foreach (TapeEventView view in cortex.Tape.GetEventViews())
            {
                if (!string.Equals(view.Source, "policy:" + funding.Policy.Value, StringComparison.Ordinal)
                    || view.Provenance != Provenances.Execution
                    || view.Roles != TapeEventRoles.AuditOnly
                    || !cortex.Tape.Resolve(view.Id, out byte[] candidatePayload)
                    || !TapePacketCreator.TryReadPolicyTrialCompletion(candidatePayload, out CortexPolicyTrialCompletion candidateSettlement)
                    || candidateSettlement != expectedSettlement
                    || candidateSettlement.ActualExecutedArmSteps + candidateSettlement.ReclaimedOrUnused != funding.PlannedArmSteps)
                    continue;
                matches++;
                eventID = view.Id;
                payload = candidatePayload;
                settlement = candidateSettlement;
            }
            return matches == 1;
        }

        private static void VerifyPaidOutcomePackets(
            Cortex cortex,
            Tape tape,
            in RepositoryPaidOutcomeReceipt outcome,
            in RepositoryFundingReceipt funding)
        {
            if (!tape.TryGetEventView(outcome.DecisionEventID, out TapeEventView decisionView)
                || decisionView.Source != "policy:" + outcome.PolicyID.Value
                || decisionView.Provenance != Provenances.Execution
                || decisionView.Roles != TapeEventRoles.AuditOnly
                || !tape.Resolve(outcome.DecisionEventID, out byte[] decisionPayload)
                || !TapePacketCreator.TryDecodePolicyDecision(decisionPayload, out CortexPolicyDecisionPacket decisionPacket)
                || decisionPacket.DecisionID != outcome.DecisionID
                || Convert.ToHexStringLower(SHA256.HashData(decisionPayload)) != outcome.DecisionPayloadSHA256)
                throw new InvalidDataException("native repository paid decision packet authority diverged");
            if (!tape.TryGetEventView(outcome.ReadoutEventID, out TapeEventView readoutView)
                || readoutView.Source != "repository:lineage"
                || readoutView.Provenance != Provenances.Execution
                || readoutView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !tape.Resolve(outcome.ReadoutEventID, out byte[] readoutPayload)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(readoutPayload, out string readoutKind, out _, out _)
                || readoutKind != "readout"
                || Convert.ToHexStringLower(SHA256.HashData(readoutPayload)) != outcome.ReadoutPayloadSHA256)
                throw new InvalidDataException("native repository paid readout packet authority diverged");
            if (!TryFindFundingPacket(cortex, funding.FundingDecision, out TapeEventID fundingEventID, out byte[] fundingPayload)
                || fundingEventID != outcome.FundingEventID
                || Convert.ToHexStringLower(SHA256.HashData(fundingPayload)) != outcome.FundingPayloadSHA256)
                throw new InvalidDataException("native repository paid funding packet authority diverged");
            if (outcome.BoundaryEventID.Value > 0
                && (!tape.TryGetEventView(outcome.BoundaryEventID, out TapeEventView boundaryView)
                    || boundaryView.Source != "policy:" + outcome.PolicyID.Value
                    || boundaryView.Provenance != Provenances.Execution
                    || boundaryView.Roles != TapeEventRoles.AuditOnly
                    || !tape.Resolve(outcome.BoundaryEventID, out byte[] boundaryPayload)
                    || Convert.ToHexStringLower(SHA256.HashData(boundaryPayload)) != outcome.BoundaryPayloadSHA256))
                throw new InvalidDataException("native repository paid boundary packet authority diverged");
        }

        private PolicyBoundaryObligation CreateBoundaryObligation()
        {
            PolicyBoundaryIdentity identity = new(
                Policy.ID,
                "repository-frontier-candidate",
                string.Concat("repository-policy:", Policy.Schema.FeatureCount.ToString(System.Globalization.CultureInfo.InvariantCulture), ":",
                    Policy.Schema.ActionCount.ToString(System.Globalization.CultureInfo.InvariantCulture), ":",
                    Policy.Schema.OutcomeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                string.Concat("repository-world:", World.WorldSHA256, ":query:", QuerySHA256),
                RepositoryPolicyBoundaryDomain.Instance.BoundaryFeatureID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "candidate-species");
            return new PolicyBoundaryObligation(identity);
        }

        public void PrepareResumeArtifacts(Run run)
        {
            Dictionary<string, string> authority = new(StringComparer.Ordinal)
            {
                ["root"] = RootPath,
                ["glob"] = World.Glob,
                ["world_sha256"] = World.WorldSHA256,
                ["query"] = Query,
                ["query_sha256"] = QuerySHA256,
                // The arm is authority, not a launch flag. Omit it and a resumed run rebuilds its
                // runtime on the constructor default — tools-live — so a blocked or shuffled arm
                // would silently become the live arm and its null would be banked as the thing it
                // was built to refute.
                ["arm"] = RepositoryToolArmNames.Render(_mediation.Arm),
            };
            if (_registration is { } registration)
                RepositoryNativeTaskAuthority.Write(authority, registration);
            run.Write(AuthorityFile, RonSerializer.SerializeToUtf8(in authority));
        }

        void ICurriculumTerminalTransition.CaptureTerminalTransition(Cortex cortex, Run run, Tape tape, Journal journal)
        {
            ArgumentNullException.ThrowIfNull(cortex);
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(tape);
            ArgumentNullException.ThrowIfNull(journal);
            if (_terminalSealAppended)
                return;

            // THE CHAIN-WIDTH READ, at the end of the life it describes. The shuffled-predecessor
            // null can only bite when a species bucket holds two or more compatible nodes; with one
            // law and one composition there is nothing to permute and the null is vacuous however
            // much fuel the run burned. So the width the run actually produced has to be legible
            // beside the null's verdict, or a vacuous null reads as a defect in the assay.
            //
            // It is stamped with the run, the arm and the step because a width is a prediction ABOUT a
            // life: an unstamped one cannot be told from the same read taken on a newborn runtime,
            // which is exactly the reading that cost a full cut of this rung.
            int confirmed = _occurrenceCheckReceipts.Count(static receipt => receipt.Outcome == RepositoryOccurrenceCheckOutcomes.Confirmed);
            int refuted = _occurrenceCheckReceipts.Count(static receipt => receipt.Outcome == RepositoryOccurrenceCheckOutcomes.Refuted);
            Trace.Note($"  → chain width · run {Path.GetFileName(run.Dir)} · arm {RepositoryToolArmNames.Render(_mediation.Arm)} · step {cortex.Step}"
                + $" · looks {_look} (mediated {_mediation.Looks} · withheld {_mediation.Withheld})"
                + $" · accesses {_access.Count} · occurrence checks {_occurrenceCheckReceipts.Count}"
                + $" (confirmed {confirmed} · refuted {refuted} · unobserved {_occurrenceCheckReceipts.Count - confirmed - refuted})"
                + $" · pattern occurrences {_pattern.OccurrenceCount} · compositions {_pattern.CompositionCount} · admissions {_pattern.AdmissionCount}");
            // Which PREDICTION species the organism actually spent its occurrence checks on, beside what the
            // frontier was holding. A composition stage that admits one species only is blind to a
            // census of totals: twelve confirmed occurrences of the wrong species look identical to
            // twelve of the right one until the species are named.
            Trace.Note($"  → prediction species · confirmed {string.Join(" · ", _occurrenceCheckReceipts
                .GroupBy(static receipt => receipt.Prediction.Species)
                .OrderBy(static group => group.Key.ToString(), StringComparer.Ordinal)
                .Select(static group => $"{group.Key} {group.Count()}"))}");
            Trace.Note($"  → frontier species · {_frontier.RenderSpeciesCensus()}");
            Trace.Note($"  → selection cause · {string.Join(" · ", _selectionCauses
                .OrderBy(static cause => cause.Key.ToString(), StringComparer.Ordinal)
                .Select(static cause => $"{cause.Key} {cause.Value}"))}"
                + "  (only GrammarCandidate opens the five-link producers)");

            TapeEventView[] seals = tape.GetEventViews()
                .Where(static view => view.Source == "repository-seal")
                .ToArray();
            if (seals.Length > 1)
                throw new InvalidDataException("native repository terminal seal repeats");
            if (seals.Length == 1)
            {
                if (!tape.Resolve(seals[0].Id, out byte[] payload)
                    || !TapePacketCreator.TryDecodeRepositoryLoopSeal(payload, out TapeEventID sealEventID,
                        out _, out _)
                    || !File.Exists(run.PathOf(RepositoryNativeTerminalEvidence.FileName)))
                    throw new InvalidDataException("native repository terminal seal exists without its RON authority");
                if (sealEventID != seals[0].Id)
                    throw new InvalidDataException("native repository terminal seal packet event identity diverges");
                _ = RepositoryNativeTerminalEvidence.ValidateAndDecode(run, tape, journal);
                _terminalSealAppended = true;
                return;
            }

            RepositoryLoopClosureWorldSnapshot world = new(World.CaptureFiles()
                .Select(static file => new RepositoryLoopClosureWorldFile(file.Path, file.Content)).ToArray());
            world.Validate();
            RepositoryNativeRuntimeSnapshot runtime = new(
                RootPath, World.Glob, Query, QuerySHA256,
                ComputeSourceSHA256(RootPath, World.Glob, Query, QuerySHA256, world.WorldSHA256),
                world, _access, _frontier.CaptureSnapshot(), _pattern.CaptureSnapshot(), _frontier, _pattern);
            LoopLineageTapeSnapshot preSealTape = RepositoryNativeTerminalEvidence.CaptureCanonicalTape(tape);
            JournalSnapshot preSealJournal = journal.CaptureSnapshot();
            RepositoryNativeRegisteredAuthorityRON? registeredAuthority = CreateRegisteredAuthority();
            string immutableAuthoritySHA256 = RepositoryNativeTerminalEvidence.ComputeImmutableAuthoritySHA256(
                runtime, tape, preSealTape, preSealJournal, registeredAuthority);
            TapePacketCreator.AppendRepositoryLoopSeal(tape, journal, cortex.Step,
                preSealTape.Digest, immutableAuthoritySHA256, out RepositoryLoopClosureTapeSeal seal);
            LoopLineageTapeSnapshot finalTape = RepositoryNativeTerminalEvidence.CaptureCanonicalTape(tape);
            JournalSnapshot finalJournal = journal.CaptureSnapshot();
            seal.Validate(finalTape.Events, preSealTape.Digest);
            RepositoryNativeTerminalEvidenceRON document = RepositoryNativeTerminalEvidence.Capture(
                run, runtime, tape, preSealTape, preSealJournal, immutableAuthoritySHA256, seal, finalTape, finalJournal,
                registeredAuthority);
            RepositoryNativeTerminalEvidence.Write(run, document);
            _ = RepositoryNativeTerminalEvidence.ValidateAndDecode(run, tape, journal);
            _terminalSealAppended = true;
        }

        private RepositoryNativeRegisteredAuthorityRON? CreateRegisteredAuthority()
        {
            if (_registration is not { } registration) return null;
            byte[] document = registration.Encode();
            return RepositoryNativeRegisteredAuthorityRON.Create(
                registration.RegistrationSHA256,
                Convert.ToHexStringLower(SHA256.HashData(document)),
                registration.TaskAuthoritySHA256,
                registration.ToolAuthoritySHA256,
                registration.PolicyAuthoritySHA256,
                registration.CandidateAuthoritySHA256,
                registration.InitialStateSHA256,
                registration.OfferedFuelSHA256);
        }

        private static string ComputeSourceSHA256(string root, string glob, string query, string querySHA256, string worldSHA256)
            => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
                "repository-native-source-v1", root, glob, query, querySHA256, worldSHA256))));

        private void EnsureTerminalTransitionOpen()
        {
            if (_terminalSealAppended)
                throw new InvalidOperationException("native repository runtime is sealed; no in-run event may append afterward");
        }

        // The runtime pool is a probe contract, not an intake rail. The query is
        // already seeded above; this sample lets the generic world setup construct a
        // truthful probe without ingesting the query a second time.
        public void AppendProbeSamples(List<byte[]> samples) => samples.Add(_queryBytes);

        public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
        {
            EnsureTerminalTransitionOpen();
            return new(0, false, 0);
        }

        public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
            => EnsureTerminalTransitionOpen();

        public bool Drained => true;
        public bool Exhausted => true;
        public int IngestedCount => 0;
        public int WorkloadCount => 1;
        public int MixEvery
        {
            get => _mixEvery;
            set
            {
                EnsureTerminalTransitionOpen();
                _mixEvery = value;
            }
        }
        public int StreakResets => 0;
        public double LastPickCoverage => double.NaN;

        public bool TryPropose(Cortex cortex, List<CortexActionArgument> arguments,
            Func<Tool.ToolVerbs, RepositoryNativeTool?> resolveTool, out CortexAction action)
        {
            EnsureTerminalTransitionOpen();
            action = CortexAction.None;
            if (_answered || !_frontier.TryPropose(out RepositoryCandidateProposal proposal))
            {
                _lastSelection = RepositoryCandidateSelectionReceipt.Exhaust(cortex.Step, _frontier.Revision,
                    _answered ? "answered" : "frontier-exhausted");
                return false;
            }

            RepositoryCandidateSpecies launchpadSpecies = proposal.Candidate.Species;
            Span<MetricSample> features = stackalloc MetricSample[8]
            {
                Metric((ushort)PolicyMetricIDs.FrontierCandidates, _frontier.Count),
                Metric((ushort)PolicyMetricIDs.EligibleCandidates, _frontier.EligibleCount),
                Metric((ushort)PolicyMetricIDs.ObservedPaths, _observedPaths.Count),
                Metric((ushort)PolicyMetricIDs.FrontierRevision, checked((long)_frontier.Revision.Value)),
                Metric((ushort)PolicyMetricIDs.QueryLength, Query.Length),
                Metric((ushort)PolicyMetricIDs.HasOccurrenceCheck, _lastOccurrenceCheck is null ? 0 : 1),
                Metric((ushort)PolicyMetricIDs.HasAnswer, _answered ? 1 : 0),
                Metric((ushort)PolicyMetricIDs.CandidateSpecies, (int)launchpadSpecies),
            };
            string frontierAuthority = _frontier.AuthoritySHA256;
            string selectedFrontierAuthority = frontierAuthority;
            PolicyCanonicalStateID state = Policy.State(_observedPaths.Count, _frontier.Count, _answered);
            CortexPolicyDecision decision = cortex.ChoosePolicyAction(
                Policy.ID, Policy.Action(launchpadSpecies), in state, features);
            if (!cortex.TryFindPolicyDecisionEvent(in decision, out TapeEventID decisionEventID)
                || !cortex.Tape.Resolve(decisionEventID, out byte[] decisionPayload))
                throw new InvalidDataException("native repository policy decision has no durable selection authority");
            RepositorySelectionReceipt selection = default;
            TapeEventID selectionEventID = default;
            byte[] selectionPayload = [];
            void EmitSelection(in RepositoryCandidateProposal finalSelection)
            {
                selection = RepositorySelectionReceipt.Create(
                    cortex.Step, Policy.ID, in decision, decisionEventID,
                    Convert.ToHexStringLower(SHA256.HashData(decisionPayload)), finalSelection, selectedFrontierAuthority,
                    _frontier.GetSelectionOrdinal(in finalSelection, null));
                selectionEventID = TapePacketCreator.AppendRepositorySelection(
                    cortex.Tape, cortex.Journal, cortex.Step, in selection);
                if (selectionEventID.Value <= decisionEventID.Value
                    || !cortex.Tape.Resolve(selectionEventID, out selectionPayload)
                    || !TapePacketCreator.TryReadRepositoryLineageReceipt(selectionPayload,
                        out string selectionKind, out string selectionCanonical, out string selectionDigest)
                    || selectionKind != selection.Kind || selectionCanonical != selection.Canonical || selectionDigest != selection.ReceiptSHA256)
                    throw new InvalidDataException("native repository selection custody diverged");
                _pendingSelectionReceipt = selection;
                _pendingSelectionEventID = selectionEventID;
                _hasPendingSelection = true;
            }
            RepositoryCandidateProposal selected = proposal;
            RepositoryCandidate? learnedCandidate = null;
            RepositoryPreferenceComparisonOutcomes preferenceOutcome = RepositoryPreferenceComparisonOutcomes.ComparisonNotAttempted;
            LoopClosureLinkAttempt? preferenceLink = null;
            if (decision.SelectionCause is CortexPolicySelectionCauses.GrammarCandidate or CortexPolicySelectionCauses.TrialOverride)
            {
                RepositoryCandidateSpecies learnedSpecies = default;
                RepositoryCandidateProposal learned = default;
                string capturedAuthority = "";
                bool learnedAvailable = Policy.TrySpecies(decision.Action, out learnedSpecies)
                    && (decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride
                        ? cortex.TryReadPolicyBoundaryForcedCandidate(
                            Policy.ID, out string capturedCanonical, out ulong capturedDigest,
                            out ulong capturedRevision, out capturedAuthority)
                            && _frontier.TryResolveCaptured(
                                new RepositoryFrontierRevision(capturedRevision),
                                new RepositoryCandidateDigest(capturedDigest), capturedCanonical,
                                capturedAuthority, out learned)
                        : _frontier.TryPropose(learnedSpecies, out learned));
                if (!learnedAvailable)
                {
                    if (decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride)
                        throw new InvalidDataException("forced repository trial selected a candidate absent from its captured frontier");
                    cortex.ResolveCensoredPolicyOutcome(in decision);
                    selected = proposal;
                    EmitSelection(in selected);
                    RepositoryPreferenceComparisonReceipt unavailable = RepositoryPreferenceComparisonReceipt.Create(
                        cortex.Step, Policy.ID, decision.DecisionID, proposal.Revision, frontierAuthority,
                        proposal.Candidate, null, selectionEventID,
                        Convert.ToHexStringLower(SHA256.HashData(selectionPayload)),
                        LoopClosureLinkAttemptStore.DigestJournalReceipt(cortex.Step, "repository-selection", selectionEventID.Value).Value,
                        RepositoryPreferenceComparisonOutcomes.CandidateUnavailable);
                    TapePacketCreator.AppendRepositoryPreferenceComparison(cortex.Tape, cortex.Journal, cortex.Step, in unavailable);
                    _lastSelection = new RepositoryCandidateSelectionReceipt(cortex.Step, proposal.Revision,
                        RepositoryCandidateDigest.Zero, false, true, "candidate-unavailable");
                    return false;
                }
                selected = learned;
                if (decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride)
                    selectedFrontierAuthority = capturedAuthority;
                learnedCandidate = learned.Candidate;
                preferenceOutcome = decision.SelectionCause == CortexPolicySelectionCauses.TrialOverride
                    ? RepositoryPreferenceComparisonOutcomes.ComparisonNotAttempted
                    : proposal.Candidate.Species == learned.Candidate.Species
                    && string.Equals(proposal.Candidate.Canonical, learned.Candidate.Canonical, StringComparison.Ordinal)
                    && proposal.Candidate.Digest == learned.Candidate.Digest
                    ? RepositoryPreferenceComparisonOutcomes.ReflexAgreement
                    : RepositoryPreferenceComparisonOutcomes.Diverged;
                if (selected.Candidate.Species != learnedSpecies)
                    throw new InvalidDataException("native learned policy selected a mismatched candidate species");
            }
            _selectionCauses[decision.SelectionCause] = _selectionCauses.GetValueOrDefault(decision.SelectionCause) + 1;
            if (selectionEventID.Value == 0)
                EmitSelection(in selected);
            if (decision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate)
            {
                RepositoryPreferenceComparisonReceipt preference = RepositoryPreferenceComparisonReceipt.Create(
                    cortex.Step, Policy.ID, decision.DecisionID, proposal.Revision, frontierAuthority,
                    proposal.Candidate, learnedCandidate, selectionEventID,
                    Convert.ToHexStringLower(SHA256.HashData(selectionPayload)),
                    LoopClosureLinkAttemptStore.DigestJournalReceipt(cortex.Step, "repository-selection", selectionEventID.Value).Value, preferenceOutcome);
                TapeEventID preferenceEventID = TapePacketCreator.AppendRepositoryPreferenceComparison(
                    cortex.Tape, cortex.Journal, cortex.Step, in preference);
                if (preferenceOutcome == RepositoryPreferenceComparisonOutcomes.Diverged)
                {
                    if (!cortex.Tape.Resolve(preferenceEventID, out byte[] preferencePayload))
                        throw new InvalidDataException("repository preference packet was not retained");
                    if (!cortex.TryRecordRepositoryPreferenceLink(
                            cortex.Step, preferenceEventID, preferencePayload, out LoopClosureLinkAttempt recordedPreference))
                        throw new InvalidDataException("repository divergent preference link was not admitted to the live lineage");
                    preferenceLink = recordedPreference;
                }
            }
            proposal = selected;
            RepositoryNativeTool? tool = resolveTool(proposal.Candidate.Verb);
            if (tool is null)
            {
                _lastSelection = RepositoryCandidateSelectionReceipt.Exhaust(cortex.Step, _frontier.Revision,
                    "candidate-tool-missing");
                return false;
            }
            action = RepositoryCandidateActionAdapter.Create(in proposal, tool, arguments);
            _pendingProposal = proposal;
            if (decision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate)
            {
                if (!cortex.TryGetPolicyBoundaryDomain(Policy.ID, out IPolicyBoundaryDomain domain)
                    || !domain.TryVerifyR4(cortex, in decision,
                        out TapeEventID r4DecisionEventID, out LoopClosureR4Provenance provenance))
                    throw new InvalidDataException("native learned repository readout has no generic R4 custody");
                _lastReadout = RepositoryReadoutReceipt.Create(cortex.Step, proposal.Candidate,
                    in decision, r4DecisionEventID, in provenance);
                _lastReadout = _lastReadout.Value.BindFrontierAuthority(
                    frontierAuthority, proposal.Revision,
                    _frontier.GetSelectionOrdinal(in proposal, decision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate ? proposal.Candidate.Species : null));
                TapeEventID readoutEventID = TapePacketCreator.AppendRepositoryLineageReceipt(
                    cortex.Tape, cortex.Journal, cortex.Step, _lastReadout.Value);
                bool ordinaryPaid = TryAcquireFunding(cortex, in decision, in proposal, decisionEventID, readoutEventID,
                        out RepositoryFundingReceipt ordinaryFunding, out _);
                if (!ordinaryPaid)
                    _pendingFundingReceipt = null;
                if (cortex.TryReadPolicyReadout(Policy.ID, out CortexPolicyReadoutReceipt boundaryReadout)
                    && TryAcquireFunding(cortex, in decision, in proposal, decisionEventID, readoutEventID,
                        out RepositoryFundingReceipt divergenceFunding, out CortexForkSeed divergenceSeed,
                        horizonSteps: cortex.Config.Learning.Policies.TrialHorizons[^1], armCount: 4, stagePending: false))
                {
                    if (!TryRunRepositoryPaidDivergence(cortex, in decision, in boundaryReadout,
                            in divergenceFunding, in divergenceSeed, in provenance, preferenceLink,
                            decisionEventID, decisionPayload))
                        throw new InvalidDataException("repository paid divergence did not close its captured four-arm trial");
                }
            }
            _pendingPolicyDecision = decision;
            _hasPendingPolicyDecision = true;
            _lastSelection = new RepositoryCandidateSelectionReceipt(cortex.Step, proposal.Revision,
                proposal.CandidateDigest, true, false,
                decision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
                    ? "policy-selected" : "frontier-selected");
            return true;

            static MetricSample Metric(ushort id, long value)
                => new(new MetricID(id), NumericValue.FromI64(value));
        }

        private static bool TryRunRepositoryPaidDivergence(
            Cortex cortex,
            in CortexPolicyDecision decision,
            in CortexPolicyReadoutReceipt readout,
            in RepositoryFundingReceipt fundingReceipt,
            in CortexForkSeed seed,
            in LoopClosureR4Provenance provenance,
            LoopClosureLinkAttempt? preferenceLink,
            TapeEventID decisionEventID,
            byte[] decisionPayload)
        {
            if (!cortex.TryGetPolicyBoundaryDomain(Policy.ID, out IPolicyBoundaryDomain domain)
                || !cortex.TryGetPolicyBoundaryObligation(Policy.ID, out PolicyBoundaryObligation obligation)
                || !readout.IsExact || readout.ReadoutCandidateFingerprint == 0)
                return false;
            CortexPolicyTrialQuotaDecision funding = fundingReceipt.FundingDecision;
            if (funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                || funding.ArmCount != 4)
                return false;
            CortexPolicyTrialAuthorityIdentity authorityIdentity = CortexPolicyTrialAuthorityIdentity.FromReadout(in readout);
            CortexRunConfig config = cortex.Config.ToRunConfig(null);
            int[] horizons = [.. cortex.Config.Learning.Policies.TrialHorizons];
            if (horizons.Length == 0 || horizons[^1] != funding.RequestedHorizonSteps) return false;
            string parentRunID = Path.GetFileName(cortex.CurrentRun.Dir);
            CortexForkArm<PolicyBoundaryTrialOutcome>[][] arms = new CortexForkArm<PolicyBoundaryTrialOutcome>[horizons.Length][];
            for (int index = 0; index < horizons.Length; index++)
            {
                (Run baseline, CortexForkMaterializationContract baselineContract) = cortex.CurrentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.Baseline, funding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                (Run candidateRun, CortexForkMaterializationContract candidateContract) = cortex.CurrentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.Candidate, funding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                (Run forced, CortexForkMaterializationContract forcedContract) = cortex.CurrentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.ForcedNull, funding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                (Run reflex, CortexForkMaterializationContract reflexContract) = cortex.CurrentRun.CreateMaterializedChildRun(
                    CortexForkRailRoles.ReflexFrozen, funding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                arms[index] =
                [Cortex.CreatePolicyBoundaryArm(baseline.Dir, cortex.Step, horizons[index], PolicyBoundaryArms.Baseline,
                    config, domain, authorityIdentity, CortexPolicyAuthorities.Launchpad,
                    railRole: CortexForkRailRoles.Baseline, parentRunID: parentRunID, materializationContract: baselineContract,
                    obligation: obligation.ID),
                 Cortex.CreatePolicyBoundaryArm(candidateRun.Dir, cortex.Step, horizons[index], PolicyBoundaryArms.Candidate,
                    config, domain, authorityIdentity, CortexPolicyAuthorities.Grammar,
                    railRole: CortexForkRailRoles.Candidate, parentRunID: parentRunID, materializationContract: candidateContract,
                    obligation: obligation.ID),
                 Cortex.CreatePolicyBoundaryArm(forced.Dir, cortex.Step, horizons[index], PolicyBoundaryArms.ForcedDivergentNull,
                    config, domain, authorityIdentity, CortexPolicyAuthorities.Grammar, forced: true,
                    requireOrdinaryOutcome: horizons[index] == horizons[^1], railRole: CortexForkRailRoles.ForcedNull,
                    parentRunID: parentRunID, materializationContract: forcedContract, obligation: obligation.ID),
                 Cortex.CreatePolicyBoundaryArm(reflex.Dir, cortex.Step, horizons[index], PolicyBoundaryArms.ReflexFrozenControl,
                    config, domain, authorityIdentity, CortexPolicyAuthorities.Shadow, frozen: true,
                    railRole: CortexForkRailRoles.ReflexFrozen, parentRunID: parentRunID, materializationContract: reflexContract,
                    obligation: obligation.ID)];
            }
            PolicyBoundaryTeacherCorroboration teacher = new(in provenance);
            bool admitted = cortex.TryRunPaidPolicyBoundaryWithReceipt(in funding, seed, obligation.Identity,
                PolicyBoundaryRational.Zero, PolicyBoundaryRational.Zero,
                arms.Select(static row => row[0]).ToArray(), arms.Select(static row => row[1]).ToArray(),
                arms.Select(static row => row[2]).ToArray(), arms.Select(static row => row[3]).ToArray(), horizons,
                readout.Fingerprint, readout.ReadoutCandidateFingerprint, readout.Revision.Value, teacher,
                out PolicyBoundaryForkReceipt forkReceipt, out CortexPolicyTrialCompletion settlement,
                out TapeEventID boundaryEventID, out byte[] boundaryPayload);
            if (!admitted || !Cortex.TryReadPolicyBoundaryDivergenceArms(in decision, in forkReceipt, domain,
                    out PolicyBoundaryDivergenceCandidateTerminal candidate, out PolicyBoundaryDivergenceArmOutcome forcedNull)
                || !cortex.TryAdjudicatePaidDivergence(in decision, in readout, in funding, in settlement,
                    in forkReceipt, in candidate, in forcedNull, teacher,
                    out PolicyBoundaryDivergenceAdjudication adjudication, provenance))
                return false;
            cortex.WriteLoopClosureDivergenceProof(in adjudication);
            cortex.CloseLoopClosureAdjudication(in adjudication,
                out TapeEventID fundedDivergenceEventID, out byte[] fundedDivergencePayload,
                out TapeEventID outcomeEventID, out byte[] outcomePayload);
            if (!cortex.TryRecordRepositoryInterventionLink(
                    cortex.Step, in decision, decisionEventID, decisionPayload,
                    fundedDivergenceEventID, fundedDivergencePayload, in funding, preferenceLink,
                    out LoopClosureLinkAttempt interventionLink))
                return false;
            return cortex.TryRecordRepositoryDivergenceContinuationLinks(
                cortex.Step, in decision, in forkReceipt, in forcedNull, in funding,
                in adjudication, in interventionLink,
                boundaryEventID, boundaryPayload,
                outcomeEventID, outcomePayload);
        }

        public bool TryReadPendingCandidate(CortexAction action, List<CortexActionArgument> arguments,
            Tool.ToolVerbs verb, out RepositoryCandidate candidate)
        {
            candidate = default!;
            if (_pendingProposal is not { } proposal || !_frontier.IsCurrent(proposal)) return false;
            if (proposal.Candidate.Verb != verb || arguments.Count == 0
                || !string.Equals(arguments[0].Value, proposal.Candidate.Argument, StringComparison.Ordinal)
                || !string.Equals(action.Raw, Tool.ToolCall.Create(verb, proposal.Candidate.Argument).Raw, StringComparison.Ordinal))
                return false;
            candidate = proposal.Candidate;
            return true;
        }

        public CortexActionAdmissionDecision ValidateProposal(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            Tool.ToolVerbs verb)
        {
            EnsureTerminalTransitionOpen();
            if (!TryReadPendingCandidate(action, arguments, verb, out RepositoryCandidate candidate))
            {
                RefundPendingFunding(cortex);
                DiscardPendingExecution();
                return CortexActionAdmissionDecision.Deny("frontier-proposal-stale");
            }
            if (_pendingPolicyDecision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
                && !TryValidateFundingLease(cortex, action, candidate, out string denial))
            {
                RefundPendingFunding(cortex);
                DiscardPendingExecution();
                return CortexActionAdmissionDecision.Deny(denial);
            }
            return CortexActionAdmissionDecision.Admit(
                _pendingPolicyDecision.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
                    ? "repository-funding-admitted" : "frontier-proposal");
        }

        private bool TryValidateFundingLease(
            Cortex cortex,
            CortexAction action,
            RepositoryCandidate candidate,
            out string denial)
        {
            denial = "repository-funding-denied";
            if (!cortex.TryGetPolicyBoundaryDomain(Policy.ID, out IPolicyBoundaryDomain domain)
                || !cortex.TryGetPolicyBoundaryObligation(Policy.ID, out PolicyBoundaryObligation obligation)
                || !domain.PolicyID.Equals(Policy.ID)
                || !domain.PolicyBinding.PolicyID.Equals(Policy.ID))
            {
                denial = "repository-funding-domain-unavailable";
                return false;
            }
            if (_pendingFundingDecision is not { } funding
                || _pendingFundingReceipt is not { } receipt
                || funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
            {
                denial = "repository-funding-unfunded";
                return false;
            }
            try { receipt.Validate(); }
            catch (InvalidDataException) { denial = "repository-funding-receipt-invalid"; return false; }
            if (!_fundingReceipts.Any(existing => existing.ReceiptSHA256 == receipt.ReceiptSHA256)
                || receipt.PolicyID != Policy.ID
                || receipt.DecisionID != _pendingPolicyDecision.DecisionID
                || receipt.CandidateDigest != candidate.Digest
                || receipt.CandidateCanonical != candidate.Canonical
                || receipt.WorldSHA256 != World.WorldSHA256
                || receipt.Authority.AccessEntryCount < 0
                || receipt.Authority.AccessEntryCount > _access.Count
                || receipt.AccessSHA256 != _access.ComputeAccessSHA256((int)receipt.Authority.AccessEntryCount)
                || receipt.QuotaDecisionID != funding.QuotaDecisionID
                || receipt.FundingDecision != funding
                || funding.CandidateFingerprint != receipt.CandidateFingerprint.Value
                || funding.ReadoutFingerprint != receipt.ReadoutFingerprint.Value
                || funding.CanonicalState != receipt.CanonicalState
                || receipt.FrontierRevision != _pendingProposal!.Value.Revision
                || receipt.Authority.FrontierAuthoritySHA256 != _frontier.AuthoritySHA256)
            {
                denial = "repository-funding-stale";
                return false;
            }
            int expectedOrdinal = _frontier.GetSelectionOrdinal(_pendingProposal.Value, candidate.Species);
            if (receipt.Authority.SelectionOrdinal != expectedOrdinal)
            {
                denial = "repository-funding-ordinal-mismatch";
                return false;
            }
            Tool.ToolCall call = Tool.ToolCall.Create(candidate.Verb, candidate.Argument);
            if (receipt.CallSHA256 != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Raw))))
            {
                denial = "repository-funding-call-mismatch";
                return false;
            }
            if (!cortex.TryReadPolicyBoundarySeedAuditOnlyDigest(
                    funding.QuotaDecisionID, out string seedAuditOnlyDigest)
                || funding.SeedAuditOnlyDigest != seedAuditOnlyDigest)
            {
                denial = "repository-funding-custody-mismatch";
                return false;
            }
            return true;
        }

        private void RefundPendingFunding(Cortex cortex)
        {
            if (_pendingFundingDecision is { } funding
                && funding.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                cortex.CompletePolicyTrial(in funding, 0, null, CortexPolicyVerifierOutcomes.Failed, null);
            _pendingFundingDecision = null;
            _pendingFundingReceipt = null;
        }

        public void StageObservation(Tool.Observation observation)
        {
            EnsureTerminalTransitionOpen();
            _pendingObservation = observation;
            _hasPendingObservation = true;
        }

        public void DiscardPendingExecution()
        {
            EnsureTerminalTransitionOpen();
            _pendingProposal = null;
            _pendingPolicyDecision = default;
            _hasPendingPolicyDecision = false;
            _hasPendingObservation = false;
            _pendingOccurrenceCheck = null;
            _pendingObservationEventID = default;
            _hasPendingAdmissionPlan = false;
            _pendingAdmissionPlanBarren = false;
            _pendingAdmissionPlanCallSHA = "";
            _pendingSelectionReceipt = default;
            _pendingSelectionEventID = default;
            _hasPendingSelection = false;
            _pendingPatternOccurrence = null;
        }

        public void CommitObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, IReadOnlyList<CortexObservationField> fields, byte[] executionBytes,
            List<TapeEventID> eventIDs)
        {
            EnsureTerminalTransitionOpen();
            if (_lastCommittedStep == cortex.Step) return;
            if (_pendingProposal is not { } proposal || !_frontier.IsCurrent(proposal) || !_hasPendingObservation)
                throw new InvalidDataException("native repository observation has no current frontier proposal");
            Tool.Observation typed = _pendingObservation;
            Tool.ToolCall call = Tool.ToolCall.Create(proposal.Candidate.Verb, proposal.Candidate.Argument);
            string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Raw)));
            RepositoryOccurrenceCheckResult? occurrenceCheck = typed.OccurrenceCheck;
            // A barren look mints no admissionPlan, so — like an answer — its own execution event is the
            // source of record. The look still committed, still spent fuel, still advanced the
            // frontier; what it lacks is world evidence, not a place in the chain.
            bool emitsAdmissionPlan = proposal.Candidate is not RepositoryCandidate.AnswerPathCandidate && !_pendingAdmissionPlanBarren;
            if (eventIDs.Count == 0 || emitsAdmissionPlan && (!_hasPendingAdmissionPlan || _pendingObservationEventID.Value <= 0))
                throw new InvalidDataException("native repository observation is missing its admitted plan");
            TapeEventID predecessorEventID = emitsAdmissionPlan ? eventIDs[0] : eventIDs[^1];
            TapeEventID sourceEventID = emitsAdmissionPlan ? _pendingObservationEventID : eventIDs[^1];
            // A transition names ITS OWN candidate's call — the report recomputes that digest from the
            // candidate and refuses anything else, so the frontier may not substitute the call of the
            // entry its evidence came from. What makes that satisfiable is the journal recording every
            // access, including a verify's and a barren look's: the call a transition names is then
            // always a call the journal holds.
            if (!_frontier.TryCommit(proposal, predecessorEventID, sourceEventID, callSHA,
                _access.AccessSHA256, typed, fields, _access.Entries, occurrenceCheck))
                throw new InvalidDataException("native repository frontier proposal became stale before commit");
            _look++;
            foreach (Tool.RepositoryPath path in typed.HitPaths)
                if (path.Length > 0 && !_observedPaths.Contains(path.Value, StringComparer.Ordinal)) { _observedPaths.Add(path.Value); _observedPathLog.Add(path.Value); }
            if (typed.Answered) { _answered = true; }
            CortexPolicyDecision settledDecision = _pendingPolicyDecision;
            if (_hasPendingPolicyDecision)
            {
                Span<MetricSample> outcomes = stackalloc MetricSample[3]
                {
                    new(new MetricID((ushort)PolicyMetricIDs.EvidenceYield), NumericValue.FromI64(
                        typed.HitPaths.Count + typed.Loci.Count > 0 ? 1 : 0)),
                    new(new MetricID((ushort)PolicyMetricIDs.OccurrenceCheckResult), NumericValue.FromI64(
                        typed.OccurrenceCheck?.Outcome switch
                        {
                            RepositoryOccurrenceCheckOutcomes.Confirmed => 1,
                            RepositoryOccurrenceCheckOutcomes.Refuted => -1,
                            _ => 0,
                        })),
                    new(new MetricID((ushort)PolicyMetricIDs.TerminalSourceBacking), NumericValue.FromI64(
                        proposal.Candidate is RepositoryCandidate.AnswerPathCandidate
                            ? typed.Answered && typed.AnswerPath.Length > 0 ? 1 : -1
                            : 0)),
                };
                long cost = typed.OccurrenceCheck is { } verified
                    ? checked(Math.Max(0, verified.EvaluatorCost) + Math.Max(0, verified.AccessCost))
                    : Math.Max(1, executionBytes.Length);
                cortex.ResolvePolicyOutcome(in _pendingPolicyDecision, outcomes, invariantClean: true, conservedCost: cost);
                _pendingPolicyDecision = default;
                _hasPendingPolicyDecision = false;
            }
            RepositoryFundingReceipt? settledFundingReceipt = _pendingFundingReceipt;
            if (_pendingFundingDecision is { } funding
                && funding.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            {
                CortexPolicyTrialCompletion settlement = cortex.CompletePolicyTrial(in funding, 1, null,
                    CortexPolicyVerifierOutcomes.NotRecorded, null);
                if (!TryFindSettlementPacket(cortex, in funding, in settlement,
                        out TapeEventID settlementEventID, out byte[] settlementPayload, out CortexPolicyTrialCompletion packetSettlement)
                    || packetSettlement != settlement
                    || settlement.ActualExecutedArmSteps + settlement.ReclaimedOrUnused != funding.PlannedArmSteps)
                    throw new InvalidDataException("repository settlement packet custody diverged");
                eventIDs.Add(settlementEventID);
                if (settledFundingReceipt is not { } fundingReceipt)
                throw new InvalidDataException("repository paid outcome has no payment receipt");
                AppendPaidOutcomeAndEvidence(cortex, action, proposal, in settledDecision, in fundingReceipt,
                    in settlement, settlementEventID, settlementPayload, executionBytes, typed, eventIDs);
            }
            _pendingFundingDecision = null;
            _pendingFundingReceipt = null;
            if (_pendingPatternOccurrence is { } pendingOccurrence)
            {
                _pattern.TryAdmitOccurrence(pendingOccurrence.OccurrenceCheck, pendingOccurrence.SourceEventID,
                    pendingOccurrence.OccurrenceCheckReceiptEventID, _access);
                TapeEventID expectedCompositionEventID = new(cortex.Tape.NextId);
                if (_pattern.TryComposeSharedIdentifier(cortex.Step, expectedCompositionEventID,
                    pendingOccurrence.OccurrenceReceiptEventID, out RepositoryPatternComposition composition))
                {
                    TapeEventID emitted = TapePacketCreator.AppendRepositoryLineageReceipt(
                        cortex.Tape, cortex.Journal, cortex.Step, composition.Receipt);
                    if (emitted != expectedCompositionEventID || composition.Receipt.CompositionEventID != emitted)
                        throw new InvalidDataException("repository composed candidate event identity diverged");
                    eventIDs.Add(emitted);
                    EmitRepositoryCompositionLineage(cortex, pendingOccurrence, emitted);
                    (Symbol[] rawTape, int rawCount, RePairResult baseline) = Engine.Induce(cortex.Tape, WScale);
                    byte[] rawWeights = cortex.Tape.GrammarWeightsFor(WScale);
                    bool admitted;
                    RepositoryPatternGrammarAdmissionReceipt admission;
                    try
                    {
                        admitted = _pattern.TryAdmitComposedCandidate(
                            composition, in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                            cortex.InstallRevision?.Revision ?? GrammarRevisionID.Zero, WScale,
                            cortex.Tape, cortex.Journal, cortex.Step, out admission);
                    }
                    finally { System.Buffers.ArrayPool<byte>.Shared.Return(rawWeights); }
                    AppendRepositoryAdmissionEvents(cortex, admission, composition.Receipt.CompositionEventID, eventIDs);
                    if (admitted)
                        _frontier.AdmitComposedCandidate(composition.Conclusion, composition.Receipt);
                }
            }
            ReconcilePendingSelection(cortex.Tape, cortex.Step);
            AppendMountedTaskReceipts(cortex, proposal, typed, eventIDs);
            _pendingProposal = null;
            _hasPendingObservation = false;
            _hasPendingAdmissionPlan = false;
            _pendingAdmissionPlanBarren = false;
            _pendingAdmissionPlanCallSHA = "";
            _pendingPatternOccurrence = null;
            _pendingSelectionReceipt = default;
            _pendingSelectionEventID = default;
            _hasPendingSelection = false;
            _lastCommittedStep = cortex.Step;
        }

        private void AppendMountedTaskReceipts(
            Cortex cortex,
            RepositoryCandidateProposal proposal,
            Tool.Observation observation,
            List<TapeEventID> eventIDs)
        {
            if (_task is not { } task || eventIDs.Count == 0)
                return;
            task.Validate();
            ValidateMountedTaskChains(cortex.Tape, task);
            if (!_hasPendingSelection)
                throw new InvalidDataException("native repository task selection is missing from the durable tape");
            if (HasConfirmedTaskOutcome(cortex.Tape, task))
                return;
            RepositoryCandidate candidate = proposal.Candidate;
            if (!RepositoryLoopTaskSpeciesRules.MatchesCandidate(task.Species, candidate.Species))
                return;
            string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)));
            if (!TryBuildTaskEvidence(task, candidate, observation, callSHA, out TaskEvidence evidence))
                throw new InvalidDataException("native repository task observation cannot produce a typed outcome");
            RepositorySelectionReceipt selection = _pendingSelectionReceipt;
            RepositoryLoopTaskActionReceipt actionReceipt = RepositoryLoopTaskActionReceipt.Create(
                task.TaskID, task.Species, task.AuthoritySHA256, _pendingSelectionEventID,
                selection.ReceiptSHA256, selection.SelectionOrdinal, candidate,
                selection.FrontierRevision, selection.FrontierAuthoritySHA256);
            TapeEventID actionEventID = new(cortex.Tape.NextId);
            byte[] actionPayload = actionReceipt.Encode();
            RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck = new(
                task.Oracle.Mode, evidence.Outcome, task.Oracle.AuthoritySHA256, evidence.Prediction,
                evidence.TypedPredictionReceipt, World.WorldSHA256, _access.AccessSHA256,
                evidence.EvaluatorCost, evidence.AccessCost, evidence.AccessSequence,
                evidence.AccessEntrySHA256, _access.Count, actionEventID, callSHA, "");
            RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheckReceipt = RepositoryLoopTaskOccurrenceCheckReceipt.Create(
                task.TaskID, task.Species, in occurrenceCheck, actionEventID,
                Convert.ToHexStringLower(SHA256.HashData(actionPayload)), task.AuthoritySHA256);
            TapeEventID occurrenceCheckEventID = new(actionEventID.Value + 1);
            byte[] occurrenceCheckPayload = occurrenceCheckReceipt.Encode();
            RepositoryLoopTaskOutcomeReceipt outcomeReceipt = RepositoryLoopTaskOutcomeReceipt.Create(
                task.TaskID, task.Species, occurrenceCheck.Outcome, occurrenceCheckEventID,
                Convert.ToHexStringLower(SHA256.HashData(occurrenceCheckPayload)), candidate, evidence.ResultSpecies,
                evidence.SourcePath, evidence.SourceLine, evidence.SourceBytes, evidence.SourceSHA256, evidence.ResultContent,
                task.AuthoritySHA256);
            TapePacketCreator.RepositoryLoopTaskReceiptEventIDs receiptIDs =
                TapePacketCreator.AppendRepositoryLoopTaskTransaction(
                    cortex.Tape, cortex.Journal, cortex.Step, in actionReceipt, in occurrenceCheckReceipt, in outcomeReceipt);
            eventIDs.Add(receiptIDs.ActionEventID);
            eventIDs.Add(receiptIDs.OccurrenceCheckEventID);
            eventIDs.Add(receiptIDs.OutcomeEventID);
        }

        private void ReconcilePendingSelection(Tape tape, int currentStep)
        {
            if (_task is null || _hasPendingSelection || _pendingProposal is not { } proposal)
                return;
            TapeEventView[] selections = tape.GetEventViews()
                .Where(static view => view.Source == "repository-selection")
                .ToArray();
            for (int index = selections.Length - 1; index >= 0; index--)
            {
                TapeEventView view = selections[index];
                if (!tape.Resolve(view.Id, out byte[] payload)
                    || !RepositorySelectionReceipt.TryDecode(payload, out RepositorySelectionReceipt selection)
                    || selection.CandidateDigest != proposal.CandidateDigest
                    || selection.CandidateCanonical != proposal.Candidate.Canonical
                    || selection.FrontierRevision != proposal.Revision
                    || selection.Step != currentStep
                    || !MatchesPendingSelectionAuthority(tape, view.Id, selection))
                    continue;
                _pendingSelectionReceipt = selection;
                _pendingSelectionEventID = view.Id;
                _hasPendingSelection = true;
                return;
            }
        }

        private bool MatchesPendingSelectionAuthority(Tape tape, TapeEventID selectionEventID, in RepositorySelectionReceipt selection)
        {
            // The receipt carries the candidate as canonical text; the ordinal is only
            // meaningful against the candidate that text actually parses to.
            if (_task is null
                || selection.PolicyID != Policy.ID
                || _pendingPolicyDecision.DecisionID.Value == 0
                || selection.DecisionID != _pendingPolicyDecision.DecisionID
                || selection.FrontierAuthoritySHA256 != _frontier.AuthoritySHA256
                || selection.FrontierRevision != _frontier.Revision
                || !RepositoryCandidate.TryParseCanonical(selection.CandidateCanonical, out RepositoryCandidate selectedCandidate)
                || selectedCandidate.Digest != selection.CandidateDigest
                || selection.SelectionOrdinal != _frontier.GetSelectionOrdinal(
                    new RepositoryCandidateProposal(selection.FrontierRevision, selection.CandidateDigest, selectedCandidate),
                    selection.CandidateSpecies)
                || selection.ReadoutFingerprint != _pendingPolicyDecision.ReadoutIdentity.Value
                || selection.ReadoutCandidateFingerprint != _pendingPolicyDecision.Readout.CandidateFingerprint)
                return false;
            if (!tape.TryGetEventView(selection.DecisionEventID, out TapeEventView decisionView)
                || !decisionView.Source.StartsWith("policy:", StringComparison.Ordinal)
                || decisionView.Provenance != Provenances.Execution
                || decisionView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !tape.Resolve(selection.DecisionEventID, out byte[] decisionPayload)
                || Convert.ToHexStringLower(SHA256.HashData(decisionPayload)) != selection.DecisionPayloadSHA256)
                return false;
            if (!TapePacketCreator.TryDecodePolicyDecision(decisionPayload, out CortexPolicyDecisionPacket decisionPacket)
                || decisionPacket.DecisionID != selection.DecisionID
                || decisionPacket.Readout.ReadoutFingerprint != selection.ReadoutFingerprint
                || decisionPacket.Readout.ReadoutCandidateFingerprint != selection.ReadoutCandidateFingerprint
                || decisionPacket.Readout.ReadoutCandidateOccurrenceDigest != _pendingPolicyDecision.Readout.CandidateOccurrenceDigest
                || decisionPacket.Readout.GrammarRevision != _pendingPolicyDecision.Readout.GrammarRevision
                || decisionPacket.Readout.Authority != _pendingPolicyDecision.Authority
                || decisionPacket.Readout.SelectionCause != _pendingPolicyDecision.SelectionCause
                || decisionPacket.Readout.SelectedCandidateAction != _pendingPolicyDecision.Readout.SelectedCandidateAction)
                return false;
            if (_pendingFundingReceipt is { } funding
                && (funding.DecisionID != selection.DecisionID
                    || funding.CandidateDigest != selection.CandidateDigest
                    || funding.CandidateCanonical != selection.CandidateCanonical
                    || funding.FrontierRevision != selection.FrontierRevision
                    || funding.ReadoutFingerprint.Value != selection.ReadoutFingerprint
                    || funding.CandidateFingerprint.Value != selection.ReadoutCandidateFingerprint
                    || funding.CandidateOccurrenceDigest != _pendingPolicyDecision.Readout.CandidateOccurrenceDigest
                    || funding.CanonicalState != _pendingPolicyDecision.ReadoutContext.CanonicalState
                    || funding.Authority.FrontierAuthoritySHA256 != selection.FrontierAuthoritySHA256
                    || funding.Authority.SelectionOrdinal != selection.SelectionOrdinal))
                return false;
            return selectionEventID.Value > selection.DecisionEventID.Value;
        }

        private static void ValidateMountedTaskChains(Tape tape, RepositoryLoopClosureTaskSpec task)
        {
            Dictionary<TapeEventID, RepositoryLoopTaskActionReceipt> actions = new();
            Dictionary<TapeEventID, RepositoryLoopTaskOccurrenceCheckReceipt> occurrenceChecks = new();
            Dictionary<TapeEventID, RepositoryLoopTaskOutcomeReceipt> outcomes = new();
            foreach (TapeEventView view in tape.GetEventViews())
            {
                if (!tape.Resolve(view.Id, out byte[] payload)) continue;
                if (view.Source == "repository-action" && payload.AsSpan().StartsWith("repository-loop-action-v1"u8))
                {
                    if (view.Provenance != Provenances.Execution || view.Roles != TapeEventRoles.AuditOnly)
                        throw new InvalidDataException("native repository task action event roles are malformed");
                    if (!RepositoryLoopTaskActionReceipt.TryDecode(payload, out RepositoryLoopTaskActionReceipt action))
                        throw new InvalidDataException("native repository task action packet is malformed");
                    if (action.TaskID == task.TaskID)
                    {
                        if (action.TaskAuthoritySHA256 != task.AuthoritySHA256)
                            throw new InvalidDataException("native repository task action authority changed");
                        actions[view.Id] = action;
                    }
                }
                // Frozen tape source and packet prefix; identifier-side name is OccurrenceCheck.
                else if (view.Source == "repository-verification" && payload.AsSpan().StartsWith("repository-loop-verification-v1"u8))
                {
                    if (view.Provenance != Provenances.Execution || view.Roles != TapeEventRoles.AuditOnly)
                        throw new InvalidDataException("native repository task occurrence check event roles are malformed");
                    if (!RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(payload, out RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheck))
                        throw new InvalidDataException("native repository task occurrence check packet is malformed");
                    if (occurrenceCheck.TaskID == task.TaskID)
                    {
                        if (occurrenceCheck.TaskAuthoritySHA256 != task.AuthoritySHA256)
                            throw new InvalidDataException("native repository task occurrence check authority changed");
                        occurrenceChecks[view.Id] = occurrenceCheck;
                    }
                }
                else if (view.Source == "repository-outcome" && payload.AsSpan().StartsWith("repository-loop-outcome-v1"u8))
                {
                    if (view.Provenance != Provenances.Execution || view.Roles != TapeEventRoles.AuditOnly)
                        throw new InvalidDataException("native repository task outcome event roles are malformed");
                    if (!RepositoryLoopTaskOutcomeReceipt.TryDecode(payload, out RepositoryLoopTaskOutcomeReceipt outcome))
                        throw new InvalidDataException("native repository task outcome packet is malformed");
                    if (outcome.TaskID == task.TaskID)
                    {
                        if (outcome.TaskAuthoritySHA256 != task.AuthoritySHA256)
                            throw new InvalidDataException("native repository task outcome authority changed");
                        outcomes[view.Id] = outcome;
                    }
                }
            }
            foreach ((TapeEventID actionEventID, RepositoryLoopTaskActionReceipt action) in actions)
            {
                if (action.SelectionEventID.Value >= actionEventID.Value)
                    throw new InvalidDataException("native repository task selection is not before action");
                bool hasOccurrenceCheck = occurrenceChecks.Values.Any(occurrenceCheck => occurrenceCheck.ActionEventID == actionEventID);
                if (!hasOccurrenceCheck)
                    throw new InvalidDataException("native repository task chain has an action without occurrence check");
                if (!tape.TryGetEventView(action.SelectionEventID, out TapeEventView selectionView)
                    || selectionView.Source != "repository-selection"
                    || selectionView.Provenance != Provenances.Execution
                    || selectionView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                    || !tape.Resolve(action.SelectionEventID, out byte[] selectionPayload)
                    || !RepositorySelectionReceipt.TryDecode(selectionPayload, out RepositorySelectionReceipt selection)
                    || selection.ReceiptSHA256 != action.SelectionReceiptSHA256
                    || selection.CandidateDigest != action.CandidateDigest
                    || selection.CandidateCanonical != action.CandidateCanonical
                    || selection.SelectionOrdinal != action.SelectionOrdinal
                    || selection.FrontierRevision != action.FrontierRevision
                    || selection.FrontierAuthoritySHA256 != action.FrontierAuthoritySHA256)
                    throw new InvalidDataException("native repository task action selection predecessor diverges");
            }
            if (occurrenceChecks.Values.GroupBy(static occurrenceCheck => occurrenceCheck.ActionEventID).Any(static group => group.Count() != 1)
                || outcomes.Values.GroupBy(static outcome => outcome.OccurrenceCheckEventID).Any(static group => group.Count() != 1))
                throw new InvalidDataException("native repository task predecessor is not one-to-one");
            foreach ((TapeEventID occurrenceCheckEventID, RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheck) in occurrenceChecks)
            {
                if (occurrenceCheck.ActionEventID.Value >= occurrenceCheckEventID.Value)
                    throw new InvalidDataException("native repository task action is not before occurrence check");
                if (!actions.TryGetValue(occurrenceCheck.ActionEventID, out RepositoryLoopTaskActionReceipt action))
                    throw new InvalidDataException("native repository task occurrence check has no action predecessor");
                if (!outcomes.Values.Any(outcome => outcome.OccurrenceCheckEventID == occurrenceCheckEventID))
                    throw new InvalidDataException("native repository task chain has occurrence check without outcome");
                if (action.CallSHA256 != occurrenceCheck.CallSHA256)
                    throw new InvalidDataException("native repository task occurrence check call diverges from action");
            }
            foreach ((TapeEventID outcomeEventID, RepositoryLoopTaskOutcomeReceipt outcome) in outcomes)
            {
                if (outcome.OccurrenceCheckEventID.Value >= outcomeEventID.Value)
                    throw new InvalidDataException("native repository task occurrence check is not before outcome");
                if (!occurrenceChecks.TryGetValue(outcome.OccurrenceCheckEventID, out RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheck)
                    || occurrenceCheck.Outcome != outcome.VerifierOutcome
                    || !actions.TryGetValue(occurrenceCheck.ActionEventID, out RepositoryLoopTaskActionReceipt action)
                    || action.CandidateDigest != outcome.CandidateDigest
                    || action.CandidateCanonical != outcome.CandidateCanonical)
                    throw new InvalidDataException("native repository task outcome chain diverges");
                if (!tape.TryGetEventView(occurrenceCheck.ActionEventID, out TapeEventView actionView)
                    || actionView.Source != "repository-action"
                    || !tape.Resolve(occurrenceCheck.ActionEventID, out byte[] actionPayload)
                    || Convert.ToHexStringLower(SHA256.HashData(actionPayload)) != occurrenceCheck.ActionPayloadSHA256
                    || !tape.TryGetEventView(outcome.OccurrenceCheckEventID, out TapeEventView occurrenceCheckView)
                    // Frozen tape source token; identifier-side name is OccurrenceCheck.
                    || occurrenceCheckView.Source != "repository-verification"
                    || !tape.Resolve(outcome.OccurrenceCheckEventID, out byte[] occurrenceCheckPayload)
                    || Convert.ToHexStringLower(SHA256.HashData(occurrenceCheckPayload)) != outcome.OccurrenceCheckPayloadSHA256)
                    throw new InvalidDataException("native repository task chain payload custody diverges");
            }
            TapeEventID[] seals = tape.GetEventViews()
                .Where(static view => view.Source == "repository-seal")
                .Select(static view => view.Id)
                .ToArray();
            if (seals.Length > 1)
                throw new InvalidDataException("native repository task tape has repeated seals");
            if (seals.Length == 1 && actions.Keys.Concat(occurrenceChecks.Keys).Concat(outcomes.Keys)
                    .Any(eventID => eventID.Value >= seals[0].Value))
                throw new InvalidDataException("native repository task chain crosses the terminal seal");
        }

        private readonly record struct TaskEvidence(
            string SourcePath,
            long SourceBytes,
            string SourceSHA256,
            int SourceLine,
            RepositoryLoopClosureResultSpecies ResultSpecies,
            byte[] ResultContent,
            RepositoryPrediction? Prediction,
            RepositoryOccurrenceCheckReceipt? TypedPredictionReceipt,
            RepositoryOccurrenceCheckOutcomes Outcome,
            long EvaluatorCost,
            long AccessCost,
            long AccessSequence,
            string AccessEntrySHA256);

        private bool TryBuildTaskEvidence(
            RepositoryLoopClosureTaskSpec task,
            RepositoryCandidate candidate,
            Tool.Observation observation,
            string callSHA,
            out TaskEvidence evidence)
        {
            evidence = default;
            if (!RepositoryLoopTaskSpeciesRules.MatchesCandidate(task.Species, candidate.Species))
                return false;
            RepositoryAccessEntry? access = FindTaskAccess(candidate, callSHA, observation);
            RepositoryAccessEntry accessEntry = access.GetValueOrDefault();
            bool hasAccess = access is { };
            if (hasAccess && task.Species != RepositoryLoopClosureTaskSpecies.Diagnosis
                && (accessEntry.Verb != candidate.Verb || accessEntry.Argument != candidate.Argument))
                hasAccess = false;
            string sourcePath = ""; int sourceLine = 0;
            RepositoryLoopClosureResultSpecies resultSpecies = task.Species switch
            {
                RepositoryLoopClosureTaskSpecies.Locate => RepositoryLoopClosureResultSpecies.Path,
                RepositoryLoopClosureTaskSpecies.Trace => RepositoryLoopClosureResultSpecies.Trace,
                RepositoryLoopClosureTaskSpecies.Read => RepositoryLoopClosureResultSpecies.Text,
                RepositoryLoopClosureTaskSpecies.Answer => RepositoryLoopClosureResultSpecies.Answer,
                RepositoryLoopClosureTaskSpecies.Diagnosis => RepositoryLoopClosureResultSpecies.Diagnosis,
                _ => throw new InvalidDataException("repository task species is malformed"),
            };
            byte[] resultContent = Array.Empty<byte>();
            long sourceBytes = 0; string sourceSHA256 = "";
            bool answerObserved = task.Species == RepositoryLoopClosureTaskSpecies.Answer
                && observation.Answered && observation.AnswerPath.Length > 0;
            bool hasObservedResult = hasAccess || answerObserved;
            if (hasObservedResult)
            {
                if (!TryResolveTaskResult(task.Species, candidate, observation,
                        out sourcePath, out sourceLine, out resultSpecies, out resultContent))
                {
                    hasObservedResult = false;
                    hasAccess = false;
                    sourcePath = ""; sourceLine = 0; resultContent = Array.Empty<byte>();
                }
                else
                {
                    Tool.RepositoryWorldFileSnapshot source = World.CaptureFile(sourcePath);
                    source.Validate();
                    sourceBytes = source.Bytes; sourceSHA256 = source.SHA256;
                }
            }
            RepositoryPrediction? prediction = candidate is RepositoryCandidate.VerifyPredictionCandidate verify ? verify.Prediction.Prediction : null;
            RepositoryOccurrenceCheckReceipt? typedPredictionReceipt = null;
            RepositoryOccurrenceCheckOutcomes outcome = RepositoryOccurrenceCheckOutcomes.Unobserved;
            long evaluatorCost = task.Species == RepositoryLoopClosureTaskSpecies.Answer ? 0 : observation.OccurrenceCheck?.EvaluatorCost ?? 0;
            long accessCost = task.Species == RepositoryLoopClosureTaskSpecies.Answer ? 0 : observation.OccurrenceCheck?.AccessCost ?? 0;
            long accessSequence = hasAccess ? accessEntry.Sequence : -1;
            string accessEntrySHA = hasAccess ? accessEntry.EntrySHA256 : "";
            if (task.Species == RepositoryLoopClosureTaskSpecies.Diagnosis)
            {
                if (prediction is not { } actualPrediction || task.Oracle.Prediction is not { } expectedPrediction)
                    return false;
                typedPredictionReceipt = _lastOccurrenceCheck;
                if (typedPredictionReceipt is { } typed && typed.CallSHA256 != callSHA)
                    typedPredictionReceipt = null;
                RepositoryOccurrenceCheckResult? generic = observation.OccurrenceCheck;
                if (generic is { Outcome: RepositoryOccurrenceCheckOutcomes.Unobserved })
                    typedPredictionReceipt = null;
                bool exact = hasAccess && generic is { Outcome: RepositoryOccurrenceCheckOutcomes.Confirmed }
                    && actualPrediction == expectedPrediction;
                outcome = !hasAccess || generic is { Outcome: RepositoryOccurrenceCheckOutcomes.Unobserved }
                    ? RepositoryOccurrenceCheckOutcomes.Unobserved
                    : exact ? RepositoryOccurrenceCheckOutcomes.Confirmed : RepositoryOccurrenceCheckOutcomes.Refuted;
                accessSequence = hasAccess ? generic?.AccessSequence ?? accessEntry.Sequence : -1;
                accessEntrySHA = hasAccess ? generic?.AccessEntrySHA256 ?? accessEntry.EntrySHA256 : "";
            }
            else if (hasObservedResult)
            {
                bool exact = sourcePath == task.Oracle.ExpectedSource.Path
                    && sourceBytes == task.Oracle.ExpectedSource.Bytes
                    && sourceSHA256 == task.Oracle.ExpectedSource.SHA256
                    && sourceLine == task.Oracle.ExpectedSourceLine
                    && resultSpecies == task.Oracle.ExpectedResult.Species
                    && resultContent.AsSpan().SequenceEqual(task.Oracle.ExpectedResult.Content.Span);
                outcome = exact ? RepositoryOccurrenceCheckOutcomes.Confirmed : RepositoryOccurrenceCheckOutcomes.Refuted;
            }
            evidence = new TaskEvidence(sourcePath, sourceBytes, sourceSHA256, sourceLine, resultSpecies,
                resultContent, prediction, typedPredictionReceipt, outcome, Math.Max(0, evaluatorCost), Math.Max(0, accessCost),
                accessSequence, accessEntrySHA);
            return true;
        }

        private RepositoryAccessEntry? FindTaskAccess(
            RepositoryCandidate candidate, string callSHA, Tool.Observation observation)
        {
            if (candidate is RepositoryCandidate.VerifyPredictionCandidate verify
                && observation.OccurrenceCheck is { AccessSequence: >= 0 } result)
                return _access.Entries.FirstOrDefault(entry => entry.Sequence == result.AccessSequence
                    && entry.EntrySHA256 == result.AccessEntrySHA256);
            for (int i = _access.Entries.Count - 1; i >= 0; i--)
            {
                RepositoryAccessEntry entry = _access.Entries[i];
                if (entry.CallSHA256 == callSHA) return entry;
            }
            return null;
        }

        private bool TryResolveTaskResult(
            RepositoryLoopClosureTaskSpecies species,
            RepositoryCandidate candidate,
            Tool.Observation observation,
            out string sourcePath,
            out int sourceLine,
            out RepositoryLoopClosureResultSpecies resultSpecies,
            out byte[] resultContent)
        {
            sourcePath = ""; sourceLine = 0; resultSpecies = default; resultContent = Array.Empty<byte>();
            switch (species)
            {
                case RepositoryLoopClosureTaskSpecies.Locate:
                    sourcePath = observation.HitPaths.FirstOrDefault().Value;
                    resultSpecies = RepositoryLoopClosureResultSpecies.Path;
                    resultContent = Encoding.UTF8.GetBytes(sourcePath);
                    break;
                case RepositoryLoopClosureTaskSpecies.Trace:
                case RepositoryLoopClosureTaskSpecies.Read:
                    if (observation.Loci.Count == 0) return false;
                    Tool.RepositoryLocus locus = observation.Loci[0];
                    sourcePath = locus.Path.Value; sourceLine = locus.Line;
                    resultSpecies = species == RepositoryLoopClosureTaskSpecies.Trace
                        ? RepositoryLoopClosureResultSpecies.Trace : RepositoryLoopClosureResultSpecies.Text;
                    resultContent = GetWorldLineBytes(sourcePath, sourceLine);
                    break;
                case RepositoryLoopClosureTaskSpecies.Answer:
                    sourcePath = observation.AnswerPath.Value;
                    resultSpecies = RepositoryLoopClosureResultSpecies.Answer;
                    resultContent = Encoding.UTF8.GetBytes(sourcePath);
                    break;
                case RepositoryLoopClosureTaskSpecies.Diagnosis:
                    if (candidate is not RepositoryCandidate.VerifyPredictionCandidate verify) return false;
                    sourcePath = verify.Prediction.Prediction.Path; sourceLine = verify.Prediction.Prediction.Line;
                    resultSpecies = RepositoryLoopClosureResultSpecies.Diagnosis;
                    resultContent = Encoding.UTF8.GetBytes(verify.Prediction.Prediction.Canonical);
                    break;
                default: return false;
            }
            return !string.IsNullOrWhiteSpace(sourcePath) && resultContent.Length > 0
                && World.ContainsPath(sourcePath) && (sourceLine == 0 || World.ContainsLine(sourcePath, sourceLine, ""));
        }

        private byte[] GetWorldLineBytes(string path, int line)
        {
            Tool.RepositoryWorldFileSnapshot source = World.CaptureFile(path);
            RepositoryLoopClosureWorldFile authoritative = new(source.Path, source.Content);
            return authoritative.TryGetLineBytes(line, out ReadOnlyMemory<byte> lineBytes)
                ? lineBytes.ToArray() : Array.Empty<byte>();
        }

        private static bool HasConfirmedTaskOutcome(Tape tape, RepositoryLoopClosureTaskSpec task)
        {
            ValidateMountedTaskChains(tape, task);
            foreach (TapeEventView view in tape.GetEventViews())
            {
                if (view.Source != "repository-outcome" || !tape.Resolve(view.Id, out byte[] payload)
                    || !payload.AsSpan().StartsWith("repository-loop-outcome-v1"u8)
                    || !RepositoryLoopTaskOutcomeReceipt.TryDecode(payload, out RepositoryLoopTaskOutcomeReceipt outcome)) continue;
                if (outcome.TaskID == task.TaskID && outcome.TaskAuthoritySHA256 == task.AuthoritySHA256
                    && outcome.VerifierOutcome == RepositoryOccurrenceCheckOutcomes.Confirmed)
                    return true;
            }
            return false;
        }

        public void Observe(int step)
        {
            if (_lastCommittedStep == step) return;
            throw new InvalidDataException("native repository reward observed an uncommitted action");
        }

        private void AppendPaidOutcomeAndEvidence(
            Cortex cortex,
            CortexAction action,
            RepositoryCandidateProposal proposal,
            in CortexPolicyDecision decision,
            in RepositoryFundingReceipt fundingReceipt,
            in CortexPolicyTrialCompletion settlement,
            TapeEventID settlementEventID,
            byte[] settlementPayload,
            byte[] executionBytes,
            Tool.Observation observation,
            List<TapeEventID> eventIDs)
        {
            if (eventIDs.Count == 0 || !cortex.Tape.TryGetEventView(eventIDs[0], out TapeEventView actionView)
                || actionView.Source != "repository-action" || actionView.Provenance != Provenances.Execution
                || actionView.Roles != TapeEventRoles.AuditOnly || !cortex.Tape.Resolve(eventIDs[0], out byte[] actionPayload)
                || !actionPayload.AsSpan().SequenceEqual(executionBytes))
                throw new InvalidDataException("repository action audit is missing before paid outcome");
            string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Tool.ToolCall.Create(proposal.Candidate.Verb, proposal.Candidate.Argument).Raw)));
            if (settlement.ActualExecutedArmSteps + settlement.ReclaimedOrUnused != fundingReceipt.PlannedArmSteps
                || settlement.QuotaDecisionID != fundingReceipt.QuotaDecisionID
                || !cortex.Tape.Resolve(settlementEventID, out byte[] exactSettlementPayload)
                || !exactSettlementPayload.AsSpan().SequenceEqual(settlementPayload))
                throw new InvalidDataException("repository paid outcome settlement identity diverged");
            string outcomePayloadSHA = proposal.Candidate is RepositoryCandidate.AnswerPathCandidate
                ? Convert.ToHexStringLower(SHA256.HashData(executionBytes))
                : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(observation.Text)));
            RepositoryAccessEntry? accessEntry = proposal.Candidate is not RepositoryCandidate.AnswerPathCandidate
                && TryResolveAdmissionAccessEntry(callSHA, out RepositoryAccessEntry rendered) ? rendered : null;
            long accessSequence = accessEntry?.Sequence ?? -1;
            string accessEntrySHA = accessEntry?.EntrySHA256 ?? "";
            RepositoryReceiptAuthority outcomeAuthority = CreateReceiptAuthority(
                cortex, "repository-outcome", decision, fundingReceipt, settlementEventID,
                Convert.ToHexStringLower(SHA256.HashData(settlementPayload)), accessSequence, accessEntrySHA);
            ValidateCanonicalStateCorroboration(in fundingReceipt);
            ValidateVerifiedScope(cortex, fundingReceipt.CanonicalState, fundingReceipt.ReadoutFingerprint,
                fundingReceipt.CandidateFingerprint, fundingReceipt.CandidateOccurrenceDigest,
                fundingReceipt.ReadoutRevision);
            TapeEventID expectedOutcomeEventID = new(cortex.Tape.NextId);
            RepositoryPaidOutcomeReceipt outcome = RepositoryPaidOutcomeReceipt.Create(
                cortex.Step, Policy.ID, decision.DecisionID, fundingReceipt.DecisionEventID, fundingReceipt.QuotaDecisionID,
                fundingReceipt.ReadoutFingerprint, fundingReceipt.CandidateFingerprint, fundingReceipt.CandidateOccurrenceDigest,
                fundingReceipt.ReadoutRevision, fundingReceipt.CanonicalState, proposal.CandidateDigest, proposal.Candidate.Canonical,
                fundingReceipt.FrontierRevision, World.WorldSHA256, _access.AccessSHA256, callSHA,
                fundingReceipt.PlannedArmSteps, settlement.ActualExecutedArmSteps, settlement.ReclaimedOrUnused,
                settlement.EvaluatorWorkUnits, settlement.VerifierOutcome, settlement.WallMilliseconds,
                expectedOutcomeEventID,
                proposal.Candidate is RepositoryCandidate.AnswerPathCandidate
                    ? RepositoryOutcomePayloadSources.ActionExecution
                    : RepositoryOutcomePayloadSources.WorldObservation,
                outcomePayloadSHA, eventIDs[0],
                new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(actionPayload))), outcomeAuthority).BindEventPayloadSHA();
            TapeEventID outcomeEventID = TapePacketCreator.AppendRepositoryOutcomeReceipt(cortex.Tape, cortex.Journal, cortex.Step, in outcome);
            if (outcomeEventID != expectedOutcomeEventID
                || !cortex.Tape.Resolve(outcomeEventID, out byte[] outcomePayload)
                || !cortex.Tape.TryGetEventView(outcomeEventID, out TapeEventView outcomeView)
                || outcomeView.Source != "repository-outcome" || outcomeView.Provenance != Provenances.Execution
                || outcomeView.Roles != TapeEventRoles.AuditOnly
                || Convert.ToHexStringLower(SHA256.HashData(outcomePayload)) != outcome.EventPayloadSHA256)
                throw new InvalidDataException("repository paid outcome packet identity diverged");
            _outcomeReceipts.Add(outcome);
            eventIDs.Add(outcomeEventID);

            if (proposal.Candidate is RepositoryCandidate.AnswerPathCandidate) return;
            if (!_hasPendingAdmissionPlan || _pendingObservationEventID.Value <= 0)
                throw new InvalidDataException("repository paid evidence has no world admission");
            TapeEventID admissionPlanEventID = new(_pendingObservationEventID.Value - 1);
            if (admissionPlanEventID.Value >= outcomeEventID.Value)
                throw new InvalidDataException("repository paid evidence admission predates outcome");
            Tool.ToolCall worldCall = Tool.ToolCall.Create(proposal.Candidate.Verb, proposal.Candidate.Argument);
            (Tool.RepositoryPath path, int line) = ResolveAdmissionSource(proposal.Candidate, worldCall);
            if (line < 1) line = 1;
            if (!cortex.Tape.Resolve(admissionPlanEventID, out byte[] admissionPlanPayload)
                || !TapePacketCreator.TryReadRepositoryWorldEncounter(admissionPlanPayload, out RepositoryAdmissionReceipt admissionPlan)
                || !cortex.Tape.TryGetEventView(admissionPlanEventID, out TapeEventView admissionPlanView)
                // Frozen tape source token repository:encounter; identifier-side name is AdmissionPlan.
                || admissionPlanView.Source != "repository:encounter"
                || admissionPlanView.Provenance != Provenances.Execution
                || admissionPlanView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !cortex.Tape.TryGetEventView(_pendingObservationEventID, out TapeEventView worldView)
                || worldView.Source != "repository:world"
                || worldView.Provenance != Provenances.Real
                || !CarriesWorldObservationRoles(worldView.Roles)
                || !cortex.Tape.Resolve(_pendingObservationEventID, out byte[] worldPayload)
                || !worldPayload.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(observation.Text))
                || admissionPlan.ObservationEventID != _pendingObservationEventID
                || admissionPlan.SourcePath != path.Value
                || admissionPlan.SourceLine != line
                || admissionPlan.CallSHA256 != callSHA
                || admissionPlan.WorldSHA256 != World.WorldSHA256
                || admissionPlan.AccessSHA256 != _access.AccessSHA256)
                throw new InvalidDataException("repository paid evidence admission audit diverged");
            // This path is reached only with an admissionPlan in hand, so the entry that built it must
            // still be there — a barren look never mints an admissionPlan to be paid against.
            if (!TryResolveAdmissionAccessEntry(callSHA, out RepositoryAccessEntry evidenceAccess))
                throw new InvalidDataException("repository paid evidence has no admitted access entry");
            string evidenceSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(observation.Text)));
            if (evidenceSHA != admissionPlan.EvidenceSHA256 || evidenceAccess.Sequence != admissionPlan.AccessSequence
                || evidenceAccess.EntrySHA256 != admissionPlan.AccessEntrySHA256)
                throw new InvalidDataException("repository paid evidence bytes/access diverged");
            TapeEventID expectedEvidenceEventID = new(cortex.Tape.NextId);
            RepositoryReceiptAuthority evidenceAuthority = CreateReceiptAuthority(
                cortex, "repository-evidence", decision, fundingReceipt, settlementEventID,
                Convert.ToHexStringLower(SHA256.HashData(settlementPayload)), evidenceAccess.Sequence, evidenceAccess.EntrySHA256);
            ValidateCanonicalStateCorroboration(in fundingReceipt);
            ValidateVerifiedScope(cortex, fundingReceipt.CanonicalState, fundingReceipt.ReadoutFingerprint,
                fundingReceipt.CandidateFingerprint, fundingReceipt.CandidateOccurrenceDigest,
                fundingReceipt.ReadoutRevision);
            RepositoryNewEvidenceReceipt evidence = RepositoryNewEvidenceReceipt.Create(
                cortex.Step, Policy.ID, decision.DecisionID, fundingReceipt.DecisionEventID, fundingReceipt.QuotaDecisionID,
                fundingReceipt.ReadoutFingerprint, fundingReceipt.CandidateFingerprint, fundingReceipt.CandidateOccurrenceDigest,
                fundingReceipt.ReadoutRevision, fundingReceipt.CanonicalState, proposal.CandidateDigest, proposal.Candidate.Canonical,
                fundingReceipt.FrontierRevision, World.WorldSHA256, _access.AccessSHA256, callSHA,
                new Tool.RepositoryLocus(path, line), _pendingObservationEventID, outcomeEventID, evidenceSHA,
                outcomeEventID, new LoopClosureDigest(outcome.ReceiptSHA256), evidenceAuthority);
            evidence = evidence with { Authority = evidence.Authority with { EventID = expectedEvidenceEventID } };
            evidence = evidence with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(evidence.Kind, evidence.Canonical) };
            evidence = evidence.BindEventPayloadSHA();
            TapeEventID evidenceEventID = TapePacketCreator.AppendRepositoryEvidenceReceipt(cortex.Tape, cortex.Journal, cortex.Step, in evidence);
            if (evidenceEventID != expectedEvidenceEventID
                || !cortex.Tape.Resolve(evidenceEventID, out byte[] evidencePayload)
                || !cortex.Tape.TryGetEventView(evidenceEventID, out TapeEventView evidenceView)
                || evidenceView.Source != "repository-evidence" || evidenceView.Provenance != Provenances.Execution
                || evidenceView.Roles != TapeEventRoles.AuditOnly
                || Convert.ToHexStringLower(SHA256.HashData(evidencePayload)) != evidence.EventPayloadSHA256)
                throw new InvalidDataException("repository paid evidence packet identity diverged");
            _evidenceReceipts.Add(evidence);
            eventIDs.Add(evidenceEventID);
        }

        private RepositoryReceiptAuthority CreateReceiptAuthority(
            Cortex cortex,
            string kind,
            in CortexPolicyDecision decision,
            in RepositoryFundingReceipt fundingReceipt,
            TapeEventID settlementEventID,
            string settlementPayloadSHA,
            long accessSequence,
            string accessEntrySHA)
        {
            return new RepositoryReceiptAuthority(
                new TapeEventID(cortex.Tape.NextId),
                new LoopLineageNodeID($"{kind}-{fundingReceipt.QuotaDecisionID.Value:X16}"),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{fundingReceipt.QuotaDecisionID.Value:X16}"))),
                fundingReceipt.DecisionPayloadSHA256, fundingReceipt.ReadoutEventID, fundingReceipt.ReadoutPayloadSHA256,
                fundingReceipt.FundingEventID, fundingReceipt.FundingPayloadSHA256, default, "",
                settlementEventID, settlementPayloadSHA, fundingReceipt.Authority.FrontierAuthoritySHA256,
                fundingReceipt.Authority.FrontierRevision, fundingReceipt.Authority.SelectionOrdinal, fundingReceipt.Authority.CandidateSpecies)
            {
                AccessSequence = accessSequence,
                AccessEntrySHA256 = accessEntrySHA,
                AccessEntryCount = _access.Count,
            };
        }

        public bool SettleInstallRevision(
            in InstallRevision publication,
            IReadOnlyList<TapeEventID> foldedAppends,
            Func<TapeEventID, bool> foldedPredicate,
            LoopLineageTurnstile? lineage,
            Tape tape,
            Journal journal,
            int step)
        {
            EnsureTerminalTransitionOpen();
            return _pattern.SettleInstallRevision(in publication, foldedAppends, foldedPredicate, tape, journal);
        }

        public Tool.Observation Verify(string raw, string callSHA256, out RepositoryPrediction prediction)
        {
            EnsureTerminalTransitionOpen();
            if (!RepositoryPrediction.TryParse(raw, out prediction))
                return new Tool.Observation($"[verify: malformed prediction '{raw}']\n", Array.Empty<Tool.RepositoryPath>(), false, "");
            return Verify(prediction, callSHA256);
        }

        public Tool.Observation Verify(RepositoryPrediction prediction, string callSHA256)
        {
            EnsureTerminalTransitionOpen();
            prediction.Validate();
            RepositoryOccurrenceCheckResult result = _access.Evaluate(World, prediction);
            _pendingOccurrenceCheck = new PendingOccurrenceCheck(prediction, result, callSHA256);
            return new Tool.Observation($"[verify: {prediction.Canonical} => {result.Outcome}; evaluator={result.EvaluatorCost}; access={result.AccessCost}]\n",
                Array.Empty<Tool.RepositoryPath>(), false, "", null, null, result);
        }

        public void AppendOccurrenceCheckReceipt(Cortex cortex, CortexAction action, List<TapeEventID> eventIDs)
        {
            EnsureTerminalTransitionOpen();
            if (_pendingOccurrenceCheck is not { } pending || eventIDs.Count == 0) return;
            string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(action.Raw)));
            if (_pendingProposal is not { Candidate.Species: RepositoryCandidateSpecies.VerifyPrediction }
                || !string.Equals(callSHA, pending.CallSHA256, StringComparison.Ordinal))
                throw new InvalidDataException("repository occurrence check receipt does not match its admitted call");
            // COUNT AND ROOT MUST BE ONE INSTANT. The receipt's AccessEntryCount is fixed when the
            // prediction is EVALUATED; the root must therefore be the prefix root at that same count, not
            // the journal's current root. They only agreed while a verify's own access went
            // unrecorded — once it lands (the journal records accesses, not findings) the current
            // root is one entry ahead of the count the receipt carries, and the receipt attests to a
            // journal state that never existed.
            RepositoryOccurrenceCheckReceipt receipt = RepositoryOccurrenceCheckReceipt.Create(cortex.Step, pending.Prediction,
                pending.Result, World.WorldSHA256,
                _access.ComputeAccessSHA256AfterDelta(pending.Result.AccessEntryCount, []),
                _pendingObservationEventID, pending.CallSHA256,
                pending.Result.AccessSequence, pending.Result.AccessEntrySHA256);
            TapeEventID receiptID = TapePacketCreator.AppendRepositoryOccurrenceCheckReceipt(cortex.Tape, cortex.Journal, cortex.Step, receipt);
            eventIDs.Add(receiptID);
            _lastOccurrenceCheck = receipt;
            _occurrenceCheckReceipts.Add(receipt);
            if (!_occurrenceCheckReceiptIDs.Add(receipt.ReceiptSHA256))
                throw new InvalidDataException("repository occurrence check receipt identity was reused");
            if (receipt.Outcome == RepositoryOccurrenceCheckOutcomes.Confirmed)
            {
                if (_pendingProposal is not { } proposal
                    || !_frontier.TryGetSourceEventID(in proposal, out TapeEventID sourceEventID))
                    throw new InvalidDataException("confirmed repository occurrence check has no candidate source event");
                RepositoryConfirmedPredictionReceipt verifiedPrediction = RepositoryConfirmedPredictionReceipt.Create(
                    cortex.Step, receipt.Prediction, pending.Result, receipt.WorldSHA256, receipt.AccessSHA256,
                    receipt.CallSHA256, receipt.PredecessorEventID);
                TapeEventID verifiedPredictionEventID = TapePacketCreator.AppendRepositoryLineageReceipt(
                    cortex.Tape, cortex.Journal, cortex.Step, verifiedPrediction);
                eventIDs.Add(verifiedPredictionEventID);
                RepositoryConfirmedOccurrenceReceipt confirmedOccurrence = RepositoryConfirmedOccurrenceReceipt.Create(
                    cortex.Step, verifiedPrediction.Prediction.SHA256, verifiedPrediction.EvidenceSHA256, receipt.ReceiptSHA256,
                    verifiedPredictionEventID);
            TapeEventID occurrenceReceiptEventID = TapePacketCreator.AppendRepositoryLineageReceipt(
                    cortex.Tape, cortex.Journal, cortex.Step, confirmedOccurrence);
                eventIDs.Add(occurrenceReceiptEventID);
                EmitRepositoryOccurrenceCheckLineage(cortex, _pendingObservationEventID, verifiedPredictionEventID, occurrenceReceiptEventID);
                _pendingPatternOccurrence = new PendingPatternOccurrence(receipt, sourceEventID, receiptID,
                    verifiedPredictionEventID, occurrenceReceiptEventID);
            }
            _pendingOccurrenceCheck = null;
        }

        private void EmitRepositoryOccurrenceCheckLineage(
            Cortex cortex,
            TapeEventID sourceEventID,
            TapeEventID verifiedPredictionEventID,
            TapeEventID occurrenceReceiptEventID)
        {
            if (cortex.LoopLineage is not LoopLineageTurnstile lineage) return;
            GrammarRevisionID revision = ResolveCurrentGrammarRevision(cortex);
            if (!lineage.EnsureWorldOpportunities(cortex.Step, verifiedPredictionEventID, [sourceEventID],
                    out IReadOnlyList<LoopLineageNode> worldNodes, revision)
                || worldNodes.Count == 0)
                throw new InvalidDataException($"repository confirmed prediction s{verifiedPredictionEventID.Value} names an invalid world opportunity — {lineage.LastRefusal}");
            LoopLineageNodeID[] worldPredecessors = worldNodes.Select(static node => node.NodeID)
                .Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray();
            LoopLineageCausalID lawCausal = LoopLineageCausalID.Merge(
                LoopLineageNodeSpecies.VerifiedLaw, worldPredecessors);
            if (!lineage.TryEmit(cortex.Step, LoopLineageNodeSpecies.VerifiedLaw, verifiedPredictionEventID,
                    revision, worldPredecessors, lawCausal)
                || !lineage.TryGetNodeForEvent(verifiedPredictionEventID, out LoopLineageNode lawNode))
                throw new InvalidDataException("repository confirmed prediction lineage emission did not close");
            LoopLineageNodeID[] occurrencePredecessors = worldPredecessors.Append(lawNode.NodeID)
                .Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray();
            LoopLineageCausalID occurrenceCausal = LoopLineageCausalID.Merge(
                LoopLineageNodeSpecies.VerifiedLawSupport, occurrencePredecessors);
            if (!lineage.TryEmit(cortex.Step, LoopLineageNodeSpecies.VerifiedLawSupport, occurrenceReceiptEventID,
                    revision, occurrencePredecessors, occurrenceCausal))
                throw new InvalidDataException("repository confirmed occurrence lineage emission did not close");
        }

        private void EmitRepositoryCompositionLineage(
            Cortex cortex,
            PendingPatternOccurrence pendingOccurrence,
            TapeEventID compositionEventID)
        {
            if (cortex.LoopLineage is not LoopLineageTurnstile lineage) return;
            GrammarRevisionID revision = ResolveCurrentGrammarRevision(cortex);
            if (!lineage.TryGetNodeForEvent(pendingOccurrence.VerifiedPredictionReceiptEventID, out LoopLineageNode lawNode)
                || lawNode.Species != LoopLineageNodeSpecies.VerifiedLaw)
                throw new InvalidDataException("repository composition has no verified-law predecessor");
            if (!lineage.TryEmit(cortex.Step, LoopLineageNodeSpecies.Rung0Composition, compositionEventID,
                    revision, [lawNode.NodeID], lawNode.CausalID))
                throw new InvalidDataException("repository composed candidate lineage emission did not close");
            cortex.RegisterLoopClosureComposition(lineage.Receipts[^1]);
        }

        private void AppendRepositoryAdmissionEvents(
            Cortex cortex,
            RepositoryPatternGrammarAdmissionReceipt admission,
            TapeEventID compositionEventID,
            List<TapeEventID> eventIDs)
        {
            if (admission.EconomicsEventID is not TapeEventID economicsEvent) return;
            eventIDs.Add(economicsEvent);
            if (admission.ReflectedTapeEventID is TapeEventID reflectionEvent)
                eventIDs.Add(reflectionEvent);
            if (cortex.LoopLineage is not LoopLineageTurnstile lineage) return;
            if (!lineage.TryGetNodeForEvent(economicsEvent, out _))
            {
                if (!lineage.TryGetNodeForEvent(compositionEventID, out LoopLineageNode compositionNode)
                    || compositionNode.Species != LoopLineageNodeSpecies.Rung0Composition)
                    throw new InvalidDataException("repository admission has no exact composed-candidate lineage predecessor");
                if (!lineage.TryEmit(cortex.Step, LoopLineageNodeSpecies.DisplacedEvaluation, economicsEvent,
                        ResolveCurrentGrammarRevision(cortex), [compositionNode.NodeID], compositionNode.CausalID))
                    throw new InvalidDataException("repository admission economics lineage emission did not close");
            }
            else if (!lineage.TryGetNodeForEvent(economicsEvent, out LoopLineageNode economicsNode)
                || economicsNode.Species != LoopLineageNodeSpecies.DisplacedEvaluation)
                throw new InvalidDataException("repository admission economics event has an incompatible lineage species");
        }

        private static GrammarRevisionID ResolveCurrentGrammarRevision(Cortex cortex)
            => cortex.InstallRevision?.Revision
                ?? throw new InvalidDataException("repository lineage emission requires the current grammar revision");

        public void AppendRepositoryAdmissionPlan(Cortex cortex, CortexAction action, List<TapeEventID> eventIDs)
        {
            EnsureTerminalTransitionOpen();
            if (_pendingProposal is not { } proposal || !_hasPendingObservation || eventIDs.Count == 0)
                throw new InvalidDataException("repository admission has no current admitted proposal");
            if (_hasPendingAdmissionPlan) throw new InvalidDataException("repository admission was appended twice");
            Tool.ToolCall call = Tool.ToolCall.Create(proposal.Candidate.Verb, proposal.Candidate.Argument);
            string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Raw)));
            RecordAccess(cortex.Step, call, _pendingObservation, callSHA);
            if (proposal.Candidate is RepositoryCandidate.AnswerPathCandidate)
            {
                _hasPendingAdmissionPlan = true;
                return;
            }
            // A BARREN LOOK — a grep that hit nothing, or a look the arm's valve withheld — spent its
            // fuel and is recorded as an access attempt, but there is no world evidence to take
            // custody of: no bytes, no source path, no line, so no admissionPlan packet can name one.
            // It closes as a spent look rather than crashing the run, which is what lets the
            // tools-blocked arm exist at all (G3) and what a live grep with no hits already needed.
            if (!TryResolveAdmissionAccessEntry(callSHA, out RepositoryAccessEntry accessEntry))
            {
                _hasPendingAdmissionPlan = true;
                _pendingAdmissionPlanBarren = true;
                return;
            }
            // The call the TRANSITION will stamp is the one whose row is in the journal — a verify
            // rests on the entry its evidence came from, and that is a different call from the one
            // just made. Stamping the call that was made would have the frontier prediction contact the
            // access journal never recorded, which is exactly what the sealed-input check refuses.
            _pendingAdmissionPlanCallSHA = accessEntry.CallSHA256;
            (Tool.RepositoryPath path, int line) = ResolveAdmissionSource(proposal.Candidate, call);
            if (line < 1) line = 1;
            byte[] sourceBytes = Encoding.UTF8.GetBytes(_pendingObservation.Text);

            // THE TOOL-INTAKE SEAM (G1). The result of a look is custody unconditionally, but it
            // becomes grammar diet only if it pays: the same intake organ that governs the world
            // mouth measures the result against the standing cover, and only bytes the grammar
            // cannot already generate earn GrammarInput. This is what makes a re-look over known
            // territory free — it still lands as evidence, it just stops feeding.
            //
            // The decision is taken BEFORE the admissionPlan's identity is computed, and that ordering is
            // load-bearing: asking the policy plane is itself a tape-writing act (its decision and
            // readout are events), so a receipt minted first would name a cursor the decision has
            // already moved past and the append would refuse its own observation.
            Engine.GrammarCover? intakeCover = cortex.Grammar.Rules is { Length: > 0 } rules
                ? cortex.GrammarCover ?? new Engine.GrammarCover(rules)
                : null;
            CortexTapeAdmissionChoice intake = cortex.ChooseTapeAdmission(
                intakeCover, sourceBytes, sourceBytes.Length, Provenances.Real, cortex.Config.Curriculum.AffirmGate);
            bool admitToGrammar = intake.Action == CortexTapeAdmissionActions.Admit;

            TapeEventID expectedObservation = new(cortex.Tape.NextId + 1);
            string evidenceSHA = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
            RepositoryAdmissionReceipt receipt = RepositoryAdmissionReceipt.Create(
                cortex.Step, expectedObservation, World.WorldSHA256, _access.AccessSHA256, callSHA,
                path.Value, line, evidenceSHA, accessEntry.Sequence, accessEntry.EntrySHA256);
            TapeEventID observationEventID = TapePacketCreator.AppendRepositoryWorldEncounter(
                cortex.Tape, cortex.Journal, cortex.Step, receipt, sourceBytes, admitToGrammar);
            cortex.CompleteTapeAdmission(in intake, appended: admitToGrammar);
            if (observationEventID != expectedObservation)
                throw new InvalidDataException("repository admission observation identity diverged");
            _pendingObservationEventID = observationEventID;
            _hasPendingAdmissionPlan = true;
            eventIDs.Add(new TapeEventID(observationEventID.Value - 1));
            eventIDs.Add(observationEventID);
        }

        /// The evidence entry this admissionPlan is built on, or false when the look rendered none.
        /// The access journal records EVIDENCE — an entry carries the paths the result actually
        /// rendered — so a look that rendered nothing legitimately has no entry, and its absence is
        /// a barren look rather than a broken invariant. A non-empty journal whose newest entry
        /// belongs to a DIFFERENT call is still a defect and still throws.
        private bool TryResolveAdmissionAccessEntry(string callSHA, out RepositoryAccessEntry accessEntry)
        {
            if (_pendingOccurrenceCheck is { Result.AccessSequence: >= 0 } pending)
                foreach (RepositoryAccessEntry pendingEntry in _access.Entries)
                    if (pendingEntry.Sequence == pending.Result.AccessSequence
                        && pendingEntry.EntrySHA256 == pending.Result.AccessEntrySHA256)
                    {
                        accessEntry = pendingEntry;
                        return true;
                    }
            accessEntry = default;
            if (_access.Entries.Count == 0) return false;
            RepositoryAccessEntry entry = _access.Entries[^1];
            if (!string.Equals(entry.CallSHA256, callSHA, StringComparison.Ordinal)) return false;
            accessEntry = entry;
            return true;
        }

        private (Tool.RepositoryPath Path, int Line) ResolveAdmissionSource(RepositoryCandidate candidate, Tool.ToolCall call)
        {
            foreach (Tool.RepositoryLocus locus in _pendingObservation.Loci)
                return (locus.Path, locus.Line);
            foreach (Tool.RepositoryPath path in _pendingObservation.HitPaths)
                return (path, 0);
            if (candidate is RepositoryCandidate.VerifyPredictionCandidate verify)
                return (verify.Prediction.Prediction.Path, verify.Prediction.Prediction.Line);
            if (candidate is RepositoryCandidate.OpenPathCandidate open)
                return (open.Path.Path, 0);
            if (candidate is RepositoryCandidate.ReadLocusCandidate read)
                return (read.Locus.Locus.Path, read.Locus.Locus.Line);
            if (candidate is RepositoryCandidate.AnswerPathCandidate answer)
                return (answer.Path.Path, 0);
            if (!string.IsNullOrWhiteSpace(call.Arg)) return (new Tool.RepositoryPath(call.Arg), 0);
            throw new InvalidDataException("admitted repository observation has no typed source locus");
        }

        public void OnExecutionAdmission(Cortex cortex, CortexAction action, in CortexActionAdmissionDecision decision)
        {
            EnsureTerminalTransitionOpen();
            if (decision.Admitted)
                return;
            RefundPendingFunding(cortex);
            DiscardPendingExecution();
        }

        public void RecordAccess(int step, Tool.ToolCall call, Tool.Observation observation, string callSHA256)
        {
            EnsureTerminalTransitionOpen();
            // Answer is terminal and touches no world. A VERIFY does — it reads to decide — and its
            // access is recorded like any other, so a transition committed on a verify names a call
            // the journal actually holds.
            if (call.Verb == Tool.ToolVerbs.Answer) return;
            _access.Record(step, call, observation, callSHA256);
        }

        private static void WritePendingSelection(CkptWriter writer, in RepositorySelectionReceipt receipt)
        {
            receipt.Validate();
            writer.I32(receipt.Step);
            writer.Str(receipt.PolicyID.Value);
            writer.U64(receipt.DecisionID.Value);
            writer.I64(receipt.DecisionEventID.Value);
            writer.Str(receipt.DecisionPayloadSHA256);
            writer.U64(receipt.ReadoutFingerprint);
            writer.U64(receipt.ReadoutCandidateFingerprint);
            writer.U64(receipt.FrontierRevision.Value);
            writer.Str(receipt.FrontierAuthoritySHA256);
            writer.I32(receipt.SelectionOrdinal);
            writer.U8((byte)receipt.CandidateSpecies);
            writer.Str(receipt.CandidateCanonical);
            writer.U64(receipt.CandidateDigest.Value);
            writer.Str(receipt.ReceiptSHA256);
        }

        private static RepositorySelectionReceipt ReadPendingSelection(CkptReader reader)
        {
            RepositorySelectionReceipt receipt = new(
                reader.I32(),
                new CortexPolicyID(reader.Str()),
                new CortexPolicyDecisionID(reader.U64()),
                new TapeEventID(reader.I64()),
                reader.Str(),
                reader.U64(),
                reader.U64(),
                new RepositoryFrontierRevision(reader.U64()),
                reader.Str(),
                reader.I32(),
                (RepositoryCandidateSpecies)reader.U8(),
                reader.Str(),
                new RepositoryCandidateDigest(reader.U64()),
                reader.Str());
            receipt.Validate();
            return receipt;
        }

        public void SaveState(CkptWriter writer)
        {
            ValidatePendingFundingTuple();
            if (_hasPendingSelection
                && (_pendingFundingReceipt is null || _pendingProposal is null || !_hasPendingPolicyDecision))
                throw new InvalidDataException("native repository pending selection lacks its funding authority tuple");
            writer.Str(RootPath); writer.Str(World.Glob); writer.Str(World.WorldSHA256);
            writer.Str(QuerySHA256); writer.Str(Query);
            writer.I32(_look); writer.Bool(_answered);
            writer.I32(_observedPaths.Count);
            foreach (string path in _observedPaths) writer.Str(path);
            writer.Section(0x52414343); // RACC
            writer.U8(8); // access/readout/funding/outcome field dialect includes pending policy and selection authority
            _access.SaveState(writer);
            writer.I32(_occurrenceCheckReceipts.Count);
            foreach (RepositoryOccurrenceCheckReceipt receipt in _occurrenceCheckReceipts)
                WriteOccurrenceCheckState(writer, receipt);
            writer.Bool(_lastOccurrenceCheck is not null);
            if (_lastOccurrenceCheck is { } last) WriteOccurrenceCheckState(writer, last);
            writer.Bool(_lastReadout is not null);
            if (_lastReadout is { } readout) WriteReadoutState(writer, readout);
            writer.I32(_fundingReceipts.Count);
            foreach (RepositoryFundingReceipt receipt in _fundingReceipts)
                RepositoryLineageReceiptCheckpoint.Write(writer, in receipt);
            writer.I32(_outcomeReceipts.Count);
            foreach (RepositoryPaidOutcomeReceipt receipt in _outcomeReceipts)
                RepositoryLineageReceiptCheckpoint.Write(writer, in receipt);
            writer.I32(_evidenceReceipts.Count);
            foreach (RepositoryNewEvidenceReceipt receipt in _evidenceReceipts)
                RepositoryLineageReceiptCheckpoint.Write(writer, in receipt);
            writer.Bool(_pendingFundingReceipt is not null);
            if (_pendingFundingReceipt is { } pendingFunding)
                RepositoryLineageReceiptCheckpoint.Write(writer, in pendingFunding);
            writer.Bool(_pendingFundingReceipt is not null);
            if (_pendingFundingReceipt is not null)
            {
                RepositoryCandidateProposal pending = _pendingProposal!.Value;
                writer.U64(pending.Revision.Value); writer.U64(pending.CandidateDigest.Value); writer.Str(pending.Candidate.Canonical);
                CortexPolicyDecision pendingDecision = _pendingPolicyDecision;
                CortexPolicyDecisionCheckpoint.Write(writer, in pendingDecision);
                RepositoryLineageReceiptCheckpoint.WriteState(writer, pendingDecision.ReadoutContext.CanonicalState);
                writer.I32(pendingDecision.ReadoutContext.ActionCount); writer.I32(pendingDecision.ReadoutContext.DeliberationDepth);
            }
            writer.Bool(_hasPendingSelection);
            if (_hasPendingSelection)
            {
                writer.I64(_pendingSelectionEventID.Value);
                WritePendingSelection(writer, in _pendingSelectionReceipt);
            }
            _frontier.SaveState(writer);
            _pattern.SaveState(writer);
        }

        public void LoadState(CkptReader reader)
        {
            string root = reader.Str(); string glob = reader.Str(); string worldSHA = reader.Str();
            string querySHA = reader.Str(); string query = reader.Str();
            if (!string.Equals(root, RootPath, StringComparison.Ordinal) || !string.Equals(glob, World.Glob, StringComparison.Ordinal)
                || !string.Equals(worldSHA, World.WorldSHA256, StringComparison.Ordinal) || !string.Equals(querySHA, QuerySHA256, StringComparison.Ordinal)
                || !string.Equals(query, Query, StringComparison.Ordinal))
                throw new InvalidDataException("native repository checkpoint authority changed (root/glob/world/query)");
            _look = reader.I32(); _answered = reader.Bool();
            _observedPaths.Clear();
            _observedPathLog.Clear();
            for (int i = 0, n = reader.I32(); i < n; i++) _observedPaths.Add(reader.Str());
            _observedPathLog.AddRange(_observedPaths);
            reader.Expect(0x52414343);
            byte raccVersion = reader.U8();
            if (raccVersion is not (7 or 8)) throw new InvalidDataException("native repository RACC dialect is unsupported");
            RepositoryAccessJournal stagedAccess = RepositoryAccessJournal.ReadState(reader);
            List<RepositoryOccurrenceCheckReceipt> loadedOccurrenceCheckReceipts = new();
            HashSet<string> loadedOccurrenceCheckReceiptIDs = new(StringComparer.Ordinal);
            int receipts = reader.I32();
            if (receipts < 0 || receipts > 1_000_000) throw new InvalidDataException("native repository occurrence check count is malformed");
            for (int i = 0; i < receipts; i++)
            {
                RepositoryOccurrenceCheckReceipt receipt = ReadOccurrenceCheckState(reader);
                if (!string.Equals(receipt.WorldSHA256, World.WorldSHA256, StringComparison.Ordinal))
                    throw new InvalidDataException("native repository occurrence check world authority changed");
                ValidateOccurrenceCheckAccess(receipt, stagedAccess.Entries);
                loadedOccurrenceCheckReceipts.Add(receipt);
                if (!loadedOccurrenceCheckReceiptIDs.Add(receipt.ReceiptSHA256))
                    throw new InvalidDataException("native repository occurrence check receipt is duplicated");
            }
            RepositoryOccurrenceCheckReceipt? loadedLastOccurrenceCheck = reader.Bool() ? ReadOccurrenceCheckState(reader) : null;
            if (loadedLastOccurrenceCheck is { } last && !string.Equals(last.WorldSHA256, World.WorldSHA256, StringComparison.Ordinal))
                throw new InvalidDataException("native repository last occurrence check world authority changed");
            if (loadedLastOccurrenceCheck is { } lastReceipt) ValidateOccurrenceCheckAccess(lastReceipt, stagedAccess.Entries);
            RepositoryReadoutReceipt? loadedLastReadout = reader.Bool() ? ReadReadoutState(reader) : null;
            List<RepositoryFundingReceipt> loadedFundingReceipts = new();
            List<RepositoryPaidOutcomeReceipt> loadedOutcomeReceipts = new();
            List<RepositoryNewEvidenceReceipt> loadedEvidenceReceipts = new();
            _pendingFundingReceipt = null;
            _pendingFundingDecision = null;
            _pendingProposal = null;
            _pendingPolicyDecision = default;
            _hasPendingPolicyDecision = false;
            if (raccVersion >= 7)
            {
                int fundingCount = reader.I32();
                if (fundingCount < 0 || fundingCount > 1_000_000) throw new InvalidDataException("native repository funding count is malformed");
                for (int i = 0; i < fundingCount; i++)
                {
                    RepositoryFundingReceipt receipt = RepositoryLineageReceiptCheckpoint.ReadFunding(reader);
                    receipt.Validate();
                    if (receipt.WorldSHA256 != World.WorldSHA256
                        || !loadedFundingReceipts.All(existing => existing.ReceiptSHA256 != receipt.ReceiptSHA256))
                        throw new InvalidDataException("native repository funding receipt authority changed or duplicated");
                    loadedFundingReceipts.Add(receipt);
                }
                int outcomeCount = reader.I32();
                if (outcomeCount < 0 || outcomeCount > 1_000_000) throw new InvalidDataException("native repository outcome count is malformed");
                for (int i = 0; i < outcomeCount; i++)
                {
                    RepositoryPaidOutcomeReceipt receipt = RepositoryLineageReceiptCheckpoint.ReadPaidOutcome(reader);
                    if (receipt.WorldSHA256 != World.WorldSHA256 || !loadedOutcomeReceipts.All(existing => existing.ReceiptSHA256 != receipt.ReceiptSHA256))
                        throw new InvalidDataException("native repository outcome authority changed or duplicated");
                    loadedOutcomeReceipts.Add(receipt);
                }
                int evidenceCount = reader.I32();
                if (evidenceCount < 0 || evidenceCount > 1_000_000) throw new InvalidDataException("native repository evidence count is malformed");
                for (int i = 0; i < evidenceCount; i++)
                {
                    RepositoryNewEvidenceReceipt receipt = RepositoryLineageReceiptCheckpoint.ReadEvidence(reader);
                    if (receipt.WorldSHA256 != World.WorldSHA256 || !loadedEvidenceReceipts.All(existing => existing.ReceiptSHA256 != receipt.ReceiptSHA256))
                        throw new InvalidDataException("native repository evidence authority changed or duplicated");
                    loadedEvidenceReceipts.Add(receipt);
                }
                if (reader.Bool())
                {
                    RepositoryFundingReceipt pendingFunding = RepositoryLineageReceiptCheckpoint.ReadFunding(reader);
                    pendingFunding.Validate();
                    _pendingFundingReceipt = pendingFunding;
                    _pendingFundingDecision = pendingFunding.FundingDecision;
                }
                bool hasPendingTuple = reader.Bool();
                if (hasPendingTuple != (_pendingFundingReceipt is not null))
                    throw new InvalidDataException("native repository pending funding tuple is incomplete");
                if (hasPendingTuple)
                {
                    RepositoryFrontierRevision pendingRevision = new(reader.U64());
                    RepositoryCandidateDigest pendingDigest = new(reader.U64());
                    string pendingCanonical = reader.Str();
                    CortexPolicyDecision pendingDecision = CortexPolicyDecisionCheckpoint.Read(reader, Policy.ID, Policy.Schema.ActionCount);
                    PolicyCanonicalStateID pendingContextState = RepositoryLineageReceiptCheckpoint.ReadState(reader);
                    int pendingContextActionCount = reader.I32(); int pendingContextDepth = reader.I32();
                    GrammarPolicyContextKey pendingContext = new(in pendingContextState, pendingContextActionCount, pendingContextDepth);
                    pendingDecision = new CortexPolicyDecision(pendingDecision.DecisionID, Policy.ID,
                        pendingDecision.Readout, in pendingContext);
                    if (!RepositoryCandidate.TryParseCanonical(pendingCanonical, out RepositoryCandidate pendingCandidate)
                        || pendingCandidate.Digest != pendingDigest
                        || pendingDecision.DecisionID != _pendingFundingReceipt!.Value.DecisionID
                        || pendingDigest != _pendingFundingReceipt.Value.CandidateDigest
                        || pendingCanonical != _pendingFundingReceipt.Value.CandidateCanonical
                        || pendingRevision != _pendingFundingReceipt.Value.FrontierRevision
                        || pendingContextState != _pendingFundingReceipt.Value.CanonicalState
                        || pendingContextActionCount != Policy.Schema.ActionCount
                        || pendingContextDepth < 0)
                        throw new InvalidDataException("native repository pending funding tuple is malformed");
                    _pendingProposal = new RepositoryCandidateProposal(pendingRevision, pendingDigest, pendingCandidate);
                    _pendingPolicyDecision = pendingDecision;
                    _hasPendingPolicyDecision = true;
                }
                _pendingSelectionEventID = default;
                _pendingSelectionReceipt = default;
                _hasPendingSelection = false;
                if (raccVersion >= 8 && reader.Bool())
                {
                    _pendingSelectionEventID = new TapeEventID(reader.I64());
                    _pendingSelectionReceipt = ReadPendingSelection(reader);
                    _pendingSelectionReceipt.Validate();
                    if (_pendingSelectionEventID.Value <= 0)
                        throw new InvalidDataException("native repository pending selection event is malformed");
                    _hasPendingSelection = true;
                }
            }
            _frontier.LoadState(reader);
            foreach (RepositoryFundingReceipt receipt in loadedFundingReceipts)
            {
                receipt.Validate();
                if (receipt.WorldSHA256 != World.WorldSHA256)
                    throw new InvalidDataException("native repository funding world authority changed");
                ValidateCanonicalStateCorroboration(in receipt);
            }
            foreach (RepositoryPaidOutcomeReceipt receipt in loadedOutcomeReceipts)
            {
                receipt.Validate();
                ValidateCanonicalStateCorroboration(in receipt);
                if (receipt.WorldSHA256 != World.WorldSHA256 || receipt.AccessSHA256 != stagedAccess.AccessSHA256
                    || receipt.Authority.AccessEntryCount != stagedAccess.Count
                    || receipt.Authority.AccessSequence >= stagedAccess.Count
                    || receipt.Authority.AccessSequence >= 0 && stagedAccess.Entries[(int)receipt.Authority.AccessSequence].EntrySHA256 != receipt.Authority.AccessEntrySHA256
                    || receipt.PredecessorEventID.Value >= receipt.SettlementEventID.Value
                    || receipt.SettlementEventID.Value >= receipt.OutcomeEventID.Value)
                    throw new InvalidDataException("native repository paid outcome authority changed");
                RepositoryFundingReceipt funding = loadedFundingReceipts.SingleOrDefault(existing => existing.QuotaDecisionID == receipt.QuotaDecisionID);
                if (funding.QuotaDecisionID != receipt.QuotaDecisionID || funding.CandidateDigest != receipt.CandidateDigest
                    || funding.CandidateCanonical != receipt.CandidateCanonical || funding.DecisionID != receipt.DecisionID
                    || funding.FundingDecision.PlannedArmSteps != receipt.PlannedArmSteps)
                    throw new InvalidDataException("native repository paid outcome payment join changed");
                ValidatePaidOutcomeAuthority(funding, receipt);
            }
            foreach (RepositoryNewEvidenceReceipt receipt in loadedEvidenceReceipts)
            {
                receipt.Validate();
                ValidateCanonicalStateCorroboration(in receipt);
                RepositoryPaidOutcomeReceipt matchedOutcome = loadedOutcomeReceipts.SingleOrDefault(outcome => outcome.EventID == receipt.OutcomeEventID);
                ValidatePaidEvidenceAuthority(matchedOutcome, receipt);
                if (receipt.WorldSHA256 != World.WorldSHA256 || receipt.AccessSHA256 != stagedAccess.AccessSHA256
                    || receipt.Authority.AccessEntryCount != stagedAccess.Count
                    || receipt.Authority.AccessSequence < 0 || receipt.Authority.AccessSequence >= stagedAccess.Count
                    || stagedAccess.Entries[(int)receipt.Authority.AccessSequence].EntrySHA256 != receipt.Authority.AccessEntrySHA256
                    || !loadedOutcomeReceipts.Any(outcome => outcome.EventID == receipt.OutcomeEventID
                        && outcome.OutcomePayloadSource == RepositoryOutcomePayloadSources.WorldObservation
                        && outcome.CandidateDigest == receipt.CandidateDigest && outcome.CandidateCanonical == receipt.CandidateCanonical
                        && outcome.WorldSHA256 == receipt.WorldSHA256 && outcome.AccessSHA256 == receipt.AccessSHA256
                        && outcome.CallSHA256 == receipt.CallSHA256 && outcome.OutcomePayloadSHA256 == receipt.EvidenceSHA256
                        && outcome.PredecessorEventID.Value < outcome.SettlementEventID.Value
                        && outcome.SettlementEventID.Value < outcome.OutcomeEventID.Value
                        && outcome.OutcomeEventID.Value < receipt.EventID.Value
                        && receipt.PredecessorEventID == outcome.EventID && receipt.PredecessorDigest.Value == outcome.ReceiptSHA256))
                    throw new InvalidDataException("native repository paid evidence authority changed");
            }
            foreach (RepositoryPaidOutcomeReceipt outcome in loadedOutcomeReceipts)
            {
                int matchingEvidence = loadedEvidenceReceipts.Count(evidence => evidence.OutcomeEventID == outcome.EventID);
                if (outcome.OutcomePayloadSource == RepositoryOutcomePayloadSources.WorldObservation
                    ? matchingEvidence != 1
                    : matchingEvidence != 0)
                    throw new InvalidDataException("native repository loaded outcome payload custody cardinality diverged");
            }
            if (_pendingFundingReceipt is { } pendingFundingReceipt)
                ValidateFundingReceipt(pendingFundingReceipt, _frontier.AuthoritySHA256, _frontier.Revision, stagedAccess.Entries);
            _access.ReplaceState(stagedAccess);
            _occurrenceCheckReceipts.Clear(); _occurrenceCheckReceipts.AddRange(loadedOccurrenceCheckReceipts);
            _occurrenceCheckReceiptIDs.Clear(); foreach (string id in loadedOccurrenceCheckReceiptIDs) _occurrenceCheckReceiptIDs.Add(id);
            _lastOccurrenceCheck = loadedLastOccurrenceCheck;
            _lastReadout = loadedLastReadout;
            _fundingReceipts.Clear(); _fundingReceipts.AddRange(loadedFundingReceipts);
            _outcomeReceipts.Clear(); _outcomeReceipts.AddRange(loadedOutcomeReceipts);
            _evidenceReceipts.Clear(); _evidenceReceipts.AddRange(loadedEvidenceReceipts);
            _pattern.LoadState(reader);
            if (_hasPendingSelection)
            {
                if (_pendingProposal is not { } pendingSelectionProposal
                    || _pendingSelectionReceipt.CandidateDigest != pendingSelectionProposal.CandidateDigest
                    || _pendingSelectionReceipt.CandidateCanonical != pendingSelectionProposal.Candidate.Canonical
                    || _pendingSelectionReceipt.FrontierRevision != pendingSelectionProposal.Revision)
                    throw new InvalidDataException("native repository pending selection tuple diverges");
            }
            _checkpointObservedPathCursor = _observedPathLog.Count;
            _checkpointAccessCursor = _access.Count;
            _checkpointOccurrenceCheckCursor = _occurrenceCheckReceipts.Count;
            _checkpointLastOccurrenceCheck = _lastOccurrenceCheck;
            _checkpointLastReadout = _lastReadout;
            _checkpointFundingCursor = _fundingReceipts.Count;
            _checkpointOutcomeCursor = _outcomeReceipts.Count;
            _checkpointEvidenceCursor = _evidenceReceipts.Count;
            _checkpointPendingFunding = _pendingFundingReceipt;
        }

        private void ValidateOccurrenceCheckAccess(in RepositoryOccurrenceCheckReceipt receipt)
            => ValidateOccurrenceCheckAccess(receipt, []);

        private void ValidateFundingReceipt(
            in RepositoryFundingReceipt receipt,
            string stagedFrontierAuthority,
            RepositoryFrontierRevision stagedFrontierRevision,
            IReadOnlyList<RepositoryAccessEntry> stagedAccess)
        {
            receipt.Validate();
            if (receipt.WorldSHA256 != World.WorldSHA256 || !Policy.IsCanonicalState(receipt.CanonicalState)
                || receipt.Authority.AccessEntryCount < 0
                || receipt.Authority.AccessEntryCount > _access.Count + stagedAccess.Count
                || receipt.AccessSHA256 != _access.ComputeAccessSHA256AfterDelta((int)receipt.Authority.AccessEntryCount, stagedAccess))
                throw new InvalidDataException("native repository funding authority diverged");
            if (!RepositoryCandidate.TryParseCanonical(receipt.CandidateCanonical, out RepositoryCandidate candidate)
                || candidate.Digest != receipt.CandidateDigest
                || candidate.Species != receipt.Authority.CandidateSpecies)
                throw new InvalidDataException("native repository funding candidate authority diverged");
            bool currentFrontier = receipt.FrontierRevision == stagedFrontierRevision
                && receipt.Authority.FrontierRevision == stagedFrontierRevision
                && receipt.Authority.FrontierAuthoritySHA256 == stagedFrontierAuthority;
            if (currentFrontier)
            {
                if (_frontier.GetSelectionOrdinal(
                        new RepositoryCandidateProposal(receipt.FrontierRevision, receipt.CandidateDigest, candidate),
                        receipt.Authority.CandidateSpecies) != receipt.Authority.SelectionOrdinal)
                    throw new InvalidDataException("native repository funding frontier ordinal diverged");
            }
            else if (!_frontier.TryGetHistoricalAuthority(receipt.FrontierRevision, receipt.CandidateDigest,
                    receipt.CandidateCanonical, out string historicalAuthority, out int historicalOrdinal, out _, out _)
                || historicalAuthority != receipt.Authority.FrontierAuthoritySHA256
                || historicalOrdinal != receipt.Authority.SelectionOrdinal)
                throw new InvalidDataException("native repository funding frontier history diverged");
            ValidateCanonicalStateCorroboration(in receipt);
        }

        private void ValidateCanonicalStateCorroboration(in RepositoryFundingReceipt receipt)
            => ValidateCanonicalStateCorroboration(receipt.CanonicalState, receipt.ReadoutFingerprint, receipt.CandidateFingerprint,
                receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.FrontierRevision,
                receipt.Authority.FrontierAuthoritySHA256, receipt.DecisionID, receipt.DecisionEventID,
                receipt.CandidateDigest, receipt.CandidateCanonical, receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256);

        private void ValidateCanonicalStateCorroboration(in RepositoryPaidOutcomeReceipt receipt)
            => ValidateCanonicalStateCorroboration(receipt.CanonicalState, receipt.ReadoutFingerprint, receipt.CandidateFingerprint,
                receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.FrontierRevision,
                receipt.Authority.FrontierAuthoritySHA256, receipt.DecisionID, receipt.DecisionEventID,
                receipt.CandidateDigest, receipt.CandidateCanonical, receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256);

        private void ValidateCanonicalStateCorroboration(in RepositoryNewEvidenceReceipt receipt)
            => ValidateCanonicalStateCorroboration(receipt.CanonicalState, receipt.ReadoutFingerprint, receipt.CandidateFingerprint,
                receipt.CandidateOccurrenceDigest, receipt.ReadoutRevision, receipt.FrontierRevision,
                receipt.Authority.FrontierAuthoritySHA256, receipt.DecisionID, receipt.DecisionEventID,
                receipt.CandidateDigest, receipt.CandidateCanonical, receipt.ReadoutEventID, receipt.ReadoutPayloadSHA256);

        private void ValidateCanonicalStateCorroboration(
            PolicyCanonicalStateID state,
            CortexPolicyReadoutFingerprint readoutFingerprint,
            CortexPolicyCandidateFingerprint candidateFingerprint,
            ulong supportDigest,
            GrammarRevisionID revision,
            RepositoryFrontierRevision frontierRevision,
            string frontierAuthority,
            CortexPolicyDecisionID decisionID,
            TapeEventID decisionEventID,
            RepositoryCandidateDigest candidateDigest,
            string candidateCanonical,
            TapeEventID readoutEventID,
            string readoutPayloadSHA256)
        {
            if (!RepositoryNative.Policy.IsCanonicalState(state)
                || _runtimeTape is null
                || !_runtimeTape.TryGetEventView(readoutEventID, out TapeEventView view)
                || view.Source != "repository:lineage"
                || view.Provenance != Provenances.Execution
                || view.Roles != TapeEventRoles.AuditOnly
                || !_runtimeTape.Resolve(readoutEventID, out byte[] payload)
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(payload)), readoutPayloadSHA256, StringComparison.Ordinal)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
                || kind != "readout"
                || digest != RepositoryLineageReceiptCodec.Digest(kind, canonical)
                || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields)
                || fields.Length < 39
                || fields[1] != RepositoryNative.Policy.ID.Value
                || fields[2] != candidateDigest.ToString()
                || fields[3] != candidateCanonical
                || fields[4] != decisionID.Value.ToString(CultureInfo.InvariantCulture)
                || fields[5] != decisionEventID.Value.ToString(CultureInfo.InvariantCulture)
                || fields[6] != readoutFingerprint.Value.ToString(CultureInfo.InvariantCulture)
                || fields[7] != candidateFingerprint.Value.ToString(CultureInfo.InvariantCulture)
                || fields[8] != supportDigest.ToString(CultureInfo.InvariantCulture)
                || fields[9] != revision.Value.ToString(CultureInfo.InvariantCulture)
                || fields[32] != RepositoryLineageReceiptCodec.CanonicalState(state)
                || fields[36] != frontierAuthority
                || fields[37] != frontierRevision.Value.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("native repository canonical state lacks its exact readout event corroboration");
            if (!TryFindVerifiedScopePacket(state, readoutFingerprint.Value, candidateFingerprint.Value,
                    supportDigest, revision))
                throw new InvalidDataException("native repository canonical state lacks its exact verified-scope packet");
        }

        private bool TryFindVerifiedScopePacket(
            PolicyCanonicalStateID state,
            ulong readoutFingerprint,
            ulong candidateFingerprint,
            ulong supportDigest,
            GrammarRevisionID revision)
        {
            if (_runtimeTape is null) return false;
            string expected = $"POLICY-VERIFICATION-SCOPE\tpolicy={RepositoryNative.Policy.ID.Value}\treadout={readoutFingerprint:X16}\tcandidate={candidateFingerprint:X16}\tsupport={supportDigest:X16}\trevision={revision.Value}\tstate_policy={state.Policy.Value}\tstate_kind={(byte)state.Kind}\tstate_version={state.Version}\tstate_value={state.Value:X16}";
            int matches = 0;
            foreach (TapeEventView view in _runtimeTape.GetEventViews())
            {
                if (view.Source != "policy:" + RepositoryNative.Policy.ID.Value
                    || view.Provenance != Provenances.Execution
                    || view.Roles != TapeEventRoles.AuditOnly
                    || !_runtimeTape.Resolve(view.Id, out byte[] payload)
                    || !Encoding.ASCII.GetString(payload).Equals(expected, StringComparison.Ordinal)) continue;
                matches++;
            }
            return matches == 1;
        }

        private static void ValidateCanonicalStateCorroboration(
            PolicyCanonicalStateID state,
            CortexPolicyReadoutFingerprint readoutFingerprint,
            CortexPolicyCandidateFingerprint candidateFingerprint,
            ulong supportDigest,
            GrammarRevisionID revision,
            RepositoryFrontierRevision frontierRevision,
            string frontierAuthority,
            RepositoryReadoutReceipt? verifiedReadout)
        {
            if (!RepositoryNative.Policy.IsCanonicalState(state)
                || verifiedReadout is not { } readout
                || readout.CanonicalState != state
                || readout.ReadoutFingerprint != readoutFingerprint.Value
                || readout.CandidateFingerprint != candidateFingerprint.Value
                || readout.CandidateOccurrenceDigest != supportDigest
                || readout.ReadoutRevision != revision
                || readout.FrontierRevision != frontierRevision
                || !string.Equals(readout.FrontierAuthoritySHA256, frontierAuthority, StringComparison.Ordinal))
            throw new InvalidDataException("native repository canonical state lacks its verified readout/source corroboration");
        }

        private static void ValidateVerifiedScope(
            Cortex cortex,
            PolicyCanonicalStateID state,
            CortexPolicyReadoutFingerprint readoutFingerprint,
            CortexPolicyCandidateFingerprint candidateFingerprint,
            ulong supportDigest,
            GrammarRevisionID revision)
        {
            PolicyCanonicalStateID verifiedState = state;
            if (!cortex.IsVerifiedPolicyScope(RepositoryNative.Policy.ID, in verifiedState,
                    readoutFingerprint.Value, candidateFingerprint.Value, supportDigest, revision))
                throw new InvalidDataException("native repository canonical state lacks a verified policy scope");
        }

        private void ValidatePendingFundingTuple()
        {
            if (_pendingFundingReceipt is null) return;
            if (_pendingFundingDecision is null || !_hasPendingPolicyDecision || _pendingProposal is null)
                throw new InvalidDataException("native repository pending funding tuple is incomplete");
        }

        private static void ValidatePaidOutcomeAuthority(
            in RepositoryFundingReceipt funding,
            in RepositoryPaidOutcomeReceipt outcome)
        {
            if (funding.QuotaDecisionID != outcome.QuotaDecisionID
                || funding.DecisionEventID != outcome.DecisionEventID
                || funding.DecisionPayloadSHA256 != outcome.DecisionPayloadSHA256
                || funding.ReadoutEventID != outcome.ReadoutEventID
                || funding.ReadoutPayloadSHA256 != outcome.ReadoutPayloadSHA256
                || funding.FundingEventID != outcome.FundingEventID
                || funding.FundingPayloadSHA256 != outcome.FundingPayloadSHA256
                || funding.BoundaryEventID != outcome.BoundaryEventID
                || funding.BoundaryPayloadSHA256 != outcome.BoundaryPayloadSHA256
                || funding.SettlementEventID != outcome.SettlementEventID
                || funding.SettlementPayloadSHA256 != outcome.SettlementPayloadSHA256
                || funding.CanonicalState != outcome.CanonicalState
                || funding.ReadoutFingerprint != outcome.ReadoutFingerprint
                || funding.CandidateFingerprint != outcome.CandidateFingerprint
                || funding.CandidateOccurrenceDigest != outcome.CandidateOccurrenceDigest
                || funding.ReadoutRevision != outcome.ReadoutRevision
                || funding.Authority.FrontierAuthoritySHA256 != outcome.Authority.FrontierAuthoritySHA256
                || funding.Authority.FrontierRevision != outcome.Authority.FrontierRevision
                || funding.Authority.SelectionOrdinal != outcome.Authority.SelectionOrdinal
                || funding.Authority.CandidateSpecies != outcome.Authority.CandidateSpecies)
                throw new InvalidDataException("repository paid outcome packet authority diverged from payment");
        }

        private static void ValidatePaidEvidenceAuthority(
            in RepositoryPaidOutcomeReceipt outcome,
            in RepositoryNewEvidenceReceipt evidence)
        {
            if (outcome.EventID != evidence.OutcomeEventID
                || outcome.DecisionEventID != evidence.DecisionEventID
                || outcome.DecisionPayloadSHA256 != evidence.DecisionPayloadSHA256
                || outcome.ReadoutEventID != evidence.ReadoutEventID
                || outcome.ReadoutPayloadSHA256 != evidence.ReadoutPayloadSHA256
                || outcome.FundingEventID != evidence.FundingEventID
                || outcome.FundingPayloadSHA256 != evidence.FundingPayloadSHA256
                || outcome.BoundaryEventID != evidence.BoundaryEventID
                || outcome.BoundaryPayloadSHA256 != evidence.BoundaryPayloadSHA256
                || outcome.SettlementEventID != evidence.SettlementEventID
                || outcome.SettlementPayloadSHA256 != evidence.SettlementPayloadSHA256
                || outcome.CanonicalState != evidence.CanonicalState
                || outcome.ReadoutFingerprint != evidence.ReadoutFingerprint
                || outcome.CandidateFingerprint != evidence.CandidateFingerprint
                || outcome.CandidateOccurrenceDigest != evidence.CandidateOccurrenceDigest
                || outcome.ReadoutRevision != evidence.ReadoutRevision
                || outcome.Authority.FrontierAuthoritySHA256 != evidence.Authority.FrontierAuthoritySHA256
                || outcome.Authority.FrontierRevision != evidence.Authority.FrontierRevision
                || outcome.Authority.SelectionOrdinal != evidence.Authority.SelectionOrdinal
                || outcome.Authority.CandidateSpecies != evidence.Authority.CandidateSpecies)
                throw new InvalidDataException("repository paid evidence packet authority diverged from outcome");
        }

        private void ValidateOccurrenceCheckAccess(in RepositoryOccurrenceCheckReceipt receipt, IReadOnlyList<RepositoryAccessEntry> stagedAccess)
        {
            if (receipt.AccessEntryCount < 0 || receipt.AccessEntryCount > _access.Count + stagedAccess.Count
                || !string.Equals(receipt.AccessSHA256, _access.ComputeAccessSHA256AfterDelta(receipt.AccessEntryCount, stagedAccess), StringComparison.Ordinal))
                throw new InvalidDataException("native repository occurrence check access aggregate authority changed");
            if (receipt.Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved) return;
            if (receipt.AccessSequence < 0 || receipt.AccessSequence >= receipt.AccessEntryCount
                || !GetStagedAccessEntry(receipt.AccessSequence, stagedAccess).EntrySHA256.Equals(receipt.AccessEntrySHA256, StringComparison.Ordinal))
                throw new InvalidDataException("native repository occurrence check access entry authority changed");
        }

        private RepositoryAccessEntry GetStagedAccessEntry(long sequence, IReadOnlyList<RepositoryAccessEntry> stagedAccess)
            => sequence < _access.Count ? _access.Entries[(int)sequence] : stagedAccess[(int)sequence - _access.Count];

        private static void WriteOccurrenceCheckState(CkptWriter writer, RepositoryOccurrenceCheckReceipt receipt)
        {
            writer.I32(receipt.Step); writer.U8((byte)receipt.Prediction.Species); writer.Str(receipt.Prediction.Path);
            writer.I32(receipt.Prediction.Line); writer.Str(receipt.Prediction.Value); writer.Str(receipt.Prediction.OtherPath);
            writer.U8((byte)receipt.Outcome); writer.Str(receipt.WorldSHA256); writer.Str(receipt.AccessSHA256);
            writer.I64(receipt.AccessSequence); writer.Str(receipt.AccessEntrySHA256); writer.I32(receipt.AccessEntryCount);
            writer.Str(receipt.PredictionSHA256); writer.Str(receipt.EvidenceSHA256); writer.I64(receipt.EvaluatorCost);
            writer.I64(receipt.AccessCost); writer.I64(receipt.PredecessorEventID.Value); writer.Str(receipt.CallSHA256);
            writer.Str(receipt.ReceiptSHA256);
        }

        private static RepositoryOccurrenceCheckReceipt ReadOccurrenceCheckState(CkptReader reader)
        {
            int step = reader.I32(); var species = (RepositoryPredictionSpecies)reader.U8();
            var prediction = new RepositoryPrediction(species, reader.Str(), reader.I32(), reader.Str(), reader.Str());
            var outcome = (RepositoryOccurrenceCheckOutcomes)reader.U8(); string world = reader.Str(); string access = reader.Str();
            long accessSequence = reader.I64(); string accessEntrySHA = reader.Str(); int accessEntryCount = reader.I32(); string predictionSHA = reader.Str(); string evidence = reader.Str(); long evaluator = reader.I64(); long accessCost = reader.I64();
            var predecessor = new TapeEventID(reader.I64()); string call = reader.Str(); string receiptSHA = reader.Str();
            var result = new RepositoryOccurrenceCheckReceipt(step, prediction, outcome, world, access, predictionSHA, evidence, evaluator, accessCost, predecessor, call, receiptSHA)
            {
                AccessSequence = accessSequence,
                AccessEntrySHA256 = accessEntrySHA,
                AccessEntryCount = accessEntryCount,
            };
            result.Validate();
            return result;
        }

        private static void WriteReadoutState(CkptWriter writer, RepositoryReadoutReceipt receipt)
        {
            writer.I32(receipt.Step); writer.Str(receipt.PolicyID); writer.U64(receipt.CandidateDigest.Value);
            writer.Str(receipt.CandidateCanonical); writer.U64(receipt.DecisionID.Value); writer.I64(receipt.DecisionEventID.Value);
            writer.U64(receipt.ReadoutFingerprint); writer.U64(receipt.CandidateFingerprint); writer.U64(receipt.CandidateOccurrenceDigest);
            writer.U64(receipt.ReadoutRevision.Value); writer.U8((byte)receipt.Authority); writer.U8((byte)receipt.SelectionCause);
            writer.I32(receipt.LaunchpadAction); writer.I32(receipt.RawCandidateAction); writer.I32(receipt.SelectedCandidateAction); writer.I32(receipt.ExecutedAction);
            writer.Str(receipt.SourceEpisodeID.Value); writer.Str(receipt.SourceEpisodeSHA256); writer.I64(receipt.CompositionEventID.Value);
            writer.U64(receipt.CompositionRevision.Value); writer.U64(receipt.FoldPreviousRevision.Value); writer.U64(receipt.FoldRevision.Value);
            writer.I32(receipt.FoldConsumedEventIDs.Length);
            foreach (TapeEventID eventID in receipt.FoldConsumedEventIDs) writer.I64(eventID.Value);
            writer.Str(receipt.FoldConsumedEventSHA256); writer.Str(receipt.FoldReceiptSHA256);
            writer.I64(receipt.TeacherPacketEventID.Value); writer.I64(receipt.TeacherCompositionEventID.Value); writer.I32(receipt.TeacherEvidenceEventIDs.Length);
            foreach (TapeEventID eventID in receipt.TeacherEvidenceEventIDs) writer.I64(eventID.Value);
            writer.Str(receipt.TeacherEvidenceSHA256); writer.Str(receipt.TeacherCorroborationSHA256); writer.Str(receipt.TeacherProvenanceSHA256);
            writer.I64(receipt.PredecessorEventID.Value); writer.Str(receipt.ReceiptSHA256);
            writer.Str(receipt.CanonicalState.Policy.Value); writer.U8((byte)receipt.CanonicalState.Kind);
            writer.U16(receipt.CanonicalState.Version); writer.U64(receipt.CanonicalState.Value);
            writer.U64(receipt.ContextDigest); writer.I32(receipt.ContextActionCount); writer.I32(receipt.ContextDeliberationDepth);
            writer.Str(receipt.FrontierAuthoritySHA256); writer.U64(receipt.FrontierRevision.Value); writer.I32(receipt.SelectionOrdinal); writer.U8((byte)receipt.CandidateSpecies);
        }

        private static RepositoryReadoutReceipt ReadReadoutState(CkptReader reader)
        {
            int step = reader.I32(); string policy = reader.Str(); var candidateDigest = new RepositoryCandidateDigest(reader.U64());
            string candidateCanonical = reader.Str(); var decisionID = new CortexPolicyDecisionID(reader.U64()); var decisionEventID = new TapeEventID(reader.I64());
            ulong readoutFingerprint = reader.U64(); ulong candidateFingerprint = reader.U64(); ulong candidateOccurrenceDigest = reader.U64();
            var readoutRevision = new GrammarRevisionID(reader.U64()); var authority = (CortexPolicyAuthorities)reader.U8(); var cause = (CortexPolicySelectionCauses)reader.U8();
            int launchpad = reader.I32(); int raw = reader.I32(); int selected = reader.I32(); int executed = reader.I32();
            var sourceEpisode = new LoopClosureCompositionEpisodeID(reader.Str()); string sourceEpisodeSHA = reader.Str(); var compositionEventID = new TapeEventID(reader.I64());
            var compositionRevision = new GrammarRevisionID(reader.U64()); var foldPrevious = new GrammarRevisionID(reader.U64()); var foldRevision = new GrammarRevisionID(reader.U64());
            int foldEventCount = reader.I32();
            if (foldEventCount <= 0 || foldEventCount > 1_000_000) throw new InvalidDataException("native repository readout fold event count is malformed");
            TapeEventID[] foldEvents = new TapeEventID[foldEventCount];
            for (int index = 0; index < foldEventCount; index++) foldEvents[index] = new TapeEventID(reader.I64());
            string foldConsumed = reader.Str(); string foldReceipt = reader.Str(); var teacherPacket = new TapeEventID(reader.I64()); var teacherComposition = new TapeEventID(reader.I64());
            int evidenceCount = reader.I32();
            if (evidenceCount <= 0 || evidenceCount > 1_000_000) throw new InvalidDataException("native repository readout teacher evidence count is malformed");
            TapeEventID[] teacherEvidence = new TapeEventID[evidenceCount];
            for (int index = 0; index < evidenceCount; index++) teacherEvidence[index] = new TapeEventID(reader.I64());
            string teacherEvidenceSHA = reader.Str(); string teacherCorroboration = reader.Str(); string teacherProvenance = reader.Str();
            var predecessor = new TapeEventID(reader.I64()); string receiptSHA = reader.Str();
            var canonicalState = new PolicyCanonicalStateID(new CortexPolicyID(reader.Str()),
                (PolicyCanonicalStateKinds)reader.U8(), reader.U16(), reader.U64());
            ulong contextDigest = reader.U64(); int contextActionCount = reader.I32(); int contextDepth = reader.I32();
            string frontierAuthority = reader.Str(); var frontierRevision = new RepositoryFrontierRevision(reader.U64()); int selectionOrdinal = reader.I32(); var candidateSpecies = (RepositoryCandidateSpecies)reader.U8();
            var receipt = new RepositoryReadoutReceipt(step, policy, candidateDigest, candidateCanonical, decisionID, decisionEventID,
                readoutFingerprint, candidateFingerprint, candidateOccurrenceDigest, readoutRevision, authority, cause,
                launchpad, raw, selected, executed, sourceEpisode, sourceEpisodeSHA, compositionEventID, compositionRevision,
                foldPrevious, foldRevision, foldEvents, foldConsumed, foldReceipt, teacherPacket, teacherComposition, teacherEvidence,
                teacherEvidenceSHA, teacherCorroboration, teacherProvenance, predecessor, receiptSHA, canonicalState,
                contextDigest, contextActionCount, contextDepth, frontierAuthority, frontierRevision, selectionOrdinal)
            {
                CandidateSpecies = candidateSpecies,
            };
            receipt.Validate();
            return receipt;
        }

    }

    private sealed class RepositoryNativeTool(RepositoryNativeRuntime runtime, RepositoryNativeToolDescriptor descriptor) : CortexTool
    {
        public Tool.ToolVerbs Verb => descriptor.Verb;
        public override string Name => descriptor.Name;
        public override bool IsTerminal => descriptor.IsTerminal;

        public override bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action)
        {
            string trimmed = line.TrimStart();
            int tokenLength = 0;
            while (tokenLength < trimmed.Length && !char.IsWhiteSpace(trimmed[tokenLength])) tokenLength++;
            if (!string.Equals(trimmed[..tokenLength], descriptor.Name, StringComparison.Ordinal))
            {
                action = CortexAction.None;
                return false;
            }
            Tool.ToolCall call = Tool.ToolCall.Parse(line);
            if (call.Verb != descriptor.Verb || call.Arg.Length == 0) { action = CortexAction.None; return false; }
            arguments.Add(new CortexActionArgument(RepositoryNativeToolAuthority.ArgumentSlot, call.Arg, Blur.SlotSources.GrammarPrior));
            action = new CortexAction(this, call.Raw.Trim());
            return true;
        }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
        {
            string arg = arguments.Count > 0 ? arguments[0].Value : "";
            Tool.Observation observation;
            if (runtime.TryReadPendingCandidate(action, arguments, descriptor.Verb, out RepositoryCandidate candidate))
            {
                string callSHA = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(action.Raw)));
                if (candidate is RepositoryCandidate.VerifyPredictionCandidate verify)
                    observation = runtime.Verify(verify.Prediction.Prediction, callSHA);
                else
                    observation = runtime.World.Act(Tool.ToolCall.Create(candidate.Verb, candidate.Argument));
            }
            else
            {
                // Text parsing remains the compatibility boundary for external/generated calls.
                Tool.ToolCall call = Tool.ToolCall.Parse(action.Raw);
                observation = call.Verb == Tool.ToolVerbs.Verify
                    ? runtime.Verify(arg, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(action.Raw))), out _)
                    : runtime.World.Act(call);
            }

            // The arm's valve sits HERE and nowhere else: after the world has been asked and before
            // anything downstream can see the answer. Every arm therefore funds, spends and records
            // the same look — only the result differs, which is what makes the three arms comparable
            // at matched fuel (G3).
            observation = runtime.MediateToolResult(in observation);

            for (int i = 0; i < observation.HitPaths.Count; i++)
            {
                fields.Add(new CortexObservationField(i == 0 ? "top_hit" : "hit_path", observation.HitPaths[i].Value,
                    Blur.SlotSources.PriorObservation));
                fields.Add(new CortexObservationField("repository_path", observation.HitPaths[i].Value,
                    Blur.SlotSources.PriorObservation));
            }
            foreach (Tool.RepositoryLocus locus in observation.Loci)
                fields.Add(new CortexObservationField("repository_locus", $"{locus.Path.Value}\t{locus.Line}",
                    Blur.SlotSources.PriorObservation));
            if (observation.AnswerPath.Length > 0)
                fields.Add(new CortexObservationField("answer_path", observation.AnswerPath.Value, Blur.SlotSources.PriorObservation));
            if (observation.OccurrenceCheck is { } result)
            {
                    // Frozen action/journal field token; identifier-side name is OccurrenceCheckPrediction.
                    fields.Add(new CortexObservationField("verification_claim", candidate is RepositoryCandidate.VerifyPredictionCandidate verify
                    ? verify.Prediction.Prediction.Canonical : arg, Blur.SlotSources.PriorObservation));
                // Frozen action/journal field tokens; identifier-side names are VerifierOutcome,
                // OccurrenceCheckEvaluatorCost, OccurrenceCheckAccessCost, and OccurrenceCheckEvidenceSHA256.
                fields.Add(new CortexObservationField("verification_outcome", result.Outcome.ToString(), Blur.SlotSources.PriorObservation));
                fields.Add(new CortexObservationField("verification_evaluator_cost", result.EvaluatorCost.ToString(), Blur.SlotSources.PriorObservation));
                fields.Add(new CortexObservationField("verification_access_cost", result.AccessCost.ToString(), Blur.SlotSources.PriorObservation));
                fields.Add(new CortexObservationField("verification_evidence_sha256", result.EvidenceSHA256, Blur.SlotSources.PriorObservation));
            }
            runtime.StageObservation(observation);
            return new CortexObservation(observation.Text, observation.Answered);
        }
    }

    private sealed class RepositoryNativeActionPolicy : CortexActionPolicy
    {
        private readonly RepositoryNativeRuntime _runtime;

        public RepositoryNativeActionPolicy(RepositoryNativeRuntime runtime) => _runtime = runtime;

        public override bool HarvestsAfterBatch => true;
        public override bool InstallsRevisionAfterBatch => true;

        public override string GetSource(Cortex cortex, CortexAction action) => "repository-action";

        public override TapeEventRoles ActionExecutionRoles(Cortex cortex, CortexAction action)
            => TapeEventRoles.AuditOnly;

        public override bool TryChooseAction(Cortex cortex, List<CortexActionArgument> arguments, out CortexAction action)
            => _runtime.TryPropose(cortex, arguments,
                verb => cortex.Tools.OfType<RepositoryNativeTool>().FirstOrDefault(tool => tool.Verb == verb), out action);

        public override CortexActionAdmissionDecision EvaluateActionRequestAdmission(Cortex cortex, CortexAction action,
            List<CortexActionArgument> arguments)
            => action.Tool is RepositoryNativeTool tool
                ? _runtime.ValidateProposal(cortex, action, arguments, tool.Verb)
                : CortexActionAdmissionDecision.Deny("native-tool-type");

        public override CortexActionAdmissionDecision EvaluateActionExecutionAdmission(Cortex cortex, CortexAction action,
            List<CortexActionArgument> arguments, CortexObservation observation, List<CortexObservationField> fields)
        {
            if (action.Tool is not RepositoryNativeTool tool)
                return CortexActionAdmissionDecision.Deny("native-tool-type");
            if (!_runtime.TryReadPendingCandidate(action, arguments, tool.Verb, out RepositoryCandidate candidate))
                return CortexActionAdmissionDecision.Deny("frontier-proposal-stale");
            if (candidate is RepositoryCandidate.AnswerPathCandidate && observation.Terminal
                && fields.FirstOrDefault(static field => field.Slot == "answer_path").Value.Length == 0)
                return CortexActionAdmissionDecision.Deny("answer-requires-observed-path");
            return CortexActionAdmissionDecision.Admit("native-observation");
        }

        public override void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, List<CortexObservationField> fields, byte[] executionBytes,
            List<TapeEventID> eventIDs)
        {
            _runtime.CommitObservation(cortex, action, arguments, observation, fields, executionBytes, eventIDs);
            if (observation.Text.Length > 0)
                cortex.AppendEvidence(Encoding.UTF8.GetBytes(observation.Text), "tool-observation");
        }

        public override void AppendDomainEvents(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs)
        {
            _runtime.AppendRepositoryAdmissionPlan(cortex, action, eventIDs);
            _runtime.AppendOccurrenceCheckReceipt(cortex, action, eventIDs);
        }

        public override void OnActionExecutionAdmission(Cortex cortex, CortexAction action,
            in CortexActionAdmissionDecision decision)
            => _runtime.OnExecutionAdmission(cortex, action, in decision);
    }

    private sealed class RepositoryNativeObservationReward : CortexReward
    {
        private readonly RepositoryNativeRuntime _runtime;

        public RepositoryNativeObservationReward(RepositoryNativeRuntime runtime) => _runtime = runtime;

        public override void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs)
            => _runtime.Observe(cortex.Step);
    }
}
