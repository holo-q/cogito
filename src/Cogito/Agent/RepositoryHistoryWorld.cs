namespace Cogito;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Cogito.Induct;

/// G5 — the moving world, taken rather than designed.
///
/// Every prior nonstationary feed in this project was CURATED: someone chose the epochs, the order,
/// the novelty schedule, and the organism's criticality was then partly a property of that choice.
/// A repository does not need any of it. Its own git history IS a natural nonstationary world —
/// real commits, real drift, in the order they actually happened, curated by nobody. Replaying it
/// forward is the perpetual-novel feed with the designer removed.
///
/// An epoch is one commit's view of a fixed path set. The path set is held fixed across epochs on
/// purpose: what must move is the CONTENT, not the membership, or "novelty" would just be files
/// appearing and the measurement would be about the tree's shape instead of the world's drift.
internal static class RepositoryHistoryWorld
{
    internal readonly record struct Epoch(string Commit, byte[] Bytes);

    /// The epochs, oldest first. `paths` are repo-relative and must exist at every sampled commit —
    /// a path missing from one epoch is dropped from all of them, so every epoch spans the same
    /// world and their residuals are comparable.
    internal static Epoch[] ReadEpochs(string root, string[] paths, int epochs)
    {
        if (epochs < 2) throw new ArgumentOutOfRangeException(nameof(epochs), "a moving world needs at least two epochs");
        string[] logArguments = [.. new[] { "log", "--reverse", "--format=%H", "--" }.Concat(paths)];
        string[] commits = [.. RunGit(root, logArguments).Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        if (commits.Length < epochs)
            throw new InvalidDataException($"the history holds {commits.Length} commits touching the sampled paths, fewer than the {epochs} epochs asked for");

        // Spread the sample across the WHOLE history rather than taking the newest run of commits:
        // consecutive commits differ by one edit, and a world that barely moves cannot tell a
        // criticality that holds from one that was never tested.
        string[] sampled = [.. Enumerable.Range(0, epochs)
            .Select(index => commits[(int)((long)index * (commits.Length - 1) / (epochs - 1))])];

        string[] surviving = [.. paths.Where(path => sampled.All(commit => TryReadBlob(root, commit, path, out _)))];
        if (surviving.Length == 0)
            throw new InvalidDataException("no sampled path survives every epoch — the world has no fixed membership to move");

        return [.. sampled.Select(commit =>
        {
            List<byte> bytes = [];
            foreach (string path in surviving)
            {
                TryReadBlob(root, commit, path, out byte[] blob);
                bytes.AddRange(blob);
                bytes.Add((byte)'\n');
            }
            return new Epoch(commit, [.. bytes]);
        })];
    }

    /// What each commit ADDED, relative to the epoch before it — the feed unit a moving world
    /// actually offers. A whole tree view cannot be eaten twice: consecutive commits share ~80% of
    /// their bytes, so the intake gate correctly refuses the second view and the organism starves
    /// beside a world that IS changing. The change itself clears the bar because it is nearly all
    /// new. The first epoch has no predecessor and contributes its whole view — the organism has to
    /// start somewhere.
    internal static Epoch[] ReadDeltas(string root, Epoch[] epochs, string[] paths)
    {
        Epoch[] deltas = new Epoch[epochs.Length];
        deltas[0] = epochs[0];
        for (int index = 1; index < epochs.Length; index++)
        {
            string[] diffArguments = [.. new[] { "diff", "--unified=0", "--no-color", epochs[index - 1].Commit, epochs[index].Commit, "--" }.Concat(paths)];
            string diff = RunGit(root, diffArguments);
            StringBuilder added = new();
            foreach (string line in diff.Split('\n'))
                if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
                    added.Append(line, 1, line.Length - 1).Append('\n');
            deltas[index] = new Epoch(epochs[index].Commit, Encoding.UTF8.GetBytes(added.ToString()));
        }
        return deltas;
    }

    private static bool TryReadBlob(string root, string commit, string path, out byte[] bytes)
    {
        try
        {
            bytes = Encoding.UTF8.GetBytes(RunGit(root, "show", $"{commit}:{path}"));
            return true;
        }
        catch (InvalidDataException)
        {
            bytes = [];
            return false;
        }
    }

    private static string RunGit(string root, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidDataException("git did not start");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidDataException($"git {string.Join(' ', arguments)} exited {process.ExitCode}");
        return output;
    }
}

/// G5's kill-line — criticality on a world that MOVES, against the same world standing still.
///
/// The banked finding this rung inherits: re-feeding an organism its own learned reality
/// renormalizes it like a dream and sinks its criticality below the basin, while an open pipe to a
/// changing world holds it. That was measured on synthesized feeds. Here the moving world is not
/// synthesized at all — it is the repository's own commits — and the control is the SAME world held
/// still, so the arms differ in one thing only: whether reality moved.
///
/// Both arms spend identical fuel (N intakes) on identical bytes at epoch 0. The moving arm then
/// receives the world as it actually changed; the static arm receives epoch 0 again, N times. The
/// G1 intake gate is live in both, which is what makes the static arm honest rather than merely
/// wasteful: its re-feeds are ADMITTED only if they pay, so a sunk meanz cannot be dismissed as an
/// artifact of stuffing the tape with duplicate bytes.
internal static class RepositoryHistoryCriticalityNull
{
    private const double BasinLow = -0.95, BasinHigh = -0.50;    // the −0.70 critical class
    private const double AffirmCut = 0.25;
    private const int Epochs = 6;

