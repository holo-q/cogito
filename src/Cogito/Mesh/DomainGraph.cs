namespace Cogito;

using System.Text;
using Cogito.Induct;

// ── THE DOMAIN GRAPH ──  ONE coupling graph over the UNION of several distinct-domain corpora, carrying DOMAIN
// PROVENANCE on every chunk and folded to a domain×domain BRIDGE structure. This is the object two verbs were
// building TWICE: CritLock.BridgeOrder (the curriculum's cross-domain sequence) and
// DomainWalk (the multi-node cross-domain trajectory) each re-derived the SAME pipeline —
//
//   induce ONE grammar over the union → Couplings.Learn one PPMI graph over its chunks → attribute each chunk to
//   the domain its bytes recur in (DomShare provenance) → fold the couplings to a domain×domain bridge matrix
//
// and each carried its OWN copy of the DomainAt range-lookup (CritLock.cs:566 / DomainWalk.cs:269). One home now:
// DomainWalk renders + WALKS the graph, CritLock/GrokBell read its GREEDY BRIDGE-ORDER (the developmental
// curriculum's sequence — start at 0, always cross the WIDEST coupling-bridge into the adjacent un-visited
// domain). A BRIDGE is a coupling edge whose two endpoints live in DIFFERENT domains (a cross-domain co-activation
// above chance — the couplings' PPMI finds it). Bridge WIDTH is the EDGE COUNT between two domains, never the max
// φ: on a real multi-domain world the max-φ matrix SATURATES near-flat (every pair 9.45–10.04, ±3% — the top-φ
// "bridges" are chunking shrapnel, one word split across a domain junction) while the edge-count matrix spans
// 114× (go-web↔go-cli 1251 edges vs csharp↔toml 11) — the count carries the world's real coupling anisotropy,
// the peak measures only PPMI's ceiling. Deterministic end to end (id-sorted vocab, id tie-break — the Vow).

/// The union coupling graph with per-chunk domain provenance, folded to the domain×domain bridge products both the
/// curriculum sequence (GreedyOrder) and the domain-walk (Bridges / EdgeCounts / the walk itself) read. Built once
/// from per-domain byte blocks; the heavy induce + coupling-learn + provenance sweep happen in `Build`, the folded
/// products are cached fields, and `GreedyOrder` is the only post-build compute (cheap, over the D×D matrix).
public sealed class DomainGraph
{
    /// A chunk BELONGS to a domain iff ≥ this fraction of its occurrences land there — else it is SHARED substrate
    /// (in ≥2 domains, no dominant one). The provenance threshold both verbs used verbatim (0.6).
    public const double DomShare = 0.6;
    /// A chunk with no dominant domain — the common substrate that is NOT a bridge endpoint (a bridge needs two
    /// concrete domains). Kept as a named sentinel because the provenance map stores it as an ordinary value.
    public const int Shared = -1;

