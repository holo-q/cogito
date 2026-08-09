namespace Cogito.Grammar;

using Cogito.Cas;
using Cogito.Codec;

// The compressed model — a hierarchical dictionary of everything recurring in what's been observed.
// The grammar IS the memory; growing it IS learning. Rules are content-addressed, so identity is stable.

/// A production: a nonterminal expands to `Pattern`. `Id` is content-addressed. Schema 150.
///
/// MEMORY-HIERARCHY body-kind: TODAY a rule is an `Expansion` — its `Pattern` materializes the
/// right-hand side. GC-demotion welds the second case: a MEMORIZATION whose expansion is COVERED by reference
/// tape spans is DEMOTED to a `TapeRef` — `Segs` names the ordered span-chain it resolves through (one seg for a
/// full-line rule, many for a multi-line mega-rule crossing newline boundaries), `Cost` drops to the reference cost.
/// The `Pattern` is RETAINED as the reconstruction fallback (so tape-unaware expanders/generators stay byte-correct);
/// a tape-aware reader (Reconstruct.Expand with a Tape) resolves `Segs` instead. Working-set state ONLY — never
/// content-address (Id stays the pattern's hash) and never serialized (GrammarSpec sees Re-Pair's Expansion rules
/// exclusively; demotion lives in the trunk's live grammar, the tape is the durable half). Default = Expansion, so
/// the 3-arg Re-Pair ctor and every existing `.Pattern` reader are untouched.
public readonly struct GrammarRule(RuleID id, Symbol[] pattern, Mbits cost)
{
    public const ushort Schema = 150;
    public readonly RuleID Id = id;
    public readonly Symbol[] Pattern = pattern;   // right-hand side (retained even when demoted — the fallback)
    public readonly Mbits Cost = cost;            // this rule's working-set cost (drops to the reference cost when demoted)
    public readonly RuleBodyKind Kind = RuleBodyKind.Expansion;   // : Expansion (materialized) | TapeRef (demoted) | SlotClass
    public readonly TapeEventSeg[]? Segs = null;       // : the ordered reference span chain a demoted body resolves through (Kind==TapeRef); null otherwise

    /// The full ctor — sets the memory-hierarchy body-kind + demoted chain. Demotion goes through `AsTapeRef`.
    public GrammarRule(RuleID id, Symbol[] pattern, Mbits cost, RuleBodyKind kind, TapeEventSeg[]? segs) : this(id, pattern, cost)
    { Kind = kind; Segs = segs; }

    /// DEMOTE this literal rule to a TAPE-SEG CHAIN — same identity + retained pattern, but the body now
    /// resolves through the reference spans `segs` (one seg = a full-line identity match; many = a multi-line
    /// mega-rule's adjacent-span run) and the working-set cost drops to `refCost`. The eviction that keeps the grammar
    /// under its bit budget WITHOUT forgetting — the bytes live on the append-only tape.
    public GrammarRule AsTapeRef(TapeEventSeg[] segs, Mbits refCost) => new(Id, Pattern, refCost, RuleBodyKind.TapeRef, segs);
    public bool IsDemoted => Kind == RuleBodyKind.TapeRef;

    /// A PARADIGM class rule — its `Pattern` is the ALTERNATIVE member symbols (pick-one),
    /// not a concatenation. Reconstruct expands it to its representative member (Pattern[0]); the generalization it
    /// carries (matching ANY member) is the meaning layer's read, not the byte-cover's. Never Re-Pair output.
    public bool IsSlot => Kind == RuleBodyKind.SlotClass;

    /// CCC of the right-hand side: LE64(len) ‖ U32 per Symbol.Value. The pre-image for rule_id and for
    /// the spec encoding (so a rule's bytes are identical wherever they appear).
    internal static void WritePattern(ref CccWriter w, Symbol[] pattern)
    {
        w.U64((ulong)pattern.Length);
        foreach (var s in pattern) w.U32(s.Value);
    }

    internal static byte[] EncodePattern(Symbol[] pattern)
    {
        var buf = new byte[8 + 4 * pattern.Length];
        var w = new CccWriter(buf);
        WritePattern(ref w, pattern);
        return buf;
    }

    /// Content-addressed identity: rule_id = H("cogito/rule_id/" ‖ ccc(pattern)).
    internal static RuleID ComputeId(Symbol[] pattern) => Hash.Rule(EncodePattern(pattern));
}

/// The grammar at a version: a rule set sorted by RuleID, content-addressed as a whole. Schema 151.
public sealed class GrammarSpec
{
    public const ushort Schema = 151;
    private const long GrammarHeaderMbits = 1024;   // L(G) fixed base cost — the grammar's own description-length floor

    /// Version 0: empty grammar, L(G) = header only, every observation is pure residual. The bootstrap.
    public static readonly GrammarSpec Null = new(version: 0, rules: []);

