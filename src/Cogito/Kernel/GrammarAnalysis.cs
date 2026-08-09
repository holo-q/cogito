namespace Cogito.Grammar;

using Cogito;
using Cogito.Codec;
using Cogito.Induct;

/// The compressed grammar sequence and its shared expansion/cover basis. This is the one
/// owner for rule expansion, greedy cover masks, parsed-size reads, symbol frequencies, and
/// adjacent transition counts. Consumers may keep one instance for a whole publication
/// revision; one-shot Engine adapters delegate here.
public sealed class GrammarSequence
{
    private readonly uint _alphabetSize;
    private readonly int _maxExpansions;
    private List<GrammarRule> _rules;
    private readonly List<Symbol> _symbols;
    private readonly Dictionary<uint, int> _symbolCounts = new();
    private readonly Dictionary<long, int> _transitionCounts = new();
    private readonly List<RuleExpansion[]> _expansionRuns = new();
    private RuleExpansion[]? _materializedRuns;
    private byte[][]? _expansions;
    private Dictionary<int, byte[][]> _coverBuckets = new();

    public GrammarSequence(GrammarRule[] rules, Symbol[] compressed, uint alphabetSize = 256, int maxExpansions = 0)
    {
        if (rules is null) throw new ArgumentNullException(nameof(rules));
        if (compressed is null) throw new ArgumentNullException(nameof(compressed));
        _alphabetSize = alphabetSize;
        _maxExpansions = maxExpansions;
        _rules = new List<GrammarRule>(rules);
        _symbols = new List<Symbol>(compressed);
        BuildBasis(rules);
        BuildCounts();
    }

    public GrammarRevisionID Revision { get; private set; } = GrammarRevisionID.Zero;
    public IReadOnlyList<Symbol> Symbols => _symbols;
    public byte[][] Expansions => MaterializeExpansions();
    internal IReadOnlyDictionary<int, byte[][]> CoverBuckets => _coverBuckets;
    public IReadOnlyDictionary<uint, int> SymbolCounts => _symbolCounts;
    public IReadOnlyDictionary<long, int> TransitionCounts => _transitionCounts;
    public int Length => _symbols.Count;

    public static GrammarSequence BuildFromSnapshot(GrammarSnapshot snapshot, int maxExpansions = 0)
    {
        var sequence = new GrammarSequence(snapshot.Rules, snapshot.Compressed, snapshot.AlphabetSize, maxExpansions)
        {
            Revision = snapshot.Revision,
        };
        return sequence;
    }

    /// Apply one publication. A parent mismatch or an explicit reset rebuilds all derived
    /// planes from the immutable snapshot. A compatible delta updates the sequence, symbol,
    /// and transition planes at the edit boundary without touching unrelated positions.
    public GrammarAnalysisApplyReceipt Apply(InstallRevision publication)
    {
        if (publication.Reset != GrammarResetKinds.None || Revision != publication.ParentRevision)
        {
            ReplaceSnapshot(publication.Snapshot);
            TraceApply(publication, reset: true);
            return new GrammarAnalysisApplyReceipt(true, false, publication.Delta.SequenceEdits.Length);
        }

        foreach (var edit in publication.Delta.SequenceEdits)
            ApplyEdit(edit);
        if (publication.Delta.AddedRules.Length != 0)
        {
            int previousRuleCount = _rules.Count;
            _rules.AddRange(publication.Delta.AddedRules);
            AppendBasis(publication.Delta.AddedRules, previousRuleCount);
        }
        Revision = publication.Revision;
        TraceApply(publication, reset: false);
        return new GrammarAnalysisApplyReceipt(false, publication.Delta.SequenceEdits.Length != 0, publication.Delta.SequenceEdits.Length);
    }

    public bool[] BuildCoverMask(byte[] text)
    {
        var covered = new bool[text.Length];
        if (text.Length == 0) return covered;
        // Present-2-gram bitset over the text: an expansion (always ≥2 bytes) can only match where
        // its first two bytes occur adjacently, so a missing PrefixKey proves zero occurrences and
        // skipping it is a no-op — the greedy longest-first order over the surviving expansions is
        // untouched and the masks stay byte-identical while thousands of can't-match rules skip
        // their full-text IndexOf scan.
        Span<ulong> present = stackalloc ulong[1024];
        present.Clear();
        for (int i = 0; i + 1 < text.Length; i++)
        {
            int key = (text[i] << 8) | text[i + 1];
            present[key >> 6] |= 1UL << (key & 63);
        }
        foreach (var expansion in MaterializeRuleExpansions())
        {
            int prefix = expansion.PrefixKey;
            if ((present[prefix >> 6] & (1UL << (prefix & 63))) == 0) continue;
            byte[] bytes = expansion.Bytes;
            int cursor = 0;
            while (cursor + bytes.Length <= text.Length)
            {
                int rel = text.AsSpan(cursor).IndexOf(bytes);
                if (rel < 0) break;
                int at = cursor + rel;
                if (RegionUncovered(covered, at, bytes.Length))
                {
                    for (int k = 0; k < bytes.Length; k++) covered[at + k] = true;
                    cursor = at + bytes.Length;
                }
                else cursor = at + 1;
            }
        }
        return covered;
    }

    public double ComputeCoverage(byte[] text)
    {
        if (text.Length == 0) return 0;
        var covered = BuildCoverMask(text);
        int count = 0;
        foreach (bool bit in covered) if (bit) count++;
        return (double)count / text.Length;
    }

    public int ComputeParsedSize(ReadOnlySpan<byte> text)
    {
        if (text.Length == 0) return 0;
        int cursor = 0, symbols = 0;
        while (cursor < text.Length)
        {
            int best = 0;
            if (text.Length - cursor >= 2 && _coverBuckets.TryGetValue((text[cursor] << 8) | text[cursor + 1], out var candidates))
                foreach (var expansion in candidates)
                    if (expansion.Length <= text.Length - cursor && text.Slice(cursor, expansion.Length).SequenceEqual(expansion))
                    { best = expansion.Length; break; }
            symbols++;
            cursor += best > 0 ? best : 1;
        }
        return symbols;
    }

    public double ComputeParsedSizePerByte(byte[] text) => text.Length == 0 ? 1.0 : (double)ComputeParsedSize(text) / text.Length;

