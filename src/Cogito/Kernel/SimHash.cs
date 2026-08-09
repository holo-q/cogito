namespace Cogito;

using System.Buffers.Binary;
using System.Numerics;
using Cogito.Codec;

// ── THE SIMHASH64 ORGAN (canon paper 05 · Similarity & Retrieval) ──  the working-set's LOCALITY organ. Re-Pair
// gives cogito the compressive lens (what recurs); SimHash gives it the RELATIONAL lens (what is NEAR) at O(1)
// per span instead of O(n²) pairwise. One 64-bit fingerprint per span, banded into 4 LSH buckets, Hamming as the
// integer distance — the whole apparatus is integer-only end-to-end (the Vow near consensus: no float touches a
// signature, a band, a witness). It mounts in three places the census named: (1) SLEEP's candidate generation —
// bucket the tape's spans so the precise φ-affinity scores only WITHIN buckets, not all n² pairs (the O(spans²)
// wall Seriate.LineAffinity is today); (2) MEMORY's GC — a Hamming-family query so near-dupe variants (the
// autoregressive mint is a variant-breeder) demote against a canonical exemplar (LOSSLESS — see the boundary on
// SimhashIndex.NearDupes); (3) the HUB INDEX — bucket-key → member TapeEventIDs, an Index event on the self-indexing
// tape + a second edge source for the navigability probe.
//
// ── HASH SUITE (the honest note — read before trusting a digest) ──  paper 05 pins H_probe / H_retrieval on
// BLAKE3; paper 04 pins the shingle hash on BLAKE3. The C# machine rides the ONE documented hash suite of Codec's
// `Hash.Domain` (SHA-256 today — the codec placeholder Codec.cs:51 names, "swap SHA-256 → BLAKE3 … is this one
// primitive"), so every provenance digest in the machine (H_blob, H_event, this organ's H_probe/H_retrieval) is in
// ONE family and swaps together on the global codec proof. Consequence, PROVEN (see `cogito simhash-vectors`): the
// paper's ABSOLUTE hash-dependent golden digits (5.2's b_1..b_3 probe bytes, 5.3's registry anchor, 5.4's witness
// digests, and 04's shingle digest) are NOT reproducible from the specs by ANY standard BLAKE3/SHA256 construction
// — the `formal-rewrites` reference impl that generated them used an undocumented framing and is absent from the
// tree. What IS bit-exact and consensus-critical reproduces perfectly: the il64 accumulator (Alg 5.1), TruncP, and
// every vector's INTERNAL relation (b_0=h>>48, b_k=b_0⊕TruncP(digest), trunc_64). This organ is spec-STRUCTURE-
// faithful; the vectors verb bit-checks the hash-independent core and structurally-checks the hash-dependent path
// under the actual suite (auto-matching the canon the day the global BLAKE3 swap lands).

/// A 64-bit SimHash fingerprint (S = {0,1}^64) — a span's LSH signature. Hamming distance IS the metric
/// (popcount of the XOR): integer, exact, portable, no float. The newtype makes a raw `ulong` that is a SIGNATURE
/// a compile-distinct thing from a raw `ulong` that is a bucket/count (Namespace Imagination — a signature is not a
/// number, it is a POSITION in the 64-cube).
public readonly record struct Sig(ulong Bits)
{
    /// d_h(this, o) = popcount(this ⊕ o) ∈ {0,…,64} (Def HAMMING DISTANCE). The whole similarity metric.
    public int Hamming(Sig o) => BitOperations.PopCount(Bits ^ o.Bits);
    public override string ToString() => $"0x{Bits:X16}";
}

/// The 4 LSH bucket keys of a signature (π : S → B^4, B = {0,…,2^16−1}). b_0 is the top-16-bits prefix; b_1..b_3
/// are domain-separated re-hashings XOR-folded onto b_0 (Alg 5.2) — 4 independent chances for a near-neighbour to
/// land in a shared bucket (Theorem 5.2 bucket coverage). A struct, not an array, so the shape (exactly four) is
/// in the type, and enumeration is alloc-free.
public readonly record struct Bands(ushort B0, ushort B1, ushort B2, ushort B3)
{
    /// The four keys in probe order — for the bucket-gather loop (query / index build).
    public ushort this[int i] => i switch { 0 => B0, 1 => B1, 2 => B2, _ => B3 };
    public const int Count = 4;
}

/// One retrieval hit — an event whose signature is within the Hamming ball of the query, with its provenance. The
/// witness records these: `Id` is the tape's hot-path
/// TapeEventID, `Hamming` the integer distance,
/// `Source` the event's provenance tag ("corpus" / "node0" — the mask/attention food Tape carries per event).
public readonly record struct NeighborHit(TapeEventID Id, int Hamming, string Source);

/// The spec's core functions as pure static verbs (Alg 5.1 SIMHASH64 · Alg 5.2 BUCKET_KEYS · TruncP · the shingle
/// feature extraction). Stateless; the stateful posting index is `SimhashIndex`. Every method integer-only.
public static class Simhash
{
    // ── shingling parameters ──
    public const int ShingleN = 5;     // token 5-gram window — "balances specificity vs. edit sensitivity"
    public const int PrefixBits = 16;  // P: b_0 = top-16-bits ⟹ 2^16 = 65536 buckets
    public const int Probes = 4;       // bounded work per query (b_0 + 3 domain-separated re-hashes)

    /// The consensus-critical contract (Alg 5.1): |S| > 2^63−1 overflows the i64 accumulators. A pure predicate —
    /// you cannot MATERIALIZE 2^63 shingles to test the guard, so the guard IS the testable contract (vector 5.5).
    public const long MaxShingles = long.MaxValue;   // 2^63 − 1 (Alg 5.1): the i64 accumulator holds |a[j]| ≤ |S|, so |S| must fit i64
    // A non-negative long is ≤ 2^63−1 and never overflows the accumulator; a count of 2^63 (the vector's overflow
    // case) is UNREPRESENTABLE as a non-negative long — it arrives wrapped-negative (2^63 == 1L<<63 == long.MinValue),
    // so the guard is n < 0. The pure predicate IS the testable contract (you cannot materialize 2^63 shingles).
    public static bool WouldOverflow(long shingleCount) => shingleCount < 0;