    public int D { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<long> Sizes { get; }               // each domain's union byte-extent (post-cap, +separator)
    public Couplings Cp { get; }                            // the learned coupling graph (Expand / Marginals — the walk's map)
    public Scorer Rich { get; }                             // the broad-reach PPMI scorer (minCocount 1 — the walk's edges)
    public RePairResult Grammar { get; }                    // the union grammar (the chunks the couplings live over)
    public IReadOnlyDictionary<uint, int> Dom { get; }      // chunk id → its dominant domain (Shared = common substrate)

    // ── the folded bridge products (domain × domain) — computed once, read by both consumers ──
    public double[,] MaxPhi { get; }                        // strongest cross-domain coupling φ between the two domains — a DIAGNOSTIC ceiling only (saturates near-flat on real worlds; never an ordering signal)
    public int[,] EdgeCounts { get; }                       // count of cross-domain coupling edges between the two domains (the bridge WIDTH — GreedyOrder / CritLock / DomainWalk's matrix)
    public IReadOnlyList<Bridge> Bridges { get; }           // every cross-domain edge, sorted strongest-φ first (id tie-break) — DomainWalk's top-bridges
    public int IntraEdges { get; }                          // undirected edges whose endpoints share a domain
    public int UndirectedEdges { get; }                     // total undirected coupling edges (both legs concrete or shared)
    public IReadOnlyList<int> Populations { get; }          // per-domain chunk count (domain-specific vocabulary size)
    public int SharedChunks { get; }                        // chunks in the shared substrate (no dominant domain)

    /// One cross-domain coupling edge — a bridge. `Phi` is the undirected max PPMI, `A`/`B` the coupled chunk ids,
    /// `Da`/`Db` their (distinct, concrete) domains. The walkable + orderable atom the two verbs both consume.
    public readonly record struct Bridge(double Phi, uint A, uint B, int Da, int Db);

    private DomainGraph(int d, string[] names, long[] sizes, Couplings cp, Scorer rich, RePairResult grammar,
        Dictionary<uint, int> dom, double[,] maxPhi, int[,] edgeCounts, List<Bridge> bridges, int intraEdges,
        int undirectedEdges, int[] populations, int sharedChunks)
    {
        D = d; Names = names; Sizes = sizes; Cp = cp; Rich = rich; Grammar = grammar; Dom = dom;
        MaxPhi = maxPhi; EdgeCounts = edgeCounts; Bridges = bridges; IntraEdges = intraEdges;
        UndirectedEdges = undirectedEdges; Populations = populations; SharedChunks = sharedChunks;
    }

    /// Build the domain graph from per-domain byte BLOCKS (each already caller-capped + newline-joined). The blocks
    /// are laid end to end (a separating newline caps each so no idiom straddles the junction), the union is induced
    /// once, couplings learned once, and each compressed chunk is attributed to the domain of its START byte (the
    /// tape is lossless — Σ expansion-lengths = union length — so the byte offset walks the provenance source-of-record).
    public static DomainGraph Build(IReadOnlyList<(string Name, byte[] Block)> domains, int window = Couplings.DefaultWindow)
    {
        int D = domains.Count;

        // ── the union + per-domain byte ranges (the provenance source-of-record) ──
        var buf = new List<byte>();
        var start = new long[D]; var end = new long[D]; var sizes = new long[D]; var names = new string[D];
        for (int d = 0; d < D; d++)
        {
            names[d] = domains[d].Name;
            start[d] = buf.Count;
            buf.AddRange(domains[d].Block);
            if (buf.Count > 0 && buf[^1] != (byte)'\n') buf.Add((byte)'\n');   // cap the junction so no idiom straddles it cleanly
            end[d] = buf.Count;
            sizes[d] = end[d] - start[d];
        }
        var (_, _, r) = Engine.Induce(buf.ToArray());
        var cp = Couplings.Learn(r, window);
        var rich = cp.BuildScorer(minCocount: 1);            // broad reach — the edges the walk / bridges read

        // ── provenance ──  attribute each compressed chunk to the domain its bytes recur in (a histogram over its
        // occurrences; dominant iff ≥ DomShare of them land in one domain, else Shared substrate).
        var hist = new Dictionary<uint, long[]>();
        long off = 0;
        foreach (var sym in r.Compressed)
        {
            int len = cp.Expand(sym.Value).Length;
            int d = DomainAt(start, end, D, off);
            if (d >= 0) { if (!hist.TryGetValue(sym.Value, out var h)) hist[sym.Value] = h = new long[D]; h[d]++; }
            off += len;
        }
        var dom = new Dictionary<uint, int>(hist.Count);
        foreach (var (u, h) in hist)
        {
            long tot = 0; int best = 0;
            for (int i = 0; i < D; i++) { tot += h[i]; if (h[i] > h[best]) best = i; }
            dom[u] = (tot > 0 && h[best] >= DomShare * tot) ? best : Shared;
        }

        // ── the undirected coupling edges ──  fold the directed rich graph to a<b edges keeping the max φ (one
        // physical edge per chunk pair, whichever direction carried the stronger co-activation).
        var (vocab, _) = cp.Vocabulary();
        var und = new Dictionary<(uint A, uint B), double>();
        foreach (var a in vocab)
            foreach (var (b, phi) in rich.Fwd(a))
            {
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                if (phi > und.GetValueOrDefault(key)) und[key] = phi;
            }

        // ── fold to the domain × domain bridge products ──  a cross-domain edge is a BRIDGE (strength = max φ,
        // tally an edge count, keep the edge); an intra-domain edge is counted; a shared-leg edge is neither.
        var maxPhi = new double[D, D];
        var edgeCounts = new int[D, D];
        var bridges = new List<Bridge>();
        int intra = 0;
        foreach (var ((a, b), phi) in und)
        {
            int da = dom.GetValueOrDefault(a, Shared), db = dom.GetValueOrDefault(b, Shared);
            if (da < 0 || db < 0) continue;                  // a leg is in the shared substrate — not a domain bridge
            if (da == db) { intra++; continue; }
            edgeCounts[da, db]++; edgeCounts[db, da]++;
            if (phi > maxPhi[da, db]) { maxPhi[da, db] = phi; maxPhi[db, da] = phi; }
            bridges.Add(new Bridge(phi, a, b, da, db));
        }
        bridges.Sort((x, y) => x.Phi != y.Phi ? y.Phi.CompareTo(x.Phi) : x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));

        // ── per-domain populations + shared substrate size ──
        var pop = new int[D]; int shared = 0;
        foreach (var u in vocab) { int d = dom.GetValueOrDefault(u, Shared); if (d >= 0) pop[d]++; else shared++; }

        return new DomainGraph(D, names, sizes, cp, rich, r, dom, maxPhi, edgeCounts, bridges, intra, und.Count, pop, shared);
    }

