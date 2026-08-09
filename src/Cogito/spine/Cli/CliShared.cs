namespace Cogito.Cli;

using System.CommandLine;
using System.Globalization;
using System.Text;

// ── SHARED CLI VOCABULARY ──  the option/argument atoms reused across cogito's verbs, minted ONCE so
// every command that takes `--seed` or a corpus positional gets the SAME parser, help text, and default.
// This is the System.CommandLine replacement for the old hand-rolled Args helper: where Args scraped
// string[] per verb (Args.Int/Str/Has/Seed), each atom here is a typed Option<T>/Argument<T> the parser
// binds and validates, read back in a command's SetAction via ParseResult.GetValue. AOT-safe: no
// reflection model-binding — every value is pulled explicitly.
//
// Minting discipline: an Option instance is STATELESS and reusable across commands, so the truly shared
// atoms (the corpus positional, --seed) are static singletons here. A knob that only ONE verb owns stays
// local to that verb's Build() (a system's config is its own primitive — do not over-pool). The census:
// --seed recurs across ~12 verbs (Seed hex), the corpus positional across ~15 (LoadCorpus) — those earn
// a shared home; --steps/--looks/--len etc. are per-verb and stay local.
internal static class CliShared
{
    /// The optional corpus positional — `cogito <verb> [corpus-path]`. Absent/missing ⇒ the builtin
    /// repetitive sample (LoadCorpus's contract). ~15 introspection verbs share this exact shape.
    public static Argument<string?> CorpusArg() =>
        new("corpus") { Arity = ArgumentArity.ZeroOrOne, Description = "corpus file; omitted/missing ⇒ builtin sample" };

    /// The run's LCG seed `--seed HEX` (the Vow's replay key) — hex digits, `0x`-prefix optional. Parsed
    /// from a string so hex survives (System.CommandLine's ulong parser is decimal-only); the verb calls
    /// ParseSeed on the raw value with its own default. Reused wherever the old Args.Seed appeared.
    public static Option<string?> SeedOpt(string description = "LCG replay seed (hex, 0x-optional)") =>
        new("--seed") { Description = description };

    /// Resolve a `--seed` raw string to its ulong, or the verb's default when absent/unparseable — the
    /// exact semantics of the old Args.Seed(args, "--seed", d).
    public static ulong ParseSeed(string? raw, ulong dflt)
    {
        if (string.IsNullOrEmpty(raw)) return dflt;
        var s = raw.StartsWith("0x", StringComparison.Ordinal) ? raw[2..] : raw;
        return ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : dflt;
    }

    /// LoadCorpus's contract, re-homed off the parsed corpus argument: the file's bytes, or the builtin
    /// sample when the path is null/missing. The one place the builtin lives (was Cli.Builtin).
    public static byte[] LoadCorpus(string? corpusPath) =>
        corpusPath is not null && File.Exists(corpusPath)
            ? File.ReadAllBytes(corpusPath)
            : Encoding.UTF8.GetBytes(Builtin);

    public const string Builtin =
        "def add(a, b): return a + b\ndef add(a, b): return a + b\ndef add(a, b): return a + b\n" +
        "for i in range(10): print(i)\nfor i in range(10): print(i)\n" +
        "the quick brown fox jumps over the lazy dog\nthe quick brown fox jumps over the lazy dog\n";
}
