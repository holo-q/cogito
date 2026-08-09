namespace Cogito;

public sealed partial class ReplayCalc
{
    private int _checkpointProcedureSolveDeltaCount;

    internal EmlActionCheckpointDelta CaptureActionCheckpointDelta()
    {
        if (_actionSelection == EmlActionSelections.Off) return default;
        if (_actionMeter is null) throw new InvalidOperationException("armed EML action state has no outcome meter");
        if (_actionInFlight || _actionMeter.PendingIndex >= 0 || _sharedActionOutcomePending)
            throw new InvalidOperationException("EML action deltas are valid only between completed action slots");
        // Solve deltas are intentionally a replacement: Cortex clears this list
        // at each action and uses it only as the current action's evidence.
        // Keeping a cursor over that ephemeral list would silently drop results
        // whenever a later action has fewer deltas.
        EmlCertificateDelta[] solveDeltas = _procedureSolveDeltas.ToArray();
        Dictionary<EmlPredictionID, EmlObligationSearchState> previousSearch = _checkpointObligationSearch ?? new();
        // Deltas ship updates + removals only; the full-state slots ride empty (a fresh capture always
        // carries non-null updates, so the legacy full-replacement apply path never reads them).
        List<EmlActionObligationSearchDelta> changedSearches = new();
        foreach (KeyValuePair<EmlPredictionID, EmlObligationSearchState> pair in _obligationSearch)
        {
            if (previousSearch.TryGetValue(pair.Key, out EmlObligationSearchState previous)
                && previous.Epoch == pair.Value.Epoch && previous.Attempts == pair.Value.Attempts) continue;
            changedSearches.Add(new EmlActionObligationSearchDelta(pair.Key, pair.Value.Epoch, pair.Value.Attempts));
        }
        changedSearches.Sort(static (left, right) => left.PredictionID.Value.CompareTo(right.PredictionID.Value));
        EmlActionObligationSearchDelta[] searchUpdates = changedSearches.ToArray();
        EmlPredictionID[] searchRemovals = previousSearch.Keys
            .Where(claim => !_obligationSearch.ContainsKey(claim))
            .OrderBy(static claim => claim.Value).ToArray();
        string[] counterexampleUpdates = _counterexampleOrder
            .Skip(_checkpointCounterexampleCount).Order(StringComparer.Ordinal).ToArray();
        return new(
            _actionSelection, _actionRng, _actionDecision, _roundRobinCursor,
            _actionEnumTaken, _actionEnumRuler, _actionEnumDone,
            _actionMeter.CaptureArmStates(), _actionMeter.PendingIndex, _currentActionArm,
            (int[])_actionOffers.Clone(), (long[])_actionEvaluatorCalls.Clone(),
            (int[])_actionFirstCaptures.Clone(), (double[])_actionDeltaOutcomes.Clone(),
            _stressCursor, _stressExactTests, _stressExactRefuted,
            _stressAsymptoticTests, _stressAsymptoticRefuted,
            _stressControlTests, _stressControlRefuted,
            Array.Empty<string>(),
            _pendingCounterexample, (int[])_actionSelectionCauses.Clone(),
            _actionFallbacks, _actionGlobalYield, _actionGlobalOutcomes,
            _sharedCanonicalDeltas, _sharedFirstCaptures,
            _actionProcedure?.CaptureCheckpointState(),
            0, solveDeltas,
            _proceduresStarted, _proceduresCompleted, _procedureBindings, _procedureShuffledBindings,
            _procedureObligationMatches, _procedureNewDeltas, _procedureGuardsPassed,
            _procedureGuardsSkipped, _procedureGuardsAbstained, _procedureCanonicalDeltas,
            _holeCursor, _intrinsicFrontierResidual, _actionBatchHadCanonicalDelta,
            _discoveryEpoch, Array.Empty<EmlActionObligationSearchDelta>(), _obligationSearchAttempts, _obligationSearchSuppressions,
            _obligationSearchRevivals, _obligationSuppressedCalls, _executionAdmissions,
            _executionAffirmSkips, _hypothesisCapSkips, _firstGenerativeDecision, _firstGenerativeStep,
            searchUpdates, searchRemovals, counterexampleUpdates);
    }

