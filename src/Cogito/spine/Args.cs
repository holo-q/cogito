namespace Cogito;

using System.Globalization;

// ── ARGS ──  the ONE CLI flag/positional parser every verb shares. Was hand-rolled Flag/FlagD/FlagS/FlagL/FlagI in
// ~8 files (Farm · Cortex · Radula · Seriate · CritLock · DomainWalk · Cli · GrokBell) — byte-identical loops, one
// home now. Beyond the fold it FIXES the positional-leak quirk: the old `args.Where(a => !a.StartsWith("--"))`
// dropped the --flags but KEPT their VALUES (which don't start with `--`), so `--energy energy` leaked "energy" into
// the steps positional slot and the run silently defaulted to 200 steps. Here a value-flag CONSUMES its next token,
// so only genuine positionals remain — and the flags win over any positional fallback (Cortex's `--steps N`).
public static class Args
{
    /// An int flag `--key N` → its value, or `d` if absent / unparseable.
    public static int Int(string[] a, string key, int d)
    { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == key && int.TryParse(a[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v; return d; }

    /// A long flag `--key N` (the grammar bit budget etc).
    public static long Long(string[] a, string key, long d)
    { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == key && long.TryParse(a[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v; return d; }

    /// A double flag `--key F` — invariant culture so a locale's decimal comma never shifts a curve (the Vow).
    public static double Double(string[] a, string key, double d)
    { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == key && double.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v; return d; }

    /// A string flag `--key val` → its value, or `d` if absent.
    public static string Str(string[] a, string key, string d)
    { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == key) return a[i + 1]; return d; }

    /// A boolean SWITCH — present anywhere in args (no value consumed).
    public static bool Has(string[] a, string key) => Array.IndexOf(a, key) >= 0;

    /// The run's LCG seed `--key HEX` (the Vow's replay key) — hex digits, `0x`-prefix optional. Absent ⇒ `d`.
    public static ulong Seed(string[] a, string key, ulong d)
    {
        var s = Str(a, key, "");
        if (s.Length == 0) return d;
        return ulong.TryParse(s.StartsWith("0x", StringComparison.Ordinal) ? s[2..] : s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : d;
    }

    /// The POSITIONAL tokens — args[skip..] that are neither a `--flag` nor the value a value-flag consumes. A
    /// `--flag` swallows its following token UNLESS the flag is a declared boolean SWITCH (`switches`) or the next
    /// token is itself a `--flag` (a trailing switch). This is what stops `--energy energy` from leaking "energy"
    /// into a positional slot (2a's report). Callers pass their boolean flags so the parse never eats a real positional.
    public static List<string> Positionals(string[] a, int skip, params string[] switches)
    {
        var pos = new List<string>();
        for (int i = skip; i < a.Length; i++)
        {
            if (a[i].StartsWith("--", StringComparison.Ordinal))
            {
                bool isSwitch = Array.IndexOf(switches, a[i]) >= 0;
                if (!isSwitch && i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal)) i++;   // skip the value this flag consumes
                continue;
            }
            pos.Add(a[i]);
        }
        return pos;
    }
}
