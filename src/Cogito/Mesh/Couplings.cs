namespace Cogito;

using System.Runtime.InteropServices;
using Cogito.Grammar;
using Cogito.Induct;


// ── COUPLINGS ──  THE MEANING organ (cogito-driver-spec), ported from the Python-proven driver
// (scratchpad/driver/{driver,coherence_fork}.py) to cogito's FINE-GRAIN chunk substrate.
//
// The Re-Pair Markov walk has NO MODEL OF VALIDITY — it rides verbatim chunk-idioms, so it can BORROW
// coherence or emit salad, never novel-AND-coherent. The fix: generate over LEARNED COUPLINGS — a PPMI
// co-activation graph modelling WHICH chunks cohere (not a verbatim table) — by an ENERGY LANDSCAPE (a
// global MRF Gibbs relaxation), so each position is the chunk that best coheres with its whole neighbourhood.
//
// THE PROVEN FUSION (driver.py), three ingredients each control-proven:
//   MEANING = combined score  0.5·φ_rich + 1·φ_robust  — rich (min_cocount 1) PROPOSES threading candidates,
//             robust (min_cocount ≥5) makes the judgment TRANSFER; summed, one energy holds thread AND transfer.
//   LIFE    = flat WARM temperature — pin T warm, NEVER cool (cooling re-creates the collapse basin); the
//             brace/validity term keeps warm-T off salad.
//   FORM    = cool-commit TAIL — warm BODY untouched (meaning intact), then a few COOL sweeps on the
//             brace-breaking positions + a deterministic brace-drain → coherent code-braces.
//
// UNIT = a CHUNK = a Symbol.Value in the compressed sequence (terminal or Re-Pair nonterminal). This IS the
// fine grain the spec marks load-bearing: robust couplings VANISH at coarse/block grain (too-unique to
// co-occur ≥5×) and the unifier dissolves — couplings must be over the fine chunk stream.
//
// NODE-BIRTH SEAM (the pending DEPTH mechanism, a parallel Python probe): the pairwise-coupling generator
// walks EXISTING units and structurally cannot exceed the W≤3 range. Node-birth EXTENDS it — mint a NEW unit
// by composing two high-φ-affinity units (beyond exact-recurrence), then walk the enlarged vocabulary. So the
// unit vocabulary here is MUTABLE (`MintComposed`) and the generator never assumes "existing rules only" — it
// walks whatever units the model holds. Node-birth adds units; it does not rewrite the walk.

/// The learned co-activation graph over chunks + the combined-score MRF-Gibbs generator. Built from one
/// RePairResult's compressed sequence; the rich/robust regularizations share ONE co-count pass.
public sealed class Couplings
{
    public const int DefaultWindow = 3;              // W — the co-activation range (fine, pairwise; node-birth breaks it)

    private GrammarRule[] _rules;
    private readonly List<Symbol> _symbols = new();
    private readonly int _w;
    private readonly Dictionary<uint, int> _marg = new();                       // unit → marginal count
    private readonly Dictionary<long, int>[] _co;                              // per-distance co-counts: key=(a<<32|b)
    private readonly long[] _coTotals;
    private readonly Dictionary<uint, List<(uint Other, int Distance)>> _coForward = new();
    private readonly Dictionary<uint, List<(uint Other, int Distance)>> _coReverse = new();
    private readonly HashSet<uint> _dirtyUnits = new();                        // units whose marginal/co-count view changed since the last scorer refresh
    private long _n;                                                            // Σ marginals (the PPMI denominator base)
    private readonly Dictionary<uint, byte[]> _expansion = new();             // unit → its expanded bytes (LAZY — only units actually placed/probed materialize; the aggregate reads below never touch bytes)
    private readonly Dictionary<uint, uint[]> _composed = new();              // node-birth: composed id → its base leaf ids
    private uint _nextMinted;                                                   // node-birth: next composed-unit id
    private uint _composedFloor;                                                // ids below this are base (terminal/nonterminal) — composed ids mint at/above it, so `u < floor` skips the _composed lookup on the hot path

    // ── THE DERIVED UNIT TABLE ──  every aggregate the energy reads off a unit's expansion — brace delta,
    // byte length, head byte, last solid byte — folded BOTTOM-UP over the rules in O(rules), replacing the old
    // expand-EVERY-unit-to-bytes path (O(total grammar bytes) materialized per stride, the measured GENERATE
    // floor's rebuild half). Unit ids are DENSE (terminals 0..255 · rules 256.. · minted after), so these are flat
    // arrays — the per-candidate energy reads (Delta/HeadClean/TailClean) become single array loads instead of
    // grammar-sized dict probes. Folds mirror Reconstruct's tape-less ExpandOne EXACTLY (slot → Pattern[0] only;
    // demoted rules keep expanding their retained Pattern), so every value equals the old walk over Expand(u).
    private int[]  _uDelta = [];       // net '{'−'}' of the expansion
    private long[] _uLen = [];         // expansion byte length
    private byte[] _uHead = [];        // first expansion byte (HeadClean's read)
    private byte[] _uSolidTail = [];   // last byte ∉ {'\n',' ','\t'} (TailClean's read; valid iff _uHasSolid)
    private bool[] _uHasSolid = [];

    public int Window => _w;
    public IReadOnlyDictionary<uint, int> Marginals => _marg;

    /// The packed pair key is shared with CouplingCounts so raw evidence and a full
    /// materialization cannot drift in their orientation or bit layout.
    internal static long PackPair(Symbol left, Symbol right) => ((long)left.Value << 32) | right.Value;

    private Couplings(GrammarRule[] rules, int w)
    {
        _rules = rules;
        _w = w;
        _composedFloor = Symbol.FirstNonterminal + (uint)rules.Length;         // the node-birth mint boundary — the base/composed split
        _co = new Dictionary<long, int>[w + 1];
        _coTotals = new long[w + 1];
        for (int d = 1; d <= w; d++) _co[d] = new();
    }

    /// Learn couplings over a compressed chunk-sequence — ONE pass builds the marginals + per-distance
    /// co-counts (rich/robust are just different min_cocount views of these). Also seeds the unit table.
    public static Couplings Learn(RePairResult r, int w = DefaultWindow)
    {
        var c = new Couplings(r.Rules, w);
        c.BuildUnitTable();
        var s = r.Compressed;
        c._symbols.AddRange(s);
        // node-birth mints ids ABOVE the existing nonterminal space (FirstNonterminal + rules.Length)
        c._nextMinted = Symbol.FirstNonterminal + (uint)r.Rules.Length;
        foreach (var sym in s) c._marg[sym.Value] = c._marg.GetValueOrDefault(sym.Value) + 1;
        c._n = 0; foreach (var kv in c._marg) c._n += kv.Value;
        if (c._n == 0) c._n = 1;
        for (int i = 0; i < s.Length; i++)
            for (int d = 1; d <= w && i + d < s.Length; d++)
            {
                long key = PackPair(s[i], s[i + d]);
                ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(c._co[d], key, out _);
                slot++;
                c._coTotals[d]++;
            }
        c.RebuildCoIndexes();
        return c;
    }

    /// Materialize the sampler's unit table over already-maintained raw counts.  This is
    /// separate from scorer materialization: rule edits invalidate expansions/generator
    /// state, while sequence-count edits invalidate the global PPMI views.
    public static Couplings FromCounts(GrammarRule[] rules, CouplingCounts counts)
    {
        var c = new Couplings((GrammarRule[])rules.Clone(), counts.Window);
        c.BuildUnitTable();
        c._symbols.AddRange(counts.Symbols);
        c._nextMinted = Symbol.FirstNonterminal + (uint)rules.Length;
        foreach (var (u, n) in counts.Marginals) c._marg[u] = n;
        c._n = counts.SymbolCount == 0 ? 1 : counts.SymbolCount;
        for (int d = 1; d <= counts.Window; d++)
            foreach (var (key, n) in counts.CoCounts(d)) { c._co[d][key] = n; c._coTotals[d] += n; }
        c.RebuildCoIndexes();
        return c;
    }

    /// Append the rule basis from a rule-only publication without rebuilding the
    /// existing coupling evidence. Added rules are emitted in dependency order, so
    /// each new unit's derived expansion can fold from the already-materialized
    /// earlier units. Sequence marginals/co-counts remain untouched.
    internal void AppendRules(GrammarRule[] addedRules)
    {
        if (addedRules is null) throw new ArgumentNullException(nameof(addedRules));
        if (addedRules.Length == 0) return;

        int firstAdded = _rules.Length;
        GrammarRule[] rules = new GrammarRule[firstAdded + addedRules.Length];
        _rules.CopyTo(rules, 0);
        addedRules.CopyTo(rules, firstAdded);
        _rules = rules;

        _composedFloor = Symbol.FirstNonterminal + (uint)_rules.Length;
        EnsureUnitCapacity((int)_composedFloor);
        for (int r = firstAdded; r < _rules.Length; r++)
        {
            int id = (int)Symbol.FirstNonterminal + r;
            GrammarRule rule = _rules[r];
            int m = rule.IsSlot ? Math.Min(1, rule.Pattern.Length) : rule.Pattern.Length;
            int delta = 0;
            long len = 0;
            bool hasSolid = false;
            byte solidTail = 0;
            for (int k = 0; k < m; k++)
            {
                uint child = rule.Pattern[k].Value;
                if (child >= (uint)id)
                    throw new InvalidOperationException($"grammar rule N{id} references non-earlier symbol {child} — Re-Pair emission order broken, derived unit table would be wrong");
                delta += _uDelta[child];
                len += _uLen[child];
                if (_uHasSolid[child]) { hasSolid = true; solidTail = _uSolidTail[child]; }
            }
            _uDelta[id] = delta;
            _uLen[id] = len;
            _uHead[id] = m > 0 ? _uHead[rule.Pattern[0].Value] : (byte)0;
            _uHasSolid[id] = hasSolid;
            _uSolidTail[id] = solidTail;
        }

        // A new rule boundary changes the legal base/composed id partition. Existing
        // minted ids must therefore be discarded by the caller before the next forge;
        // keeping the boundary here makes the next mint deterministic at the new floor.
        _nextMinted = _composedFloor;
        _composed.Clear();
        _expansion.Clear();
    }

    /// Apply a compatible publication directly to the coupling evidence. Rule
    /// additions extend only the unit table; sequence edits touch the marginals and
    /// co-count windows around the edit boundary. Reset/rebase callers deliberately
    /// stay on FromCounts so this path never hides a parent-gap recovery.
    internal void ApplyInstallRevision(InstallRevision publication)
    {
        if (publication.Reset != GrammarResetKinds.None)
            throw new InvalidOperationException("couplings cannot apply a reset publication incrementally");
        if (publication.Delta.AddedRules.Length != 0) AppendRules(publication.Delta.AddedRules);
        GrammarSequenceEdit[] edits = publication.Delta.SequenceEdits;
        if (edits.Length == 0) return;
        for (int i = 1; i < edits.Length; i++)
            if (edits[i - 1].Start < edits[i].Start + edits[i].RemovedLength)
                throw new InvalidOperationException("coupling sequence edits overlap or are not descending");

        foreach (GrammarSequenceEdit edit in edits)
        {
            int oldCount = _symbols.Count;
            int oldEnd = edit.Start + edit.RemovedLength;
            if (edit.Start < 0 || edit.RemovedLength < 0 || edit.Start > oldCount || oldEnd > oldCount)
                throw new ArgumentOutOfRangeException(nameof(publication), "coupling sequence edit exceeds the current sequence");
            RemoveNeighborhood(edit.Start, oldEnd, oldCount);
            _symbols.RemoveRange(edit.Start, edit.RemovedLength);
            _symbols.InsertRange(edit.Start, edit.Inserted);
            AddNeighborhood(edit.Start, edit.Start + edit.Inserted.Length, _symbols.Count);
            _n += edit.Inserted.Length - edit.RemovedLength;
        }
        if (!_symbols.SequenceEqual(publication.Snapshot.Compressed))
            throw new InvalidDataException("incremental couplings diverged from the published sequence");
        if (_n <= 0) _n = 1;
    }

