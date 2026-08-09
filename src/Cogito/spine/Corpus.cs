namespace Cogito;

using System.Text;
using System.Security.Cryptography;

// ── THE WORLD POOL ──  the bonfire's fuel (FUEL.md recipe #1). The intake machinery — RLEI frontier, grok-bell
// developmental scheduler, MIX anchor, vesting — was built for a vast multi-domain world and plumbed to a ~52KB
// thimble of toy data (the engine groks at 17KB; one ladder file was nearly the whole world). This verb assembles
// the world it was built for: a DURABLE, REPRODUCIBLE, many-DISTINCT-domain corpus. The pool is WORLD-ONLY and
// RELEASE-SAFE: every root is a public, permissively-licensed repo (MIT/Apache/BSD/PD — the manifest's license
// column is the audit trail); no org-private code, no copyleft, no proprietary/leaked sources, and the pool stays
// disjoint from SWE-bench/LocAgent eval repos (benchmark hygiene) — models trained on this world are releasable.
// DISTINCTNESS is load-bearing: the grok-bell's developmental bridge-ordering only bites on NON-UNIFORM couplings
// (single-source has uniform couplings → nothing to sequence; the −0.70 universality class + the C#↔Python
// homology prove distinct languages bridge STRONG), so the manifest deliberately mixes near-pairs (C flavors,
// three Rust, two Python) with far classes (Scala vs prose, C vs TOML) — anisotropy for the bell to schedule on.
//
// The world's SHAPE is the FileCorpus contract (Radula.cs): each FILE = one domain, each LINE = one span. So the
// gather emits ONE concatenated text file per manifest domain — data/code/NN-tag.txt, NN = manifest order (making
// FileCorpus's ordinal path sort ≡ manifest order), the domain tag riding the filename — source lines verbatim
// (line structure IS the span barrier). The cortex default glob matches *.txt, so
// `cogito cortex data/code --curriculum grokbell` consumes the world bare.
//
// data/code/ is the CODE MODALITY of the training set (siblings: data/music/, data/nl/) and it is MATERIALIZED:
// the gather is a one-time copy-out — after it runs, data/code/ IS the dataset, self-contained and independent
// of the external tree (the reference clones can move or vanish; the dataset survives). The manifest
// (data/code.manifest), the license audit (data/code.LICENSES.md), and the receipt (data/code.receipt.tsv) are
// the committed provenance riding alongside — attribution + re-materialization recipe, not runtime pointers.
// The gather itself stays deterministic for re-materialization: pruned tree walk, path-hash file order (a
// deterministic shuffle, so the budget samples the WHOLE tree instead of the alphabetically-first corner),
// take-until-budget — same tree ⇒ same bytes (the Vow).
public static class Corpus
{
    // ── the gather filters (all pure functions of path/bytes — determinism holds) ──
    private const int MaxFileBytes = 512 * 1024;      // above this a "source file" is a vendored/generated blob
    private const int MinFileBytes = 32;              // below this there is no structure to learn
    private const int MaxLineChars = 4000;            // any longer line marks the file minified/generated (one line = one span; a 4KB span is not a line)
    private const int BinarySniff  = 4096;            // a NUL in the head marks the file binary

    /// Directory NAMES pruned at descent (never entered): build outputs, vendored deps, caches, cogito's own run
    /// noise. Dot-dirs (.git, .venv, .idea, …) are pruned wholesale by the walk itself.
    private static readonly string[] DeniedDirs =
        ["bin", "obj", "target", "node_modules", "dist", "build", "vendor", "__pycache__", "venv", "runs", "scratchpad", "out", "third_party"];

    /// Filename SUFFIXES denied even when a glob matches: generated faces and lockfiles (huge, uniform, machine-
    /// written — they poison distinctness without adding structure a learner should grok).
    private static readonly string[] DeniedSuffixes =
        [".g.cs", ".generated.cs", ".Designer.cs", ".min.js", ".min.css", ".lock", "-lock.json"];

    /// One manifest line: a named domain, its byte budget, the roots + filename globs that feed it, and the
    /// source's license (release-safety: the world pool ships only public permissive sources, and the license
    /// rides the manifest row + receipt so the audit is one read, not a re-derivation).
    private readonly record struct WorldDomain(string Tag, int BudgetKB, string[] Roots, string[] Globs, string License);

    /// usage: corpus gather [--manifest data/code.manifest] [--out data/code] [--scale F]
    ///        corpus materialize --out data/code-native [--manifest data/code.manifest] [--scale F] [--replace]
    ///        corpus diet --source data/code-native --out data/code-diet [--authority path] [--replace]
    ///        corpus code-block-diet --source data/code-native --out data/code-block-diet [--authority path] [--replace]
    ///        --scale multiplies every domain budget (0.1 = a quick 1/10th smoke world).
    public static int Run(string[] args)
    {
        var sub = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : "gather";
        if (sub is not ("gather" or "materialize" or "diet" or "code-block-diet"))
        {
            Console.Error.WriteLine($"  cogito corpus: unknown subverb '{sub}'");
            Console.Error.WriteLine("  usage: corpus gather [--manifest data/code.manifest] [--out data/code] [--scale F]");
            Console.Error.WriteLine("         corpus materialize --out data/code-native [--manifest data/code.manifest] [--scale F] [--replace]");
            Console.Error.WriteLine("         corpus diet --source data/code-native --out data/code-diet [--authority path] [--replace]");
            Console.Error.WriteLine("         corpus code-block-diet --source data/code-native --out data/code-block-diet [--authority path] [--replace]");
            return 2;
        }
        string manifestPath = Args.Str(args, "--manifest", Path.Combine("data", "code.manifest"));
        string outDir       = Args.Str(args, "--out", Path.Combine("data", sub == "materialize" ? "code-native" : sub == "code-block-diet" ? "code-block-diet" : "code"));
        double scale        = Args.Double(args, "--scale", 1.0);
        string sourceDir    = Args.Str(args, "--source", "");
        string authorityPath = Args.Str(args, "--authority", "");
        if (sub == "materialize" && !Args.Has(args, "--out"))
        {
            Console.Error.WriteLine("  corpus materialize: --out is required (use a fresh data/code-native destination)");
            return 2;
        }
        if (sub is "diet" or "code-block-diet")
        {
            if (sourceDir.Length == 0 || !Args.Has(args, "--out"))
            {
                Console.Error.WriteLine($"  corpus {sub}: --source and --out are required (source is a frozen source-native world; out is a fresh diet destination)");
                return 2;
            }
            if (authorityPath.Length == 0) authorityPath = sourceDir.TrimEnd('/', '\\') + ".source-native.tsv";
            return sub == "code-block-diet"
                ? CodeBlockDiet(sourceDir, authorityPath, outDir, Args.Has(args, "--replace"))
                : Diet(sourceDir, authorityPath, outDir, Args.Has(args, "--replace"));
        }
        return sub == "materialize"
            ? AssembleWorld(manifestPath, outDir, scale, Args.Has(args, "--replace"))
            : Gather(manifestPath, outDir, scale);
    }

