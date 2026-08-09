namespace Cogito;

using System.Runtime.InteropServices;
using System.Text;
using Cogito.Exec;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE RING ──  the Cortex owns the topology. Main is the shared world tape; each neuron owns one intrinsic
// tape. A neuron reads the deterministic stimulus splice `[main, upstream.Intrinsic]` and emits only to its own
// intrinsic tape, so circulation is a real edge relation instead of every node secretly seeing every node through a
// flooded shared bus.

public readonly record struct NeuronID(int Value)
{
    public override string ToString() => $"node{Value}";
}

public readonly record struct NeuronRead(RePairResult Grammar, int ViewBytes, int ViewSpans);

public readonly record struct NeuronWrite(int Minted, int IntrinsicSpans, long IntrinsicBytes);

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE NEURON — one mind: a grammar over routed stimuli, emitting intrinsic claims to its own tape
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

public sealed class Neuron : IDisposable
{
    private readonly IGenerator _generator;
    private readonly Tape _stimuli = new();
    private readonly Dictionary<Tape, long> _stimulusMarks = new();
    private readonly Loom _loom;
    private Engine.GrammarCover? _cover;
    private GrammarRule[]? _coverRules;

    public readonly NeuronID ID;
    public string Name => ID.ToString();
    public Tape Intrinsic { get; } = new();
    public IGenerator Generator => _generator;
    public Metabolism Metabolism { get; }
    public RePairResult Grammar { get; private set; }

    public Neuron(NeuronID id, IGenerator generator, double lambda = 0.3)
    {
        ID = id;
        _generator = generator;
        Metabolism = new(lambda);
        _loom = new Loom(256, (uint)'\n', wScale: 1);
    }

    public Loom Loom => _loom;

    public Engine.GrammarCover Cover
    {
        get
        {
            if (!ReferenceEquals(_coverRules, Grammar.Rules))
            {
                _coverRules = Grammar.Rules;
                _cover = new Engine.GrammarCover(_coverRules);
            }
            return _cover!;
        }
    }

    public Tape Stimuli => _stimuli;

    public NeuronRead InduceFrom(List<Tape> stimuli)
    {
        AppendStimuli(stimuli);
        _loom.SpliceNew(_stimuli);
        _loom.Pump();
        Grammar = _loom.Result(_stimuli);
        return new NeuronRead(Grammar, (int)_stimuli.ByteLength, _stimuli.Count + _stimuli.ShedEventIDs.Count);
    }

    public void MarkStimuliSeen(List<Tape> stimuli)
    {
        for (int i = 0; i < stimuli.Count; i++)
            _stimulusMarks[stimuli[i]] = stimuli[i].NextId;
    }

    public void Dispose()
    {
        Intrinsic.Dispose();
        _stimuli.Dispose();
        _loom.Dispose();
    }

    /// GENERATE — sample a block from the current grammar via the strategy, curiosity-metabolism live (the proven
    /// anti-collapse reweight rides every node). Deterministic given (grammar, seed).
    public byte[] Generate(int count, ulong seed) => _generator.Generate(Grammar, count, seed, Metabolism);

    public NeuronWrite EmitIntrinsic(int step, byte[] block, Journal journal, int maxSpans = int.MaxValue)
    {
        int minted = 0;
        foreach (ReadOnlyMemory<byte> line in Engine.SplitLines(block))
        {
            if (minted >= maxSpans) break;
            if (line.Length == 0) continue;
            TapePacketCreator.AppendGeneratedUtterance(Intrinsic, journal, step, Name, line);
            minted++;
        }
        return new NeuronWrite(minted, Intrinsic.Count + Intrinsic.ShedEventIDs.Count, Intrinsic.ByteLength);
    }

    private void AppendStimuli(List<Tape> stimuli)
    {
        for (int i = 0; i < stimuli.Count; i++)
        {
            Tape sourceTape = stimuli[i];
            long mark = _stimulusMarks.GetValueOrDefault(sourceTape);
            foreach (TapeEventView unit in sourceTape.EnumerateAppendedSince(mark))
            {
                if (unit.Id.Value < mark) continue;
                if (!sourceTape.Resolve(unit.Id, out byte[] span))
                    throw new InvalidOperationException($"stimulus projection failed: {sourceTape.SourceOf(unit.Id)} {unit.Id} did not resolve");
                string source = sourceTape.SourceOf(unit.Id);
                Provenances provenance = sourceTape.ProvenanceOf(unit.Id);
                _stimuli.Append((byte[])span.Clone(), source, provenance);
            }
            _stimulusMarks[sourceTape] = sourceTape.NextId;
        }
    }

    private static IGenerator PickGen(string gen, double affFloor) => gen switch
    {
        // Each neuron owns its standing model tables. Shared singleton strategies would
        // invalidate one node's cache on every peer's grammar publish, turning the mesh
        // back into a rebuild-on-every-action sampler.
        "markov"    => new MarkovWalk(),
        "mcmc"      => new McmcWalk(),
        "coupling"  => new CouplingWalk(),
        "nodebirth" => new NodeBirthWalk(affFloor),
        _           => new MetabolicWalk(),
    };

    public static Neuron Of(NeuronID id, string gen, double affFloor, double lambda)
        => new(id, PickGen(gen, affFloor), lambda);
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE DRIVE — the N-node loopback combustion over the ONE shared tape
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The mesh drive's knobs — everything that makes a run deterministic (same corpora + seed ⇒ same curve + the
/// same vest transitions, the Vow). One corpus PER NODE (each node's domain seed); 1 corpus = the single-node arm.
internal sealed record MeshConfig(
    IReadOnlyList<string> CorpusPaths,   // one domain corpus per node (the mask's seed world); 1 = the byte-identical single-node arm
    int Steps = 200,
    int BlockLen = 700,
    int MaxBlockBytes = 16384,
    int MintSpansPerStep = 4,            //  — a node's dream accretes at the scaffold's IntakeBatch cadence, never a firehose (Cortex.MintSpansPerStep)
    double Lambda = 0.3,
    ulong Seed = 0xC0117011UL,
    string Generator = "metabolic",
    double AffFloor = 1.0,
    int WScale = 8,                      // provenance weight (power of two): a peer-vested claim outweighs an unvested one under the shared-tape count measure. >1 = the combustion armed; 1 = the vests-never-fire control arm
    int SeedSpansPerNode = 3,            // bootstrap anchor per node (its domain's first spans, source="corpus", so the residual can discriminate before the node self-drives)
    bool CrossReflect = true,            // THE SOURCE-INDEPENDENCE GATE: a node's dream (source=nodeX) reflects when a DIFFERENT source exercises its ≥8B rule — a PEER node's span, not just corpus, and SAME-source (self/clones) is REJECTED. ON by default (the multi-node driver exists to exercise it); OFF = the Real-only gate (reflect only via a corpus jewel — the control arm)
    int CheckpointEvery = 25,            // checkpoint cadence in steps — the safe-to-kill law: snapshot the whole mesh (shared tape · journal · per-node metabolism + drain edges + loop locals) atomically every N steps. 0 = never (the study-drive arm, byte-identical to the pre-checkpoint machine)
    // ── THE DREAM:REAL BALANCE ──  post-drain, unbounded mint against frozen real drowns the witness and sinks meanz off-basin. These two levers keep the EXTERNAL real flowing and cap the INTRINSIC dream against it, so the corroborated corpus holds the basin (the combustion is INTERLEAVING intrinsic + external). ──
    int MixEvery = 0,                    // THE MIX RAIL: post-drain, re-ingest a real corpus span per node on this cadence (round-robin over the node's pool, source="corpus" Real — NOT re-marked drained), so VARIED real keeps arriving after the corpus exhausts. 0 = OFF = the sealed-loop control arm (post-drain real freezes); 8 = the Cortex's proven cadence
    double ReplayRatio = 0.0,             // THE DREAM-FRACTION CAP: cap UNVESTED dream spans at ratio x born evidence, so dreams cannot outpace evidence available to corroborate them. A VESTED dream frees its slot (corroborate more, then dream more). 0 = unbounded (the firehose control arm); 1.0 = the Cortex's parity cap
    // ── THE AESTIVATION (tape-bound — the O(Δ) port's companion, phase 3) ──  post-shed/drop consolidation cadence.
    int ConsolidationPhaseEvery = 32,                 // SHED (evidence the shared grammar generates whole → event byte log, stays in view) + DROP (stale unvested dreams → leave the view) + RESPLICE (re-price every loom at post-vest weights), every N steps. Bounds the RESIDENT tape to a rolling window so the O(Δ) fold never rides an unbounded view (the loom's O(tape²) failure mode). 0 = OFF = everything-resident (the unbounded pre-shed arm); 32 = default (loose enough the recency guard never starves a working-window read, tight enough dream accretion stays capped)
    // ── THE SUSTAINED FEED ──  at depth the token MIX drip can't sustain the real FRACTION the basin needs, and the unvested-only ReplayRatio cap is DEFEATED because near-perfect vesting empties the unvested stock it measures, so the vested-dream flood escapes it. These two levers make the feed SUSTAINED (real arrives thick at depth) and the cap TOTAL (the vested flood can't escape). ──
    int MixSpans = 1,                    // THE FAT MIX RAIL — real spans re-ingested PER NODE per MIX event (round-robin, the pool WRAPS so varied real arrives forever). MixEvery sets the cadence, MixSpans the WIDTH: K sized to MintSpansPerStep restores a ~1:1 real:dream FLOW even after the finite pool drains. 1 = the token drip (byte-identical to the pre-fatten machine)
    bool ReplayCapTotal = false,          // THE TOTAL-DREAM CAP — when ARMED, ReplayRatio caps TOTAL ReplayCount (vested + unvested) against born evidence, not just the unvested stock. The unvested-only cap is DEFEATED at depth (near-perfect vesting drains the unvested stock to approximately zero so the cap never binds and the vested-dream flood escapes); total-mode counts the vested dreams the flood is MADE OF, so dream:evidence stays bounded regardless of vest_rate. false = unvested-only semantics; true = the depth-holding cap
    // ── THE MESH HOMEOSTAT (the proprioceptive fix — boredom, not mind-break; MeshHomeostat.cs) ──  where MixSpans/
    // ReplayCapTotal clamp the collapse EXOGENOUSLY (feed + cap), this gives the mesh PROPRIOCEPTION: it reads its OWN
    // criticality (meanz vs the basin — the honest RG axis, since cvz DE-groks under the flood) and DOWN-REGULATES its
    // own dream MINT RATE when meanz drifts off-basin, so the mind RESTS into BOREDOM instead of over-fitting its dream
    // structure into the sink. A fresh input re-ignites it. Complementary to the flow levers, independently armable.
    bool MeshHomeo = false,              // arm the proprioceptive dream-throttle: the mint rate becomes a negative-feedback function of meanz drift off-basin (down-regulate on sink, relax on recovery). Off = the open-loop mesh byte-identically (fixed mint rate — the collapse arm)
    double MeshFloor = 0.05,             // THE BOREDOM FLOOR — the throttle's lower bound (a rested mind dreams at this fraction of the open rate). The anti-dark-room guarantee is NOT this trickle (a per-mint span floor re-floods the tape and defeats the throttle — measured) but the MIX RAIL's real re-ingest: novelty never dies while the world re-arrives, so mint can rest near 0 and real pulls meanz back. Low (0.05) so the rest is deep enough that dream stops out-massing real
    double MeshGain = 0.30);             // the throttle's integral gain per step — how fast it chases its criticality target. The collapse is PREVENTIVE-only (once the vested-dream mass forms, no throttle restores meanz), so the gain is brisk: clamp the mint HARD as soon as meanz dips past the basin, before the mass accretes

/// The mesh loop's own locals at a checkpoint — everything the `for` body carries between steps that is not
/// already an organ's state. `NextStep` is the step the resumed loop EXECUTES first; `TotalVested` is the running
/// cross-source vest tally (the combustion readout, carried so a resumed curve continues its Σvest column); `CurveLen`
/// / `JournalLen` are the append-only artifacts' byte horizons (rows a kill left past the snapshot are truncated back
/// on resume, so the continuation splices byte-exact).
internal readonly record struct MeshSnap(int NextStep, int TotalVested, long CurveLen, long JournalLen);

/// THE TRIANGLE CHECKPOINT — mid-run durability for the mesh (the safe-to-kill law, ported to the N-node drive). The
/// trunk's Checkpoint is welded to its single-node organ suite (CortexSnap + curriculum/reads/selfmodel/homeostat/
/// loom/rhythm), none of which the mesh carries; the mesh's state is TINY by comparison, so it gets its own lean
/// dialect over the SAME primitives (CkptWriter/CkptReader, the atomic Checkpoint.Save landing). What is state-bearing:
/// the SHARED TAPE (Tape.Save — the whole reality every node minted onto, provenance + vested bits and all), the
/// JOURNAL (Journal.Save — the durable event record), and PER NODE its Metabolism (the anti-collapse recency table)
/// plus its drain edge (`drained[i]` + the `ingested[i]` bitmap — which pool spans it has eaten). What is NOT
/// serialized: the per-node GRAMMARS — each is a PURE function of its routed stimulus projection
/// (`node.InduceFrom(stimuli)`), so a resumed node continues from the restored projection and loom. The corpus GUARD
/// proves the pools rebuilt on resume match the ones the snapshot was cut from
/// (a changed corpus makes byte-identity impossible — fail loud, never drift). Save∘Load∘Save = identity: dictionaries
/// key-sorted (Metabolism/Tape already are), so a reloaded mesh re-saves byte-identically.
internal static class MeshCheckpoint
{
    public const string FileName = "checkpoint.bin";
    private static ReadOnlySpan<byte> Magic => "CGRING\n"u8;   // the routed-neuron mesh dialect — distinct from trunk/old mesh images

