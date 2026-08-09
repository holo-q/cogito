namespace Cogito;

using Cogito.Exec;
using Cogito.Grammar;
using Cogito.Induct;

public sealed record CortexWeftCurriculum : CortexCurriculumConfig
{
    internal override string Token => $"weft:{ExecutionFuel}:{TowerBlockBudget}:{CandidateLength}";
    public int ExecutionFuel { get; init; } = 128;
    public int TowerBlockBudget { get; init; } = 96;
    public int CandidateLength { get; init; } = 12;

    public WeftCurriculum Mount(ulong seed) => new(this, seed);

    internal static bool TryParseToken(string token, out CortexWeftCurriculum curriculum)
    {
        curriculum = new CortexWeftCurriculum();
        string[] fields = token.Split(':');
        if (fields.Length != 4 || fields[0] != "weft") return false;
        if (!int.TryParse(fields[1], out int fuel) || fuel <= 0) return false;
        if (!int.TryParse(fields[2], out int towerBytes) || towerBytes <= 0) return false;
        if (!int.TryParse(fields[3], out int candidateLength) || candidateLength <= 0) return false;
        curriculum = new CortexWeftCurriculum
        {
            ExecutionFuel = fuel,
            TowerBlockBudget = towerBytes,
            CandidateLength = candidateLength,
        };
        return true;
    }
}

public enum WeftDiscoveryActions
{
    Sample,
    Mutate,
    Stress,
    Compare,
}

public readonly struct WeftDiscoveryState
{
    public WeftDiscoveryState(
        int actionCursor,
        int executions,
        int behaviorClasses,
        int behaviorMembers,
        int newBehaviors,
        int admittedKnots,
        int activeKnots,
        int pendingKnots,
        int rejectedKnots,
        int shuffledAccepted,
        long mdlSavingsMbits,
        int samples,
        int mutations,
        int stressChecks,
        int comparisons,
        int executionFuel,
        int candidateLength)
    {
        ActionCursor = actionCursor;
        Executions = executions;
        BehaviorClasses = behaviorClasses;
        BehaviorMembers = behaviorMembers;
        NewBehaviors = newBehaviors;
        AdmittedKnots = admittedKnots;
        ActiveKnots = activeKnots;
        PendingKnots = pendingKnots;
        RejectedKnots = rejectedKnots;
        ShuffledAccepted = shuffledAccepted;
        MdlSavingsMbits = mdlSavingsMbits;
        Samples = samples;
        Mutations = mutations;
        StressChecks = stressChecks;
        Comparisons = comparisons;
        ExecutionFuel = executionFuel;
        CandidateLength = candidateLength;
    }

    public int ActionCursor { get; }
    public int Executions { get; }
    public int BehaviorClasses { get; }
    public int BehaviorMembers { get; }
    public int NewBehaviors { get; }
    public int AdmittedKnots { get; }
    public int ActiveKnots { get; }
    public int PendingKnots { get; }
    public int RejectedKnots { get; }
    public int ShuffledAccepted { get; }
    public long MdlSavingsMbits { get; }
    public int Samples { get; }
    public int Mutations { get; }
    public int StressChecks { get; }
    public int Comparisons { get; }
    public int ExecutionFuel { get; }
    public int CandidateLength { get; }
}

public readonly struct WeftDiscoveryDelta
{
    public WeftDiscoveryDelta(in WeftDiscoveryState before, in WeftDiscoveryState after)
    {
        Actions = after.ActionCursor - before.ActionCursor;
        Executions = after.Executions - before.Executions;
        BehaviorClasses = after.BehaviorClasses - before.BehaviorClasses;
        BehaviorMembers = after.BehaviorMembers - before.BehaviorMembers;
        NewBehaviors = after.NewBehaviors - before.NewBehaviors;
        AdmittedKnots = after.AdmittedKnots - before.AdmittedKnots;
        ActiveKnots = after.ActiveKnots - before.ActiveKnots;
        PendingKnots = after.PendingKnots - before.PendingKnots;
        RejectedKnots = after.RejectedKnots - before.RejectedKnots;
        ShuffledAccepted = after.ShuffledAccepted - before.ShuffledAccepted;
        MdlSavingsMbits = after.MdlSavingsMbits - before.MdlSavingsMbits;
        Samples = after.Samples - before.Samples;
        Mutations = after.Mutations - before.Mutations;
        StressChecks = after.StressChecks - before.StressChecks;
        Comparisons = after.Comparisons - before.Comparisons;
    }

    public int Actions { get; }
    public int Executions { get; }
    public int BehaviorClasses { get; }
    public int BehaviorMembers { get; }
    public int NewBehaviors { get; }
    public int AdmittedKnots { get; }
    public int ActiveKnots { get; }
    public int PendingKnots { get; }
    public int RejectedKnots { get; }
    public int ShuffledAccepted { get; }
    public long MdlSavingsMbits { get; }
    public int Samples { get; }
    public int Mutations { get; }
    public int StressChecks { get; }
    public int Comparisons { get; }
}

