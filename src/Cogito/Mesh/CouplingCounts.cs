namespace Cogito;

using Cogito.Grammar;
using Cogito.Induct;

/// Raw, publication-driven evidence for the coupling and transition fields.
///
/// This is deliberately not a scorer or a sampler model.  Sequence splices update only
/// the symbols, marginals, distance co-counts, and successor tables whose windows touch
/// the splice.  PPMI and CSR are materialized separately because they are global views of
/// this evidence.
public sealed class CouplingCounts
{
    private readonly int _window;
    private readonly List<Symbol> _symbols;
    private readonly Dictionary<uint, int> _marginals = new();
    private readonly Dictionary<long, int>[] _coCounts;
    private readonly Dictionary<uint, Dictionary<uint, int>> _successors = new();
    private readonly Dictionary<(uint, uint), Dictionary<uint, int>> _successors2 = new();
    private readonly Dictionary<uint, int> _successorTotals = new();
    private readonly Dictionary<(uint, uint), int> _successorTotals2 = new();

    private CouplingCounts(int window, Symbol[] symbols, GrammarRevisionID revision)
    {
        if (window < 1) throw new ArgumentOutOfRangeException(nameof(window));
        _window = window;
        _symbols = new List<Symbol>(symbols ?? throw new ArgumentNullException(nameof(symbols)));
        _coCounts = new Dictionary<long, int>[window + 1];
        for (int d = 1; d <= window; d++) _coCounts[d] = new();
        InstallRevision = revision;
        CountRevision = revision;
        BuildAll();
    }

    public int Window => _window;
    public GrammarRevisionID InstallRevision { get; private set; }
    public GrammarRevisionID CountRevision { get; private set; }
    public IReadOnlyList<Symbol> Symbols => _symbols;
    public IReadOnlyDictionary<uint, int> Marginals => _marginals;
    public int SymbolCount => _symbols.Count;
    public long TotalSymbols => _symbols.Count == 0 ? 1 : _symbols.Count;

    public static CouplingCounts Build(GrammarSequence sequence, int window = Couplings.DefaultWindow)
        => new(window, sequence.Symbols.ToArray(), sequence.Revision);

    public static CouplingCounts Build(in RePairResult result, GrammarRevisionID revision = default, int window = Couplings.DefaultWindow)
        => new(window, result.Compressed, revision);

    public IReadOnlyDictionary<long, int> CoCounts(int distance)
    {
        if ((uint)distance > (uint)_window || distance == 0) throw new ArgumentOutOfRangeException(nameof(distance));
        return _coCounts[distance];
    }

    public Transitions BuildTransitions()
        => Transitions.FromCounts(_successors, _successorTotals, _successors2, _successorTotals2, CountDistinctSymbols());

    public bool Matches(CouplingCounts other)
    {
        if (_window != other._window || !_symbols.SequenceEqual(other._symbols) || _marginals.Count != other._marginals.Count) return false;
        foreach (var (unit, count) in _marginals) if (other._marginals.GetValueOrDefault(unit) != count) return false;
        for (int d = 1; d <= _window; d++)
        {
            if (_coCounts[d].Count != other._coCounts[d].Count) return false;
            foreach (var (key, count) in _coCounts[d]) if (other._coCounts[d].GetValueOrDefault(key) != count) return false;
        }
        return BuildTransitions().Matches(other.BuildTransitions());
    }

    public CouplingCountsApplyReceipt Apply(InstallRevision publication)
    {
        GrammarRevisionID previousRevision = InstallRevision;
        if (publication.Reset != GrammarResetKinds.None || publication.ParentRevision != previousRevision)
            return Rebuild(publication, "publication-reset-or-parent-gap");
        InstallRevision = publication.Revision;

        var edits = publication.Delta.SequenceEdits;
        if (edits.Length == 0)
        {
            // Rule-only publication: evidence is unchanged.  Keep CountRevision stable so
            // scorer materialization is not falsely invalidated.
            return new CouplingCountsApplyReceipt(false, false, false, CountRevision, 0);
        }

        if (!CanApplyEdits(edits))
            return Rebuild(publication, "sequence-edit-order-not-local");

        try
        {
            foreach (var edit in edits) ApplyEdit(edit);
            if (!_symbols.SequenceEqual(publication.Snapshot.Compressed))
                return Rebuild(publication, "incremental-sequence-mismatch");
        }
        catch (ArgumentOutOfRangeException)
        {
            return Rebuild(publication, "sequence-edit-out-of-range");
        }

        CountRevision = publication.Revision;
        TraceCounts(publication, rebuilt: false, "local");
        return new CouplingCountsApplyReceipt(false, true, true, CountRevision, edits.Length);
    }