    public double ComputeConcentration()
    {
        int n = _symbolCounts.Count;
        if (n < 2) return 0;
        var counts = _symbolCounts.Values.ToArray();
        Array.Sort(counts);
        long sum = 0;
        foreach (int count in counts) sum += count;
        if (sum == 0) return 0;
        double weighted = 0;
        for (int i = 0; i < counts.Length; i++) weighted += (double)(i + 1) * counts[i];
        return 2.0 * weighted / (n * (double)sum) - (double)(n + 1) / n;
    }

    public static long PackTransition(Symbol left, Symbol right) => ((long)left.Value << 32) | right.Value;

    private void ReplaceSnapshot(GrammarSnapshot snapshot)
    {
        _symbols.Clear();
        _symbols.AddRange(snapshot.Compressed);
        _rules.Clear();
        _rules.AddRange(snapshot.Rules);
        BuildBasis(snapshot.Rules);
        BuildCounts();
        Revision = snapshot.Revision;
    }

    private void ApplyEdit(GrammarSequenceEdit edit)
    {
        if (edit.Start > _symbols.Count || edit.RemovedLength > _symbols.Count - edit.Start)
            throw new ArgumentOutOfRangeException(nameof(edit), "grammar sequence edit exceeds the current sequence");

        int oldEnd = edit.Start + edit.RemovedLength;
        for (int i = Math.Max(0, edit.Start - 1); i < Math.Min(_symbols.Count - 1, oldEnd); i++)
            DecrementTransition(_symbols[i], _symbols[i + 1]);
        for (int i = edit.Start; i < oldEnd; i++) DecrementSymbol(_symbols[i]);
        _symbols.RemoveRange(edit.Start, edit.RemovedLength);
        _symbols.InsertRange(edit.Start, edit.Inserted);
        foreach (var symbol in edit.Inserted) IncrementSymbol(symbol);
        int newEnd = edit.Start + edit.Inserted.Length;
        for (int i = Math.Max(0, edit.Start - 1); i < Math.Min(_symbols.Count - 1, newEnd); i++)
            IncrementTransition(_symbols[i], _symbols[i + 1]);
    }

    private void BuildBasis(IReadOnlyList<GrammarRule> rules)
    {
        var expansions = new List<RuleExpansion>(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            byte[] expansion = Reconstruct.Expand(rules, [new Symbol(_alphabetSize + (uint)i)]);
            if (expansion.Length >= 2) expansions.Add(new RuleExpansion(expansion));
        }
        expansions.Sort(CompareExpansions);
        if (_maxExpansions > 0 && expansions.Count > _maxExpansions)
            expansions.RemoveRange(_maxExpansions, expansions.Count - _maxExpansions);
        _expansionRuns.Clear();
        if (expansions.Count != 0) _expansionRuns.Add(expansions.ToArray());
        _expansions = null;
        _materializedRuns = null;
        RebuildBuckets(expansions);
    }

    private void AppendBasis(IReadOnlyList<GrammarRule> addedRules, int firstAddedRule)
    {
        var added = new List<RuleExpansion>(addedRules.Count);
        for (int i = 0; i < addedRules.Count; i++)
        {
            byte[] expansion = Reconstruct.Expand(_rules, [new Symbol(_alphabetSize + (uint)(firstAddedRule + i))]);
            if (expansion.Length >= 2) added.Add(new RuleExpansion(expansion));
        }
        if (added.Count == 0) return;
        added.Sort(CompareExpansions);
        var affected = new HashSet<int>(added.Select(expansion => expansion.PrefixKey));
        if (_maxExpansions == 0)
        {
            _expansionRuns.Add(added.ToArray());
            _expansions = null;
        _materializedRuns = null;
            var incomingByPrefix = new Dictionary<int, List<RuleExpansion>>();
            foreach (var expansion in added)
                (incomingByPrefix.TryGetValue(expansion.PrefixKey, out var bucket)
                    ? bucket
                    : incomingByPrefix[expansion.PrefixKey] = new()).Add(expansion);
            foreach (var (key, incoming) in incomingByPrefix) MergeBucket(key, incoming);
            return;
        }

        RuleExpansion[] old = MaterializeRuleExpansions();
        RuleExpansion[] merged = MergeSorted(old, added);
        int kept = Math.Min(_maxExpansions, merged.Length);
        var retained = new RuleExpansion[kept];
        Array.Copy(merged, retained, kept);
        var retainedBytes = new HashSet<byte[]>(retained.Select(expansion => expansion.Bytes), ReferenceEqualityComparer.Instance);
        foreach (var expansion in old)
            if (!retainedBytes.Contains(expansion.Bytes)) affected.Add(expansion.PrefixKey);
        foreach (var expansion in retained) affected.Add(expansion.PrefixKey);
        _expansionRuns.Clear();
        if (retained.Length != 0) _expansionRuns.Add(retained);
        _expansions = null;
        _materializedRuns = null;
        RebuildAffectedBuckets(affected, retained);
    }

    private void MergeBucket(int key, List<RuleExpansion> incoming)
    {
        _coverBuckets.TryGetValue(key, out var oldBucket);
        oldBucket ??= [];
        var merged = new byte[oldBucket.Length + incoming.Count][];
        int oldAt = 0, addAt = 0, at = 0;
        while (oldAt < oldBucket.Length && addAt < incoming.Count)
            merged[at++] = CompareExpansions(oldBucket[oldAt], incoming[addAt].Bytes) <= 0 ? oldBucket[oldAt++] : incoming[addAt++].Bytes;
        while (oldAt < oldBucket.Length) merged[at++] = oldBucket[oldAt++];
        while (addAt < incoming.Count) merged[at++] = incoming[addAt++].Bytes;
        _coverBuckets[key] = merged;
    }

    private void RebuildAffectedBuckets(HashSet<int> affected, RuleExpansion[] retained)
    {
        foreach (int key in affected)
        {
            var bucket = retained.Where(expansion => expansion.PrefixKey == key).Select(expansion => expansion.Bytes).ToArray();
            if (bucket.Length == 0) _coverBuckets.Remove(key); else _coverBuckets[key] = bucket;
        }
    }

