namespace Cogito;

public enum CortexTapeAdmissionActions : byte
{
    Reject,
    Admit,
}

internal enum CortexTapeAdmissionMetricIDs : ushort
{
    ParsedSymbols = 800,
    SemanticBytes,
    StoredBytes,
    Residual,
    Threshold,
    ResidualMargin,
    ExactSymbol,
    HasCover,
    GrammarRules,
    CompressedSymbols,
    TapeBytes,
    BudgetUtilization,
    Real,
    Replay,
    Breach,
    Reflected,
    Execution,
    Admitted = 840,
    AppendedBytes,
    ObservedResidual,
}

public readonly record struct CortexTapeAdmissionChoice(
    CortexPolicyDecision Decision,
    CortexTapeAdmissionActions Action,
    Radula.Affirmation Measurement,
    int StoredBytes);

public static partial class CortexTapeAdmission
{
    public static CortexPolicyID PolicyID { get; } = CortexPolicyID.Parse("cortex.tape-admission");

    public static CortexPolicySchema PolicySchema { get; } = new(
        PolicyID,
        featureCount: 17,
        actionCount: 2,
        outcomeCount: 3,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);

    internal static MetricSample[] CreateFeatureSamples(double[] values)
    {
        if (values.Length != PolicySchema.FeatureCount)
            throw new ArgumentException($"tape-admission policy expected {PolicySchema.FeatureCount} features, received {values.Length}", nameof(values));
        MetricSample[] features = new MetricSample[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            CortexTapeAdmissionMetricIDs metric = (CortexTapeAdmissionMetricIDs)((ushort)CortexTapeAdmissionMetricIDs.ParsedSymbols + i);
            bool integral = i is 0 or 1 or 2 or 6 or 7 or 8 or 9 or 10 or 12 or 13 or 14 or 15 or 16;
            NumericValue value = integral
                ? NumericValue.FromI64(checked((long)values[i]))
                : NumericValue.FromF64(values[i]);
            features[i] = new MetricSample(new MetricID((ushort)metric), value);
        }
        return features;
    }
}

public sealed partial class Cortex
{
    public CortexTapeAdmissionChoice ChooseTapeAdmission(
        Engine.GrammarCover? cover,
        ReadOnlySpan<byte> semanticBytes,
        int storedBytes,
        Provenances provenance,
        double affirmCut)
    {
        if (storedBytes < 0) throw new ArgumentOutOfRangeException(nameof(storedBytes));
        Radula.Affirmation measurement = Radula.MeasureAffirmation(cover, semanticBytes, affirmCut);
        double budgetUtilization = _config.Learning.GrammarBudgetBits > 0
            ? (double)Math.Max(0, Grammar.TotalSavings.Value) / _config.Learning.GrammarBudgetBits
            : 0;
        Span<MetricSample> features = stackalloc MetricSample[17]
        {
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.ParsedSymbols), NumericValue.FromI64(measurement.ParsedSymbols)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.SemanticBytes), NumericValue.FromI64(measurement.SemanticBytes)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.StoredBytes), NumericValue.FromI64(storedBytes)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Residual), NumericValue.FromF64(measurement.Residual)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Threshold), NumericValue.FromF64(affirmCut)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.ResidualMargin), NumericValue.FromF64(measurement.Residual - affirmCut)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.ExactSymbol), NumericValue.FromI64(measurement.ParsedSymbols <= 1 ? 1 : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.HasCover), NumericValue.FromI64(cover is null ? 0 : 1)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.GrammarRules), NumericValue.FromI64(Grammar.Rules.Length)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.CompressedSymbols), NumericValue.FromI64(Grammar.Compressed?.Length ?? 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.TapeBytes), NumericValue.FromI64(Tape.ByteLength)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.BudgetUtilization), NumericValue.FromF64(budgetUtilization)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Real), NumericValue.FromI64(provenance == Provenances.Real ? 1 : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Replay), NumericValue.FromI64(provenance == Provenances.Replay ? 1 : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Breach), NumericValue.FromI64(provenance == Provenances.Breach ? 1 : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Reflected), NumericValue.FromI64(provenance == Provenances.Reflected ? 1 : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Execution), NumericValue.FromI64(provenance == Provenances.Execution ? 1 : 0)),
        };
        int launchpadAction = measurement.Affirmed
            ? (int)CortexTapeAdmissionActions.Reject
            : (int)CortexTapeAdmissionActions.Admit;
        CortexPolicyDecision decision = ChoosePolicyAction(CortexTapeAdmission.PolicyID, launchpadAction, features);
        return new CortexTapeAdmissionChoice(
            decision,
            (CortexTapeAdmissionActions)decision.Action,
            measurement,
            storedBytes);
    }

    public void CompleteTapeAdmission(in CortexTapeAdmissionChoice choice, bool appended)
    {
        bool expectedAppend = choice.Action == CortexTapeAdmissionActions.Admit;
        Span<MetricSample> outcomes = stackalloc MetricSample[3]
        {
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.Admitted), NumericValue.FromI64(appended ? 1 : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.AppendedBytes), NumericValue.FromI64(appended ? choice.StoredBytes : 0)),
            new(new MetricID((ushort)CortexTapeAdmissionMetricIDs.ObservedResidual), NumericValue.FromF64(choice.Measurement.Residual)),
        };
        CortexPolicyDecision decision = choice.Decision;
        ResolvePolicyOutcome(
            in decision,
            outcomes,
            invariantClean: appended == expectedAppend,
            conservedCost: appended ? choice.StoredBytes : 0);
    }
}
