using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Ronmamon;

namespace Cogito;

/// Boundary semantics used only by the policy-neutral training fixtures. The
/// fixture still registers a real domain so generic Cortex code never falls
/// back to an ambient Homeostat policy when it mounts an obligation.
internal sealed class HomeostatTrainingPolicyBoundaryDomain : IPolicyBoundaryDomain
{
    private readonly LoopClosurePolicyBinding _policyBinding;

    internal HomeostatTrainingPolicyBoundaryDomain(CortexPolicySchema schema)
    {
        Schema = schema;
        PolicyID = schema.Policy;
        _policyBinding = new LoopClosurePolicyBinding(PolicyID, "policy:" + PolicyID.Value);
        _policyBinding.Validate();
    }

    public CortexPolicyID PolicyID { get; }
    public CortexPolicySchema Schema { get; }
    public PolicyCanonicalStateKinds CanonicalStateKind => PolicyCanonicalStateKinds.Homeostat;
    public PolicyCanonicalScopeModes CanonicalScopeMode => PolicyCanonicalScopeModes.None;
    public PolicyCanonicalStateID[] CanonicalStates => [];
    public ushort BoundaryFeatureID => 0;
    public PolicyBoundaryArmTopology ArmTopology { get; } = new(
        CortexPolicyAuthorities.Grammar,
        CortexPolicyAuthorities.Launchpad,
        CortexPolicyAuthorities.Grammar,
        EmlProcessCatalogs.Full,
        EmlProcessCatalogs.NegativeLog,
        EmlRung0Modes.Armed,
        EmlRung0Modes.Disabled,
        EmlDeliberationModes.Adaptive,
        EmlDeliberationModes.Frozen,
        1024,
        "paired-homeostat-boundary-v1");
    public PolicyBoundarySeedAuthorityInputs SeedAuthority { get; } = new(
        CortexPolicyAuthorities.Grammar,
        CortexPolicyAuthorities.Grammar,
        CortexPolicySelectionCauses.GrammarCandidate,
        CortexPolicySelectionCauses.TrialOverride);
    public LoopClosurePolicyBinding PolicyBinding => _policyBinding;

    public bool ValidateCanonicalState(in PolicyCanonicalStateID state)
        => state.IsValidFor(PolicyID) && state.Kind == CanonicalStateKind;

    public bool ValidateCandidateTransport(
        string candidateCanonical,
        ulong candidateDigest,
        ulong frontierRevision,
        string frontierAuthoritySHA256)
        => string.IsNullOrEmpty(candidateCanonical)
            && candidateDigest == 0
            && frontierRevision == 0
            && string.IsNullOrEmpty(frontierAuthoritySHA256);

    public bool TryResolveForcedCandidateAction(string candidateCanonical, ulong candidateDigest, out int action)
    {
        action = -1;
        return false;
    }

    public bool ValidateActionRelation(
        CortexPolicySelectionCauses cause,
        int launchpadAction,
        int rawCandidateAction,
        int selectedCandidateAction,
        int executedAction)
        => HomeostatPolicyBoundaryDomain.ValidateActionRelationCore(
            cause, launchpadAction, rawCandidateAction, selectedCandidateAction, executedAction);

    public bool ValidateExecutionAuthority(
        CortexPolicyAuthorities authority,
        CortexPolicySelectionCauses cause,
        bool requireGrammar = false)
        => HomeostatPolicyBoundaryDomain.ValidateExecutionAuthorityCore(authority, cause, requireGrammar);

    public LoopClosureDigest ComputeOutcomeIdentity(
        in PolicyBoundaryForkReceipt receipt,
        in PolicyBoundaryArmReceipt arm,
        int action)
        => HomeostatPolicyBoundaryDomain.ComputeOutcomeIdentityCore(in receipt, in arm, action);

    public bool TryVerifyR4(
        Cortex cortex,
        in CortexPolicyDecision decision,
        out TapeEventID decisionEventID,
        out LoopClosureR4Provenance provenance)
    {
        decisionEventID = new TapeEventID(-1);
        provenance = default;
        if (!decision.Policy.Equals(PolicyID)
            || !decision.ReadoutContext.IsCanonical
            || decision.ReadoutContext.ActionCount != Schema.ActionCount
            || !ValidateCanonicalState(decision.ReadoutContext.CanonicalState)
            || !PolicyBinding.PolicyID.Equals(PolicyID)) return false;
        if (!cortex.TryCreatePolicyReadoutCustody(in decision, out decisionEventID, out provenance)
            || !provenance.Training.Policy.Equals(PolicyID)
            || !provenance.Training.CanonicalState.Policy.Equals(PolicyID))
        {
            decisionEventID = new TapeEventID(-1);
            provenance = default;
            return false;
        }
        return true;
    }
}

/// The immutable hand-off from a calibration child to its sibling evaluation child.
///
/// This receipt carries the *decision provenance* needed to re-admit a trained
/// boundary. It deliberately does not carry Cortex policy runtime state: an
/// evaluation child loads the cold checkpoint and mounts this receipt as a new
/// boundary authority.
[RonObject]
public partial class PolicyBoundaryTrainingReceipt
{
    public int schemaVersion = 6;
    public string parentRunID = "";
    public string sourceChildID = "";
    public string coldSeedDigest = "";
    public int trainingStartStep;
    public int trainingEndStep;
    public string configReceiptDigest = "";
    public string checkpointReceiptDigest = "";
    public string forkReceiptDigest = "";
    public string forkAuthorityDigest = "";
    public PolicyBoundaryTrainingForkReceipt? forkAuthority;
    public string obligation = "";
    public string policy = "";
    public string feature = "";
    public PolicyBoundaryComparisons comparison = PolicyBoundaryComparisons.Unknown;
    public string boundary = "";
    public ulong sourceDecisionReadoutFingerprint;
    public ulong sourceDecisionCandidateFingerprint;
    public ulong sourceDecisionReadoutRevision;
    public ulong fundingDecisionID;
    public string contentDigest = "";
    public bool verifiedReceipt;
    public bool verifiedContent;
    public string receiptDigest = "";

    public int SchemaVersion => schemaVersion;
    public string ParentRunID => parentRunID;
    public string SourceChildID => sourceChildID;
    public string ColdSeedDigest => coldSeedDigest;
    public int TrainingStartStep => trainingStartStep;
    public int TrainingEndStep => trainingEndStep;
    public string ConfigReceiptDigest => configReceiptDigest;
    public string CheckpointReceiptDigest => checkpointReceiptDigest;
    public string ForkReceiptDigest => forkReceiptDigest;
    public string ForkAuthorityDigest => forkAuthorityDigest;
    public PolicyBoundaryTrainingForkReceipt? ForkAuthority => forkAuthority;
    public string Obligation => obligation;
    public string Policy => policy;
    public string Feature => feature;
    public PolicyBoundaryComparisons Comparison => comparison;
    public string Boundary => boundary;
    public ulong SourceDecisionReadoutFingerprint => sourceDecisionReadoutFingerprint;
    public ulong SourceDecisionCandidateFingerprint => sourceDecisionCandidateFingerprint;
    public ulong SourceDecisionReadoutRevision => sourceDecisionReadoutRevision;
    public string ContentDigest => contentDigest;
    public bool VerifiedReceipt => verifiedReceipt;
    public bool VerifiedContent => verifiedContent;
    public string ReceiptDigest => receiptDigest;

    public string ComputeContentDigest() => Digest(CanonicalContent());

    public string ComputeDigest() => Digest(string.Join('|',
        schemaVersion, CanonicalContent(), contentDigest, verifiedReceipt ? 1 : 0,
        verifiedContent ? 1 : 0, "policy-boundary-training-v6"));

