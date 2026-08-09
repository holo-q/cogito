namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Ronmamon;

/// The three report-level questions are deliberately distinct. A report may carry
/// a partial answer, but only the exact typed species can be admitted into the title.
public enum RepositoryLoopClosureVerdictSpecies : byte
{
    PatternBecameThought,
    ThoughtOverruledInstinct,
    ObjectLoopClosed,
}

public enum RepositoryLoopClosureTaskOutcomeSpecies : byte
{
    Confirmed,
    Refuted,
    Unobserved,
}

/// A world file is the authority, not a caller-asserted path/length/digest tuple.
/// The bytes are copied at the registration boundary and every later digest is
/// recomputed from this private copy.
public sealed class RepositoryLoopClosureWorldFile
{
    private readonly byte[] _bytes;
    private readonly int[] _lineStarts;
    private readonly int[] _lineLengths;

    public RepositoryLoopClosureWorldFile(Tool.RepositoryPath path, ReadOnlyMemory<byte> bytes)
    {
        if (string.IsNullOrWhiteSpace(path.Value)) throw new InvalidDataException("repository loop world file path is empty");
        Path = path;
        _bytes = bytes.ToArray();
        (_lineStarts, _lineLengths) = BuildLineBoundaries(_bytes);
        SHA256 = ComputeSHA256(_bytes);
    }

    public Tool.RepositoryPath Path { get; }
    public long Bytes => _bytes.LongLength;
    public string SHA256 { get; }
    public ReadOnlyMemory<byte> Content => _bytes;
    public Tool.RepositoryFile Authority => new(Path, Bytes, SHA256);
    public int LineCount => _lineStarts.Length;

    public bool TryGetLineBytes(int oneBasedLine, out ReadOnlyMemory<byte> line)
    {
        if (oneBasedLine < 1 || oneBasedLine > _lineStarts.Length)
        {
            line = default;
            return false;
        }
        line = new ReadOnlyMemory<byte>(_bytes, _lineStarts[oneBasedLine - 1], _lineLengths[oneBasedLine - 1]);
        return true;
    }

    public void Validate()
    {
        (int[] starts, int[] lengths) = BuildLineBoundaries(_bytes);
        if (string.IsNullOrWhiteSpace(Path.Value) || SHA256 != ComputeSHA256(_bytes)
            || !_lineStarts.AsSpan().SequenceEqual(starts) || !_lineLengths.AsSpan().SequenceEqual(lengths))
            throw new InvalidDataException("repository loop world file bytes diverge from authority");
    }

    private static (int[] Starts, int[] Lengths) BuildLineBoundaries(ReadOnlySpan<byte> bytes)
    {
        List<int> starts = new() { 0 };
        List<int> lengths = new();
        for (int index = 0; index < bytes.Length; index++)
            if (bytes[index] == (byte)'\n')
            {
                lengths.Add(index - starts[^1]);
                starts.Add(index + 1);
            }
        lengths.Add(bytes.Length - starts[^1]);
        return (starts.ToArray(), lengths.ToArray());
    }

    private static string ComputeSHA256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}

/// Immutable world authority supplied by the crawler. The bytes themselves have
/// already been admitted; this view carries the authenticated per-file copies.
public sealed class RepositoryLoopClosureWorldSnapshot
{
    public RepositoryLoopClosureWorldSnapshot(IReadOnlyList<RepositoryLoopClosureWorldFile> files)
    {
        Files = Array.AsReadOnly((files ?? throw new ArgumentNullException(nameof(files))).ToArray());
        if (Files.Count == 0 || Files.Any(static file => string.IsNullOrWhiteSpace(file.Path.Value)))
            throw new InvalidDataException("repository loop world snapshot is empty");
        WorldSHA256 = ComputeContentDigest(Files);
        SnapshotSHA256 = ComputeSnapshotDigest(Files.Select(static file => file.Authority).ToArray());
    }

    public IReadOnlyList<RepositoryLoopClosureWorldFile> Files { get; }
    public string WorldSHA256 { get; }
    /// Runtime identity: the same path/byte stream identity emitted by the live native world.
    public string ContentSHA256 => WorldSHA256;
    public string RuntimeContentSHA256 => ContentSHA256;
    /// Metadata identity: the sealed path/length/content-digest manifest projection.
    public string SnapshotSHA256 { get; }
    public string MetadataSnapshotSHA256 => SnapshotSHA256;

    public void Validate()
    {
        foreach (RepositoryLoopClosureWorldFile file in Files) file.Validate();
        if (Files.Select(static file => file.Path.Value).Distinct(StringComparer.Ordinal).Count() != Files.Count
            || Files.Any(static file => file.Bytes < 0 || file.SHA256 is not { Length: 64 } || !file.SHA256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("repository loop world snapshot file authority is malformed");
        if (WorldSHA256 != ComputeContentDigest(Files)
            || SnapshotSHA256 != ComputeSnapshotDigest(Files.Select(static file => file.Authority).ToArray()))
            throw new InvalidDataException("repository loop world identity diverges");
    }

    private static string ComputeSnapshotDigest(IReadOnlyList<Tool.RepositoryFile> files)
    {
        string canonical = string.Join('\n', files.OrderBy(static file => file.Path.Value, StringComparer.Ordinal)
            .Select(static file => $"{file.Path.Value}\t{file.Bytes}\t{file.SHA256}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + '\n')));
    }

    private static string ComputeContentDigest(IReadOnlyList<RepositoryLoopClosureWorldFile> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (RepositoryLoopClosureWorldFile file in files.OrderBy(static file => file.Path.Value, StringComparer.Ordinal))
        {
            byte[] path = Encoding.UTF8.GetBytes(file.Path.Value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, path.LongLength);
            hash.AppendData(length); hash.AppendData(path);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, file.Content.Length);
            hash.AppendData(length); hash.AppendData(file.Content.Span);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// A frozen journal view. The adjudicator receives rows and their already-sealed
/// digest; it never asks Journal to reopen its sink or reconstructs rows from disk.
public sealed class RepositoryLoopClosureJournalSnapshot
{
    public RepositoryLoopClosureJournalSnapshot(IReadOnlyList<string> lines, IReadOnlyList<JournalRowBinding> rows)
    {
        Lines = Array.AsReadOnly((lines ?? throw new ArgumentNullException(nameof(lines))).ToArray());
        Rows = Array.AsReadOnly((rows ?? throw new ArgumentNullException(nameof(rows))).ToArray());
        RowAuthorities = Array.AsReadOnly(Rows.Select(row => CreateRowAuthority(Lines, row)).ToArray());
        JournalSHA256 = LoopLineageVerifier.DigestJournal(Lines);
        RowAuthoritiesSHA256 = ComputeRowAuthoritiesSHA256(RowAuthorities);
    }

    public string JournalSHA256 { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<JournalRowBinding> Rows { get; }
    public IReadOnlyList<RepositoryLoopClosureJournalRowAuthority> RowAuthorities { get; }
    public string RowAuthoritiesSHA256 { get; }

    public void Validate()
    {
        RequireSHA(JournalSHA256, "journal");
        if (JournalSHA256 != LoopLineageVerifier.DigestJournal(Lines))
            throw new InvalidDataException("repository loop journal digest diverges");
        if (RowAuthorities.Count != Rows.Count || !IsSHA(RowAuthoritiesSHA256))
            throw new InvalidDataException("repository loop journal row authority shape is malformed");
        long previous = -1;
        HashSet<long> eventIDs = new();
        for (int rowIndex = 0; rowIndex < Rows.Count; rowIndex++)
        {
            JournalRowBinding row = Rows[rowIndex];
            if (row.LineIndex < 0 || row.LineIndex >= Lines.Count || row.Step < 0 || row.EventID.Value < 0 || string.IsNullOrWhiteSpace(row.Source)
                || !IsSHA(row.SHA256) || row.LineIndex <= previous || !eventIDs.Add(row.EventID.Value))
                throw new InvalidDataException("repository loop journal snapshot row is malformed");
            string lineSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Lines[row.LineIndex])));
            if (lineSHA256 != row.SHA256)
                throw new InvalidDataException("repository loop journal row does not bind to its sealed line");
            RepositoryLoopClosureJournalRowAuthority authority = RowAuthorities[rowIndex];
            if (authority.LineIndex != row.LineIndex || authority.Step != row.Step || authority.EventID != row.EventID
                || authority.Source != row.Source || authority.TypedSHA256 != ComputeTypedRowSHA256(authority.Step, authority.EventID, authority.Source))
                throw new InvalidDataException("repository loop journal row semantics diverge from authority");
            previous = row.LineIndex;
        }
        if (RowAuthoritiesSHA256 != ComputeRowAuthoritiesSHA256(RowAuthorities))
            throw new InvalidDataException("repository loop journal row authority digest diverges");
    }

    private static RepositoryLoopClosureJournalRowAuthority CreateRowAuthority(
        IReadOnlyList<string> lines,
        JournalRowBinding row)
    {
        if (row.LineIndex < 0 || row.LineIndex >= lines.Count
            || !TryParseCanonicalBindingLine(lines[row.LineIndex], out int step, out TapeEventID eventID, out string source))
            throw new InvalidDataException("repository loop journal row does not have a canonical event prefix");
        return new(row.LineIndex, step, eventID, source, ComputeTypedRowSHA256(step, eventID, source));
    }

    private static bool TryParseCanonicalBindingLine(
        string line,
        out int step,
        out TapeEventID eventID,
        out string source)
    {
        step = 0;
        eventID = default;
        source = string.Empty;
        string[] fields = line.Split('\t');
        if (fields.Length != 5 || fields[1] != "mint"
            || fields[2].Length < 2 || fields[2][0] != 's'
            || (fields[2].Length > 2 && fields[2][1] == '0')
            || fields[3].Length == 0 || fields[3].Contains('=')
            || fields[4].Length < 2 || fields[4][^1] != 'B'
            || !int.TryParse(fields[4].AsSpan(0, fields[4].Length - 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int byteCount)
            || byteCount < 0
            || !Journal.TryParseBindingRow(line, out step, out eventID, out source)
            || fields[2] != $"s{eventID.Value}")
            return false;
        return true;
    }

    private static string ComputeTypedRowSHA256(int step, TapeEventID eventID, string source)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "repository-journal-row-v1", step, eventID.Value, source))));

    private static string ComputeRowAuthoritiesSHA256(IReadOnlyList<RepositoryLoopClosureJournalRowAuthority> rows)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows.Select(static row =>
            $"{row.LineIndex}\t{row.Step}\t{row.EventID.Value}\t{row.Source}\t{row.TypedSHA256}")) + '\n')));

    private static void RequireSHA(string value, string name)
    {
        if (!IsSHA(value)) throw new InvalidDataException($"repository loop {name} snapshot digest is malformed");
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public readonly record struct RepositoryLoopClosureJournalRowAuthority(
    int LineIndex,
    int Step,
    TapeEventID EventID,
    string Source,
    string TypedSHA256);

/// Frozen authority identity. Files are manifest entries only; no path is opened by
/// the report or by the future adjudicator.
public sealed class RepositoryLoopClosureAuthoritySnapshot
{
    public RepositoryLoopClosureAuthoritySnapshot(
        RepositoryLoopClosureRegistration registration,
        IReadOnlyList<LoopClosureAuthorityBundleFile> files)
        : this((registration ?? throw new ArgumentNullException(nameof(registration))).Encode(), files,
            registration.RegistrationSHA256)
    {
        if (RegistrationSHA256 != registration.RegistrationSHA256)
            throw new InvalidDataException("repository loop registration bytes are not the typed registration authority");
    }

    public RepositoryLoopClosureAuthoritySnapshot(
        ReadOnlyMemory<byte> registrationBytes,
        IReadOnlyList<LoopClosureAuthorityBundleFile> files)
        : this(registrationBytes, files, null)
    {
    }

    private RepositoryLoopClosureAuthoritySnapshot(
        ReadOnlyMemory<byte> registrationBytes,
        IReadOnlyList<LoopClosureAuthorityBundleFile> files,
        string? semanticRegistrationSHA256)
    {
        _registrationBytes = registrationBytes.ToArray();
        Files = Array.AsReadOnly((files ?? throw new ArgumentNullException(nameof(files))).ToArray());
        RegistrationSHA256 = semanticRegistrationSHA256 ?? Convert.ToHexStringLower(SHA256.HashData(_registrationBytes));
        RegistrationDocumentSHA256 = Convert.ToHexStringLower(SHA256.HashData(_registrationBytes));
        AuthoritySHA256 = ComputeDigest(Files);
    }

    private readonly byte[] _registrationBytes;
    /// Digest of the manifest as a WHOLE. It is not the source-authority digest and can never
    /// equal it: the manifest lists the encoded registration, which already carries that digest.
    public string AuthoritySHA256 { get; }
    /// The bundle entry that carries the source-authority corroboration bytes. A registration's
    /// SourceAuthoritySHA256 is this entry's digest, which is what binds a run's world/query
    /// contract to the bundle it was sealed with.
    public string SourceAuthorityEntrySHA256 => Files
        .Where(static file => file.RelativePath == "source-authority.txt")
        .Select(static file => file.SHA256)
        .SingleOrDefault() ?? "";
    public string RegistrationSHA256 { get; }
    public string RegistrationDocumentSHA256 { get; }
    public ReadOnlyMemory<byte> RegistrationBytes => _registrationBytes;
    public IReadOnlyList<LoopClosureAuthorityBundleFile> Files { get; }

    public void Validate()
    {
        RequireSHA(AuthoritySHA256, "authority");
        RequireSHA(RegistrationSHA256, "registration");
        RequireSHA(RegistrationDocumentSHA256, "registration document");
        if (Files.Count == 0 || Files.Any(static file => !file.IsValid)
            || Files.Select(static file => file.RelativePath).Distinct(StringComparer.Ordinal).Count() != Files.Count)
            throw new InvalidDataException("repository loop authority snapshot manifest is malformed");
        if (RegistrationDocumentSHA256 != Convert.ToHexStringLower(SHA256.HashData(_registrationBytes))
            || AuthoritySHA256 != ComputeDigest(Files))
            throw new InvalidDataException("repository loop authority snapshot digest diverges");
    }

    private static void RequireSHA(string value, string name)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"repository loop {name} snapshot digest is malformed");
    }

    private static string ComputeDigest(IReadOnlyList<LoopClosureAuthorityBundleFile> files)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', files
            .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
            .Select(static file => $"{file.RelativePath}\t{file.SHA256}")) + '\n')));
}

public readonly record struct RepositoryLoopClosureAccessSource(
    long Sequence,
    string Path,
    long Bytes,
    string SHA256);

/// A proposal is selected against a particular frontier root, not retroactively
/// against the final frontier snapshot. The corroboration is the captured historical
/// root and ordinal that the action actually consumed.
public readonly record struct RepositoryLoopClosureFrontierSelectionCorroboration(
    RepositoryFrontierRevision Revision,
    string RuntimeAuthoritySHA256,
    long Ordinal,
    TapeEventID SelectionEventID,
    string SelectionReceiptSHA256,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical);

/// Access is carried as the exact admitted response records plus the authenticated
/// world file each path resolved against, not as a live world handle.
public sealed class RepositoryLoopClosureAccessSnapshot
{
    public RepositoryLoopClosureAccessSnapshot(
        IReadOnlyList<RepositoryAccessEntry> entries,
        RepositoryLoopClosureWorldSnapshot world)
    {
        Entries = Array.AsReadOnly((entries ?? throw new ArgumentNullException(nameof(entries))).Select(CloneEntry).ToArray());
        Sources = Array.AsReadOnly(BuildSources(Entries, world ?? throw new ArgumentNullException(nameof(world))));
        AccessSHA256 = ComputeDigest(Entries);
        SourcesSHA256 = ComputeSourcesDigest(Sources);
        JournalRoots = Array.AsReadOnly(BuildJournalRoots(Entries));
        _journalRoots = JournalRoots.ToHashSet(StringComparer.Ordinal);
    }

    private readonly HashSet<string> _journalRoots;

    /// The snapshot's own digest: it covers rendered bytes and loci, which the access journal's
    /// Merkle root does not. It is therefore NOT the digest any runtime receipt stamps.
    public string AccessSHA256 { get; }
    public string SourcesSHA256 { get; }
    public IReadOnlyList<RepositoryAccessEntry> Entries { get; }
    public IReadOnlyList<RepositoryLoopClosureAccessSource> Sources { get; }
    /// The access journal's Merkle root after each prefix, index k covering the first k entries.
    /// A frontier transition, pattern receipt, or task occurrenceCheck stamps the root AS OF the
    /// moment it was minted, so joining any of them to this snapshot means finding their stamp
    /// among these roots — never comparing it to AccessSHA256, which is a different formula.
    public IReadOnlyList<string> JournalRoots { get; }
    public string JournalAccessSHA256 => JournalRoots[^1];

    public bool CarriesJournalRoot(string digest) => _journalRoots.Contains(digest);

    private static string[] BuildJournalRoots(IReadOnlyList<RepositoryAccessEntry> entries)
    {
        RepositoryAccessMerkleAuthority authority = new();
        List<string> roots = [authority.RootHash];
        foreach (RepositoryAccessEntry entry in entries)
        {
            authority.Append(entry);
            roots.Add(authority.RootHash);
        }
        return roots.ToArray();
    }

