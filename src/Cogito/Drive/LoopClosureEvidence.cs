namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Ronmamon;

/// Immutable, one-record-per-opportunity custody for loop-closure corroborationes. Producers
/// write these files when an ordinary event closes; the certifier only enumerates and
/// validates them. A record is never rewritten or replaced by a later opportunity.
internal static class LoopClosureEvidenceStore
{
    internal const string Root = "loop-closure";
    internal const int ObjectSchemaVersion = 2;

    internal static Func<CortexPolicyID, IPolicyBoundaryDomain> ResolveRegisteredDomain(IPolicyBoundaryDomain registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        return policy => policy.Equals(registered.PolicyID)
            ? registered
            : throw new InvalidDataException($"no policy-boundary domain is registered for {policy}");
    }

    internal static LoopClosureDigest DigestPattern(in PatternBecameThoughtCorroboration corroboration)
    {
        corroboration.Validate(requireCorroboration: true);
        string canonical = string.Join('|',
            corroboration.SourcePredictionID.Value,
            corroboration.ComposedPredictionID.Value,
            corroboration.CompositionNodeID.Value,
            corroboration.ProofSHA256.Value,
            corroboration.AuditSHA256.Value,
            corroboration.MainEvaluatorDelta,
            corroboration.NumericEvaluatorDelta,
            corroboration.TargetSpecies,
            string.Join(',', corroboration.SupportEventIDs.Select(static id => id.Value)),
            string.Join(',', corroboration.BasisLawAdmissionIDs));
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    internal static bool TryWritePattern(Run run, string admissionID, in PatternBecameThoughtCorroboration corroboration)
    { WritePattern(run, admissionID, in corroboration); return true; }

    internal static bool TryWriteR4(Run run, string recordID, in LoopClosureR4Provenance provenance)
    { WriteR4(run, recordID, in provenance); return true; }

    internal static bool TryWriteDivergence(Run run, string fundingID, in ThoughtOverruledInstinctCorroboration corroboration, IPolicyBoundaryDomain domain)
    { WriteDivergence(run, fundingID, in corroboration, domain); return true; }

    internal static bool TryWriteDivergence(Run run, string fundingID, in PolicyBoundaryDivergenceAdjudication adjudication, IPolicyBoundaryDomain domain)
    { WriteDivergence(run, fundingID, in adjudication, domain); return true; }

    internal static bool TryWriteDivergenceProof(Run run, in PolicyBoundaryDivergenceAdjudication adjudication, IPolicyBoundaryDomain domain)
    { WriteDivergenceProof(run, in adjudication, domain); return true; }

    internal static bool TryWriteObject(Run run, string outcomeID, in ObjectLoopClosedCorroboration corroboration)
    { WriteObject(run, outcomeID, in corroboration); return true; }

    internal static void WritePattern(Run run, string admissionID, in PatternBecameThoughtCorroboration corroboration)
    {
        corroboration.Validate(requireCorroboration: true);
        Write(run, "theory", admissionID, new LoopClosureEvidenceRON
        {
            kind = "theory",
            runID = RunID(run),
            recordID = admissionID,
            sourcePredictionID = corroboration.SourcePredictionID.Value,
            composedPredictionID = corroboration.ComposedPredictionID.Value,
            compositionNodeID = corroboration.CompositionNodeID.Value,
            proofSHA256 = corroboration.ProofSHA256.Value,
            auditSHA256 = corroboration.AuditSHA256.Value,
            mainEvaluatorDelta = corroboration.MainEvaluatorDelta,
            numericEvaluatorDelta = corroboration.NumericEvaluatorDelta,
            targetSpecies = corroboration.TargetSpecies.ToString(),
            supportEventIDs = corroboration.SupportEventIDs.Select(static id => (long)id.Value).ToArray(),
            basisLawAdmissionIDs = corroboration.BasisLawAdmissionIDs,
        });
    }

    internal static void WriteR4(Run run, string recordID, in LoopClosureR4Provenance provenance)
    {
        provenance.Validate();
        Write(run, "r4", recordID, new LoopClosureEvidenceRON
        {
            kind = "r4",
            runID = RunID(run),
            recordID = recordID,
            provenanceBase64 = Convert.ToBase64String(provenance.Encode()),
        });
    }

    internal static void WriteDivergence(Run run, string fundingID, in ThoughtOverruledInstinctCorroboration corroboration, IPolicyBoundaryDomain domain)
    {
        corroboration.Validate(true, domain);
        // Frozen journal/RON row kind; identifier-side name is Divergence.
        Write(run, "dissent", fundingID, new LoopClosureEvidenceRON
        {
            kind = "dissent",
            runID = RunID(run),
            recordID = fundingID,
            foldNodeID = corroboration.FoldNodeID.Value,
            foldRevision = corroboration.FoldRevision.Value,
            teacherEvidenceSHA256 = corroboration.TeacherEvidenceSHA256.Value,
            fundingID = corroboration.QuotaID.Value,
            dissentRevision = corroboration.DivergenceRevision.Value,
            launchpadAction = corroboration.LaunchpadAction,
            candidateExecutionOutcome = (byte)corroboration.CandidateExecutionOutcome,
            candidateRequestCount = corroboration.CandidateRequestCount,
            candidateGuardAdmittedCount = corroboration.CandidateGuardAdmittedCount,
            candidateAction = corroboration.CandidateAction,
            forcedDecisionID = corroboration.ForcedDecisionID.Value,
            forcedAction = corroboration.ForcedAction,
            forcedDiverged = corroboration.ForcedDiverged,
            nullReceiptSHA256 = corroboration.NullReceiptSHA256.Value,
            dissentProofBase64 = corroboration.DivergenceEvidenceBase64,
        });
    }

    internal static void WriteDivergenceProof(Run run, in PolicyBoundaryDivergenceAdjudication adjudication, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        adjudication.Validate(domain);
        string fundingID = adjudication.Proof.Funding.QuotaDecisionID.ToString();
        // Frozen journal/RON row kind; identifier-side name is Divergence.
        Write(run, "dissent", fundingID, new LoopClosureEvidenceRON
        {
            kind = "dissent",
            runID = RunID(run),
            recordID = fundingID,
            dissentEvidenceBase64 = Convert.ToBase64String(LoopClosureDivergenceEvidence.Encode(in adjudication, domain)),
        });
    }

    internal static void WriteDivergence(Run run, string fundingID, in PolicyBoundaryDivergenceAdjudication adjudication, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        adjudication.Validate(domain);
        // Frozen journal/RON row kind; identifier-side name is Divergence.
        Write(run, "dissent", fundingID, new LoopClosureEvidenceRON
        {
            kind = "dissent",
            runID = RunID(run),
            recordID = fundingID,
            dissentEvidenceBase64 = Convert.ToBase64String(LoopClosureDivergenceEvidence.Encode(in adjudication, domain)),
        });
    }

    internal static void WriteObject(Run run, string outcomeID, in ObjectLoopClosedCorroboration corroboration)
    {
        corroboration.Validate(requireCorroboration: true);
        Write(run, "object", outcomeID, new LoopClosureEvidenceRON
        {
            kind = "object",
            objectSchemaVersion = ObjectSchemaVersion,
            runID = RunID(run),
            recordID = outcomeID,
            outcomeNodeID = corroboration.OutcomeNodeID.Value,
            lineageSHA256 = corroboration.LineageSHA256.Value,
            patternEvidenceSHA256 = corroboration.PatternEvidenceSHA256.Value,
            dissentEvidenceSHA256 = corroboration.DivergenceEvidenceSHA256.Value,
            terminalOutcomeEventID = corroboration.TerminalOutcomeEventID,
            childOutcomeRunID = corroboration.ChildOutcome.RunID,
            childOutcomeRelativePath = corroboration.ChildOutcome.RelativePath,
            childOutcomeAuthoritySHA256 = corroboration.ChildOutcome.AuthoritySHA256.Value,
            childOutcomeRailSHA256 = corroboration.ChildOutcome.RailSHA256.Value,
            childOutcomeForcedDecisionID = corroboration.ChildOutcome.ForcedDecisionID.Value,
            childOutcomeEventID = corroboration.ChildOutcome.OutcomeEventID.Value,
            childOutcomePayloadSHA256 = corroboration.ChildOutcome.OutcomePayloadSHA256.Value,
            childOutcomeBeforeSeal = corroboration.ChildOutcome.BeforeSeal,
        });
    }

    internal static IReadOnlyList<PatternBecameThoughtCorroboration> ReadPattern(string directory, string runID)
        => Read(directory, "theory", runID, static document =>
        {
            PatternBecameThoughtCorroboration corroboration = new(
                new EmlPredictionID(checked((int)document.sourcePredictionID)), new EmlPredictionID(checked((int)document.composedPredictionID)),
                new LoopLineageNodeID(document.compositionNodeID), new(document.proofSHA256), new(document.auditSHA256),
                document.mainEvaluatorDelta, document.numericEvaluatorDelta,
                Enum.Parse<EmlObligationTargetSpecies>(document.targetSpecies),
                document.supportEventIDs.Select(static id => new TapeEventID(id)).ToArray(),
                document.basisLawAdmissionIDs);
            corroboration.Validate(requireCorroboration: true);
            return corroboration;
        });

    internal static IReadOnlyList<LoopClosureR4Provenance> ReadR4(string directory, string runID)
        => Read(directory, "r4", runID, static document =>
            LoopClosureR4Provenance.Decode(Convert.FromBase64String(document.provenanceBase64)));

    internal static IReadOnlyList<ThoughtOverruledInstinctCorroboration> ReadDivergence(string directory, string runID, Func<CortexPolicyID, IPolicyBoundaryDomain> resolveDomain)
        // Frozen journal/RON row kind; identifier-side name is Divergence.
        => Read(directory, "dissent", runID, document =>
        {
            string divergencePayload = !string.IsNullOrEmpty(document.dissentProofBase64)
                ? document.dissentProofBase64 : document.dissentEvidenceBase64;
            if (!string.IsNullOrEmpty(divergencePayload))
            {
                PolicyBoundaryDivergenceAdjudication adjudication = LoopClosureDivergenceEvidence.Decode(Convert.FromBase64String(divergencePayload), resolveDomain);
                PolicyBoundaryDivergenceProof proof = adjudication.Proof;
                if (proof.Teacher is not PolicyBoundaryTeacherCorroboration teacher)
                    throw new InvalidDataException("divergence evidence omits its teacher corroboration");
                ThoughtOverruledInstinctCorroboration persisted = new(
                    teacher.FoldNodeID, teacher.FoldRevision, new LoopClosureDigest(teacher.EvidenceSHA256),
                    new LoopClosureQuotaID(proof.Funding.QuotaDecisionID.ToString()), proof.ReadoutRevision,
                    proof.LaunchpadAction, proof.Candidate.Outcome, proof.Candidate.RequestCount, proof.Candidate.GuardAdmittedCount,
                    proof.Candidate.ExecutedOutcome?.Action ?? -1,
                    proof.ForcedNull.DecisionID, proof.ForcedNull.Action, proof.ForcedNull.Diverged, proof.ForcedNull.OutcomeID,
                    Convert.ToBase64String(LoopClosureDivergenceEvidence.Encode(in adjudication, resolveDomain(adjudication.Proof.Policy)
                        ?? throw new InvalidDataException($"no policy-boundary domain is registered for {adjudication.Proof.Policy}"))));
                persisted.Validate(true, resolveDomain(adjudication.Proof.Policy));
                return persisted;
            }
            ThoughtOverruledInstinctCorroboration corroboration = new(
                new LoopLineageNodeID(document.foldNodeID), new GrammarRevisionID(document.foldRevision),
                new(document.teacherEvidenceSHA256), new(document.fundingID), new GrammarRevisionID(document.dissentRevision),
                document.launchpadAction, (CortexPolicyTrialExecutionOutcomes)document.candidateExecutionOutcome,
                document.candidateRequestCount, document.candidateGuardAdmittedCount, document.candidateAction,
                new CortexPolicyDecisionID(document.forcedDecisionID), document.forcedAction, document.forcedDiverged,
                new(document.nullReceiptSHA256));
            throw new InvalidDataException("legacy divergence corroboration omits the policy domain required for typed audit");
            return corroboration;
        });

    internal static IReadOnlyList<PolicyBoundaryDivergenceAdjudication> ReadDivergenceProof(string directory, string runID, Func<CortexPolicyID, IPolicyBoundaryDomain> resolveDomain)
        // Frozen journal/RON row kind; identifier-side name is Divergence.
        => Read(directory, "dissent", runID, document =>
        {
            string payload = !string.IsNullOrEmpty(document.dissentProofBase64)
                ? document.dissentProofBase64 : document.dissentEvidenceBase64;
            if (string.IsNullOrEmpty(payload))
                throw new InvalidDataException("loop-closure divergence record omits its typed policy-boundary proof");
            PolicyBoundaryDivergenceAdjudication adjudication = LoopClosureDivergenceEvidence.Decode(Convert.FromBase64String(payload), resolveDomain);
            if (!string.Equals(adjudication.Proof.Funding.QuotaDecisionID.ToString(), document.recordID, StringComparison.Ordinal))
                throw new InvalidDataException("loop-closure divergence record ID disagrees with payment audit");
            return adjudication;
        });

    internal static IReadOnlyList<ObjectLoopClosedCorroboration> ReadObject(string directory, string runID)
        => Read(directory, "object", runID, static document =>
        {
            if (document.objectSchemaVersion != ObjectSchemaVersion)
                throw new InvalidDataException("object-loop-closed evidence schema is unsupported");
            ObjectLoopClosedCorroboration corroboration = new(
                new LoopLineageNodeID(document.outcomeNodeID), new(document.lineageSHA256), new(document.patternEvidenceSHA256),
                new(document.dissentEvidenceSHA256), document.terminalOutcomeEventID,
                new LoopClosureChildOutcomeReference(document.childOutcomeRunID, document.childOutcomeRelativePath,
                    new(document.childOutcomeAuthoritySHA256), new(document.childOutcomeRailSHA256),
                    new CortexPolicyDecisionID(document.childOutcomeForcedDecisionID), new TapeEventID(document.childOutcomeEventID),
                    new(document.childOutcomePayloadSHA256), document.childOutcomeBeforeSeal));
            corroboration.Validate(requireCorroboration: true);
            return corroboration;
        });

    private static void Write(Run run, string kind, string recordID, LoopClosureEvidenceRON document)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordID);
        if (recordID.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || recordID.Contains('/') || recordID.Contains('\\'))
            throw new InvalidDataException("loop-closure evidence record ID is not a safe filename");
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("loop-closure evidence encoding is nondeterministic");
        string relative = Path.Combine(Root, kind, recordID + ".ron");
        string path = run.PathOf(relative);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(first))
                throw new IOException($"loop-closure evidence record already exists with different bytes: {relative}");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        run.WriteAtomic(relative, stream => stream.Write(first));
    }

    private static IReadOnlyList<T> Read<T>(string directory, string kind, string runID, Func<LoopClosureEvidenceRON, T> decode)
    {
        string root = Path.Combine(Path.GetFullPath(directory), Root, kind);
        if (!Directory.Exists(root)) return [];
        List<T> values = [];
        foreach (string path in Directory.EnumerateFiles(root, "*.ron", SearchOption.TopDirectoryOnly).OrderBy(static path => path, StringComparer.Ordinal))
        {
            LoopClosureEvidenceRON document = RonSerializer.Deserialize<LoopClosureEvidenceRON>(File.ReadAllBytes(path));
            if (document.kind != kind || document.runID != runID || document.recordID != Path.GetFileNameWithoutExtension(path))
                throw new InvalidDataException($"loop-closure {kind} evidence identity disagrees with its arm");
            T value = decode(document);
            values.Add(value);
        }
        return values;
    }

    private static string RunID(Run run) => Path.GetFileName(Path.GetFullPath(run.Dir));
}

