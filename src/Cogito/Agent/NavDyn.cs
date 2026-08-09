namespace Cogito;

using System.Text;
using Cogito.Grammar;
using static Cogito.Loc;   // the localization substrate: Site · Bm25Index · StandingGrammar · Toks · Load* · AggregateMaxFiles · FieldMargin · HeadRank · RepoOf · R · JsonStr · loop consts · checkpoint helpers


// ── NAVDYN ──  the DYNAMIC / FULL-METABOLISM localization bench — the LIFELONG-LEARNING mode of `navigate`.
//
// `navigate --all` runs the 300 SWE-bench-Lite instances FROZEN: each instance re-seeds a fresh RepoGrok from the
// SAME read-only pretrain checkpoint, disposes it, and re-derives its BM25 ranking from scratch. Nothing carries
// between instances — the standard frozen-model paradigm, where the "authoritative managerial system" is a static
// checkpoint. That is the WRONG mode for a living mind. `navdyn` runs the same 300 IN ORDER (instance_id-sorted, so
// they cluster by repo — earlier instances of a repo teach later ones) with a STANDING mind that CONTINUES LEARNING
// through the eval, along two channels the frozen mode structurally cannot enter:
//
//   (1) THE GRAMMAR RICHENS.  A persistent StandingGrammar accretes across the stream: each instance's grokked repo
//       grammar contributes its NOVEL binary rules (dedup by pattern) to a growing vocabulary that seeds the NEXT
//       instance's induction. The mind that touches instance 250 knows the structure it learned on instances 1–249;
//       within a repo, that is the same codebase's idioms compounding. The frozen mode throws this away every dispose.
//
//   (2) THE RANKING LEARNS FROM THE OUTCOME.  After each instance LANDS, the gold file is read and the ranking's
//       online weight-head updates: every term of the gold-file sites has its gold-corroboration counter bumped, and
//       a term's learned boost = its empirical P(term ∈ gold-file | term seen). Future instances blend this learned
//       field into the base BM25 — the ranking ADAPTS per instance, learned FROM THE ANSWER. This DELIBERATELY
//       "cheats" the frozen paradigm (the mind learns from the localization outcome, teaching the next sample); that
//       is the POINT — it is the online-learning form of the weight-head, and it is a game a frozen baseline cannot
//       play. It is also what breaks the rank-1 clamp the frozen outcomeCredit gate enforces: the learned signal comes from
//       the gold outcome, not from margin-non-dilution, so a corroborated term CAN install a new leader.
//
// THE DECISIVE METRIC IS THE SLOPE, NOT THE AGGREGATE.  The number `navdyn` reports is NOT comparable to a frozen
// baseline (different eval — lifelong vs frozen-standard) and MUST NOT be quoted as if it were. The honest read is
// the TRAJECTORY: does the running file@1 / fn@5 CLIMB across the stream as the mind learns (does the last decile beat
// the first on comparable difficulty)? A rising slope = the lifelong mind pulling away from the frozen floor. A flat
// slope = learning-through-eval doesn't help here, a real finding reported straight. The by-arrival deciles + the
// per-repo within-repo slope (does instance #k of a repo beat instance #1 of the same repo) are the verdict reads.
//
// This organ deliberately REUSES navigate's public ranking law (Bm25Index — gret's NG-BM25, byte-identical
// tokens) and the same induction organs (Loom / GrammarCover / Couplings / AnnealEvict / DomainMeter) the trunk
// proved, but reimplements the driver because the FROZEN RepoGrok is per-instance-disposed by contract — the
// persistent mind is a genuinely different organ, not a duplicate. Deterministic: instance_id-sorted arrival, seeded
// breach, stable sorts. Release-only, gold read to STEER the online update (the embraced cheat) — never quietly.
public static class NavDyn
{
    // The loop policy + induction/expansion knobs are shared (Loc + RepoGrok own them). Navdyn keeps only its
    // channel-2 (online ranking head) knobs; channel 1's StandingRuleCap is Loc's (shared with navloop).
    public  const double WGold           = 0.5;    // the learned field's weight in the blend (base + WExpand·mint + WGold·learned) — same order as the mint (learned evidence re-ranks, never drowns the base). Public: the CLI's --wgold default reads it.
    private const int    GoldMinSeen     = 3;       // a term earns a learned boost only after this many observations (the online-PPMI regularization — a term seen once in one gold file is not yet evidence)
    private const int    GoldTermCap     = 64;      // top-N gold-file terms folded per solved instance (by within-instance df-rarity — the discriminative gold vocabulary, not the file's stopwords)
    private const double GoldBoostCap    = 4.0;     // clamp on log(1+goldRate·scale) so one hyper-predictive term can't dominate the field

    public sealed record NavDynResult(
        string Instance, List<string> VisitedFiles, List<string> BeaconFiles, List<string> BaseBeaconFiles,
        List<(string Path, string Name, int Start, int End, double Score)> LocalFnSites,
        int Looks, double MaxSpan, bool Locked, int LockLook, int OutcomeCredited, int Evicted, int Admissions,
        int StandingRules, int LearnedTerms, string Verdict);

