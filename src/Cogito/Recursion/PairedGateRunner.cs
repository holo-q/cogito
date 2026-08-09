namespace Cogito;

using System.Globalization;
/// Launches the registered paired gate as fresh, sequential Cortex runs.
///
/// The two arms share one config construction path and differ only in the four
/// registered arm switches. Run.Create is given the final destination directly
/// so a collision cannot silently allocate a suffixed lineage.
internal static class PairedGateRunner
{
    internal readonly record struct Request(
        string SeedToken,
        int Steps,
        string Corpus,
        string[] SecondarySeedTokens);

    internal static int Run(Request request, TextWriter output, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(domain);
        domain.ArmTopology.Validate();
        Request normalized = Validate(request);
        SeedSpec primary = ParseSeed(normalized.SeedToken, "--seed");
        SeedSpec[] secondary = normalized.SecondarySeedTokens
            .Select(token => ParseSeed(token, "--seeds"))
            .ToArray();

        SeedSpec[] allSeeds = [primary, .. secondary];
        RequireDistinctSeeds(allSeeds);
        string[] liveNames = allSeeds.Select(static seed => DestinationName(seed.Label, live: true)).ToArray();
        string[] controlNames = allSeeds.Select(static seed => DestinationName(seed.Label, live: false)).ToArray();
        string[] reportPaths = allSeeds
            .Select(static seed => global::Cogito.Run.HomePath($"gate-paired-{seed.Label}.ron"))
            .ToArray();
        string worldSHA256 = FileCorpus.ComputeWorldSHA256(normalized.Corpus, CogitoCorpus.DefaultGlob);
        RunAuthorityBinary currentBinary = RunAuthority.CurrentBinaryIdentity();

        VerifyRegisteredWorld(normalized.Corpus, worldSHA256);
        RunAuthority? liveIdentity = TryReadCompletedIdentity(liveNames[0], out Exception? liveIdentityError);
        RunAuthority? controlIdentity = TryReadCompletedIdentity(controlNames[0], out Exception? controlIdentityError);
        RequireCurrentBinaryForMixedPair(liveIdentity, controlIdentity, currentBinary, liveIdentityError, controlIdentityError);
        ArchiveReportBeforeArmReplacement(reportPaths[0], liveNames[0], controlNames[0], liveIdentity, controlIdentity, currentBinary, liveIdentityError, controlIdentityError);
        PreparedArm live = PrepareArm(liveNames[0], primary, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: false, currentBinary: currentBinary);
        int liveExit = live.Reused ? 0 : CreateArm(primary.Value, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: false).Run(live.Run);
        output.WriteLine($"  gate paired · arm=live · seed={primary.Label} · exit={liveExit} · run={live.Run.Dir} · {(live.Reused ? "reused" : "fresh")}");
        if (liveExit != 0) return liveExit;

        VerifyRegisteredWorld(normalized.Corpus, worldSHA256);
        PreparedArm control = PrepareArm(controlNames[0], primary, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: true, currentBinary: currentBinary);
        int controlExit = control.Reused ? 0 : CreateArm(primary.Value, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: true).Run(control.Run);
        output.WriteLine($"  gate paired · arm=control · seed={primary.Label} · exit={controlExit} · run={control.Run.Dir} · {(control.Reused ? "reused" : "fresh")}");
        if (controlExit != 0) return controlExit;

        PairedGateReport primaryReport = ProduceReport(live.Run.Dir, control.Run.Dir, reportPaths[0], domain);
        int primaryExit = primaryReport.Lines.All(static line => line.Status == PairedGateVerdictStatuses.PASS) ? 0 : 1;
        output.WriteLine($"  gate paired · adjudicated · report={reportPaths[0]}");

        for (int i = 0; i < secondary.Length; i++)
        {
            SeedSpec seed = secondary[i];
            VerifyRegisteredWorld(normalized.Corpus, worldSHA256);
            RunAuthority? secondaryLiveIdentity = TryReadCompletedIdentity(liveNames[i + 1], out Exception? secondaryLiveIdentityError);
            RunAuthority? secondaryControlIdentity = TryReadCompletedIdentity(controlNames[i + 1], out Exception? secondaryControlIdentityError);
            RequireCurrentBinaryForMixedPair(secondaryLiveIdentity, secondaryControlIdentity, currentBinary, secondaryLiveIdentityError, secondaryControlIdentityError);
            ArchiveReportBeforeArmReplacement(reportPaths[i + 1], liveNames[i + 1], controlNames[i + 1], secondaryLiveIdentity, secondaryControlIdentity, currentBinary, secondaryLiveIdentityError, secondaryControlIdentityError);
            PreparedArm secondaryLive = PrepareArm(liveNames[i + 1], seed, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: false, currentBinary: currentBinary);
            int secondaryLiveExit = secondaryLive.Reused ? 0 : CreateArm(seed.Value, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: false).Run(secondaryLive.Run);
            output.WriteLine($"  gate paired · robustness={i + 1} · arm=live · seed={seed.Label} · exit={secondaryLiveExit} · run={secondaryLive.Run.Dir} · {(secondaryLive.Reused ? "reused" : "fresh")}");
            if (secondaryLiveExit != 0) return secondaryLiveExit;

            VerifyRegisteredWorld(normalized.Corpus, worldSHA256);
            PreparedArm secondaryControl = PrepareArm(controlNames[i + 1], seed, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: true, currentBinary: currentBinary);
            int secondaryControlExit = secondaryControl.Reused ? 0 : CreateArm(seed.Value, normalized.Steps, normalized.Corpus, worldSHA256, domain, control: true).Run(secondaryControl.Run);
            output.WriteLine($"  gate paired · robustness={i + 1} · arm=control · seed={seed.Label} · exit={secondaryControlExit} · run={secondaryControl.Run.Dir} · {(secondaryControl.Reused ? "reused" : "fresh")}");
            if (secondaryControlExit != 0) return secondaryControlExit;

            ProduceReport(secondaryLive.Run.Dir, secondaryControl.Run.Dir, reportPaths[i + 1], domain);
            output.WriteLine($"  gate paired · robustness={i + 1} · adjudicated · report={reportPaths[i + 1]}");
        }

        return primaryExit;
    }

