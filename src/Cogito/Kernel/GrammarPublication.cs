namespace Cogito.Grammar;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Cogito.Induct;

/// Stable identity for a published grammar revision. The old grammar surface used a bare
/// <see cref="ulong"/> version; this atom makes a revision impossible to confuse with a step,
/// event, or byte count while the Cortex cutover is still in flight.
public readonly struct GrammarRevisionID(ulong value) : IEquatable<GrammarRevisionID>, IComparable<GrammarRevisionID>
{
    public ulong Value { get; } = value;

    public static GrammarRevisionID Zero => new(0);

    public GrammarRevisionID Next() => new(checked(Value + 1));

    public int CompareTo(GrammarRevisionID other) => Value.CompareTo(other.Value);
    public bool Equals(GrammarRevisionID other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is GrammarRevisionID other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();

    public static bool operator ==(GrammarRevisionID left, GrammarRevisionID right) => left.Equals(right);
    public static bool operator !=(GrammarRevisionID left, GrammarRevisionID right) => !left.Equals(right);
    public static bool operator <(GrammarRevisionID left, GrammarRevisionID right) => left.Value < right.Value;
    public static bool operator >(GrammarRevisionID left, GrammarRevisionID right) => left.Value > right.Value;
}

/// Why a installRevision cannot be applied as a local append. A non-None value is an explicit
/// consumer instruction to replace its derived state from the accompanying snapshot.
public enum GrammarResetKinds : byte
{
    None,
    Initial,
    Rebuild,
    Rebase,
    Compact,
    Breach,
    Reindex,
}

/// A sequence splice in the compressed grammar view. Wave 1 only needs append and whole-view
/// replacement; the shape is deliberately concrete so Loom can add segment-local edits later.
public readonly struct GrammarSequenceEdit
{
    public GrammarSequenceEdit(int start, int removedLength, Symbol[] inserted)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (removedLength < 0) throw new ArgumentOutOfRangeException(nameof(removedLength));
        Inserted = inserted ?? throw new ArgumentNullException(nameof(inserted));
        Start = start;
        RemovedLength = removedLength;
    }

    public int Start { get; }
    public int RemovedLength { get; }
    public Symbol[] Inserted { get; }

    public static GrammarSequenceEdit Replace(int start, int removedLength, Symbol[] inserted)
        => new(start, removedLength, inserted);
}

/// The typed mutation packet between two grammar snapshots. A reset carries the complete
/// replacement snapshot through GrammarInstallRevision; AddedRules and SequenceEdits remain useful
/// accounting/oracle data even while consumers still rebuild their old caches.
public readonly struct GrammarDelta
{
    public GrammarDelta(
        GrammarRevisionID parentRevision,
        GrammarRevisionID revision,
        GrammarRule[] addedRules,
        RuleID[] removedRules,
        GrammarSequenceEdit[] sequenceEdits,
        Mbits mdlDelta,
        GrammarResetKinds reset)
    {
        if (revision < parentRevision)
            throw new ArgumentException("grammar revision moved backwards", nameof(revision));
        ParentRevision = parentRevision;
        Revision = revision;
        AddedRules = addedRules ?? throw new ArgumentNullException(nameof(addedRules));
        RemovedRules = removedRules ?? throw new ArgumentNullException(nameof(removedRules));
        SequenceEdits = sequenceEdits ?? throw new ArgumentNullException(nameof(sequenceEdits));
        MDLDelta = mdlDelta;
        Reset = reset;
    }

    public GrammarRevisionID ParentRevision { get; }
    public GrammarRevisionID Revision { get; }
    public GrammarRule[] AddedRules { get; }
    public RuleID[] RemovedRules { get; }
    public GrammarSequenceEdit[] SequenceEdits { get; }
    public Mbits MDLDelta { get; }
    public GrammarResetKinds Reset { get; }
    public bool IsEmpty => AddedRules.Length == 0 && RemovedRules.Length == 0 && SequenceEdits.Length == 0 && Reset == GrammarResetKinds.None;

    public static GrammarDelta CreateEmpty(GrammarRevisionID revision)
        => new(revision, revision, [], [], [], Mbits.Zero, GrammarResetKinds.None);
}

/// Immutable-at-the-boundary grammar materialization. Arrays are copied on construction so the
/// installRevision oracle can compare a stable image even while legacy Re-Pair consumers mutate their
/// own working arrays.
public sealed class GrammarSnapshot
{
    private readonly GrammarRule[]? _sourceRules;
    private readonly Symbol[]? _sourceCompressed;

    private GrammarSnapshot(
        GrammarRevisionID revision,
        GrammarRule[] rules,
        Symbol[] compressed,
        Mbits totalSavings,
        uint alphabetSize,
        bool takeOwnership,
        GrammarRule[]? sourceRules = null,
        Symbol[]? sourceCompressed = null,
        ulong? contentDigest = null)
    {
        Revision = revision;
        Rules = rules is null ? throw new ArgumentNullException(nameof(rules)) : takeOwnership ? rules : (GrammarRule[])rules.Clone();
        Compressed = compressed is null ? throw new ArgumentNullException(nameof(compressed)) : takeOwnership ? compressed : (Symbol[])compressed.Clone();
        TotalSavings = totalSavings;
        AlphabetSize = alphabetSize;
        ContentDigest = contentDigest ?? ComputeContentDigest(Rules, Compressed);
        _sourceRules = sourceRules;
        _sourceCompressed = sourceCompressed;
    }

    public GrammarSnapshot(
        GrammarRevisionID revision,
        GrammarRule[] rules,
        Symbol[] compressed,
        Mbits totalSavings,
        uint alphabetSize)
        : this(revision, rules, compressed, totalSavings, alphabetSize, takeOwnership: false) { }

    public GrammarRevisionID Revision { get; }
    public GrammarRule[] Rules { get; }
    public Symbol[] Compressed { get; }
    public Mbits TotalSavings { get; }
    public uint AlphabetSize { get; }
    /// Structural identity of the effective grammar image.  The readout corpus index is
    /// scoped by this digest plus Revision; no readout may cross either boundary.
    public ulong ContentDigest { get; }

    internal ulong RecomputeContentDigest() => ComputeContentDigest(Rules, Compressed);

