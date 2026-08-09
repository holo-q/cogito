namespace Cogito;

using Ronmamon;
using Ronmamon.Reader;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

/// Cheap production-side gate for the owner receipt dialect. It exercises the same RON/digest validator used
/// by the live step-zero callback without running the 180-step composite fixture.
internal static class HomeostatDestinationHandshakeFixture
{
    internal static bool Run(TextWriter output)
    {
        string Digest(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        HomeostatPolicyProgram program = HomeostatPolicyProgram.ParseToken("sl:h,mx:r,in:r,bb:r,br:c,fg:d");
        HomeostatPolicyContext context = HomeostatPolicyContext.ParseToken("c:rx,w:0,g:0");
        int action = Homeostat.FindDestinationPolicyAction(in program);
        CortexPolicyDecisionReadout readout = new(
            action, -1, -1, action, CortexPolicyAuthorities.Launchpad,
            new global::Cogito.Grammar.GrammarRevisionID(1), CortexPolicySelectionCauses.Launchpad);
        HomeostatDestinationHandshakeReceipt receipt = new()
        {
            decisionID = 1,
            policy = Homeostat.PolicyID.Value,
            physicalStep = 0,
            source = "explicit",
            launchpadAction = readout.LaunchpadAction,
            rawCandidateAction = readout.RawCandidateAction,
            selectedCandidateAction = readout.SelectedCandidateAction,
            executedAction = readout.ExecutedAction,
            authority = readout.Authority,
            grammarRevision = readout.GrammarRevision.Value,
            selectionCause = readout.SelectionCause,
            readoutFingerprint = GrammarPolicyReadout.ComputeFingerprint(readout.GrammarRevision, Homeostat.PolicyID),
            policyProgram = program.RenderToken(),
            policyContext = context.RenderToken(),
            sleepFrac = 1.0 / 8,
            mixEvery = 8,
            intakeBatch = 4,
            budgetBits = 0,
            breachQuota = 0,
            forceGeneralize = false,
        };
        receipt.receiptDigest = receipt.ComputeDigest();
        byte[] bytes = HomeostatDestinationHandshakeReceipt.Encode(in receipt);
        HomeostatDestinationHandshakeReceipt restored = HomeostatDestinationHandshakeReceipt.Decode(bytes);
        bool saveLoadSave = bytes.AsSpan().SequenceEqual(HomeostatDestinationHandshakeReceipt.Encode(in restored));
        bool rejects = Rejects(() => HomeostatDestinationHandshakeReceipt.Decode(bytes[..^1]));
        HomeostatDestinationHandshakeReceipt wrongStep = Clone(receipt, physicalStep: 1);
        HomeostatDestinationHandshakeReceipt wrongPolicy = Clone(receipt, policy: "spoof.policy");
        HomeostatDestinationHandshakeReceipt wrongReadout = Clone(receipt, executedAction: 1);
        bool wrongStepRejected = Rejects(() => wrongStep.ValidateForPhysicalStep(0));
        bool wrongPolicyRejected = Rejects(() => HomeostatDestinationHandshakeReceipt.Encode(in wrongPolicy));
        bool wrongReadoutRejected = Rejects(() => HomeostatDestinationHandshakeReceipt.Encode(in wrongReadout));
        PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingMountFixture.CreateFixtureTraining(
            "parent", "calibration", Digest("cold"), Digest("config"), Digest("checkpoint"), 1);
        PolicyBoundaryMountReceipt mount = PolicyBoundaryMountReceipt.CreateVerifiedAfterHandshake(
            in training, "evaluation", 1, 2, 0,
            receipt.readoutFingerprint, receipt.grammarRevision, in receipt, HomeostatPolicyBoundaryDomain.Instance);
        byte[] mountPacket = TapePacketCreator.EncodePolicyBoundaryTrainingMount(in mount);
        bool mountPacketRecovery = TapePacketCreator.TryReadPolicyBoundaryTrainingMount(mountPacket, in mount);
        string mountPacketText = Encoding.ASCII.GetString(mountPacket);
        bool mountPacketDigestRejects = !TapePacketCreator.TryReadPolicyBoundaryTrainingMount(
            Encoding.ASCII.GetBytes(mountPacketText.Replace(receipt.receiptDigest, new string('0', 64), StringComparison.Ordinal)), in mount);
        bool mountPacketDecisionRejects = !TapePacketCreator.TryReadPolicyBoundaryTrainingMount(
            Encoding.ASCII.GetBytes(mountPacketText.Replace("destination-handshake-decision-id=1", "destination-handshake-decision-id=2", StringComparison.Ordinal)), in mount);
        HomeoActuation grantHold = new(1.0 / 8, 8, 4, 0, 1, false);
        int parityAmplitude = Homeostat.ComputeCandidateBreachAmplitude(
            HomeoConditions.Stalled, currentBreachAmplitude: 128, breachQuotaBase: 128, in grantHold);
        bool directParity = parityAmplitude == 256;
        bool production = VerifyProductionPath(output);
        bool passed = saveLoadSave && rejects && wrongStepRejected && wrongPolicyRejected && wrongReadoutRejected
            && mountPacketRecovery && mountPacketDigestRejects && mountPacketDecisionRejects && directParity && production;
        output.WriteLine($"  Homeostat destination handshake fixture · owner=explicit · step0={(receipt.physicalStep == 0 ? "observed" : "BROKEN")} · SaveLoadSave={(saveLoadSave ? "exact" : "BROKEN")} · corruption={(rejects ? "rejected" : "ACCEPTED")} · wrong-step={(wrongStepRejected ? "rejected" : "ACCEPTED")} · wrong-policy={(wrongPolicyRejected ? "rejected" : "ACCEPTED")} · readout={(wrongReadoutRejected ? "rejected" : "ACCEPTED")} · tape-owner={(mountPacketRecovery && mountPacketDigestRejects && mountPacketDecisionRejects ? "bound" : "BROKEN")} · parity={(directParity ? "128→256" : "BROKEN")} · production={(production ? "4-step" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyProductionPath(TextWriter output)
    {
        string corpusPath = Path.GetFullPath(Path.Combine(".tmp", $"homeostat-destination-handshake-{Guid.NewGuid():N}.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(corpusPath)!);
        File.WriteAllText(corpusPath, "alpha beta gamma\nalpha beta delta\nalpha beta epsilon\n");
        CortexFlatPoolCurriculum curriculum = new()
        {
            Corpus = new CogitoCorpus { Path = corpusPath, Glob = "*.txt" },
            IntakeBatch = 1,
            SeedSpans = 1,
            MixEvery = 1,
        };
        CortexConfig config = new()
        {
            RunName = $"homeostat-destination-handshake-{Guid.NewGuid():N}",
            Steps = 1,
            Seed = 0xD35A710FUL,
            Curriculum = curriculum,
            ActionsPerStep = 1,
            Learning = new CortexLearningConfig
            {
                ConsolidationPhaseControl = CortexConsolidationPhaseControl.Homeostat,
                Rhythm = true,
                IntervalConsolidationPhase = 1,
                Homeostat = new CortexHomeostatConfig { Autonomy = HomeostatAutonomyModes.Full },
                Policies = new CortexPolicyLearningConfig { ShadowDecisions = 1, ProposalInterval = 2, TrialHorizons = [1] },
            },
        };
        string? parentDirectory = null;
        PolicyBoundaryObligation? liveObligation = null;
        CortexForkRunReceipt<HomeostatDestinationHandshakeReceipt>? childReceipt = null;
        string? childDirectory = null;
        try
        {
            Cortex parent = new(config);
            CortexForkSeed? seed = null;
            int parentExit = parent.CaptureColdForkSeedSetup((runtime, window) =>
            {
                if (window != new CortexExecutionWindow(0, 0))
                    throw new InvalidDataException("destination fixture cold setup did not bind the S0 window");
                if (!runtime.TryGetPolicyBoundaryObligation(Homeostat.PolicyID, out PolicyBoundaryObligation obligation))
                    throw new InvalidDataException("destination fixture did not register the Homeostat boundary obligation");
                liveObligation = obligation;
                seed = runtime.MaterializeColdForkSeed();
                parentDirectory = runtime.CurrentRun.Dir;
            });
            if (parentExit != 0 || seed is null || parentDirectory is null) return false;
            if (seed.NextStep != 0) throw new InvalidDataException("destination fixture cold seed is not S0");
            Cogito.Run parentRun = Cogito.Run.Open(parentDirectory);
            string parentID = Path.GetFileName(parentDirectory);
            Cogito.Run sourceRun = parentRun.CreateChildRun(CortexForkRailRoles.Calibration);
            string sourceID = Path.GetFileName(sourceRun.Dir);
            PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingMountFixture.CreateDestinationHandshakeTraining(
                parentID, sourceID, seed.ColdSeedDigest, seed.PersistedConfigDigest, seed.Digests.CheckpointSHA256, 1,
                liveObligation?.ID ?? throw new InvalidDataException("destination fixture lost its Homeostat boundary obligation"));
            byte[] trainingBytes = PolicyBoundaryTrainingReceipt.Encode(in training, HomeostatPolicyBoundaryDomain.Instance);
            sourceRun.WriteAtomic("policy-boundary.training.ron", stream => stream.Write(trainingBytes));
            PolicyBoundaryTrainingReceipt.Decode(File.ReadAllBytes(sourceRun.PathOf("policy-boundary.training.ron")), HomeostatPolicyBoundaryDomain.Instance).Validate(HomeostatPolicyBoundaryDomain.Instance);

            string evaluationID = parentRun.NextChildRunID(CortexForkRailRoles.Evaluation);
            childDirectory = Path.Combine(parentDirectory, "children", evaluationID);
            HomeostatDestinationHandshakeReceipt? mountedHandshake = null;
            PolicyBoundaryMountReceipt? mountedReceipt = null;
            long handshakeRawTicks = 0;
            long mountRawTicks = 0;
            CortexForkArm<HomeostatDestinationHandshakeReceipt> arm = new(
                childDirectory,
                () => new Cortex(config),
                runtime => mountedHandshake ?? throw new InvalidDataException("destination fixture did not emit a handshake"),
                afterCompletedStep: (runtime, completedStep) =>
                {
                    if (mountedHandshake is not null) return;
                    if (completedStep != 0) throw new InvalidDataException($"destination fixture callback landed at step {completedStep}");
                    (PolicyBoundaryMountReceipt mounted, _, _, long handshakeRaw, long mountRaw, HomeostatDestinationHandshakeReceipt handshake, _, _) =
                        runtime.MountDestinationHandshake(0, in training, 2);
                    mountedHandshake = handshake;
                    mountedReceipt = mounted;
                    handshakeRawTicks = handshakeRaw;
                    mountRawTicks = mountRaw;
                },
                beforeCompletedStep: static (runtime, completedStep) =>
                {
                    if (completedStep != 0) return;
                    runtime.CaptureHomeostatDestinationHandshake(completedStep, forceExplicit: true);
                    runtime.CaptureHomeostatDestinationHandshake(completedStep, forceExplicit: true);
                },
                railRole: CortexForkRailRoles.Evaluation,
                parentRunID: parentID,
                materializationContract: new CortexForkMaterializationContract(parentID, evaluationID, evaluationID, seed.ColdSeedDigest));
            childReceipt = CortexForkRunner.RunFork(parent, seed, arm, checked(seed.NextStep + 4));
            if (mountedHandshake is null || mountedReceipt is null) return false;
            string evaluationDirectory = childDirectory;
            HomeostatDestinationHandshakeReceipt handshake = mountedHandshake;
            string handshakeDiskPath = Path.Combine(evaluationDirectory, "homeostat.destination-handshake.ron");
            HomeostatDestinationHandshakeReceipt handshakeDisk = File.Exists(handshakeDiskPath)
                ? HomeostatDestinationHandshakeReceipt.Decode(File.ReadAllBytes(handshakeDiskPath))
                : throw new InvalidDataException("destination handshake sidecar is missing");
            bool handshakeDiskExact = handshakeDisk.ReceiptDigest == handshake.ReceiptDigest
                && handshakeDisk.DecisionID == handshake.DecisionID;
            bool mountExists = File.Exists(Path.Combine(evaluationDirectory, "policy-boundary.mount.ron"));
            bool mountRoundTrip = mountedReceipt.ReceiptDigest == PolicyBoundaryMountReceipt.Decode(
                File.ReadAllBytes(Path.Combine(evaluationDirectory, "policy-boundary.mount.ron")), in training, HomeostatPolicyBoundaryDomain.Instance).ReceiptDigest;
            string journalPath = Path.Combine(evaluationDirectory, "journal.log");
            bool tapePacket;
            using (Tape finalTape = Checkpoint.LoadTape(evaluationDirectory))
            {
                tapePacket = finalTape.GetEventViews().Any(view =>
                {
                    if (!string.Equals(view.Source, "policy-boundary:mount", StringComparison.Ordinal)
                        || !finalTape.Resolve(view.Id, out byte[] packet)) return false;
                    return TapePacketCreator.TryReadPolicyBoundaryTrainingMount(packet, in mountedReceipt);
                });
            }
            bool journalOwnerExact = File.Exists(journalPath)
                && TryReadJournalOwner(File.ReadAllLines(journalPath), handshake, mountedReceipt);
            string homeostatReport = File.Exists(Path.Combine(evaluationDirectory, "homeostat.txt"))
                ? File.ReadAllText(Path.Combine(evaluationDirectory, "homeostat.txt"))
                : "";
            Match conservation = Regex.Match(homeostatReport, @"decision conservation: (\d+)/(\d+) closed · unresolved (\d+)");
            bool conservationExact = conservation.Success
                && conservation.Groups[1].Value == "3"
                && conservation.Groups[2].Value == "4"
                && int.Parse(conservation.Groups[3].Value, CultureInfo.InvariantCulture) == 1;
            string computePath = Path.Combine(evaluationDirectory, "compute.tsv");
            // The handshake step schedules no aestivation; the compute row still carries sub-ms
            // phase-bookkeeping residue from closing the runtime boundary.
            bool computeStepZeroSleepMeasured = TryReadComputeStepZeroSleep(computePath, out double stepZeroSleepMilliseconds)
                && double.IsFinite(stepZeroSleepMilliseconds) && stepZeroSleepMilliseconds < 1.0;
            bool settled = childReceipt.ExitCode == 0 && conservationExact && computeStepZeroSleepMeasured;
            bool production = childReceipt.StepSpan.ActualNextStep == 4
                && mountedReceipt.MountStep == 0
                && handshake.PhysicalStep == 0
                && !handshake.IsNatural
                && handshake.DecisionID > 0
                && handshakeRawTicks > 0
                && mountRawTicks > 0
                && string.Equals(handshake.source, "explicit", StringComparison.Ordinal)
                && handshakeDiskExact && journalOwnerExact
                && mountExists && mountRoundTrip && tapePacket && settled;
            output.WriteLine($"  Homeostat destination handshake production · physical={childReceipt.StepSpan.ActualNextStep} · source={handshake.source} · decision={handshake.DecisionID} · mount={(mountExists && mountRoundTrip ? "sidecar" : "MISSING")} · tape={(tapePacket ? "packet" : "MISSING")} · conservation={(conservationExact ? "exact" : "BROKEN")} · sleep0={(computeStepZeroSleepMeasured ? "sub-ms" : "BROKEN")}({stepZeroSleepMilliseconds.ToString("G17", CultureInfo.InvariantCulture)}) · settlement={(settled ? "closed" : "OPEN")} · {(production ? "PASS" : "FAIL")}");
            return production;
        }
        finally
        {
            if (parentDirectory is not null && Directory.Exists(parentDirectory))
                Directory.Delete(parentDirectory, recursive: true);
            if (File.Exists(corpusPath)) File.Delete(corpusPath);
        }
    }

    private static bool TryReadComputeStepZeroSleep(string path, out double sleepMilliseconds)
    {
        sleepMilliseconds = double.NaN;
        if (!File.Exists(path)) return false;
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) return false;
        string[] header = lines[0].Split('\t');
        int step = Array.IndexOf(header, "step");
        int sleep = Array.IndexOf(header, "sleep_ms");
        if (step < 0 || sleep < 0) return false;
        foreach (string line in lines[1..])
        {
            string[] values = line.Split('\t');
            if (values.Length <= Math.Max(step, sleep) || values[step] != "0") continue;
            return double.TryParse(values[sleep], NumberStyles.Float, CultureInfo.InvariantCulture, out sleepMilliseconds);
        }
        return false;
    }

    private static bool TryReadJournalOwner(
        string[] lines, HomeostatDestinationHandshakeReceipt handshake, PolicyBoundaryMountReceipt mount)
    {
        string decision = $"destination-handshake-decision-id={handshake.DecisionID}";
        string digest = $"destination-handshake-digest={handshake.ReceiptDigest}";
        string receipt = $"receipt={mount.ReceiptDigest}";
        return lines.Any(line => line.Contains("policy-boundary-mount", StringComparison.Ordinal)
            && line.Contains(decision, StringComparison.Ordinal)
            && line.Contains(digest, StringComparison.Ordinal)
            && line.Contains(receipt, StringComparison.Ordinal));
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is InvalidDataException or RonReadException or FormatException) { return true; }
    }

    private static HomeostatDestinationHandshakeReceipt Clone(
        HomeostatDestinationHandshakeReceipt source,
        int? physicalStep = null,
        string? policy = null,
        int? executedAction = null)
    {
        HomeostatDestinationHandshakeReceipt clone = new()
        {
            schemaVersion = source.schemaVersion,
            decisionID = source.decisionID,
            policy = policy ?? source.policy,
            physicalStep = physicalStep ?? source.physicalStep,
            source = source.source,
            launchpadAction = source.launchpadAction,
            rawCandidateAction = source.rawCandidateAction,
            selectedCandidateAction = source.selectedCandidateAction,
            executedAction = executedAction ?? source.executedAction,
            authority = source.authority,
            grammarRevision = source.grammarRevision,
            selectionCause = source.selectionCause,
            readoutFingerprint = source.readoutFingerprint,
            readoutCandidateFingerprint = source.readoutCandidateFingerprint,
            readoutCandidateOccurrenceDigest = source.readoutCandidateOccurrenceDigest,
            policyProgram = source.policyProgram,
            policyContext = source.policyContext,
            sleepFrac = source.sleepFrac,
            mixEvery = source.mixEvery,
            intakeBatch = source.intakeBatch,
            budgetBits = source.budgetBits,
            breachQuota = source.breachQuota,
            forceGeneralize = source.forceGeneralize,
        };
        clone.receiptDigest = clone.ComputeDigest();
        return clone;
    }
}