    public void Validate()
    {
        RequireSHA(AccessSHA256, "access");
        long previousSequence = -1;
        foreach (RepositoryAccessEntry entry in Entries)
        {
            entry.Validate();
            if (entry.Sequence <= previousSequence)
                throw new InvalidDataException("repository loop access sequence is not monotone");
            string expectedCallSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Tool.ToolCall.Create(entry.Verb, entry.Argument).Raw)));
            if (entry.CallSHA256 != expectedCallSHA256)
                throw new InvalidDataException("repository loop access call digest does not match its canonical tool call");
            previousSequence = entry.Sequence;
        }
        if (AccessSHA256 != ComputeDigest(Entries)) throw new InvalidDataException("repository loop access digest diverges");
        RequireSHA(SourcesSHA256, "access source");
        if (Sources.Any(static source => source.Sequence < 0 || string.IsNullOrWhiteSpace(source.Path)
                || source.Bytes < 0 || source.SHA256 is not { Length: 64 } || !source.SHA256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("repository loop access source authority is malformed");
        Dictionary<(long Sequence, string Path), int> expectedSources = new();
        foreach (RepositoryAccessEntry entry in Entries)
            foreach (Tool.RepositoryPath path in entry.Paths)
                expectedSources[(entry.Sequence, path.Value)] = expectedSources.GetValueOrDefault((entry.Sequence, path.Value)) + 1;
        Dictionary<(long Sequence, string Path), int> actualSources = new();
        foreach (RepositoryLoopClosureAccessSource source in Sources)
            actualSources[(source.Sequence, source.Path)] = actualSources.GetValueOrDefault((source.Sequence, source.Path)) + 1;
        if (expectedSources.Count != actualSources.Count
            || expectedSources.Any(pair => !actualSources.TryGetValue(pair.Key, out int count) || count != pair.Value))
            throw new InvalidDataException("repository loop access sources are not bound to admitted paths");
        if (SourcesSHA256 != ComputeSourcesDigest(Sources)) throw new InvalidDataException("repository loop access source digest diverges");
    }

    private static void RequireSHA(string value, string name)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"repository loop {name} snapshot digest is malformed");
    }

    private static RepositoryAccessEntry CloneEntry(RepositoryAccessEntry entry)
        => entry with
        {
            Paths = entry.Paths.ToArray(),
            Loci = entry.Loci.ToArray(),
            RenderedBytes = entry.RenderedBytes.ToArray(),
        };

    private static string ComputeDigest(IReadOnlyList<RepositoryAccessEntry> entries)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RepositoryAccessEntry entry in entries)
        {
            Append(hash, entry.Step.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, entry.CallSHA256); Append(hash, entry.Verb.ToString()); Append(hash, entry.Argument);
            Append(hash, entry.Paths.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (Tool.RepositoryPath path in entry.Paths) Append(hash, path.Value);
            Append(hash, entry.Loci.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (Tool.RepositoryLocus locus in entry.Loci)
            { Append(hash, locus.Path.Value); Append(hash, locus.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            hash.AppendData(entry.RenderedBytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static RepositoryLoopClosureAccessSource[] BuildSources(
        IReadOnlyList<RepositoryAccessEntry> entries,
        RepositoryLoopClosureWorldSnapshot world)
    {
        Dictionary<string, RepositoryLoopClosureWorldFile> files = world.Files.ToDictionary(
            static file => file.Path.Value, StringComparer.Ordinal);
        List<RepositoryLoopClosureAccessSource> sources = new();
        foreach (RepositoryAccessEntry entry in entries)
            foreach (Tool.RepositoryPath path in entry.Paths)
            {
                if (!files.TryGetValue(path.Value, out RepositoryLoopClosureWorldFile? file))
                    throw new InvalidDataException("repository access path is outside the registered world");
                sources.Add(new(entry.Sequence, path.Value, file.Bytes, file.SHA256));
            }
        return sources.ToArray();
    }

    private static string ComputeSourcesDigest(IReadOnlyList<RepositoryLoopClosureAccessSource> sources)
    {
        string canonical = string.Join('\n', sources.Select(static source =>
            $"{source.Sequence}\t{source.Path}\t{source.Bytes}\t{source.SHA256}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + '\n')));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4]; BitConverter.TryWriteBytes(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }
}

/// Frozen frontier state used by the report. Candidate transitions are copied so
/// adjudication cannot observe later policy mutations.
public sealed class RepositoryLoopClosureFrontierSnapshot
{
    public RepositoryLoopClosureFrontierSnapshot(
        RepositoryFrontierRevision revision,
        IReadOnlyList<RepositoryCandidate> candidates,
        IReadOnlyList<RepositoryCandidateTransition> transitions,
        IReadOnlyList<string> observedPaths,
        string runtimeAuthoritySHA256,
        IReadOnlyList<RepositoryLoopClosureFrontierSelectionCorroboration>? selections = null)
    {
        Revision = revision;
        Candidates = Array.AsReadOnly((candidates ?? throw new ArgumentNullException(nameof(candidates))).ToArray());
        Transitions = Array.AsReadOnly((transitions ?? throw new ArgumentNullException(nameof(transitions)))
            .Select(static transition => transition with { PatternOrigin = CloneOrigin(transition.PatternOrigin) }).ToArray());
        ObservedPaths = Array.AsReadOnly((observedPaths ?? throw new ArgumentNullException(nameof(observedPaths)))
            .Order(StringComparer.Ordinal).ToArray());
        RuntimeAuthoritySHA256 = runtimeAuthoritySHA256;
        Selections = Array.AsReadOnly((selections ?? Array.Empty<RepositoryLoopClosureFrontierSelectionCorroboration>()).ToArray());
        FrontierSHA256 = ComputeDigest(Revision, Candidates, Transitions, ObservedPaths, Selections);
    }

    public RepositoryFrontierRevision Revision { get; }
    public string FrontierSHA256 { get; }
    public string SnapshotSHA256 => FrontierSHA256;
    /// The authority root emitted by the live frontier, including observed paths and transitions.
    public string RuntimeAuthoritySHA256 { get; }
    public IReadOnlyList<RepositoryCandidate> Candidates { get; }
    public IReadOnlyList<RepositoryCandidateTransition> Transitions { get; }
    public IReadOnlyList<string> ObservedPaths { get; }
    public IReadOnlyList<RepositoryLoopClosureFrontierSelectionCorroboration> Selections { get; }

    public void Validate()
    {
        if (!Revision.IsValid) throw new InvalidDataException("repository loop frontier revision is malformed");
        if (FrontierSHA256 is not { Length: 64 } || !FrontierSHA256.All(Uri.IsHexDigit))
            throw new InvalidDataException("repository loop frontier snapshot digest is malformed");
        if (RuntimeAuthoritySHA256 is not { Length: 64 } || !RuntimeAuthoritySHA256.All(Uri.IsHexDigit)
            || ObservedPaths.Any(string.IsNullOrWhiteSpace)
            || ObservedPaths.Distinct(StringComparer.Ordinal).Count() != ObservedPaths.Count)
            throw new InvalidDataException("repository loop frontier runtime authority is malformed");
        if (RuntimeAuthoritySHA256 != RepositoryCandidateFrontier.ComputeAuthoritySHA256(Transitions, ObservedPaths))
            throw new InvalidDataException("repository loop frontier runtime authority diverges");
        foreach (RepositoryCandidate candidate in Candidates)
        {
            if (!Enum.IsDefined(candidate.Species) || !candidate.Digest.IsValid || candidate.Canonical.Length == 0)
                throw new InvalidDataException("repository loop frontier candidate is malformed");
            if (!RepositoryCandidate.TryParseCanonical(candidate.Canonical, out RepositoryCandidate parsed)
                || parsed.Digest != candidate.Digest || parsed.Species != candidate.Species)
                throw new InvalidDataException("repository loop frontier candidate canonical diverges");
        }
        foreach (RepositoryCandidateTransition transition in Transitions)
        {
            if (!transition.CandidateDigest.IsValid || string.IsNullOrWhiteSpace(transition.CandidateCanonical)
                || !Enum.IsDefined(transition.State) || transition.Attempts < 0
                || transition.SourceEventID.Value < 0 || transition.PredecessorEventID.Value < 0
                || !IsOptionalSHA(transition.CallSHA256) || !IsOptionalSHA(transition.AccessSHA256))
                throw new InvalidDataException("repository loop frontier transition is malformed");
            if (!RepositoryCandidate.TryParseCanonical(transition.CandidateCanonical, out RepositoryCandidate parsed)
                || parsed.Digest != transition.CandidateDigest)
                throw new InvalidDataException("repository loop frontier transition candidate canonical diverges");
            transition.PatternOrigin?.Validate();
        }
        foreach (RepositoryLoopClosureFrontierSelectionCorroboration selection in Selections)
        {
            if (!selection.Revision.IsValid || !IsSHA(selection.RuntimeAuthoritySHA256)
                || selection.Ordinal < 0 || selection.SelectionEventID.Value <= 0 || !IsSHA(selection.SelectionReceiptSHA256)
                || !selection.CandidateDigest.IsValid || string.IsNullOrWhiteSpace(selection.CandidateCanonical))
                throw new InvalidDataException("repository frontier selection corroboration is malformed");
            if (!RepositoryCandidate.TryParseCanonical(selection.CandidateCanonical, out RepositoryCandidate parsed)
                || parsed.Digest != selection.CandidateDigest)
                throw new InvalidDataException("repository frontier selection corroboration candidate diverges");
        }
        HashSet<(RepositoryCandidateDigest Digest, string Canonical)> candidateIdentities = Candidates
            .Select(static candidate => (candidate.Digest, candidate.Canonical)).ToHashSet();
        if (candidateIdentities.Count != Candidates.Count
            || Transitions.Any(transition => !candidateIdentities.Contains((transition.CandidateDigest, transition.CandidateCanonical))))
            throw new InvalidDataException("repository loop frontier transition has no candidate authority");
        // A corroboration is identified by its tape event; ordinals are RANKS WITHIN a frontier revision,
        // and a crawler that keeps taking the top-ranked candidate legitimately selects ordinal 0
        // over and over. Demanding globally distinct ordinals declared that ordinary behaviour
        // malformed. What must not repeat is a revision selecting twice.
        if (Selections.Select(static selection => selection.SelectionEventID.Value).Distinct().Count() != Selections.Count)
            throw new InvalidDataException("repository frontier selection corroboration event repeats");
        if (Selections.Select(static selection => (selection.Revision.Value, selection.Ordinal)).Distinct().Count() != Selections.Count)
            throw new InvalidDataException("repository frontier revision selected the same ordinal twice");
        if (Selections.Any(selection => !candidateIdentities.Contains((selection.CandidateDigest, selection.CandidateCanonical))
                || !Transitions.Any(transition => transition.CandidateDigest == selection.CandidateDigest
                    && transition.CandidateCanonical == selection.CandidateCanonical)))
            throw new InvalidDataException("repository frontier selection is not closed over candidate transitions");
        Dictionary<(RepositoryCandidateDigest Digest, string Canonical), RepositoryCandidate> candidatesByIdentity = Candidates
            .ToDictionary(static candidate => (candidate.Digest, candidate.Canonical));
        foreach (RepositoryCandidateTransition transition in Transitions.Where(static transition => transition.State == RepositoryCandidateStates.Committed))
        {
            if (!IsSHA(transition.CallSHA256) || !IsSHA(transition.AccessSHA256)
                || !candidatesByIdentity.TryGetValue((transition.CandidateDigest, transition.CandidateCanonical), out RepositoryCandidate? candidate))
                throw new InvalidDataException("repository committed transition omits its call or access authority");
            string expectedCallSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)));
            if (transition.CallSHA256 != expectedCallSHA256)
                throw new InvalidDataException("repository committed transition call authority diverges");
        }
        if (FrontierSHA256 != ComputeDigest(Revision, Candidates, Transitions, ObservedPaths, Selections))
            throw new InvalidDataException("repository loop frontier digest diverges");
    }

    private static bool IsOptionalSHA(string value)
        => string.IsNullOrEmpty(value) || value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static RepositoryPatternCandidateOrigin? CloneOrigin(RepositoryPatternCandidateOrigin? origin)
    {
        if (origin is not { } value) return null;
        RepositoryPatternOccurrenceSet occurrence = RepositoryPatternOccurrenceSet.Create(value.OccurrenceSet.Occurrences.ToArray());
        RepositoryComposedCandidateReceipt receipt = value.Receipt with
        {
            OccurrenceReceiptEventIDs = value.Receipt.OccurrenceReceiptEventIDs.ToArray(),
        };
        return new RepositoryPatternCandidateOrigin(value.RuleID, occurrence, receipt);
    }

    private static string ComputeDigest(
        RepositoryFrontierRevision revision,
        IReadOnlyList<RepositoryCandidate> candidates,
        IReadOnlyList<RepositoryCandidateTransition> transitions,
        IReadOnlyList<string> observedPaths,
        IReadOnlyList<RepositoryLoopClosureFrontierSelectionCorroboration> selections)
    {
        string canonical = string.Join('\n', candidates.OrderBy(static candidate => candidate.Digest.Value)
            .Select(static candidate => $"candidate\t{candidate.Species}\t{candidate.Canonical}\t{candidate.Verb}\t{candidate.Argument}")
            .Concat(transitions.OrderBy(static item => item.CandidateDigest.Value)
            .Select(static item => string.Join('\t', item.CandidateDigest.Value, item.CandidateCanonical, item.State, item.Attempts,
                item.SourceEventID.Value, item.PredecessorEventID.Value, item.CallSHA256, item.AccessSHA256,
                item.VerifierOutcome?.ToString() ?? "", item.PatternOrigin?.Receipt.ReceiptSHA256 ?? ""))
            .Concat(observedPaths.Order(StringComparer.Ordinal).Select(static path => "observed\t" + path))
            .Concat(selections.OrderBy(static selection => selection.Ordinal).Select(static selection =>
                $"selection\t{selection.Revision.Value}\t{selection.RuntimeAuthoritySHA256}\t{selection.Ordinal}\t{selection.SelectionEventID.Value}\t{selection.SelectionReceiptSHA256}\t{selection.CandidateDigest.Value}\t{selection.CandidateCanonical}"))));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"repository-loop-frontier-v2\n{revision.Value}\n{canonical}\n")));
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// Frozen repository pattern state. Compositions are already admitted objects; this
/// type has no world reference and therefore cannot perform a second composition.
public sealed class RepositoryLoopClosurePatternSnapshot
{
    public RepositoryLoopClosurePatternSnapshot(
        RepositoryNavigationRule rule,
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<RepositoryCandidateDigest> pendingAdmissionDigests,
        string pendingAuthoritySHA256,
        IReadOnlyList<string>? pendingAdmissionCanonicals = null)
    {
        Rule = rule;
        Occurrences = Array.AsReadOnly((occurrences ?? throw new ArgumentNullException(nameof(occurrences))).ToArray());
        Compositions = Array.AsReadOnly((compositions ?? throw new ArgumentNullException(nameof(compositions)))
            .Select(CloneComposition).ToArray());
        Admissions = Array.AsReadOnly((admissions ?? throw new ArgumentNullException(nameof(admissions))).ToArray());
        PendingAdmissionDigests = Array.AsReadOnly((pendingAdmissionDigests ?? throw new ArgumentNullException(nameof(pendingAdmissionDigests))).ToArray());
        PendingAdmissionCanonicals = Array.AsReadOnly((pendingAdmissionCanonicals ?? Array.Empty<string>()).ToArray());
        PendingAuthoritySHA256 = pendingAuthoritySHA256;
        PatternSHA256 = ComputeDigest(Rule, Occurrences, Compositions, Admissions, PendingAdmissionDigests,
            PendingAdmissionCanonicals, PendingAuthoritySHA256);
    }

    public RepositoryNavigationRule Rule { get; }
    public string PatternSHA256 { get; }
    public IReadOnlyList<RepositoryPatternOccurrence> Occurrences { get; }
    public IReadOnlyList<RepositoryPatternComposition> Compositions { get; }
    public IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> Admissions { get; }
    public IReadOnlyList<RepositoryCandidateDigest> PendingAdmissionDigests { get; }
    public IReadOnlyList<string> PendingAdmissionCanonicals { get; }
    public string PendingAuthoritySHA256 { get; }

    public void Validate()
    {
        Rule.Validate();
        if (PatternSHA256 is not { Length: 64 } || !PatternSHA256.All(Uri.IsHexDigit))
            throw new InvalidDataException("repository loop pattern snapshot digest is malformed");
        foreach (RepositoryPatternOccurrence occurrence in Occurrences) occurrence.Validate();
        foreach (RepositoryPatternComposition composition in Compositions) composition.Validate();
        Dictionary<(RepositoryCandidateDigest Digest, string Canonical), RepositoryPatternComposition> compositionsByCandidate = Compositions
            .ToDictionary(static composition => (composition.Conclusion.CandidateDigest, composition.Conclusion.Candidate.Canonical));
        HashSet<(RepositoryCandidateDigest Digest, string Canonical)> admissionKeys = new();
        foreach (RepositoryPatternGrammarAdmissionReceipt admission in Admissions)
        {
            if (!admissionKeys.Add((admission.CandidateDigest, admission.CandidateCanonical)))
                throw new InvalidDataException("repository loop pattern admission identity repeats");
            if (!compositionsByCandidate.TryGetValue((admission.CandidateDigest, admission.CandidateCanonical), out RepositoryPatternComposition composition))
                throw new InvalidDataException("repository loop pattern admission has no composition authority");
            admission.Validate(composition);
        }
        if (PendingAdmissionDigests.Any(static digest => !digest.IsValid)
            || PendingAdmissionDigests.Distinct().Count() != PendingAdmissionDigests.Count
            || PendingAdmissionCanonicals.Count != PendingAdmissionDigests.Count
            || PendingAdmissionCanonicals.Any(string.IsNullOrWhiteSpace)
            || PendingAdmissionCanonicals.Distinct(StringComparer.Ordinal).Count() != PendingAdmissionCanonicals.Count
            || PendingAuthoritySHA256 is not { Length: 64 } || !PendingAuthoritySHA256.All(Uri.IsHexDigit))
            throw new InvalidDataException("repository loop pattern pending authority is malformed");
        RepositoryOrderedMerkleMap pendingAuthority = new();
        for (int index = 0; index < PendingAdmissionDigests.Count; index++)
        {
            if (!RepositoryCandidate.TryParseCanonical(PendingAdmissionCanonicals[index], out RepositoryCandidate candidate)
                || candidate.Digest != PendingAdmissionDigests[index]
                || !Admissions.Any(admission => admission.CandidateDigest == PendingAdmissionDigests[index]
                    && admission.CandidateCanonical == PendingAdmissionCanonicals[index]
                    && admission.MaterializationAdmitted && admission.ConsumedRevision is null))
                throw new InvalidDataException("repository loop pattern pending canonical diverges");
            RepositoryPatternPendingAdmission pending = new(PendingAdmissionDigests[index], PendingAdmissionCanonicals[index]);
            pendingAuthority.Set(pending.Digest.Value.ToString("X16") + "\u0000" + pending.Canonical, pending.Canonical);
        }
        if (PendingAuthoritySHA256 != pendingAuthority.RootHash)
            throw new InvalidDataException("repository loop pattern pending authority diverges");
        if (PatternSHA256 != ComputeDigest(Rule, Occurrences, Compositions, Admissions, PendingAdmissionDigests,
            PendingAdmissionCanonicals, PendingAuthoritySHA256))
            throw new InvalidDataException("repository loop pattern digest diverges");
    }

    private static string ComputeDigest(RepositoryNavigationRule rule,
        IReadOnlyList<RepositoryPatternOccurrence> occurrences,
        IReadOnlyList<RepositoryPatternComposition> compositions,
        IReadOnlyList<RepositoryPatternGrammarAdmissionReceipt> admissions,
        IReadOnlyList<RepositoryCandidateDigest> pendingAdmissionDigests,
        IReadOnlyList<string> pendingAdmissionCanonicals,
        string pendingAuthoritySHA256)
    {
        string canonical = string.Join('\n', new[] { rule.ID.Value }
            .Concat(occurrences.OrderBy(static occurrence => occurrence.OccurrenceCheckReceiptEventID.Value).Select(static occurrence => occurrence.EvidenceSHA256))
            .Concat(compositions.OrderBy(static composition => composition.Receipt.CompositionEventID.Value).Select(static composition => composition.Receipt.ReceiptSHA256))
            .Concat(admissions.OrderBy(static admission => admission.CandidateDigest.Value).Select(static admission => admission.Digest))
            .Concat(pendingAdmissionDigests.OrderBy(static digest => digest.Value).Select(static digest => digest.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Concat(pendingAdmissionCanonicals.Order(StringComparer.Ordinal))
            .Append(pendingAuthoritySHA256));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + '\n')));
    }

    private static RepositoryPatternComposition CloneComposition(RepositoryPatternComposition composition)
    {
        RepositoryComposedCandidateReceipt receipt = composition.Receipt with
        {
            OccurrenceReceiptEventIDs = composition.Receipt.OccurrenceReceiptEventIDs.ToArray(),
        };
        RepositoryPatternOccurrenceSet occurrence = RepositoryPatternOccurrenceSet.Create(
            composition.Conclusion.OccurrenceSet.Occurrences.ToArray());
        RepositoryPatternCandidateConclusion conclusion = composition.Conclusion with { OccurrenceSet = occurrence };
        return new RepositoryPatternComposition(conclusion, receipt);
    }
}

public readonly record struct RepositoryLoopClosureTapeSeal(
    TapeEventID EventID,
    string PayloadSHA256,
    string ReceiptSHA256,
    string PreSealTapeSHA256,
    string Source,
    Provenances Provenance,
    TapeEventRoles Roles)
{
    /// Digest of the immutable runtime authorities captured before this event.
    /// It deliberately excludes the final tape/journal seal so the event cannot
    /// become its own authority input.
    public string ImmutableAuthoritySHA256 { get; init; }

    public void Validate(IReadOnlyList<LoopLineageTapeEvent> events, string preSealTapeSHA256)
    {
        if (EventID.Value < 0 || !IsSHA(PayloadSHA256) || !IsSHA(ReceiptSHA256) || !IsSHA(PreSealTapeSHA256)
            || !IsSHA(ImmutableAuthoritySHA256) || PreSealTapeSHA256 != preSealTapeSHA256
            || Source != "repository-seal" || Provenance != Provenances.Execution || Roles != TapeEventRoles.AuditOnly)
            throw new InvalidDataException("repository loop tape seal is malformed");
        int index = -1;
        for (int i = 0; i < events.Count; i++)
            if (events[i].EventID == EventID)
            {
                if (index >= 0) throw new InvalidDataException("repository loop tape seal event repeats");
                index = i;
                if (Convert.ToHexStringLower(SHA256.HashData(events[i].Payload.Span)) != PayloadSHA256)
                    throw new InvalidDataException("repository loop tape seal payload diverges");
            }
        if (index < 0 || index != events.Count - 1 || events[index].Source != Source
            || events[index].Provenance != Provenance || events[index].Roles != Roles)
            throw new InvalidDataException("repository loop tape seal is not the terminal tape event");
        string canonical = string.Join('|', "repository-loop-seal-v3", EventID.Value,
            PreSealTapeSHA256, ImmutableAuthoritySHA256, PayloadSHA256, Source, Provenance, Roles);
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (expected != ReceiptSHA256) throw new InvalidDataException("repository loop tape seal receipt diverges");
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class RepositoryLoopClosureLineageNullSpec
{
    public RepositoryLoopClosureLineageNullSpec(string domain, string algorithm, string digest)
    {
        Domain = domain;
        Algorithm = algorithm;
        Digest = digest;
    }

    /// Mint a spec whose digest is COMPUTED here rather than restated by the caller —
    /// a registration that carried its own arithmetic could not be refuted by Validate.
    public static RepositoryLoopClosureLineageNullSpec Create(string domain, string algorithm)
    {
        RepositoryLoopClosureLineageNullSpec spec = new(domain, algorithm, "");
        RepositoryLoopClosureLineageNullSpec sealedSpec = new(domain, algorithm, spec.ComputeDigest());
        sealedSpec.Validate();
        return sealedSpec;
    }

    public string Domain { get; }
    public string Algorithm { get; }
    public string Digest { get; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Domain) || string.IsNullOrWhiteSpace(Algorithm) || !IsSHA(Digest)
            || Digest != ComputeDigest())
            throw new InvalidDataException("repository loop lineage null spec is malformed");
    }

    private string ComputeDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('|', "repository-loop-lineage-null-spec-v1", Domain, Algorithm))));

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// Post-run custody authority. Registration cannot contain these values because
/// they do not exist until the tape/world/access/frontier/pattern are sealed.
public sealed class RepositoryLoopClosureSealedEvidenceAuthority
{
    public RepositoryLoopClosureSealedEvidenceAuthority(
        string registrationSHA256,
        string bundleAuthoritySHA256,
        string tapeSHA256,
        string preSealTapeSHA256,
        string lineageAuthoritySHA256,
        string journalSHA256,
        string journalRowAuthoritiesSHA256,
        string worldContentSHA256,
        string worldSnapshotSHA256,
        string accessSHA256,
        string frontierSHA256,
        string frontierRuntimeAuthoritySHA256,
        string patternSHA256,
        string patternPendingAuthoritySHA256,
        RepositoryLoopClosureTapeSeal seal)
    {
        RegistrationSHA256 = registrationSHA256;
        BundleAuthoritySHA256 = bundleAuthoritySHA256;
        TapeSHA256 = tapeSHA256;
        PreSealTapeSHA256 = preSealTapeSHA256;
        LineageAuthoritySHA256 = lineageAuthoritySHA256;
        JournalSHA256 = journalSHA256;
        JournalRowAuthoritiesSHA256 = journalRowAuthoritiesSHA256;
        WorldContentSHA256 = worldContentSHA256;
        WorldSnapshotSHA256 = worldSnapshotSHA256;
        AccessSHA256 = accessSHA256;
        FrontierSHA256 = frontierSHA256;
        FrontierRuntimeAuthoritySHA256 = frontierRuntimeAuthoritySHA256;
        PatternSHA256 = patternSHA256;
        PatternPendingAuthoritySHA256 = patternPendingAuthoritySHA256;
        Seal = seal;
        AuthoritySHA256 = ComputeAuthoritySHA256();
    }

    public string RegistrationSHA256 { get; }
    public string BundleAuthoritySHA256 { get; }
    public string TapeSHA256 { get; }
    public string PreSealTapeSHA256 { get; }
    public string LineageAuthoritySHA256 { get; }
    public string JournalSHA256 { get; }
    public string JournalRowAuthoritiesSHA256 { get; }
    public string WorldContentSHA256 { get; }
    public string WorldSnapshotSHA256 { get; }
    public string AccessSHA256 { get; }
    public string FrontierSHA256 { get; }
    public string FrontierRuntimeAuthoritySHA256 { get; }
    public string PatternSHA256 { get; }
    public string PatternPendingAuthoritySHA256 { get; }
    public RepositoryLoopClosureTapeSeal Seal { get; }
    public string AuthoritySHA256 { get; }

    public void Validate(RepositoryLoopClosureAdjudicationInput input)
    {
        if (!IsSHA(RegistrationSHA256) || !IsSHA(BundleAuthoritySHA256) || !IsSHA(TapeSHA256) || !IsSHA(PreSealTapeSHA256)
            || !IsSHA(LineageAuthoritySHA256) || !IsSHA(JournalSHA256) || !IsSHA(JournalRowAuthoritiesSHA256)
            || !IsSHA(WorldContentSHA256) || !IsSHA(WorldSnapshotSHA256) || !IsSHA(AccessSHA256)
            || !IsSHA(FrontierSHA256) || !IsSHA(FrontierRuntimeAuthoritySHA256) || !IsSHA(PatternSHA256)
            || !IsSHA(PatternPendingAuthoritySHA256) || !IsSHA(AuthoritySHA256)
            || AuthoritySHA256 != ComputeAuthoritySHA256()
            || RegistrationSHA256 != input.Registration.RegistrationSHA256
            || BundleAuthoritySHA256 != input.Authority.AuthoritySHA256
            || TapeSHA256 != input.Tape.TapeSHA256
            || PreSealTapeSHA256 != input.Tape.PreSealTapeSHA256
            || LineageAuthoritySHA256 != LoopLineageAuthority.Capture(input.Tape.LineageEdges).Digest
            || JournalSHA256 != input.Journal.JournalSHA256
            || JournalRowAuthoritiesSHA256 != input.Journal.RowAuthoritiesSHA256
            || WorldContentSHA256 != input.World.ContentSHA256
            || WorldSnapshotSHA256 != input.World.SnapshotSHA256
            || AccessSHA256 != input.Access.AccessSHA256
            || FrontierSHA256 != input.Frontier.FrontierSHA256
            || FrontierRuntimeAuthoritySHA256 != input.Frontier.RuntimeAuthoritySHA256
            || PatternSHA256 != input.Pattern.PatternSHA256
            || PatternPendingAuthoritySHA256 != input.Pattern.PendingAuthoritySHA256
            || Seal.EventID != input.Tape.Seal.EventID || Seal.PayloadSHA256 != input.Tape.Seal.PayloadSHA256
            || Seal.PreSealTapeSHA256 != input.Tape.Seal.PreSealTapeSHA256
            || Seal.ImmutableAuthoritySHA256 != input.Tape.Seal.ImmutableAuthoritySHA256
            || Seal.ReceiptSHA256 != input.Tape.Seal.ReceiptSHA256 || Seal.Source != input.Tape.Seal.Source
            || Seal.Provenance != input.Tape.Seal.Provenance || Seal.Roles != input.Tape.Seal.Roles)
            throw new InvalidDataException("repository loop sealed evidence authority diverges");
    }

    private string ComputeAuthoritySHA256()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "repository-loop-sealed-evidence-v2", RegistrationSHA256, BundleAuthoritySHA256, TapeSHA256, PreSealTapeSHA256,
            LineageAuthoritySHA256, JournalSHA256, JournalRowAuthoritiesSHA256, WorldContentSHA256,
            WorldSnapshotSHA256, AccessSHA256, FrontierSHA256, FrontierRuntimeAuthoritySHA256,
            PatternSHA256, PatternPendingAuthoritySHA256, Seal.EventID.Value, Seal.PreSealTapeSHA256,
            Seal.ImmutableAuthoritySHA256, Seal.PayloadSHA256,
            Seal.ReceiptSHA256, Seal.Source, Seal.Provenance, Seal.Roles))));

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// Typed runtime authorities carry the canonical bytes that produce their digest;
/// a caller cannot merely restate a digest string and call it a corroboration.
public abstract class RepositoryLoopClosureCanonicalAuthorityCorroboration
{
    protected RepositoryLoopClosureCanonicalAuthorityCorroboration(string domain, ReadOnlyMemory<byte> canonicalBytes)
    {
        Domain = domain;
        _canonicalBytes = canonicalBytes.ToArray();
        Digest = Convert.ToHexStringLower(SHA256.HashData(_canonicalBytes));
    }