    private static ulong ComputeContentDigest(GrammarRule[] rules, Symbol[] compressed)
    {
        ulong hash = 14695981039346656037UL;
        static void Mix(ref ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        Mix(ref hash, (ulong)rules.Length);
        for (int index = 0; index < rules.Length; index++)
        {
            GrammarRule rule = rules[index];
            foreach (ulong value in MemoryMarshal.Cast<byte, ulong>(rule.Id.Hash.AsSpan())) Mix(ref hash, value);
            Mix(ref hash, unchecked((ulong)rule.Cost.Value));
            Mix(ref hash, (byte)rule.Kind);
            Mix(ref hash, (ulong)rule.Pattern.Length);
            for (int symbol = 0; symbol < rule.Pattern.Length; symbol++) Mix(ref hash, rule.Pattern[symbol].Value);
            if (rule.Segs is null)
            {
                Mix(ref hash, 0);
            }
            else
            {
                Mix(ref hash, (ulong)rule.Segs.Length);
                for (int segment = 0; segment < rule.Segs.Length; segment++)
                {
                    Mix(ref hash, unchecked((ulong)rule.Segs[segment].Id.Value));
                    Mix(ref hash, unchecked((ulong)rule.Segs[segment].Start));
                    Mix(ref hash, unchecked((ulong)rule.Segs[segment].Len));
                }
            }
        }
        Mix(ref hash, (ulong)compressed.Length);
        for (int index = 0; index < compressed.Length; index++) Mix(ref hash, compressed[index].Value);
        return hash == 0 ? 1 : hash;
    }

    public static GrammarSnapshot FromRePair(GrammarRevisionID revision, in RePairResult result)
    {
        // One owned image serves both the snapshot and the reset delta. The old path cloned
        // each array once for the snapshot and once again for AddedRules/Inserted, quadrupling
        // the large installRevision copy at every grammar harvest.
        GrammarRule[] rules = (GrammarRule[])result.Rules.Clone();
        Symbol[] compressed = (Symbol[])result.Compressed.Clone();
        return new GrammarSnapshot(revision, rules, compressed, result.TotalSavings, result.AlphabetSize,
            takeOwnership: true, sourceRules: result.Rules, sourceCompressed: result.Compressed);
    }

    public RePairResult ToRePairResult(bool cloneArrays = true)
        => cloneArrays
            ? new((GrammarRule[])Rules.Clone(), (Symbol[])Compressed.Clone(), TotalSavings, AlphabetSize)
            : new(_sourceRules ?? Rules, _sourceCompressed ?? Compressed, TotalSavings, AlphabetSize);

    internal bool HasSourceImage(in RePairResult result)
        => AlphabetSize == result.AlphabetSize
            && TotalSavings.Equals(result.TotalSavings)
            && ReferenceEquals(_sourceRules, result.Rules)
            && ReferenceEquals(_sourceCompressed, result.Compressed);

    internal GrammarSnapshot AdvanceRevision(GrammarRevisionID revision, in RePairResult result)
    {
        if (!HasSourceImage(in result))
            throw new InvalidOperationException("cannot advance a grammar snapshot from an unrelated result");
        // Rules/Compressed are reference-identical by the HasSourceImage contract, so the parent's
        // digest is already the digest of this image — rehashing 15k rules here made every no-op
        // installRevision pay a full content walk.
        return new GrammarSnapshot(revision, Rules, Compressed, result.TotalSavings, result.AlphabetSize,
            takeOwnership: true, sourceRules: _sourceRules, sourceCompressed: _sourceCompressed, contentDigest: ContentDigest);
    }

    public bool Matches(in RePairResult result)
    {
        if (HasSourceImage(in result)) return true;
        if (!TotalSavings.Equals(result.TotalSavings) || AlphabetSize != result.AlphabetSize)
            return false;
        return RulesMatch(Rules, result.Rules) && Compressed.AsSpan().SequenceEqual(result.Compressed);
    }

    public bool Matches(GrammarSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Revision == other.Revision
            && TotalSavings.Equals(other.TotalSavings)
            && AlphabetSize == other.AlphabetSize
            && RulesMatch(Rules, other.Rules)
            && Compressed.AsSpan().SequenceEqual(other.Compressed);
    }

    private static bool RulesMatch(GrammarRule[] left, GrammarRule[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            GrammarRule a = left[i];
            GrammarRule b = right[i];
            if (!a.Id.Equals(b.Id) || !a.Cost.Equals(b.Cost) || a.Kind != b.Kind)
                return false;
            if (!a.Pattern.AsSpan().SequenceEqual(b.Pattern))
                return false;
            if (a.Segs is null || b.Segs is null)
            {
                if (a.Segs is not null || b.Segs is not null) return false;
            }
            else if (!a.Segs.AsSpan().SequenceEqual(b.Segs))
                return false;
        }
        return true;
    }
}

/// A non-RePair grammar layer owned by a side organ (currently AntiUnify).  Loom's
/// standing grammar is deliberately pure, emission-ordered binary RePair; this
/// layer therefore remains outside its rank encoder.  References are carried by
/// RuleID rather than the composed array position so a later binary mint can grow
/// the base without invalidating the overlay's symbols.
public sealed class GrammarOverlay
{
    private readonly RuleID[] _baseRuleIDs;
    // The complete rule image that originally anchored the side layer.  IDs alone are
    // insufficient here: a caller can preserve an ID while changing its cost/body kind.
    // The append fast path may skip a rebind only after this prefix is still byte-for-byte
    // the same rule image.
    private readonly GrammarRule[] _baseRuleImage;
    private readonly GrammarRule[] _rules;
    private GrammarRule[]? _validatedBaseRules;
    private GrammarSnapshot? _composedBase;
    private GrammarSnapshot? _composedSnapshot;
    private int _composeCount;
    private bool _trustNextBaseAppend;

    private GrammarOverlay(RuleID[] baseRuleIDs, GrammarRule[] rules, uint alphabetSize, GrammarRule[] baseRuleImage)
    {
        _baseRuleIDs = baseRuleIDs;
        _rules = rules;
        AlphabetSize = alphabetSize;
        _baseRuleImage = baseRuleImage;
    }

    public uint AlphabetSize { get; }
    public IReadOnlyList<RuleID> BaseRuleIDs => _baseRuleIDs;
    public IReadOnlyList<GrammarRule> Rules => _rules;
    public int BaseRuleCount => _baseRuleIDs.Length;
    public int ComposeCount => _composeCount;
    public int RuleCount => _rules.Length;

