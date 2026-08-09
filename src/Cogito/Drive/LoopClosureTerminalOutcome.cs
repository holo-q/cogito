namespace Cogito;

using System.Globalization;
using System.Text;
using Ronmamon;

public enum LoopClosureTerminalCauses : byte
{
    Completed,
    CortexExit,
    CortexException,
    RunnerFailure,
}

/// Durable, best-effort explanation of how a registered loop-closure arm ended.
/// The receipt is diagnostic state, not sealing authority: a failed write must never
/// replace the original run result or exception.
[RonObject]
public partial class LoopClosureTerminalOutcome
{
    public const int CurrentSchemaVersion = 2;
    public const string FileName = "loop-closure-terminal.ron";

    public static string PathFor(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        string run = Run.Resolve(runDirectory) ?? Path.GetFullPath(runDirectory);
        string parent = Path.GetDirectoryName(run) ?? ".";
        string name = Path.GetFileName(run);
        if (name.Length == 0) throw new ArgumentException("loop-closure run directory must have a basename", nameof(runDirectory));
        return Path.Combine(parent, name + "." + FileName);
    }

    public int schemaVersion = CurrentSchemaVersion;
    public LoopClosureArms arm;
    public LoopClosureRunStatuses status;
    public LoopClosureTerminalCauses cause;
    public string runDirectory = "";
    public string authoritySHA256 = "";
    public string phase = "";
    public string lastEventKind = "";
    public string lastEventSource = "";
    public int observedStep = -1;
    public int lastCompletedStep = -1;
    public int durableNextStep = -1;
    public int exitCode = -1;
    public string exceptionType = "";
    public string exceptionMessage = "";

    internal static LoopClosureTerminalOutcome Capture(
        LoopClosureArms arm,
        LoopClosureRunStatuses status,
        LoopClosureTerminalCauses cause,
        string runDirectory,
        string authoritySHA256,
        IPolicyBoundaryDomain domain,
        int exitCode = -1,
        Exception? error = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        try
        {
            int observedStep = ReadLastStep(runDirectory, "compute.tsv");
            int lastCompletedStep = ReadLastStep(runDirectory, "curve.tsv");
            int durableNextStep = ReadDurableNextStep(runDirectory);
            ReadLastJournalEvent(runDirectory, out string lastEventKind, out string lastEventSource);
            string phase = ResolvePhase(cause, observedStep, lastCompletedStep, lastEventKind, lastEventSource);
            return new LoopClosureTerminalOutcome
            {
                arm = arm,
                status = status,
                cause = cause,
                runDirectory = Run.Resolve(runDirectory) ?? Path.GetFullPath(runDirectory),
                authoritySHA256 = authoritySHA256 ?? "",
                phase = phase,
                lastEventKind = lastEventKind,
                lastEventSource = lastEventSource,
                observedStep = observedStep,
                lastCompletedStep = lastCompletedStep,
                durableNextStep = durableNextStep,
                exitCode = exitCode,
                exceptionType = error?.GetType().FullName ?? "",
                exceptionMessage = error?.Message ?? "",
            };
        }
        catch
        {
            return new LoopClosureTerminalOutcome
            {
                arm = arm,
                status = status,
                cause = cause,
                runDirectory = runDirectory ?? "",
                authoritySHA256 = authoritySHA256 ?? "",
                phase = "diagnostic-capture-failed",
                exitCode = exitCode,
                exceptionType = error?.GetType().FullName ?? "",
                exceptionMessage = error?.Message ?? "",
            };
        }
    }

