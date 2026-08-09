namespace Cogito;


// ── SOLVEDIAGNOSTICS ──  the corroboration⊥discrimination measurement owned by the Cortex LOC curriculum. The LOC
// runtime is a CORROBORATION engine (vest = breadth of corroboration); this accumulates the per-instance DISCRIMINATION
// samples it feeds — the committed leader's COHERENCE (parse-depth self-model) and vote MARGIN, split by
// correctness — and renders the verdict that answers whether either signal SEPARATES right from wrong at rank time
// (a within-instance re-rank vs a selective-commit gate). Fed once per committed instance (Record), rendered into
// the run's verdict + report.txt; its accumulators ride the LOC curriculum's Cortex checkpoint as one flat section.
internal sealed class SolveDiagnostics
{
    // the actionability accumulators — committed-leader coherence + vote-margin, summed by correctness (the
    // corroboration⊥discrimination test: does either signal separate right from wrong at rank time?).
    private double _cohCorrectSum, _cohWrongSum, _marginCorrectSum, _marginWrongSum;
    private int _correctDiag, _wrongDiag;
    private readonly List<(double Coh, double Margin, bool Correct, int Looks)> _gate = new();   // per-instance (coherence, final-margin, correct, looks) — the escalation/gate read, the coherence-vs-margin bake-off, AND the margin-adaptive compute distribution

    /// Fold one committed instance's discrimination sample: the committed leader's coherence + normalized vote-margin,
    /// bucketed by correctness (the summed separation) and appended to the per-instance gate curve. Called only when
    /// the instance had candidates to diagnose (dCand > 0 at the LOC runtime's commit).
    public void Record(double coh, double margin, bool correct, int looks)
    {
        if (correct) { _cohCorrectSum += coh; _marginCorrectSum += margin; _correctDiag++; }
        else { _cohWrongSum += coh; _marginWrongSum += margin; _wrongDiag++; }
        _gate.Add((coh, margin, correct, looks));
    }

    // Checkpoint accumulators as one order-fixed section between the LOC curriculum's commit-calibration homeostat
    // and replay-diet state.
    public void Save(CkptWriter w)
    {
        w.F64(_cohCorrectSum); w.F64(_cohWrongSum); w.F64(_marginCorrectSum); w.F64(_marginWrongSum);
        w.I32(_correctDiag); w.I32(_wrongDiag);
        w.I32(_gate.Count);
        foreach (var g in _gate) { w.F64(g.Coh); w.F64(g.Margin); w.Bool(g.Correct); w.I32(g.Looks); }
    }

    public void Load(CkptReader r)
    {
        _cohCorrectSum = r.F64(); _cohWrongSum = r.F64(); _marginCorrectSum = r.F64(); _marginWrongSum = r.F64();
        _correctDiag = r.I32(); _wrongDiag = r.I32();
        int ng = r.I32();
        _gate.Clear();
        for (int i = 0; i < ng; i++) _gate.Add((r.F64(), r.F64(), r.Bool(), r.I32()));
    }

    /// THE ACTIONABILITY VERDICT — the committed-leader COHERENCE and vote-MARGIN, mean over CORRECT vs WRONG
    /// instances. A positive coherence-separation (correct > wrong) is the rev worker's deep-parse signal showing
    /// AT rank time; a positive margin-separation is the incompleteness signal. The SIGN + SIZE
    /// of each gap says which discriminator the converter can actually route on (and whether it's a re-rank or a
    /// gate). Reported always; one run diagnoses whether the homeostat is receiving a usable confidence signal.
    public string ActionabilityLine()
    {
        double cohC = _correctDiag > 0 ? _cohCorrectSum / _correctDiag : 0, cohW = _wrongDiag > 0 ? _cohWrongSum / _wrongDiag : 0;
        double marC = _correctDiag > 0 ? _marginCorrectSum / _correctDiag : 0, marW = _wrongDiag > 0 ? _marginWrongSum / _wrongDiag : 0;
        return $"  ACTIONABILITY · committed-leader signal, mean over correct({_correctDiag}) vs wrong({_wrongDiag}):\n"
             + $"      coherence  correct={cohC:F3}  wrong={cohW:F3}  sep={(cohC - cohW >= 0 ? "+" : "")}{cohC - cohW:F3}  ({(Math.Abs(cohC - cohW) < 0.02 ? "FLAT — coherence does NOT discriminate at rank time (the within-instance re-rank is a no-op)" : cohC > cohW ? "coherence separates right>wrong — actionable AS A GATE" : "INVERTED — coherence higher on wrong")})\n"
             + $"      margin     correct={marC:F3}  wrong={marW:F3}  sep={(marC - marW >= 0 ? "+" : "")}{marC - marW:F3}  ({(marC - marW > 0.03 ? "margin separates right>wrong too" : "flat")})\n"
             + GateLine();
    }

