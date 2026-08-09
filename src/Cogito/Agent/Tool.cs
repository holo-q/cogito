namespace Cogito;

using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;

// ── TOOL ──  THE WORLD THE LOC RUNTIME ACTS ON — the deterministic codebase instrument the Cortex reaches through
// to LOCALIZE. The retrieval paradigm (BM25 beacon → rank the whole corpus at once, NavLoop.Drive) is DEAD: it never
// let the runtime ACT — it scored every site up front and re-sorted. The LOC runtime instead GENERATES a tool-call as its own
// autoregressive decision (Engine.GenerateMCMC over the grammar, conditioned on the tape-so-far), the call EXECUTES
// against this world, and the OBSERVATION appends to the Cortex tape — intrinsic generation interleaved with external
// world-data, the loopback combustion. This file is the ACT + OBSERVE half of that loop (the GENERATE half is the
// grammar's; the LEARN half is Provenance/ReplayCalc). It owns nothing about ranking.
//
// The fixture AgentWorld remains a replay adapter over sites.jsonl. RepositoryWorldSnapshot is the native crawler world:
// it captures a real source root once, retains relative paths/content/digests, and executes grep/ls/open/read without
// touching the filesystem again. Both worlds share the same deterministic action grammar and bounded observation bytes;
// only the source authority differs.
//
// THE TOOL-CALL GRAMMAR is one line: `<verb> <arg>` — `grep payload_state`, `open src/mod_matrix_0.py`, `ls src`,
// `read src/mod.py:12`, `answer src/mod_matrix_0.py`. The agent EMITS these bytes; ToolCall.Parse is the "does it
// parse?" gate (tolerant — a generated call is imperfect; a fuzzy-but-recoverable call still acts, a garbage line is
// a no-op observation that itself teaches the shape). `answer` is the terminal verb: it ENDS the episode and names the
// LOC runtime's committed localization — the generative analogue of NavLoop's Stage-4 DECIDE, but SPOKEN by the runtime, not
// derived from a score field.
//
// Deterministic: sites arrive in file order, grep hits in (path, line) order, ls in sorted-path order, every rendering
// is a pure function of the sites. No RNG, no clock, no ambient state.
public static class Tool
{
    // ── THE ACTION SURFACE ── the verbs the agent may emit. PLURAL = the enum mark (conventions.md): the container of
    // the tool-verbs. Two worlds the LOC runtime acts on with ONE grammar: the EXTERIOR codebase (Grep/Open/Read/Ls/Answer,
    // over the instance's sites — AgentWorld) and the INTERIOR memory (Index/Recall, over the Cortex tape — the
    // hippocampus). Index BUILDS/dumps the map over the tape; Recall SEARCHES it (the runtime searches its accumulated
    // memory — prior instances' vested navigation). The persistent/compounding memory is this searchable INDEX, not
    // the raw sites: raw codebase bytes are re-searchable (grep) + forgettable, the MAP is what accumulates across the
    // stream. `Noop` is the unparseable-line sink (a generated line that named no verb — it still observes).
    public enum ToolVerbs { Grep, Open, Read, Ls, Answer, Index, Recall, Noop, Verify }

    // ── THE TOOL-CALL ── one parsed line of the agent's emission: the verb + its raw argument (a pattern, a path, a
    // path:line locus, or the answer file). `Raw` keeps the emitted bytes verbatim for the trace/journal. A record —
    // an immutable atom produced by Parse, consumed by AgentWorld.Act.
    public readonly record struct ToolCall(ToolVerbs Verb, string Arg, string Raw)
    {
        public static ToolCall Create(ToolVerbs verb, string argument)
        {
            string name = verb switch
            {
                ToolVerbs.Grep => "grep",
                ToolVerbs.Open => "open",
                ToolVerbs.Read => "read",
                ToolVerbs.Ls => "ls",
                ToolVerbs.Answer => "answer",
                ToolVerbs.Verify => "verify",
                ToolVerbs.Index => "index",
                ToolVerbs.Recall => "recall",
                _ => "noop",
            };
            argument = argument.Trim();
            return new ToolCall(verb, argument, argument.Length == 0 ? name : $"{name} {argument}");
        }

        /// Parse ONE emitted line into a call. Tolerant by design — the LOC runtime generates the tool-call grammar imperfectly
        /// early, so recovery beats rejection: leading junk before the verb is skipped, the verb match is
        /// case-insensitive + prefix-tolerant (`gr`→grep), and an unrecognized head yields Noop (which still appends an
        /// observation — the empty result is itself a signal that shapes the next emission). Never throws.
        public static ToolCall Parse(string line)
        {
            string raw = line;
            string s = line.Trim();
            if (s.Length == 0) return new ToolCall(ToolVerbs.Noop, "", raw);

            // Split off the leading token (the verb) from the remainder (the arg). The arg may itself contain spaces
            // (a grep pattern), so only the FIRST whitespace run splits.
            int sp = 0;
            while (sp < s.Length && !char.IsWhiteSpace(s[sp])) sp++;
            string head = s[..sp];
            string arg = sp < s.Length ? s[sp..].Trim() : "";

            var verb = MatchVerb(head);
            // A bare verb with no arg but a recoverable arg elsewhere on the line (the runtime sometimes emits `open:
            // path`) — strip a leading ':' or '=' the grammar may have minted around the separator.
            arg = arg.TrimStart(':', '=', ' ');
            return new ToolCall(verb, arg, raw);
        }

        private static ToolVerbs MatchVerb(string head)
        {
            string h = head.ToLowerInvariant().TrimStart('#', '>', '-', '*', ':');
            // Prefix-tolerant: the grammar may emit a truncated/extended verb (`grepp`, `op`). Longest anchors first so
            // `read` isn't shadowed. `find`/`search`/`cat`/`view` are the natural synonyms the agent drifts into — map
            // them to the semantic core rather than punishing the drift.
            // INTERIOR-memory verbs anchored FIRST (longest/most-specific): `recall`/`remember`/`memory` search the
            // Cortex tape, `index`/`map` build the map over it — kept ahead of the exterior verbs so `recall`
            // isn't shadowed by a prefix collision and the memory surface is reachable from the grammar walk.
            if (h.StartsWith("recall") || h.StartsWith("remember") || h.StartsWith("memory") || h.StartsWith("mem")) return ToolVerbs.Recall;
            if (h.StartsWith("index") || h.StartsWith("map") || h.StartsWith("hippo")) return ToolVerbs.Index;
            if (h.StartsWith("grep") || h.StartsWith("find") || h.StartsWith("search") || h.StartsWith("rg")) return ToolVerbs.Grep;
            if (h.StartsWith("answer") || h.StartsWith("final") || h.StartsWith("localize") || h.StartsWith("done")) return ToolVerbs.Answer;
            if (h.StartsWith("verify") || h.StartsWith("check") || h.StartsWith("assert")) return ToolVerbs.Verify;
            if (h.StartsWith("read") || h.StartsWith("cat") || h.StartsWith("view") || h.StartsWith("show")) return ToolVerbs.Read;
            if (h.StartsWith("open")) return ToolVerbs.Open;
            if (h.StartsWith("ls") || h.StartsWith("list") || h.StartsWith("dir") || h.StartsWith("tree")) return ToolVerbs.Ls;
            return ToolVerbs.Noop;
        }
    }

    // ── THE OBSERVATION ── the world's byte-reply to a call. `Text` is what appends to the Cortex tape (the corpus the
    // next induction re-reads); `HitPaths` are the files the call surfaced (for the journal + the reward's provenance —
    // which files the LOC runtime's own actions brought into view). `Answered`/`AnswerPath` fire only on the terminal verb.
    public readonly record struct RepositoryPath(string Value)
    {
        public int Length => Value.Length;
        public override string ToString() => Value;
        public static implicit operator RepositoryPath(string value) => new(value);
        public static implicit operator string(RepositoryPath path) => path.Value;
    }