    /// Capture the suffix after an authoritative pure base.  The base prefix is
    /// checked strictly; accepting a coincidental suffix would make a installRevision
    /// claim an ancestry it does not possess.
    public static GrammarOverlay FromComposed(GrammarSnapshot baseSnapshot, in RePairResult composed)
    {
        ArgumentNullException.ThrowIfNull(baseSnapshot);
        if (baseSnapshot.AlphabetSize != composed.AlphabetSize)
            throw new ArgumentException("grammar overlay alphabet differs from its base", nameof(composed));
        if (composed.Rules.Length < baseSnapshot.Rules.Length)
            throw new ArgumentException("grammar overlay composed image is shorter than its base", nameof(composed));
        GrammarOverlay? overlay = CreateFromComposed(baseSnapshot, in composed, validatePrefix: true);
        if (overlay is not null) return overlay;
        RuleID[] baseIDs = new RuleID[baseSnapshot.Rules.Length];
        for (int i = 0; i < baseIDs.Length; i++) baseIDs[i] = baseSnapshot.Rules[i].Id;
        return new GrammarOverlay(baseIDs, [], baseSnapshot.AlphabetSize, baseSnapshot.ToRePairResult(cloneArrays: false).Rules);
    }

    public static GrammarOverlay? TryFromComposed(GrammarSnapshot baseSnapshot, in RePairResult composed)
    {
        if (composed.AlphabetSize != baseSnapshot.AlphabetSize || composed.Rules.Length < baseSnapshot.Rules.Length)
            return null;
        for (int i = 0; i < baseSnapshot.Rules.Length; i++)
            if (!RulesMatch(baseSnapshot.Rules[i], composed.Rules[i])) return null;
        return CreateFromComposed(baseSnapshot, in composed, validatePrefix: false);
    }

    /// Reuse the prior side layer when a Loom base only grew. The suffix is the semantic
    /// identity; the base binding is validated only when a changed suffix must be published.
    public static GrammarOverlay? TryFromComposed(GrammarSnapshot baseSnapshot, in RePairResult composed, GrammarOverlay? prior)
    {
        if (prior is null) return TryFromComposed(baseSnapshot, in composed);
        if (composed.AlphabetSize != baseSnapshot.AlphabetSize || composed.Rules.Length < baseSnapshot.Rules.Length)
            return null;
        int start = baseSnapshot.Rules.Length;
        GrammarRule[] baseRules = baseSnapshot.ToRePairResult(cloneArrays: false).Rules;
        // A longer base is not trustworthy by length alone.  Check the complete image
        // that created the prior overlay before arming the append fast path; this catches
        // changed cost/kind/segments even when content IDs happen to remain equal.
        if (!ReferenceEquals(prior._baseRuleImage, baseRules)
            && !MatchesPrefix(prior._baseRuleImage, baseRules, 0)) return null;
        if (composed.Rules.Length == start)
        {
            if (start > prior._baseRuleIDs.Length) prior._trustNextBaseAppend = true;
            return prior;
        }
        if (composed.Rules.Length == start + prior._rules.Length)
        {
            bool same = true;
            for (int i = 0; i < prior._rules.Length; i++)
                if (!RulesMatch(prior._rules[i], composed.Rules[start + i])) { same = false; break; }
            if (same)
            {
                if (start > prior._baseRuleIDs.Length) prior._trustNextBaseAppend = true;
                return prior;
            }
        }
        if (composed.Rules.Length > start + prior._rules.Length
            && MatchesPrefix(prior._rules, composed.Rules, start))
        {
            int added = composed.Rules.Length - start - prior._rules.Length;
            GrammarRule[] rules = new GrammarRule[prior._rules.Length + added];
            Array.Copy(prior._rules, rules, prior._rules.Length);
            Array.Copy(composed.Rules, start + prior._rules.Length, rules, prior._rules.Length, added);
            var extended = new GrammarOverlay(prior._baseRuleIDs, rules, prior.AlphabetSize, prior._baseRuleImage)
            {
                _validatedBaseRules = prior._validatedBaseRules,
                _trustNextBaseAppend = start > prior._baseRuleIDs.Length,
            };
            return extended;
        }
        return TryFromComposed(baseSnapshot, in composed);
    }

    public static GrammarOverlay? TryFromComposed(in RePairResult baseGrammar, in RePairResult composed)
    {
        if (baseGrammar.AlphabetSize != composed.AlphabetSize || composed.Rules.Length < baseGrammar.Rules.Length)
            return null;
        for (int i = 0; i < baseGrammar.Rules.Length; i++)
            if (!RulesMatch(baseGrammar.Rules[i], composed.Rules[i])) return null;
        if (composed.Rules.Length == baseGrammar.Rules.Length) return null;
        var baseIDs = new RuleID[baseGrammar.Rules.Length];
        for (int i = 0; i < baseIDs.Length; i++) baseIDs[i] = baseGrammar.Rules[i].Id;
        var overlay = new GrammarRule[composed.Rules.Length - baseGrammar.Rules.Length];
        Array.Copy(composed.Rules, baseGrammar.Rules.Length, overlay, 0, overlay.Length);
        return new GrammarOverlay(baseIDs, overlay, baseGrammar.AlphabetSize, baseGrammar.Rules);
    }

    public static GrammarOverlay? TryFromComposed(in RePairResult baseGrammar, in RePairResult composed, GrammarOverlay? prior)
    {
        if (prior is null) return TryFromComposed(in baseGrammar, in composed);
        if (baseGrammar.AlphabetSize != composed.AlphabetSize || composed.Rules.Length < baseGrammar.Rules.Length)
            return null;
        int start = baseGrammar.Rules.Length;
        if (!ReferenceEquals(prior._baseRuleImage, baseGrammar.Rules)
            && !MatchesPrefix(prior._baseRuleImage, baseGrammar.Rules, 0)) return null;
        if (composed.Rules.Length == start)
        {
            if (start > prior._baseRuleIDs.Length) prior._trustNextBaseAppend = true;
            return prior;
        }
        if (composed.Rules.Length == start + prior._rules.Length)
        {
            bool same = true;
            for (int i = 0; i < prior._rules.Length; i++)
                if (!RulesMatch(prior._rules[i], composed.Rules[start + i])) { same = false; break; }
            if (same)
            {
                if (start > prior._baseRuleIDs.Length) prior._trustNextBaseAppend = true;
                return prior;
            }
        }
        if (composed.Rules.Length > start + prior._rules.Length
            && MatchesPrefix(prior._rules, composed.Rules, start))
        {
            int added = composed.Rules.Length - start - prior._rules.Length;
            GrammarRule[] rules = new GrammarRule[prior._rules.Length + added];
            Array.Copy(prior._rules, rules, prior._rules.Length);
            Array.Copy(composed.Rules, start + prior._rules.Length, rules, prior._rules.Length, added);
            var extended = new GrammarOverlay(prior._baseRuleIDs, rules, prior.AlphabetSize, prior._baseRuleImage)
            {
                _validatedBaseRules = prior._validatedBaseRules,
                _trustNextBaseAppend = start > prior._baseRuleIDs.Length,
            };
            return extended;
        }
        return TryFromComposed(in baseGrammar, in composed);
    }

