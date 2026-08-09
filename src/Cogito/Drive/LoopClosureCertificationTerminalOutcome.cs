namespace Cogito;

using System.Security.Cryptography;
using Ronmamon;

public enum LoopClosureCertificationStatuses : byte
{
    Failed,
    Completed,
}

public enum LoopClosureCertificationCauses : byte
{
    PreflightException,
    AdjudicatorException,
    AdjudicatorRejected,
    CertificationException,
    Completed,
}

/// Durable terminal state for the read-only `gate certify` verb. This is deliberately
/// a different artifact from both LoopClosureReport and ClosureCertificate: a verifier
/// failure must be visible without being mistaken for a report or a minted certificate.
[RonObject]
public partial class LoopClosureCertificationTerminalOutcome
{
    public const int CurrentSchemaVersion = 2;
    public const string FileName = "loop-closure-certification-terminal.ron";
    public const string ArtifactName = "LoopClosureCertificationTerminalOutcome";

    public int schemaVersion = CurrentSchemaVersion;
    public string artifactName = ArtifactName;
    public LoopClosureCertificationStatuses status;
    public LoopClosureCertificationCauses cause;
    public string phase = "";
    public string registrationDigest = "";
    public string registrationPath = "";
    public string livePath = "";
    public string controlPath = "";
    public string reportPath = "";
    public string terminalPath = "";
    public string exceptionType = "";
    public string exceptionMessage = "";
    public string expectedRegistrationDigest = "";
    public string observedRegistrationDigest = "";
    public string expectedRegistrationBytesSHA256 = "";
    public string observedRegistrationBytesSHA256 = "";
    public string expectedLiveAuthorityDigest = "";
    public string observedLiveAuthorityDigest = "";
    public string expectedControlAuthorityDigest = "";
    public string observedControlAuthorityDigest = "";
    public string expectedReportDigest = "";
    public string observedReportDigest = "";
    public string expectedReportBytesSHA256 = "";
    public string observedReportBytesSHA256 = "";

    public static string PathAdjacentToReport(string reportPath, string livePath = "", string controlPath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        string report = Path.GetFullPath(reportPath);
        string directory = Path.GetDirectoryName(report) ?? ".";
        string candidate = Path.Combine(directory, FileName);
        if (string.Equals(candidate, report, StringComparison.OrdinalIgnoreCase))
            candidate = report + ".terminal.ron";

        // A malformed operator path must not move the terminal receipt into an
        // immutable arm. The normal command path is adjacent to the report; this
        // fallback preserves the outside-both-arms invariant for hostile paths.
        if (IsInside(candidate, livePath) || IsInside(candidate, controlPath))
        {
            string? parent = Path.GetDirectoryName(CanonicalPath(livePath));
            if (!string.IsNullOrWhiteSpace(parent)) candidate = Path.Combine(parent, FileName);
            if (IsInside(candidate, livePath) || IsInside(candidate, controlPath))
                candidate = Path.Combine(directory, FileName + ".outside.ron");
        }
        return candidate;
    }

