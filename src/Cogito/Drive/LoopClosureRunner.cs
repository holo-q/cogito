namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

public enum LoopClosureArms : byte { Live, Control }
public enum LoopClosureRunStatuses : byte { NotStarted, Sealed, Invalid }

public readonly record struct LoopClosureRunRequest(
    LoopClosureRegistration Registration,
    LoopClosureArms Arm,
    string CorpusPath,
    string OutputDirectory,
    string RegistrationPath = "")
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Registration);
        Registration.Validate();
        if (string.IsNullOrWhiteSpace(CorpusPath) || string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("loop-closure run request requires corpus and output paths");
        if (!Enum.IsDefined(Arm)) throw new ArgumentOutOfRangeException(nameof(Arm));
        if (!string.IsNullOrWhiteSpace(RegistrationPath)) Registration.AssertPersistedBytes(RegistrationPath);
    }
}

public readonly record struct LoopClosureRunResult(
    LoopClosureArms Arm,
    LoopClosureRunStatuses Status,
    string RunDirectory,
    string AuthoritySHA256,
    string Error)
{
    public LoopClosureTerminalOutcome? Terminal { get; init; }

    public static LoopClosureRunResult NotStarted(LoopClosureArms arm, string detail)
        => new(arm, LoopClosureRunStatuses.NotStarted, "", "", detail);

    public string RenderLine()
        => Terminal?.RenderLine()
            ?? $"gate loop-closure · arm={Arm} · status={Status} · run={RunDirectory} · error={Error}";
}

/// Final runner seam. The mechanism wave supplies the ordinary Cortex turnstiles;
/// this schema wave deliberately does not invent a second execution path.
public interface ILoopClosureRunner
{
    LoopClosureRunResult Run(LoopClosureRunRequest request, IPolicyBoundaryDomain domain);
}

public interface ILoopClosureLineageRunner
{
    TapeEventID EmitLineageEdge(Tape tape, Journal journal, int step, in LoopLineageEdgeReceipt receipt);
}

