namespace Cogito.Exec;

using System.Text;

// The BIOS — the primitive instruction set the pinned tape-VM executes. A concatenative,
// push-total stack machine with separate integer and float arithmetic plus nonterminal-CALL
// (a Symbol ≥ Symbol.FirstNonterminal, dispatched by TapeVm — never an opcode byte) and Fuel (the step
// budget, Fuel.cs). Each opcode's enum value IS the byte it emits to the
// execution trace, so the trace is a FLAT opcode-byte stream with operands off the code plane — the
// homoiconic contract: call structure is never logged, it is re-discovered by induction as recurring
// substrings. 0x0A is deliberately absent from every opcode byte: it is the span barrier a repeated LINE
// would tower under, excluded so barrier-free trace induction can see the doubling tower.
public enum Opcodes : byte
{
    Zero = (byte)'0',    // → 0           the ONLY literals are 0/1; every other value is COMPUTED, so it never
    One  = (byte)'1',    // → 1           appears on the code plane (operands stay on the data stack)
    Add  = (byte)'+',    // b a → a+b
    Sub  = (byte)'-',    // b a → a−b
    Mul  = (byte)'*',    // b a → a*b
    Lt   = (byte)'<',    // b a → (a<b)?1:0
    ToFloat = (byte)'F', // int a → float(a); float a → a
    FloatAdd = (byte)'A',
    FloatSub = (byte)'S',
    FloatMul = (byte)'M',
    FloatDiv = (byte)'/',
    FloatLt = (byte)'L',
    FloatLe = (byte)'l',
    FloatGt = (byte)'G',
    FloatGe = (byte)'g',
    FloatEq = (byte)'=',
    Dup  = (byte)':',    // a → a a
    Swap = (byte)'\\',   // b a → a b
    Drop = (byte)'_',    // a →
    Cond = (byte)'?',    // p → ;  execute-or-skip the NEXT continuation item (ContConsume=1)
}

// The concatenative stack-effect TYPE of an opcode: net stack-height change `DeltaH`,
// the MINIMUM operands a "fed" call needs `MinReq` (push-total lets an underfed op still run — reads below the
// floor see 0 — so MinReq is the CONTRACT Exp-2 inputs are gated at, never a runtime guard), and how many
// CONTINUATION items the op consumes off the code stream `ContConsume` (only `?` does — it eats the next
// instruction, executing or skipping it under a predicate).
public readonly record struct StackEffect(sbyte DeltaH, byte MinReq, byte ContConsume);

// The BIOS table + the op-string codec — the one home the instruction set is authored in. `Effect` pins each
// opcode's stack-type; `Parse`/`Render` move between the human op-string ("1 : +") and the Symbol tape the VM
// runs (terminals only — a rule CALL is authored as a raw Symbol ≥ FirstNonterminal, never spelled here).
public static class Bios
{
    /// The stack-effect type of `op` — authored beside the opcode so the type can never drift from the byte.
    public static StackEffect Effect(Opcodes op) => op switch
    {
        Opcodes.Zero or Opcodes.One  => new(+1, 0, 0),
        Opcodes.Add or Opcodes.Sub or Opcodes.Mul or Opcodes.Lt
            or Opcodes.FloatAdd or Opcodes.FloatSub or Opcodes.FloatMul or Opcodes.FloatDiv
            or Opcodes.FloatLt or Opcodes.FloatLe or Opcodes.FloatGt or Opcodes.FloatGe or Opcodes.FloatEq
            => new(-1, 2, 0),
        Opcodes.ToFloat => new(0, 1, 0),
        Opcodes.Dup  => new(+1, 1, 0),
        Opcodes.Swap => new(0, 2, 0),
        Opcodes.Drop => new(-1, 1, 0),
        Opcodes.Cond => new(-1, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// A trace/opcode byte → its `Opcodes` case, or false for any non-opcode byte (0x0A among them). The dense
    /// switch is AOT-clean where Enum.IsDefined would box + reflect.
    public static bool TryOpcode(byte b, out Opcodes op)
    {
        switch ((char)b)
        {
            case '0': op = Opcodes.Zero; return true;
            case '1': op = Opcodes.One;  return true;
            case '+': op = Opcodes.Add;  return true;
            case '-': op = Opcodes.Sub;  return true;
            case '*': op = Opcodes.Mul;  return true;
            case '<': op = Opcodes.Lt;   return true;
            case 'F': op = Opcodes.ToFloat; return true;
            case 'A': op = Opcodes.FloatAdd; return true;
            case 'S': op = Opcodes.FloatSub; return true;
            case 'M': op = Opcodes.FloatMul; return true;
            case '/': op = Opcodes.FloatDiv; return true;
            case 'L': op = Opcodes.FloatLt; return true;
            case 'l': op = Opcodes.FloatLe; return true;
            case 'G': op = Opcodes.FloatGt; return true;
            case 'g': op = Opcodes.FloatGe; return true;
            case '=': op = Opcodes.FloatEq; return true;
            case ':': op = Opcodes.Dup;  return true;
            case '\\': op = Opcodes.Swap; return true;
            case '_': op = Opcodes.Drop; return true;
            case '?': op = Opcodes.Cond; return true;
            default: op = default; return false;
        }
    }

    /// Parse an op-string ("1 : +", whitespace ignored) into the Symbol tape of TERMINALS the VM runs. Throws
    /// on any char that is not an opcode — a malformed program fails loud, never silently mis-executes.
    public static Symbol[] Parse(string src)
    {
        List<Symbol> tape = new(src.Length);
        foreach (char c in src)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (!TryOpcode((byte)c, out Opcodes op)) throw new ArgumentException($"'{c}' is not an opcode (valid: 0 1 + - * < F A S M / L l G g = : \\ _ ?)", nameof(src));
            tape.Add(new Symbol((uint)(byte)op));
        }
        return tape.ToArray();
    }

    /// Render a flat opcode-byte trace back to its mnemonic string (the trace bytes ARE ASCII opcode chars);
    /// `max` truncates the head for a report line, appending an ellipsis when clipped.
    public static string Render(ReadOnlySpan<byte> trace, int max = int.MaxValue)
    {
        int n = Math.Min(trace.Length, max);
        StringBuilder sb = new(n + 1);
        for (int i = 0; i < n; i++) sb.Append((char)trace[i]);
        if (trace.Length > max) sb.Append('…');
        return sb.ToString();
    }
}
