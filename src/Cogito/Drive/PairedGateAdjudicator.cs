namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// Read-only views over one fresh live/control run pair. The run artifacts remain the
/// authority; this type only derives a deterministic report and never opens a drive for
/// mutation or writes below either run directory.
public static class PairedGateAdjudicator
{
    public static PairedGateReport Adjudicate(string liveDirectory, string controlDirectory, IPolicyBoundaryDomain domain, string? reportPath = null)
        => Adjudicate(liveDirectory, controlDirectory, domain, reportPath, beforeClosureRecheck: null);

    private static PairedGateReport Adjudicate(string liveDirectory, string controlDirectory, IPolicyBoundaryDomain domain, string? reportPath,
        Action<string>? beforeClosureRecheck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlDirectory);
        ArgumentNullException.ThrowIfNull(domain);
        domain.ArmTopology.Validate();
        string live = ResolveDirectory(liveDirectory);
        string control = ResolveDirectory(controlDirectory);

        // Each arm is an authority-bound snapshot. The completed authority
        // seals the initial closure; a cheap path+digest recheck after all
        // artifact readers finish preserves the intra-invocation stability
        // corroboration without replaying the arm seven times.
        ArmRead liveArm = ReadArm(live, beforeClosureRecheck);
        ArmRead controlArm = ReadArm(control, beforeClosureRecheck);
        bool fullReadExact = liveArm.Closure.Exact && controlArm.Closure.Exact;
        List<PairedGateLineVerdict> lines = new(7);
        bool pairExact = liveArm.Error.Length == 0 && controlArm.Error.Length == 0;
        string pairError = pairExact ? ValidatePair(liveArm.Authority!, controlArm.Authority!, domain) : JoinErrors(liveArm, controlArm);

        // A second read is the assay's stability corroboration. Once source evidence
        // moved, dependent lines are Invalid; the drift is not a powered failure.
        bool closureDrift = (liveArm.Authority is not null && !liveArm.Closure.Exact)
            || (controlArm.Authority is not null && !controlArm.Closure.Exact);
        if (closureDrift)
        {
            string detail = "arm evidence changed after the authority closure snapshot; assay is not stable: "
                + JoinErrors(liveArm, controlArm);
            foreach (string name in new[] { "vocabulary", "efficiency", "derivation", "decider", "vow", "zero-dark", "organism" })
                lines.Add(Invalid(name, detail));
            return WriteReport(liveArm, controlArm, lines, "assay evidence drifted", reportPath, live, control);
        }

        EmlStandardAnytimeCurveSummary? liveCurve = liveArm.Curve;
        EmlStandardAnytimeCurveSummary? controlCurve = controlArm.Curve;
        EmlAnytimePairedComparison comparison = default;
        if (pairExact && liveCurve is not null && controlCurve is not null)
        {
            string curvePairError = ValidateCurvePair(liveCurve, controlCurve, liveArm, controlArm);
            if (curvePairError.Length > 0) pairError = pairError.Length == 0 ? curvePairError : pairError + "; " + curvePairError;
            if (curvePairError.Length == 0 && liveArm.PairedSchedule is { } liveSchedule
                && liveArm.PairedScheduleCursor is { } liveCursor
                && controlArm.PairedSchedule is { } controlSchedule
                && controlArm.PairedScheduleCursor is { } controlCursor)
                comparison = EmlStandardAnytimeCurveReader.ComparePairedSchedule(
                    liveCurve, controlCurve, in liveSchedule, in controlSchedule, in liveCursor, in controlCursor);
            else if (!RequiresPairedSchedule(liveArm.Directory, controlArm.Directory))
            {
                EmlDeliberationQuota liveBudget = liveArm.Authority!.DeliberationBudget;
                EmlDeliberationQuota controlBudget = controlArm.Authority!.DeliberationBudget;
                int commonHorizon = liveArm.Authority!.Checkpoint.NextStep == controlArm.Authority!.Checkpoint.NextStep
                    ? liveArm.Authority.Checkpoint.NextStep : 0;
                comparison = EmlStandardAnytimeCurveReader.Compare(liveCurve, controlCurve, in liveBudget, in controlBudget, commonHorizon);
            }
            else
                comparison = new(false, false, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, null, null,
                    false, false, false, false, 0, "paired fuel schedule is missing or invalid");
            if (!comparison.Comparable && IsPairedScheduleIntegrityFailure(comparison.Failure))
            {
                pairError = pairError.Length == 0 ? comparison.Failure : pairError + "; " + comparison.Failure;
                pairExact = false;
            }
            pairExact = pairExact && curvePairError.Length == 0;
        }

        pairExact = pairExact && pairError.Length == 0;

        lines.Add(BuildCurveLine("vocabulary", pairExact, liveCurve, controlCurve, comparison, comparison.VocabularyPass, "live exact-class knee exceeds control and crosses 63-class wall", "paired vocabulary threshold did not pass", pairError));
        lines.Add(BuildCurveLine("efficiency", pairExact, liveCurve, controlCurve, comparison, comparison.EfficiencyPass, "live evaluator-calls per certified capture is no worse", "paired efficiency threshold did not pass", pairError));

        lines.Add(BuildCompositionLine(liveArm.Rung0, pairExact, pairError, domain));
        lines.Add(BuildDeciderLine(liveArm.Vitals, controlArm.Vitals, pairExact, pairError));
        lines.Add(BuildVowLine(liveArm, controlArm, fullReadExact, pairExact, pairError));
        lines.Add(BuildAccountingLine(liveArm.Compute, controlArm.Compute, liveArm.Vitals, controlArm.Vitals, pairExact, pairError));
        lines.Add(BuildOrganismLine(liveArm.Vitals, controlArm.Vitals, pairExact, pairError));

