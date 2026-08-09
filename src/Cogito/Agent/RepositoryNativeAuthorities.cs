namespace Cogito;

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

/// The immutable native tool mount.  The order is executable grammar precedence:
/// changing it changes which mounted parser receives a generated line.
internal static class RepositoryNativeToolAuthority
{
    internal const string ArgumentSlot = "repo-argument";
    internal const string ParserContract = "ToolCall.Parse:v1;argument:trimmed-nonempty";

    private static readonly RepositoryNativeToolDescriptor[] OrderedDescriptors =
    [
        new(0, Tool.ToolVerbs.Grep, "grep", false),
        new(1, Tool.ToolVerbs.Open, "open", false),
        new(2, Tool.ToolVerbs.Read, "read", false),
        new(3, Tool.ToolVerbs.Ls, "ls", false),
        new(4, Tool.ToolVerbs.Verify, "verify", false),
        new(5, Tool.ToolVerbs.Answer, "answer", true),
    ];

    internal static IReadOnlyList<RepositoryNativeToolDescriptor> Descriptors { get; } =
        new ReadOnlyCollection<RepositoryNativeToolDescriptor>(OrderedDescriptors);

    internal static byte[] CanonicalBytes
    {
        get
        {
            Validate();
            List<byte> bytes = new(256);
            RepositoryNativeAuthorityCanonical.AppendText(bytes, "repository-native-tool-authority-v2");
            RepositoryNativeAuthorityCanonical.AppendText(bytes, ArgumentSlot);
            RepositoryNativeAuthorityCanonical.AppendText(bytes, ParserContract);
            RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)OrderedDescriptors.Length));
            foreach (RepositoryNativeToolDescriptor descriptor in OrderedDescriptors)
            {
                RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)descriptor.Ordinal));
                RepositoryNativeAuthorityCanonical.AppendU8(bytes, checked((byte)descriptor.Verb));
                RepositoryNativeAuthorityCanonical.AppendText(bytes, descriptor.Name);
                RepositoryNativeAuthorityCanonical.AppendU8(bytes, descriptor.IsTerminal ? (byte)1 : (byte)0);
                RepositoryNativeAuthorityCanonical.AppendText(bytes, descriptor.ArgumentSlot);
                RepositoryNativeAuthorityCanonical.AppendText(bytes, descriptor.ParserContract);
            }
            return bytes.ToArray();
        }
    }

    internal static string SHA256
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(CanonicalBytes));

    internal static bool Matches(Tool.ToolVerbs verb, string name, bool terminal)
        => OrderedDescriptors.Any(descriptor => descriptor.Verb == verb
            && descriptor.Name == name && descriptor.IsTerminal == terminal);

    internal static void Validate()
    {
        if (OrderedDescriptors.Length != 6
            || OrderedDescriptors.Select(static descriptor => descriptor.Ordinal).Distinct().Count() != OrderedDescriptors.Length
            || OrderedDescriptors.Select(static descriptor => descriptor.Name).Distinct(StringComparer.Ordinal).Count() != OrderedDescriptors.Length
            || OrderedDescriptors.Count(static descriptor => descriptor.IsTerminal) != 1)
            throw new InvalidDataException("native tool authority is not a unique ordered six-tool schema");

        for (int index = 0; index < OrderedDescriptors.Length; index++)
        {
            RepositoryNativeToolDescriptor descriptor = OrderedDescriptors[index];
            if (descriptor.Ordinal != index || !Enum.IsDefined(descriptor.Verb)
                || descriptor.ArgumentSlot != ArgumentSlot || descriptor.ParserContract != ParserContract
                || descriptor.Name.Length == 0 || descriptor.IsTerminal != (descriptor.Verb == Tool.ToolVerbs.Answer))
                throw new InvalidDataException("native tool authority descriptor diverges");

            Tool.ToolCall parsed = Tool.ToolCall.Parse($"{descriptor.Name}   native-argument");
            if (parsed.Verb != descriptor.Verb || parsed.Arg != "native-argument")
                throw new InvalidDataException("native tool authority parser contract diverges");
            Tool.ToolCall empty = Tool.ToolCall.Parse(descriptor.Name);
            if (empty.Verb != descriptor.Verb || empty.Arg.Length != 0)
                throw new InvalidDataException("native tool authority empty-argument contract diverges");
        }
    }
}

internal readonly record struct RepositoryNativeToolDescriptor(
    int Ordinal,
    Tool.ToolVerbs Verb,
    string Name,
    bool IsTerminal)
{
    internal string ArgumentSlot => RepositoryNativeToolAuthority.ArgumentSlot;
    internal string ParserContract => RepositoryNativeToolAuthority.ParserContract;
}