public readonly struct WeftDiscoveryActionOutcome
{
    public WeftDiscoveryActionOutcome(
        WeftDiscoveryActions action,
        int candidateSymbols,
        int candidateFuel,
        int traceBytes,
        int dataDepth,
        int fuelSpent,
        bool halted,
        bool openedBehavior,
        in WeftDiscoveryState before,
        in WeftDiscoveryState after)
    {
        Action = action;
        CandidateSymbols = candidateSymbols;
        CandidateFuel = candidateFuel;
        TraceBytes = traceBytes;
        DataDepth = dataDepth;
        FuelSpent = fuelSpent;
        Halted = halted;
        OpenedBehavior = openedBehavior;
        Before = before;
        After = after;
        Delta = new WeftDiscoveryDelta(in before, in after);
    }

    public WeftDiscoveryActions Action { get; }
    public int CandidateSymbols { get; }
    public int CandidateFuel { get; }
    public int TraceBytes { get; }
    public int DataDepth { get; }
    public int FuelSpent { get; }
    public bool Halted { get; }
    public bool OpenedBehavior { get; }
    public WeftDiscoveryState Before { get; }
    public WeftDiscoveryState After { get; }
    public WeftDiscoveryDelta Delta { get; }
}

/// The Weft discovery world. Domain semantics remain here: candidate production, VM grading, behavior classes,
/// tower-to-knot induction, and the frontier readout all cross Cortex only through ICurriculum.
public sealed class WeftCurriculum : ICurriculum, IDisposable
{
    private const int STATE_VERSION = 1;
    private static readonly byte[][] ClosedReplacements =
    [
        [(byte)Opcodes.Zero],
        [(byte)Opcodes.One],
        [(byte)Opcodes.Zero, (byte)Opcodes.One, (byte)Opcodes.Add],
        [(byte)Opcodes.One, (byte)Opcodes.Dup, (byte)Opcodes.Add],
    ];

    private readonly CortexWeftCurriculum _config;
    private readonly ulong _seed;
    private readonly ulong _configFingerprint;
    private readonly WeftDiscovery _discovery = new();
    private readonly WeftBehaviorStore _behaviors = new();
    private readonly List<WeftProgram> _activeKnots = new();
    private readonly List<WeftProgram> _pendingKnots = new();
    private int _cursor;
    private int _ingested;
    private int _newBehaviors;
    private int _stressChecks;
    private int _mutations;
    private int _samples;
    private int _comparisons;
    private int _shuffledAccepted;
    private long _mdlSavings;

    public WeftCurriculum(CortexWeftCurriculum config, ulong seed)
    {
        if (config.ExecutionFuel <= 0) throw new ArgumentOutOfRangeException(nameof(config.ExecutionFuel));
        if (config.TowerBlockBudget <= 0) throw new ArgumentOutOfRangeException(nameof(config.TowerBlockBudget));
        if (config.CandidateLength <= 0) throw new ArgumentOutOfRangeException(nameof(config.CandidateLength));
        _config = config;
        _seed = seed;
        _configFingerprint = ComputeConfigFingerprint(config, seed);
        MixEvery = config.MixEvery;
    }

