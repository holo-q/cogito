namespace Cogito;

using System.Numerics;
using System.Text;
using Cogito.Induct;

internal static partial class EmlIntensionalRematchRunner
{
    private const int TrialsPerReplicate = 64;
    private const int HoleBranchRadius = 2;
    private const string LogExpTemplate = "11?E1EE1E = ?";

    internal static string ReadArmName(EmlIntensionalRematchArms arm) => arm switch
    {
        EmlIntensionalRematchArms.FreshEnumeration => "fresh-enumeration",
        EmlIntensionalRematchArms.ObligationHoleSolve => "obligation-hole-solve",
        EmlIntensionalRematchArms.GuardedBinding => "guarded-binding",
        EmlIntensionalRematchArms.BindingShuffledNull => "binding-shuffled-null",
        EmlIntensionalRematchArms.LawCandidateShadow => "law-candidate-shadow",
        EmlIntensionalRematchArms.NoLaw => "no-law",
        EmlIntensionalRematchArms.LawShuffledNull => "law-relation-null",
        _ => throw new ArgumentOutOfRangeException(nameof(arm)),
    };

    internal static (bool FirstVerified, bool SecondVerified, long FirstEvaluatorCalls, long SecondEvaluatorCalls)
        ProbeReusedLawCandidate(
            int signatureDigits,
            byte[] baseImage,
            List<EmlHoleCandidate> bindings,
            EmlLawStore lawStore,
            in EmlLawCandidateInstantiation candidate)
    {
        byte[] lawImage = SaveLawStore(lawStore);
        ConcreteArm arm = new(
            EmlIntensionalRematchArms.LawCandidateShadow,
            "reused-law-probe",
            signatureDigits,
            baseImage,
            bindings,
            lawImage,
            scheduledTrials: 2,
            deliberationEpoch: "mint-frontier");
        var first = arm.ExecuteLawForProbe(in candidate);
        var second = arm.ExecuteLawForProbe(in candidate);
        return (first.Verified, second.Verified, first.EvaluatorCalls, second.EvaluatorCalls);
    }

    public static int RunMatched(
        ulong seed,
        long evaluatorCalls,
        long descendantDelayEvaluatorCalls,
        int independentReplicates,
        int signatureDigits)
    {
        if (independentReplicates <= 0)
            throw new ArgumentOutOfRangeException(nameof(independentReplicates), independentReplicates,
                "rematch replicates must be positive");
        EmlIntensionalRematchConfig config = new(
            evaluatorCalls,
            descendantDelayEvaluatorCalls,
            independentReplicates);
        Run receipt = Run.New("eml-intensional-rematch");
        string runPath = Path.GetRelativePath(Environment.CurrentDirectory, receipt.Dir);
        Console.WriteLine($"  EML intensional rematch plan -> {runPath}/");
        Console.WriteLine($"  plan evaluator={evaluatorCalls:N0} delay={descendantDelayEvaluatorCalls:N0} replicates={independentReplicates:N0} trials={TrialsPerReplicate:N0}");
        StringBuilder output = new("section\tname\tmetric\tvalue\n");
        Dictionary<string, int> graduatedByContrast = new(StringComparer.Ordinal);
        int poweredReplicates = 0;
        bool valid = true;
        for (int replicate = 0; replicate < independentReplicates; replicate++)
        {
            ulong replicateSeed = MixReplicateSeed(seed, replicate);
            Console.WriteLine($"  rematch replicate {replicate + 1:N0}/{independentReplicates:N0} start seed={replicateSeed:X16}");
            EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
            EmlSieve baseSieve = fixture.Sieve;
            byte[] baseImage = fixture.AdmissionImage;
            List<EmlHoleCandidate> bindings = fixture.Bindings;
            List<EmlObligationResolution> obligations = fixture.Obligations;
            EmlLawStore lawStore = BuildLawStore(baseSieve, replicateSeed, signatureDigits);
            byte[] lawImage = SaveLawStore(lawStore);
            List<EmlIntensionalRematchTrialSeed> seeds = BuildScheduleSeeds(
                replicateSeed,
                baseSieve,
                obligations,
                bindings,
                lawStore);

            string invalidDetail = ValidateSupply(obligations, bindings, lawStore, seeds);
            if (invalidDetail.Length > 0)
            {
                output.AppendLine($"replicate\t{replicate}\tassay_status\t{EmlRematchAssayStatuses.Invalid}");
                output.AppendLine($"replicate\t{replicate}\tassay_detail\t{invalidDetail}");
                valid = false;
                continue;
            }

            EmlIntensionalRematchSchedule schedule = EmlIntensionalRematchSchedule.Create(replicateSeed, seeds);
            IEmlIntensionalRematchArm[] arms = CreateArms(
                signatureDigits,
                baseImage,
                bindings,
                lawImage,
                schedule.Trials.Count);
            Console.WriteLine($"  rematch replicate {replicate + 1:N0}/{independentReplicates:N0} schedule-ready arms={arms.Length:N0} trials={schedule.Trials.Count:N0}");
            EmlIntensionalRematchReport report = EmlIntensionalRematch.Run(
                in config,
                schedule,
                arms,
                progress => Console.WriteLine(
                    $"  rematch replicate {replicate + 1:N0}/{independentReplicates:N0} arm={progress.Arm} phase={progress.Phase} "
                    + $"{(progress.Completed ? "complete" : "start")} calls={progress.EvaluatorCalls:N0} trials={progress.ExecutedTrials:N0}/{progress.ScheduledTrials:N0}"));
            output.AppendLine($"replicate\t{replicate}\tseed\t{replicateSeed:X16}");
            AppendReplicateReport(output, replicate, report);
            output.AppendLine($"base-{replicate}\trematch\tcheckpoint_bytes\t{baseImage.Length}");
            output.AppendLine($"base-{replicate}\trematch\tobligations\t{obligations.Count}");
            output.AppendLine($"base-{replicate}\trematch\tbindings\t{bindings.Count}");
            output.AppendLine($"base-{replicate}\trematch\tlaw_classes\t{lawStore.Count}");
            for (int i = 0; i < report.Arms.Count; i++) valid &= report.Arms[i].AssayExact;
            EmlIntensionalRematchArmReport lawArm = report.Arms.First(static arm => arm.Kind == EmlIntensionalRematchArms.LawCandidateShadow);
            EmlIntensionalRematchArmReport nullArm = report.Arms.First(static arm => arm.Kind == EmlIntensionalRematchArms.LawShuffledNull);
            bool powered = lawArm.PowerStatus == EmlRematchPowerStatuses.Powered
                && nullArm.PowerStatus == EmlRematchPowerStatuses.Powered;
            if (powered) poweredReplicates++;
            for (int i = 0; i < report.Contrasts.Count; i++)
            {
                EmlIntensionalRematchContrastReport contrast = report.Contrasts[i];
                if (contrast.Graduation != EmlRematchGraduationStatuses.Graduated) continue;
                graduatedByContrast[contrast.Name] = graduatedByContrast.GetValueOrDefault(contrast.Name) + 1;
            }
            Console.WriteLine($"  rematch replicate {replicate + 1:N0}/{independentReplicates:N0} complete powered={(powered ? 1 : 0)}");
        }
        output.AppendLine($"consensus\trung0-powered\tpowered_replicates\t{poweredReplicates}");
        output.AppendLine($"consensus\trung0-powered\tpowered_all\t{(poweredReplicates == independentReplicates ? 1 : 0)}");
        string[] contrastNames = ["obligation-hole-solve", "guarded-binding", "law-candidate-shadow"];
        for (int i = 0; i < contrastNames.Length; i++)
        {
            string contrast = contrastNames[i];
            int graduated = graduatedByContrast.GetValueOrDefault(contrast);
            output.AppendLine($"consensus\t{contrast}\tgraduated_replicates\t{graduated}");
            output.AppendLine($"consensus\t{contrast}\tgraduated_all\t{(graduated == independentReplicates ? 1 : 0)}");
        }
        receipt.Write("eml_intensional_rematch.tsv", output.ToString());
        Console.WriteLine($"  EML intensional rematch -> {Path.GetRelativePath(Environment.CurrentDirectory, receipt.PathOf("eml_intensional_rematch.tsv"))}");
        Console.WriteLine($"  {independentReplicates:N0} isolated replicates · {TrialsPerReplicate:N0} trials each · evaluator {evaluatorCalls:N0}+{descendantDelayEvaluatorCalls:N0} per arm");
        return valid ? 0 : 1;
    }

