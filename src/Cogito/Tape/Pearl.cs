namespace Cogito;

using Cogito.Grammar;
using Cogito.Induct;
using Cogito.Observe;

// ── THE PEARL ──  the corroboration organ over the
// provenance-tagged tape: a replay becomes real only when OTHER jewels reflect it (Indra's net — never self-
// reflection). The mollusk may SPEAK from hypothesis but may not COUNT it as evidence: replay spans enter the
// count measure at weight 1 against evidence's wScale (Tape.WeightsFor → RePair's weighted counts), and REFLECTION
// is the de-correlation event that promotes them — a REAL span exercising a rule is the independent jewel that the
// rule is world-structure, not self-echo, so the replay spans supporting that rule reflect (their recurrences stop
// being autocorrelated samples of the mollusk's own state and start counting).
//
// Two verbs, one pass each:
//   Audit       — ONE O(compressed + rules) co-walk of the induction output against the tape's span units
//                 (the barrier law makes it a monotone cursor: no rule crosses a span, so every nonterminal
//                 occurrence sits in exactly one span), then ONE reverse-order DAG pass for nested-exercise
//                 credit (a span exercising a parent rule exercised its children — Re-Pair children have
//                 smaller indices, so high→low order propagates transitively in one sweep).
//   Corroborate — reflect the supporters of every real-reflected rule, in deterministic rule-index/span-id order,
//                 one journal line per TRANSITION (Tape.Reflect is monotonic+idempotent, so re-runs are silent).
//
// The audit also emits wuses — the weighted use counts MemoryHierarchy.Gc prices rent with (an echo-only rule's
// uses are worth 1/wScale of an exercised rule's, so replay-echo rules rank lowest-rent and demote first).

/// One audit's verdict over a grammar × tape pair, rule-indexed. `SawReal[r]`: an evidence span exercised rule r
/// (directly in the compressed sequence or nested under an exercised ancestor). `Supporters[r]`: the unreflected-Replay
/// span ids exercising r (sorted ascending — the deterministic reflect set when r is corroborated). `WUses[r]`: the
/// provenance-weighted use count (compressed occurrences by span weight + wScale per pattern reference — mirrors
/// Engine.RuleUses exactly at wScale=1). `ExpLen[r]`: expansion byte length by child recurrence (no materialization).
///
/// `JewelSources[r]`: the CROSS-REFLECTION axis (null unless the audit ran with crossReflect on) — the set of DISTINCT
/// source labels (the JEWELS of Indra's net) whose spans exercised rule r, from ANY provenance (evidence corpus AND
/// unreflected peer-node dreams alike). This is the source-set `SawReal` becomes under : corroboration is
/// the RANK of the jewel set, and a peer node's span is a generator-INDEPENDENT jewel the Real-only gate could not
/// see. `CrossReflect` records which gate Corroborate applies: OFF ⇒ the Real-only gate (SawReal), byte-identical to
/// the pre-fix mollusk; ON ⇒ a Replay reflects iff `JewelSources[r]` holds a source ≠ the supporter's OWN source (same-
/// source rejected).
///
/// `JewelCounts[r]`: the per-source MULTIPLICITY `JewelSources` collapses away — source label → occurrence count of
/// rule r's exercise by that source (co-walk fires + reverse-DAG nested credit, the same two passes and gate that fill
/// the set). This is the DAG-UNION count: a child inherits every parent's per-source count (the reverse-DAG pass adds
/// the parent's map into the child's), so `JewelCounts` mirrors `JewelSources`' breadth semantics exactly. BREADTH is
/// `|JewelSources|`. Filled iff crossReflect (null otherwise); consumed by nobody in the reflection hot path
/// (Corroborate reads only the SET) — pure instrumentation. Whorl B 6.2 (slot-aware reflection) GROWS THIS DICTIONARY
/// (a slot-keyed sub-count), never a parallel array — the struct grows once.
///
/// `JewelCountsDirect[r]`: the SAME per-source multiplicity WITHOUT the reverse-DAG union — the co-walk fires ONLY (a
/// rule's OWN top-level exercises in the compressed stream, never a count propagated down from a parent). This is the
/// DE-CONFOUNDED raw material for the SHARPNESS axis: the reverse-DAG union pours a parent's per-source distribution
/// into every child, so a child's `JewelCounts` peakedness is contaminated by its ancestors' — the concentration a
/// child appears to have is partly borrowed. `JewelCountsDirect` is a rule's own distribution over the sources that
/// DIRECTLY re-derived it, which is the distribution the breadth-normalized evenness (quadrant assay) must read. Same
/// fill site + gate as `JewelCounts`, snapshotted before the reverse-DAG touches it; also crossReflect-gated, also
/// pure instrumentation.
///
/// `SlotJewels[r]`: WHORL B 6.2 — the SLOT-AWARE reflection set (the DEPTH CURE), the SECOND behavioral tenant beside
/// the instrumentation counts. A deep rule's exact surface rarely recurs, so `JewelSources[r]` decays ~exponentially
/// with depth (vest_peer FOOLED at depth) — a peer never re-derives the LITERAL deep rule. The blur cure is variant
/// POOLING: `SlotJewels[r]` is the UNION of `JewelSources` over r's SLOT-MATES — the rules that instantiate the same
/// slot-PATTERN (anti-unify at one token position, over-merge-safe via mutual-kNN — Blur.DetectRuleSlots). A peer
/// exercising a slot-mate makes r real by exercising its slot-pattern, not its bytes. Null unless the audit ran with
/// `slotMates` (slots-OFF is the parity default — byte-identical to the pre-6.2 mollusk); it never touches
/// reconstruction (audit-side only, so byte-exactness holds), and Corroborate consults it in place of `JewelSources`.
public readonly struct PearlAudit(bool[] sawReal, long[][] supporters, long[] wuses, long[] expLen, GrammarRule[] rules, int wScale, HashSet<string>?[]? jewelSources = null, Dictionary<string, long>?[]? jewelCounts = null, Dictionary<string, long>?[]? jewelCountsDirect = null, bool crossReflect = false, HashSet<string>?[]? slotJewels = null, Symbol[]? compressed = null, long tapeBytes = 0, long nextEventID = 0,
    bool[]? directSawReal = null, long[][]? directSupporters = null, HashSet<string>?[]? directJewelSources = null, Dictionary<string, long>?[]? directJewelCounts = null,
    long[]? directRealCounts = null, Dictionary<long, int>?[]? directSupportCounts = null, TapeEventView[]? units = null, long[]? unitEnds = null, uint alphabet = 0)
{
    public readonly bool[] SawReal = sawReal;
    public readonly long[][] Supporters = supporters;
    public readonly long[] WUses = wuses;
    public readonly long[] ExpLen = expLen;
    public readonly GrammarRule[] Rules = rules;
    public readonly int WScale = wScale;
    public readonly HashSet<string>?[]? JewelSources = jewelSources;
    public readonly Dictionary<string, long>?[]? JewelCounts = jewelCounts;
    public readonly Dictionary<string, long>?[]? JewelCountsDirect = jewelCountsDirect;
    public readonly bool CrossReflect = crossReflect;
    public readonly HashSet<string>?[]? SlotJewels = slotJewels;   // Whorl B 6.2 — pooled slot-mate jewels (the depth cure); null = slots-OFF (byte-identical)
    internal readonly Symbol[]? Compressed = compressed;
    internal readonly long TapeBytes = tapeBytes;
    internal readonly long NextEventID = nextEventID;
    internal readonly bool[]? DirectSawReal = directSawReal;
    internal readonly long[][]? DirectSupporters = directSupporters;
    internal readonly HashSet<string>?[]? DirectJewelSources = directJewelSources;
    internal readonly Dictionary<string, long>?[]? DirectJewelCounts = directJewelCounts;
    internal readonly long[]? DirectRealCounts = directRealCounts;
    internal readonly Dictionary<long, int>?[]? DirectSupportCounts = directSupportCounts;
    internal readonly TapeEventView[]? Units = units;
    internal readonly long[]? UnitEnds = unitEnds;
    internal readonly uint Alphabet = alphabet;

    /// The rule's stable name — "r" + FNV64 of its expansion. Computed ON DEMAND: only rules whose supporters
    /// actually reflect are ever named (Corroborate's journal line, a small fraction of n), so materializing all N
    /// up front expanded + hashed mostly-discarded rules. A pure function of the rule's expansion — byte-identical
    /// to the eager name it replaces.
    public string NameOf(int r)
        => "r" + Simhash.Fnv64(Reconstruct.Expand(Rules, [new Symbol(Symbol.FirstNonterminal + (uint)r)])).ToString("x16")[..12];
}

