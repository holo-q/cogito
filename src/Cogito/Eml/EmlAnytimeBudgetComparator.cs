namespace Cogito;

using System.Security.Cryptography;
using System.Text;

/// One immutable observation on an arm's anytime frontier.  The evaluator axis is cumulative;
/// a comparator carries the actual spend and the unused planned interval so budget alignment
/// cannot silently collapse into a window-index comparison.
internal readonly record struct EmlAnytimeBudgetPoint(
    long EvaluatorSpend,
    long PlannedEvaluatorSpend,
    long ActualEvaluatorSpend,
    EmlAnytimeCommitments Quality,
    bool AccountingExact,
    bool EvidenceExact,
    string PointID)
{
    public long Slack => checked(PlannedEvaluatorSpend - ActualEvaluatorSpend);

    public void Validate(string label)
    {
        if (EvaluatorSpend < 0 || PlannedEvaluatorSpend < 0 || ActualEvaluatorSpend < 0)
            throw new InvalidDataException($"anytime budget point {label} has negative evaluator work");
        if (ActualEvaluatorSpend > PlannedEvaluatorSpend)
            throw new InvalidDataException($"anytime budget point {label} exceeds planned evaluator work");
        Quality.Validate(label);
        if (string.IsNullOrWhiteSpace(PointID)) throw new InvalidDataException($"anytime budget point {label} lacks identity");
    }
}

internal readonly record struct EmlAnytimeBudgetAlignment(
    long Budget,
    long LeftSpend,
    long RightSpend,
    long LeftSlack,
    long RightSlack,
    EmlAnytimeCommitments LeftQuality,
    EmlAnytimeCommitments RightQuality,
    bool LeftAccountingExact,
    bool RightAccountingExact,
    bool LeftEvidenceExact,
    bool RightEvidenceExact,
    bool RightNoWorse,
    bool StrictLaterGain,
    string LeftPointID,
    string RightPointID)
{
    public bool Passed => LeftAccountingExact && RightAccountingExact
        && LeftEvidenceExact && RightEvidenceExact && RightNoWorse;
}

internal readonly record struct EmlAnytimeBudgetComparison(
    bool Comparable,
    bool StepFunction,
    bool RightNoWorse,
    bool StrictLaterGain,
    EmlAnytimeBudgetAlignment[] Alignments,
    string Digest)
{
    public bool Passed => Comparable && RightNoWorse && StrictLaterGain;
}

/// Shared paired-fork adjudication.  Both Ruler and the standalone anytime kill line compare the
/// right arm's step-function incumbent at identical cumulative evaluator budgets; windows are
/// merely labels and never substitute for spend.
internal static class EmlAnytimeBudgetComparator
{
    internal static EmlAnytimeBudgetComparison Compare(
        IReadOnlyList<EmlAnytimeBudgetPoint> left,
        IReadOnlyList<EmlAnytimeBudgetPoint> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return new(false, false, false, false, [], Hash("empty"));
        Validate(left, "left");
        Validate(right, "right");
        long minimumBudget = Math.Max(left[0].EvaluatorSpend, right[0].EvaluatorSpend);
        List<long> thresholds = left.Select(static p => p.EvaluatorSpend)
            .Concat(right.Select(static p => p.EvaluatorSpend))
            .Where(spend => spend >= minimumBudget)
            .Distinct().OrderBy(static spend => spend).ToList();
        if (thresholds.Count == 0)
            return new(false, false, false, false, [], Hash("insufficient"));

        bool exact = thresholds.All(threshold => left.Any(point => point.EvaluatorSpend == threshold)
            && right.Any(point => point.EvaluatorSpend == threshold));
        List<EmlAnytimeBudgetAlignment> alignments = new(thresholds.Count);
        int leftIndex = 0, rightIndex = 0;
        EmlAnytimeBudgetPoint leftCurrent = left[0], rightCurrent = right[0];
        bool rightNoWorse = true;
        bool strictLaterGain = false;
        for (int i = 0; i < thresholds.Count; i++)
        {
            long budget = thresholds[i];
            while (leftIndex < left.Count && left[leftIndex].EvaluatorSpend <= budget) leftCurrent = left[leftIndex++];
            while (rightIndex < right.Count && right[rightIndex].EvaluatorSpend <= budget) rightCurrent = right[rightIndex++];
            bool noWorse = rightCurrent.Quality.Dominates(leftCurrent.Quality);
            bool strict = budget > minimumBudget && rightCurrent.Quality.Dominates(leftCurrent.Quality)
                && rightCurrent.Quality != leftCurrent.Quality;
            EmlAnytimeBudgetAlignment alignment = new(
                budget,
                leftCurrent.EvaluatorSpend,
                rightCurrent.EvaluatorSpend,
                leftCurrent.Slack,
                rightCurrent.Slack,
                leftCurrent.Quality,
                rightCurrent.Quality,
                leftCurrent.AccountingExact,
                rightCurrent.AccountingExact,
                leftCurrent.EvidenceExact,
                rightCurrent.EvidenceExact,
                noWorse,
                strict,
                leftCurrent.PointID,
                rightCurrent.PointID);
            alignments.Add(alignment);
            rightNoWorse &= alignment.Passed;
            strictLaterGain |= strict;
        }
        EmlAnytimeBudgetAlignment[] rows = alignments.ToArray();
        return new(true, !exact, rightNoWorse, strictLaterGain, rows, Hash(rows));
    }

    private static void Validate(IReadOnlyList<EmlAnytimeBudgetPoint> points, string label)
    {
        long prior = -1;
        for (int i = 0; i < points.Count; i++)
        {
            points[i].Validate(label);
            if (points[i].EvaluatorSpend < prior)
                throw new InvalidDataException($"anytime budget {label} evaluator spend regressed");
            prior = points[i].EvaluatorSpend;
        }
    }

    private static string Hash(object value)
    {
        string text = value is string literal ? literal : value is EmlAnytimeBudgetAlignment[] rows
            ? string.Join('\n', rows.Select(static row => string.Join('\t', row.Budget, row.LeftSpend, row.RightSpend,
                row.LeftSlack, row.RightSlack,
                row.LeftQuality.ExactClasses, row.LeftQuality.TheoremClasses, row.LeftQuality.CertificateClasses,
                row.LeftQuality.ClosedObligations, row.LeftQuality.HeldOutCaptures, row.LeftQuality.HeldOutBestK,
                row.LeftQuality.VerifiedLaws, row.LeftQuality.VerifiedProofs,
                row.RightQuality.ExactClasses, row.RightQuality.TheoremClasses, row.RightQuality.CertificateClasses,
                row.RightQuality.ClosedObligations, row.RightQuality.HeldOutCaptures, row.RightQuality.HeldOutBestK,
                row.RightQuality.VerifiedLaws, row.RightQuality.VerifiedProofs,
                row.LeftAccountingExact, row.RightAccountingExact, row.LeftEvidenceExact, row.RightEvidenceExact,
                row.RightNoWorse, row.StrictLaterGain,
                row.LeftPointID, row.RightPointID)))
            : value.ToString() ?? "";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
