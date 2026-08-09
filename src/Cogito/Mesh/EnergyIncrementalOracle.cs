namespace Cogito;

using Cogito.Grammar;
using Cogito.Induct;

/// Differential gate for publication-driven Energy.  Every publication is compared with
/// fresh raw counts, transitions, scorer CSR, and a fresh sampler instance.
public static class EnergyIncrementalOracle
{
    public static int Verify()
    {
        byte[] baseBytes = "alpha alpha alpha\nbeta beta beta\n"u8.ToArray();
        RePairResult baseResult = Engine.Induce(baseBytes).Result;
        var basePub = InstallRevision.FromRePair(new GrammarRevisionID(1), GrammarRevisionID.Zero, in baseResult);
        var incremental = new EnergyPolicy("coupling");
        bool ok = CheckInstallRevision(incremental, basePub, "initial", 0xA11CEUL);
        var standing = new Seriate.WeaveModel();
        standing.Reset(baseResult);
        ok &= CheckStandingScorer(standing, baseResult, "initial");

        Symbol[] appended = [.. baseResult.Compressed, new Symbol((byte)'!')];
        var appendSnapshot = new GrammarSnapshot(new GrammarRevisionID(2), baseResult.Rules, appended, baseResult.TotalSavings, baseResult.AlphabetSize);
        var appendDelta = new GrammarDelta(basePub.Revision, appendSnapshot.Revision, [], [],
            [GrammarSequenceEdit.Replace(baseResult.Compressed.Length, 0, [new Symbol((byte)'!')])], Mbits.Zero, GrammarResetKinds.None);
        var appendPub = new InstallRevision(appendSnapshot, appendDelta);
        ok &= CheckInstallRevision(incremental, appendPub, "append", 0xA11CEUL);
        RePairResult appendResult = appendSnapshot.ToRePairResult();
        standing.Ensure(appendResult);
        ok &= CheckStandingScorer(standing, appendResult, "append");

        Symbol[] locallyReplaced = [.. appendSnapshot.Compressed];
        locallyReplaced[^1] = new Symbol((byte)'?');
        var localReplaceSnapshot = new GrammarSnapshot(new GrammarRevisionID(3), appendSnapshot.Rules, locallyReplaced,
            appendSnapshot.TotalSavings, appendSnapshot.AlphabetSize);
        var localReplaceDelta = new GrammarDelta(appendPub.Revision, localReplaceSnapshot.Revision, [], [],
            [GrammarSequenceEdit.Replace(locallyReplaced.Length - 1, 1, [new Symbol((byte)'?')])], Mbits.Zero, GrammarResetKinds.None);
        ok &= CheckInstallRevision(incremental, new InstallRevision(localReplaceSnapshot, localReplaceDelta), "splice", 0xA11CEUL);
        RePairResult localResult = localReplaceSnapshot.ToRePairResult();
        standing.Ensure(localResult);
        ok &= CheckStandingScorer(standing, localResult, "splice");

        // A rule-only publication must invalidate the rule/unit model but retain the same
        // count revision and scorer materialization object.
        GrammarRule extraRule = baseResult.Rules.Length == 0 ? default : baseResult.Rules[0];
        GrammarRule[] ruleSnapshot = [.. localReplaceSnapshot.Rules, extraRule];
        var ruleOnlySnapshot = new GrammarSnapshot(new GrammarRevisionID(25), ruleSnapshot, localReplaceSnapshot.Compressed,
            localReplaceSnapshot.TotalSavings, localReplaceSnapshot.AlphabetSize);
        var ruleOnlyDelta = new GrammarDelta(localReplaceSnapshot.Revision, ruleOnlySnapshot.Revision, [extraRule], [], [], Mbits.Zero, GrammarResetKinds.None);
        var beforeScorers = incremental.PublishedScorers;
        var ruleOnlyPub = new InstallRevision(ruleOnlySnapshot, ruleOnlyDelta);
        var ruleOnlyReceipt = incremental.Apply(ruleOnlyPub);
        RePairResult ruleOnlyResult = ruleOnlySnapshot.ToRePairResult();
        standing.Ensure(ruleOnlyResult);
        ok &= CheckStandingScorer(standing, ruleOnlyResult, "rule-append");
        ok &= !ruleOnlyReceipt.CountsChanged && ReferenceEquals(beforeScorers, incremental.PublishedScorers);
        byte[] ruleOnlyBytes = incremental.Generate(ruleOnlyPub, 12, 0xA11CEUL, new Metabolism(), Weights.Coupling);
        var ruleOnlyExpected = CouplingCounts.Build(ruleOnlySnapshot.ToRePairResult(), ruleOnlySnapshot.Revision);
        bool ruleOnlyCounts = incremental.PublishedCounts is { } ruleCounts && ruleCounts.Matches(ruleOnlyExpected);
        bool ruleOnlySample = ruleOnlyBytes.AsSpan().SequenceEqual(new EnergyPolicy("coupling").Generate(ruleOnlyPub, 12, 0xA11CEUL, new Metabolism(), Weights.Coupling));
        ok &= ruleOnlyCounts && ruleOnlySample;
        Console.WriteLine($"  rule-only consumers · counts={(ruleOnlyCounts ? "ok" : "FAIL")} scorers={(ReferenceEquals(beforeScorers, incremental.PublishedScorers) ? "retained" : "FAIL")} sample={(ruleOnlySample ? "ok" : "FAIL")}");
        var sameReceipt = incremental.Apply(ruleOnlyPub);
        bool sameRevisionNoop = !sameReceipt.RulesChanged && !sameReceipt.CountsChanged && !sameReceipt.SequenceRebuilt && !sameReceipt.CountsRebuilt;
        ok &= sameRevisionNoop;
        Console.WriteLine($"  rule-only apply · count-rebuild={(ruleOnlyReceipt.CountsRebuilt ? "FAIL" : "none")} · same-revision={(sameRevisionNoop ? "no-op" : "FAIL")}");

        byte[] replacementBytes = "gamma gamma gamma\ndelta delta delta\n"u8.ToArray();
        RePairResult replacement = Engine.Induce(replacementBytes).Result;
        var replacementPub = InstallRevision.FromRePair(new GrammarRevisionID(26), ruleOnlySnapshot.Revision, in replacement);
        ok &= CheckInstallRevision(incremental, replacementPub, "reset", 0xBEEFUL);
        standing.Ensure(replacement);
        ok &= CheckStandingScorer(standing, replacement, "reset");

        var resetPub = InstallRevision.FromRePair(new GrammarRevisionID(27), replacementPub.Revision, in replacement);
        ok &= CheckInstallRevision(incremental, resetPub, "rebase", 0xBEEFUL);
        standing.Ensure(replacement);
        ok &= CheckStandingScorer(standing, replacement, "rebase-noop");

        bool topology = standing.Resets == 2 && standing.Appends >= 1 && standing.Splices >= 2;
        ok &= topology;
        Console.WriteLine($"  standing scorer · resets={standing.Resets} appends={standing.Appends} splices={standing.Splices} topology={(topology ? "ok" : "FAIL")}");
        Console.WriteLine($"verify-energy-incremental · append/splice/rule-only/reset/rebase counts+transitions+scorers+sample · {(ok ? "PASS" : "FAIL")}");
        return ok ? 0 : 1;
    }

