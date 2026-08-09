namespace Cogito;

public enum CortexProcedureComparisons
{
    Present,
    Equal,
    NotEqual,
}

public enum CortexProcedureFailureModes
{
    Skip,
    Abstain,
}

public enum CortexProcedureTransitions
{
    Blocked,
    Execute,
    Skip,
    Abstain,
    Complete,
}

public readonly record struct CortexProcedureProposal
{
    public CortexProcedureTransitions Transition { get; }
    public string Tool { get; }

    internal CortexProcedure? Procedure { get; }
    internal int Revision { get; }
    internal int Step { get; }

    internal CortexProcedureProposal(CortexProcedure procedure, int revision, int step,
        CortexProcedureTransitions transition, string tool)
    {
        Procedure = procedure;
        Revision = revision;
        Step = step;
        Transition = transition;
        Tool = tool;
    }
}

public readonly record struct CortexProcedureInput(string Channel, Blur.SlotSources Source, string Value);

internal readonly record struct CortexProcedureInputQueueState(
    string Channel,
    Blur.SlotSources Source,
    string[] Values);

public readonly record struct CortexProcedureArgument(
    string Slot,
    string Channel,
    Blur.SlotSources Source,
    bool ConsumeInput = true);

public readonly record struct CortexProcedureGuard(
    string Channel,
    Blur.SlotSources Source,
    CortexProcedureComparisons Comparison,
    string Operand,
    bool ConsumeInput = true);

public readonly record struct CortexProcedureStep
{
    public string Tool { get; }
    public CortexProcedureArgument[] Arguments { get; }
    public CortexProcedureGuard? Guard { get; }
    public CortexProcedureFailureModes OnGuardFalse { get; }

    public CortexProcedureStep(string tool, CortexProcedureArgument[] arguments,
        CortexProcedureGuard? guard = null,
        CortexProcedureFailureModes onGuardFalse = CortexProcedureFailureModes.Skip)
    {
        Tool = tool;
        Arguments = arguments;
        Guard = guard;
        OnGuardFalse = onGuardFalse;
    }

    public CortexProcedureStep(string tool, string argumentSlot, string inputChannel,
        Blur.SlotSources source, bool ConsumeInput = true,
        CortexProcedureGuard? guard = null,
        CortexProcedureFailureModes onGuardFalse = CortexProcedureFailureModes.Skip)
        : this(tool, [new CortexProcedureArgument(argumentSlot, inputChannel, source, ConsumeInput)],
            guard, onGuardFalse) { }
}

internal readonly record struct CortexProcedureCheckpointState(
    CortexProcedureStep[] Steps,
    int Next,
    int Revision,
    CortexProcedureInputQueueState[] Inputs,
    CortexActionArgument[] CarriedGuards);

/// An executable action skeleton whose content is supplied by provenance-typed runtime channels.
/// Reading stages one transition; only advancing it may consume inputs or move control flow.
public sealed class CortexProcedure
{
    private const int MaxSteps = 1 << 20;
    private const int MaxArguments = 1 << 16;
    private const int MaxInputs = 1 << 20;

    public static CortexProcedure Disabled => new(Array.Empty<CortexProcedureStep>());

    private readonly CortexProcedureStep[] _steps;
    private readonly Dictionary<(string Channel, Blur.SlotSources Source), Queue<string>> _inputs = new();
    private readonly List<CortexActionArgument> _carriedGuards = new();
    private int _next;
    private int _revision;
    private bool _readPending;
    private CortexProcedureProposal _readProposal;

    public CortexProcedure(CortexProcedureStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ValidateSteps(steps);
        _steps = steps;
    }

    public bool Enabled => _steps.Length > 0;

    public bool Complete => _next >= _steps.Length;

    internal CortexProcedureCheckpointState CaptureCheckpointState()
    {
        if (_readPending)
            throw new InvalidOperationException("cannot checkpoint a staged procedure transition");
        CortexProcedureStep[] steps = new CortexProcedureStep[_steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            CortexProcedureStep step = _steps[i];
            steps[i] = new CortexProcedureStep(step.Tool, (CortexProcedureArgument[])step.Arguments.Clone(), step.Guard, step.OnGuardFalse);
        }
        CortexProcedureInputQueueState[] inputs = _inputs
            .OrderBy(static pair => pair.Key.Channel, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Source)
            .Select(static pair => new CortexProcedureInputQueueState(
                pair.Key.Channel, pair.Key.Source, pair.Value.ToArray()))
            .ToArray();
        return new(steps, _next, _revision, inputs, _carriedGuards.ToArray());
    }