    /// Apply one sequence splice without manufacturing a publication image.  The weave owns the previous
    /// compressed sequence and can therefore expose the exact local edit directly; this keeps the standing PPMI
    /// evidence alive across sleeps instead of relearning the whole grammar on every append.
    internal void ApplySequenceSplice(int start, int removedLength, Symbol[] inserted)
    {
        if (start < 0 || removedLength < 0 || start > _symbols.Count - removedLength)
            throw new ArgumentOutOfRangeException(nameof(start));
        int oldCount = _symbols.Count;
        int end = start + removedLength;
        RemoveNeighborhood(start, end, oldCount);
        _symbols.RemoveRange(start, removedLength);
        _symbols.InsertRange(start, inserted);
        AddNeighborhood(start, start + inserted.Length, _symbols.Count);
        _n += inserted.Length - removedLength;
        if (_n <= 0) _n = 1;
    }

    internal void RefreshScorer(Scorer scorer, int minCocount = 1, int topk = 14, int minUnitFreq = 2)
    {
        scorer.LastRemovedKeys = 0;
        scorer.LastIndexedKeys = 0;
        if (_dirtyUnits.Count == 0) return;
        HashSet<uint> dirty = new(_dirtyUnits);
        HashSet<uint> affected = scorer.RemoveUnits(dirty);
        foreach (uint a in dirty)
            if (_coForward.TryGetValue(a, out var outgoing))
                foreach (var (b, d) in outgoing) AddScorerEdge(a, b, d);
        foreach (uint b in dirty)
            if (_coReverse.TryGetValue(b, out var incoming))
                foreach (var (a, d) in incoming) AddScorerEdge(a, b, d);
        scorer.RebuildTopK(topk, affected);
        _dirtyUnits.Clear();

        void AddScorerEdge(uint a, uint b, int d)
        {
            int n = _co[d].GetValueOrDefault(PackPair(new Symbol(a), new Symbol(b)));
            long nd = _coTotals[d] == 0 ? 1 : _coTotals[d];
            if (a == b || _marg.GetValueOrDefault(a) < minUnitFreq || _marg.GetValueOrDefault(b) < minUnitFreq || n < minCocount) return;
            scorer.AddEdge(a, b, 0.0, d, affected);
        }
    }

    internal double ComputePmi(uint a, uint b, int d)
    {
        if ((uint)d > (uint)_w || d == 0) return 0.0;
        int n = _co[d].GetValueOrDefault(PackPair(new Symbol(a), new Symbol(b)));
        if (a == b || n < 1) return 0.0;
        int ma = _marg.GetValueOrDefault(a), mb = _marg.GetValueOrDefault(b);
        if (ma == 0 || mb == 0) return 0.0;
        long nd = _coTotals[d] == 0 ? 1 : _coTotals[d];
        return Math.Log((double)n / nd / (((double)ma / _n) * ((double)mb / _n) + 1e-12) + 1e-12);
    }

    internal void ClearDirty() => _dirtyUnits.Clear();

    private void RebuildCoIndexes()
    {
        _coForward.Clear(); _coReverse.Clear();
        for (int d = 1; d <= _w; d++)
            foreach (long key in _co[d].Keys)
            {
                uint a = (uint)(key >> 32), b = (uint)(key & 0xFFFFFFFFL);
                (_coForward.TryGetValue(a, out var f) ? f : _coForward[a] = new()).Add((b, d));
                (_coReverse.TryGetValue(b, out var r) ? r : _coReverse[b] = new()).Add((a, d));
            }
    }

    private void RemoveNeighborhood(int start, int end, int count)
    {
        for (int i = Math.Max(0, start - _w); i < count; i++)
            for (int d = 1; d <= _w && i + d < count; d++)
            {
                int j = i + d;
                if (Touches(i, j, start, end)) DecrementCo(_symbols[i], _symbols[j], d);
            }
        for (int i = start; i < end; i++) DecrementMarginal(_symbols[i]);
    }

    private void AddNeighborhood(int start, int end, int count)
    {
        for (int i = Math.Max(0, start - _w); i < count; i++)
            for (int d = 1; d <= _w && i + d < count; d++)
            {
                int j = i + d;
                if (Touches(i, j, start, end)) IncrementCo(_symbols[i], _symbols[j], d);
            }
        for (int i = start; i < end; i++) IncrementMarginal(_symbols[i]);
    }

    private static bool Touches(int left, int right, int start, int end)
        => (uint)(left - start) < (uint)(end - start) || (uint)(right - start) < (uint)(end - start);

    private void IncrementMarginal(Symbol symbol)
    {
        _marg[symbol.Value] = _marg.GetValueOrDefault(symbol.Value) + 1;
        _dirtyUnits.Add(symbol.Value);
    }

    private void DecrementMarginal(Symbol symbol)
    {
        int count = _marg[symbol.Value] - 1;
        if (count == 0) _marg.Remove(symbol.Value); else _marg[symbol.Value] = count;
        _dirtyUnits.Add(symbol.Value);
    }

    private void IncrementCo(Symbol left, Symbol right, int distance)
    {
        long key = PackPair(left, right);
        bool fresh = !_co[distance].ContainsKey(key);
        _co[distance][key] = _co[distance].GetValueOrDefault(key) + 1;
        _coTotals[distance]++;
        if (fresh)
        {
            (_coForward.TryGetValue(left.Value, out var f) ? f : _coForward[left.Value] = new()).Add((right.Value, distance));
            (_coReverse.TryGetValue(right.Value, out var r) ? r : _coReverse[right.Value] = new()).Add((left.Value, distance));
        }
        _dirtyUnits.Add(left.Value); _dirtyUnits.Add(right.Value);
    }

    private void DecrementCo(Symbol left, Symbol right, int distance)
    {
        long key = PackPair(left, right);
        int count = _co[distance][key] - 1;
        if (count == 0)
        {
            _co[distance].Remove(key);
            if (_coForward.TryGetValue(left.Value, out var f)) f.Remove((right.Value, distance));
            if (_coReverse.TryGetValue(right.Value, out var r)) r.Remove((left.Value, distance));
        }
        else _co[distance][key] = count;
        _coTotals[distance]--;
        _dirtyUnits.Add(left.Value); _dirtyUnits.Add(right.Value);
    }

    internal long ExpandMisses, ExpandMissBytes;   // profile lens — cold expansions triggered per sample call (reset by the generator)

    // fold the derived unit table bottom-up over the rules — O(rules + Σ|pattern|), zero byte materialization.
    // Re-Pair emission order guarantees a pattern references only EARLIER ids (a rule replaces a pair of already-
    // existing symbols), and sleep's demote/slot passes retain that: demoted rules keep their Pattern, slot rules'
    // members predate the slot. The guard throws LOUD if the invariant ever breaks — a silent wrong fold here would
    // be an invisible Vow break (the energy would read wrong deltas and the sampler would drift).
    private void BuildUnitTable()
    {
        int n = (int)_composedFloor;
        _uDelta = new int[n]; _uLen = new long[n]; _uHead = new byte[n]; _uSolidTail = new byte[n]; _uHasSolid = new bool[n];
        for (int t = 0; t < (int)Symbol.FirstNonterminal; t++)
        {
            byte b = (byte)t;
            _uDelta[t] = b == (byte)'{' ? 1 : b == (byte)'}' ? -1 : 0;
            _uLen[t] = 1;
            _uHead[t] = b;
            bool solid = b != (byte)'\n' && b != (byte)' ' && b != (byte)'\t';
            _uHasSolid[t] = solid; if (solid) _uSolidTail[t] = b;
        }
        for (int r = 0; r < _rules.Length; r++)
        {
            int id = (int)Symbol.FirstNonterminal + r;
            var rule = _rules[r];
            int m = rule.IsSlot ? Math.Min(1, rule.Pattern.Length) : rule.Pattern.Length;   // slot expands to its representative member (Reconstruct's tape-less read)
            int delta = 0; long len = 0; bool hasSolid = false; byte solidTail = 0;
            for (int k = 0; k < m; k++)
            {
                uint c = rule.Pattern[k].Value;
                if (c >= (uint)id) throw new InvalidOperationException($"grammar rule N{id} references non-earlier symbol {c} — Re-Pair emission order broken, derived unit table would be wrong");
                delta += _uDelta[c]; len += _uLen[c];
                if (_uHasSolid[c]) { hasSolid = true; solidTail = _uSolidTail[c]; }
            }
            _uDelta[id] = delta; _uLen[id] = len;
            _uHead[id] = m > 0 ? _uHead[rule.Pattern[0].Value] : (byte)0;
            _uHasSolid[id] = hasSolid; _uSolidTail[id] = solidTail;
        }
    }

    // grow the derived arrays to hold a freshly minted composed id (amortized doubling; mint order is deterministic).
    private void EnsureUnitCapacity(int need)
    {
        if (need <= _uDelta.Length) return;
        int cap = Math.Max(need, _uDelta.Length * 2);
        Array.Resize(ref _uDelta, cap); Array.Resize(ref _uLen, cap); Array.Resize(ref _uHead, cap);
        Array.Resize(ref _uSolidTail, cap); Array.Resize(ref _uHasSolid, cap);
    }

    // ── the unit table (expansion bytes on demand; aggregates from the derived table) ───────────────────
    public byte[] Expand(uint unit)
    {
        if (_expansion.TryGetValue(unit, out var e)) return e;
        if (unit >= _composedFloor && _composed.TryGetValue(unit, out var leaves))
        {
            e = new byte[(int)_uLen[unit]];                                    // a composed unit's bytes = its leaf expansions concatenated (lazy — MintChain no longer materializes)
            int off = 0;
            foreach (var l in leaves) { var el = Expand(l); el.CopyTo(e, off); off += el.Length; }
        }
        else e = Reconstruct.Expand(_rules, [new Symbol(unit)]);
        ExpandMisses++; ExpandMissBytes += e.Length;
        _expansion[unit] = e;
        return e;
    }

    /// Net curly-brace delta of a unit's expansion (opens − closes) — the per-candidate structural term the
    /// energy reads: one flat array load off the derived unit table (was a grammar-sized dict probe backed by
    /// whole-vocab byte expansion).
    public int Delta(uint unit) => _uDelta[unit];