        bool allPass = lines.All(static line => line.Status == PairedGateVerdictStatuses.PASS);
        return WriteReport(liveArm, controlArm, lines, allPass ? "7/7 green" : "typed nulls retained", reportPath, live, control);
    }

    private static PairedGateReport WriteReport(ArmRead liveArm, ArmRead controlArm, IReadOnlyList<PairedGateLineVerdict> lines,
        string outcome, string? reportPath, string live, string control)
    {
        PairedGateReport report = PairedGateReport.Create(liveArm.ToReport(), controlArm.ToReport(), lines, outcome);
        reportPath ??= $"paired-gate-{Path.GetFileName(live)}-{Path.GetFileName(control)}.ron";
        string output = Path.GetFullPath(reportPath);
        EnsureOutsideRuns(output, live, control);
        byte[] encoded = report.Encode();
        if (File.Exists(output))
        {
            byte[] existing = File.ReadAllBytes(output);
            if (!existing.AsSpan().SequenceEqual(encoded))
                throw new IOException($"paired adjudication report destination differs from the current arm evidence: {output} — refusing to overwrite");
            return ReadReport(output);
        }
        if (Directory.Exists(output))
            throw new IOException($"paired adjudication report destination is a directory: {output}");
        string? parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(output, encoded);
        return report;
    }

    internal static PairedGateReport ReadReport(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        string path = Path.GetFullPath(reportPath);
        if (!File.Exists(path)) throw new FileNotFoundException("paired adjudication report is missing", path);
        if (Directory.Exists(path)) throw new IOException($"paired adjudication report is a directory: {path}");
        PairedGateRON document = RonSerializer.Deserialize<PairedGateRON>(File.ReadAllBytes(path));
        if (document.schemaVersion != 1 || document.live is null || document.control is null || document.lines is null)
            throw new InvalidDataException("paired adjudication report schema is invalid");
        List<PairedGateLineVerdict> lines = new(document.lines.Count);
        foreach (PairedGateRONLine line in document.lines)
        {
            if (!Enum.TryParse(line.assay, out PairedGateAssayStatuses assay)
                || !Enum.TryParse(line.power, out PairedGatePowerStatuses power)
                || !Enum.TryParse(line.status, out PairedGateVerdictStatuses status))
                throw new InvalidDataException("paired adjudication report carries an unknown typed verdict");
            lines.Add(new(line.name, assay, power, status, line.detail, line.evidenceDigest));
        }
        ArmReport live = new(document.live.runID, document.live.configFingerprint, document.live.worldSHA256,
            document.live.authorityDigest, document.live.checkpointDigest, document.live.computeDigest,
            document.live.closureDigest, document.live.binaryDigest, Counts(document.live.plannedFuel), Counts(document.live.actualFuel), Counts(document.live.refundFuel), document.live.fuelHorizon,
            document.live.scheduleDigest, document.live.schedulePrefixStep, document.live.scheduleHorizon);
        ArmReport control = new(document.control.runID, document.control.configFingerprint, document.control.worldSHA256,
            document.control.authorityDigest, document.control.checkpointDigest, document.control.computeDigest,
            document.control.closureDigest, document.control.binaryDigest, Counts(document.control.plannedFuel), Counts(document.control.actualFuel), Counts(document.control.refundFuel), document.control.fuelHorizon,
            document.control.scheduleDigest, document.control.schedulePrefixStep, document.control.scheduleHorizon);
        PairedGateReport report = PairedGateReport.Create(live, control, lines, document.outcome);
        if (!string.Equals(report.NextAdmissibleExperiment, document.next_admissible_experiment, StringComparison.Ordinal))
            throw new InvalidDataException("paired adjudication report next experiment paragraph does not match its ordered typed lines");
        if (!string.Equals(report.Digest, document.digest, StringComparison.Ordinal))
            throw new InvalidDataException("paired adjudication report digest does not match its payload");
        return report;
    }

    /// Rebinds a persisted report to the complete, current arm identities before a
    /// runner trusts it. Run IDs alone are not authority: every report identity field
    /// must still agree with the read-only arm evidence.
    internal static void ValidateReportIdentity(PairedGateReport report, string liveDirectory, string controlDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        string live = ResolveDirectory(liveDirectory);
        string control = ResolveDirectory(controlDirectory);
        ArmReport expectedLive = ReadRequiredArmReport(live, "live");
        ArmReport expectedControl = ReadRequiredArmReport(control, "control");
        RequireExactArmIdentity(report.Live, expectedLive, "live", live);
        RequireExactArmIdentity(report.Control, expectedControl, "control", control);
    }

    /// Rebinds a report to sealed historical arms without asking the current
    /// checkpoint serializer to reproduce bytes emitted by another binary.
    /// Every report field still comes from authority-covered artifacts.
    internal static void ValidateHistoricalReportIdentity(PairedGateReport report, string liveDirectory, string controlDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        string live = ResolveDirectory(liveDirectory);
        string control = ResolveDirectory(controlDirectory);
        RequireExactArmIdentity(report.Live, ReadHistoricalArmReport(live), "historical live", live);
        RequireExactArmIdentity(report.Control, ReadHistoricalArmReport(control), "historical control", control);
    }

    private static ArmReport ReadRequiredArmReport(string directory, string role)
    {
        ArmRead arm = ReadArm(directory);
        if (arm.Error.Length > 0 || arm.Authority is null)
            throw new IOException($"paired {role} arm identity cannot be established for report reuse: {directory} — {arm.Error}");
        return arm.ToReport();
    }

    private static ArmReport ReadHistoricalArmReport(string directory)
    {
        AuthorityFiles authorityFiles = CaptureAuthorityFiles(directory);
        RunAuthority authority = RunAuthority.LoadIdentity(directory);
        RequireSemanticFiles(directory);
        (EmlPairedFuelSchedule? pairedSchedule, EmlPairedFuelScheduleCursor? pairedCursor) = ReadPairedFuelSchedule(directory, authority);
        EmlStandardAnytimeCurveSummary curve = EmlStandardAnytimeCurveReader.Read(Path.Combine(directory, "eml_anytime_curve.tsv"));
        ValidateCurveIdentity(curve, authority);
        ComputeRead compute = ReadCompute(Path.Combine(directory, "compute.tsv"), authority.Checkpoint.NextStep);
        if (!authority.ClosureMatches(directory, out string closureError))
            throw new InvalidDataException($"historical arm closure changed: {closureError}");
        if (!AuthorityFilesMatch(directory, in authorityFiles, out string authorityError))
            throw new InvalidDataException(authorityError);
        return ArmReport.FromHistorical(authority, compute.Digest, curve, pairedSchedule, pairedCursor);
    }

    private static void RequireExactArmIdentity(ArmReport actual, ArmReport expected, string role, string directory)
    {
        if (actual.RunID != expected.RunID
            || actual.ConfigFingerprint != expected.ConfigFingerprint
            || actual.WorldSHA256 != expected.WorldSHA256
            || actual.AuthorityDigest != expected.AuthorityDigest
            || actual.CheckpointDigest != expected.CheckpointDigest
            || actual.ComputeDigest != expected.ComputeDigest
            || actual.ClosureDigest != expected.ClosureDigest
            || actual.BinaryDigest != expected.BinaryDigest
            || actual.PlannedFuel != expected.PlannedFuel
            || actual.ActualFuel != expected.ActualFuel
            || actual.RefundFuel != expected.RefundFuel
            || actual.FuelHorizon != expected.FuelHorizon
            || actual.ScheduleDigest != expected.ScheduleDigest
            || actual.SchedulePrefixStep != expected.SchedulePrefixStep
            || actual.ScheduleHorizon != expected.ScheduleHorizon)
            throw new IOException($"paired adjudication report {role} arm identity is stale or forged for current evidence: {directory}");
    }

    private static EmlDeliberationCounts Counts(PairedGateRONFuel value) => new(value.candidateEvaluations, value.logicalProgramPoints, value.executedProgramPoints,
        value.inverseTransforms, value.hashProbes, value.joinAttempts, value.joinHits, value.processTerms, value.verifierProgramPoints,
        value.candidateSupplyItems, value.lawRewriteApplications, value.lawRewriteTreeNodes);

    public static bool VerifyFixture(TextWriter output, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(domain);
        string root = Path.GetFullPath(Path.Combine(".tmp", $"paired-gate-adjudicator-fixture-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(root);
        try
        {
            string corpus = Path.Combine(root, "corpus");
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "corpus.txt"), "alpha beta gamma\n");
            string world = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
            CortexConfig liveConfig = FixtureConfig(corpus, world, 0xDEADBEF2UL, domain, control: false);
            CortexConfig controlConfig = FixtureConfig(corpus, world, 0xDEADBEF2UL, domain, control: true);
            Run live = Run.Create(Path.Combine(root, "live"));
            Run control = Run.Create(Path.Combine(root, "control"));
            bool drove = new Cortex(liveConfig).Run(live) == 0 && new Cortex(controlConfig).Run(control) == 0;
            if (!drove)
            {
                output.WriteLine("  paired-gate adjudicator fixture · synthetic runs failed to land · FAIL");
                return false;
            }
            EnsureSemanticHeaders(live);
            EnsureSemanticHeaders(control);
            RunAuthority.WriteCompleted(Run.Open(live.Dir), Checkpoint.PeekConfig(live.Dir), Checkpoint.NextStep(live.Dir));
            RunAuthority.WriteCompleted(Run.Open(control.Dir), Checkpoint.PeekConfig(control.Dir), Checkpoint.NextStep(control.Dir));

            Dictionary<string, string> liveBefore = SnapshotFiles(live.Dir);
            Dictionary<string, string> controlBefore = SnapshotFiles(control.Dir);
            string firstPath = Path.Combine(root, "first.ron"), secondPath = Path.Combine(root, "second.ron");
            PairedGateReport first = Adjudicate(live.Dir, control.Dir, domain, firstPath);
            PairedGateReport rerun = Adjudicate(live.Dir, control.Dir, domain, firstPath);
            PairedGateReport second = Adjudicate(live.Dir, control.Dir, domain, secondPath);
            bool sameOutputRerun = rerun.Digest == first.Digest
                && File.ReadAllBytes(firstPath).AsSpan().SequenceEqual(rerun.Encode());
            bool deterministic = File.ReadAllBytes(firstPath).AsSpan().SequenceEqual(File.ReadAllBytes(secondPath));
            PairedGateReport loaded = ReadReport(firstPath);
            bool validEvidence = first.Lines.Count == 7 && first.Live.WorldSHA256 == world && first.Control.WorldSHA256 == world
                && loaded.Digest == first.Digest && loaded.NextAdmissibleExperiment == first.NextAdmissibleExperiment
                && loaded.Live.PlannedFuel == first.Live.PlannedFuel && loaded.Control.RefundFuel == first.Control.RefundFuel;

            string tamperedPath = Path.Combine(root, "tampered.ron");
            string tamperedText = File.ReadAllText(firstPath).Replace(first.NextAdmissibleExperiment, first.NextAdmissibleExperiment + " tampered", StringComparison.Ordinal);
            File.WriteAllText(tamperedPath, tamperedText);
            bool ronTamperRejected;
            try
            {
                _ = ReadReport(tamperedPath);
                ronTamperRejected = false;
            }
            catch (InvalidDataException)
            {
                ronTamperRejected = true;
            }

            string corruptDir = Path.Combine(root, "corrupt-live");
            CopyDirectory(live.Dir, corruptDir);
            string corruptCompute = Path.Combine(corruptDir, "compute.tsv");
            byte[] corruptBytes = File.ReadAllBytes(corruptCompute); corruptBytes[^1] ^= 1; File.WriteAllBytes(corruptCompute, corruptBytes);
            PairedGateReport corrupt = Adjudicate(corruptDir, control.Dir, domain, Path.Combine(root, "corrupt.ron"));
            bool corruptTyped = corrupt.Lines.All(static line => line.Assay == PairedGateAssayStatuses.Invalid && line.Status == PairedGateVerdictStatuses.INVALID);

            string closureParent = Path.Combine(root, "closure-mutation");
            Directory.CreateDirectory(closureParent);
            string closureControl = Path.Combine(closureParent, Path.GetFileName(control.Dir));
            CopyDirectory(control.Dir, closureControl);
            bool mutationArmed = true;
            PairedGateReport closureDrift = Adjudicate(live.Dir, closureControl, domain, Path.Combine(root, "closure-drift.ron"), directory =>
            {
                if (!mutationArmed || !directory.Equals(closureControl, StringComparison.Ordinal)) return;
                File.AppendAllText(Path.Combine(directory, "compute.tsv"), "\n");
                mutationArmed = false;
            });
            bool closureTyped = !mutationArmed && closureDrift.Lines.All(static line => line.Assay == PairedGateAssayStatuses.Invalid
                && line.Status == PairedGateVerdictStatuses.INVALID);

            string authorityParent = Path.Combine(root, "authority-mutation");
            Directory.CreateDirectory(authorityParent);
            string authorityControl = Path.Combine(authorityParent, Path.GetFileName(control.Dir));
            CopyDirectory(control.Dir, authorityControl);
            bool authorityMutationArmed = true;
            PairedGateReport authorityDrift = Adjudicate(live.Dir, authorityControl, domain, Path.Combine(root, "authority-drift.ron"), directory =>
            {
                if (!authorityMutationArmed || !directory.Equals(authorityControl, StringComparison.Ordinal)) return;
                File.AppendAllText(Path.Combine(directory, RunAuthority.DigestFileName), "tampered\n");
                authorityMutationArmed = false;
            });
            bool authorityTyped = !authorityMutationArmed && authorityDrift.Lines.All(static line => line.Assay == PairedGateAssayStatuses.Invalid
                && line.Status == PairedGateVerdictStatuses.INVALID);

            string zeroDir = Path.Combine(root, "zero-live");
            CopyDirectory(live.Dir, zeroDir);
            string curvePath = Path.Combine(zeroDir, "eml_anytime_curve.tsv");
            File.WriteAllText(curvePath, File.ReadLines(curvePath).First() + "\n");
            RunAuthority.WriteCompleted(Run.Open(zeroDir), Checkpoint.PeekConfig(live.Dir), Checkpoint.NextStep(zeroDir));
            PairedGateReport zero = Adjudicate(zeroDir, control.Dir, domain, Path.Combine(root, "zero.ron"));
            bool zeroTyped = zero.Lines.Any(static line => line.Name == "vocabulary" && line.Assay == PairedGateAssayStatuses.Exact
                && line.Power == PairedGatePowerStatuses.Unpowered && line.Status == PairedGateVerdictStatuses.BANKED_NULL)
                && zero.Lines.Any(static line => line.Name == "efficiency" && line.Status == PairedGateVerdictStatuses.BANKED_NULL);

            bool noRunWrites = liveBefore.SequenceEqual(SnapshotFiles(live.Dir)) && controlBefore.SequenceEqual(SnapshotFiles(control.Dir));
            bool scheduleFixture = VerifyPairedScheduleFixture(out string scheduleDetail);
            bool pass = validEvidence && sameOutputRerun && deterministic && ronTamperRejected && corruptTyped && closureTyped && authorityTyped && zeroTyped && noRunWrites && scheduleFixture;
            output.WriteLine($"  paired-gate adjudicator fixture · synthetic={(validEvidence ? "typed" : "BROKEN")} · rerun={(sameOutputRerun ? "accepted" : "REJECTED")} · ron-tamper={(ronTamperRejected ? "rejected" : "ACCEPTED")} · corrupt={(corruptTyped ? "Invalid" : "ACCEPTED")} · closure-drift={(closureTyped ? "Invalid" : "ACCEPTED")} · authority-drift={(authorityTyped ? "Invalid" : "ACCEPTED")} · zero-opportunity={(zeroTyped ? "typed-null" : "BROKEN")} · schedule={scheduleDetail} · deterministic={(deterministic ? "byte-exact" : "DRIFT")} · no-run-writes={(noRunWrites ? "yes" : "NO")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static bool VerifyPairedScheduleFixture(out string detail)
    {
        EmlDeliberationCounts total = new(8, 12, 16, 4, 20, 24, 8, 32, 12, 4, 8, 16);
        EmlPairedFuelSchedule schedule = EmlPairedFuelSchedule.Create("paired-adjudicator-fixture-v1", 4, in total);
        EmlPairedFuelScheduleCursor liveCursor = EmlPairedFuelScheduleCursor.Create(in schedule);
        EmlPairedFuelScheduleCursor controlCursor = EmlPairedFuelScheduleCursor.Create(in schedule);
        EmlDeliberationCounts zero = EmlDeliberationCounts.Zero;
        for (int step = 0; step < schedule.Horizon; step++)
        {
            EmlDeliberationCounts row = schedule.Row(step);
            liveCursor.Append(in schedule, step, in row, in row);
            controlCursor.Append(in schedule, step, in row, in zero);
        }
        EmlDeliberationCounts planned = liveCursor.Planned, liveActual = liveCursor.Actual, controlActual = controlCursor.Actual;
        EmlStandardAnytimeCurveSummary live = MakeScheduleFixtureCurve("live", schedule.Horizon, in planned, in liveActual);
        EmlStandardAnytimeCurveSummary control = MakeScheduleFixtureCurve("control", schedule.Horizon, in planned, in controlActual);
        EmlAnytimePairedComparison accepted = EmlStandardAnytimeCurveReader.ComparePairedSchedule(live, control, in schedule, in schedule, in liveCursor, in controlCursor);
        EmlPairedFuelScheduleCursor truncated = EmlPairedFuelScheduleCursor.Create(in schedule);
        EmlDeliberationCounts firstRow = schedule.Row(0);
        truncated.Append(in schedule, 0, in firstRow, in zero);
        bool truncatedRejected = !EmlStandardAnytimeCurveReader.ComparePairedSchedule(live, control, in schedule, in schedule, in truncated, in controlCursor).Comparable;
        EmlPairedFuelSchedule digestMismatch = EmlPairedFuelSchedule.Create("paired-adjudicator-fixture-tampered", 4, in total);
        EmlPairedFuelSchedule horizonMismatch = EmlPairedFuelSchedule.Create(schedule.Identity, 5, in total);
        EmlDeliberationCounts changedTotal = new(9, total.LogicalProgramPoints, total.ExecutedProgramPoints, total.InverseTransforms, total.HashProbes, total.JoinAttempts, total.JoinHits, total.ProcessTerms, total.VerifierProgramPoints, total.CandidateSupplyItems, total.LawRewriteApplications, total.LawRewriteTreeNodes);
        EmlPairedFuelSchedule totalMismatch = EmlPairedFuelSchedule.Create(schedule.Identity, 4, in changedTotal);
        bool mismatchRejected = !EmlStandardAnytimeCurveReader.ComparePairedSchedule(live, control, in schedule, in digestMismatch, in liveCursor, in controlCursor).Comparable
            && !EmlStandardAnytimeCurveReader.ComparePairedSchedule(live, control, in schedule, in horizonMismatch, in liveCursor, in controlCursor).Comparable
            && !EmlStandardAnytimeCurveReader.ComparePairedSchedule(live, control, in schedule, in totalMismatch, in liveCursor, in controlCursor).Comparable;

        string root = Path.GetFullPath(Path.Combine(".tmp", $"paired-adjudicator-schedule-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(root);
        try
        {
            string arm = Path.Combine(root, "gate-paired-fixture_live");
            Directory.CreateDirectory(arm);
            byte[] bytes = EmlPairedFuelScheduleJournal.Encode(in schedule, liveCursor);
            File.WriteAllBytes(Path.Combine(arm, EmlPairedFuelSchedule.SidecarFile), bytes);
            RunAuthority authority = new()
            {
                Checkpoint = new RunAuthorityCheckpoint { NextStep = schedule.Horizon },
                Artifacts = [new RunAuthorityArtifact { RelativePath = EmlPairedFuelSchedule.SidecarFile, SHA256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) }],
            };
            _ = ReadPairedFuelSchedule(arm, authority);
            File.WriteAllBytes(Path.Combine(arm, EmlPairedFuelSchedule.SidecarFile), Encoding.UTF8.GetBytes("tampered"));
            bool tamperRejected = false;
            try { _ = ReadPairedFuelSchedule(arm, authority); } catch (InvalidDataException) { tamperRejected = true; }
            string missing = Path.Combine(root, "gate-paired-fixture_missing");
            Directory.CreateDirectory(missing);
            bool missingRejected = false;
            try { _ = ReadPairedFuelSchedule(missing, authority); } catch (FileNotFoundException) { missingRejected = true; }
            detail = $"prefix={(accepted.Comparable && accepted.PlannedFuelMatched ? "equal" : "REJECTED")} lease-actual=unequal mismatch={(mismatchRejected ? "rejected" : "ACCEPTED")} truncated={(truncatedRejected ? "rejected" : "ACCEPTED")} sidecar={(tamperRejected && missingRejected ? "tamper+absent-rejected" : "BROKEN")}";
            return accepted.Comparable && accepted.PlannedFuelMatched && mismatchRejected && truncatedRejected && tamperRejected && missingRejected;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static EmlStandardAnytimeCurveSummary MakeScheduleFixtureCurve(string armID, int horizon, in EmlDeliberationCounts planned, in EmlDeliberationCounts actual)
    {
        EmlAnytimeCommitments quality = new(64, 1, 1, 1, 1, 1, 1, 1);
        EmlAnytimeCurvePoint point = new("point-" + armID, "", "digest-" + armID, "run-" + armID, "config-" + armID, "chain", armID, "", 0, horizon, 1, "fixture",
            quality, actual, planned, actual, 1, 1, 1, true, true, true, false, false, false, false, false, true, false, 0, "", 0, 1, 1, 0, "evidence-" + armID);
        EmlAnytimeEfficiency efficiency = EmlAnytimeEfficiency.Create(1, 1);
        return new EmlStandardAnytimeCurveSummary([point], point, point, planned, actual, EmlDeliberationCounts.Subtract(in planned, in actual), efficiency, point.Digest);
    }

    private static CortexConfig FixtureConfig(string corpus, string world, ulong seed, IPolicyBoundaryDomain domain, bool control)
    {
        domain.ArmTopology.Validate();
        CortexEmlCurriculum curriculum = new()
        {
            Corpus = new CogitoCorpus { Path = corpus, ExpectedWorldSHA256 = world },
            IntakeBatch = 4,
            ProcessCatalog = control ? domain.ArmTopology.ControlProcessCatalog : domain.ArmTopology.LiveProcessCatalog,
            Rung0 = control ? domain.ArmTopology.ControlRung0 : domain.ArmTopology.LiveRung0,
            Deliberation = control ? domain.ArmTopology.ControlDeliberation : domain.ArmTopology.LiveDeliberation,
            DeliberationBudget = EmlDeliberationQuota.PairedGateNominal,
            Actions = EmlActionSelections.ProcedureGuarded,
        };
        return new CortexConfig
        {
            RunName = "paired-gate-fixture",
            Seed = seed,
            Steps = 1,
            ActionsPerStep = 4,
            Curriculum = curriculum,
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    AuthorityCeiling = control ? domain.ArmTopology.ControlAuthority : domain.ArmTopology.LiveAuthorityCeiling,
                    TrialAllocation = control ? null : new CortexPolicyTrialAllocationConfig
                    {
                        ArmSteps = domain.ArmTopology.TrialArmSteps,
                        Authority = domain.ArmTopology.TrialAllocationAuthority,
                        Identity = domain.ArmTopology.TrialAllocationIdentity,
                    },
                },
            },
        };
    }

    private static void EnsureSemanticHeaders(Run run)
    {
        Dictionary<string, string> headers = new(StringComparer.Ordinal)
        {
            ["policy_trial_funding.journal.tsv"] = "funding_id\tpolicy\tcandidate_fingerprint\tfunding_step\trequested_horizon_steps\tarm_count\tplanned_arm_steps\treserved_arm_steps\tdecision\tcharged_steps\tremaining_budget\tcandidate_state\tdenial_reason\tcandidate_origin_step\tcandidate_current_step\tcandidate_required_step\tcandidate_revision\tallocation_identity\tallocation_digest\tallocation_arm_steps",
            ["policy_trial_settlements.journal.tsv"] = "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds",
            ["policy_readout_funding.journal.tsv"] = "funding_id\tpolicy\tcandidate_fingerprint\tgrammar_revision\tcontext_digest\tcontext_bytes\tdeliberation_depth\tfunding_step\tplanned_units\treserved_units\tdecision\tcharged_units\tremaining_budget\tallocation_sequence\troster_digest\taccount_balance_before\taccount_balance_after",
            ["policy_readout_settlements.journal.tsv"] = "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds",
            ["policy_readout_allocations.journal.tsv"] = "sequence\tstep\troster_digest\tpolicy\tbalance_before\tcredited_units\texpired_units\tbalance_after",
            ["policy_decisions.tsv"] = "step\tevent_id\tdecision_id\tpolicy\tlaunchpad_action\traw_candidate_action\tselected_candidate_action\texecuted_action\taction_count\tauthority\trevision\tselection_cause\tdrill\tpacket_base64",
        };
        foreach ((string name, string header) in headers)
        {
            string path = run.PathOf(name);
            if (!File.Exists(path)) File.WriteAllText(path, header + "\n");
        }
    }

    private static Dictionary<string, string> SnapshotFiles(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: false);
        }
    }

    private static PairedGateLineVerdict BuildCompositionLine(EmlOrdinaryRunRung0Receipt? receipt, bool pairExact, string pairError, IPolicyBoundaryDomain domain)
    {
        if (!pairExact || receipt is null) return Invalid("derivation", pairError.Length == 0 ? "ordinary rung-0 receipt is missing" : pairError);
        EmlOrdinaryRunRung0Receipt value = receipt.Value;
        if (!value.IsValid) return Invalid("derivation", "ordinary rung-0 receipt digest is invalid");
        if (value.Opportunities == 0)
            return new("derivation", PairedGateAssayStatuses.Exact, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.BANKED_NULL, "no rung-0 opportunity occurred", value.Digest);
        bool pass = value.Rung0 == domain.ArmTopology.LiveRung0 && value.Assay == EmlRematchAssayStatuses.Exact
            && value.Power == EmlRematchPowerStatuses.Powered && value.Compositions > 0
            && (value.SchemaVersion >= 3 ? value.AttemptedCandidates > 0 && value.Compositions <= value.AttemptedCandidates : value.Compositions <= value.Opportunities)
            && value.ZeroEvaluatorCompositions == value.Compositions && value.HasCleanSampledAudits
            && value.RelationNullExecutions > 0 && value.RelationNullDivergences == value.RelationNullExecutions
            && value.RelationNullAuthorityPredictions == 0;
        return Verdict("derivation", pass, PairedGatePowerStatuses.Powered,
            pass ? "armed rung-0 admits zero-evaluator derivation and clean shuffled null" : "rung-0 powered evidence did not pass", value.Digest);
    }

    private static PairedGateLineVerdict BuildDeciderLine(PairedGateVitalsReader.RunReadout? live, PairedGateVitalsReader.RunReadout? control, bool pairExact, string pairError)
    {
        if (!pairExact || live is null || control is null) return Invalid("decider", pairError.Length == 0 ? "policy evidence is incomplete" : pairError);
        PairedGateVitalsReader.RunReadout l = live.Value, c = control.Value;
        bool behaviorallyExecuted = l.Policy.HasBoundaryOpportunity && l.Policy.ForcedDivergentNullBehaviorExecuted;
        if (!behaviorallyExecuted) return new("decider", PairedGateAssayStatuses.Exact, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.BANKED_NULL, "policy boundary had no behaviorally executed forced-divergent null", "");
        bool pass = l.Policy.VerifiersPassed && l.Policy.PaidTrials > 0 && l.Policy.ReflexAdaptationZero
            && l.Homeostat.PaidGrammarOutcomes >= c.Homeostat.PaidGrammarOutcomes
            && l.Homeostat.WastedCloses <= c.Homeostat.WastedCloses;
        return Verdict("decider", pass, PairedGatePowerStatuses.Powered,
            pass ? "funded readout is non-inferior to reflex on paid/wasted closes" : "decider paid/wasted close comparison did not pass", DigestVitals(l, c));
    }

    private static PairedGateLineVerdict BuildVowLine(ArmRead live, ArmRead control, bool fullReadExact, bool pairExact, string pairError)
    {
        if (!pairExact) return Invalid("vow", pairError);
        bool pass = live.Vow.Passed && control.Vow.Passed && live.Authority!.Complete && control.Authority!.Complete
            && live.Authority.Checkpoint.SaveLoadSaveExact && control.Authority.Checkpoint.SaveLoadSaveExact
            && live.Closure.Exact && control.Closure.Exact && fullReadExact;
        string detail = !fullReadExact
            ? "full arm adjudication evidence changed between read-only passes"
            : pass ? "both checkpoint Vows and full arm evidence are stable" : "checkpoint Vow or manifest stability failed";
        return Verdict("vow", pass, PairedGatePowerStatuses.Powered, detail, DigestPair(live.Vow, control.Vow));
    }

    private static PairedGateLineVerdict BuildAccountingLine(ComputeRead live, ComputeRead control, PairedGateVitalsReader.RunReadout? liveVitals, PairedGateVitalsReader.RunReadout? controlVitals, bool pairExact, string pairError)
    {
        if (!pairExact) return Invalid("zero-dark", pairError);
        bool pass = live.Exact && control.Exact && liveVitals is not null && controlVitals is not null
            && liveVitals.Value.Policy.Trial.AccountingClosed && liveVitals.Value.Policy.ReadoutTrial.AccountingClosed
            && controlVitals.Value.Policy.Trial.AccountingClosed && controlVitals.Value.Policy.ReadoutTrial.AccountingClosed;
        return Verdict("zero-dark", pass, PairedGatePowerStatuses.Powered, pass ? "raw-tick phase coverage is contiguous with zero dark" : "compute accounting is malformed or dark", DigestPair(live.Digest, control.Digest));
    }

    private static PairedGateLineVerdict BuildOrganismLine(PairedGateVitalsReader.RunReadout? live, PairedGateVitalsReader.RunReadout? control, bool pairExact, string pairError)
    {
        if (!pairExact || live is null || control is null) return Invalid("organism", pairError.Length == 0 ? "vitals evidence is incomplete" : pairError);
        PairedGateVitalsReader.RhythmReadout l = live.Value.Rhythm, c = control.Value.Rhythm;
        bool band = InBand(l.Day, c.Day) && InBand(l.Replay, c.Replay) && InBand(l.ConsolidationPhase, c.ConsolidationPhase);
        if (!l.HasOpportunity) return new("organism", PairedGateAssayStatuses.Exact, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.BANKED_NULL, "rhythm arm had no opportunity", "");
        bool pass = live.Value.Homeostat.Closes >= 3 && live.Value.Homeostat.WastedCloses == 0 && l.DayPresent && l.ResidualThawed && band;
        return Verdict("organism", pass, PairedGatePowerStatuses.Powered,
            pass ? "live vitals meet paid-close, day, thaw, and co-registered 25% bands" : "live vitals did not meet the organism thresholds", DigestVitals(live.Value, control.Value));
    }

    private static bool InBand(long value, long control) => control == 0 ? value == 0 : Math.Abs(value - control) <= Math.Abs(control) * 0.25;
    private static PairedGateLineVerdict Verdict(string name, bool pass, PairedGatePowerStatuses power, string detail, string digest)
        => new(name, PairedGateAssayStatuses.Exact, power, pass ? PairedGateVerdictStatuses.PASS : PairedGateVerdictStatuses.FAIL, detail, digest);
    private static PairedGateLineVerdict Invalid(string name, string detail)
        => new(name, PairedGateAssayStatuses.Invalid, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.INVALID, detail, "");

    private static PairedGateLineVerdict BuildCurveLine(string name, bool pairExact, EmlStandardAnytimeCurveSummary? live, EmlStandardAnytimeCurveSummary? control, EmlAnytimePairedComparison comparison, bool pass, string passDetail, string failDetail, string pairError)
    {
        if (!pairExact || live is null || control is null) return Invalid(name, pairError.Length == 0 ? "standard EML curve evidence is incomplete" : pairError);
        if (!comparison.Comparable) return new(name, PairedGateAssayStatuses.Exact, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.BANKED_NULL, comparison.Failure, DigestPair(live, control));
        if (name == "efficiency" && !comparison.EfficiencyPowered)
            return new(name, PairedGateAssayStatuses.Exact, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.BANKED_NULL, "no certified captures at the common accepted horizon", DigestPair(live, control));
        return Verdict(name, pass, PairedGatePowerStatuses.Powered, pass ? passDetail : failDetail, DigestPair(live, control));
    }

    private static ArmRead ReadArm(string directory, Action<string>? beforeClosureRecheck = null)
    {
        try
        {
            AuthorityFiles authorityFiles = CaptureAuthorityFiles(directory);
            (byte[] effectiveImage, string basePhysical, string chain) = CheckpointDelta.ReadEffectiveSnapshot(directory);
            CheckpointVowReceipt vow = Cortex.VerifyReadOnlyCheckpointVow(directory, effectiveImage, basePhysical, chain);
            RunAuthority authority = RunAuthority.Load(directory, effectiveImage, vow);
            RequireSemanticFiles(directory);
            (EmlPairedFuelSchedule? pairedSchedule, EmlPairedFuelScheduleCursor? pairedCursor) = ReadPairedFuelSchedule(directory, authority);
            string curvePath = Path.Combine(directory, "eml_anytime_curve.tsv");
            EmlStandardAnytimeCurveSummary curve = EmlStandardAnytimeCurveReader.Read(curvePath);
            ValidateCurveIdentity(curve, authority);
            using Tape tape = Checkpoint.LoadTape(effectiveImage, directory);
            PairedGateVitalsReader.RunReadout vitals = PairedGateVitalsReader.Read(directory, tape);
            EmlOrdinaryRunRung0Receipt? rung0 = ReadRung0(directory);
            ComputeRead compute = ReadCompute(Path.Combine(directory, "compute.tsv"), authority.Checkpoint.NextStep);
            beforeClosureRecheck?.Invoke(directory);
            bool closureExact = authority.ClosureMatches(directory, out string closureError);
            bool authorityExact = AuthorityFilesMatch(directory, in authorityFiles, out string authorityError);
            ClosureRead closure = new(closureExact && authorityExact,
                closureExact ? authorityError : closureError);
            ArmRead arm = new(directory, authority, vow, curve, vitals, rung0, compute, pairedSchedule, pairedCursor,
                closure.Exact ? "" : closure.Error, closure, Array.Empty<byte>());
            return arm with { EvidenceBytes = EncodeEvidence(arm) };
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return new(directory, null, default, null, null, null, default, null, null, $"{error.GetType().Name}: {error.Message}", default, Array.Empty<byte>());
        }
    }

    private static (EmlPairedFuelSchedule? Schedule, EmlPairedFuelScheduleCursor? Cursor) ReadPairedFuelSchedule(string directory, RunAuthority authority)
    {
        string path = Path.Combine(directory, EmlPairedFuelSchedule.SidecarFile);
        bool registered = Path.GetFileName(directory).StartsWith("gate-paired-", StringComparison.Ordinal);
        if (!File.Exists(path))
        {
            if (registered) throw new FileNotFoundException("registered paired arm is missing its fuel schedule sidecar", path);
            return (null, null);
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (!authority.Artifacts.Any(item => item.RelativePath.Equals(EmlPairedFuelSchedule.SidecarFile, StringComparison.Ordinal)
                && string.Equals(item.SHA256, Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.Ordinal)))
            throw new InvalidDataException("paired fuel schedule sidecar is not sealed by run authority");
        (EmlPairedFuelSchedule schedule, EmlPairedFuelScheduleCursor cursor) = EmlPairedFuelScheduleJournal.Decode(bytes);
        schedule.Validate();
        cursor.Validate(in schedule);
        if (schedule.Horizon != authority.Checkpoint.NextStep)
            throw new InvalidDataException("paired fuel schedule horizon disagrees with authority checkpoint");
        return (schedule, cursor);
    }

    private static byte[] EncodeEvidence(ArmRead arm)
    {
        StringBuilder canonical = new();
        canonical.Append("arm-v2|").Append(arm.Error).Append('|');
        if (arm.Authority is RunAuthority authority)
        {
            canonical.Append(authority.Schema).Append('|').Append(authority.RunID).Append('|').Append(authority.ConfigFingerprint).Append('|')
                .Append(authority.PersistedConfigDigest).Append('|').Append(authority.Digest).Append('|').Append(authority.Complete).Append('|');
            canonical.Append(authority.Binary.ProcessName).Append('|').Append(authority.Binary.ProcessSHA256).Append('|')
                .Append(authority.Binary.AssemblyName).Append('|').Append(authority.Binary.AssemblySHA256).Append('|');
            foreach (RunAuthorityArtifact artifact in authority.Artifacts)
                canonical.Append(artifact.RelativePath).Append('=').Append(artifact.SHA256).Append('|');
            foreach (RunAuthorityOmission omission in authority.Omissions)
                canonical.Append("omit:").Append(omission.RelativePath).Append('=').Append(omission.Reason).Append('|');
            canonical.Append(authority.Checkpoint.LogicalSHA256).Append('|').Append(authority.Checkpoint.PhysicalSHA256).Append('|')
                .Append(authority.Checkpoint.BasePhysicalSHA256).Append('|').Append(authority.Checkpoint.PhysicalChainSHA256).Append('|')
                .Append(authority.Checkpoint.NextStep).Append('|').Append(authority.Checkpoint.SaveLoadSaveExact).Append('|');
            canonical.Append(authority.Switches.PolicyAuthorityCeiling).Append('|').Append(authority.Switches.ProcessCatalog).Append('|')
                .Append(authority.Switches.Rung0).Append('|').Append(authority.Switches.Deliberation).Append('|')
                .Append(authority.DeliberationBudget).Append('|');
        }
        canonical.Append(arm.Vow.Passed).Append('|').Append(arm.Vow.SectionsCompared).Append('|').Append(arm.Vow.EffectiveBytes).Append('|')
            .Append(arm.Vow.ReencodedBytes).Append('|').Append(arm.Vow.EffectivePhysicalSHA256).Append('|').Append(arm.Vow.ReencodedPhysicalSHA256).Append('|')
            .Append(arm.Vow.BasePhysicalSHA256).Append('|').Append(arm.Vow.ChainSHA256).Append('|').Append(arm.Vow.ManifestUnchanged).Append('|')
            .Append(string.Join(';', arm.Vow.Failures)).Append('|');
        if (arm.Curve is EmlStandardAnytimeCurveSummary curve)
        {
            canonical.Append(curve.RunID).Append('|').Append(curve.ConfigID).Append('|').Append(curve.ChainID).Append('|').Append(curve.ArmID).Append('|')
                .Append(curve.Rung).Append('|').Append(curve.Digest).Append('|').Append(curve.HasTerminal).Append('|')
                .Append(curve.PlannedFuel).Append('|').Append(curve.ActualFuel).Append('|').Append(curve.RefundFuel).Append('|').Append(curve.Efficiency).Append('|');
            foreach (EmlAnytimeCurvePoint point in curve.Points) canonical.Append(point.Digest).Append('|');
        }
        if (arm.PairedSchedule is { } schedule && arm.PairedScheduleCursor is { } cursor)
            canonical.Append("paired-schedule|").Append(schedule.Identity).Append('|').Append(schedule.Horizon).Append('|').Append(schedule.Digest).Append('|')
                .Append(schedule.Total).Append('|').Append(cursor.LastStep).Append('|').Append(cursor.RowCount).Append('|').Append(cursor.RowDigest).Append('|')
                .Append(cursor.CursorDigest).Append('|').Append(cursor.Planned).Append('|').Append(cursor.Actual).Append('|').Append(cursor.Refund).Append('|');
        if (arm.Vitals is PairedGateVitalsReader.RunReadout vitals)
        {
            canonical.Append(vitals.Rhythm).Append('|').Append(vitals.Homeostat).Append('|').Append(vitals.Policy).Append('|');
        }
        if (arm.Rung0 is EmlOrdinaryRunRung0Receipt rung0) canonical.Append(rung0.Canonical()).Append('|').Append(rung0.Digest).Append('|');
        canonical.Append(arm.Compute.Exact).Append('|').Append(arm.Compute.Rows).Append('|').Append(arm.Compute.Digest).Append('|').Append(arm.Compute.Error).Append('|')
            .Append(arm.Closure.Exact).Append('|').Append(arm.Closure.Error);
        return Encoding.UTF8.GetBytes(canonical.ToString());
    }

    private static AuthorityFiles CaptureAuthorityFiles(string directory)
    {
        byte[] authority = File.ReadAllBytes(Path.Combine(directory, RunAuthority.FileName));
        byte[] digest = File.ReadAllBytes(Path.Combine(directory, RunAuthority.DigestFileName));
        return new(authority, digest, Convert.ToHexStringLower(SHA256.HashData(authority)), Convert.ToHexStringLower(SHA256.HashData(digest)));
    }

    private static bool AuthorityFilesMatch(string directory, in AuthorityFiles expected, out string error)
    {
        try
        {
            byte[] authority = File.ReadAllBytes(Path.Combine(directory, RunAuthority.FileName));
            byte[] digest = File.ReadAllBytes(Path.Combine(directory, RunAuthority.DigestFileName));
            bool exact = string.Equals(Convert.ToHexStringLower(SHA256.HashData(authority)), expected.AuthoritySHA256, StringComparison.Ordinal)
                && string.Equals(Convert.ToHexStringLower(SHA256.HashData(digest)), expected.DigestSHA256, StringComparison.Ordinal)
                && authority.AsSpan().SequenceEqual(expected.AuthorityBytes)
                && digest.AsSpan().SequenceEqual(expected.DigestBytes);
            error = exact ? "" : "completed authority sidecar bytes changed after arm evidence was read";
            return exact;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            error = $"completed authority sidecar recheck failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static EmlOrdinaryRunRung0Receipt? ReadRung0(string directory)
    {
        string path = Path.Combine(directory, "journal.log");
        if (!File.Exists(path)) throw new FileNotFoundException("journal is missing for rung-0 receipt", path);
        EmlOrdinaryRunRung0Receipt? result = null;
        foreach (string line in File.ReadLines(path))
        {
            if (!line.Contains("\teml-rung0\t", StringComparison.Ordinal)) continue;
            Dictionary<string, string> fields = ParseFields(line);
            bool hasAgreed = fields.ContainsKey("agreed-audits");
            bool hasDisagreed = fields.ContainsKey("disagreed-audits");
            bool hasNotSelected = fields.ContainsKey("not-selected-audits");
            int schema = fields.TryGetValue("schema", out string? schemaText)
                && int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSchema)
                ? parsedSchema : 1;
            if (hasAgreed != hasDisagreed || hasAgreed != hasNotSelected)
                throw new InvalidDataException("ordinary rung-0 journal receipt has a partial audit status census");
            EmlOrdinaryRunRung0Receipt parsed = schema >= 3
                ? EmlOrdinaryRunRung0Receipt.Create(
                    Enum.Parse<EmlRung0Modes>(RequireField(fields, "rung0")),
                    Enum.Parse<EmlRematchAssayStatuses>(RequireField(fields, "assay")),
                    Enum.Parse<EmlRematchPowerStatuses>(RequireField(fields, "power")),
                    IntField(fields, "opportunities"), IntField(fields, "carrier-bound"), IntField(fields, "guard-eligible"),
                    IntField(fields, "funded-attempts"), IntField(fields, "attempted-candidates"), IntField(fields, "derivations"),
                    IntField(fields, "zero-evaluator"), IntField(fields, "audits"), IntField(fields, "agreed-audits"),
                    IntField(fields, "disagreed-audits"), IntField(fields, "not-selected-audits"), IntField(fields, "null-executions"),
                    IntField(fields, "null-divergences"), IntField(fields, "null-authority"), IntField(fields, "null-pairs-considered"),
                    IntField(fields, "null-pairs-created"), IntField(fields, "null-reject-no-carrier"), IntField(fields, "null-reject-shape"),
                    IntField(fields, "null-reject-grade"), RequireField(fields, "derivation"), RequireField(fields, "source"), RequireField(fields, "config"))
                : hasAgreed
                ? EmlOrdinaryRunRung0Receipt.Create(
                    Enum.Parse<EmlRung0Modes>(RequireField(fields, "rung0")),
                    Enum.Parse<EmlRematchAssayStatuses>(RequireField(fields, "assay")),
                    Enum.Parse<EmlRematchPowerStatuses>(RequireField(fields, "power")),
                    IntField(fields, "opportunities"), IntField(fields, "derivations"), IntField(fields, "zero-evaluator"), IntField(fields, "audits"),
                    IntField(fields, "agreed-audits"), IntField(fields, "disagreed-audits"), IntField(fields, "not-selected-audits"),
                    IntField(fields, "null-executions"), IntField(fields, "null-divergences"), IntField(fields, "null-authority"),
                    RequireField(fields, "derivation"), RequireField(fields, "source"), RequireField(fields, "config"))
                : EmlOrdinaryRunRung0Receipt.Create(
                    Enum.Parse<EmlRung0Modes>(RequireField(fields, "rung0")),
                    Enum.Parse<EmlRematchAssayStatuses>(RequireField(fields, "assay")),
                    Enum.Parse<EmlRematchPowerStatuses>(RequireField(fields, "power")),
                    IntField(fields, "opportunities"), IntField(fields, "derivations"), IntField(fields, "zero-evaluator"), IntField(fields, "audits"),
                    IntField(fields, "null-executions"), IntField(fields, "null-divergences"), IntField(fields, "null-authority"),
                    RequireField(fields, "derivation"), RequireField(fields, "source"), RequireField(fields, "config"));
            if (hasAgreed && IntField(fields, "schema") != parsed.SchemaVersion)
                throw new InvalidDataException("ordinary rung-0 journal receipt schema is not bound to its status census");
            if (schema != parsed.SchemaVersion)
                throw new InvalidDataException("ordinary rung-0 journal receipt schema is not bound to its field dialect");
            if (parsed.Opportunities < 0 || parsed.Compositions < 0 || parsed.ZeroEvaluatorCompositions < 0 || parsed.Audits < 0
                || parsed.RelationNullExecutions < 0 || parsed.RelationNullDivergences < 0 || parsed.RelationNullAuthorityPredictions < 0
                || (parsed.SchemaVersion >= 3 ? parsed.Compositions > parsed.AttemptedCandidates : parsed.Compositions > parsed.Opportunities)
                || parsed.ZeroEvaluatorCompositions > parsed.Compositions
                || parsed.RelationNullDivergences > parsed.RelationNullExecutions
                || parsed.RelationNullAuthorityPredictions > parsed.RelationNullExecutions)
                throw new InvalidDataException("ordinary rung-0 receipt counters do not close");
            string digest = RequireField(fields, "digest");
            parsed = parsed with { Digest = digest };
            if (!parsed.IsValid) throw new InvalidDataException("ordinary rung-0 journal receipt digest is invalid");
            result = parsed;
        }
        return result;
    }

    private static ComputeRead ReadCompute(string path, int horizon)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("compute accounting is missing", path);
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException("compute accounting has no rows");
        string[] header = lines[0].Split('\t');
        int rows = 0, previousStep = -1, firstStep = -1;
        List<string> failures = [];
        StringBuilder canonical = new();
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (!CortexComputeAccounting.TryParse(lines[i], header, out CortexComputeRecord? record) || record is null) { failures.Add($"row {i + 1} malformed"); continue; }
            CortexComputeOccurrenceCheck verification = CortexComputeAccountingVerifier.Verify(record, requireZeroDark: true);
            if (!verification.Passed) failures.Add($"row {i + 1}: {verification.Summary}");
            if (previousStep >= 0 && record.Step != previousStep + 1) failures.Add($"row {i + 1} step is not contiguous");
            if (firstStep < 0) firstStep = record.Step;
            previousStep = record.Step; rows++; canonical.Append(record.Digest).Append('|');
        }
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        if (horizon <= 0 || firstStep != 0 || rows != horizon || previousStep != horizon - 1) failures.Add($"compute horizon does not close 0..{horizon - 1}");
        string reportPath = Path.ChangeExtension(path, ".report.tsv");
        string reportText = File.Exists(reportPath) ? File.ReadAllText(reportPath) : "";
        if (!reportText.Contains("status\tPASS", StringComparison.Ordinal) || !reportText.Contains("zero_dark\tPASS", StringComparison.Ordinal)) failures.Add("compute.report.tsv is absent, failed, or dark");
        return new(failures.Count == 0, rows, digest, failures.Count == 0 ? "" : string.Join("; ", failures));
    }

    private static void RequireSemanticFiles(string directory)
    {
        string[] required = ["tape.spanlog", "journal.log", "rhythm.txt", "homeostat.txt", "eml_anytime_curve.tsv", "compute.tsv", "compute.report.tsv",
            "policy_trial_funding.journal.tsv", "policy_trial_settlements.journal.tsv", "policy_readout_funding.journal.tsv", "policy_readout_settlements.journal.tsv", "policy_readout_allocations.journal.tsv", "policy_decisions.tsv"];
        foreach (string name in required)
            if (!File.Exists(Path.Combine(directory, name))) throw new FileNotFoundException($"paired gate semantic artifact is missing: {name}", Path.Combine(directory, name));
    }

    private static void ValidateCurveIdentity(EmlStandardAnytimeCurveSummary curve, RunAuthority authority)
    {
        if (!curve.HasTerminal) return;
        if (!string.Equals(curve.RunID, authority.RunID, StringComparison.Ordinal)) throw new InvalidDataException("standard EML curve run identity disagrees with authority");
        if (string.IsNullOrWhiteSpace(curve.ConfigID) || string.IsNullOrWhiteSpace(curve.ChainID) || string.IsNullOrWhiteSpace(curve.ArmID)) throw new InvalidDataException("standard EML curve omits source/config/arm identity");
        if (curve.Terminal.RunID != curve.RunID || curve.Terminal.ConfigID != curve.ConfigID || curve.Terminal.ChainID != curve.ChainID || curve.Terminal.ArmID != curve.ArmID) throw new InvalidDataException("standard EML curve terminal identity drifted");
    }

    private static string ValidateCurvePair(EmlStandardAnytimeCurveSummary live, EmlStandardAnytimeCurveSummary control, ArmRead liveArm, ArmRead controlArm)
    {
        if (RequiresPairedSchedule(liveArm.Directory, controlArm.Directory)
            && (liveArm.PairedSchedule is null || liveArm.PairedScheduleCursor is null
            || controlArm.PairedSchedule is null || controlArm.PairedScheduleCursor is null)
            )
            return "registered paired curves require sealed fuel schedule sidecars";
        if (RequiresPairedSchedule(liveArm.Directory, controlArm.Directory))
        {
            int liveStep = liveArm.Authority!.Checkpoint.NextStep;
            int controlStep = controlArm.Authority!.Checkpoint.NextStep;
            EmlPairedFuelSchedule liveSchedule = liveArm.PairedSchedule!.Value;
            EmlPairedFuelSchedule controlSchedule = controlArm.PairedSchedule!.Value;
            EmlPairedFuelScheduleCursor liveCursor = liveArm.PairedScheduleCursor!;
            EmlPairedFuelScheduleCursor controlCursor = controlArm.PairedScheduleCursor!;
            if (liveStep <= 0 || liveStep != controlStep || liveSchedule.Horizon != liveStep || controlSchedule.Horizon != controlStep
                || liveCursor.RowCount != liveStep || controlCursor.RowCount != controlStep)
                return "paired schedule horizon does not equal both completed checkpoint horizons";
        }
        if (!live.HasTerminal || !control.HasTerminal) return "";
        if (live.ArmID == control.ArmID) return "paired curves share an arm identity";
        if (live.ConfigID == control.ConfigID) return "paired curves share a config identity";
        return "";
    }

    private static bool IsPairedScheduleIntegrityFailure(string failure)
        => failure.StartsWith("paired schedule", StringComparison.Ordinal)
            || failure.StartsWith("accepted curve prefix", StringComparison.Ordinal)
            || failure.StartsWith("paired fuel schedule", StringComparison.Ordinal);

    private static bool RequiresPairedSchedule(string liveDirectory, string controlDirectory)
        => Path.GetFileName(liveDirectory).StartsWith("gate-paired-", StringComparison.Ordinal)
            || Path.GetFileName(controlDirectory).StartsWith("gate-paired-", StringComparison.Ordinal);

    private static string ValidatePair(RunAuthority live, RunAuthority control, IPolicyBoundaryDomain domain)
    {
        if (live.WorldSHA256.Length == 0 || control.WorldSHA256.Length == 0)
            return "paired arms omit the registered world SHA-256";
        if (!string.Equals(live.WorldSHA256, control.WorldSHA256, StringComparison.Ordinal))
            return "paired arms recorded different world SHA-256 identities";
        if (live.Checkpoint.NextStep <= 0 || live.Checkpoint.NextStep != control.Checkpoint.NextStep)
            return "paired arms do not close at one equal terminal checkpoint horizon";
        if (live.ConfigFingerprint != control.ConfigFingerprint) return "arm-neutral config fingerprints differ";
        if (live.PersistedConfigDigest == control.PersistedConfigDigest) return "registered arm config digests did not differ";
        EmlDeliberationQuota nominal = EmlDeliberationQuota.PairedGateNominal;
        if (live.DeliberationBudget != nominal || control.DeliberationBudget != nominal)
            return "paired arms do not carry the registered non-sentinel nominal deliberation profile";
        if (live.Binary.ProcessSHA256 != control.Binary.ProcessSHA256 || live.Binary.AssemblySHA256 != control.Binary.AssemblySHA256) return "paired arms loaded different binary identities";
        if (live.RunID == control.RunID) return "paired arms share a run ID";
        if (live.RunID.StartsWith("gate-paired-", StringComparison.Ordinal)
            || control.RunID.StartsWith("gate-paired-", StringComparison.Ordinal))
        {
            if (!live.Artifacts.Any(item => item.RelativePath.Equals(EmlPairedFuelSchedule.SidecarFile, StringComparison.Ordinal))
                || !control.Artifacts.Any(item => item.RelativePath.Equals(EmlPairedFuelSchedule.SidecarFile, StringComparison.Ordinal)))
                return "registered paired arms must carry the sealed fuel schedule artifact";
        }
        if (live.Switches.PolicyAuthorityCeiling != domain.ArmTopology.LiveAuthorityCeiling.ToString() || control.Switches.PolicyAuthorityCeiling != domain.ArmTopology.ControlAuthority.ToString()) return "policy authority switches are not registered against paired topology";
        if (live.Switches.ProcessCatalog != domain.ArmTopology.LiveProcessCatalog.ToString() || control.Switches.ProcessCatalog != domain.ArmTopology.ControlProcessCatalog.ToString()) return "process catalog switches are not registered against paired topology";
        if (live.Switches.Rung0 != domain.ArmTopology.LiveRung0.ToString() || control.Switches.Rung0 != domain.ArmTopology.ControlRung0.ToString()) return "rung-0 switches are not registered against paired topology";
        if (live.Switches.Deliberation != domain.ArmTopology.LiveDeliberation.ToString() || control.Switches.Deliberation != domain.ArmTopology.ControlDeliberation.ToString()) return "deliberation switches are not registered against paired topology";
        string expectedTrialAllocationDigest = CortexPolicyTrialAllocation.ComputeDigest(
            domain.PolicyID, domain.ArmTopology.TrialAllocationAuthority, domain.ArmTopology.TrialArmSteps, domain.ArmTopology.TrialAllocationIdentity);
        if (live.Switches.PolicyTrialAllocationArmSteps != domain.ArmTopology.TrialArmSteps
            || live.Switches.PolicyTrialAllocationIdentity != domain.ArmTopology.TrialAllocationIdentity
            || live.Switches.PolicyTrialAllocationDigest != expectedTrialAllocationDigest)
            return "live policy trial allocation does not carry the registered funding custody";
        if (control.Switches.PolicyTrialAllocationArmSteps != 0
            || control.Switches.PolicyTrialAllocationIdentity.Length != 0
            || control.Switches.PolicyTrialAllocationDigest.Length != 0)
            return "control policy trial allocation is not absent";
        return "";
    }

    private static string JoinErrors(ArmRead live, ArmRead control) => string.Join("; ", new[] { live.Error, control.Error }.Where(static value => value.Length > 0));
    private static string ResolveDirectory(string path) => Run.Resolve(path) ?? throw new DirectoryNotFoundException(path);
    private static void EnsureOutsideRuns(string output, string live, string control)
    {
        if (IsBelow(output, live) || IsBelow(output, control)) throw new InvalidOperationException("paired adjudication report must be outside both run directories");
    }
    private static bool IsBelow(string path, string root) => path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    private static Dictionary<string, string> ParseFields(string line) => line.Split('\t').Skip(2).Select(static field => field.Split('=', 2)).Where(static pair => pair.Length == 2).ToDictionary(static pair => pair[0], static pair => pair[1], StringComparer.Ordinal);
    private static string RequireField(Dictionary<string, string> fields, string name) => fields.TryGetValue(name, out string? value) && value.Length > 0 ? value : throw new InvalidDataException($"rung-0 receipt omits {name}");
    private static int IntField(Dictionary<string, string> fields, string name) => int.TryParse(RequireField(fields, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0 ? value : throw new InvalidDataException($"rung-0 receipt has invalid {name}");
    private static string DigestPair(object left, object right) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(left + "|" + right)));
    private static string DigestVitals(PairedGateVitalsReader.RunReadout left, PairedGateVitalsReader.RunReadout right) => DigestPair(left.Rhythm, right.Rhythm);
    private static string DigestPair(EmlStandardAnytimeCurveSummary? left, EmlStandardAnytimeCurveSummary? right) => DigestPair(left?.Digest ?? "", right?.Digest ?? "");

    internal readonly record struct ComputeRead(bool Exact, int Rows, string Digest, string Error);
    internal readonly record struct AuthorityFiles(byte[] AuthorityBytes, byte[] DigestBytes, string AuthoritySHA256, string DigestSHA256);
    internal readonly record struct ClosureRead(bool Exact, string Error);
    internal readonly record struct ArmRead(string Directory, RunAuthority? Authority, CheckpointVowReceipt Vow, EmlStandardAnytimeCurveSummary? Curve, PairedGateVitalsReader.RunReadout? Vitals, EmlOrdinaryRunRung0Receipt? Rung0, ComputeRead Compute, EmlPairedFuelSchedule? PairedSchedule, EmlPairedFuelScheduleCursor? PairedScheduleCursor, string Error, ClosureRead Closure, byte[] EvidenceBytes)
    {
        internal ArmReport ToReport() => ArmReport.From(this);
    }
}

public enum PairedGateAssayStatuses : byte { Exact, Invalid }
public enum PairedGatePowerStatuses : byte { Powered, Unpowered }
public enum PairedGateVerdictStatuses : byte { PASS, FAIL, BANKED_NULL, INVALID }

public readonly record struct PairedGateLineVerdict(string Name, PairedGateAssayStatuses Assay, PairedGatePowerStatuses Power, PairedGateVerdictStatuses Status, string Detail, string EvidenceDigest);

public sealed class PairedGateReport
{
    internal PairedGateReport(ArmReport live, ArmReport control, IReadOnlyList<PairedGateLineVerdict> lines, string Outcome, string nextAdmissibleExperiment, string Digest)
    { Live = live; Control = control; Lines = lines; this.Outcome = Outcome; NextAdmissibleExperiment = nextAdmissibleExperiment; this.Digest = Digest; }
    public ArmReport Live { get; }
    public ArmReport Control { get; }
    public IReadOnlyList<PairedGateLineVerdict> Lines { get; }
    public string Outcome { get; }
    public string NextAdmissibleExperiment { get; }
    public string Digest { get; }
    internal static PairedGateReport Create(ArmReport live, ArmReport control, IReadOnlyList<PairedGateLineVerdict> lines, string outcome)
    {
        string[] names = ["vocabulary", "efficiency", "derivation", "decider", "vow", "zero-dark", "organism"];
        if (lines.Count != names.Length || !lines.Select(static line => line.Name).SequenceEqual(names, StringComparer.Ordinal)) throw new ArgumentException("paired gate reports require exactly seven named pre-registered lines", nameof(lines));
        string nextAdmissibleExperiment = DeriveNextAdmissibleExperiment(lines);
        PairedGateReport report = new(live, control, lines, outcome, nextAdmissibleExperiment, "");
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToHexString(report.EncodeDocument("")))));
        return new(live, control, lines, outcome, nextAdmissibleExperiment, digest);
    }
    public byte[] Encode()
    {
        byte[] first = EncodeDocument(Digest); byte[] second = EncodeDocument(Digest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("paired gate RON encoding is nondeterministic");
        return first;
    }
    private byte[] EncodeDocument(string digest)
    {
        PairedGateRON document = new() { schemaVersion = 1, live = Live.ToRON(), control = Control.ToRON(), outcome = Outcome, next_admissible_experiment = NextAdmissibleExperiment, digest = digest };
        foreach (PairedGateLineVerdict line in Lines) document.lines.Add(new PairedGateRONLine { name = line.Name, assay = line.Assay.ToString(), power = line.Power.ToString(), status = line.Status.ToString(), detail = line.Detail, evidenceDigest = line.EvidenceDigest });
        return RonSerializer.SerializeToUtf8(in document);
    }

    private static string DeriveNextAdmissibleExperiment(IReadOnlyList<PairedGateLineVerdict> lines)
    {
        if (lines.Any(static line => line.Assay == PairedGateAssayStatuses.Invalid || line.Status == PairedGateVerdictStatuses.INVALID))
            return "Repair assay integrity, then rerun the exact registered paired design; do not interpret the science.";

        if (lines.All(static line => line.Status == PairedGateVerdictStatuses.PASS))
            return "The exact registered PairedGate is complete at 7/7; no next experiment is admissible.";

        PairedGateLineVerdict derivation = lines[2];
        if (derivation.Name == "derivation"
            && derivation.Assay == PairedGateAssayStatuses.Exact
            && derivation.Power == PairedGatePowerStatuses.Unpowered
            && derivation.Status == PairedGateVerdictStatuses.BANKED_NULL)
            return "Re-register the exact paired design with the same arms and thresholds at 2x the registered horizon; do not raise ActionsPerStep or hand-seed the basis. Retain all other typed nulls.";

        if (lines.Any(static line => line.Status == PairedGateVerdictStatuses.BANKED_NULL))
            return "Repeat the exact registered paired design and preserve every typed null; do not tune thresholds.";

        bool poweredFailOnly = lines.Any(static line => line.Status == PairedGateVerdictStatuses.FAIL)
            && lines.All(static line => line.Status == PairedGateVerdictStatuses.PASS
                || (line.Assay == PairedGateAssayStatuses.Exact
                    && line.Power == PairedGatePowerStatuses.Powered
                    && line.Status == PairedGateVerdictStatuses.FAIL));
        if (poweredFailOnly)
            return "Run a confirmatory exact registered paired pair without tuning thresholds.";

        return "Repeat the exact registered paired design without tuning thresholds.";
    }
}

