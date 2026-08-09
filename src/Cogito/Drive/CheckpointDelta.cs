namespace Cogito;

using Cogito.Induct;
using Ronmamon;
using System.Security.Cryptography;
using System.Text;

/// Durable receipt for one typed checkpoint mutation.  The canonical
/// checkpoint remains the keyframe; this rail records only append/reorder
/// transitions and the two append-only text cursors.  It is deliberately not
/// a second state image: replay starts from the keyframe and the receipts are
/// the continuity proof for the streams that fed it.
internal readonly record struct CheckpointDeltaReceipt(
    long Bytes,
    long AllocatedBytes,
    int RecordCount,
    bool Compacted,
    string LogicalStateSHA256,
    string ChainSHA256);

internal readonly record struct CheckpointDeltaReplayReceipt(
    int RecordCount,
    int LastToStep,
    GrammarArtifactDelta LatestGrammarArtifact,
    CortexSnap? LatestSnap);

/// Physical authority for the mutation rail. The canonical keyframe owns the
/// image and its base horizon; the typed rail authenticates the terminal
/// horizon reached after that keyframe without pretending to be a second image.
internal readonly record struct CheckpointDeltaAuthority(
    string BasePhysicalSHA256,
    string ChainSHA256,
    int BaseStep,
    int LastToStep,
    int RecordCount);

internal enum CheckpointReplayKinds : byte
{
    None = 0,
    AnytimeRebase = 1,
}

/// The execution window in force when a mutation was persisted. This is part of
/// the typed mutation payload so replay does not consult the mutable latest
/// window file after a resumed process has replaced it.
internal readonly record struct CheckpointReplayContext(
    int WindowStartStep,
    int WindowEndStep,
    CheckpointReplayKinds Kind = CheckpointReplayKinds.None,
    int RecordFromStep = -1,
    int RecordToStep = -1,
    string ConfigDigest = "",
    string RailRunID = "",
    string ContainerRunID = "",
    string PredecessorCurveDigest = "",
    string PredecessorParentPointID = "",
    string SuccessorRunID = "",
    string SuccessorConfigID = "",
    string SuccessorChainID = "",
    string SuccessorArmID = "",
    string ScheduleDigest = "",
    string BasePhysicalSHA256 = "",
    string BaseLogicalSHA256 = "",
    long Sequence = -1,
    string PreviousRecordSHA256 = "",
    string RecordSHA256 = "",
    bool Bound = false)
{
    internal bool Present => Bound && WindowStartStep >= 0 && WindowEndStep >= WindowStartStep
        && Kind is CheckpointReplayKinds.None or CheckpointReplayKinds.AnytimeRebase;

    internal bool CoversRecord => Present && RecordFromStep >= WindowStartStep && RecordToStep >= RecordFromStep
        && RecordToStep <= WindowEndStep;

    internal CheckpointReplayContext BindRecord(int fromStep, int toStep)
        => this with { RecordFromStep = fromStep, RecordToStep = toStep };

    internal CheckpointReplayContext BindRail(string basePhysical, string baseLogical, string runID,
        long sequence, string previousRecord, string record)
        => this with
        {
            BasePhysicalSHA256 = basePhysical,
            BaseLogicalSHA256 = baseLogical,
            ContainerRunID = runID,
            Sequence = sequence,
            PreviousRecordSHA256 = previousRecord,
            RecordSHA256 = record,
        };

    internal CheckpointReplayContext Validate()
    {
        if (!Present) throw new InvalidDataException("checkpoint replay context is missing or malformed");
        if (Kind == CheckpointReplayKinds.AnytimeRebase
            && (string.IsNullOrEmpty(ConfigDigest) || string.IsNullOrEmpty(RailRunID) || string.IsNullOrEmpty(PredecessorCurveDigest)
                || string.IsNullOrEmpty(PredecessorParentPointID) || string.IsNullOrEmpty(SuccessorRunID)
                || string.IsNullOrEmpty(SuccessorConfigID) || string.IsNullOrEmpty(SuccessorChainID) || string.IsNullOrEmpty(SuccessorArmID)
                || string.IsNullOrEmpty(ScheduleDigest)))
            throw new InvalidDataException("checkpoint anytime rebase context lacks immutable custody");
        return this;
    }

    internal CheckpointReplayContext ValidateForRecord(int fromStep, int toStep)
    {
        if (!Present || RecordFromStep != fromStep || RecordToStep != toStep
            || !CoversRecord)
            throw new InvalidDataException("checkpoint replay context does not cover its authenticated mutation horizon");
        return this;
    }
}

internal readonly record struct GrammarArtifactDelta(ulong Revision, string FileName, string SHA256)
{
    internal bool IsEmpty => Revision == 0 || string.IsNullOrEmpty(FileName);
}

/// The loop carries these values between steps but they do not belong to an
/// organ, so a mutation rail used to leave them frozen at the last keyframe.
/// This is a replacement (not an arithmetic delta): every member is captured
/// at the mutation horizon and replay returns the complete resume anchor.
internal readonly record struct CortexSnapCheckpointDelta(
    bool Present,
    int NextStep,
    ulong GrammarRevision,
    long LastInduceBytes,
    int WallStreak,
    int TotalEvicted,
    int TotalPromoted,
    int LastSlotted,
    long LastBitsSaved,
    long CurveLen,
    long LastSleepBytes,
    double LastInduceOpb,
    double LastBitsPerSpan,
    long PrevTapeCount,
    int BreachConsolidationPhases,
    int BreachWindowResets,
    int ForkStep,
    double ForkVolumeFrac,
    int LastConsolidationPhaseRules,
    int LastDNodes)
{
    internal static CortexSnapCheckpointDelta Capture(in CortexSnap snap)
        => new(true, snap.NextStep, snap.GrammarRevision, snap.LastInduceBytes, snap.WallStreak,
            snap.TotalEvicted, snap.TotalPromoted, snap.LastSlotted, snap.LastBitsSaved, snap.CurveLen,
            snap.LastSleepBytes, snap.LastInduceOpb, snap.LastBitsPerSpan, snap.PrevTapeCount,
            snap.BreachConsolidationPhases, snap.BreachWindowResets, snap.ForkStep, snap.ForkVolumeFrac,
            snap.LastConsolidationPhaseRules, snap.LastDNodes);

    internal CortexSnap ToSnapshot()
    {
        if (!Present) throw new InvalidDataException("missing CortexSnap mutation replacement");
        return new(NextStep, GrammarRevision, LastInduceBytes, WallStreak,
            TotalEvicted, TotalPromoted, LastSlotted, LastBitsSaved, CurveLen,
            LastSleepBytes, LastInduceOpb, LastBitsPerSpan, PrevTapeCount,
            BreachConsolidationPhases, BreachWindowResets, ForkStep, ForkVolumeFrac,
            LastConsolidationPhaseRules, LastDNodes);
    }
}

/// Runtime-owned curriculum mutations are encoded by the curriculum that owns
/// the state.  The checkpoint rail only authenticates the envelope and replays
/// it; it must not grow a type arm for every new runtime.
internal interface ICurriculumCheckpointDelta
{
    string Kind { get; }
    void Write(CkptWriter writer);
}

internal interface ICurriculumCheckpointDeltaOwner
{
    ICurriculumCheckpointDelta? CaptureCheckpointDelta();
    void ApplyCheckpointDelta(ICurriculumCheckpointDelta delta, in CheckpointReplayContext replayContext);
    void CommitCheckpointDelta(ICurriculumCheckpointDelta captured);
}

internal readonly record struct OpaqueCurriculumCheckpointDelta(string Kind, byte[] Payload)
    : ICurriculumCheckpointDelta
{
    public void Write(CkptWriter writer) => writer.Raw(Payload);
}

/// ReplayCalc's mutation is split by ownership: EmlSieve owns append-only proof
/// logs, EmlSampler owns its deterministic rail cursor, and this envelope owns
/// the bounded replacement cursors/lineage that sit on the curriculum itself.
/// A checkpoint delta is therefore replayable state, never a support marker.
internal readonly record struct ReplayCalcCheckpointDelta(
    EmlSieveCheckpointDelta Sieve,
    EmlSamplerCheckpointDelta Sampler,
    int WorldOpportunityCursor,
    TapeEventID[] WorldOpportunityEvents,
    int EnumTaken,
    bool EnumDone,
    int Minted,
    string? Anchor,
    int AnytimePointCursor,
    EmlAnytimeCurvePoint[] AnytimePoints,
    int AnytimeKillCursor,
    EmlAnytimeKillReceipt[] AnytimeKills,
    bool PairedFuelConfigured,
    EmlPairedFuelSchedule PairedFuelSchedule,
    EmlPairedFuelScheduleRow[] PairedFuelRows,
    bool PairedFuelCursorDirty,
    int ProcessExactHighWater,
    EmlProcessConstantState CatalanProcess,
    EmlProcessConstantState Zeta3Process,
    ReplayCalcRung0CheckpointDelta Rung0,
    EmlLawStoreCheckpointDelta LawStore,
    EmlActionCheckpointDelta Action,
    bool AnytimeRebase = false,
    string AnytimeRebasePredecessorRunID = "",
    string AnytimeRebasePredecessorConfigID = "",
    string AnytimeRebasePredecessorChainID = "",
    string AnytimeRebasePredecessorArmID = "",
    string AnytimeRebasePredecessorPointID = "",
    string AnytimeRebaseSuccessorRunID = "",
    string AnytimeRebaseSuccessorConfigID = "",
    string AnytimeRebaseSuccessorChainID = "",
    string AnytimeRebaseSuccessorArmID = "",
    int AnytimeRebaseSuccessorRung = 0) : ICurriculumCheckpointDelta
{
    public string Kind => "dream-calc";
    public void Write(CkptWriter writer) => ReplayCalc.WriteCheckpointDelta(writer, in this);
}

/// The ordinary ReplayCalc rung-0 counters are replacement state, while funnel
/// receipts are an append-only proof queue.  Keep both typed here so delta
/// replay cannot silently drop the receipt lineage that SaveState carries.
internal readonly record struct ReplayCalcRung0CheckpointDelta(
    bool Present,
    int FunnelCursor,
    int Opportunities,
    int CarrierBoundCandidates,
    int GuardEligibleCandidates,
    int PaidAttempts,
    int AttemptedCandidates,
    int Compositions,
    int ZeroEvaluatorCompositions,
    int Audits,
    int AgreedAudits,
    int DisagreedAudits,
    int NotSelectedAudits,
    int RelationNullExecutions,
    int RelationNullDivergences,
    int RelationNullAuthorityPredictions,
    int RelationNullPairsConsidered,
    int RelationNullPairsCreated,
    int RelationNullRejectNoCarrier,
    int RelationNullRejectShape,
    int RelationNullRejectGrade,
    ulong CompositionDigest,
    string SourceDigest,
    string ConfigDigest,
    EmlRung0FunnelReceipt[] FunnelReceipts);

/// Typed scheduler mutation. The keyframe still owns the full curriculum; this
/// value only records the scheduler organ selected by the runtime and its
/// mutation-shaped state for the continuity rail.
internal readonly record struct CheckpointMutationState(
    SelfStreamCheckpointDelta SelfStream,
    WeightController.WeightControllerCheckpointDelta Controller,
    Metabolism.MetabolismCheckpointDelta Metabolism,
    MemoryCheckpointDelta Memory,
    HomeostatCheckpointDelta Homeostat,
    Rhythm.RhythmCheckpointDelta Rhythm,
    CortexPolicyCheckpointDelta Policy,
    LoopLineageTurnstile.LoopLineageCheckpointDelta Lineage,
    Cogito.Induct.LoomCheckpointDelta Loom,
    bool HasLoom,
    ICurriculumCheckpointDelta? Curriculum,
    CortexSnapCheckpointDelta Snap = default,
    CheckpointReplayContext ReplayContext = default,
    LoopClosureLinkAttempt[] LoopClosureLinks = null!);

/// One decoded mutation record. The keyframe remains the image authority; this
/// value only carries the typed transitions that must be folded into a loaded
/// set of organs in sequence.
internal readonly record struct DecodedMutation(
    TapeCheckpointDelta Tape,
    JournalCheckpointDelta Journal,
    ReadsCheckpointDelta Reads,
    GrammarArtifactDelta Grammar,
    SelfStreamCheckpointDelta SelfStream,
    WeightController.WeightControllerCheckpointDelta Controller,
    Metabolism.MetabolismCheckpointDelta Metabolism,
    MemoryCheckpointDelta Memory,
    HomeostatCheckpointDelta Homeostat,
    Rhythm.RhythmCheckpointDelta Rhythm,
    CortexPolicyCheckpointDelta Policy,
    LoopLineageTurnstile.LoopLineageCheckpointDelta Lineage,
    Cogito.Induct.LoomCheckpointDelta Loom,
    bool HasState,
    bool HasLoom,
    ICurriculumCheckpointDelta? Curriculum,
    CortexSnapCheckpointDelta Snap,
    CheckpointReplayContext ReplayContext,
    LoopClosureLinkAttempt[] LoopClosureLinks,
    int FromStep = 0,
    int ToStep = 0);

internal static class CheckpointDelta
{
    private const string Dialect = "CORTEXT-M3";
    private const int HashLength = 32;
    private const int MaxRecords = 1_000_000;
    private const int MaxLines = 1_000_000;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(Dialect + "\n");
    private static readonly byte[] LegacyMagic = Encoding.ASCII.GetBytes("CORTEXT-M2\n");
    private static readonly byte[] ZeroHash = new byte[HashLength];
    private const string TailDialect = "CORTEXT-M3-TAIL";
    private static readonly byte[] TailMagic = Encoding.ASCII.GetBytes(TailDialect + "\n");
    private static readonly byte[] LegacyTailMagic = Encoding.ASCII.GetBytes("CORTEXT-M2-TAIL\n");