    /// Expansion opens on a letter or brace (a plausible statement start) — the energy's head-cleanliness read,
    /// off the derived head byte (no byte materialization). Same predicate the old byte-walk applied to e[0].
    public bool HeadClean(uint unit) { byte b = _uHead[unit]; return _uLen[unit] > 0 && (char.IsLetter((char)b) || b == (byte)'{' || b == (byte)'}'); }

    /// Last non-{'\n',' ','\t'} byte is a terminator — the energy's tail-cleanliness read, off the derived solid
    /// tail (all-whitespace expansions have no solid byte and read unclean, exactly like the old backward scan).
    public bool TailClean(uint unit) => _uHasSolid[unit] && _uSolidTail[unit] is (byte)';' or (byte)'}' or (byte)')';

    /// A PPMI scorer at one regularization: keep only couplings seen ≥ minCocount (a hapax co-occurrence is
    /// overfit — it does not transfer, so judging by it sinks held-out coherence). fwd/bwd carry the top-K
    /// coherent neighbours (aggregated over distance) for candidate proposal.
    public Scorer BuildScorer(int minCocount, int topk = 14, int minUnitFreq = 2)
    {
        var edge = new Dictionary<long, double>();                              // eligible keys; φ is resolved against live counts
        for (int d = 1; d <= _w; d++)
        {
            foreach (var (key, n) in _co[d])
            {
                uint a = (uint)(key >> 32), b = (uint)(key & 0xFFFFFFFFL);
                if (a == b) continue;
                if (_marg.GetValueOrDefault(a) < minUnitFreq || _marg.GetValueOrDefault(b) < minUnitFreq || n < minCocount) continue;
                edge[((long)a << 40) | ((long)b << 8) | (uint)d] = 0.0;
            }
        }
        return new Scorer(_w, edge, this, minCocount, topk, minUnitFreq);
    }

    /// The marginal vocabulary (units with freq ≥ minUnitFreq), id-sorted (deterministic) + freq weights —
    /// the i.i.d. seed distribution and the exploration pool.
    public (uint[] Vocab, long[] Weights) Vocabulary(int minUnitFreq = 2)
    {
        var v = new List<uint>();
        foreach (var (u, c) in _marg) if (c >= minUnitFreq) v.Add(u);
        v.Sort();
        var w = new long[v.Count];
        for (int i = 0; i < v.Count; i++) w[i] = _marg[v[i]];
        return (v.ToArray(), w);
    }

    // ── NODE-BIRTH SEAM ─────────────────────────────────────────────────────────────────────────────────
    /// Mint a NEW composed unit from an affinity CHAIN of leaves (node-birth, cogito-nodebirth-probe): its
    /// expansion is the leaves concatenated, its brace-delta their sum (the correct global constraint), its
    /// provisional seeding mass weight_frac·min(leaf mass). The unit joins the vocabulary/table so the SAME
    /// driver can place it — a deep unit whose internal thread is bounded by COMPOSITION, not the W≤3 pairwise
    /// horizon (chains reach past what Re-Pair's exact-recurrence can). Couplings are inherited at the boundaries
    /// (Head/Tail) by the generator, not stored here. Returns the new composed id.
    public uint MintChain(ReadOnlySpan<uint> leaves)
    {
        uint id = _nextMinted++;
        EnsureUnitCapacity((int)id + 1);
        int delta = 0; long len = 0; long minMass = long.MaxValue; bool hasSolid = false; byte solidTail = 0;
        foreach (var l in leaves)
        {
            len += _uLen[l]; delta += _uDelta[l];
            if (_uHasSolid[l]) { hasSolid = true; solidTail = _uSolidTail[l]; }
            minMass = Math.Min(minMass, _marg.GetValueOrDefault(l, 1));
        }
        _uLen[id] = len; _uDelta[id] = delta;                                  // aggregates fold from the leaves (the derived table); bytes stay lazy — Expand concatenates on first demand
        _uHead[id] = leaves.Length > 0 ? _uHead[leaves[0]] : (byte)0;
        _uHasSolid[id] = hasSolid; _uSolidTail[id] = solidTail;
        _marg[id] = Math.Max(1, (int)(0.6 * minMass));   // weight_frac 0.6 · min-leaf mass — the driver's seeding share
        _composed[id] = leaves.ToArray();
        return id;
    }

    public uint MintComposed(uint a, uint b) => MintChain([a, b]);

    /// A COPY for independent composition — the per-stride energy cache (EnergyPolicy) forges the composition
    /// operator into a clone so the UN-composed base survives for a depth=0 step, both byte-identical to a fresh
    /// Learn+Compose (the Vow). Only the node-birth-mutable state is duplicated (marginals, the expansion/delta
    /// caches, the composed table + mint cursor); the co-count views + rule array are read-only after Learn (compose
    /// never writes them) so they are SHARED — cloning is O(vocab), paid once per re-induce, not per step.
    public Couplings Clone()
    {
        var c = new Couplings(_rules, _w);
        c._symbols.AddRange(_symbols);
        foreach (var (k, v) in _marg) c._marg[k] = v;
        c._n = _n;
        for (int d = 1; d <= _w; d++) { c._co[d] = _co[d]; c._coTotals[d] = _coTotals[d]; }              // read-only after Learn — share (compose touches only _marg/_expansion/the derived table/_composed)
        c.RebuildCoIndexes();
        foreach (var (k, v) in _expansion) c._expansion[k] = v;      // byte[] values immutable — shallow copy is faithful
        c._uDelta = (int[])_uDelta.Clone(); c._uLen = (long[])_uLen.Clone(); c._uHead = (byte[])_uHead.Clone();
        c._uSolidTail = (byte[])_uSolidTail.Clone(); c._uHasSolid = (bool[])_uHasSolid.Clone();   // mint grows these — the clone owns its copy
        foreach (var (k, v) in _composed) c._composed[k] = v;
        c._nextMinted = _nextMinted;
        return c;
    }

    // The base/composed split is an id-range test: a base unit (u < floor) can NEVER be in _composed, so the
    // floor guard skips the dict probe for the common case (the energy loop calls Head/Tail/IsComposed/Leaves
    // per candidate; base units dominate). Byte-identical to the bare TryGetValue — u<floor units are absent
    // from _composed, so both paths return the identity.
    public bool IsComposed(uint u) => u >= _composedFloor && _composed.ContainsKey(u);
    public uint[] Leaves(uint u) => u >= _composedFloor && _composed.TryGetValue(u, out var l) ? l : [u];
    public uint Head(uint u) => u >= _composedFloor && _composed.TryGetValue(u, out var l) ? l[0] : u;    // entering a composed unit = entering its head leaf
    public uint Tail(uint u) => u >= _composedFloor && _composed.TryGetValue(u, out var l) ? l[^1] : u;   // leaving a composed unit = leaving its tail leaf
    public IEnumerable<uint> ComposedIds() => _composed.Keys;                       // insertion order = forge order (deterministic)
}

/// One regularization's PPMI graph: φ(a,b,d) for the energy, fwd/bwd top-K for candidate proposal.
public sealed class Scorer
{
    private readonly int _w;
    private readonly Dictionary<long, double> _edge;
    private readonly Couplings _source;
    private readonly int _topk;
    private readonly Dictionary<uint, List<long>> _fwdKeys = new();
    private readonly Dictionary<uint, List<long>> _bwdKeys = new();
    private readonly Dictionary<uint, (long Epoch, (uint u, double phi)[] Values)> _fwdCache = new();
    private readonly Dictionary<uint, (long Epoch, (uint u, double phi)[] Values)> _bwdCache = new();
    private long _epoch;

    internal int LastRemovedKeys { get; set; }
    internal int LastIndexedKeys { get; set; }

    public Scorer(int w, Dictionary<long, double> edge,
        Couplings source, int minCocount, int topk, int minUnitFreq)
    {
        _w = w; _edge = edge; _source = source;
        _topk = topk;
        foreach (long key in edge.Keys) IndexKey(key);
    }

    public int Window => _w;
    // Count of count-eligible pair keys.  PPMI positivity is a live view because
    // N/nd offsets move on every sequence edit; Fwd/Bwd filter φ<=0 at query time.
    public int EdgeCount => _edge.Count;

    internal HashSet<uint> RemoveUnits(HashSet<uint> units)
    {
        var affected = new HashSet<uint>(units);
        var remove = new HashSet<long>();
        foreach (uint unit in units)
        {
            if (_fwdKeys.TryGetValue(unit, out var outgoing)) remove.UnionWith(outgoing);
            if (_bwdKeys.TryGetValue(unit, out var incoming)) remove.UnionWith(incoming);
        }
        foreach (long key in remove) RemoveKey(key, affected);
        LastRemovedKeys = remove.Count;
        _epoch++;
        return affected;
    }

    internal void AddEdge(uint a, uint b, double phi, int d, HashSet<uint> affected)
    {
        long key = ((long)a << 40) | ((long)b << 8) | (uint)d;
        if (!_edge.ContainsKey(key)) { _edge[key] = 0.0; IndexKey(key); LastIndexedKeys++; }
        affected.Add(a); affected.Add(b);
    }

    internal void RebuildTopK(int topk, HashSet<uint> affected)
    {
        // PPMI carries global N/nd offsets, so a local count edit can change every
        // edge's numeric score.  Keep adjacency immutable and invalidate endpoint
        // views with one scalar epoch; Fwd/Bwd rematerialize only when queried.
        _epoch++;
    }

    public bool Matches(Scorer other)
    {
        if (_w != other._w || _edge.Count != other._edge.Count) return false;
        foreach (long key in _edge.Keys)
            if (!other._edge.ContainsKey(key) || BitConverter.DoubleToInt64Bits(PhiKey(key)) != BitConverter.DoubleToInt64Bits(other.PhiKey(key))) return false;
        return true;
    }

    public double Phi(uint a, uint b, int d) => PhiKey(((long)a << 40) | ((long)b << 8) | (uint)d);

    public (uint u, double phi)[] Fwd(uint a)
    {
        if (_fwdCache.TryGetValue(a, out var cached) && cached.Epoch == _epoch) return cached.Values;
        var values = Materialize(_fwdKeys, a, forward: true);
        _fwdCache[a] = (_epoch, values);
        return values;
    }

    public (uint u, double phi)[] Bwd(uint b)
    {
        if (_bwdCache.TryGetValue(b, out var cached) && cached.Epoch == _epoch) return cached.Values;
        var values = Materialize(_bwdKeys, b, forward: false);
        _bwdCache[b] = (_epoch, values);
        return values;
    }

    private double PhiKey(long key)
    {
        if (!_edge.ContainsKey(key)) return 0.0;
        uint a = (uint)(key >> 40), b = (uint)((key >> 8) & 0xFFFFFFFFL);
        int d = (int)(key & 0xFF);
        return Math.Max(0.0, _source.ComputePmi(a, b, d));
    }

    private (uint u, double phi)[] Materialize(Dictionary<uint, List<long>> index, uint anchor, bool forward)
    {
        if (!index.TryGetValue(anchor, out var keys)) return [];
        var values = new List<(uint u, double phi)>(Math.Min(_topk, keys.Count));
        foreach (long key in keys)
        {
            double phi = PhiKey(key);
            if (phi <= 0) continue;
            uint u = forward ? (uint)((key >> 8) & 0xFFFFFFFFL) : (uint)(key >> 40);
            values.Add((u, phi));
        }
        values.Sort(static (x, y) => x.phi != y.phi ? y.phi.CompareTo(x.phi) : x.u.CompareTo(y.u));
        if (values.Count > _topk) values.RemoveRange(_topk, values.Count - _topk);
        return values.ToArray();
    }

