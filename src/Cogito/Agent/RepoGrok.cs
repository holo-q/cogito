namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  REPOGROK — the full-loop induction organ shared by all three nav modes (BENCH-PORT mechanism 1): the visited
//  material as an intake POOL drained in residual-frontier order (RLEI-root), the grammar stride-re-induced, BREACH-
//  deepened post-lock, the k-aware grok bell metering every draw. This replaces v0's flat Engine.Induce(tape) — the
//  same organs the trunk proved (grok-lock −26% budget · +21% depth), pointed at a repo instead of the farm corpus.
//
//  ONE ORGAN, THREE MODES: the seed is a ctor arg (frozen mode passes a fixed pretrain base; dyn/loop pass the
//  accreting StandingGrammar's snapshot). The value-layer reads (IssueParseDepth · MeanZ · DiscoverValue) are the
//  self-model proprioception navloop's value governance consumes; frozen/dyn never call them, so they cost nothing
//  there (MeanZ tracking is a field assignment off the RenormStats every mode already runs — no output effect). The
//  frozen telemetry getters (CvZ · KZ · RuleCount · TapeBytes) are read only by navigate's NavResult; inert elsewhere.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public sealed class RepoGrok : IDisposable
{
    // ── the induction knobs (BENCH-PORT mechanism 1 — identical across all three modes) ──
    private const int    IntakeBatch       = 48;      // spans per frontier draw (each draw = one bell round)
    private const int    InduceStrideBytes = 16_384;  // PUMP + harvest the loom every this many new tape bytes (the loom is O(Δ), so this is a read cadence, not a cost gate)
    private const int    MaxTapeBytes      = 131_072; // the grammar's DIET cap — the repo MODEL crystallizes on the visited files' vocabulary. The loom made induce O(Δ) so the cap no longer prices induce; it caps the residual/couplings/breach reads (each O(tape/grammar)) and the expansion vocabulary depth. 48KB starved outcomeCredit; 128KB restores it at O(Δ) induce.
    private const double GrokCvFloor       = 0.15;    // the SOC lock-line floor (trunk law, CortexRunConfig.GrokCv)
    private const double GrokBandSigmas    = 1.5;     // k-aware sampling-sd headroom (DomainMeter.CvStar)
    private const int    GrokLockRounds    = 3;       // CV-lock hysteresis depth (anti-chatter, trunk law)
    private const int    BreachQuota       = 64;      // count-2 template mints per post-lock breach night (+21% depth's organ)
    private const int    FrontierCapExps   = 400;     // the trunk's capped-cover law (Curriculum GrokBell)
    private const ulong  BreachSeed        = 0x0A71EEDUL;   // the noising seam is reserved/unread — any constant is the Vow
    // ── residual-driven expansion (BENCH-PORT mechanism 2) ──
    private const int    ExpandM     = 8;       // minted terms accepted per look (cap — PRF-drift bound)
    private const int    ExpandTopK  = 16;      // coupled units considered per anchor (Scorer top-K)
    private const int    MinCocount  = 2;       // the PPMI regularization at QUERY scale — deep identifier-rules recur ~2× on the bounded diet, so the ≥5-robust view starves them; 2 reaches the rare vocabulary the residual actually needs (drift is caught by the outcomeCredit gate, not this floor)
    private const int    MaxAnchors  = 16;      // gap-adjacent anchor units per expansion pass
    private const double DfCapFrac   = 0.05;    // a minted term must live in ≤ this fraction of files (discriminative)
    // ── the value layer (navloop's DiscoverValue reads — the self-model-gain appraisal; inert unless called) ──
    private const double MeanzBasin       = MeshHomeostat.Basin;    // −0.70: the honest criticality attractor
    private const double MeanzBandLo      = MeshHomeostat.BandLo;   // −0.95: below this MeanZ is sliding toward the −1.11 sink
    private const int    ValueMinExpBytes = 8;      // a sub-goal anchor must span ≥ this to be a real structural gap (Pearl.ReflectFloorBytes parity)
    private const double ValueMeanzWeight = 0.5;    // the criticality term's weight in the appraisal (value = depth-gain − ValueMeanzWeight·criticality-damage)

    public void Dispose() => _loom.Dispose();   // return the loom's ArrayPool rentals (the 300-sweep must not leak per-instance)

    /// SEED the loom's rank-encoder with the trained base so repo spans encode THROUGH the trained vocabulary. Frozen
    /// mode passes a fixed pretrain base (head-of-Exodia swing); dyn/loop pass the accreting StandingGrammar snapshot;
    /// null / empty = the COLD ablation (empty grammar, repo-only induce). The base `_g` / its RenormStats are
    /// deliberately NOT computed here — Drive always calls Drain FIRST, and the first Harvest re-derives `_g` (base ⊕
    /// the first repo delta) + its RenormStats over the current tape, overwriting anything set here. The reads that
    /// need the base-only grammar before any splice don't exist.
    public RepoGrok(RePairResult? seed)
    {
        if (seed is { } b && b.Rules.Length > 0) _loom.Seed(b);
    }

    private readonly List<byte[]> _pool = new();          // line spans of every visited file (the intake pool)
    private bool[] _ingested = [];                        // parallel drain mask
    private FrontierIndex? _frontier;                     // pool postings — rebuilt when the pool GROWS (a new file descended)
    private bool _poolGrew;
    private readonly List<byte> _tape = new();            // the accreted diet, frontier-ordered, '\n' joints (breach's byte-exact reference)
    private readonly Loom _loom = new();                  // the O(Δ) incremental induction organ (SPLICE new spans → PUMP → harvest; rule-count-independent, ~2175× cheaper than batch re-induce)
    private long _splicedSpans;                            // spanId cursor (each drained line splices once, monotone)
    private long _lastPumpBytes = -InduceStrideBytes;     // stride gate (first drain always pumps + harvests)
    private RePairResult _g;
    private Engine.GrammarCover? _coverFull;              // issue-residual fidelity (full basis)
    private Engine.GrammarCover? _coverFrontier;          // intake scoring (capped basis — the trunk's FrontierCapExps discipline)
    private Couplings? _couplings; private Scorer? _scorer;   // built lazily per grammar identity, post-lock
    private GrammarRule[]? _coupledRules;
    private List<(byte[] E, uint Id)>? _gapExps;              // GapAnchors' sorted expansion basis — per grammar identity (the per-look re-expand + re-sort of every rule was the chug); the per-issue cover stays live
    private GrammarRule[]? _gapExpRules;
    private readonly DomainMeter _bell = new(GrokCvFloor, GrokLockRounds, GrokBandSigmas);
    private double _cachedCv = double.NaN; private int _cachedK; private int _round;
    private GrammarRule[]? _breachedRules;                 // identity guard — breach the grammar at most once

    public bool Locked => _bell.Locked;
    public int LockLook { get; private set; } = -1;
    public double CvZ => _cachedCv;
    public int KZ => _cachedK;
    public double MaxSpan { get; private set; }
    /// The honest criticality axis (MeshHomeostat's RG attractor). NaN until the grammar has ≥2 scales with ≥4 rules.
    /// The value-layer DOCKS a sub-goal whose acquisition would pull this below basin (the anti-Goodhart guard).
    public double MeanZ { get; private set; } = double.NaN;
    public int RuleCount => _g.Rules?.Length ?? 0;
    public long TapeBytes => _tape.Count;

    /// Split a descended file into line spans and add them to the intake pool. Past the diet cap the pool stops growing
    /// — the grammar's budget is bounded (the homeostat's discipline), attention is not.
    public void AddFile(string text)
    {
        if (_tape.Count >= MaxTapeBytes) return;
        foreach (var mem in Engine.SplitLines(Encoding.UTF8.GetBytes(text)))
            if (mem.Length > 0) { _pool.Add(mem.ToArray()); _poolGrew = true; }
    }

    /// One look's intake: drain the pool by residual frontier (batch draws, each a bell round), SPLICING each drained
    /// span into the persistent Loom and PUMPING stride-gated (O(Δ) — no from-scratch re-induce); once LOCKED the
    /// harvested grammar is BREACH-deepened (count-2 templates past the pay-floor — the +21% depth organ, lossless).
    /// The bell's streak accrues on the cached CV between strides — a grokked grammar stays grokked while intake lands.
    public void Drain(int look)
    {
        if (_poolGrew)
        {
            var mask = new bool[_pool.Count];
            Array.Copy(_ingested, mask, _ingested.Length);
            _ingested = mask;
            _frontier = new FrontierIndex(_pool);                    // postings over the grown pool (O(pool bytes), pool ≤ diet cap ⇒ cheap)
            _poolGrew = false;
        }
        while (_tape.Count < MaxTapeBytes)
        {
            List<int> picks;
            if (_g.Rules is { Length: > 0 })
            {
                _coverFrontier ??= new Engine.GrammarCover(_g.Rules, FrontierCapExps);
                picks = Radula.FrontierPick(_coverFrontier, _pool, _ingested, IntakeBatch, _frontier!);
            }
            else                                                     // no grammar yet — the bootstrap anchor is index-order
                picks = Enumerable.Range(0, _pool.Count).Where(i => !_ingested[i]).Take(IntakeBatch).ToList();
            if (picks.Count == 0) break;
            foreach (var i in picks)
            {
                _ingested[i] = true;
                _tape.AddRange(_pool[i]); _tape.Add((byte)'\n');
                _loom.SpliceEvent(_pool[i], _splicedSpans++, weight: 1);   // O(Δ log Δ) — encode the span through the standing rules, tally its digrams
            }
            if (_tape.Count - _lastPumpBytes >= InduceStrideBytes) Harvest();
            Observe(look);                                           // one bell round per draw (cached CV between strides)
        }
        // a final Harvest if the last drain batch didn't cross the stride, so `_g` reflects the whole tape (and the
        // tiny-pool case that never crossed the stride at all gets its grammar).
        if (_tape.Count != _lastPumpBytes || _g.Rules is null) Harvest();
        // BREACH is CONSOLIDATION (sleep, not per-stride): deepen the LOCKED grammar ONCE per look, on the FINAL
        // harvested grammar — the count-2 template stratum the residual + couplings read (the +21% depth organ,
        // lossless). It runs against the loom's OWN tape (Breach.Guard is byte-exact; the loom's Result() expands to
        // exactly `_tape`). The breached grammar is a terminal SNAPSHOT the reads consume; the loom stays canonical.
        if (_bell.Locked && _g.Rules is { Length: > 0 } && !ReferenceEquals(_g.Rules, _breachedRules))
        {
            _g = AnnealEvict.Breach(in _g, _tape.ToArray(), BreachQuota, BreachSeed).Grammar;
            _breachedRules = _g.Rules;
            var rn = Engine.RenormStats(_g); _cachedCv = rn.CvZ; _cachedK = rn.KZ; MaxSpan = rn.MaxSpan; MeanZ = rn.MeanZ;
            _coverFull = null; _coverFrontier = null;
        }
    }

    // PUMP the loom's winner-loop to fixpoint, then HARVEST the grammar — O(Δ), rule-count-independent. Replaces the
    // from-scratch Engine.Induce(tape) that made drain O(strides·tape) (the traced chug: 16 re-inductions).
    private void Harvest()
    {
        _loom.Pump();
        _g = _loom.Result();                                          // splice order = drain order (the batch harvest — Result() not Result(tape))
        var rn = Engine.RenormStats(_g);
        _cachedCv = rn.CvZ; _cachedK = rn.KZ; MaxSpan = rn.MaxSpan; MeanZ = rn.MeanZ;
        _lastPumpBytes = _tape.Count;
        _coverFull = null; _coverFrontier = null;                    // identity caches — rebuilt lazily off the fresh grammar
    }

    private void Observe(int look)
    {
        bool was = _bell.Locked;
        _bell.Observe(_cachedCv, _cachedK, _round++, _tape.Count);
        if (!was && _bell.Locked) LockLook = look;
    }

    /// Coverage of the issue by the current grammar — the KNOW read (residual = 1 − this).
    public double IssueCoverage(byte[] issueBytes)
    {
        if (_g.Rules is not { Length: > 0 }) return 0;
        _coverFull ??= new Engine.GrammarCover(_g.Rules);
        return _coverFull.Coverage(issueBytes);
    }

    /// The MDL-native DEPTH read (navloop value layer): the issue's ParsedSize (symbols it compresses to) NORMALIZED
    /// by its byte length. LOWER = the mind has DEEPER structure for the issue (phrase/template rules, not just bytes).
    /// Coverage saturates at ~100% trivially, but ParsedSize keeps dropping as the mind learns the issue's real
    /// structure. A move's VALUE = how much it drops THIS. ∈ (0,1]; 1 = pure bytes (no structure), →0 = deeply chunked.
    public double IssueParseDepth(byte[] issueBytes)
    {
        if (_g.Rules is not { Length: > 0 } || issueBytes.Length == 0) return 1.0;
        _coverFull ??= new Engine.GrammarCover(_g.Rules);
        return (double)_coverFull.ParsedSize(issueBytes) / issueBytes.Length;
    }

    /// Emit only query-shaped grep proposals from the grammar already induced over
    /// observed tool replies.  This is deliberately path-blind: repository paths are
    /// selected by RepositoryWorldSnapshot after the proposal executes, never by this
    /// induction organ.  Before the first observation the user query is the sole seed.
    public List<string> SuggestGrepTerms(byte[] queryBytes, IReadOnlyCollection<string> already, int cap = 4)
    {
        var terms = new List<string>(cap);
        var seen = new HashSet<string>(already, StringComparer.Ordinal);
        void Add(string value)
        {
            if (terms.Count >= cap) return;
            string token = value.Trim().ToLowerInvariant();
            if (token.Length < Loc.MinTermLen || token.Contains(' ') || !seen.Add(token)) return;
            terms.Add(token);
        }

        if (_g.Rules is { Length: > 0 })
        {
            int limit = Math.Min(_g.Rules.Length, 32);
            for (int i = 0; i < limit && terms.Count < cap; i++)
            {
                byte[] expansion = Reconstruct.Expand(_g.Rules, [new Symbol((uint)(256 + i))]);
                foreach (string token in Loc.Toks(Encoding.UTF8.GetString(expansion))) Add(token);
            }
        }
        if (terms.Count < cap)
            foreach (string token in Loc.Toks(Encoding.UTF8.GetString(queryBytes))) Add(token);
        return terms;
    }

    // ── the harvested BINARY rules the standing grammar absorbs (dyn/loop channel 1) — the loom's current grammar
    // restricted to its pure-binary prefix (the seed-able core; the breach/consolidation suffix isn't re-seedable,
    // re-derives on re-induce). This is channel 1's contribution: the repo idioms this instance grokked. ──
    public GrammarRule[] HarvestBinary()
    {
        if (_g.Rules is not { Length: > 0 }) return [];
        var bin = new List<GrammarRule>();
        foreach (var r in _g.Rules) { if (r.Kind == RuleBodyKind.Expansion && r.Pattern.Length == 2) bin.Add(r); else break; }
        return bin.ToArray();
    }

    /// EXPANSION MINT — the residual aimed at the couplings graph. Anchor units = the grammar rules covering issue
    /// bytes ADJACENT to uncovered gaps (the edge of the known, gap-sized-first); each anchor's PPMI neighbours (robust
    /// regularization — transfer, not threading) expand to bytes, tokenized under the BM25 law; a term survives only if
    /// it is NEW to the probe and RARE across the repo's files (df-bounded — the anti-PRF-drift filter). Returns
    /// ≤ ExpandM terms (Σφ desc, term asc).
    public List<string> MintTerms(byte[] issueBytes, HashSet<string> issueToks, List<string> already, Bm25Index bm25, int fileCount)
    {
        if (_g.Rules is not { Length: > 0 }) return [];
        EnsureCouplings();
        var anchors = GapAnchors(issueBytes);
        if (anchors.Count == 0) return [];
        var alreadySet = new HashSet<string>(already, StringComparer.Ordinal);
        int dfCap = Math.Max(2, (int)(DfCapFrac * fileCount));
        var phi = new Dictionary<string, double>(StringComparer.Ordinal);
        // per anchor, its PPMI coupling NEIGHBOURS (the anchor covers issue bytes, so its own expansion is issue
        // vocabulary — filtered below; the VALUE is the transfer to the ADJACENT units the grammar learned sit beside
        // the gap). Each neighbour's expansion is tokenized; a df-rare NEW token is a discriminative bridge.
        foreach (var a in anchors)
            foreach (var (u, f) in _scorer!.Fwd(a).Concat(_scorer.Bwd(a)))
            {
                var exp = Reconstruct.Expand(_g.Rules, [new Symbol(u)]);
                foreach (var t in Loc.Toks(Encoding.UTF8.GetString(exp)))
                {
                    if (t.Length < Loc.MinTermLen || t.Contains(' ')) continue;          // unigrams only — a minted 2-gram is two independent claims
                    if (issueToks.Contains(t) || alreadySet.Contains(t)) continue;       // new vocabulary only (the issue already scores its own terms)
                    int df = bm25.FileDf(t);
                    if (df < 1 || df > dfCap) continue;                                  // must EXIST and DISCRIMINATE
                    phi[t] = phi.GetValueOrDefault(t) + f;
                }
            }
        return phi.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                  .Take(ExpandM).Select(kv => kv.Key).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  STAGE 1 DECOMPOSE + STAGE 2 VALUE-DISCOVER (navloop's value layer) — the issue's gap-anchors (grammar rules
    //  flanking its UNCOVERED regions) ARE the sub-goals ("understand this gap"); their count is the mind's own
    //  variable decomposition. Each sub-goal is appraised by its expected SELF-MODEL GAIN, read straight off the mind's
    //  proprioception (anchor depth × criticality-health) — NOT the metric. The acquisition-strategy is the anchor's
    //  PPMI-coupled terms tagged with the sub-goal's value — the descend-governing field. Returns (term, value) pairs.
    //
    //  ANTI-GOODHART (in-architecture): the value reads ParseDepth + MeanZ, never a rank-list localization target. A sub-goal whose acquisition
    //  would add generic fragments raises coverage but does not deepen the parse and destabilizes MeanZ ⇒ low value ⇒
    //  the mind does not pursue it. The mind cannot chase the metric because the metric is not in the value signal.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    public List<(string Term, double Value)> DiscoverValue(byte[] issueBytes, HashSet<string> issueToks, List<string> already, Bm25Index bm25, int fileCount)
    {
        if (_g.Rules is not { Length: > 0 }) return [];
        EnsureCouplings();
        var expLen = Engine.ExpLens(_g.Rules, _g.AlphabetSize);
        var anchors = GapAnchors(issueBytes);   // the variable sub-goal decomposition — gap-flanking anchors, largest-gap-first
        if (anchors.Count == 0) return [];

        // THE CRITICALITY GATE (the self-model's coherence read) — a single multiplier over ALL appraisals this look.
        // MeanZ at/above basin ⇒ 1 (coherent, trust the value-discovery). MeanZ sunk toward the sink ⇒ →0 (the model is
        // degrading, its appraisals are self-echo — dock the value). NaN (too-shallow grammar) ⇒ 1 (the boot regime).
        double health = double.IsNaN(MeanZ) ? 1.0
                      : MeanZ >= MeanzBasin ? 1.0
                      : Math.Clamp(1.0 - ValueMeanzWeight * (MeanzBasin - MeanZ) / Math.Max(1e-9, MeanzBasin - MeanzBandLo), 0.0, 1.0);

        var alreadySet = new HashSet<string>(already, StringComparer.Ordinal);
        int dfCap = Math.Max(2, (int)(DfCapFrac * fileCount));
        // the DEPTH SCALE — normalize anchor ExpLen to [0,1] over this look's anchors, so the deepest gap-anchor (the
        // most-understood boundary, the highest-value sub-goal) reads 1. Value is DENSITY, not raw bytes.
        double maxExp = 0;
        foreach (var a in anchors) { int r = (int)(a - Symbol.FirstNonterminal); if (r >= 0 && r < expLen.Length && expLen[r] > maxExp) maxExp = expLen[r]; }
        if (maxExp < ValueMinExpBytes) return [];

        var val = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in anchors)
        {
            int ar = (int)(a - Symbol.FirstNonterminal);
            if (ar < 0 || ar >= expLen.Length || expLen[ar] < ValueMinExpBytes) continue;
            double subGoalValue = health * (expLen[ar] / maxExp);   // the sub-goal's self-model value: depth-of-comprehension × coherence-health
            // ACQUISITION-STRATEGY: the anchor's coupled neighbours (the structure adjacent to the gap) → terms, each
            // carrying the sub-goal's value. A discriminative (df-rare, new) term IS the handle to acquire it.
            foreach (var (u, f) in _scorer!.Fwd(a).Concat(_scorer.Bwd(a)))
            {
                var exp = Reconstruct.Expand(_g.Rules, [new Symbol(u)]);
                foreach (var t in Loc.Toks(Encoding.UTF8.GetString(exp)))
                {
                    if (t.Length < Loc.MinTermLen || t.Contains(' ')) continue;
                    if (issueToks.Contains(t) || alreadySet.Contains(t)) continue;
                    int df = bm25.FileDf(t);
                    if (df < 1 || df > dfCap) continue;
                    // the term's value = the sub-goal's value × its coupling strength f (how tightly the grammar binds
                    // this handle to the gap). Accumulate — a term serving multiple sub-goals is worth more.
                    val[t] = val.GetValueOrDefault(t) + subGoalValue * f;
                }
            }
        }
        return val.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                  .Take(ExpandM).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    // build the couplings + scorer lazily per grammar identity (post-lock). minUnitFreq 1 — a deep identifier-rule
    // used ONCE near the gap is still a grounded handle (the outcomeCredit gate, not frequency, guards drift).
    private void EnsureCouplings()
    {
        if (ReferenceEquals(_g.Rules, _coupledRules)) return;
        _couplings = Couplings.Learn(_g);
        _scorer = _couplings.BuildScorer(MinCocount, ExpandTopK, minUnitFreq: 1);
        _coupledRules = _g.Rules;
    }

    /// The gap-adjacent anchor units: greedy longest-first cover of the issue WITH rule identities (the same (len desc,
    /// bytes asc) + non-overlap law as GrammarCover — which discards ids, so this small twin keeps them), then for each
    /// maximal uncovered run (largest first) the covering units immediately flanking it. These are the machine's
    /// grounded handles NEAREST its known-unknowns — what expansion hunts material for.
    private List<uint> GapAnchors(byte[] issue)
    {
        if (!ReferenceEquals(_g.Rules, _gapExpRules))                 // identity-gated like EnsureCouplings — the basis only moves when the grammar does
        {
            var exps = new List<(byte[] E, uint Id)>(_g.Rules.Length);
            for (int i = 0; i < _g.Rules.Length; i++)
            {
                var e = Reconstruct.Expand(_g.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
                if (e.Length >= 2) exps.Add((e, Symbol.FirstNonterminal + (uint)i));
            }
            exps.Sort((a, b) =>
            {
                if (a.E.Length != b.E.Length) return b.E.Length - a.E.Length;
                for (int i = 0; i < a.E.Length; i++) if (a.E[i] != b.E[i]) return a.E[i] - b.E[i];
                return 0;
            });
            _gapExps = exps;
            _gapExpRules = _g.Rules;
        }
        var unit = new uint[issue.Length];                            // 0 = uncovered (unit ids start at 256)
        foreach (var (e, id) in _gapExps!)
        {
            int i = 0;
            while (i + e.Length <= issue.Length)
            {
                int rel = issue.AsSpan(i).IndexOf(e);
                if (rel < 0) break;
                int at = i + rel;
                bool free = true;
                for (int j = 0; j < e.Length && free; j++) free = unit[at + j] == 0;
                if (free) { for (int j = 0; j < e.Length; j++) unit[at + j] = id; i = at + e.Length; }
                else i = at + 1;
            }
        }
        var gaps = new List<(int Start, int Len)>();
        for (int i = 0; i < unit.Length;)
        {
            if (unit[i] != 0) { i++; continue; }
            int s = i; while (i < unit.Length && unit[i] == 0) i++;
            gaps.Add((s, i - s));
        }
        gaps.Sort((a, b) => a.Len != b.Len ? b.Len - a.Len : a.Start - b.Start);   // largest unknown first
        var anchors = new List<uint>(); var seen = new HashSet<uint>();
        foreach (var (s, len) in gaps)
        {
            if (anchors.Count >= MaxAnchors) break;
            if (s > 0 && unit[s - 1] != 0 && seen.Add(unit[s - 1])) anchors.Add(unit[s - 1]);
            int e = s + len;
            if (e < unit.Length && unit[e] != 0 && seen.Add(unit[e])) anchors.Add(unit[e]);
        }
        return anchors;
    }
}
