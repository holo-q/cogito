namespace Cogito.Cli;

using System.CommandLine;
using Cogito;

internal static class GateCommands
{
    internal static Command Paired()
    {
        Option<string> seed = new("--seed") { Required = true, Description = "registered primary seed (hex; 0x prefix optional)" };
        Option<int> steps = new("--steps") { Required = true, Description = "planned Cortex steps (> 0)" };
        Option<string> corpus = new("--corpus") { Required = true, Description = "corpus file or directory" };
        Option<string?> seeds = new("--seeds") { Description = "exactly two report-only secondary seeds, comma-separated (hex)" };
        Command command = new("paired", "run the fresh live/control paired gate sequentially in one process")
        {
            seed,
            steps,
            corpus,
            seeds,
        };
        command.SetAction(parse =>
        {
            string? secondaryRaw = parse.GetValue(seeds);
            string[] secondary = string.IsNullOrWhiteSpace(secondaryRaw)
                ? []
                : secondaryRaw.Split(',', StringSplitOptions.TrimEntries);
            return PairedGateRunner.Run(new PairedGateRunner.Request(
                parse.GetValue(seed)!,
                parse.GetValue(steps),
                parse.GetValue(corpus)!,
                secondary), Console.Out, HomeostatPolicyBoundaryDomain.Instance);
        });
        return command;
    }

    internal static Command Adjudicate()
    {
        Argument<string> live = new("live-dir") { Description = "completed live arm run directory" };
        Argument<string> control = new("control-dir") { Description = "completed control arm run directory" };
        Option<string?> report = new("--report") { Description = "deterministic RON report destination (outside both run dirs)" };
        Command command = new("adjudicate", "read-only seven-view adjudication of one paired run") { live, control, report };
        command.SetAction(parse =>
        {
            string liveDirectory = parse.GetValue(live)!;
            string controlDirectory = parse.GetValue(control)!;
            string reportPath = parse.GetValue(report) ?? $"paired-gate-{Run.RunIDFromDirectory(liveDirectory)}-{Run.RunIDFromDirectory(controlDirectory)}.ron";
            PairedGateReport verdict = PairedGateAdjudicator.Adjudicate(
                liveDirectory, controlDirectory, HomeostatPolicyBoundaryDomain.Instance, reportPath);
            Console.WriteLine($"  paired gate adjudicate · outcome={verdict.Outcome} · report={reportPath} · digest={verdict.Digest}");
            return verdict.Lines.All(static line => line.Status == PairedGateVerdictStatuses.PASS) ? 0 : 1;
        });
        return command;
    }

