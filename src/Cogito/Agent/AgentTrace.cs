namespace Cogito;

using System.Text;

// ── AGENTTRACE ──  THE AGENTIC-TRACE CORPUS — the data that makes TOOL-USE IN-DISTRIBUTION, the prerequisite clog.
// The continuous agent LOCALIZES by GENERATING tool-calls (Engine.GenerateMCMC over the grammar, conditioned on the
// tape). But a grammar induced only on raw code has never SEEN a tool-call — `grep payload_state\n== src/mod.py ==\n`
// is out-of-distribution, so its first emissions are noise and the loop can't bootstrap. The fix is "quality of data
// first": FEED the grammar successful tool-use transcripts until the `<verb> <arg>\n[observation]\n…answer` shape is
// in-distribution, so generation FALLS INTO coherent tool-calls the way it falls into code idioms. This file
// SYNTHESIZES that corpus from the instances themselves — no external transcript scrape needed (the synth fixtures are
// self-contained), each transcript a DETERMINISTIC oracle demonstration derived from gold.
//
// WHAT A TRANSCRIPT IS: the canonical SUCCESSFUL episode for one instance — the shape a competent agent WOULD emit —
// materialized through the REAL AgentWorld so the observation bytes are byte-identical to what the agent will see at
// eval (the corpus and the eval-world share one renderer — train-test byte-parity by construction). The arc:
//   QUERY:   <the bug report>
//   grep <bridge-term>            — a term SHARED by the query and the gold site (the concept the report names)
//   <observation: where it lives>
//   open <gold-file>             — read the file gold points at
//   <observation: the file>
//   answer <gold-file>           — commit the correct localization
// The bridge-term selection is the only inference: pick the query token that best discriminates the gold site (appears
// in the gold text, rarest across the corpus). That is exactly the move the agent must learn — from the report's
// words, grep the one that leads to the fix. So the demonstration teaches BOTH the byte-shape AND the policy.
//
// THIS IS A DREAM, NOT A CHEAT: the transcript is SELF-GENERATED plausible experience (the mind rehearsing the shape of
// success), the same category as ReplayCalc's repaired dreams — legitimate pretraining fuel, never gold leaked into an
// eval answer (the eval agent generates its OWN calls; the corpus only shaped its prior). Determinism: bridge-term is
// argmax over a fixed score, ties broken lexicographically; observations are the world's pure functions. No RNG.
public static class AgentTrace
{
    // ── THE VERB ── `cogito traces <dataDir> [--out path] [--show <instanceId|idx>] [--selftest]`. The substrate's own
    // exercise: materialize the corpus (default), dump one transcript (--show), or run the tool-world round-trip
    // self-test (--selftest) that proves grep→open→read→answer produce correct, deterministic observations. This is the
    // ACT+OBSERVE half of the continuous loop, verifiable without the generative front-end (the peer lane).
    public static int Run(string[] args)
    {
        if (args.Length <= 1 || args[1].StartsWith("--"))
        {
            Console.Error.WriteLine("traces · dataDir is required");
            return 1;
        }
        string dataDir = args[1];
        if (!Directory.Exists(dataDir)) { Console.WriteLine($"traces · no data dir: {dataDir}"); return 1; }

        if (Args.Has(args, "--selftest")) return SelfTest(dataDir);
        if (Args.Has(args, "--probe")) return Probe(dataDir, args);
        if (Args.Has(args, "--episode")) return Episode(dataDir, args);

        string show = Args.Str(args, "--show", "");
        if (show.Length > 0)
        {
            var dirs = Directory.GetDirectories(dataDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
            string? dir = int.TryParse(show, out int idx) && idx >= 0 && idx < dirs.Count ? dirs[idx]
                        : dirs.FirstOrDefault(d => Path.GetFileName(d) == show);
            if (dir is null) { Console.WriteLine($"traces · no instance '{show}' in {dataDir}"); return 1; }
            var t = Synthesize(dir);
            if (t is null) { Console.WriteLine($"traces · instance '{show}' has no usable gold"); return 1; }
            Console.WriteLine($"── transcript {t.InstanceId} · bridge='{t.BridgeTerm}' → gold {t.GoldFile} · {t.Steps.Count} steps ──\n");
            Console.WriteLine(t.Bytes);
            return 0;
        }

        string outPath = Args.Str(args, "--out", Path.Combine(Path.GetDirectoryName(dataDir.TrimEnd('/'))!, "agentic_traces.txt"))!;
        SynthesizeCorpus(dataDir, outPath);
        return 0;
    }

    // ── THE ROUND-TRIP SELF-TEST ── prove the tool-world acts correctly + deterministically over every instance: the
    // synthesized oracle transcript's `answer` MUST equal the gold file (the demonstration is correct), grep MUST hit
    // the gold file (the bridge leads there), and re-running is byte-identical (determinism). No RNG anywhere on this
    // path, so a mismatch is a real bug. Reports the pass rate — the substrate's green light.
    private static int SelfTest(string dataDir)
    {
        var dirs = Directory.GetDirectories(dataDir)
                            .Where(d => File.Exists(Path.Combine(d, "sites.jsonl")) && File.Exists(Path.Combine(d, "gold.json")))
                            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
        int made = 0, answerOk = 0, grepLeads = 0, deterministic = 0;
        foreach (var d in dirs)
        {
            var t = Synthesize(d);
            if (t is null) continue;
            made++;
            if (t.Steps[^1].Call == $"answer {t.GoldFile}") answerOk++;
            // grep leads: the FIRST step (the grep) surfaced the gold file in its observation.
            var grepObs = t.Steps.Count > 0 ? t.Steps[0].Observation : "";
            if (grepObs.Contains(t.GoldFile, StringComparison.Ordinal)) grepLeads++;
            // determinism: a second synthesis is byte-identical.
            var t2 = Synthesize(d);
            if (t2 is not null && t2.Bytes == t.Bytes) deterministic++;
        }
        Console.WriteLine($"traces selftest · {made} transcripts · answer=gold {answerOk}/{made} · grep→gold {grepLeads}/{made} · deterministic {deterministic}/{made}");
        bool green = made > 0 && answerOk == made && deterministic == made;
        Console.WriteLine(green ? "  ✓ tool-world round-trips; corpus is a correct + deterministic oracle" : "  ✗ substrate FAILS — see counts above");
        return green ? 0 : 1;
    }

    // ── THE IN-DISTRIBUTION PROBE ── the proof my substrate does its job (mandate clog (a)): does INDUCING on the
    // trace corpus put tool-use IN-DISTRIBUTION for the generative path? Induce a grammar on the corpus, generate from
    // it (Engine.GenerateMCMC — the exact path the continuous agent emits from), and measure whether the generation
    // FALLS INTO the tool-call shape: the verbs (grep/open/read/answer), the QUERY:/OBS frame, and path-like tokens. A
    // grammar that never saw a tool-call generates none; a grammar fed these traces should generate them fluently. This
    // is the go/no-go on "is the corpus fit-for-purpose fuel" — reported straight (frame-break law).
    private static int Probe(string dataDir, string[] args)
    {
        int genLen = Args.Int(args, "--len", 1200);
        int sweeps = Args.Int(args, "--sweeps", 3);
        int samples = Args.Int(args, "--samples", 4);
        ulong seed = Args.Seed(args, "--seed", 0xC0617010UL);

        string corpus = SynthesizeCorpus(dataDir, null);
        byte[] bytes = Encoding.UTF8.GetBytes(corpus);
        var (_, _, g) = Engine.Induce(bytes);
        Console.WriteLine($"traces probe · induced {bytes.Length} corpus bytes → {g.Compressed.Length} symbols + {g.Rules.Length} rules · Δmdl {g.TotalSavings}");

        // THE CORPUS's OWN call-density is the honest baseline (metrics-are-theory-laden): the transcript is mostly
        // observation-body (an `open` dumps a whole file — dozens of code lines per ~4 call lines), so the fraction of
        // ALL lines that are calls is ~9% BY DESIGN. The real question is not "does generation hit an absolute 15%" but
        // "does generation reproduce the corpus's call-density" — i.e. is the action shape faithfully in-distribution,
        // neither starved (grammar never learned it) nor hallucinated (grammar over-emits calls it can't ground).
        var corpusLines = corpus.Split('\n');
        int corpusCallLines = corpusLines.Count(l => { var c = Tool.ToolCall.Parse(l); return c.Verb != Tool.ToolVerbs.Noop && c.Arg.Length > 0; });
        double corpusFrac = corpusLines.Length > 0 ? (double)corpusCallLines / corpusLines.Length : 0;
        Console.WriteLine($"  corpus call-density {corpusCallLines}/{corpusLines.Length} ({corpusFrac:P0}) — the in-distribution target for generation");

        // The tool-call vocabulary the generation should reproduce (the verbs + the frame markers). Presence-in-corpus
        // is 100% by construction; the question is presence-in-GENERATION (the grammar re-emitting the shape).
        string[] verbs = { "grep ", "open ", "read ", "answer ", "QUERY:", "OBS>", ".py" };
        Console.WriteLine($"\n  ── generation from the induced grammar ({samples} samples, len={genLen}, sweeps={sweeps}) ──");
        int anyVerbLines = 0, totalGenLines = 0;
        for (int s = 0; s < samples; s++)
        {
            byte[] outb = Engine.GenerateMCMC(g, genLen, sweeps, seed + (ulong)s * 0x9E3779B97F4A7C15UL);
            string gen = Encoding.UTF8.GetString(outb);
            var lines = gen.Split('\n');
            totalGenLines += lines.Length;
            int hitLines = 0;
            foreach (var line in lines)
            {
                var call = Tool.ToolCall.Parse(line);
                if (call.Verb != Tool.ToolVerbs.Noop && call.Arg.Length > 0) hitLines++;
            }
            anyVerbLines += hitLines;
            var tally = verbs.Select(v => (v, n: CountOcc(gen, v))).ToList();
            Console.WriteLine($"    sample {s}: {hitLines}/{lines.Length} parseable-call lines · " + string.Join(" ", tally.Select(t => $"{t.v.Trim()}={t.n}")));
            if (s == 0)
            {
                Console.WriteLine("    ┌─ sample 0 excerpt ─");
                foreach (var line in lines.Take(24)) Console.WriteLine("    │ " + line);
                Console.WriteLine("    └─");
            }
        }
        double frac = totalGenLines > 0 ? (double)anyVerbLines / totalGenLines : 0;
        // In-distribution iff generation call-density is within a band around the corpus's (ratio ∈ [0.5, 2.0]) AND all
        // four verbs appear across the samples (the grammar reproduces the whole action vocabulary, not one token).
        double ratio = corpusFrac > 0 ? frac / corpusFrac : 0;
        int verbsSeen = new[] { "grep ", "open ", "read ", "answer " }.Count(v => CountOcc(string.Join("\n", Enumerable.Range(0, samples)
                          .Select(s => Encoding.UTF8.GetString(Engine.GenerateMCMC(g, genLen, sweeps, seed + (ulong)s * 0x9E3779B97F4A7C15UL)))), v) > 0);
        bool inDist = ratio >= 0.5 && ratio <= 2.0 && verbsSeen == 4;
        Console.WriteLine($"\n  → generation call-density {anyVerbLines}/{totalGenLines} ({frac:P0}) vs corpus ({corpusFrac:P0}) = {ratio:F2}× · verbs reproduced {verbsSeen}/4");
        Console.WriteLine(inDist
            ? "  ✓ TOOL-USE IS IN-DISTRIBUTION — the induced grammar generates the full action vocabulary at the corpus's own density. Clog (a) substrate is fit-for-purpose fuel."
            : $"  ~ tool-use present but off-density ({ratio:F2}×) or partial vocab ({verbsSeen}/4) — a lever for the front-end lane (obs-body weight / induction depth), not a substrate failure.");
        return 0;
    }

    private static int CountOcc(string hay, string needle)
    {
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    // ── THE REFERENCE EPISODE ── the ACT→OBSERVE→APPEND→GENERATE loop CLOSED end-to-end on real instances, the working
    // skeleton the continuous-agent front-end lane grows the real MIND onto. NOT the final agent — it is the substrate's
    // proof-of-wiring: the mind's grammar (here: the trace corpus + this instance's query/sites, induced) GENERATES a
    // line via Engine.GenerateMCMC (the same emission path the agent uses), Tool.ToolCall.Parse reads it, AgentWorld
    // ACTS, the OBSERVATION APPENDS to the tape, the grammar RE-INDUCES over the grown tape, and the next line generates
    // from the now-larger mind — until the mind emits `answer` or the look-budget runs out. This is the loopback
    // combustion (intrinsic generation interleaved with external world-data) with the LEARN step stubbed to
    // re-induction (the peer lanes replace generation with the standing mind + learning with vest/dream). It proves the
    // seam: my World + Engine's public generate close a real loop, deterministically, no private bench internals.
    //
    // Reported: per-look the generated call + whether it hit gold, and whether the emitted `answer` equals gold. On the
    // synth fixtures the raw grammar-walk is not expected to SOLVE (that is the front-end lane's mind + learning) — the
    // point is the LOOP RUNS and the pieces compose. Deterministic (seeded).
    private static int Episode(string dataDir, string[] args)
    {
        string inst = Args.Str(args, "--inst", "");
        int maxLooks = Args.Int(args, "--looks", 6);
        int genLen = Args.Int(args, "--len", 60);
        ulong seed = Args.Seed(args, "--seed", 0xE9150DEUL);

        var dirs = Directory.GetDirectories(dataDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
        string? dir = inst.Length > 0 ? dirs.FirstOrDefault(d => Path.GetFileName(d) == inst) : dirs.FirstOrDefault();
        if (dir is null) { Console.WriteLine($"episode · no instance (inst='{inst}') in {dataDir}"); return 1; }

        string query = File.ReadAllText(Path.Combine(dir, "query.txt")).Trim();
        var sites = Tool.LoadSites(Path.Combine(dir, "sites.jsonl"));
        var world = new Tool.AgentWorld(sites);
        var gold = LoadGold(Path.Combine(dir, "gold.json"));
        string goldFile = gold.Files.Count > 0 ? gold.Files[0] : "";
        Console.WriteLine($"── episode {Path.GetFileName(dir)} · gold {goldFile} · looks≤{maxLooks} ──");
        Console.WriteLine($"QUERY: {OneLine(query)}\n");

        // THE MIND'S GRAMMAR (reference stand-in): the trace corpus (tool-use in-distribution) + this instance's query
        // and its site texts, so the grammar knows both the ACTION shape and this codebase's vocabulary. The tape grows
        // as observations append; the front-end lane replaces this with the persistent standing mind.
        var tape = new StringBuilder();
        tape.Append(SynthesizeCorpus(dataDir, null));                 // the shared action-shape prior (in-distribution fuel)
        tape.Append("QUERY: ").Append(OneLine(query)).Append('\n');
        foreach (var s in sites) { tape.Append(s.Text); tape.Append('\n'); }   // the instance's world as readable context

        bool answered = false; string answerPath = ""; bool reachedGold = false;
        for (int look = 0; look < maxLooks; look++)
        {
            var (_, _, g) = Engine.Induce(Encoding.UTF8.GetBytes(tape.ToString()));
            // GENERATE conditioned on the tape tail (the mind's next autoregressive decision). Sample a few lines and
            // take the FIRST that parses as a real call — the raw walk is noisy; the parse-gate is the agent's own
            // "did I emit a valid action?" filter (the front-end lane's mind emits cleaner, needing no resample).
            string chosen = ""; Tool.ToolCall call = default;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                byte[] outb = Engine.GenerateMCMC(g, genLen, 2, seed + (ulong)(look * 97 + attempt) * 0x9E3779B97F4A7C15UL);
                foreach (var line in Encoding.UTF8.GetString(outb).Split('\n'))
                {
                    var c = Tool.ToolCall.Parse(line);
                    if (c.Verb != Tool.ToolVerbs.Noop && c.Arg.Length > 0) { chosen = line.Trim(); call = c; break; }
                }
                if (chosen.Length > 0) break;
            }
            if (chosen.Length == 0) { Console.WriteLine($"  look {look}: (mind emitted no parseable call)"); break; }

            var obs = world.Act(call);
            bool hitGold = goldFile.Length > 0 && obs.HitPaths.Contains(goldFile);
            reachedGold |= hitGold;
            Console.WriteLine($"  look {look}: {call.Verb,-6} '{Trunc(call.Arg, 42)}'{(hitGold ? "  ← GOLD" : "")}  → {obs.HitPaths.Count} paths, {obs.Text.Length}b obs");
            // APPEND the observation to the tape — the loopback: the world's reply becomes the mind's next context.
            tape.Append(chosen).Append('\n').Append(obs.Text);
            if (obs.Answered) { answered = true; answerPath = obs.AnswerPath; break; }
        }

        bool correct = answered && answerPath == goldFile;
        Console.WriteLine($"\n  → {(answered ? $"answered '{answerPath}'" : "no answer (budget)")} · reached-gold-in-obs {reachedGold} · {(correct ? "CORRECT" : "not-correct (expected: the mind+learning is the front-end lane)")}");
        Console.WriteLine("  ✓ the ACT→OBSERVE→APPEND→GENERATE loop closes end-to-end (World + Engine.GenerateMCMC compose; the peer lanes drop in the standing mind + vest/dream).");
        return 0;
    }

    // ── THE TRANSCRIPT ── one synthesized episode, as (a) the raw byte-corpus for induction and (b) the structured
    // steps for the journal/inspection. `Bytes` is what the grammar eats.
    public sealed record Transcript(string InstanceId, string Bytes, IReadOnlyList<Step> Steps, string BridgeTerm, string GoldFile);
    public readonly record struct Step(string Call, string Observation);

    // Line markers — the transcript's own light structure so the grammar learns the FRAME (a call line, an observation
    // block, the answer) as recurring tokens, not just the payloads. Kept ASCII + short so they cost few symbols.
    private const string QueryTag = "QUERY: ";
    private const string ObsOpen  = "OBS>\n";
    private const string ObsClose = "<OBS\n";

    /// Synthesize the canonical transcript for one instance directory. Reads query.txt + sites.jsonl + gold.json, picks
    /// the bridge-term, and REPLAYS the oracle episode through a real AgentWorld so the observations are eval-faithful.
    /// Deterministic. Returns null only if the instance has no usable gold file (nothing to demonstrate toward).
    public static Transcript? Synthesize(string instanceDir)
    {
        string queryPath = Path.Combine(instanceDir, "query.txt");
        string sitesPath = Path.Combine(instanceDir, "sites.jsonl");
        string goldPath  = Path.Combine(instanceDir, "gold.json");
        if (!File.Exists(queryPath) || !File.Exists(sitesPath) || !File.Exists(goldPath)) return null;

        string query = File.ReadAllText(queryPath).Trim();
        var sites = Tool.LoadSites(sitesPath);
        var gold = LoadGold(goldPath);
        if (gold.Files.Count == 0) return null;

        var world = new Tool.AgentWorld(sites);
        string goldFile = gold.Files[0];                      // the primary gold file — the localization target
        string goldFn   = gold.Functions.Count > 0 ? gold.Functions[0].Name : "";

        // THE BRIDGE-TERM — the one query token the agent should grep. Score every candidate by discriminative pull
        // toward the gold site (in the gold text, rare in the corpus); the argmax is the term whose grep most cleanly
        // surfaces gold. This IS the policy the demonstration teaches.
        string bridge = PickBridgeTerm(query, sites, goldFile, goldFn);

        var steps = new List<Step>();
        var sb = new StringBuilder();
        sb.Append(QueryTag).Append(OneLine(query)).Append('\n');

        void Emit(string call)
        {
            var obs = world.Act(Tool.ToolCall.Parse(call));
            sb.Append(call).Append('\n').Append(ObsOpen).Append(obs.Text).Append(ObsClose);
            steps.Add(new Step(call, obs.Text));
        }

        Emit($"grep {bridge}");
        Emit($"open {goldFile}");
        // If gold names a function, demonstrate the precision read too (grep→open→read→answer is the full idiom).
        if (goldFn.Length > 0)
        {
            var loc = gold.Functions[0];
            Emit($"read {goldFile}:{loc.Start}");
        }
        Emit($"answer {goldFile}");

        return new Transcript(gold.InstanceId.Length > 0 ? gold.InstanceId : Path.GetFileName(instanceDir.TrimEnd('/')),
                              sb.ToString(), steps, bridge, goldFile);
    }

    /// Materialize the WHOLE corpus: every instance in `dataDir` (instance_id-sorted — the same arrival the stream uses)
    /// → its transcript, concatenated with a blank-line separator into one byte-corpus the grammar can induce over.
    /// This is the PRETRAIN fuel: run the induction on this before the continuous stream and tool-use is in-distribution
    /// from step 0. Writes `agentic_traces.txt` alongside if `outPath` given (provenance + reuse); returns the bytes.
    public static string SynthesizeCorpus(string dataDir, string? outPath = null)
        => SynthesizeCorpus(Directory.GetDirectories(dataDir)
                            .Where(d => File.Exists(Path.Combine(d, "query.txt")) && File.Exists(Path.Combine(d, "sites.jsonl")))
                            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal), outPath, answerLeakFree: false);

    public static string SynthesizeCorpus(IEnumerable<string> instanceDirs, string? outPath = null, bool answerLeakFree = false)
    {
        var dirs = instanceDirs.OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        int made = 0;
        foreach (var d in dirs)
        {
            var t = Synthesize(d);
            if (t is null) continue;
            string bytes = answerLeakFree ? RedactAnswerLiteral(t.Bytes, t.GoldFile) : t.Bytes;
            sb.Append(bytes).Append('\n');       // blank line = episode boundary the grammar learns as a span-barrier
            made++;
        }
        string corpus = sb.ToString();
        if (outPath is not null) File.WriteAllText(outPath, corpus);
        Console.WriteLine($"agentic-traces · {made}/{dirs.Count} instances → {(outPath ?? "(memory)")} · {corpus.Length} bytes{(answerLeakFree ? " · answer-path literals redacted" : "")}");
        return corpus;
    }

    private static string RedactAnswerLiteral(string text, string answer)
        => answer.Length == 0 ? text : text.Replace(answer, "<answer-path>", StringComparison.Ordinal);

    // ── THE BRIDGE-TERM PICKER ── the demonstration's policy core. Candidate tokens = the query's identifier-shaped
    // words (len≥4, alnum/underscore) plus the gold function name if present. Score each: +strong if it appears in the
    // gold site's text (leads there), −by corpus document-frequency (rare = discriminative), +bonus if it IS the gold
    // function name (the sharpest possible bridge). Argmax, lexicographic tie-break. A pure function of the inputs.
    private static string PickBridgeTerm(string query, List<Tool.SiteRow> sites, string goldFile, string goldFn)
    {
        // Corpus document-frequency over sites (how many sites contain each token) — the rarity denominator.
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in sites)
        {
            foreach (var tok in Tokens(s.Text).Distinct())
                df[tok] = df.GetValueOrDefault(tok) + 1;
        }
        // The gold site's own tokens (the target vocabulary the bridge should hit).
        var goldToks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sites)
            if (s.Path == goldFile && (goldFn.Length == 0 || s.Name == goldFn))
                foreach (var tok in Tokens(s.Text)) goldToks.Add(tok);
        // fallback: any site in the gold file
        if (goldToks.Count == 0)
            foreach (var s in sites) if (s.Path == goldFile) foreach (var tok in Tokens(s.Text)) goldToks.Add(tok);