    private const int DietOccurrenceCount = 384;
    private const int DietDomainCount = 4;
    private const int DietDomainSize = DietOccurrenceCount / DietDomainCount;
    private const string DietSelectorContract = WorldNoveltyRegistration.RegisteredDietSelectorID;
    private const int CodeBlockDomainCount = 4;
    private const int CodeBlockDomainSize = 96;
    private const string CodeBlockSelectorContract = "r20-code-block-v1";
    private const string CodeBlockEligibilityContract = "manifest-tag-allowlist-v1";
    private const string CodeBlockCandidateUniverseContract = "ordered-window-ranks-v1";
    private static readonly string[] CodeBlockEligibleTags = [
        "csharp-world", "cpp-world", "c-sqlite", "c-curl", "rust-grep", "rust-async", "rust-term",
        "go-web", "go-cli", "python-types", "python-libs", "ts-world", "scala-world"];

    private readonly record struct DietOccurrence(
        string SourcePath,
        string MaterializedPath,
        int SourceLineOrdinal,
        string RawSHA256,
        int RawBytes,
        byte[] Payload,
        string PayloadSHA256);

    private readonly record struct CodeBlockLine(
        int PhysicalLineOrdinal,
        int NormalizedNonblankOrdinal,
        string RawSHA256,
        int RawBytes,
        byte[] Payload,
        string PayloadSHA256);

    private readonly record struct CodeBlockCandidate(
        int SourceDomain,
        string Tag,
        string SourcePath,
        string MaterializedPath,
        int StartPhysicalLineOrdinal,
        int EndPhysicalLineOrdinal,
        int StartNormalizedNonblankOrdinal,
        int EndNormalizedNonblankOrdinal,
        int StartLineIndex,
        string TagRank,
        string FileRank,
        string WindowRank);

    private readonly record struct CodeBlockSelection(
        CodeBlockCandidate Block,
        int TagCandidateCount,
        int FileCandidateCount,
        int WindowCandidateCount);

