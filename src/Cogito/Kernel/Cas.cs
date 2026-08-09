namespace Cogito.Cas;

using System.Buffers.Binary;
using Cogito.Codec;

// The memory: content-addressed storage + an append-only event log. "Everything is a packet,"
// but only the few packets the nucleus needs — each addressed by the BLAKE3 of its canonical bytes.

/// Universal wrapper for every stored object.
/// Wire: magic ‖ LE32(schema) ‖ LE32(version) ‖ LE32(len) ‖ payload.
public readonly struct Envelope(SchemaID schemaId, ushort version, ReadOnlyMemory<byte> payload)
{
    public readonly SchemaID SchemaId = schemaId;
    public readonly ushort Version = version;
    public readonly ReadOnlyMemory<byte> Payload = payload;

    private const int HeaderSize = 20;                          // magic(8) ‖ schema(4) ‖ version(4) ‖ len(4)
    private static ReadOnlySpan<byte> Magic => "COGITO\0\0"u8;  // 8 bytes; \0-padded to align the header

    /// This object's content address — H_blob over its canonical bytes.
    public BlobRef Address => Hash.Blob(SchemaId, Version, Payload.Span);

    /// Exact byte count `Encode` will write — size a destination span with this.
    public int EncodedLength => HeaderSize + Payload.Length;

    public int Encode(Span<byte> dest)
    {
        Magic.CopyTo(dest);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[8..], SchemaId.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[12..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[16..], (uint)Payload.Length);
        Payload.Span.CopyTo(dest[HeaderSize..]);
        return EncodedLength;
    }

    /// rejects bad magic / truncated / trailing
    public static Envelope Decode(ReadOnlySpan<byte> wire)
    {
        if (wire.Length < HeaderSize)
            throw new FormatException($"Envelope truncated: {wire.Length} bytes, need ≥{HeaderSize} for the header.");
        if (!wire[..8].SequenceEqual(Magic))
            throw new FormatException("Envelope magic mismatch: not a COGITO blob.");

        uint schema = BinaryPrimitives.ReadUInt32LittleEndian(wire[8..]);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(wire[12..]);
        uint payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(wire[16..]);

        if (schema > ushort.MaxValue)
            throw new FormatException($"Envelope schema id {schema} exceeds the u16 space.");
        if (version > ushort.MaxValue)
            throw new FormatException($"Envelope version {version} exceeds the u16 space.");

        long declared = (long)HeaderSize + payloadLen;
        if (wire.Length < declared)
            throw new FormatException($"Envelope truncated: header declares {payloadLen} payload byte(s) (total {declared}) but only {wire.Length} present.");
        if (wire.Length > declared)
            throw new FormatException($"Envelope has {wire.Length - declared} trailing byte(s): declared total {declared}, wire {wire.Length}.");

        byte[] payload = wire.Slice(HeaderSize, (int)payloadLen).ToArray();
        return new Envelope(new SchemaID((ushort)schema), (ushort)version, payload);
    }
}

/// Content-addressable store: Put returns the address, Get is by address. In-memory v0; disk-backed is a seam.
public sealed class ContentStore
{
    private readonly Dictionary<BlobRef, byte[]> _blobs = new();

    public BlobRef Put(in Envelope e)
    {
        byte[] bytes = new byte[e.EncodedLength];
        e.Encode(bytes);
        BlobRef addr = e.Address;
        _blobs[addr] = bytes;
        return addr;
    }

    public bool TryGet(BlobRef r, out Envelope e)
    {
        if (_blobs.TryGetValue(r, out byte[]? bytes))
        {
            e = Envelope.Decode(bytes);
            return true;
        }
        e = default;
        return false;
    }

    public Envelope Get(BlobRef r) => TryGet(r, out var e) ? e : throw new KeyNotFoundException(r.ToString());
    public int Count => _blobs.Count;

    /// MemStat census read — Σ stored blob bytes (the content-addressed payload mass). Counts only.
    public long ByteMass()
    {
        long bytes = 0;
        foreach (var b in _blobs.Values) bytes += b.Length;
        return bytes;
    }
}

/// Append-only, content-addressed event log — the agent's memory. Linear, 0-based EventID.
public sealed class EventLog(ContentStore store)
{
    private readonly ContentStore _store = store;
    private readonly List<Envelope> _events = new();
    private readonly List<Hash256> _hashes = new();

    /// Append an event; stores its bytes and binds H_event(eventId). Returns the assigned EventID.
    public EventID Append(in Envelope e)
    {
        EventID id = new((ulong)_events.Count);
        _store.Put(e);
        _events.Add(e);
        _hashes.Add(Hash.Event(e.SchemaId, e.Version, id, e.Payload.Span));
        return id;
    }

    public Envelope this[EventID id] => _events[(int)id.Value];
    public Hash256 HashOf(EventID id) => _hashes[(int)id.Value];

    public EventID Last => _events.Count > 0
        ? new EventID((ulong)(_events.Count - 1))
        : throw new InvalidOperationException("EventLog is empty; no Last event.");

    public long Count => _events.Count;

    /// MemStat census read — Σ event-envelope payload bytes (the event list's own mass, beside the store's copy).
    public long ByteMass()
    {
        long bytes = 0;
        foreach (var e in _events) bytes += e.Payload.Length;
        return bytes;
    }

    public IEnumerable<(EventID Id, Envelope E)> Range(EventID lo, EventID hi)
    {
        for (ulong i = lo.Value; i <= hi.Value; i++)
            yield return (new EventID(i), _events[(int)i]);
    }
}