    private const uint TagConfig  = 0x43464721;   // CFG!
    private const uint TagGuard   = 0x47554152;   // GUAR
    private const uint TagSnap    = 0x534E4150;   // SNAP
    private const uint TagTape    = 0x54415045;   // TAPE
    private const uint TagJournal = 0x4A524E4C;   // JRNL
    private const uint TagNodes   = 0x4E4F4445;   // NODE
    private const uint TagLooms   = 0x4C4F4F4D;   // LOOM — the shared + per-node standing looms
    private const uint TagReads   = 0x52454144;   // READ
    private const uint TagMesh    = 0x4D455348;   // MESH — the proprioceptive dream-throttle's tiny state (MeshHomeostat)
    private const uint TagEnd     = 0x454E4421;   // END!

    /// Serialize the whole mesh to an in-memory image — split from Save so the resume verifier can re-encode a loaded
    /// state and byte-compare against the file (the round-trip Vow). Node order is id order (node0, node1, …) — the
    /// fixed construction order the drive iterates, so the walk is deterministic. `sharedGrammar` is the whole-tape
    /// grammar THIS step's read scored — Reads' stride seen-flag keys on its Rules identity (the same instance the
    /// drive carries), so the reader's rolling windows re-anchor exactly on resume (the momentum/collapse/JS/meanz-
    /// drift verdict columns land byte-identical, not just the grammar-derived axes).
    public static byte[] Encode(MeshConfig cfg, IReadOnlyList<long> corpusBytes, IReadOnlyList<int> poolCounts,
        in MeshSnap snap, Tape tape, Journal journal, Neuron[] nodes, int[] drained, int[] mixed, bool[][] ingested,
        Reads reads, Loom sharedLoom, in RePairResult sharedGrammar, MeshHomeostat mesh)
    {
        using MemoryStream ms = new(1 << 20);
        using (CkptWriter w = new(ms))
        {
            w.Raw(Magic);
            w.Section(TagConfig);  WriteConfig(w, cfg);
            w.Section(TagGuard);
            w.I32(nodes.Length);
            for (int i = 0; i < nodes.Length; i++) { w.I64(corpusBytes[i]); w.I32(poolCounts[i]); }
            w.Section(TagSnap);    w.I32(snap.NextStep); w.I32(snap.TotalVested); w.I64(snap.CurveLen); w.I64(snap.JournalLen);
            w.Section(TagTape);    tape.Save(w);
            w.Section(TagNodes);
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].Intrinsic.Save(w);
                nodes[i].Stimuli.Save(w);
                nodes[i].Metabolism.Save(w);
                w.I32(drained[i]);
                w.I32(mixed[i]);                              // the MIX rail cursor — post-drain real re-ingests done (the drip's round-robin position)
                w.I32(ingested[i].Length);
                foreach (bool b in ingested[i]) w.Bool(b);
            }
            w.Section(TagJournal); journal.Save(w);
            // ── THE LOOMS ── the shared weighted subject + each node's generator loom. Current sections carry
            // the entry journal and typed standing arena; legacy v2 sections retain their entry-only fallback.
            w.Section(TagLooms);
            sharedLoom.Save(w);
            for (int i = 0; i < nodes.Length; i++) nodes[i].Loom.Save(w);   // every node is loom-armed over its stimulus projection
            w.Section(TagReads);   reads.Save(w, sharedGrammar);   // the sparkline reader's rolling memory — the windowed verdict columns' anchor
            w.Section(TagMesh);    mesh.Save(w);                   // the proprioceptive dream-throttle — the throttle scalar + the smoothed meanz/drift senses (rides uniformly, armed or not, so the image shape never depends on the arm)
            w.Section(TagEnd);
        }
        return ms.ToArray();
    }

    /// Read ONLY the config (+ integrity magic) from a run dir's checkpoint — the resume entry needs the config FIRST
    /// to rebuild PHASE 1/2 (corpora/pools/nodes) before the full state load restores into those organs.
    public static MeshConfig PeekConfig(string runDir)
    {
        using FileStream fs = File.OpenRead(Path.Combine(runDir, FileName));
        using CkptReader r = new(fs);
        ReadMagic(r);
        r.Expect(TagConfig);
        return ReadConfig(r);
    }

    /// Read ONLY the converged shared GRAMMAR from a mesh run dir's checkpoint — the transfer-learning entry that lets
    /// `navigate --pretrain` seed RepoGrok off a WITNESSED mesh (CGRING) instead of a trunk (CGCKPT) checkpoint. The
    /// mesh's shared grammar is a pure function of its converged TAPE, so this walks magic→config→guard→snap→tape
    /// (mounting the run's event byte log first — a `--night`-shed mesh carries evacuated events), then batch-inducts the tape
    /// under the run's own wScale. This is Engine.Induce (batch-final), NOT the loom's greedy-in-arrival harvest — the
    /// two differ per , but for a pretrain BASE only the converged VOCABULARY matters (LoadPretrainBase keeps the
    /// pure-binary prefix to seed the rank-encoder), and the batch grammar over the exact witnessed tape is the
    /// faithful, world-rebuild-free carrier of it. Read-once, reuse read-only. Stops after the grammar — tape's the
    /// last section it touches, journal/nodes/looms/reads/mesh are never parsed.
    public static RePairResult PeekGrammar(string runDir)
    {
        using FileStream fs = File.OpenRead(Path.Combine(runDir, FileName));
        using CkptReader r = new(fs);
        ReadMagic(r);
        r.Expect(TagConfig); MeshConfig cfg = ReadConfig(r);            // consumes exactly its bytes; wScale drives the induce
        r.Expect(TagGuard);                                      // skip the corpus guard (per-node bytes/pool) — no reconstruction needed
        int ckNodes = r.I32();
        for (int i = 0; i < ckNodes; i++) { r.I64(); r.I32(); }
        r.Expect(TagSnap); r.I32(); r.I32(); r.I64(); r.I64();   // skip the 4-field MeshSnap progress snapshot
        using Tape tape = new();
        tape.MountLog(new FileStream(Path.Combine(runDir, "tape.spanlog"), FileMode.Open, FileAccess.Read));   // shed bytes resolve from here (Tape.Load requires it when evacuated entries exist)
        r.Expect(TagTape); tape.Load(r);                         // the converged resident+shed tape — the witnessed diet
        (Symbol[] Tape, int N, RePairResult Result) induced = Engine.Induce(tape, cfg.WScale);         // batch-final grammar over the exact witnessed tape (world-rebuild-free)
        return induced.Result;
    }

    /// The TRACED twin of PeekGrammar — the same batch-final grammar over the witnessed tape, PLUS the per-rule
    /// thought stream (MergeEvent[i] ↔ Rules[i] by emission order). The one place the provenance-weighted
    /// corroboration count survives as a per-rule scalar: MergeEvent.Count is the WITNESSED vest weight at mint
    /// (the wcount Mdl.PairDelta normalizes — Induct.cs:28), consumed during induce and dropped from the frozen
    /// GrammarRule. The cov-beacon's VEST weight reads it (CovBeacon.Build). Same read-once discipline as PeekGrammar.
    public static (RePairResult Grammar, List<MergeEvent> Events) PeekGrammarTraced(string runDir)
    {
        using FileStream fs = File.OpenRead(Path.Combine(runDir, FileName));
        using CkptReader r = new(fs);
        ReadMagic(r);
        r.Expect(TagConfig); MeshConfig cfg = ReadConfig(r);
        r.Expect(TagGuard);
        int ckNodes = r.I32();
        for (int i = 0; i < ckNodes; i++) { r.I64(); r.I32(); }
        r.Expect(TagSnap); r.I32(); r.I32(); r.I64(); r.I64();
        using Tape tape = new();
        tape.MountLog(new FileStream(Path.Combine(runDir, "tape.spanlog"), FileMode.Open, FileAccess.Read));
        r.Expect(TagTape); tape.Load(r);
        (Symbol[] Tape, int N, RePairResult Result, List<MergeEvent> Events) induced = Engine.InduceTraced(tape, cfg.WScale);   // grammar + the per-rule vest-weighted merge counts
        return (induced.Result, induced.Events);
    }

    /// PeekGrammar's AUDIT twin — the converged grammar PLUS the witnessed TAPE it was induced over (per-span
    /// provenance + node source-tags intact), the two inputs Pearl.Audit co-walks. The depth-profile autopsy reads
    /// reflection PER RULE off this pair (the jewel-source breadth by rule depth); no other consumer needs the raw
    /// tape, so it stays a distinct seam from the pretrain-only PeekGrammar. Also returns the run's own wScale +
    /// cross-reflect gate + corpus set, so an offline audit reproduces the run's reflection rule exactly and can
    /// re-induce the DIET's own depth ladder (P3). Read-once; the CALLER owns the returned tape (Dispose closes its
    /// mounted event byte log). The audit reads only event METADATA (len/source/evidence), never bytes — so the log handle is
    /// live only for the Induce inside here, and the returned tape is byte-free but metadata-complete.
    public static (RePairResult Grammar, Tape Tape, int WScale, bool CrossReflect, IReadOnlyList<string> CorpusPaths) PeekGrammarAndTape(string runDir)
    {
        using FileStream fs = File.OpenRead(Path.Combine(runDir, FileName));
        using CkptReader r = new(fs);
        ReadMagic(r);
        r.Expect(TagConfig); MeshConfig cfg = ReadConfig(r);
        r.Expect(TagGuard);
        int ckNodes = r.I32();
        for (int i = 0; i < ckNodes; i++) { r.I64(); r.I32(); }
        r.Expect(TagSnap); r.I32(); r.I32(); r.I64(); r.I64();
        Tape tape = new();
        string spanlog = Path.Combine(runDir, "tape.spanlog");
        if (File.Exists(spanlog)) tape.MountLog(new FileStream(spanlog, FileMode.Open, FileAccess.Read));   // shed bytes resolve here during Induce; a spanlog-less (never-shed) run skips it
        r.Expect(TagTape); tape.Load(r);
        (Symbol[] Tape, int N, RePairResult Result) induced = Engine.Induce(tape, cfg.WScale);   // the batch-final grammar over the exact witnessed tape (the same vocabulary the run's read scored)
        return (induced.Result, tape, cfg.WScale, cfg.CrossReflect, cfg.CorpusPaths);
    }

    /// Restore the whole mesh from the run dir's checkpoint into freshly-constructed organs. The caller (Resume) has
    /// already rebuilt PHASE 1/2 deterministically from the config; the GUARD proves the corpora it rebuilt are the
    /// ones the checkpoint was cut from. Node grammars are NOT restored here — the caller re-derives them off the
    /// loaded tape (pure function of the masked view). The READS section re-anchors its stride seen-flag against the
    /// SHARED grammar re-induced off the restored tape (the same grammar the checkpoint's read scored — pure function
    /// of the tape, so byte-identical), landing the first post-resume read's windowed columns exactly. Returns the
    /// loop-locals snapshot AND that re-induced shared grammar — the seen-flag was anchored on THIS instance, so a
    /// verify re-encode must reuse it (a second induce is a different array reference and would flip the flag).
    public static (MeshSnap Snap, RePairResult Shared) Load(string runDir, IReadOnlyList<long> corpusBytes, IReadOnlyList<int> poolCounts,
        Tape tape, Journal journal, Neuron[] nodes, int[] drained, int[] mixed, bool[][] ingested, Reads reads, Loom sharedLoom, MeshHomeostat mesh)
    {
        using FileStream fs = File.OpenRead(Path.Combine(runDir, FileName));
        using CkptReader r = new(fs);
        ReadMagic(r);
        r.Expect(TagConfig); ReadConfig(r);                                // re-read (the caller peeked it) — consumes exactly its bytes
        r.Expect(TagGuard);
        int ckNodes = r.I32();
        if (ckNodes != nodes.Length)
            throw new InvalidDataException($"checkpoint node-count guard failed: checkpointed {ckNodes} nodes, rebuilt {nodes.Length} — the corpus set changed since the run; byte-identical resume is impossible");
        for (int i = 0; i < ckNodes; i++)
        {
            long ckBytes = r.I64(); int ckPool = r.I32();
            if (ckBytes != corpusBytes[i] || ckPool != poolCounts[i])
                throw new InvalidDataException($"checkpoint corpus guard failed at node{i}: checkpointed {ckBytes}B/{ckPool} spans, rebuilt {corpusBytes[i]}B/{poolCounts[i]} — corpus {i} changed since the run; byte-identical resume is impossible");
        }
        r.Expect(TagSnap);
        MeshSnap snap = new(r.I32(), r.I32(), r.I64(), r.I64());
        r.Expect(TagTape);    tape.Load(r);                                // residents + shed/tomb tables + id high-water (event byte log mounted by the World before this call)
        r.Expect(TagNodes);
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i].Intrinsic.Load(r);
            nodes[i].Stimuli.Load(r);
            nodes[i].Metabolism.Load(r);
            drained[i] = r.I32();
            mixed[i] = r.I32();                              // the MIX rail cursor (written in Encode's Nodes section, same order)
            int n = r.I32();
            if (n != ingested[i].Length)
                throw new InvalidDataException($"checkpoint ingest-bitmap guard failed at node{i}: checkpointed {n} pool spans, rebuilt {ingested[i].Length} — pool {i} changed since the run");
            for (int j = 0; j < n; j++) ingested[i][j] = r.Bool();
        }
        r.Expect(TagJournal);
        journal.Load(r);
        // ── RESTORE THE LOOMS ── Loom.Load restores the typed standing arena (or legacy entry-only fallback) and
        // rebuilds only its derived count/occurrence/heap planes. The shared grammar for the seen-flag anchor comes from the SHARED loom's harvest (the loom IS the
        // greedy-in-arrival grammar the checkpoint's read scored — a batch Engine.Induce would produce a DIFFERENT
        // grammar and flip every windowed verdict column).
        r.Expect(TagLooms);
        sharedLoom.Load(r, tape);
        for (int i = 0; i < nodes.Length; i++) nodes[i].Loom.Load(r, nodes[i].Stimuli);
        RePairResult shared = sharedLoom.Result(tape);                    // the standing loom's own grammar — the seen-flag's re-anchor target (byte-identical to the checkpoint's live harvest)
        r.Expect(TagReads);
        reads.Load(r, shared);
        r.Expect(TagMesh);
        mesh.Load(r);
        r.Expect(TagEnd);
        return (snap, shared);
    }

    /// Land the snapshot ATOMICALLY in the run dir — write to a tmp sibling, then rename over checkpoint.bin (a kill
    /// mid-write leaves the previous checkpoint intact). The trunk's Checkpoint.Save is the identical atomic dance,
    /// but it takes a Cortex Run; this thin passthrough keeps the mesh self-contained on its own dialect.
    public static long Save(Run run, byte[] image) => Checkpoint.Save(run, image);

    private static void WriteConfig(CkptWriter w, MeshConfig c)
    {
        w.I32(c.CorpusPaths.Count);
        foreach (string p in c.CorpusPaths) w.Str(p);
        w.I32(c.Steps); w.I32(c.BlockLen); w.I32(c.MaxBlockBytes); w.I32(c.MintSpansPerStep);
        w.F64(c.Lambda); w.U64(c.Seed); w.Str(c.Generator); w.F64(c.AffFloor);
        w.I32(c.WScale); w.I32(c.SeedSpansPerNode); w.Bool(c.CrossReflect); w.I32(c.CheckpointEvery);
        w.I32(c.MixEvery); w.F64(c.ReplayRatio);   // the dream:evidence balance levers - part of the config the resume rebuilds byte-identically
        w.I32(c.ConsolidationPhaseEvery);                       // the shed/drop/resplice cadence (the tape-bound) — part of the config a resume rebuilds byte-identically
        w.I32(c.MixSpans); w.Bool(c.ReplayCapTotal);   // the sustained-feed levers (the depth-holding fix) — MIX width + total-dream cap; a resume rebuilds them byte-identically
        w.Bool(c.MeshHomeo); w.F64(c.MeshFloor); w.F64(c.MeshGain);   // the mesh-homeostat levers (the proprioceptive fix) — arm + boredom floor + throttle gain; a resume rebuilds them byte-identically
    }

    private static MeshConfig ReadConfig(CkptReader r)
    {
        int n = r.I32();
        List<string> paths = new(n);
        for (int i = 0; i < n; i++) paths.Add(r.Str());
        return new MeshConfig(paths, r.I32(), r.I32(), r.I32(), r.I32(),
            r.F64(), r.U64(), r.Str(), r.F64(), r.I32(), r.I32(), r.Bool(), r.I32(),   // …WScale, SeedSpansPerNode, CrossReflect, CheckpointEvery
            r.I32(), r.F64(), r.I32(),   // MixEvery + ReplayRatio + ConsolidationPhaseEvery — read in WriteConfig's order
            r.I32(), r.Bool(),           // MixSpans + ReplayCapTotal — the sustained-feed levers, same order as WriteConfig
            r.Bool(), r.F64(), r.F64());  // MeshHomeo + MeshFloor + MeshGain — the mesh-homeostat levers, same order as WriteConfig
    }

    private static void ReadMagic(CkptReader r)
    {
        byte[] m = r.Raw(Magic.Length);
        if (m.AsSpan().SequenceEqual(Magic)) return;
        throw new InvalidDataException(m.AsSpan().StartsWith("CG"u8)
            ? $"checkpoint format skew: file is {System.Text.Encoding.ASCII.GetString(m).TrimEnd('\n')}, the mesh reads {System.Text.Encoding.ASCII.GetString(Magic).TrimEnd('\n')} (a trunk checkpoint fed to the mesh resume, or an organ's shape moved)"
            : "not a cogito mesh checkpoint (bad magic)");
    }
}

