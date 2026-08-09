namespace Cogito;

using System;
using System.Collections.Generic;
using System.IO;

public readonly record struct OutcomeArmState(double YieldEma, int Outcomes, int Fires, int Decisive);

/// Records arm selection and outcome instrumentation independently of the policy choosing the arms.
public sealed class OutcomeMeter<TArm> where TArm : notnull
{
    private readonly TArm[] _arms;
    private readonly Dictionary<TArm, int> _index;
    private readonly MutableArmState[] _state;
    private readonly double _yieldDrift;
    private int _pendingIndex = -1;

    public OutcomeMeter(IReadOnlyList<TArm> arms, double yieldDrift = 1.0 / 8)
    {
        if (arms is null) throw new ArgumentNullException(nameof(arms));
        if (arms.Count == 0) throw new ArgumentException("an outcome meter requires at least one arm", nameof(arms));
        if (yieldDrift <= 0 || yieldDrift > 1) throw new ArgumentOutOfRangeException(nameof(yieldDrift));

        _arms = new TArm[arms.Count];
        _index = new Dictionary<TArm, int>(arms.Count);
        _state = new MutableArmState[arms.Count];
        for (int i = 0; i < arms.Count; i++)
        {
            TArm arm = arms[i];
            if (!_index.TryAdd(arm, i)) throw new ArgumentException("outcome meter arms must be unique", nameof(arms));
            _arms[i] = arm;
            _state[i].YieldEma = double.NaN;
        }
        _yieldDrift = yieldDrift;
    }

    public int Count => _arms.Length;
    public int PendingIndex => _pendingIndex;
    public TArm ArmAt(int index) => _arms[index];

    public OutcomeArmState Read(TArm arm) => ReadAt(IndexOf(arm));

    internal OutcomeArmState ReadAt(int index)
    {
        MutableArmState state = _state[index];
        return new OutcomeArmState(state.YieldEma, state.Outcomes, state.Fires, state.Decisive);
    }

    internal OutcomeArmState[] CaptureArmStates()
    {
        OutcomeArmState[] states = new OutcomeArmState[_state.Length];
        for (int i = 0; i < states.Length; i++) states[i] = ReadAt(i);
        return states;
    }

    internal void ApplyArmStates(IReadOnlyList<OutcomeArmState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count != _state.Length)
            throw new InvalidDataException($"outcome meter state count {states.Count} does not match {_state.Length}");
        for (int i = 0; i < states.Count; i++)
        {
            OutcomeArmState state = states[i];
            if (state.Outcomes < 0 || state.Fires < 0 || state.Decisive < 0)
                throw new InvalidDataException("outcome meter counters cannot be negative");
            if (state.Outcomes > state.Fires || state.Decisive > state.Fires)
                throw new InvalidDataException("outcome meter counters do not close");
            _state[i].YieldEma = state.YieldEma;
            _state[i].Outcomes = state.Outcomes;
            _state[i].Fires = state.Fires;
            _state[i].Decisive = state.Decisive;
        }
    }

    internal bool TryFindIndex(TArm arm, out int index) => _index.TryGetValue(arm, out index);

    public void RecordFire(TArm arm) => _state[IndexOf(arm)].Fires++;

    public void Pend(TArm arm, bool decisive = true)
    {
        int index = IndexOf(arm);
        _pendingIndex = index;
        if (decisive) _state[index].Decisive++;
    }

    public bool Meter(double yield)
    {
        if (_pendingIndex < 0) return false;
        MutableArmState state = _state[_pendingIndex];
        state.YieldEma = double.IsNaN(state.YieldEma)
            ? yield
            : state.YieldEma + _yieldDrift * (yield - state.YieldEma);
        state.Outcomes++;
        _state[_pendingIndex] = state;
        _pendingIndex = -1;
        return true;
    }

    public void RestorePendingIndex(int index)
    {
        if (index < -1 || index >= _arms.Length) throw new InvalidDataException($"outcome meter pending arm {index} is out of range");
        _pendingIndex = index;
    }

    /// Raw arm state only. Pending state remains separately persisted to preserve existing checkpoint layouts.
    public void SaveArmState(CkptWriter writer)
    {
        for (int i = 0; i < _state.Length; i++)
        {
            MutableArmState state = _state[i];
            writer.F64(state.YieldEma);
            writer.I32(state.Outcomes);
            writer.I32(state.Fires);
            writer.I32(state.Decisive);
        }
    }

    public void LoadArmState(CkptReader reader)
    {
        for (int i = 0; i < _state.Length; i++)
        {
            _state[i].YieldEma = reader.F64();
            _state[i].Outcomes = reader.I32();
            _state[i].Fires = reader.I32();
            _state[i].Decisive = reader.I32();
        }
    }

    private int IndexOf(TArm arm)
        => _index.TryGetValue(arm, out int index) ? index : throw new KeyNotFoundException($"unknown outcome meter arm '{arm}'");

    private struct MutableArmState
    {
        public double YieldEma;
        public int Outcomes;
        public int Fires;
        public int Decisive;
    }
}
