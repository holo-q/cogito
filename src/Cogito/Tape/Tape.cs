namespace Cogito;

// ── THE TAPE (hot) ──  the substrate ruling's working half. The sequence the loop induces over: an ordered
// list of events (raw utf8) — the substrate every PROVEN organ already uses (CritLock / Seriate / Radula all
// induce over a byte tape built from event payloads; only Farm's pre-fusion loop routed the hot path through the
// event-sourced EventLog and paid the ceremony every step). Mutable ORDER so SLEEP can re-serialize it
// (couplings-guided defrag recovers the relational scale a bad intake capped — Seriate.Seriate). The durable
// record is the Journal, off the hot path. One reality, one hot tape; the multi-node topology is more masks
// over THIS tape.
//
// MEMORY-HIERARCHY: the tape is the SOURCE OF RECORD and never forgets — every event carries a
// STABLE `TapeEventID` (monotonic, assigned at append, permutation-stable so it survives sleep's Reorder). Phase 3
// (TAPE-SHED) completes the hierarchy: the RESIDENT tape is a rolling working window; an event whose structure the
// grammar carries EVACUATES its bytes to the append-only event-byte log (a run-dir file) and leaves the resident lists,
// while `Resolve(TapeEventID)` keeps resolving it from the log forever — the log-backed never-forget contract every
// index posting and demotion chain rides. THE VIEW — residents in tape order
// followed by shed events in id order — is the ONE canonical corpus every whole-world reader consumes (Concat /
// WeightsFor / the loom's harvest / Pearl.Audit's co-walk), so shedding never changes the induced grammar
// (event order is grammar-irrelevant by the barrier law — the order-freedom theorem). DROPPED events (stale
// unreflected replays — hypotheses, not evidence) leave the view entirely: their counts vanish at the next re-greed
// and their ReplayCount slot frees mint headroom, but their bytes still land in the log so late references
// (demotion chains, index slots at checkpoint-reload) resolve forever — dropping is epistemic, never forensic.

/// A tape event's stable identity — monotonic, assigned at append, permutation-stable (sleep's Reorder permutes
/// the ids WITH the event bytes, so a TapeEventID names the same payload regardless of tape order). The hot-path anchor a
/// demoted rule body references, the durable companion being the Journal BlobRef.
public readonly record struct TapeEventID(long Value)
{
    public override string ToString() => $"s{Value}";
}

/// A tape event's EPISTEMIC status — THE REFLECTION LAW's memory-integrity face (the Pearl faculty,  face 2,
/// ): the mollusk may SPEAK from hypothesis but may not COUNT it as evidence. Replay recurrences are
/// autocorrelated samples of the mollusk's own state — raw counting reads the echo as the world (effective sample
/// size → 0, the sealed-loop collapse) — so the COUNT MEASURE is provenance-weighted (WeightsFor): evidence weighs
/// wScale, an unreflected Replay weighs 1. Reflection (Tape.Reflect) is the de-correlation event: a real event
/// exercising a rule reflects the replay events supporting it (Pearl.Corroborate), promoting their counts to evidence.
public enum Provenances : byte
{
    Real      = 0,   // world contact — corpus intake, MIX re-ingest: an independent sample of reality
    Replay     = 1,   // Frozen tape provenance byte; identifier renamed from Dream. Hypothesis at ε-weight until reflected.
    Breach    = 2,   // a reality-contradiction event (breach-lower) — contact the mollusk was NOT fit to; evidence
    Reflected = 3,   // verifier-certified self-data whose corroboration is already inside the machine. Born evidence,
                     // like Breach; distinct from Replay (epsilon weight until corroborated) and Real (world contact)
    Execution = 4,   // an action actually taken and its source-typed routing result. Inducible self-history, but
                     // neither evidence nor hypothesis: it cannot fund replays, corroborate itself, or rot as a claim
}

/// Orthogonal intake roles carried by each tape event. Provenance answers where
/// a span came from; roles answer which machine surfaces may consume it. A
/// single event may serve more than one surface (for example a world packet
/// can be grammar input and audit-only evidence at once).
[Flags]
public enum TapeEventRoles : byte
{
    None = 0,
    GrammarInput = 1 << 0,
    Measurement = 1 << 1,
    AuditOnly = 1 << 2, // Frozen checkpoint role bit; identifier renamed from Custody.
}

/// The KIND of a grammar rule's body — the MEMORY-HIERARCHY seam, declared here (the tape owns the ref
/// target). `Expansion` is Re-Pair's materialized case; `TapeRef` is GC-demotion's (a literal rule whose expansion
/// is covered by reference bytes, evicted under the bit budget, resolving through a TapeEventID chain); `SlotClass`
/// is anti-unification's paradigm class.
public enum RuleBodyKind
{
    Expansion,   // materialized bytes — Re-Pair's rule expansion (concatenate the pattern)
    TapeRef,     // : a demoted rule body = an ORDERED TapeEventSeg chain over reference events (single-event identity is
                 // the 1-seg case; a multi-line mega-rule is the m-seg case — demote-don't-delete; re-promote on recurrence)
    SlotClass,   // anti-unification: a PARADIGM class — Pattern lists the alternative member symbols (pick-one, not
                 // concatenate-all); the generative slot that replaces N literal surfaces (AntiUnify.MintSlots)
}

/// One link in a demoted rule body's resolution chain — a byte range over ONE tape event's CONCAT UNIT: the
/// event bytes FOLLOWED BY the '\n' separator Concat() writes after every event. `Len` runs 0..eventBytes.Length+1−Start;
/// when Start+Len == eventBytes.Length+1 the range INCLUDES that trailing separator — that is precisely how a multi-event
/// chain reproduces a multi-line mega-rule's inter-line newlines (a suffix of the start event, whole interior events
/// each WITH their separator, a prefix of the end event). A single TapeEventSeg with Start+Len ≤ eventBytes.Length is an
/// intra-event slice: single-event identity = the whole-event slice; near-dupe containment = an offset slice.
public readonly record struct TapeEventSeg(TapeEventID Id, int Start, int Len)
{
    public override string ToString() => $"{Id}[{Start}+{Len}]";
}

/// One tape event in the canonical view (residents in tape order, then shed in id order).
/// `Len` is the event byte length WITHOUT the '\n' separator (a unit contributes Len+1 view bytes). `Source` is the
/// event's provenance label ("corpus" / "node0" / a peer node's tag) — the JEWEL AXIS the cross-reflection reads
/// (a Replay reflects on a DIFFERENT source exercising its rule, so the audit must carry each exercising unit's source,
/// not just its evidence bit). `Roles` is orthogonal intake routing: the same
/// event can be grammar input, measurement, audit-only, or a combination. The
/// reflection audit's monotone cursor and the weighted induce both walk this
/// instead of positional event reads.
public readonly record struct TapeEventView(
    TapeEventID Id,
    int Len,
    Provenances Provenance,
    bool Evidence,
    string Source,
    TapeEventRoles Roles = TapeEventRoles.GrammarInput)
{
    public bool HasRole(TapeEventRoles role) => (Roles & role) == role;
}

/// One prevalidated append in a tape transaction. Transactions are used for
/// audit-only chains whose prefix would be semantically misleading if a later
/// member failed to land.
internal readonly record struct TapeAppend(
    byte[] Bytes,
    string Source,
    Provenances Provenance,
    TapeEventRoles Roles);

/// The hot working tape — the ordered events the drive induces its grammar over. `Append` accretes an event with
/// its provenance and returns the event's stable id; `Concat` renders the induction input (THE VIEW, newline-
/// joined); `Reorder` is the sleep-pass re-serialization (order changes, bytes + ids do not); `Resolve` reads a
/// payload back by its stable id — resident, shed, or dropped (the log-backed read path). `Evacuate` is the phase-3
/// night verb: SHED learned evidence and execution events to the byte log (they stay in the view) and DROP stale unreflected replays
/// (they leave it). `ByteLength` is the VIEW's byte length — what the loom's Δ probe and the stride gate read.
public sealed class Tape : IDisposable
{
    private const int FullSchemaMarker = -1;
    private const byte FullSchemaVersion = 2;
    private const TapeEventRoles KnownRoles = TapeEventRoles.GrammarInput | TapeEventRoles.Measurement | TapeEventRoles.AuditOnly;
    private const byte ProvMask     = 0x7F;              // low bits: the Provenances value
    private const byte ReflectedBit = 0x80;             // high bit: a Replay span corroborated by real exercise (monotonic — set once, never cleared)

    private readonly List<byte[]> _eventBytes = new();        // the RESIDENT working event payloads (mutable order — sleep reorders)
    private readonly List<string> _eventSources = new();      // per-resident provenance label, parallel to _eventBytes (mask/attention food)
    private readonly List<TapeEventID> _eventIDs = new();          // per-resident stable id, parallel to _eventBytes (survives Reorder)
    private readonly List<byte> _prov = new();           // per-resident packed epistemic status (Provenances | ReflectedBit)
    private readonly List<TapeEventRoles> _eventRoles = new(); // per-resident intake roles, orthogonal to provenance
    private readonly Dictionary<TapeEventID, byte[]> _eventBytesByID = new();   // resident stable-id → event bytes (the hot resolve path)
    private readonly Dictionary<int, List<TapeEventID>> _eventIDsByContent = new();   // content-hash bucket → event ids with those exact bytes
    private readonly Dictionary<TapeEventID, int> _residentIndexByID = new();   // resident stable-id → CURRENT tape index (O(1), rebuilt on Reorder)
    private long _nextId;                                // monotonic id counter — the tape never reuses an id

    // ── THE EVENT BYTE LOG (phase 3) ──  evacuated bytes' append-only home: `[bytes]` records addressed by (Off, Len)
    // from the entry tables below. SHED entries stay in the view (their counts persist — the grammar carries their
    // structure and the log carries their bytes); TOMB entries (dropped replays) left the view but still resolve —
    // demotion chains, content buckets, and the checkpoint-reload index feed never dangle.
    private readonly record struct EvacEntry(int Len, long Off, byte Prov, string Source, TapeEventRoles Roles = TapeEventRoles.GrammarInput);
    private readonly Dictionary<long, EvacEntry> _shed = new();   // id → shed record (IN the view)
    private readonly Dictionary<long, EvacEntry> _tomb = new();   // id → dropped record (OUT of the view)
    private readonly List<long> _shedEventIDs = new();                 // shed event ids, kept SORTED — the view's second half
    private Stream? _log;                                         // the mounted event byte log (null = no shedding possible)
    private long _logEnd;                                         // append cursor (≥ the file length we own)

