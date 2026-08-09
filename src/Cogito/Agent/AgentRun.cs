namespace Cogito;

using System.Text;

// ── AGENTRUN ──  the shared home for the navigation instance-stream verbs (`navigate --all` · `navdyn` · `navloop`).
// Each emits a per-instance journal step, a running file@1 curve, and per-instance rankings into one Run. The
// Cortex-backed LOC runtime owns the `solve` run and checkpoint lifecycle directly; AgentRun remains the navigation
// envelope that mints the run, mounts the Journal, and opens the append-only artifacts in the run directory.
//
// WHAT IS SHARED IS THE PLUMBING, NOT THE ROW. Each navigation verb formats its OWN journal line, curve row, and
// rankings JSON. AgentRun mints the run, mounts the journal sink, opens the two append writers (auto-flushed, so a killed
// run keeps every completed line — the safe-to-kill law), and writes the config + the intake manifest. The verb owns
// the content.
//
// THE INTAKE/OUTPUT BOUNDARY: the run dir holds OUTPUTS ONLY. The INTAKE — the external world the
// engine ran ON (the swe_loc data dir, a corpus tree) — is NEVER copied in (that would bloat runs/ and duplicate data
// that lives elsewhere). It is RECORDED BY A MANIFEST instead: the intake path + a content FINGERPRINT (drift
// detection: did the external world change since the run?) + the selection config (--limit, seed, ordering). The
// test: from runs/<verb>_NNNN/manifest ALONE, someone re-runs the exact same thing against the same
// verified-by-hash external world — the run is REPRODUCIBLE, the data is not duplicated.
public sealed class AgentRun : IDisposable
{
    public Run Run { get; }
    public Journal Journal { get; }

    private readonly StreamWriter _journalW;
    private StreamWriter? _rankings;      // rankings.jsonl — the per-instance results (opened lazily: not every verb emits JSON)
    private StreamWriter? _curve;         // curve.tsv — the running file@1 / accuracy stream

    private AgentRun(Run run, Journal? journal = null)
    {
        Run = run;
        Journal journalOwner = journal ?? new Journal();
        StreamWriter journalW = run.Appender("journal.log");
        try
        {
            journalOwner.Mount(journalW); // from here every journal line lands on disk as it happens (the live-flushed record)
        }
        catch
        {
            try { journalW.Dispose(); }
            finally
            {
                if (journal is null) journalOwner.Dispose();
            }
            throw;
        }
        Journal = journalOwner;
        _journalW = journalW;
    }

    /// Begin a fresh agent run: mint runs/<verb>_NNNN/ (Run.New prints the `run → …` locator), write the config
    /// snapshot + the intake manifest, and mount the journal sink. `config` is the verb's own deterministic knob
    /// snapshot; `intake` records the EXTERNAL world the run consumed (path + fingerprint + selection) so the run is
    /// reproducible without holding a copy of the data. The verb opens Curve/Rankings as it needs them.
    public static AgentRun Begin(string verb, string config, IntakeManifest intake)
    {
        var run = Cogito.Run.New(verb);
        run.Write("config", config.EndsWith('\n') ? config : config + "\n");
        run.Write("manifest", intake.Render());
        return new AgentRun(run);
    }

    /// Resume an existing agent run with a checkpoint-restored journal. The caller has already truncated/rewritten
    /// append-only artifacts to the checkpoint horizons; this only remounts the live sinks for the continuation.
    public static AgentRun Resume(Run run, Journal journal) => new(run, journal);

    /// The curve.tsv appender — the running file@1 / accuracy stream (one row per instance, auto-flushed). `header`
    /// lands once on first call (the TSV column line); rows append INCREMENTALLY as instances complete, so a killed
    /// run keeps every completed row. Idempotent — the header writes only on the open.
    public StreamWriter Curve(string header)
    {
        if (_curve is null)
        {
            _curve = Run.CurveAppender("curve.tsv");
            if (new FileInfo(Run.PathOf("curve.tsv")).Length == 0) _curve.WriteLine(header);
        }
        return _curve;
    }