    /// ALGORITHM 5.1 — SIMHASH64 (Consensus-Critical). The il64 accumulator: 64 SIGNED counters, +1 per feature
    /// whose bit j is 1, −1 per feature whose bit j is 0; the signature bit j is set iff its counter ended STRICTLY
    /// positive (ties, counter = 0, collapse to 0 — the deterministic tie-break-to-zero). Empty set → all-zeros
    /// (Property 5.1's sentinel). This is the heart of the organ and is HASH-SUITE-INDEPENDENT — it consumes a
    /// shingle multiset (u64 features) and is bit-exact against vector 5.5 regardless of how the features were
    /// produced. Integer-only: no float, no rounding, no ambiguity.
    public static Sig SignOf(ReadOnlySpan<ulong> shingles)
    {
        if (shingles.Length == 0) return new Sig(0);                    // Property 5.1: simhash(∅) = 0x…0000
        Span<long> acc = stackalloc long[64];                           // i64 accumulators (the invariant: SIGNED 64-bit)
        acc.Clear();
        foreach (var s in shingles)
            for (int j = 0; j < 64; j++)
                acc[j] += ((s >> j) & 1UL) == 1UL ? 1 : -1;             // 2·bit − 1 ∈ {−1,+1}
        ulong h = 0;
        for (int j = 0; j < 64; j++)
            if (acc[j] > 0) h |= 1UL << j;                              // a[j] = 0 (tie) ⟹ bit stays 0
        return new Sig(h);
    }

    /// The span fingerprint the MOUNTS call — shingle the raw bytes (the FAST feature path), then il64-collapse.
    /// cogito's terminal alphabet IS bytes (paper 04's byte-fallback: every byte is a token), so a token 5-gram is
    /// a byte 5-gram. The mounts are the spec's Stage-2 (LOCAL, non-consensus reranking/clustering —
    /// Architecture), so they use a FAST integer shingle hash (FNV-1a/64 over the window) rather than the per-
    /// shingle crypto hash: same signature SPACE, same Hamming metric, O(bytes) not O(bytes × Hash.Domain). The
    /// CANONICAL (crypto-suite) shingle hash lives in `ShingleCanonical` for the witness/consensus path.
    public static Sig OfBytes(ReadOnlySpan<byte> text)
    {
        if (text.Length == 0) return new Sig(0);
        // Feed the il64 accumulator directly. The old shape materialized one
        // ulong per byte window before immediately consuming it, which made a
        // large Δ pay an allocation + second pass over every span. The vote is
        // unchanged; only the transient carrier disappears.
        Span<long> acc = stackalloc long[64];
        acc.Clear();
        if (text.Length < ShingleN)
        {
            Vote(acc, Fnv64(text));
        }
        else
        {
            for (int i = 0; i + ShingleN <= text.Length; i++)
                Vote(acc, Fnv64(text.Slice(i, ShingleN)));
        }
        return Signature(acc);
    }

    private static void Vote(Span<long> acc, ulong shingle)
    {
        for (int j = 0; j < 64; j++)
            acc[j] += ((shingle >> j) & 1UL) == 1UL ? 1 : -1;
    }

    private static Sig Signature(ReadOnlySpan<long> acc)
    {
        ulong h = 0;
        for (int j = 0; j < 64; j++)
            if (acc[j] > 0) h |= 1UL << j;
        return new Sig(h);
    }

    /// FAST shingle multiset (mounts): every byte 5-gram (or the whole span if shorter than 5) hashed FNV-1a/64.
    /// A MULTISET (all window positions, duplicates kept) — the frequency-aware vote paper 05 names ("Duplicate
    /// shingles vote proportionally via AllowDuplicates mode"). Deterministic + alloc-light (the Vow holds).
    public static void Shingle(ReadOnlySpan<byte> text, List<ulong> into)
    {
        into.Clear();
        if (text.Length == 0) return;
        int n = text.Length;
        if (n < ShingleN) { into.Add(Fnv64(text)); return; }            // grams(t): 1 ≤ |t| < 5 ⟹ the whole thing
        for (int i = 0; i + ShingleN <= n; i++) into.Add(Fnv64(text.Slice(i, ShingleN)));
    }

    /// The CANONICAL shingle hash — h(g) = trunc_64(H_suite("cogito/shingle/" ‖ encode(g)))
    /// where encode(g) = LE32(t_0)…LE32(t_4) for a 5-gram (byte tokens). Rides the machine's `Hash.Domain` suite;
    /// used by the witness/consensus path and exercised by the vectors verb (structural check — see the suite note
    /// atop this file). Multiset (duplicates kept), same as `Shingle`.
    public static void ShingleCanonical(ReadOnlySpan<byte> text, List<ulong> into)
    {
        into.Clear();
        if (text.Length == 0) return;
        int n = text.Length;
        Span<byte> enc = stackalloc byte[ShingleN * 4];                 // LE32 per byte-token
        if (n < ShingleN)
        {
            // grams(t) for |t|<5: encode = LE32(k) ‖ LE32(t_0)…  (the k-prefixed short-gram form)
            Span<byte> shortEnc = stackalloc byte[(n + 1) * 4];
            BinaryPrimitives.WriteUInt32LittleEndian(shortEnc, (uint)n);
            for (int t = 0; t < n; t++) BinaryPrimitives.WriteUInt32LittleEndian(shortEnc[((t + 1) * 4)..], text[t]);
            into.Add(TruncU64(Hash.Domain("cogito/shingle/"u8, shortEnc)));
            return;
        }
        for (int i = 0; i + ShingleN <= n; i++)
        {
            for (int t = 0; t < ShingleN; t++) BinaryPrimitives.WriteUInt32LittleEndian(enc[(t * 4)..], text[i + t]);
            into.Add(TruncU64(Hash.Domain("cogito/shingle/"u8, enc)));
        }
    }

    /// ALGORITHM 5.2 — BUCKET_KEYS. π(h) = (b_0,b_1,b_2,b_3): b_0 is the signature's top 16 bits; each b_k (k≥1) is
    /// b_0 XOR'd with a domain-separated re-hash of (h,k), giving 4 near-independent bucket assignments so a near-
    /// neighbour (small Hamming) is likely to collide in AT LEAST one band (Theorem 5.2). Integer-only; the hash is
    /// the machine suite (see the suite note atop this file).
    public static Bands BucketKeys(Sig sig)
    {
        ushort b0 = (ushort)(sig.Bits >> 48);                           // (h >> 48) & 0xFFFF — the ushort cast IS the mask
        return new Bands(b0, ProbeBand(sig.Bits, 1, b0), ProbeBand(sig.Bits, 2, b0), ProbeBand(sig.Bits, 3, b0));
    }

    // b_k = b_0 ⊕ TruncP(H_suite("cogito/simhash/probe/" ‖ LE64(h) ‖ LE32(k))).  The probe message is LE64(h)‖LE32(k).
    private static ushort ProbeBand(ulong h, uint k, ushort b0)
    {
        Span<byte> msg = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(msg, h);
        BinaryPrimitives.WriteUInt32LittleEndian(msg[8..], k);
        var d = Hash.Domain("cogito/simhash/probe/"u8, msg);
        return (ushort)(b0 ^ TruncP(d.AsSpan()));
    }