    public bool Drained => false;
    public bool Exhausted => false;
    public int IngestedCount => _ingested;
    public int WorkloadCount => Math.Max(1, _behaviors.ClassCount);
    public int MixAffirmSkips => 0;
    public int IngestDiversity => 1;
    public double LastPickCoverage => double.NaN;
    public int MixEvery { get; set; }
    public int StreakResets => 0;
    public WeftDiscovery Discovery => _discovery;
    public WeftBehaviorStore Behaviors => _behaviors;

    public WeftDiscoveryState CaptureDiscoveryState() => new(
        actionCursor: _cursor,
        executions: _ingested,
        behaviorClasses: _behaviors.ClassCount,
        behaviorMembers: _behaviors.MemberCount,
        newBehaviors: _newBehaviors,
        admittedKnots: _discovery.Knots.Count,
        activeKnots: _activeKnots.Count,
        pendingKnots: _pendingKnots.Count,
        rejectedKnots: _discovery.Rejected,
        shuffledAccepted: _shuffledAccepted,
        mdlSavingsMbits: _mdlSavings,
        samples: _samples,
        mutations: _mutations,
        stressChecks: _stressChecks,
        comparisons: _comparisons,
        executionFuel: _config.ExecutionFuel,
        candidateLength: _config.CandidateLength);