    internal static bool TryWrite(string runDirectory, in LoopClosureTerminalOutcome outcome)
    {
        string temporary = "";
        try
        {
            string directory = Run.Resolve(runDirectory) ?? Path.GetFullPath(runDirectory);
            if (!Directory.Exists(directory)) return false;
            byte[] bytes = RonSerializer.SerializeToUtf8(in outcome);
            string final = PathFor(directory);
            temporary = final + ".tmp";
            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, final, overwrite: true);
            temporary = "";
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporary.Length != 0)
            {
                try { File.Delete(temporary); }
                catch { }
            }
        }
    }

    internal static bool TryRead(string runDirectory, out LoopClosureTerminalOutcome? outcome)
    {
        outcome = null;
        try
        {
            string path = PathFor(runDirectory);
            byte[] bytes = File.ReadAllBytes(path);
            LoopClosureTerminalOutcome decoded = RonSerializer.Deserialize<LoopClosureTerminalOutcome>(bytes);
            if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in decoded))) return false;
            if (decoded.schemaVersion != CurrentSchemaVersion) return false;
            if (!string.Equals(decoded.runDirectory, Run.Resolve(runDirectory) ?? Path.GetFullPath(runDirectory), StringComparison.Ordinal)) return false;
            outcome = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryReadSealed(
        string runDirectory,
        LoopClosureArms expectedArm,
        out LoopClosureTerminalOutcome? outcome)
    {
        if (!TryCanonicalDirectory(runDirectory, out string directory))
        {
            outcome = null;
            return false;
        }
        if (!TryRead(runDirectory, out outcome) || outcome is null) return false;
        if (outcome.arm != expectedArm || string.IsNullOrWhiteSpace(outcome.authoritySHA256)) return false;
        try
        {
            RunAuthority authority = RunAuthority.LoadIdentity(directory);
            if (!string.Equals(authority.Digest, outcome.authoritySHA256, StringComparison.Ordinal)) return false;
        }
        catch (Exception) { return false; }
        return outcome.arm == expectedArm
            && outcome.status == LoopClosureRunStatuses.Sealed
            && outcome.cause == LoopClosureTerminalCauses.Completed;
    }

    private static bool TryCanonicalDirectory(string path, out string canonical)
    {
        canonical = Run.Resolve(path) ?? Path.GetFullPath(path);
        if (!Directory.Exists(canonical)) return false;
        for (DirectoryInfo? current = new DirectoryInfo(canonical); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    internal static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.GetFullPath(Path.Combine(".tmp", $"loop-closure-terminal-fixture-{Guid.NewGuid():N}"));
        string run = Path.Combine(root, "loop-closure-c0117011_live");
        Directory.CreateDirectory(root);
        try
        {
            string corpus = Path.Combine(root, "corpus.txt");
            File.WriteAllText(corpus, "alpha beta gamma\n");
            CortexConfig config = new()
            {
                Steps = 1,
                Seed = 0xA11CEUL,
                Curriculum = new CortexFlatPoolCurriculum
                {
                    Corpus = new CogitoCorpus { Path = corpus },
                    IntakeBatch = 1,
                    SeedSpans = 1,
                },
            };
            Run runObject = Run.Create(run);
            bool drove = new Cortex(config).Run(runObject) == 0;
            if (!drove)
            {
                output.WriteLine("  loop-closure terminal fixture · run=FAILED · FAIL");
                return false;
            }
            RunAuthority.WriteCompleted(runObject, Checkpoint.PeekConfig(run), Checkpoint.NextStep(run));
            RunAuthority authority = RunAuthority.LoadIdentity(run);
            LoopClosureTerminalOutcome outcome = Capture(
                LoopClosureArms.Live,
                LoopClosureRunStatuses.Sealed,
                LoopClosureTerminalCauses.Completed,
                run,
                authority.Digest,
                HomeostatPolicyBoundaryDomain.Instance);
            bool wrote = TryWrite(run, in outcome);
            bool sibling = string.Equals(Path.GetDirectoryName(PathFor(run)), Path.GetDirectoryName(run), StringComparison.Ordinal)
                && !File.Exists(Path.Combine(run, FileName));
            string shorthand = Path.GetRelativePath(Environment.CurrentDirectory, run);
            bool read = TryReadSealed(shorthand, LoopClosureArms.Live, out LoopClosureTerminalOutcome? restored)
                && restored is not null && restored.runDirectory == Path.GetFullPath(run);
            File.WriteAllText(Path.Combine(run, FileName), "stale in-arm terminal must be ignored");
            bool inArmIgnored = TryReadSealed(shorthand, LoopClosureArms.Live, out _);
            LoopClosureTerminalOutcome stale = outcome;
            stale.authoritySHA256 = new string('0', 64);
            TryWrite(run, in stale);
            bool staleRejected = !TryReadSealed(run, LoopClosureArms.Live, out _);
            TryWrite(run, in outcome);
            string alias = Path.Combine(root, "arm-alias");
            bool aliasRejected;
            try
            {
                Directory.CreateSymbolicLink(alias, run);
                aliasRejected = !TryReadSealed(alias, LoopClosureArms.Live, out _);
            }
            catch (IOException) { aliasRejected = false; }
            finally
            {
                if (Directory.Exists(alias)) Directory.Delete(alias);
            }
            output.WriteLine($"  loop-closure terminal fixture · external={(wrote && sibling ? "sibling" : "INSIDE")}" +
                $" · shorthand={(read ? "accepted" : "REJECTED")} · stale={(staleRejected ? "rejected" : "ACCEPTED")}" +
                $" · alias={(aliasRejected ? "rejected" : "ACCEPTED")} · in-arm={(inArmIgnored ? "ignored" : "READ")} · {(wrote && sibling && read && staleRejected && aliasRejected && inArmIgnored ? "PASS" : "FAIL")}");
            return wrote && sibling && read && staleRejected && aliasRejected && inArmIgnored;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public string RenderLine()
    {
        StringBuilder line = new("gate loop-closure");
        line.Append(" · arm=").Append(arm)
            .Append(" · status=").Append(status)
            .Append(" · cause=").Append(cause)
            .Append(" · run=").Append(runDirectory)
            .Append(" · phase=").Append(phase)
            .Append(" · observed-step=").Append(observedStep.ToString(CultureInfo.InvariantCulture))
            .Append(" · last-completed-step=").Append(lastCompletedStep.ToString(CultureInfo.InvariantCulture))
            .Append(" · durable-next-step=").Append(durableNextStep.ToString(CultureInfo.InvariantCulture));
        if (runDirectory.Length != 0)
            line.Append(" · terminal=").Append(PathFor(runDirectory));
        if (lastEventKind.Length != 0)
            line.Append(" · last-event=").Append(lastEventKind).Append('/').Append(lastEventSource);
        if (exitCode >= 0) line.Append(" · exit=").Append(exitCode.ToString(CultureInfo.InvariantCulture));
        if (exceptionType.Length != 0)
            line.Append(" · exception=").Append(exceptionType).Append(": ").Append(exceptionMessage);
        return line.ToString();
    }

    private static string ResolvePhase(
        LoopClosureTerminalCauses cause,
        int observedStep,
        int lastCompletedStep,
        string lastEventKind,
        string lastEventSource)
    {
        if (cause == LoopClosureTerminalCauses.Completed) return "completed";
        if (observedStep >= 0 && lastEventKind.Length != 0)
            return $"step-{observedStep}:{lastEventKind}/{lastEventSource}";
        if (observedStep > lastCompletedStep && observedStep >= 0)
            return $"step-{observedStep}:incomplete";
        return cause.ToString();
    }

    private static int ReadDurableNextStep(string runDirectory)
    {
        try
        {
            return File.Exists(Path.Combine(Path.GetFullPath(runDirectory), Checkpoint.FileName))
                ? Checkpoint.PeekNextStep(runDirectory)
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static int ReadLastStep(string runDirectory, string file)
    {
        try
        {
            string path = Path.Combine(Path.GetFullPath(runDirectory), file);
            if (!File.Exists(path)) return -1;
            int last = -1;
            foreach (string line in File.ReadLines(path))
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string token = line[..tab].TrimStart('\uFEFF');
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)) last = step;
            }
            return last;
        }
        catch
        {
            return -1;
        }
    }

    private static void ReadLastJournalEvent(string runDirectory, out string kind, out string source)
    {
        kind = "";
        source = "";
        try
        {
            string path = Path.Combine(Path.GetFullPath(runDirectory), "journal.log");
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadLines(path))
            {
                string[] fields = line.Split('\t');
                if (fields.Length >= 4)
                {
                    kind = fields[1];
                    source = fields[3];
                }
            }
        }
        catch
        {
            kind = "";
            source = "";
        }
    }
}
