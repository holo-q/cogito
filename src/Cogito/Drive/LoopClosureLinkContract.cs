namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// The causal links that close a paid divergence.  These are deliberately not
/// policy-trial counters: each link has its own evidence identity and its own
/// lifetime gate meter.
public enum LoopClosureLinkSpecies : byte
{
    PreferenceDivergence,
    InterventionDivergence,
    AuthorityEligible,
    BoundaryAdmitted,
    ExecutedDivergence,
}

internal static class LoopClosureLinkSpeciesWire
{
    internal static string Format(LoopClosureLinkSpecies species) => species switch
    {
        // Frozen wire token PreferenceDissent; identifier-side name is PreferenceDivergence.
        LoopClosureLinkSpecies.PreferenceDivergence => "PreferenceDissent",
        // Frozen wire token InterventionDissent; identifier-side name is InterventionDivergence.
        LoopClosureLinkSpecies.InterventionDivergence => "InterventionDissent",
        // Frozen wire token ExecutedDissent; identifier-side name is ExecutedDivergence.
        LoopClosureLinkSpecies.ExecutedDivergence => "ExecutedDissent",
        _ => species.ToString(),
    };

    internal static bool TryParse(string value, out LoopClosureLinkSpecies species)
    {
        // Frozen wire token PreferenceDissent; identifier-side name is PreferenceDivergence.
        if (value == "PreferenceDissent")
        {
            species = LoopClosureLinkSpecies.PreferenceDivergence;
            return true;
        }

        // Frozen wire token InterventionDissent; identifier-side name is InterventionDivergence.
        if (value == "InterventionDissent")
        {
            species = LoopClosureLinkSpecies.InterventionDivergence;
            return true;
        }

        // Frozen wire token ExecutedDissent; identifier-side name is ExecutedDivergence.
        if (value == "ExecutedDissent")
        {
            species = LoopClosureLinkSpecies.ExecutedDivergence;
            return true;
        }

        return Enum.TryParse(value, out species);
    }
}

public enum LoopClosureLinkPaths : byte
{
    Organic,
    Forced,
}

public enum LoopClosureLinkStates : byte
{
    Denied,
    Admitted,
}

public enum LoopClosureGateDenialReasons : byte
{
    NoOrganicOpportunity,
    ReflexAgreement,
    CandidateUnavailable,
    AuthorityNotEligible,
    BoundaryNotAdmitted,
    ExecutionNotReached,
    EvidenceMissing,
    MalformedEvidence,
}

/// The policy relation authenticated by loop-closure custody readers.  The packet
/// source is carried explicitly so a domain adapter can bind the same five-link
/// contract to another policy without teaching the custody code that policy's name.
public readonly record struct LoopClosurePolicyBinding
{
    public LoopClosurePolicyBinding(CortexPolicyID policyID, string policyPacketSource)
    {
        PolicyID = policyID;
        PolicyPacketSource = policyPacketSource ?? "";
        Validate();
    }

    public CortexPolicyID PolicyID { get; }
    public string PolicyPacketSource { get; }

    internal string OrganicComparisonPacketSource
        => PolicyPacketSource + ":organic-comparison";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PolicyID.Value)
            || !string.Equals(PolicyPacketSource, "policy:" + PolicyID.Value, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure policy binding source does not match its policy identity");
    }

    internal bool MatchesPolicy(CortexPolicyID policy)
        => PolicyID.Equals(policy);

    internal bool MatchesSource(string source)
        => string.Equals(PolicyPacketSource, source, StringComparison.Ordinal);
}

public readonly record struct LoopClosureGateDenial(
    LoopClosureGateDenialReasons Reason,
    long Count)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Reason) || Count <= 0)
            throw new InvalidDataException("loop-closure gate denial carries an invalid typed reason or count");
    }
}

