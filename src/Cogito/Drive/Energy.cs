namespace Cogito;

using Cogito.Grammar;
using Cogito.Induct;

// ── THE ENERGY POLICY ──  the IGenerator zoo is samplers in
// costume: MetabolicWalk = a repetition penalty · CouplingWalk warm/cool = a temperature schedule · MarkovWalk =
// the base conditional · McmcWalk = alt inference. NodeBirth alone is different in KIND — a model-edit DURING
// decode (the transparent-latent move no opaque sampler makes). So they collapse to ONE field over the couplings:
//
//   E(next | ctx, state) = w_φ·coupling + w_seq·transition + w_nov·(−recency) + w_depth·composition + w_noise·ε(seed)
//
// one annealed sampler over it (warm body / cool tail — CouplingGenerator's proven shape) + the COMPOSITION
// OPERATOR (chain-mint via Forge, affinity-floor-gated). One field, three verbs: SAMPLE it (speak) · CLIMB it
// (compose) · SORT by it (sleep). The old walks survive only as parameter PRESETS = kill-line arms, then delete.
//
// EMERGENCE: the weights ride the WeightController homeostat — Distinct/NovelChain sag → w_nov rises (the
// machine BECOMES MetabolicWalk when it must); MaxSpan plateau → w_depth rises (becomes NodeBirthWalk); post-grok
// → cool. GUARDRAILS (scar tissue): the controller reads signals the sampler does NOT optimize (Distinct/NovelChain
// as homeostat inputs, NEVER energy terms — the longdrive Goodhart, where xc soared while the thread collapsed);
// gains fixed + minimal first (Thauten's thermostat "changed a label, not the intake"); noise is a simple FLOOR,
// not a regulated organ (the noise probe: "regulation = decoration; jitter alone did it").

/// The energy WEIGHTS — the anisotropy knobs of the one field. Each old walk is a named PRESET (a point in this
/// space); the WeightController rides these during a drive (the machine BECOMES the walk its reads demand). The
/// field names ARE the terms: `Phi` = w_φ, `Transition` = w_seq, `Novelty` = w_nov, `Depth` = w_depth,
/// `Noise` = w_noise.
public readonly record struct Weights(double Phi, double Transition, double Novelty, double Depth, double Noise)
{
    // ── PRESETS = the IGenerator zoo as parameter points (kill-line arms; deleted once the field is proven) ──
    public static readonly Weights Metabolic = new(Phi: 0, Transition: 1, Novelty: 1, Depth: 0, Noise: 0);   // the proven anti-collapse default (novelty-decay reweight)
    public static readonly Weights Markov    = new(Phi: 0, Transition: 1, Novelty: 0, Depth: 0, Noise: 0);   // the base conditional, metabolism-free
    public static readonly Weights Coupling  = new(Phi: 1, Transition: 0, Novelty: 0, Depth: 0, Noise: 0);   // the MEANING field (PPMI coherence, warm/cool)
    public static readonly Weights NodeBirth = new(Phi: 1, Transition: 0, Novelty: 0, Depth: 1, Noise: 0);   // MEANING + the composition operator (deep-unit reach)

    /// The ADAPTIVE start (`--energy energy`) — every term live so the WeightController can ride in ANY direction
    /// from one balanced point (coupling is the meaning backbone; transition/novelty/depth/noise are the nudges the
    /// reads recruit). This is the ONLY preset the controller moves; the named presets above are pinned kill-line arms.
    public static readonly Weights Adaptive  = new(Phi: 1, Transition: 1, Novelty: 1, Depth: 0.5, Noise: 0.1);

    /// The preset for a CLI/config name (the `--gen`/`--energy` string). `energy`/`adaptive` = the ride; a named
    /// walk = its pinned weight-point; unknown ⇒ the proven metabolic default.
    public static Weights Preset(string name) => name switch
    {
        "markov"            => Markov,
        "coupling"          => Coupling,
        "nodebirth"         => NodeBirth,
        "energy" or "adaptive" => Adaptive,
        _                   => Metabolic,
    };

    /// Whether a `--energy` name asks the WeightController to RIDE (the field adapts) vs pin a fixed weight-point.
    public static bool IsAdaptive(string name) => name is "energy" or "adaptive";
}

public readonly record struct EnergyInstallRevisionApplyReceipt(
    GrammarRevisionID Revision,
    GrammarRevisionID CountRevision,
    bool RulesChanged,
    bool CountsChanged,
    bool SequenceRebuilt,
    bool CountsRebuilt);

internal enum EnergyWeightActions : byte
{
    Relax,
    IncreaseNovelty,
    IncreaseDepth,
    IncreaseNoveltyAndDepth,
    CoolNoise,
    IncreaseNoveltyAndCoolNoise,
    IncreaseDepthAndCoolNoise,
    IncreaseNoveltyDepthAndCoolNoise,
}

/// THE ENERGY POLICY — the unified generator behind the IGenerator seam. ONE annealed MRF sampler over
/// E(next|ctx,state), the Weights
/// selecting the anisotropy. Every step: learn couplings, forge composed deep units iff the field reaches for
/// depth (the composition operator), build the Markov transition evidence iff the field consults it, then relax.
/// Three verbs, one field: SAMPLE it (`Generate` — speak) · CLIMB it (`Compose` — the model-edit-during-decode) ·
/// SORT by it (Seriate.Seriate — the third verb, pass-2d). The old walks survive only as `Weights` presets = the
/// kill-line arms; the field at Weights.Coupling is byte-identical to CouplingWalk (parity by construction).
public sealed class EnergyPolicy : IGenerator
{
    private readonly Weights _w;
    private readonly double _affFloor;
    private readonly LineModel? _frozenLines;   // the REAL corpus's line-length model (drive-supplied) — used verbatim, immune to the per-stride drift toward fragmentation; null ⇒ per-stride from the accreted grammar (legacy)