    internal static bool VerifyFixture(TextWriter output, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(domain);
        domain.ArmTopology.Validate();
        string fixtureID = $".paired-gate-runner-fixture-{Guid.NewGuid():N}";
        string root = global::Cogito.Run.HomePath(fixtureID);
        string name = fixtureID + "_live";
        string controlName = fixtureID + "_control";
        string runPath = global::Cogito.Run.HomePath(name);
        string controlPath = global::Cogito.Run.HomePath(controlName);
        string archivedPath = runPath + ".interrupted-0000";
        string archivedMarkerPath = archivedPath + ".txt";
        string staleArchivedPath = runPath + ".stale-binary-0000";
        string staleArchivedMarkerPath = staleArchivedPath + ".txt";
        string staleControlArchivedPath = controlPath + ".stale-binary-0000";
        string staleControlArchivedMarkerPath = staleControlArchivedPath + ".txt";
        string reportPath = global::Cogito.Run.HomePath(fixtureID + ".ron");
        string staleReportPath = Path.Combine(Path.GetDirectoryName(reportPath)!, Path.GetFileNameWithoutExtension(reportPath) + ".stale-pair-0000" + Path.GetExtension(reportPath));
        string staleReportMarkerPath = Path.ChangeExtension(staleReportPath, ".txt");
        string forgedReportPath = Path.Combine(Path.GetDirectoryName(reportPath)!, Path.GetFileNameWithoutExtension(reportPath) + ".forged.ron");
        string forgedInvalidReportPath = Path.Combine(Path.GetDirectoryName(reportPath)!, Path.GetFileNameWithoutExtension(reportPath) + ".forged.invalid-stale-0000" + Path.GetExtension(reportPath));
        string forgedInvalidReportMarkerPath = Path.ChangeExtension(forgedInvalidReportPath, ".txt");
        try
        {
            Directory.CreateDirectory(root);
            string corpus = Path.Combine(root, "corpus");
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "corpus.txt"), "alpha beta gamma\n");
            string worldSHA256 = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
            SeedSpec canonical = ParseSeed("0x000000000000DEADBEF2", "--seed");
            SeedSpec alias = ParseSeed("deadbef2", "--seeds");
            bool seedCanonical = canonical.Label == "deadbef2" && canonical.Value == alias.Value;
            CortexRunConfig liveConfig = CreateArm(canonical.Value, 1, corpus, worldSHA256, domain, control: false).Config.ToRunConfig(null);
            CortexRunConfig controlConfig = CreateArm(canonical.Value, 1, corpus, worldSHA256, domain, control: true).Config.ToRunConfig(null);
            string liveFingerprint = Cortex.ArmNeutralPersistedConfigDigest(liveConfig);
            bool armNeutralFingerprint = liveFingerprint == Cortex.ArmNeutralPersistedConfigDigest(controlConfig);
            bool persistedArmDifference = Cortex.PersistedConfigDigest(liveConfig) != Cortex.PersistedConfigDigest(controlConfig);
            bool worldDriftDetected = liveFingerprint != Cortex.ArmNeutralPersistedConfigDigest(controlConfig with { ExpectedWorldSHA256 = worldSHA256 + "-drift" });
            bool duplicateRejected;
            try
            {
                RequireDistinctSeeds([canonical, alias]);
                duplicateRejected = false;
            }
            catch (ArgumentException) { duplicateRejected = true; }

