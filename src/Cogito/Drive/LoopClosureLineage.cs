namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using Cogito.Grammar;
using Ronmamon;

/// The causal node vocabulary for one registered loop-closure tape.
public enum LoopLineageNodeSpecies : byte
{
    AdmissionPlan,
    VerifiedLaw,
    VerifiedLawSupport,
    Rung0Composition,
    DisplacedEvaluation,
    LearnedReadout,
    Quota,
    PaidDivergence,
    AdjudicatedOutcome,
    NewTapeEvidence,
    PatternGrammarAdmission,
}

internal static class LoopLineageNodeSpeciesWire
{
    internal static string Format(LoopLineageNodeSpecies species) => species switch
    {
        // Frozen wire token WorldEncounter; identifier-side name is AdmissionPlan.
        LoopLineageNodeSpecies.AdmissionPlan => "WorldEncounter",
        // Frozen wire token FundedDissent; identifier-side name is PaidDivergence.
        LoopLineageNodeSpecies.PaidDivergence => "FundedDissent",
        // Frozen wire token Funding; identifier-side name is Quota.
        LoopLineageNodeSpecies.Quota => "Funding",
        _ => species.ToString(),
    };

    internal static bool TryParse(string value, out LoopLineageNodeSpecies species)
    {
        // Frozen wire token WorldEncounter; identifier-side name is AdmissionPlan.
        if (value == "WorldEncounter")
        {
            species = LoopLineageNodeSpecies.AdmissionPlan;
            return true;
        }

        // Frozen wire token FundedDissent; identifier-side name is PaidDivergence.
        if (value == "FundedDissent")
        {
            species = LoopLineageNodeSpecies.PaidDivergence;
            return true;
        }

        // Frozen wire token Funding; identifier-side name is Quota.
        if (value == "Funding")
        {
            species = LoopLineageNodeSpecies.Quota;
            return true;
        }

        return Enum.TryParse(value, out species);
    }
}

public readonly record struct LoopLineageNodeID(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}

public readonly record struct LoopLineageEdgeID(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}

/// Key for one causal opportunity. Species alone is not an ancestry key: two
/// opportunities may carry a rung-0, readout, and funding node concurrently.
/// The key is persisted in each typed edge so resume reconstructs the same rail.
public readonly record struct LoopLineageCausalID(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;

    public static LoopLineageCausalID Merge(
        LoopLineageNodeSpecies species,
        IReadOnlyList<LoopLineageNodeID> predecessorIDs)
    {
        if (predecessorIDs is null || predecessorIDs.Count == 0
            || predecessorIDs.Any(static id => !id.IsValid))
            throw new InvalidDataException("a merged causal opportunity requires typed predecessors");
        string material = LoopLineageNodeSpeciesWire.Format(species) + "|" + string.Join('|', predecessorIDs
            .Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal)
            .Select(static id => id.Value));
        return new($"merge:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}");
    }
}

/// A typed node snapshot used by the canonical and shuffled in-memory lineage views.
public readonly record struct LoopLineageNode(
    LoopLineageNodeID NodeID,
    LoopLineageNodeSpecies Species,
    TapeEventID EventID,
    string PayloadSHA256,
    GrammarRevisionID? GrammarRevision = null,
    LoopLineageCausalID CausalID = default)
{
    public void Validate()
    {
        if (!NodeID.IsValid || !Enum.IsDefined(Species) || EventID.Value < 0 || !IsDigest(PayloadSHA256)
            || !CausalID.IsValid)
            throw new InvalidDataException("loop lineage node is malformed");
        bool requiresRevision = Species is LoopLineageNodeSpecies.Rung0Composition
            or LoopLineageNodeSpecies.LearnedReadout
            or LoopLineageNodeSpecies.Quota
            or LoopLineageNodeSpecies.PaidDivergence
            or LoopLineageNodeSpecies.AdjudicatedOutcome
            or LoopLineageNodeSpecies.NewTapeEvidence
            or LoopLineageNodeSpecies.PatternGrammarAdmission;
        if (requiresRevision && GrammarRevision is not { Value: > 0 })
            throw new InvalidDataException($"loop lineage {Species} node omits its grammar revision");
    }

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal enum LoopClosurePredecessorResolutionKinds : byte
{
    Exact,
    ReadoutMissing,
    ReadoutPacketInvalid,
    ReadoutDuplicate,
    FundingMissing,
    FundingPacketInvalid,
    FundingDuplicate,
    FundingReadoutMismatch,
    CausalMismatch,
}

internal readonly record struct LoopClosurePredecessorResolution(
    LoopClosurePredecessorResolutionKinds Kind,
    LoopLineageNode Readout,
    LoopLineageNode Funding,
    int ReadoutMatches,
    int FundingMatches,
    int InvalidReadoutPackets,
    int InvalidFundingPackets)
{
    internal bool IsExact => Kind == LoopClosurePredecessorResolutionKinds.Exact;

    internal string FormatDiagnostic(
        CortexPolicyDecisionID decisionID,
        CortexPolicyQuotaDecisionID fundingID)
        => $"reason={Kind} decision={decisionID.Value} funding={fundingID} "
            + $"readout_matches={ReadoutMatches} funding_matches={FundingMatches} "
            + $"invalid_readout_packets={InvalidReadoutPackets} invalid_funding_packets={InvalidFundingPackets} "
            + $"readout_node={Readout.NodeID.Value ?? ""} readout_event={Readout.EventID.Value} "
            + $"funding_node={Funding.NodeID.Value ?? ""} funding_event={Funding.EventID.Value}";
}

/// One receipt in the append-only lineage hash chain. Predecessor bindings are the only
/// mutable input to the shuffled null; event bytes and payload digests stay untouched.
public sealed class LoopLineageEdgeReceipt
{
    public LoopLineageEdgeReceipt(
        LoopLineageEdgeID EdgeID,
        LoopLineageNode Node,
        IReadOnlyList<LoopLineageNodeID> PredecessorIDs,
        IReadOnlyList<string> PredecessorSHA256,
        string PreviousLineageSHA256,
        string CanonicalLineageSHA256)
    {
        this.EdgeID = EdgeID;
        this.Node = Node;
        this.PredecessorIDs = PredecessorIDs.ToArray();
        this.PredecessorSHA256 = PredecessorSHA256.ToArray();
        this.PreviousLineageSHA256 = PreviousLineageSHA256;
        this.CanonicalLineageSHA256 = CanonicalLineageSHA256;
    }

    public LoopLineageEdgeID EdgeID { get; }
    public LoopLineageNode Node { get; }
    public IReadOnlyList<LoopLineageNodeID> PredecessorIDs { get; }
    public IReadOnlyList<string> PredecessorSHA256 { get; }
    public string PreviousLineageSHA256 { get; }
    public string CanonicalLineageSHA256 { get; }

    public static LoopLineageEdgeReceipt Create(
        LoopLineageEdgeID edgeID,
        LoopLineageNode node,
        IReadOnlyList<LoopLineageNodeID> predecessorIDs,
        IReadOnlyList<string> predecessorSHA256,
        string previousLineageSHA256)
    {
        LoopLineageEdgeReceipt receipt = new(edgeID, node, predecessorIDs, predecessorSHA256,
            previousLineageSHA256, "");
        return new(edgeID, node, predecessorIDs, predecessorSHA256, previousLineageSHA256, receipt.ComputeDigest());
    }

    /// Rebind only the predecessor foreign keys. The node identity and payload remain
    /// byte-identical; the returned receipt is therefore suitable only for the in-memory
    /// shuffled null, never for a tape write.
    public LoopLineageEdgeReceipt Rebind(
        IReadOnlyList<LoopLineageNodeID> predecessorIDs,
        IReadOnlyList<string> predecessorSHA256,
        string previousLineageSHA256)
        => Create(EdgeID, Node, predecessorIDs, predecessorSHA256, previousLineageSHA256);

    public string ComputeCanonicalDigest() => ComputeDigest();

    public void Validate()
    {
        Node.Validate();
        if (!EdgeID.IsValid || PredecessorIDs.Count != PredecessorSHA256.Count)
            throw new InvalidDataException("loop lineage edge predecessor binding is malformed");
        if (PredecessorIDs.Any(static id => !id.IsValid) || PredecessorSHA256.Any(static digest => !IsDigest(digest)))
            throw new InvalidDataException("loop lineage edge carries an invalid predecessor identity");
        if (PredecessorIDs.Distinct().Count() != PredecessorIDs.Count)
            throw new InvalidDataException("loop lineage edge repeats a predecessor identity");
        if (PreviousLineageSHA256.Length != 0 && !IsDigest(PreviousLineageSHA256))
            throw new InvalidDataException("loop lineage edge carries an invalid previous digest");
        if (!IsDigest(CanonicalLineageSHA256) || !string.Equals(ComputeDigest(), CanonicalLineageSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop lineage edge canonical digest does not match its typed payload");
    }

    public byte[] Encode()
    {
        Validate();
        LoopLineageEdgeReceiptRON document = new()
        {
            edgeID = EdgeID.Value, nodeID = Node.NodeID.Value, species = LoopLineageNodeSpeciesWire.Format(Node.Species), eventID = Node.EventID.Value,
            payloadSHA256 = Node.PayloadSHA256, hasGrammarRevision = Node.GrammarRevision.HasValue,
            grammarRevision = Node.GrammarRevision?.Value ?? 0, causalID = Node.CausalID.Value,
            previousLineageSHA256 = PreviousLineageSHA256,
            canonicalLineageSHA256 = CanonicalLineageSHA256,
        };
        foreach (LoopLineageNodeID id in PredecessorIDs) document.predecessorIDs.Add(id.Value);
        foreach (string digest in PredecessorSHA256) document.predecessorSHA256.Add(digest);
        return RonSerializer.SerializeToUtf8(in document);
    }

    public static LoopLineageEdgeReceipt Decode(ReadOnlySpan<byte> bytes)
    {
        LoopLineageEdgeReceiptRON document = RonSerializer.Deserialize<LoopLineageEdgeReceiptRON>(bytes);
        if (!LoopLineageNodeSpeciesWire.TryParse(document.species, out LoopLineageNodeSpecies species)) throw new InvalidDataException("loop lineage edge species is unknown");
        LoopLineageNode node = new(new LoopLineageNodeID(document.nodeID), species, new TapeEventID(document.eventID),
            document.payloadSHA256, document.hasGrammarRevision ? new GrammarRevisionID(document.grammarRevision) : null,
            new LoopLineageCausalID(document.causalID));
        LoopLineageEdgeReceipt receipt = new(new LoopLineageEdgeID(document.edgeID), node,
            document.predecessorIDs.Select(static value => new LoopLineageNodeID(value)).ToArray(),
            document.predecessorSHA256.ToArray(), document.previousLineageSHA256, document.canonicalLineageSHA256);
        receipt.Validate();
        if (!receipt.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("loop lineage edge RON round-trip changed bytes");
        return receipt;
    }

    private string ComputeDigest()
    {
        StringBuilder canonical = new();
        canonical.Append(EdgeID.Value).Append('|').Append(Node.NodeID.Value).Append('|').Append(LoopLineageNodeSpeciesWire.Format(Node.Species)).Append('|')
            .Append(Node.EventID.Value).Append('|').Append(Node.PayloadSHA256).Append('|')
            .Append(Node.GrammarRevision?.Value.ToString() ?? "").Append('|').Append(Node.CausalID.Value).Append('|').Append(PreviousLineageSHA256);
        for (int i = 0; i < PredecessorIDs.Count; i++) canonical.Append('|').Append(PredecessorIDs[i].Value).Append('|').Append(PredecessorSHA256[i]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

/// One contiguous, private payload arena. Event records expose only read-only spans;
/// callers cannot recover or mutate the backing byte array.
internal sealed class LoopLineagePayloadArena
{
    private readonly byte[] _bytes;

    private LoopLineagePayloadArena(byte[] bytes) => _bytes = bytes;

    internal static LoopLineagePayloadArena Copy(ReadOnlySpan<byte> payload)
    {
        byte[] bytes = payload.ToArray();
        return new(bytes);
    }

    internal ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if ((uint)offset > (uint)_bytes.Length || (uint)length > (uint)(_bytes.Length - offset))
            throw new InvalidDataException("loop lineage payload slice exceeds its arena");
        return _bytes.AsSpan(offset, length);
    }

    internal static LoopLineagePayloadArena Build(
        IReadOnlyList<LoopLineageTapeEvent> events,
        out LoopLineageTapeEvent[] ownedEvents)
    {
        int total = 0;
        foreach (LoopLineageTapeEvent item in events) total = checked(total + item.Payload.Length);
        byte[] bytes = new byte[total];
        ownedEvents = new LoopLineageTapeEvent[events.Count];
        int offset = 0;
        LoopLineagePayloadArena arena = new(bytes);
        for (int i = 0; i < events.Count; i++)
        {
            LoopLineageTapeEvent item = events[i];
            item.Payload.Span.CopyTo(bytes.AsSpan(offset, item.Payload.Length));
            ownedEvents[i] = new(item.EventID, arena, offset, item.Payload.Length,
                item.Source, item.Provenance, item.Roles);
            offset += item.Payload.Length;
        }
        return arena;
    }

    internal static LoopLineagePayloadArena Build(
        IReadOnlyList<(TapeEventID EventID, byte[] Payload)> events,
        out LoopLineageTapeEvent[] ownedEvents)
    {
        int total = 0;
        foreach ((TapeEventID _, byte[] payload) in events) total = checked(total + payload.Length);
        byte[] bytes = new byte[total];
        ownedEvents = new LoopLineageTapeEvent[events.Count];
        int offset = 0;
        LoopLineagePayloadArena arena = new(bytes);
        for (int i = 0; i < events.Count; i++)
        {
            (TapeEventID id, byte[] payload) = events[i];
            payload.AsSpan().CopyTo(bytes.AsSpan(offset, payload.Length));
            ownedEvents[i] = new(id, arena, offset, payload.Length);
            offset += payload.Length;
        }
        return arena;
    }

}

public readonly struct LoopLineagePayload
{
    private readonly LoopLineagePayloadArena? _arena;
    private readonly int _offset;
    public int Length { get; }

    internal LoopLineagePayload(LoopLineagePayloadArena arena, int offset, int length)
    {
        _arena = arena;
        _offset = offset;
        Length = length;
    }

    internal static LoopLineagePayload Copy(ReadOnlySpan<byte> payload)
    {
        LoopLineagePayloadArena arena = LoopLineagePayloadArena.Copy(payload);
        return new(arena, 0, payload.Length);
    }

    public ReadOnlySpan<byte> Span => _arena is null ? ReadOnlySpan<byte>.Empty : _arena.Slice(_offset, Length);
    public byte[] ToArray() => Span.ToArray();
}

/// One immutable source event snapshot. Its payload is an offset into a private
/// contiguous arena, never a publicly exposed byte[].
public readonly struct LoopLineageTapeEvent
{
    public LoopLineageTapeEvent(TapeEventID eventID, byte[] payload)
        : this(eventID, LoopLineagePayload.Copy(payload), "lineage", Provenances.Execution, TapeEventRoles.AuditOnly) { }

    internal LoopLineageTapeEvent(
        TapeEventID eventID,
        byte[] payload,
        string source,
        Provenances provenance,
        TapeEventRoles roles)
        : this(eventID, LoopLineagePayload.Copy(payload), source, provenance, roles) { }

    internal LoopLineageTapeEvent(
        TapeEventID eventID,
        LoopLineagePayload payload,
        string source = "lineage",
        Provenances provenance = Provenances.Execution,
        TapeEventRoles roles = TapeEventRoles.AuditOnly)
    {
        EventID = eventID;
        Payload = payload;
        Source = source;
        Provenance = provenance;
        Roles = roles;
    }

    internal LoopLineageTapeEvent(
        TapeEventID eventID,
        LoopLineagePayloadArena arena,
        int offset,
        int length,
        string source = "lineage",
        Provenances provenance = Provenances.Execution,
        TapeEventRoles roles = TapeEventRoles.AuditOnly)
        : this(eventID, new LoopLineagePayload(arena, offset, length), source, provenance, roles) { }

    public TapeEventID EventID { get; }
    public LoopLineagePayload Payload { get; }
    public string Source { get; }
    public Provenances Provenance { get; }
    public TapeEventRoles Roles { get; }
    public LoopLineageTapeEvent Copy() => new(EventID, Payload, Source, Provenance, Roles);
}

public sealed class LoopLineageTapeSnapshot
{
    private LoopLineageTapeSnapshot(
        IReadOnlyList<LoopLineageTapeEvent> events,
        IReadOnlyList<LoopLineagePayload> lineagePackets,
        string? digest,
        LoopLineagePayloadArena arena)
    {
        _arena = arena;
        Events = events;
        LineagePackets = lineagePackets;
        _digest = digest;
    }

    private readonly LoopLineagePayloadArena _arena;
    public IReadOnlyList<LoopLineageTapeEvent> Events { get; }
    public IReadOnlyList<LoopLineagePayload> LineagePackets { get; }
    private string? _digest;
    public string Digest => _digest ??= ComputeTapeDigest(Events);

    public static LoopLineageTapeSnapshot Capture(Tape tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        List<(TapeEventID EventID, byte[] Payload, string Source, Provenances Provenance, TapeEventRoles Roles)> events = new();
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] payload))
                throw new InvalidDataException($"loop lineage source event {view.Id} cannot be resolved");
            events.Add((view.Id, payload, view.Source, view.Provenance, view.Roles));
        }
        return CreateOwned(events);
    }

    public static LoopLineageTapeSnapshot Create(IReadOnlyList<LoopLineageTapeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Any(static item => item.EventID.Value < 0))
            throw new InvalidDataException("loop lineage source event snapshot is malformed");
        LoopLineagePayloadArena arena = LoopLineagePayloadArena.Build(events, out LoopLineageTapeEvent[] ownedEvents);
        List<LoopLineagePayload> lineagePackets = ownedEvents
            .Where(static item => TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out _))
            .Select(static item => item.Payload).ToList();
        return new(ownedEvents, lineagePackets, null, arena);
    }

    /// Adopt payloads decoded by the checkpoint lineage reader. The decoder owns
    /// those arrays and the resulting snapshot is immutable for the verifier pass;
    /// caller-owned lists should continue through Create for defensive copying.
    internal static LoopLineageTapeSnapshot CreateOwned(IReadOnlyList<LoopLineageTapeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Any(static item => item.EventID.Value < 0))
            throw new InvalidDataException("loop lineage source event snapshot is malformed");
        if (events.Count == 0) return Create(events);
        LoopLineagePayloadArena arena = LoopLineagePayloadArena.Build(events, out LoopLineageTapeEvent[] ownedEvents);
        List<LoopLineagePayload> lineagePackets = ownedEvents
            .Where(static item => TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out _))
            .Select(static item => item.Payload).ToList();
        return new(ownedEvents, lineagePackets, null, arena);
    }

    /// The checkpoint tape reader recovers only identity and bytes — a shed event's
    /// source and roles are already carried by the checkpoint's own tape record, so
    /// the snapshot adopts the payloads under the default lineage annotation rather
    /// than inventing per-event provenance it did not read.
    internal static LoopLineageTapeSnapshot CreateOwned(IReadOnlyList<(TapeEventID EventID, byte[] Payload)> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Any(static item => item.EventID.Value < 0))
            throw new InvalidDataException("loop lineage source event snapshot is malformed");
        LoopLineagePayloadArena arena = LoopLineagePayloadArena.Build(events, out LoopLineageTapeEvent[] ownedEvents);
        List<LoopLineagePayload> lineagePackets = ownedEvents
            .Where(static item => TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out _))
            .Select(static item => item.Payload).ToList();
        return new(ownedEvents, lineagePackets, null, arena);
    }

    internal static LoopLineageTapeSnapshot CreateOwned(
        IReadOnlyList<(TapeEventID EventID, byte[] Payload, string Source, Provenances Provenance, TapeEventRoles Roles)> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        List<(TapeEventID EventID, byte[] Payload)> payloads = events
            .Select(static item => (item.EventID, item.Payload)).ToList();
        LoopLineagePayloadArena arena = LoopLineagePayloadArena.Build(payloads, out LoopLineageTapeEvent[] ownedEvents);
        for (int index = 0; index < ownedEvents.Length; index++)
        {
            (TapeEventID eventID, _, string source, Provenances provenance, TapeEventRoles roles) = events[index];
            ownedEvents[index] = new(eventID, ownedEvents[index].Payload, source, provenance, roles);
        }
        List<LoopLineagePayload> lineagePackets = ownedEvents
            .Where(static item => TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out _))
            .Select(static item => item.Payload).ToList();
        return new(ownedEvents, lineagePackets, null, arena);
    }

    public bool Conserves(LoopLineageTapeSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return TryBuildEventPayloadMap(Events, out Dictionary<long, LoopLineagePayload> expected)
            && TryBuildEventPayloadMap(other.Events, out Dictionary<long, LoopLineagePayload> observed)
            && LoopLineageVerifier.CompareEventPayloadMaps(expected, observed, Digest, other.Digest,
                out _, out _);
    }

    internal static bool TryBuildEventPayloadMap(
        IEnumerable<LoopLineageTapeEvent> events,
        out Dictionary<long, LoopLineagePayload> map)
    {
        map = new();
        foreach (LoopLineageTapeEvent item in events)
            if (item.EventID.Value < 0 || !map.TryAdd(item.EventID.Value, item.Payload))
            {
                map.Clear();
                return false;
            }
        return true;
    }

    internal static string ComputeTapeDigest(IEnumerable<LoopLineageTapeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events is IReadOnlyList<LoopLineageTapeEvent> orderedEvents)
            return ComputeTapeDigest(orderedEvents);

        // Do not use Enumerable.OrderBy here.  Apart from allocating an ordered
        // buffer and a second key array, the old loop stackalloc'd the id and
        // length words once per event; a million-event tape therefore exhausted
        // the thread stack before the digest could be sealed.  The canonical
        // order is still EventID order, but the bytes are streamed directly into
        // SHA-256 so memory is bounded by the sortable event references rather
        // than the complete encoded tape.
        int capacity = events is ICollection<LoopLineageTapeEvent> collection ? collection.Count : 0;
        List<(LoopLineageTapeEvent Event, int Index)> ordered = capacity == 0 ? new() : new(capacity);
        int index = 0;
        foreach (LoopLineageTapeEvent item in events) ordered.Add((item, index++));
        ordered.Sort(static (left, right) =>
        {
            int byEventID = left.Event.EventID.Value.CompareTo(right.Event.EventID.Value);
            return byEventID != 0 ? byEventID : left.Index.CompareTo(right.Index);
        });

        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] id = new byte[sizeof(long)];
        byte[] length = new byte[sizeof(int)];
        foreach ((LoopLineageTapeEvent item, _) in ordered)
        {
            BinaryPrimitives.WriteInt64LittleEndian(id, item.EventID.Value);
            digest.AppendData(id);
            BinaryPrimitives.WriteInt32LittleEndian(length, item.Payload.Length);
            digest.AppendData(length);
            digest.AppendData(item.Payload.Span);
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static string ComputeTapeDigest(IReadOnlyList<LoopLineageTapeEvent> events)
    {
        bool monotonic = true;
        long previous = 0;
        bool hasPrevious = false;
        for (int index = 0; index < events.Count; index++)
        {
            long eventID = events[index].EventID.Value;
            if (hasPrevious && eventID < previous) { monotonic = false; break; }
            previous = eventID;
            hasPrevious = true;
        }

        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (monotonic)
        {
            AppendDigestEvents(events, digest);
            return Convert.ToHexStringLower(digest.GetHashAndReset());
        }

        List<(LoopLineageTapeEvent Event, int Index)> ordered = new(events.Count);
        for (int index = 0; index < events.Count; index++) ordered.Add((events[index], index));
        ordered.Sort(static (left, right) =>
        {
            int byEventID = left.Event.EventID.Value.CompareTo(right.Event.EventID.Value);
            return byEventID != 0 ? byEventID : left.Index.CompareTo(right.Index);
        });
        foreach ((LoopLineageTapeEvent item, _) in ordered)
            AppendDigestEvent(item, digest);
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void AppendDigestEvents(
        IReadOnlyList<LoopLineageTapeEvent> events,
        IncrementalHash digest)
    {
        for (int index = 0; index < events.Count; index++)
            AppendDigestEvent(events[index], digest);
    }

    private static void AppendDigestEvent(LoopLineageTapeEvent item, IncrementalHash digest)
    {
        Span<byte> id = stackalloc byte[sizeof(long)];
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt64LittleEndian(id, item.EventID.Value);
        digest.AppendData(id);
        BinaryPrimitives.WriteInt32LittleEndian(length, item.Payload.Length);
        digest.AppendData(length);
        digest.AppendData(item.Payload.Span);
    }
}

/// In-memory predecessor view used by the R1 null. It carries a deep-copied source
/// event map alongside the edge view so conservation is checked against an independent
/// object before and after predecessor permutation, never against a self-clone.
public sealed class LoopLineagePredecessorView
{
    private LoopLineagePredecessorView(
        IReadOnlyList<LoopLineageTapeEvent> events,
        IReadOnlyList<LoopLineageEdgeReceipt> edges,
        string eventDigest,
        bool clone)
    {
        Events = clone ? events.Select(static item => item.Copy()).ToArray() : events;
        Edges = clone ? edges.ToArray() : edges;
        EventDigest = eventDigest;
    }

    public IReadOnlyList<LoopLineageTapeEvent> Events { get; }
    public IReadOnlyList<LoopLineageEdgeReceipt> Edges { get; }
    public string EventDigest { get; }

    public static LoopLineagePredecessorView Create(
        LoopLineageTapeSnapshot source,
        IReadOnlyList<LoopLineageEdgeReceipt> edges)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edges);
        // The source snapshot is immutable for this verifier pass. Sharing its
        // payload view avoids two more full copies of a production tape.
        return new(source.Events, edges, source.Digest, clone: false);
    }
}