internal static class Mesh
{
    /// usage: mesh <corpus1> [corpus2 corpus3...] [--steps N] [--block N] [--gen metabolic|markov|mcmc|coupling|nodebirth]
    ///                 [--wscale W] [--mintspans N] [--seedspans N] [--lambda F] [--seed HEX] [--checkpoint-every N] [--mix N] [--mixspans N] [--dreamratio F] [--dreamcap-total] [--night N]
    ///        mesh --resume <run-dir> [--steps N] [--verify]   resume a killed mesh from its checkpoint (byte-identical continuation)
    /// One corpus per node — 1 corpus is the single-node arm (byte-identical to a plain autoregressive drive), 2/3
    /// corpora is the fan-out (the loopback combustion: each node's grammar witnesses the peers' dreams). Induction is
    /// O(Δ) via the STANDING LOOMS (per-node generators + the shared witness/read subject) — SpliceNew+Pump the Δ each
    /// step, never re-inducing O(tape) from scratch; the AESTIVATION (--night N) sheds/drops/resplices to keep the resident
    /// tape a rolling window, so a deep run is tractable (2000 steps in ~22min where the O(tape) drive was ~40h).
    public static int Run(string[] args)
    {
        string resumeDir = Args.Str(args, "--resume", "");
        if (resumeDir.Length > 0) return Resume(resumeDir, Args.Has(args, "--verify"), Args.Int(args, "--steps", 0));

        List<string> corpora = Args.Positionals(args, 1).Where(File.Exists).ToList();
        if (corpora.Count == 0)
        {
            Console.Error.WriteLine("  usage: mesh <corpus1> [corpus2 corpus3 ...] [--steps N] [--block N] [--gen metabolic|markov|mcmc|coupling|nodebirth] [--wscale W] [--mintspans N] [--seedspans N] [--lambda F] [--seed HEX] [--checkpoint-every N] [--mix N] [--mixspans N] [--dreamratio F] [--dreamcap-total] [--night N]");
            Console.Error.WriteLine("         mesh --resume <run-dir> [--steps N] [--verify]   resume a killed mesh from its checkpoint (byte-identical continuation; --steps EXTENDS the horizon; --verify = round-trip readout only)");
            Console.Error.WriteLine("  each corpus is a NODE (a source-tagged mind over the ONE shared tape); 1 = single-node (byte-identical), 2/3 = the multi-node loopback combustion (peers witness each other's dreams).");
            Console.Error.WriteLine("  induction is O(Δ) via the standing looms; --night N (default 32) = the shed/drop/resplice cadence that bounds the resident tape (0 = OFF, everything-resident — the unbounded pre-shed arm).");
            return 1;
        }
        int steps     = Args.Int(args, "--steps", 200);
        int block     = Args.Int(args, "--block", 700);
        int maxBlock  = Args.Int(args, "--maxblock", 16384);
        int mintSpans = Args.Int(args, "--mintspans", 4);
        int seedSpans = Args.Int(args, "--seedspans", 3);
        double lambda = Args.Double(args, "--lambda", 0.3);
        string gen    = Args.Str(args, "--gen", "metabolic");
        double affFloor = Args.Double(args, "--affloor", 1.0);
        int wScale    = Args.Int(args, "--wscale", 8);
        ulong seed    = Args.Seed(args, "--seed", 0xC0117011UL);
        int ckptEvery = Args.Int(args, "--checkpoint-every", 25);
        int mixEvery  = Args.Int(args, "--mix", 0);                // the MIX rail cadence (post-drain real re-ingest); 0 = OFF (sealed control arm)
        int mixSpans  = Args.Int(args, "--mixspans", 1);           // the MIX rail WIDTH — real spans per node per MIX event (the sustained-feed fatten; 1 = the token drip)
        double dreamRatio = Args.Double(args, "--dreamratio", 0.0);   // the dream-fraction cap (dream <= ratio x born evidence); 0 = unbounded (control arm)
        bool dreamCapTotal = Args.Has(args, "--dreamcap-total");   // cap the TOTAL dream stock (vested + unvested), not just unvested — the depth-holding cap the vested flood can't escape
        int aestivationEvery = Args.Int(args, "--night", 32);            // the shed/drop/resplice cadence (the tape-bound); 0 = OFF (everything-resident — the unbounded pre-shed arm)
        bool meshHomeo = Args.Has(args, "--mesh-homeo");           // the proprioceptive dream-throttle (boredom-not-break): mint rate ← negative feedback on meanz drift off-basin
        double meshFloor = Args.Double(args, "--mesh-floor", 0.05);   // the boredom floor (the throttle's lower bound; the MIX rail is the anti-dark-room guarantee, not this trickle)
        double meshGain  = Args.Double(args, "--mesh-gain", 0.30);    // the throttle's integral gain per step (brisk — the collapse is preventive-only)
        return Drive(new MeshConfig(corpora, steps, block, maxBlock, mintSpans, lambda, seed, gen, affFloor, wScale, seedSpans, CheckpointEvery: ckptEvery, MixEvery: mixEvery, ReplayRatio: dreamRatio, ConsolidationPhaseEvery: aestivationEvery, MixSpans: mixSpans, ReplayCapTotal: dreamCapTotal, MeshHomeo: meshHomeo, MeshFloor: meshFloor, MeshGain: meshGain));
    }

