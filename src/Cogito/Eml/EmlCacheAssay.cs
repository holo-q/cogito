namespace Cogito;

internal readonly record struct EmlCacheAssayReport(
    bool SemanticResultsExact,
    bool ProofResultsExact,
    bool WarmCacheReducedExecutedProbes,
    bool ChangedProfileMissed,
    bool ProcessStayedOutsideFiniteCache,
    bool ResumeInvisible,
    long ColdExecutedProbePoints,
    long WarmExecutedProbePoints,
    long WarmCacheHits,
    long ChangedProfileMisses,
    long ColdLogicalRequests,
    long WarmFirstLogicalRequests,
    long WarmSecondLogicalRequests,
    long ColdCacheMisses,
    long WarmFirstCacheMisses,
    long WarmSecondCacheMisses,
    double ColdWallMilliseconds,
    double WarmFirstWallMilliseconds,
    double WarmSecondWallMilliseconds,
    bool ProofExercised,
    bool TelemetryConserved,
    bool ComposedPathExercised)
{
    public bool Passed
        => SemanticResultsExact
            && WarmCacheReducedExecutedProbes
            && ChangedProfileMissed
            && ProcessStayedOutsideFiniteCache
            && ResumeInvisible
            && TelemetryConserved
            && ComposedPathExercised;
}

