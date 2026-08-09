namespace Cogito;

using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE CHECKPOINT ──  mid-run durability for the drive (the safe-to-kill law). A 2000-step world-run killed at
// step 396 landed NOTHING resumable — grammar/tape only ever landed at LAND, the journal lived in memory, and the
// curve sat in a List<string>. This file makes the drive checkpointable: a full snapshot of every state-bearing
// organ (grammar · tape · journal · curriculum · reads · self-model · controller · metabolism · memory hierarchy ·
// the loop's own locals), written atomically to the run dir, reloadable into a byte-identical continuation.
//
// FORMAT: a hand-rolled deterministic BINARY, not RON — the payload is dominated by raw byte spans (the tape, MBs)
// and bit-exact doubles (homeostat baselines, CV caches; text round-trips of NaN/denormals are where byte-identity
// goes to die), so length-prefixed little-endian binary is the shape that keeps the Vow cheap: same state ⇒ same
// bytes, Save∘Load∘Save = identity (dictionaries are written key-sorted so a reloaded snapshot re-saves
// byte-identically). Each section opens with a tag so a truncated file (killed mid-write — impossible after the
// atomic tmp+rename, but belts) fails LOUD at the exact organ, never silently mis-splices state.
//
// THE RESUME CONTRACT (the hard proof): a run driven to step K, killed, resumed, produces a continuation
// byte-identical to the same run driven straight through — every artifact (curve.tsv · journal.log · grammar.txt ·
// sample.txt · selfstream.txt · excursions.txt) lands the same bytes. What makes that hold:
//   · every RNG is stateless (seed + step ⇒ the draw) — the "RNG position" IS the step counter, checkpointed;
//   · identity-keyed acceleration caches (Reads/FlatPool/GrokBell cover caches, EnergyPolicy's stride cache)
//     rebuild from the deserialized grammar; the PolicyReadoutCache is different: it is published decision state,
//     serialized key-sorted and stamped to one GrammarRevisionID so it cannot cross an install boundary;
//   · order-sensitive internals are rebuilt in their CONSTRUCTION order, not file order (Tape's content buckets
//     are id-ascending — append order — because Reorder never touches them and FindByContent's lowest-id-first
//     contract rides on that);
//   · the append-only artifacts (curve.tsv, journal.log) are truncated/rewritten to the checkpoint's horizon on
//     resume, so rows a kill left past the last checkpoint are shed, not double-counted.

/// Deterministic little-endian writer — the checkpoint's one encoding vocabulary. Thin over BinaryWriter (which is
/// LE by spec); doubles round-trip bit-exact (raw IEEE-754 bytes), strings are BinaryWriter's 7-bit-len UTF-8.
public sealed class CkptWriter(Stream stream) : IDisposable
{
    private readonly BinaryWriter _w = new(stream, Encoding.UTF8, leaveOpen: true);

    /// Absolute byte position in the checkpoint stream.  Codec-owned fixture receipts use this
    /// instead of rediscovering fields by scanning an encoded image.
    public long Position => _w.BaseStream.Position;

    public void U8(byte v)     => _w.Write(v);
    public void U16(ushort v)  => _w.Write(v);
    public void Bool(bool v)   => _w.Write(v);
    public void I32(int v)     => _w.Write(v);
    public void U32(uint v)    => _w.Write(v);
    public void I64(long v)    => _w.Write(v);
    public void U64(ulong v)   => _w.Write(v);
    public void F64(double v)  => _w.Write(v);          // raw 8 IEEE bytes — NaN payloads and all
    public void Str(string v)  => _w.Write(v);
    public void OptionalStr(string? v)
    {
        _w.Write(v is not null);
        if (v is not null) _w.Write(v);
    }
    public void Raw(ReadOnlySpan<byte> v) => _w.Write(v);
    public void Bytes(byte[] v) { _w.Write(v.Length); _w.Write(v); }

    /// Open a section — the tag is the integrity rail: a mis-spliced or truncated load fails AT the organ.
    public void Section(uint tag) => _w.Write(tag);

    public void Dispose() => _w.Dispose();
}

/// A write-only tee that folds every byte into an IncrementalHash on its way to `inner` — digest a serialized
/// artifact DURING the one real write instead of re-reading (or re-materializing) it afterward. Pass Stream.Null
/// as `inner` to digest a serialization without landing it anywhere.
public sealed class HashTeeStream(Stream inner, IncrementalHash hash) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        hash.AppendData(buffer, offset, count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        hash.AppendData(buffer);
        inner.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        ReadOnlySpan<byte> one = [value];
        hash.AppendData(one);
        inner.WriteByte(value);
    }
}

/// A write-only growable stream backed by ArrayPool — the full-image encode path (round-trip CLI + fork seed)
/// builds its ~36 MB image WITHOUT the MemoryStream double buffer (an oversized internal array that doubles from
/// scratch, plus a ToArray copy — ~100 MB of LOH churn per save at 36 MB). Growth arrays are rented and returned,
/// so repeated saves recycle the same pooled buffers; only ToArray's exact-size result escapes to the heap. The
/// durable checkpoint path never touches this — Save streams straight into Run.WriteAtomic. Not thread-safe.
internal sealed class PooledImageBuffer : Stream
{
    private byte[] _buffer;
    private int _length;

    public PooledImageBuffer(int capacity) => _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, 1 << 12));

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(_length + buffer.Length);
        buffer.CopyTo(_buffer.AsSpan(_length));
        _length += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        EnsureCapacity(_length + 1);
        _buffer[_length++] = value;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length) return;
        byte[] grown = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = grown;
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    protected override void Dispose(bool disposing)
    {
        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }
        base.Dispose(disposing);
    }
}

/// The mirror reader. `Expect` is the section gate — a tag mismatch names the broken organ instead of letting a
/// shifted read silently deserialize garbage into a neighbor's state.
public sealed class CkptReader(Stream stream) : IDisposable
{
    private readonly BinaryReader _r = new(stream, Encoding.UTF8, leaveOpen: true);

    public byte U8()      => _r.ReadByte();
    public ushort U16()   => _r.ReadUInt16();
    public bool Bool()    => _r.ReadBoolean();
    public int I32()      => _r.ReadInt32();
    public uint U32()     => _r.ReadUInt32();
    public long I64()     => _r.ReadInt64();
    public ulong U64()    => _r.ReadUInt64();
    public double F64()   => _r.ReadDouble();
    public string Str()   => _r.ReadString();
    public string? OptionalStr() => _r.ReadBoolean() ? _r.ReadString() : null;
    public byte[] Raw(int n) => _r.ReadBytes(n);
    public byte[] RawExact(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        byte[] bytes = _r.ReadBytes(n);
        if (bytes.Length != n)
            throw new InvalidDataException($"checkpoint byte field truncated: expected {n} bytes, got {bytes.Length}");
        return bytes;
    }
    public long RemainingBytes => _r.BaseStream.Length - _r.BaseStream.Position;
    public byte[] Bytes() => _r.ReadBytes(_r.ReadInt32());
    public byte[] Bytes(int maxLength)
    {
        if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength));
        int length = _r.ReadInt32();
        if (length < 0 || length > maxLength)
            throw new InvalidDataException($"checkpoint byte field length {length} exceeds bound {maxLength}");
        byte[] bytes = _r.ReadBytes(length);
        if (bytes.Length != length)
            throw new InvalidDataException($"checkpoint byte field truncated: expected {length} bytes, got {bytes.Length}");
        return bytes;
    }

    public void Expect(uint tag)
    {
        uint got = _r.ReadUInt32();
        if (got != tag) throw new InvalidDataException($"checkpoint section mismatch: expected 0x{tag:X8}, got 0x{got:X8} — corrupt or schema-skewed checkpoint");
    }

    public bool TryExpect(uint tag)
    {
        long at = stream.Position;
        if (stream.Length - at < sizeof(uint)) return false;
        uint got = _r.ReadUInt32();
        if (got != tag)
        {
            stream.Position = at;
            return false;
        }
        return true;
    }

    public void Dispose() => _r.Dispose();
}

/// The drive loop's own locals at the checkpoint — everything the `for` body carries between steps that is not
/// already an organ's state. `NextStep` is the step the resumed loop EXECUTES first; `CurveLen` is curve.tsv's
/// byte horizon (rows past it were written after this snapshot — a kill's orphans — and are truncated on resume).
/// `LastSleepBytes` anchors the homeostat's geometric sleep stride; `LastInduceOpb`/`LastBitsPerSpan`/
/// `PrevTapeCount` are the held cost senses (maintained both arms — passive when the homeostat is off).
/// `ForkStep`/`ForkVolumeFrac` are the replay-fork edge (−1/NaN pre-fork) — a resumed run must not re-fire the
/// fork boundary nor lose the frozen coverage readout its curve rows carry. `LastConsolidationPhaseRules`/`LastDNodes` are
/// the phase-3 convergence anchors (Δnodes_dream night-over-night + its last value for the curve column).
public readonly record struct CortexSnap(
    int NextStep, ulong GrammarRevision, long LastInduceBytes, int WallStreak,
    int TotalEvicted, int TotalPromoted, int LastSlotted, long LastBitsSaved, long CurveLen,
    long LastSleepBytes, double LastInduceOpb, double LastBitsPerSpan, long PrevTapeCount,
    int BreachConsolidationPhases, int BreachWindowResets, int ForkStep, double ForkVolumeFrac,
    int LastConsolidationPhaseRules, int LastDNodes);

/// Byte and managed-allocation receipt for one checkpoint serialization or atomic landing. The byte count is the
/// payload emitted by the serializer; allocation accounting is a diagnostic bracket, not part of the checkpoint.
public readonly record struct CheckpointWriteReceipt(long Bytes, long AllocatedBytes);