    private void RebuildBuckets(IReadOnlyList<RuleExpansion> expansions)
    {
        var grouped = new Dictionary<int, List<byte[]>>();
        foreach (var expansion in expansions)
            (grouped.TryGetValue(expansion.PrefixKey, out var bucket) ? bucket : grouped[expansion.PrefixKey] = new()).Add(expansion.Bytes);
        _coverBuckets = grouped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private RuleExpansion[] MaterializeRuleExpansions()
    {
        if (_materializedRuns is not null) return _materializedRuns;
        if (_expansionRuns.Count == 0) return _materializedRuns = [];
        if (_expansionRuns.Count == 1) return _materializedRuns = _expansionRuns[0];
        var all = new List<RuleExpansion>();
        foreach (var run in _expansionRuns) all.AddRange(run);
        all.Sort(CompareExpansions);
        return _materializedRuns = all.ToArray();
    }

    private byte[][] MaterializeExpansions()
    {
        if (_expansions is not null) return _expansions;
        _expansions = MaterializeRuleExpansions().Select(expansion => expansion.Bytes).ToArray();
        return _expansions;
    }

    private static RuleExpansion[] MergeSorted(RuleExpansion[] old, List<RuleExpansion> added)
    {
        var merged = new RuleExpansion[old.Length + added.Count];
        int oldAt = 0, addedAt = 0, at = 0;
        while (oldAt < old.Length && addedAt < added.Count)
            merged[at++] = CompareExpansions(old[oldAt], added[addedAt]) <= 0 ? old[oldAt++] : added[addedAt++];
        while (oldAt < old.Length) merged[at++] = old[oldAt++];
        while (addedAt < added.Count) merged[at++] = added[addedAt++];
        return merged;
    }

    private void BuildCounts()
    {
        _symbolCounts.Clear();
        _transitionCounts.Clear();
        for (int i = 0; i < _symbols.Count; i++) IncrementSymbol(_symbols[i]);
        for (int i = 0; i + 1 < _symbols.Count; i++) IncrementTransition(_symbols[i], _symbols[i + 1]);
    }

    private void IncrementSymbol(Symbol symbol) => _symbolCounts[symbol.Value] = _symbolCounts.GetValueOrDefault(symbol.Value) + 1;
    private void DecrementSymbol(Symbol symbol)
    {
        int count = _symbolCounts[symbol.Value] - 1;
        if (count == 0) _symbolCounts.Remove(symbol.Value); else _symbolCounts[symbol.Value] = count;
    }
    private void IncrementTransition(Symbol left, Symbol right)
    {
        long key = PackTransition(left, right);
        _transitionCounts[key] = _transitionCounts.GetValueOrDefault(key) + 1;
    }
    private void DecrementTransition(Symbol left, Symbol right)
    {
        long key = PackTransition(left, right);
        int count = _transitionCounts[key] - 1;
        if (count == 0) _transitionCounts.Remove(key); else _transitionCounts[key] = count;
    }

    private static int CompareExpansions(RuleExpansion left, RuleExpansion right) => CompareExpansions(left.Bytes, right.Bytes);

    private static int CompareExpansions(byte[] left, byte[] right)
    {
        int length = right.Length.CompareTo(left.Length);
        if (length != 0) return length;
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return left[i].CompareTo(right[i]);
        return 0;
    }

    private static bool RegionUncovered(bool[] covered, int start, int length)
    {
        for (int i = 0; i < length; i++) if (covered[start + i]) return false;
        return true;
    }

    private static void TraceApply(InstallRevision publication, bool reset)
        => Cogito.Trace.Engine.Event($"grammar.analysis apply revision={publication.Revision} reset={(reset ? "yes" : "no")} edits={publication.Delta.SequenceEdits.Length}");
}

/// A materialized rule yield plus its cover prefix. The prefix is carried with the
/// expansion so append publications can update only affected buckets.
internal readonly struct RuleExpansion(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;
    public int PrefixKey => Bytes.Length < 2 ? -1 : (Bytes[0] << 8) | Bytes[1];
}

/// Per-rule structural planes plus the shared sequence plane. Rule depth/span/use are
/// maintained incrementally for append deltas; removals, rebases, and explicit resets use
/// the publication snapshot as authority and rebuild once.
public sealed class GrammarShape
{
    private int _maxExpansions;
    private readonly List<GrammarRule> _rules = new();
    private readonly List<int> _depth = new();
    private readonly List<int> _span = new();
    private readonly List<int> _uses = new();

    public GrammarRevisionID Revision { get; private set; } = GrammarRevisionID.Zero;
    public IReadOnlyList<GrammarRule> Rules => _rules;
    public IReadOnlyList<int> Depth => _depth;
    public IReadOnlyList<int> Span => _span;
    public IReadOnlyList<int> Uses => _uses;
    public GrammarSequence Sequence { get; private set; } = null!;
    public double Concentration => Sequence.ComputeConcentration();
    public int TransitionKinds => Sequence.TransitionCounts.Count;

    public static GrammarShape BuildFromSnapshot(GrammarSnapshot snapshot, int maxExpansions = 0)
    {
        var shape = new GrammarShape();
        shape._maxExpansions = maxExpansions;
        shape.Rebuild(snapshot, maxExpansions);
        return shape;
    }

    public static GrammarShape BuildFromResult(in RePairResult result, GrammarRevisionID revision = default, int maxExpansions = 0)
        => BuildFromSnapshot(new GrammarSnapshot(revision, result.Rules, result.Compressed, result.TotalSavings, result.AlphabetSize), maxExpansions);

    public static int[] ComputeUses(in RePairResult result)
    {
        RePairResult source = result;
        var uses = new int[source.Rules.Length];
        void Count(Symbol symbol)
        {
            if (symbol.Value < source.AlphabetSize) return;
            int index = (int)(symbol.Value - source.AlphabetSize);
            if ((uint)index < (uint)uses.Length) uses[index]++;
        }
        foreach (var symbol in source.Compressed) Count(symbol);
        foreach (var rule in source.Rules) foreach (var symbol in rule.Pattern) Count(symbol);
        return uses;
    }

    public static (int[] Depth, int[] Span) ComputeDepthSpan(GrammarRule[] rules, uint alphabetSize)
    {
        var depth = new int[rules.Length];
        var span = new int[rules.Length];
        for (int i = 0; i < rules.Length; i++)
        {
            int maxDepth = 0, totalSpan = 0;
            foreach (var symbol in rules[i].Pattern)
                if (symbol.Value >= alphabetSize && symbol.Value - alphabetSize < (uint)i)
                {
                    int child = (int)(symbol.Value - alphabetSize);
                    maxDepth = Math.Max(maxDepth, depth[child]);
                    totalSpan += span[child];
                }
                else totalSpan++;
            depth[i] = maxDepth + 1;
            span[i] = totalSpan;
        }
        return (depth, span);
    }