    internal static CortexProcedure RestoreCheckpointState(in CortexProcedureCheckpointState state)
    {
        if (state.Steps is null || state.Inputs is null || state.CarriedGuards is null)
            throw new InvalidDataException("procedure checkpoint state is incomplete");
        CortexProcedure procedure = new(state.Steps);
        if (state.Next < 0 || state.Next > procedure._steps.Length || state.Revision < 0)
            throw new InvalidDataException("procedure checkpoint cursor is outside its program");
        procedure._next = state.Next;
        procedure._revision = state.Revision;
        if (state.Inputs.Length > MaxInputs || state.CarriedGuards.Length > MaxArguments)
            throw new InvalidDataException("procedure checkpoint exceeds bounded state");
        for (int i = 0; i < state.Inputs.Length; i++)
        {
            CortexProcedureInputQueueState input = state.Inputs[i];
            ValidateChannel(input.Channel);
            ValidateSource(input.Source);
            if (input.Values is null || input.Values.Length > MaxInputs)
                throw new InvalidDataException("procedure input queue exceeds bounded state");
            Queue<string> values = new(input.Values);
            if (!procedure._inputs.TryAdd((input.Channel, input.Source), values))
                throw new InvalidDataException($"duplicate procedure input {input.Channel}/{input.Source}");
        }
        procedure._carriedGuards.AddRange(state.CarriedGuards);
        return procedure;
    }

    public void AddInput(in CortexProcedureInput input)
    {
        ValidateChannel(input.Channel);
        ValidateSource(input.Source);
        (string Channel, Blur.SlotSources Source) key = (input.Channel, input.Source);
        if (!_inputs.TryGetValue(key, out Queue<string>? values))
        {
            values = new Queue<string>();
            _inputs.Add(key, values);
        }
        values.Enqueue(input.Value);
        _revision++;
    }

    public CortexProcedureProposal ProposeNext(List<CortexActionArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        arguments.Clear();
        if (Complete)
            return new CortexProcedureProposal(this, _revision, _next, CortexProcedureTransitions.Complete, "");

        CortexProcedureStep step = _steps[_next];
        CortexActionArgument guardArgument = default;
        bool hasGuardArgument = false;

        if (step.Guard is CortexProcedureGuard guard)
        {
            (string Channel, Blur.SlotSources Source) guardKey = (guard.Channel, guard.Source);
            if (!_inputs.TryGetValue(guardKey, out Queue<string>? guardValues) || guardValues.Count == 0)
            {
                arguments.Clear();
                return new CortexProcedureProposal(this, _revision, _next,
                    CortexProcedureTransitions.Blocked, step.Tool);
            }
            string guardValue = guardValues.Peek();
            guardArgument = new CortexActionArgument(GetGuardSlot(in guard), guardValue, guard.Source);
            hasGuardArgument = true;
            if (!MatchesGuard(in guard, guardValue))
            {
                for (int i = 0; i < _carriedGuards.Count; i++) arguments.Add(_carriedGuards[i]);
                arguments.Add(guardArgument);
                CortexProcedureTransitions transition = step.OnGuardFalse == CortexProcedureFailureModes.Skip
                    ? CortexProcedureTransitions.Skip
                    : CortexProcedureTransitions.Abstain;
                return new CortexProcedureProposal(this, _revision, _next, transition, step.Tool);
            }
        }

        for (int i = 0; i < step.Arguments.Length; i++)
        {
            CortexProcedureArgument argument = step.Arguments[i];
            (string Channel, Blur.SlotSources Source) key = (argument.Channel, argument.Source);
            if (!_inputs.TryGetValue(key, out Queue<string>? values) || values.Count == 0)
            {
                arguments.Clear();
                return new CortexProcedureProposal(this, _revision, _next,
                    CortexProcedureTransitions.Blocked, step.Tool);
            }
            arguments.Add(new CortexActionArgument(argument.Slot, values.Peek(), argument.Source));
        }
        for (int i = 0; i < _carriedGuards.Count; i++) arguments.Add(_carriedGuards[i]);
        if (hasGuardArgument) arguments.Add(guardArgument);

        return new CortexProcedureProposal(this, _revision, _next,
            CortexProcedureTransitions.Execute, step.Tool);
    }