    internal void ApplyActionCheckpointDelta(in EmlActionCheckpointDelta delta)
    {
        if (_actionSelection == EmlActionSelections.Off) return;
        if (_actionMeter is null) throw new InvalidOperationException("armed EML action state has no outcome meter");
        if (delta.Selection != _actionSelection)
            throw new InvalidDataException($"EML action checkpoint mode drifted ({delta.Selection} != {_actionSelection})");
        // Compare is a reporting-only slot; the live OutcomeMeter is mounted
        // over ActionArmOrder (four executable arms).  Persist the meter's
        // actual cardinality rather than the five-column report projection.
        ValidateFixed(delta.ArmStates, _actionMeter.Count, "arm state");
        ValidateFixed(delta.ActionOffers, ReportArmOrder.Length, "action offer");
        ValidateFixed(delta.ActionEvaluatorCalls, ReportArmOrder.Length, "action evaluator");
        ValidateFixed(delta.ActionFirstCaptures, ReportArmOrder.Length, "action first-capture");
        ValidateFixed(delta.ActionDeltaOutcomes, ReportArmOrder.Length, "action outcome");
        ValidateFixed(delta.SelectionCauses, _actionSelectionCauses.Length, "action selection cause");
        if (delta.PendingArmIndex != -1)
            throw new InvalidDataException("EML action delta carries a pending arm across a completed-slot boundary");
        if (delta.ProcedureSolveDeltaCursor != 0)
            throw new InvalidDataException("EML procedure solve-delta replacement carries a non-zero cursor");
        if (delta.Counterexamples.Length > 1_000_000 || delta.ObligationSearch.Length > 1_000_000
            || delta.CounterexampleUpdates is { Length: > 1_000_000 })
            throw new InvalidDataException("EML action replacement exceeds bound");
        _actionRng = delta.ActionRng; _actionDecision = delta.ActionDecision; _roundRobinCursor = delta.RoundRobinCursor;
        _actionMeter.ApplyArmStates(delta.ArmStates); _actionMeter.RestorePendingIndex(delta.PendingArmIndex);
        _actionEnumTaken = delta.ActionEnumTaken; _actionEnumRuler = delta.ActionEnumRuler; _actionEnumDone = delta.ActionEnumDone;
        _currentActionArm = delta.CurrentActionArm;
        Array.Copy(delta.ActionOffers, _actionOffers, _actionOffers.Length);
        Array.Copy(delta.ActionEvaluatorCalls, _actionEvaluatorCalls, _actionEvaluatorCalls.Length);
        Array.Copy(delta.ActionFirstCaptures, _actionFirstCaptures, _actionFirstCaptures.Length);
        Array.Copy(delta.ActionDeltaOutcomes, _actionDeltaOutcomes, _actionDeltaOutcomes.Length);
        _stressCursor = delta.StressCursor; _stressExactTests = delta.StressExactTests; _stressExactRefuted = delta.StressExactRefuted;
        _stressAsymptoticTests = delta.StressAsymptoticTests; _stressAsymptoticRefuted = delta.StressAsymptoticRefuted;
        _stressControlTests = delta.StressControlTests; _stressControlRefuted = delta.StressControlRefuted;
        if (delta.CounterexampleUpdates is not null)
        {
            foreach (string value in delta.CounterexampleUpdates)
                if (_counterexamplesSeen.Add(value)) _counterexampleOrder.Add(value);
        }
        else
        {
            _counterexamplesSeen.Clear(); _counterexampleOrder.Clear();
            foreach (string value in delta.Counterexamples)
                if (_counterexamplesSeen.Add(value)) _counterexampleOrder.Add(value);
        }
        _pendingCounterexample = delta.PendingCounterexample;
        Array.Copy(delta.SelectionCauses, _actionSelectionCauses, _actionSelectionCauses.Length);
        _actionFallbacks = delta.ActionFallbacks; _actionGlobalYield = delta.ActionGlobalYield; _actionGlobalOutcomes = delta.ActionGlobalOutcomes;
        _sharedCanonicalDeltas = delta.SharedCanonicalDeltas; _sharedFirstCaptures = delta.SharedFirstCaptures;
        _actionProcedure = delta.Procedure is CortexProcedureCheckpointState procedureState
            ? CortexProcedure.RestoreCheckpointState(in procedureState) : null;
        _procedureSolveDeltas.Clear();
        for (int i = 0; i < delta.ProcedureSolveDeltas.Length; i++) _procedureSolveDeltas.Add(delta.ProcedureSolveDeltas[i]);
        _checkpointProcedureSolveDeltaCount = _procedureSolveDeltas.Count;
        _proceduresStarted = delta.ProceduresStarted; _proceduresCompleted = delta.ProceduresCompleted;
        _procedureBindings = delta.ProcedureBindings; _procedureShuffledBindings = delta.ProcedureShuffledBindings;
        _procedureObligationMatches = delta.ProcedureObligationMatches; _procedureNewDeltas = delta.ProcedureNewDeltas;
        _procedureGuardsPassed = delta.ProcedureGuardsPassed; _procedureGuardsSkipped = delta.ProcedureGuardsSkipped;
        _procedureGuardsAbstained = delta.ProcedureGuardsAbstained; _procedureCanonicalDeltas = delta.ProcedureCanonicalDeltas;
        _holeCursor = delta.HoleCursor; _intrinsicFrontierResidual = delta.IntrinsicFrontierResidual;
        _actionBatchHadCanonicalDelta = delta.ActionBatchHadCanonicalDelta; _discoveryEpoch = delta.DiscoveryEpoch;
        if (delta.ObligationSearchUpdates is not null)
        {
            for (int i = 0; i < delta.ObligationSearchRemovals.Length; i++) _obligationSearch.Remove(delta.ObligationSearchRemovals[i]);
            for (int i = 0; i < delta.ObligationSearchUpdates.Length; i++)
            {
                EmlActionObligationSearchDelta search = delta.ObligationSearchUpdates[i];
                _obligationSearch[search.PredictionID] = new EmlObligationSearchState { Epoch = search.Epoch, Attempts = search.Attempts };
            }
        }
        else
        {
            _obligationSearch.Clear();
            for (int i = 0; i < delta.ObligationSearch.Length; i++)
            {
                EmlActionObligationSearchDelta search = delta.ObligationSearch[i];
                if (!_obligationSearch.TryAdd(search.PredictionID, new EmlObligationSearchState { Epoch = search.Epoch, Attempts = search.Attempts }))
                    throw new InvalidDataException("duplicate EML obligation-search claim");
            }
        }
        _obligationSearchAttempts = delta.ObligationSearchAttempts; _obligationSearchSuppressions = delta.ObligationSearchSuppressions;
        _obligationSearchRevivals = delta.ObligationSearchRevivals; _obligationSuppressedCalls = delta.ObligationSuppressedCalls;
        _executionAdmissions = delta.ExecutionAdmissions; _executionAffirmSkips = delta.ExecutionAffirmSkips;
        _hypothesisCapSkips = delta.HypothesisCapSkips; _firstGenerativeDecision = delta.FirstGenerativeDecision; _firstGenerativeStep = delta.FirstGenerativeStep;
        RebuildActionEnumeration(delta.ActionEnumRuler, delta.ActionEnumTaken, delta.ActionEnumDone);
        CaptureActionCheckpointBaseline();
    }

