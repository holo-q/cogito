namespace Cogito;

using System.Security.Cryptography;
using System.Text;

/// G3's kill-line — only a live world may close the loop.
///
/// One registration, three arms, matched fuel. The registration freezes the world bytes, the
/// task and its hidden oracle, the seed, the horizon and the fuel, so the arms differ in exactly
/// one respect: what comes back through the mouth. Then each sealed run is adjudicated against
/// the same contract and the three findings are laid side by side.
///
///   tools-live      the world answers. If the loop closes anywhere, it closes here.
///   tools-blocked   the world answers nothing. Whatever survives came from the prior, not the
///                   world, and must not be admitted as closure.
///   tools-shuffled  the world answers the PREVIOUS question. The bytes are real repository
///                   bytes with a deranged binding — the sharper null, because an organism that
///                   scores the same under a derangement was never routing on what it found.
///
/// The assay reports what it MEASURES, including the case where the arms are indistinguishable.
/// A shuffled arm that closes the loop is a real finding about the chain's discriminating power,
/// and tuning it away would be fraud; an arm that refuses for a structural reason unrelated to
/// its world is an infrastructure loss and is never banked as learner-side behavior.
internal static class RepositoryLoopClosureNull
{
    private const string PlanID = "repository-loop-closure-g3";
    private const string TaskID = "g3-locate-intake-affirm-gate";
    private const string Query = "where is the intake affirm gate measured";
    private const string Glob = "*.cs";
    /// Selections the chain needs AFTER the seeded search queue drains: an occurrence check per prediction species
    /// the composition stage might need, the composition itself, and the answer that closes the loop.
    /// Named rather than folded into a constant because it is the depth of the evidence chain, and
    /// the chain is the thing under test.
    private const int StagesBeyondSearch = 12;

    /// The horizon every arm runs to — COMPUTED, never a constant. A SearchTerm outscores every other
    /// candidate while unattempted, and the query seeds a closed queue of them, so a horizon below
    /// that depth expires before the organism can verify anything. Three runs that all expire in the
    /// search queue are separated by their allowance, not by their world: the assay would report a
    /// shared clause and mean nothing by it. Measured at horizon 8 against a 15-deep queue: zero
    /// occurrence checks in every arm. At 20: eight confirmed occurrences.
    internal static int DefaultSteps => RepositoryCandidateFrontier.SeededQueueDepth(Query) + StagesBeyondSearch;
    /// Matched fuel: the same trial-step budget offered to every arm. It is set well above what
    /// a short horizon can spend so the arms are separated by their world, never by their allowance.
    private const long OfferedFuel = 4096;
    private const ulong Seed = 0xC07E5EEDUL;
    private const string LineageNullDomain = "repository-native-loop-closure";
    private const string LineageNullAlgorithm = "shuffled-predecessor";

    private static readonly RepositoryToolArms[] Arms =
    [
        RepositoryToolArms.ToolsLive,
        RepositoryToolArms.ToolsBlocked,
        RepositoryToolArms.ToolsShuffled,
    ];

    internal static bool Verify(TextWriter output, int steps)
    {
        if (steps <= 0) throw new ArgumentOutOfRangeException(nameof(steps), steps, "the loop-closure assay horizon must be positive");
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            output.WriteLine("    repository-loop-closure · FAIL — the repository root is unreachable; an absent world is an infrastructure loss, not a null");
            return false;
        }

        string frozenRoot;
        RepositoryNativeRegistrationArtifact artifact;
        try
        {
            frozenRoot = FreezeWorld(root);
            artifact = MintRegistration(frozenRoot, steps);
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or DirectoryNotFoundException)
        {
            output.WriteLine($"    repository-loop-closure · FAIL — registration could not be minted: {failure.Message}");
            return false;
        }

        RepositoryLoopClosureRegistration registration = artifact.Registration;
        output.WriteLine($"    registration · plan={registration.PlanID} task={registration.TaskID} species={registration.Task.Species}");
        output.WriteLine($"                 · registration={registration.RegistrationSHA256[..16]} world={registration.WorldContentSHA256[..16]}"
                       + $" seed={registration.Seed:X} horizon={registration.Horizon} fuel={registration.OfferedFuel}");