public sealed class LoopClosureRunner : ILoopClosureRunner, ILoopClosureLineageRunner
{
    public LoopClosureRunResult Run(LoopClosureRunRequest request, IPolicyBoundaryDomain domain)
    {
        request.Validate();
        ArgumentNullException.ThrowIfNull(domain);
        domain.ArmTopology.Validate();
        if (!string.IsNullOrWhiteSpace(request.RegistrationPath)) request.Registration.AssertPersistedBytes(request.RegistrationPath);
        request.Registration.ValidateFrozenAuthority(request.CorpusPath, request.RegistrationPath, domain);
        string destination = Path.GetFullPath(request.OutputDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"loop-closure arm destination already exists: {destination}");

        // Pre-arm disk budget (R18 prevention): refuse before a Cortex destination is created when the
        // mount cannot cover the frozen horizon's footprint. R18 died ENOSPC at step 498 with no guard.
        LoopClosureDiskBudget.ForRegistration(request.Registration).RequireFreeSpace(destination, $"arm={request.Arm}");

        Cortex cortex = CreateArm(request.Registration, request.Arm, request.CorpusPath, domain);
        CortexRunConfig expected = cortex.Config.ToRunConfig(null);
        string expectedPersisted = Cortex.PersistedConfigDigest(expected);
        string registeredPersisted = request.Arm == LoopClosureArms.Live
            ? request.Registration.LiveConfigSHA256 : request.Registration.ControlConfigSHA256;
        if (!string.Equals(expectedPersisted, registeredPersisted, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure arm config differs from frozen registration; refusing to arm");
        if (!string.Equals(Cortex.ArmNeutralPersistedConfigDigest(expected), request.Registration.ArmNeutralConfigSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure arm-neutral config differs from frozen registration; refusing to arm");

        global::Cogito.Run run = global::Cogito.Run.Create(destination);
        try
        {
            request.Registration.WriteArmAuthority(run);
            int exit;
            try
            {
                exit = cortex.Run(run);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return Finish(request, domain, run.Dir, LoopClosureRunStatuses.Invalid, LoopClosureTerminalCauses.CortexException,
                    "", ex.Message, -1, ex);
            }
            if (exit != 0)
                return Finish(request, domain, run.Dir, LoopClosureRunStatuses.Invalid, LoopClosureTerminalCauses.CortexExit,
                    "", $"Cortex exited with {exit}", exit);
            RunAuthority authority;
            try
            {
                VerifySealedArm(request.Registration, request.Arm, run.Dir);
                authority = RunAuthority.LoadIdentity(run.Dir);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return Finish(request, domain, run.Dir, LoopClosureRunStatuses.Invalid, LoopClosureTerminalCauses.RunnerFailure,
                    "", ex.Message, -1, ex);
            }
            return Finish(request, domain, run.Dir, LoopClosureRunStatuses.Sealed, LoopClosureTerminalCauses.Completed,
                authority.Digest, "");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Finish(request, domain, run.Dir, LoopClosureRunStatuses.Invalid, LoopClosureTerminalCauses.RunnerFailure,
                "", ex.Message, -1, ex);
        }
    }

    private static LoopClosureRunResult Finish(
        LoopClosureRunRequest request,
        IPolicyBoundaryDomain domain,
        string runDirectory,
        LoopClosureRunStatuses status,
        LoopClosureTerminalCauses cause,
        string authoritySHA256,
        string error,
        int exitCode = -1,
        Exception? exception = null)
    {
        LoopClosureTerminalOutcome outcome = LoopClosureTerminalOutcome.Capture(
            request.Arm, status, cause, runDirectory, authoritySHA256, domain, exitCode, exception);
        LoopClosureTerminalOutcome.TryWrite(runDirectory, in outcome);
        return new(request.Arm, status, runDirectory, authoritySHA256, error) { Terminal = outcome };
    }

    public TapeEventID EmitLineageEdge(Tape tape, Journal journal, int step, in LoopLineageEdgeReceipt receipt)
        => TapePacketCreator.AppendLoopLineageEdge(tape, journal, step, in receipt);

    internal static Cortex CreateArm(LoopClosureRegistration registration, LoopClosureArms arm, string corpus, IPolicyBoundaryDomain domain)
        => CreateArm(registration.Seed, registration.WorldSHA256, arm, corpus, LoopClosureRegistration.RegisteredHorizon, domain);

    internal static Cortex CreateArm(ulong seed, string worldSHA256, LoopClosureArms arm, string corpus, IPolicyBoundaryDomain domain)
        => CreateArm(seed, worldSHA256, arm, corpus, LoopClosureRegistration.RegisteredHorizon, domain);

    private static Cortex CreateArm(ulong seed, string worldSHA256, LoopClosureArms arm, string corpus, int horizon, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        bool control = arm == LoopClosureArms.Control;
        CortexEmlCurriculum curriculum = new()
        {
            Corpus = new CogitoCorpus { Path = Path.GetFullPath(corpus), ExpectedWorldSHA256 = worldSHA256 },
            ProcessCatalog = control ? domain.ArmTopology.ControlProcessCatalog : domain.ArmTopology.LiveProcessCatalog,
            Rung0 = control ? domain.ArmTopology.ControlRung0 : domain.ArmTopology.LiveRung0,
            Deliberation = control ? domain.ArmTopology.ControlDeliberation : domain.ArmTopology.LiveDeliberation,
            DeliberationBudget = EmlDeliberationQuota.PairedGateNominal,
            Actions = EmlActionSelections.ProcedureGuarded,
        };
        CortexPolicyLearningConfig policies = new()
        {
            AuthorityCeiling = control ? domain.ArmTopology.ControlAuthority : domain.ArmTopology.LiveAuthorityCeiling,
            ReadoutDeliberationQuota = control ? 0 : new CortexPolicyLearningConfig().ReadoutDeliberationQuota,
            TrialAllocation = control ? null : new CortexPolicyTrialAllocationConfig
            {
                ArmSteps = domain.ArmTopology.TrialArmSteps,
                Authority = domain.ArmTopology.TrialAllocationAuthority,
                Identity = domain.ArmTopology.TrialAllocationIdentity,
            },
        };
        Cortex cortex = new(new CortexConfig
        {
            RunName = "gate-paired",
            Seed = seed,
            Steps = horizon,
            ActionsPerStep = curriculum.IntakeBatch,
            Curriculum = curriculum,
            Learning = new CortexLearningConfig { Policies = policies },
        });
        // Lineage emission is a registered-arm capability. Ordinary Cortex runs retain
        // the null turnstile so their paired artifacts remain byte-identical.
        cortex.EnableLoopLineage();
        return cortex;
    }

    private static void VerifySealedArm(LoopClosureRegistration registration, LoopClosureArms arm, string directory)
    {
        RunAuthority authority = RunAuthority.Load(directory);
        if (!string.Equals(authority.Binary.ProcessName, registration.AppHost, StringComparison.Ordinal)
            || !string.Equals(authority.Binary.ProcessSHA256, registration.AppHostSHA256, StringComparison.Ordinal)
            || !string.Equals(authority.Binary.AssemblyName, registration.Assembly, StringComparison.Ordinal)
            || !string.Equals(authority.Binary.AssemblySHA256, registration.AssemblySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure arm authority binary differs from the registered apphost/assembly");
        if (authority.Checkpoint.NextStep != registration.Horizon || !authority.Checkpoint.SaveLoadSaveExact)
            throw new InvalidDataException("loop-closure arm did not seal an exact registered horizon");
        if (authority.WorldSHA256 != registration.WorldSHA256)
            throw new InvalidDataException("loop-closure arm authority carries a different world");
        string persisted = arm == LoopClosureArms.Live ? registration.LiveConfigSHA256 : registration.ControlConfigSHA256;
        if (authority.PersistedConfigDigest != persisted || authority.ConfigFingerprint != registration.ArmNeutralConfigSHA256)
            throw new InvalidDataException("loop-closure arm authority config custody differs from registration");
        byte[] registrationBytes = File.ReadAllBytes(Path.Combine(directory, LoopClosureRegistration.AuthorityFileName));
        if (!registration.Encode().AsSpan().SequenceEqual(registrationBytes))
            throw new InvalidDataException("loop-closure arm registration bytes drifted before seal");
        string registrationDigest = Convert.ToHexStringLower(SHA256.HashData(registrationBytes));
        if (!authority.Artifacts.Any(item => item.RelativePath == LoopClosureRegistration.AuthorityFileName && item.SHA256 == registrationDigest))
            throw new InvalidDataException("loop-closure arm authority does not cover its registration bytes");
    }

    /// Run the fixed world-fed LIVE mechanism in a fresh ordinary run. This is a
    /// mechanism probe, not a registered assay: it never mints registration,
    /// report, or ClosureCertificate artifacts and never injects an event or corroboration.
    internal static int RunWorldFedProbe(string corpus, int horizon, TextWriter output, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(domain);
        if (string.IsNullOrWhiteSpace(corpus)) throw new ArgumentException("loop-closure probe requires an explicit world path", nameof(corpus));
        if (!File.Exists(corpus) && !Directory.Exists(corpus))
            throw new DirectoryNotFoundException($"loop-closure probe world was not found: {corpus}");

        const ulong seed = 0xC0117011UL;
        if (horizon is < 1 or > LoopClosureRegistration.RegisteredHorizon)
            throw new ArgumentOutOfRangeException(nameof(horizon), "diagnostic probe horizon must be between 1 and the registered 500-step horizon");
        if (LoopClosureRegistration.RegisteredHorizon != 500)
            throw new InvalidDataException("loop-closure probe horizon diverged from the registered 500-step horizon");

        string worldSHA256 = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
        string runName = $"loop-closure-probe_{Guid.NewGuid():N}";
        Run run = global::Cogito.Run.Create(runName);
        Cortex cortex = CreateArm(seed, worldSHA256, LoopClosureArms.Live, corpus, horizon, domain);
        int exit = cortex.Run(run);
        if (exit != 0)
        {
            output.WriteLine($"  gate loop-closure-probe · seed=0x{seed:X8} · horizon={horizon} · run={run.Dir} · exit={exit} · authority=missing");
            return 1;
        }

        try { return ReadWorldFedProbe(run.Dir, worldSHA256, horizon, output, domain); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            output.WriteLine($"  gate loop-closure-probe · run={run.Dir} · evidence=invalid · error={ex.Message}");
            return 1;
        }
    }

    /// Re-certify an already completed world-fed probe without creating a Run,
    /// starting Cortex, or writing any artifact. This is the recovery surface for
    /// a verifier crash after the ordinary run sealed its authority.
    internal static int CertifyWorldFedProbe(string runDirectory, string corpus, TextWriter output, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(domain);
        if (string.IsNullOrWhiteSpace(runDirectory)) throw new ArgumentException("probe certification requires a run directory", nameof(runDirectory));
        if (string.IsNullOrWhiteSpace(corpus)) throw new ArgumentException("probe certification requires an explicit world path", nameof(corpus));
        if (!File.Exists(corpus) && !Directory.Exists(corpus))
            throw new DirectoryNotFoundException($"loop-closure probe world was not found: {corpus}");
        const int horizon = 500;
        if (LoopClosureRegistration.RegisteredHorizon != horizon)
            throw new InvalidDataException("loop-closure probe horizon diverged from the registered 500-step horizon");

        string worldSHA256 = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
        string resolved = global::Cogito.Run.Resolve(runDirectory) ?? throw new DirectoryNotFoundException($"loop-closure probe run was not found: {runDirectory}");
        try { return ReadWorldFedProbe(resolved, worldSHA256, horizon, output, domain, "certify-loop-closure-probe"); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            output.WriteLine($"  gate certify-loop-closure-probe · run={resolved} · evidence=invalid · error={ex.Message}");
            return 1;
        }
    }

    private static int ReadWorldFedProbe(string runDirectory, string worldSHA256, int horizon, TextWriter output, IPolicyBoundaryDomain domain, string verb = "loop-closure-probe")
    {
        const ulong seed = 0xC0117011UL;
        int checkpointImageReads = 0;
        byte[] effectiveImage = Checkpoint.LoadEffectiveImage(runDirectory);
        checkpointImageReads++;
        // The completed authority already seals the checkpoint's physical chain and
        // Save∘Load∘Save result. Reuse that proof here: replaying the full Cortex Vow
        // would rebuild a second World/Tape solely to certify a read-only census.
        RunAuthority identity = RunAuthority.LoadIdentity(runDirectory);
        (string basePhysical, string chain) = CheckpointDelta.ReadPhysicalAuthority(runDirectory);
        if (!string.Equals(basePhysical, identity.Checkpoint.BasePhysicalSHA256, StringComparison.Ordinal)
            || !string.Equals(chain, identity.Checkpoint.PhysicalChainSHA256, StringComparison.Ordinal)
            || !string.Equals(Checkpoint.PhysicalSHA256(effectiveImage), identity.Checkpoint.PhysicalSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("historical checkpoint proof disagrees with the sealed authority");
        CheckpointVowReceipt vow = new(
            identity.Checkpoint.SaveLoadSaveExact,
            Checkpoint.CurrentSectionCount,
            effectiveImage.LongLength,
            effectiveImage.LongLength,
            identity.Checkpoint.PhysicalSHA256,
            identity.Checkpoint.PhysicalSHA256,
            identity.Checkpoint.BasePhysicalSHA256,
            identity.Checkpoint.PhysicalChainSHA256,
            true,
            []);
        RunAuthority authority = RunAuthority.Load(runDirectory, effectiveImage, vow);
        if (checkpointImageReads != 1)
            throw new InvalidDataException($"recertifier reopened the effective checkpoint image {checkpointImageReads} times");
        CortexRunConfig config = Checkpoint.PeekConfig(effectiveImage);
        if (config.Seed != seed)
            throw new InvalidDataException($"probe config seed 0x{config.Seed:X8} differs from fixed seed 0x{seed:X8}");
        if (config.Steps != horizon || authority.Checkpoint.NextStep != horizon)
            throw new InvalidDataException($"probe authority sealed at step {authority.Checkpoint.NextStep}, expected {horizon}");
        if (!string.Equals(config.ExpectedWorldSHA256, worldSHA256, StringComparison.Ordinal)
            || !string.Equals(authority.WorldSHA256, worldSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("probe world identity differs from the supplied corpus");

        string runID = Path.GetFileName(Path.GetFullPath(runDirectory));
        IReadOnlyList<PatternBecameThoughtCorroboration> theories = LoopClosureEvidenceStore.ReadPattern(runDirectory, runID);
        IReadOnlyList<LoopClosureR4Provenance> r4Records = LoopClosureEvidenceStore.ReadR4(runDirectory, runID);
        Func<CortexPolicyID, IPolicyBoundaryDomain> resolveDomain = LoopClosureEvidenceStore.ResolveRegisteredDomain(domain);
        IReadOnlyList<ThoughtOverruledInstinctCorroboration> divergences = LoopClosureEvidenceStore.ReadDivergence(runDirectory, runID, resolveDomain);
        IReadOnlyList<PolicyBoundaryDivergenceAdjudication> divergenceProofs = LoopClosureEvidenceStore.ReadDivergenceProof(runDirectory, runID, resolveDomain);
        IReadOnlyList<ObjectLoopClosedCorroboration> objects = LoopClosureEvidenceStore.ReadObject(runDirectory, runID);
        bool needsCustodyView = theories.Count > 0 || r4Records.Count > 0 || divergences.Count > 0
            || divergenceProofs.Count > 0 || objects.Count > 0;
        using LoopClosureEvidenceCustody.View? custody = needsCustodyView
            ? LoopClosureEvidenceCustody.View.Open(runDirectory) : null;

        LoopLineageTapeSnapshot lineageSource = Checkpoint.LoadLineageSnapshot(effectiveImage, runDirectory);
        IReadOnlyList<LoopLineageEdgeReceipt> lineage = LoopLineageVerifier.ReadTapeEdges(lineageSource);
        LoopLineageOccurrenceCheckResult lineageResult = LoopLineageVerifier.Verify(lineage, lineageSource);
        bool lineagePackets = LoopLineageVerifier.VerifyPacketBijection(lineageSource, lineage, out _);
        bool lineageJournal = LoopLineageVerifier.VerifyJournalLineageRows(
            lineageSource, Path.Combine(runDirectory, "journal.log"), out _);
        int exactPattern = 0;
        foreach (PatternBecameThoughtCorroboration theory in theories)
            if (custody is not null && LoopClosureEvidenceCustody.VerifyPattern(custody, authority, in theory, out _)) exactPattern++;
        int exactR4Divergence = 0;
        int exactObject = 0;
        // When a theory chain matches but its divergence custody sub-check fails, the
        // count silently stayed zero and the terminal report named no sub-check —
        // the R19 mystery.  Retain the last such rejection reason to surface it.
        string divergenceRejectReason = "";
        HashSet<string> matchedR4 = new(StringComparer.Ordinal);
        foreach (ThoughtOverruledInstinctCorroboration divergence in divergences)
        foreach (PolicyBoundaryDivergenceAdjudication proof in divergenceProofs)
        {
            if (!string.Equals(proof.Proof.Funding.QuotaDecisionID.ToString(), divergence.QuotaID.Value, StringComparison.Ordinal)
                || proof.Proof.Provenance is not LoopClosureR4Provenance proofR4) continue;
            LoopClosureR4Provenance? persistedR4 = r4Records.FirstOrDefault(candidate =>
                candidate.Episode.EpisodeDigest == proofR4.Episode.EpisodeDigest
                && candidate.Fold.ReceiptDigest == proofR4.Fold.ReceiptDigest
                && candidate.Teacher.ProvenanceDigest == proofR4.Teacher.ProvenanceDigest
                && candidate.Training.ReadoutTrainingCorroborationSHA256 == proofR4.Training.ReadoutTrainingCorroborationSHA256
                && candidate.Training.DecisionID.Equals(proofR4.Training.DecisionID));
            if (persistedR4 is not LoopClosureR4Provenance r4) continue;
            PatternBecameThoughtCorroboration? theory = theories.FirstOrDefault(candidate =>
                candidate.CompositionNodeID.Value == r4.Episode.EpisodeID.Value);
            if (theory is not PatternBecameThoughtCorroboration chainPattern || custody is null) continue;
            if (!LoopClosureEvidenceCustody.VerifyDivergence(custody, authority, in r4, in chainPattern, in proof, domain, out string divergenceFailure))
            {
                if (divergenceFailure.Length != 0) divergenceRejectReason = divergenceFailure;
                continue;
            }
            string r4Key = r4.Fold.ReceiptDigest.Value;
            if (!matchedR4.Add(r4Key)) continue;
            exactR4Divergence++;
            foreach (ObjectLoopClosedCorroboration closed in objects)
            {
                if (closed.PatternEvidenceSHA256 != LoopClosureEvidenceStore.DigestPattern(in chainPattern)
                    || closed.DivergenceEvidenceSHA256 != proof.EvidenceSHA256
                    || custody is null
                    || !LoopClosureEvidenceCustody.Verify(custody, authority, in r4, in chainPattern, proof, in closed, domain, out _)) continue;
                exactObject++;
                break;
            }
        }

        output.WriteLine($"  gate {verb} · seed=0x{seed:X8} · horizon={horizon} · run={runDirectory} · authority=sealed · historical_vow_closure=exact · checkpoint_image_reads={checkpointImageReads} · world={worldSHA256}");
        string divergenceRejectSuffix = exactR4Divergence == 0 && divergenceRejectReason.Length != 0
            ? $" · divergence-reject={divergenceRejectReason}" : "";
        output.WriteLine($"    evidence theory={theories.Count} r4={r4Records.Count} divergence={divergences.Count} object={objects.Count} lineage={lineage.Count} · exact-theory={exactPattern} exact-r4-paid-divergence={exactR4Divergence} exact-object={exactObject} · lineage={lineageResult.Status} packets={(lineagePackets ? "exact" : "INVALID")} journal={(lineageJournal ? "exact" : "INVALID")}{divergenceRejectSuffix}");
        return exactPattern > 0 && exactR4Divergence > 0 && lineageResult.Passed && lineagePackets && lineageJournal ? 0 : 1;
    }
}

public readonly record struct LoopClosureAdjudicationRequest(
    LoopClosureRegistration Registration,
    string LiveDirectory,
    string ControlDirectory,
    string ReportPath,
    string RegistrationPath = "")
{
    public void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(Registration);
        Registration.Validate();
        Registration.ValidateFrozenAuthority(domain);
        if (string.IsNullOrWhiteSpace(LiveDirectory) || string.IsNullOrWhiteSpace(ControlDirectory) || string.IsNullOrWhiteSpace(ReportPath))
            throw new ArgumentException("loop-closure adjudication request requires both arm directories and a report path");
        if (!string.IsNullOrWhiteSpace(RegistrationPath)) Registration.AssertPersistedBytes(RegistrationPath);
    }
}

public interface ILoopClosureAdjudicator
{
    LoopClosureReport Adjudicate(LoopClosureAdjudicationRequest request, LoopClosurePolicyBinding policy, IPolicyBoundaryDomain domain);
    ClosureCertificate Certify(LoopClosureReport report, LoopClosureRegistration registration,
        string liveDirectory, string controlDirectory, IPolicyBoundaryDomain domain);
}

public sealed class LoopClosureAdjudicator : ILoopClosureAdjudicator
{
    /// Assemble custody for a named policy. Domain adapters pass their binding
    /// here without changing the canonical five-link or shuffled-lineage rules.
    public LoopClosureReport Adjudicate(
        LoopClosureAdjudicationRequest request,
        LoopClosurePolicyBinding policy,
        IPolicyBoundaryDomain domain)
    {
        request.Validate(domain);
        policy.Validate();
        ArgumentNullException.ThrowIfNull(domain);
        if (!domain.PolicyBinding.Equals(policy))
            throw new InvalidDataException("loop-closure adjudicator domain binding disagrees with the requested policy");
        if (!string.IsNullOrWhiteSpace(request.RegistrationPath)) request.Registration.AssertPersistedBytes(request.RegistrationPath);
        string live = ResolveRun(request.LiveDirectory);
        string control = ResolveRun(request.ControlDirectory);
        RunAuthority liveAuthority = ReadCustodiedAuthority(request.Registration, live, LoopClosureArms.Live);
        RunAuthority controlAuthority = ReadCustodiedAuthority(request.Registration, control, LoopClosureArms.Control);
        LoopClosureArmReport liveArm = ToArmReport(liveAuthority);
        LoopClosureArmReport controlArm = ToArmReport(controlAuthority);

        List<LoopClosurePairLineVerdict> lines = BuildPairLines(live, control, domain);
        LoopClosureLineageNullOutcome lineageNull = BuildLineageNull(request.Registration, liveAuthority, live);
        List<LoopClosureVerdict> verdicts = BuildMechanismVerdicts(request.Registration.Digest, live, control, lines, lineageNull, domain);
        if (!liveAuthority.ClosureMatches(live, out string liveClosureError))
            throw new InvalidDataException($"loop-closure LIVE authority changed during adjudication: {liveClosureError}");
        if (!controlAuthority.ClosureMatches(control, out string controlClosureError))
            throw new InvalidDataException($"loop-closure CONTROL authority changed during adjudication: {controlClosureError}");
        OrganicComparisonSummary organicComparisons = BuildOrganicComparisonSummary(live, liveAuthority, in policy);
        LoopClosureLinkContract links = BuildLiveLinkContract(live, liveAuthority, lineageNull, in policy, domain);
        LoopClosureReport report = LoopClosureReport.Create(request.Registration.Digest, liveArm, controlArm, lines, verdicts, lineageNull, "typed loop-closure evidence assembled", domain, links, organicComparisons);
        EnsureOutsideRuns(Path.GetFullPath(request.ReportPath), live, control);
        report.Write(request.ReportPath);
        return report;
    }

    private static LoopClosureLinkContract BuildLiveLinkContract(
        string liveDirectory,
        RunAuthority liveAuthority,
        LoopClosureLineageNullOutcome lineageNull,
        in LoopClosurePolicyBinding policy,
        IPolicyBoundaryDomain domain)
    {
        string runID = Path.GetFileName(Path.GetFullPath(liveDirectory));
        IReadOnlyList<LoopClosureLinkAttempt> attempts = LoopClosureLinkAttemptStore.Read(liveDirectory, runID);
        if (attempts.Count == 0)
            throw new InvalidDataException("loop-closure report has no persisted typed link attempts");
        using Tape tape = Checkpoint.LoadTape(liveDirectory);
        string journalPath = Path.Combine(liveDirectory, "journal.log");
        if (!File.Exists(journalPath)) throw new InvalidDataException("loop-closure link custody omits journal.log");
        IReadOnlyList<JournalEventRow> journalEvents = ReadJournalEventRows(journalPath);
        IReadOnlyList<PolicyBoundaryDivergenceAdjudication> divergenceProofs = LoopClosureEvidenceStore.ReadDivergenceProof(
            liveDirectory, runID, LoopClosureEvidenceStore.ResolveRegisteredDomain(domain));
        Dictionary<LoopClosureLinkSpecies, List<LoopClosureLinkAttempt>> grouped = LoopClosureLinkContract.OrderedSpecies
            .ToDictionary(static species => species, static _ => new List<LoopClosureLinkAttempt>());
        foreach (LoopClosureLinkAttempt attempt in attempts)
        {
            if (!grouped.TryGetValue(attempt.Species, out List<LoopClosureLinkAttempt>? group))
                throw new InvalidDataException("loop-closure link attempt carries an unknown gate species");
            string attemptPath = LoopClosureLinkAttemptStore.RelativePath(attempt.RecordID);
            RunAuthorityArtifact? authorityArtifact = liveAuthority.Artifacts.SingleOrDefault(item => item.RelativePath == attemptPath);
            if (authorityArtifact is null)
                throw new InvalidDataException($"loop-closure link {attempt.RecordID} is outside sealed authority closure");
            string attemptFile = Path.Combine(liveDirectory, attemptPath);
            string actualAttemptDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(attemptFile)));
            if (!string.Equals(actualAttemptDigest, authorityArtifact.SHA256, StringComparison.Ordinal))
                throw new InvalidDataException($"loop-closure link {attempt.RecordID} differs from sealed authority bytes");
            VerifyLinkAttemptCustody(attempt, liveDirectory, tape, journalEvents, divergenceProofs, in policy, domain);
            group.Add(attempt);
        }
        bool repositoryOrganicGap = domain is RepositoryPolicyBoundaryDomain
            && grouped[LoopClosureLinkSpecies.PreferenceDivergence].Count == 0;
        int receiptOffset = repositoryOrganicGap ? 1 : 0;
        int prefixLength = 0;
        while (!repositoryOrganicGap && prefixLength < LoopClosureLinkContract.OrderedSpecies.Count
            && grouped[LoopClosureLinkContract.OrderedSpecies[prefixLength]].Count > 0)
            prefixLength++;
        if (!repositoryOrganicGap && prefixLength == 0)
            throw new InvalidDataException("loop-closure link custody has no reached typed chain");
        if (repositoryOrganicGap
            ? LoopClosureLinkContract.OrderedSpecies.Skip(1).Any(species => grouped[species].Count == 0)
            : LoopClosureLinkContract.OrderedSpecies.Skip(prefixLength).Any(species => grouped[species].Count > 0))
            throw new InvalidDataException("loop-closure link custody contains a hole in its typed chain");

        for (int index = Math.Max(1, receiptOffset); index < LoopClosureLinkContract.OrderedSpecies.Count; index++)
        {
            if (repositoryOrganicGap && index == 1) continue;
            List<LoopClosureLinkAttempt> predecessors = grouped[LoopClosureLinkContract.OrderedSpecies[index - 1]];
            foreach (LoopClosureLinkAttempt attempt in grouped[LoopClosureLinkContract.OrderedSpecies[index]])
                if (!predecessors.Any(predecessor => predecessor.EventID.Value == attempt.PredecessorEventID
                    && predecessor.EvidenceSHA256 == attempt.PredecessorEvidenceSHA256
                    && predecessor.AttemptSHA256 == attempt.PredecessorAttemptSHA256))
                    throw new InvalidDataException($"loop-closure {attempt.Species} attempt is not bound to a preceding typed attempt");
        }
        LoopClosureLinkReceipt[] receipts = new LoopClosureLinkReceipt[LoopClosureLinkContract.OrderedSpecies.Count - receiptOffset];
        LoopClosureGateLiveness[] liveness = repositoryOrganicGap
            ? new LoopClosureGateLiveness[LoopClosureLinkContract.OrderedSpecies.Count]
            : new LoopClosureGateLiveness[receipts.Length];
        if (repositoryOrganicGap)
            liveness[0] = LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.PreferenceDivergence, 0, 0, 0, []);
        for (int index = receiptOffset; index < LoopClosureLinkContract.OrderedSpecies.Count; index++)
        {
            LoopClosureLinkSpecies species = LoopClosureLinkContract.OrderedSpecies[index];
            List<LoopClosureLinkAttempt> group = grouped[species];
            if (group.Count == 0) throw new InvalidDataException($"loop-closure link custody omits {species}");
            group.Sort(static (left, right) => left.EventID.Value.CompareTo(right.EventID.Value));
            long admitted = group.LongCount(static attempt => attempt.State == LoopClosureLinkStates.Admitted);
            long denied = group.LongCount(static attempt => attempt.State == LoopClosureLinkStates.Denied);
            int selectedIndex = group.FindLastIndex(static attempt => attempt.State == LoopClosureLinkStates.Admitted);
            LoopClosureLinkAttempt selected = group[selectedIndex >= 0 ? selectedIndex : group.Count - 1];
            receipts[index - receiptOffset] = selected.ToReceipt();
            LoopClosureGateDenial[] denials = group.Where(static attempt => attempt.State == LoopClosureLinkStates.Denied)
                .GroupBy(static attempt => attempt.DenialReason)
                .Select(static reasons => new LoopClosureGateDenial(reasons.Key, reasons.LongCount()))
                .OrderBy(static denial => denial.Reason).ToArray();
            liveness[repositoryOrganicGap ? index : index - receiptOffset] = LoopClosureGateLiveness.Create(species, group.Count, admitted, denied, denials);
        }
        LoopClosureLinkContract contract = new(receipts, liveness, repositoryOrganicGap);
        contract.Validate(requireComplete: false);
        if (contract.IsComplete)
            BindExecutedClosureToLineageNull(contract, lineageNull);
        return contract;
    }

    private static OrganicComparisonSummary BuildOrganicComparisonSummary(
        string liveDirectory,
        RunAuthority liveAuthority,
        in LoopClosurePolicyBinding policy)
    {
        using Tape tape = Checkpoint.LoadTape(liveDirectory);
        string journalPath = Path.Combine(liveDirectory, "journal.log");
        if (!File.Exists(journalPath)) throw new InvalidDataException("organic comparison custody omits journal.log");
        string source = policy.PolicyPacketSource;
        string comparisonSource = policy.OrganicComparisonPacketSource;
        string[] journalLines = File.ReadAllLines(journalPath);
        TapeEventView[] views = tape.GetEventViews().Where(view => view.Source == comparisonSource).ToArray();
        if (views.Length == 0)
            throw new InvalidDataException("sealed LIVE arm omits every organic comparison receipt");
        List<OrganicComparisonReceipt> receipts = new(views.Length);
        foreach (TapeEventView view in views.OrderBy(static view => view.Id.Value))
        {
            if (!view.HasRole(TapeEventRoles.Measurement | TapeEventRoles.AuditOnly))
                throw new InvalidDataException($"organic comparison event {view.Id} lacks measurement/custody roles");
            if (!tape.Resolve(view.Id, out byte[] encoded)
                || !TapePacketCreator.TryDecodeOrganicComparison(encoded, out OrganicComparisonReceipt receipt))
                throw new InvalidDataException($"organic comparison event {view.Id} is not a typed receipt");
            receipt.Validate();
            if (!receipt.Policy.Equals(policy.PolicyID) || receipt.SourceDecisionEventID.Value >= view.Id.Value)
                throw new InvalidDataException($"organic comparison event {view.Id} has an invalid source decision binding");
            if (!tape.Resolve(receipt.SourceDecisionEventID, out byte[] sourcePayload)
                || !TryReadSourceDecision(tape, receipt.SourceDecisionEventID, in policy, receipt, sourcePayload, out CortexPolicyDecisionPacket decision))
                throw new InvalidDataException($"organic comparison event {view.Id} omits its authenticated POLICY-DECISION source");
            string sourcePayloadDigest = Convert.ToHexStringLower(SHA256.HashData(sourcePayload));
            CortexPolicyDecision sourceDecision = new(receipt.DecisionID, receipt.Policy, decision.Readout);
            string sourceJournalDigest = Journal.ComputePolicyDecisionJournalSHA256(receipt.Step,
                receipt.SourceDecisionEventID, source, in sourceDecision, decision.ActionCount,
                decision.Features.Length, sourcePayload.Length);
            if (!string.Equals(sourcePayloadDigest, receipt.SourceDecisionPayloadSHA256, StringComparison.Ordinal)
                || !string.Equals(sourceJournalDigest, receipt.SourceDecisionJournalSHA256, StringComparison.Ordinal))
                throw new InvalidDataException($"organic comparison event {view.Id} source payload/journal custody drifted");
            RequireJournalRow(journalLines, receipt.Step, view.Id.Value, "organic-comparison", receipt.CanonicalReceiptSHA256);
            // The policy-decision journal row is the canonical input to the
            // digest above; it does not redundantly print that digest.
            RequireJournalRow(journalLines, receipt.Step, receipt.SourceDecisionEventID.Value, "policy-decision", "");
            VerifyFundingRows(liveDirectory, receipt, in policy);
            receipts.Add(receipt);
        }
        return OrganicComparisonSummary.Create(new(liveAuthority.Digest), receipts);
    }

    private static bool TryReadSourceDecision(
        Tape tape,
        TapeEventID eventID,
        in LoopClosurePolicyBinding policy,
        OrganicComparisonReceipt receipt,
        ReadOnlySpan<byte> payload,
        out CortexPolicyDecisionPacket packet)
    {
        policy.Validate();
        packet = default;
        TapeEventView? view = tape.GetEventViews().FirstOrDefault(candidate => candidate.Id == eventID);
        if (view is null || !policy.MatchesSource(view.Value.Source) || !TapePacketCreator.TryDecodePolicyDecision(payload, out packet)
            || !packet.DecisionID.Equals(receipt.DecisionID)
            || packet.Readout.LaunchpadAction != receipt.LaunchpadAction
            || packet.Readout.RawCandidateAction != receipt.RawCandidateAction
            || packet.Readout.SelectedCandidateAction != receipt.SelectedCandidateAction
            || packet.Readout.GrammarRevision != receipt.ReadoutRevision
            || packet.Readout.ReadoutFingerprint != receipt.ReadoutFingerprint
            || packet.Readout.ReadoutCandidateFingerprint != receipt.CandidateFingerprint
            || packet.Readout.ReadoutCandidateOccurrenceDigest != receipt.CandidateOccurrenceDigest)
            return false;
        return true;
    }

    private static void RequireJournalRow(
        IReadOnlyList<string> lines,
        int step,
        long eventID,
        string kind,
        string digest)
    {
        string marker = $"{step}\t{kind}\t{new TapeEventID(eventID)}\t";
        string[] matches = lines.Where(line => line.StartsWith(marker, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(digest) || line.Contains(digest, StringComparison.Ordinal))).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"organic comparison custody has {matches.Length} {kind} journal rows for {eventID}");
    }

    private static void VerifyFundingRows(
        string liveDirectory,
        OrganicComparisonReceipt receipt,
        in LoopClosurePolicyBinding policy)
    {
        policy.Validate();
        if (!receipt.Policy.Equals(policy.PolicyID))
            throw new InvalidDataException("organic comparison funding row carries another policy identity");
        if (receipt.QuotaDecisionID is not { Value: > 0 } fundingID)
        {
            if (receipt.FundingJournalRowSHA256.Length != 0 || receipt.SettlementJournalRowSHA256.Length != 0)
                throw new InvalidDataException("organic comparison carries funding digests without a funding identity");
            return;
        }
        string fundingToken = fundingID.ToString();
        string fundingPath = Path.Combine(liveDirectory, "policy_readout_funding.journal.tsv");
        if (!File.Exists(fundingPath)) throw new InvalidDataException($"organic comparison funding row {fundingToken} is missing");
        string[] fundingRows = File.ReadAllLines(fundingPath).Where(line => line.StartsWith(fundingToken + "\t", StringComparison.Ordinal)).ToArray();
        if (fundingRows.Length != 1 || Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fundingRows[0]))) != receipt.FundingJournalRowSHA256)
            throw new InvalidDataException($"organic comparison funding row {fundingToken} is missing, duplicated, or mutated");
        if (receipt.SettlementJournalRowSHA256.Length == 0) return;
        string settlementPath = Path.Combine(liveDirectory, "policy_readout_settlements.journal.tsv");
        if (!File.Exists(settlementPath)) throw new InvalidDataException($"organic comparison settlement row {fundingToken} is missing");
        string[] settlementRows = File.ReadAllLines(settlementPath).Where(line => line.StartsWith(fundingToken + "\t", StringComparison.Ordinal)).ToArray();
        if (settlementRows.Length != 1 || Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(settlementRows[0]))) != receipt.SettlementJournalRowSHA256)
            throw new InvalidDataException($"organic comparison settlement row {fundingToken} is missing, duplicated, or mutated");
    }