[RonObject]
internal partial class LoopClosureEvidenceRON
{
    public int objectSchemaVersion;
    public string kind = "";
    public string runID = "";
    public string recordID = "";
    public long sourcePredictionID;
    public long composedPredictionID;
    public string compositionNodeID = "";
    public string proofSHA256 = "";
    public string auditSHA256 = "";
    public long mainEvaluatorDelta;
    public long numericEvaluatorDelta;
    public string targetSpecies = "Residual";
    public long[] supportEventIDs = [];
    public string[] basisLawAdmissionIDs = [];
    public string provenanceBase64 = "";
    public string foldNodeID = "";
    public ulong foldRevision;
    public string teacherEvidenceSHA256 = "";
    public string fundingID = "";
    public ulong dissentRevision;
    public int launchpadAction;
    public byte candidateExecutionOutcome;
    public long candidateRequestCount;
    public long candidateGuardAdmittedCount;
    public int candidateAction;
    public ulong forcedDecisionID;
    public int forcedAction;
    public bool forcedDiverged;
    public string nullReceiptSHA256 = "";
    public string dissentProofBase64 = "";
    public string dissentEvidenceBase64 = "";
    public string outcomeNodeID = "";
    public string lineageSHA256 = "";
    public string patternEvidenceSHA256 = "";
    public string dissentEvidenceSHA256 = "";
    public long terminalOutcomeEventID = -1;
    public string childOutcomeRunID = ""; public string childOutcomeRelativePath = "";
    public string childOutcomeAuthoritySHA256 = ""; public string childOutcomeRailSHA256 = "";
    public ulong childOutcomeForcedDecisionID; public long childOutcomeEventID;
    public string childOutcomePayloadSHA256 = ""; public bool childOutcomeBeforeSeal;
}