    /// TruncP (vector 5.1) — first 2 digest bytes, little-endian: d[0] | (d[1] << 8). Hash-independent, bit-exact.
    public static ushort TruncP(ReadOnlySpan<byte> digest) => (ushort)(digest[0] | (digest[1] << 8));

    /// trunc_64 (paper 04) — first 8 digest bytes as a little-endian u64. The shingle-hash truncation.
    public static ulong TruncU64(ReadOnlySpan<byte> digest) => BinaryPrimitives.ReadUInt64LittleEndian(digest);
    public static ulong TruncU64(in Hash256 d) => TruncU64(d.AsSpan());

    // FNV-1a/64 over a byte window — the fast shingle feature hash (mounts). Well-distributed for LSH, deterministic.
    // `internal`: GramPostings keys its grams with the SAME hash (one hash, no drift).
    internal static ulong Fnv64(ReadOnlySpan<byte> b)
    {
        ulong h = 14695981039346656037UL;
        foreach (var x in b) { h ^= x; h *= 1099511628211UL; }
        return h;
    }
}

/// THE GRAM-POSTINGS INDEX — memory's exact-substring CONTAINMENT shape: gram (a fixed-width byte window,
/// FNV-keyed) → the (id, offset) sites carrying it, in feed order (id asc, offset asc — deterministic, so the
/// kept prefix IS the canonical-exemplar set). Postings NARROW, bytes DECIDE: a consumer verifies byte-exact
/// against the span before acting on a hit (memory's lossless-sieve law). `cap` keeps only the FIRST N postings
/// per gram (memory needs one provable exemplar, not all sites; 0 = unbounded). Sibling shape: pool-scale
/// span-CANDIDACY (per-span, offset-free, frozen over a fixed pool) is PackedSpanPostings (Radula.cs) — the same
/// narrowing law packed flat, because that mass is pool-proportional and resident for the whole run, where this
/// one is tape-proportional and capped.
public sealed class GramPostings(int gramLen, int cap = 0)
{
    public readonly record struct GramPost(long Id, int Off);
    private readonly Dictionary<ulong, List<GramPost>> _posts = new();

    public int GramLen => gramLen;
    public int Cap => cap;

    internal void Clear() => _posts.Clear();

    /// Post every width-`GramLen` gram of `span` under `id`. Feed in id-ascending order (the determinism +
    /// canonical-prefix contract); spans shorter than the gram width post nothing.
    public void Add(long id, ReadOnlySpan<byte> span)
    {
        for (int off = 0; off + gramLen <= span.Length; off++)
        {
            ulong key = Simhash.Fnv64(span.Slice(off, gramLen));
            var posts = _posts.TryGetValue(key, out var l) ? l : _posts[key] = new();
            if (cap > 0 && posts.Count >= cap) continue;
            posts.Add(new GramPost(id, off));
        }
    }

    /// The posting list for a gram key (null = the gram occurs nowhere — for the frontier this PROVES no
    /// indexed span contains any superstring of it).
    public List<GramPost>? Posts(ulong key) => _posts.TryGetValue(key, out var l) ? l : null;

    /// MemStat census read — distinct gram keys + Σ postings (the index's two growth axes). Counts only.
    public (long Keys, long Posts) Mass()
    {
        long posts = 0;
        foreach (var l in _posts.Values) posts += l.Count;
        return (_posts.Count, posts);
    }

    // The gram map is canonical checkpoint state.  Replaying tape bytes to
    // reconstruct it is both needless work and observably expensive on large
    // resumes; write the already-canonical key/posting order directly.
    internal void Save(CkptWriter w)
    {
        w.U8(1); w.I32(gramLen); w.I32(cap); w.I32(_posts.Count);
        foreach (var key in _posts.Keys.Order())
        {
            var posts = _posts[key];
            w.U64(key); w.I32(posts.Count);
            foreach (var post in posts) { w.I64(post.Id); w.I32(post.Off); }
        }
    }

    internal void Load(CkptReader r, int expectedGramLen, int expectedCap, long maxIDExclusive = long.MaxValue)
    {
        if (r.U8() != 1) throw new InvalidDataException("unsupported gram-postings checkpoint version");
        int storedLen = r.I32(), storedCap = r.I32();
        if (storedLen != expectedGramLen || storedCap != expectedCap)
            throw new InvalidDataException("gram-postings checkpoint configuration mismatch");
        int count = r.I32();
        if (count < 0 || count > 100_000_000) throw new InvalidDataException("gram-postings key count is invalid");
        _posts.Clear();
        ulong previous = 0;
        for (int i = 0; i < count; i++)
        {
            ulong key = r.U64();
            if (i != 0 && key <= previous) throw new InvalidDataException("gram-postings keys are not canonical");
            previous = key;
            int n = r.I32();
            if (n < 0 || n > 100_000_000) throw new InvalidDataException("gram-postings posting count is invalid");
            var posts = new List<GramPost>(n);
            long priorId = -1; int priorOff = -1;
            for (int j = 0; j < n; j++)
            {
                long id = r.I64(); int off = r.I32();
                if (id < 0 || id >= maxIDExclusive || off < 0 || (j != 0 && (id < priorId || id == priorId && off <= priorOff)))
                    throw new InvalidDataException("gram-postings entries are not canonical");
                posts.Add(new GramPost(id, off)); priorId = id; priorOff = off;
            }
            _posts.Add(key, posts);
        }
    }
}

/// Caller-owned state for one exact Hamming-neighbour query.  The candidate index keeps no per-query sets or
/// result lists: callers either reuse this object (the hot path) or provide a fresh one at a deliberate boundary.
public sealed class HammingQueryScratch
{
    internal HammingCandidate[] Heap = Array.Empty<HammingCandidate>();
    internal int HeapCount;
    internal int Limit;

    internal void Reset(int limit)
    {
        Limit = limit;
        HeapCount = 0;
        if (Heap.Length < limit) Array.Resize(ref Heap, Math.Max(limit, Heap.Length == 0 ? 8 : Heap.Length * 2));
    }

    internal int WorstDistance => HeapCount < Limit ? 64 : Heap[0].Hamming;
}

internal readonly record struct HammingCandidate(int Hamming, int Slot);

internal readonly record struct SimhashFeedReceipt(
    long LsmMerges,
    long AdjSelfChecks,
    long AdjBackfillVisits,
    long AdjBackfillAdds);

/// One immutable, contiguous-ID run in the Hamming LSM forest.  Runs are power-of-two sized and never mutate after
/// construction.  A deterministic VP tree indexes unique signatures; duplicate signatures retain their ascending
/// slot lists so exact ties resolve to the earliest ID.
public sealed class HammingRun
{
    private readonly ulong[] _groupBits;
    private readonly ulong[] _slotBits;
    private readonly int[] _groupStarts;
    private readonly int[] _groupLengths;
    private readonly int[] _groupSlots;
    private readonly VpNode[] _vp;