    private static void BindExecutedClosureToLineageNull(
        LoopClosureLinkContract contract,
        LoopClosureLineageNullOutcome lineageNull)
    {
        LoopClosureLinkReceipt executed = contract.Receipts[^1];
        if (executed.State != LoopClosureLinkStates.Admitted)
            throw new InvalidDataException("loop-closure executed link is not admitted");
        if (lineageNull is LoopClosureLineageNullMissing missing)
            throw new InvalidDataException($"loop-closure executed link has no shuffled-lineage null corroboration: {missing.Reason}");
        if (lineageNull is not LoopClosureLineageNullExecuted { Receipt: var receipt })
            throw new InvalidDataException("loop-closure executed link has no typed shuffled-lineage null corroboration");
        receipt.Validate();
        if (receipt.OriginalStatus != LoopLineageOccurrenceCheckStatuses.PASS
            || receipt.ShuffledStatus != LoopLineageOccurrenceCheckStatuses.FAIL
            || receipt.OriginalLineageSHA256 == receipt.ShuffledLineageSHA256
            || !receipt.FirstDiscriminatingEdge.IsValid)
            throw new InvalidDataException("loop-closure executed link is not bound to a discriminating shuffled-lineage null");
    }

    private static void VerifyLinkAttemptCustody(
        LoopClosureLinkAttempt attempt,
        string liveDirectory,
        Tape tape,
        IReadOnlyList<JournalEventRow> journalEvents,
        IReadOnlyList<PolicyBoundaryDivergenceAdjudication> divergenceProofs,
        in LoopClosurePolicyBinding policy,
        IPolicyBoundaryDomain domain)
    {
        policy.Validate();
        ArgumentNullException.ThrowIfNull(domain);
        if (policy.PolicyID == RepositoryPolicyBoundaryDomain.Instance.PolicyID)
        {
            if (attempt.LinkEventID.Value <= 0 || !attempt.LinkPacketSHA256.IsValid || !attempt.LinkJournalSHA256.IsValid
                || !tape.TryGetEventView(attempt.LinkEventID, out TapeEventView linkView)
                || linkView.Source != "repository:loop-link"
                || linkView.Provenance != Provenances.Execution
                || linkView.Roles != TapeEventRoles.AuditOnly
                || !tape.Resolve(attempt.LinkEventID, out byte[] linkPayload)
                || !TapePacketCreator.TryReadRepositoryLineageReceipt(linkPayload, out string linkKind, out string linkCanonical, out string linkDigest)
                || linkKind != attempt.Kind
                || linkCanonical != attempt.Canonical
                || linkDigest != attempt.LinkPacketSHA256.Value
                || linkDigest != RepositoryLineageReceiptCodec.Digest(attempt.Kind, attempt.Canonical)
                || attempt.LinkJournalSHA256.Value != LoopClosureLinkAttemptStore.DigestLoopClosureLinkJournalReceipt(
                    attempt.Step, attempt.LinkEventID.Value, linkPayload.Length).Value
                || !journalEvents.Any(row => row.EventID == attempt.LinkEventID.Value
                    && row.Step == attempt.Step && row.Kind == "mint" && row.Source == "repository:loop-link"
                    && row.PayloadLength == linkPayload.Length
                    && row.LineSHA256 == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"{attempt.Step}\tmint\t{attempt.LinkEventID}\trepository:loop-link\t{linkPayload.Length}B")))))
                throw new InvalidDataException($"repository loop-closure link {attempt.RecordID} omits exact packet custody");
        }
        long eventID = attempt.EventID.Value;
        Tape evidenceTape = tape;
        IReadOnlyList<JournalEventRow> evidenceJournal = journalEvents;
        PolicyBoundaryRailMetadataDocument? childRail = null;
        string childDirectory = "";
        using Tape? childTape = string.IsNullOrWhiteSpace(attempt.EvidenceRunID) ? null : Checkpoint.LoadTape(
            Path.Combine(liveDirectory, attempt.EvidenceRelativePath));
        if (childTape is not null)
        {
            childDirectory = Path.Combine(liveDirectory, attempt.EvidenceRelativePath);
            string authorityDigest = RunAuthority.LoadIdentity(childDirectory).Digest;
            string railPath = Path.Combine(childDirectory, "policy-boundary.rail.ron");
            string railDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(railPath)));
            if (authorityDigest != attempt.EvidenceAuthoritySHA256.Value || railDigest != attempt.EvidenceRailSHA256.Value)
                throw new InvalidDataException($"{attempt.Species} child authority/rail custody drifted");
            childRail = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(File.ReadAllBytes(railPath));
            evidenceTape = childTape;
            evidenceJournal = ReadJournalEventRows(Path.Combine(childDirectory, "journal.log"));
            if (attempt.ChildOutcome.IsPresent)
            {
                attempt.ChildOutcome.Validate(required: true);
                if (!string.Equals(attempt.ChildOutcome.RunID, attempt.EvidenceRunID, StringComparison.Ordinal)
                    || !string.Equals(attempt.ChildOutcome.RelativePath, attempt.EvidenceRelativePath, StringComparison.Ordinal)
                    || attempt.ChildOutcome.AuthoritySHA256.Value != attempt.EvidenceAuthoritySHA256.Value
                    || attempt.ChildOutcome.RailSHA256.Value != attempt.EvidenceRailSHA256.Value)
                    throw new InvalidDataException($"{attempt.Species} child outcome reference disagrees with its child arm custody");
            }
        }
        // ExecutedDivergence's EventID is the parent-tape reference packet. Its
        // ordinary POLICY-OUTCOME event remains in the child namespace below.
        if (attempt.Species == LoopClosureLinkSpecies.ExecutedDivergence && attempt.ChildOutcome.IsPresent)
        {
            evidenceTape = tape;
            evidenceJournal = journalEvents;
        }
        int step = attempt.Step;
        LoopClosureLinkSpecies species = attempt.Species;
        TapeEventView? attemptView = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
        bool repositoryPreference = attempt.Species == LoopClosureLinkSpecies.PreferenceDivergence
            && attemptView is TapeEventView repositoryView
            && string.Equals(repositoryView.Source, "repository-preference", StringComparison.Ordinal);
        string expectedKind = repositoryPreference
            ? "repository-preference"
            : ExpectedAttemptJournalKind(attempt);
        if (!evidenceJournal.Any(row => row.EventID == eventID && row.Step == step && row.Kind == expectedKind)
            || attempt.JournalSHA256 != LoopClosureLinkAttemptStore.DigestJournalReceipt(step, expectedKind, eventID))
            throw new InvalidDataException($"loop-closure link {attempt.RecordID} is not journal-bound");
        if (!evidenceTape.Resolve(attempt.EventID, out byte[] payload)
            || LoopClosureLinkAttemptStore.DigestPayload(payload) != attempt.EvidenceSHA256.Value)
            throw new InvalidDataException($"loop-closure link {attempt.RecordID} is not tape-payload-bound");
        if (attempt.PredecessorEventID >= 0)
        {
            if (!LoopClosureLinkAttemptStore.TryValidatePredecessorChronology(
                    in attempt, tape, evidenceTape, childTape, out string failure))
                throw new InvalidDataException(failure);
        }
        if (attempt.Species == LoopClosureLinkSpecies.PreferenceDivergence)
        {
            if (!MatchesPreferenceEvidence(attempt, attemptView, payload, in policy, evidenceTape))
                throw new InvalidDataException("organic preference divergence is not a bound preference comparison");
            if (repositoryPreference
                && (!RepositoryPreferenceComparisonReceipt.TryDecode(payload, out RepositoryPreferenceComparisonReceipt comparison)
                    || comparison.Step != attempt.Step
                    || !evidenceJournal.Any(row => row.EventID == comparison.SelectionEventID.Value
                        && row.Step == comparison.Step
                        && row.Kind == "repository-selection")
                    || LoopClosureLinkAttemptStore.DigestJournalReceipt(comparison.Step, "repository-selection", comparison.SelectionEventID.Value).Value
                        != comparison.SelectionJournalSHA256))
                throw new InvalidDataException("repository preference comparison lacks its selection journal custody");
        }
        if (attempt.State == LoopClosureLinkStates.Denied && attempt.Species is not LoopClosureLinkSpecies.PreferenceDivergence)
        {
            TapeEventView? view = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
            if (view is null || !policy.MatchesSource(view.Value.Source)
                || !TryDecodeOrganicPolicyDecision(payload, out CortexPolicyDecisionReadout deniedReadout))
                throw new InvalidDataException($"{attempt.Species} denial is not a bound policy-decision event");
            bool diverges = deniedReadout.RawCandidateAction >= 0 && deniedReadout.SelectedCandidateAction >= 0
                && deniedReadout.SelectedCandidateAction != deniedReadout.RawCandidateAction
                && deniedReadout.SelectedCandidateAction != deniedReadout.LaunchpadAction;
            bool deniedShape = attempt.Species == LoopClosureLinkSpecies.InterventionDivergence
                ? !diverges
                : attempt.Species == LoopClosureLinkSpecies.AuthorityEligible
                    ? !domain.ValidateExecutionAuthority(deniedReadout.Authority, deniedReadout.SelectionCause, requireGrammar: true)
                    : !Encoding.ASCII.GetString(payload).StartsWith("POLICY-BOUNDARY\t", StringComparison.Ordinal);
            if (!deniedShape) throw new InvalidDataException($"{attempt.Species} denial event does not prove its first failed gate");
            return;
        }
        if (attempt.State == LoopClosureLinkStates.Admitted
            && attempt.Species == LoopClosureLinkSpecies.InterventionDivergence
            && string.IsNullOrWhiteSpace(attempt.EvidenceRunID))
        {
            TapeEventView? view = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
            // Frozen tape source token policy-boundary:dissent; identifier-side name is Divergence.
            if (view is null || !string.Equals(view.Value.Source, "policy-boundary:dissent", StringComparison.Ordinal)
                || !TryResolvePaidDivergenceProof(payload, divergenceProofs, attempt.QuotaID, out _))
                throw new InvalidDataException("intervention admission is not the paid-divergence packet");
        }
        if (attempt.State == LoopClosureLinkStates.Admitted
            && attempt.Species == LoopClosureLinkSpecies.InterventionDivergence
            && !string.IsNullOrWhiteSpace(attempt.EvidenceRunID))
        {
            TapeEventView? view = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
            if (view is null || !policy.MatchesSource(view.Value.Source)
                || childRail is not PolicyBoundaryRailMetadataDocument forcedRail
                || !LoopClosureLinkAttemptStore.TryDecodeForcedChildReadout(
                    payload, forcedRail, attempt.EventID, attempt.Step, attempt.QuotaID.Value, out _))
                throw new InvalidDataException("child intervention admission is not a divergent child authority decision");
        }
        if (attempt.State == LoopClosureLinkStates.Admitted
            && attempt.Species == LoopClosureLinkSpecies.AuthorityEligible)
        {
            TapeEventView? view = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
            bool childAuthority = !string.IsNullOrWhiteSpace(attempt.EvidenceRunID)
                && childRail is PolicyBoundaryRailMetadataDocument forcedRail
                && LoopClosureLinkAttemptStore.TryDecodeForcedChildReadout(
                    payload, forcedRail, attempt.EventID, attempt.Step, attempt.QuotaID.Value, out _);
            if (view is null || !policy.MatchesSource(view.Value.Source)
                || !TapePacketCreator.TryDecodePolicyDecision(payload, out CortexPolicyDecisionPacket decision)
                || decision.DecisionID.Value == 0
                || (string.IsNullOrWhiteSpace(attempt.EvidenceRunID)
                    && !domain.ValidateExecutionAuthority(decision.Readout.Authority, decision.Readout.SelectionCause, requireGrammar: true))
                || (!string.IsNullOrWhiteSpace(attempt.EvidenceRunID) && !childAuthority)
                || (string.IsNullOrWhiteSpace(attempt.EvidenceRunID)
                    && !divergenceProofs.Any(proof => proof.Proof.Funding.QuotaDecisionID.ToString() == attempt.QuotaID.Value
                        && proof.Proof.ForcedNull.DecisionID.Value == decision.DecisionID.Value
                        && proof.Proof.ForcedNull.SelectionCause == CortexPolicySelectionCauses.TrialOverride
                        && proof.Proof.ForcedNull.BehaviorallyExecuted && proof.Proof.ForcedNull.Diverged)))
                throw new InvalidDataException("authority admission is not a grammar policy decision");
        }
        if (attempt.State == LoopClosureLinkStates.Admitted
            && attempt.Species == LoopClosureLinkSpecies.BoundaryAdmitted)
        {
            TapeEventView? view = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
            if (view is null || !policy.MatchesSource(view.Value.Source)
                || !Encoding.ASCII.GetString(payload).StartsWith("POLICY-BOUNDARY\t", StringComparison.Ordinal)
                || !TryReadLinkPacketField(Encoding.ASCII.GetString(payload), "digest", out string boundaryDigest)
                || !string.Equals(boundaryDigest, attempt.ForkReceiptSHA256.Value, StringComparison.Ordinal))
                throw new InvalidDataException("boundary admission is not the fork receipt packet");
            if (!TryResolveBoundaryProof(payload, divergenceProofs, attempt.QuotaID, out PolicyBoundaryDivergenceAdjudication proof)
                || !proof.Proof.ForkReceipt.Verified || !proof.Proof.ForkReceipt.MatchedSpend
                || !proof.Proof.ForkReceipt.ForcedNullBehaviorExecuted || !proof.Proof.ForkReceipt.ForcedNullDiverged
                || !proof.Proof.ForkReceipt.AllChildrenCompleted
                || !MatchesForkReceiptDigest(proof.Proof.ForkReceipt, attempt.ForkReceiptSHA256.Value))
                throw new InvalidDataException("boundary admission is not proven by the paid divergence fork receipt");
        }
        if (attempt.Species == LoopClosureLinkSpecies.ExecutedDivergence)
        {
            TapeEventView? outcomeView = evidenceTape.GetEventViews().FirstOrDefault(candidate => candidate.Id == attempt.EventID);
            string outcomeText = Encoding.ASCII.GetString(payload);
            if (outcomeView is null || !string.Equals(outcomeView.Value.Source, "policy-boundary:outcome", StringComparison.Ordinal)
                || !outcomeText.StartsWith("POLICY-BOUNDARY-OUTCOME\t", StringComparison.Ordinal)
                || !divergenceProofs.Any(proof => proof.EvidenceSHA256 == attempt.DivergenceEvidenceSHA256
                    && outcomeText.Contains("adjudication=" + proof.EvidenceSHA256.Value, StringComparison.Ordinal)))
                throw new InvalidDataException("executed divergence is not the adjudicated-outcome tape event");
            if (!attempt.ChildOutcome.IsPresent
                || !TryReadLinkPacketField(outcomeText, "forced-decision", out string forcedDecision)
                || !string.Equals(forcedDecision, $"u:{attempt.ChildOutcome.ForcedDecisionID.Value:X16}", StringComparison.Ordinal)
                || !TryReadLinkPacketField(outcomeText, "forced-outcome-event", out string forcedOutcomeEvent)
                || !long.TryParse(forcedOutcomeEvent, NumberStyles.Integer, CultureInfo.InvariantCulture, out long encodedChildOutcomeEventID)
                || encodedChildOutcomeEventID != attempt.ChildOutcome.OutcomeEventID.Value
                || !TryReadLinkPacketField(outcomeText, "forced-outcome-payload", out string forcedOutcomePayload)
                || !string.Equals(forcedOutcomePayload, attempt.ChildOutcome.OutcomePayloadSHA256.Value, StringComparison.Ordinal))
                throw new InvalidDataException("executed divergence parent outcome does not carry its child outcome identity");
            if (childTape is null || childRail is not PolicyBoundaryRailMetadataDocument forcedRail
                || !forcedRail.ordinaryOutcomeRequired
                || forcedRail.executedOutcomeEventID != attempt.ChildOutcome.OutcomeEventID.Value
                || !string.Equals(forcedRail.executedOutcomePayloadSHA256, attempt.ChildOutcome.OutcomePayloadSHA256.Value, StringComparison.Ordinal)
                || forcedRail.executedDecisionID != attempt.ChildOutcome.ForcedDecisionID.Value
                || !LoopClosureLinkAttemptStore.TryReadTerminalPolicyOutcome(
                    childTape, Path.Combine(childDirectory, "journal.log"), attempt.ChildOutcome.ForcedDecisionID,
                    attempt.ChildOutcome.OutcomeEventID, attempt.ChildOutcome.OutcomePayloadSHA256.Value, in policy))
                throw new InvalidDataException("executed divergence is not bound to the child's ordinary pre-seal policy outcome");
        }
    }

    private static bool IsDivergentPolicyReadout(in CortexPolicyDecisionReadout readout)
        => readout.RawCandidateAction >= 0 && readout.SelectedCandidateAction >= 0
            && readout.SelectedCandidateAction != readout.RawCandidateAction
            && readout.SelectedCandidateAction != readout.LaunchpadAction;

    internal static bool MatchesPreferenceEvidence(
        LoopClosureLinkAttempt attempt,
        TapeEventView? view,
        ReadOnlySpan<byte> payload,
        in LoopClosurePolicyBinding policy)
        => MatchesPreferenceEvidence(attempt, view, payload, in policy, null);

    private static bool MatchesPreferenceEvidence(
        LoopClosureLinkAttempt attempt,
        TapeEventView? view,
        ReadOnlySpan<byte> payload,
        in LoopClosurePolicyBinding policy,
        Tape? evidenceTape)
    {
        policy.Validate();
        if (attempt.State == LoopClosureLinkStates.Denied
            && attempt.DenialReason == LoopClosureGateDenialReasons.NoOrganicOpportunity)
        {
            string census = Encoding.ASCII.GetString(payload);
            return view is not null && string.Equals(view.Value.Source, "loop-closure:organic-opportunity", StringComparison.Ordinal)
                && census.StartsWith("LOOP-CLOSURE-ORGANIC-OPPORTUNITY\t", StringComparison.Ordinal)
                && TryReadLinkPacketField(census, "policy", out string censusPolicy)
                && string.Equals(censusPolicy, policy.PolicyID.Value, StringComparison.Ordinal)
                && TryReadLinkPacketField(census, "opportunities", out string opportunities)
                && opportunities == "0";
        }
        if (view is not null && string.Equals(view.Value.Source, "repository-preference", StringComparison.Ordinal))
        {
            if (view.Value.Provenance != Provenances.Execution
                || view.Value.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                || !RepositoryPreferenceComparisonReceipt.TryDecode(payload, out RepositoryPreferenceComparisonReceipt comparison)
                || !comparison.PolicyID.Equals(policy.PolicyID)
                || !comparison.IsPreferenceDivergence
                || attempt.State != LoopClosureLinkStates.Admitted
                || attempt.HasDenialReason
                || attempt.Path != LoopClosureLinkPaths.Organic
                || attempt.PredecessorEventID >= 0
                || attempt.EvidenceSHA256.Value != LoopClosureLinkAttemptStore.DigestPayload(payload))
                return false;
            if (evidenceTape is not null)
            {
                if (comparison.SelectionEventID.Value <= view.Value.Id.Value
                    || !evidenceTape.TryGetEventView(comparison.SelectionEventID, out TapeEventView selectionView)
                    || selectionView.Source != "repository-selection"
                    || selectionView.Provenance != Provenances.Execution
                    || selectionView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                    || !evidenceTape.Resolve(comparison.SelectionEventID, out byte[] selectionPayload)
                    || LoopClosureLinkAttemptStore.DigestPayload(selectionPayload) != comparison.SelectionPayloadSHA256
                    || !RepositorySelectionReceipt.TryDecode(selectionPayload, out RepositorySelectionReceipt selection)
                    || selection.Step != comparison.Step
                    || selection.PolicyID != comparison.PolicyID
                    || selection.DecisionID != comparison.DecisionID
                    || selection.CandidateSpecies != (comparison.LearnedCandidatePresent ? comparison.LearnedSpecies : comparison.LaunchpadSpecies)
                    || selection.CandidateCanonical != (comparison.LearnedCandidatePresent ? comparison.LearnedCanonical : comparison.LaunchpadCanonical)
                    || selection.CandidateDigest != (comparison.LearnedCandidatePresent ? comparison.LearnedDigest : comparison.LaunchpadDigest)
                    || selection.FrontierRevision != comparison.FrontierRevision
                    || selection.FrontierAuthoritySHA256 != comparison.FrontierAuthoritySHA256)
                    return false;
                if (!evidenceTape.TryGetEventView(selection.DecisionEventID, out TapeEventView decisionView)
                    || decisionView.Source != "policy:" + policy.PolicyID.Value
                    || decisionView.Provenance != Provenances.Execution
                    || decisionView.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
                    || !evidenceTape.Resolve(selection.DecisionEventID, out byte[] decisionPayload)
                    || LoopClosureLinkAttemptStore.DigestPayload(decisionPayload) != selection.DecisionPayloadSHA256
                    || !TapePacketCreator.TryDecodePolicyDecision(decisionPayload, out CortexPolicyDecisionPacket decisionPacket)
                    || decisionPacket.DecisionID != selection.DecisionID
                    || selection.PolicyID != policy.PolicyID
                    || decisionPacket.Readout.ReadoutFingerprint != selection.ReadoutFingerprint
                    || decisionPacket.Readout.ReadoutCandidateFingerprint != selection.ReadoutCandidateFingerprint
                    || selection.DecisionEventID.Value >= comparison.SelectionEventID.Value)
                    return false;
            }
            if (!RepositoryNative.Policy.TrySpecies((int)comparison.LaunchpadSpecies, out _)
                || !RepositoryNative.Policy.TrySpecies((int)comparison.LearnedSpecies, out _))
                return false;
            return comparison.LaunchpadSpecies != comparison.LearnedSpecies
                || !string.Equals(comparison.LaunchpadCanonical, comparison.LearnedCanonical, StringComparison.Ordinal)
                || comparison.LaunchpadDigest != comparison.LearnedDigest;
        }
        if (view is null || !policy.MatchesSource(view.Value.Source)
            || !TryDecodeOrganicPolicyDecision(payload, out CortexPolicyDecisionReadout preferenceReadout))
            return false;
        if (attempt.State == LoopClosureLinkStates.Denied
            && attempt.DenialReason != (preferenceReadout.RawCandidateAction < 0
                ? LoopClosureGateDenialReasons.CandidateUnavailable
                : LoopClosureGateDenialReasons.ReflexAgreement))
            return false;
        // PreferenceDivergence is reserved for an organic candidate that was
        // actually present and differed from the launchpad action.  Candidate
        // absence and agreement are measurements in the comparison stream,
        // never preference attempts.
        bool diverged = preferenceReadout.RawCandidateAction >= 0
            && preferenceReadout.RawCandidateAction != preferenceReadout.LaunchpadAction;
        return attempt.State == LoopClosureLinkStates.Admitted && diverged;
    }

    private readonly record struct JournalEventRow(int Step, string Kind, long EventID, string Source, int PayloadLength, string LineSHA256);

    private static string ExpectedJournalKind(LoopClosureLinkSpecies species)
        => species switch
        {
            LoopClosureLinkSpecies.PreferenceDivergence => "policy-decision",
            LoopClosureLinkSpecies.InterventionDivergence or LoopClosureLinkSpecies.ExecutedDivergence => "mint",
            LoopClosureLinkSpecies.AuthorityEligible => "policy-decision",
            LoopClosureLinkSpecies.BoundaryAdmitted => "policy-boundary",
            _ => throw new InvalidDataException("unknown loop-closure link species"),
        };

    private static string ExpectedAttemptJournalKind(LoopClosureLinkAttempt attempt)
    {
        if (attempt.State == LoopClosureLinkStates.Denied)
            return attempt.Species == LoopClosureLinkSpecies.PreferenceDivergence
                && attempt.DenialReason == LoopClosureGateDenialReasons.NoOrganicOpportunity
                ? "loop-closure-organic-opportunity" : "policy-decision";
        if (!string.IsNullOrWhiteSpace(attempt.EvidenceRunID)
            && (attempt.Species is LoopClosureLinkSpecies.InterventionDivergence or LoopClosureLinkSpecies.AuthorityEligible))
            return "policy-decision";
        return ExpectedJournalKind(attempt.Species);
    }

    private static bool TryDecodeOrganicPreference(ReadOnlySpan<byte> payload)
        => TryDecodeOrganicPolicyDecision(payload, out CortexPolicyDecisionReadout readout)
            && (readout.RawCandidateAction < 0 || readout.RawCandidateAction == readout.LaunchpadAction);

    private static bool TryDecodeOrganicPolicyDecision(ReadOnlySpan<byte> payload, out CortexPolicyDecisionReadout readout)
    {
        readout = default;
        try
        {
            if (!TapePacketCreator.TryDecodePolicyDecision(payload, out CortexPolicyDecisionPacket packet))
                return false;
            readout = packet.Readout;
            return readout.SelectionCause is CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate;
        }
        catch (InvalidDataException) { return false; }
    }

    private static bool TryResolvePaidDivergenceProof(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<PolicyBoundaryDivergenceAdjudication> proofs,
        LoopClosureQuotaID fundingID,
        out PolicyBoundaryDivergenceAdjudication match)
    {
        string text = Encoding.ASCII.GetString(payload);
        match = default;
        if (!text.StartsWith("POLICY-FUNDED-DISSENT\t", StringComparison.Ordinal)
            || !TryReadLinkPacketField(text, "funding", out string funding)
            || !TryReadLinkPacketField(text, "readout", out string readout)
            || !TryReadLinkPacketField(text, "revision", out string revision)
            || !TryReadLinkPacketField(text, "execution", out string execution)) return false;
        foreach (PolicyBoundaryDivergenceAdjudication proof in proofs)
        {
            if (!string.Equals(funding, proof.Proof.Funding.QuotaDecisionID.ToString(), StringComparison.Ordinal)
                || !string.Equals(readout, $"u:{proof.Proof.ReadoutFingerprint:X16}", StringComparison.Ordinal)
                || !string.Equals(revision, proof.Proof.ReadoutRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                || !string.Equals(fundingID.Value, proof.Proof.Funding.QuotaDecisionID.ToString(), StringComparison.Ordinal)
                || !string.Equals(execution, proof.Proof.ForkReceipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "none", StringComparison.Ordinal)) continue;
            match = proof;
            return true;
        }
        return false;
    }

    private static bool TryResolveBoundaryProof(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<PolicyBoundaryDivergenceAdjudication> proofs,
        LoopClosureQuotaID fundingID,
        out PolicyBoundaryDivergenceAdjudication match)
    {
        match = default;
        string text = Encoding.ASCII.GetString(payload);
        if (!text.StartsWith("POLICY-BOUNDARY\t", StringComparison.Ordinal)) return false;
        foreach (PolicyBoundaryDivergenceAdjudication proof in proofs)
        {
            if (!string.Equals(proof.Proof.Funding.QuotaDecisionID.ToString(), fundingID.Value, StringComparison.Ordinal)) continue;
            match = proof;
            return true;
        }
        return false;
    }

    private static bool TryReadLinkPacketField(string text, string name, out string value)
    {
        string marker = "\t" + name + "=";
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { value = ""; return false; }
        start += marker.Length;
        int end = text.IndexOf('\t', start);
        value = end < 0 ? text[start..] : text[start..end];
        return value.Length > 0;
    }

    private static bool MatchesForkReceiptDigest(PolicyBoundaryForkReceipt receipt, string expected)
        => PolicyBoundaryObligation.ComputeReceiptDigest(in receipt) == expected;

    private static IReadOnlyList<JournalEventRow> ReadJournalEventRows(string path)
    {
        List<JournalEventRow> events = [];
        foreach (string line in File.ReadLines(path))
        {
            if (line == Journal.LogHeader || line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (fields.Length < 3 || !int.TryParse(fields[0], out int step)
                || fields[2].Length < 2 || fields[2][0] != 's'
                || !long.TryParse(fields[2].AsSpan(1), out long eventID) || eventID < 0)
                continue;
            int payloadLength = fields.Length >= 5 && fields[4].EndsWith("B", StringComparison.Ordinal)
                && int.TryParse(fields[4][..^1], out int parsedLength) ? parsedLength : -1;
            events.Add(new(step, fields[1], eventID, fields.Length >= 4 ? fields[3] : "", payloadLength,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)))));
        }
        return events;
    }

    public ClosureCertificate Certify(LoopClosureReport report, LoopClosureRegistration registration,
        string liveDirectory, string controlDirectory, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(domain);
        registration.ValidateFrozenAuthority(domain);
        string live = ResolveRun(liveDirectory);
        string control = ResolveRun(controlDirectory);
        RunAuthority liveAuthority = ReadCustodiedAuthority(registration, live, LoopClosureArms.Live);
        RunAuthority controlAuthority = ReadCustodiedAuthority(registration, control, LoopClosureArms.Control);
        if (!string.Equals(report.RegistrationSHA256, registration.Digest, StringComparison.Ordinal)
            || !string.Equals(report.Live.AuthoritySHA256, liveAuthority.Digest, StringComparison.Ordinal)
            || !string.Equals(report.Control.AuthoritySHA256, controlAuthority.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("closure certificate report identity differs from its frozen registration or sealed arms");
        bool liveClosed = liveAuthority.ClosureMatches(live, out string liveError);
        bool controlClosed = controlAuthority.ClosureMatches(control, out string controlError);
        if (!liveClosed || !controlClosed)
            throw new InvalidDataException($"closure certificate arm closure changed: LIVE={liveError}; CONTROL={controlError}");
        return ClosureCertificate.Create(report, registration);
    }

    // Keep the mechanism seam assembly-local: caller-supplied digests and snapshots
    // cannot mint a public-looking registered receipt outside the custody path above.
    internal LoopLineageAdjudication AdjudicateLineage(string authoritySHA256, LoopLineageTapeSnapshot source,
        IReadOnlyList<LoopLineageEdgeReceipt> receipts, string journalSHA256 = "", string domain = LoopLineageVerifier.NullDomain)
        => LoopLineageVerifier.VerifyShuffledPredecessorNull(authoritySHA256, source, receipts, journalSHA256, domain);

    private static string ResolveRun(string path)
        => Run.Resolve(path) ?? throw new DirectoryNotFoundException($"loop-closure run directory was not found: {path}");

    private static RunAuthority ReadCustodiedAuthority(LoopClosureRegistration registration, string directory, LoopClosureArms arm)
    {
        string registrationPath = Path.Combine(directory, LoopClosureRegistration.AuthorityFileName);
        if (!File.Exists(registrationPath) || !registration.Encode().AsSpan().SequenceEqual(File.ReadAllBytes(registrationPath)))
            throw new InvalidDataException("loop-closure arm does not carry the exact frozen registration bytes");
        RunAuthority authority = RunAuthority.Load(directory);
        if (!string.Equals(authority.Binary.ProcessName, registration.AppHost, StringComparison.Ordinal)
            || !string.Equals(authority.Binary.ProcessSHA256, registration.AppHostSHA256, StringComparison.Ordinal)
            || !string.Equals(authority.Binary.AssemblyName, registration.Assembly, StringComparison.Ordinal)
            || !string.Equals(authority.Binary.AssemblySHA256, registration.AssemblySHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure arm authority binary differs from the registered apphost/assembly");
        if (authority.WorldSHA256 != registration.WorldSHA256 || authority.Checkpoint.NextStep != registration.Horizon)
            throw new InvalidDataException("loop-closure arm authority disagrees with the registered world or horizon");
        string expected = arm == LoopClosureArms.Live ? registration.LiveConfigSHA256 : registration.ControlConfigSHA256;
        if (authority.PersistedConfigDigest != expected || authority.ConfigFingerprint != registration.ArmNeutralConfigSHA256)
            throw new InvalidDataException("loop-closure arm authority config differs from the registered arm");
        return authority;
    }

    private static LoopClosureArmReport ToArmReport(RunAuthority authority)
    {
        string closure = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('|', authority.Artifacts.Select(static item => item.RelativePath + ":" + item.SHA256)))));
        string binary = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('|', authority.Binary.ProcessName, authority.Binary.ProcessSHA256, authority.Binary.AssemblyName, authority.Binary.AssemblySHA256))));
        return new(authority.RunID, authority.ConfigFingerprint, authority.WorldSHA256, authority.Digest,
            authority.Checkpoint.PhysicalChainSHA256, closure, binary, authority.Checkpoint.NextStep);
    }

    private static List<LoopClosurePairLineVerdict> BuildPairLines(string live, string control, IPolicyBoundaryDomain domain)
    {
        string pairedPath = Path.Combine(Path.GetDirectoryName(live)!, $".loop-closure-paired-{Path.GetFileName(live)}-{Path.GetFileName(control)}.ron");
        try
        {
            PairedGateReport paired = PairedGateAdjudicator.Adjudicate(live, control, domain, pairedPath);
            return paired.Lines.Select(line => new LoopClosurePairLineVerdict(
                line.Name == "decider" ? "inference" : line.Name,
                line.Assay == PairedGateAssayStatuses.Exact ? LoopClosureAssayStatuses.Exact : LoopClosureAssayStatuses.Invalid,
                line.Power == PairedGatePowerStatuses.Powered ? LoopClosurePowerStatuses.Powered : LoopClosurePowerStatuses.Unpowered,
                line.Status switch { PairedGateVerdictStatuses.PASS => LoopClosureVerdictStatuses.PASS, PairedGateVerdictStatuses.FAIL => LoopClosureVerdictStatuses.FAIL, PairedGateVerdictStatuses.BANKED_NULL => LoopClosureVerdictStatuses.BANKED_NULL, _ => LoopClosureVerdictStatuses.INVALID },
                IsDigest(line.EvidenceDigest) ? new LoopClosureDigest(line.EvidenceDigest) : DigestText($"paired evidence missing|{line.Name}|{line.Detail}"))).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            LoopClosureDigest digest = DigestText(ex.Message);
            string[] names = ["vocabulary", "efficiency", "derivation", "inference", "vow", "zero-dark", "organism"];
            return names
                .Select(name => new LoopClosurePairLineVerdict(name, LoopClosureAssayStatuses.Invalid, LoopClosurePowerStatuses.Unpowered, LoopClosureVerdictStatuses.INVALID, digest)).ToList();
        }
        finally
        {
            if (File.Exists(pairedPath)) File.Delete(pairedPath);
        }
    }

    private static List<LoopClosureVerdict> BuildMechanismVerdicts(
        string registrationDigest,
        string live,
        string control,
        IReadOnlyList<LoopClosurePairLineVerdict> lines,
        LoopClosureLineageNullOutcome lineageNull,
        IPolicyBoundaryDomain domain)
    {
        // Canonical certification reads immutable one-record-per-opportunity evidence
        // from the sealed LIVE arm. No adjudication request carries corroboration claims;
        // every promoted arc must therefore come from the persisted arm custody.
        // Frozen digest token witness; identifier-side name is Corroboration.
        LoopClosureDigest digest = DigestText($"missing mechanism witness|{registrationDigest}|{live}|{control}");
        string runID = Path.GetFileName(Path.GetFullPath(live));
        PatternBecameThoughtCorroboration? persistedPattern = null;
        ThoughtOverruledInstinctCorroboration? persistedDivergence = null;
        ObjectLoopClosedCorroboration? persistedObject = null;
        LoopClosureR4Provenance? selectedR4 = null;
        PolicyBoundaryDivergenceAdjudication? selectedDivergenceProof = null;
        bool theoryEvidenceValid = false;
        bool divergenceEvidenceValid = false;
        bool objectEvidenceValid = false;
        RunAuthority liveAuthority = RunAuthority.Load(live);
        try
        {
            IReadOnlyList<PatternBecameThoughtCorroboration> theories = LoopClosureEvidenceStore.ReadPattern(live, runID);
            Func<CortexPolicyID, IPolicyBoundaryDomain> resolveDomain = LoopClosureEvidenceStore.ResolveRegisteredDomain(domain);
            IReadOnlyList<ThoughtOverruledInstinctCorroboration> divergences = LoopClosureEvidenceStore.ReadDivergence(live, runID, resolveDomain);
            IReadOnlyList<ObjectLoopClosedCorroboration> objects = LoopClosureEvidenceStore.ReadObject(live, runID);
            IReadOnlyList<LoopClosureR4Provenance> r4Records = LoopClosureEvidenceStore.ReadR4(live, runID);
            IReadOnlyList<PolicyBoundaryDivergenceAdjudication> divergenceProofs = LoopClosureEvidenceStore.ReadDivergenceProof(live, runID, resolveDomain);
            foreach (PatternBecameThoughtCorroboration theoryCandidate in theories)
            {
                if (!LoopClosureEvidenceCustody.VerifyPattern(live, liveAuthority, in theoryCandidate, out _)) continue;
                persistedPattern = theoryCandidate;
                theoryEvidenceValid = true;
                break;
            }
            foreach (ThoughtOverruledInstinctCorroboration divergenceCandidate in divergences)
            {
                PolicyBoundaryDivergenceAdjudication? proofCandidate = divergenceProofs.FirstOrDefault(candidate =>
                    candidate.Proof.Funding.QuotaDecisionID.ToString() == divergenceCandidate.QuotaID.Value);
                if (proofCandidate is not PolicyBoundaryDivergenceAdjudication accepted
                    || accepted.Proof.Provenance is not LoopClosureR4Provenance proofR4) continue;
                LoopClosureR4Provenance? r4Candidate = r4Records.FirstOrDefault(candidate =>
                    candidate.Episode.EpisodeDigest == proofR4.Episode.EpisodeDigest
                    && candidate.Fold.ReceiptDigest == proofR4.Fold.ReceiptDigest
                    && candidate.Teacher.ProvenanceDigest == proofR4.Teacher.ProvenanceDigest
                    && candidate.Training.ReadoutTrainingCorroborationSHA256 == proofR4.Training.ReadoutTrainingCorroborationSHA256
                    && candidate.Training.DecisionID.Equals(proofR4.Training.DecisionID));
                if (r4Candidate is not LoopClosureR4Provenance r4) continue;
                PatternBecameThoughtCorroboration? theoryCandidate = theories.FirstOrDefault(candidate =>
                    candidate.CompositionNodeID.Value == r4.Episode.EpisodeID.Value);
                if (theoryCandidate is not PatternBecameThoughtCorroboration chainPattern
                    || !LoopClosureEvidenceCustody.VerifyDivergence(live, liveAuthority, in r4, in chainPattern, in accepted, domain, out _)) continue;
                persistedPattern = chainPattern;
                theoryEvidenceValid = true;
                persistedDivergence = divergenceCandidate;
                selectedDivergenceProof = accepted;
                selectedR4 = r4;
                divergenceEvidenceValid = true;
                LoopClosureDigest theoryEvidence = LoopClosureEvidenceStore.DigestPattern(in chainPattern);
                foreach (ObjectLoopClosedCorroboration objectCandidate in objects)
                {
                    if (objectCandidate.PatternEvidenceSHA256 != theoryEvidence
                        || objectCandidate.DivergenceEvidenceSHA256 != accepted.EvidenceSHA256
                        || !LoopClosureEvidenceCustody.Verify(live, liveAuthority, in r4, in chainPattern,
                            accepted, in objectCandidate, domain, out _)) continue;
                    persistedObject = objectCandidate;
                    objectEvidenceValid = true;
                    break;
                }
                break;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            theoryEvidenceValid = false;
            divergenceEvidenceValid = false;
            objectEvidenceValid = false;
        }
        // Certification is source-owned: caller corroborationes are retained on the
        // request only for schema compatibility and can never promote a run.
        PatternBecameThoughtCorroboration? theorySource = persistedPattern;
        ThoughtOverruledInstinctCorroboration? divergenceSource = persistedDivergence;
        ObjectLoopClosedCorroboration? objectSource = persistedObject;
        LoopClosurePairLineVerdict derivationLine = lines.Single(static line => line.Name == "derivation");
        LoopClosurePairLineVerdict inferenceLine = lines.Single(static line => line.Name == "inference");
        PatternBecameThoughtVerdict theory = theoryEvidenceValid && theorySource is PatternBecameThoughtCorroboration theoryVerdictCorroboration
            ? new(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Powered, LoopClosureVerdictStatuses.PASS, LoopClosureEvidenceStore.DigestPattern(in theoryVerdictCorroboration), theoryVerdictCorroboration)
            : new(derivationLine.Assay, derivationLine.Power, MissingMechanismStatus(in derivationLine), derivationLine.EvidenceSHA256, default);
        LoopClosureDigest verdictDivergenceEvidence = selectedDivergenceProof is PolicyBoundaryDivergenceAdjudication acceptedDivergence
            ? acceptedDivergence.EvidenceSHA256
            : divergenceSource is ThoughtOverruledInstinctCorroboration divergenceCorroboration
                // Frozen digest prefix dissent; identifier-side name is Divergence.
                ? DigestText("dissent|" + divergenceCorroboration)
                : digest;
        ThoughtOverruledInstinctVerdict divergence = divergenceEvidenceValid && selectedR4 is not null
            && selectedDivergenceProof is not null && divergenceSource is ThoughtOverruledInstinctCorroboration
            ? new(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Powered, LoopClosureVerdictStatuses.PASS, verdictDivergenceEvidence, divergenceSource.Value)
            : new(inferenceLine.Assay, inferenceLine.Power, MissingMechanismStatus(in inferenceLine), inferenceLine.EvidenceSHA256, default);
        ObjectLoopClosedVerdict closed = objectEvidenceValid && objectSource is ObjectLoopClosedCorroboration objectVerdictCorroboration
            && lineageNull is LoopClosureLineageNullExecuted
            ? new(LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Powered, LoopClosureVerdictStatuses.PASS, DigestText("object|" + objectVerdictCorroboration), objectVerdictCorroboration)
            : MissingObjectVerdict(theory, divergence, lineageNull);
        return [
            theory, divergence, closed,
        ];
    }

    private static LoopClosureVerdictStatuses MissingMechanismStatus(in LoopClosurePairLineVerdict line)
        => line.Status switch
        {
            LoopClosureVerdictStatuses.BANKED_NULL => LoopClosureVerdictStatuses.BANKED_NULL,
            LoopClosureVerdictStatuses.INVALID => LoopClosureVerdictStatuses.INVALID,
            _ => LoopClosureVerdictStatuses.FAIL,
        };

    private static ObjectLoopClosedVerdict MissingObjectVerdict(
        PatternBecameThoughtVerdict theory,
        ThoughtOverruledInstinctVerdict divergence,
        LoopClosureLineageNullOutcome lineageNull)
    {
        bool invalid = theory.Status == LoopClosureVerdictStatuses.INVALID
            || divergence.Status == LoopClosureVerdictStatuses.INVALID
            || (theory.Status == LoopClosureVerdictStatuses.PASS
                && divergence.Status == LoopClosureVerdictStatuses.PASS
                && lineageNull is not LoopClosureLineageNullExecuted);
        bool banked = !invalid && (theory.Status == LoopClosureVerdictStatuses.BANKED_NULL
            || divergence.Status == LoopClosureVerdictStatuses.BANKED_NULL);
        LoopClosureVerdictStatuses status = invalid
            ? LoopClosureVerdictStatuses.INVALID
            : banked ? LoopClosureVerdictStatuses.BANKED_NULL : LoopClosureVerdictStatuses.FAIL;
        LoopClosurePowerStatuses power = status == LoopClosureVerdictStatuses.BANKED_NULL
            ? LoopClosurePowerStatuses.Unpowered
            : theory.Power == LoopClosurePowerStatuses.Powered || divergence.Power == LoopClosurePowerStatuses.Powered
                ? LoopClosurePowerStatuses.Powered : LoopClosurePowerStatuses.Unpowered;
        LoopClosureAssayStatuses assay = invalid ? LoopClosureAssayStatuses.Invalid : LoopClosureAssayStatuses.Exact;
        string lineageStatus = lineageNull.IsExecuted ? "executed" : "missing";
        string lineageEvidence = lineageNull switch
        {
            LoopClosureLineageNullExecuted executed => string.Join(':',
                executed.Receipt.OriginalLineageSHA256,
                executed.Receipt.ShuffledLineageSHA256,
                executed.Receipt.PermutationSHA256),
            LoopClosureLineageNullMissing missing => missing.Reason,
            _ => throw new InvalidDataException("unknown loop-closure lineage-null species"),
        };
        LoopClosureDigest evidence = DigestText(string.Join('|',
            "object-loop-missing",
            theory.Status, theory.Power, theory.EvidenceSHA256.Value,
            divergence.Status, divergence.Power, divergence.EvidenceSHA256.Value,
            lineageStatus, lineageEvidence));
        return new(assay, power, status, evidence, default);
    }

    private static LoopClosureLineageNullOutcome BuildMissingLineageNull(RunAuthority authority, string directory)
    {
        return new LoopClosureLineageNullMissing($"missing lineage view|{authority.Digest}|{directory}");
    }

    private static LoopClosureLineageNullOutcome BuildLineageNull(LoopClosureRegistration registration, RunAuthority authority, string directory)
    {
        try
        {
            using Tape tape = Checkpoint.LoadTape(directory);
            LoopLineageTapeSnapshot source = LoopLineageTapeSnapshot.Capture(tape);
            IReadOnlyList<LoopLineageEdgeReceipt> persistedReceipts = LoopLineageVerifier.ReadTapeEdges(tape);
            if (persistedReceipts.Count == 0) return BuildMissingLineageNull(authority, directory);
            string journalPath = Path.Combine(directory, "journal.log");
            if (!File.Exists(journalPath)) return BuildMissingLineageNull(authority, directory);
            if (!LoopLineageVerifier.VerifyPacketBijection(source, persistedReceipts, out string packetFailure))
                return new LoopClosureLineageNullMissing($"invalid persisted lineage packet custody|{packetFailure}");
            if (!LoopLineageVerifier.VerifyJournalLineageRows(source, journalPath, out string journalFailure))
                return new LoopClosureLineageNullMissing($"invalid persisted lineage journal custody|{journalFailure}");
            LoopLineageAdjudication persisted = LoopLineageVerifier.VerifyShuffledPredecessorNull(
                source, persistedReceipts, File.ReadLines(journalPath).ToArray(), registration.LineageNullDomain);
            return new LoopClosureLineageNullExecuted(persisted.NullReceipt);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new LoopClosureLineageNullMissing($"invalid persisted lineage|{authority.Digest}|{ex.Message}");
        }
    }

    private static LoopClosureDigest DigestText(string text)
        => new(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));

    private static bool IsDigest(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void EnsureOutsideRuns(string output, string live, string control)
    {
        string full = Path.GetFullPath(output);
        foreach (string run in new[] { Path.GetFullPath(live), Path.GetFullPath(control) })
            if (full.StartsWith(run + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(full, run, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("loop-closure report must be outside both immutable arm directories");
    }
}