    private static EmlLawStore BuildLawStore(EmlSieve sieve, ulong seed, int signatureDigits)
    {
        EmlLawStore store = new();
        bool hasCalibrationLaw = AdmitLogExpLaw(
            store,
            signatureDigits,
            0,
            out EmlLawBehaviorCertificate calibrationCertificate);
        byte[] exactCorpus = sieve.TierBytes(static mint => mint.Grade == 'E');
        RePairResult grammar = Engine.Induce(exactCorpus).Result;
        List<EmlLawCandidate> candidates = EmlAntiUnify.DiscoverCandidates(sieve, grammar, seed);
        for (int i = 0; i < candidates.Count; i++)
        {
            EmlLawCandidate candidate = candidates[i];
            EmlLaw law = candidate.Law;
            if (!EmlVerifiedLaw.TryVerify(in law, candidate.Support, signatureDigits,
                    out EmlVerifiedLaw? verified)
                || verified is null) continue;
            // The rematch's control row is the exact guarded LogExp law.  An induced
            // lower-cost law with the same probe behavior must not replace that
            // authority in the semantic class, or the powered control disappears
            // nondeterministically with the replicate seed.
            if (hasCalibrationLaw && verified.Certificate == calibrationCertificate) continue;
            store.TryAdmit(verified, i + 1,
                out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> ignored);
        }
        return store;
    }

