namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE DYNAMICAL BENCH ──  `cogito emlbench` — the honest PASS-vs-LOOP thermometer (loop-develops-what-pass-can't).
//
// THE REFRAME this bench answers. cogito's thesis (task-8) is that the LOOP develops what a single forward PASS
// cannot. LocAgent measured the agentic loop with a single-PASS top-k ruler — a category error: a loop-instrument
// read by a pass-metric, so cross-instance accretion couldn't help a fixed single retrieval and even hurt it. The
// fix is not a better retriever; it is the RIGHT THERMOMETER — a principled task where a single forward pass
// PROVABLY plateaus, but the self-developing loop demonstrably RISES above it, and the metric is the SLOPE (the
// rising line no frozen baseline can produce), not an aggregate a frozen model could also post.
//
// THE TASK (cogito's NATIVE strength — grammar-induction / structure-development, NOT discriminative retrieval).
// Replay identities out of the EML Sheffer operator eml(x,y)=exp(x)−ln(y): GENERATE candidate RPN programs →
// EVALUATE (clamped ℂ, Eml.Eval) → SIEVE (dual-point Schanuel equivalence) → GRADE (the witness ladder — E/A/S/D/U,
// a FREE PERFECT VERIFIER: no labels, no rigging possible, correctness is decided by the machine's own carried
// evaluator) → MINT the discovered identities as RPN text → the new theorem-CLASS (content-addressed by its
// (grade,limit,rate-law) certificate) is the discovery unit. This is the EML env ReplayCalc runs, mounted here as a
// two-arm SLOPE bench with a HELD-OUT target set.
//
// THE TWO ARMS — the SAME task, the SAME seed, the SAME budget, the SAME verifier, differing in ONE bit:
//   PASS  — the grammar is PINNED to the seed-round induction (induced ONCE, after the shared seed shells). Its
//           chunk-bias vocabulary is FROZEN at the shallow-shell subroutines. It keeps drawing candidates every
//           step, but it CANNOT develop deeper structure: a deep theorem whose RPN string only becomes samplable
//           after the intermediate identities are minted, chunked into named subroutines, and re-induced is
//           UNREACHABLE to it (its bias rail never learns those chunks). This is the single forward pass: fixed
//           model, information-limited by its frozen vocabulary.
//   LOOP  — the grammar RE-INDUCTS every stride over the growing minted-identity corpus (the spiral re-centering):
//           each stride's fresh identities surface as grammar chunks the NEXT draw samples, so newly-named
//           subroutines re-zero downstream depth and open shells the pass could not reach. This is the self-
//           developing loop — induce→dream→re-induce.
//
// WHY A PASS IS LIMITED AND THE LOOP CAN EXCEED IT (the honest statement Req-2 demands, in the code): the reachable
// program space at any step is the chunk-closure of the sampler's current vocabulary under the length ruler. The
// PASS's vocabulary is fixed at the seed grammar, so its reachable set is fixed — once it exhausts the discoveries
// in that closure, its discovery rate DECAYS TO ZERO (a saturating line). The LOOP's vocabulary GROWS as it mints,
// so its reachable set expands step over step — newly-chunked identities are new reachable-set generators, so its
// discovery rate is SUSTAINED (a rising / non-collapsing line). The gap between the two is not a tuning artifact:
// it is the difference between a fixed reachable set and a growing one.
//
// THE CONTROLLED VARIABLE — this bench is honest because it isolates ONE channel. Both arms share the seed round,
// the uniform-ε support floor (nothing is excluded from either arm's reach — the ergodicity floor), and the exact
// same generator policy. The ONLY difference is whether the grammar the bias rail reads is re-induced. At the
// headline setting the enumeration/coverage rail is OFF (--polenum 0), so the bias rail is the ONLY channel that
// can develop structure — the loop's advantage is PURELY the re-induction, nothing else. (The enumeration rail is
// grammar-BLIND — it sweeps shells in order regardless of chunks — so leaving it on would let a pass-blind mechanism
// mask the effect; --with-enum turns it on to show the effect SURVIVES coverage, but the clean contrast is enum-off.)
//
// THE HELD-OUT TARGET SET (the metric is not in-sample). A random half of the paper's named calculator targets is
// HELD OUT: neither arm is told about them, neither can steer toward them (generation is target-blind — it dreams,
// the sieve recognizes post-hoc). Held-out capture is the generalization read: did the developed grammar reach
// NAMED mathematics it was never pointed at? The primary metric is the DISCOVERY SLOPE (new theorem-classes per
// iteration-third) — a pure structure-development read that needs no labels at all; held-out capture is the second,
// label-anchored read.
//
// THE VERDICT IS THE SLOPE + THE GAP (Req-1/Req-4). Per iteration-third we read each arm's new-theorem-class rate,
// its held-out captures, and its K-frontier. PASS should go FLAT/SATURATING (fixed reachable set exhausts); LOOP
// should RISE / SUSTAIN (growing reachable set keeps producing). The GAP between the two slopes is the demolition
// axis — the rising line the frozen pass structurally cannot produce. If the LOOP does NOT beat the PASS, that is a
// real finding about the loop at this budget, reported straight (frame-break law: honest slope, name any clog).
//
// Deterministic: same seed → same discoveries → same slope (the Vow). Release-only; the free verifier reads no
// answers (it grades the machine's own dreams against its own evaluator, never a gold label).
public static class EmlBench
{
    /// usage: cogito emlbench [--steps N] [--batch M] [--stride B] [--seedk K] [--maxlen L] [--sig S] [--seed HEX]
    ///                        [--holdout F] [--with-enum] [--polmix F] [--gain G] [--certw W]
    ///   The dynamical PASS-vs-LOOP thermometer over the EML dream-calculator. Two arms on the SAME task/seed/budget/
    ///   verifier, differing in ONE bit — whether the grammar re-inducts (LOOP, the spiral) or stays pinned to the
    ///   seed induction (PASS, the fixed forward pass). The verdict is the DISCOVERY SLOPE (new theorem-classes per
    ///   iteration-third) and the GAP between the arms — the rising line a frozen pass cannot produce.
    ///     --holdout F   fraction of the named calculator targets held out of BOTH arms (the generalization read; the
    ///                   slope metric needs no labels, this is the label-anchored second read). 0 = no holdout.
    ///     --with-enum   turn the grammar-BLIND enumeration rail ON (--polenum 0.4). Default OFF, so the chunk-bias
    ///                   rail is the ONLY structure-developing channel and the loop's advantage is purely re-induction.
    ///     --polmix F    the uniform-ε support floor (both arms — the ergodicity floor; nothing is excluded).
    ///   Deterministic — same seed, same discoveries, same slope.
    public static int Run(string[] args)
    {
        int steps    = Args.Int(args, "--steps", 300);
        int batch    = Args.Int(args, "--batch", 32);
        int stride   = Args.Int(args, "--stride", 800);
        int sig      = Args.Int(args, "--sig", ReplayCalc.MountSig);
        ulong seed   = Args.Seed(args, "--seed", 0xE311C0DEUL);
        double holdout = Args.Double(args, "--holdout", 0.5);
        bool withEnum = Args.Has(args, "--with-enum");

        var k = new EmlKnobs(
            SeedK:   Args.Int(args, "--seedk", ReplayCalc.MountSeedK),
            MaxLen:  Args.Int(args, "--maxlen", ReplayCalc.MountMaxLen),
            MaxEnum: Args.Int(args, "--maxenum", ReplayCalc.MountMaxEnum),
            Units:   Args.Int(args, "--units", ReplayCalc.MountUnits),
            Gain:    Args.Int(args, "--gain", ReplayCalc.MountGain),
            Eps:     Args.Double(args, "--polmix", ReplayCalc.MountEps),
            EpsEnum: withEnum ? Args.Double(args, "--polenum", ReplayCalc.MountEpsEnum) : 0.0,   // OFF by default — the clean pass-vs-loop channel
            CorrobW: Args.Int(args, "--corrob", ReplayCalc.MountCorrobW),
            CertW:   Args.Int(args, "--certw", ReplayCalc.MountCertW));

        var run = Cogito.Run.New("emlbench");
        Trace.Note($"emlbench · eml(x,y)=exp(x)−ln(y) · PASS (grammar pinned) vs LOOP (re-induce spiral) · SAME seed/budget/verifier · " +
                   $"seed K≤{k.SeedK} shared · {steps}×{batch}={steps * batch} cand/arm · maxlen {k.MaxLen} · sig {sig} · " +
                   $"holdout {holdout:P0} of named targets · enum-rail {(withEnum ? $"ON ε={k.EpsEnum:F2}" : "OFF (bias rail is the only developing channel)")} · seed {seed:X}");

        // the HELD-OUT split — deterministic (seed-derived), applied IDENTICALLY to both arms so the two sieves
        // recognize the exact same target subset. The held-out targets are dreamt-toward-blind by both (generation
        // never sees them); their capture is the generalization read.
        var (trainMask, heldCount, trainCount) = EmlSieve.HoldoutMask(holdout, seed);
        Trace.Note($"  targets: {trainCount} train (both arms recognize) · {heldCount} HELD-OUT (both arms blind, capture = generalization) — the split is byte-identical across arms");

        var loop = DriveArm("LOOP", reinduce: true,  steps, batch, stride, k, sig, seed, trainMask);
        var pass = DriveArm("PASS", reinduce: false, steps, batch, stride, k, sig, seed, trainMask);

        Report(run, loop, pass, steps, batch, stride, k, sig, holdout, heldCount, trainCount, withEnum);
        return 0;
    }

    // the per-iteration record — one row per step, both the raw discovery counts and the derived-at-report slopes
    // read off it. Everything here is a live read of the arm's own state the step it was taken (no hindsight leak).
    private readonly record struct Tick(
        int Step, int MintClasses, int ExactClasses, int NewClassesThisStep, int NewHeldThisStep,
        int KFrontier, int DistinctValues, double CvZ, long GrammarBytes);

    private sealed record ArmRun(
        string Label, bool Reinduce, EmlSieve Sieve, byte[] Tape, List<Tick> Ticks,
        long Candidates, int SeedRules, int FinalRules, IReadOnlyCollection<int> HeldCaptured);

    // ── DRIVE ONE ARM — the trunk's own induce→draw→read loop over the minted-identity tape. LOOP re-inducts on the
    // stride (the spiral — chunks richen); PASS induces ONCE after the seed round and PINS that grammar (the bias
    // vocabulary is frozen). Everything else — the sampler, the sieve, the grade-gate, the held-out recognition — is
    // byte-identical between the arms, so the SLOPE difference isolates the re-induction and nothing else. ──
    private static ArmRun DriveArm(string label, bool reinduce, int steps, int batch, int stride, in EmlKnobs knobs, int sig, ulong seed, bool[] trainMask)
    {
        var sieve = new EmlSieve(sig, trainMask);       // the held-out split — this arm recognizes ONLY the train targets
        var sampler = new EmlSampler(knobs.Units, knobs.MaxLen, knobs.Gain, knobs.Eps, knobs.EpsEnum, knobs.SeedK, seed);
        var tape = new Tape();
        var journal = new Journal();

        // THE SEED ROUND (shared by both arms) — enumerate the shallow shells, offer each, accrete the discovered
        // lines. This rediscovers the paper's own reductions and gives BOTH arms the same starter grammar; the arms
        // diverge only in whether that grammar is subsequently re-induced.
        foreach (var prog in EmlGen.Enumerate(1, knobs.SeedK)) sieve.Offer(prog);
        Accrete(sieve, tape, journal, knobs, step: -1);

        // the seed-grammar induction — the PASS arm PINS this; the LOOP arm re-induces on top.
        var (_, _, gSeed) = Engine.Induce(tape);
        var g = gSeed;
        int seedRules = gSeed.Rules.Length;
        var chunks = EmlGen.PureChunks(g);
        GrammarRule[]? chunkRules = g.Rules;
        long lastInduceBytes = tape.GrammarByteLength;
        double cvZ = Engine.RenormStats(g).CvZ;

        var ticks = new List<Tick>(steps);
        long candidates = 0;
        int prevClasses = sieve.TheoremClasses, prevExact = sieve.ExactClasses;
        int prevHeld = sieve.HeldCapturedCount;

        for (int step = 0; step < steps; step++)
        {
            // LOOP: re-induce on the stride (the spiral — the O(Δ)-strided re-induce the trunk runs). PASS: never
            // re-induces, so its `g`/`chunks` stay the seed grammar forever (the frozen forward pass).
            if (reinduce && tape.GrammarByteLength - lastInduceBytes >= stride)
            {
                (_, _, g) = Engine.Induce(tape);
                lastInduceBytes = tape.GrammarByteLength;
                cvZ = Engine.RenormStats(g).CvZ;
            }
            // rebuild the chunk cache only when the grammar identity changed (per-stride, not per-step — the
            // cover-cache discipline). For PASS this fires once (the seed grammar) and never again.
            if (!ReferenceEquals(g.Rules, chunkRules)) { chunks = EmlGen.PureChunks(g); chunkRules = g.Rules; }

            for (int b = 0; b < batch; b++) sieve.Offer(sampler.Next(chunks));
            Accrete(sieve, tape, journal, knobs, step);
            candidates += batch;

            int newClasses = sieve.TheoremClasses - prevClasses;
            int newHeld = sieve.HeldCapturedCount - prevHeld;
            ticks.Add(new Tick(step, sieve.TheoremClasses, sieve.ExactClasses, newClasses, newHeld,
                               sieve.KFrontier, sieve.DistinctValues, cvZ, tape.GrammarByteLength));
            prevClasses = sieve.TheoremClasses; prevExact = sieve.ExactClasses; prevHeld = sieve.HeldCapturedCount;

            if (step > 0 && step % 100 == 0)
                Trace.Note($"{label} · step {step}/{steps} · classes {sieve.TheoremClasses} (E {sieve.ExactClasses}) · K {sieve.KFrontier} · held {sieve.HeldCapturedCount} · grammar {tape.GrammarByteLength / 1024}KB · rules {g.Rules.Length}");
        }

        return new ArmRun(label, reinduce, sieve, tape.Concat(), ticks, candidates,
                          seedRules, g.Rules.Length, sieve.HeldCaptured);
    }

    // accrete the sieve's fresh mints onto the tape (the discovery IS the intake — the cert-gated weight law both
    // EML mouths pay; ReplayCalc.AccreteWeight is the one authority). The grade routes provenance (EXACT → Witnessed,
    // the rest → Replay). This is the LOOP's fuel — the minted identities the next re-induce chunks; the PASS lands
    // them on the tape too (fair budget) but never re-induces over them.
    private static void Accrete(EmlSieve sieve, Tape tape, Journal journal, in EmlKnobs knobs, int step)
    {
        var mints = sieve.NewMints;
        for (int i = 0; i < mints.Count; i++)
        {
            var m = mints[i];
            int w = ReplayCalc.AccreteWeight(in m, sieve.NewMintFirst(i), Math.Max(1, knobs.CorrobW), Math.Max(1, knobs.CertW));
            if (w <= 0) continue;
            var bytes = Encoding.ASCII.GetBytes(m.Line);
            for (int j = 0; j < w; j++)
            {
                var sid = tape.Append(bytes, "dream", m.Grade == 'E' ? Provenances.Reflected : Provenances.Replay);
                journal.Mint(step, sid, "dream", bytes);
            }
        }
        sieve.DrainNewMints();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE REPORT — the SLOPE is the verdict. Three reads, each a rising-vs-flat contrast between the arms:
    //   (1) THE DISCOVERY SLOPE — new theorem-classes per iteration-third (the primary, label-free read). PASS
    //       saturates (fixed reachable set), LOOP sustains (growing reachable set). The GAP is the demolition axis.
    //   (2) THE HELD-OUT CAPTURE SLOPE — named targets neither arm was pointed at, captured per third (generalization).
    //   (3) THE DEPTH SLOPE — K-frontier over the stream (the spiral re-centering: does the loop reach deeper shells).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static void Report(Run run, ArmRun loop, ArmRun pass, int steps, int batch, int stride,
                               in EmlKnobs knobs, int sig, double holdout, int heldCount, int trainCount, bool withEnum)
    {
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine("════════════════════════════════════════════════════════════════════════════════════════════════");
        o.AppendLine("  THE DYNAMICAL BENCH — PASS (grammar pinned) vs LOOP (re-induce spiral) over eml(x,y)=exp(x)−ln(y)");
        o.AppendLine($"    the ONE bit that differs: re-induction. LOOP re-inducts the minted corpus every {stride}B stride (chunks richen →");
        o.AppendLine("    deeper shells reachable); PASS pins the seed-round grammar (bias vocabulary frozen — the fixed forward pass).");
        o.AppendLine($"    SAME seed · SAME budget ({steps}×{batch}={steps * batch} cand/arm) · SAME free verifier (the grade-ladder) · maxlen {knobs.MaxLen} · sig {sig}");
        o.AppendLine($"    support floor ε={knobs.Eps:F3} (both arms — nothing excluded) · enum rail {(withEnum ? $"ON ε={knobs.EpsEnum:F2}" : "OFF — the bias rail is the ONLY developing channel")}");
        o.AppendLine($"    seed grammar: {pass.SeedRules} rules (shared) → final: LOOP {loop.FinalRules} rules · PASS {pass.FinalRules} rules (pinned)");
        o.AppendLine();

        // ── (1) THE DISCOVERY SLOPE — the primary verdict (label-free structure-development read) ──
        o.AppendLine("  ══ (1) THE DISCOVERY SLOPE — new theorem-classes per iteration-third (label-free; the rising line a frozen pass cannot produce) ══");
        o.AppendLine("     A theorem-class = a distinct (grade·limit·rate-law) certificate — a genuine new discovery, not a paraphrase (the CAS keys it).");
        o.AppendLine();
        var (loopThirds, loopTotal) = ThirdClasses(loop, steps);
        var (passThirds, passTotal) = ThirdClasses(pass, steps);
        o.AppendLine("     arm    third-1   third-2   third-3   |  total-classes   final-E   slope(t3−t1)   trajectory");
        o.AppendLine($"     LOOP   {loopThirds[0],7}   {loopThirds[1],7}   {loopThirds[2],7}   |  {loopTotal,13}   {loop.Sieve.ExactClasses,7}   {loopThirds[2] - loopThirds[0],+12}   {Traj(loopThirds)}");
        o.AppendLine($"     PASS   {passThirds[0],7}   {passThirds[1],7}   {passThirds[2],7}   |  {passTotal,13}   {pass.Sieve.ExactClasses,7}   {passThirds[2] - passThirds[0],+12}   {Traj(passThirds)}");
        o.AppendLine();
        // the crux read: is PASS flat/saturating while LOOP rises, AND is the LOOP's total gap positive?
        bool passSaturates = passThirds[2] <= passThirds[0];              // PASS's last third ≤ its first (fixed reachable set exhausted)
        bool loopSustains  = loopThirds[2] >= loopThirds[0] * 0.5 && loopThirds[2] > 0;   // LOOP's last third holds up (growing reachable set)
        bool loopWinsTotal = loopTotal > passTotal;
        int gapT3 = loopThirds[2] - passThirds[2];                        // the last-third discovery gap — the demolition number
        o.AppendLine($"     ⇒ PASS {(passSaturates ? "SATURATES" : "does NOT saturate")} (t1 {passThirds[0]} → t3 {passThirds[2]}: {(passSaturates ? "fixed reachable set exhausted" : "still producing")})");
        o.AppendLine($"       LOOP {(loopSustains ? "SUSTAINS" : "does NOT sustain")} (t1 {loopThirds[0]} → t3 {loopThirds[2]}: {(loopSustains ? "growing reachable set keeps producing" : "also decayed")})");
        o.AppendLine($"       LAST-THIRD GAP  LOOP {loopThirds[2]} − PASS {passThirds[2]} = {gapT3:+0;-0} new classes   ·   TOTAL  LOOP {loopTotal} vs PASS {passTotal} (Δ{loopTotal - passTotal:+0;-0})");
        string verdict1 =
            loopWinsTotal && passSaturates && loopSustains && gapT3 > 0
                ? $"THE LOOP DEVELOPS WHAT THE PASS CANNOT — the pass saturates on its fixed vocabulary while the loop keeps opening new theorem-classes (+{gapT3} in the last third alone). The rising line is the demolition axis."
          : loopWinsTotal
                ? $"THE LOOP WINS on total discovery (Δ{loopTotal - passTotal:+0}) but the slope contrast is soft (pass {(passSaturates ? "saturates" : "still climbs")}, loop {(loopSustains ? "sustains" : "decays")}) — the loop develops MORE, but the flat-vs-rising story is muddy at this budget; raise --steps/--maxlen to force the deep regime."
          : $"NO LOOP WIN at this budget — the pass matched or beat the loop on new-class discovery (LOOP {loopTotal} vs PASS {passTotal}). A real finding about the loop HERE, reported straight: the reachable set the seed grammar spans already covers what this budget reaches; the re-induction bought no depth. Force the deep regime (lower --seedk, raise --maxlen/--steps) or the loop's structure-development does not convert on THIS substrate.";
        o.AppendLine($"       VERDICT: {verdict1}");
        o.AppendLine();

        // ── (2) THE HELD-OUT CAPTURE SLOPE — the label-anchored generalization read ──
        if (heldCount > 0)
        {
            o.AppendLine($"  ══ (2) THE HELD-OUT CAPTURE — {heldCount} named calculator targets held out of BOTH arms (neither steered toward them) ══");
            o.AppendLine("     Capture = the developed grammar reached NAMED mathematics it was never pointed at (generation is target-blind — the sieve recognizes post-hoc).");
            var loopHeldThirds = ThirdHeld(loop, steps);
            var passHeldThirds = ThirdHeld(pass, steps);
            o.AppendLine("     arm    third-1   third-2   third-3   |  total-held-captured / N");
            o.AppendLine($"     LOOP   {loopHeldThirds[0],7}   {loopHeldThirds[1],7}   {loopHeldThirds[2],7}   |  {loop.HeldCaptured.Count,10} / {heldCount}");
            o.AppendLine($"     PASS   {passHeldThirds[0],7}   {passHeldThirds[1],7}   {passHeldThirds[2],7}   |  {pass.HeldCaptured.Count,10} / {heldCount}");
            int heldGap = loop.HeldCaptured.Count - pass.HeldCaptured.Count;
            o.AppendLine($"     ⇒ held-out capture  LOOP {loop.HeldCaptured.Count}/{heldCount} vs PASS {pass.HeldCaptured.Count}/{heldCount} (Δ{heldGap:+0;-0}) — " +
                         (heldGap > 0 ? "the loop generalized to targets the pass could not reach (the developed grammar's reach)."
                        : heldGap == 0 ? "both arms reached the same held-out targets (the holdout sits inside the shared seed-reachable set — raise --maxlen to push it out)."
                        : "the pass reached MORE held-out targets — inspect (a shallow holdout the pass hits directly; the slope, not this count, is the honest read)."));
            o.AppendLine();
        }

        // ── (3) THE DEPTH SLOPE — the spiral re-centering (K-frontier over the stream) ──
        o.AppendLine("  ══ (3) THE DEPTH SLOPE — K-frontier (deepest shell that produced a discovery) per iteration-third (the spiral re-centering) ══");
        var loopK = ThirdK(loop, steps);
        var passK = ThirdK(pass, steps);
        o.AppendLine("     arm    third-1   third-2   third-3   |  final-K   distinct-values");
        o.AppendLine($"     LOOP   {loopK[0],7}   {loopK[1],7}   {loopK[2],7}   |  {loop.Sieve.KFrontier,7}   {loop.Sieve.DistinctValues,15}");
        o.AppendLine($"     PASS   {passK[0],7}   {passK[1],7}   {passK[2],7}   |  {pass.Sieve.KFrontier,7}   {pass.Sieve.DistinctValues,15}");
        o.AppendLine($"     ⇒ deepest shell reached  LOOP K={loop.Sieve.KFrontier} vs PASS K={pass.Sieve.KFrontier} · distinct values LOOP {loop.Sieve.DistinctValues} vs PASS {pass.Sieve.DistinctValues} " +
                     $"({(loop.Sieve.KFrontier > pass.Sieve.KFrontier || loop.Sieve.DistinctValues > pass.Sieve.DistinctValues ? "the loop re-centered deeper / explored wider" : "no depth advantage at this budget")})");
        o.AppendLine();

        // ── the SPARKLINES — the per-step trajectory (the rising-vs-flat SHAPE the eye reads instantly) ──
        o.AppendLine("  ── the trajectory (per step, ▁▂▃▄▅▆▇█ over the joint range — the SHAPE of the two lines) ──");
        double jointMax = Math.Max(MaxD(loop.Ticks.Select(t => (double)t.MintClasses)), MaxD(pass.Ticks.Select(t => (double)t.MintClasses)));
        o.AppendLine($"     LOOP  cumulative theorem-classes  {SparkJoint(loop.Ticks.Select(t => (double)t.MintClasses), jointMax)}  → {loop.Sieve.TheoremClasses}");
        o.AppendLine($"     PASS  cumulative theorem-classes  {SparkJoint(pass.Ticks.Select(t => (double)t.MintClasses), jointMax)}  → {pass.Sieve.TheoremClasses}");
        o.AppendLine($"     LOOP  new-classes / step          {SparkOwn(loop.Ticks.Select(t => (double)t.NewClassesThisStep))}  (↓flat = saturating · sustained = rising)");
        o.AppendLine($"     PASS  new-classes / step          {SparkOwn(pass.Ticks.Select(t => (double)t.NewClassesThisStep))}");
        double jointK = Math.Max(MaxD(loop.Ticks.Select(t => (double)t.KFrontier)), MaxD(pass.Ticks.Select(t => (double)t.KFrontier)));
        o.AppendLine($"     LOOP  K-frontier                  {SparkJoint(loop.Ticks.Select(t => (double)t.KFrontier), jointK)}  → K{loop.Sieve.KFrontier}");
        o.AppendLine($"     PASS  K-frontier                  {SparkJoint(pass.Ticks.Select(t => (double)t.KFrontier), jointK)}  → K{pass.Sieve.KFrontier}");
        o.AppendLine();

        // ── HELD-OUT COMPRESSION — the deeper grammar compresses a fresh corpus of the OTHER arm's discoveries. A
        // deeper-developed grammar (LOOP) should parse held-out identities into fewer named subroutines (lower
        // ParsedSize/byte). This is the compression read of the same development the slope tracks. ──
        o.AppendLine("  ── HELD-OUT COMPRESSION — each arm's FINAL grammar parses the OTHER arm's discovered corpus (lower = deeper structure) ──");
        var loopGrammar = Engine.Induce(loop.Tape).Result;
        var passGrammar = Engine.Induce(pass.Tape).Result;
        var loopCover = new Engine.GrammarCover(loopGrammar.Rules);
        var passCover = new Engine.GrammarCover(passGrammar.Rules);
        double loopOnPass = loopCover.ParsedSizePerByte(pass.Tape);       // LOOP's grammar on PASS's corpus
        double passOnLoop = passCover.ParsedSizePerByte(loop.Tape);       // PASS's grammar on LOOP's corpus
        double loopSelf = loopCover.ParsedSizePerByte(loop.Tape);
        double passSelf = passCover.ParsedSizePerByte(pass.Tape);
        o.AppendLine($"     LOOP grammar → PASS corpus  ParsedSize/B {loopOnPass:F4}   (LOOP grammar → own corpus {loopSelf:F4})");
        o.AppendLine($"     PASS grammar → LOOP corpus  ParsedSize/B {passOnLoop:F4}   (PASS grammar → own corpus {passSelf:F4})");
        o.AppendLine($"     ⇒ the LOOP grammar {(loopOnPass < passOnLoop ? "parses held-out identities DEEPER" : "does not parse held-out deeper")} " +
                     $"(LOOP-on-held {loopOnPass:F4} vs PASS-on-held {passOnLoop:F4}) — the developed grammar's compression reach on structure it did not itself generate.");
        o.AppendLine();

        Console.Write(o.ToString());

        // ── the durable arc (surplus — the object of study) ──
        run.Write("emlbench_summary.txt", o.ToString());
        run.Write("loop_ticks.tsv", TicksTsv(loop));
        run.Write("pass_ticks.tsv", TicksTsv(pass));
        run.Write("loop_mints.txt", Encoding.ASCII.GetString(loop.Tape));
        run.Write("pass_mints.txt", Encoding.ASCII.GetString(pass.Tape));
        Trace.Note($"emlbench · arc → {Path.GetFileName(run.Dir)}/ (summary · loop/pass ticks · loop/pass mint corpora — the pass-vs-loop slope is the object)");
    }

    // ── the per-third aggregations (the slope reads) ──
    // new theorem-classes minted in each iteration-third — the label-free discovery slope. Read from the per-step
    // new-class deltas so it is the ACTUAL per-step production, not a hindsight recount.
    private static (int[] Thirds, int Total) ThirdClasses(ArmRun a, int steps)
    {
        var t = new int[3]; int total = 0;
        foreach (var tk in a.Ticks) { t[Third(tk.Step, steps)] += tk.NewClassesThisStep; total += tk.NewClassesThisStep; }
        return (t, total);
    }

    private static int[] ThirdHeld(ArmRun a, int steps)
    {
        var t = new int[3];
        foreach (var tk in a.Ticks) t[Third(tk.Step, steps)] += tk.NewHeldThisStep;
        return t;
    }

    // K-frontier at the END of each third (the depth reached by then — a level, not a rate).
    private static int[] ThirdK(ArmRun a, int steps)
    {
        var t = new int[3];
        foreach (var tk in a.Ticks) t[Third(tk.Step, steps)] = tk.KFrontier;   // last write wins = end-of-third level
        return t;
    }

    private static int Third(int step, int steps) => Math.Min(2, step * 3 / Math.Max(1, steps));

    // trajectory glyph — flat / rising / falling across the three thirds (the eye's quick read).
    private static string Traj(int[] thirds)
    {
        if (thirds[2] > thirds[0] && thirds[2] >= thirds[1]) return "RISING ↗ (sustained/growing)";
        if (thirds[2] < thirds[0] && thirds[2] <= thirds[1]) return "FALLING ↘ (saturating)";
        if (thirds[0] == thirds[1] && thirds[1] == thirds[2]) return "FLAT →";
        return "MIXED ↝";
    }

    private static string TicksTsv(ArmRun a)
    {
        var sb = new StringBuilder("step\tmint_classes\texact_classes\tnew_classes\tnew_held\tk_frontier\tdistinct_values\tcvz\tgrammar_bytes\n");
        foreach (var t in a.Ticks)
            sb.AppendLine($"{t.Step}\t{t.MintClasses}\t{t.ExactClasses}\t{t.NewClassesThisStep}\t{t.NewHeldThisStep}\t{t.KFrontier}\t{t.DistinctValues}\t{t.CvZ:R}\t{t.GrammarBytes}");
        return sb.ToString();
    }

    private static double MaxD(IEnumerable<double> xs) { double m = 0; foreach (var x in xs) if (x > m) m = x; return m; }

    // sparkline over a SHARED max (both arms on the same scale — the two lines are directly comparable).
    private static string SparkJoint(IEnumerable<double> xs, double max)
    {
        const string R = "▁▂▃▄▅▆▇█";
        var vals = xs.ToList();
        if (vals.Count == 0 || max <= 0) return new string('▁', Math.Max(1, vals.Count));
        var sb = new StringBuilder(vals.Count);
        int cols = Math.Min(64, vals.Count);
        for (int c = 0; c < cols; c++)
        {
            double v = vals[(int)((long)c * vals.Count / cols)];
            sb.Append(R[Math.Clamp((int)(v / max * (R.Length - 1)), 0, R.Length - 1)]);
        }
        return sb.ToString();
    }

    // sparkline over the series' OWN range (the shape of one line — for the per-step rate where the absolute scale
    // is small and the SHAPE is the read).
    private static string SparkOwn(IEnumerable<double> xs)
    {
        var vals = xs.ToList();
        double max = MaxD(vals);
        return SparkJoint(vals, max);
    }
}