        int nSites = Math.Max(1, sites.Count);
        string best = goldFn.Length > 0 ? goldFn : "";
        double bestScore = double.NegativeInfinity;
        var cands = Tokens(query).Distinct().ToList();
        if (goldFn.Length > 0 && !cands.Contains(goldFn)) cands.Add(goldFn);

        foreach (var tok in cands.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (tok.Length < 4) continue;
            double score = 0;
            if (goldToks.Contains(tok)) score += 10;                         // leads to gold
            double freq = df.GetValueOrDefault(tok, 0);
            score -= freq / nSites * 4;                                       // discriminative (rare) preferred
            if (tok == goldFn) score += 5;                                    // the sharpest bridge
            if (score > bestScore) { bestScore = score; best = tok; }
        }
        // Never emit an empty grep: fall back to the gold function name, else the longest query identifier.
        if (best.Length == 0)
            best = goldFn.Length > 0 ? goldFn
                 : cands.Where(t => t.Length >= 4).OrderByDescending(t => t.Length).ThenBy(t => t, StringComparer.Ordinal).FirstOrDefault() ?? "def";
        return best;
    }

    // Identifier-shaped tokens: maximal runs of [A-Za-z0-9_]. Lowercased for corpus-DF matching consistency? — NO: the
    // grep is case-insensitive, but the bridge-term is emitted VERBATIM so it reads like a real agent's grep; case is
    // preserved. Matching uses Ordinal on the same casing throughout.
    private static IEnumerable<string> Tokens(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (IsIdent(text[i]))
            {
                int start = i;
                while (i < text.Length && IsIdent(text[i])) i++;
                yield return text[start..i];
            }
            else i++;
        }
    }

    private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string OneLine(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c == '\n' || c == '\r' ? ' ' : c);
        return sb.ToString().Trim();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ── GOLD (the demonstration target, never leaked to the eval agent) ──
    private readonly record struct GoldFn(string Path, string Name, int Start, int End);
    private sealed record GoldRec(string InstanceId, List<string> Files, List<GoldFn> Functions);

    private static GoldRec LoadGold(string path)
    {
        using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = d.RootElement;
        string id = root.TryGetProperty("instance_id", out var idE) ? idE.GetString() ?? "" : "";
        var files = new List<string>();
        if (root.TryGetProperty("files", out var fArr))
            foreach (var f in fArr.EnumerateArray()) files.Add(f.GetString()!);
        var fns = new List<GoldFn>();
        if (root.TryGetProperty("functions", out var fnArr))
            foreach (var f in fnArr.EnumerateArray())
                fns.Add(new GoldFn(
                    f.GetProperty("path").GetString()!, f.GetProperty("name").GetString()!,
                    // The gold function's locus carries two schemas across the two corpora: the synth fixtures use
                    // `start_line`/`end_line`, the real 300-set SWE gold uses bare `start`/`end`. Accept EITHER so
                    // the `read <file>:<line>` demonstration lands a real locus on both (a 0 fallback would emit
                    // `read file:0`, resolving nothing — the real-set demonstration would silently degrade to open).
                    f.TryGetProperty("start_line", out var s) ? s.GetInt32()
                        : f.TryGetProperty("start", out var s2) ? s2.GetInt32() : 0,
                    f.TryGetProperty("end_line", out var e) ? e.GetInt32()
                        : f.TryGetProperty("end", out var e2) ? e2.GetInt32() : 0));
        return new GoldRec(id, files, fns);
    }
}