    private readonly record struct VpPoint(int Group, int Distance);
    private readonly record struct VpNode(int Group, int Radius, int Left, int Right);

    public int StartSlot { get; }
    public int Count { get; }
    public int Level => BitOperations.TrailingZeroCount((uint)Count);
    public int UniqueCount => _groupBits.Length;

    internal HammingRun(int startSlot, ReadOnlySpan<Sig> signatures)
    {
        if (signatures.Length == 0 || (signatures.Length & (signatures.Length - 1)) != 0)
            throw new ArgumentException("Hamming runs must be non-empty powers of two.", nameof(signatures));
        StartSlot = startSlot;
        Count = signatures.Length;
        var sourceSignatures = signatures.ToArray();
        _slotBits = new ulong[sourceSignatures.Length];
        for (int i = 0; i < sourceSignatures.Length; i++) _slotBits[i] = sourceSignatures[i].Bits;

        // Sort (bits, slot) explicitly. Array.Sort is not stable, and duplicate ordering is part of the oracle.
        var order = new int[sourceSignatures.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = sourceSignatures[a].Bits.CompareTo(sourceSignatures[b].Bits);
            return c != 0 ? c : a.CompareTo(b);
        });

        int unique = 0;
        for (int i = 0; i < order.Length; i++)
            if (i == 0 || sourceSignatures[order[i]].Bits != sourceSignatures[order[i - 1]].Bits) unique++;
        _groupBits = new ulong[unique];
        _groupStarts = new int[unique];
        _groupLengths = new int[unique];
        _groupSlots = new int[signatures.Length];
        int g = -1;
        for (int i = 0; i < order.Length; i++)
        {
            int source = order[i];
            ulong bits = sourceSignatures[source].Bits;
            if (i == 0 || bits != _groupBits[g])
            {
                g++;
                _groupBits[g] = bits;
                _groupStarts[g] = i;
            }
            _groupSlots[i] = StartSlot + source;
            _groupLengths[g]++;
        }

        var groups = new int[unique];
        for (int i = 0; i < unique; i++) groups[i] = i;
        var nodes = new List<VpNode>(unique);
        BuildVp(groups, nodes);
        _vp = nodes.ToArray();
    }

    internal void CopySignaturesTo(Span<Sig> destination)
    {
        if (destination.Length < Count) throw new ArgumentException("destination is shorter than the run", nameof(destination));
        for (int g = 0; g < _groupBits.Length; g++)
            for (int i = 0; i < _groupLengths[g]; i++)
                destination[_groupSlots[_groupStarts[g] + i] - StartSlot] = new Sig(_groupBits[g]);
    }

    private int BuildVp(int[] groups, List<VpNode> nodes)
    {
        if (groups.Length == 0) return -1;
        int nodeIndex = nodes.Count;
        nodes.Add(default); // reserve preorder slot; children point to stable indexes
        int vantage = groups[0];
        if (groups.Length == 1)
        {
            nodes[nodeIndex] = new VpNode(vantage, 0, -1, -1);
            return nodeIndex;
        }

        var rest = new VpPoint[groups.Length - 1];
        ulong vb = _groupBits[vantage];
        for (int i = 1; i < groups.Length; i++)
        {
            int group = groups[i];
            rest[i - 1] = new VpPoint(group, BitOperations.PopCount(vb ^ _groupBits[group]));
        }
        Array.Sort(rest, static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Group.CompareTo(b.Group));
        int leftCount = rest.Length / 2;
        int radius = leftCount == 0 ? 0 : rest[leftCount - 1].Distance;
        var left = new int[leftCount];
        var right = new int[rest.Length - leftCount];
        for (int i = 0; i < left.Length; i++) left[i] = rest[i].Group;
        for (int i = 0; i < right.Length; i++) right[i] = rest[leftCount + i].Group;
        int leftNode = BuildVp(left, nodes);
        int rightNode = BuildVp(right, nodes);
        nodes[nodeIndex] = new VpNode(vantage, radius, leftNode, rightNode);
        return nodeIndex;
    }

    internal void Search(Sig query, int maxSlotExclusive, HammingQueryScratch scratch)
    {
        if (_vp.Length != 0) SearchNode(0, query.Bits, maxSlotExclusive, scratch);
    }

    private void SearchNode(int nodeIndex, ulong query, int maxSlotExclusive, HammingQueryScratch scratch)
    {
        if (nodeIndex < 0) return;
        var node = _vp[nodeIndex];
        int distance = BitOperations.PopCount(query ^ _groupBits[node.Group]);
        int start = _groupStarts[node.Group];
        int end = start + _groupLengths[node.Group];
        for (int i = start; i < end; i++)
        {
            int slot = _groupSlots[i];
            if (slot < maxSlotExclusive)
                AddCandidate(new HammingCandidate(BitOperations.PopCount(query ^ _slotBits[slot - StartSlot]), slot), scratch);
        }

        int radius = node.Radius;
        int threshold = scratch.WorstDistance;
        if (distance - threshold <= radius) SearchNode(node.Left, query, maxSlotExclusive, scratch);
        if (distance + threshold >= radius) SearchNode(node.Right, query, maxSlotExclusive, scratch);
    }

    private static bool IsWorse(HammingCandidate a, HammingCandidate b)
        => a.Hamming != b.Hamming ? a.Hamming > b.Hamming : a.Slot > b.Slot;

    private static bool IsBetter(HammingCandidate a, HammingCandidate b)
        => a.Hamming != b.Hamming ? a.Hamming < b.Hamming : a.Slot < b.Slot;

    private static void AddCandidate(HammingCandidate candidate, HammingQueryScratch scratch)
    {
        if (scratch.Limit == 0) return;
        if (scratch.HeapCount < scratch.Limit)
        {
            int i = scratch.HeapCount++;
            scratch.Heap[i] = candidate;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!IsWorse(scratch.Heap[i], scratch.Heap[parent])) break;
                (scratch.Heap[i], scratch.Heap[parent]) = (scratch.Heap[parent], scratch.Heap[i]);
                i = parent;
            }
            return;
        }
        if (!IsBetter(candidate, scratch.Heap[0])) return;
        scratch.Heap[0] = candidate;
        int p = 0;
        while (true)
        {
            int left = p * 2 + 1;
            if (left >= scratch.HeapCount) break;
            int right = left + 1;
            int worst = right < scratch.HeapCount && IsWorse(scratch.Heap[right], scratch.Heap[left]) ? right : left;
            if (!IsWorse(scratch.Heap[worst], scratch.Heap[p])) break;
            (scratch.Heap[p], scratch.Heap[worst]) = (scratch.Heap[worst], scratch.Heap[p]);
            p = worst;
        }
    }

    internal static HammingRun Merge(HammingRun left, HammingRun right)
    {
        if (left.StartSlot + left.Count != right.StartSlot) throw new ArgumentException("runs must be adjacent");
        var signatures = new Sig[left.Count + right.Count];
        left.CopySignaturesTo(signatures);
        right.CopySignaturesTo(signatures.AsSpan(left.Count));
        return new HammingRun(left.StartSlot, signatures);
    }
}

