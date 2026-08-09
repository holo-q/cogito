namespace Cogito;

using System.Runtime.InteropServices;
using System.Text;
using Cogito.Induct;
using Cogito.Grammar;

// ── NODE-BIRTH ──  THE DEPTH organ (cogito-nodebirth-probe): coupling-guided affinity-chain composition that
// INVENTS coherent deep units past the pairwise W≤3 ceiling. Builds ON Couplings (the MEANING organ). The
// pairwise Gibbs walk holds a thread over its W=3 window and no farther; Re-Pair births a unit only where an
// EXACT pair recurs. Node-birth is the EXPLICIT pillar-4: compose units where the LEARNED couplings predict high
// affinity EVEN IF the whole unit never exactly recurs — a deep unit whose internal thread is bounded by
// COMPOSITION, not corpus-recurrence (chains reach past W≤3 where Re-Pair caps ~4). Proven in Python: raising the
// affinity FLOOR 0→4 MONOTONICALLY deepens+cleans the invented units (the LEARNED affinity IS the depth lever).
//
// THE PIPELINE (per generation): learn couplings → forge greedy φ+idf affinity chains (novelty-gated = not a
// corpus subsequence) → mint them into the grammar (MintChain) → the SAME coupling driver walks the enlarged
// vocab (boundary-inherited couplings + an MDL depth-bonus so it reaches for the deep units). Node-birth EXTENDS
// the coupling generator; it does not replace it.

/// The affinity a→b that guides chain growth — THE SEAM (the coordinator's forward-compat). Default = the proven
/// learned-coupling + associative id-thread; a role-analogy variant (the running frequency-vs-role probe)
/// implements this same interface and drops in, no other change. Frequency-only affinity is NOT hardcoded.
public interface IAffinity
{
    double Affinity(uint a, uint b);
}

/// The proven default (cogito-nodebirth-probe): aff(a→b) = w_phi·φ_combined(a,b,1) + w_id·Σ_{shared characteristic
/// id} idf(id). The LEARNED coupling (co-activate above chance) PLUS an associative id-thread (share a distinctive,
/// idf-weighted identifier — idf DOWN-weights the ubiquitous `i`/`result` fake-thread).
public sealed class CouplingAffinity(CombinedScore phi,
    Dictionary<uint, HashSet<string>> prof, Dictionary<string, double> idf, double wPhi = 1.0, double wId = 1.5) : IAffinity
{
    public double Affinity(uint a, uint b)
    {
        double id = 0;
        if (prof.TryGetValue(a, out var pa) && prof.TryGetValue(b, out var pb))
            foreach (var x in pa) if (pb.Contains(x)) id += idf.GetValueOrDefault(x, 0.0);
        return wPhi * phi.Phi(a, b, 1) + wId * id;
    }
}

/// The FORGE — greedy affinity chains, the node-birth mechanism. Each seed grows a DISTINCT chain by transitive
/// affinity to max_depth (STOP below the affinity floor — the depth lever); leaves distinct by construction (a
/// repeat would break its own thread); novelty-gated (minted only if the leaf-tuple is NOT a contiguous corpus
/// subsequence — an INVENTION, not a replay). Minted chains enter the grammar as atomic deep units.
public sealed class Forge(Couplings cp, Scorer rich, IAffinity aff, HashSet<ulong> corpusSubseqs,
    uint baseBoundary, int maxDepth, double affFloor)
{
    /// Grow + mint a novel chain from each seed (len ≥3 to be a genuine deep unit). Mints into `cp`; returns the ids.
    public List<uint> Run(uint[] seeds)
    {
        var minted = new List<uint>();
        foreach (var s in seeds)
        {
            var chain = GrowChain(s);
            if (chain.Count < 3) continue;                                   // too shallow to be a deep unit
            if (corpusSubseqs.Contains(HashChain(chain))) continue;          // the whole chain recurs verbatim ⇒ REPLAY, skip
            minted.Add(cp.MintChain(CollectionsMarshal.AsSpan(chain)));
        }
        return minted;
    }

    private List<uint> GrowChain(uint seed)
    {
        var chain = new List<uint> { seed };
        var used = new HashSet<uint> { seed };
        while (chain.Count < maxDepth)
        {
            uint last = chain[^1];
            double bestAff = double.NegativeInfinity; uint best = 0; bool found = false;
            foreach (var (b, _) in rich.Fwd(last))                           // candidates = φ-forward neighbours (co-activate above chance)
            {
                if (b >= baseBoundary || used.Contains(b)) continue;         // base units only, distinct by construction
                double a = aff.Affinity(last, b);
                if (!found || a > bestAff || (a == bestAff && b < best)) { bestAff = a; best = b; found = true; }   // id tie-break → deterministic
            }
            if (!found || bestAff < affFloor) break;                         // below the affinity floor — stop (the depth lever)
            chain.Add(best); used.Add(best);
        }
        return chain;
    }

    // FNV-1a fold — CorpusSubseqs and HashChain MUST fold identically so a chain hashes to a corpus window iff it IS one.
    internal static ulong Fold(ulong h, uint x) { h ^= x; h *= 1099511628211UL; h ^= 0x9E3779B97F4A7C15UL; return h; }
    private static ulong HashChain(List<uint> chain) { ulong h = 1469598103934665603UL; foreach (var x in chain) h = Fold(h, x); return h; }
}

/// The `--gen nodebirth` strategy — the DEPTH organ behind the IGenerator seam. Learns couplings, builds the
/// associative id-profiles, forges novelty-gated affinity chains at the given floor, mints them, and drives the
/// composed-aware coupling generator over the enlarged vocab.
public sealed class NodeBirthWalk(double affFloor = 1.0, int maxDepth = 8, int nSeeds = 400, double depthBonus = 0.6) : IGenerator
{
    private GrammarRule[]? _rules;
    private Symbol[]? _compressed;
    private CouplingGenerator? _generator;
    public string Name => "nodebirth";

