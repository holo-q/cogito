namespace Cogito;

using System.Globalization;
using System.Text;
using System.Collections.Concurrent;

// Experiment persistence — every run lands in runs/<lineage>_<NNNN>/ (at the project root), auto-incrementing
// per lineage. This phase studies DYNAMICS, not outputs: the probe-curves ARE the object of analysis (can I
// crash the line and recover it? what does that do to the final brain?). So we keep all of it — the config,
// the curve.tsv, its plots.png, the generated samples, the grammar — so arcs are reviewable and meta-analyzable later.
public sealed class Run
{
    private static readonly ConcurrentDictionary<string, object> CheckpointLocks = new(StringComparer.OrdinalIgnoreCase);
    public string Dir { get; }
    private Run(string dir) => Dir = dir;

    /// Open the next run dir for a lineage (runs/<lineage>_NNNN). NNNN = max existing + 1, so a lineage
    /// accretes its arc across runs.
    public static Run New(string lineage)
    {
        var root = Path.Combine(ProjectRoot(), "runs");
        Directory.CreateDirectory(root);
        int next = 0;
        foreach (var d in Directory.GetDirectories(root, $"{lineage}_*"))
        {
            var tail = Path.GetFileName(d)[(lineage.Length + 1)..];
            if (int.TryParse(tail, out var k) && k >= next) next = k + 1;
        }
        var name = $"{lineage}_{next:D4}";
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        Trace.Note($"run → runs/{name}/");   // where the arc landed — a locator, not report payload → the elevator
        return new Run(dir);
    }

    /// Resolve a run-dir reference to its existing directory: as given → project-root-relative → the
    /// `trunk_0078` shorthand under runs/ (cwd and project root). Null when nothing matches — the ONE authority
    /// every resume-shaped verb resolves through, so `resume trunk_0078` works from anywhere the binary runs.
    public static string? Resolve(string dir)
    {
        foreach (var cand in new[] { dir, Path.Combine(ProjectRoot(), dir), Path.Combine("runs", dir), Path.Combine(ProjectRoot(), "runs", dir) })
            if (Directory.Exists(cand)) return Path.GetFullPath(cand);
        return null;
    }