    private readonly byte[] _canonicalBytes;
    public string Domain { get; }
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public string Digest { get; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Domain) || _canonicalBytes.Length == 0 || !IsSHA(Digest)
            || Digest != Convert.ToHexStringLower(SHA256.HashData(_canonicalBytes)))
            throw new InvalidDataException($"repository {Domain} authority corroboration diverges");
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class RepositoryLoopClosureToolAuthorityCorroboration : RepositoryLoopClosureCanonicalAuthorityCorroboration
{
    public RepositoryLoopClosureToolAuthorityCorroboration()
        : base("tool", RepositoryNativeToolAuthority.CanonicalBytes) { }

    public new void Validate()
    {
        base.Validate();
        if (Digest != RepositoryNativeToolAuthority.SHA256
            || !CanonicalBytes.Span.SequenceEqual(RepositoryNativeToolAuthority.CanonicalBytes))
            throw new InvalidDataException("repository native tool authority corroboration diverges from the mounted schema");
    }
}

public sealed class RepositoryLoopClosurePolicyAuthorityCorroboration : RepositoryLoopClosureCanonicalAuthorityCorroboration
{
    public RepositoryLoopClosurePolicyAuthorityCorroboration()
        : this(RepositoryNativePolicyAuthority.Create()) { }

    private RepositoryLoopClosurePolicyAuthorityCorroboration(RepositoryNativePolicyAuthority authority)
        : base("policy", authority.CanonicalBytes) { }

    public new void Validate()
    {
        base.Validate();
        RepositoryNativePolicyAuthority authority = RepositoryNativePolicyAuthority.Create();
        if (Digest != authority.SHA256 || !CanonicalBytes.Span.SequenceEqual(authority.CanonicalBytes.Span))
            throw new InvalidDataException("repository native policy authority corroboration diverges from the policy domain");
    }
}

public sealed class RepositoryLoopClosureInitialStateAuthorityCorroboration : RepositoryLoopClosureCanonicalAuthorityCorroboration
{
    public RepositoryLoopClosureInitialStateAuthorityCorroboration(ReadOnlyMemory<byte> canonicalBytes)
        : base("initial-state", canonicalBytes) { }

    public static RepositoryLoopClosureInitialStateAuthorityCorroboration Create(ulong seed, int horizon)
        => new(Encoding.UTF8.GetBytes($"repository-loop-initial-state-v1|{seed}|{horizon}"));
}

public sealed class RepositoryLoopClosureFuelAuthorityCorroboration : RepositoryLoopClosureCanonicalAuthorityCorroboration
{
    public RepositoryLoopClosureFuelAuthorityCorroboration(ReadOnlyMemory<byte> canonicalBytes)
        : base("fuel", canonicalBytes) { }

    public static RepositoryLoopClosureFuelAuthorityCorroboration Create(long offeredFuel)
        => new(Encoding.UTF8.GetBytes($"repository-loop-offered-fuel-v1|{offeredFuel}"));
}

public sealed class RepositoryLoopClosureCandidateSchemaAuthorityCorroboration
{
    private static readonly RepositoryCandidateSpecies[] FrozenSpecies = Enum.GetValues<RepositoryCandidateSpecies>();

    public RepositoryLoopClosureCandidateSchemaAuthorityCorroboration()
        : this(FrozenSpecies)
    {
    }

    public RepositoryLoopClosureCandidateSchemaAuthorityCorroboration(IReadOnlyList<RepositoryCandidateSpecies> species)
    {
        Species = Array.AsReadOnly((species ?? throw new ArgumentNullException(nameof(species))).ToArray());
        if (!Species.SequenceEqual(FrozenSpecies))
            throw new InvalidDataException("repository candidate schema is not the frozen native schema");
        Canonical = ComputeCanonical(Species);
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical)));
    }

    public IReadOnlyList<RepositoryCandidateSpecies> Species { get; }
    public string Canonical { get; }
    public string Digest { get; }

    public static RepositoryLoopClosureCandidateSchemaAuthorityCorroboration CreateDefault()
        => new();

    public void Validate()
    {
        if (!Species.SequenceEqual(FrozenSpecies)
            || Canonical != ComputeCanonical(Species)
            || Digest != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical))))
            throw new InvalidDataException("repository candidate authority corroboration diverges");
    }

    private static string ComputeCanonical(IReadOnlyList<RepositoryCandidateSpecies> species)
        => string.Join('|', new[] { "repository-candidate-schema-v1" }.Concat(
            species.Select(static (value, index) => $"{index}:{(byte)value}:{value}")));
}

/// Runtime corroboration binds typed pre-run authorities to the registration and to the
/// exact source/world snapshots used by the run.
public sealed class RepositoryLoopClosureRuntimeAuthorityCorroboration
{
    public RepositoryLoopClosureRuntimeAuthorityCorroboration(
        RepositoryLoopClosureAuthoritySnapshot sourceAuthority,
        RepositoryLoopClosureWorldSnapshot world,
        RepositoryLoopClosureToolAuthorityCorroboration tool,
        RepositoryLoopClosurePolicyAuthorityCorroboration policy,
        RepositoryLoopClosureCandidateSchemaAuthorityCorroboration candidate,
        RepositoryLoopClosureInitialStateAuthorityCorroboration initialState,
        RepositoryLoopClosureFuelAuthorityCorroboration fuel)
    {
        SourceAuthority = sourceAuthority ?? throw new ArgumentNullException(nameof(sourceAuthority));
        World = world ?? throw new ArgumentNullException(nameof(world));
        Tool = tool ?? throw new ArgumentNullException(nameof(tool));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        InitialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        Fuel = fuel ?? throw new ArgumentNullException(nameof(fuel));
        AuthoritySHA256 = ComputeAuthoritySHA256();
    }

    public RepositoryLoopClosureAuthoritySnapshot SourceAuthority { get; }
    public RepositoryLoopClosureWorldSnapshot World { get; }
    public RepositoryLoopClosureToolAuthorityCorroboration Tool { get; }
    public RepositoryLoopClosurePolicyAuthorityCorroboration Policy { get; }
    public RepositoryLoopClosureCandidateSchemaAuthorityCorroboration Candidate { get; }
    public RepositoryLoopClosureInitialStateAuthorityCorroboration InitialState { get; }
    public RepositoryLoopClosureFuelAuthorityCorroboration Fuel { get; }
    public string SourceAuthoritySHA256 => SourceAuthority.AuthoritySHA256;
    public string WorldContentSHA256 => World.ContentSHA256;
    public string WorldSnapshotSHA256 => World.SnapshotSHA256;
    public string ToolAuthoritySHA256 => Tool.Digest;
    public string PolicyAuthoritySHA256 => Policy.Digest;
    public string CandidateAuthoritySHA256 => Candidate.Digest;
    public string InitialStateSHA256 => InitialState.Digest;
    public string OfferedFuelSHA256 => Fuel.Digest;
    public string AuthoritySHA256 { get; }

    /// Split per corroboration: eight pre-run authorities can each drift independently, and a fused
    /// verdict makes the reader re-derive all eight to learn which one moved.
    public void Validate(RepositoryLoopClosureRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        SourceAuthority.Validate(); World.Validate(); Tool.Validate(); Policy.Validate();
        Candidate.Validate(); InitialState.Validate(); Fuel.Validate();
        RequireCorroboration(AuthoritySHA256, ComputeAuthoritySHA256(), "runtime authority digest");
        // The registration's source authority is the bundle's source-authority.txt entry, NOT
        // the bundle manifest digest — the manifest lists the registration that carries it.
        RequireCorroboration(SourceAuthority.SourceAuthorityEntrySHA256, registration.SourceAuthoritySHA256, "source authority");
        RequireCorroboration(WorldContentSHA256, registration.WorldContentSHA256, "world content");
        RequireCorroboration(WorldSnapshotSHA256, registration.WorldSnapshotSHA256, "world snapshot");
        RequireCorroboration(ToolAuthoritySHA256, registration.ToolAuthoritySHA256, "tool authority");
        RequireCorroboration(PolicyAuthoritySHA256, registration.PolicyAuthoritySHA256, "policy authority");
        RequireCorroboration(CandidateAuthoritySHA256, registration.CandidateAuthoritySHA256, "candidate authority");
        RequireCorroboration(InitialStateSHA256, registration.InitialStateSHA256, "initial state");
        RequireCorroboration(OfferedFuelSHA256, registration.OfferedFuelSHA256, "offered fuel");
        RequireCorroboration(InitialState.Digest,
            RepositoryLoopClosureInitialStateAuthorityCorroboration.Create(registration.Seed, registration.Horizon).Digest,
            "initial-state formula");
        RequireCorroboration(Fuel.Digest,
            RepositoryLoopClosureFuelAuthorityCorroboration.Create(registration.OfferedFuel).Digest, "fuel formula");
    }

    private static void RequireCorroboration(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"repository loop runtime {name} corroboration diverges: '{actual}' vs '{expected}'");
    }

    private string ComputeAuthoritySHA256()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "repository-loop-runtime-authority-v2", SourceAuthoritySHA256, WorldContentSHA256,
            WorldSnapshotSHA256, ToolAuthoritySHA256, PolicyAuthoritySHA256, CandidateAuthoritySHA256,
            InitialStateSHA256, OfferedFuelSHA256))));
}

/// The only filesystem-adjacent input is a sealed tape snapshot. It contains event
/// bytes and lineage receipts, never a Tape, repository root, or path to reopen.
public sealed class RepositoryLoopClosureTapeSnapshot
{
    public RepositoryLoopClosureTapeSnapshot(
        LoopLineageTapeSnapshot tape,
        IReadOnlyList<LoopLineageEdgeReceipt> lineageEdges,
        RepositoryLoopClosureTapeSeal seal)
    {
        Tape = tape ?? throw new ArgumentNullException(nameof(tape));
        Events = Array.AsReadOnly(tape.Events.Select(static item => new LoopLineageTapeEvent(
            item.EventID, item.Payload.ToArray(), item.Source, item.Provenance, item.Roles)).ToArray());
        TapeSHA256 = LoopLineageTapeSnapshot.ComputeTapeDigest(Events);
        if (tape.Digest != TapeSHA256)
            throw new InvalidDataException("repository loop tape digest is not sealed from event bytes");
        PreSealTapeSHA256 = Events.Count > 0
            ? LoopLineageTapeSnapshot.ComputeTapeDigest(Events.Take(Events.Count - 1).ToArray())
            : throw new InvalidDataException("repository loop tape snapshot omits terminal seal");
        LineageEdges = Array.AsReadOnly((lineageEdges ?? throw new ArgumentNullException(nameof(lineageEdges)))
            .Select(CloneEdge).ToArray());
        Seal = seal;
    }

    public LoopLineageTapeSnapshot Tape { get; }
    public IReadOnlyList<LoopLineageTapeEvent> Events { get; }
    public IReadOnlyList<LoopLineageEdgeReceipt> LineageEdges { get; }
    public RepositoryLoopClosureTapeSeal Seal { get; }
    public string TapeSHA256 { get; }
    public string PreSealTapeSHA256 { get; }
    public long SealEventID => Seal.EventID.Value;

    private static LoopLineageEdgeReceipt CloneEdge(LoopLineageEdgeReceipt edge)
        => new(edge.EdgeID, edge.Node, edge.PredecessorIDs.ToArray(), edge.PredecessorSHA256.ToArray(),
            edge.PreviousLineageSHA256, edge.CanonicalLineageSHA256);

    public void Validate()
    {
        if (Tape.Digest != TapeSHA256 || LoopLineageTapeSnapshot.ComputeTapeDigest(Events) != TapeSHA256)
            throw new InvalidDataException("repository loop tape digest diverges from its deep seal");
        if (Events.Count == 0 || Events.Select(static item => item.EventID.Value).Distinct().Count() != Events.Count)
            throw new InvalidDataException("repository loop tape snapshot is empty or repeats an event");
        for (int index = 1; index < Events.Count; index++)
            if (Events[index - 1].EventID.Value >= Events[index].EventID.Value)
                throw new InvalidDataException("repository loop tape event order is not monotone");
        foreach (LoopLineageEdgeReceipt edge in LineageEdges) edge.Validate();
        if (LineageEdges.Select(static edge => edge.EdgeID).Distinct().Count() != LineageEdges.Count)
            throw new InvalidDataException("repository loop lineage edge identity repeats");
        if (LineageEdges.Select(static edge => edge.Node.EventID.Value).Distinct().Count() != LineageEdges.Count)
            throw new InvalidDataException("repository loop lineage event identity repeats");
        HashSet<long> eventIDs = Events.Select(static item => item.EventID.Value).ToHashSet();
        if (LineageEdges.Any(edge => !eventIDs.Contains(edge.Node.EventID.Value)))
            throw new InvalidDataException("repository loop lineage edge refers to an event outside the sealed tape");
        if (PreSealTapeSHA256 != LoopLineageTapeSnapshot.ComputeTapeDigest(Events.Take(Events.Count - 1).ToArray()))
            throw new InvalidDataException("repository loop pre-seal tape digest diverges");
        Seal.Validate(Events, PreSealTapeSHA256);
    }
}

public enum RepositoryLoopClosureTaskSpecies : byte
{
    Locate,
    Trace,
    Read,
    Answer,
    Diagnosis,
}

public enum RepositoryLoopClosureResultSpecies : byte
{
    Path,
    Trace,
    Text,
    Answer,
    Diagnosis,
}

public readonly record struct RepositoryLoopClosureExpectedSource(
    string Path,
    long Bytes,
    string SHA256);

public readonly record struct RepositoryLoopClosureExpectedResult
{
    private readonly byte[] _content;

    public RepositoryLoopClosureExpectedResult(
        RepositoryLoopClosureResultSpecies species,
        ReadOnlyMemory<byte> content)
    {
        Species = species;
        _content = content.ToArray();
        SHA256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_content));
    }

    public RepositoryLoopClosureExpectedResult(
        RepositoryLoopClosureResultSpecies species,
        string sha256,
        ReadOnlyMemory<byte> content)
    {
        Species = species;
        SHA256 = sha256;
        _content = content.ToArray();
    }

    public RepositoryLoopClosureResultSpecies Species { get; }
    public string SHA256 { get; }
    public ReadOnlyMemory<byte> Content => _content ?? Array.Empty<byte>();
}

public enum RepositoryLoopClosureTaskOracleModes : byte
{
    SourceResult,
    TypedPrediction,
}

/// The source-backed oracle is registration authority, not organism input. It is
/// intentionally reachable only by the report/adjudication seam; the public task
/// carries a prompt and task identity, never the path, candidate, or trigger.
public sealed class RepositoryLoopClosureTaskOracle
{
    public RepositoryLoopClosureTaskOracle(
        RepositoryLoopClosureTaskOracleModes mode,
        RepositoryLoopClosureExpectedSource expectedSource,
        RepositoryLoopClosureExpectedResult expectedResult,
        RepositoryPrediction? prediction = null)
    {
        Mode = mode;
        ExpectedSource = expectedSource;
        ExpectedResult = expectedResult;
        Prediction = prediction;
        AuthoritySHA256 = ComputeAuthoritySHA256();
    }

    public RepositoryLoopClosureTaskOracleModes Mode { get; }
    internal RepositoryLoopClosureExpectedSource ExpectedSource { get; }
    internal RepositoryLoopClosureExpectedResult ExpectedResult { get; }
    internal RepositoryPrediction? Prediction { get; }
    public string AuthoritySHA256 { get; }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)
            || string.IsNullOrWhiteSpace(ExpectedSource.Path) || ExpectedSource.Bytes < 0 || !IsSHA(ExpectedSource.SHA256)
            || ExpectedSourceLine < 0
            || !Enum.IsDefined(ExpectedResult.Species) || ExpectedResult.Content.Length == 0 || !IsSHA(ExpectedResult.SHA256)
            || ExpectedResult.SHA256 != Convert.ToHexStringLower(SHA256.HashData(ExpectedResult.Content.Span))
            || !IsSHA(AuthoritySHA256) || AuthoritySHA256 != ComputeAuthoritySHA256())
            throw new InvalidDataException("repository task oracle is malformed");
        if (Mode == RepositoryLoopClosureTaskOracleModes.TypedPrediction)
        {
            if (Prediction is not { } prediction) throw new InvalidDataException("repository task typed oracle omits its prediction");
            prediction.Validate();
            if (prediction.Path != ExpectedSource.Path)
                throw new InvalidDataException("repository task typed oracle source path diverges from its prediction");
            if (prediction.Species == RepositoryPredictionSpecies.SharedIdentifier)
                throw new InvalidDataException("repository shared-identifier oracle lacks a second source corroboration");
        }
        else if (Prediction is not null)
            throw new InvalidDataException("repository source oracle unexpectedly carries a typed prediction");
    }

    internal int ExpectedSourceLine => Prediction is { Species: RepositoryPredictionSpecies.LocusContains } prediction ? prediction.Line : 0;

    internal RepositoryLoopClosureExpectedResult DeriveExpectedResult(
        RepositoryLoopClosureTaskSpecies taskSpecies,
        RepositoryLoopClosureWorldSnapshot world)
    {
        RepositoryLoopClosureWorldFile file = world.Files.FirstOrDefault(value => value.Path.Value == ExpectedSource.Path)
            ?? throw new InvalidDataException("repository task oracle source is absent from the sealed world");
        ReadOnlyMemory<byte> content;
        if (taskSpecies == RepositoryLoopClosureTaskSpecies.Diagnosis)
            content = Encoding.UTF8.GetBytes(Prediction?.Canonical ?? throw new InvalidDataException("repository diagnosis oracle omits prediction"));
        else if (taskSpecies is RepositoryLoopClosureTaskSpecies.Trace or RepositoryLoopClosureTaskSpecies.Read)
        {
            if (ExpectedSourceLine < 1 || !file.TryGetLineBytes(ExpectedSourceLine, out content))
                throw new InvalidDataException("repository task oracle source locus is absent from the sealed world");
        }
        else
            content = Encoding.UTF8.GetBytes(ExpectedSource.Path);
        return new RepositoryLoopClosureExpectedResult(ExpectedResult.Species, content);
    }

    private string ComputeAuthoritySHA256()
    {
        string canonical = string.Join('|', "repository-loop-task-oracle-v2", Mode,
            ExpectedSource.Path, ExpectedSource.Bytes, ExpectedSource.SHA256,
            ExpectedSourceLine, ExpectedResult.Species, ExpectedResult.SHA256,
            Convert.ToBase64String(ExpectedResult.Content.Span), Prediction?.Canonical ?? "none");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// The registered real task freezes only the prompt/species and the hidden
/// deterministic oracle. Candidate/frontier/access/source/result facts belong to
/// the outcome evidence selected by the organism.
public sealed class RepositoryLoopClosureTaskSpec
{
    public RepositoryLoopClosureTaskSpec(
        string taskID,
        RepositoryLoopClosureTaskSpecies species,
        string prompt,
        RepositoryLoopClosureTaskOracle oracle)
    {
        TaskID = taskID;
        Species = species;
        Prompt = prompt;
        _oracle = oracle ?? throw new ArgumentNullException(nameof(oracle));
    }

    private readonly RepositoryLoopClosureTaskOracle _oracle;
    public string TaskID { get; }
    public RepositoryLoopClosureTaskSpecies Species { get; }
    public string Prompt { get; }
    public RepositoryLoopClosureTaskPromptView PromptView => new(TaskID, Species, Prompt);
    internal RepositoryLoopClosureTaskOracle Oracle => _oracle;
    public string AuthoritySHA256 => ComputeAuthoritySHA256();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TaskID) || !Enum.IsDefined(Species) || string.IsNullOrWhiteSpace(Prompt))
            throw new InvalidDataException("repository loop task specification is malformed");
        _oracle.Validate();
        if ((Species == RepositoryLoopClosureTaskSpecies.Diagnosis) != (_oracle.Mode == RepositoryLoopClosureTaskOracleModes.TypedPrediction))
            throw new InvalidDataException("repository diagnosis task oracle mode is not typed-prediction exact");
        if ((Species is RepositoryLoopClosureTaskSpecies.Trace or RepositoryLoopClosureTaskSpecies.Read
            or RepositoryLoopClosureTaskSpecies.Diagnosis) && _oracle.ExpectedSourceLine < 1)
            throw new InvalidDataException("repository task species requires a positive source locus");
        if (!MatchesResult(Species, _oracle.ExpectedResult.Species))
            throw new InvalidDataException("repository task oracle result species is not bound to its task species");
        if (!IsSHA(AuthoritySHA256) || AuthoritySHA256 != ComputeAuthoritySHA256())
            throw new InvalidDataException("repository loop task authority diverges");
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private string ComputeAuthoritySHA256()
    {
        string canonical = string.Join('|', "repository-loop-task-authority-v3", TaskID, Species, Prompt, _oracle.AuthoritySHA256);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool MatchesResult(RepositoryLoopClosureTaskSpecies species, RepositoryLoopClosureResultSpecies result)
        => RepositoryLoopTaskSpeciesRules.MatchesResult(species, result);
}

/// Organism-facing task view. The hidden oracle never crosses this boundary.
public readonly record struct RepositoryLoopClosureTaskPromptView(
    string TaskID,
    RepositoryLoopClosureTaskSpecies Species,
    string Prompt)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TaskID) || !Enum.IsDefined(Species) || string.IsNullOrWhiteSpace(Prompt))
            throw new InvalidDataException("repository task prompt view is malformed");
    }
}

/// Frozen registration authority for the native assay. These parameters are part
/// of identity, not loose runner knobs that can be changed after intake.
public sealed class RepositoryLoopClosureRegistration
{
    public const int SchemaVersion = 4;
    public RepositoryLoopClosureRegistration(
        string planID,
        string sourceAuthoritySHA256,
        string worldContentSHA256,
        string worldSnapshotSHA256,
        string toolAuthoritySHA256,
        string policyAuthoritySHA256,
        string candidateAuthoritySHA256,
        string initialStateSHA256,
        ulong seed,
        int horizon,
        long offeredFuel,
        string offeredFuelSHA256,
        int opportunityFloor,
        long decisionThreshold,
        RepositoryLoopClosureTaskSpec task,
        RepositoryLoopClosureLineageNullSpec lineageNullSpec)
    {
        PlanID = planID;
        SourceAuthoritySHA256 = sourceAuthoritySHA256;
        WorldContentSHA256 = worldContentSHA256;
        WorldSnapshotSHA256 = worldSnapshotSHA256;
        ToolAuthoritySHA256 = toolAuthoritySHA256;
        PolicyAuthoritySHA256 = policyAuthoritySHA256;
        CandidateAuthoritySHA256 = candidateAuthoritySHA256;
        InitialStateSHA256 = initialStateSHA256;
        Seed = seed;
        Horizon = horizon;
        OfferedFuel = offeredFuel;
        OfferedFuelSHA256 = offeredFuelSHA256;
        OpportunityFloor = opportunityFloor;
        DecisionThreshold = decisionThreshold;
        Task = task ?? throw new ArgumentNullException(nameof(task));
        LineageNullSpec = lineageNullSpec ?? throw new ArgumentNullException(nameof(lineageNullSpec));
        RegistrationSHA256 = ComputeDigest();
    }

