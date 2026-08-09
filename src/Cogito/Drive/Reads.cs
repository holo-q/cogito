namespace Cogito;

using System.Buffers;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

/// The mutable rolling state of a reader between keyframes.  The arrays are
/// queue-order snapshots (oldest first), not a second curve stream.  A delta
/// consumer can apply this receipt to a keyframe without re-running the read
/// algorithm and therefore preserve the exact window anchor at the next step.
internal readonly record struct ReadsRollingWindowDelta(
    double[] Momentum,
    double[] StrideMomentum,
    bool StrideRulesSeen,
    double[] Collapse,
    double[] JsTrajectory,
    double[] Meanz,
    bool HasPreviousHistogram,
    int[] PreviousHistogram,
    string StrideVerdict)
{
    internal bool HasState => Momentum is not null;
    internal bool IsEmpty => !HasState;

    internal static ReadsRollingWindowDelta Empty => default;
}

internal readonly record struct ReadsCheckpointDelta(
    long Cursor,
    string[] Excursions,
    ReadsRollingWindowDelta Rolling = default)
{
    internal bool IsEmpty => (Excursions?.Length ?? 0) == 0 && !Rolling.HasState;
}

// ── THE READ ──  the sparkline suite, the nervous system that makes a crash-out localize in
// one row. A shared home so the drain loops (Cortex single-node, Mesh multi-node) read the SAME instrument — the
// reads are the point, so they get a home, not a copy. A `Reads` instance carries a
// drive's rolling memory (momentum on TWO clocks — per-step and stride-cadence — plus collapse / JS windows, the
// previous byte-histogram, the HomeWatch, the excursion stream); `Step` folds one step's grammar + generated block
// into a LossReading and appends the
// excursion (the self-model's food). Each column's MEANING lives on LossReading; each port's provenance on its
// source. The compute is near-free — most numbers are already computed by the drive; this stops discarding them.