public readonly record struct LoopLineageOccurrenceCheckResult(
    LoopLineageOccurrenceCheckStatuses Status,
    string LineageSHA256,
    LoopLineageEdgeID FirstDiscriminatingEdge,
    string Detail)
{
    public bool Passed => Status == LoopLineageOccurrenceCheckStatuses.PASS;
}

public readonly record struct LoopLineagePredecessorBinding(
    LoopLineageEdgeID EdgeID,
    int Slot,
    LoopLineageNodeID PredecessorID,
    string PredecessorSHA256);

/// Read-only source authority for the canonical predecessor bindings. The authority is
/// captured from the sealed edge receipts before the null is constructed; a shuffled view
/// can never silently redefine what an authoritative edge means.
public sealed class LoopLineageAuthority
{
    private LoopLineageAuthority(IReadOnlyDictionary<LoopLineageEdgeID, IReadOnlyList<LoopLineageNodeID>> bindings)
    {
        Bindings = new Dictionary<LoopLineageEdgeID, IReadOnlyList<LoopLineageNodeID>>(bindings);
        Digest = ComputeDigest(Bindings);
    }

    public IReadOnlyDictionary<LoopLineageEdgeID, IReadOnlyList<LoopLineageNodeID>> Bindings { get; }
    public string Digest { get; }

    public static LoopLineageAuthority Capture(IReadOnlyList<LoopLineageEdgeReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        return new(receipts.ToDictionary(static edge => edge.EdgeID,
            static edge => (IReadOnlyList<LoopLineageNodeID>)edge.PredecessorIDs.ToArray()));
    }