/// Reuse an audit while both its tape revision and grammar identity are unchanged. Corroboration is only meaningful
/// after one of those inputs moves; retaining the full audit also avoids rebuilding its view list and reverse-DAG maps.
public sealed class PearlAuditCache
{
    private Tape? _tape;
    private long _tapeRevision = -1;
    private TapeRevision _tapeNonAppendRevision = new(-1);
    private GrammarRule[]? _rules;
    private Symbol[]? _compressed;
    private int _wScale;
    private bool _crossReflect;
    private int[]?[]? _slotMates;
    private PearlAudit _audit;

    public bool Rebuilt { get; private set; }
    public int FullRebuilds { get; private set; }
    public int DeltaRebuilds { get; private set; }
    public int ReflectedReprices { get; private set; }

    /// Fold a REFLECTED-ONLY tape mutation (Corroborate's replay→evidence transitions) into the cached
    /// audit in place, then adopt the tape's post-reflect revisions — so the reflection no longer reads
    /// as a foreign non-append mutation and the next Get is a cache hit (or an append-only delta) instead
    /// of a guaranteed full O(view) rebuild. Mirrors Loom.RepriceReflected: the mutation's exact event set
    /// is handed over by the caller, never re-inferred. No-op when the cache does not hold this tape's
    /// audit in delta-capable form (the next Get then rebuilds exactly as before).
    public void RepriceReflected(Tape tape, ReadOnlySpan<TapeEventID> reflected)
    {
        if (reflected.Length == 0 || !ReferenceEquals(_tape, tape)
            || _audit.Compressed is null || _audit.Units is null || _audit.UnitEnds is null
            || _audit.DirectRealCounts is null || _audit.DirectSupportCounts is null) return;
        _audit = Pearl.RepriceReflected(in _audit, reflected);
        _tapeRevision = tape.Revision.Value;
        _tapeNonAppendRevision = tape.NonAppendRevision;
        ReflectedReprices++;
    }

    public PearlAudit Get(Tape tape, in RePairResult grammar, int wScale, bool crossReflect = false, int[]?[]? slotMates = null)
    {
        Rebuilt = !ReferenceEquals(_tape, tape)
            || _tapeRevision != tape.Revision.Value
            || !ReferenceEquals(_rules, grammar.Rules)
            || !ReferenceEquals(_compressed, grammar.Compressed)
            || _wScale != wScale
            || _crossReflect != crossReflect
            || !ReferenceEquals(_slotMates, slotMates);
        if (Rebuilt)
        {
            bool canDelta = _audit.Compressed is not null
                && grammar.Rules.Length >= _audit.Rules.Length
                && Pearl.RulesPrefixMatches(_audit.Rules, grammar.Rules)
                && tape.GrammarByteLength >= _audit.TapeBytes
                && tape.NextId >= _audit.NextEventID
                && tape.NonAppendRevision == _tapeNonAppendRevision
                && _crossReflect == crossReflect
                && ReferenceEquals(_slotMates, slotMates);
            if (canDelta)
            {
                _audit = Pearl.AuditAppended(in _audit, tape, in grammar, wScale, crossReflect, slotMates);
                DeltaRebuilds++;
            }
            else
            {
                _audit = Pearl.Audit(tape, in grammar, wScale, crossReflect, slotMates);
                FullRebuilds++;
            }
            _tape = tape;
            _tapeRevision = tape.Revision.Value;
            _tapeNonAppendRevision = tape.NonAppendRevision;
            _rules = grammar.Rules;
            _compressed = grammar.Compressed;
            _wScale = wScale;
            _crossReflect = crossReflect;
            _slotMates = slotMates;
        }
        return _audit;
    }
}

public static class Pearl
{
    /// A rule must span at least this many expansion bytes for an occurrence to count as EXERCISE (sawReal /
    /// supporter attribution). Below it a rule is a noise morpheme ("th", "e ") that any span trivially contains —
    /// reflecting on morpheme co-occurrence would let every replay reflect through the alphabet's plumbing.
    public const int ReflectFloorBytes = 8;

