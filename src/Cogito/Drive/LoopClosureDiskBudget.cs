namespace Cogito;

/// The pre-arm disk budget for a registered loop-closure attempt. R18 died ENOSPC at step 498 with
/// runs/ at 100%; this refuses BEFORE a Cortex destination is created, while refusal is still free —
/// a fail-closed gate on free space, not a mid-run recovery. The budget is derived from the frozen
/// horizon and the empirical per-attempt footprint, never from live measurement: a registration's
/// footprint is fixed the moment its shape is sealed.
public readonly record struct LoopClosureDiskBudget(long RequiredFreeBytes, int Horizon)
{
    // R18 audit receipt: registered attempts land 2.3–3.8 GB per arm, 3.8 GB at p95, at the registered
    // 500-step horizon. The footprint is dominated by per-step checkpoint images, so it scales with the
    // horizon; the safety factor covers the child-arm fork waves and the p95→max tail that killed R18.
    public const long RegisteredHorizonArmBytes = 3_800_000_000L;
    public const int RegisteredHorizon = LoopClosureRegistration.RegisteredHorizon;
    public const double SafetyFactor = 2.0;

    public static LoopClosureDiskBudget ForRegistration(LoopClosureRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return ForHorizon(registration.Horizon);
    }

    public static LoopClosureDiskBudget ForHorizon(int horizon)
    {
        if (horizon < 1) throw new ArgumentOutOfRangeException(nameof(horizon), horizon, "loop-closure disk budget requires a positive horizon");
        double perArm = (double)RegisteredHorizonArmBytes * horizon / RegisteredHorizon;
        long required = checked((long)(perArm * SafetyFactor));
        return new LoopClosureDiskBudget(required, horizon);
    }

    /// Refuse the arm when the mount hosting its destination cannot cover the budget. Throws a typed,
    /// named refusal BEFORE the caller creates the run directory. Fails CLOSED: an unreadable or
    /// not-ready DriveInfo (a permission wall, a phantom mount) refuses rather than arming blind.
    public void RequireFreeSpace(string destination, string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        long free = ReadAvailableFreeBytes(destination);
        if (free < RequiredFreeBytes)
            throw new LoopClosureDiskBudgetException(
                $"loop-closure {stage} refused: {FormatBytes(free)} free on the mount hosting {destination} is below the "
                + $"{FormatBytes(RequiredFreeBytes)} pre-arm budget for horizon {Horizon}");
    }

    /// The free bytes on the mount that hosts (or would host) the path, resolved through the longest
    /// matching mount point so a runs/ mount distinct from / is measured correctly. Fails closed.
    private static long ReadAvailableFreeBytes(string path)
    {
        try
        {
            string probe = NearestExistingAncestor(Path.GetFullPath(path));
            DriveInfo drive = ResolveMount(probe);
            if (!drive.IsReady)
                throw new LoopClosureDiskBudgetException($"loop-closure disk budget cannot read a ready mount for {path}");
            return drive.AvailableFreeSpace;
        }
        catch (LoopClosureDiskBudgetException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LoopClosureDiskBudgetException($"loop-closure disk budget could not read free space for {path}: {ex.Message}", ex);
        }
    }

    private static string NearestExistingAncestor(string path)
    {
        string? cursor = path;
        while (!string.IsNullOrEmpty(cursor) && !Directory.Exists(cursor) && !File.Exists(cursor))
            cursor = Path.GetDirectoryName(cursor);
        return string.IsNullOrEmpty(cursor) ? path : cursor;
    }

    /// Pick the mount whose mount point is the longest prefix of the path — the mount that actually
    /// hosts it. Falls back to a direct DriveInfo when enumeration yields nothing usable.
    private static DriveInfo ResolveMount(string path)
    {
        DriveInfo? best = null;
        int bestLength = -1;
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            string mount;
            try { if (!drive.IsReady) continue; mount = drive.RootDirectory.FullName; }
            catch { continue; }
            if (!path.StartsWith(mount, StringComparison.Ordinal)) continue;
            if (mount.Length > bestLength) { best = drive; bestLength = mount.Length; }
        }
        return best ?? new DriveInfo(path);
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

/// A pre-arm disk-budget refusal. Subclasses IOException so callers that already classify IO refusals
/// treat a budget refusal in the same family, but it is thrown before any run destination exists.
public sealed class LoopClosureDiskBudgetException : IOException
{
    public LoopClosureDiskBudgetException(string message) : base(message) { }
    public LoopClosureDiskBudgetException(string message, Exception inner) : base(message, inner) { }
}
