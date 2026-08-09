namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── DEPTH-PROFILE AUTOPSY (P1 · P3) ──  READ-ONLY science on a BANKED witnessed-mesh checkpoint, testing the DEPTH
// thesis: a deep rule's long surface is re-derived by an INDEPENDENT source
// ~exponentially rarely, so deep rules behave like MEMORIES not abstractions ("maxSpan = memorization"). Mints
// nothing, changes no engine behaviour — it loads the converged (grammar, tape) via MeshCheckpoint.PeekGrammarAndTape,
// runs Pearl.Audit, and bins reflection by rule DEPTH. Sibling of EdgeAutopsy (the H2 sharpness autopsy); this is the
// DEPTH autopsy that gates Whorl B (the blur / depth cure) with a PRE-BLUR slope baseline + its closed-form null band.
//
// RE-GATED: the confirm-only harness was pre-ordained to "confirm" — eligibility
// empties the shallow bins, first populated bins saturate, zero-reflect bins were censored from the log-fit, and the
// corpus mono-label ("corpus" for every file) erased the file-vs-file independence that IS the wall-#1 witness
// population. The five cures, all here:
//   (1) MONO-LABEL RE-ATTRIBUTION at Peek — corpus spans re-sourced to their ORIGIN FILE via the deterministic
//       SplitPool line map, so a rule re-derived by csharp AND rust corpus counts as 2 independent witnesses.
//   (2) CLOSED-FORM PERMUTATION NULL — span-label shuffle over the fixed (grammar, tape) preserves every marginal and
//       destroys only source↔content correlation: P(reflect|k) = 1 − Σ_s C(m_s,k)/C(M,k) for a rule exercised by k
//       DISTINCT spans. The verdict is now the real−null GAP, not "does a curve decay" (pure noise decays too).
//   (3) LAPLACE FIT over populated bins — zero-reflect bins enter the log-fit (Jeffreys rate), the shallow anchor is
//       the FIRST populated bins (not a structurally-empty d≤3 window).
//   (4) 2D depth×log₂span STRATIFICATION — partial slopes k_depth|span and k_span|depth disentangle "deep" from "long"
//       (if depth vanishes at fixed span the axis is LENGTH and the cure forks away from blur).
//   (5) JOINT-DIET P3 — the diet ladder is one induction over the corpus-source view, not size-biased per-file batches.
//
// P1 — REFLECTION-RATE vs RULE DEPTH. Per rule: depth (the RenormStats tower recurrence — 1 + max child depth),
// byte-span, distinct exercising spans k, and the RE-ATTRIBUTED source breadth (origin-file granularity). A rule is
// REFLECTED iff its breadth ≥ 2 distinct sources. The DIRECT co-walk (a rule's OWN exercises, no reverse-DAG union) is
// the de-confounded axis; the mono-label DAG/DIRECT columns ride along for continuity.
// PREDICTION: the real reflect-rate exceeds the permutation null and that GAP decays with depth; real≈null everywhere
// REFUTES the story deeper than "flat" would (the decay was pure multiplicity), real→null at depth is the honest
// horizon, real>null at max depth refutes the wall despite a decaying curve.
//
// P3 — COLLAPSE-DEPTH vs DIET-DEPTH. The mesh grammar's per-LEVEL Zipf slope (meanz_L) + rule census across the depth
// ladder vs the DIET's own JOINT depth (one batch induction over the whole corpus). PREDICTION: the mesh ladder tops
// out AT the diet's ladder depth — the sealed loop exhausts its world's structure and cannot climb past it.
public static class DepthAutopsy
{
    /// One depth bin's reflection census. Eligible = expLen ≥ ReflectFloorBytes (the reflection-capable population).
    /// `NReflReattr` is the PRIMARY (origin-file breadth ≥ 2); `NullMean`/`NullVar` are the closed-form permutation
    /// expectation Σ p_r and its Bernoulli variance Σ p_r(1−p_r) over the bin's eligible rules. `NReflDirect`/`NReflDag`
    /// are the mono-label continuity reads (from the audit's per-source sets, no separate walk).
    readonly record struct DepthRow(int Depth, int NAll, int NEligible, int NReflDirect, int NReflDag, int NReflReattr,
                                     double NullMean, double NullVar, int NSawReal, double MeanSpan, double MeanExpLen,
                                     double MeanBreadth, double MeanK, double? MeanzLevel);