    private static bool IsMagic(ReadOnlySpan<byte> value)
        => value.SequenceEqual(Magic) || value.SequenceEqual(LegacyMagic);

    private static bool IsTailMagic(ReadOnlySpan<byte> value)
        => value.SequenceEqual(TailMagic) || value.SequenceEqual(LegacyTailMagic);

    internal static CheckpointWriteReceipt Append(
        Run run,
        int fromStep,
        int toStep,
        Tape tape,
        Journal journal,
        Reads reads,
        in GrammarArtifactDelta grammarArtifact = default,
        in CheckpointMutationState state = default,
        in CheckpointReplayContext replayContext = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(reads);
        if (fromStep < 0 || toStep < fromStep)
            throw new InvalidDataException($"checkpoint mutation horizon {fromStep}→{toStep} is invalid");

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        lock (Run.CheckpointWriteGate(run.Dir))
        {
            string path = run.PathOf(Checkpoint.DeltaFileName);
            DeltaHeader header;
            long sequence;
            byte[] previous;
            int count;
            DeltaTail tail;
            if (File.Exists(path))
            {
                using FileStream existing = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                tail = ReadAppendTail(run, existing);
                header = tail.Header;
                // The rail header is the cached keyframe authority.  Cadence
                // appends must not reread/hash the full keyframe; open/rekey
                // validation performs that one physical check before the rail
                // is admitted.  A new keyframe always calls Initialize(), which
                // replaces this rail before the next append.
                if (tail.LastToStep > fromStep)
                    throw new InvalidDataException($"checkpoint mutation horizon regressed: prior {tail.LastToStep}, next {fromStep}");
                sequence = tail.LastSequence + 1;
                previous = tail.LastRecordHash;
                count = tail.RecordCount;
            }
            else
            {
                if (!File.Exists(run.PathOf(Checkpoint.FileName)))
                    throw new InvalidDataException("typed checkpoint mutation requires a canonical keyframe");
                byte[] image = File.ReadAllBytes(run.PathOf(Checkpoint.FileName));
                header = new DeltaHeader(
                    Checkpoint.LogicalStateSHA256(image),
                    Checkpoint.PhysicalSHA256(image),
                    fromStep,
                    0);
                sequence = header.FirstSequence;
                previous = ZeroHash;
                count = 0;
                tail = new DeltaTail(header, 0, header.FirstSequence - 1, fromStep, 0, 0, previous);
            }

            // The excursion sink is intentionally buffered: a row is durable
            // only at the same horizon as its typed receipt.  Flush it before
            // appending that receipt so a kill cannot leave the side stream
            // ahead of the mutation rail.
            reads.FlushCheckpointOutput();
            TapeCheckpointDelta tapeDelta = tape.CaptureCheckpointDelta();
            JournalCheckpointDelta journalDelta = journal.CaptureCheckpointDelta();
            ReadsCheckpointDelta readsDelta = reads.CaptureCheckpointDelta();
            CheckpointReplayContext persistedContext = (replayContext.Present ? replayContext : state.ReplayContext)
                .BindRecord(fromStep, toStep);
            byte[] payload = EncodePayload(tapeDelta, journalDelta, readsDelta, in grammarArtifact,
                state with { ReplayContext = persistedContext });
            byte[] record = EncodeRecord(sequence, fromStep, toStep, payload, previous);
            long railLength;
            long recordOffset;
            using (FileStream stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                recordOffset = stream.Length;
                stream.Position = stream.Length;
                using CkptWriter writer = new(stream);
                if (stream.Length == 0)
                    WriteHeader(writer, header);
                writer.Raw(record);
                writer.Dispose();
                stream.Flush(flushToDisk: true);
                railLength = stream.Length;
            }

            byte[] recordHash = SHA256.HashData(Concat("CORTEXT-MUTATION\0"u8, record));
            DeltaTail nextTail = new(header, count + 1, sequence, toStep, railLength, recordOffset, recordHash);
            WriteTail(run, in nextTail);

            // Do not clear receipt tails until the typed append is durable.
            tape.CommitCheckpointDelta();
            journal.CommitCheckpointLines();
            reads.CommitCheckpointDelta();
            return new CheckpointWriteReceipt(record.LongLength,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }
    }

    /// Start a run with a keyframe and an empty mutation rail.  No synthetic
    /// mutation is emitted for the state already represented by the keyframe.
    internal static void Initialize(Run run, int nextStep)
    {
        string path = run.PathOf(Checkpoint.DeltaFileName);
        if (File.Exists(path)) File.Delete(path);
        string tailPath = run.PathOf(Checkpoint.DeltaTailFileName);
        if (File.Exists(tailPath)) File.Delete(tailPath);
        byte[] image = File.ReadAllBytes(run.PathOf(Checkpoint.FileName));
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        DeltaHeader header = new(Checkpoint.LogicalStateSHA256(image), Checkpoint.PhysicalSHA256(image), nextStep, 0);
        using CkptWriter writer = new(stream);
        WriteHeader(writer, header);
        writer.Dispose();
        stream.Flush(flushToDisk: true);
        DeltaTail tail = new(header, 0, -1, nextStep, stream.Length, stream.Length, ZeroHash);
        WriteTail(run, in tail);
    }

    /// The keyframe is authoritative state.  Mutation records are continuity
    /// receipts, never an alternate image; this keeps resume exact and makes a
    /// corrupt rail fail closed without silently choosing a second authority.
    internal static byte[] LoadEffectiveImage(string runDir)
    {
        lock (Run.CheckpointWriteGate(runDir))
        {
            byte[] baseImage = File.ReadAllBytes(Path.Combine(runDir, Checkpoint.FileName));
            string path = Path.Combine(runDir, Checkpoint.DeltaFileName);
            if (!File.Exists(path)) return baseImage;
            byte[] delta = File.ReadAllBytes(path);
            if (delta.AsSpan().StartsWith("CORTEXT-D1\n"u8)) return ReplayLegacyReplacement(baseImage, delta);
            // The typed rail is a receipt stream; materialize it while the
            // append gate is held so a direct reader cannot observe a prefix.
            using MemoryStream stream = new(delta, writable: false);
            _ = Scan(stream);
            // Synthetic dialect fixtures intentionally use a non-Cortex keyframe;
            // they exercise rail integrity only and have no organs to materialize.
            return Checkpoint.MatchesCurrentSchema(baseImage)
                ? Cortex.MaterializeReadOnlyCheckpoint(runDir)
                : baseImage;
        }
    }

    internal static (byte[] EffectiveImage, string BasePhysicalSHA256, string ChainSHA256) ReadEffectiveSnapshot(string runDir)
    {
        lock (Run.CheckpointWriteGate(runDir))
        {
            string basePath = Path.Combine(runDir, Checkpoint.FileName);
            if (!File.Exists(basePath)) throw new InvalidDataException($"missing checkpoint base: {basePath}");
            CheckpointDeltaAuthority authority = ReadAuthority(runDir);
            byte[] baseImage = File.ReadAllBytes(basePath);
            byte[] image = File.Exists(Path.Combine(runDir, Checkpoint.DeltaFileName))
                && Checkpoint.MatchesCurrentSchema(baseImage)
                ? Cortex.MaterializeReadOnlyCheckpoint(runDir)
                : baseImage;
            string chain = authority.ChainSHA256;
            return (image, authority.BasePhysicalSHA256, chain);
        }
    }

    internal static (string BasePhysicalSHA256, string ChainSHA256) ReadPhysicalAuthority(string runDir)
    {
        CheckpointDeltaAuthority authority = ReadAuthority(runDir);
        return (authority.BasePhysicalSHA256, authority.ChainSHA256);
    }

    internal static CheckpointDeltaAuthority ReadAuthority(string runDir)
    {
        lock (Run.CheckpointWriteGate(runDir))
        {
            string basePath = Path.Combine(runDir, Checkpoint.FileName);
            if (!File.Exists(basePath)) throw new InvalidDataException($"missing checkpoint base: {basePath}");
            byte[] image = File.ReadAllBytes(basePath);
            string physical = Checkpoint.PhysicalSHA256(image);
            int baseStep = Checkpoint.MatchesCurrentSchema(image) ? Checkpoint.PeekNextStep(image) : -1;
            string path = Path.Combine(runDir, Checkpoint.DeltaFileName);
            if (!File.Exists(path)) return new(physical, ChainSeed(physical), Math.Max(0, baseStep), Math.Max(0, baseStep), 0);
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ScanResult scan = Scan(stream);
            if (!string.Equals(scan.Header.BasePhysicalSHA256, physical, StringComparison.Ordinal))
                throw new InvalidDataException("typed checkpoint mutation rail is not bound to the current keyframe");
            if (!string.Equals(scan.Header.BaseLogicalSHA256, Checkpoint.LogicalStateSHA256(image), StringComparison.Ordinal))
                throw new InvalidDataException("typed checkpoint mutation rail is not bound to the current keyframe logical state");
            if (baseStep >= 0 && scan.Header.BaseStep != baseStep)
                throw new InvalidDataException("typed checkpoint mutation rail base horizon disagrees with the keyframe");
            return new(physical, scan.ChainSHA256, scan.Header.BaseStep, scan.LastToStep, scan.RecordCount);
        }
    }

    /// Fold every typed mutation after the canonical keyframe into caller-owned
    /// organs. The rail is scanned to completion before the first mutation is
    /// applied, so torn writes, chain gaps, checksum failures, and unsupported
    /// payload versions fail closed without a half-replayed prefix.
    internal static CheckpointDeltaReplayReceipt ReplayInto(
        string runDir,
        Tape tape,
        Journal journal,
        Reads reads,
        ICurriculum curriculum,
        SelfStream selfStream,
        WeightController controller,
        Metabolism metabolism,
        MemoryHierarchy memory,
        Homeostat homeostat,
        Rhythm rhythm,
        Cortex cortex,
        Loom? loom)
    {
        ArgumentNullException.ThrowIfNull(runDir);
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(selfStream);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(metabolism);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(homeostat);
        ArgumentNullException.ThrowIfNull(rhythm);
        ArgumentNullException.ThrowIfNull(cortex);
        // Delta replay is also a resume boundary for callers that materialize
        // an effective image outside Checkpoint.LoadBody. Keep runtime-owned
        // curricula bound to the exact restored authorities before the first
        // mutation is applied; ordinary curricula retain the no-op default.
        curriculum.BindRuntimeTape(tape, journal);

        string path = Path.Combine(runDir, Checkpoint.DeltaFileName);
        if (!File.Exists(path)) return default;
        string physical = Checkpoint.PhysicalSHA256(File.ReadAllBytes(Path.Combine(runDir, Checkpoint.FileName)));
        // Replay runs before the drive rewrites config.txt (the materialized
        // child directory starts with only its seed marker and checkpoint).
        // The checkpoint config is therefore the container authority during
        // this pass; config.txt is only a later human-facing receipt.
        string persistedConfigDigest = ReadPersistedConfigDigest(runDir);
        if (persistedConfigDigest.Length == 0)
        {
            string checkpointPath = Path.Combine(runDir, Checkpoint.FileName);
            if (File.Exists(checkpointPath))
                persistedConfigDigest = Cortex.PersistedConfigDigest(
                    Checkpoint.PeekConfig(File.ReadAllBytes(checkpointPath)));
        }
        List<DecodedMutation> mutations = new();
        ScanResult rail;
        lock (Run.CheckpointWriteGate(runDir))
        {
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using CkptReader reader = new(stream);
                if (!IsMagic(reader.RawExact(Magic.Length)))
                    throw new InvalidDataException("unknown typed checkpoint mutation dialect");
                DeltaHeader header = new(reader.Str(), reader.Str(), reader.I32(), reader.I64());
                if (header.BaseStep < 0 || header.FirstSequence < 0)
                    throw new InvalidDataException("typed mutation header is malformed");
                if (!string.Equals(header.BasePhysicalSHA256, physical, StringComparison.Ordinal))
                    throw new InvalidDataException("typed checkpoint mutation rail is not bound to the current keyframe");

                byte[] previous = ZeroHash;
                long expectedSequence = header.FirstSequence;
                int count = 0;
                int lastToStep = header.BaseStep;
                long lastRecordOffset = stream.Position;
                using IncrementalHash chain = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                chain.AppendData("CORTEXT-MUTATION-CHAIN\0"u8);
                chain.AppendData(Encoding.UTF8.GetBytes(header.BasePhysicalSHA256));
                while (reader.RemainingBytes > 0)
                {
                    lastRecordOffset = stream.Position;
                    long sequence = reader.I64();
                    int fromStep = reader.I32();
                    int toStep = reader.I32();
                    int length = reader.I32();
                    if (length < 0 || length > 64 * 1024 * 1024)
                        throw new InvalidDataException("typed mutation payload exceeds bound");
                    byte[] payload = reader.RawExact(length);
                    byte[] payloadHash = reader.RawExact(HashLength);
                    byte[] prior = reader.RawExact(HashLength);
                    if (sequence != expectedSequence || fromStep != lastToStep || toStep < fromStep
                        || !prior.AsSpan().SequenceEqual(previous))
                        throw new InvalidDataException($"typed mutation horizon/chain gap at sequence {sequence}");
                    if (!SHA256.HashData(payload).AsSpan().SequenceEqual(payloadHash))
                        throw new InvalidDataException("typed mutation payload checksum failed");
                    byte[] body = EncodeRecord(sequence, fromStep, toStep, payload, prior);
                    byte[] recordHash = SHA256.HashData(Concat("CORTEXT-MUTATION\0"u8, body));
                    chain.AppendData(recordHash);
                    DecodedMutation decoded = DecodePayload(payload);
                    if (decoded.Snap.Present && decoded.Snap.NextStep != toStep)
                        throw new InvalidDataException($"CortexSnap mutation anchor {decoded.Snap.NextStep} disagrees with record horizon {toStep}");
                    mutations.Add(decoded with
                    {
                        FromStep = fromStep,
                        ToStep = toStep,
                        ReplayContext = decoded.ReplayContext.BindRecord(fromStep, toStep).BindRail(
                            header.BasePhysicalSHA256, header.BaseLogicalSHA256, Path.GetFileName(runDir),
                            sequence, Convert.ToHexStringLower(previous), Convert.ToHexStringLower(recordHash)),
                    });
                    previous = recordHash;
                    expectedSequence++;
                    lastToStep = toStep;
                    if (++count > MaxRecords)
                        throw new InvalidDataException("typed mutation record count exceeds bound");
                }
                rail = new(header, count, expectedSequence - 1, lastToStep,
                    count == 0 ? stream.Position : lastRecordOffset, previous,
                    Convert.ToHexStringLower(chain.GetHashAndReset()));
            }
        }

        LoopLineageTurnstile? lineage = cortex.LoopLineage;
        CortexSnap? latestSnap = null;
        Trace.Cortex.Boundary("checkpoint.replay.cursor",
            $"phase=before records={mutations.Count} tape={tape.MutationCursor} loom_mark={(loom is null ? -1 : loom.SpliceIDMark)} loom_revision={(loom is null ? -1 : loom.MutationRevision)} loom_arena={(loom is null ? -1 : loom.LiveSymbols)}");
        void TraceReplayCursor(int recordIndex, string phase)
            => Trace.Cortex.Boundary("checkpoint.replay.cursor",
                $"phase={phase} record={recordIndex} tape={tape.MutationCursor} loom_mark={(loom is null ? -1 : loom.SpliceIDMark)} loom_revision={(loom is null ? -1 : loom.MutationRevision)} loom_arena={(loom is null ? -1 : loom.LiveSymbols)}");
        // Reject semantic shapes that this replay surface cannot mount before
        // touching any organ. Structural corruption was already rejected by
        // Scan; this pass keeps unsupported curriculum/loom/lineage arms from
        // leaving a partially applied prefix behind.
        bool loomNeedsRebuild = false;
        int replayIndex = 0;
        int previousInheritedHop = -1;
        bool sawContainerOrigin = false;
        CopiedPrefixAuthority? copiedAuthority = null;
        foreach (DecodedMutation mutation in mutations)
        {
            if (mutation.ReplayContext.Present)
            {
                mutation.ReplayContext.ValidateForRecord(mutation.FromStep, mutation.ToStep);
                if (mutation.ReplayContext.ContainerRunID != Path.GetFileName(Path.GetFullPath(runDir)))
                    throw new InvalidDataException("typed mutation replay context belongs to a different container run");
                CheckpointReplayContext replayContext = mutation.ReplayContext;
                int inheritedHop = ValidateReplayConfigAuthority(runDir, persistedConfigDigest, in replayContext, mutation.ToStep, ref copiedAuthority);
                if (inheritedHop < 0)
                    sawContainerOrigin = true;
                else
                {
                    if (sawContainerOrigin || inheritedHop < previousInheritedHop)
                        throw new InvalidDataException($"typed mutation copied prefix is not a contiguous ancestry prefix: run={runDir} hop={inheritedHop} previous={previousInheritedHop} child_origin={sawContainerOrigin}");
                    previousInheritedHop = inheritedHop;
                }
            }
            if (mutation.HasLoom && loom is null)
                throw new InvalidDataException("typed mutation carries loom state but runtime loom is unarmed");
            if (loom is not null && (!mutation.HasState || !mutation.HasLoom))
                throw new InvalidDataException("typed mutation omits armed loom state");
            if (mutation.Lineage.Receipts is { Length: > 0 } && lineage is null)
                throw new InvalidDataException("typed mutation carries loop-lineage state but runtime lineage is unarmed");
            if (mutation.LoopClosureLinks is { Length: > 0 } && lineage is null)
                throw new InvalidDataException("typed mutation carries native loop-link state but runtime lineage is unarmed");
            if (mutation.Curriculum is not null && curriculum is not ICurriculumCheckpointDeltaOwner)
                throw new InvalidDataException($"runtime curriculum {curriculum.GetType().Name} does not own checkpoint mutations");
        }
        foreach (DecodedMutation mutation in mutations)
        {
            int recordIndex = replayIndex++;
            if (mutation.Curriculum is { } curriculumDelta)
            {
                CheckpointReplayContext replayContext = mutation.ReplayContext;
                ((ICurriculumCheckpointDeltaOwner)curriculum).ApplyCheckpointDelta(curriculumDelta, in replayContext);
            }
            TapeCheckpointDelta tapeDelta = mutation.Tape;
            JournalCheckpointDelta journalDelta = mutation.Journal;
            ReadsCheckpointDelta readsDelta = mutation.Reads;
            bool rebuildBeforeRecord = loomNeedsRebuild;
            bool stageTapeForLoom = mutation.HasLoom && loom is not null && !rebuildBeforeRecord;
            if (mutation.HasLoom)
            {
                HashSet<long> evacuated = new(tapeDelta.Shed.Length + tapeDelta.Dropped.Length);
                foreach (TapeCheckpointEvacuation entry in tapeDelta.Shed) evacuated.Add(entry.ID.Value);
                foreach (TapeCheckpointEvacuation entry in tapeDelta.Dropped) evacuated.Add(entry.ID.Value);
                List<long> appendedAndEvacuated = [];
                foreach (TapeCheckpointAppend entry in tapeDelta.Appended)
                    if (evacuated.Contains(entry.ID.Value)) appendedAndEvacuated.Add(entry.ID.Value);
                Trace.Cortex.Boundary("checkpoint.replay.tape-transition",
                    $"record={recordIndex} horizon={mutation.FromStep}->{mutation.ToStep} appended={tapeDelta.Appended.Length} reflected={tapeDelta.Mutation.Reflected.Length} shed={tapeDelta.Shed.Length} dropped={tapeDelta.Dropped.Length} residents_before={tape.ResidentEventIDs.Count()} staged={stageTapeForLoom} append_evac={string.Join(',', appendedAndEvacuated)}");
            }
            if (stageTapeForLoom)
            {
                TapeCheckpointDelta residency = tapeDelta with
                {
                    Mutation = tapeDelta.Mutation with { OrderRevision = TapeRevision.Initial, Shed = [], Dropped = [] },
                    Shed = [], Dropped = [], ReorderEdits = [], Reordered = false,
                };
                tape.ApplyCheckpointDelta(in residency);
            }
            else
            {
                tape.ApplyCheckpointDelta(in tapeDelta);
            }
            journal.ApplyCheckpointDelta(in journalDelta);
            reads.ApplyCheckpointDelta(in readsDelta);
            if (!mutation.HasState)
            {
                TraceReplayCursor(recordIndex, "after-tape");
                continue;
            }
            SelfStreamCheckpointDelta selfDelta = mutation.SelfStream;
            WeightController.WeightControllerCheckpointDelta controllerDelta = mutation.Controller;
            Metabolism.MetabolismCheckpointDelta metabolismDelta = mutation.Metabolism;
            MemoryCheckpointDelta memoryDelta = mutation.Memory;
            HomeostatCheckpointDelta homeostatDelta = mutation.Homeostat;
            Rhythm.RhythmCheckpointDelta rhythmDelta = mutation.Rhythm;
            CortexPolicyCheckpointDelta policyDelta = mutation.Policy;
            selfStream.ApplyCheckpointDelta(in selfDelta);
            controller.ApplyCheckpointDelta(in controllerDelta);
            metabolism.ApplyCheckpointDelta(in metabolismDelta);
            memory.ApplyCheckpointDelta(in memoryDelta, tape);
            homeostat.ApplyCheckpointDelta(in homeostatDelta);
            rhythm.ApplyCheckpointDelta(in rhythmDelta);
            cortex.ApplyPolicyCheckpointDelta(in policyDelta);
            if (mutation.Lineage.Receipts is { Length: > 0 })
            {
                LoopLineageTurnstile.LoopLineageCheckpointDelta lineageDelta = mutation.Lineage;
                lineage!.ApplyCheckpointDelta(in lineageDelta);
            }
            if (mutation.LoopClosureLinks is { Length: > 0 })
                cortex.ApplyLoopClosureLinkCheckpointDelta(mutation.LoopClosureLinks, tape, journal);
            if (mutation.HasLoom)
            {
                Cogito.Induct.LoomCheckpointDelta loomDelta = mutation.Loom;
                if (loomNeedsRebuild)
                {
                    // A reset/resplice discards the old arena. Fold only the
                    // standing rank journal until the final tape is present;
                    // one exact reparse below materializes the replacement.
                    loom!.ApplyCheckpointDelta(in loomDelta);
                    if (stageTapeForLoom)
                    {
                        TapeCheckpointDelta evacuation = tapeDelta with
                        {
                            Mutation = tapeDelta.Mutation with { Appended = [], Reflected = [] },
                            Appended = [],
                        };
                        tape.ApplyCheckpointDelta(in evacuation);
                    }
                    TraceReplayCursor(recordIndex, "after-rebuild-stage");
                    continue;
                }

                if (loomDelta.Reset)
                {
                    loom!.ApplyCheckpointDelta(in loomDelta);
                    loomNeedsRebuild = true;
                    if (stageTapeForLoom)
                    {
                        TapeCheckpointDelta evacuation = tapeDelta with
                        {
                            Mutation = tapeDelta.Mutation with { Appended = [], Reflected = [] },
                            Appended = [],
                        };
                        tape.ApplyCheckpointDelta(in evacuation);
                    }
                    TraceReplayCursor(recordIndex, "after-reset");
                    continue;
                }

                if (loomDelta.Entries.Any(static entry => entry.Alias))
                {
                    // Alias installation is emitted by the rebase/compaction
                    // path. It changes the rank program without carrying the
                    // arena rewrite sites, so treat it as an explicit
                    // resplice boundary rather than leaving a stale arena.
                    loom!.ApplyCheckpointDelta(in loomDelta);
                    loomNeedsRebuild = true;
                    TraceReplayCursor(recordIndex, "after-alias");
                    continue;
                }

                // Tape spans must be rank-encoded through the pre-mutation
                // program. Apply the physical tape transition first, while
                // preserving the checkpoint revision, then replay each minted
                // rule against the resulting arena in emission order.
                TapeDelta loomTapeDelta = tapeDelta.Mutation;
                loom!.ApplyTapeDeltaForCheckpoint(tape, in loomTapeDelta);
                loom.ApplyCheckpointDelta(in loomDelta, applyArenaEntries: true);
                if (stageTapeForLoom)
                {
                    TapeCheckpointDelta evacuation = tapeDelta with
                    {
                        Mutation = tapeDelta.Mutation with { Appended = [], Reflected = [] },
                        Appended = [],
                    };
                    tape.ApplyCheckpointDelta(in evacuation);
                }
            }
            TraceReplayCursor(recordIndex, "after");
        }
        if (loomNeedsRebuild)
        {
            loom!.RebuildFromTape(tape, loom.SpliceIDMark);
            Trace.Cortex.Boundary("checkpoint.replay.loom", $"records={mutations.Count} id_mark={loom.SpliceIDMark} arena={loom.LiveSymbols} · reset/alias rank replacement folded then parsed once");
        }
        GrammarArtifactDelta latestGrammar = default;
        foreach (DecodedMutation mutation in mutations)
        {
            if (!mutation.Grammar.IsEmpty) latestGrammar = mutation.Grammar;
            if (mutation.Snap.Present) latestSnap = mutation.Snap.ToSnapshot();
        }
        return new(rail.RecordCount, rail.LastToStep, latestGrammar, latestSnap);
    }