    internal static Command LoopClosure()
    {
        Option<string> seed = new("--seed") { Required = true, Description = "registered primary seed (hex; must be 0xC0117011)" };
        Option<int> steps = new("--steps") { Required = true, Description = "registered physical horizon (must be 500)" };
        Option<string> corpus = new("--corpus") { Required = true, Description = "materialized registered world (data/code)" };
        Option<string> registrationPath = new("--registration") { Required = true, Description = "sealed schema-v5 loop-closure-registration.ron" };
        Command command = new("loop-closure", "run the schema-v5 pre-registered live/control loop-closure pair") { seed, steps, corpus, registrationPath };
        command.SetAction(parse =>
        {
            string token = parse.GetValue(seed)!;
            if (!token.Equals("0xC0117011", StringComparison.OrdinalIgnoreCase) && !token.Equals("C0117011", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("loop-closure --seed must be 0xC0117011");
            if (parse.GetValue(steps) != LoopClosureRegistration.RegisteredHorizon)
                throw new ArgumentException("loop-closure --steps must be the registered horizon 500");
            LoopClosureRegistration registration = LoopClosureRegistration.Load(parse.GetValue(registrationPath)!, HomeostatPolicyBoundaryDomain.Instance);
            string root = Path.GetDirectoryName(Path.GetFullPath(parse.GetValue(registrationPath)!)) ?? ".";
            string live = Path.Combine(root, "loop-closure-c0117011_live");
            string control = Path.Combine(root, "loop-closure-c0117011_control");
            LoopClosureRunner runner = new();
            string registrationFile = parse.GetValue(registrationPath)!;
            LoopClosureRunResult liveResult = runner.Run(new(registration, LoopClosureArms.Live, parse.GetValue(corpus)!, live, registrationFile), HomeostatPolicyBoundaryDomain.Instance);
            Console.WriteLine($"  {liveResult.RenderLine()}");
            if (liveResult.Status != LoopClosureRunStatuses.Sealed) return 1;
            LoopClosureRunResult controlResult = runner.Run(new(registration, LoopClosureArms.Control, parse.GetValue(corpus)!, control, registrationFile), HomeostatPolicyBoundaryDomain.Instance);
            Console.WriteLine($"  {controlResult.RenderLine()}");
            return controlResult.Status == LoopClosureRunStatuses.Sealed ? 0 : 1;
        });
        return command;
    }

    internal static Command LoopClosureProbe()
    {
        Option<string> corpus = new("--corpus") { Required = true, Description = "explicit fixed world data/code path" };
        Option<int?> steps = new("--steps") { Description = "diagnostic probe horizon (default 500; capped at the registered horizon)" };
        Command command = new("loop-closure-probe", "run the fixed world-fed LIVE loop-closure mechanism probe") { corpus, steps };
        command.SetAction(parse => LoopClosureRunner.RunWorldFedProbe(
            parse.GetValue(corpus)!, parse.GetValue(steps) ?? LoopClosureRegistration.RegisteredHorizon, Console.Out,
            HomeostatPolicyBoundaryDomain.Instance));
        return command;
    }

    internal static Command CertifyLoopClosureProbe()
    {
        Argument<string> run = new("run-dir") { Description = "completed world-fed loop-closure probe run directory" };
        Option<string> corpus = new("--corpus") { Required = true, Description = "explicit fixed world data/code path used by the sealed run" };
        Command command = new("certify-loop-closure-probe", "read-only recheck and recertify a completed world-fed mechanism probe") { run, corpus };
        command.SetAction(parse => LoopClosureRunner.CertifyWorldFedProbe(
            parse.GetValue(run)!, parse.GetValue(corpus)!, Console.Out, HomeostatPolicyBoundaryDomain.Instance));
        return command;
    }

    internal static Command RegisterLoopClosure()
    {
        Option<string> corpus = new("--corpus") { Required = true, Description = "materialized registered world (data/code)" };
        Option<string> registration = new("--registration") { Required = true, Description = "schema-v5 loop-closure-registration.ron destination" };
        Option<string> root = new("--root") { DefaultValueFactory = _ => ".", Description = "repository root containing docs/plans/loop-closure-birth-certificate-v2.md and src/Cogito" };
        Command command = new("register-loop-closure", "mint and seal the schema-v5 loop-closure-registration.ron before either arm starts") { corpus, registration, root };
        command.SetAction(parse =>
        {
            LoopClosureRegistration result = LoopClosureRegistrationBuilder.Mint(new(
                parse.GetValue(registration)!, parse.GetValue(corpus)!, parse.GetValue(root)!), HomeostatPolicyBoundaryDomain.Instance);
            string registrationPath = Path.GetFullPath(parse.GetValue(registration)!);
            string bundledAppHost = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(registrationPath) ?? ".", result.AuthorityBundlePath,
                result.AuthorityBundleAppHostPath.Replace('/', Path.DirectorySeparatorChar)));
            Console.WriteLine($"  gate register-loop-closure · registration={registrationPath} · bundle-apphost={bundledAppHost} · digest={result.Digest} · seed=0x{result.Seed:X8} · horizon={result.Horizon}");
            return 0;
        });
        return command;
    }