/// One step's reading of the loss-curve — the numbers the whole research arc watched instead of the state.
/// `MdlSaved` (Δmdl, the compression the grammar buys) + `Coverage` (Engine.CoverageOf on HELD-OUT text, the
/// generalization BREADTH signal) are the primary axes; `MaxSpan` (the deepest rule's byte-extent = correlation
/// length) is the DEPTH read the developmental curriculum is judged on — coverage saturates SHALLOW (words cover a
/// line's bytes), only deep concentration lifts maxSpan into the phrase/template scale (cogito-developmental-
/// curriculum: the metric-trap is reading breadth for depth). `Ingested` is the scaffold-drain progress — how much
/// of the corpus pool the frontier has accreted onto the tape by its own residual. `Concentration` (Gini, a collapse
/// detector) and `Distinct` (the ONLY collapse-robust read — repetition lowers it; the metabolism-probe keystone)
/// guard against reading a repetition-collapse as progress. `Momentum` is the slope of MdlSaved over recent STEPS
/// ("read the momentum, not the state" — a still-descending curve is a meadow); the drive's STOP decision reads its
/// stride-cadence twin (Reads.StrideVerdict), because MdlSaved is stair-stepped between grammar refreshes.
/// `NovelChain` is the generation-DEPTH read (the node-birth probe's unfakeable metric): the longest run of
/// consecutive DISTINCT generated lines each sharing a content-identifier with the previous — a repeat breaks it.
///
/// THE SPARKLINE SUITE — the reads that make a crash-out localize in one row:
///   `CvZ`/`Scales`/`MeanZ`     the GROK bell — RenormStats' per-scale Zipf-exponent variation (CvZ LOW ⟹ the SAME
///                              power-law at every scale = a critical RG fixed point = grokked), depth (Scales) and
///                              the −0.70 universality (MeanZ). Previously computed then DISCARDED every step.
///   `Depth` (sym/byte)         held-out ParsedSize/byte — the DEPTH read coverage is blind to (a family's WORDS
///                              cover a line's bytes, but only DEEP rules shrink its symbol count; LOWER = deeper).
///                              Depth-reads JUDGE; coverage never rewards (the metric-trap).
///   `HonestChain`              non-Goodhartable thread depth — best line-chain among distinct≥K windows of the block
///                              (raw chain is maximized by adjacent REPETITION; distinct-gating kills the trap).
///   `CollFrac`/`DfThird`       windowed collapse ALARMS over the recent drive — fraction of collapsed steps (level) and
///                              the byte-diversity decay across the window's thirds (trend; <0.7 ⟹ sliding into a repetitive basin).
///   `Js`/`LoopVerdict`         sealed-loop alarm — Jensen-Shannon divergence between consecutive generations'
///                              byte-distributions + its CONVERGE/ORBIT/COLLAPSE classifier (self entangled or sealed).
///   `MomentumVerdict`          meadow/wall/climbing band off the PER-STEP savings-slope — the controller/homeostat
///                              band. The drive's STOP (both arms) reads the stride-cadence twin instead
///                              (Reads.StrideVerdict): drive savings is stair-stepped — it moves only at grammar
///                              refreshes — so only
///                              stride-clock flatness is a real plateau there. Farm stops on THIS band (it
///                              refreshes its grammar every step, so its per-step clock IS the refresh clock).
///   `Excursion`                the self-signal — probes that left their homeostatic comfort zone this step (HomeWatch).
///   `Evicted`/`Promoted`      the MEMORY-HIERARCHY counters — literal rules DEMOTED to a tape-span reference
///                              this step (evicted from the working set under the bit budget) and demoted patterns
///                              RE-PROMOTED (re-earned their seat when they recurred). Held columns: emit 0
///                              until pass-2d mounts the GC/demotion organ (grammar = working set under budget;
///                              tape = source-of-record; demote-don't-delete via tape-span refs).
///   `ForkVolumeFrac`           THE SELF-REGULATION readout — pool coverage (ingested/pool) FROZEN at the step the
///                              dream-fork opened (first mint era); nan while the fork is still shut. Red-flags a
///                              premature fork at a glance: the schedule-gated fork read ~0.2 here; the volume-gated
///                              fork reads ~1.0 (the machine consumed its world before dreaming).
///   `MeanzDrift`               rolling slope of MeanZ over the last MeanzWin steps — the criticality-drift alarm at
///                              its ONSET (the post-fork meanz −0.78→−0.66 dilution drift shows here as a sustained
///                              positive slope long before two raw-meanz windows make it legible). ~0 = holding the
///                              critical line. NB depth is MISLEADING post-fork (rules explode memorizing the dream
///                              echo); meanz is the honest criticality signal — this column watches it.
///   `IngestDiversity`          distinct domains among the curriculum's recent real appends (ingest + mop-up + MIX)
///                              — are modalities still being eaten, or has the real diet collapsed to one domain?
public readonly record struct LossReading(
    int Step, int ViewBytes, long TapeCount, int Ingested, int Rules, int Compressed,
    long MdlSaved, double Coverage, double MaxSpan, double Concentration, int Distinct, int NovelChain, double Momentum,
    double CvZ, int Scales, double MeanZ, double Depth, int HonestChain, double CollFrac, double DfThird,
    double Js, string LoopVerdict, string MomentumVerdict, string Excursion, int Evicted = 0, int Promoted = 0,
    int Slotted = 0, long BitsSaved = 0, double ForkVolumeFrac = double.NaN, double MeanzDrift = 0, int IngestDiversity = 1, int KZ = 0,
    int ShedSpans = 0, int DroppedSpans = 0, double NodesPerByte = 0, int DNodesReplay = 0,
    int VestN0 = 0, int VestPeer = 0, double VestRate = 0, int ReplaysN0 = 0, int ReplaysPeer = 0,
    int Demoted = 0, int Births = 0, int Churn = 0, string RefactorBand = "")
{
    // `KZ` — the CvZ estimate's SAMPLE SIZE (RenormStat.KZ: how many per-scale slopes entered the CV). The
    // homeostat's k-aware whole-grammar lock (Interocept.Kz) reads it; deliberately NOT a curve.tsv column.
    // `Slotted`/`BitsSaved` — the anti-unification sparkline: paradigm slot-rules minted into the working grammar
    // by the last sleep pass (the GENERATIVE structure replacing literal surface hoards) and the word-level ΔMDL bits
    // that generalization saved (flat vs slotted, lossless over the same tape). 0 under the --no-antiunify ablation.
    // ── THE CONVERGENCE INVARIANT (phase 3 kill-line — the memory must converge, not drift) ──
    // `ShedSpans`/`DroppedSpans` — cumulative tape evacuation: with these climbing, `tape_count` (RESIDENT spans)
    //   plateaus while `view_bytes` keeps climbing — the rolling-window readout.
    // `NodesPerByte` — rules / view bytes: must FALL toward 0 (sublinear grammar growth in cumulative experience).
    // `DNodesReplay` — Δ(rule count) across the last dream/sleep cycle: ≤ 0 on re-admissionPlan of learned material
    //   (the night's global re-greed + self-compression net-REDUCE nodes; sustained positive = still learning).
    //
    // ── THE MOTION SUITE (the SQUADRON's live-evidence columns — the flagship saga's blind spot made a curve axis) ──
    // The flagship run wasted 12 heartbeats reading maxSpan (a memorization-tinged STATE byte-length) while the real
    // story — vest-rate-by-source — was invisible. These make the multi-node dynamics READ in the curve, live:
    // `VestN0`/`VestPeer` — cumulative Replay spans corroborated, SPLIT by the vested span's own source: node0
    //   (the origin voice) vs any PEER node (Worker B's fan-out tags). THE decisive signal: post-drain, node0-only
    //   must FREEZE (no other source to corroboration it — the sealed-loop control), while a live mesh keeps VestPeer
    //   climbing (a peer is the generator-independent corroboration). `VestRate` = ReflectedReplayCount/ReplayCount (the aggregate
    //   top-line — what the saga never plotted). `ReplaysN0`/`ReplaysPeer` — the by-source Replay POPULATIONS (the
    //   rate's denominators, so vest_n0/dreams_n0 reads as a rate at a glance; dreams_peer=0 is the single-node tell).
    // `Demoted` — cumulative GC demotions (literals → tape-refs under the bit budget): the REFACTOR numerator the
    //   loop computed every night then discarded (pass.Demoted never reached the curve). `Births` — Δrules UP the
    //   last night (max(0, dnodes_dream)): the ACCRETION numerator. `Churn` — the last night's structural turnover
    //   (evict+promote+demote+shed+drop): how much the memory MOVED, regardless of net. `RefactorBand` — the verdict
    //   over (births, compaction=evict+demote+drop, churn, dnodes): FROZEN (nothing moves — the degenerate tell) ·
    //   ACCRETING (births dominate — learning new structure) · REFACTORING (compaction dominates at net≤0 — re-
    //   shaping, the memory converging) · THRASH (both high, net≈0 — adding and shedding the same, the wasted-motion
    //   alarm). Held between nights like DNodesReplay (the night-scoped deltas are stale-but-legible between passes).
    public const string Header = "step\tview_bytes\ttape_count\tingested\trules\tcompressed\tmdl_saved\tcoverage\tmaxspan\tconcentration\tdistinct\tnovelchain\tmomentum\tcvz\tscales\tmeanz\tdepth\thonest_chain\tcoll_frac\tdf_third\tjs\tloop\tmom_band\texcursion\tevicted\tpromoted\tslotted\tbits_saved\tfork_vol_frac\tmeanz_drift\tingest_div\tshed\tdropped\tnodes_per_byte\tdnodes_dream\tvest_n0\tvest_peer\tvest_rate\tdreams_n0\tdreams_peer\tdemoted\tbirths\tchurn\trefactor";

    // The per-step curve-row scratch: the drive appends ONE row/step (~44 columns) for thousands of steps, so the row
    // is built into a reused builder (StringBuilder.Append($"…") streams each field through the interpolation handler
    // — zero intermediate-string allocs, where the chained-`+` interpolation minted ~5 throwaway strings per row) and
    // materialized once at the end. Single-threaded per drive (each drive owns its loop), so ONE shared scratch is
    // safe; every Row() call Clears then refills, and returns an independent string (Farm batches rows into a List).
    [ThreadStatic] private static StringBuilder? _rowScratch;

    public string Row()
    {
        var sb = _rowScratch ??= new StringBuilder(512);
        sb.Clear();
        RowInto(sb);
        return sb.ToString();
    }

    /// Emit this reading's TSV row into `sb` (byte-identical to the interpolation form — same invariant-culture
    /// formatters, same F4/F6/F0 specifiers, same field order). The nan-guarded columns append "nan" or the F-spec
    /// value directly (no intermediate F4/F6 string). Streaming form: a batching caller can build many rows into one
    /// buffer without a per-row string.
    public void RowInto(StringBuilder sb)
    {
        sb.Append(Step).Append('\t').Append(ViewBytes).Append('\t').Append(TapeCount).Append('\t').Append(Ingested).Append('\t')
          .Append(Rules).Append('\t').Append(Compressed).Append('\t').Append(MdlSaved).Append('\t')
          .Append($"{Coverage:F4}").Append('\t').Append($"{MaxSpan:F0}").Append('\t').Append($"{Concentration:F4}").Append('\t')
          .Append(Distinct).Append('\t').Append(NovelChain).Append('\t').Append($"{Momentum:F2}").Append('\t');
        AppendF4(sb, CvZ); sb.Append('\t').Append(Scales).Append('\t');
        AppendF4(sb, MeanZ); sb.Append('\t').Append($"{Depth:F4}").Append('\t').Append(HonestChain).Append('\t')
          .Append($"{CollFrac:F4}").Append('\t').Append($"{DfThird:F4}").Append('\t');
        AppendF4(sb, Js);
        sb.Append('\t').Append(LoopVerdict ?? "").Append('\t').Append(MomentumVerdict ?? "").Append('\t').Append(Excursion ?? "").Append('\t')
          .Append(Evicted).Append('\t').Append(Promoted).Append('\t').Append(Slotted).Append('\t').Append(BitsSaved).Append('\t');
        AppendF4(sb, ForkVolumeFrac); sb.Append('\t');
        AppendF6(sb, MeanzDrift); sb.Append('\t').Append(IngestDiversity).Append('\t')
          .Append(ShedSpans).Append('\t').Append(DroppedSpans).Append('\t');
        AppendF6(sb, NodesPerByte); sb.Append('\t').Append(DNodesReplay).Append('\t')
          .Append(VestN0).Append('\t').Append(VestPeer).Append('\t').Append($"{VestRate:F4}").Append('\t')
          .Append(ReplaysN0).Append('\t').Append(ReplaysPeer).Append('\t').Append(Demoted).Append('\t').Append(Births).Append('\t').Append(Churn).Append('\t')
          .Append(RefactorBand ?? "");
    }
    public string Line()
    {
        string cvz = double.IsNaN(CvZ) ? " n/a" : $"{CvZ,5:F3}", js = double.IsNaN(Js) ? " n/a" : $"{Js,5:F3}";
        // the vest-by-source telegraph rides ONLY once the mesh has minted (dreams exist) — pre-fork it is silent
        // (no dream population, no signal), so the READ line stays uncluttered during the schooling era.
        string vest = ReplaysN0 + ReplaysPeer == 0 ? ""
            : $"  vest n0 {VestN0}/{ReplaysN0} peer {VestPeer}/{ReplaysPeer}{(string.IsNullOrEmpty(RefactorBand) ? "" : "  " + RefactorBand)}";
        return $"  step {Step,4}  ingest {Ingested,4}  rules {Rules,4}  comp {Compressed,6}  Δmdl {MdlSaved,9}  cov {Coverage,6:P1}  maxSpan {MaxSpan,4:F0}  conc {Concentration,5:F2}  distinct {Distinct,4}  novelChain {NovelChain,3}  mom {Momentum,8:F1}"
             + $"  cvz {cvz}  depth {Depth,5:F3}  honest {HonestChain,3}  js {js}  {MomentumVerdict,-8} {(string.IsNullOrEmpty(LoopVerdict) ? "" : LoopVerdict)}{(string.IsNullOrEmpty(Excursion) ? "" : "  ⟨" + Excursion + "⟩")}{vest}";
    }

    /// THE REFACTOR VERDICT — the night's structural motion classified into one band, so the curve says whether the
    /// memory is GROWING, CONVERGING, THRASHING, or DEAD without the reader integrating five delta columns by eye.
    /// Reads the last night's rule births (Δrules up), its compaction (evict+demote+drop — rules/spans RETIRED), the
    /// total churn, and the net rule delta. The bands, in decision order:
    ///   FROZEN      — nothing moved this night (no births, no compaction): the degenerate control's post-drain tell
    ///                 (node0-only converges below the fork and the memory goes still — the sealed-loop signature).
    ///   THRASH      — births AND compaction both fired with net rule delta ≈ 0: the memory added and shed the same
    ///                 structure, pure wasted motion (the output-gated-input-rebuilt smell at the memory layer).
    ///   ACCRETING   — births outweigh compaction: net-new structure is entering (the healthy learning era).
    ///   REFACTORING — compaction outweighs births at net≤0: literals fold to refs / stale dreams drop / dup rules
    ///                 evict — the memory CONVERGING (the phase-3 win: view climbs, residents + rules do not).
    /// `netDelta` is dnodes_dream (night-over-night rule count); `churn` the summed turnover. Deterministic, pure.
    public static string RefactorVerdict(int births, int compaction, int churn, int netDelta)
    {
        if (churn == 0 && births == 0) return "FROZEN";
        if (births > 0 && compaction > 0 && Math.Abs(netDelta) <= Math.Max(1, churn / 8)) return "THRASH";   // added≈shed: net barely moved while churn was real
        if (births > compaction) return "ACCRETING";
        return "REFACTORING";                                                                                 // compaction dominates at net≤0 — the memory converging
    }

    private static string F4(double x) => double.IsNaN(x) ? "nan" : x.ToString("F4");
    private static string F6(double x) => double.IsNaN(x) ? "nan" : x.ToString("F6");   // the drift slope is ~1e-5/step — F4 would round the alarm to zero

    // Streaming twins of F4/F6 for the hot Row path — append "nan" or the F-spec value straight into the buffer
    // (Append($"{x:F4}") rides the interpolation handler, no intermediate string), byte-identical to F4(x)/F6(x).
    private static void AppendF4(StringBuilder sb, double x) { if (double.IsNaN(x)) sb.Append("nan"); else sb.Append($"{x:F4}"); }
    private static void AppendF6(StringBuilder sb, double x) { if (double.IsNaN(x)) sb.Append("nan"); else sb.Append($"{x:F6}"); }
}

