namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using Cogito.Observe;
using static Cogito.Loc;   // the localization substrate: Site · Bm25Index · StandingGrammar · Toks · Load* · AggregateMaxFiles · FieldMargin · HeadRank · RepoOf · R · JsonStr · loop consts · checkpoint helpers


// ── NAVLOOP ──  THE AGENTIC LOCALIZATION LOOP WITH A VALUE LAYER: alignment-from-within as architecture. The naive
// closed loop (reinforce-on-answer, outcome-credit the rules the gold corroborated) is RAW RL on a rank-list localization metric — and a raw optimizer
// GOODHARTS the metric HARDER than the static vest-count did (corroboration ⊥ discrimination generalized: the mind
// learns generic issue↔file fragments that hit the rank-list target without understanding). The governing fix, folded in here as
// the GOVERNING SPINE: the mind must DISCOVER what is VALUABLE from its OWN SELF-MODEL — not from the external metric
// ("value is found by the machine, not assigned to it") — and that discovered value governs the task-steps, so
// generation is value-aligned rather than metric-chasing. The reward is a SIGNAL the value-model integrates, never a
// target it raw-optimizes.
//
// THE IN-ARCHITECTURE VALUE SIGNAL (the crux — what makes value machine-found, not assigned): cogito's self-model is
// its PROPRIOCEPTION — the grammar's own reads over the issue: `Coverage`/`ParsedSize` (Engine.GrammarCover — what it
// understands vs its gaps; ParsedSize LOWER = deeper structure, the MDL-native depth read coverage saturation hides),
// `MeanZ` (Engine.RenormStats — the honest criticality axis, MeshHomeostat.Basin −0.70: model HEALTH), `CvZ` (the
// grok-lock confidence). VALUE = the ENTANGLED-HEALTH PREDICTION: a move is valuable iff it would DROP the issue's
// ParsedSize (deepen understanding, close a self-model gap) WHILE HOLDING MeanZ near basin (not collapsing coherence
// toward the sink). This is the anti-Goodhart guard IN THE ARCHITECTURE: acquiring generic fragments raises coverage
// but does NOT drop ParsedSize and destabilizes MeanZ ⇒ the value-model rates it LOW, so the mind does not pursue it —
// the rank-list metric never enters the value signal, so the mind cannot chase it directly.
//
// THE STAGES (value-layer folded through the six):
//   1 DECOMPOSE      — the issue decomposes into VARIABLE sub-goals: each gap-anchor (the grammar rules flanking the
//                      issue's UNCOVERED regions — GapAnchors) is a sub-goal "understand this gap". Count varies per
//                      instance (many thin-coverage gaps ⇒ many sub-goals); the mind reads WHERE its coverage is thin.
//   2 VALUE-DISCOVER — score each sub-goal by its expected SELF-MODEL GAIN (ValueModel.Appraise): how much acquiring
//                      its coupled structure would deepen understanding (ParsedSize drop) at held criticality. Then the
//                      ACQUISITION-STRATEGY: the coupled-term expansion off the high-value anchor, aimed at the file
//                      postings — the descend order is sorted by VALUE, not BM25 alone (value governs the task-steps).
//   3 NAVIGATE+SAMPLE— descend the value-sorted frontier; the STOP-criterion reads the self-model coherent (grok-
//                      LOCKED + ParsedSize plateaued + MeanZ held) — self-mastery and task-solving are ONE loop.
//   4 DECIDE (aligned)— commit the localization the VALUE-model ranks highest (the file whose evidence most cohered
//                      the self-model), value-aligned, not a raw external rank.
//   5 TEST + APPEND  — the bench reveals gold (LoadGold, scored AFTER — never steers); the gold file appends to the
//                      standing tape as source="gold" EVIDENCE, the mind's value-hypotheses were on it as DREAM.
//   6 VALUE REINFORCE/DREAM — Reflection over (dream ⊕ gold): RIGHT ⇒ the value-PREDICTION materialized (the gold reflects
//                      the rules the value-model predicted worth wanting) → reinforce the value + strategy; WRONG ⇒ the
//                      value-model MISPREDICTED → the gold's un-predicted structure stays dream (STUDY what value it
//                      missed, revise the value-model). The reward CALIBRATES the value-model — it is not the target.
//
// THE VALUE-DISCOVERY→TASK-GOVERNANCE WIRE: the value signal is a predicted ΔParsedSize-at-held-MeanZ per sub-goal,
// computed by the mind from its OWN grammar; it sorts the descend frontier (governs which files the mind reads) and
// picks the committed file. The rank-list metric never enters that ranking — it only, post-hoc, vests the value-predictions the
// outcome confirmed (the value-model learning WHAT IS WORTH WANTING, discriminative because only gold-confirmed
// value-predictions vest). That is the alignment: the output tracks the mind's discovered values, and the metric can
// only refine those values, never override them.
//
// THE DECISIVE READ is the SLOPE (navdyn's law) READ THROUGH THE VALUE LENS: does localization improve as the VALUE-MODEL
// calibrates — AND does value-alignment (the committed file's self-model coherence) predict correctness? A rising
// slope with value-calibration = the mind learning what is worth wanting; a flat slope = the value signal doesn't
// convert HERE, reported straight (frame-break law). NOT frozen-comparable — the lifelong/agentic/value-aligned eval.
//
// Deterministic: instance_id-sorted arrival, seeded breach, stable sorts, integer outcomeCredit. Release-only, gold read to
// CALIBRATE the value-model (the embraced closed-loop signal — a game a frozen baseline cannot play), strictly AFTER
// the instance is scored (forward flow — the drive's value-discovery never sees the answer).
public static class NavLoop
{
    // The loop policy + induction/expansion knobs + the value-layer appraisal consts are shared (Loc + RepoGrok own
    // them; RepoGrok.DiscoverValue holds the MeanZ-basin/depth appraisal). Navloop keeps its OWN crux knobs:
    //
    // ── THE OUTCOME→VEST CHANNEL (navloop's own — the crux) ──
    private const int    WScale            = 8;      // the evidence weight (Pearl.WScale — a gold span exercises a rule at ×8 vs a dream's ×1; the mint-gate arithmetic, Provenance.cs)
    private const int    VestTapeCapBytes  = 262_144; // the standing corroboration tape's rolling budget (the homeostat's discipline at the STREAM scale — past this the oldest UNVESTED nav-spans DROP (ShedToCap, evidence never drops) so the audit stays O(bounded); the vested grammar already carried the learning forward)
    public  const double WVest             = 0.6;    // the vested-structure field's blend weight (base + WVest·vestField) — a file whose sites exercise OUTCOME-VESTED rules ranks up; the task-discriminative lever the static doc-weight lacked. Public: the CLI's --wvest default reads it.

    public sealed record LoopResult(
        string Instance, List<string> VisitedFiles, List<string> BeaconFiles, List<string> BaseBeaconFiles,
        List<(string Path, string Name, int Start, int End, double Score)> LocalFnSites,
        int Looks, double MaxSpan, bool Locked, int LockLook, int OutcomeCredited, int Evicted,
        int StandingRules, int OutcomeCreditedRules, int GoldOutcomeCreditedThis, string Verdict,
        List<string> ValueTerms, double ParseDepth);   // ValueTerms = the mind's DISCOVERED value-hypotheses (calibrated by the outcome); ParseDepth = its final self-model depth (the value-alignment read)

