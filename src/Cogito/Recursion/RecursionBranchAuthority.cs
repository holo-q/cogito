namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

internal enum RecursionVerdicts : byte
{
    Graduated,
    Null,
    Hold,
    Absent,
    Pending
}

internal enum RecursionCapabilities : byte
{
    ProcedureBinding,
    EMLProcedurePayoff,
    RelativeAllocator,
    DelayedFertility,
    LawStore,
    MITMSearch,
    FormalProofAttachment,
    AbsorptionSafeguards,
    TowerSubstrate
}

internal enum RecursionWaveArms : byte
{
    R1Branching,
    R2GrammarKnots,
    R2ProcedureRecursion,
    R2KnottedReplays,
    R3Population,
    R4WeftDomain,
    R4ProofFeedback,
    R5OrganAbsorption,
    R6Marathon
}

internal enum RecursionGateModes : byte
{
    Open,
    Degraded,
    Closed,
    Pending
}

internal readonly record struct RecursionBranchVerdict(
    RecursionCapabilities Capability,
    RecursionVerdicts Verdict,
    string Evidence,
    string Caveat);

internal readonly record struct RecursionWaveGate(
    RecursionWaveArms Arm,
    RecursionGateModes Mode,
    string Requirement,
    string DegradedPath);

internal sealed class RecursionBranchAuthority
{
    internal const int SchemaVersion = 1;

    internal required string Digest { get; init; }
    internal required List<RecursionBranchVerdict> Verdicts { get; init; }
    internal required List<RecursionWaveGate> Gates { get; init; }

    internal RecursionBranchVerdict GetVerdict(RecursionCapabilities capability)
    {
        RecursionBranchVerdict? found = null;
        for (int i = 0; i < Verdicts.Count; i++)
        {
            RecursionBranchVerdict candidate = Verdicts[i];
            if (candidate.Capability != capability) continue;
            if (found.HasValue) throw new InvalidDataException($"branch authority repeats capability {capability}");
            found = candidate;
        }
        return found ?? throw new InvalidDataException($"branch authority omits capability {capability}");
    }

    internal RecursionWaveGate GetGate(RecursionWaveArms arm)
    {
        RecursionWaveGate? found = null;
        for (int i = 0; i < Gates.Count; i++)
        {
            RecursionWaveGate candidate = Gates[i];
            if (candidate.Arm != arm) continue;
            if (found.HasValue) throw new InvalidDataException($"branch authority repeats wave arm {arm}");
            found = candidate;
        }
        return found ?? throw new InvalidDataException($"branch authority omits wave arm {arm}");
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Digest)) throw new InvalidDataException("branch authority digest cannot be blank");
        if (Verdicts.Count != Enum.GetValues<RecursionCapabilities>().Length)
            throw new InvalidDataException("branch authority must decide every recursion capability exactly once");
        if (Gates.Count != Enum.GetValues<RecursionWaveArms>().Length)
            throw new InvalidDataException("branch authority must gate every recursion wave arm exactly once");

        foreach (RecursionCapabilities capability in Enum.GetValues<RecursionCapabilities>())
        {
            RecursionBranchVerdict verdict = GetVerdict(capability);
            if (string.IsNullOrWhiteSpace(verdict.Evidence))
                throw new InvalidDataException($"branch verdict {capability} has no evidence");
        }
        foreach (RecursionWaveArms arm in Enum.GetValues<RecursionWaveArms>())
        {
            RecursionWaveGate gate = GetGate(arm);
            if (string.IsNullOrWhiteSpace(gate.Requirement))
                throw new InvalidDataException($"branch gate {arm} has no requirement");
            if (gate.Mode == RecursionGateModes.Degraded && string.IsNullOrWhiteSpace(gate.DegradedPath))
                throw new InvalidDataException($"degraded branch gate {arm} has no degraded path");
        }
    }
}

