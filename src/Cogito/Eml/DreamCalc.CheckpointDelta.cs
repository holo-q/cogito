namespace Cogito;

/// Checkpoint mutation for the ReplayCalc curriculum.  The sieve and sampler
/// provide their own append/cursor protocols; ReplayCalc contributes only the
/// bounded replacement state and append-only lineage prefixes that it owns.
public sealed partial class ReplayCalc
{
    ICurriculumCheckpointDelta? ICurriculumCheckpointDeltaOwner.CaptureCheckpointDelta()
        => CaptureCheckpointDelta();

    void ICurriculumCheckpointDeltaOwner.ApplyCheckpointDelta(ICurriculumCheckpointDelta delta, in CheckpointReplayContext replayContext)
    {
        if (!string.Equals(delta.Kind, "dream-calc", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {delta.Kind} does not belong to ReplayCalc");
        ReplayCalcCheckpointDelta typed = delta switch
        {
            ReplayCalcCheckpointDelta value => value,
            OpaqueCurriculumCheckpointDelta value => ReadOpaque(value),
            _ => throw new InvalidDataException($"curriculum checkpoint delta {delta.Kind} does not belong to ReplayCalc"),
        };
        ApplyCheckpointDelta(typed, in replayContext);

        static ReplayCalcCheckpointDelta ReadOpaque(OpaqueCurriculumCheckpointDelta value)
        {
            using MemoryStream stream = new(value.Payload, writable: false);
            using CkptReader reader = new(stream);
            ReplayCalcCheckpointDelta delta = ReplayCalc.ReadCheckpointDelta(reader);
            if (reader.RemainingBytes != 0) throw new InvalidDataException("dream-calc checkpoint delta has trailing bytes");
            return delta;
        }
    }

    void ICurriculumCheckpointDeltaOwner.CommitCheckpointDelta(ICurriculumCheckpointDelta captured)
    {
        if (captured is not ReplayCalcCheckpointDelta || !string.Equals(captured.Kind, "dream-calc", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {captured.Kind} does not belong to ReplayCalc");
        ReplayCalcCheckpointDelta typed = (ReplayCalcCheckpointDelta)captured;
        CommitCheckpointDelta(in typed);
    }

    internal ReplayCalcCheckpointDelta CaptureCheckpointDelta()
    {
        if (_anytimeCurve.Points.Count == 0
            && (_checkpointAnytimePointCount != 0 || _checkpointAnytimeKillCount != 0 || _anytimeFuelCursorPresent))
            throw new InvalidDataException("ReplayCalc fresh anytime branch has stale checkpoint keyframe state");
        if (_checkpointAnytimePointCount < 0 || _checkpointAnytimePointCount > _anytimeCurve.Points.Count
            || _checkpointAnytimeKillCount < 0 || _checkpointAnytimeKillCount > _anytimeCurve.Kills.Count)
            throw new InvalidDataException("ReplayCalc anytime checkpoint cursor is outside the curve log");
        EmlAnytimeCurvePoint[] points = _anytimeCurve.Points.Skip(_checkpointAnytimePointCount).ToArray();
        EmlAnytimeKillReceipt[] kills = _anytimeCurve.Kills.Skip(_checkpointAnytimeKillCount).ToArray();
        EmlPairedFuelSchedule? schedule = _pairedFuelSchedule;
        EmlPairedFuelScheduleRow[] rows = _pairedFuelCursor is null
            ? [] : _pairedFuelCursor.ReadRows();
        bool rung0Present = _ordinaryRung0StateLoaded || _rung0Opportunities > 0
            || _rung0Audits > 0 || _relationNullExecutions > 0
            || _rung0CarrierBoundCandidates > 0 || _relationNullPairsConsidered > 0
            || _rung0FunnelReceipts.Count > 0 || _rung0CompositionDigest != 0;
        if (_checkpointRung0FunnelReceiptCount < 0 || _checkpointRung0FunnelReceiptCount > _rung0FunnelReceipts.Count)
            throw new InvalidDataException("ReplayCalc rung-0 funnel checkpoint cursor is outside its append-only receipt queue");
        EmlRung0FunnelReceipt[] rung0Receipts = _rung0FunnelReceipts
            .Skip(_checkpointRung0FunnelReceiptCount).ToArray();
        ReplayCalcRung0CheckpointDelta rung0 = new(
            rung0Present,
            _checkpointRung0FunnelReceiptCount,
            _rung0Opportunities,
            _rung0CarrierBoundCandidates,
            _rung0GuardEligibleCandidates,
            _rung0PaidAttempts,
            _rung0AttemptedCandidates,
            _rung0Compositions,
            _rung0ZeroEvaluatorCompositions,
            _rung0Audits,
            _rung0AgreedAudits,
            _rung0DisagreedAudits,
            _rung0NotSelectedAudits,
            _relationNullExecutions,
            _relationNullDivergences,
            _relationNullAuthorityPredictions,
            _relationNullPairsConsidered,
            _relationNullPairsCreated,
            _relationNullRejectNoCarrier,
            _relationNullRejectShape,
            _relationNullRejectGrade,
            _rung0CompositionDigest,
            rung0Present ? _rung0SourceDigest : string.Empty,
            rung0Present ? _rung0ConfigDigest : string.Empty,
            rung0Receipts);
        return new(
            _sieve.CaptureCheckpointDelta(),
            _sampler.CaptureCheckpointDelta(),
            _worldOpportunityCursor,
            _worldOpportunityEvents.ToArray(),
            _enumTaken,
            _enumDone,
            _minted,
            _anchor,
            _checkpointAnytimePointCount,
            points,
            _checkpointAnytimeKillCount,
            kills,
            schedule is not null,
            schedule ?? default,
            rows,
            _pairedFuelCursorDirty,
            _processExactHighWater,
            _catalanProcess,
            _zeta3Process,
            rung0,
            _lawStore.CaptureCheckpointDelta(),
            CaptureActionCheckpointDelta(),
            _anytimeCheckpointRebasePending,
            _anytimeRebasePredecessorRunID,
            _anytimeRebasePredecessorConfigID,
            _anytimeRebasePredecessorChainID,
            _anytimeRebasePredecessorArmID,
            _anytimeParentPointID,
            _anytimeRun is null ? "" : Path.GetFileName(_anytimeRun.Dir),
            _anytimeConfigID,
            _anytimeChainID,
            _anytimeArmID,
            _anytimeRung);
    }

    internal void ApplyCheckpointDelta(in ReplayCalcCheckpointDelta delta, in CheckpointReplayContext replayContext = default)
    {
        if (delta.WorldOpportunityCursor < 0 || delta.WorldOpportunityCursor > delta.WorldOpportunityEvents.Length)
            throw new InvalidDataException("ReplayCalc world-opportunity cursor is outside its lineage prefix");
        if (delta.EnumTaken < 0 || delta.Minted < 0)
            throw new InvalidDataException("ReplayCalc checkpoint counters are negative");
        if (delta.AnytimePointCursor < 0 || delta.AnytimePointCursor > _anytimeCurve.Points.Count
            || delta.AnytimeKillCursor < 0 || delta.AnytimeKillCursor > _anytimeCurve.Kills.Count)
            throw new InvalidDataException("ReplayCalc anytime checkpoint cursor has a gap");
        ReplayCalcRung0CheckpointDelta rung0Delta = delta.Rung0;
        ValidateRung0CheckpointDelta(in rung0Delta);
        if (_worldOpportunityEvents.Length > delta.WorldOpportunityEvents.Length)
            throw new InvalidDataException("ReplayCalc world-opportunity lineage regressed during replay");
        for (int i = 0; i < _worldOpportunityEvents.Length; i++)
            if (_worldOpportunityEvents[i] != delta.WorldOpportunityEvents[i])
                throw new InvalidDataException("ReplayCalc world-opportunity lineage prefix changed during replay");
        EmlSieveCheckpointDelta sieve = delta.Sieve;
        EmlSamplerCheckpointDelta sampler = delta.Sampler;
        _sieve.ApplyCheckpointDelta(in sieve);
        _sampler.LoadCheckpointDelta(in sampler);
        _worldOpportunityEvents = delta.WorldOpportunityEvents.ToArray();
        _worldOpportunityCursor = delta.WorldOpportunityCursor;
        _enumTaken = delta.EnumTaken;
        _enumDone = delta.EnumDone;
        _minted = delta.Minted;
        _anchor = delta.Anchor;
        int pairedRowsBefore = _pairedFuelCursor?.RowCount ?? -1;
        bool explicitRebase = replayContext.Present
            && replayContext.Kind == CheckpointReplayKinds.AnytimeRebase;
        if (delta.AnytimeRebase != explicitRebase)
            throw new InvalidDataException("ReplayCalc checkpoint replay kind disagrees with the anytime delta");
        bool anytimeRebase = explicitRebase;
        if (anytimeRebase)
        {
            if (delta.AnytimePointCursor != 0 || delta.AnytimePoints.Length != 0
                || delta.AnytimeKillCursor != 0 || delta.AnytimeKills.Length != 0)
                throw new InvalidDataException("ReplayCalc anytime rebase carries curve records");
            if (_anytimeCurve.Points.Count == 0)
                throw new InvalidDataException("ReplayCalc anytime rebase has no incumbent curve history");
            if (delta.AnytimeRebase && (!delta.PairedFuelConfigured || _pairedFuelSchedule is not { } rebaseSchedule
                || rebaseSchedule != delta.PairedFuelSchedule
                || delta.AnytimeRebasePredecessorRunID != _anytimeCurve.ScopeRunID
                || delta.AnytimeRebasePredecessorConfigID != _anytimeCurve.ScopeConfigID
                || delta.AnytimeRebasePredecessorChainID != _anytimeCurve.ScopeChainID
                || delta.AnytimeRebasePredecessorArmID != _anytimeCurve.ScopeArmID
                || delta.AnytimeRebasePredecessorPointID.Length == 0
                || delta.AnytimeRebasePredecessorPointID != _anytimeCurve.Digest
                || delta.AnytimeRebaseSuccessorRunID.Length == 0
                || delta.AnytimeRebaseSuccessorConfigID.Length == 0
                || delta.AnytimeRebaseSuccessorChainID.Length == 0
                || delta.AnytimeRebaseSuccessorArmID.Length == 0
                || delta.AnytimeRebaseSuccessorRung < _anytimeCurve.ScopeRung
                || replayContext.RailRunID != delta.AnytimeRebaseSuccessorRunID
                || replayContext.BasePhysicalSHA256.Length != 64
                || replayContext.BaseLogicalSHA256.Length != 64
                || replayContext.Sequence < 0
                || replayContext.PreviousRecordSHA256.Length != 64
                || replayContext.RecordSHA256.Length != 64
                || replayContext.PredecessorCurveDigest != _anytimeCurve.Digest
                || replayContext.PredecessorParentPointID != delta.AnytimeRebasePredecessorPointID
                || replayContext.SuccessorRunID != delta.AnytimeRebaseSuccessorRunID
                || replayContext.SuccessorConfigID != delta.AnytimeRebaseSuccessorConfigID
                || replayContext.SuccessorChainID != delta.AnytimeRebaseSuccessorChainID
                || replayContext.SuccessorArmID != delta.AnytimeRebaseSuccessorArmID
                || replayContext.ScheduleDigest != delta.PairedFuelSchedule.Digest))
                throw new InvalidDataException("ReplayCalc anytime rebase scope or schedule custody changed");
            Trace.Cortex.Boundary("checkpoint.replay.anytime-rebase",
                $"explicit=1 reset={_anytimeCurve.Points.Count}/{_anytimeCurve.Kills.Count} "
                + $"paired={pairedRowsBefore}->{delta.PairedFuelRows.Length} window={replayContext.WindowStartStep}..{replayContext.WindowEndStep}");
        }
        EmlAnytimeRebaseScope? successorScope = anytimeRebase
            ? new EmlAnytimeRebaseScope(
                delta.AnytimeRebaseSuccessorRunID,
                delta.AnytimeRebaseSuccessorConfigID,
                delta.AnytimeRebaseSuccessorChainID,
                delta.AnytimeRebaseSuccessorArmID,
                delta.AnytimeRebaseSuccessorRung,
                delta.AnytimeRebasePredecessorPointID).Validate()
            : null;
        _anytimeCurve.ApplyCheckpointDelta(delta.AnytimePointCursor, delta.AnytimePoints,
            delta.AnytimeKillCursor, delta.AnytimeKills, successorScope);
        _checkpointAnytimePointCount = _anytimeCurve.Points.Count;
        _checkpointAnytimeKillCount = _anytimeCurve.Kills.Count;
        if (delta.PairedFuelConfigured)
        {
            EmlPairedFuelSchedule schedule = delta.PairedFuelSchedule.Validate();
            if (_pairedFuelSchedule is { } configured && configured != schedule)
                throw new InvalidDataException("ReplayCalc paired-fuel schedule changed during replay");
            _pairedFuelSchedule = schedule;
            EmlPairedFuelScheduleRow[] rows = delta.PairedFuelRows;
            _pairedFuelCursor = EmlPairedFuelScheduleCursor.FromRows(in schedule, rows);
        }
        else if (_pairedFuelSchedule is not null || delta.PairedFuelRows.Length != 0)
            throw new InvalidDataException("ReplayCalc paired-fuel delta disagrees with configured schedule");
        _pairedFuelCursorDirty = delta.PairedFuelCursorDirty;
        _processExactHighWater = delta.ProcessExactHighWater;
        _catalanProcess = delta.CatalanProcess;
        _zeta3Process = delta.Zeta3Process;
        ValidateLoadedState(_catalanProcess);
        ValidateLoadedState(_zeta3Process);
        ApplyRung0CheckpointDelta(in rung0Delta);
        EmlLawStoreCheckpointDelta lawStore = delta.LawStore;
        _lawStore.ApplyCheckpointDelta(in lawStore);
        EmlActionCheckpointDelta action = delta.Action;
        ApplyActionCheckpointDelta(in action);
        _enum = EmlGen.Enumerate(_seedK + 2, _maxEnum).GetEnumerator();
        for (int i = 0; i < _enumTaken; i++)
            if (!_enum.MoveNext()) throw new InvalidDataException("ReplayCalc enumeration cursor exceeds its deterministic walk");
    }

    internal void CommitCheckpointDelta(in ReplayCalcCheckpointDelta delta)
    {
        EmlSieveCheckpointDelta sieve = delta.Sieve;
        _sieve.CommitCheckpointDelta(in sieve);
        _checkpointAnytimePointCount = _anytimeCurve.Points.Count;
        _checkpointAnytimeKillCount = _anytimeCurve.Kills.Count;
        _anytimeCheckpointRebasePending = false;
        _checkpointRung0FunnelReceiptCount = _rung0FunnelReceipts.Count;
        _lawStore.CommitCheckpointDelta();
        EmlActionCheckpointDelta action = delta.Action;
        CommitActionCheckpointDelta(in action);
    }

    internal void CommitCheckpointDelta()
    {
        EmlSieveCheckpointDelta sieve = _sieve.CaptureCheckpointDelta();
        _sieve.CommitCheckpointDelta(in sieve);
        _checkpointAnytimePointCount = _anytimeCurve.Points.Count;
        _checkpointAnytimeKillCount = _anytimeCurve.Kills.Count;
        _anytimeCheckpointRebasePending = false;
        _checkpointRung0FunnelReceiptCount = _rung0FunnelReceipts.Count;
        _lawStore.CommitCheckpointDelta();
        EmlActionCheckpointDelta action = CaptureActionCheckpointDelta();
        CommitActionCheckpointDelta(in action);
    }

    internal static bool VerifyCheckpointReplayKindFixture(string root)
    {
        static bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }

        ReplayCalc Prepare(string name)
        {
            ReplayCalc dream = Mount(0xC0FFEEUL);
            EmlDeliberationQuota quota = EmlDeliberationQuota.TightAssay;
            dream.ConfigurePairedFuelSchedule(2, in quota);
            dream.BindAnytimeRun(Run.Create(Path.Combine(root, name)), "replay-kind-config", "replay-kind-chain", "replay-kind-arm");
            _ = dream.CaptureDeepRematchEvaluationHandshake();
            dream.CommitCheckpointDelta();
            return dream;
        }

        ReplayCalc source = Prepare("replay-kind-source");
        EmlPairedFuelSchedule schedule = source.PairedFuelSchedule;
        EmlDeliberationCounts zero = EmlDeliberationCounts.Zero;
        EmlDeliberationCounts planned = schedule.Row(0);
        EmlPairedFuelScheduleCursor sourceCursor = source.PairedFuelScheduleCursor;
        source._pairedFuelCursor = sourceCursor.Append(in schedule, 0, in planned, in zero);
        ReplayCalcCheckpointDelta ordinaryDelta = source.CaptureCheckpointDelta();

        CheckpointReplayContext normalContext = new CheckpointReplayContext(0, 2, Bound: true).BindRecord(1, 2);
        ReplayCalc ordinaryTarget = Prepare("replay-kind-ordinary");
        ordinaryTarget.ApplyCheckpointDelta(in ordinaryDelta, in normalContext);
        bool ordinaryRowGrowthAccepted = ordinaryTarget.PairedFuelScheduleCursor.RowCount == 1
            && ordinaryTarget.AnytimeCurve.Points.Count == 1;

        ReplayCalc legacyTarget = Prepare("replay-kind-legacy");
        ReplayCalcCheckpointDelta legacyImplicit = ordinaryDelta with { AnytimePointCursor = 0 };
        bool legacyImplicitRejected = Rejects(() => legacyTarget.ApplyCheckpointDelta(in legacyImplicit));

        ReplayCalc explicitTarget = Prepare("replay-kind-explicit");
        string predecessorRun = explicitTarget.AnytimeCurve.ScopeRunID;
        ReplayCalcCheckpointDelta explicitDelta = ordinaryDelta with
        {
            AnytimePointCursor = 0,
            AnytimeRebase = true,
            AnytimeRebasePredecessorRunID = predecessorRun,
            AnytimeRebasePredecessorConfigID = explicitTarget.AnytimeCurve.ScopeConfigID,
            AnytimeRebasePredecessorChainID = explicitTarget.AnytimeCurve.ScopeChainID,
            AnytimeRebasePredecessorArmID = explicitTarget.AnytimeCurve.ScopeArmID,
            AnytimeRebasePredecessorPointID = explicitTarget.AnytimeCurve.Digest,
            AnytimeRebaseSuccessorRunID = "replay-kind-successor",
            AnytimeRebaseSuccessorConfigID = "replay-kind-successor-config",
            AnytimeRebaseSuccessorChainID = "replay-kind-successor-chain",
            AnytimeRebaseSuccessorArmID = "replay-kind-successor-arm",
        };
        CheckpointReplayContext explicitContext = new CheckpointReplayContext(0, 2, CheckpointReplayKinds.AnytimeRebase,
            ConfigDigest: "replay-kind-config-digest",
            RailRunID: explicitDelta.AnytimeRebaseSuccessorRunID,
            PredecessorCurveDigest: explicitTarget.AnytimeCurve.Digest,
            PredecessorParentPointID: explicitTarget.AnytimeCurve.Digest,
            SuccessorRunID: explicitDelta.AnytimeRebaseSuccessorRunID,
            SuccessorConfigID: explicitDelta.AnytimeRebaseSuccessorConfigID,
            SuccessorChainID: explicitDelta.AnytimeRebaseSuccessorChainID,
            SuccessorArmID: explicitDelta.AnytimeRebaseSuccessorArmID,
            ScheduleDigest: explicitDelta.PairedFuelSchedule.Digest,
            BasePhysicalSHA256: new string('a', 64),
            BaseLogicalSHA256: new string('b', 64),
            Sequence: 0,
            PreviousRecordSHA256: new string('0', 64),
            RecordSHA256: new string('c', 64),
            Bound: true).BindRecord(1, 2);
        explicitTarget.ApplyCheckpointDelta(in explicitDelta, in explicitContext);
        bool explicitRebaseAccepted = explicitTarget.AnytimeCurve.Points.Count == 0
            && explicitTarget.PairedFuelScheduleCursor.RowCount == 1;

        ReplayCalc preV5Target = Prepare("replay-kind-prev5");
        bool preV5ExplicitRejected = Rejects(() => preV5Target.ApplyCheckpointDelta(in explicitDelta));

        ReplayCalc detachedTarget = Prepare("replay-kind-detached");
        ReplayCalcCheckpointDelta detachedDelta = explicitDelta with { AnytimeRebasePredecessorPointID = "detached-parent" };
        bool detachedParentRejected = Rejects(() => detachedTarget.ApplyCheckpointDelta(in detachedDelta, in explicitContext));

        ReplayCalc mismatchTarget = Prepare("replay-kind-mismatch");
        bool kindMismatchRejected = Rejects(() => mismatchTarget.ApplyCheckpointDelta(in ordinaryDelta, in explicitContext));
        return ordinaryRowGrowthAccepted && legacyImplicitRejected && explicitRebaseAccepted
            && preV5ExplicitRejected && detachedParentRejected && kindMismatchRejected;
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in ReplayCalcCheckpointDelta delta)
    {
        writer.U8(5);
        EmlSieveCheckpointDelta sieve = delta.Sieve;
        EmlSamplerCheckpointDelta sampler = delta.Sampler;
        EmlSieve.WriteCheckpointDelta(writer, in sieve);
        EmlSampler.WriteCheckpointDelta(writer, in sampler);
        writer.I32(delta.WorldOpportunityCursor); writer.I32(delta.WorldOpportunityEvents.Length);
        foreach (TapeEventID id in delta.WorldOpportunityEvents) writer.I64(id.Value);
        writer.I32(delta.EnumTaken); writer.Bool(delta.EnumDone); writer.I32(delta.Minted);
        writer.Bool(delta.Anchor is not null); if (delta.Anchor is not null) writer.Str(delta.Anchor);
        writer.I32(delta.AnytimePointCursor); writer.I32(delta.AnytimePoints.Length);
        foreach (EmlAnytimeCurvePoint point in delta.AnytimePoints) EmlAnytimeCurve.WriteCheckpointPoint(writer, in point);
        writer.I32(delta.AnytimeKillCursor); writer.I32(delta.AnytimeKills.Length);
        foreach (EmlAnytimeKillReceipt kill in delta.AnytimeKills) EmlAnytimeCurve.WriteCheckpointKill(writer, in kill);
        writer.Bool(delta.AnytimeRebase);
        if (delta.AnytimeRebase)
        {
            writer.Str(delta.AnytimeRebasePredecessorRunID); writer.Str(delta.AnytimeRebasePredecessorConfigID);
            writer.Str(delta.AnytimeRebasePredecessorChainID); writer.Str(delta.AnytimeRebasePredecessorArmID); writer.Str(delta.AnytimeRebasePredecessorPointID);
            writer.Str(delta.AnytimeRebaseSuccessorRunID); writer.Str(delta.AnytimeRebaseSuccessorConfigID);
            writer.Str(delta.AnytimeRebaseSuccessorChainID); writer.Str(delta.AnytimeRebaseSuccessorArmID);
            writer.I32(delta.AnytimeRebaseSuccessorRung);
        }
        writer.Bool(delta.PairedFuelConfigured);
        if (delta.PairedFuelConfigured)
        {
            EmlPairedFuelSchedule schedule = delta.PairedFuelSchedule;
            EmlDeliberationCounts total = schedule.Total;
            writer.Str(schedule.Identity); writer.I32(schedule.Horizon); WriteCounts(writer, in total); writer.Str(schedule.Digest);
            writer.I32(delta.PairedFuelRows.Length);
            foreach (EmlPairedFuelScheduleRow row in delta.PairedFuelRows)
            {
                EmlDeliberationCounts planned = row.Planned, actual = row.Actual, refund = row.Refund;
                writer.I32(row.Step); WriteCounts(writer, in planned); WriteCounts(writer, in actual); WriteCounts(writer, in refund);
                writer.Str(row.PreviousDigest); writer.Str(row.Digest);
            }
        }
        writer.Bool(delta.PairedFuelCursorDirty);
        EmlProcessConstantState catalan = delta.CatalanProcess, zeta3 = delta.Zeta3Process;
        writer.I32(delta.ProcessExactHighWater); WriteProcessState(writer, in catalan); WriteProcessState(writer, in zeta3);
        ReplayCalcRung0CheckpointDelta rung0 = delta.Rung0;
        WriteRung0CheckpointDelta(writer, in rung0);
        EmlLawStoreCheckpointDelta lawStore = delta.LawStore;
        EmlLawStore.WriteCheckpointDelta(writer, in lawStore);
        EmlActionCheckpointDelta action = delta.Action;
        WriteActionCheckpointDelta(writer, in action);
    }

    internal static ReplayCalcCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte schema = reader.U8();
        if (schema is not (4 or 5)) throw new InvalidDataException("unknown ReplayCalc checkpoint delta version");
        EmlSieveCheckpointDelta sieve = EmlSieve.ReadCheckpointDelta(reader);
        EmlSamplerCheckpointDelta sampler = EmlSampler.ReadCheckpointDelta(reader);
        int worldCursor = reader.I32(); int worldCount = reader.I32();
        if (worldCursor < 0 || worldCount < 0 || worldCount > 1_000_000 || worldCursor > worldCount)
            throw new InvalidDataException("ReplayCalc world-opportunity delta is malformed");
        TapeEventID[] events = new TapeEventID[worldCount];
        for (int i = 0; i < events.Length; i++) { events[i] = new(reader.I64()); if (i > 0 && events[i].Value <= events[i - 1].Value) throw new InvalidDataException("ReplayCalc world-opportunity lineage is not ordered"); }
        int enumTaken = reader.I32(); bool enumDone = reader.Bool(); int minted = reader.I32();
        string? anchor = reader.Bool() ? reader.Str() : null;
        int pointCursor = reader.I32(); int pointCount = reader.I32();
        if (pointCursor < 0 || pointCount < 0 || pointCount > 10_000_000) throw new InvalidDataException("ReplayCalc anytime point delta is malformed");
        EmlAnytimeCurvePoint[] points = new EmlAnytimeCurvePoint[pointCount]; for (int i = 0; i < points.Length; i++) points[i] = EmlAnytimeCurve.ReadCheckpointPoint(reader);
        int killCursor = reader.I32(); int killCount = reader.I32();
        if (killCursor < 0 || killCount < 0 || killCount > pointCount + 4) throw new InvalidDataException("ReplayCalc anytime kill delta is malformed");
        EmlAnytimeKillReceipt[] kills = new EmlAnytimeKillReceipt[killCount]; for (int i = 0; i < kills.Length; i++) kills[i] = EmlAnytimeCurve.ReadCheckpointKill(reader);
        bool anytimeRebase = schema >= 5 && reader.Bool();
        string predecessorRunID = "", predecessorConfigID = "", predecessorChainID = "", predecessorArmID = "", predecessorPointID = "";
        string successorRunID = "", successorConfigID = "", successorChainID = "", successorArmID = "";
        int successorRung = 0;
        if (anytimeRebase)
        {
            predecessorRunID = reader.Str(); predecessorConfigID = reader.Str(); predecessorChainID = reader.Str(); predecessorArmID = reader.Str();
            if (schema >= 5) predecessorPointID = reader.Str();
            successorRunID = reader.Str(); successorConfigID = reader.Str(); successorChainID = reader.Str(); successorArmID = reader.Str(); successorRung = reader.I32();
        }
        bool paired = reader.Bool(); EmlPairedFuelSchedule schedule = default; EmlPairedFuelScheduleRow[] rows = [];
        if (paired)
        {
            string identity = reader.Str(); int horizon = reader.I32(); EmlDeliberationCounts total = ReadCounts(reader); string digest = reader.Str();
            schedule = new EmlPairedFuelSchedule(identity, horizon, total, digest).Validate();
            int count = reader.I32(); if (count < 0 || count > horizon) throw new InvalidDataException("ReplayCalc paired-fuel rows exceed schedule horizon");
            rows = new EmlPairedFuelScheduleRow[count]; for (int i = 0; i < count; i++) rows[i] = new(reader.I32(), ReadCounts(reader), ReadCounts(reader), ReadCounts(reader), reader.Str(), reader.Str());
        }
        bool dirty = reader.Bool();
        int processHighWater = reader.I32(); EmlProcessConstantState catalan = ReadProcessState(reader); EmlProcessConstantState zeta3 = ReadProcessState(reader);
        ReplayCalcRung0CheckpointDelta rung0 = ReadRung0CheckpointDelta(reader);
        EmlLawStoreCheckpointDelta lawStore = EmlLawStore.ReadCheckpointDelta(reader);
        EmlActionCheckpointDelta action = ReadActionCheckpointDelta(reader);
        return new(sieve, sampler, worldCursor, events, enumTaken, enumDone, minted, anchor, pointCursor, points, killCursor, kills, paired, schedule, rows, dirty, processHighWater, catalan, zeta3, rung0, lawStore, action, anytimeRebase, predecessorRunID, predecessorConfigID, predecessorChainID, predecessorArmID, predecessorPointID, successorRunID, successorConfigID, successorChainID, successorArmID, successorRung);
    }

    private void ValidateRung0CheckpointDelta(in ReplayCalcRung0CheckpointDelta delta)
    {
        if (delta.FunnelCursor < 0 || delta.FunnelCursor != _rung0FunnelReceipts.Count)
            throw new InvalidDataException("ReplayCalc rung-0 funnel checkpoint cursor has a gap");
        if (!delta.Present)
        {
            if (delta.FunnelReceipts.Length != 0 || delta.Opportunities != 0 || delta.CarrierBoundCandidates != 0
                || delta.GuardEligibleCandidates != 0 || delta.PaidAttempts != 0 || delta.AttemptedCandidates != 0
                || delta.Compositions != 0 || delta.ZeroEvaluatorCompositions != 0 || delta.Audits != 0
                || delta.AgreedAudits != 0 || delta.DisagreedAudits != 0 || delta.NotSelectedAudits != 0
                || delta.RelationNullExecutions != 0 || delta.RelationNullDivergences != 0
                || delta.RelationNullAuthorityPredictions != 0 || delta.RelationNullPairsConsidered != 0
                || delta.RelationNullPairsCreated != 0 || delta.RelationNullRejectNoCarrier != 0
                || delta.RelationNullRejectShape != 0 || delta.RelationNullRejectGrade != 0
                || delta.CompositionDigest != 0 || delta.SourceDigest.Length != 0 || delta.ConfigDigest.Length != 0)
                throw new InvalidDataException("ReplayCalc absent rung-0 state carries non-zero replacement fields");
            return;
        }
        if (!string.Equals(delta.SourceDigest, _rung0SourceDigest, StringComparison.Ordinal)
            || !string.Equals(delta.ConfigDigest, _rung0ConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException("ReplayCalc rung-0 checkpoint digest configuration drifted");
        if (delta.Opportunities < 0 || delta.CarrierBoundCandidates < 0 || delta.GuardEligibleCandidates < 0
            || delta.PaidAttempts < 0 || delta.AttemptedCandidates < 0 || delta.Compositions < 0
            || delta.ZeroEvaluatorCompositions < 0 || delta.Audits < 0 || delta.AgreedAudits < 0
            || delta.DisagreedAudits < 0 || delta.NotSelectedAudits < 0 || delta.RelationNullExecutions < 0
            || delta.RelationNullDivergences < 0 || delta.RelationNullAuthorityPredictions < 0
            || delta.RelationNullPairsConsidered < 0 || delta.RelationNullPairsCreated < 0
            || delta.RelationNullRejectNoCarrier < 0 || delta.RelationNullRejectShape < 0
            || delta.RelationNullRejectGrade < 0 || delta.Compositions > delta.AttemptedCandidates
            || delta.ZeroEvaluatorCompositions > delta.Compositions
            || delta.Audits != delta.AgreedAudits + delta.DisagreedAudits + delta.NotSelectedAudits
            || delta.RelationNullDivergences > delta.RelationNullExecutions
            || delta.RelationNullAuthorityPredictions > delta.RelationNullExecutions
            || delta.GuardEligibleCandidates > delta.CarrierBoundCandidates
            || delta.RelationNullPairsCreated > delta.RelationNullPairsConsidered)
            throw new InvalidDataException("ReplayCalc rung-0 checkpoint counters do not close");
        for (int i = 0; i < delta.FunnelReceipts.Length; i++)
        {
            EmlRung0FunnelReceipt receipt = delta.FunnelReceipts[i];
            if (!Enum.IsDefined(receipt.Stage) || receipt.ObligationID is null || receipt.Reason is null)
                throw new InvalidDataException("ReplayCalc rung-0 funnel checkpoint receipt is malformed");
        }
    }

    private void ApplyRung0CheckpointDelta(in ReplayCalcRung0CheckpointDelta delta)
    {
        _ordinaryRung0StateLoaded = delta.Present;
        _rung0Opportunities = delta.Opportunities;
        _rung0CarrierBoundCandidates = delta.CarrierBoundCandidates;
        _rung0GuardEligibleCandidates = delta.GuardEligibleCandidates;
        _rung0PaidAttempts = delta.PaidAttempts;
        _rung0AttemptedCandidates = delta.AttemptedCandidates;
        _rung0Compositions = delta.Compositions;
        _rung0ZeroEvaluatorCompositions = delta.ZeroEvaluatorCompositions;
        _rung0Audits = delta.Audits;
        _rung0AgreedAudits = delta.AgreedAudits;
        _rung0DisagreedAudits = delta.DisagreedAudits;
        _rung0NotSelectedAudits = delta.NotSelectedAudits;
        _relationNullExecutions = delta.RelationNullExecutions;
        _relationNullDivergences = delta.RelationNullDivergences;
        _relationNullAuthorityPredictions = delta.RelationNullAuthorityPredictions;
        _relationNullPairsConsidered = delta.RelationNullPairsConsidered;
        _relationNullPairsCreated = delta.RelationNullPairsCreated;
        _relationNullRejectNoCarrier = delta.RelationNullRejectNoCarrier;
        _relationNullRejectShape = delta.RelationNullRejectShape;
        _relationNullRejectGrade = delta.RelationNullRejectGrade;
        _rung0CompositionDigest = delta.CompositionDigest;
        if (delta.Present)
        {
            _rung0SourceDigest = delta.SourceDigest;
            _rung0ConfigDigest = delta.ConfigDigest;
        }
        if (delta.FunnelReceipts.Length != 0)
            _rung0FunnelReceipts.AddRange(delta.FunnelReceipts);
        _checkpointRung0FunnelReceiptCount = _rung0FunnelReceipts.Count;
    }

    private static void WriteCounts(CkptWriter w, in EmlDeliberationCounts c)
    { w.I64(c.CandidateEvaluations); w.I64(c.LogicalProgramPoints); w.I64(c.ExecutedProgramPoints); w.I64(c.InverseTransforms); w.I64(c.HashProbes); w.I64(c.JoinAttempts); w.I64(c.JoinHits); w.I64(c.ProcessTerms); w.I64(c.VerifierProgramPoints); w.I64(c.CandidateSupplyItems); w.I64(c.LawRewriteApplications); w.I64(c.LawRewriteTreeNodes); }
    private static EmlDeliberationCounts ReadCounts(CkptReader r)
        => new(r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64(), r.I64());

    private static void WriteRung0CheckpointDelta(CkptWriter writer, in ReplayCalcRung0CheckpointDelta delta)
    {
        writer.Bool(delta.Present);
        writer.I32(delta.FunnelCursor);
        writer.I32(delta.Opportunities); writer.I32(delta.CarrierBoundCandidates); writer.I32(delta.GuardEligibleCandidates);
        writer.I32(delta.PaidAttempts); writer.I32(delta.AttemptedCandidates); writer.I32(delta.Compositions);
        writer.I32(delta.ZeroEvaluatorCompositions); writer.I32(delta.Audits); writer.I32(delta.AgreedAudits);
        writer.I32(delta.DisagreedAudits); writer.I32(delta.NotSelectedAudits);
        writer.I32(delta.RelationNullExecutions); writer.I32(delta.RelationNullDivergences); writer.I32(delta.RelationNullAuthorityPredictions);
        writer.I32(delta.RelationNullPairsConsidered); writer.I32(delta.RelationNullPairsCreated);
        writer.I32(delta.RelationNullRejectNoCarrier); writer.I32(delta.RelationNullRejectShape); writer.I32(delta.RelationNullRejectGrade);
        writer.U64(delta.CompositionDigest); writer.Str(delta.SourceDigest); writer.Str(delta.ConfigDigest);
        writer.I32(delta.FunnelReceipts.Length);
        for (int i = 0; i < delta.FunnelReceipts.Length; i++)
        {
            EmlRung0FunnelReceipt receipt = delta.FunnelReceipts[i];
            WriteRung0FunnelReceipt(writer, in receipt);
        }
    }

    private static ReplayCalcRung0CheckpointDelta ReadRung0CheckpointDelta(CkptReader reader)
    {
        bool present = reader.Bool();
        int cursor = reader.I32();
        if (cursor < 0) throw new InvalidDataException("ReplayCalc rung-0 funnel checkpoint cursor is negative");
        int opportunities = reader.I32(); int carrier = reader.I32(); int eligible = reader.I32();
        int funded = reader.I32(); int attempted = reader.I32(); int derivations = reader.I32();
        int zeroEvaluator = reader.I32(); int audits = reader.I32(); int agreed = reader.I32();
        int disagreed = reader.I32(); int notSelected = reader.I32();
        int nullExecutions = reader.I32(); int nullDivergences = reader.I32(); int nullAuthority = reader.I32();
        int pairsConsidered = reader.I32(); int pairsCreated = reader.I32();
        int rejectNoCarrier = reader.I32(); int rejectShape = reader.I32(); int rejectGrade = reader.I32();
        ulong digest = reader.U64(); string sourceDigest = reader.Str(); string configDigest = reader.Str();
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("ReplayCalc rung-0 funnel receipt count is invalid");
        EmlRung0FunnelReceipt[] receipts = new EmlRung0FunnelReceipt[count];
        for (int i = 0; i < count; i++) receipts[i] = ReadRung0FunnelReceipt(reader);
        if (!present && (count != 0 || opportunities != 0 || derivations != 0 || audits != 0 || digest != 0
            || sourceDigest.Length != 0 || configDigest.Length != 0))
            throw new InvalidDataException("ReplayCalc absent rung-0 checkpoint state is not zero");
        return new(present, cursor, opportunities, carrier, eligible, funded, attempted, derivations,
            zeroEvaluator, audits, agreed, disagreed, notSelected, nullExecutions, nullDivergences,
            nullAuthority, pairsConsidered, pairsCreated, rejectNoCarrier, rejectShape, rejectGrade,
            digest, sourceDigest, configDigest, receipts);
    }

    private static void WriteRung0FunnelReceipt(CkptWriter writer, in EmlRung0FunnelReceipt receipt)
    {
        writer.I32((int)receipt.Stage); writer.I32(receipt.ObligationPredictionID.Value); writer.Str(receipt.ObligationID ?? string.Empty);
        writer.Str(receipt.RuleID.Value ?? string.Empty); writer.Bool(receipt.Accepted); writer.Str(receipt.Reason ?? string.Empty);
        writer.Str(receipt.ProofID ?? string.Empty); writer.Str(receipt.AuditID ?? string.Empty);
        writer.Str(receipt.AdmissionID ?? string.Empty); writer.Str(receipt.ClosureID ?? string.Empty);
        writer.I64(receipt.Evaluation.Start); writer.I64(receipt.Evaluation.End);
        writer.Bool(receipt.RelationNullDonor.HasValue);
        if (receipt.RelationNullDonor is EmlRelationNullDonorProvenance donor)
        {
            writer.I32(donor.SourcePredictionID.Value); writer.Str(donor.ObligationID);
            writer.I32(donor.SupportEventIDs.Count);
            for (int i = 0; i < donor.SupportEventIDs.Count; i++) writer.I64(donor.SupportEventIDs[i].Value);
            writer.I32(donor.LawAdmissionIDs.Count);
            for (int i = 0; i < donor.LawAdmissionIDs.Count; i++) writer.Str(donor.LawAdmissionIDs[i]);
        }
    }

    private static EmlRung0FunnelReceipt ReadRung0FunnelReceipt(CkptReader reader)
    {
        int stage = reader.I32();
        if (stage < (int)EmlRung0FunnelStages.Opportunity || stage > (int)EmlRung0FunnelStages.RelationNull)
            throw new InvalidDataException("ReplayCalc rung-0 funnel receipt stage is invalid");
        EmlRelationNullDonorProvenance? donor = null;
        EmlPredictionID claim = new(reader.I32());
        string obligation = reader.Str(); EmlRuleID rule = new(reader.Str()); bool accepted = reader.Bool(); string reason = reader.Str();
        string proof = reader.Str(); string audit = reader.Str(); string admission = reader.Str(); string closure = reader.Str();
        EmlEvaluatorInterval evaluation = new(reader.I64(), reader.I64());
        if (reader.Bool()) donor = ReadRelationNullDonorProvenance(reader);
        return new((EmlRung0FunnelStages)stage, claim, obligation, rule, accepted, reason, proof, audit, admission, closure, evaluation, donor);
    }
}