    // ── the per-stride cache (O(Δ): the grammar changes only on the drive's stride-gated re-induce / sleep, NOT
    //    per step, so its Rules-array IDENTITY keys the cache — the same ReferenceEquals discipline FlatPool /
    //    GrokBell / Reads use. Learn + the two scorers + the Markov evidence + the forged composed vocab were all
    //    rebuilt EVERY Generate call though none of them depend on the step; now they are rebuilt once per grammar).
    //    Byte-identical: the cached values are the SAME deterministic functions of the (unchanged) grammar, and the
    //    composition operator is forged into a CLONE so the un-composed base survives the depth=0 path. ──
    private GrammarRule[]? _cachedRules;
    private Couplings? _base;               // couplings over the current grammar (un-composed — the depth=0 sampler's cp)
    private Scorer? _rich, _robust;         // the two PPMI views (built from _base, pre-compose — as the sampler reads them)
    private CombinedScore? _combined;       // the merged 0.5·rich+1·robust CSR — built once per stride, shared by the forge affinity AND the sampler (identical merge; two builds were pure redundancy)
    private Transitions? _trans;            // the Markov evidence (built once; the field consults it iff w_seq≠0)
    private Couplings? _composed;           // _base + the composition operator forged in (a clone; built lazily on the first depth>0 step)
    private CouplingGenerator? _gen;        // the sampler MODEL over the current cp — vocab/CDF/repair-pools/boundary-maps/merged-edge are all cp-derived (stride-stable), so the generator is CACHED per cp and only re-armed with the per-step field (the ctor precompute was rebuilt every Generate though nothing in it depends on the step)
    private Couplings? _genCp;              // the cp `_gen` was built over — base vs the composed clone (depth toggles the source; a mismatch rebuilds)
    private LineModel? _lines;              // the grammar's learned line-length distribution — line-aware minting (spans stay line-bounded post-barrier); stride-stable, rebuilt with the cache
    private GrammarSequence? _publishedSequence;
    private GrammarShape? _boundShape;
    private CouplingCounts? _publishedCounts;
    private ScorerMaterialization? _publishedScorers;
    private const int CompositionMaxDepth = 8;
    private const int CompositionSeedCount = 400;
    private List<Symbol>? _compositionSymbols;
    private Dictionary<ulong, int>? _compositionSubseqCounts;
    private HashSet<ulong>? _compositionSubseqs;
    private SortedSet<SeedFrequency>? _compositionSeeds;
    private Dictionary<uint, SeedFrequency>? _compositionSeedByUnit;

    private readonly record struct SeedFrequency(int Count, uint Unit);

    private sealed class SeedFrequencyComparer : IComparer<SeedFrequency>
    {
        public static readonly SeedFrequencyComparer Instance = new();

        public int Compare(SeedFrequency x, SeedFrequency y)
            => x.Count != y.Count ? y.Count.CompareTo(x.Count) : x.Unit.CompareTo(y.Unit);
    }

    public string Name => "energy";
    /// The policy's resting weights (the preset, or the Adaptive start the controller rides). The drive overrides
    /// these per-step with the controller's live weights; other callers (Farm A/B, sample.txt) use these as-is.
    public Weights Weights => _w;
    /// The stride cache's Markov evidence — the memstat census reads its sealed CSR mass (null until first Generate).
    internal Transitions? TransEvidence => _trans;
    internal CouplingCounts? PublishedCounts => _publishedCounts;
    internal ScorerMaterialization? PublishedScorers => _publishedScorers;

    /// Construct from a preset name + the node-birth affinity floor (the DEPTH lever, 0→4 monotonically deepens
    /// the invented units). `energy`/`adaptive` starts at Weights.Adaptive (the controller then rides it).
    public EnergyPolicy(string preset, double affFloor = 1.0, LineModel? frozenLines = null)
    {
        _w = Weights.Preset(preset);
        _affFloor = affFloor;
        _frozenLines = frozenLines;
    }

    /// Bind the sampler to Cortex's publication-owned analysis plane. The shape owns the
    /// incremental GrammarSequence; Energy must not build a second O(total) expansion/count
    /// basis for the same revision. Standalone callers leave this unbound and retain the
    /// self-contained publication path.
    public void BindGrammarShape(GrammarShape shape)
    {
        _boundShape = shape ?? throw new ArgumentNullException(nameof(shape));
    }

    /// Adopt a typed publication. Raw evidence moves locally with sequence edits; the
    /// global scorer CSR is rebuilt only when the count revision changes. Rule-only
    /// publications invalidate the unit/generator model but retain scorer products.
    public EnergyInstallRevisionApplyReceipt Apply(InstallRevision publication)
    {
        GrammarSequence? previousSequence = _publishedSequence;
        bool sequenceAppliedByShape = false;
        if (_boundShape is not null)
        {
            if (_boundShape.Revision == publication.ParentRevision)
            {
                _boundShape.Apply(publication);
                sequenceAppliedByShape = true;
            }
            else if (_boundShape.Revision != publication.Revision)
                throw new InvalidOperationException($"energy grammar shape revision {_boundShape.Revision} cannot accept publication {publication.ParentRevision}->{publication.Revision}");
            else
                sequenceAppliedByShape = true;
            _publishedSequence = _boundShape.Sequence;
        }
        bool initial = _publishedSequence is null || _publishedCounts is null
            || !ReferenceEquals(previousSequence, _publishedSequence);
        if (!initial && _publishedSequence!.Revision == publication.Revision)
        {
            return new EnergyInstallRevisionApplyReceipt(
                publication.Revision,
                _publishedCounts!.CountRevision,
                RulesChanged: false,
                CountsChanged: false,
                SequenceRebuilt: false,
                CountsRebuilt: false);
        }
        GrammarAnalysisApplyReceipt sequenceReceipt;
        CouplingCountsApplyReceipt countReceipt;
        if (initial)
        {
            _publishedSequence ??= GrammarSequence.BuildFromSnapshot(publication.Snapshot);
            _publishedCounts = CouplingCounts.Build(_publishedSequence);
            sequenceReceipt = new GrammarAnalysisApplyReceipt(true, true, publication.Delta.SequenceEdits.Length);
            countReceipt = new CouplingCountsApplyReceipt(true, true, true, publication.Revision, publication.Delta.SequenceEdits.Length);
        }
        else
        {
            sequenceReceipt = sequenceAppliedByShape
                ? new GrammarAnalysisApplyReceipt(false, publication.Delta.SequenceEdits.Length != 0, publication.Delta.SequenceEdits.Length)
                : _publishedSequence!.Apply(publication);
            countReceipt = _publishedCounts!.Apply(publication);
        }

        bool rulesChanged = initial || publication.Reset != GrammarResetKinds.None
            || publication.Delta.AddedRules.Length != 0 || publication.Delta.RemovedRules.Length != 0;
        bool countsChanged = initial || countReceipt.CountRevisionChanged;
        long msTrans = 0;
        if (countsChanged) { long tTrans = Trace.NowTicks; _trans = _publishedCounts!.BuildTransitions(); msTrans = Trace.ElapsedMs(tTrans); }  // the CSR-seal twin of the stride path's timed Transitions.Build
        if (rulesChanged || countsChanged)
        {
            bool reset = initial || publication.Reset != GrammarResetKinds.None || publication.Delta.RemovedRules.Length != 0;
            if (reset || _base is null)
                _base = Couplings.FromCounts(publication.Snapshot.Rules, _publishedCounts!);
            else
                _base.ApplyInstallRevision(publication);
            _composed = null;
            if (countsChanged) { _gen = null; _genCp = null; }
            if (_frozenLines is null && (reset || publication.Delta.SequenceEdits.Length != 0))
                _lines = new LineModel(publication.Snapshot.ToRePairResult());
        }
        if (countsChanged)
        {
            _publishedScorers = new ScorerMaterialization(_base!, _publishedCounts!.CountRevision);
            _rich = _publishedScorers.Rich;
            _robust = _publishedScorers.Robust;
            _combined = _publishedScorers.Combined;
        }
        UpdateCompositionEvidence(publication, initial);
        _cachedRules = publication.Snapshot.Rules;
        Trace.Energy.Boundary("publication", $"revision={publication.Revision} countRevision={_publishedCounts!.CountRevision} rulesChanged={(rulesChanged ? "yes" : "no")} countsChanged={(countsChanged ? "yes" : "no")} sequenceRebuilt={(sequenceReceipt.Rebuilt ? "yes" : "no")} sequenceShared={(_boundShape is not null ? "yes" : "no")} trans={msTrans}");
        return new EnergyInstallRevisionApplyReceipt(publication.Revision, _publishedCounts.CountRevision, rulesChanged, countsChanged, sequenceReceipt.Rebuilt, countReceipt.Rebuilt);
    }