    public void Commit(in CortexProcedureProposal proposal)
    {
        if (!ReferenceEquals(proposal.Procedure, this) || proposal.Revision != _revision || proposal.Step != _next)
            throw new InvalidOperationException("procedure proposal is stale");
        if (proposal.Transition is CortexProcedureTransitions.Blocked or CortexProcedureTransitions.Complete)
            throw new InvalidOperationException($"procedure transition {proposal.Transition} cannot commit");

        CortexProcedureStep step = _steps[_next];
        if (proposal.Transition == CortexProcedureTransitions.Skip && step.Guard is CortexProcedureGuard skippedGuard)
        {
            (string Channel, Blur.SlotSources Source) key = (skippedGuard.Channel, skippedGuard.Source);
            string value = _inputs[key].Peek();
            _carriedGuards.Add(new CortexActionArgument(GetGuardSlot(in skippedGuard), value, skippedGuard.Source));
        }

        if (proposal.Transition == CortexProcedureTransitions.Execute)
            ConsumeArguments(step.Arguments);
        if (step.Guard is CortexProcedureGuard guard && guard.ConsumeInput &&
            (proposal.Transition != CortexProcedureTransitions.Execute ||
             !ConsumesChannel(step.Arguments, guard.Channel, guard.Source)))
            _inputs[(guard.Channel, guard.Source)].Dequeue();

        if (proposal.Transition != CortexProcedureTransitions.Skip) _carriedGuards.Clear();
        _next = proposal.Transition == CortexProcedureTransitions.Abstain ? _steps.Length : _next + 1;
        _revision++;
        _readPending = false;
    }

    public CortexProcedureTransitions ReadNext(List<CortexActionArgument> arguments, out string tool)
    {
        _readPending = false;
        CortexProcedureProposal proposal = ProposeNext(arguments);
        tool = proposal.Tool;
        if (proposal.Transition is not (CortexProcedureTransitions.Blocked or CortexProcedureTransitions.Complete))
        {
            _readProposal = proposal;
            _readPending = true;
        }
        return proposal.Transition;
    }

    public void AdvanceNext(CortexProcedureTransitions transition)
    {
        if (!_readPending || transition != _readProposal.Transition)
            throw new InvalidOperationException($"procedure transition {transition} was not staged");
        if (transition is CortexProcedureTransitions.Blocked or CortexProcedureTransitions.Complete)
            throw new InvalidOperationException($"procedure transition {transition} cannot advance");
        Commit(in _readProposal);
    }

    public void Save(CkptWriter writer) => Save(writer, includeGuards: true);

    public void Save(CkptWriter writer, bool includeGuards)
    {
        if (_readPending) throw new InvalidOperationException("cannot checkpoint a staged procedure transition");
        writer.I32(_steps.Length);
        for (int i = 0; i < _steps.Length; i++)
        {
            CortexProcedureStep step = _steps[i];
            writer.Str(step.Tool);
            if (includeGuards)
            {
                writer.Bool(step.Guard.HasValue);
                if (step.Guard is CortexProcedureGuard guard)
                {
                    writer.Str(guard.Channel);
                    writer.U8((byte)guard.Source);
                    writer.U8((byte)guard.Comparison);
                    writer.Str(guard.Operand);
                    writer.Bool(guard.ConsumeInput);
                }
                writer.U8((byte)step.OnGuardFalse);
            }
            else if (step.Guard.HasValue)
                throw new InvalidOperationException("guarded procedures require the guarded checkpoint schema");
            writer.I32(step.Arguments.Length);
            for (int j = 0; j < step.Arguments.Length; j++)
            {
                CortexProcedureArgument argument = step.Arguments[j];
                writer.Str(argument.Slot);
                writer.Str(argument.Channel);
                writer.U8((byte)argument.Source);
                writer.Bool(argument.ConsumeInput);
            }
        }
        writer.I32(_next);
        List<KeyValuePair<(string Channel, Blur.SlotSources Source), Queue<string>>> inputs =
            _inputs.OrderBy(static pair => pair.Key.Channel, StringComparer.Ordinal)
                   .ThenBy(static pair => pair.Key.Source)
                   .ToList();
        writer.I32(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            KeyValuePair<(string Channel, Blur.SlotSources Source), Queue<string>> input = inputs[i];
            writer.Str(input.Key.Channel);
            writer.U8((byte)input.Key.Source);
            writer.I32(input.Value.Count);
            foreach (string value in input.Value) writer.Str(value);
        }
        if (includeGuards)
        {
            writer.I32(_carriedGuards.Count);
            for (int i = 0; i < _carriedGuards.Count; i++)
            {
                CortexActionArgument argument = _carriedGuards[i];
                writer.Str(argument.Slot);
                writer.Str(argument.Value);
                writer.U8((byte)argument.Source);
            }
        }
    }