    private void IndexKey(long key)
    {
        uint a = (uint)(key >> 40), b = (uint)((key >> 8) & 0xFFFFFFFFL);
        ( _fwdKeys.TryGetValue(a, out var f) ? f : _fwdKeys[a] = new()).Add(key);
        ( _bwdKeys.TryGetValue(b, out var r) ? r : _bwdKeys[b] = new()).Add(key);
    }

    private void RemoveKey(long key, HashSet<uint> affected)
    {
        if (!_edge.Remove(key)) return;
        uint a = (uint)(key >> 40), b = (uint)((key >> 8) & 0xFFFFFFFFL);
        if (_fwdKeys.TryGetValue(a, out var f)) f.Remove(key);
        if (_bwdKeys.TryGetValue(b, out var r)) r.Remove(key);
        affected.Add(a); affected.Add(b);
    }

    /// Fold this graph's φ into a shared edge dict scaled by `weight` — the CombinedScore's pre-merge, so its
    /// per-lookup `wRich·rich + wRobust·robust` collapses to ONE probe (the same keys, the same doubles).
    internal void AccumulateInto(Dictionary<long, double> dst, double weight)
    {
        foreach (long k in _edge.Keys)
        {
            double phi = PhiKey(k);
            if (phi > 0) dst[k] = dst.GetValueOrDefault(k) + weight * phi;
        }
    }
}

/// The combined score — SumPhi(0.5·rich + 1·robust): rich rewards the THREADING candidates, robust rewards
/// TRANSFERABLE structure; summed, one energy lands both (scoring with either alone took only one fork). The two
/// φ-graphs are PRE-MERGED at construction (built once per stride) and flattened into TWO sorted CSR adjacency
/// views — source-keyed (fwd: all (b,d) of a) and dest-keyed (bwd: all (a,d) of b). : the energy loop probes
/// φ with one endpoint FIXED per position (left: Phi(TailL[d], x, d) · right: Phi(x, HeadR[d], d)); resolving the
/// fixed endpoint's slice once per position turns the ~44 per-candidate reads into binary searches inside a slice
/// that stays cache-hot — per-probe cost stops scaling with the O(edges) table (the measured per-candidate growth,
/// 0.46→1.4µs over 84→2000 rules, was exactly these DRAM-random dict probes). The merge folds the SAME
/// `wRich·rich[k] + wRobust·robust[k]` each key would compute at lookup, so every stored double is byte-identical.
public sealed class CombinedScore
{
    private readonly int _w;
    // CSR: _fwdStart[a].._fwdStart[a+1] indexes (_fwdKey,_fwdVal) — a's edges keyed (b<<8|d), sorted; bwd mirrors.
    private readonly int[] _fwdStart; private readonly long[] _fwdKey; private readonly double[] _fwdVal;
    private readonly int[] _bwdStart; private readonly long[] _bwdKey; private readonly double[] _bwdVal;

    public CombinedScore(Scorer rich, Scorer robust, double wRich = 0.5, double wRobust = 1.0)
    {
        _w = rich.Window;
        var merged = new Dictionary<long, double>(rich.EdgeCount + robust.EdgeCount);
        robust.AccumulateInto(merged, wRobust);   // robust first (weight 1) then rich (weight 0.5) — IEEE add is commutative, so the sum matches the old per-lookup order exactly
        rich.AccumulateInto(merged, wRich);

        // flatten → the two CSR views. Per-slice sort by packed key makes the layout deterministic regardless of
        // dict iteration order; the values are key-determined, so the views are a pure function of the merge.
        uint maxId = 0;
        foreach (var k in merged.Keys)
        {
            uint a = (uint)((ulong)k >> 40), b = (uint)((k >> 8) & 0xFFFFFFFFL);
            if (a > maxId) maxId = a; if (b > maxId) maxId = b;
        }
        int n = merged.Count, ids = (int)maxId + 1;
        _fwdStart = new int[ids + 1]; _bwdStart = new int[ids + 1];
        _fwdKey = new long[n]; _fwdVal = new double[n]; _bwdKey = new long[n]; _bwdVal = new double[n];
        foreach (var k in merged.Keys) { _fwdStart[(uint)((ulong)k >> 40) + 1]++; _bwdStart[(uint)((k >> 8) & 0xFFFFFFFFL) + 1]++; }
        for (int i = 0; i < ids; i++) { _fwdStart[i + 1] += _fwdStart[i]; _bwdStart[i + 1] += _bwdStart[i]; }
        Span<int> fCur = new int[ids], bCur = new int[ids];
        foreach (var (k, v) in merged)
        {
            uint a = (uint)((ulong)k >> 40), b = (uint)((k >> 8) & 0xFFFFFFFFL);
            long inner = k & 0xFF_FFFF_FFFFL;                                   // (b<<8|d) — the key's low 40 bits
            int d = (int)(k & 0xFF);
            int fi = _fwdStart[a] + fCur[(int)a]++;
            _fwdKey[fi] = inner; _fwdVal[fi] = v;
            int bi = _bwdStart[b] + bCur[(int)b]++;
            _bwdKey[bi] = ((long)a << 8) | (uint)d; _bwdVal[bi] = v;
        }
        for (int i = 0; i < ids; i++)
        {
            Array.Sort(_fwdKey, _fwdVal, _fwdStart[i], _fwdStart[i + 1] - _fwdStart[i]);
            Array.Sort(_bwdKey, _bwdVal, _bwdStart[i], _bwdStart[i + 1] - _bwdStart[i]);
        }
    }

    public int Window => _w;
    public bool Matches(CombinedScore other)
        => _w == other._w && _fwdStart.AsSpan().SequenceEqual(other._fwdStart)
        && _fwdKey.AsSpan().SequenceEqual(other._fwdKey) && _fwdVal.AsSpan().SequenceEqual(other._fwdVal)
        && _bwdStart.AsSpan().SequenceEqual(other._bwdStart)
        && _bwdKey.AsSpan().SequenceEqual(other._bwdKey) && _bwdVal.AsSpan().SequenceEqual(other._bwdVal);
    public double Phi(uint a, uint b, int d) { var (s, e) = FwdSlice(a); return Search(_fwdKey, _fwdVal, s, e, ((long)b << 8) | (uint)d); }

    /// The CSR slice of edges LEAVING `a` — resolved once per position for a fixed left neighbour, then probed
    /// per candidate via `FwdSearch`. (Start==End ⇒ no edges.)
    public (int Start, int End) FwdSlice(uint a) => a + 1 < (uint)_fwdStart.Length ? (_fwdStart[a], _fwdStart[a + 1]) : (0, 0);
    /// The CSR slice of edges ENTERING `b` — the right-side mirror (fixed right neighbour, candidate-sourced probe).
    public (int Start, int End) BwdSlice(uint b) => b + 1 < (uint)_bwdStart.Length ? (_bwdStart[b], _bwdStart[b + 1]) : (0, 0);
    public double FwdSearch(int start, int end, long key) => Search(_fwdKey, _fwdVal, start, end, key);
    public double BwdSearch(int start, int end, long key) => Search(_bwdKey, _bwdVal, start, end, key);

    private static double Search(long[] keys, double[] vals, int lo, int hi, long key)
    {
        hi--;                                                                   // inclusive
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            long k = keys[mid];
            if (k == key) return vals[mid];
            if (k < key) lo = mid + 1; else hi = mid - 1;
        }
        return 0.0;
    }
}

/// Global scorer products for one raw count revision.  Rich, robust, and the merged CSR
/// are intentionally one named materialization: rebuilding any one of them without the
/// others would make the generator and forge read different count revisions.
public sealed class ScorerMaterialization
{
    public ScorerMaterialization(Couplings couplings, GrammarRevisionID countRevision)
    {
        CountRevision = countRevision;
        Rich = couplings.BuildScorer(minCocount: 1);
        Robust = couplings.BuildScorer(minCocount: 5);
        Combined = new CombinedScore(Rich, Robust);
        Trace.Engine.Event($"grammar.scorer-materialization revision={countRevision} rich={Rich.EdgeCount} robust={Robust.EdgeCount} combined=ready");
    }

    public GrammarRevisionID CountRevision { get; }
    public Scorer Rich { get; }
    public Scorer Robust { get; }
    public CombinedScore Combined { get; }

    public bool Matches(ScorerMaterialization other)
        => CountRevision == other.CountRevision && Rich.Matches(other.Rich)
        && Robust.Matches(other.Robust) && Combined.Matches(other.Combined);
}

/// The MARKOV TRANSITION evidence — the field's `w_seq·transition` term, lifted from MetabolicWalk's order-2→
/// order-1 successor tables (Farm.cs), SEALED at build into sorted CSR successor rows with the add-α-smoothed
/// log P(x | a,b) precomputed per entry. The per-candidate read is one binary search inside the position-resolved
/// row — cache-hot, no libm, no grammar-sized dict probe (the same cure CombinedScore's φ side received: the
/// nested successor dicts were DRAM-random per candidate and re-grew the per-candidate cost past 20k rules, and
/// the old (count,total)→log memo churned against its clear-on-full cap). LogProb ≤ 0, near 0 for a frequent
/// successor, sharply negative for a rare one. The field reads it boundary-inherited (Tail of the left context →
/// Head of the candidate), so a composed deep unit inherits its constituents' transition mass at the seams.
public sealed class Transitions
{
    // context → CSR row, probed once per position by Resolve. Rows are laid out in sorted-context order and each
    // row's successors sorted by key, so the whole layout is a pure function of the counts (deterministic).
    // Counts ride along only for Matches (the incremental oracle's equality read); the logs are what LogProb serves.
    private readonly Dictionary<(uint, uint), int> _row2 = new();
    private readonly Dictionary<uint, int> _row1 = new();
    private readonly int[] _start2; private readonly uint[] _key2; private readonly int[] _count2; private readonly double[] _log2;
    private readonly int[] _tot2; private readonly double[] _miss2;  // per-row total + sealed Smooth(0, tot) — the seen-context-unseen-successor read
    private readonly int[] _start1; private readonly uint[] _key1; private readonly int[] _count1; private readonly double[] _log1;
    private readonly int[] _tot1; private readonly double[] _miss1;
    private readonly int _v;                         // vocabulary size — the add-α smoothing denominator base
    private readonly double _uniform;                // Math.Log(1/_v) — the unseen-context read
    private const double Alpha = 0.5;                // Laplace-ish smoothing so an unseen successor is rare, not impossible

    /// The order-2 sentinel: no two-symbol left context (position 0/1) ⟹ back off to order-1.
    public const uint None = uint.MaxValue;