    public bool IsEmpty => _rules.Length == 0;

    public bool ContentEquals(GrammarOverlay other)
    {
        ArgumentNullException.ThrowIfNull(other);
        // Base growth does not alter side-layer semantics. Base IDs remain a binding
        // witness (ValidateBase), but are deliberately excluded from installRevision identity.
        if (AlphabetSize != other.AlphabetSize || _rules.Length != other._rules.Length)
            return false;
        for (int i = 0; i < _rules.Length; i++)
        {
            if (!RulesMatch(_rules[i], other._rules[i])) return false;
        }
        return true;
    }

    /// Materialize the composed view for a consumer that explicitly needs the
    /// side layer.  Existing base consumers continue to read GrammarInstallRevision.Snapshot
    /// and never force this allocation.
    public GrammarSnapshot Compose(GrammarSnapshot baseSnapshot)
    {
        ArgumentNullException.ThrowIfNull(baseSnapshot);
        ValidateBase(baseSnapshot);
        if (ReferenceEquals(_composedBase, baseSnapshot) && _composedSnapshot is not null)
            return _composedSnapshot;

        var byBaseID = new Dictionary<RuleID, int>(_baseRuleIDs.Length);
        for (int i = 0; i < _baseRuleIDs.Length; i++)
            byBaseID.Add(_baseRuleIDs[i], i);
        var byOverlayID = new Dictionary<RuleID, int>(_rules.Length);
        for (int i = 0; i < _rules.Length; i++) byOverlayID.Add(_rules[i].Id, i);

        GrammarRule[] rules = new GrammarRule[baseSnapshot.Rules.Length + _rules.Length];
        Array.Copy(baseSnapshot.Rules, rules, baseSnapshot.Rules.Length);
        for (int i = 0; i < _rules.Length; i++)
        {
            GrammarRule source = _rules[i];
            Symbol[] pattern = new Symbol[source.Pattern.Length];
            for (int j = 0; j < pattern.Length; j++)
            {
                Symbol symbol = source.Pattern[j];
                if (symbol.Value < AlphabetSize) { pattern[j] = symbol; continue; }
                uint index = symbol.Value - AlphabetSize;
                if (index < (uint)_baseRuleIDs.Length)
                {
                    RuleID id = _baseRuleIDs[index];
                    if (!byBaseID.TryGetValue(id, out int rebased))
                        throw new InvalidDataException("grammar overlay references a missing base rule");
                    pattern[j] = new Symbol(AlphabetSize + (uint)rebased);
                }
                else
                {
                    uint overlayIndex = index - (uint)_baseRuleIDs.Length;
                    if (overlayIndex >= (uint)_rules.Length)
                        throw new InvalidDataException("grammar overlay references a missing overlay rule");
                    RuleID id = _rules[overlayIndex].Id;
                    if (!byOverlayID.TryGetValue(id, out int rebased))
                        throw new InvalidDataException("grammar overlay references a missing overlay rule");
                    pattern[j] = new Symbol(AlphabetSize + (uint)baseSnapshot.Rules.Length + (uint)rebased);
                }
            }
            rules[baseSnapshot.Rules.Length + i] = new GrammarRule(source.Id, pattern, source.Cost, source.Kind, source.Segs);
        }
        _composedBase = baseSnapshot;
        _composedSnapshot = new GrammarSnapshot(baseSnapshot.Revision, rules, baseSnapshot.Compressed, baseSnapshot.TotalSavings, baseSnapshot.AlphabetSize);
        _composeCount++;
        return _composedSnapshot;
    }

    private static GrammarOverlay? CreateFromComposed(GrammarSnapshot baseSnapshot, in RePairResult composed, bool validatePrefix)
    {
        int baseCount = baseSnapshot.Rules.Length;
        for (int i = 0; validatePrefix && i < baseCount; i++)
            if (!RulesMatch(baseSnapshot.Rules[i], composed.Rules[i]))
                throw new ArgumentException($"grammar overlay base prefix diverges at rule {i}", nameof(composed));
        if (composed.Rules.Length == baseCount) return null;
        GrammarRule[] overlay = new GrammarRule[composed.Rules.Length - baseCount];
        Array.Copy(composed.Rules, baseCount, overlay, 0, overlay.Length);
        RuleID[] baseIDs = new RuleID[baseCount];
        for (int i = 0; i < baseIDs.Length; i++) baseIDs[i] = baseSnapshot.Rules[i].Id;
        return new GrammarOverlay(baseIDs, overlay, baseSnapshot.AlphabetSize, baseSnapshot.ToRePairResult(cloneArrays: false).Rules);
    }

    private static bool MatchesPrefix(GrammarRule[] expected, GrammarRule[] actual, int start)
    {
        if (actual.Length - start < expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
            if (!RulesMatch(expected[i], actual[start + i])) return false;
        return true;
    }

    internal void ValidateBase(GrammarSnapshot baseSnapshot)
    {
        if (baseSnapshot.AlphabetSize != AlphabetSize)
            throw new InvalidDataException("grammar overlay alphabet differs from its base");
        if (ReferenceEquals(_validatedBaseRules, baseSnapshot.Rules)) return;
        GrammarRule[] baseRules = baseSnapshot.ToRePairResult(cloneArrays: false).Rules;
        if (_trustNextBaseAppend && baseSnapshot.Rules.Length > _baseRuleIDs.Length)
        {
            if (!MatchesPrefix(_baseRuleImage, baseRules, 0))
                throw new InvalidDataException("grammar overlay base prefix diverged before an append");
            _validatedBaseRules = baseSnapshot.Rules;
            _trustNextBaseAppend = false;
            return;
        }
        if (baseSnapshot.Rules.Length < _baseRuleIDs.Length)
            throw new InvalidDataException("grammar overlay base lost an earlier binary rule");
        if (!MatchesPrefix(_baseRuleImage, baseRules, 0))
            throw new InvalidDataException("grammar overlay base prefix diverged");
        _validatedBaseRules = baseSnapshot.Rules;
    }

    private static bool RulesMatch(in GrammarRule left, in GrammarRule right)
        => left.Id.Equals(right.Id) && left.Cost.Equals(right.Cost) && left.Kind == right.Kind
            && left.Pattern.AsSpan().SequenceEqual(right.Pattern)
            && (left.Segs is null ? right.Segs is null : right.Segs is not null && left.Segs.AsSpan().SequenceEqual(right.Segs));
}

/// A published revision plus its typed mutation and accounting receipt. This is the additive
/// bridge from the existing RePairResult surface; Loom and the Cortex consumers can adopt it in
/// later waves without changing the durable GrammarSpec/GrammarVersionEvent wire schemas today.
public readonly struct InstallRevision
{
    public InstallRevision(GrammarSnapshot snapshot, GrammarDelta delta)
        : this(snapshot, delta, null, null) { }

    public InstallRevision(GrammarSnapshot snapshot, GrammarDelta delta, GrammarFoldProvenanceReceipt? foldProvenance)
        : this(snapshot, delta, foldProvenance, null) { }

    private InstallRevision(GrammarSnapshot snapshot, GrammarDelta delta, GrammarFoldProvenanceReceipt? foldProvenance, GrammarOverlay? overlay)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Delta = delta;
        if (snapshot.Revision != delta.Revision)
            throw new ArgumentException("installRevision snapshot and delta revisions differ", nameof(delta));
        if (foldProvenance is { } provenance)
        {
            provenance.Validate();
            if (provenance.Revision != snapshot.Revision || provenance.PreviousRevision != delta.ParentRevision)
                throw new ArgumentException("grammar fold provenance does not bind its installRevision", nameof(foldProvenance));
        }
        FoldProvenance = foldProvenance;
        Overlay = overlay;
    }