    public string PlanID { get; }
    public string SourceAuthoritySHA256 { get; }
    public string WorldContentSHA256 { get; }
    public string WorldSnapshotSHA256 { get; }
    public string ToolAuthoritySHA256 { get; }
    public string PolicyAuthoritySHA256 { get; }
    public string CandidateAuthoritySHA256 { get; }
    public string InitialStateSHA256 { get; }
    public ulong Seed { get; }
    public int Horizon { get; }
    public long OfferedFuel { get; }
    public string OfferedFuelSHA256 { get; }
    public int OpportunityFloor { get; }
    public long DecisionThreshold { get; }
    public RepositoryLoopClosureTaskSpec Task { get; }
    public string TaskID => Task.TaskID;
    public string TaskAuthoritySHA256 => Task.AuthoritySHA256;
    public RepositoryLoopClosureLineageNullSpec LineageNullSpec { get; }
    public string RegistrationSHA256 { get; }
    public ReadOnlyMemory<byte> SemanticCanonicalBytes => Encoding.UTF8.GetBytes(CanonicalForm());

    public byte[] Encode()
    {
        ValidateStructure();
        RepositoryNativeRegistrationRON document = EncodeDocument();
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        document = EncodeDocument();
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("native repository registration RON encoding is nondeterministic");
        return first;
    }

    public static RepositoryLoopClosureRegistration Decode(ReadOnlySpan<byte> bytes)
    {
        RepositoryNativeRegistrationRON document = RonSerializer.Deserialize<RepositoryNativeRegistrationRON>(bytes);
        if (document.schemaVersion != SchemaVersion)
            throw new InvalidDataException($"native repository registration schema {document.schemaVersion} is unsupported");
        RepositoryLoopClosureTaskOracle oracle = new(
            (RepositoryLoopClosureTaskOracleModes)document.taskOracle.mode,
            new RepositoryLoopClosureExpectedSource(document.taskOracle.sourcePath, document.taskOracle.sourceBytes, document.taskOracle.sourceSHA256),
            new RepositoryLoopClosureExpectedResult((RepositoryLoopClosureResultSpecies)document.taskOracle.resultSpecies,
                document.taskOracle.resultSHA256, Convert.FromBase64String(document.taskOracle.resultContentBase64)),
            document.taskOracle.predictionPresent
                ? new RepositoryPrediction((RepositoryPredictionSpecies)document.taskOracle.predictionSpecies, document.taskOracle.predictionPath,
                    document.taskOracle.predictionLine, document.taskOracle.predictionValue, document.taskOracle.predictionOtherPath)
                : null);
        RepositoryLoopClosureTaskSpec task = new(document.taskID, (RepositoryLoopClosureTaskSpecies)document.taskSpecies,
            document.taskPrompt, oracle);
        if (!string.Equals(document.taskAuthoritySHA256, task.AuthoritySHA256, StringComparison.Ordinal)
            || !string.Equals(document.taskOracle.authoritySHA256, oracle.AuthoritySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("native repository task oracle authority diverges");
        RepositoryLoopClosureLineageNullSpec lineageNullSpec = new(
            document.lineageNullSpec.domain, document.lineageNullSpec.algorithm, document.lineageNullSpec.digest);
        RepositoryLoopClosureRegistration registration = new(document.planID,
            document.sourceAuthoritySHA256, document.worldContentSHA256, document.worldSnapshotSHA256,
            document.toolAuthoritySHA256, document.policyAuthoritySHA256,
            document.candidateAuthoritySHA256,
            document.initialStateSHA256, document.seed, document.horizon, document.offeredFuel,
            document.offeredFuelSHA256, document.opportunityFloor, document.decisionThreshold, task, lineageNullSpec);
        if (registration.RegistrationSHA256 != document.registrationSHA256)
            throw new InvalidDataException("native repository registration RON digest diverges");
        if (!registration.Encode().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("native repository registration RON round-trip changed bytes");
        return registration;
    }

    private void ValidateStructure()
    {
        Task.Validate();
        if (string.IsNullOrWhiteSpace(PlanID) || Horizon <= 0 || OfferedFuel < 0 || OpportunityFloor < 0 || DecisionThreshold < 0
            || !IsSHA(RegistrationSHA256) || RegistrationSHA256 != ComputeDigest()
            || !IsSHA(SourceAuthoritySHA256) || !IsSHA(WorldContentSHA256) || !IsSHA(WorldSnapshotSHA256)
            || !IsSHA(ToolAuthoritySHA256) || !IsSHA(PolicyAuthoritySHA256) || !IsSHA(CandidateAuthoritySHA256)
            || !IsSHA(InitialStateSHA256) || !IsSHA(OfferedFuelSHA256))
            throw new InvalidDataException("native repository registration authority is malformed");
        ValidatePreRunAuthorityFormulas();
        LineageNullSpec.Validate();
    }

    private RepositoryNativeRegistrationRON EncodeDocument()
    {
        RepositoryNativeRegistrationRON document = new()
        {
            schemaVersion = SchemaVersion,
            planID = PlanID, sourceAuthoritySHA256 = SourceAuthoritySHA256,
            worldContentSHA256 = WorldContentSHA256, worldSnapshotSHA256 = WorldSnapshotSHA256,
            toolAuthoritySHA256 = ToolAuthoritySHA256,
            policyAuthoritySHA256 = PolicyAuthoritySHA256, candidateAuthoritySHA256 = CandidateAuthoritySHA256,
            initialStateSHA256 = InitialStateSHA256, seed = Seed, horizon = Horizon, offeredFuel = OfferedFuel,
            offeredFuelSHA256 = OfferedFuelSHA256, opportunityFloor = OpportunityFloor, decisionThreshold = DecisionThreshold,
            taskID = TaskID, taskSpecies = (byte)Task.Species, taskPrompt = Task.Prompt, taskAuthoritySHA256 = Task.AuthoritySHA256,
            lineageNullSpec = new RepositoryNativeLineageNullSpecRON
            {
                domain = LineageNullSpec.Domain, algorithm = LineageNullSpec.Algorithm, digest = LineageNullSpec.Digest,
            },
            registrationSHA256 = RegistrationSHA256,
        };
        RepositoryLoopClosureTaskOracle oracle = Task.Oracle;
        document.taskOracle = new RepositoryNativeTaskOracleRON
        {
            mode = (byte)oracle.Mode, sourcePath = oracle.ExpectedSource.Path, sourceBytes = oracle.ExpectedSource.Bytes,
            sourceSHA256 = oracle.ExpectedSource.SHA256, resultSpecies = (byte)oracle.ExpectedResult.Species,
            resultSHA256 = oracle.ExpectedResult.SHA256, resultContentBase64 = Convert.ToBase64String(oracle.ExpectedResult.Content.Span),
            predictionPresent = oracle.Prediction is not null, predictionSpecies = (byte)(oracle.Prediction?.Species ?? default),
            predictionPath = oracle.Prediction?.Path ?? "", predictionLine = oracle.Prediction?.Line ?? 0,
            predictionValue = oracle.Prediction?.Value ?? "", predictionOtherPath = oracle.Prediction?.OtherPath ?? "",
            authoritySHA256 = oracle.AuthoritySHA256,
        };
        return document;
    }

    /// Split per clause on purpose. A fused conjunction here answers "the registration is wrong"
    /// with one word, and the eleven ways it can be wrong are eleven different repairs — the
    /// world drifted, the bundle is another registration's, the task was swapped, the null spec
    /// was retuned. Each clause names what it compared.
    public void Validate(RepositoryLoopClosureAdjudicationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Task.Validate();
        if (string.IsNullOrWhiteSpace(PlanID) || Horizon <= 0 || OfferedFuel < 0 || OpportunityFloor < 0 || DecisionThreshold < 0)
            throw new InvalidDataException($"repository loop registration parameters are malformed: plan='{PlanID}' horizon={Horizon} fuel={OfferedFuel} floor={OpportunityFloor} threshold={DecisionThreshold}");
        RequireRegistrationSHA(RegistrationSHA256, "registration");
        RequireRegistrationSHA(SourceAuthoritySHA256, "source authority");
        RequireRegistrationSHA(WorldContentSHA256, "world content");
        RequireRegistrationSHA(WorldSnapshotSHA256, "world snapshot");
        RequireRegistrationSHA(ToolAuthoritySHA256, "tool authority");
        RequireRegistrationSHA(PolicyAuthoritySHA256, "policy authority");
        RequireRegistrationSHA(CandidateAuthoritySHA256, "candidate authority");
        RequireRegistrationSHA(InitialStateSHA256, "initial state");
        RequireRegistrationSHA(OfferedFuelSHA256, "offered fuel");
        RequireRegistrationMatch(RegistrationSHA256, ComputeDigest(), "registration digest", "its own canonical form");
        RequireRegistrationMatch(TaskID, input.Task.TaskID, "task identity", "the sealed task");
        RequireRegistrationMatch(TaskAuthoritySHA256, input.Task.AuthoritySHA256, "task authority", "the sealed task");
        RequireRegistrationMatch(SourceAuthoritySHA256, input.Authority.SourceAuthorityEntrySHA256,
            "source authority", "the sealed bundle's source-authority.txt entry");
        RequireRegistrationMatch(WorldContentSHA256, input.World.ContentSHA256, "world content", "the sealed world");
        RequireRegistrationMatch(WorldSnapshotSHA256, input.World.SnapshotSHA256, "world snapshot", "the sealed world");
        RequireRegistrationMatch(LineageNullSpec.Domain, input.Registration.LineageNullSpec.Domain, "lineage null domain", "the input registration");
        RequireRegistrationMatch(LineageNullSpec.Algorithm, input.Registration.LineageNullSpec.Algorithm, "lineage null algorithm", "the input registration");
        RequireRegistrationMatch(LineageNullSpec.Digest, input.Registration.LineageNullSpec.Digest, "lineage null digest", "the input registration");
        RequireRegistrationMatch(input.Authority.RegistrationSHA256, RegistrationSHA256, "bundle registration identity", "this registration");
        RequireRegistrationMatch(input.Authority.RegistrationDocumentSHA256,
            Convert.ToHexStringLower(SHA256.HashData(Encode())), "bundle registration document", "the encoded registration");
        ValidatePreRunAuthorityFormulas();
    }

    private static void RequireRegistrationSHA(string value, string name)
    {
        if (!IsSHA(value)) throw new InvalidDataException($"repository loop registration {name} digest is malformed: '{value}'");
    }

    private static void RequireRegistrationMatch(string actual, string expected, string name, string against)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"repository loop registration {name} diverges from {against}: '{actual}' vs '{expected}'");
    }

    private void ValidatePreRunAuthorityFormulas()
    {
        if (ToolAuthoritySHA256 != new RepositoryLoopClosureToolAuthorityCorroboration().Digest
            || PolicyAuthoritySHA256 != new RepositoryLoopClosurePolicyAuthorityCorroboration().Digest
            || CandidateAuthoritySHA256 != RepositoryLoopClosureCandidateSchemaAuthorityCorroboration.CreateDefault().Digest
            || InitialStateSHA256 != RepositoryLoopClosureInitialStateAuthorityCorroboration.Create(Seed, Horizon).Digest
            || OfferedFuelSHA256 != RepositoryLoopClosureFuelAuthorityCorroboration.Create(OfferedFuel).Digest)
            throw new InvalidDataException("native repository registration pre-run authority formula diverges");
    }

    private string ComputeDigest()
    {
        string canonical = CanonicalForm();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private string CanonicalForm()
        => string.Join('|', "repository-loop-registration-v5", PlanID, SourceAuthoritySHA256,
            WorldContentSHA256, WorldSnapshotSHA256, ToolAuthoritySHA256, PolicyAuthoritySHA256,
            CandidateAuthoritySHA256, InitialStateSHA256, Seed, Horizon, OfferedFuel, OfferedFuelSHA256,
            OpportunityFloor, DecisionThreshold, TaskID, Task.AuthoritySHA256, Task.Species, Task.Prompt,
            LineageNullSpec.Domain, LineageNullSpec.Algorithm, LineageNullSpec.Digest);

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// All adjudication input is explicitly sealed before the report phase. There is no
/// filesystem or append-only writer in this object by design.
public sealed class RepositoryLoopClosureAdjudicationInput
{
    public RepositoryLoopClosureAdjudicationInput(
        string runID,
        RepositoryLoopClosureWorldSnapshot world,
        RepositoryLoopClosureTapeSnapshot tape,
        RepositoryLoopClosureJournalSnapshot journal,
        RepositoryLoopClosureAuthoritySnapshot authority,
        RepositoryLoopClosureAccessSnapshot access,
        RepositoryLoopClosureFrontierSnapshot frontier,
        RepositoryLoopClosurePatternSnapshot pattern,
        RepositoryLoopClosureTaskSpec task,
        RepositoryLoopClosureRuntimeAuthorityCorroboration runtimeAuthority,
        RepositoryLoopClosureSealedEvidenceAuthority evidenceAuthority,
        RepositoryLoopClosureRegistration registration)
    {
        RunID = runID;
        World = world ?? throw new ArgumentNullException(nameof(world));
        Tape = tape ?? throw new ArgumentNullException(nameof(tape));
        Journal = journal ?? throw new ArgumentNullException(nameof(journal));
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        Frontier = frontier ?? throw new ArgumentNullException(nameof(frontier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        Task = task ?? throw new ArgumentNullException(nameof(task));
        RuntimeAuthority = runtimeAuthority ?? throw new ArgumentNullException(nameof(runtimeAuthority));
        EvidenceAuthority = evidenceAuthority ?? throw new ArgumentNullException(nameof(evidenceAuthority));
        Registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    public string RunID { get; }
    public RepositoryLoopClosureWorldSnapshot World { get; }
    public RepositoryLoopClosureTapeSnapshot Tape { get; }
    public RepositoryLoopClosureJournalSnapshot Journal { get; }
    public RepositoryLoopClosureAuthoritySnapshot Authority { get; }
    public RepositoryLoopClosureAccessSnapshot Access { get; }
    public RepositoryLoopClosureFrontierSnapshot Frontier { get; }
    public RepositoryLoopClosurePatternSnapshot Pattern { get; }
    public RepositoryLoopClosureTaskSpec Task { get; }
    public RepositoryLoopClosureRuntimeAuthorityCorroboration RuntimeAuthority { get; }
    public RepositoryLoopClosureSealedEvidenceAuthority EvidenceAuthority { get; }
    public RepositoryLoopClosureRegistration Registration { get; }
    public string SealedIdentitySHA256
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            RunID, World.ContentSHA256, World.SnapshotSHA256, Tape.TapeSHA256, Journal.JournalSHA256, Authority.AuthoritySHA256,
            Authority.RegistrationSHA256, Access.AccessSHA256, Access.SourcesSHA256, Frontier.FrontierSHA256, Pattern.PatternSHA256,
            Frontier.RuntimeAuthoritySHA256, Pattern.PendingAuthoritySHA256,
            Journal.RowAuthoritiesSHA256,
            Task.TaskID, Task.AuthoritySHA256, RuntimeAuthority.AuthoritySHA256, EvidenceAuthority.AuthoritySHA256,
            Registration.RegistrationSHA256))));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RunID)) throw new InvalidDataException("repository loop run identity is empty");
        World.Validate(); Tape.Validate(); Journal.Validate(); Authority.Validate(); Access.Validate(); Frontier.Validate(); Pattern.Validate(); Task.Validate(); Task.PromptView.Validate();
        ValidateTaskPromptEvidence();
        Registration.Validate(this); RuntimeAuthority.Validate(Registration); EvidenceAuthority.Validate(this);
        if (RuntimeAuthority.SourceAuthority.AuthoritySHA256 != Authority.AuthoritySHA256
            || RuntimeAuthority.SourceAuthority.RegistrationSHA256 != Authority.RegistrationSHA256
            || RuntimeAuthority.World.ContentSHA256 != World.ContentSHA256
            || RuntimeAuthority.World.SnapshotSHA256 != World.SnapshotSHA256
            || RuntimeAuthority.Candidate.Digest != Registration.CandidateAuthoritySHA256)
            throw new InvalidDataException("repository loop runtime corroboration is not the sealed input authority");
        if (!Journal.Rows.Any(row => row.EventID == Tape.Seal.EventID && row.Source == Tape.Seal.Source))
            throw new InvalidDataException("repository loop tape seal is absent from the sealed journal");
        HashSet<string> worldPaths = World.Files.Select(static file => file.Path.Value).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, RepositoryLoopClosureWorldFile> worldFiles = World.Files.ToDictionary(static file => file.Path.Value, StringComparer.Ordinal);
        RepositoryLoopClosureExpectedSource expectedSource = Task.Oracle.ExpectedSource;
        if (!worldFiles.TryGetValue(expectedSource.Path, out RepositoryLoopClosureWorldFile? expectedFile)
            || expectedFile.Bytes != expectedSource.Bytes || expectedFile.SHA256 != expectedSource.SHA256)
            throw new InvalidDataException("repository task oracle source is not bound to the sealed world");
        RepositoryLoopClosureExpectedResult composedResult = Task.Oracle.DeriveExpectedResult(Task.Species, World);
        if (composedResult.Species != Task.Oracle.ExpectedResult.Species
            || composedResult.SHA256 != Task.Oracle.ExpectedResult.SHA256
            || !composedResult.Content.Span.SequenceEqual(Task.Oracle.ExpectedResult.Content.Span))
            throw new InvalidDataException("repository task oracle result is not composed from sealed world semantics");
        if (Task.Oracle.Prediction is { OtherPath: { Length: > 0 } otherPath }
            && !worldFiles.ContainsKey(otherPath))
            throw new InvalidDataException("repository typed oracle secondary source is not bound to the sealed world");
        if (Frontier.ObservedPaths.Any(path => !worldPaths.Contains(path)))
            throw new InvalidDataException("repository loop frontier observed path is outside the sealed world");
        foreach (RepositoryAccessEntry entry in Access.Entries)
            foreach (Tool.RepositoryPath path in entry.Paths)
                if (!worldPaths.Contains(path.Value)) throw new InvalidDataException("repository loop access entry is outside the sealed world");
        foreach (RepositoryLoopClosureAccessSource source in Access.Sources)
            if (!worldFiles.TryGetValue(source.Path, out RepositoryLoopClosureWorldFile? file)
                || file.Bytes != source.Bytes || file.SHA256 != source.SHA256)
                throw new InvalidDataException("repository loop access source authority diverges");
        RequirePatternCustody(Pattern.Occurrences.Select(static occurrence =>
            ("occurrence", occurrence.OccurrenceCheck.WorldSHA256, occurrence.OccurrenceCheck.AccessSHA256)));
        RequirePatternCustody(Pattern.Compositions.Select(static composition =>
            ("composition", composition.Receipt.WorldSHA256, composition.Receipt.AccessSHA256)));
        RequirePatternCustody(Pattern.Admissions.Select(static admission =>
            ("admission", admission.WorldSHA256, admission.AccessSHA256)));
        foreach (RepositoryCandidateTransition transition in Frontier.Transitions)
        {
            if (string.IsNullOrEmpty(transition.AccessSHA256)) continue;
            if (!Access.CarriesJournalRoot(transition.AccessSHA256))
                throw new InvalidDataException($"repository loop frontier transition for candidate {transition.CandidateDigest} stamps access root {transition.AccessSHA256}, which is not a prefix root of the sealed access journal of {Access.Entries.Count} entries");
            if (!Access.Entries.Any(entry => entry.CallSHA256 == transition.CallSHA256))
                throw new InvalidDataException($"repository loop frontier transition for candidate {transition.CandidateDigest} names call {transition.CallSHA256}, which is absent from the sealed access journal of {Access.Entries.Count} entries");
        }
    }

    /// Pattern receipts stamp the world digest and the access journal's root as of their own
    /// minting. Naming the receipt kind and the offending digest keeps "bound to another world"
    /// from meaning three unrelated drifts at once.
    private void RequirePatternCustody(IEnumerable<(string Kind, string WorldSHA256, string AccessSHA256)> receipts)
    {
        foreach ((string kind, string worldSHA256, string accessSHA256) in receipts)
        {
            if (worldSHA256 != World.WorldSHA256)
                throw new InvalidDataException($"repository loop pattern {kind} is bound to world {worldSHA256}, not the sealed world {World.WorldSHA256}");
            if (!Access.CarriesJournalRoot(accessSHA256))
                throw new InvalidDataException($"repository loop pattern {kind} stamps access root {accessSHA256}, which is not a prefix root of the sealed access journal of {Access.Entries.Count} entries");
        }
    }

    private void ValidateTaskPromptEvidence()
    {
        LoopLineageTapeEvent[] prompts = Tape.Events
            .Where(static eventRecord => eventRecord.Source == "repository-task-prompt")
            .ToArray();
        if (prompts.Length != 1)
            throw new InvalidDataException("repository task prompt custody is missing or duplicated");
        LoopLineageTapeEvent prompt = prompts[0];
        byte[] expectedPayload = Encoding.UTF8.GetBytes($"task={Task.TaskID}\tspecies={Task.Species}\tprompt={Task.Prompt}\n");
        if (prompt.EventID.Value >= Tape.SealEventID
            || prompt.Provenance != Provenances.Real
            || prompt.Roles != TapeEventRoles.GrammarInput
            || !prompt.Payload.Span.SequenceEqual(expectedPayload))
            throw new InvalidDataException("repository task prompt packet is not the exact registered prompt view");
        string expectedJournal = $"0\tingest\ts{prompt.EventID.Value}\trepository-task-prompt\t{expectedPayload.Length}B";
        if (Journal.Lines.Count(line => line == expectedJournal) != 1)
            throw new InvalidDataException("repository task prompt journal custody is missing or duplicated");
    }
}