    /// ONE co-walk of the induction output vs THE GRAMMAR VIEW (residents in tape order, shed spans in id order —
    /// the same enumeration the grammar intake paces, so shedding never desyncs the cursor) + ONE reverse DAG pass.
    /// REQUIRES the grammar was induced over THIS tape's grammar intake view (the walk re-derives every unit boundary and fails loud on
    /// desync). O(compressed + rules) time; supporter sets are bounded by the tape's unreflected-replay census.
    ///
    /// `crossReflect` OFF (default) is byte-identical to the pre-fix Real-only gate — `JewelSources`/`JewelCounts`
    /// stay null, nothing extra is walked. ON adds ONE source-set + ONE per-source count map per rule threaded
    /// through the SAME co-walk + reverse-DAG (zero new passes, Monk 1's steer): every unit exercising a rule
    /// contributes its source, so Corroborate can reflect a Replay on a DIFFERENT source — a peer node's span is the
    /// generator-independent jewel node0's own echoes are not. The COUNT map is pure instrumentation (Corroborate
    /// reads only the set); it carries the multiplicity the set discards — the sharpness axis's raw material.
    /// Sources are recorded from ALL provenances (a peer replay is as valid a jewel as corpus).
    ///
    /// `slotMates` (Whorl B 6.2 — the DEPTH CURE, slots-OFF/null is the parity default) is the per-rule slot-class:
    /// `slotMates[r]` = the rule indices that instantiate r's slot-PATTERN (Blur.DetectRuleSlots — anti-unify at one
    /// token position, over-merge-safe). When present, a post-pass pools `JewelSources` across each rule's mates into
    /// `SlotJewels`, so a deep rule reflects when a peer exercises a slot-MATE (its pattern) though the literal rule
    /// never recurs. Audit-side ONLY — reconstruction never consults slots, so byte-exactness holds; null ⇒ byte-
    /// identical to the pre-6.2 audit (nothing extra walked).
    public static PearlAudit Audit(Tape tape, in RePairResult g, int wScale, bool crossReflect = false, int[]?[]? slotMates = null)
    {
        Tape.RequireWScale(wScale);
        int n = g.Rules.Length;
        int alpha = (int)g.AlphabetSize;
        var expLen = Engine.ExpLens(g.Rules, g.AlphabetSize);   // child recurrence, zero materialization — the shared verb (slot → representative, the Reconstruct read)

        // ── THE CO-WALK — monotone cursor over (compressed symbols × VIEW units). Every nonterminal is '\n'-free
        // (barrier law), so it sits wholly inside the unit the cursor is on when it starts.
        var sawReal = new bool[n];
        var supp = new HashSet<long>?[n];
        var jewelSrc = crossReflect ? new HashSet<string>?[n] : null;   // per-rule DISTINCT source set — the cross-reflection axis (null = OFF, no extra work)
        var jewelCnt = crossReflect ? new Dictionary<string, long>?[n] : null;   // per-rule per-source COUNT, DAG-UNION (co-walk + reverse-DAG credit) — mirrors JewelSources' breadth
        var jewelCntDir = crossReflect ? new Dictionary<string, long>?[n] : null;   // the SAME count, co-walk ONLY (no reverse-DAG) — the DE-CONFOUNDED distribution the sharpness axis reads
        var directRealCounts = new long[n];
        var directSupportCounts = new Dictionary<long, int>?[n];
        var wuses = new long[n];
        var units = new List<TapeEventView>(tape.Count + tape.ShedEventIDs.Count);
        foreach (var u in tape.GetGrammarEventViews()) units.Add(u);
        long pos = 0;
        int si = 0;
        long unitEnd = units.Count > 0 ? units[0].Len + 1 : 0;
        foreach (var s in g.Compressed)
        {
            while (pos >= unitEnd && si + 1 < units.Count) { si++; unitEnd += units[si].Len + 1; }
            long len;
            if (s.Value < (uint)alpha) len = 1;
            else
            {
                int r = (int)(s.Value - alpha);
                if (r >= n) throw new InvalidOperationException($"Pearl.Audit: compressed symbol {s.Value} names rule {r} of {n}");
                len = expLen[r];
                bool ev = units[si].Evidence;
                Provenances provenance = units[si].Provenance;
                wuses[r] += ev ? wScale : 1;
                if (expLen[r] >= ReflectFloorBytes)
                {
                    if (ev) { sawReal[r] = true; directRealCounts[r]++; }
                    else if (provenance == Provenances.Replay)
                    {
                        (supp[r] ??= new()).Add(units[si].Id.Value);     // only a hypothesis awaits corroboration; neutral execution history is never promoted
                        Dictionary<long, int> supportCounts = directSupportCounts[r] ??= new();
                        supportCounts[units[si].Id.Value] = supportCounts.GetValueOrDefault(units[si].Id.Value) + 1;
                    }
                    if (jewelSrc is not null) (jewelSrc[r] ??= new()).Add(units[si].Source);   // cross-reflection: EVERY exercising unit's source (evidence or replay) — the peer-jewel set
                    if (jewelCnt is not null)   // the same fire, kept per-source (multiplicity, not just presence) — into BOTH the DAG-union count and the direct-only count
                    {
                        string src = units[si].Source;
                        var d = jewelCnt[r] ??= new(); d[src] = d.GetValueOrDefault(src) + 1;
                        var dd = jewelCntDir![r] ??= new(); dd[src] = dd.GetValueOrDefault(src) + 1;   // direct snapshot — the reverse-DAG below never touches jewelCntDir
                    }
                }
            }
            pos += len;
        }
        if (pos != tape.GrammarByteLength)
            throw new InvalidOperationException($"Pearl.Audit desync: compressed expands to {pos}B, tape grammar view is {tape.GrammarByteLength}B — the grammar was not induced over the grammar intake view");

        // ── pattern references — Engine.RuleUses' second tally, weighted: a reference from PAID structure is
        // composition (full weight), never echo. wScale=1 makes WUses == RuleUses element-wise (the degeneracy).
        foreach (var rule in g.Rules)
            foreach (var sym in rule.Pattern)
                if (sym.Value >= (uint)alpha && (int)(sym.Value - alpha) < n) wuses[(int)(sym.Value - alpha)] += wScale;

        bool[] directSawReal = (bool[])sawReal.Clone();
        long[][] directSupporters = new long[n][];
        for (int r = 0; r < n; r++)
            directSupporters[r] = supp[r] is { Count: > 0 } hs ? hs.OrderBy(x => x).ToArray() : [];
        HashSet<string>?[]? directJewelSources = CloneSets(jewelSrc, n);
        Dictionary<string, long>?[]? directJewelCounts = CloneMaps(jewelCntDir, n);

        // ── THE REVERSE DAG PASS — nested-exercise credit, parent → child in one high→low sweep (a parent's final
        // verdict is settled before any of its children is visited, so credit propagates transitively). Slots are
        // SKIPPED: pick-one semantics can't attribute an exercise to a specific member from the count alone, and
        // fabricating corroboration is the exact sin the law forbids (fail closed).
        for (int r = n - 1; r >= 0; r--)
        {
            if ((!sawReal[r] && supp[r] is null && jewelSrc?[r] is null) || g.Rules[r].IsSlot) continue;
            foreach (var sym in g.Rules[r].Pattern)
            {
                if (sym.Value < (uint)alpha) continue;
                int c = (int)(sym.Value - alpha);
                if (c >= r || expLen[c] < ReflectFloorBytes) continue;
                if (sawReal[r]) sawReal[c] = true;
                if (supp[r] is { Count: > 0 } sr) (supp[c] ??= new()).UnionWith(sr);
                if (jewelSrc?[r] is { Count: > 0 } wr) (jewelSrc[c] ??= new()).UnionWith(wr);   // a parent's jewels are the child's (the exercising span exercised both — same transitivity as sawReal)
                if (jewelCnt?[r] is { Count: > 0 } wc) { var d = jewelCnt[c] ??= new(); foreach (var (src, cnt) in wc) d[src] = d.GetValueOrDefault(src) + cnt; }   // the count transitivity: the child fired every time the parent did (the union's arithmetic twin)
            }
        }

        // materialize sorted supporter arrays — the deterministic corroboration order Corroborate walks.
        var supporters = new long[n][];
        for (int i = 0; i < n; i++)
        {
            if (supp[i] is { Count: > 0 } hs) { var a = new long[hs.Count]; hs.CopyTo(a); Array.Sort(a); supporters[i] = a; }
            else supporters[i] = [];
        }

        // ── WHORL B 6.2 · THE SLOT POOL (the depth cure) — union each rule's jewel sources across its SLOT-MATES.
        // A deep rule's literal surface rarely recurs (JewelSources decays with depth), but a peer exercising a
        // slot-MATE exercises the same PATTERN; pooling makes that count. Post-pass, gated on slotMates≠null AND the
        // cross-reflection axis being live (jewelSrc). Audit-side only; reconstruction never sees it. Slots include
        // the rule itself (Blur.DetectRuleSlots), so SlotJewels[r] ⊇ JewelSources[r] — never LOSES a reflection.
        HashSet<string>?[]? slotJewels = null;
        if (slotMates is not null && jewelSrc is not null)
        {
            slotJewels = new HashSet<string>?[n];
            for (int r = 0; r < n; r++)
            {
                var mates = slotMates[r];
                if (mates is null) continue;
                HashSet<string>? pooled = null;
                foreach (int m in mates)
                    if ((uint)m < (uint)n && jewelSrc[m] is { Count: > 0 } ms) (pooled ??= new()).UnionWith(ms);
                slotJewels[r] = pooled;
            }
        }
        long[] unitEnds = new long[units.Count];
        long unitCursor = 0;
        for (int i = 0; i < units.Count; i++) unitEnds[i] = unitCursor += units[i].Len + 1;
        return new PearlAudit(sawReal, supporters, wuses, expLen, g.Rules, wScale, jewelSrc, jewelCnt, jewelCntDir, crossReflect, slotJewels,
            g.Compressed, tape.GrammarByteLength, tape.NextId, directSawReal, directSupporters, directJewelSources, directJewelCounts,
            directRealCounts, directSupportCounts, units.ToArray(), unitEnds, g.AlphabetSize);
    }