    /// Compaction is explicit and bounded: callers first land a fresh keyframe,
    /// then invoke this verb to retire the receipt tail.  There is no hidden
    /// size-triggered full-image rewrite in the mutation append path.
    internal static void Compact(string runDir)
    {
        lock (Run.CheckpointWriteGate(runDir))
        {
            string path = Path.Combine(runDir, Checkpoint.DeltaFileName);
            string tailPath = Path.Combine(runDir, Checkpoint.DeltaTailFileName);
            if (!File.Exists(path))
            {
                if (File.Exists(tailPath)) File.Delete(tailPath);
                return;
            }
            CheckpointDeltaAuthority authority = ReadAuthority(runDir);
            string basePath = Path.Combine(runDir, Checkpoint.FileName);
            byte[] baseImage = File.ReadAllBytes(basePath);
            if (Checkpoint.MatchesCurrentSchema(baseImage))
            {
                byte[] effective = Cortex.MaterializeReadOnlyCheckpoint(runDir);
                if (Checkpoint.PeekNextStep(effective) != authority.LastToStep)
                    throw new InvalidDataException("checkpoint compaction materialized the wrong terminal horizon");
                Run.Open(runDir).WriteAtomic(Checkpoint.FileName, stream => stream.Write(effective));
            }
            // Synthetic rail fixtures have no Cortex organs to materialize; the
            // authenticated scan above still proves their chain before retire.
            File.Delete(path);
            if (File.Exists(tailPath)) File.Delete(tailPath);
        }
    }

    internal static string ChainSHA256ForImage(ReadOnlySpan<byte> image)
        => ChainSeed(Checkpoint.PhysicalSHA256(image));