    public byte[] Generate(InstallRevision publication, int count, ulong seed, Metabolism metabolism)
        => GenerateSpan(publication, count, seed, metabolism, _w).ToArray();

    public ReadOnlySpan<byte> GenerateSpan(InstallRevision publication, int count, ulong seed, Metabolism metabolism)
        => GenerateSpan(publication, count, seed, metabolism, _w);

    public byte[] Generate(InstallRevision publication, int count, ulong seed, Metabolism metabolism, Weights w)
        => GenerateSpan(publication, count, seed, metabolism, w).ToArray();

    public ReadOnlySpan<byte> GenerateSpan(InstallRevision publication, int count, ulong seed, Metabolism metabolism, Weights w)
    {
        Apply(publication);
        Couplings cp = w.Depth > 0 ? EnsurePublishedComposed(publication.Snapshot, w) : _base!;
        if (!ReferenceEquals(cp, _genCp))
        {
            _gen = new CouplingGenerator(cp, _rich!, _robust!, lines: _lines, score: _combined);
            _genCp = cp;
        }
        var field = new FieldTerms(w, w.Transition != 0 ? _trans : null, metabolism, seed);
        return _gen!.GenerateSpan(count, seed, field);
    }

    /// Samples into the cached generator's output memory. The memory is
    /// generator-owned and remains valid until the next sample.
    public ReadOnlyMemory<byte> GenerateMemory(InstallRevision publication, int count, ulong seed, Metabolism metabolism, Weights w)
    {
        ReadOnlySpan<byte> bytes = GenerateSpan(publication, count, seed, metabolism, w);
        return _gen is null ? ReadOnlyMemory<byte>.Empty : _gen.OutputMemory(bytes.Length);
    }

    private Couplings EnsurePublishedComposed(GrammarSnapshot snapshot, Weights w)
    {
        if (_composed is null)
            _composed = ComposeInto(_base!.Clone(), _rich!, _robust!, snapshot, _affFloor);
        return _composed;
    }

    private void UpdateCompositionEvidence(InstallRevision publication, bool initial)
    {
        bool reset = initial || publication.Reset != GrammarResetKinds.None
            || publication.Delta.RemovedRules.Length != 0
            || _compositionSymbols is null || _compositionSubseqCounts is null
            || _compositionSeeds is null || _compositionSeedByUnit is null;
        if (reset)
        {
            _compositionSymbols = new List<Symbol>(publication.Snapshot.Compressed);
            _compositionSubseqCounts = new Dictionary<ulong, int>();
            _compositionSubseqs = new HashSet<ulong>();
            BuildCompositionSubsequences(_compositionSymbols, _compositionSubseqCounts, _compositionSubseqs);
            _compositionSeeds = new SortedSet<SeedFrequency>(SeedFrequencyComparer.Instance);
            _compositionSeedByUnit = new Dictionary<uint, SeedFrequency>();
            foreach (var (unit, count) in _publishedCounts!.Marginals)
            {
                SeedFrequency seed = new(count, unit);
                _compositionSeeds.Add(seed);
                _compositionSeedByUnit.Add(unit, seed);
            }
            return;
        }

        if (publication.Delta.SequenceEdits.Length == 0) return;
        List<uint> affected = new();
        foreach (var edit in publication.Delta.SequenceEdits)
        {
            List<Symbol> symbols = _compositionSymbols!;
            int oldCount = symbols.Count;
            int oldEnd = edit.Start + edit.RemovedLength;
            for (int i = edit.Start; i < oldEnd; i++)
            {
                if ((uint)i < (uint)oldCount) affected.Add(symbols[i].Value);
            }
            foreach (var symbol in edit.Inserted) affected.Add(symbol.Value);
            RemoveCompositionSubsequences(symbols, edit.Start, edit.RemovedLength,
                _compositionSubseqCounts!, _compositionSubseqs!);
            symbols.RemoveRange(edit.Start, edit.RemovedLength);
            symbols.InsertRange(edit.Start, edit.Inserted);
            AddCompositionSubsequences(symbols, edit.Start, edit.Inserted.Length,
                _compositionSubseqCounts!, _compositionSubseqs!);
        }

        foreach (uint unit in affected) RefreshCompositionSeed(unit);
    }

