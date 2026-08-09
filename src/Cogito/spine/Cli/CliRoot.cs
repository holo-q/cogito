namespace Cogito.Cli;

using System.CommandLine;

// ── THE ROOT ──  the System.CommandLine command tree that IS the cogito CLI (Program.Main routes straight here).
// Builds one RootCommand("cogito") and hangs the surface off it in TWO TIERS: the flagship verbs stay top-level
// (`solve` · `cortex` · `dreamcalc`; the three nav verbs ride top-level until the unified `nav`),
// and the rest cluster under a parent command per domain (`kernel` · `drive` · `probe` · `rag` · `eml` · `tape`)
// so `cogito kernel grok`, `cogito drive critlock`, `cogito rag bench` read as a domain language. Each verb's
// SetAction reads ParseResult.GetValue and invokes its body (typed for the bodies homed here, or the ADAPTER-ARGV
// bridge for the stable-API `X.Run(string[])` engines). AOT-safe: the EXPLICIT api, no reflection model-binding.
internal static class CliRoot
{
    public static RootCommand Build()
    {
        var root = new RootCommand("cogito — the deterministic Q* scribe (Re-Pair + MDL + content-addressed log + replay)");

        // ── flagship verbs (top-level) — the loops you reach for by name ──
        root.Subcommands.Add(PrototypeCommands.Solve());       // the Cortex-backed LOC runtime
        root.Subcommands.Add(PrototypeCommands.Cortex());      // the full cortex loop
        root.Subcommands.Add(EmlTapeCommands.ReplayCalc());     // the EML dream-calculator
        root.Subcommands.Add(BuildCluster("gate", "registered experiment gates", GateCommands.Paired(), GateCommands.Adjudicate(), GateCommands.RegisterLoopClosure(), GateCommands.LoopClosure(), GateCommands.LoopClosureProbe(), GateCommands.CertifyLoopClosureProbe(), GateCommands.Certify(), GateCommands.LineageFixture(), GateCommands.AdjudicatorFixture(), GateCommands.RunnerFixture(), GateCommands.WorldFixture(), GateCommands.WorldNoveltyFixture(), GateCommands.WorldNoveltyScheduleFixture(), GateCommands.FuelScheduleFixture(), GateCommands.TapeRoleFixture(), GateCommands.RepositoryIntakeNullGate(), GateCommands.RepositorySurpriseNullGate(), GateCommands.RepositoryHistoryCriticalityNullGate(), GateCommands.RepositoryCompoundingNullGate(), GateCommands.RepositorySharpnessNullGate(), GateCommands.RepositoryIdiolectNullGate(), GateCommands.RepositoryLoopClosureNullGate(), GateCommands.RegistrationFixture()));

        // ── nav ──  the unified localization / lifelong / closed-loop eval (--mode frozen|dyn|loop) ──
        root.Subcommands.Add(AgentNavCommands.Nav());

        // ── kernel ──  introspection over the deterministic engine + the byte-identity verify gates
        root.Subcommands.Add(BuildCluster("kernel", "introspection over the engine — the grammar it learned, the substrate it holds, the verify gates",
            KernelCommands.Prove(), KernelCommands.Grammar(), KernelCommands.Export(), KernelCommands.Couplings(),
            KernelCommands.Log(), KernelCommands.Heaps(), KernelCommands.Know(), KernelCommands.Interp(),
            KernelCommands.Renorm(), KernelCommands.Grok(), KernelCommands.MergeTrace(), KernelCommands.TokRenorm(),
            KernelCommands.Overlap(), KernelCommands.Scoreboard(), KernelCommands.Fix(),
            KernelCommands.VerifyInduct(), KernelCommands.VerifyLoom(), KernelCommands.VerifyWeighted(), KernelCommands.VerifyGrammarAnalysis(), KernelCommands.VerifyEnergyIncremental(),
            SimhashCommands.Simhash(), SimhashCommands.SimhashVectors()));

        // ── drive ──  the self-play / kill-line drives
        root.Subcommands.Add(BuildCluster("drive", "the self-play / kill-line drives",
            DriveCommands.CreateMesh(), DriveCommands.Seriate(), DriveCommands.CritLock(), DriveCommands.GrokBell(), DriveCommands.AnnealEvict(),
            DriveCommands.Intake(), DriveCommands.FrontierBench(), DriveCommands.DomainWalk()));

        // ── probe ──  research one-offs + structural probes (the drawer)
        root.Subcommands.Add(BuildCluster("probe", "research one-offs + structural probes",
            DriveCommands.KillLine(), DriveCommands.Percolate(), DriveCommands.ClassTower(),
            KernelCommands.StructMatch(), EmlTapeCommands.SemGrammar(), EmlTapeCommands.ChunkMicroAssay(),
            PrototypeCommands.Stats(), PrototypeCommands.Gret(), PrototypeCommands.EdgeRerank(), PrototypeCommands.EdgeRuleRerank(),
            PrototypeCommands.DepthAutopsy(), PrototypeCommands.Swallow(), PrototypeCommands.Quadrant(),
            PrototypeCommands.Blur(), PrototypeCommands.Lattice(), PrototypeCommands.Osphradium(),
            PrototypeCommands.IgnitionJournalRead(), ExecCommands.TapeVm()));

        // ── rag ──  the LLM-in-the-loop retrieval + self-play cluster
        root.Subcommands.Add(BuildCluster("rag", "LLM-in-the-loop retrieval + self-play", [.. AgentRetrievalCommands.All()]));

        // ── eml ──  the EML dream-calc cluster
        root.Subcommands.Add(BuildCluster("eml", "the EML dream-calc cluster",
            EmlTapeCommands.EmlBench(), EmlTapeCommands.BasisCortexRematch(),
            EmlTapeCommands.Sheffer(), EmlTapeCommands.AntiUnify(), EmlTapeCommands.MintBench(),
            ProcessConstantCommands.Report()));

        // Profiling verbs remain under the recursion domain so their receipts share the run authority vocabulary.
        root.Subcommands.Add(BuildCluster("recursion", "control flow, executable knots, population, absorption, and duration",
            RecursionBranchCommands.RenderBranches(), RecursionCommands.ScanTowers(), RecursionCommands.CompareBranches(), RecursionCommands.RunEmlCacheAssay(),
            RecursionCommands.RunWeft(), RecursionCommands.RunMatchedForkProof(), RecursionCommands.RunMatchedForkRegression(), RecursionCommands.VerifyPolicyTrialJournal(), RecursionCommands.VerifyPolicyReadout(), RecursionCommands.VerifyPolicyReadoutFixture(), RecursionCommands.VerifyOrganicComparisonFixture(), RecursionCommands.VerifyPolicyCanonicalCoverageFixture(), RecursionCommands.VerifyComputeAccounting(), RecursionCommands.VerifyCheckpointDelta(), RecursionCommands.VerifyTerminalReceipt(), RecursionCommands.RunPolicyReadoutAssay(), RecursionCommands.RunPolicyBoundaryAssay(), RecursionCommands.RunThermometry(), RecursionCommands.RunPopulation(),
            RecursionCommands.CalibrateMarathon(), RecursionCommands.RunMarathon(), RecursionCommands.RunAnytimeCurveAssay(), RecursionCommands.RunEmlAnytimePairedKill(), RecursionCommands.VerifyPolicyBoundaryTrainingMount(), RecursionCommands.VerifyPolicyBoundaryMaterializationFixture(), RecursionCommands.VerifyPolicyBoundaryDivergenceTemporalSplitFixture(), RecursionCommands.VerifyReadoutTrainingCorroborationFixture(), RecursionCommands.VerifyLoopClosureResumeCorroborationFixture(), RecursionCommands.VerifyRunAuthorityFixture(), RecursionCommands.VerifyLoopClosureTerminalCheckpointFixture(), RecursionCommands.ProfileRunAuthority(), RecursionCommands.VerifyRung0ReceiptFixture(), RecursionCommands.VerifyHomeostatDestinationHandshakeFixture(), RecursionCommands.DeepRematchGateCommands()));

        // ── tape ──  the ONE memory: resume a run, materialize / self-test the corpus
        root.Subcommands.Add(BuildCluster("tape", "the tape — resume a run · materialize/gather the corpus",
            EmlTapeCommands.Resume(), AgentNavCommands.Traces(), AgentNavCommands.Corpus()));

        // ── runs ──  retention over the runs/ arc store (R18 ENOSPC prevention)
        root.Subcommands.Add(BuildCluster("runs", "retention over the runs/ arc store",
            RunsCommands.Gc()));

        return root;
    }

    /// Hang a set of verbs under one parent cluster command (`cogito <cluster> <verb>`).
    private static Command BuildCluster(string name, string description, params Command[] verbs)
    {
        var parent = new Command(name, description);
        foreach (var v in verbs) parent.Subcommands.Add(v);
        return parent;
    }

    /// The entry the CLI routes through — build the tree, parse, invoke. Returns the process exit code.
    public static int Run(string[] args) => Build().Parse(args).Invoke();
}