/// Deterministic end-to-end proof for the finite residual cache. It runs the same candidate tree cold and warm,
/// compares proposal semantics, attempts proof admission when the fixture offers an accepted finite proposal,
/// exercises a changed probe profile, and confirms process expressions never enter the finite ladder cache. The
/// cache is derived state, so admission images must remain byte-identical.
internal static class EmlCacheAssay
{
    public static EmlCacheAssayReport Run(int signatureDigits = 9)
    {
        EmlRematchFixture fixture = EmlRematchFixture.Create(signatureDigits);
        if (fixture.Obligations.Count == 0 || fixture.Bindings.Count == 0)
            throw new InvalidDataException("EML cache assay fixture produced no obligation or candidates");

        List<EmlHoleCandidate> candidates = fixture.Bindings.ToList();
        EmlObligationResolution obligation = default;
        bool foundFiniteProofFixture = false;
        for (int obligationIndex = 0; obligationIndex < fixture.Obligations.Count; obligationIndex++)
        {
            EmlObligationResolution probeObligation = fixture.Obligations[obligationIndex];
            EmlResidualWitness probeWitness = probeObligation.Corroboration;
            EmlSieve probeSieve = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
            List<EmlHoleRepairProposal> probeProposals = new();
            _ = EmlHoleSolver.SolveAgainstWitness(
                probeSieve.MintLog,
                in probeObligation,
                in probeWitness,
                candidates,
                probeProposals,
                probeSieve.EvaluatorClock,
                branchRadius: 0,
                grader: probeSieve.Grader);
            if (probeProposals.Any(static proposal => proposal.Expression.TryRenderRPN(out _)))
            {
                obligation = probeObligation;
                foundFiniteProofFixture = true;
                break;
            }
        }
        if (!foundFiniteProofFixture)
            obligation = fixture.Obligations[0];
        EmlResidualWitness targetWitness = obligation.Corroboration;

        EmlSieve cold = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        List<EmlHoleRepairProposal> coldProposals = new();
        EmlHoleSolveResult coldResult = EmlHoleSolver.SolveAgainstWitness(
            cold.MintLog,
            in obligation,
            in targetWitness,
            candidates,
            coldProposals,
            cold.EvaluatorClock,
            branchRadius: 0,
            grader: cold.Grader);
        long coldExecuted = coldResult.Telemetry.ExecutedProbePoints;

        EmlSieve coldRepeat = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        List<EmlHoleRepairProposal> coldRepeatProposals = new();
        EmlHoleSolveResult coldRepeatResult = EmlHoleSolver.SolveAgainstWitness(
            coldRepeat.MintLog,
            in obligation,
            in targetWitness,
            candidates,
            coldRepeatProposals,
            coldRepeat.EvaluatorClock,
            branchRadius: 0,
            grader: coldRepeat.Grader);

        EmlSieve warm = EmlRematchFixture.CloneSieve(signatureDigits, fixture.AdmissionImage);
        List<EmlHoleRepairProposal> warmFirstProposals = new();
        EmlHoleSolveResult warmFirst = EmlHoleSolver.SolveAgainstWitness(
            warm.MintLog,
            in obligation,
            in targetWitness,
            candidates,
            warmFirstProposals,
            warm.EvaluatorClock,
            branchRadius: 0,
            grader: warm.Grader);
        List<EmlHoleRepairProposal> warmSecondProposals = new();
        EmlHoleSolveResult warmSecond = EmlHoleSolver.SolveAgainstWitness(
            warm.MintLog,
            in obligation,
            in targetWitness,
            candidates,
            warmSecondProposals,
            warm.EvaluatorClock,
            branchRadius: 0,
            grader: warm.Grader);

        bool semanticExact = SameSolveSemantics(coldResult, coldProposals, coldRepeatResult, coldRepeatProposals)
            && SameSolveSemantics(coldResult, coldProposals, warmFirst, warmFirstProposals)
            && SameSolveSemantics(warmFirst, warmFirstProposals, warmSecond, warmSecondProposals);
        bool proofExercised = coldProposals.Any(static proposal => proposal.Expression.TryRenderRPN(out _));
        bool proofExact = proofExercised && CompareProofAdmission(cold, warm, obligation, coldProposals, warmSecondProposals);
        bool telemetryConserved = Conserves(coldResult.Telemetry)
            && Conserves(coldRepeatResult.Telemetry)
            && Conserves(warmFirst.Telemetry)
            && Conserves(warmSecond.Telemetry);
        EmlEvaluatorClock changedClock = new();
        EmlGrader changedGrader = new(0.123456789, 0.987654321, changedClock);
        EmlResidualExpression changedExpression = candidates[0].Expression;
        _ = changedExpression.Evaluate(changedClock, changedGrader);
        bool changedProfileMissed = changedClock.LadderCacheMisses > 0 && changedClock.LadderCacheHits == 0;

        EmlEvaluatorClock processClock = new();
        EmlProcessFunction process = EmlProcessFunctions.CreateNegativeLog(EmlProcessInputSlots.X, 4);
        EmlResidualExpression processExpression = EmlResidualExpression.CreateProcessFunction(in process);
        EmlResidualExpressionEvaluation processEvaluation = processExpression.Evaluate(processClock, new EmlGrader(processClock));
        bool processOutside = processEvaluation.ProcessFuelConsumed > 0 && processClock.LadderRequests == 0;

        EmlEvaluatorClock composedClock = new();
        EmlGrader composedGrader = new(composedClock);
        EmlResidualExpression composedExpression = EmlResidualExpression.CreateEGate(candidates[0].Expression, candidates[1].Expression);
        EmlResidualExpressionEvaluation composedCold = composedExpression.Evaluate(composedClock, composedGrader);
        EmlResidualExpressionEvaluation composedWarm = composedExpression.Evaluate(composedClock, composedGrader);
        bool composedPathExercised = SameEvaluation(composedCold, composedWarm)
            && composedClock.LadderCacheHits > 0
            && composedClock.ExecutedLadderProgramPointEvaluations == composedClock.LadderCacheMisses * 3;

        byte[] warmImage = warm.CaptureAdmissionState();
        EmlSieve resumed = EmlRematchFixture.CloneSieve(signatureDigits, warmImage);
        bool resumeInvisible = warmImage.AsSpan().SequenceEqual(resumed.CaptureAdmissionState());
        EmlCacheAssayReport report = new(
            semanticExact,
            proofExact,
            warmFirst.Telemetry.ExecutedProbePoints + warmSecond.Telemetry.ExecutedProbePoints
                < coldResult.Telemetry.ExecutedProbePoints + coldRepeatResult.Telemetry.ExecutedProbePoints,
            changedProfileMissed,
            processOutside,
            resumeInvisible,
            coldExecuted,
            warmSecond.Telemetry.ExecutedProbePoints,
            warmSecond.Telemetry.LadderCacheHits,
            changedClock.LadderCacheMisses,
            coldResult.Telemetry.LadderRequests,
            warmFirst.Telemetry.LadderRequests,
            warmSecond.Telemetry.LadderRequests,
            coldResult.Telemetry.LadderCacheMisses,
            warmFirst.Telemetry.LadderCacheMisses,
            warmSecond.Telemetry.LadderCacheMisses,
            coldResult.Telemetry.WallMilliseconds,
            warmFirst.Telemetry.WallMilliseconds,
            warmSecond.Telemetry.WallMilliseconds,
            proofExercised,
            telemetryConserved,
            composedPathExercised);
        string proofStatus = report.ProofExercised ? (report.ProofResultsExact ? "exact" : "FAIL") : "N/A";
        Console.WriteLine($"  eml-cache-assay · semantic={(report.SemanticResultsExact ? "exact" : "FAIL")} · proofs={proofStatus} · executed cold={report.ColdExecutedProbePoints} cold-repeat={coldRepeatResult.Telemetry.ExecutedProbePoints} warm-first={warmFirst.Telemetry.ExecutedProbePoints} warm-second={report.WarmExecutedProbePoints} · requests cold={report.ColdLogicalRequests} warm={report.WarmFirstLogicalRequests}+{report.WarmSecondLogicalRequests} · misses cold={report.ColdCacheMisses} warm={report.WarmFirstCacheMisses}+{report.WarmSecondCacheMisses} · wall-ms {report.ColdWallMilliseconds:R}/{report.WarmFirstWallMilliseconds:R}/{report.WarmSecondWallMilliseconds:R} · warm-hits={report.WarmCacheHits} · telemetry={(report.TelemetryConserved ? "conserved" : "FAIL")} · composed={(report.ComposedPathExercised ? "exercised" : "FAIL")} · changed-profile-miss={report.ChangedProfileMissed} · process-outside={report.ProcessStayedOutsideFiniteCache} · resume={(report.ResumeInvisible ? "invisible" : "FAIL")} · verdict={(report.Passed ? "PASS" : "FAIL")}");
        return report;
    }

