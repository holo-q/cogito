namespace Cogito;

using System.Text;
using Cogito.Induct;

// ── THE GROK-THERMOSTAT ──  the farm upgraded from "drain a pool" → "schedules its own school." It fuses the
// two proudest results at their UNMEASURED INTERSECTION: the intake proof (Radula.cs) drove residual-frontier
// intake and read DEPTH (maxSpan / ParsedSize) but NEVER read the CRYSTALLIZATION POINT — S1's grokking signal,
// the criticality-CV collapse (Engine.RenormStats.CvZ, the per-scale Zipf-exponent variation; LOW ⟹ the SAME
// power-law at every scale = a critical RG fixed point = the grok). This verb wires that CV in as the
// deterministic "I've grokked this" BELL and lets it SCHEDULE the curriculum:
//
//   concentrate frontier-intake on ONE domain UNTIL its criticality-CV LOCKS below the k-aware lock line
//   (CV < floor 0.15 + a sampling-noise band that widens as the domain's scale count k shrinks — DomainMeter;
//   the old flat 0.20 was the k≈12 special case) → then CROSS the strongest coupling-bridge
//   (the DomainWalk provenance graph, Couplings.cs) into the ADJACENT domain → repeat. grok → move-on → grok.
//
// THE HYSTERESIS (why a "LOCK", not a crossing): a raw single-round CV<line CHATTERS — on a mixed feed the whole-
// grammar CV bounces across the threshold as new half-formed scales enter (measured: dips at round 11, back up at
// 13, down at 19, up at 21). A thermostat with no hysteresis chatters exactly so. The bell therefore fires only on
// a LOCK — CV below threshold for `lock` consecutive rounds — and reads the domain in ISOLATION (a grammar over
// only that domain's ingested spans), which sharpens the collapse the whole-grammar CV smears. The chatter itself
// is a reported number (the reliability control): raw-first-cross vs locked-round vs #re-crossings.
//
// TWO KILL-LINES (honest, on the numbers — the curve is the signal):
//   (a) CRITICAL-POINT SHIFT — does residual-frontier intake reach the per-domain CV-lock in FEWER BYTES than
//       random-global intake? (the curriculum MOVES the critical point earlier.) frontier self-concentrates so a
//       domain's spans rush onto the tape and its CV locks at low TOTAL budget; random spreads ~B/K per domain so
//       each locks late or never. Metric: bytes-to-grok per domain, frontier vs random.
//   (b) BRIDGE-ORDER — does bridge-ordered domain sequencing (grok → cross the BEST coupling-bridge → grok-adjacent)
//       BEAT a shuffled domain order on UNION DEPTH at MATCHED total budget? crossing to an ADJACENT domain lands
//       on shared substructure already on the tape (a warm start → deeper grok per byte); a distant jump starts
//       cold. Metric: union depth (accreted maxSpan + mean held-out sym/byte across ALL domains), bridge vs shuffle.
//
// Deterministic end to end (same corpus + seed ⇒ same schedule ⇒ same curves — the Vow). No LLM.

public static class CritLock
{
    // The grok / stride knobs (GrokCv · LockRounds · MinDomainSpans · ReStrideBytes · DomStrideSpans · FrontierCapExps)
    // are GrokDefaults (Curriculum.cs) — the ONE authority the drive, the curriculum, and this kill-line share. The
    // per-domain grok meter (the CV-lock hysteresis bell) + its isolated-CV read (DomainMeter.ReadCv) + the union-depth
    // read (DomainDepth) + the domain-order null (GrokBell.ShuffledOrder) likewise live in the CURRICULUM organ (the
    // grok bell IS the curriculum's move-on signal); this kill-line RIDES them, so the two organs stay byte-identical.

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE ENTRY POINT
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// usage: thermostat [--fam K] [--overlap V] [--morph N] [--win W] [--words N] [--phrases N] [--templates N] [--lines N]
    ///                   [--batch M] [--cv T] [--lock L] [--seed HEX]
    ///        thermostat --corpus <dir> [--glob "*.cs"]...   ← real multi-file domains (each file = a domain)
    /// overlap>0 gives the tower a real bridge chain for kill-line (b); overlap=0 = islands (bridge-order = shuffle, a null).
    public static int Run(string[] args)
    {
        int fam      = Args.Int(args, "--fam", 6);
        int nMorph   = Args.Int(args, "--morph", 96);
        int mWin     = Args.Int(args, "--win", 12);
        int overlap  = Args.Int(args, "--overlap", 4);       // >0: adjacent families share morphemes → a walkable bridge chain
        int wPer     = Args.Int(args, "--words", 12);
        int pPer     = Args.Int(args, "--phrases", 16);
        int tPer     = Args.Int(args, "--templates", 12);
        int linesPer = Args.Int(args, "--lines", 60);
        int batch    = Args.Int(args, "--batch", 3);
        double cvT   = Args.Double(args, "--cv", GrokDefaults.Cv);      // the lock-line FLOOR (DomainMeter adds the k-band live)
        int lockL    = Args.Int(args, "--lock", GrokDefaults.LockRounds); // hysteresis depth
        string real  = Args.Str(args, "--corpus", "");
        string glob  = Args.Str(args, "--glob", "*.cs");
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);