    private uint[] ReadCompositionSeeds(uint baseBoundary)
    {
        if (_compositionSeeds is null) return [];
        var seeds = new uint[Math.Min(CompositionSeedCount, _compositionSeeds.Count)];
        int at = 0;
        foreach (SeedFrequency seed in _compositionSeeds)
        {
            if (seed.Unit >= baseBoundary) continue;
            seeds[at++] = seed.Unit;
            if (at == seeds.Length) break;
        }
        if (at != seeds.Length) Array.Resize(ref seeds, at);
        return seeds;
    }

    private void RefreshCompositionSeed(uint unit)
    {
        SortedSet<SeedFrequency> seeds = _compositionSeeds!;
        Dictionary<uint, SeedFrequency> byUnit = _compositionSeedByUnit!;
        if (byUnit.Remove(unit, out SeedFrequency prior)) seeds.Remove(prior);
        int count = _publishedCounts!.Marginals.GetValueOrDefault(unit);
        if (count > 0)
        {
            SeedFrequency current = new(count, unit);
            seeds.Add(current);
            byUnit.Add(unit, current);
        }
    }

    private static void BuildCompositionSubsequences(List<Symbol> symbols,
        Dictionary<ulong, int> counts, HashSet<ulong> values)
    {
        for (int start = 0; start < symbols.Count; start++)
        {
            ulong hash = 1469598103934665603UL;
            for (int length = 1; length <= CompositionMaxDepth && start + length <= symbols.Count; length++)
            {
                hash = Forge.Fold(hash, symbols[start + length - 1].Value);
                if (length >= 2) AddCompositionHash(hash, counts, values);
            }
        }
    }

    private static void RemoveCompositionSubsequences(List<Symbol> symbols, int start, int removedLength,
        Dictionary<ulong, int> counts, HashSet<ulong> values)
    {
        int end = start + removedLength;
        int first = Math.Max(0, start - CompositionMaxDepth + 1);
        int last = Math.Min(symbols.Count - 2, removedLength == 0 ? start - 1 : end - 1);
        for (int at = first; at <= last; at++)
            for (int length = 2; length <= CompositionMaxDepth && at + length <= symbols.Count; length++)
            {
                bool touches = removedLength == 0
                    ? at < start && at + length > start
                    : at < end && at + length > start;
                if (touches) RemoveCompositionHash(HashCompositionWindow(symbols, at, length), counts, values);
            }
    }

    private static void AddCompositionSubsequences(List<Symbol> symbols, int start, int insertedLength,
        Dictionary<ulong, int> counts, HashSet<ulong> values)
    {
        int end = start + insertedLength;
        int first = Math.Max(0, start - CompositionMaxDepth + 1);
        int last = Math.Min(symbols.Count - 2, insertedLength == 0 ? start - 1 : end - 1);
        for (int at = first; at <= last; at++)
            for (int length = 2; length <= CompositionMaxDepth && at + length <= symbols.Count; length++)
            {
                bool touches = insertedLength == 0
                    ? at < start && at + length > start
                    : at < end && at + length > start;
                if (touches) AddCompositionHash(HashCompositionWindow(symbols, at, length), counts, values);
            }
    }

    private static ulong HashCompositionWindow(List<Symbol> symbols, int start, int length)
    {
        ulong hash = 1469598103934665603UL;
        for (int i = 0; i < length; i++) hash = Forge.Fold(hash, symbols[start + i].Value);
        return hash;
    }

    private static void AddCompositionHash(ulong hash, Dictionary<ulong, int> counts, HashSet<ulong> values)
    {
        counts[hash] = counts.GetValueOrDefault(hash) + 1;
        values.Add(hash);
    }

    private static void RemoveCompositionHash(ulong hash, Dictionary<ulong, int> counts, HashSet<ulong> values)
    {
        int count = counts[hash] - 1;
        if (count == 0) { counts.Remove(hash); values.Remove(hash); }
        else counts[hash] = count;
    }

    /// SAMPLE the field at the policy's resting weights (the IGenerator contract — the final sample.txt). The drive
    /// uses the Weights overload so the controller's live nudges take effect each step.
    public byte[] Generate(RePairResult grammar, int count, ulong seed, Metabolism metabolism)
        => GenerateSpan(grammar, count, seed, metabolism, _w).ToArray();

    public ReadOnlySpan<byte> GenerateSpan(RePairResult grammar, int count, ulong seed, Metabolism metabolism)
        => GenerateSpan(grammar, count, seed, metabolism, _w);

    /// SAMPLE the field at the GIVEN weights — the ONE annealed sampler over E(·), warm body / cool-commit tail
    /// (CouplingGenerator's proven shape), each term scaled by `w`. The drive feeds the (possibly controller-
    /// nudged) live weights here, so the machine BECOMES the walk its reads demand. Deterministic:
    /// same grammar + seed + weights ⇒ byte-identical bytes (the Vow) — a seeded LCG relaxation, hash-based noise.
    public byte[] Generate(RePairResult grammar, int count, ulong seed, Metabolism metabolism, Weights w)
        => GenerateSpan(grammar, count, seed, metabolism, w).ToArray();

