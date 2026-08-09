namespace Cogito;

public enum HomeostatPolicyFeatureIDs : ushort
{
    Relax,
    Quiet,
    Collapsing,
    Sealed,
    Hot,
    Surprised,
    Heavy,
    Stalled,
    Speculative,
    PreviousConsolidationPhaseWasted,
    GrowthAboveMintParity,
    InduceOperationsPerByte,
    GenerateOperationsPerByte,
    GrowthRate,
    BitsPerSpan,
    ExperienceMint,
    ExperienceHit,
    ThoughtMint,
    Criticality,
    CriticalitySamples,
    Distinct,
    NovelChain,
    CollapseFraction,
    DistributionThird,
    JensenShannon,
    LoopConverged,
    Depth,
    MaximumSpan,
    MomentumStalled,
    UnvestedFraction,
    VestRate,
    ReplayEra,
    CriticalityMasked,
    SleepFraction,
    MixEvery,
    IntakeBatch,
    BudgetBits,
    BreachQuota,
    ForceGeneralize,
}

public static class HomeostatPolicyFeatures
{
    public const int Count = (int)HomeostatPolicyFeatureIDs.ForceGeneralize + 1;

    public static void Read(in HomeostatPolicyInput input, Span<double> destination)
    {
        if (destination.Length < Count) throw new ArgumentException("Homeostat policy feature destination is too small", nameof(destination));
        destination[..Count].Clear();
        int condition = (int)input.Context.Condition;
        destination[condition] = 1;
        destination[(int)HomeostatPolicyFeatureIDs.PreviousConsolidationPhaseWasted] = input.Context.PreviousConsolidationPhaseWasted ? 1 : 0;
        destination[(int)HomeostatPolicyFeatureIDs.GrowthAboveMintParity] = input.Context.GrowthAboveMintParity ? 1 : 0;
        Interocept senses = input.Senses;
        destination[(int)HomeostatPolicyFeatureIDs.InduceOperationsPerByte] = Canonicalize(senses.InduceOpb);
        destination[(int)HomeostatPolicyFeatureIDs.GenerateOperationsPerByte] = Canonicalize(senses.GenOpb);
        destination[(int)HomeostatPolicyFeatureIDs.GrowthRate] = Canonicalize(senses.GrowthRate);
        destination[(int)HomeostatPolicyFeatureIDs.BitsPerSpan] = Canonicalize(senses.BitsPerSpan);
        destination[(int)HomeostatPolicyFeatureIDs.ExperienceMint] = Canonicalize(senses.ExcMint);
        destination[(int)HomeostatPolicyFeatureIDs.ExperienceHit] = Canonicalize(senses.ExcHit);
        destination[(int)HomeostatPolicyFeatureIDs.ThoughtMint] = Canonicalize(senses.ThtMint);
        destination[(int)HomeostatPolicyFeatureIDs.Criticality] = Canonicalize(senses.Cvz);
        destination[(int)HomeostatPolicyFeatureIDs.CriticalitySamples] = senses.Kz;
        destination[(int)HomeostatPolicyFeatureIDs.Distinct] = senses.Distinct;
        destination[(int)HomeostatPolicyFeatureIDs.NovelChain] = senses.NovelChain;
        destination[(int)HomeostatPolicyFeatureIDs.CollapseFraction] = Canonicalize(senses.CollFrac);
        destination[(int)HomeostatPolicyFeatureIDs.DistributionThird] = Canonicalize(senses.DfThird);
        destination[(int)HomeostatPolicyFeatureIDs.JensenShannon] = Canonicalize(senses.Js);
        destination[(int)HomeostatPolicyFeatureIDs.LoopConverged] = senses.LoopConverged ? 1 : 0;
        destination[(int)HomeostatPolicyFeatureIDs.Depth] = Canonicalize(senses.Depth);
        destination[(int)HomeostatPolicyFeatureIDs.MaximumSpan] = Canonicalize(senses.MaxSpan);
        destination[(int)HomeostatPolicyFeatureIDs.MomentumStalled] = senses.MomentumStalled ? 1 : 0;
        destination[(int)HomeostatPolicyFeatureIDs.UnvestedFraction] = Canonicalize(senses.UnvestedFrac);
        destination[(int)HomeostatPolicyFeatureIDs.VestRate] = Canonicalize(senses.VestRate);
        destination[(int)HomeostatPolicyFeatureIDs.ReplayEra] = senses.ReplayEra ? 1 : 0;
        destination[(int)HomeostatPolicyFeatureIDs.CriticalityMasked] = senses.CvzMasked ? 1 : 0;
        HomeoActuation actuation = input.Actuation;
        destination[(int)HomeostatPolicyFeatureIDs.SleepFraction] = Canonicalize(actuation.SleepFrac);
        destination[(int)HomeostatPolicyFeatureIDs.MixEvery] = actuation.MixEvery;
        destination[(int)HomeostatPolicyFeatureIDs.IntakeBatch] = actuation.IntakeBatch;
        destination[(int)HomeostatPolicyFeatureIDs.BudgetBits] = actuation.BudgetBits;
        destination[(int)HomeostatPolicyFeatureIDs.BreachQuota] = actuation.BreachQuota;
        destination[(int)HomeostatPolicyFeatureIDs.ForceGeneralize] = actuation.ForceGeneralize ? 1 : 0;
    }

    public static PolicyCanonicalStateID ReadCanonicalState(in HomeostatPolicyInput input)
    {
        HomeostatPolicyContext context = input.Context;
        return PolicyCanonicalStates.Homeostat(Homeostat.PolicyID, in context);
    }

    private static double Canonicalize(double value)
    {
        if (double.IsNaN(value)) return double.NaN;
        return value == 0 ? 0 : value;
    }
}