    internal static Command Certify()
    {
        Argument<string> live = new("live-dir") { Description = "sealed loop-closure LIVE arm" };
        Argument<string> control = new("control-dir") { Description = "sealed loop-closure CONTROL arm" };
        Option<string> registrationPath = new("--registration") { Required = true, Description = "sealed schema-v5 loop-closure-registration.ron" };
        Option<string?> reportPath = new("--report") { Description = "LoopClosureReport RON output (outside both arms)" };
        Command command = new("certify", "read-only adjudicate and certify a sealed schema-v5 loop-closure pair") { live, control, registrationPath, reportPath };
        command.SetAction(parse =>
        {
            string liveDirectory = parse.GetValue(live)!;
            string controlDirectory = parse.GetValue(control)!;
            string registrationFile = parse.GetValue(registrationPath)!;
            string output = parse.GetValue(reportPath) ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(liveDirectory)) ?? ".", "loop-closure-report.ron");
            string terminal = LoopClosureCertificationTerminalOutcome.PathAdjacentToReport(output, liveDirectory, controlDirectory);
            LoopClosureRegistration? registration = null;
            LoopClosureReport? report = null;
            IPolicyBoundaryDomain domain = HomeostatPolicyBoundaryDomain.Instance;
            try
            {
                // Keep registration and path checks in an explicit preflight phase so
                // a malformed authority still leaves a typed terminal receipt.
                registration = LoopClosureRegistration.Load(registrationFile, domain);
                registration.AssertPersistedBytes(registrationFile);
                registration.ValidateFrozenAuthority(domain);
                RequireSealedLoopClosureTerminal(liveDirectory, LoopClosureArms.Live);
                RequireSealedLoopClosureTerminal(controlDirectory, LoopClosureArms.Control);
            }
            catch (Exception ex)
            {
                return FinishCertificationTerminal(
                    LoopClosureCertificationStatuses.Failed,
                    LoopClosureCertificationCauses.PreflightException,
                    "preflight",
                    registrationFile, liveDirectory, controlDirectory, output, terminal,
                    registration, report, ex, domain);
            }

            LoopClosureAdjudicator adjudicator = new();
            try
            {
                report = adjudicator.Adjudicate(
                    new(registration, liveDirectory, controlDirectory, output, registrationFile),
                    HomeostatPolicyBoundaryDomain.Instance.PolicyBinding,
                    domain);
            }
            catch (Exception ex)
            {
                return FinishCertificationTerminal(
                    LoopClosureCertificationStatuses.Failed,
                    LoopClosureCertificationCauses.AdjudicatorException,
                    "adjudicator",
                    registrationFile, liveDirectory, controlDirectory, output, terminal,
                    registration, report, ex, domain);
            }

            if (!report.CanMintClosureCertificate)
            {
                FinishCertificationTerminal(
                    LoopClosureCertificationStatuses.Failed,
                    LoopClosureCertificationCauses.AdjudicatorRejected,
                    "adjudicator",
                    registrationFile, liveDirectory, controlDirectory, output, terminal,
                    registration, report, null, domain);
                Console.WriteLine($"  gate certify · artifact={report.ArtifactName} · report={output} · digest={report.Digest}");
                RenderLoopClosureSummary(report);
                return 1;
            }

            try
            {
                ClosureCertificate certificate = adjudicator.Certify(report, registration, liveDirectory, controlDirectory, domain);
                if (!certificate.Encode().AsSpan().SequenceEqual(File.ReadAllBytes(output)))
                    throw new InvalidDataException("persisted BirthCertificate bytes differ from the certified source-backed report");
                FinishCertificationTerminal(
                    LoopClosureCertificationStatuses.Completed,
                    LoopClosureCertificationCauses.Completed,
                    "completed",
                    registrationFile, liveDirectory, controlDirectory, output, terminal,
                    registration, report, null, domain);
                Console.WriteLine($"  gate certify · artifact={certificate.ArtifactName} · report={output} · digest={report.Digest}");
                RenderLoopClosureSummary(report);
                return 0;
            }
            catch (Exception ex)
            {
                return FinishCertificationTerminal(
                    LoopClosureCertificationStatuses.Failed,
                    LoopClosureCertificationCauses.CertificationException,
                    "certification",
                    registrationFile, liveDirectory, controlDirectory, output, terminal,
                    registration, report, ex, domain);
            }
        });
        return command;
    }

    private static int FinishCertificationTerminal(
        LoopClosureCertificationStatuses status,
        LoopClosureCertificationCauses cause,
        string phase,
        string registrationPath,
        string livePath,
        string controlPath,
        string reportPath,
        string terminalPath,
        LoopClosureRegistration? registration,
        LoopClosureReport? report,
        Exception? error,
        IPolicyBoundaryDomain domain)
    {
        LoopClosureCertificationTerminalOutcome outcome = LoopClosureCertificationTerminalOutcome.Capture(
            status, cause, phase, registrationPath, livePath, controlPath, reportPath, terminalPath, domain, registration, report, error);
        LoopClosureCertificationTerminalOutcome.TryWrite(terminalPath, in outcome);
        if (status == LoopClosureCertificationStatuses.Failed)
            Console.WriteLine($"  {outcome.RenderLine()}");
        return status == LoopClosureCertificationStatuses.Completed ? 0 : 1;
    }

    private static void RenderLoopClosureSummary(LoopClosureReport report)
    {
        foreach (LoopClosurePairLineVerdict line in report.Lines)
            Console.WriteLine($"  paired {line.Name} · assay={line.Assay} · power={line.Power} · status={line.Status}");

        foreach (LoopClosureVerdictSpecies species in Enum.GetValues<LoopClosureVerdictSpecies>())
        {
            LoopClosureVerdict verdict = report.Verdicts.Single(verdict => verdict.Species == species);
            Console.WriteLine($"  mechanism {species} · assay={verdict.Assay} · power={verdict.Power} · status={verdict.Status}");
        }

        int linkCount = report.LinkReceipts.Count;
        Console.WriteLine($"  links · canonical-prefix={linkCount}/{LoopClosureLinkContract.OrderedSpecies.Count}");
        foreach (LoopClosureLinkReceipt receipt in report.LinkReceipts)
            Console.WriteLine($"  link {receipt.Species} · state={receipt.State} · path={receipt.Path}");

        if (report.OrganicComparisons is { } organic)
            Console.WriteLine($"  organic-comparisons · eligible={organic.EligibleDecisions} · comparisons={organic.Comparisons} · funding-denied={organic.FundingDenied} · no-match={organic.CompletedNoMatch} · agreement={organic.CandidateAgreements} · divergence={organic.CandidateDivergences} · source={organic.SourceAuthoritySHA256} · stream={organic.StreamSHA256}");
        else
            Console.WriteLine("  organic-comparisons · unavailable=legacy-schema-v2");

        switch (report.LineageNull)
        {
            case LoopClosureLineageNullExecuted executed:
                LoopLineageShuffledNullReceipt receipt = executed.Receipt;
                Console.WriteLine($"  lineage-null · outcome=Executed · original-status={receipt.OriginalStatus} · shuffled-status={receipt.ShuffledStatus} · reason=shuffled-predecessor");
                break;
            case LoopClosureLineageNullMissing missing:
                Console.WriteLine($"  lineage-null · outcome=Missing · reason={missing.Reason}");
                break;
            default:
                throw new InvalidDataException("unknown loop-closure lineage-null species");
        }

        Console.WriteLine(report.CanMintClosureCertificate
            ? "  BirthCertificate minted"
            : "  BirthCertificate withheld");
    }

    private static void RequireSealedLoopClosureTerminal(string runDirectory, LoopClosureArms arm)
    {
        if (!LoopClosureTerminalOutcome.TryReadSealed(runDirectory, arm, out LoopClosureTerminalOutcome? outcome)
            || outcome is null)
            throw new InvalidDataException($"loop-closure {arm} arm is missing its external sealed terminal receipt: {LoopClosureTerminalOutcome.PathFor(runDirectory)}");
    }

    internal static Command LineageFixture()
    {
        Command command = new("lineage-fixture", "exercise the real lineage tape/journal turnstile and shuffled-predecessor null");
        command.SetAction(_ => LoopLineageVerifier.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command AdjudicatorFixture()
    {
        Command command = new("adjudicator-fixture", "exercise deterministic typed paired adjudicator evidence and certification terminal custody");
        command.SetAction(_ => PairedGateAdjudicator.VerifyFixture(Console.Out, HomeostatPolicyBoundaryDomain.Instance)
            && LoopClosureCertificationTerminalOutcome.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command RunnerFixture()
    {
        Command command = new("runner-fixture", "exercise paired runner identity, retry contracts, and external loop-closure terminals");
        command.SetAction(_ => PairedGateRunner.VerifyFixture(Console.Out, HomeostatPolicyBoundaryDomain.Instance)
            && LoopClosureTerminalOutcome.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command WorldFixture()
    {
        Command command = new("world-fixture", "exercise same-world identity, selected-glob guards, and resumable world admission");
        command.SetAction(_ => FileCorpus.VerifyWorldIdentityFixture(Console.Out)
            && AdmissionCursor.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command WorldNoveltyFixture()
    {
        Command command = new("world-novelty-fixture", "run the focused immutable epoch world probe and separately registered order null");
        command.SetAction(_ => WorldEpochNoveltyProbe.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command WorldNoveltyScheduleFixture()
    {
        Command command = new("world-novelty-schedule-fixture", "verify immutable epoch order, checkpoint custody, and resumed cursor suffix");
        command.SetAction(_ => WorldEpochNoveltyProbe.VerifyScheduleFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command FuelScheduleFixture()
    {
        Command command = new("fuel-schedule-fixture", "exercise arm-neutral paired fuel prefixes, refunds, resume, and tamper refusal");
        command.SetAction(_ => EmlPairedFuelScheduleJournal.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command TapeRoleFixture()
    {
        Command command = new("tape-role-fixture", "verify role-preserving tape custody, grammar intake parity, and the rung-0 observer null");
        command.SetAction(_ => TapeRoleBoundaryFixture.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    /// G1's kill-line: the tool seam must record every look as custody and feed the grammar only
    /// what it could not already generate — with the disarmed arm proving the zero is the gate's.
    internal static Command RepositoryIntakeNullGate()
    {
        Command command = new("repository-intake-null", "verify the re-grep renormalization null at the tool-intake seam");
        command.SetAction(_ => RepositoryIntakeNull.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    /// G2's kill-line: steering by prediction error must beat chance, and steering against it must
    /// lose to chance, at exactly matched fuel on the real repository.
    internal static Command RepositorySurpriseNullGate()
    {
        Command command = new("repository-surprise-null", "verify prediction-error steering beats chance beats anti at matched fuel");
        command.SetAction(_ => RepositorySurpriseNull.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    /// G5's kill-line: criticality on the repository's own history replayed forward, against the
    /// same world held still.
    internal static Command RepositoryHistoryCriticalityNullGate()
    {
        Command command = new("repository-history-null", "verify criticality holds on a moving world and sinks on a static re-feed");
        command.SetAction(_ => RepositoryHistoryCriticalityNull.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    /// G4's kill-line: does closure make the next discovery cheaper — the price of understanding the
    /// epoch ahead, cycle after cycle, against the same organism with its memory taken away.
    internal static Command RepositoryCompoundingNullGate()
    {
        Command command = new("repository-compounding-null", "verify closure makes the next discovery cheaper at matched target");
        command.SetAction(_ => RepositoryCompoundingNull.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    /// G6's kill-line: query-conditioned discrimination against the idf class that killed its four
    /// predecessors, on held-out localization in the real repository.
    internal static Command RepositorySharpnessNullGate()
    {
        Command command = new("repository-sharpness-null", "verify query-seeded activation beats the idf class on held-out localization");
        command.SetAction(_ => RepositorySharpnessNull.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    /// G3's kill-line: one registration, three arms at matched fuel, and only the live arm may
    /// close the loop — the blocked arm because the world told it nothing, the shuffled arm
    /// because its evidence was deranged.
    internal static Command RepositoryLoopClosureNullGate()
    {
        Option<int> steps = new("--steps") { DefaultValueFactory = _ => RepositoryLoopClosureNull.DefaultSteps, Description = "horizon each arm runs to (matched across arms by construction)" };
        Command command = new("repository-loop-closure-null", "adjudicate the three tool arms at matched fuel against one registration") { steps };
        command.SetAction(parse => RepositoryLoopClosureNull.Verify(Console.Out, parse.GetValue(steps)) ? 0 : 1);
        return command;
    }

    /// The carrier G6's death pointed at: structure selected for uniqueness rather than recurrence,
    /// against the same idf class on the same held-out questions.
    internal static Command RepositoryIdiolectNullGate()
    {
        Command command = new("repository-idiolect-null", "verify uniqueness-selected structure beats the idf class where recurrence-selected structure lost");
        command.SetAction(_ => RepositoryIdiolectNull.Verify(Console.Out) ? 0 : 1);
        return command;
    }

    internal static Command RegistrationFixture()
    {
        Command command = new("registration-fixture", "exercise v2 immutable authority-bundle copy, identity, rehash, and tamper refusal");
        command.SetAction(_ => LoopClosureRegistration.VerifySourceTreeCensusFixture(Console.Out)
            && LoopClosureAuthorityBundleStore.VerifyFixture(Console.Out) ? 0 : 1);
        return command;
    }
}
