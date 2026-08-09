namespace Cogito;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RepositoryLocus = Cogito.Tool.RepositoryLocus;
using RepositoryPath = Cogito.Tool.RepositoryPath;

public enum RepositoryCandidateSpecies : byte
{
    SearchTerm,
    ListPrefix,
    OpenPath,
    ReadLocus,
    VerifyPrediction,
    AnswerPath,
}

public enum RepositoryCandidateStates : byte
{
    Eligible,
    Committed,
}

public readonly record struct RepositoryFrontierRevision(ulong Value)
{
    public static readonly RepositoryFrontierRevision Zero = new(0);
    public bool IsValid => Value > 0;
}

public readonly record struct RepositoryCandidateDigest(ulong Value)
{
    public static readonly RepositoryCandidateDigest Zero = new(0);
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString("X16");
}

internal readonly record struct RepositoryFrontierCandidateKey(RepositoryCandidateDigest Digest, string Canonical)
{
    public static RepositoryFrontierCandidateKey Create(RepositoryCandidate candidate)
    {
        if (candidate is null || !candidate.Digest.IsValid || string.IsNullOrEmpty(candidate.Canonical))
            throw new InvalidDataException("repository frontier candidate key is malformed");
        return new(candidate.Digest, candidate.Canonical);
    }
}

public readonly record struct RepositorySearchTerm(string Value)
{
    public string Canonical => Value.Trim().ToLowerInvariant();
}

public readonly record struct RepositoryListPrefix(string Value)
{
    public string Canonical => Value.Trim().TrimEnd('/');
}

public readonly record struct RepositoryOpenPath(RepositoryPath Path);
public readonly record struct RepositoryReadLocus(RepositoryLocus Locus);
public readonly record struct RepositoryOccurrenceCheckPrediction(RepositoryPrediction Prediction);
public readonly record struct RepositoryAnswerPath(RepositoryPath Path);

/// One typed native repository action. The concrete subclasses carry the domain
/// value; there is no stringly candidate payload for the frontier to reinterpret.
public abstract record RepositoryCandidate(RepositoryCandidateSpecies Species)
{
    public abstract string Canonical { get; }
    public RepositoryCandidateDigest Digest => ComputeDigest(Canonical);
    public abstract Tool.ToolVerbs Verb { get; }
    public abstract string Argument { get; }

    /// The granularity at which candidates COMPETE. Species is the right class for most of them, but
    /// not for a verify prediction: the downstream stages discriminate by PREDICTION species — a composition
    /// admits SharedIdentifier occurrences and nothing else — so treating all verify predictions as one class
    /// makes the deriving species invisible to any fairness rule and leaves its selection to how its
    /// canonical happens to sort. The class is what the chain actually distinguishes.
    public virtual string SelectionClass => Species.ToString();

    public static RepositoryCandidate CreateSearchTerm(RepositorySearchTerm term) => new SearchTermCandidate(term);
    public static RepositoryCandidate CreateListPrefix(RepositoryListPrefix prefix) => new ListPrefixCandidate(prefix);
    public static RepositoryCandidate CreateOpenPath(RepositoryOpenPath path) => new OpenPathCandidate(path);
    public static RepositoryCandidate CreateReadLocus(RepositoryReadLocus locus) => new ReadLocusCandidate(locus);
    public static RepositoryCandidate CreateVerifyPrediction(RepositoryOccurrenceCheckPrediction prediction) => new VerifyPredictionCandidate(prediction);
    public static RepositoryCandidate CreateAnswerPath(RepositoryAnswerPath path) => new AnswerPathCandidate(path);

    internal static bool TryParseCanonical(string canonical, out RepositoryCandidate candidate)
    {
        candidate = default!;
        if (string.IsNullOrWhiteSpace(canonical)) return false;
        int tab = canonical.IndexOf('\t');
        if (tab <= 0 || tab == canonical.Length - 1) return false;
        string value = canonical[(tab + 1)..];
        try
        {
            candidate = canonical[..tab] switch
            {
                "search-term" => CreateSearchTerm(new RepositorySearchTerm(value)),
                "list-prefix" => CreateListPrefix(new RepositoryListPrefix(value == "." ? "" : value)),
                "open-path" => CreateOpenPath(new RepositoryOpenPath(new RepositoryPath(value))),
                "read-locus" => TryParseLocus(value, out RepositoryLocus locus)
                    ? CreateReadLocus(new RepositoryReadLocus(locus)) : null!,
                // Frozen canonical prefix; identifier-side name is OccurrenceCheckPrediction.
                "verify-claim" when RepositoryPrediction.TryParse(value, out RepositoryPrediction prediction)
                    => CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(prediction)),
                "answer-path" => CreateAnswerPath(new RepositoryAnswerPath(new RepositoryPath(value))),
                _ => null!,
            };
            return candidate is not null && candidate.Canonical == canonical;
        }
        catch (InvalidDataException) { candidate = default!; return false; }

        static bool TryParseLocus(string value, out RepositoryLocus locus)
        {
            locus = default;
            int colon = value.LastIndexOf(':');
            return colon > 0 && int.TryParse(value[(colon + 1)..], out int line) && line > 0
                && (locus = new RepositoryLocus(value[..colon], line)).Path.Length > 0;
        }
    }

    internal static RepositoryCandidateDigest ComputeDigest(string canonical)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical), digest);
        return new RepositoryCandidateDigest(BinaryPrimitives.ReadUInt64BigEndian(digest));
    }

    public sealed record SearchTermCandidate(RepositorySearchTerm Term)
        : RepositoryCandidate(RepositoryCandidateSpecies.SearchTerm)
    {
        public override string Canonical => $"search-term\t{Term.Canonical}";
        public override Tool.ToolVerbs Verb => Tool.ToolVerbs.Grep;
        public override string Argument => Term.Canonical;
    }

    public sealed record ListPrefixCandidate(RepositoryListPrefix Prefix)
        : RepositoryCandidate(RepositoryCandidateSpecies.ListPrefix)
    {
        // The ROOT prefix is the empty string internally and "." on every wire — canonical and
        // argument must agree on that spelling, or the root candidate renders a canonical ending in
        // its tab, fails to re-parse, and takes the whole terminal snapshot down with it.
        public override string Canonical => $"list-prefix\t{Argument}";
        public override Tool.ToolVerbs Verb => Tool.ToolVerbs.Ls;
        public override string Argument => Prefix.Canonical.Length == 0 ? "." : Prefix.Canonical;
    }

    public sealed record OpenPathCandidate(RepositoryOpenPath Path)
        : RepositoryCandidate(RepositoryCandidateSpecies.OpenPath)
    {
        public override string Canonical => $"open-path\t{Path.Path.Value}";
        public override Tool.ToolVerbs Verb => Tool.ToolVerbs.Open;
        public override string Argument => Path.Path.Value;
    }

    public sealed record ReadLocusCandidate(RepositoryReadLocus Locus)
        : RepositoryCandidate(RepositoryCandidateSpecies.ReadLocus)
    {
        public override string Canonical => $"read-locus\t{Locus.Locus.Path.Value}:{Locus.Locus.Line}";
        public override Tool.ToolVerbs Verb => Tool.ToolVerbs.Read;
        public override string Argument => $"{Locus.Locus.Path.Value}:{Locus.Locus.Line}";
    }

    public sealed record VerifyPredictionCandidate(RepositoryOccurrenceCheckPrediction Prediction)
        : RepositoryCandidate(RepositoryCandidateSpecies.VerifyPrediction)
    {
        // Frozen canonical prefix; identifier-side name is VerifyPredictionCandidate.
        public override string Canonical => $"verify-claim\t{Prediction.Prediction.Canonical}";
        public override string SelectionClass => $"{Species}/{Prediction.Prediction.Species}";
        public override Tool.ToolVerbs Verb => Tool.ToolVerbs.Verify;
        public override string Argument => Prediction.Prediction.Canonical;
    }

    public sealed record AnswerPathCandidate(RepositoryAnswerPath Path)
        : RepositoryCandidate(RepositoryCandidateSpecies.AnswerPath)
    {
        public override string Canonical => $"answer-path\t{Path.Path.Value}";
        public override Tool.ToolVerbs Verb => Tool.ToolVerbs.Answer;
        public override string Argument => Path.Path.Value;
    }
}

public readonly record struct RepositoryCandidateProposal(
    RepositoryFrontierRevision Revision,
    RepositoryCandidateDigest CandidateDigest,
    RepositoryCandidate Candidate)
{
    public bool IsValid => Revision.IsValid && CandidateDigest.IsValid && Candidate.Digest == CandidateDigest;
}

/// Emits a native candidate directly into the Cortex action surface. The raw line
/// is only the journal/interoperability rendering; no parse round-trip is used to
/// recover the typed candidate or its tool verb.
public static class RepositoryCandidateActionAdapter
{
    public static CortexAction Create(in RepositoryCandidateProposal proposal, CortexTool tool,
        List<CortexActionArgument> arguments)
    {
        if (!proposal.IsValid) throw new InvalidDataException("repository candidate proposal is malformed");
        arguments.Clear();
        arguments.Add(new CortexActionArgument(RepositoryNativeToolAuthority.ArgumentSlot, proposal.Candidate.Argument, Blur.SlotSources.GrammarPrior));
        return new CortexAction(tool, Tool.ToolCall.Create(proposal.Candidate.Verb, proposal.Candidate.Argument).Raw);
    }
}

public readonly record struct RepositoryCandidateSelectionReceipt(
    int Step,
    RepositoryFrontierRevision Revision,
    RepositoryCandidateDigest CandidateDigest,
    bool Selected,
    bool Exhausted,
    string Reason)
{
    public static RepositoryCandidateSelectionReceipt Exhaust(int step, RepositoryFrontierRevision revision, string reason)
        => new(step, revision, RepositoryCandidateDigest.Zero, false, true, reason);
}