    private readonly GrammarRule[] _rules;          // sorted ascending by RuleID, deduped
    private readonly byte[] _payload;               // canonical pre-image == the stored envelope payload

    public ulong Version { get; }
    public IReadOnlyList<GrammarRule> Rules => _rules;
    public Mbits Cost { get; }
    public BlobRef Address { get; }

    /// Trusted ctor: `rules` MUST already be sorted + deduped (WithRules / Load guarantee it). Encodes the
    /// canonical pre-image once, then derives Cost (= header + Σ rule cost) and Address from it.
    private GrammarSpec(ulong version, GrammarRule[] rules)
    {
        Version = version;
        _rules = rules;
        _payload = Encode(version, rules);
        var cost = new Mbits(GrammarHeaderMbits);
        foreach (var r in rules) cost += r.Cost;
        Cost = cost;
        Address = Hash.Blob(new SchemaID(Schema), 1, _payload);
    }

    /// Mint a spec from an arbitrary rule bag: sort ascending by RuleID bytes, drop duplicate IDs.
    public static GrammarSpec WithRules(ulong version, GrammarRule[] rules)
    {
        var sorted = (GrammarRule[])rules.Clone();
        Array.Sort(sorted, static (a, b) =>
        {
            var x = a.Id.Hash;
            var y = b.Id.Hash;
            return x.AsSpan().SequenceCompareTo(y.AsSpan());
        });
        var unique = new List<GrammarRule>(sorted.Length);
        for (var i = 0; i < sorted.Length; i++)
            if (i == 0 || !sorted[i].Id.Equals(sorted[i - 1].Id))
                unique.Add(sorted[i]);
        return new GrammarSpec(version, unique.ToArray());
    }

    /// The schema-151 envelope; its content address IS this spec's Address (Put → Load round-trips).
    public Envelope ToEnvelope() => new(new SchemaID(Schema), 1, _payload);

    /// Inverse of the Address encoding: decode the stored envelope payload back into the rule set.
    public static GrammarSpec Load(ContentStore store, BlobRef specRef)
    {
        var e = store.Get(specRef);
        var r = new CccReader(e.Payload.Span);
        var version = r.U64();
        var count = (int)r.U64();
        var rules = new GrammarRule[count];
        for (var i = 0; i < count; i++)
        {
            var id = new RuleID(r.Digest());
            var plen = (int)r.U64();
            var pattern = new Symbol[plen];
            for (var j = 0; j < plen; j++) pattern[j] = new Symbol(r.U32());
            var cost = new Mbits(r.I64());
            rules[i] = new GrammarRule(id, pattern, cost);
        }
        // Payload already holds the sorted/deduped set; rebuild directly — the ctor re-derives Cost + Address,
        // which hash back to `specRef`.
        return new GrammarSpec(version, rules);
    }

    /// Canonical pre-image: U64(version) ‖ U64(count) ‖ per rule { id[32] ‖ ccc(pattern) ‖ I64(cost) }.
    /// `id` is stored explicitly (not recomputed from the pattern) so Load is a pure inverse for any rule,
    /// independent of the hash suite and of whether id == H(pattern).
    private static byte[] Encode(ulong version, GrammarRule[] rules)
    {
        var size = 16;   // U64 version + U64 count
        foreach (var rule in rules) size += 32 + (8 + 4 * rule.Pattern.Length) + 8;
        var buf = new byte[size];
        var w = new CccWriter(buf);
        w.U64(version);
        w.U64((ulong)rules.Length);
        foreach (var rule in rules)
        {
            w.Digest(rule.Id.Hash);
            GrammarRule.WritePattern(ref w, rule.Pattern);
            w.I64(rule.Cost.Value);
        }
        return buf;
    }
}