    private static bool CompareProofAdmission(
        EmlSieve cold,
        EmlSieve warm,
        EmlObligationResolution obligation,
        List<EmlHoleRepairProposal> coldProposals,
        List<EmlHoleRepairProposal> warmProposals)
    {
        for (int i = 0; i < coldProposals.Count; i++)
        {
            if (i >= warmProposals.Count) return false;
            EmlHoleRepairProposal coldProposal = coldProposals[i];
            if (!coldProposal.Expression.TryRenderRPN(out string coldRPN)) continue;
            if (!warmProposals[i].Expression.TryRenderRPN(out string warmRPN) || !string.Equals(coldRPN, warmRPN, StringComparison.Ordinal)) return false;
            bool coldAccepted = cold.TryAdmitResidualProof(obligation.SourcePredictionID, coldRPN, cold.EvaluatorClock.ProgramPointEvaluations, out EmlCertificateDelta coldDelta);
            bool warmAccepted = warm.TryAdmitResidualProof(obligation.SourcePredictionID, warmRPN, warm.EvaluatorClock.ProgramPointEvaluations, out EmlCertificateDelta warmDelta);
            if (coldAccepted != warmAccepted || (coldAccepted && !SameProofDelta(coldDelta, warmDelta))) return false;
            if (coldAccepted) return true;
        }
        return coldProposals.Count == 0 || warmProposals.Count == coldProposals.Count;
    }

    private static bool SameProofDelta(EmlCertificateDelta left, EmlCertificateDelta right)
        => left.Change == right.Change
            && left.PredictionID == right.PredictionID
            && left.Before == right.Before
            && left.After == right.After
            && left.DescriptionBits == right.DescriptionBits;

    private static bool SameEvaluation(EmlResidualExpressionEvaluation left, EmlResidualExpressionEvaluation right)
        => left.P1.Equals(right.P1)
            && left.P2.Equals(right.P2)
            && left.P3.Equals(right.P3)
            && left.ProcessFuelConsumed == right.ProcessFuelConsumed;

    private static bool Conserves(EmlHoleSolveTelemetry telemetry)
        => telemetry.LadderRequests == telemetry.LadderCacheHits + telemetry.LadderCacheMisses
            && telemetry.ExecutedProbePoints == telemetry.LadderCacheMisses * 3
            && telemetry.UniqueFiniteKeys >= 0
            && telemetry.StructuralNodes >= 0
            && telemetry.WallMilliseconds >= 0;

    private static bool SameSolveSemantics(
        EmlHoleSolveResult left,
        List<EmlHoleRepairProposal> leftProposals,
        EmlHoleSolveResult right,
        List<EmlHoleRepairProposal> rightProposals)
    {
        if (left.CandidatePrograms != right.CandidatePrograms
            || left.HoleCount != right.HoleCount
            || left.JoinAttempts != right.JoinAttempts
            || left.VerifiedRepairs != right.VerifiedRepairs
            || leftProposals.Count != rightProposals.Count) return false;
        for (int i = 0; i < leftProposals.Count; i++)
        {
            EmlHoleRepairProposal a = leftProposals[i], b = rightProposals[i];
            if (!string.Equals(a.Program, b.Program, StringComparison.Ordinal)
                || a.OccurrenceCheck != b.OccurrenceCheck
                || a.Witnesses != b.Witnesses
                || a.Cost != b.Cost
                || a.Provenance != b.Provenance
                || a.Composition != b.Composition) return false;
        }
        return true;
    }
}