            Directory.CreateDirectory(runPath);
            File.WriteAllText(Path.Combine(runPath, "partial.marker"), "interrupted");
            PreparedArm fresh = PrepareArm(name, canonical, 1, corpus, worldSHA256, domain, control: false);
            bool interruptedArchived = !fresh.Reused && Directory.Exists(archivedPath)
                && File.ReadAllText(archivedMarkerPath).Contains("status=interrupted", StringComparison.Ordinal);
            bool drove = CreateArm(canonical.Value, 1, corpus, worldSHA256, domain, control: false).Run(fresh.Run) == 0;
            if (drove)
            {
                CortexRunConfig config = Checkpoint.PeekConfig(fresh.Run.Dir);
                RunAuthority.WriteCompleted(fresh.Run, config, Checkpoint.NextStep(fresh.Run.Dir));
            }
            RunAuthority? completed = drove ? RunAuthority.LoadIdentity(fresh.Run.Dir) : null;
            PreparedArm reused = drove ? PrepareArm(name, canonical, 1, corpus, worldSHA256, domain, control: false) : default;
            bool completedReused = completed is not null && reused.Reused;

            PreparedArm controlFresh = PrepareArm(controlName, canonical, 1, corpus, worldSHA256, domain, control: true);
            bool controlDrove = CreateArm(canonical.Value, 1, corpus, worldSHA256, domain, control: true).Run(controlFresh.Run) == 0;
            if (controlDrove)
            {
                CortexRunConfig config = Checkpoint.PeekConfig(controlFresh.Run.Dir);
                RunAuthority.WriteCompleted(controlFresh.Run, config, Checkpoint.NextStep(controlFresh.Run.Dir));
            }
            RunAuthority? controlCompleted = controlDrove ? RunAuthority.LoadIdentity(controlFresh.Run.Dir) : null;
            if (completed is null || controlCompleted is null) throw new InvalidDataException("paired gate runner fixture did not produce the historical pair");
            byte[] historicalReportBytes = PairedGateAdjudicator.Adjudicate(runPath, controlPath, domain, reportPath).Encode();

            RunAuthorityBinary current = RunAuthority.CurrentBinaryIdentity();
            RunAuthorityBinary foreign = current with { ProcessSHA256 = current.ProcessSHA256 + "-foreign" };
            string historicalCheckpointPath = Path.Combine(runPath, Checkpoint.FileName);
            byte[] historicalCheckpointBytes = File.ReadAllBytes(historicalCheckpointPath);
            byte[] driftedCheckpointBytes = [.. historicalCheckpointBytes, 0, 0, 0, 0];
            File.WriteAllBytes(historicalCheckpointPath, driftedCheckpointBytes);
            bool historicalVowDriftRejected;
            try
            {
                _ = RunAuthority.Load(runPath);
                historicalVowDriftRejected = false;
            }
            catch (InvalidDataException) { historicalVowDriftRejected = true; }
            File.WriteAllBytes(historicalCheckpointPath, historicalCheckpointBytes);
            ArchiveReportBeforeArmReplacement(reportPath, name, controlName, completed, controlCompleted, foreign);
            bool staleReportArchived = File.Exists(staleReportPath)
                && File.ReadAllBytes(staleReportPath).AsSpan().SequenceEqual(historicalReportBytes)
                && File.Exists(staleReportMarkerPath)
                && File.ReadAllText(staleReportMarkerPath).Contains("status=stale-pair", StringComparison.Ordinal);
            bool staleBinaryArchived = false;
            PreparedArm replacement = default;
            PreparedArm controlReplacement = default;
            replacement = PrepareArm(name, canonical, 1, corpus, worldSHA256, domain, control: false, currentBinary: foreign);
            controlReplacement = PrepareArm(controlName, canonical, 1, corpus, worldSHA256, domain, control: true, currentBinary: foreign);
            staleBinaryArchived = historicalVowDriftRejected && !replacement.Reused && !controlReplacement.Reused
                && Directory.Exists(staleArchivedPath) && Directory.Exists(staleControlArchivedPath)
                && File.ReadAllText(staleArchivedMarkerPath).Contains("status=stale-binary", StringComparison.Ordinal)
                && File.ReadAllText(staleControlArchivedMarkerPath).Contains("status=stale-binary", StringComparison.Ordinal);

