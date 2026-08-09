namespace Cogito;

using System.Diagnostics;
using System.Numerics;

public enum EmlHoleJoinOrientations
{
    Direct,
    SolveLeft,
    SolveRight,
}

public readonly record struct EmlHoleCandidate(
    EmlResidualExpression Expression,
    string Provenance,
    int Cost,
    EmlResidualComposition? Composition = null)
{
    public EmlHoleCandidate(string program, string provenance, int cost)
        : this(EmlResidualExpression.CreateFiniteRPN(program), provenance, cost) { }

    public string Program
        => Expression.TryRenderRPN(out string rpn) ? rpn : Expression.RenderCanonical();
}

public readonly record struct EmlHoleProbeValues(Complex P1, Complex P2, Complex P3);

public readonly record struct EmlHoleProbeWitness(Complex RequiredValue, Complex CandidateValue);

public readonly record struct EmlHoleWitnesses(
    EmlHoleProbeWitness P1,
    EmlHoleProbeWitness P2,
    EmlHoleProbeWitness P3);

public readonly record struct EmlHoleBranchTurns(int P1, int P2, int P3)
{
    public int Magnitude => Math.Abs(P1) + Math.Abs(P2) + Math.Abs(P3);
}

public readonly record struct EmlHoleRepairCost(
    int LeftCost,
    int RightCost,
    int ProgramLength,
    int BranchTurnMagnitude)
{
    public int Total => checked(LeftCost + RightCost + ProgramLength + BranchTurnMagnitude);
}

public readonly record struct EmlHoleRepairProvenance(
    EmlPredictionID SourcePredictionID,
    EmlHoleJoinOrientations Orientation,
    string LeftProgram,
    string RightProgram,
    string LeftProvenance,
    string RightProvenance,
    EmlHoleBranchTurns Turns);

public readonly record struct EmlHoleRepairWork(
    EmlEvaluatorInterval Evaluation,
    long ProcessFuel,
    long InverseTransforms,
    long JoinAttempts,
    long JoinHits)
{
    public long EvaluatorCalls => Evaluation.Calls;
}

public readonly record struct EmlHoleRepairProposal(
    EmlResidualExpression Expression,
    EmlHoleRepairOccurrenceCheck OccurrenceCheck,
    EmlHoleWitnesses Witnesses,
    EmlHoleRepairCost Cost,
    EmlHoleRepairWork Work,
    EmlHoleRepairProvenance Provenance,
    EmlResidualComposition? Composition)
{
    public string Program
        => Expression.TryRenderRPN(out string rpn) ? rpn : Expression.RenderCanonical();
}

public readonly record struct EmlHoleRepairOccurrenceCheck(
    bool Accepted,
    string Detail,
    EmlVerdict? FiniteVerdict);

public readonly record struct EmlHoleSourceContext(
    EmlObligationResolution Obligation,
    EmlMint SourceMint,
    EmlPrediction SourcePrediction,
    EmlVerdict SourceVerdict);

public readonly record struct EmlHoleSolveResult(
    EmlHoleSourceContext Source,
    int CandidatePrograms,
    int HoleCount,
    long JoinAttempts,
    int VerifiedRepairs,
    EmlHoleRepairWork Work,
    EmlHoleSolveTelemetry Telemetry,
    EmlDeliberationOutcomes Outcome = EmlDeliberationOutcomes.Solved,
    EmlDeliberationSettlement? Completion = null);

public readonly record struct EmlHoleSolveTelemetry(
    int CandidatePrograms,
    int FiniteExpressions,
    int ComposedExpressions,
    long LadderRequests,
    long LadderCacheHits,
    long LadderCacheMisses,
    long ExecutedProbePoints,
    long UniqueFiniteKeys,
    long StructuralNodes,
    double WallMilliseconds);

/// Synthesizes a program for an asymptotic claim's residual function. The source claim supplies the three-regime
/// target vector. Candidate programs carry their full computation; a meet-in-the-middle join inverts the EML gate,
/// then the witness ladder independently verifies the composed program against the residual at all three probes.
public static class EmlHoleSolver
{
    public const int MaxBranchRadius = 8;
    private const int JoinSignatureDigits = 9;
    private const double JoinRelativeTolerance = 1e-10;