    public ReadOnlySpan<byte> GenerateSpan(RePairResult grammar, int count, ulong seed, Metabolism metabolism, Weights w)
    {
        if (grammar.Compressed is null || grammar.Compressed.Length < 4) return [];
        if (!ReferenceEquals(grammar.Rules, _cachedRules))                       // re-induce / sleep handed us a fresh grammar → rebuild the stride cache
        {
            long t0 = Trace.NowTicks, t = t0;
            _base     = Couplings.Learn(grammar);
            long msLearn = Trace.ElapsedMs(t); t = Trace.NowTicks;
            _rich     = _base.BuildScorer(minCocount: 1);
            long msRich = Trace.ElapsedMs(t); t = Trace.NowTicks;
            _robust   = _base.BuildScorer(minCocount: 5);
            long msRobust = Trace.ElapsedMs(t); t = Trace.NowTicks;
            _combined = new CombinedScore(_rich, _robust);
            long msMerge = Trace.ElapsedMs(t); t = Trace.NowTicks;
            _trans    = Transitions.Build(grammar.Compressed);                   // built once per grammar; the field reads it iff w_seq≠0
            long msTrans = Trace.ElapsedMs(t); t = Trace.NowTicks;
            _lines    = _frozenLines ?? new LineModel(grammar);                   // the learned line-length cadence — the drive's FROZEN real-corpus model (drift-immune), or per-stride from the accreted grammar if unset
            _composed = null;                                                    // forged lazily on the first depth>0 step of this grammar
            _gen      = null; _genCp = null;                                      // the cached sampler model is stale (fresh couplings) — rebuilt on the next sample
            _cachedRules = grammar.Rules;
            Trace.Energy.Boundary("stride", $"rules={grammar.Rules.Length} comp={grammar.Compressed.Length} ms={Trace.ElapsedMs(t0)} learn={msLearn} rich={msRich} robust={msRobust} merge={msMerge} trans={msTrans} lines={Trace.ElapsedMs(t)}");
        }
        Couplings cp;
        if (w.Depth > 0)                                                         // CLIMB — the composition operator forged into a clone (once), reused across the stride
        {
            if (_composed is null)
            {
                long t0 = Trace.NowTicks;
                _composed = ComposeIntoLegacy(_base!.Clone(), _rich!, _robust!, grammar, _affFloor);
                Trace.Energy.Boundary("compose", $"rules={grammar.Rules.Length} ms={Trace.ElapsedMs(t0)}");
            }
            cp = _composed;
        }
        else
            cp = _base!;                                                         // depth off → the un-composed base
        if (!ReferenceEquals(cp, _genCp))                                        // the sampler model is cp-derived — rebuild only when the source couplings change (stride re-induce, or a depth toggle across the base/composed boundary)
        {
            long t0 = Trace.NowTicks;
            _gen = new CouplingGenerator(cp, _rich!, _robust!, lines: _lines, score: _combined); _genCp = cp;
            Trace.Energy.Boundary("model", $"rules={grammar.Rules.Length} ms={Trace.ElapsedMs(t0)}");
        }
        var field = new FieldTerms(w, w.Transition != 0 ? _trans : null, metabolism, seed);   // Trans null when off (byte-identical to the pre-cache field)
        long tS = Trace.NowTicks;
        var bytes = _gen!.GenerateSpan(count, seed, field);
        Trace.Energy.Boundary("sample", $"rules={grammar.Rules.Length} ms={Trace.ElapsedMs(tS)} {_gen.StatLine()}");
        return bytes;
    }

    /// Samples into the cached generator's output memory. The memory is
    /// generator-owned and remains valid until the next sample.
    public ReadOnlyMemory<byte> GenerateMemory(RePairResult grammar, int count, ulong seed, Metabolism metabolism, Weights w)
    {
        ReadOnlySpan<byte> bytes = GenerateSpan(grammar, count, seed, metabolism, w);
        return _gen is null ? ReadOnlyMemory<byte>.Empty : _gen.OutputMemory(bytes.Length);
    }

    // forge the composition operator into `cp` (a clone) and hand it back — the cache's composed-couplings builder.
    // The affinity is built HERE over the stride-shared _combined (the same merge the sampler reads) instead of
    // letting Compose's default path build a second identical CombinedScore.
    private Couplings ComposeInto(Couplings cp, Scorer rich, Scorer robust, GrammarSnapshot grammar, double affFloor)
    {
        var (vocab, _) = cp.Vocabulary();
        var (prof, idf) = NodeBirthWalk.IdProfiles(cp, vocab);
        Compose(cp, rich, robust, grammar.Rules, grammar.Compressed, affFloor,
            _compositionSubseqs, ReadCompositionSeeds(Symbol.FirstNonterminal + (uint)grammar.Rules.Length),
            affinity: new CouplingAffinity(_combined!, prof, idf));
        return cp;
    }

    private Couplings ComposeIntoLegacy(Couplings cp, Scorer rich, Scorer robust, RePairResult grammar, double affFloor)
    {
        var (vocab, _) = cp.Vocabulary();
        var (prof, idf) = NodeBirthWalk.IdProfiles(cp, vocab);
        Compose(cp, rich, robust, grammar.Rules, grammar.Compressed, affFloor, null, null,
            affinity: new CouplingAffinity(_combined!, prof, idf));
        return cp;
    }

    /// CLIMB the field (compose) — THE COMPOSITION OPERATOR: the one move no opaque sampler makes, a model-edit
    /// DURING decode. Lifts NodeBirth's forge pipeline: greedy φ+idf affinity CHAINS grown to the affinity floor
    /// (the depth lever), novelty-gated (minted only if the chain is NOT a contiguous corpus subsequence — an
    /// INVENTION, not a replay), minted into `cp` as atomic deep units the enlarged-vocab sampler then walks. The
    /// IAffinity seam stays open; null = the proven
    /// learned-coupling + id-thread default. Mutates `cp` (mints composed ids); returns them. Deterministic (seeded
    /// forge order, id tie-breaks). This is the exact NodeBirthWalk forge, hoisted so w_depth>0 composes under ANY
    /// preset — not just `nodebirth` — and so sleep/self-model can climb the SAME operator (one field, three verbs).
    public static List<uint> Compose(Couplings cp, Scorer rich, Scorer robust, RePairResult grammar,
        double affFloor, int maxDepth = 8, int nSeeds = 400, IAffinity? affinity = null)
    {
        return Compose(cp, rich, robust, grammar.Rules, grammar.Compressed, affFloor, null, null, maxDepth, nSeeds, affinity);
    }

    private static List<uint> Compose(Couplings cp, Scorer rich, Scorer robust,
        GrammarRule[] rules, Symbol[] compressed, double affFloor,
        HashSet<ulong>? subseqs, uint[]? seeds,
        int maxDepth = CompositionMaxDepth, int nSeeds = CompositionSeedCount, IAffinity? affinity = null)
    {
        uint baseBoundary = Symbol.FirstNonterminal + (uint)rules.Length;
        IAffinity aff = affinity ?? DefaultAffinity(cp, rich, robust, cp.Vocabulary().Vocab);
        subseqs ??= NodeBirthWalk.CorpusSubseqs(compressed, maxDepth);
        seeds ??= NodeBirthWalk.SeedsByFrequency(cp, baseBoundary, nSeeds);
        return new Forge(cp, rich, aff, subseqs, baseBoundary, maxDepth, affFloor).Run(seeds);
    }