    /// Extend an audit across an append-only tape/grammar tail. Mesh execution corroborationes append
    /// spans between armed steps while the standing loom preserves the prior compressed prefix;
    /// only the new symbols are co-walked here. Rule-DAG propagation remains bounded by the rule
    /// set, while the tape walk falls from O(total view) to O(delta view).
    internal static PearlAudit AuditAppended(in PearlAudit previous, Tape tape, in RePairResult grammar,
        int wScale, bool crossReflect, int[]?[]? slotMates)
    {
        PearlAudit prior = previous;
        if (previous.Compressed is null || previous.Units is null || previous.UnitEnds is null
            || previous.DirectRealCounts is null || previous.DirectSupportCounts is null
            || previous.Rules.Length > grammar.Rules.Length || !RulesPrefixMatches(previous.Rules, grammar.Rules))
            return Audit(tape, in grammar, wScale, crossReflect, slotMates);

        int oldN = previous.Rules.Length, n = grammar.Rules.Length, alpha = (int)grammar.AlphabetSize;
        Symbol[] oldCompressed = previous.Compressed;
        Symbol[] newCompressed = grammar.Compressed;
        int prefix = 0, common = Math.Min(oldCompressed.Length, newCompressed.Length);
        while (prefix < common && oldCompressed[prefix].Value == newCompressed[prefix].Value) prefix++;
        int suffix = 0;
        while (suffix < oldCompressed.Length - prefix && suffix < newCompressed.Length - prefix
            && oldCompressed[oldCompressed.Length - 1 - suffix].Value == newCompressed[newCompressed.Length - 1 - suffix].Value) suffix++;

        long[] oldExpLen = previous.ExpLen;
        long[] expLen = ExpandLengths(oldExpLen, grammar.Rules, grammar.AlphabetSize);
        long PrefixBytes(Symbol[] symbols, GrammarRule[] rules, long[] lengths, int count)
        {
            long total = 0;
            for (int i = 0; i < count; i++) total += symbols[i].Value >= (uint)alpha && symbols[i].Value - alpha < (uint)lengths.Length ? lengths[symbols[i].Value - alpha] : 1;
            return total;
        }
        long oldPrefixBytes = PrefixBytes(oldCompressed, previous.Rules, oldExpLen, prefix);
        long newPrefixBytes = PrefixBytes(newCompressed, grammar.Rules, expLen, prefix);
        long oldChangedEnd = PrefixBytes(oldCompressed, previous.Rules, oldExpLen, oldCompressed.Length - suffix);
        long newChangedEnd = PrefixBytes(newCompressed, grammar.Rules, expLen, newCompressed.Length - suffix);
        if (oldPrefixBytes != newPrefixBytes || oldChangedEnd > previous.TapeBytes || newChangedEnd > tape.GrammarByteLength)
            return Audit(tape, in grammar, wScale, crossReflect, slotMates);

        TapeEventView[] appendedUnits = tape.EnumerateAppendedSince(prior.NextEventID, TapeEventRoles.GrammarInput).ToArray();
        long[] appendedEnds = new long[appendedUnits.Length];
        long appendedCursor = prior.TapeBytes;
        for (int i = 0; i < appendedUnits.Length; i++) appendedEnds[i] = appendedCursor += appendedUnits[i].Len + 1;

        TapeEventView ResolveUnit(long offset)
        {
            if (offset < prior.TapeBytes)
            {
                int lo = 0, hi = prior.UnitEnds!.Length - 1;
                while (lo < hi) { int mid = lo + ((hi - lo) >> 1); if (offset < prior.UnitEnds[mid]) hi = mid; else lo = mid + 1; }
                return prior.Units![lo];
            }
            if (appendedUnits.Length == 0) throw new InvalidOperationException("Pearl delta has no appended unit for changed bytes");
            int left = 0, right = appendedEnds.Length - 1;
            while (left < right) { int mid = left + ((right - left) >> 1); if (offset < appendedEnds[mid]) right = mid; else left = mid + 1; }
            return appendedUnits[left];
        }

        long[] directRealCounts = (long[])previous.DirectRealCounts.Clone();
        Array.Resize(ref directRealCounts, n);
        Dictionary<long, int>?[] directSupportCounts = CloneSupportCounts(previous.DirectSupportCounts, n);
        Dictionary<string, long>?[]? directJewelCounts = CloneMaps(previous.DirectJewelCounts ?? previous.JewelCountsDirect, n);
        long[] wuses = (long[])previous.WUses.Clone();
        Array.Resize(ref wuses, n);

        void Adjust(Symbol symbol, GrammarRule[] rules, long[] lengths, long offset, TapeEventView view, int sign)
        {
            if (symbol.Value < (uint)alpha) return;
            int r = (int)(symbol.Value - alpha);
            if ((uint)r >= (uint)rules.Length) throw new InvalidOperationException($"Pearl delta symbol {symbol.Value} names rule {r} of {rules.Length}");
            wuses[r] += sign * (view.Evidence ? wScale : 1);
            if (lengths[r] < ReflectFloorBytes) return;
            if (view.Evidence) directRealCounts[r] += sign;
            else if (view.Provenance == Provenances.Replay)
            {
                Dictionary<long, int> map = directSupportCounts[r] ??= new();
                int next = map.GetValueOrDefault(view.Id.Value) + sign;
                if (next <= 0) map.Remove(view.Id.Value); else map[view.Id.Value] = next;
                if (map.Count == 0) directSupportCounts[r] = null;
            }
            if (directJewelCounts is not null)
            {
                Dictionary<string, long> map = directJewelCounts[r] ??= new();
                long next = map.GetValueOrDefault(view.Source) + sign;
                if (next <= 0) map.Remove(view.Source); else map[view.Source] = next;
                if (map.Count == 0) directJewelCounts[r] = null;
            }
        }

        long oldOffset = oldPrefixBytes;
        for (int i = prefix; i < oldCompressed.Length - suffix; i++)
        {
            Symbol symbol = oldCompressed[i];
            TapeEventView view = ResolveUnit(oldOffset);
            Adjust(symbol, previous.Rules, oldExpLen, oldOffset, view, -1);
            oldOffset += symbol.Value >= (uint)alpha && symbol.Value - alpha < (uint)oldExpLen.Length ? oldExpLen[symbol.Value - alpha] : 1;
        }
        long newOffset = newPrefixBytes;
        for (int i = prefix; i < newCompressed.Length - suffix; i++)
        {
            Symbol symbol = newCompressed[i];
            TapeEventView view = ResolveUnit(newOffset);
            Adjust(symbol, grammar.Rules, expLen, newOffset, view, +1);
            newOffset += symbol.Value >= (uint)alpha && symbol.Value - alpha < (uint)expLen.Length ? expLen[symbol.Value - alpha] : 1;
        }
        for (int r = oldN; r < n; r++)
            foreach (Symbol symbol in grammar.Rules[r].Pattern)
                if (symbol.Value >= (uint)alpha && symbol.Value - alpha < (uint)n) wuses[symbol.Value - alpha] += wScale;

        bool[] directSawReal = new bool[n];
        var supportSets = new HashSet<long>?[n];
        long[][] directSupporters = new long[n][];
        for (int r = 0; r < n; r++)
        {
            directSawReal[r] = directRealCounts[r] > 0;
            if (directSupportCounts[r] is { Count: > 0 } map)
            {
                supportSets[r] = new HashSet<long>(map.Keys);
                directSupporters[r] = map.Keys.OrderBy(x => x).ToArray();
            }
            else directSupporters[r] = [];
        }
        HashSet<string>?[]? directJewelSources = BuildSourceSets(directJewelCounts, n);
        bool[] sawReal = (bool[])directSawReal.Clone();
        HashSet<string>?[]? jewels = CloneSets(directJewelSources, n);
        Dictionary<string, long>?[]? counts = CloneMaps(directJewelCounts, n);
        for (int r = n - 1; r >= 0; r--)
        {
            if ((!sawReal[r] && supportSets[r] is null && jewels?[r] is null) || grammar.Rules[r].IsSlot) continue;
            foreach (Symbol symbol in grammar.Rules[r].Pattern)
            {
                if (symbol.Value < (uint)alpha) continue;
                int child = (int)(symbol.Value - alpha);
                if (child >= r || expLen[child] < ReflectFloorBytes) continue;
                if (sawReal[r]) sawReal[child] = true;
                if (supportSets[r] is { Count: > 0 } source) (supportSets[child] ??= new()).UnionWith(source);
                if (jewels?[r] is { Count: > 0 } src) (jewels[child] ??= new()).UnionWith(src);
                if (counts?[r] is { Count: > 0 } csrc)
                {
                    Dictionary<string, long> target = counts[child] ??= new();
                    foreach ((string sourceName, long amount) in csrc) target[sourceName] = target.GetValueOrDefault(sourceName) + amount;
                }
            }
        }
        long[][] supporters = new long[n][];
        for (int r = 0; r < n; r++) supporters[r] = supportSets[r] is { Count: > 0 } set ? set.OrderBy(x => x).ToArray() : [];
        HashSet<string>?[]? slotJewels = null;
        if (slotMates is not null && jewels is not null)
        {
            slotJewels = new HashSet<string>?[n];
            for (int r = 0; r < n; r++) if (slotMates[r] is { } mates)
            {
                HashSet<string>? pooled = null;
                foreach (int mate in mates) if ((uint)mate < (uint)n && jewels[mate] is { Count: > 0 } values) (pooled ??= new()).UnionWith(values);
                slotJewels[r] = pooled;
            }
        }
        // append-only ⇒ the prior UnitEnds prefix is byte-identical: reuse it wholesale (aliasing prior's
        // arrays when nothing appended, same as Units) and prefix-sum only the Δ tail.
        TapeEventView[] currentUnits = prior.Units!;
        long[] currentUnitEnds = prior.UnitEnds!;
        if (appendedUnits.Length > 0)
        {
            int priorCount = prior.Units!.Length;
            currentUnits = new TapeEventView[priorCount + appendedUnits.Length];
            Array.Copy(prior.Units, currentUnits, priorCount);
            Array.Copy(appendedUnits, 0, currentUnits, priorCount, appendedUnits.Length);
            currentUnitEnds = new long[currentUnits.Length];
            Array.Copy(prior.UnitEnds!, currentUnitEnds, priorCount);
            long currentCursor = priorCount > 0 ? prior.UnitEnds![priorCount - 1] : 0;
            for (int i = 0; i < appendedUnits.Length; i++) currentUnitEnds[priorCount + i] = currentCursor += appendedUnits[i].Len + 1;
        }
        return new PearlAudit(sawReal, supporters, wuses, expLen, grammar.Rules, wScale, jewels, counts, directJewelCounts, crossReflect, slotJewels,
            grammar.Compressed, tape.GrammarByteLength, tape.NextId, directSawReal, directSupporters, directJewelSources, directJewelCounts,
            directRealCounts, directSupportCounts, currentUnits, currentUnitEnds, grammar.AlphabetSize);
    }