    private Transitions(int v,
        IReadOnlyDictionary<uint, Dictionary<uint, int>> ctx1, IReadOnlyDictionary<uint, int> tot1,
        IReadOnlyDictionary<(uint, uint), Dictionary<uint, int>> ctx2, IReadOnlyDictionary<(uint, uint), int> tot2)
    {
        _v = Math.Max(1, v);
        _uniform = Math.Log(1.0 / _v);
        var smooth = new Dictionary<long, double>();   // seal-scoped (count,total)→log dedup — one libm call per distinct pair
        (_start1, _key1, _count1, _log1, _tot1, _miss1) = Seal(ctx1, tot1, _row1, smooth);
        (_start2, _key2, _count2, _log2, _tot2, _miss2) = Seal(ctx2, tot2, _row2, smooth);
    }

    private (int[], uint[], int[], double[], int[], double[]) Seal<TKey>(
        IReadOnlyDictionary<TKey, Dictionary<uint, int>> ctx, IReadOnlyDictionary<TKey, int> totals,
        Dictionary<TKey, int> rows, Dictionary<long, double> smooth) where TKey : notnull, IComparable<TKey>
    {
        var ctxKeys = new TKey[ctx.Count];
        int at = 0; foreach (TKey key in ctx.Keys) ctxKeys[at++] = key;
        Array.Sort(ctxKeys);
        int entries = 0; foreach (var map in ctx.Values) entries += map.Count;
        var start = new int[ctxKeys.Length + 1];
        var keys = new uint[entries]; var counts = new int[entries]; var logs = new double[entries];
        var tots = new int[ctxKeys.Length]; var miss = new double[ctxKeys.Length];
        for (int r = 0; r < ctxKeys.Length; r++)
        {
            TKey key = ctxKeys[r];
            rows[key] = r;
            int tot = totals[key];
            tots[r] = tot; miss[r] = SmoothSealed(0, tot, smooth);
            int lo = start[r];
            foreach (var (x, c) in ctx[key]) { keys[lo] = x; counts[lo++] = c; }
            start[r + 1] = lo;
            Array.Sort(keys, counts, start[r], lo - start[r]);
            for (int e = start[r]; e < lo; e++) logs[e] = SmoothSealed(counts[e], tot, smooth);
        }
        return (start, keys, counts, logs, tots, miss);
    }

    private double SmoothSealed(int c, int tot, Dictionary<long, double> smooth)
    {
        long key = ((long)c << 32) | (uint)tot;
        if (!smooth.TryGetValue(key, out double v)) smooth[key] = v = Smooth(c, tot);
        return v;
    }

    internal static Transitions FromCounts(
        IReadOnlyDictionary<uint, Dictionary<uint, int>> successors,
        IReadOnlyDictionary<uint, int> successorTotals,
        IReadOnlyDictionary<(uint, uint), Dictionary<uint, int>> successors2,
        IReadOnlyDictionary<(uint, uint), int> successorTotals2,
        int vocabularySize)
        => new(vocabularySize, successors, successorTotals, successors2, successorTotals2);

    /// A position's RESOLVED left context — the CSR rows located ONCE per position (fixed across all ~44
    /// candidates there). Start==End ⟹ that order is absent (a present context always holds ≥1 successor —
    /// both builders drop emptied contexts). Default ⟹ the uniform read.
    public readonly struct Ctx(int s2Start, int s2End, double miss2, int s1Start, int s1End, double miss1)
    {
        public readonly int S2Start = s2Start, S2End = s2End, S1Start = s1Start, S1End = s1End;
        public readonly double Miss2 = miss2, Miss1 = miss1;
    }

    /// Resolve the (a,b) left context to its CSR rows — the once-per-position half of LogProb.
    public Ctx Resolve(uint a, uint b)
    {
        int s2 = 0, e2 = 0; double m2 = 0;
        if (a != None && _row2.TryGetValue((a, b), out int r2)) { s2 = _start2[r2]; e2 = _start2[r2 + 1]; m2 = _miss2[r2]; }
        int s1 = 0, e1 = 0; double m1 = 0;
        if (_row1.TryGetValue(b, out int r1)) { s1 = _start1[r1]; e1 = _start1[r1 + 1]; m1 = _miss1[r1]; }
        return new Ctx(s2, e2, m2, s1, e1, m1);
    }

    /// The per-candidate half: one binary search inside the resolved row, the sealed log served directly. Branch
    /// order matches the counts-era LogProb exactly (an order-2 context that lacks x still smooths at order-2 —
    /// no fallthrough), and the sealed doubles are the same Smooth(count, total) the memo produced.
    public double LogProb(in Ctx c, uint x)
    {
        if (c.S2End > c.S2Start) { int i = Search(_key2, c.S2Start, c.S2End, x); return i >= 0 ? _log2[i] : c.Miss2; }
        if (c.S1End > c.S1Start) { int i = Search(_key1, c.S1Start, c.S1End, x); return i >= 0 ? _log1[i] : c.Miss1; }
        return _uniform;
    }

    private static int Search(uint[] keys, int lo, int hi, uint x)
    {
        hi--;                                                            // inclusive
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            uint k = keys[mid];
            if (k == x) return mid;
            if (k < x) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    /// MemStat census read — sealed successor entries + context rows across both orders. Counts only.
    internal (int Entries, int Rows) CsrMass => (_key1.Length + _key2.Length, _tot1.Length + _tot2.Length);

    /// Build the successor tables from a compressed chunk-sequence (identical bag construction to MetabolicWalk).
    public static Transitions Build(Symbol[] compressed)
    {
        var vocab = new HashSet<uint>();
        foreach (var sym in compressed) vocab.Add(sym.Value);
        var ctx1 = new Dictionary<uint, Dictionary<uint, int>>(); var tot1 = new Dictionary<uint, int>();
        var ctx2 = new Dictionary<(uint, uint), Dictionary<uint, int>>(); var tot2 = new Dictionary<(uint, uint), int>();
        for (int i = 0; i + 1 < compressed.Length; i++)
        {
            uint b = compressed[i].Value, x = compressed[i + 1].Value;
            Inc(ctx1, tot1, b, x);
            if (i >= 1) Inc(ctx2, tot2, (compressed[i - 1].Value, b), x);
        }
        return new Transitions(vocab.Count, ctx1, tot1, ctx2, tot2);
    }

    public double LogProb(uint a, uint b, uint x) => LogProb(Resolve(a, b), x);

    // the canonical layout (sorted contexts, sorted successors) makes count equality a direct array compare;
    // row-index equality pins each row to the same context on both sides. Logs/miss are functions of the counts.
    public bool Matches(Transitions other)
        => _v == other._v
        && _start1.AsSpan().SequenceEqual(other._start1) && _key1.AsSpan().SequenceEqual(other._key1)
        && _count1.AsSpan().SequenceEqual(other._count1) && _tot1.AsSpan().SequenceEqual(other._tot1)
        && _start2.AsSpan().SequenceEqual(other._start2) && _key2.AsSpan().SequenceEqual(other._key2)
        && _count2.AsSpan().SequenceEqual(other._count2) && _tot2.AsSpan().SequenceEqual(other._tot2)
        && MatchRows(_row1, other._row1) && MatchRows(_row2, other._row2);

    private static bool MatchRows<TKey>(Dictionary<TKey, int> left, Dictionary<TKey, int> right) where TKey : notnull
    {
        if (left.Count != right.Count) return false;
        foreach (var (key, row) in left) if (!right.TryGetValue(key, out int o) || o != row) return false;
        return true;
    }

    private double Smooth(int c, int tot) => Math.Log((c + Alpha) / (tot + Alpha * _v));

    private static void Inc<TKey>(Dictionary<TKey, Dictionary<uint, int>> ctx, Dictionary<TKey, int> tot, TKey k, uint x) where TKey : notnull
    {
        if (!ctx.TryGetValue(k, out var succ)) ctx[k] = succ = new();
        succ[x] = succ.GetValueOrDefault(x) + 1;
        tot[k] = tot.GetValueOrDefault(k) + 1;
    }
}

/// The field augmentation the unified MRF (CouplingGenerator) reads — the three terms beyond the native coupling
/// energy, plus the Phi/Depth scaling. `W` is the live weight vector (the anisotropy the WeightController rides);
/// `Trans` the Markov evidence (null ⟹ Transition term off); `Metab` the curiosity organ the Novelty term reads
/// (null ⟹ Novelty off); `NoiseSeed` seeds the deterministic jitter floor. A struct so it costs nothing to pass.
public readonly record struct FieldTerms(Weights W, Transitions? Trans, Metabolism? Metab, ulong NoiseSeed);

/// The corpus's learned LINE-LENGTH distribution — the byte run-lengths between '\n' terminals in the compressed
/// sequence. The span barrier keeps '\n' out of every RULE, so a rule-composing walk can only place a newline
/// BETWEEN units; left to itself it emits KB-long newline-free runs that re-induce into giant single-span rules
/// (the world-run's 13KB maxSpan — generation was free-riding on the old straddling rules to get its newlines).
/// The generator samples THIS to break lines at the SAME cadence the corpus has, so generated content reproduces
/// the line-length STRUCTURE cogito learned — intrinsic (a learned distribution), not an arbitrary cap. Empirical
/// inverse-CDF sampling over the sorted lengths (each observed line equally likely = the empirical distribution).
/// Byte-alphabet corpora only ('\n' is terminal 10); a token corpus carries no in-band line boundary.
public sealed class LineModel
{
    private readonly int[] _lens;                    // observed line byte-lengths, sorted (the inverse-CDF table)
    private const int DefaultLine = 64;              // no learned lines (single-line corpus / no '\n') → a sane fallback cadence

    public LineModel(RePairResult g)
    {
        int nr = g.Rules.Length;
        var span = new int[nr];                      // per-rule expansion byte-length, memoized bottom-up (a pattern refs only earlier rules)
        for (int i = 0; i < nr; i++)
        {
            int s = 0;
            foreach (var sym in g.Rules[i].Pattern)
                s += sym.Value >= g.AlphabetSize && (int)(sym.Value - g.AlphabetSize) < i ? span[(int)(sym.Value - g.AlphabetSize)] : 1;
            span[i] = s;
        }
        var lens = new List<int>();
        int run = 0;                                 // bytes on the current line, summed over unit expansions between '\n' terminals
        foreach (var sym in g.Compressed)
            if (sym.Value == (uint)'\n') { if (run > 0) lens.Add(run); run = 0; }
            else run += sym.Value >= g.AlphabetSize && (int)(sym.Value - g.AlphabetSize) < nr ? span[(int)(sym.Value - g.AlphabetSize)] : 1;
        if (run > 0) lens.Add(run);
        lens.Sort();
        _lens = lens.Count > 0 ? lens.ToArray() : [DefaultLine];
    }

    /// From the REAL corpus pool — each span IS one line, so the line-length distribution is just the span lengths.
    /// The FROZEN model the trunk hands the generator: built once from source-of-record, it never drifts. A per-stride
    /// model rebuilt from the ACCRETED grammar would relearn its own (hard-capped, ever-shorter) generated lines and
    /// spiral into fragmentation — measured ~5B lines, 500 spans/step. The source-of-record is the fixed point that isn't.
    public LineModel(IReadOnlyList<byte[]> spans)
    {
        var lens = new List<int>(spans.Count);
        foreach (var s in spans) if (s.Length > 0) lens.Add(s.Length);
        lens.Sort();
        _lens = lens.Count > 0 ? lens.ToArray() : [DefaultLine];
    }

