namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public readonly record struct PolicyBoundaryAssayResult(
    bool CandidateReComposed,
    bool BaselineNoWorse,
    bool ForcedNullBehaviorExecuted,
    bool PersistenceExact,
    bool GuardClosed,
    string ReceiptDigest)
{
    public bool Passed => CandidateReComposed && BaselineNoWorse && ForcedNullBehaviorExecuted && PersistenceExact && GuardClosed;
}

/// Focused A3 battery. A boundary is never fabricated here: until the live homeostat checkpoint can be admitted by
/// the current checkpoint dialect and rerun through the four-arm funded ladder, this command records a grounded null.
public static class PolicyBoundaryAssay
{
    public static PolicyBoundaryAssayResult Run(TextWriter output)
    {
        const string Artifact = "runs/numeric-homeostat-final-checkpoint_0000";
        PolicyBoundaryHistoricalFixture fixture = PolicyBoundaryHistoricalFixture.TreeEra;
        string checkpoint = Path.Combine(Artifact, Checkpoint.FileName);
        string homeostat = Path.Combine(Artifact, "homeostat.txt");
        string trialRoot = Path.Combine(Artifact, "homeostat_trials", "step-00000147-98528D748A2492E9-long");
        string[] trialFiles = [
            Path.Combine(trialRoot, "baseline", "homeostat.txt"),
            Path.Combine(trialRoot, "candidate", "homeostat.txt"),
            Path.Combine(trialRoot, "baseline", "curve.tsv"),
            Path.Combine(trialRoot, "candidate", "curve.tsv")];
        if (!File.Exists(checkpoint) || !File.Exists(homeostat))
            throw new FileNotFoundException("A3 requires the immutable tree-era homeostat receipt artifact", Artifact);

        // The historical run directory is ignored generated output, not a stable source fixture. Its checkpoint
                // predates the current context-bound funding schema (`CORTEX!` versus the current dialect), so its trial files cannot
        // be revalidated against today's serializer. Keep the historical boundary as a provenance hint, but bank the
                // live A3 result until a fresh current-dialect run earns the four-arm receipt.
        byte[] checkpointBytes = File.ReadAllBytes(checkpoint);
        if (!Checkpoint.MatchesCurrentSchema(checkpointBytes))
        {
            string dialect = checkpointBytes.Length >= 8
                ? Encoding.ASCII.GetString(checkpointBytes, 0, 8).Trim()
                : "<truncated>";
            string retiredDigest = "grounded-null:retired-CORTEX!-historical-A3-source-requires-" + Checkpoint.CurrentDialect;
                output.WriteLine($"  policy-boundary A3 · artifact={fixture.SourceRun} dialect={dialect} candidate={fixture.CandidateBoundary} re-derived=BANKED baseline-no-worse=BANKED forced-null=BANKED horizons=16/64/256 continuity=BANKED matched-spend=BANKED resume=BANKED guard=BANKED receipt={retiredDigest} · BANKED NULL (rerun current {Checkpoint.CurrentDialect})");
            return new PolicyBoundaryAssayResult(false, false, false, false, false, retiredDigest);
        }

        if (trialFiles.Any(static path => !File.Exists(path)))
            throw new FileNotFoundException("A3 historical trial receipt is incomplete", trialRoot);
        string checkpointDigest = Convert.ToHexStringLower(SHA256.HashData(checkpointBytes));
        string homeostatDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(homeostat)));
        using MemoryStream trialBytes = new();
        for (int i = 0; i < trialFiles.Length; i++)
        {
            trialBytes.Write(File.ReadAllBytes(trialFiles[i]));
            trialBytes.WriteByte(0);
        }
        string trialDigest = Convert.ToHexStringLower(SHA256.HashData(trialBytes.ToArray()));
        if (!string.Equals(checkpointDigest, fixture.CheckpointSHA256, StringComparison.Ordinal)
            || !string.Equals(homeostatDigest, fixture.HomeostatReportSHA256, StringComparison.Ordinal)
            || !string.Equals(trialDigest, fixture.TrialReceiptSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("tree-era A3 artifact digest does not match the committed historical fixture");
        CortexPolicyID policy = fixture.Policy;
        PolicyBoundaryIdentity identity = new(policy, "tree-era-homeostat", "homeostat-policy-v1", "shared-actuation", fixture.CriticalityMetricID.ToString(CultureInfo.InvariantCulture), "criticality");
        PolicyBoundaryObligation obligation = new(identity);
        string provenance = homeostatDigest;
        obligation.ProposeObservedStatistics([fixture.BaselineBoundary.ToDouble(), fixture.CandidateBoundary.ToDouble()], "tree-era-homeostat:" + provenance);
        bool candidateReComposed = obligation.Candidates.Count == 2
            && obligation.Candidates.Any(x => x.Boundary == fixture.BaselineBoundary)
            && obligation.Candidates.Any(x => x.Boundary == fixture.CandidateBoundary);

        string digest = "grounded-null:live-" + Checkpoint.CurrentDialect + "-four-arm-receipt-required";
        output.WriteLine($"  policy-boundary A3 · artifact={provenance} candidate={fixture.CandidateBoundary} re-derived={(candidateReComposed ? "artifact-observed" : "NO")} baseline-no-worse=BANKED forced-null=BANKED horizons=16/64/256 continuity=BANKED matched-spend=BANKED resume=BANKED guard=BANKED receipt={digest} · BANKED NULL");
        return new PolicyBoundaryAssayResult(candidateReComposed, false, false, false, false, digest);
    }
}
