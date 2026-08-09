namespace Cogito;

using System.Text;

// The LOC environment maps issue-localization worlds onto Cortex. LocCurriculum supplies contact, tools execute
// observations, LocActionPolicy routes provenance-bound procedures, and FilePathAnswerReward vests successful
// walks. The shared tape, grammar, homeostats, aestivation, checkpoints, and held-out forks remain Cortex-owned.
public static partial class AgentSolve
{
    /// `cogito solve [<dataDir>] [--looks N] [--len N] [--sweeps N] [--seed hex] [--limit N] [--pretrain 0|1]
    ///              [--mesh-homeo] [--verify-durability]`.
    /// The continuous Cortex over the supplied instance stream. Deterministic (seeded); C#-canonical; release-only.
    /// A run IS a run: the engine mints runs/solve_NNNN/ and logs every step into it — config + journal.log (the
    /// per-instance record) + curve.tsv (the running commit metrics) + rankings.jsonl (the per-instance results) +
    /// report.txt (the verdict). No --out, no stdout-scraping; the verb prints only where it landed.
    public static int Run(string[] args)
    {
        if (Args.Has(args, "--verify-durability")) return VerifyDurability();
        if (args.Length <= 1 || args[1].StartsWith("--"))
        {
            Console.Error.WriteLine("solve · dataDir is required");
            return 1;
        }
        string dataDir = args[1];
        if (Args.Has(args, "--probe-index")) return ProbeIndex(dataDir);
        if (!Directory.Exists(dataDir)) { Console.WriteLine($"solve · no data dir: {dataDir}"); return 1; }

        SolveOpts opt = SolveOpts.Parse(args);
        List<string> feedRoots = new() { dataDir };
        feedRoots.AddRange(CollectFeeds(args));
        List<string> dirs = new();
        foreach (string root in feedRoots)
        {
            if (!Directory.Exists(root)) { Console.WriteLine($"solve · feed root missing (skipped): {root}"); continue; }
            dirs.AddRange(Directory.GetDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, "query.txt")) && File.Exists(Path.Combine(d, "sites.jsonl")) && File.Exists(Path.Combine(d, "gold.json"))));
        }
        dirs = opt.Interleave ? Interleave(dirs) : dirs.OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
        if (opt.Limit > 0 && dirs.Count > opt.Limit) dirs = dirs.Take(opt.Limit).ToList();
        if (dirs.Count == 0) { Console.WriteLine($"solve · no usable instances in {dataDir}"); return 1; }
        LocHeldoutExperiment? heldoutExperiment = null;
        int passSize = 0;
        int effectivePasses = opt.Passes;
        if (opt.Heldout > 0)
        {
            if (dirs.Count < 2) { Console.Error.WriteLine("solve · held-out transfer requires at least two instances"); return 1; }
            int heldoutCount = Math.Clamp(opt.Heldout, 1, dirs.Count - 1);
            int revisitedCount = opt.Revisited > 0 ? Math.Clamp(opt.Revisited, 1, dirs.Count - heldoutCount) : dirs.Count - heldoutCount;
            List<string> revisited = dirs.Take(revisitedCount).ToList();
            List<string> heldout = dirs.Skip(revisitedCount).Take(heldoutCount).ToList();
            int replayBudget = revisited.Count * opt.LooksCap;
            opt = opt with { MeshHomeo = true, MixSpans = replayBudget, Passes = 1, Interleave = false };
            dirs = new List<string>(revisited.Count * 2);
            dirs.AddRange(revisited);
            dirs.AddRange(revisited);
            passSize = revisited.Count;
            effectivePasses = 2;
            heldoutExperiment = new LocHeldoutExperiment(opt, heldout);
        }
        else if (opt.Passes > 1)
        {
            List<string> firstPass = dirs.ToList();
            dirs = new List<string>(firstPass.Count * opt.Passes);
            for (int p = 0; p < opt.Passes; p++) dirs.AddRange(firstPass);
        }

        string commitMode = $"calibration-homeostat (setpoint calibration error -> 0, actions {opt.Looks}→cap {opt.LooksCap})";
        string feedDesc = $"{feedRoots.Count} root(s) · {(opt.Interleave ? "INTERLEAVED-repos" : "id-sorted")} · {effectivePasses} pass(es)";
        Console.WriteLine($"── solve · {dirs.Count} instance(s) · {Path.GetFileName(dataDir.TrimEnd('/'))} · commit={commitMode} len={opt.Len} sweeps={opt.Sweeps} · pretrain={opt.Pretrain} ──");
        Console.WriteLine($"── COMBUSTION · feed[{feedDesc}] · bindings={(opt.ShuffleBindings ? "provenance-shuffled" : "procedure-routed")} · hold[mesh-homeo={opt.MeshHomeo} floor={opt.MeshFloor:F2} gain={opt.MeshGain:F2}] · diet[affirm-gated mix-spans={opt.MixSpans}/aestivation homeostat] ──");
        Console.WriteLine($"   {"#",-4} {"instance",-30} {"gold",-34} {"commit",-34} {"act",4} {"conf",5} {"s@c%",6} {"abs%",6}  reached cortex");

        LocCurriculum curriculum = new(dirs, opt, passSize, heldoutExperiment);
        CortexConfig config = BuildSolveConfig(curriculum, opt, ComputeSolveStepBudget(dirs.Count, opt), "solve");
        Cortex cortex = new(config);
        return cortex.Run();
    }

    private static CortexConfig BuildSolveConfig(LocCurriculum curriculum, SolveOpts opt, int steps, string runName)
        => new()
        {
            RunName = runName,
            Steps = steps,
            Seed = opt.Seed,
            Curriculum = new CortexLocCurriculum
            {
                WorkloadCount = curriculum.WorkloadCount,
            },
            RuntimeCurriculum = curriculum,
            ActionsPerStep = Math.Max(1, opt.LooksCap),
            Tools = new List<CortexTool>
            {
                new GrepTool(curriculum),
                new OpenTool(curriculum),
                new ReadTool(curriculum),
                new LsTool(curriculum),
                new AnswerTool(curriculum),
                new RecallTool(curriculum),
                new IndexTool(curriculum),
            },
            ActionPolicies = new List<CortexActionPolicy> { new LocActionPolicy(curriculum) },
            Rewards = new List<CortexReward> { new FilePathAnswerReward(curriculum) },
            Generation = new CortexGenerationConfig { BlockLength = opt.Len },
            Learning = new CortexLearningConfig
            {
                ConsolidationPhaseControl = CortexConsolidationPhaseControl.Homeostat,
                EvidenceWeightScale = 8,
                CrossReflect = true,
                ReplayRatio = 1.0,
                NearDupe = true,
                Antiunify = true,
                Loom = true,
                Shed = true,
                Homeostat = new CortexHomeostatConfig
                {
                    Policy = HomeoPolicies.Predict,
                    Autonomy = HomeostatAutonomyModes.Full,
                },
            },
            Durability = new CortexDurabilityConfig { CheckpointEvery = opt.CheckpointEvery },
        };

    internal static Cortex CreateCheckpointRuntime(CortexRunConfig config, int workloadCount, string? runDir)
    {
        SolveOpts options = new(
            Looks: CortexConfigTokens.ResolveActionsPerStep(config), LooksCap: CortexConfigTokens.ResolveActionsPerStep(config),
            Len: config.BlockLen, Sweeps: 2, Seed: config.Seed, Limit: 0, Pretrain: false,
            MeshHomeo: config.ConsolidationPhaseControl == CortexConsolidationPhaseControl.Homeostat,
            SiteBudget: 48, ConfidenceTrace: false, ExplainRank: false,
            MeshFloor: 0.05, MeshGain: 0.30, MixSpans: 0,
            Passes: 1, Interleave: false, CheckpointEvery: config.CheckpointEvery,
            AnswerLeakFree: false, ShuffleBindings: false, Heldout: 0, Revisited: 0);
        List<string> directories = string.IsNullOrWhiteSpace(runDir)
            ? new List<string>()
            : LocCurriculum.LoadInputDirectories(runDir);
        LocCurriculum curriculum = directories.Count > 0
            ? new LocCurriculum(directories, options)
            : new LocCurriculum(workloadCount, options);
        return new Cortex(BuildSolveConfig(curriculum, options, config.Steps, config.RunName));
    }

    private static int ComputeSolveStepBudget(int episodeCount, SolveOpts opt)
        => Math.Max(1, episodeCount) + 2;

    /// The repo an instance belongs to — the instance-id prefix before the "__" separator (`astropy__astropy-12907`
    /// → `astropy`; the synth `synth0__pkg-0000` → `synth0`). The compound axis groups by this: a Cortex that
    /// accumulated one repo's structure should nail the next instance of the SAME repo.
    private static string ParseRepo(string instanceId)
    {
        int sep = instanceId.IndexOf("__", StringComparison.Ordinal);
        return sep > 0 ? instanceId[..sep] : instanceId;
    }

    /// THE FEED ROOTS — every `--feed <dir>` on the command line (repeatable), the extra codebases streamed alongside
    /// the positional dataDir. This is what makes the feed a STREAM OF DIFFERENT codebases (the perpetual-novel-real the
    /// criticality law demands): point one --feed at synth, another at a real slice, and Cortex consumes a varied world stream.
    private static List<string> CollectFeeds(string[] args)
    {
        var feeds = new List<string>();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--feed") feeds.Add(args[i + 1]);
        return feeds;
    }

    /// INTERLEAVE the pool ACROSS repos — round-robin so consecutive instances are DIFFERENT codebases (astropy₀,
    /// django₀, sympy₀, …, astropy₁, django₁, …). The varied-feed order: a single repo's near-duplicate gold mass never
    /// arrives back-to-back, so it cannot renormalize the grammar flat the way the id-sorted clustering does (the plateau
    /// diagnosis: accumulated near-dup gold drifts meanz to −0.923). Deterministic — repos ordered by key, instances by
    /// id within each repo, then drawn in rounds until every bucket is drained.
    private static List<string> Interleave(List<string> dirs)
    {
        var byRepo = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var d in dirs.OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
            (byRepo.TryGetValue(ParseRepo(Path.GetFileName(d)), out var l) ? l : byRepo[ParseRepo(Path.GetFileName(d))] = new()).Add(d);
        var buckets = byRepo.Values.ToList();
        var order = new List<string>(dirs.Count);
        for (int round = 0; order.Count < dirs.Count; round++)
            foreach (var b in buckets) if (round < b.Count) order.Add(b[round]);
        return order;
    }

    // ── PROBE-INDEX ──  the coordinator's load-bearing BASIC (question a): is the grammar-index / tape-index
    // SEARCHABLE right now? Can Cortex FIND things in its own tape via the substrate's hub structures? Build the
    // MemoryWorld over a populated tape (the pretrain corpus + a synthesized navigation), then RECALL known content
    // and assert the hits come back. This proves the hippocampus works BEFORE the solve loop leans on it. Two
    // retrieval shapes: exact-substring containment (GramPostings) + associative near-match (SimhashIndex).
    private static int ProbeIndex(string dataDir)
    {
        Console.WriteLine("── probe-index · is the Cortex tape a SEARCHABLE index? (the hippocampus basic — question a) ──");
        int fails = 0;
        void Check(bool ok, string name, string detail) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "✓" : "✗ FAIL")}  {name,-26} {detail}"); }

        var tape = new Tape();
        tape.MountLog(new MemoryStream());
        // Populate the tape the way the solve stream does: the agentic-trace corpus (Real) + a synthesized navigation
        // span (Replay) that greps a distinctive term and finds a distinctive path — the shape `recall` must surface.
        string corpus = Directory.Exists(dataDir) ? AgentTrace.SynthesizeCorpus(dataDir, null) : "";
        int seeded = 0;
        foreach (var line in corpus.Split('\n')) { if (line.Length == 0) continue; tape.Append(Encoding.UTF8.GetBytes(line), "corpus", Provenances.Real); seeded++; }
        // a distinctive navigation Cortex emitted in a prior instance — the compounding memory a later recall reads.
        var navBytes = Encoding.UTF8.GetBytes("grep handle_cursor_residual\nsrc/mod_registry_0.py:1: def handle_cursor_residual(self, cursor, cache):\nanswer src/mod_registry_0.py");
        tape.Append(navBytes, "node0", Provenances.Replay);

        var mem = new Tool.MemoryWorld(tape);
        var idxObs = mem.Index();
        Console.WriteLine("  " + idxObs.Text.Replace("\n", "\n  ").TrimEnd());
        Check(mem.IndexedEvents == seeded + 1 && mem.GramKeys > 0, "index-built", $"indexed {mem.IndexedEvents} spans · {mem.GramKeys} gram-keys (the tape IS mapped)");

        // (1) EXACT recall — a literal term the navigation contains. The containment index must surface the nav span.
        var exact = mem.Recall("handle_cursor_residual", 4096);
        bool exactOk = exact.Text.Contains("registry_0") || exact.HitPaths.Contains("src/mod_registry_0.py");
        Check(exactOk, "recall-exact", $"recall('handle_cursor_residual') surfaced the navigation + its path (hits={exact.HitPaths.Count})");

        // (2) ASSOCIATIVE recall — a query that shares codebase VOCABULARY with the navigation (a real follow-up
        // re-uses the same identifiers) but isn't a substring of it: the shingle-affinity index surfaces the related
        // memory by shared 5-gram windows. This is the "what have I seen like this" retrieval — coarser than exact
        // (simhash is near-dupe, not semantic), but it fires when the query genuinely rhymes with a past navigation.
        var assoc = mem.Recall("def handle_cursor_residual cache lookup", 4096);
        Check(assoc.HitPaths.Count > 0 || assoc.Text.Contains("registry"), "recall-associative", $"a vocabulary-sharing query surfaced related memory (hits={assoc.HitPaths.Count})");

        // (3) the PATH-SURFACING contract — a recall's HitPaths carry the file the recalled navigation named, so the
        // outer loop can reach-gold / answer directly off memory (the compounding shortcut).
        Check(exact.HitPaths.Contains("src/mod_registry_0.py"), "recall-surfaces-path", "the recalled navigation's gold path flows into HitPaths (the direct-answer shortcut)");

        Console.WriteLine(fails == 0
            ? "✓ THE HIPPOCAMPUS IS SEARCHABLE — the Cortex can FIND things in its own tape (exact + associative), and recall surfaces the path a prior navigation led to. The index/recall tool-calls have a real substrate. Question (a): YES."
            : $"✗ {fails} failure(s) — the tape-index is not fully searchable; the compounding cannot lean on recall until this holds.");

        // The index is INCREMENTAL (O(Δ)/recall, not the O(tape)/recall from-scratch rebuild). It is a search
        // ACCELERATOR — it must change HOW FAST, never WHAT: prove byte-identity vs from-scratch (growth + evacuation)
        // and report the O(tape)→O(Δ) speedup here, so the searchability probe also gates the accelerator's determinism.
        Console.WriteLine();
        fails += Tool.MemoryWorld.VerifyIncremental();
        Console.WriteLine();
        fails += MemoryHierarchy.VerifyCheckpointIndex();
        Console.WriteLine();
        fails += MemoryHierarchy.VerifyCheckpointParadigm();
        Console.WriteLine();
        Tool.MemoryWorld.TimeIncremental();
        return fails == 0 ? 0 : 1;
    }

    // ── VERIFY-DURABILITY ──  the coordinator's explicit demand: does a VESTED codebase-observation survive N aestivations of
    // shed? The whole continual-learning thesis rests on vest=permanence, so PROVE it directly, not by inference.
    // Build a tape, vest an evidence-span, run N shed-aestivations (each dropping unvested dreams around it), assert the vested
    // span still RESOLVES (bytes) and still COUNTS (evidence) after every aestivation. Fail loud on any decay.
    private static int VerifyDurability()
    {
        Console.WriteLine("── verify-durability · the vest=permanence reliability contract (does earned memory survive the rot?) ──");
        int fails = 0;
        void Check(bool ok, string name, string detail) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "✓" : "✗ FAIL")}  {name,-28} {detail}"); }

        var tape = new Tape();
        tape.MountLog(new MemoryStream());
        // A vested codebase-observation: a code idiom that recurs across the world (as a real idiom does across a
        // codebase's sites) so RePair builds it up past the ≥8B vest floor. In the real solve stream this is automatic —
        // the gold file's idioms appear in many sites AND the navigation greps cite the same path repeatedly, so the
        // shared rule clears the floor. Here: several REAL world copies (the codebase's occurrences) + the Cortex's Replay
        // navigation citing the SAME idiom. The Real copies exercise the ≥8B rule; the Replay vests on that exercise.
        byte[] idiom = "def compose_cursor(self, ctx):"u8.ToArray();   // 30B — a real code idiom the world repeats
        byte[] CreateEvidence() { byte[] bytes = new byte[idiom.Length]; idiom.CopyTo(bytes, 0); return bytes; }
        TapeEventID evidenceReal = default;
        for (int w = 0; w < 12; w++) evidenceReal = tape.Append(CreateEvidence(), "corpus", Provenances.Real);   // the codebase's many occurrences (the world corroboration)
        TapeEventID navReplay = tape.Append(CreateEvidence(), "node0", Provenances.Replay);              // the Cortex's navigation citing the observation
        var (_, _, g) = Engine.Induce(tape);
        // The REAL-ONLY gate: a Real span exercising the rule vests its Replay supporters. The world's
        // many copies of the idiom exercise the ≥8B shared rule; the Cortex's navigation Replay citing it vests.
        var audit = Pearl.Audit(tape, g, 8, crossReflect: false);
        int vested = Pearl.Corroborate(audit, tape, new Journal(), 0);
        Check(vested == 1 && tape.IsEvidence(navReplay), "vest-the-evidence", $"the navigation Replay vested on the world's Real evidence (vested={vested})");

        // Now flood the tape with stale UNVESTED dreams (junk navigation the Cortex never corroborated) and run the
        // aestivation shed N times. Each aestivation drops the stale unvested dreams; the VESTED evidence must survive EVERY aestivation.
        var junk = new List<TapeEventID>();
        for (int j = 0; j < 4000; j++) junk.Add(tape.Append(Encoding.UTF8.GetBytes($"noop line {j} garbage navigation xyz"), "node0", Provenances.Replay));

        int aestivations = 8;
        bool survivedAll = true, resolvesAll = true, countsAll = true;
        for (int aestivation = 0; aestivation < aestivations; aestivation++)
        {
            // drop the oldest unvested dreams (the rot), keep the recent + all evidence — exactly the aestivation's law.
            // No grammar needed: the reliability core drops purely by PROVENANCE + AGE (an unvested Replay past the
            // turnover window), which is the exact predicate that must never touch an evidence span.
            var drop = new List<TapeEventID>();
            long dropBelow = tape.NextId - 500;   // aggressive turnover — drop everything old and unvested
            for (int i = 0; i < tape.Count; i++)
            {
                var id = tape.ResidentEventIDs[i];
                if (id.Value >= tape.NextId - 100) continue;              // recency guard
                if (!tape.IsEvidenceAt(i) && id.Value < dropBelow) drop.Add(id);
            }
            drop.Sort((a, b) => a.Value.CompareTo(b.Value));
            tape.Evacuate(Array.Empty<TapeEventID>(), drop);

            // THE INVARIANT: the vested evidence still resolves (bytes) AND still counts (evidence) — no decay.
            bool resolves = tape.Resolve(navReplay, out var back) && back.AsSpan().SequenceEqual(idiom);
            bool counts = tape.IsEvidence(navReplay) && tape.IsReflected(navReplay);
            resolvesAll &= resolves; countsAll &= counts; survivedAll &= (resolves && counts);
        }
        Check(survivedAll, "vested-survives-aestivations", $"the vested evidence resolved+counted after all {aestivations} aestivations (resolves={resolvesAll} counts={countsAll}) · {tape.DroppedCount} unvested dropped around it");
        Check(tape.IsEvidence(evidenceReal) && tape.Resolve(evidenceReal, out _), "real-survives-aestivations", "the Real world-evidence also survived (born evidence, never rots)");

        Console.WriteLine(fails == 0
            ? "✓ VEST=PERMANENCE HOLDS — earned memory survived the rot unconditionally; the agent can trust its vested tier (learns 'memory is earned', never amnesia). Continual-learning thesis is safe."
            : $"✗ CONTRACT BROKEN — {fails} failure(s). If vested memory decays the agent reverts to amnesia — this MUST be fixed before compounding can hold.");
        return fails == 0 ? 0 : 1;
    }

    // ── options ──  the solve stream's knobs, one struct (conventions: shared-prefix locals → a struct).
    private readonly record struct SolveOpts(int Looks, int LooksCap, int Len, int Sweeps, ulong Seed, int Limit, bool Pretrain, bool MeshHomeo, int SiteBudget, bool ConfidenceTrace, bool ExplainRank,
        double MeshFloor, double MeshGain, int MixSpans, int Passes, bool Interleave, int CheckpointEvery, bool AnswerLeakFree, bool ShuffleBindings, int Heldout, int Revisited)
    {
        public static SolveOpts Parse(string[] args)
        {
            int looks = Args.Int(args, "--looks", 8);
            // LooksCap is the MAX action budget for an abstaining instance; it defaults to Looks and is clamped ≥ Looks
            // (a cap below the base look-count is meaningless — the base looks always run). The commit boundary itself
            // is owned by CommitCalibrationHomeostat, targeting calibration error -> 0.
            int looksCap = Math.Max(looks, Args.Int(args, "--looks-cap", looks));
            return new(
                Looks: looks,
                LooksCap: looksCap,
                Len: Args.Int(args, "--len", 80),
                Sweeps: Args.Int(args, "--sweeps", 2),
                Seed: Args.Seed(args, "--seed", 0x50175E00UL),
                Limit: Args.Int(args, "--limit", 0),
                Pretrain: Args.Int(args, "--pretrain", 1) != 0,
                MeshHomeo: Args.Has(args, "--mesh-homeo"),
                SiteBudget: Args.Int(args, "--site-budget", 48),
                // CONFIDENCE TRACE — dump the candidate's margin/coherence/confidence at each possible commit. It is a
                // trace plane only; the homeostat owns the boundary and the setpoint.
                ConfidenceTrace: Args.Has(args, "--confidence-trace"),
                // THE RANKING AUTOPSY — dump the full PathVotes tally + idf counterfactual at each commit (read-only,
                // no re-rank). Names WHY the raw-vote leader beat the gold, and whether idf-weighting recovers it.
                ExplainRank: Args.Has(args, "--explain-rank"),
                // ── THE COMBUSTION WIRING (FEED + HOLD + CHURN — the 3 coupled frontier mechanisms) ──
                // HOLD — the mesh-homeostat's boredom floor + integral gain (MeshHomeostat's ctor knobs). Consulted only
                // under --mesh-homeo; the throttle now GOVERNS the solve stream's dream accretion (was sensed-but-inert).
                MeshFloor: Args.Double(args, "--mesh-floor", 0.05),
                MeshGain: Args.Double(args, "--mesh-gain", 0.30),
                // CHURN — the aestivation's MIX rail: prior REAL spans re-ingested per aestivation (round-robin over the accreted
                // tape, the pool WRAPS — Mesh's fat MIX rail ported), so the loopback dream has FRESH structure to
                // consolidate (0 = the inert-loopback baseline — no churn, the frozen tape the plateau ran on).
                MixSpans: Args.Int(args, "--mix-spans", 0),
                // FEED — the perpetual-novel-real stream shape. --passes N re-runs the stream N times (the LOOPED world
                // the plateau diagnosis measured — the criticality law's bounded world); --interleave round-robins the
                // instances ACROSS repos (astropy,django,… one each, then repeat) instead of the instance_id-sorted
                // clustering, so consecutive instances are DIFFERENT codebases — a varied feed even within one pass.
                Passes: Math.Max(1, Args.Int(args, "--passes", 1)),
                Interleave: Args.Has(args, "--interleave"),
                CheckpointEvery: Args.Int(args, "--checkpoint-every", 25),
                AnswerLeakFree: Args.Has(args, "--answer-leak-free"),
                ShuffleBindings: Args.Has(args, "--shuffle-bindings"),
                Heldout: Args.Int(args, "--heldout", 0),
                Revisited: Args.Int(args, "--revisited", 0));
        }
    }

    private readonly record struct SolveResult(string Gold, string Answer, bool Committed, bool Correct, bool ReachedGold, bool RecalledGold, int Looks, double Confidence, double CalibrationError,
        double Coherence, double Margin, int CandidateCount, int CommitDepth, int CommitDepthBand, int CommitPickCount,
        double NoveltyCoverage, NoveltyBands NoveltyBand, double NoveltyFloor, bool LiteralAnswerPathInPolicy, int LiteralAnswerPolicyRules);

    // ── QUERY TERMS ──  the bridge-policy the oracle transcript teaches, extracted: the query's identifier-shaped
    // tokens ranked by DISCRIMINATIVE pull — a term rare across the sites points AT the fix (a common word like
    // "error"/"result" is everywhere and leads nowhere). This is the deterministic policy the Cortex's grounded moves
    // search (recall these against memory, grep these in the codebase), the same move the oracle demonstrates.
    private static class QueryTerms
    {
        public static List<string> Rank(string query, List<Tool.SiteRow> sites)
        {
            // document-frequency over sites: how many sites contain each token (the rarity denominator).
            var df = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in sites)
                foreach (var tok in EnumerateTokens(s.Text).Distinct())
                    df[tok] = df.GetValueOrDefault(tok) + 1;
            int nSites = Math.Max(1, sites.Count);
            // score each distinct query identifier: rarer across sites = higher; longer = a touch higher (identifiers
            // beat short words). Drop tokens that appear in NO site (they can't grep to anything) and tokens < 4 chars.
            var scored = new List<(string Tok, double Score)>();
            foreach (var tok in EnumerateTokens(query).Distinct())
            {
                if (tok.Length < 4) continue;
                int freq = df.GetValueOrDefault(tok, 0);
                if (freq == 0) continue;                                  // not in the codebase — a dead grep
                double score = -(double)freq / nSites * 4 + tok.Length * 0.05;
                scored.Add((tok, score));
            }
            scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Tok, b.Tok));
            return scored.Select(x => x.Tok).Take(8).ToList();
        }

        private static IEnumerable<string> EnumerateTokens(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsLetterOrDigit(text[i]) || text[i] == '_')
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    yield return text[start..i];
                }
                else i++;
            }
        }
    }

    // ── PATH VOTES ──  the candidate-localization accumulator, DISCRIMINATION-WEIGHTED and COHERENCE-CONVERTED. Every
    // observation's hit paths vote, but the weight is INVERSE to how many paths that observation surfaced: a grep
    // returning FEW files is specific (a rare bridge-term — it points AT the fix), so each of its hits votes HIGH; a
    // grep flooding many files (a common word) votes little per path. This is the fix for the measured clog — the gold
    // was REACHED but out-voted, because a broad common-term grep flooded its neighbours (core.py/base.py) with as many
    // votes as the sharp bridge-term grep gave the gold. Weighting by 1/hits makes the sharp hit win. Recall hits
    // (memory of a prior successful navigation) carry an extra multiplier — they ARE a past answer to a rhyming query.
    //
    // THE CONFIDENCE READ (the corroboration⊥discrimination fix). Votes measure CORROBORATION — how much the Cortex's
    // moves piled onto a path — and the recurring clog is that corroboration is ORTHOGONAL to which path is actually
    // right. The discriminator the whole bench arc needed is the Cortex's OWN COHERENCE: the rev worker measured that
    // instances where the Cortex's self-model DEEP-PARSES its navigation are materially more often correct UNTRAINED.
    // The commit candidate therefore carries confidence as the agreement of two self-signals: vote margin (can the
    // Cortex separate the winner from the runner-up?) and coherence (does its grammar parse the path's navigation?).
    // The homeostat, not a user knob, decides whether that confidence is enough to commit.
    //
    // The answer draws the highest-scored path (ties by name — deterministic); it is ALWAYS a real observed site path,
    // never a hallucinated arg (the traceback-path clog: a generated `answer /usr/lib/...` never enters here).
    private sealed class PathVotes
    {
        private readonly Dictionary<string, double> _votes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StringBuilder> _nav = new(StringComparer.Ordinal);   // per-path NAVIGATION text — the observation lines that surfaced this path (the coherence-parse subject)
        private readonly HashSet<string> _valid;   // the current world's real paths — the answer can ONLY be one of these
        // ── THE EXPLAIN JOURNAL ──  per-path contribution provenance, populated ONLY under --explain-rank (null on the hot
        // path — zero cost when dark). Each entry is one grep/recall's push onto a path: the query TERM that fired it, the
        // verb, the grep's FAN-OUT (how many valid files it hit — the disc denominator), and the weight it added. This is
        // the raw material the ranking autopsy reads: is the wrong #1 a HUB hit by many broad greps (breadth), or did it
        // out-sharp the gold? And the counterfactual it feeds: re-weight each push by idf(term) and see if gold flips to top-1.
        private Dictionary<string, List<Contrib>>? _contrib;
        public readonly record struct Contrib(string Term, Tool.ToolVerbs Verb, int Fanout, double Weight);
        public void ArmExplain() => _contrib ??= new(StringComparer.Ordinal);

        public readonly record struct CommitCandidate(string Path, int Score, double Margin, double Coherence, double Confidence, double NoveltyCoverage);

        public PathVotes(HashSet<string> valid) { _valid = valid; }

        /// Tally the observation's hit paths (discrimination-weighted) AND record the observation text against each hit
        /// path — that navigation is what the coherence converter parses (how deeply did the Cortex's grammar explain the
        /// reasoning that reached this path). `obsText` is the call's observation (the grep/recall lines); capped per
        /// path so a flood can't dominate the parse-length denominator. `term` is the query token that fired this call
        /// (the grep/recall arg) — recorded in the explain report so the idf counterfactual can weight by ITS rarity.
        public void Tally(IReadOnlyList<string> paths, Tool.ToolVerbs verb, string obsText = "", string term = "", int termRank = -1)
        {
            if (paths.Count == 0) return;
            double verbW = verb switch { Tool.ToolVerbs.Recall => 4.0, Tool.ToolVerbs.Grep => 3.0, Tool.ToolVerbs.Read => 2.5, Tool.ToolVerbs.Open => 2.0, _ => 1.0 };
            // discrimination: fewer paths surfaced ⟹ each is more specific. 1/√count keeps a 1-hit grep strong and a
            // 50-hit flood weak, without zeroing the flood entirely (a broad term still weakly supports its members).
            // count discrimination over the VALID (in-world) hits only — a recall returning 20 cross-domain paths and
            // 1 real one is a SPECIFIC recall of that one real path, not a 21-way flood.
            int validCount = 0; foreach (var p in paths) if (_valid.Contains(p)) validCount++;
            if (validCount == 0) return;
            double disc = 1.0 / Math.Sqrt(validCount);
            double weight = verbW * disc;
            foreach (var p in paths)
            {
                if (!_valid.Contains(p)) continue;
                _votes[p] = _votes.GetValueOrDefault(p) + weight;
                if (_contrib is not null)
                {
                    if (!_contrib.TryGetValue(p, out var cs)) _contrib[p] = cs = new List<Contrib>();
                    cs.Add(new Contrib(term, verb, validCount, weight));
                }
                if (obsText.Length == 0) continue;
                if (!_nav.TryGetValue(p, out var sb)) _nav[p] = sb = new StringBuilder();
                if (sb.Length < NavCap) sb.Append(obsText.AsSpan(0, Math.Min(obsText.Length, NavCap - sb.Length))).Append('\n');
            }
        }

        private const int NavCap = 2048;   // per-path navigation-text cap — enough idioms for the parse-depth read, bounded against a flood skewing the denominator

        /// The raw-vote leader (ties by name) — the answer-TRIGGER gate reads this: the commit-TIMING must stay fixed
        /// so the coherence A/B isolates the RANKING effect, not "commits earlier/later". Score is the integer vote pile.
        public bool TryGetBest(out string path, out int score)
        {
            path = ""; double best = 0;
            foreach (var (p, v) in _votes)
                if (v > best || (v == best && string.CompareOrdinal(p, path) < 0)) { path = p; best = v; }
            score = (int)Math.Round(best);
            return path.Length > 0;
        }

        /// THE VOTE-MARGIN — the Cortex's SELF-CONFIDENCE in its committed leader, normalized top1−top2 ∈ [0,1] (1 =
        /// uncontested, →0 = a dead tie between the top two candidates). This is the in-architecture discriminator the
        /// margin-adaptive commit reads PER-LOOK: a thin margin means the Cortex is UNSURE which file is right, so it
        /// keeps navigating (more looks); a wide margin means it has separated the winner and can commit. Cover-free
        /// (pure votes) so the per-look gate stays O(candidates), never a per-look grammar rebuild (perf.md). Zero
        /// candidates ⇒ 0 (nothing surfaced yet — maximally uncertain, keep searching).
        public double ComputeMargin()
        {
            double v1 = double.MinValue, v2 = double.MinValue; string top1 = "";
            foreach (var (p, v) in _votes)
            {
                if (v > v1 || (v == v1 && string.CompareOrdinal(p, top1) < 0)) { v2 = v1; v1 = v; top1 = p; }
                else if (v > v2) v2 = v;
            }
            if (v1 <= double.MinValue) return 0.0;                     // no votes yet — unresolved
            if (v2 <= double.MinValue) return 1.0;                     // a single uncontested candidate
            return (v1 - v2) / Math.Max(1e-9, v1);
        }

        /// The commit candidate — raw-vote leader plus the confidence signals the homeostat regulates on. Value is allowed
        /// to move the vote field, so it can improve the margin only by separating one observed path from its rivals. It
        /// cannot substitute for coherence as a commit-confidence shortcut; the shuffled-reward null would otherwise become
        /// a generic confidence pump instead of a credit-assignment test.
        public bool TryCandidate(Engine.GrammarCover? cover, out CommitCandidate candidate)
        {
            candidate = default;
            if (!TryGetBest(out string path, out int score)) return false;
            double margin = ComputeMargin();
            double coherence = cover is null ? 0.0 : ComputeCoherence(cover, path);
            double noveltyCoverage = cover is null ? 0.0 : ComputeCoverage(cover, path);
            double confidence = coherence > 0 ? Math.Min(margin, coherence) : margin;
            candidate = new CommitCandidate(path, score, margin, coherence, Math.Clamp(confidence, 0.0, 1.0), noveltyCoverage);
            return true;
        }

        public void TraceCandidate(CommitCandidate c, string tag, CommitCalibrationRead read)
            => Console.WriteLine($"      confidence-trace {tag} · path={Truncate(c.Path, 48)} score={c.Score} margin={c.Margin:F3} coherence={c.Coherence:F3} confidence={c.Confidence:F3} novelty={read.Label}/{read.Coverage:F3} floor={read.Floor:F3} {(c.Confidence >= read.Floor ? "commit" : "abstain")}");

        /// THE ACTIONABILITY READ — the two candidate discriminators the whole clog turns on, measured at the commit:
        /// the committed leader's COHERENCE (the rev worker's deep-parse self-model signal) and the vote MARGIN
        /// (top1−top2, normalized — the "incompleteness" signal that discriminated +0.12 where
        /// coverage-residual failed +0.01). Correlating each against correctness across the stream settles WHICH signal
        /// is actionable at rank time — coherence (a within-instance re-rank works) or margin (only a gate/abstain works).
        public (double Coherence, double Margin, double Confidence, double NoveltyCoverage, int Candidates) Diagnose(Engine.GrammarCover? cover)
        {
            if (!TryCandidate(cover, out var c)) return (0, 0, 0, 0, _votes.Count);
            return (c.Coherence, c.Margin, c.Confidence, c.NoveltyCoverage, _votes.Count);
        }

        /// The Cortex's coherence on a candidate: how deeply its standing grammar parses the NAVIGATION that surfaced the
        /// path. Deep parse (few symbols/byte → the grammar has named subroutines for this reasoning) = a coherent
        /// self-model = the rev worker's +11-15pt discriminative signal. No navigation recorded (the path came from a
        /// bare hit with no observation text) ⇒ neutral 0 (falls back to pure votes for that candidate).
        private double ComputeCoherence(Engine.GrammarCover cover, string path)
        {
            if (!_nav.TryGetValue(path, out var sb) || sb.Length == 0) return 0.0;
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return Math.Clamp(1.0 - cover.ParsedSizePerByte(bytes), 0.0, 1.0);
        }

        private double ComputeCoverage(Engine.GrammarCover cover, string path)
        {
            if (!_nav.TryGetValue(path, out var sb) || sb.Length == 0) return 0.0;
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return Math.Clamp(cover.Coverage(bytes), 0.0, 1.0);
        }

        /// ── THE RANKING AUTOPSY ──  the read-only dump that answers WHY the raw-vote leader beat the gold when the gold
        /// was reached. Prints every candidate's raw vote (the score the answer-trigger commits on) sorted, marks the GOLD
        /// and the committed PICK, and — per candidate — the vote's PROVENANCE: how many pushes, the widest grep fan-out
        /// that fed it, and the terms. Then it runs the IDF COUNTERFACTUAL inline: re-score every candidate as Σ weight·idf(term)
        /// where idf(t)=ln(N/df(t)) over THIS world's files (df = files whose text contains the term, the grep's own
        /// semantics), and reports whether idf FLIPS the gold to top-1. `docFreq` is the world's term→file-count oracle;
        /// `nFiles` the corpus size. Pure accounting over the already-tallied votes — no navigation, no mutation.
        public void ExplainRank(string gold, Func<string, int> docFreq, int nFiles, string tag)
        {
            if (_contrib is null || _votes.Count == 0) { Console.WriteLine($"      ┌─ explain-rank {tag} · (no candidates / report unarmed)"); return; }
            TryGetBest(out string pick, out _);
            // RAW ranking — descending vote, ties by name (the deterministic answer order).
            var raw = new List<(string P, double V)>();
            foreach (var (p, v) in _votes) raw.Add((p, v));
            raw.Sort((a, b) => b.V != a.V ? b.V.CompareTo(a.V) : string.CompareOrdinal(a.P, b.P));
            int goldRank = -1; for (int i = 0; i < raw.Count; i++) if (raw[i].P == gold) { goldRank = i; break; }

            // IDF re-score — Σ over this path's pushes of weight·idf(term). A hub hit by many BROAD greps (common terms,
            // low idf) shrinks; a file hit by a RARE bridge-term (high idf) grows. df==0 ⇒ term never greps (guard to idf 0).
            var idf = new List<(string P, double S)>();
            foreach (var (p, cs) in _contrib)
            {
                double s = 0;
                foreach (var c in cs)
                {
                    int dfc = c.Term.Length > 0 ? docFreq(c.Term) : 0;
                    double w = dfc > 0 ? Math.Log((double)nFiles / dfc) : 0.0;
                    s += c.Weight * w;
                }
                idf.Add((p, s));
            }
            idf.Sort((a, b) => b.S != a.S ? b.S.CompareTo(a.S) : string.CompareOrdinal(a.P, b.P));
            int goldIdfRank = -1; for (int i = 0; i < idf.Count; i++) if (idf[i].P == gold) { goldIdfRank = i; break; }
            string idfLeader = idf.Count > 0 ? idf[0].P : "";
            bool idfFlipsGold = goldIdfRank == 0 && goldRank != 0;

            Console.WriteLine($"      ┌─ explain-rank {tag} · candidates={raw.Count} · gold={Truncate(gold, 42)} rank#{goldRank} · pick={Truncate(pick, 42)}{(gold == pick ? " (=gold)" : "")}");
            for (int k = 0; k < raw.Count && k < 8; k++)
            {
                var (p, v) = raw[k];
                _contrib.TryGetValue(p, out var cs);
                int pushes = cs?.Count ?? 0;
                int maxFan = 0; foreach (var c in cs ?? new()) if (c.Fanout > maxFan) maxFan = c.Fanout;
                var termSet = new List<string>();
                foreach (var c in cs ?? new()) if (c.Term.Length > 0 && !termSet.Contains(c.Term)) termSet.Add(c.Term);
                string terms = termSet.Count > 0 ? string.Join(",", termSet.Take(4)) : "-";
                string mark = p == gold ? " ◄GOLD" : (p == pick ? " ◄PICK" : "");
                Console.WriteLine($"      │  {k}: vote={v,6:F2} pushes={pushes,2} fan≤{maxFan,3} [{Truncate(terms, 34),-34}] {Truncate(p, 40)}{mark}");
            }
            Console.WriteLine($"      ├─ IDF-counterfactual · leader={Truncate(idfLeader, 42)} · gold→rank#{goldIdfRank} · {(idfFlipsGold ? "FLIPS-GOLD-TO-1" : goldIdfRank == goldRank ? "no-change" : $"gold {goldRank}→{goldIdfRank}")}");
            for (int k = 0; k < idf.Count && k < 6; k++)
            {
                var (p, s) = idf[k];
                string mark = p == gold ? " ◄GOLD" : "";
                Console.WriteLine($"      │  {k}: idf-score={s,7:F2}  {Truncate(p, 44)}{mark}");
            }
            Console.WriteLine($"      └─ verdict · gold {(goldRank == 0 ? "already-#1" : $"lost@#{goldRank}")} → idf {(idfFlipsGold ? "RECOVERS-#1" : goldIdfRank == 0 ? "holds-#1" : $"still-#{goldIdfRank}")}");
        }
    }

    /// One rankings.jsonl row — the run's durable per-instance result. Hand-built (StringBuilder + JSON-escaped
    /// strings, the nav EmitJson shape) so it is AOT-safe with no reflection: instance id, its repo, the gold + the
    /// Cortex's committed answer or abstain, the execution-corroboration flags, confidence, and the running commit-gate metrics.
    private static string EmitJson(int idx, string instance, string repo, SolveResult r, bool isReturn, CommitCalibrationHomeostat gate)
    {
        var sb = new StringBuilder();
        sb.Append("{\"idx\":").Append(idx);
        sb.Append(",\"instance_id\":").Append(FormatJsonString(instance));
        sb.Append(",\"repo\":").Append(FormatJsonString(repo)).Append(",\"return\":").Append(isReturn ? "true" : "false");
        sb.Append(",\"gold\":").Append(FormatJsonString(r.Gold)).Append(",\"answer\":").Append(FormatJsonString(r.Answer));
        sb.Append(",\"committed\":").Append(r.Committed ? "true" : "false");
        sb.Append(",\"correct\":").Append(r.Correct ? "true" : "false");
        sb.Append(",\"reached_gold\":").Append(r.ReachedGold ? "true" : "false").Append(",\"recalled_gold\":").Append(r.RecalledGold ? "true" : "false");
        sb.Append(",\"actions\":").Append(r.Looks);
        sb.Append(",\"confidence\":").Append(r.Confidence.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"calibration_error\":").Append(r.CalibrationError.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"coherence\":").Append(r.Coherence.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"margin\":").Append(r.Margin.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"candidate_count\":").Append(r.CandidateCount);
        sb.Append(",\"commit_depth\":").Append(r.CommitDepth);
        sb.Append(",\"commit_depth_band\":").Append(r.CommitDepthBand);
        sb.Append(",\"commit_pick_count\":").Append(r.CommitPickCount);
        sb.Append(",\"novelty_coverage\":").Append(r.NoveltyCoverage.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"novelty_band\":").Append((int)r.NoveltyBand);
        sb.Append(",\"novelty_band_label\":").Append(FormatJsonString(CommitCalibrationHomeostat.LabelOf(r.NoveltyBand)));
        sb.Append(",\"novelty_floor\":").Append(r.NoveltyFloor.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"literal_answer_path_in_policy\":").Append(r.LiteralAnswerPathInPolicy ? "true" : "false");
        sb.Append(",\"literal_answer_policy_rules\":").Append(r.LiteralAnswerPolicyRules);
        sb.Append(",\"success_at_commit\":").Append(gate.SuccessAtCommit.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"abstention_rate\":").Append(gate.AbstentionRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"actions_to_commit\":").Append(gate.ActionsToCommit.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('}');
        return sb.ToString();
    }

    private static string FormatJsonString(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    /// The primary gold file for an instance (schema-tolerant — synth `instance_id` present, real 300-set not).
    private static string LoadGoldFile(string goldPath)
    {
        using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(goldPath));
        if (d.RootElement.TryGetProperty("files", out var f) && f.GetArrayLength() > 0)
            return f[0].GetString() ?? "";
        return "";
    }

    private static IEnumerable<string> ChunkLines(string text, int maxLines)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i += maxLines)
            yield return string.Join('\n', lines.Skip(i).Take(maxLines));
    }

    private static string FormatOneLine(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c == '\n' || c == '\r' ? ' ' : c);
        return sb.ToString().Trim();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