    /// The run ID (directory basename) of a run dir. `GetFileName(GetFullPath(dir))` alone returns EMPTY when
    /// `dir` carries a trailing separator — GetFullPath preserves it on Linux — so a trailing-slash run-dir arg
    /// silently produced an empty run ID that then failed every basename-identity check. The trim is the fix; every
    /// run-ID-from-directory derivation routes here so the trailing-slash class can never recur.
    public static string RunIDFromDirectory(string directory)
        => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));

    /// Reopen an EXISTING (already-Resolved) run dir — the resume path (no new lineage slot).
    public static Run Open(string dir)
    {
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"run dir not found: {dir}");
        var full = Path.GetFullPath(dir);
        Trace.Note($"run ⇄ {Path.GetFileName(full)}/ (resumed)");
        return new Run(full);
    }

    /// Open a NEW run dir with an EXACT name (no lineage auto-numbering) — the migration fork's home. Bare names
    /// land under runs/; an existing dir refuses loudly (a fork must never clobber an arc).
    public static Run Create(string name)
    {
        var dir = Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar)
            ? Path.GetFullPath(name)
            : Path.Combine(ProjectRoot(), "runs", name);
        if (Directory.Exists(dir) || File.Exists(dir)) throw new IOException($"run destination already exists: {dir} — refusing to clobber");
        Directory.CreateDirectory(dir);
        Trace.Note($"run + runs/{Path.GetFileName(dir)}/ (created)");
        return new Run(dir);
    }

    /// Create a uniquely named child arc owned by this run.  Child roles are
    /// directory-name atoms, never paths: traversal, rooted names, separators,
    /// and platform-invalid characters are rejected before any directory is
    /// created.  The parent remains the sole owner of the child namespace.
    public Run CreateChildRun(string role)
    {
        string safeRole = ValidateChildRole(role);
        string children = Path.Combine(Dir, "children");
        Directory.CreateDirectory(children);
        string childDirectory = Path.Combine(children, NextChildRunID(safeRole));
        return Create(childDirectory);
    }

    public string NextChildRunID(string role)
    {
        string safeRole = ValidateChildRole(role);
        string children = Path.Combine(Dir, "children");
        int next = 0;
        if (Directory.Exists(children))
            foreach (string child in Directory.GetDirectories(children, safeRole + "_*"))
            {
                string tail = Path.GetFileName(child)[(safeRole.Length + 1)..];
                if (int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= next)
                    next = index + 1;
            }
        return $"{safeRole}_{next:D4}";
    }

    public Run CreateChildRun(CortexForkRailRoles role)
    {
        if (role == CortexForkRailRoles.Unknown)
            throw new ArgumentOutOfRangeException(nameof(role), "a child run requires a concrete rail role");
        string token = role switch
        {
            CortexForkRailRoles.ForcedNull => "forced-null",
            CortexForkRailRoles.ReflexFrozen => "reflex-frozen",
            _ => role.ToString(),
        };
        return CreateChildRun(token);
    }

    /// Create a typed fork child and atomically bind its materialization marker to this parent run.
    public (Run Child, CortexForkMaterializationContract Contract) CreateMaterializedChildRun(
        CortexForkRailRoles role, string attemptID, string coldSeedDigest)
    {
        Run child = CreateChildRun(role);
        try
        {
            CortexForkMaterializationContract contract = new(
                Run.RunIDFromDirectory(Dir), attemptID, Path.GetFileName(child.Dir), coldSeedDigest);
            contract.Validate(child.Dir);
            byte[] marker = Encoding.UTF8.GetBytes(contract.Encode());
            child.WriteAtomic(CortexForkMaterializationContract.MarkerFileName,
                stream => stream.Write(marker));
            return (child, contract);
        }
        catch
        {
            if (Directory.Exists(child.Dir)) Directory.Delete(child.Dir, recursive: true);
            throw;
        }
    }

    public string NextChildRunID(CortexForkRailRoles role)
    {
        if (role == CortexForkRailRoles.Unknown)
            throw new ArgumentOutOfRangeException(nameof(role), "a child run requires a concrete rail role");
        string token = role switch
        {
            CortexForkRailRoles.ForcedNull => "forced-null",
            CortexForkRailRoles.ReflexFrozen => "reflex-frozen",
            _ => role.ToString(),
        };
        return NextChildRunID(token);
    }

    private static string ValidateChildRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        string safe = role.Trim().ToLowerInvariant();
        if (safe is "." or ".."
            || Path.IsPathRooted(role)
            || safe.Contains('/')
            || safe.Contains('\\')
            || safe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || safe.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException($"child run role must be a single safe name: '{role}'", nameof(role));
        return safe;
    }

    /// The gitignored artifact home (runs/ under the project root) for a STATELESS verb's default output —
    /// a one-shot export (export/couplings JSON) has no run dir, so its default lands here instead of
    /// littering the CWD (an explicit out-path arg still wins). Ensures runs/ exists.
    public static string HomePath(string file)
    {
        var root = Path.Combine(ProjectRoot(), "runs");
        Directory.CreateDirectory(root);
        return Path.Combine(root, file);
    }

    /// The runs/ root under the project root — the sweep surface for retention verbs (`runs gc`).
    public static string RunsRoot() => Path.Combine(ProjectRoot(), "runs");

    public string PathOf(string file) => Path.Combine(Dir, file);
    internal static object CheckpointWriteGate(string dir)
        => CheckpointLocks.GetOrAdd(Path.GetFullPath(dir), static _ => new object());
    public void Write(string file, string content) => File.WriteAllText(PathOf(file), content);
    public void Write(string file, byte[] content) => File.WriteAllBytes(PathOf(file), content);

    /// Write one artifact through a sibling temporary file, flush it durably, and atomically replace the final
    /// path. The callback owns serialization; this boundary owns only the safe-to-kill landing and reports the
    /// exact bytes that crossed it. A callback must leave the supplied stream open.
    public long WriteAtomic(string file, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(write);
        string final = PathOf(file);
        string temporary = final + ".tmp";
        long bytes;
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            write(stream);
            stream.Flush(flushToDisk: true);
            bytes = stream.Position;
        }
        File.Move(temporary, final, overwrite: true);
        return bytes;
    }

    public void WriteCurve(string file, string content)
    {
        File.WriteAllText(PathOf(file), content);
        RunPlotDocument.Load(Dir, file).Render();
    }

    /// An APPEND writer for an incremental artifact (curve.tsv, journal.log, rankings.jsonl) — line-flushed so a
    /// killed run keeps every completed line (the safe-to-kill law); the checkpoint records the flushed byte horizon.
    public StreamWriter Appender(string file)
        => new LineFlushWriter(new FileStream(PathOf(file), FileMode.Append, FileAccess.Write, FileShare.Read));

    public StreamWriter CurveAppender(string file)
        => new RunCurveWriter(this, file);

    /// Truncate an incremental artifact to a checkpoint's byte horizon — the resume path sheds rows a kill
    /// appended after the last snapshot, so the continuation splices byte-exact.
    public void Truncate(string file, long length)
    {
        using FileStream stream = new(PathOf(file), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        stream.SetLength(length);
    }

    public void TruncateCurve(string file, long length)
    {
        using (FileStream stream = new(PathOf(file), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            stream.SetLength(length);
        RunPlotDocument.Load(Dir, file).Render();
    }

    /// Keep only rows whose leading step is before the resumed logical horizon, then atomically replace the
    /// artifact and regenerate its plot. The horizon is derived from checkpoint state, never serialized as a
    /// second telemetry byte offset; this keeps append-only curves honest when a killed leg left orphan rows.
    public long TruncateCurveByLeadingStep(string file, long nextStep)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        if (!File.Exists(PathOf(file))) return 0;

        long bytes = WriteAtomic(file, stream =>
        {
            using StreamReader reader = new(PathOf(file), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16_384, leaveOpen: true);
            string? line;
            bool header = true;
            while ((line = reader.ReadLine()) is not null)
            {
                if (header)
                {
                    writer.WriteLine(line);
                    header = false;
                    continue;
                }

                int tab = line.IndexOf('\t');
                ReadOnlySpan<char> leading = (tab < 0 ? line : line[..tab]).AsSpan();
                if (long.TryParse(leading, NumberStyles.Integer, CultureInfo.InvariantCulture, out long step) && step < nextStep)
                    writer.WriteLine(line);
            }
            writer.Flush();
        });
        RunPlotDocument.Load(Dir, file).Render();
        return bytes;
    }

    /// Walk up from the binary to the project root (where cogito.slnx lives) — so runs land in the source
    /// tree for review, not buried under bin/.
    private static string ProjectRoot()
    {
        var d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "cogito.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? Directory.GetCurrentDirectory();
    }

    /// ONE FLUSH PER DURABLE LINE: partial Write()s compose the row in the buffer; each completed WriteLine
    /// lands with a single Flush (one write(2) to the OS). This keeps the safe-to-kill law — every completed
    /// line is durable-to-OS before the step proceeds — at LINE-granular atomicity, where AutoFlush paid a
    /// syscall per Write() call and could land a torn half-row under a mid-row kill.
    private class LineFlushWriter : StreamWriter
    {
        internal LineFlushWriter(Stream stream)
            : base(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 32_768) { }

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            Flush();
        }

        public override void WriteLine()
        {
            base.WriteLine();
            Flush();
        }
    }

    private sealed class RunCurveWriter : LineFlushWriter
    {
        private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(10);

        private readonly RunPlotDocument _plots;
        private readonly object _renderGate = new();
        private readonly Timer _renderTimer;
        private int _dirty;
        private int _rendering;
        private bool _disposed;

        internal RunCurveWriter(Run run, string file)
            : base(new FileStream(run.PathOf(file), FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            _plots = RunPlotDocument.Load(run.Dir, file);
            _renderTimer = new Timer(RenderIfDirty, null, RenderInterval, RenderInterval);
        }

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            _plots.ObserveLine(value);
            Interlocked.Exchange(ref _dirty, 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _renderTimer.Dispose();
                Flush();
                lock (_renderGate)
                {
                    _disposed = true;
                    Interlocked.Exchange(ref _dirty, 0);
                    RenderPlots();
                }
            }
            base.Dispose(disposing);
        }

        private void RenderIfDirty(object? state)
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0) return;
            if (Interlocked.CompareExchange(ref _rendering, 1, 0) != 0)
            {
                Interlocked.Exchange(ref _dirty, 1);
                return;
            }

            try
            {
                lock (_renderGate)
                {
                    if (_disposed) return;
                    RenderPlots();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _rendering, 0);
            }
        }

        private void RenderPlots()
        {
            try
            {
                _plots.Render();
            }
            catch (Exception exception)
            {
                Trace.Note($"plot render failed · {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