    private static string ReadPersistedConfigDigest(string runDir)
    {
        string path = Path.Combine(runDir, "config.txt");
        if (!File.Exists(path)) return "";
        foreach (string line in File.ReadLines(path))
            if (line.StartsWith("persisted_config_digest=", StringComparison.Ordinal))
                return line["persisted_config_digest=".Length..].Trim();
        return "";
    }

    /// The loop-invariant slice of copied-prefix replay authority: the parsed
    /// seed-load intent, its ancestry index, and the rail base image hashes.
    /// ReplayInto validates every mutation record; without this hoist each
    /// record re-read and re-canonicalized the identical intent/receipt files
    /// and re-hashed the identical checkpoint base.
    private sealed class CopiedPrefixAuthority
    {
        public required CortexForkSeedLoadReceipt Receipt { get; init; }
        public required CortexForkAdoptionHop[] Ancestry { get; init; }
        public required Dictionary<string, int> HopIndexByOrigin { get; init; }
        public required string BasePhysicalSHA256 { get; init; }
        public required string BaseLogicalSHA256 { get; init; }
    }

    private static CopiedPrefixAuthority LoadCopiedPrefixAuthority(string runDir, string originRunID)
    {
        // A copied prefix may contain records written under several ancestor
        // config epochs. Its seed-load intent is the immutable custody receipt
        // for that whole prefix; comparing every ancestor record to the mutable
        // child config would erase that provenance and reject valid replays.
        string intentPath = Path.Combine(runDir, "seed-load-intent.ron");
        if (!File.Exists(intentPath))
            intentPath = Path.Combine(runDir, "seed-load-receipt.ron");
        if (!File.Exists(intentPath))
        {
            throw new InvalidDataException($"copied typed mutation prefix is missing its seed-load intent/receipt: {runDir} origin={originRunID}");
        }
        CortexForkSeedLoadRailDocument intentDocument = CortexForkTerminalRunReceipt.ReadSeedRailDocument(intentPath);
        CortexForkSeedLoadReceipt receipt = intentDocument.Receipt;
        CortexForkAdoptionHop[] ancestry = receipt.AdoptionAncestry ?? [];
        Dictionary<string, int> hopIndexByOrigin = new(ancestry.Length, StringComparer.Ordinal);
        for (int i = 0; i < ancestry.Length; i++)
            if (ancestry[i].ChildRunID.Length > 0 && !hopIndexByOrigin.ContainsKey(ancestry[i].OriginRunID))
                hopIndexByOrigin.Add(ancestry[i].OriginRunID, i);

        string checkpointPath = Path.Combine(runDir, Checkpoint.FileName);
        if (!File.Exists(checkpointPath))
            throw new InvalidDataException("copied typed mutation prefix is missing its checkpoint base");
        byte[] checkpointBase = File.ReadAllBytes(checkpointPath);

        string receiptPath = Path.Combine(runDir, "seed-load-receipt.ron");
        if (File.Exists(receiptPath) && !string.Equals(receiptPath, intentPath, StringComparison.Ordinal))
        {
            CortexForkSeedLoadRailDocument landedDocument = CortexForkTerminalRunReceipt.ReadSeedRailDocument(receiptPath);
            if (landedDocument.StoredBindingDigest != intentDocument.StoredBindingDigest)
                throw new InvalidDataException("copied typed mutation seed-load intent disagrees with its landed receipt");
        }
        return new CopiedPrefixAuthority
        {
            Receipt = receipt,
            Ancestry = ancestry,
            HopIndexByOrigin = hopIndexByOrigin,
            BasePhysicalSHA256 = Checkpoint.PhysicalSHA256(checkpointBase),
            BaseLogicalSHA256 = Checkpoint.LogicalStateSHA256(checkpointBase),
        };
    }

    private static int ValidateReplayConfigAuthority(
        string runDir,
        string containerConfigDigest,
        in CheckpointReplayContext context,
        int recordToStep,
        ref CopiedPrefixAuthority? copiedAuthority)
    {
        if (!IsDigest(context.ConfigDigest))
            throw new InvalidDataException("typed mutation replay context has no authenticated config digest");

        string containerRunID = Path.GetFileName(Path.GetFullPath(runDir));
        if (string.Equals(context.RailRunID, containerRunID, StringComparison.Ordinal))
        {
            if (containerConfigDigest.Length == 0 || context.ConfigDigest != containerConfigDigest)
                throw new InvalidDataException("typed mutation replay context disagrees with persisted config authority");
            return -1;
        }

        copiedAuthority ??= LoadCopiedPrefixAuthority(runDir, context.RailRunID);
        CortexForkSeedLoadReceipt receipt = copiedAuthority.Receipt;
        CortexForkAdoptionHop[] ancestry = copiedAuthority.Ancestry;
        int hopIndex = copiedAuthority.HopIndexByOrigin.TryGetValue(context.RailRunID, out int found) ? found : -1;
        if (!receipt.Bound || !receipt.Exact
            || receipt.ChildRunID != containerRunID
            || receipt.SourceRunID != (ancestry.Length == 0 ? "" : ancestry[^1].OriginRunID)
            || ancestry.Length == 0
            || ancestry[^1].ChildRunID != containerRunID
            || hopIndex < 0
            || ancestry[hopIndex].SourceNextStep < recordToStep
            || receipt.SourceRunID.Length == 0
            || receipt.CheckpointProof.PersistedConfigDigest != receipt.PersistedConfigDigest
            || receipt.CheckpointProof.NextStep != receipt.SourceNextStep
            || receipt.CheckpointProof.EffectiveImageSHA256 != receipt.LoadedCheckpointSHA256
            || ancestry[^1].SourceSeedDigest != receipt.SourceSeedDigest
            || context.ConfigDigest != receipt.PersistedConfigDigest
            || context.BasePhysicalSHA256 != receipt.CheckpointProof.BasePhysicalSHA256
            || context.ConfigDigest != ancestry[hopIndex].PersistedConfigDigest
            || context.BasePhysicalSHA256 != ancestry[hopIndex].BasePhysicalSHA256)
            throw new InvalidDataException($"copied typed mutation seed-load intent does not authenticate its prefix horizon: child={receipt.ChildRunID}/{containerRunID} source={receipt.SourceRunID}/{context.RailRunID} to={recordToStep} source_next={receipt.SourceNextStep} hop={hopIndex}/{ancestry.Length} hop_child={(hopIndex < 0 ? "" : ancestry[hopIndex].ChildRunID)} cfg={context.ConfigDigest}/{receipt.PersistedConfigDigest}/{(hopIndex < 0 ? "" : ancestry[hopIndex].PersistedConfigDigest)} base={context.BasePhysicalSHA256}/{receipt.CheckpointProof.BasePhysicalSHA256}/{(hopIndex < 0 ? "" : ancestry[hopIndex].BasePhysicalSHA256)} proof={receipt.CheckpointProof.PersistedConfigDigest}/{receipt.CheckpointProof.NextStep} bound={receipt.Bound} exact={receipt.Exact}");

        if (copiedAuthority.BasePhysicalSHA256 != context.BasePhysicalSHA256
            || copiedAuthority.BaseLogicalSHA256 != context.BaseLogicalSHA256)
            throw new InvalidDataException("copied typed mutation replay context disagrees with its rail base image");
        return hopIndex;
    }

    private static bool IsDigest(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static byte[] EncodePayload(in TapeCheckpointDelta tape, in JournalCheckpointDelta journal, in ReadsCheckpointDelta reads, in GrammarArtifactDelta grammar, in CheckpointMutationState state)
    {
        using MemoryStream stream = new();
        using CkptWriter writer = new(stream);
        // Version 7 adds the append-only native loop-link rail. Version 6 seals the curriculum body as (kind,payload), so it cannot
        // be reinterpreted as the old enum-plus-typed body. Version 5 remains
        // readable through the explicit legacy reader below. Version 5 adds
        // the authenticated execution-window replay context and an explicit replay kind. Version 4 carried only the window; versions
        // 2/3 use each stream's own typed envelope. Version 1 wrote
        // Journal/Reads by hand and silently discarded Reads' rolling windows,
        // so it cannot be replayed into a live continuation.
        writer.U8(7);
        Tape.WriteCheckpointDelta(writer, in tape);
        Journal.WriteCheckpointDelta(writer, in journal);
        Reads.WriteCheckpointDelta(writer, in reads);
        writer.U8(grammar.IsEmpty ? (byte)0 : (byte)1);
        if (!grammar.IsEmpty) { writer.U64(grammar.Revision); writer.Str(grammar.FileName); writer.Str(grammar.SHA256); }
        writer.Bool(state.Snap.Present);
        if (state.Snap.Present)
        {
            CortexSnapCheckpointDelta snap = state.Snap;
            WriteCortexSnapCheckpointDelta(writer, in snap);
        }
        writer.Bool(state.ReplayContext.Present);
        if (state.ReplayContext.Present)
        {
            writer.I32(state.ReplayContext.WindowStartStep);
            writer.I32(state.ReplayContext.WindowEndStep);
            writer.U8((byte)state.ReplayContext.Kind);
            writer.Str(state.ReplayContext.ConfigDigest ?? "");
            writer.Str(state.ReplayContext.RailRunID ?? "");
            writer.Str(state.ReplayContext.PredecessorCurveDigest ?? "");
            writer.Str(state.ReplayContext.PredecessorParentPointID ?? "");
            writer.Str(state.ReplayContext.SuccessorRunID ?? "");
            writer.Str(state.ReplayContext.SuccessorConfigID ?? "");
            writer.Str(state.ReplayContext.SuccessorChainID ?? "");
            writer.Str(state.ReplayContext.SuccessorArmID ?? "");
            writer.Str(state.ReplayContext.ScheduleDigest ?? "");
        }
        LoopClosureLinkAttempt[] loopClosureLinks = state.LoopClosureLinks ?? Array.Empty<LoopClosureLinkAttempt>();
        bool hasState = state.SelfStream.Events is not null
            || !state.Homeostat.IsEmpty
            || state.Curriculum is not null
            || state.HasLoom
            || loopClosureLinks.Length > 0;
        writer.U8(hasState ? (byte)1 : (byte)0);
        if (hasState)
        {
            SelfStreamCheckpointDelta self = state.SelfStream;
            WeightController.WeightControllerCheckpointDelta controller = state.Controller;
            Metabolism.MetabolismCheckpointDelta metabolism = state.Metabolism;
            MemoryCheckpointDelta memory = state.Memory;
            HomeostatCheckpointDelta homeostat = state.Homeostat;
            Rhythm.RhythmCheckpointDelta rhythm = state.Rhythm;
            CortexPolicyCheckpointDelta policy = state.Policy;
            LoopLineageTurnstile.LoopLineageCheckpointDelta lineage = state.Lineage;
            SelfStream.WriteCheckpointDelta(writer, in self);
            WeightController.WriteCheckpointDelta(writer, in controller);
            Metabolism.WriteCheckpointDelta(writer, in metabolism);
            MemoryHierarchy.WriteCheckpointDelta(writer, in memory);
            Homeostat.WriteCheckpointDelta(writer, in homeostat);
            Rhythm.WriteCheckpointDelta(writer, in rhythm);
            Cortex.WriteCheckpointDelta(writer, in policy);
            LoopLineageTurnstile.WriteCheckpointDelta(writer, in lineage);
            bool hasLoom = state.HasLoom;
            writer.Bool(hasLoom);
            if (hasLoom) { Cogito.Induct.LoomCheckpointDelta loom = state.Loom; Cogito.Induct.Loom.WriteCheckpointDelta(writer, in loom); }
            bool hasCurriculum = state.Curriculum is not null;
            writer.Bool(hasCurriculum);
            if (hasCurriculum)
            {
                WriteCurriculumCheckpointDelta(writer, state.Curriculum!);
            }
            writer.I32(loopClosureLinks.Length);
            foreach (LoopClosureLinkAttempt attempt in loopClosureLinks)
                writer.Bytes(LoopClosureLinkAttemptStore.EncodeCheckpoint(in attempt));
        }
        writer.Dispose();
        return stream.ToArray();
    }

    private static byte[] EncodeRecord(long sequence, int fromStep, int toStep, byte[] payload, byte[] previous)
    {
        byte[] payloadHash = SHA256.HashData(payload);
        using MemoryStream stream = new();
        using CkptWriter writer = new(stream);
        writer.I64(sequence); writer.I32(fromStep); writer.I32(toStep); writer.I32(payload.Length); writer.Raw(payload);
        writer.Raw(payloadHash); writer.Raw(previous);
        writer.Dispose();
        return stream.ToArray();
    }

    private static void WriteHeader(CkptWriter writer, in DeltaHeader header)
    {
        writer.Raw(Magic); writer.Str(header.BaseLogicalSHA256); writer.Str(header.BasePhysicalSHA256);
        writer.I32(header.BaseStep); writer.I64(header.FirstSequence);
    }

    /// The append path trusts only this authenticated tail index.  It is a tiny
    /// sibling of the mutation rail, written atomically after the rail record is
    /// flushed.  A crash between those two landings leaves a complete rail suffix
    /// to recover; a torn suffix or forged index fails closed.
    private static DeltaTail ReadAppendTail(Run run, FileStream rail)
    {
        string tailPath = run.PathOf(Checkpoint.DeltaTailFileName);
        if (!File.Exists(tailPath))
        {
            rail.Position = 0;
            ScanResult scan = Scan(rail);
            DeltaTail recovered = new(scan.Header, scan.RecordCount, scan.LastSequence, scan.LastToStep, rail.Length, scan.LastRecordOffset, scan.LastRecordHash);
            WriteTail(run, in recovered);
            return recovered;
        }

        DeltaTail tail = ReadTail(tailPath);
        DeltaHeader tailHeader = tail.Header;
        ValidateTailHeader(rail, in tailHeader);
        if (tail.RailLength > rail.Length)
            throw new InvalidDataException("checkpoint mutation tail is ahead of the rail");
        if (tail.RailLength < rail.Length)
        {
            ScanResult suffix = ScanSuffix(rail, tail.RailLength, tail.LastSequence + 1, tail.LastToStep, tail.LastRecordHash);
            tail = new(tail.Header, tail.RecordCount + suffix.RecordCount, suffix.LastSequence, suffix.LastToStep, rail.Length, suffix.LastRecordOffset, suffix.LastRecordHash);
            WriteTail(run, in tail);
        }
        if (tail.RecordCount > 0) ValidateTailRecord(rail, in tail);
        return tail;
    }

    private static void ValidateTailHeader(FileStream rail, in DeltaHeader expected)
    {
        rail.Position = 0;
        using CkptReader reader = new(rail);
        if (!IsMagic(reader.RawExact(Magic.Length)))
            throw new InvalidDataException("unknown typed checkpoint mutation dialect");
        DeltaHeader actual = new(reader.Str(), reader.Str(), reader.I32(), reader.I64());
        if (!string.Equals(actual.BaseLogicalSHA256, expected.BaseLogicalSHA256, StringComparison.Ordinal)
            || !string.Equals(actual.BasePhysicalSHA256, expected.BasePhysicalSHA256, StringComparison.Ordinal)
            || actual.BaseStep != expected.BaseStep || actual.FirstSequence != expected.FirstSequence)
            throw new InvalidDataException("checkpoint mutation tail is not bound to the rail header");
    }

    private static void ValidateTailRecord(FileStream rail, in DeltaTail tail)
    {
        rail.Position = tail.LastRecordOffset;
        using CkptReader reader = new(rail);
        long sequence = reader.I64();
        int fromStep = reader.I32();
        int toStep = reader.I32();
        int length = reader.I32();
        if (length < 0 || length > 64 * 1024 * 1024)
            throw new InvalidDataException("typed mutation tail payload exceeds bound");
        byte[] payload = reader.RawExact(length);
        byte[] payloadHash = reader.RawExact(HashLength);
        byte[] prior = reader.RawExact(HashLength);
        DecodedMutation decoded = DecodePayload(payload);
        if (decoded.Snap.Present && decoded.Snap.NextStep != toStep)
            throw new InvalidDataException($"CortexSnap mutation anchor {decoded.Snap.NextStep} disagrees with record horizon {toStep}");
        byte[] body = EncodeRecord(sequence, fromStep, toStep, payload, prior);
        byte[] actual = SHA256.HashData(Concat("CORTEXT-MUTATION\0"u8, body));
        if (sequence != tail.LastSequence || toStep != tail.LastToStep
            || !SHA256.HashData(payload).AsSpan().SequenceEqual(payloadHash)
            || !actual.AsSpan().SequenceEqual(tail.LastRecordHash)
            || rail.Position != tail.RailLength)
            throw new InvalidDataException("checkpoint mutation tail does not authenticate the rail suffix");
    }

    private static void WriteTail(Run run, in DeltaTail tail)
    {
        byte[] body;
        using (MemoryStream stream = new())
        {
            using CkptWriter writer = new(stream);
            writer.Raw(TailMagic);
            writer.Str(tail.Header.BaseLogicalSHA256);
            writer.Str(tail.Header.BasePhysicalSHA256);
            writer.I32(tail.Header.BaseStep);
            writer.I64(tail.Header.FirstSequence);
            writer.I64(tail.RailLength);
            writer.I64(tail.LastRecordOffset);
            writer.I32(tail.RecordCount);
            writer.I64(tail.LastSequence);
            writer.I32(tail.LastToStep);
            writer.Raw(tail.LastRecordHash);
            writer.Dispose();
            body = stream.ToArray();
        }
        byte[] digest = SHA256.HashData(body);
        run.WriteAtomic(Checkpoint.DeltaTailFileName, output =>
        {
            output.Write(body);
            output.Write(digest);
        });
    }

    private static DeltaTail ReadTail(string path)
    {
        byte[] encoded = File.ReadAllBytes(path);
        if (encoded.Length <= HashLength)
            throw new InvalidDataException("checkpoint mutation tail is truncated");
        ReadOnlySpan<byte> body = encoded.AsSpan(0, encoded.Length - HashLength);
        ReadOnlySpan<byte> digest = encoded.AsSpan(encoded.Length - HashLength, HashLength);
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(digest))
            throw new InvalidDataException("checkpoint mutation tail authentication failed");
        using MemoryStream stream = new(body.ToArray(), writable: false);
        using CkptReader reader = new(stream);
        if (!IsTailMagic(reader.RawExact(TailMagic.Length)))
            throw new InvalidDataException("unknown checkpoint mutation tail dialect");
        DeltaHeader header = new(reader.Str(), reader.Str(), reader.I32(), reader.I64());
        long railLength = reader.I64();
        long lastRecordOffset = reader.I64();
        int count = reader.I32();
        long lastSequence = reader.I64();
        int lastToStep = reader.I32();
        byte[] lastHash = reader.RawExact(HashLength);
        if (reader.RemainingBytes != 0 || header.BaseStep < 0 || header.FirstSequence < 0 || railLength < 0 || lastRecordOffset < 0 || lastRecordOffset > railLength || count < 0 || count > MaxRecords
            || lastSequence < header.FirstSequence - 1 || lastToStep < header.BaseStep
            || (count == 0 && lastSequence != header.FirstSequence - 1)
            || (count == 0 && lastToStep != header.BaseStep)
            || (count > 0 && lastSequence != header.FirstSequence + count - 1))
            throw new InvalidDataException("checkpoint mutation tail metadata is malformed");
        if (count == 0 && !lastHash.AsSpan().SequenceEqual(ZeroHash))
            throw new InvalidDataException("empty checkpoint mutation tail carries a record hash");
        if (count == 0 && lastRecordOffset != railLength)
            throw new InvalidDataException("empty checkpoint mutation tail carries a record offset");
        return new(header, count, lastSequence, lastToStep, railLength, lastRecordOffset, lastHash);
    }