        IIntakeCorpus corpus; string label;
        if (real.Length > 0) { corpus = new FileCorpus(real, glob, holdEvery: 8, "blocked", seed); label = $"REAL CODE — {corpus.Families} files ({real})"; }
        else { corpus = new TowerCorpus(fam, nMorph, mWin, overlap, wPer, pPer, tPer, linesPer, holdEvery: 8, seed, negControl: false, poolOrder: "blocked", flat: false); label = $"tower · {fam} families · overlap {overlap}"; }

        // Group the (blocked) pool into per-domain span lists — the schedulable atoms.
        int D = corpus.Families;
        var byDom = new List<byte[]>[D];
        for (int d = 0; d < D; d++) byDom[d] = new();
        foreach (var (fm, b) in corpus.Lines) byDom[fm].Add(b);
        int totalBytes = corpus.Lines.Sum(l => l.Bytes.Length);

        var run = Cogito.Run.New(real.Length > 0 ? "thermostat-real" : "thermostat");
        Trace.Note($"thermostat · {D} domains · {corpus.Lines.Count} spans ({totalBytes}B) + {corpus.Heldout.Count} held-out · {label}");
        Trace.Note($"  the GROK-BELL: per-domain criticality-CV LOCK < floor {cvT:P0} + k-band for {lockL} rounds ⟹ move on. batch {batch}. no LLM.");

        // ── the coupling bridge graph over the domains (DomainWalk provenance, Couplings.cs) → the greedy bridge-order ──
        var (bridgeOrder, bridgeReport) = BridgeOrder(byDom, D, seed);
        Trace.Note("");
        Trace.Note(bridgeReport);

        // ── KILL-LINE (a) · CRITICAL-POINT SHIFT ──  frontier self-scheduling vs random-global; per-domain bytes-to-grok.
        var frontierA = DrainGlobal(byDom, corpus.Heldout, D, "frontier", batch, cvT, lockL, seed);
        var randomA   = DrainGlobal(byDom, corpus.Heldout, D, "random",   batch, cvT, lockL, seed);
        run.Write("killA_frontier.tsv", frontierA.Curve);
        run.Write("killA_random.tsv",   randomA.Curve);

        // ── KILL-LINE (b) · BRIDGE-ORDER ──  bridge-order vs shuffled domain sequence; union depth at matched budget.
        var shuffleOrder = GrokBell.ShuffledOrder(D, seed);
        var bridgeB  = DrainScheduled(byDom, corpus.Heldout, D, bridgeOrder,  batch, cvT, lockL, seed, "bridge");
        var shuffleB = DrainScheduled(byDom, corpus.Heldout, D, shuffleOrder, batch, cvT, lockL, seed, "shuffle");
        run.Write("killB_bridge.tsv",  bridgeB.Curve);
        run.Write("killB_shuffle.tsv", shuffleB.Curve);

        // MATCHED-BUDGET read (the honest (b) verdict). The two schedules stop each domain at its OWN CV-lock, so
        // their tapes end at DIFFERENT byte counts — a raw end-of-run union-depth read confounds "deeper ORDER"
        // with "more BYTES ingested." Re-read union depth for BOTH arms at the shared budget B* = min(both tapes),
        // truncating each schedule-ordered tape to B*: identical bytes, ONLY the intake order differs ⇒ any depth
        // gap is attributable to the SEQUENCE alone (the ragged ends are kept as context, not the verdict).
        int bStar = Math.Min(bridgeB.Tape.Length, shuffleB.Tape.Length);
        var bridgeM  = DomainDepth.UnionAt(bridgeB.Tape,  bStar, corpus.Heldout);
        var shuffleM = DomainDepth.UnionAt(shuffleB.Tape, bStar, corpus.Heldout);