    public static int Run(string runDir, string outDir, string[]? dietOverride, bool traceRules, bool slotsOn)
    {
        if (!File.Exists(Path.Combine(runDir, MeshCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"depth-autopsy: no mesh checkpoint at {runDir}/{MeshCheckpoint.FileName} — need a banked witnessed-mesh run (triangle/mesh with CheckpointEvery > 0)");
            return 1;
        }

        Console.WriteLine($"depth-profile autopsy — the DEPTH thesis on dead data, monk-re-gated{(slotsOn ? " · SLOTS-ON (Tier 1.5 depth cure)" : " · slots-OFF (pre-blur baseline)")}");
        Console.WriteLine($"  checkpoint  {runDir}");

        // ── load the converged (grammar, tape) and reproduce the run's reflection rule ──
        var peek = MeshCheckpoint.PeekGrammarAndTape(runDir);
        using var tape = peek.Tape;
        var g = peek.Grammar;
        int wScale = peek.WScale;
        bool crossReflect = peek.CrossReflect;
        var corpusPaths = peek.CorpusPaths;
        int n = g.Rules.Length;
        Console.WriteLine($"  grammar     {n} rules · alphabet {g.AlphabetSize} · wScale {wScale} · cross-reflect {crossReflect}");
        Console.WriteLine($"  tape        {tape.Count} resident + {tape.ShedEventIDs.Count} shed spans · {tape.ByteLength}B");

        var audit = Pearl.Audit(tape, g, wScale, crossReflect: true);   // cross-reflection ON — the source-independence gate the witnessed mesh runs under
        var (depth, span) = Engine.RuleDepthSpan(g);
        var expLen = audit.ExpLen;                                      // slot-aware expansion length (Engine.ExpLens) — the reflect-floor gate

        // ── RE-ATTRIBUTED co-walk (re-gate 1) — corpus spans → origin file, DISTINCT exercising spans per rule, and the
        // per-source unit census the null (re-gate 2) permutes. Replaces the old DirectSources re-walk with a single
        // pass that produces what the mono-label audit CANNOT: per-file breadth + distinct-span multiplicity. ──
        var reattr = ReAttribute(g, tape, expLen, corpusPaths);
        Console.WriteLine($"  sources     {reattr.SourceCensus.Count} labels over {reattr.TotalUnits} view units — {string.Join(" · ", reattr.SourceCensus.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}");

        // ── SLOT POOLING (Tier 1.5 — the depth cure) ──  slots-ON unions a rule's exercising units with its slot-mates'
        // (Blur.DetectRuleSlots: rules instantiating the same rich-frame slot-PATTERN), so a deep single-source rule
        // reflects when a PEER exercises its pattern. `ownBreadth`/`ownK` stay the slots-OFF read (for the newly-reflect
        // margin below + the byte-identical baseline); `breadth`/`kUnits` are the pooled read the P1/P2 rows measure.
        int slotClasses = 0, pooledRules = 0, biggestClass = 0;
        var mates = new int[]?[n];
        if (slotsOn)
        {
            (mates, slotClasses, _) = Blur.DetectRuleSlots(g.Rules, g.AlphabetSize);
            for (int r = 0; r < n; r++) if (mates[r] is { Length: > 1 } m) { pooledRules++; if (m.Length > biggestClass) biggestClass = m.Length; }
            Console.WriteLine($"  slots       {slotClasses} slot-classes · {pooledRules} rules pooled · biggest class {biggestClass}");
        }
        var (breadth, kUnits) = Pool(reattr, mates);
        var (ownBreadth, _) = slotsOn ? Pool(reattr, new int[]?[n]) : (breadth, kUnits);

        var uses = global::Cogito.Engine.RuleUses(g);

        // ── P1 — bin by depth ──
        int maxDepth = 0; for (int i = 0; i < n; i++) if (depth[i] > maxDepth) maxDepth = depth[i];
        long M = reattr.TotalUnits;
        var mS = reattr.SourceCensus.Values.ToArray();   // per-source unit counts — the null's marginal
        var rows = new List<DepthRow>(maxDepth);
        for (int d = 1; d <= maxDepth; d++)
        {
            int nAll = 0, nElig = 0, nRD = 0, nRDag = 0, nRR = 0, nSaw = 0;
            long spanSum = 0, expSum = 0, breadthSum = 0, kSum = 0;
            double nullMean = 0, nullVar = 0;
            var lvlFreqs = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (depth[i] != d) continue;
                nAll++;
                lvlFreqs.Add(uses[i]);
                if (expLen[i] < Pearl.ReflectFloorBytes) continue;
                nElig++;
                spanSum += span[i]; expSum += expLen[i];
                int ds = audit.JewelCountsDirect?[i]?.Count ?? 0;        // mono-label DIRECT breadth (from the audit — no re-walk)
                int gs = audit.JewelSources?[i]?.Count ?? 0;             // mono-label DAG breadth
                int rr = breadth[i];                                     // origin-file breadth (slot-pooled when slots-ON) — the honest witness count
                long k = kUnits[i];                                      // distinct exercising spans (slot-pooled when slots-ON) — the null's k
                breadthSum += rr; kSum += k;
                if (ds >= 2) nRD++;
                if (gs >= 2) nRDag++;
                if (rr >= 2) nRR++;
                double p = NullReflectProb(k, mS, M);
                nullMean += p; nullVar += p * (1 - p);
                if (audit.SawReal[i]) nSaw++;
            }
            double? meanz = lvlFreqs.Count >= 4 ? Nan(global::Cogito.Engine.ZipfOf(lvlFreqs)) : null;
            rows.Add(new DepthRow(d, nAll, nElig, nRD, nRDag, nRR, nullMean, nullVar, nSaw,
                nElig > 0 ? (double)spanSum / nElig : 0, nElig > 0 ? (double)expSum / nElig : 0,
                nElig > 0 ? (double)breadthSum / nElig : 0, nElig > 0 ? (double)kSum / nElig : 0, meanz));
        }

        // ── P1 — span bins (log2 of byte-span) : the thesis's mechanistic variable is SURFACE LENGTH ──
        var spanRows = SpanBins(n, depth, span, expLen, breadth, audit);

        // ── P1 VERDICTS ──
        // (a) the BASELINE SLOPE (re-gate 2) — the LINEAR real-reattr rate vs depth against the closed-form null band.
        var slope = NullSlopeBand(rows);
        // (b) the DECAY descriptor (re-gate 3) — Laplace log-fit over populated bins, shallow=first populated bins.
        var fitReattr = FitDecayLaplace(rows, r => r.NReflReattr, "REATTR (origin-file breadth — the honest reflection)");
        var fitDag = FitDecayLaplace(rows, r => r.NReflDag, "DAG (nested-credited, mono-label — continuity)");
        // (c) DEPTH-BEYOND-LENGTH (re-gate 4) — 2D partial slopes over the eligible rule cloud.
        var strat = Partial2D(n, depth, span, expLen, breadth);
        // (d) THE SLOT MARGIN (exit gate) — per deep newly-reflecting rule, real slot-pool breadth − size-matched
        // random-floor-pool expectation; the DISTRIBUTION's SHAPE names the cure (tight=dosage, bimodal=threshold).
        var margin = slotsOn ? ComputeSlotMargin(reattr, mates, depth, expLen, breadth, ownBreadth) : default;
        List<SlotCoreRow> slotCore = slotsOn ? ComputeSlotCore(g, corpusPaths, mates, margin) : [];

        // ── P3 — the diet's own depth ladder ──
        var dietPaths = (dietOverride is { Length: > 0 } ? dietOverride : corpusPaths.ToArray());
        var diet = DietDepths(dietPaths);
        var joint = JointDiet(dietPaths);   // re-gate 5 — one induction over the whole corpus-source view
        int meshCollapseL = 0; for (int i = 0; i < rows.Count; i++) if (rows[i].NAll >= 4) meshCollapseL = rows[i].Depth;   // deepest level still yielding a Zipf slope (KZ boundary)
        int dietMaxL = joint.Ok ? joint.MaxL : diet.Where(dd => dd.Ok).Select(dd => dd.MaxL).DefaultIfEmpty(0).Max();   // JOINT is the ladder of record; per-file max only if joint unreadable

        // ── THE P1↔P3 BRIDGE — corroboration WITHIN the diet's ladder vs ABOVE it. Rules whose depth exceeds the raw
        // diet's own structural depth are loopback-manufactured (dream-on-dream) and cannot be independently witnessed;
        // WithinReattr/AboveReattr is the readout: reflection holds inside the ladder, hollows out in the OVERSHOOT band. ──
        (int refl, int elig) within = (0, 0), above = (0, 0);
        foreach (var r in rows)
        {
            if (dietMaxL <= 0) break;
            if (r.Depth <= dietMaxL) { within.refl += r.NReflReattr; within.elig += r.NEligible; }
            else { above.refl += r.NReflReattr; above.elig += r.NEligible; }
        }
        double withinPct = within.elig > 0 ? 100.0 * within.refl / within.elig : double.NaN;
        double abovePct = above.elig > 0 ? 100.0 * above.refl / above.elig : double.NaN;

        // ── render ──
        Directory.CreateDirectory(outDir);
        WriteP1Tsv(Path.Combine(outDir, "p1_depth_reflection.tsv"), rows);
        WriteSpanTsv(Path.Combine(outDir, "p1_span_reflection.tsv"), spanRows);
        WriteP3Tsv(Path.Combine(outDir, "p3_criticality_by_depth.tsv"), rows, diet, joint, meshCollapseL);
        WriteSlopeTsv(Path.Combine(outDir, "p2_baseline_slope.tsv"), slope, fitReattr, strat, maxDepth, dietMaxL, withinPct, abovePct);
        WriteHtml(Path.Combine(outDir, "depth_autopsy.html"), runDir, n, wScale, rows, spanRows, slope, fitReattr, fitDag, strat, diet, joint, meshCollapseL, maxDepth, dietMaxL, withinPct, abovePct);
        if (slotsOn)
        {
            WriteMarginTsv(Path.Combine(outDir, "p2_slot_margin.tsv"), margin);
            WriteSlotCoreTsv(Path.Combine(outDir, "p2_slot_core.tsv"), slotCore);
            WriteSlotModesTsv(Path.Combine(outDir, "p2_slot_modes.tsv"), slotCore);
        }
        if (traceRules) WriteRuleTrace(Path.Combine(outDir, "rules_by_depth.tsv"), g, depth, span, expLen, kUnits, breadth, audit);

        // ── console summary — the routing payload ──
        Console.WriteLine();
        Console.WriteLine("P1 · REFLECTION-RATE vs RULE DEPTH  (eligible = expLen ≥ 8B · real% = origin-file breadth ≥ 2)");
        Console.WriteLine("  depth  n_all  n_elig    real%    null%     gap    dag%*   mean_k   mean_span   meanz");
        foreach (var r in rows)
        {
            if (r.NAll == 0) continue;
            string rp = r.NEligible > 0 ? Pct(r.NReflReattr, r.NEligible) : "   —  ";
            string np = r.NEligible > 0 ? $"{100.0 * r.NullMean / r.NEligible,5:F1}%" : "   —  ";
            string gp = r.NEligible > 0 ? Sgn1(100.0 * (r.NReflReattr - r.NullMean) / r.NEligible) : "  —  ";
            string dp = r.NEligible > 0 ? Pct(r.NReflDag, r.NEligible) : "   —  ";
            string mz = r.MeanzLevel is double m ? m.ToString("F3", CultureInfo.InvariantCulture) : "  —  ";
            Console.WriteLine($"  {r.Depth,4}  {r.NAll,6}  {r.NEligible,6}   {rp}   {np}   {gp}   {dp}   {r.MeanK,6:F1}   {r.MeanSpan,9:F1}   {mz,7}");
        }
        Console.WriteLine("  (* dag% = mono-label nested-credited substrate, continuity only; real% vs null% is the honest read)");
        Console.WriteLine();
        Console.WriteLine("P2 · THE PRE-BLUR BASELINE SLOPE — real reattr rate vs depth against the closed-form permutation null");
        Console.WriteLine($"    real slope β = {Sgn5(slope.RealSlope)}/level   (95% CI {Sgn5(slope.RealSlope - 1.96 * slope.RealSE)} … {Sgn5(slope.RealSlope + 1.96 * slope.RealSE)})");
        Console.WriteLine($"    null slope band (span-label permutation): β_null = {Sgn5(slope.NullSlope)} ± {2 * slope.NullSD:F5}  (2σ)");
        Console.WriteLine($"    → {slope.Verdict}");
        Console.WriteLine($"    RAW-rate descriptor (log-fit, Laplace, first-populated anchor): {fitReattr.Label}");
        Console.WriteLine($"      k = {Sgn5(fitReattr.Slope)}/level (factor {Math.Exp(fitReattr.Slope):F3}×), R² {fitReattr.R2:F3}, shallow {fitReattr.ShallowPct:F1}% → deep {fitReattr.DeepPct:F1}% — the raw rate {(fitReattr.Slope > 0 ? "RISES" : "falls")} with depth (DIRECT top-level-chunk bias; the null absorbs it — read the gap, not this)");
        Console.WriteLine();
        Console.WriteLine("P2b · DEPTH-BEYOND-LENGTH — 2D (depth × log₂span) partial slopes over eligible rules");
        Console.WriteLine($"    marginal k_depth        = {Sgn5(strat.MargDepth)}      partial k_depth|span = {Sgn5(strat.PartDepth)}");
        Console.WriteLine($"    marginal k_span         = {Sgn5(strat.MargSpan)}      partial k_span|depth = {Sgn5(strat.PartSpan)}");
        Console.WriteLine($"    → {strat.Verdict}");
        Console.WriteLine();
        if (slotsOn)
        {
            Console.WriteLine("P2c · THE SLOT MARGIN — per deep newly-reflecting rule: real slot-pool breadth − size-matched random-floor expectation");
            Console.WriteLine($"    slot-classes {margin.Classes} · {margin.PooledRules} rules pooled · deep (d≥3) newly-reflecting slots-ON: REAL {margin.NewlyReflectDeep} vs RANDOM-floor null {margin.NullDeepGain:F1}");
            if (margin.NewlyReflectDeep == 0)
                Console.WriteLine("    → NO DEEP CURE ON THE REAL MESH — no deep rule crosses to ≥2 sources under slot pooling (the synthetic-split cure does NOT transfer; a clean SYNTHETIC-ONLY finding)");
            else
            {
                Console.WriteLine($"    margin distribution (n={margin.NewlyReflectDeep}, mean {Sgn5(margin.MeanMargin)} sources):");
                for (int b = 0; b < margin.Hist.Length; b++)
                    Console.WriteLine($"      {MarginBinLabel(b),-14} {margin.Hist[b],4}  {new string('█', Math.Min(48, margin.Hist[b] * 48 / Math.Max(1, margin.NewlyReflectDeep)))}");
                Console.WriteLine($"    → SHAPE: {margin.Shape}");
                Console.WriteLine("    held-out ΔMDL by mode (real pooled witness vs marginal-filler null):");
                foreach (var mode in SummarizeSlotModes(slotCore))
                    Console.WriteLine($"      {mode.Mode,-4} rows {mode.Rows,3} · classes {mode.Classes,3} · real-shrink {mode.RealShrink}/{mode.Rows} ({100.0 * mode.RealShrink / Math.Max(1, mode.Rows):F1}%) · null-shrink {mode.NullShrink}/{mode.Rows} ({100.0 * mode.NullShrink / Math.Max(1, mode.Rows):F1}%) · survives {mode.Survives}/{mode.Rows} ({100.0 * mode.Survives / Math.Max(1, mode.Rows):F1}%) · mean real {mode.MeanRealPay:F1}b vs null {mode.MeanNullPay:F1}b");
            }
            Console.WriteLine();
        }
        Console.WriteLine("P3 · COLLAPSE-DEPTH vs DIET-DEPTH  (JOINT induction — re-gate 5)");
        Console.WriteLine($"  mesh grammar depth ladder: maxDepth {maxDepth} · deepest Zipf-scored level (KZ boundary) {meshCollapseL}");
        Console.WriteLine(joint.Ok
            ? $"  JOINT diet (all corpora, one induction): maxDepth {joint.MaxL} · KZ boundary {joint.Kz} · {joint.Rules} rules"
            : "  JOINT diet unavailable — corpus paths not found; pass --diet <files>");
        foreach (var (name, ok, maxL, kz, rules) in diet)
            Console.WriteLine(ok
                ? $"    per-file (size-biased) {name,-16} maxDepth {maxL,3} · KZ {kz,3} · {rules} rules"
                : $"    per-file {name,-16} (unreadable — {rules} rules)");
        if (dietMaxL > 0)
        {
            Console.WriteLine($"  mesh ladder {maxDepth} vs JOINT diet ladder {dietMaxL} — {(maxDepth > dietMaxL + 1 ? $"OVERSHOOTS by {maxDepth - dietMaxL} levels (the loop climbed PAST its world's structure)" : "TRACKS the diet")}");
            Console.WriteLine($"  P1↔P3 BRIDGE — reattr reflection WITHIN diet ladder (d≤{dietMaxL}) = {withinPct:F1}%  vs  ABOVE it (d>{dietMaxL}) = {abovePct:F1}%");
            Console.WriteLine($"    → {(withinPct - abovePct > 8 ? "the overshoot band is HOLLOW (reflection collapses past diet depth — the loop built un-witnessable literals)" : "reflection holds through the overshoot (no diet-depth cliff at this measure)")}");
        }
        Console.WriteLine();
        Console.WriteLine($"  rendered → {outDir}/  (depth_autopsy.html · p1_depth_reflection.tsv · p1_span_reflection.tsv · p2_baseline_slope.tsv{(slotsOn ? " · p2_slot_margin.tsv · p2_slot_core.tsv · p2_slot_modes.tsv" : "")} · p3_criticality_by_depth.tsv)");
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  MONO-LABEL RE-ATTRIBUTION (re-gate 1) + the null's raw material.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// The re-attributed co-walk's raw material, rule-indexed over eligible rules. `OwnUnits[r]` = the DISTINCT view
    /// units that DIRECTLY exercised r (corpus→origin-file via `UnitSrc`, node dreams keep their tag); `UnitSrc[u]` =
    /// unit u's re-attributed source label. `SourceCensus` maps each label → its unit count (the null's marginal m_s);
    /// `TotalUnits` = M. Direct co-walk only — no reverse-DAG union (the de-confounded axis). `Pool` turns the unit sets
    /// into (breadth, k): own for slots-OFF, UNIONED over slot-mates for slots-ON (the depth cure — a peer exercising a
    /// slot-MATE credits the rule, mirroring Pearl.SlotJewels; both breadth AND the null's k then inherit slots).
    readonly record struct ReAttrib(HashSet<int>?[] OwnUnits, string[] UnitSrc, Dictionary<string, long> SourceCensus, long TotalUnits);

    static ReAttrib ReAttribute(in RePairResult g, Tape tape, long[] expLen, IReadOnlyList<string> corpusPaths)
    {
        int n = g.Rules.Length;
        int alpha = (int)g.AlphabetSize;

        // origin-file map: hash of a pool line's bytes → "corpus:<file>"; a line shared across files → "corpus:shared".
        var fileMap = new Dictionary<ulong, string>();
        foreach (var p in corpusPaths)
        {
            if (!File.Exists(p)) continue;
            string label = "corpus:" + Path.GetFileNameWithoutExtension(p);
            int line = 0;
            foreach (var mem in global::Cogito.Engine.SplitLines(File.ReadAllBytes(p)))
            {
                if (line++ % 10 == 9) continue;   // held-out lines never reach the tape (SplitPool)
                ulong h = Fnv(mem.Span);
                if (fileMap.TryGetValue(h, out var prev)) { if (prev != label) fileMap[h] = "corpus:shared"; }
                else fileMap[h] = label;
            }
        }

        // per-unit re-attributed source label — slice the view's bytes (residents then shed, the GetEventViews order) so
        // corpus units resolve to origin file; every other source (node dreams) passes through unchanged.
        var units = new List<TapeEventView>(tape.Count + tape.ShedEventIDs.Count);
        foreach (var u in tape.GetEventViews()) units.Add(u);
        var full = tape.Concat();
        var unitSrc = new string[units.Count];
        long off = 0;
        for (int si = 0; si < units.Count; si++)
        {
            var u = units[si];
            unitSrc[si] = u.Source == "corpus" && fileMap.TryGetValue(Fnv(full.AsSpan((int)off, u.Len)), out var lab)
                ? lab : u.Source;
            off += u.Len + 1;
        }

        // the source census (null marginal m_s, M) over the re-attributed labels
        var census = new Dictionary<string, long>();
        for (int si = 0; si < unitSrc.Length; si++) census[unitSrc[si]] = census.GetValueOrDefault(unitSrc[si]) + 1;

        // the co-walk — monotone cursor (barrier law) — collecting each rule's DISTINCT exercising units. The unit SET
        // (not just a count) is the raw material Pool needs: slots-ON UNIONS a rule's units with its slot-mates' units,
        // so a peer exercising a slot-MATE credits the rule (the depth cure). Slots-OFF, Pool reads own units → the
        // count and per-file source breadth are identical to the committed baseline (|set| = distinct spans).
        var ownUnits = new HashSet<int>?[n];
        long pos = 0; int cur = 0;
        long unitEnd = units.Count > 0 ? units[0].Len + 1 : 0;
        foreach (var s in g.Compressed)
        {
            while (pos >= unitEnd && cur + 1 < units.Count) { cur++; unitEnd += units[cur].Len + 1; }
            long len;
            if (s.Value < (uint)alpha) len = 1;
            else
            {
                int r = (int)(s.Value - alpha);
                len = expLen[r];
                if (expLen[r] >= Pearl.ReflectFloorBytes) (ownUnits[r] ??= new()).Add(cur);
            }
            pos += len;
        }
        return new ReAttrib(ownUnits, unitSrc, census, units.Count);
    }

    static readonly HashSet<int> EmptyUnits = new();

    /// Turn per-rule OWN unit sets into (breadth, k). `mates[r]` all-null ⇒ SLOTS-OFF: a rule reads its own units, so
    /// breadth = distinct re-attributed sources and k = distinct spans, BYTE-IDENTICAL to the committed baseline.
    /// `mates[r]` non-null (Blur.DetectRuleSlots) ⇒ SLOTS-ON: the rule's units UNION its slot-mates' units (the depth
    /// cure), so a peer exercising the same slot-PATTERN lifts both the per-file breadth AND the permutation null's k.
    static (int[] Breadth, long[] KUnits) Pool(in ReAttrib ra, int[]?[] mates)
    {
        int n = ra.OwnUnits.Length;
        var breadth = new int[n];
        var kUnits = new long[n];
        var pooledTmp = new HashSet<int>();
        var srcTmp = new HashSet<string>();
        for (int r = 0; r < n; r++)
        {
            HashSet<int> pooled;
            if (mates[r] is { Length: > 1 } m)
            {
                pooledTmp.Clear();
                foreach (int mate in m) if (ra.OwnUnits[mate] is { } su) pooledTmp.UnionWith(su);
                pooled = pooledTmp;
            }
            else pooled = ra.OwnUnits[r] ?? EmptyUnits;
            kUnits[r] = pooled.Count;
            srcTmp.Clear();
            foreach (int u in pooled) srcTmp.Add(ra.UnitSrc[u]);
            breadth[r] = srcTmp.Count;
        }
        return (breadth, kUnits);
    }

    readonly record struct SlotMarginRow(int Rule, int Depth, long ExpLen, int ClassSize, int OwnBreadth,
                                         int SlotBreadth, double RealGain, double NullGain, double Margin,
                                         double NullReflectProb);
    readonly record struct SlotMargin(int Classes, int PooledRules, int NewlyReflectDeep, double NullDeepGain,
                                      double MeanMargin, int[] Hist, string Shape, List<SlotMarginRow> Rows);
    readonly record struct SlotCoreRow(int Rule, string Mode, int Depth, long ExpLen, int ClassSize, int OwnBreadth,
                                       int SlotBreadth, double Margin, string ClassKey, string Frame, string Filler,
                                       int Fillers, double RealPay, double NullPay, bool RealShrinks, bool NullShrinks,
                                       bool Survives, string Expansion, string FillersPreview, string MatePreview);
    readonly record struct SlotModeRow(string Mode, int Rows, int Classes, int RealShrink, int NullShrink, int Survives,
                                       double MeanRealPay, double MeanNullPay, double MeanMargin);
    readonly record struct TokenSplit(List<string[]> Train, List<string[]> Heldout, string[] Vocab, long[] Cum, long Total, double HeldoutFlatMdl);
    readonly record struct SlotDescriptor(string ClassKey, string Frame, string Filler, string[] Fillers,
                                          string Expansion, string MatePreview);
    readonly record struct HeldoutPay(double RealPay, double NullPay, bool RealShrinks, bool NullShrinks, bool Survives);

    static SlotMargin ComputeSlotMargin(in ReAttrib ra, int[]?[] mates, int[] depth, long[] expLen, int[] breadth, int[] ownBreadth)
    {
        const int NullDraws = 64;
        int n = ra.OwnUnits.Length;
        var floorRules = new List<int>();
        for (int r = 0; r < n; r++) if (expLen[r] >= Pearl.ReflectFloorBytes) floorRules.Add(r);

        int classes = 0, pooledRules = 0;
        var seenRoot = new HashSet<int>();
        for (int r = 0; r < n; r++)
            if (mates[r] is { Length: > 1 } m)
            {
                pooledRules++;
                if (seenRoot.Add(m[0])) classes++;
            }

        double nullDeepGain = 0;
        var rows = new List<SlotMarginRow>();
        for (int r = 0; r < n; r++)
        {
            if (depth[r] < 3 || expLen[r] < Pearl.ReflectFloorBytes || ownBreadth[r] >= 2) continue;
            if (mates[r] is not { Length: > 1 } m) continue;
            var (nullGain, nullReflectProb) = RandomFloorGain(ra, floorRules, r, m.Length, ownBreadth[r], NullDraws);
            nullDeepGain += nullReflectProb;
            if (breadth[r] < 2) continue;
            double realGain = breadth[r] - ownBreadth[r];
            rows.Add(new SlotMarginRow(r, depth[r], expLen[r], m.Length, ownBreadth[r], breadth[r],
                realGain, nullGain, realGain - nullGain, nullReflectProb));
        }

        var hist = new int[6];
        double sum = 0;
        foreach (var row in rows)
        {
            sum += row.Margin;
            hist[MarginBin(row.Margin)]++;
        }
        double mean = rows.Count > 0 ? sum / rows.Count : 0;
        return new SlotMargin(classes, pooledRules, rows.Count, nullDeepGain, mean, hist, ClassifyMarginShape(rows, mean), rows);
    }

    static (double Gain, double ReflectProb) RandomFloorGain(in ReAttrib ra, List<int> floorRules, int r, int classSize, int ownBreadth, int draws)
    {
        if (classSize <= 1 || floorRules.Count <= 1 || draws <= 0) return (0, 0);
        int need = Math.Min(classSize - 1, floorRules.Count - 1);
        if (need <= 0) return (0, 0);

        var pooled = new HashSet<int>();
        var picked = new HashSet<int>();
        var srcTmp = new HashSet<string>();
        double gain = 0, reflect = 0;
        for (int draw = 0; draw < draws; draw++)
        {
            pooled.Clear();
            picked.Clear();
            if (ra.OwnUnits[r] is { } own) pooled.UnionWith(own);
            ulong rng = 0xD631_5A10_5EED_5107UL ^ ((ulong)(uint)r * 0x9E37_79B9_7F4A_7C15UL) ^ ((ulong)draw * 0xD1B5_4A32_D192_ED03UL);
            int taken = 0, guard = 0;
            while (taken < need && guard++ < floorRules.Count * 8 + need * 8)
            {
                rng = rng * 6364136223846793005UL + 1442695040888963407UL;
                int cand = floorRules[(int)((rng >> 33) % (ulong)floorRules.Count)];
                if (cand == r || !picked.Add(cand)) continue;
                if (ra.OwnUnits[cand] is { } su) pooled.UnionWith(su);
                taken++;
            }
            if (taken < need)
                foreach (int cand in floorRules)
                {
                    if (cand == r || !picked.Add(cand)) continue;
                    if (ra.OwnUnits[cand] is { } su) pooled.UnionWith(su);
                    if (++taken == need) break;
                }
            int b = UnitSourceBreadth(ra, pooled, srcTmp);
            gain += Math.Max(0, b - ownBreadth);
            if (b >= 2) reflect++;
        }
        return (gain / draws, reflect / draws);
    }

    static int UnitSourceBreadth(in ReAttrib ra, HashSet<int> units, HashSet<string> srcTmp)
    {
        srcTmp.Clear();
        foreach (int u in units) srcTmp.Add(ra.UnitSrc[u]);
        return srcTmp.Count;
    }

    static int MarginBin(double margin)
        => margin <= 0 ? 0
         : margin <= 0.25 ? 1
         : margin <= 0.75 ? 2
         : margin <= 1.5 ? 3
         : margin <= 3.0 ? 4
         : 5;

    static string ClassifyMarginShape(List<SlotMarginRow> rows, double mean)
    {
        int n = rows.Count;
        if (n == 0) return "NO NEWLY-REFLECTING DEEP RULES — the slot cure did not transfer to this mesh";
        int nonPos = rows.Count(r => r.Margin <= 0);
        int near = rows.Count(r => r.Margin > 0 && r.Margin <= 0.75);
        int strong = rows.Count(r => r.Margin >= 1.5);
        if (nonPos >= Math.Max(2, n / 2))
            return "NULL-LEVEL — most newly-reflecting rules barely beat or trail the random floor; selection signal is weak";
        if (strong >= Math.Max(2, n / 4) && nonPos + near >= Math.Max(2, n / 4))
            return "BIMODAL SPLIT — strong core plus near-null/noisy slots; this is a THRESHOLD problem";
        if (near >= Math.Max(2, (int)Math.Ceiling(0.6 * n)) && mean <= 0.75)
            return "TIGHT JUST-ABOVE-NULL CLUSTER — guard calibrated, paradigms weak; this is a DOSAGE problem";
        if (strong >= Math.Max(2, (int)Math.Ceiling(0.6 * n)) && mean > 1.25)
            return "STRONG SHIFT — real slot classes outrun random floor across most newly-reflecting rules";
        return "BROAD MIXED — neither pure dosage nor pure threshold; inspect p2_slot_margin.tsv";
    }

    const double SlotModeHighFloor = 1.5;   // the existing BIMODAL classifier's valley: below = near-null/noise, above = strong core.
    const double HeldoutPayFloorBits = 1.0; // matches Blur.MarginalFillerNull's anti-Goodhart floor.

    static List<SlotCoreRow> ComputeSlotCore(in RePairResult g, IReadOnlyList<string> corpusPaths, int[]?[] mates, in SlotMargin margin)
    {
        var split = LoadHeldoutTokenSplit(corpusPaths);
        var scoreCache = new Dictionary<string, HeldoutPay>(StringComparer.Ordinal);
        var rows = new List<SlotCoreRow>(margin.Rows.Count);
        foreach (var row in margin.Rows.OrderByDescending(r => r.Margin).ThenBy(r => r.Rule))
        {
            var desc = DescribeSlotRule(g.Rules, g.AlphabetSize, row.Rule, mates[row.Rule] ?? new[] { row.Rule });
            string classKey = desc.ClassKey;
            if (!scoreCache.TryGetValue(classKey, out var pay))
            {
                ulong seed = 0xD3E7_4150_5107_C0DEUL ^ FnvString(classKey);
                pay = ScoreHeldoutSlot(split, desc.Fillers, seed);
                scoreCache[classKey] = pay;
            }
            string mode = row.Margin >= SlotModeHighFloor ? "high" : "low";
            rows.Add(new SlotCoreRow(row.Rule, mode, row.Depth, row.ExpLen, row.ClassSize, row.OwnBreadth,
                row.SlotBreadth, row.Margin, classKey, desc.Frame, desc.Filler, desc.Fillers.Length, pay.RealPay,
                pay.NullPay, pay.RealShrinks, pay.NullShrinks, pay.Survives, desc.Expansion,
                string.Join(" ", desc.Fillers.Take(12).Select(f => EscapeText(f, 18))) + (desc.Fillers.Length > 12 ? " ..." : ""),
                desc.MatePreview));
        }
        return rows;
    }

    static SlotDescriptor DescribeSlotRule(GrammarRule[] rules, uint alpha, int rule, int[] mates)
    {
        var toks = new Dictionary<int, string[]>();
        foreach (int m in mates)
            toks[m] = Blur.TokensOf(Reconstruct.Expand(rules, [new Symbol(alpha + (uint)m)]));

        string[] target = toks.TryGetValue(rule, out var tt) ? tt : [];
        int bestPos = -1, bestCount = 0, bestFillers = 0;
        List<string> bestMembers = [];
        if (target.Length > 0)
            for (int p = 0; p < target.Length; p++)
            {
                var fillers = new HashSet<string>(StringComparer.Ordinal);
                var members = new List<string>();
                foreach (var (m, mt) in toks)
                {
                    if (!SameFrame(target, mt, p)) continue;
                    fillers.Add(mt[p]);
                    members.Add(mt[p]);
                }
                if (members.Count < 2 || fillers.Count < 2) continue;
                if (members.Count > bestCount || (members.Count == bestCount && fillers.Count > bestFillers))
                {
                    bestPos = p; bestCount = members.Count; bestFillers = fillers.Count;
                    bestMembers = fillers.Order(StringComparer.Ordinal).ToList();
                }
            }

        if (bestPos < 0)
        {
            bestMembers = toks.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(16).ToList();
            if (bestMembers.Count == 0 && target.Length > 0) bestMembers.Add(target[0]);
        }

        string frame = bestPos >= 0 ? FrameText(target, bestPos) : "(no single dominant frame)";
        string filler = bestPos >= 0 ? target[bestPos] : "";
        string classKey = string.Join("\u001f", bestMembers);
        string expansion = EscapeText(Encoding.UTF8.GetString(Reconstruct.Expand(rules, [new Symbol(alpha + (uint)rule)])), 180);
        string matePreview = string.Join(" | ", mates.Take(5).Select(m =>
            $"N{256 + m}:{EscapeText(Encoding.UTF8.GetString(Reconstruct.Expand(rules, [new Symbol(alpha + (uint)m)])), 70)}"));
        if (mates.Length > 5) matePreview += " | ...";
        return new SlotDescriptor(classKey, frame, filler, bestMembers.ToArray(), expansion, matePreview);
    }

    static bool SameFrame(string[] a, string[] b, int blank)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (i != blank && a[i] != b[i]) return false;
        return true;
    }

    static string FrameText(string[] toks, int blank)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < toks.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(i == blank ? "___" : EscapeText(toks[i], 24));
        }
        return sb.ToString();
    }