public readonly record struct RepositoryCandidateTransition(
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    RepositoryCandidateStates State,
    int Attempts,
    TapeEventID SourceEventID,
    TapeEventID PredecessorEventID,
    string CallSHA256,
    string AccessSHA256,
    RepositoryOccurrenceCheckOutcomes? VerifierOutcome,
    RepositoryPatternCandidateOrigin? PatternOrigin);

internal readonly record struct RepositoryFrontierCheckpointDelta(
    RepositoryFrontierRevision Revision,
    RepositoryCandidateTransition[] Edits,
    string[] ObservedPathAdds,
    RepositoryFrontierMutation[] History);

internal readonly record struct RepositoryFrontierMutation(
    RepositoryFrontierRevision Revision,
    bool HasBefore,
    RepositoryCandidateTransition Before,
    bool HasAfter,
    RepositoryCandidateTransition After,
    string[] ObservedPathAdds);

/// Immutable runtime frontier authority. The observed paths and authority root
/// are captured from the live frontier together, so a sealed consumer cannot
/// substitute a path list or recompute state from an older revision.
public sealed class RepositoryCandidateFrontierSnapshot
{
    public RepositoryCandidateFrontierSnapshot(
        RepositoryFrontierRevision revision,
        IReadOnlyList<RepositoryCandidate> candidates,
        IReadOnlyList<RepositoryCandidateTransition> transitions,
        IReadOnlyCollection<string> observedPaths,
        string authoritySHA256)
    {
        Revision = revision;
        Candidates = Array.AsReadOnly((candidates ?? throw new ArgumentNullException(nameof(candidates))).ToArray());
        Transitions = Array.AsReadOnly((transitions ?? throw new ArgumentNullException(nameof(transitions)))
            .Select(CloneTransition).ToArray());
        ObservedPaths = Array.AsReadOnly((observedPaths ?? throw new ArgumentNullException(nameof(observedPaths)))
            .Order(StringComparer.Ordinal).ToArray());
        AuthoritySHA256 = authoritySHA256;
    }

    public RepositoryFrontierRevision Revision { get; }
    public IReadOnlyList<RepositoryCandidate> Candidates { get; }
    public IReadOnlyList<RepositoryCandidateTransition> Transitions { get; }
    public IReadOnlyList<string> ObservedPaths { get; }
    public string AuthoritySHA256 { get; }
    public string RuntimeAuthoritySHA256 => AuthoritySHA256;

    public void Validate()
    {
        if (!Revision.IsValid || !IsSHA(AuthoritySHA256)
            || ObservedPaths.Any(string.IsNullOrWhiteSpace)
            || ObservedPaths.Count != ObservedPaths.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("repository frontier snapshot authority is malformed");

        HashSet<RepositoryFrontierCandidateKey> candidateKeys = new();
        foreach (RepositoryCandidate candidate in Candidates)
        {
            if (candidate is null || !candidate.Digest.IsValid || !candidateKeys.Add(RepositoryFrontierCandidateKey.Create(candidate)))
                throw new InvalidDataException("repository frontier snapshot candidate is malformed");
        }

        HashSet<RepositoryFrontierCandidateKey> transitionKeys = new();
        foreach (RepositoryCandidateTransition transition in Transitions)
        {
            // Name WHICH clause failed and on WHAT: a bare "malformed" on a snapshot of hundreds of
            // transitions sends the reader back through the whole frontier to find the one row.
            if (!RepositoryCandidate.TryParseCanonical(transition.CandidateCanonical, out RepositoryCandidate candidate))
                throw new InvalidDataException($"repository frontier snapshot transition does not re-parse: '{transition.CandidateCanonical}'");
            if (candidate.Digest != transition.CandidateDigest)
                throw new InvalidDataException($"repository frontier snapshot transition digest diverges from its canonical: '{transition.CandidateCanonical}'");
            if (!Enum.IsDefined(transition.State) || transition.Attempts < 0)
                throw new InvalidDataException($"repository frontier snapshot transition state/attempts are malformed: '{transition.CandidateCanonical}' state={transition.State} attempts={transition.Attempts}");
            if (!transitionKeys.Add(RepositoryFrontierCandidateKey.Create(candidate)))
                throw new InvalidDataException($"repository frontier snapshot transition is duplicated: '{transition.CandidateCanonical}'");
            if (transition.PatternOrigin is { } origin) origin.Validate();
        }

        if (!candidateKeys.SetEquals(transitionKeys)
            || AuthoritySHA256 != RepositoryCandidateFrontier.ComputeAuthoritySHA256(Transitions, ObservedPaths))
            throw new InvalidDataException("repository frontier snapshot authority diverges");
    }

    private static RepositoryCandidateTransition CloneTransition(RepositoryCandidateTransition transition)
        => transition with { PatternOrigin = CloneOrigin(transition.PatternOrigin) };

    private static RepositoryPatternCandidateOrigin? CloneOrigin(RepositoryPatternCandidateOrigin? origin)
    {
        if (origin is not { } value) return null;
        RepositoryPatternOccurrenceSet occurrences = RepositoryPatternOccurrenceSet.Create(value.OccurrenceSet.Occurrences.ToArray());
        RepositoryComposedCandidateReceipt receipt = value.Receipt with
        {
            OccurrenceReceiptEventIDs = value.Receipt.OccurrenceReceiptEventIDs.ToArray(),
        };
        return new RepositoryPatternCandidateOrigin(value.RuleID, occurrences, receipt);
    }

    private static bool IsSHA(string value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// Deterministic ordered Merkle map for mutable frontier authority.  The treap
/// priority is composed from the key, so insertion order cannot alter the root;
/// replacing one candidate/path touches only its search path (O(log n)).
internal sealed class RepositoryOrderedMerkleMap
{
    private sealed class Node
    {
        public Node(string key, string value, ulong priority) { Key = key; Value = value; Priority = priority; }
        public string Key; public string Value; public ulong Priority; public Node? Left; public Node? Right; public string Hash = "";
    }

    private static readonly string EmptyHash = Digest("empty");
    private Node? _root;
    public string RootHash => _root?.Hash ?? EmptyHash;

    public void Set(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) throw new InvalidDataException("ordered Merkle key is empty");
        _root = Insert(_root, new Node(key, value, Priority(key)));
    }

    public void Remove(string key) { if (!string.IsNullOrEmpty(key)) _root = Delete(_root, key); }
    public void Clear() => _root = null;

    internal string PreviewRootAfter(IReadOnlyList<(bool Set, string Key, string Value)> operations)
    {
        Node? root = _root;
        foreach ((bool set, string key, string value) in operations)
        {
            if (set) root = PreviewSet(root, key, value);
            else root = PreviewRemove(root, key);
        }
        return root?.Hash ?? EmptyHash;
    }

    private static Node PreviewSet(Node? root, string key, string value)
    {
        if (root is null) return CreateNode(key, value, Priority(key), null, null);
        int compare = string.CompareOrdinal(key, root.Key);
        if (compare == 0) return CreateNode(root.Key, value, root.Priority, root.Left, root.Right);
        if (compare < 0)
        {
            Node left = PreviewSet(root.Left, key, value);
            Node next = CreateNode(root.Key, root.Value, root.Priority, left, root.Right);
            return Precedes(left, next) ? PreviewRotateRight(next) : next;
        }
        Node right = PreviewSet(root.Right, key, value);
        Node nextRight = CreateNode(root.Key, root.Value, root.Priority, root.Left, right);
        return Precedes(right, nextRight) ? PreviewRotateLeft(nextRight) : nextRight;
    }

    private static Node? PreviewRemove(Node? root, string key)
    {
        if (root is null) return null;
        int compare = string.CompareOrdinal(key, root.Key);
        if (compare < 0) return CreateNode(root.Key, root.Value, root.Priority, PreviewRemove(root.Left, key), root.Right);
        if (compare > 0) return CreateNode(root.Key, root.Value, root.Priority, root.Left, PreviewRemove(root.Right, key));
        return PreviewMerge(root.Left, root.Right);
    }

    private static Node? PreviewMerge(Node? left, Node? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        if (Precedes(left, right))
            return CreateNode(left.Key, left.Value, left.Priority, left.Left, PreviewMerge(left.Right, right));
        return CreateNode(right.Key, right.Value, right.Priority, PreviewMerge(left, right.Left), right.Right);
    }

    private static Node PreviewRotateRight(Node root)
    {
        Node next = root.Left!;
        return CreateNode(next.Key, next.Value, next.Priority, next.Left,
            CreateNode(root.Key, root.Value, root.Priority, next.Right, root.Right));
    }

    private static Node PreviewRotateLeft(Node root)
    {
        Node next = root.Right!;
        return CreateNode(next.Key, next.Value, next.Priority,
            CreateNode(root.Key, root.Value, root.Priority, root.Left, next.Left), next.Right);
    }

    private static Node CreateNode(string key, string value, ulong priority, Node? left, Node? right)
    {
        Node node = new(key, value, priority) { Left = left, Right = right };
        Refresh(node);
        return node;
    }

    private static Node Insert(Node? root, Node incoming)
    {
        if (root is null) { Refresh(incoming); return incoming; }
        int compare = string.CompareOrdinal(incoming.Key, root.Key);
        if (compare == 0) { root.Value = incoming.Value; Refresh(root); return root; }
        if (compare < 0)
        {
            root.Left = Insert(root.Left, incoming);
            if (Precedes(root.Left!, root)) root = RotateRight(root);
        }
        else
        {
            root.Right = Insert(root.Right, incoming);
            if (Precedes(root.Right!, root)) root = RotateLeft(root);
        }
        Refresh(root); return root;
    }

    private static Node? Delete(Node? root, string key)
    {
        if (root is null) return null;
        int compare = string.CompareOrdinal(key, root.Key);
        if (compare < 0) root.Left = Delete(root.Left, key);
        else if (compare > 0) root.Right = Delete(root.Right, key);
        else return Merge(root.Left, root.Right);
        Refresh(root); return root;
    }

    private static Node? Merge(Node? left, Node? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        if (Precedes(left, right)) { left.Right = Merge(left.Right, right); Refresh(left); return left; }
        right.Left = Merge(left, right.Left); Refresh(right); return right;
    }

    private static Node RotateRight(Node root) { Node next = root.Left!; root.Left = next.Right; next.Right = root; Refresh(root); Refresh(next); return next; }
    private static Node RotateLeft(Node root) { Node next = root.Right!; root.Right = next.Left; next.Left = root; Refresh(root); Refresh(next); return next; }

    private static ulong Priority(string key)
        => BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("priority|" + key)));

