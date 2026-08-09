using System.CommandLine;
using System.Linq;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using static Cogito.CliReports;

namespace Cogito.Cli   // the child namespace sees Cogito (parent) directly — no `using Cogito;` needed
{

// ── AGENT-RETRIEVAL CLUSTER (`rag`) ──  cogito's LLM-in-the-loop retrieval + self-play verbs. Each verb's typed
// Build() owns tokenize + validation + generated help; the SetAction reads ParseResult.GetValue and invokes the
// verb BODY that lives right here in the cluster (no more Cli-partial bridge hop). AOT-safe by construction —
// the EXPLICIT api, never SetHandler reflection-binding. Shared report helpers come from CliReports.
//
// Two shapes appear here:
//   • TYPED-CALL (llmcodec/speak/talk) — the body takes `byte[] corpus`; resolve the optional corpus positional
//     via CliShared.LoadCorpus and hand the bytes straight in. The `stats` floor of the pattern.
//   • ADAPTER-ARGV (the rest) — the body is `int Foo(string[] args)` with a dense internal Args scrape we keep;
//     rebuild the argv it expects. Defaults live in the BODY (e.g. grow rounds=3, distill paraN=6) — the adapter
//     only forwards what the user set, so the body applies its own default when a knob is absent.
//
// The FREE-TEXT VARIADIC verbs (grow/hunt) fold an optional trailing-int rounds INTO the free words: the body
// pops args[^1] as rounds iff it int-parses, else it's part of the seed/domain. So the adapter must pass the
// trailing token THROUGH unmolested — modeled as a single Argument<string[]> that captures every trailing
// token (seed/domain words AND the optional rounds int); the body's own tail-parse then reproduces exactly.
public static class AgentRetrievalCommands
{
    public static IEnumerable<Command> All()
    {
        yield return Bench();
        yield return LlmCodec();
        yield return Grow();
        yield return Rl();
        yield return Speak();
        yield return Talk();
        yield return Chat();
        yield return Hunt();
        yield return Retrieve();
        yield return Gendev();
        yield return Distill();
        yield return Normalize();
        yield return Kill();
    }

