namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;   // RePairResult (the pretrain base)
using static Cogito.Loc;   // the localization substrate: Toks · IsTest · Load* · AggregateMaxFiles · FileMax · FieldMargin · HeadRank · R · JsonStr · the loop-policy consts · the checkpoint helpers


// ── NAVIGATE ──  the edit-site NAVIGATION loop.
// Design references: TROPHY.md, scratchpad/nav_trophy/DESIGN.md, BENCH-PORT.md. Where `gret --rankdump` is ONE
// static BM25 pass over every site of a repo, navigate is the autoregressive cycle:
//
//   BEACON  rank files coarsely (NG-BM25 over per-file docs — the cheap global field, EXPANDED as terms vest)
//   DESCEND read the top un-visited files (+ the files the issue names by path) — attention spent, metered
//   INDUCE  the repo model via the FULL LOOP, not a one-shot: the visited material becomes an intake POOL whose
//           line-spans accrete in residual-frontier order (RLEI-root — Radula.FrontierPick), the grammar
//           re-induced stride-gated, BREACH-deepened past the greedy pay-floor once the k-aware grok bell
//           (DomainMeter — the homeostat's CvStar law) LOCKS. The bell is the schedule; the residual is the diet.
//   DEDUCE  the know-mask residual (how much of the issue the grokked model structurally explains — uncovered =
//           the known-unknowns) + the LOCAL site field (visited sites under the blended global field)
//   EXPAND  (v1, grok-GATED) mint PPMI-coupled terms off the residual: anchor units = the grammar rules covering
//           issue bytes ADJACENT to uncovered gaps (the edge of the known, aimed at the unknown); their
//           Couplings-graph neighbours expand to candidate terms, df-bounded against the pool. Minted terms are
//           DREAM until corroborated — vested PER TERM only if it SHARPENS the field's top-1 margin (doesn't
//           dilute the leader), evicted otherwise (the Reflection Law at the query scale; ungated expansion is PRF
//           drift, the grammar-crossover-negative).
//   STABILIZE the calibrated verdict — LAND when the field concentrates and persists · JUMP down the beacon when
//           the field is FLAT and the residual plateaued (the frontier hop) · CONTINUE otherwise. The residual
//           prices the verdict as P(correct) — the escalation channel for gold-beyond-beacon-reach instances.
//
// v0 (43.0/66.0 file, fn@5 33.9 — nav_trophy/RESULT.md) was the ablation floor: static field + locality only,
// pre-registered "zero scoring information; the loop's sequential gain begins at v1". THIS is v1: the trunk's
// proven metric explosion (grok-lock earlier · vested dream · breach depth) wired into the retrieval loop.
// Consumes worker A's frozen SWE-bench-Lite localization plane (qstar/scratchpad/swe_loc FORMAT.md): an instance
// dir holding query.txt + sites.jsonl (+ gold.json, used only to PRINT verdicts — never to steer).
public static class Navigate
{
    // The loop policy + the induction/expansion knobs are shared across all three modes (Loc + RepoGrok own them).
    // Frozen mode keeps only its own knob: the promotion un-clamp threshold.
    //
    // ── the UN-CLAMP (--promote): the outcomeCredit gate's PROMOTION channel (the clog-fix experiment) ──
    // The v1 gate can only sharpen the SITTING leader (non-dilution) — it structurally can't promote a NEW top-1,
    // because a promotion transiently drops the old leader's margin and evicts the term. That walls expansion off from
    // file@1 (the traced clog: mesh ≡ prior-pretrain on file@1, Δ+0, all divergences ties). The promotion channel:
    // a mint ALSO vests if it DECISIVELY SURGES the field margin (mTrial ≥ mCur + PromoteMarginEps) — strong positive
    // corroboration that a different file is the real leader. This is the FIXED-THRESHOLD precursor to the learned
    // weight-head (which would learn per-rule which mints earn promotion via gradient on retrieval loss); the constant
    // here is the hand-set gate the head replaces.
    public const double DefaultPromoteEps = 0.10;  // the --promote-eps default (the CLI's --mode frozen surface reads this so the flag-absent default can't drift from the mode's own)
    private static double PromoteMarginEps = DefaultPromoteEps; // the margin SURGE a mint must produce to earn a promotion (decisive corroboration — well above VestMarginEps's non-dilution tolerance, so only a strongly-concentrating term re-ranks the leader). Overridable via --promote-eps for the calibration sweep (is promotions=0 a threshold or a STRUCTURAL block?).

    /// The loop's RAW output for one instance — the HEAD navigate read + reordered (visited files in landed
    /// field-guarded order; visited fn/method sites by the blended local field), plus the meters and the v1
    /// telemetry (grok state, outcomeCredit counts, the calibrated verdict). NO beacon tail: the scoring harness
    /// splices the static ranking for what navigate did not visit.
    public sealed record NavResult(
        string Instance, List<string> VisitedFiles,
        List<(string Path, string Name, int Start, int End, double Score)> LocalFnSites,
        int Looks, long AttnBytes, long CorpusBytes,
        string Verdict, double PCorrect, int Jumps,
        bool Locked, int LockLook, double CvZ, int KZ, double MaxSpan, int Rules, long TapeBytes,
        double Know, int OutcomeCredited, int Evicted, int Admissions, List<string> MintedTerms);

    /// The cov-beacon lever (the doc-side file@1 un-clamp) — the sub-flags of `--mode frozen --cov-beacon`. Defaults
    /// match the frozen mode's historical Args defaults (weight=vest, topk=0=keep-all, scale=1.0, minlen=0=all rules).
    public readonly record struct CovOpts(bool Beacon, string Weight, int TopK, double Scale, int MinLen, bool Diag, int Dump);