    public readonly record struct RepositoryLocus(RepositoryPath Path, int Line);

    public readonly record struct Observation
    {
        public Observation(string text, IReadOnlyList<RepositoryPath> hitPaths, bool answered, RepositoryPath answerPath, RepositoryLocus? locus = null,
            IReadOnlyList<RepositoryLocus>? loci = null, RepositoryOccurrenceCheckResult? verification = null)
        {
            Text = text; HitPaths = hitPaths; Answered = answered; AnswerPath = answerPath; Locus = locus;
            Loci = loci ?? (locus is { } one ? new[] { one } : Array.Empty<RepositoryLocus>());
            OccurrenceCheck = verification;
        }

        public string Text { get; }
        public IReadOnlyList<RepositoryPath> HitPaths { get; }
        public bool Answered { get; }
        public RepositoryPath AnswerPath { get; }
        public RepositoryLocus? Locus { get; }
        public IReadOnlyList<RepositoryLocus> Loci { get; }
        public RepositoryOccurrenceCheckResult? OccurrenceCheck { get; }
        public static readonly Observation Empty = new("", Array.Empty<RepositoryPath>(), false, new RepositoryPath(""));
    }

    // ── FIXTURE WORLD ── one replay instance's codebase, reconstructed from its sites, exposing the deterministic tool surface.
    // Constructed once per instance; the LOC runtime acts against it across the episode. Files are the union of site texts by
    // path, in first-appearance order (the sites arrive pre-ordered, so this is stable). A CLASS (not a record) — it is
    // long-lived across an episode and its verbs entangle through the shared file index; you never reach past it to the
    // raw sites.
    public sealed class AgentWorld
    {
        private readonly List<SiteRow> _sites;
        private readonly List<string> _paths;                    // distinct paths, first-appearance order = deterministic
        private readonly Dictionary<string, List<int>> _byPath;  // path → site indices (source order)

        public AgentWorld(List<SiteRow> sites)
        {
            _sites = sites;
            _paths = new List<string>();
            _byPath = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < sites.Count; i++)
            {
                var p = sites[i].Path;
                if (!_byPath.TryGetValue(p, out var idx)) { _byPath[p] = idx = new List<int>(); _paths.Add(p); }
                idx.Add(i);
            }
        }

        public int FileCount => _paths.Count;
        public int SiteCount => _sites.Count;
        public IReadOnlyList<string> Paths => _paths;