    internal void CommitActionCheckpointDelta(in EmlActionCheckpointDelta delta)
    {
        if (_actionSelection == EmlActionSelections.Off) return;
        if (delta.ProcedureSolveDeltaCursor != 0)
            throw new InvalidDataException("EML procedure solve-delta replacement carries a non-zero cursor");
        _checkpointProcedureSolveDeltaCount = _procedureSolveDeltas.Count;
        if (_checkpointObligationSearch is not Dictionary<EmlPredictionID, EmlObligationSearchState> baseline
            || delta.ObligationSearchUpdates is null)
        {
            CaptureActionCheckpointBaseline();
            return;
        }
        // The delta already names exactly what moved since the last baseline — advance it in O(Δ)
        // instead of re-cloning the whole obligation dictionary per commit.
        for (int i = 0; i < delta.ObligationSearchRemovals.Length; i++) baseline.Remove(delta.ObligationSearchRemovals[i]);
        for (int i = 0; i < delta.ObligationSearchUpdates.Length; i++)
        {
            EmlActionObligationSearchDelta search = delta.ObligationSearchUpdates[i];
            baseline[search.PredictionID] = new EmlObligationSearchState { Epoch = search.Epoch, Attempts = search.Attempts };
        }
        _checkpointCounterexampleCount = _counterexampleOrder.Count;
    }