    public static CortexProcedure Load(CkptReader reader) => Load(reader, includesGuards: true);

    public static CortexProcedure Load(CkptReader reader, bool includesGuards)
    {
        int stepCount = ReadCount(reader, MaxSteps, "procedure step");
        CortexProcedureStep[] steps = new CortexProcedureStep[stepCount];
        for (int i = 0; i < stepCount; i++)
        {
            string tool = reader.Str();
            CortexProcedureGuard? guard = null;
            CortexProcedureFailureModes onGuardFalse = CortexProcedureFailureModes.Skip;
            if (includesGuards)
            {
                if (reader.Bool())
                {
                    string channel = reader.Str();
                    Blur.SlotSources source = ReadSource(reader);
                    CortexProcedureComparisons comparison = ReadComparison(reader);
                    string operand = reader.Str();
                    bool consumeInput = reader.Bool();
                    guard = new CortexProcedureGuard(channel, source, comparison, operand, consumeInput);
                }
                onGuardFalse = ReadFailureMode(reader);
            }
            int argumentCount = ReadCount(reader, MaxArguments, "procedure argument");
            CortexProcedureArgument[] arguments = new CortexProcedureArgument[argumentCount];
            for (int j = 0; j < argumentCount; j++)
                arguments[j] = new CortexProcedureArgument(reader.Str(), reader.Str(), ReadSource(reader), reader.Bool());
            steps[i] = new CortexProcedureStep(tool, arguments, guard, onGuardFalse);
        }
        CortexProcedure procedure = new(steps) { _next = reader.I32() };
        if (procedure._next < 0 || procedure._next > steps.Length)
            throw new InvalidDataException($"procedure step {procedure._next} exceeds {steps.Length}");
        int inputCount = ReadCount(reader, MaxInputs, "procedure input");
        for (int i = 0; i < inputCount; i++)
        {
            string channel = reader.Str();
            Blur.SlotSources source = ReadSource(reader);
            int valueCount = ReadCount(reader, MaxInputs, "procedure input value");
            Queue<string> values = new();
            if (!procedure._inputs.TryAdd((channel, source), values))
                throw new InvalidDataException($"duplicate procedure input {channel}/{source}");
            for (int j = 0; j < valueCount; j++) values.Enqueue(reader.Str());
        }
        if (includesGuards)
        {
            int carriedCount = ReadCount(reader, MaxArguments, "carried guard");
            for (int i = 0; i < carriedCount; i++)
                procedure._carriedGuards.Add(new CortexActionArgument(reader.Str(), reader.Str(), ReadSource(reader)));
        }
        return procedure;
    }

    private void ConsumeArguments(CortexProcedureArgument[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            CortexProcedureArgument argument = arguments[i];
            if (!argument.ConsumeInput) continue;
            bool alreadyConsumed = false;
            for (int j = 0; j < i; j++)
            {
                CortexProcedureArgument prior = arguments[j];
                if (prior.ConsumeInput && prior.Channel == argument.Channel && prior.Source == argument.Source)
                {
                    alreadyConsumed = true;
                    break;
                }
            }
            if (!alreadyConsumed) _inputs[(argument.Channel, argument.Source)].Dequeue();
        }
    }