    /// Draw a target line length (bytes) — uniform over the observed lengths = inverse-CDF of the empirical
    /// distribution, so the generated line-length cadence matches the learned one. Advances `rng` (deterministic).
    public int Sample(ref Lcg rng) => _lens[(int)(rng.Next() % (ulong)_lens.Length)];
}

/// The generator — an energy landscape OVER the couplings. Global MRF Gibbs (not a bigram walk): each
/// position is resampled to the chunk that best coheres with its whole window under the combined score, plus
/// a light structural prior (brace balance + clean head/tail). Warm-flat body (the meaning), then a
/// cool-commit tail that snaps braces shut while the threaded body stays pinned. Deterministic via a seeded LCG.
///
/// THE UNIFIED FIELD: this MRF IS the one annealed sampler over E(next|ctx,state). Its native
/// energy is the coupling term (−Wc·coherence) + the FORM prior (brace + head/tail); the OPTIONAL `FieldTerms`
/// augmentation adds the other three terms — the Markov TRANSITION log-prob, the curiosity-metabolism NOVELTY
/// (−recency), and a seeded NOISE floor — each scaled by its `Weights` knob, and folds `Weights.Depth` into the
/// composed-unit depth-bonus. With no field (`field: null`) the energy is byte-identical to the proven driver, so
/// the `coupling`/`nodebirth` presets and this class's other callers are unchanged; EnergyPolicy passes a field so
/// ONE sampler expresses the whole zoo as points in weight-space (the machine becomes the walk its weights name).
public sealed class CouplingGenerator
{
    // The proven driver constants (driver.py / coherence_fork.py). Warm body, cool brace-only tail.
    private const double Wc = 2.0;                   // coupling weight
    private const double We = 1.0;                   // head/tail cleanliness weight (warm body)
    private const double Wb = 4.0;                   // brace-validity weight (strong enough to keep warm-T off salad)
    private const double TWarm = 1.2;                // pinned WARM temperature (never cools in the body)
    private const double TCool = 0.06;               // cool-commit tail temperature
    private const double WeTail = 10.0;              // completeness weight cranked for the tail
    private const int WarmSweeps = 16, TailSweeps = 3;
    private const int NExplore = 4, Cap = 44;
    private const double FieldDepthUnit = 0.6;       // the proven node-birth depth-bonus per composed leaf; Weights.Depth SCALES it (Depth 1 = NodeBirthWalk's 0.6)

    private readonly Couplings _cp;
    private readonly Scorer _propose;                // rich graph — broad reach → threading candidates
    private readonly CombinedScore _score;           // 0.5·rich + 1·robust — the energy
    private readonly uint[] _vocab; private readonly long[] _vw; private readonly long _vwTot;
    private readonly long[] _cumw;                    // inclusive prefix sums of _vw — the marginal-sampler's CDF (O(log V) draw, not O(V))
    private readonly uint[] _closer, _neutral;       // structural-repair pools for the cool tail
    private double _wd;                               // composed-unit MDL depth-bonus per leaf (0 = plain coupling; the driver reaches for deep units) — per-CALL (the field's Weights.Depth rides it), so the model caches per stride
    private FieldTerms? _field;                      // the field augmentation (transition/novelty/noise + Phi/Depth scaling); null = the proven pure-coupling energy. Per-CALL: the generator MODEL (couplings/scorers/vocab/pools) is stride-stable; only the field/weights change per step, so `Generate(count, seed, field)` re-arms these on the cached instance
    private readonly Dictionary<uint, List<uint>> _headToComposed = new();   // head-leaf → composed units that START there (propose after its predecessors)
    private readonly Dictionary<uint, List<uint>> _tailToComposed = new();   // tail-leaf → composed units that END there (propose before its successors)
    private readonly HashSet<uint> _candSeen = new();   // reused per-GibbsStep dedup set (clear-don't-new — the candidate build hits this ~11k×/Generate)
    private readonly List<uint> _candBuf = new();       // reused per-GibbsStep candidate buffer (single-use scratch; GibbsStep consumes it before the next call)
    private readonly Dictionary<uint, double> _novMemo = new();   // per-Relax NovLog memo — novelty is constant within a block (Fired/Leak run between blocks), and the same candidates recur across the 16 sweeps (the measured 8.5%-of-cycles libm log was mostly this recomputation)
    private readonly LineModel? _lines;                 // learned line-length distribution — line-aware minting; null = legacy one-'\n'-per-block (byte-identical to pre-model callers)
    private readonly List<byte> _bodyScratch = new();   // reused per-block body buffer (single-use; AppendExpanded consumes it before the next call)
    private byte[] _outputScratch = new byte[256];       // reused across samples; span callers consume it before the next Generate
    // Implicated's per-call scratch (≤TailSweeps× per Relax) — the brace-stack, the position→repair map, and the
    // sorted result. Each is fully consumed inside Relax's foreach before the next Implicated call, so one reused
    // set per generator suffices (clear-don't-new).
    private readonly List<int> _implStack = new();
    private readonly Dictionary<int, Repairs> _implKind = new();
    private readonly List<(int, Repairs)> _implRes = new();
    private readonly List<uint> _seqScratch = new();    // reused per-block relaxation buffer — each Relax result is fully consumed by the caller (Fired loop + AppendExpanded / the GenerateTraced foreach) before the next Relax call

    // ── per-Generate work counters.
    //    Pure accounting: reset at Generate entry, read via StatLine; never touches RNG or ordering. ──
    private long _nGibbs, _nCand, _nBlocks, _nExpandBytes, _tFill, _tEval;
    internal string StatLine() => $"blocks={_nBlocks} gibbs={_nGibbs} cand={_nCand} expB={_nExpandBytes} vocab={_vocab.Length}"
        + $" fill={Trace.MsOf(_tFill)}ms eval={Trace.MsOf(_tEval)}ms xm={_cp.ExpandMisses} xmB={_cp.ExpandMissBytes}";

    public CouplingGenerator(Couplings cp, Scorer rich, Scorer robust, double depthBonus = 0.0, FieldTerms? field = null, LineModel? lines = null, CombinedScore? score = null)
    {
        _cp = cp;
        _propose = rich;
        _score = score ?? new CombinedScore(rich, robust);   // stride-shared when the caller already built it (EnergyPolicy hands the SAME merge to the forge affinity and the sampler — one CSR build per stride)
        _field = field;
        _lines = lines;
        _wd = field is { } f ? f.W.Depth * FieldDepthUnit : depthBonus;   // field routes depth through Weights.Depth; the no-field path keeps the raw bonus
        (_vocab, _vw) = cp.Vocabulary();
        _cumw = new long[_vw.Length];
        long t = 0; for (int i = 0; i < _vw.Length; i++) { t += _vw[i]; _cumw[i] = t; }   // CDF for the O(log V) marginal draw
        _vwTot = t == 0 ? 1 : t;
        foreach (var c in cp.ComposedIds())          // ExtView boundary map: a composed unit inherits its head/tail's couplings
        {
            (_headToComposed.TryGetValue(cp.Head(c), out var hl) ? hl : _headToComposed[cp.Head(c)] = new()).Add(c);
            (_tailToComposed.TryGetValue(cp.Tail(c), out var tl) ? tl : _tailToComposed[cp.Tail(c)] = new()).Add(c);
        }
        // pools mined once: closers (net-negative delta, drain an open) + neutrals (balanced) — the cool
        // tail's repair materials (a coupling-neighbour rarely carries the corrective brace delta).
        var closer = new List<(uint u, long w)>(); var neutral = new List<(uint u, long w)>();
        foreach (var u in _vocab) { int d = cp.Delta(u); if (d < 0) closer.Add((u, cp.Marginals[u])); else if (d == 0) neutral.Add((u, cp.Marginals[u])); }
        closer.Sort((x, y) => y.w.CompareTo(x.w)); neutral.Sort((x, y) => y.w.CompareTo(x.w));
        _closer = closer.Take(30).Select(p => p.u).ToArray();
        _neutral = neutral.Take(30).Select(p => p.u).ToArray();
    }

    /// SAMPLE the field at the augmentation — the per-step entry (EnergyPolicy re-arms the cached,
    /// stride-stable model with this step's live weights/metabolism/noise-seed, then samples). Byte-identical to
    /// constructing a fresh generator with `field:` — only the field/depth-bonus are per-call state.
    public byte[] Generate(int count, ulong seed, FieldTerms field)
    {
        _field = field;
        _wd = field.W.Depth * FieldDepthUnit;
        return GenerateSpan(count, seed).ToArray();
    }

    /// Samples into the generator-owned output buffer. The span is valid until the next sample on this generator;
    /// callers on the hot path consume it immediately instead of paying the compatibility copy in Generate.
    public ReadOnlySpan<byte> GenerateSpan(int count, ulong seed, FieldTerms field)
    {
        _field = field;
        _wd = field.W.Depth * FieldDepthUnit;
        return GenerateSpan(count, seed);
    }

    /// Samples into the generator-owned output buffer without materializing a
    /// compatibility array. The returned memory remains valid until the next
    /// sample on this generator.
    public ReadOnlyMemory<byte> GenerateMemory(int count, ulong seed, FieldTerms field)
    {
        ReadOnlySpan<byte> bytes = GenerateSpan(count, seed, field);
        return _outputScratch.AsMemory(0, bytes.Length);
    }

    internal ReadOnlyMemory<byte> OutputMemory(int length) => _outputScratch.AsMemory(0, length);

    /// Generate ~`count` chunks as a run of short coherent blocks (the coupling generator's natural unit is a
    /// short block; each block a coherent utterance, closed by a '\n'). With a LineModel armed, LONG blocks also
    /// break INTERNALLY at the corpus's learned line-length cadence — so minted spans stay line-bounded and never
    /// re-induce into the giant single-span rules the barrier would otherwise let a rule-composing walk breed.
    public byte[] Generate(int count, ulong seed)
        => GenerateSpan(count, seed).ToArray();

    public ReadOnlySpan<byte> GenerateSpan(int count, ulong seed)
    {
        if (_vocab.Length < 2) return [];
        _nGibbs = _nCand = _nBlocks = _nExpandBytes = _tFill = _tEval = 0;
        _cp.ExpandMisses = 0; _cp.ExpandMissBytes = 0;
        var rng = new Lcg(seed);
        int emitted = 0;
        int outputLength = 0;
        int lineBytes = 0;                                                // bytes on the current line, carried across the intra-block cadence breaks
        int target = _lines?.Sample(ref rng) ?? 0;                       // learned target line length; 0 ⇒ no model ⇒ legacy per-block '\n' only
        while (emitted < count)
        {
            int len = 4 + (int)(rng.Next() % 7);                          // 4..10 units per block
            var seq = Relax(rng, len);
            _nBlocks++;
            emitted += seq.Count;
            // NOVELTY firing (metabolic field term): a placed unit's leaves are "recently emitted", so the next
            // sub-block's novelty read demotes them — the proven anti-collapse decay, at sub-block granularity.
            if (_field is { W.Novelty: not 0, Metab: { } mb })
                foreach (var u in seq)
                    if (_cp.IsComposed(u)) foreach (var l in _cp.Leaves(u)) mb.Fired(l);   // composed: fire each constituent leaf
                    else mb.Fired(u);                                                       // base: its own single leaf (no `[u]` mint per placed unit)
            AppendExpanded(ref outputLength, seq, ref rng, ref lineBytes, ref target);
        }
        return _outputScratch.AsSpan(0, outputLength);
    }