    /// Reprice an audit across a REFLECTED-ONLY tape mutation. Reflection flips each reflected unit's
    /// evidence bit and nothing else — bytes, order, sources, and the grammar are untouched — so the exact
    /// audit delta is: per occurrence of a rule inside a reflected unit, the weighted use moves 1→wScale,
    /// the rule's direct real count gains the occurrence, and the unit leaves the rule's supporter map.
    /// Every jewel plane (JewelSources/JewelCounts/JewelCountsDirect/SlotJewels) is filled provenance-blind
    /// in the full audit, so those arrays are reused untouched; the sawReal/supporter planes re-derive
    /// through the same reverse-DAG pass the full audit runs. One monotone walk over the RETAINED
    /// compressed×units snapshot — no view enumeration, no tape resolve, no map clones. The retained unit
    /// snapshots are patched to Evidence so a later append-only delta audit adjusts with the same weights
    /// a fresh audit would. Equivalence to the full rebuild is gated by verify-induct's kill-line.
    internal static PearlAudit RepriceReflected(in PearlAudit previous, ReadOnlySpan<TapeEventID> reflected)
    {
        int n = previous.Rules.Length;
        uint alpha = previous.Alphabet;
        var reflectedIDs = new HashSet<long>();
        foreach (TapeEventID id in reflected) reflectedIDs.Add(id.Value);
        TapeEventView[] units = previous.Units!;
        long[] unitEnds = previous.UnitEnds!;
        long[] expLen = previous.ExpLen;
        long[] wuses = previous.WUses;                                   // adjusted in place — the cache owns the audit
        long[] directRealCounts = previous.DirectRealCounts!;
        Dictionary<long, int>?[] directSupportCounts = previous.DirectSupportCounts!;
        for (int i = 0; i < units.Length; i++)
            if (reflectedIDs.Contains(units[i].Id.Value))
                units[i] = units[i] with { Evidence = true };
        long pos = 0;
        int si = 0;
        long unitEnd = units.Length > 0 ? unitEnds[0] : 0;
        bool inReflected = units.Length > 0 && reflectedIDs.Contains(units[0].Id.Value);
        foreach (Symbol s in previous.Compressed!)
        {
            while (pos >= unitEnd && si + 1 < units.Length)
            {
                si++;
                unitEnd = unitEnds[si];
                inReflected = reflectedIDs.Contains(units[si].Id.Value);
            }
            if (s.Value < alpha) { pos++; continue; }
            int r = (int)(s.Value - alpha);
            pos += expLen[r];
            if (!inReflected) continue;
            wuses[r] += previous.WScale - 1;
            if (expLen[r] < ReflectFloorBytes) continue;
            directRealCounts[r]++;
            if (directSupportCounts[r] is { } map && map.Remove(units[si].Id.Value) && map.Count == 0)
                directSupportCounts[r] = null;
        }
        if (pos != previous.TapeBytes)
            throw new InvalidOperationException($"Pearl reprice desync: compressed expands to {pos}B, audited view is {previous.TapeBytes}B");

        bool[] directSawReal = new bool[n];
        var supportSets = new HashSet<long>?[n];
        long[][] directSupporters = new long[n][];
        for (int r = 0; r < n; r++)
        {
            directSawReal[r] = directRealCounts[r] > 0;
            if (directSupportCounts[r] is { Count: > 0 } map)
            {
                supportSets[r] = new HashSet<long>(map.Keys);
                directSupporters[r] = map.Keys.OrderBy(x => x).ToArray();
            }
            else directSupporters[r] = [];
        }
        bool[] sawReal = (bool[])directSawReal.Clone();
        for (int r = n - 1; r >= 0; r--)
        {
            if ((!sawReal[r] && supportSets[r] is null && previous.JewelSources?[r] is null) || previous.Rules[r].IsSlot) continue;
            foreach (Symbol symbol in previous.Rules[r].Pattern)
            {
                if (symbol.Value < alpha) continue;
                int child = (int)(symbol.Value - alpha);
                if (child >= r || expLen[child] < ReflectFloorBytes) continue;
                if (sawReal[r]) sawReal[child] = true;
                if (supportSets[r] is { Count: > 0 } source) (supportSets[child] ??= new()).UnionWith(source);
            }
        }
        long[][] supporters = new long[n][];
        for (int r = 0; r < n; r++) supporters[r] = supportSets[r] is { Count: > 0 } set ? set.OrderBy(x => x).ToArray() : [];
        return new PearlAudit(sawReal, supporters, wuses, expLen, previous.Rules, previous.WScale,
            previous.JewelSources, previous.JewelCounts, previous.JewelCountsDirect, previous.CrossReflect, previous.SlotJewels,
            previous.Compressed, previous.TapeBytes, previous.NextEventID, directSawReal, directSupporters,
            previous.DirectJewelSources, previous.DirectJewelCounts, directRealCounts, directSupportCounts, units, unitEnds, alpha);
    }

    private static HashSet<string>?[]? CloneSets(HashSet<string>?[]? source, int n)
    {
        if (source is null) return null;
        var clone = new HashSet<string>?[n];
        for (int i = 0; i < Math.Min(source.Length, n); i++) if (source[i] is { } set) clone[i] = new HashSet<string>(set);
        return clone;
    }

    private static Dictionary<string, long>?[]? CloneMaps(Dictionary<string, long>?[]? source, int n)
    {
        if (source is null) return null;
        var clone = new Dictionary<string, long>?[n];
        for (int i = 0; i < Math.Min(source.Length, n); i++) if (source[i] is { } map) clone[i] = new Dictionary<string, long>(map);
        return clone;
    }

    private static Dictionary<long, int>?[] CloneSupportCounts(Dictionary<long, int>?[] source, int n)
    {
        var clone = new Dictionary<long, int>?[n];
        for (int i = 0; i < Math.Min(source.Length, n); i++) if (source[i] is { } map) clone[i] = new Dictionary<long, int>(map);
        return clone;
    }

    private static HashSet<string>?[]? BuildSourceSets(Dictionary<string, long>?[]? source, int n)
    {
        if (source is null) return null;
        var sets = new HashSet<string>?[n];
        for (int i = 0; i < Math.Min(source.Length, n); i++)
            if (source[i] is { Count: > 0 } map) sets[i] = new HashSet<string>(map.Keys);
        return sets;
    }

    internal static bool RulesPrefixMatches(GrammarRule[] previous, GrammarRule[] current)
    {
        if (previous.Length > current.Length) return false;
        for (int i = 0; i < previous.Length; i++)
        {
            GrammarRule left = previous[i], right = current[i];
            if (!left.Id.Equals(right.Id) || !left.Cost.Equals(right.Cost) || left.Kind != right.Kind
                || !left.Pattern.AsSpan().SequenceEqual(right.Pattern)) return false;
        }
        return true;
    }

    private static long[] ExpandLengths(long[] previous, GrammarRule[] rules, uint alphabet)
    {
        if (previous.Length > rules.Length) return Engine.ExpLens(rules, alphabet);
        long[] lengths = new long[rules.Length];
        Array.Copy(previous, lengths, previous.Length);
        for (int r = previous.Length; r < rules.Length; r++)
        {
            long length = 0;
            foreach (Symbol symbol in rules[r].Pattern)
            {
                uint value = symbol.Value;
                length += value >= alphabet && value - alphabet < (uint)r ? lengths[value - alphabet] : 1;
            }
            lengths[r] = length;
        }
        return lengths;
    }