    /// The rankings.jsonl appender — the per-instance results (one JSON object per line, auto-flushed). Opened lazily
    /// so a navigation verb that has no per-instance JSON never opens the file.
    public StreamWriter Rankings => _rankings ??= Run.Appender("rankings.jsonl");

    /// Land a final artifact into the run dir (a report, a summary) — the drive's PHASE 2 LAND for the agent verbs.
    public void Write(string file, string content) => Run.Write(file, content);

    public string Dir => Run.Dir;

    public void Dispose()
    {
        try { _curve?.Dispose(); }
        finally
        {
            try { _rankings?.Dispose(); }
            finally
            {
                try { _journalW.Dispose(); }
                finally { Journal.Dispose(); }
            }
        }
    }
}

/// THE INTAKE MANIFEST — the run's record of the EXTERNAL world it consumed, enough to reproduce + verify without
/// copying the data. `Path` is where the world lives; `Fingerprint` is a content hash (drift detection — a re-run
/// against a changed world is caught by a mismatched fingerprint); `Selection` is the deterministic subset/order
/// (the --limit / seed / stream-ordering that, with the world, pins the exact run). `World` optionally names the
/// intake-manifest (the `corpus gather` world recipe) the intake was built from, so the record REFERENCES the world
/// definition rather than re-listing its files.
public readonly record struct IntakeManifest(string Path, ulong Fingerprint, string Selection, string World = "")
{
    /// The manifest file body — one legible `key=value` line per fact (the house's dependency-free shape). The
    /// fingerprint renders hex (a stable content digest); everything else is the reproduction recipe.
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append("intake=").Append(Path).Append('\n');
        sb.Append("fingerprint=").Append(Fingerprint.ToString("x16")).Append('\n');
        if (World.Length > 0) sb.Append("world=").Append(World).Append('\n');
        sb.Append("selection=").Append(Selection).Append('\n');
        return sb.ToString();
    }

    /// Fingerprint an intake path for the manifest — a deterministic FNV-1a/64 (Simhash.Fnv64, the org's canonical
    /// content hash) folded over the world's SHAPE: a FILE hashes its bytes; a DIRECTORY folds each contained regular
    /// file's (repo-relative path · byte length · content hash) in sorted-path order. Path+size+content catches any
    /// drift (a file added, removed, resized, or edited) while staying one pass — no mtime (not reproducible across
    /// clones), no listing stored (the hash IS the drift detector). A missing path fingerprints 0 (the caller's
    /// existence check already gated the run).
    public static ulong Of(string path)
    {
        if (File.Exists(path)) return Simhash.Fnv64(File.ReadAllBytes(path));
        if (!Directory.Exists(path)) return 0;
        ulong h = 14695981039346656037UL;
        var full = System.IO.Path.GetFullPath(path);
        foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var rel = System.IO.Path.GetRelativePath(full, f);
            foreach (var b in Encoding.UTF8.GetBytes(rel)) { h ^= b; h *= 1099511628211UL; }
            var info = new FileInfo(f);
            h ^= (ulong)info.Length; h *= 1099511628211UL;
            h ^= Simhash.Fnv64(File.ReadAllBytes(f)); h *= 1099511628211UL;
        }
        return h;
    }
}

/// Checkpoint dialect for navigation instance-stream runs. Cortex owns its full runtime checkpoint; the navigation
/// verbs share only this stream envelope and serialize their private runtime state inside the preserved MIND wire section.
public static class AgentCheckpoint
{
    public const string FileName = Checkpoint.FileName;
    private static ReadOnlySpan<byte> Magic => "CGAGENT\n"u8;

    public enum AgentVerbs : byte { Navigate = 2, NavDyn = 3, NavLoop = 4 }
    public readonly record struct StreamSnap(int Next, int Pass, long JournalLen, long RankingsLen, long CurveLen);

    private const uint TagConfig  = 0x43464721;   // CFG!
    private const uint TagSnap    = 0x534E4150;   // SNAP
    private const uint TagJournal = 0x4A524E4C;   // JRNL
    private const uint TagRuntime = 0x4D494E44;   // MIND on wire; retained for existing navigation checkpoints
    private const uint TagEnd     = 0x454E4421;   // END!