/// A drive's per-step reader — owns the sparkline suite's rolling memory and turns one step's grammar + block
/// into a LossReading. One instance per drive; `Step` is called once per loop step, in the READ phase, by both
/// Cortex.Drive (the single-node drain loop) and Mesh.Drive (the multi-node combustion). The reads are pure
/// functions over (grammar, block, probe); the state is only the windows they smooth over.
public sealed class Reads
{
    /// THE MOTION SNAPSHOT — the whole-machine state the READ can't derive from (grammar, block, probe) alone: the
    /// tape's vest-by-source census (read straight off Tape at the call site) + the LAST NIGHT's structural deltas
    /// (threaded from the sleep pass, held between nights). Grouped so `Reads.Step` takes ONE motion argument, not
    /// nine loose primitives — and so the off-path (no mesh, no night) is a single `default` that leaves every motion
    /// column at 0/"" (the reserved-column contract: a single-node gate-off run's curve stays byte-identical).
    public readonly record struct Motion(
        int VestN0 = 0, int VestPeer = 0, int OutcomeCreditedTotal = 0, int ReplayTotal = 0, int ReplaysN0 = 0, int ReplaysPeer = 0,
        int Demoted = 0, int Births = 0, int Churn = 0)
    {
        /// The aggregate vest-rate (ReflectedReplayCount/ReplayCount) — the top-line the flagship saga never plotted. 0 when no dreams live.
        public double VestRate => ReplayTotal > 0 ? (double)OutcomeCreditedTotal / ReplayTotal : 0;
    }

    // ── the sparkline suite's per-drive memory ──
    private readonly Queue<double> _momentum = new();        // recent Δmdl for the slope + verdict-band read (per-step — the controller/homeostat/sparkline band)
    // ── the STRIDE-cadence momentum twin (the drive's STOP clock) ──  TotalSavings is STAIR-STEPPED: it can only
    //    move when the grammar refreshes (day-stride induce / night consolidate — ≈ each +12.5% of tape), so between
    //    refreshes the per-step _momentum slope reads exact-zero REGARDLESS of the underlying trend, and no tolerance
    //    can tell that stair from a plateau (the STILLBORN-DREAM bug: post-fork a fresh reading is ~10² capped
    //    mint-steps away, so the per-step wall guillotined the dream era 3-5 steps after the fork — before ONE
    //    reading landed).
    //    This window samples savings once per grammar-IDENTITY change; on ITS clock a flat slope IS a stride-over-
    //    stride plateau. Keyed separately from _cachedRules: the read-cache is transient (rebuilds post-resume to the
    //    same values), while THIS key rides the checkpoint as a seen-flag — a save can land between a night's fresh
    //    grammar and the Step that would read it, so priming from the restored grammar alone would skip that pending
    //    reading and break resume-exactness.
    private readonly Queue<double> _strideMomentum = new();
    private GrammarRule[]? _strideRules;                     // identity key — the last grammar the stride window has SEEN
    private readonly Queue<double> _collapse = new();        // rolling 8-byte-diversity window for the collapse alarms
    private readonly Queue<double> _jsTraj   = new();        // last-3 JS divergences for the sealed-loop classifier
    private readonly Queue<double> _meanz    = new();        // recent MeanZ for the criticality-drift slope (meanz_drift — the dilution alarm at onset)
    private int[]? _prevHist;                                // previous block's byte-histogram (JS between consecutive generations) — always one of the two ping-pong buffers below (or null pre-step-1)
    private int[]? _histScratchA, _histScratchB;                           // the double-buffered byte-histogram scratch — ping-ponged each step so `hist` and `_prevHist` are two live arrays with no per-step alloc
    // ── per-step block-read scratch (the reads run once/step; these reused sets/lists kill the per-step `new`) ──
    private HashSet<ulong>? _distinctScratch;                    // DistinctBlocks' 8-byte-key set
    private readonly List<ReadLine> _tokenLines = new();
    private readonly List<IdentifierToken> _tokenIDs = new();
    private readonly HashSet<ByteSlice> _novelRunLines = new();
    private readonly List<int> _honestLineScratch = new();
    private readonly HashSet<ByteSlice> _honestWindowScratch = new();
    private readonly HomeWatch _home = new("zcsd");          // the self-signal probes: z=CvZ · c=Coverage · s=MaxSpan · d=Depth
    private readonly List<string> _excursions = new();       // the per-step excursion stream (the self-model's food)
    private int _excursionCheckpointCursor;
    private long _excursionBaseCount;
    private TextWriter? _excursionSink;
    private bool _legacyWireFormat;
    private bool _strideRulesSeenPending;

    // ── the grammar-derived read cache (O(Δ): coverage/depth/renorm/concentration are pure functions of the grammar
    //    (+ the drive-constant probe), so between the drive's stride-gated re-inductions they are CONSTANT — but the
    //    old code rebuilt a whole GrammarCover (expanding EVERY rule) + re-ran RenormStats + ConcentrationOf EVERY
    //    step. Keyed on the Rules-array identity (the same reference until re-induce/sleep hands a fresh grammar);
    //    only the BLOCK-derived reads (distinct/novelChain/honest/collapse/JS/momentum) recompute per step. ──
    private GrammarRule[]? _cachedRules;
    private GrammarShape? _cachedShape;
    private GrammarRevisionID _cachedShapeRevision;
    private double _cCoverage, _cDepth, _cCvZ, _cMeanZ, _cMaxSpan, _cConcentration;
    private int _cScales, _cKz;

    public long BestMdl { get; private set; } = long.MinValue;
    public int  BestStep { get; private set; } = -1;
    /// The momentum window is full (the WALL stop condition may fire) — the drive's momentum-termination debounce.
    public bool MomentumWindowFull => _momentum.Count >= MomentumWin;
    /// A fresh stride reading landed THIS step (the grammar's identity changed since the last Step) — the drive's
    /// wall gate advances its streak only on these steps; between them the savings signal CANNOT move, so the
    /// verdict is not evidence either way and the streak just holds.
    public bool StrideReadingLanded { get; private set; }
    /// The stride-cadence momentum verdict — meadow/wall/climbing over the last MomentumWin STRIDE readings
    /// (fresh-grammar savings samples, not steps). Meaningful on StrideReadingLanded steps; holds between them.
    public string StrideVerdict { get; private set; } = "";
    /// The stride window is full — the stride-cadence WALL may fire (MomentumWindowFull's twin, on the stride clock).
    public bool StrideWindowFull => _strideMomentum.Count >= MomentumWin;
    /// The excursion stream (step\ttoken lines) — WAVE 1's self-model induces the meta-grammar over it.
    public IReadOnlyList<string> Excursions => _excursions;

    /// Mount the durable excursion stream. Once mounted, committed rows are
    /// released from the in-memory readout; the file is the history authority.
    internal void MountExcursionSink(TextWriter sink) => _excursionSink = sink;

    internal void FlushCheckpointOutput()
    {
        if (_excursionSink is StreamWriter writer)
        {
            writer.Flush();
            if (writer.BaseStream is FileStream file)
                file.Flush(flushToDisk: true);
            else
                writer.BaseStream.Flush();
            return;
        }
        _excursionSink?.Flush();
    }

    internal long ExcursionCount => checked(_excursionBaseCount + _excursions.Count);

