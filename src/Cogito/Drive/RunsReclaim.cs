namespace Cogito;

using System.Globalization;
using System.Text;

/// The reclaim planner for runs/ (R19 retention). The audit found grammar-revision-*.bin at 43 GB
/// across ~19k files with ZERO pruning and children/ never GC'd — the substrate that pushed R18 to
/// ENOSPC. This walks the run tree, classifies reclaimable interim images, and either reports the
/// plan (dry-run) or deletes it (apply). It is fail-SAFE by construction: a run subtree that carries
/// a loop-closure registration is banked and immutable, so it is skipped whole; the latest N grammar
/// revisions per dir are always kept, as is any revision a receipt cites, so custody stays verifiable.
public sealed class RunsReclaim
{
    public readonly record struct Options(int KeepGrammarRevisions, int OlderThanDays, bool IncludeStaleTemporaries)
    {
        public static Options Default => new(KeepGrammarRevisions: 4, OlderThanDays: 7, IncludeStaleTemporaries: true);
    }

    public enum ReclaimClasses : byte { GrammarInterimImage, StaleTemporary }

    public readonly record struct ReclaimItem(ReclaimClasses Class, string Path, long Bytes, string Reason);

    public readonly record struct SkippedRun(string Path, string Reason);

    public sealed class Plan
    {
        public List<ReclaimItem> Items { get; } = [];
        public List<SkippedRun> RegisteredSkipped { get; } = [];
        public int RunDirsScanned { get; set; }
        public long TotalBytes => Items.Sum(static item => item.Bytes);

        public IEnumerable<(ReclaimClasses Class, int Count, long Bytes)> ByClass()
            => Items.GroupBy(static item => item.Class)
                .Select(static g => (g.Key, g.Count(), g.Sum(static item => item.Bytes)))
                .OrderByDescending(static row => row.Item3);
    }

    private readonly Options _options;
    private readonly DateTime _ageCutoffUtc;