    public static byte[] Encode(AgentVerbs verb, string config, in StreamSnap snap, Journal journal, Action<CkptWriter> writeRuntime)
    {
        using var ms = new MemoryStream(1 << 20);
        using (var w = new CkptWriter(ms))
        {
            w.Raw(Magic);
            w.Section(TagConfig);  w.U8((byte)verb); w.Str(config);
            w.Section(TagSnap);    WriteSnap(w, snap);
            w.Section(TagRuntime); writeRuntime(w);
            w.Section(TagJournal); journal.Save(w);
            w.Section(TagEnd);
        }
        return ms.ToArray();
    }

    public static long Save(Run run, byte[] image) => Checkpoint.Save(run, image);

    public static AgentVerbs PeekVerb(string runDir)
    {
        using var fs = File.OpenRead(Path.Combine(runDir, FileName));
        using var r = new CkptReader(fs);
        ReadMagic(r);
        r.Expect(TagConfig);
        return (AgentVerbs)r.U8();
    }

    public static (string Config, StreamSnap Snap, Journal Journal) Load(string runDir, AgentVerbs expected, Func<CkptReader, Tape> readRuntime)
    {
        using var fs = File.OpenRead(Path.Combine(runDir, FileName));
        using var r = new CkptReader(fs);
        ReadMagic(r);
        r.Expect(TagConfig);
        var verb = (AgentVerbs)r.U8();
        if (verb != expected) throw new InvalidDataException($"agent checkpoint verb skew: expected {expected}, got {verb}");
        string config = r.Str();
        r.Expect(TagSnap); var snap = ReadSnap(r);
        r.Expect(TagRuntime);
        var tape = readRuntime(r);
        r.Expect(TagJournal);
        var journal = new Journal();
        journal.Load(r, tape);
        r.Expect(TagEnd);
        return (config, snap, journal);
    }

    private static void WriteSnap(CkptWriter w, in StreamSnap snap)
    {
        w.I32(snap.Next); w.I32(snap.Pass);
        w.I64(snap.JournalLen); w.I64(snap.RankingsLen); w.I64(snap.CurveLen);
    }

    private static StreamSnap ReadSnap(CkptReader r)
        => new(r.I32(), r.I32(), r.I64(), r.I64(), r.I64());

    private static void ReadMagic(CkptReader r)
    {
        var m = r.Raw(Magic.Length);
        if (m.AsSpan().SequenceEqual(Magic)) return;
        throw new InvalidDataException(m.AsSpan().StartsWith("CG"u8)
            ? $"checkpoint format skew: file is {System.Text.Encoding.ASCII.GetString(m).TrimEnd('\n')}, the agent runner reads {System.Text.Encoding.ASCII.GetString(Magic).TrimEnd('\n')}"
            : "not a cogito agent checkpoint (bad magic)");
    }
}

public static class AgentResume
{
    public static int Resume(string runDir, bool verify, int steps)
    {
        var dir = Cogito.Run.Resolve(runDir);
        if (dir is null || !File.Exists(Path.Combine(dir, AgentCheckpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {AgentCheckpoint.FileName} under '{runDir}' — nothing to resume");
            return 1;
        }
        return AgentCheckpoint.PeekVerb(dir) switch
        {
            AgentCheckpoint.AgentVerbs.Navigate => Navigate.Resume(dir, verify, steps),
            AgentCheckpoint.AgentVerbs.NavDyn => NavDyn.Resume(dir, verify, steps),
            AgentCheckpoint.AgentVerbs.NavLoop => NavLoop.Resume(dir, verify, steps),
            _ => ReportUnsupportedAgentVerb(),
        };
    }

    private static int ReportUnsupportedAgentVerb()
    {
        Console.Error.WriteLine("  agent checkpoint verb is unsupported; only navigate, navdyn, and navloop checkpoints can resume here");
        return 1;
    }
}