        Dictionary<RepositoryToolArms, RepositoryLoopClosureFinding> findings = new();
        foreach (RepositoryToolArms arm in Arms)
        {
            RepositoryLoopClosureFinding finding;
            try { finding = RunAndAssayArm(frozenRoot, artifact, arm, steps); }
            catch (Exception failure) when (failure is InvalidDataException or IOException or InvalidOperationException)
            {
                output.WriteLine($"    {RepositoryToolArmNames.Render(arm),-15} · FAIL — the arm did not produce sealed evidence: {failure.Message}");
                return false;
            }
            findings[arm] = finding;
        }

        output.WriteLine("    arm             evidence census                                                          clause");
        foreach (RepositoryToolArms arm in Arms)
            output.WriteLine(RepositoryLoopClosureAdjudicator.RenderRow(RepositoryToolArmNames.Render(arm), findings[arm]));
        // Every refusal prints its own detail. A shared clause across arms can still hide three
        // different offending rows, and the row is what a reader has to act on.
        foreach (RepositoryToolArms arm in Arms)
            if (findings[arm].Refusal is { } refusal)
                output.WriteLine($"      {RepositoryToolArmNames.Render(arm),-15} · {refusal.Detail}");

        return ReportKillLine(output, findings);
    }

    /// The kill-line: closure is admissible only from the live arm. Blocked and shuffled must
    /// fail, and they must fail FOR THEIR OWN REASON — if all three stop at the same clause the
    /// arms were never separated and the line is undemonstrated, which is reported as such.
    private static bool ReportKillLine(
        TextWriter output,
        IReadOnlyDictionary<RepositoryToolArms, RepositoryLoopClosureFinding> findings)
    {
        RepositoryLoopClosureFinding live = findings[RepositoryToolArms.ToolsLive];
        RepositoryLoopClosureFinding blocked = findings[RepositoryToolArms.ToolsBlocked];
        RepositoryLoopClosureFinding shuffled = findings[RepositoryToolArms.ToolsShuffled];

        output.WriteLine("    kill-line · only tools-live may close the loop");
        if (blocked.RendersClosureCertificate)
            output.WriteLine("      tools-blocked RENDERED a closure certificate with a mouth that returned nothing —"
                           + " the closure it reports did not come from the world");
        if (shuffled.RendersClosureCertificate)
            output.WriteLine("      tools-shuffled RENDERED a closure certificate on deranged evidence —"
                           + " the chain does not discriminate what the look was bound to");

        bool distinctClauses = live.Clause != blocked.Clause || live.Clause != shuffled.Clause;
        if (!distinctClauses)
        {
            output.WriteLine($"      every arm stopped at {RepositoryLoopClosureAdjudicationRefusal.ClauseName(live.Clause)},"
                           + " so the arms were never separated by their world");
            output.WriteLine($"      {live.Refusal?.Detail ?? "no refusal detail"}");
            output.WriteLine("    repository-loop-closure · UNDEMONSTRATED — a shared structural clause, not a null result;"
                           + " this is an infrastructure gap and is not banked");
            return false;
        }

        bool killed = live.RendersClosureCertificate && !blocked.RendersClosureCertificate && !shuffled.RendersClosureCertificate;
        output.WriteLine($"    repository-loop-closure · {(killed ? "PASS" : "FAIL")}"
                       + $" · live={ClauseOrCertificate(live)} blocked={ClauseOrCertificate(blocked)} shuffled={ClauseOrCertificate(shuffled)}");
        return killed;
    }

    private static string ClauseOrCertificate(RepositoryLoopClosureFinding finding)
        => finding.RendersClosureCertificate
            // Frozen artifact token BirthCertificate; identifier-side name is ClosureCertificate.
            ? "BirthCertificate"
            : RepositoryLoopClosureAdjudicationRefusal.ClauseName(finding.Clause);

    /// One arm: drive the registered native run, then read back ONLY its sealed terminal
    /// document. Nothing from the live runtime crosses into adjudication.
    private static RepositoryLoopClosureFinding RunAndAssayArm(
        string root,
        RepositoryNativeRegistrationArtifact artifact,
        RepositoryToolArms arm,
        int steps)
    {
        int exit = RepositoryNative.Run(root, Query, steps, Glob, artifact.Registration, arm, out Run? destination);
        if (exit != 0 || destination is null)
            throw new InvalidDataException($"native repository arm {RepositoryToolArmNames.Render(arm)} exited {exit}");
        string terminalPath = destination.PathOf(RepositoryNativeTerminalEvidence.FileName);
        if (!File.Exists(terminalPath))
            throw new InvalidDataException($"native repository arm {RepositoryToolArmNames.Render(arm)} sealed no terminal evidence at {terminalPath}");
        RepositoryNativeTerminalDecoded decoded = RepositoryNativeTerminalEvidence.Decode(
            File.ReadAllBytes(terminalPath), artifact.MountedAuthority);
        RepositoryLoopClosureEvidenceCensus census = RepositoryLoopClosureEvidenceCensus.Measure(
            decoded.Tape, decoded.Journal, decoded.World, decoded.Access, decoded.Frontier, decoded.Pattern,
            artifact.Registration.Task.Species);
        RepositoryLoopClosureAdjudicationInput input;
        try { input = BuildAdjudicationInput(artifact, decoded); }
        catch (Exception failure) when (failure is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return RepositoryLoopClosureAdjudicator.RefuseUnassembled(census,
                $"the sealed input could not be assembled: {failure.Message}", failure);
        }
        return RepositoryLoopClosureAdjudicator.Assay(input);
    }

    /// Assemble the sealed adjudication input from the registration bundle and the decoded
    /// terminal document. Every digest here is READ from the object that owns it; none is
    /// restated, so the input's own validators can still refute the assembly.
    private static RepositoryLoopClosureAdjudicationInput BuildAdjudicationInput(
        RepositoryNativeRegistrationArtifact artifact,
        RepositoryNativeTerminalDecoded decoded)
    {
        RepositoryLoopClosureRegistration registration = artifact.Registration;
        RepositoryLoopClosureAuthoritySnapshot authority = artifact.CreateAuthoritySnapshot();
        RepositoryLoopClosureRuntimeAuthorityCorroboration runtimeAuthority = new(
            authority,
            decoded.World,
            new RepositoryLoopClosureToolAuthorityCorroboration(),
            new RepositoryLoopClosurePolicyAuthorityCorroboration(),
            RepositoryLoopClosureCandidateSchemaAuthorityCorroboration.CreateDefault(),
            RepositoryLoopClosureInitialStateAuthorityCorroboration.Create(registration.Seed, registration.Horizon),
            RepositoryLoopClosureFuelAuthorityCorroboration.Create(registration.OfferedFuel));
        RepositoryLoopClosureSealedEvidenceAuthority evidenceAuthority = new(
            registration.RegistrationSHA256,
            authority.AuthoritySHA256,
            decoded.Tape.TapeSHA256,
            decoded.Tape.PreSealTapeSHA256,
            LoopLineageAuthority.Capture(decoded.Tape.LineageEdges).Digest,
            decoded.Journal.JournalSHA256,
            decoded.Journal.RowAuthoritiesSHA256,
            decoded.World.ContentSHA256,
            decoded.World.SnapshotSHA256,
            decoded.Access.AccessSHA256,
            decoded.Frontier.FrontierSHA256,
            decoded.Frontier.RuntimeAuthoritySHA256,
            decoded.Pattern.PatternSHA256,
            decoded.Pattern.PendingAuthoritySHA256,
            decoded.Seal);
        return new RepositoryLoopClosureAdjudicationInput(
            decoded.Document.runID, decoded.World, decoded.Tape, decoded.Journal, authority,
            decoded.Access, decoded.Frontier, decoded.Pattern, registration.Task,
            runtimeAuthority, evidenceAuthority, registration);
    }

    /// The registration is minted from the repository as it stands right now. The oracle is
    /// source-backed: a Locate task whose expected result IS the path of a file that exists in
    /// the frozen world, so the answer is decided by the world rather than by the registration.
    private static RepositoryNativeRegistrationArtifact MintRegistration(string frozenRoot, int steps)
    {
        Tool.RepositoryWorldSnapshot world = new(frozenRoot, Glob);
        string querySHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Query + "\n")));
        RepositoryNativeSourceAuthorityCorroboration sourceAuthority = RepositoryNativeSourceAuthorityCorroboration.Create(
            frozenRoot, Glob, Query, querySHA256, world);

        Tool.RepositoryFile[] targets = world.Files.Where(file => file.Path.Value == OraclePath).ToArray();
        if (targets.Length != 1)
            throw new InvalidDataException($"the G3 oracle target '{OraclePath}' resolves to {targets.Length} files in the frozen world");
        Tool.RepositoryFile target = targets[0];
        RepositoryLoopClosureTaskOracle oracle = new(
            RepositoryLoopClosureTaskOracleModes.SourceResult,
            new RepositoryLoopClosureExpectedSource(target.Path.Value, target.Bytes, target.SHA256),
            new RepositoryLoopClosureExpectedResult(RepositoryLoopClosureResultSpecies.Path,
                Encoding.UTF8.GetBytes(target.Path.Value)));
        RepositoryLoopClosureTaskSpec task = new(TaskID, RepositoryLoopClosureTaskSpecies.Locate, Query, oracle);

        RepositoryNativeRegistrationArtifact artifact = RepositoryNativeRegistrationArtifact.Mint(new(
            PlanID, sourceAuthority, world, task,
            new RepositoryLoopClosureToolAuthorityCorroboration(),
            new RepositoryLoopClosurePolicyAuthorityCorroboration(),
            RepositoryLoopClosureCandidateSchemaAuthorityCorroboration.CreateDefault(),
            RepositoryLoopClosureInitialStateAuthorityCorroboration.Create(Seed, steps),
            RepositoryLoopClosureFuelAuthorityCorroboration.Create(OfferedFuel),
            Seed, steps, OfferedFuel, 0, 0,
            RepositoryLoopClosureLineageNullSpec.Create(LineageNullDomain, LineageNullAlgorithm)));

        // The authority bundle must exist on disk before the run: a registration whose bundle
        // was never materialized cannot produce the authority snapshot adjudication joins against.
        artifact.WriteBundle(Path.Combine(Run.RunsRoot(), "repository-loop-closure-registration"));
        return artifact;
    }

    /// Copy the live source tree into a frozen root before anything is registered. The working
    /// tree is SHARED — a peer landing one file between the registration and the third arm changes
    /// the world digest under the assay, which is how a genuine kill-line result gets destroyed by
    /// an unrelated commit. Freezing also makes the three arms re-runnable against the same world.
    private static string FreezeWorld(string liveRoot)
    {
        Tool.RepositoryWorldSnapshot live = new(liveRoot, Glob);
        string frozenRoot = Path.Combine(Run.RunsRoot(), "repository-loop-closure-world");
        if (Directory.Exists(frozenRoot)) Directory.Delete(frozenRoot, recursive: true);
        Directory.CreateDirectory(frozenRoot);
        foreach (string relativePath in live.Paths)
        {
            string destination = Path.Combine(frozenRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, live.CaptureFile(new Tool.RepositoryPath(relativePath)).Content.ToArray());
        }
        Tool.RepositoryWorldSnapshot frozen = new(frozenRoot, Glob);
        if (frozen.WorldSHA256 != live.WorldSHA256)
            throw new InvalidDataException($"the frozen world diverges from the live tree it was copied from: '{frozen.WorldSHA256}' ({frozen.FileCount} files) vs '{live.WorldSHA256}' ({live.FileCount} files) — the tree moved during the copy");
        return frozenRoot;
    }

    /// The oracle's target is a file whose existence is the question the query asks about, so a
    /// Confirmed outcome means the crawler located the seam rather than any file at all.
    private const string OraclePath = "src/Cogito/Agent/RepositoryIntakeNull.cs";

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