/// The mutation packet: G_{v-1} → G_v, with the MDL win it earned. Append-only grammar evolution. Schema 152.
public readonly struct GrammarVersionEvent
{
    public const ushort Schema = 152;
    public ulong Version { get; init; }
    public ulong ParentVersion { get; init; }
    public BlobRef SpecRef { get; init; }
    public Mbits MdlDelta { get; init; }                  // > 0: the grammar got tighter
    public IReadOnlyList<RuleID> RulesAdded { get; init; }
    public (EventID Lo, EventID Hi) Window { get; init; } // training window this was induced from

    /// Schema-152 envelope: U64(version) ‖ U64(parent) ‖ specRef[32] ‖ I64(Δmdl) ‖
    /// U64(count) ‖ ruleId[32]·count ‖ U64(window.lo) ‖ U64(window.hi).
    public Envelope ToEnvelope()
    {
        var size = 8 + 8 + 32 + 8 + (8 + 32 * RulesAdded.Count) + 8 + 8;
        var buf = new byte[size];
        var w = new CccWriter(buf);
        w.U64(Version);
        w.U64(ParentVersion);
        w.Digest(SpecRef.Hash);
        w.I64(MdlDelta.Value);
        w.U64((ulong)RulesAdded.Count);
        foreach (var rid in RulesAdded) w.Digest(rid.Hash);
        w.U64(Window.Lo.Value);
        w.U64(Window.Hi.Value);
        return new Envelope(new SchemaID(Schema), 1, buf);
    }

    public static GrammarVersionEvent FromEnvelope(in Envelope envelope)
    {
        if (envelope.SchemaId.Value != Schema || envelope.Version != 1)
            throw new InvalidDataException("unknown grammar version-event envelope");
        var r = new CccReader(envelope.Payload.Span);
        ulong version = r.U64(), parent = r.U64();
        BlobRef spec = new(r.Digest());
        var mdl = new Mbits(r.I64());
        int count = checked((int)r.U64());
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("grammar version-event rule count is invalid");
        var added = new RuleID[count];
        for (int i = 0; i < count; i++) added[i] = new RuleID(r.Digest());
        var window = (new EventID(r.U64()), new EventID(r.U64()));
        return new GrammarVersionEvent { Version = version, ParentVersion = parent, SpecRef = spec, MdlDelta = mdl, RulesAdded = added, Window = window };
    }
}

/// Append-only grammar publication receipts between keyframes.  The durable
/// grammar image remains in ContentStore under each event's SpecRef; this rail
/// carries only revision edges and the rule ids added at each edge.
internal readonly record struct GrammarCheckpointDelta(
    ulong ParentVersion,
    ulong Version,
    GrammarVersionEvent[] Events)
{
    internal bool IsEmpty => ParentVersion == Version && (Events?.Length ?? 0) == 0;
}

/// Cursor for the append-only GrammarVersionEvent stream.  It intentionally
/// does not retain a second grammar image: replay resolves each event's SpecRef
/// from ContentStore and applies the publication boundary in order.
internal sealed class GrammarCheckpointCursor
{
    private readonly List<GrammarVersionEvent> _events = new();
    private int _checkpointCursor;
    private ulong _version;

    internal ulong Version => _version;

    internal void Append(in GrammarVersionEvent versionEvent)
    {
        if (versionEvent.ParentVersion != _version)
            throw new InvalidDataException($"grammar version parent {versionEvent.ParentVersion} disagrees with {_version}");
        if (versionEvent.Version <= versionEvent.ParentVersion)
            throw new InvalidDataException("grammar version must advance");
        _events.Add(versionEvent);
        _version = versionEvent.Version;
    }

    internal GrammarCheckpointDelta CaptureCheckpointDelta()
    {
        ulong parent = _checkpointCursor == 0 ? 0 : _events[_checkpointCursor - 1].Version;
        return new GrammarCheckpointDelta(parent, _version, _events.Skip(_checkpointCursor).ToArray());
    }

    internal void CommitCheckpointDelta() => _checkpointCursor = _events.Count;

    internal void ApplyCheckpointDelta(in GrammarCheckpointDelta delta)
    {
        if (delta.ParentVersion != _version)
            throw new InvalidDataException($"grammar checkpoint delta parent {delta.ParentVersion} disagrees with {_version}");
        foreach (GrammarVersionEvent versionEvent in delta.Events) Append(in versionEvent);
        if (_version != delta.Version) throw new InvalidDataException("grammar checkpoint delta version does not match event tail");
        _checkpointCursor = _events.Count;
    }

    internal static void WriteCheckpointDelta(CkptWriter w, in GrammarCheckpointDelta delta)
    {
        if (delta.Events.Length > 1_000_000) throw new InvalidDataException("grammar checkpoint delta exceeds event bound");
        w.U8(1); w.U64(delta.ParentVersion); w.U64(delta.Version); w.I32(delta.Events.Length);
        foreach (GrammarVersionEvent versionEvent in delta.Events)
        {
            Envelope envelope = versionEvent.ToEnvelope();
            w.U32(envelope.SchemaId.Value); w.U16(envelope.Version); w.Bytes(envelope.Payload.ToArray());
        }
    }

    internal static GrammarCheckpointDelta ReadCheckpointDelta(CkptReader r)
    {
        if (r.U8() != 1) throw new InvalidDataException("unknown grammar checkpoint delta version");
        ulong parent = r.U64(), version = r.U64(); int count = r.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("grammar checkpoint event count is invalid");
        var events = new GrammarVersionEvent[count];
        for (int i = 0; i < count; i++)
            events[i] = GrammarVersionEvent.FromEnvelope(new Envelope(new SchemaID(checked((ushort)r.U32())), r.U16(), r.Bytes()));
        return new GrammarCheckpointDelta(parent, version, events);
    }
}
