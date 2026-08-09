namespace Cogito;

using System.Text;

/// Semantic assay for the obligation search lease. It compares the bounded path with the legacy unrestricted oracle,
/// then proves pre-operation exhaustion, Save∘Load∘Save identity, duplicate reuse, and journal corruption rejection.
internal static class EmlFuelledDeliberationAssay
{
    public static int Run(int signatureDigits)
    {
        EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
        if (fixture.Obligations.Count == 0 || fixture.Bindings.Count == 0)
            throw new InvalidDataException("fuelled deliberation fixture has no obligation or candidate supply");
        EmlObligationResolution obligation = fixture.Obligations[0];
        List<EmlHoleCandidate> candidates = new(fixture.Bindings);
        EmlMint sourceMint = fixture.Sieve.MintLog[obligation.SourcePredictionID.Value];
        if (EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
            && EmlResidualDeriver.TryDeriveSharedExponentialArgument(
                obligation.SourcePredictionID, in sourcePrediction, 32, out EmlResidualComposition processComposition))
        {
            EmlProcessFunction process = processComposition.Process;
            candidates.Add(new EmlHoleCandidate(
                EmlResidualExpression.CreateProcessFunction(in process),
                "assay-process", checked((int)process.Fuel), processComposition));
        }

        EmlSieve oracle = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        List<EmlHoleRepairProposal> oracleRepairs = new();
        EmlHoleSolveResult unrestricted = EmlHoleSolver.Solve(
            oracle.MintLog, in obligation, candidates, oracleRepairs, oracle.EvaluatorClock, 2);

        EmlSieve bounded = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        EmlDeliberationQuota adequateQuota = EmlDeliberationQuota.Default;
        EmlDeliberationLease adequateLease = bounded.ReserveDeliberation(in obligation, adequateQuota, "assay", "eml-hole-solver-v1", "eml-hole-verifier-v1");
        List<EmlHoleRepairProposal> boundedRepairs = new();
        EmlHoleSolveResult boundedResult = EmlHoleSolver.Solve(
            bounded.MintLog,
            in obligation,
            candidates,
            boundedRepairs,
            bounded.EvaluatorClock,
            2,
            new EmlGrader(bounded.EvaluatorClock, adequateLease),
            adequateLease);
        EmlDeliberationSettlement adequateSettlement = adequateLease.Complete(boundedResult.Outcome, "adequate");
        bool adequateExact = boundedResult.Outcome == unrestricted.Outcome
            && boundedRepairs.Count == oracleRepairs.Count
            && ProgramsEqual(boundedRepairs, oracleRepairs);

        EmlSieve tight = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        EmlDeliberationLease tightLease = tight.ReserveDeliberation(in obligation, EmlDeliberationQuota.TightAssay, "assay-tight", "eml-hole-solver-v1", "eml-hole-verifier-v1");
        List<EmlHoleRepairProposal> tightRepairs = new();
        EmlHoleSolveResult tightResult = EmlHoleSolver.Solve(
            tight.MintLog,
            in obligation,
            candidates,
            tightRepairs,
            tight.EvaluatorClock,
            2,
            new EmlGrader(tight.EvaluatorClock, tightLease),
            tightLease);
        EmlDeliberationSettlement tightSettlement = tightLease.Complete(tightResult.Outcome, "tight");
        bool tightExact = tightResult.Outcome == EmlDeliberationOutcomes.Exhausted
            && tightRepairs.Count == 0
            && tightSettlement.Actual.CandidateEvaluations == 0;

        byte[] image = bounded.CaptureAdmissionState();
        EmlSieve reloaded = EmlRematchFixture.CloneSieve(signatureDigits, image);
        bool resumeExact = image.AsSpan().SequenceEqual(reloaded.CaptureAdmissionState());
        EmlDeliberationLease reusedLease = reloaded.ReserveDeliberation(in obligation, adequateQuota, "assay", "eml-hole-solver-v1", "eml-hole-verifier-v1");
        EmlDeliberationSettlement reused = reusedLease.Complete(EmlDeliberationOutcomes.Reused, "resume");
        bool reuseExact = reusedLease.IsReused && reused.Outcome == EmlDeliberationOutcomes.Reused && reused.Actual == EmlDeliberationCounts.Zero;

        byte[] corrupt = image.ToArray();
        string reservationID = bounded.DeliberationJournal.Admissions.Count == 0
            ? ""
            : bounded.DeliberationJournal.Admissions[0].ReservationID;
        int marker = FindBytes(corrupt, Encoding.UTF8.GetBytes(reservationID));
        bool corruptionRejected = marker >= 0 && AssertRejects(signatureDigits, corrupt, marker);
        int reservedMarker = FindLastBytes(image, BitConverter.GetBytes(100_000L));
        bool reservedCorruptionRejected = reservedMarker >= 0 && AssertRejects(signatureDigits, image, reservedMarker);
        int settlementReservationMarker = FindLastBytes(image, Encoding.UTF8.GetBytes(reservationID));
        int outcomeMarker = settlementReservationMarker < 0
            ? -1
            : checked(settlementReservationMarker + 1);
        bool invalidOutcomeRejected = outcomeMarker >= 0 && AssertRejectsValue(signatureDigits, image, outcomeMarker, 255);
        int wallMarker = FindLastBytes(image, BitConverter.GetBytes(adequateSettlement.WallTicks));
        bool negativeWallRejected = wallMarker >= 0 && AssertRejectsValue(signatureDigits, image, wallMarker, BitConverter.GetBytes(-1L));

        StringBuilder report = new("case\tmetric\tvalue\n");
        report.Append("oracle\tproposals\t").Append(oracleRepairs.Count).Append('\n');
        report.Append("adequate\tsemantic_exact\t").Append(adequateExact ? 1 : 0).Append('\n');
        report.Append("adequate\tplanned_actual_refund\t").Append(adequateSettlement.Held.Equals(EmlDeliberationCounts.Add(adequateSettlement.Actual, adequateSettlement.Refund)) ? 1 : 0).Append('\n');
        report.Append("tight\texhausted_before_candidate\t").Append(tightExact ? 1 : 0).Append('\n');
        report.Append("resume\tcheckpoint_exact\t").Append(resumeExact ? 1 : 0).Append('\n');
        report.Append("reuse\tzero_spend\t").Append(reuseExact ? 1 : 0).Append('\n');
        report.Append("corruption\toverdraw_rejected\t").Append(corruptionRejected ? 1 : 0).Append('\n');
        report.Append("corruption\treserved_mismatch_rejected\t").Append(reservedCorruptionRejected ? 1 : 0).Append('\n');
        report.Append("corruption\tinvalid_outcome_rejected\t").Append(invalidOutcomeRejected ? 1 : 0).Append('\n');
        report.Append("corruption\tnegative_wall_rejected\t").Append(negativeWallRejected ? 1 : 0).Append('\n');
        AppendSettlement(report, adequateSettlement);
        AppendSettlement(report, tightSettlement);
        string rendered = report.ToString();
        Run receipt = Cogito.Run.New("eml-fuelled-deliberation");
        receipt.Write("eml_fuelled_deliberation.tsv", rendered);
        Console.Write(rendered);
        bool passed = adequateExact && tightExact && resumeExact && reuseExact && corruptionRejected
            && reservedCorruptionRejected && invalidOutcomeRejected && negativeWallRejected;
        Console.WriteLine($"fuelled-deliberation-assay\t{(passed ? "PASS" : "FAIL")}");
        return passed ? 0 : 1;
    }