/// The canonical lineage result plus the required shuffled-predecessor null. A
/// missing null remains explicit and can never be admitted by a trial-local count.
public readonly record struct RepositoryLoopClosureLineageResult(
    LoopLineageOccurrenceCheckResult Canonical,
    LoopClosureLineageNullOutcome ShuffledPredecessorNull)
{
    public void Validate(RepositoryLoopClosureAdjudicationInput input)
    {
        input.Tape.Validate();
        if (!Enum.IsDefined(Canonical.Status)) throw new InvalidDataException("repository loop canonical lineage status is unknown");
        ShuffledPredecessorNull?.Validate();
        LoopLineageAuthority authority = LoopLineageAuthority.Capture(input.Tape.LineageEdges);
        LoopLineageOccurrenceCheckResult expectedCanonical = LoopLineageVerifier.Verify(input.Tape.LineageEdges, input.Tape.Tape, authority);
        if (Canonical != expectedCanonical)
            throw new InvalidDataException("repository loop canonical lineage result diverges from sealed tape");
        if (Canonical.Status == LoopLineageOccurrenceCheckStatuses.PASS && Canonical.LineageSHA256 is not { Length: 64 })
            throw new InvalidDataException("repository loop canonical lineage pass omits its digest");
        if (ShuffledPredecessorNull is LoopClosureLineageNullExecuted executed)
        {
            LoopLineageAdjudication expected = LoopLineageVerifier.VerifyShuffledPredecessorNull(
                input.Tape.Tape, input.Tape.LineageEdges, input.Journal.Lines, input.Registration.LineageNullSpec.Domain);
            if (!SameNull(executed.Receipt, expected.NullReceipt))
                throw new InvalidDataException("repository loop shuffled predecessor null diverges from sealed tape");
            if (executed.Receipt.SourceAuthoritySHA256 != input.EvidenceAuthority.LineageAuthoritySHA256
                || executed.Receipt.SourceJournalSHA256 != input.EvidenceAuthority.JournalSHA256)
                throw new InvalidDataException("repository loop shuffled predecessor null is not bound to sealed evidence authority");
        }
    }

    public bool NullDiscriminates
        => ShuffledPredecessorNull is LoopClosureLineageNullExecuted executed
            && executed.Receipt.OriginalStatus == LoopLineageOccurrenceCheckStatuses.PASS
            && executed.Receipt.ShuffledStatus == LoopLineageOccurrenceCheckStatuses.FAIL;

    private static bool SameNull(LoopLineageShuffledNullReceipt left, LoopLineageShuffledNullReceipt right)
        => left.SourceAuthoritySHA256 == right.SourceAuthoritySHA256
            && left.SourceTapeSHA256 == right.SourceTapeSHA256
            && left.SourceJournalSHA256 == right.SourceJournalSHA256
            && left.EventCount == right.EventCount && left.EdgeCount == right.EdgeCount
            && left.EligibleBucketCount == right.EligibleBucketCount && left.PermutationSeed == right.PermutationSeed
            && left.PermutationSHA256 == right.PermutationSHA256 && left.SwappedEdgeCount == right.SwappedEdgeCount
            && left.Derangement == right.Derangement && left.SameEvents == right.SameEvents
            && left.SamePayloads == right.SamePayloads && left.OriginalLineageSHA256 == right.OriginalLineageSHA256
            && left.OriginalStatus == right.OriginalStatus && left.ShuffledLineageSHA256 == right.ShuffledLineageSHA256
            && left.ShuffledStatus == right.ShuffledStatus && left.FirstDiscriminatingEdge == right.FirstDiscriminatingEdge;
}

/// The ordinary occurrenceCheck event evaluates the hidden task oracle for every
/// task species. Only a typed-prediction oracle carries a prediction receipt.
public sealed class RepositoryLoopClosureTaskOccurrenceCheck
{
    public RepositoryLoopClosureTaskOccurrenceCheck(
        RepositoryLoopClosureTaskOracleModes mode,
        RepositoryOccurrenceCheckOutcomes outcome,
        string oracleSHA256,
        RepositoryPrediction? prediction,
        RepositoryOccurrenceCheckReceipt? typedPredictionReceipt,
        string worldSHA256,
        string accessSHA256,
        long evaluatorCost,
        long accessCost,
        long accessSequence,
        string accessEntrySHA256,
        int accessEntryCount,
        TapeEventID predecessorEventID,
        string callSHA256,
        string evidenceSHA256)
    {
        Mode = mode; Outcome = outcome; OracleSHA256 = oracleSHA256; Prediction = prediction;
        TypedPredictionReceipt = typedPredictionReceipt; WorldSHA256 = worldSHA256; AccessSHA256 = accessSHA256;
        EvaluatorCost = evaluatorCost; AccessCost = accessCost; AccessSequence = accessSequence;
        AccessEntrySHA256 = accessEntrySHA256; AccessEntryCount = accessEntryCount;
        PredecessorEventID = predecessorEventID; CallSHA256 = callSHA256;
        EvidenceSHA256 = string.IsNullOrWhiteSpace(evidenceSHA256) ? ComputeEvidenceSHA256() : evidenceSHA256;
        ReceiptSHA256 = ComputeReceiptSHA256();
    }

    public RepositoryLoopClosureTaskOracleModes Mode { get; }
    public RepositoryOccurrenceCheckOutcomes Outcome { get; }
    public string OracleSHA256 { get; }
    public RepositoryPrediction? Prediction { get; }
    public RepositoryOccurrenceCheckReceipt? TypedPredictionReceipt { get; }
    public string WorldSHA256 { get; }
    public string AccessSHA256 { get; }
    public long EvaluatorCost { get; }
    public long AccessCost { get; }
    public long AccessSequence { get; }
    public string AccessEntrySHA256 { get; }
    public int AccessEntryCount { get; }
    public TapeEventID PredecessorEventID { get; }
    public string CallSHA256 { get; }
    public string EvidenceSHA256 { get; }
    public string ReceiptSHA256 { get; }