    /// usage: navloop <data-dir> [--limit N] [--pretrain <runDir>] [--no-outcome-vest] [--no-grammar-carry] [--no-expand]
    /// The run lands in runs/navloop_NNNN/ (journal.log · curve.tsv · rankings.jsonl · report.txt · config — the engine
    /// logs the whole run into the run dir; the `run → …` locator says where). The closed agentic loop. --pretrain seeds
    /// the standing grammar warm (then accretes on top). The outcome-vest
    /// channel is ablatable: --no-outcome-vest freezes the gold→vest wire (the grammar still accretes via channel 1,
    /// but no outcome-driven corroboration steers the field — the control that isolates whether the TASK SIGNAL in the
    /// loop is what makes the weights discriminative). --no-grammar-carry freezes channel 1 too (ablation floor).
    /// The closed-loop mode entry (nav --mode loop). The value layer governs the descend (alignment-from-within); the
    /// outcome→vest channel calibrates it (differential RL — Mode 2). --supervised is the Mode-1 append control. Multi-
    /// pass (passes>1) re-drives the SAME stream over ONE never-reset mind (the cross-pass curve is the verdict);
    /// --dream-between interleaves an intrinsic consolidation night between passes. "" pretrainDir = cold.
    public static int Run(string dir, int maxLooks, int k, bool valueLayer, bool outcomeVest, bool grammarCarry, bool expand,
                          bool supervised, double wVest, int passes, bool dreamBetween, int dreamNights, string pretrainDir, int limit, int checkpointEvery)
    {
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"  nav --mode loop: '{dir}' is not a directory"); return 1; }
        var pretrain = LoadPretrainBase(pretrainDir);
        return RunStream(dir, limit, maxLooks, k, valueLayer, outcomeVest, grammarCarry, expand, supervised, wVest, passes, dreamBetween, dreamNights, checkpointEvery, pretrain);
    }

    // ── load a checkpoint's binary-prefix grammar as the standing seed ──
    private static List<GrammarRule> LoadPretrainBase(string runDir)
    {
        if (runDir.Length == 0) return new List<GrammarRule>();
        var (raw, bin) = LoadBinaryPrefix(runDir);
        Console.WriteLine($"  standing-seed ← {runDir} · {raw.Rules.Length} trained rules · seeding {bin.Count} (binary prefix)");
        return bin;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE STREAM DRIVER — the 300 instances in instance_id order, threading (a) the STANDING grammar (channel 1)
    //  and (b) THE CORROBORATION MIND (the standing Tape + its outcome-vested grammar). After each land, the gold
    //  outcome appends to the tape and Corroborate outcome-credits the dreams the gold corroborated — the closed loop.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static int RunStream(string dataDir, int limit, int maxLooks, int k, bool valueLayer, bool outcomeVest, bool grammarCarry, bool expand, bool supervised, double wVest, int passes, bool dreamBetween, int dreamNights, int checkpointEvery, List<GrammarRule> pretrain)
    {
        var dirs = Directory.GetDirectories(dataDir)
                            .Where(d => File.Exists(Path.Combine(d, "query.txt")) && File.Exists(Path.Combine(d, "sites.jsonl")))
                            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).Take(limit).ToList();
        // the run's config snapshot = the deterministic policy telegraph (same fields the run log prints; same config +
        // same data ⇒ same run — the replay config, AgentRun.Begin lands it as `config` in the run dir).
        string config = $"navloop · {dirs.Count} instances (instance_id-sorted) · k={k} looks≤{maxLooks} · " +
                        $"MODE={(supervised ? "1-SUPERVISED" : "2-DIFFERENTIAL-RL")} · VALUE-LAYER={valueLayer} · outcome-vest={outcomeVest} (wvest={wVest}) · grammar-carry={grammarCarry} · expand={expand} · " +
                        $"PASSES={passes}{(dreamBetween ? $" · DREAM-BETWEEN={dreamNights}n" : "")} · seed={(pretrain.Count == 0 ? "COLD" : $"{pretrain.Count} rules")}";
        using var ar = AgentRun.Begin("navloop", config, new IntakeManifest(dataDir, IntakeManifest.Of(dataDir), $"limit={limit} passes={passes}"));
        Console.WriteLine($"navloop · {dirs.Count} instances (instance_id-sorted — earlier teaches later) · policy k={k} looks≤{maxLooks} · " +
                          $"MODE={(supervised ? "1-SUPERVISED (control)" : "2-DIFFERENTIAL-RL")} · VALUE-LAYER={valueLayer} · outcome-vest={outcomeVest} (wvest={wVest}) · grammar-carry={grammarCarry} · expand={expand} · " +
                          $"PASSES={passes}{(dreamBetween ? $" · DREAM-BETWEEN={dreamNights}n" : "")} · seed={(pretrain.Count == 0 ? "COLD" : $"{pretrain.Count} rules")}");
        Console.WriteLine("  THE CRUX (Mode 2, differential RL): the reward is the MATCH — the mind's committed PATH spans vest ONLY when the path reached gold (gold=EVIDENCE witness, self-reinforcement); a MISS routes gold to a DREAM-TARGET (source=\"miss:\", no vest of the path). Getting it right reinforces; getting it wrong does not.");
        if (passes > 1)
            Console.WriteLine($"  MULTI-PASS COMPOUNDING: {passes} passes over the SAME {dirs.Count}-stream, ONE never-reset mind (standing grammar + corroboration tape carry ACROSS passes). The cross-pass accuracy curve is the verdict — CLIMB→asymptote (wants more) vs PLATEAU (a ceiling). WATCH MeanZ for renormalization (re-fed learned real drifting criticality toward the sink).");

        var standing = new StandingGrammar(pretrain, StandingRuleCap);      // channel 1 — the accreting vocabulary
        using var corrob = new CorroborationMind(WScale, VestTapeCapBytes, ar.Run.PathOf("tape.spanlog")); // THE CLOSED LOOP — the standing tape + outcome-vested grammar (disposed at stream end: returns the loom's ArrayPool rentals)

        long t0 = Environment.TickCount64;
        // ── THE CROSS-PASS COMPOUNDING JOURNAL — one row per pass. The SHAPE of file@1 across passes IS the push-to-max
        // verdict (does the mind, re-solving issues it has now lived in, keep climbing or plateau). vestedRules + MeanZ
        // per pass expose the renormalization failure mode: rising vest with MeanZ sliding toward −1.11 = the bounded
        // world running dry (memorizing the re-fed real, criticality collapsing) rather than genuinely compounding.
        var passJournal = new List<(int Pass, double VisF1, double BcnF1, double Fn5, int OutcomeCredited, int Standing, double MeanZ, double MaxSpan, int PathHits, long Sec)>();

        // the per-instance results (rankings.jsonl) + the running multi-pass curve (curve.tsv) — both live in the run
        // dir, auto-flushed (a killed run keeps every completed line — the safe-to-kill law).
        var w = ar.Rankings;
        var curve = ar.Curve("pass\tidx\tinstance_id\trepo\trepo_seq\tgold_file_rank\tgold_beacon_rank\tgold_fn_rank\tfile_hit_at1\tbeacon_hit_at1\tfn_hit_at5\tstanding_rules\tvested_rules\tgold_vested\tmaxspan");
        {
            List<(LoopResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)> lastStream = new();
            for (int pass = 0; pass < passes; pass++)
            {
                long tp0 = Environment.TickCount64;
                var stream = new List<(LoopResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)>();
                var repoSeq = new Dictionary<string, int>(StringComparer.Ordinal);   // repo_seq resets per pass — within-repo slope is measured WITHIN a pass (difficulty-held), the cross-pass climb is measured BETWEEN
                int hitF1 = 0, hitBF1 = 0, hitF5 = 0;
                int vestRecover = 0, vestLoss = 0;   // outcome-vest effect on gold's beacon rank vs the base (recover = base-miss→hit@1)
                int pathHits = 0, pathMisses = 0;
                for (int n = 0; n < dirs.Count; n++)
                {
                    string id = Path.GetFileName(dirs[n].TrimEnd('/'));
                    string repo = RepoOf(id);
                    int rseq = repoSeq.GetValueOrDefault(repo, 0);
                    repoSeq[repo] = rseq + 1;

                    var r = Drive(dirs[n], maxLooks, k, verbose: false, expand, grammarCarry, valueLayer, outcomeVest, wVest, standing, corrob, out var sites);
                    var (goldFiles, goldFns) = LoadGold(Path.Combine(dirs[n], "gold.json"));
                    bool f1  = r.VisitedFiles.Count > 0 && goldFiles.Contains(r.VisitedFiles[0]);
                    bool bf1 = r.BeaconFiles.Count > 0 && goldFiles.Contains(r.BeaconFiles[0]);
                    bool f5  = r.LocalFnSites.Take(5).Any(x => goldFns.Contains((x.Path, x.Name)));
                    int goldFileRank   = HeadRank(r.VisitedFiles, goldFiles);
                    int goldBeaconRank = HeadRank(r.BeaconFiles, goldFiles);
                    int goldBaseRank   = HeadRank(r.BaseBeaconFiles, goldFiles);
                    int goldFnRank     = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name))) + 1;
                    bool baseHit1 = goldBaseRank == 1;
                    if (baseHit1 && !bf1) vestLoss++;
                    if (!baseHit1 && bf1) vestRecover++;

                    // ── STAGE 6 THE DIFFERENTIAL RL CALIBRATION (Mode 2 — reward CORRECTNESS, not append answers). The
                    // reward is the MATCH: did the mind's OWN traversed path (the files it committed to) REACH the gold?
                    // pathHit = gold ∈ the mind's committed top-K (its answer, not the beacon tail). On a HIT the mind's
                    // path-rules vest (self-reinforcement of correct reasoning — the gold WITNESSES the path it took); on a
                    // MISS the path does NOT vest and the gold routes to a dream-TARGET (the missed structure, studied).
                    // Runs AFTER scoring (forward flow — the reward calibrates, never steers this instance's answer). ──
                    const int CommitK = 3;   // the mind's committed localization = its top-K visited files; gold within it = the path was RIGHT
                    bool truePathHit = r.VisitedFiles.Take(CommitK).Any(goldFiles.Contains);
                    bool pathHit = supervised || truePathHit;   // MODE 1 (--supervised): force gold=evidence regardless (no correctness gate — the append-answers control)
                    int goldOutcomeCreditedThis = 0;
                    if (outcomeVest)
                    {
                        goldOutcomeCreditedThis = corrob.AbsorbOutcome(dirs[n], sites, goldFiles, r.ValueTerms, r.VisitedFiles, pathHit, truePathHit);
                        if (truePathHit) pathHits++; else pathMisses++;   // the journal tracks the TRUE hit/miss (Mode-1 forces the vest but the mind still really hit or missed)
                    }

                    var rFinal = r with { GoldOutcomeCreditedThis = goldOutcomeCreditedThis, OutcomeCreditedRules = corrob.OutcomeCreditedRuleCount };
                    stream.Add((rFinal, f1, bf1, f5, repo, rseq));
                    w.WriteLine(EmitJson(rFinal, f1, bf1, f5, goldBeaconRank, repo, rseq, pass + 1));
                    if (f1) hitF1++; if (bf1) hitBF1++; if (f5) hitF5++;
                    curve.WriteLine($"{pass + 1}\t{n + 1}\t{r.Instance}\t{repo}\t{rseq}\t{(goldFileRank == 0 ? "miss" : goldFileRank)}\t{(goldBeaconRank == 0 ? "miss" : goldBeaconRank)}\t{(goldFnRank == 0 ? "miss" : goldFnRank)}\t{(f1 ? 1 : 0)}\t{(bf1 ? 1 : 0)}\t{(f5 ? 1 : 0)}\t{rFinal.StandingRules}\t{rFinal.OutcomeCreditedRules}\t{goldOutcomeCreditedThis}\t{r.MaxSpan:F0}");
                    ar.Journal.Index(n + 1, $"navloop P{pass + 1} {r.Instance}({repo}#{rseq}) · gold file-rank {(goldFileRank == 0 ? "miss" : goldFileRank.ToString())}/beacon {(goldBeaconRank == 0 ? "miss" : goldBeaconRank.ToString())}/fn {(goldFnRank == 0 ? "miss" : goldFnRank.ToString())} · file@1={(f1 ? 1 : 0)} bcn@1={(bf1 ? 1 : 0)} fn@5={(f5 ? 1 : 0)} · standing {rFinal.StandingRules} vested {rFinal.OutcomeCreditedRules} gold-vested {goldOutcomeCreditedThis} maxspan {r.MaxSpan:F0}");
                    if (checkpointEvery > 0 && (n + 1) % checkpointEvery == 0)
                        SaveCheckpoint(ar.Run, ar.Journal, config, pass, n + 1, maxLooks, k, valueLayer, outcomeVest, grammarCarry, expand, supervised, wVest, passes, dreamBetween, dreamNights, checkpointEvery,
                            dirs, standing, corrob, repoSeq, hitF1, hitBF1, hitF5, vestRecover, vestLoss, pathHits, pathMisses, t0);
                    if ((n + 1) % 10 == 0 || n + 1 == dirs.Count)
                        Console.WriteLine($"  {(passes > 1 ? $"P{pass + 1} " : "")}[{n + 1}/{dirs.Count}] {(Environment.TickCount64 - tp0) / 1000.0:F0}s · visited-file@1={100.0 * hitF1 / (n + 1):F1}% BEACON-file@1={100.0 * hitBF1 / (n + 1):F1}% fn@5={100.0 * hitF5 / (n + 1):F1}% · " +
                                          $"standing {rFinal.StandingRules} · VESTED {rFinal.OutcomeCreditedRules} rules · last {r.Instance}({repo}#{rseq}) {(r.Locked ? $"LOCK@{r.LockLook}" : "unlkd")} gold-vested {goldOutcomeCreditedThis}");
                }
                long psec = (Environment.TickCount64 - tp0) / 1000;
                passJournal.Add((pass + 1, 100.0 * hitF1 / dirs.Count, 100.0 * hitBF1 / dirs.Count, 100.0 * hitF5 / dirs.Count,
                                corrob.OutcomeCreditedRuleCount, standing.Count, corrob.MeanZ, corrob.MaxSpan, pathHits, psec));
                Console.WriteLine($"  ══ PASS {pass + 1}/{passes} DONE ({psec}s) · visited-file@1 {100.0 * hitF1 / dirs.Count:F1}% · BEACON-file@1 {100.0 * hitBF1 / dirs.Count:F1}% · fn@5 {100.0 * hitF5 / dirs.Count:F1}% · " +
                                  $"standing {standing.Count} · VESTED {corrob.OutcomeCreditedRuleCount} rules · path-hits {pathHits}/{dirs.Count} · MeanZ {(double.IsNaN(corrob.MeanZ) ? "—" : corrob.MeanZ.ToString("F3"))} · outcome-vest NET@1 {vestRecover - vestLoss:+0;-0}");

                // ── THE LOOPBACK COMBUSTION (--dream-between) — between external passes, let the mind DREAM on its
                // accumulated grammar: extra consolidation nights on the standing corroboration tape with NO new external
                // gold (the intrinsic signal interleaved with the external repos). Does dreaming on lived grammar AMPLIFY
                // the next pass's climb vs a plain re-pass? WATCH: pure re-feed of learned real renormalizes like dream —
                // if MeanZ sinks HARDER with the dream than without, the loopback is running the world dry, not deepening it.
                if (dreamBetween && pass + 1 < passes)
                {
                    var (dv, dMeanZ) = corrob.ReplayConsolidate(dreamNights);
                    Console.WriteLine($"  ~~ DREAM-BETWEEN (loopback P{pass + 1}→P{pass + 2}): {dreamNights} consolidation nights · vested {dv} · MeanZ {(double.IsNaN(dMeanZ) ? "—" : dMeanZ.ToString("F3"))} (intrinsic — no external gold)");
                }
                lastStream = stream;
            }
            // the SLOPE report reads the FINAL pass's stream (the most-lived mind — its within-pass arrival/repo slope).
            // The verdict block is teed: printed to the console AND landed as report.txt so the run carries its own verdict.
            var report = new StringBuilder();
            report.Append($"\n  → {passes} pass(es) × {dirs.Count} instances → {ar.Dir} ({(Environment.TickCount64 - t0) / 1000.0:F0}s) · final standing {standing.Count} · VESTED {corrob.OutcomeCreditedRuleCount} rules\n");
            ReportCrossPass(report, passJournal);
            ReportSlope(report, lastStream, outcomeVest);
            Console.Write(report);
            ar.Write("report.txt", report.ToString());
        }
        return 0;
    }

    public static int Resume(string runDir, bool verify = false, int steps = 0)
    {
        if (verify) { Console.Error.WriteLine("  navloop resume --verify is not implemented yet; use the full byte-match gate"); return 1; }
        if (steps != 0) { Console.Error.WriteLine("  navloop resume does not support --steps; the instance horizon rides the checkpointed stream"); return 1; }
        var dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, AgentCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {AgentCheckpoint.FileName} under '{runDir}' — nothing to resume");
            return 1;
        }
        var run = Cogito.Run.Open(dir);
        int maxLooks = 0, k = 0, passes = 0, dreamNights = 0, checkpointEvery = 0;
        int hitF1 = 0, hitBF1 = 0, hitF5 = 0, vestRecover = 0, vestLoss = 0, pathHits = 0, pathMisses = 0;
        bool valueLayer = false, outcomeVest = false, grammarCarry = false, expand = false, supervised = false, dreamBetween = false;
        double wVest = 0;
        long t0 = Environment.TickCount64;
        List<string> dirs = new();
        Dictionary<string, int> repoSeq = new(StringComparer.Ordinal);
        StandingGrammar? standing = null;
        CorroborationMind? corrob = null;
        var (_, snap, journal) = AgentCheckpoint.Load(run.Dir, AgentCheckpoint.AgentVerbs.NavLoop, r =>
        {
            maxLooks = r.I32(); k = r.I32();
            valueLayer = r.Bool(); outcomeVest = r.Bool(); grammarCarry = r.Bool(); expand = r.Bool(); supervised = r.Bool();
            wVest = r.F64(); passes = r.I32(); dreamBetween = r.Bool(); dreamNights = r.I32(); checkpointEvery = r.I32();
            dirs = ReadStrings(r);
            standing = new StandingGrammar([], StandingRuleCap); standing.Load(r);
            corrob = new CorroborationMind(WScale, VestTapeCapBytes, run.PathOf("tape.spanlog")); corrob.Load(r);   // log mounted by the ctor BEFORE Load — a checkpoint carrying dropped spans addresses its bytes there
            repoSeq = ReadIntMap(r);
            hitF1 = r.I32(); hitBF1 = r.I32(); hitF5 = r.I32(); vestRecover = r.I32(); vestLoss = r.I32(); pathHits = r.I32(); pathMisses = r.I32();
            t0 = Environment.TickCount64 - r.I64();
            return new Tape();
        });
        if (standing is null || corrob is null) throw new InvalidDataException("navloop checkpoint did not restore a mind");
        journal.Rewrite(run, header: false);
        run.Truncate("rankings.jsonl", snap.RankingsLen);
        run.TruncateCurve("curve.tsv", snap.CurveLen);
        using var ar = AgentRun.Resume(run, journal);
        var w = ar.Rankings;
        var curve = ar.Curve("pass\tidx\tinstance_id\trepo\trepo_seq\tgold_file_rank\tgold_beacon_rank\tgold_fn_rank\tfile_hit_at1\tbeacon_hit_at1\tfn_hit_at5\tstanding_rules\tvested_rules\tgold_vested\tmaxspan");
        Console.WriteLine($"navloop ⇄ {Path.GetFileName(run.Dir)} · resumed at pass {snap.Pass + 1}, instance {snap.Next}/{dirs.Count}");
        var lastStream = new List<(LoopResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)>();
        var passJournal = new List<(int Pass, double VisF1, double BcnF1, double Fn5, int OutcomeCredited, int Standing, double MeanZ, double MaxSpan, int PathHits, long Sec)>();
        for (int pass = snap.Pass; pass < passes; pass++)
        {
            long tp0 = Environment.TickCount64;
            var stream = pass == snap.Pass ? lastStream : new List<(LoopResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)>();
            if (pass != snap.Pass)
            {
                repoSeq.Clear();
                hitF1 = hitBF1 = hitF5 = vestRecover = vestLoss = pathHits = pathMisses = 0;
            }
            int start = pass == snap.Pass ? snap.Next : 0;
            for (int n = start; n < dirs.Count; n++)
            {
                string id = Path.GetFileName(dirs[n].TrimEnd('/'));
                string repo = RepoOf(id);
                int rseq = repoSeq.GetValueOrDefault(repo, 0);
                repoSeq[repo] = rseq + 1;
                var r = Drive(dirs[n], maxLooks, k, verbose: false, expand, grammarCarry, valueLayer, outcomeVest, wVest, standing, corrob, out var sites);
                var (goldFiles, goldFns) = LoadGold(Path.Combine(dirs[n], "gold.json"));
                bool f1 = r.VisitedFiles.Count > 0 && goldFiles.Contains(r.VisitedFiles[0]);
                bool bf1 = r.BeaconFiles.Count > 0 && goldFiles.Contains(r.BeaconFiles[0]);
                bool f5 = r.LocalFnSites.Take(5).Any(x => goldFns.Contains((x.Path, x.Name)));
                int goldFileRank = HeadRank(r.VisitedFiles, goldFiles);
                int goldBeaconRank = HeadRank(r.BeaconFiles, goldFiles);
                int goldBaseRank = HeadRank(r.BaseBeaconFiles, goldFiles);
                int goldFnRank = r.LocalFnSites.FindIndex(x => goldFns.Contains((x.Path, x.Name))) + 1;
                bool baseHit1 = goldBaseRank == 1;
                if (baseHit1 && !bf1) vestLoss++;
                if (!baseHit1 && bf1) vestRecover++;
                const int CommitK = 3;
                bool truePathHit = r.VisitedFiles.Take(CommitK).Any(goldFiles.Contains);
                bool pathHit = supervised || truePathHit;
                int goldOutcomeCreditedThis = 0;
                if (outcomeVest)
                {
                    goldOutcomeCreditedThis = corrob.AbsorbOutcome(dirs[n], sites, goldFiles, r.ValueTerms, r.VisitedFiles, pathHit, truePathHit);
                    if (truePathHit) pathHits++; else pathMisses++;
                }
                var rFinal = r with { GoldOutcomeCreditedThis = goldOutcomeCreditedThis, OutcomeCreditedRules = corrob.OutcomeCreditedRuleCount };
                stream.Add((rFinal, f1, bf1, f5, repo, rseq));
                w.WriteLine(EmitJson(rFinal, f1, bf1, f5, goldBeaconRank, repo, rseq, pass + 1));
                if (f1) hitF1++; if (bf1) hitBF1++; if (f5) hitF5++;
                curve.WriteLine($"{pass + 1}\t{n + 1}\t{r.Instance}\t{repo}\t{rseq}\t{(goldFileRank == 0 ? "miss" : goldFileRank)}\t{(goldBeaconRank == 0 ? "miss" : goldBeaconRank)}\t{(goldFnRank == 0 ? "miss" : goldFnRank)}\t{(f1 ? 1 : 0)}\t{(bf1 ? 1 : 0)}\t{(f5 ? 1 : 0)}\t{rFinal.StandingRules}\t{rFinal.OutcomeCreditedRules}\t{goldOutcomeCreditedThis}\t{r.MaxSpan:F0}");
                ar.Journal.Index(n + 1, $"navloop P{pass + 1} {r.Instance}({repo}#{rseq}) · gold file-rank {(goldFileRank == 0 ? "miss" : goldFileRank.ToString())}/beacon {(goldBeaconRank == 0 ? "miss" : goldBeaconRank.ToString())}/fn {(goldFnRank == 0 ? "miss" : goldFnRank.ToString())} · file@1={(f1 ? 1 : 0)} bcn@1={(bf1 ? 1 : 0)} fn@5={(f5 ? 1 : 0)} · standing {rFinal.StandingRules} vested {rFinal.OutcomeCreditedRules} gold-vested {goldOutcomeCreditedThis} maxspan {r.MaxSpan:F0}");
                if (checkpointEvery > 0 && (n + 1) % checkpointEvery == 0)
                    SaveCheckpoint(run, ar.Journal, File.ReadAllText(run.PathOf("config")).TrimEnd(), pass, n + 1, maxLooks, k, valueLayer, outcomeVest, grammarCarry, expand, supervised, wVest, passes, dreamBetween, dreamNights, checkpointEvery,
                        dirs, standing, corrob, repoSeq, hitF1, hitBF1, hitF5, vestRecover, vestLoss, pathHits, pathMisses, t0);
            }
            long psec = (Environment.TickCount64 - tp0) / 1000;
            passJournal.Add((pass + 1, 100.0 * hitF1 / dirs.Count, 100.0 * hitBF1 / dirs.Count, 100.0 * hitF5 / dirs.Count,
                            corrob.OutcomeCreditedRuleCount, standing.Count, corrob.MeanZ, corrob.MaxSpan, pathHits, psec));
            lastStream = stream;
            if (dreamBetween && pass + 1 < passes)
            {
                var (dv, dMeanZ) = corrob.ReplayConsolidate(dreamNights);
                Console.WriteLine($"  ~~ DREAM-BETWEEN (loopback P{pass + 1}→P{pass + 2}): {dreamNights} consolidation nights · vested {dv} · MeanZ {(double.IsNaN(dMeanZ) ? "—" : dMeanZ.ToString("F3"))} (intrinsic — no external gold)");
            }
        }
        var report = new StringBuilder();
        report.Append($"\n  → {passes} pass(es) × {dirs.Count} instances → {ar.Dir} ({(Environment.TickCount64 - t0) / 1000.0:F0}s) · final standing {standing.Count} · VESTED {corrob.OutcomeCreditedRuleCount} rules\n");
        ReportCrossPass(report, passJournal);
        ReportSlope(report, lastStream, outcomeVest);
        Console.Write(report);
        ar.Write("report.txt", report.ToString());
        corrob.Dispose();
        return 0;
    }

    private static void SaveCheckpoint(Run run, Journal journal, string config, int pass, int next, int maxLooks, int k, bool valueLayer, bool outcomeVest, bool grammarCarry, bool expand, bool supervised,
        double wVest, int passes, bool dreamBetween, int dreamNights, int checkpointEvery, List<string> dirs, StandingGrammar standing, CorroborationMind corrob, Dictionary<string, int> repoSeq,
        int hitF1, int hitBF1, int hitF5, int vestRecover, int vestLoss, int pathHits, int pathMisses, long t0)
    {
        var snap = new AgentCheckpoint.StreamSnap(next, pass, Len(run, "journal.log"), Len(run, "rankings.jsonl"), Len(run, "curve.tsv"));
        var image = AgentCheckpoint.Encode(AgentCheckpoint.AgentVerbs.NavLoop, config, snap, journal, w =>
        {
            w.I32(maxLooks); w.I32(k);
            w.Bool(valueLayer); w.Bool(outcomeVest); w.Bool(grammarCarry); w.Bool(expand); w.Bool(supervised);
            w.F64(wVest); w.I32(passes); w.Bool(dreamBetween); w.I32(dreamNights); w.I32(checkpointEvery);
            WriteStrings(w, dirs);
            standing.Save(w);
            corrob.Save(w);
            WriteIntMap(w, repoSeq);
            w.I32(hitF1); w.I32(hitBF1); w.I32(hitF5); w.I32(vestRecover); w.I32(vestLoss); w.I32(pathHits); w.I32(pathMisses);
            w.I64(Environment.TickCount64 - t0);
        });
        AgentCheckpoint.Save(run, image);
    }

    // ── THE CROSS-PASS COMPOUNDING CURVE — the push-to-max verdict. One row per pass: the SHAPE of file@1 across passes
    // answers "does the compounding scream for more". A monotone CLIMB toward an unreached ceiling = the curvature wants
    // more/varied data next. A PLATEAU after pass k = a ceiling — name the clog (the deciles/within-repo slope say which
    // kind: env-starved if fn@5 caps while file@1 climbs; renormalized if vest rises while MeanZ sinks; navigation-capped
    // if both flat but the beacon holds gold in-tail). ──
    private static void ReportCrossPass(StringBuilder sb, List<(int Pass, double VisF1, double BcnF1, double Fn5, int OutcomeCredited, int Standing, double MeanZ, double MaxSpan, int PathHits, long Sec)> L)
    {
        if (L.Count <= 1) return;
        sb.Append("\n  ═══ CROSS-PASS COMPOUNDING CURVE (the same stream re-solved by ONE never-reset mind — does accuracy CLIMB toward an asymptote or PLATEAU) ═══\n");
        sb.Append("    pass ·  vis-file@1  BEACON-file@1   fn@5   · vested-rules  standing  MeanZ    maxspan · path-hits · Δvis-file@1\n");
        double prevVis = double.NaN;
        foreach (var p in L)
        {
            string dv = double.IsNaN(prevVis) ? "  —  " : $"{p.VisF1 - prevVis:+0.0;-0.0}";
            sb.Append($"    P{p.Pass,2}  ·   {p.VisF1,6:F1}      {p.BcnF1,6:F1}     {p.Fn5,6:F1} ·   {p.OutcomeCredited,8}  {p.Standing,7}  {(double.IsNaN(p.MeanZ) ? "  —  " : p.MeanZ.ToString("F3")),6}  {p.MaxSpan,7:F0} ·  {p.PathHits,4}    ·  {dv}\n");
            prevVis = p.VisF1;
        }
        // THE VERDICT LINE — the slope of the LAST HALF of passes (the asymptotic regime, not the pass-1→2 transient).
        int half = Math.Max(1, L.Count / 2);
        double firstVis = L[0].VisF1, lastVis = L[^1].VisF1;
        double lateSlope = L.Count >= 2 ? (L[^1].VisF1 - L[L.Count - 1 - half].VisF1) / half : 0;   // per-pass Δ over the last half
        double meanzDrift = double.IsNaN(L[0].MeanZ) || double.IsNaN(L[^1].MeanZ) ? double.NaN : L[^1].MeanZ - L[0].MeanZ;
        bool renorm = !double.IsNaN(meanzDrift) && meanzDrift < -0.05 && L[^1].OutcomeCredited > L[0].OutcomeCredited;   // vest RISING while MeanZ SINKING = the bounded-world-runs-dry signature
        sb.Append($"  CROSS-PASS SLOPE: vis-file@1 P1 {firstVis:F1} → P{L.Count} {lastVis:F1} (Δ{lastVis - firstVis:+0.0;-0.0} total) · late-half per-pass Δ {lateSlope:+0.00;-0.00}pt/pass · " +
                  $"MeanZ drift {(double.IsNaN(meanzDrift) ? "—" : meanzDrift.ToString("+0.000;-0.000"))}\n");
        sb.Append($"  VERDICT: {(lateSlope > 0.3 ? "CLIMB — the curvature SCREAMS for more (late-half still rising; no ceiling hit — rev with more/varied data)" : Math.Abs(lateSlope) <= 0.3 ? $"PLATEAU — a ceiling{(renorm ? " · RENORMALIZED (vest rising while MeanZ sinks toward the sink — the bounded world ran dry; needs perpetual-novel real, not re-feed)" : " (structural — env/navigation-capped; the deciles+within-repo slope name which)")}" : "DECLINE — the re-feed DEGRADES (over-outcomeCredit drowning the lexical field, or criticality collapse — read MeanZ)")}\n");
    }

    // ── THE SLOPE — the decisive read (navdyn parity): by-arrival deciles + within-repo + grok-depth buckets. A
    // rising slope as VESTED rules accrete = the closed loop making the weights task-discriminative. ──
    private static void ReportSlope(StringBuilder sb, List<(LoopResult R, bool File1, bool BeaconF1, bool Fn5, string Repo, int RepoSeq)> rows, bool outcomeVest)
    {
        if (rows.Count == 0) return;
        sb.Append($"\n  ── the SLOPE ({rows.Count} instances; head-only accuracy, gold STEERS the outcome-vest — the agentic/closed-loop eval, NOT frozen-comparable) ──\n");
        sb.Append($"    outcome-vest={outcomeVest}: head-localization climb (or its absence) as OUTCOME-VESTED rules accrete IS the verdict on whether the task signal in the loop makes the weights discriminative.\n");

        sb.Append("  by-arrival deciles (does the score CLIMB as vested structure accretes across the stream):\n");
        int D = Math.Min(10, rows.Count);
        for (int d = 0; d < D; d++)
        {
            int lo = d * rows.Count / D, hi = (d + 1) * rows.Count / D;
            var b = rows.GetRange(lo, hi - lo);
            if (b.Count == 0) continue;
            sb.Append($"    decile {d + 1,2} [{lo + 1,3}–{hi,3}] n={b.Count,3} · vis-file@1 {100.0 * b.Count(x => x.File1) / b.Count,5:F1} · BEACON-file@1 {100.0 * b.Count(x => x.BeaconF1) / b.Count,5:F1} · fn@5 {100.0 * b.Count(x => x.Fn5) / b.Count,5:F1} · " +
                      $"standing {(int)b.Average(x => x.R.StandingRules),6} · vested {(int)b.Average(x => x.R.OutcomeCreditedRules),5}\n");
        }
        int third = Math.Max(1, rows.Count / 3);
        var first = rows.GetRange(0, third); var last = rows.GetRange(rows.Count - third, third);
        double vFirst = 100.0 * first.Count(x => x.File1) / first.Count, vLast = 100.0 * last.Count(x => x.File1) / last.Count;
        double bFirst = 100.0 * first.Count(x => x.BeaconF1) / first.Count, bLast = 100.0 * last.Count(x => x.BeaconF1) / last.Count;
        double f5First = 100.0 * first.Count(x => x.Fn5) / first.Count, f5Last = 100.0 * last.Count(x => x.Fn5) / last.Count;
        sb.Append($"  ARRIVAL SLOPE: vis-file@1 {vFirst:F1}→{vLast:F1} (Δ{vLast - vFirst:+0.0;-0.0}) · BEACON-file@1 {bFirst:F1}→{bLast:F1} (Δ{bLast - bFirst:+0.0;-0.0}) · fn@5 {f5First:F1}→{f5Last:F1} (Δ{f5Last - f5First:+0.0;-0.0})\n");

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

        int locked = rows.Count(x => x.R.Locked);
        sb.Append($"  grok-lock {locked}/{rows.Count} · vested {rows.Sum(x => x.R.OutcomeCredited)} / evicted {rows.Sum(x => x.R.Evicted)} · final vested-rules {(rows.Count > 0 ? rows[^1].R.OutcomeCreditedRules : 0)} (the outcome-corroborated grammar — the task-discriminative structure the static weight lacked)\n");

        // ── THE VALUE-ALIGNMENT READ (the value-layer's decisive verdict) — does the mind's OWN self-model coherence
        // at commit predict correctness? Split instances by ParseDepth (LOWER = deeper self-model = more coherent
        // understanding). If the deep-parse half hits file@1 MORE than the shallow half, the mind's INTERNAL value
        // signal (coherence) tracks the external oracle WITHOUT being trained on it — value-alignment from within. If
        // the halves are equal, the self-model coherence is decoupled from correctness HERE (reported straight). ──
        var byDepth = rows.Where(x => x.R.ParseDepth > 0).OrderBy(x => x.R.ParseDepth).ToList();
        if (byDepth.Count >= 4)
        {
            int h = byDepth.Count / 2;
            var deep = byDepth.GetRange(0, h);         // lowest ParseDepth = deepest self-model coherence
            var shallow = byDepth.GetRange(h, byDepth.Count - h);
            double deepF1 = 100.0 * deep.Count(x => x.BeaconF1) / Math.Max(1, deep.Count);
            double shalF1 = 100.0 * shallow.Count(x => x.BeaconF1) / Math.Max(1, shallow.Count);
            sb.Append($"  VALUE-ALIGNMENT (self-model coherence predicts correctness?): deep-parse[{deep.Count}] BEACON-file@1 {deepF1:F1} vs shallow-parse[{shallow.Count}] {shalF1:F1} (Δ{deepF1 - shalF1:+0.0;-0.0}) — >0 = the mind's INTERNAL value signal tracks the oracle without being trained on it (alignment-from-within); ≈0 = coherence decoupled here\n");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE DRIVE — one instance, threading the STANDING grammar (channel 1) AND the corroboration field (the
    //  outcome-vested grammar reads the issue↔file overlap the SAME way CovBeacon does, but its weights are the
    //  OUTCOME-VESTED rules, not a frozen self-supervised weight). Gold loaded only by the caller AFTER this returns.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static LoopResult Drive(string dir, int maxLooks, int k, bool verbose, bool expand, bool grammarCarry, bool valueLayer, bool outcomeVest, double wVest,
                                    StandingGrammar standing, CorroborationMind corrob, out List<Site> outSites)
    {
        string issue = File.ReadAllText(Path.Combine(dir, "query.txt"));
        byte[] issueBytes = Encoding.UTF8.GetBytes(issue);
        var sites = LoadSites(Path.Combine(dir, "sites.jsonl"));
        outSites = sites;                                             // threaded to the caller's AbsorbOutcome — no re-load of what this drive already parsed
        var fileDocs = sites.Where(s => s.Kind == "module").ToList();
        var fileByPath = new Dictionary<string, Site>();
        foreach (var f in fileDocs) fileByPath[f.Path] = f;

        var bm25 = new Bm25Index(sites.Select(s => s.Text).ToList());
        bm25.IndexModules(Enumerable.Range(0, sites.Count).Where(i => sites[i].Kind == "module"));
        var issueToks = Toks(issue).Distinct().ToList();
        var issueTokSet = new HashSet<string>(issueToks, StringComparer.Ordinal);
        var baseScore = bm25.Score(issueToks);

        // the BASE-ONLY beacon (pure BM25) — the counterfactual the recovery journal reads.
        var (baseBeaconOrder, _) = AggregateMaxFiles(sites, baseScore);
        var baseBeaconFiles = baseBeaconOrder.OrderBy(IsTest).ToList();

        // THE OUTCOME-VEST FIELD — the document side of the crux. The corroboration mind reads the issue's grammar
        // terms through its OUTCOME-VESTED rules (rules corroborated by past gold outcomes) and adds a per-file bonus
        // for shared vested structure. This is CovBeacon's shape (issue↔file overlap under the grammar) but the weight
        // is the task-outcome outcomeCredit, not a frozen self-supervised weight — the discriminative lever the static arm
        // lacked. Silent until the mind has vested something (early stream = pure BM25, byte-identical to navdyn base).
        var blend = (double[])baseScore.Clone();
        if (outcomeVest) corrob.BlendVestField(blend, baseScore, sites, issueBytes, wVest);
        var scratch = new double[blend.Length];                                 // the --no-value control arm's per-candidate trial buffer (copy-in, keep-by-swap; no clone-per-candidate)

        var (beaconOrder, _) = AggregateMaxFiles(sites, blend);
        var beaconRank = new Dictionary<string, int>();
        for (int i = 0; i < beaconOrder.Count; i++) beaconRank[beaconOrder[i]] = i;
        var descendOrder = beaconOrder;
        var descendRank = beaconRank;
        var quoted = fileDocs.Where(f => issue.Contains(f.Path, StringComparison.Ordinal))
                             .OrderBy(f => beaconRank.GetValueOrDefault(f.Path, int.MaxValue)).Select(f => f.Path).ToList();

        // CHANNEL 1 — seed induction from the STANDING grammar (accreting when grammarCarry).
        using var mind = new RepoGrok(standing.Snapshot(grammarCarry));
        var minted = new List<string>();                                        // the mind's VALUE-HYPOTHESES (the terms it discovered worth acquiring — calibrated by the outcome)
        int vestedCount = 0, evictedCount = 0, jumps = 0;
        // THE VALUE FIELD — the per-site self-model-VALUE bonus (governed by DiscoverValue). Accretes across looks;
        // added to the base to form the descend-governing blend. This is where the mind's DISCOVERED value governs the
        // task-steps (which files it reads next) — NOT the metric. Zero until the grammar groks + finds a valued gap.
        var valueField = new double[sites.Count];

        var visited = new List<string>(); var visitedSet = new HashSet<string>();
        string lastTop = ""; int persist = 0; int looks = 0;
        double know = 0, lastKnow = -1; bool jumpNext = false;
        double parseDepth = 1.0, lastParseDepth = 1.0;                          // the self-model DEPTH read (ParsedSize/byte — LOWER = deeper); its DROP is the self-model gain the value-layer chases
        string verdict = "budget"; double lastMargin = 0;
        List<(Site S, double Score)> field = new();

        for (int look = 0; look < maxLooks; look++)
        {
            // STAGE 3 NAVIGATE+SAMPLE — descend the VALUE-GOVERNED frontier (base beacon re-sorted by the value field,
            // so the mind reads the files its self-model predicts will cohere it). STAGE 2's adaptive stop reads the
            // self-model coherent below. `blend` = base(+vestField) + valueField, dimension-matched to the live base
            // field at each reblend so value can steer without a fixed blend coefficient.
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
            lastKnow = know; lastParseDepth = parseDepth;
            know = mind.IssueCoverage(issueBytes);
            parseDepth = mind.IssueParseDepth(issueBytes);                       // the self-model DEPTH — the value signal's live read

            // STAGE 1 DECOMPOSE + STAGE 2 VALUE-DISCOVER + ACQUISITION-STRATEGY. DiscoverValue decomposes the issue
            // into gap sub-goals, appraises each by SELF-MODEL
            // gain (depth × criticality-health — never the metric), and returns the valued acquisition terms. Their
            // BM25 field, scaled by the discovered value, ACCRETES into valueField → re-sorts the descend (value
            // governs the task). The terms are the mind's value-hypotheses (DREAM), calibrated post-hoc by the outcome.
            if (expand && know < 0.999)
            {
                if (valueLayer)
                {
                    var valued = mind.DiscoverValue(issueBytes, issueTokSet, minted, bm25, fileDocs.Count);
                    if (valued.Count > 0)
                    {
                        double vMax = valued.Max(x => x.Value);
                        foreach (var (t, v) in valued)
                        {
                            if (vMax <= 0) break;
                            var tScore = bm25.Score([t]);
                            double w = WExpand * (v / vMax);                    // the acquisition-strategy field: the term weighted by its DISCOVERED VALUE (not raw BM25) — high-value handles pull hardest
                            for (int i = 0; i < valueField.Length; i++) valueField[i] += w * tScore[i];
                            minted.Add(t);
                        }
                        vestedCount += valued.Count;
                        // RE-BLEND + RE-SORT — value governs the descend order (the task-steps follow the discovered value).
                        Array.Copy(baseScore, blend, blend.Length);             // reuse the blend buffer (line 486) — a fresh Clone per re-blend allocated a double[sites] each look
                        if (outcomeVest) corrob.BlendVestField(blend, baseScore, sites, issueBytes, wVest);
                        BlendValueField(blend, baseScore, valueField);
                        (descendOrder, _) = AggregateMaxFiles(sites, blend);
                        descendRank = new Dictionary<string, int>();
                        for (int i = 0; i < descendOrder.Count; i++) descendRank[descendOrder[i]] = i;
                    }
                }
                else
                {
                    // THE CONTROL (--no-value): the naive expansion — mint terms by the residual couplings + accept on
                    // visited-set non-dilution (navigate parity), NO self-model value governing them. Isolates what the
                    // value layer buys: this arm still has channel-1 accretion + the outcome-vest field, just no value.
                    var cand = mind.MintTerms(issueBytes, issueTokSet, minted, bm25, fileDocs.Count);
                    if (cand.Count > 0)
                    {
                        var vestedNow = new List<string>();
                        foreach (var t in cand)
                        {
                            // trial into the reusable scratch (byte-identical to a fresh clone); keep-by-swap on vest.
                            Array.Copy(blend, scratch, blend.Length);
                            var tScore = bm25.Score([t]);
                            for (int i = 0; i < scratch.Length; i++) scratch[i] += WExpand * tScore[i];
                            double mCur = FieldMargin(sites, blend, visitedSet), mTrial = FieldMargin(sites, scratch, visitedSet);
                            if (mTrial >= mCur - VestMarginEps) { (blend, scratch) = (scratch, blend); vestedNow.Add(t); }
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
            }

            // STAGE 4 DECIDE (provisional, VALUE-ALIGNED) — the visited-set field over the value-governed blend, so the
            // committed leader is the file whose evidence most cohered the self-model, not the external rank metric.
            field = Enumerable.Range(0, sites.Count).Where(i => visitedSet.Contains(sites[i].Path))
                              .Select(i => (S: sites[i], Score: blend[i])).OrderByDescending(x => x.Score).ToList();

            var fileLocal = field.GroupBy(x => x.S.Path).Select(gr => (Path: gr.Key, Score: gr.Max(x => x.Score)))
                                 .OrderByDescending(x => x.Score).ThenBy(x => beaconRank[x.Path]).ToList();
            string top = fileLocal.Count > 0 ? fileLocal[0].Path : "";
            double margin = fileLocal.Count > 1 && fileLocal[0].Score > 0 ? (fileLocal[0].Score - fileLocal[1].Score) / fileLocal[0].Score : 1.0;
            persist = top == lastTop && top.Length > 0 ? persist + 1 : 0; lastTop = top; lastMargin = margin;
            // STAGE 2 STOP-CRITERION (SELF-MASTERY interleave): land when the self-model reads COHERENT — the field
            // concentrated + persistent AND the self-model depth PLATEAUED (ΔparseDepth small: reading more of this
            // neighbourhood no longer deepens understanding — the mind knows it has enough). JUMP when flat + the
            // depth-residual plateaued (this region explains nothing new — hop). Task-solving and self-modeling are one.
            bool depthPlateau = Math.Abs(parseDepth - lastParseDepth) < CovPlateauEps;
            bool land = persist >= SPersist - 1 && margin >= TauMargin && depthPlateau && look > 0;
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

        // THE BEACON — the FINAL full-corpus blend ranking; the scorer splices this tail.
        var (beaconFinalOrder, _) = AggregateMaxFiles(sites, blend);
        var beaconFiles = beaconFinalOrder.OrderBy(IsTest).ToList();

        // CHANNEL 1 — contribute this instance's novel grammar to the standing vocabulary.
        if (grammarCarry) standing.Absorb(mind.HarvestBinary());

        return new LoopResult(Path.GetFileName(dir.TrimEnd('/')), visitedFiles, beaconFiles, baseBeaconFiles, localFnSites, looks, mind.MaxSpan,
                              mind.Locked, mind.LockLook, vestedCount, evictedCount, standing.Count, corrob.OutcomeCreditedRuleCount, 0, verdict,
                              minted, parseDepth);
    }

    private static void BlendValueField(double[] blend, double[] baseScore, double[] valueField)
    {
        double baseMax = 0, valueMax = 0;
        for (int i = 0; i < blend.Length; i++)
        {
            if (baseScore[i] > baseMax) baseMax = baseScore[i];
            if (valueField[i] > valueMax) valueMax = valueField[i];
        }
        if (baseMax <= 0 || valueMax <= 0) return;
        double scale = baseMax / valueMax;
        for (int i = 0; i < blend.Length; i++) blend[i] += scale * valueField[i];
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE CORROBORATION MIND — THE DIFFERENTIAL RL CORE (Mode 2 — the coordinator's decisive distinction). Holds a
    //  standing Tape + a standing Loom. The reward is CORRECTNESS: the mind's OWN committed-path spans vest ONLY when
    //  the path reached gold, so the vest signal DIFFERS on a hit vs a miss — that difference IS the RL, not a costume.
    //
    //  HIT  (gold ∈ committed top-K): the mind's path spans (DREAM) + the gold (EVIDENCE, source="gold") both land; the
    //       gold WITNESSES the path-dream rules ⇒ they vest — the correct reasoning self-reinforces.
    //  MISS (gold ∉ committed top-K): the mind's path spans (DREAM) + the gold as a DREAM-TARGET (source="miss:", NOT
    //       evidence) land; the path finds NO cross-source EVIDENCE corroboration ⇒ it does NOT vest. The gold sits as an
    //       uncorroborated dream — the missed structure the mind studies, never rewarded as if found.
    //
    //  THE DIFFERENTIAL (why this is RL, not Mode-1 supervised-append): in Mode 1 the gold's spans append + vest
    //  unconditionally (identical on right/wrong — pretrain-on-task in an RL costume). Here the gold's PROVENANCE flips
    //  on the outcome (Real on a hit, Replay on a miss), so the mind's-path outcomeCredit is gated on correctness. PathOutcomeCredited
    //  counts the mind's-path spans that vested — >0 only on hits (HitOutcomeCreditedTotal ≫ MissOutcomeCreditedTotal is the proof).
    //
    //  WHY THIS ANSWERS THE CRUX (vs navdyn's RankHead): the RankHead learns a token→gold-rate table BESIDE the
    //  grammar, unconditionally. This vests the GRAMMAR RULES of the mind's OWN correct path — credit lands on the
    //  reasoning that WORKED, and the wrong path is starved. The outcome is a correctness-gated event on the grammar.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private sealed class CorroborationMind : IDisposable
    {
        private readonly int _wScale;
        private readonly int _capBytes;
        private readonly Tape _tape = new();
        private readonly Loom _loom;   // constructed at the mind's wScale (ctor) — a default wScale-1 loom rejects the ×8 Real-span splice (Loom's weight gate, ead96a0)
        private readonly Journal _journal = new();
        private long _splicedSpans;
        private int _step;
        private RePairResult _g;
        // the VESTED-rule expansion set — the outcome-corroborated structure. Keyed on the byte-expansion of each rule
        // that carries a vested supporter (the loom's ids churn per re-induce, so the durable identity is the EXPANSION
        // bytes, not the rule id). Read by BlendVestField as the document-side task-discriminative weight.
        private readonly Dictionary<string, double> _vestedExp = new(StringComparer.Ordinal);   // expansion-string → accumulated vest weight
        // the per-instance vest-field cache: rawBonus depends only on (sites, issue, _vestedExp) — all constant across
        // one instance's looks (_vestedExp mutates only AFTER the instance). Keyed on the sites reference; cleared when
        // _vestedExp moves (HarvestOutcomeCredited/Load). The per-look recompute was byte-identical work × looks.
        private List<Site>? _bonusSites;
        private double[]? _bonusRaw;
        private double _bonusRawMax;
        public int OutcomeCreditedRuleCount => _vestedExp.Count;
        public int TotalOutcomeCredited { get; private set; }

        // ── THE STANDING MIND'S PROPRIOCEPTION (the criticality-health axis across passes — the renormalization sentinel).
        // MeanZ = the honest criticality exponent of the accumulated corroboration grammar (MeshHomeostat basin −0.70).
        // Re-read after every re-induce. If it slides toward the −1.11 sink across passes while vest RISES, the re-fed
        // learned real is renormalizing like dream (the bounded world running dry — [[criticality-needs-perpetual-novel-real]]).
        public double MeanZ { get; private set; } = double.NaN;
        public double MaxSpan { get; private set; }
        private void ReadProprioception() { if (_g.Rules is { Length: > 0 }) { var rn = Engine.RenormStats(_g); MeanZ = rn.MeanZ; MaxSpan = rn.MaxSpan; } }

        // the tape's event byte log (<run>/tape.spanlog) — dropped spans' forensic home, mounted BEFORE any Load so a
        // resumed checkpoint carrying evacuated entries resolves against the same file (Cortex.Loop's mount discipline).
        public CorroborationMind(int wScale, int capBytes, string tapeLogPath)
        {
            _wScale = wScale; _capBytes = capBytes;
            _loom = new Loom(256, '\n', wScale);
            _tape.MountLog(new FileStream(tapeLogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read));
        }

        /// THE ROLLING SHED — the VestTapeCapBytes contract, enforced. Past the cap, the OLDEST unvested nav-spans DROP
        /// (oldest-first, id-ascending — AgentSolve's aestivation shape) until the residents fit; evidence (vested dreams
        /// + gold) never drops (the Tape law), so the corroborated structure persists while the audit stays O(cap).
        /// Runs AFTER the corroboration event, so every span had its vest chance against THIS instance's gold first;
        /// a dropped span forfeits only later cross-instance carryover — the declared rolling-budget semantics.
        private void ShedToCap()
        {
            long resident = _tape.ResidentBytes;
            if (resident <= _capBytes) return;
            var drop = new List<TapeEventID>();
            for (int i = 0; i < _tape.Count && resident > _capBytes; i++)
            {
                if (_tape.IsEvidenceAt(i)) continue;
                var id = _tape.ResidentEventIDs[i];
                if (!_tape.Resolve(id, out var span)) continue;
                drop.Add(id);
                resident -= span.Length + 1;
            }
            if (drop.Count > 0) _tape.Evacuate(Array.Empty<TapeEventID>(), drop);
        }

        /// STAGE 5 APPEND + STAGE 6 THE DIFFERENTIAL RL CALIBRATION (Mode 2 — reward CORRECTNESS, not append answers).
        /// The reward is `pathHit`: did the mind's OWN committed path REACH the gold? The vest signal is DIFFERENT on a
        /// hit vs a miss — THIS is what makes it RL, not supervised-append:
        ///
        ///   HIT  (gold ∈ the mind's committed top-K): append the mind's TRAVERSED-PATH spans (the text of the files it
        ///        actually committed to) as DREAM, AND the gold as EVIDENCE (source="gold"). Cross-corroboration Corroborate
        ///        then vests the PATH-dream rules the gold confirms — the mind's CORRECT REASONING self-reinforces (the
        ///        gold WITNESSES the path it took). The value-hypothesis vests too (its prediction materialized).
        ///   MISS (gold ∉ the committed top-K): append the mind's path spans as DREAM but the gold as a DREAM-TARGET
        ///        (source="miss:", NOT evidence). The path-dream gets NO cross-source corroboration ⇒ it does NOT vest (the
        ///        wrong path is correctly starved). The gold sits as an uncorroborated dream — the missed structure the
        ///        mind STUDIES on re-induce (revising the value-model), never rewarded as if it had been found.
        ///
        /// The DIFFERENTIAL: on a hit the mind's path-rules vest; on a miss they do not (and the answer becomes a
        /// study-target instead). Getting it right causes reinforcement getting it wrong does not — the correctness
        /// reward. `pathOutcomeCredited` (the mind's-path spans that vested) is the RL signal; it is >0 only on hits.
        /// Returns the total # of spans vested (path + value + prior-instance carryover) this call.
        public int AbsorbOutcome(string dir, List<Site> sites, HashSet<string> goldFiles, List<string> valueTerms, List<string> committedPath, bool pathHit, bool trueHit)
        {
            if (goldFiles.Count == 0) return 0;
            string inst = Path.GetFileName(dir.TrimEnd('/'));
            var siteByPath = new Dictionary<string, List<Site>>(StringComparer.Ordinal);
            foreach (var s in sites) (siteByPath.TryGetValue(s.Path, out var l) ? l : siteByPath[s.Path] = new()).Add(s);

            // ── (a) THE MIND'S TRAVERSED PATH — the spans of the files it COMMITTED to (its top-K answer). This is the
            // reasoning path the mind actually took. It enters as DREAM under a per-instance PATH source; on a HIT the
            // gold corroborationes it and it vests (self-reinforcement); on a MISS it stays dream (the wrong path, unrewarded).
            // We fingerprint WHICH spans are the mind's path so pathOutcomeCredited can be counted (the RL signal, hit-only).
            var pathSources = new HashSet<string>(StringComparer.Ordinal);
            int nPathSpans = 0;
            foreach (var p in committedPath.Take(3))
            {
                string psrc = "path:" + inst;
                pathSources.Add(psrc);
                if (siteByPath.TryGetValue(p, out var pss))
                    foreach (var ps in pss.Where(s => s.Kind == "module"))
                        foreach (var mem in Engine.SplitLines(Encoding.UTF8.GetBytes(ps.Text)))
                        { var span = mem.ToArray(); if (span.Length == 0) continue; Splice(span, psrc, Provenances.Replay); nPathSpans++; }
            }

            // ── (b) THE VALUE-HYPOTHESIS — the mind's discovered value-terms as DREAM (what it judged worth wanting).
            // Vests on a hit alongside the path (the value-prediction that led to the correct answer is confirmed).
            if (valueTerms.Count > 0)
            {
                var vspan = Encoding.UTF8.GetBytes(string.Join(" ", valueTerms));
                if (vspan.Length > 0) { Splice(vspan, "value:" + inst, Provenances.Replay); pathSources.Add("value:" + inst); }
            }

            // ── (c) THE OUTCOME, ROLE-SWITCHED ON CORRECTNESS (the differential's core):
            //   HIT  → gold is EVIDENCE (source="gold"): a generator-independent WITNESS that vests the mind's path.
            //   MISS → gold is a DREAM-TARGET (source="miss:<inst>"): an uncorroborated dream. It does NOT corroboration the
            //          mind's path (same-instance-miss source is still a DREAM, carries no evidence weight), so the
            //          path cannot vest off it — the missed structure is planted to study, never rewarded.
            var goldProv = pathHit ? Provenances.Real : Provenances.Replay;
            string goldSrc = pathHit ? "gold" : "miss:" + inst;
            var goldSites = sites.Where(s => goldFiles.Contains(s.Path) && s.Kind == "module").ToList();
            foreach (var gs in goldSites)
                foreach (var mem in Engine.SplitLines(Encoding.UTF8.GetBytes(gs.Text)))
                { var span = mem.ToArray(); if (span.Length == 0) continue; Splice(span, goldSrc, goldProv); }

            // RE-INDUCE the standing tape (loom O(Δ)); the grammar spans all prior + this instance's dream/evidence.
            _loom.Pump();
            _g = _loom.Result(_tape);
            ReadProprioception();   // refresh MeanZ/MaxSpan — the cross-pass criticality sentinel reads the latest re-induce

            // STAGE 6 — the CORROBORATION EVENT. crossReflect:true ⇒ a dream vests iff a DIFFERENT source exercised its
            // rule. On a HIT "gold" (a foreign evidence source) corroborationes the mind's path-dream rules ⇒ they vest. On a
            // MISS the only new source is "miss:<inst>" (a DREAM, not evidence) — it cannot vest anything; the path-dream
            // finds no cross-source EVIDENCE corroboration and stays dream. THE DIFFERENTIAL is exactly this evidence/dream flip.
            var audit = Pearl.Audit(_tape, in _g, _wScale, crossReflect: true);
            int before = _tape.ReflectedReplayCount;
            int vested = Pearl.Corroborate(in audit, _tape, _journal, _step++);
            TotalOutcomeCredited += vested;

            // COUNT THE RL SIGNAL — how many of the MIND'S-PATH spans vested this call, bucketed by the TRUE hit/miss
            // (NOT the forced pathHit): in Mode-2 a true-miss's path vests only via cross-instance carryover; in Mode-1
            // (--supervised, pathHit forced) a true-miss's path vests off THIS gold too — that nonzero missVest is the
            // supervised-append signature (wrong paths rewarded). Splitting by trueHit makes the two modes contrast.
            PathOutcomeCreditedLast = CountPathOutcomeCredited(in audit, pathSources);
            if (trueHit) HitOutcomeCreditedTotal += PathOutcomeCreditedLast; else MissOutcomeCreditedTotal += PathOutcomeCreditedLast;

            // HARVEST the outcome-vested structure into the value-field weight (the durable task-discriminative grammar).
            if (vested > 0) HarvestOutcomeCredited(in audit);
            ShedToCap();   // the rolling budget — enforced at the instance boundary, after the vest chance
            return vested;
        }

        // ── THE LOOPBACK COMBUSTION (dream-between-passes) — INTRINSIC consolidation with NO new external gold. Between
        // external passes, re-run the corroboration cycle N times on the ALREADY-ACCUMULATED tape: re-induce (loom O(Δ)),
        // audit cross-reflection, corroborate. This lets the mind's OWN accreted structure reflect each other — a dream on
        // lived grammar (the standing evidence spans keep corroborationing dreams that recurred), the intrinsic signal the
        // intrinsic loopback interleaves with the external repos. Each night also HARVESTS newly-vested structure into
        // the vest-field, so a genuine consolidation DEEPENS the task-discriminative grammar the next pass reads.
        //
        // THE RENORMALIZATION SENTINEL: pure re-feed of learned real renormalizes like dream ([[criticality-needs-
        // perpetual-novel-real]]). If MeanZ sinks across the nights while vest rises, the loopback is running the bounded
        // world dry (memorizing its own echo), not deepening it — reported straight via the returned MeanZ. Deterministic:
        // integer outcomeCredit, the same _step counter, stable audit order — same tape in ⇒ same consolidation out.
        public (int OutcomeCredited, double MeanZ) ReplayConsolidate(int nights)
        {
            int total = 0;
            for (int i = 0; i < nights; i++)
            {
                _loom.Pump();
                _g = _loom.Result(_tape);
                ReadProprioception();
                var audit = Pearl.Audit(_tape, in _g, _wScale, crossReflect: true);
                int v = Pearl.Corroborate(in audit, _tape, _journal, _step++);
                total += v;
                TotalOutcomeCredited += v;
                if (v > 0) HarvestOutcomeCredited(in audit);
            }
            return (total, MeanZ);
        }

        /// The RL signal: how many of the MIND'S-PATH dream spans (path + value sources) transitioned to evidence this
        /// corroboration. Walks the audit's supporters for cross-source-corroborated rules and counts supporters whose
        /// source is one of the mind's path sources AND that just became evidence. >0 ⟺ the mind's correct path vested.
        public int PathOutcomeCreditedLast { get; private set; }
        public long HitOutcomeCreditedTotal { get; private set; }
        public long MissOutcomeCreditedTotal { get; private set; }

        private int CountPathOutcomeCredited(in PearlAudit audit, HashSet<string> pathSources)
        {
            if (audit.JewelSources is null) return 0;
            int c = 0;
            var counted = new HashSet<long>();
            for (int r = 0; r < audit.SawReal.Length; r++)
            {
                var ws = audit.JewelSources[r];
                if (ws is null || ws.Count < 2) continue;
                // a rule corroborated by a FOREIGN evidence source (gold/corpus) whose supporters include the mind's path:
                bool foreignEvidence = false; foreach (var s in ws) if (s == "gold" || s == "corpus") { foreignEvidence = true; break; }
                if (!foreignEvidence) continue;
                foreach (long sid in audit.Supporters[r])
                {
                    if (!counted.Add(sid)) continue;
                    var span = new TapeEventID(sid);
                    if (pathSources.Contains(_tape.SourceOf(span)) && _tape.IsEvidence(span)) c++;
                }
            }
            return c;
        }

        private void Splice(byte[] span, string source, Provenances prov)
        {
            _tape.Append(span, source, prov);
            _loom.SpliceEvent(span, _splicedSpans++, weight: prov == Provenances.Real ? (byte)_wScale : (byte)1);
        }

        // record each outcome-corroborated rule's expansion into the vest-field weight (the durable task-discriminative
        // structure). A rule counts iff (i) it clears the reflect floor and (ii) its jewel-source set is cross-source
        // (a gold/corpus source corroborated it) — the exact rules Corroborate just outcome-credited dreams for.
        private void HarvestOutcomeCredited(in PearlAudit audit)
        {
            if (audit.JewelSources is null) return;
            for (int r = 0; r < _g.Rules.Length; r++)
            {
                if (audit.ExpLen[r] < Pearl.ReflectFloorBytes) continue;
                var ws = audit.JewelSources[r];
                if (ws is null || ws.Count < 2) continue;             // needs ≥2 distinct sources — the cross-reflection that reflected
                bool hasGold = false; foreach (var s in ws) if (s == "gold" || s == "corpus") { hasGold = true; break; }
                if (!hasGold) continue;
                var exp = Reconstruct.Expand(_g.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)r)]);
                var key = Encoding.UTF8.GetString(exp);
                // weight = expansion length (deeper = more specific structure ⇒ more discriminative), accumulated so a
                // rule corroborated across multiple instances (a recurring issue↔gold idiom) weighs more.
                _vestedExp[key] = _vestedExp.GetValueOrDefault(key) + audit.ExpLen[r];
            }
            _bonusSites = null; _bonusRaw = null;                     // the vest-field moved — the per-instance bonus cache is stale
        }

        // ── THE DOCUMENT-SIDE READ (the crux payoff) — BlendVestField adds a per-file bonus for the OUTCOME-VESTED
        // structure the issue and file SHARE. For each vested-rule expansion that appears in BOTH the issue and file
        // f, f gains its weight; normalized to BM25's scale (CovBeacon's discipline — the vested field re-ranks on
        // corroborated structure without drowning the lexical field). This is where past outcomes STEER retrieval. ──
        public void BlendVestField(double[] blend, double[] baseScore, List<Site> sites, byte[] issueBytes, double wVest)
        {
            if (_vestedExp.Count == 0) return;
            if (!ReferenceEquals(_bonusSites, sites))                // first blend of the instance — compute rawBonus once; re-blends reuse it
            {
                string issueStr = Encoding.UTF8.GetString(issueBytes);
                // the vested idioms the ISSUE carries (present in the issue text) — only shared structure can re-rank.
                var issueOutcomeCredited = new List<(string Exp, double W)>();
                foreach (var (exp, w) in _vestedExp)
                    if (exp.Length >= Pearl.ReflectFloorBytes && issueStr.Contains(exp, StringComparison.Ordinal)) issueOutcomeCredited.Add((exp, w));
                var rawBonus = new double[sites.Count]; double rawMax = 0;
                for (int i = 0; i < sites.Count; i++)
                {
                    if (sites[i].Kind != "module") continue;         // MODULE site only — the file aggregate reads it, fn field untouched (CovBeacon's isolation)
                    double b = 0; var txt = sites[i].Text;
                    foreach (var (exp, w) in issueOutcomeCredited) if (txt.Contains(exp, StringComparison.Ordinal)) b += w;
                    rawBonus[i] = b; if (b > rawMax) rawMax = b;
                }
                _bonusSites = sites; _bonusRaw = rawBonus; _bonusRawMax = rawMax;
            }
            if (_bonusRawMax <= 0) return;
            double bm25Max = 0; for (int i = 0; i < baseScore.Length; i++) if (baseScore[i] > bm25Max) bm25Max = baseScore[i];
            if (bm25Max <= 0) return;
            double norm = wVest * bm25Max / _bonusRawMax;
            var raw = _bonusRaw!;
            for (int i = 0; i < sites.Count; i++) if (raw[i] > 0) blend[i] += norm * raw[i];
        }

        public Tape Tape => _tape;

        public void Save(CkptWriter w)
        {
            w.Section(0x54415045); _tape.Save(w);
            w.Section(0x4C4F4F4D); _loom.Save(w);
            w.Section(0x4752414D); Checkpoint.WriteGrammar(w, _g);
            w.I64(_splicedSpans); w.I32(_step); w.I32(TotalOutcomeCredited);
            w.F64(MeanZ); w.F64(MaxSpan);
            WriteStringDoubleMap(w, _vestedExp);
            w.I32(PathOutcomeCreditedLast); w.I64(HitOutcomeCreditedTotal); w.I64(MissOutcomeCreditedTotal);
            w.Section(0x4A524E4C); _journal.Save(w);
        }

        public void Load(CkptReader r)
        {
            r.Expect(0x54415045); _tape.Load(r);
            r.Expect(0x4C4F4F4D); _loom.Load(r, _tape); _loom.SpliceNew(_tape);
            r.Expect(0x4752414D); _g = Checkpoint.ReadGrammar(r);
            _splicedSpans = r.I64(); _step = r.I32(); TotalOutcomeCredited = r.I32();
            MeanZ = r.F64(); MaxSpan = r.F64();
            _vestedExp.Clear(); foreach (var (k, v) in ReadStringDoubleMap(r)) _vestedExp[k] = v;
            _bonusSites = null; _bonusRaw = null;
            PathOutcomeCreditedLast = r.I32(); HitOutcomeCreditedTotal = r.I64(); MissOutcomeCreditedTotal = r.I64();
            r.Expect(0x4A524E4C); _journal.Load(r, _tape);
        }

        public void Dispose() { _loom.Dispose(); _tape.Dispose(); }   // the tape owns the mounted spanlog stream
    }

    private static string EmitJson(LoopResult r, bool goldFile1, bool goldBeacon1, bool goldFn5, int goldBeaconRank, string repo, int repoSeq, int pass)
    {
        var sb = new StringBuilder();
        sb.Append("{\"pass\":").Append(pass).Append(",\"instance_id\":").Append(JsonStr(r.Instance));
        sb.Append(",\"repo\":").Append(JsonStr(repo)).Append(",\"repo_seq\":").Append(repoSeq);
        sb.Append(",\"looks\":").Append(r.Looks).Append(",\"verdict\":").Append(JsonStr(r.Verdict));
        sb.Append(",\"locked\":").Append(r.Locked ? "true" : "false").Append(",\"lock_look\":").Append(r.LockLook);
        sb.Append(",\"maxspan\":").Append(R(r.MaxSpan)).Append(",\"vested\":").Append(r.OutcomeCredited).Append(",\"evicted\":").Append(r.Evicted);
        sb.Append(",\"standing_rules\":").Append(r.StandingRules).Append(",\"vested_rules\":").Append(r.OutcomeCreditedRules).Append(",\"gold_vested\":").Append(r.GoldOutcomeCreditedThis);
        sb.Append(",\"parse_depth\":").Append(R(r.ParseDepth)).Append(",\"value_terms\":").Append(r.ValueTerms.Count);   // the self-model depth (value-alignment read) + how many value-hypotheses the mind discovered this instance
        sb.Append(",\"gold_file1_head\":").Append(goldFile1 ? "true" : "false").Append(",\"gold_beacon1\":").Append(goldBeacon1 ? "true" : "false");
        sb.Append(",\"gold_beacon_rank\":").Append(goldBeaconRank).Append(",\"gold_fn5_head\":").Append(goldFn5 ? "true" : "false");
        sb.Append(",\"visited_files\":[").Append(string.Join(",", r.VisitedFiles.Select(JsonStr))).Append(']');
        sb.Append(",\"beacon_files\":[").Append(string.Join(",", r.BeaconFiles.Take(10).Select(JsonStr))).Append(']');
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