/// Exact append-only Hamming candidate index.  Appends land in immutable power-of-two runs; equal-level runs merge
/// like an LSM forest. Queries search each run's deterministic VP tree over unique signatures and return the exact
/// nearest prior slots ordered by (Hamming distance, slot). No approximate/bounded production mode is hidden here.
public sealed class HammingCandidateIndex
{
    private readonly List<Sig> _signatures = new();
    private readonly List<HammingRun?> _levels = new();

    public int Count => _signatures.Count;
    internal long MergeCount { get; private set; }
    public IReadOnlyList<Sig> Signatures => _signatures;
    public IReadOnlyList<HammingRun?> Runs => _levels;

    internal void Clear() { _signatures.Clear(); _levels.Clear(); MergeCount = 0; }

    internal void ResetMergeReceipt() => MergeCount = 0;

    internal long ConsumeMergeReceipt()
    {
        long count = MergeCount;
        MergeCount = 0;
        return count;
    }

    public int Add(Sig signature)
    {
        int slot = _signatures.Count;
        _signatures.Add(signature);
        var carry = new HammingRun(slot, stackalloc[] { signature });
        int level = 0;
        while (true)
        {
            if (level == _levels.Count) _levels.Add(carry);
            else if (_levels[level] is null) _levels[level] = carry;
            else
            {
                var lower = _levels[level]!;
                _levels[level] = null;
                MergeCount++;
                carry = HammingRun.Merge(lower, carry);
                level++;
                continue;
            }
            return slot;
        }
    }

    public int FindPriorNearest(int slot, int topK, List<int> into, HammingQueryScratch scratch)
    {
        into.Clear();
        if (topK <= 0 || slot <= 0 || slot >= Count) return 0;
        scratch.Reset(Math.Min(topK, slot));
        for (int level = 0; level < _levels.Count; level++)
        {
            var run = _levels[level];
            if (run is null || run.StartSlot >= slot) continue;
            run.Search(_signatures[slot], slot, scratch);
        }
        Array.Sort(scratch.Heap, 0, scratch.HeapCount, HammingCandidateComparer.Instance);
        for (int i = 0; i < scratch.HeapCount; i++) into.Add(scratch.Heap[i].Slot);
        return scratch.HeapCount;
    }

    /// Independent O(n) oracle for the exactness gate. It intentionally does not consult the LSM/VP structure.
    public int FindPriorNearestBruteForce(int slot, int topK, List<int> into)
    {
        into.Clear();
        if (topK <= 0 || slot <= 0 || slot >= Count) return 0;
        for (int i = 0; i < slot; i++) into.Add(i);
        into.Sort((a, b) =>
        {
            int da = _signatures[slot].Hamming(_signatures[a]);
            int db = _signatures[slot].Hamming(_signatures[b]);
            return da != db ? da.CompareTo(db) : a.CompareTo(b);
        });
        if (into.Count > topK) into.RemoveRange(topK, into.Count - topK);
        return into.Count;
    }

    private sealed class HammingCandidateComparer : IComparer<HammingCandidate>
    {
        public static readonly HammingCandidateComparer Instance = new();
        public int Compare(HammingCandidate a, HammingCandidate b)
            => a.Hamming != b.Hamming ? a.Hamming.CompareTo(b.Hamming) : a.Slot.CompareTo(b.Slot);
    }
}

/// The stateful posting index — bucket-key → member TapeEventIDs (the HUB INDEX, Mount 3) + the query engine (Mount's
/// candidate generation) + the near-dupe family query (Mount 2's GC). Built over a set of (TapeEventID, bytes, source)
/// once per consolidation; the mounts read it. One signature per span, cached; the band postings are the inverted
/// index the "5-clicks" navigability probe walks (bucket-hops as edges).
public sealed class SimhashIndex
{
    private readonly List<TapeEventID> _ids = new();
    private readonly List<Sig> _sigs = new();
    private readonly List<string> _sources = new();
    private readonly HammingCandidateIndex _hamming = new(); // exact prior-neighbour substrate; raw postings remain the hub/GC surface
    private readonly HammingQueryScratch _hammingScratch = new();
    private readonly Dictionary<int, int> _slotOf = new();              // TapeEventID.Value (int-keyed) → dense slot, for provenance lookups
    private readonly Dictionary<ushort, List<int>> _postings = new();   // band key → member slots (the inverted hub index, all 4 bands merged)

    // ── MOUNT 3 · THE STANDING BUCKET ADJACENCY ──  BucketGraph() rebuilt the whole
    // per-slot adjacency EVERY night — O(slots × hub-prefix) with degenerate mega-buckets (the variant-breeder's
    // 20k+-member hubs), the measured minutes-scale organ of the 456s night. The graph is maintained HERE instead,
    // O(1) amortized per Add: a slot's adjacency is its AdjCap LOWEST bucket co-members (ascending), which is
    // APPEND-STABLE — later members only carry HIGHER slots, so the lowest-AdjCap set changes only while a slot is
    // still hungry (<AdjCap entries). SELF-FILL at Add gathers the lowest existing co-members (ascending 4-way
    // merge over the band lists' heads); BACK-FILL completes earlier still-hungry members (each leaves its ≤4
    // hungry lists exactly once — amortized O(1)). At any read point the structure EQUALS BucketGraph(AdjCap):
    // the lowest-AdjCap of a bucket union live in each ascending list's first AdjCap entries ⊂ the BandScanCap
    // prefix, so the eager walk and the standing graph see the same neighbours. Deterministic (feed order = id
    // order); NOT checkpointed — Load re-feeds by id and re-derives it bit-identically (Save∘Load ≡ incremental).
    private const int AdjCap = 12;                                      // = BucketGraph's capPerNode — ONE cap, both readers
    private readonly List<int[]> _adj = new();                          // per-slot int[AdjCap], ascending prefix of _adjN[slot]
    private readonly List<int> _adjN = new();
    private readonly Dictionary<ushort, List<int>> _hungry = new();     // bucket → members still below AdjCap (lazily compacted)
    private readonly List<int>?[] _mergeLists = new List<int>?[Bands.Count];   // self-fill scratch (single-threaded)
    private readonly int[] _mergePtr = new int[Bands.Count];
    private long _adjSelfChecks;
    private long _adjBackfillVisits;
    private long _adjBackfillAdds;

