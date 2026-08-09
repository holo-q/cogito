namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Induct;

/// Rebuilds the present exact-claim corpus from banked mint journals plus their retro-grade journals, then
/// measures every anti-unification funnel boundary without loading or advancing the banked runtime.
internal static class EmlLawProbe
{
    private const ulong OccurrenceCheckSeed = 0xE311C0DEUL;

    private static readonly ArmArtifact[] Artifacts =
    [
        new ArmArtifact("on", "mints_on.txt", "regrade_on.tsv"),
        new ArmArtifact("off", "mints_off.txt", "regrade_off.tsv"),
    ];

    public static int Run(string runArg, int signatureDigits)
    {
        string? dir = Cogito.Run.Resolve(runArg);
        if (dir is null)
        {
            Console.Error.WriteLine($"law-probe: run dir not found: {runArg}");
            return 2;
        }

        try
        {
            List<ArmRebuild> rebuilds = new();
            for (int i = 0; i < Artifacts.Length; i++)
            {
                ArmArtifact artifact = Artifacts[i];
                string mintPath = Path.Combine(dir, artifact.MintFile);
                string journalPath = Path.Combine(dir, artifact.JournalFile);
                bool hasMints = File.Exists(mintPath);
                bool hasJournal = File.Exists(journalPath);
                if (!hasMints && !hasJournal) continue;
                if (!hasMints || !hasJournal)
                    throw new InvalidDataException($"law-probe: {artifact.Arm} artifact pair is incomplete ({artifact.MintFile}, {artifact.JournalFile})");
                rebuilds.Add(RebuildArm(artifact, mintPath, journalPath, signatureDigits));
            }
            if (rebuilds.Count == 0)
                throw new InvalidDataException("law-probe: no mints_{on,off}.txt + regrade_{on,off}.tsv artifact pair found");

            string report = BuildReport(rebuilds);
            File.WriteAllText(Path.Combine(dir, "law_probe.tsv"), report);
            for (int i = 0; i < rebuilds.Count; i++)
                File.WriteAllText(Path.Combine(dir, $"law_store_{rebuilds[i].Arm}.tsv"), rebuilds[i].StoreReport);
            Trace.Note($"law-probe · {Path.GetFileName(dir)} · {rebuilds.Count} arm(s) · {Path.Combine(dir, "law_probe.tsv")}");
            return 0;
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    private static ArmRebuild RebuildArm(ArmArtifact artifact, string mintPath, string journalPath, int signatureDigits)
    {
        Dictionary<string, char> grades = ReadJournal(journalPath, out int journalRows);
        HashSet<string> seenMints = new(StringComparer.Ordinal);
        List<string> mints = new();
        int rawMints = 0;
        foreach (string line in File.ReadLines(mintPath))
        {
            if (line.Length == 0) continue;
            rawMints++;
            if (seenMints.Add(line)) mints.Add(line);
        }

        EmlGrader grader = new();
        Dictionary<string, Func<System.Numerics.Complex, System.Numerics.Complex, System.Numerics.Complex>> references = EmlSieve.LabelChart();
        HashSet<EmlCert> cas = new();
        List<EmlLawPrediction> claims = new();
        StringBuilder exactCorpus = new();
        int joined = 0;
        int exactPredictions = 0;
        int casFirstCaptures = 0;
        for (int i = 0; i < mints.Count; i++)
        {
            string line = mints[i];
            if (!grades.TryGetValue(line, out char grade)) continue;
            joined++;
            if (grade != 'E') continue;
            exactPredictions++;
            if (!EmlPrediction.TryParse(line, out EmlPrediction claim))
                throw new InvalidDataException($"law-probe: E-grade journal row is not a claim: {line}");
            if (!grader.TryGrade(in claim, references, out EmlVerdict verdict))
                throw new InvalidDataException($"law-probe: E-grade journal row cannot be graded by the current ladder: {line}");
            if (verdict.Grade != grade)
                throw new InvalidDataException($"law-probe: stale regrade journal ({grade} banked, {verdict.Grade} current): {line}");

            exactCorpus.Append(claim.Lhs).Append(" = ").Append(claim.Rhs).Append('\n');
            EmlCert cert = EmlCert.Of(in verdict, signatureDigits);
            if (!cas.Add(cert)) continue;
            casFirstCaptures++;
            if (claim.RhsRpn) claims.Add(new EmlLawPrediction(cert, claim.Lhs, claim.Rhs));
        }

        if (joined != grades.Count)
            throw new InvalidDataException($"law-probe: {artifact.Arm} journal/mint join mismatch ({joined} joined, {grades.Count} journal rows)");

        byte[] corpus = Encoding.ASCII.GetBytes(exactCorpus.ToString());
        RePairResult grammar = Engine.Induce(corpus).Result;
        List<EmlLawLaneResult> lanes = EmlAntiUnify.Probe(claims, grammar, OccurrenceCheckSeed);
        List<EmlLawCandidate> candidates = EmlAntiUnify.DiscoverCandidates(claims, grammar, OccurrenceCheckSeed);
        (int behaviorVerified, int opened, int replayOpened, int loadedCount, int loadedReplayOpened, string storeReport) =
            CertifyLaws(candidates, signatureDigits, admit: artifact.Arm == "on");
        return new ArmRebuild(artifact.Arm, rawMints, mints.Count, journalRows, joined, exactPredictions,
                              casFirstCaptures, claims.Count, corpus.Length, grammar.Rules.Length, lanes,
                              candidates.Count, behaviorVerified, opened, replayOpened, loadedCount,
                              loadedReplayOpened, storeReport);
    }

    private static (int BehaviorVerified, int Opened, int ReplayOpened, int LoadedCount,
        int LoadedReplayOpened, string StoreReport) CertifyLaws(List<EmlLawCandidate> candidates,
            int signatureDigits, bool admit)
    {
        EmlLawStore store = new();
        List<EmlVerifiedLaw> verified = new();
        for (int i = 0; i < candidates.Count; i++)
            if (EmlVerifiedLaw.TryVerify(candidates[i].Law, candidates[i].Support, signatureDigits,
                    out EmlVerifiedLaw? law) && law is not null) verified.Add(law);

        int capture = 0;
        int opened = admit ? AdmitLaws(store, verified, ref capture) : 0;
        int replayOpened = admit ? AdmitLaws(store, verified, ref capture) : 0;
        using MemoryStream image = new();
        using (CkptWriter writer = new(image)) store.Save(writer);
        image.Position = 0;
        EmlLawStore loaded = new();
        using (CkptReader reader = new(image)) loaded.Load(reader);
        int loadedReplayOpened = admit ? AdmitLaws(loaded, verified, ref capture) : 0;
        return (verified.Count, opened, replayOpened, loaded.Count, loadedReplayOpened, loaded.Report());
    }

    private static int AdmitLaws(EmlLawStore store, List<EmlVerifiedLaw> verified, ref int capture)
    {
        int opened = 0;
        for (int i = 0; i < verified.Count; i++)
        {
            if (!store.TryAdmit(verified[i], capture++,
                    out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)) continue;
            if (admission.FirstCapture) opened++;
        }
        return opened;
    }

    private static Dictionary<string, char> ReadJournal(string path, out int rows)
    {
        Dictionary<string, char> grades = new(StringComparer.Ordinal);
        rows = 0;
        bool header = true;
        foreach (string row in File.ReadLines(path))
        {
            if (header)
            {
                header = false;
                if (!row.StartsWith("grade\t", StringComparison.Ordinal) || !row.EndsWith("\tline", StringComparison.Ordinal))
                    throw new InvalidDataException($"law-probe: unsupported regrade journal header in {path}");
                continue;
            }
            if (row.Length == 0) continue;
            rows++;
            int lineCut = row.LastIndexOf('\t');
            if (lineCut <= 1 || row[1] != '\t')
                throw new InvalidDataException($"law-probe: malformed regrade journal row in {path}: {row}");
            string line = row[(lineCut + 1)..];
            if (!grades.TryAdd(line, row[0]))
                throw new InvalidDataException($"law-probe: duplicate regrade journal claim in {path}: {line}");
        }
        if (header) throw new InvalidDataException($"law-probe: empty regrade journal: {path}");
        return grades;
    }

    private static string BuildReport(IReadOnlyList<ArmRebuild> rebuilds)
    {
        StringBuilder report = new("arm\tlane\tcandidate_cap\tneighbors\tmutual\trow\tstage\tcount\tordinal\ttemplate\tcertificate_classes\tfillers\tmdl_gain\tverification_filler\tverification_claim\n");
        for (int i = 0; i < rebuilds.Count; i++)
        {
            ArmRebuild rebuild = rebuilds[i];
            AppendStage(report, rebuild.Arm, "rebuild", "mint_lines_raw", rebuild.RawMints);
            AppendStage(report, rebuild.Arm, "rebuild", "mint_lines_unique", rebuild.UniqueMints);
            AppendStage(report, rebuild.Arm, "rebuild", "journal_rows", rebuild.JournalRows);
            AppendStage(report, rebuild.Arm, "rebuild", "journal_joined", rebuild.Joined);
            AppendStage(report, rebuild.Arm, "rebuild", "current_e_grade", rebuild.ExactPredictions);
            AppendStage(report, rebuild.Arm, "rebuild", "e_cas_classes", rebuild.CasFirstCaptures);
            AppendStage(report, rebuild.Arm, "rebuild", "e_rpn_first_captures", rebuild.RpnFirstCaptures);
            AppendStage(report, rebuild.Arm, "rebuild", "grammar_bytes", rebuild.GrammarBytes);
            AppendStage(report, rebuild.Arm, "rebuild", "grammar_rules", rebuild.GrammarRules);
            AppendStage(report, rebuild.Arm, "law_store", "shipping_candidates", rebuild.ShippingCandidates);
            AppendStage(report, rebuild.Arm, "law_store", "behavior_verified", rebuild.BehaviorVerified);
            AppendStage(report, rebuild.Arm, "law_store", "feedback_eligible",
                rebuild.Arm == "on" ? rebuild.BehaviorVerified : 0);
            AppendStage(report, rebuild.Arm, "law_store", "classes_opened", rebuild.ClassesOpened);
            AppendStage(report, rebuild.Arm, "law_store", "replay_classes_opened", rebuild.ReplayClassesOpened);
            AppendStage(report, rebuild.Arm, "law_store", "loaded_classes", rebuild.LoadedClasses);
            AppendStage(report, rebuild.Arm, "law_store", "loaded_replay_classes_opened", rebuild.LoadedReplayClassesOpened);
            for (int laneIndex = 0; laneIndex < rebuild.Lanes.Count; laneIndex++)
                AppendLane(report, rebuild.Arm, rebuild.Lanes[laneIndex]);
        }
        return report.ToString();
    }

    private static void AppendStage(StringBuilder report, string arm, string lane, string stage, long count)
        => report.Append(arm).Append('\t').Append(lane).Append("\t\t\t\tstage\t").Append(stage).Append('\t')
                 .Append(count.ToString(CultureInfo.InvariantCulture)).Append("\t\t\t\t\t\t\t\n");

    private static void AppendLane(StringBuilder report, string arm, EmlLawLaneResult result)
    {
        EmlLawFunnel funnel = result.Funnel;
        AppendLaneStage(report, arm, result.Lane, "input_claims", funnel.InputPredictions);
        AppendLaneStage(report, arm, result.Lane, "candidate_claims", funnel.CandidatePredictions);
        AppendLaneStage(report, arm, result.Lane, "pair_space", funnel.PairSpace);
        AppendLaneStage(report, arm, result.Lane, "neighbor_edges", funnel.NeighborEdges);
        AppendLaneStage(report, arm, result.Lane, "pair_inputs", funnel.PairInputs);
        AppendLaneStage(report, arm, result.Lane, "templates", funnel.Templates);
        AppendLaneStage(report, arm, result.Lane, "template_groups", funnel.Groups);
        AppendLaneStage(report, arm, result.Lane, "supported_groups", funnel.SupportedGroups);
        AppendLaneStage(report, arm, result.Lane, "mdl_groups", funnel.MdlGroups);
        AppendLaneStage(report, arm, result.Lane, "verification_samples", funnel.OccurrenceCheckSamples);
        AppendLaneStage(report, arm, result.Lane, "fresh_verified", funnel.VerifiedGroups);
        AppendLaneStage(report, arm, result.Lane, "accepted_laws", result.Laws.Count);
        for (int i = 0; i < result.Laws.Count; i++) AppendLaw(report, arm, result.Lane, i, result.Laws[i]);
    }

    private static void AppendLaneStage(StringBuilder report, string arm, EmlLawLane lane, string stage, long count)
        => report.Append(arm).Append('\t').Append(lane.Name).Append('\t')
                 .Append(lane.CandidateCap == int.MaxValue ? "uncapped" : lane.CandidateCap.ToString(CultureInfo.InvariantCulture)).Append('\t')
                 .Append(lane.Neighbors.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(lane.MutualNeighbors ? '1' : '0')
                 .Append("\tstage\t").Append(stage).Append('\t').Append(count.ToString(CultureInfo.InvariantCulture))
                 .Append("\t\t\t\t\t\t\t\n");

    private static void AppendLaw(StringBuilder report, string arm, EmlLawLane lane, int index, EmlLaw law)
        => report.Append(arm).Append('\t').Append(lane.Name).Append('\t')
                 .Append(lane.CandidateCap == int.MaxValue ? "uncapped" : lane.CandidateCap.ToString(CultureInfo.InvariantCulture)).Append('\t')
                 .Append(lane.Neighbors.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(lane.MutualNeighbors ? '1' : '0')
                 .Append("\tlaw\taccepted_law\t1\t").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append('\t')
                 .Append(law.Template).Append('\t').Append(law.CertificateClasses.ToString(CultureInfo.InvariantCulture)).Append('\t')
                 .Append(law.Fillers.ToString(CultureInfo.InvariantCulture)).Append('\t')
                 .Append(law.MdlGain.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                 .Append(law.OccurrenceCheckFiller).Append('\t').Append(law.OccurrenceCheckPrediction).Append('\n');

    private readonly record struct ArmArtifact(string Arm, string MintFile, string JournalFile);

    private sealed record ArmRebuild(string Arm, int RawMints, int UniqueMints, int JournalRows, int Joined,
                                     int ExactPredictions, int CasFirstCaptures, int RpnFirstCaptures, int GrammarBytes,
                                     int GrammarRules, List<EmlLawLaneResult> Lanes, int ShippingCandidates,
                                     int BehaviorVerified, int ClassesOpened, int ReplayClassesOpened,
                                     int LoadedClasses, int LoadedReplayClassesOpened, string StoreReport);
}