    private static bool Precedes(Node left, Node right)
        => left.Priority > right.Priority || left.Priority == right.Priority && string.CompareOrdinal(left.Key, right.Key) < 0;

    private static void Refresh(Node node)
        => node.Hash = Digest(EncodeFields("node", node.Left?.Hash ?? EmptyHash, node.Key, node.Value, node.Right?.Hash ?? EmptyHash));

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string EncodeFields(params string[] fields)
    {
        StringBuilder result = new();
        foreach (string field in fields)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(field);
            result.Append(bytes.Length).Append(':').Append(Convert.ToBase64String(bytes));
        }
        return result.ToString();
    }
}

public sealed class RepositoryCandidateFrontier
{
    private sealed class Entry
    {
        public required RepositoryCandidate Candidate { get; init; }
        public RepositoryCandidateStates State { get; set; }
        public int Attempts { get; set; }
        public TapeEventID SourceEventID { get; set; }
        public TapeEventID PredecessorEventID { get; set; }
        public string CallSHA256 { get; set; } = "";
        public string AccessSHA256 { get; set; } = "";
        public RepositoryOccurrenceCheckOutcomes? VerifierOutcome { get; set; }
        public RepositoryPatternCandidateOrigin? PatternOrigin { get; set; }
        public ulong MutationRevision { get; set; }
    }

    private readonly Dictionary<RepositoryFrontierCandidateKey, Entry> _entries = new();
    private readonly HashSet<string> _observedPaths = new(StringComparer.Ordinal);
    private RepositoryFrontierRevision _revision = new(1);
    private ulong _mutationRevision;
    private ulong _checkpointMutationRevision;
    private readonly List<string> _observedPathLog = new();
    private readonly List<RepositoryFrontierMutation> _history = new();
    private readonly List<RepositoryCandidateTransition> _transitionMutationLog = new();
    private readonly RepositoryOrderedMerkleMap _authorityEntries = new();
    private readonly RepositoryOrderedMerkleMap _authorityPaths = new();
    private int _checkpointObservedPathCursor;
    private int _checkpointHistoryCursor;
    private int _checkpointTransitionCursor;
    private string? _authorityCache;
    private RepositoryFrontierRevision _authorityCacheRevision;

    public RepositoryFrontierRevision Revision => _revision;
    public int Count => _entries.Count;
    public int EligibleCount => _entries.Values.Count(static entry => entry.State == RepositoryCandidateStates.Eligible);

    /// What the frontier is actually HOLDING, per species, eligible/attempted. A stage of the evidence
    /// chain that never fires has two possible causes and they need different cures: the candidate its
    /// prediction species requires was never minted, or it was minted and never selected. An aggregate
    /// count cannot tell those apart, so the census reports the shape rather than the total.
    internal string RenderSpeciesCensus()
        => string.Join(" · ", Enum.GetValues<RepositoryCandidateSpecies>()
            .Select(species => (Species: species,
                Eligible: _entries.Values.Count(entry => entry.Candidate.Species == species && entry.State == RepositoryCandidateStates.Eligible),
                Attempts: _entries.Values.Where(entry => entry.Candidate.Species == species).Sum(static entry => entry.Attempts)))
            .Where(static row => row.Eligible > 0 || row.Attempts > 0)
            .Select(static row => $"{row.Species} {row.Eligible}e/{row.Attempts}a"));
    public IReadOnlyCollection<RepositoryCandidate> Candidates
        => _entries.Values.OrderBy(static entry => entry.Candidate.Digest.Value).ThenBy(static entry => entry.Candidate.Canonical, StringComparer.Ordinal).Select(static entry => entry.Candidate).ToArray();
    public IReadOnlyCollection<RepositoryCandidateTransition> Transitions
        => _entries.Values.OrderBy(static entry => entry.Candidate.Digest.Value).ThenBy(static entry => entry.Candidate.Canonical, StringComparer.Ordinal)
            .Select(static entry => new RepositoryCandidateTransition(entry.Candidate.Digest, entry.Candidate.Canonical, entry.State, entry.Attempts,
                entry.SourceEventID, entry.PredecessorEventID, entry.CallSHA256, entry.AccessSHA256, entry.VerifierOutcome, entry.PatternOrigin)).ToArray();
    public IReadOnlyCollection<string> ObservedPaths => _observedPaths.Order(StringComparer.Ordinal).ToArray();
    public string AuthoritySHA256
    {
        get
        {
            if (_authorityCacheRevision == _revision && _authorityCache is not null) return _authorityCache;
            _authorityCache = CombineAuthorityRoots(_authorityEntries.RootHash, _authorityPaths.RootHash);
            _authorityCacheRevision = _revision;
            return _authorityCache;
        }
    }

    public RepositoryCandidateFrontierSnapshot CaptureSnapshot()
    {
        RepositoryCandidateFrontierSnapshot snapshot = new(
            Revision, Candidates.ToArray(), Transitions.ToArray(), ObservedPaths.ToArray(), AuthoritySHA256);
        snapshot.Validate();
        return snapshot;
    }

    internal RepositoryFrontierCheckpointDelta CaptureCheckpointDelta()
        => new(_revision,
            _transitionMutationLog.GetRange(_checkpointTransitionCursor, _transitionMutationLog.Count - _checkpointTransitionCursor).ToArray(),
            _observedPathLog.GetRange(_checkpointObservedPathCursor, _observedPathLog.Count - _checkpointObservedPathCursor).ToArray(),
            _history.GetRange(_checkpointHistoryCursor, _history.Count - _checkpointHistoryCursor).ToArray());

    internal void ValidateCheckpointDelta(in RepositoryFrontierCheckpointDelta delta)
        => ApplyCheckpointDeltaCore(in delta, commit: false);

    internal readonly struct PreparedCheckpointDelta
    {
        internal PreparedCheckpointDelta(RepositoryFrontierCheckpointDelta delta, HashSet<RepositoryFrontierCandidateKey> finalCandidates)
        {
            Delta = delta;
            FinalCandidates = finalCandidates;
        }

        internal RepositoryFrontierCheckpointDelta Delta { get; }
        private HashSet<RepositoryFrontierCandidateKey> FinalCandidates { get; }
        internal bool ContainsCandidate(RepositoryCandidateDigest digest, string canonical)
            => FinalCandidates.Contains(new(digest, canonical));
    }

    internal PreparedCheckpointDelta PrepareCheckpointDelta(in RepositoryFrontierCheckpointDelta delta)
    {
        ApplyCheckpointDeltaCore(in delta, commit: false);
        HashSet<RepositoryFrontierCandidateKey> finalCandidates = _entries.Keys.ToHashSet();
        foreach (RepositoryFrontierMutation mutation in delta.History)
        {
            if (mutation.HasBefore) finalCandidates.Remove(RepositoryFrontierCandidateKey.Create(TryParseCandidate(mutation.Before.CandidateCanonical)));
            if (mutation.HasAfter) finalCandidates.Add(RepositoryFrontierCandidateKey.Create(TryParseCandidate(mutation.After.CandidateCanonical)));
        }
        return new(delta, finalCandidates);
    }

    internal (string Authority, int Count) ComputeAuthorityAfterDelta(in RepositoryFrontierCheckpointDelta delta)
    {
        ValidateCheckpointDelta(in delta);
        List<(bool Set, string Key, string Value)> entryOperations = new();
        List<(bool Set, string Key, string Value)> pathOperations = new();
        foreach (string path in delta.ObservedPathAdds) pathOperations.Add((true, path, path));
        foreach (RepositoryFrontierMutation mutation in delta.History)
        {
            if (mutation.HasBefore) entryOperations.Add((false, AuthorityKey(mutation.Before), ""));
            if (mutation.HasAfter) entryOperations.Add((true, AuthorityKey(mutation.After), AuthorityValue(mutation.After)));
            foreach (string path in mutation.ObservedPathAdds) pathOperations.Add((true, path, path));
        }
        string root = CombineAuthorityRoots(
            _authorityEntries.PreviewRootAfter(entryOperations),
            _authorityPaths.PreviewRootAfter(pathOperations));
        int count = _entries.Count;
        foreach (RepositoryFrontierMutation mutation in delta.History)
            if (!mutation.HasBefore && mutation.HasAfter) count++;
            else if (mutation.HasBefore && !mutation.HasAfter) count--;
        return (root, count);
    }

    internal void ApplyCheckpointDelta(in RepositoryFrontierCheckpointDelta delta)
        => CommitPreparedCheckpointDelta(PrepareCheckpointDelta(in delta));

    internal void CommitPreparedCheckpointDelta(in PreparedCheckpointDelta prepared)
        => CommitCheckpointDeltaCore(prepared.Delta);