    // ── bench ──  memory + intents (both required files) + 3 mode switches (--hybrid/--grounded/--rerank),
    // each a bool the body reads via args.Contains. ADAPTER-ARGV: argv = [bench, memory, intents, ...switches].
    private static Command Bench()
    {
        var memory   = new Argument<string>("memory-file")   { Description = "passages, \\n\\n-separated (site i = passage i)" };
        var intents  = new Argument<string>("intents-file")  { Description = "one intent per line, intent i targets site i" };
        var hybrid   = new Option<bool>("--hybrid")   { Description = "LLM expands each paraphrase into generic tech keywords" };
        var grounded = new Option<bool>("--grounded") { Description = "LLM rewrites each intent into cogito's OWN concept vocabulary" };
        var rerank   = new Option<bool>("--rerank")   { Description = "cogito narrows to top-6, the LLM picks the best (the hybrid machine gun)" };

        var cmd = new Command("bench", "edit-site induction benchmark (top-1 accuracy + MRR + recall@6)")
        {
            memory, intents, hybrid, grounded, rerank
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "bench", parse.GetValue(memory)!, parse.GetValue(intents)! };
            if (parse.GetValue(hybrid))   argv.Add("--hybrid");
            if (parse.GetValue(grounded)) argv.Add("--grounded");
            if (parse.GetValue(rerank))   argv.Add("--rerank");
            return Bench(argv.ToArray());
        });
        return cmd;
    }

    // ── llmcodec ──  the beyond-golf predictive codec via a frontier LLM. Corpus positional only (LoadCorpus's contract:
    // omitted/missing ⇒ builtin sample). TYPED-CALL: resolve the bytes and hand them to the body.
    private static Command LlmCodec()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("llmcodec", "beyond-code-golf predictive codec via a frontier LLM (predict → diff → residual)")
        {
            corpus
        };
        cmd.SetAction(parse => LlmCodecRun(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── grow ──  the closed loop (pretrain → induce → cogito directs the next request). Free-text seed
    // (default "short idiomatic python functions") with an OPTIONAL trailing rounds int (1–12, default 3)
    // folded into the words, plus --self (autoregressive re-observation). ADAPTER-ARGV: emit --self, then pass
    // EVERY trailing token through so the body's own `words.Remove("--self")` + tail-int parse reproduces exactly.
    private static Command Grow()
    {
        var self = new Option<bool>("--self") { Description = "autoregressive: re-observe cogito's own mutations" };
        var seed = new Argument<string[]>("seed") { Arity = ArgumentArity.ZeroOrMore, Description = "seed domain words + optional trailing rounds int (1–12, default 3); default seed = short idiomatic python functions" };

        var cmd = new Command("grow", "the loop closed — LLM feeds, cogito induces, cogito directs the next hunt")
        {
            self, seed
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "grow" };
            if (parse.GetValue(self)) argv.Add("--self");
            argv.AddRange(parse.GetValue(seed) ?? []);
            return Grow(argv.ToArray());
        });
        return cmd;
    }

    // ── rl ──  post-training RLVR, verifier = codex. Corpus positional (LoadCorpus, reads argv[1]) + REQUIRED
    // criterion which the body takes as args[^1] ONLY. ADAPTER-ARGV: argv = [rl, corpus, criterion] (exactly 3,
    // the body gates on args.Length < 3). Corpus required here so criterion always lands at args[^1] past it.
    private static Command Rl()
    {
        var corpus    = new Argument<string>("corpus")    { Description = "seed corpus the policy grammar induces from" };
        var criterion = new Argument<string>("criterion") { Description = "reward criterion codex scores 0–10 (e.g. \"valid idiomatic SQL\")" };

        var cmd = new Command("rl", "post-training RL analogue — codex is the verifier (generate→score→select→re-induce)")
        {
            corpus, criterion
        };
        cmd.SetAction(parse => Rl(["rl", parse.GetValue(corpus)!, parse.GetValue(criterion)!]));
        return cmd;
    }

    // ── speak ──  cogito GENERATES from its own induced grammar (greedy chunk-transition walk). Corpus
    // positional, builtin fallback. TYPED-CALL.
    private static Command Speak()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("speak", "cogito generates from its own grammar — learning to talk (greedy walk)")
        {
            corpus
        };
        cmd.SetAction(parse => Speak(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── talk ──  coherent generation via MCMC (Gibbs, bidirectional). Corpus positional, builtin fallback. TYPED-CALL.
    private static Command Talk()
    {
        var corpus = CliShared.CorpusArg();
        var cmd = new Command("talk", "coherent generation via MCMC (Gibbs over the chunk-sequence)")
        {
            corpus
        };
        cmd.SetAction(parse => Talk(CliShared.LoadCorpus(parse.GetValue(corpus))));
        return cmd;
    }

    // ── chat ──  the conditioned turn (retrieve-then-generate). knowledge-file (builtin fallback when the path
    // isn't a file) + variadic prompt words. ADAPTER-ARGV: argv = [chat, knowledge, ...prompt] (body gates on
    // args.Length < 3, so knowledge is required and the prompt takes ≥1 word).
    private static Command Chat()
    {
        var knowledge = new Argument<string>("knowledge-file") { Description = "knowledge passages; non-file ⇒ builtin sample" };
        var prompt    = new Argument<string[]>("prompt") { Arity = ArgumentArity.OneOrMore, Description = "the prompt words to condition on" };

        var cmd = new Command("chat", "the conditioned turn — retrieve the topic, induce on it, generate greedy")
        {
            knowledge, prompt
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "chat", parse.GetValue(knowledge)! };
            argv.AddRange(parse.GetValue(prompt) ?? []);
            return Chat(argv.ToArray());
        });
        return cmd;
    }

    // ── hunt ──  the self-directed curriculum (cogito aims codex at its OWN gaps vs random spray). Variadic
    // domain words with an OPTIONAL trailing rounds int (2–8, default 4) folded in; the body strips any --flag
    // from the domain and pops the trailing int itself. ADAPTER-ARGV: pass every trailing token through.
    private static Command Hunt()
    {
        var domain = new Argument<string[]>("domain") { Arity = ArgumentArity.OneOrMore, Description = "the domain to hunt + optional trailing rounds int (2–8, default 4)" };

        var cmd = new Command("hunt", "self-directed curriculum — cogito directs codex at its own gaps (the loopback seed)")
        {
            domain
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "hunt" };
            argv.AddRange(parse.GetValue(domain) ?? []);
            return Hunt(argv.ToArray());
        });
        return cmd;
    }

    // ── retrieve ──  RAG via the grammar (compression = relevance). memory-file (required file) + variadic
    // query words. ADAPTER-ARGV: argv = [retrieve, memory, ...query] (body gates on args.Length < 3 && File.Exists).
    private static Command Retrieve()
    {
        var memory = new Argument<string>("memory-file") { Description = "passages, \\n\\n-separated — the retrieval corpus" };
        var query  = new Argument<string[]>("query") { Arity = ArgumentArity.OneOrMore, Description = "the query words to rank passages against" };

        var cmd = new Command("retrieve", "RAG via the grammar — rank passages by structural coverage of the query")
        {
            memory, query
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "retrieve", parse.GetValue(memory)! };
            argv.AddRange(parse.GetValue(query) ?? []);
            return Retrieve(argv.ToArray());
        });
        return cmd;
    }

    // ── gendev ──  mint a LABELED held-out set (site<TAB>intent). index-file (required) + out-file + optional
    // per-site int (default 6, positional args[3]). ADAPTER-ARGV: argv = [gendev, index, out, per?].
    private static Command Gendev()
    {
        var index   = new Argument<string>("index-file") { Description = "the code sites to paraphrase, \\n\\n-separated" };
        var outFile = new Argument<string>("out-file")   { Description = "labeled rollout corpus (site\\tintent per line) written here" };
        var perSite = new Argument<int?>("per-site") { Arity = ArgumentArity.ZeroOrOne, Description = "adversarial paraphrases per site (default 6)" };

        var cmd = new Command("gendev", "mint a labeled held-out set of adversarial paraphrases (the rollout corpus)")
        {
            index, outFile, perSite
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "gendev", parse.GetValue(index)!, parse.GetValue(outFile)! };
            if (parse.GetValue(perSite) is int per) argv.Add(per.ToString());
            return GenDev(argv.ToArray());
        });
        return cmd;
    }

    // ── distill ──  the DISTILLATION (teacher LLM writes NL/queries into the grammar). memory-file (required) +
    // out-file + optional paraN int (default 6, positional args[3]) + --queries/--contrastive switches.
    // ADAPTER-ARGV: argv = [distill, memory, out, paraN?, ...switches]. paraN MUST precede the switches (body
    // reads it positionally at args[3] and int-parses; a --flag there would fail the parse).
    private static Command Distill()
    {
        var memory      = new Argument<string>("memory-file") { Description = "passages the teacher describes/queries, \\n\\n-separated" };
        var outFile     = new Argument<string>("out-file")    { Description = "distilled memory written here" };
        var paraN       = new Argument<int?>("paraphrases-per-passage") { Arity = ArgumentArity.ZeroOrOne, Description = "paraphrases/queries per passage (2–200, default 6)" };
        var queries     = new Option<bool>("--queries")     { Description = "emit SEARCH QUERIES that REPLACE the passage (doc2query-grammar)" };
        var contrastive = new Option<bool>("--contrastive") { Description = "feed each passage its nearest SIBLINGS → DISCRIMINATING queries" };

        var cmd = new Command("distill", "distillation — the teacher LLM writes the NL↔code map into the grammar")
        {
            memory, outFile, paraN, queries, contrastive
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "distill", parse.GetValue(memory)!, parse.GetValue(outFile)! };
            if (parse.GetValue(paraN) is int pn) argv.Add(pn.ToString());
            if (parse.GetValue(queries))     argv.Add("--queries");
            if (parse.GetValue(contrastive)) argv.Add("--contrastive");
            return Distill(argv.ToArray());
        });
        return cmd;
    }

    // ── normalize ──  SEMANTIC NORMALIZATION (the caveman test). english-file (required) + out-file + --each
    // (normalize each \n\n-passage independently). ADAPTER-ARGV: argv = [normalize, english, out, --each?].
    private static Command Normalize()
    {
        var english = new Argument<string>("english-file") { Description = "English prose to telegraphically normalize" };
        var outFile = new Argument<string>("out-file")     { Description = "normalized caveman stream written here" };
        var each    = new Option<bool>("--each") { Description = "normalize each \\n\\n-passage independently (preserve boundaries)" };

        var cmd = new Command("normalize", "semantic normalization — English → canonical telegraphic caveman form")
        {
            english, outFile, each
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "normalize", parse.GetValue(english)!, parse.GetValue(outFile)! };
            if (parse.GetValue(each)) argv.Add("--each");
            return Normalize(argv.ToArray());
        });
        return cmd;
    }

    // ── kill ──  THE KILL-LINE (matched-budget held-out A/B). held-out-file (required) + VARIADIC ≥2 arms, each
    // `label:train-file` (first = candidate, last = baseline; the body needs args.Length ≥ 4 ⇒ ≥2 arms).
    // ADAPTER-ARGV: argv = [kill, held-out, ...arms]. Arms pass through raw so the body's `a.Split(':', 2)` +
    // File.Exists filter reproduces exactly.
    private static Command Kill()
    {
        var heldOut = new Argument<string>("held-out-file") { Description = "the held-out corpus every arm is scored against" };
        var arms    = new Argument<string[]>("arms") { Arity = ArgumentArity.OneOrMore, Description = "label:train-file arms, ≥2 (first = candidate, last = baseline)" };

        var cmd = new Command("kill", "the kill-line — matched-budget held-out A/B (candidate vs baseline)")
        {
            heldOut, arms
        };
        cmd.SetAction(parse =>
        {
            var argv = new List<string> { "kill", parse.GetValue(heldOut)! };
            argv.AddRange(parse.GetValue(arms) ?? []);
            return Kill(argv.ToArray());
        });
        return cmd;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    //  THE VERB BODIES — the LLM-in-the-loop retrieval + self-play routines. The TYPED-CALL trio (llmcodec/speak/
    //  talk) takes byte[]; the rest own a dense internal Args scrape the handlers feed a rebuilt argv. Shared
    //  report helpers (Truncate/NgramTokens/ScoreBatch/Slug/…) come from CliReports via `using static`.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    // ── llmcodec ──  the beyond-code-golf predictive codec via a frontier LLM (codex gpt-5.5/low). Verbose on purpose:
    // the pack, the cold-read, and WHERE it diverges — that divergence is the navigation signal for the next move.
    private static int LlmCodecRun(byte[] corpus)
    {
        var codex = CodexLlm.Default;
        int floor = global::Cogito.LlmCodec.FloorBudget(corpus);   // global:: — the `LlmCodec()` command-builder shadows the engine class
        Console.WriteLine($"llmcodec · {corpus.Length} bytes");
        Console.WriteLine($"  re-pair floor : {floor} B  (cogito owns — literal repetition, deterministic, zero LLM)");

        var codec = new PredictiveCodec(codex);
        Console.WriteLine("  encode: describe → LLM regenerates → cogito diffs…");
        var enc = codec.Encode(corpus);
        int predLcp = Lcp(corpus, enc.Prediction);
        Console.WriteLine($"  description ({Encoding.UTF8.GetByteCount(enc.Description),4} B): {Truncate(enc.Description, 220)}");
        Console.WriteLine($"  prediction  ({enc.Prediction.Length,4} B): matched {predLcp}/{corpus.Length} B before the first diff");
        Console.WriteLine($"  residual    ({enc.Residual.Size,4} B): prefix {enc.Residual.Prefix}, middle {enc.Residual.Middle.Length} B, suffix {enc.Residual.Suffix}");

        Console.WriteLine("  decode: LLM re-regenerates (determinism) → cogito applies residual…");
        var recon = codec.Decode(enc.Description, enc.Residual);
        bool lossless = recon.AsSpan().SequenceEqual(corpus);
        Console.WriteLine($"  recon: {recon.Length} B · lossless={lossless}");
        Console.WriteLine($"  codec {enc.Budget} B  vs  re-pair floor {floor} B  →  beyond-golf: {lossless && enc.Budget < floor}");
        return lossless ? 0 : 1;
    }

    // ── speak ──  cogito GENERATES from its own grammar — learning to talk. A deterministic walk over the
    // compressed sequence's chunk-transitions (the grammar IS a generative model), expanded to text. This is
    // the projection back out: cogito producing the language it induced. (RNG is a seeded LCG for v0 —
    // integer-only/deterministic; the canonical ChaCha20 (appendix-b) lands later. MCMC over the energy
    // landscape — the non-linear, variable-time-per-token generation — is the stage-4 upgrade of this walk.)
    private static int Speak(byte[] corpus)
    {
        var (_, n, r) = Engine.Induce(corpus);
        if (r.Rules.Length == 0) { Console.Error.WriteLine("  grammar too small to speak"); return 1; }
        var text = Engine.Generate(r, 160, 0x243F6A8885A308D3UL);
        Console.WriteLine($"speak · learned from {n} B → {r.Rules.Length} rules");
        Console.WriteLine("  ── cogito speaks ──");
        Console.WriteLine(Encoding.UTF8.GetString(text));
        return 0;
    }

    // ── talk ──  coherent generation via MCMC (Gibbs over the chunk-sequence, bidirectional conditioning).
    // The energy-landscape upgrade of speak: every chunk must fit BOTH neighbors, so the global thread holds
    // where the greedy forward-walk degenerated.
    private static int Talk(byte[] corpus)
    {
        var (_, n, r) = Engine.Induce(corpus);
        if (r.Rules.Length == 0) { Console.Error.WriteLine("  grammar too small to talk"); return 1; }
        var text = Engine.GenerateMCMC(r, 120, 14, 0x7A1C0DE5UL);
        Console.WriteLine($"talk · learned from {n} B → {r.Rules.Length} rules · MCMC (Gibbs, 14 sweeps, bidirectional)");
        Console.WriteLine("  ── cogito talks ──");
        Console.WriteLine(Encoding.UTF8.GetString(text));
        return 0;
    }

    // ── chat ──  the conditioned turn (the chatter), RETRIEVE-then-GENERATE. Rank the knowledge's lines by
    // IDF-weighted overlap with the prompt (BM25-lite — cogito's proven lexical strength), keep the top hits +
    // their neighbour windows as the TOPIC CONTEXT, induce a grammar on JUST that + the prompt, then generate
    // greedy. Conditioning is a PROPERTY of the restricted substrate — the grammar cannot wander off-topic
    // because it only KNOWS the topic — not a trick on the walk. Retrieve conditions; greedy generates clean.
    private static int Chat(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("  usage: chat <knowledge-file> <prompt words...>"); return 1; }
        var prompt = string.Join(' ', args[2..]);
        var lines = (File.Exists(args[1]) ? File.ReadAllText(args[1]) : Builtin)
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        // RETRIEVE: IDF-weighted term overlap with the prompt — the relevant lines.
        var qTerms = new HashSet<string>(NgramTokens(prompt));
        var lineTok = lines.Select(l => new HashSet<string>(NgramTokens(l))).ToList();
        var df = new Dictionary<string, int>();
        foreach (var lt in lineTok) foreach (var t in lt) df[t] = df.GetValueOrDefault(t) + 1;
        double Idf(string t) => Math.Log(1.0 + lines.Count / (1.0 + df.GetValueOrDefault(t)));
        double Score(int i) { double s = 0; foreach (var q in qTerms) if (lineTok[i].Contains(q)) s += Idf(q); return s; }
        var hits = Enumerable.Range(0, lines.Count).Where(i => Score(i) > 0).OrderByDescending(Score).Take(8).ToList();
        if (hits.Count == 0) { Console.Error.WriteLine($"  nothing relevant to '{prompt}' in the knowledge"); return 1; }
        // expand each hit to a ±-line window, union in original order → the retrieved topic context.
        var keep = new bool[lines.Count];
        foreach (var i in hits) for (int w = Math.Max(0, i - 1); w <= Math.Min(lines.Count - 1, i + 3); w++) keep[w] = true;
        var context = string.Join("\n", Enumerable.Range(0, lines.Count).Where(i => keep[i]).Select(i => lines[i]));
        // INDUCE on the retrieved context + the prompt; GENERATE greedy over it. The RESTRICTION is the whole
        // conditioning (the grammar only knows the topic) — no seed-trick on the walk (the tail-seed fought it:
        // "ran" → "ran into the court"). Let the topic-restricted grammar speak from its strongest opening.
        var (_, n, r) = Engine.Induce(Encoding.UTF8.GetBytes(context + "\n" + prompt));
        if (r.Compressed.Length < 3) { Console.Error.WriteLine("  retrieved context too thin to chat"); return 1; }
        var resp = Engine.Generate(r, 80, 0x243F6A8885A308D3UL);
        Console.WriteLine($"chat · retrieved {keep.Count(k => k)}/{lines.Count} lines for '{prompt}' → {n} B → {r.Rules.Length} rules");
        Console.WriteLine($"  user:   {prompt}");
        Console.WriteLine($"  cogito: {Encoding.UTF8.GetString(resp)}");
        return 0;
    }

    // ── hunt ──  the self-directed curriculum (a MEGAMORPH / the loopback seed). cogito DIRECTS codex at its
    // OWN gaps: each round it finds the held-out example it covers WORST and tells codex "generate more like
    // this", vs a control that sprays the domain randomly. If gap-targeted coverage outruns random, cogito
    // amplifies its own learning by self-direction — the seed of stage-6 (cogito steering the LLM).
    private static int Hunt(string[] args)
    {
        int rounds = 4;
        var rest = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (rest.Count > 1 && int.TryParse(rest[^1], out var rr)) { rounds = Math.Clamp(rr, 2, 8); rest.RemoveAt(rest.Count - 1); }
        var domain = string.Join(" ", rest);
        if (domain.Length == 0) { Console.Error.WriteLine("  usage: hunt <domain> [rounds]"); return 1; }
        var codex = CodexLlm.Default;
        const string sys = "You feed a grammar-induction engine. Diverse, REAL, self-contained examples. Raw only — no numbering, commentary, or fences.";
        var run = Run.New($"hunt-{Slug(domain)}");
        Console.WriteLine($"hunt · {domain} · cogito DIRECTS codex at its OWN gaps (vs random spray) · {rounds} rounds");
        var held = codex.Complete(sys, $"Generate 15 typical examples of: {domain}\nSeparate each with a blank line.")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => Encoding.UTF8.GetBytes(s)).Where(b => b.Length > 5).ToList();
        var seed = Encoding.UTF8.GetBytes(codex.Complete(sys, $"Generate 8 examples of: {domain}\nSeparate each with a blank line."));
        var corpusH = new List<byte>(seed); var corpusR = new List<byte>(seed);
        var gh = Engine.Induce(corpusH.ToArray()).Result; var gr = Engine.Induce(corpusR.ToArray()).Result;
        Console.WriteLine("    round   hunt-cov   random-cov   (held-out)");
        var rows = new List<string>();
        for (int r = 0; r <= rounds; r++)
        {
            double covH = held.Count == 0 ? 0 : held.Average(h => Engine.CoverageOf(gh.Rules, h));
            double covR = held.Count == 0 ? 0 : held.Average(h => Engine.CoverageOf(gr.Rules, h));
            Console.WriteLine($"    {r,4}    {covH,7:P0}    {covR,7:P0}");
            rows.Add($"{r}\t{covH:F4}\t{covR:F4}");
            if (r == rounds) break;
            var gap = held.OrderBy(h => Engine.CoverageOf(gh.Rules, h)).First();          // cogito's worst-covered held example
            var targeted = codex.Complete(sys, $"Generate 12 examples structurally similar to this one:\n{Encoding.UTF8.GetString(gap)}\nSeparate each with a blank line.");
            var generic = codex.Complete(sys, $"Generate 12 examples of: {domain}\nSeparate each with a blank line.");
            corpusH.AddRange(Encoding.UTF8.GetBytes(targeted)); gh = Engine.Induce(corpusH.ToArray()).Result;
            corpusR.AddRange(Encoding.UTF8.GetBytes(generic)); gr = Engine.Induce(corpusR.ToArray()).Result;
        }
        Console.WriteLine("  → hunt aims the LLM at cogito's OWN gaps; random sprays the domain. hunt-cov > random-cov ⟹ self-direction amplifies learning (the loopback seed).");
        run.WriteCurve("curve.tsv", "round\thunt_cov\trandom_cov\n" + string.Join("\n", rows) + "\n");
        return 0;
    }

    // ── retrieve ──  RAG via the grammar (the first trophy: reinvent retrieval — local, cheap, legible).
    // Memory = passages; cogito induces a grammar per passage and ranks them by how well each COVERS the query
    // (compression = relevance — the MDL analogue of cosine/BM25). Returns the relevant SITES — the LLM's local
    // machine-gun retrieval: infinite memory (add passages), surgical updates (edit a rule), zero blackbox.
    private static int Retrieve(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1])) { Console.Error.WriteLine("  usage: retrieve <memory-file> <query>"); return 1; }
        var passages = File.ReadAllText(args[1]).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 20).ToList();
        var query = Encoding.UTF8.GetBytes(string.Join(' ', args[2..]));
        var grams = passages.Select(p => Engine.Induce(Encoding.UTF8.GetBytes(p)).Result).ToList();
        // relevance = the query's STRUCTURAL coverage by passage p's grammar — multi-word CONCEPTS, not a
        // bag of words. This is cogito's edge over BM25/word-TF-IDF: the grammar's "light years" / "spiral
        // galaxy" concepts disambiguate where the bare word "light" (shared with photosynthesis) cannot.
        // STRUCTURAL coverage, LENGTH-NORMALIZED: divide by √|grammar| so a big passage can't win by sheer
        // rule-count covering generic words (the BM25 length-normalization, cogito-native — over multi-word
        // concepts, the edge over BM25's bag-of-words and cosine's blackbox).
        var ranked = grams.Select((g, i) => { double cov = Engine.CoverageOf(g.Rules, query); return (Idx: i, Cov: cov, Rel: cov / Math.Sqrt(Math.Max(1, g.Rules.Length)), Snippet: passages[i].ReplaceLineEndings(" ")); }).OrderByDescending(x => x.Rel).ToList();
        Console.WriteLine($"retrieve · memory {passages.Count} passages · relevance = STRUCTURAL coverage / √|grammar| (length-normalized MDL relevance)");
        Console.WriteLine("  ranked sites (most relevant first):");
        foreach (var x in ranked.Take(5))
            Console.WriteLine($"    [{x.Cov,5:P0} cov]  #{x.Idx}  {Truncate(x.Snippet, 56)}");
        if (ranked.Count >= 2)
            Console.WriteLine($"  → top #{ranked[0].Idx} vs worst #{ranked[^1].Idx} = {ranked[0].Rel / Math.Max(1e-6, ranked[^1].Rel):F1}× separation (length-normalized)");
        var topG = grams[ranked[0].Idx];
        var path = Encoding.UTF8.GetString(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).Distinct()
            .Select(w => { var wb = Encoding.UTF8.GetBytes(w); double top = Engine.CoverageOf(topG.Rules, wb), avg = grams.Average(gr => Engine.CoverageOf(gr.Rules, wb)); return (Word: w, Disc: top - avg, Top: top); })
            .Where(x => x.Top >= 0.5).OrderByDescending(x => x.Disc).Select(x => x.Word).Take(8);
        Console.WriteLine($"  why (relevance path — the discriminating concepts the top passage explains): {string.Join(" · ", path)}");
        return 0;
    }

    // ── bench ──  the edit-site-induction BENCHMARK (the trophy's measure). Memory = passages; intents file =
    // one intent per line in PASSAGE ORDER (intent i targets passage i). Retrieve each, report top-1 accuracy +
    // MRR — does cogito land the right site? The teacher LLM generates the intents (the distillation seed).
    private static int Bench(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1]) || !File.Exists(args[2])) { Console.Error.WriteLine("  usage: bench <memory-file> <intents-file> [--hybrid]"); return 1; }
        bool hybrid = args.Contains("--hybrid");                              // --hybrid: LLM expands each paraphrase into generic tech keywords
        bool grounded = args.Contains("--grounded");                         // --grounded: LLM rewrites each intent into cogito's OWN concept vocabulary
        bool rerank = args.Contains("--rerank");                             // --rerank: cogito narrows to top-6 (deterministic), the LLM picks the best — the hybrid machine gun
        var passages = File.ReadAllText(args[1]).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 20).ToList();
        var grams = passages.Select(p => Engine.Induce(Encoding.UTF8.GetBytes(p)).Result).ToList();
        var intents = File.ReadAllLines(args[2]).Select(l => l.Trim()).Where(l => l.Length > 5).ToList();
        var codex = (hybrid || grounded || rerank) ? CodexLlm.Default : null;
        const string expandSys = "You rewrite a vague software-feature description into the specific technical and code-level keywords a programmer would grep for to find the code. Output ONE line — the concepts/keywords only, no commentary.";
        // --grounded: extract cogito's OWN vocabulary (the grammars' load-bearing concepts) so the LLM rewrites
        // the intent on cogito's home turf — surface-matching the lexical retriever instead of guessing generic terms.
        string vocab = "";
        if (grounded)
        {
            var freq = new Dictionary<string, int>();
            foreach (var g in grams)
                for (int r = 0; r < g.Rules.Length; r++)
                {
                    var c = Encoding.UTF8.GetString(Expand(g, r)).Trim();
                    if (c.Length is >= 4 and <= 28 && c.Count(char.IsLetter) >= c.Length / 2) freq[c] = freq.GetValueOrDefault(c) + 1;
                }
            vocab = string.Join(", ", freq.OrderByDescending(kv => kv.Value).Take(120).Select(kv => kv.Key));
        }
        const string groundSys = "You translate a vague developer intent into a search query using ONLY words drawn from the provided vocabulary of one specific codebase. Choose the 4–8 vocabulary entries most relevant to the intent. Output ONLY those entries, space-separated, nothing else.";
        // N-GRAM-COUNT relevance: how many of the query's 1+2-grams the passage contains — bag-of-concepts overlap
        // at the granularity that actually matches paraphrases. Beats byte-coverage (32%→48%) AND the deep grammar
        // concepts (24%, too coarse): retrieval is about HOW MANY discriminating sub-concepts overlap, and the
        // 1+2-gram is the unit that matches. (The grammar earns its keep elsewhere — the legible PATH + the science.)
        var tfByP = passages.Select(p => { var c = new Dictionary<string, int>(); foreach (var g in NgramTokens(p)) c[g] = c.GetValueOrDefault(g) + 1; return c; }).ToList();
        var dfN = new Dictionary<string, int>();                             // doc-frequency per n-gram → IDF down-weights generic terms (the discrimination lever)
        foreach (var c in tfByP) foreach (var g in c.Keys) dfN[g] = dfN.GetValueOrDefault(g) + 1;
        double avgdl = tfByP.Average(c => (double)c.Values.Sum());
        const double k1 = 1.5, b = 0.3;                                      // BM25: term-frequency saturation + LOW length-norm (passages are equal-size doc2query blocks)
        Console.WriteLine($"bench · edit-site induction · {passages.Count} sites · {intents.Count} intents · {(hybrid ? "HYBRID · " : grounded ? "GROUNDED · " : "")}{(rerank ? "RERANK(top-6) · " : "")}relevance = BM25(ngram)");
        int hits = 0, n = 0, recall6 = 0; double mrr = 0;
        for (int i = 0; i < intents.Count && i < passages.Count; i++)
        {
            string qtext = hybrid ? codex!.Complete(expandSys, intents[i])
                         : grounded ? codex!.Complete(groundSys, $"Vocabulary: {vocab}\n\nIntent: {intents[i]}")
                         : intents[i];
            var qn = Ngrams(qtext);
            var ranked = Enumerable.Range(0, passages.Count).Select(j =>
            {
                var c = tfByP[j]; double dl = c.Values.Sum();
                double rel = qn.Where(c.ContainsKey).Sum(g => Math.Log(1 + (passages.Count - dfN[g] + 0.5) / (dfN[g] + 0.5)) * (c[g] * (k1 + 1)) / (c[g] + k1 * (1 - b + b * dl / avgdl)));
                return (J: j, Rel: rel);
            }).OrderByDescending(x => x.Rel).ToList();
            int rank = ranked.FindIndex(x => x.J == i) + 1;
            if (rank <= 6) recall6++;
            int predSite = ranked[0].J;
            if (rerank)
            {
                var topK = ranked.Take(6).ToList();
                var cands = string.Join("\n", topK.Select((x, k) => $"[{k}] {Truncate(passages[x.J].ReplaceLineEndings(" "), 180)}"));
                var ans = codex!.Complete("Given a developer intent and candidate code-site descriptions, reply with ONLY the bracketed index of the single best match (e.g. [2]).", $"Intent: {intents[i]}\n\nCandidates:\n{cands}");
                var mm = System.Text.RegularExpressions.Regex.Match(ans, @"\[?(\d+)\]?");
                if (mm.Success) predSite = topK[Math.Clamp(int.Parse(mm.Groups[1].Value), 0, topK.Count - 1)].J;
            }
            bool ok = predSite == i;
            if (ok) hits++;
            mrr += 1.0 / rank; n++;
            Console.WriteLine($"    intent {i,2} → site #{predSite,2} (want #{i,2})  cogito-rank {rank,2}  {(ok ? "✓" : "✗")}  \"{Truncate(qtext, 44)}\"");
        }
        Console.WriteLine($"  → top-1 accuracy {hits}/{n} = {(n == 0 ? 0 : 100.0 * hits / n):F0}%  ·  MRR {(n == 0 ? 0 : mrr / n):F2}  ·  recall@6 {recall6}/{n} = {(n == 0 ? 0 : 100.0 * recall6 / n):F0}% (the rerank ceiling)");
        return 0;
    }

    // ── gendev ──  mint a LABELED held-out set of adversarial paraphrases (one per line: "site<TAB>intent") —
    // the rollout corpus for learning the incompleteness measure (cogito predicting its own retrieval correctness).
    private static int GenDev(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1])) { Console.Error.WriteLine("  usage: gendev <index-file> <out-file> [per-site]"); return 1; }
        int per = (args.Length > 3 && int.TryParse(args[3], out var p)) ? p : 6;
        var sites = File.ReadAllText(args[1]).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 20).ToList();
        var codex = CodexLlm.Default;
        var sb = new StringBuilder();
        Console.WriteLine($"gendev · {sites.Count} sites · {per} adversarial paraphrases each → labeled rollout corpus · batched ×8");
        // Fan the per-site paraphrase calls out (CompleteBatch ×8) — they are independent, so serial crawling
        // was pure wall-time waste (the same fan-out distill/drive already use). Results return in input order.
        var gsys = $"Write {per} search intents a NON-EXPERT user would type to find THIS code — casual everyday words, synonyms, vague/indirect phrasing; AVOID the code's technical identifiers. Maximize vocabulary distance. One per line, no numbering, no commentary.";
        var gprompts = sites.Select(s => (gsys, s.Length > 1200 ? s[..1200] : s)).ToList();
        var gens = codex.CompleteBatch(gprompts);
        for (int s = 0; s < sites.Count; s++)
            foreach (var line in gens[s].Split('\n').Select(x => x.Trim()).Where(x => x.Length > 5).Take(per))
                sb.AppendLine($"{s}\t{line}");
        File.WriteAllText(args[2], sb.ToString());
        Console.WriteLine($"  → {sb.ToString().Count(c => c == '\n')} labeled queries → {args[2]}");
        return 0;
    }

    // ── distill ──  the DISTILLATION (the trophy's "distilled from the teacher LLM"). For each memory passage,
    // the LLM writes diverse plain-English descriptions of what it does; these are APPENDED to the passage so
    // the GRAMMAR learns the NL↔code map. Retrieval then becomes SEMANTIC (a paraphrased intent matches the
    // learned NL space), not just lexical — the semantic capability LEARNED INTO the grammar, not bolted on.
    private static int Distill(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1])) { Console.Error.WriteLine("  usage: distill <memory-file> <out-file> [paraphrases-per-passage] [--queries] [--contrastive]"); return 1; }
        int paraN = (args.Length > 3 && int.TryParse(args[3], out var pn)) ? Math.Clamp(pn, 2, 200) : 6;  // ceiling-hunt: push doc2query/site past 40
        bool queriesMode = args.Contains("--queries");                       // --queries: emit SEARCH QUERIES that REPLACE the passage (doc2query-grammar), not descriptions appended to it
        bool contrastive = args.Contains("--contrastive");                   // --contrastive: feed each passage its nearest SIBLINGS so the teacher writes DISCRIMINATING queries — aims the doc2query at the rank-2 sibling near-ties (the residual after plain doc2query)
        var passages = File.ReadAllText(args[1]).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 20).ToList();
        var codex = CodexLlm.Default;
        string sys = contrastive
            ? $"You write SEARCH QUERIES that DISCRIMINATE a target method from its confusable SIBLINGS in the same codebase. Every query must retrieve the TARGET and NOT the siblings — emphasize the TARGET's DISTINCTIVE input type, output, core operation, or side-effect that the siblings lack. Output exactly {paraN} short queries, one per line, no numbering, no commentary, no code."
            : queriesMode
            ? $"You write the diverse SEARCH QUERIES a developer would type to FIND this code — different phrasings, synonyms, and angles, plus the key domain concepts. Output exactly {paraN} short queries, one per line, no numbering, no commentary, no code."
            : $"You write SHORT descriptions that DISTINGUISH this code from other utilities in the same project. Use its SPECIFIC unique terminology and purpose — the distinctive nouns and verbs a developer would actually search for. AVOID generic phrasing ('processes data', 'handles logic', 'manages state', 'when you want to') that could fit any code. Output exactly {paraN} short lines, each a different specific angle; no numbering, no commentary, no code.";
        // contrastive: each passage's top-3 siblings by shared-ngram IDF mass — the confusion the lexical retriever itself sees.
        int[][] sibs = contrastive ? ComputeSiblings(passages, 3) : System.Array.Empty<int[]>();
        var sb = new StringBuilder();
        Console.WriteLine($"distill · {passages.Count} passages · {paraN} {(queriesMode || contrastive ? "SEARCH QUERIES (doc2query-grammar, replaces passage)" : "NL paraphrases (appended)")} each{(contrastive ? " · CONTRASTIVE (sibling-aware)" : "")} · batched ×8");
        // Fan the per-passage teacher calls out (CompleteBatch, parallelism 8) — they are independent, so serial
        // crawling was pure wall-time waste. Results return in input order.
        var prompts = passages.Select((p, i) =>
        {
            string code = p.Length > 1500 ? p[..1500] : p;
            if (!contrastive) return (sys, $"{(queriesMode ? "Generate the search queries to FIND this code" : "Describe what this code does")}:\n{code}");
            string sibText = string.Concat(sibs[i].Select(j => $"\n### SIBLING — your queries must NOT match this one:\n{(passages[j].Length > 500 ? passages[j][..500] : passages[j])}\n"));
            return (sys, $"TARGET CODE (write queries that find THIS, not the siblings below):\n{code}\n{sibText}");
        }).ToList();
        var descs = codex.CompleteBatch(prompts);
        for (int i = 0; i < passages.Count; i++)
        {
            // queries/contrastive mode: the queries REPLACE the passage (the query-language grammar IS the index); desc
            // mode: append to the code. Collapse blank lines so each passage stays ONE block (either can introduce them).
            var content = queriesMode || contrastive ? descs[i] : passages[i] + "\n" + descs[i];
            sb.Append(System.Text.RegularExpressions.Regex.Replace(content, @"\n\s*\n+", "\n").Trim()).Append("\n\n");
            Console.WriteLine($"    [{i,2}] {Truncate(descs[i].ReplaceLineEndings(" "), 68)}");
        }
        File.WriteAllText(args[2], sb.ToString());
        Console.WriteLine($"  → distilled memory → {args[2]} (re-bench it vs the original intents to measure the lift)");
        return 0;
    }

    // ── normalize ──  SEMANTIC NORMALIZATION (E1, the caveman test). The LLM rewrites English into a canonical
    // telegraphic "grug" form — collapse synonyms to ONE word, strip function words, base-form morphology — so the
    // surface variance Re-Pair CANNOT see through (the synonymy wall that capped RAG at 16–32%) is removed. THE
    // question: does the regularized stream GROK (reach a critical scale-invariant fixed point) where raw English
    // — stuck at a characteristic scale — never did? grok/renorm read the verdict straight off the output.
    private static int Normalize(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1])) { Console.Error.WriteLine("  usage: normalize <english-file> <out-file>"); return 1; }
        bool each = args.Contains("--each");                                 // --each: normalize each \n\n-passage independently (preserve boundaries for bench/retrieval)
        var raw = File.ReadAllText(args[1]);
        List<string> units;
        if (each) units = raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 3).ToList();
        else { var flat = raw.ReplaceLineEndings(" "); units = []; for (int i = 0; i < flat.Length; i += 1200) units.Add(flat.Substring(i, Math.Min(1200, flat.Length - i))); }
        var codex = CodexLlm.Default;
        const string sys = "You normalize English prose into a canonical TELEGRAPHIC 'caveman' form. Rules: collapse synonyms to ONE canonical content word (watching/gazing/observing/monitoring→watch, big/large/huge→big); DROP function words (the, a, an, of, was, were, is, are, to, that, and); keep only content words (nouns/verbs/adjectives) in subject-verb-object order; canonicalize morphology to base form (ran/running→run, cats→cat). Preserve the meaning and the sequence of events. Output ONLY the normalized telegraphic words, lowercase, space-separated, no punctuation, no commentary.";
        var sb = new StringBuilder();
        Console.WriteLine($"normalize · {units.Count} {(each ? "passages" : "chunks")} · English → canonical caveman (collapse synonyms, strip function words)");
        for (int i = 0; i < units.Count; i++)
        {
            var norm = codex.Complete(sys, units[i].Length > 1500 ? units[i][..1500] : units[i]);
            sb.Append(norm.Trim()).Append(each ? "\n\n" : " ");
            if (i < 3) Console.WriteLine($"    [{i}] {Truncate(norm.ReplaceLineEndings(" "), 72)}");
        }
        File.WriteAllText(args[2], sb.ToString());
        Console.WriteLine($"  → normalized → {args[2]} ({sb.Length} chars){(each ? $" · {units.Count} passages preserved" : "")} — grok/renorm vs the raw baseline, or bench it");
        return 0;
    }

    // ── kill ──  THE KILL-LINE (the one discipline that paid for the neural sibling). A pre-registered,
    // matched-budget, held-out A/B: induce a grammar per arm-corpus, score each by held-out coverage, rank, and
    // report the gain of the candidate (first arm) over the baseline (last arm). "survived" iff the gain clears
    // the noise floor. Gate every lever on a yield readout here — never on a signal the active mode suppresses.
    private static int Kill(string[] args)
    {
        if (args.Length < 4 || !File.Exists(args[1])) { Console.Error.WriteLine("  usage: kill <held-out-file> <label:train-file> <label:train-file> ...   (first arm = candidate, last = baseline)"); return 1; }
        var heldOut = File.ReadAllBytes(args[1]);
        var arms = args[2..]
            .Select(a => { var p = a.Split(':', 2); return (Label: p[0], File: p.Length > 1 ? p[1] : p[0]); })
            .Where(a => File.Exists(a.File)).ToList();
        if (arms.Count < 2) { Console.Error.WriteLine("  need ≥2 arms (candidate + baseline)"); return 1; }
        Console.WriteLine($"kill · matched-budget held-out A/B · held-out {heldOut.Length}B · {arms.Count} arms");
        var results = new List<(string Label, double Cov, int Rules, int TrainB)>();
        foreach (var (label, file) in arms)
        {
            var train = File.ReadAllBytes(file);
            var (_, _, r) = Engine.Induce(train);
            results.Add((label, Engine.CoverageOf(r.Rules, heldOut), r.Rules.Length, train.Length));
        }
        var spread = results.Max(x => x.TrainB) - results.Min(x => x.TrainB);
        if (spread > results.Min(x => x.TrainB) * 0.15) Console.WriteLine($"  ⚠ train budgets differ by {spread}B (>15%) — NOT matched-budget; the A/B is confounded by corpus size");
        foreach (var x in results.OrderByDescending(x => x.Cov))
            Console.WriteLine($"    {x.Cov,7:P1}  {x.Label,-18}  ({x.Rules} rules · {x.TrainB}B train)");
        double cand = results[0].Cov, baseline = results[^1].Cov, gain = cand - baseline;
        Console.WriteLine($"  → candidate {results[0].Label} {cand:P1} vs baseline {results[^1].Label} {baseline:P1} · gain {(gain >= 0 ? "+" : "")}{gain:P1} · {(gain > 0.01 ? "SURVIVED" : "NO YIELD (fired-but-didn't-yield — do not build on it)")}");
        return 0;
    }

    // ── rl ──  post-training RL analogue (RLVR). The VERIFIER is codex — the same LLM that cold-read at associative stage
    // is now the reward model. cogito generates rollouts → codex scores them against a criterion → the
    // above-average ones are kept and re-induced (anchored on the seed). The policy (grammar) drifts toward
    // output codex rates higher. "Value flows wherever a cheap sound verifier exists" — codex is the verifier.
    private static int Rl(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("  usage: rl <corpus> <criterion>   (e.g. rl sql.txt \"valid idiomatic SQL\")"); return 1; }
        var criterion = args[^1];
        var seed = LoadCorpus(args);
        var codex = CodexLlm.Default;
        var (_, _, g) = Engine.Induce(seed);
        var run = Run.New($"rl-{Slug(criterion)}");
        Console.WriteLine($"rl · seed {seed.Length}B · VERIFIER = codex scoring \"{criterion}\" (0-10) · generate→score→select→re-induce");
        Console.WriteLine("    round  rules   reward (codex mean 0-10)");
        var rows = new List<string>();
        const int K = 6;
        for (int round = 0; round <= 5; round++)
        {
            var cands = new List<byte[]>();
            for (int c = 0; c < K; c++) cands.Add(Engine.Generate(g, 220, 0xCA00UL + (ulong)(round * 100 + c)));
            var scores = ScoreBatch(codex, criterion, cands);
            double reward = scores.Average();
            Console.WriteLine($"    {round,5}  {g.Rules.Length,5}   {reward,6:F1}   [{string.Join(" ", scores.Select(s => $"{s:F0}"))}]");
            rows.Add($"{round}\t{g.Rules.Length}\t{reward:F3}");
            if (round == 0 || round == 5)
            {
                int bi = 0; for (int c = 1; c < K; c++) if (scores[c] > scores[bi]) bi = c;
                Console.WriteLine($"      best (scored {scores[bi]:F0}): {Truncate(Encoding.UTF8.GetString(cands[bi]), 160)}");
            }
            if (round == 5) break;
            var kept = new List<byte>(seed);                                   // anchor on real data
            for (int c = 0; c < K; c++) if (scores[c] >= reward) kept.AddRange(cands[c]);   // keep above-mean rollouts
            if (kept.Count > 60000) kept = kept.Take(60000).ToList();
            g = Engine.Induce(kept.ToArray()).Result;
        }
        run.Write("config.txt", $"rl · seed {seed.Length}B · verifier=codex · criterion=\"{criterion}\"\n");
        run.WriteCurve("curve.tsv", "round\trules\treward\n" + string.Join("\n", rows) + "\n");
        return 0;
    }

    // ── grow ──  the loop closed. PRETRAINING: the LLM generates corpus, cogito induces grammar. Then each
    // round cogito reads its OWN grammar (what it learned + its hunger) and DIRECTS the next request — the
    // hunt, prompting in correlation with the grammar. The probes draw the learning curve: growth + Zipf
    // health + new-rules/round declining toward homeostasis (the domain saturated).
    private static int Grow(string[] args)
    {
        int rounds = 3;
        var words = args.Skip(1).ToList();
        bool self = words.Remove("--self");                  // autoregressive: re-observe cogito's own mutations
        if (words.Count > 0 && int.TryParse(words[^1], out var rr)) { rounds = Math.Clamp(rr, 1, 12); words.RemoveAt(words.Count - 1); }
        var seed = words.Count > 0 ? string.Join(' ', words) : "short idiomatic python functions";

        var codex = CodexLlm.Default;
        Console.WriteLine($"grow · \"{seed}\" · {rounds} rounds  (LLM feeds → cogito induces → cogito directs{(self ? " → RE-OBSERVES its mutations" : "")})");

        const string genSys = "You feed a grammar-induction engine. Diverse, REAL, self-contained examples. Raw only — no numbering, commentary, or fences.";

        // Held-out validation set — an UNtargeted domain sample, generated once up front. cogito never trains
        // on it; we measure how many of ITS rules the growing grammar already knows (RuleID overlap — the
        // content-addressed codec identity). Rising coverage of unseen samples = generalizing, not memorizing.
        Console.WriteLine("  generating held-out validation sample…");
        var heldout = Encoding.UTF8.GetBytes(
            codex.Complete(genSys, $"Generate 12 typical, random examples of: {seed}\nSeparate each with a single blank line."));

        var corpus = new List<byte>();
        var curve = new List<(int Round, int Bytes, int Rules, double Comp, double Zipf, int NewRules, double Gen)>();
        int prevRules = 0;
        Cogito.Induct.RePairResult lastGrammar = default;

        for (int round = 0; round <= rounds; round++)
        {
            string user;
            if (round == 0)
                user = $"Generate 20 short examples of: {seed}\nSeparate each with a single blank line.";
            else
            {
                var (_, _, g) = Engine.Induce(corpus.ToArray());     // cogito reads its own grammar to direct
                var u = RuleUses(g);
                var learned = Join(g, u, Enumerable.Range(0, g.Rules.Length).OrderByDescending(i => u[i]).Take(8));
                Console.WriteLine($"  round {round}: cogito directs · knows [{learned}] → demands new STRUCTURE");
                user = $"Generate 20 MORE examples of: {seed}\n"
                     + $"A grammar-learner has already absorbed these recurring structural patterns: [{learned}].\n"
                     + "Maximize STRUCTURAL COVERAGE: span the full breadth of COMMON query/clause shapes it has "
                     + "not yet shown — every typical JOIN, aggregation, subquery, and filter idiom. Reuse the SAME "
                     + "common identifiers and literals; vary ONLY the structure. Stay idiomatic and typical, not exotic.\n"
                     + "Separate each with a single blank line.";
            }

            var gen = codex.Complete(genSys, user);
            if (gen.Length == 0) { Console.Error.WriteLine($"  round {round}: LLM returned empty"); return 1; }
            corpus.AddRange(Encoding.UTF8.GetBytes(gen));
            if (self && round > 0) corpus.AddRange(SelfText(lastGrammar));   // the mutation, minted as an observation

            var arr = corpus.ToArray();
            var (_, n, r) = Engine.Induce(arr);
            double comp = n == 0 ? 0 : (double)global::Cogito.LlmCodec.FloorBudget(arr) / n;   // global:: — the `LlmCodec()` builder shadows the engine class
            double zipf = ZipfSlope(r);
            int newRules = r.Rules.Length - prevRules; prevRules = r.Rules.Length;
            double cov = Engine.CoverageOf(r.Rules, heldout);            // grammar applied to UNSEEN text
            curve.Add((round, n, r.Rules.Length, comp, zipf, newRules, cov));
            Console.WriteLine($"  round {round}: {(round == 0 ? "seed corpus" : "+corpus    ")}  {n,6} B → {r.Rules.Length,4} rules (+{newRules,3})  {comp,5:P0}  zipf {zipf:F2}");
            lastGrammar = r;
        }

        Directory.CreateDirectory("data/grown");
        var path = $"data/grown/{Slug(seed)}.txt";
        File.WriteAllBytes(path, corpus.ToArray());

        Console.WriteLine($"  ── learning curve · corpus {corpus.Count} B → {path} ──");
        Console.WriteLine("    round   bytes  rules  +new  in-sample   zipf   held-out");
        foreach (var c in curve)
            Console.WriteLine($"    {c.Round,5}  {c.Bytes,6}  {c.Rules,5}  {c.NewRules,4}  {c.Comp,9:P0}  {c.Zipf,6:F2}  {c.Gen,8:P0}");
        if (curve.Count >= 2)
        {
            Console.WriteLine($"  in-sample : grammar compresses SEEN corpus {curve[0].Comp:P0}→{curve[^1].Comp:P0} "
                            + (curve[^1].Comp < curve[0].Comp - 0.02 ? "(tightening — fitting the corpus)" : "(plateaued)"));
            Console.WriteLine($"  held-out  : grammar COVERS unseen text {curve[0].Gen:P0}→{curve[^1].Gen:P0} "
                            + (curve[^1].Gen > curve[0].Gen + 0.03 ? "(rising — GENERALIZING to the domain's structure)" : "(flat — fitting content, not structure)"));
        }
        return 0;
    }
}

}  // namespace Cogito.Cli
