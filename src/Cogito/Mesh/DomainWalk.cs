namespace Cogito;

using System.Text;
using Cogito.Induct;

// ── DOMAIN-WALK ──  the MULTI-NODE farm's missing organ: the engine's OWN cross-domain trajectory.
//
// The Farm is single-node (one grammar over one corpus, one coupling graph over its chunks) — it has NO
// domain concept, so the alien-chatter walk (a directed trajectory across maximally-distant DOMAINS via
// grounded cross-domain BRIDGES, cogito-gasp-produced) is still a Python probe, never the real machine.
//
// THE UNIFICATION (nodes = domains): a "multi-node" topology and a "domain-graph" are THE SAME OBJECT. Learn
// ONE coupling graph over the UNION of several distinct-domain corpora and it IS the domain-graph — every
// chunk (a Re-Pair idiom) carries DOMAIN PROVENANCE (which corpus its bytes recur in), and a BRIDGE is a
// coupling edge whose two endpoints live in DIFFERENT domains (a cross-domain co-activation above chance —
// the couplings' PPMI finds it). A NODE in the multi-node sense is a DOMAIN; the multi-node coupling-walk is
// a walk over that graph that CROSSES domains via bridges. The single-node Farm's within-domain composition
// is exactly this graph with the domain labels erased and the crossing drive absent.
//
// WHY THIS IS THE RIGHT HALF TO BUILD: the multi-node probe proved the coupling topology WEAVES minds (real
// cross-mind threading, 30–190× the interleave null) but adds no DEPTH (cogito-multinode-probe). The gasp is
// a WIDTH phenomenon — moving through 16 distant domains — not a depth one, so the proven WEAVE IS the domain-
// walk substrate. This verb is the C# half of the chatter architecture (the deterministic structural PATH
// across domains); the coherent RENDER of that path is the LLM's half, proven separately (cogito-gasp-produced).
//
// THE PROOF IT LANDS: the crossing-biased walk vs a plain-φ control on the SAME graph — does the real C#
// coupling graph carry cross-domain bridges the walk can actually TRAVERSE (edge-crossings, not teleports),
// yielding a coherent cross-domain trajectory? Determinism is total (greedy argmax, id tie-break — the Vow).

public static class DomainWalk
{
    private const int RecentDoms = 3;                    // crossing memory: prefer domains not among the last 3 visited
    private const int RecentUnits = 6;                   // anti-repeat: never step back onto the last 6 units