internal static class RecursionBranchAuthorityStore
{
    internal static RecursionBranchAuthority CreateCurrent()
    {
        RecursionBranchAuthority authority = new()
        {
            Digest = "pending",
            Verdicts =
            [
                new(RecursionCapabilities.ProcedureBinding, RecursionVerdicts.Graduated,
                    "Provenance-typed LOC routing transferred at 100 percent while shuffled bindings collapsed to 0 of 50.",
                    "This establishes causal binding, not mathematical discovery yield."),
                new(RecursionCapabilities.EMLProcedurePayoff, RecursionVerdicts.Null,
                    "Matched and shuffled EML procedures produced no immediate mathematical payoff.",
                    "R1 may extend the representation but cannot pre-claim discovery improvement."),
                new(RecursionCapabilities.RelativeAllocator, RecursionVerdicts.Null,
                    "The relative allocator did not beat round-robin or shuffled controls.",
                    "Fixed scheduling remains authoritative; allocator selection stays disabled."),
                new(RecursionCapabilities.DelayedFertility, RecursionVerdicts.Null,
                    "Actual-root forks opened no exclusive theorem classes at plus 10 or plus 100 evaluator calls.",
                    "Fertility remains report-only and has no selection or residency authority."),
                new(RecursionCapabilities.LawStore, RecursionVerdicts.Graduated,
                    "Two behavior-verified law classes survived save, load, and replay without duplicate admission.",
                    "The OFF arm produced 94 candidates, 84 passing the current behavior verifier, but admitted none; admission-policy abundance remains unresolved."),
                new(RecursionCapabilities.MITMSearch, RecursionVerdicts.Hold,
                    "MITM found 2 targets per 100000 calls versus 8 for each fresh control; its positive control found 11 exact classes.",
                    "The instrument works, but MITM is not a live discovery arm."),
                new(RecursionCapabilities.FormalProofAttachment, RecursionVerdicts.Absent,
                    "The current surface is eml_proof_queue.tsv; no formal verifier has attached proof terms.",
                    "Proof feedback must record an explicit degraded skip."),
                new(RecursionCapabilities.AbsorptionSafeguards, RecursionVerdicts.Graduated,
                    "R5 added shadow execution, schema checks, deterministic audit, persisted policy receipts, and re-promotion control.",
                    "Learned takeover still requires each organ's emulation, adaptation, and forced re-promotion receipts."),
                new(RecursionCapabilities.TowerSubstrate, RecursionVerdicts.Pending,
                    "The infant receipt found 4 towers, maximum height 5, and deepest span 96 bytes.",
                    "R0 tower census v2 must prove scaling on the fattened trace corpus before grammar knots actuate.")
            ],
            Gates =
            [
                new(RecursionWaveArms.R1Branching, RecursionGateModes.Degraded,
                    "Procedure binding is graduated; branching may test whether predicates carry useful information.",
                    "Keep guards as inducible inert schema if guarded discovery does not beat the linear arm."),
                new(RecursionWaveArms.R2GrammarKnots, RecursionGateModes.Pending,
                    "R0 tower census v2 must demonstrate tower scaling and byte-exact reproduction.",
                    "Keep executable knots parked and audit trace barriers if scaling fails."),
                new(RecursionWaveArms.R2ProcedureRecursion, RecursionGateModes.Pending,
                    "R1 shuffled-guard null must establish causal guarded control flow.",
                    "Grammar knots may proceed independently; recursive procedure actuation remains disabled."),
                new(RecursionWaveArms.R2KnottedReplays, RecursionGateModes.Pending,
                    "Admitted grammar knots and stable criticality must both clear before dream actuation.",
                    "Retain finite tower compression without adding recursive dream diet."),
                new(RecursionWaveArms.R3Population, RecursionGateModes.Open,
                    "The graduated law store supplies tape-resident, behavior-verified claim identities.",
                    "If a later run has an empty law store, exchange sealed procedure packages under the same membrane contract."),
                new(RecursionWaveArms.R4WeftDomain, RecursionGateModes.Open,
                    "The generic runtime curriculum permits the Weft substrate mount independently of organ absorption.",
                    "Any Cortex-core edit after the generic probe seam is a named substrate gap."),
                new(RecursionWaveArms.R4ProofFeedback, RecursionGateModes.Degraded,
                    "Formal proof attachment is absent.",
                    "Skip proof feedback and bank no proof claim until a formal verifier attaches nonempty proof terms."),
                new(RecursionWaveArms.R5OrganAbsorption, RecursionGateModes.Degraded,
                    "Generic absorption safeguards are graduated after R5 infrastructure.",
                    "Keep Reflex authoritative until the target organ passes emulation, adaptation, and forced re-promotion."),
                new(RecursionWaveArms.R6Marathon, RecursionGateModes.Pending,
                    "Every preceding arm must be either graduated or banked under this authority before the matched marathon begins.",
                    "Arm only graduated mechanisms; Null, Hold, Absent, and Pending mechanisms remain disabled.")
            ]
        };
        string digest = ComputeDigest(authority);
        return new RecursionBranchAuthority
        {
            Digest = digest,
            Verdicts = authority.Verdicts,
            Gates = authority.Gates
        };
    }