/// Identity of one effective checkpoint image and the physical chain that
/// produced it. The proof is independent of a child role or directory so it
/// can be checked once and consumed by every identical cold fanout arm.
public readonly record struct CheckpointRoundTripProof(
    string EffectiveImageSHA256,
    string EffectivePhysicalSHA256,
    string BasePhysicalSHA256,
    string PhysicalChainSHA256,
    string PersistedConfigDigest,
    int NextStep,
    bool SaveLoadSaveExact)
{
    public string BindingDigest => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
        EffectiveImageSHA256, EffectivePhysicalSHA256, BasePhysicalSHA256, PhysicalChainSHA256,
        PersistedConfigDigest, NextStep, SaveLoadSaveExact))));

    public bool IsBound => IsDigest(EffectiveImageSHA256) && IsDigest(EffectivePhysicalSHA256)
        && IsDigest(BasePhysicalSHA256) && IsDigest(PhysicalChainSHA256)
        && IsDigest(PersistedConfigDigest) && NextStep >= 0;

    internal bool Matches(in CheckpointRoundTripProof other)
        => string.Equals(EffectiveImageSHA256, other.EffectiveImageSHA256, StringComparison.Ordinal)
        && string.Equals(EffectivePhysicalSHA256, other.EffectivePhysicalSHA256, StringComparison.Ordinal)
        && string.Equals(BasePhysicalSHA256, other.BasePhysicalSHA256, StringComparison.Ordinal)
        && string.Equals(PhysicalChainSHA256, other.PhysicalChainSHA256, StringComparison.Ordinal)
        && string.Equals(PersistedConfigDigest, other.PersistedConfigDigest, StringComparison.Ordinal)
        && NextStep == other.NextStep
        && SaveLoadSaveExact == other.SaveLoadSaveExact;

    private static bool IsDigest(string value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// Read-only checkpoint Vow receipt. The verifier compares the effective delta-replayed image with an
/// in-memory reconstruction and re-encoding; it carries no landing authority and never writes the run.
internal readonly record struct CheckpointVowReceipt(
    bool Passed,
    int SectionsCompared,
    long EffectiveBytes,
    long ReencodedBytes,
    string EffectivePhysicalSHA256,
    string ReencodedPhysicalSHA256,
    string BasePhysicalSHA256,
    string ChainSHA256,
    bool ManifestUnchanged,
    string[] Failures)
{
    public string Summary => Passed
        ? $"PASS sections={SectionsCompared} bytes={EffectiveBytes} manifest=stable"
        : $"FAIL sections={SectionsCompared} bytes={EffectiveBytes}/{ReencodedBytes} · {string.Join("; ", Failures)}";
}

public static class Checkpoint
{
    public const string FileName = "checkpoint.bin";
    public const string DeltaFileName = "checkpoint.delta";
    public const string DeltaTailFileName = "checkpoint.delta.tail";
    internal const int CurrentSectionCount = 17;
    public const string CurrentDialect = "CORTEX0";
    internal const string RetiredVZMessage = "CORTEXZ checkpoint predates durable world admission plans; start a new run instead of parsing a shifted config layout";
    internal const string RetiredVXMessage = "CORTEXX checkpoint omits durable historical trial execution receipts; start a new run instead of restoring incomplete installed-revision history";
    internal const string RetiredVTMessage = "CORTEXT checkpoint predates split policy readout/candidate identity custody; start a new run instead of restoring ambiguous authority";
    internal const string RetiredVRMessage = "CORTEXR checkpoint predates persisted loop-closure causal checkpoint state; a new run is required";
    internal const string RetiredVQMessage = "CORTEXQ checkpoint omits the paired fuel schedule identity; start a new run instead of accepting checkpoint state as a behavior switch";
    internal const string RetiredVOMessage = "CORTEXO checkpoint predates the paired-gate arm switch schema; start a new run instead of parsing a shifted config layout";
    internal const string RetiredVNMessage = "CORTEXN checkpoint omits the exact installed grammar snapshot; start a new run instead of restoring divergent install-revision authority";
    internal const string RetiredVBMessage = "CORTEXB checkpoint carries retired Homeostat learner config fields; start a new run instead of parsing a shifted config layout";
    internal const string RetiredVCMessage = "CORTEXC checkpoint predates the policy-boundary checkpoint section; start a new run instead of parsing a shifted policy section";
    internal const string RetiredVDMessage = "CORTEXD checkpoint predates the pre-registered deep-rematch gate identity; start a new run instead of parsing a shifted config layout";
    internal const string RetiredVEMessage = "CORTEXE checkpoint predates the append-only grammar readout funding journal; start a new run instead of parsing a shifted cache layout";
    internal const string RetiredVFMessage = "CORTEXF checkpoint carries trial-shaped readout funding rows without grammar context identity; start a new run instead of parsing a shifted policy section";
    internal const string RetiredVHMessage = "CORTEXH checkpoint omits persisted trial adaptation transitions; start a new run instead of parsing a shifted policy state";
    internal const string RetiredVIMessage = "CORTEXI checkpoint omits persisted trial-frozen adaptation state; start a new run instead of parsing a shifted policy state";
    internal const string RetiredVJMessage = "CORTEXJ checkpoint omits persisted policy-boundary training mount lineage; start a new run instead of parsing a shifted policy state";
    internal const string RetiredVKMessage = "CORTEXK checkpoint omits persisted per-policy readout allocation state; start a new run instead of parsing a shifted policy section";
    internal const string RetiredVLMessage = "CORTEXL checkpoint carries revision-bound raw policy readout state; start a new run instead of parsing a shifted canonical policy section";
    internal const string RetiredVSMessage = "CORTEXS checkpoint carries pre-causal policy readout state; start a new run instead of accepting schema-skewed loop state";
    internal const string RetiredVYMessage = "CORTEXY checkpoint omits persisted run identity and explicit default selections; start a new run instead of restoring ambiguous runtime configuration";
    // The schema signature is the dialect. The current schema carries versioned canonical policy state identity and persisted loop-closure causal queues.
    public static ReadOnlySpan<byte> CurrentMagic => "CORTEX0\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVZ => "CORTEXZ\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVY => "CORTEXY\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVX => "CORTEXX\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVW => "CORTEXW\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVV => "CORTEXV\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVU => "CORTEXU\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVT => "CORTEXT\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVS => "CORTEXS\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVR => "CORTEXR\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVQ => "CORTEXQ\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVP => "CORTEXP\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVO => "CORTEXO\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVN => "CORTEXN\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVM => "CORTEXM\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVL => "CORTEXL\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVK => "CORTEXK\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVJ => "CORTEXJ\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVI => "CORTEXI\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVH => "CORTEXH\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVF => "CORTEXF\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVE => "CORTEXE\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVD => "CORTEXD\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVB => "CORTEXB\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVC => "CORTEXC\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicVA => "CORTEXA\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicV9 => "CORTEX9\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicV8 => "CORTEX8\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicV7 => "CORTEX7\n"u8;
    private static ReadOnlySpan<byte> LegacyMagic => "CORTEX6\n"u8;
    private static ReadOnlySpan<byte> LegacyMagicV4 => "CORTEX4\n"u8;

    internal static bool MatchesCurrentSchema(ReadOnlySpan<byte> image)
        => image.StartsWith(CurrentMagic);

    // section tags — one per organ, in file order (ASCII-ish fourcc for a legible hexdump)
    private const uint TagConfig     = 0x43464721;   // CFG!
    private const uint TagGuard      = 0x47554152;   // GUAR
    private const uint TagSnap       = 0x534E4150;   // SNAP
    private const uint TagGrammar    = 0x4752414D;   // GRAM
    // Frozen checkpoint wire tags: PUBG and GPTU. The identifier names describe the current install boundary.
    private const uint TagInstalledGrammar = 0x50554247; // PUBG
    private const uint TagInstallRevisionTuple = 0x47505455; // GPTU (snapshot-only CORTEXZ images omit this nested rail)
    private const uint TagTape       = 0x54415045;   // TAPE
    private const uint TagJournal    = 0x4A524E4C;   // JRNL
    private const uint TagCurriculum = 0x43555252;   // CURR
    private const uint TagReads      = 0x52454144;   // READ
    private const uint TagSelfStream  = 0x53454C46;   // SELF
    private const uint TagController = 0x43545254;   // CTRT
    private const uint TagMetabolism = 0x4D455441;   // META
    private const uint TagMemory     = 0x4D454D48;   // MEMH
    private const uint TagHomeostat  = 0x484F4D45;   // HOME
    private const uint TagLoom       = 0x4C4F4F4D;   // LOOM
    private const uint TagRhythm     = 0x5259544D;   // RYTM
    private const uint TagPolicies   = 0x504F4C59;   // POLY
    private const uint TagEnd        = 0x454E4421;   // END!

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  SAVE / LOAD — the whole-machine snapshot, organ by organ (each organ owns its own Save/Load; this file
    //  owns the ORDER, the tags, and the atomic landing)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Serialize the whole drive state to an in-memory image. This is intentionally the only caller that materializes
    /// the full image; durable saves use Write(Stream, ...) so the payload goes directly to the atomic sibling file.
    /// The resume verifier and fork seed are the two legitimate full-image consumers.
    internal static byte[] Encode(CortexRunConfig cfg, long corpusBytes, int poolCount, int families, in CortexSnap snap,
        in RePairResult g, InstallRevision installRevision, Tape tape, Journal journal, ICurriculum curriculum, Reads reads, SelfStream selfModel,
        WeightController controller, Metabolism metabolism, MemoryHierarchy memory, Homeostat homeo, Loom? loom, Rhythm rhythm,
        Cortex cortex)
    {
        return EncodeMeasured(cfg, corpusBytes, poolCount, families, snap, g, installRevision, tape, journal, curriculum, reads,
            selfModel, controller, metabolism, memory, homeo, loom, rhythm, cortex, out _);
    }

    /// Encode the verifier/fork image and return its byte plus managed-allocation receipt. Durable paths should use
    /// Save(Run, Action<Stream>) instead; this method exists so the round-trip CLI can account for its intentional
    /// full-image materialization separately from the stream-owned save path.
    internal static byte[] EncodeMeasured(CortexRunConfig cfg, long corpusBytes, int poolCount, int families, in CortexSnap snap,
        in RePairResult g, InstallRevision installRevision, Tape tape, Journal journal, ICurriculum curriculum, Reads reads, SelfStream selfModel,
        WeightController controller, Metabolism metabolism, MemoryHierarchy memory, Homeostat homeo, Loom? loom, Rhythm rhythm,
        Cortex cortex, out CheckpointWriteReceipt receipt)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        using PooledImageBuffer buffer = new(1 << 20);
        Write(buffer, cfg, corpusBytes, poolCount, families, snap, g, installRevision, tape, journal, curriculum, reads, selfModel,
            controller, metabolism, memory, homeo, loom, rhythm, cortex);
        byte[] image = buffer.ToArray();
        receipt = new CheckpointWriteReceipt(image.Length, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        return image;
    }

    /// Serialize directly into the caller-owned stream. This is the one checkpoint serialization authority: Encode
    /// wraps it with a MemoryStream for round-trip/fork materialization, while Save wraps it with Run.WriteAtomic for
    /// durable checkpoints. The field order and bytes are unchanged from the former Encode body.
    internal static CheckpointWriteReceipt Write(Stream stream, CortexRunConfig cfg, long corpusBytes, int poolCount,
        int families, in CortexSnap snap, in RePairResult g, InstallRevision installRevision, Tape tape, Journal journal, ICurriculum curriculum,
        Reads reads, SelfStream selfModel, WeightController controller, Metabolism metabolism, MemoryHierarchy memory,
        Homeostat homeo, Loom? loom, Rhythm rhythm, Cortex cortex)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        using (CkptWriter w = new(stream))
        {
            w.Raw(CurrentMagic);

            w.Section(TagConfig);     WriteConfig(w, cfg);
            w.Section(TagGuard);      w.I64(corpusBytes); w.I32(poolCount); w.I32(families);
            w.Section(TagSnap);
            w.I32(snap.NextStep); w.U64(snap.GrammarRevision); w.I64(snap.LastInduceBytes); w.I32(snap.WallStreak);
            w.I32(snap.TotalEvicted); w.I32(snap.TotalPromoted); w.I32(snap.LastSlotted); w.I64(snap.LastBitsSaved);
            w.I64(snap.CurveLen);
            w.I64(snap.LastSleepBytes); w.F64(snap.LastInduceOpb); w.F64(snap.LastBitsPerSpan); w.I64(snap.PrevTapeCount);
            w.I32(snap.BreachConsolidationPhases); w.I32(snap.BreachWindowResets);
            w.I32(snap.ForkStep); w.F64(snap.ForkVolumeFrac);
            w.I32(snap.LastConsolidationPhaseRules); w.I32(snap.LastDNodes);

            w.Section(TagGrammar);    WriteGrammar(w, g);
            w.Section(TagInstalledGrammar); WriteInstallRevision(w, in installRevision);
            w.Section(TagTape);       tape.Save(w);
            w.Section(TagJournal);    journal.Save(w);
            w.Section(TagCurriculum); curriculum.SaveState(w);
            // Reads' stride-window seen flag is keyed against the LIVE grammar instance; preserve that anchor for
            // Save∘Load∘Save identity rather than re-inducing or substituting an equivalent grammar value.
            w.Section(TagReads);      reads.Save(w, g);
            w.Section(TagSelfStream); selfModel.Save(w);
            w.Section(TagController); controller.Save(w);
            w.Section(TagMetabolism); metabolism.Save(w);
            w.Section(TagMemory);     memory.Save(w);
            w.Section(TagHomeostat);  homeo.Save(w); homeo.SaveAbsorptionState(w);
            w.Section(TagLoom);       w.Bool(loom is not null); loom?.Save(w);
            w.Section(TagRhythm);     rhythm.Save(w);
            w.Section(TagPolicies);   cortex.SavePolicyState(w);
            cortex.SaveLoopClosureState(w);
            w.Section(TagEnd);
        }
        return new CheckpointWriteReceipt(stream.Position, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    /// Land a serializer ATOMICALLY in the run dir. The payload streams directly to a temporary sibling, so a
    /// durable checkpoint never allocates a second full image before the rename.
    public static CheckpointWriteReceipt Save(Run run, Action<Stream> serialize)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(serialize);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long bytes = run.WriteAtomic(FileName, serialize);
        return new CheckpointWriteReceipt(bytes, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    /// Land an already-materialized image for legacy callers and fork adapters. New durable paths should pass a
    /// stream serializer to Save so the image is never materialized in the first place.
    public static long Save(Run run, byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Save(run, stream => stream.Write(image)).Bytes;
    }

    internal static (string FileName, string SHA256) SaveGrammarArtifact(Run run, in InstallRevision installRevision)
    {
        string fileName = $"grammar-revision-{installRevision.Revision.Value:X16}.bin";
        InstallRevision artifactInstallRevision = installRevision;
        using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        run.WriteAtomic(fileName, stream =>
        {
            using CkptWriter writer = new(new HashTeeStream(stream, sha));
            WriteInstallRevision(writer, in artifactInstallRevision);
        });
        string digest = Convert.ToHexStringLower(sha.GetHashAndReset());
        return (fileName, digest);
    }

    internal static InstallRevision LoadGrammarArtifact(string runDir, string fileName, string expectedSHA256, ulong revision)
    {
        string path = Path.Combine(runDir, fileName);
        byte[] bytes = File.ReadAllBytes(path);
        string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedSHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"grammar artifact digest mismatch: {fileName}");
        using MemoryStream stream = new(bytes, writable: false);
        using CkptReader reader = new(stream);
        try
        {
            InstallRevision installRevision = ReadInstallRevision(reader);
            if (reader.RemainingBytes != 0 || installRevision.Revision.Value != revision)
                throw new InvalidDataException("grammar install revision artifact has trailing bytes or a revision mismatch");
            return installRevision;
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException)
        {
            // CORTEXZ mutation artifacts written before the install revision tuple
            // carried only RePair grammar bytes. Restore those as an explicit
            // no-fold install revision; never infer a predecessor from the revision.
            stream.Position = 0;
            using CkptReader legacyReader = new(stream);
            RePairResult grammar = ReadGrammar(legacyReader);
            if (legacyReader.RemainingBytes != 0)
                throw new InvalidDataException("legacy grammar artifact has trailing bytes", error);
            GrammarSnapshot snapshot = new(new GrammarRevisionID(revision), grammar.Rules, grammar.Compressed, grammar.TotalSavings, grammar.AlphabetSize);
            return new InstallRevision(snapshot, GrammarDelta.CreateEmpty(snapshot.Revision));
        }
    }

    /// Append the typed mutation receipt after a keyframe has landed. The keyframe
    /// remains the sole resume authority; the mutation rail proves tape/journal/
    /// reads cursor continuity without serializing a second machine image.
    internal static CheckpointWriteReceipt SaveMutation(Run run, int fromStep, int nextStep, Tape tape, Journal journal, Reads reads,
        in GrammarArtifactDelta grammarArtifact = default, in CheckpointMutationState state = default,
        in CheckpointReplayContext replayContext = default)
        => CheckpointDelta.Append(run, fromStep, nextStep, tape, journal, reads, in grammarArtifact, in state, in replayContext);

    internal static void InitializeMutationRail(Run run, int nextStep)
        => CheckpointDelta.Initialize(run, nextStep);

    internal static byte[] LoadEffectiveImage(string runDir)
        => CheckpointDelta.LoadEffectiveImage(runDir);

    internal static void Compact(string runDir)
        => CheckpointDelta.Compact(runDir);

    internal static string LogicalStateSHA256(ReadOnlySpan<byte> image)
    {
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData("CORTEXN-LOGICAL\0"u8);
        digest.AppendData(image);
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    internal static string PhysicalSHA256(ReadOnlySpan<byte> image)
        => Convert.ToHexStringLower(SHA256.HashData(image));

    internal static CheckpointRoundTripProof CreateImageProof(ReadOnlySpan<byte> image, string persistedConfigDigest,
        int nextStep, bool saveLoadSaveExact)
    {
        if (!MatchesCurrentSchema(image))
            throw new InvalidDataException($"checkpoint proof requires the current {CurrentDialect} image dialect");
        if (string.IsNullOrWhiteSpace(persistedConfigDigest) || nextStep < 0)
            throw new InvalidDataException("checkpoint proof requires persisted config and nonnegative step");
        string effectivePhysical = PhysicalSHA256(image);
        return new(
            LogicalStateSHA256(image), effectivePhysical, effectivePhysical,
            CheckpointDelta.ChainSHA256ForImage(image), persistedConfigDigest, nextStep, saveLoadSaveExact);
    }

    internal static CheckpointRoundTripProof ReadImageProof(string runDir, string persistedConfigDigest,
        int expectedNextStep, bool saveLoadSaveExact)
    {
        (byte[] image, string basePhysical, string chain) = CheckpointDelta.ReadEffectiveSnapshot(runDir);
        return ReadImageProof(image, basePhysical, chain, persistedConfigDigest, expectedNextStep, saveLoadSaveExact);
    }

    internal static CheckpointRoundTripProof ReadImageProof(ReadOnlySpan<byte> image, string basePhysical, string chain,
        string persistedConfigDigest, int expectedNextStep, bool saveLoadSaveExact)
        => ReadImageProof(image.ToArray(), basePhysical, chain, persistedConfigDigest, expectedNextStep, saveLoadSaveExact);

    internal static CheckpointRoundTripProof ReadImageProof(byte[] image, string basePhysical, string chain,
        string persistedConfigDigest, int expectedNextStep, bool saveLoadSaveExact)
    {
        CortexRunConfig config = PeekConfig(image);
        string actualConfigDigest = Cortex.PersistedConfigDigest(config);
        if (!string.Equals(actualConfigDigest, persistedConfigDigest, StringComparison.Ordinal))
            throw new InvalidDataException("checkpoint proof persisted config digest disagrees with the image");
        int actualNextStep = PeekNextStep(image);
        if (actualNextStep != expectedNextStep)
            throw new InvalidDataException($"checkpoint proof step mismatch: expected {expectedNextStep}, got {actualNextStep}");
        string effectivePhysical = PhysicalSHA256(image);
        return new(LogicalStateSHA256(image), effectivePhysical, basePhysical, chain,
            actualConfigDigest, actualNextStep, saveLoadSaveExact);
    }

    // CkptReader is Stream-shaped, so a span must land in an array before it
    // can be decoded; the byte[] overloads wrap the caller's array copy-free
    // and win overload resolution at every byte[] call site.
    internal static CortexRunConfig PeekConfig(ReadOnlySpan<byte> image)
        => PeekConfig(image.ToArray());

    internal static CortexRunConfig PeekConfig(byte[] image)
    {
        using MemoryStream stream = new(image, writable: false);
        using CkptReader reader = new(stream);
        ReadMagic(reader);
        reader.Expect(TagConfig);
        return ReadConfig(reader);
    }

    internal static int PeekNextStep(ReadOnlySpan<byte> image)
        => PeekNextStep(image.ToArray());

    internal static int PeekNextStep(byte[] image)
    {
        using MemoryStream stream = new(image, writable: false);
        using CkptReader reader = new(stream);
        ReadMagic(reader);
        reader.Expect(TagConfig); ReadConfig(reader);
        reader.Expect(TagGuard); reader.I64(); reader.I32(); reader.I32();
        reader.Expect(TagSnap);
        return reader.I32();
    }

    /// Read ONLY the config (+ integrity magic) from a run dir's checkpoint — the resume entry needs the config
    /// FIRST to rebuild PHASE 0 (corpus/pool/curriculum) before the full state load restores into those organs.
    internal static CortexRunConfig PeekConfig(string runDir)
    {
        using var fs = new MemoryStream(LoadEffectiveImage(runDir), writable: false);
        using var r = new CkptReader(fs);
        ReadMagic(r);
        r.Expect(TagConfig);
        return ReadConfig(r);
    }

    internal static int NextStep(string runDir)
        => PeekNextStep(runDir);

    private static bool IsLegacyDelta(string deltaPath)
    {
        Span<byte> magic = stackalloc byte[11];
        using FileStream stream = File.Open(deltaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
        return magic[..read].SequenceEqual("CORTEXT-D1\n"u8);
    }

    /// Read ONLY the trained GRAMMAR from a run dir's checkpoint — the standing rules, no tape/journal/organs, no
    /// corpus guard. The TRANSFER-LEARNING entry (nav-v1's pretrained RepoGrok base): a consumer that only wants the
    /// grammar the run learned — to splice new material ONTO it — needn't reconstruct the run's exact corpus to pass
    /// the resume guards. Walks config→guard→snap up to TagGrammar, decodes it, and stops; the rest of the image is
    /// never touched. Read-once, reuse read-only.
    public static RePairResult PeekGrammar(string runDir)
    {
        using var fs = new MemoryStream(LoadEffectiveImage(runDir), writable: false);
        using var r = new CkptReader(fs);
        ReadMagic(r);
        r.Expect(TagConfig);  ReadConfig(r);
        r.Expect(TagGuard);   r.I64(); r.I32(); r.I32();   // skip the corpus guard (corpusBytes, poolCount, families) — no reconstruction needed
        r.Expect(TagSnap);                                       // skip the 18-word CortexSnap progress snapshot (the exact field shape LoadBody reads)
        r.I32(); r.U64(); r.I64(); r.I32(); r.I32(); r.I32(); r.I32(); r.I64(); r.I64();
        r.I64(); r.F64(); r.F64(); r.I64(); r.I32(); r.I32(); r.I32(); r.F64(); r.I32(); r.I32();
        r.Expect(TagGrammar); return ReadGrammar(r);
    }

    /// Read only the persisted next-step cursor from a checkpoint. This is a structural receipt check; it does not
    /// rebuild the Cortex world or drive a continuation. With a typed rail present the terminal horizon comes from
    /// the authenticated rail scan (ReadAuthority binds the rail to the keyframe and chain-verifies every record —
    /// the same LastToStep the Compact verb asserts against the materialized image), so no effective image is built.
    public static int PeekNextStep(string runDir)
    {
        lock (Cogito.Run.CheckpointWriteGate(runDir))
        {
            byte[] baseImage = File.ReadAllBytes(Path.Combine(runDir, FileName));
            string deltaPath = Path.Combine(runDir, DeltaFileName);
            if (File.Exists(deltaPath) && MatchesCurrentSchema(baseImage) && !IsLegacyDelta(deltaPath))
                return CheckpointDelta.ReadAuthority(runDir).LastToStep;
            return PeekNextStep(LoadEffectiveImage(runDir));
        }
    }

    /// Restore the whole drive state from the run dir's checkpoint into freshly-constructed organs. The caller
    /// (Cortex.Drive's resume path) has already rebuilt PHASE 0 deterministically from the config; the GUARD section
    /// proves the corpus it rebuilt is the one the checkpoint was cut from (a changed corpus makes byte-identity
    /// impossible — fail loud, never drift silently).
    public static (CortexSnap Snap, RePairResult Grammar, InstallRevision InstallRevision) Load(string runDir, long corpusBytes, int poolCount, int families,
        Tape tape, Journal journal, ICurriculum curriculum, Reads reads, SelfStream selfModel,
        WeightController controller, Metabolism metabolism, MemoryHierarchy memory, Homeostat homeo, Loom? loom, Rhythm rhythm,
        Cortex cortex,
        bool allowRuntimeWorldFork = false, bool readOnlyEffectiveImage = false)
    {
        // A normal resume loads the canonical keyframe, then folds the typed
        // mutation rail into the live organs. Read-only adjudication retains
        // the captured effective image so it never mutates a caller-owned world.
        byte[] image = readOnlyEffectiveImage
            ? CheckpointDelta.ReadEffectiveSnapshot(runDir).EffectiveImage
            : File.ReadAllBytes(Path.Combine(runDir, FileName));
        using var fs = new MemoryStream(image, writable: false);
        using var r = new CkptReader(fs);
        int policySchema = ReadMagic(r);
        (CortexSnap Snap, RePairResult Grammar, InstallRevision InstallRevision) loaded = LoadBody(r, corpusBytes, poolCount, families,
            tape, journal, curriculum, reads, selfModel, controller, metabolism, memory, homeo, loom, rhythm, cortex,
            allowRuntimeWorldFork, policySchema);
        if (!readOnlyEffectiveImage)
        {
            // LoadBody restores the persisted enable bit without hydrating any
            // derived lineage joins. Rebind only on the mutation-replay path;
            // read-only Vow must remain a pure Save∘Load identity.
            cortex.BindLoopLineage(tape, journal, curriculum.LineageWorldRootPredicate);
            CheckpointDeltaReplayReceipt replay = CheckpointDelta.ReplayInto(
                runDir, tape, journal, reads, curriculum, selfModel, controller, metabolism,
                memory, homeo, rhythm, cortex, loom);
            Trace.Cortex.Boundary("checkpoint.replay.bound",
                $"records={replay.RecordCount} horizon={replay.LastToStep} tape={tape.MutationCursor} loom_mark={(loom is null ? -1 : loom.SpliceIDMark)} loom_revision={(loom is null ? -1 : loom.MutationRevision)} loom_arena={(loom is null ? -1 : loom.LiveSymbols)}");
            if (replay.RecordCount != 0 && replay.LatestSnap is null)
                throw new InvalidDataException("typed mutation rail predates the complete CortexSnap replacement and cannot resume exactly");
            if (!replay.LatestGrammarArtifact.IsEmpty)
            {
                GrammarArtifactDelta artifact = replay.LatestGrammarArtifact;
                InstallRevision installRevision = LoadGrammarArtifact(runDir, artifact.FileName, artifact.SHA256, artifact.Revision);
                RePairResult grammar = installRevision.Snapshot.ToRePairResult();
                CortexSnap snap = replay.LatestSnap is CortexSnap resumedSnap
                    ? resumedSnap with { GrammarRevision = artifact.Revision }
                    : loaded.Snap with { NextStep = replay.LastToStep, GrammarRevision = artifact.Revision };
                loaded = (snap, grammar, installRevision);
            }
            else if (replay.RecordCount != 0)
            {
                CortexSnap snap = replay.LatestSnap is CortexSnap resumedSnap
                    ? resumedSnap
                    : loaded.Snap with { NextStep = replay.LastToStep };
                loaded = (snap, loaded.Grammar, loaded.InstallRevision);
            }
        }
        return loaded;
    }

    internal static Tape LoadTape(string runDir)
    {
        return LoadTape(LoadEffectiveImage(runDir), runDir);
    }

    /// Decode only the immutable tape view needed by lineage adjudication. A full
    /// Tape restore also rebuilds content buckets, source counters, and resident
    /// indexes; those are valuable to Cortex but pure waste for a read-only
    /// certificate reader. This path retains one payload array per visible event
    /// and never constructs the working Tape graph.
    internal static LoopLineageTapeSnapshot LoadLineageSnapshot(string runDir)
        => LoadLineageSnapshot(LoadEffectiveImage(runDir), runDir);

    internal static LoopLineageTapeSnapshot LoadLineageSnapshot(byte[] image, string runDir)
    {
        ArgumentNullException.ThrowIfNull(image);
        MemoryStream checkpoint = new(image, writable: false);
        CkptReader reader = new(checkpoint);
        try
        {
            _ = ReadMagic(reader);
            reader.Expect(TagConfig); _ = ReadConfig(reader);
            reader.Expect(TagGuard); reader.I64(); reader.I32(); reader.I32();
            reader.Expect(TagSnap);
            reader.I32(); reader.U64(); reader.I64(); reader.I32(); reader.I32(); reader.I32(); reader.I32(); reader.I64(); reader.I64();
            reader.I64(); reader.F64(); reader.F64(); reader.I64(); reader.I32(); reader.I32(); reader.I32(); reader.F64(); reader.I32(); reader.I32();
            reader.Expect(TagGrammar); _ = ReadGrammar(reader);
            reader.Expect(TagInstalledGrammar); _ = ReadInstallRevision(reader);
            reader.Expect(TagTape);

            int tapeHeader = reader.I32();
            bool tapeHasRoles = tapeHeader == -1;
            if (tapeHasRoles && reader.U8() != 2) throw new InvalidDataException("unknown tape full checkpoint schema");
            int residentCount = tapeHasRoles ? reader.I32() : tapeHeader;
            if (residentCount < 0) throw new InvalidDataException("checkpoint tape resident count is negative");
            List<(TapeEventID EventID, byte[] Payload)> events = new(residentCount);
            for (int i = 0; i < residentCount; i++)
            {
                TapeEventID id = new(reader.I64());
                _ = reader.Str();
                byte[] payload = reader.Bytes();
                _ = reader.U8();
                if (tapeHasRoles) _ = reader.U8();
                events.Add((id, payload));
            }

            int shedCount = reader.I32();
            if (shedCount < 0) throw new InvalidDataException("checkpoint tape shed count is negative");
            List<(long ID, int Length, long Offset)> shed = new(shedCount);
            for (int i = 0; i < shedCount; i++)
            {
                long id = reader.I64();
                _ = reader.Str(); _ = reader.U8();
                int length = reader.I32(); long offset = reader.I64();
                if (tapeHasRoles) _ = reader.U8();
                if (length < 0 || offset < 0) throw new InvalidDataException("checkpoint tape shed entry is malformed");
                shed.Add((id, length, offset));
            }

            int tombCount = reader.I32();
            if (tombCount < 0) throw new InvalidDataException("checkpoint tape tomb count is negative");
            for (int i = 0; i < tombCount; i++)
            {
                _ = reader.I64(); _ = reader.Str(); _ = reader.U8();
                int length = reader.I32(); long offset = reader.I64();
                if (tapeHasRoles) _ = reader.U8();
                if (length < 0 || offset < 0) throw new InvalidDataException("checkpoint tape tomb entry is malformed");
            }
            _ = reader.I64();

            if (shed.Count > 0)
            {
                string logPath = Path.Combine(runDir, "tape.spanlog");
                using FileStream log = File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                shed.Sort(static (left, right) => left.ID.CompareTo(right.ID));
                foreach ((long id, int length, long offset) in shed)
                {
                    if (offset > log.Length || length > log.Length - offset)
                        throw new InvalidDataException("checkpoint tape shed entry exceeds its byte log");
                    byte[] payload = new byte[length];
                    log.Seek(offset, SeekOrigin.Begin);
                    log.ReadExactly(payload);
                    events.Add((new TapeEventID(id), payload));
                }
            }

            return LoopLineageTapeSnapshot.CreateOwned(events);
        }
        finally
        {
            reader.Dispose();
            checkpoint.Dispose();
        }
    }

    /// Decode the tape from an already captured effective image. Read-only
    /// adjudication passes the immutable arm image through every consumer so a
    /// single checkpoint read is enough for the whole arm snapshot.
    internal static Tape LoadTape(byte[] image, string runDir)
    {
        ArgumentNullException.ThrowIfNull(image);
        MemoryStream checkpoint = new(image, writable: false);
        CkptReader reader = new(checkpoint);
        try
        {
            _ = ReadMagic(reader);
            reader.Expect(TagConfig); _ = ReadConfig(reader);
            reader.Expect(TagGuard); reader.I64(); reader.I32(); reader.I32();
            reader.Expect(TagSnap);
            reader.I32(); reader.U64(); reader.I64(); reader.I32(); reader.I32(); reader.I32(); reader.I32(); reader.I64(); reader.I64();
            reader.I64(); reader.F64(); reader.F64(); reader.I64(); reader.I32(); reader.I32(); reader.I32(); reader.F64(); reader.I32(); reader.I32();
            reader.Expect(TagGrammar); _ = ReadGrammar(reader);
            reader.Expect(TagInstalledGrammar); _ = ReadInstallRevision(reader);
            reader.Expect(TagTape);
            Tape tape = new();
            string logPath = Path.Combine(runDir, "tape.spanlog");
            if (File.Exists(logPath)) tape.MountLog(File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            tape.Load(reader);
            reader.Dispose();
            checkpoint.Dispose();
            return tape;
        }
        catch
        {
            reader.Dispose();
            checkpoint.Dispose();
            throw;
        }
    }

    /// Restore an already-read effective image into caller-owned organs. This overload is deliberately
    /// filesystem-free: read-only adjudicators use CheckpointDelta.ReadEffectiveSnapshot once, then keep the
    /// image and every re-encoding in memory. Durable resume continues to use the run-directory overload.
    internal static (CortexSnap Snap, RePairResult Grammar, InstallRevision InstallRevision) LoadImage(
        ReadOnlySpan<byte> image, long corpusBytes, int poolCount, int families,
        Tape tape, Journal journal, ICurriculum curriculum, Reads reads, SelfStream selfModel,
        WeightController controller, Metabolism metabolism, MemoryHierarchy memory, Homeostat homeo, Loom? loom, Rhythm rhythm,
        Cortex cortex, bool allowRuntimeWorldFork = false)
    {
        using MemoryStream stream = new(image.ToArray(), writable: false);
        using CkptReader reader = new(stream);
        int policySchema = ReadMagic(reader);
        return LoadBody(reader, corpusBytes, poolCount, families, tape, journal, curriculum, reads, selfModel,
            controller, metabolism, memory, homeo, loom, rhythm, cortex, allowRuntimeWorldFork, policySchema);
    }

    /// Restore every section in the one current schema after the fixed signature has been validated.
    private static (CortexSnap Snap, RePairResult Grammar, InstallRevision InstallRevision) LoadBody(CkptReader r, long corpusBytes, int poolCount, int families,
        Tape tape, Journal journal, ICurriculum curriculum, Reads reads, SelfStream selfModel,
        WeightController controller, Metabolism metabolism, MemoryHierarchy memory, Homeostat homeo, Loom? loom, Rhythm rhythm,
        Cortex cortex,
        bool allowRuntimeWorldFork = false,
        int policySchema = 5)
    {
        Stopwatch sectionClock = Stopwatch.StartNew();
        void MarkSection(string section)
        {
            long sectionMs = sectionClock.ElapsedMilliseconds;
            if (sectionMs >= 5) Trace.Cortex.Boundary("checkpoint.load.section", $"section={section} ms={sectionMs}");
            sectionClock.Restart();
        }

        r.Expect(TagConfig); CortexRunConfig ckCfg = ReadConfig(r);
        r.Expect(TagGuard);
        long ckCorpus = r.I64(); int ckPool = r.I32(); int ckFams = r.I32();
        // A held-out fork intentionally replaces only the runtime curriculum's world stream. Its curriculum
        // fingerprint resets stream position below while every learned organ still loads from this image.
        if (!allowRuntimeWorldFork && (ckCorpus != corpusBytes || ckPool != poolCount || ckFams != families))
            throw new InvalidDataException($"checkpoint corpus guard failed: checkpointed {ckCorpus}B/{ckPool} spans/{ckFams} domains, rebuilt {corpusBytes}B/{poolCount}/{families} — the corpus changed since the run; byte-identical resume is impossible");
        MarkSection("config-guard");

        r.Expect(TagSnap);
        CortexSnap snap = new(r.I32(), r.U64(), r.I64(), r.I32(), r.I32(), r.I32(), r.I32(), r.I64(), r.I64(),
                              r.I64(), r.F64(), r.F64(), r.I64(), r.I32(), r.I32(), r.I32(), r.F64(),
                              r.I32(), r.I32());
        MarkSection("snap");

        r.Expect(TagGrammar);    RePairResult g = ReadGrammar(r);
        MarkSection("grammar");
        r.Expect(TagInstalledGrammar); InstallRevision installRevision = ReadInstallRevision(r);
        if (installRevision.Revision.Value != snap.GrammarRevision) throw new InvalidDataException($"checkpoint install revision {installRevision.Revision.Value} disagrees with snapshot {snap.GrammarRevision}");
        MarkSection("published-grammar");
        r.Expect(TagTape);       tape.Load(r);
        MarkSection("tape");
        r.Expect(TagJournal);    journal.Load(r, tape);
        MarkSection("journal");
        curriculum.BindRuntimeTape(tape, journal);
        r.Expect(TagCurriculum); curriculum.LoadState(r);
        MarkSection("curriculum");
        r.Expect(TagReads);      reads.Load(r, g);   // g decoded above — the stride window re-anchors its identity key on the restored instance
        MarkSection("reads");
        r.Expect(TagSelfStream);  selfModel.Load(r);
        MarkSection("selfstream");
        r.Expect(TagController); controller.Load(r);
        MarkSection("controller");
        r.Expect(TagMetabolism); metabolism.Load(r);
        MarkSection("metabolism");
        r.Expect(TagMemory);     memory.Load(r, tape);   // rebuilds the persistent night-shift indexes from the restored tape (id order — Save∘Load ≡ the incremental accretion)
        MarkSection("memory");
        r.Expect(TagHomeostat);  homeo.Load(r); homeo.LoadAbsorptionState(r);
        MarkSection("homeostat");
        r.Expect(TagLoom);
        bool loomArmed = r.Bool();
        if (loomArmed != (loom is not null))
            throw new InvalidDataException($"checkpoint loom skew: image {(loomArmed ? "carries" : "lacks")} a loom, runtime {(loom is not null ? "armed" : "un-armed")} one — the config restore desynced");
            loom?.Load(r, tape);                             // rules+savings+high-water + typed arena → derived count/occurrence/heap rebuild
        if (loom is not null)
            tape.RestorePendingAppends(loom.SpliceIDMark);
        MarkSection("loom");
        r.Expect(TagRhythm);     rhythm.Load(r);
        MarkSection("rhythm");
        r.Expect(TagPolicies);
        cortex.LoadPolicyState(r, policySchema);
        cortex.LoadLoopClosureState(r);
        r.Expect(TagEnd);
        MarkSection("policies-loop");
        return (snap, g, installRevision);
    }

    private static int ReadMagic(CkptReader r)
    {
        byte[] magic = r.Raw(CurrentMagic.Length);
        if (magic.AsSpan().SequenceEqual(CurrentMagic)) return 13;
        if (magic.AsSpan().SequenceEqual(LegacyMagicVZ)) throw new InvalidDataException(RetiredVZMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVX)) throw new InvalidDataException(RetiredVXMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVY)) throw new InvalidDataException(RetiredVYMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVW)) return 9;
        if (magic.AsSpan().SequenceEqual(LegacyMagicVV)) return 8;
        if (magic.AsSpan().SequenceEqual(LegacyMagicVU)) return 7;
        if (magic.AsSpan().SequenceEqual(LegacyMagicVT)) throw new InvalidDataException(RetiredVTMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVS)) throw new InvalidDataException(RetiredVSMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVR)) throw new InvalidDataException(RetiredVRMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVQ)) throw new InvalidDataException(RetiredVQMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVP))
            throw new InvalidDataException("CORTEXP checkpoint omits the persisted arm-neutral EML deliberation budget; start a new run instead of parsing a shifted config layout");
        if (magic.AsSpan().SequenceEqual(LegacyMagicVO)) throw new InvalidDataException(RetiredVOMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVN)) throw new InvalidDataException(RetiredVNMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVM)) throw new InvalidDataException("CORTEXM checkpoint carries the pre-maturity policy state; start a new run instead of parsing a schema-skewed image");
        if (magic.AsSpan().SequenceEqual(LegacyMagicVL)) throw new InvalidDataException(RetiredVLMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVK))
            throw new InvalidDataException(RetiredVKMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVJ))
            throw new InvalidDataException(RetiredVJMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVH))
            throw new InvalidDataException(RetiredVHMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVI))
            throw new InvalidDataException(RetiredVIMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVF))
            throw new InvalidDataException(RetiredVFMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVE))
            throw new InvalidDataException(RetiredVEMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVD))
            throw new InvalidDataException(RetiredVDMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVB))
            throw new InvalidDataException(RetiredVBMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVC))
            throw new InvalidDataException(RetiredVCMessage);
        if (magic.AsSpan().SequenceEqual(LegacyMagicVA))
            throw new InvalidDataException("CORTEXA checkpoint carries retired tree-policy and Homeostat learner state; start a new run instead of restoring a second decision authority");
        if (magic.AsSpan().SequenceEqual(LegacyMagicV9))
            throw new InvalidDataException("CORTEX9 checkpoint carries retired tree-policy state; start a new run instead of restoring a second decision authority");
        if (magic.AsSpan().SequenceEqual(LegacyMagicV8))
            throw new InvalidDataException("CORTEX8 checkpoint lacks typed anytime wall/resume baselines; start a new run instead of parsing a schema-skewed image");
        if (magic.AsSpan().SequenceEqual(LegacyMagicV7))
            throw new InvalidDataException("CORTEX7 checkpoint predates the typed anytime curve authority; start a new run instead of parsing a schema-skewed image");
        if (magic.AsSpan().SequenceEqual(LegacyMagic))
            throw new InvalidDataException("CORTEX6 checkpoint predates typed EML obligation closure packets; start a new run instead of parsing a schema-skewed image");
        if (magic.AsSpan().SequenceEqual(LegacyMagicV4))
            throw new InvalidDataException("CORTEX4 checkpoint predates the typed policy decision readout; start a new run instead of parsing a schema-skewed image");
        throw new InvalidDataException(magic.AsSpan().StartsWith("CGCTX"u8)
            ? $"retired Cortex checkpoint schema. This build reads only the current {CurrentDialect} schema; start a new run."
            : magic.AsSpan().StartsWith("CGCKPT"u8)
            ? "legacy trunk checkpoint dialect (CGCKPT). Cortex is a separate runtime dialect; keep the old run as data and start a new cortex run."
            : magic.AsSpan().StartsWith("CGTRIA"u8)
            ? "legacy mesh checkpoint dialect (CGTRIA). Cortex no longer resumes mesh images through the public tape surface; keep the old run as data."
            : magic.AsSpan().StartsWith("CGRING"u8)
            ? "routed mesh checkpoint dialect (CGRING). Cortex does not resume mesh images through the public tape surface; use the mesh resume path."
            : "not a cogito checkpoint (bad magic)");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  CONFIG — field-by-field (the record's shape IS the schema; adding a knob bumps the magic)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void WriteConfig(CkptWriter w, CortexRunConfig c)
    {
        w.Str(c.CorpusPath); w.Str(c.ExpectedWorldSHA256); w.I32(c.Steps); w.I32(c.BlockLen); w.I32(c.MaxBlockBytes); w.I32(c.Window);
        w.F64(c.Lambda); w.U64(c.Seed);
        w.I32(c.ReStrideBytes); w.I32(c.DomStrideSpans); w.I32(c.FrontierCapExps);
        w.I32(c.IntakeBatch); w.I32(c.SeedSpans); w.I32(c.MixEvery);
        w.Str(c.Curriculum); w.Str(c.Glob); w.F64(c.GrokCv); w.I32(c.LockRounds);
        w.Str(c.Energy); w.F64(c.AffFloor);
        w.I32(c.IntervalConsolidationPhase); w.I64(c.GrammarBudgetBits);
        w.Str(c.Simhash); w.Bool(c.NearDupe); w.Bool(c.Antiunify);
        w.F64(c.WallTol);
        w.I32(c.CheckpointEvery); w.I32(c.CurveEvery);
        w.I32(c.WScale); w.Bool(c.ConsolidationPhaseControl == CortexConsolidationPhaseControl.Homeostat); w.Str(c.SenseMask); w.Bool(c.Breach);
        w.F64(c.ReplayRatio);
        w.Bool(c.Loom); w.Bool(c.Shed);
        w.Bool(c.Rhythm);
        w.U8((byte)c.HomeoPolicy); w.U8((byte)c.HomeoAutonomy);
        w.U8((byte)c.PolicyDefaultMode);
        w.U8((byte)c.PolicyAuthorityCeiling);
        CortexPolicyOverride[] policyOverrides = c.PolicyOverrides ?? [];
        w.I32(policyOverrides.Length);
        for (int i = 0; i < policyOverrides.Length; i++)
        {
            w.Str(policyOverrides[i].Policy.Value);
            w.U8((byte)policyOverrides[i].Mode);
        }
        w.I32(c.PolicyShadowDecisions); w.I32(c.PolicyProposalInterval); w.I32(c.ReadoutDeliberationQuota);
        int[] policyHorizons = c.PolicyTrialHorizons ?? [16, 64, 256];
        w.I32(policyHorizons.Length);
        for (int i = 0; i < policyHorizons.Length; i++) w.I32(policyHorizons[i]);
        w.I64(c.PolicyTrialAllocationArmSteps); w.Str(c.PolicyTrialAllocationIdentity); w.U8((byte)c.PolicyTrialAllocationAuthority);
        w.F64(c.AffirmGate);
        w.Bool(c.CrossReflect);
        w.I32(c.EmlSignatureDigits);
        EmlKnobs eml = c.Eml.Equals(default) ? EmlKnobs.Mount : c.Eml;
        w.I32(eml.SeedK); w.I32(eml.MaxLen); w.I32(eml.MaxEnum); w.I32(eml.Units); w.I32(eml.Gain);
        w.F64(eml.Eps); w.F64(eml.EpsEnum); w.I32(eml.CorrobW); w.I32(eml.CertW);
        w.I32(eml.Lift.KMax); w.F64(eml.Lift.Factor); w.I32(eml.Lift.Window); w.I32(eml.Lift.Sustain);
        w.F64(eml.Lift.Frac); w.F64(eml.Lift.MeanzBand); w.Bool(eml.Lift.CensusOnly); w.Bool(eml.Lift.LockMeanz);
        w.F64(c.EmlHoldoutFraction); w.U64(c.EmlHoldoutSeed);
        w.OptionalStr(c.CurveReadout);
        w.I32(CortexConfigTokens.ResolveActionsPerStep(c));
        CortexStopCondition[] stops = c.StopConditions ?? [];
        w.I32(stops.Length);
        foreach (CortexStopCondition stop in stops) { w.Str(stop.Selector); w.F64(stop.AtLeast); }
        w.U8((byte)c.EmlTargetCatalog);
        w.U8((byte)c.EmlGrammarSampling);
        w.U8((byte)c.EmlProcessCatalog);
        w.U8((byte)c.EmlRung0);
        w.U8((byte)c.EmlDeliberation);
        WriteQuota(w, c.EmlDeliberationBudget);
        w.Str(c.DeepRematchGatePath);
        w.Str(c.DeepRematchGateDigest);
        w.Str(c.RunName);
        w.OptionalStr(c.EmlPairedFuelScheduleIdentity);
        WriteAdmissionPlan(w, c.AdmissionPlan);
    }

    private static CortexRunConfig ReadConfig(CkptReader r)
    {
        CortexRunConfig config = new(
            CorpusPath: r.Str(), ExpectedWorldSHA256: r.Str(), Steps: r.I32(), BlockLen: r.I32(), MaxBlockBytes: r.I32(), Window: r.I32(),
            Lambda: r.F64(), Seed: r.U64(),
            ReStrideBytes: r.I32(), DomStrideSpans: r.I32(), FrontierCapExps: r.I32(),
            IntakeBatch: r.I32(), SeedSpans: r.I32(), MixEvery: r.I32(),
            Curriculum: r.Str(), Glob: r.Str(), GrokCv: r.F64(), LockRounds: r.I32(),
            Energy: r.Str(), AffFloor: r.F64(),
            IntervalConsolidationPhase: r.I32(), GrammarBudgetBits: r.I64(),
            Simhash: r.Str(), NearDupe: r.Bool(), Antiunify: r.Bool(),
            WallTol: r.F64(),
            CheckpointEvery: r.I32(), CurveEvery: r.I32(),
            WScale: r.I32(), ConsolidationPhaseControl: r.Bool() ? CortexConsolidationPhaseControl.Homeostat : CortexConsolidationPhaseControl.Interval, SenseMask: r.Str(), Breach: r.Bool(),
            ReplayRatio: r.F64(),
            Loom: r.Bool(), Shed: r.Bool(),
            Rhythm: r.Bool(),
            HomeoPolicy: (HomeoPolicies)r.U8(), HomeoAutonomy: (HomeostatAutonomyModes)r.U8(),
            PolicyDefaultMode: (CortexPolicyModes)r.U8(),
            PolicyAuthorityCeiling: (CortexPolicyAuthorities)r.U8(),
            PolicyOverrides: ReadPolicyOverrides(r),
            PolicyShadowDecisions: r.I32(), PolicyProposalInterval: r.I32(), ReadoutDeliberationQuota: r.I32(),
            PolicyTrialHorizons: ReadPolicyHorizons(r),
            PolicyTrialAllocationArmSteps: r.I64(), PolicyTrialAllocationIdentity: r.Str(),
            PolicyTrialAllocationAuthority: (CortexPolicyAuthorities)r.U8(),
            AffirmGate: r.F64(),
            CrossReflect: r.Bool(),
            EmlSignatureDigits: r.I32(),
            Eml: new EmlKnobs(r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.F64(), r.F64(), r.I32(), r.I32(),
                new LiftKnobs(r.I32(), r.F64(), r.I32(), r.I32(), r.F64(), r.F64(), r.Bool(), r.Bool())),
            EmlHoldoutFraction: r.F64(),
            EmlHoldoutSeed: r.U64(),
            CurveReadout: r.OptionalStr(),
            ActionsPerStep: r.I32(),
            StopConditions: ReadStopConditions(r),
            EmlTargetCatalog: (EmlTargetCatalogs)r.U8(),
            EmlGrammarSampling: (EmlGrammarSamplingModes)r.U8(),
            EmlProcessCatalog: (EmlProcessCatalogs)r.U8(),
            EmlRung0: (EmlRung0Modes)r.U8(),
            EmlDeliberation: (EmlDeliberationModes)r.U8(),
            EmlDeliberationBudget: ReadQuota(r),
            DeepRematchGatePath: r.Str(),
            DeepRematchGateDigest: r.Str(),
            RunName: r.Str(),
            EmlPairedFuelScheduleIdentity: r.OptionalStr(),
            AdmissionPlan: ReadAdmissionPlan(r));
        ValidateConfigEnums(config);
        return config;
    }

    private static void ValidateConfigEnums(CortexRunConfig config)
    {
        FileCorpus.ValidateExpectedWorldSHA256(config.ExpectedWorldSHA256);
        RequireDefined(config.HomeoPolicy, nameof(config.HomeoPolicy));
        RequireDefined(config.HomeoAutonomy, nameof(config.HomeoAutonomy));
        RequireDefined(config.PolicyDefaultMode, nameof(config.PolicyDefaultMode));
        RequireDefined(config.PolicyAuthorityCeiling, nameof(config.PolicyAuthorityCeiling));
        for (int i = 0; i < config.PolicyOverrides!.Length; i++)
            RequireDefined(config.PolicyOverrides[i].Mode, $"{nameof(config.PolicyOverrides)}[{i}].{nameof(CortexPolicyOverride.Mode)}");
        RequireDefined(config.EmlTargetCatalog, nameof(config.EmlTargetCatalog));
        RequireDefined(config.EmlGrammarSampling, nameof(config.EmlGrammarSampling));
        RequireDefined(config.EmlProcessCatalog, nameof(config.EmlProcessCatalog));
        RequireDefined(config.EmlRung0, nameof(config.EmlRung0));
        RequireDefined(config.EmlDeliberation, nameof(config.EmlDeliberation));
        config.EmlDeliberationBudget.Validate();
        if (config.EmlPairedFuelScheduleIdentity is not null && config.EmlPairedFuelScheduleIdentity.Length > 0 && string.IsNullOrWhiteSpace(config.EmlPairedFuelScheduleIdentity))
            throw new InvalidDataException("paired fuel schedule identity cannot be whitespace");
        if (config.AdmissionPlan is not null)
            config.AdmissionPlan.Validate(config.AdmissionPlan.DomainSequence.Max() + 1);
    }

    private static void WriteAdmissionPlan(CkptWriter writer, AdmissionPlan? plan)
    {
        writer.Bool(plan is not null);
        if (plan is null) return;
        writer.Str(plan.ScheduleID);
        writer.Str(plan.WorldSHA256);
        writer.Str(plan.AuthorityDigest);
        writer.I32(plan.DomainSequence.Count);
        for (int i = 0; i < plan.DomainSequence.Count; i++) writer.I32(plan.DomainSequence[i]);
    }

    private static AdmissionPlan? ReadAdmissionPlan(CkptReader reader)
    {
        if (!reader.Bool()) return null;
        string scheduleID = reader.Str();
        string worldSHA256 = reader.Str();
        string authorityDigest = reader.Str();
        int count = reader.I32();
        if (count <= 0 || count > 10_000_000) throw new InvalidDataException("world encounter plan sequence is invalid");
        int[] sequence = new int[count];
        for (int i = 0; i < sequence.Length; i++) sequence[i] = reader.I32();
        AdmissionPlan plan = new(scheduleID, sequence, worldSHA256, authorityDigest);
        plan.Validate(sequence.Max() + 1);
        return plan;
    }

    private static void WriteQuota(CkptWriter writer, in EmlDeliberationQuota quota)
    {
        quota.Validate();
        writer.I64(quota.CandidateEvaluations); writer.I64(quota.LogicalProgramPoints);
        writer.I64(quota.ExecutedProgramPoints); writer.I64(quota.InverseTransforms);
        writer.I64(quota.HashProbes); writer.I64(quota.JoinAttempts); writer.I64(quota.JoinHits);
        writer.I64(quota.ProcessTerms); writer.I64(quota.VerifierProgramPoints);
        writer.I64(quota.CandidateSupplyItems); writer.I64(quota.LawRewriteApplications);
        writer.I64(quota.LawRewriteTreeNodes);
    }

    private static EmlDeliberationQuota ReadQuota(CkptReader reader)
        => new(reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64(),
            reader.I64(), reader.I64(), reader.I64(), reader.I64(), reader.I64());

    private static void RequireDefined<T>(T value, string field) where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
            throw new InvalidDataException($"invalid checkpoint config field {field}={(Convert.ToUInt64(value))}");
    }

    /// Focused wire proof for the installed revision identity carried into fork seed
    /// preparation. It exercises the concrete v3 failure shape (revision 90,
    /// fold predecessor 89), round-trips the tuple twice, accepts old
    /// snapshot-only images as explicit no-fold installed revisions, and rejects a
    /// tuple whose delta and fold disagree.
    internal static bool VerifyInstallRevisionTupleFixture(TextWriter output)
    {
        GrammarRevisionID previous = new(89);
        GrammarRevisionID revision = new(90);
        GrammarSnapshot snapshot = new(revision, [], [], Mbits.Zero, 256);
        LoopClosureCompositionEpisode episode = LoopClosureCompositionEpisode.Create(
            new LoopClosureCompositionEpisodeID("checkpoint-publication-fixture"),
            new TapeEventID(1), [new TapeEventID(2)], previous);
        GrammarFoldProvenanceReceipt fold = GrammarFoldProvenanceReceipt.Create(
            previous, revision, [new TapeEventID(1), new TapeEventID(2)], [episode]);
        GrammarDelta delta = new(previous, revision, [], [],
            [GrammarSequenceEdit.Replace(0, 0, snapshot.Compressed)], Mbits.Zero, GrammarResetKinds.None);
        InstallRevision installRevision = new(snapshot, delta, fold);

        byte[] first;
        using (MemoryStream stream = new())
        {
            using (CkptWriter writer = new(stream)) WriteInstallRevision(writer, in installRevision);
            first = stream.ToArray();
        }
        InstallRevision restored;
        using (MemoryStream stream = new(first, writable: false))
        using (CkptReader reader = new(stream)) restored = ReadInstallRevision(reader);
        byte[] second;
        using (MemoryStream stream = new())
        {
            using (CkptWriter writer = new(stream)) WriteInstallRevision(writer, in restored);
            second = stream.ToArray();
        }
        bool exact = first.AsSpan().SequenceEqual(second)
            && restored.Delta.ParentRevision == previous
            && restored.Revision == revision
            && restored.Delta.Reset == delta.Reset
            && restored.Delta.SequenceEdits.Length == 1
            && restored.Delta.SequenceEdits[0].Start == 0
            && restored.Delta.SequenceEdits[0].RemovedLength == 0
            && restored.FoldProvenance is { } restoredFold
            && restoredFold.PreviousRevision == restored.Delta.ParentRevision
            && restoredFold.Revision == restored.Revision
            && restoredFold.ReceiptDigest.Value == fold.ReceiptDigest.Value;

        byte[] oldSnapshotOnly;
        using (MemoryStream stream = new())
        {
            using (CkptWriter writer = new(stream)) WriteGrammarSnapshot(writer, snapshot);
            oldSnapshotOnly = stream.ToArray();
        }
        InstallRevision legacy;
        using (MemoryStream stream = new(oldSnapshotOnly, writable: false))
        using (CkptReader reader = new(stream)) legacy = ReadInstallRevision(reader);
        bool legacyExplicit = legacy.FoldProvenance is null
            && legacy.Delta.ParentRevision == revision
            && legacy.Delta.Revision == revision;

        bool mismatchRejected;
        try
        {
            GrammarDelta mismatch = new(new GrammarRevisionID(88), revision, [], [], [], Mbits.Zero, GrammarResetKinds.None);
            using MemoryStream stream = new();
            using (CkptWriter writer = new(stream))
            {
                WriteGrammarSnapshot(writer, snapshot);
                writer.Section(TagInstallRevisionTuple);
                WriteGrammarDelta(writer, in mismatch, snapshot);
                writer.Bool(true); WriteGrammarFold(writer, in fold);
            }
            stream.Position = 0;
            using CkptReader reader = new(stream);
            _ = ReadInstallRevision(reader);
            mismatchRejected = false;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            mismatchRejected = true;
        }

        bool passed = exact && legacyExplicit && mismatchRejected;
        output.WriteLine($"  grammar install revision tuple · rev={restored.Revision} parent={restored.Delta.ParentRevision} fold={(restored.FoldProvenance is null ? "none" : "exact")} save-load={(exact ? "exact" : "BROKEN")} legacy={(legacyExplicit ? "no-fold" : "AMBIGUOUS")} mismatch={(mismatchRejected ? "rejected" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    internal static bool VerifyDialectFixture(TextWriter output)
    {
        bool oldYRejected;
        try
        {
            using MemoryStream oldY = new(LegacyMagicVY.ToArray());
            using CkptReader reader = new(oldY);
            _ = ReadMagic(reader);
            oldYRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldYRejected = error.Message == RetiredVYMessage;
        }

        bool oldRRejected;
        try
        {
            using MemoryStream oldR = new(LegacyMagicVR.ToArray());
            using CkptReader reader = new(oldR);
            _ = ReadMagic(reader);
            oldRRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldRRejected = error.Message == RetiredVRMessage;
        }

        bool oldQRejected;
        try
        {
            using MemoryStream oldQ = new(LegacyMagicVQ.ToArray());
            using CkptReader reader = new(oldQ);
            _ = ReadMagic(reader);
            oldQRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldQRejected = error.Message == RetiredVQMessage;
        }
        bool oldKRejected;
        try
        {
            using MemoryStream oldK = new(LegacyMagicVK.ToArray());
            using CkptReader reader = new(oldK);
            _ = ReadMagic(reader);
            oldKRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldKRejected = error.Message == RetiredVKMessage;
        }
        bool oldDRejected;
        try
        {
            using MemoryStream oldD = new(LegacyMagicVD.ToArray());
            using CkptReader reader = new(oldD);
            _ = ReadMagic(reader);
            oldDRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldDRejected = error.Message == RetiredVDMessage;
        }
        bool oldERejected;
        try
        {
            using MemoryStream oldE = new(LegacyMagicVE.ToArray());
            using CkptReader reader = new(oldE);
            _ = ReadMagic(reader);
            oldERejected = false;
        }
        catch (InvalidDataException error)
        {
            oldERejected = error.Message == RetiredVEMessage;
        }
        bool oldFRejected;
        try
        {
            using MemoryStream oldF = new(LegacyMagicVF.ToArray());
            using CkptReader reader = new(oldF);
            _ = ReadMagic(reader);
            oldFRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldFRejected = error.Message == RetiredVFMessage;
        }
        bool oldHRejected;
        try
        {
            using MemoryStream oldH = new(LegacyMagicVH.ToArray());
            using CkptReader reader = new(oldH);
            _ = ReadMagic(reader);
            oldHRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldHRejected = error.Message == RetiredVHMessage;
        }
        bool oldIRejected;
        try
        {
            using MemoryStream oldI = new(LegacyMagicVI.ToArray());
            using CkptReader reader = new(oldI);
            _ = ReadMagic(reader);
            oldIRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldIRejected = error.Message == RetiredVIMessage;
        }
        bool oldJRejected;
        try
        {
            using MemoryStream oldJ = new(LegacyMagicVJ.ToArray());
            using CkptReader reader = new(oldJ);
            _ = ReadMagic(reader);
            oldJRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldJRejected = error.Message == RetiredVJMessage;
        }
        bool oldORejected;
        try
        {
            using MemoryStream oldO = new(LegacyMagicVO.ToArray());
            using CkptReader reader = new(oldO);
            _ = ReadMagic(reader);
            oldORejected = false;
        }
        catch (InvalidDataException error)
        {
            oldORejected = error.Message == RetiredVOMessage;
        }
        bool oldSRejected;
        try
        {
            using MemoryStream oldS = new(LegacyMagicVS.ToArray());
            using CkptReader reader = new(oldS);
            _ = ReadMagic(reader);
            oldSRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldSRejected = error.Message == RetiredVSMessage;
        }
        bool oldXRejected;
        try
        {
            using MemoryStream oldX = new(LegacyMagicVX.ToArray());
            using CkptReader reader = new(oldX);
            _ = ReadMagic(reader);
            oldXRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldXRejected = error.Message == RetiredVXMessage;
        }
        bool oldZRejected;
        try
        {
            using MemoryStream oldZ = new(LegacyMagicVZ.ToArray());
            using CkptReader reader = new(oldZ);
            _ = ReadMagic(reader);
            oldZRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldZRejected = error.Message == RetiredVZMessage;
        }
        bool currentDialect = CurrentDialect == "CORTEX0"
            && CurrentMagic.SequenceEqual("CORTEX0\n"u8)
            && MatchesCurrentSchema(CurrentMagic);
        StringWriter publicResumeMessage = new();
        bool publicOldORejected = global::Cogito.Cli.EmlTapeCommands.TryRejectRetiredCortexO("CORTEXO\n"u8, publicResumeMessage)
            && publicResumeMessage.ToString().Contains(RetiredVOMessage, StringComparison.Ordinal);
        bool publicOldDRejected = global::Cogito.Cli.EmlTapeCommands.TryRejectRetiredCortexD("CORTEXD\n"u8, publicResumeMessage)
            && publicResumeMessage.ToString().Contains(RetiredVDMessage, StringComparison.Ordinal);
        bool publicOldFRejected = global::Cogito.Cli.EmlTapeCommands.TryRejectRetiredCortexF("CORTEXF\n"u8, publicResumeMessage)
            && publicResumeMessage.ToString().Contains(RetiredVFMessage, StringComparison.Ordinal);
        bool publicOldHRejected = global::Cogito.Cli.EmlTapeCommands.TryRejectRetiredCortexH("CORTEXH\n"u8, publicResumeMessage)
            && publicResumeMessage.ToString().Contains(RetiredVHMessage, StringComparison.Ordinal);
        bool publicOldIRejected = global::Cogito.Cli.EmlTapeCommands.TryRejectRetiredCortexI("CORTEXI\n"u8, publicResumeMessage)
            && publicResumeMessage.ToString().Contains(RetiredVIMessage, StringComparison.Ordinal);
        bool publicOldJRejected = global::Cogito.Cli.EmlTapeCommands.TryRejectRetiredCortexJ("CORTEXJ\n"u8, publicResumeMessage)
            && publicResumeMessage.ToString().Contains(RetiredVJMessage, StringComparison.Ordinal);
        bool oldBRejected;
        try
        {
            using MemoryStream oldB = new(LegacyMagicVB.ToArray());
            using CkptReader reader = new(oldB);
            _ = ReadMagic(reader);
            oldBRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldBRejected = error.Message == RetiredVBMessage;
        }

        bool oldCRejected;
        try
        {
            using MemoryStream oldC = new(LegacyMagicVC.ToArray());
            using CkptReader reader = new(oldC);
            _ = ReadMagic(reader);
            oldCRejected = false;
        }
        catch (InvalidDataException error)
        {
            oldCRejected = error.Message == RetiredVCMessage;
        }

        CortexRunConfig config = new("", Curriculum: "eml", CurveReadout: "", RunName: "recovered-non-gate",
            DeepRematchGatePath: "", DeepRematchGateDigest: "digest", EmlDeliberationBudget: EmlDeliberationQuota.Default,
            EmlPairedFuelScheduleIdentity: "");
        byte[] saved = WriteConfigFixture(config);
        CortexRunConfig loaded = ReadConfigFixture(saved);
        byte[] resaved = WriteConfigFixture(loaded);
        bool saveLoadSaveExact = saved.AsSpan().SequenceEqual(resaved)
            && loaded.DeepRematchGatePath == config.DeepRematchGatePath
            && loaded.DeepRematchGateDigest == config.DeepRematchGateDigest
            && loaded.RunName == config.RunName
            && loaded.CurveReadout == config.CurveReadout
            && loaded.EmlPairedFuelScheduleIdentity == config.EmlPairedFuelScheduleIdentity;
        Cortex recovered = Cortex.CreateCheckpointRuntime(loaded);
        CortexRunConfig rebuilt = recovered.Config.ToRunConfig(null);
        string factoryDelta = Cortex.DescribePersistedConfigDelta(loaded, rebuilt);
        bool factoryRoundTripExact = Cortex.PersistedConfigDigest(loaded) == Cortex.PersistedConfigDigest(rebuilt)
            && rebuilt.RunName == config.RunName
            && rebuilt.CurveReadout == config.CurveReadout
            && rebuilt.EmlPairedFuelScheduleIdentity == config.EmlPairedFuelScheduleIdentity;
        CortexRunConfig freshGate = new CortexConfig { RunName = "gate-paired" }.ToRunConfig(null);
        bool freshDefaultsRemain = freshGate.EmlPairedFuelScheduleIdentity == "paired-gate-fuel-v1"
            && !string.IsNullOrEmpty(freshGate.CurveReadout);

        CortexRunConfig corruptConfig = config with { PolicyDefaultMode = (CortexPolicyModes)byte.MaxValue };
        bool corruptFieldRejected;
        try
        {
            _ = ReadConfigFixture(WriteConfigFixture(corruptConfig));
            corruptFieldRejected = false;
        }
        catch (InvalidDataException error)
        {
            corruptFieldRejected = error.Message.StartsWith("invalid checkpoint config field PolicyDefaultMode=", StringComparison.Ordinal);
        }

        bool installRevisionTuple = VerifyInstallRevisionTupleFixture(output);
        bool deltaDialect = CheckpointDelta.VerifyFixture(output);
        bool passed = oldBRejected && oldCRejected && oldDRejected && publicOldDRejected && oldERejected && oldFRejected && publicOldFRejected && oldHRejected && publicOldHRejected && oldIRejected && publicOldIRejected && oldJRejected && publicOldJRejected && oldKRejected && oldORejected && publicOldORejected && oldQRejected && oldRRejected && oldSRejected && oldXRejected && oldYRejected && oldZRejected && currentDialect && saveLoadSaveExact && factoryRoundTripExact && freshDefaultsRemain && corruptFieldRejected && installRevisionTuple && deltaDialect;
        output.WriteLine($"  checkpoint dialect · old-B={(oldBRejected ? "rejected" : "ACCEPTED")} old-C={(oldCRejected ? "rejected" : "ACCEPTED")} old-D={(oldDRejected && publicOldDRejected ? "rejected" : "ACCEPTED")} old-E={(oldERejected ? "rejected" : "ACCEPTED")} old-F={(oldFRejected ? "rejected" : "ACCEPTED")} old-H={(oldHRejected && publicOldHRejected ? "rejected" : "ACCEPTED")} old-I={(oldIRejected && publicOldIRejected ? "rejected" : "ACCEPTED")} old-J={(oldJRejected && publicOldJRejected ? "rejected" : "ACCEPTED")} old-K={(oldKRejected ? "rejected" : "ACCEPTED")} old-O={(oldORejected && publicOldORejected ? "rejected" : "ACCEPTED")} old-Q={(oldQRejected ? "rejected" : "ACCEPTED")} old-R={(oldRRejected ? "rejected" : "ACCEPTED")} old-S={(oldSRejected ? "rejected" : "ACCEPTED")} old-X={(oldXRejected ? "rejected" : "ACCEPTED")} old-Y={(oldYRejected ? "rejected" : "ACCEPTED")} old-Z={(oldZRejected ? "rejected" : "ACCEPTED")} current-{CurrentDialect[6]}={(currentDialect && saveLoadSaveExact && factoryRoundTripExact ? "exact" : "BROKEN")} save-load={(saveLoadSaveExact ? "exact" : "BROKEN")} factory={(factoryRoundTripExact ? "exact" : "BROKEN")} factory-delta={factoryDelta} fresh-defaults={(freshDefaultsRemain ? "exact" : "BROKEN")} corrupt-field={(corruptFieldRejected ? "rejected" : "ACCEPTED")} delta={(deltaDialect ? "exact" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static byte[] WriteConfigFixture(CortexRunConfig config)
    {
        using MemoryStream image = new();
        using (CkptWriter writer = new(image))
        {
            writer.Raw(CurrentMagic);
            writer.Section(TagConfig);
            WriteConfig(writer, config);
        }
        return image.ToArray();
    }

    internal static byte[] EncodeConfig(CortexRunConfig config)
        => WriteConfigFixture(config);

    private static CortexRunConfig ReadConfigFixture(byte[] image)
    {
        using MemoryStream stream = new(image);
        using CkptReader reader = new(stream);
        _ = ReadMagic(reader);
        reader.Expect(TagConfig);
        return ReadConfig(reader);
    }

    private static CortexPolicyOverride[] ReadPolicyOverrides(CkptReader reader)
    {
        int count = reader.I32();
        if (count < 0 || count > 1024) throw new InvalidDataException($"invalid policy override count {count}");
        CortexPolicyOverride[] overrides = new CortexPolicyOverride[count];
        for (int i = 0; i < count; i++)
            overrides[i] = new CortexPolicyOverride
            {
                Policy = CortexPolicyID.Parse(reader.Str()),
                Mode = (CortexPolicyModes)reader.U8(),
            };
        return overrides;
    }

    private static int[] ReadPolicyHorizons(CkptReader reader)
    {
        int count = reader.I32();
        if (count <= 0 || count > 1024) throw new InvalidDataException($"invalid policy horizon count {count}");
        int[] horizons = new int[count];
        for (int i = 0; i < count; i++) horizons[i] = reader.I32();
        return horizons;
    }

    private static CortexStopCondition[] ReadStopConditions(CkptReader r)
    {
        int count = r.I32();
        if (count < 0 || count > 1024) throw new InvalidDataException($"invalid stop-condition count {count}");
        CortexStopCondition[] conditions = new CortexStopCondition[count];
        for (int i = 0; i < count; i++) conditions[i] = new CortexStopCondition(r.Str(), r.F64());
        return conditions;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  GRAMMAR — the live RePairResult, serialized WHOLE. The drive's working grammar is NOT reproducible by
    //  re-induction: between sleeps it carries CONSOLIDATION state (Gc's TapeRef demotions, AntiUnify's SlotClass
    //  paradigm rules) layered over the Re-Pair output — so the checkpoint stores the artifact itself, rules with
    //  their body-kind + demoted span chains, compressed tape, savings, alphabet boundary. Shared by the drive
    //  grammar and the self-model's meta-grammars (token alphabets ride the same shape).
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    public static void WriteGrammar(CkptWriter w, in RePairResult g)
    {
        w.U32(g.AlphabetSize);
        w.I64(g.TotalSavings.Value);
        var rules = g.Rules ?? [];
        w.I32(rules.Length);
        foreach (var rule in rules)
        {
            w.Raw(rule.Id.Hash.AsSpan());
            w.I32(rule.Pattern.Length);
            foreach (var s in rule.Pattern) w.U32(s.Value);
            w.I64(rule.Cost.Value);
            w.U8((byte)rule.Kind);
            var segs = rule.Segs;
            w.I32(segs?.Length ?? -1);                        // -1 = no chain (Expansion/SlotClass); 0+ = the demoted chain
            if (segs is not null)
                foreach (var seg in segs) { w.I64(seg.Id.Value); w.I32(seg.Start); w.I32(seg.Len); }
        }
        var comp = g.Compressed ?? [];
        w.I32(comp.Length);
        foreach (var s in comp) w.U32(s.Value);
    }

    private static void WriteGrammarSnapshot(CkptWriter w, GrammarSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        w.U64(snapshot.Revision.Value);
        RePairResult grammar = snapshot.ToRePairResult();
        WriteGrammar(w, in grammar);
    }

    /// Persist the installed revision as one binding tuple. The nested tag keeps the
    /// pre-tuple snapshot-only wire readable: an old image explicitly becomes
    /// a no-fold installed revision instead of manufacturing a parent revision.
    private static void WriteInstallRevision(CkptWriter w, in InstallRevision installRevision)
    {
        WriteGrammarSnapshot(w, installRevision.Snapshot);
        w.Section(TagInstallRevisionTuple);
        GrammarDelta delta = installRevision.Delta;
        WriteGrammarDelta(w, in delta, installRevision.Snapshot);
        if (installRevision.FoldProvenance is { } fold)
        {
            w.Bool(true);
            WriteGrammarFold(w, in fold);
        }
        else w.Bool(false);
    }

    private static InstallRevision ReadInstallRevision(CkptReader r)
    {
        GrammarSnapshot snapshot = ReadGrammarSnapshot(r);
        if (!r.TryExpect(TagInstallRevisionTuple))
            return new InstallRevision(snapshot, GrammarDelta.CreateEmpty(snapshot.Revision));
        GrammarDelta delta = ReadGrammarDelta(r, snapshot);
        if (!r.Bool()) return new InstallRevision(snapshot, delta);
        return new InstallRevision(snapshot, delta, ReadGrammarFold(r));
    }

    private static void WriteGrammarDelta(CkptWriter w, in GrammarDelta delta, GrammarSnapshot snapshot)
    {
        w.U64(delta.ParentRevision.Value);
        w.U64(delta.Revision.Value);
        w.U8((byte)delta.Reset);
        bool addedIsSnapshot = ReferenceEquals(delta.AddedRules, snapshot.Rules);
        w.Bool(addedIsSnapshot);
        if (!addedIsSnapshot)
        {
            w.I32(delta.AddedRules.Length);
            foreach (GrammarRule rule in delta.AddedRules) WriteGrammarRule(w, in rule);
        }
        w.I32(delta.RemovedRules.Length);
        foreach (RuleID rule in delta.RemovedRules) w.Raw(rule.Hash.AsSpan());
        w.I32(delta.SequenceEdits.Length);
        foreach (GrammarSequenceEdit edit in delta.SequenceEdits)
        {
            w.I32(edit.Start); w.I32(edit.RemovedLength);
            bool insertedIsSnapshot = ReferenceEquals(edit.Inserted, snapshot.Compressed);
            w.Bool(insertedIsSnapshot);
            if (!insertedIsSnapshot)
            {
                w.I32(edit.Inserted.Length);
                foreach (Symbol symbol in edit.Inserted) w.U32(symbol.Value);
            }
        }
        w.I64(delta.MDLDelta.Value);
    }

    private static GrammarDelta ReadGrammarDelta(CkptReader r, GrammarSnapshot snapshot)
    {
        GrammarRevisionID parent = new(r.U64());
        GrammarRevisionID revision = new(r.U64());
        GrammarResetKinds reset = (GrammarResetKinds)r.U8();
        if (!Enum.IsDefined(reset)) throw new InvalidDataException("checkpoint carries an invalid grammar reset kind");
        GrammarRule[] added;
        if (r.Bool()) added = snapshot.Rules;
        else
        {
            int count = ReadBoundedCount(r, "grammar delta added rule");
            added = new GrammarRule[count];
            for (int i = 0; i < count; i++) added[i] = ReadGrammarRule(r);
        }
        int removedCount = ReadBoundedCount(r, "grammar delta removed rule");
        RuleID[] removed = new RuleID[removedCount];
        for (int i = 0; i < removedCount; i++) removed[i] = new RuleID(Hash256.From(r.RawExact(32)));
        int editCount = ReadBoundedCount(r, "grammar delta sequence edit");
        GrammarSequenceEdit[] edits = new GrammarSequenceEdit[editCount];
        for (int i = 0; i < editCount; i++)
        {
            int start = r.I32(); int removedLength = r.I32();
            Symbol[] inserted;
            if (r.Bool()) inserted = snapshot.Compressed;
            else
            {
                int count = ReadBoundedCount(r, "grammar delta inserted symbol");
                inserted = new Symbol[count];
                for (int j = 0; j < count; j++) inserted[j] = new Symbol(r.U32());
            }
            edits[i] = new GrammarSequenceEdit(start, removedLength, inserted);
        }
        return new GrammarDelta(parent, revision, added, removed, edits, new Mbits(r.I64()), reset);
    }

    private static void WriteGrammarRule(CkptWriter w, in GrammarRule rule)
    {
        w.Raw(rule.Id.Hash.AsSpan());
        w.I32(rule.Pattern.Length);
        foreach (Symbol symbol in rule.Pattern) w.U32(symbol.Value);
        w.I64(rule.Cost.Value); w.U8((byte)rule.Kind);
        w.I32(rule.Segs?.Length ?? -1);
        if (rule.Segs is not null)
            foreach (TapeEventSeg seg in rule.Segs) { w.I64(seg.Id.Value); w.I32(seg.Start); w.I32(seg.Len); }
    }

    private static GrammarRule ReadGrammarRule(CkptReader r)
    {
        RuleID id = new(Hash256.From(r.RawExact(32)));
        int patternCount = ReadBoundedCount(r, "grammar delta pattern symbol");
        Symbol[] pattern = new Symbol[patternCount];
        for (int i = 0; i < patternCount; i++) pattern[i] = new Symbol(r.U32());
        Mbits cost = new(r.I64());
        RuleBodyKind kind = (RuleBodyKind)r.U8();
        int segCount = r.I32();
        TapeEventSeg[]? segs = null;
        if (segCount >= 0)
        {
            if (segCount > 1_000_000) throw new InvalidDataException("grammar delta segment count exceeds bound");
            segs = new TapeEventSeg[segCount];
            for (int i = 0; i < segCount; i++) segs[i] = new TapeEventSeg(new TapeEventID(r.I64()), r.I32(), r.I32());
        }
        else if (segCount != -1) throw new InvalidDataException("grammar delta segment count is malformed");
        return new GrammarRule(id, pattern, cost, kind, segs);
    }

    private static void WriteGrammarFold(CkptWriter w, in GrammarFoldProvenanceReceipt fold)
    {
        fold.Validate();
        w.U64(fold.PreviousRevision.Value); w.U64(fold.Revision.Value);
        w.I32(fold.ConsumedEventIDs.Length);
        foreach (TapeEventID id in fold.ConsumedEventIDs) w.I64(id.Value);
        w.I32(fold.CompositionEpisodeDigests.Length);
        foreach (LoopClosureDigest digest in fold.CompositionEpisodeDigests) w.Str(digest.Value);
        w.Str(fold.ConsumedEventDigest.Value); w.Str(fold.ReceiptDigest.Value);
    }

    private static GrammarFoldProvenanceReceipt ReadGrammarFold(CkptReader r)
    {
        GrammarRevisionID previous = new(r.U64()); GrammarRevisionID revision = new(r.U64());
        int eventCount = ReadBoundedCount(r, "grammar fold consumed event");
        TapeEventID[] events = new TapeEventID[eventCount];
        for (int i = 0; i < eventCount; i++) events[i] = new TapeEventID(r.I64());
        int episodeCount = ReadBoundedCount(r, "grammar fold derivation episode");
        LoopClosureDigest[] episodes = new LoopClosureDigest[episodeCount];
        for (int i = 0; i < episodeCount; i++) episodes[i] = new LoopClosureDigest(r.Str());
        return new(previous, revision, events, episodes, new LoopClosureDigest(r.Str()), new LoopClosureDigest(r.Str()));
    }

    private static int ReadBoundedCount(CkptReader r, string role)
    {
        int count = r.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"checkpoint {role} count exceeds bound: {count}");
        return count;
    }

    private static GrammarSnapshot ReadGrammarSnapshot(CkptReader r)
    {
        GrammarRevisionID revision = new(r.U64());
        RePairResult grammar = ReadGrammar(r);
        return new GrammarSnapshot(revision, grammar.Rules, grammar.Compressed, grammar.TotalSavings, grammar.AlphabetSize);
    }

    public static RePairResult ReadGrammar(CkptReader r)
    {
        uint alphabet = r.U32();
        long savings = r.I64();
        int nRules = r.I32();
        var rules = new GrammarRule[nRules];
        for (int i = 0; i < nRules; i++)
        {
            var id = new RuleID(Hash256.From(r.Raw(32)));
            int plen = r.I32();
            var pattern = new Symbol[plen];
            for (int j = 0; j < plen; j++) pattern[j] = new Symbol(r.U32());
            var cost = new Mbits(r.I64());
            var kind = (RuleBodyKind)r.U8();
            int nSegs = r.I32();
            TapeEventSeg[]? segs = null;
            if (nSegs >= 0)
            {
                segs = new TapeEventSeg[nSegs];
                for (int j = 0; j < nSegs; j++) segs[j] = new TapeEventSeg(new TapeEventID(r.I64()), r.I32(), r.I32());
            }
            rules[i] = new GrammarRule(id, pattern, cost, kind, segs);
        }
        int nComp = r.I32();
        var comp = new Symbol[nComp];
        for (int i = 0; i < nComp; i++) comp[i] = new Symbol(r.U32());
        return new RePairResult(rules, comp, new Mbits(savings), alphabet);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  SHARED SMALL SHAPES — queues and sorted dictionaries (sorted writes are what make Save∘Load∘Save = id)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    public static void WriteQueue(CkptWriter w, Queue<double> q) { w.I32(q.Count); foreach (var v in q) w.F64(v); }
    public static void ReadQueue(CkptReader r, Queue<double> q)  { q.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) q.Enqueue(r.F64()); }
    public static void WriteQueue(CkptWriter w, Queue<int> q)    { w.I32(q.Count); foreach (var v in q) w.I32(v); }
    public static void ReadQueue(CkptReader r, Queue<int> q)     { q.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) q.Enqueue(r.I32()); }

}