    /// Derive the fixed R20 world from ordinary source occurrences. Selection is deliberately
    /// role-oblivious: only frozen source authority, path, line ordinal, and payload bytes enter
    /// the selector. The resulting four files are the runtime schedule domains; the authority is
    /// kept beside the directory so the default runtime glob cannot ingest its metadata.
    public static int Diet(string sourceDir, string authorityPath, string outDir, bool replace = false)
    {
        string source = Path.GetFullPath(sourceDir);
        string authority = Path.GetFullPath(authorityPath);
        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"  corpus diet: source-native directory does not exist: '{source}'");
            return 1;
        }
        if (!File.Exists(authority))
        {
            Console.Error.WriteLine($"  corpus diet: source-native authority does not exist: '{authority}'");
            return 1;
        }

        byte[] authorityBytes = File.ReadAllBytes(authority);
        string authoritySHA256 = Convert.ToHexStringLower(SHA256.HashData(authorityBytes));
        string authorityDigestPath = SourceNativeDigestPath(authority);
        if (!File.Exists(authorityDigestPath))
        {
            Console.Error.WriteLine($"  corpus diet: source-native authority digest does not exist: '{authorityDigestPath}'");
            return 1;
        }
        string declaredAuthoritySHA256 = File.ReadAllText(authorityDigestPath).Trim();
        if (!string.Equals(declaredAuthoritySHA256, authoritySHA256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"  corpus diet: source-native authority digest mismatch (declared {declaredAuthoritySHA256}, observed {authoritySHA256})");
            return 1;
        }

        List<SourceNativeEntry> entries;
        try { entries = ReadSourceNativeEntries(authorityBytes, authority); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  corpus diet: invalid source-native authority: {ex.Message}");
            return 1;
        }

        List<DietOccurrence> occurrences = new();
        var occurrenceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SourceNativeEntry entry in entries)
        {
            string materialized = Path.GetFullPath(Path.Combine(source, entry.MaterializedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(materialized, source))
            {
                Console.Error.WriteLine($"  corpus diet: authority path escapes source-native root: '{entry.MaterializedPath}'");
                return 1;
            }
            if (!File.Exists(materialized))
            {
                Console.Error.WriteLine($"  corpus diet: authority file is missing: '{entry.MaterializedPath}'");
                return 1;
            }
            byte[] fileBytes = File.ReadAllBytes(materialized);
            string fileSHA256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
            if (fileBytes.LongLength != entry.Bytes || !string.Equals(fileSHA256, entry.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"  corpus diet: materialized authority mismatch: '{entry.MaterializedPath}'");
                return 1;
            }

            int sourceLineOrdinal = 0;
            foreach (string raw in File.ReadLines(materialized))
            {
                string payloadText = raw.TrimEnd();
                if (payloadText.Trim().Length == 0) { sourceLineOrdinal++; continue; }
                string occurrenceKey = entry.MaterializedPath + "\0" + sourceLineOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!occurrenceKeys.Add(occurrenceKey))
                {
                    Console.Error.WriteLine($"  corpus diet: duplicate source occurrence in authority: '{entry.MaterializedPath}' line {sourceLineOrdinal}");
                    return 1;
                }
                byte[] rawBytes = Encoding.UTF8.GetBytes(raw);
                byte[] payload = Encoding.UTF8.GetBytes(payloadText);
                occurrences.Add(new DietOccurrence(
                    entry.SourcePath,
                    entry.MaterializedPath,
                    sourceLineOrdinal,
                    Convert.ToHexStringLower(SHA256.HashData(rawBytes)),
                    rawBytes.Length,
                    payload,
                    Convert.ToHexStringLower(SHA256.HashData(payload))));
                sourceLineOrdinal++;
            }
        }

        if (occurrences.Count < DietOccurrenceCount)
        {
            Console.Error.WriteLine($"  corpus diet: source-native world has only {occurrences.Count} nonblank occurrences; need {DietOccurrenceCount}");
            return 1;
        }

        List<DietOccurrence> ranked = occurrences
            .Select(occurrence => (Occurrence: occurrence, Rank: RankOccurrence(authoritySHA256, occurrence)))
            .OrderBy(value => value.Rank, StringComparer.Ordinal)
            .ThenBy(value => value.Occurrence.MaterializedPath, StringComparer.Ordinal)
            .ThenBy(value => value.Occurrence.SourceLineOrdinal)
            .ThenBy(value => value.Occurrence.PayloadSHA256, StringComparer.Ordinal)
            .Take(DietOccurrenceCount)
            .Select(value => value.Occurrence)
            .ToList();

        string destination = Path.GetFullPath(outDir);
        if (IsWithin(destination, source) || IsWithin(source, destination))
        {
            Console.Error.WriteLine("  corpus diet: destination must be disjoint from the frozen source-native directory");
            return 2;
        }
        if (File.Exists(destination))
        {
            Console.Error.WriteLine($"  corpus diet: destination is a file: '{destination}'");
            return 2;
        }
        if (Directory.Exists(destination))
        {
            bool nonempty = Directory.EnumerateFileSystemEntries(destination).Any();
            if (nonempty && !replace)
            {
                Console.Error.WriteLine($"  corpus diet: refusing non-empty destination '{destination}' (pass --replace to replace it)");
                return 2;
            }
            if (replace) Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(destination);

        for (int epoch = 0; epoch < DietDomainCount; epoch++)
        {
            string path = Path.Combine(destination, $"domain-{epoch:D2}.txt");
            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
            writer.NewLine = "\n";
            for (int ordinal = 0; ordinal < DietDomainSize; ordinal++)
                writer.WriteLine(Encoding.UTF8.GetString(ranked[epoch * DietDomainSize + ordinal].Payload));
        }

        string runtimeWorldSHA256 = FileCorpus.ComputeWorldSHA256(destination, "*.txt");
        if (!ValidateDietFiles(destination, runtimeWorldSHA256))
        {
            Console.Error.WriteLine("  corpus diet: final diet validation failed");
            return 1;
        }

        string authorityOutput = destination.TrimEnd('/', '\\') + ".diet.tsv";
        string report = BuildDietAuthority(ranked, authoritySHA256, runtimeWorldSHA256);
        File.WriteAllText(authorityOutput, report, new UTF8Encoding(false));
        string reportSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(report)));
        File.WriteAllText(destination.TrimEnd('/', '\\') + ".diet.sha256", reportSHA256 + "\n", new UTF8Encoding(false));
        Console.WriteLine($"source-occurrence diet · {DietOccurrenceCount} lines · {DietDomainCount}×{DietDomainSize} → {destination}/");
        Console.WriteLine($"  source authority · {authority} · {authoritySHA256}");
        Console.WriteLine($"  runtime world    · {runtimeWorldSHA256}");
        Console.WriteLine($"  line authority   · {authorityOutput} · {reportSHA256}");
        return 0;
    }

    private static List<SourceNativeEntry> ReadSourceNativeEntries(byte[] authorityBytes, string authorityPath)
    {
        string text = new UTF8Encoding(false, true).GetString(authorityBytes);
        string[] lines = text.Split('\n');
        if (lines.Length < 4 || lines[0].TrimEnd('\r') != "schema\t1" ||
            lines[3].TrimEnd('\r') != "domain\ttag\tordinal\tsource_path\tmaterialized_path\tbytes\tsha256")
            throw new InvalidDataException($"{authorityPath}: expected source-native authority schema 1");

        var entries = new List<SourceNativeEntry>();
        for (int line = 4; line < lines.Length; line++)
        {
            string row = lines[line].TrimEnd('\r');
            if (row.Length == 0) continue;
            string[] fields = row.Split('\t');
            if (fields.Length != 7 || !int.TryParse(fields[0], out int domain) || !int.TryParse(fields[2], out int ordinal)
                || !long.TryParse(fields[5], out long bytes) || fields[6].Length != 64)
                throw new InvalidDataException($"{authorityPath}:{line + 1}: malformed source-native row");
            entries.Add(new SourceNativeEntry(domain, fields[1], ordinal, fields[3], fields[4], bytes, fields[6]));
        }
        if (entries.Count == 0) throw new InvalidDataException($"{authorityPath}: no source-native entries");
        return entries;
    }

    private static string RankOccurrence(string authoritySHA256, DietOccurrence occurrence)
    {
        string key = string.Join('\n', DietSelectorContract, authoritySHA256, occurrence.MaterializedPath,
            occurrence.SourceLineOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture), occurrence.PayloadSHA256);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// Derive the fresh R20 code-block world from the frozen source-native authority. This
    /// successor selector is intentionally source-local: it sees only manifest tags, source
    /// paths, physical line ordinals, and normalized source bytes. No token, role, policy, or
    /// lattice inspection is permitted to influence the four selected blocks.
    public static int CodeBlockDiet(string sourceDir, string authorityPath, string outDir, bool replace = false)
    {
        string source = Path.GetFullPath(sourceDir);
        string authority = Path.GetFullPath(authorityPath);
        string destination = Path.GetFullPath(outDir);
        string sourceResolved = ResolveExistingPath(source);
        string authorityResolved = ResolveExistingPath(authority);
        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"  corpus code-block-diet: source-native directory does not exist: '{source}'");
            return 1;
        }
        if (!File.Exists(authority))
        {
            Console.Error.WriteLine($"  corpus code-block-diet: source-native authority does not exist: '{authority}'");
            return 1;
        }
        string destinationResolved = Directory.Exists(destination) ? ResolveExistingPath(destination) : destination;
        if (IsWithin(destinationResolved, sourceResolved) || IsWithin(sourceResolved, destinationResolved))
        {
            Console.Error.WriteLine("  corpus code-block-diet: destination must be disjoint from the frozen source-native directory");
            return 2;
        }
        string authorityDigestPath = SourceNativeDigestPath(authority);
        string authorityDigestResolved = ResolveExistingPath(authorityDigestPath);
        if (IsWithin(authority, destination) || IsWithin(authorityDigestPath, destination)
            || IsWithin(authorityResolved, destinationResolved) || IsWithin(authorityDigestResolved, destinationResolved))
        {
            Console.Error.WriteLine("  corpus code-block-diet: source-native authority must be outside the output destination");
            return 2;
        }
        if (File.Exists(destination))
        {
            Console.Error.WriteLine($"  corpus code-block-diet: destination is a file: '{destination}'");
            return 2;
        }
        if (Directory.Exists(destination))
        {
            bool nonempty = Directory.EnumerateFileSystemEntries(destination).Any();
            if (nonempty && !replace)
            {
                Console.Error.WriteLine($"  corpus code-block-diet: refusing non-empty destination '{destination}' (pass --replace to replace it)");
                return 2;
            }
            if (replace) Directory.Delete(destination, recursive: true);
        }

        byte[] authorityBytes;
        string authoritySHA256;
        try
        {
            authorityBytes = File.ReadAllBytes(authority);
            authoritySHA256 = Convert.ToHexStringLower(SHA256.HashData(authorityBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  corpus code-block-diet: cannot read source-native authority: {ex.Message}");
            return 1;
        }

        if (!File.Exists(authorityDigestPath))
        {
            Console.Error.WriteLine($"  corpus code-block-diet: source-native authority digest does not exist: '{authorityDigestPath}'");
            return 1;
        }
        string declaredAuthoritySHA256;
        try { declaredAuthoritySHA256 = File.ReadAllText(authorityDigestPath).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  corpus code-block-diet: cannot read source-native authority digest: {ex.Message}");
            return 1;
        }
        if (!string.Equals(declaredAuthoritySHA256, authoritySHA256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"  corpus code-block-diet: source-native authority digest mismatch (declared {declaredAuthoritySHA256}, observed {authoritySHA256})");
            return 1;
        }

        List<SourceNativeEntry> entries;
        try { entries = ReadSourceNativeEntries(authorityBytes, authority); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  corpus code-block-diet: invalid source-native authority: {ex.Message}");
            return 1;
        }

        var seenMaterializedPaths = new HashSet<string>(StringComparer.Ordinal);
        var domainTags = new Dictionary<int, string>();
        var domainOrdinals = new HashSet<string>(StringComparer.Ordinal);
        var eligibleTags = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<CodeBlockCandidate>();
        var sourceLines = new Dictionary<string, CodeBlockLine[]>(StringComparer.Ordinal);
        foreach (SourceNativeEntry entry in entries)
        {
            if (entry.Domain < 0 || entry.Ordinal < 0)
            {
                Console.Error.WriteLine($"  corpus code-block-diet: authority domain/ordinal must be non-negative: domain={entry.Domain} ordinal={entry.Ordinal}");
                return 1;
            }
            if (domainTags.TryGetValue(entry.Domain, out string? domainTag)
                && !string.Equals(domainTag, entry.Tag, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"  corpus code-block-diet: authority maps source domain {entry.Domain} to multiple tags ('{domainTag}' and '{entry.Tag}')");
                return 1;
            }
            domainTags[entry.Domain] = entry.Tag;
            if (!domainOrdinals.Add(entry.Domain.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0" + entry.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                Console.Error.WriteLine($"  corpus code-block-diet: authority repeats source domain/ordinal {entry.Domain}/{entry.Ordinal}");
                return 1;
            }
            string materialized = Path.GetFullPath(Path.Combine(source, entry.MaterializedPath.Replace('/', Path.DirectorySeparatorChar)));
            string materializedResolved = ResolveExistingPath(materialized);
            if (!IsWithin(materializedResolved, sourceResolved))
            {
                Console.Error.WriteLine($"  corpus code-block-diet: authority path escapes source-native root: '{entry.MaterializedPath}'");
                return 1;
            }
            if (!seenMaterializedPaths.Add(materialized))
            {
                Console.Error.WriteLine($"  corpus code-block-diet: authority lists materialized file more than once: '{entry.MaterializedPath}'");
                return 1;
            }
            if (!File.Exists(materialized))
            {
                Console.Error.WriteLine($"  corpus code-block-diet: authority file is missing: '{entry.MaterializedPath}'");
                return 1;
            }

            byte[] fileBytes;
            try { fileBytes = File.ReadAllBytes(materialized); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"  corpus code-block-diet: cannot read materialized authority file '{entry.MaterializedPath}': {ex.Message}");
                return 1;
            }
            string fileSHA256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
            if (fileBytes.LongLength != entry.Bytes || !string.Equals(fileSHA256, entry.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"  corpus code-block-diet: materialized authority mismatch: '{entry.MaterializedPath}'");
                return 1;
            }
            if (!IsCodeBlockEligibleTag(entry.Tag)) continue;
            eligibleTags.Add(entry.Tag);

            CodeBlockLine[] lines;
            try { lines = ReadCodeBlockLines(materialized); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"  corpus code-block-diet: cannot read materialized source lines: '{entry.MaterializedPath}' ({ex.Message})");
                return 1;
            }
            if (lines.Length < CodeBlockDomainSize) continue;
            sourceLines.Add(entry.MaterializedPath, lines);

            string tagRank = RankCodeBlockTag(authoritySHA256, entry.Tag);
            string fileRank = RankCodeBlockFile(authoritySHA256, entry.Tag, entry.MaterializedPath);
            for (int start = 0; start <= lines.Length - CodeBlockDomainSize; start++)
            {
                int end = start + CodeBlockDomainSize - 1;
                int startOrdinal = lines[start].PhysicalLineOrdinal;
                int endOrdinal = lines[end].PhysicalLineOrdinal;
                int startNormalizedOrdinal = lines[start].NormalizedNonblankOrdinal;
                int endNormalizedOrdinal = lines[end].NormalizedNonblankOrdinal;
                string rankKey = string.Join('\n', CodeBlockSelectorContract, authoritySHA256, entry.Tag,
                    entry.MaterializedPath, startOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    endOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string windowRank = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rankKey)));
                candidates.Add(new CodeBlockCandidate(entry.Domain, entry.Tag, entry.SourcePath, entry.MaterializedPath,
                    startOrdinal, endOrdinal, startNormalizedOrdinal, endNormalizedOrdinal,
                    start, tagRank, fileRank, windowRank));
            }
        }

        foreach (IGrouping<int, SourceNativeEntry> domainEntries in entries.GroupBy(entry => entry.Domain))
        {
            int expectedOrdinal = 0;
            foreach (SourceNativeEntry entry in domainEntries.OrderBy(entry => entry.Ordinal))
                if (entry.Ordinal != expectedOrdinal++)
                {
                    Console.Error.WriteLine($"  corpus code-block-diet: source domain {domainEntries.Key} ordinals are not contiguous from zero");
                    return 1;
                }
        }

        if (eligibleTags.Count < CodeBlockDomainCount)
        {
            Console.Error.WriteLine($"  corpus code-block-diet: source-native authority exposes only {eligibleTags.Count} distinct eligible manifest tags; need at least {CodeBlockDomainCount}");
            return 1;
        }

        string candidateUniverseSHA256 = BuildCodeBlockCandidateUniverseDigest(candidates);
        var selected = new List<CodeBlockSelection>(CodeBlockDomainCount);
        var selectedPaths = new HashSet<string>(StringComparer.Ordinal);
        var selectedTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, CodeBlockCandidate> tagGroup in candidates.GroupBy(candidate => candidate.Tag, StringComparer.Ordinal)
            .OrderBy(group => group.Min(candidate => candidate.TagRank), StringComparer.Ordinal)
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            if (selected.Count == CodeBlockDomainCount) break;
            if (selectedTags.Contains(tagGroup.Key)) continue;
            int tagCandidateCount = tagGroup.Count();
            int fileCandidateCount = tagGroup.Select(candidate => candidate.MaterializedPath).Distinct(StringComparer.Ordinal).Count();
            foreach (IGrouping<string, CodeBlockCandidate> fileGroup in tagGroup.GroupBy(candidate => candidate.MaterializedPath, StringComparer.Ordinal)
                .OrderBy(group => group.Min(candidate => candidate.FileRank), StringComparer.Ordinal)
                .ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                if (!selectedPaths.Add(fileGroup.Key)) continue;
                selectedTags.Add(tagGroup.Key);
                CodeBlockCandidate candidate = fileGroup
                    .OrderBy(window => window.WindowRank, StringComparer.Ordinal)
                    .ThenBy(window => window.StartPhysicalLineOrdinal)
                    .ThenBy(window => window.EndPhysicalLineOrdinal)
                    .First();
                selected.Add(new CodeBlockSelection(candidate, tagCandidateCount, fileCandidateCount, fileGroup.Count()));
                break;
            }
        }
        if (selected.Count != CodeBlockDomainCount)
        {
            Console.Error.WriteLine($"  corpus code-block-diet: eligible source yielded {selected.Count} distinct file/tag blocks; need {CodeBlockDomainCount}");
            return 1;
        }

        Directory.CreateDirectory(destination);
        for (int domain = 0; domain < selected.Count; domain++)
        {
            string path = Path.Combine(destination, CodeBlockFileName(domain, selected[domain].Block.Tag));
            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
            writer.NewLine = "\n";
            CodeBlockCandidate block = selected[domain].Block;
            CodeBlockLine[] lines = sourceLines[block.MaterializedPath];
            for (int offset = 0; offset < CodeBlockDomainSize; offset++)
                writer.WriteLine(Encoding.UTF8.GetString(lines[block.StartLineIndex + offset].Payload));
        }

        string runtimeWorldSHA256 = FileCorpus.ComputeWorldSHA256(destination, "*.txt");
        string normalizedPayloadSHA256 = ComputeCodeBlockPayloadSHA256(selected, sourceLines);
        string eligibleTagsSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', eligibleTags.OrderBy(tag => tag, StringComparer.Ordinal)))));
        if (!ValidateCodeBlockFiles(destination, selected, runtimeWorldSHA256))
        {
            Console.Error.WriteLine("  corpus code-block-diet: final code-block world validation failed");
            return 1;
        }

        string authorityOutput = destination.TrimEnd('/', '\\') + ".diet.tsv";
        string report = BuildCodeBlockAuthority(selected, eligibleTags, candidates.Count, authoritySHA256, runtimeWorldSHA256,
            normalizedPayloadSHA256, candidateUniverseSHA256, eligibleTagsSHA256, sourceLines);
        File.WriteAllText(authorityOutput, report, new UTF8Encoding(false));
        string reportSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(report)));
        File.WriteAllText(destination.TrimEnd('/', '\\') + ".diet.sha256", reportSHA256 + "\n", new UTF8Encoding(false));
        Console.WriteLine($"code-block diet · selector={CodeBlockSelectorContract} · {CodeBlockDomainCount}×{CodeBlockDomainSize} → {destination}/");
        Console.WriteLine($"  source authority · {authority} · {authoritySHA256}");
        Console.WriteLine($"  runtime world    · {runtimeWorldSHA256}");
        Console.WriteLine($"  normalized bytes · {normalizedPayloadSHA256}");
        Console.WriteLine($"  candidates       · {candidates.Count} · {candidateUniverseSHA256}");
        Console.WriteLine($"  block authority  · {authorityOutput} · {reportSHA256}");
        return 0;
    }

    private static CodeBlockLine[] ReadCodeBlockLines(string path)
    {
        var lines = new List<CodeBlockLine>();
        int normalizedOrdinal = 0;
        int physicalOrdinal = 0;
        foreach (string raw in File.ReadLines(path))
        {
            string payloadText = raw.TrimEnd();
            if (payloadText.Trim().Length == 0) { physicalOrdinal++; continue; }
            byte[] rawBytes = Encoding.UTF8.GetBytes(raw);
            byte[] payload = Encoding.UTF8.GetBytes(payloadText);
            lines.Add(new CodeBlockLine(physicalOrdinal, normalizedOrdinal++,
                Convert.ToHexStringLower(SHA256.HashData(rawBytes)), rawBytes.Length, payload,
                Convert.ToHexStringLower(SHA256.HashData(payload))));
            physicalOrdinal++;
        }
        return lines.ToArray();
    }

    private static bool IsCodeBlockEligibleTag(string tag)
        => CodeBlockEligibleTags.Contains(tag, StringComparer.Ordinal);

    private static string RankCodeBlockTag(string authoritySHA256, string tag)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', CodeBlockSelectorContract, authoritySHA256, tag))));

    private static string RankCodeBlockFile(string authoritySHA256, string tag, string materializedPath)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', CodeBlockSelectorContract, authoritySHA256, tag, materializedPath))));

    private static string BuildCodeBlockCandidateUniverseDigest(IReadOnlyList<CodeBlockCandidate> candidates)
    {
        var sb = new StringBuilder();
        foreach (CodeBlockCandidate candidate in candidates
            .OrderBy(candidate => candidate.WindowRank, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.MaterializedPath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.StartPhysicalLineOrdinal)
            .ThenBy(candidate => candidate.EndPhysicalLineOrdinal))
        {
            sb.Append(candidate.SourceDomain).Append('\t').Append(candidate.Tag).Append('\t').Append(candidate.MaterializedPath).Append('\t')
                .Append(candidate.StartPhysicalLineOrdinal).Append('\t').Append(candidate.EndPhysicalLineOrdinal).Append('\t')
                .Append(candidate.StartNormalizedNonblankOrdinal).Append('\t').Append(candidate.EndNormalizedNonblankOrdinal).Append('\t')
                .Append(candidate.TagRank).Append('\t').Append(candidate.FileRank).Append('\t').Append(candidate.WindowRank).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static string ComputeCodeBlockPayloadSHA256(IReadOnlyList<CodeBlockSelection> selected,
        IReadOnlyDictionary<string, CodeBlockLine[]> sourceLines)
    {
        using var payload = new MemoryStream();
        foreach (CodeBlockSelection selection in selected)
        {
            CodeBlockCandidate block = selection.Block;
            CodeBlockLine[] lines = sourceLines[block.MaterializedPath];
            for (int offset = 0; offset < CodeBlockDomainSize; offset++)
            {
                CodeBlockLine line = lines[block.StartLineIndex + offset];
                payload.Write(line.Payload);
                payload.WriteByte((byte)'\n');
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(payload.ToArray()));
    }

    private static bool ValidateCodeBlockFiles(string destination, IReadOnlyList<CodeBlockSelection> selected,
        string runtimeWorldSHA256)
    {
        string[] files = Directory.GetFiles(destination, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray()!;
        string[] expected = Enumerable.Range(0, CodeBlockDomainCount)
            .Select(i => CodeBlockFileName(i, selected[i].Block.Tag))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!files.SequenceEqual(expected, StringComparer.Ordinal)) return false;
        if (!string.Equals(runtimeWorldSHA256, FileCorpus.ComputeWorldSHA256(destination, "*.txt"), StringComparison.Ordinal)) return false;
        foreach (string file in files)
        {
            int count = 0;
            foreach (string raw in File.ReadLines(Path.Combine(destination, file)))
                if (raw.TrimEnd().Trim().Length != 0) count++;
            if (count != CodeBlockDomainSize) return false;
        }
        return true;
    }

    private static string CodeBlockFileName(int domain, string tag) => $"domain-{domain:D2}-{tag}.txt";

    private static string ResolveExistingPath(string path)
    {
        string full = Path.GetFullPath(path);
        string cursor = full;
        var suffix = new Stack<string>();
        while (!File.Exists(cursor) && !Directory.Exists(cursor))
        {
            string? parent = Directory.GetParent(cursor)?.FullName;
            if (parent is null) return full;
            suffix.Push(Path.GetFileName(cursor));
            cursor = parent;
        }
        try
        {
            FileSystemInfo info = Directory.Exists(cursor) ? new DirectoryInfo(cursor) : new FileInfo(cursor);
            string resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? cursor;
            while (suffix.Count > 0) resolved = Path.Combine(resolved, suffix.Pop());
            return resolved;
        }
        catch (IOException) { return full; }
        catch (UnauthorizedAccessException) { return full; }
    }

    private static string BuildCodeBlockAuthority(IReadOnlyList<CodeBlockSelection> selected,
        IEnumerable<string> eligibleTags, int candidateCount, string sourceAuthoritySHA256, string runtimeWorldSHA256,
        string normalizedPayloadSHA256, string candidateUniverseSHA256, string eligibleTagsSHA256,
        IReadOnlyDictionary<string, CodeBlockLine[]> sourceLines)
    {
        var sb = new StringBuilder("schema\t1\n");
        sb.Append("selector_id\t").Append(CodeBlockSelectorContract).Append('\n')
            .Append("source_native_authority_sha256\t").Append(sourceAuthoritySHA256).Append('\n')
            .Append("world_sha256\t").Append(runtimeWorldSHA256).Append('\n')
            .Append("byte_world_sha256\t").Append(runtimeWorldSHA256).Append('\n')
            .Append("normalized_payload_sha256\t").Append(normalizedPayloadSHA256).Append('\n')
            .Append("candidate_count\t").Append(candidateCount).Append('\n')
            .Append("candidate_universe_sha256\t").Append(candidateUniverseSHA256).Append('\n')
            .Append("candidate_universe_contract\t").Append(CodeBlockCandidateUniverseContract).Append('\n')
            .Append("eligible_manifest_tags_sha256\t").Append(eligibleTagsSHA256).Append('\n')
            .Append("eligibility_contract\t").Append(CodeBlockEligibilityContract).Append('\n')
            .Append("normalization_contract\ttrim-end-v1\n")
            .Append("block_contiguity\tconsecutive-nonblank-v1\n")
            .Append("eligibility_tags\t").Append(string.Join(',', CodeBlockEligibleTags)).Append('\n')
            .Append("eligible_manifest_tags\t").Append(string.Join(',', eligibleTags.OrderBy(tag => tag, StringComparer.Ordinal))).Append('\n')
            .Append("domain_count\t").Append(CodeBlockDomainCount).Append('\n')
            .Append("lines_per_domain\t").Append(CodeBlockDomainSize).Append('\n')
            .Append("source_blocks\t").Append(selected.Count).Append('\n')
            .Append("domain\tordinal\tsource_domain\ttag\tsource_path\tmaterialized_path\ttag_rank\tfile_rank\twindow_rank\ttag_candidate_count\tfile_candidate_count\twindow_candidate_count\tblock_start_physical_line_ordinal\tblock_end_physical_line_ordinal\tblock_start_normalized_nonblank_ordinal\tblock_end_normalized_nonblank_ordinal\tphysical_line_ordinal\tnormalized_nonblank_ordinal\traw_sha256\traw_bytes\tpayload_sha256\tpayload_bytes\tselector_id\tworld_sha256\n");
        for (int domain = 0; domain < selected.Count; domain++)
        {
            CodeBlockSelection selection = selected[domain];
            CodeBlockCandidate block = selection.Block;
            CodeBlockLine[] lines = sourceLines[block.MaterializedPath];
            for (int ordinal = 0; ordinal < CodeBlockDomainSize; ordinal++)
            {
                CodeBlockLine line = lines[block.StartLineIndex + ordinal];
                sb.Append(domain).Append('\t').Append(ordinal).Append('\t').Append(block.SourceDomain).Append('\t').Append(block.Tag).Append('\t')
                    .Append(block.SourcePath).Append('\t').Append(block.MaterializedPath).Append('\t')
                    .Append(block.TagRank).Append('\t').Append(block.FileRank).Append('\t').Append(block.WindowRank).Append('\t')
                    .Append(selection.TagCandidateCount).Append('\t').Append(selection.FileCandidateCount).Append('\t').Append(selection.WindowCandidateCount).Append('\t')
                    .Append(block.StartPhysicalLineOrdinal).Append('\t').Append(block.EndPhysicalLineOrdinal).Append('\t')
                    .Append(block.StartNormalizedNonblankOrdinal).Append('\t').Append(block.EndNormalizedNonblankOrdinal).Append('\t')
                    .Append(line.PhysicalLineOrdinal).Append('\t').Append(line.NormalizedNonblankOrdinal).Append('\t')
                    .Append(line.RawSHA256).Append('\t').Append(line.RawBytes).Append('\t')
                    .Append(line.PayloadSHA256).Append('\t').Append(line.Payload.Length).Append('\t')
                    .Append(CodeBlockSelectorContract).Append('\t').Append(runtimeWorldSHA256).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static bool ValidateDietFiles(string destination, string runtimeWorldSHA256)
    {
        string[] files = Directory.GetFiles(destination, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray()!;
        string[] expected = Enumerable.Range(0, DietDomainCount).Select(i => $"domain-{i:D2}.txt").ToArray();
        if (!files.SequenceEqual(expected, StringComparer.Ordinal)) return false;
        if (!string.Equals(runtimeWorldSHA256, FileCorpus.ComputeWorldSHA256(destination, "*.txt"), StringComparison.Ordinal)) return false;
        foreach (string file in files)
        {
            int count = 0;
            foreach (string raw in File.ReadLines(Path.Combine(destination, file)))
            {
                string payload = raw.TrimEnd();
                if (payload.Trim().Length != 0) count++;
            }
            if (count != DietDomainSize) return false;
        }
        return true;
    }

    private static string BuildDietAuthority(IReadOnlyList<DietOccurrence> selected, string sourceAuthoritySHA256,
        string runtimeWorldSHA256)
    {
        var sb = new StringBuilder("schema\t1\n");
        sb.Append("selector_id\t").Append(DietSelectorContract).Append('\n')
            .Append("source_native_authority_sha256\t").Append(sourceAuthoritySHA256).Append('\n')
            .Append("world_sha256\t").Append(runtimeWorldSHA256).Append('\n')
            .Append("domain_count\t").Append(DietDomainCount).Append('\n')
            .Append("lines_per_domain\t").Append(DietDomainSize).Append('\n')
            .Append("source_occurrences\t").Append(DietOccurrenceCount).Append('\n')
            .Append("epoch\tordinal\tsource_path\tmaterialized_path\tsource_line_ordinal\traw_sha256\traw_bytes\tpayload_sha256\tpayload_bytes\tselector_id\tworld_sha256\n");
        for (int index = 0; index < selected.Count; index++)
        {
            DietOccurrence occurrence = selected[index];
            sb.Append(index / DietDomainSize).Append('\t').Append(index % DietDomainSize).Append('\t')
                .Append(occurrence.SourcePath).Append('\t').Append(occurrence.MaterializedPath).Append('\t')
                .Append(occurrence.SourceLineOrdinal).Append('\t').Append(occurrence.RawSHA256).Append('\t')
                .Append(occurrence.RawBytes).Append('\t').Append(occurrence.PayloadSHA256).Append('\t')
                .Append(occurrence.Payload.Length).Append('\t').Append(DietSelectorContract).Append('\t')
                .Append(runtimeWorldSHA256).Append('\n');
        }
        return sb.ToString();
    }

    private static string SourceNativeDigestPath(string authorityPath)
    {
        const string authoritySuffix = ".source-native.tsv";
        return authorityPath.EndsWith(authoritySuffix, StringComparison.Ordinal)
            ? authorityPath[..^authoritySuffix.Length] + ".source-native.sha256"
            : authorityPath + ".sha256";
    }

    private static bool IsWithin(string child, string root)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedChild = Path.GetFullPath(child);
        return normalizedChild.StartsWith(normalizedRoot, StringComparison.Ordinal)
            || string.Equals(normalizedChild, normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
    }

    /// Copy the ordinary source selected by a registered manifest into a self-contained world.
    /// Each selected source file remains one output file and its bytes cross this boundary unchanged;
    /// only the destination name is ordinalized so the result is portable and collision-free.
    /// Selection is a pure function of the manifest roots, relative paths, and byte budgets: no
    /// runtime policy, event, candidate, or generated trigger can influence the world.
    public static int AssembleWorld(string manifestPath, string outDir, double scale = 1.0, bool replace = false)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"  corpus materialize: no manifest at '{manifestPath}'");
            return 1;
        }
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            Console.Error.WriteLine("  corpus materialize: --scale must be finite and positive");
            return 1;
        }

        List<WorldDomain> domains = ParseManifest(manifestPath);
        if (domains.Count == 0)
        {
            Console.Error.WriteLine($"  corpus materialize: '{manifestPath}' declares no domains");
            return 1;
        }

        string destination = Path.GetFullPath(outDir);
        if (File.Exists(destination))
        {
            Console.Error.WriteLine($"  corpus materialize: destination is a file: '{destination}'");
            return 2;
        }
        if (Directory.Exists(destination))
        {
            bool nonempty = Directory.EnumerateFileSystemEntries(destination).Any();
            if (nonempty && !replace)
            {
                Console.Error.WriteLine($"  corpus materialize: refusing non-empty destination '{destination}' (pass --replace to replace it)");
                return 2;
            }
            if (replace) Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(destination);

        var entries = new List<SourceNativeEntry>();
        for (int domainIndex = 0; domainIndex < domains.Count; domainIndex++)
        {
            WorldDomain domain = domains[domainIndex];
            long budget = checked((long)(domain.BudgetKB * 1024 * scale));
            List<SourceCandidate> candidates = FindCandidates(domain);
            long selectedBytes = 0;
            int selected = 0;
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                SourceCandidate candidate = candidates[candidateIndex];
                byte[] bytes;
                try { bytes = File.ReadAllBytes(candidate.FullPath); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (bytes.Length < MinFileBytes || bytes.Length > MaxFileBytes) continue;
                if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinarySniff)) >= 0) continue;
                if (selectedBytes != 0 && selectedBytes + bytes.LongLength > budget) continue;
                if (selectedBytes == 0 && bytes.LongLength > budget) continue;

                string relative = NormalizeMaterializedPath(Path.Combine(
                    $"{domainIndex:D2}-{domain.Tag}", $"{selected:D6}-{Path.GetFileName(candidate.RelativePath)}.txt"));
                string outputPath = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, bytes);
                entries.Add(new SourceNativeEntry(domainIndex, domain.Tag, selected, $"root{candidate.RootOrdinal}:{candidate.RelativePath}",
                    relative, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))));
                selectedBytes += bytes.LongLength;
                selected++;
            }
            if (selected == 0)
                Console.Error.WriteLine($"  ⚠ domain '{domain.Tag}': no source files fit the registered budget");
        }

        string authority = SourceNativeAuthority(entries, manifestPath, scale);
        string authorityPath = destination.TrimEnd('/', '\\') + ".source-native.tsv";
        File.WriteAllText(authorityPath, authority, new UTF8Encoding(false));
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(authority)));
        string digestPath = destination.TrimEnd('/', '\\') + ".source-native.sha256";
        File.WriteAllText(digestPath, digest + "\n", new UTF8Encoding(false));

        Console.WriteLine($"source-native world · {entries.Count} files · {entries.Sum(e => e.Bytes)}B → {destination}/");
        Console.WriteLine($"  authority · {authorityPath}");
        Console.WriteLine($"  digest    · {digestPath} · {digest}");
        return entries.Count == 0 ? 1 : 0;
    }

    private static int Gather(string manifestPath, string outDir, double scale)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"  corpus gather: no manifest at '{manifestPath}' — the world recipe is missing (data/code.manifest is committed; run from the repo root)");
            return 1;
        }
        var domains = ParseManifest(manifestPath);
        if (domains.Count == 0)
        {
            Console.Error.WriteLine($"  corpus gather: '{manifestPath}' declares no domains");
            return 1;
        }

        // wholesale recreate — the world dir is a build artifact; stale domain files from an older manifest must
        // not linger as phantom domains (FileCorpus would ingest them).
        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        var rows = new List<(string NN, string Tag, int Files, int Cand, long Lines, long Bytes, string License, string Src)>();
        long totalBytes = 0;
        for (int i = 0; i < domains.Count; i++)
        {
            var d = domains[i];
            long budget = (long)(d.BudgetKB * 1024 * scale);
            var (files, cand, lines, bytes) = GatherDomain(d, Path.Combine(outDir, $"{i:D2}-{d.Tag}.txt"), budget);
            totalBytes += bytes;
            rows.Add(($"{i:D2}", d.Tag, files, cand, lines, bytes, d.License, $"{string.Join(",", d.Roots)} ({string.Join(",", d.Globs)})"));
            if (files == 0) Console.Error.WriteLine($"  ⚠ domain '{d.Tag}': 0 files taken (missing roots, or every candidate filtered) — the world is thinner than the manifest declares");
        }
        rows.RemoveAll(r => r.Files == 0);                 // an empty domain file was never written — drop it from the receipt too
        foreach (var f in Directory.GetFiles(outDir)) if (new FileInfo(f).Length == 0) File.Delete(f);

        // ── the receipt (durable, deterministic — beside the dir so no glob ever ingests it) ──
        var receiptPath = outDir.TrimEnd('/', '\\') + ".receipt.tsv";
        var tsv = new StringBuilder("nn\ttag\tfiles\tcandidates\tlines\tbytes\tlicense\tsource\n");
        foreach (var r in rows) tsv.Append($"{r.NN}\t{r.Tag}\t{r.Files}\t{r.Cand}\t{r.Lines}\t{r.Bytes}\t{r.License}\t{r.Src}\n");
        File.WriteAllText(receiptPath, tsv.ToString());

        // ── the report (the payload) + the bonfire kill-line verdicts (FUEL.md: ≥6 distinct domains, ≥5MB) ──
        Console.WriteLine($"world · {rows.Count} domains · {totalBytes / 1024.0 / 1024.0:F1} MB → {outDir}/  (receipt: {receiptPath})");
        Console.WriteLine("  nn  tag            files/cand     lines      bytes  license          source");
        foreach (var r in rows)
            Console.WriteLine($"  {r.NN}  {r.Tag,-14} {r.Files,4}/{r.Cand,-5} {r.Lines,9} {r.Bytes,10}  {r.License,-15}  {r.Src}");
        Console.WriteLine();
        Console.WriteLine($"  domains ≥6 : {(rows.Count >= 6 ? "YES" : "NO ")} ({rows.Count})");
        Console.WriteLine($"  vast ≥5MB  : {(totalBytes >= 5 * 1024 * 1024 ? "YES" : "NO ")} ({totalBytes / 1024.0 / 1024.0:F1} MB ≈ {totalBytes / (52.0 * 1024):F0}× the 52KB thimble)");
        Console.WriteLine($"  ingest     : cogito cortex {outDir} --curriculum grokbell   (default glob matches the world's *.txt)");
        return rows.Count == 0 ? 1 : 0;
    }

    // ── one domain: collect → hash-order → filter → take-until-budget → one concatenated line-file ──
    private static (int Files, int Cand, long Lines, long Bytes) GatherDomain(WorldDomain d, string outPath, long budget)
    {
        // candidates across all roots, deduped, keyed by FNV-1a(root-relative path) — the deterministic shuffle.
        var cand = new List<(ulong H, string Rel, string Full)>();
        var seen = new HashSet<string>();
        foreach (var rootRaw in d.Roots)
        {
            var root = ExpandHome(rootRaw);
            if (!Directory.Exists(root)) { Console.Error.WriteLine($"  ⚠ domain '{d.Tag}': root '{rootRaw}' does not exist — skipped"); continue; }
            var found = new List<string>();
            Walk(root, found);
            foreach (var f in found)
            {
                var name = Path.GetFileName(f);
                if (!MatchesAnyGlob(name, d.Globs) || HasDeniedSuffix(name) || !seen.Add(f)) continue;
                var rel = Path.GetRelativePath(root, f);
                cand.Add((Fnv1a(rel), rel, f));
            }
        }
        cand.Sort((a, b) => a.H != b.H ? a.H.CompareTo(b.H) : string.CompareOrdinal(a.Rel, b.Rel));

        using var w = new StreamWriter(outPath, append: false, new UTF8Encoding(false));
        w.NewLine = "\n";
        int files = 0; long lines = 0, bytes = 0;
        foreach (var (_, _, full) in cand)
        {
            if (bytes >= budget) break;
            byte[] raw;
            try { raw = File.ReadAllBytes(full); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            if (raw.Length < MinFileBytes || raw.Length > MaxFileBytes) continue;
            if (Array.IndexOf(raw, (byte)0, 0, Math.Min(raw.Length, BinarySniff)) >= 0) continue;   // binary
            var text = Encoding.UTF8.GetString(raw);
            var ls = text.Split('\n');
            bool minified = false;
            foreach (var l in ls) if (l.Length > MaxLineChars) { minified = true; break; }
            if (minified) continue;
            int last = ls.Length - 1;
            if (last >= 0 && ls[last].Length == 0) last--;                       // the trailing-\n split artifact — not a source line
            for (int i = 0; i <= last; i++)
            {
                var line = ls[i].TrimEnd('\r');                                  // CRLF → LF; content otherwise verbatim (indent preserved — it is structure)
                w.WriteLine(line);
                bytes += Encoding.UTF8.GetByteCount(line) + 1;
                lines++;
            }
            files++;
        }
        return (files, cand.Count, lines, bytes);
    }

    private readonly record struct SourceCandidate(ulong Hash, int RootOrdinal, string RelativePath, string FullPath);

    /// Resolve one manifest row into the exact ordinal path/hash candidate order used by
    /// source-native materialization. The root-relative path is the tie-breaker, so equal
    /// hashes cannot make two materializations diverge.
    private static List<SourceCandidate> FindCandidates(WorldDomain domain)
    {
        var candidates = new List<SourceCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int rootOrdinal = 0; rootOrdinal < domain.Roots.Length; rootOrdinal++)
        {
            string rootRaw = domain.Roots[rootOrdinal];
            string root = ExpandHome(rootRaw);
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"  ⚠ domain '{domain.Tag}': root '{rootRaw}' does not exist — skipped");
                continue;
            }

            var found = new List<string>();
            Walk(root, found);
            foreach (string fullPath in found)
            {
                string name = Path.GetFileName(fullPath);
                if (!MatchesAnyGlob(name, domain.Globs) || HasDeniedSuffix(name) || !seen.Add(fullPath)) continue;
                string relative = NormalizeMaterializedPath(Path.GetRelativePath(root, fullPath));
                candidates.Add(new SourceCandidate(Fnv1a(relative), rootOrdinal, relative, fullPath));
            }
        }
        candidates.Sort(static (a, b) => a.Hash != b.Hash
            ? a.Hash.CompareTo(b.Hash)
            : a.RootOrdinal != b.RootOrdinal
                ? a.RootOrdinal.CompareTo(b.RootOrdinal)
                : string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return candidates;
    }

    private readonly record struct SourceNativeEntry(int Domain, string Tag, int Ordinal,
        string SourcePath, string MaterializedPath, long Bytes, string SHA256);

    private static string SourceNativeAuthority(IReadOnlyList<SourceNativeEntry> entries, string manifestPath, double scale)
    {
        string manifestSHA256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestPath)));
        var sb = new StringBuilder("schema\t1\nmanifest_sha256\t");
        sb.Append(manifestSHA256).Append("\nscale\t")
            .Append(scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append("\n");
        sb.AppendLine("domain\ttag\tordinal\tsource_path\tmaterialized_path\tbytes\tsha256");
        foreach (SourceNativeEntry entry in entries.OrderBy(e => e.Domain).ThenBy(e => e.Ordinal))
            sb.Append(entry.Domain).Append('\t').Append(entry.Tag).Append('\t').Append(entry.Ordinal).Append('\t')
                .Append(entry.SourcePath).Append('\t').Append(entry.MaterializedPath).Append('\t').Append(entry.Bytes)
                .Append('\t').Append(entry.SHA256).Append('\n');
        return sb.ToString();
    }

    private static string NormalizeMaterializedPath(string path)
        => path.Replace('\\', '/');

    /// Pruned recursive walk — denied/dot directories are never ENTERED (a .git objects tree or node_modules would
    /// dominate the enumeration otherwise), symlinked dirs are never followed (no cycles).
    private static void Walk(string dir, List<string> into)
    {
        string[] subs, files;
        try { subs = Directory.GetDirectories(dir); files = Directory.GetFiles(dir); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
        into.AddRange(files);
        foreach (var s in subs)
        {
            var name = Path.GetFileName(s);
            if (name.Length == 0 || name[0] == '.' || Array.IndexOf(DeniedDirs, name) >= 0) continue;
            if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue;
            Walk(s, into);
        }
    }

    // ── the manifest ──  one domain per line: `tag | budgetKB | roots (comma-sep) | globs (comma-sep) | license`;
    // the license field is optional-but-expected (release-safety audit trail — every world source is public +
    // permissive, and the row says which license). '#' comments + blank lines skipped. Bespoke on purpose: cogito
    // is dependency-free (hand-rolled Args/checkpoint/JSON is the house shape), and five pipe-split fields need
    // no format machinery.
    private static List<WorldDomain> ParseManifest(string path)
    {
        var domains = new List<WorldDomain>();
        int ln = 0;
        foreach (var raw in File.ReadLines(path))
        {
            ln++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length is not (4 or 5) || !int.TryParse(parts[1], out var kb) || kb <= 0)
                throw new InvalidDataException($"{path}:{ln}: expected `tag | budgetKB | roots | globs | license`, got '{raw}'");
            domains.Add(new WorldDomain(
                parts[0],
                kb,
                parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                parts.Length == 5 ? parts[4] : ""));
        }
        return domains;
    }

    private static string ExpandHome(string p)
        => p.StartsWith("~", StringComparison.Ordinal)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..]
            : p;

    /// Minimal glob: `*.ext` suffix match or an exact filename — all the manifest needs (ordinal, case-sensitive).
    private static bool MatchesAnyGlob(string name, string[] globs)
    {
        foreach (var g in globs)
        {
            if (g.StartsWith("*", StringComparison.Ordinal)) { if (name.EndsWith(g[1..], StringComparison.Ordinal)) return true; }
            else if (name == g) return true;
        }
        return false;
    }

    private static bool HasDeniedSuffix(string name)
    {
        foreach (var s in DeniedSuffixes) if (name.EndsWith(s, StringComparison.Ordinal)) return true;
        return false;
    }

    /// FNV-1a 64 over the root-relative path — the deterministic shuffle key (stable across machines + gathers).
    private static ulong Fnv1a(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 1099511628211UL; }
        return h;
    }
}