    public int Count => _ids.Count;
    public IReadOnlyList<Sig> Signatures => _sigs;
    public IReadOnlyList<TapeEventID> Ids => _ids;
    public HammingCandidateIndex CandidateIndex => _hamming;
    public int BucketCount => _postings.Count;
    public string SourceAt(int slot) => _sources[slot];
    /// Mean posting-list occupancy — the load L (Theorem 5.4's N/2^P) as a sparkline (how crowded the buckets are).
    public double MeanOccupancy => _postings.Count == 0 ? 0 : (double)_postings.Values.Sum(p => p.Count) / _postings.Count;

    internal void ResetFeedReceipt()
    {
        _adjSelfChecks = 0;
        _adjBackfillVisits = 0;
        _adjBackfillAdds = 0;
        _hamming.ResetMergeReceipt();
    }

    internal SimhashFeedReceipt ConsumeFeedReceipt()
    {
        var receipt = new SimhashFeedReceipt(_hamming.ConsumeMergeReceipt(), _adjSelfChecks, _adjBackfillVisits, _adjBackfillAdds);
        ResetFeedReceipt();
        return receipt;
    }

    /// Ingest one span: fingerprint (fast path), band, and post into all 4 bucket lists (Alg 5.5 upsert shape —
    /// a span joins every one of its 4 buckets), then maintain the standing bucket adjacency (self-fill +
    /// back-fill — see the field note). Returns the signature (so a caller can cache it).
    public Sig Add(TapeEventID id, ReadOnlySpan<byte> bytes, string source)
        => AddCore(id, Simhash.OfBytes(bytes), source);

    private Sig AddCore(TapeEventID id, Sig sig, string source)
    {
        int slot = _ids.Count;
        _ids.Add(id); _sigs.Add(sig); _sources.Add(source);
        _hamming.Add(sig);
        _slotOf[(int)id.Value] = slot;
        var bands = Simhash.BucketKeys(sig);
        for (int k = 0; k < Bands.Count; k++)
        {
            ushort key = bands[k];
            (_postings.TryGetValue(key, out var l) ? l : _postings[key] = new()).Add(slot);
        }

        // ── the standing adjacency ──  distinct band keys only (duplicate keys post twice above — the postings'
        // historical shape — but count once as a neighbourhood).
        Span<ushort> dk = stackalloc ushort[Bands.Count];
        int nk = 0;
        for (int k = 0; k < Bands.Count; k++)
        {
            ushort key = bands[k];
            bool dup = false;
            for (int j = 0; j < nk; j++) if (dk[j] == key) { dup = true; break; }
            if (!dup) dk[nk++] = key;
        }

        // SELF-FILL — the AdjCap lowest existing co-members across the distinct band lists (ascending nk-way
        // merge; lists are slot-ascending by construction, self sits at each tail and is skipped).
        var adj = new int[AdjCap];
        int an = 0;
        for (int k = 0; k < nk; k++) { _mergeLists[k] = _postings[dk[k]]; _mergePtr[k] = 0; }
        while (an < AdjCap)
        {
            int best = int.MaxValue;
            for (int k = 0; k < nk; k++)
            {
                _adjSelfChecks++;
                var l = _mergeLists[k]!;
                int p = _mergePtr[k];
                while (p < l.Count && l[p] == slot) p++;             // self is never a neighbour
                _mergePtr[k] = p;
                if (p < l.Count && l[p] < best) best = l[p];
            }
            if (best == int.MaxValue) break;
            adj[an++] = best;
            for (int k = 0; k < nk; k++)                             // advance every list past the emitted value (cross-list dedup)
            {
                _adjSelfChecks++;
                var l = _mergeLists[k]!;
                int p = _mergePtr[k];
                while (p < l.Count && l[p] == best) p++;
                _mergePtr[k] = p;
            }
        }
        _adj.Add(adj); _adjN.Add(an);
        for (int k = 0; k < nk; k++) _mergeLists[k] = null;

        // BACK-FILL — this slot completes earlier members still hungry in its buckets (arrival order = ascending,
        // so appends preserve each member's ascending adjacency); full members lazily swap-remove out of the
        // hungry lists (each membership leaves exactly once — amortized O(1) per Add).
        for (int k = 0; k < nk; k++)
        {
            if (_hungry.TryGetValue(dk[k], out var hu))
            {
                for (int i = 0; i < hu.Count; )
                {
                    _adjBackfillVisits++;
                    int m = hu[i];
                    int mn = _adjN[m];
                    if (mn >= AdjCap) { hu[i] = hu[^1]; hu.RemoveAt(hu.Count - 1); continue; }
                    var ma = _adj[m];
                    bool has = false;                                // m can sit in two of this slot's buckets — append once
                    for (int t = 0; t < mn; t++) if (ma[t] == slot) { has = true; break; }
                    if (!has)
                    {
                        ma[mn] = slot;
                        _adjN[m] = mn + 1;
                        _adjBackfillAdds++;
                        if (mn + 1 >= AdjCap) { hu[i] = hu[^1]; hu.RemoveAt(hu.Count - 1); continue; }
                    }
                    i++;
                }
                if (hu.Count == 0) _hungry.Remove(dk[k]);
            }
            if (an < AdjCap)
                (_hungry.TryGetValue(dk[k], out var mine) ? mine : _hungry[dk[k]] = new()).Add(slot);
        }
        return sig;
    }

    internal void Clear()
    {
        _ids.Clear(); _sigs.Clear(); _sources.Clear(); _hamming.Clear(); _slotOf.Clear(); _postings.Clear();
        _adj.Clear(); _adjN.Clear(); _hungry.Clear();
        _adjSelfChecks = _adjBackfillVisits = _adjBackfillAdds = 0;
    }

    // Restore the complete canonical index without touching tape bytes.  AddCore
    // rebuilds only the compact structural maps from the persisted signatures;
    // the expensive SimHash byte pass is deliberately absent from this path.
    internal void Save(CkptWriter w)
    {
        w.U8(1); w.I32(_ids.Count);
        for (int i = 0; i < _ids.Count; i++) { w.I64(_ids[i].Value); w.U64(_sigs[i].Bits); w.Str(_sources[i]); }
    }

    internal void Load(CkptReader r)
    {
        if (r.U8() != 1) throw new InvalidDataException("unsupported simhash-index checkpoint version");
        int count = r.I32();
        if (count < 0 || count > 100_000_000) throw new InvalidDataException("simhash-index count is invalid");
        Clear();
        long previous = -1;
        for (int i = 0; i < count; i++)
        {
            long id = r.I64(); ulong bits = r.U64(); string source = r.Str();
            if (id < 0 || (i != 0 && id <= previous)) throw new InvalidDataException("simhash-index IDs are not canonical");
            previous = id;
            AddCore(new TapeEventID(id), new Sig(bits), source);
        }
    }