    public CouplingCounts Clone()
    {
        var clone = new CouplingCounts(_window, _symbols.ToArray(), InstallRevision)
        {
            CountRevision = CountRevision,
        };
        return clone;
    }

    private CouplingCountsApplyReceipt Rebuild(InstallRevision publication, string reason)
    {
        _symbols.Clear();
        _symbols.AddRange(publication.Snapshot.Compressed);
        ClearEvidence();
        BuildAll();
        InstallRevision = publication.Revision;
        CountRevision = publication.Revision;
        TraceCounts(publication, rebuilt: true, reason);
        return new CouplingCountsApplyReceipt(true, true, true, CountRevision, publication.Delta.SequenceEdits.Length);
    }

    private bool CanApplyEdits(GrammarSequenceEdit[] edits)
    {
        // The packet coordinates are against the pre-publication sequence.  Descending,
        // disjoint edits preserve those coordinates as each earlier splice is applied.
        for (int i = 1; i < edits.Length; i++)
            if (edits[i - 1].Start < edits[i].Start + edits[i].RemovedLength)
                return false;
        return true;
    }

    private void ApplyEdit(GrammarSequenceEdit edit)
    {
        if (edit.Start > _symbols.Count || edit.RemovedLength > _symbols.Count - edit.Start)
            throw new ArgumentOutOfRangeException(nameof(edit));

        int oldCount = _symbols.Count;
        int oldEnd = edit.Start + edit.RemovedLength;
        RemoveNeighborhood(edit.Start, oldEnd, oldCount);
        _symbols.RemoveRange(edit.Start, edit.RemovedLength);
        _symbols.InsertRange(edit.Start, edit.Inserted);
        AddNeighborhood(edit.Start, edit.Start + edit.Inserted.Length, _symbols.Count);
    }

    private void RemoveNeighborhood(int start, int end, int count)
    {
        for (int i = Math.Max(0, start - _window); i < count; i++)
            for (int d = 1; d <= _window && i + d < count; d++)
            {
                int j = i + d;
                if (Touches(i, j, start, end)) DecrementCo(_symbols[i], _symbols[j], d);
            }
        RemoveSuccessors(start, end, count);
        for (int i = start; i < end; i++) DecrementMarginal(_symbols[i]);
    }

    private void AddNeighborhood(int start, int end, int count)
    {
        for (int i = Math.Max(0, start - _window); i < count; i++)
            for (int d = 1; d <= _window && i + d < count; d++)
            {
                int j = i + d;
                if (Touches(i, j, start, end)) IncrementCo(_symbols[i], _symbols[j], d);
            }
        AddSuccessors(start, end, count);
        for (int i = start; i < end; i++) IncrementMarginal(_symbols[i]);
    }

    private static bool Touches(int left, int right, int start, int end)
        => (uint)(left - start) < (uint)(end - start) || (uint)(right - start) < (uint)(end - start);

    private void RemoveSuccessors(int start, int end, int count)
    {
        int first = Math.Max(0, start - 2);
        int last = Math.Min(count - 3, end - 1);
        for (int i = first; i <= last; i++)
        {
            if (Touches(i, i + 1, start, end) || Touches(i + 1, i + 2, start, end))
                DecrementSuccessor(_symbols[i], _symbols[i + 1], _symbols[i + 2]);
        }
        int firstPair = Math.Max(0, start - 1);
        int lastPair = Math.Min(count - 2, end);
        for (int i = firstPair; i <= lastPair; i++)
            if (Touches(i, i + 1, start, end)) DecrementSuccessor(_symbols[i], _symbols[i + 1]);
    }

    private void AddSuccessors(int start, int end, int count)
    {
        int firstPair = Math.Max(0, start - 1);
        int lastPair = Math.Min(count - 2, end);
        for (int i = firstPair; i <= lastPair; i++)
            if (Touches(i, i + 1, start, end)) IncrementSuccessor(_symbols[i], _symbols[i + 1]);
        int first = Math.Max(0, start - 2);
        int last = Math.Min(count - 3, end - 1);
        for (int i = first; i <= last; i++)
            if (Touches(i, i + 1, start, end) || Touches(i + 1, i + 2, start, end))
                IncrementSuccessor(_symbols[i], _symbols[i + 1], _symbols[i + 2]);
    }

