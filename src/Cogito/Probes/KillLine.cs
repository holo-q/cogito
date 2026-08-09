namespace Cogito;

using System.Text;

// ── THE KILL-LINE ──  the PRE-REGISTERED falsification battery for the multi-node WITNESS hypothesis (monk 1's
// falsifiers, as a runnable manifest + checker). The single-node self-play (trunk_0301) DEGENERATED: dreams had no
// witness, so vest-rate cliffed 55.6%→3.5% and the mind converged BELOW the hard-fork ceiling. The fix is the
// multi-node mesh — a peer node's span is the generator-INDEPENDENT witness node0's own echoes are not (Provenance's
// cross-reflection gate). This verb pre-registers the experiment that PROVES it, so the run is READABLE before it runs
// and JUDGED against conditions fixed in advance — the flagship-saga lesson (a run wasted measuring maxSpan while the
// real story, vest-by-source, was invisible) turned into a discipline: the pass/fail lines are written FIRST.
//
// FOUR ARMS, one substrate (campfire · --wscale 8 · seed 3222368273 — a clean A/B vs trunk_0250's hard-fork ceiling
// cov 0.968/maxSpan 592 and the degenerate trunk_0301):
//   a · node0-only         the DEGENERATE CONTROL — one voice, no mesh. Replay-vests must FREEZE post-drain (nothing
//                          but node0 to witness node0 ⇒ the sealed loop reproduces trunk_0301).
//   b · 2-node             two minds over distinct domains. Must converge to a rank-1 MIRROR (mutual witness, but a
//                          two-body loop has one shared mode — vest sustains yet the ensemble collapses to one axis).
//   c · 3-node symmetric   THE HYPOTHESIS — three minds. Must SUSTAIN (a 3-body mesh percolates: each node witnessed
//                          by TWO independent others, no single mirror to collapse into).
//   d · world-force-drain  a CONTROL — the single node with the world-stream forced to drain (isolates whether the
//                          freeze is the missing MESH or merely a starved intake).
//
// THE FALSIFIERS (pre-registered — a run PASSES iff ALL hold; --check reads the landed curve and grades each):
//   F1 dream-vests DON'T freeze post-drain          (the mesh keeps corroborating when the corpus is dry)
//   F2 meanz holds −0.70, not −1.08                 (the sealed-loop signature is −1.08; the mesh stays critical)
//   F3 seam_cross reproduces 30–190×                (the cross-mind weave, the multinode-probe's measured band)
//   F4 novelChain stays 5–7 — WIDTH not depth       (FLAT is a PASS; breaking the freeze ≠ adding depth)
//   F5 the 2-node arm CONVERGES where 3 SUSTAINS    (proves 3 = the percolation threshold, not just "more nodes")
//
// The verb has two faces: BARE prints the manifest (the exact run-command per arm + the falsifiers) — the Captain
// launches those; `--check <run-dir>...` reads each landed curve.tsv and grades the falsifiers it can see there
// (vest-freeze + meanz + novelChain off ANY curve; the cross-arm F5 when given both the 2-node and 3-node dirs).

public static class KillLine
{
    // the pre-registered substrate constants — the SAME across every arm, so the only variable is the node count
    // (the clean A/B: one knob moved). campfire schools code+NL + vest-gated EML; the source-independent gate lets a
    // Replay vest only when a DIFFERENT source exercises it; seed + steps stay fixed so the arms are comparable.
    private const string Seed = "3222368273";
    private const int Steps = 400;

    public static int Run(string[] args)
    {
        var check = Args.Str(args, "--check", "");
        if (check.Length > 0 || Args.Has(args, "--check"))
            return Check(Args.Positionals(args, 1, "--check").Where(p => p != "--check").ToList(), args);
        Manifest(args);
        return 0;
    }