    public void Validate(RepositoryLoopClosureAdjudicationInput input, TapeEventID actionEventID, RepositoryCandidate candidate)
    {
        // AccessSHA256 is the access JOURNAL's root as of this occurrenceCheck, not the access
        // snapshot digest — the two are different formulas over the same entries.
        if (!input.Access.CarriesJournalRoot(AccessSHA256))
            throw new InvalidDataException($"repository task occurrence check stamps access root {AccessSHA256}, which is not a prefix root of the sealed access journal of {input.Access.Entries.Count} entries");
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Outcome)
            || Mode != input.Task.Oracle.Mode
            || OracleSHA256 != input.Task.Oracle.AuthoritySHA256 || !IsSHA(OracleSHA256)
            || WorldSHA256 != input.World.ContentSHA256
            || EvaluatorCost < 0 || AccessCost < 0 || AccessEntryCount != input.Access.Entries.Count
            || PredecessorEventID != actionEventID || !IsSHA(CallSHA256) || !IsSHA(EvidenceSHA256)
            || EvidenceSHA256 != ComputeEvidenceSHA256()
            || !IsSHA(ReceiptSHA256) || ReceiptSHA256 != ComputeReceiptSHA256())
            throw new InvalidDataException("repository task oracle occurrence check is malformed");
        string expectedCandidateCallSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)));
        if (CallSHA256 != expectedCandidateCallSHA256)
            throw new InvalidDataException("repository task occurrence check call is not the selected candidate call");
        if (input.Task.Species == RepositoryLoopClosureTaskSpecies.Diagnosis
            && Outcome != RepositoryOccurrenceCheckOutcomes.Unobserved
            && Prediction is null)
            throw new InvalidDataException("repository diagnosis occurrence check omits its typed prediction before access binding");
        bool answerTask = input.Task.Species == RepositoryLoopClosureTaskSpecies.Answer;
        if (answerTask)
        {
            // Answer is a terminal response, not a repository access.  A
            // fabricated access row would make an answer look source-backed.
            if (AccessSequence != -1 || AccessEntrySHA256.Length != 0 || AccessCost != 0 || EvaluatorCost != 0)
                throw new InvalidDataException("repository answer occurrence check fabricates access or evaluator work");
        }
        else if (Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved)
        {
            if (AccessSequence != -1 || AccessEntrySHA256.Length != 0)
                throw new InvalidDataException("repository task unobserved oracle carries access authority");
        }
        else if (AccessSequence < 0 || AccessSequence >= AccessEntryCount
            || !IsSHA(AccessEntrySHA256)
            || !input.Access.Entries.Any(entry => entry.Sequence == AccessSequence
                && entry.EntrySHA256 == AccessEntrySHA256
                && (input.Task.Species == RepositoryLoopClosureTaskSpecies.Diagnosis
                    ? Prediction is { } accessPrediction && entry.Paths.Any(path => path.Value == accessPrediction.Path)
                    : entry.Verb == candidate.Verb && entry.Argument == candidate.Argument)
                && (input.Task.Species == RepositoryLoopClosureTaskSpecies.Diagnosis
                    || entry.CallSHA256 == CallSHA256)
                && CallSHA256 == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)))))
            throw new InvalidDataException("repository task oracle access authority is absent");
        if (Mode == RepositoryLoopClosureTaskOracleModes.TypedPrediction)
        {
            if (Prediction is not { } prediction || input.Task.Oracle.Prediction is not { } expectedPrediction || prediction != expectedPrediction
                || Outcome != RepositoryOccurrenceCheckOutcomes.Unobserved && TypedPredictionReceipt is not { })
                throw new InvalidDataException("repository task typed oracle occurrence check omits its prediction receipt");
            if (Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved && TypedPredictionReceipt is not null)
                throw new InvalidDataException("repository unobserved typed oracle occurrence check fabricates a prediction receipt");
            if (TypedPredictionReceipt is not { } typedReceipt) return;
            typedReceipt.Validate();
            if (typedReceipt.Prediction != prediction || typedReceipt.WorldSHA256 != WorldSHA256 || typedReceipt.AccessSHA256 != AccessSHA256
                || typedReceipt.Outcome != Outcome || typedReceipt.EvaluatorCost != EvaluatorCost || typedReceipt.AccessCost != AccessCost
                || typedReceipt.AccessSequence != AccessSequence || typedReceipt.AccessEntrySHA256 != AccessEntrySHA256
                || typedReceipt.AccessEntryCount != AccessEntryCount || typedReceipt.CallSHA256 != CallSHA256
                || !input.Tape.Events.Any(eventRecord => eventRecord.EventID == typedReceipt.PredecessorEventID)
                || !input.Journal.Lines.Any(line => line.Contains($"\t{typedReceipt.PredecessorEventID}\t", StringComparison.Ordinal)))
                throw new InvalidDataException("repository task typed prediction receipt diverges from oracle evaluation");
            // Frozen tape source and field tokens; identifier-side names are OccurrenceCheck and Prediction.
            byte[] typedPayload = Encoding.UTF8.GetBytes($"REPOSITORY-VERIFICATION\tstep={typedReceipt.Step}\tspecies={typedReceipt.Prediction.Species}\tclaim={typedReceipt.Prediction.Canonical}\toutcome={typedReceipt.Outcome}\tworld={typedReceipt.WorldSHA256}\taccess={typedReceipt.AccessSHA256}\taccess-sequence={typedReceipt.AccessSequence}\taccess-entry-sha256={typedReceipt.AccessEntrySHA256}\taccess-entry-count={typedReceipt.AccessEntryCount}\tclaim-sha256={typedReceipt.PredictionSHA256}\tevidence={typedReceipt.EvidenceSHA256}\tevaluator-cost={typedReceipt.EvaluatorCost}\taccess-cost={typedReceipt.AccessCost}\tpredecessor={typedReceipt.PredecessorEventID.Value}\tcall={typedReceipt.CallSHA256}\treceipt={typedReceipt.ReceiptSHA256}");
            // Frozen tape source token; identifier-side name is OccurrenceCheck.
            LoopLineageTapeEvent[] typedEvents = input.Tape.Events
                .Where(eventRecord => eventRecord.Source == "repository:verification")
                .Where(eventRecord => eventRecord.Payload.Span.SequenceEqual(typedPayload))
                .ToArray();
            if (typedEvents.Length != 1
                || typedEvents[0].Provenance != Provenances.Execution
                || typedEvents[0].Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                // Frozen journal source and row kind; identifier-side name is OccurrenceCheck.
                || !input.Journal.Lines.Any(line => line.StartsWith($"{typedReceipt.Step}\trepository-verification\t{typedEvents[0].EventID}\t", StringComparison.Ordinal)
                    && line.EndsWith($"\t{typedPayload.Length}B", StringComparison.Ordinal)))
            throw new InvalidDataException("repository task typed prediction receipt packet custody is absent");
        }
        else if (Prediction is not null || TypedPredictionReceipt is not null)
            throw new InvalidDataException("repository source oracle occurrence check carries typed prediction custody");
    }

    private string ComputeReceiptSHA256()
    {
        // Frozen digest prefix; identifier-side name is OccurrenceCheck.
        string canonical = string.Join('|', "repository-loop-task-verification-v1", Mode, Outcome,
            OracleSHA256, Prediction?.Canonical ?? "none", TypedPredictionReceipt?.ReceiptSHA256 ?? "none",
            WorldSHA256, AccessSHA256, EvaluatorCost, AccessCost, AccessSequence, AccessEntrySHA256,
            AccessEntryCount, PredecessorEventID.Value, CallSHA256, EvidenceSHA256);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private string ComputeEvidenceSHA256()
    {
        string canonical = string.Join('|', "repository-loop-task-oracle-evidence-v1", Mode, Outcome,
            OracleSHA256, Prediction?.Canonical ?? "none", TypedPredictionReceipt?.ReceiptSHA256 ?? "none",
            WorldSHA256, AccessSHA256, EvaluatorCost, AccessCost, AccessSequence, AccessEntrySHA256,
            AccessEntryCount, CallSHA256);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// A source-backed real repository task. Candidate/frontier/access/source/result
/// facts are the organism's outcome evidence; the hidden oracle only adjudicates them.
public sealed class RepositoryLoopClosureTaskOutcome
{
    public RepositoryLoopClosureTaskOutcome(
        string taskID,
        RepositoryCandidate candidate,
        RepositoryFrontierRevision frontierRevision,
        string frontierAuthoritySHA256,
        RepositoryFrontierRevision selectionRevision,
        string selectionFrontierAuthoritySHA256,
        long selectionOrdinal,
        TapeEventID selectionEventID,
        string selectionReceiptSHA256,
        TapeEventID actionEventID,
        TapeEventID occurrenceCheckEventID,
        TapeEventID outcomeEventID,
        TapeEventID outcomePredecessorEventID,
        string actionPayloadSHA256,
        string occurrenceCheckPayloadSHA256,
        string outcomePayloadSHA256,
        string sourcePath,
        long sourceBytes,
        string sourceSHA256,
        int sourceLine,
        RepositoryLoopClosureResultSpecies resultSpecies,
        ReadOnlyMemory<byte> resultContent,
        RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck,
        string outcomeSHA256)
    {
        TaskID = taskID;
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        FrontierRevision = frontierRevision;
        FrontierAuthoritySHA256 = frontierAuthoritySHA256;
        SelectionRevision = selectionRevision;
        SelectionFrontierAuthoritySHA256 = selectionFrontierAuthoritySHA256;
        SelectionOrdinal = selectionOrdinal;
        SelectionEventID = selectionEventID;
        SelectionReceiptSHA256 = selectionReceiptSHA256;
        ActionEventID = actionEventID;
        OccurrenceCheckEventID = occurrenceCheckEventID;
        OutcomeEventID = outcomeEventID;
        OutcomePredecessorEventID = outcomePredecessorEventID;
        ActionPayloadSHA256 = actionPayloadSHA256;
        OccurrenceCheckPayloadSHA256 = occurrenceCheckPayloadSHA256;
        OutcomePayloadSHA256 = outcomePayloadSHA256;
        SourcePath = sourcePath;
        SourceBytes = sourceBytes;
        SourceSHA256 = sourceSHA256;
        SourceLine = sourceLine;
        ResultSpecies = resultSpecies;
        ResultContent = resultContent.ToArray();
        ResultSHA256 = Convert.ToHexStringLower(SHA256.HashData(ResultContent.Span));
        OccurrenceCheck = occurrenceCheck ?? throw new ArgumentNullException(nameof(occurrenceCheck));
        OutcomeSHA256 = outcomeSHA256;
    }

    public string TaskID { get; }
    public RepositoryCandidate Candidate { get; }
    public RepositoryFrontierRevision FrontierRevision { get; }
    public string FrontierAuthoritySHA256 { get; }
    public RepositoryFrontierRevision SelectionRevision { get; }
    public string SelectionFrontierAuthoritySHA256 { get; }
    public long SelectionOrdinal { get; }
    public TapeEventID SelectionEventID { get; }
    public string SelectionReceiptSHA256 { get; }
    public RepositoryLoopClosureTaskOccurrenceCheck OccurrenceCheck { get; }
    public TapeEventID ActionEventID { get; }
    public TapeEventID OccurrenceCheckEventID { get; }
    public TapeEventID OutcomeEventID { get; }
    public TapeEventID OutcomePredecessorEventID { get; }
    public string ActionPayloadSHA256 { get; }
    public string OccurrenceCheckPayloadSHA256 { get; }
    public string OutcomePayloadSHA256 { get; }
    public string SourcePath { get; }
    public long SourceBytes { get; }
    public string SourceSHA256 { get; }
    public int SourceLine { get; }
    public RepositoryLoopClosureResultSpecies ResultSpecies { get; }
    public ReadOnlyMemory<byte> ResultContent { get; }
    public string ResultSHA256 { get; }
    public string OutcomeSHA256 { get; }
    public long AccessSequence => OccurrenceCheck.AccessSequence;
    public string AccessEntrySHA256 => OccurrenceCheck.AccessEntrySHA256;
    public string AccessSHA256 => OccurrenceCheck.AccessSHA256;
    public RepositoryLoopClosureTaskOutcomeSpecies Species
        => OccurrenceCheck is { } receipt
            ? receipt.Outcome switch
            {
                RepositoryOccurrenceCheckOutcomes.Confirmed => RepositoryLoopClosureTaskOutcomeSpecies.Confirmed,
                RepositoryOccurrenceCheckOutcomes.Refuted => RepositoryLoopClosureTaskOutcomeSpecies.Refuted,
                _ => RepositoryLoopClosureTaskOutcomeSpecies.Unobserved,
            }
            : RepositoryLoopClosureTaskOutcomeSpecies.Unobserved;
    public bool IsSourceBacked => IsSHA(ResultSHA256) && IsSHA(SourceSHA256) && !string.IsNullOrWhiteSpace(SourcePath);

    public bool IsBeforeSeal(RepositoryLoopClosureTapeSnapshot tape)
        => ActionEventID.Value < tape.SealEventID
            && OccurrenceCheckEventID.Value < tape.SealEventID
            && OutcomeEventID.Value < tape.SealEventID;

    public void Validate(RepositoryLoopClosureAdjudicationInput input)
    {
        input.Task.Validate();
        if (string.IsNullOrWhiteSpace(TaskID) || !Candidate.Digest.IsValid
            || !FrontierRevision.IsValid || !IsSHA(FrontierAuthoritySHA256)
            || !SelectionRevision.IsValid || !IsSHA(SelectionFrontierAuthoritySHA256) || SelectionOrdinal < 0
            || SelectionEventID.Value <= 0 || !IsSHA(SelectionReceiptSHA256)
            || ActionEventID.Value <= 0 || OccurrenceCheckEventID.Value <= ActionEventID.Value
            || OutcomeEventID.Value <= OccurrenceCheckEventID.Value || OutcomePredecessorEventID != OccurrenceCheckEventID
            || !IsSHA(ActionPayloadSHA256) || !IsSHA(OccurrenceCheckPayloadSHA256) || !IsSHA(OutcomePayloadSHA256)
            || SourceBytes < 0 || SourceLine < 0 || !Enum.IsDefined(ResultSpecies)
            || !IsSHA(OutcomeSHA256)
            || (OccurrenceCheck.Outcome != RepositoryOccurrenceCheckOutcomes.Unobserved
                && (!IsSHA(SourceSHA256)
                    || ((input.Task.Species is RepositoryLoopClosureTaskSpecies.Trace or RepositoryLoopClosureTaskSpecies.Read
                        or RepositoryLoopClosureTaskSpecies.Diagnosis) && SourceLine < 1)
                    || ResultContent.Length == 0))
            || (OccurrenceCheck.Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved
                && (SourcePath.Length != 0 || SourceBytes != 0 || SourceSHA256.Length != 0 || ResultContent.Length != 0))
            || !IsSHA(ResultSHA256)
            || !IsBeforeSeal(input.Tape) || TaskID != input.Task.TaskID
            || !input.Frontier.Transitions.Any(transition => transition.CandidateDigest == Candidate.Digest
                && transition.CandidateCanonical == Candidate.Canonical)
            || FrontierRevision != input.Frontier.Revision || FrontierAuthoritySHA256 != input.Frontier.RuntimeAuthoritySHA256
            || !input.Frontier.Selections.Any(selection => selection.Revision == SelectionRevision
                && selection.RuntimeAuthoritySHA256 == SelectionFrontierAuthoritySHA256
                && selection.Ordinal == SelectionOrdinal && selection.SelectionEventID == SelectionEventID
                && selection.CandidateDigest == Candidate.Digest && selection.CandidateCanonical == Candidate.Canonical)
            || !input.Tape.Events.Any(eventRecord => eventRecord.EventID == SelectionEventID
                && eventRecord.EventID.Value < ActionEventID.Value)
            || !input.Journal.Rows.Any(row => row.EventID == SelectionEventID && row.Source == "repository-selection")
            || !MatchesSelectionReceipt(input, SelectionEventID, SelectionRevision, SelectionFrontierAuthoritySHA256,
                SelectionOrdinal, Candidate, SelectionReceiptSHA256)
            || !MatchesCandidate(input.Task.Species, Candidate.Species)
            || !MatchesResult(input.Task.Species, ResultSpecies)
            || (OccurrenceCheck.Outcome != RepositoryOccurrenceCheckOutcomes.Unobserved
                && (!input.World.Files.Any(file => file.Path.Value == SourcePath && file.Bytes == SourceBytes && file.SHA256 == SourceSHA256)
                    || !input.Access.Entries.Any(entry => entry.Sequence == OccurrenceCheck.AccessSequence
                        && entry.Paths.Any(path => path.Value == SourcePath)
                        && (SourceLine == 0 || entry.Loci.Any(locus => locus.Path.Value == SourcePath && locus.Line == SourceLine)))))
            || !TapeContains(input.Tape, input.Journal, ActionEventID, ActionPayloadSHA256, "repository-action")
            // Frozen tape source token; identifier-side name is OccurrenceCheck.
            || !TapeContains(input.Tape, input.Journal, OccurrenceCheckEventID, OccurrenceCheckPayloadSHA256, "repository-verification")
            || !TapeContains(input.Tape, input.Journal, OutcomeEventID, OutcomePayloadSHA256, "repository-outcome"))
            throw new InvalidDataException("repository loop task outcome is malformed");
        OccurrenceCheck.Validate(input, ActionEventID, Candidate);
        if (OccurrenceCheck.Outcome == RepositoryOccurrenceCheckOutcomes.Confirmed
            && (SourcePath != input.Task.Oracle.ExpectedSource.Path
                || SourceBytes != input.Task.Oracle.ExpectedSource.Bytes
                || SourceSHA256 != input.Task.Oracle.ExpectedSource.SHA256
                || SourceLine != input.Task.Oracle.ExpectedSourceLine
                || ResultSpecies != input.Task.Oracle.ExpectedResult.Species
                || ResultSHA256 != input.Task.Oracle.ExpectedResult.SHA256
                || !ResultContent.Span.SequenceEqual(input.Task.Oracle.ExpectedResult.Content.Span)))
            throw new InvalidDataException("repository task confirmed result does not satisfy hidden source oracle");
        RepositoryLoopTaskActionReceipt actionReceipt = RepositoryLoopTaskActionReceipt.Create(
            TaskID, input.Task.Species, input.Task.AuthoritySHA256, SelectionEventID, SelectionReceiptSHA256, checked((int)SelectionOrdinal), Candidate,
            SelectionRevision, SelectionFrontierAuthoritySHA256);
        byte[] actionPayload = actionReceipt.Encode();
        RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheckReceipt = RepositoryLoopTaskOccurrenceCheckReceipt.Create(
            TaskID, input.Task.Species, OccurrenceCheck, ActionEventID, ActionPayloadSHA256, input.Task.AuthoritySHA256);
        byte[] occurrenceCheckPayload = occurrenceCheckReceipt.Encode();
        RepositoryLoopTaskOutcomeReceipt outcomeReceipt = RepositoryLoopTaskOutcomeReceipt.Create(
            TaskID, input.Task.Species, OccurrenceCheck.Outcome, OccurrenceCheckEventID, OccurrenceCheckPayloadSHA256, Candidate, ResultSpecies,
            SourcePath, SourceLine, SourceBytes, SourceSHA256, ResultContent, input.Task.AuthoritySHA256);
        byte[] outcomePayload = outcomeReceipt.Encode();
        if (!HasPayload(input.Tape, ActionEventID, actionPayload, ActionPayloadSHA256)
            || !HasPayload(input.Tape, OccurrenceCheckEventID, occurrenceCheckPayload, OccurrenceCheckPayloadSHA256)
            || !HasPayload(input.Tape, OutcomeEventID, outcomePayload, OutcomePayloadSHA256)
            || !HasRole(input.Tape, ActionEventID, "repository-action")
            // Frozen tape source token; identifier-side name is OccurrenceCheck.
            || !HasRole(input.Tape, OccurrenceCheckEventID, "repository-verification")
            || !HasRole(input.Tape, OutcomeEventID, "repository-outcome")
            || ResultSHA256 != Convert.ToHexStringLower(SHA256.HashData(ResultContent.Span)))
            throw new InvalidDataException("repository loop task event roles or result authority diverge");
        if (ComputeOutcomeSHA256(this, input.Task.Species) != OutcomeSHA256)
            throw new InvalidDataException("repository loop task outcome digest diverges");
    }

    /// Seal an outcome whose digest is COMPUTED from its own fields. An assembler that
    /// reconstructs the outcome from sealed packets must not restate the digest formula,
    /// or a drift between the formula here and the one Validate applies would be invisible.
    public static RepositoryLoopClosureTaskOutcome Create(
        string taskID,
        RepositoryLoopClosureTaskSpecies taskSpecies,
        RepositoryCandidate candidate,
        RepositoryFrontierRevision frontierRevision,
        string frontierAuthoritySHA256,
        RepositoryFrontierRevision selectionRevision,
        string selectionFrontierAuthoritySHA256,
        long selectionOrdinal,
        TapeEventID selectionEventID,
        string selectionReceiptSHA256,
        TapeEventID actionEventID,
        TapeEventID occurrenceCheckEventID,
        TapeEventID outcomeEventID,
        TapeEventID outcomePredecessorEventID,
        string actionPayloadSHA256,
        string occurrenceCheckPayloadSHA256,
        string outcomePayloadSHA256,
        string sourcePath,
        long sourceBytes,
        string sourceSHA256,
        int sourceLine,
        RepositoryLoopClosureResultSpecies resultSpecies,
        ReadOnlyMemory<byte> resultContent,
        RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck)
    {
        RepositoryLoopClosureTaskOutcome outcome = new(taskID, candidate, frontierRevision, frontierAuthoritySHA256,
            selectionRevision, selectionFrontierAuthoritySHA256, selectionOrdinal, selectionEventID, selectionReceiptSHA256,
            actionEventID, occurrenceCheckEventID, outcomeEventID, outcomePredecessorEventID,
            actionPayloadSHA256, occurrenceCheckPayloadSHA256, outcomePayloadSHA256,
            sourcePath, sourceBytes, sourceSHA256, sourceLine, resultSpecies, resultContent, occurrenceCheck, "");
        return new(taskID, candidate, frontierRevision, frontierAuthoritySHA256,
            selectionRevision, selectionFrontierAuthoritySHA256, selectionOrdinal, selectionEventID, selectionReceiptSHA256,
            actionEventID, occurrenceCheckEventID, outcomeEventID, outcomePredecessorEventID,
            actionPayloadSHA256, occurrenceCheckPayloadSHA256, outcomePayloadSHA256,
            sourcePath, sourceBytes, sourceSHA256, sourceLine, resultSpecies, resultContent, occurrenceCheck,
            ComputeOutcomeSHA256(outcome, taskSpecies));
    }

    private static string ComputeOutcomeSHA256(
        RepositoryLoopClosureTaskOutcome outcome,
        RepositoryLoopClosureTaskSpecies taskSpecies)
    {
        string canonical = string.Join('|', "repository-loop-task-v4", outcome.TaskID, taskSpecies,
            outcome.Candidate.Canonical, outcome.Candidate.Digest.Value, outcome.FrontierRevision.Value, outcome.FrontierAuthoritySHA256,
            outcome.SelectionRevision.Value, outcome.SelectionFrontierAuthoritySHA256, outcome.SelectionOrdinal, outcome.SelectionEventID.Value,
            outcome.SelectionReceiptSHA256,
            outcome.OccurrenceCheck.ReceiptSHA256,
            outcome.ActionEventID.Value, outcome.OccurrenceCheckEventID.Value, outcome.OutcomeEventID.Value, outcome.OutcomePredecessorEventID.Value,
            outcome.ActionPayloadSHA256, outcome.OccurrenceCheckPayloadSHA256, outcome.OutcomePayloadSHA256,
            outcome.SourcePath, outcome.SourceLine, outcome.SourceBytes, outcome.SourceSHA256, outcome.ResultSpecies, outcome.ResultSHA256);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool MatchesCandidate(RepositoryLoopClosureTaskSpecies species, RepositoryCandidateSpecies candidate)
        => RepositoryLoopTaskSpeciesRules.MatchesCandidate(species, candidate);

    private static bool MatchesResult(RepositoryLoopClosureTaskSpecies species, RepositoryLoopClosureResultSpecies result)
        => RepositoryLoopTaskSpeciesRules.MatchesResult(species, result);

    private static bool TapeContains(RepositoryLoopClosureTapeSnapshot tape,
        RepositoryLoopClosureJournalSnapshot journal, TapeEventID eventID, string payloadSHA256, string source)
    {
        bool found = false;
        for (int index = 0; index < tape.Events.Count; index++)
            if (tape.Events[index].EventID == eventID)
            {
                if (found) throw new InvalidDataException("repository loop event identity repeats");
                found = true;
                if (Convert.ToHexStringLower(SHA256.HashData(tape.Events[index].Payload.Span)) != payloadSHA256)
                    return false;
            }
        return found && journal.Rows.Any(row => row.EventID == eventID && row.Source == source);
    }

    private static bool MatchesSelectionReceipt(
        RepositoryLoopClosureAdjudicationInput input,
        TapeEventID selectionEventID,
        RepositoryFrontierRevision revision,
        string frontierAuthoritySHA256,
        long ordinal,
        RepositoryCandidate candidate,
        string receiptSHA256)
    {
        if (!input.Tape.Events.Any(eventRecord => eventRecord.EventID == selectionEventID
                && eventRecord.Source == "repository-selection"
                && eventRecord.Provenance == Provenances.Execution
                && eventRecord.Roles == (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)))
            return false;
        LoopLineageTapeEvent eventRecord = input.Tape.Events.Single(eventValue => eventValue.EventID == selectionEventID);
        if (!RepositorySelectionReceipt.TryDecode(eventRecord.Payload.Span, out RepositorySelectionReceipt receipt))
            return false;
        if (!receipt.PolicyID.Equals(RepositoryPolicyBoundaryDomain.Instance.PolicyID)
            || receipt.DecisionEventID.Value >= selectionEventID.Value)
            return false;
        if (!input.Tape.Events.Any(eventValue => eventValue.EventID == receipt.DecisionEventID
                && eventValue.Source == "policy:" + receipt.PolicyID.Value
                && eventValue.Provenance == Provenances.Execution
                && eventValue.Roles == (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)))
            return false;
        LoopLineageTapeEvent decisionEvent = input.Tape.Events.Single(eventValue => eventValue.EventID == receipt.DecisionEventID);
        if (!TapePacketCreator.TryDecodePolicyDecision(decisionEvent.Payload.Span, out CortexPolicyDecisionPacket decisionPacket))
            return false;
        if (decisionPacket.DecisionID != receipt.DecisionID
            || decisionPacket.Readout.ReadoutFingerprint != receipt.ReadoutFingerprint
            || decisionPacket.Readout.ReadoutCandidateFingerprint != receipt.ReadoutCandidateFingerprint
            || Convert.ToHexStringLower(SHA256.HashData(decisionEvent.Payload.Span)) != receipt.DecisionPayloadSHA256)
            return false;
        return receipt.ReceiptSHA256 == receiptSHA256
            && receipt.FrontierRevision == revision
            && receipt.FrontierAuthoritySHA256 == frontierAuthoritySHA256
            && receipt.SelectionOrdinal == ordinal
            && receipt.CandidateSpecies == candidate.Species
            && receipt.CandidateCanonical == candidate.Canonical
            && receipt.CandidateDigest == candidate.Digest
            && input.Journal.Rows.Any(row => row.EventID == receipt.DecisionEventID
                && row.Source == "policy:" + receipt.PolicyID.Value && row.Step == receipt.Step)
            && input.Journal.Rows.Any(row => row.EventID == selectionEventID
                && row.Source == "repository-selection" && row.Step == receipt.Step);
    }

    private static bool HasPayload(RepositoryLoopClosureTapeSnapshot tape, TapeEventID eventID, ReadOnlySpan<byte> expected, string payloadSHA256)
    {
        for (int index = 0; index < tape.Events.Count; index++)
            if (tape.Events[index].EventID == eventID)
                return tape.Events[index].Payload.Span.SequenceEqual(expected)
                    && Convert.ToHexStringLower(SHA256.HashData(expected)) == payloadSHA256;
        return false;
    }

    private static bool HasRole(RepositoryLoopClosureTapeSnapshot tape, TapeEventID eventID, string source)
    {
        for (int index = 0; index < tape.Events.Count; index++)
            if (tape.Events[index].EventID == eventID)
                return tape.Events[index].Source == source
                    && tape.Events[index].Provenance == Provenances.Execution
                    && tape.Events[index].Roles == TapeEventRoles.AuditOnly;
        return false;
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// Repository-specific custody for one of the five causal links. The historical
/// link contract remains useful for generic gates, but this wrapper prevents a
/// generic prefix from being mistaken for repository evidence.
public readonly record struct RepositoryLoopClosureLinkEvidence(
    string RecordID,
    LoopClosureLinkSpecies Species,
    LoopClosureLinkPaths Path,
    LoopClosureLinkStates State,
    TapeEventID EventID,
    string PayloadSHA256,
    string EvidenceSHA256,
    string PredecessorEvidenceSHA256,
    string LineageSHA256,
    string JournalSHA256,
    LoopLineageNodeSpecies NodeSpecies,
    RepositoryCandidateSpecies CandidateSpecies,
    RepositoryCandidateDigest CandidateDigest,
    string CandidateCanonical,
    string SourcePath,
    int SourceLine,
    long SourceBytes,
    string SourceSHA256,
    long AccessSequence,
    Tool.ToolVerbs ToolVerb)
{
    // The causal event is the gate's own observation.  The repository:loop-link
    // packet is a second event carrying the attempt declaration; it is never an
    // alias for EventID.  Keep the attempt projection explicit so every field in
    // LoopClosureLinkAttempt remains adjudicable after the report is detached
    // from the live runner.
    public string RunID { get; init; } = "";
    public int Step { get; init; } = -1;
    public string AttemptEvidenceSHA256 { get; init; } = "";
    public string AttemptJournalSHA256 { get; init; } = "";
    public long AttemptPredecessorEventID { get; init; }
    public string AttemptPredecessorEvidenceSHA256 { get; init; } = "";
    public LoopClosureGateDenialReasons DenialReason { get; init; }
    public bool HasDenialReason { get; init; }
    public LoopClosureQuotaID QuotaID { get; init; }
    public LoopClosureDigest ForkReceiptSHA256 { get; init; }
    public LoopClosureDigest DivergenceEvidenceSHA256 { get; init; }
    public Grammar.GrammarRevisionID GrammarRevision { get; init; }
    public LoopClosureDigest AttemptSHA256 { get; init; }
    public LoopClosureDigest PredecessorAttemptSHA256 { get; init; }
    public string AttemptEvidenceRunID { get; init; } = "";
    public string AttemptEvidenceRelativePath { get; init; } = "";
    public LoopClosureDigest AttemptEvidenceAuthoritySHA256 { get; init; }
    public LoopClosureDigest AttemptEvidenceRailSHA256 { get; init; }
    public TapeEventID LinkEventID { get; init; }
    public string LinkPacketSHA256 { get; init; } = "";
    public string LinkJournalSHA256 { get; init; } = "";

    /// Rehydrates the complete durable attempt declaration from the report
    /// projection.  EventID remains the causal event; LinkEventID remains the
    /// packet event.  This is deliberately not a compatibility alias.
    public LoopClosureLinkAttempt ToLinkAttempt()
    {
        if (AttemptEvidenceSHA256 != PayloadSHA256)
            throw new InvalidDataException("repository link attempt evidence digest diverges from causal payload digest");
        return new(
            RecordID, RunID, Species, Path, State, Step, EventID,
            new(AttemptEvidenceSHA256), AttemptPredecessorEventID,
            new(AttemptPredecessorEvidenceSHA256), new(AttemptJournalSHA256), DenialReason,
            HasDenialReason, QuotaID, ForkReceiptSHA256, DivergenceEvidenceSHA256,
            GrammarRevision, AttemptSHA256, PredecessorAttemptSHA256,
            AttemptEvidenceRunID, AttemptEvidenceRelativePath,
            AttemptEvidenceAuthoritySHA256, AttemptEvidenceRailSHA256, ChildOutcome,
            LinkEventID, new(LinkPacketSHA256), new(LinkJournalSHA256));
    }

    /// Bind a typed attempt to the repository evidence fields without touching
    /// the evidence receipt digest until all fields have been projected.
    public RepositoryLoopClosureLinkEvidence BindLinkAttempt(in LoopClosureLinkAttempt attempt)
    {
        attempt.Validate();
        RepositoryLoopClosureLinkEvidence bound = this with
        {
            RunID = attempt.RunID,
            Step = attempt.Step,
            EventID = attempt.EventID,
            PayloadSHA256 = attempt.EvidenceSHA256.Value,
            AttemptEvidenceSHA256 = attempt.EvidenceSHA256.Value,
            AttemptPredecessorEventID = attempt.PredecessorEventID,
            AttemptPredecessorEvidenceSHA256 = attempt.PredecessorEvidenceSHA256.Value ?? "",
            AttemptJournalSHA256 = attempt.JournalSHA256.Value ?? "",
            DenialReason = attempt.DenialReason,
            HasDenialReason = attempt.HasDenialReason,
            QuotaID = attempt.QuotaID,
            ForkReceiptSHA256 = attempt.ForkReceiptSHA256,
            DivergenceEvidenceSHA256 = attempt.DivergenceEvidenceSHA256,
            GrammarRevision = attempt.GrammarRevision,
            AttemptSHA256 = attempt.AttemptSHA256,
            PredecessorAttemptSHA256 = attempt.PredecessorAttemptSHA256,
            AttemptEvidenceRunID = attempt.EvidenceRunID,
            AttemptEvidenceRelativePath = attempt.EvidenceRelativePath,
            AttemptEvidenceAuthoritySHA256 = attempt.EvidenceAuthoritySHA256,
            AttemptEvidenceRailSHA256 = attempt.EvidenceRailSHA256,
            ChildOutcome = attempt.ChildOutcome,
            LinkEventID = attempt.LinkEventID,
            LinkPacketSHA256 = attempt.LinkPacketSHA256.Value ?? "",
            LinkJournalSHA256 = attempt.LinkJournalSHA256.Value ?? "",
            EvidenceSHA256 = "",
            ReceiptSHA256 = "",
        };
        bound = bound with { EvidenceSHA256 = ComputeEvidenceSHA256(bound) };
        return bound with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(bound.Kind, bound.Canonical) };
    }

    public CortexPolicyID PolicyID { get; init; }
    public CortexPolicyDecisionID DecisionID { get; init; }
    public TapeEventID DecisionEventID { get; init; }
    public CortexPolicyQuotaDecisionID QuotaDecisionID { get; init; }
    public CortexPolicyReadoutFingerprint ReadoutFingerprint { get; init; }
    public CortexPolicyCandidateFingerprint CandidateFingerprint { get; init; }
    public ulong CandidateOccurrenceDigest { get; init; }
    public GrammarRevisionID ReadoutRevision { get; init; }
    public PolicyCanonicalStateID CanonicalState { get; init; }
    public RepositoryFrontierRevision FrontierRevision { get; init; }
    public string FrontierAuthoritySHA256 { get; init; } = "";
    public int SelectionOrdinal { get; init; } = -1;
    public string WorldSHA256 { get; init; } = "";
    public string AccessSHA256 { get; init; } = "";
    public string AccessEntrySHA256 { get; init; } = "";
    public string CallSHA256 { get; init; } = "";
    public LoopClosureDigest ForkArmSHA256 { get; init; }
    public LoopClosureDigest ChildExecutionReceiptSHA256 { get; init; }
    public LoopClosureChildOutcomeReference ChildOutcome { get; init; }
    public TapeEventID OutcomeEventID { get; init; }
    public string OutcomePayloadSHA256 { get; init; } = "";
    public TapeEventID PredecessorEventID { get; init; }
    public LoopClosureDigest PredecessorDigest { get; init; }
    public string ReceiptSHA256 { get; init; } = "";
    public LoopLineageNodeID NodeID { get; init; }
    public string DecisionPayloadSHA256 { get; init; } = "";
    public TapeEventID ReadoutEventID { get; init; }
    public string ReadoutPayloadSHA256 { get; init; } = "";
    public TapeEventID FundingEventID { get; init; }
    public string FundingPayloadSHA256 { get; init; } = "";
    public TapeEventID BoundaryEventID { get; init; }
    public string BoundaryPayloadSHA256 { get; init; } = "";
    public TapeEventID SettlementEventID { get; init; }
    public string SettlementPayloadSHA256 { get; init; } = "";
    // Frozen journal row kind; identifier-side name is Divergence.
    public string Kind => "dissent";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RecordID, Species.ToString(), Path.ToString(), State.ToString(), EventID.Value.ToString(CultureInfo.InvariantCulture),
        PayloadSHA256, PredecessorEvidenceSHA256, LineageSHA256, JournalSHA256, NodeSpecies.ToString(),
        CandidateSpecies.ToString(), CandidateDigest.ToString(), CandidateCanonical, SourcePath, SourceLine.ToString(CultureInfo.InvariantCulture), SourceBytes.ToString(CultureInfo.InvariantCulture),
        SourceSHA256, AccessSequence.ToString(CultureInfo.InvariantCulture), ToolVerb.ToString(), PolicyID.Value,
        DecisionID.Value.ToString(CultureInfo.InvariantCulture), DecisionEventID.Value.ToString(CultureInfo.InvariantCulture),
        QuotaDecisionID.Value.ToString(CultureInfo.InvariantCulture), ReadoutFingerprint.ToString(), CandidateFingerprint.ToString(),
        CandidateOccurrenceDigest.ToString(CultureInfo.InvariantCulture), ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture),
        RepositoryLineageReceiptCodec.CanonicalState(CanonicalState), FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), WorldSHA256, AccessSHA256, CallSHA256,
        ForkArmSHA256.Value ?? "", ChildExecutionReceiptSHA256.Value ?? "", NodeID.Value, OutcomeEventID.Value.ToString(CultureInfo.InvariantCulture),
        OutcomePayloadSHA256, PredecessorEventID.Value.ToString(CultureInfo.InvariantCulture), PredecessorDigest.Value ?? "",
        DecisionPayloadSHA256, ReadoutEventID.Value.ToString(CultureInfo.InvariantCulture), ReadoutPayloadSHA256,
        FundingEventID.Value.ToString(CultureInfo.InvariantCulture), FundingPayloadSHA256,
        BoundaryEventID.Value.ToString(CultureInfo.InvariantCulture), BoundaryPayloadSHA256,
        SettlementEventID.Value.ToString(CultureInfo.InvariantCulture), SettlementPayloadSHA256,
        RepositoryLineageReceiptCodec.ChildOutcomeCanonical(ChildOutcome), FrontierAuthoritySHA256,
        FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), SelectionOrdinal.ToString(CultureInfo.InvariantCulture), AccessEntrySHA256,
        // Complete LoopClosureLinkAttempt projection.  PayloadSHA256 is the
        // causal attempt's EvidenceSHA256; the named copy below prevents a
        // later reader from silently treating the report evidence digest as
        // that attempt field.
        RunID, Step.ToString(CultureInfo.InvariantCulture), AttemptEvidenceSHA256, AttemptJournalSHA256,
        AttemptPredecessorEventID.ToString(CultureInfo.InvariantCulture), AttemptPredecessorEvidenceSHA256,
        DenialReason.ToString(), HasDenialReason ? "1" : "0", QuotaID.Value ?? "",
        ForkReceiptSHA256.Value ?? "", DivergenceEvidenceSHA256.Value ?? "", GrammarRevision.Value.ToString(CultureInfo.InvariantCulture),
        AttemptSHA256.Value ?? "", PredecessorAttemptSHA256.Value ?? "", AttemptEvidenceRunID,
        AttemptEvidenceRelativePath, AttemptEvidenceAuthoritySHA256.Value ?? "", AttemptEvidenceRailSHA256.Value ?? "",
        LinkEventID.Value.ToString(CultureInfo.InvariantCulture), LinkPacketSHA256, LinkJournalSHA256);

    public void Validate()
        => Validate(Species, null);

    public void Validate(
        LoopClosureLinkSpecies expectedSpecies,
        string? predecessorEvidence,
        bool allowExternalPredecessor = false)
    {
        if (string.IsNullOrWhiteSpace(RecordID) || Species != expectedSpecies || !Enum.IsDefined(Path)
            || !Enum.IsDefined(State) || EventID.Value <= 0 || !IsLowerSHA(PayloadSHA256) || !IsLowerSHA(EvidenceSHA256)
            || !IsLowerSHA(LineageSHA256) || !IsLowerSHA(JournalSHA256) || !Enum.IsDefined(NodeSpecies)
            || !Enum.IsDefined(CandidateSpecies) || !CandidateDigest.IsValid || string.IsNullOrWhiteSpace(CandidateCanonical)
            || string.IsNullOrWhiteSpace(SourcePath) || SourceLine < 1
            || SourceBytes < 0 || !IsLowerSHA(SourceSHA256) || AccessSequence < 0 || !Enum.IsDefined(ToolVerb))
            throw new InvalidDataException("repository loop link evidence is malformed");
        if (predecessorEvidence is null)
        {
            if (!allowExternalPredecessor && !string.IsNullOrEmpty(PredecessorEvidenceSHA256))
                throw new InvalidDataException("repository loop preference evidence unexpectedly carries a predecessor");
        }
        else if (PredecessorEvidenceSHA256 != predecessorEvidence)
            throw new InvalidDataException("repository loop link predecessor evidence diverges");
        if (State == LoopClosureLinkStates.Admitted && Path == LoopClosureLinkPaths.Organic
            && Species != LoopClosureLinkSpecies.PreferenceDivergence)
            throw new InvalidDataException("repository loop non-preference organic link is malformed");
        string expectedEvidence = ComputeEvidenceSHA256(this);
        if (EvidenceSHA256 != expectedEvidence)
            throw new InvalidDataException("repository loop link evidence digest diverges from typed evidence");
        if (!PolicyID.Equals(RepositoryNative.Policy.ID) || DecisionID.Value == 0 || DecisionEventID.Value <= 0
            || !ReadoutFingerprint.IsValid || !CandidateFingerprint.IsValid || CandidateOccurrenceDigest == 0
            || ReadoutRevision.Value == 0 || !RepositoryNative.Policy.IsCanonicalState(CanonicalState)
            || !FrontierRevision.IsValid || SelectionOrdinal < 0 || string.IsNullOrWhiteSpace(WorldSHA256)
            || string.IsNullOrWhiteSpace(AccessSHA256) || !IsLowerSHA(AccessEntrySHA256) || string.IsNullOrWhiteSpace(CallSHA256)
            || !NodeID.IsValid)
            throw new InvalidDataException("repository loop link generic authority is malformed");
        RepositoryLineageReceiptCodec.RequireLinkPredecessor(Species, PredecessorEventID, PredecessorDigest);
        RepositoryLineageReceiptCodec.RequireFrontierSelection(FrontierAuthoritySHA256, FrontierRevision, SelectionOrdinal, "link");
        RepositoryCandidate parsedCandidate = RepositoryLineageReceiptCodec.RequireCandidate(CandidateDigest, CandidateCanonical, FrontierRevision, CallSHA256);
        if (CandidateSpecies != parsedCandidate.Species)
            throw new InvalidDataException("repository link candidate species authority diverges");
        bool terminal = Species == LoopClosureLinkSpecies.ExecutedDivergence && State == LoopClosureLinkStates.Admitted;
        bool forkRequired = State == LoopClosureLinkStates.Admitted
            && Species is LoopClosureLinkSpecies.BoundaryAdmitted or LoopClosureLinkSpecies.ExecutedDivergence;
        if (terminal)
        {
            if (OutcomeEventID.Value <= 0 || !IsLowerSHA(OutcomePayloadSHA256) || !ForkArmSHA256.IsValid || !ChildExecutionReceiptSHA256.IsValid)
                throw new InvalidDataException("repository executed divergence link omits terminal outcome custody");
            ChildOutcome.Validate(required: true);
        }
        else
        {
            if (OutcomeEventID.Value != 0 || !string.IsNullOrEmpty(OutcomePayloadSHA256) || ChildOutcome.IsPresent)
                throw new InvalidDataException("repository prefix link fabricates terminal outcome custody");
            if (forkRequired)
            {
                if (!ForkArmSHA256.IsValid || ChildExecutionReceiptSHA256.IsValid)
                    throw new InvalidDataException("repository boundary link fork custody is malformed");
            }
            else if (ForkArmSHA256.IsValid || ChildExecutionReceiptSHA256.IsValid)
                throw new InvalidDataException("repository pre-contact link fabricates fork custody");
        ChildOutcome.Validate(required: false);
        }
        if (string.IsNullOrWhiteSpace(RunID) || Step < 0 || !IsLowerSHA(AttemptEvidenceSHA256)
            || AttemptEvidenceSHA256 != PayloadSHA256 || !IsLowerSHA(AttemptJournalSHA256)
            || !IsLowerSHA(AttemptPredecessorEvidenceSHA256, optional: true)
            || !IsLowerSHA(JournalSHA256) || !Enum.IsDefined(DenialReason)
            || (Species != LoopClosureLinkSpecies.PreferenceDivergence && !QuotaID.IsValid)
            || !AttemptSHA256.IsValid
            || (AttemptEvidenceRunID.Length != 0
                && (string.IsNullOrWhiteSpace(AttemptEvidenceRelativePath)
                    || !AttemptEvidenceAuthoritySHA256.IsValid || !AttemptEvidenceRailSHA256.IsValid))
            || LinkEventID.Value <= 0 || LinkEventID == EventID || !IsLowerSHA(LinkPacketSHA256)
            || !IsLowerSHA(LinkJournalSHA256))
            throw new InvalidDataException("repository loop link attempt projection is malformed");
        LoopClosureLinkAttempt projectedAttempt = ToLinkAttempt();
        projectedAttempt.Validate();
        if (LinkPacketSHA256 != RepositoryLineageReceiptCodec.Digest(projectedAttempt.Kind, projectedAttempt.Canonical))
            throw new InvalidDataException("repository loop link packet digest diverges from the typed attempt");
        if (projectedAttempt.Canonical.Length == 0)
            throw new InvalidDataException("repository loop link attempt projection is empty");
        RepositoryLineageReceiptCodec.RequirePacket(DecisionEventID, DecisionPayloadSHA256, "link decision", required: true);
        RepositoryLineageReceiptCodec.RequirePacket(ReadoutEventID, ReadoutPayloadSHA256, "link readout", required: true);
        RepositoryLineageReceiptCodec.RequirePacket(FundingEventID, FundingPayloadSHA256, "link funding", required: QuotaDecisionID.Value != 0);
        RepositoryLineageReceiptCodec.RequirePacket(BoundaryEventID, BoundaryPayloadSHA256, "link boundary", required: false);
        RepositoryLineageReceiptCodec.RequirePacket(SettlementEventID, SettlementPayloadSHA256, "link settlement", required: false);
        RepositoryLineageReceiptCodec.RequireLowerSHA(WorldSHA256, "link world");
        RepositoryLineageReceiptCodec.RequireLowerSHA(AccessSHA256, "link access");
        RepositoryLineageReceiptCodec.RequireLowerSHA(CallSHA256, "link call");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "link receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical))
            throw new InvalidDataException("repository loop link receipt digest diverges");
    }

    public static string ComputeEvidenceSHA256(RepositoryLoopClosureLinkEvidence evidence)
        => RepositoryLineageReceiptCodec.Digest(evidence.Kind, evidence.Canonical);

    private static bool IsLowerSHA(string value, bool optional = false)
        => optional && value.Length == 0 || value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class RepositoryLoopClosureLinkContract
{
    public RepositoryLoopClosureLinkContract(
        IReadOnlyList<RepositoryLoopClosureLinkEvidence> evidence,
        IReadOnlyList<LoopClosureGateLiveness> liveness,
        bool allowOrganicGap = false)
    {
        Evidence = Array.AsReadOnly((evidence ?? throw new ArgumentNullException(nameof(evidence))).ToArray());
        Liveness = Array.AsReadOnly((liveness ?? throw new ArgumentNullException(nameof(liveness))).ToArray());
        AllowOrganicGap = allowOrganicGap || (Evidence.Count == LoopClosureLinkContract.OrderedSpecies.Count - 1
            && Liveness.Count == LoopClosureLinkContract.OrderedSpecies.Count
            && Evidence.Count > 0
            && Evidence[0].Species == LoopClosureLinkSpecies.InterventionDivergence);
    }

    public IReadOnlyList<RepositoryLoopClosureLinkEvidence> Evidence { get; }
    public IReadOnlyList<LoopClosureGateLiveness> Liveness { get; }
    public bool AllowOrganicGap { get; }
    public bool IsComplete => (Evidence.Count == LoopClosureLinkContract.OrderedSpecies.Count
        && Liveness.Count == LoopClosureLinkContract.OrderedSpecies.Count)
        || (AllowOrganicGap && Evidence.Count == LoopClosureLinkContract.OrderedSpecies.Count - 1
            && Liveness.Count == LoopClosureLinkContract.OrderedSpecies.Count);

    public void Validate(bool requireComplete)
    {
        bool organicGap = AllowOrganicGap
            && Evidence.Count == LoopClosureLinkContract.OrderedSpecies.Count - 1
            && Liveness.Count == LoopClosureLinkContract.OrderedSpecies.Count
            && Evidence.Count > 0
            && Evidence[0].Species == LoopClosureLinkSpecies.InterventionDivergence;
        if ((!organicGap && Evidence.Count != Liveness.Count)
            || Evidence.Count == 0
            || Evidence.Count > LoopClosureLinkContract.OrderedSpecies.Count
            || requireComplete && !IsComplete)
            throw new InvalidDataException("repository loop link contract length is malformed");

        int evidenceOffset = organicGap ? 1 : 0;
        IReadOnlyList<LoopClosureLinkSpecies> expectedEvidence = LoopClosureLinkContract.OrderedSpecies
            .Skip(evidenceOffset).Take(Evidence.Count).ToArray();
        if (!Evidence.Select(static receipt => receipt.Species).SequenceEqual(expectedEvidence))
            throw new InvalidDataException("repository loop link evidence species are not the canonical typed chain");
        if (organicGap && (Liveness.Count != LoopClosureLinkContract.OrderedSpecies.Count
            || Liveness[0].Species != LoopClosureLinkSpecies.PreferenceDivergence
            || Liveness[0].Reached != 0 || Liveness[0].Admitted != 0 || Liveness[0].Denied != 0))
            throw new InvalidDataException("repository organic preference gap carries nonzero liveness");

        for (int index = 0; index < Evidence.Count; index++)
        {
            int speciesIndex = index + evidenceOffset;
            LoopClosureLinkSpecies species = LoopClosureLinkContract.OrderedSpecies[speciesIndex];
            string? predecessor = speciesIndex == 0 ? null : index == 0 && organicGap
                ? null : Evidence[index - 1].EvidenceSHA256;
            Evidence[index].Validate(species, predecessor, allowExternalPredecessor: organicGap && index == 0);
            Liveness[speciesIndex].Validate();
            if (Liveness[speciesIndex].Species != species)
                throw new InvalidDataException("repository loop link liveness species diverges");
            LoopClosureLinkPaths expectedPath = speciesIndex == 0 ? LoopClosureLinkPaths.Organic : LoopClosureLinkPaths.Forced;
            if (Evidence[index].Path != expectedPath)
                throw new InvalidDataException("repository loop link path is not canonical");
            if (Evidence[index].State == LoopClosureLinkStates.Admitted && Liveness[speciesIndex].Admitted == 0
                || Evidence[index].State == LoopClosureLinkStates.Denied && Liveness[speciesIndex].Denied == 0)
                throw new InvalidDataException("repository loop link state has no lifetime receipt");
            if (index > 0 && Evidence[index].PredecessorEvidenceSHA256 != Evidence[index - 1].EvidenceSHA256)
                throw new InvalidDataException("repository loop link evidence chain diverges");
        }
        for (int index = 0; index < Liveness.Count; index++) Liveness[index].Validate();
        if (requireComplete && (Evidence[^1].State != LoopClosureLinkStates.Admitted || Liveness[^1].Admitted == 0))
            throw new InvalidDataException("repository loop executed divergence is not admitted");
    }
}

public abstract record RepositoryLoopClosureVerdict
{
    internal RepositoryLoopClosureVerdict(
        LoopClosureAssayStatuses assay,
        LoopClosurePowerStatuses power,
        LoopClosureVerdictStatuses status,
        LoopClosureDigest evidenceSHA256)
    {
        Assay = assay; Power = power; Status = status; EvidenceSHA256 = evidenceSHA256;
    }

    public LoopClosureAssayStatuses Assay { get; }
    public LoopClosurePowerStatuses Power { get; }
    public LoopClosureVerdictStatuses Status { get; }
    public LoopClosureDigest EvidenceSHA256 { get; }
    public abstract RepositoryLoopClosureVerdictSpecies Species { get; }
    public string SpeciesName => Species switch
    {
        // Frozen verdict species token; identifier-side name is PatternBecameThought.
        RepositoryLoopClosureVerdictSpecies.PatternBecameThought => "theory_became_thought",
        RepositoryLoopClosureVerdictSpecies.ThoughtOverruledInstinct => "thought_overruled_instinct",
        RepositoryLoopClosureVerdictSpecies.ObjectLoopClosed => "object_loop_closed",
        _ => throw new InvalidDataException("repository loop verdict species is unknown"),
    };

    public virtual void Validate()
    {
        if (!Enum.IsDefined(Assay) || !Enum.IsDefined(Power) || !Enum.IsDefined(Status) || !EvidenceSHA256.IsValid)
            throw new InvalidDataException("repository loop verdict envelope is malformed");
        if (Status == LoopClosureVerdictStatuses.PASS
            && (Assay != LoopClosureAssayStatuses.Exact || Power != LoopClosurePowerStatuses.Powered))
            throw new InvalidDataException("repository loop PASS requires exact powered evidence");
        if (Status == LoopClosureVerdictStatuses.BANKED_NULL && Power != LoopClosurePowerStatuses.Unpowered)
            throw new InvalidDataException("repository loop BANKED_NULL requires an unpowered arc");
    }
}

public sealed record RepositoryPatternBecameThoughtVerdict(
    LoopClosureAssayStatuses Assay,
    LoopClosurePowerStatuses Power,
    LoopClosureVerdictStatuses Status,
    LoopClosureDigest EvidenceSHA256,
    RepositoryPatternComposition Composition)
    : RepositoryLoopClosureVerdict(Assay, Power, Status, EvidenceSHA256)
{
    public override RepositoryLoopClosureVerdictSpecies Species => RepositoryLoopClosureVerdictSpecies.PatternBecameThought;
    public override void Validate()
    {
        base.Validate();
        if (Status == LoopClosureVerdictStatuses.PASS)
        {
            Composition.Validate();
            if (Composition.Receipt.EvaluatorDelta <= 0 || EvidenceSHA256.Value != Composition.Receipt.ReceiptSHA256)
                throw new InvalidDataException("repository pattern-became-thought evidence is not the exact displaced-evaluation receipt");
        }
    }
}

public sealed record RepositoryThoughtOverruledInstinctVerdict(
    LoopClosureAssayStatuses Assay,
    LoopClosurePowerStatuses Power,
    LoopClosureVerdictStatuses Status,
    LoopClosureDigest EvidenceSHA256,
    LoopClosureDigest DivergenceEvidenceSHA256)
    : RepositoryLoopClosureVerdict(Assay, Power, Status, EvidenceSHA256)
{
    public override RepositoryLoopClosureVerdictSpecies Species => RepositoryLoopClosureVerdictSpecies.ThoughtOverruledInstinct;
    public override void Validate()
    {
        base.Validate();
        if (Status == LoopClosureVerdictStatuses.PASS && !DivergenceEvidenceSHA256.IsValid)
            throw new InvalidDataException("repository thought-overruled-instinct pass omits divergence evidence");
    }
}

public sealed record RepositoryObjectLoopClosedVerdict(
    LoopClosureAssayStatuses Assay,
    LoopClosurePowerStatuses Power,
    LoopClosureVerdictStatuses Status,
    LoopClosureDigest EvidenceSHA256,
    LoopClosureDigest OutcomeEvidenceSHA256)
    : RepositoryLoopClosureVerdict(Assay, Power, Status, EvidenceSHA256)
{
    public override RepositoryLoopClosureVerdictSpecies Species => RepositoryLoopClosureVerdictSpecies.ObjectLoopClosed;
    public override void Validate()
    {
        base.Validate();
        if (Status == LoopClosureVerdictStatuses.PASS && !OutcomeEvidenceSHA256.IsValid)
            throw new InvalidDataException("repository object-loop-closed pass omits outcome evidence");
    }
}

/// Repository-native report owner. This is intentionally not the historical
/// Homeostat/EML LoopClosureReport and has no serializer until the RON generator
/// emits the domain types correctly.
public sealed class RepositoryLoopClosureReport
{
    public const string ReportSpecies = "repository-loop-closure";
    // Frozen artifact token BirthCertificate; identifier-side name is ClosureCertificate.
    public const string ClosureCertificateTitle = "BirthCertificate";
    public const string ReportTitle = "RepositoryLoopClosureReport";

    public RepositoryLoopClosureReport(
        RepositoryLoopClosureAdjudicationInput input,
        IReadOnlyList<RepositoryLoopClosureVerdict> verdicts,
        RepositoryLoopClosureLinkContract links,
        RepositoryLoopClosureTaskOutcome? taskOutcome,
        RepositoryLoopClosureLineageResult lineage)
        : this(input, verdicts, links, taskOutcome, lineage, null)
    {
    }

    internal RepositoryLoopClosureReport(
        RepositoryLoopClosureAdjudicationInput input,
        IReadOnlyList<RepositoryLoopClosureVerdict> verdicts,
        RepositoryLoopClosureLinkContract links,
        RepositoryLoopClosureTaskOutcome? taskOutcome,
        RepositoryLoopClosureLineageResult lineage,
        RepositoryLoopClosureAdjudicator.AdjudicationCapability? adjudicationCapability)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Verdicts = Array.AsReadOnly((verdicts ?? throw new ArgumentNullException(nameof(verdicts))).ToArray());
        Links = links ?? throw new ArgumentNullException(nameof(links));
        TaskOutcome = taskOutcome;
        Lineage = lineage;
        if (adjudicationCapability is not null
            && !string.Equals(adjudicationCapability.SealedEvidenceAuthoritySHA256,
                Input.EvidenceAuthority.AuthoritySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository loop adjudication capability is bound to another sealed evidence authority");
        _adjudicationCapability = adjudicationCapability;
    }

    private readonly RepositoryLoopClosureAdjudicator.AdjudicationCapability? _adjudicationCapability;

    public RepositoryLoopClosureAdjudicationInput Input { get; }
    public IReadOnlyList<RepositoryLoopClosureVerdict> Verdicts { get; }
    public RepositoryLoopClosureLinkContract Links { get; }
    public RepositoryLoopClosureTaskOutcome? TaskOutcome { get; }
    public RepositoryLoopClosureLineageResult Lineage { get; }
    public bool AllVerdictsPass => Verdicts.Count == 3
        && Verdicts.Select(static verdict => verdict.Species).OrderBy(static species => species).SequenceEqual(Enum.GetValues<RepositoryLoopClosureVerdictSpecies>())
        && Verdicts.All(static verdict => verdict.Status == LoopClosureVerdictStatuses.PASS
            && verdict.Assay == LoopClosureAssayStatuses.Exact
            && verdict.Power == LoopClosurePowerStatuses.Powered);
    public bool CanRenderClosureCertificate
    {
        get
        {
            try
            {
                Validate();
                if (!RepositoryLoopClosureAdjudicator.IsMounted)
                    return false;
                if (_adjudicationCapability is null
                    || TaskOutcome is null || TaskOutcome.Species != RepositoryLoopClosureTaskOutcomeSpecies.Confirmed
                    || !TaskOutcome.IsSourceBacked || !TaskOutcome.IsBeforeSeal(Input.Tape)
                    || !AllVerdictsPass || !Lineage.Canonical.Passed || !Lineage.NullDiscriminates)
                    return false;
                Links.Validate(requireComplete: true);
                Lineage.Validate(Input);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    public string RenderTitle() => CanRenderClosureCertificate ? ClosureCertificateTitle : ReportTitle;

    public void Validate()
    {
        Input.Validate();
        if (Verdicts.Count == 0 || Verdicts.Count > Enum.GetValues<RepositoryLoopClosureVerdictSpecies>().Length
            || Verdicts.Select(static verdict => verdict.Species).Distinct().Count() != Verdicts.Count)
            throw new InvalidDataException("repository loop report verdict set is malformed");
        foreach (RepositoryLoopClosureVerdict verdict in Verdicts)
        {
            if (verdict.Species switch
                {
                    RepositoryLoopClosureVerdictSpecies.PatternBecameThought => verdict is RepositoryPatternBecameThoughtVerdict,
                    RepositoryLoopClosureVerdictSpecies.ThoughtOverruledInstinct => verdict is RepositoryThoughtOverruledInstinctVerdict,
                    RepositoryLoopClosureVerdictSpecies.ObjectLoopClosed => verdict is RepositoryObjectLoopClosedVerdict,
                    _ => false,
                } is false)
                throw new InvalidDataException("repository loop verdict species is not carried by its concrete corroboration");
            verdict.Validate();
        }
        Links.Validate(requireComplete: false);
        Dictionary<(RepositoryCandidateDigest Digest, string Canonical), RepositoryCandidate> frontierCandidates = Input.Frontier.Candidates
            .ToDictionary(static candidate => (candidate.Digest, candidate.Canonical));
        HashSet<(RepositoryCandidateDigest Digest, string Canonical)> frontierTransitions = Input.Frontier.Transitions
            .Select(static transition => (transition.CandidateDigest, transition.CandidateCanonical)).ToHashSet();
        Dictionary<long, LoopLineageEdgeReceipt> lineageByEvent = Input.Tape.LineageEdges
            .ToDictionary(static edge => edge.Node.EventID.Value);
        Dictionary<long, LoopLineageTapeEvent> tapeByEvent = Input.Tape.Events
            .ToDictionary(static item => item.EventID.Value);
        Dictionary<long, JournalRowBinding> journalByEvent = Input.Journal.Rows
            .GroupBy(static row => row.EventID.Value).ToDictionary(static rows => rows.Key, static rows => rows.Single());
        Dictionary<long, RepositoryAccessEntry> accessBySequence = Input.Access.Entries
            .ToDictionary(static entry => entry.Sequence);
        Dictionary<string, RepositoryLoopClosureWorldFile> worldFiles = Input.World.Files
            .ToDictionary(static file => file.Path.Value, StringComparer.Ordinal);
        foreach (RepositoryLoopClosureLinkEvidence evidence in Links.Evidence)
        {
            if (!tapeByEvent.TryGetValue(evidence.EventID.Value, out LoopLineageTapeEvent eventItem)
                || !lineageByEvent.TryGetValue(evidence.EventID.Value, out LoopLineageEdgeReceipt edge)
                || !frontierCandidates.TryGetValue((evidence.CandidateDigest, evidence.CandidateCanonical), out RepositoryCandidate? candidate)
                || !frontierTransitions.Contains((evidence.CandidateDigest, evidence.CandidateCanonical))
                || !worldFiles.TryGetValue(evidence.SourcePath, out RepositoryLoopClosureWorldFile? source)
                || !accessBySequence.TryGetValue(evidence.AccessSequence, out RepositoryAccessEntry access)
                || !journalByEvent.TryGetValue(evidence.EventID.Value, out JournalRowBinding journalRow))
                throw new InvalidDataException("repository loop link evidence omits a sealed source join");
            if (!Input.Access.CarriesJournalRoot(evidence.AccessSHA256))
                throw new InvalidDataException($"repository loop link evidence {evidence.RecordID} stamps access root {evidence.AccessSHA256}, which is not a prefix root of the sealed access journal of {Input.Access.Entries.Count} entries");
            if (evidence.JournalSHA256 != Input.Journal.JournalSHA256
                || evidence.EventID.Value >= Input.Tape.SealEventID
                || evidence.NodeSpecies != edge.Node.Species
                || evidence.NodeID != edge.Node.NodeID
                || evidence.LineageSHA256 != edge.CanonicalLineageSHA256
                || evidence.PayloadSHA256 != Convert.ToHexStringLower(SHA256.HashData(eventItem.Payload.Span))
                || evidence.CandidateSpecies != candidate.Species || evidence.ToolVerb != candidate.Verb
                || access.Verb != evidence.ToolVerb || access.Argument != candidate.Argument
                || access.CallSHA256 != evidence.CallSHA256 || access.EntrySHA256 != evidence.AccessEntrySHA256
                || !access.Paths.Any(path => path.Value == evidence.SourcePath)
                || !access.Loci.Any(locus => locus.Path.Value == evidence.SourcePath && locus.Line == evidence.SourceLine)
                || !source.TryGetLineBytes(evidence.SourceLine, out _)
                || !Input.Access.Sources.Any(sourceBinding => sourceBinding.Sequence == evidence.AccessSequence
                    && sourceBinding.Path == evidence.SourcePath && sourceBinding.Bytes == evidence.SourceBytes
                    && sourceBinding.SHA256 == evidence.SourceSHA256)
                || evidence.SourceBytes != source.Bytes || evidence.SourceSHA256 != source.SHA256
                || journalRow.Source != eventItem.Source
                || journalRow.LineIndex < 0 || journalRow.LineIndex >= Input.Journal.Lines.Count
                || journalRow.SHA256 != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Input.Journal.Lines[journalRow.LineIndex]))))
                throw new InvalidDataException("repository loop link evidence is bound to another sealed source");
            if (!tapeByEvent.TryGetValue(evidence.LinkEventID.Value, out LoopLineageTapeEvent linkPacket)
                || linkPacket.Source != "repository:loop-link"
                || linkPacket.Provenance != Provenances.Execution
                || linkPacket.Roles != TapeEventRoles.AuditOnly
                || evidence.LinkEventID.Value >= Input.Tape.SealEventID
                || !journalByEvent.TryGetValue(evidence.LinkEventID.Value, out JournalRowBinding linkJournalRow)
                || evidence.LinkJournalSHA256 != LoopClosureLinkAttemptStore.DigestLoopClosureLinkJournalReceipt(
                    linkJournalRow.Step, evidence.LinkEventID.Value, linkPacket.Payload.Length).Value
                || linkJournalRow.Source != "repository:loop-link"
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(linkPacket.Payload.Span,
                    out string linkKind, out string linkCanonical, out string linkDigest)
                || !MatchesLoopLinkReceipt(evidence, linkKind, linkCanonical, linkDigest))
                throw new InvalidDataException("repository loop link tape packet does not decode to its typed receipt");
        }
        TaskOutcome?.Validate(Input);
        Lineage.Validate(Input);
        if (AllVerdictsPass && TaskOutcome?.Species == RepositoryLoopClosureTaskOutcomeSpecies.Confirmed)
            ValidateCompleteClosureBindings(TaskOutcome);
    }

    /// A source-backed PASS is one causal chain, not a set of independently valid
    /// receipts. Keep the cross-domain joins here so every complete report applies
    /// the same link, verdict, task, and chronology contract.
    private void ValidateCompleteClosureBindings(RepositoryLoopClosureTaskOutcome outcome)
    {
        Links.Validate(requireComplete: true);
        RepositoryLoopClosureLinkEvidence[] executedDivergence = Links.Evidence
            .Where(static evidence => evidence.Species == LoopClosureLinkSpecies.ExecutedDivergence
                && evidence.State == LoopClosureLinkStates.Admitted)
            .ToArray();
        if (executedDivergence.Length != 1)
            throw new InvalidDataException("repository complete closure requires exactly one admitted executed divergence");

        RepositoryLoopClosureLinkEvidence link = executedDivergence[0];
        if (link.CandidateDigest != outcome.Candidate.Digest
            || link.CandidateCanonical != outcome.Candidate.Canonical
            || link.CandidateSpecies != outcome.Candidate.Species
            || link.FrontierRevision != outcome.FrontierRevision
            || link.FrontierAuthoritySHA256 != outcome.FrontierAuthoritySHA256
            || link.FrontierRevision != outcome.SelectionRevision
            || link.FrontierAuthoritySHA256 != outcome.SelectionFrontierAuthoritySHA256
            || link.SelectionOrdinal != outcome.SelectionOrdinal)
            throw new InvalidDataException("repository executed divergence is not bound to the confirmed task selection");

        LoopLineageTapeEvent selectionEvent = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == outcome.SelectionEventID);
        if (selectionEvent.EventID != outcome.SelectionEventID
            || !RepositorySelectionReceipt.TryDecode(selectionEvent.Payload.Span, out RepositorySelectionReceipt selectionReceipt))
            throw new InvalidDataException("repository complete closure selection receipt is missing or malformed");
        if (selectionReceipt.ReceiptSHA256 != outcome.SelectionReceiptSHA256
            || selectionReceipt.CandidateDigest != outcome.Candidate.Digest
            || selectionReceipt.CandidateCanonical != outcome.Candidate.Canonical
            || selectionReceipt.CandidateSpecies != outcome.Candidate.Species
            || selectionReceipt.FrontierRevision != outcome.SelectionRevision
            || selectionReceipt.FrontierAuthoritySHA256 != outcome.SelectionFrontierAuthoritySHA256
            || selectionReceipt.SelectionOrdinal != outcome.SelectionOrdinal
            || link.ReadoutFingerprint.Value != selectionReceipt.ReadoutFingerprint
            || link.CandidateFingerprint.Value != selectionReceipt.ReadoutCandidateFingerprint
            || link.DecisionID != selectionReceipt.DecisionID
            || link.DecisionEventID != selectionReceipt.DecisionEventID
            || link.DecisionPayloadSHA256 != selectionReceipt.DecisionPayloadSHA256
            || !(outcome.SelectionEventID.Value < link.EventID.Value
                && link.EventID.Value < outcome.ActionEventID.Value
                && outcome.ActionEventID.Value < outcome.OccurrenceCheckEventID.Value
                && outcome.OccurrenceCheckEventID.Value < outcome.OutcomeEventID.Value
                && outcome.OutcomeEventID.Value < Input.Tape.SealEventID))
            throw new InvalidDataException("repository complete closure selection/link/task chronology or decision custody diverges");

        RepositoryThoughtOverruledInstinctVerdict divergence = Verdicts.OfType<RepositoryThoughtOverruledInstinctVerdict>().Single();
        if (divergence.DivergenceEvidenceSHA256.Value != link.DivergenceEvidenceSHA256.Value)
            throw new InvalidDataException("repository thought-overruled-instinct evidence is not the admitted executed divergence");

        RepositoryObjectLoopClosedVerdict closed = Verdicts.OfType<RepositoryObjectLoopClosedVerdict>().Single();
        if (closed.OutcomeEvidenceSHA256.Value != outcome.OutcomeSHA256)
            throw new InvalidDataException("repository object-loop-closed evidence is not the confirmed task outcome authority");

        RepositoryPatternBecameThoughtVerdict pattern = Verdicts.OfType<RepositoryPatternBecameThoughtVerdict>().Single();
        if (!Input.Pattern.Compositions.Any(composition =>
                composition.Receipt.ReceiptSHA256 == pattern.EvidenceSHA256.Value
                && composition.Conclusion.CandidateDigest == pattern.Composition.Conclusion.CandidateDigest
                && composition.Conclusion.Candidate.Canonical == pattern.Composition.Conclusion.Candidate.Canonical))
            throw new InvalidDataException("repository pattern-became-thought evidence is not bound to the sealed pattern");
        if (pattern.Composition.Conclusion.CandidateDigest != outcome.Candidate.Digest
            || pattern.Composition.Conclusion.Candidate.Canonical != outcome.Candidate.Canonical
            || !Input.Frontier.Transitions.Any(transition => transition.CandidateDigest == outcome.Candidate.Digest
                && transition.CandidateCanonical == outcome.Candidate.Canonical)
            || !Input.Frontier.Selections.Any(selection => selection.Revision == outcome.SelectionRevision
                && selection.RuntimeAuthoritySHA256 == outcome.SelectionFrontierAuthoritySHA256
                && selection.Ordinal == outcome.SelectionOrdinal
                && selection.SelectionEventID == outcome.SelectionEventID
                && selection.CandidateDigest == outcome.Candidate.Digest
                && selection.CandidateCanonical == outcome.Candidate.Canonical))
            throw new InvalidDataException("repository pattern-became-thought evidence is not on the confirmed task selection chain");
        ValidatePatternCustody(pattern.Composition);

        ValidateExecutedDivergenceCustody(link, outcome);
    }

    private void ValidatePatternCustody(RepositoryPatternComposition composition)
    {
        TapeEventID[] expectedOccurrenceReceipts = composition.Conclusion.OccurrenceSet.Occurrences
            .Select(static occurrence => occurrence.OccurrenceCheckReceiptEventID)
            .ToArray();
        if (!composition.Receipt.OccurrenceReceiptEventIDs.SequenceEqual(expectedOccurrenceReceipts))
            throw new InvalidDataException("repository pattern composition occurrence receipt chain diverges");
        foreach (RepositoryPatternOccurrence occurrence in composition.Conclusion.OccurrenceSet.Occurrences)
        {
            LoopLineageTapeEvent sourceEvent = Input.Tape.Events
                .SingleOrDefault(eventRecord => eventRecord.EventID == occurrence.SourceEventID);
            if (sourceEvent.EventID != occurrence.SourceEventID
                || sourceEvent.EventID.Value >= Input.Tape.SealEventID
                || !Input.Journal.Lines.Any(line => line.Contains($"\t{occurrence.SourceEventID}\t", StringComparison.Ordinal)))
                throw new InvalidDataException("repository pattern occurrence source event is absent from sealed tape or journal");

            // Frozen tape payload tokens and field names; identifier-side names are OccurrenceCheck and Prediction.
            byte[] occurrenceCheckPayload = Encoding.UTF8.GetBytes($"REPOSITORY-VERIFICATION\tstep={occurrence.OccurrenceCheck.Step}\tspecies={occurrence.OccurrenceCheck.Prediction.Species}\tclaim={occurrence.OccurrenceCheck.Prediction.Canonical}\toutcome={occurrence.OccurrenceCheck.Outcome}\tworld={occurrence.OccurrenceCheck.WorldSHA256}\taccess={occurrence.OccurrenceCheck.AccessSHA256}\taccess-sequence={occurrence.OccurrenceCheck.AccessSequence}\taccess-entry-sha256={occurrence.OccurrenceCheck.AccessEntrySHA256}\taccess-entry-count={occurrence.OccurrenceCheck.AccessEntryCount}\tclaim-sha256={occurrence.OccurrenceCheck.PredictionSHA256}\tevidence={occurrence.OccurrenceCheck.EvidenceSHA256}\tevaluator-cost={occurrence.OccurrenceCheck.EvaluatorCost}\taccess-cost={occurrence.OccurrenceCheck.AccessCost}\tpredecessor={occurrence.OccurrenceCheck.PredecessorEventID.Value}\tcall={occurrence.OccurrenceCheck.CallSHA256}\treceipt={occurrence.OccurrenceCheck.ReceiptSHA256}");
            LoopLineageTapeEvent occurrenceCheckEvent = Input.Tape.Events
                .SingleOrDefault(eventRecord => eventRecord.EventID == occurrence.OccurrenceCheckReceiptEventID);
            if (occurrenceCheckEvent.EventID != occurrence.OccurrenceCheckReceiptEventID
                // Frozen tape source token; identifier-side name is OccurrenceCheck.
                || occurrenceCheckEvent.Source != "repository:verification"
                || occurrenceCheckEvent.Provenance != Provenances.Execution
                || occurrenceCheckEvent.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || occurrenceCheckEvent.EventID.Value >= Input.Tape.SealEventID
                || !occurrenceCheckEvent.Payload.Span.SequenceEqual(occurrenceCheckPayload)
                // Frozen journal source and row kind; identifier-side name is OccurrenceCheck.
                || !Input.Journal.Lines.Any(line => line.StartsWith($"{occurrence.OccurrenceCheck.Step}\trepository-verification\t{occurrenceCheckEvent.EventID}\t", StringComparison.Ordinal)
                    && line.EndsWith($"\t{occurrenceCheckPayload.Length}B", StringComparison.Ordinal))
                || !Input.Tape.LineageEdges.Any(edge => edge.Node.EventID == occurrence.OccurrenceCheckReceiptEventID
                    && edge.Node.Species == LoopLineageNodeSpecies.VerifiedLaw))
                throw new InvalidDataException("repository pattern occurrence check custody is absent from sealed tape, journal, or lineage");
        }

        LoopLineageTapeEvent compositionEvent = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == composition.Receipt.CompositionEventID);
        LoopLineageEdgeReceipt compositionEdge = Input.Tape.LineageEdges
            .SingleOrDefault(edge => edge.Node.EventID == composition.Receipt.CompositionEventID);
        if (compositionEvent.EventID != composition.Receipt.CompositionEventID
            || compositionEvent.EventID.Value >= Input.Tape.SealEventID
            || compositionEdge is null
            || compositionEvent.Source != "repository:lineage"
            || !TapePacketCreator.TryReadRepositoryLineageReceipt(compositionEvent.Payload.Span,
                out string kind, out string canonical, out string digest)
            || kind != composition.Receipt.Kind
            || canonical != composition.Receipt.Canonical
            || digest != composition.Receipt.ReceiptSHA256
            || !Input.Journal.Lines.Any(line => line.Contains($"\t{compositionEvent.EventID}\t", StringComparison.Ordinal))
            || compositionEdge.Node.EventID != composition.Receipt.CompositionEventID
            || compositionEdge.Node.Species != LoopLineageNodeSpecies.Rung0Composition
            // The edge names predecessor NODES; the occurrence set names EVENTS. The chain holds
            // only when each predecessor node resolves, in order, to the occurrence receipt event
            // that the composition requires.
            || !compositionEdge.PredecessorIDs
                .Select(predecessorID => Input.Tape.LineageEdges
                    .SingleOrDefault(edge => edge.Node.NodeID == predecessorID)?.Node.EventID ?? default)
                .SequenceEqual(expectedOccurrenceReceipts))
            throw new InvalidDataException("repository pattern composition custody is absent from sealed tape, journal, or lineage");
    }

    private void ValidateExecutedDivergenceCustody(
        RepositoryLoopClosureLinkEvidence link,
        RepositoryLoopClosureTaskOutcome outcome)
    {
        LoopLineageTapeEvent terminalEvent = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == link.OutcomeEventID);
        if (terminalEvent.EventID != link.OutcomeEventID
            || terminalEvent.Source != "policy-boundary:outcome"
            || terminalEvent.Provenance != Provenances.Execution
            || terminalEvent.Roles != TapeEventRoles.AuditOnly
            || Convert.ToHexStringLower(SHA256.HashData(terminalEvent.Payload.Span)) != link.OutcomePayloadSHA256
            || !Input.Journal.Rows.Any(row => row.EventID == link.OutcomeEventID && row.Source == "policy-boundary:outcome")
            || !TryReadBoundaryOutcomeFields(terminalEvent.Payload.Span, out Dictionary<string, string> fields)
            || !string.Equals(fields["policy"], link.PolicyID.Value, StringComparison.Ordinal)
            || !string.Equals(fields["funding"], link.QuotaDecisionID.ToString(), StringComparison.Ordinal)
            || !string.Equals(fields["adjudication"], link.DivergenceEvidenceSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(fields["forced-decision"], $"u:{link.ChildOutcome.ForcedDecisionID.Value:X16}", StringComparison.Ordinal)
            || !long.TryParse(fields["forced-outcome-event"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long childOutcomeEventID)
            || childOutcomeEventID != link.ChildOutcome.OutcomeEventID.Value
            || !string.Equals(fields["forced-outcome-payload"], link.ChildOutcome.OutcomePayloadSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(fields["execution-fork"], link.ForkArmSHA256.Value, StringComparison.Ordinal)
            || !string.Equals(fields["execution-child"], link.ChildExecutionReceiptSHA256.Value, StringComparison.Ordinal)
            || !fields["decision"].StartsWith("u:", StringComparison.Ordinal)
            || !ulong.TryParse(fields["decision"].AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong decisionID)
            || decisionID != link.DecisionID.Value
            || link.QuotaDecisionID.Value == 0
            || link.ChildOutcome.ForcedDecisionID.Value == 0
            || link.OutcomeEventID.Value >= outcome.ActionEventID.Value)
            throw new InvalidDataException("repository executed divergence terminal custody is not the sealed policy child outcome");

        ValidateDecisionReadoutFundingCustody(link);
    }

    private void ValidateDecisionReadoutFundingCustody(RepositoryLoopClosureLinkEvidence link)
    {
        LoopLineageTapeEvent decisionEvent = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == link.DecisionEventID);
        if (decisionEvent.EventID != link.DecisionEventID
            || decisionEvent.Source != "policy:" + link.PolicyID.Value
            || decisionEvent.Provenance != Provenances.Execution
            || decisionEvent.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
            || Convert.ToHexStringLower(SHA256.HashData(decisionEvent.Payload.Span)) != link.DecisionPayloadSHA256
            || !TapePacketCreator.TryDecodePolicyDecision(decisionEvent.Payload.Span, out CortexPolicyDecisionPacket decision)
            || decision.DecisionID != link.DecisionID
            || decision.Readout.ReadoutFingerprint != link.ReadoutFingerprint.Value
            || decision.Readout.ReadoutCandidateFingerprint != link.CandidateFingerprint.Value
            || decision.Readout.ReadoutCandidateOccurrenceDigest != link.CandidateOccurrenceDigest
            || decision.Readout.GrammarRevision != link.ReadoutRevision)
            throw new InvalidDataException("repository executed divergence decision custody diverges from its sealed readout");

        LoopLineageTapeEvent readoutEvent = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == link.ReadoutEventID);
        if (readoutEvent.EventID != link.ReadoutEventID
            || readoutEvent.Source != "repository:lineage"
            || readoutEvent.Provenance != Provenances.Execution
            || readoutEvent.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
            || Convert.ToHexStringLower(SHA256.HashData(readoutEvent.Payload.Span)) != link.ReadoutPayloadSHA256
            || !TapePacketCreator.TryReadRepositoryLineageReceipt(readoutEvent.Payload.Span, out string readoutKind, out string readoutCanonical, out _)
            || readoutKind != "readout"
            || !RepositoryLineageReceiptCodec.TrySplit(readoutCanonical, out string[] readoutFields)
            || readoutFields.Length != 40
            || readoutFields[1] != link.PolicyID.Value
            || readoutFields[2] != link.CandidateDigest.ToString()
            || readoutFields[3] != link.CandidateCanonical
            || readoutFields[4] != link.DecisionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[5] != link.DecisionEventID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[6] != link.ReadoutFingerprint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[7] != link.CandidateFingerprint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[8] != link.CandidateOccurrenceDigest.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[9] != link.ReadoutRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[32] != RepositoryLineageReceiptCodec.CanonicalState(link.CanonicalState)
            || readoutFields[36] != link.FrontierAuthoritySHA256
            || readoutFields[37] != link.FrontierRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[38] != link.SelectionOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || readoutFields[39] != link.CandidateSpecies.ToString())
            throw new InvalidDataException("repository executed divergence readout custody diverges from its sealed selection");

        LoopLineageTapeEvent fundingEvent = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == link.FundingEventID);
        if (fundingEvent.EventID != link.FundingEventID
            || fundingEvent.Source != "policy:" + link.PolicyID.Value
            || fundingEvent.Provenance != Provenances.Execution
            || fundingEvent.Roles != TapeEventRoles.AuditOnly
            || Convert.ToHexStringLower(SHA256.HashData(fundingEvent.Payload.Span)) != link.FundingPayloadSHA256
            || !TapePacketCreator.TryDecodePolicyTrialQuota(fundingEvent.Payload.Span, out CortexPolicyTrialQuotaDecision funding, out _, out bool hasReadoutFingerprint)
            || !hasReadoutFingerprint
            || !funding.HasCanonicalState
            || funding.Policy != link.PolicyID
            || funding.QuotaDecisionID != link.QuotaDecisionID
            || funding.ReadoutFingerprint != link.ReadoutFingerprint.Value
            || funding.CandidateFingerprint != link.CandidateFingerprint.Value
            || funding.CanonicalState != link.CanonicalState)
            throw new InvalidDataException("repository executed divergence payment custody diverges from its sealed readout");

        ValidateOptionalPolicyPacket(link.PolicyID, link.BoundaryEventID, link.BoundaryPayloadSHA256, "boundary");
        ValidateOptionalPolicyPacket(link.PolicyID, link.SettlementEventID, link.SettlementPayloadSHA256, "settlement");
    }

    private void ValidateOptionalPolicyPacket(
        CortexPolicyID policy,
        TapeEventID eventID,
        string payloadSHA256,
        string name)
    {
        if (eventID.Value == 0)
        {
            if (payloadSHA256.Length != 0)
                throw new InvalidDataException($"repository link {name} packet carries a payload without an event");
            return;
        }
        LoopLineageTapeEvent packet = Input.Tape.Events
            .SingleOrDefault(eventRecord => eventRecord.EventID == eventID);
        if (packet.EventID != eventID
            || packet.Source != "policy:" + policy.Value
            || packet.Provenance != Provenances.Execution
            || packet.Roles != TapeEventRoles.AuditOnly
            || Convert.ToHexStringLower(SHA256.HashData(packet.Payload.Span)) != payloadSHA256
            || !Input.Journal.Rows.Any(row => row.EventID == eventID && row.Source == packet.Source))
            throw new InvalidDataException($"repository link {name} packet is not sealed under its policy authority");
    }

    private static bool TryReadBoundaryOutcomeFields(
        ReadOnlySpan<byte> payload,
        out Dictionary<string, string> fields)
    {
        fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] tokens = Encoding.ASCII.GetString(payload).Split('\t');
        if (tokens.Length < 2 || tokens[0] != "POLICY-BOUNDARY-OUTCOME") return false;
        foreach (string token in tokens[1..])
        {
            int separator = token.IndexOf('=');
            if (separator <= 0 || !fields.TryAdd(token[..separator], token[(separator + 1)..])) return false;
        }
        string[] required = ["policy", "decision", "funding", "forced-decision", "forced-outcome-event", "forced-outcome-payload", "adjudication", "execution-fork", "execution-child"];
        return required.All(fields.ContainsKey);
    }

    private static bool MatchesLoopLinkReceipt(
        RepositoryLoopClosureLinkEvidence evidence,
        string kind,
        string canonical,
        string digest)
    {
        if (kind != "loop-link"
            || digest != RepositoryLineageReceiptCodec.Digest(kind, canonical))
            return false;
        LoopClosureLinkAttempt attempt;
        try { attempt = evidence.ToLinkAttempt(); attempt.Validate(); }
        catch (InvalidDataException) { return false; }
        return canonical == attempt.Canonical
            && digest == evidence.LinkPacketSHA256
            && attempt.EventID == evidence.EventID
            && attempt.LinkEventID == evidence.LinkEventID
            && attempt.LinkJournalSHA256.Value == evidence.LinkJournalSHA256;
    }
}