    /// RESUME a killed mesh from its run dir's checkpoint.bin — the config rides INSIDE the checkpoint, so the run dir
    /// is the only argument; PHASE 1/2 rebuilds the corpus world + nodes deterministically from it, then Load restores
    /// the shared tape/journal/per-node organs and the drive continues to cfg.Steps. `verify` short-circuits before the
    /// drive: load → re-encode → byte-compare against the file (the Save∘Load∘Save = identity readout, the Vow).
    /// `steps` > 0 EXTENDS the horizon (a landed mesh resumes past its original cap; checkpoints cut during the extended
    /// leg carry the new horizon, so resume-of-resume continues it) — never valid under `verify` (the round-trip
    /// compares the config the image carries, not an override).
    public static int Resume(string runDir, bool verify = false, int steps = 0)
    {
        string? dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, MeshCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {MeshCheckpoint.FileName} under '{runDir}' — nothing to resume (the run predates checkpointing, or never reached its first checkpoint)");
            return 1;
        }
        MeshConfig cfg = MeshCheckpoint.PeekConfig(dir);
        if (steps > 0)
        {
            if (verify) { Console.Error.WriteLine("  --steps cannot ride --verify: the round-trip readout compares the config the checkpoint carries, not an override"); return 1; }
            cfg = cfg with { Steps = steps };
        }
        return Drive(cfg, Cogito.Run.Open(dir), verify);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE WORLD — PHASE 1/2 as ONE deterministic construction, identical on the fresh and resume paths. The
    //  checkpoint's corpus guard is only meaningful because BOTH paths build THIS world in THIS order (same corpus
    //  read, same SplitPool, same node/generator ladder) — a second hand-kept copy would drift exactly where
    //  byte-identity dies. Owns the shared Tape (Dispose closes it) and every per-node organ + drain edge.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private sealed class World : IDisposable
    {
        public readonly Tape Main = new();
        public readonly Journal Journal = new();
        public readonly List<byte[]>[] Pools;
        public readonly byte[][] Heldouts;
        public readonly long[] CorpusBytes;                            // per-node corpus size — the checkpoint's GUARD (a changed corpus fails byte-identity loud)
        public readonly long TotalCorpusBytes;
        public readonly Neuron[] Nodes;
        public readonly FrontierIndex[] Frontiers;
        public readonly bool[][] Ingested;                            // per-node pool-eaten bitmap (the drain frontier — checkpointed)
        public readonly int[] Drained;                               // per-node drain high-water (spans eaten from the pool)
        public readonly int[] Mixed;                                 // per-node MIX rail cursor — post-drain real re-ingests done (round-robin over the pool; checkpointed so the drip resumes byte-exact)
        public readonly byte[] Probe;                                // the shared-reality generalization probe (every node's held-out, concat'd)
        private readonly List<Tape> _stimuli = new(2);

        // ── THE SHARED LOOM (O(Δ)) ──  the standing Re-Pair state behind the WITNESS + READ subject: the shared
        // reality's grammar under the provenance-weighted count measure (wScale) — the trunk's InduceVested shape,
        // ported to the loom. SpliceNew+Pump folds the Δ spans every node minted/ingested this step; Result harvests
        // the grammar the audit vests on and the read scores. WEIGHTED (wScale=cfg.WScale): evidence splices at wScale,
        // an unvested dream at 1 — so a peer-vested claim outweighs an unvested one exactly as Engine.Induce(tape,
        // wScale) did (the vests-armed count measure). Where the per-node looms are the GENERATORS (unweighted, each
        // mind's dream strategy), THIS loom is the shared reality's STRUCTURE (the witness).
        public readonly Loom SharedLoom;

        public World(MeshConfig cfg, Run run)
        {
            int nNodes = cfg.CorpusPaths.Count;
            Pools = new List<byte[]>[nNodes];
            Heldouts = new byte[nNodes][];
            CorpusBytes = new long[nNodes];
            for (int i = 0; i < nNodes; i++)
            {
                byte[] corpus = File.ReadAllBytes(cfg.CorpusPaths[i]);
                CorpusBytes[i] = corpus.Length;
                TotalCorpusBytes += corpus.Length;
                (Pools[i], Heldouts[i]) = SplitPool(corpus);
            }
            Nodes = new Neuron[nNodes];
            Frontiers = new FrontierIndex[nNodes];
            for (int i = 0; i < nNodes; i++)
            {
                Nodes[i] = Neuron.Of(new NeuronID(i), cfg.Generator, cfg.AffFloor, cfg.Lambda);
                Frontiers[i] = new FrontierIndex(Pools[i]);
            }
            Ingested = new bool[nNodes][];
            Drained = new int[nNodes];
            Mixed = new int[nNodes];
            for (int i = 0; i < nNodes; i++) Ingested[i] = new bool[Pools[i].Count];
            // ── THE SHARED-REALITY GENERALIZATION PROBE ──  every node's held-out lines, concatenated: the multi-
            // domain probe the READ scores coverage/depth over (the shared grammar must generalize to text no node
            // ingested). Newline-joined like Tape.Concat so the probe reads the same way the induction input does.
            List<byte> probeBuf = new();
            for (int i = 0; i < nNodes; i++) { probeBuf.AddRange(Heldouts[i]); if (Heldouts[i].Length > 0 && Heldouts[i][^1] != (byte)'\n') probeBuf.Add((byte)'\n'); }
            Probe = probeBuf.ToArray();

            // ── THE SHARED LOOM + THE EVENT BYTE LOG ──  the shared reality's view IS the tape's whole view. The byte log is
            // the shed bytes' durable home, mounted BEFORE any resume Load restores a tape that had shed (shed entries
            // resolve through it) and BEFORE the first Resplice touches a shed span. Both looms (per-node + shared)
            // re-derive off the loaded tape on resume (Loom.Load — a re-splice, never a pump-from-zero), so kill→resume
            // stays byte-exact.
            SharedLoom = new Loom(256, (uint)'\n', cfg.WScale);
            Main.MountLog(new FileStream(run.PathOf("tape.spanlog"), FileMode.OpenOrCreate, FileAccess.ReadWrite));
        }

        public void GetStimuli(NeuronID nid, out List<Tape> stimuli)
        {
            _stimuli.Clear();
            _stimuli.Add(Main);
            _stimuli.Add(GetUpstreamNeuron(nid).Intrinsic);
            stimuli = _stimuli;
        }

        public void MarkStimuliSeen()
        {
            for (int i = 0; i < Nodes.Length; i++)
            {
                GetStimuli(Nodes[i].ID, out List<Tape> stimuli);
                Nodes[i].MarkStimuliSeen(stimuli);
            }
        }

        public string DescribeStimuli(NeuronID nid)
        {
            Neuron upstream = GetUpstreamNeuron(nid);
            return "main + " + upstream.Name + ".intrinsic";
        }

        public int[] GetPoolCounts()
        {
            int[] counts = new int[Pools.Length];
            for (int i = 0; i < Pools.Length; i++) counts[i] = Pools[i].Count;
            return counts;
        }

        private Neuron GetUpstreamNeuron(NeuronID nid)
        {
            int upstream = (nid.Value + Nodes.Length - 1) % Nodes.Length;
            return Nodes[upstream];
        }

        public void Dispose()
        {
            try { Main.Dispose(); }
            finally
            {
                try { SharedLoom.Dispose(); }
                finally
                {
                    try
                    {
                        foreach (Neuron neuron in Nodes) neuron.Dispose();
                    }
                    finally { Journal.Dispose(); }
                }
            }
        }
    }