    // ── THE MANIFEST ──  the pre-registration: each arm's exact command + the falsifiers, printed for the Captain to
    // launch. corpusDir is the campfire corpus root (a directory of *.cs/*.py/*.md → one domain per file); the 2/3-node
    // arms need 2/3 DISTINCT-domain corpora (Worker B's mesh takes them as positional args — node count = arg count).
    private static void Manifest(string[] args)
    {
        string dir = Args.Str(args, "--corpus", "<corpus-dir>");
        string c1 = Args.Str(args, "--c1", "<domain1>");
        string c2 = Args.Str(args, "--c2", "<domain2>");
        string c3 = Args.Str(args, "--c3", "<domain3>");

        var o = new StringBuilder();
        o.AppendLine("── THE MULTI-NODE WITNESS KILL-LINE · pre-registered (the pass/fail lines fixed BEFORE the run) ──");
        o.AppendLine($"   substrate: campfire · wscale 8 · source-independent corroboration · seed {Seed} · {Steps} steps");
        o.AppendLine($"   baseline A/B: trunk_0250 (hard-fork ceiling cov 0.968 / maxSpan 592) · trunk_0301 (the degenerate sealed loop)");
        o.AppendLine();
        o.AppendLine("   ARMS (launch each; --check the landed run dirs after):");
        o.AppendLine($"     a · node0-only  [degenerate control — dream-vests FREEZE post-drain]");
        o.AppendLine($"         cogito cortex {dir} {Steps} --curriculum campfire --wscale 8 --dreamratio 1.0 --seed {Seed}");
        o.AppendLine($"     b · 2-node      [rank-1 MIRROR]");
        o.AppendLine($"         cogito mesh {c1} {c2} --steps {Steps} --wscale 8 --seed {Seed}");
        o.AppendLine($"     c · 3-node sym  [SUSTAIN — THE HYPOTHESIS]");
        o.AppendLine($"         cogito mesh {c1} {c2} {c3} --steps {Steps} --wscale 8 --seed {Seed}");
        o.AppendLine($"     d · world-force-drain  [control — starved-intake isolation]");
        o.AppendLine($"         cogito cortex {dir} {Steps} --curriculum campfire --wscale 8 --mix 1 --seed {Seed}");
        o.AppendLine();
        o.AppendLine("   FALSIFIERS (the run PASSES iff ALL hold):");
        o.AppendLine("     F1  dream-vests DON'T freeze post-drain       (vest_peer keeps climbing after the corpus drains)");
        o.AppendLine("     F2  meanz holds −0.70, not −1.08");
        o.AppendLine("     F3  seam_cross reproduces 30–190×             (the cross-mind weave — the multinode-probe band)");
        o.AppendLine("     F4  novelChain stays 5–7 — WIDTH not depth    (FLAT is a PASS; breaking the freeze ≠ adding depth)");
        o.AppendLine("     F5  the 2-node CONVERGES where 3 SUSTAINS     (3 = the percolation threshold, not just 'more nodes')");
        o.AppendLine();
        o.AppendLine("   grade a landed run:  cogito killline --check <arm-c-run-dir>            (F1/F2/F4 off one curve)");
        o.AppendLine("                        cogito killline --check <2-node-dir> <3-node-dir>  (+ F5 the cross-arm convergence)");
        Console.Write(o.ToString());
    }