    static TokenSplit LoadHeldoutTokenSplit(IReadOnlyList<string> corpusPaths)
    {
        var train = new List<string[]>();
        var held = new List<string[]>();
        foreach (var p in corpusPaths)
        {
            if (!File.Exists(p)) continue;
            int line = 0;
            foreach (var mem in global::Cogito.Engine.SplitLines(File.ReadAllBytes(p)))
            {
                var toks = Blur.Tokenize(mem.ToArray());
                if (toks.Count == 0) { line++; continue; }
                if (line++ % 10 == 9) held.AddRange(toks);
                else train.AddRange(toks);
            }
        }

        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in train) foreach (var t in s) freq[t] = freq.GetValueOrDefault(t) + 1;
        var vocab = freq.Keys.Order(StringComparer.Ordinal).ToArray();
        var cum = new long[vocab.Length]; long acc = 0;
        for (int i = 0; i < vocab.Length; i++) { acc += freq[vocab[i]]; cum[i] = acc; }
        double flat = held.Count > 0 ? AntiUnify.TwoPartMdl(held, held, new Dictionary<string, string>(StringComparer.Ordinal)).Total : 0;
        return new TokenSplit(train, held, vocab, cum, acc, flat);
    }

    static HeldoutPay ScoreHeldoutSlot(in TokenSplit split, string[] members, ulong seed)
    {
        if (split.Heldout.Count == 0 || split.Vocab.Length == 0 || members.Length < 2) return new HeldoutPay(0, 0, false, false, false);
        const int Draws = 8;
        double real = SlotHeldoutPay(split.HeldoutFlatMdl, split.Heldout, members, "[P2S]");
        double nullSum = 0;
        for (int d = 0; d < Draws; d++)
            nullSum += SlotHeldoutPay(split.HeldoutFlatMdl, split.Heldout, DrawNullMembers(split, members.Length, seed ^ ((ulong)d * 0x9E37_79B9_7F4A_7C15UL)), "[P2S]");
        double nul = nullSum / Draws;
        bool realShrinks = real > HeldoutPayFloorBits;
        bool nullShrinks = nul > HeldoutPayFloorBits;
        return new HeldoutPay(real, nul, realShrinks, nullShrinks, realShrinks && real > nul + HeldoutPayFloorBits);
    }

    static double SlotHeldoutPay(double flatMdl, IReadOnlyList<string[]> heldout, IReadOnlyList<string> members, string slotName)
    {
        var set = new HashSet<string>(members, StringComparer.Ordinal);
        var slotted = new List<List<string>>(heldout.Count);
        foreach (var s in heldout)
        {
            var row = new List<string>(s.Length);
            foreach (var t in s) row.Add(set.Contains(t) ? slotName : t);
            slotted.Add(row);
        }
        var m2s = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in members) m2s[m] = slotName;
        return flatMdl - AntiUnify.TwoPartMdl(heldout, slotted, m2s).Total;
    }

    static string[] DrawNullMembers(in TokenSplit split, int count, ulong seed)
    {
        var members = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ulong rng = seed == 0 ? 0xA17D_5EEDUL : seed;
        int guard = 0;
        while (members.Count < count && guard++ < count * 64 + split.Vocab.Length)
        {
            rng = rng * 6364136223846793005UL + 1442695040888963407UL;
            long r = (long)((rng >> 11) % (ulong)Math.Max(1, split.Total));
            int lo = 0, hi = split.Vocab.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (split.Cum[mid] <= r) lo = mid + 1; else hi = mid;
            }
            if (seen.Add(split.Vocab[lo])) members.Add(split.Vocab[lo]);
        }
        return members.ToArray();
    }

    static List<SlotModeRow> SummarizeSlotModes(List<SlotCoreRow> rows)
    {
        var outp = new List<SlotModeRow>();
        foreach (var mode in new[] { "high", "low" })
        {
            var rs = rows.Where(r => r.Mode == mode).ToList();
            if (rs.Count == 0) { outp.Add(new SlotModeRow(mode, 0, 0, 0, 0, 0, 0, 0, 0)); continue; }
            outp.Add(new SlotModeRow(mode, rs.Count, rs.Select(r => r.ClassKey).Distinct(StringComparer.Ordinal).Count(),
                rs.Count(r => r.RealShrinks), rs.Count(r => r.NullShrinks), rs.Count(r => r.Survives),
                rs.Average(r => r.RealPay), rs.Average(r => r.NullPay), rs.Average(r => r.Margin)));
        }
        return outp;
    }

    /// P(rule reflects | k distinct exercising spans) under the span-label PERMUTATION null (re-gate 2): a rule reflects
    /// iff its k spans span ≥ 2 sources; under a uniform relabeling the labels on a fixed k-set are a random k-sample
    /// without replacement from the marginal {m_s}, so P(all one source) = Σ_s C(m_s,k)/C(M,k). Iterative product
    /// (no lgamma) — a term underflows to 0 exactly when the deep rule can't be monochromatic, the correct limit.
    static double NullReflectProb(long k, long[] mS, long M)
    {
        if (k <= 1 || M <= 1) return 0.0;   // a single-span (or degenerate) rule can never show 2 sources
        double same = 0;
        foreach (long m in mS)
        {
            if (m < k) continue;
            double term = 1.0;
            for (long i = 0; i < k; i++) { term *= (double)(m - i) / (double)(M - i); if (term < 1e-300) { term = 0; break; } }
            same += term;
        }
        return 1.0 - same;
    }

    static ulong Fnv(ReadOnlySpan<byte> data)
    {
        ulong h = 1469598103934665603UL;
        foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    // ── span-bucket reflection (log2 of byte-span) — the mechanistic axis: reflection horizon is length-driven. ──
    readonly record struct SpanRow(int Log2, int Lo, int NEligible, int NReflReattr, int NReflDag, double MeanDepth);
    static List<SpanRow> SpanBins(int n, int[] depth, int[] span, long[] expLen, int[] breadth, in PearlAudit audit)
    {
        var byBucket = new Dictionary<int, (int elig, int rr, int rdag, long depthSum)>();
        for (int i = 0; i < n; i++)
        {
            if (expLen[i] < Pearl.ReflectFloorBytes) continue;
            int b = span[i] <= 1 ? 0 : (int)Math.Floor(Math.Log2(span[i]));
            var c = byBucket.GetValueOrDefault(b);
            int rr = breadth[i], gs = audit.JewelSources?[i]?.Count ?? 0;
            byBucket[b] = (c.elig + 1, c.rr + (rr >= 2 ? 1 : 0), c.rdag + (gs >= 2 ? 1 : 0), c.depthSum + depth[i]);
        }
        var rows = new List<SpanRow>();
        foreach (var b in byBucket.Keys.OrderBy(x => x))
        {
            var c = byBucket[b];
            rows.Add(new SpanRow(b, 1 << b, c.elig, c.rr, c.rdag, (double)c.depthSum / c.elig));
        }
        return rows;
    }

    // ── the DIET's own depth ladder — per-file batch induction (SIZE-BIASED: kept only as the contrast to the joint
    // read, which is the ladder of record). The KZ boundary (deepest level with ≥ 4 rules) is the structural ceiling. ──
    static List<(string Name, bool Ok, int MaxL, int Kz, int Rules)> DietDepths(string[] paths)
    {
        var outp = new List<(string, bool, int, int, int)>();
        foreach (var p in paths)
        {
            string name = Path.GetFileName(p);
            if (!File.Exists(p)) { outp.Add((name, false, 0, 0, 0)); continue; }
            try { var (ml, kz, nr) = InduceLadder(File.ReadAllBytes(p)); outp.Add((name, true, ml, kz, nr)); }
            catch { outp.Add((name, false, 0, 0, 0)); }
        }
        return outp;
    }

    /// JOINT diet (re-gate 5) — ONE induction over the whole corpus-source view (all files newline-joined). Per-file
    /// batches are size-biased AND jointness-biased (overshoot guaranteed); the joint ladder is the honest world ceiling.
    static (bool Ok, int MaxL, int Kz, int Rules) JointDiet(string[] paths)
    {
        var buf = new List<byte>();
        bool any = false;
        foreach (var p in paths)
            if (File.Exists(p)) { buf.AddRange(File.ReadAllBytes(p)); buf.Add((byte)'\n'); any = true; }
        if (!any) return (false, 0, 0, 0);
        try { var (ml, kz, nr) = InduceLadder(buf.ToArray()); return (true, ml, kz, nr); }
        catch { return (false, 0, 0, 0); }
    }

    static (int MaxL, int Kz, int Rules) InduceLadder(byte[] bytes)
    {
        var (_, _, gd) = global::Cogito.Engine.Induce(bytes);
        var (dd, _) = Engine.RuleDepthSpan(gd);
        int maxL = 0; for (int i = 0; i < dd.Length; i++) if (dd[i] > maxL) maxL = dd[i];
        int kz = 0;
        for (int L = 1; L <= maxL; L++) { int c = 0; for (int i = 0; i < dd.Length; i++) if (dd[i] == L) c++; if (c >= 4) kz = L; }
        return (maxL, kz, gd.Rules.Length);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE BASELINE SLOPE + NULL BAND (re-gate 2) and the LAPLACE DECAY FIT (re-gate 3).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// The pre-blur baseline: the LINEAR real-reattr rate vs depth (OLS over populated bins) against the closed-form
    /// permutation null. `NullSlope` is the slope the null EXPECTS from the k-multiplicity confound alone (source↔
    /// content correlation destroyed); `NullSD` its 2σ sampling band from the per-bin Bernoulli variance. The signal is
    /// the GAP: real inside the null band ⇒ the decay is pure multiplicity (refutation deeper than "flat").
    readonly record struct SlopeBand(int Bins, double RealSlope, double RealSE, double NullSlope, double NullSD, string Verdict);

    static SlopeBand NullSlopeBand(List<DepthRow> rows)
    {
        var pts = rows.Where(r => r.NEligible >= 5).Select(r => (
            x: (double)r.Depth,
            yReal: (double)r.NReflReattr / r.NEligible,
            yNull: r.NullMean / r.NEligible,
            vNull: r.NullVar / ((double)r.NEligible * r.NEligible))).ToList();   // Var(rate) = Σp(1−p)/n²
        if (pts.Count < 3) return new SlopeBand(pts.Count, double.NaN, double.NaN, double.NaN, double.NaN,
            "INSUFFICIENT — too few populated depth bins for a slope (need a deeper run)");
        double xbar = pts.Average(p => p.x);
        double sxx = pts.Sum(p => (p.x - xbar) * (p.x - xbar));
        double realSlope = pts.Sum(p => (p.x - xbar) * p.yReal) / sxx;
        double nullSlope = pts.Sum(p => (p.x - xbar) * p.yNull) / sxx;
        double nullVar = pts.Sum(p => (p.x - xbar) * (p.x - xbar) * p.vNull) / (sxx * sxx);
        double nullSD = Math.Sqrt(nullVar);
        // real-slope SE from the OLS residuals of the real fit
        double bReal = pts.Average(p => p.yReal) - realSlope * xbar;
        double ssRes = pts.Sum(p => { double e = p.yReal - (realSlope * p.x + bReal); return e * e; });
        double realSE = pts.Count > 2 ? Math.Sqrt(ssRes / (pts.Count - 2) / sxx) : double.NaN;
        double z = nullSD > 0 ? (realSlope - nullSlope) / nullSD : 0;
        // GAP LEVEL at the deepest populated bin — the "real≥null at max depth = wall refuted" clause. The gap is
        // negative everywhere source-clustering holds (a csharp idiom recurs in csharp, not rust); the DEPTH SIGNAL is
        // whether that gap WIDENS (real rises slower than the null) — deep rules ever more single-source-memorized.
        var deepest = rows.Where(r => r.NEligible >= 5).OrderByDescending(r => r.Depth).First();
        double deepGap = (deepest.NReflReattr - deepest.NullMean) / deepest.NEligible;
        string verdict =
            Math.Abs(z) < 2
                ? $"REAL-SLOPE ≈ NULL-SLOPE (z={z:F1}σ) — the independence gap is DEPTH-FLAT; source-independence neither strengthens nor weakens with depth (deep is no more memorized than shallow at this measure)"
                : realSlope < nullSlope
                    ? $"THE WALL, MEASURED — real reflection rises SLOWER than the null (z={z:F1}σ), so the independence gap WIDENS with depth (gap {100 * deepGap:+0.0;-0.0}pt at max depth): deep rules increasingly fail cross-source corroboration — memorized single-source literals, exactly the depth thesis, now against a null the old mono-label read could not see"
                    : $"WALL REFUTED — real reflection rises FASTER than the null (z={z:F1}σ), gap {100 * deepGap:+0.0;-0.0}pt at max depth: deep rules GAIN independence (blur may be unnecessary at this measure)";
        return new SlopeBand(pts.Count, realSlope, realSE, nullSlope, nullSD, verdict);
    }

    /// The exponential-decay DESCRIPTOR — log(Laplace rate) vs depth, least-squares over POPULATED bins (re-gate 3):
    /// zero-reflect bins enter via the Jeffreys rate (refl+0.5)/(elig+1) instead of being censored, and the shallow
    /// anchor is the FIRST TWO populated bins (not a structurally-empty d≤3 window). A descriptor, not the verdict —
    /// the verdict is the real-vs-null GAP above; a bare "it decays" proves nothing (the null decays too).
    readonly record struct Fit(string Label, int Points, double Slope, double R2, double ShallowPct, double DeepPct, double Ratio);
    static Fit FitDecayLaplace(List<DepthRow> rows, Func<DepthRow, int> refl, string label)
    {
        var pop = rows.Where(r => r.NEligible >= 5).OrderBy(r => r.Depth).ToList();
        var pts = pop.Select(r => (x: (double)r.Depth, y: Math.Log((refl(r) + 0.5) / (r.NEligible + 1.0)))).ToList();
        double slope = double.NaN, r2 = double.NaN;
        if (pts.Count >= 3)
        {
            int k = pts.Count; double sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (var (x, y) in pts) { sx += x; sy += y; sxx += x * x; sxy += x * y; }
            slope = (k * sxy - sx * sy) / (k * sxx - sx * sx);
            double b = (sy - slope * sx) / k, ssTot = 0, ssRes = 0, my = sy / k;
            foreach (var (x, y) in pts) { double pred = slope * x + b; ssRes += (y - pred) * (y - pred); ssTot += (y - my) * (y - my); }
            r2 = ssTot > 0 ? 1 - ssRes / ssTot : double.NaN;
        }
        // shallow = first two populated bins, deep = last two — anchored to real data, not a fixed window
        double ShPct(IEnumerable<DepthRow> rs) { int rr = 0, el = 0; foreach (var r in rs) { rr += refl(r); el += r.NEligible; } return el > 0 ? 100.0 * rr / el : 0; }
        double shPct = ShPct(pop.Take(2));
        double dpPct = ShPct(pop.AsEnumerable().Reverse().Take(2));
        double ratio = dpPct > 0 ? shPct / dpPct : double.PositiveInfinity;
        return new Fit(label, pts.Count, slope, r2, shPct, dpPct, ratio);
    }

    /// DEPTH-BEYOND-LENGTH (re-gate 4) — a 2-predictor OLS of reflect∈{0,1} on [1, depth, log₂span] over the eligible
    /// rule cloud. `PartDepth` (=β_depth) is the depth slope AT FIXED span; if it collapses toward 0 against the
    /// marginal `MargDepth`, the axis is LENGTH not depth and the cure forks AWAY from blur (sub-span credit, not slots).
    readonly record struct Strat(double MargDepth, double PartDepth, double MargSpan, double PartSpan, string Verdict);
    static Strat Partial2D(int n, int[] depth, int[] span, long[] expLen, int[] breadth)
    {
        // accumulate the normal-equation sums over eligible rules; x1 = depth, x2 = log2(span), y = reflect(0/1)
        double N = 0, sx1 = 0, sx2 = 0, sx11 = 0, sx22 = 0, sx12 = 0, sy = 0, sx1y = 0, sx2y = 0;
        for (int i = 0; i < n; i++)
        {
            if (expLen[i] < Pearl.ReflectFloorBytes) continue;
            double x1 = depth[i], x2 = span[i] <= 1 ? 0 : Math.Log2(span[i]), y = breadth[i] >= 2 ? 1 : 0;
            N++; sx1 += x1; sx2 += x2; sx11 += x1 * x1; sx22 += x2 * x2; sx12 += x1 * x2; sy += y; sx1y += x1 * y; sx2y += x2 * y;
        }
        if (N < 4) return new Strat(double.NaN, double.NaN, double.NaN, double.NaN, "INSUFFICIENT — too few eligible rules to stratify");
        // marginal slopes (single-predictor)
        double margDepth = (N * sx1y - sx1 * sy) / (N * sx11 - sx1 * sx1);
        double margSpan = (N * sx2y - sx2 * sy) / (N * sx22 - sx2 * sx2);
        // partial slopes — solve the 3×3 normal equations [N sx1 sx2; sx1 sx11 sx12; sx2 sx12 sx22] b = [sy sx1y sx2y]
        var (b1, b2) = Solve3(N, sx1, sx2, sx11, sx12, sx22, sy, sx1y, sx2y);
        string verdict = double.IsNaN(b1)
            ? "SINGULAR — depth and span are collinear at this grammar (cannot separate)"
            : Math.Abs(b1) < 0.25 * Math.Abs(margDepth) || (margDepth < 0 && b1 >= 0)
                ? "LENGTH AXIS — the depth slope collapses once span is controlled; deep≈long, the cure is sub-span witness credit + upward compositional trust, NOT the blur"
                : Math.Sign(b1) == Math.Sign(margDepth)
                    ? "DEPTH AXIS — the depth slope survives span control; depth is a genuine axis beyond mere length (blur is on-target)"
                    : "MIXED — depth flips sign under span control; the axes are entangled, read the null-gap per stratum before committing the cure";
        return new Strat(margDepth, b1, margSpan, b2, verdict);
    }

    // solve the symmetric 3×3 [[a00 a01 a02][a01 a11 a12][a02 a12 a22]] x = [r0 r1 r2] via Cramer; return (x1, x2).
    static (double B1, double B2) Solve3(double a00, double a01, double a02, double a11, double a12, double a22,
                                         double r0, double r1, double r2)
    {
        double Det(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
            => m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
        double det = Det(a00, a01, a02, a01, a11, a12, a02, a12, a22);
        if (Math.Abs(det) < 1e-12) return (double.NaN, double.NaN);
        double d1 = Det(a00, r0, a02, a01, r1, a12, a02, r2, a22);   // replace col 1 with r
        double d2 = Det(a00, a01, r0, a01, a11, r1, a02, a12, r2);   // replace col 2 with r
        return (d1 / det, d2 / det);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  WRITERS.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    static void WriteP1Tsv(string path, List<DepthRow> rows)
    {
        var sb = new StringBuilder("depth\tn_all\tn_eligible\tn_refl_direct\tn_refl_dag\tn_refl_reattr\tnull_mean\tnull_var\tn_sawreal\treflect_reattr\treflect_null\tgap\tmean_span\tmean_k\tmean_breadth\tmeanz_level\n");
        foreach (var r in rows)
        {
            double real = r.NEligible > 0 ? (double)r.NReflReattr / r.NEligible : double.NaN;
            double nul = r.NEligible > 0 ? r.NullMean / r.NEligible : double.NaN;
            sb.Append(CultureInfo.InvariantCulture, $"{r.Depth}\t{r.NAll}\t{r.NEligible}\t{r.NReflDirect}\t{r.NReflDag}\t{r.NReflReattr}\t{r.NullMean:F4}\t{r.NullVar:F4}\t{r.NSawReal}\t{F4(real)}\t{F4(nul)}\t{F4(real - nul)}\t{r.MeanSpan:F2}\t{r.MeanK:F2}\t{r.MeanBreadth:F3}\t{(r.MeanzLevel is double m ? m.ToString("F4", CultureInfo.InvariantCulture) : "nan")}\n");
        }
        File.WriteAllText(path, sb.ToString());
    }
    static void WriteSpanTsv(string path, List<SpanRow> rows)
    {
        var sb = new StringBuilder("log2_span\tspan_lo\tn_eligible\tn_refl_reattr\tn_refl_dag\treflect_reattr\treflect_dag\tmean_depth\n");
        foreach (var r in rows)
            sb.Append(CultureInfo.InvariantCulture, $"{r.Log2}\t{r.Lo}\t{r.NEligible}\t{r.NReflReattr}\t{r.NReflDag}\t{Rate(r.NReflReattr, r.NEligible)}\t{Rate(r.NReflDag, r.NEligible)}\t{r.MeanDepth:F2}\n");
        File.WriteAllText(path, sb.ToString());
    }
    static void WriteSlopeTsv(string path, in SlopeBand s, in Fit fit, in Strat st, int maxDepth, int dietMaxL, double withinPct, double abovePct)
    {
        var sb = new StringBuilder("# P2 pre-blur baseline slope — real reattr rate vs depth against the closed-form permutation null\n");
        sb.Append(CultureInfo.InvariantCulture, $"real_slope\t{s.RealSlope:F6}\nreal_se\t{s.RealSE:F6}\nreal_ci_lo\t{s.RealSlope - 1.96 * s.RealSE:F6}\nreal_ci_hi\t{s.RealSlope + 1.96 * s.RealSE:F6}\n");
        sb.Append(CultureInfo.InvariantCulture, $"null_slope\t{s.NullSlope:F6}\nnull_sd\t{s.NullSD:F6}\nnull_band_lo\t{s.NullSlope - 2 * s.NullSD:F6}\nnull_band_hi\t{s.NullSlope + 2 * s.NullSD:F6}\n");
        sb.Append(CultureInfo.InvariantCulture, $"decay_k_log\t{fit.Slope:F6}\ndecay_r2\t{fit.R2:F4}\nshallow_pct\t{fit.ShallowPct:F3}\ndeep_pct\t{fit.DeepPct:F3}\n");
        sb.Append(CultureInfo.InvariantCulture, $"marg_k_depth\t{st.MargDepth:F6}\npart_k_depth_given_span\t{st.PartDepth:F6}\nmarg_k_span\t{st.MargSpan:F6}\npart_k_span_given_depth\t{st.PartSpan:F6}\n");
        sb.Append(CultureInfo.InvariantCulture, $"mesh_maxdepth\t{maxDepth}\njoint_diet_maxdepth\t{dietMaxL}\nwithin_diet_pct\t{withinPct:F3}\nabove_diet_pct\t{abovePct:F3}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# verdict(slope): {s.Verdict}\n# verdict(strat): {st.Verdict}\n");
        File.WriteAllText(path, sb.ToString());
    }
    static void WriteP3Tsv(string path, List<DepthRow> rows, List<(string Name, bool Ok, int MaxL, int Kz, int Rules)> diet, (bool Ok, int MaxL, int Kz, int Rules) joint, int meshKz)
    {
        var sb = new StringBuilder("# mesh depth ladder — per-level Zipf slope + census\n");
        sb.Append("depth\tn_rules\tn_eligible\tmeanz_level\treflect_reattr\n");
        foreach (var r in rows)
            sb.Append(CultureInfo.InvariantCulture, $"{r.Depth}\t{r.NAll}\t{r.NEligible}\t{(r.MeanzLevel is double m ? m.ToString("F4", CultureInfo.InvariantCulture) : "nan")}\t{Rate(r.NReflReattr, r.NEligible)}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# mesh KZ boundary (deepest ≥4-rule level): {meshKz}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# JOINT diet: ok={joint.Ok} maxDepth={joint.MaxL} KZ={joint.Kz} rules={joint.Rules}\n");
        foreach (var d in diet) sb.Append(CultureInfo.InvariantCulture, $"# per-file {d.Name}: ok={d.Ok} maxDepth={d.MaxL} KZ={d.Kz} rules={d.Rules}\n");
        File.WriteAllText(path, sb.ToString());
    }
    static void WriteRuleTrace(string path, in RePairResult g, int[] depth, int[] span, long[] expLen, long[] kUnits, int[] breadth, in PearlAudit audit)
    {
        var sb = new StringBuilder("rule\tdepth\tspan\texplen\tk_units\treattr_breadth\tdag_srcs\tsaw_real\n");
        for (int i = 0; i < g.Rules.Length; i++)
            sb.Append(CultureInfo.InvariantCulture, $"N{256 + i}\t{depth[i]}\t{span[i]}\t{expLen[i]}\t{kUnits[i]}\t{breadth[i]}\t{audit.JewelSources?[i]?.Count ?? 0}\t{(audit.SawReal[i] ? 1 : 0)}\n");
        File.WriteAllText(path, sb.ToString());
    }
    static void WriteMarginTsv(string path, in SlotMargin margin)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# slot_classes\t{margin.Classes}\n# pooled_rules\t{margin.PooledRules}\n# newly_reflect_deep\t{margin.NewlyReflectDeep}\n# null_deep_gain\t{margin.NullDeepGain:F4}\n# mean_margin\t{margin.MeanMargin:F4}\n# shape\t{margin.Shape}\n");
        sb.Append("rule\tdepth\texplen\tclass_size\town_breadth\tslot_breadth\treal_gain\trandom_floor_gain\tmargin\trandom_reflect_prob\n");
        foreach (var r in margin.Rows.OrderByDescending(r => r.Margin).ThenBy(r => r.Rule))
            sb.Append(CultureInfo.InvariantCulture, $"N{256 + r.Rule}\t{r.Depth}\t{r.ExpLen}\t{r.ClassSize}\t{r.OwnBreadth}\t{r.SlotBreadth}\t{r.RealGain:F4}\t{r.NullGain:F4}\t{r.Margin:F4}\t{r.NullReflectProb:F4}\n");
        File.WriteAllText(path, sb.ToString());
    }
    static void WriteSlotCoreTsv(string path, List<SlotCoreRow> rows)
    {
        var sb = new StringBuilder("rule\tmode\tdepth\texplen\tclass_size\town_breadth\tslot_breadth\tmargin\tfillers\theldout_real_pay\theldout_null_pay\treal_shrinks\tnull_shrinks\tsurvives\tfiller\tframe\texpansion\tfillers_preview\tmate_preview\n");
        foreach (var r in rows)
            sb.Append(CultureInfo.InvariantCulture,
                $"N{256 + r.Rule}\t{r.Mode}\t{r.Depth}\t{r.ExpLen}\t{r.ClassSize}\t{r.OwnBreadth}\t{r.SlotBreadth}\t{r.Margin:F4}\t{r.Fillers}\t{r.RealPay:F3}\t{r.NullPay:F3}\t{(r.RealShrinks ? 1 : 0)}\t{(r.NullShrinks ? 1 : 0)}\t{(r.Survives ? 1 : 0)}\t{Tsv(r.Filler)}\t{Tsv(r.Frame)}\t{Tsv(r.Expansion)}\t{Tsv(r.FillersPreview)}\t{Tsv(r.MatePreview)}\n");
        File.WriteAllText(path, sb.ToString());
    }

    static void WriteSlotModesTsv(string path, List<SlotCoreRow> rows)
    {
        var sb = new StringBuilder("# high mode = margin >= 1.5; low mode = margin < 1.5\nmode\trows\tclasses\treal_shrink\tnull_shrink\tsurvives\treal_shrink_rate\tnull_shrink_rate\tsurvive_rate\tmean_real_pay\tmean_null_pay\tmean_margin\n");
        foreach (var r in SummarizeSlotModes(rows))
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Mode}\t{r.Rows}\t{r.Classes}\t{r.RealShrink}\t{r.NullShrink}\t{r.Survives}\t{Rate(r.RealShrink, r.Rows)}\t{Rate(r.NullShrink, r.Rows)}\t{Rate(r.Survives, r.Rows)}\t{r.MeanRealPay:F3}\t{r.MeanNullPay:F3}\t{r.MeanMargin:F4}\n");
        File.WriteAllText(path, sb.ToString());
    }

    static void WriteHtml(string path, string runDir, int nRules, int wScale, List<DepthRow> rows, List<SpanRow> spanRows,
        SlopeBand slope, Fit fitReattr, Fit fitDag, Strat strat, List<(string Name, bool Ok, int MaxL, int Kz, int Rules)> diet,
        (bool Ok, int MaxL, int Kz, int Rules) joint, int meshKz, int maxDepth, int dietMaxL, double withinPct, double abovePct)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><meta charset=utf-8><title>depth autopsy</title>");
        sb.Append("<style>body{font:14px/1.5 ui-monospace,monospace;background:#0d0f12;color:#d6dae0;max-width:920px;margin:2rem auto;padding:0 1rem}h1{font-size:18px}h2{font-size:15px;color:#8fd0ff;margin-top:2rem}.v{padding:.5rem .8rem;border-radius:6px;background:#161a20;border-left:3px solid #f0a}table{border-collapse:collapse;font-size:12px}td,th{padding:2px 8px;text-align:right}th{color:#7a8290}svg{background:#12151a;border-radius:6px}.d{color:#f5a}.g{color:#5bd}.n{color:#fa0}</style>");
        sb.Append(CultureInfo.InvariantCulture, $"<h1>depth-profile autopsy (monk-re-gated)</h1><p>{runDir} · {nRules} rules · wScale {wScale} · maxDepth {maxDepth} · joint-diet depth {dietMaxL}</p>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=v><b>P2 baseline slope:</b> {slope.Verdict}<br>real β={slope.RealSlope:F5} (95% CI ±{1.96 * slope.RealSE:F5}) · <span class=n>null band</span> {slope.NullSlope:F5} ± {2 * slope.NullSD:F5}<br><b>decay:</b> reattr k={fitReattr.Slope:F3}/level (R²={fitReattr.R2:F3}), shallow {fitReattr.ShallowPct:F1}% → deep {fitReattr.DeepPct:F1}%<br><b>depth-beyond-length:</b> {strat.Verdict}<br>marginal k_depth={strat.MargDepth:F4} → partial k_depth|span={strat.PartDepth:F4}<br><b>P1↔P3 bridge:</b> reattr reflection within joint-diet ladder (d≤{dietMaxL}) = {withinPct:F1}% vs above it = {abovePct:F1}%</div>");

        sb.Append("<h2>P1 — reflection-rate vs rule depth <span class=g>■ real (reattr)</span> <span class=n>■ null (permutation)</span> <span class=d>■ dag</span></h2>");
        sb.Append(DepthChart(rows));

        sb.Append("<h2>P1 — reflection-rate vs byte-span (log₂)</h2>");
        sb.Append(SpanChart(spanRows));

        sb.Append("<h2>P3 — per-level Zipf slope (criticality) across the depth ladder</h2>");
        sb.Append(MeanzChart(rows, meshKz, joint));

        sb.Append("<h2>P3 — collapse-depth vs diet-depth</h2><table><tr><th>source<th>maxDepth<th>KZ boundary<th>rules</tr>");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><td>MESH (converged)<td>{maxDepth}<td>{meshKz}<td>{nRules}</tr>");
        if (joint.Ok) sb.Append(CultureInfo.InvariantCulture, $"<tr><td><b>JOINT diet</b><td>{joint.MaxL}<td>{joint.Kz}<td>{joint.Rules}</tr>");
        foreach (var d in diet)
            sb.Append(d.Ok ? $"<tr><td>per-file {d.Name}<td>{d.MaxL}<td>{d.Kz}<td>{d.Rules}</tr>" : $"<tr><td>per-file {d.Name}<td colspan=3>unreadable</tr>");
        sb.Append("</table>");

        sb.Append("<h2>P1 table</h2><table><tr><th>depth<th>n_all<th>n_elig<th>real%<th>null%<th>gap<th>dag%<th>mean_k<th>meanz</tr>");
        foreach (var r in rows)
        {
            if (r.NAll == 0) continue;
            double real = r.NEligible > 0 ? 100.0 * r.NReflReattr / r.NEligible : double.NaN;
            double nul = r.NEligible > 0 ? 100.0 * r.NullMean / r.NEligible : double.NaN;
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{r.Depth}<td>{r.NAll}<td>{r.NEligible}<td>{PctH(r.NReflReattr, r.NEligible)}<td>{nul:F1}%<td>{real - nul:+0.0;-0.0}<td>{PctH(r.NReflDag, r.NEligible)}<td>{r.MeanK:F1}<td>{(r.MeanzLevel is double m ? m.ToString("F3", CultureInfo.InvariantCulture) : "—")}</tr>");
        }
        sb.Append("</table>");
        File.WriteAllText(path, sb.ToString());
    }

    // simple inline-SVG line charts (no external deps — the strict-CSP-safe self-contained render)
    static string DepthChart(List<DepthRow> rows)
    {
        var pop = rows.Where(r => r.NEligible >= 1).ToList();
        if (pop.Count == 0) return "<p>(no eligible rules)</p>";
        int maxD = pop.Max(r => r.Depth);
        Func<DepthRow, double> real = r => r.NEligible > 0 ? (double)r.NReflReattr / r.NEligible : 0;
        Func<DepthRow, double> nul = r => r.NEligible > 0 ? r.NullMean / r.NEligible : 0;
        Func<DepthRow, double> dag = r => r.NEligible > 0 ? (double)r.NReflDag / r.NEligible : 0;
        return LineChart3(pop, r => r.Depth, maxD, real, nul, dag, "depth", 1.0);
    }
    static string SpanChart(List<SpanRow> rows)
    {
        if (rows.Count == 0) return "<p>(no eligible rules)</p>";
        int maxB = rows.Max(r => r.Log2);
        return LineChart3(rows, r => r.Log2, maxB, r => r.NEligible > 0 ? (double)r.NReflReattr / r.NEligible : 0,
            _ => -1, r => r.NEligible > 0 ? (double)r.NReflDag / r.NEligible : 0, "log₂(span)", 1.0);
    }
    static string LineChart3<T>(List<T> rows, Func<T, int> x, int maxX, Func<T, double> yReal, Func<T, double> yNull, Func<T, double> yDag, string xlab, double maxY)
    {
        const int W = 860, H = 240, ml = 44, mb = 28, mt = 10, mr = 10;
        double px(int xi) => ml + (double)xi / Math.Max(1, maxX) * (W - ml - mr);
        double py(double yv) => mt + (1 - yv / maxY) * (H - mt - mb);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg width={W} height={H} viewBox='0 0 {W} {H}'>");
        for (int gy = 0; gy <= 4; gy++) { double yy = py(gy / 4.0); sb.Append(CultureInfo.InvariantCulture, $"<line x1={ml} y1={yy:F1} x2={W - mr} y2={yy:F1} stroke='#222' /><text x=6 y={yy + 4:F1} fill='#7a8290' font-size=10>{gy * 25}%</text>"); }
        string Path(Func<T, double> y, string col, bool dash)
        {
            var p = new StringBuilder(); var dots = new StringBuilder(); bool any = false;
            for (int i = 0; i < rows.Count; i++)
            {
                double yv = y(rows[i]); if (yv < 0) continue;   // sentinel: skip (null-less charts)
                p.Append(CultureInfo.InvariantCulture, $"{(any ? "L" : "M")}{px(x(rows[i])):F1},{py(yv):F1} ");
                dots.Append(CultureInfo.InvariantCulture, $"<circle cx={px(x(rows[i])):F1} cy={py(yv):F1} r=2.5 fill='{col}' />");
                any = true;
            }
            return any ? $"<path d='{p}' fill=none stroke='{col}' stroke-width=1.8 {(dash ? "stroke-dasharray='4 3'" : "")} />{dots}" : "";
        }
        sb.Append(Path(yNull, "#ffaa00", true));
        sb.Append(Path(yDag, "#5bd6d0", false));
        sb.Append(Path(yReal, "#ff55aa", false));
        sb.Append(CultureInfo.InvariantCulture, $"<text x={W / 2} y={H - 6} fill='#7a8290' font-size=11 text-anchor=middle>{xlab}</text></svg>");
        return sb.ToString();
    }
    static string MeanzChart(List<DepthRow> rows, int meshKz, (bool Ok, int MaxL, int Kz, int Rules) joint)
    {
        var pop = rows.Where(r => r.MeanzLevel is double).ToList();
        if (pop.Count == 0) return "<p>(no scored levels)</p>";
        int maxD = rows.Max(r => r.Depth);
        const int W = 860, H = 240, ml = 44, mb = 28, mt = 10, mr = 10;
        double lo = -1.6, hi = 0.0;   // Zipf slope band (−0.70 universality sits mid)
        double px(int xi) => ml + (double)xi / Math.Max(1, maxD) * (W - ml - mr);
        double py(double yv) => mt + (1 - (yv - lo) / (hi - lo)) * (H - mt - mb);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg width={W} height={H} viewBox='0 0 {W} {H}'>");
        double band = py(-0.70);
        sb.Append(CultureInfo.InvariantCulture, $"<line x1={ml} y1={band:F1} x2={W - mr} y2={band:F1} stroke='#3a5' stroke-dasharray='4 3' /><text x=6 y={band + 4:F1} fill='#3a5' font-size=10>−0.70</text>");
        if (joint.Ok && joint.Kz > 0) { double dx = px(joint.Kz); sb.Append(CultureInfo.InvariantCulture, $"<line x1={dx:F1} y1={mt} x2={dx:F1} y2={H - mb} stroke='#fa0' stroke-dasharray='4 3' /><text x={dx + 3:F1} y={mt + 12} fill='#fa0' font-size=10>joint-diet KZ {joint.Kz}</text>"); }
        double mx = px(meshKz); sb.Append(CultureInfo.InvariantCulture, $"<line x1={mx:F1} y1={mt} x2={mx:F1} y2={H - mb} stroke='#f5a' stroke-dasharray='2 2' /><text x={mx + 3:F1} y={H - mb - 4:F1} fill='#f5a' font-size=10>mesh KZ {meshKz}</text>");
        var p = new StringBuilder(); bool first = true;
        foreach (var r in rows) if (r.MeanzLevel is double m) { p.Append(CultureInfo.InvariantCulture, $"{(first ? "M" : "L")}{px(r.Depth):F1},{py(m):F1} "); first = false; sb.Append(CultureInfo.InvariantCulture, $"<circle cx={px(r.Depth):F1} cy={py(m):F1} r=2.5 fill='#8fd0ff' />"); }
        sb.Insert(sb.Length, $"<path d='{p}' fill=none stroke='#8fd0ff' stroke-width=1.6 />");
        sb.Append(CultureInfo.InvariantCulture, $"<text x={W / 2} y={H - 6} fill='#7a8290' font-size=11 text-anchor=middle>depth</text></svg>");
        return sb.ToString();
    }

    // ── formatting helpers ──
    static double? Nan(double v) => double.IsNaN(v) ? null : v;
    static string Sgn1(double v) => double.IsNaN(v) ? "  nan" : v.ToString("+0.0;-0.0", CultureInfo.InvariantCulture);
    static string Sgn5(double v) => double.IsNaN(v) ? "nan" : v.ToString("+0.00000;-0.00000", CultureInfo.InvariantCulture);
    static string F4(double v) => double.IsNaN(v) ? "nan" : v.ToString("F4", CultureInfo.InvariantCulture);
    static string Rate(int a, int b) => b > 0 ? ((double)a / b).ToString("F4", CultureInfo.InvariantCulture) : "nan";
    static string Pct(int a, int b) => b > 0 ? $"{100.0 * a / b,5:F1}%" : "   —  ";
    static string PctH(int a, int b) => b > 0 ? $"{100.0 * a / b:F1}%" : "—";
    static string Tsv(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    static string EscapeText(string s, int max)
    {
        var sb = new StringBuilder(Math.Min(max, s.Length));
        foreach (char ch in s)
        {
            if (sb.Length >= max) break;
            sb.Append(char.IsControl(ch) ? ' ' : ch);
        }
        if (s.Length > max) sb.Append("...");
        return sb.ToString();
    }
    static ulong FnvString(string s)
    {
        ulong h = 1469598103934665603UL;
        foreach (char ch in s)
        {
            h ^= (byte)ch; h *= 1099511628211UL;
            h ^= (byte)(ch >> 8); h *= 1099511628211UL;
        }
        return h;
    }
    static string MarginBinLabel(int b) => b switch
    {
        0 => "<= 0",
        1 => "(0, .25]",
        2 => "(.25, .75]",
        3 => "(.75, 1.5]",
        4 => "(1.5, 3]",
        _ => "> 3",
    };
}