    public static double ComputeConcentration(in RePairResult result)
    {
        var counts = new Dictionary<uint, long>();
        foreach (var symbol in result.Compressed) counts[symbol.Value] = counts.GetValueOrDefault(symbol.Value) + 1;
        var values = counts.Values.ToArray();
        if (values.Length < 2) return 0;
        Array.Sort(values);
        long total = 0;
        foreach (long value in values) total += value;
        if (total == 0) return 0;
        double weighted = 0;
        for (int i = 0; i < values.Length; i++) weighted += (double)(i + 1) * values[i];
        return 2.0 * weighted / (values.Length * (double)total) - (double)(values.Length + 1) / values.Length;
    }

    public static Engine.RenormStat ComputeRenorm(in RePairResult result)
    {
        if (result.Rules.Length == 0) return new Engine.RenormStat(0, double.NaN, double.NaN, 0, 0);
        var (depth, span) = ComputeDepthSpan(result.Rules, result.AlphabetSize);
        var uses = ComputeUses(in result);
        int maxDepth = depth.Max(), maxSpan = span.Max();
        var zipfs = new List<double>();
        for (int level = 1; level <= maxDepth; level++)
        {
            var frequencies = new List<int>();
            for (int i = 0; i < depth.Length; i++) if (depth[i] == level) frequencies.Add(uses[i]);
            double z = ComputeZipf(frequencies);
            if (!double.IsNaN(z)) zipfs.Add(z);
        }
        double mean = double.NaN, cv = double.NaN;
        if (zipfs.Count > 0) mean = zipfs.Sum() / zipfs.Count;
        if (zipfs.Count > 1)
        {
            double variance = zipfs.Sum(z => (z - mean) * (z - mean)) / zipfs.Count;
            cv = Math.Sqrt(variance) / Math.Abs(mean);
        }
        return new Engine.RenormStat(maxDepth, mean, cv, maxSpan, zipfs.Count);
    }

    public GrammarShapeApplyReceipt Apply(InstallRevision publication)
    {
        bool reset = publication.Reset != GrammarResetKinds.None || Sequence is null || Revision != publication.ParentRevision;
        if (reset || publication.Delta.RemovedRules.Length != 0)
        {
            Rebuild(publication.Snapshot, _maxExpansions);
            TraceApply(publication, reset: true);
            return new GrammarShapeApplyReceipt(true, false, publication.Delta.AddedRules.Length);
        }

        // A revision-only publication advances the cursor without touching any derived
        // plane. In particular, do not route an empty anti-unify delta through Sequence;
        // the no-op receipt is the O(1) proof that no rebuild/compose/base visit occurred.
        if (publication.Delta.IsEmpty)
        {
            Revision = publication.Revision;
            TraceApply(publication, reset: false);
            return new GrammarShapeApplyReceipt(false, false, 0);
        }

        if (publication.Delta.AddedRules.Length != 0)
        {
            foreach (var rule in publication.Delta.AddedRules) AppendRule(rule, publication.Snapshot.AlphabetSize);
        }
        GrammarSequence sequence = Sequence ?? throw new InvalidOperationException("grammar shape has no sequence plane");
        var removedSymbols = new List<Symbol>();
        foreach (var edit in publication.Delta.SequenceEdits)
        {
            for (int i = edit.Start; i < edit.Start + edit.RemovedLength; i++)
                if ((uint)i < (uint)sequence.Symbols.Count) removedSymbols.Add(sequence.Symbols[i]);
        }
        GrammarAnalysisApplyReceipt sequenceReceipt = sequence.Apply(publication);
        foreach (var symbol in removedSymbols)
            if (symbol.Value >= publication.Snapshot.AlphabetSize && symbol.Value - publication.Snapshot.AlphabetSize < (uint)_uses.Count)
                _uses[(int)(symbol.Value - publication.Snapshot.AlphabetSize)]--;
        foreach (var edit in publication.Delta.SequenceEdits)
        {
            foreach (var symbol in edit.Inserted)
                if (symbol.Value >= publication.Snapshot.AlphabetSize && symbol.Value - publication.Snapshot.AlphabetSize < (uint)_uses.Count)
                    _uses[(int)(symbol.Value - publication.Snapshot.AlphabetSize)]++;
            // Removed symbols are accounted by Sequence's old span, so a delta consumer
            // that needs exact per-rule uses should pass a publication with a reset snapshot.
        }
        Revision = publication.Revision;
        TraceApply(publication, reset: false);
        return new GrammarShapeApplyReceipt(false, sequenceReceipt.Changed, publication.Delta.AddedRules.Length);
    }

    public Engine.RenormStat ReadRenorm()
    {
        if (_rules.Count == 0) return new Engine.RenormStat(0, double.NaN, double.NaN, 0, 0);
        int maxDepth = _depth.Max();
        var zipfs = new List<double>();
        for (int level = 1; level <= maxDepth; level++)
        {
            var frequencies = new List<int>();
            for (int i = 0; i < _depth.Count; i++) if (_depth[i] == level) frequencies.Add(_uses[i]);
            double z = ComputeZipf(frequencies);
            if (!double.IsNaN(z)) zipfs.Add(z);
        }
        double mean = double.NaN, cv = double.NaN;
        if (zipfs.Count > 0) mean = zipfs.Sum() / zipfs.Count;
        if (zipfs.Count > 1)
        {
            double variance = zipfs.Sum(z => (z - mean) * (z - mean)) / zipfs.Count;
            cv = Math.Sqrt(variance) / Math.Abs(mean);
        }
        return new Engine.RenormStat(maxDepth, mean, cv, _span.Max(), zipfs.Count);
    }

    private void Rebuild(GrammarSnapshot snapshot, int maxExpansions)
    {
        _rules.Clear(); _depth.Clear(); _span.Clear(); _uses.Clear();
        Sequence = GrammarSequence.BuildFromSnapshot(snapshot, maxExpansions);
        foreach (var rule in snapshot.Rules) AppendRule(rule, snapshot.AlphabetSize);
        RecomputeUses(snapshot.AlphabetSize);
        Revision = snapshot.Revision;
        Trace.Engine.Event($"grammar.analysis rebuild revision={Revision} rules={_rules.Count} symbols={Sequence.Length}");
    }