/// Semantic authority for the native repository policy.  Its canonical bytes bind
/// every pre-run policy input; dynamic frontier and canonical-state values remain
/// runtime state and are intentionally absent from this authority.
internal sealed class RepositoryNativePolicyAuthority
{
    private RepositoryNativePolicyAuthority(byte[] canonicalBytes)
    {
        _canonicalBytes = canonicalBytes.ToArray();
        SHA256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_canonicalBytes));
    }

    private readonly byte[] _canonicalBytes;
    internal ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    internal string SHA256 { get; }

    internal static RepositoryNativePolicyAuthority Create()
    {
        RepositoryNativeToolAuthority.Validate();
        IPolicyBoundaryDomain domain = RepositoryPolicyBoundaryDomain.Instance;
        domain.ArmTopology.Validate();
        domain.PolicyBinding.Validate();
        RepositoryCandidateSpecies[] speciesValues = Enum.GetValues<RepositoryCandidateSpecies>();
        RepositoryCandidateSpecies[] frozenSpecies =
        [
            RepositoryCandidateSpecies.SearchTerm,
            RepositoryCandidateSpecies.ListPrefix,
            RepositoryCandidateSpecies.OpenPath,
            RepositoryCandidateSpecies.ReadLocus,
            RepositoryCandidateSpecies.VerifyPrediction,
            RepositoryCandidateSpecies.AnswerPath,
        ];
        if (domain.Schema.ActionCount != frozenSpecies.Length
            || !speciesValues.AsSpan().SequenceEqual(frozenSpecies)
            || speciesValues.Select(static species => (byte)species).Distinct().Count() != speciesValues.Length)
            throw new InvalidDataException("native policy species schema is not the frozen six-action mapping");
        HashSet<int> actions = new();
        foreach (RepositoryCandidateSpecies species in speciesValues)
        {
            int action = RepositoryNative.Policy.Action(species);
            if ((uint)action >= (uint)domain.Schema.ActionCount || !actions.Add(action))
                throw new InvalidDataException("native policy species action mapping is not unique or bounded");
        }
        string bindingCanonical = domain.PolicyBinding.PolicyPacketSource;
        string bindingSHA256 = LoopClosureRegistration.ComputePolicyBindingSHA256(domain.PolicyID.Value, bindingCanonical);
        string schemaSHA256 = LoopClosureRegistration.ComputePolicyDomainSHA256(domain);
        string topologySHA256 = LoopClosureRegistration.ComputeArmTopologySHA256(domain);

        List<byte> bytes = new(1024);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, "repository-native-policy-authority-v2");
        RepositoryNativeAuthorityCanonical.AppendText(bytes, domain.PolicyID.Value);
        RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)domain.Schema.FeatureCount));
        RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)domain.Schema.ActionCount));
        RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)domain.Schema.OutcomeCount));
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.Schema.ModeCeiling);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.Schema.Admission);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, bindingCanonical);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, bindingSHA256);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, schemaSHA256);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.CanonicalStateKind);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.CanonicalScopeMode);
        RepositoryNativeAuthorityCanonical.AppendU16(bytes, domain.BoundaryFeatureID);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, topologySHA256);
        AppendArmTopology(bytes, domain.ArmTopology);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.SeedAuthority.CandidateAuthority);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.SeedAuthority.ForcedNullAuthority);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.SeedAuthority.CandidateSelectionCause);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)domain.SeedAuthority.ForcedNullSelectionCause);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, RepositoryNative.Policy.CanonicalStateFormulaVersion);
        RepositoryNativeAuthorityCanonical.AppendU16(bytes, RepositoryNative.Policy.CanonicalStateVersion);
        RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)Enum.GetValues<RepositoryCandidateSpecies>().Length));
        foreach (RepositoryCandidateSpecies species in Enum.GetValues<RepositoryCandidateSpecies>())
        {
            RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)species);
            RepositoryNativeAuthorityCanonical.AppendText(bytes, species.ToString());
            RepositoryNativeAuthorityCanonical.AppendU32(bytes, checked((uint)RepositoryNative.Policy.Action(species)));
        }
        return new RepositoryNativePolicyAuthority(bytes.ToArray());
    }

    internal void Validate()
    {
        RepositoryNativePolicyAuthority expected = Create();
        if (!CanonicalBytes.Span.SequenceEqual(expected.CanonicalBytes.Span) || SHA256 != expected.SHA256)
            throw new InvalidDataException("native policy authority diverges from the semantic domain");
    }

    private static void AppendArmTopology(List<byte> bytes, PolicyBoundaryArmTopology topology)
    {
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.LiveAuthorityCeiling);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.ControlAuthority);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.TrialAllocationAuthority);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.LiveProcessCatalog);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.ControlProcessCatalog);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.LiveRung0);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.ControlRung0);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.LiveDeliberation);
        RepositoryNativeAuthorityCanonical.AppendU8(bytes, (byte)topology.ControlDeliberation);
        RepositoryNativeAuthorityCanonical.AppendI64(bytes, topology.TrialArmSteps);
        RepositoryNativeAuthorityCanonical.AppendText(bytes, topology.TrialAllocationIdentity);
    }
}

internal static class RepositoryNativeAuthorityCanonical
{
    internal static void AppendText(List<byte> bytes, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        AppendU32(bytes, checked((uint)utf8.Length));
        bytes.AddRange(utf8);
    }

    internal static void AppendU8(List<byte> bytes, byte value) => bytes.Add(value);

    internal static void AppendU16(List<byte> bytes, ushort value)
    {
        Span<byte> scalar = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(scalar, value);
        bytes.AddRange(scalar.ToArray());
    }

    internal static void AppendU32(List<byte> bytes, uint value)
    {
        Span<byte> scalar = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(scalar, value);
        bytes.AddRange(scalar.ToArray());
    }

    internal static void AppendI64(List<byte> bytes, long value)
    {
        Span<byte> scalar = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(scalar, value);
        bytes.AddRange(scalar.ToArray());
    }
}