    public static LoopClosureCertificationTerminalOutcome Capture(
        LoopClosureCertificationStatuses status,
        LoopClosureCertificationCauses cause,
        string phase,
        string registrationPath,
        string livePath,
        string controlPath,
        string reportPath,
        string terminalPath,
        IPolicyBoundaryDomain domain,
        LoopClosureRegistration? registration = null,
        LoopClosureReport? report = null,
        Exception? error = null)
    {
        string registrationFile = FullPath(registrationPath);
        string live = FullPath(livePath);
        string control = FullPath(controlPath);
        string reportFile = FullPath(reportPath);
        string terminal = FullPath(terminalPath);
        string expectedRegistration = registration?.Digest ?? "";
        string observedRegistration = registration?.Digest ?? "";
        string expectedRegistrationBytes = DigestBytes(registration?.Encode());
        string observedRegistrationBytes = DigestFile(registrationFile);
        string expectedLive = report?.Live.AuthoritySHA256 ?? "";
        string expectedControl = report?.Control.AuthoritySHA256 ?? "";
        string observedLive = DigestAuthority(live);
        string observedControl = DigestAuthority(control);
        string expectedReport = report?.Digest ?? "";
        string observedReport = ReadReportDigest(reportFile, domain);
        string expectedReportBytes = DigestBytes(report?.Encode());
        string observedReportBytes = DigestFile(reportFile);
        return new()
        {
            status = status,
            cause = cause,
            phase = phase ?? "",
            registrationDigest = expectedRegistration,
            registrationPath = registrationFile,
            livePath = live,
            controlPath = control,
            reportPath = reportFile,
            terminalPath = terminal,
            exceptionType = error?.GetType().FullName ?? "",
            exceptionMessage = error?.Message ?? "",
            expectedRegistrationDigest = expectedRegistration,
            observedRegistrationDigest = observedRegistration,
            expectedRegistrationBytesSHA256 = expectedRegistrationBytes,
            observedRegistrationBytesSHA256 = observedRegistrationBytes,
            expectedLiveAuthorityDigest = expectedLive,
            observedLiveAuthorityDigest = observedLive,
            expectedControlAuthorityDigest = expectedControl,
            observedControlAuthorityDigest = observedControl,
            expectedReportDigest = expectedReport,
            observedReportDigest = observedReport,
            expectedReportBytesSHA256 = expectedReportBytes,
            observedReportBytesSHA256 = observedReportBytes,
        };
    }