    // the proven default affinity — learned coupling (φ_combined) + idf-weighted shared-identifier thread. The
    // binder (an ILlm-backed IAffinity) replaces this in ; Compose takes it as a parameter so nothing else moves.
    private static IAffinity DefaultAffinity(Couplings cp, Scorer rich, Scorer robust, uint[] vocab)
    {
        var (prof, idf) = NodeBirthWalk.IdProfiles(cp, vocab);
        return new CouplingAffinity(new CombinedScore(rich, robust), prof, idf);
    }
}

/// THE WEIGHT CONTROLLER — the homeostat that nudges the Weights during a drive. The machine
/// BECOMES the walk its reads demand: collapse alarms → Novelty rises (become MetabolicWalk); MaxSpan plateaus with
/// a stalled thread → Depth rises (become NodeBirthWalk); post-grok → cool the anneal. It rides ONLY when adaptive
/// (`--energy energy`); a pinned preset returns its weight-point unchanged (the kill-line baseline arms).
///
/// THE GOODHART GUARDRAIL (the longdrive scar — where xc soared while the thread COLLAPSED): `Nudge` reads ONLY the
/// COLLAPSE-ROBUST signals the sampler does NOT optimize — Distinct, NovelChain, the MaxSpan-plateau, coll_frac/
/// df_third, the momentum band, the grok CvZ — and NEVER the energy's own terms (Phi/Transition/Novelty/Depth/
/// Noise). A controller that reads its own objective optimizes the metric and collapses the thing; so the inputs
/// here are exactly the reads that a repetition-collapse DROPS (the metabolism-probe keystone) — orthogonal to the
/// score the field maximizes. Gains fixed + minimal (Thauten's thermostat "changed a label, not the intake");
/// continuous nudges + a relax-to-rest pull, NO regime state machine (the nudges accumulate only while their
/// signal persists, and drain back when it clears — bounded by clamps, never a ratchet).
public sealed class WeightController(Weights initial, bool adaptive = false, double grokCv = 0.20)
{
    public static readonly CortexPolicyID PolicyID = new("energy.weight-actuation");
    public static readonly CortexPolicySchema PolicySchema = new(
        PolicyID, featureCount: 10, actionCount: 8, outcomeCount: 4,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);

    public static PolicyCanonicalStateID CanonicalizePolicyState(
        bool collapse,
        bool plateau,
        bool grokked,
        bool momentumClimbing,
        bool adaptive,
        bool increaseNovelty,
        bool increaseDepth,
        bool coolNoise)
        => PolicyCanonicalStates.Energy(
            PolicyID,
            collapse,
            plateau,
            grokked,
            momentumClimbing,
            adaptive,
            increaseNovelty,
            increaseDepth,
            coolNoise);

    private enum MetricIDs : ushort
    {
        Distinct = 560,
        NovelChain,
        MaximumSpan,
        CollapseFraction,
        ThirdDerivativeFraction,
        Criticality,
        Momentum,
        NoveltyWeight,
        DepthWeight,
        NoiseWeight,
        DistinctDelta = 580,
        NovelChainDelta,
        MaximumSpanDelta,
        CollapseDelta,
    }

    private Weights _w = initial;
    private readonly Weights _rest = initial;              // the resting point the nudges relax toward (the Adaptive start)
    private readonly bool _adaptive = adaptive;
    private readonly double _grokCv = grokCv;
    private readonly Queue<double> _span = new();          // recent maxSpan — the plateau read (the DEPTH stall signal)
    private readonly Queue<int> _distinct = new();         // recent Distinct — the collapse down-trend read
    private readonly Queue<int> _chain = new();            // recent NovelChain — the thread-rising read (gates the depth push)
    private bool _policyOutcomePending;
    private CortexPolicyDecision _policyDecision;
    private int _pendingDistinct;
    private int _pendingNovelChain;
    private double _pendingMaximumSpan;
    private double _pendingCollapseFraction;

    // fixed minimal gains (the proven-safe start) + clamps (bounded, never a ratchet)
    private const double NovGain = 0.10, DepthGain = 0.06, CoolGain = 0.04, Relax = 0.04;
    private const double WMax = 3.0, NoiseMax = 0.3;
    private const int PlateauWin = 8, TrendWin = 6;

    internal readonly record struct WeightControllerCheckpointDelta(
        Weights Current,
        double[] Span,
        int[] Distinct,
        int[] Chain,
        bool PolicyOutcomePending,
        CortexPolicyDecision PolicyDecision,
        int PendingDistinct,
        int PendingNovelChain,
        double PendingMaximumSpan,
        double PendingCollapseFraction)
    {
        internal bool IsEmpty => false;
    }

    public Weights Current => _w;

    internal WeightControllerCheckpointDelta CaptureCheckpointDelta()
        => new(_w, _span.ToArray(), _distinct.ToArray(), _chain.ToArray(), _policyOutcomePending,
            _policyDecision, _pendingDistinct, _pendingNovelChain, _pendingMaximumSpan, _pendingCollapseFraction);

    internal void ApplyCheckpointDelta(in WeightControllerCheckpointDelta delta)
    {
        if (delta.Span.Length > PlateauWin || delta.Distinct.Length > TrendWin || delta.Chain.Length > TrendWin)
            throw new InvalidDataException("weight-controller checkpoint window exceeds bound");
        _w = delta.Current;
        _span.Clear(); foreach (double value in delta.Span) _span.Enqueue(value);
        _distinct.Clear(); foreach (int value in delta.Distinct) _distinct.Enqueue(value);
        _chain.Clear(); foreach (int value in delta.Chain) _chain.Enqueue(value);
        _policyOutcomePending = delta.PolicyOutcomePending;
        _policyDecision = delta.PolicyDecision;
        _pendingDistinct = delta.PendingDistinct; _pendingNovelChain = delta.PendingNovelChain;
        _pendingMaximumSpan = delta.PendingMaximumSpan; _pendingCollapseFraction = delta.PendingCollapseFraction;
    }