    /// THE CORROBORATION EVENT — reflect the supporters a JEWEL exercised. Deterministic order (rule index ascending,
    /// span ids ascending within); a span supporting several corroborated rules reflects on the FIRST and is silent
    /// after (Tape.Reflect's idempotence), so the journal carries exactly one `vest` line per transition. Returns the
    /// number of spans that transitioned replay → evidence this call.
    ///
    /// The reflection gate is `audit.CrossReflect`:
    ///   OFF — the Real-only gate (SawReal): reflect a rule's Replay supporters iff a REAL span exercised it. Byte-
    ///         identical to the pre-fix mollusk; node0's dreams starve when the corpus drains (the sealed-loop bug).
    ///   ON  — the SOURCE-INDEPENDENCE gate: reflect a Replay supporter iff the rule's jewel-source
    ///         set holds a source ≠ the supporter's OWN source. A peer node's span (source≠node0) is a generator-
    ///         independent jewel, so node0's dreams keep reflecting off the mesh even when the Real corpus is dry;
    ///         a same-source jewel (self / a clone minting the identical source label) is REJECTED — no self-
    ///         reflection. `SawReal` is unused in this arm (a Real corpus span carries source="corpus", already a
    ///         different source from any node — the Real gate is the source-independence gate's corpus special case).
    /// `reflectedOut`, when supplied, collects the exact transitioned span ids — the mutation set
    /// PearlAuditCache.RepriceReflected (and Loom's reflected reprice via DrainDelta) keys on.
    public static int Corroborate(in PearlAudit audit, Tape tape, Journal journal, int step, List<TapeEventID>? reflectedOut = null)
    {
        int reflected = 0;
        for (int r = 0; r < audit.SawReal.Length; r++)
        {
            // Whorl B 6.2: the SLOT-POOLED jewel set stands in for the rule's own when slots are armed — a deep rule
            // reflects off a peer's exercise of its slot-PATTERN. SlotJewels[r] ⊇ JewelSources[r], and the whole
            // array is null when slots-OFF (the `?? JewelSources` fallback is then byte-identical to the pre-6.2 gate).
            HashSet<string>? jewels = audit.CrossReflect ? (audit.SlotJewels?[r] ?? audit.JewelSources?[r]) : null;
            if (audit.CrossReflect ? jewels is null : !audit.SawReal[r]) continue;
            string? name = null;   // computed at most once per rule, and only when a supporter actually transitions
            foreach (long sid in audit.Supporters[r])
            {
                var span = new TapeEventID(sid);
                if (audit.CrossReflect && !HasOtherSource(jewels!, tape.SourceOf(span))) continue;   // independence guard: a DIFFERENT source must have exercised the rule
                if (tape.Reflect(span)) { journal.Reflect(step, span, name ??= audit.NameOf(r)); reflected++; reflectedOut?.Add(span); }
            }
        }
        return reflected;
    }