    public static bool TryWrite(string terminalPath, in LoopClosureCertificationTerminalOutcome outcome)
    {
        string temporary = "";
        try
        {
            string destination = Path.GetFullPath(terminalPath);
            if (string.Equals(destination, Path.GetFullPath(outcome.reportPath), StringComparison.OrdinalIgnoreCase)) return false;
            if (IsInside(destination, outcome.livePath) || IsInside(destination, outcome.controlPath)) return false;
            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            byte[] bytes = RonSerializer.SerializeToUtf8(in outcome);
            temporary = destination + ".tmp";
            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
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

    public static bool TryRead(string terminalPath, out LoopClosureCertificationTerminalOutcome? outcome)
    {
        outcome = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(terminalPath));
            LoopClosureCertificationTerminalOutcome decoded = RonSerializer.Deserialize<LoopClosureCertificationTerminalOutcome>(bytes);
            decoded.Validate();
            if (!bytes.AsSpan().SequenceEqual(RonSerializer.SerializeToUtf8(in decoded))) return false;
            outcome = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Validate()
    {
        if (schemaVersion != CurrentSchemaVersion || artifactName != ArtifactName)
            throw new InvalidDataException("loop-closure certification terminal artifact identity is unsupported");
        if (!Enum.IsDefined(status) || !Enum.IsDefined(cause) || string.IsNullOrWhiteSpace(phase)
            || string.IsNullOrWhiteSpace(registrationPath) || string.IsNullOrWhiteSpace(livePath)
            || string.IsNullOrWhiteSpace(controlPath) || string.IsNullOrWhiteSpace(reportPath)
            || string.IsNullOrWhiteSpace(terminalPath))
            throw new InvalidDataException("loop-closure certification terminal artifact is incomplete");
        if (status == LoopClosureCertificationStatuses.Completed && cause != LoopClosureCertificationCauses.Completed)
            throw new InvalidDataException("completed loop-closure certification terminal has a non-completed cause");
        if (cause == LoopClosureCertificationCauses.Completed && status != LoopClosureCertificationStatuses.Completed)
            throw new InvalidDataException("completed loop-closure certification cause has a non-completed status");
        if (string.Equals(reportPath, terminalPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("loop-closure certification terminal would overwrite its report");
    }

    public string RenderLine()
        => $"gate certify · status={status} · cause={cause} · phase={phase} · terminal={terminalPath} · report={reportPath}"
            + (exceptionType.Length == 0 ? "" : $" · exception={exceptionType}: {exceptionMessage}");

    internal static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.GetFullPath(Path.Combine(".tmp", $"loop-closure-certification-terminal-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(root);
        try
        {
            string live = Path.Combine(root, "live");
            string control = Path.Combine(root, "control");
            Directory.CreateDirectory(live);
            Directory.CreateDirectory(control);
            string report = Path.Combine(root, "report.ron");
            string registration = Path.Combine(root, "registration.ron");
            File.WriteAllText(report, "report bytes must survive terminal writes");
            File.WriteAllText(registration, "registration bytes");
            byte[] reportBefore = File.ReadAllBytes(report);
            string terminal = PathAdjacentToReport(report, live, control);
            Exception error = new InvalidDataException("fixture preflight");
            LoopClosureCertificationTerminalOutcome failed = Capture(
                LoopClosureCertificationStatuses.Failed,
                LoopClosureCertificationCauses.PreflightException,
                "preflight", registration, live, control, report, terminal, error: error,
                domain: HomeostatPolicyBoundaryDomain.Instance);
            bool wroteFailure = TryWrite(terminal, in failed);
            bool readFailure = TryRead(terminal, out LoopClosureCertificationTerminalOutcome? restoredFailure)
                && restoredFailure is not null
                && restoredFailure.status == LoopClosureCertificationStatuses.Failed
                && restoredFailure.cause == LoopClosureCertificationCauses.PreflightException
                && restoredFailure.phase == "preflight"
                && restoredFailure.exceptionType == typeof(InvalidDataException).FullName
                && restoredFailure.observedRegistrationBytesSHA256.Length == 64;

            LoopClosureCertificationTerminalOutcome completed = Capture(
                LoopClosureCertificationStatuses.Completed,
                LoopClosureCertificationCauses.Completed,
                "completed", registration, live, control, report, terminal, domain: HomeostatPolicyBoundaryDomain.Instance);
            bool wroteCompleted = TryWrite(terminal, in completed);
            bool readCompleted = TryRead(terminal, out LoopClosureCertificationTerminalOutcome? restoredCompleted)
                && restoredCompleted is not null
                && restoredCompleted.status == LoopClosureCertificationStatuses.Completed
                && restoredCompleted.cause == LoopClosureCertificationCauses.Completed;
            bool reportUntouched = reportBefore.AsSpan().SequenceEqual(File.ReadAllBytes(report));
            bool outsideArms = !IsInside(terminal, live) && !IsInside(terminal, control);
            string insideReport = Path.Combine(live, "inside-report.ron");
            string insideTerminal = PathAdjacentToReport(insideReport, live, control);
            bool insideReportRerouted = !IsInside(insideTerminal, live) && !IsInside(insideTerminal, control);
            bool pass = wroteFailure && readFailure && wroteCompleted && readCompleted && reportUntouched && outsideArms && insideReportRerouted
                && !string.Equals(failed.artifactName, "LoopClosureReport", StringComparison.Ordinal)
                // Frozen artifact token BirthCertificate; identifier-side name is ClosureCertificate.
                && !string.Equals(failed.artifactName, "BirthCertificate", StringComparison.Ordinal);
            output.WriteLine($"  loop-closure certification terminal fixture · failure={(readFailure ? "typed" : "BROKEN")} · completed={(readCompleted ? "typed" : "BROKEN")} · report={(reportUntouched ? "untouched" : "MUTATED")} · outside-arms={(outsideArms ? "yes" : "NO")} · inside-report={(insideReportRerouted ? "rerouted" : "INSIDE")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static bool IsInside(string candidate, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        string root = CanonicalPath(directory);
        string full = Path.GetFullPath(candidate);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string FullPath(string path)
        => string.IsNullOrWhiteSpace(path) ? "" : CanonicalPath(path);

    private static string CanonicalPath(string path)
        => Run.Resolve(path) ?? Path.GetFullPath(path);

    private static string DigestFile(string path)
    {
        try { return File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : ""; }
        catch { return ""; }
    }

    private static string DigestBytes(byte[]? bytes)
        => bytes is null ? "" : Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string ReadReportDigest(string path, IPolicyBoundaryDomain domain)
    {
        try { return File.Exists(path) ? LoopClosureReport.Load(path, domain).Digest : ""; }
        catch { return ""; }
    }

    private static string DigestAuthority(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? "" : RunAuthority.LoadIdentity(path).Digest; }
        catch { return ""; }
    }
}