    /// The frozen mode entry (nav --mode frozen). `all` routes the batch driver (→ runs/navigate_NNNN/); else the
    /// single-instance verbose path. `pretrainDir` seeds RepoGrok's STANDING BASE (transfer learning: the repo inducts
    /// against a mind that already knows general code+prose structure — the head-of-Exodia swing); "" = the COLD
    /// ablation floor. The cov-beacon (frozen-only) is the doc-side grammar-coverage file@1 lever.
    public static int Run(string dir, bool all, int maxLooks, int k, bool testPrior, bool expand, bool promote,
                          double promoteEps, string pretrainDir, int limit, int checkpointEvery, CovOpts cov)
    {
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"  nav --mode frozen: '{dir}' is not a directory"); return 1; }
        PromoteMarginEps = promoteEps;
        var pretrain = LoadPretrainBase(pretrainDir);   // the trained standing base (null = cold)
        var covBeacon = LoadCovBeacon(cov, pretrainDir);   // THE FILE@1 UN-CLAMP: the doc-side coverage beacon (null unless --cov-beacon; needs --pretrain for the grammar)
        return all ? RunAll(dir, limit, maxLooks, k, testPrior, expand, promote, checkpointEvery, cov.Diag, cov.Dump, pretrain, covBeacon)
                   : RunOne(dir, maxLooks, k, testPrior, expand, promote, pretrain, covBeacon);
    }

    /// Build the document-side coverage beacon from the SAME mesh checkpoint --pretrain loads, TRACED (the per-rule
    /// vest weight survives only in the traced merge stream — CovBeacon.Build). Null unless --cov-beacon; requires
    /// --pretrain (the beacon IS the trained grammar reading the issue↔file overlap — no grammar, no beacon). Loaded
    /// ONCE, reused read-only across all instances. See CovBeacon for the LSR doc-weighting rationale + the SUPERVISED
    /// caveat on --cov-weight ratio/prod (they read gold — the oracle null-kill, reported apart from the pure arm).
    private static CovBeacon? LoadCovBeacon(CovOpts cov, string pretrainDir)
    {
        if (!cov.Beacon) return null;
        if (pretrainDir.Length == 0) { Console.Error.WriteLine("  --cov-beacon requires --pretrain (the beacon reads the trained grammar's issue↔file coverage)"); return null; }
        CovBeacon.Weights mode = cov.Weight switch
        {
            "idf"   or "d" => CovBeacon.Weights.Idf,     // vest × rarity (unsupervised discriminative — the ubiquity cure)
            "ratio" or "b" => CovBeacon.Weights.Ratio,   // gold term-recall (SUPERVISED — oracle null-kill)
            "prod"  or "c" => CovBeacon.Weights.Prod,    // vest × gold-recall (SUPERVISED)
            _              => CovBeacon.Weights.Vest,    // "vest"/"a"/default — the pure unsupervised corroborated weight
        };
        int topK = cov.TopK; double scale = cov.Scale; int minLen = cov.MinLen;
        // TRACED peek: mesh (CGRING) only — the beacon is the deep corroborated grammar reading structural overlap; a
        // trunk checkpoint would work too but this session's mesh is the point.
        if (!IsMeshCheckpoint(pretrainDir)) { Console.Error.WriteLine("  --cov-beacon: only a mesh (CGRING) checkpoint carries the traced vest weights; got a trunk checkpoint"); return null; }
        (RePairResult g, List<MergeEvent> events) = MeshCheckpoint.PeekGrammarTraced(pretrainDir);
        CovBeacon? beacon = CovBeacon.Build(g, events, mode, topK, scale, minLen);
        if (beacon is null) { Console.Error.WriteLine("  --cov-beacon: the checkpoint yielded no grammar"); return null; }
        Console.WriteLine($"  cov-beacon ← {pretrainDir} · {beacon.RuleCount} rules (FULL grammar, vest-weighted) · weight={mode}{(beacon.Supervised ? " [SUPERVISED — oracle null-kill, reads gold]" : "")} topk={topK} scale={scale:F2} minlen={minLen}");
        return beacon;
    }

    /// Load a trunk run's trained grammar as the pretrained base — ONCE, compacted to PURE BINARY (the Loom's Seed
    /// contract). A trained trunk grammar carries campfire/breach/GC consolidation (n-ary templates, demoted
    /// TapeRef bodies) layered over the pure Re-Pair core; SelfCompress.Compact + a binary-purity filter recover
    /// the seed-able rule set. Returns null when no --pretrain (the cold arm). Prints the base's shape so the run
    /// log records exactly which mind touched the bench.
    private static RePairResult? LoadPretrainBase(string runDir)
    {
        if (runDir.Length == 0) return null;
        (RePairResult raw, List<GrammarRule> bin) = LoadBinaryPrefix(runDir);   // routes CGRING(mesh)/CGCKPT(trunk); keeps the seed-able binary prefix
        int nBin = 0, nOther = 0;
        foreach (GrammarRule r in raw.Rules) { if (r.Kind == RuleBodyKind.Expansion && r.Pattern.Length == 2) nBin++; else nOther++; }
        RePairResult baseG = new(bin.ToArray(), [], raw.TotalSavings, raw.AlphabetSize);
        Console.WriteLine($"  pretrain base ← {runDir} · {raw.Rules.Length} trained rules ({nBin} pure-binary / {nOther} consolidated) · seeding {bin.Count} (binary prefix)");
        return baseG;
    }

    // ── the batch driver: run the loop over every instance dir, emit ONE rankings jsonl line each + the
    // SLOPE/calibration summary (gold read PRINT-ONLY — the accuracy-vs-grok-depth read BENCH-PORT names) ──
    private static int RunAll(string dataDir, int limit, int maxLooks, int k, bool testPrior, bool expand, bool promote, int checkpointEvery, bool covDiag, int covDump, RePairResult? pretrain, CovBeacon? covBeacon)
    {
        var dirs = Directory.GetDirectories(dataDir)
                            .Where(d => File.Exists(Path.Combine(d, "query.txt")) && File.Exists(Path.Combine(d, "sites.jsonl")))
                            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).Take(limit).ToList();
        // the run's config snapshot = the deterministic policy telegraph (same config + same data ⇒ same run — the
        // replay config, landed as `config` in the run dir by AgentRun.Begin).
        string config = $"navigate --all · {dirs.Count} instances · k={k} looks≤{maxLooks} testprior={testPrior} expand={expand} promote={promote} pretrain={(pretrain is null ? "COLD" : $"{pretrain.Value.Rules.Length} rules")}";
        using var ar = AgentRun.Begin("navigate", config, new IntakeManifest(dataDir, IntakeManifest.Of(dataDir), $"limit={limit}"));
        Console.WriteLine($"navigate --all · {dirs.Count} instances · policy k={k} looks≤{maxLooks} testprior={testPrior} expand={expand} pretrain={(pretrain is null ? "COLD" : $"{pretrain.Value.Rules.Length} rules")}");

        // the verdict block accumulates the run's own reads (cov-diag pre-pass + the post-stream Summarize) → report.txt.
        var report = new StringBuilder();

        // ── THE DEEP-RULE TRANSFER READ (--cov-diag) ──  the ONE measurement the domain-gap hypothesis turns on:
        // does THIS grammar have deep rules (expansion ≥ minLen) that FIRE on the bench issue text — the transfer the
        // C#-grammar had ZERO of (not one deep C# rule fired on any Python issue)? Runs a cheap read-only pre-pass over
        // every instance's issue↔(files, gold) BEFORE the drive: per instance counts (i) deep issue-terms (rules ≥minLen
        // firing on the issue), (ii) deep terms firing on BOTH issue and the GOLD file (the discriminative substance),
        // (iii) instances with ≥1 such shared deep rule. This is the honest C#-vs-Python transfer number, printed apart.
        if (covBeacon is not null && covDiag)
        {
            var diag = new StringBuilder();
            CovDiag(diag, dirs, covBeacon, covDump);
            Console.Write(diag);           // the pre-pass prints before the stream (its live position preserved)
            report.Append(diag);
        }

        long t0 = Environment.TickCount64;
        var slope = new List<(NavResult R, bool File1, bool Fn5)>();
        // OBSERVABILITY (no-trace-no-elevator on the bench): the per-instance curve lands in the run dir, flushed per
        // line, so a monitor previews the running score at instance ~35 instead of waiting for 300. The running file@1 /
        // fn@5 print every 10 catches a dead-pretrain clog (flat-at-cold) in minutes.
        int hitF1 = 0, hitF5 = 0;
        var w = ar.Rankings;
        var curve = ar.Curve("idx\tinstance_id\tgold_file_rank\tgold_fn_rank\tfile_hit_at1\tfn_hit_at5");
        {
            for (int n = 0; n < dirs.Count; n++)
            {
                var r = Drive(dirs[n], maxLooks, k, testPrior, verbose: false, expand, promote, pretrain, covBeacon);
                var (goldFiles, goldFns) = LoadGold(Path.Combine(dirs[n], "gold.json"));
                bool f1 = r.VisitedFiles.Count > 0 && goldFiles.Contains(r.VisitedFiles[0]);
                bool f5 = r.LocalFnSites.Take(5).Any(x => goldFns.Contains((x.Path, x.Name)));
                int goldFileRank = HeadRank(r.VisitedFiles, goldFiles);                 // the loop's HEAD rank (nav's own output; print-only)
                int goldFnRank   = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name))) + 1;
                slope.Add((r, f1, f5));
                w.WriteLine(EmitJson(r, f1, f5));
                if (f1) hitF1++; if (f5) hitF5++;
                curve.WriteLine($"{n + 1}\t{r.Instance}\t{(goldFileRank == 0 ? "miss" : goldFileRank)}\t{(goldFnRank == 0 ? "miss" : goldFnRank)}\t{(f1 ? 1 : 0)}\t{(f5 ? 1 : 0)}");
                ar.Journal.Index(n + 1, $"navigate {r.Instance} · gold file-rank {(goldFileRank == 0 ? "miss" : goldFileRank.ToString())}/fn {(goldFnRank == 0 ? "miss" : goldFnRank.ToString())} · file@1={(f1 ? 1 : 0)} fn@5={(f5 ? 1 : 0)} · {(r.Locked ? $"LOCK@{r.LockLook}" : "unlkd")} vest {r.OutcomeCredited} promo {r.Admissions} maxSpan {r.MaxSpan:F0}");
                if (checkpointEvery > 0 && (n + 1) % checkpointEvery == 0)
                    SaveCheckpoint(ar.Run, ar.Journal, config, n + 1, maxLooks, k, testPrior, expand, promote, checkpointEvery, dirs, pretrain, covBeacon is not null, hitF1, hitF5, t0);
                if ((n + 1) % 10 == 0 || n + 1 == dirs.Count)
                    Console.WriteLine($"  [{n + 1}/{dirs.Count}] {(Environment.TickCount64 - t0) / 1000.0:F0}s · running file@1={100.0 * hitF1 / (n + 1):F1}% fn@5={100.0 * hitF5 / (n + 1):F1}% (SOTA 77.7/94.2) · last {r.Instance} {(r.Locked ? $"LOCK@{r.LockLook}" : "unlkd")} vest {r.OutcomeCredited} promo {r.Admissions} maxSpan {r.MaxSpan:F0}");
            }
        }
        var summary = new StringBuilder();
        summary.Append($"  → {dirs.Count} instances → {ar.Dir} ({(Environment.TickCount64 - t0) / 1000.0:F0}s) · running head file@1 {100.0 * hitF1 / dirs.Count:F1}% fn@5 {100.0 * hitF5 / dirs.Count:F1}%\n");
        Summarize(summary, slope);
        Console.Write(summary);
        report.Append(summary);
        ar.Write("report.txt", report.ToString());
        return 0;
    }

    public static int Resume(string runDir, bool verify = false, int steps = 0)
    {
        if (verify) { Console.Error.WriteLine("  navigate resume --verify is not implemented yet; use the full byte-match gate"); return 1; }
        if (steps != 0) { Console.Error.WriteLine("  navigate resume does not support --steps; the instance horizon rides the checkpointed stream"); return 1; }
        var dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, AgentCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {AgentCheckpoint.FileName} under '{runDir}' — nothing to resume");
            return 1;
        }
        var run = Cogito.Run.Open(dir);
        int maxLooks = 0, k = 0, checkpointEvery = 0, hitF1 = 0, hitF5 = 0;
        bool testPrior = false, expand = false, promote = false, hadCovBeacon = false;
        long t0 = Environment.TickCount64;
        List<string> dirs = new();
        RePairResult? pretrain = null;
        var (_, snap, journal) = AgentCheckpoint.Load(run.Dir, AgentCheckpoint.AgentVerbs.Navigate, r =>
        {
            maxLooks = r.I32(); k = r.I32(); testPrior = r.Bool(); expand = r.Bool(); promote = r.Bool(); checkpointEvery = r.I32();
            dirs = ReadStrings(r);
            if (r.Bool()) pretrain = Checkpoint.ReadGrammar(r);
            hadCovBeacon = r.Bool();
            hitF1 = r.I32(); hitF5 = r.I32();
            t0 = Environment.TickCount64 - r.I64();
            return new Tape();
        });
        if (hadCovBeacon) { Console.Error.WriteLine("  navigate resume cannot restore --cov-beacon yet; checkpoint preserved the refusal instead of drifting"); return 1; }
        journal.Rewrite(run, header: false);
        run.Truncate("rankings.jsonl", snap.RankingsLen);
        run.TruncateCurve("curve.tsv", snap.CurveLen);
        using var ar = AgentRun.Resume(run, journal);
        var w = ar.Rankings;
        var curve = ar.Curve("idx\tinstance_id\tgold_file_rank\tgold_fn_rank\tfile_hit_at1\tfn_hit_at5");
        Console.WriteLine($"navigate ⇄ {Path.GetFileName(run.Dir)} · resumed at instance {snap.Next}/{dirs.Count}");
        var slope = new List<(NavResult R, bool File1, bool Fn5)>();
        for (int n = snap.Next; n < dirs.Count; n++)
        {
            var r = Drive(dirs[n], maxLooks, k, testPrior, verbose: false, expand, promote, pretrain, covBeacon: null);
            var (goldFiles, goldFns) = LoadGold(Path.Combine(dirs[n], "gold.json"));
            bool f1 = r.VisitedFiles.Count > 0 && goldFiles.Contains(r.VisitedFiles[0]);
            bool f5 = r.LocalFnSites.Take(5).Any(x => goldFns.Contains((x.Path, x.Name)));
            int goldFileRank = HeadRank(r.VisitedFiles, goldFiles);
            int goldFnRank = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name))) + 1;
            slope.Add((r, f1, f5));
            w.WriteLine(EmitJson(r, f1, f5));
            if (f1) hitF1++; if (f5) hitF5++;
            curve.WriteLine($"{n + 1}\t{r.Instance}\t{(goldFileRank == 0 ? "miss" : goldFileRank)}\t{(goldFnRank == 0 ? "miss" : goldFnRank)}\t{(f1 ? 1 : 0)}\t{(f5 ? 1 : 0)}");
            ar.Journal.Index(n + 1, $"navigate {r.Instance} · gold file-rank {(goldFileRank == 0 ? "miss" : goldFileRank.ToString())}/fn {(goldFnRank == 0 ? "miss" : goldFnRank.ToString())} · file@1={(f1 ? 1 : 0)} fn@5={(f5 ? 1 : 0)} · {(r.Locked ? $"LOCK@{r.LockLook}" : "unlkd")} vest {r.OutcomeCredited} promo {r.Admissions} maxSpan {r.MaxSpan:F0}");
            if (checkpointEvery > 0 && (n + 1) % checkpointEvery == 0)
                SaveCheckpoint(run, ar.Journal, File.ReadAllText(run.PathOf("config")).TrimEnd(), n + 1, maxLooks, k, testPrior, expand, promote, checkpointEvery, dirs, pretrain, false, hitF1, hitF5, t0);
        }
        var summary = new StringBuilder();
        summary.Append($"  → {dirs.Count} instances → {ar.Dir} ({(Environment.TickCount64 - t0) / 1000.0:F0}s) · running head file@1 {100.0 * hitF1 / dirs.Count:F1}% fn@5 {100.0 * hitF5 / dirs.Count:F1}%\n");
        Summarize(summary, slope);
        Console.Write(summary);
        ar.Write("report.txt", summary.ToString());
        return 0;
    }

    private static void SaveCheckpoint(Run run, Journal journal, string config, int next, int maxLooks, int k, bool testPrior, bool expand, bool promote, int checkpointEvery,
        List<string> dirs, RePairResult? pretrain, bool hadCovBeacon, int hitF1, int hitF5, long t0)
    {
        var snap = new AgentCheckpoint.StreamSnap(next, 0, Len(run, "journal.log"), Len(run, "rankings.jsonl"), Len(run, "curve.tsv"));
        var image = AgentCheckpoint.Encode(AgentCheckpoint.AgentVerbs.Navigate, config, snap, journal, w =>
        {
            w.I32(maxLooks); w.I32(k); w.Bool(testPrior); w.Bool(expand); w.Bool(promote); w.I32(checkpointEvery);
            WriteStrings(w, dirs);
            w.Bool(pretrain.HasValue);
            if (pretrain.HasValue) Checkpoint.WriteGrammar(w, pretrain.Value);
            w.Bool(hadCovBeacon);
            w.I32(hitF1); w.I32(hitF5);
            w.I64(Environment.TickCount64 - t0);
        });
        AgentCheckpoint.Save(run, image);
    }

    // ── THE DEEP-RULE TRANSFER DIAGNOSTIC ──  the C#-vs-Python domain-gap number. For each instance: the issue's deep
    // grammar-terms (rules ≥ beacon.MinLen firing on the issue text), and how many of those ALSO fire on the GOLD file
    // (the discriminative shared structure). Aggregates the transfer verdict: mean deep-terms/issue, mean shared-with-
    // gold, and the instance-fraction with ≥1 shared deep rule. A Python-domain grammar should show many; the C#
    // grammar showed ZERO deep rules firing on Python. Read-only, gold read for the DIAGNOSTIC only (never steers).
    private static void CovDiag(StringBuilder sb, List<string> dirs, CovBeacon beacon, int dump)
    {
        long sumIssueDeep = 0, sumSharedGold = 0; int instAnyIssue = 0, instSharedGold = 0, instWithGold = 0;
        int maxIssueDeep = 0; int minLen = beacon.MinLen; int dumped = 0;
        foreach (var dir in dirs)
        {
            byte[] issue = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(dir, "query.txt")));
            var terms = beacon.IssueGrammarTerms(issue);
            sumIssueDeep += terms.Idx.Length;
            if (terms.Idx.Length > 0) instAnyIssue++;
            if (terms.Idx.Length > maxIssueDeep) maxIssueDeep = terms.Idx.Length;
            var (goldFiles, _) = LoadGold(Path.Combine(dir, "gold.json"));
            if (goldFiles.Count == 0) continue;
            instWithGold++;
            // the gold file's text (its module site) — the shared-deep-rule count is over the FIRST gold file present
            var sites = LoadSites(Path.Combine(dir, "sites.jsonl"));
            int shared = 0; string sharedGf = "";
            foreach (var gf in goldFiles)
            {
                var mod = sites.FirstOrDefault(s => s.Kind == "module" && s.Path == gf);
                if (mod is null) continue;
                int c = beacon.CountFiring(terms, Encoding.UTF8.GetBytes(mod.Text));
                if (c > shared) { shared = c; sharedGf = gf; }
            }
            sumSharedGold += shared;
            if (shared > 0) instSharedGold++;
            // CONCRETE RECEIPT: the actual shared issue∩gold rule expansions for the first `dump` instances that have any
            if (dump > 0 && dumped < dump && shared > 0)
            {
                var mod = sites.First(s => s.Kind == "module" && s.Path == sharedGf);
                var exps = beacon.SharedExpansions(terms, Encoding.UTF8.GetBytes(mod.Text), 12);
                sb.Append($"     ⟨dump {Path.GetFileName(dir)}⟩ gold={sharedGf} · {shared} shared rules: [{string.Join(" | ", exps.Select(e => $"\"{e}\""))}]\n");
                dumped++;
            }
        }
        int n = dirs.Count;
        sb.Append($"  ── DEEP-RULE TRANSFER (--cov-diag; minlen={minLen}B, the ≥{minLen}B discriminative scale) ──\n");
        sb.Append($"     grammar deep rules (≥{minLen}B expansion): {beacon.DeepRuleCount} / {beacon.RuleCount} total\n");
        sb.Append($"     issue-firing deep rules: mean {(double)sumIssueDeep / n:F2}/issue · max {maxIssueDeep} · instances with ≥1: {instAnyIssue}/{n} ({100.0 * instAnyIssue / n:F1}%)\n");
        sb.Append($"     shared issue∩GOLD-file deep rules: mean {(double)sumSharedGold / Math.Max(1, instWithGold):F2}/inst · instances with ≥1 shared: {instSharedGold}/{instWithGold} ({100.0 * instSharedGold / Math.Max(1, instWithGold):F1}%)\n");
        sb.Append($"     THE TRANSFER: {(instAnyIssue == 0 ? "ZERO — no deep rule fires on any issue (the C#-grammar signature)" : $"LIVE — deep Python rules fire on {100.0 * instAnyIssue / n:F0}% of issues, share structure with gold on {100.0 * instSharedGold / Math.Max(1, instWithGold):F0}%")}\n");
    }

    // ── THE SLOPE + the calibration — the batch's own verdict reads (nav-HEAD accuracy: the loop's top-1 file /
    // top-5 fns after the kind prior, no static tail — the scorer of record stays worker B's scorer.py; these
    // reads answer BENCH-PORT's "does accuracy CLIMB with repo-grok depth" and "is P(correct) calibrated") ──
    private static void Summarize(StringBuilder sb, List<(NavResult R, bool File1, bool Fn5)> rows)
    {
        if (rows.Count == 0) return;
        int locked = rows.Count(x => x.R.Locked);
        sb.Append($"\n  ── the loop's own reads ({rows.Count} instances; head-only accuracy, gold print-never-steer) ──\n");
        sb.Append($"  grok-lock: {locked}/{rows.Count} locked · mean lock-look {(locked > 0 ? rows.Where(x => x.R.Locked).Average(x => x.R.LockLook) : 0):F1} · " +
                  $"vested {rows.Sum(x => x.R.OutcomeCredited)} terms / evicted {rows.Sum(x => x.R.Evicted)} · promotions {rows.Sum(x => x.R.Admissions)} · jumps {rows.Sum(x => x.R.Jumps)}\n");
        sb.Append($"  verdicts: land {rows.Count(x => x.R.Verdict == "land")} · budget {rows.Count(x => x.R.Verdict == "budget")} · pool {rows.Count(x => x.R.Verdict == "pool")}\n");

        // THE SLOPE: accuracy vs repo-grok depth (maxSpan = the grammar's correlation length; the theory puts the
        // win at the ~150–300B fn-template knee). Flat slope ⇒ the clog is upstream in the induction.
        sb.Append("  slope (accuracy vs grok depth — maxSpan buckets):\n");
        (double Lo, double Hi, string Tag)[] buckets = [(0, 50, "<50B"), (50, 150, "50–150B"), (150, 300, "150–300B"), (300, double.MaxValue, ">300B")];
        foreach (var (lo, hi, tag) in buckets)
        {
            var b = rows.Where(x => x.R.MaxSpan >= lo && x.R.MaxSpan < hi).ToList();
            if (b.Count == 0) { sb.Append($"    {tag,-9} —\n"); continue; }
            sb.Append($"    {tag,-9} n={b.Count,3} · file@1 {100.0 * b.Count(x => x.File1) / b.Count,5:F1} · fn@5 {100.0 * b.Count(x => x.Fn5) / b.Count,5:F1} · locked {b.Count(x => x.R.Locked)}\n");
        }
        var lk = rows.Where(x => x.R.Locked).ToList(); var ul = rows.Where(x => !x.R.Locked).ToList();
        if (lk.Count > 0 && ul.Count > 0)
            sb.Append($"    locked    n={lk.Count,3} · file@1 {100.0 * lk.Count(x => x.File1) / lk.Count,5:F1} · fn@5 {100.0 * lk.Count(x => x.Fn5) / lk.Count,5:F1}\n" +
                      $"    unlocked  n={ul.Count,3} · file@1 {100.0 * ul.Count(x => x.File1) / ul.Count,5:F1} · fn@5 {100.0 * ul.Count(x => x.Fn5) / ul.Count,5:F1}\n");

        // the calibration: P(correct) quintiles vs actual head file@1 — the escalation channel's price sheet
        // (a monotone curve ⇒ the low-P band IS where an LLM escalation buys misses cheapest-first).
        sb.Append("  calibration (P(correct) quintiles vs head file@1):\n");
        var byP = rows.OrderBy(x => x.R.PCorrect).ToList();
        for (int q = 0; q < 5; q++)
        {
            var seg = byP.Skip(q * byP.Count / 5).Take((q + 1) * byP.Count / 5 - q * byP.Count / 5).ToList();
            if (seg.Count == 0) continue;
            sb.Append($"    p∈[{seg[0].R.PCorrect:F2},{seg[^1].R.PCorrect:F2}] n={seg.Count,3} · file@1 {100.0 * seg.Count(x => x.File1) / seg.Count,5:F1}\n");
        }
    }

    // ── the single-instance verbose path: the human-readable per-look trace + the verdict vs the static beacon ──
    private static int RunOne(string dir, int maxLooks, int k, bool testPrior, bool expand, bool promote, RePairResult? pretrain, CovBeacon? covBeacon)
    {
        var r = Drive(dir, maxLooks, k, testPrior, verbose: true, expand, promote, pretrain, covBeacon);
        var (goldFiles, goldFns) = LoadGold(Path.Combine(dir, "gold.json"));
        // the verbose verdict composes the beacon tail HERE (the aggregate-max static file ranking — matches the harness)
        var sites = LoadSites(Path.Combine(dir, "sites.jsonl"));
        string issue = File.ReadAllText(Path.Combine(dir, "query.txt"));
        var bm25 = new Bm25Index(sites.Select(s => s.Text).ToList());
        var siteScore = bm25.Score(Toks(issue).Distinct().ToList());
        var (beaconOrder, _) = AggregateMaxFiles(sites, siteScore);          // static's own ranking (matches the harness)
        var visitedSet = new HashSet<string>(r.VisitedFiles);
        var landedFiles = r.VisitedFiles.Concat(beaconOrder.Where(p => !visitedSet.Contains(p))).ToList();
        if (testPrior) landedFiles = landedFiles.OrderBy(IsTest).ToList();
        Console.WriteLine($"  landed files (top5):     {string.Join("  ", landedFiles.Take(5).Select(Base))}");
        Console.WriteLine($"  landed functions (top5): {string.Join("  ", r.LocalFnSites.Take(5).Select(x => $"{Base(x.Path)}:{x.Name}"))}");
        Console.WriteLine($"  verdict {r.Verdict} · P(correct) {r.PCorrect:F2} · {(r.Locked ? $"grok-LOCKED@look{r.LockLook} cvz {r.CvZ:F3}/k{r.KZ}" : "un-grokked")} · maxSpan {r.MaxSpan:F0}B · " +
                          $"minted [{string.Join(" ", r.MintedTerms.Take(10))}]{(r.MintedTerms.Count > 10 ? " …" : "")} (vested {r.OutcomeCredited} evicted {r.Evicted})");
        if (goldFiles.Count > 0)
        {
            int stat = RankOf(beaconOrder, goldFiles), landed = RankOf(landedFiles, goldFiles);
            int fnHit = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name)));
            Console.WriteLine($"  gold: static file-rank {Fmt(stat)} → landed {Fmt(landed)} · gold-fn landed rank {Fmt(fnHit + 1)} · " +
                              $"attention {100.0 * r.AttnBytes / Math.Max(1, r.CorpusBytes):F1}% of corpus");
        }
        return 0;
    }

    // ── THE DRIVE ──  one instance: beacon → (descend → full-loop induce → residual → expand → local field →
    // verdict) × looks → land. Gold NEVER enters — it is loaded only by the verbose/scoring layers, against the
    // landing. Deterministic: seeded breach, integer/stable-sort everywhere else.
    private static NavResult Drive(string dir, int maxLooks, int k, bool testPrior, bool verbose, bool expand, bool promote, RePairResult? pretrain, CovBeacon? covBeacon)
    {
        string issue = File.ReadAllText(Path.Combine(dir, "query.txt"));
        byte[] issueBytes = Encoding.UTF8.GetBytes(issue);
        var sites = LoadSites(Path.Combine(dir, "sites.jsonl"));
        var fileDocs = sites.Where(s => s.Kind == "module").ToList();            // one per file (FORMAT.md guarantees it)
        var fileByPath = new Dictionary<string, Site>();
        foreach (var f in fileDocs) fileByPath[f.Path] = f;                      // O(1) descend lookup
        long corpusBytes = fileDocs.Sum(f => (long)f.Text.Length);

        // GLOBAL FIELD (postings built once) — every site scored with global-IDF BM25, the SAME field static ranks
        // over. v0's law stands: locality PRUNES the candidate set, re-scoring with a locally-recomputed IDF is the
        // demotion trap. v1's ONLY score change is the ADDITIVE vested-mint field (base + WExpand·mint),
        // both halves under GLOBAL IDF — a mint can re-rank on new evidence, it cannot re-weight old evidence.
        var bm25 = new Bm25Index(sites.Select(s => s.Text).ToList());
        bm25.IndexModules(Enumerable.Range(0, sites.Count).Where(i => sites[i].Kind == "module"));   // file-level df for the minted-term rarity bound
        var issueToks = Toks(issue).Distinct().ToList();
        var issueTokSet = new HashSet<string>(issueToks, StringComparer.Ordinal);
        var baseScore = bm25.Score(issueToks);

        // COV-BEACON (the file@1 un-clamp) — the DOCUMENT-SIDE grammar-coverage term, added to each file's MODULE
        // site BEFORE aggregation (CovBeacon; RESULT_v1_pretrained.md:85). The frozen mesh grammar reads the
        // issue↔file structural overlap BM25's exact-token field is blind to: covBonus[f] = Σ vest-weight over the
        // grammar rules that cover BOTH the issue and file f, Top-K pruned, quantized. It lands on the MODULE site
        // ONLY — the file aggregate reads it (aggregate-max surfaces the file's best site), the fn field (function/
        // method sites) provably never sees it, so fn@5 is untouched and the file@1 lever is isolated. Off (null
        // beacon) skips the whole block: baseScore is the pure BM25 field, byte-identical to the pre-beacon nav.
        double covMax = 0; int covFilesLifted = 0;
        if (covBeacon is not null)
        {
            var issueTerms = covBeacon.IssueGrammarTerms(issueBytes);
            // The per-issue-term discriminative FACTOR (null for pure VEST). IDF (unsupervised) = rarity across the
            // candidate files — the gold-free cure for vest's ubiquity bias. RATIO/PROD (SUPERVISED) read gold ONCE
            // per instance — the oracle null-kill (disclosed in CovBeacon). Both fold into FileBonus per the mode.
            int[]? termFactor = null;
            if (covBeacon.Mode is CovBeacon.Weights.Idf)
            {
                var fileTexts = fileDocs.Select(f => (f.Path, Text: Encoding.UTF8.GetBytes(f.Text))).ToList();
                termFactor = covBeacon.IdfWeights(issueTerms, fileTexts);
            }
            else if (covBeacon.Supervised)
            {
                var (goldFiles, _) = LoadGold(Path.Combine(dir, "gold.json"));
                var fileTexts = fileDocs.Select(f => (f.Path, Text: Encoding.UTF8.GetBytes(f.Text))).ToList();
                termFactor = covBeacon.RatioWeights(issueTerms, fileTexts, goldFiles);
            }
            // Gather the RAW per-module bonus (Σ vest-weight over shared rules, Top-K pruned) then NORMALIZE it to
            // BM25's own scale before adding. The raw Σ is length-biased (a 30KB file fires more rules than a 500B
            // file — the un-normalized-TF trap BM25 solves with length norm) and lives at a wildly different
            // magnitude (max ~1364 vs BM25 ~10). Normalizing the bonus's MAX to covScale × (BM25 max) makes
            // --cov-scale a true RELATIVE weight (0.5 = the beacon can lift a file by up to half BM25's top score)
            // and portable across instances — the beacon RE-RANKS on grammar evidence without drowning the lexical
            // field. Density, not raw count: paired with Top-K, only the strongest shared structure re-ranks.
            double bm25Max = 0; for (int i = 0; i < baseScore.Length; i++) if (baseScore[i] > bm25Max) bm25Max = baseScore[i];
            var rawBonus = new double[sites.Count]; double rawMax = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i].Kind != "module") continue;
                double b = covBeacon.FileBonus(issueTerms, Encoding.UTF8.GetBytes(sites[i].Text), termFactor);
                rawBonus[i] = b;
                if (b > rawMax) rawMax = b;
            }
            double norm = rawMax > 0 && bm25Max > 0 ? covBeacon.Scale * bm25Max / rawMax : 0;
            for (int i = 0; i < sites.Count; i++)
            {
                if (rawBonus[i] <= 0) continue;
                double bonus = norm * rawBonus[i];
                baseScore[i] += bonus;
                if (bonus > covMax) covMax = bonus;
                covFilesLifted++;
            }
        }

        var blend = (double[])baseScore.Clone();                                  // base + WExpand·mint (mint zero until vested); the cov-beacon bonus already folded into baseScore's module sites
        var scratch = new double[blend.Length];                                   // the per-candidate trial buffer — copy blend in, add the mint, keep-by-swap on vest (no clone-per-candidate; ~48 full-corpus double[] clones/instance saved)

        if (verbose && covBeacon is not null)
            Console.WriteLine($"  cov-beacon: {covFilesLifted} files lifted · max bonus {covMax:F3} (normalized to BM25 top, weight {covBeacon.Mode}{(covBeacon.Supervised ? " SUPERVISED" : "")}, scale {covBeacon.Scale:F2})");

        // BEACON = static's own file ranking (aggregate-max of the base scores) — the PARITY ANCHOR: descend
        // tie-breaks, the promotion guard, and the quoted-path order all read THIS rank, so v1 degrades exactly to
        // v0 when nothing vests. The BLENDED aggregate (recomputed on vest) steers which files get READ next —
        // the sequential gain: minted vocabulary can pull a beyond-beacon-reach file into the visited set.
        var (beaconOrder, _) = AggregateMaxFiles(sites, baseScore);
        var beaconRank = new Dictionary<string, int>();
        for (int i = 0; i < beaconOrder.Count; i++) beaconRank[beaconOrder[i]] = i;
        var descendOrder = beaconOrder;                                           // re-aggregated from `blend` when a mint vests
        var descendRank = beaconRank;                                             // the kind-prior re-sort's tie-key MUST follow the blend, or the re-beacon is silently discarded
        var quoted = fileDocs.Where(f => issue.Contains(f.Path, StringComparison.Ordinal))   // path prior: files the issue names verbatim, read FIRST
                             .OrderBy(f => beaconRank.GetValueOrDefault(f.Path, int.MaxValue)).Select(f => f.Path).ToList();

        using var mind = new RepoGrok(pretrain);                                  // the full-loop induction organ (pretrained base → loom-splice repo on top → breach → bell)
        var minted = new List<string>();                                          // VESTED expansion terms (the probe's dream-corroborated growth)
        int vestedCount = 0, evictedCount = 0, jumps = 0, promotions = 0;   // promotions = mints that surged the margin AND re-ranked the leader (the un-clamp's true action count)

        if (verbose)
            Console.WriteLine($"navigate · {Path.GetFileName(dir.TrimEnd('/'))} · {fileDocs.Count} files ({corpusBytes}B) · " +
                              $"{sites.Count} sites · policy k={k} looks≤{maxLooks} persist={SPersist} τ={TauMargin} expand={expand} · path-quoted {quoted.Count}");

        var visited = new List<string>(); var visitedSet = new HashSet<string>();
        long attnBytes = issue.Length; string lastTop = ""; int persist = 0; int looks = 0;
        double know = 0, lastKnow = -1; bool jumpNext = false;
        string verdict = "budget"; double lastMargin = 0;
        List<(Site S, double Score)> field = new();

        for (int look = 0; look < maxLooks; look++)
        {
            // DESCEND — path-quoted first, then down the CURRENT (blended) beacon (un-visited; implementation
            // before test files under the kind prior). A JUMP verdict from the previous look skips k files — the
            // frontier hop past a flat neighbourhood.
            var down = descendOrder.Where(p => !visitedSet.Contains(p));
            if (testPrior) down = down.OrderBy(IsTest).ThenBy(p => descendRank[p]);
            if (jumpNext) { down = down.Skip(k); jumps++; jumpNext = false; }
            var picks = quoted.Where(p => !visitedSet.Contains(p)).Concat(down).Distinct().Take(k).ToList();
            if (picks.Count == 0) { verdict = "pool"; break; }                    // pool exhausted
            foreach (var p in picks)
            {
                var doc = DescendDoc(fileByPath, p);
                visited.Add(p); visitedSet.Add(p);
                attnBytes += doc.Text.Length;
                mind.AddFile(doc.Text);                                           // → the intake pool (line spans)
            }
            looks = look + 1;

            // INDUCE — the full loop: drain the pool by residual frontier, stride-gated re-induce + post-lock
            // breach, one bell round per draw. KNOW = coverage of the ISSUE by the grokked repo grammar (the
            // residual — v1's steering signal, computed every look: it gates expansion, feeds the verdict, and
            // its plateau is JUMP's second condition).
            mind.Drain(look);
            lastKnow = know;
            know = mind.IssueCoverage(issueBytes);

            // EXPAND (grok-GATED) — mint PPMI-coupled terms off the residual's gap-adjacent anchors; DREAM until
            // corroborated. The gate is the k-aware bell: expanding on an un-grokked grammar is PRF drift (the
            // pre-registered grammar-crossover-negative), so before lock the probe stays the bare issue.
            if (expand && mind.Locked && know < 0.999)
            {
                var cand = mind.MintTerms(issueBytes, issueTokSet, minted, bm25, fileDocs.Count);
                if (cand.Count > 0)
                {
                    // CORROBORATION, per term (the Reflection Law at query scale — each mint corroborated INDEPENDENTLY,
                    // not the batch): the currency is the FULL-field top-1 MARGIN — the same concentration signal the
                    // loop lands on. A term VESTS iff adding it to the running blend does not DILUTE the leader
                    // (margin ≥ current − ε); the whole-batch entropy5 test evicted every term because any evidence
                    // spreads the top-5 a hair. Greedy accretion: winners fold into `blend` in Σφ order, losers drop
                    // (revert-the-expansion, DESIGN.md risk 3). This is the sequential information gain v0 lacked.
                    var vestedNow = new List<string>();
                    foreach (var t in cand)
                    {
                        // trial = blend + WExpand·tScore, computed into the reusable scratch (byte-identical to a fresh
                        // clone); on vest we SWAP scratch↔blend (blend adopts the trial array, scratch recycles the old).
                        Array.Copy(blend, scratch, blend.Length);
                        var tScore = bm25.Score([t]);
                        for (int i = 0; i < scratch.Length; i++) scratch[i] += WExpand * tScore[i];
                        double mCur = FieldMargin(sites, blend, visitedSet), mTrial = FieldMargin(sites, scratch, visitedSet);
                        // v1 non-dilution path (sharpen the sitting leader) — always on. UN-CLAMP promotion path:
                        // when --promote, a mint that DECISIVELY surges the margin ALSO vests even if it re-ranks the
                        // leader (the surge IS the corroboration that a different file leads). The leader before/after
                        // the vest is tracked so the telemetry can price promotion's gain (true-positive) vs loss.
                        bool nonDilute = mTrial >= mCur - VestMarginEps;
                        bool surges    = promote && mTrial >= mCur + PromoteMarginEps;
                        if (nonDilute || surges)
                        {
                            string leadBefore = FieldLeader(sites, blend, visitedSet);
                            (blend, scratch) = (scratch, blend); vestedNow.Add(t);
                            if (surges && FieldLeader(sites, blend, visitedSet) != leadBefore) promotions++;
                        }
                        else evictedCount++;
                    }
                    if (vestedNow.Count > 0)
                    {
                        minted.AddRange(vestedNow); vestedCount += vestedNow.Count;
                        (descendOrder, _) = AggregateMaxFiles(sites, blend);      // the re-beacon — the vocabulary bridge
                        descendRank = new Dictionary<string, int>();
                        for (int i = 0; i < descendOrder.Count; i++) descendRank[descendOrder[i]] = i;
                        if (verbose) Console.WriteLine($"    minted+vested [{string.Join(" ", vestedNow)}] · evicted {cand.Count - vestedNow.Count}");
                    }
                    else if (verbose) Console.WriteLine($"    all {cand.Count} evicted — none corroborated (margin diluted)");
                }
            }

            // DEDUCE — the LOCAL field: the VISITED files' sites with their BLENDED global scores (candidate-set
            // pruning + additive vested evidence; never a local re-weighting). All kinds incl. module — the file
            // rank aggregates max over this; the fn ranking filters to function/method downstream.
            // OrderByDescending is stable → equal scores keep site order (the Vow).
            field = Enumerable.Range(0, sites.Count).Where(i => visitedSet.Contains(sites[i].Path))
                              .Select(i => (S: sites[i], Score: blend[i])).OrderByDescending(x => x.Score).ToList();

            // STABILIZE — the calibrated verdict. LAND: concentration + persistence (v0 law). JUMP: the field is
            // FLAT and the residual PLATEAUED — more of this neighbourhood explains nothing new, hop the beacon.
            var fileLocal = field.GroupBy(x => x.S.Path).Select(gr => (Path: gr.Key, Score: gr.Max(x => x.Score)))
                                 .OrderByDescending(x => x.Score).ThenBy(x => beaconRank[x.Path]).ToList();
            string top = fileLocal.Count > 0 ? fileLocal[0].Path : "";
            double margin = fileLocal.Count > 1 && fileLocal[0].Score > 0 ? (fileLocal[0].Score - fileLocal[1].Score) / fileLocal[0].Score : 1.0;
            persist = top == lastTop && top.Length > 0 ? persist + 1 : 0; lastTop = top; lastMargin = margin;
            bool land = persist >= SPersist - 1 && margin >= TauMargin && look > 0;
            bool flat = margin < TauFlat && lastKnow >= 0 && Math.Abs(know - lastKnow) < CovPlateauEps && look > 0;
            if (flat && !land) jumpNext = true;
            if (verbose)
                Console.WriteLine($"  look {look} · +[{string.Join(", ", picks.Select(Base))}] · attn {attnBytes}B ({100.0 * attnBytes / Math.Max(1, corpusBytes):F1}%) · " +
                                  $"know {know:P0} · {(mind.Locked ? "LOCKED" : $"cvz {mind.CvZ:F3}")} · top {Base(top)} · margin {margin:F2} · persist {persist + 1} → {(land ? "LAND" : flat ? "JUMP" : "continue")}");
            if (land) { verdict = "land"; break; }
        }

        // LAND (head only — the harness splices the tail). Visited files by the blended local field, promotion
        // GUARDED (near-ties restore STATIC beacon order — the rerank-regression guard; a vested mint must earn a
        // DECISIVE margin to move a file); then the kind prior (stable demote).
        var landedVisited = field.GroupBy(x => x.S.Path).Select(gr => (Path: gr.Key, Score: gr.Max(x => x.Score)))
                                 .OrderByDescending(x => x.Score).ThenBy(x => beaconRank[x.Path]).ToList();
        for (int i = 0; i + 1 < landedVisited.Count; i++)
        {
            var (a, b) = (landedVisited[i], landedVisited[i + 1]);
            bool nearTie = a.Score <= 0 || (a.Score - b.Score) / a.Score < TauPromote;
            if (nearTie && beaconRank[b.Path] < beaconRank[a.Path]) (landedVisited[i], landedVisited[i + 1]) = (b, a);
        }
        var visitedFiles = landedVisited.Select(x => x.Path).ToList();
        if (testPrior) visitedFiles = visitedFiles.OrderBy(IsTest).ToList();     // stable — band order preserved
        var localFns = field.Where(x => x.S.Kind is "function" or "method");
        if (testPrior) localFns = localFns.OrderBy(x => IsTest(x.S.Path));
        var localFnSites = localFns.Select(x => (x.S.Path, x.S.Name, x.S.Start, x.S.End, x.Score)).ToList();

        // P(correct) — the residual priced (pre-registered monotone blend of the verdict's own reads: field
        // concentration, persistence, issue coverage). The escalation channel reads the LOW band.
        double pCorrect = 0.45 * Math.Min(1.0, lastMargin / 0.5)
                        + 0.35 * Math.Min(1.0, (persist + 1) / (double)SPersist)
                        + 0.20 * know;
        return new NavResult(Path.GetFileName(dir.TrimEnd('/')), visitedFiles, localFnSites, looks, attnBytes, corpusBytes,
                             verdict, pCorrect, jumps, mind.Locked, mind.LockLook, mind.CvZ, mind.KZ, mind.MaxSpan,
                             mind.RuleCount, mind.TapeBytes, know, vestedCount, evictedCount, promotions, minted);
    }

    /// The visited file field's current TOP-1 file path (the aggregate-max leader) — the un-clamp reads this
    /// before/after a vest to detect a PROMOTION (the leader changed), separating margin-surges that merely sharpen
    /// the sitting leader from those that install a new one (the file@1-moving action the promotion channel exists for).
    private static string FieldLeader(List<Site> sites, double[] score, HashSet<string> visitedSet)
    {
        string best = ""; double bestScore = double.NegativeInfinity;
        var fileBest = new Dictionary<string, double>();
        for (int i = 0; i < sites.Count; i++)
        {
            if (!visitedSet.Contains(sites[i].Path)) continue;
            if (!fileBest.TryGetValue(sites[i].Path, out var s) || score[i] > s) fileBest[sites[i].Path] = score[i];
        }
        foreach (var (p, s) in fileBest) if (s > bestScore) { bestScore = s; best = p; }
        return best;
    }

    // ── the rankings-jsonl line (world boundary — JSON) the scoring harness consumes; the v1 telemetry rides as
    // EXTRA keys (verdict/grok/vesting + the gold head-hits — print-only reads, the scorer ignores them) ──
    private static string EmitJson(NavResult r, bool goldFile1, bool goldFn5)
    {
        var sb = new StringBuilder();
        sb.Append("{\"instance_id\":").Append(JsonStr(r.Instance));
        sb.Append(",\"looks\":").Append(r.Looks).Append(",\"attn_bytes\":").Append(r.AttnBytes).Append(",\"corpus_bytes\":").Append(r.CorpusBytes);
        sb.Append(",\"verdict\":").Append(JsonStr(r.Verdict)).Append(",\"p_correct\":").Append(R(r.PCorrect)).Append(",\"jumps\":").Append(r.Jumps);
        sb.Append(",\"locked\":").Append(r.Locked ? "true" : "false").Append(",\"lock_look\":").Append(r.LockLook);
        sb.Append(",\"cvz\":").Append(double.IsNaN(r.CvZ) ? "null" : R(r.CvZ)).Append(",\"kz\":").Append(r.KZ);
        sb.Append(",\"maxspan\":").Append(R(r.MaxSpan)).Append(",\"rules\":").Append(r.Rules).Append(",\"tape_bytes\":").Append(r.TapeBytes);
        sb.Append(",\"know\":").Append(R(r.Know)).Append(",\"vested\":").Append(r.OutcomeCredited).Append(",\"evicted\":").Append(r.Evicted).Append(",\"promotions\":").Append(r.Admissions);
        sb.Append(",\"minted\":[").Append(string.Join(",", r.MintedTerms.Select(JsonStr))).Append(']');
        sb.Append(",\"gold_file1_head\":").Append(goldFile1 ? "true" : "false").Append(",\"gold_fn5_head\":").Append(goldFn5 ? "true" : "false");
        sb.Append(",\"visited_files\":[").Append(string.Join(",", r.VisitedFiles.Select(JsonStr))).Append(']');
        sb.Append(",\"local_fn_sites\":[");
        for (int i = 0; i < r.LocalFnSites.Count; i++)
        {
            var s = r.LocalFnSites[i];
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(JsonStr(s.Path)).Append(',').Append(JsonStr(s.Name)).Append(',')
              .Append(s.Start).Append(',').Append(s.End).Append(',').Append(R(s.Score)).Append(']');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string Base(string path) => path.Length == 0 ? "—" : Path.GetFileName(path);
    private static string Fmt(int rank) => rank <= 0 ? "miss" : $"#{rank}";
    private static int RankOf(List<string> order, HashSet<string> gold)
    { for (int i = 0; i < order.Count; i++) if (gold.Contains(order[i])) return i + 1; return 0; }
}