    private static bool CheckStandingScorer(Seriate.WeaveModel standing, RePairResult grammar, string label)
    {
        var fresh = Couplings.Learn(grammar).BuildScorer(minCocount: 1);
        bool match = standing.Rich.Matches(fresh);
        Console.WriteLine($"  standing-{label,-9} scorer={(match ? "ok" : "FAIL")} edges={standing.Rich.EdgeCount} touched-remove={standing.Rich.LastRemovedKeys} touched-add={standing.Rich.LastIndexedKeys}");
        return match;
    }

    private static bool CheckInstallRevision(EnergyPolicy incremental, InstallRevision publication, string label, ulong seed)
    {
        byte[] incrementalBytes = incremental.Generate(publication, 12, seed, new Metabolism(), Weights.Coupling);
        var expectedCounts = CouplingCounts.Build(publication.Snapshot.ToRePairResult(), publication.Revision);
        var expectedCouplings = Couplings.FromCounts(publication.Snapshot.Rules, expectedCounts);
        var expectedScorers = new ScorerMaterialization(expectedCouplings, expectedCounts.CountRevision);
        var fresh = new EnergyPolicy("coupling");
        byte[] freshBytes = fresh.Generate(publication, 12, seed, new Metabolism(), Weights.Coupling);
        byte[] legacyBytes = new EnergyPolicy("coupling").Generate(publication.Snapshot.ToRePairResult(), 12, seed, new Metabolism(), Weights.Coupling);
        bool counts = incremental.PublishedCounts is { } actualCounts && actualCounts.Matches(expectedCounts);
        bool transitions = incremental.TransEvidence is { } actualTransitions && actualTransitions.Matches(expectedCounts.BuildTransitions());
        bool scorers = incremental.PublishedScorers is { } actualScorers && actualScorers.Matches(expectedScorers);
        bool bytes = incrementalBytes.AsSpan().SequenceEqual(freshBytes);
        bool legacy = incrementalBytes.AsSpan().SequenceEqual(legacyBytes);
        Console.WriteLine($"  {label,-7} counts={(counts ? "ok" : "FAIL")} transitions={(transitions ? "ok" : "FAIL")} scorers={(scorers ? "ok" : "FAIL")} sample={(bytes ? "ok" : "FAIL")} legacy={(legacy ? "ok" : "FAIL")}");
        return counts && transitions && scorers && bytes && legacy;
    }
}
