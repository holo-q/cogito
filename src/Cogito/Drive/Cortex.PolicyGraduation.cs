namespace Cogito;

internal enum CortexPolicyGraduationActions
{
    Reject,
    Promote,
}

internal readonly struct CortexPolicyHorizonEvidence(
    long baselineBenefit,
    long candidateBenefit,
    long baselineCost,
    long candidateCost,
    long baselineStabilityDebt,
    long candidateStabilityDebt,
    bool measured,
    bool invariantClean)
{
    public long BaselineBenefit { get; } = baselineBenefit;
    public long CandidateBenefit { get; } = candidateBenefit;
    public long BaselineCost { get; } = baselineCost;
    public long CandidateCost { get; } = candidateCost;
    public long BaselineStabilityDebt { get; } = baselineStabilityDebt;
    public long CandidateStabilityDebt { get; } = candidateStabilityDebt;
    public bool Measured { get; } = measured;
    public bool InvariantClean { get; } = invariantClean;
}

/// Learns how verified multi-horizon evidence composes into authority. The measurements and invariant verdicts
/// remain outside this policy, so a candidate can absorb the judge without becoming its own evidence source.
internal sealed class CortexPolicyGraduation
{
    internal static readonly CortexPolicyID PolicyID = new("cortex.policy-graduation");
    internal static readonly CortexPolicySchema PolicySchema = new(
        PolicyID, featureCount: 15, actionCount: 2, outcomeCount: 1,
        admission: CortexPolicyAdmissionKinds.Verified);

    private const int HorizonCount = 3;
    private ulong _lastVerifiedFingerprint;

    internal bool ShouldPromote(Cortex cortex, ReadOnlySpan<CortexPolicyHorizonEvidence> horizons)
    {
        if (horizons.Length != HorizonCount)
            throw new ArgumentException("policy graduation requires short, medium, and long evidence", nameof(horizons));
        TryGrantAuthority(cortex);

        Span<MetricSample> features = stackalloc MetricSample[15];
        ReadFeatures(horizons, features);
        CortexPolicyGraduationActions launchpad = Evaluate(horizons);
        CortexPolicyDecision decision = cortex.ChoosePolicyAction(PolicyID, (int)launchpad, features);
        bool agrees = decision.Action == (int)launchpad;
        Span<MetricSample> outcomes = stackalloc MetricSample[1]
        {
            new(new MetricID(824), NumericValue.FromI64(agrees ? 1 : 0)),
        };
        cortex.ResolvePolicyOutcome(in decision, outcomes, invariantClean: agrees, conservedCost: 0);
        return agrees ? decision.Action == (int)CortexPolicyGraduationActions.Promote : launchpad == CortexPolicyGraduationActions.Promote;
    }

    internal void AdvanceAuthority(Cortex cortex) => TryGrantAuthority(cortex);

    private void TryGrantAuthority(Cortex cortex)
    {
        if (!cortex.TryReadPolicyReadout(PolicyID, out CortexPolicyReadoutReceipt receipt)
            || cortex.HasPolicyGrammarAuthority(PolicyID, receipt.Fingerprint)
            || receipt.Fingerprint == _lastVerifiedFingerprint)
            return;

        _lastVerifiedFingerprint = receipt.Fingerprint;
        List<CortexPolicyHorizonEvidence[]> rows = CreateAdversarialRows();
        int comparisons = 0;
        int agreements = 0;
        int failures = 0;
        List<string> failureRows = new();
        for (int i = 0; i < rows.Count; i++)
        {
            Span<MetricSample> features = stackalloc MetricSample[15];
            ReadFeatures(rows[i], features);
            int expected = (int)Evaluate(rows[i]);
            CortexPolicyDecision decision = cortex.ChoosePolicyAction(PolicyID, expected, features);
            int actual = decision.RawCandidateAction;
            if (actual < 0) return;
            comparisons++;
            if (actual == expected) agreements++;
            else
            {
                failures++;
                failureRows.Add($"{i}:{(CortexPolicyGraduationActions)expected}→{(CortexPolicyGraduationActions)actual}");
            }
        }

        bool passed = comparisons == agreements && failures == 0;
        cortex.RecordPolicyOccurrenceCheck(PolicyID, receipt.Fingerprint, comparisons, agreements, failures, passed);
        Trace.Cortex.Boundary("policy.verify",
            $"policy={PolicyID} fp={receipt.Fingerprint:X16} adversarial={agreements}/{comparisons} failures={failures} rows={(failureRows.Count == 0 ? "none" : string.Join(',', failureRows))} result={(passed ? "PASS" : "reject")}");
        if (passed) cortex.TryGrantVerifiedPolicySuccession(
            PolicyID, receipt.Fingerprint, receipt.CandidateFingerprint, receipt.Revision);
    }