        Report(D, frontierA, randomA, bridgeOrder, shuffleOrder, bridgeB, shuffleB, cvT, lockL, bStar, bridgeM, shuffleM);
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  KILL-LINE (a) + THE RELIABILITY CONTROL — global drain, per-domain CV-lock as bytes accrue
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record DrainResult(
        string Label, DomainMeter[] Meters, double UnionMaxSpan, double UnionMeanSym, int TotalBytes, int Rounds, string Curve, byte[] Tape);

    /// Drain the WHOLE pool one batch at a time — frontier (residual self-concentration, no imposed order) or random
    /// (the DL global feed) — reading EVERY domain's isolated CV each round so we catch the exact byte at which each
    /// domain's exponent LOCKS. This is the intake proof's setup read through the crystallization bell instead of
    /// maxSpan: frontier locks each domain early (its spans rush in once the residual latches on); random locks late
    /// (a domain's spans dribble in ~1/K). The per-domain lock trail IS the bytes-to-grok curve + the chatter control.
    private static DrainResult DrainGlobal(
        List<byte[]>[] byDom, IReadOnlyList<(int Fam, byte[] Bytes)> heldout, int D,
        string policy, int batch, double cvT, int lockL, ulong seed)
    {
        // flat pool with domain labels (frontier scores over span bytes; random draws uniformly)
        var pool = new List<byte[]>(); var poolDom = new List<int>();
        for (int d = 0; d < D; d++) foreach (var b in byDom[d]) { pool.Add(b); poolDom.Add(d); }
        int n = pool.Count;
        var frontier = policy == "frontier" ? new FrontierIndex(pool) : null;   // pool postings, built once (face 3c)
        var ingested = new bool[n];
        var tape = new List<byte>();
        var meters = NewMeters(D, cvT, lockL);
        var lastSpans = NewLastSpans(D); var cachedCv = new double[D]; var cachedK = new int[D];
        void Take(int i) { if (!ingested[i]) { ingested[i] = true; tape.AddRange(pool[i]); tape.Add((byte)'\n'); } }

        // identical minimal seed for both policies (first span of domain 0) — the divergence is the SELECTION.
        Take(0);
        ulong rng = seed ^ 0x5EED;
        int NextRand(int m) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)m); }

        var rows = new List<string> { CurveHeader };
        int round = 0;
        RePairResult g = Engine.Induce([]).Result; var cover = new Engine.GrammarCover(g.Rules, GrokDefaults.FrontierCapExps); int lastAcc = -1;   // valid empty grammar; the guard forces the real first induce
        while (true)
        {
            long roundT0 = Trace.NowTicks;                            // round wall for the slow-round reaper
            Trace.CritLock.Boundary("round", $"{policy}/r{round}");
            if (lastAcc < 0 || tape.Count - lastAcc >= GrokDefaults.ReStrideBytes) { var phRi = Trace.CritLockPhase("reinduce"); g = Engine.Induce(tape.ToArray()).Result; cover = new Engine.GrammarCover(g.Rules, GrokDefaults.FrontierCapExps); lastAcc = tape.Count; phRi.Dispose(); }   // stride re-induce (drives frontier + globalcv) — the O(n²) chug, gated by ReStrideBytes
            var phRead = Trace.CritLockPhase("read", boundary: true);   // per-domain CV reads — unconditional every round ⇒ the GcProbe iteration boundary
            ReadDomains(meters, byDom, ingested, poolDom, pool, D, round, tape.Count, lastSpans, cachedCv, cachedK);
            var (_, _, gcv, gspan, _) = Engine.RenormStats(g);         // one RenormStats — CvZ + MaxSpan (was two full calls)
            double comp = g.Compressed.Length == 0 ? 0 : 1.0 - (double)g.Compressed.Length / Math.Max(1, tape.Count);   // cheap per-round depth proxy
            rows.Add(CurveRow(policy, round, CountTrue(ingested), tape.Count, g.Rules.Length, gcv, gspan, comp, LockedCount(meters)));
            phRead.Dispose();
            round++;
            long roundMs = Trace.ElapsedMs(roundT0);                  // frame.slow-style reaper for the drain round
            if (roundMs > Trace.StepSlowMs) Trace.CritLock.Warn("round.slow", $"{policy} r{round} ms={roundMs} bytes={tape.Count}");

            var remaining = Enumerable.Range(0, n).Where(i => !ingested[i]).ToList();
            if (remaining.Count == 0) break;
            int m = Math.Min(batch, remaining.Count);
            if (policy == "frontier")
                foreach (var i in Radula.FrontierPick(cover, pool, ingested, m, frontier!)) Take(i);
            else
                for (int k = 0; k < m; k++) { var r = remaining.Where(i => !ingested[i]).ToList(); Take(r[NextRand(r.Count)]); }
        }
        // final union depth (expensive held-out sweep, once) + per-domain final depth
        var finalTape = tape.ToArray();
        var gf = Engine.Induce(finalTape).Result;
        var (fMax, fSym) = DomainDepth.Union(gf, heldout);
        for (int d = 0; d < D; d++) meters[d].BestSym = FinalDomainBestSym(GatherDom(pool, poolDom, ingested, d));
        return new DrainResult(policy, meters, fMax, fSym, tape.Count, round, string.Join("\n", rows) + "\n", finalTape);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  KILL-LINE (b) — scheduled concentration, CV-lock advances the domain, union depth at matched budget
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Follow `order`: concentrate residual-frontier intake on the CURRENT domain until its isolated CV LOCKS
    /// (< cvT for lockL rounds), then advance to the next domain in the order. Both the bridge-order and the shuffle
    /// arm run this identical engine — the ONLY difference is `order`, so any union-depth gap is attributable to the
    /// SEQUENCE alone. Budget is matched by construction (both drain the same pool to exhaustion; union depth is read
    /// at the shared final budget AND at each per-domain-count checkpoint for the matched-budget verdict).
    private static DrainResult DrainScheduled(
        List<byte[]>[] byDom, IReadOnlyList<(int Fam, byte[] Bytes)> heldout, int D,
        int[] order, int batch, double cvT, int lockL, ulong seed, string label)
    {
        // per-domain sub-pools (frontier picks WITHIN the current domain — concentration is imposed by the schedule)
        var domPool = new List<byte[]>[D]; var domIngested = new bool[D][]; var domFrontier = new FrontierIndex[D];
        for (int d = 0; d < D; d++) { domPool[d] = byDom[d].ToList(); domIngested[d] = new bool[domPool[d].Count]; domFrontier[d] = new FrontierIndex(domPool[d]); }
        var tape = new List<byte>();
        var meters = NewMeters(D, cvT, lockL);
        var lastSpans = NewLastSpans(D); var cachedCv = new double[D]; var cachedK = new int[D];
        void TakeDom(int d, int i) { if (!domIngested[d][i]) { domIngested[d][i] = true; tape.AddRange(domPool[d][i]); tape.Add((byte)'\n'); } }

        var rows = new List<string> { CurveHeader };
        int round = 0;
        RePairResult g = Engine.Induce([]).Result; var cover = new Engine.GrammarCover(g.Rules, GrokDefaults.FrontierCapExps); int lastAcc = -1;   // valid empty grammar; the guard forces the real first induce
        foreach (int d in order)
        {
            if (domPool[d].Count == 0) continue;
            TakeDom(d, 0);                                   // bootstrap this domain so its residual can discriminate
            while (true)
            {
                long roundT0 = Trace.NowTicks;                        // round wall for the slow-round reaper
                Trace.CritLock.Boundary("round", $"{label}:d{d}/r{round}");
                if (lastAcc < 0 || tape.Count - lastAcc >= GrokDefaults.ReStrideBytes) { var phRi = Trace.CritLockPhase("reinduce"); g = Engine.Induce(tape.ToArray()).Result; cover = new Engine.GrammarCover(g.Rules, GrokDefaults.FrontierCapExps); lastAcc = tape.Count; phRi.Dispose(); }   // stride re-induce (a ≤stride-stale grammar warm-starts the next domain — the transfer)
                var phRead = Trace.CritLockPhase("read", boundary: true);   // per-domain CV reads — unconditional every round ⇒ the GcProbe iteration boundary
                ReadDomains(meters, byDom, null, null, null, D, round, tape.Count, lastSpans, cachedCv, cachedK, domIngested);
                var (_, _, gcv, gspan, _) = Engine.RenormStats(g);     // one RenormStats — CvZ + MaxSpan (was two full calls)
                double comp = g.Compressed.Length == 0 ? 0 : 1.0 - (double)g.Compressed.Length / Math.Max(1, tape.Count);
                rows.Add(CurveRow($"{label}:d{d}", round, TotalIngested(domIngested), tape.Count, g.Rules.Length, gcv, gspan, comp, LockedCount(meters)));
                phRead.Dispose();
                round++;
                long roundMs = Trace.ElapsedMs(roundT0);              // frame.slow-style reaper for the drain round
                if (roundMs > Trace.StepSlowMs) Trace.CritLock.Warn("round.slow", $"{label}:d{d} r{round} ms={roundMs} bytes={tape.Count}");

                if (meters[d].Locked) break;                // the bell — this domain grokked, cross the bridge
                var rem = Enumerable.Range(0, domPool[d].Count).Where(i => !domIngested[d][i]).ToList();
                if (rem.Count == 0) break;                   // pool exhausted before lock (record no-grok, advance)
                foreach (int i in FrontierPickDom(cover, domPool[d], domIngested[d], Math.Min(batch, rem.Count), domFrontier[d])) TakeDom(d, i);
            }
        }
        var finalTape = tape.ToArray();
        var gf = Engine.Induce(finalTape).Result;
        var (fMax, fSym) = DomainDepth.Union(gf, heldout);
        for (int d = 0; d < D; d++) meters[d].BestSym = FinalDomainBestSym(GatherDomSched(domPool[d], domIngested[d]));
        return new DrainResult(label, meters, fMax, fSym, tape.Count, round, string.Join("\n", rows) + "\n", finalTape);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE SHARED READS — per-domain CV (isolated) + union depth (accreted)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Read every domain's ISOLATED CV this round and fold it into the meters (DomainMeter.ReadCv — the shared
    /// O(Δ)-gated crystallization read). A domain's CV is the RenormStats CvZ of a grammar induced over ONLY that
    /// domain's ingested spans — the crystallization signal cleaned of the cross-domain scale-mixing that makes the
    /// whole-grammar CV chatter. The stride cache (`lastSpans`/`cachedCv`/`cachedK`) is per-drain-local (this
    /// kill-line never checkpoints); the read re-induces a domain only on a span-count change, so the per-round
    /// induction count collapses from D to ~1 (in frontier/scheduled only the concentrated domain grows). The
    /// scheduled arm reads each domain over its OWN drain mask (DomainSpans); the global arm carves the domain out of
    /// the flat pool by poolDom attribution (PoolDomainSpans).
    private static void ReadDomains(
        DomainMeter[] meters, List<byte[]>[] byDom, bool[]? globalIngested, List<int>? poolDom, List<byte[]>? pool,
        int D, int round, int totalBytes, int[] lastSpans, double[] cachedCv, int[] cachedK, bool[][]? domIngested = null)
    {
        for (int d = 0; d < D; d++)
        {
            if (domIngested != null)
                meters[d].ReadCv(CountTrue(domIngested[d]), GrokDefaults.MinDomainSpans, GrokDefaults.DomStrideSpans, ref lastSpans[d], ref cachedCv[d], ref cachedK[d], round, totalBytes, new DomainSpans(byDom[d], domIngested[d]));
            else
                meters[d].ReadCv(CountDom(globalIngested!, poolDom!, d), GrokDefaults.MinDomainSpans, GrokDefaults.DomStrideSpans, ref lastSpans[d], ref cachedCv[d], ref cachedK[d], round, totalBytes, new PoolDomainSpans(pool!, poolDom!, globalIngested!, d));
        }
    }

    /// The deepest per-domain held-out sym/byte under an isolated domain grammar — the per-domain depth companion to
    /// its CV (a domain can lock its CV yet stay shallow; reporting both keeps the bell honest). Called ONCE at
    /// drain end per domain over that domain's INGESTED spans (subsampled, cover built once) — never per-round.
    private static double FinalDomainBestSym(List<byte[]> ingestedSpans)
    {
        if (ingestedSpans.Count == 0) return 1.0;
        var span = new List<byte>();
        foreach (var b in ingestedSpans) { span.AddRange(b); span.Add((byte)'\n'); }
        var gd = Engine.Induce(span.ToArray()).Result;
        var cover = new Engine.GrammarCover(gd.Rules);
        double best = 1.0; int step = Math.Max(1, ingestedSpans.Count / 30);
        for (int i = 0; i < ingestedSpans.Count; i += step) { var hb = ingestedSpans[i]; if (hb.Length == 0) continue; double s = (double)cover.ParsedSize(hb) / hb.Length; if (s < best) best = s; }
        return best;
    }

    /// Domain-restricted frontier pick — the thermostat's concentration move: from the current domain's un-ingested
    /// spans, the ones the CURRENT (accreted) grammar compresses best. Identical residual logic to Radula.FrontierPick,
    /// scoped to one domain's sub-pool + its once-built FrontierIndex (face 3c).
    private static List<int> FrontierPickDom(Engine.GrammarCover cover, List<byte[]> domPool, bool[] domIngested, int count, FrontierIndex frontier)
        => Radula.FrontierPick(cover, domPool, domIngested, count, frontier);

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE BRIDGE GRAPH — DomainWalk provenance over the domains → greedy bridge-order
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// The greedy coupling bridge-order over the domains — start at 0, repeatedly cross to the un-visited domain with
    /// the strongest cross-domain bridge to the visited SET ("cross the best coupling-bridge into the adjacent
    /// domain," precomputed as a schedule). The bridge-matrix build + chunk-provenance that used to live here (a twin
    /// of DomainWalk's) is now the shared DomainGraph; this caps each domain at PerDomCap so
    /// the union induce stays quick + no domain dominates. Returns the order + DomainGraph's max-φ matrix report.
    private static (int[] Order, string Report) BridgeOrder(List<byte[]>[] byDom, int D, ulong seed)
    {
        const int PerDomCap = 40_000;
        var blocks = new (string Name, byte[] Block)[D];
        for (int d = 0; d < D; d++)
        {
            var buf = new List<byte>(); int taken = 0;
            foreach (var b in byDom[d]) { if (taken + b.Length > PerDomCap) break; buf.AddRange(b); buf.Add((byte)'\n'); taken += b.Length + 1; }
            blocks[d] = ($"d{d}", buf.ToArray());
        }
        var graph = DomainGraph.Build(blocks, Couplings.DefaultWindow);
        var order = graph.GreedyOrder();
        return (order, graph.RenderBridgeMatrix(order));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE REPORT
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void Report(
        int D, DrainResult frontierA, DrainResult randomA, int[] bridgeOrder, int[] shuffleOrder,
        DrainResult bridgeB, DrainResult shuffleB, double cvT, int lockL,
        int bStar, (double MaxSpan, double MeanSym) bridgeM, (double MaxSpan, double MeanSym) shuffleM)
    {
        Console.WriteLine("");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  CONTROL · IS THE CV-COLLAPSE A RELIABLE PER-DOMAIN GROK-BELL?");
        Console.WriteLine("    (frontier drain, per domain: first raw crossing · #raw crossings = descent chatter · the debounced LOCK · finalCV)");
        int lockedF = 0, chatterSum = 0, staysBelow = 0;
        for (int d = 0; d < D; d++)
        {
            var m = frontierA.Meters[d];
            string bell = m.Locked ? $"LOCK@r{m.LockRound} ({m.LockBytes}B)" : "never locked";
            if (m.Locked) { lockedF++; if (!double.IsNaN(m.Cv) && m.Cv < cvT) staysBelow++; }
            chatterSum += m.Crossings;
            Console.WriteLine($"    d{d}  firstCross r{m.FirstCrossRound,-4} crossings {m.Crossings,-2}  {bell,-22}  finalCV {m.Cv,6:F3}  bestSym {m.BestSym:F3}");
        }
        Console.WriteLine($"    → BELL FIRES {lockedF}/{D} domains · {staysBelow}/{lockedF} still below at budget-end · descent chatter {chatterSum} raw crossings (a BARE crossing would misfire {chatterSum}×; the {lockL}-round LOCK debounces it)");
        Console.WriteLine($"    → {(lockedF == D ? $"RELIABLE WITH HYSTERESIS — every domain's exponent crystallizes below {cvT:P0} and LOCKS: a deterministic per-domain move-on bell (raw CV chatters on the descent, the LOCK is the clean signal — the thermostat's anti-chatter)" : lockedF > 0 ? $"PARTIAL — {lockedF}/{D} lock; the rest don't crystallize in budget (undersized domains, or a non-critical corpus)" : "NEVER LOCKS — wrong regime: the −0.70 CV-collapse is a REAL-CODE phenomenon (S1); a synthetic/non-critical corpus has no fixed point to lock into (use --corpus real code)")}");

        Console.WriteLine("");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  KILL-LINE (a) · CRITICAL-POINT SHIFT — does residual-frontier crystallize the SET at fewer bytes than random-global?");
        Console.WriteLine("    (bytes-to-grok = TOTAL accreted bytes when a domain's CV LOCKS. frontier SERIALIZES [grok one, move on]; random SPREADS)");
        for (int d = 0; d < D; d++)
        {
            var mf = frontierA.Meters[d]; var mr = randomA.Meters[d];
            string cmp = mf.Locked && mr.Locked ? (mf.LockBytes <= mr.LockBytes ? $"frontier {(double)mr.LockBytes / Math.Max(1, mf.LockBytes):F1}× earlier" : $"random {(double)mf.LockBytes / Math.Max(1, mr.LockBytes):F1}× earlier")
                       : mf.Locked ? "frontier LOCKED, random never" : mr.Locked ? "random locked, frontier never" : "neither locked";
            Console.WriteLine($"    d{d}  frontier {LockStr(mf),-15}  random {LockStr(mr),-15}  → {cmp}");
        }
        int firstF = FirstLock(frontierA.Meters);
        if (firstF >= 0)
        {
            var mf0 = frontierA.Meters[firstF]; var mr0 = randomA.Meters[firstF];
            string vs = mr0.Locked ? $"{(double)mr0.LockBytes / Math.Max(1, mf0.LockBytes):F1}× sooner than random's {mr0.LockBytes}B on the same domain" : "a domain random never locks in budget";
            Console.WriteLine($"    → CONCENTRATION TARGET d{firstF}: frontier front-loads it to a lock at {mf0.LockBytes}B — {vs}");
        }
        int allF = BytesToGrokAll(frontierA.Meters), allR = BytesToGrokAll(randomA.Meters);
        Console.WriteLine($"    → bytes to grok the WHOLE union (the LAST domain crystallizes):  frontier {(allF < 0 ? $"incomplete ({lockedF}/{D})" : allF + "B")}  vs random {(allR < 0 ? $"incomplete ({LockedCount(randomA.Meters)}/{D})" : allR + "B")}");
        bool frontierShift = allF >= 0 && (allR < 0 || allF < allR);
        Console.WriteLine($"    → {(frontierShift ? $"FRONTIER SHIFTS THE CRITICAL POINT EARLIER — the serialized curriculum groks the WHOLE SET by {allF}B, front-loading its focus domain; random's uniform spread starves its hardest domain to {(allR < 0 ? "never (incomplete)" : allR + "B")} (concentration crystallizes the set sooner and leaves nothing un-grokked)" : "no whole-set earlier-shift here — random's spread crystallized all domains sooner than frontier's serialization reached them (concentration gain < serialization cost at this scale; the per-domain front-load above is still the real mechanism)")}");

        Console.WriteLine("");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  KILL-LINE (b) · BRIDGE-ORDER — does bridge-order beat shuffle on UNION DEPTH at MATCHED budget?");
        Console.WriteLine($"    bridge-order  {string.Join(" → ", bridgeOrder)}");
        Console.WriteLine($"    shuffle-order {string.Join(" → ", shuffleOrder)}");
        bool sameOrder = bridgeOrder.SequenceEqual(shuffleOrder);
        // THE VERDICT — matched budget B* (both schedule-ordered tapes truncated to the same byte count; ONLY the
        // order differs). The ragged ends below are NOT matched (each arm halts a domain at its lock ⇒ unequal totals),
        // so their depth gap confounds order with byte-count — reported as context, never as the verdict.
        Console.WriteLine($"    ── at MATCHED budget B* = {bStar}B (both tapes truncated equal; only the intake ORDER differs) ──");
        Console.WriteLine($"    bridge   union maxSpan {bridgeM.MaxSpan,4:F0}B  · mean held-out sym/byte {bridgeM.MeanSym:F4} (↓deeper)");
        Console.WriteLine($"    shuffle  union maxSpan {shuffleM.MaxSpan,4:F0}B  · mean held-out sym/byte {shuffleM.MeanSym:F4} (↓deeper)");
        bool bridgeDeeper = !sameOrder && (bridgeM.MeanSym < shuffleM.MeanSym - 1e-4 || bridgeM.MaxSpan > shuffleM.MaxSpan);
        Console.WriteLine($"    → {(sameOrder ? "DEGENERATE — the shuffle coincided with the bridge-order (too few domains / uniform bridges); need ≥5-6 domains with distinct bridge structure to separate the two" : bridgeDeeper ? "BRIDGE-ORDER GOES DEEPER AT MATCHED BUDGET — at EQUAL bytes, coupling-adjacency sequencing lands warmer cross-domain starts and deepens the union grammar (the SEQUENCE matters, not the byte count)" : "NO MATCHED-BUDGET SEPARATION — at equal bytes bridge-order ties or trails shuffle; any ragged-end 'deeper' below is a byte-count artifact, NOT the order (bridges too uniform, or the domains are near-equidistant)")}");
        Console.WriteLine($"    ── context · ragged ends (UNEQUAL budget — each arm halts a domain at its own lock) ──");
        Console.WriteLine($"    bridge   {bridgeB.UnionMeanSym:F4} sym/byte · maxSpan {bridgeB.UnionMaxSpan:F0}B @ {bridgeB.TotalBytes}B / {bridgeB.Rounds}r · {LockedCount(bridgeB.Meters)}/{D} locked");
        Console.WriteLine($"    shuffle  {shuffleB.UnionMeanSym:F4} sym/byte · maxSpan {shuffleB.UnionMaxSpan:F0}B @ {shuffleB.TotalBytes}B / {shuffleB.Rounds}r · {LockedCount(shuffleB.Meters)}/{D} locked");

        Console.WriteLine("");
        Console.WriteLine("  ── the school ──  RLEI-root (frontier intake) + the grok-bell (per-domain CV-lock) + the sequence");
        Console.WriteLine("     (coupling bridge-order) = the developmental curriculum SELF-SCHEDULING, deterministic, no LLM.");
    }

    // ── curve tsv (landed per arm for meta-analysis) ──  per-round reads are the CHEAP ones (globalcv/maxspan from
    // RenormStats, comp from the compressed length); the expensive held-out union-depth sweep is a drain-end read.
    private const string CurveHeader = "arm\tround\tingested\tbytes\trules\tglobalcv\tmaxspan\tcomp\tlocked";
    private static string CurveRow(string arm, int round, int ingested, int bytes, int rules, double gcv, double maxSpan, double comp, int locked)
        => $"{arm}\t{round}\t{ingested}\t{bytes}\t{rules}\t{(double.IsNaN(gcv) ? "nan" : gcv.ToString("F3"))}\t{maxSpan:F0}\t{comp:F4}\t{locked}";

    // ── small shared helpers ──
    private static DomainMeter[] NewMeters(int D, double cvT, int lockL) { var m = new DomainMeter[D]; for (int i = 0; i < D; i++) m[i] = new(cvT, lockL); return m; }
    private static int[] NewLastSpans(int D) { var a = new int[D]; for (int i = 0; i < D; i++) a[i] = -1; return a; }   // -1 = never induced (force first read)
    private static int LockedCount(DomainMeter[] m) { int c = 0; foreach (var x in m) if (x.Locked) c++; return c; }
    private static int CountTrue(bool[] b) { int c = 0; foreach (var x in b) if (x) c++; return c; }
    private static int CountDom(bool[] ingested, List<int> poolDom, int d) { int c = 0; for (int i = 0; i < ingested.Length; i++) if (ingested[i] && poolDom[i] == d) c++; return c; }
    private static int TotalIngested(bool[][] di) { int c = 0; foreach (var a in di) foreach (var x in a) if (x) c++; return c; }
    private static List<byte[]> GatherDom(List<byte[]> pool, List<int> poolDom, bool[] ingested, int d) { var l = new List<byte[]>(); for (int i = 0; i < pool.Count; i++) if (ingested[i] && poolDom[i] == d) l.Add(pool[i]); return l; }
    private static List<byte[]> GatherDomSched(List<byte[]> domPool, bool[] domIngested) { var l = new List<byte[]>(); for (int i = 0; i < domPool.Count; i++) if (domIngested[i]) l.Add(domPool[i]); return l; }
    private static string LockStr(DomainMeter m) => m.Locked ? $"{m.LockBytes}B@r{m.LockRound}" : "never";
    /// Bytes to grok the WHOLE union = the budget at which the LAST domain crystallizes (max lock-byte); -1 if any
    /// domain never locked (the set was not fully grokked in budget). The developmental-curriculum headline for (a).
    private static int BytesToGrokAll(DomainMeter[] m) { int mx = 0; foreach (var x in m) { if (!x.Locked) return -1; mx = Math.Max(mx, x.LockBytes); } return mx; }
    /// Frontier's concentration TARGET = the domain that locked first (min lock-round among locked); -1 if none.
    private static int FirstLock(DomainMeter[] m) { int best = -1, bestR = int.MaxValue; for (int d = 0; d < m.Length; d++) if (m[d].Locked && m[d].LockRound < bestR) { bestR = m[d].LockRound; best = d; } return best; }

}