    private static string ComputeDigest(IReadOnlyDictionary<LoopLineageEdgeID, IReadOnlyList<LoopLineageNodeID>> bindings)
    {
        StringBuilder canonical = new();
        foreach ((LoopLineageEdgeID edgeID, IReadOnlyList<LoopLineageNodeID> predecessors) in bindings.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
        {
            AppendLengthPrefixed(canonical, edgeID.Value);
            canonical.Append(predecessors.Count).Append('|');
            foreach (LoopLineageNodeID predecessor in predecessors) AppendLengthPrefixed(canonical, predecessor.Value);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendLengthPrefixed(StringBuilder target, string value)
        => target.Append(value.Length).Append(':').Append(value).Append('|');
}

/// The ordinary causal turnstile used by a registered run. Each organ hands over the
/// event it just admitted; this owner derives only typed predecessor foreign keys,
/// emits the edge packet through TapePacketCreator, and retains the receipt list for the
/// post-hoc adjudicator. Step is only the journal coordinate; no event name, threshold,
/// or outcome is consulted to decide whether an edge exists.
public sealed class LoopLineageTurnstile
{
    internal readonly record struct LoopLineageCheckpointDelta(int Cursor, LoopLineageEdgeReceipt[] Receipts)
    {
        internal bool IsEmpty => Receipts is null || Receipts.Length == 0;
    }

    private readonly Tape _tape;
    private readonly Journal _journal;
    private readonly Func<Tape, TapeEventID, bool> _worldRootPredicate;
    private readonly Dictionary<(LoopLineageCausalID CausalID, LoopLineageNodeSpecies Species), LoopLineageNodeID> _latest = new();
    private readonly Dictionary<LoopLineageNodeID, LoopLineageNode> _nodes = new();
    private readonly Dictionary<TapeEventID, LoopLineageNodeID> _worldRootsByEvent = new();
    // First-wins event->node index so TryGetNodeForEvent is O(1); emitted events are
    // unique, so first-wins names the same node the retired linear scan returned.
    private readonly Dictionary<TapeEventID, LoopLineageNodeID> _nodesByEvent = new();
    private readonly List<LoopLineageEdgeReceipt> _receipts = new();
    private string _previous = "";
    private int _sequence;
    private int _checkpointReceiptCursor;

    public LoopLineageTurnstile(Tape tape, Journal journal, Func<Tape, TapeEventID, bool>? worldRootPredicate = null)
    {
        _tape = tape ?? throw new ArgumentNullException(nameof(tape));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _worldRootPredicate = worldRootPredicate ?? IsCorpusWorldOpportunity;
        RestoreFromTape();
        _checkpointReceiptCursor = _receipts.Count;
    }

    public IReadOnlyList<LoopLineageEdgeReceipt> Receipts => _receipts;

    internal bool IsBoundTo(Tape tape, Journal journal)
        => ReferenceEquals(_tape, tape) && ReferenceEquals(_journal, journal);

    internal LoopClosurePredecessorResolution ResolvePolicyFundingPredecessors(
        CortexPolicyDecisionID decisionID,
        CortexPolicyQuotaDecisionID fundingID,
        in LoopClosurePolicyBinding policy)
    {
        policy.Validate();
        List<LoopLineageNode> readouts = new();
        int invalidReadoutPackets = 0;
        foreach (LoopLineageEdgeReceipt edge in _receipts)
        {
            if (edge.Node.Species != LoopLineageNodeSpecies.LearnedReadout) continue;
            TapeEventView? view = _tape.GetEventViews().FirstOrDefault(candidate => candidate.Id == edge.Node.EventID);
            if (view is null || !policy.MatchesSource(view.Value.Source)
                || !_tape.Resolve(edge.Node.EventID, out byte[] payload))
            {
                invalidReadoutPackets++;
                continue;
            }
            try
            {
                CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
                if (packet.DecisionID.Equals(decisionID))
                    readouts.Add(edge.Node);
            }
            catch (InvalidDataException)
            {
                invalidReadoutPackets++;
            }
        }
        if (readouts.Count == 0)
            return new(invalidReadoutPackets == 0
                    ? LoopClosurePredecessorResolutionKinds.ReadoutMissing
                    : LoopClosurePredecessorResolutionKinds.ReadoutPacketInvalid,
                default, default, 0, 0, invalidReadoutPackets, 0);
        if (readouts.Count != 1)
            return new(LoopClosurePredecessorResolutionKinds.ReadoutDuplicate,
                readouts[0], default, readouts.Count, 0, invalidReadoutPackets, 0);

        LoopLineageNode readout = readouts[0];
        List<(LoopLineageEdgeReceipt Edge, CortexPolicyTrialQuotaDecision Decision)> funding = new();
        int invalidFundingPackets = 0;
        foreach (LoopLineageEdgeReceipt edge in _receipts)
        {
            if (edge.Node.Species != LoopLineageNodeSpecies.Quota) continue;
            TapeEventView? view = _tape.GetEventViews().FirstOrDefault(candidate => candidate.Id == edge.Node.EventID);
            if (view is null || !policy.MatchesSource(view.Value.Source)
                || !_tape.Resolve(edge.Node.EventID, out byte[] payload)
                || !TapePacketCreator.TryDecodePolicyTrialQuota(payload, out CortexPolicyTrialQuotaDecision decision))
            {
                invalidFundingPackets++;
                continue;
            }
            if (!policy.MatchesPolicy(decision.Policy))
            {
                invalidFundingPackets++;
                continue;
            }
            if (decision.QuotaDecisionID.Equals(fundingID)) funding.Add((edge, decision));
        }
        if (funding.Count == 0)
            return new(invalidFundingPackets == 0
                    ? LoopClosurePredecessorResolutionKinds.FundingMissing
                    : LoopClosurePredecessorResolutionKinds.FundingPacketInvalid,
                readout, default, 1, 0, invalidReadoutPackets, invalidFundingPackets);
        if (funding.Any(candidate => candidate.Edge.PredecessorIDs.Count != 1
                || candidate.Edge.PredecessorIDs[0] != readout.NodeID))
            return new(LoopClosurePredecessorResolutionKinds.FundingReadoutMismatch,
                readout, funding[0].Edge.Node, 1, funding.Count, invalidReadoutPackets, invalidFundingPackets);

        List<(LoopLineageEdgeReceipt Edge, CortexPolicyTrialQuotaDecision Decision)> paid = funding
            .Where(static candidate => candidate.Decision.Decision == CortexPolicyQuotaDecisions.Paid).ToList();
        List<(LoopLineageEdgeReceipt Edge, CortexPolicyTrialQuotaDecision Decision)> reused = funding
            .Where(static candidate => candidate.Decision.Decision == CortexPolicyQuotaDecisions.Reused).ToList();
        if (paid.Count > 1 || reused.Count > 1 || paid.Count + reused.Count != funding.Count)
            return new(LoopClosurePredecessorResolutionKinds.FundingDuplicate,
                readout, funding[0].Edge.Node, 1, funding.Count, invalidReadoutPackets, invalidFundingPackets);
        LoopLineageNode selected = (reused.Count == 1 ? reused[0] : paid[0]).Edge.Node;
        if (selected.CausalID != readout.CausalID)
            return new(LoopClosurePredecessorResolutionKinds.CausalMismatch,
                readout, selected, 1, funding.Count, invalidReadoutPackets, invalidFundingPackets);
        return new(LoopClosurePredecessorResolutionKinds.Exact,
            readout, selected, 1, funding.Count, invalidReadoutPackets, invalidFundingPackets);
    }

    internal LoopLineageCheckpointDelta CaptureCheckpointDelta()
    {
        if (_checkpointReceiptCursor < 0 || _checkpointReceiptCursor > _receipts.Count)
            throw new InvalidDataException("loop-lineage receipt cursor is invalid");
        int count = _receipts.Count - _checkpointReceiptCursor;
        return new(_checkpointReceiptCursor, count == 0
            ? Array.Empty<LoopLineageEdgeReceipt>()
            : _receipts.GetRange(_checkpointReceiptCursor, count).ToArray());
    }

    internal void ApplyCheckpointDelta(in LoopLineageCheckpointDelta delta)
    {
        if (delta.Receipts is null || delta.Cursor < 0)
            throw new InvalidDataException("loop-lineage receipt cursor gap");
        RestoreFromTape();
        if (delta.Cursor > _receipts.Count || delta.Receipts.Length != _receipts.Count - delta.Cursor)
            throw new InvalidDataException("loop-lineage tape mutation does not contain the exact receipt tail");
        for (int index = 0; index < delta.Receipts.Length; index++)
        {
            LoopLineageEdgeReceipt expected = delta.Receipts[index];
            LoopLineageEdgeReceipt observed = _receipts[delta.Cursor + index];
            if (!ReceiptBytesEqual(expected, observed))
                throw new InvalidDataException($"loop-lineage checkpoint tail differs at receipt {delta.Cursor + index}");
        }
        _checkpointReceiptCursor = _receipts.Count;
    }

    internal void CommitCheckpointDelta() => _checkpointReceiptCursor = _receipts.Count;

    private static bool ReceiptBytesEqual(LoopLineageEdgeReceipt expected, LoopLineageEdgeReceipt observed)
        => expected.Encode().AsSpan().SequenceEqual(observed.Encode());

    internal static void WriteCheckpointDelta(CkptWriter writer, in LoopLineageCheckpointDelta delta)
    {
        writer.U8(1); writer.I32(delta.Cursor);
        LoopLineageEdgeReceipt[] receipts = delta.Receipts ?? Array.Empty<LoopLineageEdgeReceipt>();
        writer.I32(receipts.Length);
        foreach (LoopLineageEdgeReceipt edge in receipts) writer.Bytes(edge.Encode());
    }

    internal static LoopLineageCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    { if (reader.U8() != 1) throw new InvalidDataException("unknown loop-lineage checkpoint delta version"); int cursor = reader.I32(); int count = reader.I32(); if (cursor < 0 || count < 0 || count > 1_000_000) throw new InvalidDataException("loop-lineage receipt delta exceeds bound"); LoopLineageEdgeReceipt[] receipts = new LoopLineageEdgeReceipt[count]; for (int i = 0; i < count; i++) receipts[i] = LoopLineageEdgeReceipt.Decode(reader.Bytes()); return new(cursor, receipts); }

    internal void RestoreFromTape()
    {
        _latest.Clear();
        _nodes.Clear();
        _worldRootsByEvent.Clear();
        _nodesByEvent.Clear();
        _receipts.Clear();
        _previous = "";
        _sequence = 0;
        LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(_tape);
        IReadOnlyList<LoopLineageEdgeReceipt> restored = LoopLineageVerifier.ReadTapeEdges(source);
        if (restored.Count > 0)
        {
            LoopLineageOccurrenceCheckResult structural = LoopLineageVerifier.Verify(restored, source);
            if (!structural.Passed)
                throw new InvalidDataException($"loop lineage tape is structurally invalid: {structural.Detail}");
        }
        foreach (LoopLineageEdgeReceipt edge in restored)
        {
            _receipts.Add(edge);
            if (!_nodes.TryAdd(edge.Node.NodeID, edge.Node))
                throw new InvalidDataException($"loop lineage repeats node {edge.Node.NodeID}");
            if (!_nodesByEvent.TryAdd(edge.Node.EventID, edge.Node.NodeID))
                throw new InvalidDataException($"loop lineage repeats causal event {edge.Node.EventID}");
            if (edge.Node.Species == LoopLineageNodeSpecies.AdmissionPlan)
            {
                if (!IsWorldOpportunity(edge.Node.EventID))
                    throw new InvalidDataException($"loop lineage world root {edge.Node.EventID} is not an exact corpus admission");
                if (!_worldRootsByEvent.TryAdd(edge.Node.EventID, edge.Node.NodeID))
                    throw new InvalidDataException($"loop lineage repeats world root for corpus event {edge.Node.EventID}");
            }
            _latest[(edge.Node.CausalID, edge.Node.Species)] = edge.Node.NodeID;
            _previous = edge.CanonicalLineageSHA256;
            _sequence++;
        }
        _checkpointReceiptCursor = _receipts.Count;
    }

    public bool TryEmit(
        int step,
        LoopLineageNodeSpecies species,
        TapeEventID eventID,
        GrammarRevisionID? grammarRevision = null,
        IReadOnlyList<LoopLineageNodeID>? predecessorIDs = null,
        LoopLineageCausalID causalID = default)
    {
        try
        {
            Emit(step, species, eventID, grammarRevision, predecessorIDs, causalID);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public LoopLineageEdgeReceipt Emit(
        int step,
        LoopLineageNodeSpecies species,
        TapeEventID eventID,
        GrammarRevisionID? grammarRevision = null,
        IReadOnlyList<LoopLineageNodeID>? predecessorIDs = null,
        LoopLineageCausalID causalID = default)
    {
        if (!_tape.Resolve(eventID, out byte[] payload))
            throw new InvalidDataException($"lineage turnstile event {eventID} cannot be resolved");
        if (_nodesByEvent.ContainsKey(eventID))
            throw new InvalidDataException($"lineage causal event {eventID} already has an admitted node");
        // Three species take their causal identity from the MERGE of their support rather than
        // by inheritance: a law and its support stand on the exact set of world roots that
        // corroborated them, and a promotion on the exact laws it consumed. Inheriting from the
        // first predecessor would let a two-support law masquerade as its first support's rail.
        bool mergesCausalIdentity = species is LoopLineageNodeSpecies.PatternGrammarAdmission
            or LoopLineageNodeSpecies.VerifiedLaw or LoopLineageNodeSpecies.VerifiedLawSupport;
        if (mergesCausalIdentity && predecessorIDs is { Count: > 0 })
        {
            LoopLineageCausalID merged = LoopLineageCausalID.Merge(species, predecessorIDs);
            if (causalID.IsValid && causalID != merged)
                throw new InvalidDataException($"lineage {species} emission declares a causal key that is not its support merge");
            causalID = merged;
        }
        else if (!causalID.IsValid)
        {
            if (predecessorIDs is { Count: > 0 } && _nodes.TryGetValue(predecessorIDs[0], out LoopLineageNode predecessor))
                causalID = predecessor.CausalID;
            else if (species == LoopLineageNodeSpecies.AdmissionPlan)
                causalID = new LoopLineageCausalID($"world:{eventID.Value}");
            else
                throw new InvalidDataException($"lineage {species} emission omits its causal opportunity key");
        }
        LoopLineageNodeID nodeID = new($"lineage-{(byte)species}-{eventID.Value}-{_sequence}");
        // Production lineage is an admission-path receipt, not a temporal join.
        // Every non-root edge must name the exact predecessor(s) that admitted it;
        // consulting the latest node on a causal rail would let unrelated events
        // inherit ancestry merely because they happened nearby.
        if (species != LoopLineageNodeSpecies.AdmissionPlan
            && (predecessorIDs is null || predecessorIDs.Count == 0))
            throw new InvalidDataException($"lineage {species} emission omits its exact predecessor IDs");
        if (species == LoopLineageNodeSpecies.AdmissionPlan
            && predecessorIDs is { Count: > 0 })
            throw new InvalidDataException("admission lineage roots cannot declare predecessors");
        if (species == LoopLineageNodeSpecies.AdmissionPlan && !IsWorldOpportunity(eventID))
            throw new InvalidDataException($"loop lineage world root {eventID} is not an exact corpus admission");
        if (species == LoopLineageNodeSpecies.AdmissionPlan
            && _worldRootsByEvent.ContainsKey(eventID))
            throw new InvalidDataException($"loop lineage repeats world root for corpus event {eventID}");
        IReadOnlyList<LoopLineageNodeID> predecessors = predecessorIDs ?? [];
        string[] predecessorDigests = predecessors.Select(id => _nodes.TryGetValue(id, out LoopLineageNode node)
            ? node.PayloadSHA256
            : throw new InvalidDataException($"lineage predecessor {id} has not been admitted")).ToArray();
        foreach (LoopLineageNodeID predecessorID in predecessors)
            if (!LoopLineageVerifier.AdmitsPredecessorSpecies(species, _nodes[predecessorID].Species))
                throw new InvalidDataException(
                    $"lineage predecessor species {_nodes[predecessorID].Species} is incompatible with {species}");
        LoopLineageNode nodeValue = new(nodeID, species, eventID,
            Convert.ToHexStringLower(SHA256.HashData(payload)), grammarRevision, causalID);
        LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
            new($"lineage-edge-{_receipts.Count}"), nodeValue, predecessors, predecessorDigests, _previous);
        TapePacketCreator.AppendLoopLineageEdge(_tape, _journal, step, in edge);
        _sequence++;
        _receipts.Add(edge); _nodes.Add(nodeID, nodeValue);
        _nodesByEvent.TryAdd(eventID, nodeID);
        if (species == LoopLineageNodeSpecies.AdmissionPlan)
        {
            if (!_worldRootsByEvent.TryAdd(eventID, nodeID))
                throw new InvalidDataException($"loop lineage repeats world root for corpus event {eventID}");
        }
        _latest[(causalID, species)] = nodeID; _previous = edge.CanonicalLineageSHA256;
        return edge;
    }

    public bool TryGetLatest(LoopLineageCausalID causalID, LoopLineageNodeSpecies species, out LoopLineageNodeID nodeID)
        => _latest.TryGetValue((causalID, species), out nodeID);

    public bool TryGetNodeForEvent(TapeEventID eventID, out LoopLineageNode node)
    {
        if (_nodesByEvent.TryGetValue(eventID, out LoopLineageNodeID nodeID))
        {
            node = _nodes[nodeID];
            return true;
        }
        node = default;
        return false;
    }

    public bool TryFindNode(Func<LoopLineageNode, bool> predicate, out LoopLineageNode node)
    {
        foreach (LoopLineageNode candidate in _nodes.Values)
            if (predicate(candidate)) { node = candidate; return true; }
        node = default;
        return false;
    }

    internal bool EnsureWorldOpportunities(
        int step,
        TapeEventID childAdmissionEventID,
        IReadOnlyList<TapeEventID> eventIDs,
        out IReadOnlyList<LoopLineageNode> worldNodes,
        GrammarRevisionID? grammarRevision = null)
    {
        // Every refusal below says WHICH clause refused and on what: a bare false here surfaces at
        // the caller as "invalid world opportunity" over an event that may be perfectly valid, and
        // the reader cannot tell an ordering violation from a species collision from a bad root.
        LastRefusal = "";
        worldNodes = Array.Empty<LoopLineageNode>();
        if (eventIDs is null || eventIDs.Count == 0 || childAdmissionEventID.Value < 0)
        {
            LastRefusal = $"no cited events, or child s{childAdmissionEventID.Value} is not a tape event";
            return false;
        }
        TapeEventID[] ordered = eventIDs.Distinct().OrderBy(static id => id.Value).ToArray();
        List<LoopLineageNode> selected = new(ordered.Length);
        List<TapeEventID> missing = new();
        foreach (TapeEventID eventID in ordered)
        {
            if (eventID.Value < 0 || eventID.Value >= childAdmissionEventID.Value)
            {
                LastRefusal = $"cited s{eventID.Value} is not earlier than its child s{childAdmissionEventID.Value}";
                return false;
            }
            if (_worldRootsByEvent.TryGetValue(eventID, out LoopLineageNodeID rootID))
            {
                if (!_nodes.TryGetValue(rootID, out LoopLineageNode root))
                {
                    LastRefusal = $"cited s{eventID.Value} indexes root {rootID.Value}, which is not a node";
                    return false;
                }
                if (root.Species != LoopLineageNodeSpecies.AdmissionPlan || root.EventID != eventID)
                {
                    LastRefusal = $"cited s{eventID.Value} is already held by a {root.Species} node on s{root.EventID.Value}";
                    return false;
                }
                selected.Add(root);
            }
            else missing.Add(eventID);
        }

        // Validate every uncited root before emitting any of them, so a malformed
        // opportunity cannot leave a partially materialized admission rail.
        foreach (TapeEventID eventID in missing)
        {
            if (!IsWorldOpportunity(eventID))
            {
                LastRefusal = $"cited s{eventID.Value} (source '{_tape.SourceOf(eventID)}') is not a world opportunity under this runtime's root predicate";
                return false;
            }
        }
        foreach (TapeEventID eventID in missing)
        {
            LoopLineageEdgeReceipt root = Emit(step, LoopLineageNodeSpecies.AdmissionPlan, eventID, grammarRevision);
            selected.Add(root.Node);
        }
        worldNodes = selected.OrderBy(static node => node.EventID.Value).ToArray();
        return selected.Count > 0;
    }

    /// Why the last EnsureWorldOpportunities call refused. Read by the caller that turns the refusal
    /// into a typed throw, so the message names the clause instead of the symptom.
    internal string LastRefusal { get; private set; } = "";

    private bool IsWorldOpportunity(TapeEventID eventID)
        => _worldRootPredicate(_tape, eventID);

    private static bool IsCorpusWorldOpportunity(Tape tape, TapeEventID eventID)
    {
        if (eventID.Value <= 0
            || !tape.Resolve(eventID, out _)
            || !string.Equals(tape.SourceOf(eventID), "corpus", StringComparison.Ordinal)
            || tape.ProvenanceOf(eventID) != Provenances.Real)
            return false;
        TapeEventID receiptID = new(eventID.Value - 1);
        return tape.Resolve(receiptID, out byte[] receiptPayload)
            // Frozen tape source token world:encounter; identifier-side name is AdmissionPlan.
            && string.Equals(tape.SourceOf(receiptID), "world:encounter", StringComparison.Ordinal)
            && tape.ProvenanceOf(receiptID) == Provenances.Execution
            && TapePacketCreator.TryReadWorldEncounterObservation(receiptPayload, out TapeEventID observed)
            && observed == eventID;
    }

    public string CanonicalDigest
        => _receipts.Count == 0 ? "" : LoopLineageVerifier.ComputeCanonicalDigest(_receipts);

}

public sealed class LoopLineageAdjudication
{
    internal LoopLineageAdjudication(
        LoopLineageOccurrenceCheckResult original,
        LoopLineageOccurrenceCheckResult shuffled,
        LoopLineageShuffledNullReceipt nullReceipt,
        ulong permutationSeed,
        IReadOnlyList<LoopLineageEdgeReceipt> canonical,
        IReadOnlyList<LoopLineageEdgeReceipt> shuffledEdges)
    {
        Original = original; Shuffled = shuffled; NullReceipt = nullReceipt; PermutationSeed = permutationSeed;
        CanonicalEdges = canonical; ShuffledEdges = shuffledEdges;
    }

    public LoopLineageOccurrenceCheckResult Original { get; }
    public LoopLineageOccurrenceCheckResult Shuffled { get; }
    public LoopLineageShuffledNullReceipt NullReceipt { get; }
    public ulong PermutationSeed { get; }
    public IReadOnlyList<LoopLineageEdgeReceipt> CanonicalEdges { get; }
    public IReadOnlyList<LoopLineageEdgeReceipt> ShuffledEdges { get; }
}

/// Canonical ancestry verifier and R1 shuffled-predecessor adjudicator. It is deliberately
/// independent of Cortex state: the input is a sealed tape/journal snapshot and typed edge
/// receipts, so adjudication cannot write a run or influence its ordinary turnstiles.
public static class LoopLineageVerifier
{
    public const string NullDomain = "loop-closure-lineage-r1";
    private static readonly IReadOnlyDictionary<LoopLineageNodeSpecies, LoopLineageNodeSpecies[]> AllowedPredecessors =
        new Dictionary<LoopLineageNodeSpecies, LoopLineageNodeSpecies[]>
        {
            [LoopLineageNodeSpecies.AdmissionPlan] = [],
            [LoopLineageNodeSpecies.VerifiedLaw] = [LoopLineageNodeSpecies.AdmissionPlan],
            [LoopLineageNodeSpecies.VerifiedLawSupport] = [LoopLineageNodeSpecies.AdmissionPlan, LoopLineageNodeSpecies.VerifiedLaw],
            [LoopLineageNodeSpecies.Rung0Composition] = [LoopLineageNodeSpecies.VerifiedLaw],
            [LoopLineageNodeSpecies.DisplacedEvaluation] = [LoopLineageNodeSpecies.Rung0Composition],
            [LoopLineageNodeSpecies.LearnedReadout] = [LoopLineageNodeSpecies.DisplacedEvaluation],
            [LoopLineageNodeSpecies.Quota] = [LoopLineageNodeSpecies.LearnedReadout],
            [LoopLineageNodeSpecies.PaidDivergence] = [LoopLineageNodeSpecies.Quota, LoopLineageNodeSpecies.LearnedReadout],
            [LoopLineageNodeSpecies.AdjudicatedOutcome] = [LoopLineageNodeSpecies.PaidDivergence],
            [LoopLineageNodeSpecies.NewTapeEvidence] = [LoopLineageNodeSpecies.AdjudicatedOutcome],
            [LoopLineageNodeSpecies.PatternGrammarAdmission] = [LoopLineageNodeSpecies.VerifiedLaw, LoopLineageNodeSpecies.VerifiedLawSupport],
        };

    /// The predecessor law, readable by the emitter. A chain that the verifier will reject
    /// must not be emittable in the first place: the alternative is an organism that records
    /// an illegal lineage all run and only learns at rebind, with the run already spent.
    internal static bool AdmitsPredecessorSpecies(LoopLineageNodeSpecies species, LoopLineageNodeSpecies predecessor)
        => AllowedPredecessors.TryGetValue(species, out LoopLineageNodeSpecies[]? allowed)
            && allowed.Contains(predecessor);

    internal static int RequiredPredecessorCount(LoopLineageNodeSpecies species)
        => AllowedPredecessors.TryGetValue(species, out LoopLineageNodeSpecies[]? allowed) ? allowed.Length : 0;

    public static LoopLineageOccurrenceCheckResult Verify(
        IReadOnlyList<LoopLineageEdgeReceipt> receipts,
        LoopLineageTapeSnapshot? source = null,
        LoopLineageAuthority? authority = null)
    {
        try
        {
            if (receipts is null || receipts.Count == 0) return Invalid("lineage has no edge receipts");
            Dictionary<LoopLineageNodeID, LoopLineageNode> nodes = new();
            Dictionary<LoopLineageEdgeID, LoopLineageEdgeReceipt> edges = new();
            HashSet<TapeEventID> nodeEvents = new();
            string previous = "";
            foreach (LoopLineageEdgeReceipt edge in receipts)
            {
                edge.Validate();
                if (!edges.TryAdd(edge.EdgeID, edge)) return Invalid("lineage repeats an edge", edge.EdgeID);
                if (!nodes.TryAdd(edge.Node.NodeID, edge.Node)) return Invalid("lineage repeats a node", edge.EdgeID);
                if (!nodeEvents.Add(edge.Node.EventID)) return Invalid("lineage repeats a causal event", edge.EdgeID);
                if (!string.Equals(previous, edge.PreviousLineageSHA256, StringComparison.Ordinal))
                    return Invalid("lineage hash chain predecessor does not match", edge.EdgeID);
                previous = edge.CanonicalLineageSHA256;
            }

            foreach (LoopLineageEdgeReceipt edge in receipts)
            {
                LoopLineageNodeSpecies[] allowed = AllowedPredecessors[edge.Node.Species];
                bool variableBasis = edge.Node.Species is LoopLineageNodeSpecies.VerifiedLaw
                    or LoopLineageNodeSpecies.VerifiedLawSupport or LoopLineageNodeSpecies.Rung0Composition;
                if ((!variableBasis && edge.PredecessorIDs.Count != allowed.Length)
                    || (variableBasis && edge.PredecessorIDs.Count == 0))
                    return Invalid($"lineage {edge.Node.Species} requires {(variableBasis ? "one or more" : allowed.Length)} predecessors, observed {edge.PredecessorIDs.Count}", edge.EdgeID);
                for (int slot = 0; slot < edge.PredecessorIDs.Count; slot++)
                {
                    LoopLineageNodeID predecessorID = edge.PredecessorIDs[slot];
                    if (!nodes.TryGetValue(predecessorID, out LoopLineageNode predecessor))
                        return Invalid("lineage predecessor is not present", edge.EdgeID);
                    if (!allowed.Contains(predecessor.Species))
                        return Invalid($"lineage predecessor species {predecessor.Species} is incompatible with {edge.Node.Species}", edge.EdgeID);
                    if (!string.Equals(edge.PredecessorSHA256[slot], predecessor.PayloadSHA256, StringComparison.Ordinal))
                        return Invalid("lineage predecessor payload digest disagrees", edge.EdgeID);
                    if (predecessor.EventID.Value >= edge.Node.EventID.Value)
                        return Invalid("lineage predecessor is not earlier than its child", edge.EdgeID);
                }
                if (authority is not null && authority.Bindings.TryGetValue(edge.EdgeID, out IReadOnlyList<LoopLineageNodeID>? expected)
                    && !expected.SequenceEqual(edge.PredecessorIDs))
                    return new(LoopLineageOccurrenceCheckStatuses.FAIL, ComputeLineageDigest(receipts), edge.EdgeID, "authoritative predecessor binding changed");
                if (edge.Node.Species is LoopLineageNodeSpecies.VerifiedLaw or LoopLineageNodeSpecies.VerifiedLawSupport)
                {
                    LoopLineageCausalID merged = LoopLineageCausalID.Merge(edge.Node.Species, edge.PredecessorIDs);
                    if (edge.Node.CausalID != merged)
                        return Invalid("verified-law causal identity does not merge its exact world support", edge.EdgeID);
                }
                else if (edge.Node.Species == LoopLineageNodeSpecies.PatternGrammarAdmission)
                {
                    LoopLineageCausalID merged = LoopLineageCausalID.Merge(edge.Node.Species, edge.PredecessorIDs);
                    if (edge.Node.CausalID != merged)
                        return Invalid("theory-to-grammar promotion causal identity does not merge its law and support", edge.EdgeID);
                }
                else if (edge.Node.Species == LoopLineageNodeSpecies.Rung0Composition && edge.PredecessorIDs.Count > 1)
                {
                    LoopLineageCausalID merged = LoopLineageCausalID.Merge(edge.Node.Species, edge.PredecessorIDs);
                    if (edge.Node.CausalID != merged)
                        return Invalid("rung-0 causal identity does not merge its exact law basis", edge.EdgeID);
                }
                else if (edge.PredecessorIDs.Count > 0)
                {
                    LoopLineageCausalID inherited = nodes[edge.PredecessorIDs[0]].CausalID;
                    if (edge.Node.CausalID != inherited
                        || edge.PredecessorIDs.Any(id => nodes[id].CausalID != inherited))
                        return Invalid("lineage child does not inherit one exact causal opportunity", edge.EdgeID);
                }
            }

            if (source is not null)
            {
                Dictionary<TapeEventID, LoopLineagePayload> events = source.Events.ToDictionary(static item => item.EventID, static item => item.Payload);
                foreach (LoopLineageEdgeReceipt edge in receipts)
                {
                    if (!events.TryGetValue(edge.Node.EventID, out LoopLineagePayload payload))
                        return Invalid("lineage node event is absent from source tape", edge.EdgeID);
                    string payloadDigest = Convert.ToHexStringLower(SHA256.HashData(payload.Span));
                    if (!string.Equals(payloadDigest, edge.Node.PayloadSHA256, StringComparison.Ordinal))
                        return Invalid("lineage node payload digest disagrees with source tape", edge.EdgeID);
                }
            }

            return new(LoopLineageOccurrenceCheckStatuses.PASS, ComputeLineageDigest(receipts), new(""), "canonical ancestry verified");
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            return Invalid(ex.Message);
        }
    }

    public static LoopLineageAdjudication VerifyShuffledPredecessorNull(
        string authoritySHA256,
        LoopLineageTapeSnapshot source,
        IReadOnlyList<LoopLineageEdgeReceipt> receipts,
        string journalSHA256 = "",
        string domain = NullDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritySHA256);
        if (!IsDigest(authoritySHA256)) throw new InvalidDataException("loop lineage authority digest is malformed");
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(receipts);
        IReadOnlyList<LoopLineageEdgeReceipt> canonicalReceipts = OrderHashChain(receipts);
        LoopLineageAuthority authority = LoopLineageAuthority.Capture(canonicalReceipts);
        if (!string.Equals(authoritySHA256, authority.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("loop lineage authority digest does not match sealed predecessor bindings");
        if (journalSHA256.Length != 0)
            throw new InvalidDataException("loop lineage journal digest requires sealed journal lines");
        return VerifyShuffledPredecessorNullCore(authority.Digest, source, canonicalReceipts,
            DigestJournal([]), domain);
    }

    public static LoopLineageAdjudication VerifyShuffledPredecessorNull(
        LoopLineageTapeSnapshot source,
        IReadOnlyList<LoopLineageEdgeReceipt> receipts,
        IReadOnlyList<string> journalLines,
        string domain = NullDomain)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(journalLines);
        IReadOnlyList<LoopLineageEdgeReceipt> canonicalReceipts = OrderHashChain(receipts);
        LoopLineageAuthority authority = LoopLineageAuthority.Capture(canonicalReceipts);
        return VerifyShuffledPredecessorNullCore(authority.Digest, source, canonicalReceipts,
            DigestJournal(journalLines), domain);
    }

    private static LoopLineageAdjudication VerifyShuffledPredecessorNullCore(
        string authoritySHA256,
        LoopLineageTapeSnapshot source,
        IReadOnlyList<LoopLineageEdgeReceipt> canonicalReceipts,
        string journalSHA256,
        string domain)
    {
        // The physical tape view is not lineage order.  Reconstruct the chain before
        // taking authority or deriving the null seed so a sleep/reload reorder cannot
        // mint a different adjudication.
        LoopLineageAuthority authority = LoopLineageAuthority.Capture(canonicalReceipts);
        if (!string.Equals(authoritySHA256, authority.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("loop lineage authority digest does not match sealed predecessor bindings");
        LoopLineageOccurrenceCheckResult original = Verify(canonicalReceipts, source, authority);
        LoopLineagePredecessorView originalView = LoopLineagePredecessorView.Create(source, canonicalReceipts);
        ulong seed = DerivePermutationSeed(authoritySHA256, original.LineageSHA256, domain);
        List<LoopLineageEdgeReceipt> shuffled = Shuffle(canonicalReceipts, seed, out int eligibleBuckets, out int swappedEdges, out bool derangement, out string permutationDigest);
        LoopLineageOccurrenceCheckResult shuffledResult = Verify(shuffled, source, authority);
        LoopLineagePredecessorView shuffledView = LoopLineagePredecessorView.Create(source, shuffled);
        bool sameViews = VerifyEventConservation(source, originalView, shuffledView,
            out bool sameEvents, out bool samePayloads, out string conservationFailure);
        bool onlyBindingsChanged = VerifyOnlyPredecessorBindingsChanged(
            originalView.Edges, shuffledView.Edges, out string bindingFailure);
        bool locallyCompatible = VerifyEdgeLocalBindings(
            canonicalReceipts, shuffled, out string localBindingFailure);
        bool packetCustody = VerifyPacketBijection(source, canonicalReceipts, out string packetFailure);
        LoopLineageEdgeID discriminating = shuffledResult.FirstDiscriminatingEdge;
        if (original.Status != LoopLineageOccurrenceCheckStatuses.PASS)
            throw new InvalidDataException($"canonical lineage is not valid: {original.Detail}");
        if (eligibleBuckets == 0 || swappedEdges == 0 || !derangement)
            throw new InvalidDataException("loop lineage shuffle is vacuous or has no compatible derangement");
        if (!sameViews || !sameEvents || !samePayloads || !packetCustody || !onlyBindingsChanged || !locallyCompatible)
            throw new InvalidDataException($"loop lineage shuffle changed conserved source evidence: {conservationFailure}{bindingFailure}{localBindingFailure}{packetFailure}");
        if (shuffledResult.LineageSHA256 == original.LineageSHA256)
            throw new InvalidDataException("loop lineage shuffle did not change lineage");
        if (shuffledResult.Status != LoopLineageOccurrenceCheckStatuses.FAIL)
        {
            string statusDetail = shuffledResult.Status == LoopLineageOccurrenceCheckStatuses.INVALID
                ? $"loop lineage shuffle was INVALID: {shuffledResult.Detail}"
                : "loop lineage shuffle remained valid; canonical verifier did not reject the null";
            throw new InvalidDataException(statusDetail);
        }
        LoopLineageShuffledNullReceipt receipt = new(
            authoritySHA256, source.Digest, journalSHA256.Length == 0 ? DigestJournal([]) : journalSHA256,
            source.Events.Count, canonicalReceipts.Count, eligibleBuckets, seed, permutationDigest, swappedEdges, derangement,
            sameEvents, samePayloads, original.LineageSHA256, original.Status, shuffledResult.LineageSHA256,
            shuffledResult.Status, discriminating);
        receipt.Validate();
        return new(original, shuffledResult, receipt, seed, canonicalReceipts.ToArray(), shuffled);
    }

    public static ulong DerivePermutationSeed(string authoritySHA256, string lineageSHA256, string domain = NullDomain)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{authoritySHA256}|{lineageSHA256}|{domain}"));
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    public static string DigestJournal(IReadOnlyList<string> lines)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines) + (lines.Count == 0 ? "" : "\n"))));

    /// Digest a journal directly from disk. ReadLines removes the physical line
    /// ending and the canonical digest restores one LF after every logical row,
    /// exactly matching DigestJournal without retaining the full journal.
    internal static string DigestJournalFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string line in File.ReadLines(path))
        {
            digest.AppendData(Encoding.UTF8.GetBytes(line));
            digest.AppendData("\n"u8);
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    public static HashSet<long> ReadJournalEventIDs(IReadOnlyList<string> lines)
    {
        HashSet<long> events = new();
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (fields.Length >= 3 && fields[2].Length > 1 && fields[2][0] == 's'
                && long.TryParse(fields[2].AsSpan(1), out long value)) events.Add(value);
        }
        return events;
    }

    /// Compare the source map with both independent predecessor views. Event IDs are
    /// matched as a bijection; payload bytes and their SHA-256 digests must agree for
    /// every ID. The digest check is intentionally separate from byte equality so a
    /// malformed map cannot pass by preserving only cardinality.
    internal static bool VerifyEventConservation(
        LoopLineageTapeSnapshot source,
        LoopLineagePredecessorView original,
        LoopLineagePredecessorView shuffled,
        out bool sameEvents,
        out bool samePayloads,
        out string failure)
    {
        sameEvents = false;
        samePayloads = false;
        failure = "";
        if (ReferenceEquals(source.Events, original.Events) && ReferenceEquals(source.Events, shuffled.Events))
        {
            sameEvents = source.Events.Count == original.Events.Count && source.Events.Count == shuffled.Events.Count;
            samePayloads = sameEvents
                && string.Equals(source.Digest, original.EventDigest, StringComparison.Ordinal)
                && string.Equals(source.Digest, shuffled.EventDigest, StringComparison.Ordinal);
            return samePayloads;
        }

        if (!LoopLineageTapeSnapshot.TryBuildEventPayloadMap(source.Events, out Dictionary<long, LoopLineagePayload> sourceMap)
            || !LoopLineageTapeSnapshot.TryBuildEventPayloadMap(original.Events, out Dictionary<long, LoopLineagePayload> originalMap)
            || !LoopLineageTapeSnapshot.TryBuildEventPayloadMap(shuffled.Events, out Dictionary<long, LoopLineagePayload> shuffledMap))
        {
            failure = "source or predecessor view repeats an event ID or carries malformed payload bytes";
            return false;
        }

        bool originalMatch = CompareEventPayloadMaps(sourceMap, originalMap, source.Digest, original.EventDigest,
            out bool originalEvents, out bool originalPayloads);
        bool shuffledMatch = CompareEventPayloadMaps(sourceMap, shuffledMap, source.Digest, shuffled.EventDigest,
            out bool shuffledEvents, out bool shuffledPayloads);
        bool viewsMatch = CompareEventPayloadMaps(originalMap, shuffledMap, original.EventDigest, shuffled.EventDigest,
            out bool viewEvents, out bool viewPayloads);
        sameEvents = originalEvents && shuffledEvents && viewEvents;
        samePayloads = originalPayloads && shuffledPayloads && viewPayloads;
        if (!originalMatch || !shuffledMatch || !viewsMatch)
            failure = "source/original/shuffled event ID/payload maps are not a bijection with matching digests";
        return originalMatch && shuffledMatch && viewsMatch;
    }

    internal static bool CompareEventPayloadMaps(
        IReadOnlyDictionary<long, LoopLineagePayload> expected,
        IReadOnlyDictionary<long, LoopLineagePayload> observed,
        string expectedDigest,
        string observedDigest,
        out bool sameEvents,
        out bool samePayloads)
    {
        sameEvents = expected.Count == observed.Count
            && expected.Keys.All(observed.ContainsKey)
            && observed.Keys.All(expected.ContainsKey);
        samePayloads = sameEvents;
        if (!sameEvents) return false;
        foreach ((long eventID, LoopLineagePayload payload) in expected)
        {
            LoopLineagePayload observedPayload = observed[eventID];
            if (!payload.Span.SequenceEqual(observedPayload.Span))
            {
                samePayloads = false;
                return false;
            }
        }
        bool mapDigestMatches = string.Equals(expectedDigest, observedDigest, StringComparison.Ordinal);
        if (!mapDigestMatches) samePayloads = false;
        return samePayloads && mapDigestMatches;
    }

    /// The shuffled view may alter predecessor IDs/digests and the re-derived hash
    /// chain only. Node identity and payload custody remain byte-identical.
    internal static bool VerifyOnlyPredecessorBindingsChanged(
        IReadOnlyList<LoopLineageEdgeReceipt> original,
        IReadOnlyList<LoopLineageEdgeReceipt> shuffled,
        out string failure)
    {
        failure = "";
        if (original.Count != shuffled.Count)
        {
            failure = "predecessor permutation changed edge cardinality";
            return false;
        }
        for (int index = 0; index < original.Count; index++)
        {
            LoopLineageEdgeReceipt expected = original[index];
            LoopLineageEdgeReceipt observed = shuffled[index];
            if (expected.EdgeID != observed.EdgeID || expected.Node != observed.Node)
            {
                failure = $"predecessor permutation changed edge/node identity at index {index}";
                return false;
            }
            if (observed.PredecessorIDs.Count != observed.PredecessorSHA256.Count)
            {
                failure = $"predecessor permutation malformed edge {observed.EdgeID}";
                return false;
            }
        }
        return true;
    }

    /// Validate the null's local shape without applying canonical causal identity.
    /// A shuffled binding is expected to disagree with a node's merged causal ID;
    /// authority must report that disagreement as FAIL, while this corroboration proves the
    /// assignment itself stayed typed, digest-correct, and temporally executable.
    private static bool VerifyEdgeLocalBindings(
        IReadOnlyList<LoopLineageEdgeReceipt> canonical,
        IReadOnlyList<LoopLineageEdgeReceipt> shuffled,
        out string failure)
    {
        failure = "";
        if (canonical.Count != shuffled.Count)
        {
            failure = "edge-local shuffle changed edge cardinality";
            return false;
        }
        Dictionary<LoopLineageNodeID, LoopLineageNode> nodes = canonical
            .Select(static edge => edge.Node)
            .ToDictionary(static node => node.NodeID);
        for (int index = 0; index < canonical.Count; index++)
        {
            LoopLineageEdgeReceipt expected = canonical[index];
            LoopLineageEdgeReceipt observed = shuffled[index];
            if (expected.EdgeID != observed.EdgeID || expected.Node != observed.Node)
            {
                failure = $"edge-local shuffle changed edge/node identity at index {index}";
                return false;
            }
            if (observed.PredecessorIDs.Count != observed.PredecessorSHA256.Count)
            {
                failure = $"edge-local shuffle malformed predecessor slots on {observed.EdgeID}";
                return false;
            }
            LoopLineageNodeSpecies[] allowed = AllowedPredecessors[observed.Node.Species];
            for (int slot = 0; slot < observed.PredecessorIDs.Count; slot++)
            {
                if (!nodes.TryGetValue(observed.PredecessorIDs[slot], out LoopLineageNode predecessor))
                {
                    failure = $"edge-local shuffle imported unknown predecessor on {observed.EdgeID}";
                    return false;
                }
                if (!allowed.Contains(predecessor.Species))
                {
                    failure = $"edge-local shuffle imported {predecessor.Species} into {observed.Node.Species}";
                    return false;
                }
                if (!string.Equals(observed.PredecessorSHA256[slot], predecessor.PayloadSHA256, StringComparison.Ordinal))
                {
                    failure = $"edge-local shuffle imported a digest mismatch on {observed.EdgeID}";
                    return false;
                }
                if (predecessor.EventID.Value >= observed.Node.EventID.Value)
                {
                    failure = $"edge-local shuffle imported a later predecessor on {observed.EdgeID}";
                    return false;
                }
            }
        }
        return true;
    }

    /// Requires a one-to-one packet custody map.  Counting packets is insufficient:
    /// two copies of one edge would otherwise satisfy the count while another EdgeID
    /// had disappeared.
    internal static bool VerifyPacketBijection(
        LoopLineageTapeSnapshot source,
        IReadOnlyList<LoopLineageEdgeReceipt> receipts,
        out string failure)
    {
        failure = "";
        Dictionary<LoopLineageEdgeID, byte[]> expected = new();
        foreach (LoopLineageEdgeReceipt edge in receipts)
            if (!expected.TryAdd(edge.EdgeID, EncodePacket(edge)))
            {
                failure = $"lineage receipts repeat edge {edge.EdgeID}";
                return false;
            }

        Dictionary<LoopLineageEdgeID, LoopLineagePayload> observed = new();
        foreach (LoopLineageTapeEvent item in source.Events)
        {
            if (!TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out LoopLineageEdgeReceipt edge)) continue;
            if (!string.Equals(item.Source, "lineage", StringComparison.Ordinal)
                || item.Provenance != Provenances.Execution
                || item.Roles != TapeEventRoles.AuditOnly)
            {
                failure = $"lineage packet {item.EventID} has non-lineage source/provenance/roles";
                return false;
            }
            if (!observed.TryAdd(edge.EdgeID, item.Payload))
            {
                failure = $"lineage tape repeats packet EdgeID {edge.EdgeID}";
                return false;
            }
            if (!expected.TryGetValue(edge.EdgeID, out byte[]? packet)
                || !item.Payload.Span.SequenceEqual(packet))
            {
                failure = $"lineage packet {edge.EdgeID} bytes differ from its sealed edge";
                return false;
            }
        }

        if (observed.Count != expected.Count || expected.Keys.Any(key => !observed.ContainsKey(key)))
        {
            failure = "lineage tape packets are not a bijection over sealed EdgeIDs";
            return false;
        }
        return true;
    }