    // ── THE CHECKER ──  read each landed curve.tsv and grade the falsifiers it carries. F1/F2/F4 read a single curve
    // (the 3-node arm is the hypothesis to confirm); F5 needs the 2-node AND 3-node curves to compare convergence. The
    // curve schemas DIFFER across arms (trunk's 50-col vs mesh's 10-col), so every read is BY HEADER NAME — a
    // column absent from an arm's curve grades as "n/a (column not in this arm's curve)", never a false fail.
    private static int Check(List<string> dirs, string[] args)
    {
        if (dirs.Count == 0)
        {
            Console.Error.WriteLine("  usage: killline --check <run-dir> [<run-dir2>]   — grade the pre-registered falsifiers against landed curve(s)");
            return 1;
        }
        var curves = new List<(string Name, Curve C)>();
        foreach (var d in dirs)
        {
            var resolved = Cogito.Run.Resolve(d);
            var path = resolved is not null ? Path.Combine(resolved, "curve.tsv") : Path.Combine(d, "curve.tsv");
            if (!File.Exists(path)) { Console.Error.WriteLine($"  ✗ no curve.tsv under '{d}'"); return 1; }
            curves.Add((Path.GetFileName(resolved ?? d), Curve.Load(path)));
        }

        int fails = 0;
        void Grade(bool? ok, string id, string detail)
        {
            string mark = ok is null ? "  ·  " : ok.Value ? "  ✓  " : "  ✗  ";
            if (ok == false) fails++;
            Console.WriteLine($"  {mark}{id,-4} {detail}");
        }

        Console.WriteLine($"── KILL-LINE CHECK · {string.Join(" · ", curves.Select(c => c.Name))} — grading the pre-registered falsifiers ──");

        // the hypothesis curve = the LAST given (convention: --check <2-node> <3-node>, or a single arm-c dir).
        var (hypName, hyp) = curves[^1];

        // F1 · dream-vests don't freeze post-drain — vest_peer must still be RISING in the run's last quartile (a
        //      frozen mesh has a flat tail; a live one keeps corroborating). Falls back to vest_rate where vest_peer
        //      is absent (a single-node curve has no peer column — there the freeze IS expected, so it grades n/a).
        {
            var vp = hyp.Col("vest_peer");
            if (vp is null) Grade(null, "F1", "vest_peer not in this curve (single-node arm — freeze is the EXPECTED control here)");
            else
            {
                double tail = TailSlope(vp);
                Grade(tail > 0, "F1", $"vest_peer last-quartile slope {tail:F3}/step (>0 ⇒ not frozen — the mesh keeps witnessing)");
            }
        }

        // F2 · meanz holds −0.70 not −1.08 — the sealed-loop signature is −1.08 (over-collapsed to a degenerate
        //      fixpoint); a healthy mesh stays near the −0.70 universality. Read the run's SETTLED meanz (last-decile
        //      median, NaN-skipping). PASS iff meanz ∈ (−0.90, −0.55] — clear of the −1.08 sink, in the critical band.
        {
            var mz = hyp.Col("meanz");
            if (mz is null) Grade(null, "F2", "meanz not in this curve");
            else
            {
                double settled = SettledMedian(mz);
                bool ok = !double.IsNaN(settled) && settled > -0.90 && settled <= -0.55;
                Grade(double.IsNaN(settled) ? (bool?)null : ok, "F2", $"settled meanz {settled:F3} (PASS in (−0.90,−0.55]; −1.08 = the sealed-loop sink)");
            }
        }

        // F4 · novelChain stays 5–7 — WIDTH not depth. The FIX must break the vest-freeze WITHOUT the mesh suddenly
        //      manufacturing deep units (that would be a different, suspicious result — the multinode-probe proved
        //      topology weaves WIDTH, never depth). PASS iff the settled novelChain sits in [5,7] — flat is the pass.
        {
            var nc = hyp.Col("novelchain");
            if (nc is null) Grade(null, "F4", "novelchain not in this curve");
            else
            {
                double settled = SettledMedian(nc);
                bool ok = settled >= 5 && settled <= 7;
                Grade(ok, "F4", $"settled novelChain {settled:F1} (PASS in [5,7] — WIDTH not depth; FLAT is a PASS)");
            }
        }

        // F5 · the 2-node CONVERGES where the 3-node SUSTAINS — the percolation-threshold proof. Needs BOTH curves.
        //      "Converge" = vest_rate PLATEAUS (its last-quartile slope → 0); "sustain" = vest_rate still climbing.
        //      So PASS iff |slope(2-node)| < slope(3-node): the 2-body mirror settles, the 3-body mesh keeps going.
        if (curves.Count >= 2)
        {
            var (n2, c2) = curves[0];
            var vr2 = c2.Col("vest_rate") ?? c2.Col("vest_peer");
            var vr3 = hyp.Col("vest_rate") ?? hyp.Col("vest_peer");
            if (vr2 is null || vr3 is null) Grade(null, "F5", "vest_rate/vest_peer missing in one arm — cannot compare convergence");
            else
            {
                double s2 = TailSlope(vr2), s3 = TailSlope(vr3);
                bool ok = Math.Abs(s2) < s3;   // 2-node flattening, 3-node still climbing
                Grade(ok, "F5", $"2-node({n2}) tail-slope {s2:F3} vs 3-node({hypName}) {s3:F3} (PASS: |2-node| < 3-node — the mirror converges, the mesh sustains)");
            }
        }
        else Grade(null, "F5", "needs BOTH the 2-node and 3-node run dirs (killline --check <2-node> <3-node>)");

        // F3 · seam_cross 30–190× is a CROSS-mind interleave read the curve doesn't carry (it's a per-pair weave
        //      measured over the samples, not a per-step scalar) — flagged as a manual/mesh-report read.
        Grade(null, "F3", "seam_cross is a per-pair weave read — see the mesh run's own combustion report (not a curve column)");

        Console.WriteLine(fails == 0
            ? "  ⇒ every gradable falsifier HELD — the multi-node witness hypothesis is CORROBORATED on this curve (F3 manual)."
            : $"  ⇒ {fails} falsifier{(fails == 1 ? "" : "s")} FAILED — the hypothesis is REFUTED on this curve (read the failing line's number).");
        return fails == 0 ? 0 : 1;
    }