    public GrammarSnapshot Snapshot { get; }
    public GrammarDelta Delta { get; }
    public GrammarRevisionID Revision => Snapshot.Revision;
    public GrammarRevisionID ParentRevision => Delta.ParentRevision;
    public GrammarResetKinds Reset => Delta.Reset;
    public GrammarFoldProvenanceReceipt? FoldProvenance { get; }
    /// Optional side grammar. Snapshot remains the pure installRevision authority;
    /// callers that need AntiUnify's generative layer opt into EffectiveSnapshot.
    public GrammarOverlay? Overlay { get; }
    public GrammarSnapshot EffectiveSnapshot => Overlay is null ? Snapshot : Overlay.Compose(Snapshot);

    internal ReadoutCorpusIndex GetReadoutCorpusIndex()
    {
        GrammarSnapshot effective = EffectiveSnapshot;
        InstallRevision installRevision = this;
        return ReadoutCorpusIndexes.Get(effective, () =>
        {
            installRevision.ValidateReadoutConsistency(effective);
            return ReadoutCorpusIndex.Build(effective);
        });
    }

    public InstallRevision WithOverlay(GrammarOverlay? overlay)
    {
        if (overlay is not null) overlay.ValidateBase(Snapshot); // validate the typed base binding without materializing the side view
        return new InstallRevision(Snapshot, Delta, FoldProvenance, overlay);
    }

    public InstallRevisionReceipt Account()
        => new(
            ParentRevision,
            Revision,
            Delta.Reset,
            Delta.AddedRules.Length,
            Delta.RemovedRules.Length,
            Delta.SequenceEdits.Length,
            Snapshot.Rules.Length,
            Snapshot.Compressed.Length,
            Delta.MDLDelta,
            Snapshot.Rules.Length,
            Snapshot.Compressed.Length,
            2,
            Delta.AddedRules.Length == Snapshot.Rules.Length
                && Delta.SequenceEdits.Length == 1
                && ReferenceEquals(Delta.AddedRules, Snapshot.Rules)
                && ReferenceEquals(Delta.SequenceEdits[0].Inserted, Snapshot.Compressed))
        {
            FoldProvenance = FoldProvenance,
        };

    public bool Matches(in RePairResult result) => Snapshot.Matches(in result);

    /// Read a learned continuation directly from this installRevision. The installRevision validates its
    /// snapshot/delta pair before spending the caller's reserved work; no snapshot-only side door
    /// can score policy continuations.
    public bool TryChooseContinuation(
        ReadOnlySpan<byte> context,
        IReadOnlyList<byte[]> continuations,
        GrammarContinuationQuota quota,
        int deliberationDepth,
        out GrammarContinuationDecision decision)
        => TryChooseContinuation(context, continuations, quota, deliberationDepth, out decision, out _);

    public bool TryChooseContinuation(
        ReadOnlySpan<byte> context,
        IReadOnlyList<byte[]> continuations,
        GrammarContinuationQuota quota,
        int deliberationDepth,
        out GrammarContinuationDecision decision,
        out GrammarContinuationReadoutReceipt receipt)
    {
        ReadoutCorpusIndex index = GetReadoutCorpusIndex();
        return index.TryChooseContinuation(context, continuations, quota, deliberationDepth, out decision, out receipt);
    }

    public byte[] ReconstructPublishedBytes()
    {
        GrammarSnapshot effective = EffectiveSnapshot;
        ValidateReadoutConsistency(effective);
        return Reconstruct.Expand(effective.Rules, effective.Compressed);
    }

    private void ValidateReadoutConsistency(GrammarSnapshot? effective = null)
    {
        if (Snapshot.Revision != Delta.Revision)
            throw new InvalidDataException("grammar installRevision snapshot/delta revision mismatch");
        effective ??= EffectiveSnapshot;
        HashSet<RuleID> snapshotRules = new();
        Dictionary<RuleID, GrammarRule> snapshotRuleByID = new();
        for (int index = 0; index < effective.Rules.Length; index++)
        {
            GrammarRule rule = effective.Rules[index];
            if (!snapshotRules.Add(rule.Id))
                throw new InvalidDataException("grammar installRevision snapshot repeats a rule identity");
            snapshotRuleByID.Add(rule.Id, rule);
        }
        HashSet<RuleID> added = new();
        for (int index = 0; index < Delta.AddedRules.Length; index++)
        {
            GrammarRule rule = Delta.AddedRules[index];
            if (!added.Add(rule.Id))
                throw new InvalidDataException("grammar installRevision delta repeats an added rule");
            if (!snapshotRuleByID.TryGetValue(rule.Id, out GrammarRule published))
                throw new InvalidDataException("grammar installRevision delta adds a rule absent from its snapshot");
            if (!rule.Pattern.AsSpan().SequenceEqual(published.Pattern))
                throw new InvalidDataException("grammar installRevision delta rule body differs from its snapshot");
        }
        HashSet<RuleID> removed = new();
        for (int index = 0; index < Delta.RemovedRules.Length; index++)
        {
            if (!removed.Add(Delta.RemovedRules[index]))
                throw new InvalidDataException("grammar installRevision delta repeats a removed rule");
            if (snapshotRules.Contains(Delta.RemovedRules[index]))
                throw new InvalidDataException("grammar installRevision snapshot retains a delta-removed rule");
        }
        uint alphabetSize = effective.AlphabetSize;
        int ruleCount = effective.Rules.Length;
        for (int index = 0; index < effective.Compressed.Length; index++)
            ValidateSymbol(effective.Compressed[index], alphabetSize, ruleCount);
        for (int rule = 0; rule < effective.Rules.Length; rule++)
            for (int symbol = 0; symbol < effective.Rules[rule].Pattern.Length; symbol++)
                ValidateSymbol(effective.Rules[rule].Pattern[symbol], alphabetSize, ruleCount);
        for (int edit = 0; edit < Delta.SequenceEdits.Length; edit++)
            for (int symbol = 0; symbol < Delta.SequenceEdits[edit].Inserted.Length; symbol++)
                ValidateSymbol(Delta.SequenceEdits[edit].Inserted[symbol], alphabetSize, ruleCount);
    }