    /// usage: domainwalk <file1> <file2> [...files] [--steps N] [--cross F] [--perdomain BYTES] [--render N]
    public static int Run(string[] args)
    {
        var files = Args.Positionals(args, 1).Where(File.Exists).ToList();
        if (files.Count < 2)
        {
            Console.Error.WriteLine("  usage: domainwalk <file1> <file2> [...] [--steps N] [--cross F] [--perdomain BYTES] [--render N]");
            Console.Error.WriteLine("  each file is a DOMAIN; the union induces one coupling graph, the walk crosses domains via bridges.");
            return 1;
        }
        int steps    = Args.Int(args, "--steps", 60);
        double cross = Args.Double(args, "--cross", 3.0);
        int perDom   = Args.Int(args, "--perdomain", 180_000);
        int render   = Args.Int(args, "--render", 44);

        // ── THE DOMAIN GRAPH ──  one coupling graph over the union of the domain corpora (each file a domain, capped
        // at perDom bytes), with per-chunk provenance + the folded bridge matrix. The union/induce/provenance/fold
        // pipeline this verb used to carry inline is now the shared DomainGraph; this verb keeps its half:
        // it RENDERS + WALKS the graph the curriculum ORDERS.
        var blocks = new (string Name, byte[] Block)[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            var bytes = File.ReadAllBytes(files[i]);
            if (bytes.Length > perDom) bytes = bytes[..perDom];
            blocks[i] = (Path.GetFileNameWithoutExtension(files[i]), bytes);
        }
        var graph = DomainGraph.Build(blocks, Couplings.DefaultWindow);
        int D = graph.D;
        var cp = graph.Cp; var rich = graph.Rich; var dom = graph.Dom;
        var (vocab, _) = cp.Vocabulary();
        long unionBytes = 0; foreach (var s in graph.Sizes) unionBytes += s;

        // ── THE WALKS ──  crossing-biased (nodes=domains) vs plain-φ control, same seed-free greedy.
        var biased  = Walk(cp, rich, dom, D, steps, cross);
        var control = Walk(cp, rich, dom, D, steps, 0.0);

        // ── REPORT ──  the report IS the payload (world boundary → stdout).
        var o = new StringBuilder();
        o.AppendLine($"domainwalk · {D} domains · {unionBytes}B union → {graph.Grammar.Rules.Length} rules, {graph.Grammar.Compressed.Length} tape · {vocab.Length} coupled chunks · W={cp.Window}");
        o.AppendLine();
        o.AppendLine("── domains (nodes) ─────────────────────────────────────────");
        for (int i = 0; i < D; i++) o.AppendLine($"  [{i}] {graph.Names[i],-16} {graph.Sizes[i],8}B  ·  {graph.Populations[i],5} domain-chunks");
        o.AppendLine($"  [·] shared substrate      ·  {graph.SharedChunks,5} chunks (in ≥2 domains, no dominant)");
        o.AppendLine();
        o.AppendLine("── the domain graph ────────────────────────────────────────");
        o.AppendLine($"  {graph.UndirectedEdges} undirected coupling edges · {graph.IntraEdges} intra-domain · {graph.Bridges.Count} CROSS-DOMAIN BRIDGES");
        o.AppendLine("  bridge matrix (rows→cols, count of cross-domain coupling edges):");
        o.Append("        ");
        for (int j = 0; j < D; j++) o.Append($"{Short(graph.Names[j]),8}");
        o.AppendLine();
        for (int i = 0; i < D; i++)
        {
            o.Append($"  {Short(graph.Names[i]),6}");
            for (int j = 0; j < D; j++) o.Append($"{(i == j ? "·" : graph.EdgeCounts[i, j].ToString()),8}");
            o.AppendLine();
        }
        o.AppendLine();
        o.AppendLine("── top bridges (highest-φ cross-domain couplings) ──────────");
        foreach (var br in graph.Bridges.Take(12))
            o.AppendLine($"  φ={br.Phi,6:F2}  [{Short(graph.Names[br.Da])}] {Label(cp.Expand(br.A)),-26} ⇄  [{Short(graph.Names[br.Db])}] {Label(cp.Expand(br.B))}");
        o.AppendLine();
        o.AppendLine("── the walks ───────────────────────────────────────────────");
        o.AppendLine(WalkStats("crossing-biased (multi-node)", biased, D));
        o.AppendLine(WalkStats("plain-φ control (single-node)", control, D));
        o.AppendLine();
        o.AppendLine($"── crossing-biased trajectory (first {render} steps) ───────");
        RenderWalk(o, cp, graph.Names, biased, render);
        o.AppendLine();
        o.AppendLine($"── control trajectory (first {render} steps) ───────────────");
        RenderWalk(o, cp, graph.Names, control, render);
        o.AppendLine();
        o.AppendLine("── verdict ─────────────────────────────────────────────────");
        o.AppendLine(Verdict(biased, control, graph.Bridges.Count, D));
        Console.Write(o.ToString());
        return 0;
    }

    // ── the walk ──  greedy, fully deterministic. State = current unit + a window of recent domains/units.
    // Each step: from cur's φ-forward neighbours, score = φ + cross·(neighbour enters a domain NOT recently
    // visited). Argmax with id tie-break. A dead-end TELEPORTS to the top-frequency chunk of the least-recently
    // visited domain (counted separately — a teleport is NOT a walked bridge). The crossing bonus is the ONLY
    // difference from the control, so any gain in domains-crossed is attributable to it alone.
    private sealed record WalkResult(
        List<(int Dom, uint Unit)> Trail, int DistinctDomains, int EdgeCrossings, int Teleports, double MeanPhi, int LongestRun);

    private static WalkResult Walk(Couplings cp, Scorer rich, IReadOnlyDictionary<uint, int> dom, int D, int steps, double cross)
    {
        uint cur = 0; long bestC = -1;
        foreach (var (u, c) in cp.Marginals)               // start at the highest-marginal DOMAIN-specific chunk
            if (dom.GetValueOrDefault(u, DomainGraph.Shared) >= 0 && (c > bestC || (c == bestC && u < cur))) { bestC = c; cur = u; }

        var recentDom = new Queue<int>(); var recentUnit = new Queue<uint>();
        var trail = new List<(int, uint)>(steps);
        double phiSum = 0; int edgeCross = 0, teleports = 0;

        for (int s = 0; s < steps; s++)
        {
            int cd = dom.GetValueOrDefault(cur, DomainGraph.Shared);
            trail.Add((cd, cur));

            uint next = 0; double bestScore = double.NegativeInfinity, pickedPhi = 0; bool found = false;
            foreach (var (b, phi) in rich.Fwd(cur))
            {
                if (recentUnit.Contains(b)) continue;
                int bd = dom.GetValueOrDefault(b, DomainGraph.Shared);
                double score = phi + (cross > 0 && bd >= 0 && !recentDom.Contains(bd) ? cross : 0);
                if (!found || score > bestScore || (score == bestScore && b < next)) { bestScore = score; next = b; pickedPhi = phi; found = true; }
            }
            bool teleported = false;
            if (!found) { next = Teleport(cp, dom, D, recentDom, out pickedPhi); teleported = true; teleports++; }

            int nd = dom.GetValueOrDefault(next, DomainGraph.Shared);
            if (!teleported && cd >= 0 && nd >= 0 && cd != nd) edgeCross++;   // a domain change via a REAL coupling edge = a walked bridge
            phiSum += pickedPhi;
            cur = next;
            recentDom.Enqueue(nd); if (recentDom.Count > RecentDoms) recentDom.Dequeue();
            recentUnit.Enqueue(next); if (recentUnit.Count > RecentUnits) recentUnit.Dequeue();
        }

        var distinct = new HashSet<int>(); int longest = 0, run = 0, last = -2;
        foreach (var (d, _) in trail) { if (d >= 0) distinct.Add(d); if (d == last) run++; else { run = 1; last = d; } if (run > longest) longest = run; }
        return new WalkResult(trail, distinct.Count, edgeCross, teleports, steps > 0 ? phiSum / steps : 0, longest);
    }