    private static ScanResult ScanSuffix(FileStream rail, long offset, long expectedSequence, int lastToStep, byte[] previous)
    {
        if (offset < 0 || offset > rail.Length) throw new InvalidDataException("checkpoint mutation tail offset is outside the rail");
        rail.Position = offset;
        using CkptReader reader = new(rail);
        int count = 0;
        long lastRecordOffset = offset;
        long lastSequence = expectedSequence - 1;
        byte[] lastHash = previous;
        while (reader.RemainingBytes > 0)
        {
            lastRecordOffset = rail.Position;
            long sequence = reader.I64();
            int fromStep = reader.I32();
            int toStep = reader.I32();
            int length = reader.I32();
            if (length < 0 || length > 64 * 1024 * 1024)
                throw new InvalidDataException("typed mutation payload exceeds bound");
            byte[] payload = reader.RawExact(length);
            byte[] payloadHash = reader.RawExact(HashLength);
            byte[] prior = reader.RawExact(HashLength);
            if (sequence != expectedSequence || fromStep != lastToStep || toStep < fromStep || !prior.AsSpan().SequenceEqual(lastHash))
                throw new InvalidDataException($"typed mutation horizon/chain gap at sequence {sequence}");
            if (!SHA256.HashData(payload).AsSpan().SequenceEqual(payloadHash))
                throw new InvalidDataException("typed mutation payload checksum failed");
            DecodedMutation decoded = DecodePayload(payload);
            if (decoded.Snap.Present && decoded.Snap.NextStep != toStep)
                throw new InvalidDataException($"CortexSnap mutation anchor {decoded.Snap.NextStep} disagrees with record horizon {toStep}");
            byte[] body = EncodeRecord(sequence, fromStep, toStep, payload, prior);
            lastHash = SHA256.HashData(Concat("CORTEXT-MUTATION\0"u8, body));
            lastSequence = sequence;
            lastToStep = toStep;
            expectedSequence++;
            if (++count > MaxRecords) throw new InvalidDataException("typed mutation record count exceeds bound");
        }
        return new(default, count, lastSequence, lastToStep, lastRecordOffset, lastHash, "");
    }

    private static ScanResult Scan(Stream stream)
    {
        stream.Position = 0;
        using CkptReader reader = new(stream);
        if (!IsMagic(reader.RawExact(Magic.Length)))
            throw new InvalidDataException("unknown typed checkpoint mutation dialect");
        DeltaHeader header = new(reader.Str(), reader.Str(), reader.I32(), reader.I64());
        if (header.BaseStep < 0 || header.FirstSequence < 0) throw new InvalidDataException("typed mutation header is malformed");
        byte[] previous = ZeroHash;
        long expected = header.FirstSequence;
        int count = 0;
        int lastToStep = header.BaseStep;
        long lastRecordOffset = stream.Position;
        List<byte[]> hashes = new();
        while (reader.RemainingBytes > 0)
        {
            lastRecordOffset = stream.Position;
            long sequence = reader.I64(); int fromStep = reader.I32(); int toStep = reader.I32(); int length = reader.I32();
            if (length < 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("typed mutation payload exceeds bound");
            byte[] payload = reader.RawExact(length); byte[] payloadHash = reader.RawExact(HashLength); byte[] prior = reader.RawExact(HashLength);
            if (sequence != expected || fromStep != lastToStep || toStep < fromStep || !prior.AsSpan().SequenceEqual(previous))
                throw new InvalidDataException($"typed mutation horizon/chain gap at sequence {sequence}");
            if (!SHA256.HashData(payload).AsSpan().SequenceEqual(payloadHash)) throw new InvalidDataException("typed mutation payload checksum failed");
            DecodedMutation decoded = DecodePayload(payload); // structural validation; state remains owned by the keyframe
            if (decoded.Snap.Present && decoded.Snap.NextStep != toStep)
                throw new InvalidDataException($"CortexSnap mutation anchor {decoded.Snap.NextStep} disagrees with record horizon {toStep}");
            byte[] body = EncodeRecord(sequence, fromStep, toStep, payload, prior);
            byte[] hash = SHA256.HashData(Concat("CORTEXT-MUTATION\0"u8, body));
            hashes.Add(hash); previous = hash; expected++; lastToStep = toStep;
            if (++count > MaxRecords) throw new InvalidDataException("typed mutation record count exceeds bound");
        }
        return new ScanResult(header, count, expected - 1, lastToStep, count == 0 ? stream.Position : lastRecordOffset, previous, ComputeChain(header.BasePhysicalSHA256, hashes));
    }

    private static DecodedMutation DecodePayload(byte[] payload)
    {
        using MemoryStream stream = new(payload, writable: false);
        using CkptReader reader = new(stream);
        byte version = reader.U8();
        if (version is not (2 or 3 or 4 or 5 or 6 or 7)) throw new InvalidDataException("unsupported typed mutation payload version");
        TapeCheckpointDelta tape = Tape.ReadCheckpointDelta(reader);
        JournalCheckpointDelta journal = Journal.ReadCheckpointDelta(reader);
        ReadsCheckpointDelta reads = Reads.ReadCheckpointDelta(reader);
        GrammarArtifactDelta grammar = default;
        if (reader.U8() != 0) grammar = new(reader.U64(), reader.Str(), reader.Str());
        CortexSnapCheckpointDelta snap = default;
        if (version >= 3 && reader.Bool()) snap = ReadCortexSnapCheckpointDelta(reader);
        CheckpointReplayContext replayContext = default;
        if (version >= 4) {
            if (reader.Bool())
            {
                int windowStart = reader.I32();
                int windowEnd = reader.I32();
                CheckpointReplayKinds kind = version >= 5
                    ? (CheckpointReplayKinds)reader.U8()
                    : CheckpointReplayKinds.None;
                string configDigest = "", railRunID = "", predecessorCurve = "", predecessorParent = "";
                string successorRun = "", successorConfig = "", successorChain = "", successorArm = "", scheduleDigest = "";
                if (version >= 5)
                {
                    configDigest = reader.Str(); railRunID = reader.Str(); predecessorCurve = reader.Str(); predecessorParent = reader.Str();
                    successorRun = reader.Str(); successorConfig = reader.Str(); successorChain = reader.Str(); successorArm = reader.Str();
                    scheduleDigest = reader.Str();
                }
                replayContext = new CheckpointReplayContext(windowStart, windowEnd, kind, -1, -1,
                    configDigest, railRunID, "", predecessorCurve, predecessorParent, successorRun, successorConfig,
                    successorChain, successorArm, scheduleDigest, Bound: true).Validate();
                if (version >= 5 && (configDigest.Length == 0 || railRunID.Length == 0))
                    throw new InvalidDataException("typed v5 mutation context lacks persisted config or rail identity");
            }
        }
        bool hasState = reader.U8() == 1;
        SelfStreamCheckpointDelta self = default;
        WeightController.WeightControllerCheckpointDelta controller = default;
        Metabolism.MetabolismCheckpointDelta metabolism = default;
        MemoryCheckpointDelta memory = default;
        HomeostatCheckpointDelta homeostat = default;
        Rhythm.RhythmCheckpointDelta rhythm = default;
        CortexPolicyCheckpointDelta policy = default;
        LoopLineageTurnstile.LoopLineageCheckpointDelta lineage = default;
        Cogito.Induct.LoomCheckpointDelta loom = default;
        bool hasLoom = false;
        ICurriculumCheckpointDelta? curriculum = null;
        LoopClosureLinkAttempt[] loopClosureLinks = Array.Empty<LoopClosureLinkAttempt>();
        if (hasState)
        {
            self = SelfStream.ReadCheckpointDelta(reader); controller = WeightController.ReadCheckpointDelta(reader); metabolism = Metabolism.ReadCheckpointDelta(reader);
            memory = MemoryHierarchy.ReadCheckpointDelta(reader); homeostat = Homeostat.ReadCheckpointDelta(reader); rhythm = Rhythm.ReadCheckpointDelta(reader);
            policy = Cortex.ReadCheckpointDelta(reader); lineage = LoopLineageTurnstile.ReadCheckpointDelta(reader);
            hasLoom = reader.Bool();
            if (hasLoom) loom = Cogito.Induct.Loom.ReadCheckpointDelta(reader);
            if (reader.Bool()) curriculum = version >= 6
                ? ReadCurriculumCheckpointDelta(reader)
                : ReadLegacyCurriculumCheckpointDelta(reader);
            if (version >= 7)
            {
                int linkCount = reader.I32();
                if (linkCount < 0 || linkCount > 1_000_000)
                    throw new InvalidDataException("typed mutation loop-link count exceeds bound");
                loopClosureLinks = new LoopClosureLinkAttempt[linkCount];
                for (int index = 0; index < linkCount; index++)
                    loopClosureLinks[index] = LoopClosureLinkAttemptStore.DecodeCheckpoint(reader.Bytes(1_000_000));
            }
        }
        if (reader.RemainingBytes != 0) throw new InvalidDataException("typed mutation payload has trailing bytes");
        return new(tape, journal, reads, grammar, self, controller, metabolism, memory, homeostat, rhythm, policy, lineage, loom, hasState, hasLoom, curriculum, snap, replayContext, loopClosureLinks);
    }

    private static void WriteCortexSnapCheckpointDelta(CkptWriter writer, in CortexSnapCheckpointDelta snap)
    {
        writer.I32(snap.NextStep); writer.U64(snap.GrammarRevision); writer.I64(snap.LastInduceBytes); writer.I32(snap.WallStreak);
        writer.I32(snap.TotalEvicted); writer.I32(snap.TotalPromoted); writer.I32(snap.LastSlotted); writer.I64(snap.LastBitsSaved);
        writer.I64(snap.CurveLen); writer.I64(snap.LastSleepBytes); writer.F64(snap.LastInduceOpb); writer.F64(snap.LastBitsPerSpan);
        writer.I64(snap.PrevTapeCount); writer.I32(snap.BreachConsolidationPhases); writer.I32(snap.BreachWindowResets);
        writer.I32(snap.ForkStep); writer.F64(snap.ForkVolumeFrac); writer.I32(snap.LastConsolidationPhaseRules); writer.I32(snap.LastDNodes);
    }

    private static CortexSnapCheckpointDelta ReadCortexSnapCheckpointDelta(CkptReader reader)
        => new(true, reader.I32(), reader.U64(), reader.I64(), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.I64(),
            reader.I64(), reader.I64(), reader.F64(), reader.F64(), reader.I64(), reader.I32(), reader.I32(), reader.I32(), reader.F64(),
            reader.I32(), reader.I32());

    private static void WriteCurriculumCheckpointDelta(CkptWriter writer, ICurriculumCheckpointDelta delta)
    {
        if (string.IsNullOrWhiteSpace(delta.Kind) || delta.Kind.Length > 128 || delta.Kind.Any(char.IsControl))
            throw new InvalidDataException("curriculum checkpoint delta kind is malformed");
        writer.Str(delta.Kind);
        using MemoryStream payload = new();
        using (CkptWriter payloadWriter = new(payload)) delta.Write(payloadWriter);
        byte[] bytes = payload.ToArray();
        if (bytes.Length == 0) throw new InvalidDataException($"curriculum checkpoint delta kind '{delta.Kind}' has an empty payload");
        writer.Bytes(bytes);
    }

    private static ICurriculumCheckpointDelta ReadCurriculumCheckpointDelta(CkptReader reader)
    {
        string kind = reader.Str();
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 128 || kind.Any(char.IsControl))
            throw new InvalidDataException("curriculum checkpoint delta kind is malformed");
        byte[] payload = reader.Bytes(64 * 1024 * 1024);
        if (payload.Length == 0) throw new InvalidDataException($"curriculum checkpoint delta kind '{kind}' has an empty payload");
        return new OpaqueCurriculumCheckpointDelta(kind, payload);
    }