    private static CortexPolicyGraduationActions Evaluate(ReadOnlySpan<CortexPolicyHorizonEvidence> horizons)
    {
        bool strict = false;
        for (int i = 0; i < horizons.Length; i++)
        {
            CortexPolicyHorizonEvidence row = horizons[i];
            if (!row.Measured || !row.InvariantClean
                || row.CandidateBenefit < row.BaselineBenefit
                || row.CandidateCost > row.BaselineCost)
                return CortexPolicyGraduationActions.Reject;
            strict |= row.CandidateBenefit > row.BaselineBenefit || row.CandidateCost < row.BaselineCost;
        }
        CortexPolicyHorizonEvidence terminal = horizons[^1];
        return strict && terminal.CandidateStabilityDebt <= terminal.BaselineStabilityDebt
            ? CortexPolicyGraduationActions.Promote
            : CortexPolicyGraduationActions.Reject;
    }

    private static void ReadFeatures(ReadOnlySpan<CortexPolicyHorizonEvidence> horizons, Span<MetricSample> destination)
    {
        if (horizons.Length != HorizonCount || destination.Length != HorizonCount * 5)
            throw new ArgumentException("policy graduation feature shape is invalid");
        for (int i = 0; i < horizons.Length; i++)
        {
            CortexPolicyHorizonEvidence row = horizons[i];
            int offset = i * 5;
            destination[offset] = new MetricSample(new MetricID((ushort)(800 + offset)), NumericValue.FromF64((double)row.CandidateBenefit - row.BaselineBenefit));
            destination[offset + 1] = new MetricSample(new MetricID((ushort)(801 + offset)), NumericValue.FromF64((double)row.CandidateCost - row.BaselineCost));
            destination[offset + 2] = new MetricSample(new MetricID((ushort)(802 + offset)), NumericValue.FromF64((double)row.CandidateStabilityDebt - row.BaselineStabilityDebt));
            destination[offset + 3] = new MetricSample(new MetricID((ushort)(803 + offset)), NumericValue.FromI64(row.Measured ? 1 : 0));
            destination[offset + 4] = new MetricSample(new MetricID((ushort)(804 + offset)), NumericValue.FromI64(row.InvariantClean ? 1 : 0));
        }
    }

    private static List<CortexPolicyHorizonEvidence[]> CreateAdversarialRows()
    {
        List<CortexPolicyHorizonEvidence[]> rows = new();
        rows.Add(CreateRows());
        for (int horizon = 0; horizon < HorizonCount; horizon++)
        {
            rows.Add(CreateRows(strictHorizon: horizon, strictMagnitude: 2));
            rows.Add(CreateRows(strictHorizon: horizon, improveCost: true, strictMagnitude: 2));
            rows.Add(CreateRows(strictHorizon: horizon, strictMagnitude: 2, transientStabilityDebt: true));
        }
        rows.Add(CreateRows(strictHorizon: 2, strictMagnitude: 2, terminalStabilityDebt: true));
        AppendFailureRows(rows, strictMagnitude: 2);
        return rows;
    }

    private static void AppendFailureRows(List<CortexPolicyHorizonEvidence[]> rows, int strictMagnitude)
    {
        for (int horizon = 0; horizon < HorizonCount; horizon++)
        {
            int strictHorizon = (horizon + 1) % HorizonCount;
            rows.Add(CreateRows(strictHorizon: strictHorizon, strictMagnitude: strictMagnitude, unmeasuredHorizon: horizon));
            rows.Add(CreateRows(strictHorizon: strictHorizon, strictMagnitude: strictMagnitude, invariantFailureHorizon: horizon));
            rows.Add(CreateRows(strictHorizon: strictHorizon, strictMagnitude: strictMagnitude, benefitRegressionHorizon: horizon));
            rows.Add(CreateRows(strictHorizon: strictHorizon, strictMagnitude: strictMagnitude, costRegressionHorizon: horizon));
        }
    }

    private static CortexPolicyHorizonEvidence[] CreateRows(
        int strictHorizon = -1,
        bool improveCost = false,
        int strictMagnitude = 1,
        bool transientStabilityDebt = false,
        bool terminalStabilityDebt = false,
        int unmeasuredHorizon = -1,
        int invariantFailureHorizon = -1,
        int benefitRegressionHorizon = -1,
        int costRegressionHorizon = -1)
    {
        CortexPolicyHorizonEvidence[] rows = new CortexPolicyHorizonEvidence[HorizonCount];
        for (int i = 0; i < rows.Length; i++)
        {
            long baselineBenefit = 10 + i;
            long candidateBenefit = baselineBenefit;
            long baselineCost = 8 + i;
            long candidateCost = baselineCost;
            if (i == strictHorizon)
            {
                if (improveCost) candidateCost -= strictMagnitude;
                else candidateBenefit += strictMagnitude;
            }
            if (i == benefitRegressionHorizon) candidateBenefit--;
            if (i == costRegressionHorizon) candidateCost++;
            long baselineStability = 2;
            long candidateStability = transientStabilityDebt && i == 1 ? 3 : 2;
            if (terminalStabilityDebt && i == HorizonCount - 1) candidateStability = 3;
            rows[i] = new CortexPolicyHorizonEvidence(
                baselineBenefit, candidateBenefit, baselineCost, candidateCost,
                baselineStability, candidateStability,
                measured: i != unmeasuredHorizon,
                invariantClean: i != invariantFailureHorizon);
        }
        return rows;
    }
}
