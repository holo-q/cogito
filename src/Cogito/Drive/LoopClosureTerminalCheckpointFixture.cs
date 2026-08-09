namespace Cogito;

using System.Text;
using System.Security.Cryptography;

/// Terminal custody corroboration for the no-Homeostat-policy arm. The fallback
/// organic census is minted at seal time; the effective checkpoint must carry
/// that same event before the sealed attempt can be trusted.
internal static class LoopClosureTerminalCheckpointFixture
{
    internal static bool Run(TextWriter output)
    {
        string root = Path.Combine(".tmp", $"loop-closure-terminal-{Guid.NewGuid():N}");
        string corpusPath = root + "-corpus.txt";
        try
        {
            File.WriteAllText(corpusPath, string.Join('\n', Enumerable.Range(0, 10).Select(index => $"loop closure terminal census {index}")) + "\n");
            CortexFlatPoolCurriculum curriculum = new()
            {
                Corpus = new CogitoCorpus
                {
                    Path = corpusPath,
                    ExpectedWorldSHA256 = FileCorpus.ComputeWorldSHA256(corpusPath, CogitoCorpus.DefaultGlob),
                },
                IntakeBatch = 1,
                SeedSpans = 1,
                MixEvery = 1,
            };
            Cortex cortex = new(new CortexConfig
            {
                RunName = "loop-closure-terminal-fixture",
                Steps = 1,
                Curriculum = curriculum,
                Learning = new CortexLearningConfig
                {
                    ConsolidationPhaseControl = CortexConsolidationPhaseControl.Interval,
                    IntervalConsolidationPhase = 0,
                    Homeostat = new CortexHomeostatConfig
                    {
                        Policy = HomeoPolicies.Reflex,
                        Autonomy = HomeostatAutonomyModes.Off,
                    },
                    CrossReflect = false,
                    ReplayRatio = 0,
                    Breach = false,
                    Loom = false,
                    Rhythm = false,
                    Simhash = CortexSimhash.Off,
                    NearDupe = false,
                    Antiunify = false,
                },
                Durability = new CortexDurabilityConfig { CheckpointEvery = 1, CurveEvery = 1 },
            });
            cortex.EnableLoopLineage();
            global::Cogito.Run run = global::Cogito.Run.Create(root);
            if (cortex.Run(run) != 0) throw new InvalidDataException("terminal checkpoint fixture Cortex run failed");

            string runID = Path.GetFileName(Path.GetFullPath(run.Dir));
            IReadOnlyList<LoopClosureLinkAttempt> attempts = LoopClosureLinkAttemptStore.Read(run.Dir, runID);
            LoopClosureLinkAttempt censusAttempt = attempts.Single(attempt =>
                attempt.Species == LoopClosureLinkSpecies.PreferenceDivergence
                && attempt.DenialReason == LoopClosureGateDenialReasons.NoOrganicOpportunity);
            using Tape tape = Checkpoint.LoadTape(run.Dir);
            bool retained = tape.Resolve(censusAttempt.EventID, out byte[] payload);
            bool census = retained && IsOrganicCensus(payload, Homeostat.PolicyID);
            bool digest = retained && LoopClosureLinkAttemptStore.DigestPayload(payload) == censusAttempt.EvidenceSHA256.Value;
            string attemptPath = LoopClosureLinkAttemptStore.RelativePath(censusAttempt.RecordID);
            RunAuthority authority = RunAuthority.Load(run.Dir);
            RunAuthorityArtifact? artifact = authority.Artifacts.SingleOrDefault(item => item.RelativePath == attemptPath);
            bool sealedAttempt = artifact is not null
                && string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(run.Dir, attemptPath)))), artifact.SHA256, StringComparison.Ordinal);
            bool exact = retained && census && digest && sealedAttempt;
            output.WriteLine($"  terminal link checkpoint · census={(census ? "retained" : "MISSING")} · digest={(digest ? "exact" : "BROKEN")} · attempt={(sealedAttempt ? "custodied" : "UNSEALED")} · {(exact ? "PASS" : "FAIL")}");
            return exact;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (File.Exists(corpusPath)) File.Delete(corpusPath);
        }
    }

    private static bool IsOrganicCensus(ReadOnlySpan<byte> payload, CortexPolicyID policy)
    {
        string text = Encoding.ASCII.GetString(payload);
        return text.StartsWith("LOOP-CLOSURE-ORGANIC-OPPORTUNITY\t", StringComparison.Ordinal)
            && text.Contains("\tpolicy=" + policy.Value, StringComparison.Ordinal)
            && text.EndsWith("\topportunities=0", StringComparison.Ordinal);
    }
}
