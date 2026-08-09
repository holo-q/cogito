namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using RepositoryLocus = Cogito.Tool.RepositoryLocus;
using RepositoryPath = Cogito.Tool.RepositoryPath;

/// The three predictions the native repository world can evaluate. A prediction is data,
/// not a command: its canonical form is the digest input used by receipts.
public enum RepositoryPredictionSpecies : byte
{
    PathExists,
    LocusContains,
    SharedIdentifier,
}

public readonly record struct RepositoryPrediction(
    RepositoryPredictionSpecies Species,
    string Path,
    int Line,
    string Value,
    string OtherPath)
{
    public static RepositoryPrediction PathExists(RepositoryPath path)
        => new(RepositoryPredictionSpecies.PathExists, path.Value, 0, "", "");

    public static RepositoryPrediction LocusContains(RepositoryLocus locus, string value)
        => new(RepositoryPredictionSpecies.LocusContains, locus.Path.Value, locus.Line, value.Trim(), "");

    public static RepositoryPrediction SharedIdentifier(string value, RepositoryPath left, RepositoryPath right)
        => new(RepositoryPredictionSpecies.SharedIdentifier, left.Value, 0, value.Trim(), right.Value);

    public string Canonical
        => Species switch
        {
            // Frozen canonical prefix; identifier-side name is PredictionSpecies.PathExists.
            RepositoryPredictionSpecies.PathExists => $"path-exists\t{Path}",
            // Frozen canonical prefix; identifier-side name is PredictionSpecies.LocusContains.
            RepositoryPredictionSpecies.LocusContains => $"locus-contains\t{Path}:{Line}\t{Value}",
            // Frozen canonical prefix; identifier-side name is PredictionSpecies.SharedIdentifier.
            RepositoryPredictionSpecies.SharedIdentifier => $"shared-identifier\t{Value}\t{Path}\t{OtherPath}",
            _ => throw new InvalidDataException("unknown repository prediction species"),
        };

    public string SHA256 => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Canonical)));

    public void Validate()
    {
        if (!Enum.IsDefined(Species) || string.IsNullOrWhiteSpace(Path))
            throw new InvalidDataException("repository prediction is malformed");
        if (Species == RepositoryPredictionSpecies.LocusContains && (Line < 1 || string.IsNullOrWhiteSpace(Value)))
            throw new InvalidDataException("repository locus prediction is malformed");
        if (Species == RepositoryPredictionSpecies.SharedIdentifier
            && (string.IsNullOrWhiteSpace(Value) || string.IsNullOrWhiteSpace(OtherPath)))
            throw new InvalidDataException("repository shared-identifier prediction is malformed");
    }

    /// Parse the ordinary `verify` grammar. Delimiters are intentionally forgiving
    /// because the generated call is a language surface, not a shell expression.
    public static bool TryParse(string raw, out RepositoryPrediction prediction)
    {
        prediction = default;
        string[] parts = raw.Trim().Split([',', ' ', '\t', '='], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        string head = parts[0].ToLowerInvariant().Replace('_', '-');
        try
        {
            if (head is "path-exists" or "exists" or "path" && parts.Length >= 2)
            {
                prediction = PathExists(parts[1]);
            }
            else if (head is "locus-contains" or "contains" or "locus" && parts.Length >= 3)
            {
                int colon = parts[1].LastIndexOf(':');
                if (colon <= 0 || !int.TryParse(parts[1][(colon + 1)..], out int line)) return false;
                // The needle is EVERYTHING after the locus, taken verbatim. Tokenizing it would keep
                // only its first word, and a prediction whose needle is a line of real code ("return
                // Aggregate.AggregateAsync(source, seed, …)") would then re-parse into a DIFFERENT
                // prediction than the one it was rendered from — its canonical no longer round-trips, its
                // digest diverges, and the frontier snapshot refuses itself at the terminal
                // transition. Encode and decode must be symmetric or the authority is fiction.
                prediction = LocusContains(new RepositoryLocus(parts[1][..colon], line), ReadTail(raw, parts[1]));
            }
            else if (head is "shared-identifier" or "shared" or "identifier" && parts.Length >= 4)
            {
                prediction = SharedIdentifier(parts[1], parts[2], parts[3]);
            }
            else return false;
            prediction.Validate();
            return true;
        }
        catch (InvalidDataException) { return false; }

        /// Everything after the locus token, with the separator run that followed it removed. Kept
        /// verbatim so a needle carrying spaces, commas or '=' survives the round trip.
        static string ReadTail(string raw, string locusToken)
        {
            string text = raw.Trim();
            int start = text.IndexOf(locusToken, StringComparison.Ordinal);
            if (start < 0) return "";
            return text[(start + locusToken.Length)..].TrimStart(',', ' ', '\t', '=').Trim();
        }
    }
}