    private static ICurriculumCheckpointDelta ReadLegacyCurriculumCheckpointDelta(CkptReader reader)
    {
        byte kind = reader.U8();
        return kind switch
        {
            1 => FlatPool.ReadCheckpointDelta(reader),
            2 => GrokBell.ReadCheckpointDelta(reader),
            3 => Campfire.ReadCheckpointDelta(reader),
            4 => ReplayCalc.ReadCheckpointDelta(reader),
            _ => throw new InvalidDataException($"unknown legacy curriculum checkpoint delta kind {kind}"),
        };
    }

    private static string ChainSeed(string physical) => ComputeChain(physical, []);

    private static string ComputeChain(string physical, List<byte[]> hashes)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("CORTEXT-MUTATION-CHAIN\0"u8); hash.AppendData(Encoding.UTF8.GetBytes(physical));
        foreach (byte[] value in hashes) hash.AppendData(value);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static byte[] Concat(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        byte[] bytes = new byte[left.Length + right.Length]; left.CopyTo(bytes); right.CopyTo(bytes.AsSpan(left.Length)); return bytes;
    }

    private static byte[] ReplayLegacyReplacement(byte[] baseImage, byte[] delta)
    {
        const string oldKind = "FullImageReplacement";
        const int hashLength = 32;
        using MemoryStream stream = new(delta, writable: false);
        using CkptReader reader = new(stream);
        _ = reader.RawExact("CORTEXT-D1\n"u8.Length); _ = reader.Str(); _ = reader.Str();
        int baseStep = reader.I32(); long expected = reader.I64(); int lastTo = baseStep;
        byte[] previous = new byte[hashLength]; byte[]? effective = null; int count = 0;
        while (reader.RemainingBytes > 0)
        {
            long sequence = reader.I64(); int from = reader.I32(); int to = reader.I32(); string kind = reader.Str(); string component = reader.Str();
            int length = reader.I32(); byte[] payload = reader.RawExact(length); byte[] payloadHash = reader.RawExact(hashLength); byte[] prior = reader.RawExact(hashLength);
            if (sequence != expected || from != lastTo || to < from || !prior.AsSpan().SequenceEqual(previous)
                || !string.Equals(kind, oldKind, StringComparison.Ordinal)
                || !SHA256.HashData(payload).AsSpan().SequenceEqual(payloadHash))
                throw new InvalidDataException("legacy checkpoint delta chain is corrupt");
            effective = payload; previous = SHA256.HashData(Concat("CORTEX-RECORD\0"u8, EncodeLegacyBodyWithPrevious(sequence, from, to, kind, component, payload, payloadHash, prior)));
            expected++; lastTo = to; if (++count > MaxRecords) throw new InvalidDataException("legacy checkpoint delta exceeds bound");
        }
        return effective ?? baseImage;
    }