    private void ApplyCheckpointDeltaCore(in RepositoryFrontierCheckpointDelta delta, bool commit)
    {
        if (!delta.Revision.IsValid || delta.Edits is null || delta.ObservedPathAdds is null || delta.History is null
            || delta.Revision.Value < _revision.Value)
            throw new InvalidDataException("repository frontier checkpoint delta is malformed");
        if (delta.Revision.Value > _revision.Value && delta.History.Length == 0)
            throw new InvalidDataException("repository frontier checkpoint revision advances without history");
        if (delta.Revision.Value == _revision.Value && (delta.Edits.Length != 0 || delta.ObservedPathAdds.Length != 0 || delta.History.Length != 0))
            throw new InvalidDataException("repository frontier checkpoint delta repeats its revision");
        HashSet<string> paths = new(_observedPaths, StringComparer.Ordinal);
        foreach (string path in delta.ObservedPathAdds)
            if (string.IsNullOrWhiteSpace(path) || !paths.Add(path))
                throw new InvalidDataException("repository frontier checkpoint observed-path delta is duplicated or blank");

        Dictionary<RepositoryFrontierCandidateKey, RepositoryCandidateTransition?> staged = new();
        List<RepositoryCandidateTransition> expectedEdits = new();
        ulong historyRevision = _revision.Value;
        foreach (RepositoryFrontierMutation mutation in delta.History)
        {
            if (!mutation.Revision.IsValid || mutation.Revision.Value != ++historyRevision
                || mutation.ObservedPathAdds is null || (mutation.HasAfter == false && mutation.HasBefore))
                throw new InvalidDataException("repository frontier checkpoint history is malformed");
            if (mutation.ObservedPathAdds.Any(path => !paths.Contains(path)))
                throw new InvalidDataException("repository frontier checkpoint history names an unknown observed path");
            if (!mutation.HasBefore && !mutation.HasAfter)
            {
                if (mutation.ObservedPathAdds.Length == 0)
                    throw new InvalidDataException("repository frontier checkpoint mutation is empty");
                continue;
            }
            RepositoryFrontierCandidateKey key = mutation.HasBefore
                ? ValidateTransition(mutation.Before)
                : ValidateTransition(mutation.After);
            if (mutation.HasBefore)
            {
                bool found;
                RepositoryCandidateTransition actualBefore;
                if (staged.TryGetValue(key, out RepositoryCandidateTransition? stagedBefore))
                {
                    found = stagedBefore is { };
                    actualBefore = stagedBefore.GetValueOrDefault();
                }
                else if (_entries.TryGetValue(key, out Entry? currentEntry))
                {
                    found = true;
                    actualBefore = ToTransition(currentEntry);
                }
                else
                {
                    found = false;
                    actualBefore = default;
                }
                if (!found
                    || !TransitionEquals(actualBefore, mutation.Before))
                    throw new InvalidDataException("repository frontier checkpoint mutation predecessor diverged");
            }
            else if (staged.TryGetValue(key, out RepositoryCandidateTransition? stagedCurrent)
                ? stagedCurrent is { }
                : _entries.ContainsKey(key))
                throw new InvalidDataException("repository frontier checkpoint mutation unexpectedly creates an existing candidate");
            if (mutation.HasAfter)
            {
                RepositoryCandidateTransition after = mutation.After;
                if (RepositoryFrontierCandidateKey.Create(TryParseCandidate(after.CandidateCanonical)) != key)
                    throw new InvalidDataException("repository frontier checkpoint mutation candidate identity diverged");
                staged[key] = after;
                expectedEdits.Add(after);
            }
            else staged[key] = null;
        }
        if (delta.History.Length != 0 && historyRevision != delta.Revision.Value)
            throw new InvalidDataException("repository frontier checkpoint history does not reach its revision");
        RepositoryCandidateTransition[] deltaEdits = delta.Edits;
        if (expectedEdits.Count != deltaEdits.Length
            || expectedEdits.Where((transition, index) => !TransitionEquals(transition, deltaEdits[index])).Any())
            throw new InvalidDataException("repository frontier checkpoint edits diverge from mutation history");

        if (!commit) return;

        CommitCheckpointDeltaCore(in delta);
    }

    private void CommitCheckpointDeltaCore(in RepositoryFrontierCheckpointDelta delta)
    {
        // Every validation ran against staged state. Only the delta keys are
        // touched after validation; unchanged authority stays in place.
        foreach (string path in delta.ObservedPathAdds)
        {
            _observedPaths.Add(path);
            _observedPathLog.Add(path);
        }
        foreach (RepositoryFrontierMutation mutation in delta.History)
        {
            RepositoryFrontierCandidateKey key = mutation.HasBefore
                ? RepositoryFrontierCandidateKey.Create(TryParseCandidate(mutation.Before.CandidateCanonical))
                : RepositoryFrontierCandidateKey.Create(TryParseCandidate(mutation.After.CandidateCanonical));
            if (mutation.HasBefore && !mutation.HasAfter)
            {
                _entries.Remove(key);
                continue;
            }
            RepositoryCandidateTransition transition = mutation.After;
            if (!_entries.TryGetValue(key, out Entry? entry))
            {
                entry = new() { Candidate = TryParseCandidate(transition.CandidateCanonical) };
                _entries.Add(key, entry);
            }
            entry.State = transition.State;
            entry.Attempts = transition.Attempts;
            entry.SourceEventID = transition.SourceEventID;
            entry.PredecessorEventID = transition.PredecessorEventID;
            entry.CallSHA256 = transition.CallSHA256;
            entry.AccessSHA256 = transition.AccessSHA256;
            entry.VerifierOutcome = transition.VerifierOutcome;
            entry.PatternOrigin = transition.PatternOrigin;
            entry.MutationRevision = ++_mutationRevision;
        }
        _history.AddRange(delta.History);
        _transitionMutationLog.AddRange(delta.Edits);
        foreach (RepositoryFrontierMutation mutation in delta.History)
        {
            if (mutation.HasBefore) _authorityEntries.Remove(AuthorityKey(mutation.Before));
            if (mutation.HasAfter) _authorityEntries.Set(AuthorityKey(mutation.After), AuthorityValue(mutation.After));
            foreach (string path in mutation.ObservedPathAdds) _authorityPaths.Set(path, path);
        }
        _revision = delta.Revision;
        _authorityCache = null;
        _checkpointMutationRevision = _mutationRevision;
        _checkpointObservedPathCursor = _observedPathLog.Count;
        _checkpointHistoryCursor = _history.Count;
        _checkpointTransitionCursor = _transitionMutationLog.Count;
    }

    private static RepositoryFrontierCandidateKey ValidateTransition(in RepositoryCandidateTransition transition)
    {
        if (!RepositoryCandidate.TryParseCanonical(transition.CandidateCanonical, out RepositoryCandidate candidate)
            || candidate.Digest != transition.CandidateDigest
            || !Enum.IsDefined(transition.State) || transition.Attempts < 0)
            throw new InvalidDataException("repository frontier checkpoint candidate is not reconstructible");
        return RepositoryFrontierCandidateKey.Create(candidate);
    }

    private static RepositoryCandidate TryParseCandidate(string canonical)
        => RepositoryCandidate.TryParseCanonical(canonical, out RepositoryCandidate candidate)
            ? candidate : throw new InvalidDataException("repository frontier candidate is not reconstructible");

    private static bool TransitionEquals(in RepositoryCandidateTransition left, in RepositoryCandidateTransition right)
        => left.CandidateDigest == right.CandidateDigest
            && left.CandidateCanonical == right.CandidateCanonical
            && left.State == right.State
            && left.Attempts == right.Attempts
            && left.SourceEventID == right.SourceEventID
            && left.PredecessorEventID == right.PredecessorEventID
            && left.CallSHA256 == right.CallSHA256
            && left.AccessSHA256 == right.AccessSHA256
            && left.VerifierOutcome == right.VerifierOutcome
            && OriginEquals(left.PatternOrigin, right.PatternOrigin);

    private static bool OriginEquals(RepositoryPatternCandidateOrigin? left, RepositoryPatternCandidateOrigin? right)
        => left is null && right is null
            || left is RepositoryPatternCandidateOrigin leftValue && right is RepositoryPatternCandidateOrigin rightValue
            && leftValue.RuleID == rightValue.RuleID
            && leftValue.OccurrenceSet.OccurrenceSetSHA256 == rightValue.OccurrenceSet.OccurrenceSetSHA256
            && leftValue.Receipt.ReceiptSHA256 == rightValue.Receipt.ReceiptSHA256;

    internal void CommitCheckpointDelta()
    {
        _checkpointMutationRevision = _mutationRevision;
        _checkpointObservedPathCursor = _observedPathLog.Count;
        _checkpointHistoryCursor = _history.Count;
        _checkpointTransitionCursor = _transitionMutationLog.Count;
    }

    private static RepositoryCandidateTransition ToTransition(Entry entry)
        => new(entry.Candidate.Digest, entry.Candidate.Canonical, entry.State, entry.Attempts,
            entry.SourceEventID, entry.PredecessorEventID, entry.CallSHA256, entry.AccessSHA256,
            entry.VerifierOutcome, entry.PatternOrigin);

    private void Touch(Entry entry) { entry.MutationRevision = ++_mutationRevision; }

    private void RecordMutation(RepositoryCandidateTransition? before, Entry? after, IReadOnlyList<string>? observedPathAdds = null)
    {
        string[] paths = observedPathAdds?.ToArray() ?? [];
        if (before is null && after is null && paths.Length == 0) return;
        _revision = new RepositoryFrontierRevision(_revision.Value + 1);
        _authorityCache = null;
        _history.Add(new(_revision, before is not null, before.GetValueOrDefault(), after is not null,
            after is null ? default : ToTransition(after), paths));
        if (before is { } prior) _authorityEntries.Remove(AuthorityKey(prior));
        if (after is not null) _authorityEntries.Set(AuthorityKey(ToTransition(after)), AuthorityValue(ToTransition(after)));
        foreach (string path in paths) _authorityPaths.Set(path, path);
        if (after is not null) _transitionMutationLog.Add(ToTransition(after));
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in RepositoryFrontierCheckpointDelta delta)
    {
        writer.U8(2); writer.U64(delta.Revision.Value); writer.I32(delta.Edits.Length);
        foreach (RepositoryCandidateTransition edit in delta.Edits)
        {
            writer.U64(edit.CandidateDigest.Value); writer.Str(edit.CandidateCanonical); writer.U8((byte)edit.State);
            writer.I32(edit.Attempts); writer.I64(edit.SourceEventID.Value); writer.I64(edit.PredecessorEventID.Value);
            writer.Str(edit.CallSHA256); writer.Str(edit.AccessSHA256); writer.Bool(edit.VerifierOutcome is not null);
            if (edit.VerifierOutcome is { } outcome) writer.U8((byte)outcome);
            writer.Bool(edit.PatternOrigin is not null);
            if (edit.PatternOrigin is { } origin) RepositoryPatternStore.WriteOrigin(writer, origin);
        }
        writer.I32(delta.ObservedPathAdds.Length); foreach (string path in delta.ObservedPathAdds) writer.Str(path);
        writer.I32(delta.History.Length);
        foreach (RepositoryFrontierMutation mutation in delta.History)
        {
            writer.U64(mutation.Revision.Value); writer.Bool(mutation.HasBefore); if (mutation.HasBefore) WriteTransition(writer, mutation.Before);
            writer.Bool(mutation.HasAfter); if (mutation.HasAfter) WriteTransition(writer, mutation.After);
            writer.I32(mutation.ObservedPathAdds.Length); foreach (string path in mutation.ObservedPathAdds) writer.Str(path);
        }
    }