    private static void ValidateFixed<T>(T[] values, int expected, string name)
    {
        if (values is null || values.Length != expected) throw new InvalidDataException($"EML {name} state count is {values?.Length ?? -1}, expected {expected}");
    }

    internal static void WriteActionCheckpointDelta(CkptWriter writer, in EmlActionCheckpointDelta delta)
    {
        // v3: the counterexample slot carries new-since-baseline updates (the set is append-only);
        // v2 files carried the full sorted set in the same slot — the reader keys off the schema byte.
        writer.U8(3); writer.U8((byte)delta.Selection);
        if (delta.Selection == EmlActionSelections.Off) return;
        writer.U64(delta.ActionRng); writer.I32(delta.ActionDecision); writer.I32(delta.RoundRobinCursor);
        writer.I32(delta.ActionEnumTaken); writer.I32(delta.ActionEnumRuler); writer.Bool(delta.ActionEnumDone);
        writer.I32(delta.ArmStates.Length); foreach (OutcomeArmState state in delta.ArmStates) { writer.F64(state.YieldEma); writer.I32(state.Outcomes); writer.I32(state.Fires); writer.I32(state.Decisive); }
        writer.I32(delta.PendingArmIndex); writer.U8((byte)delta.CurrentActionArm);
        WriteInts(writer, delta.ActionOffers); WriteLongs(writer, delta.ActionEvaluatorCalls); WriteInts(writer, delta.ActionFirstCaptures); WriteDoubles(writer, delta.ActionDeltaOutcomes);
        writer.I32(delta.StressCursor); writer.I32(delta.StressExactTests); writer.I32(delta.StressExactRefuted); writer.I32(delta.StressAsymptoticTests); writer.I32(delta.StressAsymptoticRefuted); writer.I32(delta.StressControlTests); writer.I32(delta.StressControlRefuted);
        writer.I32(delta.CounterexampleUpdates.Length); foreach (string value in delta.CounterexampleUpdates) writer.Str(value); writer.Bool(delta.PendingCounterexample is not null); if (delta.PendingCounterexample is not null) writer.Str(delta.PendingCounterexample);
        WriteInts(writer, delta.SelectionCauses); writer.I32(delta.ActionFallbacks); writer.F64(delta.ActionGlobalYield); writer.I32(delta.ActionGlobalOutcomes); writer.I64(delta.SharedCanonicalDeltas); writer.I64(delta.SharedFirstCaptures);
        writer.Bool(delta.Procedure.HasValue); if (delta.Procedure is CortexProcedureCheckpointState procedure) WriteProcedureCheckpointState(writer, in procedure);
        writer.I32(delta.ProcedureSolveDeltaCursor); writer.I32(delta.ProcedureSolveDeltas.Length); foreach (EmlCertificateDelta solveDelta in delta.ProcedureSolveDeltas) SaveCertificateDelta(writer, in solveDelta);
        writer.I32(delta.ProceduresStarted); writer.I32(delta.ProceduresCompleted); writer.I32(delta.ProcedureBindings); writer.I32(delta.ProcedureShuffledBindings); writer.I32(delta.ProcedureObligationMatches); writer.I32(delta.ProcedureNewDeltas); writer.I32(delta.ProcedureGuardsPassed); writer.I32(delta.ProcedureGuardsSkipped); writer.I32(delta.ProcedureGuardsAbstained); writer.I32(delta.ProcedureCanonicalDeltas); writer.I32(delta.HoleCursor); writer.F64(delta.IntrinsicFrontierResidual); writer.Bool(delta.ActionBatchHadCanonicalDelta); writer.I32(delta.DiscoveryEpoch);
        EmlActionObligationSearchDelta[] searchRows = delta.ObligationSearchUpdates;
        writer.I32(searchRows.Length); foreach (EmlActionObligationSearchDelta search in searchRows) { writer.I32(search.PredictionID.Value); writer.I32(search.Epoch); writer.I32(search.Attempts); }
        writer.I32(delta.ObligationSearchAttempts); writer.I32(delta.ObligationSearchSuppressions); writer.I32(delta.ObligationSearchRevivals); writer.I32(delta.ObligationSuppressedCalls); writer.I32(delta.ExecutionAdmissions); writer.I32(delta.ExecutionAffirmSkips); writer.I32(delta.HypothesisCapSkips); writer.I32(delta.FirstGenerativeDecision); writer.I32(delta.FirstGenerativeStep);
        writer.I32(delta.ObligationSearchRemovals.Length); foreach (EmlPredictionID claim in delta.ObligationSearchRemovals) writer.I32(claim.Value);
    }