    internal static bool VerifyFixture(TextWriter output)
    {
        string fixtureDirectory = Path.GetFullPath(Path.Combine(".tmp", $"checkpoint-delta-fixture-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(Path.GetDirectoryName(fixtureDirectory)!);
        bool replayExact = false;
        bool typedRecords = false;
        bool corruptTailRejected = false;
        bool partialTailRejected = false;
        bool sectionTailRejected = false;
        bool noImplicitCompaction = false;
        bool explicitCompaction = false;
        bool keyframeStable = false;
        bool reorderExact = false;
        bool reorderChainExact = false;
        bool reorderEvacExact = false;
        bool reorderCorruptRejected = false;
        bool reorderDrainExact = false;
        bool loomAppendEvacuationExact = false;
        bool loomReplayLiveExact = false;
        bool legacyReadsRoundtrip = false;
        bool cortexSnapRoundtrip = false;
        bool replayKindFixture = false;
        bool currentRailCompactionExact = false;
        bool corruptRailLeavesStateUntouched = false;
        bool selfStreamObservation = false;
        bool homeostatPendingDelta = false;
        try
        {
            selfStreamObservation = SelfStream.VerifyCheckpointObservationFixture(output);
            homeostatPendingDelta = VerifyHomeostatPendingDeltaFixture(output);
            Run run = Run.Create(fixtureDirectory);
            byte[] keyframe = Encoding.UTF8.GetBytes("CORTEXT-keyframe-fixture\n");
            Checkpoint.Save(run, keyframe);
            byte[] keyframeBefore = File.ReadAllBytes(run.PathOf(Checkpoint.FileName));
            CheckpointDelta.Initialize(run, nextStep: 0);
            DateTime keyframeWriteBeforeCadence = File.GetLastWriteTimeUtc(run.PathOf(Checkpoint.FileName));

            using Tape tape = new();
            Journal journal = new();
            Reads reads = new();
            byte[] readsCurrent = SerializeReads(reads);
            byte[] readsLegacy = DowngradeReadsRetentionSection(readsCurrent);
            Reads readsReloaded = new();
            using (MemoryStream readsLegacyStream = new(readsLegacy, writable: false))
            using (CkptReader readsReader = new(readsLegacyStream))
                readsReloaded.Load(readsReader, default);
            legacyReadsRoundtrip = SerializeReads(readsReloaded).AsSpan().SequenceEqual(readsLegacy);
            TapeEventID first = tape.Append(Encoding.UTF8.GetBytes("first-event"), "fixture");
            journal.Ingest(1, first, "fixture", Encoding.UTF8.GetBytes("first-event"));
            CheckpointDelta.Append(run, fromStep: 0, toStep: 1, tape, journal, reads);
            TapeEventID second = tape.Append(Encoding.UTF8.GetBytes("second-event"), "fixture");
            journal.Mint(2, second, "fixture", Encoding.UTF8.GetBytes("second-event"));
            CheckpointDelta.Append(run, fromStep: 1, toStep: 2, tape, journal, reads);

            using (Tape reorderSource = new())
            {
                reorderSource.Append("a"u8.ToArray(), "fixture");
                reorderSource.Append("b"u8.ToArray(), "fixture");
                reorderSource.Append("c"u8.ToArray(), "fixture");
                reorderSource.Reorder([2, 0, 1]);
                TapeCheckpointDelta reorderDelta = reorderSource.CaptureCheckpointDelta();
                using Tape reorderRestored = new();
                reorderRestored.ApplyCheckpointDelta(in reorderDelta);
                reorderExact = reorderRestored.ResidentEventIDs.SequenceEqual(reorderSource.ResidentEventIDs);
            }

            // A checkpoint interval can contain several sleep reorders. Each
            // local permutation must compose into one final sparse frame;
            // replay must not interpret the second frame in the first frame's
            // coordinate system.
            using (Tape reorderChainSource = new())
            {
                reorderChainSource.Append("a"u8.ToArray(), "fixture");
                reorderChainSource.Append("b"u8.ToArray(), "fixture");
                reorderChainSource.Append("c"u8.ToArray(), "fixture");
                reorderChainSource.Reorder([2, 0, 1]);
                reorderChainSource.Reorder([2, 1, 0]);
                TapeCheckpointDelta reorderChainDelta = reorderChainSource.CaptureCheckpointDelta();
                using Tape reorderChainRestored = new();
                reorderChainRestored.ApplyCheckpointDelta(in reorderChainDelta);
                reorderChainExact = reorderChainRestored.ResidentEventIDs.SequenceEqual(reorderChainSource.ResidentEventIDs);
                reorderDrainExact = reorderChainRestored.DrainDelta().OrderRevision == TapeRevision.Initial;
                if (reorderChainDelta.ReorderEdits.Length > 0)
                {
                    TapeCheckpointReorderEdit[] corruptEdits = reorderChainDelta.ReorderEdits
                        .Concat([reorderChainDelta.ReorderEdits[0]])
                        .ToArray();
                    TapeCheckpointDelta corruptDelta = reorderChainDelta with { ReorderEdits = corruptEdits };
                    using Tape corruptRestored = new();
                    reorderCorruptRejected = Rejects(() => corruptRestored.ApplyCheckpointDelta(in corruptDelta));
                }
            }

            // Evacuation is the coordinate-frame boundary for v2. A resident
            // can be shed/dropped before the final reorder and must not leave a
            // stale slot reference or make the surviving order unreplayable.
            using (Tape reorderEvacSource = new())
            {
                TapeEventID shed = reorderEvacSource.Append("shed"u8.ToArray(), "fixture", Provenances.Real);
                reorderEvacSource.Append("keep-b"u8.ToArray(), "fixture", Provenances.Real);
                reorderEvacSource.Append("keep-c"u8.ToArray(), "fixture", Provenances.Real);
                TapeEventID drop = reorderEvacSource.Append("drop"u8.ToArray(), "fixture", Provenances.Replay);
                using MemoryStream sourceLog = new();
                reorderEvacSource.MountLog(sourceLog);
                reorderEvacSource.Evacuate([shed], [drop]);
                reorderEvacSource.Reorder([1, 0]);
                TapeCheckpointDelta reorderEvacDelta = reorderEvacSource.CaptureCheckpointDelta();
                sourceLog.Position = 0;
                using MemoryStream replayLog = new();
                sourceLog.CopyTo(replayLog);
                replayLog.Position = 0;
                using Tape reorderEvacRestored = new();
                reorderEvacRestored.MountLog(replayLog);
                reorderEvacRestored.ApplyCheckpointDelta(in reorderEvacDelta);
                reorderEvacExact = reorderEvacRestored.ResidentEventIDs.SequenceEqual(reorderEvacSource.ResidentEventIDs)
                    && reorderEvacRestored.ShedEventIDs.SequenceEqual(reorderEvacSource.ShedEventIDs)
                    && reorderEvacRestored.DroppedCount == reorderEvacSource.DroppedCount;
            }

            // A checkpoint interval may append a span, splice it into Loom,
            // then shed it before the receipt is cut.  The physical tape
            // delta therefore names the same id in Appended and Shed.  The
            // naive full-delta order rejects it because Loom sees the span
            // only after Tape has evacuated it; the staged order is the
            // resume contract and must reproduce the live grammar exactly.
            using (Tape loomSourceTape = new())
            using (MemoryStream loomSourceLog = new())
            using (Loom loomSource = new())
            {
                loomSourceTape.MountLog(loomSourceLog);
                loomSourceTape.Append("abababab"u8.ToArray(), "fixture", Provenances.Real);
                loomSource.SpliceNew(loomSourceTape); loomSource.Pump();
                _ = loomSourceTape.DrainDelta();
                byte[] baseTapeImage;
                using (MemoryStream image = new())
                { using CkptWriter writer = new(image); loomSourceTape.Save(writer); writer.Dispose(); baseTapeImage = image.ToArray(); }
                byte[] baseLoomImage;
                using (MemoryStream image = new())
                { using CkptWriter writer = new(image); loomSource.Save(writer); writer.Dispose(); baseLoomImage = image.ToArray(); }
                loomSourceTape.CommitCheckpointDelta(); loomSource.CommitCheckpointDelta();

                TapeEventID appended = loomSourceTape.Append("abababab"u8.ToArray(), "fixture", Provenances.Real);
                TapeDelta appendedDelta = loomSourceTape.DrainDelta();
                loomSource.ApplyTapeDelta(loomSourceTape, in appendedDelta); loomSource.Pump();
                loomSourceTape.Evacuate([appended], []);
                TapeCheckpointDelta tapeDelta = loomSourceTape.CaptureCheckpointDelta();
                LoomCheckpointDelta loomDelta = loomSource.CaptureCheckpointDelta();
                bool combined = tapeDelta.Appended.Any(entry => entry.ID == appended)
                    && tapeDelta.Shed.Any(entry => entry.ID == appended);

                using Tape naiveTape = new();
                using MemoryStream naiveLog = new(loomSourceLog.ToArray(), writable: false);
                naiveTape.MountLog(naiveLog);
                using (MemoryStream image = new(baseTapeImage, writable: false))
                using (CkptReader reader = new(image)) naiveTape.Load(reader);
                using Loom naiveLoom = new();
                using (MemoryStream image = new(baseLoomImage, writable: false))
                using (CkptReader reader = new(image)) naiveLoom.Load(reader, naiveTape);
                bool naiveRejected = Rejects(() =>
                {
                    naiveTape.ApplyCheckpointDelta(in tapeDelta);
                    TapeDelta mutation = tapeDelta.Mutation;
                    naiveLoom.ApplyTapeDeltaForCheckpoint(naiveTape, in mutation);
                });

                using Tape resumedTape = new();
                using MemoryStream resumedLog = new(loomSourceLog.ToArray(), writable: false);
                resumedTape.MountLog(resumedLog);
                using (MemoryStream image = new(baseTapeImage, writable: false))
                using (CkptReader reader = new(image)) resumedTape.Load(reader);
                using Loom resumedLoom = new();
                using (MemoryStream image = new(baseLoomImage, writable: false))
                using (CkptReader reader = new(image)) resumedLoom.Load(reader, resumedTape);
                TapeCheckpointDelta residency = tapeDelta with
                {
                    Mutation = tapeDelta.Mutation with { OrderRevision = TapeRevision.Initial, Shed = [], Dropped = [] },
                    Shed = [], Dropped = [], ReorderEdits = [], Reordered = false,
                };
                resumedTape.ApplyCheckpointDelta(in residency);
                TapeDelta resumedMutation = tapeDelta.Mutation;
                resumedLoom.ApplyTapeDeltaForCheckpoint(resumedTape, in resumedMutation);
                resumedLoom.ApplyCheckpointDelta(in loomDelta, applyArenaEntries: true);
                TapeCheckpointDelta evacuation = tapeDelta with
                {
                    Mutation = tapeDelta.Mutation with { Appended = [], Reflected = [] },
                    Appended = [],
                };
                resumedTape.ApplyCheckpointDelta(in evacuation);
                TapeDelta replayDrain = resumedTape.DrainDelta();
                TapeEventID sourceLive = loomSourceTape.Append("live-after-replay"u8.ToArray(), "fixture", Provenances.Real);
                TapeDelta sourceLiveDelta = loomSourceTape.DrainDelta();
                loomSource.ApplyTapeDelta(loomSourceTape, in sourceLiveDelta); loomSource.Pump();
                TapeEventID resumedLive = resumedTape.Append("live-after-replay"u8.ToArray(), "fixture", Provenances.Real);
                TapeDelta resumedLiveDelta = resumedTape.DrainDelta();
                resumedLoom.ApplyTapeDelta(resumedTape, in resumedLiveDelta); resumedLoom.Pump();
                loomAppendEvacuationExact = combined && naiveRejected
                    && resumedTape.ResidentEventIDs.SequenceEqual(loomSourceTape.ResidentEventIDs)
                    && resumedTape.ShedEventIDs.SequenceEqual(loomSourceTape.ShedEventIDs)
                    && resumedLoom.Result(resumedTape).Compressed.AsSpan().SequenceEqual(loomSource.Result(loomSourceTape).Compressed);
                loomReplayLiveExact = replayDrain.IsEmpty
                    && sourceLiveDelta.Appended.SequenceEqual([sourceLive])
                    && resumedLiveDelta.Appended.SequenceEqual([resumedLive])
                    && resumedLoom.Result(resumedTape).Compressed.AsSpan().SequenceEqual(loomSource.Result(loomSourceTape).Compressed);
            }

            byte[] rail = File.ReadAllBytes(run.PathOf(Checkpoint.DeltaFileName));
            using (FileStream stream = File.OpenRead(run.PathOf(Checkpoint.DeltaFileName)))
            {
                ScanResult scan = Scan(stream);
                typedRecords = scan.RecordCount == 2 && scan.LastToStep == 2;
            }
            byte[] keyframeAfter = File.ReadAllBytes(run.PathOf(Checkpoint.FileName));
            DateTime keyframeWriteAfterCadence = File.GetLastWriteTimeUtc(run.PathOf(Checkpoint.FileName));
            byte[] effective = LoadEffectiveImage(run.Dir);
            replayExact = keyframeBefore.AsSpan().SequenceEqual(keyframeAfter)
                && keyframe.AsSpan().SequenceEqual(effective);
            keyframeStable = keyframeBefore.AsSpan().SequenceEqual(keyframeAfter)
                && Checkpoint.PhysicalSHA256(keyframeBefore) == Checkpoint.PhysicalSHA256(keyframeAfter)
                && keyframeWriteBeforeCadence == keyframeWriteAfterCadence;
            noImplicitCompaction = File.Exists(run.PathOf(Checkpoint.DeltaFileName))
                && keyframeBefore.AsSpan().SequenceEqual(keyframeAfter);

            // A torn write must never be treated as a valid prefix. Restore the
            // good rail after each mutation so every adversarial arm starts from
            // the same two-record chain.
            byte[] partial = rail[..^1];
            File.WriteAllBytes(run.PathOf(Checkpoint.DeltaFileName), partial);
            partialTailRejected = Rejects(() => LoadEffectiveImage(run.Dir));
            File.WriteAllBytes(run.PathOf(Checkpoint.DeltaFileName), rail);
            byte[] corrupt = (byte[])rail.Clone();
            corrupt[^1] ^= 0x5A;
            File.WriteAllBytes(run.PathOf(Checkpoint.DeltaFileName), corrupt);
            corruptTailRejected = Rejects(() => LoadEffectiveImage(run.Dir));
            File.WriteAllBytes(run.PathOf(Checkpoint.DeltaFileName), rail);

            TapeCheckpointDelta emptyTape = new(
                new TapeDelta(TapeRevision.Initial, TapeRevision.Initial, [], [], [], []),
                [], [], [], [], false);
            byte[] validPayload = EncodePayload(emptyTape,
                new JournalCheckpointDelta(0, []), new ReadsCheckpointDelta(0, []), default, default);
            byte[] payloadWithTrailingSection = new byte[validPayload.Length + 1];
            validPayload.CopyTo(payloadWithTrailingSection, 0);
            payloadWithTrailingSection[^1] = 0x7F;
            sectionTailRejected = Rejects(() => DecodePayload(payloadWithTrailingSection));

            CortexSnap expectedSnap = new(
                64, 0x1122334455667788UL, 987654321L, -7,
                12, 34, 56, 7890, 456789L, 654321L,
                1.25, -0.75, 321L, 8, 9, 17, 0.875, 43, -5);
            CheckpointMutationState snapState = new(
                default, default, default, default, default, default, default, default, default,
                false, default, CortexSnapCheckpointDelta.Capture(in expectedSnap));
            byte[] snapPayload = EncodePayload(emptyTape,
                new JournalCheckpointDelta(0, []), new ReadsCheckpointDelta(0, []), default, in snapState);
            CortexSnapCheckpointDelta decodedSnap = DecodePayload(snapPayload).Snap;
            cortexSnapRoundtrip = decodedSnap.Present && decodedSnap.ToSnapshot().Equals(expectedSnap);
            replayKindFixture = ReplayCalc.VerifyCheckpointReplayKindFixture(Path.Combine(fixtureDirectory, "replay-kinds"));

            CheckpointDelta.Compact(run.Dir);
            explicitCompaction = !File.Exists(run.PathOf(Checkpoint.DeltaFileName))
                && File.ReadAllBytes(run.PathOf(Checkpoint.FileName)).AsSpan().SequenceEqual(keyframeBefore)
                && LoadEffectiveImage(run.Dir).AsSpan().SequenceEqual(keyframe);

            // The synthetic fixture above proves the receipt rail itself. Keep a
            // separate current-schema arm to prove the real contract: a
            // non-empty v3 rail materializes an effective terminal image, then
            // explicit compaction promotes that image into the canonical base.
            string cortexDirectory = Path.Combine(fixtureDirectory, "cortex-compaction");
            string cortexCorpus = Path.Combine(fixtureDirectory, "cortex-corpus.txt");
            File.WriteAllText(cortexCorpus, "alpha beta gamma\n");
            Run cortexRun = Run.Create(cortexDirectory);
            CortexConfig cortexConfig = new()
            {
                Steps = 4,
                Seed = 0xC0FFEEUL,
                Durability = new CortexDurabilityConfig { CheckpointEvery = 2 },
                Curriculum = new CortexFlatPoolCurriculum
                {
                    Corpus = new CogitoCorpus { Path = cortexCorpus },
                    IntakeBatch = 1,
                    SeedSpans = 1,
                },
            };
            Cortex lineageCortex = new(cortexConfig);
            lineageCortex.EnableLoopLineage();
            bool cortexDrove = lineageCortex.Run(cortexRun) == 0;
            if (cortexDrove)
            {
                byte[] cortexBase = File.ReadAllBytes(cortexRun.PathOf(Checkpoint.FileName));
                int cortexBaseStep = Checkpoint.PeekNextStep(cortexBase);
                byte[] effectiveBeforeCompact = LoadEffectiveImage(cortexRun.Dir);
                CheckpointDeltaAuthority railAuthority = ReadAuthority(cortexRun.Dir);
                bool effectiveDiffersFromBase = cortexBaseStep == 2
                    && railAuthority.LastToStep == 4
                    && !effectiveBeforeCompact.AsSpan().SequenceEqual(cortexBase)
                    && Checkpoint.PeekNextStep(effectiveBeforeCompact) == 4;

                string corruptDirectory = Path.Combine(fixtureDirectory, "cortex-corrupt");
                Directory.CreateDirectory(corruptDirectory);
                foreach (string file in Directory.EnumerateFiles(cortexRun.Dir))
                    File.Copy(file, Path.Combine(corruptDirectory, Path.GetFileName(file)));
                byte[] corruptBaseBefore = File.ReadAllBytes(Path.Combine(corruptDirectory, Checkpoint.FileName));
                byte[] corruptRail = File.ReadAllBytes(Path.Combine(corruptDirectory, Checkpoint.DeltaFileName));
                corruptRail[^1] ^= 0x5A;
                File.WriteAllBytes(Path.Combine(corruptDirectory, Checkpoint.DeltaFileName), corruptRail);
                bool corruptRejected = Rejects(() => LoadEffectiveImage(corruptDirectory));
                byte[] corruptBaseAfter = File.ReadAllBytes(Path.Combine(corruptDirectory, Checkpoint.FileName));
                byte[] corruptRailAfter = File.ReadAllBytes(Path.Combine(corruptDirectory, Checkpoint.DeltaFileName));
                corruptRailLeavesStateUntouched = corruptRejected
                    && corruptBaseBefore.AsSpan().SequenceEqual(corruptBaseAfter)
                    && corruptRail.AsSpan().SequenceEqual(corruptRailAfter);

                CheckpointDelta.Compact(cortexRun.Dir);
                byte[] compactedBase = File.ReadAllBytes(cortexRun.PathOf(Checkpoint.FileName));
                currentRailCompactionExact = effectiveDiffersFromBase
                    && !File.Exists(cortexRun.PathOf(Checkpoint.DeltaFileName))
                    && !File.Exists(cortexRun.PathOf(Checkpoint.DeltaTailFileName))
                    && compactedBase.AsSpan().SequenceEqual(effectiveBeforeCompact)
                    && Checkpoint.PeekNextStep(compactedBase) == cortexBaseStep + 2;
            }
        }
        finally
        {
            if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory, recursive: true);
        }

        bool passed = replayExact && typedRecords && reorderExact && reorderChainExact && reorderEvacExact && reorderCorruptRejected && reorderDrainExact && loomAppendEvacuationExact && loomReplayLiveExact && corruptTailRejected && partialTailRejected
            && sectionTailRejected && noImplicitCompaction && explicitCompaction;
        passed &= keyframeStable && legacyReadsRoundtrip && cortexSnapRoundtrip;
        passed &= replayKindFixture;
        passed &= currentRailCompactionExact && corruptRailLeavesStateUntouched;
        passed &= selfStreamObservation && homeostatPendingDelta;
        output.WriteLine($"  checkpoint delta · keyframe+typed-replay={(replayExact ? "exact" : "BROKEN")} records={(typedRecords ? "2" : "BROKEN")} reorder={(reorderExact ? "typed-exact" : "BROKEN")} reorder-chain={(reorderChainExact ? "composed" : "BROKEN")} reorder-evac={(reorderEvacExact ? "exact" : "BROKEN")} loom-append-evac={(loomAppendEvacuationExact ? "staged-exact" : "BROKEN")} loom-replay-live={(loomReplayLiveExact ? "quiet+exact" : "HISTORICAL-REPORTED")} reorder-corrupt={(reorderCorruptRejected ? "rejected" : "ACCEPTED")} reorder-drain={(reorderDrainExact ? "quiet" : "PHANTOM")} homeostat-pending={(homeostatPendingDelta ? "replace-clear-exact" : "BROKEN")} snap={(cortexSnapRoundtrip ? "all-fields-exact" : "DRIFT")} replay-kind={(replayKindFixture ? "custodied" : "BROKEN")} partial-tail={(partialTailRejected ? "rejected" : "ACCEPTED")} corrupt-tail={(corruptTailRejected ? "rejected" : "ACCEPTED")} section-tail={(sectionTailRejected ? "rejected" : "ACCEPTED")} implicit-compaction={(noImplicitCompaction ? "absent" : "PRESENT")} explicit-compaction={(explicitCompaction ? "exact" : "BROKEN")} current-rail-compaction={(currentRailCompactionExact ? "effective→base" : "BROKEN")} corrupt-rail-state={(corruptRailLeavesStateUntouched ? "untouched" : "MUTATED")} keyframe={(keyframeStable ? "mtime+hash-stable" : "CHANGED")} legacy-reads-vow={(legacyReadsRoundtrip ? "exact" : "DRIFT")} · {(passed ? "PASS" : "FAIL")}");
        return passed;

        static byte[] SerializeReads(Reads value)
        {
            using MemoryStream stream = new();
            using CkptWriter writer = new(stream);
            value.Save(writer, default);
            writer.Dispose();
            return stream.ToArray();
        }

        static byte[] DowngradeReadsRetentionSection(byte[] current)
        {
            ReadOnlySpan<byte> marker = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            for (int offset = current.Length - 12; offset >= 0; offset--)
            {
                if (!current.AsSpan(offset, 4).SequenceEqual(marker)) continue;
                if (!current.AsSpan(offset + 4, 8).SequenceEqual(new byte[8])) continue;
                byte[] legacy = new byte[current.Length - 12];
                current.AsSpan(0, offset).CopyTo(legacy);
                current.AsSpan(offset + 12).CopyTo(legacy.AsSpan(offset));
                return legacy;
            }
            throw new InvalidDataException("reads fixture could not locate retention marker");
        }
    }

    private static bool VerifyHomeostatPendingDeltaFixture(TextWriter output)
    {
        static CortexPolicyDecision Decision(ulong id, CortexPolicyID policy)
        {
            CortexPolicyDecisionReadout readout = new(
                LaunchpadAction: 0, RawCandidateAction: -1, SelectedCandidateAction: -1, ExecutedAction: 0,
                Authority: CortexPolicyAuthorities.Launchpad,
                GrammarRevision: new global::Cogito.Grammar.GrammarRevisionID(1),
                SelectionCause: CortexPolicySelectionCauses.Launchpad);
            return new(new CortexPolicyDecisionID(id), policy, readout);
        }

        static byte[] Encode(HomeostatCheckpointDelta delta)
        {
            using MemoryStream stream = new();
            using CkptWriter writer = new(stream);
            Homeostat.WriteCheckpointDelta(writer, in delta);
            writer.Dispose();
            return stream.ToArray();
        }

        static HomeostatCheckpointDelta Decode(byte[] bytes)
        {
            using MemoryStream stream = new(bytes, writable: false);
            using CkptReader reader = new(stream);
            HomeostatCheckpointDelta delta = Homeostat.ReadCheckpointDelta(reader);
            if (reader.RemainingBytes != 0) throw new InvalidDataException("homeostat fixture delta has trailing bytes");
            return delta;
        }

        static bool SameReceipt(HomeostatAdaptiveConstantReceipt left, HomeostatAdaptiveConstantReceipt right)
            => left.Constant == right.Constant && left.Decision == right.Decision
                && string.Equals(left.Context, right.Context, StringComparison.Ordinal)
                && left.Paid == right.Paid && left.Close == right.Close;

        static bool SameDecision(CortexPolicyDecision left, CortexPolicyDecision right)
            => left.DecisionID.Equals(right.DecisionID)
                && left.Policy.Equals(right.Policy)
                && left.Readout.Equals(right.Readout);

        static bool IsDefaultDecision(CortexPolicyDecision decision)
            => decision.DecisionID.Value == 0 && decision.Policy.Value is null && decision.Readout.Equals(default);

        try
        {
            CortexPolicyDecision staleShared = Decision(2477, Homeostat.PolicyID);
            CortexPolicyDecision newerShared = Decision(2559, Homeostat.PolicyID);
            CortexPolicyDecision lead = Decision(2480, Homeostat.ForecastLeadPolicyID);
            HomeostatAdaptiveConstantReceipt receipt = new(HomeostatAdaptiveConstants.HomeBandK, 0, "fixture", true, 0);
            HomeostatCheckpointDelta pending = new(0, [receipt], true, true, staleShared, true, true, lead);
            byte[] pendingBytes = Encode(pending);
            HomeostatCheckpointDelta pendingDecoded = Decode(pendingBytes);
            bool codecExact = pendingBytes.AsSpan().SequenceEqual(Encode(pendingDecoded))
                && pending.Cursor == pendingDecoded.Cursor
                && pending.Receipts.Length == pendingDecoded.Receipts.Length
                && SameReceipt(pending.Receipts[0], pendingDecoded.Receipts[0])
                && pending.SharedPolicyOutcomePending == pendingDecoded.SharedPolicyOutcomePending
                && pending.SharedPolicyDecisionInvariantClean == pendingDecoded.SharedPolicyDecisionInvariantClean
                && SameDecision(pending.SharedPolicyDecision, pendingDecoded.SharedPolicyDecision)
                && pending.LeadPolicyOutcomePending == pendingDecoded.LeadPolicyOutcomePending
                && pending.LeadPolicyDecisionInvariantClean == pendingDecoded.LeadPolicyDecisionInvariantClean
                && SameDecision(pending.LeadPolicyDecision, pendingDecoded.LeadPolicyDecision);

            Homeostat fresh = new(new WeightController(new Weights(1, 1, 1, 1, 1)), new HomeoActuation(1.0 / 8, 8, 1, 0, 0, false));
            HomeostatCheckpointDelta freshCaptured = fresh.CaptureCheckpointDelta();
            bool freshClosed = !freshCaptured.SharedPolicyOutcomePending && !freshCaptured.LeadPolicyOutcomePending
                && freshCaptured.SharedPolicyDecisionInvariantClean && freshCaptured.LeadPolicyDecisionInvariantClean;
            Homeostat homeostat = new(new WeightController(new Weights(1, 1, 1, 1, 1)), new HomeoActuation(1.0 / 8, 8, 1, 0, 0, false));
            homeostat.ApplyCheckpointDelta(in pendingDecoded);
            HomeostatCheckpointDelta closed = new(1, [], false, true, default, false, true, default);
            homeostat.ApplyCheckpointDelta(in closed);
            HomeostatCheckpointDelta closedCaptured = homeostat.CaptureCheckpointDelta();
            bool closedExact = closedCaptured.Cursor == 1 && !closedCaptured.SharedPolicyOutcomePending && !closedCaptured.LeadPolicyOutcomePending
                && IsDefaultDecision(closedCaptured.SharedPolicyDecision) && IsDefaultDecision(closedCaptured.LeadPolicyDecision);

            HomeostatCheckpointDelta replacement = new(1, [], true, true, newerShared, false, true, default);
            homeostat.ApplyCheckpointDelta(in replacement);
            HomeostatCheckpointDelta replacementCaptured = homeostat.CaptureCheckpointDelta();
            bool replacementExact = replacementCaptured.Cursor == 1 && replacementCaptured.SharedPolicyOutcomePending
                && replacementCaptured.SharedPolicyDecision.DecisionID.Equals(newerShared.DecisionID)
                && !replacementCaptured.LeadPolicyOutcomePending;

            Homeostat resumed = new(new WeightController(new Weights(1, 1, 1, 1, 1)), new HomeoActuation(1.0 / 8, 8, 1, 0, 0, false));
            HomeostatCheckpointDelta replacementDecoded = Decode(Encode(replacement));
            resumed.ApplyCheckpointDelta(in pendingDecoded);
            resumed.ApplyCheckpointDelta(in replacementDecoded);
            HomeostatCheckpointDelta materialized = resumed.CaptureCheckpointDelta();
            bool materializedExact = materialized.SharedPolicyOutcomePending
                && materialized.SharedPolicyDecision.DecisionID.Equals(newerShared.DecisionID)
                && IsDefaultDecision(materialized.LeadPolicyDecision);

            bool malformedRejected = Rejects(() =>
            {
                HomeostatCheckpointDelta malformed = replacement with
                {
                    SharedPolicyOutcomePending = false,
                    SharedPolicyDecisionInvariantClean = false,
                    SharedPolicyDecision = newerShared,
                };
                _ = Encode(malformed);
            });
            bool crossPolicyRejected = Rejects(() =>
            {
                HomeostatCheckpointDelta malformed = replacement with { SharedPolicyDecision = lead };
                _ = Encode(malformed);
            });
            bool nullRejected = Rejects(() =>
            {
                HomeostatCheckpointDelta malformed = replacement with { Receipts = null! };
                _ = Encode(malformed);
            });
            bool legacyRejected = Rejects(() =>
            {
                byte[] legacy = (byte[])pendingBytes.Clone();
                legacy[0] = 1;
                _ = Decode(legacy);
            });
            bool passed = codecExact && freshClosed && closedExact && replacementExact && materializedExact
                && malformedRejected && crossPolicyRejected && nullRejected && legacyRejected;
            output.WriteLine($"  homeostat checkpoint pending fixture · codec={(codecExact ? "byte-exact" : "BROKEN")} · fresh={(freshClosed ? "closed-canonical" : "BROKEN")} · closed={(closedExact ? "stays-closed" : "REOPENED")} · replacement={(replacementExact ? "2477→2559" : "STALE")} · materialized={(materializedExact ? "continuation-exact" : "BROKEN")} · tamper={(malformedRejected && crossPolicyRejected && nullRejected && legacyRejected ? "rejected" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        catch (Exception error)
        {
            output.WriteLine($"  homeostat checkpoint pending fixture · FAIL · {error.Message}");
            return false;
        }
    }

    private static bool Rejects(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    // Kept solely for the fork-transfer regression's synthetic image fixture.
    // Production runs never emit this retired replacement dialect.
    internal static byte[] EncodeFixtureDeltaForFork(byte[] baseImage, params byte[][] images)
    {
        using MemoryStream stream = new();
        using CkptWriter writer = new(stream);
        writer.Raw("CORTEXT-D1\n"u8);
        writer.Str(Checkpoint.LogicalStateSHA256(baseImage));
        writer.Str(Checkpoint.PhysicalSHA256(baseImage));
        writer.I32(0); writer.I64(0);
        byte[] previous = ZeroHash;
        for (int i = 0; i < images.Length; i++)
        {
            byte[] payloadHash = SHA256.HashData(images[i]);
            byte[] body = EncodeLegacyBodyWithPrevious(i, i, i + 1, "FullImageReplacement", "CORTEXT", images[i], payloadHash, previous);
            writer.Raw(body);
            previous = SHA256.HashData(Concat("CORTEX-RECORD\0"u8, body));
        }
        writer.Dispose();
        return stream.ToArray();
    }

    private readonly record struct DeltaHeader(string BaseLogicalSHA256, string BasePhysicalSHA256, int BaseStep, long FirstSequence);
    private readonly record struct DeltaTail(
        DeltaHeader Header,
        int RecordCount,
        long LastSequence,
        int LastToStep,
        long RailLength,
        long LastRecordOffset,
        byte[] LastRecordHash);
    private readonly record struct ScanResult(DeltaHeader Header, int RecordCount, long LastSequence, int LastToStep, long LastRecordOffset, byte[] LastRecordHash, string ChainSHA256);

    private static byte[] EncodeLegacyBodyWithPrevious(long sequence, int from, int to, string kind, string component, byte[] payload, byte[] payloadHash, byte[] previous)
    {
        using MemoryStream stream = new(); using CkptWriter writer = new(stream);
        writer.I64(sequence); writer.I32(from); writer.I32(to); writer.Str(kind); writer.Str(component); writer.I32(payload.Length); writer.Raw(payload);
        writer.Raw(payloadHash); writer.Raw(previous); writer.Dispose(); return stream.ToArray();
    }
}