    // dead-end recovery: the top-marginal chunk of the least-recently-visited domain (deterministic). Not a
    // walked bridge — it is the graph telling us the couplings ran out, so the crossing had to be forced.
    private static uint Teleport(Couplings cp, IReadOnlyDictionary<uint, int> dom, int D, Queue<int> recentDom, out double phi)
    {
        phi = 0;
        for (int d = 0; d < D; d++)
        {
            if (recentDom.Contains(d)) continue;
            uint best = 0; long bestC = -1;
            foreach (var (u, c) in cp.Marginals) if (dom.GetValueOrDefault(u, DomainGraph.Shared) == d && (c > bestC || (c == bestC && u < best))) { bestC = c; best = u; }
            if (bestC >= 0) return best;
        }
        uint any = 0; long ac = -1;                        // every domain recently visited — highest-marginal domain chunk
        foreach (var (u, c) in cp.Marginals) if (dom.GetValueOrDefault(u, DomainGraph.Shared) >= 0 && (c > ac || (c == ac && u < any))) { ac = c; any = u; }
        return any;
    }

    private static string WalkStats(string name, WalkResult w, int D) =>
        $"  {name,-30}  domains {w.DistinctDomains}/{D}  ·  edge-bridges {w.EdgeCrossings}  ·  teleports {w.Teleports}  ·  meanφ {w.MeanPhi:F2}  ·  longest single-domain run {w.LongestRun}";

    private static void RenderWalk(StringBuilder o, Couplings cp, IReadOnlyList<string> names, WalkResult w, int render)
    {
        int last = -2;
        foreach (var (d, u) in w.Trail.Take(render))
        {
            string tag = d >= 0 ? Short(names[d]) : "·shared";
            string arrow = (d != last && last != -2 && d >= 0) ? " ⇄" : "  ";
            o.AppendLine($"   {arrow} [{tag,-7}] {Label(cp.Expand(u))}");
            last = d;
        }
    }

    private static string Verdict(WalkResult b, WalkResult c, int bridges, int D)
    {
        var s = new StringBuilder();
        bool graphHasBridges = bridges > 0;
        bool walkTraverses = b.EdgeCrossings > 0;
        bool biasWidens = b.DistinctDomains > c.DistinctDomains || b.EdgeCrossings > c.EdgeCrossings;
        bool coherenceHeld = b.MeanPhi >= 0.5 * c.MeanPhi;   // crossing did not crater coherence into salad
        s.AppendLine($"  coupling graph carries cross-domain bridges : {(graphHasBridges ? "YES" : "no")} ({bridges} bridge edges)");
        s.AppendLine($"  walk TRAVERSES bridges via real edges       : {(walkTraverses ? "YES" : "no")} ({b.EdgeCrossings} edge-crossings vs {c.EdgeCrossings} control)");
        s.AppendLine($"  crossing bias widens the trajectory         : {(biasWidens ? "YES" : "no")} ({b.DistinctDomains}/{D} vs {c.DistinctDomains}/{D} domains)");
        s.AppendLine($"  coherence held (φ not cratered to salad)     : {(coherenceHeld ? "YES" : "no")} (meanφ {b.MeanPhi:F2} vs {c.MeanPhi:F2})");
        s.Append(graphHasBridges && walkTraverses && biasWidens
            ? "  ⇒ the real C# coupling graph IS a walkable domain-graph — the multi-node cross-domain walk is the engine's OWN structural path (the LLM renders it, cogito-gasp-produced)."
            : graphHasBridges
                ? "  ⇒ the graph HAS bridges but the walk cannot traverse them into a wider trajectory without teleport — bridges too sparse/weak at this grain (honest negative)."
                : "  ⇒ NO cross-domain bridges at this grain — the domains are coupling-disjoint (need finer grain or closer domains).");
        return s.ToString();
    }

    // ── helpers ──
    private static string Short(string name) => name.Length <= 7 ? name : name[..7];

    private static string Label(byte[] e)
    {
        var t = Encoding.UTF8.GetString(e).Replace("\n", "⏎").Replace("\t", "→").Replace("\r", "");
        return t.Length > 26 ? t[..26] : t;
    }

}