    internal static EmlActionCheckpointDelta ReadActionCheckpointDelta(CkptReader reader)
    {
        byte schema = reader.U8();
        if (schema is not (1 or 2 or 3)) throw new InvalidDataException("unknown EML action checkpoint delta version");
        EmlActionSelections selection = (EmlActionSelections)reader.U8(); if (!Enum.IsDefined(selection)) throw new InvalidDataException("unknown EML action selection");
        if (selection == EmlActionSelections.Off) return default;
        ulong rng = reader.U64(); int decision = reader.I32(); int roundRobin = reader.I32(); int enumTaken = reader.I32(); int enumRuler = reader.I32(); bool enumDone = reader.Bool();
        int armCount = ReadBoundedCount(reader, 32, "action arm"); OutcomeArmState[] arms = new OutcomeArmState[armCount]; for (int i = 0; i < armCount; i++) arms[i] = new(reader.F64(), reader.I32(), reader.I32(), reader.I32());
        int pending = reader.I32(); EmlActionArms current = (EmlActionArms)reader.U8(); int[] offers = ReadInts(reader); long[] calls = ReadLongs(reader); int[] first = ReadInts(reader); double[] outcomes = ReadDoubles(reader);
        int stressCursor = reader.I32(), exactTests = reader.I32(), exactRefuted = reader.I32(), asymTests = reader.I32(), asymRefuted = reader.I32(), controlTests = reader.I32(), controlRefuted = reader.I32();
        int counterexampleCount = ReadBoundedCount(reader, 1_000_000, "counterexample"); string[] counterexamples = new string[counterexampleCount]; for (int i = 0; i < counterexamples.Length; i++) counterexamples[i] = reader.Str(); string? pendingCounterexample = reader.Bool() ? reader.Str() : null;
        string[] counterexampleUpdates = schema >= 3 ? counterexamples : null!;
        if (schema >= 3) counterexamples = Array.Empty<string>();
        int[] causes = ReadInts(reader); int fallback = reader.I32(); double globalYield = reader.F64(); int globalOutcomes = reader.I32(); long canonical = reader.I64(); long sharedFirst = reader.I64();
        CortexProcedureCheckpointState? procedure = reader.Bool() ? ReadProcedureCheckpointState(reader) : null;
        int solveCursor = reader.I32(); int solveCount = ReadBoundedCount(reader, 1_000_000, "procedure solve delta"); EmlCertificateDelta[] solveDeltas = new EmlCertificateDelta[solveCount]; for (int i = 0; i < solveCount; i++) solveDeltas[i] = LoadCertificateDelta(reader);
        int proceduresStarted = reader.I32(), proceduresCompleted = reader.I32(), bindings = reader.I32(), shuffled = reader.I32(), matches = reader.I32(), newDeltas = reader.I32(), guardsPassed = reader.I32(), guardsSkipped = reader.I32(), guardsAbstained = reader.I32(), procedureCanonical = reader.I32(), hole = reader.I32(); double residual = reader.F64(); bool batchCanonical = reader.Bool(); int epoch = reader.I32();
        int searchCount = ReadBoundedCount(reader, 1_000_000, "obligation search"); EmlActionObligationSearchDelta[] searches = new EmlActionObligationSearchDelta[searchCount]; for (int i = 0; i < searchCount; i++) searches[i] = new(new EmlPredictionID(reader.I32()), reader.I32(), reader.I32());
        EmlActionObligationSearchDelta[] updates = schema >= 2 ? searches : null!;
        if (schema >= 2) searches = Array.Empty<EmlActionObligationSearchDelta>();
        int searchAttempts = reader.I32(), searchSuppressions = reader.I32(), searchRevivals = reader.I32(), suppressedCalls = reader.I32(), admissions = reader.I32(), affirmSkips = reader.I32(), capSkips = reader.I32(), firstDecision = reader.I32(), firstStep = reader.I32();
        EmlPredictionID[] removals = schema >= 2 ? ReadPredictionIDs(reader, ReadBoundedCount(reader, 1_000_000, "obligation-search removal")) : null!;
        return new(selection, rng, decision, roundRobin, enumTaken, enumRuler, enumDone, arms, pending, current, offers, calls, first, outcomes, stressCursor, exactTests, exactRefuted, asymTests, asymRefuted, controlTests, controlRefuted, counterexamples, pendingCounterexample, causes, fallback, globalYield, globalOutcomes, canonical, sharedFirst, procedure, solveCursor, solveDeltas, proceduresStarted, proceduresCompleted, bindings, shuffled, matches, newDeltas, guardsPassed, guardsSkipped, guardsAbstained, procedureCanonical, hole, residual, batchCanonical, epoch, searches, searchAttempts, searchSuppressions, searchRevivals, suppressedCalls, admissions, affirmSkips, capSkips, firstDecision, firstStep, updates, removals, counterexampleUpdates);
    }

