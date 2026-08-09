using System.CommandLine;
using System.Linq;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using static Cogito.CliReports;

namespace Cogito.Cli
{

// ── KERNEL COMMANDS ──  the introspection cluster: the instruments you read to know WHERE you are over the
// deterministic engine — the grammar it learned, whether it's a language or a hoard, what the substrate holds.
// Each verb's typed Option<T>/Argument<T> own validation + generated help; the SetAction reads ParseResult and
// invokes the verb BODY that lives right here in the cluster (no more Cli-partial bridge hop). The [corpus?]
// family is TYPED-CALL (byte[] straight to the body); the string[] bodies keep their own Args scrape and the
// handler rebuilds the minimal argv they expect (ADAPTER-ARGV). Shared report helpers come from CliReports.
//
// SEED NORMALIZATION (structmatch + scoreboard): their bodies read --seed as DECIMAL (ArgInt / ulong.TryParse),
// but the front rides CliShared.SeedOpt + ParseSeed (HEX, 0x-optional) like every other cogito verb — so the
// handler parses hex, then feeds the body a DECIMAL string, and the body resolves it identically. (The hex front
// is a surface break from the old decimal-only seed; recorded in the grouping commit.)
internal static class KernelCommands
{
    // Verbs are registered by CliRoot: the introspection + verify gates land under the `kernel` cluster; the
    // structmatch bridge is a research probe (registered under `probe`).

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE [corpus?] FAMILY — verbs that take just the optional corpus positional (CorpusArg + LoadCorpus,
    //  falling to the builtin sample when absent/missing — NOT AcceptExistingOnly). One shape, mirrored from
    //  the `stats` prototype: resolve the corpus bytes, hand them to the verb body.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    // ── prove ──  the codec proof, the DEFAULT verb (Pipeline.Run over the corpus → 0/1).
    public static Command Prove()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("prove", "the codec proof (Re-Pair + MDL round-trip) — the default verb") { corpus };
        cmd.SetAction(parse => Pipeline.Run(CliShared.LoadCorpus(parse.GetValue(corpus))) ? 0 : 1);
        return cmd;
    }