    // Runtime mutation receipts. These are explicit transition sets, populated at
    // the mutation site and drained by Loom; no consumer scans the tape to infer
    // what changed. Revisions are not serialized (the tape bytes/ids are the
    // durable state and a resumed tape starts a fresh receipt epoch).
    private long _revision;
    private long _nonAppendRevision;
    private long _orderRevision;
    private long _reportedOrderRevision;
    private readonly List<TapeEventID> _appendedSinceDrain = new();
    private readonly List<TapeEventID> _reflectedSinceDrain = new();
    private readonly List<TapeEventID> _shedSinceDrain = new();
    private readonly List<TapeEventID> _droppedSinceDrain = new();
    private readonly List<TapeEventID> _checkpointAppended = new();
    private readonly List<TapeEventID> _checkpointReflected = new();
    private readonly List<TapeEventID> _checkpointShed = new();
    private readonly List<TapeEventID> _checkpointDropped = new();
    // The resident order at the last committed checkpoint. Reorders are
    // composed against this one coordinate frame at capture time; recording
    // each local Reorder frame made chained sleep passes unreplayable.
    private TapeEventID[] _checkpointBaseResidentIDs = [];
    private bool _checkpointReordered;

    public int Count => _eventBytes.Count;                    // RESIDENT event count — the phase-3 plateau counter
    public long NextId => _nextId;                       // the id high-water — id-range iteration reads THIS, never Count (residents ≠ ids once shedding starts)
    public int ShedCount => _shed.Count;
    public int DroppedCount { get; private set; }
    public long ByteLength { get; private set; }         // Σ VIEW event bytes + newline separators — the loom Δ probe / stride gate input (append +, drop −; shed leaves it)
    public long GrammarByteLength { get; private set; }  // Σ VIEW bytes for GrammarInput events; audit-only ByteLength remains the full view
    public long ResidentBytes { get; private set; }      // Σ RESIDENT event bytes + separators — the census's RAM axis
    public TapeRevision Revision => new(_revision);
    /// Changes that invalidate append-only audit extension: reflection,
    /// evacuation, and resident reordering. Append-only grammar tails may use
    /// the delta audit only while this epoch is unchanged.
    public TapeRevision NonAppendRevision => new(_nonAppendRevision);
    public TapeRevision OrderRevision => new(_orderRevision);
    internal TapeMutationCursor MutationCursor => new(
        _revision, _orderRevision, _reportedOrderRevision, _nextId, _eventIDs.Count,
        _appendedSinceDrain.Count, _reflectedSinceDrain.Count, _shedSinceDrain.Count, _droppedSinceDrain.Count);
    public IReadOnlyList<byte[]> ResidentEventBytes => _eventBytes;        // residents only — positional readers (defrag, census, report scans)
    public IReadOnlyList<string> ResidentEventSources => _eventSources;
    public IReadOnlyList<TapeEventID> ResidentEventIDs => _eventIDs;            // residents' stable event ids in current tape order
    public IReadOnlyList<long> ShedEventIDs => _shedEventIDs;      // shed event ids ascending — the view's tail order

    // O(1) epistemic census — maintained on Append/Reflect/Evacuate, recomputed by Load (derived, never serialized).
    public int RealCount           { get; private set; } // spans born Provenances.Real
    public int ReplayCount          { get; private set; } // spans born Provenances.Replay still on the view (reflected or not; a DROP frees its slot)
    public int BreachCount         { get; private set; } // spans born Provenances.Breach
    public int ReflectedReplayCount { get; private set; } // Replay spans corroborated into evidence by reflection (ReflectedBit set)
    public int ReflectedCount      { get; private set; } // spans born Provenances.Reflected (verifier-certified evidence at birth)
    public int ExecutionCount      { get; private set; } // spans born Provenances.Execution (inducible action history, epistemically neutral)
    public int BornEvidenceCount => RealCount + BreachCount + ReflectedCount;

    /// The replay-fraction headroom — how many more UNREFLECTED replay spans
    /// may land before the cap (`ratio` × born-evidence spans) binds. A reflected replay frees its slot (it earned evidence
    /// status), so the live unreflected stock is ReplayCount − ReflectedReplayCount. Negative ⟹ the cap is over-full and
    /// the mint follows newly admitted evidence (replays can never outpace evidence available to corroborate them).
    /// The `ratio <= 0` (unbounded) sentinel is the
    /// CALLER's — its value differs by site (a per-step mint cap vs long.MaxValue) — so this computes only the bounded arm.
    public long ComputeUnreflectedHeadroom(double ratio) => (long)(ratio * BornEvidenceCount) - (ReplayCount - ReflectedReplayCount);

    // ── THE REFLECT-BY-SOURCE CENSUS (the multi-node jewel read — the SQUADRON's decisive live signal) ──  the
    // aggregate ReflectedReplayCount was the flagship saga's blind spot: it counts corroboration but hides WHICH JEWEL
    // reflected whom, so a node re-reflecting its OWN echoes (the sealed-loop degeneracy) and a genuine cross-mesh
    // reflection read IDENTICAL on the curve. These split the population + the corroboration by the REPLAY event's own
    // source tag, so the curve can show node0-replays-reflected vs peer-replays-reflected LIVE (the by-source reflect-
    // rate columns — Reads reads ReplaysBySource/ReflectedBySource + the node0/peer rollups). Composed exactly like the
    // aggregate counters above — incremented on Append(Replay)/Reflect, rebuilt by CountProv on Load; NEVER serialized
    // (the resident+shed spans' source labels + reflected bits are the source of record, so a Load reconstructs them
    // byte-exactly — the same derived-census contract ReflectedReplayCount rides, so resume stays exact for free).
    private readonly Dictionary<string, int> _replaysBySource = new();      // source tag → Replay spans born under it still on the view (reflected or not)
    private readonly Dictionary<string, int> _reflectedBySource = new();   // source tag → those Replays corroborated into evidence (a subset of _replaysBySource, per key)
    /// Per-source Replay population — the by-source reflect-rate's DENOMINATOR (how many of this node's utterances live on the view).
    public IReadOnlyDictionary<string, int> ReplaysBySource => _replaysBySource;
    /// Per-source reflected-Replay count — the by-source reflect-rate's NUMERATOR (how many of this node's utterances a DIFFERENT source corroborated).
    public IReadOnlyDictionary<string, int> ReflectedBySource => _reflectedBySource;
    /// node0's reflected replays — the ORIGIN node's corroboration (the degenerate-control read: post-drain this must FREEZE with no mesh to reflect it).
    public int ReflectedNode0 => _reflectedBySource.GetValueOrDefault(OriginSource);
    /// PEER-sourced reflected replays — Σ over every source tag that is neither node0 nor the corpus/Real label (the cross-mesh jewel the sealed loop cannot produce; SUSTAINS in the 3-node arm, converges in the 2-node MIRROR).
    public int ReflectedPeer { get { int s = 0; foreach (var (k, v) in _reflectedBySource) if (IsPeerSource(k)) s += v; return s; } }
    /// node0's Replay population (the origin node's un-corroborated utterance stock — the node0 reflect-rate's denominator).
    public int ReplaysNode0 => _replaysBySource.GetValueOrDefault(OriginSource);
    /// PEER Replay population — Σ over peer source tags (the peer reflect-rate's denominator; 0 in single-node arms — the FREEZE control's tell).
    public int ReplaysPeer { get { int s = 0; foreach (var (k, v) in _replaysBySource) if (IsPeerSource(k)) s += v; return s; } }

    // The jewel taxonomy over source tags: node0 is the origin (single-node self-play's only voice); "corpus"
    // and any Real-intake label are WORLD, never a node — they are born evidence and never enter these Replay
    // counters, but the classifier excludes them so a mislabeled Replay can never masquerade as a peer. Everything
    // else is a PEER node (node1, node2, … — Worker B's fan-out tags), the generator-independent jewel.
    private const string OriginSource = "node0";
    private static bool IsPeerSource(string src) => src != OriginSource && src != "corpus" && src != "eml" && src != "breach";

    /// Mount the event byte log — the shed bytes' append-only home (the trunk mounts `<run>/tape.spanlog`; verify arms
    /// mount a MemoryStream). Idempotent per tape; must be mounted BEFORE Load restores a tape that had shed.
    /// The tape takes ownership (Dispose closes it). `_logEnd` continues from the stream's current length, so a
    /// resumed run appends after the records the checkpoint's entries address (orphan tail records from a kill
    /// between shed and checkpoint are dead weight, never mis-addressed — the entries carry exact offsets).
    public void MountLog(Stream log)
    {
        if (_log is not null) throw new InvalidOperationException("Tape.MountLog: an event byte log is already mounted");
        _log = log;
        _logEnd = log.Length;
    }

    public void CopyLogTo(Stream target)
    {
        if (_log is null) return;
        _log.Flush();
        long position = _log.Position;
        _log.Position = 0;
        _log.CopyTo(target);
        _log.Position = position;
        target.Flush();
    }

    public void Dispose() { _log?.Dispose(); _log = null; }