    public byte[] Generate(RePairResult grammar, int count, ulong seed, Metabolism _)
    {
        if (grammar.Compressed is null || grammar.Compressed.Length < 4) return [];
        // Loom.Result mints a fresh Compressed array every mutation revision even when the emitted
        // sequence is unchanged, so reference identity alone never hits; a content-equal image keeps
        // the standing model (an O(n) memcmp against an O(n·model) rebuild).
        if (ReferenceEquals(_rules, grammar.Rules)
            && (ReferenceEquals(_compressed, grammar.Compressed)
                || (_compressed is not null && grammar.Compressed.AsSpan().SequenceEqual(_compressed))))
        {
            _compressed = grammar.Compressed;
        }
        else
        {
            var cp = Couplings.Learn(grammar);
            var rich = cp.BuildScorer(minCocount: 1);
            var robust = cp.BuildScorer(minCocount: 5);
            uint baseBoundary = Symbol.FirstNonterminal + (uint)grammar.Rules.Length;   // ids ≥ this are minted composites

            var (vocab, _) = cp.Vocabulary();
            var (prof, idf) = IdProfiles(cp, vocab);
            var aff = new CouplingAffinity(new CombinedScore(rich, robust), prof, idf);
            var subseqs = CorpusSubseqs(grammar.Compressed, maxDepth);
            var seeds = SeedsByFrequency(cp, baseBoundary, nSeeds);

            new Forge(cp, rich, aff, subseqs, baseBoundary, maxDepth, affFloor).Run(seeds);   // mints composed units into cp
            // line-aware so the depth organ's deep composed units mint into line-bounded spans (post-barrier '\n' lives
            // only between units — without this the deepest walks emit the longest newline-free runs of all).
            _generator = new CouplingGenerator(cp, rich, robust, depthBonus, lines: new LineModel(grammar));
            _rules = grammar.Rules;
            _compressed = grammar.Compressed;
        }
        return _generator!.Generate(count, seed);
    }

    // per-unit characteristic content-ids (its expansion's identifiers) + global idf. A single expansion, so the
    // Python's ≥min_mass filter is trivially "present"; idf(id) = log(V / (1+df)) down-weights the ubiquitous.
    internal static (Dictionary<uint, HashSet<string>> Prof, Dictionary<string, double> Idf) IdProfiles(Couplings cp, uint[] vocab)
    {
        var prof = new Dictionary<uint, HashSet<string>>(vocab.Length);
        var df = new Dictionary<string, int>();
        foreach (var u in vocab)
        {
            var ids = ContentIds(cp.Expand(u));
            prof[u] = ids;
            foreach (var id in ids) df[id] = df.GetValueOrDefault(id) + 1;
        }
        int nt = Math.Max(1, vocab.Length);
        var idf = new Dictionary<string, double>(df.Count);
        foreach (var (id, d) in df) idf[id] = Math.Log((double)nt / (1 + d));
        return (prof, idf);
    }

    // maximal identifier runs (≥3 chars, not a common keyword) in an expansion — the co-referable content tokens.
    private static HashSet<string> ContentIds(byte[] e)
    {
        var ids = new HashSet<string>();
        int i = 0, n = e.Length;
        while (i < n)
        {
            if (IsIdentStart(e[i]))
            {
                int j = i + 1; while (j < n && IsIdentPart(e[j])) j++;
                if (j - i >= 3) { var s = Encoding.ASCII.GetString(e, i, j - i); if (!Noise.Contains(s)) ids.Add(s); }
                i = j;
            }
            else i++;
        }
        return ids;
    }

    private static bool IsIdentStart(byte b) => (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || b == '_';
    private static bool IsIdentPart(byte b) => IsIdentStart(b) || (b >= '0' && b <= '9');

    // the worst fake-thread offenders (idf handles the rest); keeps the id-thread on DISTINCTIVE names.
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    { "the", "and", "for", "var", "new", "get", "set", "out", "ref", "int", "this", "null", "true",
      "string", "void", "return", "public", "private", "static", "class", "using", "value", "result", "item", "index" };

    // contiguous compressed-window hashes (len 2..maxlen) — the REPLAY set the novelty gate tests against.
    internal static HashSet<ulong> CorpusSubseqs(Symbol[] comp, int maxlen)
    {
        var set = new HashSet<ulong>();
        for (int i = 0; i < comp.Length; i++)
        {
            ulong h = 1469598103934665603UL;
            for (int L = 1; L <= maxlen && i + L <= comp.Length; L++)
            {
                h = Forge.Fold(h, comp[i + L - 1].Value);
                if (L >= 2) set.Add(h);
            }
        }
        return set;
    }

    // seeds = the most-frequent base units (the live idioms worth composing from), id-sorted tie-break.
    // internal: EnergyPolicy.Compose reuses this exact seed policy so the field's forge == NodeBirthWalk's.
    internal static uint[] SeedsByFrequency(Couplings cp, uint baseBoundary, int n)
    {
        var items = new List<(uint u, int c)>();
        foreach (var (u, c) in cp.Marginals) if (u < baseBoundary) items.Add((u, c));
        items.Sort((x, y) => x.c != y.c ? y.c.CompareTo(x.c) : x.u.CompareTo(y.u));
        var o = new uint[Math.Min(n, items.Count)];
        for (int i = 0; i < o.Length; i++) o[i] = items[i].u;
        return o;
    }
}