    /// The FRESH-run entry — config in hand, mint a new run dir. Public so the kill-line study (and tests) can call it
    /// without the CLI.
    public static int Drive(MeshConfig cfg) => Drive(cfg, Cogito.Run.New("mesh"), verify: false);

    /// The drive core — fresh (a run just Cogito.Run.New'd) OR resume (a run Cogito.Run.Open'd, checkpoint.bin present).
    /// PHASE 1/2 (the World) is built deterministically either way; the resume path then LOADS the checkpoint over it
    /// (shared tape · journal · per-node metabolism + drain edges + loop locals) and continues from the snapshot's
    /// step, byte-identical to a straight-through run to the same horizon. `verify` (resume only) short-circuits: load
    /// → re-encode → byte-compare against the file (the Save∘Load∘Save = identity readout, the Vow).
    public static int Drive(MeshConfig cfg, Run run, bool verify)
    {
        Tape.RequireWScale(cfg.WScale);                                // the power-of-two law — fail loud at entry, every path
        int nNodes = cfg.CorpusPaths.Count;
        bool resume = File.Exists(Path.Combine(run.Dir, MeshCheckpoint.FileName));

        using World world = new(cfg, run);
        Tape main = world.Main;
        Journal journal = world.Journal;
        Neuron[] nodes = world.Nodes;
        List<byte[]>[] pools = world.Pools;
        bool[][] ingested = world.Ingested;
        int[] drained = world.Drained;
        int[] mixed = world.Mixed;
        byte[] probe = world.Probe;
        Loom sharedLoom = world.SharedLoom;

        // ── THE READ INSTRUMENT ──  constructed BEFORE the resume Load restores its rolling windows into it (the
        // sparkline reader — Reads.Step → LossReading — emitting the trunk's motion schema so ONE curve format spans
        // single- and multi-node; the GUI + kill-line grade meanz/CvZ/coverage/novelchain off ANY arm's curve by name).
        Reads reads = new();
        // ── THE MESH HOMEOSTAT ──  the proprioceptive dream-throttle (boredom-not-break). Constructed ALWAYS (its tiny
        // state rides the checkpoint uniformly — the HOME pattern, like reads); CONSULTED only when cfg.MeshHomeo arms.
        // Off, the mint rate is the open-loop MintSpansPerStep exactly as the pre-organ mesh (the collapse arm).
        MeshHomeostat mesh = new(cfg.MeshFloor, cfg.MeshGain);
        PearlAuditCache auditCache = new();
        // ── THE READ SAMPLER ──  one standing order-2/order-1 model over the shared grammar, delta-applied per
        // step (Engine.MarkovModel) — the static Engine.Generate rebuilt the whole ctx2/ctx1 successor tables
        // over the entire shared sequence every step, the same O(total) rebuild the per-neuron generators
        // already avoid by holding their own standing model (Generators.cs).
        Engine.MarkovModel sharedRead = new();
        List<TapeEventID> reflectedIDs = new();                        // per-step vest collector, reused (cleared per witness pass)

        int step0 = 0, totalVested = 0;
        if (resume)
        {
            // ── RESUME ──  restore the mesh from the checkpoint over the freshly-built world; the corpus GUARD proves
            // the pools rebuilt above are the ones the snapshot was cut from. Node grammars are NOT in the image — each
            // re-derives off the restored tape in the drive's first induce (pure function of the masked view). Reads'
            // rolling windows ARE restored (the windowed verdict columns' anchor — else momentum/collapse/JS/meanz-drift
            // would land as fresh-window noise for the first few post-resume steps, breaking byte-identity).
            (MeshSnap snap, RePairResult sharedLoaded) = MeshCheckpoint.Load(run.Dir, world.CorpusBytes, world.GetPoolCounts(), main, journal, nodes, drained, mixed, ingested, reads, sharedLoom, mesh);
            world.MarkStimuliSeen();
            if (verify)
            {
                // the round-trip Vow: re-encode the loaded state, byte-compare against the file the checkpoint was read
                // from (Save∘Load∘Save = identity — the resume-exactness proof, cheaper than a full straight-through A/B).
                // The re-encode MUST reuse the SAME shared grammar Load anchored Reads' seen-flag on — a second induce
                // is a different array reference, so the seen-flag (ReferenceEquals) would flip and the re-encode would
                // spuriously diverge from an in-loop checkpoint (which saved the flag TRUE against the live instance).
                byte[] onDisk = File.ReadAllBytes(Path.Combine(run.Dir, MeshCheckpoint.FileName));
                byte[] reEncoded = MeshCheckpoint.Encode(cfg, world.CorpusBytes, world.GetPoolCounts(), snap, main, journal, nodes, drained, mixed, ingested, reads, sharedLoom, sharedLoaded, mesh);
                bool ok = onDisk.AsSpan().SequenceEqual(reEncoded);
                Console.WriteLine(ok
                    ? $"  ✓ checkpoint round-trip byte-exact — {reEncoded.Length}B · step {snap.NextStep} · main {main.Count} spans · journal {journal.LineCount} lines (Save∘Load∘Save = identity)"
                    : $"  ✗ checkpoint round-trip DIVERGED — on-disk {onDisk.Length}B vs re-encoded {reEncoded.Length}B (the Vow is broken — an organ's Save∘Load is not identity)");
                return ok ? 0 : 1;
            }
            step0 = snap.NextStep;
            totalVested = snap.TotalVested;
            journal.Rewrite(run);                                      // reset journal.log to the checkpoint's line horizon (kill-orphans shed)
            run.TruncateCurve("curve.tsv", snap.CurveLen);             // shed curve rows a kill appended past the snapshot
            Trace.Note($"mesh ⇄ {Path.GetFileName(run.Dir)} · resumed at step {step0}/{cfg.Steps} · main {main.Count} spans (real {main.RealCount} · dream {main.ReplayCount} · vested {main.ReflectedReplayCount}) · Σvest {totalVested}");
        }
        else
        {
            // ── bootstrap each node's anchor ──  seed the first SeedSpansPerNode spans of each node's corpus onto the
            // shared tape as REAL corpus spans (so the residual can discriminate before the node self-drives, and so
            // the corpus source is present to vest dreams from step 0 — the single-node corpus-vs-node0 witness).
            for (int i = 0; i < nNodes; i++)
            {
                int seed0 = Math.Min(cfg.SeedSpansPerNode, pools[i].Count);
                for (int j = 0; j < seed0; j++) IngestSpan(pools[i], main, journal, ingested[i], j);
                drained[i] = seed0;
            }
            run.Write("config.txt", $"{cfg}\n");
            run.Write("curve.tsv", LossReading.Header + "\n");         // header lands NOW — rows append incrementally (a killed run keeps its curve; the checkpoint records the byte horizon)
            Trace.Note($"mesh · {nNodes} node(s) · {world.TotalCorpusBytes}B across {nNodes} corpora → shared tape · wScale={cfg.WScale} ({(cfg.WScale > 1 ? "combustion ARMED" : "control (vests dark)")}) · SYMMETRIC mutual witness · gen={cfg.Generator} λ={cfg.Lambda} · {cfg.Steps} steps · ckpt every {(cfg.CheckpointEvery > 0 ? cfg.CheckpointEvery + " steps" : "never")}");
            Trace.Note($"  dream:evidence balance · MIX rail {(cfg.MixEvery > 0 ? $"every {cfg.MixEvery} steps × {cfg.MixSpans} span/node (≈{(double)cfg.MixSpans * nNodes / cfg.MixEvery:F1} real/step — {(cfg.MixSpans > 1 ? "the FAT sustained feed" : "the token drip")})" : "OFF (post-drain real FREEZES — the sealed-loop confound arm)")} · dream cap {(cfg.ReplayRatio > 0 ? $"{(cfg.ReplayCapTotal ? "TOTAL" : "unvested")} dream ≤ {cfg.ReplayRatio:F2}×born-evidence{(cfg.ReplayCapTotal ? " (the vested flood can't escape — the depth-holding cap)" : "")}" : "UNBOUNDED (the firehose control arm)")}");
            for (int i = 0; i < nNodes; i++)
                Trace.Note($"  {nodes[i].Name} · {Path.GetFileName(cfg.CorpusPaths[i])} · pool {pools[i].Count} spans ({world.Heldouts[i].Length}B held out) · stimuli {world.DescribeStimuli(nodes[i].ID)}");
        }

        using WeftChannel weft = WeftChannel.Open(run, cfg.BlockLen, journal, main, step0, resume);

        using StreamWriter journalW = run.Appender("journal.log");
        journal.Mount(journalW);                                       // from here every journal line lands on disk as it happens
        using StreamWriter curveW = run.CurveAppender("curve.tsv");             // incremental curve + bounded live plots; AutoFlush preserves the checkpoint horizon

        // ── PHASE 3 · THE COMBUSTION DRIVE ──  each step, every node: INDUCE its masked view → GENERATE → READ →
        // then FORK (drain its scaffold by residual, or dream once its scaffold is internalized). After ALL nodes
        // have acted, the WITNESS PASS runs the shared-tape audit per node — a node's grammar exercising a
        // DIFFERENT-source span's ≥8B rule vests it (the cross-source corroboration, A's source-aware Audit). The
        // vest transitions are the combustion readout: a sealed single node produces ~0; the fan-out produces the
        // cross-mind vests the echo could not.
        // ── THE READ INSTRUMENT ──  the trunk's canonical sparkline reader (Reads.Step → LossReading), so the mesh
        // emits the SAME motion schema the single-node trunk does — one curve schema across single- and multi-node
        // (the GUI reads one format; the kill-line grades meanz/CvZ/coverage/novelchain off ANY arm's curve by name).
        // The READ subject is the SHARED reality's grammar (the standing shared loom's weighted harvest — the witness
        // structure below), because the curve row IS the shared reality's journal; its meanz is the criticality F2 grades (a SEALED loop
        // renorms toward −1.08, a WITNESSED mesh holds −0.70). Byte-identical across resume: Reads' grammar-derived
        // caches re-derive from the restored grammar (pure functions of it) and its rolling windows were restored above.
        for (int step = step0; step < cfg.Steps; step++)
        {
            long stepT0 = Trace.NowTicks;
            Trace.Mesh.Boundary("step", $"#{step}");

            // ── per-node act ──  induce → generate → fork (intake | mint). The nodes act in id order over the ONE
            // tape, so a node minted THIS step is visible to the next node's induce this step (the tightest coupling)
            // — deterministic by the fixed id order (the Vow).
            for (int i = 0; i < nNodes; i++)
            {
                Neuron node = nodes[i];
                PhaseScope phInduce = Trace.MeshPhase("induce");
                world.GetStimuli(node.ID, out List<Tape> stimuli);
                NeuronRead nodeRead = node.InduceFrom(stimuli);
                RePairResult g = nodeRead.Grammar;
                int viewBytes = nodeRead.ViewBytes;
                int viewSpans = nodeRead.ViewSpans;
                phInduce.Dispose();
                if (g.Compressed is null || g.Compressed.Length == 0) continue;   // this node has nothing on its view yet — skip it (a peer may still be seeding)

                PhaseScope phGen = Trace.MeshPhase("generate");
                byte[] block = node.Generate(cfg.BlockLen, cfg.Seed + (ulong)step * 131 + (ulong)i);   // per-(step,node) seed — deterministic, distinct per mind
                if (block.Length > cfg.MaxBlockBytes) block = block[..cfg.MaxBlockBytes];
                phGen.Dispose();

                if (drained[i] < pools[i].Count)
                {
                    PhaseScope phIntake = Trace.MeshPhase("intake", boundary: i == nNodes - 1);   // drain this node's scaffold by residual (RLEI-root)
                    foreach (int p in Radula.FrontierPick(node.Cover, pools[i], ingested[i], cfg.MintSpansPerStep, world.Frontiers[i]))
                    { IngestSpan(pools[i], main, journal, ingested[i], p); drained[i]++; }
                    phIntake.Dispose();
                }
                else
                {
                    // ── POST-DRAIN · THE DREAM:REAL BALANCE ──  the corpus is exhausted; without a counter-measure the
                    // node mints MintSpansPerStep dreams every step forever while real FREEZES → dream:real explodes
                    // (~11:1) and the shared grammar's meanz sinks to −1.04 in BOTH arms, drowning the witness (F2
                    // confound, ). Two levers restore the interleave of intrinsic (dream) + external (real):
                    //
                    //   (a) THE MIX RAIL — re-ingest one real corpus span on the MixEvery cadence (round-robin over the
                    //   node's own pool, source="corpus" Real, NOT re-marked drained). Keeps VARIED real arriving so the
                    //   external signal that holds the −0.70 basin never freezes. Runs BEFORE the mint so this step's
                    //   real is on the tape when the cap is measured (new evidence lifts dream headroom same-step).
                    //   THE SUSTAINED FEED (MixSpans): re-ingest MixSpans real spans per node per MIX event (round-robin,
                    //   the pool WRAPS so varied real arrives forever) — the token drip (MixSpans=1) could not hold the
                    //   real FRACTION the −0.70 basin needs at depth (0.38 real/step vs ~30 dream/step = ~80:1 flow), so
                    //   MixSpans sized to MintSpansPerStep restores a ~1:1 real:dream FLOW even after the finite pool drains.
                    if (cfg.MixEvery > 0 && step % cfg.MixEvery == 0 && pools[i].Count > 0)
                    {
                        PhaseScope phMix = Trace.MeshPhase("mix");
                        for (int m = 0; m < cfg.MixSpans; m++)
                        {
                            int pi = mixed[i]++ % pools[i].Count;   // round-robin re-ingest — extrinsic reality re-mounted (Cortex's MIX rail, fattened)
                            TapePacketCreator.AppendCorpusItem(main, journal, step, pools[i], pi);
                        }
                        phMix.Dispose();
                    }
                    //   (b) THE DREAM-FRACTION CAP — bound this step's mint against ReplayRatio x born evidence.
                    //   The UNVESTED-only measure (ReplayCapTotal=false — the Cortex's ReplayStep formula) is DEFEATED at
                    //   depth: near-perfect vesting empties the unvested stock to ≈0, so the cap never binds and the
                    //   VESTED-dream flood (44,661 spans in the collapsed run) escapes it, renorming meanz to −1.20. The
                    //   TOTAL measure (ReplayCapTotal=true) counts the vested dreams the flood is MADE OF, so dream:evidence
                    //   stays bounded regardless of vest_rate. Once it binds, mint follows new evidence admitted to the tape.
                    //   A VESTED dream frees its slot ONLY in unvested-mode (corroborate more → dream more); total-mode
                    //   holds the whole dream stock against born evidence (the basin-holding contract). 0 = unbounded (control).
                    long dreamStock = cfg.ReplayCapTotal ? node.Intrinsic.ReplayCount : node.Intrinsic.ReplayCount - node.Intrinsic.ReflectedReplayCount;
                    long dreamHeadroom = cfg.ReplayRatio <= 0 ? cfg.MintSpansPerStep
                        : (long)(cfg.ReplayRatio * main.BornEvidenceCount) - dreamStock;
                    int mintSpans = (int)Math.Clamp(dreamHeadroom, 0, cfg.MintSpansPerStep);
                    //   (c) THE MESH HOMEOSTAT — the PROPRIOCEPTIVE throttle. Where (a)/(b) clamp the flood exogenously
                    //   (feed + cap), this reads the mesh's OWN criticality (last step's meanz off the shared reality)
                    //   and DOWN-REGULATES the mint when meanz drifts off the −0.70 basin toward the −1.11 sink — so the
                    //   mind RESTS into boredom (minimal dream activity) instead of over-fitting its dream structure into
                    //   the sink, and RE-IGNITES when a fresh input lifts meanz back. Applied LAST (after the cap): the
                    //   throttle scales whatever the cap allowed, and floors ≥1 while raw>0 so a fresh MIX span can always
                    //   be dreamed-about and re-ignite the read (the anti-dark-room floor at the span level).
                    if (cfg.MeshHomeo) mintSpans = mesh.Apply(mintSpans);
                    PhaseScope phMint = Trace.MeshPhase("mint", boundary: i == nNodes - 1);
                    if (mintSpans > 0) node.EmitIntrinsic(step, block, journal, mintSpans);
                    node.Metabolism.Leak();                                                // between-block novelty decay — ALWAYS, so the metabolism cadence is cap-independent (Cortex.ReplayStep's shape)
                    phMint.Dispose();
                }
            }

            // ── WEFT EXECUTION WITNESS ──  run the tape-VM diet as a standing mesh channel. The sourced traces are
            // appended to the shared Tape as Replay spans BEFORE the shared loom folds this step, so Pearl can vest
            // them through the ordinary cross-source reflection pass. A separate barrier-free trace loom keeps the
            // value/depth/tower reads live without weakening the main Tape's newline barrier.
            PhaseScope phWeft = Trace.MeshPhase("weft", boundary: true);
            weft.Step(step, main, journal, nodes);
            phWeft.Dispose();

            // ── THE SHARED REALITY'S GRAMMAR (O(Δ)) ──  the WHOLE shared tape's grammar under the provenance-weighted
            // count measure — the trunk's InduceVested shape, ported to the STANDING SHARED LOOM: SpliceNew folds the Δ
            // spans every node minted/ingested/mixed this step (rule-count-independent), Pump mints the new winners, and
            // Result harvests the grammar the audit vests on and the read scores. Induced ONCE, unconditionally, every
            // step — load-bearing for F2: the SEALED arm (wScale=1) never vests, but its shared grammar still has a
            // criticality (meanz) that must be seen sliding toward −1.08. The loom rides the count measure exactly:
            // evidence splices at wScale, an unvested dream at 1 (SpliceNew reads IsEvidenceAt) — byte-identical count
            // math to Engine.Induce(tape, wScale)'s weighted path, greedy-in-arrival where the batch was greedy-in-final
            // (the trunk's proven trade; the vest-reweigh catches up at the aestivation's Resplice).
            PhaseScope phShared = Trace.MeshPhase("shared");
            sharedLoom.SpliceNew(main);
            sharedLoom.Pump();
            RePairResult shared = sharedLoom.Result(main);
            phShared.Dispose();

            // ── THE REFLECTION PASS ──  the combustion, keyed on the shared-tape grammar (the source-aware Pearl.Audit).
            // The division of labor is exact: the per-node looms are the GENERATORS (UNWEIGHTED — each mind's dream
            // strategy, diverging by its own metabolism + mint history), while the SHARED loom (WEIGHTED at wScale)
            // is the shared reality's STRUCTURE — the audit's provenance-count witness. The audit runs on the shared
            // grammar because that is the weighted structure; `crossReflect: true` vests a Replay span iff a
            // DIFFERENT-SOURCE span (a peer node's claim, or the corpus) exercised its ≥8B rule — the source-
            // independence guard. That is the combustion: a node's dream corroborated by a PEER's independent data
            // exercising the shared structure, never by the node's own echo. Runs only when armed (wScale>1); at
            // wScale=1 the count measure is dormant (the sealed-loop control arm, vests never fire — the F2 sink).
            if (cfg.WScale > 1 && shared.Compressed is { Length: > 0 })
            {
                PhaseScope phWitness = Trace.MeshPhase("witness", boundary: true);
                PearlAudit audit = auditCache.Get(main, in shared, cfg.WScale, crossReflect: true);
                reflectedIDs.Clear();
                int vested = auditCache.Rebuilt ? Pearl.Corroborate(audit, main, journal, step, reflectedIDs) : 0;
                if (vested > 0)
                {
                    // ── VEST → REPRICE ──  the standing shared loom fixed these spans' weights at splice time
                    // (SpliceNew reads IsEvidenceAt), so a vest must re-weight the exact transitioned spans to
                    // wScale NOW. The mesh has no tape delta-drain (the trunk reprices via DrainDelta →
                    // ApplyTapeDelta), and a skewed arena weight persists into MeshCheckpoint and fails
                    // LoadArena's tape-evidence cross-check on resume. The audit cache rides the same mutation
                    // set so the next witness pass stays a delta, not a full rebuild.
                    sharedLoom.RepriceReflected(main, CollectionsMarshal.AsSpan(reflectedIDs));
                    auditCache.RepriceReflected(main, CollectionsMarshal.AsSpan(reflectedIDs));
                    totalVested += vested;
                    Trace.Mesh.Boundary("vest", $"step={step} cross-source vested={vested} · main real={main.RealCount} dream={main.ReplayCount} vested={main.ReflectedReplayCount}");
                }
                Trace.Mesh.Boundary("witness-model", $"step={step} audit={(auditCache.Rebuilt ? (auditCache.DeltaRebuilds > 0 ? "delta" : "full") : "hit")} full={auditCache.FullRebuilds} delta={auditCache.DeltaRebuilds}");
                phWitness.Dispose();
            }

            // ── READ ──  the SHARED reality's row on the trunk's motion schema (Reads.Step → LossReading). The read
            // subject is the shared grammar just induced; the read BLOCK is a deterministic sample from it (the shared
            // reality dreaming aloud — the standing sharedRead model, delta-applied against the shared grammar, seeded
            // per step, not tangled in any one node's metabolism). The MOTION snapshot rides in the tape's vest-by-source census
            // (node0 vs peer — the SQUADRON's decisive read: node0-only must FREEZE peer-vests post-drain while the
            // fan-out keeps vest_peer climbing, the generator-independent witness the sealed node cannot be; these
            // counters live on the shared Tape, populated for free at every Vest). Converging onto LossReading is what
            // gives the curve meanz/CvZ/coverage/novelchain — the criticality F2 grades — on ONE schema the single-
            // node trunk and the GUI both read (the kill-line's --check reads any arm's curve by column name).
            if (shared.Compressed is { Length: > 0 })
            {
                PhaseScope phRead = Trace.MeshPhase("read", boundary: true);
                byte[] readBlock = sharedRead.GenerateFrom(in shared, cfg.BlockLen, cfg.Seed + (ulong)step, 1);
                if (readBlock.Length > cfg.MaxBlockBytes) readBlock = readBlock[..cfg.MaxBlockBytes];
                Reads.Motion motion = new(
                    VestN0: main.ReflectedNode0, VestPeer: main.ReflectedPeer, OutcomeCreditedTotal: main.ReflectedReplayCount, ReplayTotal: main.ReplayCount,
                    ReplaysN0: main.ReplaysNode0, ReplaysPeer: main.ReplaysPeer);
                LossReading read = reads.Step(step, (int)main.ByteLength, main.Count, main.RealCount, shared, readBlock, probe, wallTol: 0.003,
                    shedSpans: main.ShedCount, droppedSpans: main.DroppedCount, motion: motion);   // wallTol feeds only the momentum-verdict band (Cortex's 0.003 default) — orthogonal to the criticality columns F2 grades
                curveW.WriteLine(read.Row());                          // incremental (AutoFlush) — a kill keeps every completed row; the checkpoint records the flushed horizon
                phRead.Dispose();
                // ── THE PROPRIOCEPTIVE LOOP CLOSES HERE ──  the shared reality's criticality is now measured; feed it to
                // the throttle (armed only). The throttle it settles NOW governs the NEXT step's mint (mesh.Apply reads
                // the same _throttle) — a one-step-delayed negative feedback: the mind senses the consequence of last
                // step's dreaming and modulates the next. meanz is the HONEST axis (cvz DE-groks under the flood; meanz
                // stays honest), meanz_drift its onset alarm — the throttle down-regulates when this pair says the mind
                // is sliding off-basin toward the sink (boredom-not-break), relaxes back when it recovers (re-ignite).
                if (cfg.MeshHomeo)
                {
                    mesh.Sense(read.MeanZ, read.MeanzDrift);
                    if (step % 10 == 0 || step == cfg.Steps - 1) Trace.Mesh.Boundary("mesh", $"step={step} " + mesh.Line());
                }
                if (step % 10 == 0 || step == cfg.Steps - 1) Trace.Note("  " + read.Line());
            }

            long stepMs = Trace.ElapsedMs(stepT0);
            if (stepMs > Trace.StepSlowMs) Trace.Mesh.Warn("step.slow", $"step={step} ms={stepMs} main={main.Count}");

            // ── THE AESTIVATION · SHED + DROP + RESPLICE (the tape-bound, O(view·log) at the sleep cadence) ──  the mesh's
            // consolidation, ported from Cortex.Consolidate's phase-3 evacuation. Without it dreams accrete unbounded
            // (mint every step, real frozen) → the tape grows without limit → each step's O(Δ) fold rides an ever-
            // growing view → the resident RAM + the Resplice cost climb toward the O(tape²) wall the loom was meant to
            // kill. This keeps the RESIDENT tape a rolling window:
            //   SHED  — an EVIDENCE span the shared loom parses to ONE symbol (the grammar generates it whole —
            //           ParsedLenOf ≤ 1) sheds its raw bytes to the event byte log and STAYS in the view: not one count,
            //           use, or criticality read moves (order-freedom), only the RAM. Keyed on the SHARED loom (the
            //           witness structure), so a span that is still an active WITNESS — it exercises a ≥8B shared rule
            //           a peer will vest on — parses to >1 symbol and is NOT shed (the VOW's shed-vs-witness guard:
            //           shed never removes a span from the view, and the whole-span-parse criterion only fires once the
            //           span's structure is fully internalized, i.e. it carries no rule the grammar lacks).
            //   DROP  — a stale UNVESTED Replay older than the turnover window leaves the VIEW: reality never
            //           corroborated it, so its ReplayCount slot frees mint headroom and its counts retire in the
            //           Resplice (absent from GetEventViews). A VESTED dream is evidence and never drops (it sheds instead).
            // Then RESPLICE every loom (shared + per-node) — re-parse the view through each standing grammar at CURRENT
            // evidence status: the vest-reweigh hook (a vested span re-enters at wScale) + drop-retirement + arena
            // compaction, EXACTLY the state a fresh Load re-derives (Load's body is rule-replay + SpliceView), so
            // kill→resume stays byte-identical without ever paying pump-from-zero. The recency guard keeps every
            // recent span fully resident; both evacuation sets are id-ascending (deterministic — the Vow).
            if (cfg.ConsolidationPhaseEvery > 0 && step > 0 && (step + 1) % cfg.ConsolidationPhaseEvery == 0 && main.Count > ConsolidationPhase.ShedKeepRecentEvents)
            {
                PhaseScope phConsolidationPhase = Trace.MeshPhase("aestivation");
                // ── RE-PRICE FIRST (the resume-exactness pin) ──  the shed criterion reads ParsedLenOf, and the LIVE
                // loom's per-segment parse length is FROZEN AT ARRIVAL (greedy-in-arrival — a span spliced early was
                // parsed through fewer rules), while a RELOADED loom re-parses every span through the FINAL grammar
                // at once (Load → SpliceView, order-free). So a shed decision read off arrival-order lengths would
                // shed a DIFFERENT set live-vs-resumed (measured: +5 spans shed after a kill@60). Resplice re-parses
                // the whole view through the standing rules at CURRENT evidence status — landing exactly the state a
                // fresh Load re-derives — so ParsedLenOf now reads the SAME final-order lengths on both paths, and the
                // shed set is byte-identical. (This is ALSO the vest-reweigh + drop-nothing-yet pre-pass; the second
                // Resplice below retires the spans this pass evacuates.)
                sharedLoom.Resplice(main);
                foreach (Neuron node in nodes) node.Loom.Resplice(node.Stimuli);

                // the shed criterion reads the PRE-RESPLICED sharedLoom (the resume-exactness pin above): final-order
                // parse lengths, byte-identical live-vs-resumed. drops always ride (every mesh aestivation is a full aestivation).
                (int shedN, int dropN) = ConsolidationPhase.Evacuate(main, sharedLoom, dropUnvested: true);
                if (shedN + dropN > 0)
                {
                    // retire the evacuated spans: re-parse the (now smaller) view through every standing grammar —
                    // dropped events vanish from GetEventViews (their counts retire), shed events keep their counts (bytes
                    // only left RAM). Both the pre-Resplice (above) and this one land exactly the state a fresh Load
                    // re-derives, so the whole TRAJECTORY (rules · compressed · tape · vests · meanz/maxSpan/coverage/
                    // cvz) is byte-identical live-vs-resumed. The sole residual is the loom's SAVINGS TALLY: Resplice
                    // keeps `_savings` at its arrival-order value (no re-pump), so `mdl_saved` (and the momentum slope
                    // derived from it) carries a bounded greedy-in-arrival accounting artifact across a resume (measured
                    // ~23 mbits / 0.00003% — the Loom's documented MDL gap, never zero). The mesh reads neither for
                    // any decision (no momentum STOP — unlike the trunk), so it is telemetry-only; determinism is exact.
                    sharedLoom.Resplice(main);
                    foreach (Neuron node in nodes) node.Loom.Resplice(node.Stimuli);
                    journal.Consolidation(step, $"aestivation · shed {shedN} (learned → event byte log) · dropped {dropN} (stale unvested dreams) · residents {main.Count} · view {main.ByteLength}B");
                }
                // ── RE-HARVEST `shared` UNCONDITIONALLY ──  the pre-Resplice re-priced the loom whether or not anything
                // evacuated (a aestivation with no shed/drop STILL re-weighs vests), and Resplice re-parses the view so the
                // harvested compressed sequence can move. So the checkpoint's seen-flag anchor + the next step's read
                // subject must be the POST-aestivation grammar on EVERY aestivation the loom was re-priced — else a checkpoint
                // landing on a no-evacuation aestivation would carry a `shared` that a resume (which re-harvests off the
                // Loaded loom) would not reproduce.
                shared = sharedLoom.Result(main);
                if (shedN + dropN > 0)
                    Trace.Mesh.Boundary("aestivation", $"step={step} shed={shedN} drop={dropN} · residents={main.Count} shed_total={main.ShedCount} dropped_total={main.DroppedCount} · view={main.ByteLength}B rules={shared.Rules.Length}");
                phConsolidationPhase.Dispose();
            }

            // ── CHECKPOINT ──  the safe-to-kill law: snapshot the whole mesh (shared tape · journal · per-node
            // metabolism + drain edges + loop locals) atomically to checkpoint.bin at the config cadence. Outside the
            // step reaper — durability's cost is not the machine's, so it never trips the step.slow alarm. `step + 1`
            // is the step the resumed loop executes first (this step is DONE); the curve/journal horizons are the
            // flushed byte lengths a resume truncates back to (kill-orphans past them are shed).
            if (cfg.CheckpointEvery > 0 && (step + 1) % cfg.CheckpointEvery == 0)
            {
                PhaseScope phCkpt = Trace.MeshPhase("ckpt");
                curveW.Flush();
                MeshSnap snap = new(step + 1, totalVested, new FileInfo(run.PathOf("curve.tsv")).Length, new FileInfo(run.PathOf("journal.log")).Length);
                long bytes = MeshCheckpoint.Save(run, MeshCheckpoint.Encode(cfg, world.CorpusBytes, world.GetPoolCounts(), snap, main, journal, nodes, drained, mixed, ingested, reads, sharedLoom, shared, mesh));
                Trace.Mesh.Boundary("ckpt", $"step {step + 1} · {bytes}B · main {main.Count} spans · journal {journal.LineCount} lines → {MeshCheckpoint.FileName}");
                phCkpt.Dispose();
            }
        }

        // ── PHASE 4 · LAND ──  curve.tsv + journal.log are ALREADY on disk (incremental, the safe-to-kill law); land
        // each node's final grammar + a sample, and cut ONE FINAL checkpoint at the horizon so a landed run resumes
        // (extend --steps to drive it deeper without re-running from zero). The curve's vest column is the readout: a
        // sealed single node vests ~0 (no cross-source witness beyond its own corpus); the fan-out vests the peer-
        // corroborated claims — the anti-echo made measurable.
        if (cfg.CheckpointEvery > 0)
        {
            curveW.Flush();
            // the horizon's shared grammar — the seen-flag anchor. Harvest the STANDING shared loom (its greedy-in-
            // arrival grammar is what a resume re-derives from the serialized entry journal — a batch Engine.Induce
            // would land a DIFFERENT grammar and break the seen-flag).
            sharedLoom.SpliceNew(main); sharedLoom.Pump();
            RePairResult sharedFinal = sharedLoom.Result(main);
            MeshSnap snap = new(cfg.Steps, totalVested, new FileInfo(run.PathOf("curve.tsv")).Length, new FileInfo(run.PathOf("journal.log")).Length);
            MeshCheckpoint.Save(run, MeshCheckpoint.Encode(cfg, world.CorpusBytes, world.GetPoolCounts(), snap, main, journal, nodes, drained, mixed, ingested, reads, sharedLoom, sharedFinal, mesh));
        }
        StringBuilder sb = new();
        for (int i = 0; i < nNodes; i++)
        {
            world.GetStimuli(nodes[i].ID, out List<Tape> stimuli);
            NeuronRead finalRead = nodes[i].InduceFrom(stimuli);
            RePairResult g = finalRead.Grammar;
            sb.AppendLine($"── node{i} ({Path.GetFileName(cfg.CorpusPaths[i])}) · {g.Rules?.Length ?? 0} rules ──");
            sb.AppendLine(DumpGrammar(g));
            run.Write($"sample.node{i}.txt", Encoding.UTF8.GetString(nodes[i].Generate(cfg.BlockLen, cfg.Seed)));
        }
        run.Write("grammars.txt", sb.ToString());
        if (cfg.MeshHomeo) run.Write("mesh-homeostat.txt", mesh.Report());   // the proprioceptive throttle's land readout — did it hold the basin (armed arm only, so the off arm's run dir stays artifact-identical)
        run.Write("weft-summary.txt", weft.Report());
        Trace.Note($"  → main {main.Count} spans (real {main.RealCount} · dream {main.ReplayCount} · VESTED {main.ReflectedReplayCount}) · Σvest {totalVested} across the run · curve.tsv landed");
        Trace.Note($"  ⇒ the combustion readout: {(totalVested > 0 ? $"{totalVested} cross-source reflections — the peers reflected each other (the anti-echo the sealed node could not be)" : "0 reflections — sealed (single-node control, or the masks carry no cross-reflection)")}");
        return 0;
    }