    /// THE GATE CURVE + THE BAKE-OFF — the ACTIONABLE form of the self-signal, and WHICH self-signal converts.
    /// Both coherence and margin are per-INSTANCE (uniform across candidates → no within-instance re-rank), so the
    /// lever is SELECTIVE COMMIT: sort instances by the signal, read accuracy over the confident HEAD. A head far
    /// cleaner than overall = the LOC runtime's self-knowledge-of-when-it's-right is convertible AS A GATE (the DISCOVERY
    ///  selective-escalation shape, 76%→92%). Reporting BOTH sorts side-by-side is the direct coherence-vs-
    /// margin bake-off: the steeper head names the actionable discriminator.
    private string GateLine()
    {
        if (_gate.Count < 4) return "      gate · too few instances for the sorted-head read";
        int n = _gate.Count;
        double overall = 100.0 * _gate.Count(x => x.Correct) / n;
        int q25 = Math.Max(1, n / 4), q33 = Math.Max(1, n / 3), q50 = Math.Max(1, n / 2);
        string Curve(string label, Func<(double Coh, double Margin, bool Correct, int Looks), double> key)
        {
            var s = _gate.OrderByDescending(key).ToList();
            double Acc(int k) { int ok = 0; for (int i = 0; i < k; i++) if (s[i].Correct) ok++; return k > 0 ? 100.0 * ok / k : 0; }
            double tail = q50 < n ? 100.0 * s.Skip(q50).Count(x => x.Correct) / (n - q50) : 0;
            return $"      GATE({label,-16}) · top-25% {Acc(q25),5:F1}% · top-33% {Acc(q33),5:F1}% · top-50% {Acc(q50),5:F1}% · bottom-50% {tail,5:F1}%";
        }
        // the winner: which signal's top-25% head is highest (the sharpest confident-commit slice).
        double cohHead = HeadAcc(_gate.OrderByDescending(x => x.Coh).ToList(), q25);
        double marHead = HeadAcc(_gate.OrderByDescending(x => x.Margin).ToList(), q25);
        string winner = Math.Abs(cohHead - marHead) < 3 ? "coherence≈margin (both gate)" : marHead > cohHead
            ? $"MARGIN wins ({marHead:F1}% vs coherence {cohHead:F1}%) — the vote-margin is the actionable converter"
            : $"COHERENCE wins ({cohHead:F1}% vs margin {marHead:F1}%) — the LOC runtime's parse-depth self-model is the actionable converter";
        return $"      overall {overall:F1}% ({n} instances) · the confident HEAD vs the escalate-me TAIL:\n"
             + Curve("coherence-sorted", x => x.Coh) + "\n"
             + Curve("margin-sorted", x => x.Margin) + "\n"
             + $"          → {winner}\n"
             + ComputeDistLine();
    }