    /// Does the jewel-source set hold ANY source other than `self`? — the source-independence predicate. A set of
    /// {self} alone is pure self-reflection (a rule exercised ONLY by node0's own spans) and reflects nothing; a set
    /// containing "corpus" or any peer node's tag corroborates. O(set) with an early out on the first foreign source.
    private static bool HasOtherSource(HashSet<string> jewels, string self)
    {
        foreach (var w in jewels) if (w != self) return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE KILL-LINE BATTERY — `verify-induct --weighted` calls this (the machinery's own gate, CLI-independent).
    //  Sections: WeightsFor correctness · PairDelta degeneracy/exactness · the 3·wScale mint gate · weighted
    //  linear-vs-reference oracle (mixed per-span weights) · Audit attribution + Corroborate on a synthetic
    //  real/replay grammar. All-integer, deterministic, zero tolerance — every check is an identity, not a bound.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    public static int RunKillLine()
    {
        int fails = 0;
        void Check(bool ok, string name, string detail)
        {
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "✓" : "✗ FAIL")}  {name,-26} {detail}");
        }
        Console.WriteLine("verify-induct --weighted · the provenance-reflected count measure — every check an exact identity");

        // ── 1 · WeightsFor + counters + checkpoint round-trip + Reorder permutation (all five provenances) ──
        {
            var tape = new Tape();
            var a = tape.Append("realspan"u8.ToArray(), "corpus");                          // evidence (Real)
            var d = tape.Append("dreamspn"u8.ToArray(), "node0", Provenances.Replay);        // hypothesis
            var b = tape.Append("breachsp"u8.ToArray(), "breach", Provenances.Breach);      // evidence (contact)
            var v = tape.Append("vestedsp"u8.ToArray(), "node0", Provenances.Replay);
            var wt = tape.Append("witnspan"u8.ToArray(), "eml", Provenances.Reflected);     // evidence (ladder-certified self-data)
            TapeEventID x = tape.Append("execspan"u8.ToArray(), "node0", Provenances.Execution);     // neutral, inducible action history
            bool t1 = tape.Reflect(v) && !tape.Reflect(v) && !tape.Reflect(a) && !tape.Reflect(wt) && !tape.Reflect(x);   // transition once; idempotent; non-Replay no-ops
            var w = tape.WeightsFor(8);
            bool t2 = tape.ByteLength == 54;
            bool t3 = true;
            for (int i = 0; i < 9; i++)
                t3 &= w[i] == 8 && w[9 + i] == 1 && w[18 + i] == 8 && w[27 + i] == 8
                    && w[36 + i] == 8 && w[45 + i] == 1;
            System.Buffers.ArrayPool<byte>.Shared.Return(w);
            bool t4 = tape is { RealCount: 1, ReplayCount: 2, BreachCount: 1, ReflectedReplayCount: 1, ReflectedCount: 1, ExecutionCount: 1 }
                && tape.BornEvidenceCount == 3 && tape.IsEvidence(v) && tape.IsEvidence(wt)
                && !tape.IsEvidence(d) && !tape.IsEvidence(x);
            using var ms = new MemoryStream();
            using (var cw = new CkptWriter(ms)) tape.Save(cw);
            ms.Position = 0;
            var tape2 = new Tape();
            using (var cr = new CkptReader(ms)) tape2.Load(cr);
            bool t5 = tape2 is { RealCount: 1, ReplayCount: 2, BreachCount: 1, ReflectedReplayCount: 1, ReflectedCount: 1, ExecutionCount: 1 }
                && tape2.IsEvidence(v) && !tape2.IsEvidence(d) && !tape2.IsEvidence(x)
                && tape2.ProvenanceOf(b) == Provenances.Breach && tape2.ProvenanceOf(wt) == Provenances.Reflected
                && tape2.ProvenanceOf(x) == Provenances.Execution;
            tape.Reorder([5, 4, 3, 2, 1, 0]);
            bool t6 = tape.IsEvidence(v) && !tape.IsEvidence(d) && tape.ProvenanceOf(d) == Provenances.Replay
                && tape.IsEvidenceAt(5) && tape.ProvenanceOf(tape.ResidentEventIDs[0]) == Provenances.Execution;
            Check(t1 && t2 && t3 && t4 && t5 && t6, "weights-for", $"bytes[8,1,8,8,8,1]·reflect-once={t1} len={tape.ByteLength} counters={t4} save∘load={t5} reorder={t6}");
        }

        // ── 2 · PairDelta — wScale=1 degeneracy, uniform-evidence exactness at any wScale, the mint boundary ──
        {
            bool ok = true;
            foreach (int c in new[] { 0, 1, 2, 3, 4, 10, 1000 })
                foreach (int vv in new[] { 2, 256, 257, 300, 4096 })
                    ok &= Mdl.PairDelta((long)c, vv, 1).Value == Mdl.PairDelta(c, vv).Value           // wScale=1 IS the old arithmetic
                       && Mdl.PairDelta((long)c * 8, vv, 8).Value == Mdl.PairDelta(c, vv).Value;      // uniform-evidence: ×8 counts / 8 divides out exactly
            bool gate = Mdl.PairDelta(24L, 256, 8).Value == Mdl.PairDelta(3, 256).Value               // 24 replay echoes buy what 3 jewels buy
                     && Mdl.PairDelta(23L, 256, 8).Value < Mdl.PairDelta(3, 256).Value;
            Check(ok && gate, "pairdelta", $"degeneracy+uniform sweep={ok} boundary(24@8≡3)={gate}");
        }

        // ── 3 · the mint gate — wScale=8: 24 replay recurrences mint, 23 starve; 3 REAL recurrences mint (today) ──
        {
            var tok = ByteTokenizer.Instance;
            RePairResult InduceN(int k, bool replay)
            {
                var data = new byte[2 * k];
                for (int i = 0; i < k; i++) { data[2 * i] = (byte)'a'; data[2 * i + 1] = (byte)'b'; }
                var tp = new Symbol[tok.MaxSymbols(data.Length)];
                int nn = tok.Tokenize(data, tp);
                var wts = new byte[nn];
                Array.Fill(wts, replay ? (byte)1 : (byte)8);
                return new RePair().Induce(tp.AsSpan(0, nn), Mbits.Zero, weights: wts, wScale: 8);
            }
            bool starve = InduceN(23, replay: true).Rules.Length == 0;
            bool mint   = InduceN(24, replay: true).Rules.Length >= 1;
            bool real3  = InduceN(3, replay: false).Rules.Length >= 1 && InduceN(2, replay: false).Rules.Length == 0;
            Check(starve && mint && real3, "mint-gate", $"replay 23→starve={starve} 24→mint={mint} · real 3→mint,2→starve={real3}");
        }

        // ── 4 · the weighted ORACLE — linear Induce == reference, mixed per-span weights, barrier armed; and the
        // uniform-evidence grammar at wScale=8 == the unweighted grammar (arithmetic identity, whole artifact) ──
        {
            var cases = new List<(string Name, byte[] Data)>
            {
                ("edges", "\n\nab\nab\nab\n\n"u8.ToArray()),
                ("lines", "the cat sat\nthe cat sat\nthe dog sat\nthe cat sat\n"u8.ToArray()),
            };
            var rng = new Random(11);
            for (int k = 0; k < 4; k++)
            {
                var d = new byte[400 + rng.Next(2000)];
                for (int i = 0; i < d.Length; i++) d[i] = rng.Next(9) == 0 ? (byte)'\n' : (byte)('a' + rng.Next(4));
                cases.Add(($"rand{k}", d));
            }
            var tok = ByteTokenizer.Instance;
            bool allSame = true, allUniform = true;
            foreach (var (name, data) in cases)
            {
                var tp = new Symbol[tok.MaxSymbols(data.Length)];
                int nn = tok.Tokenize(data, tp);
                var span = tp.AsSpan(0, nn);
                var wts = SegmentWeights(data, 8);
                var fastW = new RePair().Induce(span, Mbits.Zero, barrier: '\n', weights: wts, wScale: 8);
                var refW  = new RePair().InduceReference(span, Mbits.Zero, barrier: '\n', weights: wts, wScale: 8);
                allSame &= SameGrammar(fastW, refW);
                var uni  = new RePair().Induce(span, Mbits.Zero, barrier: '\n', wScale: 8);          // empty weights = uniform evidence
                var un1  = new RePair().Induce(span, Mbits.Zero, barrier: '\n');                     // the pre-provenance arithmetic
                allUniform &= SameGrammar(uni, un1);
            }
            Check(allSame, "weighted-oracle", $"linear==reference over {cases.Count} mixed-weight corpora");
            Check(allUniform, "uniform-invariance", "wScale=8 uniform-evidence grammar == unweighted grammar (exact)");
        }

        // ── 5 · Audit attribution + Corroborate — a real span and a replay span sharing a 16B unit: the top rule
        // and BOTH nested 8B children must see the real jewel (reverse-DAG credit) and carry the replay span as
        // supporter; corroboration reflects it exactly once, then the re-audit sees an all-evidence tape ──
        {
            var unit = "abcdefghijklmnop"u8;                                   // 16B → rules at 16B + 2×8B ≥ the reflect floor
            var spanBytes = new byte[32];
            unit.CopyTo(spanBytes); unit.CopyTo(spanBytes.AsSpan(16));
            var tape = new Tape();
            var A = tape.Append((byte[])spanBytes.Clone(), "corpus");
            var B = tape.Append((byte[])spanBytes.Clone(), "node0", Provenances.Replay);
            var (_, _, g) = Engine.Induce(tape);
            var audit = Audit(tape, g, 8);
            int floorRules = 0; bool attributed = true;
            for (int r = 0; r < g.Rules.Length; r++)
                if (audit.ExpLen[r] >= ReflectFloorBytes)
                {
                    floorRules++;
                    attributed &= audit.SawReal[r] && audit.Supporters[r].Length == 1 && audit.Supporters[r][0] == B.Value;
                }
                else attributed &= audit.Supporters[r].Length == 0;            // morphemes never support
            var uses = Engine.RuleUses(g);
            var audit1 = Audit(tape, g, 1);
            bool degen = true;
            for (int r = 0; r < uses.Length; r++) degen &= audit1.WUses[r] == uses[r];
            var journal = new Journal();
            int lines0 = journal.LineCount;
            int reflected = Corroborate(audit, tape, journal, step: 42);
            int again     = Corroborate(audit, tape, journal, step: 43);
            bool reflectOk = reflected == 1 && again == 0 && journal.LineCount == lines0 + 1
                       && tape.IsEvidence(B) && tape.ReflectedReplayCount == 1;
            var audit2 = Audit(tape, g, 8);
            bool post = audit2.Supporters.All(s => s.Length == 0) && audit2.WUses.Zip(audit.WUses).All(p => p.First >= p.Second);
            // A reflected corroboration may then evacuate to the shed tail. The
            // append-only audit extension must retire that resident identity;
            // reusing its old supporter set would attempt an illegal Reflect.
            var shedTape = new Tape();
            using var shedLog = new MemoryStream();
            shedTape.MountLog(shedLog);
            TapeEventID shedReal = shedTape.Append((byte[])spanBytes.Clone(), "corpus");
            TapeEventID shedReplay = shedTape.Append((byte[])spanBytes.Clone(), "node0", Provenances.Replay);
            RePairResult shedGrammar = Engine.Induce(shedTape).Result;
            var shedCache = new PearlAuditCache();
            PearlAudit shedAudit = shedCache.Get(shedTape, in shedGrammar, 8);
            Corroborate(shedAudit, shedTape, new Journal(), step: 44);
            shedTape.Evacuate([shedReplay], []);
            PearlAudit postShedAudit = shedCache.Get(shedTape, in shedGrammar, 8);
            bool shedWitnessRetired = postShedAudit.Supporters.All(supporters => !supporters.Contains(shedReplay.Value))
                && shedTape.IsEvidence(shedReal);
            // The reflected-only REPRICE: after Corroborate, the cached audit repriced with the exact
            // transition set must equal a from-scratch audit of the post-reflect tape, and the next
            // cache Get must be a HIT (no rebuild) — the consolidationPhase's guaranteed-double-audit killer.
            var repTape = new Tape();
            repTape.Append((byte[])spanBytes.Clone(), "corpus");
            TapeEventID repReplay = repTape.Append((byte[])spanBytes.Clone(), "node0", Provenances.Replay);
            RePairResult repGrammar = Engine.Induce(repTape).Result;
            var repCache = new PearlAuditCache();
            PearlAudit repAudit = repCache.Get(repTape, in repGrammar, 8, crossReflect: true);
            var repIDs = new List<TapeEventID>();
            int repVested = Corroborate(repAudit, repTape, new Journal(), step: 45, repIDs);
            repCache.RepriceReflected(repTape, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(repIDs));
            PearlAudit repriced = repCache.Get(repTape, in repGrammar, 8, crossReflect: true);
            bool repHit = !repCache.Rebuilt && repCache.ReflectedReprices == 1 && repCache.FullRebuilds == 1;
            PearlAudit fresh = Audit(repTape, in repGrammar, 8, crossReflect: true);
            bool repEq = repriced.SawReal.SequenceEqual(fresh.SawReal)
                && repriced.WUses.SequenceEqual(fresh.WUses)
                && repriced.Supporters.Length == fresh.Supporters.Length
                && repriced.Supporters.Zip(fresh.Supporters).All(p => p.First.SequenceEqual(p.Second));
            bool repJewels = repriced.JewelSources is not null && fresh.JewelSources is not null;
            if (repJewels)
                for (int r = 0; r < repriced.SawReal.Length; r++)
                {
                    HashSet<string>? mine = repriced.JewelSources![r]; HashSet<string>? theirs = fresh.JewelSources![r];
                    repJewels &= mine is null ? theirs is null : theirs is not null && mine.SetEquals(theirs);
                }
            Check(repVested == 1 && repTape.IsEvidence(repReplay) && repHit && repEq && repJewels, "reprice-reflected",
                $"vested={repVested} cache-hit={repHit} (reprices={repCache.ReflectedReprices} fulls={repCache.FullRebuilds}) planes-equal={repEq} jewels-invariant={repJewels}");
            Check(floorRules == 3 && attributed, "audit-attribution", $"floor-rules={floorRules} (16B+8B+8B) nested-credit+supporters={attributed}");
            Check(degen, "audit-degeneracy", "WUses@wScale=1 == Engine.RuleUses element-wise");
            Check(reflectOk && post && shedWitnessRetired, "corroborate", $"reflected={reflected} re-run={again} journal+1 · post-reflect all-evidence={post} · shed-witness-retired={shedWitnessRetired}");
        }

        // ── 6 · CROSS-REFLECTION (the sealed-loop fix, F0) — the source-independence gate. NO corpus, only dreams:
        // repeated dreams from DIFFERENT sources sharing a 16B rule MUTUALLY reflect under cross-reflection (each is the other's
        // generator-independent jewel), while the Real-only gate reflects NOTHING (SawReal all false — the sealed-loop
        // degeneracy the fix cures). The INDEPENDENCE GUARD: two SAME-source dreams sharing a rule reflect NOTHING even
        // under cross-reflection (self / clone is not a jewel). The grammar is induced UNWEIGHTED with three examples
        // per source so the shared rule clears the current MDL rent independently of the provenance claim under test.
        {
            var unitBytes = "ABCDEFGHIJKLMNOP"u8.ToArray();                  // 16B shared body → a ≥8B rule when two spans carry it
            byte[] Body() => (byte[])unitBytes.Clone();

            // (a) CROSS-SOURCE — node0 + node1 dreams, no corpus. Cross-reflection reflects BOTH; Real-only reflects neither.
            var tX = new Tape();
            TapeEventID[] crossReplays = new TapeEventID[6];
            for (int i = 0; i < 3; i++) crossReplays[i] = tX.Append(Body(), "node0", Provenances.Replay);
            for (int i = 3; i < 6; i++) crossReplays[i] = tX.Append(Body(), "node1", Provenances.Replay);
            var (_, _, gX) = Engine.Induce(tX);
            var jX = new Journal();
            var offAudit = Audit(tX, gX, 8, crossReflect: false);
            int offReflect = Corroborate(offAudit, tX, jX, step: 1);         // Real-only: no corpus ⇒ 0
            var onAudit = Audit(tX, gX, 8, crossReflect: true);
            System.Text.StringBuilder crossTrace = new();
            foreach (TapeEventView view in tX.GetGrammarEventViews())
                crossTrace.Append(view.Id).Append(':').Append(view.Provenance).Append('@').Append(view.Source).Append(',');
            for (int r = 0; r < gX.Rules.Length; r++)
            {
                if (onAudit.ExpLen[r] < ReflectFloorBytes) continue;
                crossTrace.Append('r').Append(r).Append("[supp=").Append(onAudit.Supporters[r].Length).Append(";src=");
                if (onAudit.JewelSources?[r] is HashSet<string> sources)
                    foreach (string source in sources.Order(StringComparer.Ordinal)) crossTrace.Append(source).Append(',');
                crossTrace.Append("],");
            }
            int onReflect = Corroborate(onAudit, tX, jX, step: 2);          // cross-reflection: node0⊥node1 ⇒ both reflect
            bool crossOk = offReflect == 0 && onReflect == crossReplays.Length
                && crossReplays.All(tX.IsEvidence) && tX.ReflectedReplayCount == crossReplays.Length;

            // (b) SAME-SOURCE — two node0 dreams sharing the rule. The independence guard rejects self-reflection ⇒ 0.
            var tS = new Tape();
            TapeEventID[] selfReplays = new TapeEventID[6];
            for (int i = 0; i < selfReplays.Length; i++) selfReplays[i] = tS.Append(Body(), "node0", Provenances.Replay);
            var (_, _, gS) = Engine.Induce(tS);
            var jS = new Journal();
            var selfAudit = Audit(tS, gS, 8, crossReflect: true);
            int selfReflect = Corroborate(selfAudit, tS, jS, step: 3);
            bool guardOk = selfReflect == 0 && selfReplays.All(id => !tS.IsEvidence(id)) && tS.ReflectedReplayCount == 0;

            // the jewel-source set itself: the shared ≥8B rule under (a) carries BOTH sources; under (b) only node0.
            int rX = -1; for (int r = 0; r < gX.Rules.Length; r++) if (onAudit.ExpLen[r] >= ReflectFloorBytes) { rX = r; break; }
            bool setOk = rX >= 0 && onAudit.JewelSources is not null
                      && onAudit.JewelSources[rX] is { } ws && ws.Count == 2 && ws.Contains("node0") && ws.Contains("node1")
                      && selfAudit.JewelSources is { } && selfAudit.JewelSources.Any(s => s is { Count: 1 } one && one.Contains("node0"));

            Check(crossOk, "cross-reflection", $"cross-source: off(real-only)={offReflect} on(cross)={onReflect} · all dreams reflected={tX.ReflectedReplayCount == crossReplays.Length} · {crossTrace}");
            Check(guardOk, "independence-guard", $"same-source(node0×{selfReplays.Length}) under cross-reflection reflected={selfReflect} (self-reflection rejected)");
            Check(setOk, "jewel-source-set", "the ≥8B rule's source set = {node0,node1} cross, {node0} same");
        }

        // ── 7 · EXECUTION NEUTRALITY — execution packets grammarize but never enter the supporter set or evidence
        // stock. Their source may exercise a rule, yet Corroborate has no execution span it can promote.
        {
            Tape tape = new();
            TapeEventID[] executions = new TapeEventID[6];
            for (int i = 0; i < executions.Length; i++)
                executions[i] = tape.Append("EXECUTION-ROUTE"u8.ToArray(), "node0", Provenances.Execution);
            RePairResult grammar = Engine.Induce(tape).Result;
            PearlAudit audit = Audit(tape, grammar, 8, crossReflect: true);
            Journal journal = new();
            int reflected = Corroborate(audit, tape, journal, step: 4);
            int supporters = 0;
            for (int r = 0; r < audit.Supporters.Length; r++) supporters += audit.Supporters[r].Length;
            bool neutral = reflected == 0 && supporters == 0 && tape.ExecutionCount == executions.Length
                && tape.BornEvidenceCount == 0 && executions.All(id => !tape.IsEvidence(id));
            Check(neutral, "execution-neutral", $"execution={tape.ExecutionCount} supporters={supporters} reflected={reflected} born-evidence={tape.BornEvidenceCount}");
        }

        Console.WriteLine(fails == 0
            ? "✓ verify-induct --weighted PASSED — the provenance machinery is exact: wScale=1 degenerates to today, evidence behaves as today at any wScale, replay echoes pay 1/wScale."
            : $"✗ verify-induct --weighted FAILED — {fails} broken identit{(fails == 1 ? "y" : "ies")}.");
        return fails == 0 ? 0 : 1;
    }