    internal static bool Verify(TextWriter output)
    {
        try
        {
            string root = FindRepositoryRoot()
                ?? throw new InvalidDataException("repository root not found — the moving world is the repository's own history, and will not be synthesized");
            string[] paths =
            [
                "src/Cogito/Kernel/Engine.cs", "src/Cogito/Kernel/Induct.cs", "src/Cogito/Kernel/Loom.cs",
                "src/Cogito/Tape/Tape.cs", "src/Cogito/Tape/Journal.cs", "src/Cogito/Drive/Radula.cs",
            ];
            RepositoryHistoryWorld.Epoch[] history = RepositoryHistoryWorld.ReadEpochs(root, paths, Epochs);
            RepositoryHistoryWorld.Epoch[] deltas = RepositoryHistoryWorld.ReadDeltas(root, history, paths);

            (double movingMeanZ, long movingEarned, double[] movingTrace, double[] movingResiduals) = Feed(deltas);
            (double staticMeanZ, long staticEarned, double[] staticTrace, double[] staticResiduals) = Feed([.. Enumerable.Repeat(deltas[0], Epochs)]);

            output.WriteLine($"    world · {history.Length} epochs from {history[0].Commit[..8]} to {history[^1].Commit[..8]}"
                           + $" · view {history[0].Bytes.Length}B → {history[^1].Bytes.Length}B · change {string.Join('/', deltas.Skip(1).Select(static delta => delta.Bytes.Length))}B");
            output.WriteLine($"    moving · residual {Render(movingResiduals)}");
            output.WriteLine($"    static · residual {Render(staticResiduals)}");
            output.WriteLine($"    moving · meanz {Render(movingTrace)} → {movingMeanZ:F4} · earned {movingEarned}B");
            output.WriteLine($"    static · meanz {Render(staticTrace)} → {staticMeanZ:F4} · earned {staticEarned}B");

            // Fed its own change, the organism keeps eating; fed the same change again, it does not.
            // That asymmetry is the point of the control: both arms start identical and spend the
            // same fuel, so a criticality that holds in one and sinks in the other is caused by the
            // world moving and by nothing else.
            bool movingHolds = movingMeanZ >= BasinLow && movingMeanZ <= BasinHigh;
            bool movingKeepsEating = movingEarned > staticEarned;
            bool staticStalls = staticEarned <= deltas[0].Bytes.Length + 1;
            output.WriteLine($"  repository-history-null · moving-holds-basin={(movingHolds ? "PASS" : "FAIL")}"
                           + $" · moving-keeps-eating={(movingKeepsEating ? "PASS" : "FAIL")}"
                           + $" · static-stalls-after-first={(staticStalls ? "PASS" : "FAIL")} (basin [{BasinLow:F2},{BasinHigh:F2}])");
            return movingHolds && movingKeepsEating && staticStalls;
        }
        catch (Exception failure)
        {
            output.WriteLine($"  repository-history-criticality-null · FAIL — {failure.Message}");
            return false;
        }
    }

    /// Feed the epochs in order through the live intake gate, inducing after each admitted epoch.
    /// Returns the criticality the organism ends on, the grammar bytes it earned, and the meanz it
    /// passed through — the trace matters because a mind can end in the basin having fallen through
    /// it, and an endpoint alone would hide that.
    private static (double MeanZ, long Earned, double[] Trace, double[] Residuals) Feed(RepositoryHistoryWorld.Epoch[] epochs)
    {
        double[] residuals = new double[epochs.Length];
        using Tape tape = new();
        List<byte[]> eaten = [];
        Engine.GrammarCover? cover = null;
        RePairResult grammar = default;
        long earned = 0;
        double[] trace = new double[epochs.Length];
        for (int index = 0; index < epochs.Length; index++)
        {
            byte[] bytes = epochs[index].Bytes;
            Radula.Affirmation measurement = Radula.MeasureAffirmation(cover, bytes, AffirmCut);
            bool admit = !measurement.Affirmed;
            long before = tape.GrammarByteLength;
            tape.Append(bytes, "history:epoch", Provenances.Real,
                TapeEventRoles.Measurement | TapeEventRoles.AuditOnly | (admit ? TapeEventRoles.GrammarInput : 0));
            earned += tape.GrammarByteLength - before;
            if (admit)
            {
                eaten.Add(bytes);
                List<byte> corpus = [];
                foreach (byte[] span in eaten) { corpus.AddRange(span); corpus.Add((byte)'\n'); }
                (_, _, grammar) = Engine.Induce([.. corpus]);
                cover = new Engine.GrammarCover(grammar.Rules);
            }
            trace[index] = grammar.Rules is null or { Length: 0 } ? double.NaN : Engine.RenormStats(grammar).MeanZ;
            residuals[index] = measurement.Residual;
        }
        return (trace[^1], earned, trace, residuals);
    }

    /// True when every step is at least as large as the one before it — a decay that never reverses.
    private static bool Monotone(double[] values)
    {
        for (int index = 1; index < values.Length; index++)
            if (values[index] < values[index - 1]) return false;
        return true;
    }

    private static string Render(double[] trace)
        => string.Join(" ", trace.Select(static value => double.IsNaN(value) ? "n/a" : value.ToString("F4")));

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(Environment.CurrentDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "cogito.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}