public sealed class ArmReport
{
    internal ArmReport(string runID, string configFingerprint, string worldSHA256, string authorityDigest, string checkpointDigest, string computeDigest, string closureDigest, string binaryDigest,
        EmlDeliberationCounts plannedFuel, EmlDeliberationCounts actualFuel, EmlDeliberationCounts refundFuel, int fuelHorizon,
        string scheduleDigest = "", int schedulePrefixStep = 0, int scheduleHorizon = 0)
    { RunID = runID; ConfigFingerprint = configFingerprint; WorldSHA256 = worldSHA256; AuthorityDigest = authorityDigest; CheckpointDigest = checkpointDigest; ComputeDigest = computeDigest; ClosureDigest = closureDigest; BinaryDigest = binaryDigest; PlannedFuel = plannedFuel; ActualFuel = actualFuel; RefundFuel = refundFuel; FuelHorizon = fuelHorizon; ScheduleDigest = scheduleDigest; SchedulePrefixStep = schedulePrefixStep; ScheduleHorizon = scheduleHorizon; }
    public string RunID { get; }
    public string ConfigFingerprint { get; }
    public string WorldSHA256 { get; }
    public string AuthorityDigest { get; }
    public string CheckpointDigest { get; }
    public string ComputeDigest { get; }
    public string ClosureDigest { get; }
    public string BinaryDigest { get; }
    public EmlDeliberationCounts PlannedFuel { get; }
    public EmlDeliberationCounts ActualFuel { get; }
    public EmlDeliberationCounts RefundFuel { get; }
    public int FuelHorizon { get; }
    public string ScheduleDigest { get; }
    public int SchedulePrefixStep { get; }
    public int ScheduleHorizon { get; }
    internal PairedGateRONArm ToRON() => new() { runID = RunID, configFingerprint = ConfigFingerprint, worldSHA256 = WorldSHA256, authorityDigest = AuthorityDigest, checkpointDigest = CheckpointDigest, computeDigest = ComputeDigest, closureDigest = ClosureDigest, binaryDigest = BinaryDigest, plannedFuel = Fuel(PlannedFuel), actualFuel = Fuel(ActualFuel), refundFuel = Fuel(RefundFuel), fuelHorizon = FuelHorizon, scheduleDigest = ScheduleDigest, schedulePrefixStep = SchedulePrefixStep, scheduleHorizon = ScheduleHorizon };
    internal static ArmReport From(object value) => value is PairedGateAdjudicator.ArmRead arm ? From(arm) : throw new InvalidOperationException();
    internal static ArmReport FromHistorical(RunAuthority authority, string computeDigest, EmlStandardAnytimeCurveSummary curve,
        EmlPairedFuelSchedule? pairedSchedule, EmlPairedFuelScheduleCursor? pairedCursor)
        => Create(authority, authority.Checkpoint.PhysicalChainSHA256, computeDigest, curve, pairedSchedule, pairedCursor);
    private static ArmReport From(PairedGateAdjudicator.ArmRead arm)
    {
        EmlStandardAnytimeCurveSummary? curve = arm.Curve;
        if (arm.Authority is not RunAuthority authority)
            return new("", "", "", "", arm.Vow.ChainSHA256, arm.Compute.Digest, "", "",
                curve?.PlannedFuel ?? EmlDeliberationCounts.Zero, curve?.ActualFuel ?? EmlDeliberationCounts.Zero,
                curve?.RefundFuel ?? EmlDeliberationCounts.Zero, curve?.AcceptedKnee?.PrefixStep ?? 0,
                arm.PairedSchedule?.Digest ?? "", arm.PairedScheduleCursor?.RowCount ?? 0, arm.PairedSchedule?.Horizon ?? 0);
        return Create(authority, arm.Vow.ChainSHA256, arm.Compute.Digest, curve, arm.PairedSchedule, arm.PairedScheduleCursor);
    }
    private static ArmReport Create(RunAuthority authority, string checkpointDigest, string computeDigest,
        EmlStandardAnytimeCurveSummary? curve, EmlPairedFuelSchedule? pairedSchedule, EmlPairedFuelScheduleCursor? pairedCursor)
        => new(authority.RunID, authority.ConfigFingerprint, authority.WorldSHA256, authority.Digest, checkpointDigest, computeDigest,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', authority.Artifacts.Select(static item => item.RelativePath + ":" + item.SHA256))))),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', authority.Binary.ProcessName, authority.Binary.ProcessSHA256, authority.Binary.AssemblyName, authority.Binary.AssemblySHA256)))),
            curve?.PlannedFuel ?? EmlDeliberationCounts.Zero, curve?.ActualFuel ?? EmlDeliberationCounts.Zero,
            curve?.RefundFuel ?? EmlDeliberationCounts.Zero, curve?.AcceptedKnee?.PrefixStep ?? 0,
            pairedSchedule?.Digest ?? "", pairedCursor?.RowCount ?? 0, pairedSchedule?.Horizon ?? 0);
    private static PairedGateRONFuel Fuel(in EmlDeliberationCounts value) => new()
    {
        candidateEvaluations = value.CandidateEvaluations, logicalProgramPoints = value.LogicalProgramPoints, executedProgramPoints = value.ExecutedProgramPoints,
        inverseTransforms = value.InverseTransforms, hashProbes = value.HashProbes, joinAttempts = value.JoinAttempts, joinHits = value.JoinHits,
        processTerms = value.ProcessTerms, verifierProgramPoints = value.VerifierProgramPoints, candidateSupplyItems = value.CandidateSupplyItems,
        lawRewriteApplications = value.LawRewriteApplications, lawRewriteTreeNodes = value.LawRewriteTreeNodes,
    };
}