public readonly record struct LoopClosureGateLiveness(
    LoopClosureLinkSpecies Species,
    long Reached,
    long Admitted,
    long Denied,
    IReadOnlyList<LoopClosureGateDenial> DenialReasons,
    LoopClosureDigest MeterSHA256)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Species) || Reached < 0 || Admitted < 0 || Denied < 0 || Admitted > Reached || Denied > Reached
            || Reached != checked(Admitted + Denied) || !MeterSHA256.IsValid)
            throw new InvalidDataException("loop-closure gate liveness is not a conserved lifetime meter");
        if (DenialReasons is null || DenialReasons.Any(static denial => !Enum.IsDefined(denial.Reason)))
            throw new InvalidDataException("loop-closure gate liveness omits typed denial reasons");
        LoopClosureGateDenial[] reasons = DenialReasons.ToArray();
        foreach (LoopClosureGateDenial reason in reasons)
        {
            reason.Validate();
            if (!IsAllowedDenialReason(Species, reason.Reason))
                throw new InvalidDataException("loop-closure gate liveness carries a denial reason from another species");
        }
        if (reasons.GroupBy(static denial => denial.Reason).Any(static group => group.Count() != 1)
            || reasons.Length != reasons.Select(static denial => denial.Reason).Distinct().Count())
            throw new InvalidDataException("loop-closure gate liveness carries duplicate denial reasons");
        long deniedByReason = reasons.Sum(static denial => denial.Count);
        if (deniedByReason != Denied)
            throw new InvalidDataException("loop-closure gate liveness denial reasons do not conserve denied count");
        if (!string.Equals(MeterSHA256.Value, DigestMeter(Species, Reached, Admitted, Denied, reasons).Value, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure gate liveness digest does not match its lifetime meter");
    }

    public static LoopClosureGateLiveness Create(
        LoopClosureLinkSpecies species,
        long reached,
        long admitted,
        long denied,
        IReadOnlyList<LoopClosureGateDenial> denialReasons)
    {
        LoopClosureGateDenial[] reasons = denialReasons?.ToArray() ?? throw new ArgumentNullException(nameof(denialReasons));
        LoopClosureDigest digest = DigestMeter(species, reached, admitted, denied, reasons);
        LoopClosureGateLiveness meter = new(species, reached, admitted, denied, reasons, digest);
        meter.Validate();
        return meter;
    }

    internal static LoopClosureDigest DigestMeter(
        LoopClosureLinkSpecies species,
        long reached,
        long admitted,
        long denied,
        IReadOnlyList<LoopClosureGateDenial> reasons)
    {
        string canonical = string.Join('|', "loop-closure-gate-liveness-v1", LoopClosureLinkSpeciesWire.Format(species), reached, admitted, denied,
            string.Join(',', reasons.OrderBy(static denial => denial.Reason).Select(static denial => $"{denial.Reason}:{denial.Count}")));
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    internal static bool IsAllowedDenialReason(LoopClosureLinkSpecies species, LoopClosureGateDenialReasons reason)
        => reason is LoopClosureGateDenialReasons.EvidenceMissing or LoopClosureGateDenialReasons.MalformedEvidence
            || (species, reason) switch
            {
                (LoopClosureLinkSpecies.PreferenceDivergence, LoopClosureGateDenialReasons.NoOrganicOpportunity) => true,
                (LoopClosureLinkSpecies.PreferenceDivergence, LoopClosureGateDenialReasons.CandidateUnavailable) => true,
                (LoopClosureLinkSpecies.PreferenceDivergence, LoopClosureGateDenialReasons.ReflexAgreement) => true,
                (LoopClosureLinkSpecies.InterventionDivergence, LoopClosureGateDenialReasons.CandidateUnavailable) => true,
                (LoopClosureLinkSpecies.AuthorityEligible, LoopClosureGateDenialReasons.AuthorityNotEligible) => true,
                (LoopClosureLinkSpecies.BoundaryAdmitted, LoopClosureGateDenialReasons.BoundaryNotAdmitted) => true,
                (LoopClosureLinkSpecies.ExecutedDivergence, LoopClosureGateDenialReasons.ExecutionNotReached) => true,
                _ => false,
            };
}

public readonly record struct LoopClosureLinkReceipt(
    LoopClosureLinkSpecies Species,
    LoopClosureLinkPaths Path,
    LoopClosureLinkStates State,
    LoopClosureDigest EvidenceSHA256,
    LoopClosureDigest PredecessorEvidenceSHA256,
    long EvidenceEventID,
    LoopClosureChildOutcomeReference ChildOutcome = default)
{
    public void Validate(LoopClosureLinkSpecies expectedSpecies, LoopClosureLinkSpecies? predecessor)
    {
        if (Species != expectedSpecies || !Enum.IsDefined(Path) || !Enum.IsDefined(State) || !EvidenceSHA256.IsValid || EvidenceEventID < 0)
            throw new InvalidDataException("loop-closure link receipt is malformed or species-drifted");
        if (predecessor is null)
        {
            if (!string.IsNullOrEmpty(PredecessorEvidenceSHA256.Value))
                throw new InvalidDataException("loop-closure preference link unexpectedly carries a predecessor");
        }
        else if (!PredecessorEvidenceSHA256.IsValid)
            throw new InvalidDataException("loop-closure link receipt omits its predecessor evidence identity");
        ChildOutcome.Validate(Species == LoopClosureLinkSpecies.ExecutedDivergence && State == LoopClosureLinkStates.Admitted);
    }
}

/// A report-level causal contract. Generic domains use a reached prefix. The
/// repository-native domain also admits a deliberate organic gap: preference
/// divergence may remain at zero while the forced chain starts at intervention.
public sealed class LoopClosureLinkContract
{
    public static IReadOnlyList<LoopClosureLinkSpecies> OrderedSpecies { get; } = Array.AsReadOnly(new[]
    {
        LoopClosureLinkSpecies.PreferenceDivergence,
        LoopClosureLinkSpecies.InterventionDivergence,
        LoopClosureLinkSpecies.AuthorityEligible,
        LoopClosureLinkSpecies.BoundaryAdmitted,
        LoopClosureLinkSpecies.ExecutedDivergence,
    });

    public LoopClosureLinkContract(
        IReadOnlyList<LoopClosureLinkReceipt> receipts,
        IReadOnlyList<LoopClosureGateLiveness> liveness,
        bool allowOrganicGap = false)
    {
        Receipts = receipts?.ToArray() ?? throw new ArgumentNullException(nameof(receipts));
        Liveness = liveness?.ToArray() ?? throw new ArgumentNullException(nameof(liveness));
        AllowOrganicGap = allowOrganicGap || (Receipts.Count == OrderedSpecies.Count - 1
            && Liveness.Count == OrderedSpecies.Count
            && Receipts.Count > 0 && Receipts[0].Species == LoopClosureLinkSpecies.InterventionDivergence);
    }

    public IReadOnlyList<LoopClosureLinkReceipt> Receipts { get; }
    public IReadOnlyList<LoopClosureGateLiveness> Liveness { get; }
    public bool AllowOrganicGap { get; }
    public bool IsComplete => (Receipts.Count == OrderedSpecies.Count && Liveness.Count == OrderedSpecies.Count)
        || (AllowOrganicGap && Receipts.Count == OrderedSpecies.Count - 1 && Liveness.Count == OrderedSpecies.Count);

    public void Validate(bool requireComplete)
    {
        bool organicGap = AllowOrganicGap && Receipts.Count == OrderedSpecies.Count - 1
            && Liveness.Count == OrderedSpecies.Count
            && Receipts.Count > 0 && Receipts[0].Species == LoopClosureLinkSpecies.InterventionDivergence;
        if (!organicGap && Receipts.Count != Liveness.Count)
            throw new InvalidDataException("loop-closure link receipts and liveness meters have different counts");
        if (Receipts.Count == 0 || Liveness.Count == 0)
            throw new InvalidDataException("loop-closure report carries an empty typed link contract");
        int receiptOffset = organicGap ? 1 : 0;
        int receiptCount = Receipts.Count;
        int expectedCount = organicGap ? OrderedSpecies.Count : receiptCount;
        if (expectedCount > OrderedSpecies.Count || (requireComplete && !IsComplete))
            throw new InvalidDataException("loop-closure report does not carry the required typed link chain");
        IReadOnlyList<LoopClosureLinkSpecies> expectedReceipts = OrderedSpecies.Skip(receiptOffset).Take(receiptCount).ToArray();
        IReadOnlyList<LoopClosureLinkSpecies> expectedLiveness = OrderedSpecies.Take(organicGap ? OrderedSpecies.Count : receiptCount).ToArray();
        if (!Receipts.Select(static receipt => receipt.Species).SequenceEqual(expectedReceipts)
            || !Liveness.Select(static meter => meter.Species).SequenceEqual(expectedLiveness))
            throw new InvalidDataException("loop-closure link species are not the canonical typed chain");
        if (organicGap && (Liveness[0].Reached != 0 || Liveness[0].Admitted != 0 || Liveness[0].Denied != 0))
            throw new InvalidDataException("repository organic preference gap carries nonzero liveness");
        for (int index = 0; index < receiptCount; index++)
        {
            int speciesIndex = index + receiptOffset;
            LoopClosureLinkSpecies species = OrderedSpecies[speciesIndex];
            LoopClosureLinkSpecies? predecessor = speciesIndex == 0 ? null : OrderedSpecies[speciesIndex - 1];
            Receipts[index].Validate(species, predecessor);
            Liveness[speciesIndex].Validate();
            LoopClosureLinkPaths expectedPath = speciesIndex == 0 ? LoopClosureLinkPaths.Organic : LoopClosureLinkPaths.Forced;
            if (Receipts[index].Path != expectedPath)
                throw new InvalidDataException("loop-closure link path does not match its species");
            if (Receipts[index].State == LoopClosureLinkStates.Admitted && Liveness[speciesIndex].Admitted == 0)
                throw new InvalidDataException("loop-closure admitted link has no lifetime admission");
            if (Receipts[index].State == LoopClosureLinkStates.Denied && Liveness[speciesIndex].Denied == 0)
                throw new InvalidDataException("loop-closure denied link has no lifetime denial");
            if (speciesIndex > receiptOffset && Receipts[index].PredecessorEvidenceSHA256 != Receipts[index - 1].EvidenceSHA256)
                throw new InvalidDataException("loop-closure link predecessor evidence is not the preceding typed link");
        }
        for (int index = 0; index < Liveness.Count; index++) Liveness[index].Validate();

        if (IsComplete)
        {
            // Organic preference divergence may show zero admissions. The forced
            // execution path is the non-negotiable power receipt for closure.
            if (Receipts[^1].State != LoopClosureLinkStates.Admitted || Liveness[4].Admitted <= 0)
                throw new InvalidDataException("loop-closure executed divergence is not powered by the forced path");
        }
    }

    internal static bool VerifyRonFixture()
    {
        LoopClosureDigest[] evidence = Enumerable.Range(0, OrderedSpecies.Count)
            .Select(index => new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"link-{index}"))))).ToArray();
        LoopClosureGateLiveness[] meters =
        [
            LoopClosureGateLiveness.Create(OrderedSpecies[0], 3, 0, 3,
                [new(LoopClosureGateDenialReasons.NoOrganicOpportunity, 1),
                 new(LoopClosureGateDenialReasons.ReflexAgreement, 1),
                 new(LoopClosureGateDenialReasons.CandidateUnavailable, 1)]),
            LoopClosureGateLiveness.Create(OrderedSpecies[1], 1, 1, 0, []),
            LoopClosureGateLiveness.Create(OrderedSpecies[2], 1, 1, 0, []),
            LoopClosureGateLiveness.Create(OrderedSpecies[3], 1, 1, 0, []),
            LoopClosureGateLiveness.Create(OrderedSpecies[4], 1, 1, 0, []),
        ];
        LoopClosureChildOutcomeReference fixtureChild = new("fixture-child", "children/fixture-child",
            new LoopClosureDigest(new string('a', 64)), new LoopClosureDigest(new string('b', 64)),
            new CortexPolicyDecisionID(9001), new TapeEventID(9002), new LoopClosureDigest(new string('c', 64)), true);
        LoopClosureLinkReceipt[] receipts = OrderedSpecies.Select((species, index) =>
            new LoopClosureLinkReceipt(species, index == 0 ? LoopClosureLinkPaths.Organic : LoopClosureLinkPaths.Forced,
                index == 0 ? LoopClosureLinkStates.Denied : LoopClosureLinkStates.Admitted, evidence[index],
                index == 0 ? default : evidence[index - 1], index + 10,
                index == OrderedSpecies.Count - 1 ? fixtureChild : default)).ToArray();
        LoopClosureLinkContract contract = new(receipts, meters);
        contract.Validate(requireComplete: true);
        LoopClosureLinkContractRON encoded = EncodeRon(contract);
        byte[] first = RonSerializer.SerializeToUtf8(in encoded);
        byte[] second = RonSerializer.SerializeToUtf8(in encoded);
        LoopClosureLinkContract restored = DecodeRon(first);
        restored.Validate(requireComplete: true);
        LoopClosureLinkReceipt[] shuffled = receipts.ToArray();
        (shuffled[1], shuffled[2]) = (shuffled[2], shuffled[1]);
        bool shuffleRejected = Rejects(() => new LoopClosureLinkContract(shuffled, meters).Validate(requireComplete: true));
        LoopClosureGateLiveness forged = meters[4] with { Admitted = 0, Denied = 1, DenialReasons = [new(LoopClosureGateDenialReasons.ExecutionNotReached, 1)] };
        bool forcedRejected = Rejects(() => new LoopClosureLinkContract(receipts, [.. meters[..4], forged]).Validate(requireComplete: true));
        LoopClosureLinkContract preferenceOnly = new(receipts[..1], meters[..1]);
        preferenceOnly.Validate(requireComplete: false);
        LoopClosureLinkContractRON partialEncoded = EncodeRon(preferenceOnly);
        byte[] partialFirst = RonSerializer.SerializeToUtf8(in partialEncoded);
        byte[] partialSecond = RonSerializer.SerializeToUtf8(in partialEncoded);
        LoopClosureLinkContract partialRestored = DecodeRon(partialFirst);
        partialRestored.Validate(requireComplete: false);
        bool preferenceOnlyRoundTrip = partialRestored.Receipts.Count == 1
            && partialFirst.AsSpan().SequenceEqual(partialSecond);
        bool holeRejected = Rejects(() => new LoopClosureLinkContract(
            [receipts[0], receipts[2]], [meters[0], meters[2]]).Validate(requireComplete: false));
        bool shuffledPrefixRejected = Rejects(() => new LoopClosureLinkContract(
            [receipts[1], receipts[0]], [meters[1], meters[0]]).Validate(requireComplete: false));
        bool mismatchedCountRejected = Rejects(() => new LoopClosureLinkContract(
            receipts[..1], meters).Validate(requireComplete: false));
        LoopClosureLinkReceipt predecessorMismatch = receipts[1] with { PredecessorEvidenceSHA256 = evidence[2] };
        bool predecessorRejected = Rejects(() => new LoopClosureLinkContract(
            [receipts[0], predecessorMismatch], meters[..2]).Validate(requireComplete: false));
        LoopClosureGateLiveness badLiveness = meters[0] with { Admitted = 1 };
        bool livenessRejected = Rejects(() => new LoopClosureLinkContract(
            receipts[..1], [badLiveness]).Validate(requireComplete: false));
        return contract.IsComplete && first.AsSpan().SequenceEqual(second) && restored.IsComplete && shuffleRejected && forcedRejected
            && preferenceOnlyRoundTrip && holeRejected && shuffledPrefixRejected && mismatchedCountRejected
            && predecessorRejected && livenessRejected;
    }

    private static LoopClosureLinkContractRON EncodeRon(LoopClosureLinkContract contract)
        => new()
        {
            linkReceipts = contract.Receipts.Select(static receipt => new LoopClosureLinkReceiptRON
            {
                species = LoopClosureLinkSpeciesWire.Format(receipt.Species), path = receipt.Path.ToString(), state = receipt.State.ToString(),
                evidenceSHA256 = receipt.EvidenceSHA256.Value, predecessorEvidenceSHA256 = receipt.PredecessorEvidenceSHA256.Value,
                evidenceEventID = receipt.EvidenceEventID,
                childOutcomeRunID = receipt.ChildOutcome.RunID, childOutcomeRelativePath = receipt.ChildOutcome.RelativePath,
                childOutcomeAuthoritySHA256 = receipt.ChildOutcome.AuthoritySHA256.Value,
                childOutcomeRailSHA256 = receipt.ChildOutcome.RailSHA256.Value,
                childOutcomeForcedDecisionID = receipt.ChildOutcome.ForcedDecisionID.Value,
                childOutcomeEventID = receipt.ChildOutcome.OutcomeEventID.Value,
                childOutcomePayloadSHA256 = receipt.ChildOutcome.OutcomePayloadSHA256.Value,
                childOutcomeBeforeSeal = receipt.ChildOutcome.BeforeSeal,
            }).ToList(),
            gateLiveness = contract.Liveness.Select(static meter => new LoopClosureGateLivenessRON
            {
                species = LoopClosureLinkSpeciesWire.Format(meter.Species), reached = meter.Reached, admitted = meter.Admitted, denied = meter.Denied,
                meterSHA256 = meter.MeterSHA256.Value,
                denialReasons = meter.DenialReasons.Select(static denial => new LoopClosureGateDenialRON { reason = denial.Reason.ToString(), count = denial.Count }).ToList(),
            }).ToList(),
        };

    private static LoopClosureLinkContract DecodeRon(ReadOnlySpan<byte> bytes)
    {
        LoopClosureLinkContractRON document = RonSerializer.Deserialize<LoopClosureLinkContractRON>(bytes);
        return new LoopClosureLinkContract(
            document.linkReceipts.Select(static receipt => new LoopClosureLinkReceipt(
                ParseSpecies(receipt.species), Parse<LoopClosureLinkPaths>(receipt.path), Parse<LoopClosureLinkStates>(receipt.state),
                new(receipt.evidenceSHA256), new(receipt.predecessorEvidenceSHA256), receipt.evidenceEventID,
                new LoopClosureChildOutcomeReference(receipt.childOutcomeRunID, receipt.childOutcomeRelativePath,
                    new(receipt.childOutcomeAuthoritySHA256), new(receipt.childOutcomeRailSHA256),
                    new CortexPolicyDecisionID(receipt.childOutcomeForcedDecisionID), new TapeEventID(receipt.childOutcomeEventID),
                    new(receipt.childOutcomePayloadSHA256), receipt.childOutcomeBeforeSeal))).ToArray(),
            document.gateLiveness.Select(static meter => new LoopClosureGateLiveness(
                ParseSpecies(meter.species), meter.reached, meter.admitted, meter.denied,
                meter.denialReasons.Select(static denial => new LoopClosureGateDenial(Parse<LoopClosureGateDenialReasons>(denial.reason), denial.count)).ToArray(),
                new(meter.meterSHA256))).ToArray());
    }

    private static T Parse<T>(string value) where T : struct, Enum
        => Enum.TryParse(value, out T result) ? result : throw new InvalidDataException($"loop-closure contract fixture carries unknown {typeof(T).Name}");

    private static LoopClosureLinkSpecies ParseSpecies(string value)
        => LoopClosureLinkSpeciesWire.TryParse(value, out LoopClosureLinkSpecies species)
            ? species
            : throw new InvalidDataException("loop-closure contract fixture carries unknown LoopClosureLinkSpecies");

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}

[RonObject]
internal partial class LoopClosureLinkContractRON
{
    public List<LoopClosureLinkReceiptRON> linkReceipts = new();
    public List<LoopClosureGateLivenessRON> gateLiveness = new();
}