    /// The registered null is only eligible when every typed lineage packet has one
    /// exact loop-lineage journal row.  Event-ID membership alone is not custody: the
    /// row must carry the packet EdgeID, node, species, causal event, predecessor list,
    /// lineage digest, and encoded packet byte length.
    internal static bool VerifyJournalLineageRows(
        LoopLineageTapeSnapshot source,
        IReadOnlyList<string> lines,
        out string failure)
        => VerifyJournalLineageRows(source, (IEnumerable<string>)lines, out failure);

    internal static bool VerifyJournalLineageRows(
        LoopLineageTapeSnapshot source,
        string journalPath,
        out string failure)
    {
        if (!File.Exists(journalPath))
        {
            failure = $"journal is missing: {journalPath}";
            return false;
        }
        return VerifyJournalLineageRows(source, File.ReadLines(journalPath), out failure);
    }

    private static bool VerifyJournalLineageRows(
        LoopLineageTapeSnapshot source,
        IEnumerable<string> lines,
        out string failure)
    {
        failure = "";
        Dictionary<long, string> expected = new();
        foreach (LoopLineageTapeEvent item in source.Events)
        {
            if (!TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out LoopLineageEdgeReceipt edge)) continue;
            string suffix = $"edge={edge.EdgeID.Value}\tnode={edge.Node.NodeID.Value}\tspecies={edge.Node.Species}\tcausal-event={edge.Node.EventID}\tpredecessors={string.Join(',', edge.PredecessorIDs.Select(static id => id.Value))}\tlineage={edge.CanonicalLineageSHA256}\t{item.Payload.Length}B";
            if (!expected.TryAdd(item.EventID.Value, suffix))
            {
                failure = $"lineage tape repeats packet event {item.EventID}";
                return false;
            }
        }