[RonObject]
internal partial class PairedGateRON
{
    public int schemaVersion;
    public PairedGateRONArm live = new();
    public PairedGateRONArm control = new();
    public List<PairedGateRONLine> lines = new();
    public string outcome = "";
    public string next_admissible_experiment = "";
    public string digest = "";
}
[RonObject]
internal partial class PairedGateRONArm
{
    public string runID = "";
    public string configFingerprint = "";
    public string worldSHA256 = "";
    public string authorityDigest = "";
    public string checkpointDigest = "";
    public string computeDigest = "";
    public string closureDigest = "";
    public string binaryDigest = "";
    public PairedGateRONFuel plannedFuel = new();
    public PairedGateRONFuel actualFuel = new();
    public PairedGateRONFuel refundFuel = new();
    public int fuelHorizon;
    public string scheduleDigest = "";
    public int schedulePrefixStep;
    public int scheduleHorizon;
}
[RonObject]
internal partial class PairedGateRONFuel
{
    public long candidateEvaluations;
    public long logicalProgramPoints;
    public long executedProgramPoints;
    public long inverseTransforms;
    public long hashProbes;
    public long joinAttempts;
    public long joinHits;
    public long processTerms;
    public long verifierProgramPoints;
    public long candidateSupplyItems;
    public long lawRewriteApplications;
    public long lawRewriteTreeNodes;
}
[RonObject]
internal partial class PairedGateRONLine
{
    public string name = "";
    public string assay = "";
    public string power = "";
    public string status = "";
    public string detail = "";
    public string evidenceDigest = "";
}