    private int CountDistinctSymbols() => _marginals.Count;

    private void BuildAll()
    {
        for (int i = 0; i < _symbols.Count; i++) IncrementMarginal(_symbols[i]);
        for (int i = 0; i < _symbols.Count; i++)
            for (int d = 1; d <= _window && i + d < _symbols.Count; d++) IncrementCo(_symbols[i], _symbols[i + d], d);
        for (int i = 0; i + 1 < _symbols.Count; i++) IncrementSuccessor(_symbols[i], _symbols[i + 1]);
        for (int i = 1; i + 1 < _symbols.Count; i++) IncrementSuccessor(_symbols[i - 1], _symbols[i], _symbols[i + 1]);
    }

    private void ClearEvidence()
    {
        _marginals.Clear();
        for (int d = 1; d <= _window; d++) _coCounts[d].Clear();
        _successors.Clear(); _successors2.Clear(); _successorTotals.Clear(); _successorTotals2.Clear();
    }

    private void IncrementMarginal(Symbol symbol) => _marginals[symbol.Value] = _marginals.GetValueOrDefault(symbol.Value) + 1;
    private void DecrementMarginal(Symbol symbol)
    {
        int count = _marginals[symbol.Value] - 1;
        if (count == 0) _marginals.Remove(symbol.Value); else _marginals[symbol.Value] = count;
    }

    private void IncrementCo(Symbol left, Symbol right, int distance)
    {
        long key = Couplings.PackPair(left, right);
        _coCounts[distance][key] = _coCounts[distance].GetValueOrDefault(key) + 1;
    }
    private void DecrementCo(Symbol left, Symbol right, int distance)
    {
        long key = Couplings.PackPair(left, right);
        int count = _coCounts[distance][key] - 1;
        if (count == 0) _coCounts[distance].Remove(key); else _coCounts[distance][key] = count;
    }

    private void IncrementSuccessor(Symbol left, Symbol right)
    {
        if (!_successors.TryGetValue(left.Value, out var map)) _successors[left.Value] = map = new();
        map[right.Value] = map.GetValueOrDefault(right.Value) + 1;
        _successorTotals[left.Value] = _successorTotals.GetValueOrDefault(left.Value) + 1;
    }
    private void DecrementSuccessor(Symbol left, Symbol right)
    {
        var map = _successors[left.Value];
        int count = map[right.Value] - 1;
        if (count == 0) map.Remove(right.Value); else map[right.Value] = count;
        if (map.Count == 0) _successors.Remove(left.Value);
        int total = _successorTotals[left.Value] - 1;
        if (total == 0) _successorTotals.Remove(left.Value); else _successorTotals[left.Value] = total;
    }
    private void IncrementSuccessor(Symbol left, Symbol middle, Symbol right)
    {
        var key = (left.Value, middle.Value);
        if (!_successors2.TryGetValue(key, out var map)) _successors2[key] = map = new();
        map[right.Value] = map.GetValueOrDefault(right.Value) + 1;
        _successorTotals2[key] = _successorTotals2.GetValueOrDefault(key) + 1;
    }
    private void DecrementSuccessor(Symbol left, Symbol middle, Symbol right)
    {
        var key = (left.Value, middle.Value);
        var map = _successors2[key];
        int count = map[right.Value] - 1;
        if (count == 0) map.Remove(right.Value); else map[right.Value] = count;
        if (map.Count == 0) _successors2.Remove(key);
        int total = _successorTotals2[key] - 1;
        if (total == 0) _successorTotals2.Remove(key); else _successorTotals2[key] = total;
    }

    private static void TraceCounts(InstallRevision publication, bool rebuilt, string reason)
        => Trace.Engine.Event($"grammar.coupling-counts revision={publication.Revision} rebuilt={(rebuilt ? "yes" : "no")} reason={reason} edits={publication.Delta.SequenceEdits.Length}");
}

public readonly record struct CouplingCountsApplyReceipt(
    bool Rebuilt,
    bool Changed,
    bool CountRevisionChanged,
    GrammarRevisionID CountRevision,
    int SequenceEdits);