    /// Prepare the persistent excursion artifact at the checkpoint horizon.
    /// Resume drops rows emitted after the last committed cursor before the
    /// append sink is reopened; fresh runs start with the stable header.
    internal void PrepareExcursionLog(Run run, bool fresh)
    {
        string path = run.PathOf("excursions.txt");
        if (fresh)
        {
            run.Write("excursions.txt", "step\ttoken\n");
            return;
        }
        long keepRows = ExcursionCount;
        if (!File.Exists(path))
        {
            if (keepRows != 0) throw new InvalidDataException("checkpoint carries excursions but excursions.txt is missing");
            run.Write("excursions.txt", "step\ttoken\n");
            return;
        }
        run.WriteAtomic("excursions.txt", output =>
        {
            using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16_384, leaveOpen: true);
            string? header = reader.ReadLine();
            if (header is null || !string.Equals(header, "step\ttoken", StringComparison.Ordinal))
                throw new InvalidDataException("excursions.txt header is malformed");
            writer.WriteLine(header);
            for (long i = 0; i < keepRows; i++)
            {
                string? line = reader.ReadLine();
                if (line is null) throw new InvalidDataException("excursions.txt is shorter than the checkpoint cursor");
                writer.WriteLine(line);
            }
        });
    }

    internal ReadsCheckpointDelta CaptureCheckpointDelta()
    {
        ValidateExcursionCursor(_excursionCheckpointCursor, _excursions.Count);
        return new(checked(_excursionBaseCount + _excursionCheckpointCursor), _excursions.Count == _excursionCheckpointCursor
            ? Array.Empty<string>()
            : _excursions.GetRange(_excursionCheckpointCursor, _excursions.Count - _excursionCheckpointCursor).ToArray(),
            CaptureRollingWindowDelta());
    }

    internal void CommitCheckpointDelta()
    {
        if (_excursionSink is null)
        {
            _excursionCheckpointCursor = _excursions.Count;
            return;
        }
        _excursionBaseCount = checked(_excursionBaseCount + _excursions.Count);
        if (_excursions.Count > 0)
            _legacyWireFormat = false;
        _excursions.Clear();
        _excursionCheckpointCursor = 0;
    }

    internal void ApplyCheckpointDelta(in ReadsCheckpointDelta delta)
    {
        if (delta.Cursor < _excursionBaseCount)
            throw new InvalidDataException($"reads excursion checkpoint cursor regressed: expected at least {_excursionBaseCount}, got {delta.Cursor}");
        ValidateExcursionCursor(delta.Cursor - _excursionBaseCount, _excursions.Count);
        if (delta.Excursions is null)
            throw new InvalidDataException("reads checkpoint delta has no excursion rows");
        if (delta.Excursions.Length > MaxExcursions)
            throw new InvalidDataException($"reads checkpoint delta exceeds {MaxExcursions} excursion rows");
        long expectedCursor = checked(_excursionBaseCount + _excursions.Count);
        if (delta.Cursor != expectedCursor)
            throw new InvalidDataException($"reads excursion checkpoint cursor gap: expected {expectedCursor}, got {delta.Cursor}");
        if (delta.Rolling.HasState) ValidateRollingWindowDelta(delta.Rolling);
        if (delta.Excursions.Any(static excursion => excursion is null))
            throw new InvalidDataException("reads checkpoint delta contains a null excursion");
        _excursions.AddRange(delta.Excursions);
        _excursionCheckpointCursor = _excursions.Count;
        if (delta.Rolling.HasState) ApplyRollingWindowDelta(delta.Rolling);
    }

    internal void ApplyCheckpointDelta(in ReadsCheckpointDelta delta, in RePairResult g)
    {
        ApplyCheckpointDelta(delta);
        if (delta.Rolling.HasState && delta.Rolling.StrideRulesSeen)
        {
            _strideRules = g.Rules;
            _strideRulesSeenPending = false;
        }
    }

    /// Apply a rolling receipt while retaining the grammar identity anchor.  A
    /// one-argument apply stores the seen-flag as pending because references do
    /// not survive serialization; the first subsequent Step re-anchors it to
    /// that step's live grammar before deciding whether a stride landed.
    internal void ApplyRollingWindowDelta(in ReadsRollingWindowDelta delta)
    {
        if (!delta.HasState) return;
        ValidateRollingWindowDelta(delta);
        RestoreQueue(_momentum, delta.Momentum, MomentumWin, "momentum");
        RestoreQueue(_strideMomentum, delta.StrideMomentum, MomentumWin, "stride momentum");
        RestoreQueue(_collapse, delta.Collapse, CollapseWin, "collapse");
        RestoreQueue(_jsTraj, delta.JsTrajectory, 3, "JS trajectory");
        RestoreQueue(_meanz, delta.Meanz, MeanzWin, "MeanZ");
        if (delta.HasPreviousHistogram)
        {
            if (delta.PreviousHistogram is null || delta.PreviousHistogram.Length != 256)
                throw new InvalidDataException("reads rolling delta histogram must contain 256 bins");
            _histScratchA = (int[])delta.PreviousHistogram.Clone();
            _histScratchB = null;
            _prevHist = _histScratchA;
        }
        else
        {
            _histScratchA = _histScratchB = null;
            _prevHist = null;
        }
        _strideRules = null;
        _strideRulesSeenPending = delta.StrideRulesSeen;
        StrideReadingLanded = false;
        StrideVerdict = delta.StrideVerdict ?? "";
        _cachedRules = null;
    }

    internal ReadsRollingWindowDelta CaptureRollingWindowDelta()
        => new(
            _momentum.ToArray(), _strideMomentum.ToArray(), _strideRules is not null || _strideRulesSeenPending,
            _collapse.ToArray(), _jsTraj.ToArray(), _meanz.ToArray(),
            _prevHist is not null, _prevHist is null ? Array.Empty<int>() : (int[])_prevHist.Clone(), StrideVerdict);

    internal static void WriteCheckpointDelta(CkptWriter writer, in ReadsCheckpointDelta delta)
    {
        if (delta.Cursor < 0 || delta.Excursions is null || delta.Excursions.Length > MaxExcursions)
            throw new InvalidDataException("reads checkpoint delta cursor or excursion count is malformed");
        writer.U8(2);
        writer.I64(delta.Cursor);
        writer.I32(delta.Excursions.Length);
        foreach (string excursion in delta.Excursions)
        {
            if (excursion is null) throw new InvalidDataException("reads checkpoint delta contains a null excursion");
            writer.Str(excursion);
        }
        writer.Bool(delta.Rolling.HasState);
        if (!delta.Rolling.HasState) return;

        WriteQueue(writer, delta.Rolling.Momentum, MomentumWin, "momentum");
        WriteQueue(writer, delta.Rolling.StrideMomentum, MomentumWin, "stride momentum");
        writer.Bool(delta.Rolling.StrideRulesSeen);
        WriteQueue(writer, delta.Rolling.Collapse, CollapseWin, "collapse");
        WriteQueue(writer, delta.Rolling.JsTrajectory, 3, "JS trajectory");
        WriteQueue(writer, delta.Rolling.Meanz, MeanzWin, "MeanZ");
        writer.Bool(delta.Rolling.HasPreviousHistogram);
        if (delta.Rolling.HasPreviousHistogram)
        {
            if (delta.Rolling.PreviousHistogram is null || delta.Rolling.PreviousHistogram.Length != 256)
                throw new InvalidDataException("reads rolling delta histogram must contain 256 bins");
            foreach (int value in delta.Rolling.PreviousHistogram) writer.I32(value);
        }
        writer.Str(delta.Rolling.StrideVerdict ?? "");
    }

    internal static ReadsCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte version = reader.U8();
        if (version is not (1 or 2)) throw new InvalidDataException("unknown reads checkpoint delta version");
        long cursor = version == 1 ? reader.I32() : reader.I64();
        int count = reader.I32();
        if (cursor < 0 || count < 0 || count > MaxExcursions)
            throw new InvalidDataException("reads checkpoint delta cursor or excursion count is malformed");
        string[] excursions = new string[count];
        for (int i = 0; i < count; i++) excursions[i] = reader.Str();
        if (!reader.Bool()) return new(cursor, excursions);

        double[] momentum = ReadQueue(reader, MomentumWin, "momentum");
        double[] stride = ReadQueue(reader, MomentumWin, "stride momentum");
        bool strideSeen = reader.Bool();
        double[] collapse = ReadQueue(reader, CollapseWin, "collapse");
        double[] js = ReadQueue(reader, 3, "JS trajectory");
        double[] meanz = ReadQueue(reader, MeanzWin, "MeanZ");
        bool hasHistogram = reader.Bool();
        int[] histogram = hasHistogram ? ReadHistogram(reader) : Array.Empty<int>();
        return new(cursor, excursions, new(momentum, stride, strideSeen, collapse, js, meanz,
            hasHistogram, histogram, reader.Str()));
    }

    /// READ · the sparkline suite. ONE GrammarCover for BOTH held-out reads (coverage = breadth, ParsedSize/byte =
    /// depth — the metric-trap's two poles), ONE RenormStats destructured for the whole grok read (CvZ + Scales +
    /// MeanZ + MaxSpan, no longer discarding all but MaxSpan), the block reads (distinct / novelChain / honestChain /
    /// collapse / JS), and the momentum band. Folds the excursion into the stream and tracks the best Δmdl step.
    /// `probe` is the held-out text when present, else the corpus (the generalization probe).
    public LossReading Step(int step, int viewBytes, long tapeCount, int ingested, RePairResult g, byte[] block, byte[] probe, double wallTol, int evicted = 0, int promoted = 0, int slotted = 0, long bitsSaved = 0, double forkVolumeFrac = double.NaN, int ingestDiversity = 1, int shedSpans = 0, int droppedSpans = 0, int dNodesReplay = 0,
        in Motion motion = default, GrammarShape? sharedShape = null)
        => Step(step, viewBytes, tapeCount, ingested, g, block.AsMemory(), probe, wallTol, evicted, promoted, slotted, bitsSaved, forkVolumeFrac, ingestDiversity, shedSpans, droppedSpans, dNodesReplay, in motion, sharedShape);

    public LossReading Step(int step, int viewBytes, long tapeCount, int ingested, RePairResult g, ReadOnlyMemory<byte> block, byte[] probe, double wallTol, int evicted = 0, int promoted = 0, int slotted = 0, long bitsSaved = 0, double forkVolumeFrac = double.NaN, int ingestDiversity = 1, int shedSpans = 0, int droppedSpans = 0, int dNodesReplay = 0,
        in Motion motion = default, GrammarShape? sharedShape = null)
    {
        // grammar-derived reads (coverage=breadth + ParsedSize/byte=depth on the held-out probe, the grok bell, the
        // Gini) — rebuilt only when the grammar's Rules identity changes (re-induce / sleep); reused between strides.
        bool shapeChanged = sharedShape is not null &&
            (!ReferenceEquals(sharedShape, _cachedShape) || sharedShape.Revision != _cachedShapeRevision);
        if (shapeChanged || (sharedShape is null && !ReferenceEquals(g.Rules, _cachedRules)))
        {
            _cachedShape = sharedShape ?? GrammarShape.BuildFromResult(in g);
            _cCoverage = _cachedShape.Sequence.ComputeCoverage(probe);
            _cDepth = _cachedShape.Sequence.ComputeParsedSizePerByte(probe);                          // sym/byte — LOWER = deeper
            var renorm = _cachedShape.ReadRenorm();
            (_cScales, _cMeanZ, _cCvZ, _cMaxSpan, _cKz) = (renorm.Scales, renorm.MeanZ, renorm.CvZ, renorm.MaxSpan, renorm.KZ);
            _cConcentration = _cachedShape.Concentration;
            _cachedRules = g.Rules;
            _cachedShapeRevision = _cachedShape.Revision;
        }
        double coverage = _cCoverage, depth = _cDepth, cvZ = _cCvZ, meanZ = _cMeanZ, maxSpan = _cMaxSpan;
        int scales = _cScales;

        ReadOnlySpan<byte> blockSpan = block.Span;
        TokenizeBlock(block);
        int distinct = DistinctBlocks(blockSpan), novel = NovelChain();
        int honestChain = HonestChain();                                   // non-Goodhartable line-thread depth (dormant on newline-sparse generation)
        int nBlocks = blockSpan.Length / 8;
        double divRatio = nBlocks > 0 ? (double)distinct / nBlocks : 1.0;  // distinct 8-byte blocks ÷ total — the collapse-robust diversity
        _collapse.Enqueue(divRatio);
        while (_collapse.Count > CollapseWin) _collapse.Dequeue();
        var (collFrac, dfThird) = CollapseAlarms(_collapse);               // windowed collapse alarms — level + trend

        // ping-pong the two byte-histogram buffers: fill the SPARE (the one _prevHist isn't holding), diff against
        // prev, then _prevHist adopts it — two live 256-int arrays, zero per-step `new int[256]` (the JS diff needs
        // BOTH this block's and the previous block's histogram alive at once, so it's a pair, not a single scratch).
        int[] hist = FillHist(_prevHist == _histScratchA ? (_histScratchB ??= new int[256]) : (_histScratchA ??= new int[256]), blockSpan);
        double js = _prevHist is null ? double.NaN : JsDivergence(_prevHist, hist);   // sealed-loop alarm: JS between consecutive generations
        _prevHist = hist;
        if (!double.IsNaN(js)) { _jsTraj.Enqueue(js); while (_jsTraj.Count > 3) _jsTraj.Dequeue(); }
        string loopVerdict = LoopClassify(_jsTraj);

        _momentum.Enqueue(g.TotalSavings.Value);
        while (_momentum.Count > MomentumWin) _momentum.Dequeue();
        string momBand = MomentumVerdict(_momentum, wallTol);              // meadow/wall/climbing — the per-step band (controller/homeostat/sparkline)

        // the stride-cadence twin: ONE savings sample per grammar refresh — the only clock on which a flat slope
        // means a real plateau (the field's law above). The drive's STOP gate reads THESE, never momBand.
        if (_strideRulesSeenPending)
        {
            // A delta can carry the stride seen-flag but not the reference it
            // named.  Re-anchor once on the live grammar before this step's
            // identity comparison, preserving the straight-through cadence.
            _strideRules = g.Rules;
            _strideRulesSeenPending = false;
        }
        StrideReadingLanded = !ReferenceEquals(g.Rules, _strideRules);
        if (StrideReadingLanded)
        {
            _strideRules = g.Rules;
            _strideMomentum.Enqueue(g.TotalSavings.Value);
            while (_strideMomentum.Count > MomentumWin) _strideMomentum.Dequeue();
            StrideVerdict = MomentumVerdict(_strideMomentum, wallTol);
        }

        // the criticality-drift window: MeanZ is stair-stepped (cached between strides), so the slope reads over a
        // window long enough to span several grammar refreshes — a sustained positive slope IS the dilution drift's
        // onset. NaN reads (a too-shallow grammar) are skipped rather than poisoning the window.
        if (!double.IsNaN(meanZ)) { _meanz.Enqueue(meanZ); while (_meanz.Count > MeanzWin) _meanz.Dequeue(); }
        double meanzDrift = Slope(_meanz);

        Span<double> homeValues = stackalloc double[4] { cvZ, coverage, maxSpan, depth };
        string excursion = _home.Observe(homeValues);                       // the self-signal: which probes left home this step
        string excursionRow = $"{step}\t{excursion}";
        _excursions.Add(excursionRow);
        if (_excursionSink is not null)
            _excursionSink.WriteLine(excursionRow);

        // the refactor verdict over the last night's motion (compaction = structure RETIRED: evicts+demotes+drops;
        // shed is NOT compaction — a shed span stays in the view, only its RAM moved). "" until a night has run.
        int compaction = evicted + motion.Demoted + droppedSpans;
        string refactorBand = motion.Churn == 0 && motion.Births == 0 && dNodesReplay == 0 && shedSpans == 0
            ? "" : LossReading.RefactorVerdict(motion.Births, compaction, motion.Churn, dNodesReplay);

        var read = new LossReading(step, viewBytes, tapeCount, ingested, g.Rules.Length, g.Compressed?.Length ?? 0,
            g.TotalSavings.Value, coverage, maxSpan, _cConcentration, distinct, novel, Slope(_momentum),
            cvZ, scales, meanZ, depth, honestChain, collFrac, dfThird, js, loopVerdict, momBand, excursion, evicted, promoted, slotted, bitsSaved, forkVolumeFrac, meanzDrift, ingestDiversity, _cKz,
            shedSpans, droppedSpans, viewBytes > 0 ? (double)g.Rules.Length / viewBytes : 0, dNodesReplay,
            motion.VestN0, motion.VestPeer, motion.VestRate, motion.ReplaysN0, motion.ReplaysPeer, motion.Demoted, motion.Births, motion.Churn, refactorBand);
        if (read.MdlSaved > BestMdl) { BestMdl = read.MdlSaved; BestStep = step; }
        return read;
    }

    /// CHECKPOINT — the reader's whole rolling memory: the rolling windows, the previous byte-histogram, the
    /// HomeWatch baselines, the excursion stream, the best-Δmdl marker. The grammar-derived cache (_cached*) is
    /// deliberately NOT stored — it is identity-keyed on the Rules array and rebuilds to the same values from the
    /// restored grammar on the first post-resume Step. The stride window's identity key CANNOT rebuild that way
    /// (references don't serialize, and "rebuild" would re-land a reading the live run already consumed), so it
    /// rides as a seen-flag against the LIVE grammar: `g` here must be the same instance the drive carries.
    public void Save(CkptWriter w, in RePairResult g)
    {
        ValidateQueue(_momentum, MomentumWin, "momentum");
        ValidateQueue(_strideMomentum, MomentumWin, "stride momentum");
        ValidateQueue(_collapse, CollapseWin, "collapse");
        ValidateQueue(_jsTraj, 3, "JS trajectory");
        ValidateQueue(_meanz, MeanzWin, "MeanZ");
        if (_excursions.Count > MaxExcursions) throw new InvalidDataException($"reads contains more than {MaxExcursions} retained excursions");
        Checkpoint.WriteQueue(w, _momentum);
        Checkpoint.WriteQueue(w, _strideMomentum);
        w.Bool(_strideRulesSeenPending || ReferenceEquals(_strideRules, g.Rules));   // seen-flag — false ⇒ a fresh grammar landed after the last Step (a post-sleep save): the resumed run must land that pending stride reading exactly like the straight-through run
        Checkpoint.WriteQueue(w, _collapse);
        Checkpoint.WriteQueue(w, _jsTraj);
        Checkpoint.WriteQueue(w, _meanz);
        w.Bool(_prevHist is not null);
        if (_prevHist is not null) foreach (var h in _prevHist) w.I32(h);
        _home.Save(w);
        if (_legacyWireFormat && _excursionBaseCount == 0)
        {
            // Preserve a historical image's exact section bytes until a
            // retention commit actually creates an external base horizon.
            w.I32(_excursions.Count);
        }
        else
        {
            w.I32(-1); // version marker; legacy images begin this section with the excursion count (non-negative)
            w.I64(_excursionBaseCount);
            w.I32(_excursions.Count);
        }
        foreach (var e in _excursions) w.Str(e);
        w.I64(BestMdl); w.I32(BestStep);
    }

    public void Load(CkptReader r, in RePairResult g)
    {
        ReadQueue(r, _momentum, MomentumWin, "momentum");
        ReadQueue(r, _strideMomentum, MomentumWin, "stride momentum");
        _strideRules = r.Bool() ? g.Rules : null;         // seen ⇒ re-anchor on the RESTORED instance; unseen ⇒ the first post-resume Step lands the pending reading
        ReadQueue(r, _collapse, CollapseWin, "collapse");
        ReadQueue(r, _jsTraj, 3, "JS trajectory");
        ReadQueue(r, _meanz, MeanzWin, "MeanZ");
        if (r.Bool()) { _histScratchA = new int[256]; for (int i = 0; i < 256; i++) _histScratchA[i] = r.I32(); _prevHist = _histScratchA; }   // restore into buffer A; the next Step fills B as the spare
        else _prevHist = null;
        _home.Load(r);
        _excursions.Clear();
        int marker = r.I32();
        if (marker == -1)
        {
            _legacyWireFormat = false;
            _excursionBaseCount = r.I64();
            if (_excursionBaseCount < 0)
                throw new InvalidDataException($"reads checkpoint base excursion count {_excursionBaseCount} is invalid");
        }
        else
        {
            // Legacy CORTEXT images had no retention marker: `marker` is the first
            // field of the old excursion section (the base count, which was zero).
            if (marker < 0) throw new InvalidDataException("reads checkpoint excursion section marker is malformed");
            _legacyWireFormat = true;
            _excursionBaseCount = 0;
            int nLegacy = marker;
            if (nLegacy > MaxExcursions)
                throw new InvalidDataException($"reads checkpoint contains {nLegacy} excursion rows; maximum is {MaxExcursions}");
            for (int i = 0; i < nLegacy; i++) _excursions.Add(r.Str());
            _excursionCheckpointCursor = _excursions.Count;
            BestMdl = r.I64(); BestStep = r.I32();
            _strideRulesSeenPending = false;
            StrideReadingLanded = false;
            StrideVerdict = "";
            _cachedRules = null;
            return;
        }
        int n = r.I32();
        if (n < 0 || n > MaxExcursions)
            throw new InvalidDataException($"reads checkpoint contains {n} retained excursion rows; maximum is {MaxExcursions}");
        for (int i = 0; i < n; i++) _excursions.Add(r.Str());
        _excursionCheckpointCursor = _excursions.Count;
        BestMdl = r.I64(); BestStep = r.I32();
        _strideRulesSeenPending = false;
        StrideReadingLanded = false;
        StrideVerdict = "";
        _cachedRules = null;                                              // force the pure-function cache rebuild off the restored grammar
    }

    // The durable artifact is unbounded history; only an in-memory delta/tail is bounded.
    private const int MaxExcursions = 1_000_000;

    private static void ValidateQueue(Queue<double> queue, int max, string name)
    {
        if (queue.Count > max) throw new InvalidDataException($"reads {name} window exceeds {max} values");
    }

    private static void WriteQueue(CkptWriter writer, double[] values, int max, string name)
    {
        if (values is null || values.Length > max)
            throw new InvalidDataException($"reads rolling delta {name} window exceeds {max} values");
        writer.I32(values.Length);
        foreach (double value in values) writer.F64(value);
    }

    private static double[] ReadQueue(CkptReader reader, int max, string name)
    {
        int count = reader.I32();
        if (count < 0 || count > max)
            throw new InvalidDataException($"reads rolling delta {name} window has {count} values; maximum is {max}");
        double[] values = new double[count];
        for (int i = 0; i < count; i++) values[i] = reader.F64();
        return values;
    }

    private static int[] ReadHistogram(CkptReader reader)
    {
        int[] values = new int[256];
        for (int i = 0; i < values.Length; i++) values[i] = reader.I32();
        return values;
    }

    private static void ReadQueue(CkptReader reader, Queue<double> target, int max, string name)
    {
        int count = reader.I32();
        if (count < 0 || count > max)
            throw new InvalidDataException($"reads checkpoint {name} window has {count} values; maximum is {max}");
        target.Clear();
        for (int i = 0; i < count; i++) target.Enqueue(reader.F64());
    }

    private static void ValidateExcursionCursor(long cursor, long count)
    {
        if (cursor < 0 || cursor > count)
            throw new InvalidDataException($"reads excursion checkpoint cursor {cursor} is outside {count} rows");
    }

    private static void RestoreQueue(Queue<double> target, double[] values, int max, string name)
    {
        if (values is null || values.Length > max)
            throw new InvalidDataException($"reads rolling delta {name} window exceeds {max} values");
        target.Clear();
        foreach (double value in values) target.Enqueue(value);
    }

    private static void ValidateRollingWindowDelta(in ReadsRollingWindowDelta delta)
    {
        if (!delta.HasState) return;
        if (delta.Momentum is null || delta.Momentum.Length > MomentumWin
            || delta.StrideMomentum is null || delta.StrideMomentum.Length > MomentumWin
            || delta.Collapse is null || delta.Collapse.Length > CollapseWin
            || delta.JsTrajectory is null || delta.JsTrajectory.Length > 3
            || delta.Meanz is null || delta.Meanz.Length > MeanzWin)
            throw new InvalidDataException("reads rolling delta contains an oversized window");
        if (delta.HasPreviousHistogram && (delta.PreviousHistogram is null || delta.PreviousHistogram.Length != 256))
            throw new InvalidDataException("reads rolling delta histogram must contain 256 bins");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE SPARKLINE SUITE — the ported reads (each column's meaning lives on LossReading; the ports on their source)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    public const int MomentumWin = 12;      // recent steps the savings-slope + verdict-band read over
    public const int CollapseWin = 24;      // recent steps the collapse alarms (coll_frac / df_third) window over
    public const int MeanzWin = 24;         // recent steps the criticality-drift slope (meanz_drift) reads over — long enough to span several stride-stale grammar refreshes
    public const int WallLock = 3;          // consecutive WALL verdicts before the drive halts (momentum-termination debounce)
    private const double CollapseRatio = 0.35; // a block whose distinct-8-byte-blocks ÷ total falls below this is in the repetition basin — the drive.py distinct<6 collapse flag, at the byte granularity our newline-sparse generation actually emits (line-distinctness degenerates to 1)
    private const int HonestK = 20;            // a block-window must hold ≥ this many distinct lines to count as an honest (non-repetition) thread

    /// Least-squares slope of a small window of recent values — the MOMENTUM read (index vs value). 0 for <2 points.
    /// The one authority for the drift/momentum slope: Reads runs it per-step (meanz, savings, momentum); the solve
    /// mind's SenseCriticality shares it at the instance timescale (AgentSolve.SenseCriticality).
    internal static double Slope(Queue<double> ys)
    {
        int n = ys.Count;
        if (n < 2) return 0;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        int i = 0;
        foreach (double y in ys) { sx += i; sy += y; sxx += (double)i * i; sxy += (double)i * y; i++; }   // struct-enumerate the Queue in order — no ToArray alloc (called 3×/step)
        double det = n * sxx - sx * sx;
        return det == 0 ? 0 : (n * sxy - sx * sy) / det;
    }

    /// Distinct non-overlapping 8-byte blocks — the collapse-robust volume read (repetition ⇒ few distinct blocks).
    /// The keystone guard: chain/coherence measures are Goodhartable by repetition, only DISTINCT is not.
    /// Reuses `_distinctScratch` (cleared per call) — the drive calls this once/step, so one scratch set kills the alloc.
    private int DistinctBlocks(ReadOnlySpan<byte> b, int w = 8)
    {
        if (b.Length < w) return b.Length == 0 ? 0 : 1;
        var set = _distinctScratch ??= new HashSet<ulong>();
        set.Clear();
        for (int i = 0; i + w <= b.Length; i += w)
        {
            ulong k = 0;
            for (int j = 0; j < w; j++) k = (k << 8) | b[i + j];
            set.Add(k);
        }
        return set.Count;
    }

    private readonly struct ByteSlice : IEquatable<ByteSlice>
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly int _offset, _length;

        public ByteSlice(ReadOnlyMemory<byte> bytes, int offset, int length) { _bytes = bytes; _offset = offset; _length = length; }
        public ReadOnlySpan<byte> Span => _bytes.Span.Slice(_offset, _length);
        public bool Equals(ByteSlice other) => _length == other._length && Span.SequenceEqual(other.Span);
        public override bool Equals(object? obj) => obj is ByteSlice other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (byte value in Span) hash = (hash ^ value) * 16777619;
                return (int)hash;
            }
        }
    }

    private readonly struct ReadLine
    {
        public readonly ByteSlice Raw, Trimmed;
        public readonly int IDStart, IDCount;
        public ReadLine(ByteSlice raw, ByteSlice trimmed, int idStart, int idCount)
        {
            Raw = raw; Trimmed = trimmed; IDStart = idStart; IDCount = idCount;
        }
    }

    private readonly struct IdentifierToken
    {
        public readonly ByteSlice Value;
        public readonly int TextLength;
        public IdentifierToken(ByteSlice value, int textLength) { Value = value; TextLength = textLength; }
    }

    private void TokenizeBlock(ReadOnlyMemory<byte> block)
    {
        _tokenLines.Clear();
        _tokenIDs.Clear();
        ReadOnlySpan<byte> bytes = block.Span;
        int start = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            if (i > start) AddTokenLine(block, start, i - start);
            start = i + 1;
        }
        if (start < bytes.Length) AddTokenLine(block, start, bytes.Length - start);
    }

    private void AddTokenLine(ReadOnlyMemory<byte> block, int start, int length)
    {
        int trimStart = start, trimEnd = start + length;
        while (trimStart < trimEnd && IsWhitespace(block, trimStart, trimEnd, out int width)) trimStart += width;
        while (trimEnd > trimStart && IsTrailingWhitespace(block, trimEnd, start, out int width)) trimEnd -= width;
        int idStart = _tokenIDs.Count;
        ScanIdentifiers(block, start, start + length);
        _tokenLines.Add(new ReadLine(new ByteSlice(block, start, length), new ByteSlice(block, trimStart, trimEnd - trimStart), idStart, _tokenIDs.Count - idStart));
    }

    private static bool IsWhitespace(ReadOnlyMemory<byte> bytes, int offset, int end, out int width)
    {
        DecodeRune(bytes, offset, end, out Rune rune, out width);
        return Rune.IsWhiteSpace(rune);
    }

    private static bool IsTrailingWhitespace(ReadOnlyMemory<byte> bytes, int end, int start, out int width)
    {
        int offset = end - 1;
        ReadOnlySpan<byte> span = bytes.Span;
        while (offset > start && (span[offset] & 0xC0) == 0x80) offset--;
        DecodeRune(bytes, offset, end, out Rune rune, out width);
        return Rune.IsWhiteSpace(rune);
    }

    private static void DecodeRune(ReadOnlyMemory<byte> bytes, int offset, int end, out Rune rune, out int width)
    {
        OperationStatus status = Rune.DecodeFromUtf8(bytes.Span.Slice(offset, end - offset), out rune, out width);
        if (status != OperationStatus.Done) { rune = Rune.ReplacementChar; width = 1; }
    }

    private void ScanIdentifiers(ReadOnlyMemory<byte> bytes, int start, int end)
    {
        int i = start;
        while (i < end)
        {
            DecodeRune(bytes, i, end, out Rune rune, out int width);
            if (Rune.IsLetter(rune) || rune.Value == '_')
            {
                int tokenStart = i, utf16Length = rune.Value > 0xFFFF ? 2 : 1;
                i += width;
                while (i < end)
                {
                    DecodeRune(bytes, i, end, out Rune next, out int nextWidth);
                    if (!Rune.IsLetterOrDigit(next) && next.Value != '_') break;
                    utf16Length += next.Value > 0xFFFF ? 2 : 1;
                    i += nextWidth;
                }
                _tokenIDs.Add(new IdentifierToken(new ByteSlice(bytes, tokenStart, i - tokenStart), utf16Length));
            }
            else i += width;
        }
    }

    /// The DEPTH read — longest run of consecutive DISTINCT generated lines each sharing a ≥3-char identifier
    /// with the previous. The tokenizer is shared with HonestChain so the block is decoded and split once.
    private int NovelChain()
    {
        _novelRunLines.Clear();
        int best = 0, run = 0, previousIDStart = 0, previousIDCount = 0;
        bool havePrevious = false;
        for (int lineIndex = 0; lineIndex < _tokenLines.Count; lineIndex++)
        {
            ReadLine line = _tokenLines[lineIndex];
            bool shared = havePrevious && IdentifiersOverlap(line, previousIDStart, previousIDCount, contentOnly: false);
            if (shared && !_novelRunLines.Contains(line.Raw)) { run++; _novelRunLines.Add(line.Raw); }
            else { run = 1; _novelRunLines.Clear(); _novelRunLines.Add(line.Raw); }
            if (run > best) best = run;
            previousIDStart = line.IDStart; previousIDCount = line.IDCount; havePrevious = true;
        }
        return best;
    }

    /// C-family / prose keywords stripped from content-ids so the thread measure tracks DATA-FLOW units.
    private static bool IsContentID(IdentifierToken token)
    {
        ReadOnlySpan<byte> value = token.Value.Span;
        return !(value.SequenceEqual("S"u8) || value.SequenceEqual("C"u8) || IsKeyword(value));
    }

    private static bool IsKeyword(ReadOnlySpan<byte> value) =>
        value.SequenceEqual("if"u8) || value.SequenceEqual("else"u8) || value.SequenceEqual("for"u8) || value.SequenceEqual("while"u8) ||
        value.SequenceEqual("return"u8) || value.SequenceEqual("break"u8) || value.SequenceEqual("continue"u8) || value.SequenceEqual("switch"u8) ||
        value.SequenceEqual("case"u8) || value.SequenceEqual("default"u8) || value.SequenceEqual("do"u8) || value.SequenceEqual("goto"u8) ||
        value.SequenceEqual("try"u8) || value.SequenceEqual("catch"u8) || value.SequenceEqual("finally"u8) || value.SequenceEqual("throw"u8) ||
        value.SequenceEqual("new"u8) || value.SequenceEqual("public"u8) || value.SequenceEqual("private"u8) || value.SequenceEqual("protected"u8) ||
        value.SequenceEqual("internal"u8) || value.SequenceEqual("static"u8) || value.SequenceEqual("readonly"u8) || value.SequenceEqual("const"u8) ||
        value.SequenceEqual("void"u8) || value.SequenceEqual("var"u8) || value.SequenceEqual("let"u8) || value.SequenceEqual("mut"u8) ||
        value.SequenceEqual("fn"u8) || value.SequenceEqual("pub"u8) || value.SequenceEqual("struct"u8) || value.SequenceEqual("enum"u8) ||
        value.SequenceEqual("impl"u8) || value.SequenceEqual("trait"u8) || value.SequenceEqual("class"u8) || value.SequenceEqual("interface"u8) ||
        value.SequenceEqual("namespace"u8) || value.SequenceEqual("using"u8) || value.SequenceEqual("import"u8) || value.SequenceEqual("from"u8) ||
        value.SequenceEqual("as"u8) || value.SequenceEqual("async"u8) || value.SequenceEqual("await"u8) || value.SequenceEqual("match"u8) ||
        value.SequenceEqual("where"u8) || value.SequenceEqual("self"u8) || value.SequenceEqual("this"u8) || value.SequenceEqual("super"u8) ||
        value.SequenceEqual("true"u8) || value.SequenceEqual("false"u8) || value.SequenceEqual("null"u8) || value.SequenceEqual("None"u8) ||
        value.SequenceEqual("True"u8) || value.SequenceEqual("False"u8) || value.SequenceEqual("and"u8) || value.SequenceEqual("or"u8) ||
        value.SequenceEqual("not"u8) || value.SequenceEqual("in"u8) || value.SequenceEqual("is"u8) || value.SequenceEqual("def"u8) ||
        value.SequenceEqual("lambda"u8) || value.SequenceEqual("yield"u8) || value.SequenceEqual("with"u8) || value.SequenceEqual("global"u8) ||
        value.SequenceEqual("nonlocal"u8) || value.SequenceEqual("assert"u8) || value.SequenceEqual("del"u8) || value.SequenceEqual("pass"u8) ||
        value.SequenceEqual("raise"u8) || value.SequenceEqual("except"u8) || value.SequenceEqual("int"u8) || value.SequenceEqual("string"u8) ||
        value.SequenceEqual("bool"u8) || value.SequenceEqual("float"u8) || value.SequenceEqual("double"u8) || value.SequenceEqual("long"u8) ||
        value.SequenceEqual("short"u8) || value.SequenceEqual("byte"u8) || value.SequenceEqual("char"u8) || value.SequenceEqual("unsigned"u8) ||
        value.SequenceEqual("signed"u8) || value.SequenceEqual("template"u8) || value.SequenceEqual("typename"u8);

    private bool IdentifiersOverlap(ReadLine current, int previousIDStart, int previousIDCount, bool contentOnly)
    {
        for (int i = 0; i < current.IDCount; i++)
        {
            IdentifierToken currentID = _tokenIDs[current.IDStart + i];
            if ((!contentOnly && currentID.TextLength < 3) || (contentOnly && !IsContentID(currentID))) continue;
            for (int j = 0; j < previousIDCount; j++)
            {
                IdentifierToken previousID = _tokenIDs[previousIDStart + j];
                if ((!contentOnly && previousID.TextLength < 3) || (contentOnly && !IsContentID(previousID))) continue;
                if (currentID.Value.Equals(previousID.Value)) return true;
            }
        }
        return false;
    }

    /// The HONEST thread depth — non-collapsed windows over the already-tokenized lines.
    private int HonestChain(int win = 30, int stride = 15, int kDistinct = HonestK)
    {
        _honestLineScratch.Clear();
        for (int i = 0; i < _tokenLines.Count; i++)
            if (_tokenLines[i].Trimmed.Span.Length > 0) _honestLineScratch.Add(i);
        if (_honestLineScratch.Count < 2) return _honestLineScratch.Count;
        _honestWindowScratch.Clear();
        int best = 0;
        for (int lo = 0; lo < Math.Max(1, _honestLineScratch.Count - win); lo += stride)
        {
            int hi = Math.Min(_honestLineScratch.Count, lo + win);
            _honestWindowScratch.Clear();
            for (int i = lo; i < hi; i++) _honestWindowScratch.Add(_tokenLines[_honestLineScratch[i]].Trimmed);
            if (_honestWindowScratch.Count < kDistinct) continue;
            int chain = 1, chainBest = 1;
            for (int i = lo; i + 1 < hi; i++)
            {
                bool shared = IdentifiersOverlap(_tokenLines[_honestLineScratch[i]], _tokenLines[_honestLineScratch[i + 1]].IDStart,
                    _tokenLines[_honestLineScratch[i + 1]].IDCount, contentOnly: true);
                chain = shared ? chain + 1 : 1;
                if (chain > chainBest) chainBest = chain;
            }
            if (chainBest > best) best = chainBest;
        }
        return best;
    }

    /// The windowed collapse ALARMS (drive.py verdict gate) over the recent 8-byte-diversity ratios: `collFrac` = the
    /// LEVEL, fraction of recent steps whose block sat in the repetition basin (< CollapseRatio); `dfThird` = the
    /// TREND, diversity last-third ÷ first-third of the window (< 0.7 ⟹ diversity DECAYING toward collapse). drive.py
    /// reads distinct-lines + a data-flow `df`; our newline-sparse generation collapses line-distinctness to 1, so both
    /// alarms ride the byte-block diversity (our genuine collapse-robust read) until FLOW's `df` lands in WAVE 2.
    private static (double CollFrac, double DfThird) CollapseAlarms(Queue<double> divRatios)
    {
        int n = divRatios.Count;
        if (n == 0) return (0, 1.0);
        // single forward pass over the FIFO Queue (order == the old ToArray's): count collapsed, and sum the first/
        // last third by position — no per-step ToArray alloc. Same float ops in the same order ⇒ byte-identical.
        int t = n / 3, collapsed = 0, i = 0; double first = 0, last = 0;
        foreach (double r in divRatios)
        {
            if (r < CollapseRatio) collapsed++;
            if (i < t) first += r;
            if (i >= n - t) last += r;
            i++;
        }
        double collFrac = (double)collapsed / n;
        if (n < 3) return (collFrac, 1.0);
        double fm = first / t, lm = last / t;
        return (collFrac, fm > 1e-9 ? lm / fm : 1.0);
    }

    /// Per-byte histogram — the unigram distribution JS divergence compares between consecutive generations. Fills a
    /// caller-owned 256-int buffer (the ping-pong scratch) in place; zeroes it first so a reused buffer is clean.
    private static int[] FillHist(int[] h, ReadOnlySpan<byte> b) { Array.Clear(h); foreach (var x in b) h[x]++; return h; }

    /// Jensen-Shannon divergence (bits) between two byte-distributions — the sealed-loop distance (strangeloop.js).
    /// JS = H(M) − ½(H(P)+H(Q)), M = ½P+½Q. 0 = identical (a fixpoint self-image); rising = the generations diverge.
    private static double JsDivergence(int[] p, int[] q)
    {
        long tp = 0, tq = 0; for (int i = 0; i < 256; i++) { tp += p[i]; tq += q[i]; }
        if (tp == 0 || tq == 0) return double.NaN;
        double hp = 0, hq = 0, hm = 0;
        for (int i = 0; i < 256; i++)
        {
            double pi = (double)p[i] / tp, qi = (double)q[i] / tq, mi = 0.5 * (pi + qi);
            if (pi > 0) hp -= pi * Math.Log2(pi);
            if (qi > 0) hq -= qi * Math.Log2(qi);
            if (mi > 0) hm -= mi * Math.Log2(mi);
        }
        return hm - 0.5 * (hp + hq);
    }

    /// Classify the recent JS trajectory (strangeloop dream-loop verdict): CONVERGE (JS→0, a fixpoint self-image —
    /// the sealed loop when mint-rate also →0), ORBIT (a bounded plateau — a strange attractor), COLLAPSE (drifting
    /// away or degenerate). Needs a full 3-point tail; "" until then.
    private static string LoopClassify(Queue<double> jsTraj)
    {
        if (jsTraj.Count < 3) return "";
        double t0 = 0, t1 = 0, t2 = 0; int i = 0;                       // read the exactly-3-point FIFO tail in order — no ToArray alloc
        foreach (double v in jsTraj) { if (i == 0) t0 = v; else if (i == 1) t1 = v; else t2 = v; i++; }
        double mn = Math.Min(t0, Math.Min(t1, t2)), mx = Math.Max(t0, Math.Max(t1, t2));
        if (t2 < 0.02 && t2 <= t0) return "CONVERGE";
        if (mx - mn < 0.05 && mn > 0.02) return "ORBIT";
        return "COLLAPSE";
    }

    /// The MOMENTUM verdict-band (mlib.verdict) — meadow/wall/climbing off the savings-slope. NB: mlib reads a LOSS
    /// (lower = better) so slope<0 = descending = meadow; the drive reads SAVINGS (g.TotalSavings, higher = better), so
    /// the sign is FLIPPED here. The dead-band is RELATIVE (|slope| ÷ level) so it is scale-free across corpora. WALL is
    /// the drive's stop condition — but only on a clock where the signal can move: Farm stops on the per-step
    /// window (it refreshes its grammar every step, so its per-step clock IS the refresh clock); the drive (both arms)
    /// stops on the STRIDE-cadence window (its savings is stair-stepped between grammar refreshes — batch re-induce
    /// and loom harvest both ride the stride).
    private static string MomentumVerdict(Queue<double> savings, double tolFrac)
    {
        if (savings.Count < 2) return "";
        double slope = Slope(savings), mean = 0;
        foreach (var v in savings) mean += Math.Abs(v);
        double rel = slope / Math.Max(1.0, mean / savings.Count);
        if (rel > tolFrac) return "MEADOW";                            // savings still climbing = loss still descending → premature to stop
        if (rel < -tolFrac) return "CLIMBING";                         // savings falling = loss rising → diverging / over-saturating
        return "WALL";                                                 // flat → a genuine plateau
    }
}
