namespace Cogito;

using System.Diagnostics;
using System.Text;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Induct;


// ── ANTI-UNIFICATION — the MDL-gated paradigm-class growth operator ──
//
// Re-Pair memorizes exact surfaces; under the memory budget the machine NEEDS generalization —
// SLOTS over surfaces — and this is the escape route the budget selects for. Port of
// coact/coact_growth.py:growth_loop. The mechanism (ported by MECHANISM, never by name — the
// FRANKENSTEIN word-collision glossary is LAW):
//
//   (1) INDUCE   faithful Re-Pair over the corpus (word resolution — a "word" is a token between
//                delimiters; the synthetic kill-line + real text are whitespace-structured, and the
//                byte engine's rules over such corpora ARE surface phrases whose fillers vary).
//   (2) ANTI-UNIFY  rule yields → blank one position → two fillers colliding on the same
//                (length, position, prefix, suffix) blanked key are SLOT-MATES ("cat"/"dog" from
//                "the ___ was watching" — the rules anti-unify at that slot).
//   (3) GATE     each candidate paradigm class by ΔMDL (a lossless two-part code) — mint the slot
//                IFF it PAYS: abstraction (one slot) must beat memorization (N literals) BY
//                compression. This ΔMDL gate IS the intrinsic signal; it also rejects filler-noise.
//   (4) SUBSTITUTE accepted slot symbols into the corpus  (5) RE-INDUCE  (6) ITERATE
//
// A minted [S0] is a terminal to the NEXT pass, so iter-1 can anti-unify `[S0] was [S1]` vs
// `[S0] was [S2]` → a class-of-classes (the recursion DEEPEN — port 3 — turns into a real tower).
//
// The readout (a synthetic corpus with KNOWN paradigm structure — the CHAIN corpus): minting PAYS
// thousands of bits (the +7675-bit-analog on the port's own terms) with ≥95% slot purity, where a
// distributional clusterer FRAGMENTS the same classes. The gate is the point — frozen Re-Pair mints
// ZERO class-spanning rules; the growth loop's abstract rules fire on unseen member combinations.
//
// GLOSSARY: this `residual` is the MDL member-choice residual (the within-slot substitution the
// slot deferred), NOT the frontier residual (Radula) nor the self-model residual (SelfStream). Same
// word, three organs. Ported by mechanism.

// ─────────────────────────────────────────────────────────────────────────────────────────────────
//  THE WORD-LEVEL FAITHFUL RE-PAIR — per-stream (sentence boundaries are hard barriers)
// ─────────────────────────────────────────────────────────────────────────────────────────────────

/// One Re-Pair snapshot over a word corpus: the compressed streams (for the MDL data term), the rules
/// as (a,b) child-id pairs (nonterminal `Base+i`), each rule's FULL terminal yield already mapped back
/// to word strings (what anti-unification aligns), and `Base` = the terminal count. `NAlpha` = the
/// working-alphabet size the MDL charges per symbol (terminals + rules), matching the python.
internal readonly record struct WordSnapshot(
    List<int[]> Compressed, List<(int A, int B)> Rules, List<string[]> Yield, int Base)
{
    public int NAlpha => Math.Max(Base + Rules.Count, 2);
}

/// Faithful word-resolution Re-Pair (port of loopy.repair / sweep_lib.repair_checkpoints — byte-
/// identical mechanism). Per-stream: a pair NEVER crosses a sentence boundary (each span is its own
/// stream), so the yields are within-line phrases the way the python corpus is per-sentence.
/// Deterministic: vocab ids are assigned in SORTED order and the winner is (max count, then smallest
/// a, then smallest b) — the same total order as the byte engine's smallest-packed-key tie-break.
internal static class WordRePair
{
    public static WordSnapshot Induce(IReadOnlyList<IReadOnlyList<string>> corpus, int cmin = 3, int maxRules = 4000)
    {
        // vocab = sorted distinct words → dense ids [0, base); base = the first nonterminal id.
        var vocabSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in corpus) foreach (var w in s) vocabSet.Add(w);
        var vocab = vocabSet.ToArray();
        var wid = new Dictionary<string, int>(vocab.Length, StringComparer.Ordinal);
        for (int i = 0; i < vocab.Length; i++) wid[vocab[i]] = i;
        int @base = vocab.Length;

        // Winner-order-IDENTICAL incremental rewrite of the reference loop (the AnnealEvict.Mint planes —
        // incremental digram counts, occurrence postings validated at rewrite, a lazy max-heap; same total
        // order: count desc, then smallest packed key). The reference body re-tallied EVERY digram over ALL
        // streams and rewrote every stream PER MINT — O(rules·corpus); this is O(corpus + merge work) per
        // call, and the growth loop calls Induce ~10-20× per sleep. Streams ride ONE linked list with a
        // Barrier sentinel between them, so no pair ever crosses a sentence boundary (the per-stream law).
        const int Barrier = -1;
        int total = corpus.Count;
        foreach (var s in corpus) total += s.Count;
        if (total == 0) return new WordSnapshot(new List<int[]>(), new List<(int, int)>(), new List<string[]>(), @base);
        var sym = new int[total]; var nxt = new int[total]; var prv = new int[total]; var dead = new bool[total];
        int n = 0;
        foreach (var s in corpus)
        {
            for (int i = 0; i < s.Count; i++) sym[n++] = wid[s[i]];
            sym[n++] = Barrier;
        }
        for (int i = 0; i < total; i++) { nxt[i] = i + 1 < total ? i + 1 : -1; prv[i] = i - 1; }

        var rules = new List<(int A, int B)>();
        var yield = new List<string[]>();                 // yield[i] = full terminal WORD sequence of nonterminal base+i
        var yieldOf = new Dictionary<int, string[]>();     // symbol id → its terminal-word yield (terminals map to themselves)

        var counts = new Dictionary<long, int>();
        var occ = new Dictionary<long, List<int>>();
        var heap = new PriorityQueue<long, (long NegC, long Key)>();
        void Push(long key, int c) => heap.Enqueue(key, (-(long)c, key));
        void Inc(long key, int pos)
        {
            int c = counts.GetValueOrDefault(key) + 1; counts[key] = c;
            (occ.TryGetValue(key, out var l) ? l : occ[key] = new()).Add(pos);
            Push(key, c);
        }
        void Dec(long key)
        {
            if (!counts.TryGetValue(key, out int c)) return;
            c--;
            if (c <= 0) counts.Remove(key); else { counts[key] = c; Push(key, c); }   // occ cleaned lazily at rewrite
        }
        // THE one tally (self-overlaps counted, like the reference engine) — counts + postings, heap seeded
        // once per distinct key (distinct keys ⇒ distinct priorities, so heap order is dictionary-order-free).
        for (int i = 0; i + 1 < total; i++)
        {
            if (sym[i] == Barrier || sym[i + 1] == Barrier) continue;
            long key = Pack(sym[i], sym[i + 1]);
            counts[key] = counts.GetValueOrDefault(key) + 1;
            (occ.TryGetValue(key, out var l) ? l : occ[key] = new()).Add(i);
        }
        foreach (var (key, c) in counts) Push(key, c);

        for (int pass = 0; pass < maxRules; pass++)
        {
            // Winner = highest count, then smallest packed key (smallest a, then smallest b — the Vow).
            long bestKey = 0; int bestCount = -1;
            while (heap.TryDequeue(out long key, out var pr))
            {
                if (!counts.TryGetValue(key, out int cur) || cur != (int)(-pr.NegC)) continue;   // stale entry
                if (cur < cmin) { Push(key, cur); break; }     // heap top IS the global max ⟹ nothing eligible remains
                bestKey = key; bestCount = cur; break;
            }
            if (bestCount < cmin) break;

            int a = (int)(bestKey >> 32), b = (int)(bestKey & 0xFFFFFFFFL);
            int nt = @base + rules.Count;
            rules.Add((a, b));
            var ya = a < @base ? [vocab[a]] : yieldOf[a];
            var yb = b < @base ? [vocab[b]] : yieldOf[b];
            var y = new string[ya.Length + yb.Length];
            ya.CopyTo(y, 0); yb.CopyTo(y, ya.Length);
            yield.Add(y); yieldOf[nt] = y;

            // Rewrite (a,b)→nt over the postings, left-to-right non-overlapping (positions sorted; stale /
            // overlap-consumed occurrences are validated away by the sym/dead check — the reference scan's order).
            if (occ.TryGetValue(bestKey, out var positions))
            {
                positions.Sort();
                foreach (int i in positions)
                {
                    if (dead[i] || sym[i] != a) continue;
                    int j = nxt[i];
                    if (j < 0 || dead[j] || sym[j] != b) continue;
                    int p = prv[i], q = nxt[j];
                    if (p >= 0 && sym[p] != Barrier) Dec(Pack(sym[p], a));
                    if (q >= 0 && sym[q] != Barrier) Dec(Pack(b, sym[q]));
                    Dec(bestKey);
                    sym[i] = nt; dead[j] = true; nxt[i] = q; if (q >= 0) prv[q] = i;
                    if (p >= 0 && sym[p] != Barrier) Inc(Pack(sym[p], nt), p);
                    if (q >= 0 && sym[q] != Barrier) Inc(Pack(nt, sym[q]), i);
                }
                occ.Remove(bestKey);
            }
        }

        // land the linked list back as the per-stream compressed arrays (split at the barriers).
        var streams = new List<int[]>(corpus.Count);
        var cur2 = new List<int>();
        for (int i = 0; i >= 0; i = nxt[i])
        {
            if (sym[i] == Barrier) { streams.Add(cur2.ToArray()); cur2.Clear(); }
            else cur2.Add(sym[i]);
        }
        return new WordSnapshot(streams, rules, yield, @base);

        static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────
//  THE PARADIGM MODEL — the growth loop's discovered slot structure
// ─────────────────────────────────────────────────────────────────────────────────────────────────

/// The discovered paradigm structure: which word wears which slot (member → slot; slots nest, a slot
/// can wear a super-slot — the tower), and each slot's members. The MEANING substrate the byte grammar
/// generalizes onto — a slot [S0]={cat,dog,…} + a skeleton "the [S0] was" replaces N literal lines.
public sealed class Paradigm
{
    /// member word → its immediate slot name (chains up the tower: "cat"→"[S0]"→"[[S3]]").
    public Dictionary<string, string> MemberToSlot { get; } = new(StringComparer.Ordinal);
    /// slot name → its immediate members (words or sub-slots).
    public Dictionary<string, List<string>> SlotMembers { get; } = new(StringComparer.Ordinal);
    /// birth order of each slot — GLOBALLY monotonic across the machine's whole life (not a per-loop iteration
    /// index): MintSlots emits slots birth-ascending so a super-slot's sub-slot symbols always already exist, and
    /// that contract must survive the paradigm persisting across sleep passes: the model is night-shift
    /// state now, seeded into every growth pass, not rediscovered from scratch.
    public Dictionary<string, int> SlotBirth { get; } = new(StringComparer.Ordinal);
    /// the next [S{n}] name — persistent so slot names never collide across sleep passes.
    public int NextName;
    /// the next birth stamp (see SlotBirth).
    public int NextBirth;
    // ── THE MINT SPINE (face 3e — the Δ-mint) ── derived caches maintained AT BIRTH, so MintSlots rematerializes
    // the standing tower without re-sorting SlotMembers.Keys by birth, re-sorting every member list, or re-encoding
    // every literal surface — the per-sleep re-walk that grew with the paradigm forever. Stable slot identities
    // suppress duplicate appends on an unchanged grammar. Pure
    // functions of the maps ⟹ NEVER checkpointed — the Load path calls RebuildMintSpine (same law as the
    // night-shift indexes: Save∘Load ≡ the incremental accretion).
    internal readonly List<string> MintOrder = new();                                            // slots birth-ascending (birth is monotonic ⟹ append order IS the sort)
    internal readonly Dictionary<string, string[]> SortedMembers = new(StringComparer.Ordinal);  // per-slot members, ordinal-sorted once at birth
    internal readonly HashSet<int> MemberSurfaceHashes = new();                                  // ContentHash of every member surface — the fresh-grammar probe (which rule expansions the mint can ask for)
    // Standing grammar expansion index. The rule order is the append/rebase corroboration: when the
    // next publication keeps this prefix, MintSlots expands only the suffix AddedRules. A
    // changed prefix is a deliberate rebase/bootstrap and repopulates the index once.
    internal readonly List<RuleID> ExpansionRuleOrder = new();
    internal readonly Dictionary<RuleID, byte[]> RuleExpansions = new();
    internal readonly Dictionary<string, Symbol> SurfaceSymbols = new(StringComparer.Ordinal);
    // The binary Loom spine is a separate authority from the suffix layer.  A
    // stride can append binary rules beneath the side layer; these IDs let the
    // next mint expand only that new base suffix instead of mistaking the old
    // overlay for a broken prefix.
    internal readonly List<RuleID> BaseRuleOrder = new();
    internal readonly Dictionary<RuleID, byte[]> BaseRuleExpansions = new();
    // hash(expansion) → newest base rule slot, with older same-hash slots retained only when a collision
    // actually occurs. ContentHash is a 32-bit probe, never identity: the retro lookup byte-compares the
    // newest candidate then its collision bucket so Δ mint has the full walk's exact last-match semantics.
    // Invariant: covers exactly BaseRuleOrder's slots (populated by the full walk, extended by the Δ walk,
    // cleared with the prefix on rebase/RebuildMintSpine).
    internal readonly Dictionary<int, int> BaseExpansionSlots = new();
    internal readonly Dictionary<int, List<int>> BaseExpansionCollisionSlots = new();
    internal readonly HashSet<RuleID> OverlayRuleIDs = new();
    internal readonly Dictionary<string, Symbol> SlotSymbols = new(StringComparer.Ordinal);
    internal GrammarRule[]? LastMintRules;
    private readonly Dictionary<string, GrammarRule> _literalTemplates = new(StringComparer.Ordinal);   // surface → its minted literal rule (position-independent: terminal-byte pattern, content-addressed id, fixed cost)

