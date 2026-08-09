namespace Cogito;

/// Immutable provenance for the only tree-era numeric boundary admitted to the A3 lane. The run directory is a
/// lookup hint; these digests are the authority that lets another checkout reject a substituted local artifact.
internal readonly record struct PolicyBoundaryHistoricalFixture(
    string SourceRun,
    string CheckpointSHA256,
    string HomeostatReportSHA256,
    CortexPolicyID Policy,
    ushort CriticalityMetricID,
    PolicyBoundaryRational BaselineBoundary,
    PolicyBoundaryRational CandidateBoundary,
    string TrialReceiptSHA256)
{
    internal static PolicyBoundaryHistoricalFixture TreeEra { get; } = new(
        "numeric-homeostat-final-checkpoint_0000",
        "574c959d0986977ed68504836d9e06145b34cb0ff6e4a857efe6fdd3888a8925",
        "7100b1f6495d283a052b90a35a1e8b623d1ff2cdc309ca13b5a330a940c109c4",
        Homeostat.PolicyID,
        418,
        PolicyBoundaryRational.Parse("18594974030796563/100000000000000000"),
        PolicyBoundaryRational.Parse("2197719509828559/10000000000000000"),
        "a1a5be74930ad4a4a5e0aecd65938c7b6b5336bf1ea615e1e72552033cfa6d15");

    internal PolicyBoundaryObligation CreateObligation()
    {
        PolicyBoundaryIdentity identity = new(Policy, "tree-era-homeostat", "homeostat-policy-v1", "shared-actuation",
            CriticalityMetricID.ToString(System.Globalization.CultureInfo.InvariantCulture), "criticality");
        PolicyBoundaryObligation obligation = new(identity);
        obligation.Propose(new PolicyBoundaryCandidate(BaselineBoundary, PolicyBoundaryComparisons.LessThanOrEqual,
            "tree-era-homeostat:" + HomeostatReportSHA256));
        obligation.Propose(new PolicyBoundaryCandidate(CandidateBoundary, PolicyBoundaryComparisons.LessThanOrEqual,
            "tree-era-homeostat:" + HomeostatReportSHA256));
        return obligation;
    }
}