public enum RepositoryOccurrenceCheckOutcomes : byte
{
    Confirmed,
    Refuted,
    Unobserved,
}

public readonly record struct RepositoryAccessEntry(
    int Step,
    long Sequence,
    string CallSHA256,
    Tool.ToolVerbs Verb,
    string Argument,
    RepositoryPath[] Paths,
    RepositoryLocus[] Loci,
    byte[] RenderedBytes)
{
    public string RenderedSHA256 => Convert.ToHexStringLower(SHA256.HashData(RenderedBytes));
    public long RenderedByteCount => RenderedBytes.LongLength;
    public string EntrySHA256 => ComputeEntrySHA256(this);

    public void Validate()
    {
        if (Step < 0 || Sequence < 0 || !Enum.IsDefined(Verb)
            || CallSHA256 is not { Length: 64 } || !CallSHA256.All(Uri.IsHexDigit)
            || Argument is null || Paths is null || Loci is null || Paths.Any(path => string.IsNullOrWhiteSpace(path.Value))
            || Loci.Any(locus => string.IsNullOrWhiteSpace(locus.Path.Value) || locus.Line < 1)
            || RenderedBytes is null)
            throw new InvalidDataException("repository access entry is malformed");
        string expectedCallSHA256 = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(Tool.ToolCall.Create(Verb, Argument).Raw)));
        if (!string.Equals(CallSHA256, expectedCallSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository access entry call digest does not match its canonical tool call");
    }

    internal static string ComputeEntrySHA256(in RepositoryAccessEntry entry)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, entry.Step.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, entry.CallSHA256); Append(hash, entry.Verb.ToString()); Append(hash, entry.Argument);
        Append(hash, entry.Paths.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (RepositoryPath path in entry.Paths) Append(hash, path.Value);
        Append(hash, entry.Loci.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (RepositoryLocus locus in entry.Loci)
        {
            Append(hash, locus.Path.Value);
            Append(hash, locus.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        hash.AppendData(entry.RenderedBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4]; BitConverter.TryWriteBytes(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }
}

/// Append-only ordered Merkle authority for the access journal.  The frontier
/// levels are a binary carry chain, so appending one row touches O(log n) nodes;
/// staged checkpoint roots copy only those levels and append the delta.
internal sealed class RepositoryAccessMerkleAuthority
{
    private static readonly string EmptyRoot = Digest(RepositoryOrderedMerkleMap.EncodeFields("access-empty-v1"));
    private readonly List<string?> _levels = new();
    private readonly List<string> _prefixRoots = [EmptyRoot];

    internal string RootHash => _prefixRoots[^1];

    internal void Clear()
    {
        _levels.Clear();
        _prefixRoots.Clear();
        _prefixRoots.Add(EmptyRoot);
    }

    internal void Append(in RepositoryAccessEntry entry)
    {
        string carry = Digest(RepositoryOrderedMerkleMap.EncodeFields(
            "access-leaf-v1", entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.EntrySHA256));
        int level = 0;
        while (true)
        {
            if (level == _levels.Count) _levels.Add(null);
            if (_levels[level] is null)
            {
                _levels[level] = carry;
                break;
            }
            carry = Digest(RepositoryOrderedMerkleMap.EncodeFields("access-node-v1", _levels[level]!, carry));
            _levels[level] = null;
            level++;
        }
        _prefixRoots.Add(ComputeRoot(_levels));
    }

    internal string RootAt(int count)
        => count >= 0 && count < _prefixRoots.Count
            ? _prefixRoots[count]
            : throw new InvalidDataException("repository access authority prefix is outside the journal");

    internal string RootAfterDelta(int count, IReadOnlyList<RepositoryAccessEntry> appended, int currentCount)
    {
        if (count < 0 || count > currentCount + appended.Count)
            throw new InvalidDataException("repository access authority count is outside the staged journal");
        if (count <= currentCount) return RootAt(count);
        List<string?> levels = new(_levels);
        for (int index = 0; index < count - currentCount; index++)
            AppendTo(levels, appended[index]);
        return ComputeRoot(levels);
    }

    private static void AppendTo(List<string?> levels, in RepositoryAccessEntry entry)
    {
        string carry = Digest(RepositoryOrderedMerkleMap.EncodeFields(
            "access-leaf-v1", entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.EntrySHA256));
        int level = 0;
        while (true)
        {
            if (level == levels.Count) levels.Add(null);
            if (levels[level] is null) { levels[level] = carry; return; }
            carry = Digest(RepositoryOrderedMerkleMap.EncodeFields("access-node-v1", levels[level]!, carry));
            levels[level] = null;
            level++;
        }
    }

    private static string ComputeRoot(IReadOnlyList<string?> levels)
    {
        string? root = null;
        for (int level = levels.Count - 1; level >= 0; level--)
            if (levels[level] is { } block)
                root = root is null ? block : Digest(RepositoryOrderedMerkleMap.EncodeFields("access-root-v1", block, root));
        return root ?? EmptyRoot;
    }

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// Durable evidence frontier for native repository observations. Entries are made
/// from the exact bytes returned by the admitted tool; the evaluator never opens
/// the repository or re-derives evidence from the filesystem.
public sealed class RepositoryAccessJournal
{
    private readonly List<RepositoryAccessEntry> _entries = new();
    private readonly RepositoryAccessMerkleAuthority _authority = new();
    private int _checkpointCursor;
    public IReadOnlyList<RepositoryAccessEntry> Entries => _entries;
    public int Count => _entries.Count;
    public long RenderedBytes => _entries.Sum(static entry => entry.RenderedByteCount);

    internal RepositoryAccessEntry[] CaptureCheckpointDelta()
        => _entries.GetRange(_checkpointCursor, _entries.Count - _checkpointCursor).ToArray();

    internal void ValidateCheckpointDelta(IReadOnlyList<RepositoryAccessEntry> entries)
    {
        int expectedSequence = _entries.Count;
        foreach (RepositoryAccessEntry entry in entries)
        {
            entry.Validate();
            if (entry.Sequence != expectedSequence++) throw new InvalidDataException("repository access checkpoint sequence diverged");
        }
    }

    internal readonly struct PreparedCheckpointDelta
    {
        internal PreparedCheckpointDelta(RepositoryAccessEntry[] entries) => Entries = entries;
        internal RepositoryAccessEntry[] Entries { get; }
    }

    internal PreparedCheckpointDelta PrepareCheckpointDelta(IReadOnlyList<RepositoryAccessEntry> entries)
    {
        ValidateCheckpointDelta(entries);
        return new(entries.ToArray());
    }

    internal void ApplyCheckpointDelta(IReadOnlyList<RepositoryAccessEntry> entries)
    {
        CommitPreparedCheckpointDelta(PrepareCheckpointDelta(entries));
    }

    internal void CommitPreparedCheckpointDelta(in PreparedCheckpointDelta prepared)
    {
        foreach (RepositoryAccessEntry entry in prepared.Entries)
        {
            _entries.Add(entry);
            _authority.Append(entry);
        }
    }

    internal void CommitCheckpointDelta() => _checkpointCursor = _entries.Count;

    internal static void WriteCheckpointDelta(CkptWriter writer, IReadOnlyList<RepositoryAccessEntry> entries)
    {
        writer.U8(1); writer.I32(entries.Count); foreach (RepositoryAccessEntry entry in entries) WriteEntry(writer, entry);
    }

    /// A DELTA is read against the sequence it CONTINUES, not against zero. The caller knows the
    /// journal's count after the delta applies (its checkpoint endpoint), so the base is that
    /// endpoint minus the rows carried here, and each row must read `base + i`. Checking `i` alone
    /// only ever held for the FIRST delta of a run; a later non-empty one refused itself, and that
    /// stayed hidden while a run happened to record no accesses after its first checkpoint.
    /// endpointCount < 0 keeps the from-zero reading for callers with no endpoint in hand.
    internal static RepositoryAccessEntry[] ReadCheckpointDelta(CkptReader reader, long endpointCount = -1)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown repository access checkpoint delta version");
        int count = reader.I32(); if (count < 0 || count > 1_000_000) throw new InvalidDataException("repository access checkpoint delta is malformed");
        long baseSequence = endpointCount < 0 ? 0 : endpointCount - count;
        if (baseSequence < 0)
            throw new InvalidDataException($"repository access checkpoint delta carries {count} rows, more than its endpoint of {endpointCount}");
        RepositoryAccessEntry[] entries = new RepositoryAccessEntry[count]; for (int i = 0; i < count; i++) entries[i] = ReadEntry(reader, baseSequence + i);
        return entries;
    }

    private static void WriteEntry(CkptWriter writer, RepositoryAccessEntry entry)
    {
        writer.I32(entry.Step); writer.I64(entry.Sequence); writer.Str(entry.CallSHA256); writer.U8((byte)entry.Verb); writer.Str(entry.Argument);
        writer.I32(entry.Paths.Length); foreach (RepositoryPath path in entry.Paths) writer.Str(path.Value);
        writer.I32(entry.Loci.Length); foreach (RepositoryLocus locus in entry.Loci) { writer.Str(locus.Path.Value); writer.I32(locus.Line); }
        writer.Bytes(entry.RenderedBytes);
    }

    private static RepositoryAccessEntry ReadEntry(CkptReader reader, long expectedSequence)
    {
        int step = reader.I32(); long sequence = reader.I64(); string call = reader.Str(); Tool.ToolVerbs verb = (Tool.ToolVerbs)reader.U8(); string argument = reader.Str();
        // ZERO paths is a legal entry, not a malformed one: the journal records ACCESSES, and a look
        // that rendered nothing is an access that happened. The decoders rejected it because the
        // encoder could never produce it.
        int pathsCount = reader.I32(); if (pathsCount < 0 || pathsCount > 4096) throw new InvalidDataException("repository access checkpoint paths are malformed");
        RepositoryPath[] paths = new RepositoryPath[pathsCount]; for (int i = 0; i < pathsCount; i++) paths[i] = reader.Str();
        int lociCount = reader.I32(); if (lociCount < 0 || lociCount > 1_000_000) throw new InvalidDataException("repository access checkpoint loci are malformed");
        RepositoryLocus[] loci = new RepositoryLocus[lociCount]; for (int i = 0; i < lociCount; i++) loci[i] = new(reader.Str(), reader.I32());
        RepositoryAccessEntry entry = new(step, sequence, call, verb, argument, paths, loci, reader.Bytes(1 << 20)); entry.Validate();
        if (entry.Sequence != expectedSequence)
            throw new InvalidDataException($"repository access checkpoint sequence is not contiguous: row carries {entry.Sequence}, the delta continues at {expectedSequence}");
        return entry;
    }

    public string AccessSHA256
    {
        get => _authority.RootHash;
    }

    internal string ComputeAccessSHA256(int count)
    {
        if (count < 0 || count > _entries.Count) throw new InvalidDataException("repository access count is outside the journal");
        return _authority.RootAt(count);
    }

    internal string ComputeAccessSHA256AfterDelta(int count, IReadOnlyList<RepositoryAccessEntry> appended)
    {
        return _authority.RootAfterDelta(count, appended, _entries.Count);
    }

    internal static string ComputeAccessSHA256(IReadOnlyList<RepositoryAccessEntry> entries)
    {
        RepositoryAccessMerkleAuthority authority = new();
        foreach (RepositoryAccessEntry entry in entries) authority.Append(entry);
        return authority.RootHash;
    }

    /// Record the access. THE JOURNAL IS OF ACCESSES, NOT OF FINDINGS — a look that rendered nothing
    /// still happened, still cost fuel, and its emptiness is itself a result the frontier may commit
    /// a transition against. Refusing to record it was the conflation that closed the loop's contract
    /// from both sides at once: the transition had to name a call, and the call it named could not be
    /// in a journal that only admitted looks which found something. Every consumer filters on
    /// `Paths.Any(...)`, so a barren row contributes to no occurrenceCheck and no evidence set — it is
    /// present only as the custody of an access that occurred.
    public void Record(int step, Tool.ToolCall call, Tool.Observation observation, string callSHA256)
    {
        byte[] rendered = Encoding.UTF8.GetBytes(observation.Text);
        var renderedPaths = new List<RepositoryPath>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (RepositoryPath path in observation.HitPaths)
            if (path.Length > 0 && observation.Text.Contains(path.Value, StringComparison.Ordinal) && seenPaths.Add(path.Value))
                renderedPaths.Add(path);

        RepositoryLocus[] loci = observation.Loci.Where(candidate => seenPaths.Contains(candidate.Path.Value)).ToArray();
        RepositoryAccessEntry entry = new(step, _entries.Count, callSHA256, call.Verb, call.Arg,
            renderedPaths.ToArray(), loci, rendered);
        _entries.Add(entry);
        _authority.Append(entry);
    }

    public RepositoryOccurrenceCheckResult Evaluate(Tool.RepositoryWorldSnapshot world, RepositoryPrediction prediction)
    {
        prediction.Validate();
        RepositoryOccurrenceCheckResult result = prediction.Species switch
        {
            RepositoryPredictionSpecies.PathExists => EvaluatePath(world, prediction),
            RepositoryPredictionSpecies.LocusContains => EvaluateLocus(world, prediction),
            RepositoryPredictionSpecies.SharedIdentifier => EvaluateShared(world, prediction),
            _ => throw new InvalidDataException("unknown repository prediction species"),
        };
        RepositoryAccessEntry[] evidence = prediction.Species == RepositoryPredictionSpecies.SharedIdentifier
            ? _entries.Where(entry => entry.Paths.Any(path => path.Value is var value && (value == prediction.Path || value == prediction.OtherPath))).ToArray()
            : _entries.Where(entry => entry.Paths.Any(path => path.Value == prediction.Path)).ToArray();
        RepositoryAccessEntry[] contributing = result.Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved
            ? Array.Empty<RepositoryAccessEntry>()
            : evidence.Length > 0 ? evidence : _entries.ToArray();
        RepositoryAccessEntry? joined = contributing.Length > 0 ? contributing[0] : null;
        return result with
        {
            EvidenceSHA256 = ComputeEvidenceSHA256(evidence),
            AccessSequence = joined?.Sequence ?? -1,
            AccessEntrySHA256 = joined?.EntrySHA256 ?? "",
            AccessEntryCount = _entries.Count,
        };
    }

    public void SaveState(CkptWriter writer)
    {
        writer.I32(_entries.Count);
        foreach (RepositoryAccessEntry entry in _entries)
        {
            writer.I32(entry.Step); writer.I64(entry.Sequence); writer.Str(entry.CallSHA256);
            writer.U8((byte)entry.Verb); writer.Str(entry.Argument); writer.I32(entry.Paths.Length);
            foreach (RepositoryPath path in entry.Paths) writer.Str(path.Value);
            writer.I32(entry.Loci.Length);
            foreach (RepositoryLocus locus in entry.Loci) { writer.Str(locus.Path.Value); writer.I32(locus.Line); }
            writer.Bytes(entry.RenderedBytes);
        }
    }

    public void LoadState(CkptReader reader)
    {
        if (_entries.Count != 0) throw new InvalidOperationException("repository access journal requires a fresh load");
        _authority.Clear();
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("repository access journal count is malformed");
        for (int i = 0; i < count; i++)
        {
            int step = reader.I32(); long sequence = reader.I64(); string call = reader.Str();
            Tool.ToolVerbs verb = (Tool.ToolVerbs)reader.U8(); string argument = reader.Str();
            int pathCount = reader.I32();
            if (pathCount < 0 || pathCount > 4096) throw new InvalidDataException("repository access path count is malformed");
            RepositoryPath[] paths = new RepositoryPath[pathCount];
            for (int p = 0; p < pathCount; p++) paths[p] = reader.Str();
            int locusCount = reader.I32();
            if (locusCount < 0 || locusCount > 1_000_000) throw new InvalidDataException("repository access locus count is malformed");
            RepositoryLocus[] loci = new RepositoryLocus[locusCount];
            for (int l = 0; l < locusCount; l++) loci[l] = new RepositoryLocus(reader.Str(), reader.I32());
            byte[] bytes = reader.Bytes(1 << 20);
            var entry = new RepositoryAccessEntry(step, sequence, call, verb, argument, paths, loci, bytes);
            entry.Validate();
            if (entry.Sequence != i) throw new InvalidDataException("repository access journal sequence is not contiguous");
            _entries.Add(entry);
            _authority.Append(entry);
        }
        CommitCheckpointDelta();
    }

    internal static RepositoryAccessJournal ReadState(CkptReader reader)
    {
        RepositoryAccessJournal journal = new();
        journal.LoadState(reader);
        return journal;
    }

    internal void ReplaceState(RepositoryAccessJournal source)
    {
        _entries.Clear();
        _authority.Clear();
        foreach (RepositoryAccessEntry entry in source._entries)
        {
            _entries.Add(entry);
            _authority.Append(entry);
        }
        _checkpointCursor = _entries.Count;
    }

    private RepositoryOccurrenceCheckResult EvaluatePath(Tool.RepositoryWorldSnapshot world, RepositoryPrediction prediction)
    {
        bool rendered = _entries.Any(entry => entry.Paths.Any(path => path.Value == prediction.Path));
        if (rendered && world.ContainsPath(prediction.Path))
            return RepositoryOccurrenceCheckResult.Confirmed(1, ByteCost(_entries.Where(entry => entry.Paths.Any(path => path.Value == prediction.Path))));
        bool completeListing = _entries.Any(entry => entry.Verb == Tool.ToolVerbs.Ls
            && entry.RenderedBytes.AsSpan().IndexOf("[…"u8) < 0
            && entry.RenderedBytes.AsSpan().IndexOf("ls ."u8) >= 0);
        if (rendered && !world.ContainsPath(prediction.Path)
            && _entries.Any(entry => entry.Paths.Any(path => path.Value == prediction.Path)
                && (entry.RenderedBytes.AsSpan().IndexOf("no file matching"u8) >= 0
                    || entry.RenderedBytes.AsSpan().IndexOf("no line"u8) >= 0)))
            return RepositoryOccurrenceCheckResult.Refuted(1, ByteCost(_entries.Where(entry => entry.Paths.Any(path => path.Value == prediction.Path))));
        if (completeListing && !world.ContainsPath(prediction.Path)) return RepositoryOccurrenceCheckResult.Refuted(1, ByteCost(_entries));
        return RepositoryOccurrenceCheckResult.Unobserved(0, ByteCost(_entries.Where(entry => entry.Paths.Any(path => path.Value == prediction.Path))));
    }

    private RepositoryOccurrenceCheckResult EvaluateLocus(Tool.RepositoryWorldSnapshot world, RepositoryPrediction prediction)
    {
        RepositoryAccessEntry[] entries = _entries.Where(entry => entry.Paths.Any(path => path.Value == prediction.Path)).ToArray();
        if (entries.Length == 0) return RepositoryOccurrenceCheckResult.Unobserved(0, 0);
        bool rendered = entries.Any(entry => entry.Loci.Any(locus => locus.Path.Value == prediction.Path && locus.Line == prediction.Line)
            || entry.RenderedBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes($":{prediction.Line}:")) >= 0
            || entry.RenderedBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes($"\n{prediction.Line}:")) >= 0);
        if (!rendered) return RepositoryOccurrenceCheckResult.Unobserved(0, ByteCost(entries));
        bool authority = world.ContainsLine(prediction.Path, prediction.Line, prediction.Value);
        return authority ? RepositoryOccurrenceCheckResult.Confirmed(1, ByteCost(entries)) : RepositoryOccurrenceCheckResult.Refuted(1, ByteCost(entries));
    }

    private RepositoryOccurrenceCheckResult EvaluateShared(Tool.RepositoryWorldSnapshot world, RepositoryPrediction prediction)
    {
        RepositoryAccessEntry[] evidence = _entries.Where(entry =>
            entry.Verb is (Tool.ToolVerbs.Grep or Tool.ToolVerbs.Open or Tool.ToolVerbs.Read)
            && entry.Paths.Any(path => path.Value == prediction.Path || path.Value == prediction.OtherPath)).ToArray();
        RepositoryAccessEntry[] left = evidence.Where(entry => entry.Paths.Any(path => path.Value == prediction.Path)).ToArray();
        RepositoryAccessEntry[] right = evidence.Where(entry => entry.Paths.Any(path => path.Value == prediction.OtherPath)).ToArray();
        long accessCost = ByteCost(evidence);
        if (left.Length == 0 || right.Length == 0) return RepositoryOccurrenceCheckResult.Unobserved(0, accessCost);
        byte[] needle = Encoding.UTF8.GetBytes(prediction.Value);
        bool leftRendered = left.Any(entry => entry.RenderedBytes.AsSpan().IndexOf(needle) >= 0);
        bool rightRendered = right.Any(entry => entry.RenderedBytes.AsSpan().IndexOf(needle) >= 0);
        if (!leftRendered || !rightRendered) return RepositoryOccurrenceCheckResult.Unobserved(2, accessCost);
        bool authority = world.ContainsText(prediction.Path, prediction.Value) && world.ContainsText(prediction.OtherPath, prediction.Value);
        return authority ? RepositoryOccurrenceCheckResult.Confirmed(2, accessCost) : RepositoryOccurrenceCheckResult.Refuted(2, accessCost);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> len = stackalloc byte[4]; BitConverter.TryWriteBytes(len, bytes.Length);
        hash.AppendData(len); hash.AppendData(bytes);
    }

    private static string ComputeEvidenceSHA256(IEnumerable<RepositoryAccessEntry> entries)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RepositoryAccessEntry entry in entries)
        {
            Append(hash, entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, entry.Paths.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (RepositoryPath path in entry.Paths) Append(hash, path.Value);
            Append(hash, entry.RenderedSHA256);
            Append(hash, entry.Loci.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (RepositoryLocus locus in entry.Loci)
            {
                Append(hash, locus.Path.Value);
                Append(hash, locus.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static long ByteCost(IEnumerable<RepositoryAccessEntry> entries)
        => entries.Sum(static entry => entry.RenderedByteCount);
}

public readonly record struct RepositoryOccurrenceCheckResult(
    RepositoryOccurrenceCheckOutcomes Outcome,
    long EvaluatorCost,
    long AccessCost,
    string EvidenceSHA256)
{
    public long AccessSequence { get; init; } = -1;
    public string AccessEntrySHA256 { get; init; } = "";
    public int AccessEntryCount { get; init; }

    public static RepositoryOccurrenceCheckResult Confirmed(long evaluatorCost, long accessCost)
        => Create(RepositoryOccurrenceCheckOutcomes.Confirmed, evaluatorCost, accessCost);
    public static RepositoryOccurrenceCheckResult Refuted(long evaluatorCost, long accessCost)
        => Create(RepositoryOccurrenceCheckOutcomes.Refuted, evaluatorCost, accessCost);
    public static RepositoryOccurrenceCheckResult Unobserved(long evaluatorCost, long accessCost)
        => Create(RepositoryOccurrenceCheckOutcomes.Unobserved, evaluatorCost, accessCost);

    private static RepositoryOccurrenceCheckResult Create(RepositoryOccurrenceCheckOutcomes outcome, long evaluatorCost, long accessCost)
    {
        string evidence = $"{outcome}\t{evaluatorCost}\t{accessCost}";
        return new(outcome, evaluatorCost, accessCost,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))));
    }
}

public readonly record struct RepositoryOccurrenceCheckReceipt(
    int Step,
    RepositoryPrediction Prediction,
    RepositoryOccurrenceCheckOutcomes Outcome,
    string WorldSHA256,
    string AccessSHA256,
    string PredictionSHA256,
    string EvidenceSHA256,
    long EvaluatorCost,
    long AccessCost,
    TapeEventID PredecessorEventID,
    string CallSHA256,
    string ReceiptSHA256)
{
    public long AccessSequence { get; init; } = -1;
    public string AccessEntrySHA256 { get; init; } = "";
    public int AccessEntryCount { get; init; }
    internal string Canonical => CanonicalFields(Step, Prediction, Outcome, WorldSHA256, AccessSHA256, AccessSequence,
        AccessEntrySHA256, AccessEntryCount, PredictionSHA256, EvidenceSHA256, EvaluatorCost, AccessCost, PredecessorEventID, CallSHA256);

    internal static RepositoryOccurrenceCheckReceipt Create(int step, RepositoryPrediction prediction,
        RepositoryOccurrenceCheckResult result, string worldSHA256, string accessSHA256,
        TapeEventID predecessorEventID, string callSHA256, long accessSequence, string accessEntrySHA256)
    {
        prediction.Validate();
        if (accessSequence != result.AccessSequence || !string.Equals(accessEntrySHA256, result.AccessEntrySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository occurrence check access entry authority diverges from evaluation");
        string predictionSHA = prediction.SHA256;
        string canonical = CanonicalFields(step, prediction, result.Outcome, worldSHA256, accessSHA256, accessSequence,
            accessEntrySHA256, result.AccessEntryCount, predictionSHA, result.EvidenceSHA256, result.EvaluatorCost, result.AccessCost, predecessorEventID, callSHA256);
        RepositoryOccurrenceCheckReceipt receipt = new(step, prediction, result.Outcome, worldSHA256, accessSHA256, predictionSHA,
            result.EvidenceSHA256, result.EvaluatorCost, result.AccessCost, predecessorEventID, callSHA256,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))))
        {
            AccessSequence = accessSequence,
            AccessEntrySHA256 = accessEntrySHA256,
            AccessEntryCount = result.AccessEntryCount,
        };
        receipt.Validate();
        return receipt;
    }

    public void Validate()
    {
        Prediction.Validate();
        if (Step < 0 || !Enum.IsDefined(Outcome) || !IsSHA(WorldSHA256) || !IsSHA(AccessSHA256)
            || !IsSHA(PredictionSHA256) || !IsSHA(EvidenceSHA256) || EvaluatorCost < 0 || AccessCost < 0
            || PredecessorEventID.Value < 0 || !IsSHA(CallSHA256) || AccessEntryCount < 0
            || Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved && (AccessSequence != -1 || AccessEntrySHA256.Length != 0)
            || Outcome != RepositoryOccurrenceCheckOutcomes.Unobserved && (AccessSequence < 0 || !IsSHA(AccessEntrySHA256))
            || !IsSHA(ReceiptSHA256))
            throw new InvalidDataException("repository occurrence check receipt is malformed");
        if (!string.Equals(PredictionSHA256, Prediction.SHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository occurrence check prediction digest diverges");
        string canonical = Canonical;
        string expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (!string.Equals(expected, ReceiptSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository occurrence check receipt digest diverges");
    }

    private static string CanonicalFields(int step, RepositoryPrediction prediction, RepositoryOccurrenceCheckOutcomes outcome,
        string worldSHA256, string accessSHA256, long accessSequence, string accessEntrySHA256, int accessEntryCount, string predictionSHA256,
        string evidenceSHA256, long evaluatorCost, long accessCost, TapeEventID predecessorEventID, string callSHA256)
        => $"{step}\t{prediction.Canonical}\t{outcome}\t{worldSHA256}\t{accessSHA256}\t{accessSequence}\t{accessEntrySHA256}\t{accessEntryCount}\t{predictionSHA256}\t{evidenceSHA256}\t{evaluatorCost}\t{accessCost}\t{predecessorEventID.Value}\t{callSHA256}";

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