        /// DOCUMENT FREQUENCY — how many world FILES contain `term` (case-insensitive substring, grep's own match): the
        /// idf denominator the ranking autopsy weights votes by. A file counts once if ANY of its sites contains the term
        /// (the distinct-path fan-out a full grep would surface — the byte-cap that truncates the OBSERVATION doesn't
        /// shrink the term's true document frequency). O(sites·|term|) per call, memoized per world (--explain-rank only,
        /// so the cost never touches the solve hot path). Common terms → high df → low idf; rare bridge-terms → df 1–2 → high idf.
        public int DocFreq(string term)
        {
            term = term.Trim();
            if (term.Length == 0) return 0;
            _docFreq ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (_docFreq.TryGetValue(term, out int cached)) return cached;
            int df = 0;
            foreach (var p in _paths)
            {
                bool hit = false;
                foreach (var si in _byPath[p])
                    if (_sites[si].Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                if (hit) df++;
            }
            _docFreq[term] = df;
            return df;
        }
        private Dictionary<string, int>? _docFreq;   // term → world-file count, memoized (the idf denominator; explain-rank only)

        /// Load a world from an instance directory (the LOC runtime contract — reuses the exact sites.jsonl format the
        /// benches read). Byte-identical parse to NavLoop.LoadSites; kept here so the tool-world is self-contained.
        public static AgentWorld Load(string instanceDir)
            => new(LoadSites(Path.Combine(instanceDir, "sites.jsonl")));

        /// EXECUTE a call against the world → the observation. The single dispatch the agent's emission funnels
        /// through: parse-tolerant in, deterministic bytes out. `MaxObsBytes` caps a runaway grep so one observation
        /// can't blow the tape (the runtime must localize, not slurp) — the cap is part of the world's discipline.
        public Observation Act(ToolCall call, int maxObsBytes = 4096)
            => call.Verb switch
            {
                ToolVerbs.Grep   => Grep(call.Arg, maxObsBytes),
                ToolVerbs.Open   => OpenFile(call.Arg, maxObsBytes),
                ToolVerbs.Read   => ReadLocus(call.Arg, maxObsBytes),
                ToolVerbs.Ls     => Ls(call.Arg),
                ToolVerbs.Answer => Answer(call.Arg),
                _                => new Observation($"[no-op: {Trunc(call.Raw, 60)}]\n", Array.Empty<RepositoryPath>(), false, ""),
            };

        // ── grep <pattern> ── every site line containing the pattern (case-insensitive substring — the runtime's patterns
        // are noisy), rendered `path:line: text`, in (path-order, line-order). The workhorse discovery verb: the runtime
        // greps a term from the query and the world tells it where that concept lives. Hit paths de-duplicated in order.
        private Observation Grep(string pattern, int cap)
        {
            pattern = pattern.Trim();
            if (pattern.Length == 0) return new Observation("[grep: empty pattern]\n", Array.Empty<RepositoryPath>(), false, "");
            var sb = new StringBuilder();
            var hits = new List<RepositoryPath>(); var loci = new List<RepositoryLocus>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            int n = 0;
            foreach (var p in _paths)
                foreach (var si in _byPath[p])
                {
                    var site = _sites[si];
                    if (site.Text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;   // fast-reject: pattern absent from the whole site → no line can match; skip the per-site split
                    var (lines, first) = (SplitKeep(site.Text), site.Start);
                    for (int li = 0; li < lines.Count; li++)
                    {
                        if (lines[li].IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        sb.Append(p).Append(':').Append(first + li).Append(": ").Append(lines[li].Trim()).Append('\n');
                        if (seen.Add(p)) hits.Add(p);
                        loci.Add(new RepositoryLocus(p, first + li));
                        n++;
                        if (sb.Length >= cap) { sb.Append("[…grep truncated]\n"); return new Observation(sb.ToString(), hits, false, "", null, loci); }
                    }
                }
            if (n == 0) sb.Append("[grep: no match for '").Append(Trunc(pattern, 40)).Append("']\n");
            return new Observation(sb.ToString(), hits, false, "", null, loci);
        }

        // ── open <path> ── the whole file: its sites concatenated in line order, each prefixed `line: text`. The
        // read-into-context verb: once grep points at a file, the runtime opens it to ingest its structure. Path match is
        // suffix-tolerant (the runtime may emit `mod_matrix_0.py` for `src/mod_matrix_0.py`).
        private Observation OpenFile(string pathArg, int cap)
        {
            string? path = ResolvePath(pathArg);
            if (path is null) return new Observation($"[open: no file matching '{Trunc(pathArg, 40)}']\n", Array.Empty<RepositoryPath>(), false, "");
            var sb = new StringBuilder();
            sb.Append("== ").Append(path).Append(" ==\n");
            foreach (var si in _byPath[path])
            {
                var site = _sites[si];
                var lines = SplitKeep(site.Text);
                for (int li = 0; li < lines.Count; li++)
                {
                    sb.Append(site.Start + li).Append(": ").Append(lines[li]).Append('\n');
                    if (sb.Length >= cap) { sb.Append("[…open truncated]\n"); return new Observation(sb.ToString(), new[] { new RepositoryPath(path) }, false, ""); }
                }
            }
            return new Observation(sb.ToString(), new[] { new RepositoryPath(path) }, false, "");
        }

        // ── read <path>:<line> ── a focused window around a locus (the site whose extent covers the line). The
        // precision verb: the runtime narrows from a file to the specific definition. `<path>` alone (no line) falls back
        // to open.
        private Observation ReadLocus(string arg, int cap)
        {
            int colon = arg.LastIndexOf(':');
            if (colon < 0 || !int.TryParse(arg[(colon + 1)..].Trim(), out int line))
                return OpenFile(arg, cap);
            string? path = ResolvePath(arg[..colon]);
            if (path is null) return new Observation($"[read: no file matching '{Trunc(arg, 40)}']\n", Array.Empty<RepositoryPath>(), false, "");
            foreach (var si in _byPath[path])
            {
                var site = _sites[si];
                if (line < site.Start || line > site.End) continue;
                var sb = new StringBuilder();
                sb.Append("== ").Append(path).Append(':').Append(site.Name).Append(" (").Append(site.Kind).Append(") ==\n");
                var lines = SplitKeep(site.Text);
                for (int li = 0; li < lines.Count; li++)
                    sb.Append(site.Start + li).Append(": ").Append(lines[li]).Append('\n');
                return new Observation(Cap(sb.ToString(), cap), new[] { new RepositoryPath(path) }, false, "", new RepositoryLocus(path, line));
            }
            return new Observation($"[read: no site covers {path}:{line}]\n", new[] { new RepositoryPath(path) }, false, "", new RepositoryLocus(path, line));
        }

        // ── ls [dir] ── the file tree (paths under the optional dir prefix, sorted). The orientation verb: the runtime
        // surveys what exists before it greps. No arg = the whole tree.
        private Observation Ls(string dir)
        {
            dir = dir.Trim().TrimEnd('/');
            var matched = (dir.Length == 0 ? _paths : _paths.Where(p => p.StartsWith(dir, StringComparison.Ordinal)))
                          .OrderBy(p => p, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            sb.Append("ls ").Append(dir.Length == 0 ? "." : dir).Append(" (").Append(matched.Count).Append(" files)\n");
            foreach (var p in matched) sb.Append(p).Append('\n');
            return new Observation(sb.ToString(), matched.Select(static path => new RepositoryPath(path)).ToArray(), false, "");
        }

        // ── answer <path> ── the terminal verb: the LOC runtime SPEAKS its committed localization. Ends the episode; the
        // bench scores AnswerPath against gold AFTER (forward flow — the answer never sees the gold). The generative
        // analogue of NavLoop's DECIDE, but emitted, not scored.
        private Observation Answer(string pathArg)
        {
            string? path = ResolvePath(pathArg) ?? pathArg.Trim();
            return new Observation($"[answer: {path}]\n", string.IsNullOrEmpty(path) ? Array.Empty<RepositoryPath>() : new[] { new RepositoryPath(path) }, true, path);
        }

        /// Resolve a GENERATED answer arg to a real path — the LOC loop's grade gate. The runtime's `answer` arg is
        /// grammar-noise-adjacent (`src/mod_coupling, traversal):`), so recover a real path from it: try the whole
        /// arg through ResolvePath (exact/suffix/contains), else scan the arg's whitespace/punctuation-split tokens
        /// for the first that resolves (a path buried in a noisy line). Empty when nothing resolves — the honest
        /// "the runtime named no real file" verdict. This is the generative analogue of the bench's answer-vs-gold, but
        /// tolerant of the raw walk's imperfection (a clean standing grammar emits cleaner and needs no token-scan).
        public string ResolveAnswer(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return "";
            if (ResolvePath(arg) is { } whole) return whole;
            foreach (var tok in arg.Split(new[] { ' ', '\t', ',', '(', ')', ':', '"', '\'', '`', '=' }, StringSplitOptions.RemoveEmptyEntries))
                if (tok.Length >= 3 && ResolvePath(tok) is { } p) return p;
            return "";
        }

        /// The whole text of a file (its sites concatenated in line order) — the outcome-witness's source. The vest
        /// pass appends this as the REAL corroboration/repair span; distinct from OpenFile's rendering (no `line:`
        /// prefixes — the witness is the gold's CODE, the idioms the audit vests navigation on, not a display).
        public string FileText(string path)
        {
            if (!_byPath.TryGetValue(path, out var idx))
            {
                if (ResolvePath(path) is not { } p) return "";
                idx = _byPath[p];
            }
            var sb = new StringBuilder();
            foreach (var si in idx) sb.Append(_sites[si].Text).Append('\n');
            return sb.ToString();
        }

        // ── path resolution ── exact, else unique suffix match (the runtime emits basenames). Ambiguous suffix → the
        // FIRST in source order (deterministic; the runtime can disambiguate by opening). Null = no match.
        private string? ResolvePath(string arg)
        {
            arg = arg.Trim().Trim('"', '\'', '`').TrimEnd('/');
            if (arg.Length == 0) return null;
            if (_byPath.ContainsKey(arg)) return arg;
            foreach (var p in _paths)
                if (p.EndsWith(arg, StringComparison.Ordinal) &&
                    (p.Length == arg.Length || p[p.Length - arg.Length - 1] == '/')) return p;
            // last resort: any path CONTAINING the arg (the runtime emitted a fragment)
            foreach (var p in _paths) if (p.IndexOf(arg, StringComparison.Ordinal) >= 0) return p;
            return null;
        }

        // Split a site's text into lines WITHOUT the trailing newline, preserving blank lines (the line-number math
        // needs every physical line). `\r\n` and `\n` both split.
        private static List<string> SplitKeep(string text)
        {
            var outp = new List<string>();
            int i = 0, start = 0;
            while (i < text.Length)
            {
                if (text[i] == '\n')
                {
                    int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
                    outp.Add(text[start..end]); start = i + 1;
                }
                i++;
            }
            if (start < text.Length) outp.Add(text[start..]);
            else if (start == text.Length && (text.Length == 0 || text[^1] == '\n')) { /* trailing newline: no phantom line */ }
            return outp;
        }

        private static string Cap(string s, int cap) => s.Length <= cap ? s : s[..cap] + "[…truncated]\n";
    }

    /// A real repository-backed tool world.  The source tree is read once at construction,
    /// retained as an indexed set of relative paths, lines, and digests, and never rescanned
    /// while actions execute.  The old AgentWorld remains the sites.jsonl replay adapter; this
    /// world is the crawler's native authority and never writes a sites file.
    public sealed class RepositoryWorldSnapshot
    {
        private sealed class IndexedFile
        {
            public readonly RepositoryFile Authority;
            public readonly byte[] OriginalBytes;
            public readonly string Text;
            public readonly string[] Lines;

            public IndexedFile(RepositoryFile authority, byte[] originalBytes, string text)
            {
                Authority = authority; OriginalBytes = originalBytes;
                Text = text;
                Lines = SplitLines(text);
            }
        }

        private readonly string _root;
        private readonly string[] _paths;
        private readonly IReadOnlyList<string> _pathView;
        private readonly Dictionary<string, IndexedFile> _files;
        private readonly Dictionary<string, RepositoryLocus[]> _postings;

        public string RootPath => _root;
        public string Glob { get; }
        public string WorldSHA256 { get; }
        public int FileCount => _paths.Length;
        public IReadOnlyList<string> Paths => _pathView;
        private readonly IReadOnlyList<RepositoryFile> _authorities;
        public IReadOnlyList<RepositoryFile> Files => _authorities;

        /// Capture the indexed bytes, not the path on disk. This is the only
        /// source-to-snapshot seam: once constructed, no file is reopened or
        /// re-encoded to produce the authority copy.
        public RepositoryWorldFileSnapshot CaptureFile(RepositoryPath path)
        {
            if (!TryNormalizeRepositoryPath(path.Value, out string normalized)
                || !_files.TryGetValue(normalized, out IndexedFile? file))
                throw new KeyNotFoundException($"repository world file is not indexed: '{path.Value}'");
            return new RepositoryWorldFileSnapshot(file.Authority.Path, file.OriginalBytes);
        }

        public IReadOnlyList<RepositoryWorldFileSnapshot> CaptureFiles()
            => Array.AsReadOnly(_paths.Select(path => CaptureFile(new RepositoryPath(path))).ToArray());

        public RepositoryWorldSnapshot(string root, string glob = "*.cs,*.csx,*.c,*.h,*.hpp,*.cpp,*.cc,*.py,*.js,*.jsx,*.ts,*.tsx,*.rs,*.go,*.java,*.scala,*.md,*.toml,*.ron,*.json")
        {
            _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(_root)) throw new DirectoryNotFoundException($"repository root does not exist: '{_root}'");
            Glob = glob;
            _files = new Dictionary<string, IndexedFile>(StringComparer.Ordinal);

            var selected = FileCorpus.GatherRepositoryFiles(_root, glob);
            foreach (string file in selected)
            {
                string relative = NormalizeRelativePath(Path.GetRelativePath(_root, file));
                byte[] bytes;
                string text;
                try
                {
                    bytes = File.ReadAllBytes(file);
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    throw new InvalidDataException($"repository source cannot be decoded: '{relative}'", ex);
                }
                string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                _files.Add(relative, new IndexedFile(new RepositoryFile(relative, bytes.LongLength, digest), bytes, text));
            }

            _paths = _files.Keys.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
            _pathView = Array.AsReadOnly(_paths);
            _authorities = Array.AsReadOnly(_paths.Select(path => _files[path].Authority).ToArray());
            _postings = BuildPostings(_paths, _files);
            WorldSHA256 = ComputeWorldSHA256(_paths, _files);
        }

        /// Execute a deterministic read-only tool call against the indexed source tree.
        /// Every response is bounded before it crosses into the tape.
        public Observation Act(ToolCall call, int maxObsBytes = 4096)
            => call.Verb switch
            {
                ToolVerbs.Grep => Grep(call.Arg, maxObsBytes),
                ToolVerbs.Open => Open(call.Arg, maxObsBytes),
                ToolVerbs.Read => Read(call.Arg, maxObsBytes),
                ToolVerbs.Ls => List(call.Arg, maxObsBytes),
                ToolVerbs.Answer => Answer(call.Arg),
                _ => new Observation($"[no-op: {Trunc(call.Raw, 60)}]\n", Array.Empty<RepositoryPath>(), false, "")
            };

        public string ResolveAnswer(string arg) => ResolvePath(arg) ?? "";

        public bool ContainsPath(string path)
            => TryNormalizeRepositoryPath(path, out string candidate) && _files.ContainsKey(candidate);

        public bool ContainsText(string path, string value)
            => TryNormalizeRepositoryPath(path, out string candidate) && _files.TryGetValue(candidate, out IndexedFile? file)
                && file.Text.Contains(value, StringComparison.Ordinal);

        public bool ContainsLine(string path, int line, string value)
            => TryNormalizeRepositoryPath(path, out string candidate) && _files.TryGetValue(candidate, out IndexedFile? file)
                && line >= 1 && line <= file.Lines.Length
                && file.Lines[line - 1].Contains(value, StringComparison.Ordinal);

        public string FileText(string path)
            => ResolvePath(path) is { } resolved && _files.TryGetValue(resolved, out IndexedFile? file) ? file.Text : "";

        private Observation Grep(string pattern, int cap)
        {
            pattern = pattern.Trim();
            if (pattern.Length == 0) return new Observation("[grep: empty pattern]\n", Array.Empty<RepositoryPath>(), false, "");
            var output = new StringBuilder();
            var hits = new List<RepositoryPath>();
            var loci = new List<RepositoryLocus>();
            string lower = pattern.ToLowerInvariant();
            string probe = lower.Length >= 3 ? lower[..3] : lower;
            if (!_postings.TryGetValue(probe, out RepositoryLocus[]? candidates))
                return new Observation($"[grep: no match for '{Trunc(pattern, 40)}']\n", Array.Empty<RepositoryPath>(), false, "");
            foreach (RepositoryLocus locus in candidates)
            {
                string path = locus.Path.Value;
                IndexedFile file = _files[path];
                string text = file.Lines[locus.Line - 1];
                if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!hits.Contains(path)) hits.Add(path);
                loci.Add(locus);
                output.Append(path).Append(':').Append(locus.Line).Append(": ").Append(text.Trim()).Append('\n');
                if (output.Length >= cap || hits.Count >= 64) return new Observation(CapUtf8(output.ToString(), cap), hits, false, "", null, loci);
            }
            if (output.Length == 0) output.Append("[grep: no match for '").Append(Trunc(pattern, 40)).Append("']\n");
            return new Observation(CapUtf8(output.ToString(), cap), hits, false, "", null, loci);
        }

        private Observation Open(string arg, int cap)
        {
            string? path = ResolvePath(arg);
            if (path is null)
            {
                IReadOnlyList<RepositoryPath> requested = TryNormalizeRepositoryPath(arg, out string candidate)
                    ? new[] { new RepositoryPath(candidate) } : Array.Empty<RepositoryPath>();
                return new Observation($"[open: no file matching '{Trunc(arg, 40)}']\n", requested, false, "");
            }
            IndexedFile file = _files[path];
            var output = new StringBuilder().Append("== ").Append(path).Append(" ==\n");
            for (int line = 0; line < file.Lines.Length; line++)
            {
                output.Append(line + 1).Append(": ").Append(file.Lines[line]).Append('\n');
                if (output.Length >= cap) break;
            }
            return new Observation(CapUtf8(output.ToString(), cap), new[] { new RepositoryPath(path) }, false, "");
        }

        private Observation Read(string arg, int cap)
        {
            int colon = arg.LastIndexOf(':');
            if (colon < 0 || !int.TryParse(arg[(colon + 1)..].Trim(), out int line)) return Open(arg, cap);
            string? path = ResolvePath(arg[..colon]);
            if (path is null)
            {
                IReadOnlyList<RepositoryPath> requested = TryNormalizeRepositoryPath(arg[..colon], out string candidate)
                    ? new[] { new RepositoryPath(candidate) } : Array.Empty<RepositoryPath>();
                return new Observation($"[read: no file matching '{Trunc(arg, 40)}']\n", requested, false, "");
            }
            IndexedFile file = _files[path];
            if (line < 1 || line > file.Lines.Length) return new Observation($"[read: no line {line} in {path}]\n", new[] { new RepositoryPath(path) }, false, "", new RepositoryLocus(path, line));
            int first = Math.Max(1, line - 3), last = Math.Min(file.Lines.Length, line + 3);
            var output = new StringBuilder().Append("== ").Append(path).Append(':').Append(line).Append(" ==\n");
            for (int current = first; current <= last; current++)
                output.Append(current).Append(": ").Append(file.Lines[current - 1]).Append('\n');
            return new Observation(CapUtf8(output.ToString(), cap), new[] { new RepositoryPath(path) }, false, "", new RepositoryLocus(path, line));
        }

        private Observation List(string arg, int cap)
        {
            string rawPrefix = arg.Trim().TrimEnd('/');
            string prefix = "";
            if (rawPrefix is "." or "./") rawPrefix = "";
            if (rawPrefix.Length > 0 && !TryNormalizeRepositoryPath(rawPrefix, out prefix))
                return new Observation("[ls: path must stay inside repository]\n", Array.Empty<RepositoryPath>(), false, "");
            var matched = new List<string>();
            foreach (string path in _paths)
                if (prefix.Length == 0 || path.StartsWith(prefix + "/", StringComparison.Ordinal) || path == prefix) matched.Add(path);
            var output = new StringBuilder().Append("ls ").Append(prefix.Length == 0 ? "." : prefix).Append(" (").Append(matched.Count).Append(" files)\n");
            var rendered = new List<RepositoryPath>();
            foreach (string path in matched)
            {
                output.Append(path).Append('\n');
                rendered.Add(path);
                if (output.Length >= cap) break;
            }
            return new Observation(CapUtf8(output.ToString(), cap), rendered, false, "");
        }

        private Observation Answer(string arg)
        {
            string path = ResolvePath(arg) ?? "";
            return new Observation($"[answer: {path}]\n", path.Length == 0 ? Array.Empty<RepositoryPath>() : new[] { new RepositoryPath(path) }, true, path);
        }

        private string? ResolvePath(string arg)
        {
            if (!TryNormalizeRepositoryPath(arg, out string candidate)) return null;
            if (_files.ContainsKey(candidate)) return candidate;
            foreach (string path in _paths)
                if (path.EndsWith(candidate, StringComparison.Ordinal) && (path.Length == candidate.Length || path[path.Length - candidate.Length - 1] == '/')) return path;
            foreach (string path in _paths)
                if (path.IndexOf(candidate, StringComparison.Ordinal) >= 0) return path;
            return null;
        }

        private static string ComputeWorldSHA256(string[] paths, Dictionary<string, IndexedFile> files)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> length = stackalloc byte[8];
            foreach (string path in paths)
            {
                byte[] relative = Encoding.UTF8.GetBytes(path);
                BinaryPrimitives.WriteInt64LittleEndian(length, relative.LongLength);
                hash.AppendData(length); hash.AppendData(relative);
                byte[] content = files[path].Authority.Bytes == 0 ? Array.Empty<byte>() : files[path].OriginalBytes;
                BinaryPrimitives.WriteInt64LittleEndian(length, content.LongLength);
                hash.AppendData(length); hash.AppendData(content);
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private static Dictionary<string, RepositoryLocus[]> BuildPostings(string[] paths, Dictionary<string, IndexedFile> files)
        {
            var postings = new Dictionary<string, List<RepositoryLocus>>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                string[] lines = files[path].Lines;
                for (int line = 0; line < lines.Length; line++)
                {
                    string lower = lines[line].ToLowerInvariant();
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    for (int width = 1; width <= 3; width++)
                        for (int offset = 0; offset + width <= lower.Length; offset++)
                            keys.Add(lower.Substring(offset, width));
                    foreach (string key in keys)
                    {
                        if (!postings.TryGetValue(key, out List<RepositoryLocus>? bucket)) postings.Add(key, bucket = new List<RepositoryLocus>());
                        bucket.Add(new RepositoryLocus(path, line + 1));
                    }
                }
            }
            return postings.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        }

        private static string[] SplitLines(string text)
        {
            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
        }

        private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

        private static bool TryNormalizeRepositoryPath(string raw, out string path)
        {
            path = "";
            string candidate = NormalizeRelativePath(raw.Trim().Trim('"', '\'', '`').TrimEnd('/'));
            if (candidate.Length == 0) return true;
            if (Path.IsPathRooted(candidate)) return false;
            string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(static part => part is "." or "..")) return false;
            path = string.Join('/', parts);
            return path.Length > 0;
        }
    }

