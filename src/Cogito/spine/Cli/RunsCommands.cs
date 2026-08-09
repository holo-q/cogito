namespace Cogito.Cli;

using System.CommandLine;

// ── runs ──  retention over the runs/ arc store. R18 died ENOSPC (runs/ at 100%); `runs gc` is the
// substrate that keeps that from recurring — an age + terminal-state reclaim over interim grammar images
// and orphaned temporaries, dry-run by default so the plan is always inspected before anything is deleted.
internal static class RunsCommands
{
    internal static Command Gc()
    {
        Option<int> keep = new("--keep") { Description = "grammar revisions kept per run dir (newest N, always incl. terminal)", DefaultValueFactory = _ => 4 };
        Option<int> olderThan = new("--older-than") { Description = "only reclaim run dirs whose newest file predates N days (terminal runs always eligible)", DefaultValueFactory = _ => 7 };
        Option<bool> keepTemporaries = new("--keep-temporaries") { Description = "leave orphaned *.tmp atomic-write leftovers in place" };
        Option<bool> apply = new("--apply") { Description = "DELETE the planned items (default: dry-run, plan only)" };

        Command command = new("gc", "reclaim interim run artifacts — dry-run plan by default, --apply to execute")
        {
            keep, olderThan, keepTemporaries, apply,
        };
        command.SetAction(parse =>
        {
            RunsReclaim.Options options = new(
                KeepGrammarRevisions: parse.GetValue(keep),
                OlderThanDays: parse.GetValue(olderThan),
                IncludeStaleTemporaries: !parse.GetValue(keepTemporaries));
            RunReclaim(new RunsReclaim(options), options, apply: parse.GetValue(apply));
        });
        return command;
    }

    private static void RunReclaim(RunsReclaim reclaim, RunsReclaim.Options options, bool apply)
    {
        string runsRoot = Run.RunsRoot();
        RunsReclaim.Plan plan = reclaim.PlanReclaim(runsRoot);

        Console.WriteLine($"runs gc · {runsRoot}");
        Console.WriteLine($"  policy · keep-newest={options.KeepGrammarRevisions} grammar revisions · older-than={options.OlderThanDays}d · temporaries={(options.IncludeStaleTemporaries ? "reclaim" : "keep")}");
        Console.WriteLine($"  scanned {plan.RunDirsScanned} run dirs · {plan.RegisteredSkipped.Count} registered subtrees skipped (banked, immutable)");
        Console.WriteLine();

        if (plan.Items.Count == 0)
        {
            Console.WriteLine("  nothing to reclaim — the run store is already lean under this policy");
            return;
        }

        Console.WriteLine("  reclaim plan by class:");
        foreach ((RunsReclaim.ReclaimClasses cls, int count, long bytes) in plan.ByClass())
            Console.WriteLine($"    {cls,-20} · {count,6} files · {RunsReclaim.FormatBytes(bytes),12}");
        Console.WriteLine();

        // The heaviest individual reclaims, so the plan names WHERE the space is, not just how much.
        Console.WriteLine("  largest reclaims:");
        foreach (RunsReclaim.ReclaimItem item in plan.Items.OrderByDescending(static i => i.Bytes).Take(12))
            Console.WriteLine($"    {RunsReclaim.FormatBytes(item.Bytes),12}  {Path.GetRelativePath(runsRoot, item.Path)}  ({item.Reason})");
        Console.WriteLine();

        Console.WriteLine($"  TOTAL reclaimable · {plan.Items.Count} files · {RunsReclaim.FormatBytes(plan.TotalBytes)}");
        if (!apply)
        {
            Console.WriteLine("  DRY-RUN — nothing deleted. Re-run with --apply to reclaim.");
            return;
        }
        long reclaimed = RunsReclaim.Apply(plan);
        Console.WriteLine($"  APPLIED — reclaimed {RunsReclaim.FormatBytes(reclaimed)} across {plan.Items.Count} files.");
    }
}