    /// THE COMMIT-HOMEOSTAT ACTION DISTRIBUTION — the direct readout for "does the LOC runtime spend where it's uncertain".
    /// Three reads, all off the per-instance (final-margin, correct, looks) samples:
    ///   (1) LOOKS-BY-MARGIN — bucket instances by final-margin (thin/mid/wide) and report mean looks per bucket. If
    ///       the thin-margin bucket burned MORE looks than the wide-margin one, the adaptive commit worked: compute
    ///       flowed to the hard (uncertain) instances, not uniformly. Flat looks-across-buckets = the homeostat floor
    ///       never made the LOC runtime walk further, or every commit candidate was already certain.
    ///   (2) ACCURACY-BY-FINAL-MARGIN — the gate-validity read: do the instances that REACHED a high final margin
    ///       score high? A monotone accuracy-climb with margin = the margin is a TRUE confidence signal (the commit
    ///       gate is well-founded). Flat = the LOC runtime's margin is decoupled from correctness (the sub-clog: it can't
    ///       resolve its own uncertainty by searching — a named escalation need).
    ///   (3) THE TRADEOFF HEADLINE — mean looks/committed instance beside success@commit.
    private string ComputeDistLine()
    {
        int n = _gate.Count;
        if (n < 4) return "      compute-dist · too few instances";
        double meanLooks = _gate.Average(x => (double)x.Looks);
        int maxLooks = _gate.Max(x => x.Looks), minLooks = _gate.Min(x => x.Looks);
        double overall = 100.0 * _gate.Count(x => x.Correct) / n;
        // (1) looks spent, bucketed by FINAL margin (the uncertainty axis). Fixed cutpoints on the normalized margin
        // ∈ [0,1] so the buckets mean the same thing across runs: thin <0.33, mid [0.33,0.66), wide ≥0.66.
        string LooksBucket(string label, Func<double, bool> inBucket)
        {
            var b = _gate.Where(x => inBucket(x.Margin)).ToList();
            if (b.Count == 0) return $"        {label,-18} n=  0 · —";
            return $"        {label,-18} n={b.Count,3} · mean-actions {b.Average(x => (double)x.Looks),5:F2} · success@commit {100.0 * b.Count(x => x.Correct) / b.Count,5:F1}%";
        }
        // (2) accuracy climbing with the final margin — quartiles of the margin distribution (data-relative, so it
        // reads even when all margins cluster), each quartile's success@commit. Monotone up = the gate is well-founded.
        var byMargin = _gate.OrderBy(x => x.Margin).ToList();
        string MarginQuartile(int q)
        {
            int lo = q * n / 4, hi = (q + 1) * n / 4; if (hi <= lo) hi = lo + 1; if (hi > n) hi = n;
            var b = byMargin.GetRange(lo, hi - lo);
            double mLo = b[0].Margin, mHi = b[^1].Margin;
            return $"        Q{q + 1} margin[{mLo:F2}–{mHi:F2}] n={b.Count,3} · success@commit {100.0 * b.Count(x => x.Correct) / b.Count,5:F1}% · mean-actions {b.Average(x => (double)x.Looks),5:F2}";
        }
        return "      COMMIT-HOMEOSTAT ACTION DISTRIBUTION (does the LOC runtime spend actions where its margin is thin?):\n"
             + $"        TRADEOFF · mean {meanLooks:F2} actions/commit [{minLooks}–{maxLooks}] · success@commit {overall:F1}% · = {(meanLooks > 0 ? overall / meanLooks : 0):F2} success-pts/action\n"
             + "        actions spent by FINAL margin (thin=uncertain should burn MORE):\n"
             + LooksBucket("thin  (<0.33)", m => m < 0.33) + "\n"
             + LooksBucket("mid   [.33–.66)", m => m >= 0.33 && m < 0.66) + "\n"
             + LooksBucket("wide  (≥0.66)", m => m >= 0.66) + "\n"
             + "        accuracy by FINAL-margin quartile (monotone-up = the commit gate is well-founded):\n"
             + MarginQuartile(0) + "\n" + MarginQuartile(1) + "\n" + MarginQuartile(2) + "\n" + MarginQuartile(3);
    }

    private static double HeadAcc(List<(double Coh, double Margin, bool Correct, int Looks)> s, int k) { int ok = 0; for (int i = 0; i < k && i < s.Count; i++) if (s[i].Correct) ok++; return k > 0 ? 100.0 * ok / k : 0; }
}