    private static void WriteTransition(CkptWriter writer, RepositoryCandidateTransition edit)
    {
        writer.U64(edit.CandidateDigest.Value); writer.Str(edit.CandidateCanonical); writer.U8((byte)edit.State);
        writer.I32(edit.Attempts); writer.I64(edit.SourceEventID.Value); writer.I64(edit.PredecessorEventID.Value);
        writer.Str(edit.CallSHA256); writer.Str(edit.AccessSHA256); writer.Bool(edit.VerifierOutcome is not null);
        if (edit.VerifierOutcome is { } outcome) writer.U8((byte)outcome);
        writer.Bool(edit.PatternOrigin is not null);
        if (edit.PatternOrigin is { } origin) RepositoryPatternStore.WriteOrigin(writer, origin);
    }

    private static RepositoryCandidateTransition ReadTransition(CkptReader reader)
    {
        RepositoryCandidateDigest digest = new(reader.U64()); string canonical = reader.Str();
        RepositoryCandidateStates state = (RepositoryCandidateStates)reader.U8(); int attempts = reader.I32();
        TapeEventID source = new(reader.I64()); TapeEventID predecessor = new(reader.I64()); string call = reader.Str(); string access = reader.Str();
        RepositoryOccurrenceCheckOutcomes? outcome = reader.Bool() ? (RepositoryOccurrenceCheckOutcomes)reader.U8() : null;
        RepositoryPatternCandidateOrigin? origin = reader.Bool() ? RepositoryPatternStore.ReadOrigin(reader) : null;
        return new(digest, canonical, state, attempts, source, predecessor, call, access, outcome, origin);
    }