            bool replacementDrove = staleBinaryArchived
                && CreateArm(canonical.Value, 1, corpus, worldSHA256, domain, control: false).Run(replacement.Run) == 0
                && CreateArm(canonical.Value, 1, corpus, worldSHA256, domain, control: true).Run(controlReplacement.Run) == 0;
            if (replacementDrove)
            {
                CortexRunConfig replacementConfig = Checkpoint.PeekConfig(replacement.Run.Dir);
                RunAuthority.WriteCompleted(replacement.Run, replacementConfig, Checkpoint.NextStep(replacement.Run.Dir));
                CortexRunConfig controlReplacementConfig = Checkpoint.PeekConfig(controlReplacement.Run.Dir);
                RunAuthority.WriteCompleted(controlReplacement.Run, controlReplacementConfig, Checkpoint.NextStep(controlReplacement.Run.Dir));
            }
            PairedGateReport? recoveredReport = replacementDrove ? ProduceReport(runPath, controlPath, reportPath, domain) : null;
            bool staleReportRecovered = staleReportArchived && recoveredReport is not null;

            bool mixedBinaryRefused;
            try
            {
                RequireCurrentBinaryForMixedPair(completed, null, foreign);
                mixedBinaryRefused = false;
            }
            catch (IOException) { mixedBinaryRefused = true; }

            string[] names = ["vocabulary", "efficiency", "derivation", "decider", "vow", "zero-dark", "organism"];
            List<PairedGateLineVerdict> lines = names
                .Select(static line => new PairedGateLineVerdict(line, PairedGateAssayStatuses.Exact, PairedGatePowerStatuses.Unpowered, PairedGateVerdictStatuses.BANKED_NULL, "fixture", "fixture"))
                .ToList();
            RunAuthority? replacementIdentity = replacementDrove ? RunAuthority.LoadIdentity(runPath) : null;
            if (replacementIdentity is null || recoveredReport is null) throw new InvalidDataException("paired gate runner fixture did not produce a replacement identity");
            ArmReport forgedArm = new(
                replacementIdentity.RunID, replacementIdentity.ConfigFingerprint, replacementIdentity.WorldSHA256,
                replacementIdentity.Digest, "forged-checkpoint", "forged-compute", "forged-closure", "forged-binary",
                EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, EmlDeliberationCounts.Zero, 0);
            PairedGateReport fixtureReport = PairedGateReport.Create(forgedArm, recoveredReport.Control, lines, "fixture");
            File.WriteAllBytes(reportPath, fixtureReport.Encode());
            File.Copy(reportPath, forgedReportPath, overwrite: true);
            ArchiveReportBeforeArmReplacement(forgedReportPath, name, controlName, replacementIdentity, controlCompleted, current);
            bool forgedInvalidArchived = File.Exists(forgedInvalidReportPath)
                && File.Exists(forgedInvalidReportMarkerPath)
                && File.ReadAllText(forgedInvalidReportMarkerPath).Contains("status=invalid-stale", StringComparison.Ordinal);
            bool reportIdentityRefused;
            try
            {
                _ = ProduceReport(runPath, controlPath, reportPath, domain);
                reportIdentityRefused = false;
            }
            catch (IOException) { reportIdentityRefused = true; }