    // ── grammar ──  what cogito learned: each rule's expansion, ordered by how load-bearing it is.
    public static Command Grammar()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("grammar", "what cogito learned — rules by load, most load-bearing first") { corpus };
        cmd.SetAction(parse => DumpGrammar(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── log ──  the event-sourced substrate: every event's schema + content hash, the mutation packet.
    public static Command Log()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("log", "the event-sourced substrate — every event's schema + content hash") { corpus };
        cmd.SetAction(parse => DumpLog(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── heaps ──  rule growth (Heaps' law β over prefixes) + a byte-shuffle permutation significance test.
    public static Command Heaps()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("heaps", "rule growth (Heaps β) + structure z vs a byte-shuffled null") { corpus };
        cmd.SetAction(parse => Heaps(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── interp ──  mechanistic read of the grammar's internals (composition depth, load-bearing concepts, deepest abstractions).
    public static Command Interp()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("interp", "mechanistic interpretability — the grammar's abstraction ladder") { corpus };
        cmd.SetAction(parse => Interp(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── renorm ──  the RG-flow probe: stratify the grammar by abstraction scale, test the criticality exponent for scale-invariance.
    public static Command Renorm()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("renorm", "the renormalization-group probe — is the fixed point scale-invariant?") { corpus };
        cmd.SetAction(parse => Renorm(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── grok ──  watch the RG fixed point CRYSTALLIZE as data grows (scales / scale-invariance / held-out over growing prefixes).
    public static Command Grok()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("grok", "grokking probe — abstraction emerging suddenly at a critical data size") { corpus };
        cmd.SetAction(parse => Grok(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── mergetrace ──  re-pair induction as an observable merge-event stream, re-induced as its own meta-grammar.
    public static Command MergeTrace()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("mergetrace", "induction as observable cognition — the merge-event stream re-modeled") { corpus };
        cmd.SetAction(parse => MergeTraceRun(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  EXPORT / COUPLINGS — a [corpus?] positional PLUS an optional out-path positional (defaults grammar.json /
    //  couplings.json inside the body). The body reads args[2] for the out-path, so the argv is rebuilt as
    //  { verb, corpus?, outPath? }. NOTE the body's out-path is args[2] — so a corpus MUST precede it positionally
    //  (matching the current CLI: `export [corpus] [out]`). Absent corpus ⇒ builtin AND the out-path can't be given
    //  without a corpus (same as today — a lone positional is read as the corpus). ADAPTER-ARGV.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    // ── export ──  the raw learnt structure as JSON (world boundary → Python viz). out-path default grammar.json.
    public static Command Export()
    {
        var corpus  = CliShared.CorpusArg();
        var outPath = new Argument<string?>("out-path") { Arity = ArgumentArity.ZeroOrOne, Description = "JSON out path (default grammar.json)" };
        var cmd = new Command("export", "the learnt grammar as JSON (rules DAG + tape + uses) → viz") { corpus, outPath };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "export" };
            AddCorpusThenOut(argv, parse.GetValue(corpus), parse.GetValue(outPath));
            return Export(argv.ToArray());
        });
        return cmd;
    }

    // ── couplings ──  the LEARNED PPMI coupling graph as JSON (world boundary → the topology viz). out-path default couplings.json.
    public static Command Couplings()
    {
        var corpus  = CliShared.CorpusArg();
        var outPath = new Argument<string?>("out-path") { Arity = ArgumentArity.ZeroOrOne, Description = "JSON out path (default couplings.json)" };
        var cmd = new Command("couplings", "the learned PPMI coupling graph as JSON (nodes/edges/walks) → viz") { corpus, outPath };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "couplings" };
            AddCorpusThenOut(argv, parse.GetValue(corpus), parse.GetValue(outPath));
            return DumpCouplings(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  KNOW / FIX — the DUAL-MODE probe pair. Body reads a required <corpus> (LoadCorpus reads args[1]) then a
    //  probe: `File.Exists(args[^1])` ⇒ read that file as bytes, ELSE `string.Join(' ', args[2..])` as text. So
    //  the probe is a VARIADIC positional (one-or-more) whose LAST element decides file-vs-text. Rebuild the argv
    //  as { verb, corpus, probe... } verbatim — the body's args[^1]/args[2..] scrape resolves identically. Both
    //  require ≥1 probe token (usage guard: args.Length < 3). ADAPTER-ARGV.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    // ── know ──  structural self-awareness — marks what the grammar KNOWS vs its known-unknowns over the probe.
    public static Command Know()
    {
        var corpus = new Argument<string>("corpus")     { Description = "the substrate to induce the grammar over" };
        var probe  = new Argument<string[]>("probe")    { Arity = ArgumentArity.OneOrMore, Description = "probe: a file (if the LAST token is an existing path) else the joined text" };
        var cmd = new Command("know", "structural self-awareness — «marks» the grammar's known-unknowns") { corpus, probe };
        cmd.SetAction(parse => Know(DualModeArgv("know", parse.GetValue(corpus)!, parse.GetValue(probe)!)));
        return cmd;
    }

    // ── fix ──  error correction — localize uncovered words, propose the nearest grammar CONCEPT by edit distance.
    public static Command Fix()
    {
        var corpus = new Argument<string>("corpus")     { Description = "the substrate to induce the grammar over" };
        var probe  = new Argument<string[]>("probe")    { Arity = ArgumentArity.OneOrMore, Description = "probe: a file (if the LAST token is an existing path) else the joined text" };
        var cmd = new Command("fix", "grammar autocorrect — nearest concept by edit distance over uncovered words") { corpus, probe };
        cmd.SetAction(parse => Fix(DualModeArgv("fix", parse.GetValue(corpus)!, parse.GetValue(probe)!)));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  REQUIRED-FILE POSITIONALS — verbs whose body demands specific existing inputs. We do NOT mark them
    //  AcceptExistingOnly at the arg layer (the body owns the File.Exists guard + its exact usage string, kept
    //  byte-identical); the typed Argument just names + documents them. ADAPTER-ARGV.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    // ── tokrenorm ──  TOKEN-resolution renorm over a uint32 token-ID stream. Body requires args[1] to exist.
    public static Command TokRenorm()
    {
        var tokenFile = new Argument<string>("token-file") { Description = "a uint32 token-ID stream (an LLM tokenizer's output)" };
        var cmd = new Command("tokrenorm", "token-resolution renorm — does it grok at token grain where bytes missed?") { tokenFile };
        cmd.SetAction(parse => TokRenorm(["tokrenorm", parse.GetValue(tokenFile)!]));
        return cmd;
    }

    // ── overlap ──  grammar similarity (Jaccard of learned concepts). Body requires BOTH corpusA + corpusB to exist.
    public static Command Overlap()
    {
        var corpusA = new Argument<string>("corpusA") { Description = "first domain corpus" };
        var corpusB = new Argument<string>("corpusB") { Description = "second domain corpus" };
        var cmd = new Command("overlap", "grammar similarity — Jaccard of the concepts two grammars both learned") { corpusA, corpusB };
        cmd.SetAction(parse => Overlap(["overlap", parse.GetValue(corpusA)!, parse.GetValue(corpusB)!]));
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE HAND-ROLLED-PARSER VERBS — structmatch + scoreboard own bespoke int/ulong arg loops (NOT the Args helper).
    //  Typed here from their EXACT current defaults, and their --seed NORMALIZED to CliShared.ParseSeed/hex (the
    //  behavior change reported below). The parsed ulong is passed back as a DECIMAL string so each untouched body
    //  reads it identically (structmatch: (ulong)ArgInt "--seed" 1 ; scoreboard: ulong.TryParse "--seed" 0).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    // ── structmatch ──  the STRUCTURAL FEATURE-MATCH BRIDGE. Old parser: ArgInt --bytes 120000 / --n 1200 / --seed 1
    // (DECIMAL) / --shuffles 128 / --top 12 (StructMatch.Run:54-58). SEED NORMALIZED to hex (ParseSeed default 1).
    public static Command StructMatch()
    {
        var bytes    = new Option<int?>("--bytes")    { Description = "per-pool byte cap (default 120000)" };
        var n        = new Option<int?>("--n")        { Description = "rules sampled per grammar for the match (default 1200)" };
        var seed     = CliShared.SeedOpt("structmatch seed (hex, default 1)");
        var shuffles = new Option<int?>("--shuffles") { Description = "null shuffle replicates (default 128)" };
        var top      = new Option<int?>("--top")      { Description = "top homolog pairs to inspect (default 12)" };
        var cmd = new Command("structmatch", "the structural feature-match bridge — same niche = structurally analogous, alphabet-blind")
        {
            bytes, n, seed, shuffles, top
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "structmatch" };
            AddInt(argv, "--bytes",    parse.GetValue(bytes));
            AddInt(argv, "--n",        parse.GetValue(n));
            AddSeedDecimal(argv, parse.GetValue(seed), dflt: 1);   // hex-in → decimal-out (body's ArgInt reads decimal)
            AddInt(argv, "--shuffles", parse.GetValue(shuffles));
            AddInt(argv, "--top",      parse.GetValue(top));
            return global::Cogito.StructMatch.Run(argv.ToArray());
        });
        return cmd;
    }

    // ── scoreboard ──  the composed machine's ACCEPTANCE HARNESS. Old parser: <path> (file-or-dir, first non-flag
    // positional, required) + --seed 0 via ulong.TryParse (DECIMAL, Scoreboard.Run:41-48). SEED NORMALIZED to hex
    // (ParseSeed default 0). The path positional is required and file-OR-dir — the body owns the existence guard.
    public static Command Scoreboard()
    {
        var path = new Argument<string>("path") { Description = "corpus file OR directory (a dir = self-read, concatenated in Ordinal order)" };
        var seed = CliShared.SeedOpt("scoreboard null-battery seed (hex, default 0)");
        var cmd = new Command("scoreboard", "the acceptance harness — self-recognition above the null battery") { path, seed };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "scoreboard", parse.GetValue(path)! };
            AddSeedDecimal(argv, parse.GetValue(seed), dflt: 0);   // hex-in → decimal-out (body's ulong.TryParse reads decimal)
            return global::Cogito.Scoreboard.Run(argv.ToArray());
        });
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE VERIFY GATES — determinism/correctness harnesses. verify-induct + verify-loom take an OPTIONAL [corpus?]
    //  (appended to their built-in danger-zone minefield when it File.Exists — InductCases / the batch path);
    //  verify-loom ALSO carries a --bench mode-switch that hijacks to LoomBench. verify-weighted takes ZERO args.
    //  The corpus here is a plain optional positional (NOT AcceptExistingOnly — the body silently ignores a
    //  missing path, same as today). ADAPTER-ARGV.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    // ── verify-induct ──  the O(Δ) correctness gate — linear Induce must be BYTE-IDENTICAL to the reference (barrier off + armed).
    public static Command VerifyInduct()
    {
        var corpus = new Argument<string?>("corpus") { Arity = ArgumentArity.ZeroOrOne, Description = "extra corpus appended to the danger-zone suite (used only if it exists)" };
        var cmd = new Command("verify-induct", "linear Induce vs the O(n·rules) reference — must be byte-identical") { corpus };
        cmd.SetAction(parse => VerifyInduct(CorpusArgv("verify-induct", parse.GetValue(corpus))));
        return cmd;
    }

    // ── verify-loom ──  the LOOM's differential gate (batch-identity · incremental · resume). --bench → the O(Δ) payoff readout (LoomBench).
    public static Command VerifyLoom()
    {
        var corpus = new Argument<string?>("corpus") { Arity = ArgumentArity.ZeroOrOne, Description = "extra corpus appended to the danger-zone suite (used only if it exists)" };
        var bench  = new Option<bool>("--bench") { Description = "the O(Δ) payoff readout — loom vs batch induce-wall on a growing tape (mode-switch → LoomBench)" };
        var cmd = new Command("verify-loom", "persistent splice+pump vs the batch oracle — the incremental-induction gate") { corpus, bench };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "verify-loom" };
            if (parse.GetValue(corpus) is { Length: > 0 } c) argv.Add(c);   // positional before the switch (InductCases reads args[1])
            if (parse.GetValue(bench)) argv.Add("--bench");
            return VerifyLoom(argv.ToArray());
        });
        return cmd;
    }

    // ── verify-weighted ──  the reflect=permanence kill-line (Pearl.RunKillLine). ZERO args.
    public static Command VerifyWeighted()
    {
        var cmd = new Command("verify-weighted", "the vest=permanence contract kill-line (weighted-induction check)");
        cmd.SetAction(_ => Pearl.RunKillLine());
        return cmd;
    }

    /// The grammar-analysis owner gate: shared sequence basis + shape planes must remain
    /// identical to fresh Engine products across append, replace, and reset publications.
    public static Command VerifyGrammarAnalysis()
    {
        var cmd = new Command("verify-grammar-analysis", "publication-driven grammar shape/sequence differential oracle");
        cmd.SetAction(_ => GrammarAnalysisOracle.Verify());
        return cmd;
    }

    /// The Energy count/scorer publication gate: local append evidence and explicit reset
    /// are compared with fresh counts, transition tables, scorer CSR, and sample bytes.
    public static Command VerifyEnergyIncremental()
    {
        var cmd = new Command("verify-energy-incremental", "publication-driven Energy counts/transitions/scorers differential oracle");
        cmd.SetAction(_ => EnergyIncrementalOracle.Verify());
        return cmd;
    }

    // (simhash + simhash-vectors moved to SimhashCommands.cs — typed handlers, no bridge; registered by CliRoot.)

    // ── argv-rebuild helpers (the ADAPTER-ARGV bridge for the two hand-rolled-parser probes) ──
    private static void AddInt(List<string> argv, string key, int? val)    { if (val is int v)  { argv.Add(key); argv.Add(v.ToString()); } }

    /// The seed-normalization bridge: parse the raw --seed via CliShared.ParseSeed (HEX, org convention), then emit
    /// it as a DECIMAL string so a hand-rolled body that reads it with int/ulong.TryParse resolves identically. When
    /// the option is absent the parsed value IS the default, so we emit nothing (the body applies its own default) —
    /// preserving byte-identical no-flag behavior.
    private static void AddSeedDecimal(List<string> argv, string? raw, ulong dflt)
    {
        if (string.IsNullOrEmpty(raw)) return;                 // absent ⇒ let the body default (no --seed on the argv)
        ulong v = CliShared.ParseSeed(raw, dflt);
        argv.Add("--seed"); argv.Add(v.ToString());            // hex-in → decimal-out
    }

    /// The `[corpus?] [out-path?]` positional pair (export/couplings): the body reads corpus at args[1], out-path at
    /// args[2] — so an out-path can only follow a corpus. Emit corpus then (iff a corpus was given) the out-path.
    private static void AddCorpusThenOut(List<string> argv, string? corpus, string? outPath)
    {
        if (corpus is { Length: > 0 } c)
        {
            argv.Add(c);
            if (outPath is { Length: > 0 } o) argv.Add(o);
        }
    }

    /// The dual-mode `<corpus> <probe...>` argv (know/fix): { verb, corpus, probe... } verbatim — the body's
    /// `File.Exists(args[^1]) ? file : string.Join(' ', args[2..])` scrape decides file-vs-text unchanged.
    private static string[] DualModeArgv(string verb, string corpus, string[] probe)
    {
        var argv = new List<string>(probe.Length + 2) { verb, corpus };
        argv.AddRange(probe);
        return argv.ToArray();
    }

    /// The optional-corpus argv (verify-induct etc.): { verb } or { verb, corpus } — the body appends it to its
    /// suite only when it exists, so passing a non-file is harmless (matches today).
    private static string[] CorpusArgv(string verb, string? corpus) =>
        corpus is { Length: > 0 } c ? [verb, c] : [verb];

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    //  THE VERB BODIES — introspection over the deterministic engine + the byte-identity verify gates. Reports go
    //  to stdout (the report IS the payload); engine progress traces are separate (Trace / VTR-when-stuck). The
    //  [corpus?] family takes byte[] straight; the string[] bodies own an internal Args scrape the handlers feed.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    // ── grammar ──  what cogito learned: each rule's expansion, ordered by how load-bearing it is.
    private static int DumpGrammar(byte[] corpus)
    {
        var (_, n, r) = Engine.Induce(corpus);
        var uses = RuleUses(r);
        Console.WriteLine($"grammar · {n} bytes → {r.Compressed.Length} symbols + {r.Rules.Length} rules · Δmdl {r.TotalSavings}");

        var order = Enumerable.Range(0, r.Rules.Length).OrderByDescending(i => uses[i]).ToArray();
        int shown = 0;
        foreach (var i in order)
        {
            if (shown++ >= 50) { Console.WriteLine($"  …+{r.Rules.Length - 50} more rules"); break; }
            uint nt = Symbol.FirstNonterminal + (uint)i;
            var exp = Reconstruct.Expand(r.Rules, [new Symbol(nt)]);
            Console.WriteLine($"  N{nt,-5} ×{uses[i],-4} {Show(exp)}");
        }
        return 0;
    }

    // ── export ──  the raw learnt structure as JSON (world boundary → Python viz): each rule's RHS pattern
    // (the DAG edges, NOT the flattened expansion), the top-level compressed tape, and per-rule uses.
    // Depth, foveation (per-byte grammar depth), and rule co-activation are all derivable from these three.
    private static int Export(string[] args)
    {
        var corpus = LoadCorpus(args);
        var (_, n, r) = Engine.Induce(corpus);
        var uses = RuleUses(r);

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"corpusBytes\": ").Append(n).Append(",\n");
        sb.Append("  \"alphabetSize\": ").Append(r.AlphabetSize).Append(",\n");
        sb.Append("  \"firstNonterminal\": ").Append(Symbol.FirstNonterminal).Append(",\n");
        sb.Append("  \"totalSavingsMbits\": ").Append(r.TotalSavings.Value).Append(",\n");

        // rules[i] is the production for nonterminal (firstNonterminal + i); pattern = child symbol values
        // (a value < firstNonterminal is a terminal byte, ≥ is a nonterminal reference = a DAG edge).
        sb.Append("  \"rules\": [");
        for (int i = 0; i < r.Rules.Length; i++)
        {
            sb.Append(i > 0 ? ",\n    " : "\n    ");
            sb.Append("{\"nt\": ").Append(Symbol.FirstNonterminal + (uint)i);
            sb.Append(", \"uses\": ").Append(uses[i]);
            sb.Append(", \"pattern\": [");
            var p = r.Rules[i].Pattern;
            for (int j = 0; j < p.Length; j++) { if (j > 0) sb.Append(','); sb.Append(p[j].Value); }
            sb.Append("]}");
        }
        sb.Append("\n  ],\n");

        sb.Append("  \"tape\": [");
        for (int i = 0; i < r.Compressed.Length; i++) { if (i > 0) sb.Append(','); sb.Append(r.Compressed[i].Value); }
        sb.Append("]\n}\n");

        string outPath = args.Length > 2 ? args[2] : Run.HomePath("grammar.json");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"export · {n} bytes → {r.Rules.Length} rules, {r.Compressed.Length} tape symbols → {outPath}");
        return 0;
    }

    // ── couplings ──  the LEARNED PPMI coupling graph as JSON (world boundary → the topology viz). The REAL
    // engine (Couplings.cs, not the Python reimplementation): induce → learn co-activations over the compressed
    // CHUNK stream → the rich PPMI graph, and the CouplingGenerator's OWN MRF-relaxation walks as unit-id trails.
    // Nodes = coupled chunks (marginal freq + degree), edges = max-PPMI-over-distance (symmetrized a<b), walks =
    // the generator's composition blocks. Communities/pruning/layout are the consumer's job (Python-side Louvain).
    private static int DumpCouplings(string[] args)
    {
        var corpus = LoadCorpus(args);
        var (_, n, r) = Engine.Induce(corpus);
        var cp = global::Cogito.Couplings.Learn(r, global::Cogito.Couplings.DefaultWindow);           // W=3 — the fine chunk grain the couplings need (global:: — the `Couplings()` command-builder shadows the engine class)
        var rich = cp.BuildScorer(minCocount: 1);                      // the rich PPMI graph (broad reach — the edges we draw)
        var robust = cp.BuildScorer(minCocount: 5);                    // transfer-only graph — the generator's second energy
        var (vocab, _) = cp.Vocabulary();

        // edges: fold each unit's top-K forward couplings (already aggregated over distance in the scorer) into
        // one undirected a<b edge at the stronger φ — a→b and b→a collapse to their max-PPMI.
        var und = new Dictionary<(uint A, uint B), double>();
        foreach (var a in vocab)
            foreach (var (b, phi) in rich.Fwd(a))
            {
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                if (phi > und.GetValueOrDefault(key)) und[key] = phi;
            }
        var deg = new Dictionary<uint, int>();                         // distinct PPMI neighbours per node (the folded edge set)
        foreach (var (a, b) in und.Keys) { deg[a] = deg.GetValueOrDefault(a) + 1; deg[b] = deg.GetValueOrDefault(b) + 1; }

        // walks: the engine's OWN composition — CouplingGenerator MRF-relaxes short coherent unit blocks; the
        // traced unit ids (pre-byte-expansion) are the trail over the graph. Honest: single-node coupling walk
        // over code chunks (the Farm is single-node today — NOT a multi-domain walk).
        var walks = new CouplingGenerator(cp, rich, robust).GenerateTraced(6, 0x243F6A8885A308D3UL);

        var emit = new HashSet<uint>(deg.Keys);                        // emit coupled nodes ∪ every walk step (so trails land on real nodes)
        foreach (var w in walks) foreach (var u in w) emit.Add(u);

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"corpus\": \"").Append(JsonStr(Path.GetFileName(args.Length > 1 && File.Exists(args[1]) ? args[1] : "builtin"))).Append("\",\n");
        sb.Append("  \"corpusBytes\": ").Append(n).Append(",\n");
        sb.Append("  \"window\": ").Append(cp.Window).Append(",\n");

        sb.Append("  \"nodes\": [");
        bool first = true;
        foreach (var u in emit.OrderBy(x => x))
        {
            sb.Append(first ? "\n    " : ",\n    "); first = false;
            sb.Append("{\"id\": ").Append(u);
            sb.Append(", \"label\": \"").Append(JsonStr(Label(cp.Expand(u)))).Append('"');
            sb.Append(", \"freq\": ").Append(cp.Marginals.GetValueOrDefault(u));
            sb.Append(", \"deg\": ").Append(deg.GetValueOrDefault(u)).Append('}');
        }
        sb.Append("\n  ],\n");

        sb.Append("  \"edges\": [");
        first = true;
        foreach (var ((a, b), w) in und)
        {
            sb.Append(first ? "\n    " : ",\n    "); first = false;
            sb.Append("{\"a\": ").Append(a).Append(", \"b\": ").Append(b)
              .Append(", \"w\": ").Append(w.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        }
        sb.Append("\n  ],\n");

        sb.Append("  \"walks\": [");
        for (int i = 0; i < walks.Count; i++)
        {
            sb.Append(i > 0 ? ",\n    " : "\n    ").Append('[');
            for (int j = 0; j < walks[i].Length; j++) { if (j > 0) sb.Append(','); sb.Append(walks[i][j]); }
            sb.Append(']');
        }
        sb.Append("\n  ]\n}\n");

        string outPath = args.Length > 2 ? args[2] : Run.HomePath("couplings.json");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"couplings · {n} bytes → {emit.Count} coupled nodes ({vocab.Length} vocab) · {und.Count} PPMI edges · {walks.Count} walks · W={cp.Window} → {outPath}");
        return 0;
    }

    // ── log ──  the event-sourced substrate: every event's schema + content hash, the mutation packet.
    private static int DumpLog(byte[] corpus)
    {
        var (_, log, gve) = Engine.BuildLog(corpus);
        Console.WriteLine($"event log · {log.Count} events");
        for (var id = EventID.Zero; id.Value < (ulong)log.Count; id = id.Next)
        {
            var e = log[id];
            Console.WriteLine($"  #{id.Value,3}  schema={e.SchemaId.Value,3} v{e.Version}  {e.Payload.Length,5}B  H={log.HashOf(id)}");
        }
        if (gve is { } g)
            Console.WriteLine($"  → grammar v{g.Version}: +{g.RulesAdded.Count} rules, Δmdl={g.MdlDelta}, ref={g.SpecRef}");
        return 0;
    }

    // ── heaps ──  rule-growth diagnostic (the explosion/leak watch). Induce over increasing prefixes of a
    // corpus; fit Heaps' law (rules ∝ bytes^β). β<1 = sublinear (healthy generalization); β≥1 = the grammar
    // hoards instead of generalizing — an explosion / leak.
    private static int Heaps(byte[] corpus)
    {
        Console.WriteLine($"heaps · {corpus.Length} B — rule growth + structure significance");
        Console.WriteLine("     bytes  rules  rules/KB");
        var pts = new List<(double X, double Y)>();
        for (int p = 1; p <= 10; p++)
        {
            int len = corpus.Length * p / 10;
            if (len < 32) continue;
            var (_, n, r) = Engine.Induce(corpus[..len]);
            Console.WriteLine($"    {n,6}  {r.Rules.Length,5}  {(n == 0 ? 0 : r.Rules.Length * 1024.0 / n),7:F1}");
            if (n > 1 && r.Rules.Length > 1) pts.Add((Math.Log(n), Math.Log(r.Rules.Length)));
        }
        double beta = Slope(pts);

        // Contrastive structure test (a permutation test, not a magic threshold): does the data compress
        // significantly better than a byte-SHUFFLE of itself — same byte distribution, sequential order
        // destroyed? z = deviation of the real MDL savings above the shuffled null, in σ. z >> 3 = genuine
        // sequential structure; z ≈ 0 = the savings were only the byte-frequency, no order (noise-like).
        double real = Engine.Induce(corpus).Result.TotalSavings.Value;
        var nul = new List<double>();
        for (int k = 0; k < 8; k++) nul.Add(Engine.Induce(Engine.Shuffled(corpus, 0x9E3779B1UL + (ulong)k)).Result.TotalSavings.Value);
        double mean = nul.Average();
        double std = Math.Sqrt(nul.Select(x => (x - mean) * (x - mean)).Average());
        double z = std < 1.0 ? (real > mean + 1 ? 99.0 : 0.0) : (real - mean) / std;

        Console.WriteLine($"  structure z = {z:F1}σ  (real compression vs byte-shuffled null — a permutation significance test)");
        if (z < 3.0)
            Console.WriteLine($"  → INCOMPRESSIBLE: no significant sequential structure (noise-like); the Heaps β={beta:F2} is moot.");
        else
            Console.WriteLine($"  Heaps β = {beta:F3}  "
                + (beta < 0.9 ? "(SUBLINEAR — healthy generalization)"
                 : beta < 1.05 ? "(~linear — watch it)"
                 :               "(SUPER-LINEAR — explosion / leak!)"));
        return 0;
    }

    // ── know ──  structural self-awareness. cogito reads a probe and marks what it KNOWS (covered by its
    // grammar) vs what it DOESN'T (uncovered = its known-unknowns) — straight off the grammar, the SAME
    // structure it generates from. Unlike an LLM, whose "what I know" is a separate confabulated memo divorced
    // from generation, cogito's self-knowledge IS its capability.
    private static int Know(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("  usage: know <corpus> <probe-text-or-path>"); return 1; }
        var seed = LoadCorpus(args);
        byte[] probe = File.Exists(args[^1]) ? File.ReadAllBytes(args[^1]) : Encoding.UTF8.GetBytes(string.Join(' ', args[2..]));
        var (_, _, g) = Engine.Induce(seed);
        var mask = Engine.CoverMask(g.Rules, probe);
        int known = 0; for (int i = 0; i < mask.Length; i++) if (mask[i]) known++;

        var sb = new StringBuilder();
        bool unk = false;
        for (int i = 0; i < probe.Length; i++)
        {
            if (!mask[i] && !unk) { sb.Append('«'); unk = true; }
            else if (mask[i] && unk) { sb.Append('»'); unk = false; }
            sb.Append(probe[i] == (byte)'\n' || (probe[i] >= 32 && probe[i] < 127) ? (char)probe[i] : '·');
        }
        if (unk) sb.Append('»');

        Console.WriteLine($"know · grammar {g.Rules.Length} rules · probe {probe.Length} B");
        Console.WriteLine($"  cogito knows {(probe.Length == 0 ? 0 : 100.0 * known / probe.Length):F0}% of this. «marked» = its structural known-unknowns:");
        Console.WriteLine(sb.ToString());
        return 0;
    }

    // ── interp ──  mechanistic interpretability. The grammar IS cogito's weights; read its internals — the
    // COMPOSITION DEPTH of each rule (the abstraction levels it built, terminals → deep compositions), the
    // LOAD-BEARING concepts (most-referenced rules = the core vocabulary), and the deepest abstractions. Reads
    // a learned grammar the way you'd read a net's features: what concepts did it form, and how do they stack?
    private static int Interp(byte[] corpus)
    {
        var (_, n, r) = Engine.Induce(corpus);
        if (r.Rules.Length == 0) { Console.Error.WriteLine("  grammar too small to interpret"); return 1; }
        int nr = r.Rules.Length;
        var depth = new int[nr];                                            // composition depth: 1 = terminals only, higher = nests rules
        for (int i = 0; i < nr; i++)
        {
            int d = 0;
            foreach (var sym in r.Rules[i].Pattern)
                if (sym.Value >= Symbol.FirstNonterminal) { int j = (int)(sym.Value - Symbol.FirstNonterminal); if (j < i) d = Math.Max(d, depth[j]); }
            depth[i] = d + 1;
        }
        var uses = RuleUses(r);
        int maxDepth = depth.Max();
        Console.WriteLine($"interp · {n} bytes → {nr} rules · mechanistic read of the grammar's internals");
        Console.WriteLine("  abstraction ladder (rules by composition depth — the levels cogito built):");
        for (int d = 1; d <= maxDepth; d++)
        {
            var atD = Enumerable.Range(0, nr).Where(i => depth[i] == d).ToList();
            if (atD.Count == 0) continue;
            var ex = atD.OrderByDescending(i => uses[i]).Take(2).Select(i => $"\"{ShowRaw(Expand(r, i))}\"");
            Console.WriteLine($"    depth {d,2}:  {atD.Count,4} rules   e.g. {string.Join(", ", ex)}");
        }
        Console.WriteLine("  load-bearing concepts (most-referenced — the core vocabulary):");
        foreach (var i in Enumerable.Range(0, nr).OrderByDescending(i => uses[i]).Take(6))
            Console.WriteLine($"    \"{ShowRaw(Expand(r, i))}\"  ×{uses[i]} (depth {depth[i]})");
        Console.WriteLine("  deepest abstractions (most-composed):");
        foreach (var i in Enumerable.Range(0, nr).OrderByDescending(i => depth[i]).ThenByDescending(i => uses[i]).Take(4))
            Console.WriteLine($"    \"{Truncate(ShowRaw(Expand(r, i)), 60)}\"  depth {depth[i]}");
        return 0;
    }

    // ── renorm ──  the RENORMALIZATION-GROUP probe. cogito's grammar induction IS an RG flow: each Re-Pair merge
    // coarse-grains a frequent adjacent pair into a macro-symbol (a block-spin transformation), the MDL gate is
    // the relevance filter (a coarse-graining survives iff its macro-DOF pays its bits), rule DEPTH is the RG
    // scale, and Zipf ≈ -1 is the scale-invariance signature of a critical fixed point. This stratifies the
    // grammar by abstraction scale and tests whether the per-scale statistics are SELF-SIMILAR — the fingerprint
    // of the transcendental fixed-point abstraction (the same structure at every level of coarse-graining).
    private static int Renorm(byte[] corpus)
    {
        var (_, n, r) = Engine.Induce(corpus);
        int nr = r.Rules.Length;
        if (nr == 0) { Console.Error.WriteLine("  grammar too small to renormalize"); return 1; }
        var depth = new int[nr];                                            // composition depth = the RG scale (coarse-graining steps)
        for (int i = 0; i < nr; i++)
        {
            int d = 0;
            foreach (var sym in r.Rules[i].Pattern)
                if (sym.Value >= Symbol.FirstNonterminal) { int j = (int)(sym.Value - Symbol.FirstNonterminal); if (j < i) d = Math.Max(d, depth[j]); }
            depth[i] = d + 1;
        }
        var uses = RuleUses(r);
        var span = new int[nr];                                             // expansion length = correlation length (the abstraction's physical extent), memoized bottom-up
        for (int i = 0; i < nr; i++)
        {
            int s = 0;
            foreach (var sym in r.Rules[i].Pattern)
                s += sym.Value >= Symbol.FirstNonterminal && (int)(sym.Value - Symbol.FirstNonterminal) < i ? span[(int)(sym.Value - Symbol.FirstNonterminal)] : 1;
            span[i] = s;
        }
        int maxL = depth.Max();
        Console.WriteLine($"renorm · RG flow over {nr} rules · {maxL} abstraction scales · corpus {n}B");
        Console.WriteLine($"  each scale coarse-grains the one below; a CONSTANT criticality exponent across scales ⟹ a self-similar fixed point");
        Console.WriteLine($"  scale   rules   n_L/n_(L-1)   Zipf(usage)   ⟨span⟩B   Σusage");
        var zipfsByScale = new List<double>();
        var spansByScale = new List<double>();
        int prevN = 0;
        for (int L = 1; L <= maxL; L++)
        {
            var idx = Enumerable.Range(0, nr).Where(i => depth[i] == L).ToList();
            if (idx.Count == 0) continue;
            double ratio = prevN == 0 ? double.NaN : (double)idx.Count / prevN;
            double zipf = ZipfOf(idx.Select(i => uses[i]));
            double spanL = idx.Average(i => (double)span[i]);
            long sumUse = idx.Sum(i => (long)uses[i]);
            Console.WriteLine($"  {L,5}   {idx.Count,5}   {(double.IsNaN(ratio) ? "    —" : ratio.ToString("F2")),11}   {(double.IsNaN(zipf) ? "  n/a" : zipf.ToString("F2")),11}   {spanL,7:F1}   {sumUse,6}");
            if (!double.IsNaN(zipf) && idx.Count >= 4) zipfsByScale.Add(zipf);   // only well-populated scales carry an exponent
            spansByScale.Add(spanL);
            prevN = idx.Count;
        }
        // THE fixed-point read: scale-invariance of the criticality exponent (the per-scale Zipf slope).
        // Constant exponent across scales = the grammar looks the same at every level of coarse-graining = a critical RG fixed point.
        double meanZ = zipfsByScale.Count > 0 ? zipfsByScale.Average() : double.NaN;
        double cvZ = zipfsByScale.Count > 1 ? Math.Sqrt(zipfsByScale.Sum(x => (x - meanZ) * (x - meanZ)) / zipfsByScale.Count) / Math.Abs(meanZ) : double.NaN;
        double spanGrowth = spansByScale.Count > 1 && spansByScale[0] > 0 ? Math.Pow(spansByScale[^1] / spansByScale[0], 1.0 / (spansByScale.Count - 1)) : double.NaN;
        Console.WriteLine($"  ── fixed-point read ──");
        Console.WriteLine($"  criticality exponent over {zipfsByScale.Count} populated scales: Zipf = {meanZ:F2} ± {(double.IsNaN(cvZ) ? "n/a" : cvZ.ToString("P0"))}");
        Console.WriteLine($"    {(cvZ < 0.20 ? "→ SCALE-INVARIANT: the SAME power-law at every scale — a critical RG fixed point (the transcendental object)" : "→ CHARACTERISTIC SCALE: the exponent drifts — abstraction is not self-similar (the flow hasn't reached criticality)")}");
        Console.WriteLine($"  correlation length ⟨span⟩: {spansByScale[0]:F1}B → {spansByScale[^1]:F1}B  ·  ×{(double.IsNaN(spanGrowth) ? double.NaN : spanGrowth):F2}/scale (the abstraction's extent growing under coarse-graining)");
        return 0;
    }

    // ── grok ──  the GROKKING probe: watch the RG fixed point CRYSTALLIZE as data grows. Induce over increasing
    // prefixes; track abstraction DEPTH (scales), the criticality exponent's scale-invariance (CV), the correlation
    // length, and held-out generalization. Grokking = the DEEP structure (scales / scale-invariance / held-out)
    // emerging SUDDENLY at a critical data size while the surface (rule count) grows smoothly — delayed abstraction.
    private static int Grok(byte[] corpus)
    {
        if (corpus.Length < 800) { Console.Error.WriteLine("  corpus too small to watch grokking"); return 1; }
        int holdStart = corpus.Length * 80 / 100;
        var heldOut = corpus[holdStart..];
        var trainable = corpus[..holdStart];
        int[] fracs = [8, 15, 25, 40, 60, 80, 100];
        Console.WriteLine($"grok · abstraction emergence over growing data · train ≤{trainable.Length}B + held-out {heldOut.Length}B");
        Console.WriteLine($"  GROKKING = deep structure (scales / scale-invariance / held-out) emerging SUDDENLY while rules grow smoothly");
        Console.WriteLine($"  data%   bytes   rules   scales   exponent±CV     corrLen   held-out");
        foreach (int f in fracs)
        {
            int len = Math.Max(1, trainable.Length * f / 100);
            var (_, n, r) = Engine.Induce(trainable[..len]);
            var (scales, meanZ, cvZ, maxSpan) = RenormStats(r);
            double ho = Engine.CoverageOf(r.Rules, heldOut);
            string exp = double.IsNaN(meanZ) ? "   n/a" : $"{meanZ,5:F2}±{(double.IsNaN(cvZ) ? "n/a" : cvZ.ToString("P0")),4}";
            Console.WriteLine($"  {f,4}%   {n,6}   {r.Rules.Length,5}   {scales,5}   {exp,11}   {maxSpan,7:F0}   {ho,7:P0}");
        }
        Console.WriteLine($"  read: a sudden jump in SCALES / drop in CV (the exponent locking to one value) = the fixed point crystallizing — grokking the abstraction.");
        return 0;
    }

    // ── thoughtstream ──  RE-PAIR INDUCTION AS AN OBSERVABLE THOUGHT STREAM. Induce over the corpus with the
    // per-merge event seam ARMED (each merge is a decision), encode the merge-events into thought-tokens, then
    // RE-INDUCE over that stream — cogito grammar-modeling its OWN THINKING (the strange-loop substrate that feeds
    // dream-of-cognition + the tower + the self-model's food). Prints both grammars' headline renorm stats + the
    // meta-compression ratio: does cogito's cognition itself compress (recurring routines), and is its thinking
    // scale-invariant? Deterministic — same corpus ⇒ same thought stream ⇒ same meta-grammar (the Vow). This is
    // the trace-loop thought-stream, NOT the `dream` code-loop.
    private static int MergeTraceRun(byte[] corpus)
    {
        var (_, n, g, events) = Engine.InduceTraced(corpus);
        if (events.Count < 4) { Console.Error.WriteLine("  corpus too small — too few merges to think about"); return 1; }

        // ── the base cognition: the grammar over the corpus (context for the thinking about to be modeled) ──
        var (scales, meanZ, cvZ, maxSpan) = RenormStats(g);
        double codeRatio = g.Compressed.Length > 0 ? (double)n / g.Compressed.Length : 1.0;
        Console.WriteLine($"thoughtstream · induction as observable cognition · corpus {n}B → {events.Count} merge-events (the thought stream)");
        Console.WriteLine($"  base grammar:    {g.Rules.Length,5} rules · {scales,2} scales · exponent {ExpFmt(meanZ, cvZ),11} · corrLen {maxSpan,5:F0}B · compresses {codeRatio:F2}×");
        var gUses = RuleUses(g);
        Console.Write("    top concepts:  ");
        foreach (var i in Enumerable.Range(0, g.Rules.Length).OrderByDescending(i => gUses[i]).Take(4))
            Console.Write($"{Show(Expand(g, i), 18)}×{gUses[i]}  ");
        Console.WriteLine();

        // ── E1: the thought stream (climb·salience per merge) re-induced as its own grammar ──
        var toks = global::Cogito.MergeTrace.EncodeEvents(events, g.AlphabetSize);
        int distinct = toks.Distinct().Count();
        var tape = global::Cogito.MergeTrace.ToTape(toks, out var vocab);
        var meta = new RePair().Induce(tape, Mbits.Zero, (uint)vocab.Length);
        var (mScales, mMeanZ, mCvZ, mMaxSpan) = RenormStats(meta);
        double metaRatio = meta.Compressed.Length > 0 ? (double)toks.Length / meta.Compressed.Length : 1.0;
        Console.WriteLine($"  thought grammar: {meta.Rules.Length,5} rules · {mScales,2} scales · exponent {ExpFmt(mMeanZ, mCvZ),11} · corrLen {mMaxSpan,5:F0} · META-COMPRESSION {metaRatio:F2}× over {distinct} distinct thought-tokens");

        if (meta.Rules.Length > 0)
        {
            var mUses = RuleUses(meta);
            Console.WriteLine("    recurring thought-routines (how cogito repeatedly thinks — meta-rules by load):");
            foreach (var i in Enumerable.Range(0, meta.Rules.Length).OrderByDescending(i => mUses[i]).Take(6))
                Console.WriteLine($"      ×{mUses[i],-4} {(global::Cogito.MergeTrace.Render(meta, i, vocab))}");
        }

        // ── E2: the rule-SHAPE stream — a second facet of self-modeling (what KINDS of rules cogito builds) ──
        var shapeToks = global::Cogito.MergeTrace.EncodeRuleset(g);
        var shapeTape = global::Cogito.MergeTrace.ToTape(shapeToks, out var shapeVocab);
        var shapeMeta = new RePair().Induce(shapeTape, Mbits.Zero, (uint)Math.Max(1, shapeVocab.Length));
        double shapeRatio = shapeMeta.Compressed.Length > 0 ? (double)shapeToks.Length / shapeMeta.Compressed.Length : 1.0;
        Console.WriteLine($"  rule-shape grammar: {shapeMeta.Rules.Length} rules over {shapeVocab.Length} distinct shapes · compresses {shapeRatio:F2}× (the recurring shapes of rule it builds)");

        // ── the read ──
        Console.WriteLine("  ── read ──");
        Console.WriteLine($"  META-COMPRESSION {metaRatio:F2}× {(metaRatio > 1.15 ? "→ cogito's THINKING has recurring routines — its cognition is itself STRUCTURED, not merge-noise" : "→ the thought stream looks unstructured (each merge nearly novel)")}");
        if (!double.IsNaN(mCvZ))
            Console.WriteLine($"  the thinking's OWN exponent CV = {mCvZ:P0} {(mCvZ < 0.20 ? "→ SCALE-INVARIANT cognition (the strange loop reaches a critical fixed point)" : "→ characteristic scale — the thinking is not self-similar across levels")}");
        Console.WriteLine("  → the self-model cluster (dream-of-cognition · tower · self-model food) can now consume this merge-event stream.");
        return 0;
    }

    // ── tokrenorm ──  TOKEN-resolution renorm. Reads a uint32 token-ID stream (an LLM tokenizer's output — the
    // sub-word "blur" over bytes) and induces at TOKEN resolution, then reports the scale-structure. THE question:
    // does a domain reach the critical scale-invariant fixed point at TOKEN resolution that it missed at BYTE
    // resolution? — the blur letting it see the ambience first (progressive resolution, the training-wheels idea).
    private static int TokRenorm(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1])) { Console.Error.WriteLine("  usage: tokrenorm <uint32-token-file>"); return 1; }
        var bytes = File.ReadAllBytes(args[1]);
        var tokens = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, tokens, 0, tokens.Length * 4);
        if (tokens.Length < 8) { Console.Error.WriteLine("  too few tokens"); return 1; }
        var (n, r) = Engine.InduceTokens(tokens);
        var (scales, meanZ, cvZ, maxSpan) = RenormStats(r);
        Console.WriteLine($"tokrenorm · {n} tokens · alphabet {r.AlphabetSize} distinct · {r.Rules.Length} rules · {scales} abstraction scales");
        Console.WriteLine($"  criticality exponent: Zipf = {(double.IsNaN(meanZ) ? double.NaN : meanZ):F2} ± {(double.IsNaN(cvZ) ? "n/a" : cvZ.ToString("P0"))}");
        Console.WriteLine($"    {(cvZ < 0.20 ? "→ SCALE-INVARIANT at token resolution — the blur let it grok (a critical fixed point)" : "→ CHARACTERISTIC SCALE even at token resolution")}");
        Console.WriteLine($"  max correlation length {maxSpan:F0} tokens");
        return 0;
    }

    // ── overlap ──  grammar similarity. Induce two domains' grammars and measure the JACCARD of their learned
    // concepts (rule expansions) — the cross-domain invariants they BOTH discovered, quantified. High overlap =
    // similar domains (confusable by the classifier); the shared vocabulary IS the universal structure.
    private static int Overlap(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1]) || !File.Exists(args[2]))
        { Console.Error.WriteLine("  usage: overlap <corpusA> <corpusB>"); return 1; }
        var (_, _, ga) = Engine.Induce(File.ReadAllBytes(args[1]));
        var (_, _, gb) = Engine.Induce(File.ReadAllBytes(args[2]));
        var ea = Enumerable.Range(0, ga.Rules.Length).Select(i => Encoding.UTF8.GetString(Expand(ga, i))).ToHashSet();
        var eb = Enumerable.Range(0, gb.Rules.Length).Select(i => Encoding.UTF8.GetString(Expand(gb, i))).ToHashSet();
        var shared = ea.Where(eb.Contains).ToList();
        int union = ea.Count + eb.Count - shared.Count;
        double jac = union == 0 ? 0 : (double)shared.Count / union;
        Console.WriteLine($"overlap · {System.IO.Path.GetFileName(args[1])} ({ea.Count} concepts) vs {System.IO.Path.GetFileName(args[2])} ({eb.Count} concepts)");
        Console.WriteLine($"  shared concepts: {shared.Count}  ·  Jaccard similarity: {jac:P1}  (the cross-domain invariants both grammars learned)");
        Console.WriteLine("  the shared vocabulary (longest first — the universal structure):");
        foreach (var s in shared.OrderByDescending(s => s.Length).Take(10))
            Console.WriteLine($"    \"{Truncate(s, 50)}\"");
        return 0;
    }

    // ── verify-induct ──  the O(Δ) correctness gate: the linear Induce MUST be byte-identical to the O(n·rules)
    // reference on every case — same rules (id + pattern, in order), same compressed tape, same Δmdl. Runs the
    // synthetic self-overlap danger-zone (aaaa/ababab/mixed runs — where an incremental count can drift) + random
    // small-alphabet corpora (many merges) + the real corpus. A single divergence fails red; determinism rests here.
    private static int VerifyInduct(string[] args)
    {
        var cases = InductCases(args);
        var tok = Cogito.Observe.ByteTokenizer.Instance;
        int fails = 0;
        Console.WriteLine("verify-induct · linear Induce vs O(n·rules) reference — must be BYTE-IDENTICAL (barrier off AND armed)");
        foreach (var (name, data) in cases)
        {
            var tape = new Symbol[tok.MaxSymbols(data.Length)];
            int nn = tok.Tokenize(data, tape);
            var span = tape.AsSpan(0, nn);
            var fast = new RePair().Induce(span, Mbits.Zero);
            var reff = new RePair().InduceReference(span, Mbits.Zero);
            var fastB = new RePair().Induce(span, Mbits.Zero, barrier: '\n');
            var reffB = new RePair().InduceReference(span, Mbits.Zero, barrier: '\n');
            // the barrier LAW itself, checked directly: no armed rule's expansion may contain the barrier byte
            bool clean = true;
            for (int i = 0; i < fastB.Rules.Length && clean; i++)
                if (Expand(fastB, i).AsSpan().Contains((byte)'\n')) clean = false;
            bool ok = SameGrammar(fast, reff) && SameGrammar(fastB, reffB) && clean;
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "✓" : "✗ DIVERGENCE")}  {name,-8} {data.Length,7}B · rules {fast.Rules.Length}/{reff.Rules.Length} · barrier rules {fastB.Rules.Length}/{reffB.Rules.Length}{(clean ? "" : " · STRADDLER LEAKED")} · Δmdl {fast.TotalSavings.Value}/{reff.TotalSavings.Value}");
        }
        Console.WriteLine(fails == 0
            ? "✓ verify-induct PASSED — the linear path reproduces the reference grammar exactly (determinism preserved), barrier off and armed; no rule straddles '\\n'."
            : $"✗ verify-induct FAILED — {fails} divergence(s); the O(Δ) path is NOT byte-identical (or a straddler leaked past the barrier).");
        return fails == 0 ? 0 : 1;
    }