    private static byte[] SaveLawStore(EmlLawStore store)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) store.Save(writer);
        return stream.ToArray();
    }

    private static bool AdmitLogExpLaw(
        EmlLawStore store,
        int signatureDigits,
        int captureIndex,
        out EmlLawBehaviorCertificate certificate)
    {
        EmlGrader grader = new();
        EmlVerdict xVerdict = grader.GradeRpn("11xE1EE1E", "x");
        EmlVerdict yVerdict = grader.GradeRpn("11yE1EE1E", "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in xVerdict, signatureDigits), "11xE1EE1E", "x"),
            new EmlLawPrediction(EmlCert.Of(in yVerdict, signatureDigits), "11yE1EE1E", "y"),
        ];
        EmlLaw law = new(LogExpTemplate, 2, 2, 16.0, "1", "111E1EE1E = 1");
        if (!EmlVerifiedLaw.TryVerify(in law, support, signatureDigits, out EmlVerifiedLaw? verified)
            || verified is null)
        {
            certificate = default;
            return false;
        }
        store.TryAdmit(verified, captureIndex,
            out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> ignored);
        certificate = verified.Certificate;
        return true;
    }

    private static List<EmlIntensionalRematchTrialSeed> BuildScheduleSeeds(
        ulong seed,
        EmlSieve sieve,
        List<EmlObligationResolution> obligations,
        List<EmlHoleCandidate> bindings,
        EmlLawStore lawStore)
    {
        if (obligations.Count == 0 || bindings.Count == 0 || lawStore.Count == 0)
            return new List<EmlIntensionalRematchTrialSeed>();

        Dictionary<int, List<EmlLawCandidateInstantiation>> lawsByObligation = new();
        Dictionary<int, EmlLawCandidateInstantiation> xCalibrationByObligation = new();
        Dictionary<int, EmlLawCandidateInstantiation> yCalibrationByObligation = new();
        bool hasPoweredCalibration = TryFindPoweredCalibrationRewrite(
            sieve,
            lawStore,
            out EmlLawRewrite poweredCalibration);
        for (int i = 0; i < obligations.Count; i++)
        {
            EmlObligationResolution obligation = obligations[i];
            List<EmlLawCandidateInstantiation> candidates = new();
            // This assay lane uses the same claim-bound source enumeration as ordinary Cortex;
            // its calibration rows are selected from those candidates, never rebound by program text.
            lawStore.AppendPredictionBoundCandidateRewrites(in obligation, sieve, candidates);
            lawsByObligation.Add(obligation.SourcePredictionID.Value, candidates);
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                EmlLawCandidateInstantiation candidate = candidates[candidateIndex];
                if (string.Equals(candidate.Rewrite.AntecedentRpn, "11xE1EE1E", StringComparison.Ordinal)
                    && string.Equals(candidate.Rewrite.ConsequentRpn, "x", StringComparison.Ordinal)
                    && string.Equals(candidate.Rewrite.RulePattern, LogExpTemplate, StringComparison.Ordinal))
                    xCalibrationByObligation[obligation.SourcePredictionID.Value] = candidate;
                else if (string.Equals(candidate.Rewrite.AntecedentRpn, "11yE1EE1E", StringComparison.Ordinal)
                    && string.Equals(candidate.Rewrite.ConsequentRpn, "y", StringComparison.Ordinal)
                    && string.Equals(candidate.Rewrite.RulePattern, LogExpTemplate, StringComparison.Ordinal))
                    yCalibrationByObligation[obligation.SourcePredictionID.Value] = candidate;
            }
        }

        List<EmlIntensionalRematchTrialSeed> seeds = new(TrialsPerReplicate);
        Dictionary<int, int> rowsByObligation = new();
        ulong state = seed;
        for (int i = 0; i < TrialsPerReplicate; i++)
        {
            state = EmlGen.Lcg(state);
            EmlObligationResolution obligation = obligations[(int)((state >> 33) % (ulong)obligations.Count)];
            int bindingIndex = i % bindings.Count;
            List<EmlLawCandidateInstantiation> laws = lawsByObligation[obligation.SourcePredictionID.Value];
            int obligationID = obligation.SourcePredictionID.Value;
            int obligationRow = rowsByObligation.GetValueOrDefault(obligationID);
            rowsByObligation[obligationID] = obligationRow + 1;
            EmlLawCandidateInstantiation? law = obligationRow switch
            {
                0 when hasPoweredCalibration
                    => new EmlLawCandidateInstantiation(obligation, poweredCalibration),
                1 when xCalibrationByObligation.TryGetValue(obligationID, out EmlLawCandidateInstantiation xCalibration)
                    => xCalibration,
                2 when yCalibrationByObligation.TryGetValue(obligationID, out EmlLawCandidateInstantiation yCalibration)
                    => yCalibration,
                _ => laws.Count == 0 ? null : laws[i % laws.Count],
            };
            seeds.Add(new EmlIntensionalRematchTrialSeed(
                obligation,
                new EmlIntensionalRematchBindingID(bindingIndex),
                law));
        }
        return seeds;
    }

    private static bool TryFindPoweredCalibrationRewrite(
        EmlSieve sieve,
        EmlLawStore lawStore,
        out EmlLawRewrite calibration)
    {
        // Calibration is a control row, not a generic shortest-edge probe.  The
        // rematch must exercise the exact two guarded laws whose semantics are
        // known: log(exp(x)) -> x or log(exp(y)) -> y.  A same-antecedent edge
        // from the induced corpus is not an equivalent witness.
        string[] antecedents = ["11xE1EE1E", "11yE1EE1E"];
        string[] consequents = ["x", "y"];
        for (int antecedentIndex = 0; antecedentIndex < antecedents.Length; antecedentIndex++)
        {
            string antecedent = antecedents[antecedentIndex];
            IReadOnlyList<EmlExactRPNForm> forms = sieve.ExactRPNLhsForms;
            for (int formIndex = 0; formIndex < forms.Count; formIndex++)
            {
                EmlExactRPNForm form = forms[formIndex];
                if (!string.Equals(form.Program, antecedent, StringComparison.Ordinal)
                    || !sieve.TryCreateRewriteCarrier(in form, out EmlRewritePredictionCarrier carrier)) continue;
            EmlRewriteState state = carrier.CreateState(antecedent);
            List<EmlLawRewrite> rewrites = new();
            lawStore.AppendRewritesForEvaluation(antecedent, rewrites, state.Evaluation);
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count; rewriteIndex++)
            {
                EmlLawRewrite rewrite = rewrites[rewriteIndex];
                if (string.Equals(rewrite.RulePattern, LogExpTemplate, StringComparison.Ordinal)
                    && string.Equals(rewrite.AntecedentRpn, antecedent, StringComparison.Ordinal)
                    && string.Equals(rewrite.ConsequentRpn, consequents[antecedentIndex], StringComparison.Ordinal)
                    && !rewrite.IsRelationNull
                    && rewrite.IsRung0Eligible
                    && rewrite.ConsequentSize < rewrite.AntecedentSize)
                {
                    calibration = rewrite;
                    return true;
                }
            }
            }
        }
        calibration = default;
        return false;
    }

    private static void AppendReplicateReport(
        StringBuilder output,
        int replicate,
        EmlIntensionalRematchReport report)
    {
        using StringReader reader = new(report.FormatTSV());
        reader.ReadLine();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            int tab = line.IndexOf('\t');
            if (tab < 0) continue;
            output.Append(line, 0, tab)
                .Append('-').Append(replicate)
                .Append(line, tab, line.Length - tab)
                .AppendLine();
        }
    }

    private static ulong MixReplicateSeed(ulong seed, int replicate)
    {
        ulong mixed = seed ^ unchecked((ulong)(uint)replicate * 0x9E3779B97F4A7C15UL);
        mixed ^= mixed >> 30;
        mixed *= 0xBF58476D1CE4E5B9UL;
        mixed ^= mixed >> 27;
        mixed *= 0x94D049BB133111EBUL;
        return mixed ^ (mixed >> 31);
    }

    private static string ValidateSupply(
        List<EmlObligationResolution> obligations,
        List<EmlHoleCandidate> bindings,
        EmlLawStore lawStore,
        List<EmlIntensionalRematchTrialSeed> seeds)
    {
        if (obligations.Count == 0) return "finite base state contains no mature obligations";
        if (bindings.Count < 2) return "finite base state contains fewer than two deterministic bindings";
        if (lawStore.Count == 0) return "finite base state contains no forward-verified law class";
        if (seeds.Count == 0) return "finite base state yielded no matched schedule rows";
        HashSet<EmlLawCandidateInstantiation> laws = new();
        for (int i = 0; i < seeds.Count; i++)
            if (seeds[i].LawCandidate is EmlLawCandidateInstantiation law) laws.Add(law);
        if (laws.Count < 2) return "finite base state contains fewer than two deterministic law assignments";
        return "";
    }

    private static IEmlIntensionalRematchArm[] CreateArms(
        int signatureDigits,
        byte[] baseImage,
        List<EmlHoleCandidate> bindings,
        byte[] lawImage,
        int scheduledTrials,
        string deliberationEpoch = "mint-frontier")
        =>
        [
            new ConcreteArm(EmlIntensionalRematchArms.FreshEnumeration, ReadArmName(EmlIntensionalRematchArms.FreshEnumeration), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
            new ConcreteArm(EmlIntensionalRematchArms.ObligationHoleSolve, ReadArmName(EmlIntensionalRematchArms.ObligationHoleSolve), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
            new ConcreteArm(EmlIntensionalRematchArms.GuardedBinding, ReadArmName(EmlIntensionalRematchArms.GuardedBinding), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
            new ConcreteArm(EmlIntensionalRematchArms.BindingShuffledNull, ReadArmName(EmlIntensionalRematchArms.BindingShuffledNull), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
            new ConcreteArm(EmlIntensionalRematchArms.LawCandidateShadow, ReadArmName(EmlIntensionalRematchArms.LawCandidateShadow), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
            new ConcreteArm(EmlIntensionalRematchArms.NoLaw, ReadArmName(EmlIntensionalRematchArms.NoLaw), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
            new ConcreteArm(EmlIntensionalRematchArms.LawShuffledNull, ReadArmName(EmlIntensionalRematchArms.LawShuffledNull), signatureDigits, baseImage, bindings, lawImage, scheduledTrials, deliberationEpoch),
        ];

    private sealed class ConcreteArm : IEmlIntensionalRematchArm
    {
        private static readonly Complex EvaluationX = new(EmlGrader.FeigenbaumDelta, 0);
        private static readonly Complex EvaluationY = new(EmlGrader.FeigenbaumAlpha, 0);
        private readonly int _signatureDigits;
        private readonly List<EmlHoleCandidate> _bindings;
        private readonly string[] _continuation;
        private readonly List<LawRule> _lawRules = new();
        private readonly EmlLawStore _lawStore = new();
        private readonly List<string> _lawFrontier = new();
        private readonly HashSet<string> _lawFrontierSeen = new(StringComparer.Ordinal);
        private readonly EmlSieve _sieve;
        private readonly string _deliberationEpoch;
        private int _continuationCursor;
        private int _lawFrontierCursor;
        private int _rung0Attempts;
        private int _rung0Composed;
        private int _rung0EvaluatorZero;
        private int _rung0Audits;
        private int _rung0UniqueAudits;
        private int _rung0NoCandidates;
        private int _rung0Exhausted;
        private int _rung0GuardRejected;
        private int _rung0ZeroWorkAttempts;
        private int _relationNullExecutions;
        private int _relationNullDivergences;
        private int _relationNullAuthoritativeCompositions;
        private EmlSpeculativeTransactionMetrics _speculativeTransactions;

        public ConcreteArm(
            EmlIntensionalRematchArms kind,
            string name,
            int signatureDigits,
            byte[] baseImage,
            List<EmlHoleCandidate> bindings,
            byte[] lawImage,
            int scheduledTrials,
            string deliberationEpoch)
        {
            Kind = kind;
            Name = name;
            _signatureDigits = signatureDigits;
            _deliberationEpoch = string.IsNullOrWhiteSpace(deliberationEpoch)
                ? throw new ArgumentException("rematch deliberation epoch is required", nameof(deliberationEpoch))
                : deliberationEpoch;
            _bindings = new List<EmlHoleCandidate>(bindings);
            _continuation = BuildContinuation(bindings, scheduledTrials);
            _sieve = EmlRematchFixture.CloneSieve(signatureDigits, baseImage);
            using MemoryStream lawStream = new(lawImage, writable: false);
            using CkptReader lawReader = new(lawStream);
            _lawStore.Load(lawReader);
            List<EmlVerifiedLaw> verifiedLaws = new();
            _lawStore.AppendVerifiedLaws(verifiedLaws);
            for (int i = 0; i < verifiedLaws.Count; i++)
            {
                EmlVerifiedLaw law = verifiedLaws[i];
                if (EmlOneHoleLaw.TryParse(law.Law.Template, out EmlOneHoleLaw parsed))
                    _lawRules.Add(new LawRule(parsed, law.Certificate, law.Proof));
            }
        }

        public EmlIntensionalRematchArms Kind { get; }
        public string Name { get; }
        public EmlEvaluatorClock EvaluatorClock => _sieve.EvaluatorClock;

        public IReadOnlyCollection<EmlCert> CaptureCertificates()
            => new HashSet<EmlCert>(_sieve.Cas.Keys);

        public EmlRung0RematchTelemetry CaptureRung0Telemetry()
            => new EmlRung0RematchTelemetry(
                Attempts: _rung0Attempts,
                Composed: _rung0Composed,
                EvaluatorZeroCompositions: _rung0EvaluatorZero,
                Audits: _rung0Audits,
                UniqueAudits: _rung0UniqueAudits,
                NoCandidates: _rung0NoCandidates,
                Exhausted: _rung0Exhausted,
                GuardRejected: _rung0GuardRejected,
                ZeroWorkAttempts: _rung0ZeroWorkAttempts,
                RelationNullExecutions: _relationNullExecutions,
                RelationNullDivergences: _relationNullDivergences,
                RelationNullAuthoritativeCompositions: _relationNullAuthoritativeCompositions);

        public EmlSpeculativeTransactionMetrics CaptureSpeculativeTransactionMetrics()
            => _speculativeTransactions;

        private void RecordSpeculation(EmlSieve.SpeculativeTransaction transaction, bool committed)
        {
            if (committed) _rung0UniqueAudits += transaction.PublishedRung0Audits;
            EmlSpeculativeTransactionMetrics delta = new(
                ProbeTrials: 1,
                Commits: committed ? 1 : 0,
                Rollbacks: committed ? 0 : 1,
                SerializeLoads: transaction.SerializeLoads,
                SerializeBytes: transaction.SerializeBytes,
                Restores: transaction.Restores,
                RestoreBytes: transaction.RestoreBytes,
                PreviewEvaluatorCalls: transaction.PreviewEvaluatorCalls,
                CommittedEvaluatorCalls: transaction.CommittedEvaluatorCalls,
                PreviewWallTicks: transaction.PreviewWallTicks,
                CommitWallTicks: transaction.CommitWallTicks,
                RollbackWallTicks: transaction.RollbackWallTicks);
            _speculativeTransactions = _speculativeTransactions.Add(in delta);
        }

        private void RecordRung0(in EmlRung0AdmissionResult rung0)
        {
            _rung0Attempts++;
            if (rung0.Composition.Composed)
            {
                _rung0Composed++;
                if (rung0.MainEvaluatorDelta == 0) _rung0EvaluatorZero++;
            }
            if (rung0.Audit is not null) _rung0Audits++;
            if (!rung0.Composition.Work.DidWork) _rung0ZeroWorkAttempts++;
            if (rung0.Composition.Status == EmlRung0Statuses.NoCandidate) _rung0NoCandidates++;
            else if (rung0.Composition.Status == EmlRung0Statuses.Exhausted) _rung0Exhausted++;
            else if (rung0.Composition.Status == EmlRung0Statuses.GuardRejected) _rung0GuardRejected++;
        }

        private void RecordRelationNull(in EmlRung0NullExecution execution)
        {
            _relationNullExecutions++;
            if (execution.Powered) _relationNullDivergences++;
            _relationNullAuthoritativeCompositions = checked(
                _relationNullAuthoritativeCompositions + execution.AuthoritativeCompositions);
        }

        public string DescribePrediction(EmlPredictionID claimID)
        {
            if ((uint)claimID.Value >= (uint)_sieve.MintLog.Count)
                return "missing-claim:" + claimID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string line = _sieve.MintLog[claimID.Value].Line;
            if (!EmlPrediction.TryParse(line, out EmlPrediction claim)) return line;
            string relation = claim.Tilde ? " ~ " : " = ";
            string right = claim.RhsRpn ? EmlRender.Render(claim.Rhs) : claim.Rhs;
            return line + "|math=" + EmlRender.Render(claim.Lhs) + relation + right;
        }

        public string DescribeCertificate(EmlCert certificate)
        {
            if (!_sieve.Cas.TryGetValue(certificate, out SemanticCASClass<string> certificateClass))
                return "missing-certificate:" + certificate.Hex();
            return certificateClass.Rep + "|math=" + EmlRender.Render(certificateClass.Rep);
        }

        public EmlIntensionalRematchStepResult ExecuteTrial(in EmlIntensionalRematchTrial trial)
        {
            return Kind switch
            {
                EmlIntensionalRematchArms.FreshEnumeration => ExecuteOffer(trial.Binding, trial.RemainingEvaluatorCalls),
                EmlIntensionalRematchArms.ObligationHoleSolve => ExecuteHoleSolve(in trial, useSingleBinding: false),
                EmlIntensionalRematchArms.GuardedBinding => ExecuteHoleSolve(in trial, useSingleBinding: true),
                EmlIntensionalRematchArms.BindingShuffledNull => ExecuteShuffledHoleSolve(in trial),
                EmlIntensionalRematchArms.LawCandidateShadow => ExecuteLaw(trial.LawCandidate, trial.RemainingEvaluatorCalls),
                EmlIntensionalRematchArms.NoLaw => EmlIntensionalRematchStepResult.RecordAbstention([]),
                EmlIntensionalRematchArms.LawShuffledNull => ExecuteLaw(trial.ShuffledLawCandidate, trial.RemainingEvaluatorCalls),
                _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "unknown rematch arm"),
            };
        }

        internal (bool Verified, long EvaluatorCalls) ExecuteLawForProbe(
            in EmlLawCandidateInstantiation candidate)
        {
            LawOperation operation = VerifyAndAdmitLaw(_sieve, _lawStore, candidate);
            return (operation.Verified, operation.EvaluatorCalls);
        }

        public EmlIntensionalRematchStepResult AdvanceExactly(
            long evaluatorCalls,
            EmlIntensionalRematchPhases phase)
        {
            long endpoint = checked(EvaluatorClock.ProgramPointEvaluations + evaluatorCalls);
            List<EmlCertificateDelta> deltas = new();
            while (EvaluatorClock.ProgramPointEvaluations < endpoint)
            {
                long remaining = endpoint - EvaluatorClock.ProgramPointEvaluations;
                string program = phase == EmlIntensionalRematchPhases.DescendantDelay
                    && Kind == EmlIntensionalRematchArms.LawCandidateShadow
                    && TryGenerateLawDescendant(out string descendant)
                        ? descendant
                        : _continuation[_continuationCursor++ % _continuation.Length];
                if (TryOfferAndCommit(program, remaining, out IReadOnlyList<EmlCertificateDelta> admitted))
                {
                    for (int i = 0; i < admitted.Count; i++) deltas.Add(admitted[i]);
                    continue;
                }
                EvaluateOne(program);
            }
            return EmlIntensionalRematchStepResult.RecordAbstention(deltas.ToArray());
        }

        private void AppendLawFrontier(string program)
        {
            if (_lawFrontierSeen.Add(program)) _lawFrontier.Add(program);
        }

        private bool TryGenerateLawDescendant(out string program)
        {
            while (_lawFrontierCursor < _lawFrontier.Count)
            {
                string antecedentRpn = _lawFrontier[_lawFrontierCursor++];
                if (!EmlTree.TryParseRPN(antecedentRpn, out EmlTree? antecedent)) continue;
                for (int lawIndex = 0; lawIndex < _lawRules.Count; lawIndex++)
                {
                    LawRule rule = _lawRules[lawIndex];
                    List<EmlLawRewrite> rewrites = new();
                    rule.Law.AppendRewrites(antecedent!, rule.Certificate, rule.Proof, rewrites);
                    for (int rewriteIndex = 0; rewriteIndex < rewrites.Count; rewriteIndex++)
                    {
                        string consequent = rewrites[rewriteIndex].ConsequentRpn;
                        if (!_lawFrontierSeen.Add(consequent)) continue;
                        _lawFrontier.Add(consequent);
                        program = consequent;
                        return true;
                    }
                }
            }
            program = "";
            return false;
        }

        private EmlIntensionalRematchStepResult ExecuteOffer(
            EmlIntensionalRematchBindingID binding,
            long remainingEvaluatorCalls)
        {
            EmlHoleCandidate candidate = GetBinding(binding);
            if (!TryOfferAndCommit(candidate.Program, remainingEvaluatorCalls, out IReadOnlyList<EmlCertificateDelta> deltas))
                return EmlIntensionalRematchStepResult.RecordAbstention([]);
            return EmlIntensionalRematchStepResult.RecordCandidate(deltas);
        }

        private EmlIntensionalRematchStepResult ExecuteShuffledHoleSolve(in EmlIntensionalRematchTrial trial)
        {
            EmlIntensionalRematchTrial shuffled = trial with { Binding = trial.ShuffledBinding };
            return ExecuteHoleSolve(in shuffled, useSingleBinding: true);
        }

        private EmlIntensionalRematchStepResult ExecuteHoleSolve(
            in EmlIntensionalRematchTrial trial,
            bool useSingleBinding)
        {
            List<EmlHoleCandidate> candidates = useSingleBinding
                ? new List<EmlHoleCandidate> { GetBinding(trial.Binding) }
                : new List<EmlHoleCandidate>(_bindings);
            EmlObligationResolution obligation = trial.Obligation;
            using EmlSieve.SpeculativeTransaction transaction = _sieve.BeginSpeculativeTransaction();
            long transactionStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
            HoleOperation preview = SolveAndAdmit(_sieve, _lawStore, trial.LawCandidate, in obligation, candidates);
            long measuredCalls = _sieve.EvaluatorClock.ProgramPointEvaluations - transactionStart;
            if (preview.EvaluatorCalls != measuredCalls)
                throw new InvalidDataException($"EML hole probe accounting drift: reported={preview.EvaluatorCalls} measured={measuredCalls}");
            if (preview.EvaluatorCalls > trial.RemainingEvaluatorCalls)
            {
                transaction.RecordPreview(preview.EvaluatorCalls);
                transaction.Rollback();
                RecordSpeculation(transaction, committed: false);
                return EmlIntensionalRematchStepResult.RecordAbstention([]);
            }
            transaction.RecordPreview(preview.EvaluatorCalls);
            transaction.RecordCommitted(preview.EvaluatorCalls);
            transaction.Commit();
            RecordSpeculation(transaction, committed: true);
            if (!preview.HasCandidate)
                return EmlIntensionalRematchStepResult.RecordNoCandidate([]);
            return EmlIntensionalRematchStepResult.RecordCandidate(preview.Deltas);
        }

        private HoleOperation SolveAndAdmit(
            EmlSieve sieve,
            EmlLawStore lawStore,
            EmlLawCandidateInstantiation? lawCandidate,
            in EmlObligationResolution obligation,
            List<EmlHoleCandidate> candidates)
        {
            long start = sieve.EvaluatorClock.ProgramPointEvaluations;
            sieve.DrainNewMints();
            sieve.DrainSemanticDeltas();
            List<EmlHoleRepairProposal> repairs = new();
            EmlDeliberationLease deliberation = sieve.ReserveDeliberation(
                in obligation, EmlDeliberationQuota.Default, _deliberationEpoch);
            if (deliberation.IsReused)
            {
                EmlDeliberationSettlement reused = deliberation.Complete(EmlDeliberationOutcomes.Reused, "reservation already settled");
                return new HoleOperation(false, 0, [], EmlDeliberationOutcomes.Reused, reused);
            }
            EmlHoleSolveResult solve;
            try
            {
                deliberation.BeginPhase("rung0-derivation");
                if (lawCandidate is EmlLawCandidateInstantiation candidate)
                {
                    EmlRung0AdmissionResult rung0 = EmlRung0Admission.TryAdmit(sieve, lawStore, in candidate, deliberation);
                    RecordRung0(in rung0);
                    if (rung0.Admitted)
                    {
                        if (rung0.MainEvaluatorDelta != 0)
                            throw new InvalidOperationException("admitted rung-0 rematch derivation touched the main evaluator");
                        EmlDeliberationSettlement rung0Settlement = deliberation.Complete(EmlDeliberationOutcomes.Solved, "rung0-derived");
                        sieve.Grader.BindDeliberation(null);
                        return new HoleOperation(true, 0, CaptureDeltas(sieve, start), EmlDeliberationOutcomes.Solved, rung0Settlement);
                    }
                    if (rung0.NumericFallbackProhibited)
                        candidates.RemoveAll(hole => string.Equals(hole.Program, candidate.Rewrite.ConsequentRpn, StringComparison.Ordinal));
                }
                solve = EmlHoleSolver.Solve(
                    sieve.MintLog,
                    in obligation,
                    candidates,
                    repairs,
                    sieve.EvaluatorClock,
                    HoleBranchRadius,
                    grader: sieve.Grader,
                    deliberationLease: deliberation);
            }
            catch (EmlDeliberationExhaustedException exhausted)
            {
                EmlDeliberationSettlement settlement = deliberation.Complete(EmlDeliberationOutcomes.Exhausted, exhausted.Message);
                sieve.Grader.BindDeliberation(null);
                return new HoleOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, [], EmlDeliberationOutcomes.Exhausted, settlement);
            }
            EmlDeliberationOutcomes outcome = solve.Outcome;
            if (repairs.Count == 0)
            {
                EmlDeliberationSettlement settlement = deliberation.Complete(outcome, "no candidate");
                sieve.Grader.BindDeliberation(null);
                return new HoleOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, [], outcome, settlement);
            }
            EmlHoleRepairProposal repair = repairs[0];
            if (!repair.OccurrenceCheck.Accepted)
                throw new InvalidDataException("hole solver returned a repair without an exact forward-verification verdict");
            string program = repair.Program;
            if (!sieve.TryAdmitResidualProof(obligation.SourcePredictionID, program, start, out EmlCertificateDelta ignored, deliberation))
            {
                EmlDeliberationSettlement settlement = deliberation.Complete(EmlDeliberationOutcomes.Rejected, "repair admission rejected");
                sieve.Grader.BindDeliberation(null);
                return new HoleOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, [], EmlDeliberationOutcomes.Rejected, settlement);
            }
            sieve.Offer(program);
            IReadOnlyList<EmlCertificateDelta> deltas = CaptureDeltas(sieve, start);
            EmlDeliberationSettlement solved = deliberation.Complete(EmlDeliberationOutcomes.Solved, "accepted");
            sieve.Grader.BindDeliberation(null);
            return new HoleOperation(true, sieve.EvaluatorClock.ProgramPointEvaluations - start, deltas, EmlDeliberationOutcomes.Solved, solved);
        }

        private EmlIntensionalRematchStepResult ExecuteLaw(
            EmlLawCandidateInstantiation? candidate,
            long remainingEvaluatorCalls)
        {
            if (candidate is null) return EmlIntensionalRematchStepResult.RecordNoCandidate([]);
            using EmlSieve.SpeculativeTransaction transaction = _sieve.BeginSpeculativeTransaction();
            long transactionStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
            LawOperation preview = VerifyAndAdmitLaw(_sieve, _lawStore, candidate.Value);
            long measuredCalls = _sieve.EvaluatorClock.ProgramPointEvaluations - transactionStart;
            if (preview.EvaluatorCalls != measuredCalls)
                throw new InvalidDataException($"EML law probe accounting drift: reported={preview.EvaluatorCalls} measured={measuredCalls}");
            if (preview.EvaluatorCalls > remainingEvaluatorCalls)
            {
                transaction.RecordPreview(preview.EvaluatorCalls);
                transaction.Rollback();
                RecordSpeculation(transaction, committed: false);
                return EmlIntensionalRematchStepResult.RecordAbstention([]);
            }
            transaction.RecordPreview(preview.EvaluatorCalls);
            transaction.RecordCommitted(preview.EvaluatorCalls);
            transaction.Commit();
            RecordSpeculation(transaction, committed: true);
            if (!preview.Verified)
                return EmlIntensionalRematchStepResult.RecordNoCandidate([]);
            if (Kind == EmlIntensionalRematchArms.LawCandidateShadow && !candidate.Value.Rewrite.IsRelationNull)
                AppendLawFrontier(candidate.Value.Rewrite.ConsequentRpn);
            return EmlIntensionalRematchStepResult.RecordCandidate(preview.Deltas);
        }

        private LawOperation VerifyAndAdmitLaw(
            EmlSieve sieve,
            EmlLawStore lawStore,
            EmlLawCandidateInstantiation candidate)
        {
            long start = sieve.EvaluatorClock.ProgramPointEvaluations;
            sieve.DrainNewMints();
            sieve.DrainSemanticDeltas();
            EmlObligationResolution candidateObligation = candidate.Obligation;
            EmlDeliberationLease deliberation = sieve.ReserveDeliberation(
                in candidateObligation,
                EmlDeliberationQuota.Default,
                _deliberationEpoch);
            bool reused = deliberation.IsReused;
            if (reused)
            {
                deliberation.Complete(EmlDeliberationOutcomes.Reused, "reservation already settled");
                // A reused reservation has no authority to reopen a prohibited
                // law.  Relation-null and quarantined candidates must never
                // fall through to numeric grading; ordinary rung-one rows may
                // still use their explicit numeric verifier path.
                if (candidate.Rewrite.IsRelationNull
                    || lawStore.IsRung0RuleQuarantined(candidate.Rewrite.RuleID))
                    return new LawOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, []);
            }
            if (!reused)
            {
                deliberation.BeginPhase("rung0-derivation");
                if (candidate.Rewrite.IsRelationNull)
                {
                    EmlRung0NullExecution nullExecution = default;
                    if (candidate.PredictionCarrier is EmlRewritePredictionCarrier nullCarrier)
                    {
                        EmlRung0Budget nullBudget = EmlRung0Budget.Default;
                        EmlLawRewrite relationNull = candidate.Rewrite;
                        nullExecution = lawStore.DeriveRung0Null(
                            in nullCarrier,
                            candidate.Instantiation.LeftRpn,
                            in relationNull,
                            in nullBudget,
                            deliberation);
                    }
                    RecordRelationNull(in nullExecution);
                    deliberation.Complete(
                        nullExecution.Powered ? EmlDeliberationOutcomes.NoCandidate : EmlDeliberationOutcomes.Rejected,
                        nullExecution.Powered ? "powered-relation-null" : "unpowered-relation-null");
                    return new LawOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, []);
                }
                EmlRung0AdmissionResult rung0 = EmlRung0Admission.TryAdmit(
                    sieve,
                    lawStore,
                    in candidate,
                    deliberation);
                RecordRung0(in rung0);
                EmlDeliberationOutcomes outcome = rung0.Admitted
                    ? EmlDeliberationOutcomes.Solved
                    : rung0.Composition.Status switch
                    {
                        EmlRung0Statuses.Exhausted => EmlDeliberationOutcomes.Exhausted,
                        EmlRung0Statuses.GuardRejected => EmlDeliberationOutcomes.Rejected,
                        _ => EmlDeliberationOutcomes.NoCandidate,
                    };
                deliberation.Complete(outcome, "rung0-" + rung0.Composition.Status.ToString().ToLowerInvariant());
                if (rung0.Admitted)
                    return new LawOperation(
                        true,
                        sieve.EvaluatorClock.ProgramPointEvaluations - start,
                        CaptureDeltas(sieve, start));
                if (rung0.NumericFallbackProhibited)
                    return new LawOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, []);
            }

            EmlGrader grader = new(sieve.EvaluatorClock);
            EmlVerdict verdict = grader.GradeRpn(
                candidate.Instantiation.LeftRpn,
                candidate.Instantiation.RightRpn);
            if (verdict.Grade != 'E')
                return new LawOperation(false, sieve.EvaluatorClock.ProgramPointEvaluations - start, []);
            sieve.Offer(candidate.Rewrite.ConsequentRpn);
            IReadOnlyList<EmlCertificateDelta> deltas = CaptureDeltas(sieve, start);
            return new LawOperation(true, sieve.EvaluatorClock.ProgramPointEvaluations - start, deltas);
        }

        private bool TryOfferAndCommit(
            string program,
            long remainingEvaluatorCalls,
            out IReadOnlyList<EmlCertificateDelta> deltas)
        {
            using EmlSieve.SpeculativeTransaction transaction = _sieve.BeginSpeculativeTransaction();
            _sieve.DrainNewMints();
            _sieve.DrainSemanticDeltas();
            long start = _sieve.EvaluatorClock.ProgramPointEvaluations;
            _sieve.Offer(program);
            IReadOnlyList<EmlCertificateDelta> previewDeltas = CaptureDeltas(_sieve, start);
            long calls = _sieve.EvaluatorClock.ProgramPointEvaluations - start;
            if (calls <= 0 || calls > remainingEvaluatorCalls)
            {
                deltas = Array.Empty<EmlCertificateDelta>();
                transaction.RecordPreview(calls);
                transaction.Rollback();
                RecordSpeculation(transaction, committed: false);
                return false;
            }
            transaction.RecordPreview(calls);
            transaction.RecordCommitted(calls);
            transaction.Commit();
            RecordSpeculation(transaction, committed: true);
            deltas = previewDeltas;
            return true;
        }

        private void EvaluateOne(string program)
        {
            EvaluatorClock.RecordOutOfDistributionProbeCall();
            Eml.Eval(program, EvaluationX, EvaluationY);
        }

        private EmlHoleCandidate GetBinding(EmlIntensionalRematchBindingID binding)
        {
            if ((uint)binding.Value >= (uint)_bindings.Count)
                throw new InvalidDataException($"rematch binding {binding.Value} is outside the deterministic candidate supply");
            return _bindings[binding.Value];
        }

        private static string[] BuildContinuation(List<EmlHoleCandidate> bindings, int scheduledTrials)
        {
            int count = Math.Max(1, Math.Min(bindings.Count, scheduledTrials));
            string[] programs = new string[count];
            for (int i = 0; i < count; i++) programs[i] = bindings[i % bindings.Count].Program;
            return programs;
        }

        private static IReadOnlyList<EmlCertificateDelta> CaptureDeltas(EmlSieve sieve, long start)
        {
            EmlEvaluatorInterval evaluation = new(start, sieve.EvaluatorClock.ProgramPointEvaluations);
            List<EmlCertificateDelta> deltas = new();
            for (int i = 0; i < sieve.NewSemanticDeltas.Count; i++)
                deltas.Add(sieve.NewSemanticDeltas[i]);
            for (int i = 0; i < sieve.NewMints.Count; i++)
            {
                EmlCert certificate = sieve.NewMintCert(i);
                EmlPredictionID claimID = sieve.NewMintPredictionID(i);
                if (sieve.NewMintFirst(i))
                    deltas.Add(new EmlCertificateDelta(
                        EmlCertificateChanges.ClassOpened,
                        claimID,
                        null,
                        certificate,
                        evaluation,
                        0));
                else if (sieve.NewMintRepresentativeChanged(i))
                    deltas.Add(new EmlCertificateDelta(
                        EmlCertificateChanges.RepresentativeImproved,
                        claimID,
                        certificate,
                        certificate,
                        evaluation,
                        0));
            }
            sieve.DrainNewMints();
            sieve.DrainSemanticDeltas();
            return deltas.ToArray();
        }

        private readonly record struct HoleOperation(
            bool HasCandidate,
            long EvaluatorCalls,
            IReadOnlyList<EmlCertificateDelta> Deltas,
            EmlDeliberationOutcomes Outcome = EmlDeliberationOutcomes.NoCandidate,
            EmlDeliberationSettlement? Completion = null);

        private readonly record struct LawOperation(
            bool Verified,
            long EvaluatorCalls,
            IReadOnlyList<EmlCertificateDelta> Deltas);

        private readonly record struct LawRule(
            EmlOneHoleLaw Law,
            EmlLawBehaviorCertificate Certificate,
            EmlLawProof Proof);
    }

}