    internal static byte[] Encode(RecursionBranchAuthority authority)
    {
        authority.Validate();
        string expectedDigest = ComputeDigest(authority);
        if (!string.Equals(authority.Digest, expectedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("branch authority digest does not match its verdicts and gates");

        byte[] first = EncodeDocument(authority, authority.Digest);
        byte[] second = EncodeDocument(authority, authority.Digest);
        if (!first.AsSpan().SequenceEqual(second))
            throw new InvalidDataException("branch authority RON encoding is nondeterministic");
        RecursionBranchAuthority restored = Decode(first);
        byte[] roundTrip = EncodeDocument(restored, restored.Digest);
        if (!first.AsSpan().SequenceEqual(roundTrip))
            throw new InvalidDataException("branch authority RON round-trip changed bytes");
        return first;
    }

    internal static RecursionBranchAuthority Decode(ReadOnlySpan<byte> bytes)
    {
        RecursionRONBranchAuthority document = RonSerializer.Deserialize<RecursionRONBranchAuthority>(bytes);
        if (document.schemaVersion != RecursionBranchAuthority.SchemaVersion)
            throw new InvalidDataException($"unsupported recursion branch authority schema {document.schemaVersion}");

        List<RecursionBranchVerdict> verdicts = new(document.verdicts.Count);
        for (int i = 0; i < document.verdicts.Count; i++)
        {
            RecursionRONBranchVerdict verdict = document.verdicts[i];
            verdicts.Add(new RecursionBranchVerdict(verdict.capability, verdict.verdict,
                RequireText(verdict.evidence, "branch evidence"), verdict.caveat ?? ""));
        }
        List<RecursionWaveGate> gates = new(document.gates.Count);
        for (int i = 0; i < document.gates.Count; i++)
        {
            RecursionRONWaveGate gate = document.gates[i];
            gates.Add(new RecursionWaveGate(gate.arm, gate.mode,
                RequireText(gate.requirement, "branch gate requirement"), gate.degradedPath ?? ""));
        }
        RecursionBranchAuthority authority = new()
        {
            Digest = RequireText(document.digest, "branch authority digest"),
            Verdicts = verdicts,
            Gates = gates
        };
        authority.Validate();
        string expectedDigest = ComputeDigest(authority);
        if (!string.Equals(authority.Digest, expectedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("decoded branch authority digest does not match its payload");
        return authority;
    }

    internal static void WriteArtifacts(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("output directory cannot be blank", nameof(outputDirectory));
        Directory.CreateDirectory(outputDirectory);
        RecursionBranchAuthority authority = CreateCurrent();
        File.WriteAllBytes(Path.Combine(outputDirectory, "recursion_branches.ron"), Encode(authority));
        File.WriteAllText(Path.Combine(outputDirectory, "recursion_branches.md"), RenderMarkdown(authority), new UTF8Encoding(false));
    }

    internal static string RenderMarkdown(RecursionBranchAuthority authority)
    {
        authority.Validate();
        StringBuilder report = new();
        report.AppendLine("# Recursion Branch Authority").AppendLine();
        report.Append("Authority digest: `").Append(authority.Digest).AppendLine("`").AppendLine();
        report.AppendLine("## Capability verdicts").AppendLine();
        report.AppendLine("| Capability | Verdict | Evidence | Caveat |");
        report.AppendLine("|---|---|---|---|");
        for (int i = 0; i < authority.Verdicts.Count; i++)
        {
            RecursionBranchVerdict verdict = authority.Verdicts[i];
            AppendMarkdownRow(report, verdict.Capability.ToString(), verdict.Verdict.ToString(), verdict.Evidence, verdict.Caveat);
        }
        report.AppendLine().AppendLine("## Downstream gates").AppendLine();
        report.AppendLine("| Arm | Mode | Requirement | Degraded path |");
        report.AppendLine("|---|---|---|---|");
        for (int i = 0; i < authority.Gates.Count; i++)
        {
            RecursionWaveGate gate = authority.Gates[i];
            AppendMarkdownRow(report, gate.Arm.ToString(), gate.Mode.ToString(), gate.Requirement, gate.DegradedPath);
        }
        return report.ToString();
    }

    private static string ComputeDigest(RecursionBranchAuthority authority)
        => Convert.ToHexStringLower(SHA256.HashData(EncodeDocument(authority, "")));

    private static byte[] EncodeDocument(RecursionBranchAuthority authority, string digest)
    {
        RecursionRONBranchAuthority document = new()
        {
            schemaVersion = RecursionBranchAuthority.SchemaVersion,
            digest = digest
        };
        for (int i = 0; i < authority.Verdicts.Count; i++)
        {
            RecursionBranchVerdict verdict = authority.Verdicts[i];
            document.verdicts.Add(new RecursionRONBranchVerdict
            {
                capability = verdict.Capability,
                verdict = verdict.Verdict,
                evidence = verdict.Evidence,
                caveat = verdict.Caveat
            });
        }
        for (int i = 0; i < authority.Gates.Count; i++)
        {
            RecursionWaveGate gate = authority.Gates[i];
            document.gates.Add(new RecursionRONWaveGate
            {
                arm = gate.Arm,
                mode = gate.Mode,
                requirement = gate.Requirement,
                degradedPath = gate.DegradedPath
            });
        }
        return RonSerializer.SerializeToUtf8(in document);
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"{label} cannot be blank") : value;

    private static void AppendMarkdownRow(StringBuilder report, string first, string second, string third, string fourth)
    {
        report.Append("| ").Append(EscapeMarkdown(first)).Append(" | ").Append(EscapeMarkdown(second)).Append(" | ")
            .Append(EscapeMarkdown(third)).Append(" | ").Append(EscapeMarkdown(fourth)).AppendLine(" |");
    }

    private static string EscapeMarkdown(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

[RonObject]
internal partial class RecursionRONBranchAuthority
{
    public int schemaVersion;
    public string digest = "";
    public List<RecursionRONBranchVerdict> verdicts = new();
    public List<RecursionRONWaveGate> gates = new();
}

[RonObject]
internal partial class RecursionRONBranchVerdict
{
    public RecursionCapabilities capability;
    public RecursionVerdicts verdict;
    public string evidence = "";
    public string caveat = "";
}

[RonObject]
internal partial class RecursionRONWaveGate
{
    public RecursionWaveArms arm;
    public RecursionGateModes mode;
    public string requirement = "";
    public string degradedPath = "";
}