    /// usage: navdyn <data-dir> [--limit N] [--pretrain <runDir>] [--no-grammar-carry] [--no-rank-learn] [--no-expand]
    /// The run lands in runs/navdyn_NNNN/ (journal.log · curve.tsv · rankings.jsonl · report.txt · config — the engine
    /// logs the whole run into the run dir; the `run → …` locator says where). The dynamic eval. --pretrain seeds the
    /// STANDING grammar's initial state (the mind starts warm, then accretes on
    /// top); omitted = cold (the standing grammar grows from empty across the stream). The two learning channels are
    /// independently ablatable: --no-grammar-carry freezes channel 1 (re-seed the same base every instance = navigate's
    /// frozen grammar), --no-rank-learn freezes channel 2 (base BM25 only, no learned gold field). Both off ⇒ this
    /// degrades to navigate's frozen loop (the ablation floor — the slope MUST be flat there, the control).
    /// The dynamic mode entry (nav --mode dyn). The standing mind learns THROUGH the stream: channel 1 (grammarCarry)
    /// accretes the standing grammar, channel 2 (rankLearn) the learned gold field. Both off ⇒ this degrades to the
    /// frozen loop (the ablation floor). "" pretrainDir = cold (the standing grammar grows from empty).
    public static int Run(string dir, int maxLooks, int k, bool grammarCarry, bool rankLearn, bool expand,
                          double wGold, string pretrainDir, int limit, int checkpointEvery)
    {
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"  nav --mode dyn: '{dir}' is not a directory"); return 1; }
        var pretrain = LoadPretrainBase(pretrainDir);
        return RunStream(dir, limit, maxLooks, k, grammarCarry, rankLearn, expand, wGold, checkpointEvery, pretrain);
    }

    // ── load a checkpoint's trained grammar as the standing grammar's SEED (binary prefix only — Loom.Seed's
    // contract; the consolidated suffix is re-derivable from the repo splices). ──
    private static List<GrammarRule> LoadPretrainBase(string runDir)
    {
        if (runDir.Length == 0) return new List<GrammarRule>();
        var (raw, bin) = LoadBinaryPrefix(runDir);
        Console.WriteLine($"  standing-seed ← {runDir} · {raw.Rules.Length} trained rules · seeding {bin.Count} (binary prefix)");
        return bin;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE STREAM DRIVER — the 300 instances in instance_id order, threading the STANDING MIND (grammar + learned
    //  ranking) through every Drive. The gold outcome updates the mind AFTER each land (the embraced online cheat).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static int RunStream(string dataDir, int limit, int maxLooks, int k, bool grammarCarry, bool rankLearn, bool expand, double wGold, int checkpointEvery, List<GrammarRule> pretrain)
    {
        var dirs = Directory.GetDirectories(dataDir)
                            .Where(d => File.Exists(Path.Combine(d, "query.txt")) && File.Exists(Path.Combine(d, "sites.jsonl")))
                            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).Take(limit).ToList();
        // the run's config snapshot = the deterministic policy telegraph (same config + same data ⇒ same run — the
        // replay config, landed as `config` in the run dir by AgentRun.Begin).
        string config = $"navdyn · {dirs.Count} instances (instance_id-sorted) · k={k} looks≤{maxLooks} · " +
                        $"channel1 grammar-carry={grammarCarry} · channel2 rank-learn={rankLearn} (wgold={wGold}) · expand={expand} · seed={(pretrain.Count == 0 ? "COLD" : $"{pretrain.Count} rules")}";
        using var ar = AgentRun.Begin("navdyn", config, new IntakeManifest(dataDir, IntakeManifest.Of(dataDir), $"limit={limit}"));
        Console.WriteLine($"navdyn · {dirs.Count} instances (instance_id-sorted — earlier teaches later) · policy k={k} looks≤{maxLooks} · " +
                          $"channel1 grammar-carry={grammarCarry} · channel2 rank-learn={rankLearn} (wgold={wGold}) · expand={expand} · seed={(pretrain.Count == 0 ? "COLD" : $"{pretrain.Count} rules")}");

        // THE STANDING MIND — the two persistent learners, alive across all 300.
        var standing = new StandingGrammar(pretrain, StandingRuleCap);   // channel 1
        var head = new RankHead();                                        // channel 2

        long t0 = Environment.TickCount64;
        var stream = new List<(NavDynResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)>();
        var repoSeq = new Dictionary<string, int>(StringComparer.Ordinal);
        int hitF1 = 0, hitBF1 = 0, hitF5 = 0;
        int rankRecover = 0, rankLoss = 0, rankImprove = 0, rankWorsen = 0;   // channel-2 effect vs base BM25 (recover = base-miss→blend-hit@1; the demolition action count)
        // the per-instance results (rankings.jsonl) + the running curve (curve.tsv) — both in the run dir, auto-flushed.
        var w = ar.Rankings;
        var curve = ar.Curve("idx\tinstance_id\trepo\trepo_seq\tgold_file_rank\tgold_beacon_rank\tgold_fn_rank\tfile_hit_at1\tbeacon_hit_at1\tfn_hit_at5\tstanding_rules\tlearned_terms\tmaxspan\tpromotions");
        {
            for (int n = 0; n < dirs.Count; n++)
            {
                string id = Path.GetFileName(dirs[n].TrimEnd('/'));
                string repo = RepoOf(id);
                int rseq = repoSeq.GetValueOrDefault(repo, 0);           // 0-based: this instance is the (rseq)-th of its repo
                repoSeq[repo] = rseq + 1;

                var r = Drive(dirs[n], maxLooks, k, verbose: false, expand, grammarCarry, rankLearn, wGold, standing, head, out var sites, out var bm25);
                var (goldFiles, goldFns) = LoadGold(Path.Combine(dirs[n], "gold.json"));
                bool f1  = r.VisitedFiles.Count > 0 && goldFiles.Contains(r.VisitedFiles[0]);   // VISITED-set file@1 (where expansion already works)
                bool bf1 = r.BeaconFiles.Count > 0 && goldFiles.Contains(r.BeaconFiles[0]);     // BEACON file@1 (the hard clamp — the coordinator's locus)
                bool f5  = r.LocalFnSites.Take(5).Any(x => goldFns.Contains((x.Path, x.Name)));
                int goldFileRank   = HeadRank(r.VisitedFiles, goldFiles);
                int goldBeaconRank = HeadRank(r.BeaconFiles, goldFiles);                        // gold's rank on the FULL-corpus beacon — the demolition read
                int goldBaseRank   = HeadRank(r.BaseBeaconFiles, goldFiles);                    // gold's rank on the BASE-only beacon (pure BM25) — the channel-2 counterfactual
                int goldFnRank     = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name))) + 1;
                // the CHANNEL-2 EFFECT, per instance: did the learned field move gold's beacon rank vs pure BM25?
                bool baseHit1 = goldBaseRank == 1;
                if (baseHit1 && !bf1) rankLoss++;           // learned field DEMOTED gold off rank-1 (the cost)
                if (!baseHit1 && bf1) rankRecover++;        // learned field PROMOTED gold to rank-1 that BM25 missed (the demolition action)
                if (goldBaseRank > 0 && goldBeaconRank > 0 && goldBeaconRank < goldBaseRank) rankImprove++;   // learned field improved gold's rank (any climb)
                if (goldBaseRank > 0 && goldBeaconRank > 0 && goldBeaconRank > goldBaseRank) rankWorsen++;

                // ── THE ONLINE UPDATE (the embraced cheat): teach the ranking head from the gold outcome. Runs
                // AFTER the instance is scored, so this instance's own rank was produced by the mind as it stood
                // BEFORE seeing this answer — the learning flows strictly forward to future instances. ──
                if (rankLearn) head.Learn(sites, bm25, goldFiles);

                stream.Add((r, f1, bf1, f5, repo, rseq));
                w.WriteLine(EmitJson(r, f1, bf1, f5, goldBeaconRank, repo, rseq));
                if (f1) hitF1++; if (bf1) hitBF1++; if (f5) hitF5++;
                curve.WriteLine($"{n + 1}\t{r.Instance}\t{repo}\t{rseq}\t{(goldFileRank == 0 ? "miss" : goldFileRank)}\t{(goldBeaconRank == 0 ? "miss" : goldBeaconRank)}\t{(goldFnRank == 0 ? "miss" : goldFnRank)}\t{(f1 ? 1 : 0)}\t{(bf1 ? 1 : 0)}\t{(f5 ? 1 : 0)}\t{r.StandingRules}\t{r.LearnedTerms}\t{r.MaxSpan:F0}\t{r.Admissions}");
                ar.Journal.Index(n + 1, $"navdyn {r.Instance}({repo}#{rseq}) · gold file-rank {(goldFileRank == 0 ? "miss" : goldFileRank.ToString())}/beacon {(goldBeaconRank == 0 ? "miss" : goldBeaconRank.ToString())}/fn {(goldFnRank == 0 ? "miss" : goldFnRank.ToString())} · file@1={(f1 ? 1 : 0)} bcn@1={(bf1 ? 1 : 0)} fn@5={(f5 ? 1 : 0)} · standing {r.StandingRules} learned {r.LearnedTerms} maxspan {r.MaxSpan:F0} promo {r.Admissions}");
                if (checkpointEvery > 0 && (n + 1) % checkpointEvery == 0)
                    SaveCheckpoint(ar.Run, ar.Journal, config, n + 1, maxLooks, k, grammarCarry, rankLearn, expand, wGold, checkpointEvery, dirs, standing, head, repoSeq, hitF1, hitBF1, hitF5, rankRecover, rankLoss, rankImprove, rankWorsen, t0);
                if ((n + 1) % 10 == 0 || n + 1 == dirs.Count)
                    Console.WriteLine($"  [{n + 1}/{dirs.Count}] {(Environment.TickCount64 - t0) / 1000.0:F0}s · visited-file@1={100.0 * hitF1 / (n + 1):F1}% BEACON-file@1={100.0 * hitBF1 / (n + 1):F1}% fn@5={100.0 * hitF5 / (n + 1):F1}% · " +
                                      $"standing {r.StandingRules} rules · learned {r.LearnedTerms} terms · last {r.Instance}({repo}#{rseq}) {(r.Locked ? $"LOCK@{r.LockLook}" : "unlkd")} vest {r.OutcomeCredited} promo {r.Admissions}");
            }
        }
        // THE VERDICT BLOCK — the run's own read, teed: printed to the console AND landed as report.txt so the run
        // carries its verdict. The channel-2 tally is the honest "did online learning break the clamp" (recoveries
        // base-miss→hit@1 MINUS losses = the net file@1 gain the learned field bought); ReportSlope is the trajectory.
        var report = new StringBuilder();
        report.Append($"  → {dirs.Count} instances → {ar.Dir} ({(Environment.TickCount64 - t0) / 1000.0:F0}s) · final standing {standing.Count} rules · learned {head.TermCount} terms · " +
                      $"visited-file@1 {100.0 * hitF1 / dirs.Count:F1}% · BEACON-file@1 {100.0 * hitBF1 / dirs.Count:F1}% · fn@5 {100.0 * hitF5 / dirs.Count:F1}%\n");
        report.Append($"  channel-2 tally (learned beacon vs pure BM25): recover@1 {rankRecover} · loss@1 {rankLoss} · NET@1 {rankRecover - rankLoss:+0;-0} · rank-improve {rankImprove} · rank-worsen {rankWorsen} · net-rank {rankImprove - rankWorsen:+0;-0}\n");
        ReportSlope(report, stream);
        Console.Write(report);
        ar.Write("report.txt", report.ToString());
        return 0;
    }

    public static int Resume(string runDir, bool verify = false, int steps = 0)
    {
        if (verify) { Console.Error.WriteLine("  navdyn resume --verify is not implemented yet; use the full byte-match gate"); return 1; }
        if (steps != 0) { Console.Error.WriteLine("  navdyn resume does not support --steps; the instance horizon rides the checkpointed stream"); return 1; }
        var dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, AgentCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {AgentCheckpoint.FileName} under '{runDir}' — nothing to resume");
            return 1;
        }
        var run = Cogito.Run.Open(dir);
        int maxLooks = 0, k = 0, checkpointEvery = 0, hitF1 = 0, hitBF1 = 0, hitF5 = 0, rankRecover = 0, rankLoss = 0, rankImprove = 0, rankWorsen = 0;
        bool grammarCarry = false, rankLearn = false, expand = false;
        double wGold = 0;
        long t0 = Environment.TickCount64;
        List<string> dirs = new();
        Dictionary<string, int> repoSeq = new(StringComparer.Ordinal);
        StandingGrammar? standing = null;
        RankHead? head = null;
        var (_, snap, journal) = AgentCheckpoint.Load(run.Dir, AgentCheckpoint.AgentVerbs.NavDyn, r =>
        {
            maxLooks = r.I32(); k = r.I32(); grammarCarry = r.Bool(); rankLearn = r.Bool(); expand = r.Bool(); wGold = r.F64(); checkpointEvery = r.I32();
            dirs = ReadStrings(r);
            standing = new StandingGrammar([], StandingRuleCap); standing.Load(r);
            head = new RankHead(); head.Load(r);
            repoSeq = ReadIntMap(r);
            hitF1 = r.I32(); hitBF1 = r.I32(); hitF5 = r.I32();
            rankRecover = r.I32(); rankLoss = r.I32(); rankImprove = r.I32(); rankWorsen = r.I32();
            t0 = Environment.TickCount64 - r.I64();
            return new Tape();
        });
        if (standing is null || head is null) throw new InvalidDataException("navdyn checkpoint did not restore a mind");
        journal.Rewrite(run, header: false);
        run.Truncate("rankings.jsonl", snap.RankingsLen);
        run.TruncateCurve("curve.tsv", snap.CurveLen);
        using var ar = AgentRun.Resume(run, journal);
        var w = ar.Rankings;
        var curve = ar.Curve("idx\tinstance_id\trepo\trepo_seq\tgold_file_rank\tgold_beacon_rank\tgold_fn_rank\tfile_hit_at1\tbeacon_hit_at1\tfn_hit_at5\tstanding_rules\tlearned_terms\tmaxspan\tpromotions");
        Console.WriteLine($"navdyn ⇄ {Path.GetFileName(run.Dir)} · resumed at instance {snap.Next}/{dirs.Count}");
        var stream = new List<(NavDynResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)>();
        for (int n = snap.Next; n < dirs.Count; n++)
        {
            string id = Path.GetFileName(dirs[n].TrimEnd('/'));
            string repo = RepoOf(id);
            int rseq = repoSeq.GetValueOrDefault(repo, 0);
            repoSeq[repo] = rseq + 1;
            var r = Drive(dirs[n], maxLooks, k, verbose: false, expand, grammarCarry, rankLearn, wGold, standing, head, out var sites, out var bm25);
            var (goldFiles, goldFns) = LoadGold(Path.Combine(dirs[n], "gold.json"));
            bool f1 = r.VisitedFiles.Count > 0 && goldFiles.Contains(r.VisitedFiles[0]);
            bool bf1 = r.BeaconFiles.Count > 0 && goldFiles.Contains(r.BeaconFiles[0]);
            bool f5 = r.LocalFnSites.Take(5).Any(x => goldFns.Contains((x.Path, x.Name)));
            int goldFileRank = HeadRank(r.VisitedFiles, goldFiles);
            int goldBeaconRank = HeadRank(r.BeaconFiles, goldFiles);
            int goldBaseRank = HeadRank(r.BaseBeaconFiles, goldFiles);
            int goldFnRank = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name))) + 1;
            bool baseHit1 = goldBaseRank == 1;
            if (baseHit1 && !bf1) rankLoss++;
            if (!baseHit1 && bf1) rankRecover++;
            if (goldBaseRank > 0 && goldBeaconRank > 0 && goldBeaconRank < goldBaseRank) rankImprove++;
            if (goldBaseRank > 0 && goldBeaconRank > 0 && goldBeaconRank > goldBaseRank) rankWorsen++;
            if (rankLearn) head.Learn(sites, bm25, goldFiles);
            stream.Add((r, f1, bf1, f5, repo, rseq));
            w.WriteLine(EmitJson(r, f1, bf1, f5, goldBeaconRank, repo, rseq));
            if (f1) hitF1++; if (bf1) hitBF1++; if (f5) hitF5++;
            curve.WriteLine($"{n + 1}\t{r.Instance}\t{repo}\t{rseq}\t{(goldFileRank == 0 ? "miss" : goldFileRank)}\t{(goldBeaconRank == 0 ? "miss" : goldBeaconRank)}\t{(goldFnRank == 0 ? "miss" : goldFnRank)}\t{(f1 ? 1 : 0)}\t{(bf1 ? 1 : 0)}\t{(f5 ? 1 : 0)}\t{r.StandingRules}\t{r.LearnedTerms}\t{r.MaxSpan:F0}\t{r.Admissions}");
            ar.Journal.Index(n + 1, $"navdyn {r.Instance}({repo}#{rseq}) · gold file-rank {(goldFileRank == 0 ? "miss" : goldFileRank.ToString())}/beacon {(goldBeaconRank == 0 ? "miss" : goldBeaconRank.ToString())}/fn {(goldFnRank == 0 ? "miss" : goldFnRank.ToString())} · file@1={(f1 ? 1 : 0)} bcn@1={(bf1 ? 1 : 0)} fn@5={(f5 ? 1 : 0)} · standing {r.StandingRules} learned {r.LearnedTerms} maxspan {r.MaxSpan:F0} promo {r.Admissions}");
            if (checkpointEvery > 0 && (n + 1) % checkpointEvery == 0)
                SaveCheckpoint(run, ar.Journal, File.ReadAllText(run.PathOf("config")).TrimEnd(), n + 1, maxLooks, k, grammarCarry, rankLearn, expand, wGold, checkpointEvery, dirs, standing, head, repoSeq, hitF1, hitBF1, hitF5, rankRecover, rankLoss, rankImprove, rankWorsen, t0);
        }
        var report = new StringBuilder();
        report.Append($"  → {dirs.Count} instances → {ar.Dir} ({(Environment.TickCount64 - t0) / 1000.0:F0}s) · final standing {standing.Count} rules · learned {head.TermCount} terms · " +
                      $"visited-file@1 {100.0 * hitF1 / dirs.Count:F1}% · BEACON-file@1 {100.0 * hitBF1 / dirs.Count:F1}% · fn@5 {100.0 * hitF5 / dirs.Count:F1}%\n");
        report.Append($"  channel-2 tally (learned beacon vs pure BM25): recover@1 {rankRecover} · loss@1 {rankLoss} · NET@1 {rankRecover - rankLoss:+0;-0} · rank-improve {rankImprove} · rank-worsen {rankWorsen} · net-rank {rankImprove - rankWorsen:+0;-0}\n");
        ReportSlope(report, stream);
        Console.Write(report);
        ar.Write("report.txt", report.ToString());
        return 0;
    }

    private static void SaveCheckpoint(Run run, Journal journal, string config, int next, int maxLooks, int k, bool grammarCarry, bool rankLearn, bool expand, double wGold, int checkpointEvery,
        List<string> dirs, StandingGrammar standing, RankHead head, Dictionary<string, int> repoSeq, int hitF1, int hitBF1, int hitF5, int rankRecover, int rankLoss, int rankImprove, int rankWorsen, long t0)
    {
        var snap = new AgentCheckpoint.StreamSnap(next, 0, Len(run, "journal.log"), Len(run, "rankings.jsonl"), Len(run, "curve.tsv"));
        var image = AgentCheckpoint.Encode(AgentCheckpoint.AgentVerbs.NavDyn, config, snap, journal, w =>
        {
            w.I32(maxLooks); w.I32(k); w.Bool(grammarCarry); w.Bool(rankLearn); w.Bool(expand); w.F64(wGold); w.I32(checkpointEvery);
            WriteStrings(w, dirs);
            standing.Save(w);
            head.Save(w);
            WriteIntMap(w, repoSeq);
            w.I32(hitF1); w.I32(hitBF1); w.I32(hitF5);
            w.I32(rankRecover); w.I32(rankLoss); w.I32(rankImprove); w.I32(rankWorsen);
            w.I64(Environment.TickCount64 - t0);
        });
        AgentCheckpoint.Save(run, image);
    }

    // ── THE SLOPE — the decisive read. Three cuts: (1) BY-ARRIVAL deciles (does the running score climb as the mind
    // learns across the whole stream); (2) WITHIN-REPO (does instance #k of a repo beat instance #1 of the same repo
    // — the cleanest "earlier teaches later" isolation, difficulty held ~constant by same-codebase); (3) the maxSpan
    // grok-depth buckets (navigate parity). A rising by-arrival OR within-repo slope = the lifelong mind pulling away
    // from the frozen floor; flat = learning-through-eval didn't help HERE (reported straight). ──
    private static void ReportSlope(StringBuilder sb, List<(NavDynResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)> rows)
    {
        if (rows.Count == 0) return;
        sb.Append($"\n  ── the SLOPE ({rows.Count} instances; head-only accuracy, gold STEERS the online update — the dynamic/lifelong eval, NOT frozen-comparable) ──\n");
        sb.Append("    two file@1 surfaces: VISITED (visited-set aggregate, where expansion already works) · BEACON (full-corpus rank-1, the HARD CLAMP the frozen fixed-gate couldn't move — a rising beacon slope is the demolition signal)\n");

        // (1) BY-ARRIVAL deciles — the whole-stream learning trajectory, both surfaces.
        sb.Append("  by-arrival deciles (does the score CLIMB as the mind accretes across the stream):\n");
        int D = Math.Min(10, rows.Count);
        for (int d = 0; d < D; d++)
        {
            int lo = d * rows.Count / D, hi = (d + 1) * rows.Count / D;
            var b = rows.GetRange(lo, hi - lo);
            if (b.Count == 0) continue;
            sb.Append($"    decile {d + 1,2} [{lo + 1,3}–{hi,3}] n={b.Count,3} · vis-file@1 {100.0 * b.Count(x => x.File1) / b.Count,5:F1} · BEACON-file@1 {100.0 * b.Count(x => x.BeaconF1) / b.Count,5:F1} · fn@5 {100.0 * b.Count(x => x.Fn5) / b.Count,5:F1} · " +
                      $"standing {(int)b.Average(x => x.R.StandingRules),6} · learned {(int)b.Average(x => x.R.LearnedTerms),5}\n");
        }
        // the crude slope: last-third minus first-third, BOTH surfaces (the headline trajectory numbers).
        int third = Math.Max(1, rows.Count / 3);
        var first = rows.GetRange(0, third); var last = rows.GetRange(rows.Count - third, third);
        double vFirst = 100.0 * first.Count(x => x.File1) / first.Count, vLast = 100.0 * last.Count(x => x.File1) / last.Count;
        double bFirst = 100.0 * first.Count(x => x.BeaconF1) / first.Count, bLast = 100.0 * last.Count(x => x.BeaconF1) / last.Count;
        double f5First = 100.0 * first.Count(x => x.Fn5) / first.Count, f5Last = 100.0 * last.Count(x => x.Fn5) / last.Count;
        sb.Append($"  ARRIVAL SLOPE: vis-file@1 {vFirst:F1}→{vLast:F1} (Δ{vLast - vFirst:+0.0;-0.0}) · BEACON-file@1 {bFirst:F1}→{bLast:F1} (Δ{bLast - bFirst:+0.0;-0.0}) · fn@5 {f5First:F1}→{f5Last:F1} (Δ{f5Last - f5First:+0.0;-0.0})\n");

        // (2) WITHIN-REPO — the cleanest earlier-teaches-later isolation. Compare a repo's EARLY instances (seq < half)
        // vs its LATE instances (seq ≥ half): same codebase, so grammar-carry + repo-vocabulary-learning show up with
        // difficulty roughly held. Only repos with ≥4 instances contribute (need both bins populated). BOTH surfaces.
        sb.Append("  within-repo slope (same codebase — early seq vs late seq, difficulty ~held · vis-file@1 / BEACON-file@1):\n");
        var byRepo = rows.GroupBy(x => x.Repo).Where(g => g.Count() >= 4).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        int veHit = 0, veN = 0, vlHit = 0, vlN = 0, beHit = 0, blHit = 0;
        foreach (var g in byRepo)
        {
            var items = g.OrderBy(x => x.RepoSeq).ToList();
            int half = items.Count / 2;
            var early = items.GetRange(0, half); var late = items.GetRange(half, items.Count - half);
            int veh = early.Count(x => x.File1), vlh = late.Count(x => x.File1);
            int beh = early.Count(x => x.BeaconF1), blh = late.Count(x => x.BeaconF1);
            veHit += veh; veN += early.Count; vlHit += vlh; vlN += late.Count; beHit += beh; blHit += blh;
            sb.Append($"    {g.Key,-12} n={items.Count,3} · early[{early.Count}] vis {100.0 * veh / Math.Max(1, early.Count),5:F1}/bcn {100.0 * beh / Math.Max(1, early.Count),5:F1} → late[{late.Count}] vis {100.0 * vlh / Math.Max(1, late.Count),5:F1}/bcn {100.0 * blh / Math.Max(1, late.Count),5:F1}\n");
        }
        if (veN > 0 && vlN > 0)
            sb.Append($"  WITHIN-REPO SLOPE: vis-file@1 {100.0 * veHit / veN:F1}→{100.0 * vlHit / vlN:F1} (Δ{100.0 * vlHit / vlN - 100.0 * veHit / veN:+0.0;-0.0}) · BEACON-file@1 {100.0 * beHit / veN:F1}→{100.0 * blHit / vlN:F1} (Δ{100.0 * blHit / vlN - 100.0 * beHit / veN:+0.0;-0.0}) · pooled over {byRepo.Count} repos\n");

        // (3) grok-depth buckets (navigate parity) + the outcomeCredit/promotion action counts.
        int locked = rows.Count(x => x.R.Locked);
        sb.Append($"  grok-lock {locked}/{rows.Count} · vested {rows.Sum(x => x.R.OutcomeCredited)} / evicted {rows.Sum(x => x.R.Evicted)} · beacon-promotions {rows.Sum(x => x.R.Admissions)} (the rank-1 clamp broken iff >0 — learned promotion fired on the beacon surface)\n");
        sb.Append("  grok-depth buckets (maxSpan · vis-file@1 / BEACON-file@1):\n");
        (double Lo, double Hi, string Tag)[] buckets = [(0, 50, "<50B"), (50, 150, "50–150B"), (150, 300, "150–300B"), (300, double.MaxValue, ">300B")];
        foreach (var (lo, hi, tag) in buckets)
        {
            var b = rows.Where(x => x.R.MaxSpan >= lo && x.R.MaxSpan < hi).ToList();
            if (b.Count == 0) { sb.Append($"    {tag,-9} —\n"); continue; }
            sb.Append($"    {tag,-9} n={b.Count,3} · vis-file@1 {100.0 * b.Count(x => x.File1) / b.Count,5:F1} · BEACON-file@1 {100.0 * b.Count(x => x.BeaconF1) / b.Count,5:F1} · fn@5 {100.0 * b.Count(x => x.Fn5) / b.Count,5:F1}\n");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE DRIVE — one instance, threading the STANDING mind. Mirrors navigate.Drive's loop shape (beacon → descend
    //  → full-loop induce → residual → expand → local field → verdict) but (a) seeds induction from the STANDING
    //  grammar (channel 1, accreting), and (b) blends the LEARNED gold field into the ranking (channel 2). Gold is
    //  loaded only by the caller AFTER this returns — the drive itself never sees the answer.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static NavDynResult Drive(string dir, int maxLooks, int k, bool verbose, bool expand, bool grammarCarry, bool rankLearn, double wGold,
                                   StandingGrammar standing, RankHead head, out List<Site> outSites, out Bm25Index outBm25)
    {
        string issue = File.ReadAllText(Path.Combine(dir, "query.txt"));
        byte[] issueBytes = Encoding.UTF8.GetBytes(issue);
        var sites = LoadSites(Path.Combine(dir, "sites.jsonl"));
        var fileDocs = sites.Where(s => s.Kind == "module").ToList();
        var fileByPath = new Dictionary<string, Site>();
        foreach (var f in fileDocs) fileByPath[f.Path] = f;

        var bm25 = new Bm25Index(sites.Select(s => s.Text).ToList());
        bm25.IndexModules(Enumerable.Range(0, sites.Count).Where(i => sites[i].Kind == "module"));
        outSites = sites; outBm25 = bm25;                             // threaded to the caller's head.Learn — no re-load + re-tokenize of what this drive already indexed
        var issueToks = Toks(issue).Distinct().ToList();
        var issueTokSet = new HashSet<string>(issueToks, StringComparer.Ordinal);
        var baseScore = bm25.Score(issueToks);

        // the BASE-ONLY beacon (pure BM25, no learned field, no mint) — the counterfactual the channel-2 recovery
        // diagnostic reads: gold's rank here vs on the final blended beacon prices EXACTLY what the learned field
        // moved (a base-miss→blend-hit is a learned recovery; the demolition mechanism made auditable).
        var (baseBeaconOrder, _) = AggregateMaxFiles(sites, baseScore);
        var baseBeaconFiles = baseBeaconOrder.OrderBy(IsTest).ToList();

        // CHANNEL 2 — the LEARNED gold field, blended into the base BEFORE the beacon so the very first descend is
        // steered by everything the head has learned so far. The head scores each site by its terms' learned
        // gold-corroboration; a term that has empirically flagged gold files gets its BM25 contribution boosted.
        var blend = (double[])baseScore.Clone();
        if (rankLearn) head.Blend(blend, sites, bm25, wGold);
        var scratch = new double[blend.Length];                                  // the per-candidate trial buffer — copy blend in, add the mint, keep-by-swap on vest (no clone-per-candidate)

        var (beaconOrder, _) = AggregateMaxFiles(sites, blend);
        var beaconRank = new Dictionary<string, int>();
        for (int i = 0; i < beaconOrder.Count; i++) beaconRank[beaconOrder[i]] = i;
        var descendOrder = beaconOrder;
        var descendRank = beaconRank;
        var quoted = fileDocs.Where(f => issue.Contains(f.Path, StringComparison.Ordinal))
                             .OrderBy(f => beaconRank.GetValueOrDefault(f.Path, int.MaxValue)).Select(f => f.Path).ToList();

        // CHANNEL 1 — seed the induction from the STANDING grammar (accreted across the stream when grammarCarry;
        // a fresh snapshot of the same seed each time when frozen). The grok organ inducts the repo ON TOP.
        using var mind = new RepoGrok(standing.Snapshot(grammarCarry));
        var minted = new List<string>();
        int vestedCount = 0, evictedCount = 0, jumps = 0, promotions = 0;

        var visited = new List<string>(); var visitedSet = new HashSet<string>();
        string lastTop = ""; int persist = 0; int looks = 0;
        double know = 0, lastKnow = -1; bool jumpNext = false;
        string verdict = "budget"; double lastMargin = 0;
        List<(Site S, double Score)> field = new();

        for (int look = 0; look < maxLooks; look++)
        {
            var down = descendOrder.Where(p => !visitedSet.Contains(p)).OrderBy(IsTest).ThenBy(p => descendRank[p]);
            var downList = down.AsEnumerable();
            if (jumpNext) { downList = downList.Skip(k); jumps++; jumpNext = false; }
            var picks = quoted.Where(p => !visitedSet.Contains(p)).Concat(downList).Distinct().Take(k).ToList();
            if (picks.Count == 0) { verdict = "pool"; break; }
            foreach (var p in picks)
            {
                var doc = DescendDoc(fileByPath, p);
                visited.Add(p); visitedSet.Add(p);
                mind.AddFile(doc.Text);
            }
            looks = look + 1;

            mind.Drain(look);
            lastKnow = know;
            know = mind.IssueCoverage(issueBytes);

            // EXPAND (grok-gated) — mint PPMI-coupled terms off the residual; DREAM until corroborated. Two vest
            // channels: (v1) non-dilution sharpens the sitting leader; (learned promotion) a mint that lands on a
            // file the LEARNED head already flags as gold-shaped surges the margin and MAY install a new leader —
            // the online-learned form of the un-clamp (the gold outcome, not a fixed threshold, opens the gate).
            if (expand && mind.Locked && know < 0.999)
            {
                var cand = mind.MintTerms(issueBytes, issueTokSet, minted, bm25, fileDocs.Count);
                if (cand.Count > 0)
                {
                    var vestedNow = new List<string>();
                    foreach (var t in cand)
                    {
                        // trial = blend + WExpand·tScore into the reusable scratch (byte-identical to a fresh clone);
                        // on vest we SWAP scratch↔blend (blend adopts the trial array, scratch recycles the old).
                        Array.Copy(blend, scratch, blend.Length);
                        var tScore = bm25.Score([t]);
                        for (int i = 0; i < scratch.Length; i++) scratch[i] += WExpand * tScore[i];
                        double mCur = FieldMargin(sites, blend, visitedSet), mTrial = FieldMargin(sites, scratch, visitedSet);
                        bool nonDilute = mTrial >= mCur - VestMarginEps;
                        // LEARNED promotion — keyed on the BEACON (the FULL-CORPUS blend that governs rank-1), NOT the
                        // visited-set margin. The frozen-lane's un-clamp null proved the visited-set margin is decoupled
                        // and PINNED (17 vests moved it zero); file@1 lives on the beacon (vested terms move gold 4→2
                        // there). So the mint vests + re-beacons iff the LEARNED head trusts the term (gold-shaped) AND
                        // it improves the BEACON margin (the surface where rank-1 actually moves). The learning is the
                        // corroboration; the fixed PromoteMarginEps of the frozen --promote is replaced by "the head,
                        // trained on 300 gold outcomes, trusts it" — and it acts on the ONLY surface that converts.
                        bool learnedGold = rankLearn && head.Boost(t) > 0 && BeaconMargin(sites, scratch) > BeaconMargin(sites, blend);
                        if (nonDilute || learnedGold)
                        {
                            string beaconLeadBefore = BeaconLeader(sites, blend);
                            (blend, scratch) = (scratch, blend); vestedNow.Add(t);
                            if (learnedGold && !nonDilute && BeaconLeader(sites, blend) != beaconLeadBefore) promotions++;
                        }
                        else evictedCount++;
                    }
                    if (vestedNow.Count > 0)
                    {
                        minted.AddRange(vestedNow); vestedCount += vestedNow.Count;
                        (descendOrder, _) = AggregateMaxFiles(sites, blend);
                        descendRank = new Dictionary<string, int>();
                        for (int i = 0; i < descendOrder.Count; i++) descendRank[descendOrder[i]] = i;
                    }
                }
            }

            field = Enumerable.Range(0, sites.Count).Where(i => visitedSet.Contains(sites[i].Path))
                              .Select(i => (S: sites[i], Score: blend[i])).OrderByDescending(x => x.Score).ToList();

            var fileLocal = field.GroupBy(x => x.S.Path).Select(gr => (Path: gr.Key, Score: gr.Max(x => x.Score)))
                                 .OrderByDescending(x => x.Score).ThenBy(x => beaconRank[x.Path]).ToList();
            string top = fileLocal.Count > 0 ? fileLocal[0].Path : "";
            double margin = fileLocal.Count > 1 && fileLocal[0].Score > 0 ? (fileLocal[0].Score - fileLocal[1].Score) / fileLocal[0].Score : 1.0;
            persist = top == lastTop && top.Length > 0 ? persist + 1 : 0; lastTop = top; lastMargin = margin;
            bool land = persist >= SPersist - 1 && margin >= TauMargin && look > 0;
            bool flat = margin < TauFlat && lastKnow >= 0 && Math.Abs(know - lastKnow) < CovPlateauEps && look > 0;
            if (flat && !land) jumpNext = true;
            if (land) { verdict = "land"; break; }
        }

        var landedVisited = field.GroupBy(x => x.S.Path).Select(gr => (Path: gr.Key, Score: gr.Max(x => x.Score)))
                                 .OrderByDescending(x => x.Score).ThenBy(x => beaconRank[x.Path]).ToList();
        for (int i = 0; i + 1 < landedVisited.Count; i++)
        {
            var (a, b) = (landedVisited[i], landedVisited[i + 1]);
            bool nearTie = a.Score <= 0 || (a.Score - b.Score) / a.Score < TauPromote;
            if (nearTie && beaconRank[b.Path] < beaconRank[a.Path]) (landedVisited[i], landedVisited[i + 1]) = (b, a);
        }
        var visitedFiles = landedVisited.Select(x => x.Path).OrderBy(IsTest).ToList();
        var localFns = field.Where(x => x.S.Kind is "function" or "method").OrderBy(x => IsTest(x.S.Path));
        var localFnSites = localFns.Select(x => (x.S.Path, x.S.Name, x.S.Start, x.S.End, x.Score)).ToList();

        // THE BEACON — the FINAL full-corpus blend ranking (all files, learned-field + vested-mint blended in). This
        // is the surface file@1 lives on (the coordinator's locus); the scorer of record splices this tail for
        // unvisited files, so the beacon rank of gold IS the hard-clamp metric. Test-file demote applied (harness law).
        var (beaconFinalOrder, _) = BeaconFiles(sites, blend);
        var beaconFiles = beaconFinalOrder.OrderBy(IsTest).ToList();

        // CHANNEL 1 — after the instance is grokked, contribute its NOVEL grammar to the standing vocabulary (the
        // accretion; a no-op when grammarCarry is off — the standing grammar stays the frozen seed).
        if (grammarCarry) standing.Absorb(mind.HarvestBinary());

        return new NavDynResult(Path.GetFileName(dir.TrimEnd('/')), visitedFiles, beaconFiles, baseBeaconFiles, localFnSites, looks, mind.MaxSpan,
                             mind.Locked, mind.LockLook, vestedCount, evictedCount, promotions,
                             standing.Count, head.TermCount, verdict);
    }

    // ── the BEACON reads (FULL corpus — every file, not just visited): the surface file@1 actually lives on. The
    // frozen un-clamp null proved the visited-set margin is decoupled + pinned; the learned-promotion gate reads
    // THESE so a corroborated mint moves the surface where rank-1 converts (the coordinator's locus). ──
    private static (List<string> Order, Dictionary<string, double> Score) BeaconFiles(List<Site> sites, double[] score)
        => AggregateMaxFiles(sites, score);

    private static double BeaconMargin(List<Site> sites, double[] score)
    {
        var (order, fs) = AggregateMaxFiles(sites, score);
        if (order.Count < 2) return 1.0;
        double top = fs[order[0]], two = fs[order[1]];
        return top > 0 ? (top - two) / top : 1.0;
    }

    private static string BeaconLeader(List<Site> sites, double[] score)
    { var (order, _) = AggregateMaxFiles(sites, score); return order.Count > 0 ? order[0] : ""; }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  CHANNEL 2 — THE RANK HEAD: the online-learned gold-corroboration field. For every term, two counters —
    //  how often it appeared in a gold-file's sites (goldHits) and how often it was seen at all across gold-file
    //  observations (seen). A term's learned boost = log(1 + GoldScale · goldHits/seen), capped. Blended into the
    //  ranking so terms empirically predictive of gold-file membership up-weight future instances. Learned FROM the
    //  answer — the embraced cheat; the online form of the weight-head, adapting per instance.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private sealed class RankHead
    {
        private readonly Dictionary<string, int> _goldHits = new(StringComparer.Ordinal);   // term appeared in a gold-file site
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);       // term seen in ANY gold-file-instance site (the denominator)
        private const double GoldScale = 8.0;   // steepens the log so a mid gold-rate (~0.3) already earns a meaningful boost

        public int TermCount => _goldHits.Count;

        // LEARN from one solved instance: over the gold FILES' sites, bump every discriminative term's goldHit, and
        // bump `seen` for every term of every site of the instance (the base rate). A term's boost rises when it
        // recurs in gold files MORE than its base occurrence — the learned discriminative gold vocabulary. Reads the
        // drive's already-built Bm25Index postings (per-doc tf + module df) — no re-load, no re-tokenize.
        public void Learn(List<Site> sites, Bm25Index bm25, HashSet<string> goldFiles)
        {
            if (goldFiles.Count == 0) return;
            // denominator: every distinct term in EVERY site (the base occurrence of the term in this instance).
            var seenHere = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < sites.Count; j++)
                foreach (var t in bm25.Tf(j).Keys) if (t.Length >= MinTermLen && !t.Contains(' ')) seenHere.Add(t);
            foreach (var t in seenHere) _seen[t] = _seen.GetValueOrDefault(t) + 1;
            // numerator: the discriminative terms of the GOLD files' sites — top by within-instance rarity (df over
            // the instance's module docs — bm25.FileDf), so the file's stopwords don't drown the deep gold idioms.
            var goldTf = new Dictionary<string, int>(StringComparer.Ordinal);
            int nModules = sites.Count(s => s.Kind == "module");
            for (int j = 0; j < sites.Count; j++)
            {
                if (!goldFiles.Contains(sites[j].Path)) continue;
                foreach (var (t, c) in bm25.Tf(j))
                    if (t.Length >= MinTermLen && !t.Contains(' ')) goldTf[t] = goldTf.GetValueOrDefault(t) + c;
            }
            // rank the gold terms by tf · idf-over-modules (discriminative within the repo), keep the top GoldTermCap.
            var ranked = goldTf.Select(kv =>
            {
                double idf = Math.Log(1.0 + (double)nModules / Math.Max(1, bm25.FileDf(kv.Key)));
                return (Term: kv.Key, Weight: kv.Value * idf);
            }).OrderByDescending(x => x.Weight).ThenBy(x => x.Term, StringComparer.Ordinal).Take(GoldTermCap);
            foreach (var (term, _) in ranked) _goldHits[term] = _goldHits.GetValueOrDefault(term) + 1;
        }

        // the learned boost for a term: 0 until GoldMinSeen observations, then log(1 + GoldScale · goldRate), capped.
        public double Boost(string term)
        {
            if (!_seen.TryGetValue(term, out int seen) || seen < GoldMinSeen) return 0;
            int hits = _goldHits.GetValueOrDefault(term);
            if (hits == 0) return 0;
            double rate = (double)hits / seen;
            return Math.Min(GoldBoostCap, Math.Log(1.0 + GoldScale * rate));
        }

        // BLEND the learned field into the ranking: each site gains WGold · Σ_t boost(t)·tf(t,site) / |site| — the
        // learned gold-vocabulary evidence, length-normalized like BM25 so long files don't win on term mass alone.
        // Only terms with a positive learned boost touch the field (the head is silent until it has learned).
        public void Blend(double[] blend, List<Site> sites, Bm25Index bm25, double wGold)
        {
            if (_goldHits.Count == 0) return;
            for (int i = 0; i < sites.Count; i++)
            {
                var counted = bm25.Tf(i);                             // the drive's hoisted tokenization — same insertion order as a fresh Toks count, bit-identical acc
                if (counted.Count == 0) continue;
                double acc = 0; int n = 0;
                foreach (var c in counted.Values) n += c;
                foreach (var (t, c) in counted)
                {
                    double b = Boost(t);
                    if (b > 0) acc += b * c;
                }
                if (acc > 0) blend[i] += wGold * acc / Math.Sqrt(n);
            }
        }

        public void Save(CkptWriter w)
        {
            WriteIntMap(w, _goldHits);
            WriteIntMap(w, _seen);
        }

        public void Load(CkptReader r)
        {
            _goldHits.Clear(); foreach (var (k, v) in ReadIntMap(r)) _goldHits[k] = v;
            _seen.Clear(); foreach (var (k, v) in ReadIntMap(r)) _seen[k] = v;
        }
    }

    private static string EmitJson(NavDynResult r, bool goldFile1, bool goldBeacon1, bool goldFn5, int goldBeaconRank, string repo, int repoSeq)
    {
        var sb = new StringBuilder();
        sb.Append("{\"instance_id\":").Append(JsonStr(r.Instance));
        sb.Append(",\"repo\":").Append(JsonStr(repo)).Append(",\"repo_seq\":").Append(repoSeq);
        sb.Append(",\"looks\":").Append(r.Looks).Append(",\"verdict\":").Append(JsonStr(r.Verdict));
        sb.Append(",\"locked\":").Append(r.Locked ? "true" : "false").Append(",\"lock_look\":").Append(r.LockLook);
        sb.Append(",\"maxspan\":").Append(R(r.MaxSpan)).Append(",\"vested\":").Append(r.OutcomeCredited).Append(",\"evicted\":").Append(r.Evicted).Append(",\"promotions\":").Append(r.Admissions);
        sb.Append(",\"standing_rules\":").Append(r.StandingRules).Append(",\"learned_terms\":").Append(r.LearnedTerms);
        sb.Append(",\"gold_file1_head\":").Append(goldFile1 ? "true" : "false").Append(",\"gold_beacon1\":").Append(goldBeacon1 ? "true" : "false");
        sb.Append(",\"gold_beacon_rank\":").Append(goldBeaconRank).Append(",\"gold_fn5_head\":").Append(goldFn5 ? "true" : "false");
        sb.Append(",\"visited_files\":[").Append(string.Join(",", r.VisitedFiles.Select(JsonStr))).Append(']');
        sb.Append(",\"beacon_files\":[").Append(string.Join(",", r.BeaconFiles.Take(10).Select(JsonStr))).Append(']');   // additive — the full-corpus rank-1 surface (the coordinator's hard-clamp read)
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
}