    /// Accrete one span onto the tape with its source (the autoregressive loopback appends a node's utterances;
    /// intake + the MIX rail append "corpus") and its EPISTEMIC provenance (the reflection law's irreversible field —
    /// a span appended without it loses its evidence status permanently, hence the byte precedes the campfire).
    /// Defaults to Real so pre-provenance call sites keep today's all-evidence semantics unchanged. Order is
    /// append; sleep re-orders in place via Reorder. Returns the event's stable id.
    public TapeEventID Append(byte[] eventBytes, string source, Provenances prov = Provenances.Real,
        TapeEventRoles roles = TapeEventRoles.GrammarInput)
    {
        if (eventBytes.AsSpan().StartsWith("ORGANIC-COMPARISON"u8)
            && roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly))
            throw new InvalidDataException("organic comparison packets require Measurement|AuditOnly roles");
        ValidateRoles(roles);
        var id = new TapeEventID(_nextId++);
        _eventBytes.Add(eventBytes);
        _eventSources.Add(source);
        _eventIDs.Add(id);
        _prov.Add((byte)prov);
        _eventRoles.Add(roles);
        if (prov == Provenances.Real) RealCount++;
        else if (prov == Provenances.Replay) { ReplayCount++; _replaysBySource[source] = _replaysBySource.GetValueOrDefault(source) + 1; }   // by-source population (the reflect-rate denominator)
        else if (prov == Provenances.Breach) BreachCount++;
        else if (prov == Provenances.Reflected) ReflectedCount++;
        else if (prov == Provenances.Execution) ExecutionCount++;
        _eventBytesByID[id] = eventBytes;
        _residentIndexByID[id] = _eventBytes.Count - 1;
        int ch = ContentHash(eventBytes);
        (_eventIDsByContent.TryGetValue(ch, out var bucket) ? bucket : _eventIDsByContent[ch] = new()).Add(id);
        ByteLength += eventBytes.Length + 1;                   // +1 for the newline separator Concat inserts
        if ((roles & TapeEventRoles.GrammarInput) != 0) GrammarByteLength += eventBytes.Length + 1;
        ResidentBytes += eventBytes.Length + 1;
        _revision++;
        _appendedSinceDrain.Add(id);
        _checkpointAppended.Add(id);
        return id;
    }

    /// Append a contiguous receipt chain after validating every member. No
    /// caller can observe an action-only prefix between the three task links.
    /// The returned ids are reserved in the same order as the input batch.
    internal TapeEventID[] AppendTransaction(IReadOnlyList<TapeAppend> appends, Action<TapeEventID[]>? commit = null)
    {
        ArgumentNullException.ThrowIfNull(appends);
        if (appends.Count == 0) throw new ArgumentException("tape transaction is empty", nameof(appends));
        for (int index = 0; index < appends.Count; index++)
        {
            TapeAppend append = appends[index];
            if (append.Bytes is null || append.Source is null)
                throw new InvalidDataException("tape transaction append is incomplete");
            if (append.Bytes.AsSpan().StartsWith("ORGANIC-COMPARISON"u8)
                && append.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly))
                throw new InvalidDataException("organic comparison packets require Measurement|AuditOnly roles");
            ValidateRoles(append.Roles);
        }

        TapeEventID[] eventIDs = new TapeEventID[appends.Count];
        for (int index = 0; index < appends.Count; index++)
        {
            TapeAppend append = appends[index];
            eventIDs[index] = Append(append.Bytes, append.Source, append.Provenance, append.Roles);
        }
        try
        {
            commit?.Invoke(eventIDs);
            return eventIDs;
        }
        catch
        {
            RollbackAppends(eventIDs);
            throw;
        }
    }

    private void RollbackAppends(IReadOnlyList<TapeEventID> eventIDs)
    {
        for (int index = eventIDs.Count - 1; index >= 0; index--)
        {
            TapeEventID id = eventIDs[index];
            if (_eventIDs.Count == 0 || _eventIDs[^1] != id)
                throw new InvalidOperationException("tape transaction rollback lost its append tail");
            int residentIndex = _eventIDs.Count - 1;
            byte[] bytes = _eventBytes[residentIndex];
            string source = _eventSources[residentIndex];
            Provenances provenance = ProvenanceOf(id);
            TapeEventRoles roles = _eventRoles[residentIndex];
            _eventIDs.RemoveAt(residentIndex);
            _eventBytes.RemoveAt(residentIndex);
            _eventSources.RemoveAt(residentIndex);
            _prov.RemoveAt(residentIndex);
            _eventRoles.RemoveAt(residentIndex);
            _eventBytesByID.Remove(id);
            _residentIndexByID.Remove(id);
            int contentHash = ContentHash(bytes);
            if (_eventIDsByContent.TryGetValue(contentHash, out List<TapeEventID>? bucket))
            {
                bucket.Remove(id);
                if (bucket.Count == 0) _eventIDsByContent.Remove(contentHash);
            }
            ByteLength -= bytes.Length + 1;
            if ((roles & TapeEventRoles.GrammarInput) != 0)
                GrammarByteLength -= bytes.Length + 1;
            ResidentBytes -= bytes.Length + 1;
            switch (provenance)
            {
                case Provenances.Real: RealCount--; break;
                case Provenances.Replay:
                    ReplayCount--;
                    _replaysBySource[source] = _replaysBySource.GetValueOrDefault(source) - 1;
                    if (_replaysBySource[source] == 0) _replaysBySource.Remove(source);
                    break;
                case Provenances.Breach: BreachCount--; break;
                case Provenances.Reflected: ReflectedCount--; break;
                case Provenances.Execution: ExecutionCount--; break;
            }
            RemoveLastMutation(_appendedSinceDrain, id);
            RemoveLastMutation(_checkpointAppended, id);
            _nextId--;
            _revision--;
        }
    }

    private static void RemoveLastMutation(List<TapeEventID> ids, TapeEventID id)
    {
        if (ids.Count == 0 || ids[^1] != id)
            throw new InvalidOperationException("tape transaction rollback mutation tail diverged");
        ids.RemoveAt(ids.Count - 1);
    }

    /// Drain the exact tape transitions since the previous drain. The returned
    /// arrays are detached from the tape and safe for Loom to retain. An order
    /// revision is present only in the delta that first observes that reorder.
    public TapeDelta DrainDelta()
    {
        TapeRevision orderRevision = _orderRevision != _reportedOrderRevision
            ? new TapeRevision(_orderRevision)
            : TapeRevision.Initial;
        _reportedOrderRevision = _orderRevision;
        var delta = new TapeDelta(
            new TapeRevision(_revision),
            orderRevision,
            _appendedSinceDrain.ToArray(),
            _reflectedSinceDrain.ToArray(),
            _shedSinceDrain.ToArray(),
            _droppedSinceDrain.ToArray());
        _appendedSinceDrain.Clear();
        _reflectedSinceDrain.Clear();
        _shedSinceDrain.Clear();
        _droppedSinceDrain.Clear();
        return delta;
    }

    internal TapeCheckpointDelta CaptureCheckpointDelta()
    {
        TapeRevision orderRevision = _checkpointReordered ? new TapeRevision(_orderRevision) : TapeRevision.Initial;
        TapeDelta mutation = new(new TapeRevision(_revision), orderRevision,
            _checkpointAppended.ToArray(), _checkpointReflected.ToArray(), _checkpointShed.ToArray(), _checkpointDropped.ToArray());
        TapeCheckpointAppend[] appended = new TapeCheckpointAppend[_checkpointAppended.Count];
        for (int i = 0; i < appended.Length; i++)
        {
            TapeEventID id = _checkpointAppended[i];
            appended[i] = new TapeCheckpointAppend(id, SourceOf(id),
                Resolve(_checkpointAppended[i], out byte[] bytes) ? bytes : throw new InvalidDataException($"checkpoint tape append {id} is not resolvable"),
                (byte)((byte)ProvenanceOf(id) | (IsReflected(id) ? 0x80 : 0)), RolesOf(id));
        }
        TapeCheckpointEvacuation[] shed = CaptureEvacuations(_checkpointShed, _shed);
        TapeCheckpointEvacuation[] dropped = CaptureEvacuations(_checkpointDropped, _tomb);
        TapeCheckpointReorderEdit[] edits = _checkpointReordered
            ? BuildCheckpointReorderEdits()
            : [];
        return new TapeCheckpointDelta(mutation, appended, shed, dropped, edits, _checkpointReordered, ReorderAfterEvacuation: true);
    }

    private TapeCheckpointReorderEdit[] BuildCheckpointReorderEdits()
    {
        // Appends are always born at the tail. Evacuation removes ids from
        // that baseline regardless of whether it happened before or after a
        // live reorder, so the replay frame is the post-evacuation resident
        // order and one final sparse permutation is sufficient.
        var baseline = new List<TapeEventID>(_checkpointBaseResidentIDs.Length + _checkpointAppended.Count);
        baseline.AddRange(_checkpointBaseResidentIDs);
        foreach (TapeEventID id in _checkpointAppended) baseline.Add(id);
        if (_checkpointShed.Count != 0 || _checkpointDropped.Count != 0)
        {
            var evacuated = new HashSet<long>(_checkpointShed.Count + _checkpointDropped.Count);
            foreach (TapeEventID id in _checkpointShed) evacuated.Add(id.Value);
            foreach (TapeEventID id in _checkpointDropped) evacuated.Add(id.Value);
            baseline.RemoveAll(id => evacuated.Contains(id.Value));
        }

        if (baseline.Count != _eventIDs.Count)
            throw new InvalidDataException($"checkpoint tape reorder baseline has {baseline.Count} residents, current tape has {_eventIDs.Count}");

        var remaining = new HashSet<TapeEventID>(baseline);
        if (remaining.Count != baseline.Count)
            throw new InvalidDataException("checkpoint tape reorder baseline contains duplicate resident ids");
        foreach (TapeEventID id in _eventIDs)
            if (!remaining.Remove(id))
                throw new InvalidDataException($"checkpoint tape reorder current resident {id} is absent from the baseline");
        if (remaining.Count != 0)
            throw new InvalidDataException("checkpoint tape reorder baseline contains a resident absent from the current tape");

        var edits = new List<TapeCheckpointReorderEdit>();
        for (int target = 0; target < _eventIDs.Count; target++)
        {
            if (baseline[target] != _eventIDs[target])
                edits.Add(new(_eventIDs[target], baseline[target]));
        }
        return edits.ToArray();
    }

    private static TapeCheckpointEvacuation[] CaptureEvacuations(List<TapeEventID> ids, Dictionary<long, EvacEntry> entries)
    {
        TapeCheckpointEvacuation[] result = new TapeCheckpointEvacuation[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            TapeEventID id = ids[i];
            if (!entries.TryGetValue(id.Value, out EvacEntry entry)) throw new InvalidDataException($"checkpoint tape evacuation {id} is missing");
            result[i] = new TapeCheckpointEvacuation(id, entry.Source, entry.Prov, entry.Len, entry.Off, entry.Roles);
        }
        return result;
    }

    internal void CommitCheckpointDelta()
    {
        _checkpointAppended.Clear(); _checkpointReflected.Clear(); _checkpointShed.Clear(); _checkpointDropped.Clear();
        _checkpointBaseResidentIDs = _eventIDs.ToArray();
        _checkpointReordered = false;
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in TapeCheckpointDelta delta)
    {
        writer.U8(3);
        writer.Bool(delta.Reordered);
        WriteAppends(writer, delta.Appended);
        WriteIDs(writer, delta.Mutation.Reflected);
        WriteEvacuations(writer, delta.Shed);
        WriteEvacuations(writer, delta.Dropped);
        if (delta.Reordered)
        {
            writer.I32(delta.ReorderEdits.Length);
            foreach (TapeCheckpointReorderEdit edit in delta.ReorderEdits)
            {
                writer.I64(edit.ID.Value);
                writer.I64(edit.SlotID.Value);
            }
        }
    }

    private static void WriteAppends(CkptWriter writer, TapeCheckpointAppend[] appends)
    {
        writer.I32(appends.Length);
        foreach (TapeCheckpointAppend append in appends)
        {
            writer.I64(append.ID.Value); writer.Str(append.Source); writer.Bytes(append.Bytes); writer.U8(append.Provenance); writer.U8((byte)append.Roles);
        }
    }

    private static void WriteIDs(CkptWriter writer, TapeEventID[] ids)
    {
        writer.I32(ids.Length);
        foreach (TapeEventID id in ids) writer.I64(id.Value);
    }

    private static void WriteEvacuations(CkptWriter writer, TapeCheckpointEvacuation[] entries)
    {
        writer.I32(entries.Length);
        foreach (TapeCheckpointEvacuation entry in entries)
        {
            writer.I64(entry.ID.Value); writer.Str(entry.Source); writer.U8(entry.Provenance); writer.I32(entry.Length); writer.I64(entry.Offset); writer.U8((byte)entry.Roles);
        }
    }

    internal static TapeCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte version = reader.U8();
        if (version is not (1 or 2 or 3)) throw new InvalidDataException("unknown tape checkpoint delta version");
        bool reordered = reader.Bool();
        TapeCheckpointAppend[] appends = ReadAppends(reader, version >= 3);
        TapeEventID[] reflected = ReadIDs(reader);
        TapeCheckpointEvacuation[] shed = ReadEvacuations(reader, version >= 3);
        TapeCheckpointEvacuation[] dropped = ReadEvacuations(reader, version >= 3);
        TapeCheckpointReorderEdit[] edits = reordered ? ReadReorderEdits(reader) : [];
        TapeDelta mutation = new(TapeRevision.Initial, reordered ? new TapeRevision(1) : TapeRevision.Initial,
            appends.Select(static x => x.ID).ToArray(), reflected, shed.Select(static x => x.ID).ToArray(), dropped.Select(static x => x.ID).ToArray());
        return new TapeCheckpointDelta(mutation, appends, shed, dropped, edits, reordered, ReorderAfterEvacuation: version >= 2);
    }

    private static TapeCheckpointReorderEdit[] ReadReorderEdits(CkptReader reader)
    {
        int count = reader.I32();
        if (count < 0) throw new InvalidDataException("negative tape checkpoint reorder edit count");
        TapeCheckpointReorderEdit[] result = new TapeCheckpointReorderEdit[count];
        for (int i = 0; i < count; i++) result[i] = new(new TapeEventID(reader.I64()), new TapeEventID(reader.I64()));
        return result;
    }

    private static TapeCheckpointAppend[] ReadAppends(CkptReader reader, bool hasRoles)
    {
        int count = reader.I32();
        if (count < 0) throw new InvalidDataException("negative tape checkpoint append count");
        TapeCheckpointAppend[] result = new TapeCheckpointAppend[count];
        for (int i = 0; i < count; i++) result[i] = new(new TapeEventID(reader.I64()), reader.Str(), reader.Bytes(), reader.U8(), hasRoles ? ReadRoles(reader) : TapeEventRoles.GrammarInput);
        return result;
    }

    private static TapeEventID[] ReadIDs(CkptReader reader)
    {
        int count = reader.I32();
        if (count < 0) throw new InvalidDataException("negative tape checkpoint id count");
        TapeEventID[] result = new TapeEventID[count];
        for (int i = 0; i < count; i++) result[i] = new TapeEventID(reader.I64());
        return result;
    }

    private static TapeCheckpointEvacuation[] ReadEvacuations(CkptReader reader, bool hasRoles)
    {
        int count = reader.I32();
        if (count < 0) throw new InvalidDataException("negative tape checkpoint evacuation count");
        TapeCheckpointEvacuation[] result = new TapeCheckpointEvacuation[count];
        for (int i = 0; i < count; i++) result[i] = new(new TapeEventID(reader.I64()), reader.Str(), reader.U8(), reader.I32(), reader.I64(), hasRoles ? ReadRoles(reader) : TapeEventRoles.GrammarInput);
        return result;
    }

    private static TapeEventRoles ReadRoles(CkptReader reader)
    {
        TapeEventRoles roles = (TapeEventRoles)reader.U8();
        ValidateRoles(roles);
        return roles;
    }

    private static void ValidateRoles(TapeEventRoles roles)
    {
        if ((roles & ~KnownRoles) != 0)
            throw new InvalidDataException($"unknown tape event role bits 0x{(byte)roles:X2}");
    }

    internal void ApplyCheckpointDelta(in TapeCheckpointDelta delta)
    {
        foreach (TapeCheckpointAppend append in delta.Appended)
        {
            TapeEventID actual = Append(append.Bytes, append.Source, (Provenances)(append.Provenance & 0x7F), append.Roles);
            if (actual != append.ID) throw new InvalidDataException($"checkpoint tape append expected {append.ID}, got {actual}");
            if ((append.Provenance & 0x80) != 0 && !Reflect(actual)) throw new InvalidDataException($"checkpoint tape append {actual} reflection failed");
        }
        foreach (TapeEventID id in delta.Mutation.Reflected)
            if (!Reflect(id) && !IsReflected(id)) throw new InvalidDataException($"checkpoint tape reflection {id} failed");
        if (!delta.ReorderAfterEvacuation && delta.Reordered)
        {
            int[] order = BuildCheckpointReorder(delta.ReorderEdits);
            Reorder(order);
        }
        ApplyCheckpointEvacuations(delta.Shed, delta.Dropped);
        if (delta.ReorderAfterEvacuation && delta.Reordered)
        {
            int[] order = BuildCheckpointReorder(delta.ReorderEdits);
            Reorder(order);
        }
        CommitCheckpointDelta();
        // The replayed order is already consumed from the durable rail. Do
        // not surface it again as a fresh runtime reorder on the first drain
        // after resume; subsequent live Reorder calls advance the receipt.
        _reportedOrderRevision = _orderRevision;
        // Checkpoint replay is a durable consumer, not a live mutation. Append,
        // reflect, and evacuation receipts produced while rebuilding the tape
        // must not be reported to Loom's first post-resume drain.
        _appendedSinceDrain.Clear();
        _reflectedSinceDrain.Clear();
        _shedSinceDrain.Clear();
        _droppedSinceDrain.Clear();
    }

    private int[] BuildCheckpointReorder(TapeCheckpointReorderEdit[] edits)
    {
        if (edits.Length > _eventIDs.Count) throw new InvalidDataException("checkpoint tape reorder edits exceed resident count");
        int[] order = new int[_eventIDs.Count];
        Array.Fill(order, -1);
        HashSet<long> moved = new();
        HashSet<long> slots = new();
        foreach (TapeCheckpointReorderEdit edit in edits)
        {
            int source = PositionOf(edit.ID);
            int target = PositionOf(edit.SlotID);
            if (source < 0 || target < 0) throw new InvalidDataException($"checkpoint tape reorder references missing resident {edit.ID}/{edit.SlotID}");
            if (!moved.Add(edit.ID.Value) || !slots.Add(edit.SlotID.Value))
                throw new InvalidDataException("checkpoint tape reorder edits contain duplicate source or slot");
            order[target] = source;
        }
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] >= 0) continue;
            TapeEventID resident = _eventIDs[i];
            if (moved.Contains(resident.Value))
                throw new InvalidDataException("checkpoint tape reorder leaves a moved slot unassigned");
            order[i] = i;
        }
        bool[] seen = new bool[order.Length];
        foreach (int source in order)
        {
            if ((uint)source >= (uint)order.Length || seen[source])
                throw new InvalidDataException("checkpoint tape reorder is not a permutation");
            seen[source] = true;
        }
        return order;
    }

    private void ApplyCheckpointEvacuations(TapeCheckpointEvacuation[] shed, TapeCheckpointEvacuation[] dropped)
    {
        if (shed.Length == 0 && dropped.Length == 0) return;
        HashSet<long> gone = new();
        foreach (TapeCheckpointEvacuation entry in shed)
        {
            ValidateRoles(entry.Roles);
            int index = PositionOf(entry.ID);
            if (index < 0) throw new InvalidDataException($"checkpoint tape shed references missing resident {entry.ID}");
            if (_eventRoles[index] != entry.Roles) throw new InvalidDataException($"checkpoint tape shed role mismatch for {entry.ID}: resident={_eventRoles[index]}, receipt={entry.Roles}");
            _shed[entry.ID.Value] = new EvacEntry(entry.Length, entry.Offset, entry.Provenance, entry.Source, entry.Roles);
            _shedEventIDs.Add(entry.ID.Value); gone.Add(entry.ID.Value);
        }
        foreach (TapeCheckpointEvacuation entry in dropped)
        {
            ValidateRoles(entry.Roles);
            int index = PositionOf(entry.ID);
            if (index < 0) throw new InvalidDataException($"checkpoint tape drop references missing resident {entry.ID}");
            if (_eventRoles[index] != entry.Roles) throw new InvalidDataException($"checkpoint tape drop role mismatch for {entry.ID}: resident={_eventRoles[index]}, receipt={entry.Roles}");
            byte provenance = _prov[index];
            _tomb[entry.ID.Value] = new EvacEntry(entry.Length, entry.Offset, provenance, entry.Source, entry.Roles);
            ByteLength -= _eventBytes[index].Length + 1;
            if ((entry.Roles & TapeEventRoles.GrammarInput) != 0) GrammarByteLength -= _eventBytes[index].Length + 1;
            ReplayCount--; DroppedCount++;
            _replaysBySource[entry.Source] = _replaysBySource.GetValueOrDefault(entry.Source) - 1;
            gone.Add(entry.ID.Value);
        }
        for (int i = 0, write = 0; i < _eventIDs.Count; i++)
        {
            if (gone.Contains(_eventIDs[i].Value))
            {
                ResidentBytes -= _eventBytes[i].Length + 1; _eventBytesByID.Remove(_eventIDs[i]); continue;
            }
            _eventBytes[write] = _eventBytes[i]; _eventSources[write] = _eventSources[i]; _eventIDs[write] = _eventIDs[i]; _prov[write] = _prov[i]; _eventRoles[write] = _eventRoles[i]; write++;
        }
        int count = _eventIDs.Count - gone.Count;
        _eventBytes.RemoveRange(count, _eventBytes.Count - count); _eventSources.RemoveRange(count, _eventSources.Count - count);
        _eventIDs.RemoveRange(count, _eventIDs.Count - count); _prov.RemoveRange(count, _prov.Count - count); _eventRoles.RemoveRange(count, _eventRoles.Count - count);
        _residentIndexByID.Clear(); for (int i = 0; i < _eventIDs.Count; i++) _residentIndexByID[_eventIDs[i]] = i;
        _shedEventIDs.Sort(); _revision++; _nonAppendRevision++;
    }

    /// Re-arm the append receipt after loading a checkpoint whose Loom splice
    /// mark predates the tape high-water. Mutation receipts are runtime-only;
    /// the durable tape still carries the stable ids, so the pending resident
    /// appends can be reconstructed without changing checkpoint bytes.
    internal void RestorePendingAppends(long firstID)
    {
        if (firstID < 0 || firstID > _nextId)
            throw new ArgumentOutOfRangeException(nameof(firstID), firstID, $"pending append floor must lie in [0,{_nextId}]");
        _appendedSinceDrain.Clear();
        for (long value = firstID; value < _nextId; value++)
        {
            TapeEventID id = new(value);
            if (_residentIndexByID.ContainsKey(id)) _appendedSinceDrain.Add(id);
        }
    }

    /// REFLECT a Replay span — the de-correlation event: a real span exercised a rule this span supports, so its
    /// counts stop being echo and start being evidence. MONOTONIC (the bit is never cleared — corroboration is not
    /// retracted) and IDEMPOTENT (re-reflecting is a no-op). Returns true iff this call was the TRANSITION (the caller
    /// journals transitions, never repeats). Real/Breach spans are born evidence — reflecting them is meaningless, false.
    /// Only RESIDENT spans reflect: shed spans are evidence already (the shed gate), dropped ones are gone — an unknown
    /// id here is a corroboration-plane bug, fail loud.
    public bool Reflect(TapeEventID id)
    {
        if (!_residentIndexByID.TryGetValue(id, out int i)) throw new ArgumentException($"Reflect: unknown span {id} — not resident on this tape", nameof(id));
        byte p = _prov[i];
        if ((Provenances)(p & ProvMask) != Provenances.Replay || (p & ReflectedBit) != 0) return false;
        _prov[i] = (byte)(p | ReflectedBit);
        ReflectedReplayCount++;
        _reflectedBySource[_eventSources[i]] = _reflectedBySource.GetValueOrDefault(_eventSources[i]) + 1;   // WHICH JEWEL got corroborated — the by-source reflect numerator (the decisive multi-node read)
        _revision++;
        _nonAppendRevision++;
        _reflectedSinceDrain.Add(id);
        _checkpointReflected.Add(id);
        return true;
    }

    public Provenances ProvenanceOf(TapeEventID id) => (Provenances)(ProvByteOf(id) & ProvMask);
    public bool IsReflected(TapeEventID id) => (ProvByteOf(id) & ReflectedBit) != 0;

    public TapeEventRoles RolesOf(TapeEventID id)
    {
        if (_residentIndexByID.TryGetValue(id, out int resident)) return _eventRoles[resident];
        if (_shed.TryGetValue(id.Value, out EvacEntry shed)) return shed.Roles;
        if (_tomb.TryGetValue(id.Value, out EvacEntry tomb)) return tomb.Roles;
        throw new ArgumentException($"unknown span {id} — never on this tape", nameof(id));
    }

    public bool HasRole(TapeEventID id, TapeEventRoles role) => (RolesOf(id) & role) == role;

    /// Does this span COUNT as evidence under the reflection law? Real, Breach, and Reflected are born evidence;
    /// a Replay is evidence only once reflected. Resolves residents, shed, and dropped spans alike (the weight fork
    /// WeightsFor/Audit/Gc-promotion read, plus the loom's re-splice over the view).
    public bool IsEvidence(TapeEventID id) => IsEvidenceByte(ProvByteOf(id));

    /// The positional read (current tape order) — the hot-loop face of IsEvidence over residents.
    public bool IsEvidenceAt(int idx) => IsEvidenceByte(_prov[idx]);

    private static bool IsEvidenceByte(byte p)
    {
        Provenances provenance = (Provenances)(p & ProvMask);
        return provenance is Provenances.Real or Provenances.Breach or Provenances.Reflected
            || provenance == Provenances.Replay && (p & ReflectedBit) != 0;
    }

    private byte ProvByteOf(TapeEventID id)
    {
        if (_residentIndexByID.TryGetValue(id, out int i)) return _prov[i];
        if (_shed.TryGetValue(id.Value, out var s)) return s.Prov;
        if (_tomb.TryGetValue(id.Value, out var t)) return t.Prov;
        throw new ArgumentException($"unknown span {id} — never on this tape");
    }

    /// THE COUNT-MEASURE WEIGHTS — one weight per Concat() byte position, in VIEW order: each unit contributes
    /// Length+1 entries (its bytes + the '\n' separator) of IsEvidence ? wScale : 1. Constant WITHIN each span by
    /// construction — the contract RePair.Induce's one-fetch-per-merge-site optimization rests on (the barrier law
    /// guarantees a merge neighborhood never crosses a span). The buffer is RENTED from ArrayPool&lt;byte&gt;.Shared
    /// (length ≥ ByteLength; entries past ByteLength are garbage) — the caller MUST Return it. wScale must be a
    /// power of two in 1..128 (the Vow: uniform-evidence tapes divide out EXACTLY — see Mdl.PairDelta).
    public byte[] WeightsFor(int wScale)
    {
        RequireWScale(wScale);
        if (ByteLength > int.MaxValue) throw new InvalidOperationException($"WeightsFor: view is {ByteLength}B — past the int-indexed induction ceiling");
        var w = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Max(1, ByteLength));
        int at = 0;
        foreach (var u in GetEventViews())
        {
            w.AsSpan(at, u.Len + 1).Fill(u.Evidence ? (byte)wScale : (byte)1);
            at += u.Len + 1;
        }
        return w;
    }

    /// Weighted bytes aligned with the GrammarInput-only induction view.  The
    /// audit-only view may interleave measurement packets; this walk must stay
    /// paired with Engine's GetGrammarEventViews order or weights shift onto
    /// the wrong symbols.
    internal byte[] GrammarWeightsFor(int wScale)
    {
        RequireWScale(wScale);
        if (GrammarByteLength > int.MaxValue) throw new InvalidOperationException($"GrammarWeightsFor: view is {GrammarByteLength}B — past the int-indexed induction ceiling");
        byte[] w = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Max(1, GrammarByteLength));
        int at = 0;
        foreach (TapeEventView view in GetGrammarEventViews())
        {
            w.AsSpan(at, view.Len + 1).Fill(view.Evidence ? (byte)wScale : (byte)1);
            at += view.Len + 1;
        }
        return w;
    }

    /// The power-of-two law, enforced at every weighted entry point: wScale ∈ {1,2,4,…,128} so a uniform-evidence
    /// tape's weighted arithmetic divides back to the unweighted arithmetic EXACTLY (never a truncation drift).
    internal static void RequireWScale(int wScale)
    {
        if (wScale < 1 || wScale > 128 || (wScale & (wScale - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(wScale), wScale, "wScale must be a power of two in 1..128");
    }

    /// THE VIEW — the canonical whole-corpus walk: residents in CURRENT tape order, then shed events in id order.
    /// Concat / WeightsFor / the loom harvest / Pearl.Audit all pace this ONE enumeration, so the corpus every
    /// whole-world reader sees is the same corpus regardless of how much of it has physically left RAM.
    public IEnumerable<TapeEventView> GetEventViews()
    {
        for (int i = 0; i < _eventBytes.Count; i++)
        {
            Provenances provenance = (Provenances)(_prov[i] & ProvMask);
            yield return new TapeEventView(_eventIDs[i], _eventBytes[i].Length, provenance, IsEvidenceByte(_prov[i]), _eventSources[i], _eventRoles[i]);
        }
        foreach (long v in _shedEventIDs)
        {
            EvacEntry entry = _shed[v];
            Provenances provenance = (Provenances)(entry.Prov & ProvMask);
            yield return new TapeEventView(new TapeEventID(v), entry.Len, provenance, IsEvidenceByte(entry.Prov), entry.Source, entry.Roles);
        }
    }

    public IEnumerable<TapeEventView> GetEventViews(TapeEventRoles requiredRoles)
    {
        foreach (TapeEventView view in GetEventViews())
            if (view.HasRole(requiredRoles)) yield return view;
    }

    public IEnumerable<TapeEventView> GetGrammarEventViews() => GetEventViews(TapeEventRoles.GrammarInput);

    /// Resolve one event's current view metadata by its stable id without walking
    /// the resident+shed view.  AuditOnly validators use this identity index for
    /// append/bind transitions; whole-view enumeration remains the certification
    /// path for load and terminal checks.
    public bool TryGetEventView(TapeEventID id, out TapeEventView view)
    {
        if (_residentIndexByID.TryGetValue(id, out int resident))
        {
            byte provenance = _prov[resident];
            view = new TapeEventView(id, _eventBytes[resident].Length,
                (Provenances)(provenance & ProvMask), IsEvidenceByte(provenance), _eventSources[resident], _eventRoles[resident]);
            return true;
        }
        if (_shed.TryGetValue(id.Value, out EvacEntry shed))
        {
            view = new TapeEventView(id, shed.Len, (Provenances)(shed.Prov & ProvMask),
                IsEvidenceByte(shed.Prov), shed.Source, shed.Roles);
            return true;
        }
        view = default;
        return false;
    }

    /// Enumerate view events appended at or after the supplied id mark. The append ledger is monotonic, so the
    /// monotonic id range makes this O(Δ log Δ) in the number of post-mark ids instead of walking every resident
    /// and shed event to reject the already-seen prefix. The small suffix is then ordered by its current view position,
    /// which preserves GetEventViews' resident-reorder semantics exactly. A required role narrows the view without
    /// changing the audit-only cursor, allowing grammar consumers to skip measurement-only packets while still
    /// advancing over their IDs.
    public IEnumerable<TapeEventView> EnumerateAppendedSince(long mark, TapeEventRoles requiredRoles = TapeEventRoles.None)
    {
        if (mark < 0 || mark > _nextId)
            throw new ArgumentOutOfRangeException(nameof(mark), mark, $"append mark must lie in [0,{_nextId}]");

        var ids = new List<TapeEventID>();
        for (long value = mark; value < _nextId; value++)
        {
            TapeEventID id = new(value);
            if (_residentIndexByID.ContainsKey(id) || _shed.ContainsKey(id.Value)) ids.Add(id);
        }
        ids.Sort((a, b) => CurrentViewPosition(a).CompareTo(CurrentViewPosition(b)));
        foreach (TapeEventID id in ids)
        {
            if (_residentIndexByID.TryGetValue(id, out int resident))
            {
                byte p = _prov[resident];
                TapeEventView view = new(id, _eventBytes[resident].Length, (Provenances)(p & ProvMask), IsEvidenceByte(p), _eventSources[resident], _eventRoles[resident]);
                if (view.HasRole(requiredRoles)) yield return view;
            }
            else if (_shed.TryGetValue(id.Value, out EvacEntry entry))
            {
                TapeEventView view = new(id, entry.Len, (Provenances)(entry.Prov & ProvMask), IsEvidenceByte(entry.Prov), entry.Source, entry.Roles);
                if (view.HasRole(requiredRoles)) yield return view;
            }
        }
    }

    private int CurrentViewPosition(TapeEventID id)
        => _residentIndexByID.TryGetValue(id, out int resident)
            ? resident
            : _eventBytes.Count + _shedEventIDs.BinarySearch(id.Value);

    /// Read event bytes back by stable id — the RESOLVE path: resident (RAM), shed, or dropped (the event byte log).
    /// The source of record never forgets — a once-appended id always resolves; only a never-appended id returns false.
    public bool Resolve(TapeEventID id, out byte[] eventBytes)
    {
        if (_eventBytesByID.TryGetValue(id, out eventBytes!)) return true;
        if (_shed.TryGetValue(id.Value, out var s)) { eventBytes = ReadLog(s); return true; }
        if (_tomb.TryGetValue(id.Value, out var t)) { eventBytes = ReadLog(t); return true; }
        eventBytes = null!;
        return false;
    }

    private byte[] ReadLog(in EvacEntry e)
    {
        if (_log is null) throw new InvalidOperationException("Tape: event byte log not mounted — an evacuated event cannot resolve");
        var buf = new byte[e.Len];
        _log.Seek(e.Off, SeekOrigin.Begin);
        _log.ReadExactly(buf);
        return buf;
    }

    /// An event's CURRENT tape position by stable id (−1 if not resident) — the night shift's id→order bridge and the
    /// shed test every positional consumer guards on (a shed/dropped event has no position). O(1), refreshed by Reorder.
    public int PositionOf(TapeEventID id) => _residentIndexByID.TryGetValue(id, out var i) ? i : -1;

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  EVACUATION (phase 3) — the night verb: SHED learned evidence to the log, DROP stale unreflected replays
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Evacuate spans from the resident set, ONE compaction pass (O(residents), paid at the sleep cadence).
    ///   SHED  — evidence or neutral execution spans whose structure the grammar carries (the caller's parsed==1
    ///           criterion): bytes move
    ///           to the event byte log, the id joins the view's shed tail; counts, weights, and the harvest see the SAME
    ///           corpus — only the RAM moved. Requires IsEvidence (an unreflected hypothesis must never be laundered
    ///           into the permanent record as if learned).
    ///   DROP  — stale UNREFLECTED Replay spans: bytes go to the log (forensics — chains and index reloads still
    ///           resolve) but the id leaves the VIEW: its counts vanish at the next re-greed, ByteLength shrinks,
    ///           and its ReplayCount slot frees mint headroom.
    /// Ids must be resident; the caller passes deterministic sets (id-ascending). Returns (shed, dropped) counts.
    public TapeEvacuation Evacuate(IReadOnlyList<TapeEventID> shed, IReadOnlyList<TapeEventID> drop)
    {
        if (shed.Count == 0 && drop.Count == 0)
            return new TapeEvacuation(new TapeRevision(_revision), [], []);
        if (_log is null) throw new InvalidOperationException("Tape.Evacuate: no event byte log mounted (MountLog first) — shedding needs a durable byte home");

        var gone = new HashSet<TapeEventID>();
        foreach (var id in shed)
        {
            int i = _residentIndexByID.TryGetValue(id, out var ii) ? ii : throw new ArgumentException($"Evacuate: {id} not resident", nameof(shed));
            Provenances provenance = (Provenances)(_prov[i] & ProvMask);
            if (!IsEvidenceAt(i) && provenance != Provenances.Execution)
                throw new ArgumentException($"Evacuate: {id} is an unreflected hypothesis — only evidence and execution history shed", nameof(shed));
            _shed[id.Value] = WriteLog(_eventBytes[i], _prov[i], _eventSources[i], _eventRoles[i]);
            _shedEventIDs.Add(id.Value);
            gone.Add(id);
        }
        foreach (var id in drop)
        {
            int i = _residentIndexByID.TryGetValue(id, out var ii) ? ii : throw new ArgumentException($"Evacuate: {id} not resident", nameof(drop));
            byte p = _prov[i];
            if ((Provenances)(p & ProvMask) != Provenances.Replay || (p & ReflectedBit) != 0)
                throw new ArgumentException($"Evacuate: {id} is evidence — evidence sheds, never drops", nameof(drop));
            _tomb[id.Value] = WriteLog(_eventBytes[i], p, _eventSources[i], _eventRoles[i]);
            ByteLength -= _eventBytes[i].Length + 1;          // leaves the view
            if ((_eventRoles[i] & TapeEventRoles.GrammarInput) != 0) GrammarByteLength -= _eventBytes[i].Length + 1;
            ReplayCount--;                                // the hypothesis slot frees
            _replaysBySource[_eventSources[i]] = _replaysBySource.GetValueOrDefault(_eventSources[i]) - 1;   // the by-source denominator frees with it (an unreflected drop is never in _reflectedBySource — the IsEvidence guard above forbids dropping a reflected span)
            DroppedCount++;
            gone.Add(id);
        }
        _log.Flush();
        _shedEventIDs.Sort();                                 // the view's tail is id-ascending across nights

        // one compaction pass over the four parallel resident lists, preserving current (defragged) order
        int w = 0;
        for (int i = 0; i < _eventBytes.Count; i++)
        {
            if (gone.Contains(_eventIDs[i]))
            {
                ResidentBytes -= _eventBytes[i].Length + 1;
                _eventBytesByID.Remove(_eventIDs[i]);
                continue;
            }
            _eventBytes[w] = _eventBytes[i]; _eventSources[w] = _eventSources[i]; _eventIDs[w] = _eventIDs[i]; _prov[w] = _prov[i]; _eventRoles[w] = _eventRoles[i];
            w++;
        }
        _eventBytes.RemoveRange(w, _eventBytes.Count - w);
        _eventSources.RemoveRange(w, _eventSources.Count - w);
        _eventIDs.RemoveRange(w, _eventIDs.Count - w);
        _prov.RemoveRange(w, _prov.Count - w);
        _eventRoles.RemoveRange(w, _eventRoles.Count - w);
        _residentIndexByID.Clear();
        for (int i = 0; i < _eventIDs.Count; i++) _residentIndexByID[_eventIDs[i]] = i;
        _revision++; _nonAppendRevision++;
        var shedIDs = shed.ToArray();
        var droppedIDs = drop.ToArray();
        _shedSinceDrain.AddRange(shedIDs);
        _droppedSinceDrain.AddRange(droppedIDs);
        _checkpointShed.AddRange(shedIDs);
        _checkpointDropped.AddRange(droppedIDs);
        return new TapeEvacuation(new TapeRevision(_revision), shedIDs, droppedIDs);
    }

    private EvacEntry WriteLog(byte[] span, byte prov, string source, TapeEventRoles roles)
    {
        long off = _logEnd;
        _log!.Seek(off, SeekOrigin.Begin);
        _log.Write(span);
        _logEnd = off + span.Length;
        return new EvacEntry(span.Length, off, prov, source, roles);
    }

    /// The evacuated-span source label (index-reload feed parity: a slot fed "node0" live must re-feed "node0").
    internal string EvacSourceOf(TapeEventID id)
        => _shed.TryGetValue(id.Value, out var s) ? s.Source
         : _tomb.TryGetValue(id.Value, out var t) ? t.Source
         : throw new ArgumentException($"EvacSourceOf: {id} is not evacuated");

    /// The event's provenance label by stable id — resident, shed, or dropped alike. The JEWEL-IDENTITY read the
    /// cross-reflection corroboration compares a Replay supporter's own source against the rule's jewel-source set:
    /// a Replay reflects only on a DIFFERENT source, so Corroborate must know the supporter's own label to reject a
    /// same-source (self / clone) jewel. Unknown ids fail loud — a corroboration-plane bug, never silent.
    public string SourceOf(TapeEventID id)
    {
        if (_residentIndexByID.TryGetValue(id, out int i)) return _eventSources[i];
        if (_shed.TryGetValue(id.Value, out var s)) return s.Source;
        if (_tomb.TryGetValue(id.Value, out var t)) return t.Source;
        throw new ArgumentException($"SourceOf: unknown span {id} — never on this tape", nameof(id));
    }

    /// MemStat census read — the span-log axis: evacuated record count + Σ log bytes on disk.
    internal (int Records, long Bytes) LogMass() => (_shed.Count + _tomb.Count, _logEnd);

    /// The DEMOTE anchor — is there a tape event whose bytes EXACTLY equal `bytes`? Returns the first (lowest-id,
    /// deterministic) match. GC-demotion uses this: a literal rule whose whole expansion IS a tape event (a full-line
    /// MEMORIZATION) can be evicted from the working set and replaced by a reference to the source-of-record event that
    /// already holds the bytes — resident or evacuated alike (the log resolves either). Bucketed by a fast content
    /// hash, verified byte-exact through Resolve so a hash collision cannot mis-resolve — the resolve is lossless.
    public bool FindByContent(byte[] bytes, out TapeEventID id)
    {
        if (_eventIDsByContent.TryGetValue(ContentHash(bytes), out var bucket))
            foreach (var sid in bucket)
                if (Resolve(sid, out var s) && s.AsSpan().SequenceEqual(bytes)) { id = sid; return true; }
        id = default;
        return false;
    }

    /// The multi-event demote matcher — find the ordered TapeEventSeg chain whose resolution reproduces `expansion`
    /// BYTE-EXACTLY, or return false. A grammar rule's expansion is always a contiguous substring of the RESIDENT
    /// prefix of Concat() (rules are induced over the view, but a multi-line run only demotes onto ADJACENT resident
    /// events — evacuated events have no position, so a chain crossing one simply stays resident: the honest residual).
    /// The walk is deterministic (lowest-id anchor candidate first) and the returned chain is self-verified
    /// byte-exact (the Vow: the caller re-proves via ResolveChain, Resolved == Demoted). `chain` is caller-owned
    /// scratch, cleared on entry.
    public bool FindCover(byte[] expansion, List<TapeEventSeg> chain)
    {
        chain.Clear();
        int nl0 = Array.IndexOf(expansion, (byte)'\n');
        if (nl0 < 0)   // within a single event ⟹ whole-event identity anchors the walk at offset 0
        {
            if (!FindByContent(expansion, out var only)) return false;
            if (PositionOf(only) >= 0) return TryCoverFrom(_residentIndexByID[only], 0, expansion, chain);
            chain.Add(new TapeEventSeg(only, 0, expansion.Length));   // evacuated whole-event identity — a 1-seg ref, no walk needed
            return true;
        }

        // ≥2 lines. Anchor on a line-part guaranteed to be a WHOLE event, take its candidate tape positions, place the
        // implied start event (the first part, length nl0, is a SUFFIX of eventBytes[base]), then walk-verify the whole run.
        // An INTERIOR part (between two '\n') is whole for sure; with a single '\n' either end-line may be — try both.
        int nl1 = Array.IndexOf(expansion, (byte)'\n', nl0 + 1);
        Span<(int Start, int Len, int PartIdx)> anchors = stackalloc (int, int, int)[2];
        int na = 0;
        if (nl1 >= 0) anchors[na++] = (nl0 + 1, nl1 - nl0 - 1, 1);
        else { anchors[na++] = (nl0 + 1, expansion.Length - nl0 - 1, 1); anchors[na++] = (0, nl0, 0); }

        for (int ai = 0; ai < na; ai++)
        {
            var (aStart, aLen, aPart) = anchors[ai];
            if (!_eventIDsByContent.TryGetValue(ContentHash(expansion.AsSpan(aStart, aLen)), out var bucket)) continue;
            foreach (var sid in bucket)
            {
                if (!_residentIndexByID.TryGetValue(sid, out int anchorIdx)) continue;                       // multi-event chains walk RESIDENT adjacency only
                if (!_eventBytesByID[sid].AsSpan().SequenceEqual(expansion.AsSpan(aStart, aLen))) continue;  // exact anchor event (guard the FNV bucket)
                int baseIdx = anchorIdx - aPart;                           // the event holding the expansion's first byte
                if (baseIdx < 0) continue;
                int o0 = _eventBytes[baseIdx].Length - nl0;                     // first part (length nl0) is a suffix of eventBytes[base]
                if (o0 < 0) continue;
                if (TryCoverFrom(baseIdx, o0, expansion, chain)) return true;
            }
        }
        return false;
    }

    // Walk events forward from (index baseIdx, offset o0 over the event's Concat unit), emitting TapeEventSegs and comparing
    // to `E` byte-for-byte over each unit (event bytes, then the '\n' at index eventBytes.Length). Fills `chain` + returns
    // true iff the run reproduces E exactly; a mismatch or running off the tape clears `chain` and returns false.
    private bool TryCoverFrom(int baseIdx, int o0, byte[] E, List<TapeEventSeg> chain)
    {
        chain.Clear();
        int produced = 0, idx = baseIdx, off = o0;
        while (produced < E.Length)
        {
            if (idx >= _eventBytes.Count) { chain.Clear(); return false; }
            var span = _eventBytes[idx];
            int unitLen = span.Length + 1;                             // + the '\n' Concat appends after this event
            if (off >= unitLen) { chain.Clear(); return false; }
            int take = Math.Min(unitLen - off, E.Length - produced);
            for (int k = 0; k < take; k++)
            {
                int ui = off + k;
                byte ub = ui < span.Length ? span[ui] : (byte)'\n';
                if (ub != E[produced + k]) { chain.Clear(); return false; }
            }
            chain.Add(new TapeEventSeg(_eventIDs[idx], off, take));
            produced += take;
            idx++; off = 0;                                            // subsequent events start at their unit head
        }
        return true;
    }

    /// Resolve ONE TapeEventSeg into `outp` — append event bytes, plus the '\n' separator when the
    /// range reaches the Concat unit's trailing slot (Start+Len == eventBytes.Length+1). False iff the id is unknown (never
    /// on a well-formed chain — the tape+log never forget). The inverse of FindCover's unit walk.
    public bool ResolveSeg(TapeEventSeg seg, List<byte> outp)
    {
        if (!Resolve(seg.Id, out var span)) return false;
        int bodyEnd = Math.Min(seg.Start + seg.Len, span.Length);
        for (int i = seg.Start; i < bodyEnd; i++) outp.Add(span[i]);
        if (seg.Start + seg.Len == span.Length + 1) outp.Add((byte)'\n');
        return true;
    }

    /// Resolve a demoted body's whole chain into `outp`, ALL-OR-NOTHING (a failed link rewinds the appends so a
    /// tape-unaware fallback stays byte-clean). Demote-don't-delete: the bytes provably live on the tape or its log.
    public bool TryResolveChain(TapeEventSeg[] segs, List<byte> outp)
    {
        int mark = outp.Count;
        foreach (var s in segs) if (!ResolveSeg(s, outp)) { outp.RemoveRange(mark, outp.Count - mark); return false; }
        return true;
    }

    /// Resolve a chain to a fresh byte[] (the resolution PROOF path — MemoryHierarchy.Gc compares this to the rule's
    /// expansion; Resolved == Demoted is the Vow). Null iff a link is unresolvable.
    public byte[]? ResolveChain(TapeEventSeg[] segs)
    {
        var outp = new List<byte>();
        return TryResolveChain(segs, outp) ? outp.ToArray() : null;
    }

    /// The COMPARE-ONLY resolution — resolve the chain into a caller-owned `scratch` (cleared on entry) and test it
    /// byte-exact against `expect`, allocating no `byte[]` (the Vow-check without the throwaway array). The Gc proof
    /// loops run this once per demoted rule per night, reusing ONE scratch list across all rules — the per-rule
    /// List+array pair the naive ResolveChain(…).SequenceEqual would mint per rule.
    public bool ChainResolvesTo(TapeEventSeg[] segs, ReadOnlySpan<byte> expect, List<byte> scratch)
    {
        scratch.Clear();
        if (!TryResolveChain(segs, scratch)) return false;
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(scratch).SequenceEqual(expect);
    }

    /// FNV-1a/32 over bytes — the content bucket key (verified byte-exact on lookup, so collisions only cost a scan,
    /// never a wrong resolve). Deterministic, alloc-free — the Vow holds on the demote lookup. `internal` so the
    /// MemoryHierarchy keys its persistent demotion set by the SAME content hash.
    internal static int ContentHash(byte[] b) => ContentHash(b.AsSpan());

    /// The span form — FindCover keys the anchor bucket by a SUB-RANGE of a rule expansion (a line-part) without
    /// slicing it into a fresh array, so the same FNV that indexed the whole-span buckets matches on the sub-range.
    internal static int ContentHash(ReadOnlySpan<byte> b)
    {
        uint h = 2166136261u;
        foreach (var x in b) { h ^= x; h *= 16777619u; }
        return (int)h;
    }

    /// MemStat census read — the derived content-index's mass (bucket count + Σ bucket slots). Counts only.
    internal (int Buckets, long Slots) ContentIndexMass()
    {
        long slots = 0;
        foreach (var b in _eventIDsByContent.Values) slots += b.Count;
        return (_eventIDsByContent.Count, slots);
    }

    /// The induction input — THE VIEW, newline-joined (what Engine.Induce runs over; shed spans stream back from
    /// the event byte log). Allocates a fresh byte[] per call; whole-view readers pay this at the sleep/LAND cadence only.
    public byte[] Concat()
    {
        if (ByteLength > int.MaxValue) throw new InvalidOperationException($"Concat: view is {ByteLength}B — past the int-indexed ceiling");
        var buf = new byte[ByteLength];
        int at = 0;
        foreach (var s in _eventBytes) { s.CopyTo(buf, at); at += s.Length; buf[at++] = (byte)'\n'; }
        foreach (long v in _shedEventIDs)
        {
            var e = _shed[v];
            var b = ReadLog(e);
            b.CopyTo(buf, at); at += b.Length; buf[at++] = (byte)'\n';
        }
        return buf;
    }

    /// CHECKPOINT — the source of record, serialized whole: residents (id, source, bytes, provenance) in CURRENT tape
    /// order (so a resumed induce sees the exact post-defrag sequence), then the SHED and TOMB entry tables (id,
    /// source, provenance, log address — the BYTES stay in the event byte log; "the shed events resolve from the log on
    /// Load"), then the monotonic id counter. The provenance byte carries the ReflectedBit with it — corroboration
    /// survives the checkpoint. Composed indexes (_byId/_idToIdx/_byContent) and the counters rebuild on Load.
    public void Save(CkptWriter w)
    {
        w.I32(FullSchemaMarker);
        w.U8(FullSchemaVersion);
        w.I32(_eventBytes.Count);
        for (int i = 0; i < _eventBytes.Count; i++)
        {
            w.I64(_eventIDs[i].Value);
            w.Str(_eventSources[i]);
            w.Bytes(_eventBytes[i]);
            w.U8(_prov[i]);
            w.U8((byte)_eventRoles[i]);
        }
        WriteEvac(w, _shed);
        WriteEvac(w, _tomb);
        w.I64(_nextId);
    }

    private static void WriteEvac(CkptWriter w, Dictionary<long, EvacEntry> d)
    {
        w.I32(d.Count);
        foreach (var k in d.Keys.Order())                // key-sorted — Save∘Load∘Save = identity
        {
            var e = d[k];
            w.I64(k); w.Str(e.Source); w.U8(e.Prov); w.I32(e.Len); w.I64(e.Off); w.U8((byte)e.Roles);
        }
    }

    /// Restore into a FRESH tape (event byte log mounted first when evacuated entries exist). The content buckets are
    /// rebuilt in ID-ASCENDING order over EVERY id ever appended — that is APPEND order, which Reorder never
    /// disturbs, and FindByContent/FindCover's deterministic lowest-id-first contract rides on exactly that bucket
    /// order (evacuated ids re-verify through the log). Rebuilding in tape order would silently flip which exemplar
    /// a demotion anchors on after a defrag — a byte-identity break that never throws.
    public void Load(CkptReader r)
    {
        if (_eventBytes.Count != 0 || _shed.Count != 0 || _tomb.Count != 0) throw new InvalidOperationException("Tape.Load requires a fresh tape");
        int header = r.I32();
        bool hasRoles = header == FullSchemaMarker;
        int n;
        if (hasRoles)
        {
            byte version = r.U8();
            if (version != FullSchemaVersion) throw new InvalidDataException($"unknown tape full checkpoint schema {version}");
            n = r.I32();
        }
        else
        {
            // CORTEXZ and earlier images encoded the resident count directly.
            // Those spans were all grammar input by definition.
            n = header;
        }
        if (n < 0) throw new InvalidDataException("checkpoint tape resident count is negative");
        for (int i = 0; i < n; i++)
        {
            var id = new TapeEventID(r.I64());
            var src = r.Str();
            var span = r.Bytes();
            byte prov = r.U8();
            TapeEventRoles roles = hasRoles ? ReadRoles(r) : TapeEventRoles.GrammarInput;
            _eventBytes.Add(span); _eventSources.Add(src); _eventIDs.Add(id); _prov.Add(prov); _eventRoles.Add(roles);
            CountProv(prov, src);
            _eventBytesByID[id] = span;
            _residentIndexByID[id] = i;
            ByteLength += span.Length + 1;
            if ((roles & TapeEventRoles.GrammarInput) != 0) GrammarByteLength += span.Length + 1;
            ResidentBytes += span.Length + 1;
        }
        int ns = r.I32();
        for (int i = 0; i < ns; i++)
        {
            long id = r.I64(); var src = r.Str(); byte prov = r.U8(); int len = r.I32(); long off = r.I64();
            _shed[id] = new EvacEntry(len, off, prov, src, hasRoles ? ReadRoles(r) : TapeEventRoles.GrammarInput);
            _shedEventIDs.Add(id);
            CountProv(prov, src);
            ByteLength += len + 1;
            if ((_shed[id].Roles & TapeEventRoles.GrammarInput) != 0) GrammarByteLength += len + 1;
        }
        _shedEventIDs.Sort();
        int nt = r.I32();
        for (int i = 0; i < nt; i++)
        {
            long id = r.I64(); var src = r.Str(); byte prov = r.U8(); int len = r.I32(); long off = r.I64();
            _tomb[id] = new EvacEntry(len, off, prov, src, hasRoles ? ReadRoles(r) : TapeEventRoles.GrammarInput);
            DroppedCount++;
        }
        _nextId = r.I64();
        _revision = 0;
        _orderRevision = 0;
        _reportedOrderRevision = 0;
        _appendedSinceDrain.Clear();
        _reflectedSinceDrain.Clear();
        _shedSinceDrain.Clear();
        _droppedSinceDrain.Clear();
        _checkpointBaseResidentIDs = _eventIDs.ToArray();
        if (_shed.Count > 0 || _tomb.Count > 0)
        {
            if (_log is null)
                throw new InvalidOperationException("Tape.Load: checkpoint carries evacuated events but no event byte log is mounted — MountLog(<run>/tape.spanlog) first");
            long need = 0;
            foreach (var e in _shed.Values) need = Math.Max(need, e.Off + e.Len);
            foreach (var e in _tomb.Values) need = Math.Max(need, e.Off + e.Len);
            if (_log.Length < need)
                throw new InvalidDataException($"Tape.Load: event byte log holds {_log.Length}B but the checkpoint's evacuated entries address {need}B — tape.spanlog was truncated or replaced; the run cannot resume");
        }

        // content buckets: EVERY id ever, ascending (append order) — residents from RAM, evacuated via the log.
        var all = new List<(long Id, byte[] Bytes)>(n + ns + nt);
        for (int i = 0; i < n; i++) all.Add((_eventIDs[i].Value, _eventBytes[i]));
        foreach (var (k, e) in _shed) all.Add((k, ReadLog(e)));
        foreach (var (k, e) in _tomb) all.Add((k, ReadLog(e)));
        all.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var (id, bytes) in all)
        {
            int h = ContentHash(bytes);
            (_eventIDsByContent.TryGetValue(h, out var bucket) ? bucket : _eventIDsByContent[h] = new()).Add(new TapeEventID(id));
        }
    }

    // The derived-census rebuild (Load) — mirror EVERY aggregate counter AND its by-source split off the restored
    // event's provenance byte + source label, so the reconstructed tape's census is byte-identical to the live one
    // (the resume-exactness contract: these counters never serialize, they re-derive here).
    private void CountProv(byte prov, string source)
    {
        var p = (Provenances)(prov & ProvMask);
        if (p == Provenances.Real) RealCount++;
        else if (p == Provenances.Replay)
        {
            ReplayCount++;
            _replaysBySource[source] = _replaysBySource.GetValueOrDefault(source) + 1;
            if ((prov & ReflectedBit) != 0) { ReflectedReplayCount++; _reflectedBySource[source] = _reflectedBySource.GetValueOrDefault(source) + 1; }
        }
        else if (p == Provenances.Breach) BreachCount++;
        else if (p == Provenances.Reflected) ReflectedCount++;
        else if (p == Provenances.Execution) ExecutionCount++;
    }

    /// SLEEP re-serialization — permute the RESIDENT span order (couplings-guided defrag; `order` is a permutation
    /// of 0..Count-1). Byte content + stable ids are preserved (ids move WITH their spans); only the SEQUENCE changes,
    /// recovering cross-line template adjacency a globally-mixed intake destroyed (Seriate.Seriate's output). Shed
    /// spans have no order (the view's tail is id-fixed), so defrag and shedding never fight.
    public void Reorder(int[] order)
    {
        if (order.Length != _eventBytes.Count) throw new ArgumentException($"Reorder expects a permutation of {_eventBytes.Count}, got {order.Length}", nameof(order));
        bool changed = false;
        for (int i = 0; i < order.Length; i++)
            if (order[i] != i) { changed = true; break; }

        TapeEventID[] beforeIDs = changed ? _eventIDs.ToArray() : [];
        TapeEventID[] targetIDs = changed ? new TapeEventID[order.Length] : [];
        if (changed)
            for (int i = 0; i < order.Length; i++) targetIDs[i] = beforeIDs[order[i]];

        var s = new byte[_eventBytes.Count][];
        var src = new string[_eventBytes.Count];
        var id = new TapeEventID[_eventBytes.Count];
        var pv = new byte[_eventBytes.Count];
        var roles = new TapeEventRoles[_eventBytes.Count];
        for (int i = 0; i < order.Length; i++) { s[i] = _eventBytes[order[i]]; src[i] = _eventSources[order[i]]; id[i] = _eventIDs[order[i]]; pv[i] = _prov[order[i]]; roles[i] = _eventRoles[order[i]]; }
        _eventBytes.Clear(); _eventBytes.AddRange(s);
        _eventSources.Clear(); _eventSources.AddRange(src);
        _eventIDs.Clear(); _eventIDs.AddRange(id);
        _prov.Clear(); _prov.AddRange(pv);
        _eventRoles.Clear(); _eventRoles.AddRange(roles);
        _residentIndexByID.Clear();
        for (int i = 0; i < _eventIDs.Count; i++) _residentIndexByID[_eventIDs[i]] = i;   // ids moved with their spans → refresh id→position for FindCover
        if (changed)
        {
            _revision++;
            _nonAppendRevision++;
            _orderRevision = _revision;
            _checkpointReordered = true;
        }
    }
}