    private static void ValidateSymbol(Symbol symbol, uint alphabetSize, int ruleCount)
    {
        if (symbol.Value < alphabetSize) return;
        uint ruleIndex = symbol.Value - alphabetSize;
        if (ruleIndex >= (uint)ruleCount)
            throw new InvalidDataException("grammar installRevision references a non-existent rule symbol");
    }

    /// Adapt one legacy Re-Pair result into a installRevision. The conservative rebuild reset is
    /// intentional: until Loom emits local edits, no consumer may mistake a full result for an
    /// append-only delta. The adapter is the wave-1 oracle seam, not the final hot path.
    public static InstallRevision FromRePair(
        GrammarRevisionID revision,
        GrammarRevisionID parentRevision,
        in RePairResult result)
    {
        GrammarSnapshot snapshot = GrammarSnapshot.FromRePair(revision, in result);
        return CreateRebuildInstallRevision(snapshot, revision, parentRevision, result.TotalSavings);
    }

    public static InstallRevision FromRePair(
        GrammarRevisionID revision,
        GrammarRevisionID parentRevision,
        in RePairResult result,
        GrammarSnapshot? parentSnapshot,
        GrammarOverlay? overlay)
        => FromRePair(revision, parentRevision, in result, parentSnapshot).WithOverlay(overlay);

    /// Adapt a result when the caller still owns the preceding installRevision image. Re-Pair emits
    /// rules in dependency order, so an unchanged emission prefix is a truthful append delta;
    /// the compressed view then contributes one minimal splice. Results that reorder or rewrite
    /// an existing rule deliberately retain the conservative rebuild reset.
    public static InstallRevision FromRePair(
        GrammarRevisionID revision,
        GrammarRevisionID parentRevision,
        in RePairResult result,
        GrammarSnapshot? parentSnapshot)
    {
        if (parentSnapshot is null || parentSnapshot.Revision != parentRevision || parentSnapshot.AlphabetSize != result.AlphabetSize)
            return FromRePair(revision, parentRevision, in result);

        // Loom.Result is identity-stable between mutations.  A sleep/anti-unify close may
        // therefore hand us the exact prior image with a new installRevision boundary.  Keep the
        // owned snapshot arrays and publish an empty base delta; cloning 15k rules + 375k
        // symbols here was the apparent "grammar.analysis" no-op chug.
        if (parentSnapshot.HasSourceImage(in result))
        {
            GrammarSnapshot noOpSnapshot = parentSnapshot.AdvanceRevision(revision, in result);
            GrammarDelta noOpDelta = new(parentRevision, revision, [], [], [], Mbits.Zero, GrammarResetKinds.None);
            return new InstallRevision(noOpSnapshot, noOpDelta);
        }

        GrammarSnapshot snapshot = GrammarSnapshot.FromRePair(revision, in result);
        int commonRules = Math.Min(parentSnapshot.Rules.Length, snapshot.Rules.Length);
        int prefixRules = 0;
        while (prefixRules < commonRules && RulesMatch(parentSnapshot.Rules[prefixRules], snapshot.Rules[prefixRules])) prefixRules++;
        if (prefixRules != parentSnapshot.Rules.Length)
            return CreateRebuildInstallRevision(snapshot, revision, parentRevision, result.TotalSavings);

        GrammarRule[] added = new GrammarRule[snapshot.Rules.Length - prefixRules];
        Array.Copy(snapshot.Rules, prefixRules, added, 0, added.Length);
        GrammarSequenceEdit[] edits = CreateSequenceEdits(parentSnapshot.Compressed, snapshot.Compressed);
        GrammarDelta delta = new(parentRevision, revision, added, [], edits, result.TotalSavings, GrammarResetKinds.None);
        return new InstallRevision(snapshot, delta);
    }

    public static InstallRevision FromRePair(
        GrammarRevisionID revision,
        GrammarRevisionID parentRevision,
        in RePairResult result,
        in GrammarFoldProvenanceReceipt foldProvenance)
    {
        InstallRevision installRevision = FromRePair(revision, parentRevision, in result);
        return new InstallRevision(installRevision.Snapshot, installRevision.Delta, foldProvenance);
    }

    public static InstallRevision FromRePair(
        GrammarRevisionID revision,
        GrammarRevisionID parentRevision,
        in RePairResult result,
        GrammarSnapshot? parentSnapshot,
        in GrammarFoldProvenanceReceipt foldProvenance)
    {
        InstallRevision installRevision = FromRePair(revision, parentRevision, in result, parentSnapshot);
        return new InstallRevision(installRevision.Snapshot, installRevision.Delta, foldProvenance);
    }

    private static GrammarSequenceEdit[] CreateSequenceEdits(Symbol[] previous, Symbol[] next)
    {
        int prefix = 0;
        int common = Math.Min(previous.Length, next.Length);
        while (prefix < common && previous[prefix].Equals(next[prefix])) prefix++;
        if (prefix == previous.Length && prefix == next.Length) return [];

        int suffix = 0;
        while (suffix < previous.Length - prefix && suffix < next.Length - prefix
            && previous[previous.Length - 1 - suffix].Equals(next[next.Length - 1 - suffix])) suffix++;
        int removedLength = previous.Length - prefix - suffix;
        int insertedLength = next.Length - prefix - suffix;
        Symbol[] inserted = new Symbol[insertedLength];
        Array.Copy(next, prefix, inserted, 0, insertedLength);
        return [GrammarSequenceEdit.Replace(prefix, removedLength, inserted)];
    }

    private static InstallRevision CreateRebuildInstallRevision(
        GrammarSnapshot snapshot,
        GrammarRevisionID revision,
        GrammarRevisionID parentRevision,
        Mbits mdlDelta)
    {
        GrammarResetKinds reset = parentRevision == GrammarRevisionID.Zero ? GrammarResetKinds.Initial : GrammarResetKinds.Rebuild;
        // The installRevision owns these arrays. AddedRules and the sequence replacement are
        // immutable views of the same image; consumers only read them and never mutate them.
        GrammarDelta delta = new(
            parentRevision,
            revision,
            snapshot.Rules,
            [],
            [GrammarSequenceEdit.Replace(0, 0, snapshot.Compressed)],
            mdlDelta,
            reset);
        return new InstallRevision(snapshot, delta);
    }