    private static EmlActionObligationSearchDelta[] ReadSearches(CkptReader reader, int count)
    {
        EmlActionObligationSearchDelta[] values = new EmlActionObligationSearchDelta[count];
        for (int i = 0; i < count; i++) values[i] = new(new EmlPredictionID(reader.I32()), reader.I32(), reader.I32());
        return values;
    }

    private static EmlPredictionID[] ReadPredictionIDs(CkptReader reader, int count)
    {
        EmlPredictionID[] values = new EmlPredictionID[count];
        for (int i = 0; i < count; i++) values[i] = new EmlPredictionID(reader.I32());
        return values;
    }

    private Dictionary<EmlPredictionID, EmlObligationSearchState>? _checkpointObligationSearch;

    private void CaptureActionCheckpointBaseline()
    {
        _checkpointObligationSearch = new(_obligationSearch);
        _checkpointCounterexampleCount = _counterexampleOrder.Count;
    }

    private static int ReadBoundedCount(CkptReader reader, int maximum, string name) { int count = reader.I32(); if (count < 0 || count > maximum) throw new InvalidDataException($"EML {name} count {count} exceeds bound"); return count; }
    private static void WriteInts(CkptWriter writer, int[] values) { writer.I32(values.Length); foreach (int value in values) writer.I32(value); }
    private static int[] ReadInts(CkptReader reader) { int count = ReadBoundedCount(reader, 1_000_000, "integer replacement"); int[] values = new int[count]; for (int i = 0; i < count; i++) values[i] = reader.I32(); return values; }
    private static void WriteLongs(CkptWriter writer, long[] values) { writer.I32(values.Length); foreach (long value in values) writer.I64(value); }
    private static long[] ReadLongs(CkptReader reader) { int count = ReadBoundedCount(reader, 1_000_000, "long replacement"); long[] values = new long[count]; for (int i = 0; i < count; i++) values[i] = reader.I64(); return values; }
    private static void WriteDoubles(CkptWriter writer, double[] values) { writer.I32(values.Length); foreach (double value in values) writer.F64(value); }
    private static double[] ReadDoubles(CkptReader reader) { int count = ReadBoundedCount(reader, 1_000_000, "double replacement"); double[] values = new double[count]; for (int i = 0; i < count; i++) values[i] = reader.F64(); return values; }