    private void AppendRule(GrammarRule rule, uint alphabet)
    {
        int index = _rules.Count;
        _rules.Add(rule);
        int depth = 0, span = 0;
        foreach (var symbol in rule.Pattern)
        {
            if (symbol.Value >= alphabet && symbol.Value - alphabet < (uint)index)
            {
                int child = (int)(symbol.Value - alphabet);
                depth = Math.Max(depth, _depth[child]);
                span += _span[child];
            }
            else span++;
        }
        _depth.Add(depth + 1); _span.Add(span); _uses.Add(0);
        foreach (var symbol in rule.Pattern)
            if (symbol.Value >= alphabet && symbol.Value - alphabet < (uint)index)
                _uses[(int)(symbol.Value - alphabet)]++;
    }

    private void RecomputeUses(uint alphabet)
    {
        for (int i = 0; i < _uses.Count; i++) _uses[i] = 0;
        foreach (var symbol in Sequence.Symbols)
            if (symbol.Value >= alphabet && symbol.Value - alphabet < (uint)_uses.Count) _uses[(int)(symbol.Value - alphabet)]++;
        foreach (var rule in _rules)
            foreach (var symbol in rule.Pattern)
                if (symbol.Value >= alphabet && symbol.Value - alphabet < (uint)_uses.Count) _uses[(int)(symbol.Value - alphabet)]++;
    }

    private static double ComputeZipf(List<int> frequencies)
    {
        frequencies.RemoveAll(value => value <= 0);
        if (frequencies.Count < 3) return double.NaN;
        frequencies.Sort((left, right) => right.CompareTo(left));
        int count = frequencies.Count;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < count; i++) { double x = Math.Log(i + 1), y = Math.Log(frequencies[i]); sx += x; sy += y; sxx += x * x; sxy += x * y; }
        return (count * sxy - sx * sy) / (count * sxx - sx * sx);
    }

    private static void TraceApply(InstallRevision publication, bool reset)
        => Cogito.Trace.Engine.Event($"grammar.analysis shape revision={publication.Revision} reset={(reset ? "yes" : "no")} additions={publication.Delta.AddedRules.Length}");
}

public readonly record struct GrammarAnalysisApplyReceipt(bool Rebuilt, bool Changed, int SequenceEdits);
public readonly record struct GrammarShapeApplyReceipt(bool Rebuilt, bool Changed, int AddedRules);

/// A CLI-facing differential oracle for the publication/analysis seam. It exercises an
/// append edit, a replacement reset, and a second reset after a revision gap, comparing the
/// shared shape to fresh Engine materializations at every transition.
public static class GrammarAnalysisOracle
{
    public static int Verify()
    {
        byte[] baseBytes = "alpha alpha alpha\nbeta beta beta\n"u8.ToArray();
        var baseResult = Engine.Induce(baseBytes).Result;
        var basePub = InstallRevision.FromRePair(new GrammarRevisionID(1), GrammarRevisionID.Zero, in baseResult);
        InstallRevisionReceipt baseReceipt = basePub.Account();
        var shape = GrammarShape.BuildFromSnapshot(basePub.Snapshot);
        var sharedCover = new Engine.GrammarCover(shape);
        bool ok = Matches(shape, baseResult, sharedCover)
            && baseReceipt.SnapshotRuleElements == baseResult.Rules.Length
            && baseReceipt.SnapshotCompressedElements == baseResult.Compressed.Length
            && baseReceipt.DeltaSharesSnapshotArrays;

        Symbol[] appended = [.. baseResult.Compressed, new Symbol((byte)'!')];
        var appendSnapshot = new GrammarSnapshot(new GrammarRevisionID(2), baseResult.Rules, appended, baseResult.TotalSavings, baseResult.AlphabetSize);
        var appendDelta = new GrammarDelta(basePub.Revision, appendSnapshot.Revision, [], [], [GrammarSequenceEdit.Replace(baseResult.Compressed.Length, 0, [new Symbol((byte)'!')])], Mbits.Zero, GrammarResetKinds.None);
        ok &= !shape.Apply(new InstallRevision(appendSnapshot, appendDelta)).Rebuilt;
        ok &= Matches(shape, appendSnapshot, sharedCover);

        var appendResult = appendSnapshot.ToRePairResult();
        InstallRevision appendInstallRevision = InstallRevision.FromRePair(appendSnapshot.Revision, basePub.Revision, in appendResult, basePub.Snapshot);
        var incrementalShape = GrammarShape.BuildFromSnapshot(basePub.Snapshot);
        GrammarShapeApplyReceipt incrementalReceipt = incrementalShape.Apply(appendInstallRevision);
        ok &= appendInstallRevision.Reset == GrammarResetKinds.None
            && appendInstallRevision.Delta.AddedRules.Length == 0
            && appendInstallRevision.Delta.SequenceEdits.Length == 1
            && !incrementalReceipt.Rebuilt
            && Matches(incrementalShape, appendSnapshot, new Engine.GrammarCover(incrementalShape));

        // Rule-only publication: the sequence is unchanged, but the shared shape
        // must absorb the new basis without rebuilding the consumer cover.
        if (baseResult.Rules.Length > 0)
        {
            GrammarRule extraRule = baseResult.Rules[0];
            GrammarRule[] ruleSnapshot = [.. appendSnapshot.Rules, extraRule];
            var ruleOnlySnapshot = new GrammarSnapshot(new GrammarRevisionID(25), ruleSnapshot, appendSnapshot.Compressed,
                appendSnapshot.TotalSavings, appendSnapshot.AlphabetSize);
            var ruleOnlyDelta = new GrammarDelta(appendSnapshot.Revision, ruleOnlySnapshot.Revision, [extraRule], [], [], Mbits.Zero, GrammarResetKinds.None);
            ok &= !shape.Apply(new InstallRevision(ruleOnlySnapshot, ruleOnlyDelta)).Rebuilt;
            ok &= Matches(shape, ruleOnlySnapshot, sharedCover);
        }

        byte[] replacementBytes = "gamma gamma gamma\ndelta delta delta\n"u8.ToArray();
        var replacement = Engine.Induce(replacementBytes).Result;
        var replacementPub = InstallRevision.FromRePair(new GrammarRevisionID(3), appendSnapshot.Revision, in replacement);
        ok &= shape.Apply(replacementPub).Rebuilt;
        ok &= Matches(shape, replacement, sharedCover);

        InstallRevision noOpInstallRevision = InstallRevision.FromRePair(new GrammarRevisionID(2), basePub.Revision, in baseResult, basePub.Snapshot);
        ok &= noOpInstallRevision.Reset == GrammarResetKinds.None
            && noOpInstallRevision.Delta.IsEmpty
            && ReferenceEquals(noOpInstallRevision.Snapshot.Rules, basePub.Snapshot.Rules)
            && ReferenceEquals(noOpInstallRevision.Snapshot.Compressed, basePub.Snapshot.Compressed);
        GrammarShape noOpShape = GrammarShape.BuildFromSnapshot(basePub.Snapshot);
        GrammarShapeApplyReceipt noOpReceipt = noOpShape.Apply(noOpInstallRevision);
        ok &= !noOpReceipt.Rebuilt && !noOpReceipt.Changed && noOpShape.Revision == noOpInstallRevision.Revision;

        // The side-layer append fast path must reject a longer base whose old prefix
        // changed while the overlay suffix stayed byte-identical.  Length-only trust is
        // an ancestry forgery: the resulting composed image would look plausible while
        // its references belong to a different base.
        bool overlayBaseRebindNull = VerifyOverlayBaseRebindNull();
        ok &= overlayBaseRebindNull;

        (double IncrementalMilliseconds, double RebuildMilliseconds, double NoOpInstallRevisionMilliseconds, int TimingRules, double WideIncrementalMilliseconds, double WideRebuildMilliseconds, double WideShapeIncrementalMilliseconds, double WideShapeRebuildMilliseconds, int WidePrefixes, double WideNoOpInstallRevisionMilliseconds, int WideNoOpRules, int WideNoOpSymbols) timing = MeasureInstallRevisionApply();
        ok &= VerifyWideAppendBasis();
        Console.WriteLine($"verify-grammar-analysis · append/rule-only/replace/reset/no-op consumer differential · {(ok ? "PASS" : "FAIL")} · rules {replacement.Rules.Length} · symbols {replacement.Compressed.Length} · publication-arrays {(baseReceipt.DeltaSharesSnapshotArrays ? "shared" : "duplicated")} · owned={baseReceipt.OwnedArrayCount} · overlay-base-rebind={(overlayBaseRebindNull ? "null" : "ACCEPTED")} · incremental_ms={timing.IncrementalMilliseconds:F3} · rebuild_ms={timing.RebuildMilliseconds:F3} · no_op_publication_ms={timing.NoOpInstallRevisionMilliseconds:F3} · timing_rules={timing.TimingRules} · wide_incremental_ms={timing.WideIncrementalMilliseconds:F3} · wide_rebuild_ms={timing.WideRebuildMilliseconds:F3} · wide_shape_incremental_ms={timing.WideShapeIncrementalMilliseconds:F3} · wide_shape_rebuild_ms={timing.WideShapeRebuildMilliseconds:F3} · wide_prefixes={timing.WidePrefixes} · wide_no_op_publication_ms={timing.WideNoOpInstallRevisionMilliseconds:F3} · wide_no_op_scale={timing.WideNoOpRules}rules/{timing.WideNoOpSymbols}symbols");
        return ok ? 0 : 1;
    }