    public static EmlHoleSolveResult Solve(
        IReadOnlyList<EmlMint> mintJournal,
        in EmlObligationResolution obligation,
        IReadOnlyList<EmlHoleCandidate> candidates,
        List<EmlHoleRepairProposal> output,
        EmlEvaluatorClock? clock = null,
        int branchRadius = 2,
        EmlDeliberationLease? deliberationLease = null)
    {
        EmlResidualWitness targetWitness = obligation.Corroboration;
        return SolveAgainstWitness(
            mintJournal,
            in obligation,
            in targetWitness,
            candidates,
            output,
            clock,
            branchRadius,
            grader: null,
            deliberationLease: deliberationLease);
    }

    internal static EmlHoleSolveResult Solve(
        IReadOnlyList<EmlMint> mintJournal,
        in EmlObligationResolution obligation,
        IReadOnlyList<EmlHoleCandidate> candidates,
        List<EmlHoleRepairProposal> output,
        EmlEvaluatorClock? clock,
        int branchRadius,
        EmlGrader grader,
        EmlDeliberationLease? deliberationLease = null)
    {
        EmlResidualWitness targetWitness = obligation.Corroboration;
        return SolveAgainstWitness(mintJournal, in obligation, in targetWitness, candidates, output, clock, branchRadius, grader, deliberationLease);
    }