    public int SlotCount => SlotMembers.Count;

    // The spine's byte[]/string[] values are frozen at insertion (MintSlots/IndexSlot only ever insert
    // freshly-built arrays — no writer mutates a stored value), so clone/commit copy MAP STRUCTURE and
    // share the arrays. The pre-share shape deep-cloned every expansion twice per committing sleep
    // (CloneForGrowth at GrowthLoop start + again inside CommitFrom) — O(grammar bytes) per sleep for
    // arrays nobody writes.
    internal Paradigm CloneForGrowth()
    {
        var clone = new Paradigm
        {
            NextName = NextName,
            NextBirth = NextBirth,
            LastMintRules = LastMintRules,
        };
        foreach (var (name, members) in SlotMembers) clone.SlotMembers[name] = new List<string>(members);
        foreach (var pair in SlotBirth) clone.SlotBirth[pair.Key] = pair.Value;
        foreach (var pair in MemberToSlot) clone.MemberToSlot[pair.Key] = pair.Value;
        clone.MintOrder.AddRange(MintOrder);
        foreach (var pair in SortedMembers) clone.SortedMembers[pair.Key] = pair.Value;
        foreach (int hash in MemberSurfaceHashes) clone.MemberSurfaceHashes.Add(hash);
        clone.ExpansionRuleOrder.AddRange(ExpansionRuleOrder);
        foreach (var pair in RuleExpansions) clone.RuleExpansions[pair.Key] = pair.Value;
        foreach (var pair in SurfaceSymbols) clone.SurfaceSymbols[pair.Key] = pair.Value;
        clone.BaseRuleOrder.AddRange(BaseRuleOrder);
        foreach (var pair in BaseRuleExpansions) clone.BaseRuleExpansions[pair.Key] = pair.Value;
        foreach (var pair in BaseExpansionSlots) clone.BaseExpansionSlots[pair.Key] = pair.Value;
        foreach (var pair in BaseExpansionCollisionSlots) clone.BaseExpansionCollisionSlots[pair.Key] = new List<int>(pair.Value);
        foreach (RuleID id in OverlayRuleIDs) clone.OverlayRuleIDs.Add(id);
        foreach (var pair in SlotSymbols) clone.SlotSymbols[pair.Key] = pair.Value;
        foreach (var pair in _literalTemplates) clone._literalTemplates[pair.Key] = pair.Value;
        return clone;
    }

    internal void CommitFrom(Paradigm source)
    {
        MemberToSlot.Clear(); foreach (var pair in source.MemberToSlot) MemberToSlot[pair.Key] = pair.Value;
        SlotMembers.Clear(); foreach (var pair in source.SlotMembers) SlotMembers[pair.Key] = new List<string>(pair.Value);
        SlotBirth.Clear(); foreach (var pair in source.SlotBirth) SlotBirth[pair.Key] = pair.Value;
        NextName = source.NextName; NextBirth = source.NextBirth;
        MintOrder.Clear(); MintOrder.AddRange(source.MintOrder);
        SortedMembers.Clear(); foreach (var pair in source.SortedMembers) SortedMembers[pair.Key] = pair.Value;
        MemberSurfaceHashes.Clear(); foreach (int hash in source.MemberSurfaceHashes) MemberSurfaceHashes.Add(hash);
        ExpansionRuleOrder.Clear(); ExpansionRuleOrder.AddRange(source.ExpansionRuleOrder);
        RuleExpansions.Clear(); foreach (var pair in source.RuleExpansions) RuleExpansions[pair.Key] = pair.Value;
        SurfaceSymbols.Clear(); foreach (var pair in source.SurfaceSymbols) SurfaceSymbols[pair.Key] = pair.Value;
        BaseRuleOrder.Clear(); BaseRuleOrder.AddRange(source.BaseRuleOrder);
        BaseRuleExpansions.Clear(); foreach (var pair in source.BaseRuleExpansions) BaseRuleExpansions[pair.Key] = pair.Value;
        BaseExpansionSlots.Clear(); foreach (var pair in source.BaseExpansionSlots) BaseExpansionSlots[pair.Key] = pair.Value;
        BaseExpansionCollisionSlots.Clear(); foreach (var pair in source.BaseExpansionCollisionSlots) BaseExpansionCollisionSlots[pair.Key] = new List<int>(pair.Value);
        OverlayRuleIDs.Clear(); foreach (RuleID id in source.OverlayRuleIDs) OverlayRuleIDs.Add(id);
        SlotSymbols.Clear(); foreach (var pair in source.SlotSymbols) SlotSymbols[pair.Key] = pair.Value;
        LastMintRules = source.LastMintRules;
        _literalTemplates.Clear(); foreach (var pair in source._literalTemplates) _literalTemplates[pair.Key] = pair.Value;
    }

    /// Validate the persisted tower before it is admitted into a live model.
    /// The inverse map is derived, so the durable authority is the slot/member
    /// graph plus birth order; every load and delta path proves that graph is
    /// single-owner, acyclic, and child-before-parent before mutating state.
    internal void ValidatePersistence()
        => ValidatePersistence(SlotMembers, SlotBirth, NextName, NextBirth, MemberToSlot);

    internal static void ValidatePersistence(
        IReadOnlyDictionary<string, List<string>> slots,
        IReadOnlyDictionary<string, int> births,
        int nextName,
        int nextBirth,
        IReadOnlyDictionary<string, string>? directOwners = null)
    {
        if (nextName < 0 || nextBirth < 0)
            throw new InvalidDataException("paradigm counters are negative");
        if (slots.Count != births.Count)
            throw new InvalidDataException("paradigm slot/birth maps disagree");

        var seenBirths = new HashSet<int>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        int maxBirth = -1;
        foreach (var pair in slots)
        {
            string name = pair.Key;
            List<string>? members = pair.Value;
            if (string.IsNullOrWhiteSpace(name) || members is null || members.Count == 0)
                throw new InvalidDataException("paradigm slot is malformed");
            if (!births.TryGetValue(name, out int birth) || birth < 0 || !seenBirths.Add(birth))
                throw new InvalidDataException($"paradigm slot '{name}' has a duplicate or invalid birth");
            maxBirth = Math.Max(maxBirth, birth);

            var membersSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? member in members)
            {
                if (string.IsNullOrWhiteSpace(member) || !membersSeen.Add(member))
                    throw new InvalidDataException($"paradigm slot '{name}' has a duplicate or empty member");
                if (string.Equals(name, member, StringComparison.Ordinal))
                    throw new InvalidDataException($"paradigm slot '{name}' contains itself");
                if (!owners.TryAdd(member, name))
                    throw new InvalidDataException($"paradigm member '{member}' has multiple direct owners");
                if (slots.ContainsKey(member))
                {
                    if (!births.TryGetValue(member, out int childBirth) || childBirth >= birth)
                        throw new InvalidDataException($"paradigm child '{member}' is not born before parent '{name}'");
                }
            }
        }
        if (maxBirth >= 0 && nextBirth <= maxBirth)
            throw new InvalidDataException("paradigm next birth is not beyond the standing tower");

        if (directOwners is not null)
        {
            if (directOwners.Count != owners.Count)
                throw new InvalidDataException("paradigm inverse ownership count disagrees");
            foreach (var pair in owners)
                if (!directOwners.TryGetValue(pair.Key, out string? owner)
                    || !string.Equals(owner, pair.Value, StringComparison.Ordinal))
                    throw new InvalidDataException($"paradigm inverse ownership disagrees for '{pair.Key}'");
        }

        // A birth-order check catches ordinary malformed nesting; DFS catches
        // cycles that otherwise make ResolveTop spin forever.
        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (string name in slots.Keys)
            Visit(name);

        void Visit(string name)
        {
            if (state.TryGetValue(name, out byte mark))
            {
                if (mark == 1) throw new InvalidDataException($"paradigm slot cycle reaches '{name}'");
                return;
            }
            state[name] = 1;
            foreach (string member in slots[name])
                if (slots.ContainsKey(member)) Visit(member);
            state[name] = 2;
        }
    }