    public ReadOnlyMemory<byte> GenerateMemory(int count, ulong seed)
    {
        ReadOnlySpan<byte> bytes = GenerateSpan(count, seed);
        return _outputScratch.AsMemory(0, bytes.Length);
    }

    /// Generate `blocks` blocks as the placed UNIT-ID sequences — the Relax output BEFORE byte-expansion, the
    /// trail the generator actually walks over the coupling graph (Generate throws this away when it flattens to
    /// bytes). A composed (node-birth) unit is resolved to its leaf chain so every step lands on a real graph
    /// node id. The spatial view of the engine's own composition, for the topology viz (world-boundary dump).
    public List<uint[]> GenerateTraced(int blocks, ulong seed)
    {
        var walks = new List<uint[]>(blocks);
        if (_vocab.Length < 2) return walks;
        var rng = new Lcg(seed);
        for (int b = 0; b < blocks; b++)
        {
            int len = 4 + (int)(rng.Next() % 7);                     // 4..10 units per block (matches Generate)
            var placed = new List<uint>();
            foreach (var u in Relax(rng, len))
                if (_cp.IsComposed(u)) placed.AddRange(_cp.Leaves(u)); else placed.Add(u);
            walks.Add(placed.ToArray());
        }
        return walks;
    }

    // ── the relaxation: warm-flat body (meaning) + cool-commit brace tail (form) ─────────────────────────
    private List<uint> Relax(Lcg rng, int length)
    {
        _novMemo.Clear();                                                   // novelty is CONSTANT within one Relax (Fired/Leak only run between blocks) — memoize per block
        var seq = _seqScratch; seq.Clear();                                 // reused per-block buffer (clear-don't-new; consumed by the caller before the next Relax)
        for (int i = 0; i < length; i++) seq.Add(SampleMarginal(rng));      // i.i.d. seed — coherence is EARNED
        for (int sw = 0; sw < WarmSweeps; sw++)                             // warm body — pinned warm, no anneal
            for (int i = 0; i < seq.Count; i++) GibbsStep(seq, i, rng, TWarm, We, null);
        // cool commit tail — resample ONLY the brace-implicated positions cold, with repair pools injected,
        // completeness cranked; the threaded body stays pinned ("never cool" surrendered only as a tail).
        for (int t = 0; t < TailSweeps; t++)
        {
            var implicated = Implicated(seq);
            if (implicated.Count == 0) break;
            foreach (var (i, kind) in implicated)
                GibbsStep(seq, i, rng, TCool, WeTail, kind == Repairs.Open ? _closer : _neutral);
        }
        return seq;
    }

    private enum Repairs { Open, Close, Head, Tail }