    public byte[] CaptureCheckpointState()
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) SaveState(writer);
        return stream.ToArray();
    }

    public WeftCurriculum CreateCheckpointFork(ReadOnlySpan<byte> checkpointState)
    {
        WeftCurriculum fork = new(_config, _seed);
        using MemoryStream stream = new(checkpointState.ToArray(), writable: false);
        using CkptReader reader = new(stream);
        fork.LoadState(reader);
        return fork;
    }

    public void DefineWorkspace(CogitoWorkspace workspace)
    {
        workspace.Define(
            "weft.executions",
            "weft.behavior.classes",
            "weft.behavior.members",
            "weft.knots.admitted",
            "weft.knots.pending",
            "weft.knots.rejected",
            "weft.knots.shuffled_accepted",
            "weft.knots.mdl_savings_mbits",
            "weft.actions.sample",
            "weft.actions.mutate",
            "weft.actions.stress",
            "weft.actions.compare");
    }

    public void PostWorkspace(CogitoWorkspace workspace)
    {
        workspace.Post("weft.executions", _ingested);
        workspace.Post("weft.behavior.classes", _behaviors.ClassCount);
        workspace.Post("weft.behavior.members", _behaviors.MemberCount);
        workspace.Post("weft.knots.admitted", _discovery.Knots.Count);
        workspace.Post("weft.knots.pending", _pendingKnots.Count);
        workspace.Post("weft.knots.rejected", _discovery.Rejected);
        workspace.Post("weft.knots.shuffled_accepted", _shuffledAccepted);
        workspace.Post("weft.knots.mdl_savings_mbits", _mdlSavings);
        workspace.Post("weft.actions.sample", _samples);
        workspace.Post("weft.actions.mutate", _mutations);
        workspace.Post("weft.actions.stress", _stressChecks);
        workspace.Post("weft.actions.compare", _comparisons);
    }

    public void Seed(Tape tape, Journal journal)
    {
        WeftProgram first = WeftDiet.GetByName("loop-accumulate", "weft:seed:zero", _config.TowerBlockBudget);
        WeftProgram second = WeftDiet.GetByName("loop-accumulate", "weft:seed:one", _config.TowerBlockBudget);
        ExecuteProgram(first, [], tape, journal, step: 0, "weft:seed:zero");
        ExecuteProgram(second, [WeftNumber.One], tape, journal, step: 0, "weft:seed:one");
        _behaviors.Admit(first, out WeftBehaviorClass _);
    }

    public void AppendProbeSamples(List<byte[]> samples)
    {
        string[] names = ["loop-accumulate", "loop-even", "cross-fold"];
        for (int i = 0; i < names.Length; i++)
        {
            WeftProgram program = WeftDiet.GetByName(names[i], $"weft:probe:{i}", _config.TowerBlockBudget);
            ExecResult result = new TapeVm(program.Rules).Run(
                program.Start,
                Math.Min(program.Fuel, Math.Max(1, _config.ExecutionFuel)),
                [WeftNumber.FromInt64(i)]);
            if (result.Trace.Length > 0) samples.Add(result.Trace);
        }
    }

    public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
    {
        ActivatePendingKnots();
        int before = _behaviors.ClassCount;
        int ingestedBefore = _ingested;
        int count = Math.Max(1, batch);
        for (int slot = 0; slot < count; slot++)
        {
            WeftDiscoveryActions action = (WeftDiscoveryActions)(_cursor % 4);
            ExecuteDiscoveryAction(action, in grammar, tape, journal, step, slot);
        }
        InduceDiscoveryKnots();
        return new IntakeStep(_ingested - ingestedBefore, Advanced: _behaviors.ClassCount > before, Domain: _behaviors.ClassCount);
    }

    public WeftDiscoveryActionOutcome ExecuteDiscoveryAction(
        WeftDiscoveryActions action,
        in RePairResult grammar,
        Tape tape,
        Journal journal,
        int step,
        int slot)
    {
        ActivatePendingKnots();
        WeftDiscoveryState before = CaptureDiscoveryState();
        _cursor++;
        WeftProgram candidate = action switch
        {
            WeftDiscoveryActions.Sample => Sample(in grammar, step, slot),
            WeftDiscoveryActions.Mutate => Mutate(step, slot),
            WeftDiscoveryActions.Stress => Stress(step, slot),
            WeftDiscoveryActions.Compare => Compare(step, slot),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        bool opened = _behaviors.Admit(candidate, out WeftBehaviorClass behaviorClass);
        if (opened) _newBehaviors++;
        WeftNumber[] input = SelectInput(step, slot);
        string source = $"weft:{action.ToString().ToLowerInvariant()}:input-{(step + slot) & 7}";
        WeftExecutionMeasurement execution = ExecuteProgram(candidate, input, tape, journal, step, source);
        if (action == WeftDiscoveryActions.Stress && !_behaviors.Contains(behaviorClass.Certificate))
            throw new InvalidOperationException("Weft stress check lost an admitted behavior certificate");
        WeftDiscoveryState after = CaptureDiscoveryState();
        return new WeftDiscoveryActionOutcome(
            action,
            MeasureProgramSymbols(in candidate),
            candidate.Fuel,
            execution.TraceBytes,
            execution.DataDepth,
            execution.FuelSpent,
            execution.Halted,
            opened,
            in before,
            in after);
    }

    public WeftDiscoveryDelta InduceDiscoveryKnots()
    {
        WeftDiscoveryState before = CaptureDiscoveryState();
        List<WeftKnotReceipt> receipts = _discovery.InduceKnots();
        foreach (WeftKnotReceipt receipt in receipts)
        {
            _shuffledAccepted += receipt.ShuffledAccepted ? 1 : 0;
            _mdlSavings = checked(_mdlSavings + receipt.SavingsMbits);
            if (!_discovery.TryGetKnot(receipt.ID, out WeftKnot knot)) throw new InvalidOperationException($"admitted knot {receipt.ID} missing from catalog");
            _pendingKnots.Add(_discovery.CreateProgram(knot.ID, knot.DeepestVerifiedIteration + 1));
        }
        WeftDiscoveryState after = CaptureDiscoveryState();
        return new WeftDiscoveryDelta(in before, in after);
    }

    public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
    {
        if (_behaviors.ClassCount == 0) return;
        WeftProgram representative = _behaviors.GetRepresentative(step % _behaviors.ClassCount);
        ExecuteProgram(representative, SelectInput(step, 0), tape, journal, step, "weft:mix");
    }

    public void MixOne(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut)
        => Mix(cortex, grammar, tape, journal, step, affirmCut);

    public void SaveState(CkptWriter writer)
    {
        writer.I32(STATE_VERSION);
        writer.U64(_configFingerprint);
        writer.I32(_cursor);
        writer.I32(_ingested);
        writer.I32(_newBehaviors);
        writer.I32(_stressChecks);
        writer.I32(_mutations);
        writer.I32(_samples);
        writer.I32(_comparisons);
        writer.I32(_shuffledAccepted);
        writer.I64(_mdlSavings);
        SavePrograms(writer, _activeKnots);
        SavePrograms(writer, _pendingKnots);
        _discovery.Save(writer);
        _behaviors.Save(writer);
    }

    public void LoadState(CkptReader reader)
    {
        int version = reader.I32();
        if (version != STATE_VERSION) throw new InvalidDataException($"unsupported Weft curriculum state {version}");
        ulong fingerprint = reader.U64();
        if (fingerprint != _configFingerprint) throw new InvalidDataException($"Weft curriculum config mismatch: stored {fingerprint:x16}, mounted {_configFingerprint:x16}");
        _cursor = reader.I32();
        _ingested = reader.I32();
        _newBehaviors = reader.I32();
        _stressChecks = reader.I32();
        _mutations = reader.I32();
        _samples = reader.I32();
        _comparisons = reader.I32();
        _shuffledAccepted = reader.I32();
        _mdlSavings = reader.I64();
        LoadPrograms(reader, _activeKnots);
        LoadPrograms(reader, _pendingKnots);
        _discovery.Load(reader);
        _behaviors.Load(reader);
    }

    public void Dispose() => _discovery.Dispose();

    private WeftProgram Sample(in RePairResult grammar, int step, int slot)
    {
        _samples++;
        if (grammar.Rules.Length > 0)
        {
            int index = (int)((_seed + (ulong)step * 17UL + (ulong)slot * 31UL) % (ulong)grammar.Rules.Length);
            byte[] expansion = Reconstruct.Expand(grammar.Rules, [new Symbol(grammar.AlphabetSize + (uint)index)]);
            if (TryReadOperations(expansion, _config.CandidateLength, out byte[] operations))
                return WeftDiet.CreateFinite($"sample-{step}-{slot}", "grammar-biased executable sample", Bios.Render(operations), operations, _config.ExecutionFuel);
        }
        return WeftDiet.Pick(slot, step, _config.TowerBlockBudget, $"weft:sample:{slot}");
    }

    private WeftProgram Mutate(int step, int slot)
    {
        _mutations++;
        WeftProgram seed = SelectRepresentative(step + slot);
        ExecResult execution = new TapeVm(seed.Rules).Run(seed.Start, Math.Min(seed.Fuel, Math.Max(1, _config.ExecutionFuel)));
        List<ClosedSpan> spans = new();
        FindClosedSpans(execution.Trace, spans);
        if (spans.Count == 0) return seed;
        int target = (int)((_seed + (ulong)step * 29UL + (ulong)slot * 43UL) % (ulong)spans.Count);
        ClosedSpan span = spans[target];
        byte[] replacement = ClosedReplacements[(step + slot + (int)(_seed % (ulong)ClosedReplacements.Length)) % ClosedReplacements.Length];
        byte[] operations = new byte[execution.Trace.Length - span.Length + replacement.Length];
        execution.Trace.AsSpan(0, span.Offset).CopyTo(operations);
        replacement.CopyTo(operations.AsSpan(span.Offset));
        execution.Trace.AsSpan(span.Offset + span.Length).CopyTo(operations.AsSpan(span.Offset + replacement.Length));
        return WeftDiet.CreateFinite($"mutate-{step}-{slot}", "closed stack-producing span resample", Bios.Render(operations), operations, _config.ExecutionFuel);
    }

    private WeftProgram Stress(int step, int slot)
    {
        _stressChecks++;
        return SelectRepresentative(step + slot);
    }

    private WeftProgram Compare(int step, int slot)
    {
        _comparisons++;
        if (_activeKnots.Count > 0) return _activeKnots[(step + slot) % _activeKnots.Count];
        return SelectRepresentative(step + slot);
    }

    private WeftProgram SelectRepresentative(int index)
    {
        if (_behaviors.ClassCount > 0) return _behaviors.GetRepresentative(Math.Abs(index) % _behaviors.ClassCount);
        return WeftDiet.GetByName("loop-triad", "weft:fallback", _config.TowerBlockBudget);
    }

    private WeftExecutionMeasurement ExecuteProgram(in WeftProgram program, ReadOnlySpan<WeftNumber> initialData, Tape tape, Journal journal, int step, string source)
    {
        int fuel = Math.Min(program.Fuel, Math.Max(1, _config.ExecutionFuel));
        ExecResult result = new TapeVm(program.Rules).Run(program.Start, fuel, initialData);
        if (result.Trace.Length == 0)
            return new WeftExecutionMeasurement(0, result.Data.Length, result.FuelSpent(fuel), result.Halted);
        TapePacketCreator.AppendWeftExecution(tape, journal, step, source, in program, in result);
        _discovery.ObserveExecution(source, result.Trace);
        _ingested++;
        return new WeftExecutionMeasurement(result.Trace.Length, result.Data.Length, result.FuelSpent(fuel), result.Halted);
    }

    private void ActivatePendingKnots()
    {
        if (_pendingKnots.Count == 0) return;
        _activeKnots.AddRange(_pendingKnots);
        _pendingKnots.Clear();
    }

    private static WeftNumber[] SelectInput(int step, int slot) => ((step + slot) & 7) switch
    {
        0 => [],
        1 => [WeftNumber.Zero],
        2 => [WeftNumber.One],
        3 => [WeftNumber.FromInt64(-1)],
        4 => [WeftNumber.Zero, WeftNumber.One],
        5 => [WeftNumber.One, WeftNumber.Zero],
        6 => [WeftNumber.FromInt64(2), WeftNumber.FromInt64(3)],
        _ => [WeftNumber.FromInt64(-2), WeftNumber.FromInt64(3)],
    };

    private static bool TryReadOperations(ReadOnlySpan<byte> expansion, int maxLength, out byte[] operations)
    {
        int length = Math.Min(expansion.Length, Math.Max(1, maxLength));
        if (length == 0) { operations = []; return false; }
        operations = new byte[length];
        for (int i = 0; i < length; i++)
        {
            if (!Bios.TryOpcode(expansion[i], out Opcodes opcode) || opcode == Opcodes.Cond)
            {
                operations = [];
                return false;
            }
            operations[i] = expansion[i];
        }
        return true;
    }

    private static void FindClosedSpans(ReadOnlySpan<byte> operations, List<ClosedSpan> spans)
    {
        spans.Clear();
        for (int offset = 0; offset < operations.Length; offset++)
        {
            int delta = 0;
            int minimum = 0;
            for (int end = offset; end < operations.Length; end++)
            {
                if (!Bios.TryOpcode(operations[end], out Opcodes opcode) || opcode == Opcodes.Cond) break;
                StackEffect effect = Bios.Effect(opcode);
                minimum = Math.Max(minimum, effect.MinReq - delta);
                delta += effect.DeltaH;
                if (minimum == 0 && delta == 1) spans.Add(new ClosedSpan(offset, end - offset + 1));
            }
        }
    }

    private static void SavePrograms(CkptWriter writer, List<WeftProgram> programs)
    {
        writer.I32(programs.Count);
        foreach (WeftProgram program in programs) WeftProgramCodec.Save(writer, program);
    }

    private static void LoadPrograms(CkptReader reader, List<WeftProgram> programs)
    {
        programs.Clear();
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"invalid Weft program count {count}");
        for (int i = 0; i < count; i++) programs.Add(WeftProgramCodec.Load(reader));
    }

    private static ulong ComputeConfigFingerprint(CortexWeftCurriculum config, ulong seed)
    {
        ulong hash = 14695981039346656037UL;
        Add(ref hash, seed);
        Add(ref hash, (ulong)config.ExecutionFuel);
        Add(ref hash, (ulong)config.TowerBlockBudget);
        Add(ref hash, (ulong)config.CandidateLength);
        Add(ref hash, (ulong)config.IntakeBatch);
        Add(ref hash, (ulong)config.SeedSpans);
        Add(ref hash, (ulong)config.MixEvery);
        return hash;
    }

    private static void Add(ref ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= 1099511628211UL;
        }
    }

    private static int MeasureProgramSymbols(in WeftProgram program)
    {
        int symbols = program.Start.Length;
        for (int i = 0; i < program.Rules.Length; i++) symbols = checked(symbols + program.Rules[i].Pattern.Length);
        return symbols;
    }

    private readonly record struct ClosedSpan(int Offset, int Length);
    private readonly record struct WeftExecutionMeasurement(int TraceBytes, int DataDepth, int FuelSpent, bool Halted);
}