    /// The standing bucket adjacency of one slot — ascending, ≡ BucketGraph(AdjCap)'s row at any read point,
    /// maintained incrementally (never rebuilt). The navigability probe walks THIS, O(Δ) per night.
    public ReadOnlySpan<int> AdjOf(int slot) => _adj[slot].AsSpan(0, _adjN[slot]);

    /// Build the index over the whole tape (the consolidation entry — one signature per span). Clears first.
    public static SimhashIndex OfTape(Tape tape)
    {
        var idx = new SimhashIndex();
        var ids = tape.ResidentEventIDs; var spans = tape.ResidentEventBytes; var src = tape.ResidentEventSources;
        for (int k = 0; k < spans.Count; k++) idx.Add(ids[k], spans[k], src[k]);
        return idx;
    }

    /// Build over an explicit event array in a given order (the sleep-mount path — events are `tape.ResidentEventBytes.ToArray()`,
    /// slots align 1:1 with the affinity-matrix indices so the mount can turn slot-pairs into matrix cells).
    public static SimhashIndex OfEvents(byte[][] spans, IReadOnlyList<TapeEventID>? ids = null, IReadOnlyList<string>? sources = null)
    {
        var idx = new SimhashIndex();
        for (int i = 0; i < spans.Length; i++)
            idx.Add(ids is null ? new TapeEventID(i) : ids[i], spans[i], sources is null ? "" : sources[i]);
        return idx;
    }

    // ── MOUNT 1 · CANDIDATE PAIRS ──  the O(spans²) → O(candidate-pairs) collapse. Two spans are CANDIDATES iff
    // they co-occur in ≥1 band bucket (the LSH promise: near ⟹ shared bucket, Theorem 5.2). The precise φ-affinity
    // (Seriate.LineAffinity) then scores ONLY these pairs — the two-tier: Hamming/bucketing FILTERS, φ DECIDES.
    /// Yields each unordered candidate slot-pair (i<j) exactly once. `bandFlip` adds the 1-bit band-flip probes
    /// (each band's Hamming-1 neighbours) to widen recall at a small cost.
    public HashSet<(int I, int J)> CandidatePairs(bool bandFlip = false)
    {
        var pairs = new HashSet<(int, int)>();
        void EmitBucket(List<int> members)
        {
            for (int x = 0; x < members.Count; x++)
                for (int y = x + 1; y < members.Count; y++)
                {
                    int a = members[x], b = members[y];
                    pairs.Add(a < b ? (a, b) : (b, a));
                }
        }
        foreach (var members in _postings.Values) if (members.Count > 1) EmitBucket(members);
        if (bandFlip)
            // For each span, probe the Hamming-1 neighbours of each of its bands; any co-member is a candidate too.
            for (int slot = 0; slot < _ids.Count; slot++)
            {
                var bands = Simhash.BucketKeys(_sigs[slot]);
                for (int k = 0; k < Bands.Count; k++)
                    for (int bit = 0; bit < Simhash.PrefixBits; bit++)
                    {
                        ushort flipped = (ushort)(bands[k] ^ (1 << bit));
                        if (_postings.TryGetValue(flipped, out var members))
                            foreach (int other in members) if (other != slot) pairs.Add(slot < other ? (slot, other) : (other, slot));
                    }
            }
        return pairs;
    }

    // ── MOUNT 1 · INCREMENTAL ──  the O(Δ) candidate verb the persistent night-shift index
    // exists for. `CandidatePairs` above sweeps EVERY bucket (all-pairs within buckets — O(spans²) when the variant-
    // breeder degenerates a bucket; the measured 82M-pair timeout); this yields candidates for ONE slot: its bucket
    // co-members with a LOWER slot (earlier span — new×existing and new×earlier-new, exactly the pairs a Δ pass owes),
    // ranked (Hamming asc, slot asc), top-k. Posting lists are slot-ASCENDING by construction (the index is fed in
    // TapeEventID order), so the scan self-terminates at the query slot and the BandScanCap prefix keeps the LOWEST ids —
    // the canonical-exemplar end — when a degenerate mega-bucket overflows it. Deterministic; `into` is caller scratch.
    private const int BandScanCap = 4096;   // per-posting-list scan bound — the mega-bucket (all-near-dupe tape) cost ceiling
    public void TopPriorCandidates(int slot, int k, List<int> into, bool bandFlip = true)
    {
        // The old band scan is intentionally retained by CandidatePairs/NearDupes/BucketGraph.  Candidate lookup is
        // now the exact LSM/VP index: no mega-bucket cap, no per-query HashSet/List, and no approximate band-flip mode.
        _hamming.FindPriorNearest(slot, k, into, _hammingScratch);
    }

    // ── QUERY (Alg 5.3) · Hamming-neighbour retrieval + the RETRIEVAL WITNESS ──  gather the query's 4 buckets
    // (+ optional 1-bit band-flip probes), dedup by TapeEventID, filter Hamming ≤ tau, sort (Hamming ASC, TapeEventID ASC —
    // the consensus-critical order, Property 5.3), truncate top-k. The witness records (query sig, bands probed,
    // hits) — the deterministic replay contract (Theorem 5.3): same query + same index ⟹ same result.
    public RetrievalWitness Query(Sig query, int maxHamming, int topK, bool bandFlip = false)
    {
        var qBands = Simhash.BucketKeys(query);
        var probed = new List<ushort>(Bands.Count);
        var seen = new Dictionary<int, int>();                          // TapeEventID.Value → best slot (dedup, Property 5.2)
        void Gather(ushort key)
        {
            probed.Add(key);
            if (!_postings.TryGetValue(key, out var members)) return;
            foreach (int slot in members)
            {
                int idv = (int)_ids[slot].Value;
                if (!seen.ContainsKey(idv)) seen[idv] = slot;           // dense monotone ids: first-seen is the span itself
            }
        }
        for (int k = 0; k < Bands.Count; k++) Gather(qBands[k]);
        if (bandFlip)
            for (int k = 0; k < Bands.Count; k++)
                for (int bit = 0; bit < Simhash.PrefixBits; bit++) Gather((ushort)(qBands[k] ^ (1 << bit)));

        var hits = new List<NeighborHit>(seen.Count);
        foreach (var slot in seen.Values)
        {
            int d = query.Hamming(_sigs[slot]);
            if (d <= maxHamming) hits.Add(new NeighborHit(_ids[slot], d, _sources[slot]));
        }
        // sort: Hamming ASC, then TapeEventID ASC (the consensus tie-break — bytes-lexicographic degenerates to id order
        // for the monotone TapeEventID, which is the cogito-native BlobRef companion).
        hits.Sort((a, b) => a.Hamming != b.Hamming ? a.Hamming.CompareTo(b.Hamming) : a.Id.Value.CompareTo(b.Id.Value));
        if (hits.Count > topK) hits.RemoveRange(topK, hits.Count - topK);
        return new RetrievalWitness(query, qBands, probed.ToArray(), hits);
    }