    private static bool ProgramsEqual(IReadOnlyList<EmlHoleRepairProposal> left, IReadOnlyList<EmlHoleRepairProposal> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (!string.Equals(left[i].Program, right[i].Program, StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool AssertRejects(int signatureDigits, byte[] image, int marker)
    {
        byte[] broken = image.ToArray();
        broken[marker] ^= 0x01;
        try
        {
            _ = EmlRematchFixture.CloneSieve(signatureDigits, broken);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static bool AssertRejectsValue(int signatureDigits, byte[] image, int marker, byte value)
        => AssertRejectsValue(signatureDigits, image, marker, new[] { value });

    private static bool AssertRejectsValue(int signatureDigits, byte[] image, int marker, byte[] replacement)
    {
        byte[] broken = image.ToArray();
        replacement.AsSpan().CopyTo(broken.AsSpan(marker, replacement.Length));
        try
        {
            _ = EmlRematchFixture.CloneSieve(signatureDigits, broken);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return -1;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            for (; j < needle.Length && haystack[i + j] == needle[j]; j++) { }
            if (j == needle.Length) return i;
        }
        return -1;
    }

    private static int FindLastBytes(byte[] haystack, byte[] needle)
    {
        for (int i = haystack.Length - needle.Length; i >= 0; i--)
        {
            int j = 0;
            for (; j < needle.Length && haystack[i + j] == needle[j]; j++) { }
            if (j == needle.Length) return i;
        }
        return -1;
    }


    private static void AppendSettlement(StringBuilder report, in EmlDeliberationSettlement settlement)
    {
        report.Append("journal\t").Append(settlement.ReservationID).Append("\toutcome\t").Append(settlement.Outcome).Append('\n');
        report.Append("journal\t").Append(settlement.ReservationID).Append("\twall_ticks\t").Append(settlement.WallTicks).Append('\n');
        AppendCounts(report, settlement.ReservationID, "planned", settlement.Planned);
        AppendCounts(report, settlement.ReservationID, "actual", settlement.Actual);
        AppendCounts(report, settlement.ReservationID, "refund", settlement.Refund);
    }

    private static void AppendCounts(StringBuilder report, string reservationID, string phase, EmlDeliberationCounts counts)
    {
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_candidate_evaluations\t").Append(counts.CandidateEvaluations).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_logical_program_points\t").Append(counts.LogicalProgramPoints).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_executed_program_points\t").Append(counts.ExecutedProgramPoints).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_inverse_transforms\t").Append(counts.InverseTransforms).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_hash_probes\t").Append(counts.HashProbes).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_join_attempts\t").Append(counts.JoinAttempts).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_join_hits\t").Append(counts.JoinHits).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_process_terms\t").Append(counts.ProcessTerms).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_verifier_program_points\t").Append(counts.VerifierProgramPoints).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_candidate_supply_items\t").Append(counts.CandidateSupplyItems).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_law_rewrite_applications\t").Append(counts.LawRewriteApplications).Append('\n');
        report.Append("journal\t").Append(reservationID).Append('\t').Append(phase).Append("_law_rewrite_tree_nodes\t").Append(counts.LawRewriteTreeNodes).Append('\n');
    }
}