    private static bool VerifyOverlayBaseRebindNull()
    {
        Symbol[] pattern = [new Symbol((byte)'a'), new Symbol((byte)'b')];
        GrammarRule baseRule = new(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));
        Symbol[] suffixPattern = [new Symbol(Symbol.FirstNonterminal), new Symbol((byte)'c')];
        GrammarRule suffixRule = new(GrammarRule.ComputeId(suffixPattern), suffixPattern, new Mbits(512));
        RePairResult baseResult = new([baseRule], [new Symbol(Symbol.FirstNonterminal)], Mbits.Zero, 256);
        RePairResult composed = new([baseRule, suffixRule], baseResult.Compressed, Mbits.Zero, 256);
        GrammarSnapshot baseSnapshot = GrammarSnapshot.FromRePair(new GrammarRevisionID(90), in baseResult);
        GrammarOverlay? prior = GrammarOverlay.TryFromComposed(baseSnapshot, in composed);
        if (prior is null) return false;

        // Preserve the content-addressed RuleID but alter the rule cost.  This is the
        // adversarial case IDs alone cannot detect; the strict rule image must reject it.
        GrammarRule changedBaseRule = new(baseRule.Id, baseRule.Pattern, new Mbits(1024));
        RePairResult changedBase = new([changedBaseRule], baseResult.Compressed, Mbits.Zero, 256);
        RePairResult changedComposed = new([changedBaseRule, suffixRule], changedBase.Compressed, Mbits.Zero, 256);
        GrammarSnapshot changedSnapshot = GrammarSnapshot.FromRePair(new GrammarRevisionID(91), in changedBase);
        return GrammarOverlay.TryFromComposed(changedSnapshot, in changedComposed, prior) is null;
    }

    private static (double IncrementalMilliseconds, double RebuildMilliseconds, double NoOpInstallRevisionMilliseconds, int TimingRules, double WideIncrementalMilliseconds, double WideRebuildMilliseconds, double WideShapeIncrementalMilliseconds, double WideShapeRebuildMilliseconds, int WidePrefixes, double WideNoOpInstallRevisionMilliseconds, int WideNoOpRules, int WideNoOpSymbols) MeasureInstallRevisionApply()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("alpha alpha alpha beta beta beta gamma gamma gamma\n", 128)));
        RePairResult result = Engine.Induce(bytes).Result;
        if (result.Rules.Length == 0) return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        GrammarSnapshot parent = GrammarSnapshot.FromRePair(new GrammarRevisionID(10), in result);
        Symbol[] appended = [.. result.Compressed, new Symbol((byte)'!')];
        RePairResult nextResult = new(result.Rules, appended, result.TotalSavings, result.AlphabetSize);
        InstallRevision incrementalInstallRevision = InstallRevision.FromRePair(new GrammarRevisionID(11), parent.Revision, in nextResult, parent);
        InstallRevision rebuildInstallRevision = InstallRevision.FromRePair(new GrammarRevisionID(11), parent.Revision, in nextResult);
        var warm = GrammarShape.BuildFromSnapshot(parent);
        warm.Apply(rebuildInstallRevision);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        _ = InstallRevision.FromRePair(new GrammarRevisionID(12), parent.Revision, in result, parent);
        double noOpInstallRevisionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var incremental = GrammarShape.BuildFromSnapshot(parent);
        var rebuild = GrammarShape.BuildFromSnapshot(parent);
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        incremental.Apply(incrementalInstallRevision);
        double incrementalMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        rebuild.Apply(rebuildInstallRevision);
        double rebuildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        (double wideIncrementalMilliseconds, double wideRebuildMilliseconds, double wideShapeIncrementalMilliseconds, double wideShapeRebuildMilliseconds, int widePrefixes) = MeasureWideAppendApply();
        (double wideNoOpMilliseconds, int wideNoOpRules, int wideNoOpSymbols) = MeasureWideNoOpInstallRevision();
        return (incrementalMilliseconds, rebuildMilliseconds, noOpInstallRevisionMilliseconds, result.Rules.Length, wideIncrementalMilliseconds, wideRebuildMilliseconds, wideShapeIncrementalMilliseconds, wideShapeRebuildMilliseconds, widePrefixes, wideNoOpMilliseconds, wideNoOpRules, wideNoOpSymbols);
    }

    private static (double Milliseconds, int Rules, int Symbols) MeasureWideNoOpInstallRevision()
    {
        const int rules = 16_493;
        const int symbols = 375_000;
        RePairResult result = new(BuildPairRules(0, rules), new Symbol[symbols], Mbits.Zero, 256);
        GrammarSnapshot parent = GrammarSnapshot.FromRePair(new GrammarRevisionID(20), in result);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        InstallRevision publication = InstallRevision.FromRePair(new GrammarRevisionID(21), parent.Revision, in result, parent);
        double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (!publication.Delta.IsEmpty || !ReferenceEquals(publication.Snapshot.Rules, parent.Rules) || !ReferenceEquals(publication.Snapshot.Compressed, parent.Compressed))
            throw new InvalidOperationException("wide no-op publication did not preserve the owned image");
        return (milliseconds, rules, symbols);
    }

    private static (double IncrementalMilliseconds, double RebuildMilliseconds, double ShapeIncrementalMilliseconds, double ShapeRebuildMilliseconds, int Prefixes) MeasureWideAppendApply()
    {
        const int oldRuleCount = 8_192;
        const int addedRuleCount = 4_096;
        GrammarRule[] parentRules = BuildPairRules(0, oldRuleCount);
        GrammarRule[] addedRules = BuildPairRules(16_384, addedRuleCount);
        GrammarRule[] allRules = [.. parentRules, .. addedRules];
        GrammarRevisionID parentRevision = new(100);
        GrammarRevisionID revision = new(101);
        var parent = new GrammarSnapshot(parentRevision, parentRules, [], Mbits.Zero, 256);
        var snapshot = new GrammarSnapshot(revision, allRules, [], Mbits.Zero, 256);
        var delta = new GrammarDelta(parentRevision, revision, addedRules, [], [], Mbits.Zero, GrammarResetKinds.None);
        var publication = new InstallRevision(snapshot, delta);
        var resetDelta = new GrammarDelta(parentRevision, revision, [], [], [], Mbits.Zero, GrammarResetKinds.Rebuild);
        var rebuildInstallRevision = new InstallRevision(snapshot, resetDelta);
        var incremental = GrammarSequence.BuildFromSnapshot(parent);
        var rebuild = GrammarSequence.BuildFromSnapshot(parent);
        var shapeIncremental = GrammarShape.BuildFromSnapshot(parent);
        var shapeRebuild = GrammarShape.BuildFromSnapshot(parent);

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        incremental.Apply(publication);
        double incrementalMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        rebuild.Apply(rebuildInstallRevision);
        double rebuildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        shapeIncremental.Apply(publication);
        double shapeIncrementalMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        shapeRebuild.Apply(rebuildInstallRevision);
        double shapeRebuildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (incrementalMilliseconds, rebuildMilliseconds, shapeIncrementalMilliseconds, shapeRebuildMilliseconds, addedRuleCount);
    }

    private static GrammarRule[] BuildPairRules(int firstPrefix, int count)
    {
        var rules = new GrammarRule[count];
        for (int i = 0; i < count; i++)
        {
            int key = firstPrefix + i;
            Symbol[] pattern = [new Symbol((uint)(byte)(key >> 8)), new Symbol((uint)(byte)key)];
            rules[i] = new GrammarRule(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));
        }
        return rules;
    }

    private static GrammarRule CreateRule(params byte[] values)
    {
        var pattern = new Symbol[values.Length];
        for (int i = 0; i < values.Length; i++) pattern[i] = new Symbol(values[i]);
        return new GrammarRule(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));
    }

    private static bool VerifyWideAppendBasis()
    {
        GrammarRule[] parentRules = BuildPairRules(0, 128);
        GrammarRule[] addedRules = BuildPairRules(16_384, 96);
        GrammarRule[] allRules = [.. parentRules, .. addedRules];
        GrammarRevisionID parentRevision = new(200);
        GrammarRevisionID revision = new(201);
        var parent = new GrammarSnapshot(parentRevision, parentRules, [], Mbits.Zero, 256);
        var snapshot = new GrammarSnapshot(revision, allRules, [], Mbits.Zero, 256);
        var delta = new GrammarDelta(parentRevision, revision, addedRules, [], [], Mbits.Zero, GrammarResetKinds.None);
        var incremental = GrammarSequence.BuildFromSnapshot(parent);
        byte[][] oldExpansions = incremental.Expansions;
        var publication = new InstallRevision(snapshot, delta);
        incremental.Apply(publication);
        var fresh = GrammarSequence.BuildFromSnapshot(snapshot);
        if (incremental.Expansions.Length != fresh.Expansions.Length || incremental.CoverBuckets.Count != fresh.CoverBuckets.Count)
            return false;
        for (int i = 0; i < incremental.Expansions.Length; i++)
        {
            if (!incremental.Expansions[i].AsSpan().SequenceEqual(fresh.Expansions[i])) return false;
        }
        foreach (byte[] expansion in oldExpansions)
            if (!incremental.Expansions.Any(candidate => ReferenceEquals(candidate, expansion))) return false;
        foreach (var (key, expected) in fresh.CoverBuckets)
        {
            if (!incremental.CoverBuckets.TryGetValue(key, out var actual) || actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++)
                if (!actual[i].AsSpan().SequenceEqual(expected[i])) return false;
        }
        return VerifyAppendBasisEdgeCases();
    }

    private static bool VerifyAppendBasisEdgeCases()
    {
        // An added expansion may share an existing prefix and tie its bytes exactly. The
        // merge comparator must retain the old instance first, while the affected bucket gets
        // one ordered rebuild containing both old and new entries.
        GrammarRule[] overlapParentRules = [CreateRule(0x0A, 0x0B), CreateRule(0x0C, 0x0D)];
        GrammarRule[] overlapAddedRules = [CreateRule(0x0A, 0x0B), CreateRule(0x0A, 0x0E)];
        GrammarRule[] overlapAllRules = [.. overlapParentRules, .. overlapAddedRules];
        var overlapParent = new GrammarSnapshot(new GrammarRevisionID(300), overlapParentRules, [], Mbits.Zero, 256);
        var overlapSnapshot = new GrammarSnapshot(new GrammarRevisionID(301), overlapAllRules, [], Mbits.Zero, 256);
        var overlapDelta = new GrammarDelta(overlapParent.Revision, overlapSnapshot.Revision, overlapAddedRules, [], [], Mbits.Zero, GrammarResetKinds.None);
        var overlapSequence = GrammarSequence.BuildFromSnapshot(overlapParent);
        byte[] oldTie = overlapSequence.Expansions.Single(expansion => expansion.AsSpan().SequenceEqual(new byte[] { 0x0A, 0x0B }));
        overlapSequence.Apply(new InstallRevision(overlapSnapshot, overlapDelta));
        if (!overlapSequence.CoverBuckets.TryGetValue(0x0A0B, out var tieBucket)
            || tieBucket.Length != 2 || !ReferenceEquals(tieBucket[0], oldTie)
            || !tieBucket[0].AsSpan().SequenceEqual(tieBucket[1])) return false;
        if (!overlapSequence.CoverBuckets.TryGetValue(0x0A0E, out var overlapBucket)
            || overlapBucket.Length != 1 || !overlapBucket[0].AsSpan().SequenceEqual(new byte[] { 0x0A, 0x0E })) return false;
        var overlapShape = GrammarShape.BuildFromSnapshot(overlapParent);
        if (overlapShape.Apply(new InstallRevision(overlapSnapshot, overlapDelta)).Rebuilt) return false;
        var overlapFreshShape = GrammarShape.BuildFromSnapshot(overlapSnapshot);
        if (!BasisMatches(overlapShape.Sequence, overlapFreshShape.Sequence)) return false;

        // A longer delta entry can truncate an old tail under maxExpansions. The evicted old
        // prefix must be marked affected and removed, while the retained old byte[] remains
        // shared with the incremental basis.
        GrammarRule[] cappedParentRules = [CreateRule(0x01, 0x01), CreateRule(0x02, 0x02)];
        GrammarRule[] cappedAddedRules = [CreateRule(0x09, 0x09, 0x09)];
        GrammarRule[] cappedAllRules = [.. cappedParentRules, .. cappedAddedRules];
        var cappedParent = new GrammarSnapshot(new GrammarRevisionID(310), cappedParentRules, [], Mbits.Zero, 256);
        var cappedSnapshot = new GrammarSnapshot(new GrammarRevisionID(311), cappedAllRules, [], Mbits.Zero, 256);
        var cappedDelta = new GrammarDelta(cappedParent.Revision, cappedSnapshot.Revision, cappedAddedRules, [], [], Mbits.Zero, GrammarResetKinds.None);
        var cappedSequence = GrammarSequence.BuildFromSnapshot(cappedParent, maxExpansions: 2);
        byte[] retainedOld = cappedSequence.Expansions.Single(expansion => expansion.AsSpan().SequenceEqual(new byte[] { 0x01, 0x01 }));
        byte[] evictedOld = cappedSequence.Expansions.Single(expansion => expansion.AsSpan().SequenceEqual(new byte[] { 0x02, 0x02 }));
        cappedSequence.Apply(new InstallRevision(cappedSnapshot, cappedDelta));
        if (!cappedSequence.Expansions.Any(expansion => ReferenceEquals(expansion, retainedOld))
            || cappedSequence.Expansions.Any(expansion => ReferenceEquals(expansion, evictedOld))
            || cappedSequence.CoverBuckets.ContainsKey(0x0202)
            || !cappedSequence.CoverBuckets.TryGetValue(0x0101, out var retainedBucket)
            || retainedBucket.Length != 1 || !ReferenceEquals(retainedBucket[0], retainedOld)) return false;
        var cappedFresh = GrammarSequence.BuildFromSnapshot(cappedSnapshot, maxExpansions: 2);
        return BasisMatches(cappedSequence, cappedFresh);
    }

    private static bool BasisMatches(GrammarSequence actual, GrammarSequence expected)
    {
        if (actual.Expansions.Length != expected.Expansions.Length || actual.CoverBuckets.Count != expected.CoverBuckets.Count)
            return false;
        for (int i = 0; i < actual.Expansions.Length; i++)
            if (!actual.Expansions[i].AsSpan().SequenceEqual(expected.Expansions[i])) return false;
        foreach (var (key, expectedBucket) in expected.CoverBuckets)
        {
            if (!actual.CoverBuckets.TryGetValue(key, out var actualBucket) || actualBucket.Length != expectedBucket.Length) return false;
            for (int i = 0; i < actualBucket.Length; i++)
                if (!actualBucket[i].AsSpan().SequenceEqual(expectedBucket[i])) return false;
        }
        return true;
    }

    private static bool Matches(GrammarShape shape, in RePairResult expected, Engine.GrammarCover sharedCover)
    {
        if (shape.Rules.Count != expected.Rules.Length || shape.Sequence.Length != expected.Compressed.Length) return false;
        for (int i = 0; i < expected.Compressed.Length; i++) if (!shape.Sequence.Symbols[i].Equals(expected.Compressed[i])) return false;
        var fresh = GrammarShape.BuildFromResult(in expected);
        byte[] probe = "alpha alpha alpha\nbeta beta beta\ngamma gamma gamma\n"u8.ToArray();
        var cover = new Engine.GrammarCover(expected.Rules);
        return shape.Depth.SequenceEqual(fresh.Depth) && shape.Span.SequenceEqual(fresh.Span) && shape.Uses.SequenceEqual(fresh.Uses)
            && Math.Abs(shape.Concentration - fresh.Concentration) < 1e-12
            && Math.Abs(shape.Sequence.ComputeCoverage(probe) - cover.Coverage(probe)) < 1e-12
            && shape.Sequence.ComputeParsedSize(probe) == cover.ParsedSize(probe)
            && Math.Abs(sharedCover.Coverage(probe) - cover.Coverage(probe)) < 1e-12
            && sharedCover.ParsedSize(probe) == cover.ParsedSize(probe);
    }

    private static bool Matches(GrammarShape shape, GrammarSnapshot expected, Engine.GrammarCover sharedCover)
        => Matches(shape, expected.ToRePairResult(), sharedCover);
}