    // ── curve reading (by header name — schema-independent across arms) ──

    /// A landed curve.tsv as named columns — the header maps a name to its column index; `Col` pulls a numeric column
    /// (NaN for blank/unparseable cells — a verdict-band column reads as all-NaN and every stat NaN-skips it).
    private sealed class Curve
    {
        private readonly Dictionary<string, int> _idx = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string[]> _rows = new();

        public static Curve Load(string path)
        {
            var c = new Curve();
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return c;
            var header = lines[0].Split('\t');
            for (int i = 0; i < header.Length; i++) c._idx[header[i]] = i;
            for (int r = 1; r < lines.Length; r++)
            {
                if (lines[r].Length == 0) continue;
                c._rows.Add(lines[r].Split('\t'));
            }
            return c;
        }

        /// The named column as doubles (NaN where a cell is blank or non-numeric), or null if the header lacks it.
        public double[]? Col(string name)
        {
            if (!_idx.TryGetValue(name, out int i)) return null;
            var v = new double[_rows.Count];
            for (int r = 0; r < _rows.Count; r++)
                v[r] = i < _rows[r].Length && double.TryParse(_rows[r][i], out var d) ? d : double.NaN;
            return v;
        }
    }

    /// Least-squares slope over the last quartile of a series (NaN-skipping) — the "still moving vs settled" read.
    /// 0 for <2 valid tail points. Index-vs-value, so units are Δcolumn/step.
    private static double TailSlope(double[] v)
    {
        int lo = v.Length - Math.Max(2, v.Length / 4);
        double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = 0;
        for (int i = Math.Max(0, lo); i < v.Length; i++)
        {
            if (double.IsNaN(v[i])) continue;
            sx += n; sy += v[i]; sxx += (double)n * n; sxy += (double)n * v[i]; n++;
        }
        if (n < 2) return 0;
        double det = n * sxx - sx * sx;
        return det == 0 ? 0 : (n * sxy - sx * sy) / det;
    }

    /// Median of the last decile (NaN-skipping) — the run's SETTLED value of a column, robust to a late spike.
    private static double SettledMedian(double[] v)
    {
        int lo = v.Length - Math.Max(1, v.Length / 10);
        var tail = new List<double>();
        for (int i = Math.Max(0, lo); i < v.Length; i++) if (!double.IsNaN(v[i])) tail.Add(v[i]);
        if (tail.Count == 0) return double.NaN;
        tail.Sort();
        return tail[tail.Count / 2];
    }
}