    public readonly record struct RepositoryFile(RepositoryPath Path, long Bytes, string SHA256);

    /// A byte-exact copy of one file admitted by the native repository world.
    /// The world owns the original indexed bytes; this value owns a private copy
    /// so consumers can seal a snapshot without reopening or decoding the path.
    public sealed class RepositoryWorldFileSnapshot
    {
        private readonly byte[] _bytes;

        internal RepositoryWorldFileSnapshot(RepositoryPath path, ReadOnlySpan<byte> bytes)
        {
            if (string.IsNullOrWhiteSpace(path.Value))
                throw new InvalidDataException("repository world snapshot file path is empty");
            Path = path;
            _bytes = bytes.ToArray();
            SHA256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_bytes));
        }

        public RepositoryPath Path { get; }
        public long Bytes => _bytes.LongLength;
        public string SHA256 { get; }
        public ReadOnlyMemory<byte> Content => _bytes;

        /// Return another copy when a mutable buffer is required by a caller.
        public byte[] CopyBytes() => _bytes.ToArray();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Path.Value)
                || SHA256 != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_bytes)))
                throw new InvalidDataException("repository world snapshot file bytes diverge from authority");
        }
    }

    // ── THE MEMORY WORLD (THE HIPPOCAMPUS) ── the INTERIOR the LOC runtime acts on, the twin of AgentWorld's EXTERIOR.
    // Where AgentWorld is the codebase's sites, THIS is the Cortex tape — accumulated experience across the whole
    // instance stream (every query it saw, every navigation it made, every gold it learned). The runtime SEARCHES this
    // the same way it searches the codebase: emit `recall <term>`, get the tape spans that match. The persistent,
    // compounding memory is this SEARCHABLE INDEX — not the raw sites (which are re-searchable via grep + forgettable).
    // A node in a prior instance grepped X → found gold Y; that navigation is a vested tape span, and `recall`
    // surfaces it when the current query rhymes — the mechanism by which experience in codebase N transfers to N+1.
    //
    // TWO INDEX SHAPES over the tape (both the substrate's own, byte-for-byte the consolidation machinery): the
    // SimhashIndex (ASSOCIATIVE — bucket-key → member TapeEventIDs, near-match by shingle affinity; the "what have I seen
    // like this" query) and GramPostings (CONTAINMENT — exact-substring gram → sites; the "where does this literal
    // token live in my memory" query). Recall runs BOTH and merges: exact-substring hits first (a precise recall),
    // then affinity neighbours (an associative recall). A CLASS — long-lived per solve call, rebuilt when the tape
    // grows (the INDEX verb is the rebuild+dump; Recall reads the standing index). Deterministic: the indices are
    // pure functions of the tape's spans in id order.
    public sealed class MemoryWorld
    {
        private readonly Tape _tape;
        private SimhashIndex _sim;                       // associative — near-match by shingle affinity
        private GramPostings _grams;                     // containment — exact-substring recall
        private long _indexedThrough;                    // the tape's NextId high-water the standing index covers — a Δ append is `id >= this` (NextId is one-PAST the last id, so the newest span is `id == this`, not `> this`)
        private int _indexedEvac;                        // ShedCount+DroppedCount at the last index — the evacuation EPOCH: it only rises (both counters are monotonic), and a rise means residents LEFT, forcing a full rebuild (the standing index is append-only, so it cannot un-index an evacuated span in place)
        private const int GramLen = 8;                   // the containment gram width — ≥ the vest floor, so a recalled idiom is a real shared rule's worth

        public MemoryWorld(Tape tape) { _tape = tape; _sim = new SimhashIndex(); _grams = new GramPostings(GramLen, cap: 16); _indexedThrough = 0; _indexedEvac = 0; Reindex(); }

        public int IndexedEvents => _sim.Count;
        public int GramKeys => (int)_grams.Mass().Keys;

        /// EXECUTE a memory call — the INTERIOR dispatch over the Cortex tape. Index rebuilds+dumps the map; Recall
        /// searches it. The LOC tool adapters share AgentWorld.Act's Observation contract and route both worlds uniformly.
        public Observation Act(ToolCall call, int maxObsBytes = 4096)
            => call.Verb switch
            {
                ToolVerbs.Index  => Index(),
                ToolVerbs.Recall => Recall(call.Arg, maxObsBytes),
                _                => new Observation($"[memory: not an interior verb: {Trunc(call.Raw, 60)}]\n", Array.Empty<RepositoryPath>(), false, ""),
            };

        // ── index ── (re)build the map over the Cortex tape and dump its shape: how many spans indexed, how crowded
        // the associative buckets are (the load L), how many containment grams. The runtime's "survey memory"
        // move — the interior analogue of `ls`. Cheap when the tape hasn't grown (Reindex is a no-op then).
        public Observation Index()
        {
            Reindex();
            var (keys, posts) = _grams.Mass();
            var sb = new StringBuilder();
            sb.Append("== memory index ==\n");
            sb.Append("spans ").Append(_sim.Count).Append(" · assoc-buckets ").Append(_sim.BucketCount)
              .Append(" (load ").Append(_sim.MeanOccupancy.ToString("F2")).Append(") · gram-keys ").Append(keys)
              .Append(" · postings ").Append(posts).Append('\n');
            return new Observation(sb.ToString(), Array.Empty<RepositoryPath>(), false, "");
        }

        // ── recall <term> ── SEARCH the Cortex tape: exact-substring containment hits first (precise), then
        // affinity neighbours (associative). Each hit renders its span's source + a snippet + the paths it names, so
        // a recalled navigation ("grep X → src/foo.py") re-surfaces the path that led to gold. Hit paths flow into the
        // Observation's HitPaths so the outer loop's reach-gold read + the answer resolver see recalled paths too —
        // memory recall can DIRECTLY surface the gold path from a prior visit (the compounding shortcut).
        public Observation Recall(string term, int cap)
        {
            Reindex();
            term = term.Trim();
            if (term.Length == 0) return new Observation("[recall: empty query]\n", Array.Empty<RepositoryPath>(), false, "");
            var sb = new StringBuilder();
            sb.Append("== recall '").Append(Trunc(term, 40)).Append("' ==\n");
            var hitPaths = new List<RepositoryPath>(); var seenPath = new HashSet<string>(StringComparer.Ordinal);
            var seenEvent = new HashSet<long>();
            int shown = 0;

            // (1) CONTAINMENT — a recall query is not one literal string; it is a bag of DISTINCTIVE grams. Probe
            // EVERY GramLen-window of the query against the postings and gather the spans that contain ANY of them,
            // ranked by how many of the query's grams they carry (the most-overlapping memory first). Each candidate
            // is byte-verified to actually hold that gram (postings narrow, bytes decide — guard the FNV bucket). This
            // fires for a follow-up query that re-uses codebase identifiers even when it isn't a verbatim substring of
            // the past navigation (`def handle_cursor_residual cache lookup` shares the `handle_cursor_residual` grams
            // with the stored `grep handle_cursor_residual` nav).
            var termBytes = Encoding.UTF8.GetBytes(term);
            if (termBytes.Length >= GramLen)
            {
                var gramHits = new Dictionary<long, int>();
                for (int off = 0; off + GramLen <= termBytes.Length; off++)
                {
                    var gram = termBytes.AsSpan(off, GramLen);
                    var posts = _grams.Posts(Simhash.Fnv64(gram));
                    if (posts is null) continue;
                    foreach (var post in posts)
                        if (_tape.Resolve(new TapeEventID(post.Id), out var span) && span.AsSpan().IndexOf(gram) >= 0)
                            gramHits[post.Id] = gramHits.GetValueOrDefault(post.Id) + 1;
                }
                foreach (var (id, _) in gramHits.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
                {
                    if (!seenEvent.Add(id)) continue;
                    if (!_tape.Resolve(new TapeEventID(id), out var span)) continue;
                    RenderHit(sb, id, span, hitPaths, seenPath, "gram");
                    if (++shown >= 8 || sb.Length >= cap) return Done(sb, hitPaths);
                }
            }

            // (2) ASSOCIATIVE — near-match by shingle affinity: sign the query, query the index, walk the Hamming
            // ball. The "what have I seen LIKE this" recall — surfaces navigations whose surrounding context rhymes
            // with the current query even without a literal substring hit.
            var qSig = Simhash.OfBytes(termBytes);
            var wit = _sim.Query(qSig, maxHamming: 24, topK: 12, bandFlip: true);
            foreach (var hit in wit.Hits)
            {
                if (!seenEvent.Add(hit.Id.Value)) continue;
                if (!_tape.Resolve(hit.Id, out var span)) continue;
                RenderHit(sb, hit.Id.Value, span, hitPaths, seenPath, $"~{hit.Hamming}");
                if (++shown >= 12 || sb.Length >= cap) break;
            }
            if (shown == 0) sb.Append("[recall: nothing in memory matches]\n");
            return Done(sb, hitPaths);
        }

        private Observation Done(StringBuilder sb, List<RepositoryPath> hitPaths)
            => new(sb.ToString(), hitPaths, false, "");

        // Render one recalled span: its provenance source, the match kind, a one-line snippet, and the file paths its
        // text names (so a recalled navigation re-surfaces the path it found — the compounding shortcut).
        private void RenderHit(StringBuilder sb, long id, byte[] span, List<RepositoryPath> hitPaths, HashSet<string> seenPath, string kind)
        {
            string text = Encoding.UTF8.GetString(span);
            sb.Append('s').Append(id).Append(" [").Append(_tape.SourceOf(new TapeEventID(id))).Append(' ').Append(kind).Append("] ")
              .Append(Trunc(OneLineOf(text), 100)).Append('\n');
            foreach (var p in PathsIn(text)) if (seenPath.Add(p)) hitPaths.Add(p);
        }

        // Extract file-path-looking tokens from a recalled span's text (the navigations carry `path:line:` and `==
        // path ==` shapes) — a path is a slash-bearing token ending in a source-file extension. These become the
        // recall's HitPaths: a recalled navigation surfaces the path it led to.
        private static IEnumerable<string> PathsIn(string text)
        {
            foreach (var tok in text.Split(' ', '\n', '\t', ':', '=', '(', ')', ',', '"', '\'', '`'))
            {
                var t = tok.Trim();
                if (t.Length >= 5 && t.Contains('/') &&
                    (t.EndsWith(".py") || t.EndsWith(".cs") || t.EndsWith(".js") || t.EndsWith(".ts") || t.EndsWith(".rs") || t.EndsWith(".go") || t.EndsWith(".java")))
                    yield return t;
            }
        }

        private static string OneLineOf(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s[..nl];
        }

        /// Bring the standing index up to the tape — O(Δ·shingles) on the hot per-recall path, NOT the O(tape·shingles)
        /// from-scratch rebuild this used to be (the tape grows every step and the LOC runtime recalls every step, so a full
        /// re-feed per recall was an O(tape) chug hiding behind the recall cadence — gating WHEN it fires never made it
        /// not-O(tape)). Two cases, and the result is BYTE-IDENTICAL to a from-scratch rebuild over the current
        /// residents in either:
        ///   GROWTH (the common step) — APPEND only the Δ-new spans (id ≥ the watermark) into the standing indices. The
        ///     LOC runtime does not reorder the tape between evacuation epochs, so the
        ///     residents are always append-order = id-ascending: the Δ-new spans are exactly the tail, and appending
        ///     them reproduces the exact feed order — hence the exact slot assignment + gram-cap prefix — a from-scratch
        ///     feed would produce (SimhashIndex/GramPostings are id-order-fed by contract; SimHash.cs).
        ///   EVACUATION (the rare night) — a shed/drop RETIRES residents, but the standing indices are APPEND-ONLY
        ///     (dense slots + append-stable adjacency — they cannot un-index a span in place), and stale postings can't
        ///     just be filtered at read time: an evacuated posting still occupies a gram-cap / topK slot AHEAD of a live
        ///     one, crowding a resident hit out before residency can be checked. So on any evacuation-epoch rise we
        ///     REBUILD over the current residents (the honest residents-only map). This is O(residents), paid at the
        ///     sleep cadence over the BOUNDED resident set (shedding holds it near ShedKeepRecent) — not the tape.
        /// A no-op when neither the id high-water nor the evacuation epoch moved.
        public void Reindex()
        {
            int evac = _tape.ShedCount + _tape.DroppedCount;
            if (evac != _indexedEvac) { Rebuild(); return; }           // residents left → the append-only index must be rebuilt over the survivors
            if (_tape.NextId == _indexedThrough && _sim.Count > 0) return;

            // GROWTH — feed the Δ-new id range directly. _indexedThrough is a NextId high-water (one PAST the last
            // indexed id), so the first new event is `id == _indexedThrough`. With no evacuation-epoch change,
            // every id in this range is resident; resolving the range avoids walking the entire reordered resident
            // view on every recall. The id feed is the same canonical order used by the checkpoint rebuild, so the
            // standing Simhash/gram slots remain byte-identical to a from-scratch index.
            for (long value = _indexedThrough; value < _tape.NextId; value++)
            {
                var id = new TapeEventID(value);
                if (!_tape.Resolve(id, out var bytes))
                    throw new InvalidOperationException($"MemoryWorld.Reindex: new event {id} is unresolvable");
                int pos = _tape.PositionOf(id);
                if (pos >= 0) FeedEvent(id, bytes, _tape.ResidentEventSources[pos]);
            }
            _indexedThrough = _tape.NextId;
        }

        /// Full residents-only rebuild — the evacuation path (a shed/drop changed WHICH spans are resident). Fresh
        /// indices fed the whole current view in id-ascending order (the from-scratch contract the growth path stays
        /// byte-identical to). Also the constructor's initial fill.
        private void Rebuild()
        {
            _sim = new SimhashIndex();
            _grams = new GramPostings(GramLen, cap: 16);
            var ids = _tape.ResidentEventIDs; var spans = _tape.ResidentEventBytes; var src = _tape.ResidentEventSources;
            for (int k = 0; k < spans.Count; k++) FeedEvent(ids[k], spans[k], src[k]);
            _indexedThrough = _tape.NextId;
            _indexedEvac = _tape.ShedCount + _tape.DroppedCount;
        }

        /// Post one span into BOTH indices — the single feed site both Reindex cases share, so the associative +
        /// containment maps can never drift on how a span is ingested.
        private void FeedEvent(TapeEventID id, byte[] span, string source)
        {
            _sim.Add(id, span, source);
            _grams.Add(id.Value, span);
        }

        // ── THE DETERMINISM GATE ──  the incremental Reindex is a search ACCELERATOR: it may change HOW FAST recall
        // runs, NEVER WHAT it recalls. This proves that byte-for-byte against the residents-only from-scratch index it
        // replaced, over the two states that stress it: GROWTH (append-only) and EVACUATION (a drop retires residents,
        // the case a naive leave-stale-postings incremental would silently diverge on — an evacuated posting occupies a
        // gram-cap / topK slot ahead of a live one and crowds a resident hit out). At each state we build a FRESH
        // MemoryWorld over the identical resident spans (its ctor Rebuilds = pure residents-only, the oracle) and diff
        // its Recall text against the long-lived incremental one that saw the spans arrive with interleaved Reindex.
        public static int VerifyIncremental()
        {
            Console.WriteLine("── verify-incremental · the memory-index is byte-identical incremental vs from-scratch (determinism gate) ──");
            int fails = 0;
            void Check(bool ok, string name, string detail) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "✓" : "✗ FAIL")}  {name,-30} {detail}"); }

            // Distinctive span-text generator — a shared gram `resonant_cursor_delta` many spans carry (so the gram-cap
            // truncation is EXERCISED: >16 carriers force the cap), plus a per-id salt so signatures spread across buckets.
            static byte[] BuildEventBytes(long i) => Encoding.UTF8.GetBytes($"grep resonant_cursor_delta\nsrc/mod_{i:D4}.py:{i}: def resonant_cursor_delta(self, cursor_{i}, cache): return handle_{i % 7}\nanswer src/mod_{i:D4}.py");
            string[] queries = { "resonant_cursor_delta", "def resonant_cursor_delta cache handle", "src/mod_0007.py", "cursor cache lookup residual", "handle_3" };

            // The ORACLE — a fresh residents-only MemoryWorld over an explicit id-ascending resident set (ctor Rebuilds).
            static string Oracle(List<(long Id, byte[] Bytes)> residents, string[] qs)
            {
                var t = new Tape(); t.MountLog(new MemoryStream());
                // Append is what mints ids; to reproduce a resident set with GAPS (post-drop) we append the survivors in
                // id order. The absolute id values differ from the live tape, but recall output is a pure function of the
                // resident (id-ascending, bytes) sequence + query — the SHAPE the incremental index must match.
                foreach (var (_, b) in residents) t.Append(b, "node0", Provenances.Replay);
                var m = new MemoryWorld(t);
                var sb = new StringBuilder();
                foreach (var q in qs) { sb.Append("Q<").Append(q).Append(">\n"); sb.Append(m.Recall(q, 4096).Text); }
                return sb.ToString();
            }
            // The INCREMENTAL subject reproduced on its OWN fresh tape so the oracle's id values align 1:1 (both start at
            // id 0, same append sequence) — the diff is then a pure incremental-vs-rebuilt comparison at equal ids.
            static string IncrementalReplay(List<byte[]> appendLog, string[] qs, params int[] recallAfter)
            {
                var t = new Tape(); t.MountLog(new MemoryStream());
                var m = new MemoryWorld(t);
                var sb = new StringBuilder();
                int nextCheck = 0;
                for (int k = 0; k < appendLog.Count; k++)
                {
                    t.Append(appendLog[k], "node0", Provenances.Replay);
                    if (nextCheck < recallAfter.Length && k == recallAfter[nextCheck]) { foreach (var q in qs) { sb.Append("Q<").Append(q).Append(">\n"); sb.Append(m.Recall(q, 4096).Text); } nextCheck++; }
                }
                return sb.ToString();
            }

            // (1) GROWTH — append 40 spans, recalling at three growth points. Incremental (one long-lived index, fed the
            // Δ-tail each recall) must equal the oracle rebuilt over the same prefix at each point.
            {
                var log = new List<byte[]>(); for (long i = 0; i < 40; i++) log.Add(BuildEventBytes(i));
                int[] pts = { 9, 24, 39 };
                string incr = IncrementalReplay(log, queries, pts);
                var sb = new StringBuilder();
                foreach (int p in pts)
                {
                    var res = new List<(long, byte[])>(); for (long i = 0; i <= p; i++) res.Add((i, BuildEventBytes(i)));
                    foreach (var q in queries) { sb.Append("Q<").Append(q).Append(">\n"); sb.Append(Oracle(res, new[] { q })); }
                }
                // Oracle() re-emits the Q< header itself; strip the doubled header to compare bodies. Simpler: rebuild the
                // oracle string in the SAME framing as incr by calling Oracle per-point over the prefix.
                var oracleSb = new StringBuilder();
                foreach (int p in pts) { var res = new List<(long, byte[])>(); for (long i = 0; i <= p; i++) res.Add((i, BuildEventBytes(i))); oracleSb.Append(Oracle(res, queries)); }
                Check(incr == oracleSb.ToString(), "growth-byte-identical", $"40-span append, 3 recall points × {queries.Length} queries — incremental ≡ from-scratch ({incr.Length}B)");
            }

            // (2) EVACUATION — the crowd-out case. Append 40 spans (>16 share the capped gram, and >12 are near in
            // signature), recall, then DROP the earliest 20 (unvested dreams), recall again. The incremental index keeps
            // stale postings from the 20 dropped spans UNLESS Reindex rebuilds on the evac-epoch rise; the oracle is the
            // residents-only rebuild over the surviving 20. They must match byte-for-byte — proving the rebuild-on-evac
            // path (not a doomed read-time filter) recovers the exact residents-only result.
            {
                var live = new Tape(); live.MountLog(new MemoryStream());
                var incrMem = new MemoryWorld(live);
                var ids = new List<TapeEventID>();
                for (long i = 0; i < 40; i++) ids.Add(live.Append(BuildEventBytes(i), "node0", Provenances.Replay));
                var beforeSb = new StringBuilder(); foreach (var q in queries) { beforeSb.Append("Q<").Append(q).Append(">\n"); beforeSb.Append(incrMem.Recall(q, 4096).Text); }

                var drop = new List<TapeEventID>(); for (int i = 0; i < 20; i++) drop.Add(ids[i]); drop.Sort((a, b) => a.Value.CompareTo(b.Value));
                live.Evacuate(Array.Empty<TapeEventID>(), drop);
                var afterSb = new StringBuilder(); foreach (var q in queries) { afterSb.Append("Q<").Append(q).Append(">\n"); afterSb.Append(incrMem.Recall(q, 4096).Text); }

                // Oracle over survivors (ids 20..39), rebuilt from scratch. Recall output is a pure function of the
                // (id-ascending resident, bytes) set — but the SURVIVOR IDS are 20..39 on the live tape while a fresh
                // append-oracle would id them 0..19. Recall renders the id (`sN`), so to compare we must give the oracle
                // the SAME id values: append 20 filler dreams then drop them, leaving survivors at 20..39. Cleaner: build
                // the oracle by the identical live sequence (append 40, drop first 20) — a second independent tape.
                var oracleTape = new Tape(); oracleTape.MountLog(new MemoryStream());
                var oIds = new List<TapeEventID>(); for (long i = 0; i < 40; i++) oIds.Add(oracleTape.Append(BuildEventBytes(i), "node0", Provenances.Replay));
                var oDrop = new List<TapeEventID>(); for (int i = 0; i < 20; i++) oDrop.Add(oIds[i]); oDrop.Sort((a, b) => a.Value.CompareTo(b.Value));
                oracleTape.Evacuate(Array.Empty<TapeEventID>(), oDrop);
                var oracleMem = new MemoryWorld(oracleTape);   // FRESH ctor over the post-drop residents = residents-only from-scratch
                var oracleSb = new StringBuilder(); foreach (var q in queries) { oracleSb.Append("Q<").Append(q).Append(">\n"); oracleSb.Append(oracleMem.Recall(q, 4096).Text); }

                Check(afterSb.ToString() == oracleSb.ToString(), "evacuation-byte-identical", $"drop 20/40 then recall — incremental (rebuild-on-evac) ≡ residents-only from-scratch ({afterSb.Length}B)");
                Check(beforeSb.ToString() != afterSb.ToString(), "evacuation-actually-changed", "the drop DID change recall (the test exercises a real state transition, not a no-op)");
                Check(incrMem.IndexedEvents == 20, "evacuation-shrank-index", $"the standing index rebuilt to the 20 survivors (was 40) — stale postings gone, not filtered (idx={incrMem.IndexedEvents}sp)");
            }

            Console.WriteLine(fails == 0
                ? "✓ THE INDEX IS A PURE ACCELERATOR — incremental Reindex is byte-identical to from-scratch across growth AND evacuation. Determinism HOLDS; only the cost changed (O(tape)/recall → O(Δ)/recall)."
                : $"✗ {fails} failure(s) — the incremental index DIVERGED from from-scratch; determinism is broken and the fix is not byte-safe.");
            return fails == 0 ? 0 : 1;
        }

        // ── THE SPEEDUP RECEIPT ──  the whole point: per-recall wall must go O(tape)→O(Δ). Grow a tape to `n` spans,
        // recalling every step (the solve cadence — grow one, recall once), and time the recalls in an early window vs a
        // late window. From-scratch was O(tape): late recalls (big tape) cost strictly more than early. Incremental is
        // O(Δ=1 span/step): the per-recall cost is FLAT regardless of tape size — the readout is late/early ≈ 1, not ≈ n.
        public static int TimeIncremental(int n = 4000)
        {
            Console.WriteLine($"── time-incremental · per-recall wall on a growing tape (n={n}) — the O(tape)→O(Δ) readout ──");
            var tape = new Tape(); tape.MountLog(new MemoryStream());
            var mem = new MemoryWorld(tape);
            static byte[] BuildEventBytes(long i) => Encoding.UTF8.GetBytes($"grep resonant_cursor_delta\nsrc/mod_{i:D5}.py:{i}: def resonant_cursor_delta(self, cursor_{i}, cache): return handle_{i % 7}\nanswer src/mod_{i:D5}.py");
            var sw = new System.Diagnostics.Stopwatch();
            double earlyMs = 0, lateMs = 0; int earlyN = 0, lateN = 0;
            int loWin = n / 10, hiWin = n - n / 10;
            for (long i = 0; i < n; i++)
            {
                tape.Append(BuildEventBytes(i), "node0", Provenances.Replay);
                sw.Restart();
                var obs = mem.Recall("resonant_cursor_delta cache", 4096);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                if (i < loWin) { earlyMs += ms; earlyN++; }
                else if (i >= hiWin) { lateMs += ms; lateN++; }
                if (obs.Text.Length == 0) { Console.WriteLine("  ✗ recall returned empty — abort"); return 1; }
            }
            double eAvg = earlyMs / Math.Max(1, earlyN), lAvg = lateMs / Math.Max(1, lateN);
            double ratio = lAvg / Math.Max(1e-9, eAvg);
            Console.WriteLine($"  early (tape≈{loWin}sp):  {eAvg:F4} ms/recall  (n={earlyN})");
            Console.WriteLine($"  late  (tape≈{hiWin}sp):  {lAvg:F4} ms/recall  (n={lateN})");
            Console.WriteLine($"  late/early ratio: {ratio:F2}×  —  O(Δ) predicts ≈1 (FLAT); the old O(tape) rebuild predicted ≈{(double)hiWin / Math.Max(1, loWin):F0}× (linear in tape)");
            Console.WriteLine(ratio < 3.0
                ? $"  ✓ FLAT — per-recall cost is independent of tape size (the O(Δ) win; a from-scratch rebuild would be ~{(double)hiWin / Math.Max(1, loWin):F0}× slower late)"
                : $"  ⚠ per-recall cost still climbs {ratio:F1}× with tape size — the O(tape) work is not fully gone");
            return 0;
        }
    }

    // ── THE SITE ROW ── the tool-world's own view of a site (the JSON contract). Distinct from NavLoop's private `Site`
    // record so Tool.cs is dependency-free of the bench internals; the fields ARE the sites.jsonl schema. A record —
    // an immutable row parsed from one JSONL line.
    public readonly record struct SiteRow(string Path, string Kind, string Name, int Start, int End, string Text);

    /// Load sites.jsonl → the world's rows. Byte-identical to the bench loaders (the FileCorpus contract); kept local so
    /// the tool-world stands alone. One JSON object per non-empty line.
    public static List<SiteRow> LoadSites(string path)
    {
        var list = new List<SiteRow>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            list.Add(new SiteRow(
                r.GetProperty("path").GetString()!, r.GetProperty("kind").GetString()!,
                r.GetProperty("name").GetString()!, r.GetProperty("start_line").GetInt32(),
                r.GetProperty("end_line").GetInt32(), r.GetProperty("text").GetString()!));
        }
        return list;
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static string CapUtf8(string s, int maxBytes)
    {
        if (maxBytes <= 0) return "";
        if (Encoding.UTF8.GetByteCount(s) <= maxBytes) return s;
        const string suffix = "[…truncated]\n";
        int take = Math.Min(s.Length, maxBytes);
        while (take > 0 && Encoding.UTF8.GetByteCount(s.AsSpan(0, take)) + Encoding.UTF8.GetByteCount(suffix) > maxBytes) take--;
        return s[..take] + suffix;
    }
}