        // Most sealed world runs carry no lineage packets yet.  The journal is
        // still a custody surface, but there is no reason to split a million
        // ordinary rows into field arrays when the expected packet set is empty.
        if (expected.Count == 0)
        {
            foreach (string line in lines)
                if (line.Contains("\tloop-lineage\t", StringComparison.Ordinal))
                {
                    failure = "journal contains a loop-lineage row for an unknown packet event";
                    return false;
                }
            return true;
        }

        HashSet<long> observed = new();
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (fields.Length < 2 || fields[1] != "loop-lineage") continue;
            if (fields.Length != 10 || !TryParseEventID(fields[2], out long eventID)
                || !expected.TryGetValue(eventID, out string? suffix))
            {
                failure = "journal contains a loop-lineage row for an unknown packet event";
                return false;
            }
            if (!observed.Add(eventID))
            {
                failure = $"journal repeats loop-lineage row for packet event s{eventID}";
                return false;
            }
            if (!string.Equals(string.Join('\t', fields[3..]), suffix, StringComparison.Ordinal))
            {
                failure = $"journal loop-lineage row for packet event s{eventID} does not match its packet";
                return false;
            }
        }
        if (observed.Count != expected.Count || expected.Keys.Any(key => !observed.Contains(key)))
        {
            failure = "journal is missing an exact loop-lineage row for a packet";
            return false;
        }
        return true;
    }

    private static bool TryParseEventID(string value, out long eventID)
    {
        eventID = -1;
        return value.Length > 1 && value[0] == 's'
            && long.TryParse(value.AsSpan(1), out eventID);
    }

    public static IReadOnlyList<LoopLineageEdgeReceipt> ReadTapeEdges(Tape tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        List<LoopLineageTapeEvent> events = new();
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] payload))
                throw new InvalidDataException($"loop lineage packet {view.Id} cannot be resolved");
            events.Add(new(view.Id, payload));
        }
        return ReadTapeEdges(events);
    }

    internal static IReadOnlyList<LoopLineageEdgeReceipt> ReadTapeEdges(LoopLineageTapeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ReadTapeEdges(snapshot.Events);
    }

    private static IReadOnlyList<LoopLineageEdgeReceipt> ReadTapeEdges(IEnumerable<LoopLineageTapeEvent> events)
    {
        List<LoopLineageEdgeReceipt> edges = new();
        foreach (LoopLineageTapeEvent item in events)
        {
            if (!TapePacketCreator.TryDecodeLoopLineageEdge(item.Payload.Span, out LoopLineageEdgeReceipt receipt)) continue;
            RequireLineagePacketMetadata(item);
            edges.Add(receipt);
        }
        return OrderHashChain(edges);
    }

    private static void RequireLineagePacketMetadata(in LoopLineageTapeEvent item)
    {
        if (!string.Equals(item.Source, "lineage", StringComparison.Ordinal)
            || item.Provenance != Provenances.Execution
            || item.Roles != TapeEventRoles.AuditOnly)
            throw new InvalidDataException(
                $"lineage packet event {item.EventID} has non-lineage source/provenance/roles");
    }

    /// Tape sleep may reorder resident events. Lineage order is therefore recovered
    /// from the receipt hash chain, never from the tape's current physical view.
    private static IReadOnlyList<LoopLineageEdgeReceipt> OrderHashChain(IReadOnlyList<LoopLineageEdgeReceipt> edges)
    {
        if (edges.Count == 0) return [];
        Dictionary<string, LoopLineageEdgeReceipt> byPrevious = new(StringComparer.Ordinal);
        foreach (LoopLineageEdgeReceipt edge in edges)
            if (!byPrevious.TryAdd(edge.PreviousLineageSHA256, edge))
                throw new InvalidDataException("loop lineage hash chain branches or repeats a predecessor digest");
        if (!byPrevious.TryGetValue("", out LoopLineageEdgeReceipt? current))
            throw new InvalidDataException("loop lineage hash chain has no root");
        List<LoopLineageEdgeReceipt> ordered = new(edges.Count);
        while (current is not null)
        {
            ordered.Add(current);
            byPrevious.Remove(current.PreviousLineageSHA256);
            byPrevious.TryGetValue(current.CanonicalLineageSHA256, out current);
        }
        if (ordered.Count != edges.Count || byPrevious.Count != 0)
            throw new InvalidDataException("loop lineage hash chain is disconnected");
        return ordered;
    }

    /// End-to-end mechanism fixture: source payloads enter a real Tape, typed edge packets
    /// cross the ordinary TapePacketCreator/Journal turnstiles, then the read-only verifier
    /// accepts the canonical chain and rejects its deterministic predecessor shuffle.
    public static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using Tape tape = new();
        Journal journal = new();
        LoopLineageNodeSpecies[] species = Enum.GetValues<LoopLineageNodeSpecies>();
        Dictionary<(LoopLineageNodeSpecies Species, int Chain), (LoopLineageNodeID ID, TapeEventID EventID, string Payload)> nodes = new();
        foreach (LoopLineageNodeSpecies kind in species)
            for (int chain = 0; chain < 2; chain++)
            {
                byte[] payload = Encoding.UTF8.GetBytes($"lineage-fixture-payload-{kind}-{chain}");
                TapeEventID eventID = tape.Append(payload, "lineage-fixture", Provenances.Real);
                nodes[(kind, chain)] = (new($"node-{(byte)kind}-{chain}"), eventID, Convert.ToHexStringLower(SHA256.HashData(payload)));
            }

        List<LoopLineageEdgeReceipt> edges = new();
        Dictionary<(LoopLineageNodeSpecies Species, int Chain), LoopLineageCausalID> causalIDs = new();
        string previous = "";
        int edgeNumber = 0;
        foreach (LoopLineageNodeSpecies kind in species)
            for (int chain = 0; chain < 2; chain++)
            {
                (LoopLineageNodeID nodeID, TapeEventID eventID, string payload) = nodes[(kind, chain)];
                List<LoopLineageNodeID> predecessorIDs = new();
                List<string> predecessorDigests = new();
                if (kind != LoopLineageNodeSpecies.AdmissionPlan)
                {
                    LoopLineageNodeSpecies parent = kind switch
                    {
                    LoopLineageNodeSpecies.PaidDivergence => LoopLineageNodeSpecies.Quota,
                        LoopLineageNodeSpecies.Rung0Composition => LoopLineageNodeSpecies.VerifiedLaw,
                        LoopLineageNodeSpecies.VerifiedLawSupport => LoopLineageNodeSpecies.AdmissionPlan,
                        LoopLineageNodeSpecies.PatternGrammarAdmission => LoopLineageNodeSpecies.VerifiedLaw,
                        _ => (LoopLineageNodeSpecies)((byte)kind - 1),
                    };
                    (LoopLineageNodeID parentID, _, string parentPayload) = nodes[(parent, chain)];
                    predecessorIDs.Add(parentID); predecessorDigests.Add(parentPayload);
                    if (kind == LoopLineageNodeSpecies.PaidDivergence)
                    {
                        (LoopLineageNodeID readoutID, _, string readoutPayload) = nodes[(LoopLineageNodeSpecies.LearnedReadout, chain)];
                        predecessorIDs.Add(readoutID); predecessorDigests.Add(readoutPayload);
                    }
                    else if (kind == LoopLineageNodeSpecies.PatternGrammarAdmission)
                    {
                        (LoopLineageNodeID supportID, _, string supportPayload) = nodes[(LoopLineageNodeSpecies.VerifiedLawSupport, chain)];
                        predecessorIDs.Add(supportID); predecessorDigests.Add(supportPayload);
                    }
                }
                LoopLineageCausalID causalID = kind switch
                {
                    LoopLineageNodeSpecies.AdmissionPlan => new($"fixture:{chain}"),
                    LoopLineageNodeSpecies.VerifiedLaw or LoopLineageNodeSpecies.VerifiedLawSupport => LoopLineageCausalID.Merge(kind, predecessorIDs),
                    LoopLineageNodeSpecies.PatternGrammarAdmission => LoopLineageCausalID.Merge(kind, predecessorIDs),
                    LoopLineageNodeSpecies.Rung0Composition => causalIDs[(LoopLineageNodeSpecies.VerifiedLaw, chain)],
                    _ => causalIDs[((LoopLineageNodeSpecies)((byte)kind - 1), chain)],
                };
                LoopLineageNode node = new(nodeID, kind, eventID, payload,
                    kind is LoopLineageNodeSpecies.Rung0Composition
                        or LoopLineageNodeSpecies.LearnedReadout
                        or LoopLineageNodeSpecies.Quota
                        or LoopLineageNodeSpecies.PaidDivergence
                        or LoopLineageNodeSpecies.AdjudicatedOutcome
                        or LoopLineageNodeSpecies.NewTapeEvidence
                        or LoopLineageNodeSpecies.PatternGrammarAdmission
                        ? new GrammarRevisionID((ulong)(edgeNumber + 1)) : null,
                    causalID);
                LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
                    new($"edge-{edgeNumber++}"), node, predecessorIDs, predecessorDigests, previous);
                causalIDs[(kind, chain)] = causalID;
                edges.Add(edge); previous = edge.CanonicalLineageSHA256;
            }
        foreach (LoopLineageEdgeReceipt edge in edges)
            TapePacketCreator.AppendLoopLineageEdge(tape, journal, checked((int)edge.Node.EventID.Value), in edge);
        LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
        LoopLineageAdjudication adjudication = VerifyShuffledPredecessorNull(
            source, ReadTapeEdges(tape), journal.ResidentLines);
        LoopLineagePredecessorView originalView = LoopLineagePredecessorView.Create(source, adjudication.CanonicalEdges);
        LoopLineagePredecessorView shuffledView = LoopLineagePredecessorView.Create(source, adjudication.ShuffledEdges);
        bool edgeLocalCompatibility = VerifyEdgeLocalBindings(
            adjudication.CanonicalEdges, adjudication.ShuffledEdges, out string edgeLocalFailure);
        LoopLineageTapeEvent[] mutatedEvents = source.Events.Select(static item => item.Copy()).ToArray();
        byte[] mutatedPayload = mutatedEvents[0].Payload.ToArray();
        mutatedPayload[0] ^= 0x01;
        mutatedEvents[0] = new(mutatedEvents[0].EventID, mutatedPayload);
        LoopLineagePredecessorView mutatedView = LoopLineagePredecessorView.Create(
            LoopLineageTapeSnapshot.Create(mutatedEvents), adjudication.ShuffledEdges);
        bool mutationRejected = !VerifyEventConservation(source, originalView, mutatedView,
            out bool mutationSameEvents, out bool mutationSamePayloads, out _)
            && mutationSameEvents && !mutationSamePayloads;
        LoopLineageTapeSnapshot duplicateSnapshot = LoopLineageTapeSnapshot.Create(
            source.Events.Concat(new[] { source.Events[0].Copy() }).ToArray());
        bool duplicateRejected = !source.Conserves(duplicateSnapshot);
        bool invalidPatternTargetRejected = VerifyInvalidPatternTargetSpecies();
        bool theoryCustody = LoopClosureEvidenceCustody.VerifyPatternLineageCustodyFixture();
        bool fundingIdentity = Cortex.VerifyPolicyFundingLineageIdentityFixture(output);
        bool duplicateBuckets = VerifyDuplicatePredecessorBuckets();
        bool compatibleCycleSubset = VerifyCompatibleCycleSubset(output);
        bool openPathIgnored = VerifyOpenTemporalPathIgnored();
        bool theoryGrammarAdmission = EmlPatternGrammarAdmissionFixture.Verify(output);
        bool passed = adjudication.Original.Passed && adjudication.Shuffled.Status == LoopLineageOccurrenceCheckStatuses.FAIL
            && adjudication.NullReceipt.SameEvents && adjudication.NullReceipt.SamePayloads
            && adjudication.NullReceipt.Derangement && adjudication.NullReceipt.FirstDiscriminatingEdge.IsValid
            && edgeLocalCompatibility && mutationRejected && duplicateRejected && invalidPatternTargetRejected
            && theoryCustody && fundingIdentity && duplicateBuckets && compatibleCycleSubset && openPathIgnored
            && theoryGrammarAdmission && VerifyLazyWorldOpportunities(output)
            && VerifyDigestScale(output);
        output.WriteLine($"  loop lineage fixture · original={adjudication.Original.Status} · shuffled={adjudication.Shuffled.Status} · edge-local={(edgeLocalCompatibility ? "PASS" : edgeLocalFailure)} · swapped={adjudication.NullReceipt.SwappedEdgeCount} · edge={adjudication.NullReceipt.FirstDiscriminatingEdge} · mutation={(mutationRejected ? "REJECTED" : "ACCEPTED")} · duplicate={(duplicateRejected ? "REJECTED" : "ACCEPTED")} · duplicate-buckets={(duplicateBuckets ? "PASS" : "FAIL")} · compatible-cycle={(compatibleCycleSubset ? "PASS" : "FAIL")} · open-path={(openPathIgnored ? "IGNORED" : "MOVED")} · theory-target={(invalidPatternTargetRejected ? "REJECTED" : "ACCEPTED")} · theory-custody={(theoryCustody ? "EXACT" : "ACCEPTED")} · funding-identity={(fundingIdentity ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyDuplicatePredecessorBuckets()
    {
        using Tape tape = new();
        Journal journal = new();
        List<(LoopLineageNode Node, string PayloadSHA256)> roots = new(2);
        List<LoopLineageEdgeReceipt> edges = new();
        string previous = "";
        for (int index = 0; index < 2; index++)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"lineage-duplicate-root-{index}");
            TapeEventID eventID = tape.Append(payload, "lineage-duplicate-fixture", Provenances.Real);
            LoopLineageNode node = new(new LoopLineageNodeID($"duplicate-root-{index}"),
                LoopLineageNodeSpecies.AdmissionPlan, eventID,
                Convert.ToHexStringLower(SHA256.HashData(payload)), null,
                new LoopLineageCausalID($"duplicate:{index}"));
            roots.Add((node, node.PayloadSHA256));
            LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
                new LoopLineageEdgeID($"duplicate-root-edge-{index}"), node, [], [], previous);
            edges.Add(edge);
            previous = edge.CanonicalLineageSHA256;
        }

        // Two child pairs deliberately share one predecessor value.  A compatible
        // null must move A→B and B→A while retaining event order; rotating binding
        // instances that carry the same value is not movement and is tested below.
        for (int index = 0; index < 4; index++)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"lineage-duplicate-child-{index}");
            TapeEventID eventID = tape.Append(payload, "lineage-duplicate-fixture", Provenances.Real);
            int rootIndex = index < 2 ? 0 : 1;
            LoopLineageNode root = roots[rootIndex].Node;
            LoopLineageNode node = new(new LoopLineageNodeID($"duplicate-child-{index}"),
                LoopLineageNodeSpecies.VerifiedLaw, eventID,
                Convert.ToHexStringLower(SHA256.HashData(payload)), null,
                LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLaw, [root.NodeID]));
            LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
                new LoopLineageEdgeID($"duplicate-child-edge-{index}"), node,
                [root.NodeID], [root.PayloadSHA256], previous);
            edges.Add(edge);
            previous = edge.CanonicalLineageSHA256;
        }
        foreach (LoopLineageEdgeReceipt edge in edges)
            TapePacketCreator.AppendLoopLineageEdge(tape, journal, checked((int)edge.Node.EventID.Value), in edge);

        LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
        LoopLineageAdjudication adjudication = VerifyShuffledPredecessorNull(
            source, edges, journal.ResidentLines);
        bool duplicateEdgeLocal = VerifyEdgeLocalBindings(
            adjudication.CanonicalEdges, adjudication.ShuffledEdges, out _);
        bool duplicateShuffle = adjudication.Original.Passed
            && adjudication.Shuffled.Status == LoopLineageOccurrenceCheckStatuses.FAIL
            && duplicateEdgeLocal
            && adjudication.NullReceipt.SwappedEdgeCount == 4;

        // Collapse the two values to one.  The bucket remains large, but every donor
        // is byte-identical to every target, so the semantic movement receipt must be
        // zero and the null must remain vacuous rather than inventing a swap.
        List<LoopLineageEdgeReceipt> allSame = new(edges.Count);
        previous = "";
        LoopLineageNodeID rootID = roots[0].Node.NodeID;
        string rootPayload = roots[0].PayloadSHA256;
        foreach (LoopLineageEdgeReceipt edge in edges)
        {
            LoopLineageEdgeReceipt rebound = edge.Node.Species == LoopLineageNodeSpecies.VerifiedLaw
                ? edge.Rebind([rootID], [rootPayload], previous)
                : edge.Rebind(edge.PredecessorIDs, edge.PredecessorSHA256, previous);
            allSame.Add(rebound);
            previous = rebound.CanonicalLineageSHA256;
        }
        List<LoopLineageEdgeReceipt> unchanged = Shuffle(allSame, 0xD00DUL,
            out int duplicateEligibleBuckets, out int duplicateSwappedEdges,
            out bool duplicateDerangement, out _);
        bool duplicateValuesIgnored = duplicateEligibleBuckets == 0 && duplicateSwappedEdges == 0
            && !duplicateDerangement
            && unchanged.Zip(allSame, static (actual, expected) => actual.PredecessorIDs.SequenceEqual(expected.PredecessorIDs))
                .All(static same => same);
        return duplicateShuffle && duplicateValuesIgnored;
    }

    private static bool VerifyCompatibleCycleSubset(TextWriter output)
    {
        using Tape tape = new();
        Journal journal = new();
        LoopLineageNode world220 = AppendFixtureNode(tape, "world220", LoopLineageNodeSpecies.AdmissionPlan,
            new LoopLineageCausalID("fixture:world220"));
        LoopLineageNode law220 = AppendFixtureNode(tape, "law220", LoopLineageNodeSpecies.VerifiedLaw,
            LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLaw, [world220.NodeID]));
        LoopLineageNode support226 = AppendFixtureNode(tape, "s226", LoopLineageNodeSpecies.VerifiedLawSupport,
            LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, [law220.NodeID]));
        LoopLineageNode support228 = AppendFixtureNode(tape, "s228", LoopLineageNodeSpecies.VerifiedLawSupport,
            LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, [law220.NodeID]));
        LoopLineageNode world281 = AppendFixtureNode(tape, "world281", LoopLineageNodeSpecies.AdmissionPlan,
            new LoopLineageCausalID("fixture:world281"));
        LoopLineageNode law281 = AppendFixtureNode(tape, "law281", LoopLineageNodeSpecies.VerifiedLaw,
            LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLaw, [world281.NodeID]));
        LoopLineageNode support283 = AppendFixtureNode(tape, "s283", LoopLineageNodeSpecies.VerifiedLawSupport,
            LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, [law281.NodeID]));
        LoopLineageNode support341 = AppendFixtureNode(tape, "s341", LoopLineageNodeSpecies.VerifiedLawSupport,
            LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, [law220.NodeID]));

        List<LoopLineageEdgeReceipt> edges = new();
        string previous = "";
        void Add(LoopLineageNode node, params LoopLineageNode[] predecessors)
        {
            LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
                new($"cycle-{node.NodeID.Value}"), node,
                predecessors.Select(static item => item.NodeID).ToArray(),
                predecessors.Select(static item => item.PayloadSHA256).ToArray(), previous);
            edges.Add(edge);
            previous = edge.CanonicalLineageSHA256;
        }
        Add(world220);
        Add(law220, world220);
        Add(support226, law220);
        Add(support228, law220);
        Add(world281);
        Add(law281, world281);
        Add(support283, law281);
        Add(support341, law220);
        foreach (LoopLineageEdgeReceipt edge in edges)
            TapePacketCreator.AppendLoopLineageEdge(tape, journal, checked((int)edge.Node.EventID.Value), in edge);

        LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
        LoopLineageAuthority authority = LoopLineageAuthority.Capture(edges);
        LoopLineageOccurrenceCheckResult canonical = LoopLineageVerifier.Verify(edges, source, authority);
        List<LoopLineageEdgeReceipt> shuffled = Shuffle(edges, 0xC1C1220UL,
            out int eligibleBuckets, out int swappedEdges, out bool derangement, out _);
        LoopLineageOccurrenceCheckResult nulled = LoopLineageVerifier.Verify(shuffled, source, authority);
        LoopLineagePredecessorView originalView = LoopLineagePredecessorView.Create(source, edges);
        LoopLineagePredecessorView shuffledView = LoopLineagePredecessorView.Create(source, shuffled);
        bool conserved = VerifyEventConservation(source, originalView, shuffledView,
            out bool sameEvents, out bool samePayloads, out _);
        bool identities = VerifyOnlyPredecessorBindingsChanged(edges, shuffled, out _);
        bool bindings = FlattenBindings(edges).SequenceEqual(FlattenBindings(shuffled));
        Dictionary<LoopLineageNodeID, LoopLineageEdgeReceipt> byNode = shuffled.ToDictionary(static edge => edge.Node.NodeID);
        bool blockersUnchanged = byNode[support226.NodeID].PredecessorIDs.SequenceEqual([law220.NodeID])
            && byNode[support228.NodeID].PredecessorIDs.SequenceEqual([law220.NodeID]);
        bool cycleMoved = byNode[support283.NodeID].PredecessorIDs.SequenceEqual([law220.NodeID])
            && byNode[support341.NodeID].PredecessorIDs.SequenceEqual([law281.NodeID]);
        bool firstEdge = nulled.FirstDiscriminatingEdge == new LoopLineageEdgeID("cycle-s283");
        bool passed = canonical.Status == LoopLineageOccurrenceCheckStatuses.PASS
            && nulled.Status == LoopLineageOccurrenceCheckStatuses.FAIL
            && eligibleBuckets == 1 && swappedEdges == 2 && derangement
            && conserved && sameEvents && samePayloads && identities && bindings
            && blockersUnchanged && cycleMoved && firstEdge
            && ComputeLineageDigest(edges) != ComputeLineageDigest(shuffled);
        output.WriteLine($"  lineage compatible-cycle subset · canonical={canonical.Status} · shuffled={nulled.Status} · blockers={(blockersUnchanged ? "unchanged" : "MOVED")} · cycle={(cycleMoved ? "s283/s341" : "BROKEN")} · swapped={swappedEdges} · bindings={(bindings ? "conserved" : "BROKEN")} · first={(firstEdge ? nulled.FirstDiscriminatingEdge : "unexpected")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyOpenTemporalPathIgnored()
    {
        using Tape tape = new();
        LoopLineageNode[] worlds = new LoopLineageNode[3];
        LoopLineageNode[] laws = new LoopLineageNode[3];
        LoopLineageNode[] rungs = new LoopLineageNode[3];
        LoopLineageNode[] evaluations = new LoopLineageNode[3];
        string[] suffixes = ["372", "432", "560"];
        for (int index = 0; index < suffixes.Length; index++)
        {
            worlds[index] = AppendFixtureNode(tape, $"world{suffixes[index]}", LoopLineageNodeSpecies.AdmissionPlan,
                new LoopLineageCausalID($"fixture:world{suffixes[index]}"));
            laws[index] = AppendFixtureNode(tape, $"law{suffixes[index]}", LoopLineageNodeSpecies.VerifiedLaw,
                LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLaw, [worlds[index].NodeID]));
            rungs[index] = AppendFixtureNode(tape, $"r{suffixes[index]}", LoopLineageNodeSpecies.Rung0Composition,
                laws[index].CausalID);
            evaluations[index] = AppendFixtureNode(tape, $"e{(index == 0 ? "373" : index == 1 ? "433" : "561")}", LoopLineageNodeSpecies.DisplacedEvaluation,
                rungs[index].CausalID);
        }
        List<LoopLineageEdgeReceipt> edges = new();
        string previous = "";
        void Add(LoopLineageNode node, LoopLineageNode predecessor)
        {
            LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
                new($"open-{node.NodeID.Value}"), node, [predecessor.NodeID], [predecessor.PayloadSHA256], previous);
            edges.Add(edge);
            previous = edge.CanonicalLineageSHA256;
        }
        foreach (LoopLineageNode world in worlds)
        {
            LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
                new($"open-{world.NodeID.Value}"), world, [], [], previous);
            edges.Add(edge);
            previous = edge.CanonicalLineageSHA256;
        }
        Add(laws[0], worlds[0]); Add(laws[1], worlds[1]); Add(laws[2], worlds[2]);
        Add(rungs[0], laws[0]); Add(rungs[1], laws[1]); Add(rungs[2], laws[2]);
        Add(evaluations[0], rungs[0]); Add(evaluations[1], rungs[1]); Add(evaluations[2], rungs[2]);
        List<LoopLineageEdgeReceipt> shuffled = Shuffle(edges, 0x0A1E560UL,
            out int eligibleBuckets, out int swappedEdges, out bool derangement, out _);
        return eligibleBuckets == 0 && swappedEdges == 0 && !derangement
            && FlattenBindings(edges).SequenceEqual(FlattenBindings(shuffled));
    }

    private static LoopLineageNode AppendFixtureNode(
        Tape tape, string id, LoopLineageNodeSpecies species, LoopLineageCausalID causalID)
    {
        byte[] payload = Encoding.UTF8.GetBytes($"lineage-cycle-fixture-{id}");
        TapeEventID eventID = tape.Append(payload, "lineage-cycle-fixture", Provenances.Real);
        return new(new LoopLineageNodeID(id), species, eventID,
            Convert.ToHexStringLower(SHA256.HashData(payload)), null, causalID);
    }

    private static IEnumerable<string> FlattenBindings(IReadOnlyList<LoopLineageEdgeReceipt> edges)
        => edges.SelectMany(static edge => edge.PredecessorIDs.Zip(edge.PredecessorSHA256,
            static (id, digest) => $"{id.Value}:{digest}"))
            .OrderBy(static binding => binding, StringComparer.Ordinal);

    private static bool VerifyInvalidPatternTargetSpecies()
    {
        PatternBecameThoughtCorroboration corroboration = new(
            new EmlPredictionID(0), new EmlPredictionID(1), new LoopLineageNodeID("theory-node"),
            new LoopClosureDigest(new string('a', 64)), new LoopClosureDigest(new string('b', 64)),
            0, 1, (EmlObligationTargetSpecies)byte.MaxValue);
        try
        {
            corroboration.Validate(requireCorroboration: true);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static bool VerifyDigestScale(TextWriter output)
    {
        const int eventCount = 1_100_000;
        byte[] payload = "lineage-digest-scale"u8.ToArray();
        string descending = LoopLineageTapeSnapshot.ComputeTapeDigest(EnumerateScale(eventCount, payload, descending: true));
        string ascending = LoopLineageTapeSnapshot.ComputeTapeDigest(EnumerateScale(eventCount, payload, descending: false));
        bool deterministic = string.Equals(descending, ascending, StringComparison.Ordinal);
        bool journal = VerifyJournalDigestScale(output);
        output.WriteLine($"  lineage digest scale · events={eventCount} · order-independent={(deterministic ? "yes" : "BROKEN")} · stack-safe={(deterministic ? "yes" : "BROKEN")} · journal-stream={(journal ? "yes" : "BROKEN")} · {(deterministic && journal ? "PASS" : "FAIL")}");
        return deterministic && journal;
    }

    private static bool VerifyJournalDigestScale(TextWriter output)
    {
        const int rowCount = 1_100_000;
        string path = Run.HomePath($".lineage-journal-scale-{Guid.NewGuid():N}.log");
        try
        {
            using IncrementalHash expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (StreamWriter writer = new(path, false, new UTF8Encoding(false), 64 * 1024))
            {
                for (int index = 0; index < rowCount; index++)
                {
                    string line = $"journal-scale-{index}";
                    writer.WriteLine(line);
                    expected.AppendData(Encoding.UTF8.GetBytes(line));
                    expected.AppendData("\n"u8);
                }
            }

            string expectedDigest = Convert.ToHexStringLower(expected.GetHashAndReset());
            string actualDigest = DigestJournalFile(path);
            using Tape tape = new();
            LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
            bool rows = VerifyJournalLineageRows(source, path, out _);
            bool passed = string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal) && rows;
            output.WriteLine($"  journal digest scale · rows={rowCount} · digest={(string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal) ? "exact" : "DRIFT")} · streaming={(rows ? "yes" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static IEnumerable<LoopLineageTapeEvent> EnumerateScale(int count, byte[] payload, bool descending)
    {
        if (descending)
        {
            for (int value = count - 1; value >= 0; value--)
                yield return new(new TapeEventID(value), payload);
        }
        else
        {
            for (int value = 0; value < count; value++)
                yield return new(new TapeEventID(value), payload);
        }
    }

    private static bool VerifyLazyWorldOpportunities(TextWriter output)
    {
        using Tape tape = new();
        Journal journal = new();
        List<byte[]> world = Enumerable.Range(0, 128)
            .Select(static index => Encoding.UTF8.GetBytes($"lazy-world-{index}"))
            .ToList();
        TapeEventID[] events = new TapeEventID[world.Count];
        for (int index = 0; index < world.Count; index++)
            events[index] = TapePacketCreator.CommitWorldEncounter(tape, journal, 0, world, index, 0, true, double.NaN);

        bool noEagerRoots = !LoopLineageVerifier.ReadTapeEdges(tape).Any();
        LoopLineageTurnstile lineage = new(tape, journal);
        TapeEventID firstAdmission = tape.Append("LAW\tclass\tlazy"u8.ToArray(), "eml:law", Provenances.Reflected);
        TapeEventID[] cited = [events[7], events[79], events[7]];
        if (!lineage.EnsureWorldOpportunities(0, firstAdmission, cited, out IReadOnlyList<LoopLineageNode> firstNodes))
            return false;
        int firstPacketCount = LoopLineageVerifier.ReadTapeEdges(tape).Count;
        TapeEventID secondAdmission = tape.Append("LAW\tclass\tlazy-repeat"u8.ToArray(), "eml:law", Provenances.Reflected);
        if (!lineage.EnsureWorldOpportunities(0, secondAdmission, cited, out IReadOnlyList<LoopLineageNode> secondNodes))
            return false;
        IReadOnlyList<LoopLineageEdgeReceipt> edges = LoopLineageVerifier.ReadTapeEdges(tape);
        LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
        bool onlyCited = edges.Count == 2
            && edges.All(edge => edge.Node.Species == LoopLineageNodeSpecies.AdmissionPlan
                && cited.Contains(edge.Node.EventID));
        bool reused = firstNodes.Select(static node => node.NodeID).SequenceEqual(secondNodes.Select(static node => node.NodeID));
        bool custody = LoopLineageVerifier.VerifyPacketBijection(source, edges, out _)
            && LoopLineageVerifier.VerifyJournalLineageRows(source, journal.ResidentLines, out _);
        bool malformedRestoreRejected = VerifyMalformedWorldRootRestore();
        bool passed = noEagerRoots && firstPacketCount == 2 && onlyCited && reused && custody
            && malformedRestoreRejected;
        output.WriteLine($"  lazy world lineage · world={world.Count} · eager-roots={(noEagerRoots ? 0 : -1)} · cited={firstNodes.Count} · reused={(reused ? "yes" : "no")} · packets={edges.Count} · malformed-restore={(malformedRestoreRejected ? "REJECTED" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyMalformedWorldRootRestore()
    {
        using Tape tape = new();
        Journal journal = new();
        byte[] payload = "not-a-corpus-encounter"u8.ToArray();
        TapeEventID eventID = tape.Append(payload, "fixture:bogus", Provenances.Reflected);
        LoopLineageNode node = new(new LoopLineageNodeID("malformed-world-root"),
            LoopLineageNodeSpecies.AdmissionPlan, eventID,
            Convert.ToHexStringLower(SHA256.HashData(payload)), null,
            new LoopLineageCausalID($"world:{eventID.Value}"));
        LoopLineageEdgeReceipt edge = LoopLineageEdgeReceipt.Create(
            new LoopLineageEdgeID("malformed-world-edge"), node, [], [], "");
        TapePacketCreator.AppendLoopLineageEdge(tape, journal, 0, in edge);
        try
        {
            _ = new LoopLineageTurnstile(tape, journal);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static List<LoopLineageEdgeReceipt> Shuffle(
        IReadOnlyList<LoopLineageEdgeReceipt> source, ulong seed, out int eligibleBuckets,
        out int swappedEdges, out bool derangement, out string permutationDigest)
    {
        Dictionary<LoopLineageNodeID, LoopLineageNode> nodes = source.Select(static edge => edge.Node).ToDictionary(static node => node.NodeID);
        Dictionary<LoopLineageEdgeID, LoopLineageNode> edgeNodes = source.ToDictionary(static edge => edge.EdgeID, static edge => edge.Node);
        Dictionary<(LoopLineageNodeSpecies Child, int Slot, LoopLineageNodeSpecies Parent), List<LoopLineagePredecessorBinding>> buckets = new();
        for (int edgeIndex = 0; edgeIndex < source.Count; edgeIndex++)
            for (int slot = 0; slot < source[edgeIndex].PredecessorIDs.Count; slot++)
            {
                LoopLineageNodeID id = source[edgeIndex].PredecessorIDs[slot];
                if (!nodes.TryGetValue(id, out LoopLineageNode node)) throw new InvalidDataException("shuffle predecessor is unknown");
                var key = (source[edgeIndex].Node.Species, slot, node.Species);
                if (!buckets.TryGetValue(key, out List<LoopLineagePredecessorBinding>? bucket)) buckets[key] = bucket = new();
                bucket.Add(new(source[edgeIndex].EdgeID, slot, id, source[edgeIndex].PredecessorSHA256[slot]));
            }
        eligibleBuckets = 0;
        Dictionary<LoopLineageEdgeID, LoopLineageNodeID[]> IDs = source.ToDictionary(static edge => edge.EdgeID, static edge => edge.PredecessorIDs.ToArray());
        Dictionary<LoopLineageEdgeID, string[]> Digests = source.ToDictionary(static edge => edge.EdgeID, static edge => edge.PredecessorSHA256.ToArray());
        List<string> permutation = new();
        HashSet<LoopLineageEdgeID> movedEdges = new();
        int semanticMoves = 0;
        foreach ((_, List<LoopLineagePredecessorBinding> bucket) in buckets.OrderBy(static pair => pair.Key.Child).ThenBy(static pair => pair.Key.Slot).ThenBy(static pair => pair.Key.Parent))
        {
            // A bucket may contain an unshufflable prefix and a later closed cycle.
            // Select only vertex-disjoint compatible cycles; an open injective path
            // is not a permutation and must remain untouched.  The edge-local
            // constraints are fixed by the bucket plus predecessor-before-child.
            if (bucket.Count < 2
                || !TrySelectCompatibleCycles(bucket, nodes, edgeNodes, ref seed, out Dictionary<int, int> matching))
                continue;

            eligibleBuckets++;
            foreach ((int targetIndex, int donorIndex) in matching)
            {
                LoopLineagePredecessorBinding target = bucket[targetIndex];
                LoopLineagePredecessorBinding donor = bucket[donorIndex];
                // Equal predecessor values are not movement.  The matcher excludes
                // these pairs, so this guard also documents and pins the null's
                // semantic accounting when duplicate IDs occupy one bucket.
                if (target.PredecessorID == donor.PredecessorID
                    && string.Equals(target.PredecessorSHA256, donor.PredecessorSHA256, StringComparison.Ordinal))
                    continue;
                semanticMoves++;
                movedEdges.Add(target.EdgeID);
                IDs[target.EdgeID][target.Slot] = donor.PredecessorID;
                Digests[target.EdgeID][target.Slot] = donor.PredecessorSHA256;
                permutation.Add($"{target.EdgeID.Value}:{target.Slot}<-{donor.EdgeID.Value}:{donor.Slot}:{donor.PredecessorID.Value}");
            }
        }
        List<LoopLineageEdgeReceipt> result = new(source.Count);
        string previous = "";
        swappedEdges = 0;
        for (int i = 0; i < source.Count; i++)
        {
            LoopLineageEdgeReceipt edge = source[i];
            // Count semantic edge movement, not binding-instance movement.  Rotating
            // two identical predecessor values is observationally unchanged and must
            // not satisfy the null's movement receipt.
            if (movedEdges.Contains(edge.EdgeID)) swappedEdges++;
            result.Add(edge.Rebind(IDs[edge.EdgeID], Digests[edge.EdgeID], previous));
            previous = result[^1].CanonicalLineageSHA256;
        }
        derangement = permutation.Count > 0 && semanticMoves > 0;
        permutationDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', permutation))));
        return result;
    }

    private static bool TrySelectCompatibleCycles(
        IReadOnlyList<LoopLineagePredecessorBinding> bucket,
        IReadOnlyDictionary<LoopLineageNodeID, LoopLineageNode> nodes,
        IReadOnlyDictionary<LoopLineageEdgeID, LoopLineageNode> edgeNodes,
        ref ulong seed,
        out Dictionary<int, int> matching)
    {
        matching = new();
        List<int>[] candidates = new List<int>[bucket.Count];
        uint[] ordering = new uint[bucket.Count];
        for (int index = 0; index < ordering.Length; index++) ordering[index] = Next(ref seed);
        for (int targetIndex = 0; targetIndex < bucket.Count; targetIndex++)
        {
            LoopLineagePredecessorBinding target = bucket[targetIndex];
            if (!nodes.TryGetValue(target.PredecessorID, out LoopLineageNode targetPredecessor)
                || !edgeNodes.TryGetValue(target.EdgeID, out LoopLineageNode targetNode))
                return false;
            List<int> compatible = new();
            for (int donorIndex = 0; donorIndex < bucket.Count; donorIndex++)
            {
                LoopLineagePredecessorBinding donor = bucket[donorIndex];
                if (target.PredecessorID == donor.PredecessorID
                    && string.Equals(target.PredecessorSHA256, donor.PredecessorSHA256, StringComparison.Ordinal))
                    continue;
                if (!nodes.TryGetValue(donor.PredecessorID, out LoopLineageNode donorPredecessor)
                    || donorPredecessor.Species != targetPredecessor.Species
                    || donorPredecessor.EventID.Value >= targetNode.EventID.Value)
                    continue;
                compatible.Add(donorIndex);
            }
            compatible.Sort((left, right) => ordering[left] != ordering[right]
                ? ordering[left].CompareTo(ordering[right])
                : left.CompareTo(right));
            candidates[targetIndex] = compatible;
        }

        // The candidate digraph is dominated by the predecessor-before-child
        // chronology (donorPredecessor.EventID < targetNode.EventID, above), so in
        // the common case it is a dense DESCENDING DAG that carries no rotation at
        // all.  The retired matcher proved that absence by backtracking over every
        // simple path back to a fixed start — ~b·2^b per bucket, so b≈35 ran for
        // hours and b≈50 never terminated, once per adjudication over the full run
        // lineage.  Tarjan isolates the cyclic vertices (nontrivial SCCs) in O(V+E),
        // and we peel vertex-disjoint directed cycles ONLY inside those components.
        // Cycle-start selection and successor traversal both follow the seeded
        // `ordering`, so the extracted swap set is deterministic and replay-stable;
        // an open injective path forms no nontrivial SCC and is left untouched.
        bool[] available = Enumerable.Repeat(true, bucket.Count).ToArray();
        while (ExtractSeededCycle(candidates, available, ordering) is { } cycle)
            for (int index = 0; index < cycle.Count; index++)
            {
                matching[cycle[index]] = cycle[(index + 1) % cycle.Count];
                available[cycle[index]] = false;
            }
        return matching.Count > 0;
    }

    /// Peel one vertex-disjoint directed cycle from the available candidate digraph,
    /// or null when the available subgraph is acyclic.  Tarjan marks every vertex
    /// on a cycle (a nontrivial SCC) in O(V+E); we then walk one seeded cycle out of
    /// the deterministically-first such component.  A strongly-connected component
    /// has an out-edge at every vertex, so the forward walk revisits a path vertex
    /// within |SCC| steps — no backtracking, no exponential blowup.
    private static List<int>? ExtractSeededCycle(
        IReadOnlyList<int>[] candidates,
        IReadOnlyList<bool> available,
        uint[] ordering)
    {
        int[] component = TarjanComponents(candidates, available, out int[] componentSize);
        int start = -1;
        for (int vertex = 0; vertex < candidates.Length; vertex++)
        {
            if (!available[vertex] || component[vertex] < 0 || componentSize[component[vertex]] < 2) continue;
            if (start < 0 || ordering[vertex] < ordering[start]
                || (ordering[vertex] == ordering[start] && vertex < start)) start = vertex;
        }
        if (start < 0) return null;

        int scc = component[start];
        int[] pathPosition = new int[candidates.Length];
        Array.Fill(pathPosition, -1);
        List<int> path = [start];
        pathPosition[start] = 0;
        for (int vertex = start; ;)
        {
            int next = -1;
            foreach (int candidate in candidates[vertex])
                if (available[candidate] && component[candidate] == scc) { next = candidate; break; }
            if (next < 0) return null; // Unreachable for a genuine SCC; every member has an in-component out-edge.
            if (pathPosition[next] >= 0)
                return path.GetRange(pathPosition[next], path.Count - pathPosition[next]);
            pathPosition[next] = path.Count;
            path.Add(next);
            vertex = next;
        }
    }

    /// Tarjan strongly-connected components over the available candidate subgraph.
    /// Returns the component index per vertex (-1 for unavailable) and each
    /// component's size; a component of size >= 2 is exactly a set of vertices that
    /// lie on a directed cycle (self-loops are impossible — a binding is never its
    /// own donor).  Root and successor iteration are index/seed ordered, so the
    /// component assignment is deterministic.
    private static int[] TarjanComponents(
        IReadOnlyList<int>[] candidates,
        IReadOnlyList<bool> available,
        out int[] componentSize)
    {
        int count = candidates.Length;
        int[] component = new int[count];
        int[] index = new int[count];
        int[] lowlink = new int[count];
        bool[] onStack = new bool[count];
        Array.Fill(component, -1);
        Array.Fill(index, -1);
        Stack<int> stack = new();
        List<int> sizes = [];
        int nextIndex = 0;

        void StrongConnect(int vertex)
        {
            index[vertex] = lowlink[vertex] = nextIndex++;
            stack.Push(vertex);
            onStack[vertex] = true;
            foreach (int next in candidates[vertex])
            {
                if (!available[next]) continue;
                if (index[next] < 0)
                {
                    StrongConnect(next);
                    lowlink[vertex] = Math.Min(lowlink[vertex], lowlink[next]);
                }
                else if (onStack[next])
                    lowlink[vertex] = Math.Min(lowlink[vertex], index[next]);
            }
            if (lowlink[vertex] != index[vertex]) return;
            int componentID = sizes.Count;
            int size = 0;
            while (true)
            {
                int member = stack.Pop();
                onStack[member] = false;
                component[member] = componentID;
                size++;
                if (member == vertex) break;
            }
            sizes.Add(size);
        }

        for (int root = 0; root < count; root++)
            if (available[root] && index[root] < 0) StrongConnect(root);
        componentSize = sizes.ToArray();
        return component;
    }

    private static uint Next(ref ulong state)
    {
        state ^= state << 7; state ^= state >> 9; state ^= state << 8;
        return (uint)state;
    }

    public static string ComputeCanonicalDigest(IReadOnlyList<LoopLineageEdgeReceipt> edges)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', edges.Select(static edge => edge.CanonicalLineageSHA256)))));

    private static string ComputeLineageDigest(IReadOnlyList<LoopLineageEdgeReceipt> edges)
        => ComputeCanonicalDigest(edges);

    private static byte[] EncodePacket(LoopLineageEdgeReceipt edge)
    {
        byte[] body = edge.Encode();
        byte[] packet = new byte[TapePacketCreator.LoopLineagePrefix.Length + body.Length];
        TapePacketCreator.LoopLineagePrefix.CopyTo(packet);
        body.CopyTo(packet, TapePacketCreator.LoopLineagePrefix.Length);
        return packet;
    }

    private static LoopLineageOccurrenceCheckResult Invalid(string detail, LoopLineageEdgeID edge = default)
        => new(LoopLineageOccurrenceCheckStatuses.INVALID, "", edge, detail);

    private static LoopLineageOccurrenceCheckResult Reject(string detail, LoopLineageEdgeID edge = default)
        => new(LoopLineageOccurrenceCheckStatuses.FAIL, "", edge, detail);

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

public enum LoopLineageOccurrenceCheckStatuses : byte { PASS, FAIL, INVALID }

/// Receipt for the deterministic shuffled-predecessor null. It proves that the source
/// tape and edge bytes were held fixed while only typed predecessor bindings moved.
public sealed class LoopLineageShuffledNullReceipt
{
    public LoopLineageShuffledNullReceipt(
        string sourceAuthoritySHA256,
        string sourceTapeSHA256,
        string sourceJournalSHA256,
        int eventCount,
        int edgeCount,
        int eligibleBucketCount,
        ulong permutationSeed,
        string permutationSHA256,
        int swappedEdgeCount,
        bool derangement,
        bool sameEvents,
        bool samePayloads,
        string originalLineageSHA256,
        LoopLineageOccurrenceCheckStatuses originalStatus,
        string shuffledLineageSHA256,
        LoopLineageOccurrenceCheckStatuses shuffledStatus,
        LoopLineageEdgeID firstDiscriminatingEdge)
    {
        SourceAuthoritySHA256 = sourceAuthoritySHA256; SourceTapeSHA256 = sourceTapeSHA256; SourceJournalSHA256 = sourceJournalSHA256;
        EventCount = eventCount; EdgeCount = edgeCount; EligibleBucketCount = eligibleBucketCount; PermutationSeed = permutationSeed;
        PermutationSHA256 = permutationSHA256; SwappedEdgeCount = swappedEdgeCount; Derangement = derangement; SameEvents = sameEvents;
        SamePayloads = samePayloads; OriginalLineageSHA256 = originalLineageSHA256; OriginalStatus = originalStatus;
        ShuffledLineageSHA256 = shuffledLineageSHA256; ShuffledStatus = shuffledStatus; FirstDiscriminatingEdge = firstDiscriminatingEdge;
    }

    public string SourceAuthoritySHA256 { get; }
    public string SourceTapeSHA256 { get; }
    public string SourceJournalSHA256 { get; }
    public int EventCount { get; }
    public int EdgeCount { get; }
    public int EligibleBucketCount { get; }
    public ulong PermutationSeed { get; }
    public string PermutationSHA256 { get; }
    public int SwappedEdgeCount { get; }
    public bool Derangement { get; }
    public bool SameEvents { get; }
    public bool SamePayloads { get; }
    public string OriginalLineageSHA256 { get; }
    public LoopLineageOccurrenceCheckStatuses OriginalStatus { get; }
    public string ShuffledLineageSHA256 { get; }
    public LoopLineageOccurrenceCheckStatuses ShuffledStatus { get; }
    public LoopLineageEdgeID FirstDiscriminatingEdge { get; }

    public void Validate()
    {
        foreach (string digest in new[] { SourceAuthoritySHA256, SourceTapeSHA256, SourceJournalSHA256, PermutationSHA256, OriginalLineageSHA256, ShuffledLineageSHA256 })
            if (!IsDigest(digest)) throw new InvalidDataException("loop lineage shuffled null carries an invalid digest");
        if (EventCount < 0 || EdgeCount < 0 || EligibleBucketCount < 0 || SwappedEdgeCount < 0)
            throw new InvalidDataException("loop lineage shuffled null carries a negative count");
        if (!Enum.IsDefined(OriginalStatus) || !Enum.IsDefined(ShuffledStatus)) throw new InvalidDataException("loop lineage shuffled null status is unknown");
        if (SameEvents != SamePayloads) throw new InvalidDataException("loop lineage shuffled null event and payload conservation disagree");
        if (!SameEvents || !SamePayloads) throw new InvalidDataException("loop lineage shuffled null did not conserve source events and payloads");
        if (OriginalStatus != LoopLineageOccurrenceCheckStatuses.PASS || ShuffledStatus != LoopLineageOccurrenceCheckStatuses.FAIL)
            throw new InvalidDataException("loop lineage shuffled null statuses do not prove PASS→FAIL");
        if (OriginalLineageSHA256 == ShuffledLineageSHA256)
            throw new InvalidDataException("loop lineage shuffled null did not change the lineage digest");
        if (EligibleBucketCount == 0 || SwappedEdgeCount == 0 || !Derangement)
            throw new InvalidDataException("loop lineage shuffled null is vacuous or lacks a derangement");
        if (!FirstDiscriminatingEdge.IsValid) throw new InvalidDataException("loop lineage shuffled null omits its first discriminating edge");
    }

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

[RonObject]
internal partial class LoopLineageEdgeReceiptRON
{
    public string edgeID = "";
    public string nodeID = "";
    public string species = "";
    public long eventID;
    public string payloadSHA256 = "";
    public bool hasGrammarRevision;
    public ulong grammarRevision;
    public string causalID = "";
    public List<string> predecessorIDs = new();
    public List<string> predecessorSHA256 = new();
    public string previousLineageSHA256 = "";
    public string canonicalLineageSHA256 = "";
}