    /// THE one slot-creation verb — every writer (the growth loop, the anchor-percolation loop, nothing else)
    /// births through here, so the mint spine can never drift from the maps.
    public void BirthSlot(string name, IReadOnlyList<string> members)
    {
        if (string.IsNullOrWhiteSpace(name) || SlotMembers.ContainsKey(name))
            throw new InvalidOperationException($"paradigm slot '{name}' already exists or is empty");
        if (members is null || members.Count == 0 || NextBirth < 0)
            throw new InvalidOperationException("paradigm slot birth is malformed");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? member in members)
        {
            if (string.IsNullOrWhiteSpace(member) || !seen.Add(member) || MemberToSlot.ContainsKey(member))
                throw new InvalidOperationException($"paradigm member '{member}' is already directly owned or malformed");
            if (SlotMembers.ContainsKey(member)
                && SlotBirth.GetValueOrDefault(member) >= NextBirth)
                throw new InvalidOperationException($"paradigm child '{member}' is not born before parent '{name}'");
        }
        var mem = new List<string>(members);
        SlotMembers[name] = mem;
        SlotBirth[name] = NextBirth++;
        foreach (var w in mem) MemberToSlot[w] = name;
        IndexSlot(name, mem);
    }

    /// Rebuild the derived spine from the maps — the checkpoint-Load path (slots arrive name-sorted there; the
    /// spine is birth-sorted). The name tie-break mirrors the pre-spine OrderBy exactly (births are unique under
    /// BirthSlot, so it never fires on organically-grown state).
    public void RebuildMintSpine()
    {
        MintOrder.Clear(); SortedMembers.Clear(); MemberSurfaceHashes.Clear(); _literalTemplates.Clear();
        ExpansionRuleOrder.Clear(); RuleExpansions.Clear(); SurfaceSymbols.Clear();
        BaseRuleOrder.Clear(); BaseRuleExpansions.Clear(); BaseExpansionSlots.Clear(); BaseExpansionCollisionSlots.Clear(); OverlayRuleIDs.Clear();
        SlotSymbols.Clear();
        LastMintRules = null;
        foreach (var (name, mem) in SlotMembers) IndexSlot(name, mem, order: false);
        MintOrder.AddRange(SlotMembers.Keys.OrderBy(k => SlotBirth.GetValueOrDefault(k)).ThenBy(k => k, StringComparer.Ordinal));
    }

    private void IndexSlot(string name, List<string> mem, bool order = true)
    {
        var sorted = mem.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);
        SortedMembers[name] = sorted;
        foreach (var w in mem) MemberSurfaceHashes.Add(Tape.ContentHash(Encoding.UTF8.GetBytes(w)));
        if (order) MintOrder.Add(name);
    }

    /// The literal rule a surface mints as — position-independent (terminal-byte pattern; the symbol a rule gets
    /// IS its array position, not a field), so one construction serves every sleep. Cached per distinct surface.
    internal GrammarRule LiteralTemplate(string surface)
    {
        if (_literalTemplates.TryGetValue(surface, out var r)) return r;
        var bytes = Encoding.UTF8.GetBytes(surface);
        var pat = new Symbol[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) pat[i] = new Symbol(bytes[i]);
        return _literalTemplates[surface] = new GrammarRule(GrammarRule.ComputeId(pat), pat, new Mbits(256 + 8000L * bytes.Length));
    }

    /// Stable slot identity independent of the current grammar's symbol indexes. A Loom
    /// re-induction retains only binary rules, so a SlotClass's member symbols are rebased
    /// on every fresh snapshot; its persisted paradigm name is the identity authority.
    internal RuleID SlotRuleID(string name)
        => Hash.Rule(Encoding.UTF8.GetBytes("cogito/antiunify/slot/" + name));

    internal void IndexBaseExpansionSlot(int expansionHash, int slot)
    {
        if (BaseExpansionSlots.TryGetValue(expansionHash, out int previous))
        {
            if (!BaseExpansionCollisionSlots.TryGetValue(expansionHash, out List<int>? collisions))
                BaseExpansionCollisionSlots[expansionHash] = collisions = new List<int>();
            collisions.Add(previous);
        }
        BaseExpansionSlots[expansionHash] = slot;
    }

    /// Follow the member→slot chain to the top slot (the resolved paradigm — the tower root a word lives under).
    public string ResolveTop(string sym)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (MemberToSlot.TryGetValue(sym, out var s))
        {
            if (!seen.Add(sym))
                throw new InvalidOperationException($"paradigm member-to-slot cycle reaches '{sym}'");
            sym = s;
        }
        return sym;
    }

    /// The deepest tower height — how many slot levels stack over a leaf word (0 = no slot; 1 = flat class; 2 = a
    /// class-of-classes). Compression alone rests at 1 (the port-2 readout); the tower (port 3) is what climbs it.
    public int MaxDepth()
    {
        int d = 0;
        foreach (var w in MemberToSlot.Keys)
        {
            int n = 0; string cur = w;
            while (MemberToSlot.TryGetValue(cur, out var s)) { cur = s; n++; }
            d = Math.Max(d, n);
        }
        return d;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────
//  ANTI-UNIFICATION — edges · candidates · two-part MDL · the growth loop
// ─────────────────────────────────────────────────────────────────────────────────────────────────

public static class AntiUnify
{
    private const char Sep = '\u001f';   // unit separator — joins prefix/suffix word-tuples into a hashable slot key

    /// The MDL breakdown (bits) of a slotted corpus, LOSSLESSLY encoding the ORIGINAL corpus through the slots:
    ///   data     = entropy code of the Re-Pair-compressed SLOTTED stream
    ///   grammar  = Re-Pair rules (2 symbols each) at log₂(alphabet)
    ///   slotdef  = the slot member lists
    ///   residual = per original slot-member token, the within-slot member choice the substitution threw away
    ///              (charged at the TOP slot — hierarchical choice decomposes additively, so this is exact/lossless).
    /// Lower total = a better model of the SAME corpus. Flat baseline = no slots (frozen Re-Pair).
    public readonly record struct Mdl(double Total, double Data, double Grammar, double SlotDef, double Residual, int Rules);

    /// ANTI-UNIFY: blank-one-position over every rule's WORD yield. Two fillers colliding on the same
    /// (length, position, prefix, suffix) blanked key are SLOT-MATES. Edge weight = Σ min(filler counts) over
    /// shared slots — tight paradigm-mates (many shared contexts) outweigh loose accidental collisions.
    /// Returns edges keyed "a␟b" with a<b (ordinal), so lookups are order-free.
    public static Dictionary<string, int> Edges(List<string[]> yields)
    {
        // slot key → (filler word → count)
        var slot = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var sb = new StringBuilder();                     // ONE builder across every blanked key (was a fresh alloc per rule×position)
        foreach (var y in yields)
        {
            if (y.Length < 2) continue;
            for (int p = 0; p < y.Length; p++)
            {
                sb.Clear();
                sb.Append(y.Length).Append(Sep).Append(p).Append(Sep);
                for (int i = 0; i < p; i++) sb.Append(y[i]).Append(Sep);
                sb.Append(Sep);                                   // prefix|suffix divider
                for (int i = p + 1; i < y.Length; i++) sb.Append(y[i]).Append(Sep);
                string key = sb.ToString();
                var fillers = slot.TryGetValue(key, out var f) ? f : (slot[key] = new(StringComparer.Ordinal));
                fillers[y[p]] = fillers.GetValueOrDefault(y[p]) + 1;
            }
        }
        var edge = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fillers in slot.Values)
        {
            if (fillers.Count < 2) continue;
            var ws = fillers.Keys.ToArray(); Array.Sort(ws, StringComparer.Ordinal);
            for (int i = 0; i < ws.Length; i++)
                for (int j = i + 1; j < ws.Length; j++)
                {
                    string k = ws[i] + Sep + ws[j];
                    edge[k] = edge.GetValueOrDefault(k) + Math.Min(fillers[ws[i]], fillers[ws[j]]);
                }
        }
        return edge;
    }

    /// Group slot-mates into candidate paradigm classes via MUTUAL-k-NN connected components on the slot-mate
    /// weight. Raw components over-merge ("the ___" bridges every noun into one blob); mutual-kNN keeps an edge
    /// only when EACH endpoint ranks the other in its top-k, so tight intra-class evidence survives and the loose
    /// cross-class bridge is dropped. Deterministic (sorted throughout). Returns classes of ≥2 members.
    public static List<List<string>> MintCandidates(Dictionary<string, int> edge, int k = 3, int minWeight = 2)
    {
        var nbrW = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (key, w) in edge)
        {
            if (w < minWeight) continue;
            int s = key.IndexOf(Sep);
            string a = key[..s], b = key[(s + 1)..];
            (nbrW.TryGetValue(a, out var na) ? na : nbrW[a] = new(StringComparer.Ordinal))[b] = w;
            (nbrW.TryGetValue(b, out var nb) ? nb : nbrW[b] = new(StringComparer.Ordinal))[a] = w;
        }
        var topk = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (n, ws) in nbrW)
        {
            var ranked = ws.ToList();
            ranked.Sort((x, y) => x.Value != y.Value ? y.Value.CompareTo(x.Value) : StringComparer.Ordinal.Compare(x.Key, y.Key));
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Math.Min(k, ranked.Count); i++) set.Add(ranked[i].Key);
            topk[n] = set;
        }
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (n, ks) in topk)
            foreach (var m in ks)
                if (topk.TryGetValue(m, out var mk) && mk.Contains(n))
                {
                    (adj.TryGetValue(n, out var an) ? an : adj[n] = new(StringComparer.Ordinal)).Add(m);
                    (adj.TryGetValue(m, out var am) ? am : adj[m] = new(StringComparer.Ordinal)).Add(n);
                }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var comps = new List<List<string>>();
        foreach (var n in adj.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (seen.Contains(n)) continue;
            var comp = new List<string>();
            var stack = new Stack<string>(); stack.Push(n);
            while (stack.Count > 0)
            {
                var u = stack.Pop();
                if (!seen.Add(u)) continue;
                comp.Add(u);
                foreach (var v in adj[u]) if (!seen.Contains(v)) stack.Push(v);
            }
            if (comp.Count >= 2) { comp.Sort(StringComparer.Ordinal); comps.Add(comp); }
        }
        return comps;
    }

    /// The internal cohesion of a candidate — Σ slot-mate weight over all its member pairs (the sort key so the
    /// gate sees the tightest classes first).
    public static int InternalWeight(List<string> cand, Dictionary<string, int> edge)
    {
        int sum = 0;
        var ws = cand.ToArray(); Array.Sort(ws, StringComparer.Ordinal);
        for (int i = 0; i < ws.Length; i++)
            for (int j = i + 1; j < ws.Length; j++)
                sum += edge.GetValueOrDefault(ws[i] + Sep + ws[j]);
        return sum;
    }

    /// The two-part MDL of a slotted corpus (bits). `origCorpus` is the ORIGINAL (unsubstituted) — the residual is
    /// charged from it so the code stays lossless as slots nest. `slottedCorpus` carries the current substitutions.
    public static Mdl TwoPartMdl(IReadOnlyList<IReadOnlyList<string>> origCorpus,
                                 IReadOnlyList<IReadOnlyList<string>> slottedCorpus,
                                 Dictionary<string, string> memberToSlot)
    {
        var snap = WordRePair.Induce(slottedCorpus);
        double lg = Math.Log2(snap.NAlpha);

        // data — Shannon bits of the compressed slotted stream (sorted symbol census → order-stable float sum).
        var counts = new Dictionary<int, int>();
        long tot = 0;
        foreach (var st in snap.Compressed) foreach (var x in st) { counts[x] = counts.GetValueOrDefault(x) + 1; tot++; }
        double data = 0;
        if (tot > 0) foreach (var sym in counts.Keys.OrderBy(x => x)) data += counts[sym] * -Math.Log2((double)counts[sym] / tot);

        double grammar = snap.Rules.Count * 2 * lg;

        // slotdef — charge only the active ownership closure for this window.  The
        // standing paradigm can contain thousands of historical slots; charging every
        // one on every recency window made a stale seed permanently underwater.  A
        // live leaf activates its direct slot and every super-slot above it.  Nested
        // definitions remain whole (their member lists are required for lossless decode).
        var slotMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (member, slot) in memberToSlot)
            (slotMembers.TryGetValue(slot, out var s) ? s : slotMembers[slot] = new(StringComparer.Ordinal)).Add(member);
        var activeSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sentence in origCorpus)
            foreach (string token in sentence)
                Activate(token);
        foreach (var sentence in slottedCorpus)
            foreach (string token in sentence)
                if (token.StartsWith('[')) Activate(token);
        int members = 0;
        foreach (string slot in activeSlots)
            if (slotMembers.TryGetValue(slot, out var directMembers)) members += directMembers.Count;
        double slotdef = members * lg;

        void Activate(string token)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (memberToSlot.TryGetValue(token, out string? slot) && seen.Add(token))
            {
                activeSlots.Add(slot);
                token = slot;
            }
        }

        // residual — the within-TOP-slot member-choice each substitution deferred (exact, lossless).
        var topFreq = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var s in origCorpus)
            foreach (var t in s)
            {
                string top = ResolveTop(t, memberToSlot);
                if (top != t)
                    (topFreq.TryGetValue(top, out var f) ? f : topFreq[top] = new(StringComparer.Ordinal))[t] =
                        (topFreq[top].TryGetValue(t, out var c) ? c : 0) + 1;
            }
        double residual = 0;
        foreach (var top in topFreq.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var fc = topFreq[top]; long ftot = 0; foreach (var v in fc.Values) ftot += v;
            foreach (var t in fc.Keys.OrderBy(x => x, StringComparer.Ordinal)) residual += fc[t] * -Math.Log2((double)fc[t] / ftot);
        }

        return new Mdl(data + grammar + slotdef + residual, data, grammar, slotdef, residual, snap.Rules.Count);
    }

    private static string ResolveTop(string sym, Dictionary<string, string> m2s)
    {
        while (m2s.TryGetValue(sym, out var s)) sym = s;
        return sym;
    }

    /// One iteration's telemetry — new/gated slots, the tower depth reached, the ΔMDL the pass earned, and the
    /// held-out abstract-rule coverage (the composition read: an abstract slot-spanning rule firing on mostly-novel
    /// held-out combinations = generalization, which frozen single-pass Re-Pair scores at 0%).
    public readonly record struct GrowthRow(
        int It, int NewSlots, int Rejected, int TotalSlots, int Depth, double Mdl, double DMdl,
        double Data, double Residual, double HeldoutRatio, double HeldoutAbstract, int PartialRejected = 0);

    /// Typed phase receipt for one sleep pass.  The split keeps a cheap tokenize/growth
    /// wall from being mistaken for the (usually much larger) MDL or mint wall.
    public readonly record struct AntiUnifyTiming(
        double TokenizeMs, double GrowthMs, double FlatMdlMs, double SlottedMdlMs, double MintMs,
        double BaseVisitMs, double BaseCopyMs, double RuleExpansionMs, double OverlayVisitMs);

    /// Distinct MDL contracts for one sleep window: marginal gain is the staged
    /// candidate admission signal; absolute gain is the final current-window code
    /// advantage against the flat model.  A positive marginal gain may still be
    /// rejected when the standing seed remains underwater in absolute terms.
    public readonly record struct AntiUnifyMdlReceipt(double MarginalGain, double AbsoluteGain, bool Committed);

    /// THE GROWTH LOOP (the circulation, MDL-gated). Induce → anti-unify → gate each candidate by ΔMDL → mint the
    /// accepted → substitute → re-induce → iterate until nothing pays. `heldout` (may be empty) drives the
    /// composition read only. Returns the per-iteration rows + the discovered paradigm. Deterministic.
    /// `seed` non-null = GROW an existing paradigm: the corpus is pre-substituted
    /// with the seed's slots (so discovery continues ON TOP of the standing tower — meta-slots keep climbing) and
    /// new slots extend a staged clone under its persistent name/birth counters; the caller commits that clone only
    /// after the current-window absolute MDL receipt pays. Null = fresh discovery (the standalone study verbs).
    public static (List<GrowthRow> Rows, Paradigm Model) GrowthLoop(
        IReadOnlyList<string[]> train, IReadOnlyList<string[]> heldout,
        int maxIter = 6, int k = 3, int minWeight = 2, double mdlFloor = 1.0, int maxCand = 8, Paradigm? seed = null)
    {
        // Growth is staged.  A sleep window may show positive marginal candidates
        // while the standing model is underwater on the window as a whole; callers
        // decide whether to commit this clone after the absolute MDL receipt.
        var model = seed?.CloneForGrowth() ?? new Paradigm();
        var corpus = new List<List<string>>(train.Count);
        foreach (var s in train) corpus.Add(model.MemberToSlot.Count == 0 ? new List<string>(s) : ApplySlots(s, model.MemberToSlot));
        int slotCounter = model.NextName;
        var rows = new List<GrowthRow>();
        double mdlPrev = TwoPartMdl(train, corpus, model.MemberToSlot).Total;

        for (int it = 0; it < maxIter; it++)
        {
            var snap = WordRePair.Induce(corpus);
            var edge = Edges(snap.Yield);
            var cands = MintCandidates(edge, k, minWeight);
            // tightest first — weight precomputed once per candidate (the comparator recomputed the O(m²)
            // InternalWeight pair-sum on EVERY comparison); same comparator decisions ⇒ same order.
            var ranked = new List<(int Weight, List<string> Cand)>(cands.Count);
            foreach (var c in cands) ranked.Add((InternalWeight(c, edge), c));
            ranked.Sort((x, y) => y.Weight.CompareTo(x.Weight));
            cands.Clear();
            foreach (var (_, c) in ranked) cands.Add(c);

            // Admissions are transactional and sequential.  Every accepted candidate is
            // priced against the corpus produced by the previous accepted candidate; a
            // frozen batch baseline lets mutually-conflicting candidates all claim the
            // same savings, then collapses to zero aggregate ΔMDL after substitution.
            double mdl0 = mdlPrev;
            var accepted = new List<(string Name, List<string> Members, double Gain)>();
            int gated = 0;
            int partialRejected = 0;
            foreach (var cand in cands.Take(maxCand))
            {
                if (IsPartiallyOwnedCandidate(cand, model)) { partialRejected++; gated++; continue; }
                bool allSlotted = cand.All(model.MemberToSlot.ContainsKey);
                if (allSlotted) continue;
                bool meta = cand.All(w => w.StartsWith('['));
                string name = meta ? $"[[S{slotCounter}]]" : $"[S{slotCounter}]";
                var candSet = new HashSet<string>(cand, StringComparer.Ordinal);
                var testCorpus = new List<List<string>>(corpus.Count);
                foreach (var s in corpus)
                {
                    var row = new List<string>(s.Count);
                    foreach (var t in s) row.Add(candSet.Contains(t) ? name : t);
                    testCorpus.Add(row);
                }
                var testM2s = new Dictionary<string, string>(model.MemberToSlot, StringComparer.Ordinal);
                foreach (var w in cand) testM2s[w] = name;
                double gain = mdl0 - TwoPartMdl(train, testCorpus, testM2s).Total;
                gated++;
                if (gain <= mdlFloor) continue;                         // the name is not consumed on rejection

                // Commit this candidate immediately.  The next candidate sees both the
                // updated member→slot map and the substituted corpus, so its MDL is priced
                // against the post-previous-candidate state.
                slotCounter++;
                accepted.Add((name, cand, gain));
                model.BirthSlot(name, cand);
                for (int i = 0; i < corpus.Count; i++)
                {
                    var row = corpus[i];
                    for (int j = 0; j < row.Count; j++)
                        if (candSet.Contains(row[j])) row[j] = name;
                }
                mdl0 -= gain;
            }

            var m = TwoPartMdl(train, corpus, model.MemberToSlot);
            double dMdl = mdlPrev - m.Total; mdlPrev = m.Total;
            var (hoRatio, hoAbs) = heldout.Count > 0 ? HeldoutEval(corpus, heldout, model.MemberToSlot) : (1.0, 0.0);
            rows.Add(new GrowthRow(it, accepted.Count, gated - accepted.Count, model.SlotCount, model.MaxDepth(),
                m.Total, dMdl, m.Data, m.Residual, hoRatio, hoAbs, partialRejected));
            if (accepted.Count == 0) break;
        }
        model.NextName = slotCounter;   // persist the name counter (rejected candidates reclaimed theirs above)
        return (rows, model);
    }

    /// Apply the discovered slots to a sentence (chase the tower to a fixpoint — a member wears its slot, that slot
    /// wears its super-slot, …).
    private static List<string> ApplySlots(IReadOnlyList<string> sentence, Dictionary<string, string> m2s)
    {
        var s = new List<string>(sentence);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < s.Count; i++)
                if (m2s.TryGetValue(s[i], out var slot)) { s[i] = slot; changed = true; }
        }
        return s;
    }

    /// Induce on SLOTTED train, apply to SLOTTED held-out: `ratio` = compression (orig/compressed); `absCov` =
    /// fraction of held-out tokens absorbed by an ABSTRACT rule (a rule whose yield contains a slot) — the abstract
    /// rule firing on a mostly-novel held-out combination IS composition (what the frozen single pass cannot do).
    private static (double Ratio, double AbsCov) HeldoutEval(
        IReadOnlyList<IReadOnlyList<string>> trainSlotted, IReadOnlyList<string[]> held, Dictionary<string, string> m2s)
    {
        var heldSlotted = new List<List<string>>(held.Count);
        foreach (var s in held) heldSlotted.Add(ApplySlots(s, m2s));

        var snap = WordRePair.Induce(trainSlotted);
        // abstract rule = a rule whose full yield contains a slot token (starts with '[')
        var abstractRule = new bool[snap.Rules.Count];
        for (int i = 0; i < snap.Rules.Count; i++)
            foreach (var w in snap.Yield[i]) if (w.StartsWith('[')) { abstractRule[i] = true; break; }

        // cover each held-out sentence greedily by the induced rules (longest yield first), count absorbed tokens.
        var order = Enumerable.Range(0, snap.Rules.Count).OrderByDescending(i => snap.Yield[i].Length).ToArray();
        long orig = 0, comp = 0, absorbed = 0;
        foreach (var sent in heldSlotted)
        {
            orig += sent.Count;
            int i = 0;
            while (i < sent.Count)
            {
                int bestLen = 0, bestRule = -1;
                foreach (var r in order)
                {
                    var y = snap.Yield[r];
                    if (y.Length > sent.Count - i || y.Length <= bestLen) continue;
                    bool match = true; for (int t = 0; t < y.Length; t++) if (sent[i + t] != y[t]) { match = false; break; }
                    if (match) { bestLen = y.Length; bestRule = r; break; }   // order is longest-first, so first match is longest
                }
                comp++;
                if (bestRule >= 0) { if (abstractRule[bestRule]) absorbed += bestLen; i += bestLen; }
                else i++;
            }
        }
        return (comp > 0 ? (double)orig / comp : 1.0, orig > 0 ? (double)absorbed / orig : 0.0);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  RECOVERY — purity + concentration of the discovered slots vs the GOLD classes (kill-line (a))
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// (#slots, purity%, #gold-classes-concentrated, #gold-classes). purity catches MERGING (distinct golds in one
    /// slot); concentration catches FRAGMENTATION (one gold shattered across slots). Clean recovery = high both.
    /// `wordToSlot` resolves each gold word to its TOP slot (the tower root) — the compounding-aware recovery.
    public static (int Slots, int Purity, int Concentrated, int GoldClasses) Recovery(
        Dictionary<string, string> wordToSlot, Dictionary<string, string> gold)
    {
        // slot → gold-class breakdown (only gold words)
        var comp = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (w, slot) in wordToSlot)
            if (gold.TryGetValue(w, out var g))
                (comp.TryGetValue(slot, out var c) ? c : comp[slot] = new(StringComparer.Ordinal))[g] =
                    (comp[slot].TryGetValue(g, out var n) ? n : 0) + 1;
        var goldClasses = new HashSet<string>(gold.Values, StringComparer.Ordinal);
        if (comp.Count == 0) return (0, 100, 0, goldClasses.Count);

        long total = 0, pure = 0;
        var goldTotal = new Dictionary<string, int>(StringComparer.Ordinal);
        var goldMax = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in comp.Values)
        {
            int mx = 0; foreach (var (g, n) in v) { total += n; goldTotal[g] = goldTotal.GetValueOrDefault(g) + n; goldMax[g] = Math.Max(goldMax.GetValueOrDefault(g), n); mx = Math.Max(mx, n); }
            pure += mx;
        }
        int concentrated = 0; foreach (var g in goldTotal.Keys) if (goldMax[g] >= 0.6 * goldTotal[g]) concentrated++;
        return (comp.Count, (int)Math.Round(100.0 * pure / total), concentrated, goldTotal.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE CHAIN CORPUS — the synthetic testbed with KNOWN paradigm structure (overlapping frames)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    // The overlapping-frame corpus (loopy.build_chain): PERCEIVE and MOTION both live in "the {AN} was ___ the ___",
    // so distributional clustering FRAGMENTS them — but anti-unification, which reads slot-substitutability, gives
    // WHOLE classes. The clean test that the gate crystallizes synonymy without shattering it.
    private static readonly string[] Animal   = "bird cat dog fox owl wolf deer hare".Split(' ');
    private static readonly string[] Place    = "barn field forest meadow cave hill marsh grove".Split(' ');
    private static readonly string[] Perceive = "watching gazing observing monitoring eyeing studying inspecting scanning".Split(' ');
    private static readonly string[] Motion   = "running walking crawling sprinting strolling trotting prowling roaming".Split(' ');
    private static readonly string[] Emotion  = "happy sad angry calm anxious weary restless wary".Split(' ');

    /// The gold word→class map (fillers f{i} are deliberately absent — not gold; the noise the gate must reject).
    public static Dictionary<string, string> ChainGold()
    {
        var g = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in Animal) g[w] = "ANIMAL";
        foreach (var w in Place) g[w] = "PLACE";
        foreach (var w in Perceive) g[w] = "PERCEIVE";
        foreach (var w in Motion) g[w] = "MOTION";
        foreach (var w in Emotion) g[w] = "EMOTION";
        return g;
    }

    /// Build `n` CHAIN sentences (deterministic LCG — the Vow). Clauses: "the {AN} was {PE} the {AN}" (34%),
    /// "the {AN} was {MO} the {PL}" (34%), "a {EM} {AN} appeared" (rest); 0-2 filler words bracket each clause.
    public static List<string[]> ChainCorpus(int n, ulong seed, int nFiller = 120)
    {
        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
        int Rand(int lo, int hi) => lo + (int)(U() * (hi - lo + 1));
        string Pick(string[] a) => a[(int)(U() * a.Length)];
        void Fillers(List<string> into) { int c = Rand(0, 2); for (int i = 0; i < c; i++) into.Add("f" + (int)(U() * nFiller)); }

        var corpus = new List<string[]>(n);
        for (int s = 0; s < n; s++)
        {
            var sent = new List<string>();
            int clauses = Rand(1, 2);
            for (int c = 0; c < clauses; c++)
            {
                Fillers(sent);
                double r = U();
                if (r < 0.34) { sent.Add("the"); sent.Add(Pick(Animal)); sent.Add("was"); sent.Add(Pick(Perceive)); sent.Add("the"); sent.Add(Pick(Animal)); }
                else if (r < 0.68) { sent.Add("the"); sent.Add(Pick(Animal)); sent.Add("was"); sent.Add(Pick(Motion)); sent.Add("the"); sent.Add(Pick(Place)); }
                else { sent.Add("a"); sent.Add(Pick(Emotion)); sent.Add(Pick(Animal)); sent.Add("appeared"); }
            }
            Fillers(sent);
            corpus.Add(sent.ToArray());
        }
        return corpus;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE KILL-LINE VERB — the +7675-bit-analog readout on the port's own terms
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// usage: antiunify [--n N] [--held H] [--iter I] [--cand C] [--seed HEX] [--budget BITS]
    ///   The (a) readout: minting PAYS thousands of bits at ≥95% slot purity on the CHAIN corpus (overlapping
    ///   frames — the hard case). --budget adds the (b) scarcity read: at a tight budget, antiunify holds the
    ///   GENERATIVE slot structure the literal-only grammar must evict.
    public static int Run(string[] args)
    {
        int n = Args.Int(args, "--n", 500);
        int held = Args.Int(args, "--held", 200);
        int iter = Args.Int(args, "--iter", 6);
        int cand = Args.Int(args, "--cand", 8);
        ulong seed = Args.Seed(args, "--seed", 0xC0117011UL);
        long budget = Args.Long(args, "--budget", 0);

        var gold = ChainGold();
        var train = ChainCorpus(n, seed);
        var heldout = ChainCorpus(held, seed + 1000);

        var run = Cogito.Run.New("antiunify");
        Trace.Note($"antiunify · CHAIN corpus · {n} train + {held} held-out sentences · {gold.Values.Distinct().Count()} gold classes · {iter} iters · MDL-gated · no LLM · seed {seed:X}");
        Trace.Note("  induce Re-Pair → anti-unify rule yields → mint slot [S] IFF ΔMDL pays → substitute → re-induce");
        Trace.Note("");

        var (rows, model) = GrowthLoop(train, heldout, maxIter: iter, maxCand: cand);

        // the per-iteration growth curve
        Trace.Note("  it  +slot/gated  partial  depth  |  slots  ΔMDL bits  |  held-out: comp×  abstract-cov%");
        Trace.Note("  " + new string('─', 78));
        foreach (var r in rows)
            Trace.Note($"  {r.It,2}  {"+" + r.NewSlots + " / " + r.Rejected,10}  {r.PartialRejected,7}  {r.Depth,5}  |  {r.TotalSlots,4}  {r.DMdl,+10:F0}  |  {r.HeldoutRatio,10:F2}  {100 * r.HeldoutAbstract,10:F0}%");
        bool overlapTransaction = VerifyTransactionalOverlap();
        Trace.Note($"  overlap transaction · partial-owned candidates={(overlapTransaction ? "rejected" : "NOT-OBSERVED")} · ownership={(overlapTransaction ? "stable" : "BROKEN")} · {(overlapTransaction ? "PASS" : "FAIL")}");
        if (!overlapTransaction) return 1;
        bool staleRollback = VerifyStaleOrphanRollback();
        Trace.Note($"  stale-orphan rollback · marginal-positive/absolute-negative={(staleRollback ? "observed" : "NOT-OBSERVED")} · seed/publication={(staleRollback ? "unchanged" : "MUTATED")} · {(staleRollback ? "PASS" : "FAIL")}");
        if (!staleRollback) return 1;
        Trace.Note("");

        // (d) the two-part MDL readout: frozen Re-Pair vs the growth loop — does minting PAY?
        var flat = TwoPartMdl(train, train, new Dictionary<string, string>(StringComparer.Ordinal));
        var slottedCorpus = new List<List<string>>(train.Count);
        foreach (var s in train) slottedCorpus.Add(ApplySlots(s, model.MemberToSlot));
        var slotted = TwoPartMdl(train, slottedCorpus, model.MemberToSlot);
        double pay = flat.Total - slotted.Total;
        Trace.Note("  ── two-part MDL (lossless), frozen Re-Pair vs the growth loop ──");
        Trace.Note($"            {"data",10} {"grammar",10} {"slotdef",9} {"residual",10} {"TOTAL",11}");
        Trace.Note($"    frozen  {flat.Data,10:F0} {flat.Grammar,10:F0} {0,9} {0,10} {flat.Total,11:F0}");
        Trace.Note($"    slotted {slotted.Data,10:F0} {slotted.Grammar,10:F0} {slotted.SlotDef,9:F0} {slotted.Residual,10:F0} {slotted.Total,11:F0}");
        Trace.Note($"    ⇒ minting PAYS {pay:F0} bits ({(flat.Total > 0 ? 100 * pay / flat.Total : 0):+0.0}%) — abstraction beats memorization BY compression, residual accounted (lossless).");
        Trace.Note("");

        // (a) recovery — purity + concentration of the resolved slots vs gold (the crystallize-without-fragmentation read)
        var wordTop = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in gold.Keys) if (model.MemberToSlot.ContainsKey(w)) wordTop[w] = model.ResolveTop(w);
        var (slots, purity, conc, goldN) = Recovery(wordTop, gold);
        Trace.Note("  ── recovery vs gold (resolved to the top slot) ──");
        Trace.Note($"    {slots} slots · purity {purity}% · {conc}/{goldN} gold classes concentrated (≥60% in one slot)");
        Trace.Note("    the discovered slots (gold breakdown):");
        foreach (var (name, mem) in model.SlotMembers.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var golds = mem.Where(m => gold.ContainsKey(m)).Select(m => gold[m]).ToList();
            string tag = golds.Count == 0 ? "(slots/filler)" : string.Join(",", golds.GroupBy(g => g).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}"));
            Trace.Note($"      {name,-8} = {{{string.Join(" ", mem.Take(10))}{(mem.Count > 10 ? " …" : "")}}}  → {tag}");
        }
        Trace.Note("");

        // Integration receipt: the same standing paradigm is presented to MintSlots twice.
        // The second pass must be a true no-op (not another copy of the class layer), and
        // the resulting publication must remain readable by its duplicate-ID guard.
        var mintTape = new Tape();
        foreach (var sentence in train.Take(Math.Min(train.Count, 64)))
            mintTape.Append(Encoding.UTF8.GetBytes(string.Join(' ', sentence)), "antiunify", Provenances.Replay);
        RePairResult mintBase = Engine.Induce(mintTape).Result;
        AntiUnifyPass firstMint = MintSlots(mintBase, model);
        AntiUnifyPass secondMint = MintSlots(firstMint.Grammar, model);
        RePairResult freshBase = Engine.Induce(mintTape).Result;
        AntiUnifyPass freshMint = MintSlots(freshBase, model);
        bool overlayAppend = VerifyOverlayBaseAppend(mintBase, firstMint, model);
        int duplicateIDs = firstMint.Grammar.Rules.Length - firstMint.Grammar.Rules.Select(static rule => rule.Id).Distinct().Count();
        bool publicationReadable = true;
        try
        {
            RePairResult firstGrammar = firstMint.Grammar;
            InstallRevision publication = InstallRevision.FromRePair(new GrammarRevisionID(1), GrammarRevisionID.Zero, in firstGrammar);
            _ = publication.ReconstructPublishedBytes();
        }
        catch (InvalidDataException) { publicationReadable = false; }
        bool freshStable = freshMint.SlottedRules == 0
            && ReferenceEquals(freshMint.Grammar.Rules, freshBase.Rules)
            && ReferenceEquals(freshMint.Grammar.Compressed, freshBase.Compressed);
        bool secondNoOp = secondMint.SlottedRules == 0
            && ReferenceEquals(secondMint.Grammar.Rules, firstMint.Grammar.Rules)
            && secondMint.Operations.ReusedInput
            && secondMint.Operations.BaseRulesVisited == 0
            && secondMint.Operations.RuleExpansions == 0;
        bool mintDelta = secondNoOp && freshStable && overlayAppend && duplicateIDs == 0 && publicationReadable;
        Trace.Note($"  antiunify Δ-mint receipt · first=+{firstMint.SlottedRules} second=+{secondMint.SlottedRules} fresh=+{freshMint.SlottedRules} stable={(freshStable ? "identity" : "BROKEN")} no-op={(secondNoOp ? "identity/zero-scan" : "BROKEN")} overlay-append={(overlayAppend ? "yes" : "BROKEN")} duplicate_rule_ids={duplicateIDs} publication={(publicationReadable ? "readable" : "BROKEN")} · {(mintDelta ? "PASS" : "FAIL")}");
        Trace.Note($"  antiunify mint timing · base={firstMint.Operations.BaseVisitMs:F2}ms copy={firstMint.Operations.BaseCopyMs:F2}ms expand={firstMint.Operations.RuleExpansionMs:F2}ms overlay={firstMint.Operations.OverlayVisitMs:F2}ms");
        if (!mintDelta) return 1;

        // land the durable readout
        var sb = new StringBuilder("it\tnew_slots\trejected\tpartial_rejected\ttotal_slots\tdepth\tmdl\td_mdl\tdata\tresidual\tho_ratio\tho_abstract\n");
        foreach (var r in rows) sb.AppendLine($"{r.It}\t{r.NewSlots}\t{r.Rejected}\t{r.PartialRejected}\t{r.TotalSlots}\t{r.Depth}\t{r.Mdl:F1}\t{r.DMdl:F1}\t{r.Data:F1}\t{r.Residual:F1}\t{r.HeldoutRatio:F4}\t{r.HeldoutAbstract:F4}");
        run.Write("growth.tsv", sb.ToString());
        run.Write("verdict.txt", $"pay={pay:F0} bits ({(flat.Total > 0 ? 100 * pay / flat.Total : 0):F1}%) · purity={purity}% · concentrated={conc}/{goldN} · slots={slots} · depth={model.MaxDepth()}\n");

        // the verdict
        Trace.Note("  ── VERDICT ──");
        Trace.Note($"    (a) crystallize synonymy WITHOUT fragmentation : {(purity >= 95 && conc >= goldN - 1 ? "YES" : "partial")} (purity {purity}%, {conc}/{goldN} concentrated)");
        Trace.Note($"    (d) the intrinsic signal PAYS                  : {(pay > 1 ? "YES" : "no")} ({pay:F0} bits — the ΔMDL gate chose generalize over memorize, losslessly)");
        Trace.Note($"    (b) composition — abstract rules fire held-out : {(rows.Count > 0 && rows[^1].HeldoutAbstract > 0 ? $"YES ({100 * rows[^1].HeldoutAbstract:F0}% held-out coverage by slot-spanning rules)" : "no")} (frozen single-pass Re-Pair = 0%)");

        if (budget > 0) BudgetScarcity(train, model, budget);
        return 0;
    }

    internal static bool IsPartiallyOwnedCandidate(IReadOnlyList<string> candidate, Paradigm model)
    {
        int owned = 0;
        foreach (string member in candidate) if (model.MemberToSlot.ContainsKey(member)) owned++;
        return owned != 0 && owned != candidate.Count;
    }

    private static bool VerifyTransactionalOverlap()
    {
        var seed = new Paradigm { NextName = 1 };
        seed.BirthSlot("[S0]", ["cat", "dog"]);
        string[][] train =
        [
            ["the", "cat", "runs"], ["the", "dog", "runs"], ["the", "fox", "runs"],
            ["the", "cat", "jumps"], ["the", "dog", "jumps"], ["the", "fox", "jumps"],
        ];
        bool partialRejected = IsPartiallyOwnedCandidate(["cat", "fox"], seed);
        _ = GrowthLoop(train, Array.Empty<string[]>(), maxIter: 1, maxCand: 16, seed: seed);
        bool ownershipStable = seed.MemberToSlot["cat"] == "[S0]" && !seed.MemberToSlot.ContainsKey("fox");
        return partialRejected && ownershipStable;
    }

    private static bool VerifyStaleOrphanRollback()
    {
        var seed = new Paradigm();
        var staleMembers = new List<string> { "cat" };
        for (int i = 0; i < 512; i++) staleMembers.Add($"orphan{i}");
        seed.BirthSlot("[S0]", staleMembers);
        seed.NextName = 1;
        var tape = new Tape();
        foreach (string[] sentence in ChainCorpus(80, 0xC0117011UL))
            tape.Append(Encoding.UTF8.GetBytes(string.Join(' ', sentence)), "stale-orphan", Provenances.Replay);
        RePairResult grammar = Engine.Induce(tape).Result;
        int slotsBefore = seed.SlotMembers.Count;
        int nextNameBefore = seed.NextName;
        string ownerBefore = seed.MemberToSlot["cat"];
        AntiUnifyPass pass = Consolidate(tape, grammar, seed, maxIter: 2, maxCand: 8);
        Trace.Note($"  stale-orphan receipt · marginal={pass.Mdl.MarginalGain:F3} absolute={pass.Mdl.AbsoluteGain:F3} committed={(pass.Mdl.Committed ? 1 : 0)} slotted={pass.SlottedRules} bits={pass.BitsSaved}");
        var journal = new Journal();
        journal.Consolidation(0, "antiunify · " + pass.FormatJournalNote());
        bool durableReceipt = journal.ResidentLines.Count == 1
            && journal.ResidentLines[0].Contains("mdl marginal=", StringComparison.Ordinal)
            && journal.ResidentLines[0].Contains("absolute=", StringComparison.Ordinal)
            && journal.ResidentLines[0].Contains("committed=0", StringComparison.Ordinal)
            && journal.ResidentLines[0].Contains("ops base_visit=", StringComparison.Ordinal)
            && !journal.ResidentLines[0].Contains("ms tokenize=", StringComparison.Ordinal);
        Trace.Note($"  stale-orphan durable journal · deterministic signed-pay={(durableReceipt ? "present" : "MISSING")} · {(durableReceipt ? "PASS" : "FAIL")}");
        return pass.Mdl.MarginalGain > 0
            && pass.Mdl.AbsoluteGain < 0
            && !pass.Mdl.Committed
            && pass.SlottedRules == 0
            && ReferenceEquals(pass.Grammar.Rules, grammar.Rules)
            && seed.SlotMembers.Count == slotsBefore
            && seed.NextName == nextNameBefore
            && seed.MemberToSlot["cat"] == ownerBefore
            && durableReceipt;
    }

    private static bool VerifyOverlayBaseAppend(RePairResult baseGrammar, AntiUnifyPass first, Paradigm model)
    {
        if (first.Grammar.Rules.Length <= baseGrammar.Rules.Length) return true;
        var rules = (GrammarRule[])baseGrammar.Rules.Clone();
        var pattern = new[] { new Symbol((uint)0xF9), new Symbol((uint)0xFA) };
        var appended = new GrammarRule(GrammarRule.ComputeId(pattern), pattern, new Mbits(16_000));
        if (rules.Any(rule => rule.Id.Equals(appended.Id))) return true;
        Array.Resize(ref rules, rules.Length + 1);
        rules[^1] = appended;
        var grown = new RePairResult(rules, baseGrammar.Compressed, baseGrammar.TotalSavings, baseGrammar.AlphabetSize);
        AntiUnifyPass reminted = MintSlots(grown, model);
        bool idsUnique = reminted.Grammar.Rules.Select(static rule => rule.Id).Distinct().Count() == reminted.Grammar.Rules.Length;
        GrammarSnapshot baseSnapshot = GrammarSnapshot.FromRePair(new GrammarRevisionID(41), in baseGrammar);
        RePairResult firstGrammar = first.Grammar;
        GrammarOverlay? priorOverlay = GrammarOverlay.TryFromComposed(baseSnapshot, in firstGrammar);
        if (priorOverlay is null) return false;
        GrammarSnapshot grownSnapshot = GrammarSnapshot.FromRePair(new GrammarRevisionID(42), in grown);
        GrammarOverlay? reusedOverlay = GrammarOverlay.TryFromComposed(grownSnapshot, in grown, priorOverlay);
        InstallRevision noOpInstallRevision = new InstallRevision(grownSnapshot, GrammarDelta.CreateEmpty(grownSnapshot.Revision)).WithOverlay(reusedOverlay);
        bool publicationNoOp = ReferenceEquals(reusedOverlay, priorOverlay)
            && noOpInstallRevision.Delta.AddedRules.Length == 0
            && noOpInstallRevision.Delta.SequenceEdits.Length == 0
            && noOpInstallRevision.Reset == GrammarResetKinds.None
            && priorOverlay.ComposeCount == 0;
        return reminted.SlottedRules == 0
            && ReferenceEquals(reminted.Grammar.Rules, grown.Rules)
            && reminted.Operations.ReusedInput
            && reminted.Operations.BaseRulesVisited == 0
            && publicationNoOp
            && idsUnique;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  (b) BUDGET SCARCITY — antiunify-ON holds MORE structure under the SAME bits than antiunify-OFF
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// (b) THE GENERALIZATION-UNDER-SCARCITY HEADLINE — the hypothesis that a tight budget converts the
    /// compressor into a generalizer, read as a two-part MDL table. Both codes reconstruct the SAME corpus LOSSLESSLY;
    /// the slotted code needs `bitsSaved` FEWER bits (the paradigm generalization is pure compression). So at any
    /// budget in the window [slotted.Total, flat.Total) the SLOTTED model fits the whole corpus and the FLAT
    /// (literal-only) model does NOT — it must drop structure. The byte-grammar mint below confirms the generative
    /// slot-classes actually enter the working set.
    private static void BudgetScarcity(IReadOnlyList<string[]> train, Paradigm model, long budgetBits)
    {
        var flat = TwoPartMdl(train, train, new Dictionary<string, string>(StringComparer.Ordinal));
        var slottedCorpus = new List<List<string>>(train.Count);
        foreach (var s in train) slottedCorpus.Add(ApplySlots(s, model.MemberToSlot));
        var slotted = TwoPartMdl(train, slottedCorpus, model.MemberToSlot);
        long saved = (long)(flat.Total - slotted.Total);

        // the byte grammar carries the classes into the working set (the sparkline the drive emits per sleep).
        var tape = new Tape();
        foreach (var s in train) tape.Append(Encoding.UTF8.GetBytes(string.Join(' ', s)), "corpus", Provenances.Replay);   // synthetic harness sentences — machine-generated, not world contact (the census: every Append declares its epistemics)
        var (_, _, g) = Engine.Induce(tape);
        var mint = MintSlots(g, model);

        Trace.Note("");
        Trace.Note($"  ── (b) GENERALIZATION UNDER SCARCITY — lossless two-part MDL, SAME corpus (budget arg {budgetBits} bits) ──");
        Trace.Note($"    model                bits (lossless)   fits a budget of…");
        Trace.Note($"    flat  (literal only) {flat.Total,15:F0}   ≥ {flat.Total,0:F0}");
        Trace.Note($"    slotted (paradigm)   {slotted.Total,15:F0}   ≥ {slotted.Total,0:F0}");
        Trace.Note($"    ⇒ at any budget in [{slotted.Total:F0}, {flat.Total:F0}) the SLOTTED model reconstructs the WHOLE corpus and the");
        Trace.Note($"      literal-only model CANNOT — it must drop {saved} bits of structure. The budget selects the generalizer.");
        Trace.Note($"    byte grammar: {mint.SlottedRules} paradigm class-rules minted into the working set (Reconstruct-safe; the generative");
        Trace.Note($"      structure OFF cannot express). Boundary: v1 mints the CLASSES; wiring skeleton rules to physically demote the");
        Trace.Note($"      subsumed byte literals (so the byte surface-budget itself drops) is the co-reference-renderer follow-up.");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE DRIVE INTEGRATION — mint the discovered slots into the byte grammar (the SLEEP night shift)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// One anti-unify consolidation pass's outcome: the byte grammar with the slot structure minted in, how many
    /// slot-rules were added, and the word-level bits the generalization saved (the sparkline readout).
    public readonly record struct AntiUnifyMintOperations(
        int BaseRulesVisited,
        int BaseRulesCopied,
        int RuleExpansions,
        int OverlayRulesVisited,
        bool ReusedInput,
        bool Rebuilt,
        double BaseVisitMs = 0,
        double BaseCopyMs = 0,
        double RuleExpansionMs = 0,
        double OverlayVisitMs = 0,
        int ImageRulesCopied = 0);

    public readonly record struct AntiUnifyPass(
        RePairResult Grammar,
        int SlottedRules,
        long BitsSaved,
        AntiUnifyMintOperations Operations = default,
        AntiUnifyTiming Timing = default,
        AntiUnifyMdlReceipt Mdl = default)
    {
        // The journal note carries ONLY deterministic fields (slot-rules, bits, MDL verdict, structural op counts)
        // — the journal.log is a deterministic curve artifact (Cortex.Loop.cs). Wall-clock timing rides the
        // `antiunify.timing` VTR boundary at the sleep-pass call site instead, never this note.
        public string FormatJournalNote()
            => $"+{SlottedRules} slot-rules · bits={BitsSaved} · mdl marginal={Mdl.MarginalGain:F3} absolute={Mdl.AbsoluteGain:F3} committed={(Mdl.Committed ? 1 : 0)} · ops base_visit={Operations.BaseRulesVisited} base_copy={Operations.BaseRulesCopied} image_copy={Operations.ImageRulesCopied} expand={Operations.RuleExpansions} overlay_visit={Operations.OverlayRulesVisited}";
    }

    /// THE SLEEP-PASS ENTRY. Tokenize a WINDOW of the tape's most recent spans into
    /// words, GROW the persistent paradigm over it (seeded — discovery continues on top of the standing tower, it
    /// never restarts), mint the WHOLE paradigm into the fresh BYTE grammar (SlotClass class-rules + slotted
    /// skeleton rules), and report the word-level bits the paradigm saves on the window. Generalization is
    /// night-shift work: slotted rules REPLACE clusters of literal surface memorizations → the budget breathes.
    /// THE PIN: the growth loop is ~10 word-Re-Pair inductions of its corpus per iteration — over the WHOLE
    /// tape that was 70-107s per sleep and growing with the tape forever (the measured wall that stalled the world
    /// run at step ~250); over a fixed recency window with the model persisted, the pass is O(window) FLAT and
    /// nothing already learned is lost. `model` null = fresh + `windowSpans` ≤ 0 = whole tape (the standalone
    /// study-verb behavior, unchanged). Deterministic: the window is an id range, the seed evolves monotonic counters.
    public static AntiUnifyPass Consolidate(Tape tape, RePairResult grammar, Paradigm? model = null,
        int windowSpans = 0, int maxIter = 4, int maxCand = 8)
    {
        long phaseStart = Stopwatch.GetTimestamp();
        IReadOnlyList<byte[]> spans;
        if (windowSpans > 0 && tape.NextId > windowSpans)
        {
            var w = new List<byte[]>(windowSpans);
            for (long v = tape.NextId - windowSpans; v < tape.NextId; v++)   // ids are monotonic — the window is the id range off the HIGH-WATER (residents ≠ ids under shedding; the shed recency guard keeps this range resident)
                if (tape.Resolve(new TapeEventID(v), out var s)) w.Add(s);
            spans = w;
        }
        else spans = tape.ResidentEventBytes;
        var corpus = Tokenize(spans);
        double tokenizeMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
        if (corpus.Count < 3) return new AntiUnifyPass(grammar, 0, 0, Timing: new AntiUnifyTiming(tokenizeMs, 0, 0, 0, 0, 0, 0, 0, 0));

        phaseStart = Stopwatch.GetTimestamp();
        var (rows, grown) = GrowthLoop(corpus, Array.Empty<string[]>(), maxIter, maxCand: maxCand, seed: model);
        double growthMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;

        // Absolute pay is the final code for THIS window, including the active
        // ownership closure.  It is deliberately distinct from the marginal gains
        // that admitted staged candidates against the standing seed.
        phaseStart = Stopwatch.GetTimestamp();
        var flat = TwoPartMdl(corpus, corpus, new Dictionary<string, string>(StringComparer.Ordinal));
        double flatMdlMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
        var slottedCorpus = new List<List<string>>(corpus.Count);
        foreach (var s in corpus) slottedCorpus.Add(ApplySlots(s, grown.MemberToSlot));
        phaseStart = Stopwatch.GetTimestamp();
        var slotted = TwoPartMdl(corpus, slottedCorpus, grown.MemberToSlot);
        double slottedMdlMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
        double absoluteGain = flat.Total - slotted.Total;
        double marginalGain = rows.Sum(row => row.DMdl);
        long bitsSaved = (long)Math.Round(absoluteGain);

        var baseTiming = new AntiUnifyTiming(tokenizeMs, growthMs, flatMdlMs, slottedMdlMs, 0, 0, 0, 0, 0);
        bool hasNewBirths = rows.Any(row => row.NewSlots > 0);
        if (!hasNewBirths || absoluteGain <= 0)
        {
            // The staged clone is intentionally discarded.  No counters, maps, mint
            // spine, or overlay image may change on a marginally-positive but
            // absolutely-underwater window.
            return new AntiUnifyPass(grammar, 0, bitsSaved, Timing: baseTiming,
                Mdl: new AntiUnifyMdlReceipt(marginalGain, absoluteGain, false));
        }

        phaseStart = Stopwatch.GetTimestamp();
        var mint = MintSlots(grammar, grown);
        double mintMs = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
        var op = mint.Operations;
        var timing = new AntiUnifyTiming(tokenizeMs, growthMs, flatMdlMs, slottedMdlMs, mintMs,
            op.BaseVisitMs, op.BaseCopyMs, op.RuleExpansionMs, op.OverlayVisitMs);
        if (model is not null) model.CommitFrom(grown);
        return new AntiUnifyPass(mint.Grammar, mint.SlottedRules, bitsSaved, op, timing,
            new AntiUnifyMdlReceipt(marginalGain, absoluteGain, true));
    }

    /// Tokenize tape spans into word sentences: a word = a maximal run of non-whitespace bytes; whitespace runs
    /// become their own single tokens so the skeleton (the literal glue between fillers) is preserved. Byte-native
    /// (no tokenizer model), deterministic. This is the word view anti-unification aligns; the byte grammar is
    /// untouched by it (the slots re-enter the byte grammar only through MintSlots).
    private static List<string[]> Tokenize(IReadOnlyList<byte[]> spans)
    {
        var corpus = new List<string[]>(spans.Count);
        foreach (var span in spans)
        {
            var words = new List<string>();
            int i = 0;
            while (i < span.Length)
            {
                bool ws = span[i] == (byte)' ' || span[i] == (byte)'\t';
                int j = i + 1;
                while (j < span.Length && (span[j] == (byte)' ' || span[j] == (byte)'\t') == ws) j++;
                words.Add(ws ? " " : Encoding.UTF8.GetString(span, i, j - i));
                i = j;
            }
            if (words.Count >= 2) corpus.Add(words.ToArray());
        }
        return corpus;
    }

    private static int CountMintAppendRules(Paradigm model, Dictionary<string, Symbol> surfaces,
        HashSet<RuleID> existingRuleIDs, bool carryStandingSlots)
    {
        var knownSurfaces = new HashSet<string>(surfaces.Keys, StringComparer.Ordinal);
        var knownSlots = new HashSet<string>(StringComparer.Ordinal);
        if (carryStandingSlots) knownSlots.UnionWith(model.SlotSymbols.Keys);
        int count = 0;
        foreach (string name in model.MintOrder)
        {
            if (existingRuleIDs.Contains(model.SlotRuleID(name))) continue;
            foreach (string member in model.SortedMembers[name])
            {
                if (knownSlots.Contains(member)) continue;
                if (model.SlotMembers.ContainsKey(member))
                    throw new InvalidDataException($"paradigm slot '{name}' precedes its child '{member}' in mint order");
                if (knownSurfaces.Add(member)) count++;
            }
            knownSlots.Add(name);
            count++;
        }
        return count;
    }

    /// Mint the paradigm into the BYTE grammar — APPEND-ONLY so the emission-order contract (nonterminal = 256+i,
    /// children before parents) is never broken: (1) a literal Expansion rule per surface a slot needs as a symbol,
    /// (2) a SlotClass class-rule per slot (Pattern = the member symbols; Reconstruct.Expand renders it as its
    /// representative member[0]), (3) skeleton rules "the [S] was" as Expansion rules whose Pattern references the
    /// class symbol. Existing rules keep their index. This is the generative structure that replaces literal
    /// surface hoarding — the generalization. Deterministic (surfaces + slots minted in sorted order).
    ///
    /// THE Δ-MINT (face 3e): each fresh grammar rematerializes the standing paradigm from
    /// the persisted slot names, while an unchanged grammar dedupes by those stable IDs.
    /// This preserves the generative layer across Loom re-induction without hashing
    /// rebased symbol indexes into a new identity on every sleep.
    internal static AntiUnifyPass MintSlots(RePairResult grammar, Paradigm model)
    {
        // Slot birth is append-only.  An unchanged composed image is a true identity
        // operation: no base walk, expansion, or overlay visit is allowed on this path.
        bool hasStandingOverlay = model.OverlayRuleIDs.Count != 0;
        bool hasNewSlots = false;
        foreach (string name in model.MintOrder)
        {
            if (!model.OverlayRuleIDs.Contains(model.SlotRuleID(name)))
            {
                hasNewSlots = true;
                break;
            }
        }
        // A pure Re-Pair re-induction commonly arrives without yesterday's side layer.
        // That is not a rebase: the binary prefix is still authoritative and the
        // persistent paradigm identity is sufficient to re-attach the overlay.
        bool inputCarriesOverlay = false;
        int inputOverlayStart = grammar.Rules.Length;
        if (hasStandingOverlay)
        {
            for (int i = 0; i < grammar.Rules.Length; i++)
            {
                if (model.OverlayRuleIDs.Contains(grammar.Rules[i].Id))
                {
                    inputCarriesOverlay = true;
                    inputOverlayStart = i;
                    break;
                }
            }
        }
        // The side layer is published separately from the pure binary image.  A
        // fresh/reinduced base with no new paradigm identities is therefore a valid
        // identity too; GrammarOverlay.TryFromComposed reuses the live side layer.
        if (hasStandingOverlay && !hasNewSlots && (!inputCarriesOverlay || ReferenceEquals(model.LastMintRules, grammar.Rules)))
            return new AntiUnifyPass(grammar, 0, 0,
                new AntiUnifyMintOperations(0, 0, 0, 0, ReusedInput: true, Rebuilt: false));

        int baseCount = inputCarriesOverlay ? inputOverlayStart : grammar.Rules.Length;
        int baseVisited = 0;
        int baseCopied = 0;
        int expansions = 0;
        int overlayVisited = 0;
        HashSet<RuleID> previousOverlayIDs = new(model.OverlayRuleIDs);
        bool rebase = model.BaseRuleOrder.Count > baseCount;
        int oldPrefix = Math.Min(model.BaseRuleOrder.Count, baseCount);
        for (int i = 0; !rebase && i < oldPrefix; i++)
            if (!grammar.Rules[i].Id.Equals(model.BaseRuleOrder[i])) rebase = true;
        // Overlay symbols are array-positioned.  A binary append before an input
        // overlay shifts every side-layer reference, so it is a real side rebind even
        // though the binary prefix itself remains valid.
        if (inputCarriesOverlay && model.BaseRuleOrder.Count != baseCount) rebase = true;
        if (!inputCarriesOverlay)
        {
            // The caller supplied a pure binary image.  Its whole rule array is the
            // current base, including any append since the last mint.
            baseCount = grammar.Rules.Length;
            for (int i = 0; !rebase && i < oldPrefix; i++)
                if (!grammar.Rules[i].Id.Equals(model.BaseRuleOrder[i])) rebase = true;
        }
        if (rebase)
        {
            model.BaseRuleOrder.Clear();
            model.BaseRuleExpansions.Clear();
            model.BaseExpansionSlots.Clear();
            model.BaseExpansionCollisionSlots.Clear();
            model.SurfaceSymbols.Clear();
            model.SlotSymbols.Clear();
        }

        bool appendOnlyOverlay = inputCarriesOverlay && !rebase && baseCount == model.BaseRuleOrder.Count;
        // A pure loom image whose prefix matches the standing corroboration only APPENDED binary rules (the drive's
        // every-cadence shape): visit/expand/hash that suffix alone and keep the output copy a single memcpy.
        bool appendOnlyPureBase = !inputCarriesOverlay && !rebase && model.BaseRuleOrder.Count > 0;
        int standingBaseCount = model.BaseRuleOrder.Count;
        // Reconstruct.Expand takes the base prefix; slicing grammar.Rules[..baseCount] PER expanded rule was an
        // O(rules) array copy per expansion — hoist the slice once (pure inputs alias the array outright).
        GrammarRule[] baseRules = baseCount == grammar.Rules.Length ? grammar.Rules : grammar.Rules[..baseCount];
        var symOf = model.SurfaceSymbols;
        var ruleIDs = new HashSet<RuleID>();
        double baseVisitMs = 0;
        double baseCopyMs = 0;
        double ruleExpansionMs = 0;
        GrammarRule[]? appendBuffer = null;
        int appendCount = 0;
        List<GrammarRule>? ruleList = null;
        if (appendOnlyOverlay)
        {
            int appendedRules = CountMintAppendRules(model, symOf, previousOverlayIDs, carryStandingSlots: true);
            appendBuffer = new GrammarRule[grammar.Rules.Length + appendedRules];
            long copyStart = Stopwatch.GetTimestamp();
            Array.Copy(grammar.Rules, appendBuffer, grammar.Rules.Length);
            baseCopyMs = Stopwatch.GetElapsedTime(copyStart).TotalMilliseconds;
            appendCount = grammar.Rules.Length;
            baseCopied = grammar.Rules.Length; // the output image is a contiguous array; account for its physical copy
        }
        else if (appendOnlyPureBase)
        {
            // ── THE Δ BASE WALK ──  the full per-rule re-walk (visit + expand-cache probe + ContentHash ×
            // every base rule, every cadence sleep — the measured image_copied=49,330 mint wall) collapses to
            // the appended suffix. The standing BaseExpansionSlots probe answers the one question the skipped
            // prefix could still be asked: does an OLD rule render a member birthed SINCE the last mint.
            foreach (string surface in symOf.Keys.ToArray())
                if (symOf[surface].Value >= Symbol.FirstNonterminal + (uint)baseCount)
                    symOf.Remove(surface);                            // the shared post-walk prune, hoisted: the pending-literal census below must see the pruned view
            long visitStart = Stopwatch.GetTimestamp();
            for (int i = standingBaseCount; i < baseCount; i++)
            {
                baseVisited++;
                GrammarRule rule = grammar.Rules[i];
                if (!model.BaseRuleExpansions.TryGetValue(rule.Id, out byte[]? expansion))
                {
                    long expansionStart = Stopwatch.GetTimestamp();
                    expansion = Reconstruct.Expand(baseRules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
                    ruleExpansionMs += Stopwatch.GetElapsedTime(expansionStart).TotalMilliseconds;
                    model.BaseRuleExpansions[rule.Id] = expansion;
                    expansions++;
                }
                int expansionHash = Tape.ContentHash(expansion);
                model.IndexBaseExpansionSlot(expansionHash, i);
                if (model.MemberSurfaceHashes.Contains(expansionHash))
                    symOf[Encoding.UTF8.GetString(expansion)] = new Symbol(Symbol.FirstNonterminal + (uint)i);
            }
            // the retro-probe + the pending-literal census: exactly the matches and literal mints the full
            // walk's SymbolFor path would produce, sized here so the output buffer allocates once.
            foreach (string name in model.MintOrder)
                foreach (string member in model.SortedMembers[name])
                {
                    if (model.SlotMembers.ContainsKey(member)) continue;
                    if (!symOf.ContainsKey(member) && TryFindBaseExpansionSlot(member, out int slot))
                        symOf[member] = new Symbol(Symbol.FirstNonterminal + (uint)slot);
                }
            baseVisitMs = Stopwatch.GetElapsedTime(visitStart).TotalMilliseconds;
            int appendedRules = CountMintAppendRules(model, symOf, ruleIDs, carryStandingSlots: false);
            appendBuffer = new GrammarRule[baseCount + appendedRules];
            long copyStart = Stopwatch.GetTimestamp();
            Array.Copy(grammar.Rules, appendBuffer, baseCount);
            baseCopyMs = Stopwatch.GetElapsedTime(copyStart).TotalMilliseconds;
            appendCount = baseCount;
            baseCopied = baseCount;                                   // the output image's single physical memcpy
        }
        else
            ruleList = new List<GrammarRule>(baseCount + model.SlotCount * 2);
        int GetRuleCount() => appendBuffer is not null ? appendCount : ruleList!.Count;
        void AddRule(GrammarRule rule)
        {
            if (appendBuffer is not null)
            {
                if ((uint)appendCount >= (uint)appendBuffer.Length)
                    throw new InvalidOperationException($"antiunify append plan undercount: wrote {appendCount} of {appendBuffer.Length} rules before {rule.Id}");
                appendBuffer[appendCount++] = rule;
            }
            else ruleList!.Add(rule);
        }
        bool TryFindBaseExpansionSlot(string surface, out int slot)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(surface);
            int hash = Tape.ContentHash(bytes);
            if (!model.BaseExpansionSlots.TryGetValue(hash, out slot)) return false;
            if (ExpansionMatches(slot, bytes)) return true;
            if (model.BaseExpansionCollisionSlots.TryGetValue(hash, out List<int>? collisions))
                for (int i = collisions.Count - 1; i >= 0; i--)
                    if (ExpansionMatches(collisions[i], bytes))
                    {
                        slot = collisions[i];
                        return true;
                    }
            slot = 0;
            return false;
        }
        bool ExpansionMatches(int slot, byte[] bytes)
            => model.BaseRuleExpansions.TryGetValue(grammar.Rules[slot].Id, out byte[]? rendered)
                && rendered.AsSpan().SequenceEqual(bytes);
        if (ruleList is not null)
        {
            long visitStart = Stopwatch.GetTimestamp();
            long copyStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < baseCount; i++)
            {
                baseVisited++;
                GrammarRule rule = grammar.Rules[i];
                AddRule(rule);
                baseCopied++;
                ruleIDs.Add(rule.Id);
                // Expand only a genuinely new base rule.  Re-induction append keeps the
                // previous prefix byte-identical, so cached expansions survive every sleep.
                if (!model.BaseRuleExpansions.TryGetValue(rule.Id, out byte[]? expansion))
                {
                    long expansionStart = Stopwatch.GetTimestamp();
                    expansion = Reconstruct.Expand(baseRules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
                    ruleExpansionMs += Stopwatch.GetElapsedTime(expansionStart).TotalMilliseconds;
                    model.BaseRuleExpansions[rule.Id] = expansion;
                    expansions++;
                }
                int expansionHash = Tape.ContentHash(expansion);
                model.IndexBaseExpansionSlot(expansionHash, i);
                if (model.MemberSurfaceHashes.Contains(expansionHash))
                    symOf[Encoding.UTF8.GetString(expansion)] = new Symbol(Symbol.FirstNonterminal + (uint)i);
            }
            baseCopyMs = Stopwatch.GetElapsedTime(copyStart).TotalMilliseconds;
            baseVisitMs = Stopwatch.GetElapsedTime(visitStart).TotalMilliseconds;
        }
        // BaseRuleOrder is the append-only prefix corroboration.  Do not treat a larger pure
        // binary image as a rebase; only an ID mismatch above is a real invalidation.
        if (appendOnlyPureBase)
            for (int i = standingBaseCount; i < baseCount; i++) model.BaseRuleOrder.Add(grammar.Rules[i].Id);
        else
        {
            model.BaseRuleOrder.Clear();
            for (int i = 0; i < baseCount; i++) model.BaseRuleOrder.Add(grammar.Rules[i].Id);
        }
        foreach (string surface in symOf.Keys.ToArray())
            if (symOf[surface].Value >= Symbol.FirstNonterminal + (uint)baseCount)
                symOf.Remove(surface);

        Symbol SymbolFor(string surface)
        {
            if (symOf.TryGetValue(surface, out var sym)) return sym;
            var s = new Symbol(Symbol.FirstNonterminal + (uint)GetRuleCount());
            AddRule(model.LiteralTemplate(surface));
            symOf[surface] = s;
            return s;
        }

        // If the input already carries the old overlay and the binary prefix did not
        // change, preserve its rules verbatim and append only new literals/classes.
        // A changed prefix must rebind references, but that rebase is explicit and
        // bounded to the side layer — never to base expansion work.
        var slotSym = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        int overlayStart = baseCount;
        if (appendOnlyOverlay)
        {
            // Existing side identities and their symbols are persistent spine state;
            // re-reading the suffix would turn a one-slot append into an O(overlay)
            // scan while pretending it was delta work.
            foreach (RuleID id in previousOverlayIDs) ruleIDs.Add(id);
            foreach (var (name, symbol) in model.SlotSymbols) slotSym[name] = symbol;
            overlayStart = inputOverlayStart;
        }

        long overlayStartTicks = Stopwatch.GetTimestamp();
        int slotted = 0;
        foreach (var name in model.MintOrder)
        {
            var id = model.SlotRuleID(name);
            if (ruleIDs.Contains(id)) continue;
            bool isNew = !previousOverlayIDs.Contains(id);
            if (isNew) overlayVisited++;
            var members = model.SortedMembers[name];
            var memberSyms = new Symbol[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                if (slotSym.TryGetValue(members[i], out var ss)) memberSyms[i] = ss;
                else
                {
                    if (model.SlotMembers.ContainsKey(members[i]))
                        throw new InvalidDataException($"paradigm slot '{name}' cannot bind child '{members[i]}' before it is minted");
                    memberSyms[i] = SymbolFor(members[i]);
                }
            }
            var sym = new Symbol(Symbol.FirstNonterminal + (uint)GetRuleCount());
            long cost = 256 + 32L * members.Length;
            AddRule(new GrammarRule(id, memberSyms, new Mbits(cost), RuleBodyKind.SlotClass, default));
            slotSym[name] = sym;
            model.SlotSymbols[name] = sym;
            ruleIDs.Add(id);
            if (isNew) slotted++;
        }
        double overlayVisitMs = Stopwatch.GetElapsedTime(overlayStartTicks).TotalMilliseconds;

        if (model.SlotCount == 0)
            return new AntiUnifyPass(grammar, 0, 0,
                new AntiUnifyMintOperations(baseVisited, baseCopied, expansions, overlayVisited, ReusedInput: true, Rebuilt: false,
                    BaseVisitMs: baseVisitMs, BaseCopyMs: baseCopyMs, RuleExpansionMs: ruleExpansionMs, OverlayVisitMs: overlayVisitMs));

        GrammarRule[] outputRules;
        if (appendBuffer is not null)
        {
            if (appendCount != appendBuffer.Length)
                throw new InvalidOperationException($"antiunify append accounting drift: wrote {appendCount} rules into {appendBuffer.Length}");
            outputRules = appendBuffer;
        }
        else
        {
            long outputCopyStart = Stopwatch.GetTimestamp();
            outputRules = ruleList!.ToArray();
            baseCopyMs += Stopwatch.GetElapsedTime(outputCopyStart).TotalMilliseconds;
        }
        var g = new RePairResult(outputRules, grammar.Compressed, grammar.TotalSavings, grammar.AlphabetSize);
        model.OverlayRuleIDs.Clear();
        for (int i = baseCount; i < g.Rules.Length; i++) model.OverlayRuleIDs.Add(g.Rules[i].Id);
        model.ExpansionRuleOrder.Clear();
        for (int i = 0; i < g.Rules.Length; i++) model.ExpansionRuleOrder.Add(g.Rules[i].Id);
        model.LastMintRules = g.Rules;
        return new AntiUnifyPass(g, slotted, 0,
            new AntiUnifyMintOperations(baseVisited, baseCopied, expansions, overlayVisited, ReusedInput: false, Rebuilt: true,
                BaseVisitMs: baseVisitMs, BaseCopyMs: baseCopyMs, RuleExpansionMs: ruleExpansionMs, OverlayVisitMs: overlayVisitMs,
                ImageRulesCopied: outputRules.Length));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    //  THE MINT-BENCH KILL-LINE (face 3e) — per-sleep MintSlots wall vs paradigm size + byte-identity
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// usage: mintbench [--slots 50,200,800,3200] [--members 8] [--passes 7] [--seed HEX]
    /// The pre-registered face-3e readout: standing paradigms of growing size minted into ONE fixed fresh
    /// grammar — the per-sleep wall must stay µs-scale-per-slot (the spine re-attach) where the legacy path
    /// re-sorted and re-encoded the whole tower every sleep. The legacy algorithm rides along as the frozen
    /// A/B control: its output grammar must be BYTE-IDENTICAL to the Δ-mint's (rule ids, kinds, patterns,
    /// costs — the Vow, asserted in-process on every size).
    public static int MintBench(string[] args)
    {
        int[] sizes  = Args.Str(args, "--slots", "50,200,800,3200").Split(',').Select(int.Parse).ToArray();
        int members  = Args.Int(args, "--members", 8);
        int passes   = Args.Int(args, "--passes", 7);
        int lines    = Args.Int(args, "--lines", 2000);        // fresh-grammar size knob — the probe side scales with THIS (legacy stringifies every fresh rule; the drive's grammar is thousands of rules)
        ulong seed   = Args.Seed(args, "--seed", 0x317BE7C4UL);

        // ONE fixed fresh grammar (the fresh-probe cost is grammar-shaped, constant across the sweep) — a
        // templated corpus so Re-Pair mints whole-word rules some members can REUSE (the symOf-hit path).
        var corpusWords = Enumerable.Range(0, 48).Select(i => $"cw{i:x2}q").ToArray();
        var sb = new StringBuilder();
        ulong rng = seed;
        int Next(int m) { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return (int)((rng >> 33) % (ulong)m); }
        for (int l = 0; l < lines; l++)
        {
            for (int k = 0; k < 6; k++) { if (k > 0) sb.Append(' '); sb.Append(corpusWords[Next(48)]); }
            sb.Append('\n');
        }
        var g = Engine.Induce(Encoding.UTF8.GetBytes(sb.ToString())).Result;

        var run = Cogito.Run.New("mintbench");
        Trace.Note($"mintbench · paradigm sizes [{string.Join(", ", sizes)}] · {members} members/slot · {passes} passes · fresh grammar {g.Rules.Length} rules");
        var tsv = new StringBuilder("slots\tmembers_total\tlegacy_ms\tdelta_ms\tratio\tidentical\n");

        Paradigm BuildBenchParadigm(int size)
        {
            var model = new Paradigm();
            for (int s = 0; s < size; s++)
            {
                var mem = new List<string>(members);
                for (int m = 0; m < members; m++)
                    mem.Add(s == 0 && m == 0 ? corpusWords[0] : $"s{s}m{m}z");
                model.BirthSlot($"[S{s}]", mem);
            }
            return model;
        }

        foreach (int W in sizes)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AntiUnifyPass nu = default, legacy = default;
            double dMs = 0, lMs = 0;
            var model = BuildBenchParadigm(W);
            nu = MintSlots(g, model);                         // establish the standing tower once
            for (int p = 0; p < passes; p++) { sw.Restart(); _ = MintSlots(nu.Grammar, model); sw.Stop(); dMs += sw.Elapsed.TotalMilliseconds; }
            for (int p = 0; p < passes; p++) { var legacyModel = BuildBenchParadigm(W); sw.Restart(); legacy = MintSlotsLegacy(g, legacyModel); sw.Stop(); lMs += sw.Elapsed.TotalMilliseconds; }
            dMs /= passes; lMs /= passes;
            bool same = GrammarsIdentical(nu.Grammar, legacy.Grammar) && nu.SlottedRules == legacy.SlottedRules;
            Trace.Note($"  W {W,5} ({W * members,6} members) · legacy {lMs,8:F2}ms · Δ-mint {dMs,8:F2}ms · {lMs / Math.Max(1e-4, dMs),5:F1}× · {(same ? "IDENTICAL" : "DIVERGED")}");
            tsv.AppendLine($"{W}\t{W * members}\t{lMs:F3}\t{dMs:F3}\t{lMs / Math.Max(1e-4, dMs):F2}\t{same}");
            if (!same) { run.Write("bench.tsv", tsv.ToString()); Trace.Note("  ✗ DIVERGED — the Δ-mint is not byte-identical; kill-line FAILED"); return 1; }
        }

        // One-new-slot probe: the standing large base/overlay is already materialized;
        // admitting one genuinely new identity must visit only that delta and must not
        // walk or expand the binary prefix again.
        int probeSize = sizes[^1];
        var probeModel = BuildBenchParadigm(probeSize);
        AntiUnifyPass probeBase = MintSlots(g, probeModel);
        probeModel.BirthSlot($"[S{probeSize}]", [$"delta{probeSize}a", $"delta{probeSize}b", "[ordinary-bracket]"]);
        var probeControl = probeModel.CloneForGrowth();
        probeControl.RebuildMintSpine();
        AntiUnifyPass probeDelta = MintSlots(probeBase.Grammar, probeModel);
        AntiUnifyPass probeFull = MintSlots(g, probeControl);
        bool deltaOperations = probeDelta.SlottedRules == 1
            && probeDelta.Operations.OverlayRulesVisited == 1
            && probeDelta.Operations.BaseRulesVisited == 0
            && probeDelta.Operations.RuleExpansions == 0
            && probeDelta.Grammar.Rules.Any(r => r.Id.Equals(probeModel.LiteralTemplate("[ordinary-bracket]").Id))
            && GrammarsIdentical(probeDelta.Grammar, probeFull.Grammar);
        Trace.Note($"  one-new-slot · base={probeBase.Grammar.Rules.Length} · +slots={probeDelta.SlottedRules} · base-visits={probeDelta.Operations.BaseRulesVisited} · base-copied={probeDelta.Operations.BaseRulesCopied} · image-copied={probeDelta.Operations.ImageRulesCopied} · overlay-visits={probeDelta.Operations.OverlayRulesVisited} · bracket-literal=bound · {(deltaOperations ? "DELTA-OPS/IDENTICAL" : "FULL-REWALK/DIVERGED")}");
        if (!deltaOperations) { run.Write("bench.tsv", tsv.ToString()); Trace.Note("  ✗ one-new-slot probe failed — delta mint walked standing state"); return 1; }

        // Pure-base append probe: the DRIVE's every-cadence shape — a PURE binary image that extends the
        // standing corroboration (the loom appended rules since the last mint). The Δ base walk must reproduce the
        // full re-walk byte-for-byte, including the retroactive case: a slot birthed AFTER the corroboration whose
        // member an OLD base rule already renders.
        var pureModel = BuildBenchParadigm(8);
        _ = MintSlots(g, pureModel);                                  // full walk — establishes the standing corroboration over g
        string? retroSurface = null;
        for (int i = 0; i < g.Rules.Length && retroSurface is null; i++)
        {
            var e = Reconstruct.Expand(g.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            if (e.Length >= 2 && e.All(b => b > (byte)' ') && !pureModel.MemberToSlot.ContainsKey(Encoding.UTF8.GetString(e)))
                retroSurface = Encoding.UTF8.GetString(e);
        }
        var appendedRules = new List<GrammarRule>(g.Rules);
        foreach (string w in (string[])["zapp1q", "zapp2q"])
        {
            var bytes = Encoding.UTF8.GetBytes(w);
            var pat = new Symbol[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) pat[i] = new Symbol(bytes[i]);
            appendedRules.Add(new GrammarRule(GrammarRule.ComputeId(pat), pat, new Mbits(256 + 8000L * bytes.Length)));
        }
        var gAppended = new RePairResult(appendedRules.ToArray(), g.Compressed, g.TotalSavings, g.AlphabetSize);
        pureModel.BirthSlot("[SP0]", ["zapp1q", retroSurface ?? "zretroq", "znovelq", "[ordinary-bracket]"]);   // Δ-rendered + old-rule-rendered + literal-minted members
        bool collisionBucketPowered = false;
        if (retroSurface is not null)
        {
            byte[] retroBytes = Encoding.UTF8.GetBytes(retroSurface);
            int retroHash = Tape.ContentHash(retroBytes);
            for (int i = g.Rules.Length - 1; i >= 0; i--)
                if (pureModel.BaseRuleExpansions.TryGetValue(g.Rules[i].Id, out byte[]? rendered)
                    && !rendered.AsSpan().SequenceEqual(retroBytes))
                {
                    pureModel.IndexBaseExpansionSlot(retroHash, i);   // same-hash/different-bytes fixture: newest probe must not hide the older exact match
                    collisionBucketPowered = true;
                    break;
                }
        }
        var pureControl = pureModel.CloneForGrowth();
        pureControl.BaseRuleOrder.Clear();                            // wipe only the corroboration: the control replays the FULL walk over identical standing state (the before)
        pureControl.BaseExpansionSlots.Clear();
        pureControl.BaseExpansionCollisionSlots.Clear();
        AntiUnifyPass pureDelta = MintSlots(gAppended, pureModel);    // the Δ pure-base walk (the after)
        AntiUnifyPass pureFull = MintSlots(gAppended, pureControl);
        bool pureIdentical = GrammarsIdentical(pureDelta.Grammar, pureFull.Grammar) && pureDelta.SlottedRules == pureFull.SlottedRules
            && pureDelta.Operations.BaseRulesVisited == 2 && pureDelta.Operations.RuleExpansions == 2
            && collisionBucketPowered
            && pureDelta.Grammar.Rules.Any(r => r.Id.Equals(pureModel.LiteralTemplate("[ordinary-bracket]").Id));
        Trace.Note($"  pure-base-append · base {g.Rules.Length}→{gAppended.Rules.Length} · +slots={pureDelta.SlottedRules} · base-visits={pureDelta.Operations.BaseRulesVisited} · expand={pureDelta.Operations.RuleExpansions} · image-copied={pureDelta.Operations.ImageRulesCopied} · retro={retroSurface ?? "none"} · bracket-literal=bound · collision-bucket={(collisionBucketPowered ? "powered" : "UNPOWERED")} · {(pureIdentical ? "IDENTICAL (Δ walk ≡ full walk)" : "DIVERGED")}");
        if (!pureIdentical) { run.Write("bench.tsv", tsv.ToString()); Trace.Note("  ✗ pure-base-append probe failed — the Δ base walk diverged from the full re-walk"); return 1; }

        run.Write("bench.tsv", tsv.ToString());
        Trace.Note("  ⇒ kill-line: Δ-mint wall vs paradigm size collapses to the spine re-attach; output grammar byte-identical to the legacy full re-walk at every size");
        return 0;
    }

    /// THE FROZEN LEGACY MINT — the pre-Δ algorithm, kept verbatim as the bench's A/B control arm (it re-indexes
    /// the whole fresh grammar as strings and re-sorts the whole paradigm every call). Not a code path — a
    /// measurement instrument; only MintBench calls it.
    private static AntiUnifyPass MintSlotsLegacy(RePairResult grammar, Paradigm model)
    {
        var rules = new List<GrammarRule>(grammar.Rules);
        var symOf = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        for (int i = 0; i < grammar.Rules.Length; i++)
        {
            var e = Reconstruct.Expand(grammar.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            var s = Encoding.UTF8.GetString(e);
            symOf.TryAdd(s, new Symbol(Symbol.FirstNonterminal + (uint)i));
        }
        Symbol SymbolFor(string surface)
        {
            if (symOf.TryGetValue(surface, out var sym)) return sym;
            var bytes = Encoding.UTF8.GetBytes(surface);
            var pat = new Symbol[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) pat[i] = new Symbol(bytes[i]);
            var id = GrammarRule.ComputeId(pat);
            var s = new Symbol(Symbol.FirstNonterminal + (uint)rules.Count);
            rules.Add(new GrammarRule(id, pat, new Mbits(256 + 8000L * bytes.Length)));
            symOf[surface] = s;
            return s;
        }
        int slotted = 0;
        var slotSym = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        foreach (var name in model.SlotMembers.Keys.OrderBy(k => model.SlotBirth.GetValueOrDefault(k)).ThenBy(k => k, StringComparer.Ordinal))
        {
            var mem = model.SlotMembers[name].OrderBy(m => m, StringComparer.Ordinal).ToArray();
            var memberSyms = new Symbol[mem.Length];
            for (int i = 0; i < mem.Length; i++)
                memberSyms[i] = slotSym.TryGetValue(mem[i], out var ss) ? ss : SymbolFor(mem[i]);
            var id = model.SlotRuleID(name);
            var sym = new Symbol(Symbol.FirstNonterminal + (uint)rules.Count);
            long cost = 256; foreach (var m in mem) cost += 32;
            rules.Add(new GrammarRule(id, memberSyms, new Mbits(cost), RuleBodyKind.SlotClass, default));
            slotSym[name] = sym; symOf[name] = sym;
            slotted++;
        }
        var gg = new RePairResult(rules.ToArray(), grammar.Compressed, grammar.TotalSavings, grammar.AlphabetSize);
        return new AntiUnifyPass(gg, slotted, 0);
    }

    // rule-by-rule identity: id hash, body kind, cost, pattern symbol values (the byte-identity the Vow asks of
    // an O(total)→O(Δ) rewrite — same mints, only cheaper).
    private static bool GrammarsIdentical(RePairResult a, RePairResult b)
    {
        if (a.Rules.Length != b.Rules.Length) return false;
        for (int i = 0; i < a.Rules.Length; i++)
        {
            var (ra, rb) = (a.Rules[i], b.Rules[i]);
            if (!ra.Id.Hash.AsSpan().SequenceEqual(rb.Id.Hash.AsSpan())) return false;
            if (ra.Kind != rb.Kind || ra.Cost.Value != rb.Cost.Value) return false;
            if (ra.Pattern.Length != rb.Pattern.Length) return false;
            for (int j = 0; j < ra.Pattern.Length; j++) if (ra.Pattern[j].Value != rb.Pattern[j].Value) return false;
        }
        return true;
    }
}
