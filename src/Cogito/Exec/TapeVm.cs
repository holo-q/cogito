namespace Cogito.Exec;

using Cogito.Grammar;
using System.Globalization;

public enum WeftNumberKinds : byte
{
    Invalid,
    Int64,
    Float64,
}

/// A canonical VM datum. Code remains a byte grammar; numeric identity lives off the code plane as a typed
/// 64-bit atom. Canonical zero and NaN make execution hashes stable even when IEEE arithmetic admits several
/// bit representations of the same semantic result.
public readonly struct WeftNumber : IEquatable<WeftNumber>
{
    private const ulong FLOAT_EXPONENT = 0x7ff0000000000000UL;
    private const ulong FLOAT_MANTISSA = 0x000fffffffffffffUL;
    private const ulong CANONICAL_NAN = 0x7ff8000000000000UL;

    private WeftNumber(WeftNumberKinds kind, ulong bits)
    {
        Kind = kind;
        Bits = bits;
    }

    public WeftNumberKinds Kind { get; }
    public ulong Bits { get; }

    public static WeftNumber Invalid => new(WeftNumberKinds.Invalid, 0);
    public static WeftNumber Zero => FromInt64(0);
    public static WeftNumber One => FromInt64(1);

    public static WeftNumber FromInt64(long value) => new(WeftNumberKinds.Int64, unchecked((ulong)value));

    public static WeftNumber FromFloat64(double value)
    {
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        if ((bits << 1) == 0) bits = 0;
        else if ((bits & FLOAT_EXPONENT) == FLOAT_EXPONENT && (bits & FLOAT_MANTISSA) != 0) bits = CANONICAL_NAN;
        return new WeftNumber(WeftNumberKinds.Float64, bits);
    }

    public static bool TryCreateCanonical(WeftNumberKinds kind, ulong bits, out WeftNumber value)
    {
        switch (kind)
        {
            case WeftNumberKinds.Invalid when bits == 0:
                value = Invalid;
                return true;
            case WeftNumberKinds.Int64:
                value = new WeftNumber(kind, bits);
                return true;
            case WeftNumberKinds.Float64:
                value = FromFloat64(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
                return value.Bits == bits;
            default:
                value = Invalid;
                return false;
        }
    }

    public bool TryReadInt64(out long value)
    {
        value = unchecked((long)Bits);
        return Kind == WeftNumberKinds.Int64;
    }

    public bool TryReadFloat64(out double value)
    {
        value = BitConverter.Int64BitsToDouble(unchecked((long)Bits));
        return Kind == WeftNumberKinds.Float64;
    }

    public WeftNumber ConvertToFloat64()
    {
        if (Kind == WeftNumberKinds.Float64) return this;
        return TryReadInt64(out long value) ? FromFloat64(value) : Invalid;
    }

    public bool ReadsTrue() => TryReadInt64(out long value) && value != 0;

    public static WeftNumber AddIntegers(WeftNumber left, WeftNumber right)
        => left.TryReadInt64(out long a) && right.TryReadInt64(out long b) ? FromInt64(unchecked(a + b)) : Invalid;

    public static WeftNumber SubtractIntegers(WeftNumber left, WeftNumber right)
        => left.TryReadInt64(out long a) && right.TryReadInt64(out long b) ? FromInt64(unchecked(a - b)) : Invalid;

    public static WeftNumber MultiplyIntegers(WeftNumber left, WeftNumber right)
        => left.TryReadInt64(out long a) && right.TryReadInt64(out long b) ? FromInt64(unchecked(a * b)) : Invalid;

    public static WeftNumber CompareIntegersLessThan(WeftNumber left, WeftNumber right)
        => left.TryReadInt64(out long a) && right.TryReadInt64(out long b) ? FromInt64(a < b ? 1 : 0) : Invalid;

    public static WeftNumber AddFloats(WeftNumber left, WeftNumber right)
        => left.TryReadFloat64(out double a) && right.TryReadFloat64(out double b) ? FromFloat64(a + b) : Invalid;

    public static WeftNumber SubtractFloats(WeftNumber left, WeftNumber right)
        => left.TryReadFloat64(out double a) && right.TryReadFloat64(out double b) ? FromFloat64(a - b) : Invalid;

    public static WeftNumber MultiplyFloats(WeftNumber left, WeftNumber right)
        => left.TryReadFloat64(out double a) && right.TryReadFloat64(out double b) ? FromFloat64(a * b) : Invalid;

    public static WeftNumber DivideFloats(WeftNumber left, WeftNumber right)
        => left.TryReadFloat64(out double a) && right.TryReadFloat64(out double b) ? FromFloat64(a / b) : Invalid;

    public static WeftNumber CompareFloatsLessThan(WeftNumber left, WeftNumber right)
        => TryReadFloatPair(left, right, out double a, out double b) ? FromInt64(a < b ? 1 : 0) : Invalid;

    public static WeftNumber CompareFloatsLessThanOrEqual(WeftNumber left, WeftNumber right)
        => TryReadFloatPair(left, right, out double a, out double b) ? FromInt64(a <= b ? 1 : 0) : Invalid;

    public static WeftNumber CompareFloatsGreaterThan(WeftNumber left, WeftNumber right)
        => TryReadFloatPair(left, right, out double a, out double b) ? FromInt64(a > b ? 1 : 0) : Invalid;

    public static WeftNumber CompareFloatsGreaterThanOrEqual(WeftNumber left, WeftNumber right)
        => TryReadFloatPair(left, right, out double a, out double b) ? FromInt64(a >= b ? 1 : 0) : Invalid;

    public static WeftNumber CompareFloatsEqual(WeftNumber left, WeftNumber right)
        => TryReadFloatPair(left, right, out double a, out double b) ? FromInt64(a == b ? 1 : 0) : Invalid;

    private static bool TryReadFloatPair(WeftNumber left, WeftNumber right, out double a, out double b)
    {
        b = 0;
        return left.TryReadFloat64(out a) && right.TryReadFloat64(out b);
    }

    public bool Equals(WeftNumber other) => Kind == other.Kind && Bits == other.Bits;
    public override bool Equals(object? obj) => obj is WeftNumber other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((byte)Kind, Bits);
    public static bool operator ==(WeftNumber left, WeftNumber right) => left.Equals(right);
    public static bool operator !=(WeftNumber left, WeftNumber right) => !left.Equals(right);

    public override string ToString() => Kind switch
    {
        WeftNumberKinds.Int64 => unchecked((long)Bits).ToString(CultureInfo.InvariantCulture),
        WeftNumberKinds.Float64 => BitConverter.Int64BitsToDouble(unchecked((long)Bits)).ToString("R", CultureInfo.InvariantCulture),
        _ => "invalid",
    };
}

// The pinned v0 TAPE-VM — the executable-grammar step machine.
// Reconstruct.ExpandOne is the DAG expander; made ITERATIVE + Fuel-metered it IS the VM step, and that single
// change buys loops: a bootstrap rule may be SELF-REFERENTIAL (`R = BODY R`) — illegal for Re-Pair induction
// (DAG-only; knot-INDUCTION is deferred) but legal to author + execute here, because Fuel bounds the unroll
// where a conditional otherwise would. Execution emits a FLAT opcode-byte trace: a leaf opcode appends its
// byte, a nonterminal CALL appends NOTHING (call structure is re-discovered by induction as recurring
// substrings — the homoiconic reason `()` and bracket tokens are dropped). Push-total: an underfed op reads 0
// for its missing operands and still runs, so every program AND every substring is a well-formed
// stack-transformer (H1's concatenative closure) — the property that makes an induced substring a callable rule.
public sealed class TapeVm
{
    private readonly GrammarRule[] _rules;   // emission-order: nonterminal (FirstNonterminal+i) IS rule index i, the Reconstruct contract

    public TapeVm(GrammarRule[] rules) => _rules = rules;

    /// Run `start` under a `fuelBudget`-step budget, returning the flat opcode-byte trace + the final data
    /// stack + the per-rule Fuel journal. Deterministic: identical (rules, start, budget) ⟹ byte-identical trace.
    public ExecResult Run(ReadOnlySpan<Symbol> start, int fuelBudget)
        => Run(start, fuelBudget, ReadOnlySpan<WeftNumber>.Empty);

    /// Run with caller-supplied typed world data already present on the data stack.
    public ExecResult Run(ReadOnlySpan<Symbol> start, int fuelBudget, ReadOnlySpan<WeftNumber> initialData)
    {
        List<byte> trace = new(Math.Min(Math.Max(fuelBudget, 16), 1 << 22));
        DataStack ds = new();
        for (int i = 0; i < initialData.Length; i++) ds.Push(initialData[i]);
        FuelJournal journal = new(_rules.Length);
        Fuel fuel = new(fuelBudget);

        // The CONTINUATION stack — pending symbols in execution order, each tagged with the rule that expanded to
        // it (owner = -1 for the top-level start sequence). A body is pushed REVERSED so it pops left-to-right;
        // the owner tag is what lets the journal attribute a leaf op's Fuel to the rule that DIRECTLY emitted it.
        Stack<Cont> cont = new();
        for (int i = start.Length - 1; i >= 0; i--) cont.Push(new Cont(start[i], -1));

        while (cont.Count > 0)
        {
            if (!fuel.TrySpend()) break;                        // one step per pop (call OR leaf) — guarantees even `R = R` halts
            Cont c = cont.Pop();
            Symbol s = c.Sym;
            if (c.Owner >= 0) journal.BodyFuel[c.Owner]++;

            if (s.IsNonterminal)
            {
                int r = (int)(s.Value - Symbol.FirstNonterminal);
                journal.Calls[r]++;
                Symbol[] body = _rules[r].Pattern;
                for (int i = body.Length - 1; i >= 0; i--) cont.Push(new Cont(body[i], r));   // expand: NOT emitted, body owned by r
                continue;
            }

            Opcodes op = (Opcodes)(byte)s.Value;
            if (op == Opcodes.Cond)
            {
                // execute-or-skip-NEXT (ContConsume=1): pop the predicate, then EAT the next continuation item —
                // push it back to run it (predicate ≠ 0) or discard it to skip (0). A dangling `?` (empty
                // continuation) is a valid no-op continuation-transformer, never dead like a dangling `(`.
                WeftNumber predicate = ds.Pop();
                if (cont.Count > 0) { Cont next = cont.Pop(); if (predicate.ReadsTrue()) cont.Push(next); }
            }
            else Exec(op, ds);

            trace.Add((byte)s.Value);                           // the flat opcode-byte trace — leaf emits only
            if (c.Owner >= 0) journal.LeafFuel[c.Owner]++;
        }

        return new ExecResult(trace.ToArray(), ds.Snapshot(), journal, fuel.Remaining, cont.Count == 0);
    }

    /// Apply one opcode's push-total stack effect. Underflow reads 0 (DataStack.Pop/Peek), so no op is partial —
    /// the closure guarantee. `Cond` is absent here: it transforms the CONTINUATION, so the run loop owns it.
    private static void Exec(Opcodes op, DataStack ds)
    {
        switch (op)
        {
            case Opcodes.Zero: ds.Push(WeftNumber.Zero); break;
            case Opcodes.One: ds.Push(WeftNumber.One); break;
            case Opcodes.Add: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.AddIntegers(a, b)); break; }
            case Opcodes.Sub: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.SubtractIntegers(a, b)); break; }
            case Opcodes.Mul: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.MultiplyIntegers(a, b)); break; }
            case Opcodes.Lt: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareIntegersLessThan(a, b)); break; }
            case Opcodes.ToFloat: ds.Push(ds.Pop().ConvertToFloat64()); break;
            case Opcodes.FloatAdd: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.AddFloats(a, b)); break; }
            case Opcodes.FloatSub: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.SubtractFloats(a, b)); break; }
            case Opcodes.FloatMul: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.MultiplyFloats(a, b)); break; }
            case Opcodes.FloatDiv: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.DivideFloats(a, b)); break; }
            case Opcodes.FloatLt: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsLessThan(a, b)); break; }
            case Opcodes.FloatLe: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsLessThanOrEqual(a, b)); break; }
            case Opcodes.FloatGt: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsGreaterThan(a, b)); break; }
            case Opcodes.FloatGe: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsGreaterThanOrEqual(a, b)); break; }
            case Opcodes.FloatEq: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(WeftNumber.CompareFloatsEqual(a, b)); break; }
            case Opcodes.Dup:  ds.Push(ds.Peek()); break;
            case Opcodes.Swap: { WeftNumber b = ds.Pop(), a = ds.Pop(); ds.Push(b); ds.Push(a); break; }
            case Opcodes.Drop: ds.Pop(); break;
            default: throw new ArgumentOutOfRangeException(nameof(op));
        }
    }

    // A pending instruction + the rule that expanded to it (-1 = the top-level start sequence). Owner drives the
    // journal's per-rule Fuel attribution; without it a leaf op could not name which rule's use it belongs to.
    private readonly record struct Cont(Symbol Sym, int Owner);

    // The data stack — push-total (underflow reads 0), grows on demand. A private class: one heap object per run,
    // mutated in place; the VM never exposes it except as the immutable snapshot in ExecResult.
    private sealed class DataStack
    {
        private WeftNumber[] _a = new WeftNumber[64];
        private int _n;

        public void Push(WeftNumber value)
        {
            if (_n == _a.Length) Array.Resize(ref _a, _a.Length * 2);
            _a[_n++] = value;
        }

        public WeftNumber Pop() => _n > 0 ? _a[--_n] : WeftNumber.Zero;
        public WeftNumber Peek() => _n > 0 ? _a[_n - 1] : WeftNumber.Zero;
        public WeftNumber[] Snapshot() => _a[.._n];
    }
}

// The outcome of one run: the flat opcode-byte `Trace` (the tape the tower induces over), the final `Data`
// stack (bottom→top — the operands that DIVERGED off the code plane while the trace stayed byte-identical per
// iteration, Exp-1's whole point), the per-rule `Journal`, the `FuelLeft` at halt, and `Halted` = the
// continuation drained naturally (true) vs Fuel truncated an unbounded loop (false).
public readonly record struct ExecResult(byte[] Trace, WeftNumber[] Data, FuelJournal FuelJournal, int FuelLeft, bool Halted)
{
    public WeftNumber DataTop => Data.Length > 0 ? Data[^1] : WeftNumber.Zero;
    public int FuelSpent(int budget) => budget - FuelLeft;
}