    // ── the intake pool ──  corpus lines → the un-ingested span POOL (every 10th line held out, off-tape). Mirrors
    // Farm.SplitPool / Cortex.SplitPool exactly (the same held-out cadence, so a node's pool is the fused loop's pool).
    private static (List<byte[]> Pool, byte[] Heldout) SplitPool(byte[] corpus)
    {
        List<byte[]> pool = new();
        List<byte> held = new();
        int line = 0;
        foreach (ReadOnlyMemory<byte> mem in Engine.SplitLines(corpus))
        {
            if (line++ % 10 == 9) { held.AddRange(mem.Span); held.Add((byte)'\n'); continue; }
            pool.Add(mem.ToArray());
        }
        return (pool, held.ToArray());
    }

    // accrete one pool span onto the shared tape as a REAL corpus span, marking it ingested (the scaffold drain).
    // Source "corpus" (not the node's name): a node's domain seed is WORLD contact — born evidence, and a DIFFERENT
    // source than any node, so it is the corpus-vs-node witness the single-node arm already vests on.
    private static void IngestSpan(List<byte[]> pool, Tape tape, Journal journal, bool[] ingested, int i)
    {
        if (ingested[i]) return;
        ingested[i] = true;
        TapePacketCreator.AppendCorpusItem(tape, journal, step: 0, pool, i);
    }

    private static string DumpGrammar(RePairResult r)
    {
        if (r.Rules is null || r.Rules.Length == 0) return "(empty grammar)\n";
        StringBuilder sb = new();
        sb.AppendLine($"grammar · {r.Compressed.Length} symbols + {r.Rules.Length} rules · Δmdl {r.TotalSavings}");
        int shown = 0;
        foreach (int i in Enumerable.Range(0, r.Rules.Length))
        {
            if (shown++ >= 40) { sb.AppendLine($"  …+{r.Rules.Length - 40} more"); break; }
            uint nt = Symbol.FirstNonterminal + (uint)i;
            byte[] exp = Reconstruct.Expand(r.Rules, [new Symbol(nt)]);
            string text = Encoding.UTF8.GetString(exp).Replace("\n", "\\n");
            sb.AppendLine($"  N{nt,-6} {(text.Length > 80 ? text[..80] : text)}");
        }
        return sb.ToString();
    }
}