    internal static RepositoryFrontierCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        byte version = reader.U8(); if (version is not (1 or 2)) throw new InvalidDataException("unknown repository frontier checkpoint delta version");
        RepositoryFrontierRevision revision = new(reader.U64()); int count = reader.I32();
        if (!revision.IsValid || count < 0 || count > 1_000_000) throw new InvalidDataException("repository frontier checkpoint delta is malformed");
        RepositoryCandidateTransition[] edits = new RepositoryCandidateTransition[count];
        for (int i = 0; i < count; i++)
        {
            edits[i] = ReadTransition(reader);
        }
        int observed = reader.I32(); if (observed < 0 || observed > 1_000_000) throw new InvalidDataException("repository frontier observed-path delta is malformed");
        string[] paths = new string[observed]; for (int i = 0; i < observed; i++) paths[i] = reader.Str();
        if (version == 1) return new(revision, edits, paths, []);
        int historyCount = reader.I32(); if (historyCount < 0 || historyCount > 1_000_000) throw new InvalidDataException("repository frontier checkpoint history is malformed");
        RepositoryFrontierMutation[] history = new RepositoryFrontierMutation[historyCount];
        for (int i = 0; i < history.Length; i++)
        {
            RepositoryFrontierRevision mutationRevision = new(reader.U64()); bool hasBefore = reader.Bool(); RepositoryCandidateTransition before = hasBefore ? ReadTransition(reader) : default;
            bool hasAfter = reader.Bool(); RepositoryCandidateTransition after = hasAfter ? ReadTransition(reader) : default;
            int pathCount = reader.I32(); if (pathCount < 0 || pathCount > 1_000_000) throw new InvalidDataException("repository frontier checkpoint history paths are malformed");
            string[] mutationPaths = new string[pathCount]; for (int p = 0; p < pathCount; p++) mutationPaths[p] = reader.Str();
            history[i] = new(mutationRevision, hasBefore, before, hasAfter, after, mutationPaths);
        }
        return new(revision, edits, paths, history);
    }

    internal void ReplaceState(RepositoryFrontierRevision revision,
        IReadOnlyCollection<RepositoryCandidateTransition> transitions,
        IReadOnlyCollection<string> observedPaths)
    {
        if (!revision.IsValid || transitions is null || observedPaths is null)
            throw new InvalidDataException("repository frontier mutation is malformed");
        _entries.Clear();
        _observedPaths.Clear();
        _observedPathLog.Clear();
        _history.Clear();
        _transitionMutationLog.Clear();
        _authorityEntries.Clear();
        _authorityPaths.Clear();
        _mutationRevision = 0;
        _revision = revision;
        _authorityCache = null;
        foreach (RepositoryCandidateTransition transition in transitions)
        {
            if (!RepositoryCandidate.TryParseCanonical(transition.CandidateCanonical, out RepositoryCandidate candidate)
                || candidate.Digest != transition.CandidateDigest)
                throw new InvalidDataException("repository frontier mutation candidate is not reconstructible");
            Entry entry = new()
            {
                Candidate = candidate,
                State = transition.State,
                Attempts = transition.Attempts,
                SourceEventID = transition.SourceEventID,
                PredecessorEventID = transition.PredecessorEventID,
                CallSHA256 = transition.CallSHA256,
                AccessSHA256 = transition.AccessSHA256,
                VerifierOutcome = transition.VerifierOutcome,
                PatternOrigin = transition.PatternOrigin,
            };
            if (!_entries.TryAdd(RepositoryFrontierCandidateKey.Create(candidate), entry))
                throw new InvalidDataException("repository frontier mutation contains duplicate candidates");
        }
        foreach (string path in observedPaths)
            if (!string.IsNullOrWhiteSpace(path)) { _observedPaths.Add(path); _observedPathLog.Add(path); _authorityPaths.Set(path, path); }
        foreach (Entry entry in _entries.Values)
        {
            entry.MutationRevision = ++_mutationRevision;
            _authorityEntries.Set(AuthorityKey(ToTransition(entry)), AuthorityValue(ToTransition(entry)));
        }
        CommitCheckpointDelta();
    }

    internal static string ComputeAuthoritySHA256(IReadOnlyCollection<RepositoryCandidateTransition> transitions,
        IReadOnlyCollection<string>? observedPaths = null)
        => ComputeMerkleAuthority(transitions, observedPaths ?? []);

    private static string AuthorityKey(in RepositoryCandidateTransition transition)
        => transition.CandidateDigest.Value.ToString("X16") + "\u0000" + transition.CandidateCanonical;

    private static string AuthorityValue(in RepositoryCandidateTransition transition)
        => string.Join("\u0001", transition.State, transition.Attempts,
            transition.SourceEventID.Value, transition.PredecessorEventID.Value,
            transition.CallSHA256, transition.AccessSHA256,
            transition.VerifierOutcome?.ToString() ?? "",
            transition.PatternOrigin?.RuleID.Value ?? "",
            transition.PatternOrigin?.OccurrenceSet.OccurrenceSetSHA256 ?? "",
            transition.PatternOrigin?.Receipt.ReceiptSHA256 ?? "");

    private static string CombineAuthorityRoots(string entriesRoot, string pathsRoot)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            RepositoryOrderedMerkleMap.EncodeFields("frontier-authority-v2", entriesRoot, pathsRoot))));

    private static string ComputeMerkleAuthority(
        IReadOnlyCollection<RepositoryCandidateTransition> transitions,
        IReadOnlyCollection<string> paths)
    {
        RepositoryOrderedMerkleMap entries = new();
        RepositoryOrderedMerkleMap observed = new();
        foreach (RepositoryCandidateTransition transition in transitions)
            entries.Set(AuthorityKey(transition), AuthorityValue(transition));
        foreach (string path in paths) observed.Set(path, path);
        return CombineAuthorityRoots(entries.RootHash, observed.RootHash);
    }

    internal static ulong ComputeAuthorityDigest(IReadOnlyCollection<RepositoryCandidateTransition> transitions)
        => BinaryPrimitives.ReadUInt64BigEndian(Convert.FromHexString(ComputeMerkleAuthority(transitions, [])));

    public void SeedQuery(string query)
    {
        foreach (string term in SeededSearchTerms(query))
            Add(RepositoryCandidate.CreateSearchTerm(new RepositorySearchTerm(term)));
    }

    /// The search terms a query seeds, once, at birth — every word n-gram up to width three. This is
    /// the crawler's entire initial appetite: nothing later mints a SearchTerm, so this set is a
    /// closed queue that drains exactly one entry per selection.
    internal static IEnumerable<string> SeededSearchTerms(string query)
    {
        List<string> tokens = Tokenize(query);
        for (int width = 1; width <= Math.Min(3, tokens.Count); width++)
            for (int offset = 0; offset + width <= tokens.Count; offset++)
                yield return string.Join(' ', tokens.Skip(offset).Take(width));
    }

    /// How many selections the seeded search queue will consume before any other species can be
    /// reached. A SearchTerm outscores every other candidate while it is unattempted, so this depth
    /// is a HARD FLOOR on the horizon: a run shorter than it cannot reach its own occurrenceCheck stage,
    /// and three such runs are separated by their allowance rather than by their world.
    internal static int SeededQueueDepth(string query) => SeededSearchTerms(query).Count();

    public bool TryPropose(out RepositoryCandidateProposal proposal)
    {
        return TryPropose(species: null, out proposal);
    }

    public bool TryPropose(RepositoryCandidateSpecies? species, out RepositoryCandidateProposal proposal)
    {
        Entry[] ordered = OrderForSelection(
            _entries.Values.Where(entry => entry.State == RepositoryCandidateStates.Eligible
                && (species is not { } requested || entry.Candidate.Species == requested)),
            _entries.Values, _observedPaths);
        if (ordered.Length == 0)
        {
            proposal = default;
            return false;
        }
        proposal = new RepositoryCandidateProposal(_revision, ordered[0].Candidate.Digest, ordered[0].Candidate);
        return true;
    }

    /// The selection order, in ONE place because three sites must agree on it byte-for-byte: the live
    /// proposal, the ordinal a receipt records, and the ordinal a replay reconstructs from mutations.
    ///
    /// Score decides first. What breaks a TIE is the load-bearing part. An alphabetical tie-break made
    /// the canonical SPELLING a priority, and because every verify prediction scores alike, that starved
    /// the one prediction species the composition stage admits: `shared-identifier` sorts behind
    /// `locus-contains` and `path-exists`, so twelve confirmed occurrences produced zero compositions —
    /// deterministically, at any horizon. Ties now go to the species the organism has spent the LEAST
    /// on. That is not a new prior; it is the removal of an accidental one, and no species can be
    /// starved by how its name happens to sort. Canonical stays last so the order remains total.
    ///
    /// The load is summed over EVERY entry, not the eligible pool: an attempted candidate leaves the
    /// pool and would take its Attempts with it, so a pool-local sum reads zero for every class
    /// forever and the tie-break silently degenerates back to alphabetical. Spending is a property of
    /// the organism, not of what remains selectable. Attempts rides every candidate transition, so a
    /// replay reconstructing entries from the mutation log computes an identical key and the recorded
    /// selection ordinals still reproduce.
    private static Entry[] OrderForSelection(IEnumerable<Entry> eligible, IEnumerable<Entry> all, IReadOnlySet<string> observedPaths)
    {
        Entry[] entries = eligible.ToArray();
        Dictionary<string, int> load = new(StringComparer.Ordinal);
        foreach (Entry entry in all)
            load[entry.Candidate.SelectionClass] = load.GetValueOrDefault(entry.Candidate.SelectionClass) + entry.Attempts;
        return [.. entries
            .OrderByDescending(entry => Score(entry, observedPaths))
            .ThenBy(entry => load.GetValueOrDefault(entry.Candidate.SelectionClass))
            .ThenBy(entry => entry.Candidate.Canonical, StringComparer.Ordinal)];
    }

    public bool IsCurrent(in RepositoryCandidateProposal proposal)
        => proposal.IsValid && proposal.Revision == _revision
            && _entries.TryGetValue(RepositoryFrontierCandidateKey.Create(proposal.Candidate), out Entry? entry)
            && entry.State == RepositoryCandidateStates.Eligible
            && entry.Candidate.Canonical == proposal.Candidate.Canonical;

    internal bool TryResolveCaptured(
        RepositoryFrontierRevision revision,
        RepositoryCandidateDigest digest,
        string canonical,
        string authoritySHA256,
        out RepositoryCandidateProposal proposal)
    {
        proposal = default;
        if (revision != _revision || !RepositoryLineageReceiptCodec.IsSHA(authoritySHA256)
            || !string.Equals(authoritySHA256, AuthoritySHA256, StringComparison.Ordinal)
            || !RepositoryCandidate.TryParseCanonical(canonical, out RepositoryCandidate candidate)
            || candidate.Digest != digest)
            return false;
        proposal = new RepositoryCandidateProposal(revision, digest, candidate);
        return IsCurrent(in proposal);
    }

    internal int GetSelectionOrdinal(in RepositoryCandidateProposal proposal, RepositoryCandidateSpecies? species = null)
    {
        if (!proposal.IsValid) throw new InvalidDataException("repository candidate proposal is malformed");
        RepositoryCandidate[] ordered = OrderForSelection(
                _entries.Values.Where(entry => entry.State == RepositoryCandidateStates.Eligible
                    && (species is null || entry.Candidate.Species == species)),
                _entries.Values, _observedPaths)
            .Select(static entry => entry.Candidate)
            .ToArray();
        RepositoryCandidateDigest proposedDigest = proposal.CandidateDigest;
        string proposedCanonical = proposal.Candidate.Canonical;
        int ordinal = Array.FindIndex(ordered, candidate => candidate.Digest == proposedDigest
            && candidate.Canonical == proposedCanonical);
        if (ordinal < 0) throw new InvalidDataException("repository candidate proposal is absent from the frontier order");
        return ordinal;
    }

    internal bool TryGetHistoricalAuthority(RepositoryFrontierRevision revision,
        RepositoryCandidateDigest selectedDigest, string selectedCanonical,
        out string authoritySHA256, out int selectionOrdinal, out int observedPathCount, out int frontierCount)
    {
        authoritySHA256 = ""; selectionOrdinal = -1; observedPathCount = 0; frontierCount = 0;
        if (!revision.IsValid || revision.Value > _revision.Value || _history.Count == 0) return false;
        Dictionary<RepositoryFrontierCandidateKey, Entry> entries = new();
        HashSet<string> paths = new(StringComparer.Ordinal);
        ulong expectedRevision = 2;
        foreach (RepositoryFrontierMutation mutation in _history)
        {
            if (mutation.Revision.Value != expectedRevision) return false;
            if (mutation.Revision.Value > revision.Value) break;
            foreach (string path in mutation.ObservedPathAdds) if (!paths.Add(path)) return false;
            if (mutation.HasBefore)
            {
                if (!RepositoryCandidate.TryParseCanonical(mutation.Before.CandidateCanonical, out RepositoryCandidate beforeCandidate)) return false;
                RepositoryFrontierCandidateKey key = RepositoryFrontierCandidateKey.Create(beforeCandidate);
                if (!entries.ContainsKey(key)) return false;
                entries.Remove(key);
            }
            if (mutation.HasAfter)
            {
                if (!RepositoryCandidate.TryParseCanonical(mutation.After.CandidateCanonical, out RepositoryCandidate afterCandidate)) return false;
                entries[RepositoryFrontierCandidateKey.Create(afterCandidate)] = ToEntry(afterCandidate, mutation.After);
            }
            expectedRevision++;
        }
        if (expectedRevision - 1 != revision.Value) return false;
        RepositoryCandidateTransition[] transitions = entries.Values.Select(ToTransition).ToArray();
        authoritySHA256 = ComputeMerkleAuthority(transitions, paths);
        observedPathCount = paths.Count; frontierCount = entries.Count;
        Entry[] ordered = OrderForSelection(
            entries.Values.Where(entry => entry.State == RepositoryCandidateStates.Eligible), entries.Values, paths);
        selectionOrdinal = Array.FindIndex(ordered, entry => entry.Candidate.Digest == selectedDigest && entry.Candidate.Canonical == selectedCanonical);
        return selectionOrdinal >= 0;

        static Entry ToEntry(RepositoryCandidate candidate, RepositoryCandidateTransition transition)
            => new() { Candidate = candidate, State = transition.State, Attempts = transition.Attempts,
                SourceEventID = transition.SourceEventID, PredecessorEventID = transition.PredecessorEventID,
                CallSHA256 = transition.CallSHA256, AccessSHA256 = transition.AccessSHA256,
                VerifierOutcome = transition.VerifierOutcome, PatternOrigin = transition.PatternOrigin };
    }

    public bool TryCommit(in RepositoryCandidateProposal proposal, TapeEventID predecessorEventID, TapeEventID sourceEventID,
        string callSHA256, string accessSHA256, Tool.Observation observation,
        IReadOnlyList<CortexObservationField> fields, IReadOnlyList<RepositoryAccessEntry> accessEntries,
        RepositoryOccurrenceCheckResult? occurrenceCheck)
    {
        if (!IsCurrent(proposal)) return false;
        Entry entry = _entries[RepositoryFrontierCandidateKey.Create(proposal.Candidate)];
        RepositoryCandidateTransition before = ToTransition(entry);
        entry.Attempts++;
        entry.State = RepositoryCandidateStates.Committed;
        entry.SourceEventID = sourceEventID;
        entry.PredecessorEventID = predecessorEventID;
        entry.CallSHA256 = callSHA256;
        entry.AccessSHA256 = accessSHA256;
        entry.VerifierOutcome = occurrenceCheck?.Outcome;
        Touch(entry);
        RecordMutation(before, entry);
        MintObservation(observation, fields, accessEntries, sourceEventID);
        return true;
    }

    public bool TryGetSourceEventID(RepositoryCandidateDigest digest, out TapeEventID sourceEventID)
    {
        Entry? match = null;
        foreach (KeyValuePair<RepositoryFrontierCandidateKey, Entry> item in _entries)
        {
            if (item.Key.Digest != digest) continue;
            if (match is not null) { sourceEventID = default; return false; }
            match = item.Value;
        }
        if (match is { SourceEventID.Value: > 0 })
        {
            sourceEventID = match.SourceEventID;
            return true;
        }
        sourceEventID = default;
        return false;
    }

    public bool TryGetSourceEventID(in RepositoryCandidateProposal proposal, out TapeEventID sourceEventID)
    {
        if (!proposal.IsValid || !_entries.TryGetValue(RepositoryFrontierCandidateKey.Create(proposal.Candidate), out Entry? entry)
            || entry.SourceEventID.Value <= 0)
        {
            sourceEventID = default;
            return false;
        }
        sourceEventID = entry.SourceEventID;
        return true;
    }

    public bool AdmitComposedCandidate(RepositoryPatternCandidateConclusion conclusion, RepositoryComposedCandidateReceipt receipt)
    {
        conclusion.Validate();
        receipt.Validate();
        if (receipt.RuleID != conclusion.RuleID || receipt.CandidateDigest != conclusion.CandidateDigest
            || receipt.CandidateCanonical != conclusion.Candidate.Canonical || receipt.OccurrenceSetSHA256 != conclusion.OccurrenceSet.OccurrenceSetSHA256)
            throw new InvalidDataException("repository composed candidate frontier custody diverges");
        var origin = new RepositoryPatternCandidateOrigin(conclusion.RuleID, conclusion.OccurrenceSet, receipt);
        origin.Validate();
        RepositoryFrontierCandidateKey key = RepositoryFrontierCandidateKey.Create(conclusion.Candidate);
        if (_entries.TryGetValue(key, out Entry? existing))
        {
            if (existing.PatternOrigin is { } prior)
            {
                if (prior.RuleID != origin.RuleID || prior.OccurrenceSet.OccurrenceSetSHA256 != origin.OccurrenceSet.OccurrenceSetSHA256
                    || prior.Receipt.ReceiptSHA256 != origin.Receipt.ReceiptSHA256)
                    throw new InvalidDataException("repository composed candidate origin was reused");
                return false;
            }
            RepositoryCandidateTransition before = ToTransition(existing);
            existing.PatternOrigin = origin;
            Touch(existing);
            RecordMutation(before, existing);
            return true;
        }
        _entries.Add(key, new Entry
        {
            Candidate = conclusion.Candidate,
            State = RepositoryCandidateStates.Eligible,
            PatternOrigin = origin,
        });
        Touch(_entries[key]);
        RecordMutation(null, _entries[key]);
        return true;
    }

    public void SaveState(CkptWriter writer)
    {
        writer.Section(0x52434633); // RCF3 — ordered Merkle authority + durable history
        writer.U64(_revision.Value);
        writer.I32(_entries.Count);
        foreach (Entry entry in _entries.Values.OrderBy(static value => value.Candidate.Digest.Value).ThenBy(static value => value.Candidate.Canonical, StringComparer.Ordinal))
        {
            WriteCandidate(writer, entry.Candidate);
            writer.U8((byte)entry.State); writer.I32(entry.Attempts); writer.I64(entry.SourceEventID.Value);
            writer.I64(entry.PredecessorEventID.Value); writer.Str(entry.CallSHA256); writer.Str(entry.AccessSHA256);
            writer.Bool(entry.VerifierOutcome is not null);
            if (entry.VerifierOutcome is { } outcome) writer.U8((byte)outcome);
            writer.Bool(entry.PatternOrigin is not null);
            if (entry.PatternOrigin is { } origin) RepositoryPatternStore.WriteOrigin(writer, origin);
        }
        writer.I32(_observedPaths.Count);
        foreach (string path in _observedPaths.Order(StringComparer.Ordinal)) writer.Str(path);
        writer.I32(_history.Count);
        foreach (RepositoryFrontierMutation mutation in _history)
        {
            writer.U64(mutation.Revision.Value); writer.Bool(mutation.HasBefore); if (mutation.HasBefore) WriteTransition(writer, mutation.Before);
            writer.Bool(mutation.HasAfter); if (mutation.HasAfter) WriteTransition(writer, mutation.After);
            writer.I32(mutation.ObservedPathAdds.Length); foreach (string path in mutation.ObservedPathAdds) writer.Str(path);
        }
        writer.Str(AuthoritySHA256);
    }

    public void LoadState(CkptReader reader)
    {
        uint section = reader.U32();
        if (section != 0x52434633) throw new InvalidDataException("repository candidate frontier section is unsupported");
        ulong revision = reader.U64();
        if (revision == 0) throw new InvalidDataException("repository candidate frontier revision is malformed");
        _entries.Clear(); _observedPaths.Clear(); _history.Clear(); _transitionMutationLog.Clear(); _revision = new RepositoryFrontierRevision(revision);
        _authorityEntries.Clear(); _authorityPaths.Clear();
        _authorityCache = null;
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("repository candidate frontier count is malformed");
        for (int i = 0; i < count; i++)
        {
            RepositoryCandidate candidate = ReadCandidate(reader);
            var state = (RepositoryCandidateStates)reader.U8();
            if (!Enum.IsDefined(state)) throw new InvalidDataException("repository candidate state is malformed");
            Entry entry = new()
            {
                Candidate = candidate,
                State = state,
                Attempts = reader.I32(),
                SourceEventID = new TapeEventID(reader.I64()),
                PredecessorEventID = new TapeEventID(reader.I64()),
                CallSHA256 = reader.Str(),
                AccessSHA256 = reader.Str(),
                VerifierOutcome = reader.Bool() ? (RepositoryOccurrenceCheckOutcomes)reader.U8() : null,
            };
            entry.PatternOrigin = reader.Bool() ? RepositoryPatternStore.ReadOrigin(reader) : null;
            if (entry.Attempts < 0 || entry.SourceEventID.Value < 0 || entry.PredecessorEventID.Value < 0
                || !_entries.TryAdd(RepositoryFrontierCandidateKey.Create(candidate), entry))
                throw new InvalidDataException("repository candidate frontier entry is malformed");
        }
        int observed = reader.I32();
        if (observed < 0 || observed > 1_000_000) throw new InvalidDataException("repository observed path count is malformed");
        for (int i = 0; i < observed; i++)
        {
            string path = reader.Str();
            if (path.Length == 0 || !_observedPaths.Add(path)) throw new InvalidDataException("repository observed path is malformed");
            _observedPathLog.Add(path);
        }
        _history.Clear();
        _transitionMutationLog.Clear();
        if (section == 0x52434633)
        {
            int historyCount = reader.I32(); if (historyCount < 0 || historyCount > 10_000_000) throw new InvalidDataException("repository frontier history count is malformed");
            for (int i = 0; i < historyCount; i++)
            {
                RepositoryFrontierRevision mutationRevision = new(reader.U64()); bool hasBefore = reader.Bool(); RepositoryCandidateTransition before = hasBefore ? ReadTransition(reader) : default;
                bool hasAfter = reader.Bool(); RepositoryCandidateTransition after = hasAfter ? ReadTransition(reader) : default;
                int pathCount = reader.I32(); if (pathCount < 0 || pathCount > 1_000_000) throw new InvalidDataException("repository frontier history paths are malformed");
                string[] paths = new string[pathCount]; for (int p = 0; p < pathCount; p++) paths[p] = reader.Str();
                _history.Add(new(mutationRevision, hasBefore, before, hasAfter, after, paths));
            }
            ulong expectedHistoryRevision = 2;
            foreach (RepositoryFrontierMutation mutation in _history)
                if (mutation.Revision.Value != expectedHistoryRevision++)
                    throw new InvalidDataException("repository frontier history revision gap or duplicate");
            if (_history.Count != 0 && _history[^1].Revision != _revision)
                throw new InvalidDataException("repository frontier history does not reach loaded revision");
        }
        string expectedAuthority = reader.Str();
        RepositoryLineageReceiptCodec.RequireSHA(expectedAuthority, "frontier keyframe authority");
        Dictionary<RepositoryFrontierCandidateKey, RepositoryCandidateTransition> replayed = new();
        HashSet<string> replayedPaths = new(StringComparer.Ordinal);
        ulong replayRevision = 1;
        foreach (RepositoryFrontierMutation mutation in _history)
        {
            if (mutation.Revision.Value != ++replayRevision
                || mutation.ObservedPathAdds.Any(path => string.IsNullOrWhiteSpace(path) || !replayedPaths.Add(path)))
                throw new InvalidDataException("repository frontier history cannot be replayed");
            if (!mutation.HasBefore && !mutation.HasAfter && mutation.ObservedPathAdds.Length == 0)
                throw new InvalidDataException("repository frontier history contains an empty mutation");
            RepositoryFrontierCandidateKey key = default;
            if (mutation.HasBefore)
            {
                key = ValidateTransition(mutation.Before);
                if (!replayed.TryGetValue(key, out RepositoryCandidateTransition prior)
                    || !TransitionEquals(prior, mutation.Before))
                    throw new InvalidDataException("repository frontier history predecessor diverged");
            }
            if (mutation.HasAfter)
            {
                RepositoryCandidateTransition after = mutation.After;
                RepositoryFrontierCandidateKey afterKey = ValidateTransition(after);
                if (mutation.HasBefore && afterKey != key)
                    throw new InvalidDataException("repository frontier history candidate identity diverged");
                if (!mutation.HasBefore && replayed.ContainsKey(afterKey))
                    throw new InvalidDataException("repository frontier history creates an existing candidate");
                replayed[afterKey] = after;
            }
            else if (mutation.HasBefore)
                replayed.Remove(key);
        }
        if (replayRevision != _revision.Value
            || replayedPaths.SetEquals(_observedPaths) == false
            || replayed.Count != _entries.Count)
            throw new InvalidDataException("repository frontier keyframe does not match replayed history");
        foreach ((RepositoryFrontierCandidateKey key, Entry entry) in _entries)
            if (!replayed.TryGetValue(key, out RepositoryCandidateTransition replayedTransition)
                || !TransitionEquals(replayedTransition, ToTransition(entry)))
                throw new InvalidDataException("repository frontier keyframe entry diverges from history");
        _mutationRevision = 0;
        foreach (Entry entry in _entries.Values) entry.MutationRevision = ++_mutationRevision;
        foreach (Entry entry in _entries.Values) _authorityEntries.Set(AuthorityKey(ToTransition(entry)), AuthorityValue(ToTransition(entry)));
        foreach (string path in _observedPaths) _authorityPaths.Set(path, path);
        if (!string.Equals(expectedAuthority, AuthoritySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository frontier keyframe authority root diverges from replay");
        CommitCheckpointDelta();
    }

    private void MintObservation(Tool.Observation observation, IReadOnlyList<CortexObservationField> fields,
        IReadOnlyList<RepositoryAccessEntry> accessEntries, TapeEventID sourceEventID)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (RepositoryPath path in observation.HitPaths)
            if (path.Length > 0) paths.Add(path.Value);
        if (observation.AnswerPath.Length > 0) paths.Add(observation.AnswerPath.Value);
        foreach (CortexObservationField field in fields)
            if (field.Slot is "top_hit" or "hit_path" or "answer_path" or "repository_path" && field.Value.Length > 0)
                paths.Add(field.Value);
        foreach (RepositoryAccessEntry access in accessEntries)
            foreach (RepositoryPath path in access.Paths)
                if (path.Length > 0) paths.Add(path.Value);

        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            if (_observedPaths.Add(path))
            {
                _observedPathLog.Add(path);
                RecordMutation(null, null, [path]);
            }
            Add(RepositoryCandidate.CreateOpenPath(new RepositoryOpenPath(path)), sourceEventID);
            Add(RepositoryCandidate.CreateAnswerPath(new RepositoryAnswerPath(path)), sourceEventID);
            Add(RepositoryCandidate.CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(RepositoryPrediction.PathExists(path))), sourceEventID);
            AddPrefixes(path, sourceEventID);
        }

        var loci = new HashSet<RepositoryLocus>();
        foreach (RepositoryLocus locus in observation.Loci)
            if (locus.Line > 0 && locus.Path.Length > 0) loci.Add(locus);
        foreach (CortexObservationField field in fields.Where(static field => field.Slot == "repository_locus"))
            if (TryParseLocusField(field.Value, out RepositoryLocus locus)) loci.Add(locus);
        foreach (RepositoryAccessEntry access in accessEntries)
            foreach (RepositoryLocus locus in access.Loci)
                if (locus.Line > 0 && locus.Path.Length > 0) loci.Add(locus);

        foreach (RepositoryLocus locus in loci.OrderBy(static value => value.Path.Value, StringComparer.Ordinal).ThenBy(static value => value.Line))
        {
            Add(RepositoryCandidate.CreateReadLocus(new RepositoryReadLocus(locus)), sourceEventID);
            if (TryReadLocusValue(observation.Text, locus, out string value)
                || TryReadLocusValue(accessEntries, locus, out value))
                Add(RepositoryCandidate.CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(RepositoryPrediction.LocusContains(locus, value))), sourceEventID);
        }

        foreach ((string identifier, string[] identifierPaths) in FindSharedIdentifiers(observation.Text, accessEntries))
            Add(RepositoryCandidate.CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(
                RepositoryPrediction.SharedIdentifier(identifier, identifierPaths[0], identifierPaths[1]))), sourceEventID);
    }

    private void AddPrefixes(string path, TapeEventID sourceEventID)
    {
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Add(RepositoryCandidate.CreateListPrefix(new RepositoryListPrefix("")), sourceEventID);
        for (int length = 1; length < segments.Length; length++)
            Add(RepositoryCandidate.CreateListPrefix(new RepositoryListPrefix(string.Join('/', segments.Take(length)))), sourceEventID);
    }

    private void Add(RepositoryCandidate candidate, TapeEventID sourceEventID = default)
    {
        if (candidate.Canonical.Length == 0 || !candidate.Digest.IsValid) return;
        if (_entries.TryAdd(RepositoryFrontierCandidateKey.Create(candidate), new Entry { Candidate = candidate, State = RepositoryCandidateStates.Eligible, SourceEventID = sourceEventID }))
        {
            Entry entry = _entries[RepositoryFrontierCandidateKey.Create(candidate)];
            Touch(entry);
            RecordMutation(null, entry);
        }
    }

    private double Score(Entry entry)
        => Score(entry, _observedPaths);

    private static double Score(Entry entry, IReadOnlySet<string> observedPaths)
    {
        RepositoryCandidate candidate = entry.Candidate;
        double querySupport = candidate.Species == RepositoryCandidateSpecies.SearchTerm ? 30 : 0;
        double evidenceDepth = candidate.Species switch
        {
            RepositoryCandidateSpecies.VerifyPrediction => 25,
            RepositoryCandidateSpecies.ReadLocus => 23,
            RepositoryCandidateSpecies.OpenPath => 18,
            RepositoryCandidateSpecies.ListPrefix => 12,
            RepositoryCandidateSpecies.AnswerPath => 10,
            _ => 0,
        };
        double coherence = candidate.Species == RepositoryCandidateSpecies.AnswerPath && observedPaths.Contains(candidate.Argument) ? 8 : 0;
        double novelty = entry.Attempts == 0 ? 8 : -entry.Attempts * 9;
        double occurrenceCheck = entry.VerifierOutcome switch
        {
            RepositoryOccurrenceCheckOutcomes.Confirmed => 20,
            RepositoryOccurrenceCheckOutcomes.Refuted => -20,
            RepositoryOccurrenceCheckOutcomes.Unobserved => -2,
            _ => 0,
        };
        return querySupport + evidenceDepth + coherence + novelty + occurrenceCheck;
    }

    private static List<string> Tokenize(string query)
    {
        var tokens = new List<string>();
        int offset = 0;
        while (offset < query.Length)
        {
            while (offset < query.Length && !char.IsLetterOrDigit(query[offset]) && query[offset] != '_') offset++;
            int start = offset;
            while (offset < query.Length && (char.IsLetterOrDigit(query[offset]) || query[offset] == '_')) offset++;
            if (offset - start >= Loc.MinTermLen) tokens.Add(query[start..offset].ToLowerInvariant());
        }
        return tokens;
    }

    private static bool TryParseLocusField(string value, out RepositoryLocus locus)
    {
        int tab = value.LastIndexOf('\t');
        int colon = value.LastIndexOf(':');
        if (tab > 0 && int.TryParse(value[(tab + 1)..], out int tabLine))
        {
            locus = new RepositoryLocus(value[..tab], tabLine);
            return locus.Line > 0;
        }
        if (colon > 0 && int.TryParse(value[(colon + 1)..], out int line))
        {
            locus = new RepositoryLocus(value[..colon], line);
            return locus.Line > 0;
        }
        locus = default;
        return false;
    }

    private static bool TryReadLocusValue(string text, RepositoryLocus locus, out string value)
    {
        string prefix = $"{locus.Path.Value}:{locus.Line}:";
        foreach (string line in text.Split('\n'))
        {
            int start = line.IndexOf(prefix, StringComparison.Ordinal);
            if (start >= 0)
            {
                value = line[(start + prefix.Length)..].Trim();
                if (value.Length > 0) return true;
            }
        }
        value = "";
        return false;
    }

    private static bool TryReadLocusValue(IReadOnlyList<RepositoryAccessEntry> entries, RepositoryLocus locus, out string value)
    {
        foreach (RepositoryAccessEntry entry in entries)
            if (TryReadLocusValue(Encoding.UTF8.GetString(entry.RenderedBytes), locus, out value)) return true;
        value = "";
        return false;
    }

    private static IEnumerable<(string Identifier, string[] Paths)> FindSharedIdentifiers(string text,
        IReadOnlyList<RepositoryAccessEntry> entries)
    {
        var byIdentifier = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Scan(string source, IEnumerable<RepositoryPath> paths)
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            int offset = 0;
            while (offset < source.Length)
            {
                while (offset < source.Length && !(char.IsLetter(source[offset]) || source[offset] == '_')) offset++;
                int start = offset;
                while (offset < source.Length && (char.IsLetterOrDigit(source[offset]) || source[offset] == '_')) offset++;
                if (offset - start >= 3) identifiers.Add(source[start..offset]);
            }
            foreach (string identifier in identifiers)
            {
                if (!byIdentifier.TryGetValue(identifier, out HashSet<string>? seen)) byIdentifier.Add(identifier, seen = new());
                foreach (RepositoryPath path in paths) if (path.Length > 0) seen.Add(path.Value);
            }
        }
        if (entries.Count == 0) Scan(text, Array.Empty<RepositoryPath>());
        foreach (RepositoryAccessEntry entry in entries)
        {
            string rendered = Encoding.UTF8.GetString(entry.RenderedBytes);
            if (entry.Paths.Length == 1)
            {
                Scan(rendered, entry.Paths);
                continue;
            }
            foreach (string line in rendered.Split('\n'))
                foreach (RepositoryPath path in entry.Paths)
                    if (line.Contains(path.Value + ":", StringComparison.Ordinal)) Scan(line, [path]);
        }
        foreach ((string identifier, HashSet<string> paths) in byIdentifier.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            if (paths.Count >= 2)
            {
                string[] ordered = paths.Order(StringComparer.Ordinal).Take(2).ToArray();
                yield return (identifier, ordered);
            }
    }

    private static void WriteCandidate(CkptWriter writer, RepositoryCandidate candidate)
    {
        writer.U8((byte)candidate.Species);
        writer.Str(candidate.Argument);
        if (candidate is RepositoryCandidate.VerifyPredictionCandidate verify)
        {
            RepositoryPrediction prediction = verify.Prediction.Prediction;
            writer.U8((byte)prediction.Species); writer.Str(prediction.Path); writer.I32(prediction.Line);
            writer.Str(prediction.Value); writer.Str(prediction.OtherPath);
        }
    }

    private static RepositoryCandidate ReadCandidate(CkptReader reader)
    {
        var species = (RepositoryCandidateSpecies)reader.U8();
        string argument = reader.Str();
        return species switch
        {
            RepositoryCandidateSpecies.SearchTerm => CreateSearch(argument),
            RepositoryCandidateSpecies.ListPrefix => RepositoryCandidate.CreateListPrefix(new RepositoryListPrefix(argument == "." ? "" : argument)),
            RepositoryCandidateSpecies.OpenPath => RepositoryCandidate.CreateOpenPath(new RepositoryOpenPath(argument)),
            RepositoryCandidateSpecies.ReadLocus when TryParseLocusField(argument, out RepositoryLocus locus)
                => RepositoryCandidate.CreateReadLocus(new RepositoryReadLocus(locus)),
            RepositoryCandidateSpecies.VerifyPrediction => ReadVerifyPrediction(reader),
            RepositoryCandidateSpecies.AnswerPath => RepositoryCandidate.CreateAnswerPath(new RepositoryAnswerPath(argument)),
            _ => throw new InvalidDataException("repository candidate payload is malformed"),
        };

        static RepositoryCandidate CreateSearch(string argument)
            => RepositoryCandidate.CreateSearchTerm(new RepositorySearchTerm(argument));

        static RepositoryCandidate ReadVerifyPrediction(CkptReader reader)
        {
            var prediction = new RepositoryPrediction((RepositoryPredictionSpecies)reader.U8(), reader.Str(), reader.I32(), reader.Str(), reader.Str());
            prediction.Validate();
            return RepositoryCandidate.CreateVerifyPrediction(new RepositoryOccurrenceCheckPrediction(prediction));
        }
    }
}