    // per-'\n'-segment weights, alternating evidence/replay (Tape.WeightsFor's shape: a segment's bytes AND its
    // trailing separator carry the segment's weight — the span-constant contract the one-fetch merge rests on).
    private static byte[] SegmentWeights(byte[] data, int wScale)
    {
        var w = new byte[data.Length];
        int seg = 0;
        for (int i = 0; i < data.Length; i++)
        {
            w[i] = seg % 2 == 0 ? (byte)wScale : (byte)1;
            if (data[i] == (byte)'\n') seg++;
        }
        return w;
    }

    // byte-identical grammar equality — same rule sequence (id + pattern), same compressed tape, same savings.
    private static bool SameGrammar(in RePairResult x, in RePairResult y)
    {
        if (x.Rules.Length != y.Rules.Length || x.Compressed.Length != y.Compressed.Length || x.TotalSavings.Value != y.TotalSavings.Value) return false;
        for (int i = 0; i < x.Rules.Length; i++)
        {
            if (!x.Rules[i].Id.Equals(y.Rules[i].Id) || x.Rules[i].Pattern.Length != y.Rules[i].Pattern.Length) return false;
            for (int j = 0; j < x.Rules[i].Pattern.Length; j++)
                if (x.Rules[i].Pattern[j].Value != y.Rules[i].Pattern[j].Value) return false;
        }
        for (int i = 0; i < x.Compressed.Length; i++)
            if (x.Compressed[i].Value != y.Compressed[i].Value) return false;
        return true;
    }
}