    // positions implicated in INCOMPLETENESS: unmatched opens/closes (brace-stack walk) + unclean head/tail.
    // Reuses the generator's Impl* scratch (cleared per call) — the result is consumed by Relax's foreach before
    // the next Implicated call, so one scratch set kills the three per-call collection allocs.
    private List<(int, Repairs)> Implicated(List<uint> seq)
    {
        var stack = _implStack; stack.Clear();
        var kind = _implKind; kind.Clear();
        for (int i = 0; i < seq.Count; i++)
        {
            int d = _cp.Delta(seq[i]);
            if (d >= 0) for (int k = 0; k < d; k++) stack.Add(i);
            else for (int k = 0; k < -d; k++) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); else kind[i] = Repairs.Close; }
        }
        foreach (var i in stack) kind[i] = Repairs.Open;
        var (h, tl) = HeadTail(seq);
        if (!h) kind[0] = Repairs.Head;
        if (!tl && seq.Count > 0) kind.TryAdd(seq.Count - 1, Repairs.Tail);
        var res = _implRes; res.Clear();
        foreach (var kv in kind) res.Add((kv.Key, kv.Value));
        res.Sort((x, y) => x.Item1.CompareTo(y.Item1));
        return res;
    }

    private void GibbsStep(List<uint> seq, int i, Lcg rng, double temp, double we, uint[]? extra)
    {
        long tf = Trace.NowTicks;
        var cand = Candidates(seq, i, rng, extra);
        long te = Trace.NowTicks; _tFill += te - tf;
        _nGibbs++; _nCand += cand.Count;
        if (cand.Count < 2) { _tEval += Trace.NowTicks - te; return; }
        // ── PER-POSITION INVARIANTS (constant across ALL candidates at i) — hoisted out of the per-candidate
        //    energy loop. The energy is evaluated ~40×/position; without hoisting each candidate re-walked the
        //    window (Tail/Head probes on the FIXED neighbours), re-walked the WHOLE brace (Delta probes on the
        //    fixed positions), and re-expanded the fixed ends. Only x's own boundary/delta/leaves vary. ──
        int n = seq.Count, W = _score.Window;
        double phiW = _field is { } fp ? fp.W.Phi : 1.0;         // Phi scales the proven coupling weight (1 = untouched)
        Span<int> lsl = stackalloc int[2 * (W + 1)];             // lsl[2d..] = the CSR (start,end) of edges LEAVING Tail(seq[i-d]) — the left φ-reads' slice, resolved once per position
        Span<int> rsl = stackalloc int[2 * (W + 1)];             // rsl[2d..] = the CSR (start,end) of edges ENTERING Head(seq[i+d]) — the right mirror
        if (phiW != 0)                                           // Phi:0 (the metabolic/markov presets) ⟹ the φ term is exactly 0 — the slice hoist AND the per-candidate CSR searches are dead work, not deferral
            for (int d = 1; d <= W; d++)
            {
                if (i - d >= 0) (lsl[2 * d], lsl[2 * d + 1]) = _score.FwdSlice(_cp.Tail(seq[i - d]));
                if (i + d <  n) (rsl[2 * d], rsl[2 * d + 1]) = _score.BwdSlice(_cp.Head(seq[i + d]));
            }
        Span<int> deltas = n <= 32 ? stackalloc int[n] : new int[n];
        for (int k = 0; k < n; k++) if (k != i) deltas[k] = _cp.Delta(seq[k]);   // position i's delta is x-specific (BraceAt substitutes it)
        int preStack = 0, preMism = 0;                            // brace state over 0..i-1 — the candidate-invariant prefix (BraceAt resumes from here)
        for (int k = 0; k < i; k++) { int d = deltas[k]; if (d >= 0) preStack += d; else for (int c = 0; c < -d; c++) { if (preStack > 0) preStack--; else preMism++; } }
        bool hConst = i != 0 && _cp.HeadClean(seq[0]);            // head/tail cleanliness is constant unless x occupies that end
        bool tConst = i != n - 1 && _cp.TailClean(seq[n - 1]);
        Transitions.Ctx tCtx = default;                           // the position's RESOLVED transition context (the per-candidate read is one binary search in the sealed CSR row)
        if (_field is { W.Transition: not 0, Trans: { } tr0 } && i >= 1)
            tCtx = tr0.Resolve(i >= 2 ? _cp.Tail(seq[i - 2]) : Transitions.None, _cp.Tail(seq[i - 1]));
        var ctx = new PosCtx(i, n, W, lsl, rsl, deltas, preStack, preMism, hConst, tConst, tCtx, phiW);
        Span<double> es = cand.Count <= 64 ? stackalloc double[cand.Count] : new double[cand.Count];
        double emin = double.MaxValue;
        for (int c = 0; c < cand.Count; c++) { es[c] = EnergyAt(in ctx, cand[c], we); if (es[c] < emin) emin = es[c]; }
        double tot = 0, T = Math.Max(1e-3, temp);
        for (int c = 0; c < cand.Count; c++) { es[c] = Math.Exp(-(es[c] - emin) / T); tot += es[c]; }
        double pick = rng.NextDouble() * tot, acc = 0;
        for (int c = 0; c < cand.Count; c++) { acc += es[c]; if (pick <= acc) { seq[i] = cand[c]; _tEval += Trace.NowTicks - te; return; } }
        seq[i] = cand[^1];
        _tEval += Trace.NowTicks - te;
    }

    // candidate set: self + fwd-neighbours of the left context + bwd-neighbours of the right context (from the
    // RICH proposer) + a few marginal explorers + any structural-repair extras. Insertion-ordered (deterministic).
    private List<uint> Candidates(List<uint> seq, int i, Lcg rng, uint[]? extra)
    {
        _candSeen.Clear(); _candBuf.Clear();                                    // reuse the buffers (clear-don't-new — insertion order + dedup identical)
        void Add(uint u) { if (_candSeen.Add(u)) _candBuf.Add(u); }
        Add(seq[i]);
        for (int d = 1; d <= _propose.Window; d++)
        {
            int j = i - d;
            if (j >= 0) foreach (var (b, _) in _propose.Fwd(_cp.Tail(seq[j])))   // things that cohere AFTER the left context
                { Add(b); if (_headToComposed.TryGetValue(b, out var cs)) foreach (var c in cs) Add(c); }   // + composed units headed by b
            j = i + d;
            if (j < seq.Count) foreach (var (a, _) in _propose.Bwd(_cp.Head(seq[j])))   // things that cohere BEFORE the right context
                { Add(a); if (_tailToComposed.TryGetValue(a, out var cs)) foreach (var c in cs) Add(c); }   // + composed units tailed by a
        }
        for (int k = 0; k < NExplore; k++) Add(SampleMarginal(rng));
        if (extra != null) foreach (var u in extra) Add(u);
        if (_candBuf.Count > Cap) _candBuf.RemoveRange(Cap, _candBuf.Count - Cap);
        return _candBuf;
    }

    /// The candidate-invariant context at one position i — everything the energy read needs that does NOT depend
    /// on the candidate x (the fixed neighbours' resolved φ-slices, the brace prefix + suffix deltas, the
    /// fixed-end cleanliness, the RESOLVED transition context, the Phi weight). Built once per GibbsStep, read
    /// per candidate — : everything grammar-sized is resolved HERE, so the per-candidate loop touches only
    /// position-local slices and sealed CSR rows.
    private readonly ref struct PosCtx(int i, int n, int w,
        ReadOnlySpan<int> lSlice, ReadOnlySpan<int> rSlice, ReadOnlySpan<int> deltas,
        int preStack, int preMism, bool hConst, bool tConst, Transitions.Ctx tCtx, double phiW)
    {
        public readonly int I = i, N = n, W = w, PreStack = preStack, PreMism = preMism;
        public readonly ReadOnlySpan<int> LSlice = lSlice, RSlice = rSlice;
        public readonly ReadOnlySpan<int> Deltas = deltas;
        public readonly bool HConst = hConst, TConst = tConst;
        public readonly Transitions.Ctx TCtx = tCtx;
        public readonly double PhiW = phiW;
    }

    // energy of placing x at i — the field E(next|ctx,state), lower = more likely (the sampler minimizes it):
    //   −Phi·Wc·(coherence both sides)        the coupling term (Phi=1 ⟹ the proven Wc; the pure-coupling path)
    //   −Wd·(composed-unit leaf count)         the DEPTH bonus (Weights.Depth·FieldDepthUnit) — reach for deep units
    //   −Transition·logP(x | left ctx)         the Markov transition evidence (boundary-inherited; logP≤0 so low-prob costs energy)
    //   −Novelty·meanLogWeight(x's leaves)     the curiosity-metabolism (−recency); over-fired units cost energy (anti-collapse)
    //   −Noise·jitter(x,i)                     a static seeded jitter FLOOR (not a regulated organ — the noise-probe finding)
    //   +Wb·(brace imbalance) + we·(head/tail) the FORM prior (always on — what keeps warm-T off salad)
    // Byte-identical to the pre-CSR EnergyAt: same summation order (d=1..W left-then-right), same operands
    // (Tail(seq[i-d]), hx=Head(x); tx=Tail(x), Head(seq[i+d])), same brace/head-tail arithmetic
    // — the φ-reads binary-search the position-resolved CSR slices (same doubles the merged dict held), the
    // transition reads the position-resolved Ctx (same branch order, sealed smooth doubles), novelty reads the
    // per-block memo (same NovLog value — the metabolism is constant within a Relax). : nothing here probes a
    // structure that scales with the grammar; the per-candidate cost is bounded by the position's slices. PhiW=0
    // skips the φ searches outright — the term multiplies to exactly ±0 either way, so the skip is byte-identical.
    private double EnergyAt(in PosCtx ctx, uint x, double we)
    {
        uint hx = _cp.Head(x), tx = _cp.Tail(x);                            // x's boundary units (one probe each, floor-skipped for base)
        double s = 0;
        if (ctx.PhiW != 0)
            for (int d = 1; d <= ctx.W; d++)
            {
                if (ctx.I - d >= 0)     s += _score.FwdSearch(ctx.LSlice[2 * d], ctx.LSlice[2 * d + 1], ((long)hx << 8) | (uint)d);   // left: cohere seq[i-d]→x
                if (ctx.I + d < ctx.N)  s += _score.BwdSearch(ctx.RSlice[2 * d], ctx.RSlice[2 * d + 1], ((long)tx << 8) | (uint)d);   // right: cohere x→seq[i+d]
            }
        double e = -ctx.PhiW * Wc * s + Wb * BraceAt(in ctx, x);
        if (_wd > 0 && _cp.IsComposed(x)) e -= _wd * _cp.Leaves(x).Length;   // reward placing a deep invented unit
        if (_field is { } f)                                                // the unified field's extra terms (each 0-cost when its weight is 0)
        {
            if (f.W.Transition != 0 && f.Trans is { } tr && ctx.I >= 1)     // forward transition (no left context at i=0)
                e -= f.W.Transition * tr.LogProb(in ctx.TCtx, hx);
            if (f.W.Novelty != 0 && f.Metab is { } mb)
            {
                if (!_novMemo.TryGetValue(x, out var nl)) _novMemo[x] = nl = NovLog(mb, x);
                e -= f.W.Novelty * nl;
            }
            if (f.W.Noise != 0)
                e -= f.W.Noise * Jitter(f.NoiseSeed, x, ctx.I);
        }
        bool h = ctx.I == 0        ? _cp.HeadClean(x) : ctx.HConst;          // x sets the head only when it occupies position 0
        bool t = ctx.I == ctx.N - 1 ? _cp.TailClean(x) : ctx.TConst;         // …the tail only at the last position
        e += we * ((h ? 0.0 : 1.0) + (t ? 0.0 : 1.0));
        return e;
    }

    // Brace imbalance (stack + unmatched-closes) with x at position i — resumes from the precomputed prefix state
    // (0..i-1), applies x's delta, then walks the fixed suffix. Same arithmetic as the full-sequence Brace walk.
    private int BraceAt(in PosCtx ctx, uint x)
    {
        int stack = ctx.PreStack, mism = ctx.PreMism;
        int dx = _cp.Delta(x);
        if (dx >= 0) stack += dx; else for (int c = 0; c < -dx; c++) { if (stack > 0) stack--; else mism++; }
        for (int k = ctx.I + 1; k < ctx.N; k++) { int d = ctx.Deltas[k]; if (d >= 0) stack += d; else for (int c = 0; c < -d; c++) { if (stack > 0) stack--; else mism++; } }
        return stack + mism;
    }

    // NOVELTY = mean log curiosity-weight over the unit's leaves — ≤0, near 0 for a novel unit, sharply negative for
    // an over-fired one (Metabolism.Weight = 1/(1+λ·recent)); a composed unit is as novel as its constituents average.
    // A BASE unit is its own single leaf — read it directly (Leaves would mint a throwaway `[x]` per candidate; this
    // path is per-candidate hot when novelty is on), only the composed case touches the stored leaf array.
    private double NovLog(Metabolism mb, uint x)
    {
        if (!_cp.IsComposed(x)) return Math.Log(Math.Max(1e-6, mb.Weight(x)));
        var leaves = _cp.Leaves(x);
        double sum = 0;
        foreach (var l in leaves) sum += Math.Log(Math.Max(1e-6, mb.Weight(l)));
        return sum / leaves.Length;
    }

    // NOISE floor — a deterministic jitter over (candidate, position), SplitMix64-hashed from the block seed (no
    // sampling-RNG state consumed, so a zero-noise field stays byte-identical). Range [−0.5, 0.5]: a simple floor.
    private static double Jitter(ulong seed, uint x, int i)
    {
        ulong h = seed ^ ((ulong)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)i * 0xD1B54A32D192ED03UL);
        h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 27; h *= 0x94D049BB133111EBUL; h ^= h >> 31;
        return ((h >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53) - 0.5;
    }

    // head/tail cleanliness — a light nudge (the byte-drain guarantees actual balance); the per-unit reads live on
    // Couplings' derived unit table (HeadClean/TailClean — same predicates, no byte materialization).
    private (bool head, bool tail) HeadTail(List<uint> seq)
    {
        if (seq.Count == 0) return (false, false);
        return (_cp.HeadClean(seq[0]), _cp.TailClean(seq[^1]));
    }

    // expand the chunk block to bytes + the DETERMINISTIC brace-drain: append one '}' per unmatched '{'
    // (closing your braces is what a code generator does; the spec's deployable form-closer). Leading unmatched
    // '}' are dropped so the block opens clean. With `_lines` armed a '\n' is emitted between units whenever the
    // running line reaches the sampled learned length (so a long block is line-broken, never a KB-long run); the
    // block always ends with a '\n' (the utterance boundary — the legacy per-block newline). No model ⇒ only that
    // per-block '\n', byte-identical to the pre-LineModel callers.
    private void AppendExpanded(ref int outputLength, List<uint> seq, ref Lcg rng, ref int lineBytes, ref int target)
    {
        var body = _bodyScratch; body.Clear();
        foreach (var u in seq)
        {
            var e = _cp.Expand(u);
            _nExpandBytes += e.Length;
            if (_lines is not { } lm) { body.AddRange(e); continue; }
            // line-aware HARD CAP: no line may exceed the learned length. A normal unit fills the line and breaks
            // after; an OVERSIZED dream-unit (a composed chain longer than any real line — the runaway the barrier
            // would otherwise mint as a giant single-span rule) is SPLIT at line boundaries. Capping WITHIN the
            // unit (not just between units) is load-bearing: a between-units break lets one oversized unit overshoot
            // target, and the per-stride LineModel then RELEARNS that long line → targets creep → the maxSpan
            // feedback loop runs through the model itself (measured: 221→9603B). The hard cap keeps every minted
            // span ≤ target, so the tape never pollutes and the model self-stabilizes from the real-corpus seed.
            for (int off = 0; off < e.Length;)
            {
                int take = Math.Min(target - lineBytes, e.Length - off);
                body.AddRange(e.AsSpan(off, take));
                off += take; lineBytes += take;
                if (lineBytes >= target) { body.Add((byte)'\n'); lineBytes = 0; target = lm.Sample(ref rng); }
            }
        }
        int depth = 0, start = 0;
        for (int i = 0; i < body.Count; i++)
        {
            if (body[i] == (byte)'{') depth++;
            else if (body[i] == (byte)'}') { if (depth > 0) depth--; else if (i == start) start++; }   // drop a leading unmatched close
        }
        EnsureOutputCapacity(outputLength + body.Count - start + depth + 1);
        body.CopyTo(start, _outputScratch, outputLength, body.Count - start);
        outputLength += body.Count - start;
        for (int k = 0; k < depth; k++) _outputScratch[outputLength++] = (byte)'}'; // drain unmatched opens
        _outputScratch[outputLength++] = (byte)'\n';                                // block boundary — the coherent utterance ends a line
        lineBytes = 0;                                                              // …and the next block opens a fresh line
    }

    private void EnsureOutputCapacity(int required)
    {
        if (required <= _outputScratch.Length) return;
        int capacity = _outputScratch.Length;
        while (capacity < required) capacity = checked(capacity * 2);
        Array.Resize(ref _outputScratch, capacity);
    }

    // Draw a unit ∝ marginal mass. Byte-identical to the old O(V) linear scan (`first i with Σ_{j≤i} vw[j] > pick`),
    // but binary-searched over the prefix-sum CDF — this sampler is hit ~45k×/Generate (seed + NExplore per Gibbs
    // step), so the linear scan was O(V) × 45k/step = the measured GENERATE chug that GREW with the vocabulary.
    private uint SampleMarginal(Lcg rng)
    {
        long pick = (long)(rng.NextDouble() * _vwTot);
        int lo = 0, hi = _cumw.Length - 1;                          // smallest lo with _cumw[lo] > pick (= the linear scan's `pick < acc`)
        while (lo < hi) { int mid = (lo + hi) >> 1; if (_cumw[mid] > pick) hi = mid; else lo = mid + 1; }
        return _vocab[lo];
    }
}

/// The deterministic sampler RNG — the same LCG the other cogito generators walk (integer-only, seed-replayable).
public struct Lcg(ulong seed)
{
    private ulong _s = seed;
    public ulong Next() { _s = _s * 6364136223846793005UL + 1442695040888963407UL; return _s; }
    public double NextDouble() { Next(); return ((_s >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
}

/// The `--gen coupling` strategy — the driver-spec MEANING generator behind the IGenerator seam. Builds the
/// rich+robust couplings from the grammar's compressed sequence, then drives the combined-score MRF relaxation.
public sealed class CouplingWalk : IGenerator
{
    /// Shared instance remains for callers that want a process-wide strategy, while mesh neurons
    /// receive their own instance so the standing model is never invalidated by a peer grammar.
    public static readonly CouplingWalk Instance = new();
    private GrammarRule[]? _rules;
    private Symbol[]? _compressed;
    private Couplings? _cp;
    private Scorer? _rich;
    private Scorer? _robust;
    private CouplingGenerator? _generator;
    public string Name => "coupling";

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
            _cp = Couplings.Learn(grammar);
            _rich = _cp.BuildScorer(minCocount: 1);
            _robust = _cp.BuildScorer(minCocount: 5);
            _generator = new CouplingGenerator(_cp, _rich, _robust);
            _rules = grammar.Rules;
            _compressed = grammar.Compressed;
        }
        return _generator!.Generate(count, seed);
    }
}