    private static bool RulesMatch(in GrammarRule left, in GrammarRule right)
    {
        if (!left.Id.Equals(right.Id) || !left.Cost.Equals(right.Cost) || left.Kind != right.Kind)
            return false;
        if (!left.Pattern.AsSpan().SequenceEqual(right.Pattern)) return false;
        if (left.Segs is null || right.Segs is null) return left.Segs is null && right.Segs is null;
        return left.Segs.AsSpan().SequenceEqual(right.Segs);
    }
}

public sealed class GrammarContinuationQuota
{
    private readonly int _held;
    private int _used;
    private long _scannedBytes;
    private long _expandedEdges;
    private bool _completed;

    public GrammarContinuationQuota(int held)
        => _held = held > 0 ? held : throw new ArgumentOutOfRangeException(nameof(held));

    public int Held => _held;
    public int Used => _used;
    public long WorkUnits => checked(_scannedBytes + _expandedEdges);

    internal void UseScan(int bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        Use();
        _scannedBytes = checked(_scannedBytes + bytes);
    }

    internal void UseExpansion(int edges)
    {
        if (edges < 0) throw new ArgumentOutOfRangeException(nameof(edges));
        Use();
        _expandedEdges = checked(_expandedEdges + edges);
    }

    public GrammarContinuationQuotaCompletion Complete()
    {
        if (_completed) throw new InvalidOperationException("grammar continuation quota completed twice");
        _completed = true;
        return new GrammarContinuationQuotaCompletion(_held, _used, _held - _used, _scannedBytes, _expandedEdges);
    }

    private void Use()
    {
        if (_completed) throw new InvalidOperationException("grammar continuation quota is already complete");
        if (_used == _held) throw new InvalidOperationException("grammar continuation quota exhausted");
        _used++;
    }
}

public readonly struct GrammarContinuationQuotaCompletion
{
    public GrammarContinuationQuotaCompletion(int held, int used, int reclaimed, long scannedBytes, long expandedEdges)
    {
        if (held <= 0 || used < 0 || reclaimed < 0 || held != checked(used + reclaimed))
            throw new InvalidDataException("grammar continuation quota does not conserve its held units");
        if (scannedBytes < 0 || expandedEdges < 0)
            throw new InvalidDataException("grammar continuation quota carries negative work");
        Held = held;
        Used = used;
        Reclaimed = reclaimed;
        ScannedBytes = scannedBytes;
        ExpandedEdges = expandedEdges;
    }

    public int Held { get; }
    public int Used { get; }
    public int Reclaimed { get; }
    public long ScannedBytes { get; }
    public long ExpandedEdges { get; }
}

public readonly record struct GrammarContinuationDecision(
    int Continuation,
    long LearnedWeight,
    int MatchingRecords,
    long ScannedBytes,
    long ExpandedEdges,
    long[] CandidateScores,
    int[] CandidateCounts);

/// Compact, allocation-free-at-read accounting for trace sinks and later cutover receipts.
public readonly record struct InstallRevisionReceipt(
    GrammarRevisionID ParentRevision,
    GrammarRevisionID Revision,
    GrammarResetKinds Reset,
    int AddedRules,
    int RemovedRules,
    int SequenceEdits,
    int RuleCount,
    int CompressedSymbols,
    Mbits MDLDelta,
    int SnapshotRuleElements,
    int SnapshotCompressedElements,
    int OwnedArrayCount,
    bool DeltaSharesSnapshotArrays)
{
    public GrammarFoldProvenanceReceipt? FoldProvenance { get; init; }
}

/// The immutable read basis for policy continuations.  A installRevision owns one effective
/// grammar image; this index expands and covers it once, then answers every context query
/// against the same records and parsed sizes.  Revision and ContentDigest are part of the
/// identity so a query cannot accidentally cross a installRevision boundary.
public sealed class ReadoutCorpusIndex
{
    private readonly byte[] _corpus;
    private readonly byte[][] _records;
    private readonly int[] _parsedSizes;
    private readonly Engine.GrammarCover _cover;

    private ReadoutCorpusIndex(
        GrammarRevisionID revision,
        ulong effectiveDigest,
        byte[] corpus,
        byte[][] records,
        int[] parsedSizes,
        Engine.GrammarCover cover,
        GrammarReadoutIndexBuildReceipt buildReceipt)
    {
        Revision = revision;
        EffectiveDigest = effectiveDigest;
        _corpus = corpus;
        _records = records;
        _parsedSizes = parsedSizes;
        _cover = cover;
        BuildReceipt = buildReceipt;
    }

    public GrammarRevisionID Revision { get; }
    public ulong EffectiveDigest { get; }
    public GrammarReadoutIndexBuildReceipt BuildReceipt { get; }
    public int CorpusBytes => _corpus.Length;
    public int RecordCount => _records.Length;

    internal static ReadoutCorpusIndex Build(GrammarSnapshot effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        long totalStart = Stopwatch.GetTimestamp();
        long validationStart = Stopwatch.GetTimestamp();
        // GrammarInstallRevision validates the delta/snapshot boundary before entering this
        // builder.  The index still rejects an empty effective image here: there is no
        // useful readout basis and silently accepting one hides a broken installRevision.
        if (effective.Rules is null || effective.Compressed is null)
            throw new InvalidDataException("readout index cannot bind a null grammar image");
        double validationMilliseconds = Stopwatch.GetElapsedTime(validationStart).TotalMilliseconds;

        long expandStart = Stopwatch.GetTimestamp();
        byte[] corpus = Reconstruct.Expand(effective.Rules, effective.Compressed);
        double expandMilliseconds = Stopwatch.GetElapsedTime(expandStart).TotalMilliseconds;
        long coverStart = Stopwatch.GetTimestamp();
        Engine.GrammarCover cover = new(effective.Rules);
        double coverMilliseconds = Stopwatch.GetElapsedTime(coverStart).TotalMilliseconds;

        long recordsStart = Stopwatch.GetTimestamp();
        List<byte[]> records = new();
        List<int> parsedSizes = new();
        int start = 0;
        while (start < corpus.Length)
        {
            int end = Array.IndexOf(corpus, (byte)'\n', start);
            if (end < 0) end = corpus.Length;
            byte[] record = corpus.AsSpan(start, end - start).ToArray();
            records.Add(record);
            parsedSizes.Add(cover.ParsedSize(record));
            start = end + 1;
        }
        double recordsMilliseconds = Stopwatch.GetElapsedTime(recordsStart).TotalMilliseconds;
        GrammarReadoutIndexBuildReceipt receipt = new(
            effective.Revision,
            effective.ContentDigest,
            corpus.Length,
            records.Count,
            validationMilliseconds,
            expandMilliseconds,
            coverMilliseconds,
            recordsMilliseconds,
            Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds);
        return new ReadoutCorpusIndex(
            effective.Revision,
            effective.ContentDigest,
            corpus,
            records.ToArray(),
            parsedSizes.ToArray(),
            cover,
            receipt);
    }