    // ── MOUNT 2 · NEAR-DUPE FAMILY QUERY ──  the Hamming ball around a signature, over the INDEXED spans. Returns
    // the member slots with Hamming ≤ tau (the near-dupe cluster the variant-breeder produced), lowest-id first
    // (the CANONICAL exemplar is the earliest-seen member). The GC uses this to find candidate exemplar spans a
    // rule's expansion might be byte-contained in — see MemoryHierarchy.Gc's near-dupe pass for the LOSSLESS gate.
    public List<int> NearDupes(Sig query, int tau, bool bandFlip = true)
    {
        var seen = new HashSet<int>();
        var hits = new List<int>();
        void Consider(ushort key)
        {
            if (!_postings.TryGetValue(key, out var members)) return;
            foreach (int slot in members)
                if (seen.Add(slot) && query.Hamming(_sigs[slot]) <= tau) hits.Add(slot);
        }
        var bands = Simhash.BucketKeys(query);
        for (int k = 0; k < Bands.Count; k++) Consider(bands[k]);
        if (bandFlip)
            for (int k = 0; k < Bands.Count; k++)
                for (int bit = 0; bit < Simhash.PrefixBits; bit++) Consider((ushort)(bands[k] ^ (1 << bit)));
        hits.Sort((a, b) => _ids[a].Value.CompareTo(_ids[b].Value));    // lowest-id first = the canonical exemplar first
        return hits;
    }

    /// TapeEventID → its slot (for the near-dupe GC to look up a specific event's signature / neighbours). −1 if absent.
    public int SlotOf(TapeEventID id) => _slotOf.TryGetValue((int)id.Value, out var s) ? s : -1;
    public Sig SigAt(int slot) => _sigs[slot];
    public TapeEventID IdAt(int slot) => _ids[slot];

    // ── MOUNT 3 · THE BUCKET GRAPH (navigability edge source) ──  each span links to its bucket co-members (the
    // hub structure — a high-degree bucket is a hub; the small-world "5 clicks"). Returns per-slot adjacency (co-
    // members across all 4 bands, deduped, self excluded, capped) — the SECOND edge source Memory.Navigability
    // walks, compared against the affinity-kNN graph (do bucket-hops give a shorter mean path than φ-kNN?).
    // THE EAGER ARM — bare CLI verbs over transient indexes only; the trunk's night reads the STANDING adjacency
    // (AdjOf, row-identical by contract) because this whole-index walk is O(slots × hub-prefix) per call.
    public int[][] BucketGraph(int capPerNode = 12)
    {
        int n = _ids.Count;
        var adj = new int[n][];
        var nbr = new HashSet<int>();
        for (int slot = 0; slot < n; slot++)
        {
            nbr.Clear();
            var bands = Simhash.BucketKeys(_sigs[slot]);
            for (int k = 0; k < Bands.Count; k++)
                if (_postings.TryGetValue(bands[k], out var members))
                {
                    // gather bound: a degenerate mega-bucket would make this walk O(spans²) across the loop.
                    // Lists are slot-ascending (id-order feed), so the BandScanCap prefix holds the LOWEST slots —
                    // a superset of the capPerNode lowest the OrderBy keeps — and the output is IDENTICAL until a
                    // bucket exceeds the cap, where the graph degrades deterministically to the exemplar end.
                    int lim = Math.Min(members.Count, BandScanCap);
                    for (int x = 0; x < lim; x++) { int other = members[x]; if (other != slot) nbr.Add(other); }
                }
            adj[slot] = nbr.OrderBy(x => x).Take(capPerNode).ToArray();  // deterministic (id-ordered), capped
        }
        return adj;
    }

    /// MemStat census read — Σ band-posting slots across all buckets (each span posts into 4 bands). Counts only.
    public long PostingMass()
    {
        long posts = 0;
        foreach (var l in _postings.Values) posts += l.Count;
        return posts;
    }

    /// The hub sparkline for the Index journal event — buckets, mean occupancy, and the top hubs (largest buckets).
    public string HubSummary(int topHubs = 6)
    {
        var top = _postings.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key)
            .Take(topHubs).Select(kv => $"0x{kv.Key:X4}→{kv.Value.Count}");
        return $"buckets={_postings.Count} occ={MeanOccupancy:F2} spans={_ids.Count} · hubs {string.Join(" ", top)}";
    }
}

/// The RETRIEVAL WITNESS — the deterministic-replay record of one query: the query
/// signature, the 4 bands it derived, the buckets actually probed (4, or 4+flips), and the sorted hits with their
/// Hamming + provenance. The witness digest binds it (Theorem 5.3: same inputs ⟹ same digest). `Digest()` rides
/// the machine suite over the CCC of the result (the honest-note caveat atop SimHash.cs applies to the ABSOLUTE
/// digest; the DETERMINISM — recompute ⟹ equal — holds bit-exactly and IS the theorem the witness exists to serve).
public readonly record struct RetrievalWitness(Sig Query, Bands QueryBands, ushort[] Probed, List<NeighborHit> Hits)
{
    /// H_retrieval(R) = H_suite("cogito/retrieval_witness/" ‖ CCC(R)). Deterministic over (query, bands, hits) —
    /// the digest a replay must reproduce. CCC layout: U64(query) ‖ U16×4(bands) ‖ U64(hits) ‖ per hit {I64(id) ‖
    /// U32(hamming)}. Integer-only.
    public Hash256 Digest()
    {
        int size = 8 + Bands.Count * 2 + 8 + Hits.Count * (8 + 4);
        var ccc = new byte[size];                                       // off the hot path (per-query witness) — heap is fine, no stackalloc ternary
        var w = new CccWriter(ccc);
        w.U64(Query.Bits);
        w.U16(QueryBands.B0); w.U16(QueryBands.B1); w.U16(QueryBands.B2); w.U16(QueryBands.B3);
        w.U64((ulong)Hits.Count);
        foreach (var h in Hits) { w.I64(h.Id.Value); w.U32((uint)h.Hamming); }
        return Hash.Domain("cogito/retrieval_witness/"u8, ccc[..w.Written]);
    }

    /// A compact human/journal render — the query, buckets probed, and the hits (id · Hamming · source).
    public string Render(int maxHits = 8)
    {
        var hs = string.Join(" ", Hits.Take(maxHits).Select(h => $"{h.Id}·h{h.Hamming}·{h.Source}"));
        return $"q={Query} bands=[{QueryBands.B0:X4} {QueryBands.B1:X4} {QueryBands.B2:X4} {QueryBands.B3:X4}] probed={Probed.Length} hits={Hits.Count} · {hs}";
    }
}