            bool pass = seedCanonical && armNeutralFingerprint && persistedArmDifference && worldDriftDetected && duplicateRejected && interruptedArchived && completedReused && staleBinaryArchived
                && staleReportRecovered && forgedInvalidArchived && mixedBinaryRefused && reportIdentityRefused;
            output.WriteLine($"  paired-gate runner fixture · seed={(seedCanonical ? "canonical" : "DRIFT")} · arm-neutral={(armNeutralFingerprint ? "exact" : "DRIFT")} · arm-specific={(persistedArmDifference ? "distinct" : "COLLAPSED")} · world-drift={(worldDriftDetected ? "rejected" : "ACCEPTED")} · duplicate={(duplicateRejected ? "rejected" : "ACCEPTED")} · interrupted={(interruptedArchived ? "archived" : "CLOBBERED")} · completed={(completedReused ? "reused" : "RERAN")} · vow-drift={(historicalVowDriftRejected ? "rejected" : "ACCEPTED")} · stale-binary={(staleBinaryArchived ? "archived-reran" : "REUSED")} · stale-report={(staleReportRecovered ? "archived-reran" : "LOST")} · invalid-stale={(forgedInvalidArchived ? "quarantined" : "ACCEPTED")} · mixed-binary={(mixedBinaryRefused ? "rejected" : "ACCEPTED")} · report-identity={(reportIdentityRefused ? "rejected" : "ACCEPTED")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            foreach (string path in new[] { reportPath, forgedReportPath, staleReportPath, staleReportMarkerPath, forgedInvalidReportPath, forgedInvalidReportMarkerPath,
                         runPath, controlPath, archivedPath, archivedMarkerPath, staleArchivedPath, staleArchivedMarkerPath,
                         staleControlArchivedPath, staleControlArchivedMarkerPath, root })
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static Request Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.SeedToken))
            throw new ArgumentException("--seed is required", nameof(request));
        if (request.Steps <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.Steps, "--steps must be greater than zero");
        if (string.IsNullOrWhiteSpace(request.Corpus))
            throw new ArgumentException("--corpus is required", nameof(request));

        string corpus = Path.GetFullPath(request.Corpus);
        if (!Directory.Exists(corpus) && !File.Exists(corpus))
            throw new DirectoryNotFoundException($"paired gate corpus was not found: {request.Corpus}");

        string[] seeds = request.SecondarySeedTokens ?? [];
        if (seeds.Length > 0 && seeds.Length != 2)
            throw new ArgumentException("--seeds requires exactly two comma-separated secondary seeds", nameof(request));
        return request with { Corpus = corpus, SecondarySeedTokens = seeds };
    }

    private static Cortex CreateArm(ulong seed, int steps, string corpus, string worldSHA256, IPolicyBoundaryDomain domain, bool control)
    {
        domain.ArmTopology.Validate();
        CortexEmlCurriculum curriculum = new()
        {
            Corpus = new CogitoCorpus { Path = corpus, ExpectedWorldSHA256 = worldSHA256 },
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
        CortexConfig config = new()
        {
            RunName = "gate-paired",
            Seed = seed,
            Steps = steps,
            ActionsPerStep = curriculum.IntakeBatch,
            Curriculum = curriculum,
            Learning = new CortexLearningConfig { Policies = policies },
        };
        return new Cortex(config);
    }

    private static void VerifyRegisteredWorld(string corpus, string expected)
    {
        string observed = FileCorpus.ComputeWorldSHA256(corpus, CogitoCorpus.DefaultGlob);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"paired gate corpus world drifted: registered {expected}, observed {observed}");
    }

    private static SeedSpec ParseSeed(string token, string option)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException($"{option} contains an empty seed");
        string label = token.Trim();
        if (label.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) label = label[2..];
        if (label.Length == 0 || !ulong.TryParse(label, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
            throw new ArgumentException($"{option} seed must be hexadecimal: '{token}'");
        return new SeedSpec(value.ToString("x", CultureInfo.InvariantCulture), value);
    }

    private static string DestinationName(string seedLabel, bool live)
        => $"gate-paired-{seedLabel}_{(live ? "live" : "control")}";

    private static void RequireDistinctSeeds(IReadOnlyList<SeedSpec> seeds)
    {
        if (seeds.Select(static seed => seed.Value).Distinct().Count() != seeds.Count)
            throw new ArgumentException("--seed and --seeds must contain distinct numeric seeds");
    }

    private static PreparedArm PrepareArm(string name, SeedSpec seed, int steps, string corpus, string worldSHA256, IPolicyBoundaryDomain domain, bool control, RunAuthorityBinary? currentBinary = null)
    {
        string path = global::Cogito.Run.HomePath(name);
        if (!Directory.Exists(path))
        {
            if (File.Exists(path)) throw new IOException($"paired gate destination is a file: {path}");
            return new(global::Cogito.Run.Create(name), Reused: false);
        }

        RunAuthority? authority = null;
        Exception? incomplete = null;
        try
        {
            authority = RunAuthority.LoadIdentity(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            incomplete = ex;
        }

        if (authority is not null)
        {
            RunAuthorityBinary expectedBinary = currentBinary ?? RunAuthority.CurrentBinaryIdentity();
            if (!MatchesBinary(authority.Binary, expectedBinary))
            {
                if (!authority.ClosureMatches(path, out string closureError))
                {
                    string invalidArchive = ArchiveInvalidStale(path, new InvalidDataException(closureError));
                    Trace.Note($"paired gate · archived invalid stale arm {Path.GetFileName(path)} → {Path.GetFileName(invalidArchive)}");
                    return new(global::Cogito.Run.Create(name), Reused: false);
                }
                string staleArchive = ArchiveStaleCompleted(path, authority, expectedBinary);
                Trace.Note($"paired gate · archived stale completed arm {Path.GetFileName(path)} → {Path.GetFileName(staleArchive)}");
                return new(global::Cogito.Run.Create(name), Reused: false);
            }

            try
            {
                // Same-binary evidence must satisfy the current checkpoint Vow before
                // it can be reused. Producer-binary mismatches were classified above
                // using the sealed historical closure and never reach this replay.
                _ = RunAuthority.Load(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                string invalidArchive = ArchiveInvalidStale(path, ex);
                Trace.Note($"paired gate · archived invalid stale arm {Path.GetFileName(path)} → {Path.GetFileName(invalidArchive)}");
                return new(global::Cogito.Run.Create(name), Reused: false);
            }
            CortexRunConfig config = Checkpoint.PeekConfig(path);
            CortexRunConfig expectedConfig = CreateArm(seed.Value, steps, corpus, worldSHA256, domain, control).Config.ToRunConfig(null);
            string expectedAuthority = (control ? domain.ArmTopology.ControlAuthority : domain.ArmTopology.LiveAuthorityCeiling).ToString();
            string expectedCatalog = (control ? domain.ArmTopology.ControlProcessCatalog : domain.ArmTopology.LiveProcessCatalog).ToString();
            string expectedRung0 = (control ? domain.ArmTopology.ControlRung0 : domain.ArmTopology.LiveRung0).ToString();
            string expectedDeliberation = (control ? domain.ArmTopology.ControlDeliberation : domain.ArmTopology.LiveDeliberation).ToString();
            long expectedAllocation = control ? 0 : domain.ArmTopology.TrialArmSteps;
            string expectedAllocationIdentity = control ? "" : domain.ArmTopology.TrialAllocationIdentity;
            if (config.Seed != seed.Value || config.Steps != steps || config.ExpectedWorldSHA256 != worldSHA256
                || authority.Checkpoint.NextStep != steps
                || authority.PersistedConfigDigest != Cortex.PersistedConfigDigest(expectedConfig)
                || authority.ConfigFingerprint != Cortex.ArmNeutralPersistedConfigDigest(expectedConfig)
                || authority.Switches.PolicyAuthorityCeiling != expectedAuthority
                || authority.Switches.ProcessCatalog != expectedCatalog
                || authority.Switches.Rung0 != expectedRung0
                || authority.Switches.Deliberation != expectedDeliberation
                || authority.Switches.PolicyTrialAllocationArmSteps != expectedAllocation
                || authority.Switches.PolicyTrialAllocationIdentity != expectedAllocationIdentity)
                throw new IOException($"paired gate completed arm does not match registered identity: {path} — refusing to mutate immutable evidence");
            return new(global::Cogito.Run.Open(path), Reused: true);
        }

        bool hadAuthority = File.Exists(Path.Combine(path, RunAuthority.FileName));
        string archived = hadAuthority ? ArchiveInvalidStale(path, incomplete) : ArchiveInterrupted(path, incomplete);
        Trace.Note($"paired gate · archived {(hadAuthority ? "invalid stale" : "interrupted")} arm {Path.GetFileName(path)} → {Path.GetFileName(archived)}");
        return new(global::Cogito.Run.Create(name), Reused: false);
    }

    private static RunAuthority? TryReadCompletedIdentity(string name, out Exception? error)
    {
        error = null;
        string path = global::Cogito.Run.HomePath(name);
        if (!Directory.Exists(path)) return null;
        try { return RunAuthority.LoadIdentity(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex;
            return null;
        }
    }

    private static void RequireCurrentBinaryForMixedPair(RunAuthority? live, RunAuthority? control, RunAuthorityBinary? currentBinary = null, Exception? liveError = null, Exception? controlError = null)
    {
        if ((live is null) == (control is null)) return;
        // A failed identity read is quarantined immediately after this admission
        // check; do not let a sibling's stale binary abort before its evidence can
        // be archived byte-for-byte.
        if ((live is null && liveError is not null) || (control is null && controlError is not null)) return;
        RunAuthority recorded = live ?? control!;
        RunAuthorityBinary current = currentBinary ?? RunAuthority.CurrentBinaryIdentity();
        if (!MatchesBinary(recorded.Binary, current))
            throw new IOException($"paired gate cannot mix a completed arm from another binary with a fresh peer: {recorded.RunID}");
    }

    private static bool MatchesBinary(RunAuthorityBinary recorded, RunAuthorityBinary current)
        => string.Equals(recorded.ProcessName, current.ProcessName, StringComparison.Ordinal)
            && string.Equals(recorded.ProcessSHA256, current.ProcessSHA256, StringComparison.Ordinal)
            && string.Equals(recorded.AssemblyName, current.AssemblyName, StringComparison.Ordinal)
            && string.Equals(recorded.AssemblySHA256, current.AssemblySHA256, StringComparison.Ordinal);

    private static string ArchiveInterrupted(string path, Exception? incomplete)
    {
        string archived;
        for (int index = 0; ; index++)
        {
            archived = path + $".interrupted-{index:D4}";
            if (!Directory.Exists(archived) && !File.Exists(archived)) break;
        }
        Directory.Move(path, archived);
        string marker = $"canonical={Path.GetFileName(path)}\nattempt={Path.GetFileName(archived)}\nstatus=interrupted\nreason={(incomplete?.GetType().Name ?? "unknown")}\n";
        File.WriteAllText(archived + ".txt", marker);
        return archived;
    }

    private static string ArchiveStaleCompleted(string path, RunAuthority authority, RunAuthorityBinary current)
    {
        string archived;
        for (int index = 0; ; index++)
        {
            archived = path + $".stale-binary-{index:D4}";
            if (!Directory.Exists(archived) && !File.Exists(archived)) break;
        }
        Directory.Move(path, archived);
        string marker = $"canonical={Path.GetFileName(path)}\nattempt={Path.GetFileName(archived)}\nstatus=stale-binary\nrun-id={authority.RunID}\nrecorded-process={authority.Binary.ProcessName}:{authority.Binary.ProcessSHA256}\ncurrent-process={current.ProcessName}:{current.ProcessSHA256}\nrecorded-assembly={authority.Binary.AssemblyName}:{authority.Binary.AssemblySHA256}\ncurrent-assembly={current.AssemblyName}:{current.AssemblySHA256}\n";
        File.WriteAllText(archived + ".txt", marker);
        return archived;
    }

    private static void ArchiveReportBeforeArmReplacement(string path, string liveName, string controlName, RunAuthority? liveIdentity, RunAuthority? controlIdentity, RunAuthorityBinary current, Exception? liveError = null, Exception? controlError = null)
    {
        if (!File.Exists(path))
        {
            if (Directory.Exists(path)) throw new IOException($"paired gate report destination is a directory: {path}");
            return;
        }

        // A report is reusable only when both arm identities are available and the
        // complete read-only arm validation (including checkpoint Vow) succeeds. A
        // failed identity is stale evidence, not an invitation to overwrite it.
        if (liveIdentity is null || controlIdentity is null)
        {
            string archived = ArchiveInvalidStaleReport(path, liveName, controlName, current,
                liveError ?? controlError ?? new InvalidDataException("paired gate report has no two valid arm identities"));
            Trace.Note($"paired gate · archived invalid stale report {Path.GetFileName(path)} → {Path.GetFileName(archived)}");
            return;
        }

        string live = global::Cogito.Run.HomePath(liveName);
        string control = global::Cogito.Run.HomePath(controlName);
        bool liveCurrent = MatchesBinary(liveIdentity.Binary, current);
        bool controlCurrent = MatchesBinary(controlIdentity.Binary, current);
        try
        {
            PairedGateReport report = PairedGateAdjudicator.ReadReport(path);
            if (!string.Equals(report.Live.RunID, liveName, StringComparison.Ordinal)
                || !string.Equals(report.Control.RunID, controlName, StringComparison.Ordinal))
                throw new IOException($"paired gate report collision is bound to a different run pair: {path}");
            if (!liveCurrent || !controlCurrent)
            {
                bool coherentHistoricalPair = !liveCurrent && !controlCurrent
                    && MatchesBinary(liveIdentity.Binary, controlIdentity.Binary);
                if (!coherentHistoricalPair)
                    throw new InvalidDataException("paired gate stale report is not bound to one sealed historical binary pair");
                PairedGateAdjudicator.ValidateHistoricalReportIdentity(report, live, control);
                string archived = ArchiveStalePairReport(path, report, live, control, current);
                Trace.Note($"paired gate · archived stale pair report {Path.GetFileName(path)} → {Path.GetFileName(archived)}");
                return;
            }

            PairedGateAdjudicator.ValidateReportIdentity(report, live, control);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            string archived = ArchiveInvalidStaleReport(path, liveName, controlName, current, ex);
            Trace.Note($"paired gate · archived invalid stale report {Path.GetFileName(path)} → {Path.GetFileName(archived)}");
        }
    }

    private static PairedGateReport ProduceReport(string live, string control, string path, IPolicyBoundaryDomain domain)
    {
        if (!File.Exists(path)) return PairedGateAdjudicator.Adjudicate(live, control, domain, path);
        if (Directory.Exists(path)) throw new IOException($"paired gate report destination is a directory: {path}");
        PairedGateReport report = PairedGateAdjudicator.ReadReport(path);
        if (!string.Equals(report.Live.RunID, Path.GetFileName(live), StringComparison.Ordinal)
            || !string.Equals(report.Control.RunID, Path.GetFileName(control), StringComparison.Ordinal))
            throw new IOException($"paired gate report collision is bound to a different run pair: {path}");
        PairedGateAdjudicator.ValidateReportIdentity(report, live, control);
        return report;
    }

    private static string ArchiveStalePairReport(string path, PairedGateReport report, string historicalLive, string historicalControl, RunAuthorityBinary current)
    {
        string parent = Path.GetDirectoryName(path) ?? throw new IOException($"paired gate report has no parent directory: {path}");
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string archived;
        string marker;
        for (int index = 0; ; index++)
        {
            archived = Path.Combine(parent, $"{stem}.stale-pair-{index:D4}{extension}");
            marker = Path.ChangeExtension(archived, ".txt");
            if (!File.Exists(archived) && !Directory.Exists(archived) && !File.Exists(marker) && !Directory.Exists(marker)) break;
        }

        byte[] bytes = File.ReadAllBytes(path);
        File.Move(path, archived);
        string reportSHA256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        File.WriteAllText(marker,
            $"canonical={Path.GetFileName(path)}\n"
            + $"attempt={Path.GetFileName(archived)}\n"
            + "status=stale-pair\n"
            + $"report-sha256={reportSHA256}\n"
            + $"report-digest={report.Digest}\n"
            + $"historical-live={Path.GetFileName(historicalLive)}:{report.Live.AuthorityDigest}\n"
            + $"historical-control={Path.GetFileName(historicalControl)}:{report.Control.AuthorityDigest}\n"
            + $"current-process={current.ProcessName}:{current.ProcessSHA256}\n"
            + $"current-assembly={current.AssemblyName}:{current.AssemblySHA256}\n");
        return archived;
    }

    private static string ArchiveInvalidStale(string path, Exception? reason)
    {
        string archived;
        for (int index = 0; ; index++)
        {
            archived = path + $".invalid-stale-{index:D4}";
            if (!Directory.Exists(archived) && !File.Exists(archived)) break;
        }
        Directory.Move(path, archived);
        string detail = reason is null ? "unknown" : $"{reason.GetType().Name}: {reason.Message}".Replace('\n', ' ').Replace('\r', ' ');
        File.WriteAllText(archived + ".txt",
            $"canonical={Path.GetFileName(path)}\n"
            + $"attempt={Path.GetFileName(archived)}\n"
            + "status=invalid-stale\n"
            + "kind=arm\n"
            + $"reason={detail}\n");
        return archived;
    }

    private static string ArchiveInvalidStaleReport(string path, string historicalLive, string historicalControl, RunAuthorityBinary current, Exception reason)
    {
        string parent = Path.GetDirectoryName(path) ?? throw new IOException($"paired gate report has no parent directory: {path}");
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string archived;
        string marker;
        for (int index = 0; ; index++)
        {
            archived = Path.Combine(parent, $"{stem}.invalid-stale-{index:D4}{extension}");
            marker = Path.ChangeExtension(archived, ".txt");
            if (!File.Exists(archived) && !Directory.Exists(archived) && !File.Exists(marker) && !Directory.Exists(marker)) break;
        }

        byte[] bytes = File.ReadAllBytes(path);
        File.Move(path, archived);
        string reportSHA256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        string detail = $"{reason.GetType().Name}: {reason.Message}".Replace('\n', ' ').Replace('\r', ' ');
        File.WriteAllText(marker,
            $"canonical={Path.GetFileName(path)}\n"
            + $"attempt={Path.GetFileName(archived)}\n"
            + "status=invalid-stale\n"
            + "kind=paired-report\n"
            + $"report-sha256={reportSHA256}\n"
            + $"historical-live={Path.GetFileName(historicalLive)}\n"
            + $"historical-control={Path.GetFileName(historicalControl)}\n"
            + $"current-process={current.ProcessName}:{current.ProcessSHA256}\n"
            + $"current-assembly={current.AssemblyName}:{current.AssemblySHA256}\n"
            + $"reason={detail}\n");
        return archived;
    }

    private readonly record struct SeedSpec(string Label, ulong Value);
    private readonly record struct PreparedArm(global::Cogito.Run Run, bool Reused);
}