    internal bool TryChooseContinuation(
        ReadOnlySpan<byte> context,
        IReadOnlyList<byte[]> continuations,
        GrammarContinuationQuota quota,
        int deliberationDepth,
        out GrammarContinuationDecision decision,
        out GrammarContinuationReadoutReceipt receipt)
    {
        if (context.IsEmpty) throw new ArgumentException("continuation context cannot be empty", nameof(context));
        if (continuations.Count < 2) throw new ArgumentException("continuation choice requires at least two candidates", nameof(continuations));
        if (deliberationDepth < 0) throw new ArgumentOutOfRangeException(nameof(deliberationDepth));
        ArgumentNullException.ThrowIfNull(quota);
        long queryStart = Stopwatch.GetTimestamp();
        quota.UseScan(_corpus.Length);
        long[] weights = new long[continuations.Count];
        int[] counts = new int[continuations.Count];
        long[,] transitions = new long[continuations.Count, continuations.Count];
        int previous = -1;
        int matches = 0;
        for (int recordIndex = 0; recordIndex < _records.Length; recordIndex++)
        {
            ReadOnlySpan<byte> record = _records[recordIndex];
            int candidate = MatchContinuation(record, context, continuations);
            if (candidate < 0) continue;
            long weight = checked(1L + record.Length - _parsedSizes[recordIndex]);
            weights[candidate] = checked(weights[candidate] + weight);
            counts[candidate] = checked(counts[candidate] + 1);
            if (previous >= 0)
                transitions[previous, candidate] = checked(transitions[previous, candidate] + weight);
            previous = candidate;
            matches++;
        }

        if (matches == 0)
        {
            decision = new GrammarContinuationDecision(-1, 0, 0, _corpus.Length, 0, weights, counts);
            receipt = CreateReceipt(queryStart, matches, 0, false);
            return false;
        }

        long[] scores = (long[])weights.Clone();
        int expandedEdges = 0;
        for (int depth = 0; depth < deliberationDepth; depth++)
        {
            int inspectedEdges = checked(scores.Length * scores.Length);
            quota.UseExpansion(inspectedEdges);
            expandedEdges = checked(expandedEdges + inspectedEdges);
            long[] next = new long[scores.Length];
            for (int candidate = 0; candidate < scores.Length; candidate++)
            {
                long bestFuture = 0;
                for (int following = 0; following < scores.Length; following++)
                {
                    long edge = transitions[candidate, following];
                    if (edge == 0) continue;
                    long future = checked(edge + scores[following]);
                    if (future > bestFuture) bestFuture = future;
                }
                next[candidate] = checked(weights[candidate] + bestFuture);
            }
            scores = next;
        }

        int selected = 0;
        for (int candidate = 1; candidate < scores.Length; candidate++)
            if (scores[candidate] > scores[selected]) selected = candidate;
        decision = new GrammarContinuationDecision(selected, scores[selected], matches, _corpus.Length, expandedEdges, scores, counts);
        receipt = CreateReceipt(queryStart, matches, expandedEdges, true);
        return true;
    }

    internal void RequireCompatible(GrammarRevisionID revision, ulong effectiveDigest)
    {
        if (Revision != revision || EffectiveDigest != effectiveDigest)
            throw new InvalidDataException($"readout corpus index identity mismatch: index={Revision.Value}/{EffectiveDigest:X16} request={revision.Value}/{effectiveDigest:X16}");
    }

    internal void RequireCompatible(GrammarSnapshot effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        RequireCompatible(effective.Revision, effective.RecomputeContentDigest());
    }

    private GrammarContinuationReadoutReceipt CreateReceipt(long queryStart, int matches, int expandedEdges, bool found)
        => new(
            Revision,
            EffectiveDigest,
            BuildReceipt,
            CorpusBytes,
            RecordCount,
            matches,
            expandedEdges,
            found,
            Stopwatch.GetElapsedTime(queryStart).TotalMilliseconds);

    private static int MatchContinuation(ReadOnlySpan<byte> record, ReadOnlySpan<byte> context, IReadOnlyList<byte[]> continuations)
    {
        if (!record.StartsWith(context)) return -1;
        ReadOnlySpan<byte> suffix = record[context.Length..];
        for (int candidate = 0; candidate < continuations.Count; candidate++)
        {
            ReadOnlySpan<byte> continuation = continuations[candidate];
            if (suffix.SequenceEqual(continuation)) return candidate;
            if (suffix.StartsWith(continuation)
                && suffix[continuation.Length..].StartsWith("\tRAW-EVIDENCE="u8)) return candidate;
        }
        return -1;
    }
}

public readonly record struct GrammarReadoutIndexBuildReceipt(
    GrammarRevisionID Revision,
    ulong EffectiveDigest,
    int CorpusBytes,
    int RecordCount,
    double ValidationMilliseconds,
    double ExpandMilliseconds,
    double CoverMilliseconds,
    double RecordsMilliseconds,
    double TotalMilliseconds);

public readonly record struct GrammarContinuationReadoutReceipt(
    GrammarRevisionID Revision,
    ulong EffectiveDigest,
    GrammarReadoutIndexBuildReceipt Build,
    int CorpusBytes,
    int RecordCount,
    int MatchingRecords,
    int ExpandedEdges,
    bool Found,
    double QueryMilliseconds)
{
    public bool IndexReused => Build.TotalMilliseconds >= 0;
}

internal static class ReadoutCorpusIndexes
{
    private static readonly ConditionalWeakTable<GrammarSnapshot, ReadoutCorpusIndex> Cache = new();

    internal static ReadoutCorpusIndex Get(GrammarSnapshot effective, Func<ReadoutCorpusIndex> build)
    {
        ArgumentNullException.ThrowIfNull(effective);
        ArgumentNullException.ThrowIfNull(build);
        if (Cache.TryGetValue(effective, out ReadoutCorpusIndex? existing))
        {
            existing.RequireCompatible(effective.Revision, effective.ContentDigest);
            return existing;
        }
        ReadoutCorpusIndex created = build();
        created.RequireCompatible(effective.Revision, effective.ContentDigest);
        Cache.Add(effective, created);
        return created;
    }
}