    private static void WriteProcedureCheckpointState(CkptWriter writer, in CortexProcedureCheckpointState state)
    {
        writer.I32(state.Steps.Length);
        foreach (CortexProcedureStep step in state.Steps)
        {
            writer.Str(step.Tool); writer.Bool(step.Guard.HasValue);
            if (step.Guard is CortexProcedureGuard guard)
            {
                writer.Str(guard.Channel); writer.U8((byte)guard.Source); writer.U8((byte)guard.Comparison); writer.Str(guard.Operand); writer.Bool(guard.ConsumeInput);
            }
            writer.U8((byte)step.OnGuardFalse); writer.I32(step.Arguments.Length);
            foreach (CortexProcedureArgument argument in step.Arguments)
            { writer.Str(argument.Slot); writer.Str(argument.Channel); writer.U8((byte)argument.Source); writer.Bool(argument.ConsumeInput); }
        }
        writer.I32(state.Next); writer.I32(state.Revision); writer.I32(state.Inputs.Length);
        foreach (CortexProcedureInputQueueState input in state.Inputs)
        { writer.Str(input.Channel); writer.U8((byte)input.Source); writer.I32(input.Values.Length); foreach (string value in input.Values) writer.Str(value); }
        writer.I32(state.CarriedGuards.Length);
        foreach (CortexActionArgument argument in state.CarriedGuards)
        { writer.Str(argument.Slot); writer.Str(argument.Value); writer.U8((byte)argument.Source); }
    }

    private static CortexProcedureCheckpointState ReadProcedureCheckpointState(CkptReader reader)
    {
        int stepCount = ReadBoundedCount(reader, 1 << 20, "procedure step"); CortexProcedureStep[] steps = new CortexProcedureStep[stepCount];
        for (int i = 0; i < stepCount; i++)
        {
            string tool = reader.Str(); CortexProcedureGuard? guard = null;
            if (reader.Bool()) guard = new CortexProcedureGuard(reader.Str(), ReadProcedureSource(reader), ReadProcedureComparison(reader), reader.Str(), reader.Bool());
            CortexProcedureFailureModes failure = ReadProcedureFailure(reader); int argumentCount = ReadBoundedCount(reader, 1 << 16, "procedure argument"); CortexProcedureArgument[] arguments = new CortexProcedureArgument[argumentCount];
            for (int j = 0; j < argumentCount; j++) arguments[j] = new CortexProcedureArgument(reader.Str(), reader.Str(), ReadProcedureSource(reader), reader.Bool());
            steps[i] = new CortexProcedureStep(tool, arguments, guard, failure);
        }
        int next = reader.I32(), revision = reader.I32(); int inputCount = ReadBoundedCount(reader, 1 << 20, "procedure input"); CortexProcedureInputQueueState[] inputs = new CortexProcedureInputQueueState[inputCount];
        for (int i = 0; i < inputCount; i++) { string channel = reader.Str(); Blur.SlotSources source = ReadProcedureSource(reader); int valueCount = ReadBoundedCount(reader, 1 << 20, "procedure input value"); string[] values = new string[valueCount]; for (int j = 0; j < valueCount; j++) values[j] = reader.Str(); inputs[i] = new(channel, source, values); }
        int carriedCount = ReadBoundedCount(reader, 1 << 16, "carried guard"); CortexActionArgument[] carried = new CortexActionArgument[carriedCount];
        for (int i = 0; i < carriedCount; i++) carried[i] = new CortexActionArgument(reader.Str(), reader.Str(), ReadProcedureSource(reader));
        return new(steps, next, revision, inputs, carried);
    }

    private static Blur.SlotSources ReadProcedureSource(CkptReader reader) { Blur.SlotSources source = (Blur.SlotSources)reader.U8(); if (!Enum.IsDefined(source)) throw new InvalidDataException("unknown procedure source"); return source; }
    private static CortexProcedureComparisons ReadProcedureComparison(CkptReader reader) { CortexProcedureComparisons comparison = (CortexProcedureComparisons)reader.U8(); if (!Enum.IsDefined(comparison)) throw new InvalidDataException("unknown procedure comparison"); return comparison; }
    private static CortexProcedureFailureModes ReadProcedureFailure(CkptReader reader) { CortexProcedureFailureModes failure = (CortexProcedureFailureModes)reader.U8(); if (!Enum.IsDefined(failure)) throw new InvalidDataException("unknown procedure failure mode"); return failure; }
}