[RonObject]
internal partial class RepositoryNativeRegistrationRON
{
    public int schemaVersion;
    public string planID = "";
    public string sourceAuthoritySHA256 = "";
    public string worldContentSHA256 = "";
    public string worldSnapshotSHA256 = "";
    public string toolAuthoritySHA256 = "";
    public string policyAuthoritySHA256 = "";
    public string candidateAuthoritySHA256 = "";
    public string initialStateSHA256 = "";
    public ulong seed;
    public int horizon;
    public long offeredFuel;
    public string offeredFuelSHA256 = "";
    public int opportunityFloor;
    public long decisionThreshold;
    public string taskID = "";
    public byte taskSpecies;
    public string taskPrompt = "";
    public string taskAuthoritySHA256 = "";
    public RepositoryNativeTaskOracleRON taskOracle = new();
    public RepositoryNativeLineageNullSpecRON lineageNullSpec = new();
    public string registrationSHA256 = "";
}

[RonObject]
internal partial class RepositoryNativeTaskOracleRON
{
    public byte mode;
    public string sourcePath = "";
    public long sourceBytes;
    public string sourceSHA256 = "";
    public byte resultSpecies;
    public string resultSHA256 = "";
    public string resultContentBase64 = "";
    public bool predictionPresent;
    public byte predictionSpecies;
    public string predictionPath = "";
    public int predictionLine;
    public string predictionValue = "";
    public string predictionOtherPath = "";
    public string authoritySHA256 = "";
}

[RonObject]
internal partial class RepositoryNativeLineageNullSpecRON
{
    public string domain = "";
    public string algorithm = "";
    public string digest = "";
}