    // ── verify-loom ──  THE LOOM's differential gate (O(Δ) incremental induction, phase 1) — three arms per case:
    //   BATCH-IDENTITY  Engine's batch entries now run on a fresh Loom (splice-all + pump); they must reproduce
    //                   the linear RePair oracle BYTE-FOR-BYTE (the Vow anchor — this arm proves the whole
    //                   machine's un-armed path didn't move).
    //   INCREMENTAL     a Tape grown span-batch by span-batch, spliced+pumped per batch (the trunk's shape):
    //                   reconstruction must be EXACT at every growth step (grammar + compressed expand to the
    //                   reference Concat), the incrementally-held usage counters must equal Engine.RuleUses
    //                   of the harvest, and the MDL gap vs the batch oracle over the same final bytes — the
    //                   greedy-in-arrival tax, living in the Zipf tail near the mint bar by design — is
    //                   reported and BOUNDED (≤ +20% description length), never required to be zero.
    //   RESUME          the loom checkpointed mid-growth, restored into a fresh loom over a replayed tape, both
    //                   continued identically — final grammars BYTE-IDENTICAL and Save∘Load∘Save = identity
    //.
    // --bench: the O(Δ) payoff readout — per-round induce wall on a growing tape, loom (splice+pump | emit,
    // decomposed) vs batch (concat+tokenize+induce); the loom column must flatline while the batch column climbs.
    private static int VerifyLoom(string[] args)
    {
        if (Args.Has(args, "--bench")) return LoomBench(args);
        var cases = InductCases(args);
        var tok = Cogito.Observe.ByteTokenizer.Instance;
        int fails = 0;
        Console.WriteLine("verify-loom · persistent splice+pump vs the batch oracle — batch BYTE-IDENTICAL · incremental reconstruction EXACT · resume BYTE-IDENTICAL · MDL gap ≤ +20% · compact+shed lossless");
        bool ActivePlanes(Loom loom) => loom.ValidateActivePlanes();
        bool heapFixture = Loom.ValidateIndexedHeapFixture();
        if (!heapFixture) fails++;
        Console.WriteLine($"  {(heapFixture ? "✓" : "✗ DIVERGENCE")}  indexed-heap · one node/key · root/middle/leaf repairs · remove · tie-key order");
        bool arenaBucketFixture;
        using (var crossing = new Loom())
        {
            crossing.SpliceEvent(new byte[5000], 1, 1);
            crossing.SpliceEvent(new byte[1000], 2, 1); // ArrayPool may over-rent the first arena; the reverse index must grow independently.
            arenaBucketFixture = crossing.ValidateActivePlanes();
        }
        if (!arenaBucketFixture) fails++;
        Console.WriteLine($"  {(arenaBucketFixture ? "✓" : "✗ DIVERGENCE")}  arena-bucket-crossing · reverse occurrence index follows rented arena capacity");

        // A high-frequency winner rewrites thousands of postings but has only one touched heap key: the winner
        // itself. This arm is the transaction receipt for the pump's O(touched-keys) heap maintenance, and compares
        // the resulting grammar/view against the fresh batch barrier-separated oracle so the optimization cannot alter
        // winner order or bytes.
        bool highFrequencyPump = true;
        long highFrequencySites = 2048;
        using (var highTape = new Tape())
        using (var high = new Loom())
        {
            for (long i = 0; i < highFrequencySites; i++) highTape.Append("ab"u8.ToArray(), "loom-heap");
            high.SpliceNew(highTape);
            LoomMutationReceipt pumpReceipt = high.Pump();
            RePairResult actual = high.Result(highTape);
            RePairResult expected = Engine.Induce(highTape, 1).Result;
            highFrequencyPump = SameGrammar(actual, expected)
                && actual.Compressed.AsSpan().SequenceEqual(expected.Compressed)
                && high.ValidateActivePlanes()
                && pumpReceipt.MintedRules == 1
                && pumpReceipt.HeapChangedKeys == 1
                && pumpReceipt.HeapMutations <= pumpReceipt.HeapChangedKeys + 1
                && pumpReceipt.HeapMutations < highFrequencySites;
            if (!highFrequencyPump) fails++;
            Console.WriteLine($"  {(highFrequencyPump ? "✓" : "✗ DIVERGENCE")}  high-frequency-pump · {highFrequencySites} sites → heap mutations={pumpReceipt.HeapMutations} touched-keys={pumpReceipt.HeapChangedKeys} · grammar/view exact");
        }

        // ── arm 0 · SELF-COMPRESS on SYNTHETIC duplicates ──  fresh Re-Pair output is expansion-duplicate-free by
        // construction (the global left-to-right rewrite is a normal form — the whole battery below measures 0
        // merges), so the MERGE move is proven on a hand-built grammar: R2=(R0,c) and R3=(a,R1) both expand "abc".
        // Compact must collapse them to the lowest-index canonical, alias the dead digram, keep reconstruction
        // byte-exact, and a loom rebased on the compacted grammar must parse re-admissionPlans into the canonical.
        {
            var a = new Symbol('a'); var b = new Symbol('b'); var c = new Symbol('c');
            Symbol N(int i) => new(Symbol.FirstNonterminal + (uint)i);
            GrammarRule Rule(Symbol x, Symbol y) { var p = new Symbol[] { x, y }; return new GrammarRule(GrammarRule.ComputeId(p), p, new Cogito.Mbits(256 + 8000L * 16)); }
            var rules = new[] { Rule(a, b), Rule(b, c), Rule(N(0), c), Rule(a, N(1)) };   // R2 exp "abc" ≡ R3 exp "abc"
            var comp = new[] { N(2), new Symbol('\n'), N(3), new Symbol('\n'), N(2), new Symbol('\n'), N(3), new Symbol('\n') };
            var dup = new Cogito.Induct.RePairResult(rules, comp, Cogito.Mbits.Zero, 256);
            var scs = Cogito.Induct.SelfCompress.Compact(dup);
            var expDup = Reconstruct.Expand(dup.Rules, dup.Compressed);
            bool synOk = scs.Merged == 1 && scs.Aliases.Count == 1 && scs.Grammar.Rules.Length == 3
                      && Reconstruct.Expand(scs.Grammar.Rules, scs.Grammar.Compressed).AsSpan().SequenceEqual(expDup);
            using (var syn = new Loom())
            {
                var stape = new Tape();
                foreach (var _ in Enumerable.Range(0, 4)) stape.Append("abc"u8.ToArray(), "syn");
                syn.Rebase(stape, scs.Grammar, scs.Aliases);
                stape.Append("abc"u8.ToArray(), "syn");                     // the re-admissionPlan — must parse into the CANONICAL, minting nothing
                syn.SpliceNew(stape); syn.Pump();
                var res = syn.Result(stape);
                synOk &= res.Rules.Length == 3 && Reconstruct.Expand(res.Rules, res.Compressed).AsSpan().SequenceEqual(stape.Concat());
            }
            if (!synOk) fails++;
            Console.WriteLine($"  {(synOk ? "✓" : "✗ DIVERGENCE")}  synth-dup     · compact merged 1 → 3 rules · alias re-encounter mints 0 · recon exact");
        }

        // ── arm 6 · TAPE DELTA MUTATIONS ──  exercise the exact Tape.DrainDelta receipt once per verifier run:
        // append, reflect, drop, and shed all mutate one standing Loom without a Resplice. A fresh Loom replays
        // the current standing rules after every transition; expansion, compressed symbols, and usage counters agree.
        bool deltaOk = true;
        long deltaHeapMutations = 0; int deltaHeapChangedKeys = 0;
        using var deltaTape = new Tape();
        using var deltaLoom = new Loom();
        bool batchArmExact = true;
        long batchHeapMutations = 0; int batchHeapChangedKeys = 0;
        bool thresholdArmExact = true;
        long thresholdHeapMutations = 0;
        using (var immediateTape = new Tape())
        using (var batchedTape = new Tape())
        using (var immediate = new Loom(256, '\n', 8))
        using (var batched = new Loom(256, '\n', 8))
        {
            foreach (var (text, provenance) in new[]
            {
                ("aaaa", Provenances.Real), ("abab", Provenances.Real), ("bbbb", Provenances.Replay),
                ("aa", Provenances.Real), ("abab", Provenances.Real), ("aaaa", Provenances.Replay),
            })
            {
                immediateTape.Append(Encoding.UTF8.GetBytes(text), "loom-delta", provenance);
                batchedTape.Append(Encoding.UTF8.GetBytes(text), "loom-delta", provenance);
            }
            foreach (TapeEventID id in immediateTape.ResidentEventIDs)
            {
                immediateTape.Resolve(id, out byte[] bytes);
                immediate.SpliceEvent(bytes, id.Value, immediateTape.IsEvidence(id) ? (byte)8 : (byte)1);
            }
            LoomMutationReceipt batchReceipt = batched.ApplyTapeDelta(batchedTape, batchedTape.DrainDelta());
            batchHeapMutations = batchReceipt.HeapMutations;
            batchHeapChangedKeys = batchReceipt.HeapChangedKeys;
            immediate.Pump(); batched.Pump();
            RePairResult immediateResult = immediate.Result(immediateTape);
            RePairResult batchedResult = batched.Result(batchedTape);
            batchArmExact = SameGrammar(immediateResult, batchedResult)
                         && immediateResult.Compressed.AsSpan().SequenceEqual(batchedResult.Compressed)
                         && batchReceipt.HeapMutations <= batchReceipt.HeapChangedKeys
                         && ActivePlanes(immediate) && ActivePlanes(batched);
        }
        using (var thresholdTape = new Tape())
        using (var thresholdLoom = new Loom(256, '\n', 8))
        using (var thresholdExpected = new Loom(256, '\n', 8))
        {
            thresholdTape.Append("xy"u8.ToArray(), "loom-threshold", Provenances.Real);
            LoomMutationReceipt coldReceipt = thresholdLoom.ApplyTapeDelta(thresholdTape, thresholdTape.DrainDelta());
            thresholdArmExact &= coldReceipt.HeapMutations == 0 && coldReceipt.HeapChangedKeys > 0;
            thresholdLoom.Pump();
            thresholdTape.Append("xy"u8.ToArray(), "loom-threshold", Provenances.Real);
            thresholdTape.Append("xy"u8.ToArray(), "loom-threshold", Provenances.Real);
            LoomMutationReceipt hotReceipt = thresholdLoom.ApplyTapeDelta(thresholdTape, thresholdTape.DrainDelta());
            thresholdHeapMutations = hotReceipt.HeapMutations;
            thresholdArmExact &= hotReceipt.HeapMutations > 0 && hotReceipt.HeapChangedKeys > 0;
            thresholdLoom.Pump();
            foreach (TapeEventID id in thresholdTape.ResidentEventIDs)
            {
                thresholdTape.Resolve(id, out byte[] bytes);
                thresholdExpected.SpliceEvent(bytes, id.Value, (byte)8);
            }
            thresholdExpected.Pump();
            thresholdArmExact &= SameGrammar(thresholdExpected.Result(thresholdTape), thresholdLoom.Result(thresholdTape))
                              && ActivePlanes(thresholdLoom) && ActivePlanes(thresholdExpected);
        }
        bool failurePrefixExact = true;
        using (var failureTape = new Tape())
        using (var failureBatch = new Loom())
        using (var failureImmediate = new Loom())
        {
            TapeEventID valid = failureTape.Append("failure-prefix"u8.ToArray(), "loom-failure", Provenances.Real);
            bool threw = false;
            try
            {
                failureBatch.ApplyTapeDelta(failureTape, new TapeDelta(
                    new TapeRevision(1), TapeRevision.Initial,
                    [valid, new TapeEventID(999)], [], [], []));
            }
            catch (InvalidOperationException) { threw = true; }
            failureTape.Resolve(valid, out byte[] bytes);
            failureImmediate.SpliceEvent(bytes, valid.Value, weight: 1);
            failureBatch.Pump(); failureImmediate.Pump();
            failurePrefixExact = threw && SameGrammar(failureBatch.Result(failureTape), failureImmediate.Result(failureTape))
                              && ActivePlanes(failureBatch) && ActivePlanes(failureImmediate);
        }
        deltaOk &= batchArmExact && thresholdArmExact && failurePrefixExact;
        bool DeltaMatches()
        {
            var current = deltaLoom.Result(deltaTape);
            bool okDelta = ActivePlanes(deltaLoom)
                        && Reconstruct.Expand(current.Rules, current.Compressed).AsSpan().SequenceEqual(deltaTape.Concat());
            using var fresh = new Loom();
            fresh.Rebase(deltaTape, current);
            var rebuilt = fresh.Result(deltaTape);
            okDelta &= ActivePlanes(fresh) && SameGrammar(rebuilt, current);
            var usesDelta = Engine.RuleUses(current);
            for (int i = 0; i < usesDelta.Length && okDelta; i++) okDelta &= usesDelta[i] == deltaLoom.Uses[i];
            return okDelta;
        }

        // Checkpoint replay is deliberately a different path from the live
        // TapeDelta verb: new spans land through the pre-mutation rank
        // program, then emitted winner entries rewrite only their occurrence
        // postings. Keep this fixture beside the live-delta arm so a change to
        // either ordering cannot silently fall back to a tape-wide resplice.
        bool checkpointReplayExact = true;
        using (var checkpointTape = new Tape())
        using (var checkpointLoom = new Loom())
        {
            foreach (string text in new[] { "aaaa", "abab", "bbbb", "aaaa" }) checkpointTape.Append(Encoding.UTF8.GetBytes(text), "loom-checkpoint");
            checkpointLoom.ApplyTapeDelta(checkpointTape, checkpointTape.DrainDelta());
            checkpointLoom.Pump();
            checkpointTape.CommitCheckpointDelta();
            checkpointLoom.CommitCheckpointDelta();

            byte[] baseTapeImage;
            using (var stream = new MemoryStream())
            {
                using (var writer = new CkptWriter(stream)) checkpointTape.Save(writer);
                baseTapeImage = stream.ToArray();
            }
            byte[] baseLoomImage;
            using (var stream = new MemoryStream())
            {
                using (var writer = new CkptWriter(stream)) checkpointLoom.Save(writer);
                baseLoomImage = stream.ToArray();
            }

            using var resumedTape = new Tape();
            using (var reader = new CkptReader(new MemoryStream(baseTapeImage))) resumedTape.Load(reader);
            using var resumedLoom = new Loom();
            using (var reader = new CkptReader(new MemoryStream(baseLoomImage))) resumedLoom.Load(reader, resumedTape);

            checkpointTape.Append(Encoding.UTF8.GetBytes("aaaa"), "loom-checkpoint");
            TapeCheckpointDelta tapeDelta = checkpointTape.CaptureCheckpointDelta();
            checkpointLoom.ApplyTapeDelta(checkpointTape, tapeDelta.Mutation);
            checkpointLoom.Pump();
            LoomCheckpointDelta loomDelta = checkpointLoom.CaptureCheckpointDelta();
            resumedTape.ApplyCheckpointDelta(in tapeDelta);
            TapeDelta replayMutation = tapeDelta.Mutation;
            resumedLoom.ApplyTapeDeltaForCheckpoint(resumedTape, in replayMutation);
            resumedLoom.ApplyCheckpointDelta(in loomDelta, applyArenaEntries: true);
            bool checkpointMutationExact = SameGrammar(checkpointLoom.Result(checkpointTape), resumedLoom.Result(resumedTape))
                && checkpointLoom.Result(checkpointTape).Compressed.AsSpan().SequenceEqual(resumedLoom.Result(resumedTape).Compressed)
                && ActivePlanes(resumedLoom);

            using var resetResumeTape = new Tape();
            using (var reader = new CkptReader(new MemoryStream(baseTapeImage))) resetResumeTape.Load(reader);
            using var resetResumeLoom = new Loom();
            using (var reader = new CkptReader(new MemoryStream(baseLoomImage))) resetResumeLoom.Load(reader, resetResumeTape);
            using var resetSourceLoom = new Loom();
            using var resetSourceTape = new Tape();
            using (var reader = new CkptReader(new MemoryStream(baseTapeImage))) resetSourceTape.Load(reader);
            using (var reader = new CkptReader(new MemoryStream(baseLoomImage))) resetSourceLoom.Load(reader, resetSourceTape);
            RePairResult resetGrammar = Engine.Induce(resetSourceTape, 1).Result;
            resetSourceLoom.Rebase(resetSourceTape, resetGrammar);
            LoomCheckpointDelta resetDelta = resetSourceLoom.CaptureCheckpointDelta();
            resetResumeLoom.ApplyCheckpointDelta(in resetDelta);
            resetResumeLoom.RebuildFromTape(resetResumeTape, resetResumeLoom.SpliceIDMark);
            bool checkpointResetExact = SameGrammar(resetResumeLoom.Result(resetResumeTape), resetGrammar)
                && ActivePlanes(resetResumeLoom);
            checkpointReplayExact = checkpointMutationExact && checkpointResetExact;
            if (!checkpointMutationExact || !checkpointResetExact)
                Console.WriteLine($"      checkpoint replay detail · mutation={(checkpointMutationExact ? "exact" : "BROKEN")} reset={(checkpointResetExact ? "exact" : "BROKEN")} rules={checkpointLoom.RuleCount}/{resumedLoom.RuleCount} reset_rules={resetResumeLoom.RuleCount}/{resetGrammar.Rules.Length}");
        }
        if (!checkpointReplayExact) fails++;
        Console.WriteLine($"  {(checkpointReplayExact ? "✓" : "✗ DIVERGENCE")}  checkpoint-delta-replay · non-reset arena patches + explicit reset resplice exact");
        foreach (var text in new[] { "delta-alpha", "delta-alpha", "delta-alpha", "delta-beta", "delta-alpha" })
            deltaTape.Append(Encoding.UTF8.GetBytes(text), "delta", Provenances.Real);
        LoomMutationReceipt firstDelta = deltaLoom.ApplyTapeDelta(deltaTape, deltaTape.DrainDelta());
        deltaHeapMutations += firstDelta.HeapMutations; deltaHeapChangedKeys += firstDelta.HeapChangedKeys;
        deltaOk &= firstDelta.HeapMutations <= firstDelta.HeapChangedKeys;
        deltaLoom.Pump();
        deltaOk &= DeltaMatches();

        TapeEventID reflected = deltaTape.Append(Encoding.UTF8.GetBytes("delta-alpha"), "delta", Provenances.Replay);
        deltaLoom.ApplyTapeDelta(deltaTape, deltaTape.DrainDelta());
        deltaLoom.Pump();
        deltaTape.Reflect(reflected);
        deltaLoom.ApplyTapeDelta(deltaTape, deltaTape.DrainDelta());
        deltaOk &= DeltaMatches();

        var deltaLog = new MemoryStream();
        deltaTape.MountLog(deltaLog);
        TapeEventID dropped = deltaTape.Append(Encoding.UTF8.GetBytes("delta-unseen"), "delta", Provenances.Replay);
        deltaLoom.ApplyTapeDelta(deltaTape, deltaTape.DrainDelta());
        deltaLoom.Pump();
        deltaTape.Evacuate([], [dropped]);
        deltaLoom.ApplyTapeDelta(deltaTape, deltaTape.DrainDelta());
        deltaOk &= DeltaMatches();

        var shed = deltaTape.ResidentEventIDs.FirstOrDefault(id => deltaLoom.ParsedLenOf(id.Value) is >= 0 and <= 1);
        if (shed != default)
        {
            deltaTape.Evacuate([shed], []);
            deltaLoom.ApplyTapeDelta(deltaTape, deltaTape.DrainDelta());
            deltaOk &= DeltaMatches();
        }
        deltaLoom.CompactArena();
        deltaOk &= DeltaMatches();
        deltaOk &= deltaLoom.Result(deltaTape).Compressed.AsSpan().SequenceEqual(deltaLoom.Result(deltaTape).Compressed);
        if (!deltaOk) fails++;
        Console.WriteLine($"  {(deltaOk ? "✓" : "✗ DIVERGENCE")}  delta-mutations · append + reflect + drop + shed(no-op) + explicit arena compaction · heap mutations={deltaHeapMutations} changed-keys={deltaHeapChangedKeys} · batch differential {(batchArmExact ? "exact" : "BROKEN")} mutations={batchHeapMutations} changed-keys={batchHeapChangedKeys} · threshold re-arm {(thresholdArmExact ? "exact" : "BROKEN")} mutations={thresholdHeapMutations} · failure-prefix {(failurePrefixExact ? "exact" : "BROKEN")}");

        foreach (var (name, data) in cases)
        {
            // ── arm 1 · BATCH IDENTITY ──  loom-batch (Engine.Induce) vs the linear RePair oracle — plus the
            // WEIGHTED route (Engine.Induce(Tape, wScale) → per-segment weights) over a mixed-provenance tape,
            // the production path an armed (wScale>1) un-loomed run drives every stride.
            var sym0 = new Symbol[tok.MaxSymbols(data.Length)];
            int n0 = tok.Tokenize(data, sym0);
            var oracle = new RePair().Induce(sym0.AsSpan(0, n0), Mbits.Zero, barrier: '\n');
            bool batchOk = SameGrammar(Engine.Induce(data).Result, oracle);
            {
                var wtape = new Tape();
                int li = 0;
                foreach (var m in Engine.SplitLines(data))
                    wtape.Append(m.ToArray(), "gate", li++ % 3 == 2 ? Provenances.Replay : Provenances.Real);
                if (wtape.Count > 0)
                {
                    var wconcat = wtape.Concat();
                    var wsym = new Symbol[tok.MaxSymbols(wconcat.Length)];
                    int wn = tok.Tokenize(wconcat, wsym);
                    var wts = wtape.WeightsFor(8);
                    try
                    {
                        var woracle = new RePair().Induce(wsym.AsSpan(0, wn), Mbits.Zero, barrier: '\n', weights: wts.AsSpan(0, wn), wScale: 8);
                        batchOk &= SameGrammar(Engine.Induce(wtape, 8).Result, woracle);
                    }
                    finally { System.Buffers.ArrayPool<byte>.Shared.Return(wts); }

                    // weighted INCREMENTAL lineage (phase 2: wScale>1 rides the loom) — grow at per-span evidence
                    // weights (reconstruction exact), then VEST a dream span and REBASE on the fresh weighted
                    // re-greed (the Consolidate pattern: the rebase IS the vest-reweigh hook) — a killed+reloaded
                    // loom re-splices at CURRENT evidence status, so it must equal the live rebased one exactly.
                    using var wloom = new Loom(256, '\n', 8);
                    wloom.SpliceNew(wtape); wloom.Pump();
                    var wres = wloom.Result(wtape);
                    batchOk &= Reconstruct.Expand(wres.Rules, wres.Compressed).AsSpan().SequenceEqual(wconcat);
                    foreach (var sid in wtape.ResidentEventIDs) if (!wtape.IsEvidence(sid)) { wtape.Reflect(sid); break; }   // one reflect transition
                    wloom.Rebase(wtape, Engine.Induce(wtape, 8).Result);
                    using var wms = new MemoryStream();
                    using (var ww = new CkptWriter(wms)) wloom.Save(ww);
                    using var wre = new Loom(256, '\n', 8);
                    using (var wr = new CkptReader(new MemoryStream(wms.ToArray()))) wre.Load(wr, wtape);
                    batchOk &= ActivePlanes(wloom) && ActivePlanes(wre)
                             && SameGrammar(wre.Result(wtape), wloom.Result(wtape));
                }
            }

            // ── arms 2+3 · INCREMENTAL growth + mid-growth RESUME ──
            var spans = new List<byte[]>();
            foreach (var m in Engine.SplitLines(data)) spans.Add(m.ToArray());
            var tape = new Tape();
            var tape2 = new Tape();
            using var live = new Loom();
            Loom? resumed = null;
            byte[]? saved = null;
            bool reconOk = true, usesOk = true, resumeOk = true;
            int at = 0, rounds = 0, mid = Math.Max(1, spans.Count / 2);
            do
            {
                int take = Math.Min(3, spans.Count - at);
                for (int k = 0; k < take; k++) { tape.Append(spans[at + k], "gate"); tape2.Append(spans[at + k], "gate"); }
                at += take; rounds++;
                live.SpliceNew(tape); live.Pump();
                if (resumed is not null) { resumed.SpliceNew(tape2); resumed.Pump(); }
                if (data.Length < 8192 || rounds % 16 == 0 || at >= spans.Count)          // gate cadence — every round small, strided large
                {
                    reconOk &= ActivePlanes(live);
                    if (resumed is not null) reconOk &= ActivePlanes(resumed);
                    var res = live.Result(tape);
                    reconOk &= Reconstruct.Expand(res.Rules, res.Compressed).AsSpan().SequenceEqual(tape.Concat());
                    var uses = Engine.RuleUses(res);
                    for (int u = 0; u < uses.Length && usesOk; u++) usesOk &= uses[u] == live.Uses[u];
                }
                if (saved is null && at >= mid)
                {
                    using var ms = new MemoryStream();
                    using (var w = new CkptWriter(ms)) live.Save(w);
                    saved = ms.ToArray();
                    resumed = new Loom();
                    using (var rd = new CkptReader(new MemoryStream(saved))) resumed.Load(rd, tape2);
                    using var ms2 = new MemoryStream();
                    using (var w2 = new CkptWriter(ms2)) resumed.Save(w2);
                    resumeOk &= ms2.ToArray().AsSpan().SequenceEqual(saved);              // Save∘Load∘Save = identity
                }
            } while (at < spans.Count);

            var incr = live.Result(tape);
            if (resumed is not null) { resumeOk &= ActivePlanes(resumed) && SameGrammar(incr, resumed.Result(tape2)); resumed.Dispose(); }

            // ── the MDL gap vs the batch oracle over the SAME final bytes (the greedy-in-arrival tax) ──
            var concat = tape.Concat();
            var sym1 = new Symbol[tok.MaxSymbols(concat.Length)];
            int n1 = tok.Tokenize(concat, sym1);
            var batch = new RePair().Induce(sym1.AsSpan(0, n1), Mbits.Zero, barrier: '\n');
            long dlI = DescriptionLength(incr), dlB = DescriptionLength(batch);
            double gapPct = dlB > 0 ? (dlI - dlB) * 100.0 / dlB : 0;
            bool gapOk = gapPct <= 20.0;

            // ── arm 4 · SELF-COMPRESS (phase 3) ──  compact the batch output: reconstruction EXACT, rule count
            // drops by exactly the merge count, and a loom REBASED on (compacted rules + rank aliases) harvests a
            // reconstruction-exact grammar whose usage tally matches the harvest (the alias plane holds counts).
            var sc = Cogito.Induct.SelfCompress.Compact(batch);
            bool scOk = Reconstruct.Expand(sc.Grammar.Rules, sc.Grammar.Compressed).AsSpan().SequenceEqual(concat)
                     && sc.Grammar.Rules.Length == batch.Rules.Length - sc.Merged;
            using (var rebased = new Loom())
            {
                rebased.Rebase(tape, sc.Grammar, sc.Aliases);
                var harvest = rebased.Result(tape);
                scOk &= ActivePlanes(rebased)
                     && Reconstruct.Expand(harvest.Rules, harvest.Compressed).AsSpan().SequenceEqual(tape.Concat());
                var hu = Engine.RuleUses(harvest);
                for (int u = 0; u < hu.Length && scOk; u++) scOk &= hu[u] == rebased.Uses[u];

                // ── arm 5 · TAPE-SHED (phase 3) ──  shed every span the grammar generates whole (parsed ≤ 1):
                // the GRAMMAR is untouched (rules + savings + uses — order-freedom: not one count moves), the new
                // view reconstructs exactly, shed bytes resolve from the log byte-exact, the tape checkpoint
                // round-trips (Save∘Load∘Save = identity), and a fresh loom LOADED over the shed tape harvests
                // the identical grammar (kill→resume with the log).
                bool shedOk = true;
                var shedSet = new List<TapeEventID>();
                foreach (var sid in tape.ResidentEventIDs) if (rebased.ParsedLenOf(sid.Value) is >= 0 and <= 1) shedSet.Add(sid);
                if (shedSet.Count > 0)
                {
                    var origBytes = new Dictionary<long, byte[]>();
                    foreach (var sid in shedSet) { tape.Resolve(sid, out var b); origBytes[sid.Value] = b; }
                    var logStream = new MemoryStream();
                    tape.MountLog(logStream);
                    tape.Evacuate(shedSet, []);
                    var gAfter = rebased.Result(tape);
                    shedOk &= gAfter.Rules.Length == harvest.Rules.Length && gAfter.TotalSavings.Value == harvest.TotalSavings.Value;
                    for (int u = 0; u < harvest.Rules.Length && shedOk; u++) shedOk &= gAfter.Rules[u].Id.Equals(harvest.Rules[u].Id);
                    shedOk &= Reconstruct.Expand(gAfter.Rules, gAfter.Compressed).AsSpan().SequenceEqual(tape.Concat());
                    foreach (var sid in shedSet)
                        shedOk &= tape.Resolve(sid, out var rb) && rb.AsSpan().SequenceEqual(origBytes[sid.Value]);
                    using var tms = new MemoryStream();
                    using (var tw = new CkptWriter(tms)) tape.Save(tw);
                    var timg = tms.ToArray();
                    var tape3 = new Tape();
                    tape3.MountLog(new MemoryStream(logStream.ToArray()));
                    using (var tr = new CkptReader(new MemoryStream(timg))) tape3.Load(tr);
                    using var tms2 = new MemoryStream();
                    using (var tw2 = new CkptWriter(tms2)) tape3.Save(tw2);
                    shedOk &= tms2.ToArray().AsSpan().SequenceEqual(timg) && tape3.Concat().AsSpan().SequenceEqual(tape.Concat());
                    using var lms = new MemoryStream();
                    using (var lw = new CkptWriter(lms)) rebased.Save(lw);
                    using var reloaded = new Loom();
                    using (var lr = new CkptReader(new MemoryStream(lms.ToArray()))) reloaded.Load(lr, tape3);
                    shedOk &= SameGrammar(reloaded.Result(tape3), gAfter);
                    tape3.Dispose();
                }
                scOk &= shedOk;
            }

            bool ok = batchOk && reconOk && usesOk && resumeOk && gapOk && scOk;
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "✓" : "✗ DIVERGENCE")}  {name,-8} {data.Length,7}B · batch {(batchOk ? "≡" : "BROKEN")} · recon {(reconOk ? "exact" : "BROKEN")} · uses {(usesOk ? "exact" : "DRIFT")} · resume {(resumeOk ? "≡" : "BROKEN")} · rules {incr.Rules.Length}/{batch.Rules.Length} · MDL gap {gapPct:+0.00;-0.00}% · compact −{sc.Merged}r/{sc.Aliases.Count}a+shed {(scOk ? "≡" : "BROKEN")}");
            if (data.Length >= 4096)
            {
                // the read-drift readout on the real corpus: the incremental grammar must READ like the batch one
                var rnI = Engine.RenormStats(incr);
                var rnB = Engine.RenormStats(batch);
                var covI = new Engine.GrammarCover(incr.Rules);
                var covB = new Engine.GrammarCover(batch.Rules);
                Console.WriteLine($"      drift · cvz {rnI.CvZ:F3}/{rnB.CvZ:F3} · depth {rnI.Scales}/{rnB.Scales} · self-cover {covI.Coverage(concat):F3}/{covB.Coverage(concat):F3} · parsed {covI.ParsedSize(concat)}/{covB.ParsedSize(concat)} · Δmdl {incr.TotalSavings.Value}/{batch.TotalSavings.Value}");
            }
        }
        Console.WriteLine(fails == 0
            ? "✓ verify-loom PASSED — batch ≡ oracle byte-for-byte, incremental reconstruction + usage counters exact at every growth step, kill→resume byte-identical, MDL gap bounded."
            : $"✗ verify-loom FAILED — {fails} divergence(s); the loom is NOT a sound incremental substrate.");
        return fails == 0 ? 0 : 1;
    }

    // ── fix ──  error correction. Localize the uncovered words (where the probe deviates from learned
    // structure) and propose the nearest grammar CONCEPT by edit distance — autocorrect via the grammar. The
    // trophy's edit-PRODUCE step in miniature: not just locating the edit site, but proposing the edit.
    private static int Fix(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("  usage: fix <corpus> <probe-text>"); return 1; }
        var seed = LoadCorpus(args);
        byte[] probe = File.Exists(args[^1]) ? File.ReadAllBytes(args[^1]) : Encoding.UTF8.GetBytes(string.Join(' ', args[2..]));
        var (_, _, g) = Engine.Induce(seed);
        var mask = Engine.CoverMask(g.Rules, probe);
        var concepts = Enumerable.Range(0, g.Rules.Length).Select(i => Encoding.UTF8.GetString(Expand(g, i)).Trim()).Where(s => s.Length >= 2).Distinct().ToList();
        Console.WriteLine($"fix · grammar {g.Rules.Length} rules · probe {probe.Length} B · localize errors → propose the nearest concept");
        var done = new HashSet<string>();
        int hits = 0;
        for (int i = 0; i < probe.Length; i++)
        {
            if (mask[i] || probe[i] == (byte)' ' || probe[i] == (byte)'\n') continue;
            int s = i; while (s > 0 && probe[s - 1] != (byte)' ' && probe[s - 1] != (byte)'\n') s--;
            int e = i; while (e < probe.Length && probe[e] != (byte)' ' && probe[e] != (byte)'\n') e++;
            var word = Encoding.UTF8.GetString(probe, s, e - s);
            i = e;
            if (word.Length < 2 || !done.Add(word)) continue;
            var best = concepts.Where(c => Math.Abs(c.Length - word.Length) <= 3).OrderBy(c => Lev(word, c)).ThenByDescending(c => c.Length).FirstOrDefault();
            int d = best == null ? 99 : Lev(word, best);
            if (best != null && d <= Math.Max(1, word.Length / 3)) { Console.WriteLine($"    \"{word}\" → \"{best}\"?  (edit distance {d})"); hits++; }
            else Console.WriteLine($"    \"{word}\" → novel (no close concept)");
        }
        if (hits == 0) Console.WriteLine("    (no high-confidence corrections — probe structurally clean or wholly novel)");
        return 0;
    }

    // ── verify-loom --bench ──  the O(Δ) payoff readout (kill-line c): grow a tape round by round; per round
    // measure the loom's splice+pump (must FLATLINE — it prices the Δ, not the tape) and emit (O(compressed),
    // linear-mild) against the batch re-induce (concat+tokenize+induce — O(tape), the climbing wall the loom kills).
    private static int LoomBench(string[] args)
    {
        var corpus = LoadCorpus(args);
        var spans = new List<byte[]>();
        foreach (var m in Engine.SplitLines(corpus)) spans.Add(m.ToArray());
        if (spans.Count == 0) { Console.Error.WriteLine("  verify-loom --bench: empty corpus — nothing to grow"); return 1; }
        const int rounds = 24;
        int per = Math.Max(1, spans.Count / rounds);
        Console.WriteLine($"verify-loom --bench · {corpus.Length}B / {spans.Count} spans · ~{rounds} rounds × {per} spans — loom splice+pump must flatline while batch climbs");
        Console.WriteLine("  round   spans     bytes  Δspans   loom.splice+pump ms   loom.emit ms   batch ms   rules loom/batch");
        var tape = new Tape();
        using var loom = new Loom();
        var tok = Cogito.Observe.ByteTokenizer.Instance;
        var sw = new System.Diagnostics.Stopwatch();
        var loomMs = new List<double>();
        var batchMs = new List<double>();
        int at = 0;
        Cogito.Induct.RePairResult res = default, batch = default;
        while (at < spans.Count)
        {
            int take = Math.Min(per, spans.Count - at);
            for (int k = 0; k < take; k++) tape.Append(spans[at + k], "bench");
            at += take;
            sw.Restart(); int added = loom.SpliceNew(tape); loom.Pump(); double msSplice = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); res = loom.Result(tape); double msEmit = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var concat = tape.Concat();
            var sym = new Symbol[tok.MaxSymbols(concat.Length)];
            int n = tok.Tokenize(concat, sym);
            batch = new RePair().Induce(sym.AsSpan(0, n), Mbits.Zero, barrier: '\n');
            double msBatch = sw.Elapsed.TotalMilliseconds;
            loomMs.Add(msSplice); batchMs.Add(msBatch);
            Console.WriteLine($"  {loomMs.Count,5} {tape.Count,7} {tape.ByteLength,9} {added,7} {msSplice,21:F2} {msEmit,14:F2} {msBatch,10:F2}   {res.Rules.Length}/{batch.Rules.Length}");
        }
        bool recon = Reconstruct.Expand(res.Rules!, res.Compressed).AsSpan().SequenceEqual(tape.Concat());   // ! — the empty-corpus guard above proves ≥1 round ran
        int q = Math.Max(1, loomMs.Count / 4);
        double head = 0, tail = 0, bHead = 0, bTail = 0;
        for (int i = 0; i < q; i++) { head += loomMs[i]; bHead += batchMs[i]; tail += loomMs[^(i + 1)]; bTail += batchMs[^(i + 1)]; }
        Console.WriteLine($"  → loom splice+pump first-quartile μ {head / q:F2}ms vs last-quartile μ {tail / q:F2}ms (×{(head > 0 ? tail / head : 0):F1})"
                        + $" · batch μ {bHead / q:F2}ms → {bTail / q:F2}ms (×{(bHead > 0 ? bTail / bHead : 0):F1})"
                        + $" · final round loom/batch = {(batchMs[^1] > 0 ? loomMs[^1] / batchMs[^1] * 100 : 0):F1}% · recon {(recon ? "exact" : "BROKEN")}");
        return recon ? 0 : 1;
    }
}

}   // namespace Cogito.Cli