    private static bool ConsumesChannel(CortexProcedureArgument[] arguments, string channel, Blur.SlotSources source)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            CortexProcedureArgument argument = arguments[i];
            if (argument.ConsumeInput && argument.Channel == channel && argument.Source == source) return true;
        }
        return false;
    }

    private static bool MatchesGuard(in CortexProcedureGuard guard, string value) => guard.Comparison switch
    {
        CortexProcedureComparisons.Present => true,
        CortexProcedureComparisons.Equal => string.Equals(value, guard.Operand, StringComparison.Ordinal),
        CortexProcedureComparisons.NotEqual => !string.Equals(value, guard.Operand, StringComparison.Ordinal),
        _ => throw new InvalidOperationException($"unknown procedure comparison {guard.Comparison}"),
    };

    private static string GetGuardSlot(in CortexProcedureGuard guard)
        => $"guard:{guard.Channel}:{GetComparisonToken(guard.Comparison)}:{Uri.EscapeDataString(guard.Operand)}";

    private static string GetComparisonToken(CortexProcedureComparisons comparison) => comparison switch
    {
        CortexProcedureComparisons.Present => "present",
        CortexProcedureComparisons.Equal => "equal",
        CortexProcedureComparisons.NotEqual => "not-equal",
        _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, "unknown procedure comparison"),
    };

    private static int ReadCount(CkptReader reader, int maximum, string name)
    {
        int count = reader.I32();
        if (count < 0 || count > maximum) throw new InvalidDataException($"{name} count {count} exceeds 0..{maximum}");
        return count;
    }

    private static Blur.SlotSources ReadSource(CkptReader reader)
    {
        Blur.SlotSources source = (Blur.SlotSources)reader.U8();
        ValidateSource(source);
        return source;
    }

    private static CortexProcedureComparisons ReadComparison(CkptReader reader)
    {
        CortexProcedureComparisons comparison = (CortexProcedureComparisons)reader.U8();
        if (!Enum.IsDefined(comparison)) throw new InvalidDataException($"unknown procedure comparison {(byte)comparison}");
        return comparison;
    }

    private static CortexProcedureFailureModes ReadFailureMode(CkptReader reader)
    {
        CortexProcedureFailureModes mode = (CortexProcedureFailureModes)reader.U8();
        if (!Enum.IsDefined(mode)) throw new InvalidDataException($"unknown procedure failure mode {(byte)mode}");
        return mode;
    }

    private static void ValidateSteps(CortexProcedureStep[] steps)
    {
        if (steps.Length > MaxSteps) throw new ArgumentOutOfRangeException(nameof(steps));
        for (int i = 0; i < steps.Length; i++)
        {
            CortexProcedureStep step = steps[i];
            if (string.IsNullOrWhiteSpace(step.Tool)) throw new ArgumentException($"procedure step {i} has no tool", nameof(steps));
            ArgumentNullException.ThrowIfNull(step.Arguments);
            if (step.Arguments.Length > MaxArguments) throw new ArgumentException($"procedure step {i} has too many arguments", nameof(steps));
            if (!Enum.IsDefined(step.OnGuardFalse)) throw new ArgumentException($"procedure step {i} has an invalid guard failure mode", nameof(steps));
            for (int j = 0; j < step.Arguments.Length; j++)
            {
                CortexProcedureArgument argument = step.Arguments[j];
                if (string.IsNullOrWhiteSpace(argument.Slot)) throw new ArgumentException($"procedure step {i} argument {j} has no slot", nameof(steps));
                ValidateChannel(argument.Channel);
                ValidateSource(argument.Source);
            }
            if (step.Guard is CortexProcedureGuard guard)
            {
                ValidateChannel(guard.Channel);
                ValidateSource(guard.Source);
                if (!Enum.IsDefined(guard.Comparison)) throw new ArgumentException($"procedure step {i} has an invalid guard comparison", nameof(steps));
            }
        }
    }

    private static void ValidateChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("procedure channel cannot be empty", nameof(channel));
    }

    private static void ValidateSource(Blur.SlotSources source)
    {
        if (!Enum.IsDefined(source)) throw new InvalidDataException($"unknown procedure source {(byte)source}");
    }
}