    internal void CommitCheckpointDelta() { }

    internal static void WriteCheckpointDelta(CkptWriter writer, in WeightControllerCheckpointDelta delta)
    {
        writer.U8(1);
        writer.F64(delta.Current.Phi); writer.F64(delta.Current.Transition); writer.F64(delta.Current.Novelty); writer.F64(delta.Current.Depth); writer.F64(delta.Current.Noise);
        WriteDoubles(writer, delta.Span); WriteInts(writer, delta.Distinct); WriteInts(writer, delta.Chain);
        writer.Bool(delta.PolicyOutcomePending);
        if (delta.PolicyOutcomePending) { CortexPolicyDecision decision = delta.PolicyDecision; CortexPolicyDecisionCheckpoint.Write(writer, in decision); }
        writer.I32(delta.PendingDistinct); writer.I32(delta.PendingNovelChain); writer.F64(delta.PendingMaximumSpan); writer.F64(delta.PendingCollapseFraction);
    }

    internal static WeightControllerCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown weight-controller checkpoint delta version");
        Weights weights = new(reader.F64(), reader.F64(), reader.F64(), reader.F64(), reader.F64());
        double[] span = ReadDoubles(reader, PlateauWin); int[] distinct = ReadInts(reader, TrendWin); int[] chain = ReadInts(reader, TrendWin);
        bool pending = reader.Bool(); CortexPolicyDecision decision = pending ? CortexPolicyDecisionCheckpoint.Read(reader, PolicyID, PolicySchema.ActionCount) : default;
        return new(weights, span, distinct, chain, pending, decision, reader.I32(), reader.I32(), reader.F64(), reader.F64());
    }

    private static void WriteDoubles(CkptWriter writer, double[] values) { writer.I32(values.Length); foreach (double value in values) writer.F64(value); }
    private static void WriteInts(CkptWriter writer, int[] values) { writer.I32(values.Length); foreach (int value in values) writer.I32(value); }
    private static double[] ReadDoubles(CkptReader reader, int max) { int n = reader.I32(); if (n < 0 || n > max) throw new InvalidDataException("weight-controller window exceeds bound"); double[] values = new double[n]; for (int i = 0; i < n; i++) values[i] = reader.F64(); return values; }
    private static int[] ReadInts(CkptReader reader, int max) { int n = reader.I32(); if (n < 0 || n > max) throw new InvalidDataException("weight-controller window exceeds bound"); int[] values = new int[n]; for (int i = 0; i < n; i++) values[i] = reader.I32(); return values; }

    /// Fold this step's COLLAPSE-ROBUST reads into a weight nudge (never the energy terms — the guardrail above).
    /// `distinct`/`novelChain`/`maxSpan`/`collFrac`/`dfThird`/`cvZ`/`momentumVerdict` are all reads the sampler does
    /// not optimize. Returns the (possibly-nudged) live weights; identity when not adaptive (the pinned preset arm).
    public Weights Nudge(Cortex cortex, int distinct, int novelChain, double maxSpan, double collFrac, double dfThird, double cvZ, string momentumVerdict)
    {
        if (!_adaptive) return _w;

        if (_policyOutcomePending)
        {
            Span<MetricSample> outcomes = stackalloc MetricSample[4]
            {
                new(new MetricID((ushort)MetricIDs.DistinctDelta), NumericValue.FromI64(distinct - _pendingDistinct)),
                new(new MetricID((ushort)MetricIDs.NovelChainDelta), NumericValue.FromI64(novelChain - _pendingNovelChain)),
                new(new MetricID((ushort)MetricIDs.MaximumSpanDelta), NumericValue.FromF64(maxSpan - _pendingMaximumSpan)),
                new(new MetricID((ushort)MetricIDs.CollapseDelta), NumericValue.FromF64(_pendingCollapseFraction - collFrac)),
            };
            bool invariantClean = AreFinite(_w) && maxSpan >= 0 && collFrac is >= 0 and <= 1;
            cortex.ResolvePolicyOutcome(in _policyDecision, outcomes, invariantClean, conservedCost: 1);
            _policyOutcomePending = false;
        }

        double nov = _w.Novelty, depth = _w.Depth, noise = _w.Noise;

        // ── (1) COLLAPSE → Novelty↑ ──  coll_frac is the LEVEL (fraction of recent steps in the repetition basin),
        // df_third the TREND (<1 ⟹ byte-diversity decaying), Distinct's own down-trend the third corroboration. Any of the
        // three recruits novelty — the machine becomes MetabolicWalk exactly when the collapse-robust reads demand it.
        bool distinctSag = Sagging(_distinct, distinct);
        bool increaseNovelty = collFrac > 0.25 || dfThird < 0.85 || distinctSag;

        // ── (2) DEPTH STALL → Depth↑ ──  maxSpan (grammar depth) plateaued over K steps AND the honest thread
        // (NovelChain) is not lengthening ⟹ the pairwise W≤3 horizon is spent → reach for composition (NodeBirthWalk).
        // Requiring BOTH keeps the depth push off a still-climbing drive (a plateau alone can be a between-stride flat).
        _span.Enqueue(maxSpan); Trim(_span, PlateauWin);
        bool threadRising = Rising(_chain, novelChain);
        bool increaseDepth = _span.Count >= PlateauWin && Plateaued(_span) && !threadRising;

        // ── (3) POST-GROK → cool the anneal ──  once the criticality-CV LOCKS (cvZ < grokCv — a real RG fixed point;
        // GrokBell's bell when 2b provides it, the raw RenormStats CvZ otherwise) the structure is FOUND: commit, stop
        // exploring. Exploration in the field is the NOISE floor, so
        // cooling drains it toward 0. Don't cool while still CLIMBING/diverging; NaN CvZ (too few scales) is no lock.
        bool coolNoise = !double.IsNaN(cvZ) && cvZ < _grokCv && momentumVerdict != "CLIMBING";

        int launchpadAction = (increaseNovelty ? 1 : 0) | (increaseDepth ? 2 : 0) | (coolNoise ? 4 : 0);
        Span<MetricSample> features = stackalloc MetricSample[10]
        {
            new(new MetricID((ushort)MetricIDs.Distinct), NumericValue.FromI64(distinct)),
            new(new MetricID((ushort)MetricIDs.NovelChain), NumericValue.FromI64(novelChain)),
            new(new MetricID((ushort)MetricIDs.MaximumSpan), NumericValue.FromF64(maxSpan)),
            new(new MetricID((ushort)MetricIDs.CollapseFraction), NumericValue.FromF64(collFrac)),
            new(new MetricID((ushort)MetricIDs.ThirdDerivativeFraction), NumericValue.FromF64(dfThird)),
            new(new MetricID((ushort)MetricIDs.Criticality), NumericValue.FromF64(cvZ)),
            new(new MetricID((ushort)MetricIDs.Momentum), NumericValue.FromI64(EncodeMomentum(momentumVerdict))),
            new(new MetricID((ushort)MetricIDs.NoveltyWeight), NumericValue.FromF64(_w.Novelty)),
            new(new MetricID((ushort)MetricIDs.DepthWeight), NumericValue.FromF64(_w.Depth)),
            new(new MetricID((ushort)MetricIDs.NoiseWeight), NumericValue.FromF64(_w.Noise)),
        };
        PolicyCanonicalStateID canonicalState = CanonicalizePolicyState(
            collFrac > 0.25,
            increaseDepth,
            !double.IsNaN(cvZ) && cvZ < _grokCv,
            momentumVerdict == "CLIMBING",
            _adaptive,
            increaseNovelty,
            increaseDepth,
            coolNoise);
        _policyDecision = cortex.ChoosePolicyAction(PolicyID, launchpadAction, in canonicalState, features);
        int action = _policyDecision.Action;
        if ((action & 1) != 0) nov += NovGain;
        if ((action & 2) != 0) depth += DepthGain;
        if ((action & 4) != 0) noise -= CoolGain;

        // ── relax toward rest ──  a gentle pull back so a transient alarm nudges without ratcheting forever (the
        // nudge holds only while its signal persists; continuous control, no discrete regime switch).
        nov   += Relax * (_rest.Novelty - nov);
        depth += Relax * (_rest.Depth   - depth);
        noise += Relax * (_rest.Noise   - noise);

        Trim(_distinct, TrendWin, distinct); Trim(_chain, TrendWin, novelChain);
        _w = _w with
        {
            Novelty = Math.Clamp(nov,   0, WMax),
            Depth   = Math.Clamp(depth, 0, WMax),
            Noise   = Math.Clamp(noise, 0, NoiseMax),
        };
        _pendingDistinct = distinct;
        _pendingNovelChain = novelChain;
        _pendingMaximumSpan = maxSpan;
        _pendingCollapseFraction = collFrac;
        _policyOutcomePending = true;
        return _w;
    }

    // checkpoint — the live weights + the three trend windows (rest/adaptive/grokCv are ctor inputs the resume
    // path reconstructs from the config; the nudge history lives entirely in these four).
    public void Save(CkptWriter w)
    {
        w.F64(_w.Phi); w.F64(_w.Transition); w.F64(_w.Novelty); w.F64(_w.Depth); w.F64(_w.Noise);
        Checkpoint.WriteQueue(w, _span);
        Checkpoint.WriteQueue(w, _distinct);
        Checkpoint.WriteQueue(w, _chain);
        w.Bool(_policyOutcomePending);
        if (_policyOutcomePending)
        {
            CortexPolicyDecisionCheckpoint.Write(w, in _policyDecision);
        }
        w.I32(_pendingDistinct); w.I32(_pendingNovelChain); w.F64(_pendingMaximumSpan); w.F64(_pendingCollapseFraction);
    }

    public void Load(CkptReader r)
    {
        _w = new Weights(r.F64(), r.F64(), r.F64(), r.F64(), r.F64());
        Checkpoint.ReadQueue(r, _span);
        Checkpoint.ReadQueue(r, _distinct);
        Checkpoint.ReadQueue(r, _chain);
        _policyOutcomePending = r.Bool();
        if (_policyOutcomePending)
        {
            _policyDecision = CortexPolicyDecisionCheckpoint.Read(r, PolicyID, PolicySchema.ActionCount);
        }
        _pendingDistinct = r.I32(); _pendingNovelChain = r.I32(); _pendingMaximumSpan = r.F64(); _pendingCollapseFraction = r.F64();
    }

    private static bool AreFinite(in Weights weights)
        => double.IsFinite(weights.Phi) && double.IsFinite(weights.Transition)
           && double.IsFinite(weights.Novelty) && double.IsFinite(weights.Depth) && double.IsFinite(weights.Noise)
           && weights.Novelty is >= 0 and <= WMax && weights.Depth is >= 0 and <= WMax
           && weights.Noise is >= 0 and <= NoiseMax;

    private static int EncodeMomentum(string momentum)
        => momentum switch
        {
            "CLIMBING" => 1,
            "WALL" => -1,
            _ => 0,
        };

    // plateau = the recent half set NO new maxSpan high over the earlier half — grammar depth has stalled.
    private static bool Plateaued(Queue<double> span)
    {
        int half = span.Count / 2;
        double first = 0, second = 0;
        int i = 0;
        foreach (double v in span)
            if (i++ < half) first = Math.Max(first, v);
            else second = Math.Max(second, v);
        return second <= first + 1e-9;
    }

    // sagging = the current value fell ≥10% below the recent-window mean (a down-trend, not a single dip).
    private static bool Sagging(Queue<int> ring, int cur)
    {
        if (ring.Count < 3) return false;
        double sum = 0; foreach (var v in ring) sum += v;
        return cur < 0.9 * (sum / ring.Count);
    }

    // rising = the current value beat the recent-window best (still improving) — or too little history to judge.
    private static bool Rising(Queue<int> ring, int cur)
    {
        if (ring.Count < 3) return true;
        int mx = int.MinValue; foreach (var v in ring) mx = Math.Max(mx, v);
        return cur > mx;
    }

    private static void Trim<T>(Queue<T> q, int cap) { while (q.Count > cap) q.Dequeue(); }
    private static void Trim<T>(Queue<T> q, int cap, T add) { q.Enqueue(add); while (q.Count > cap) q.Dequeue(); }
}