    internal static EmlHoleSolveResult SolveAgainstWitness(
        IReadOnlyList<EmlMint> mintJournal,
        in EmlObligationResolution obligation,
        in EmlResidualWitness targetWitness,
        IReadOnlyList<EmlHoleCandidate> candidates,
        List<EmlHoleRepairProposal> output,
        EmlEvaluatorClock? clock = null,
        int branchRadius = 2,
        EmlGrader? grader = null,
        EmlDeliberationLease? deliberationLease = null)
    {
        ArgumentNullException.ThrowIfNull(mintJournal);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(output);
        if (branchRadius is < 0 or > MaxBranchRadius)
            throw new ArgumentOutOfRangeException(nameof(branchRadius), branchRadius, $"branch radius must be 0..{MaxBranchRadius}");
        int sourceIndex = obligation.SourcePredictionID.Value;
        if ((uint)sourceIndex >= (uint)mintJournal.Count)
            throw new ArgumentOutOfRangeException(nameof(obligation), sourceIndex, "obligation addresses no source claim");

        EmlMint sourceMint = mintJournal[sourceIndex];
        if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction))
            throw new InvalidDataException($"obligation source claim {sourceIndex} is not parseable: {sourceMint.Line}");
        EmlEvaluatorClock evaluatorClock = clock ?? new EmlEvaluatorClock();
        EmlEvaluatorClockSnapshot begin = evaluatorClock.Capture();
        long startTimestamp = Stopwatch.GetTimestamp();
        long start = evaluatorClock.ProgramPointEvaluations;
        grader?.BindDeliberation(deliberationLease);
        EmlGrader evaluationGrader = grader ?? new EmlGrader(evaluatorClock, deliberationLease);
        Dictionary<string, Func<Complex, Complex, Complex>> references = EmlSieve.LabelChart();
        EmlDeliberationOutcomes outcome = EmlDeliberationOutcomes.NoCandidate;
        EmlVerdict sourceVerdict = default;
        try
        {
        deliberationLease?.BeginPhase("source-validation");
        if (!evaluationGrader.TryGrade(in sourcePrediction, references, out sourceVerdict)
            || sourceVerdict.Grade != 'A')
            throw new InvalidDataException($"obligation {sourceIndex} is not currently asymptotic");
        if (!evaluationGrader.TryDescribeResidual(in sourcePrediction, references, out EmlResidualWitness currentWitness)
            || currentWitness != obligation.Corroboration
            || currentWitness != targetWitness)
            throw new InvalidDataException($"obligation {sourceIndex} disagrees with its current residual function");

        deliberationLease?.BeginPhase("candidate-preparation");
        List<CandidateEvaluation> prepared = PrepareCandidates(candidates, evaluatorClock, evaluationGrader, deliberationLease, out long processFuel);
        deliberationLease?.BeginPhase("candidate-joins");
        Dictionary<EmlTripleSignature, List<int>> index = BuildIndex(prepared, deliberationLease);
        List<EmlHoleRepairProposal> proposals = new();
        HashSet<string> seenPrograms = new(StringComparer.Ordinal);
        long attempts = 0;
        long hits = 0;
        long inverseStart = evaluatorClock.InverseTransforms;
        EmlHoleProbeValues target = ReadTarget(targetWitness);

        for (int i = 0; i < prepared.Count; i++)
        {
            CandidateEvaluation candidate = prepared[i];
            deliberationLease?.ReserveJoinAttempt();
            attempts++;
            if (candidate.Candidate.Expression.BearsProcess || Matches(candidate.Values, target))
                TryAppendProposal(candidate.Candidate.Expression, candidate, candidate, EmlHoleJoinOrientations.Direct,
                    default, in obligation, in sourcePrediction, in currentWitness, references, evaluationGrader, evaluatorClock,
                    proposals, seenPrograms, deliberationLease, ref processFuel, ref hits);
        }

        for (int leftIndex = 0; leftIndex < prepared.Count; leftIndex++)
        {
            CandidateEvaluation left = prepared[leftIndex];
            deliberationLease?.ReserveInverseTransform();
            evaluatorClock.RecordInverseTransform();
            if (!TrySolveRight(left.Values, target, out EmlHoleProbeValues requiredRight)) continue;
            AppendIndexedJoins(index, prepared, requiredRight, left, solveRight: true, default, in obligation,
                in sourcePrediction, in currentWitness, references, evaluationGrader, evaluatorClock, proposals, seenPrograms,
                deliberationLease, ref processFuel, ref attempts, ref hits);
        }

        for (int rightIndex = 0; rightIndex < prepared.Count; rightIndex++)
        {
            CandidateEvaluation right = prepared[rightIndex];
            for (int p1Turn = -branchRadius; p1Turn <= branchRadius; p1Turn++)
            for (int p2Turn = -branchRadius; p2Turn <= branchRadius; p2Turn++)
            for (int p3Turn = -branchRadius; p3Turn <= branchRadius; p3Turn++)
            {
                deliberationLease?.ReserveInverseTransform();
                evaluatorClock.RecordInverseTransform();
                EmlHoleBranchTurns turns = new(p1Turn, p2Turn, p3Turn);
                if (!TrySolveLeft(right.Values, target, in turns, out EmlHoleProbeValues requiredLeft)) continue;
                AppendIndexedJoins(index, prepared, requiredLeft, right, solveRight: false, turns, in obligation,
                    in sourcePrediction, in currentWitness, references, evaluationGrader, evaluatorClock, proposals, seenPrograms,
                    deliberationLease, ref processFuel, ref attempts, ref hits);
            }
        }

        proposals.Sort(EmlHoleRepairProposalComparer.Instance);
        output.AddRange(proposals);
        EmlHoleRepairWork work = new(
            evaluatorClock.MeasureFrom(start),
            processFuel,
            evaluatorClock.InverseTransforms - inverseStart,
            attempts,
            hits);
        EmlHoleSourceContext source = new(obligation, sourceMint, sourcePrediction, sourceVerdict);
        EmlEvaluatorClockSnapshot finish = evaluatorClock.Capture();
        HashSet<string> finiteKeys = new(StringComparer.Ordinal);
        HashSet<string> candidateIdentities = new(StringComparer.Ordinal);
        long structuralNodes = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            structuralNodes = checked(structuralNodes + candidates[i].Expression.StructuralCost);
            candidateIdentities.Add(candidates[i].Expression.RenderCanonical());
            if (candidates[i].Expression.TryRenderRPN(out string finiteRPN)) finiteKeys.Add(finiteRPN);
        }
        long composedExpressions = 0;
        foreach (string identity in seenPrograms)
            if (!candidateIdentities.Contains(identity)) composedExpressions++;
        EmlHoleSolveTelemetry telemetry = new(
            prepared.Count,
            checked((int)Math.Min(int.MaxValue, finiteKeys.Count)),
            checked((int)Math.Min(int.MaxValue, composedExpressions)),
            finish.LadderRequests - begin.LadderRequests,
            finish.LadderCacheHits - begin.LadderCacheHits,
            finish.LadderCacheMisses - begin.LadderCacheMisses,
            finish.ExecutedLadderProgramPointEvaluations - begin.ExecutedLadderProgramPointEvaluations,
            finiteKeys.Count,
            structuralNodes,
            Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        outcome = proposals.Count > 0 ? EmlDeliberationOutcomes.Solved : EmlDeliberationOutcomes.NoCandidate;
        return new EmlHoleSolveResult(source, prepared.Count, 1, attempts, proposals.Count, work, telemetry, outcome);
        }
        catch (EmlDeliberationExhaustedException)
        {
            outcome = EmlDeliberationOutcomes.Exhausted;
            EmlHoleSolveTelemetry telemetry = new(0, 0, 0, 0, 0, 0, 0, 0, 0, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            EmlHoleRepairWork work = new(evaluatorClock.MeasureFrom(start), 0, 0, 0, 0);
            EmlHoleSourceContext source = new(obligation, sourceMint, sourcePrediction, sourceVerdict);
            return new EmlHoleSolveResult(source, 0, 1, 0, 0, work, telemetry, outcome);
        }
    }

    private static void AppendIndexedJoins(
        Dictionary<EmlTripleSignature, List<int>> index,
        List<CandidateEvaluation> prepared,
        EmlHoleProbeValues required,
        CandidateEvaluation fixedCandidate,
        bool solveRight,
        EmlHoleBranchTurns turns,
        in EmlObligationResolution obligation,
        in EmlPrediction sourcePrediction,
        in EmlResidualWitness sourceWitness,
        Dictionary<string, Func<Complex, Complex, Complex>> references,
        EmlGrader grader,
        EmlEvaluatorClock clock,
        List<EmlHoleRepairProposal> proposals,
        HashSet<string> seenPrograms,
        EmlDeliberationLease? deliberationLease,
        ref long processFuel,
        ref long attempts,
        ref long hits)
    {
        deliberationLease?.ReserveHashProbe();
        clock.RecordHashProbe();
        EmlTripleSignature signature = CreateSignature(required);
        if (!index.TryGetValue(signature, out List<int>? matches)) return;
        for (int i = 0; i < matches.Count; i++)
        {
            CandidateEvaluation matched = prepared[matches[i]];
            deliberationLease?.ReserveJoinAttempt();
            attempts++;
            if (!Matches(matched.Values, required)) continue;
            CandidateEvaluation left = solveRight ? fixedCandidate : matched;
            CandidateEvaluation right = solveRight ? matched : fixedCandidate;
            EmlResidualExpression expression = EmlResidualExpression.CreateEGate(
                left.Candidate.Expression,
                right.Candidate.Expression);
            TryAppendProposal(expression, left, right,
                solveRight ? EmlHoleJoinOrientations.SolveRight : EmlHoleJoinOrientations.SolveLeft,
                turns, in obligation, in sourcePrediction, in sourceWitness, references, grader, clock,
                proposals, seenPrograms, deliberationLease, ref processFuel, ref hits);
        }
    }

    private static void TryAppendProposal(
        EmlResidualExpression expression,
        CandidateEvaluation left,
        CandidateEvaluation right,
        EmlHoleJoinOrientations orientation,
        EmlHoleBranchTurns turns,
        in EmlObligationResolution obligation,
        in EmlPrediction sourcePrediction,
        in EmlResidualWitness sourceWitness,
        Dictionary<string, Func<Complex, Complex, Complex>> references,
        EmlGrader grader,
        EmlEvaluatorClock clock,
        List<EmlHoleRepairProposal> proposals,
        HashSet<string> seenPrograms,
        EmlDeliberationLease? deliberationLease,
        ref long processFuel,
        ref long hits)
    {
        string identity = expression.RenderCanonical();
        if (!expression.BearsProcess
            && (!expression.TryRenderRPN(out string finiteRPN) || finiteRPN.Length > Eml.MaxProgramLen)) return;
        if (!seenPrograms.Add(identity)) return;
        EmlResidualExpressionEvaluation evaluation = expression.Evaluate(clock, grader, deliberationLease);
        processFuel = checked(processFuel + evaluation.ProcessFuelConsumed);
        EmlResidualComposition? derivation = orientation == EmlHoleJoinOrientations.Direct
            && left.Candidate.Composition is EmlResidualComposition structural
                ? structural
                : null;
        EmlHoleRepairOccurrenceCheck verification;
        if (expression.TryGetProcessFunction(out EmlProcessFunction processFunction))
        {
            deliberationLease?.ReserveVerifierProgramPoints(1);
            EmlProcessResidualOccurrenceCheck processOccurrenceCheck = EmlProcessResidualVerifier.Verify(
                obligation.SourcePredictionID,
                in sourcePrediction,
                in sourceWitness,
                in processFunction,
                derivation,
                references,
                clock,
                deliberationLease);
            verification = processOccurrenceCheck.ToHoleRepairOccurrenceCheck();
        }
        else
        {
            deliberationLease?.ReserveVerifierProgramPoints(1);
            verification = VerifyExpression(expression, in evaluation, in sourceWitness, grader);
        }
        if (!verification.Accepted) return;
        deliberationLease?.ReserveJoinHit();
        clock.RecordOfferedJoinHit();
        hits++;
        EmlHoleProbeValues values = ReadRoot(in evaluation);
        EmlHoleProbeValues target = ReadTarget(sourceWitness);
        EmlHoleWitnesses witnesses = new(
            new EmlHoleProbeWitness(target.P1, values.P1),
            new EmlHoleProbeWitness(target.P2, values.P2),
            new EmlHoleProbeWitness(target.P3, values.P3));
        EmlHoleRepairCost cost = new(
            left.Candidate.Cost,
            orientation == EmlHoleJoinOrientations.Direct ? 0 : right.Candidate.Cost,
            expression.StructuralCost,
            turns.Magnitude);
        EmlHoleRepairProvenance provenance = new(
            obligation.SourcePredictionID,
            orientation,
            left.Candidate.Program,
            orientation == EmlHoleJoinOrientations.Direct ? "" : right.Candidate.Program,
            left.Candidate.Provenance,
            orientation == EmlHoleJoinOrientations.Direct ? "" : right.Candidate.Provenance,
            turns);
        EmlHoleRepairWork work = new(
            EmlEvaluatorInterval.EmptyAt(clock.ProgramPointEvaluations),
            evaluation.ProcessFuelConsumed,
            0,
            1,
            1);
        proposals.Add(new EmlHoleRepairProposal(expression, verification, witnesses, cost, work, provenance, derivation));
    }

    private static List<CandidateEvaluation> PrepareCandidates(
        IReadOnlyList<EmlHoleCandidate> candidates,
        EmlEvaluatorClock clock,
        EmlGrader grader,
        EmlDeliberationLease? deliberationLease,
        out long processFuel)
    {
        List<EmlHoleCandidate> ordered = new(candidates.Count);
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < candidates.Count; i++)
        {
            EmlHoleCandidate candidate = candidates[i];
            if (candidate.Cost < 0) throw new ArgumentOutOfRangeException(nameof(candidates), candidate.Cost, "candidate cost cannot be negative");
            string identity = candidate.Expression.RenderCanonical();
            if (!seen.Add(identity)) continue;
            ordered.Add(candidate);
        }
        ordered.Sort(static (left, right) =>
        {
            int byCost = left.Cost.CompareTo(right.Cost);
            return byCost != 0
                ? byCost
                : string.CompareOrdinal(left.Expression.RenderCanonical(), right.Expression.RenderCanonical());
        });
        List<CandidateEvaluation> prepared = new(ordered.Count);
        processFuel = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            deliberationLease?.ReserveCandidateEvaluation();
            EmlResidualExpressionEvaluation evaluation = ordered[i].Expression.Evaluate(clock, grader, deliberationLease);
            processFuel = checked(processFuel + evaluation.ProcessFuelConsumed);
            prepared.Add(new CandidateEvaluation(ordered[i], ReadRoot(in evaluation)));
        }
        return prepared;
    }

    private static Dictionary<EmlTripleSignature, List<int>> BuildIndex(List<CandidateEvaluation> prepared, EmlDeliberationLease? deliberationLease)
    {
        Dictionary<EmlTripleSignature, List<int>> index = new();
        for (int i = 0; i < prepared.Count; i++)
        {
            deliberationLease?.ReserveHashProbe();
            EmlTripleSignature signature = CreateSignature(prepared[i].Values);
            if (!index.TryGetValue(signature, out List<int>? rows))
            {
                rows = new List<int>();
                index.Add(signature, rows);
            }
            rows.Add(i);
        }
        return index;
    }

    private static bool TrySolveRight(
        EmlHoleProbeValues left,
        EmlHoleProbeValues target,
        out EmlHoleProbeValues right)
    {
        bool p1 = TrySolveRight(left.P1, target.P1, out Complex rightP1);
        bool p2 = TrySolveRight(left.P2, target.P2, out Complex rightP2);
        bool p3 = TrySolveRight(left.P3, target.P3, out Complex rightP3);
        right = new EmlHoleProbeValues(rightP1, rightP2, rightP3);
        return p1 && p2 && p3;
    }

    private static bool TrySolveRight(Complex left, Complex target, out Complex right)
    {
        right = default;
        if (!IsFinite(left) || !IsFinite(target) || left.Real > Eml.ExpReMax) return false;
        Complex logarithm = Complex.Exp(left) - target;
        if (!IsFinite(logarithm) || logarithm.Real > Eml.ExpReMax) return false;
        right = Complex.Exp(logarithm);
        return IsFinite(right) && right != Complex.Zero && NearlyEqual(Complex.Log(right), logarithm);
    }

    private static bool TrySolveLeft(
        EmlHoleProbeValues right,
        EmlHoleProbeValues target,
        in EmlHoleBranchTurns turns,
        out EmlHoleProbeValues left)
    {
        bool p1 = TrySolveLeft(right.P1, target.P1, turns.P1, out Complex leftP1);
        bool p2 = TrySolveLeft(right.P2, target.P2, turns.P2, out Complex leftP2);
        bool p3 = TrySolveLeft(right.P3, target.P3, turns.P3, out Complex leftP3);
        left = new EmlHoleProbeValues(leftP1, leftP2, leftP3);
        return p1 && p2 && p3;
    }

    private static bool TrySolveLeft(Complex right, Complex target, int turn, out Complex left)
    {
        left = default;
        if (!IsFinite(right) || right == Complex.Zero || !IsFinite(target)) return false;
        Complex exponential = target + Complex.Log(right);
        if (!IsFinite(exponential) || exponential == Complex.Zero) return false;
        left = Complex.Log(exponential) + new Complex(0, 2.0 * Math.PI * turn);
        return IsFinite(left) && left.Real <= Eml.ExpReMax && NearlyEqual(Complex.Exp(left), exponential);
    }

    private static EmlHoleProbeValues ReadTarget(EmlResidualWitness witness)
        => new(witness.P1.Value, witness.P2.Value, witness.P3.Value);

    private static EmlHoleProbeValues ReadRoot(in EmlResidualExpressionEvaluation evaluation)
    {
        if (!evaluation.P1.Plain.Finite || !evaluation.P2.Plain.Finite || !evaluation.P3.Plain.Finite)
            return new EmlHoleProbeValues(InvalidComplex(), InvalidComplex(), InvalidComplex());
        return new EmlHoleProbeValues(
            evaluation.P1.Plain.Value,
            evaluation.P2.Plain.Value,
            evaluation.P3.Plain.Value);
    }

    internal static EmlHoleRepairOccurrenceCheck VerifyExpression(
        EmlResidualExpression expression,
        in EmlResidualExpressionEvaluation evaluation,
        in EmlResidualWitness witness,
        EmlGrader grader)
    {
        if (expression.TryRenderRPN(out string rpn))
        {
            EmlVerdict verdict = grader.GradeResidual(rpn, witness);
            if (verdict.Grade == 'E')
                return new EmlHoleRepairOccurrenceCheck(true, "finite-exact", verdict);
            bool finiteP1 = IsEnclosedByProbe(evaluation.P1, witness.P1);
            bool finiteP2 = IsEnclosedByProbe(evaluation.P2, witness.P2);
            bool finiteP3 = IsEnclosedByProbe(evaluation.P3, witness.P3);
            return new EmlHoleRepairOccurrenceCheck(
                finiteP1 && finiteP2 && finiteP3,
                finiteP1 && finiteP2 && finiteP3 ? "finite-enclosure" : $"finite-grade-{verdict.Grade}:probes-{(finiteP1 ? 'o' : 'x')}{(finiteP2 ? 'o' : 'x')}{(finiteP3 ? 'o' : 'x')}",
                verdict);
        }

        bool p1 = HasEquivalentProbe(evaluation.P1, witness.P1);
        bool p2 = HasEquivalentProbe(evaluation.P2, witness.P2);
        bool p3 = HasEquivalentProbe(evaluation.P3, witness.P3);
        return new EmlHoleRepairOccurrenceCheck(
            p1 && p2 && p3,
            p1 && p2 && p3 ? "process-enclosure-equivalent" : $"process-equality-unproved:{(p1 ? 'o' : 'x')}{(p2 ? 'o' : 'x')}{(p3 ? 'o' : 'x')}",
            null);
    }

    private static bool HasEquivalentProbe(EmlLadder candidate, EmlResidualProbe target)
        => candidate.Plain.Finite
            && candidate.Plain.Value == target.Value
            && candidate.Rect == target.Enclosure;

    private static bool IsEnclosedByProbe(EmlLadder candidate, EmlResidualProbe target)
        => candidate.Plain.Finite
            && !candidate.Rect.IsBlown
            && !target.Enclosure.IsBlown
            && target.Enclosure.Re.Contains(candidate.Rect.Re.Lo)
            && target.Enclosure.Re.Contains(candidate.Rect.Re.Hi)
            && target.Enclosure.Im.Contains(candidate.Rect.Im.Lo)
            && target.Enclosure.Im.Contains(candidate.Rect.Im.Hi);

    private static EmlTripleSignature CreateSignature(EmlHoleProbeValues values)
        => new(
            Eml.Signature(new EmlValue(values.P1, true), new EmlValue(values.P2, true), JoinSignatureDigits),
            Eml.Signature(new EmlValue(values.P3, true), new EmlValue(values.P3, true), JoinSignatureDigits));

    private static bool Matches(EmlHoleProbeValues candidate, EmlHoleProbeValues required)
        => NearlyEqual(candidate.P1, required.P1)
           && NearlyEqual(candidate.P2, required.P2)
           && NearlyEqual(candidate.P3, required.P3);

    private static bool NearlyEqual(Complex left, Complex right)
    {
        double scale = Math.Max(1.0, Math.Max(left.Magnitude, right.Magnitude));
        return IsFinite(left) && IsFinite(right) && (left - right).Magnitude <= JoinRelativeTolerance * scale;
    }

    private static bool IsFinite(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    private static Complex InvalidComplex() => new(double.NaN, double.NaN);

    private readonly record struct EmlTripleSignature(EmlSig Home, EmlSig Regime);

    private sealed record CandidateEvaluation(EmlHoleCandidate Candidate, EmlHoleProbeValues Values);

    private sealed class EmlHoleRepairProposalComparer : IComparer<EmlHoleRepairProposal>
    {
        public static readonly EmlHoleRepairProposalComparer Instance = new();

        public int Compare(EmlHoleRepairProposal left, EmlHoleRepairProposal right)
        {
            int byCost = left.Cost.Total.CompareTo(right.Cost.Total);
            if (byCost != 0) return byCost;
            int byProgram = string.CompareOrdinal(
                left.Expression.RenderCanonical(),
                right.Expression.RenderCanonical());
            if (byProgram != 0) return byProgram;
            return left.Provenance.Orientation.CompareTo(right.Provenance.Orientation);
        }
    }
}