    public RunsReclaim(Options options)
    {
        if (options.KeepGrammarRevisions < 0) throw new ArgumentOutOfRangeException(nameof(options));
        _options = options;
        _ageCutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(0, options.OlderThanDays));
    }

    /// Plan the reclaim over every run dir directly under runs/. Registered subtrees are recorded as
    /// skipped and never entered; unregistered runs (and their children/) contribute interim images.
    public Plan PlanReclaim(string runsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runsRoot);
        Plan plan = new();
        string root = Path.GetFullPath(runsRoot);
        if (!Directory.Exists(root)) return plan;
        foreach (string runDir in Directory.GetDirectories(root).OrderBy(static d => d, StringComparer.Ordinal))
        {
            plan.RunDirsScanned++;
            ScanRunSubtree(runDir, plan);
        }
        return plan;
    }

    private void ScanRunSubtree(string dir, Plan plan)
    {
        if (File.Exists(Path.Combine(dir, LoopClosureRegistration.AuthorityFileName)))
        {
            plan.RegisteredSkipped.Add(new SkippedRun(dir, "carries a loop-closure registration (banked, immutable)"));
            return;
        }
        if (!IsEligibleByAge(dir)) return;
        CollectGrammarInterimImages(dir, plan);
        if (_options.IncludeStaleTemporaries) CollectStaleTemporaries(dir, plan);
        string children = Path.Combine(dir, "children");
        if (Directory.Exists(children))
            foreach (string child in Directory.GetDirectories(children).OrderBy(static d => d, StringComparer.Ordinal))
                ScanRunSubtree(child, plan);
    }

    /// A run dir is eligible when its newest content predates the age cutoff, OR it carries a terminal
    /// outcome (a settled run whose interim images are safe to shed). A live, recent run is left alone.
    private bool IsEligibleByAge(string dir)
    {
        if (CarriesTerminalOutcome(dir)) return true;
        DateTime newest = DateTime.MinValue;
        foreach (string file in Directory.EnumerateFiles(dir))
        {
            DateTime written = File.GetLastWriteTimeUtc(file);
            if (written > newest) newest = written;
        }
        return newest != DateTime.MinValue && newest < _ageCutoffUtc;
    }

    private static bool CarriesTerminalOutcome(string dir)
    {
        if (File.Exists(Path.Combine(dir, LoopClosureCertificationTerminalOutcome.FileName))) return true;
        string name = Path.GetFileName(dir);
        return File.Exists(Path.Combine(dir, name + "." + LoopClosureTerminalOutcome.FileName));
    }

    /// Grammar images beyond the newest N, minus any revision a receipt cites. Revisions are ordered by
    /// their numeric value (the filename embeds a 16-hex revision), so the N highest survive — which
    /// always includes the terminal/resume revision — and older interim images are reclaimed.
    private void CollectGrammarInterimImages(string dir, Plan plan)
    {
        List<(ulong Revision, string Path, long Bytes)> images = [];
        foreach (string file in Directory.EnumerateFiles(dir, "grammar-revision-*.bin"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            int dash = stem.LastIndexOf('-');
            if (dash < 0 || !ulong.TryParse(stem.AsSpan(dash + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong revision))
                continue;
            images.Add((revision, file, new FileInfo(file).Length));
        }
        if (images.Count <= _options.KeepGrammarRevisions) return;

        HashSet<ulong> cited = CitedRevisions(dir, images);
        var doomed = images
            .OrderByDescending(static image => image.Revision)
            .Skip(_options.KeepGrammarRevisions)
            .Where(image => !cited.Contains(image.Revision));
        foreach (var (revision, path, bytes) in doomed)
            plan.Items.Add(new ReclaimItem(ReclaimClasses.GrammarInterimImage, path, bytes,
                $"interim grammar revision {revision:X16} beyond the newest {_options.KeepGrammarRevisions}, uncited"));
    }

    /// Revisions named by any textual receipt in the dir (RON/JSON/log/tsv are UTF-8 text). A cited
    /// revision is protected however old it is, so a receipt referencing an image keeps it verifiable.
    private static HashSet<ulong> CitedRevisions(string dir, List<(ulong Revision, string Path, long Bytes)> images)
    {
        HashSet<ulong> cited = [];
        string[] receipts = Directory.EnumerateFiles(dir)
            .Where(static f => f.EndsWith(".ron", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal)
                || f.EndsWith(".log", StringComparison.Ordinal) || f.EndsWith(".tsv", StringComparison.Ordinal)
                || f.EndsWith(".txt", StringComparison.Ordinal))
            .ToArray();
        if (receipts.Length == 0) return cited;
        StringBuilder corpus = new();
        foreach (string receipt in receipts)
        {
            try { corpus.Append(File.ReadAllText(receipt)); corpus.Append('\n'); }
            catch (IOException) { /* a concurrently-written receipt is treated as citing nothing here */ }
        }
        string text = corpus.ToString();
        foreach (var image in images)
        {
            string token = $"{image.Revision:X16}";
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase)
                || text.Contains($"grammar-revision-{token}.bin", StringComparison.OrdinalIgnoreCase))
                cited.Add(image.Revision);
        }
        return cited;
    }

    private static void CollectStaleTemporaries(string dir, Plan plan)
    {
        foreach (string file in Directory.EnumerateFiles(dir, "*.tmp"))
            plan.Items.Add(new ReclaimItem(ReclaimClasses.StaleTemporary, file, SafeLength(file),
                "orphaned atomic-write temporary"));
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; } catch (IOException) { return 0; }
    }

    /// Delete every planned item. Returns the bytes actually reclaimed. Idempotent against a file that
    /// already vanished (a concurrent gc, a manual delete).
    public static long Apply(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        long reclaimed = 0;
        foreach (ReclaimItem item in plan.Items)
        {
            if (!File.Exists(item.Path)) continue;
            reclaimed += item.Bytes;
            File.Delete(item.Path);
        }
        return reclaimed;
    }

    public static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