    public void Validate(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (schemaVersion != 6) throw new InvalidDataException("policy-boundary training receipt schema is unsupported");
        RequireID(parentRunID, nameof(parentRunID));
        RequireID(sourceChildID, nameof(sourceChildID));
        RequireDigest(coldSeedDigest, nameof(coldSeedDigest));
        if (trainingStartStep < 0 || trainingEndStep < trainingStartStep)
            throw new InvalidDataException("policy-boundary training horizon is invalid");
        RequireDigest(configReceiptDigest, nameof(configReceiptDigest));
        RequireDigest(checkpointReceiptDigest, nameof(checkpointReceiptDigest));
        RequireDigest(forkReceiptDigest, nameof(forkReceiptDigest));
        RequireDigest(forkAuthorityDigest, nameof(forkAuthorityDigest));
        if (forkAuthority is null) throw new InvalidDataException("policy-boundary training receipt is missing its fork authority");
        PolicyBoundaryForkReceipt authority = forkAuthority.ToDomain();
        if (!string.Equals(policy, domain.PolicyID.Value, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary training receipt addresses a different policy domain");
        authority.Validate(domain);
        if (authority.Obligation.Value != obligation || !string.Equals(boundary, authority.CandidateBoundary.ToString(), StringComparison.Ordinal)
            || !string.Equals(forkReceiptDigest, PolicyBoundaryObligation.ComputeReceiptDigest(in authority), StringComparison.Ordinal)
            || !string.Equals(PolicyBoundaryObligation.ComputeReceiptDigest(in authority), forkAuthorityDigest, StringComparison.Ordinal)
            || sourceDecisionReadoutFingerprint != authority.SourceDecisionReadoutFingerprint
            || sourceDecisionCandidateFingerprint != authority.SourceDecisionCandidateFingerprint
            || sourceDecisionReadoutRevision != authority.SourceDecisionReadoutRevision)
            throw new InvalidDataException("policy-boundary training fork authority binding mismatch");
        RequireID(obligation, nameof(obligation));
        RequireID(policy, nameof(policy));
        RequireID(feature, nameof(feature));
        if (!Enum.IsDefined(comparison) || comparison == PolicyBoundaryComparisons.Unknown)
            throw new InvalidDataException("policy-boundary training comparison is unsupported");
        _ = PolicyBoundaryRational.Parse(boundary);
        if (sourceDecisionReadoutFingerprint == 0 || sourceDecisionCandidateFingerprint == 0 || sourceDecisionReadoutRevision == 0)
            throw new InvalidDataException("policy-boundary training receipt requires a nonzero source decision readout");
        if (fundingDecisionID == 0 || fundingDecisionID != authority.QuotaDecisionID.Value)
            throw new InvalidDataException("policy-boundary training receipt funding identity disagrees with its fork authority");
        if (!string.Equals(contentDigest, ComputeContentDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary training receipt content digest mismatch");
        if (!verifiedReceipt || !verifiedContent)
            throw new InvalidDataException("policy-boundary training receipt is not verified");
        if (!string.Equals(receiptDigest, ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary training receipt digest mismatch");
    }

    public bool IsVerified(IPolicyBoundaryDomain domain)
    {
        try { Validate(domain); return true; }
        catch (InvalidDataException) { return false; }
    }

    internal static PolicyBoundaryTrainingReceipt CreateVerified(
        string parentRunID, string sourceChildID, string coldSeedDigest,
        int trainingStartStep, int trainingEndStep,
        string configReceiptDigest, string checkpointReceiptDigest, string forkReceiptDigest,
        string obligation, string policy, PolicyBoundaryRational boundary,
        ulong sourceDecisionReadoutFingerprint, ulong sourceDecisionReadoutRevision,
        in PolicyBoundaryForkReceipt authority, string feature, PolicyBoundaryComparisons comparison,
        IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        authority.Validate(domain);
        if (authority.Obligation.Value != obligation || authority.CandidateBoundary != boundary)
            throw new InvalidDataException("training authority does not match the policy boundary identity");
        if (sourceDecisionReadoutFingerprint != authority.SourceDecisionReadoutFingerprint
            || sourceDecisionReadoutRevision != authority.SourceDecisionReadoutRevision
            || authority.SourceDecisionCandidateFingerprint == 0)
            throw new InvalidDataException("training authority does not carry the exact split readout/candidate identity");
        PolicyBoundaryTrainingForkReceipt authorityImage = PolicyBoundaryTrainingForkReceipt.FromDomain(in authority);
        string authorityDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
        if (!string.Equals(forkReceiptDigest, authorityDigest, StringComparison.Ordinal))
            throw new InvalidDataException("training fork receipt digest does not match the verified authority");
        PolicyBoundaryTrainingReceipt receipt = new()
        {
            parentRunID = parentRunID,
            sourceChildID = sourceChildID,
            coldSeedDigest = coldSeedDigest,
            trainingStartStep = trainingStartStep,
            trainingEndStep = trainingEndStep,
            configReceiptDigest = configReceiptDigest,
            checkpointReceiptDigest = checkpointReceiptDigest,
            forkReceiptDigest = forkReceiptDigest,
            forkAuthorityDigest = authorityDigest,
            forkAuthority = authorityImage,
            obligation = obligation,
            policy = policy,
            feature = feature,
            comparison = comparison,
            boundary = boundary.ToString(),
            sourceDecisionReadoutFingerprint = sourceDecisionReadoutFingerprint,
            sourceDecisionCandidateFingerprint = authority.SourceDecisionCandidateFingerprint,
            sourceDecisionReadoutRevision = sourceDecisionReadoutRevision,
            fundingDecisionID = authority.QuotaDecisionID.Value,
            verifiedReceipt = true,
            verifiedContent = true,
        };
        receipt.contentDigest = receipt.ComputeContentDigest();
        receipt.receiptDigest = receipt.ComputeDigest();
        receipt.Validate(domain);
        return receipt;
    }

    internal static PolicyBoundaryTrainingReceipt CreateFromCalibration(
        int trainingStartStep, int trainingEndStep,
        in Cortex.PolicyBoundaryAuthorityReceipt sourceAuthority,
        IPolicyBoundaryDomain domain)
    {
        if (!sourceAuthority.Verified || sourceAuthority.ForkReceiptDigest.Length == 0
            || !string.Equals(sourceAuthority.ReceiptDigest, sourceAuthority.ComputeDigest(), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(sourceAuthority.ParentRunID)
            || string.IsNullOrWhiteSpace(sourceAuthority.SourceChildID)
            || sourceAuthority.ColdSeedDigest.Length != 64
            || sourceAuthority.ConfigReceiptDigest.Length != 64
            || sourceAuthority.CheckpointReceiptDigest.Length != 64
            || sourceAuthority.DecisionReadoutFingerprint == 0 || sourceAuthority.DecisionReadoutRevision == 0)
            throw new InvalidDataException("calibration source readout is not exact");
        PolicyBoundaryForkReceipt authority = sourceAuthority.ForkReceipt;
        ArgumentNullException.ThrowIfNull(domain);
        authority.Validate(domain);
        string authorityDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
        if (!string.Equals(sourceAuthority.ForkReceiptDigest, authorityDigest, StringComparison.Ordinal)
            || !string.Equals(sourceAuthority.Obligation, authority.Obligation.Value, StringComparison.Ordinal)
            || !string.Equals(sourceAuthority.Boundary, authority.CandidateBoundary.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("calibration source authority is internally inconsistent");
        return CreateVerified(sourceAuthority.ParentRunID, sourceAuthority.SourceChildID, sourceAuthority.ColdSeedDigest, trainingStartStep, trainingEndStep,
            sourceAuthority.ConfigReceiptDigest, sourceAuthority.CheckpointReceiptDigest,
            sourceAuthority.ForkReceiptDigest, sourceAuthority.Obligation, sourceAuthority.Policy,
            authority.CandidateBoundary, sourceAuthority.DecisionReadoutFingerprint, sourceAuthority.DecisionReadoutRevision,
            in authority, sourceAuthority.Feature, sourceAuthority.Comparison, domain);
    }

    public static byte[] Encode(in PolicyBoundaryTrainingReceipt receipt, IPolicyBoundaryDomain domain)
    {
        receipt.Validate(domain);
        return RonSerializer.SerializeToUtf8(in receipt);
    }

    public static PolicyBoundaryTrainingReceipt Decode(ReadOnlySpan<byte> bytes, IPolicyBoundaryDomain domain)
    {
        PolicyBoundaryTrainingReceipt receipt = RonSerializer.Deserialize<PolicyBoundaryTrainingReceipt>(bytes);
        receipt.Validate(domain);
        return receipt;
    }

    private string CanonicalContent() => string.Join('|',
        parentRunID, sourceChildID, coldSeedDigest, trainingStartStep, trainingEndStep,
        configReceiptDigest, checkpointReceiptDigest, forkReceiptDigest, forkAuthorityDigest,
        obligation, policy, feature, comparison, boundary, sourceDecisionReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture),
        sourceDecisionCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), sourceDecisionReadoutRevision, fundingDecisionID);

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void RequireID(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('|', StringComparison.Ordinal))
            throw new InvalidDataException($"policy-boundary training receipt {name} is invalid");
    }

    private static void RequireDigest(string value, string name)
    {
        if (value.Length != 64 || value.Any(static c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException($"policy-boundary training receipt {name} is not a SHA-256 digest");
    }
}

/// Proof that one verified training receipt was mounted into a distinct evaluation child.
[Flags]
public enum PolicyBoundaryMountRelations : byte
{
    OfflineCalibrationToColdEvaluation = 1,
    OfflineCalibrationToColdEvaluationAfterHandshake = 2,
}

[RonObject]
public partial class PolicyBoundaryMountReceipt
{
    public int schemaVersion = 1;
    public string parentRunID = "";
    public string sourceChildID = "";
    public string destinationChildID = "";
    public string coldSeedDigest = "";
    public string trainingReceiptDigest = "";
    public string sourceContentDigest = "";
    public PolicyBoundaryMountRelations relation = PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluation;
    public int evaluationStartStep;
    public int evaluationEndStep;
    public int mountStep;
    public ulong destinationDecisionReadoutFingerprint;
    public ulong destinationDecisionReadoutRevision;
    // Empty on historical mounts; populated on the current post-handshake dialect.
    public string destinationHandshakeReceiptDigest = "";
    public ulong destinationHandshakeDecisionID;
    public bool verifiedReceipt;
    public bool verifiedContent;
    public string receiptDigest = "";

    public int SchemaVersion => schemaVersion;
    public string ParentRunID => parentRunID;
    public string SourceChildID => sourceChildID;
    public string DestinationChildID => destinationChildID;
    public string ColdSeedDigest => coldSeedDigest;
    public string TrainingReceiptDigest => trainingReceiptDigest;
    public string SourceContentDigest => sourceContentDigest;
    public PolicyBoundaryMountRelations Relation => relation;
    public int EvaluationStartStep => evaluationStartStep;
    public int EvaluationEndStep => evaluationEndStep;
    public int MountStep => mountStep;
    public ulong DestinationDecisionReadoutFingerprint => destinationDecisionReadoutFingerprint;
    public ulong DestinationDecisionReadoutRevision => destinationDecisionReadoutRevision;
    public string DestinationHandshakeReceiptDigest => destinationHandshakeReceiptDigest;
    public ulong DestinationHandshakeDecisionID => destinationHandshakeDecisionID;
    public bool VerifiedReceipt => verifiedReceipt;
    public bool VerifiedContent => verifiedContent;
    public string ReceiptDigest => receiptDigest;

    public string ComputeDigest()
    {
        string canonical = string.Join('|',
            schemaVersion, parentRunID, sourceChildID, destinationChildID, coldSeedDigest,
            trainingReceiptDigest, sourceContentDigest, relation, evaluationStartStep, evaluationEndStep, mountStep,
            destinationDecisionReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), destinationDecisionReadoutRevision,
            verifiedReceipt ? 1 : 0, verifiedContent ? 1 : 0);
        if (schemaVersion >= 2)
        {
            canonical += "|" + destinationHandshakeReceiptDigest;
            canonical += "|" + destinationHandshakeDecisionID.ToString(CultureInfo.InvariantCulture);
            canonical += "|policy-boundary-mount-v2";
        }
        else
        {
            // Read the short-lived v1 handshake dialect emitted before the owner-ID field was added.
            if (!string.IsNullOrEmpty(destinationHandshakeReceiptDigest)) canonical += "|" + destinationHandshakeReceiptDigest;
            canonical += "|policy-boundary-mount-v1";
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal void ValidateForEmission()
    {
        if (schemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(parentRunID) || string.IsNullOrWhiteSpace(sourceChildID)
            || string.IsNullOrWhiteSpace(destinationChildID) || string.Equals(sourceChildID, destinationChildID, StringComparison.Ordinal)
            || coldSeedDigest.Length != 64 || trainingReceiptDigest.Length != 64 || sourceContentDigest.Length != 64
            || !IsSupportedRelation()
            || evaluationStartStep < 0 || evaluationEndStep < evaluationStartStep
            || !HasValidMountBoundary()
            || !verifiedReceipt || !verifiedContent || !string.Equals(receiptDigest, ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount receipt cannot be emitted");
        if (schemaVersion == 2 && (relation != PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake
            || string.IsNullOrWhiteSpace(destinationHandshakeReceiptDigest) || destinationHandshakeReceiptDigest.Length != 64
            || destinationHandshakeDecisionID == 0))
            throw new InvalidDataException("current post-handshake mount owner identity is incomplete");
    }

    public void Validate(in PolicyBoundaryTrainingReceipt training, string expectedParentID,
        string expectedDestinationID, string expectedColdSeedDigest, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (schemaVersion is not (1 or 2)) throw new InvalidDataException("policy-boundary mount receipt schema is unsupported");
        if (!string.Equals(parentRunID, expectedParentID, StringComparison.Ordinal)
            || !string.Equals(parentRunID, training.ParentRunID, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount parent mismatch");
        if (string.Equals(sourceChildID, destinationChildID, StringComparison.Ordinal)
            || !string.Equals(sourceChildID, training.SourceChildID, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(destinationChildID))
            throw new InvalidDataException("policy-boundary mount source and destination must be distinct children");
        if (!string.Equals(coldSeedDigest, expectedColdSeedDigest, StringComparison.Ordinal)
            || !string.Equals(coldSeedDigest, training.ColdSeedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount cold seed mismatch");
        if (!training.IsVerified(domain) || !string.Equals(training.ReceiptDigest, trainingReceiptDigest, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount does not bind a verified training receipt");
        if (!string.Equals(sourceContentDigest, training.ContentDigest, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount source content mismatch");
        if (!IsSupportedRelation())
            throw new InvalidDataException("policy-boundary mount relation is unsupported");
        if (evaluationStartStep < 0 || evaluationEndStep < evaluationStartStep || !HasValidMountBoundary())
            throw new InvalidDataException("policy-boundary evaluation horizon or mount step is invalid");
        if (!verifiedReceipt || !verifiedContent || !string.Equals(receiptDigest, ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount receipt is not verified");
        if (relation == PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake
            && (destinationDecisionReadoutFingerprint == 0 || destinationDecisionReadoutRevision == 0))
            throw new InvalidDataException("post-handshake policy-boundary mount is missing the destination step-zero decision readout");
        if (schemaVersion == 2 && (relation != PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake
            || string.IsNullOrWhiteSpace(destinationHandshakeReceiptDigest) || destinationHandshakeReceiptDigest.Length != 64
            || destinationHandshakeDecisionID == 0))
            throw new InvalidDataException("current post-handshake mount is missing its owner receipt identity");
    }

    public static PolicyBoundaryMountReceipt CreateVerified(
        in PolicyBoundaryTrainingReceipt training, string destinationChildID,
        int evaluationStartStep, int evaluationEndStep, int mountStep,
        IPolicyBoundaryDomain domain,
        PolicyBoundaryMountRelations relation = PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluation)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!training.IsVerified(domain)) throw new InvalidDataException("cannot mount an unverified training receipt");
        PolicyBoundaryMountReceipt receipt = new()
        {
            parentRunID = training.ParentRunID,
            sourceChildID = training.SourceChildID,
            destinationChildID = destinationChildID,
            coldSeedDigest = training.ColdSeedDigest,
            trainingReceiptDigest = training.ReceiptDigest,
            sourceContentDigest = training.ContentDigest,
            relation = relation,
            evaluationStartStep = evaluationStartStep,
            evaluationEndStep = evaluationEndStep,
            mountStep = mountStep,
            verifiedReceipt = true,
            verifiedContent = true,
        };
        receipt.receiptDigest = receipt.ComputeDigest();
        receipt.Validate(in training, training.ParentRunID, destinationChildID, training.ColdSeedDigest, domain);
        return receipt;
    }

    internal static PolicyBoundaryMountReceipt CreateVerifiedAfterHandshake(
        in PolicyBoundaryTrainingReceipt training, string destinationChildID,
        int evaluationStartStep, int evaluationEndStep, int mountStep,
        ulong destinationReadoutFingerprint, ulong destinationReadoutRevision,
        in HomeostatDestinationHandshakeReceipt handshake, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (evaluationStartStep < 1 || mountStep != evaluationStartStep - 1)
            throw new InvalidDataException("post-handshake mount boundary is invalid");
        if (!training.IsVerified(domain)) throw new InvalidDataException("cannot mount an unverified training receipt");
        handshake.Validate();
        if (destinationReadoutFingerprint != handshake.readoutFingerprint
            || destinationReadoutRevision != handshake.grammarRevision)
            throw new InvalidDataException("post-handshake mount readout does not match its owner receipt");
        PolicyBoundaryMountReceipt receipt = new()
        {
            schemaVersion = 2,
            parentRunID = training.ParentRunID,
            sourceChildID = training.SourceChildID,
            destinationChildID = destinationChildID,
            coldSeedDigest = training.ColdSeedDigest,
            trainingReceiptDigest = training.ReceiptDigest,
            sourceContentDigest = training.ContentDigest,
            relation = PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake,
            evaluationStartStep = evaluationStartStep,
            evaluationEndStep = evaluationEndStep,
            mountStep = mountStep,
            destinationDecisionReadoutFingerprint = destinationReadoutFingerprint,
            destinationDecisionReadoutRevision = destinationReadoutRevision,
            destinationHandshakeReceiptDigest = handshake.ReceiptDigest,
            destinationHandshakeDecisionID = handshake.DecisionID,
            verifiedReceipt = true,
            verifiedContent = true,
        };
        receipt.receiptDigest = receipt.ComputeDigest();
        receipt.Validate(in training, training.ParentRunID, destinationChildID, training.ColdSeedDigest, domain);
        return receipt;
    }

    private bool IsSupportedRelation()
        => relation is PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluation
            or PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake;

    private bool HasValidMountBoundary()
        => relation == PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluation
            ? mountStep >= evaluationStartStep && mountStep <= evaluationEndStep
            : evaluationStartStep >= 1 && mountStep == evaluationStartStep - 1;

    internal PolicyBoundaryMountReceipt WithDestinationReadout(ulong fingerprint, ulong revision)
    {
        PolicyBoundaryMountReceipt updated = new()
        {
            schemaVersion = schemaVersion,
            parentRunID = parentRunID,
            sourceChildID = sourceChildID,
            destinationChildID = destinationChildID,
            coldSeedDigest = coldSeedDigest,
            trainingReceiptDigest = trainingReceiptDigest,
            sourceContentDigest = sourceContentDigest,
            relation = relation,
            evaluationStartStep = evaluationStartStep,
            evaluationEndStep = evaluationEndStep,
            mountStep = mountStep,
            verifiedReceipt = verifiedReceipt,
            verifiedContent = verifiedContent,
            destinationDecisionReadoutFingerprint = fingerprint,
            destinationDecisionReadoutRevision = revision,
            destinationHandshakeReceiptDigest = destinationHandshakeReceiptDigest,
            destinationHandshakeDecisionID = destinationHandshakeDecisionID,
        };
        updated.receiptDigest = updated.ComputeDigest();
        return updated;
    }

    internal PolicyBoundaryMountReceipt WithDestinationHandshake(in HomeostatDestinationHandshakeReceipt handshake)
    {
        handshake.Validate();
        if (destinationDecisionReadoutFingerprint != handshake.readoutFingerprint
            || destinationDecisionReadoutRevision != handshake.grammarRevision)
            throw new InvalidDataException("destination mount readout does not match its owner receipt");
        PolicyBoundaryMountReceipt updated = new()
        {
            schemaVersion = 2,
            parentRunID = parentRunID,
            sourceChildID = sourceChildID,
            destinationChildID = destinationChildID,
            coldSeedDigest = coldSeedDigest,
            trainingReceiptDigest = trainingReceiptDigest,
            sourceContentDigest = sourceContentDigest,
            relation = PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake,
            evaluationStartStep = evaluationStartStep,
            evaluationEndStep = evaluationEndStep,
            mountStep = mountStep,
            destinationDecisionReadoutFingerprint = destinationDecisionReadoutFingerprint,
            destinationDecisionReadoutRevision = destinationDecisionReadoutRevision,
            destinationHandshakeReceiptDigest = handshake.ReceiptDigest,
            destinationHandshakeDecisionID = handshake.decisionID,
            verifiedReceipt = verifiedReceipt,
            verifiedContent = verifiedContent,
        };
        updated.receiptDigest = updated.ComputeDigest();
        return updated;
    }

    public static byte[] Encode(in PolicyBoundaryMountReceipt receipt, in PolicyBoundaryTrainingReceipt training, IPolicyBoundaryDomain domain)
    {
        receipt.Validate(in training, training.ParentRunID, receipt.destinationChildID, training.ColdSeedDigest, domain);
        return RonSerializer.SerializeToUtf8(in receipt);
    }

    public static PolicyBoundaryMountReceipt Decode(ReadOnlySpan<byte> bytes, in PolicyBoundaryTrainingReceipt training,
        string expectedParentID, string expectedDestinationID, string expectedColdSeedDigest, IPolicyBoundaryDomain domain)
    {
        PolicyBoundaryMountReceipt receipt = RonSerializer.Deserialize<PolicyBoundaryMountReceipt>(bytes);
        receipt.Validate(in training, expectedParentID, expectedDestinationID, expectedColdSeedDigest, domain);
        return receipt;
    }

    internal static PolicyBoundaryMountReceipt Decode(ReadOnlySpan<byte> bytes, in PolicyBoundaryTrainingReceipt training, IPolicyBoundaryDomain domain)
    {
        PolicyBoundaryMountReceipt receipt = RonSerializer.Deserialize<PolicyBoundaryMountReceipt>(bytes);
        receipt.Validate(in training, training.ParentRunID, receipt.DestinationChildID, training.ColdSeedDigest, domain);
        return receipt;
    }
}

[RonObject]
// Frozen RON field names retain witness/dissent vocabulary; identifier-side names use Corroboration/Divergence.
public partial class PolicyBoundaryTrainingForkReceipt
{
    public int schemaVersion = 4;
    public string obligation = "";
    public string baselineBoundary = "0";
    public string candidateBoundary = "0";
    public List<int> horizons = [];
    public List<PolicyBoundaryTrainingArmReceipt> arms = [];
    public bool continuityExact;
    public bool matchedSpend;
    public bool forcedNullBehaviorExecuted;
    public bool forcedNullDiverged;
    public bool verified;
    public ulong sourceDecisionReadoutFingerprint;
    public ulong sourceDecisionCandidateFingerprint;
    public ulong sourceDecisionReadoutRevision;
    public ulong fundingDecisionID;
    public string executionWitnessSHA256 = "";
    public string executionTrainingWitnessSHA256 = "";
    public ulong executionQuotaReadoutFingerprint;
    public ulong executionQuotaCandidateFingerprint;
    public ulong executionFundingCandidateRevision;
    public string executionForkArmSHA256 = "";
    public string executionChildReceiptSHA256 = "";
    public ulong executionDissentDecisionID;
    public string executionDissentOutcomeID = "";
    public long executionDissentOutcomeEventID;
    public string executionDissentOutcomePayloadSHA256 = "";

    internal PolicyBoundaryForkReceipt ToDomain()
    {
        if (schemaVersion != 4)
            throw new InvalidDataException("policy-boundary training fork receipt schema is unsupported");
        PolicyBoundaryArmReceipt[] rows = arms.Select(static arm => arm.ToDomain()).ToArray();
        PaidDivergenceExecutionCorroboration? execution = null;
        bool hasExecutionCorroboration = !string.IsNullOrWhiteSpace(executionWitnessSHA256);
        bool hasAnyExecutionField = hasExecutionCorroboration
            || !string.IsNullOrEmpty(executionTrainingWitnessSHA256)
            || executionQuotaReadoutFingerprint != 0 || executionQuotaCandidateFingerprint != 0
            || executionFundingCandidateRevision != 0 || !string.IsNullOrEmpty(executionForkArmSHA256)
            || !string.IsNullOrEmpty(executionChildReceiptSHA256) || executionDissentDecisionID != 0
            || !string.IsNullOrEmpty(executionDissentOutcomeID) || executionDissentOutcomeEventID != 0
            || !string.IsNullOrEmpty(executionDissentOutcomePayloadSHA256);
        if (!hasExecutionCorroboration && hasAnyExecutionField)
            throw new InvalidDataException("policy-boundary training fork receipt carries stray execution custody");
        if (hasExecutionCorroboration)
            execution = new PaidDivergenceExecutionCorroboration(
                new LoopClosureDigest(executionTrainingWitnessSHA256), new CortexPolicyQuotaDecisionID(fundingDecisionID),
                executionQuotaReadoutFingerprint, executionQuotaCandidateFingerprint, new GrammarRevisionID(executionFundingCandidateRevision),
                new LoopClosureDigest(executionForkArmSHA256), new LoopClosureDigest(executionChildReceiptSHA256),
                new CortexPolicyDecisionID(executionDissentDecisionID), new LoopClosureDigest(executionDissentOutcomeID),
                new LoopClosureDigest(executionWitnessSHA256), new TapeEventID(executionDissentOutcomeEventID),
                executionDissentOutcomePayloadSHA256);
        return new(new PolicyBoundaryObligationID(obligation), PolicyBoundaryRational.Parse(baselineBoundary),
            PolicyBoundaryRational.Parse(candidateBoundary), horizons.ToArray(), rows,
            continuityExact, matchedSpend, forcedNullBehaviorExecuted, verified,
            sourceDecisionReadoutFingerprint, sourceDecisionReadoutRevision, null, execution)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(fundingDecisionID),
            SourceDecisionCandidateFingerprint = sourceDecisionCandidateFingerprint,
        };
    }

    internal static PolicyBoundaryTrainingForkReceipt FromDomain(in PolicyBoundaryForkReceipt source)
    {
        PolicyBoundaryTrainingForkReceipt value = new() { schemaVersion = 4, obligation = source.Obligation.Value, baselineBoundary = source.BaselineBoundary.ToString(), candidateBoundary = source.CandidateBoundary.ToString(), horizons = [.. source.Horizons], continuityExact = source.ContinuityExact, matchedSpend = source.MatchedSpend, forcedNullBehaviorExecuted = source.ForcedNullBehaviorExecuted, forcedNullDiverged = source.ForcedNullDiverged, verified = source.Verified, sourceDecisionReadoutFingerprint = source.SourceDecisionReadoutFingerprint, sourceDecisionCandidateFingerprint = source.SourceDecisionCandidateFingerprint, sourceDecisionReadoutRevision = source.SourceDecisionReadoutRevision, fundingDecisionID = source.QuotaDecisionID.Value };
        if (source.ExecutionCorroboration is PaidDivergenceExecutionCorroboration execution)
        {
            value.executionWitnessSHA256 = execution.PaidDivergenceExecutionCorroborationSHA256.Value;
            value.executionTrainingWitnessSHA256 = execution.ReadoutTrainingCorroborationSHA256.Value;
            value.executionQuotaReadoutFingerprint = execution.QuotaReadoutFingerprint;
            value.executionQuotaCandidateFingerprint = execution.QuotaCandidateFingerprint;
            value.executionFundingCandidateRevision = execution.FundingCandidateRevision.Value;
            value.executionForkArmSHA256 = execution.ForkArmSHA256.Value;
            value.executionChildReceiptSHA256 = execution.ChildExecutionReceiptSHA256.Value;
            value.executionDissentDecisionID = execution.ExecutedDivergenceDecisionID.Value;
            value.executionDissentOutcomeID = execution.ExecutedDivergenceOutcomeID.Value;
            value.executionDissentOutcomeEventID = execution.ExecutedDivergenceOutcomeEventID.Value;
            value.executionDissentOutcomePayloadSHA256 = execution.ExecutedDivergenceOutcomePayloadSHA256;
        }
        value.arms = [.. source.Arms.Select(static arm => new PolicyBoundaryTrainingArmReceipt { arm = arm.Arm, horizon = arm.Horizon, paidCloseDelta = arm.PaidCloseDelta, matchedSpend = arm.MatchedSpend, continuityExact = arm.ContinuityExact, childProcessCompleted = arm.ChildProcessCompleted, behaviorallyExecuted = arm.BehaviorallyExecuted, diverged = arm.Diverged, grammarExecutionsDelta = arm.GrammarExecutionsDelta, trialAdaptationTransitions = arm.TrialAdaptationTransitions, adaptationEnabled = arm.AdaptationEnabled, executionOutcome = arm.ExecutionOutcome, requestCount = arm.RequestCount, guardAdmittedCount = arm.GuardAdmittedCount, lastRequestDecisionID = arm.LastRequestDecisionID.Value, lastRequestStep = arm.LastRequestStep, lastRequestLaunchpadAction = arm.LastRequestReadout.LaunchpadAction, lastRequestRawCandidateAction = arm.LastRequestReadout.RawCandidateAction, lastRequestSelectedCandidateAction = arm.LastRequestReadout.SelectedCandidateAction, lastRequestExecutedAction = arm.LastRequestReadout.ExecutedAction, lastRequestAuthority = arm.LastRequestReadout.Authority, lastRequestRevision = arm.LastRequestReadout.GrammarRevision.Value, lastRequestSelectionCause = arm.LastRequestReadout.SelectionCause, lastRequestSupportDigest = arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest, lastRequestCandidateFingerprint = arm.LastRequestReadout.ReadoutCandidateFingerprint, executedDecisionID = arm.ExecutedDecisionID.Value, executedStep = arm.ExecutedStep, executedLaunchpadAction = arm.ExecutedLaunchpadAction, executedRawCandidateAction = arm.ExecutedRawCandidateAction, executedSelectedCandidateAction = arm.ExecutedSelectedCandidateAction, executedAction = arm.ExecutedAction, executedAuthority = arm.ExecutedAuthority, executedSelectionCause = arm.ExecutedSelectionCause, executedReadoutFingerprint = arm.ExecutedReadoutFingerprint, executedReadoutRevision = arm.ExecutedReadoutRevision, executedReadoutOccurrenceDigest = arm.ExecutedReadoutOccurrenceDigest, executedCandidateFingerprint = arm.ExecutedCandidateFingerprint, executedCanonicalPolicy = arm.ExecutedCanonicalState.Version == 0 ? "" : arm.ExecutedCanonicalState.Policy.Value, executedCanonicalKind = arm.ExecutedCanonicalState.Version == 0 ? default : arm.ExecutedCanonicalState.Kind, executedCanonicalVersion = arm.ExecutedCanonicalState.Version, executedCanonicalValue = arm.ExecutedCanonicalState.Value, executedDecisionEventID = arm.ExecutedDecisionEventID.Value, executedOutcomeEventID = arm.ExecutedOutcomeEventID.Value, executedOutcomePayloadSHA256 = arm.ExecutedOutcomePayloadSHA256, forcedDivergenceSeed = arm.ForcedDivergenceSeed })];
        return value;
    }
}

[RonObject]
public partial class PolicyBoundaryTrainingArmReceipt
{
    public PolicyBoundaryArms arm;
    public int horizon;
    public long paidCloseDelta;
    public long matchedSpend;
    public bool continuityExact;
    [RonAlias("powered")]
    public bool childProcessCompleted;
    public bool behaviorallyExecuted;
    public bool diverged;
    public long grammarExecutionsDelta;
    public long trialAdaptationTransitions;
    public bool adaptationEnabled;
    public CortexPolicyTrialExecutionOutcomes executionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
    public long requestCount;
    public long guardAdmittedCount;
    public ulong lastRequestDecisionID;
    public int lastRequestStep = -1;
    public int lastRequestLaunchpadAction = -1;
    public int lastRequestRawCandidateAction = -1;
    public int lastRequestSelectedCandidateAction = -1;
    public int lastRequestExecutedAction = -1;
    public CortexPolicyAuthorities lastRequestAuthority = CortexPolicyAuthorities.Launchpad;
    public ulong lastRequestRevision;
    public CortexPolicySelectionCauses lastRequestSelectionCause = CortexPolicySelectionCauses.Launchpad;
    public ulong lastRequestSupportDigest;
    public ulong lastRequestCandidateFingerprint;
    public ulong executedDecisionID;
    public int executedStep = -1;
    public int executedLaunchpadAction = -1;
    public int executedRawCandidateAction = -1;
    public int executedSelectedCandidateAction = -1;
    public int executedAction = -1;
    public CortexPolicyAuthorities executedAuthority = CortexPolicyAuthorities.Launchpad;
    public CortexPolicySelectionCauses executedSelectionCause = CortexPolicySelectionCauses.Launchpad;
    public ulong executedReadoutFingerprint;
    public ulong executedReadoutRevision;
    public ulong executedReadoutOccurrenceDigest;
    public ulong executedCandidateFingerprint;
    public string executedCanonicalPolicy = "";
    public PolicyCanonicalStateKinds executedCanonicalKind;
    public ushort executedCanonicalVersion;
    public ulong executedCanonicalValue;
    public long executedDecisionEventID;
    public long executedOutcomeEventID;
    public string executedOutcomePayloadSHA256 = "";
    public ulong forcedDivergenceSeed;

    internal PolicyBoundaryArmReceipt ToDomain()
    {
        PolicyBoundaryArmReceipt value = new(arm, horizon, paidCloseDelta, matchedSpend, continuityExact, childProcessCompleted, grammarExecutionsDelta, trialAdaptationTransitions, adaptationEnabled)
    {
        ExecutionOutcome = executionOutcome,
        RequestCount = requestCount,
        GuardAdmittedCount = guardAdmittedCount,
        LastRequestDecisionID = new(lastRequestDecisionID),
        LastRequestStep = lastRequestStep,
        LastRequestReadout = new(lastRequestLaunchpadAction, lastRequestRawCandidateAction, lastRequestSelectedCandidateAction, lastRequestExecutedAction,
            lastRequestAuthority, new GrammarRevisionID(lastRequestRevision), lastRequestSelectionCause, lastRequestSupportDigest, lastRequestCandidateFingerprint),
        ExecutedDecisionID = new(executedDecisionID),
        ExecutedStep = executedStep,
        ExecutedLaunchpadAction = executedLaunchpadAction,
        ExecutedRawCandidateAction = executedRawCandidateAction,
        ExecutedSelectedCandidateAction = executedSelectedCandidateAction,
        ExecutedAction = executedAction,
        ExecutedAuthority = executedAuthority,
        ExecutedSelectionCause = executedSelectionCause,
        ExecutedReadoutFingerprint = executedReadoutFingerprint,
        ExecutedReadoutRevision = executedReadoutRevision,
        ExecutedReadoutOccurrenceDigest = executedReadoutOccurrenceDigest,
        ExecutedCandidateFingerprint = executedCandidateFingerprint,
        ExecutedCanonicalState = executedCanonicalVersion == 0
            ? default
            : new PolicyCanonicalStateID(new CortexPolicyID(executedCanonicalPolicy), executedCanonicalKind, executedCanonicalVersion, executedCanonicalValue),
        ExecutedDecisionEventID = new(executedDecisionEventID),
        ExecutedOutcomeEventID = new(executedOutcomeEventID),
        ExecutedOutcomePayloadSHA256 = executedOutcomePayloadSHA256,
        ForcedDivergenceSeed = forcedDivergenceSeed,
        Diverged = diverged,
    };
        return value;
    }
}

internal static class PolicyBoundaryTrainingMountFixture
{
    private const ulong FixtureCandidateFingerprint = 0xB002UL;

    private static PolicyCanonicalStateID FixtureCanonicalState
    {
        get
        {
            HomeostatPolicyContext context = HomeostatPolicyContext.ParseToken("c:rx,w:0,g:0");
            return PolicyCanonicalStates.Homeostat(Homeostat.PolicyID, in context);
        }
    }

    private static ulong ReadoutFingerprint(ulong revision)
        => GrammarPolicyReadout.ComputeFingerprint(new GrammarRevisionID(revision), Homeostat.PolicyID);

    private static PolicyBoundaryArmReceipt[] StampFixtureArms(PolicyBoundaryArmReceipt[] arms, ulong fingerprint, ulong revision)
        => [.. arms.Select(arm => arm with
        {
            ExecutedDecisionID = new CortexPolicyDecisionID((ulong)(arm.Horizon * 10 + (int)arm.Arm + 1)),
            ExecutedStep = arm.Horizon,
            ExecutedLaunchpadAction = 0,
            ExecutedRawCandidateAction = arm.Arm == PolicyBoundaryArms.Baseline ? -1 : 1,
            ExecutedSelectedCandidateAction = arm.Arm == PolicyBoundaryArms.Baseline ? -1 : arm.Arm == PolicyBoundaryArms.Candidate ? 2 : arm.Arm == PolicyBoundaryArms.ForcedDivergentNull ? 3 : 1,
            ExecutedAction = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate => 2,
                PolicyBoundaryArms.ForcedDivergentNull => 3,
                _ => 0,
            },
            ExecutedAuthority = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull => CortexPolicyAuthorities.Grammar,
                PolicyBoundaryArms.ReflexFrozenControl => CortexPolicyAuthorities.Shadow,
                _ => CortexPolicyAuthorities.Launchpad,
            },
            ExecutedSelectionCause = arm.Arm switch
            {
                PolicyBoundaryArms.Candidate => CortexPolicySelectionCauses.GrammarCandidate,
                PolicyBoundaryArms.ForcedDivergentNull => CortexPolicySelectionCauses.TrialOverride,
                PolicyBoundaryArms.ReflexFrozenControl => CortexPolicySelectionCauses.ShadowCandidate,
                _ => CortexPolicySelectionCauses.Launchpad,
            },
            ExecutedReadoutFingerprint = fingerprint,
            ExecutedReadoutRevision = revision,
            ExecutedReadoutOccurrenceDigest = arm.Arm == PolicyBoundaryArms.Baseline ? 0UL : 0xA001UL + (ulong)arm.Arm,
            ExecutedCandidateFingerprint = arm.Arm == PolicyBoundaryArms.Baseline ? 0UL : 0xB001UL + (ulong)arm.Arm,
            ExecutedCanonicalState = FixtureCanonicalState,
            ExecutedDecisionEventID = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                ? new TapeEventID((long)(arm.Horizon * 100 + 7)) : default,
            ExecutedOutcomeEventID = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                ? new TapeEventID((long)(arm.Horizon * 100 + 8)) : default,
            ExecutedOutcomePayloadSHA256 = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                ? new string('c', 64) : "",
            ForcedDivergenceSeed = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull
                ? 0xF00D0000UL + (ulong)arm.Horizon : 0UL,
            RequestCount = 1,
            GuardAdmittedCount = 1,
            LastRequestDecisionID = new CortexPolicyDecisionID((ulong)(arm.Horizon * 10 + (int)arm.Arm + 1)),
            LastRequestStep = arm.Horizon,
            LastRequestReadout = new(
                0,
                arm.Arm == PolicyBoundaryArms.Baseline ? -1 : 1,
                arm.Arm == PolicyBoundaryArms.Baseline ? -1 : arm.Arm == PolicyBoundaryArms.Candidate ? 2 : arm.Arm == PolicyBoundaryArms.ForcedDivergentNull ? 3 : 1,
                arm.Arm switch
                {
                    PolicyBoundaryArms.Candidate => 2,
                    PolicyBoundaryArms.ForcedDivergentNull => 3,
                    _ => 0,
                },
                arm.Arm switch
                {
                    PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull => CortexPolicyAuthorities.Grammar,
                    PolicyBoundaryArms.ReflexFrozenControl => CortexPolicyAuthorities.Shadow,
                    _ => CortexPolicyAuthorities.Launchpad,
                },
                new GrammarRevisionID(revision),
                arm.Arm switch
                {
                    PolicyBoundaryArms.Candidate => CortexPolicySelectionCauses.GrammarCandidate,
                    PolicyBoundaryArms.ForcedDivergentNull => CortexPolicySelectionCauses.TrialOverride,
                    PolicyBoundaryArms.ReflexFrozenControl => CortexPolicySelectionCauses.ShadowCandidate,
                    _ => CortexPolicySelectionCauses.Launchpad,
                },
                arm.Arm == PolicyBoundaryArms.Baseline ? 0UL : 0xA001UL + (ulong)arm.Arm,
                arm.Arm == PolicyBoundaryArms.Baseline ? 0UL : 0xB001UL + (ulong)arm.Arm),
            Diverged = arm.Arm == PolicyBoundaryArms.ForcedDivergentNull,
        })];

    internal static PolicyBoundaryTrainingReceipt CreateDestinationHandshakeTraining(
        string parentRunID, string sourceChildID, string coldSeedDigest,
        string configReceiptDigest, string checkpointReceiptDigest, int trainingEndStep,
        PolicyBoundaryObligationID obligationID = default)
    {
        PolicyBoundaryHistoricalFixture historical = PolicyBoundaryHistoricalFixture.TreeEra;
        int[] horizons = [1, 2, 3];
        List<PolicyBoundaryArmReceipt> arms = [];
        foreach (int horizon in horizons)
        {
            arms.Add(new(PolicyBoundaryArms.Baseline, horizon, 1, 1, true, true, 1, 1, true));
            arms.Add(new(PolicyBoundaryArms.Candidate, horizon, 2, 1, true, true, 1, 1, true));
            arms.Add(new(PolicyBoundaryArms.ForcedDivergentNull, horizon, 1, 1, true, true, 1, 1, true));
            arms.Add(new(PolicyBoundaryArms.ReflexFrozenControl, horizon, 1, 1, true, true, 0, 0, false));
        }
        PolicyBoundaryForkReceipt authority = new(
            obligationID.Value.Length == 0 ? historical.CreateObligation().ID : obligationID,
            historical.BaselineBoundary,
            historical.CandidateBoundary,
            horizons,
            StampFixtureArms([.. arms], ReadoutFingerprint(1), 1), true, true, true, true, ReadoutFingerprint(1), 1)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(1),
            SourceDecisionCandidateFingerprint = FixtureCandidateFingerprint,
        };
        string authorityDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
        return PolicyBoundaryTrainingReceipt.CreateVerified(
            parentRunID, sourceChildID, coldSeedDigest, 0, trainingEndStep,
            configReceiptDigest, checkpointReceiptDigest, authorityDigest,
            authority.Obligation.Value, Homeostat.PolicyID.Value, authority.CandidateBoundary,
            authority.SourceDecisionReadoutFingerprint, authority.SourceDecisionReadoutRevision,
            in authority, historical.CriticalityMetricID.ToString(CultureInfo.InvariantCulture),
            PolicyBoundaryComparisons.LessThanOrEqual, HomeostatPolicyBoundaryDomain.Instance);
    }

    internal static PolicyBoundaryTrainingReceipt CreateFixtureTraining(
        string parentRunID, string sourceChildID, string coldSeedDigest,
        string configReceiptDigest, string checkpointReceiptDigest, int trainingEndStep)
    {
        int[] horizons = [16, 64, 256];
        List<PolicyBoundaryArmReceipt> authorityArms = [];
        foreach (int horizon in horizons)
            foreach (PolicyBoundaryArms arm in Enum.GetValues<PolicyBoundaryArms>())
                authorityArms.Add(new(arm, horizon, arm == PolicyBoundaryArms.Candidate ? 2 : 1, horizon, true, true,
                    arm == PolicyBoundaryArms.ReflexFrozenControl ? 0 : 1, 0, arm != PolicyBoundaryArms.ReflexFrozenControl));
        PolicyBoundaryForkReceipt authority = new(
            new PolicyBoundaryObligationID("fixture-obligation"), PolicyBoundaryRational.Parse("0"),
            PolicyBoundaryRational.Parse("1/2"), horizons, StampFixtureArms([.. authorityArms], ReadoutFingerprint(7), 7), true, true, true, true, ReadoutFingerprint(7), 7)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(1),
            SourceDecisionCandidateFingerprint = FixtureCandidateFingerprint,
        };
        string authorityDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
        return PolicyBoundaryTrainingReceipt.CreateVerified(
            parentRunID, sourceChildID, coldSeedDigest, 0, trainingEndStep,
            configReceiptDigest, checkpointReceiptDigest, authorityDigest,
            "fixture-obligation", "homeostat", PolicyBoundaryRational.Parse("1/2"),
            ReadoutFingerprint(7), 7, in authority, "fixture-feature", PolicyBoundaryComparisons.LessThanOrEqual,
            HomeostatPolicyBoundaryDomain.Instance);
    }

    internal static bool Verify(TextWriter output)
    {
        string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        int[] horizons = [16, 64, 256];
        List<PolicyBoundaryArmReceipt> authorityArms = [];
        foreach (int horizon in horizons)
            foreach (PolicyBoundaryArms arm in Enum.GetValues<PolicyBoundaryArms>())
                authorityArms.Add(new(arm, horizon, arm == PolicyBoundaryArms.Candidate ? 2 : 1, horizon, true, true, arm == PolicyBoundaryArms.ReflexFrozenControl ? 0 : 1, 0, arm != PolicyBoundaryArms.ReflexFrozenControl));
        PolicyBoundaryForkReceipt authority = new(new PolicyBoundaryObligationID("obligation"), PolicyBoundaryRational.Parse("0"), PolicyBoundaryRational.Parse("1/2"), horizons, StampFixtureArms([.. authorityArms], ReadoutFingerprint(7), 7), true, true, true, true, ReadoutFingerprint(7), 7)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(1),
            SourceDecisionCandidateFingerprint = FixtureCandidateFingerprint,
        };
        string authorityDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
        PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.CreateVerified(
            "parent", "calibration", Digest("cold"), 0, 1280,
            Digest("config"), Digest("checkpoint"), authorityDigest,
            "obligation", "homeostat", PolicyBoundaryRational.Parse("1/2"), ReadoutFingerprint(7), 7, in authority,
            "1", PolicyBoundaryComparisons.LessThanOrEqual, HomeostatPolicyBoundaryDomain.Instance);
        Cortex.PolicyBoundaryAuthorityReceipt sourceAuthority = new(
            Policy: "homeostat", Obligation: "obligation", Boundary: authority.CandidateBoundary.ToString(),
            ForkReceiptDigest: authorityDigest, DecisionReadoutFingerprint: ReadoutFingerprint(7),
            DecisionReadoutRevision: 7, Verified: true, ReceiptDigest: "",
            ForkReceipt: authority, Feature: "1", Comparison: PolicyBoundaryComparisons.LessThanOrEqual,
            ParentRunID: "parent", SourceChildID: "calibration", ColdSeedDigest: Digest("cold"),
            ConfigReceiptDigest: Digest("config"), CheckpointReceiptDigest: Digest("checkpoint"),
            DestinationDecisionReadoutFingerprint: 0, DestinationDecisionReadoutRevision: 0,
            TrainingParentRunID: "parent", TrainingSourceChildID: "calibration", MountDestinationChildID: "",
            TrainingContentDigest: "", MountReceiptDigest: "");
        sourceAuthority = sourceAuthority with { ReceiptDigest = sourceAuthority.ComputeDigest() };
        bool authorityDigestExact = sourceAuthority.ReceiptDigest == sourceAuthority.ComputeDigest();
        Cortex.PolicyBoundaryAuthorityReceipt tamperedAuthority = sourceAuthority with { DecisionReadoutFingerprint = 0x5678UL };
        bool authorityDigestRejectsTamper = Rejects(() => _ = PolicyBoundaryTrainingReceipt.CreateFromCalibration(0, 1280, in tamperedAuthority, HomeostatPolicyBoundaryDomain.Instance));
        bool fromCalibration = PolicyBoundaryTrainingReceipt.CreateFromCalibration(
            0, 1280, in sourceAuthority, HomeostatPolicyBoundaryDomain.Instance).IsVerified(HomeostatPolicyBoundaryDomain.Instance);
        byte[] bytes = PolicyBoundaryTrainingReceipt.Encode(in training, HomeostatPolicyBoundaryDomain.Instance);
        bool roundTrip = PolicyBoundaryTrainingReceipt.Decode(bytes, HomeostatPolicyBoundaryDomain.Instance).ReceiptDigest == training.ReceiptDigest;
        PolicyBoundaryTrainingReceipt legacyForkSchema = PolicyBoundaryTrainingReceipt.Decode(bytes, HomeostatPolicyBoundaryDomain.Instance);
        legacyForkSchema.forkAuthority!.schemaVersion = 2;
        bool rejectsLegacyForkSchema = Rejects(() => legacyForkSchema.Validate(HomeostatPolicyBoundaryDomain.Instance));
        PolicyBoundaryMountReceipt mount = PolicyBoundaryMountReceipt.CreateVerified(in training, "evaluation", 0, 499, 0, HomeostatPolicyBoundaryDomain.Instance);
        bool valid = mount.ValidateAndReturn(in training, "parent", "evaluation", training.ColdSeedDigest, HomeostatPolicyBoundaryDomain.Instance);
        bool rejectsTamper = Rejects(() => PolicyBoundaryTrainingReceipt.Decode(bytes[..^1], HomeostatPolicyBoundaryDomain.Instance));
        bool rejectsSameChild = Rejects(() => PolicyBoundaryMountReceipt.CreateVerified(in training, "calibration", 0, 499, 0, HomeostatPolicyBoundaryDomain.Instance));
        bool rejectsFields = true;
        foreach (Action<PolicyBoundaryTrainingReceipt> mutate in new Action<PolicyBoundaryTrainingReceipt>[] {
            value => value.boundary = "9/10", value => value.parentRunID = "other", value => value.sourceChildID = "other",
            value => value.coldSeedDigest = Digest("other"), value => value.configReceiptDigest = Digest("other"),
            value => value.checkpointReceiptDigest = Digest("other"), value => value.sourceDecisionReadoutFingerprint++,
            value => value.forkAuthority!.candidateBoundary = "9/10" })
        {
            PolicyBoundaryTrainingReceipt candidate = PolicyBoundaryTrainingReceipt.Decode(bytes, HomeostatPolicyBoundaryDomain.Instance);
            mutate(candidate);
            rejectsFields &= Rejects(() => candidate.Validate(HomeostatPolicyBoundaryDomain.Instance));
        }
        bool packetRecovery = TapePacketCreator.TryReadPolicyBoundaryTrainingMount(
            Encoding.ASCII.GetBytes($"POLICY-BOUNDARY-MOUNT\tparent={mount.ParentRunID}\tsource={mount.SourceChildID}\tdestination={mount.DestinationChildID}\tcold={mount.ColdSeedDigest}\ttraining={mount.TrainingReceiptDigest}\tcontent={mount.SourceContentDigest}\trelation={mount.Relation}\tevaluation={mount.EvaluationStartStep}..{mount.EvaluationEndStep}\tmount={mount.MountStep}\tdestination-fingerprint={mount.DestinationDecisionReadoutFingerprint:X16}\tdestination-revision={mount.DestinationDecisionReadoutRevision}\tverified=1\treceipt={mount.ReceiptDigest}"), in mount);
        PolicyBoundaryIdentity identity = new(new CortexPolicyID("homeostat"), "candidate", "grammar", "production", "1", "1");
        PolicyBoundaryObligation mounted = new(identity);
        PolicyBoundaryForkReceipt mountedAuthority = authority with { Obligation = identity.ObligationID };
        mounted.Propose(new PolicyBoundaryCandidate(mountedAuthority.CandidateBoundary, PolicyBoundaryComparisons.LessThanOrEqual, "fixture"));
        mounted.MountVerifiedTrainingReceipt(in mountedAuthority, PolicyBoundaryComparisons.LessThanOrEqual,
            HomeostatPolicyBoundaryDomain.Instance);
        using MemoryStream checkpoint = new();
        using (CkptWriter writer = new(checkpoint)) mounted.Save(writer);
        checkpoint.Position = 0;
        PolicyBoundaryObligation restored;
        using (CkptReader reader = new(checkpoint))
            restored = PolicyBoundaryObligation.Load(reader, static _ => HomeostatPolicyBoundaryDomain.Instance);
        using MemoryStream secondCheckpoint = new();
        using (CkptWriter writer = new(secondCheckpoint)) restored.Save(writer);
        byte[] firstCheckpoint = checkpoint.ToArray();
        bool saveLoadSave = firstCheckpoint.AsSpan().SequenceEqual(secondCheckpoint.ToArray())
            && restored.Receipt is PolicyBoundaryForkReceipt restoredAuthority
            && PolicyBoundaryObligation.ComputeReceiptDigest(in restoredAuthority) == PolicyBoundaryObligation.ComputeReceiptDigest(in mountedAuthority)
            && restored.Winner == mounted.Winner
            && string.Equals(restored.Winner?.Provenance, mounted.Winner?.Provenance, StringComparison.Ordinal)
            && restored.Winner?.Provenance.StartsWith("verified-training:", StringComparison.Ordinal) == true;
        bool ordinaryGuard = restored.Readout(0.25).CanActuate;
        bool cortexCheckpoint = VerifyCortexPolicyCheckpoint(identity, mountedAuthority, sourceAuthority, output);
        bool cortexCurrent = Checkpoint.MatchesCurrentSchema(Checkpoint.CurrentMagic)
            && !Checkpoint.MatchesCurrentSchema("CORTEXJ\n"u8);
        bool fullCheckpoint = VerifyFullCortexCheckpoint(authority, sourceAuthority, output);
        bool executionAdmission = VerifyExecutionAdmissionFixture(output);
        bool realTrialReceipt = VerifyRealTrialReceiptFixture(output);
        output.WriteLine($"  policy-boundary training/mount fixture · round-trip={(roundTrip ? "PASS" : "FAIL")} · valid={(valid ? "PASS" : "FAIL")} · corruption={(rejectsTamper && rejectsSameChild && rejectsFields && authorityDigestExact && authorityDigestRejectsTamper ? "PASS" : "FAIL")} · legacy-fork-schema={(rejectsLegacyForkSchema ? "rejected" : "ACCEPTED")} · packet={(packetRecovery ? "PASS" : "FAIL")} · obligation-save-load-save={(saveLoadSave ? "PASS" : "FAIL")} · ordinary-guard={(ordinaryGuard ? "PASS" : "FAIL")} · cortex-current-policy-checkpoint={(cortexCheckpoint && cortexCurrent && fullCheckpoint ? "PASS" : "FAIL")} · execution-admission={(executionAdmission ? "PASS" : "FAIL")} · real-trial-receipt={(realTrialReceipt ? "PASS" : "FAIL")}");
        return roundTrip && valid && fromCalibration && rejectsTamper && rejectsSameChild && rejectsFields && rejectsLegacyForkSchema && authorityDigestExact && authorityDigestRejectsTamper && packetRecovery && saveLoadSave && ordinaryGuard && cortexCheckpoint && cortexCurrent && fullCheckpoint && executionAdmission && realTrialReceipt;
    }

    private static bool VerifyExecutionAdmissionFixture(TextWriter output)
    {
        int[] horizons = [270, 286, 302];
        ulong fingerprint = ReadoutFingerprint(7);
        const ulong revision = 7;
        List<PolicyBoundaryArmReceipt> rows = [];
        foreach (int horizon in horizons)
            foreach (PolicyBoundaryArms arm in Enum.GetValues<PolicyBoundaryArms>())
                rows.Add(new(arm, horizon, arm == PolicyBoundaryArms.Candidate ? 2 : 1, 1, true, true,
                    arm == PolicyBoundaryArms.ReflexFrozenControl ? 0 : 1, 0, arm != PolicyBoundaryArms.ReflexFrozenControl));
        rows = [.. StampFixtureArms([.. rows], fingerprint, revision)];
        CortexPolicyDecisionReadout RequestReadout(PolicyBoundaryArms arm) => arm switch
        {
            PolicyBoundaryArms.Candidate => new(0, 1, 2, 2, CortexPolicyAuthorities.Grammar, new GrammarRevisionID(revision), CortexPolicySelectionCauses.GrammarCandidate, 0xA1, 0xB1),
            PolicyBoundaryArms.ForcedDivergentNull => new(0, 1, 2, 2, CortexPolicyAuthorities.Grammar, new GrammarRevisionID(revision), CortexPolicySelectionCauses.TrialOverride, 0xA2, 0xB2),
            PolicyBoundaryArms.ReflexFrozenControl => new(0, 1, 1, 0, CortexPolicyAuthorities.Shadow, new GrammarRevisionID(revision), CortexPolicySelectionCauses.ShadowCandidate, 0xA3, 0xB3),
            _ => new(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad, GrammarRevisionID.Zero, CortexPolicySelectionCauses.Launchpad),
        };
        for (int i = 0; i < rows.Count; i++)
        {
            PolicyBoundaryArmReceipt row = rows[i];
            bool denied = row.Horizon == horizons[0] && row.Arm is (PolicyBoundaryArms.Candidate or PolicyBoundaryArms.ForcedDivergentNull);
            row = row with
            {
                RequestCount = 1,
                GuardAdmittedCount = denied ? 0 : 1,
                LastRequestDecisionID = new CortexPolicyDecisionID((ulong)(row.Horizon * 10 + (int)row.Arm + 1000)),
                LastRequestStep = row.Horizon,
                LastRequestReadout = RequestReadout(row.Arm),
                ExecutionOutcome = denied ? CortexPolicyTrialExecutionOutcomes.GuardDenied : CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted,
                ExecutedDecisionID = denied ? default : row.ExecutedDecisionID,
                ExecutedStep = denied ? -1 : row.ExecutedStep,
                ExecutedLaunchpadAction = denied ? -1 : row.ExecutedLaunchpadAction,
                ExecutedRawCandidateAction = denied ? -1 : row.ExecutedRawCandidateAction,
                ExecutedSelectedCandidateAction = denied ? -1 : row.ExecutedSelectedCandidateAction,
                ExecutedAction = denied ? -1 : row.ExecutedAction,
                ExecutedAuthority = denied ? CortexPolicyAuthorities.Launchpad : row.ExecutedAuthority,
                ExecutedSelectionCause = denied ? CortexPolicySelectionCauses.Launchpad : row.ExecutedSelectionCause,
                ExecutedReadoutFingerprint = denied ? 0 : row.ExecutedReadoutFingerprint,
                ExecutedReadoutRevision = denied ? 0 : row.ExecutedReadoutRevision,
                ExecutedReadoutOccurrenceDigest = denied ? 0 : row.ExecutedReadoutOccurrenceDigest,
                ExecutedCandidateFingerprint = denied ? 0 : row.ExecutedCandidateFingerprint,
                ExecutedCanonicalState = denied ? default : row.ExecutedCanonicalState,
                ExecutedDecisionEventID = denied ? default : row.ExecutedDecisionEventID,
                ExecutedOutcomeEventID = denied ? default : row.ExecutedOutcomeEventID,
                ExecutedOutcomePayloadSHA256 = denied ? "" : row.ExecutedOutcomePayloadSHA256,
                ForcedDivergenceSeed = denied ? 0 : row.ForcedDivergenceSeed,
            };
            rows[i] = row;
        }
        PolicyBoundaryForkReceipt receipt = new(new PolicyBoundaryObligationID("run28-shaped"), PolicyBoundaryRational.Parse("0"), PolicyBoundaryRational.Parse("1/2"), horizons, [.. rows], true, true, true, true, fingerprint, revision)
        {
            QuotaDecisionID = new CortexPolicyQuotaDecisionID(28),
            SourceDecisionCandidateFingerprint = FixtureCandidateFingerprint,
        };
        bool valid = true;
        try { receipt.Validate(HomeostatPolicyBoundaryDomain.Instance); } catch (InvalidDataException) { valid = false; }
        PolicyBoundaryArmReceipt[] forgedRows = [.. receipt.Arms];
        forgedRows[1] = forgedRows[1] with { GuardAdmittedCount = 1 };
        bool rejectsForgedAdmission = Rejects(() => (receipt with { Arms = forgedRows }).Validate(HomeostatPolicyBoundaryDomain.Instance));
        PolicyBoundaryArmReceipt[] missingRows = [.. receipt.Arms];
        missingRows[^3] = missingRows[^3] with { ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.GuardDenied, GuardAdmittedCount = 0, ExecutedDecisionID = default, ExecutedStep = -1, ExecutedReadoutFingerprint = 0, ExecutedReadoutRevision = 0 };
        bool rejectsMissingTerminal = Rejects(() => (receipt with { Arms = missingRows }).Validate(HomeostatPolicyBoundaryDomain.Instance));
        bool passed = valid && rejectsForgedAdmission && rejectsMissingTerminal;
        output.WriteLine($"  policy-boundary execution-admission fixture · first-rung-guard-denied={(valid ? "accepted" : "BROKEN")} · forged-admission={(rejectsForgedAdmission ? "rejected" : "ACCEPTED")} · terminal-witness={(rejectsMissingTerminal ? "required" : "MISSING")}");
        return passed;
    }

    private static bool VerifyRealTrialReceiptFixture(TextWriter output)
    {
        CortexPolicyID policyID = new("policy-boundary.real-trial");
        CortexPolicySchema schema = new(policyID, featureCount: 1, actionCount: 3, outcomeCount: 1,
            authorityCeiling: CortexPolicyModes.Autonomic, admission: CortexPolicyAdmissionKinds.Verified);
        CortexConfig config = new()
        {
            Tools = [],
            ActionPolicies = [],
            Rewards = [],
            Learning = new CortexLearningConfig
            {
                Policies = new CortexPolicyLearningConfig
                {
                    DefaultMode = CortexPolicyModes.Autonomic,
                    AuthorityCeiling = CortexPolicyAuthorities.Grammar,
                    ReadoutDeliberationQuota = 0,
                },
            },
        };
        Cortex cortex = new(config);
        cortex.RegisterPolicy(schema);
        cortex.RegisterPolicyBoundaryDomain(new HomeostatTrainingPolicyBoundaryDomain(schema));
        MetricSample[] features = [new(new MetricID(1), NumericValue.FromF64(0.25))];
        using Tape tape = new();
        Journal journal = new();
        int step = 0;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, policyID, 0, features, schema.ActionCount);
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, policyID, 1, features, schema.ActionCount);
            TapePacketCreator.AppendPolicyExample(tape, journal, step++, policyID, 2, features, schema.ActionCount);
        }
        global::Cogito.Induct.RePairResult grammar = Engine.Induce(tape, 1).Result;
        InstallRevision publication = InstallRevision.FromRePair(new GrammarRevisionID(1), GrammarRevisionID.Zero, in grammar);
        cortex.SwapGrammar(in publication, advancePolicies: false);
        _ = cortex.ChoosePolicyAction(policyID, 0, features);
        cortex.BindRuntimeStep(1, in grammar);
        _ = cortex.ChoosePolicyAction(policyID, 0, features);
        if (!cortex.TryReadPolicyReadout(policyID, out CortexPolicyReadoutReceipt readout))
        {
            output.WriteLine("  policy-boundary real-trial-receipt fixture · readout=MISSING");
            return false;
        }
        PolicyBoundaryIdentity identity = new(policyID, "fixture-schema", "fixture-grammar", "fixture-production", "1", "fixture");
        cortex.RegisterPolicyBoundaryObligation(new PolicyBoundaryObligation(identity));
        cortex.DisableAutonomicSpawning();
        cortex.SetPolicyTrialAuthority(policyID, CortexPolicyTrialAuthorityIdentity.FromReadout(in readout), CortexPolicyAuthorities.Grammar);
        CortexPolicyDecision deniedDecision = cortex.ChoosePolicyAction(policyID, 0, features);
        bool receiptRead = cortex.TryReadPolicyTrialExecutionReceipt(policyID,
            out CortexPolicyTrialExecutionOutcomes outcome,
            out long requestCount, out long guardAdmittedCount,
            out CortexPolicyDecisionReadout lastRequestReadout,
            out CortexPolicyDecisionID lastRequestDecisionID, out int lastRequestStep,
            out CortexPolicyDecisionReadout executionReadout,
            out CortexPolicyDecisionID executionDecisionID, out ulong executionFingerprint, out int executionStep);
        bool lastRequestCarried = receiptRead
            && outcome == CortexPolicyTrialExecutionOutcomes.GuardDenied
            && requestCount == 1 && guardAdmittedCount == 0
            && lastRequestDecisionID.Equals(deniedDecision.DecisionID)
            && lastRequestStep >= 0
            && lastRequestReadout == deniedDecision.Readout;
        bool executedSurfaceZero = receiptRead
            && executionDecisionID.Value == 0 && executionStep == -1 && executionFingerprint == 0
            && executionReadout.LaunchpadAction == -1
            && executionReadout.RawCandidateAction == -1
            && executionReadout.SelectedCandidateAction == -1
            && executionReadout.ExecutedAction == -1
            && executionReadout.Authority == CortexPolicyAuthorities.Launchpad
            && executionReadout.GrammarRevision == GrammarRevisionID.Zero
            && executionReadout.SelectionCause == CortexPolicySelectionCauses.Launchpad
            && executionReadout.ReadoutCandidateOccurrenceDigest == 0
            && executionReadout.ReadoutCandidateFingerprint == 0;
        PolicyBoundaryTrialOutcome mapped = new(0, 1, true)
        {
            ExecutionOutcome = outcome,
            RequestCount = requestCount,
            GuardAdmittedCount = guardAdmittedCount,
            LastRequestDecisionID = lastRequestDecisionID,
            LastRequestStep = lastRequestStep,
            LastRequestReadout = lastRequestReadout,
            ExecutedDecisionID = executionDecisionID,
            ExecutedStep = executionStep,
            ExecutedLaunchpadAction = executionReadout.LaunchpadAction,
            ExecutedRawCandidateAction = executionReadout.RawCandidateAction,
            ExecutedSelectedCandidateAction = executionReadout.SelectedCandidateAction,
            ExecutedAction = executionReadout.ExecutedAction,
            ExecutedAuthority = executionReadout.Authority,
            ExecutedSelectionCause = executionReadout.SelectionCause,
            ExecutedReadoutFingerprint = executionFingerprint,
            ExecutedReadoutRevision = executionReadout.GrammarRevision.Value,
            ExecutedReadoutOccurrenceDigest = executionReadout.ReadoutCandidateOccurrenceDigest,
            ExecutedCandidateFingerprint = executionReadout.ReadoutCandidateFingerprint,
        };
        bool mappedStrict = true;
        try { mapped.Validate(HomeostatPolicyBoundaryDomain.Instance); mapped.ValidateExecutionIdentity(HomeostatPolicyBoundaryDomain.Instance); }
        catch (InvalidDataException) { mappedStrict = false; }
        bool passed = lastRequestCarried && executedSurfaceZero && mappedStrict;
        output.WriteLine($"  policy-boundary real-trial-receipt fixture · last-request={(lastRequestCarried ? "carried" : "LOST")} · executed-surface={(executedSurfaceZero ? "zero" : "STALE")} · strict={(mappedStrict ? "accepted" : "REJECTED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool VerifyFullCortexCheckpoint(
        PolicyBoundaryForkReceipt sourceReceipt,
        Cortex.PolicyBoundaryAuthorityReceipt sourceAuthority,
        TextWriter output)
    {
        string token = Guid.NewGuid().ToString("N");
        string corpusPath = Path.GetFullPath(Path.Combine(".tmp", $"policy-boundary-checkpoint-{token}.txt"));
        string? runDirectory = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(corpusPath)!);
            File.WriteAllText(corpusPath, "alpha beta gamma\nalpha beta delta\n");
            CortexConfig config = new()
            {
                RunName = $"policy-boundary-checkpoint-{token}", Steps = 1, Seed = 0xC0117011UL,
                Curriculum = new CortexFlatPoolCurriculum
                {
                    Corpus = new CogitoCorpus { Path = corpusPath, Glob = "*.txt" }, IntakeBatch = 1, SeedSpans = 1, MixEvery = 1,
                },
                Learning = new CortexLearningConfig { Homeostat = new CortexHomeostatConfig { Autonomy = HomeostatAutonomyModes.Full }, Policies = new CortexPolicyLearningConfig { DefaultMode = CortexPolicyModes.Autonomic } },
            };
            PolicyBoundaryIdentity identity = PolicyBoundaryHistoricalFixture.TreeEra.CreateObligation().Identity;
            PolicyBoundaryForkReceipt authority = sourceReceipt with { Obligation = identity.ObligationID };
            string forkDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
            Cortex.PolicyBoundaryAuthorityReceipt authoritySource = sourceAuthority with
            {
                Policy = identity.Policy.Value, Obligation = identity.ObligationID.Value, Boundary = authority.CandidateBoundary.ToString(),
                ForkReceiptDigest = forkDigest, ForkReceipt = authority, Feature = identity.Feature,
                ParentRunID = "parent", SourceChildID = "calibration", ColdSeedDigest = sourceAuthority.ColdSeedDigest,
                ConfigReceiptDigest = sourceAuthority.ConfigReceiptDigest, CheckpointReceiptDigest = sourceAuthority.CheckpointReceiptDigest,
                ReceiptDigest = "",
            };
            authoritySource = authoritySource with { ReceiptDigest = authoritySource.ComputeDigest() };
            PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.CreateFromCalibration(0, 1, in authoritySource, HomeostatPolicyBoundaryDomain.Instance);
            Cortex? loaded = null;
            Cortex runtime = new(config);
            int result = runtime.Run((bound, _) =>
            {
                if (!bound.TryGetPolicyBoundaryObligation(Homeostat.PolicyID, out PolicyBoundaryObligation liveObligation))
                    throw new InvalidDataException("live Cortex did not register its Homeostat boundary obligation");
                PolicyBoundaryForkReceipt liveAuthority = sourceReceipt with { Obligation = liveObligation.ID };
                string liveForkDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in liveAuthority);
                Cortex.PolicyBoundaryAuthorityReceipt liveSourceAuthority = sourceAuthority with
                {
                    Policy = Homeostat.PolicyID.Value,
                    Obligation = liveObligation.ID.Value,
                    Boundary = liveAuthority.CandidateBoundary.ToString(),
                    ForkReceiptDigest = liveForkDigest,
                    ForkReceipt = liveAuthority,
                    ReceiptDigest = "",
                };
                liveSourceAuthority = liveSourceAuthority with { ReceiptDigest = liveSourceAuthority.ComputeDigest() };
                PolicyBoundaryTrainingReceipt liveTraining = PolicyBoundaryTrainingReceipt.CreateFromCalibration(0, 1, in liveSourceAuthority, HomeostatPolicyBoundaryDomain.Instance);
                string destination = Path.GetFileName(Path.GetFullPath(bound.CurrentRun.Dir));
                PolicyBoundaryMountReceipt mount = PolicyBoundaryMountReceipt.CreateVerified(in liveTraining, destination, 0, 1, 0, HomeostatPolicyBoundaryDomain.Instance).WithDestinationReadout(0xBEEFUL, 11);
                bound.RestorePolicyBoundaryLineage(in liveTraining, in mount);
                runDirectory = bound.CurrentRun.Dir;
            });
            if (result != 0 || runDirectory is null) return false;
            bool exact = Cortex.VerifyCheckpointRoundTrip(runDirectory, out string diskDigest, out string encodedDigest, restored =>
            {
                loaded = restored;
                MetricSample[] features = [new(new MetricID(418), NumericValue.FromF64(0.0))];
                if (!restored.PolicyBoundaryAllowsProduction(Homeostat.PolicyID, features))
                    throw new InvalidDataException("full checkpoint restore lost policy boundary production guard");
                if (!restored.TryReadPolicyBoundaryReadout(Homeostat.PolicyID, features, out PolicyBoundaryReadout readout) || !readout.CanActuate)
                    throw new InvalidDataException("full checkpoint restore lost policy boundary readout");
            });
            output.WriteLine($"  policy-boundary {Checkpoint.CurrentDialect} full checkpoint · encode-load-encode={(exact ? "exact" : "BROKEN")} disk={diskDigest[..8]} encoded={encodedDigest[..8]} loaded={(loaded is not null ? "yes" : "no")}");
            return exact && loaded is not null;
        }
        finally
        {
            if (runDirectory is not null && Directory.Exists(runDirectory)) Directory.Delete(runDirectory, recursive: true);
            if (File.Exists(corpusPath)) File.Delete(corpusPath);
        }
    }

    private static bool VerifyCortexPolicyCheckpoint(
        PolicyBoundaryIdentity identity,
        PolicyBoundaryForkReceipt authority,
        Cortex.PolicyBoundaryAuthorityReceipt sourceAuthority,
        TextWriter output)
    {
        string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in authority);
        Cortex.PolicyBoundaryAuthorityReceipt boundAuthority = sourceAuthority with
        {
            Obligation = identity.ObligationID.Value,
            Boundary = authority.CandidateBoundary.ToString(),
            ForkReceipt = authority,
            ForkReceiptDigest = digest,
            ReceiptDigest = "",
        };
        boundAuthority = boundAuthority with { ReceiptDigest = boundAuthority.ComputeDigest() };
        PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.CreateFromCalibration(0, 1280, in boundAuthority, HomeostatPolicyBoundaryDomain.Instance);
        PolicyBoundaryMountReceipt mount = PolicyBoundaryMountReceipt.CreateVerified(in training, "evaluation", 0, 499, 0, HomeostatPolicyBoundaryDomain.Instance).WithDestinationReadout(0xBEEFUL, 11);
        PolicyBoundaryObligation obligation = new(identity);
        obligation.MountVerifiedTrainingReceipt(in authority, PolicyBoundaryComparisons.LessThanOrEqual,
            HomeostatPolicyBoundaryDomain.Instance);
        CortexConfig config = new()
        {
            Tools = [], ActionPolicies = [], Rewards = [],
            Learning = new CortexLearningConfig { Policies = new CortexPolicyLearningConfig { DefaultMode = CortexPolicyModes.Autonomic, ShadowDecisions = 1, ReadoutDeliberationQuota = 0 } },
        };
        CortexPolicySchema schema = new(identity.Policy, 1, 2, 1);
        Cortex source = new(config);
        source.RegisterPolicy(schema);
        source.RegisterPolicyBoundaryDomain(new HomeostatTrainingPolicyBoundaryDomain(schema));
        source.RegisterPolicyBoundaryObligation(obligation);
        source.RestorePolicyBoundaryLineage(in training, in mount);
        using MemoryStream first = new();
        using (CkptWriter writer = new(first)) source.SavePolicyState(writer);
        Cortex restored = new(config);
        restored.RegisterPolicy(schema);
        restored.RegisterPolicyBoundaryDomain(new HomeostatTrainingPolicyBoundaryDomain(schema));
        first.Position = 0;
        using (CkptReader reader = new(first)) restored.LoadPolicyState(reader);
        using MemoryStream second = new();
        using (CkptWriter writer = new(second)) restored.SavePolicyState(writer);
        MetricSample[] features = [new(new MetricID(1), NumericValue.FromF64(0.25))];
        bool ordinary = restored.PolicyBoundaryAllowsProduction(identity.Policy, features)
            && restored.TryReadPolicyBoundaryReadout(identity.Policy, features, out PolicyBoundaryReadout readout)
            && readout.CanActuate;
        bool exact = first.ToArray().AsSpan().SequenceEqual(second.ToArray());
        bool malformedV14Rejected = false;
        byte[] malformedImage = first.ToArray();
        ReadOnlySpan<byte> policyBoundaryTag = stackalloc byte[] { 0x4C, 0x42, 0x4F, 0x42 };
        int policyBoundaryOffset = malformedImage.AsSpan().IndexOf(policyBoundaryTag);
        if (policyBoundaryOffset >= 0 && policyBoundaryOffset + 8 <= malformedImage.Length)
        {
            malformedImage[policyBoundaryOffset + 4] = 14;
            malformedImage[policyBoundaryOffset + 5] = 0;
            malformedImage[policyBoundaryOffset + 6] = 0;
            malformedImage[policyBoundaryOffset + 7] = 0;
            try
            {
                using MemoryStream malformed = new(malformedImage);
                using CkptReader reader = new(malformed);
                restored.LoadPolicyState(reader);
            }
            catch (InvalidDataException) { malformedV14Rejected = true; }
        }
        output.WriteLine($"  policy-boundary {Checkpoint.CurrentDialect} policy section · lineage={(exact ? "exact" : "BROKEN")} destination={mount.DestinationDecisionReadoutFingerprint:X16}/{mount.DestinationDecisionReadoutRevision} ordinary={(ordinary ? "PASS" : "FAIL")} malformed-v14={(malformedV14Rejected ? "rejected" : "ACCEPTED")}");
        return exact && ordinary && malformedV14Rejected;
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }
}

internal static class PolicyBoundaryMountReceiptExtensions
{
    internal static bool ValidateAndReturn(this PolicyBoundaryMountReceipt receipt,
        in PolicyBoundaryTrainingReceipt training, string parentID, string destinationID, string coldSeedDigest,
        IPolicyBoundaryDomain domain)
    {
        receipt.Validate(in training, parentID, destinationID, coldSeedDigest, domain);
        return true;
    }
}