    /// The GREEDY BRIDGE-ORDER — the developmental curriculum's cross-domain sequence: start at domain 0, then
    /// repeatedly cross to the un-visited domain with the WIDEST bridge (most cross-domain coupling EDGES) to ANY
    /// already-visited domain (id tie-break; a disconnected domain is appended in id order). Edge COUNT, not max φ:
    /// the φ-peak saturates near-flat on real worlds (chunking shrapnel wins it) while the count spans 114× — the
    /// count is the anisotropy the schedule exists to follow. THIS IS "cross the widest coupling-bridge into the
    /// adjacent domain," precomputed as a schedule GrokBell follows and CritLock's kill-line (b) tests.
    public int[] GreedyOrder()
    {
        if (D == 0) return [];                           // an empty graph has an empty order — the old `[0]` lied a phantom domain into schedulers
        var order = new List<int> { 0 };
        var visited = new bool[D]; visited[0] = true;
        for (int step = 1; step < D; step++)
        {
            int pick = -1; int bestCnt = -1;
            for (int cand = 0; cand < D; cand++)
            {
                if (visited[cand]) continue;
                int linkto = 0; foreach (int v in order) linkto = Math.Max(linkto, EdgeCounts[v, cand]);
                if (linkto > bestCnt || (linkto == bestCnt && (pick < 0 || cand < pick))) { bestCnt = linkto; pick = cand; }
            }
            if (pick < 0) { for (int c = 0; c < D; c++) if (!visited[c]) { pick = c; break; } }
            visited[pick] = true; order.Add(pick);
        }
        return order.ToArray();
    }

    /// The bridge matrix the schedule ORDERS ON (domain×domain cross-edge counts) + the recovered chain, as a human
    /// report (CritLock's kill-line header). The domains are labelled `d{i}` (anonymous file indices); DomainWalk
    /// renders its own name-labelled matrix from EdgeCounts + Names. `order` is threaded through so the report shows
    /// the schedule it produced.
    public string RenderBridgeMatrix(int[] order)
    {
        var o = new StringBuilder();
        o.AppendLine("── the coupling bridge graph (domain×domain cross-edge COUNT — the ordering signal; max-φ saturates flat) ────");
        o.Append("        "); for (int j = 0; j < D; j++) o.Append($"{$"d{j}",7}"); o.AppendLine();
        for (int i = 0; i < D; i++)
        {
            o.Append($"    d{i}  ");
            for (int j = 0; j < D; j++) o.Append(i == j ? "      ·" : $"{EdgeCounts[i, j],7}");
            o.AppendLine();
        }
        o.AppendLine($"  greedy bridge-order: {string.Join(" → ", order)}   (start 0, cross widest edge-count bridge to visited set)");
        return o.ToString();
    }

    /// The domain a union byte-offset lands in — a linear scan of the (small, D-length) range table. The ONE copy
    /// of the lookup both verbs used to carry privately (CritLock.cs:566 / DomainWalk.cs:269).
    private static int DomainAt(long[] start, long[] end, int D, long off)
    {
        for (int i = 0; i < D; i++) if (off >= start[i] && off < end[i]) return i;
        return -1;
    }
}
